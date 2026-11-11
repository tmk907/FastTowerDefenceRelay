# FVD relay wire protocol

This is the protocol between Fast Tower Defence (FVD) game clients and a relay.
It covers the frame format, the room directory a relay offers before a match, and forwarding during a match.
The reference implementation is the C++ relay in openage (`libopenage/network/relay.cpp`, `protocol.cpp`); this repository's relay and its conformance suite follow it.

A relay lets every player make outbound TCP connections only, so no player needs port forwarding.
The authoritative player, the *host*, holds one relay connection per remote player, and each *guest* holds one connection to the host.
The relay pairs these connections and then forwards frames between them, without reading their payloads.

The key words "must", "must not", "should" and "may" are used as in RFC 2119.

## Conventions

- All integers are unsigned and big-endian (network byte order).
- `u16`, `u32` and `u64` are 2, 4 and 8 bytes wide.
- `token` is 32 raw bytes.
- `text` is a `u32` byte length followed by that many bytes of UTF-8. Directory text must be non-empty, must not exceed its field's limit in bytes and must not contain bytes below `0x20` or the byte `0x7F`. Relays store and return text as raw bytes and do not validate the UTF-8 itself.

## Transport

Frames travel over one TCP connection per host/guest channel.
Relays disable Nagle's algorithm on their sockets so small frames go out at once.
Neither TLS nor any other encryption is used: tokens and gameplay are visible to anyone on the path.

## Frames

Every message is one frame: a fixed 32-byte header followed by the payload.

