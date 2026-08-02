# Performance overhaul for client 1.1.10

This document records the measurements behind `performance-optimizer`, the startup changes in the installer, and the automated lobby-to-match harness. The supported target is Steam build `24488474`, client `1.1.10`, Unity `2022.3.62f3`.

The goal is not to apply every plausible Unity tweak. Each default change below either produced a repeatable improvement or removes a measured startup cost. Experiments that did not improve frame time were removed from the shipped plugin.

## Test setup

- Linux, Flatpak Steam, Proton 10, DXVK/D3D11
- Intel Core i5-12500H, 16 logical CPUs
- NVIDIA RTX 4050 Laptop, 6 GB VRAM
- 1920x1080
- one-second host sampling for CPU, RSS, threads, file descriptors, cgroup I/O, GPU load, VRAM and power
- ten-second in-game aggregate rows for FPS, frame-time percentiles, stutters, GC, Unity quality/URP state and Fusion RTT
- narrow Harmony method profiling only after a configurable warmup
- real private Fusion sessions driven by the stock portal callbacks and one authoritative test bot

Run a capture and summarize it with:

```bash
npm run performance:session -- --duration-seconds 120 --session my-test
npm run performance:analyze -- .tmp/performance-sessions/<session>
```

The runner owns one exact game PID, restores the window-manager compatibility value, copies only reports changed by the run, and closes the test client at the end. The lobby bot diagnostic settings can open the portal, add the bot, choose a map and invoke the real private `PLAY` callback without manual input.

## Startup

Two independent startup costs were found.

1. `PreloadIL2CPPInteropAssemblies = true` loaded the complete generated interop set before plugins needed it. Warm launches took roughly 90 seconds to reach the chainloader in the observed configuration. Disabling global preload reduced the warm path to roughly 15–22 seconds.
2. A normal reinstall deleted BepInEx's generated `interop` and `unity-libs` directories. The next launch then ran Cpp2IL again, taking about 90 seconds in the observed cold run and peaking near 6.4 GB RSS.

The installer now disables global preload and preserves both cache directories only when the native game fingerprints and BepInEx `assembly-hash.txt` key match the supported build. A measured reinstall with a valid cache took about 0.4 seconds, and the following launch skipped Cpp2IL, reached the lobby during a 65-second session, and peaked at 2.69 GB RSS.

The generated `Assembly-CSharp.dll` PE hash is intentionally not the cache identity: identical interop regeneration can produce a different whole-file PE hash. The BepInEx cache key plus native binary and metadata hashes is the reproducible compatibility gate.

## Lobby rendering result

The largest repeatable lobby graphics cost was additional-light real-time shadows in URP.

| High-quality lobby configuration | Average FPS | Change |
| --- | ---: | ---: |
| Stock/observe-only | 31.97 | baseline |
| vSync off only | 34.61 | +8.3% |
| Additional-light shadows off | 46.20 | +44.5% |
| Hard shadows, additional shadows retained | 33.71 | +5.4% |
| Low quality | 46.21 | +44.5%, large visual tradeoff |

`Auto` keeps the selected quality level and visuals at first. After the active scene has settled for at least 15 seconds, sustained performance below the configured threshold disables additional-light shadows and, when the game was preserving vSync, disables vSync. On the same machine an end-to-end adaptive run averaged 42.85 FPS across intervals that included both the pre- and post-fallback states.

`LowSpec` is deliberately more aggressive. In the Map02 comparison it reduced peak RSS from 3.02 GB to 2.76 GB and peak VRAM from 2.73 GB to 2.16 GB. That is useful for memory-constrained PCs, but it did not materially improve Map02 frame time on this CPU-bound machine.

## Map02 bottleneck

The automated harness repeatedly loaded Map02 with a real Fusion session and bot. The scene contains approximately:

- 8,175 renderers and 10,200 material slots
- 6,309 static-batched renderers
- 645 skinned renderers
- 152 animators, 128 marked `AlwaysAnimate`
- 228 particle systems
- 215 lights
- 2,090 colliders
- no LOD groups

Only about 760–910 renderers were visible in the sampled frames, while four room layers each contained hundreds or thousands of renderers. The current client does not expose a sufficiently reliable active-room-to-layer mapping for a safe generic culling patch.

Map02 stayed around 15.4–15.9 FPS while GPU utilization was commonly below 25%. The following experiments were rejected because the differences were noise-sized or visually destructive:

