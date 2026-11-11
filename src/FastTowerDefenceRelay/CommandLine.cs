using System.Globalization;
using FastTowerDefenceRelay.Protocol;
using FastTowerDefenceRelay.Relay;

namespace FastTowerDefenceRelay;

/// <summary>
/// The relay command line, a superset of the options of the C++ openage-relay.
/// </summary>
internal static class CommandLine
{
    public const string Usage = """
        Usage: ftd-relay [options]
          --listen ADDRESS          Listen address (default: 0.0.0.0)
          --port PORT               TCP port (default: 40000)
          --player-token HEX        Authorized 32-byte token, repeatable (default: public)
          --max-rooms COUNT         Maximum advertised rooms (default: 64; max: 4096)
          --max-connections COUNT   Maximum concurrent connections (default: 128)
          --http-port PORT          Serve the room list as JSON at http://ADDRESS:PORT/rooms;
                                    public relays only (default: disabled)
          --protocol MAJOR.MINOR    Protocol version to accept and advertise (default: 22.4)
          --request-timeout SECONDS Time to send a directory request (default: 30)
          -h, --help                Show this help
          --version                 Show the relay version
        """;

    private static readonly string[] ValueOptions =
    [
        "--listen", "--port", "--player-token", "--max-rooms", "--max-connections",
        "--http-port", "--protocol", "--request-timeout",
    ];

    /// <summary>The parsed command: options to run with, or a request for help or the version.</summary>
    public sealed record Command(RelayOptions? Options, bool Help = false, bool Version = false);

    /// <exception cref="ArgumentException">Malformed or inconsistent options.</exception>
    public static Command Parse(IReadOnlyList<string> args)
    {
        var defaults = new RelayOptions();
        var listen = defaults.Listen;
        var port = defaults.Port;
        var tokens = new List<RelayToken>();
        var maxRooms = defaults.MaxRooms;
        var maxConnections = defaults.MaxConnections;
        int? httpPort = null;
        var protocol = defaults.Protocol;
        var requestTimeout = defaults.RequestTimeout;

        for (var index = 0; index < args.Count; ++index)
        {
            var argument = args[index];
            if (argument is "--help" or "-h")
            {
                return new Command(null, Help: true);
            }
            if (argument == "--version")
            {
                return new Command(null, Version: true);
            }

            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? argument : argument[..equals];
            if (!ValueOptions.Contains(name))
            {
                throw new ArgumentException($"unknown relay option: {argument}");
            }

            string value;
            if (equals >= 0)
            {
                value = argument[(equals + 1)..];
            }
            else if (index + 1 < args.Count)
            {
                value = args[++index];
            }
            else
            {
                throw new ArgumentException($"missing value for {name}");
            }
            if (value.Length == 0)
            {
                throw new ArgumentException($"empty value for {name}");
            }

            switch (name)
            {
                case "--listen":
                    listen = value;
                    break;
                case "--port":
                    port = ParsePositive(name, value, ushort.MaxValue);
                    break;
                case "--http-port":
                    httpPort = ParsePositive(name, value, ushort.MaxValue);
                    break;
                case "--max-rooms":
                    maxRooms = ParsePositive(name, value, Wire.MaxDirectoryRooms);
                    break;
                case "--max-connections":
                    maxConnections = ParsePositive(name, value, int.MaxValue);
                    break;
                case "--protocol":
                    protocol = ParseProtocol(value);
                    break;
                case "--request-timeout":
                    requestTimeout = TimeSpan.FromSeconds(ParsePositive(name, value, 3600));
                    break;
                default:
                    tokens.Add(ParseToken(value));
                    break;
            }
        }

        var options = new RelayOptions
        {
            Listen = listen,
            Port = port,
            PlayerTokens = tokens,
            MaxRooms = maxRooms,
            MaxConnections = maxConnections,
            HttpPort = httpPort,
            Protocol = protocol,
            RequestTimeout = requestTimeout,
        };
        options.Validate();
        return new Command(options);
    }

    private static int ParsePositive(string name, string value, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 1 || number > maximum)
        {
            throw new ArgumentException($"invalid {name}: {value}");
        }
        return number;
    }

    private static ProtocolVersion ParseProtocol(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 2
            || !ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            throw new ArgumentException($"invalid --protocol: {value}, expected MAJOR.MINOR");
        }
        return new ProtocolVersion(major, minor);
    }

    private static RelayToken ParseToken(string value)
    {
        try
        {
            return RelayToken.FromHex(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(exception.Message, exception);
        }
    }
}
