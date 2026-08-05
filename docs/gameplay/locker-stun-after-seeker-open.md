# Locker Boo Eligibility

`Locker.IsOpen` cannot identify who opened a locker. In client 1.1.10,
`Locker.ComeOut(int)` creates a coroutine whose first step calls
`Locker.Open(playerId, false)`, marks the locker open, clears the occupant, and
only then calls `Locker.HandleBooSkill(int)`. Consequently both a voluntary
penguin exit and a hunter-forced exit are open by the time Boo is evaluated.

The stable distinction exists at the earlier `Open`/`TryToOpen` event:

- `PlayerCurrentlyUsing == playerId`: the occupant is opening their own locker
  as part of `ComeOut`; vanilla Boo handling must run;
- `PlayerCurrentlyUsing != playerId`: another player is opening an occupied
  locker; the next matching occupant exit must not run Boo;
- no occupant, a mismatched occupant, or an unknown call: preserve vanilla
  behavior rather than suppressing a legitimate stun.

`Locker Stun Fix` records the opener and occupant per native locker instance.
The marker is consumed only by the matching occupant's `HandleBooSkill` and is
cleared on close/hide boundaries. Suppressing the entire handler prevents both
the stun and Boo cooldown consumption. The plugin does not manufacture a stun:
when vanilla runs, its own `PenguinBoo` equipped-skill check remains
authoritative.

Runtime diagnostics emit one `boo-decision` line for every evaluation, including
the exiting player, whether Boo was detected, the allow/suppress result, and the
recorded opener source. This localizes future signature or ordering changes
without logging voice data or unrelated player payloads.

Relevant client 1.1.10 RVAs:

- `Locker.ComeOut(int)`: `0x6D30D0`
- `Locker.HandleBooSkill(int)`: `0x6D3500`
- `Locker.Open(int, bool)`: `0x6D3AC0`
- `Locker.TryToOpen(int)`: `0x6D3CF0`
- `Locker+<ComeOut>d__27.MoveNext()`: `0x6DEE10`
