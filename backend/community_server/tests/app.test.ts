import { assertEquals, assertExists } from "@std/assert";
import { createApp } from "../src/app.ts";
import { CommunityBackendStore } from "../src/store.ts";

Deno.test("steam login creates session and maxed player flow", async () => {
  const app = createApp(new CommunityBackendStore());

  const loginResponse = await app.request("/v2/auth/steam-login", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      steamId: "76561198000000000",
      displayName: "Chelokot",
    }),
  });

  assertEquals(loginResponse.status, 200);
  const loginPayload = await loginResponse.json();
  assertEquals(loginPayload.steamId, "76561198000000000");
  assertExists(loginPayload.authorizationToken);
  assertExists(loginPayload.userSessionToken);

  const sessionResponse = await app.request("/v2/session", {
    headers: {
      authorization: `Bearer ${loginPayload.authorizationToken}`,
    },
  });

  assertEquals(sessionResponse.status, 200);
  const sessionPayload = await sessionResponse.json();
  assertEquals(sessionPayload.displayName, "Chelokot");
  assertEquals(sessionPayload.gameUserMetadata.can_be_seeker, "true");

  const refreshResponse = await app.request("/v2/player/refresh", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      userSessionToken: loginPayload.userSessionToken,
    }),
  });

  assertEquals(refreshResponse.status, 200);
  const refreshPayload = await refreshResponse.json();
  assertEquals(refreshPayload.progression.level, 100);
  assertEquals(refreshPayload.currencies.gold, 999999);
  assertEquals(refreshPayload.owned.hunters.includes("murderer_mummy"), true);
});

Deno.test("metadata can be read and updated", async () => {
  const app = createApp(new CommunityBackendStore());

  const loginResponse = await app.request("/v2/auth/steam-login", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      steamId: "76561198000000001",
    }),
  });
  const loginPayload = await loginResponse.json();

  const readResponse = await app.request(`/v2/player/${loginPayload.userId}/metadata`);
  assertEquals(readResponse.status, 200);
  const readPayload = await readResponse.json();
  assertEquals(readPayload.total_games_finished, "0");

  const updateResponse = await app.request(
    `/v2/player/${loginPayload.userId}/metadata/total_games_finished`,
    {
      method: "PUT",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify({
        value: "12",
      }),
    },
  );

  assertEquals(updateResponse.status, 200);
  const updatePayload = await updateResponse.json();
  assertEquals(updatePayload.total_games_finished, "12");
});

Deno.test("match lifecycle works for authenticated private lobbies", async () => {
  const app = createApp(new CommunityBackendStore());

  const loginResponse = await app.request("/v2/auth/steam-login", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      steamId: "76561198000000002",
    }),
  });
  const loginPayload = await loginResponse.json();

  const createMatchResponse = await app.request("/v2/matches", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      userSessionToken: loginPayload.userSessionToken,
      gameMode: "Default",
      lobbyType: "private",
      sceneType: "Map01",
    }),
  });

  assertEquals(createMatchResponse.status, 201);
  const createMatchPayload = await createMatchResponse.json();
  assertEquals(createMatchPayload.hostUserId, loginPayload.userId);
  assertEquals(createMatchPayload.lobbyType, "private");

  const updateMatchResponse = await app.request(`/v2/matches/${createMatchPayload.matchId}`, {
    method: "PUT",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      status: "started",
      roomCode: "private-room",
      playerUserIds: [loginPayload.userId, 999],
    }),
  });

  assertEquals(updateMatchResponse.status, 200);
  const updateMatchPayload = await updateMatchResponse.json();
  assertEquals(updateMatchPayload.status, "started");
  assertEquals(updateMatchPayload.roomCode, "private-room");
  assertEquals(updateMatchPayload.playerUserIds.length, 2);
});

