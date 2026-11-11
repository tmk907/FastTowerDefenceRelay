# HTTP room listing

A public relay started with `--http-port PORT` serves its open rooms as JSON on the same listen address, for web pages, bots and other tools.

```text
GET http://RELAY:PORT/rooms
```

```json
{
  "protocol": { "major": 22, "minor": 4 },
  "rooms": [
    { "id": 1, "name": "FVD Game", "host": "Host", "players": 2, "capacity": 4 }
  ]
}
```

| Field | Meaning |
|---|---|
| `protocol` | The relay's protocol version; game clients must have the same major to join. |
| `id` | Room ID, as in the game's room list. |
| `name`, `host` | Room and host name; invalid UTF-8 is replaced with U+FFFD. |
| `players` | The host plus every guest that joined the room. |
| `capacity` | The host plus every guest seat the host reserved. It grows while the host reserves its seats, right after creating the room. |

Rooms are ordered by ID.
They are the same rooms the game lists: full rooms and rooms whose host disconnected are left out.
The relay cannot see a guest leave before the match starts, so `players` may still count them; their seat cannot be joined again either.

The response has `Access-Control-Allow-Origin: *`, so web pages on any origin may read it, and `Cache-Control: no-store`.
Other paths answer 404 and other methods on `/rooms` answer 405.

Rooms of a token-restricted relay are private, so the relay refuses to start with both `--player-token` and `--http-port`.
