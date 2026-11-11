using System.Globalization;
using System.Net;
using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Relay;
using FastTowerDefenceRelay.Tests.Client;

namespace FastTowerDefenceRelay.Tests.Conformance;

/// <summary>
/// A public and a token-restricted relay for the conformance suite.
/// </summary>
/// <remarks>
/// By default both run in-process. Setting FTD_CONFORMANCE_PUBLIC_RELAY and
/// FTD_CONFORMANCE_PRIVATE_RELAY to HOST:PORT runs the suite against external
/// relays instead, such as the C++ reference relay. The private relay must
/// authorize exactly <see cref="HostToken"/>, <see cref="GuestToken"/> and
/// <see cref="SecondGuestToken"/>. FTD_CONFORMANCE_PROTOCOL gives their
/// protocol version as MAJOR.MINOR.
/// </remarks>
public sealed class ConformanceRelays : IAsyncLifetime
{
    public static readonly byte[] HostToken = Token(0x11);
    public static readonly byte[] GuestToken = Token(0x22);
    public static readonly byte[] SecondGuestToken = Token(0x33);

    /// <summary>A nonzero host token for the public relay, needed to reserve more seats.</summary>
    public static readonly byte[] PublicHostToken = Token(0x44);

    public static readonly byte[] ZeroToken = new byte[32];

    private InProcessRelay? publicRelay;
    private InProcessRelay? privateRelay;

    internal RelayTarget Public { get; private set; } = null!;

    internal RelayTarget Private { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var protocol = Environment.GetEnvironmentVariable("FTD_CONFORMANCE_PROTOCOL") is { Length: > 0 } version
            ? ParseProtocol(version)
            : new RelayOptions().Protocol;
        var publicEndPoint = Environment.GetEnvironmentVariable("FTD_CONFORMANCE_PUBLIC_RELAY");
        var privateEndPoint = Environment.GetEnvironmentVariable("FTD_CONFORMANCE_PRIVATE_RELAY");
        if (!string.IsNullOrEmpty(publicEndPoint) && !string.IsNullOrEmpty(privateEndPoint))
        {
            this.Public = new RelayTarget(IPEndPoint.Parse(publicEndPoint), protocol.Major, protocol.Minor);
            this.Private = new RelayTarget(IPEndPoint.Parse(privateEndPoint), protocol.Major, protocol.Minor);
            return;
        }

        this.publicRelay = await InProcessRelay.StartAsync(new RelayOptions
        {
            Listen = IPAddress.Loopback.ToString(),
            Port = 0,
            Protocol = protocol,
        });
        this.privateRelay = await InProcessRelay.StartAsync(new RelayOptions
        {
            Listen = IPAddress.Loopback.ToString(),
            Port = 0,
            Protocol = protocol,
            PlayerTokens = [new(HostToken), new(GuestToken), new(SecondGuestToken)],
        });
        this.Public = this.publicRelay.Target;
        this.Private = this.privateRelay.Target;
    }

    public async Task DisposeAsync()
    {
        if (this.publicRelay is not null)
        {
            await this.publicRelay.DisposeAsync();
        }
        if (this.privateRelay is not null)
        {
            await this.privateRelay.DisposeAsync();
        }
    }

    private static byte[] Token(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private static ProtocolVersion ParseProtocol(string version)
    {
        var parts = version.Split('.');
        return new ProtocolVersion(
            ushort.Parse(parts[0], CultureInfo.InvariantCulture),
            ushort.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}

[CollectionDefinition(Name)]
public sealed class ConformanceGroup : ICollectionFixture<ConformanceRelays>
{
    public const string Name = "Conformance";
}
