export type CurrencyBalances = {
  gold: number;
  krowns: number;
  shards: number;
};

export type OwnedCollections = {
  hunters: string[];
  skins: string[];
  avatars: string[];
  avatarFrames: string[];
  skillCards: string[];
};

export type EquippedLoadout = {
  hunter: string;
  skin: string;
  avatar: string;
  avatarFrame: string;
  preferredRole: "none" | "seeker" | "victim";
};

export type ProgressionState = {
  experience: number;
  level: number;
  seasonPassLevel: number;
};

export type SkillCardLoadout = {
  characterKey: string;
  slot: number;
  cardId: string;
};

export type SkillProgressState = {
  loadout: SkillCardLoadout[];
  maxedCharacters: string[];
};

export type PlayerMetadata = {
  games_played_as_seeker: string;
  total_games_finished: string;
  can_be_seeker: string;
};

export type ProductRecord = {
  id: number;
  key: string;
  name: string;
  category: "hunter" | "skin" | "booster" | "currency";
  priceGold: number;
  state: "owned" | "maxed";
};

export type ServerRecord = {
  id: number;
  name: string;
  host: string;
  port: number;
  alive: boolean;
  region: string;
};

export type InventoryItemRecord = {
  id: number;
  key: string;
  category: "hunter" | "skin" | "avatar" | "avatar_frame" | "skill_card";
  metadata: Record<string, string>;
};

export type PlayerRecord = {
  userId: number;
  steamId: string;
  displayName: string;
  currencies: CurrencyBalances;
  owned: OwnedCollections;
  equipped: EquippedLoadout;
  progression: ProgressionState;
  skills: SkillProgressState;
  metadata: PlayerMetadata;
  inventory: InventoryItemRecord[];
};

export type SessionRecord = {
  authorizationToken: string;
  userSessionToken: string;
  createdAtIso: string;
  playerUserId: number;
};

export type MatchRecord = {
  matchId: number;
  roomCode: string;
  hostUserId: number;
  gameMode: string;
  lobbyType: "private" | "public";
  playerUserIds: number[];
  sceneType: string;
  status: "created" | "updated" | "started" | "finished";
};

export type SteamLoginRequest = {
  steamId: string;
  displayName?: string;
};

export type SteamLoginResponse = {
  authorizationToken: string;
  userSessionToken: string;
  userId: number;
  steamId: string;
  displayName: string;
};

export type UserSessionResponse = {
  authorizationToken: string;
  userSessionToken: string;
  userId: number;
  steamId: string;
  displayName: string;
  gameUserMetadata: Record<string, string>;
};

export type RefreshPlayerResponse = {
  userId: number;
  displayName: string;
  currencies: CurrencyBalances;
  progression: ProgressionState;
  equipped: EquippedLoadout;
  owned: OwnedCollections;
  inventory: InventoryItemRecord[];
  skills: SkillProgressState;
};

export type CreateMatchRequest = {
  userSessionToken: string;
  gameMode: string;
  lobbyType: "private" | "public";
  sceneType: string;
};

export type UpdateMatchRequest = {
  matchId: number;
  roomCode?: string;
  gameMode?: string;
  lobbyType?: "private" | "public";
  playerUserIds?: number[];
  sceneType?: string;
  status?: "created" | "updated" | "started" | "finished";
};

export type CreateMatchResponse = MatchRecord;
