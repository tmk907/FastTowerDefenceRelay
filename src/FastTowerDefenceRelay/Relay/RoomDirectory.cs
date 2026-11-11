using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Transport;

namespace FastTowerDefenceRelay.Relay;

/// <summary>A listed room with occupancy that only the relay itself tracks.</summary>
/// <param name="PlayerCount">The host plus every guest admitted to the room.</param>
/// <param name="PlayerCapacity">The host plus every guest seat the host has reserved.</param>
internal sealed record RoomStatus(RoomSummary Summary, int PlayerCount, int PlayerCapacity);

/// <summary>A host seat and the guest that just took it; both are ready for forwarding.</summary>
internal sealed record RelayPair(ulong RoomId, ulong Slot, FrameConnection Host, FrameConnection Guest);

/// <summary>
/// The rooms created by connected hosts, each with its waiting host connections.
/// </summary>
/// <remarks>
/// Responses to a room's host connections are sent while holding the directory
/// lock, so a host never receives a heartbeat or a join after its own seat was
/// handed to a guest, nor a join before its room creation was confirmed.
/// </remarks>
internal sealed class RoomDirectory(RelayOptions options, ILogger<RoomDirectory> logger) : IDisposable
{
    /// <summary>Longest wait for a rejected peer to close its side.</summary>
    private static readonly TimeSpan RejectionLinger = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SortedDictionary<ulong, Room> rooms = [];
    private ulong nextRoomId = 1;

    public ProtocolVersion Protocol => options.Protocol;

    public bool IsAuthorized(RelayToken token) =>
        !options.RequiresToken || options.PlayerTokens.Any(token.Equals);

