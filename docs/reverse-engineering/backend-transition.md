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

- `mods/backend_redirector/`

Current behavior:

- logs web-service construction
- logs environment-change requests
- logs session-cache state at `ClientCache.OnClientConfirmed`
- logs session-cache state before `ClientCache.RefreshPlayer`

Current non-behavior:

- no HTTP redirect
- no response rewrite
- no session spoofing
- no live backend replacement

## Planned investigation order

1. verify which constructor path actually builds the live HTTP client in the current retail build
2. verify whether `WebServiceUrl` is stable after `ChangeEnvironment`
3. confirm whether any important profile/shop path still bypasses `KinguinverseWebServiceV2`
4. map the minimum request surface needed for private-lobby-only operation
5. only then add controlled redirect logic
