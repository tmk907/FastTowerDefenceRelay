using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Tests;

public sealed class CommandLineTests
{
    private static readonly string TokenA = new string('a', 63) + "A";
    private static readonly string TokenB = new string('b', 63) + "B";

    [Fact]
    public void Defaults_match_the_cpp_relay()
    {
        var options = CommandLine.Parse([]).Options!;
        Assert.Equal("0.0.0.0", options.Listen);
        Assert.Equal(40000, options.Port);
        Assert.Equal(64, options.MaxRooms);
        Assert.Equal(128, options.MaxConnections);
        Assert.Empty(options.PlayerTokens);
        Assert.Null(options.HttpPort);
        Assert.Equal(new ProtocolVersion(22, 4), options.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
    }

    [Fact]
    public void Options_accept_separate_and_inline_values()
    {
        var options = CommandLine.Parse(
        [
            "--listen=::1", "--port", "23456", "--max-rooms=4096", "--max-connections", "512",
            "--player-token", TokenA, $"--player-token={TokenB}", "--protocol", "23.2", "--request-timeout=5",
        ]).Options!;
        Assert.Equal("::1", options.Listen);
        Assert.Equal(23456, options.Port);
        Assert.Equal(4096, options.MaxRooms);
        Assert.Equal(512, options.MaxConnections);
        Assert.Equal([RelayToken.FromHex(TokenA), RelayToken.FromHex(TokenB)], options.PlayerTokens);
        Assert.Equal(new ProtocolVersion(23, 2), options.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RequestTimeout);
    }

    [Fact]
    public void The_http_port_enables_the_room_listing()
    {
        Assert.Equal(40080, CommandLine.Parse(["--http-port", "40080"]).Options!.HttpPort);
        Assert.Equal(40000, CommandLine.Parse(["--port=1", "--http-port=40000"]).Options!.HttpPort);
    }

    [Fact]
    public void Help_and_version_need_no_other_options()
    {
        Assert.True(CommandLine.Parse(["--port", "1", "--help"]).Help);
        Assert.True(CommandLine.Parse(["-h"]).Help);
        Assert.True(CommandLine.Parse(["--version"]).Version);
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "80suffix")]
    [InlineData("--port")]
    [InlineData("--listen=")]
    [InlineData("--max-rooms", "4097")]
    [InlineData("--max-rooms", "0")]
    [InlineData("--max-connections", "0")]
    [InlineData("--max-connections", "999999999999999999999999999999")]
    [InlineData("--http-port", "0")]
    [InlineData("--http-port", "40000")]
    [InlineData("--port", "40080", "--http-port", "40080")]
    [InlineData("--protocol", "22")]
    [InlineData("--protocol", "22.1")]
    [InlineData("--protocol", "22.x")]
    [InlineData("--request-timeout", "0")]
    [InlineData("--unknown")]
    [InlineData("--player-token", "xyz")]
    [InlineData("--player-token", "000000000000000000000000000000000000000000000000000000000000000z")]
    [InlineData("--player-token", "0000000000000000000000000000000000000000000000000000000000000000")]
    public void Invalid_options_are_rejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(args));
    }

    [Fact]
    public void Tokens_must_be_unique()
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--player-token", TokenA, "--player-token", TokenA.ToLowerInvariant()]));
    }

    [Fact]
    public void Rooms_of_a_token_restricted_relay_are_never_listed_over_http()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--player-token", TokenA, "--http-port", "40080"]));
        Assert.Contains("private", error.Message, StringComparison.Ordinal);
    }
}
