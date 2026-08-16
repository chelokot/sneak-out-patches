# Proximity Voice Chat

## Scope

`Proximity Voice Chat` is a separate BepInEx IL2CPP plugin. It does not modify the game's
backend, Fusion simulation, player count, matchmaking, or native binaries.

The plugin uses the Steam client already required by the game for:

- authenticated peer identity
- relayed P2P datagrams when a direct route is unavailable

Microphone PCM is encoded with the Photon Voice Opus codec already shipped by the game. No
external voice server, account, native codec, or microphone recording file is created.

## User behavior

- Push-to-talk is the default, on physical `V` regardless of keyboard layout. Its stock-style key
  binding row supports recording, cancellation, and reset inside Audio settings.
- `VoiceActivation` has configurable threshold, 160 ms pre-roll, and hangover so syllable starts
  and ends are not clipped.
- `AlwaysOn` exists but is not the privacy-preserving default.
- `Stop when game is unfocused` is enabled by default and can be changed in Audio settings.
- `My voice volume` applies a persistent 0–200% peak-safe gain before Opus encoding, changing the
  level sent to every other player without affecting voice-activation detection.
- The `Microphone test` Start/Stop button continuously replays encoded capture with a one-second
  delay through the same Opus decoder, receive gain, and Unity audio output used for remote voices.
  It never transmits test audio and drains the final delayed second after Stop is pressed. Headphones
  prevent the delayed monitor from being picked up by the microphone again.
- Audio settings shows a separate 0–200% volume slider for every remote, non-bot party member.
  The local player is never listed, and overrides persist by SteamID64 across reconnects. Receive
  gain maps 0% to silence, 100% to the old 600%, and 200% to the old 1200%. A smooth output
  limiter preserves quiet-signal amplification without hard-clipping loud peaks.
- `Directional voice` is enabled by default. Turning it off centers every remote voice equally in
  the left and right channels while retaining normal route-distance volume and wall/door muffling.
- `MutedSteamIds` is a persistent local deny-list.
- Enabled state and focus behavior use stock toggle rows, voice mode uses a stock dropdown,
  push-to-talk uses the stock key-binding row, microphone testing uses a stock action button, and
  outgoing/per-player volume and sensitivity use stock sliders inside the game's Audio settings
  scroll. The normal rounded panels, hover outline, mouse, and controller navigation are kept.
  Audible distance is fixed at 20 metres.

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

Once a voice peer is authenticated, push-to-talk keeps Steam's local microphone capture session
warm and drains unsent frames while the key is released. No audio is transmitted until the key is
held; avoiding a fresh Steam capture startup on every press prevents clipped or delayed first
utterances.

Transport uses a dedicated P2P channel and closes only that channel during teardown; it never
closes another mod's or the game's whole Steam peer session.

The initial `Hello` uses Steam's connection-establishing unreliable mode, which may queue that
small control packet while NAT traversal or relay negotiation completes. Encoded speech switches
to no-delay unreliable delivery only after the authenticated handshake, so stale voice is dropped
instead of accumulating latency. Teardown remains reliable.

Each remote speaker owns:

- a six-times-original receive baseline with smooth output limiting, driven by that speaker's
  SteamID64 volume override and falling back to the configured default when no override is saved
- an adaptive packet-jitter buffer based on arrival/capture delta variation
- late/duplicate packet rejection and bounded loss recovery
- Steam voice decompression into a three-second circular PCM clip
- a world-space `AudioSource` placed at the remote avatar's head while the direct route is clear
- manual evaluation of the existing 3D rolloff: full volume within 2.5 metres, progressively
  quieter using travelled route distance, and exactly silent at the fixed 20-metre edge
- throttled direct-path physics probing that ignores player colliders and lightly attenuates
  ordinary items (75% volume, 6.5 kHz low-pass)
- NavMesh propagation around structurally named walls: the source moves to the first route corner,
  so speech comes from the nearest doorway/corridor while volume uses the complete route length
- rejection of straight baked paths through physics blockers, plus route-segment door probing;
  an intersecting closed door or unavailable route retains heavy fallback muffling (20% volume,
  1.1 kHz low-pass)
- participation in the game's existing `AudioReverbZone`s, so voice naturally inherits authored
  room echo while retaining its own distance and occlusion filters

The implementation prefers dropping stale speech and resynchronizing over accumulating seconds of
latency. All packet, fragment, PCM, and per-tick decode work is bounded.
Per-peer packet and byte budgets prevent a malformed client from monopolizing the Unity update
loop or growing unbounded receive state.

## Runtime diagnostics

Normal logs contain deduplicated state transitions for Steam initialization, local Fusion player
discovery, room discovery, peer discovery, P2P handshake, microphone capture, first transmission,
and first playback. An eight-second handshake timeout includes Steam's active/connecting/accepted,
relay, error, and queued-packet state. A capture timeout distinguishes a working voice route from a
microphone that returns no encoded frames. Rejected packets log one bounded reason per peer and
session; encoded audio and microphone samples are never logged.

`Diagnostics.EnableLogging` adds verbose UI/capture details but is not required for the lifecycle
and failure-boundary messages needed in a bug report.

## Compatibility state

The implementation is bound against Sneak Out 1.1.10 interop and has loaded successfully through
the BepInEx chainloader into a real online lobby. Deterministic tests cover packet validation,
out-of-order fragmentation, jitter delay, packet-loss recovery, route-distance attenuation, and
the complete living/ghost audibility matrix.

The 0.4.0 stock-settings UI passed a captured visual smoke run on the live client: the audio panel
showed the voice, transmission-mode, and directional-audio controls with their intended labels and
did not create the removed global volume row. A remote account is required to populate and
visually test the dynamic party-member rows.
The local microphone test validates capture, Opus, gain, and Unity playback without a second
account. Authentication, Steam transport, per-peer jitter, and world propagation still require two
different Steam accounts because a local client cannot authenticate a P2P voice peer to itself.
