## Emotes And Dances

The client has a real per-character emote configuration layer in the player profile. This is not cosmetic metadata that can be left empty.

### Profile model

- `PlayerEmotions.AllEmotions`
  - the full owned emote inventory for the account
- `Character.Emotions`
  - six equipped wheel slots for a specific character
- `Character.Dance`
  - the currently selected dance for a specific character
- `Character.Fart`
  - the currently selected fart emote for a specific character

Relevant client-side references:

- [`Character.Emotions`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L716156)
- [`Character.Fart`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L716159)
- [`Character.Dance`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L716160)
- [`CharacterEmotions`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L716263)
- [`PlayerEmotions`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L717944)

### Web helpers that consume this data

Lobby spawn details use `WebPlayerExtensions` helpers:

- [`GetMyCurrentEmotes`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68395)
- [`GetMyCurrentDance`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68399)
- [`GetMyCurrentAvatar`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68395)

This matters in practice because an incomplete synthetic `WebPlayer` can crash lobby startup in `SceneSpawner.ShareMyDetails()` on `GetMyCurrentDance(...)`.

### Emote categories

The client explicitly classifies emotes:

- [`IsDanceType`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68374)
- [`IsFartType`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68377)
- [`IsGeneralEmote`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L68380)

There are also dedicated backend/profile write endpoints:

- [`PutOnCharacterFart(...)`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L709941)
- [`PutOnCharacterDance(...)`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L709944)
- [`PutOnEmotion(...)`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L709959)

### Known emote content in the client

The retail client contains these `EmoteType` groups:

- Penguin:
  - `emotion_penguin_wave`
  - `emotion_penguin_follow_me`
  - `emotion_penguin_like`
  - `emotion_penguin_jump`
  - `emotion_penguin_fart_1..10`
  - `emotion_penguin_dance_1..20`
- Reaper:
  - `emotion_reaper_dance_1..3`
  - `emotion_reaper_laugh_1..3`
- Scarecrow:
  - `emotion_scarecrow_dance_1..3`
  - `emotion_scarecrow_laugh_1..3`
- Dracula:
  - `emotion_dracula_dance_1..3`
- Ghost:
  - `emotion_ghost_buu`

Reference:

- [`EmoteType`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L717822)

### Hidden / suspicious content

- `emotion_ghost_buu` exists in the client enum, while the web-side profile model also contains `CharacterType.Ghost`.
- This looks like leftover or partially hidden ghost-related content.
- No confirmed live playable ghost path has been established yet.

### Useful built-in defaults

The gameplay/client-side `Types.CharacterTypeExtension` exposes:

- [`GetDefaultEmotesForCharacter(...)`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L23230)
- [`GetDefaultDanceForCharacter(...)`](/var/home/chelokot/Documents/Projects/Sneakout/.tmp/il2cppdumper/dump.cs#L23234)

These are safer sources for synthesizing profile defaults than hand-picking emotes, as long as the web-side character type is mapped to the client-side character enum first.
