# Locker Open Attack Cooldown

There is a real attack cooldown when a seeker opens a locker.

This is not only an animation lock or an input feeling issue.

## What happens

In `EntityInteractiveComponent.Interact()` (`RVA 0x6573E0`, `VA 0x1806573E0`), the code checks for:

- current actor is a seeker via `CharacterTypeExtension.IsSeeker()` (`RVA 0x7EF910`)
- `interactionType == OpenLocker` (`5`)

When both are true, the function reads `ISpookedGameplaySettings.AttackCooldown` from settings and writes it into the seeker's `EntityMovementComponent.AttackCooldown` field at offset `0xEC`.

The current retail value in `SpookedGameplaySettings` is `1.5`.

The same path immediately syncs the value to the network state.

That logic runs before the interaction is dispatched to the locker-specific handler.

## Why the seeker cannot hit immediately

`EntityMovementComponent.CanAttack()` (`RVA 0x65F040`, `VA 0x18065F040`) starts by checking `AttackCooldown`:

- if `AttackCooldown > 0`, it returns `false`
- only when the value reaches `0` does the rest of the attack gate continue

So after `OpenLocker`, the seeker is explicitly put on cooldown and `OnKillPressInput()` cannot proceed into the attack RPC path.

## What this is not

This is separate from the locker hide / come-out buffs:

- `BlockInputsForLockerHide = 110`
- `BlockInputsForLockerComeOut = 111`

Those buffs are part of the locker interaction flow itself.

The seeker-side problem described by players is explained earlier: opening the locker explicitly applies the normal seeker `AttackCooldown`.

## Relevant functions and values

- `EntityInteractiveComponent.Interact()` — `RVA 0x6573E0`
- `EntityMovementComponent.CanAttack()` — `RVA 0x65F040`
- `EntityMovementComponent.SetAttackCooldownAfterInteraction()` — `RVA 0x65FF60`
- `InteractionType.OpenLocker = 5`
- `ISpookedGameplaySettings.AttackCooldown`
- `sharedassets0.assets:3208 (Gameplay) -> AttackCooldown = 1.5`

## Current conclusion

Yes, the cooldown exists.

More precisely:

- opening a locker as a seeker applies the attack cooldown from gameplay settings
- the current gameplay settings value is `1.5` seconds
- this happens before locker-specific interaction handling
- that cooldown creates the timing window in which a penguin can come out and trigger the locker stun interaction first
