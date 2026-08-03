# Magic Wardrobe Return After Butcher Hook

The large teleporting wardrobe is `Gameplay.Interactions.MagicWardrobe`. It is
not the ordinary hiding `Gameplay.Interactions.Locker`.

When a player begins entering a magic wardrobe, the client runs
`EntityInteractiveComponent.InteractWithMagicWardrobe(...)`. Its generated
`_InteractWithMagicWardrobe_d__76.MoveNext()` coroutine caches the wardrobe
entry position and moves the player toward it over several frames. A Butcher
hook can take over player movement while that coroutine is still alive. If the
wardrobe coroutine resumes after the pull, it applies its cached movement again
and returns the player to the wardrobe.

`Magic Wardrobe Hook Fix` tracks only `Hide` instances of that exact wardrobe
coroutine. A hook hit is recorded only while the hooked player has such an entry
in progress. On the next step for that same player, the patch completes the
stale coroutine without changing the player's position. The normal hook
movement remains authoritative.

Relevant client 1.1.10 RVAs:

- `ButcherHook.OnTriggerEnter(Collider)`: `0x639B80`
- `EntityInteractiveComponent.InteractWithMagicWardrobe(...)` coroutine
  `MoveNext()`: `0x68B3E0`
- `MagicWardrobe.ComeOut(int)`: `0x6D40B0`
- `MagicWardrobe.ForceStopInteraction(int)`: `0x6D4130`
