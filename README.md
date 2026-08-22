# Sneak Out Mods

Runtime mods for **Sneak Out**.

## Install

Download the graphical installer for your platform from the latest GitHub release:

- `SneakOutPatches-windows-x86_64.exe` on Windows
- `SneakOutPatches-linux-x86_64` on Linux
- `SneakOutPatches.app` from `SneakOutPatches-macos-universal.zip` on macOS when
  Windows Steam and Sneak Out are installed in a Sikarugir wrapper

Use the installer to choose or remove mods.

On Linux, make the downloaded binary executable before running it:

```sh
chmod +x SneakOutPatches-linux-x86_64
./SneakOutPatches-linux-x86_64
```

The macOS helper finds the Sikarugir Steam wrapper and opens the bundled Windows GUI
inside it. Quit Sneak Out and Windows Steam before opening the installer. The app is
not Developer ID signed or notarized; if necessary, Control-click it and choose
**Open** after verifying the official release download.

## Mods

### Core

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `uniform-seeker-random` | Uniform Seeker Random | Yes | Gives every player an equal chance of being selected as the seeker. |

### Performance

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `performance-optimizer` | Performance Optimizer | Yes | Improves startup times, frame pacing, loading, memory use, rendering, and network performance. |

### Gameplay

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `portal-mode-selector` | Portal Mode Selector | Yes | Lets you choose Classic or Crown maps from the portal while keeping unfinished maps unavailable. |
| `mummy-unlock` | Mummy Unlock | No | Restores Mummy as a complete selectable hunter with portraits, perks, and corrected sarcophagus behavior. |
| `alternate-skill-hotkey` | Alternate Skill Hotkey | No | Lets you press Left Alt to use your character's unequipped alternate active skill. |
| `prop-buff` | Prop Buff | Yes | Lets you change your prop with the mouse wheel while remaining stationary. |
| `first-person-experiment` | First Person Experiment | No | Adds an experimental first-person mode with mouse look, immersive hiding and task views, a top-center stamina bar, and hold-X cursor release. |
| `locker-stun-fix` | Locker Stun Fix | Yes | Suppresses Boo when another player opens an occupied locker, while retaining a balanced 1.2 metre stun zone and optional diagnostic guides. |
| `magic-wardrobe-hook-fix` | Magic Wardrobe Hook Fix | Yes | Prevents a Butcher hook from snapping you back to a magic wardrobe after interrupting your entry. |
| `chair-wall-throw-fix` | Chair Wall Throw Fix | Yes | Lets you throw held chairs, barrels, and ingredients even when they overlap a wall or exceed the usual distance. |
| `pumpkin-radius-indicator-fix` | Pumpkin Radius Indicator Fix | Yes | Shows accurate kill and stun radius rings when the pumpkin activates. |
| `ripper-corner-blink-fix` | Ripper Corner Blink Fix | Yes | Fixes the Ripper's through-wall blink at room corners without changing the normal blink. |
| `proximity-voice-chat` | Proximity Voice Chat | Yes | Adds low-latency Opus proximity voice with microphone selection, push-to-talk, voice activation, outgoing and per-player volume controls, a delayed solo microphone monitor, optional directional audio, routed wall and door occlusion, and separate living and ghost channels. |
| `lobby-skill-sandbox` | Lobby Skill Sandbox | Yes | Lets you open the skill panel and safely practice sliding in the lobby. |

### Quality of life

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `network-host-selector` | Leader Host | Yes | Makes the party creator host private matches when every player has the compatible mod; public matchmaking keeps the assigned host. |
| `quick-reconnect` | Quick Reconnect | Yes | Enables the game's dormant reconnect-and-rejoin flow after a Photon timeout. Relayed non-host clients are not supported. |
| `minimap` | Minimap | Yes | Adds a configurable minimap showing rooms, objectives, hiding spots, item dispensers, and your position. |
| `start-delay-reducer` | Start Now | Yes | Lets the host start the match immediately after the normal connection wait. |
| `background-loading-guard` | Background Loading Guard | Yes | Allows loading to continue while the game is in the background and restores your previous setting afterward. |
| `keyboard-layout-fix` | Keyboard Layout Fix | Yes | Keeps physical WASD controls working on Cyrillic keyboard layouts under Wine and updates key labels when layouts change. |
| `friend-invite-unlock` | Friend Invite Unlock | Yes | Sends Steam game invites to offline friends and adds Steam overlay Invite to Play and Join Game support for lobby parties. |
| `community-discord` | Community Discord | Yes | Replaces the existing lobby Discord statue invite URL with the configured community invite. |

### Progression

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `unlock-everything` | Unlock Everything | Yes | Unlocks emotes and skill cards, and lets you buy and keep every cosmetic for 1,000 Gold without changing hunter ownership. |

### Debug

| Id | Name | Default | Description |
| --- | --- | :---: | --- |
| `lobby-test-bot` | Lobby Test Bot | No | Lets the host add a test bot, choose it as the Classic-mode hunter, start private test matches, and use Plus to switch the camera, movement, interaction, attack, skill, and emote controls. |
| `runtime-profiler` | Runtime Profiler (Debug) | No | Creates a performance report when the game closes to help diagnose slow mods and game functions. |

## Command line

Running `sneakout-patches` without arguments opens the graphical installer. Pass an
action for noninteractive use:

```bash
sneakout-patches --install-mods=default
sneakout-patches --install-mods=default,mod1,mod2
sneakout-patches --install-mods=all
sneakout-patches --remove-mods
sneakout-patches --no-update
```

## Development

```bash
make mods-build
make installer-test
make installer-build
make installer-build-dev
```
