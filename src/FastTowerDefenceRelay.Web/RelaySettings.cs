using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Relay;

namespace FastTowerDefenceRelay.Web;

/// <summary>
/// The "Relay" configuration section.
/// </summary>
/// <remarks>
/// The web app always lists its rooms publicly, so it has no player tokens:
/// rooms of a token-restricted relay are private.
/// </remarks>
public sealed class RelaySettings
{
    private static readonly RelayOptions Defaults = new();

    public string Listen { get; set; } = Defaults.Listen;

    public int Port { get; set; } = Defaults.Port;

    public int MaxRooms { get; set; } = Defaults.MaxRooms;

    public int MaxConnections { get; set; } = Defaults.MaxConnections;

    /// <summary>MAJOR.MINOR of the game protocol to accept and advertise.</summary>
    public string Protocol { get; set; } = Defaults.Protocol.ToString();

    public TimeSpan RequestTimeout { get; set; } = Defaults.RequestTimeout;

    /// <exception cref="FormatException">An invalid protocol version.</exception>
    public RelayOptions ToOptions() => new()
    {
        Listen = this.Listen,
        Port = this.Port,
        MaxRooms = this.MaxRooms,
        MaxConnections = this.MaxConnections,
        Protocol = ProtocolVersion.Parse(this.Protocol),
        RequestTimeout = this.RequestTimeout,
    };
}
