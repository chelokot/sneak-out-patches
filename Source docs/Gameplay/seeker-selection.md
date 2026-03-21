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
- the four default-mode thresholds are loaded inside `GetRandomSeeker()` from a small `.rdata` constant block
- the first threshold is the `0.1f` constant at raw offset `0x357E7C0`

## Minimal uniform-random patch

The smallest practical patch for removing the default fairness buckets is:

- `GameAssembly.dll`
- raw offset `0x357E7C0`
- `cdcccc3d` -> `0000803f`

That changes the first threshold from `0.1f` to `1.0f`.

Practical effect:

- preferred-role filtering still happens first
- almost all normal players still land in the first bucket
- the final pick inside that bucket remains the game's existing fair uniform random selection

This does not fully rewrite the selection function. It deliberately leaves edge cases such as `GamesFinishedCount == 0` to the original fallback behavior.
