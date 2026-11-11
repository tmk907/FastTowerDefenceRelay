using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests.Http;

public sealed class RoomsEndpointTests : IAsyncLifetime, IDisposable
{
    private static readonly byte[] Token = new byte[32];
    private static readonly byte[] HostToken = Enumerable.Repeat((byte)7, 32).ToArray();

    private readonly HttpClient http = new();
    private InProcessRelay relay = null!;

    public async Task InitializeAsync()
    {
        this.relay = await InProcessRelay.StartAsync(InProcessRelay.Loopback(), httpPort: 0);
        this.http.BaseAddress = this.relay.HttpBase;
    }

    public async Task DisposeAsync() => await this.relay.DisposeAsync();

    public void Dispose() => this.http.Dispose();

    [Fact]
    public async Task An_empty_relay_lists_no_rooms_and_its_protocol()
    {
        using var response = await this.http.GetAsync(new Uri("/rooms", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"protocol":{"major":22,"minor":4},"rooms":[]}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_listing_is_public_and_never_cached()
    {
        using var response = await this.http.GetAsync(new Uri("/rooms?client=web", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["*"], response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Rooms_report_players_and_reserved_capacity()
    {
        var target = this.relay.Target;
        var (primary, roomId) = await target.CreateRoomAsync(HostToken, "Lobby", "Host");
        var (third, _) = await target.AttachAsync(HostToken, roomId);
        var (fourth, _) = await target.AttachAsync(HostToken, roomId);
        using (primary)
        using (third)
        using (fourth)
        {
            Assert.Equal((1, 4), await this.OccupancyAsync(roomId));

            var (guest, _) = await target.JoinAsync(Token, roomId);
            using (guest)
            {
                Assert.Equal((2, 4), await this.OccupancyAsync(roomId));
            }
        }
    }

    [Fact]
    public async Task Names_are_returned_as_text()
    {
        var target = this.relay.Target;
        using var host = await WireClient.ConnectAsync(target.EndPoint);
        byte[] roomName = [.. "Gra "u8, 0xff, .. "ł\"\\"u8];
        await host.SendAsync(target.Frame(Spec.RelayCreateRoom, [.. HostToken, 0, 0, 0, (byte)roomName.Length, .. roomName, .. Spec.Text("Tomek")]));
        var created = await host.ExpectAsync(Spec.RelayRoomCreated);
        var roomId = BinaryPrimitives.ReadUInt64BigEndian(created.Payload);

        var room = (await this.RoomsAsync()).Single(room => room.GetProperty("id").GetUInt64() == roomId);
        Assert.Equal("Gra �ł\"\\", room.GetProperty("name").GetString());
        Assert.Equal("Tomek", room.GetProperty("host").GetString());
    }

    [Fact]
    public async Task Rooms_whose_host_left_are_not_listed()
    {
        var (host, roomId) = await this.relay.Target.CreateRoomAsync(HostToken);
        host.Dispose();
        var deadline = DateTime.UtcNow + WireClient.Timeout;
        while ((await this.RoomsAsync()).Any(room => room.GetProperty("id").GetUInt64() == roomId))
        {
            Assert.True(DateTime.UtcNow < deadline, "The abandoned room stayed listed");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Only_get_rooms_is_served()
    {
        using var root = await this.http.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        using var room = await this.http.GetAsync(new Uri("/rooms/1", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, room.StatusCode);
        using var post = await this.http.PostAsync(new Uri("/rooms", UriKind.Relative), new StringContent("", Encoding.UTF8));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public void A_token_restricted_relay_refuses_to_serve_the_listing()
    {
        Assert.Throws<ArgumentException>(() => RelayApplication.Build(InProcessRelay.Loopback([HostToken]), httpPort: 0));
    }

    private async Task<(int Players, int Capacity)> OccupancyAsync(ulong roomId)
    {
        var room = (await this.RoomsAsync()).Single(room => room.GetProperty("id").GetUInt64() == roomId);
        Assert.Equal("Lobby", room.GetProperty("name").GetString());
        Assert.Equal("Host", room.GetProperty("host").GetString());
        return (room.GetProperty("players").GetInt32(), room.GetProperty("capacity").GetInt32());
    }

    private async Task<List<JsonElement>> RoomsAsync()
    {
        using var document = JsonDocument.Parse(await this.http.GetStringAsync(new Uri("/rooms", UriKind.Relative)));
        return document.RootElement.GetProperty("rooms").EnumerateArray().Select(room => room.Clone()).ToList();
    }
}
