import { Hono } from "@hono/hono";
import type { Context } from "@hono/hono";
import type { CreateMatchRequest, SteamLoginRequest, UpdateMatchRequest } from "./contracts.ts";
import { CommunityBackendStore } from "./store.ts";

type AppBindings = {
  Variables: {
    store: CommunityBackendStore;
  };
};

type AppContext = Context<AppBindings>;

function badRequest(message: string) {
  return Response.json({ error: message }, { status: 400 });
}

function unauthorized() {
  return Response.json({ error: "Unauthorized" }, { status: 401 });
}

function notFound(message: string) {
  return Response.json({ error: message }, { status: 404 });
}

function okResult<T>(payload: T) {
  return {
    success: true,
    error: null,
    responseCode: "200",
    result: payload,
  };
}

function failResult(status: number, message: string) {
  return Response.json(
    {
      success: false,
      error: message,
      responseCode: String(status),
      result: null,
    },
    { status },
  );
}

function resolveAuthorizationToken(authorizationHeader: string | undefined) {
  if (!authorizationHeader) {
    return null;
  }

  const bearerPrefix = "Bearer ";
  return authorizationHeader.startsWith(bearerPrefix)
    ? authorizationHeader.slice(bearerPrefix.length)
    : authorizationHeader;
}

function buildRefreshPayload(
  player: ReturnType<CommunityBackendStore["getPlayerByUserSessionToken"]>,
) {
  if (!player) {
    return null;
  }

  return {
    userId: player.userId,
    displayName: player.displayName,
    currencies: player.currencies,
    progression: player.progression,
    equipped: player.equipped,
    owned: player.owned,
    inventory: player.inventory,
    skills: player.skills,
  };
}

