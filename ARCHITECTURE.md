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
[7] HydrologyPass — carve river system: assign edge-based non-navigable
        │              segments and tile-based navigable segments;
        │              compute transitions along the flow
        ▼
[8] ResourcePlacement — honor biome rules + spacing
        │
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

## River representation (dual)

Rivers are represented in two complementary ways within a single `RiverNetwork`:

- **Non-navigable rivers** live on **edges between tiles** (a hex seam). These are small streams, headwaters, and tributaries. Two adjacent tiles can be on opposite banks. Rendering: low-poly ribbon meshes along the shared edge.
- **Navigable rivers** live on **whole tiles** (a tile *is* the river). These are wide, downstream sections that gameplay can treat as traversable water. Rendering: the tile's surface mesh uses a navigable-river material and may swap geometry.

A single river system can include both. Transitions along the flow (an edge segment connecting into a tile-river, or a tile-river feeding additional edge segments downstream as the system braids near a delta) are explicit nodes in `RiverNetwork`. The hydrology pass decides where each transition lands based on accumulated flow, slope, and biome rules. Rationale and trade-offs are in ADR-0006.

## Code layout inside `Assets/HexWorldStudio/`

```
Runtime/
  HexWorldStudio.Runtime.asmdef
  Coordinates/
    HexCoord.cs                 ← axial coord struct + conversions
    HexCoordExtensions.cs       ← neighbor/range/line helpers
  Data/
    WorldMap.cs                 ← pure-data root container (class)
    HexTile.cs                  ← per-tile class
    RiverNetwork.cs             ← edge segments + tile segments + transitions
    WorldMapSerialization.cs    ← JSON / binary IO
  Content/
    BiomeDefinition.cs          ← ScriptableObject
    ResourceDefinition.cs       ← ScriptableObject
    TileFeatureDefinition.cs    ← ScriptableObject
    WorldPreset.cs              ← ScriptableObject
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
        HydrologyStage.cs
        ResourcePlacementStage.cs
        StartingPositionStage.cs
        PostProcessStage.cs
    Rng/
      RngService.cs             ← deterministic seeded RNG
      NoiseSources.cs           ← vendored Perlin, Simplex, Worley
  Rendering/
    WorldRenderer.cs            ← MonoBehaviour; consumes WorldMap
    BiomeMeshCache.cs           ← instanced mesh batching
    RiverRibbonBuilder.cs       ← non-navigable river edge meshes
    NavigableRiverTileBuilder.cs← navigable river tile mesh/material swap
    ResourcePropPlacer.cs       ← parents resource prefabs to tiles
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
| `RiverNetwork` | Runtime/Data | Edge-based non-navigable segments + tile-based navigable segments + explicit transition nodes. |
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
