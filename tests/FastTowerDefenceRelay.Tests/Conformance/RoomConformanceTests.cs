using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests.Conformance;

/// <summary>Room creation, listing, joining and forwarding on a public relay.</summary>
[Collection(ConformanceGroup.Name)]
[Trait("Category", "Conformance")]
public sealed class RoomConformanceTests(ConformanceRelays relays)
{
    private RelayTarget Relay => relays.Public;

    [Fact]
    public async Task A_created_room_is_listed_with_its_names()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken, "Łódź \"Game\"", "Tomek");
        using (host)
        {
            var rooms = await this.Relay.ListAsync(ConformanceRelays.ZeroToken);
            Assert.Contains(new ListedRoom(roomId, "Łódź \"Game\"", "Tomek"), rooms);
            Assert.Equal(rooms.OrderBy(room => room.Id).ToList(), rooms);
        }
    }

    [Fact]
    public async Task A_join_pairs_the_guest_with_the_host_and_forwards_frames_unchanged()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken);
        using (host)
        {
            var (guest, slot) = await this.Relay.JoinAsync(ConformanceRelays.ZeroToken, roomId);
            using (guest)
            {
                Assert.Equal(2UL, slot);
                var ready = await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
                this.Relay.ExpectEnvelope(ready);
                Assert.Equal((roomId, 2UL), Spec.Slot(ready.Payload));

                // Game frames keep their own session, sequence and version through the relay.
                var hello = new TestFrame(this.Relay.Major, this.Relay.Minor, Spec.ClientHello, 0, 0x0102030405060708, 7, [1, 2, 3]);
                await guest.SendAsync(hello.Encode());
                Assert.Equal(hello.Encode(), (await host.ExpectAsync(Spec.ClientHello)).Encode());

                var large = new TestFrame(this.Relay.Major, this.Relay.Minor, Spec.Ping, 0, 9, 8, new byte[200_000]);
                Random.Shared.NextBytes(large.Payload);
                await host.SendAsync([.. large.Encode(), .. hello.Encode()]);
                Assert.Equal(large.Encode(), (await guest.ExpectAsync(Spec.Ping)).Encode());
                Assert.Equal(hello.Encode(), (await guest.ExpectAsync(Spec.ClientHello)).Encode());
            }

            // Either side leaving ends the pair.
            await host.ExpectClosedAsync();
        }
    }

    [Fact]
    public async Task A_host_leaving_closes_its_paired_guest()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken);
        var (guest, _) = await this.Relay.JoinAsync(ConformanceRelays.ZeroToken, roomId);
        using (guest)
        {
            await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
            host.Dispose();
            await guest.ExpectClosedAsync();
        }
    }

    [Fact]
    public async Task Additional_host_seats_are_handed_to_guests_in_slot_order()
    {
        var token = ConformanceRelays.PublicHostToken;
        var (primary, roomId) = await this.Relay.CreateRoomAsync(token, "Group");
        var (third, thirdSlot) = await this.Relay.AttachAsync(token, roomId);
        var (fourth, fourthSlot) = await this.Relay.AttachAsync(token, roomId);
        Assert.Equal((3UL, 4UL), (thirdSlot, fourthSlot));
        using var hostTwo = primary;
        using var hostThree = third;
        using var hostFour = fourth;

        var guests = new List<WireClient>();
        try
        {
            foreach (var (host, expectedSlot) in new[] { (hostTwo, 2UL), (hostThree, 3UL), (hostFour, 4UL) })
            {
                Assert.Contains(await this.Relay.ListAsync(ConformanceRelays.ZeroToken), room => room.Id == roomId);
                var (guest, slot) = await this.Relay.JoinAsync(ConformanceRelays.ZeroToken, roomId);
                guests.Add(guest);
                Assert.Equal(expectedSlot, slot);
                var ready = await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
                Assert.Equal((roomId, expectedSlot), Spec.Slot(ready.Payload));
            }

            // The last seat is taken, so the room is gone.
            Assert.DoesNotContain(await this.Relay.ListAsync(ConformanceRelays.ZeroToken), room => room.Id == roomId);

            // Each pair is forwarded independently of the others.
            var frame = new TestFrame(this.Relay.Major, this.Relay.Minor, Spec.ClientHello, 0, 3, 1, [3]);
            await hostThree.SendAsync(frame.Encode());
            Assert.Equal(frame.Encode(), (await guests[1].ExpectAsync(Spec.ClientHello)).Encode());
            await guests[2].SendAsync(frame.Encode());
            Assert.Equal(frame.Encode(), (await hostFour.ExpectAsync(Spec.ClientHello)).Encode());
        }
        finally
        {
            guests.ForEach(guest => guest.Dispose());
        }
    }

    [Fact]
    public async Task A_room_offers_at_most_seven_guest_seats()
    {
        var token = ConformanceRelays.PublicHostToken;
        var (primary, roomId) = await this.Relay.CreateRoomAsync(token);
        var hosts = new List<WireClient> { primary };
        try
        {
            for (var expected = 3UL; expected <= 8; ++expected)
            {
                var (host, slot) = await this.Relay.AttachAsync(token, roomId);
                hosts.Add(host);
                Assert.Equal(expected, slot);
            }
            Assert.Equal("invalid or full host room attachment",
                         await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(token, roomId))));
        }
        finally
        {
            hosts.ForEach(host => host.Dispose());
        }
    }

    [Fact]
    public async Task Listing_sends_heartbeats_to_waiting_hosts()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken);
        using (host)
        {
            Assert.Contains(await this.Relay.ListAsync(ConformanceRelays.ZeroToken), room => room.Id == roomId);
            var heartbeat = await host.ExpectAsync(Spec.Ping);
            this.Relay.ExpectEnvelope(heartbeat);
            Assert.Empty(heartbeat.Payload);
        }
    }

    [Fact]
    public async Task A_room_whose_host_left_leaves_the_list()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken, "Abandoned");
        host.Dispose();

        var deadline = DateTime.UtcNow + WireClient.Timeout;
        while ((await this.Relay.ListAsync(ConformanceRelays.ZeroToken)).Any(room => room.Id == roomId))
        {
            Assert.True(DateTime.UtcNow < deadline, "The abandoned room stayed listed");
            await Task.Delay(20);
        }
        Assert.Equal("relay room is no longer available",
                     await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(ConformanceRelays.ZeroToken, roomId))));
    }

    [Fact]
    public async Task Joining_an_unknown_room_is_rejected()
    {
        Assert.Equal("relay room is no longer available",
                     await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(ConformanceRelays.ZeroToken, ulong.MaxValue))));
    }

    [Fact]
    public async Task Rejections_carry_the_relay_version_and_no_session()
    {
        using var client = await WireClient.ConnectAsync(this.Relay.EndPoint);
        await client.SendAsync(this.Relay.Frame(Spec.RelayJoinRoom, Spec.RoomRequest(ConformanceRelays.ZeroToken, ulong.MaxValue)));
        var reject = await client.ReceiveAsync();
        Assert.NotNull(reject);
        Assert.Equal(Spec.RelayReject, reject.Type);
        this.Relay.ExpectEnvelope(reject);
    }

    [Fact]
    public async Task Seats_can_only_be_reserved_with_the_room_creators_token()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken);
        using (host)
        {
            Assert.Equal("invalid or full host room attachment",
                         await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(ConformanceRelays.HostToken, roomId))));
            Assert.Equal("invalid or full host room attachment",
                         await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(ConformanceRelays.PublicHostToken, ulong.MaxValue))));
        }

        // The all-zero public token is no creator capability.
        var (zeroHost, zeroRoom) = await this.Relay.CreateRoomAsync(ConformanceRelays.ZeroToken);
        using (zeroHost)
        {
            Assert.Equal("invalid or full host room attachment",
                         await this.Relay.RejectedAsync(this.Relay.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(ConformanceRelays.ZeroToken, zeroRoom))));
        }
    }

    [Fact]
    public async Task Reserving_a_seat_requires_protocol_minor_3()
    {
        var (host, roomId) = await this.Relay.CreateRoomAsync(ConformanceRelays.PublicHostToken);
        using (host)
        {
            var request = Spec.Frame(Spec.RelayAttachRoom, Spec.RoomRequest(ConformanceRelays.PublicHostToken, roomId), this.Relay.Major, 2);
            Assert.Equal("multi-guest room attachment requires protocol minor 3", await this.Relay.RejectedAsync(request));
        }
    }
}
