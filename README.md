# Fast Tower Defence relay

A relay server for Fast Tower Defence (FVD), the openage-based game.
Players connect out to the relay, so nobody needs port forwarding: hosts advertise rooms, guests join them, and the relay forwards the match traffic between them.

It is a drop-in replacement for the C++ `openage-relay`: same wire protocol, same command line, same game clients.
It also serves the open rooms as JSON over HTTP, and as a web page.

- [docs/wire-protocol.md](docs/wire-protocol.md): the TCP protocol between game and relay
- [docs/http-api.md](docs/http-api.md): the HTTP room listing

| Project | |
|---|---|
| `src/FastTowerDefenceRelay.Core` | the relay as a library: protocol, room directory, background service and room listing endpoint |
| `src/FastTowerDefenceRelay` | `ftd-relay`, the console relay |
| `src/FastTowerDefenceRelay.Web` | the relay as a background service of a web app with a lobby page |

## Console relay

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

## Web app

`FastTowerDefenceRelay.Web` runs the same relay in the background of an ASP.NET Core app.
Its page at `/` lists the open lobbies and refreshes every 10 seconds; `/rooms` is the [JSON API](docs/http-api.md) behind it.
The page can show a browser notification whenever a new lobby opens while it is open.
Browsers only allow notifications on pages served over HTTPS (or from `localhost`), so serve it through a reverse proxy with TLS.

The web app always lists its lobbies publicly, so it runs a public relay only.
The relay is configured in the `Relay` section of `appsettings.json`, `appsettings.Production.json` or environment variables such as `Relay__Port=40000`:

| Key | Default | |
|---|---|---|
| `Listen` | `0.0.0.0` | as `--listen` |
| `Port` | `40000` | TCP port for game clients |
| `MaxRooms` | `64` | at most 4096 |
| `MaxConnections` | `128` | requests in progress and forwarded pairs |
| `Protocol` | `22.4` | game protocol version to accept and advertise |
| `RequestTimeout` | `00:00:30` | time a new connection has to send its request |

The page itself listens where ASP.NET Core is told to, for example `ASPNETCORE_URLS=http://localhost:5130`; the container image uses port 8080.

### Deploying to a VPS

The `Build and deploy to VPS` workflow (`.github/workflows/vps.yml`, run manually) tests and publishes the web app, copies it with `src/FastTowerDefenceRelay.Web/Dockerfile` to `/srv/www/ftd-relay` on the VPS and runs it there in the `ftd-relay` container.
The Dockerfile has no build step; it only packages the published files.
The workflow needs the repository secrets `SERVER_HOST`, `SERVER_USERNAME` and `SERVER_KEY` (a private SSH key), and the VPS needs Docker.

The container publishes

- port 40000 for game clients on all interfaces; Docker bypasses `ufw`, so this port is reachable regardless of its rules,
- the web page on `127.0.0.1:5130`, for a reverse proxy to serve over HTTPS.

Settings that differ on the VPS go into `/srv/www/ftd-relay/appsettings.Production.json`, which each deployment copies into the app.
Stop an older relay that uses port 40000, such as a systemd service, before the first deployment.

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
- tests of this relay's own limits, shutdown, HTTP listing and web app.

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
