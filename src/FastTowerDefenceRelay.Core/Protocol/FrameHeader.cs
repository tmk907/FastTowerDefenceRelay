using System.Buffers.Binary;

namespace FastTowerDefenceRelay.Protocol;

/// <summary>
/// The fixed 32-byte header in front of every frame payload.
/// </summary>
/// <remarks>
/// The message type is not validated here: the relay forwards frames of
/// any type, so new game messages need no relay update.
/// </remarks>
internal readonly record struct FrameHeader(
    ProtocolVersion Version,
    ushort MessageType,
    int PayloadSize,
    ulong SessionId,
    ulong Sequence)
{
    /// <summary>Validate and decode a header.</summary>
    /// <exception cref="ProtocolException">Wrong magic, nonzero flags or oversized payload.</exception>
    public static FrameHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length != Wire.FrameHeaderSize)
        {
            throw new ArgumentException($"A frame header has {Wire.FrameHeaderSize} bytes", nameof(header));
        }
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != Wire.FrameMagic)
        {
            throw new ProtocolException("Invalid frame magic");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(header[10..]);
        if (flags != 0)
        {
            throw new ProtocolException($"Unsupported frame flags {flags}");
        }

        var payloadSize = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        if (payloadSize > Wire.MaxPayloadSize)
        {
            throw new ProtocolException($"Frame payload of {payloadSize} bytes exceeds {Wire.MaxPayloadSize}");
        }

        return new FrameHeader(
            new ProtocolVersion(
                BinaryPrimitives.ReadUInt16BigEndian(header[4..]),
                BinaryPrimitives.ReadUInt16BigEndian(header[6..])),
            BinaryPrimitives.ReadUInt16BigEndian(header[8..]),
            (int)payloadSize,
            BinaryPrimitives.ReadUInt64BigEndian(header[16..]),
            BinaryPrimitives.ReadUInt64BigEndian(header[24..]));
    }

    /// <summary>Encode one frame from a header version, type and payload.</summary>
    public static byte[] Encode(ProtocolVersion version, MessageType type, ReadOnlySpan<byte> payload,
                                ulong sessionId = 0, ulong sequence = 0)
    {
        if (payload.Length > Wire.MaxPayloadSize)
        {
            throw new ArgumentException($"Frame payload exceeds {Wire.MaxPayloadSize} bytes", nameof(payload));
        }

        var frame = new byte[Wire.FrameHeaderSize + payload.Length];
        var span = frame.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, Wire.FrameMagic);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], version.Major);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], version.Minor);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)type);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], sessionId);
        BinaryPrimitives.WriteUInt64BigEndian(span[24..], sequence);
        payload.CopyTo(span[Wire.FrameHeaderSize..]);
        return frame;
    }
}

/// <summary>One received frame: its decoded header and its complete encoded bytes.</summary>
internal sealed record ReceivedFrame(FrameHeader Header, byte[] Bytes)
{
    public ReadOnlySpan<byte> Payload => this.Bytes.AsSpan(Wire.FrameHeaderSize);
}