Deno.test("stock client routes resolve against wrapped responses", async () => {
  const app = createApp(new CommunityBackendStore());

  const loginResponse = await app.request("/api/authn/authorize/steamV2", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      steamId: "76561198000000003",
      displayName: "StockClient",
    }),
  });

  assertEquals(loginResponse.status, 200);
  const loginPayload = await loginResponse.json();
  assertEquals(loginPayload.success, true);
  assertEquals(loginPayload.result.displayName, "StockClient");

  const sessionResponse = await app.request(
    `/api/authn/session/v2/${loginPayload.result.userSessionToken}`,
  );
  assertEquals(sessionResponse.status, 200);
  const sessionPayload = await sessionResponse.json();
  assertEquals(sessionPayload.success, true);
  assertEquals(sessionPayload.result.userId, loginPayload.result.userId);

  const refreshResponse = await app.request("/api/meta/player/refresh_lobby_player", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      userSessionToken: loginPayload.result.userSessionToken,
    }),
  });
  assertEquals(refreshResponse.status, 200);
  const refreshPayload = await refreshResponse.json();
  assertEquals(refreshPayload.success, true);
  assertEquals(refreshPayload.result.owned.hunters.includes("murderer_mummy"), true);

  const productResponse = await app.request("/api/meta/products");
  assertEquals(productResponse.status, 200);
  const productPayload = await productResponse.json();
  assertEquals(productPayload.success, true);
  assertEquals(Array.isArray(productPayload.result), true);

  const serverResponse = await app.request("/api/server?aliveOnly=true");
  assertEquals(serverResponse.status, 200);
  const serverPayload = await serverResponse.json();
  assertEquals(serverPayload.success, true);
  assertEquals(serverPayload.result.length, 1);

  const metadataResponse = await app.request(
    `/api/game/user/${loginPayload.result.userId}/metadata/key/can_be_seeker`,
  );
  assertEquals(metadataResponse.status, 200);
  const metadataPayload = await metadataResponse.json();
  assertEquals(metadataPayload.success, true);
  assertEquals(metadataPayload.result.value, "true");
});

Deno.test("observed stock get routes resolve against wrapped responses", async () => {
  const app = createApp(new CommunityBackendStore());

  const loginResponse = await app.request("/api/authn/authorize/steamV2", {
    method: "POST",
    headers: {
      "content-type": "application/json",
    },
    body: JSON.stringify({
      steamId: "76561198000000004",
      displayName: "ObservedRoutes",
    }),
  });
  const loginPayload = await loginResponse.json();
  const authorizationToken = loginPayload.result.authorizationToken as string;
  const userId = loginPayload.result.userId as number;

  const refreshResponse = await app.request("/api/meta/player/refresh_lobby_player", {
    headers: {
      authorization: `Bearer ${authorizationToken}`,
    },
  });
  assertEquals(refreshResponse.status, 200);
  const refreshPayload = await refreshResponse.json();
  assertEquals(refreshPayload.success, true);
  assertEquals(refreshPayload.result.userId, userId);

  const messagesResponse = await app.request("/api/meta/player/messages");
  assertEquals(messagesResponse.status, 200);
  const messagesPayload = await messagesResponse.json();
  assertEquals(messagesPayload.success, true);
  assertEquals(Array.isArray(messagesPayload.result), true);

  const gameResponse = await app.request("/api/game");
  assertEquals(gameResponse.status, 200);
  const gamePayload = await gameResponse.json();
  assertEquals(gamePayload.success, true);
  assertEquals(gamePayload.result.key, "sneakout");

  const ccuResponse = await app.request("/api/photon/ccu");
  assertEquals(ccuResponse.status, 200);
  const ccuPayload = await ccuResponse.json();
  assertEquals(ccuPayload.success, true);
  assertEquals(ccuPayload.result.currentPlayers, 0);

  const allDataResponse = await app.request(
    "/api/user/steam/all-data?id=76561198000000004&id=76561198000000005",
  );
  assertEquals(allDataResponse.status, 200);
  const allDataPayload = await allDataResponse.json();
  assertEquals(allDataPayload.success, true);
  assertEquals(allDataPayload.result.length, 2);

  const simplifiedResponse = await app.request(`/api/meta/player/user-simplified/${userId}`);
  assertEquals(simplifiedResponse.status, 200);
  const simplifiedPayload = await simplifiedResponse.json();
  assertEquals(simplifiedPayload.success, true);
  assertEquals(simplifiedPayload.result.userId, userId);
});
