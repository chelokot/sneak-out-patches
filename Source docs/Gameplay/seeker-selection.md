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
