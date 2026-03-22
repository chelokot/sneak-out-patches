# Patching Methodology

## Goal

Keep binary and asset patches repeatable, explainable, and narrow enough to debug when they fail.

## Stronger workflow

The most reliable workflow so far is:

1. restore a clean baseline from the script-managed backups
2. apply candidate patches to a temporary copy first
3. validate the exact bytes, texts, and serialized references on the temporary copy
4. only then apply the same patch set to the retail installation
5. verify the live result with `Player.log` and, when needed, the crash dump path

This already prevented several bad iterations from being written directly into the installed build.

The patcher now also includes a static post-apply validator. It reconstructs the expected patched files from the clean backups, compares them against the installed files, and runs `GameAssembly.dll` checks on the known patched regions.

## What worked better than raw one-off hex edits

### Treat raw offsets and code RVAs as separate domains

One class of mistakes came from mixing:

- raw file offsets in `GameAssembly.dll`
- virtual addresses inside the loaded image
- RVAs reported by `dump.cs` and stack traces

Practical rule:

- compute helper call and jump targets in VA space
- write patch bytes in raw-file space
- convert explicitly instead of doing mental arithmetic inline

### Validate patch preconditions against known clean bytes

Every binary patch in the script should keep:

- a precise `before` byte pattern
- a precise `after` byte pattern
- a short human explanation

If the `before` pattern no longer matches, the patch should abort immediately instead of trying to be clever.

### Validate the patched result, not just the clean input

Precondition checks are not enough. A patch can start from the right clean bytes and still generate a broken output.

The current validator checks:

- the installed files exactly match the deterministic result rebuilt from the clean backups and the selected patch set
- every known executable patch region in `GameAssembly.dll` fully disassembles
- hook targets for the selector loader and selector wrapper resolve to the expected helper stubs
- helper stubs keep sane ABI invariants:
  - stack alignment before `call`
  - allowed stack deltas on `ret`
  - allowed tail-jump targets
  - no RIP-relative writes back into executable sections

This is strong enough to catch malformed `GameAssembly.dll` edits before blaming Proton or Unity runtime behavior.

### Prefer asset-level edits for layout and serialized wiring

Layout and reference problems were usually easier to express in Unity assets than in code:

- moving portal rows
- renaming labels
- cloning UI rows
- pre-wiring `EntityBerekComponent`

These changes are usually more stable than reimplementing the same effect in assembly.

### Use code patches for flow, not for content

Binary code patches worked best when they only changed:

- which mode value is sent downstream
- which lobby id is passed into a join call
- which state transition is taken
- whether a specific handler is registered

They were much less robust when they tried to synthesize missing UI or reconstruct large higher-level behaviors from scratch.

## Important lessons from recent failures

### `PortalPlayView` is not a single UI layer

The visible popup is split into at least two relevant scene layers:

- `Background`
- `GameSettings`

Patching only one of them produced hybrid states:

- duplicated rows
- wrong labels
- working visuals with dead controls

Future UI edits should always inspect both the decorative subtree and the live control subtree before cloning or moving rows.

### Diff ranges are not instruction boundaries

One failed validator iteration tried to disassemble raw changed-byte ranges from the clean file diff.

That was wrong because a diff range can begin in the middle of an unchanged instruction and produce a false alarm.

Practical rule:

- disassemble known patched regions
- do not disassemble generic contiguous diffs unless they are explicitly aligned to instruction boundaries

### A `Transform` is not a `GameObject`

One startup crash came from walking a transform hierarchy and then calling `GameObject.GetComponentAtIndex()` on a `Transform` pointer.

Practical rule:

- when the code path returns a `Transform`, explicitly convert through `Component.get_gameObject()`
- do not assume child traversal returns a `GameObject`

### `GetComponentAtIndex()` is fragile

Another startup crash came from using a component index derived from a different object than the one later passed to `GetComponentAtIndex()`.

Practical rule:

- prefer direct serialized references when they exist
- if index-based lookup is unavoidable, validate `GetComponentCount()` first and keep the object identity stable across the lookup chain

### Event-system inference is useful, but should stay narrow

Using the current selected object from `EventSystem` helped distinguish:

- the original preferred-role row
- the injected mode row

This worked for click-time routing, but it is a poor fit for startup registration logic.

Practical rule:

- use `EventSystem` only for click-time disambiguation
- use explicit traversal or serialized references for startup-time listener registration

## More robust directions for future work

### Keep using a structured patch script instead of ad-hoc hex editing

The current patcher is already more robust than manual hex edits because it:

- restores a trusted baseline before every apply
- verifies hashes
- validates `before` bytes
- centralizes patch descriptions

That should remain the default path.

### Raise the abstraction level inside the patcher, not by rebuilding the game

The game is IL2CPP, so a full project reconstruction is still unrealistic. The more practical middle ground is:

- keep binary patches as small stubs
- move the patch intent into Python helpers and named constants
- document every patched function, field offset, and asset path

This does not eliminate assembly work, but it makes it auditable and easier to update.

### Prefer reusable helper stubs over repeated inline patch logic

When multiple patches need custom code:

- allocate a known helper area once
- keep small, single-purpose helpers there
- jump or call into them from original methods

This is easier to reason about than scattering slightly different inline byte edits across unrelated call sites.

### Consider higher-level runtime modding only if the game tolerates it

A more robust alternative to file patching would be a runtime hook layer:

- BepInEx-style loader
- Harmony-like managed patches
- native detours with symbol maps

This would be more maintainable than raw byte editing, but only if the retail build and Proton setup tolerate injected loaders cleanly. That is still unproven for this game, so file patching remains the current practical default.

## Current practical recommendation

For this project, the best balance is still:

- asset patches for UI structure and prefab wiring
- minimal assembly patches for flow redirection and value selection
- strict baseline restoration before every apply
- temporary-copy validation before touching the live install

That is not high level in the Unity-editor sense, but it is the most robust approach currently available for this IL2CPP retail build.
