# Patch History

## What did not work

### Simply enabling the old UI mode selector

Attempt:

- enable the old `GameModeView` objects in the scene
- redirect the portal into the old mode selection screen

Result:

- it caused a black screen or broke early client initialization
- the old UI still exists in the build, but it is not safely integrated into the current flow

### Global force of `GameState.get_GameMode() = Berek`

Attempt:

- hard-force the runtime getter to return `Berek`

Result:

- it broke the client before the normal lobby loaded
- the logs showed early DI/Autofac failures

### Force `CharacterType -> victim_penguin`

Attempt:

- replace the selected player's type with a penguin too early

Result:

- it produced the `two penguins without a crown` state
- this was the wrong layer to patch

## What worked

### Network mode and map

Working part:

- force `Berek` into the host flow
- force host map selection into the berek branch
- redirect `BeforeSelectionState` into `BerekSelectionState`

Result:

- `game_mode=Berek`
- `scene_type` switches into a berek map

### Berek component wiring

Working part:

- stop forcing more state machine behavior
- fix the real crash source in `InitializeBerekComponents()`
- bind `EntityBerekComponent` to `SpookedNetworkPlayer` through the prefab asset

Result:

- the berek match runs from start to finish

## Logs that were critical

Main runtime log:

- `Player.log` in `compatdata/2410490/.../LocalLow/Kinguin Studios/Sneak Out/Player.log`

Most useful markers:

- `game_mode=Berek`
- `scene_type=Map05_TagGame` or another berek map
- `Chosen seeker`
- `NullReferenceException` in `GameStartController.InitializeBerekComponents()`
