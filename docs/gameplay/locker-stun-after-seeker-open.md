# Locker Stun After Seeker Open

`Locker.ComeOut(int)` cannot use `Locker.IsOpen` alone to decide whether the
locker stun is valid. In client 1.1.10, the coroutine performs this sequence:

1. call `Locker.Open(playerId, false)`
2. set `IsOpen = true`
3. clear the current locker occupant
4. call `Locker.HandleBooSkill(playerId)`

Consequently, `IsOpen` is true for both a normal penguin exit and an exit that
was forced by a seeker.

The distinguishing event is `Locker.TryToOpen(int)`. It is reached through the
seeker's `OpenLocker` interaction while the locker is still closed, before the
forced `ComeOut` flow. `Locker Stun Fix` records that event per locker instance,
suppresses only the next `HandleBooSkill` call for that instance, and consumes
or clears the marker before a new hide cycle.

Relevant client 1.1.10 RVAs:

- `Locker.ComeOut(int)`: `0x6D30D0`
- `Locker.HandleBooSkill(int)`: `0x6D3500`
- `Locker.TryToOpen(int)`: `0x6D3CF0`
