# Seeker Selection

This applies to the default match start flow, not to `Berek`.

## Algorithm

The game does not choose a hunter uniformly across all players.

What it does:

1. takes only players with `CanBeSeeker = true`
2. computes `GamesPlayedAsSeeker / GamesFinishedCount` for each one
3. splits players into buckets
4. takes the first non-empty bucket with the smallest ratio
5. chooses uniformly at random inside that bucket

## Buckets

- `< 0.1`
- `< 0.2`
- `< 0.4`
- `< 0.6`
- everything else

Priority goes top to bottom:

1. below 10%
2. then below 20%
3. then below 40%
4. then below 60%
5. then everyone else

## Special case

If `GamesFinishedCount == 0`, the player goes into the last fallback bucket instead of getting top priority.

## Practical meaning

- the game tries to pick the player who has been hunter less often relative to total finished matches
- preferred role affects the result through `CanBeSeeker`
- if multiple players are in the same bucket, the final choice is a fair random pick among them

## Important caveat

`GetRandomSeeker()` has a separate branch for `Berek`, so this logic should not be assumed to apply to crown mode.

## Confirmed implementation points

- `ShouldStartState.GetRandomSeeker()` is at `GameAssembly.dll` RVA `0x6A2E60`
- `ShouldStartState.RandomizeSeeker()` is at `GameAssembly.dll` RVA `0x6A36B0`
- the first two thresholds are loaded from `.rdata` near `0x357E7C0`
- the `0.4` and `0.6` thresholds are loaded from separate `.rdata` addresses
- the first threshold load itself is the `movss` at VA `0x1806A318F`

## Minimal uniform-random patch

The safe practical patch is to retarget only the first threshold load inside `GetRandomSeeker()`, not to overwrite the shared global `0.1f` literal.

Patch:

- `GameAssembly.dll`
- raw offset `0x6A1D8F`
- `f3440f101528cced02` -> `f3440f101559cced02`

That changes the first bucket check to load an existing `1.0f` constant instead of the shared `0.1f` constant.

Practical effect:

- preferred-role filtering still happens first
- almost all normal players still land in the first bucket
- the final pick inside that bucket remains the game's existing fair uniform random selection

This does not fully rewrite the selection function. It deliberately leaves edge cases such as `GamesFinishedCount == 0` to the original fallback behavior.

The older global-literal patch at `0x357E7C0` is unsafe because that `0.1f` value is reused by many unrelated systems.