| Experiment | Average FPS |
| --- | ---: |
| High, shadows fallback active | 15.41 |
| Render scale 50% | 15.82 |
| Low quality | 15.77 |
| Disable all additional lights | 15.89 |
| Force material instancing | 15.75 vs 15.88 control |
| Offscreen animator culling crossover | 15.24 vs 15.59 control |

The narrow Fusion profile also ruled out networking as the dominant steady-state cost. Over a 60-second post-warmup window, `NetworkRunnerUpdaterDefault.InvokeUpdate` averaged 5.62 ms, including 1.96 ms average fixed-network update time; relay update averaged 0.08 ms. The approximately 60–65 ms frame time therefore remains dominated by native scene/render traversal that retail managed method hooks cannot safely isolate further.

For that reason the shipped default does not force render scale, material instancing, animator culling, all-light removal, or speculative room culling. Fixing Map02 substantially requires authored scene work: LOD groups, room activation/culling metadata, fewer active colliders/lights, and correction of the hundreds of missing-script and negative-collider warnings in the game assets.

## Telemetry overhead and safety

Normal telemetry uses a fixed histogram and writes one aggregate row per interval; it performs no per-frame disk writes or list allocation. Native `FrameTimingManager` returned zero CPU/GPU timings under the tested DXVK backend, so sampling disables itself after 120 empty frames.

The full scene census and Unity `ProfilerRecorder` markers are opt-in. The retail IL2CPP/Wine build hung under broad recorder instrumentation, and scanning thousands of scene objects can itself create a hitch. They are diagnostic tools, not normal-play defaults.

The optimizer also removes null serialized `Room.Lights` entries immediately before the game's `Room.OnAwake` cache path. This is a narrow guard for malformed scene data; it does not suppress the room handler.

## Presets

- `ObserveOnly`: telemetry only; no quality or frame-pacing changes.
- `Auto`: chooses `Balanced` or `LowSpec` from RAM, VRAM and CPU count, then applies the measured shadow/vSync fallback only after sustained low FPS.
- `Balanced`: preserves the selected quality level, prioritizes background loading below gameplay, keeps the async upload buffer, and permits explicit overrides.
- `LowSpec`: selects quality level 0 unless explicitly overridden, shortens shadow distance, limits cascades and particle work, disables real-time reflection probes and soft particles, and can optionally enable mip streaming.

Every individual override remains available in `BepInEx/config/chelokot.sneakout.performance-optimizer.cfg`. Texture streaming is off by default because it only helps assets authored with streaming mipmaps.

## Display-mode safety

The optimizer does not set the resolution, fullscreen mode, or window position. An early profiling
session accidentally left the diagnostic command line
`-screen-fullscreen 0 -screen-width 1280 -screen-height 720` in Steam launch options. Because
Unity command-line values override the saved game settings, every later launch was forced to
1280×720 windowed mode. The installer now removes that exact legacy diagnostic override while
preserving unrelated user launch options.

Sneak Out 1.1.10 also throws from `ResolutionSelector.RefreshShownValue` when the active mode is
not present verbatim in its dropdown array. The optimizer now detects that invalid state and shows
the nearest dropdown entry without changing the active display mode. A live 1920×1080 exclusive
fullscreen test opened Video Settings in 33.7 ms without the previous exception.

## Verified automatic match path

The final unattended tests performed the following real flow in both Classic and Crown:

1. authenticate and enter the online lobby;
2. spawn an authoritative Fusion bot as player ref 2;
3. switch the stock portal to a private game;
4. invoke the real `PLAY` callback;
5. resolve and join the match session;
6. create a Photon game session with the requested authoritative `game_mode` property;
7. load Map02 for Classic or Map School 2 for Crown;
8. respawn and register the bot's real `NetworkObject` in the match.

The final Classic session contained `game_mode=Default`; the final Crown sessions contained
`game_mode=Berek` in both `StartGameArgs` and live Photon `SessionInfo`. Both modes reached their
requested map with player ref 2 registered, no BepInEx error, and no `NullReferenceException`.
The bot remains intentionally inert and animation-less. The transition guard only skips danger-audio
work during the short interval in which the local-player registry has already been cleared, and the
match-start guard skips victim staging that requires a fully authored animation character.

The same automation opens the real portal UI and can capture its framebuffer. At 1280×720 the final
layout keeps the map grid and `CLASSIC`/`CROWN`, `DONE`, bot, and stock `PLAY` controls inside the
portal modal instead of placing raw buttons across the game view.
