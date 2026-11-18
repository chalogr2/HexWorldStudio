# Architecture — Hex World Studio v1

This document describes the major subsystems, how they communicate, the request-to-render flow, the on-disk layout of our code, and the small set of core types that anchor the design. Read [`CLAUDE.md`](CLAUDE.md), [`FEATURES.md`](FEATURES.md), and [`DECISIONS.md`](DECISIONS.md) first.

## Guiding constraints

These are imported from the brief and are non-negotiable for v1 unless an ADR supersedes them:

1. **Data and rendering are completely separate.**
2. **ScriptableObject-driven content.**
3. **Deterministic generation.** Same seed + same settings → byte-identical `WorldMap`.
4. **Public API stable; internals can change.**
5. **No magic.** No reflection-based wiring, no metaprogramming.
6. **Zero third-party dependencies.**

## Subsystems

```
┌──────────────────────────────────────────────────────────────────────┐
│                        CUSTOM EDITOR (Editor/)                       │
│  - HexWorldGeneratorWindow                                           │
│  - Property drawers, inspectors                                      │
│  - Diagnostic overlays                                               │
└────────────────┬─────────────────────────────────────┬───────────────┘
                 │                                     │
                 │ invokes                             │ inspects
                 ▼                                     │
┌────────────────────────────────┐          ┌─────────▼──────────────┐
│      RUNTIME API (Runtime/)    │          │  CONTENT (ScriptableOb-│
│  - HexWorldGenerator           │  reads   │  jects, authored in    │
│  - WorldGenerationSettings     │◀─────────│  Editor inspectors)    │
│  - Progress / cancellation     │          │   BiomeDefinition      │
└────────────────┬───────────────┘          │   ResourceDefinition   │
                 │ produces                 │   TileFeatureDefinition│
                 ▼                          │   WorldPreset          │
┌────────────────────────────────┐          └────────────────────────┘
│      DATA MODEL (Runtime/)     │
│  - WorldMap (class, mutable;   │
│    contract documented)        │
│  - HexTile (class), HexCoord   │
│  - RiverNetwork (edge segments │
│    + tile segments)            │
│  - Serialization (JSON/binary) │
└────────────────┬───────────────┘
                 │ consumed by
                 ▼
┌────────────────────────────────┐
│   VISUALIZATION (Runtime/      │
│   Rendering/ - optional;       │
│   buyers may swap)             │
│  - WorldRenderer (MonoBehaviour│
│  - Per-biome instanced meshes  │
│  - Navigable river tile meshes │
│  - Non-navigable river ribbons │
│  - Resource prop placement     │
└────────────────────────────────┘
```

The **generator** writes to the **data model**. The **visualization** reads from the data model. The visualization never calls back into the generator. Buyers may delete `Assets/HexWorldStudio/Rendering/` and `Assets/HexWorldStudio/Examples/` entirely and still have a working generator + data layer.

## Generation pipeline (deterministic, single-threaded for v1)

The pipeline is a fixed ordered sequence of *stages*. Each stage takes the in-progress `WorldMap` plus settings and mutates the map. All randomness flows through a single seeded RNG. No stage may use ambient sources of nondeterminism (system time, `UnityEngine.Random`, hash codes from unstable sources, parallel-for ordering).

```
WorldGenerationSettings (seed, dimensions, preset, content refs)
        │
        ▼
[1] RngService — initialize from seed
        │
        ▼
[2] CoordinateSpace — allocate axial grid, set up neighbor topology
        │
        ▼
[3] ContinentShape — determine land/water mask
        │
        ▼
[4] ElevationField — assign per-tile elevation
        │
        ▼
[5] ClimateModel — temperature (latitude + elevation)
        │              moisture (winds + distance-from-water)
        ▼
[6] BiomeAssignment — Whittaker-style classification from SO content
        │
        ▼
[6a] BiomeCohesion — neighbor-consensus smoothing of single-tile
        │              speckling (ADR-0019); no-op at strength 0
        ▼
[6b] TileFeaturePlacement — weighted Gaussian-falloff feature pick
        │              per biome (ADR-0014)
        ▼
[6c] FeatureCohesion — same smoothing for features, restricted to
        │              the tile's biome AllowedFeatures
        ▼
[6d] ResourcePlacement — opt-in resource placement on eligible tiles
        │              per ADR-0022; no-op when Resources roster
        │              empty or ResourceDensity = 0
        ▼
[6e] RiverStage — depression-fill the elevation field, derive lakes
        │              from the filled basins, grow a descent tree
        │              INLAND FROM THE SEA over the hex-corner graph,
        │              then read rivers off it: channels along a
        │              terrace, single-edge waterfalls across terrace
        │              boundaries (ADR-0025). Growing from water is what
        │              makes every river reach it. Writes
        │              `WorldMap.RiverEdges` and the ordered
        │              `WorldMap.RiverPaths`. Opt-out when
        │              RiverDensity = 0. Navigable / non-navigable
        │              dual representation deferred to Slice 17.2.
        ▼
[9] StartingPositions — fairness-aware seat selection
        │
        ▼
[10] PostProcess — smoothing, isolated-tile cleanup
        │
        ▼
WorldMap (caller is free to mutate; see "Data model contract")
```

