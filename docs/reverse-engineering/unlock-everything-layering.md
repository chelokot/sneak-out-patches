# Unlock Everything Layering

## Goal

`Unlock Everything` should not reimplement the game's equip flows.

The mod only needs to provide:

1. unlocks and maxed meta data
2. local apply instead of dead backend requests
3. persistence load on startup
4. persistence save after a successful local apply

Anything outside these four responsibilities is a code smell.

## Correct layers

### 1. Ownership

This layer answers:

- does the player own the item
- what owned item id should the game use
- which lists should include the item

Expected hooks:

- `PlayerNewMetaInventory.DoIOwnThisItem(Enum itemType)`
- `PlayerNewMetaInventory.GetOwnedItemId(Enum itemType)`
- product/meta expansion only when a screen enumerates from product lists

This layer must not perform equip side effects.

### 2. Apply

This layer answers:

- when the game wants to equip something, how do we turn that into an immediate local success without the backend

Expected hooks:

- `KinguinverseWebService.PutOnSkinPart(int characterId, int skinPartId)`
- `KinguinverseWebService.PutOnSkillCard(int characterId, int skillCardSlot, int skillCardId)`
- `KinguinverseWebService.PutOnAvatar(int characterId, int avatarId)`
- `KinguinverseWebService.PutOnAvatarFrame(int characterId, int avatarFrameId)`
- `KinguinverseWebService.PutOnCharacterDescription(int characterId, DescriptionType descriptionType)`

This is the preferred control point because the UI already resolved:

- the selected item
- the target character
- the intended action

If possible, all local apply logic should live here.

### 3. Persistence

This layer answers:

- what should be restored when the client starts
- what should be written after a local apply succeeds

Expected hooks:

- `LocalSelectionsStore.ApplySelections(WebPlayer player)`
- `LocalSelectionsStore.SaveCharacterSelection(Character character)`
- focused save helpers for skin parts or whole skins only when needed

Persistence must operate on stable profile state and must not become the main source of live equip behavior.

### 4. Live sync

This layer exists only when the game's runtime keeps a second copy of the same state.

Examples:

- skin preview/player customization data
- network player avatar/frame/title mirrors
- live skill registry derived from `CharactersSkills`

This layer should mirror already-applied local state into runtime caches.
It should not invent its own domain model.

### 5. UI refresh

This layer is last.

Only use it when:

- apply already succeeded
- persistence already succeeded
- live sync already succeeded
- and the screen still does not repaint

This layer should never contain business logic.

## Current strategic mistake

The current codebase drifted because several entity types are handled from the wrong layer:

- skills are partially handled in `MainBoostersViewModel.ChangeEquippedSkill` and `PlayerNewMetaInventory.OnTreeSkillChange`
- avatars are partially handled in `AvatarAndFrameView.EquipModification` and `PlayerNewMetaInventory.OnAvatarModyficationChange`
- skins are handled across inventory, preview, and persistence paths

That made the mod depend on lossy UI wrappers instead of stable backend-facing apply methods.

## What should stay special

### Skins

Skins really do need extra live sync because the game keeps separate preview/runtime state for:

- `CustomizeCharacterNewMetaView`
- `PlayerCustomizationView`
- `SpookedPlayerCharacterData`

So skins are the exception that legitimately needs:

- apply
- persistence
- live sync
- limited UI refresh

### Skills and avatars

These should be much simpler than skins.

For them the preferred architecture is:

1. unlock in ownership layer
2. local success in `PutOn*`
3. save persistence
4. sync one live runtime mirror if needed

They should not require large custom UI reimplementations.

## Current misplaced patches

These are suspicious because they sit above the preferred apply layer:

- `PlayerNewMetaInventory.OnTreeSkillChange`
- `MainBoostersViewModel.ChangeEquippedSkill`
- `PlayerNewMetaInventory.OnAvatarModyficationChange`
- `AvatarAndFrameView.EquipModification`

These are closer to the intended layer:

- `KinguinverseWebService.PutOnSkinPart`
- `KinguinverseWebService.PutOnAvatar`
- `KinguinverseWebService.PutOnAvatarFrame`
- `KinguinverseWebService.PutOnCharacterDescription`

`PutOnSkillCard` is the missing piece that should become the primary control point for skill equip.

## Rules for future work

1. Choose one entity type at a time.
2. Write down the intended layer before writing code.
3. Prefer `PutOn*` hooks over higher-level UI wrappers.
4. Use UI hooks only to capture selection when the lower layer loses information.
5. Do not mutate `Character.SkillCards` manually if the game already exposes `CharactersSkills`.
6. Do not treat `Enum.ToString()` or generic `Il2CppSystem.Enum` as authoritative data on hot paths.
7. When a patch above the apply layer is required, it must be justified as selection capture or UI refresh, not business logic.
