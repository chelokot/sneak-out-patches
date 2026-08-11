# Performance work for client 1.1.10

This is the measurement record behind `performance-optimizer` and the Linux installer changes for Steam build `24488474`, client `1.1.10`, Unity `2022.3.62f3`. Defaults are included only when they produced a repeatable gain or removed an observed hitch.

## Test setup

- Flatpak Steam, Proton 10, DXVK/D3D11
- Intel Core i5-12500H, NVIDIA RTX 4050 Laptop, 1920x1080 at 144 Hz
- real private Fusion session on Map02 with one authoritative test bot
- deterministic traversal through the same rooms
- one-second host sampling for CPU, RSS, cgroup I/O, GPU load, VRAM and power
- opt-in in-game frame histograms and narrow managed-method profiling
- Linux `perf` sampling for native/Unity/DXVK work

Diagnostic measurements use the runtime profiler's opt-in interval reports alongside
host process, GPU, and cgroup sampling. Keep production measurements on the default
no-I/O path unless the session explicitly needs detailed CSV output.

## Host state mattered

The laptop had accidentally been left in `tuned-adm powersave`, with turbo disabled and clocks around 1.5 GHz. Measurements from that state, including an apparent 15 FPS Map02 ceiling, did not describe the game or mod correctly. Tests below use the persistent `balanced` profile with turbo enabled.

On Linux the installer now adds `gamemoderun` when GameMode is available. It is scoped to Sneak Out's Steam launch command and does not change the desktop's global power profile. In the controlled Map02 route it raised average frame rate from 71.49 to 88.40 FPS and improved average p95 frame time from 20.53 to 15.20 ms. GPU utilization rose to an 87% average and 100% maximum, which means the workload was finally reaching the GPU instead of being throttled earlier in the pipeline.

## Measured runtime result

All rows below use quality level 2 and 100% render scale.

| Map02 configuration | Average FPS | p50 frame | p95 frame |
| --- | ---: | ---: | ---: |
| Additional-light shadows on, vSync off | 45.36 | 21.63 ms | 30.95 ms |
| Adaptive shadows off, 144 Hz target | 71.49 | 13.35 ms | 20.53 ms |
| Same settings plus Linux GameMode | 88.40 | 11.10 ms | 15.20 ms |

The display is 144 Hz, but the client had `Application.targetFrameRate=60`. `TargetFrameRate=0` now follows the current display refresh rate; `-1` preserves the game value and a positive value is an explicit cap. This does not force resolution, fullscreen mode or window position.

Additional-light real-time shadows were the largest repeatable graphics cost. Disabling them improved the controlled comparison by about 27%. `Auto` preserves normal visuals initially, measures a fixed in-memory frame histogram, and disables additional-light shadows and vSync only after a settled sustained deficit. On a 144 Hz display the adaptive floor is the greater of the configured minimum and 65% of the target; a severe deficit can trigger after one interval so the match starts in the recovered state.

## The regular freezes were telemetry I/O

The optimizer itself was creating a regular hitch. Its diagnostic CSV path called synchronous `File.AppendAllText` from Unity's main thread. In one otherwise healthy GameMode run, frame-time peaks appeared at 43.1, 53.1, 63.1, 73.1, 83.2 and 93.2 seconds; the corresponding maximum frames were 49, 48, 42, 44, 53 and 48 ms. The ten-second spacing matched the report flushes exactly.

Production behavior now keeps the fixed frame histogram in memory and performs no interval filesystem write. `WriteReportsDuringGameplay=false` is the default. Detailed room events, network samples, native frame timing calls and callback stopwatches are also skipped on that path. A best-effort final snapshot is attempted only on a clean Unity shutdown. Diagnostic sessions may explicitly re-enable CSV output, accepting that the measurement mechanism can perturb frame time.

The normal per-frame path was tightened further:

- scene identity is compared by integer handle, avoiding an IL2CPP scene-name conversion every frame;
- Fusion RTT uses the already captured local player's runner instead of a globally sorted `FindObjectsOfType` scan;
- room/light context is collected only for diagnostic reports;
- the lobby test bot and Start Now cache their authoritative objects instead of repeatedly scanning Unity's global object registry.

