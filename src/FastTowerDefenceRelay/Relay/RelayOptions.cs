using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Relay;

/// <summary>Configuration of one relay process.</summary>
internal sealed record RelayOptions
{
    /// <summary>IP address or host name to listen on.</summary>
    public string Listen { get; init; } = "0.0.0.0";

    /// <summary>TCP port of the relay protocol, zero picks a free port.</summary>
    public int Port { get; init; } = 40000;

    /// <summary>Authorized player tokens; none makes the relay public.</summary>
    public IReadOnlyList<RelayToken> PlayerTokens { get; init; } = [];

    public int MaxRooms { get; init; } = 64;

    /// <summary>Most requests and forwarded pairs handled at once.</summary>
    public int MaxConnections { get; init; } = 128;

    /// <summary>TCP port of the HTTP room listing, zero picks a free port, null disables it.</summary>
    public int? HttpPort { get; init; }

    /// <summary>The protocol version this relay accepts and advertises.</summary>
    public ProtocolVersion Protocol { get; init; } = new(22, 4);

    /// <summary>Time a new connection has to send its directory request.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Time a peer has to accept one frame.</summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool RequiresToken => this.PlayerTokens.Count > 0;

    /// <summary>Reject inconsistent settings before any socket is opened.</summary>
    /// <exception cref="ArgumentException">An invalid setting.</exception>
    public void Validate()
    {
        if (this.Port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentException($"invalid relay port {this.Port}");
        }
        if (this.HttpPort is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentException($"invalid HTTP port {this.HttpPort}");
        }
        if (this.HttpPort is > 0 && this.HttpPort == this.Port)
        {
            throw new ArgumentException("the HTTP port must differ from the relay port");
        }
        if (this.HttpPort is not null && this.RequiresToken)
        {
            throw new ArgumentException("the HTTP room listing requires a public relay, rooms of a token-restricted relay are private");
        }
        if (this.MaxRooms is < 1 or > Wire.MaxDirectoryRooms)
        {
            throw new ArgumentException($"the room limit must be between 1 and {Wire.MaxDirectoryRooms}");
        }
        if (this.MaxConnections < 1)
        {
            throw new ArgumentException("the connection limit must be positive");
        }
        if (this.Protocol.Minor < Wire.DirectoryMinor)
        {
            throw new ArgumentException($"the room directory requires protocol minor version {Wire.DirectoryMinor} or newer");
        }
        if (this.RequestTimeout <= TimeSpan.Zero || this.SendTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("timeouts must be positive");
        }
        for (var i = 0; i < this.PlayerTokens.Count; ++i)
        {
            if (this.PlayerTokens[i].IsZero)
            {
                throw new ArgumentException("relay player tokens must not be all zero");
            }
            for (var j = 0; j < i; ++j)
            {
                if (this.PlayerTokens[i].Equals(this.PlayerTokens[j]))
                {
                    throw new ArgumentException("relay player tokens must be unique");
                }
            }
        }
    }
}
