# Fast Tower Defence relay

A relay server for Fast Tower Defence (FVD), the openage-based game.
Players connect out to the relay, so nobody needs port forwarding: hosts advertise rooms, guests join them, and the relay forwards the match traffic between them.

It is a drop-in replacement for the C++ `openage-relay`: same wire protocol, same command line, same game clients.
It also serves the open rooms as JSON over HTTP.

- [docs/wire-protocol.md](docs/wire-protocol.md): the TCP protocol between game and relay
- [docs/http-api.md](docs/http-api.md): the HTTP room listing

## Run

Download the executable for your platform from the CI artifacts, or build it as below.

```sh
ftd-relay --listen 0.0.0.0 --port 40000 --http-port 40080
```

| Option | Default | |
|---|---|---|
| `--listen ADDRESS` | `0.0.0.0` | IP address or host name; `::` listens on IPv6 and IPv4 |
| `--port PORT` | `40000` | TCP port for game clients |
| `--player-token HEX` | public | 64 hexadecimal characters, repeatable; restricts the relay to these tokens |
| `--max-rooms COUNT` | `64` | at most 4096 |
| `--max-connections COUNT` | `128` | requests in progress and forwarded pairs |
| `--http-port PORT` | disabled | serve `GET /rooms`; public relays only |
| `--protocol MAJOR.MINOR` | `22.4` | game protocol version to accept and advertise |
| `--request-timeout SECONDS` | `30` | time a new connection has to send its request |

Open the TCP port, and the HTTP port if used, in the firewall.
Ctrl+C or SIGTERM stops the relay and closes all rooms and matches.

When the game's protocol version changes, restart the relay with the new `--protocol`; game messages it does not know are forwarded unchanged.

Tokens and gameplay travel unencrypted, so treat tokens as an access filter, not as a security boundary.

## Build and test

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet publish src/FastTowerDefenceRelay -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The tests are split into

- unit tests of the codecs and the command line, checked against byte layouts from the specification,
- conformance tests (`--filter Category=Conformance`) that drive a relay through a client written independently from the specification,
- tests of this relay's own limits, shutdown and HTTP listing.

The conformance tests start this relay in-process by default.
To run them against another relay, such as the C++ reference, start a public relay and one restricted to the tokens in `tests/FastTowerDefenceRelay.Tests/Conformance/ConformanceRelays.cs`, then set

```sh
FTD_CONFORMANCE_PUBLIC_RELAY=127.0.0.1:41100
FTD_CONFORMANCE_PRIVATE_RELAY=127.0.0.1:41101
FTD_CONFORMANCE_PROTOCOL=22.4
```

CI does this on every push, against the relay built from the openage fork's `FVD` branch, so this relay, the specification and the reference cannot drift apart unnoticed.

## License

GPL-3.0-or-later, like openage, from which this relay is ported; see [LICENSE](LICENSE).
