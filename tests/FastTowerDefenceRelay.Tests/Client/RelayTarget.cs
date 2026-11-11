using System.Buffers.Binary;
using System.Net;

namespace FastTowerDefenceRelay.Tests.Client;

/// <summary>A running relay under test and the protocol version it speaks.</summary>
internal sealed record RelayTarget(IPEndPoint EndPoint, ushort Major, ushort Minor)
{
    public byte[] Frame(ushort type, byte[] payload) => Spec.Frame(type, payload, this.Major, this.Minor);

    public async Task<(WireClient Host, ulong RoomId)> CreateRoomAsync(byte[] token, string roomName = "Room", string hostName = "Host")
    {
        var host = await WireClient.ConnectAsync(this.EndPoint);
        await host.SendAsync(this.Frame(Spec.RelayCreateRoom, Spec.CreateRoom(token, roomName, hostName)));
        var created = await host.ExpectAsync(Spec.RelayRoomCreated);
        this.ExpectEnvelope(created);
        Assert.Equal(8, created.Payload.Length);
        var roomId = BinaryPrimitives.ReadUInt64BigEndian(created.Payload);
        Assert.NotEqual(0UL, roomId);
        return (host, roomId);
    }

    public async Task<(WireClient Host, ulong Slot)> AttachAsync(byte[] token, ulong roomId)
    {
        var host = await WireClient.ConnectAsync(this.EndPoint);
        await host.SendAsync(this.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(token, roomId)));
        var attached = await host.ExpectAsync(Spec.RelayRoomAttached);
        var (attachedRoom, slot) = Spec.Slot(attached.Payload);
        Assert.Equal(roomId, attachedRoom);
        return (host, slot);
    }

    public async Task<(WireClient Guest, ulong Slot)> JoinAsync(byte[] token, ulong roomId)
    {
        var guest = await WireClient.ConnectAsync(this.EndPoint);
        await guest.SendAsync(this.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(token, roomId)));
        var ready = await guest.ExpectAsync(Spec.RelayReady);
        this.ExpectEnvelope(ready);
        var (joinedRoom, slot) = Spec.Slot(ready.Payload);
        Assert.Equal(roomId, joinedRoom);
        return (guest, slot);
    }

    public async Task<List<ListedRoom>> ListAsync(byte[] token)
    {
        using var client = await WireClient.ConnectAsync(this.EndPoint);
        await client.SendAsync(this.Frame(Spec.RelayListRooms, token));
        var list = await client.ExpectAsync(Spec.RelayRoomList);
        this.ExpectEnvelope(list);
        return Spec.RoomList(list.Payload);
    }

    /// <summary>Send one raw request on a new connection and return the rejection reason.</summary>
    public async Task<string> RejectedAsync(byte[] request)
    {
        using var client = await WireClient.ConnectAsync(this.EndPoint);
        await client.SendAsync(request);
        return await client.ExpectRejectAsync();
    }

    public void ExpectEnvelope(TestFrame frame)
    {
        Assert.Equal(this.Major, frame.Major);
        Assert.Equal(this.Minor, frame.Minor);
        Assert.Equal(0, frame.Flags);
        Assert.Equal(0UL, frame.Session);
        Assert.Equal(0UL, frame.Sequence);
    }
}
