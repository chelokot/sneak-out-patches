import type {
  CreateMatchRequest,
  CreateMatchResponse,
  MatchRecord,
  PlayerRecord,
  ProductRecord,
  ServerRecord,
  SessionRecord,
  SteamLoginRequest,
  SteamLoginResponse,
  UpdateMatchRequest,
  UserSessionResponse,
} from "./contracts.ts";
import { createDefaultProducts, createDefaultServers, createMaxedPlayer } from "./seed.ts";

type StoreOptions = {
  now?: () => Date;
  baseUrl?: string;
};

export class CommunityBackendStore {
  readonly #playersByUserId = new Map<number, PlayerRecord>();
  readonly #userIdBySteamId = new Map<string, number>();
  readonly #sessionsByAuthorizationToken = new Map<string, SessionRecord>();
  readonly #sessionsByUserSessionToken = new Map<string, SessionRecord>();
  readonly #matchesById = new Map<number, MatchRecord>();
  readonly #products: ProductRecord[];
  readonly #servers: ServerRecord[];
  readonly #now: () => Date;
  #nextUserId = 1;
  #nextMatchId = 1;

  constructor(options: StoreOptions = {}) {
    this.#now = options.now ?? (() => new Date());
    this.#products = createDefaultProducts();
    this.#servers = createDefaultServers(options.baseUrl ?? "http://127.0.0.1:8080");
  }

  loginWithSteam(request: SteamLoginRequest): SteamLoginResponse {
    const normalizedSteamId = request.steamId.trim();
    const displayName = request.displayName?.trim() || `Player-${normalizedSteamId}`;
    const existingUserId = this.#userIdBySteamId.get(normalizedSteamId);
    const userId = existingUserId ?? this.#nextUserId++;
    if (!existingUserId) {
      const player = createMaxedPlayer(userId, normalizedSteamId, displayName);
      this.#playersByUserId.set(userId, player);
      this.#userIdBySteamId.set(normalizedSteamId, userId);
    }

    const player = this.requirePlayer(userId);
    player.displayName = displayName;

    const authorizationToken = `auth_${crypto.randomUUID()}`;
    const userSessionToken = `session_${crypto.randomUUID()}`;
    const session: SessionRecord = {
      authorizationToken,
      userSessionToken,
      createdAtIso: this.#now().toISOString(),
      playerUserId: userId,
    };

    this.#sessionsByAuthorizationToken.set(authorizationToken, session);
    this.#sessionsByUserSessionToken.set(userSessionToken, session);

    return {
      authorizationToken,
      userSessionToken,
      userId,
      steamId: normalizedSteamId,
      displayName,
    };
  }

  getSessionByAuthorizationToken(authorizationToken: string): UserSessionResponse | null {
    const session = this.#sessionsByAuthorizationToken.get(authorizationToken);
    if (!session) {
      return null;
    }

    const player = this.requirePlayer(session.playerUserId);
    return {
      authorizationToken: session.authorizationToken,
      userSessionToken: session.userSessionToken,
      userId: player.userId,
      steamId: player.steamId,
      displayName: player.displayName,
      gameUserMetadata: { ...player.metadata },
    };
  }

  getSessionByUserSessionToken(userSessionToken: string): UserSessionResponse | null {
    const session = this.#sessionsByUserSessionToken.get(userSessionToken);
    if (!session) {
      return null;
    }

    return this.getSessionByAuthorizationToken(session.authorizationToken);
  }

  getPlayerByAuthorizationToken(authorizationToken: string): PlayerRecord | null {
    const session = this.#sessionsByAuthorizationToken.get(authorizationToken);
    if (!session) {
      return null;
    }

    return this.requirePlayer(session.playerUserId);
  }

  getPlayerByUserSessionToken(userSessionToken: string): PlayerRecord | null {
    const session = this.#sessionsByUserSessionToken.get(userSessionToken);
    if (!session) {
      return null;
    }

    return this.requirePlayer(session.playerUserId);
  }

  getPlayerMetadata(userId: number): Record<string, string> | null {
    const player = this.#playersByUserId.get(userId);
    return player ? { ...player.metadata } : null;
  }

  setPlayerMetadata(userId: number, key: string, value: string): Record<string, string> | null {
    const player = this.#playersByUserId.get(userId);
    if (!player) {
      return null;
    }

    player.metadata = {
      ...player.metadata,
      [key]: value,
    };
    return { ...player.metadata };
  }

  listProducts(): ProductRecord[] {
    return this.#products.map((product) => ({ ...product }));
  }

  listServers(aliveOnly: boolean): ServerRecord[] {
    return this.#servers
      .filter((server) => !aliveOnly || server.alive)
      .map((server) => ({ ...server }));
  }

  createMatch(request: CreateMatchRequest): CreateMatchResponse | null {
    const session = this.#sessionsByUserSessionToken.get(request.userSessionToken);
    if (!session) {
      return null;
    }

    const matchId = this.#nextMatchId++;
    const match: MatchRecord = {
      matchId,
      roomCode: `room_${matchId}`,
      hostUserId: session.playerUserId,
      gameMode: request.gameMode,
      lobbyType: request.lobbyType,
      playerUserIds: [session.playerUserId],
      sceneType: request.sceneType,
      status: "created",
    };

    this.#matchesById.set(matchId, match);
    return { ...match, playerUserIds: [...match.playerUserIds] };
  }

  updateMatch(request: UpdateMatchRequest): MatchRecord | null {
    const existingMatch = this.#matchesById.get(request.matchId);
    if (!existingMatch) {
      return null;
    }

    const updatedMatch: MatchRecord = {
      ...existingMatch,
      roomCode: request.roomCode ?? existingMatch.roomCode,
      gameMode: request.gameMode ?? existingMatch.gameMode,
      lobbyType: request.lobbyType ?? existingMatch.lobbyType,
      playerUserIds: request.playerUserIds
        ? [...request.playerUserIds]
        : [...existingMatch.playerUserIds],
      sceneType: request.sceneType ?? existingMatch.sceneType,
      status: request.status ?? existingMatch.status,
    };

    this.#matchesById.set(request.matchId, updatedMatch);
    return { ...updatedMatch, playerUserIds: [...updatedMatch.playerUserIds] };
  }

  private requirePlayer(userId: number): PlayerRecord {
    const player = this.#playersByUserId.get(userId);
    if (!player) {
      throw new Error(`Player ${userId} is missing`);
    }

    return player;
  }
}
