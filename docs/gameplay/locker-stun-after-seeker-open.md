# Locker Boo Eligibility

In client 1.1.10, `Locker.ComeOut(int)` opens the locker before its coroutine
eventually calls `Locker.HandleBooSkill(int)`. Looking at `IsOpen` inside
`HandleBooSkill` is therefore too late: both exit paths are open by then.

`Locker Stun Fix` captures `IsOpen` synchronously at the `ComeOut` call:

- closed at exit start: the one matching `HandleBooSkill` call may run;
- already open at exit start: `HandleBooSkill` is skipped entirely;
- no matching captured exit: fail closed and skip the handler.

Skipping the entire handler is intentional. It prevents both the hunter stun
and Boo cooldown consumption when the hunter has already opened the locker.
The record is keyed by the native locker pointer and player id, consumed once,
and cleared when a new hide cycle begins.

Relevant client 1.1.10 RVAs:

- `Locker.ComeOut(int)`: `0x6D30D0`
- `Locker.HandleBooSkill(int)`: `0x6D3500`