export function createApp(store = new CommunityBackendStore()) {
  const app = new Hono<AppBindings>();
  const logRequests = Deno.env.get("COMMUNITY_BACKEND_LOG_REQUESTS") === "true";

  app.use(async (context, next) => {
    context.set("store", store);
    const startedAt = performance.now();
    await next();
    if (logRequests) {
      const elapsedMs = Math.round((performance.now() - startedAt) * 100) / 100;
      console.log(
        `${context.req.method} ${
          new URL(context.req.url).pathname
        } -> ${context.res.status} (${elapsedMs} ms)`,
      );
    }
  });

  app.get("/health", (context) => {
    return context.json({ status: "ok" });
  });

  const handleSteamLogin = async (context: AppContext) => {
    const body = await context.req.json() as SteamLoginRequest;
    if (!body.steamId?.trim()) {
      return badRequest("steamId is required");
    }

    return context.json(store.loginWithSteam(body));
  };

  const handleWrappedSteamLogin = async (context: AppContext) => {
    const body = await context.req.json() as SteamLoginRequest;
    if (!body.steamId?.trim()) {
      return failResult(400, "steamId is required");
    }

    return context.json(okResult(store.loginWithSteam(body)));
  };

  app.post("/v2/auth/steam-login", handleSteamLogin);
  app.post("/api/authn/authorize/steamV2", handleWrappedSteamLogin);
  app.post("/api/authn/authorize/steamv2", handleWrappedSteamLogin);

  const handleSessionByAuthorizationHeader = (context: AppContext) => {
    const authorizationToken = resolveAuthorizationToken(
      context.req.header("authorization"),
    );
    if (!authorizationToken) {
      return unauthorized();
    }

    const session = store.getSessionByAuthorizationToken(authorizationToken);
    if (!session) {
      return unauthorized();
    }

    return context.json(session);
  };

  const handleWrappedSessionByAuthorizationHeader = (context: AppContext) => {
    const authorizationToken = resolveAuthorizationToken(
      context.req.header("authorization"),
    );
    if (!authorizationToken) {
      return failResult(401, "Unauthorized");
    }

    const session = store.getSessionByAuthorizationToken(authorizationToken);
    if (!session) {
      return failResult(401, "Unauthorized");
    }

    return context.json(okResult(session));
  };

  app.get("/v2/session", handleSessionByAuthorizationHeader);
  app.get("/api/meta/player/session", handleWrappedSessionByAuthorizationHeader);
  app.get("/api/meta/player/session/", handleWrappedSessionByAuthorizationHeader);

  const handleSessionByUserSessionToken = (context: AppContext) => {
    const userSessionToken = context.req.param("userSessionToken") as string;
    const session = store.getSessionByUserSessionToken(userSessionToken);
    if (!session) {
      return unauthorized();
    }

    return context.json(session);
  };

  const handleWrappedSessionByUserSessionToken = (context: AppContext) => {
    const userSessionToken = context.req.param("userSessionToken") as string;
    const session = store.getSessionByUserSessionToken(userSessionToken);
    if (!session) {
      return failResult(401, "Unauthorized");
    }

    return context.json(okResult(session));
  };

  app.get("/v2/session/:userSessionToken", handleSessionByUserSessionToken);
  app.get("/api/authn/session/:userSessionToken", handleWrappedSessionByUserSessionToken);
  app.get("/api/authn/session/v2/:userSessionToken", handleWrappedSessionByUserSessionToken);

  const handlePlayerRefresh = async (context: AppContext) => {
    const body = await context.req.json() as { userSessionToken: string };
    if (!body.userSessionToken?.trim()) {
      return badRequest("userSessionToken is required");
    }

    const refreshPayload = buildRefreshPayload(
      store.getPlayerByUserSessionToken(body.userSessionToken),
    );
    if (!refreshPayload) {
      return unauthorized();
    }

    return context.json(refreshPayload);
  };

  const handleWrappedPlayerRefresh = async (context: AppContext) => {
    const body = await context.req.json() as { userSessionToken: string };
    if (!body.userSessionToken?.trim()) {
      return failResult(400, "userSessionToken is required");
    }

    const refreshPayload = buildRefreshPayload(
      store.getPlayerByUserSessionToken(body.userSessionToken),
    );
    if (!refreshPayload) {
      return failResult(401, "Unauthorized");
    }

    return context.json(okResult(refreshPayload));
  };

  app.post("/v2/player/refresh", handlePlayerRefresh);
  app.post("/api/meta/player/refresh_lobby_player", handleWrappedPlayerRefresh);
  app.get("/api/meta/player/refresh_lobby_player", (context) => {
    const authorizationToken = resolveAuthorizationToken(context.req.header("authorization"));
    if (!authorizationToken) {
      return failResult(401, "Unauthorized");
    }

    const refreshPayload = buildRefreshPayload(
      store.getPlayerByAuthorizationToken(authorizationToken),
    );
    if (!refreshPayload) {
      return failResult(401, "Unauthorized");
    }

    return context.json(okResult(refreshPayload));
  });

  app.get("/v2/player/:userId/metadata", (context) => {
    const metadata = store.getPlayerMetadata(Number(context.req.param("userId")));
    if (!metadata) {
      return notFound("Player metadata was not found");
    }

    return context.json(metadata);
  });

  app.get("/api/game/user/:userId/metadata", (context) => {
    const metadata = store.getPlayerMetadata(Number(context.req.param("userId")));
    if (!metadata) {
      return failResult(404, "Player metadata was not found");
    }

    return context.json(okResult(metadata));
  });

  app.get("/api/game/user/:userId/metadata/key/:key", (context) => {
    const metadata = store.getPlayerMetadata(Number(context.req.param("userId")));
    if (!metadata) {
      return failResult(404, "Player metadata was not found");
    }

    return context.json(
      okResult({
        key: context.req.param("key"),
        value: metadata[context.req.param("key")] ?? null,
      }),
    );
  });

  app.put("/v2/player/:userId/metadata/:key", async (context) => {
    const userId = Number(context.req.param("userId"));
    const key = context.req.param("key");
    const body = await context.req.json() as { value: string };
    if (!body.value) {
      return badRequest("value is required");
    }

    const metadata = store.setPlayerMetadata(userId, key, body.value);
    if (!metadata) {
      return notFound("Player metadata was not found");
    }

    return context.json(metadata);
  });

  app.put("/api/game/user/:userId/metadata/key/:key", async (context) => {
    const userId = Number(context.req.param("userId"));
    const key = context.req.param("key");
    const body = await context.req.json() as { value: string };
    if (!body.value) {
      return failResult(400, "value is required");
    }

    const metadata = store.setPlayerMetadata(userId, key, body.value);
    if (!metadata) {
      return failResult(404, "Player metadata was not found");
    }

    return context.json(okResult(metadata));
  });

  app.get("/v2/products", (context) => {
    return context.json(store.listProducts());
  });

  app.get("/api/meta/products", (context) => {
    return context.json(okResult(store.listProducts()));
  });

  app.get("/api/meta/player/messages", (context) => {
    return context.json(okResult([]));
  });

  app.get("/api/game", (context) => {
    return context.json(
      okResult({
        id: 1,
        key: "sneakout",
        name: "Sneak Out",
        channel: "Release",
      }),
    );
  });

  app.get("/api/photon/ccu", (context) => {
    return context.json(
      okResult({
        currentPlayers: 0,
        peakPlayers: 0,
      }),
    );
  });

  app.get("/api/score", (context) => {
    return context.json(
      okResult({
        scores: [],
      }),
    );
  });

  app.get("/api/steam/finalize_my_transactions", (context) => {
    return context.json(okResult(true));
  });

  app.get("/api/meta/player/user-simplified/:userId", (context) => {
    const userId = Number(context.req.param("userId"));
    const metadata = store.getPlayerMetadata(userId);
    return context.json(
      okResult({
        userId,
        displayName: metadata ? `Player-${userId}` : `Unknown-${userId}`,
      }),
    );
  });

  app.get("/api/user/steam/all-data", (context) => {
    const steamIds = context.req.queries("id") ?? [];
    return context.json(
      okResult(
        steamIds.map((steamId, index) => ({
          userId: index + 1,
          steamId,
          displayName: `Player-${steamId}`,
        })),
      ),
    );
  });

  app.get("/api/service/check-alive", (context) => {
    return context.json(okResult(true));
  });

  app.get("/api/server", (context) => {
    const aliveOnly = context.req.query("aliveOnly") === "true";
    return context.json(okResult(store.listServers(aliveOnly)));
  });

  app.post("/v2/matches", async (context) => {
    const body = await context.req.json() as CreateMatchRequest;
    if (!body.userSessionToken?.trim()) {
      return badRequest("userSessionToken is required");
    }
    if (!body.gameMode?.trim()) {
      return badRequest("gameMode is required");
    }
    if (!body.sceneType?.trim()) {
      return badRequest("sceneType is required");
    }

    const match = store.createMatch(body);
    if (!match) {
      return unauthorized();
    }

    return context.json(match, 201);
  });

  app.post("/api/match", async (context) => {
    const body = await context.req.json() as CreateMatchRequest;
    if (!body.userSessionToken?.trim()) {
      return failResult(400, "userSessionToken is required");
    }
    if (!body.gameMode?.trim()) {
      return failResult(400, "gameMode is required");
    }
    if (!body.sceneType?.trim()) {
      return failResult(400, "sceneType is required");
    }

    const match = store.createMatch(body);
    if (!match) {
      return failResult(401, "Unauthorized");
    }

    return context.json(okResult(match), 201);
  });

  app.put("/v2/matches/:matchId", async (context) => {
    const body = await context.req.json() as Omit<UpdateMatchRequest, "matchId">;
    const updatedMatch = store.updateMatch({
      matchId: Number(context.req.param("matchId")),
      ...body,
    });
    if (!updatedMatch) {
      return notFound("Match was not found");
    }

    return context.json(updatedMatch);
  });

  app.put("/api/match/:matchId", async (context) => {
    const body = await context.req.json() as Omit<UpdateMatchRequest, "matchId">;
    const updatedMatch = store.updateMatch({
      matchId: Number(context.req.param("matchId")),
      ...body,
    });
    if (!updatedMatch) {
      return failResult(404, "Match was not found");
    }

    return context.json(okResult(updatedMatch));
  });

  app.notFound((context) => {
    return context.json(
      {
        error: "Not found",
        method: context.req.method,
        path: new URL(context.req.url).pathname,
      },
      404,
    );
  });

  return app;
}
