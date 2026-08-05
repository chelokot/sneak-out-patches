# Gameplay geometry fixes

These fixes target three independent client 1.1.10 defects. They are separate plugins so each can fail open or be disabled without changing the other two.

## Chair release beside a wall

`Gameplay.Interactions.Chair.Throw(int)` enables collisions, unparents the held chair, and applies the stock forward impulse. The held chair can become unavailable before that call when its release pose overlaps level geometry, producing the crossed-out action prompt. If the request does reach `Throw`, physics can also re-enable while the chair still intersects the wall.

`chair-wall-throw-fix` keeps the throw interaction available for the player already holding the chair. On Fusion state authority, immediately before the stock method enables physics and applies velocity, five non-allocating rays sweep the chair collider's forward envelope from the player-side anchor to the held pose. If any ray reaches intervening geometry, the chair center is clamped before the first contact by its projected collider radius. This also catches a chair whose animation has already carried its center completely through a wall; an endpoint-only overlap test cannot. The original throw, collision, force, stamina, and hit logic still run unchanged.

The current client strips `Physics.BoxCastAll` from its IL2CPP player even though the interop reference exposes the managed signature. Version 0.1.6 attempted that API and therefore failed open with `Method unstripping failed`. The replacement deliberately uses `Physics.RaycastNonAlloc`, the same runtime-supported physics path used by the game's own forward detector. The older bounded overlap correction remains only as a fallback when the envelope rays find no intervening surface.

## Pumpkin trigger, kill, and stun indicators

The authoritative `PumpkinBomb.Tick()` query and the instant-kill branch of the explosion coroutine both read `Gameplay.GetSkillSettings(ScarecrowPumpkinBomb).Range`. The outer stun query separately adds `ScarecrowBombStunRange`. `PumpkinBomb.ShowRange(bool)` only activates the prefab indicator; it never aligns its scale to those live settings.

In the shipped `resources.assets`, `VFX_PumpkinEngageRange` has local scale 3 while its pumpkin parent has scale 0.8, producing a world radius of 2.4 instead of the configured 3. `pumpkin-radius-indicator-fix` derives local scale from the live range divided by the parent's lossy scale, so backend/config changes remain authoritative. The persistent hunter-only ring now matches the trigger radius. When a victim triggers the bomb, two copies of the stock ring effect appear: the full-opacity kill radius and the outer stun radius at 20% opacity. The plugin does not alter damage, stun, triggering, targeting, or line-of-sight checks.

## Ripper blink through a shared corner

`EntitySkillsComponent.OnRipperBlink()` raycasts from the player toward the full skill range and clips the destination to the first non-trigger hit before validating NavMesh. Asset inspection supplies the missing distinction: the client defines a dedicated `Intersections` layer (15), and Map02 contains four 2.0 x 1.5 x 0.26 box colliders on it under the `Colliders` room-junction object. Those residual junction strips are why the wall-blink perk can cross normal walls but still stop on a diagonal four-room junction.

`ReaperHelloThere` is the existing perk that switches blink to the game's through-wall collision mask. `ripper-corner-blink-fix` is inert unless that perk is equipped. With the perk active, it uses the same live base range, perk range modifier, through-wall mask, ground projection, and 0.2 m NavMesh validation. It bypasses the remaining clipping only when:

- at least one pre-destination blocker is on the dedicated `Intersections` layer;
- the uncut destination is valid NavMesh.

A normal blink without the perk, a path without the room-junction strip, and an invalid destination all keep the original behavior. Once the strip is present, ordinary geometry reported by the raycast does not make the patch stricter than the equipped through-wall perk. A floor hit within 0.05 m of the path endpoint is treated only as the already validated destination surface.

## Validation status

The policies have deterministic tests for bounded chair correction, parent-scale compensation, perk gating, intersection paths, endpoint floors, and ordinary geometry coexisting with the junction. All plugins compile against the current generated interop. Per the active testing constraint, no game process was launched for this pass; gameplay interaction still requires a later runtime matrix.
