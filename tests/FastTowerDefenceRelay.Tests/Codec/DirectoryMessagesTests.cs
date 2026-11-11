using System.Text;
using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Tests.Codec;

public sealed class DirectoryMessagesTests
{
    private static readonly byte[] Token = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void A_room_creation_request_is_a_token_and_two_length_prefixed_names()
    {
        var payload = Convert.FromHexString(
            Convert.ToHexString(Token) + "00000004" + "526F6F6D" + "00000002" + "4869");
        var request = DirectoryMessages.DecodeCreateRoom(payload);
        Assert.Equal(new RelayToken(Token), request.Token);
        Assert.Equal("Room"u8.ToArray(), request.RoomName);
        Assert.Equal("Hi"u8.ToArray(), request.HostName);
    }

    [Theory]
    [InlineData("", "Host")]
    [InlineData("Room", "")]
    [InlineData("Tab\tRoom", "Host")]
    [InlineData("Room", "Host\u007f")]
    public void Names_must_be_nonempty_and_free_of_control_characters(string roomName, string hostName)
    {
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeCreateRoom(CreateRoom(roomName, hostName)));
    }

    [Fact]
    public void Names_are_limited_in_bytes_not_characters()
    {
        // 32 two-byte characters fill the 64-byte room name limit exactly.
        var roomName = new string('ł', 32);
        Assert.Equal(64, DirectoryMessages.DecodeCreateRoom(CreateRoom(roomName, new string('h', 128))).RoomName.Length);
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeCreateRoom(CreateRoom(roomName + "x", "Host")));
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeCreateRoom(CreateRoom("Room", new string('h', 129))));
    }

    [Fact]
    public void Truncated_or_padded_requests_are_rejected()
    {
        var valid = CreateRoom("Room", "Host");
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeCreateRoom(valid.AsSpan(..^1)));
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeCreateRoom([.. valid, 0]));
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeTokenRequest(Token.AsSpan(..^1)));
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeRoomRequest(Token));
    }

    [Fact]
    public void A_room_request_is_a_token_and_a_nonzero_room_id()
    {
        var request = DirectoryMessages.DecodeRoomRequest([.. Token, 0, 0, 0, 0, 0, 0, 1, 2]);
        Assert.Equal(new RelayToken(Token), request.Token);
        Assert.Equal(0x0102UL, request.RoomId);
        Assert.Throws<ProtocolException>(() => DirectoryMessages.DecodeRoomRequest([.. Token, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void The_room_list_is_a_count_and_length_prefixed_entries()
    {
        var payload = DirectoryMessages.EncodeRoomList(
        [
            new RoomSummary(1, "A"u8.ToArray(), "Bc"u8.ToArray()),
            new RoomSummary(0x0203, "D"u8.ToArray(), "E"u8.ToArray()),
        ]);
        Assert.Equal(
            "00000002"
            + "0000000000000001" + "00000001" + "41" + "00000002" + "4263"
            + "0000000000000203" + "00000001" + "44" + "00000001" + "45",
            Convert.ToHexString(payload));
        Assert.Equal("00000000", Convert.ToHexString(DirectoryMessages.EncodeRoomList([])));
    }

    [Fact]
    public void Slot_and_room_responses_are_big_endian()
    {
        Assert.Equal("0000000000000007", Convert.ToHexString(DirectoryMessages.EncodeRoomCreated(7)));
        Assert.Equal("0000000000000007" + "0000000000000003", Convert.ToHexString(DirectoryMessages.EncodeSlot(7, 3)));
    }

    [Fact]
    public void Rejection_reasons_are_truncated_to_4096_bytes()
    {
        Assert.Equal("00000002" + "6E6F", Convert.ToHexString(DirectoryMessages.EncodeReject("no")));
        var payload = DirectoryMessages.EncodeReject(new string('x', 5000));
        Assert.Equal(4 + 4096, payload.Length);
        Assert.Equal("00001000", Convert.ToHexString(payload[..4]));
    }

    private static byte[] CreateRoom(string roomName, string hostName)
    {
        static byte[] Text(string value)
        {
            var text = Encoding.UTF8.GetBytes(value);
            return [(byte)(text.Length >> 24), (byte)(text.Length >> 16), (byte)(text.Length >> 8), (byte)text.Length, .. text];
        }
        return [.. Token, .. Text(roomName), .. Text(hostName)];
    }
}
