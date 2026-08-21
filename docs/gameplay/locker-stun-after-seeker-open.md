# Locker Boo Balance

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

`Locker Stun Fix` replaces only that spatial query. While vanilla
`HandleBooSkill` is running, the plugin:

1. performs a conservative broad-phase overlap around the complete locker
   collider;
2. resolves every candidate player's network position at the same `+0.5`
   metre interaction height used by the local prompt resolver;
3. measures from that point to `Locker._collider.ClosestPoint`;
4. retains candidates at or below `1.2` metres.

The resulting Boo zone is the opening-distance rounded rectangle with a true
`0.2` metre margin on every edge and corner. Any hunter position satisfying the
`1.0` metre opening-distance test also satisfies the `1.2` metre Boo test.

The plugin no longer suppresses Boo when another player opens an occupied
locker. Vanilla remains authoritative for the equipped `PenguinBoo` check,
target role and life-state filters, stun dispatch, and cooldown consumption.
Like stock Boo, this spatial test does not add wall occlusion.

Every regular locker displays two persistent floor guides:

- cyan outlines the balanced `1.2` metre rounded-rectangle Boo zone;
- amber samples the client prompt resolver at a maximum `0.15` metre spacing.

The amber resolver visualization uses the live `LocalDistanceToInteract`, the
`InteractDistanceWithoutRaycast` close-range rule, and the local player's
interaction raycast mask. Walls, neighboring interactables, or another selected
candidate can therefore make the visible amber region smaller than its raw
`1.0` metre rounded rectangle. The cyan outline deliberately represents Boo's
non-occluded spatial rule.

`HighlightStunZone` and `HighlightInteractionZone` control the two guides
independently without disabling the balance change.

Relevant client 1.1.10 RVAs:

- `Locker.HandleBooSkill(int)`: `0x6D6220`
- `EntityInteractiveComponent.FindInteractables()`: `0x671390`
- `EntityInteractiveComponent.ResolveSelectedInteractiveComponent(int)`: `0x6791E0`
- `Physics.OverlapSphere(Vector3, float, int, QueryTriggerInteraction)`:
  `0x38DC080`
