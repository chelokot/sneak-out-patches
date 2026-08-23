# Leader Host

`Leader Host` makes the party creator host the new Fusion session created for private matches. Public matchmaking keeps the backend-assigned host because public players outside the party have not participated in the compatibility handshake. The mod does not transfer state authority inside an already running match: Sneak Out leaves its Fusion host-migration callback empty, so pretending that live migration works would be unsafe.

The stock client receives `matchId`, `hostId`, and `region`, then compares `hostId` with its local social user id. The matching client starts the match runner as `GameMode.Host`; the others start it as clients. The mod synchronizes the exact `PgosLobby.TeamLeaderId` value as a replacement `hostId` while everyone is still in the lobby and applies it in `PhotonLobby.JoinMatchSession`. Fusion participant user ids remain separate and are used only to secure the compatibility quorum.

Safety protocol:

- every real Fusion client sends the current protocol version, membership signature, acknowledgement revision, and enabled capabilities to the lobby authority through Fusion reliable data;
- the lobby authority binds each advertisement to Fusion's authenticated sender `PlayerRef`, keeps the authoritative peer registry in memory, and is the only process that writes the compact registry to room custom properties;
- each peer also advertises enabled multiplayer feature capabilities; the coordinator publishes only the bitwise intersection supported by every current participant;
- the participant snapshot comes from `SpookedNetworkPlayer` spawn/despawn callbacks and must match Fusion's scalar session player count before use, avoiding Fusion's mutable IL2CPP `ActivePlayers` iterator during joins;
- the advertised `PlayerRef` set must exactly match the current real-player set, and every advertisement must carry the current membership signature; this rejects missing, extra, and stale entries without depending on Fusion user-id lookup behavior;
- the current lobby authority publishes the `PlayerRef` membership signature, revision, creator `PlayerRef`, and exact party-leader social user id as session custom properties;
- the lobby authority also publishes its local private/public portal selection, because that UI state is not synchronized into other party members' local `GameState` objects;
- every real participant acknowledges the same revision through its own membership-bound room property;
- Leader Host becomes ready only after all acknowledgements arrive;
- lobby test bots are excluded from the quorum;
- a join, leave, missing or extra advertisement, membership mismatch, stale revision, or absent mod disarms the override;
- a changed feature-capability intersection increments the handshake revision and requires fresh acknowledgements before dependent gameplay features arm;
- public matchmaking always preserves the backend-assigned host, even when the private-party compatibility quorum is ready;
- when disarmed, the mod leaves PLAY and the stock match-host choice untouched.

The protocol deliberately does not Harmony-patch `PhotonLobby.OnReliableDataReceived`.
The BepInEx IL2CPP bridge bundled with the game setup cannot safely box Fusion's nested
`ReliableKey` and `ArraySegment<byte>` value types in a Harmony postfix. Instead, Leader Host
registers a typed `NetworkEvents.OnReliableData` listener with the lobby runner.

There is no host selector or extra portal button. The stock lower-left map/ping strip appends `Host: <name>` for the party creator selected to host the match. Ping never changes that choice.
