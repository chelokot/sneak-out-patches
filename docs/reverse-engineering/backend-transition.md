# Backend Transition

## Goal

Prepare a runtime-mod path for moving `Sneak Out` away from the dead upstream web service and toward a community-hosted backend for private lobbies.
Prepare a runtime-mod path for taking control of the dead upstream web-service layer from a local runtime stub or an optional redirect target.

## Confirmed client seams

### `Kinguinverse.WebServiceProvider.KinguinverseWebServiceSettings`

Confirmed from interop:

- `WebServiceEnvironment`
- `HttpClientFactory`
- constructors:
  - `KinguinverseWebServiceSettings(string webServiceEnvironment)`
  - `KinguinverseWebServiceSettings(string webServiceEnvironment, Func<HttpClient> httpClientFactory)`

Practical meaning:

- this is the earliest clean configuration object for the v2 HTTP client
- if the client can be redirected without touching every endpoint individually, this settings object is one of the strongest candidates

### `Kinguinverse.WebServiceProvider.KinguinverseWebServiceV2`

Confirmed from interop:

- constructors:
  - `KinguinverseWebServiceV2(KinguinverseWebServiceSettings settings)`
  - `KinguinverseWebServiceV2(KinguinverseWebServiceSettings settings, string authorizationToken)`
- mutable property:
  - `WebServiceUrl`
- method:
  - `ChangeEnvironment(EnvType env)`

Practical meaning:

- the v2 service already exposes a direct runtime URL property
- this is the most promising redirect seam for the modern HTTP path

### `Kinguinverse.WebServiceProvider.UnityHttp`

Confirmed from interop:

- internal URL field/property:
  - `_webServiceUrl`
- auth field/property:
  - `_authorizationToken`
- timeout field/property:
  - `_timeout`
- method:
  - `ChangeEnvironment(EnvType env)`
- endpoint-specific coroutine wrappers such as:
  - `RefreshPlayer`
  - `GetProducts`
  - `GetGameUserMetadata`
  - `StartMatch`
  - `FinishMatch`

Practical meaning:

- this is the older coroutine-driven HTTP layer
- if redirect work needs to support legacy flows, this object also needs coverage

### `Base.ClientCache`

Confirmed from interop:

- stores:
  - `AuthorizationToken`
  - `UserSessionToken`
  - `UserInfo`
  - `UserWebPlayer`
- holds:
  - `_kinguinverseWebService`
- calls:
  - `OnClientConfirmed(int myServerId)`
  - `RefreshPlayer()`

Practical meaning:

- this is the client-side session/profile cache seam
- if redirect succeeds but the profile model still mismatches, this is the next place to inspect

## Confirmed environment enum

`Kinguinverse.WebServiceProvider.EnvType`

- `LoadBalanceStaging`
- `LoadBalanceProd`
- `LoadBalanceProxy`
- `LoadBalanceProxyV2`

Practical meaning:

- the stock client thinks in terms of named upstream environments
- any local recovery path needs either:
  - one of these existing routes repointed locally
  - or a direct `WebServiceUrl` override that bypasses environment lookup

## Current runtime scaffold

The repo now contains a new research-only mod scaffold at:

- `mods/backend_stabilizer/`

Current behavior:

- can redirect `KinguinverseWebService`, `KinguinverseWebServiceV2`, and `UnityHttp` to a custom base URL
- can serve a local maxed-account profile overlay when `EnableLocalStub=true`
- populates `ClientCache` during `OnClientConfirmed` and `RefreshPlayer`
- can stub late profile/meta methods such as products, resources, boosters, and metadata
- logs web-service construction and redirect targets when research logging is enabled

Current stubbed surface:

- profile/meta/store only:
  - `ClientCache.OnClientConfirmed`
  - `ClientCache.RefreshPlayer`
  - `RefreshPlayer`
  - `GetProducts`
  - `GetProductsV2`
  - `GetGameUserMetadata`
  - `GetGameUserMetadatas`
  - `SetGameUserMetadata`
  - `SetGameUserMetadatas`
  - `GetPlayerMessages`
  - `GetPlayerResources`
  - `GetMyBoosters`

## Confirmed minimum backend surface

Based on `dump.cs`, the current runtime mod, and the actual goal of the project, the correct minimum surface is narrower than a full backend replacement.

1. leave auth/session/friends/party untouched
   - `SteamLogInV2`
   - `GetUserSession` / `GetUserSessionV2`
   - `GetPlayer(...)`
   - `GetAllUserData(...)`
   - `GetFriends(...)`
   - `SetHiddenOnFriendlists`
   - `SetLastServerName`
   - `PgosLobby` / invites / party / matchmaking
2. apply a local max-profile overlay after the real backend/bootstrap has already run
   - `ClientCache.OnClientConfirmed`
   - `ClientCache.RefreshPlayer`
3. optionally stub late economy/profile reads that the dead backend no longer serves correctly
   - `RefreshPlayer`
   - products/resources/messages/boosters
   - game user metadata

## Confirmed local-stub bug that mattered

The original local stub built both session responses with swapped arguments:

- `GetUserSessionResponse(bool authorized, int userId, List<GameUserMetadataDto> metadata, string kinguinEmail, bool hideOnFriendlists, string lastServerName)`
- `GetUserSessionV2Response(bool authorized, int userId, string kinguinEmail, bool hideOnFriendlists, string lastServerName, UserAllData userAllData)`

The older stub passed `CommunityServerName` where `kinguinEmail` should go and `_kinguinEmail` where `lastServerName` should go.

The fixed runtime stub now keeps explicit state for:

- `KinguinEmail`
- `HideOnFriendlists`
- `LastServerName`

and feeds those values consistently into:

- session responses
- `UserInfoDto`
- `ClientCache`

That fix is still useful if full stub mode is ever re-enabled for research, but it is no longer part of the recommended production architecture for the stabilizer mod.

## Planned investigation order

1. verify which unlock/economy UI paths still ignore the `ClientCache` overlay and require late-method stubs
2. make the local overlay preserve all real identity/session values while only replacing progression/unlock/economy data
3. keep matchmaking, party, and invite flows entirely on the stock code path
4. if needed, split the current config into:
   - redirect-only
   - profile-overlay-only
   - late-economy-stub
