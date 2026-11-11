using System.Net;
using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Relay;
using FastTowerDefenceRelay.Tests.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastTowerDefenceRelay.Tests;

/// <summary>A relay host running inside the test process on free loopback ports.</summary>
internal sealed class InProcessRelay : IAsyncDisposable
{
    private readonly IHost host;

    private InProcessRelay(IHost host, RelayOptions options, bool listsRooms)
    {
        this.host = host;
        var endPoint = host.Services.GetRequiredService<RelayServer>().EndPoint;
        this.Target = new RelayTarget(endPoint, options.Protocol.Major, options.Protocol.Minor);
        if (listsRooms)
        {
            var address = host.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
            this.HttpBase = new Uri(address);
        }
    }

    public RelayTarget Target { get; }

    /// <summary>Base address of the HTTP room listing, when enabled.</summary>
    public Uri? HttpBase { get; }

    /// <summary>Start the console relay host.</summary>
    /// <param name="httpPort">Port of the HTTP room listing, zero picks a free one, null disables it.</param>
    public static async Task<InProcessRelay> StartAsync(RelayOptions options, int? httpPort = null)
    {
        var host = RelayApplication.Build(options, httpPort, logging => logging.SetMinimumLevel(LogLevel.Warning));
        await host.StartAsync();
        return new InProcessRelay(host, options, httpPort is not null);
    }

    /// <summary>Options for a loopback relay; tests override what they exercise.</summary>
    public static RelayOptions Loopback(IReadOnlyList<byte[]>? tokens = null) => new()
    {
        Listen = IPAddress.Loopback.ToString(),
        Port = 0,
        PlayerTokens = (tokens ?? []).Select(token => new RelayToken(token)).ToList(),
    };

    public async Task StopAsync() => await this.host.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await this.host.StopAsync();
        this.host.Dispose();
    }
}
