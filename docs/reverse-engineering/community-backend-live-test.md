# Community Backend Live Test

This is the first install-ready runbook for testing the community backend with the runtime redirector mod.

## Prerequisites

- `BepInEx` is already installed into the retail game.
- Proton launch options include:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

- The backend is reachable from the game machine at the configured base URL.

## Build the redirector mod

```bash
./.tmp/runtime-mod/dotnet/dotnet build mods/backend_redirector/BackendRedirector.csproj -c Release
```

Expected output:

```text
mods/backend_redirector/bin/Release/net6.0/SneakOut.BackendRedirector.dll
```

## Install the redirector mod

Copy the built plugin into the game:

```bash
cp -f \
  mods/backend_redirector/bin/Release/net6.0/SneakOut.BackendRedirector.dll \
  "/path/to/Sneak Out/BepInEx/plugins/SneakOut.BackendRedirector.dll"
```

The plugin config file is created here on first launch:

```text
/path/to/Sneak Out/BepInEx/config/chelokot.sneakout.backend-redirector.cfg
```

Current defaults:

- `EnableResearchLogging=false`
- `EnableLocalStub=false`
- `EnableRedirect=false`
- `TargetBaseUrl=http://127.0.0.1:8080`
- `TargetEnvironmentName=CommunityLocal`

## Start the backend

```bash
cd backend/community_server
COMMUNITY_BACKEND_LOG_REQUESTS=true deno task dev
```

Current stock-compatible routes already implemented:

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

## First live test

1. Start `community_server`.
2. Install `SneakOut.BackendRedirector.dll`.
3. Launch the game.
4. Watch `backend/community_server` logs for incoming stock routes.
5. Watch `BepInEx/LogOutput.log` for `Backend Redirector` entries.
6. Add missing endpoints only after observing the next failing request path.

## Expected current limitations

- The redirector only changes the backend base URL. It does not rewrite endpoint paths.
- The backend currently covers the first known auth/session/profile/metadata/product/match surface, not the full live service.
- `Nakama` and `Fusion` are still separate runtime layers and are not replaced by this backend.
