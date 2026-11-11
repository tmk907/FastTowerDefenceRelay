using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Transport;

namespace FastTowerDefenceRelay.Relay;

/// <summary>
/// Accepts relay connections, answers one directory request on each and
/// forwards every completed host/guest pair.
/// </summary>
/// <remarks>
/// Each connection still processing its request and each forwarded pair
/// counts once against the connection limit; waiting host seats do not.
/// </remarks>
internal sealed class RelayServer(RelayOptions options, RoomDirectory directory, ILogger<RelayServer> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<Task, byte> handlers = new();
    private TcpListener? listener;
    private int activeConnections;

    /// <summary>The bound relay endpoint, available once the host has started.</summary>
    public IPEndPoint EndPoint =>
        (IPEndPoint)(this.listener ?? throw new InvalidOperationException("The relay has not started")).LocalEndpoint;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Bind before the host reports itself started, so the port is known and in use.
        var address = ListenAddress.Resolve(options.Listen);
        this.listener = new TcpListener(address, options.Port);
        if (address.Equals(IPAddress.IPv6Any))
        {
            this.listener.Server.DualMode = true;
        }
        this.listener.Start();
        Log.Listening(logger, this.EndPoint, options.RequiresToken ? "player-token" : "public", options.Protocol);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = this.listener!;
        try
        {
            while (true)
            {
                Socket socket;
                try
                {
                    socket = await listener.AcceptSocketAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var connection = new FrameConnection(socket, options.SendTimeout);
                if (Interlocked.Increment(ref this.activeConnections) > options.MaxConnections)
                {
                    Interlocked.Decrement(ref this.activeConnections);
                    // Rejecting waits for the peer, which must not stall accepting others.
                    this.Track(directory.RejectAsync(connection, Rejections.CapacityFull, stoppingToken));
                    continue;
                }

                this.Track(Task.Run(() => this.HandleAsync(connection, stoppingToken), CancellationToken.None));
            }
        }
        finally
        {
            listener.Stop();
            await directory.CancelAllAsync();
            await Task.WhenAll(this.handlers.Keys);
        }
    }

    /// <summary>Keep a connection task until it finishes, so shutdown can wait for it.</summary>
    private void Track(Task task)
    {
        this.handlers.TryAdd(task, 0);
        _ = task.ContinueWith(finished => this.handlers.TryRemove(finished, out _), TaskScheduler.Default);
    }

    private async Task HandleAsync(FrameConnection connection, CancellationToken stoppingToken)
    {
        var retained = false;
        try
        {
            var request = await this.ReceiveRequestAsync(connection, stoppingToken);
            if (request is not null)
            {
                retained = await this.DispatchAsync(connection, request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The relay is shutting down.
        }
        catch (Exception exception) when (ConnectionFailure.Is(exception))
        {
            Log.ConnectionError(logger, connection.RemoteEndPoint, exception);
        }
        finally
        {
            if (!retained)
            {
                connection.Close();
            }
            Interlocked.Decrement(ref this.activeConnections);
        }
    }

    /// <returns>A request in a valid directory envelope, or null after a rejection or disconnect.</returns>
    private async Task<ReceivedFrame?> ReceiveRequestAsync(FrameConnection connection, CancellationToken stoppingToken)
    {
        ReceivedFrame? request;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            timeout.CancelAfter(options.RequestTimeout);
            try
            {
                request = await connection.ReceiveAsync(timeout.Token);
            }
            catch (ProtocolException)
            {
                await this.RejectAsync(connection, Rejections.MalformedRequest, stoppingToken);
                return null;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                await this.RejectAsync(connection, Rejections.RequestTimedOut, stoppingToken);
                return null;
            }
        }

        if (request is null)
        {
            return null;
        }

        var header = request.Header;
        if (header.Version.Major != options.Protocol.Major
            || header.Version.Minor < Wire.DirectoryMinor
            || header.Version.Minor > options.Protocol.Minor
            || header.SessionId != 0
            || header.Sequence != 0)
        {
            await this.RejectAsync(connection, Rejections.MalformedRequest, stoppingToken);
            return null;
        }
        return request;
    }

    /// <returns>Whether the directory took ownership of the connection.</returns>
    private async Task<bool> DispatchAsync(FrameConnection connection, ReceivedFrame request, CancellationToken stoppingToken)
    {
        switch ((MessageType)request.Header.MessageType)
        {
            case MessageType.RelayListRooms:
                {
                    RelayToken token;
                    try
                    {
                        token = DirectoryMessages.DecodeTokenRequest(request.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await this.RejectAsync(connection, Rejections.MalformedListing, stoppingToken);
                        return false;
                    }
                    if (!directory.IsAuthorized(token))
                    {
                        await this.RejectAsync(connection, Rejections.Unauthorized, stoppingToken);
                        return false;
                    }

                    var rooms = await directory.StatusAsync(stoppingToken);
                    var list = DirectoryMessages.EncodeRoomList(rooms.Select(room => room.Summary).ToList());
                    await connection.SendAsync(directory.Frame(MessageType.RelayRoomList, list), stoppingToken);
                    return false;
                }

            case MessageType.RelayCreateRoom:
                {
                    CreateRoomRequest create;
                    try
                    {
                        create = DirectoryMessages.DecodeCreateRoom(request.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await this.RejectAsync(connection, Rejections.MalformedCreation, stoppingToken);
                        return false;
                    }
                    return await directory.CreateRoomAsync(connection, create, stoppingToken);
                }

            case MessageType.RelayAttachRoom:
                {
                    RoomRequest attach;
                    try
                    {
                        attach = DirectoryMessages.DecodeRoomRequest(request.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await this.RejectAsync(connection, Rejections.MalformedAttachment, stoppingToken);
                        return false;
                    }
                    return await directory.AttachRoomAsync(connection, request.Header.Version.Minor, attach, stoppingToken);
                }

            case MessageType.RelayJoinRoom:
                {
                    RoomRequest join;
                    try
                    {
                        join = DirectoryMessages.DecodeRoomRequest(request.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await this.RejectAsync(connection, Rejections.MalformedJoin, stoppingToken);
                        return false;
                    }
                    if (await directory.JoinRoomAsync(connection, join, stoppingToken) is { } pair)
                    {
                        await this.ForwardAsync(pair, stoppingToken);
                    }
                    return false;
                }

            default:
                await this.RejectAsync(connection, Rejections.Unsupported, stoppingToken);
                return false;
        }
    }

    /// <summary>Forward complete frames in both directions until either peer disconnects.</summary>
    private async Task ForwardAsync(RelayPair pair, CancellationToken stoppingToken)
    {
        Log.ForwardingStarted(logger, pair.RoomId, pair.Slot);
        var stopped = 0;
        Exception? failure = null;

        void Stop(Exception? exception)
        {
            // Only the first stop is meaningful: closing makes the other direction fail too.
            if (Interlocked.Exchange(ref stopped, 1) == 0)
            {
                failure = exception;
            }
            pair.Host.Close();
            pair.Guest.Close();
        }

        async Task PumpAsync(FrameConnection source, FrameConnection destination)
        {
            try
            {
                while (await source.ReceiveAsync(stoppingToken) is { } frame)
                {
                    await destination.SendAsync(frame.Bytes, stoppingToken);
                }
                Stop(null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Stop(null);
            }
            catch (Exception exception) when (ConnectionFailure.Is(exception) || exception is ProtocolException)
            {
                Stop(exception);
            }
        }

        await Task.WhenAll(PumpAsync(pair.Host, pair.Guest), PumpAsync(pair.Guest, pair.Host));
        if (failure is null)
        {
            Log.ForwardingEnded(logger, pair.RoomId, pair.Slot);
        }
        else
        {
            Log.ForwardingError(logger, pair.RoomId, pair.Slot, failure);
        }
    }

    private Task RejectAsync(FrameConnection connection, string reason, CancellationToken stoppingToken) =>
        directory.RejectAsync(connection, reason, stoppingToken);
}