| Offset | Size | Field | Value |
|---:|---:|---|---|
| 0 | 4 | magic | `0x4656444E`, ASCII `FVDN` |
| 4 | 2 | protocol major | see [Versions](#versions) |
| 6 | 2 | protocol minor | see [Versions](#versions) |
| 8 | 2 | message type | see [Message types](#message-types) |
| 10 | 2 | flags | must be `0` |
| 12 | 4 | payload size | at most 1 048 576 (1 MiB) |
| 16 | 8 | session ID | `0` in every relay directory frame |
| 24 | 8 | sequence | `0` in every relay directory frame |
| 32 | n | payload | `payload size` bytes |

A receiver must treat a wrong magic, nonzero flags or a payload size above 1 MiB as a protocol violation.
Example: a PING of protocol 22.4 with session `0x0102030405060708`, sequence 9 and payload `01 02 03` is

```text
4656444E 0016 0004 000B 0000 00000003 0102030405060708 0000000000000009 010203
```

## Versions

Each frame carries the sender's protocol version.
A new major version is incompatible; a new minor version adds compatible features.
The current game protocol is **22.4**.
Minor versions gate relay features:

| Minor | Introduced |
|---:|---|
| 1 | relay admission |
| 2 | room directory |
| 3 | additional host seats (`RELAY_ATTACH_ROOM`) |
| 4 | Hunter rehost proofs (between players, not interpreted by the relay) |

A relay is configured with one version, its *relay version*.
It accepts a directory request only if

- the request major equals the relay major,
- the request minor is at least 2 and at most the relay minor,
- session ID and sequence are both 0.

Every frame the relay itself sends, its responses and heartbeats, carries the relay version with session ID and sequence 0.
Clients check this version: the game rejects a response whose major differs from its own or whose minor exceeds its own.
So a relay must advertise exactly the game's major version and a minor no newer than its clients'.

## Message types

| Type | Name | Direction |
|---:|---|---|
| 1–13 | game messages (`CLIENT_HELLO` … `SNAPSHOT_REQUEST`) | player ↔ player |
| 11 | `PING` | also relay → waiting host, as a heartbeat |
| 14 | `RELAY_CONNECT` | provisioned pairs; not served by the directory |
| 15 | `RELAY_READY` | relay → host and guest |
| 16 | `RELAY_REJECT` | relay → client |
| 17 | `MATCH_READY` | player ↔ player |
| 18 | `RELAY_CREATE_ROOM` | host → relay |
| 19 | `RELAY_ROOM_CREATED` | relay → host |
| 20 | `RELAY_LIST_ROOMS` | client → relay |
| 21 | `RELAY_ROOM_LIST` | relay → client |
| 22 | `RELAY_JOIN_ROOM` | guest → relay |
| 23 | `RELAY_ATTACH_ROOM` | host → relay |
| 24 | `RELAY_ROOM_ATTACHED` | relay → host |
| 25–26 | `AUTHORITY_REHOST_OFFER`, `AUTHORITY_REHOST_PROOF` | player ↔ player |

## Rooms and seats

A *room* is advertised by a host and identified by a nonzero `u64` room ID that the relay assigns.
Room IDs identify rooms; they are not credentials.

A room has *host seats*.
Each seat is one waiting host connection, numbered by the player slot the guest will get: the creating connection is slot 2, and each `RELAY_ATTACH_ROOM` reserves the next slot, 3 up to 8.
A guest joining takes the lowest free seat, and that host connection and the guest connection become a *pair*.
A room stays listed until its last seat is taken or every remaining host connection has closed.

## Access

A relay is either public or token-restricted.

- A **public** relay accepts every token. Clients conventionally send the all-zero token.
- A **token-restricted** relay accepts only its configured tokens in every listing, creation and join. Additionally, a guest must not use the token that created the room, and each token takes at most one seat per room.

Seats can only be reserved with the token that created the room, and never with the all-zero token.
So a host that wants several seats on a public relay must create its room with a random nonzero token and keep it secret.

## Directory session

Each new connection carries exactly one directory request as its first frame.
The relay answers it and then

- closes the connection after a listing or any rejection,
- keeps the connection as a host seat after a room creation or attachment,
- pairs the connection after a successful join and starts forwarding.

A relay may close a connection that does not send its request within a timeout; this relay uses 30 seconds by default and answers with a rejection.

### List rooms

Request `RELAY_LIST_ROOMS`: `token`, exactly 32 bytes.

Response `RELAY_ROOM_LIST`:

```text
u32  count
count × { u64 room ID, text room name, text host name }
```

Rooms are ordered by ascending ID.
Before answering, the relay sends a heartbeat to every waiting host seat and drops seats whose host has disconnected, so abandoned rooms disappear.

### Create a room

Request `RELAY_CREATE_ROOM`:

```text
token  creator token
text   room name, 1–64 bytes
text   host name, 1–128 bytes
```

No bytes may follow.
Response `RELAY_ROOM_CREATED`: `u64` room ID.
The connection is now the room's seat for slot 2 and waits for `RELAY_READY`.

### Reserve another host seat

Request `RELAY_ATTACH_ROOM` with minor version 3 or newer: `token` followed by `u64` room ID, exactly 40 bytes; the room ID must be nonzero.

Response `RELAY_ROOM_ATTACHED`: `u64` room ID, `u64` reserved slot (3–8).
The connection is now that seat and waits for `RELAY_READY`.

### Join a room

Request `RELAY_JOIN_ROOM`: `token` followed by `u64` room ID, exactly 40 bytes; the room ID must be nonzero.

On success the relay sends `RELAY_READY` with `u64` room ID and `u64` slot (2–8) to the seat's host connection first and then to the guest.
Both connections are now a pair, and the next frames on them are game frames.

### Heartbeats

While a host connection waits as a seat, the relay may send it `PING` frames with an empty payload and the relay version.
A waiting host must skip them.
After `RELAY_READY` the relay sends no further heartbeats on that connection.

### Rejections

`RELAY_REJECT` carries `u32` reason length and up to 4096 bytes of UTF-8 reason.
The relay closes the connection after it; clients show the reason to the player.
The reasons are:

| Situation | Reason |
|---|---|
| connection limit reached | `relay connection capacity is full` |
| no request in time | `relay directory request timed out` |
| invalid header or envelope | `malformed relay directory request` |
| a type other than list, create, attach or join | `unsupported relay directory request` |
| token not accepted | `player token is not authorized` |
| bad `RELAY_LIST_ROOMS` payload | `malformed room listing request` |
| bad `RELAY_CREATE_ROOM` payload | `malformed room creation request` |
| room limit reached | `relay room directory is full` |
| bad `RELAY_ATTACH_ROOM` payload | `malformed host room attachment` |
| attachment with a minor below 3 | `multi-guest room attachment requires protocol minor 3` |
| unknown room, wrong or zero creator token, or all seats reserved | `invalid or full host room attachment` |
| bad `RELAY_JOIN_ROOM` payload | `malformed room join request` |
| unknown or removed room | `relay room is no longer available` |
| guest uses the creator token (restricted relay) | `host and guest must use different player tokens` |
| token already seated in this room (restricted relay) | `player token is already admitted to this room` |
| every seat's host has disconnected | `relay room host has disconnected or has no free channels` |
| host vanished while being paired | `relay room host disconnected before join` |

A payload is checked before the protocol minor of an attachment, and the token before the room.

## Forwarding

The relay forwards every complete frame unchanged in both directions until either side closes, then closes both.
It requires a valid header, magic, zero flags and size, but does not interpret the version, type, session, sequence or payload.
One pair never affects another, and each keeps its own frame order.

The reference implementation additionally closes a pair on a message type it does not know.
This relay forwards any type, so new game messages need no relay update; clients must not rely on either behavior.

## Limits

| Limit | Value |
|---|---|
| frame payload | 1 MiB |
| room name | 64 bytes |
| participant name | 128 bytes |
| rejection reason | 4096 bytes |
| rooms per relay | at most 4096, configurable, 64 by default |
| seats per room | 7 (slots 2–8) |
| concurrent connections | configurable, 128 by default |

Requests being processed and forwarded pairs count against the connection limit, while waiting host seats do not.
A relay must not block indefinitely on a peer that does not read: this relay gives up on a frame after 15 seconds.

## HTTP room listing

Public relays may also serve their rooms over HTTP, see [http-api.md](http-api.md).
This is not part of the TCP protocol.
