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

    private InProcessRelay(IHost host, RelayOptions options)
    {
        this.host = host;
        var endPoint = host.Services.GetRequiredService<RelayServer>().EndPoint;
        this.Target = new RelayTarget(endPoint, options.Protocol.Major, options.Protocol.Minor);
        if (options.HttpPort is not null)
        {
            var address = host.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
            this.HttpBase = new Uri(address);
        }
    }

    public RelayTarget Target { get; }

    /// <summary>Base address of the HTTP room listing, when enabled.</summary>
    public Uri? HttpBase { get; }

    public static async Task<InProcessRelay> StartAsync(RelayOptions options)
    {
        var host = RelayApplication.Build(options, logging => logging.SetMinimumLevel(LogLevel.Warning));
        await host.StartAsync();
        return new InProcessRelay(host, options);
    }

    /// <summary>Options for a loopback relay; tests override what they exercise.</summary>
    public static RelayOptions Loopback(IReadOnlyList<byte[]>? tokens = null, int? httpPort = null) => new()
    {
        Listen = IPAddress.Loopback.ToString(),
        Port = 0,
        PlayerTokens = (tokens ?? []).Select(token => new RelayToken(token)).ToList(),
        HttpPort = httpPort,
    };

    public async Task StopAsync() => await this.host.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await this.host.StopAsync();
        this.host.Dispose();
    }
}
