# Community Backend Scope

## Intended outcome

A self-hosted backend that is sufficient for:

- logging in patched clients
- loading a stable player session
- returning deterministic profile and inventory state
- supporting private-party and private-lobby play

## Source-of-truth model

The clean target is one canonical player record per user.

Suggested fields:

- identity
  - steam id
  - display name
  - internal user id
- session
  - authorization token
  - user session token
  - expiry
- profile
  - currencies
  - selected cosmetics
  - selected hunter
  - preferred role
- ownership
  - hunters
  - skins
  - avatars
  - avatar frames
  - skill cards
- progression
  - experience
  - resources
  - season pass state
  - daily-quest visibility state if needed
- metadata
  - `games_played_as_seeker`
  - `total_games_finished`
  - `can_be_seeker`
- match state
  - current match id
  - current party id
  - host designation if needed

## MVP request groups

### Phase 1

- `SteamLogInV2`
- `GetUserSession`
- `GetUserSessionV2`
- `RefreshPlayer`

### Phase 2

- `GetGameUserMetadatas`
- `GetGameUserMetadata`
- `SetGameUserMetadata`
- `SetGameUserMetadatas`

### Phase 3

- `CreateMatch`
- `UpdateMatch`

### Phase 4

- `GetProducts`
- `GetInventory`
- `GetMyBoosters`
- ownership-related equip endpoints

## Why this order makes sense

Without phase 1, the client cannot establish a coherent session.

Without phase 2, private-lobby fairness and player metadata will drift.

Without phase 3, room lifecycle will still depend on dead upstream services.

Without phase 4, the UI can still look broken even if private matches work.

## What the runtime mod should own

- endpoint and environment redirection
- temporary logging of live request flow
- compatibility shims when the client expects upstream-specific conventions

## What the backend should own

- canonical player data
- session issuance
- profile and inventory responses
- match records for private lobbies

## What should stay out of the mod

- hardcoded fake inventory payloads
- duplicated profile logic
- business state that belongs in the backend

The runtime mod should become thin over time, not thick.
