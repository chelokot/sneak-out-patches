# Locker Boo Eligibility and Balance

`Locker.IsOpen` cannot identify who opened a locker. `Locker.ComeOut(int)`
opens the door with the occupant's player id before `HandleBooSkill(int)` runs,
so both a voluntary exit and a forced exit are open when Boo is evaluated.

`Locker Stun Fix` records the opener and current occupant at the earlier
`TryToOpen(int)` and `Open(int, bool)` calls. When a different player opens an
occupied locker, the marker is consumed by the matching occupant's next
`HandleBooSkill(int)` call and that handler is skipped. This prevents both the
stun and Boo cooldown consumption. A voluntary self-exit, empty locker,
mismatched occupant, or unavailable open preserves vanilla Boo behavior.
Markers are cleared at close and hide boundaries so they cannot leak into a
later locker cycle.

Client 1.1.10 gives locker opening and Boo different spatial shapes. The local
interaction resolver discovers a locker when the player's interaction-height
point is within `1.0` metre of `Locker._collider`. For a regular `BoxCollider`,
that closest-point rule produces a rounded rectangle around the locker rather
than a circle centered on its transform.

The stock Boo query does not share that shape. `Locker.HandleBooSkill(int)` uses
a `1.25` metre `Physics.OverlapSphere` centered `1.0` metre forward and `0.75`
metres above the serialized interaction transform. Its sideways reach at the
locker plane can therefore omit a hunter who was close enough to open the
locker from its side.

For Boo activations that remain eligible, the plugin replaces only the spatial
query. While vanilla `HandleBooSkill` is running, the plugin:

1. performs a conservative broad-phase overlap around the complete locker
   collider;
2. resolves every candidate player's network position at the same `+0.5`
   metre interaction height used by the local prompt resolver;
3. measures from that point to `Locker._collider.ClosestPoint`;
4. retains candidates at or below `1.2` metres.

The resulting Boo zone is the opening-distance rounded rectangle with a true
`0.2` metre margin on every edge and corner. Any hunter position satisfying the
`1.0` metre opening-distance test also satisfies the `1.2` metre Boo test.

Except for the forced-open case above, vanilla remains authoritative for the
equipped `PenguinBoo` check, target role and life-state filters, stun dispatch,
and cooldown consumption. Like stock Boo, this spatial test does not add wall
occlusion.

The optional diagnostic overlay provides two persistent floor guides:

- cyan outlines the balanced `1.2` metre rounded-rectangle Boo zone;
- amber samples the client prompt resolver at a maximum `0.15` metre spacing.

The amber resolver visualization uses the live `LocalDistanceToInteract`, the
`InteractDistanceWithoutRaycast` close-range rule, and the local player's
interaction raycast mask. Walls, neighboring interactables, or another selected
candidate can therefore make the visible amber region smaller than its raw
`1.0` metre rounded rectangle. The cyan outline deliberately represents Boo's
non-occluded spatial rule.

Both guides are off by default. To enable or disable them, edit
`BepInEx/config/chelokot.sneakout.locker-stun-fix.cfg` and set either option in
the `[visuals]` section:

```ini
HighlightStunZone = true
HighlightInteractionZone = true
```

Set a value to `false` to hide that guide. Restart the game after editing the
file. These options are visual only; `EnableMod` controls the gameplay change.

When patching the Boo query, target the three-argument
`Physics.OverlapSphere(Vector3, float, int)` overload. `HandleBooSkill` calls
that overload directly; patching the four-argument overload that also accepts
`QueryTriggerInteraction` produces a valid patch that is never reached.

Relevant client 1.1.10 RVAs:

- `Locker.HandleBooSkill(int)`: `0x6D6220`
- `EntityInteractiveComponent.FindInteractables()`: `0x671390`
- `EntityInteractiveComponent.ResolveSelectedInteractiveComponent(int)`: `0x6791E0`
- `Physics.OverlapSphere(Vector3, float, int)`:
  `0x38DC080`
