using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FastTowerDefenceRelay.Tests.Client;

/// <summary>
/// A client written from docs/wire-protocol.md alone, independently of the
/// relay's own codec, so tests check the relay against the specification
/// and can run against any relay implementation.
/// </summary>
internal static class Spec
{
    public const uint Magic = 0x4656444e;
    public const int HeaderSize = 32;

    public const ushort ClientHello = 1;
    public const ushort Ping = 11;
    public const ushort RelayReady = 15;
    public const ushort RelayReject = 16;
    public const ushort RelayCreateRoom = 18;
    public const ushort RelayRoomCreated = 19;
    public const ushort RelayListRooms = 20;
    public const ushort RelayRoomList = 21;
    public const ushort RelayJoinRoom = 22;
    public const ushort RelayAttachRoom = 23;
    public const ushort RelayRoomAttached = 24;

    public static byte[] Frame(ushort type, byte[] payload, ushort major, ushort minor,
                               ulong session = 0, ulong sequence = 0, ushort flags = 0, uint magic = Magic)
    {
        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0), magic);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), major);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), minor);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), type);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), flags);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12), (uint)payload.Length);
        BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(16), session);
        BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(24), sequence);
        payload.CopyTo(frame, HeaderSize);
        return frame;
    }

    public static byte[] Text(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var field = new byte[4 + text.Length];
        BinaryPrimitives.WriteUInt32BigEndian(field, (uint)text.Length);
        text.CopyTo(field, 4);
        return field;
    }

    public static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] CreateRoom(byte[] token, string roomName, string hostName) =>
        [.. token, .. Text(roomName), .. Text(hostName)];

    public static byte[] RoomRequest(byte[] token, ulong roomId) => [.. token, .. U64(roomId)];

    public static string Reject(byte[] payload)
    {
        var size = BinaryPrimitives.ReadUInt32BigEndian(payload);
        Assert.Equal(payload.Length - 4, (int)size);
        return Encoding.UTF8.GetString(payload, 4, (int)size);
    }

    public static (ulong RoomId, ulong Slot) Slot(byte[] payload)
    {
        Assert.Equal(16, payload.Length);
        return (BinaryPrimitives.ReadUInt64BigEndian(payload), BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(8)));
    }

    public static List<ListedRoom> RoomList(byte[] payload)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var offset = 4;
        var rooms = new List<ListedRoom>();
        for (var i = 0; i < count; ++i)
        {
            var id = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(offset));
            offset += 8;
            var name = ReadText(payload, ref offset);
            var host = ReadText(payload, ref offset);
            rooms.Add(new ListedRoom(id, name, host));
        }
        Assert.Equal(payload.Length, offset);
        return rooms;
    }

    private static string ReadText(byte[] payload, ref int offset)
    {
        var size = (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset));
        offset += 4;
        var text = Encoding.UTF8.GetString(payload, offset, size);
        offset += size;
        return text;
    }
}

internal sealed record ListedRoom(ulong Id, string Name, string Host);

internal sealed record TestFrame(ushort Major, ushort Minor, ushort Type, ushort Flags, ulong Session, ulong Sequence, byte[] Payload)
{
    public byte[] Encode() => Spec.Frame(this.Type, this.Payload, this.Major, this.Minor, this.Session, this.Sequence, this.Flags);
}

/// <summary>One raw TCP connection to a relay.</summary>
internal sealed class WireClient : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TcpClient client;
    private readonly NetworkStream stream;

    private WireClient(TcpClient client)
    {
        this.client = client;
        this.client.NoDelay = true;
        this.stream = client.GetStream();
    }

    public static async Task<WireClient> ConnectAsync(IPEndPoint endPoint)
    {
        var client = new TcpClient(endPoint.AddressFamily);
        await client.ConnectAsync(endPoint);
        return new WireClient(client);
    }

    public async Task SendAsync(byte[] bytes)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await this.stream.WriteAsync(bytes, timeout.Token);
    }

    /// <returns>The next frame, or null once the relay closed the connection.</returns>
    public async Task<TestFrame?> ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var header = new byte[Spec.HeaderSize];
        int received;
        try
        {
            received = await this.stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, timeout.Token);
        }
        catch (IOException)
        {
            // A reset after the relay closed is as final as an orderly close.
            return null;
        }
        if (received == 0)
        {
            return null;
        }
        Assert.Equal(header.Length, received);
        Assert.Equal(Spec.Magic, BinaryPrimitives.ReadUInt32BigEndian(header));

        var payload = new byte[BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12))];
        await this.stream.ReadExactlyAsync(payload, timeout.Token);
        return new TestFrame(
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)),
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6)),
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8)),
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10)),
            BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16)),
            BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(24)),
            payload);
    }

    public async Task<TestFrame> ExpectAsync(ushort type)
    {
        var frame = await this.ReceiveAsync();
        Assert.NotNull(frame);
        if (frame.Type == Spec.RelayReject)
        {
            Assert.Fail($"Relay rejected the request: {Spec.Reject(frame.Payload)}");
        }
        Assert.Equal(type, frame.Type);
        return frame;
    }

    /// <summary>Wait for a frame as a waiting host does, skipping relay heartbeats.</summary>
    public async Task<TestFrame> ExpectSkippingHeartbeatsAsync(ushort type)
    {
        while (true)
        {
            var frame = await this.ExpectAnyAsync();
            if (frame.Type != Spec.Ping)
            {
                Assert.Equal(type, frame.Type);
                return frame;
            }
            Assert.Empty(frame.Payload);
        }
    }

    public async Task<string> ExpectRejectAsync()
    {
        var frame = await this.ExpectAnyAsync();
        Assert.Equal(Spec.RelayReject, frame.Type);
        var reason = Spec.Reject(frame.Payload);
        await this.ExpectClosedAsync();
        return reason;
    }

    public async Task ExpectClosedAsync()
    {
        var frame = await this.ReceiveAsync();
        Assert.Null(frame);
    }

    public void Dispose() => this.client.Dispose();

    private async Task<TestFrame> ExpectAnyAsync()
    {
        var frame = await this.ReceiveAsync();
        Assert.NotNull(frame);
        return frame;
    }
}
