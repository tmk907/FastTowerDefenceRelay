using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests.Conformance;

/// <summary>Access control of a token-restricted relay.</summary>
[Collection(ConformanceGroup.Name)]
[Trait("Category", "Conformance")]
public sealed class TokenConformanceTests(ConformanceRelays relays)
{
    private const string Unauthorized = "player token is not authorized";

    private static readonly byte[] Stranger = Enumerable.Repeat((byte)0x99, 32).ToArray();

    private RelayTarget Relay => relays.Private;

    [Fact]
    public async Task Every_request_needs_an_authorized_token()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.HostToken);
        using (host)
        {
            Assert.Equal(Unauthorized, await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayListRooms, Stranger)));
            Assert.Equal(Unauthorized, await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayListRooms, ConformanceRelays.ZeroToken)));
            Assert.Equal(Unauthorized, await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayCreateRoom, Spec.CreateRoom(Stranger, "Room", "Host"))));
            Assert.Equal(Unauthorized, await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(Stranger, roomId))));
            Assert.Contains(await this.Relay.ListAsync(ConformanceRelays.GuestToken), room => room.Id == roomId);
        }
    }

    [Fact]
    public async Task A_host_cannot_join_its_own_room()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.HostToken);
        using (host)
        {
            Assert.Equal("host and guest must use different player tokens",
                         await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(ConformanceRelays.HostToken, roomId))));
        }
    }

    [Fact]
    public async Task A_guest_token_takes_one_seat_per_room()
    {
        var (primary, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.HostToken);
        var (extra, _) = await this.Relay.AttachAsync(ConformanceRelays.HostToken, roomId);
        using (primary)
        using (extra)
        {
            var (guest, slot) = await this.Relay.JoinAsync(ConformanceRelays.GuestToken, roomId);
            using (guest)
            {
                Assert.Equal(2UL, slot);
                Assert.Equal("player token is already admitted to this room",
                             await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(ConformanceRelays.GuestToken, roomId))));

                var (second, secondSlot) = await this.Relay.JoinAsync(ConformanceRelays.SecondGuestToken, roomId);
                using (second)
                {
                    Assert.Equal(3UL, secondSlot);
                }
            }
        }
    }
}
