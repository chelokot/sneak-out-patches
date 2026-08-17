# Locker Boo Eligibility

`Locker.IsOpen` cannot identify who opened a locker. In client 1.1.10,
`Locker.ComeOut(int)` creates a coroutine whose first step calls
`Locker.Open(playerId, false)`, marks the locker open, clears the occupant, and
only then calls `Locker.HandleBooSkill(int)`. Consequently both a voluntary
penguin exit and a hunter-forced exit are open by the time Boo is evaluated.

The stable distinction exists at the earlier `Open`/`TryToOpen` event:

- `PlayerCurrentlyUsing == playerId`: the occupant is opening their own locker
  as part of `ComeOut`; vanilla Boo handling must run;
- `PlayerCurrentlyUsing != playerId`: another player is opening an occupied
  locker; the next matching occupant exit must not run Boo;
- no occupant, a mismatched occupant, or an unknown call: preserve vanilla
  behavior rather than suppressing a legitimate stun.

`Locker Stun Fix` records the opener and occupant per native locker instance.
The marker is consumed only by the matching occupant's `HandleBooSkill` and is
cleared on close/hide boundaries. Suppressing the entire handler prevents both
the stun and Boo cooldown consumption. The plugin does not manufacture a stun:
when vanilla runs, its own `PenguinBoo` equipped-skill check remains
authoritative.

Every live regular locker displays a persistent cyan floor-level cross-section
of the native Boo overlap query, regardless of occupant, equipped skills, or
cooldown. The marker is anchored to the same serialized
`Interactable.Transform` that the game's `Locker.HandleBooSkill` reads; that
anchor is not necessarily the locker GameObject's root transform.

The client checks a sphere with a `1.25` metre radius centered `1.0` metre in
front of the locker and `0.75` metres above its origin. Rendering the sphere's
widest circle would place that circle `0.75` metres in the air and, under the
isometric camera, misleadingly project it over positions beside the locker.
Instead, the marker shows the sphere's intersection with the horizontal plane
through the locker origin. For a level locker this produces a `1.0` metre
radius centered `1.0` metre forward: it is tangent at the locker origin and has
no lateral reach at the locker's side plane. A line and arrow make its facing
unambiguous.

`HandleBooSkill` does not raycast or clip its query against walls. The marker is
a floor-position guide; the native `Physics.OverlapSphere` still tests collider
intersection and applies its player eligibility filters. `HighlightStunZone`
can disable the marker without disabling the opener-attribution fix.

Each locker also displays a translucent amber interaction area. This is not a
radius invented from the locker mesh: the plugin samples floor positions at a
maximum spacing of `0.15` metres and passes each position through the native
`Interactable.CanInteract` implementation. That predicate raises the candidate
position by `0.5` metres, measures to `Locker._collider.ClosestPoint`, applies
the live `HostDistanceToInteract` outer limit and
`InteractDistanceWithoutRaycast` close-range limit, and performs the game's
raycast checks between those limits. As a result, walls and neighboring
interactables can cut cells out of the displayed area. The shipped values are
`2.2` metres for the outer limit and `0.5` metres for the automatically
accepted close range.

The regular locker collider is `0.76` metres deep and centered `0.01` metres
forward, so its front face is `0.39` metres forward of the interaction anchor.
With a clear raycast directly in front, the outer interaction edge is therefore
`0.39 + 2.2 = 2.59` metres from the anchor. The cyan floor cross-section ends at
`1.0 + 1.0 = 2.0` metres, making the clear forward interaction reach `0.59`
metres longer than the floor-position stun footprint.

The amber area represents where the geometric interaction check accepts a
player. Entering or opening can still be unavailable because of player role,
locker occupancy, or current interaction state; those are action-availability
conditions rather than positions. `HighlightInteractionZone` controls this
marker independently of the cyan stun marker.

Runtime diagnostics emit one `boo-decision` line for every evaluation, including
the exiting player, whether Boo was detected, the allow/suppress result, and the
recorded opener source. This localizes future signature or ordering changes
without logging voice data or unrelated player payloads.

Relevant client 1.1.10 RVAs:

- `Locker.ComeOut(int)`: `0x6D5DF0`
- `Locker.HandleBooSkill(int)`: `0x6D6220`
- `Locker.Open(int, bool)`: `0x6D67E0`
- `Locker.TryToOpen(int)`: `0x6D6A10`
- `Locker+<ComeOut>d__27.MoveNext()`: `0x6E1B30`
