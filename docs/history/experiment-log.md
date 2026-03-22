# Experiment Log

## Goal

Make `Sneak Out` reliably host and play `Berek` in a current retail build.

## Early stage

### Inspect the installed game and find the binary

Outcome:

- the game installation location was confirmed
- the main binary and related files were identified

### Validate that `Berek` still exists in the client

Outcome:

- `Berek` was confirmed in the code as a real match mode
- crown-specific states and logic were still present
- the older mode selector UI was still present, but hidden

## Failed UI-first route

### Enable hidden mode selector objects in the scene

Outcome:

- Steam still launched the modified build
- interaction behavior changed
- but the UI flow was unstable and could collapse into a black screen

Conclusion:

- scene-only toggles were not a reliable path

### Redirect portal flow into old mode selector UI

Outcome:

- sometimes produced an empty or black screen
- sometimes broke client initialization altogether

Conclusion:

- the selector exists, but restoring it safely is not a one-line fix

## Network and state route

### Force room creation into `Berek`

Outcome:

- the room eventually started reporting `game_mode=Berek`

### Force map selection into berek maps

Outcome:

- the room eventually started launching on berek-compatible maps

### Redirect selection flow into berek states

Outcome:

- the client started showing more berek-specific behavior
- but there were still hybrid states where a seeker was selected

## Wrong-layer character forcing

### Force the selected character into penguin form

Outcome:

- this created distorted mixed states such as two penguins without a proper crown flow

Conclusion:

- forcing character type too early was the wrong abstraction layer

## Critical runtime crash

### Match reaches `HandleBerekModeStart` and crashes

Observed symptom:

- `NullReferenceException` in `GameStartController.InitializeBerekComponents()`

Interpretation:

- the mode was no longer failing at matchmaking or map selection
- it was now failing inside berek-specific startup

## Root cause that unlocked the working patch

### `SpookedNetworkPlayer.EntityBerekComponent` was null

Confirmed facts:

- `UnityPlayer` prefab already had `EntityBerekComponent`
- `SpookedNetworkPlayer` expected a reference to that component
- the relevant slot was not initialized correctly

Fix:

- patch the serialized player prefab wiring in `resources.assets`

Outcome:

- the match ran from start to finish
- the mode became playable end-to-end

## Current known remaining issue

The crown visual still does not appear correctly, even though the mode is now functionally playable.

## Current UI experiment

### Redirect the portal popup into `GameModeViewV2`

Observed facts behind the design:

- `GameModeViewV2` is present in `level0`, but not wired into `GameUIManager`
- `GameModeViewV2` root was disabled, but its own child `Canvas` was already disabled too
- `OpenPortalPlayView()` still ends in a direct jump to `PortalPlayView.Open()`

Current experiment:

- set `GameModeViewV2` root active in `level0`
- repurpose the dormant `OpenGameModeView()` body into a tiny runtime opener for `GameModeViewV2`
- replace the final tail of `OpenPortalPlayView()` with a jump to that helper

Why this experiment is cleaner than older UI attempts:

- it does not swap serialized `PortalPlayView` references to an unrelated type
- it does not rely on restoring the older `GameModeView`
- it changes only the last step of portal opening, after the surrounding lobby cleanup logic already ran

## Runtime noise that should not be overinterpreted

Recent live logs still contain unrelated UI exceptions such as:

- `BattlepassView.OnOnWebplayerRefreshEvent`
- `FinishBattlepassProgressView.SetProgress`

Practical conclusion:

- these exceptions are noisy, but they do not by themselves explain the crown issue
- the useful crown investigation surface is still the berek buff and component path

## Portal mode selector retrofit

### Clone a second row inside the live portal popup

Outcome:

- the popup could be extended without restoring the dead legacy selector
- the lower cloned preferred-role row became functional first
- the upper mode row initially remained partially decorative and partially dead

Conclusion:

- the portal popup uses more than one UI subtree
- cloning only the visible background row was not enough

### Discover the split between `Background` and `GameSettings`

Outcome:

- the visual frame and the live control widgets were confirmed to come from separate scene layers
- this explained the strange mixed states where labels and click behavior did not match

Conclusion:

- future portal UI work must patch both layers together

### Startup crash from `GetComponentAtIndex()`

Observed symptom:

- lobby initialization crashed before interaction with the portal
- the log reported `ArgumentOutOfRangeException: Valid range is 0 to GetComponentCount() - 1`

