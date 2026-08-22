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

## Current implementation

A dedicated BepInEx mod owns the complete Mummy compatibility boundary at:

- `mods/mummy_unlock/`

It owns:

- Mummy ownership and selector availability
- selector, character-shop, perk-store, HUD, and player-list portraits
- the character-shop description and missing localization
- an independent three-slot passive registry backed by a Mummy-only JSON store
- the borrowed Reaper passive catalog, descriptions, modifiers, and tier-five cards
- sarcophagus visuals, placement, interaction anchors, teleport ordering, and wardrobe-style animations
- the network-avatar fallback required by the retail schema

Mummy perk selections are stored in `chelokot.sneakout.mummy-unlock.json`, partitioned by profile. On first load, the mod imports existing `runtime:12` selections from Unlock Everything's legacy persistence file so the ownership migration does not discard equipped perks.

## Ownership boundary

`Mummy Unlock` is the only assembly allowed to mention `murderer_mummy` or patch Mummy-specific gameplay. `Unlock Everything` no longer changes `OwnedSeekers`, creates character products, or synthesizes missing paid hunters. It continues to max skill cards and unlock cosmetics for characters the real profile already owns.

The only characters synthesized by Unlock Everything are Penguin and Reaper when its explicitly enabled local-stub mode has no usable profile at all. They are the base playable pair needed to keep that emergency fallback structurally valid; normal profile overlay mode preserves the server's character list exactly.

## Runtime limitation

The retail `CharactersSkills` network value has fields for six stock character registries and no Mummy field. Mummy therefore keeps its passive selection locally. The local input-authority player resolves equipped-state and modifier lookups from the Mummy registry while Reaper remains the source of the shared modifier curves.
