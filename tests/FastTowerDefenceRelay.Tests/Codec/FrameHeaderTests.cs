using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Tests.Codec;

public sealed class FrameHeaderTests
{
    // A PING of protocol 22.4, session 0x0102030405060708, sequence 9 and payload 01 02 03,
    // laid out as in docs/wire-protocol.md.
    private static readonly byte[] Golden = Convert.FromHexString(
        "4656444E" + "0016" + "0004" + "000B" + "0000" + "00000003"
        + "0102030405060708" + "0000000000000009" + "010203");

    [Fact]
    public void Encoding_matches_the_specified_layout()
    {
        var frame = FrameHeader.Encode(new ProtocolVersion(22, 4), MessageType.Ping, [1, 2, 3], 0x0102030405060708, 9);
        Assert.Equal(Golden, frame);
    }

    [Fact]
    public void Parsing_reads_every_header_field()
    {
        var header = FrameHeader.Parse(Golden.AsSpan(0, Wire.FrameHeaderSize));
        Assert.Equal(new FrameHeader(new ProtocolVersion(22, 4), (ushort)MessageType.Ping, 3, 0x0102030405060708, 9), header);
    }

    [Fact]
    public void Unknown_message_types_are_accepted_for_forwarding()
    {
        var frame = Golden.ToArray();
        frame[8] = 0xff;
        frame[9] = 0xff;
        Assert.Equal(0xffff, FrameHeader.Parse(frame.AsSpan(0, Wire.FrameHeaderSize)).MessageType);
    }

    [Theory]
    [InlineData(0, 0x00)]
    [InlineData(3, 0x4f)]
    [InlineData(11, 0x01)]
    [InlineData(10, 0x80)]
    public void Invalid_magic_or_flags_are_rejected(int index, byte value)
    {
        var header = Golden[..Wire.FrameHeaderSize];
        header[index] = value;
        Assert.Throws<ProtocolException>(() => FrameHeader.Parse(header));
    }

    [Fact]
    public void Payloads_are_limited_to_one_mebibyte()
    {
        var header = Golden[..Wire.FrameHeaderSize];
        Convert.FromHexString("00100000").CopyTo(header, 12);
        Assert.Equal(Wire.MaxPayloadSize, FrameHeader.Parse(header).PayloadSize);

        Convert.FromHexString("00100001").CopyTo(header, 12);
        Assert.Throws<ProtocolException>(() => FrameHeader.Parse(header));
        Convert.FromHexString("FFFFFFFF").CopyTo(header, 12);
        Assert.Throws<ProtocolException>(() => FrameHeader.Parse(header));
    }
}
