# Proximity Voice Chat

## Scope

`Proximity Voice Chat` is a separate BepInEx IL2CPP plugin. It does not modify the game's
backend, Fusion simulation, player count, matchmaking, or native binaries.

The first version uses the Steam client already required by the game for:

- microphone capture and Steam's voice codec
- authenticated peer identity
- relayed P2P datagrams when a direct route is unavailable

No external voice server, account, native codec, or microphone recording file is created.

## User behavior

- Push-to-talk is the default, on physical `V` regardless of keyboard layout.
- `VoiceActivation` has configurable threshold, 160 ms pre-roll, and hangover so syllable starts
  and ends are not clipped.
- `AlwaysOn` exists but is not the privacy-preserving default.
- Transmission stops when the game loses focus and while a text field is selected, unless the
  corresponding setting is explicitly changed.
- `MutedSteamIds` is a persistent local deny-list.
- Voice mode, voice volume, microphone sensitivity, and audible distance are exposed inside the
  game's stock Audio settings scroll using cloned `SettingsPanelView`/`SpookedOutlineSlider`
  controls. The normal rounded panels, hover outline, mouse, and controller navigation are kept.

## Living and ghost channels

- Living players hear living players, regardless of whether either side is a hunter, penguin, or
  currently controlling a prop.
- Dead players (ghosts) hear both living players and other ghosts.
- Living players never hear ghosts. The receiver discards buffered ghost speech immediately when
  either player's replicated life state changes, so a death or resurrection cannot leak the tail
  of an earlier phrase into the wrong channel.
- Resurrection immediately restores the normal living-only receive filter.

## Session and identity safety

Voice is enabled only when a local input-authority player and a real Fusion session name both
exist. The raw room name is never published; its stable hash separates lobby/match voice traffic.

Each packet contains both the Steam-authenticated sender ID and the player's in-game internal ID.
The receiver requires them to agree with one of these discovery sources:

1. the current network-player registry
2. a Steam friend advertising the same room hash through rich presence
3. an explicitly configured `AdditionalPeerSteamIds` entry

A repeated discovery `Hello` carries a random per-process instance ID. Audio is rejected before that hello,
after a session transition, or if the Steam transport sender differs from the packet sender. This
also keeps delayed relay datagrams from a previous run of the same lobby from being replayed.

Network-registry identities are discovery candidates, not immediate audio recipients. Actual voice
datagrams are sent only after the other client advertises or answers the proximity-voice protocol,
so mixed lobbies do not build reliable Steam queues toward players who do not have the mod.

## Audio pipeline

The sender reads compressed Steam voice frames on the Unity main thread. Frames larger than a
Steam unreliable datagram are split into bounded fragments; the receiver applies strict fragment
count, memory, and expiry limits before reassembly.

Transport uses a dedicated P2P channel and closes only that channel during teardown; it never
closes another mod's or the game's whole Steam peer session.

Each remote speaker owns:

- an adaptive packet-jitter buffer based on arrival/capture delta variation
- late/duplicate packet rejection and bounded loss recovery
- Steam voice decompression into a three-second circular PCM clip
- a child `AudioSource` attached near the remote avatar's head
- normalized custom 3D rolloff: full volume nearby, progressively quieter with distance, and
  exactly silent at the configurable far edge
- a throttled wall probe which smoothly blends volume and low-pass filtering

The implementation prefers dropping stale speech and resynchronizing over accumulating seconds of
latency. All packet, fragment, PCM, and per-tick decode work is bounded.
Per-peer packet and byte budgets prevent a malformed client from monopolizing the Unity update
loop or growing unbounded receive state.

## Compatibility state

The implementation is bound against Sneak Out 1.1.10 interop and has loaded successfully through
the BepInEx chainloader into a real online lobby. Deterministic tests cover packet validation,
out-of-order fragmentation, jitter delay, packet-loss recovery, and the complete living/ghost
audibility matrix.

The current unattended client later developed a Unity graphics-startup stall before plugin
discovery, with the same behavior both with and without this plugin and under both DXVK and
WineD3D. Consequently the newest stock-settings UI revision still needs its captured visual smoke
run after the external startup issue clears. Actual microphone-to-speaker validation also requires
two different Steam accounts; one local client cannot authenticate a P2P voice peer to itself.