Stages are internal types implementing a small `IGenerationStage` interface. The orchestrator is the only thing that knows the stage order. Buyers do **not** reorder stages in v1 — that's a planned v1.1 extensibility surface.

**Each stage role is pluggable** (ADR-0010): multiple alternative implementations of `IGenerationStage` can be authored for the same role (e.g., continent shaping has at least Pure Perlin Threshold, Continents, Pangaea, Archipelago, Fractal, Inland Sea, Island Plates, Lakes, Highlands, etc. all implementing the same interface). The active implementation per slot is selected by `WorldGenerationSettings` initially and by `WorldPreset` ScriptableObjects later. v1 ships one default implementation per slot; additional Civ-style algorithms are tracked in `FEATURES.md` under "Map type algorithms" and land in dedicated slices.

**Per-tile classification is three-axis** (ADR-0011): `Biome` (climate band, from a `BiomeDefinition` SO), `TileFeature` (terrain detail, from a `TileFeatureDefinition` SO chosen from the biome's allow-list), and `ElevationLevel` (discrete int derived from the continuous `Elevation` field via a quantization + smoothing pass that produces contiguous plateaus). The discrete level supports the default Civ-style "move ±1 elevation level" traversal rule for buyer gameplay code.

**Climate model uses prevailing-wind moisture** (ADR-0012): the default `ClimateStage` computes temperature from latitude with an elevation lapse-rate modifier, and moisture by simulating east-to-west wind transport across each cylindrical row (water tiles charge the wind with moisture; land tiles deposit it via orographic precipitation; high-elevation tiles produce rain shadows on their leeward side). This is the v1 baseline; latitude-banded wind variants and other Civ-style climate models land as alternative `IGenerationStage` implementations later per ADR-0010.

**Biome content is fully modular**: `WorldGenerationSettings.Biomes` accepts any `IReadOnlyList<BiomeDefinition>`. The default 7-biome content set (Ocean, Coast, Temperate, Arid, Tropical, Tundra, Snow per ADR-0011) is shipped under `Assets/HexWorldStudio/Content/Biomes/` and may be replaced wholesale, extended, or shrunk by buyers. The only structural requirements (validated at `Generate`) are at least one water biome, at least one coastal-water biome (`IsWater && IsCoastalWater`), and at least one non-water biome. Fewer or more than seven biomes is fine; fantasy biomes are first-class per ADR-0007.

**Renderer uses pointy-top hex orientation** (ADR-0013): the default `WorldRenderer` and `HexMesh` builder produce pointy-top hexes in odd-r offset layout. `HexCoord` itself remains orientation-agnostic; the orientation choice lives in the rendering layer alone. Outer radius (`OuterRadius`, default 1 world unit) is the center-to-vertex distance; inner radius (`InnerRadius`, `√3 / 2`) is center-to-edge. Horizontal tile-to-tile spacing is `2 * InnerRadius`, vertical row-to-row spacing is `1.5 * OuterRadius`, and odd rows shift right by `InnerRadius`. Tiles are positioned vertically by `ElevationLevel * ElevationStep` so the discrete elevation bands are visible in the demo scene. Flat-top variants are out of scope for v1; see ADR-0013.

**Cylindrical render wrap + camera normalization** (ADR-0015): `WorldWrapRenderer` produces two visual copies of the rendered world at `±WorldWidthInWorldUnits` along X via `Object.Instantiate` of canonical tile GameObjects plus a per-tile `MaterialPropertyBlock` copy. Each copy carries its own `MeshCollider` so raycasts hit any visible tile. `WorldWrapRenderer.WorldToCoord(worldPoint, map)` maps a world-space point back to a canonical `HexCoord` through `WorldMap.Normalize`. `CivCameraController` (in `Examples/QuickStart/Scripts/`) reads input through `ICameraInput` (in `Runtime/Input/`), pans / zooms / rotates / tilts the camera, soft-clamps the focus point vertically (just past the pole rows), and normalizes the camera's X back into `[0, WorldWidthInWorldUnits)` when it crosses the wrap boundary — the visible scene stays continuous because the wrap copies cover the seam.

**Tile interaction** (ADR-0016): `HexTileBinding` (public MonoBehaviour in `Runtime/Rendering/`) carries the canonical `HexCoord` and is attached to every tile GameObject by `WorldRenderer` (and rides through `WorldWrapRenderer`'s `Instantiate` onto copies). `HexOutline.Create(name, color, width)` builds a closed `LineRenderer` hex outline as a self-contained GameObject. Two demo scripts in `Examples/QuickStart/Scripts/`: `TileHover` raycasts from the camera through the cursor, reads `HexTileBinding.Coord` on the hit (with `WorldToCoord` math fallback when raycast misses), positions a `HoverOutline` GameObject over the hovered tile, and exposes `HoverDuration` for Slice 10's tooltip. `TileSelection` watches for primary clicks via `ICameraInput.PrimaryClickPressed`, commits the hovered coord as selection, and pins a `SelectionOutline`. Right-click clears. The tile materials are never touched — outlines are separate scene-graph objects.

**Destroying render objects in Play mode** (Slice 18.1): `Destroy` is deferred to the end of the frame, so a destroyed child is still in its parent's hierarchy for the rest of that frame. `WorldWrapRenderer.Refresh` walks the renderer's children right after `WorldRenderer.Render`, so stale tiles used to get cloned into the wrap copies and stack up on every regenerate. Rule: every renderer deactivates and unparents a GameObject before a Play-mode `Destroy` (`WorldRenderer`, `RiverRenderer`, and `WorldWrapRenderer` all do). Any new code that destroys render objects and any code that walks a transform's children must keep to this. Covered by `Tests/Runtime/RegenerateInPlayModeTests`, the only test that runs in real Play mode.

**Water height and cliffs** (ADR-0027): connected water renders at one height. Sea water always sits at `OceanRenderHeight`; lake bodies are flattened in the data layer by `RiverStage.FlattenLakeBodies` to their lowest shore level, and `ComputeTileY` renders them with the land formula. `WorldRenderer` optionally stacks `CliffPrefab` pieces under each tile root from its own Y down to its lowest neighbor's Y (`ComputeCliffFloorY`), squashing pieces so the stack fits exactly.

**Runtime regenerate overlay** (Slice 18): `RegenerateOverlay` (in `Examples/QuickStart/Scripts/`) is an IMGUI panel that drives `WorldGeneratorBootstrap` through its small public control surface (`Regenerate()`, `Regenerate(ulong seed)`, `Seed`, `ContinentShapeAlgorithm`, `LandRatio`, `Preset`). Dependency direction is one way: the overlay knows the bootstrap, nothing knows the overlay. It reads keys from `Event.current` rather than `ICameraInput`, so it works under any Active Input Handling setting and needs no Canvas or EventSystem. Map type and land changes rebuild the current seed so the effect of the setting is visible on the same world.

**Prefab tile visuals** (ADR-0017): `BiomeDefinition` and `TileFeatureDefinition` each expose an optional `public GameObject Prefab;` field. `WorldRenderer.BuildTileGameObject` resolves the visual in feature-wins precedence: `tile.Feature?.Prefab` → `tile.Biome?.Prefab` → procedural `HexMesh` + per-tile `MaterialPropertyBlock` (unchanged when both prefabs are null). The tile root always carries `HexTileBinding` plus a flat-`HexMesh` `MeshCollider` regardless of which visual path is chosen, so Slice 9 picking continues to work even on buyer prefabs that supply no colliders of their own. Data layer (`HexTile`) carries no GameObject references — the prefab lives on the ScriptableObject content, not the tile. Slice 10.1 adds prefab variants + per-tile deterministic Y rotation; Slice 10.2 bundles the CC0 Kenney Hexagon Kit and a sample biome→prefab mapping.

**Dual input provider** (ADR-0018): the demo input layer ships two `ICameraInput` implementations and a runtime installer that picks the right one. `Runtime/Input/LegacyUnityInputProvider.cs` reads `UnityEngine.Input`, with every call site wrapped in `#if ENABLE_LEGACY_INPUT_MANAGER` so it stubs out cleanly under new-only projects instead of throwing `InvalidOperationException`. `Runtime/Input/InputSystem/` is a separate asmdef (`HexWorldStudio.Runtime.InputSystem`) with `defineConstraints: ["ENABLE_INPUT_SYSTEM"]` and a hard reference to `Unity.InputSystem`; it carries `InputSystemInputProvider.cs` (mirrors the legacy provider's semantics on top of `Keyboard.current` / `Mouse.current`) and `InputSystemBootstrap.cs` (a tiny `[RuntimeInitializeOnLoadMethod]` / `[InitializeOnLoadMethod]` that registers a factory on `CameraInputInstaller.InputSystemFactory`). `Runtime/Input/CameraInputInstaller.cs` is a `MonoBehaviour` on the `QuickStartScene`'s camera GameObject; on `Awake` it calls the registered factory if one is present (new system path) and falls back to `LegacyUnityInputProvider` under `#if ENABLE_LEGACY_INPUT_MANAGER`. The static-delegate hook is the conventional Unity workaround for the cross-asmdef-reference problem the InputSystem-only sibling asmdef creates. `CivCameraController` keeps reading through `ICameraInput` via `GetComponent<ICameraInput>()` — no controller change. Net effect: the QuickStart scene plays under any of Unity's three `Active Input Handling` configurations (Legacy, New, Both) without buyers touching Player Settings.

**Corner-network rivers** (ADR-0025, superseding ADR-0024's generation and rendering halves): `RiverStage` runs tenth in the pipeline. It depression-fills the elevation field (`Generation/Hydrology/DepressionFill.cs`, priority flood on a scratch copy), derives lakes from the filled basins (`HexTile.IsLake`), grows a descent tree **inland from the sea** over the hex-**corner** graph, then reads rivers off that tree. Because three hexes meet at every corner and a step between adjacent corners traverses exactly one hex edge, river chains are corner-connected by construction — the ADR-0024 tile walk plus `AddCornerConnectors` repair pass is deleted. A channel runs only between two tiles of equal `ElevationLevel`; a terrace boundary is crossed by a single-edge waterfall, never run along, and never twice in a row. The tree never expands into a second water-touching corner, so rivers meet the sea head-on rather than running along the shore, and every river reaches water by construction. Output is `WorldMap.RiverEdges` (unchanged ADR-0024 contract) plus the ordered, directed `WorldMap.RiverPaths`, whose `RiverStep`s carry both corners, the traversed edge, the terrace levels entering and leaving, and a waterfall flag, with `StrahlerOrder` per path. `RiverRenderer` builds one mitered ribbon per path — segments meet exactly at shared corners, height follows terrace levels, `V` accumulates source-to-mouth so flow animates coherently, width tapers by Strahler order, and waterfalls render as lip / face / plunge pool. `RiverStyleAsset` carries Material, the width range, SurfaceYOffset, BaseColor, and WaterfallLipFraction. `RiversRebuilt` still lets buyers draw their own. Wrap is handled by unwrapping corner positions against the previous corner and by `WorldWrapRenderer`'s explicit `RiverRenderer` reference.

**Resource render modes** (ADR-0023): `ResourceDefinition` exposes a `ReplaceTile` boolean. When off (default), the resource's `Prefab` instantiates as a child of the tile root positioned at the visible top of the underlying biome / feature visual (renderer caches the per-source-prefab surface Y once and reuses it across every tile sharing that source). When on, the renderer looks up `(tile.Biome, tile.Feature)` in the resource's auto-synced `ReplacementPrefabs` list and instantiates that entry's prefab as the tile's sole visual — no biome / feature prefab is instantiated alongside. The list's entries are auto-populated from `AllowedBiomes × AllowedFeatures` via the `ResourceDefinitionEditor` custom inspector; existing prefab assignments are preserved across resyncs by `(Biome, Feature)` key match. No-prefab combos fall back to the default add-on-top behavior so buyers can iterate without breaking the demo.

**Optional resource placement** (ADR-0022): a ninth pipeline stage (`ResourcePlacementStage`) runs after `FeatureCohesionStage` and writes `HexTile.Resource` (a new public field). Resources are authored as `ResourceDefinition` ScriptableObjects with biome / feature / elevation filters, rarity weight, minimum spacing, optional 3D Prefab, and color. The stage is opt-in: an empty `WorldGenerationSettings.Resources` roster or `ResourceDensity = 0` makes the stage a complete no-op, so worlds with no resources are byte-identical to worlds generated before Slice 15. Buyers add resources by populating the roster (Bootstrap's individual fields, or via a preset's `Resources` array). The renderer instantiates `tile.Resource.Prefab` as a child of the tile root, layered above the biome / feature visual; the flat-hex `MeshCollider` on the tile root still drives picking. Eighteen worked-example resources ship under `Content/Resources/` covering Civ's classic catalog (Wheat, Cattle, Horses, Corn, Rice, Sheep, Deer, Bananas, Furs, Spices, Sugar, Incense, Iron, Gold, Silver, Gems, Fish, Whales) with `AllowedBiomes` / `AllowedFeatures` references pre-wired to the shipped biome and feature SOs.

**`WorldPreset` ScriptableObject** (ADR-0021): a buyer-authored asset that bundles generation configuration — `Width`, `Height`, `LandRatio`, `ContinentShapeAlgorithm`, `BiomeCohesion`, `FeatureCohesion`, `Biomes` — into a single reusable recipe. Lives in `HexWorldStudio.Content` next to `BiomeDefinition` / `TileFeatureDefinition`. The RNG seed is *not* on the preset (preset = "what kind of world", seed = "which instance"); `WorldPreset.ToSettings(seed)` builds the runtime `WorldGenerationSettings`. `WorldGeneratorBootstrap` carries an optional `_preset` slot above the per-field inspector values: when assigned, the preset wins; when null, the bootstrap falls back to its individual inspector fields (no breaking change for the existing QuickStart scene wiring). Four worked-example presets ship under `Content/Presets/`: Earthlike (Continents 0.4), Pangaea (0.45), Archipelago (0.3), InlandSea (0.65).

**Pluggable continent-shape algorithms** (ADR-0020): the `ContinentShapeStage` pipeline slot dispatches via `WorldGenerationSettings.ContinentShapeAlgorithm` (public enum, default `Continents`) to one of seven concrete algorithms living in `ContinentShapeAlgorithms`: `Continents` (multiple medium landmasses — historical default), `Pangaea` (single supercontinent in temperate latitudes), `Archipelago` (many small islands), `Fractal` (irregular fjord coastlines), `InlandSea` (land ring around a central sea), `Lakes` (mostly land with scattered water), `Shuffle` (RNG-picks one of the others). Each algorithm forks the master RNG with its own label so adding or reordering algorithms does not shift the seeded stream observed by other algorithms — most importantly, `Continents` keeps the `"noise-continent-shape"` label from Slice 5 so existing buyer-pinned seeds remain byte-identical. Every algorithm finishes by handing its sample field to a shared percentile-threshold helper so `LandRatio` is honored regardless of any bias gradient. `WorldGeneratorBootstrap` exposes the choice as an inspector enum field. Future algorithms (Highlands, Small Continents, Island Plates, Mirror, Continents Plus, Terra) land as small-diff follow-up slices that append to the enum and add another case to the dispatcher switch.

**Biome + Feature Cohesion** (ADR-0019): two post-classification smoothing stages eliminate single-tile speckling near biome and feature boundaries. `BiomeCohesionStage` runs immediately after `BiomeAssignmentStage`; `FeatureCohesionStage` runs immediately after `TileFeaturePlacementStage`. Each stage reads a single 0..1 strength slider from `WorldGenerationSettings` (`BiomeCohesion`, `FeatureCohesion`) and runs `N` iterations of neighbor-consensus smoothing — for each non-water tile, if fewer than `K` of its 6 neighbors share its current biome / feature, switch to the most-common neighbor value. Tie-breaking uses `WorldGenerationSettings.Biomes` order (for biomes) and `BiomeDefinition.AllowedFeatures` order (for features). Strength 0 is a complete no-op. Water tiles are preserved (their biomes come from topology in `BiomeAssignmentStage`, not from climate). Feature smoothing's candidate set is restricted to the current tile's biome's `AllowedFeatures` array so a smoothing pass can never violate biome-feature compatibility. The strength → (iterations, threshold) mapping lives in the public-static `BiomeCohesionStage.FromStrength` so tests and `FeatureCohesionStage` share it.

## Request-to-render flow

1. User opens the **Hex World Generator** editor window.
2. User picks a `WorldPreset` ScriptableObject (or composes settings manually), sets a seed, hits **Generate**.
3. Window constructs a `WorldGenerationSettings` from inspector values.
4. Window calls `HexWorldGenerator.Generate(settings)`, which runs the stages above on the main thread and returns a `WorldMap`.
5. Window hands the `WorldMap` to a `WorldRenderer` MonoBehaviour in the active scene (or creates one if absent).
6. `WorldRenderer` walks the tiles, batches per-biome instanced meshes, renders navigable river tiles with the navigable-river material, builds non-navigable river ribbons on edges, and parents resource props.
7. Diagnostic overlays (elevation/temperature/moisture/biome/rivers) read from the `WorldMap` and draw via `Handles.DrawAAConvexPolygon` / gizmos.

Runtime (not editor) callers do steps 3–6 themselves via the public API; the window is just a UI on top.

## Data model contract

The `WorldMap` returned from `Generate` is a normal mutable class. **We do not technically prevent buyers from modifying it.** Instead we document the contract:

- **Safe to mutate freely**: gameplay-layer fields buyers add (subclasses or external dictionaries keyed by `HexCoord`).
- **Safe to mutate with care**: tile-level state like `elevation`, `biome`, `feature`, `resource` — buyers may change these after generation if they accept that diagnostic overlays and serialization will reflect the changes.
- **Do not mutate after construction**: `dimensions`, `seed`, the `HexCoord`-to-tile index mapping, and the `RiverNetwork` topology. These are assumed stable by every downstream consumer (rendering, serialization, queries). If buyers want a different topology, they regenerate.

This contract is enforced by documentation and `<remarks>` blocks in XML comments, **not** by readonly facades or runtime guards. Rationale and trade-offs are in ADR-0005.

## Map topology (cylindrical)

Maps are rectangular (`Width × Height` tiles) and cylindrical: the horizontal axis wraps so column `Width - 1` connects to column `0`, while the vertical axis is bounded (the top and bottom rows are hard edges representing the world's poles). Matches the Civilization convention. Rationale and trade-offs are in ADR-0009.

Wrap is enforced inside `WorldMap`. The safe lookup API is `bool TryGetTile(HexCoord, out HexTile)` — it returns `false` (without throwing and without leaking null) for R outside `[0, Height)`, and otherwise wraps Q into the canonical range and returns the resolved tile. `WorldMap.Normalize(HexCoord)` exposes the Q-wrap operation directly so buyer code can canonicalize coords before equality comparisons, dictionary keys, or external indexing.

`HexCoord` itself is topology-unaware: coordinate math and wrap are separate concerns. Neighbor queries via `HexCoordExtensions.Neighbor` continue to return raw axial coords; wrap kicks in only when those coords are passed to a `WorldMap` lookup. A coord and its normalized equivalent will compare unequal under `==`; if you need "same tile" semantics across the seam, compare normalized coords or compare `HexTile` references.

## River representation

**v1 (Slice 17.3, ADR-0025):** Rivers are generated as **paths through hex corners** and stored in two complementary views.

- `WorldMap.RiverEdges` — the unordered canonical `IReadOnlyList<RiverEdge>` from ADR-0024, unchanged. Each edge is a `(HexCoord, HexDirection)` pair naming the seam between two neighboring hexes; canonical form (lex-smaller endpoint owns) makes dedup trivial and `HasRiverAt` direction-agnostic. Use it for adjacency questions.
- `WorldMap.RiverPaths` — the ordered, directed view. One `RiverPath` per river, a sequence of `RiverStep`s from source to mouth carrying the two corners, the traversed edge, the terrace levels entering and leaving, and whether the step is a waterfall. Plus `StrahlerOrder` and `ReachesWater`. Use it when direction matters.

Why corners: three hexes meet at every corner and every corner has exactly three neighbours, so **a step between adjacent corners traverses exactly one hex edge**. A river expressed as a corner sequence is edge-connected by construction — no bridging pass, and confluences are just shared corners. This replaced ADR-0024's tile walk plus `AddCornerConnectors` repair pass, which skipped every confluence.

`HexCorner` (in `Runtime/Data/`, wrap-aware like `RiverEdge`) is the canonical vertex identity: the three possible `(tile, index)` namings collapse to the lex-smallest owning tile.

The search runs **backwards**: a descent tree is grown outward from every water-touching corner, expanding uphill along legal steps in reverse. A corner enters the tree only via a legal step from a corner that already drains to water, so everything the tree reaches has a route to the sea by construction and a traced river cannot strand. Tree state is `(corner, terrace level on arrival, was the downstream step a fall)`, because continuity and the no-two-falls rule make the legal onward steps depend on all three.

Step legality — all four must hold: both tiles flanking the edge are **land** (so no river edge ever lies on a shoreline); the destination corner is strictly lower on the **depression-filled** continuous field; the edge is either a **channel** between two tiles of equal `ElevationLevel` or a single-edge **waterfall** crossing a terrace boundary within `MaxWaterfallDrop` and not following another waterfall; and terrace levels are **continuous** with the previous step. The tree never expands into a second water-touching corner, so a river's mouth is its only corner against the sea — which is what keeps rivers from running *along* a coastline.

A river must lie on a height boundary for at least one edge per level it descends. That is forced, not chosen: consecutive river edges always share a tile (verified, 1296 of 1296), so requiring equal levels everywhere would hold a river on one terrace forever. Such edges are ~22% of river length and pricing them into the route does not reduce that. What makes them read correctly is the renderer, which drops the water at the upstream corner so it runs at the foot of the cliff rather than along its top.

Two elevation fields do two jobs: the filled continuous field drives direction (discrete levels are flat across a terrace), the discrete `ElevationLevel` drives legality (it is the terracing the player sees).

`Runtime/Generation/Hydrology/DepressionFill.cs` conditions the field first — priority flood on a scratch copy, raising to just above the spill level so filled basins are not perfectly flat. Lakes are then derived from the filled basins (`HexTile.IsLake`), before any walking, so they are contiguous and rivers terminate at them naturally.

The default `RiverRenderer` emits one mitered ribbon per path; buyers who want richer visuals swap the material or subscribe to `RiversRebuilt` and render their own.

**Deferred to Slice 17.2 (dual representation per ADR-0006):**

- **Non-navigable rivers** on edges — already shipped in Slice 17 above.
- **Navigable rivers** on whole tiles — wide downstream sections that gameplay can treat as traversable water. Rendering: the tile's surface mesh uses a navigable-river material and may swap geometry.
- **Transitions** where an edge segment feeds into a navigable tile (or vice-versa in a delta). Explicit nodes in a `RiverNetwork` container. The hydrology pass would decide where each transition lands based on accumulated flow, slope, and biome rules.

Slice 17.2 will extend `WorldMap` with the navigable / transition surface and appropriate renderer support; the Slice 17.0 `RiverEdge` set remains the source of truth for the non-navigable path.

## Code layout inside `Assets/HexWorldStudio/`

```
Runtime/
  HexWorldStudio.Runtime.asmdef
  Coordinates/
    HexCoord.cs                 ← axial coord struct + conversions
    HexCoordExtensions.cs       ← neighbor/range/line helpers
  Data/
    WorldMap.cs                 ← pure-data root container (class); river-edge storage + query API
    HexTile.cs                  ← per-tile class
    RiverEdge.cs                ← public struct: canonical (Coord, Direction) river segment (ADR-0024)
    HexCorner.cs                ← public struct: canonical hex-vertex identity, wrap-aware (ADR-0025)
    RiverPath.cs                ← public RiverStep + RiverPath: ordered source-to-mouth river view (ADR-0025)
    WorldMapSerialization.cs    ← JSON / binary IO (deferred)
  Content/
    BiomeDefinition.cs          ← ScriptableObject
    ResourceDefinition.cs       ← ScriptableObject
    TileFeatureDefinition.cs    ← ScriptableObject
    WorldPreset.cs              ← ScriptableObject
    RiverStyleAsset.cs          ← ScriptableObject: river visual style (ADR-0024)
  Generation/
    HexWorldGenerator.cs        ← public entry point
    WorldGenerationSettings.cs  ← public settings (class)
    Pipeline/
      IGenerationStage.cs       ← internal stage interface
      GenerationContext.cs      ← per-run state shared between stages
      Stages/
        ContinentShapeStage.cs
        ElevationFieldStage.cs
        ClimateStage.cs
        BiomeAssignmentStage.cs
        ResourcePlacementStage.cs
        RiverStage.cs             ← corner-network walk + lakes; writes RiverEdges + RiverPaths (ADR-0025)
        StartingPositionStage.cs
        PostProcessStage.cs
    Hydrology/
      DepressionFill.cs         ← internal priority-flood pit removal (ADR-0025)
    Rng/
      RngService.cs             ← deterministic seeded RNG
      NoiseSources.cs           ← vendored Perlin, Simplex, Worley
  Rendering/
    WorldRenderer.cs            ← MonoBehaviour; consumes WorldMap
    RiverRenderer.cs            ← MonoBehaviour; procedural strip mesh from WorldMap.RiverEdges (ADR-0024)
    BiomeMeshCache.cs           ← instanced mesh batching
    ResourcePropPlacer.cs       ← parents resource prefabs to tiles
  Input/
    ICameraInput.cs             ← public interface (axes + click frames)
    LegacyUnityInputProvider.cs ← UnityEngine.Input-backed, #if ENABLE_LEGACY_INPUT_MANAGER guarded
    CameraInputInstaller.cs     ← Awake-time picker that AddComponents the right provider
    InputSystem/                ← separate asmdef, defineConstraints: ["ENABLE_INPUT_SYSTEM"]
      HexWorldStudio.Runtime.InputSystem.asmdef
      InputSystemInputProvider.cs ← Keyboard.current / Mouse.current backed
      InputSystemBootstrap.cs     ← [RuntimeInitializeOnLoad] registers a factory on CameraInputInstaller
Editor/
  HexWorldStudio.Editor.asmdef
  Windows/
    HexWorldGeneratorWindow.cs  ← main authoring window
  Drawers/
    HexCoordDrawer.cs           ← PropertyDrawer for HexCoord
  Inspectors/
    BiomeDefinitionEditor.cs
    WorldPresetEditor.cs
  Overlays/
    DiagnosticOverlays.cs       ← elevation/temp/moisture/biome/river
Examples/
  QuickStart/
  Presets/
  ApiOnly/
Tests/
  Runtime/
    HexWorldStudio.Tests.Runtime.asmdef
    PipelineCancellationTests.cs
  Editor/
    HexWorldStudio.Tests.Editor.asmdef
    HexCoordMathTests.cs
    DeterminismTests.cs
    RiverInvariantTests.cs
    BiomeClassificationTests.cs
Documentation/
  GettingStarted.md
  Concepts/DataVsRendering.md
  Concepts/ScriptableObjectContent.md
  Concepts/DeterminismAndSeeding.md
  Concepts/DataModelContract.md         ← what's safe to mutate, what isn't
  Concepts/RiverRepresentation.md       ← navigable vs non-navigable
  Cookbook/ReplaceTheRenderer.md
  Cookbook/AddACustomBiome.md
  Cookbook/AddACustomGenerationPass.md
  APIReference/                          ← deferred; DocFX-generated at release
  FAQ.md
ThirdParty/
  Kenney-HexagonKit/
    [model files]
    LICENSE-CC0.txt
```

## The most important types

| Type | Layer | One-line role |
|---|---|---|
| `HexCoord` | Runtime/Coordinates | Immutable axial coordinate struct with cube/offset conversions and equality. |
| `WorldMap` | Runtime/Data | Class. Root container: dimensions, tiles, river network, seed, settings echo, schema version. Mutability contract is documentation-only. |
| `HexTile` | Runtime/Data | Class. Per-tile state: elevation, temperature, moisture, biome ref, feature ref, resource ref, optional navigable-river marker. |
| `RiverEdge` | Runtime/Data | Struct. Canonical naming of one hex edge; the unordered river set. |
| `HexCorner` | Runtime/Data | Struct. Canonical hex-vertex identity. A step between adjacent corners is exactly one hex edge — the basis of river topology (ADR-0025). |
| `RiverPath` | Runtime/Data | Class. Ordered source-to-mouth river: steps, terrace levels, waterfalls, Strahler order. |
| `WorldGenerationSettings` | Runtime/Generation | Class. Public input to generation: seed, dimensions, preset, content references, overrides. |
| `HexWorldGenerator` | Runtime/Generation | Public entry point. `Generate(settings) → WorldMap`, plus async/progress variant. |
| `IGenerationStage` | Runtime/Generation/Pipeline | Internal contract: takes a `GenerationContext`, mutates the map for one pipeline step. |
| `RngService` | Runtime/Generation/Rng | The *only* source of randomness in the pipeline. Seeded, deterministic, splittable. |
| `BiomeDefinition` | Runtime/Content | ScriptableObject: climate envelope, base color, allowed features/resources. |
| `WorldPreset` | Runtime/Content | ScriptableObject: named bundle of generation settings ("Earth-like", "Archipelago"). |
| `WorldRenderer` | Runtime/Rendering | MonoBehaviour that consumes a `WorldMap` and produces scene-graph visuals. Optional. |
| `HexWorldGeneratorWindow` | Editor/Windows | Main authoring UI: seed, settings, preset, generate, live preview, export. |

## Documentation tooling

API reference is **deferred for v1 development**. XML doc comments are authored on every public type and member as if a generator will consume them. The expected generator at release time is **DocFX** (industry standard for .NET / Unity packages, reads XML comments, outputs static HTML). DocFX is not installed during development to avoid coupling daily work to a tooling pipeline; the call to wire it up sits in `FEATURES.md` under Documentation.

## Out of scope for v1

These have been considered and deliberately excluded; revisit only with a new ADR:

- **Multi-threaded generation.** Pipeline runs on the main thread. Determinism is easier; perf is acceptable for sub-200×200 maps. Job system parallelism is a v1.1 candidate.
- **Live in-game regeneration.** No streaming, no infinite worlds. Generate once, use the result.
- **Hex combat, pathfinding, fog of war, units, AI.** This is a world-generation asset, not a 4X engine. We expose neighbor/range/line queries; everything above that is the buyer's domain.
- **HDRP support.** Per ADR-0002.
- **Built-in render pipeline shipping templates.** Manual migration cookbook only.
- **Mobile / WebGL platform targets.** Stretch for v1.1.
- **Buyer-extensible pipeline stages.** v1 has a fixed stage list. A formal extension API is a v1.1 candidate.
- **Map editor for hand-painted tiles.** Procedural-only.
- **Networking / multiplayer integration.** Determinism makes lockstep sync feasible *for buyers*, but we ship no Netcode/Mirror/etc. integration.
- **Pre-generated map library shipped with the asset.** Buyers generate; we don't ship sample `WorldMap` assets except as test fixtures.
