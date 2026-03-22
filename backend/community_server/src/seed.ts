import type {
  InventoryItemRecord,
  PlayerRecord,
  ProductRecord,
  ServerRecord,
} from "./contracts.ts";

const hunterKeys = [
  "murderer_ripper",
  "murderer_scarecrow",
  "murderer_butcher",
  "murderer_clown",
  "murderer_dracula",
  "murderer_mummy",
];

const skinKeys = [
  "skin_ripper_default",
  "skin_scarecrow_default",
  "skin_butcher_default",
  "skin_clown_default",
  "skin_dracula_default",
  "skin_mummy_default",
];

const avatarKeys = [
  "avatar_ripper_default",
  "avatar_scarecrow_default",
  "avatar_butcher_default",
  "avatar_clown_default",
  "avatar_dracula_default",
  "avatar_mummy_default",
];

const avatarFrameKeys = [
  "frame_bronze",
  "frame_silver",
  "frame_gold",
  "frame_max",
];

const skillCardKeys = [
  "card_ripper_1",
  "card_ripper_2",
  "card_scarecrow_1",
  "card_scarecrow_2",
  "card_butcher_1",
  "card_butcher_2",
  "card_clown_1",
  "card_clown_2",
  "card_dracula_1",
  "card_dracula_2",
  "card_mummy_1",
  "card_mummy_2",
];

function toInventoryItems(
  values: string[],
  category: InventoryItemRecord["category"],
  startId: number,
) {
  return values.map<InventoryItemRecord>((value, index) => ({
    id: startId + index,
    key: value,
    category,
    metadata: {},
  }));
}

export function createMaxedPlayer(
  userId: number,
  steamId: string,
  displayName: string,
): PlayerRecord {
  return {
    userId,
    steamId,
    displayName,
    currencies: {
      gold: 999_999,
      krowns: 999_999,
      shards: 999_999,
    },
    owned: {
      hunters: [...hunterKeys],
      skins: [...skinKeys],
      avatars: [...avatarKeys],
      avatarFrames: [...avatarFrameKeys],
      skillCards: [...skillCardKeys],
    },
    equipped: {
      hunter: "murderer_ripper",
      skin: "skin_ripper_default",
      avatar: "avatar_ripper_default",
      avatarFrame: "frame_max",
      preferredRole: "none",
    },
    progression: {
      experience: 9_999_999,
      level: 100,
      seasonPassLevel: 100,
    },
    skills: {
      loadout: [
        { characterKey: "murderer_ripper", slot: 0, cardId: "card_ripper_1" },
        { characterKey: "murderer_ripper", slot: 1, cardId: "card_ripper_2" },
        { characterKey: "murderer_scarecrow", slot: 0, cardId: "card_scarecrow_1" },
        { characterKey: "murderer_scarecrow", slot: 1, cardId: "card_scarecrow_2" },
        { characterKey: "murderer_butcher", slot: 0, cardId: "card_butcher_1" },
        { characterKey: "murderer_butcher", slot: 1, cardId: "card_butcher_2" },
        { characterKey: "murderer_clown", slot: 0, cardId: "card_clown_1" },
        { characterKey: "murderer_clown", slot: 1, cardId: "card_clown_2" },
        { characterKey: "murderer_dracula", slot: 0, cardId: "card_dracula_1" },
        { characterKey: "murderer_dracula", slot: 1, cardId: "card_dracula_2" },
        { characterKey: "murderer_mummy", slot: 0, cardId: "card_mummy_1" },
        { characterKey: "murderer_mummy", slot: 1, cardId: "card_mummy_2" },
      ],
      maxedCharacters: [...hunterKeys],
    },
    metadata: {
      games_played_as_seeker: "0",
      total_games_finished: "0",
      can_be_seeker: "true",
    },
    inventory: [
      ...toInventoryItems(hunterKeys, "hunter", 1000),
      ...toInventoryItems(skinKeys, "skin", 2000),
      ...toInventoryItems(avatarKeys, "avatar", 3000),
      ...toInventoryItems(avatarFrameKeys, "avatar_frame", 4000),
      ...toInventoryItems(skillCardKeys, "skill_card", 5000),
    ],
  };
}

export function createDefaultProducts(): ProductRecord[] {
  const hunterProducts = hunterKeys.map<ProductRecord>((value, index) => ({
    id: 100 + index,
    key: value,
    name: value,
    category: "hunter",
    priceGold: 0,
    state: "maxed",
  }));
  const skinProducts = skinKeys.map<ProductRecord>((value, index) => ({
    id: 200 + index,
    key: value,
    name: value,
    category: "skin",
    priceGold: 0,
    state: "owned",
  }));
  return [...hunterProducts, ...skinProducts];
}

export function createDefaultServers(baseUrl: string): ServerRecord[] {
  const parsedUrl = new URL(baseUrl);
  return [
    {
      id: 1,
      name: "community-local",
      host: parsedUrl.hostname,
      port: Number(parsedUrl.port || (parsedUrl.protocol === "https:" ? 443 : 80)),
      alive: true,
      region: "local",
    },
  ];
}
