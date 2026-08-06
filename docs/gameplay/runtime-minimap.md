# Runtime minimap

## Scope

`SneakOut.Minimap` is a runtime-only BepInEx plugin. It does not edit `resources.assets`, map scenes, prefabs, or network state.

The default display contains:

- a full-scene floor plan in a circular top-right frame by default;
- distinct colors for ordinary rooms, hallways, spawn, task rooms, and labyrinth rooms;
- matching gold markers for interactive doors and authored pass-through doorway frames;
- cyan point markers for teleporting magic wardrobes;
- yellow point markers for the coin-operated item rollers;
- one local-player arrow with world position and facing;
- a `Tab` key binding, configurable for toggle or hold behavior with the in-game key recorder;
- a stock-styled Map tab in the settings menu.

It deliberately does not show remote players. That keeps the feature informational without exposing opponents whose positions happen to exist in the local Fusion simulation.

## Geometry source

The client has no minimap or radar type. `UnityEngine.AI.NavMesh.Triangulate` appears in the generated interop assembly, but its native implementation is stripped in client 1.1.10 and throws `System.NotSupportedException: Method unstripping failed` at runtime.

The working room source is `Gameplay.Enviro.Room`. Each playable room or hallway has one root trigger collider. The plugin reads those colliders once, converts box-collider corners into world X/Z polygons, rounds their projected corners slightly, fits a square projection with padding, and rasterizes a 512 x 512 texture. `Room` instances without a root trigger are excluded, which removes unrelated table/owl helpers that also use the type.

Interactive doors come from `Gameplay.Interactions.Door._doorInteractableCollider`. The longer horizontal axis of each live interaction volume becomes a short gold line over the room outline. Standard wall doorway lintel renderers supply the complete set of authored door slots; slots with no nearby interaction volume use the same gold line, so both kinds of traversable doorway have one visual language.

Points of interest are also resolved directly from live interactables. `Gameplay.Interactions.MagicWardrobe` positions become cyan dots, while `Gameplay.Interactions.ItemGenerator` positions become yellow dots. The latter is the victim item roller backed by the game's `ItemGeneratorCost` setting. Both use a dark outline so their color survives reduction from the 512 x 512 floor-plan texture to HUD size. This is still runtime geometry: no scene or prefab asset is modified and no state is networked by the minimap.

The circular presentation uses a small runtime `MaskableGraphic` that emits a 64-segment circle directly into the UI stencil. It clips both the dark backing and the floor plan with circular geometry; sprite alpha is not used as a mask. Rectangle mode emits a normal four-vertex stencil. The floor plan is aligned once to the active authored camera yaw (135 degrees on Map02), while the local arrow continues to show the player's facing relative to that fixed view.

No additional camera is created. This matters on Map02, where a second live render would duplicate work across a scene with more than 8,000 renderers.

## Runtime lifecycle

1. `SpookedNetworkPlayer.Spawned` and `Init` capture the local input-authority player.
2. A persistent watcher notices an active scene change and discards the previous generated texture.
3. Five seconds after a `Map*` scene becomes active, it collects that scene's room trigger volumes and creates the floor-plan texture.
4. Each frame updates the local marker and selected zoom window inside the fixed camera-aligned floor plan.
5. `Despawned`, non-map scenes, disabled configuration, or repeated exceptions hide the panel safely.

## Configuration

The in-game Map tab is the normal configuration surface. It reuses the appropriate stock
controls: checkboxes for `EnableMod`, `StartVisible`, and `ShowWhileHolding`; a two-option dropdown for `MapShape`;
sliders for size, zoom, top margin, and right margin; and a Controls-style record/reset row for
`ToggleBinding`. Changes persist immediately through the BepInEx config file; the default
binding is `Tab`. The shape dropdown owns only `Circle` and `Rectangle`; the cloned Video screen
mode selector and resolution-confirmation components are removed.

The backing values remain available in `BepInEx/config/chelokot.sneakout.minimap.cfg` for
manual or deployment-time configuration:

- `EnableMod`: master switch.
- `StartVisible`: visibility applied whenever a playable map is entered.
- `ShowWhileHolding`: when enabled, the map is visible only while the configured key is held; when disabled, each press toggles it and `StartVisible` sets its initial state.
- `MapShape`: `Circle` (default) or `Rectangle`.
- `MapSize`: clamped to 140-500 reference pixels.
- `Zoom`: 0 shows the complete map; 1-100 progressively enlarges the local surroundings.
- `TopMargin` and `RightMargin`: independent screen-edge insets, clamped to 0-300; the right margin defaults to 12.
- `ToggleBinding`: Unity Input System path, defaulting to `<Keyboard>/tab`.
- `EnableLogging`: one-time floor-plan diagnostics and visibility changes.

## Validation

The implementation was compiled against the supported Steam build `24488474` interop assemblies and exercised in private Fusion matches with the authoritative test bot.

- Map02: 37 usable room volumes, 35 live door colliders, and 19 additional pass-through doorway frames; square projection from approximately `(-55.47, -62.91)` to `(55.39, 47.96)`; fixed 135-degree orientation and the true circular stencil were confirmed in a framebuffer capture.
- Map_School02: its structurally different compact room layout generated without map-specific values; floor plan and marker confirmed in a second framebuffer capture.

Both sessions completed without minimap exceptions. The room-volume path is scene-driven and applies to every supported `Map*` scene without per-map textures or coordinate tables.

The injected Map settings page was also exercised in the live lobby. Its six-button category
row, single map icon, native checkbox/slider/key-recording controls, open two-option shape
dropdown, scroll layout, current config values, and stock controller panel lookup were verified
in a framebuffer capture without runtime errors.