Cause:

- the custom `OnAwake` helper used a fragile component-index lookup path while registering the upper mode row

Fix:

- keep the lookup narrower
- validate component count
- avoid recomputing the index from the wrong object identity

Practical lesson:

- startup registration code must be more conservative than click-time routing code
- `EventSystem`-based disambiguation is acceptable for click-time logic, but not a good foundation for initialization-time listener wiring

### Re-run selector layout on every popup open

Outcome:

- the selector setup path was changed to re-apply row reparenting, sibling order, and anchored positions every time the popup state check runs
- only the listener registration remains guarded to a one-time path

Reason:

- treating the popup retrofit as fully one-time setup was too fragile
- `PortalPlayView` open flow can leave rows back in their stock arrangement, so the injected mode row must be laid out repeatedly

Conclusion:

- popup retrofit code should separate one-time wiring from repeatable layout work

### Move selector setup from `PrivateGameStateCheck()` to the tail of `PortalPlayView.Open()`

Outcome:

- the selector no longer relies on `PrivateGameStateCheck()` as a layout trigger
- the new hook point is the late tail of `PortalPlayView.Open()`, after the stock popup has already activated and laid out its controls
- the private/public state check path can stay closer to baseline again

Reason:

- `PrivateGameStateCheck()` was too early and too indirect
- it made selector state depend on a side path that was not designed to own popup layout
- `Open()` is the first place that clearly means “the portal popup is now being shown”

Conclusion:

- popup-retrofit patches should prefer explicit late-open hooks over unrelated refresh helpers

### Store the injected mode control as a real button component

Outcome:

- the selector stopped storing a raw `GameObject` for the injected row
- it now stores the actual button component, matching how `_privateGameButton` is stored in `PortalPlayView`
- listener wiring and refresh logic can both derive the needed `GameObject` from that component

Reason:

- keeping only a `GameObject` forced the patch through a fragile `GetComponentCount() / GetComponentAtIndex()` path
- the stock `OnAwake()` code already showed the cleaner model: button field first, `button+0x100` listener target second

Conclusion:

- when retrofitting an extra UI control into an existing view, prefer mirroring the stock field type exactly instead of storing a looser proxy object

### Move the injected mode row off the private/public callback path

Outcome:

- the injected mode row was moved away from `OnPrivateGameButton()`
- the new design hooks `OnChangeRoleButton()` instead and lets the real private/public control keep its original code path
- the mode row is now resolved from `GameModeViewV2.ModePanel` on demand instead of being stashed into a reused serialized field

Reason:

- using `OnPrivateGameButton()` forced the patch to multiplex two unrelated controls through one callback
- that created unnecessary risk around `EventSystem.current` routing and around reused serialized fields such as `_privateGameGamepadIcon`
- the role-toggle callback is the closer semantic match for a second binary toggle row

Conclusion:

- when extending a live popup with one more toggle, prefer piggybacking on the closest stock callback family instead of hijacking an unrelated control path

### Remove startup-time selector setup hooks

Outcome:

- startup-time selector setup hooks were dropped from the `mode-selector` patch
- only the click-path wrapper and `OnPlay()` mode loader remain in `GameAssembly.dll`

Reason:

- the startup hook path was the most fragile part of the retrofit
- earlier failures included `GetComponentAtIndex` crashes and later native startup exceptions even when the hook bytes themselves were formally valid
- the portal popup can be retrofitted through scene structure plus click-time routing without touching early lobby setup

Conclusion:

- when a retrofit can be split into scene structure and click-time logic, prefer that over startup-time Unity API calls

### Stop cloning TMP objects for the injected portal row

Outcome:

- the selector patch no longer relies on cloned `TextMeshProUGUI` objects for the injected mode row
- the cloned row now reuses existing text nodes from the hidden `GameModeViewV2` subtree

Reason:

- cloned TMP components looked structurally valid but crashed at runtime in `TMP_FontAsset.UpgradeFontAsset()` during `PortalPlayView.Open()`
- this proved that the cloned layout objects were acceptable, while cloned TMP text objects were not
- reusing already-scene-valid TMP nodes is safer than manufacturing new TMP instances in a raw asset clone

Conclusion:

- for Unity retail asset patching, cloned visual layout objects are often safe, but cloned TMP text components deserve separate suspicion and should be replaced with reused live nodes when possible