## Startup

Two independent startup costs were removed.

1. `PreloadIL2CPPInteropAssemblies=true` eagerly loaded the complete generated interop set. Disabling global preload reduced observed warm chainloader time from roughly 90 seconds to 15–22 seconds.
2. Reinstall used to delete the generated `interop` and `unity-libs` caches, forcing Cpp2IL on the next launch. The installer now preserves them only when the native binary, metadata and BepInEx assembly-hash cache key match the supported build.

A measured reinstall with a valid cache took about 0.4 seconds. A genuinely new game build still regenerates interop by design.

## Map02 investigation

Map02 is one Unity scene (`level5`), not streamed room scenes. Its approximate live inventory is:

- 8,175 renderers and 10,200 material slots;
- 6,309 static-batched renderers and 645 skinned renderers;
- 152 animators, 128 marked `AlwaysAnimate`;
- 228 particle systems and 215 lights;
- 2,090 colliders;
- no LOD groups.

The game keeps rooms registered and changes `SceneCameraManager.CurrentRoom`; transitions toggle serialized room lights. Native profiles after the fixes were dominated by Unity render submission and DXVK work rather than a remaining managed update loop. A narrow managed audit found `SteamManager.Update` at 0.067 ms per call, both `UnityModulesRunner` loops below 0.025 ms, `MainThreadActionExecutor.Update` at 0.007 ms and camera updates around 0.003 ms.

There is still a one-time roughly 196–225 ms transition from `BeforeSelection` to `Selection`. Temporary managed callback timers accounted for less than 1 ms, so the remaining cost is in Unity/native activation, resource creation or graphics-pipeline work. Broad `Shader.WarmupAllShaders()` was tested and rejected: it stalled startup for 56.4 seconds, raised RSS to 4.69 GB, wrote roughly 436 MB and did not eliminate the transition.

The scene also emits about 300 missing-script warnings and 41 negative `BoxCollider` warnings from shipped assets. Correcting those safely requires authored scene changes, not a generic runtime deletion pass.

MagicaCloth2 also warns twice that a null transform was added to its job array. A focused 60-second Map02 profile ruled it out as the steady-state bottleneck: the four active cloth callbacks averaged 0.002–0.006 ms per call over 5,172 frames, with a 0.406 ms maximum and no exception. Removing clothing physics to silence that warning would therefore trade visible behavior for no meaningful frame-time gain.

## Rejected defaults

These remain explicit options where useful, but are not automatic defaults:

- 90% render scale produced only a small 2–4% improvement and visibly reduces image quality;
- low quality did not materially improve the balanced-machine route;
- disabling all additional lights added only about 4% beyond the shadow fix and materially changed the scene;
- hard instead of soft shadows was noise-sized;
- material-instancing, animator-culling and broad room-culling experiments were either neutral or unsafe;
- global shader warmup was catastrophically expensive.

At the final 88 FPS state the GPU is commonly 87–100% utilized. Substantially higher performance without a visual tradeoff now needs changes to the game's authored assets: LOD groups, fewer active renderers/lights/colliders, fixed missing scripts and explicit room activation metadata.

## Presets and safety

- `ObserveOnly`: no quality or frame-pacing changes.
- `Auto`: keeps the selected quality, follows display refresh and applies only the measured shadow/vSync fallback after a sustained deficit.
- `Balanced`: preserves quality and applies safe loading/upload settings plus explicit overrides.
- `LowSpec`: opts into shorter shadows, fewer cascades and particles, disabled real-time reflection probes and optional mip streaming for memory-constrained PCs.

The optimizer never sets resolution, fullscreen mode or window position. It preserves every distinct width/height pair reported by the display, so modes with the same width but different aspect ratios remain selectable, and repairs the resolution-dropdown lookup without changing the active mode. Heavy scene census, Unity `ProfilerRecorder` markers and interval CSV reports are diagnostic-only because all three can distort the workload they are meant to observe.
