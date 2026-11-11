using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests.Conformance;

/// <summary>Validation of directory requests and their rejection reasons.</summary>
[Collection(ConformanceGroup.Name)]
[Trait("Category", "Conformance")]
public sealed class RequestConformanceTests(ConformanceRelays relays)
{
    private static readonly byte[] Token = ConformanceRelays.ZeroToken;

    private RelayTarget Relay => relays.Public;

    public static TheoryData<string> InvalidEnvelopes => ["magic", "flags", "older major", "newer major", "newer minor", "minor 1", "session", "sequence"];

    [Theory]
    [MemberData(nameof(InvalidEnvelopes))]
    public async Task Requests_outside_the_directory_envelope_are_malformed(string defect)
    {
        var (major, minor) = (this.Relay.Major, this.Relay.Minor);
        var request = defect switch
        {
            "magic" => Spec.Frame(Spec.RelayListRooms, Token, major, minor, magic: 0x12345678),
            "flags" => Spec.Frame(Spec.RelayListRooms, Token, major, minor, flags: 1),
            "older major" => Spec.Frame(Spec.RelayListRooms, Token, (ushort)(major - 1), minor),
            "newer major" => Spec.Frame(Spec.RelayListRooms, Token, (ushort)(major + 1), minor),
            "newer minor" => Spec.Frame(Spec.RelayListRooms, Token, major, (ushort)(minor + 1)),
            "minor 1" => Spec.Frame(Spec.RelayListRooms, Token, major, 1),
            "session" => Spec.Frame(Spec.RelayListRooms, Token, major, minor, session: 1),
            "sequence" => Spec.Frame(Spec.RelayListRooms, Token, major, minor, sequence: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Assert.Equal("malformed relay directory request", await this.Relay.RejectedAsync(request));
    }

    [Fact]
    public async Task Game_messages_are_not_directory_requests()
    {
        Assert.Equal("unsupported relay directory request",
                     await this.Relay.RejectedAsync(this.Relay.Frame(Spec.Ping, [])));
        Assert.Equal("unsupported relay directory request",
                     await this.Relay.RejectedAsync(this.Relay.Frame(Spec.ClientHello, [1, 2, 3])));
    }

    public static TheoryData<string, string> InvalidPayloads => new()
    {
        { "list: short token", "malformed room listing request" },
        { "list: trailing byte", "malformed room listing request" },
        { "create: truncated", "malformed room creation request" },
        { "create: empty room name", "malformed room creation request" },
        { "create: room name too long", "malformed room creation request" },
        { "create: host name too long", "malformed room creation request" },
        { "create: control character", "malformed room creation request" },
        { "create: DEL character", "malformed room creation request" },
        { "create: trailing byte", "malformed room creation request" },
        { "attach: short", "malformed host room attachment" },
        { "attach: room 0", "malformed host room attachment" },
        { "join: long", "malformed room join request" },
        { "join: room 0", "malformed room join request" },
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Malformed_payloads_are_rejected_by_request_type(string defect, string reason)
    {
        var (type, payload) = defect switch
        {
            "list: short token" => (Spec.RelayListRooms, Token[1..]),
            "list: trailing byte" => (Spec.RelayListRooms, [.. Token, 0]),
            "create: truncated" => (Spec.RelayCreateRoom, [.. Token, 0, 0, 0]),
            "create: empty room name" => (Spec.RelayCreateRoom, Spec.CreateRoom(Token, "", "Host")),
            "create: room name too long" => (Spec.RelayCreateRoom, Spec.CreateRoom(Token, new string('r', 65), "Host")),
            "create: host name too long" => (Spec.RelayCreateRoom, Spec.CreateRoom(Token, "Room", new string('h', 129))),
            "create: control character" => (Spec.RelayCreateRoom, Spec.CreateRoom(Token, "Ro\nom", "Host")),
            "create: DEL character" => (Spec.RelayCreateRoom, Spec.CreateRoom(Token, "Room", "Ho\u007fst")),
            "create: trailing byte" => (Spec.RelayCreateRoom, [.. Spec.CreateRoom(Token, "Room", "Host"), 0]),
            "attach: short" => (Spec.RelayAttachRoom, Token),
            "attach: room 0" => (Spec.RelayAttachRoom, Spec.RoomRequest(ConformanceRelays.PublicHostToken, 0)),
            "join: long" => (Spec.RelayJoinRoom, [.. Spec.RoomRequest(Token, 1), 0]),
            "join: room 0" => (Spec.RelayJoinRoom, Spec.RoomRequest(Token, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Assert.Equal(reason, await this.Relay.RejectedAsync(this.Relay.Frame(type, payload)));
    }

    [Fact]
    public async Task Names_of_the_maximum_size_are_accepted()
    {
        var roomName = new string('r', 64);
        var hostName = new string('h', 128);
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken, roomName, hostName);
        using (host)
        {
            Assert.Contains(new ListedRoom(roomId, roomName, hostName), await this.Relay.ListAsync(Token));
        }
    }

    [Fact]
    public async Task Each_connection_carries_one_listing()
    {
        using var client = await WireClient.ConnectAsync(this.Relay.EndPoint);
        await client.SendAsync(this.Relay.Frame(Spec.RelayListRooms, Token));
        await client.ExpectAsync(Spec.RelayRoomList);
        await client.ExpectClosedAsync();
    }
}
