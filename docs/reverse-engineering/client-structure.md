# Client Structure

## Main files

### `GameAssembly.dll`

What it is:

- the main native IL2CPP client binary

What to look for there:

- match state machine
- host flow
- matchmaking
- UI flow
- gameplay logic that is not visible in asset files

### `Sneak Out_Data/resources.assets`

What it is:

- prefabs and part of the serialized data

What to look for there:

- `UnityPlayer` prefab
- MonoBehaviour serialized slots
- links between components on prefabs

### `Sneak Out_Data/level0`

Why it matters:

- the lobby scene lives here
- old UI views such as the mode selector can physically still exist here even if they are disconnected from the current flow

### `Player.log`

What it is:

- the main runtime client log

Why it matters:

- verify the real `game_mode`
- verify `scene_type`
- catch state machine crashes
- distinguish "the wrong mode arrived" from "the right mode arrived and then broke later"

## Practical workflow

1. inspect `Player.log` first
2. then decide whether the issue belongs to UI, session properties, state machine, or prefab wiring
3. if the crash happens late and is tied to a player component, inspect not only `GameAssembly.dll` but also prefab wiring in `resources.assets`

## Useful artifacts

Temporary IL2CPP dumps may exist in `/tmp`, but they should not be treated as long-term storage. Final findings should be copied into this repository.
