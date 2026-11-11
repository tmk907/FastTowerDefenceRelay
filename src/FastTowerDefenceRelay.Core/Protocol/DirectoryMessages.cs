using System.Buffers.Binary;
using System.Text;

namespace FastTowerDefenceRelay.Protocol;

/// <summary>A room creation request: the host's token and the advertised names.</summary>
internal sealed record CreateRoomRequest(RelayToken Token, byte[] RoomName, byte[] HostName);

/// <summary>A join or attach request: a token and the room it addresses.</summary>
internal sealed record RoomRequest(RelayToken Token, ulong RoomId);

/// <summary>A room as advertised in the binary room list.</summary>
internal sealed record RoomSummary(ulong RoomId, byte[] RoomName, byte[] HostName);

/// <summary>Payload codecs of the room directory messages, see docs/wire-protocol.md.</summary>
internal static class DirectoryMessages
{
    private const int RoomRequestSize = Wire.TokenSize + sizeof(ulong);

    /// <summary>Decode a RELAY_LIST_ROOMS payload: exactly one token.</summary>
    public static RelayToken DecodeTokenRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != Wire.TokenSize)
        {
            throw new ProtocolException("Invalid token request size");
        }
        return new RelayToken(payload);
    }

    /// <summary>Decode a RELAY_CREATE_ROOM payload.</summary>
    public static CreateRoomRequest DecodeCreateRoom(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Wire.TokenSize + sizeof(uint) * 2)
        {
            throw new ProtocolException("Truncated room creation request");
        }

        var token = new RelayToken(payload[..Wire.TokenSize]);
        var offset = Wire.TokenSize;
        var roomName = ReadText(payload, ref offset, Wire.MaxRoomNameSize, "room name");
        var hostName = ReadText(payload, ref offset, Wire.MaxParticipantNameSize, "host name");
        if (offset != payload.Length)
        {
            throw new ProtocolException("Trailing data in room creation request");
        }
        return new CreateRoomRequest(token, roomName, hostName);
    }

    /// <summary>Decode a RELAY_JOIN_ROOM or RELAY_ATTACH_ROOM payload.</summary>
    public static RoomRequest DecodeRoomRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != RoomRequestSize)
        {
            throw new ProtocolException("Invalid room request size");
        }

        var roomId = BinaryPrimitives.ReadUInt64BigEndian(payload[Wire.TokenSize..]);
        if (roomId == 0)
        {
            throw new ProtocolException("Room ID must be nonzero");
        }
        return new RoomRequest(new RelayToken(payload[..Wire.TokenSize]), roomId);
    }

    /// <summary>Encode the RELAY_ROOM_CREATED payload.</summary>
    public static byte[] EncodeRoomCreated(ulong roomId)
    {
        var payload = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(payload, roomId);
        return payload;
    }

    /// <summary>Encode the RELAY_READY or RELAY_ROOM_ATTACHED payload.</summary>
    public static byte[] EncodeSlot(ulong roomId, ulong slot)
    {
        var payload = new byte[sizeof(ulong) * 2];
        BinaryPrimitives.WriteUInt64BigEndian(payload, roomId);
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(sizeof(ulong)), slot);
        return payload;
    }

    /// <summary>Encode the RELAY_ROOM_LIST payload.</summary>
    public static byte[] EncodeRoomList(IReadOnlyList<RoomSummary> rooms)
    {
        if (rooms.Count > Wire.MaxDirectoryRooms)
        {
            throw new ArgumentException($"A room list holds at most {Wire.MaxDirectoryRooms} rooms", nameof(rooms));
        }

        var size = sizeof(uint);
        foreach (var room in rooms)
        {
            size += sizeof(ulong) + sizeof(uint) * 2 + room.RoomName.Length + room.HostName.Length;
        }

        var payload = new byte[size];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)rooms.Count);
        var offset = sizeof(uint);
        foreach (var room in rooms)
        {
            BinaryPrimitives.WriteUInt64BigEndian(span[offset..], room.RoomId);
            offset += sizeof(ulong);
            WriteText(span, ref offset, room.RoomName);
            WriteText(span, ref offset, room.HostName);
        }
        return payload;
    }

    /// <summary>Encode the RELAY_REJECT payload, truncating the reason to its limit.</summary>
    public static byte[] EncodeReject(string reason)
    {
        var text = Encoding.UTF8.GetBytes(reason);
        var size = Math.Min(text.Length, Wire.MaxRejectionSize);
        var payload = new byte[sizeof(uint) + size];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)size);
        text.AsSpan(0, size).CopyTo(payload.AsSpan(sizeof(uint)));
        return payload;
    }

    private static byte[] ReadText(ReadOnlySpan<byte> payload, ref int offset, int maximumSize, string field)
    {
        if (payload.Length - offset < sizeof(uint))
        {
            throw new ProtocolException($"Truncated {field}");
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
        offset += sizeof(uint);
        if (size == 0 || size > maximumSize || (uint)(payload.Length - offset) < size)
        {
            throw new ProtocolException($"Invalid {field} size");
        }

        var text = payload.Slice(offset, (int)size);
        offset += (int)size;
        foreach (var value in text)
        {
            if (value < 32 || value == 127)
            {
                throw new ProtocolException($"The {field} contains a control character");
            }
        }
        return text.ToArray();
    }

    private static void WriteText(Span<byte> payload, ref int offset, byte[] text)
    {
        BinaryPrimitives.WriteUInt32BigEndian(payload[offset..], (uint)text.Length);
        offset += sizeof(uint);
        text.CopyTo(payload[offset..]);
        offset += text.Length;
    }
}
