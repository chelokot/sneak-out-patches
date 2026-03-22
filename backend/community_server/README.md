# Community Backend

`Hono + Deno` MVP backend for private-lobby recovery and maxed-profile testing.

## Run

```bash
cd backend/community_server
deno task dev
```

Optional port:

```bash
COMMUNITY_BACKEND_PORT=8080 deno task dev
```

Verbose request logging:

```bash
COMMUNITY_BACKEND_LOG_REQUESTS=true deno task dev
```

## Endpoints

- `GET /health`
- `POST /v2/auth/steam-login`
- `GET /v2/session`
- `GET /v2/session/:userSessionToken`
- `POST /v2/player/refresh`
- `GET /v2/player/:userId/metadata`
- `PUT /v2/player/:userId/metadata/:key`
- `GET /v2/products`
- `POST /v2/matches`
- `PUT /v2/matches/:matchId`

## Stock client aliases

The server also accepts the stock client paths already discovered in the game metadata:

- `POST /api/authn/authorize/steamV2`
- `POST /api/authn/authorize/steamv2`
- `GET /api/meta/player/session`
- `GET /api/meta/player/session/`
- `GET /api/authn/session/:userSessionToken`
- `GET /api/authn/session/v2/:userSessionToken`
- `POST /api/meta/player/refresh_lobby_player`
- `GET /api/game/user/:userId/metadata`
- `GET /api/game/user/:userId/metadata/key/:key`
- `PUT /api/game/user/:userId/metadata/key/:key`
- `GET /api/meta/products`
- `GET /api/service/check-alive`
- `GET /api/server`
- `POST /api/match`
- `PUT /api/match/:matchId`

These stock aliases return wrapped `{ success, error, responseCode, result }` payloads, while the
`/v2/...` routes return the direct JSON models used in tests.

## Tasks

```bash
deno task check
deno task test
deno task fmt
```
