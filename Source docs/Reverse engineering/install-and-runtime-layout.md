# Install And Runtime Layout

This document tracks the currently confirmed Steam-side layout for `Sneak Out`.

## Steam app identity

Confirmed app id:

- `2410490`

Confirmed desktop launcher:

- `~/.var/app/com.valvesoftware.Steam/.local/share/applications/Sneak Out.desktop`

Observed launcher target:

- `steam steam://rungameid/2410490`

## Steam library configuration

The current Steam library metadata shows two configured libraries.

### Primary Flatpak Steam library

Path:

- `/var/home/chelokot/.var/app/com.valvesoftware.Steam/.local/share/Steam`

Confirmed by:

- `steamapps/libraryfolders.vdf`
- `config/libraryfolders.vdf`

### Secondary external Steam library

Path:

- `/run/media/chelokot/second/SteamLibrary`

Important detail:

- app `2410490` is registered in this external library

Practical implication:

- the retail game files that were patched during the successful Berek work lived in the external library
- if that mount is missing, the actual game binaries and assets are not accessible from the shell

## Runtime state observed in the current shell

Current observation:

- `/run/media/chelokot/second` is not mounted in the current shell session
- the external `SteamLibrary` is therefore unavailable right now

Practical implication:

- active binary inspection depends on the external drive being mounted
- documentation work can continue from local metadata, history, and prior verified notes even when the library is absent

## Steam metadata artifacts currently accessible

Confirmed accessible artifacts:

- `steamapps/libraryfolders.vdf`
- `config/libraryfolders.vdf`
- `userdata/290321332/config/librarycache/2410490.json`
- `.desktop` launcher entry for the game
- depot manifest cache entries for app `2410490`

## Runtime logs

Historically important runtime log path:

- Proton compatdata path under app `2410490`
- `Player.log` in the `LocalLow/Kinguin Studios/Sneak Out` subtree

Current limitation:

- the exact previously used compatdata log path is not currently available through the missing external library path

## Research workflow implication

When the external Steam library is available:

1. inspect `GameAssembly.dll`
2. inspect `Sneak Out_Data/resources.assets`
3. inspect lobby scene files such as `level0`
4. validate behavior through `Player.log`

When the external Steam library is not available:

1. use repository notes
2. use Steam metadata
3. use local session history
4. record what remains unverified until the library is mounted again

