# Steam party invites

`friend-invite-unlock` 0.2.1 bridges Steam's game-invite transport to Sneak Out's existing lobby flow.

While the local player is eligible to invite and is in an open lobby party, the mod publishes a Steam `connect` rich-presence value. This enables Steam overlay **Join Game** and **Invite to Play**. Clicking an offline friend in Sneak Out also sends a real Steam game invite, while preserving the game's original backend invite attempt for online compatibility.

The connect value contains a versioned, URL-safe token with the host Steam ID, Nakama party ID, and Photon region. It contains no authentication ticket or player credentials. Incoming tokens are accepted only when the advertised host is an immediate Steam friend; overlay callbacks must also identify the same inviter.

After an invite is accepted, the recipient waits until Steam, Nakama, and the lobby scene are ready. It then supplies the party ID and region to `PgosLobby.JoinLobbyFromInvitationAsync()`. Sneak Out performs its normal Nakama party join and Photon lobby transition, including its normal party existence and capacity checks.

The IL2CPP Steamworks interop represents `GameRichPresenceJoinRequested_t` as a non-blittable wrapper because it contains a fixed-size connect-string buffer. The mod therefore registers a `CallbackDispatcher` adapter that receives the native parameter pointer and wraps it inside the callback, avoiding a typed managed-to-IL2CPP delegate boundary. Callback registration failure is isolated from launch-token parsing and outgoing rich presence.

Both players need `friend-invite-unlock` 0.2.1 or newer for automatic party joining. Without the recipient mod, Steam can launch or focus Sneak Out and deliver the connect value, but the unmodified client does not consume it and therefore does not automatically join the party.

The feature is controlled by:

- `steam.EnableSteamInvites`: sends Steam invites and publishes the overlay join presence.
- `steam.AutoJoinSteamInvites`: consumes accepted Steam invite and Join Game requests.
- `general.RequireTeamLeader`: limits invite/presence publication to the party leader when enabled.
