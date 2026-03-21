# Evidence Sources

This document classifies the source types used in the current reverse-engineering work.

## Highest-confidence sources

### Live binary and asset inspection

Examples:

- `GameAssembly.dll`
- `Sneak Out_Data/resources.assets`
- scene assets such as `level0`

Why they matter:

- they describe the exact build being patched
- they are the authoritative source for byte-level patching
- they are the strongest source for final conclusions about implementation

## High-confidence sources

### Runtime logs

Examples:

- `Player.log`

Why they matter:

- they show what the client actually did at runtime
- they help distinguish the correct room mode from a late runtime failure
- they were critical in narrowing the Berek crash to `InitializeBerekComponents()`

## Medium-confidence sources

### Temporary IL2CPP dump artifacts

Examples:

- `dump.cs`
- `script.json`
- `il2cpp.h`
- `stringliteral.json`

Why they matter:

- they are extremely useful for naming, field layouts, and method discovery
- they are less reliable as permanent references because they usually live in `/tmp`
- they should be treated as transient working artifacts and summarized into the repo

## Supporting sources

### Steam metadata

Examples:

- `libraryfolders.vdf`
- app library cache json
- launcher desktop entry

Why they matter:

- they confirm app identity and library placement
- they help recover paths when the actual game library is temporarily unavailable

### Session history and shell snapshots

Examples:

- `~/.codex/history.jsonl`
- `~/.codex/shell_snapshots/`

Why they matter:

- they help recover findings after context compaction
- they are useful for reconstructing investigative steps
- they should not override direct binary evidence when the binary is available

## Practical evidence policy for this repo

Preferred order:

1. live binary or asset evidence
2. runtime logs
3. temporary IL2CPP dumps
4. Steam metadata
5. recovered session history

## What should always be copied into the repo

Whenever a temporary source produced an important conclusion, the durable part should be copied here:

- function purpose
- field meaning
- offsets used in patches
- known failure mode
- why a patch worked or failed

That prevents future work from depending on `/tmp` or on chat memory.