    /// <summary>Advertise a new room whose first host seat is the requesting connection.</summary>
    /// <returns>Whether the directory took ownership of the connection.</returns>
    public async Task<bool> CreateRoomAsync(FrameConnection connection, CreateRoomRequest request,
                                            CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized(request.Token))
        {
            await this.RejectAsync(connection, Rejections.Unauthorized, cancellationToken);
            return false;
        }

        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (this.rooms.Count < options.MaxRooms)
            {
                var roomId = this.AllocateRoomId();
                var room = new Room(new RoomSummary(roomId, request.RoomName, request.HostName), request.Token);
                room.HostSlots.Add(Wire.FirstGuestSlot, connection);
                this.rooms.Add(roomId, room);
                try
                {
                    await connection.SendAsync(this.Frame(MessageType.RelayRoomCreated, DirectoryMessages.EncodeRoomCreated(roomId)), cancellationToken);
                }
                catch (Exception exception) when (ConnectionFailure.Is(exception))
                {
                    this.rooms.Remove(roomId);
                    return false;
                }
                Log.RoomCreated(logger, roomId);
                return true;
            }
        }
        finally
        {
            this.gate.Release();
        }

        await this.RejectAsync(connection, Rejections.DirectoryFull, cancellationToken);
        return false;
    }

    /// <summary>Reserve one more host seat in a room created with the same token.</summary>
    /// <returns>Whether the directory took ownership of the connection.</returns>
    public async Task<bool> AttachRoomAsync(FrameConnection connection, ushort requestMinor, RoomRequest request,
                                            CancellationToken cancellationToken)
    {
        if (requestMinor < Wire.MultiGuestMinor)
        {
            await this.RejectAsync(connection, Rejections.AttachNeedsMultiGuest, cancellationToken);
            return false;
        }

        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (this.rooms.TryGetValue(request.RoomId, out var room)
                && this.IsAuthorized(request.Token)
                && room.HostToken.Equals(request.Token)
                && !room.HostToken.IsZero
                && room.NextHostSlot <= Wire.MaxPlayers)
            {
                var slot = room.NextHostSlot++;
                room.HostSlots.Add(slot, connection);
                try
                {
                    await connection.SendAsync(this.Frame(MessageType.RelayRoomAttached, DirectoryMessages.EncodeSlot(request.RoomId, slot)), cancellationToken);
                }
                catch (Exception exception) when (ConnectionFailure.Is(exception))
                {
                    room.HostSlots.Remove(slot);
                    return false;
                }
                Log.HostSlotReserved(logger, request.RoomId, slot);
                return true;
            }
        }
        finally
        {
            this.gate.Release();
        }

        await this.RejectAsync(connection, Rejections.InvalidAttachment, cancellationToken);
        return false;
    }

    /// <summary>Hand the lowest free host seat of a room to a guest.</summary>
    /// <returns>The confirmed pair, or null after a rejection.</returns>
    public async Task<RelayPair?> JoinRoomAsync(FrameConnection guest, RoomRequest request,
                                                CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized(request.Token))
        {
            await this.RejectAsync(guest, Rejections.Unauthorized, cancellationToken);
            return null;
        }

        RelayPair? pair = null;
        string? rejection = null;
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            if (!this.rooms.TryGetValue(request.RoomId, out var room))
            {
                rejection = Rejections.RoomUnavailable;
            }
            else if (options.RequiresToken && room.HostToken.Equals(request.Token))
            {
                rejection = Rejections.SameTokenAsHost;
            }
            else if (options.RequiresToken && room.AdmittedGuests.Any(request.Token.Equals))
            {
                rejection = Rejections.TokenAlreadyAdmitted;
            }
            else
            {
                var slots = room.HostSlots;
                while (slots.Count > 0 && !slots.First().Value.PeerConnected())
                {
                    var dead = slots.First();
                    dead.Value.Close();
                    slots.Remove(dead.Key);
                }

                if (slots.Count == 0)
                {
                    this.rooms.Remove(request.RoomId);
                    rejection = Rejections.HostGone;
                }
                else
                {
                    var (slot, host) = slots.First();
                    slots.Remove(slot);
                    room.AdmittedGuests.Add(request.Token);
                    if (slots.Count == 0)
                    {
                        this.rooms.Remove(request.RoomId);
                    }
                    pair = new RelayPair(request.RoomId, slot, host, guest);
                }
            }
        }
        finally
        {
            this.gate.Release();
        }

        if (pair is null)
        {
            await this.RejectAsync(guest, rejection!, cancellationToken);
            return null;
        }

        // The seat has left the directory, so no heartbeat can follow this frame.
        var ready = this.Frame(MessageType.RelayReady, DirectoryMessages.EncodeSlot(pair.RoomId, pair.Slot));
        try
        {
            await pair.Host.SendAsync(ready, cancellationToken);
            await pair.Guest.SendAsync(ready, cancellationToken);
        }
        catch (Exception exception) when (ConnectionFailure.Is(exception))
        {
            pair.Host.Close();
            await this.RejectAsync(guest, Rejections.HostLeftDuringJoin, cancellationToken);
            return null;
        }
        Log.PairConnected(logger, pair.RoomId, pair.Slot);
        return pair;
    }

    /// <summary>
    /// The live rooms with their occupancy, ordered by room ID.
    /// </summary>
    /// <remarks>
    /// Sends a heartbeat to every waiting host seat and drops seats whose host
    /// has disconnected, and rooms without seats left.
    /// </remarks>
    public async Task<IReadOnlyList<RoomStatus>> StatusAsync(CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            var heartbeat = this.Frame(MessageType.Ping, []);
            var result = new List<RoomStatus>(this.rooms.Count);
            foreach (var (roomId, room) in this.rooms.ToList())
            {
                foreach (var (slot, host) in room.HostSlots.ToList())
                {
                    var alive = host.PeerConnected();
                    if (alive)
                    {
                        try
                        {
                            await host.SendAsync(heartbeat, cancellationToken);
                        }
                        catch (Exception exception) when (ConnectionFailure.Is(exception))
                        {
                            alive = false;
                        }
                    }
                    if (!alive)
                    {
                        host.Close();
                        room.HostSlots.Remove(slot);
                    }
                }

                if (room.HostSlots.Count == 0)
                {
                    this.rooms.Remove(roomId);
                }
                else
                {
                    // Host seats are numbered from 2 and reserved contiguously.
                    result.Add(new RoomStatus(room.Summary, 1 + room.AdmittedGuests.Count, (int)room.NextHostSlot - 1));
                }
            }
            return result;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <summary>Close and remove every waiting room.</summary>
    public async Task CancelAllAsync()
    {
        await this.gate.WaitAsync();
        try
        {
            foreach (var room in this.rooms.Values)
            {
                foreach (var host in room.HostSlots.Values)
                {
                    host.Close();
                }
            }
            this.rooms.Clear();
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <summary>Send a rejection and close the connection, ignoring a peer that already left.</summary>
    /// <remarks>
    /// Waits for the peer to read the rejection, so it must not be called
    /// while holding the directory lock.
    /// </remarks>
    public async Task RejectAsync(FrameConnection connection, string reason, CancellationToken cancellationToken)
    {
        Log.Rejected(logger, connection.RemoteEndPoint, reason);
        try
        {
            await connection.SendAsync(this.Frame(MessageType.RelayReject, DirectoryMessages.EncodeReject(reason)), cancellationToken);
        }
        catch (Exception exception) when (ConnectionFailure.Is(exception))
        {
            connection.Close();
            return;
        }
        await connection.DrainAndCloseAsync(RejectionLinger);
    }

    public byte[] Frame(MessageType type, ReadOnlySpan<byte> payload) =>
        FrameHeader.Encode(options.Protocol, type, payload);

    public void Dispose() => this.gate.Dispose();

    private ulong AllocateRoomId()
    {
        for (var attempt = 0; attempt <= this.rooms.Count; ++attempt)
        {
            var candidate = this.nextRoomId++;
            if (this.nextRoomId == 0)
            {
                this.nextRoomId = 1;
            }
            if (candidate != 0 && !this.rooms.ContainsKey(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("The room ID space is exhausted");
    }

    private sealed class Room(RoomSummary summary, RelayToken hostToken)
    {
        public RoomSummary Summary { get; } = summary;

        public RelayToken HostToken { get; } = hostToken;

        /// <summary>Waiting host connections by the slot a guest receives with them.</summary>
        public SortedDictionary<ulong, FrameConnection> HostSlots { get; } = [];

        public List<RelayToken> AdmittedGuests { get; } = [];

        public ulong NextHostSlot { get; set; } = Wire.FirstGuestSlot + 1;
    }
}
