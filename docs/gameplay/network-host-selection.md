# Network host selection

`Network Host Selector` changes the host of the new Fusion session created for a match. It does not transfer state authority inside an already running match: Sneak Out 1.1.10 leaves its Fusion host-migration callback empty, so pretending that live migration works would be unsafe.

The stock client receives `matchId`, `hostId`, and `region`, then compares `hostId` with its local Steam/Fusion authentication user id. The matching client starts the match runner as `GameMode.Host`; the others start it as clients. The mod synchronizes a replacement `hostId` while everyone is still in the lobby and applies it in `PhotonLobby.JoinMatchSession`.

Safety protocol:

- every real Fusion peer advertises protocol version 1 and its membership-bound identity through room custom properties;
- the current lobby authority publishes the participant signature, proposal revision, selected `PlayerRef` and exact Fusion user id as session custom properties;
- every real participant acknowledges the same proposal revision through its own membership-bound room property;
- the selector becomes ready only after all acknowledgements arrive;
- lobby test bots are excluded from both the quorum and candidate list;
- a join, leave, missing identity advertisement, identity mismatch, stale revision, or absent mod disarms the override;
- when disarmed or set to `Automatic`, the original backend `hostId` is left untouched.

The protocol deliberately does not Harmony-patch `PhotonLobby.OnReliableDataReceived`.
The BepInEx IL2CPP bridge bundled with the game setup cannot safely box Fusion's nested
`ReliableKey` and `ArraySegment<byte>` value types and can otherwise terminate the process
before a managed postfix is entered.

Only the current party leader can cycle the portal button. Candidate labels include measured lobby RTT as a useful hint, but RTT never automatically changes the host and never affects protocol eligibility.
