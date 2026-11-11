using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests;

/// <summary>
/// Behavior of this relay beyond the conformance suite: configured limits,
/// forwarding of future message types and shutdown.
/// </summary>
public sealed class RelayLimitsTests
{
    private static readonly byte[] Token = new byte[32];
    private static readonly byte[] HostToken = Enumerable.Repeat((byte)7, 32).ToArray();

    [Fact]
    public async Task A_full_directory_rejects_new_rooms()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback() with { MaxRooms = 1 });
        var target = relay.Target;
        var (host, _) = await target.CreateRoomAsync(HostToken);
        using (host)
        {
            Assert.Equal("relay room directory is full",
                         await target.RejectedAsync(target.Frame(Spec.RelayCreateRoom, Spec.CreateRoom(Token, "Second", "Host"))));
        }
    }

    [Fact]
    public async Task Connections_beyond_the_limit_are_rejected()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback() with { MaxConnections = 1 });
        var target = relay.Target;

        // A connection that has not sent its request yet occupies the only slot.
        using var silent = await WireClient.ConnectAsync(target.EndPoint);
        await Task.Delay(100);
        using var rejected = await WireClient.ConnectAsync(target.EndPoint);
        Assert.Equal("relay connection capacity is full", await rejected.ExpectRejectAsync());

        silent.Dispose();
        await Eventually(async () => (await TryListAsync(target)) is not null);
    }

    [Fact]
    public async Task Waiting_hosts_do_not_count_against_the_connection_limit()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback() with { MaxConnections = 1 });
        var target = relay.Target;
        var (host, roomId) = await target.CreateRoomAsync(HostToken);
        using (host)
        {
            await Eventually(async () => (await TryListAsync(target))?.Any(room => room.Id == roomId) == true);
        }
    }

    [Fact]
    public async Task A_connection_must_send_its_request_in_time()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback() with { RequestTimeout = TimeSpan.FromMilliseconds(200) });
        using var client = await WireClient.ConnectAsync(relay.Target.EndPoint);
        Assert.Equal("relay directory request timed out", await client.ExpectRejectAsync());
    }

    [Fact]
    public async Task Pairs_forward_message_types_the_relay_does_not_know()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback());
        var target = relay.Target;
        var (host, roomId) = await target.CreateRoomAsync(HostToken);
        var (guest, _) = await target.JoinAsync(Token, roomId);
        using (host)
        using (guest)
        {
            await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
            var future = new TestFrame(target.Major, (ushort)(target.Minor + 1), 999, 0, 1, 1, [4, 2]);
            await guest.SendAsync(future.Encode());
            Assert.Equal(future.Encode(), (await host.ExpectAsync(999)).Encode());
        }
    }

    [Fact]
    public async Task An_invalid_frame_ends_the_pair()
    {
        await using var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback());
        var target = relay.Target;
        var (host, roomId) = await target.CreateRoomAsync(HostToken);
        var (guest, _) = await target.JoinAsync(Token, roomId);
        using (host)
        using (guest)
        {
            await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
            await guest.SendAsync(Spec.Frame(Spec.ClientHello, [], target.Major, target.Minor, flags: 1));
            await host.ExpectClosedAsync();
            await guest.ExpectClosedAsync();
        }
    }

    [Fact]
    public async Task Stopping_the_relay_closes_waiting_hosts_and_pairs()
    {
        var relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback());
        await using (relay)
        {
            var target = relay.Target;
            var (waiting, _) = await target.CreateRoomAsync(HostToken);
            var (host, roomId) = await target.CreateRoomAsync(HostToken);
            var (guest, _) = await target.JoinAsync(Token, roomId);
            using (waiting)
            using (host)
            using (guest)
            {
                await host.ExpectSkippingHeartbeatsAsync(Spec.RelayReady);
                await relay.StopAsync();
                await waiting.ExpectClosedAsync();
                await host.ExpectClosedAsync();
                await guest.ExpectClosedAsync();
            }
        }
    }

    private static async Task<List<ListedRoom>?> TryListAsync(RelayTarget target)
    {
        using var client = await WireClient.ConnectAsync(target.EndPoint);
        await client.SendAsync(target.Frame(Spec.RelayListRooms, Token));
        var frame = await client.ReceiveAsync();
        return frame?.Type == Spec.RelayRoomList ? Spec.RoomList(frame.Payload) : null;
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + WireClient.Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time");
            await Task.Delay(20);
        }
    }
}
