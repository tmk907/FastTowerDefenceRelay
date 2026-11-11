using System.Net;
using System.Text.Json;
using FastTowerDefenceRelay.Relay;
using FastTowerDefenceRelay.Tests.Client;
using FastTowerDefenceRelay.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FastTowerDefenceRelay.Tests.Http;

/// <summary>The web app: the relay as a background service, its lobby page and API.</summary>
public sealed class WebAppTests : IDisposable
{
    private static readonly byte[] HostToken = Enumerable.Repeat((byte)9, 32).ToArray();

    private readonly WebApplicationFactory<RelaySettings> factory = new WebApplicationFactory<RelaySettings>()
        .WithWebHostBuilder(web => web
            .UseSetting("Relay:Listen", IPAddress.Loopback.ToString())
            .UseSetting("Relay:Port", "0"));

    public void Dispose() => this.factory.Dispose();

    [Fact]
    public async Task The_lobby_page_and_its_assets_are_served()
    {
        using var http = this.factory.CreateClient();

        using var page = await http.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<script src=\"app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"notifications\"", html, StringComparison.Ordinal);

        foreach (var (asset, mediaType) in new[] { ("app.js", "text/javascript"), ("site.css", "text/css") })
        {
            using var response = await http.GetAsync(new Uri(asset, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task The_api_lists_lobbies_hosted_on_the_background_relay()
    {
        using var http = this.factory.CreateClient();
        var relay = this.factory.Services.GetRequiredService<RelayServer>();
        var options = this.factory.Services.GetRequiredService<RelayOptions>();
        var target = new RelayTarget(relay.EndPoint, options.Protocol.Major, options.Protocol.Minor);

        Assert.Empty(await RoomsAsync(http));

        var (host, roomId) = await target.CreateRoomAsync(HostToken, "Evening game", "Tomek");
        using (host)
        {
            var room = Assert.Single(await RoomsAsync(http));
            Assert.Equal(roomId, room.GetProperty("id").GetUInt64());
            Assert.Equal("Evening game", room.GetProperty("name").GetString());
            Assert.Equal("Tomek", room.GetProperty("host").GetString());
            Assert.Equal(1, room.GetProperty("players").GetInt32());
            Assert.Equal(2, room.GetProperty("capacity").GetInt32());
        }
    }

    [Fact]
    public void Relay_settings_map_to_relay_options()
    {
        var options = new RelaySettings { Port = 41000, MaxRooms = 5, Protocol = "23.2", RequestTimeout = TimeSpan.FromSeconds(3) }.ToOptions();
        Assert.Equal(new RelayOptions
        {
            Port = 41000,
            MaxRooms = 5,
            Protocol = new Protocol.ProtocolVersion(23, 2),
            RequestTimeout = TimeSpan.FromSeconds(3),
            PlayerTokens = options.PlayerTokens,
        }, options);
        Assert.Throws<FormatException>(() => new RelaySettings { Protocol = "latest" }.ToOptions());
    }

    private static async Task<List<JsonElement>> RoomsAsync(HttpClient http)
    {
        using var document = JsonDocument.Parse(await http.GetStringAsync(new Uri("/rooms", UriKind.Relative)));
        return document.RootElement.GetProperty("rooms").EnumerateArray().Select(room => room.Clone()).ToList();
    }
}
