# Mummy Unlock Research

`Mummy` still exists as a full runtime seeker character, but it appears to be cut off at the meta and selection layers rather than removed from gameplay code.

## Confirmed runtime facts

- `Types.CharacterType.murderer_mummy = 12`
- mummy-specific skills still exist:
  - `MummySandTrap`
  - `MummySarcophagus`
- mummy-specific sounds and buffs also still exist in the client

This means the character was not removed from the underlying gameplay implementation.

## Confirmed UI and inventory entry points

These are the main points that currently look relevant for restoring Mummy as a selectable hunter:

- `Collections.PlayerNewMetaInventory.OwnedSeekers`
- `Collections.PlayerNewMetaInventory.LoadOwnedSeekers()`
- `Collections.PlayerNewMetaInventory.DoIOwnThisItem(Enum itemType)`
- `UI.Views.SeekerSelectionViewModel.AvailableSeekers`
- `UI.Views.SeekerSelectionViewModel.Init()`
- `UI.Views.Lobby.CharacterShopView._charactersToBuy`

Together, these strongly suggest a layered flow:

1. backend/meta inventory is loaded into `OwnedSeekers`
2. seeker selection UI gets its list from `AvailableSeekers`
3. character shop UI has a separate serialized list of purchasable hunters
4. ownership checks may still block Mummy later even if it is injected into the visible UI list

## Why a runtime mod is the right path

Raw retail file patching already proved fragile for UI work. For Mummy, a runtime mod is the cleaner route because it allows:

- logging real inventory and selection arrays at runtime
- testing whether adding Mummy to `OwnedSeekers` is enough
- testing whether `AvailableSeekers` must also be patched separately
- testing whether the shop also needs its own list update

## Current research mod

A dedicated BepInEx research mod now exists at:

- `mods/mummy_unlock/`

The current version is diagnostic rather than enabling. It logs:

- `OwnedSeekers` after `LoadOwnedSeekers()`
- `AvailableSeekers` after `SeekerSelectionViewModel.Init()`
- `CharacterShopView._charactersToBuy` when the shop opens
- `DoIOwnThisItem(murderer_mummy)` when that ownership check is reached

## Most likely implementation path

The current best candidate path is:

1. patch `LoadOwnedSeekers()` postfix to append `murderer_mummy`
2. patch `SeekerSelectionViewModel.Init()` postfix to append `murderer_mummy` if still missing
3. patch `CharacterShopView` only if the normal character selection flow still hides Mummy
4. test whether `ConfirmSeekerCharacter(CharacterType)` accepts `murderer_mummy` without additional ownership or validation hooks

## Open questions

- Is Mummy absent from `OwnedSeekers`, or only removed later from `AvailableSeekers`?
- Does `CharacterShopView` still include Mummy in `_charactersToBuy`?
- Does `DoIOwnThisItem(murderer_mummy)` return `false` even after selection UI injection?
- Is there any later server or client validation that rejects `ConfirmSeekerCharacter(murderer_mummy)`?
