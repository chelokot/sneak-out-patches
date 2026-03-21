# Private Party Double-Invite Bug

## Symptom

In private parties, the invited player accepts the first invite, sees a short loading transition, and then ends up back in their own lobby instead of the host lobby. A second invite from the host then works reliably.

Observed behavior is stable:

- first invite: loading, then back to the invitee's own lobby
- second invite: invitee joins the host lobby

## High-confidence conclusion

This is a client-side synchronization bug between the Nakama party layer and the Photon lobby layer.

The first invite acceptance reaches the "join host lobby" flow, but the Photon join attempt fails. When that join attempt fails, the client explicitly falls back to leaving the current party context and recreating / rejoining its own Photon lobby. The second invite then succeeds because the first attempt already initialized enough party-side state for the retry path to work.

This is not consistent with a pure server rejection. The client has an explicit failure fallback that matches the visible symptom very closely.

## Relevant call chain

### 1. Invitation UI accept path

`InvitationRecord.OnAccept()` calls `PgosLobby.JoinLobbyFromInvitation()` directly.

- `InvitationRecord.OnAccept` at `0x1807DE130`
- direct call to `PgosLobby.JoinLobbyFromInvitation` at `0x1807DE340`

This means the accept button itself is not the issue. The bad behavior happens after the accept action has already entered the PGOS / lobby flow.

### 2. PGOS lobby join from invitation

`PgosLobby.JoinLobbyFromInvitation()` reads the first cached invitation from `PlayfabIntvitationsData`, clears that cache entry, builds a `JoinLobbyEvent`, and publishes it.

- `PgosLobby.JoinLobbyFromInvitation` at `0x1808169A0`
- `JoinLobbyEvent..ctor(string lobbyId, string region)` at `0x1807043C0`

`JoinLobbyFromInvitation()` does not directly look like the final Photon join. It prepares the join request and routes it into the event-driven lobby flow.

### 3. JoinLobby event handler

`PgosLobby.OnJoinLobbyEvent()` handles that event, updates current lobby-region state, and then calls `SpookedLobbyUtils.JoinLobby()`.

- `PgosLobby.OnJoinLobbyEvent` at `0x180816CA0`
- tail call to `SpookedLobbyUtils.JoinLobby()` at `0x1806067D0`

This is the Photon-lobby handoff point.

### 4. Nakama party join async fallback

Inside `NakamaController.<JoinParty>d__38.MoveNext`, there is a failure branch after a `StartGameResult.Ok` check.

- `NakamaController.<JoinParty>d__38.MoveNext` around `0x180824A7F` to `0x180824BC3`
- `StartGameResult.get_Ok()` is `0x180A71240`

When `Ok == false`, the method does this:

- emits an error / transitional state update
- calls `PgosLobby.LeaveParty()` at `0x180816C00`
- immediately calls `PgosLobby.TryCreatePartyAndJoinToPhotonLobby()` at `0x180817DB0`

That branch is the exact "short loading, then back to my own lobby" behavior:

- the client gives up on joining the target party/lobby
- leaves the current party flow
- creates or rejoins its own Photon lobby again

## Why the second invite works

The current best explanation is state ordering:

- first invite gets far enough to initialize or refresh party-related state on the invitee side
- the Photon join still fails on that first pass, so the client falls back to its own lobby
- second invite runs after that party-side state already exists, so the retry path succeeds

This is consistent with the deterministic behavior users see: it is not random network flakiness, and it is not "sometimes the server is slow". The client logic is structured in a way that makes "first attempt initializes, second attempt succeeds" plausible.

## Why this is probably not just a UI bug

There is a separate method, `PgosLobby.OnTeamInvitationClickedEvent()` at `0x180817A60`, that routes through `InviteToParty(...)`, but the invitation card accept button does not use that path directly. The actual accept button calls `JoinLobbyFromInvitation()` instead.

That makes the problem more specific:

- the invitation UI is wired
- the accept click is reaching the intended PGOS join flow
- the breakage happens later, during the party / Photon transition

## Working patch direction

The most reliable minimal fix is earlier than the async `JoinParty` failure branch.

`PgosLobby.OnJoinLobbyEvent()` already receives a `JoinLobbyEvent` object that carries:

- `LobbyId` at offset `0x18`
- `Region` at offset `0x20`

The vanilla handler updates current region state from the event, but then tail-calls `SpookedLobbyUtils.JoinLobby()` without arguments. That no-arg path falls back to cached lobby state, which matches the stale-lobby-id symptom seen in guest logs.

The patchable fix is to replace the final tail with a direct call to:

- `SpookedLobbyUtils.JoinLobby(string lobbyId, string region)` at `0x180606760`

and to pass:

- `JoinLobbyEvent.LobbyId`
- the already extracted event region

In the tested build this is a single contiguous patch in `GameAssembly.dll`:

- function: `PgosLobby.OnJoinLobbyEvent`
- VA: `0x180816D3E`
- raw offset: `0x81593E`

This patch keeps the event flow intact and only stops the client from discarding the explicit lobby id that came from the invitation.

## Evidence quality

Confidence is high for these points:

- the accept path enters `JoinLobbyFromInvitation()`
- `JoinLobbyFromInvitation()` leads into the lobby join event flow
- the client has an explicit `join failed -> leave party -> create own lobby` branch
- that fallback matches the observed symptom closely

Confidence is lower for one point:

- the exact missing piece that causes the first join attempt to return `StartGameResult.Ok == false`

That part still needs either:

- a guest-side `Player.log` captured during a failing first invite, or
- deeper tracing of the objects used by `JoinLobbyFromInvitation()` and the async `JoinParty` flow

## Practical takeaway

The bug is real and structural. The second invite is not "doing the same thing again and randomly succeeding". The first invite appears to initialize state and then hit a client fallback that returns the player to their own lobby. The second invite succeeds because the first pass already changed the local party state.
