# FEATURES — living inventory

Every feature, planned or implemented, lives here. **Add a feature here before writing code for it.**

## Status legend

- `[ ]` planned
- `[~]` in progress
- `[x]` done
- `[!]` blocked
- `[-]` descoped

---

## Map type algorithms (per ADR-0010 — pluggable per stage)

Each pipeline-stage role accepts any `IGenerationStage` implementation. The Civ-flavored algorithms below are tracked as separate planned implementations of the appropriate stage. The exact final list will be verified against base Civilization VII when each algorithm is authored (same pattern as ADR-0007's biome enumeration commitment).

Continent-shape algorithms:

- [x] Continents — multiple medium landmasses (Slice 13 / ADR-0020; this is the historical Slice-5 percentile-threshold algorithm, now named explicitly as one entry in the catalog)
- [x] Pangaea — one supercontinent in temperate latitudes (Slice 13)
- [x] Archipelago — many small islands (Slice 13)
- [x] Fractal — irregular fjord coastlines (Slice 13)
- [x] Inland Sea — land ring around a central sea (Slice 13)
- [x] Lakes — mostly land with scattered water bodies (Slice 13)
- [x] Shuffle — RNG-picks one of the above (Slice 13)
- [ ] Continents Plus
- [ ] Island Plates
- [ ] Highlands
- [ ] Small Continents
- [ ] Mirror
- [ ] Terra (geographically Continents; distinguished at starting-position time)
- [ ] Custom buyer-supplied `IGenerationStage` (formal extension API is v1.1 candidate)

Elevation, climate, hydrology, biome, and resource stages each get the same "pluggable per slot" treatment; additional implementations beyond the default are appended here as they are authored.

## Generation Pipeline

- [x] Deterministic seeded RNG service (same seed + settings → byte-identical output)
- [~] Continent/landmass shape generation — pure Perlin percentile-threshold baseline shipped in Slice 5 (honors `WorldGenerationSettings.LandRatio` to within one tile); Civ-style algorithm variants tracked in "Map type algorithms" still pending
- [x] Elevation field generation — fBm Perlin, cylindrical, normalized to [0, 1] (Slice 5)
- [x] Climate model: temperature based on latitude + elevation (Slice 6; lapse rate + Perlin perturbation; latitude-banded variants per ADR-0010 still pending)
- [x] Climate model: moisture / precipitation based on prevailing winds (Slice 6 per ADR-0012; east-to-west wind transport with orographic precipitation and rain shadow; latitude-banded wind variants per ADR-0010 still pending)
- [~] Biome assignment from temperature/moisture lookup (Slice 6 envelope-based first-match-wins assignment shipped; richer Whittaker-style or weighted variants per ADR-0010 still pending)
- [x] Biome + Feature cohesion smoothing (Slice 12 / ADR-0019): `BiomeCohesionStage` runs right after biome assignment, `FeatureCohesionStage` runs right after feature placement. Each is driven by a single 0..1 strength slider on `WorldGenerationSettings` (`BiomeCohesion`, `FeatureCohesion`) surfaced on `WorldGeneratorBootstrap`. Neighbor-consensus algorithm with deterministic tie-break (Biomes order / AllowedFeatures order). Strength 0 = complete no-op. Water tiles preserved (topology-classified). Feature smoothing restricted to the current biome's allow-list so it never violates biome-feature compatibility. Defaults: 0.5 for biomes (mild Civ-like blobs), 0.3 for features (preserve natural diversity).
- [x] River generation along elevation gradients, terminating at oceans or lakes (Slice 17.3 / ADR-0025, superseding the ADR-0024 generation half): `RiverStage` runs tenth in the pipeline and forks RNG with label `"river-generation"`. Rivers are **paths through hex corners**, not sets of edges — three hexes meet at a corner and a step between adjacent corners traverses exactly one hex edge, so chains are corner-connected by construction with no bridging pass. Sources are moisture-weighted corners whose three tiles are all land at `ElevationLevel ≥ RiverSourceMinElevation` (default 3 of 6). **The search runs backwards**: a descent tree is grown outward from every water-touching corner, expanding uphill along legal steps in reverse, so every corner it reaches provably has a route back down to water and a river traced down its parent links *cannot strand* (measured: 0% stranded, versus 43% under the original forward walk). The tree never expands into a second water-touching corner, so a river's mouth is its only corner against the sea — which is what stops rivers running *along* coastlines. Opt-out via `WorldGenerationSettings.RiverDensity = 0` (default 0.3), which skips the whole hydrology pass including lakes. `WorldMap.RiverEdges` keeps its ADR-0024 contract; `HasRiverAt(coord, dir)` and `CountRiverEdgesAround(coord)` are unchanged.
- [x] Terrace-constrained channels + waterfall crossings (Slice 17.3 / ADR-0025): a river channel only ever runs between two tiles of equal `ElevationLevel`. A terrace boundary is **crossed** by a single-edge waterfall, bounded by `MaxWaterfallDrop` (default 1 level), never two in a row, so a river cannot staircase down a cliff face. Those crossing edges are ~22% of river length and that is a floor rather than a tuning failure — verified by execution that consecutive river edges always share a tile (1296/1296), so requiring equal levels everywhere would hold a whole river on one terrace and it could never reach the sea. The renderer is what makes them read right (Slice 17.4). Terrace levels are continuous along a path (`Steps[i].ToLevel == Steps[i+1].FromLevel`), so the water surface never teleports. The one-edge waterfall is forced by hex topology, not chosen: there is no way to pivot at a single corner from a level-`L` channel to a level-`L-1` one.
- [x] Depression filling / hydrological conditioning (Slice 17.3 / ADR-0025): `Runtime/Generation/Hydrology/DepressionFill.cs` is a priority-flood fill (`O(n log n)`, hand-rolled min-heap since Unity's netstandard2.1 runtime predates `PriorityQueue`) that removes the pits raw fBM elevation is full of, so a downhill walk actually reaches the sea. Runs on a scratch copy — `HexTile.Elevation` is never mutated. Raises to just *above* the spill level so filled basins are not perfectly flat, which a strict-descent walk could not cross.
- [x] Fill-derived lakes (Slice 17.3 / ADR-0025): a land tile the fill had to raise by more than `LakeDepthThreshold` (default 0.08) becomes a lake, decided before any walking. Contiguous bodies rather than the single-tile holes ADR-0024's stranded-walk flips produced; rivers terminate at them naturally. `HexTile.IsLake` distinguishes them from sea, so the renderer places a mountain tarn at its terrace height instead of at sea level.
- [x] Ordered river paths + Strahler order (Slice 17.3 / ADR-0025): `WorldMap.RiverPaths` is the directed source-to-mouth view — `RiverPath` of `RiverStep`s carrying `From`/`To` corners, the traversed `Edge`, `FromLevel`/`ToLevel`, and `IsWaterfall`, plus `StrahlerOrder` and `ReachesWater`. Answers "which way does this flow", which the unordered edge set could not, and drives coherent flow animation and width-by-order in the renderer.
- [ ] Hydrology pass: decide navigable (tile) vs non-navigable (edge) river segments and transition points (deferred to Slice 17.2 — v1 ships edge-only rivers)
- [x] Resource placement honoring biome rules and minimum-distance constraints (Slice 15 / ADR-0022): optional `ResourcePlacementStage` writes `HexTile.Resource` from a buyer-supplied `ResourceDefinition` roster on `WorldGenerationSettings.Resources` (+ `ResourceDensity` slider). Empty roster = no resources (stage no-ops). Each `ResourceDefinition` exposes biome / feature / elevation filters + Rarity + MinimumSpacing + Prefab. Renderer instantiates the resource prefab as a child of the tile root. 18 shipped Civ-style resources (Wheat, Cattle, Horses, Corn, Rice, Sheep, Deer, Bananas, Furs, Spices, Sugar, Incense, Iron, Gold, Silver, Gems, Fish, Whales) pre-wired to shipped biome / feature SOs.
- [ ] Starting-position selection (configurable player count, fairness/distance heuristics)
- [ ] Map post-processing pass (smoothing, error correction, isolated-tile cleanup)
- [~] Single-pass pipeline orchestrator with cancellation support (orchestrator + `IGenerationStage` + `GenerationContext` landed in Slice 4; default pipeline currently empty, stages append per slice; cancellation / progress / async variant deferred to a later slice)

## Data Model

- [~] `WorldMap` — pure-data root container (cylindrical wrap + tile array + lookup API + river-edge storage / query surface in; serialization pending)
- [~] `HexTile` — per-tile data (coord, index, elevation, temperature, moisture, land state, discrete `ElevationLevel`, biome reference, and `TileFeature` reference all in; resource references land with the resource slice)
- [x] Discrete `ElevationLevel` field on `HexTile` (int, 0..N-1) plus the `ElevationLevelQuantizationStage` that derives it from the continuous `Elevation` field with median-smoothing over the wrap-aware 6-neighborhood (Slice 5; default `N = 6`, per-map configurability lands with `WorldPreset` content). Water tiles pin to `ElevationLevel 0` regardless of the continuous elevation noise so oceans and coasts never render above the surrounding land (Slice 11.1 fix).
- [x] Axial / cube / offset coordinate utilities and conversions
- [~] Neighbor / range / line-of-sight queries (neighbor / range / line drawing landed; true LOS with obstacles requires `WorldMap` and is deferred)
- [~] River network representation: edge-based `RiverEdge` set on `WorldMap.RiverEdges` (Slice 17 / ADR-0024) plus the ordered, directed `WorldMap.RiverPaths` view (Slice 17.3 / ADR-0025). Navigable / non-navigable dual representation deferred to Slice 17.2.
- [x] `HexCorner` — canonical hex-vertex identity (Slice 17.3 / ADR-0025). Three hexes meet at every corner and every corner has three neighbours; a step between adjacent corners is exactly one hex edge. Canonicalizes the three possible `(tile, index)` namings to the lex-smallest owning tile, mirroring `RiverEdge`. Wrap-aware, so it lives in `Data` rather than `Coordinates`. `GetIncidentTiles`, `GetAdjacentCorners`, and `TryGetSharedEdge` are allocation-free.
- [ ] JSON / binary serialization with a stable schema version

## ScriptableObject content

- [~] `BiomeDefinition` SO (climate envelope, base color, water/coastal flags shipped in Slice 6; `Prefab` visual reference added in Slice 10 per ADR-0017; allowed-resources list lands alongside the resource slice)
- [x] `ResourceDefinition` SO (Slice 15 / ADR-0022 + Slice 16 / ADR-0023): placement rules (AllowedBiomes, AllowedFeatures, MinElevationLevel..MaxElevationLevel), Rarity weight, MinimumSpacing, optional Prefab, Color. Plus `ReplaceTile` toggle + per-(biome × feature) `ReplacementPrefabs` array (auto-synced by the custom inspector to AllowedBiomes × AllowedFeatures cross-product). Default render mode positions the resource Prefab on the surface of the underlying tile (no clipping — renderer caches per-source-prefab visible top); replace-tile mode swaps the tile's biome / feature visual entirely with a per-combo prefab. 18 shipped under `Content/Resources/`.
- [x] `TileFeatureDefinition` SO (Slice 7B; carries FeatureName, Color, PreferredElevationLevel, Weight, IsImpassable, MovementCostMultiplier per ADR-0014; 8 default features shipped; `Prefab` visual reference added in Slice 10 per ADR-0017)
- [x] `WorldPreset` SO (Slice 14 / ADR-0021): bundles `Width`, `Height`, `LandRatio`, `ContinentShapeAlgorithm`, `BiomeCohesion`, `FeatureCohesion`, and the `Biomes` roster into a buyer-authored asset. `ToSettings(seed)` builds the runtime `WorldGenerationSettings`. Bootstrap optionally consumes a preset (falls back to inspector fields when null). Four shipped presets under `Content/Presets/`: Earthlike, Pangaea, Archipelago, InlandSea. Buyers duplicate-and-edit those or build new ones via Create → Hex World Studio → World Preset.
- [x] Default content set v1 — 7 starter biome `BiomeDefinition.asset` files shipped under `Assets/HexWorldStudio/Content/Biomes/` per ADR-0011: Ocean, Coast, Temperate, Arid, Tropical, Tundra, Snow. Climate envelopes exhaustively partition the (T, M) unit square for land tiles. Mediterranean and the full Civilization VII catalog remain the long-term parity target per ADR-0007.
- [x] Default tile-feature set v1 (Slice 7B): Grassland, Savannah, Desert, Snowfield, Forest, Hill, Mountain, Rocky shipped under `Assets/HexWorldStudio/Content/Features/`. Per-biome allow-lists wired per the ADR-0011 / ADR-0014 table.
- [x] Per-biome feature variants (Slice 11.3): seven climate-specific feature SOs under `Assets/HexWorldStudio/Content/Features/Variants/` — `Grassland_{Tundra, Temperate, Tropical}` and `Forest_{Snow, Tundra, Temperate, Tropical}`. The four affected biome SOs (Snow, Tundra, Temperate, Tropical) reference the variant in their `AllowedFeatures` arrays instead of the generic feature, so a Tropical tile gets `Grassland_Tropical` and a Tundra tile gets `Grassland_Tundra` automatically. Each variant has its own `Color`, `Prefab`, and gameplay metadata so buyers differentiate any combination of those per climate. The base `Grassland.asset` / `Forest.asset` remain as default / fallback content for buyers who don't want differentiation. Geological features (`Hill`, `Mountain`, `Rocky`) are shared across all biomes (no variant needed); buyers can author climate-flavored variants for those following the same pattern if desired.

## Custom Editor

- [ ] Generator window with seed, size, preset, biome/resource selection
- [ ] Live preview (small-map regeneration on parameter change)
- [ ] Side-by-side seed comparison
- [ ] Export generated map as asset (ScriptableObject) or JSON
- [ ] Inspector property drawers for axial coordinates, biome references, resource references
- [ ] Diagnostic overlay (elevation, temperature, moisture, biome, river-flow, resources — toggleable)

## Runtime API

- [x] `HexWorldGenerator.Generate(WorldGenerationSettings)` entry point (Slice 4; instance-based, settings validated at boundary, empty default pipeline pending stage slices)
- [ ] Async / coroutine-friendly variant with progress callback
- [~] Public read-only access to `WorldMap` and `HexTile` (public types with internal setters on per-tile mutable state, `TryGetTile` / `Normalize` / `Contains` / `IndexOf` on `WorldMap`; XML doc comments in place. Closes when content SO references and serialization land.)
- [ ] Public events for pipeline stages (for buyers who want to hook in)
- [~] XML doc comments on every public type and member (in place on every public type shipped so far — `HexCoord`, `HexCoordExtensions`, `HexDirection`, `HexTile`, `TileLandState`, `WorldMap`, `WorldGenerationSettings`, `HexWorldGenerator`. Maintained as a hard rule per `CLAUDE.md`; flips to `[x]` when the v1 public surface is frozen.)

## Rendering / Visualization

- [~] URP demo renderer (Slice 7A): `WorldRenderer` MonoBehaviour produces one GameObject per tile, shared `HexMesh`, per-biome `Material` cache, per-tile `MaterialPropertyBlock` for color (feature color preferred over biome color), `MeshCollider` for Slice 9 raycasts. Chunked / instanced rendering deferred per FEATURES "Performance & scaling". Tile Y uses a Civ-style stepped baseline (Slice 11.2): Ocean at `OceanRenderHeight`, Coast at `OceanRenderHeight + CoastRiseAboveOcean` (continental-shelf look), land at `OceanRenderHeight + LandRiseAboveWater + ElevationLevel * ElevationStep`. All three knobs configurable per-renderer.
- [~] Low-poly biome materials — URP/Lit material per biome with `_BaseColor` set from `BiomeDefinition.Color` (Slice 7A). Custom URP shader graphs deferred.
- [x] Cylindrical render wrap (`WorldWrapRenderer`, Slice 8 / ADR-0015): two visual copies of the world at ±`WorldWidthInWorldUnits` with per-tile MaterialPropertyBlock copy + collider re-assertion. `WorldToCoord(worldPoint, map)` resolves hits on any copy back to the canonical coord.
- [x] Civ-style camera controller (`CivCameraController`, Slice 8): pan (WASD / arrows / edge-scroll), zoom (scroll wheel, camera height 6 to 22; starts fully zoomed out at 22, Slice 18.3), orbital yaw rotate (Q/E and middle-mouse drag), tilt (R/F and right-mouse drag, default 50°, clamped 40°–65°). Soft vertical bounds; horizontal wrap normalization. Reads input through `ICameraInput`.
- [x] Input abstraction (`ICameraInput` + `LegacyUnityInputProvider` in `Runtime/Input/`, Slice 8): public interface buyers swap to migrate to the new Input System.
- [x] New Input System provider + runtime installer (Slice 11 / ADR-0018): `Runtime/Input/InputSystem/InputSystemInputProvider.cs` in its own define-constrained asmdef reads from `Keyboard.current` / `Mouse.current`. `Runtime/Input/InputSystem/InputSystemBootstrap.cs` registers a factory on `CameraInputInstaller.InputSystemFactory` via `[RuntimeInitializeOnLoadMethod]` so the main asmdef can pick the new provider without naming its type directly. `Runtime/Input/CameraInputInstaller.cs` calls the factory when set and falls back to `LegacyUnityInputProvider` under `#if ENABLE_LEGACY_INPUT_MANAGER` otherwise. `LegacyUnityInputProvider`'s call sites are wrapped in `#if ENABLE_LEGACY_INPUT_MANAGER` so they stub to zero under new-only projects instead of throwing. QuickStartScene now wires the installer; the demo runs under all three `Active Input Handling` configurations without buyers touching Player Settings.
- [~] Prefab tile visuals (Slice 10 / ADR-0017): `BiomeDefinition.Prefab` and `TileFeatureDefinition.Prefab` optional `GameObject` fields. `WorldRenderer` instantiates the assigned prefab as a child of the tile root (feature wins over biome) and falls back to the procedural `HexMesh` path when both are null. Tile root always carries `HexTileBinding` + a flat-`HexMesh` `MeshCollider` so picking works regardless of prefab geometry.
- [ ] Prefab variants + per-tile rotation (Slice 10.1): `BiomeDefinition.PrefabVariants[]` (and feature counterpart) for visual variety, with per-tile deterministic Y rotation so identical prefabs don't tile visibly.
- [x] `[CustomEditor]` for `BiomeDefinition` / `TileFeatureDefinition` (Slice 11.4): `Editor/Inspectors/BiomeDefinitionEditor.cs` adds a 2D Whittaker chart (T × M) above the default fields showing every land biome's envelope as a draggable colored rectangle. Drag edges to resize, drag interior to translate (snaps to 0.05); click any other biome's rect to ping it in the Project view. Coverage diagnostics under the chart warn on uncovered (T,M) regions and flag overlapping envelope pairs. `Editor/Inspectors/TileFeatureDefinitionEditor.cs` is the lighter sibling — same default fields, just routes the `Prefab` slot through `PermissivePrefabField` (manual drag-drop bypass) so Prefab Variants of FBX models can be assigned without hitting Unity 6's "Content type mismatch" rejection. Pure helper `BiomeEnvelopeCoverage` lives in `Runtime/Content/` (testable).
- [x] Bundled Kenney Hexagon Kit content (Slice 10.2, shipped ahead of 10.1): CC0 1.0 Universal models under `Assets/ThirdParty/Models/` (FBX + GLB + OBJ formats + `Textures/`) with `Previews/` for buyer reference. Sample biome/feature → prefab mapping under `Assets/HexWorldStudio/Content/Prefabs/`; default biome and feature assets reference those prefabs through their `Prefab` field so the QuickStart scene renders Kenney models out of the box. Attribution in `Third-Party Notices.txt`. Known authoring caveat: Unity 6's two-pane Object Picker rejects Prefab Variants whose source is an FBX with "Content type mismatch" — workaround is to Unpack Completely; a `[CustomEditor]` that bypasses the picker filter is a planned follow-up.
- [x] Non-navigable river ribbon meshes (along edges) (Slice 17.3 / ADR-0025, superseding the ADR-0024 renderer): public `RiverRenderer` MonoBehaviour builds **one continuous mitered ribbon per `RiverPath`** into a single shared `Mesh` textured by the assigned `RiverStyleAsset`. Consecutive steps share a corner, so cross-sections miter at the joints and segments meet exactly — no caps, no overlap tricks, no seams. Height follows the terrace levels, so adjacent segments agree by the generator's continuity invariant instead of stair-stepping. `V` accumulates monotonically source-to-mouth, so a scrolling shader animates a whole river coherently downstream (previously adjacent quads could scroll in opposite directions). Width tapers from `WidthRelativeToHex` toward `MaxWidthRelativeToHex` by Strahler order. Wrap-aware: corner positions are unwrapped against the previous corner, and `WorldWrapRenderer` takes an explicit `RiverRenderer` reference rather than relying on the two components sharing a GameObject. `RiversRebuilt` event unchanged.
- [x] Waterfall geometry (Slice 17.3 / ADR-0025): a waterfall step renders as a flat lip, a near-vertical face, and a plunge pool, sized by `RiverStyleAsset.WaterfallLipFraction`, descending exactly one terrace step. Falls out of the generator's `FromLevel`/`ToLevel` per step — no separate authoring.
- [x] Lakes render at terrace height (Slice 17.3 / ADR-0025): `WorldRenderer.ComputeTileY` treats `HexTile.IsLake` as land for height purposes. Pinning inland water to sea level punched a hole straight through the highlands.
- [x] `HexMesh.CornerPosition(coord, index)` / `(HexCorner)` (Slice 17.3 / ADR-0025): the shared source of hex-corner world geometry for renderers and buyer code. All three tiles meeting at a corner resolve to the same point, which is what lets edge-based geometry (rivers, borders, outlines) meet exactly.
- [ ] Navigable river tile geometry/material (per-tile) (deferred to Slice 17.2)
- [ ] Resource prop placement
- [ ] Camera rig for demo scenes

## Demo Scenes

- [~] "Quick Start" scene (Slice 7A + Slice 8 + Slice 9 + Slice 11): hand-authored `Examples/QuickStart/QuickStartScene.unity` ships with a Bootstrap GameObject pre-wired to the 7 default biome assets, the `WorldRenderer`, and a `WorldWrapRenderer`. The camera carries `CameraInputInstaller` (Slice 11) which adds the right `ICameraInput` provider for the project's Active Input Handling setting, plus `CivCameraController` (Civ-style pan / zoom / orbital rotate / tilt). A `TileInteraction` GameObject hosts `TileHover` and `TileSelection`. Bootstrap defaults to `RandomizeSeedOnRegenerate = true` so each Regenerate produces a new world; uncheck and edit the Seed field to pin a specific world (Slice 11.1 fix). Press Play to fly around, hover, and select.
- [x] Hex-outline overlay helper (`HexOutline.Create`, Slice 9 / ADR-0016) — closed `LineRenderer` outline at `OuterRadius`, used by hover and selection layers.
- [x] Per-tile coord lookup component (`HexTileBinding`, Slice 9 / ADR-0016) — `WorldRenderer` attaches one per tile so picking is a constant-time component lookup.
- [x] Hover layer (`TileHover`, Slice 9): camera-raycast picking with `WorldWrapRenderer.WorldToCoord` math fallback; translucent white wash over the whole tile (`TileHighlight`, Slice 18.4 / ADR-0028, replaced the outline); `HoverDuration` and `HoveredTileObject` exposed.
- [x] Selection layer (`TileSelection`, Slice 9): primary click commits, secondary clears, `WorldGeneratorBootstrap.OnRegenerated` clears stale state. Selected tile gets a translucent amber wash (Slice 18.4 / ADR-0028).
- [x] QuickStart visual pass (Slice 18.5): 4x MSAA on the PC quality level, shadow distance 100, dedicated `QuickStartVolumeProfile` (ACES tonemapping, color adjustments, gentle bloom, soft vignette), gradient ambient light, linear haze fog (45 to 140) matching a solid background color, and an endless `BackgroundSea` plane at sea level colored to match the deep-water tiles. `FlowingWater.shader` now supports fog.
- [x] Flat water bodies + cliffs under raised tiles (Slice 18.2 / ADR-0027): every connected water body renders at one height (sea at sea level, each lake body at its lowest shore); optional `WorldRenderer.CliffPrefab` stacks cliff pieces under any tile down to its lowest neighbor. QuickStart uses `Content/Prefabs/Tiles/Cliff.prefab`.
- [x] Runtime regenerate overlay (`RegenerateOverlay`, Slice 18): drop-in IMGUI panel in `Examples/QuickStart/Scripts/` for visitors at the keyboard. New random world (Space / N), rebuild a typed seed, step map type, nudge land ratio, hide with H. Finds the bootstrap itself; nothing references it, so deleting its GameObject removes it cleanly. No Canvas, EventSystem, or Input System dependency. Lives on its own `RegenerateOverlay` GameObject in the QuickStart scene.
- [ ] "Earth-like" preset showcase
- [ ] "Archipelago" preset showcase
- [ ] "Frozen World" preset showcase
- [ ] "API only — no renderer" scene proving data/rendering separation

## Tests

- [x] EditMode tests for axial / cube / offset coordinate math
- [x] EditMode tests for neighbor / range / line queries
- [ ] EditMode tests for determinism (same seed + settings → identical `WorldMap`)
- [ ] EditMode tests for biome classification edge cases
- [x] EditMode tests for river invariants (Slice 17 + 17.3): `RiverEdgeTests`, `WorldMapRiversTests`, `HexCornerTests`, `DepressionFillTests`, `RiverStageTests`, `RiverRenderGeometryTests`. The ADR-0025 suite asserts the properties that were previously only eyeballed: consecutive steps share a corner, no edge touches water, channels run between equal elevation levels, no two consecutive waterfalls, terrace levels are continuous along a path, only a path's final corner may touch water, no path below `MinRiverLength`, `RiverEdges` equals the union of path edges, and full determinism of rivers *and* lakes. Plus a guard that default settings actually produce rivers, so the negative assertions cannot all pass vacuously. Navigable↔non-navigable transition tests deferred to Slice 17.2; PlayMode tests for the built mesh deferred.
- [ ] PlayMode tests for pipeline orchestrator cancellation and progress reporting

## Documentation (ships with package)

- [ ] Getting Started guide (5-minute first generated world)
- [ ] Concepts: data vs rendering separation
- [ ] Concepts: ScriptableObject content authoring
- [ ] Concepts: determinism + seeding
- [ ] Cookbook: replace our renderer with yours
- [ ] Cookbook: add a custom biome
- [ ] Cookbook: add a custom generation pass
- [ ] API reference (generated from XML doc comments)
- [ ] Concept doc: WorldMap mutability contract (what's safe to mutate, what isn't)
- [ ] Concept doc: river representation (navigable vs non-navigable, transitions)
- [ ] DocFX setup at release time for API reference generation
- [ ] FAQ + troubleshooting

## Exhibition (ADR-0026, replaces the Asset Store marketing plan)

- [x] README written for visitors (what it is, how to run it, controls, how generation works, code sample, credits)
- [x] Third-Party Notices cover every bundled kit (Kenney Hexagon Kit, Cube Pets, Food Kit; all CC0)
- [x] LICENSE finalized as all rights reserved (MIT expected later, owner's call)
- [x] Runtime regenerate overlay in the QuickStart scene (Slice 18)
- [x] Test suite green (469 / 469)
- [ ] Screenshots / GIF in the README
- [ ] Windows standalone build of the QuickStart scene (owner is doing this)
- [ ] Make the GitHub repo public (owner's call, later)
- [-] Asset Store listing copy, hero image, icon, landing-page mock (descoped, ADR-0026)
- [-] Asset Store demo video (descoped; a short capture for the exhibition may still happen)

## Performance & scaling

- [ ] Per-biome instanced rendering (one draw call per biome batch) — required before maps grow past ~50×40 tiles
- [ ] Chunked rendering (group tiles into ~32×32 chunks, build one combined mesh per chunk) — deferred; current demo strategy is one GameObject per tile, which is fine for demo maps up to ~30×30 but will not scale to v1 target sizes. Revisit when a 200×200 demo is actually authored.
- [ ] GPU instancing path via `Graphics.DrawMeshInstanced` for biome props (forest trees, mountain peaks, resource models)
- [ ] Generator performance budget: sub-2-second `Generate()` for 100×60 maps on a baseline desktop (target, not yet measured)

## Stretch — v1.1

- [ ] Mobile platform support (iOS/Android performance pass)
- [ ] WebGL platform support
- [ ] Built-in render pipeline shipping templates
- [ ] HDRP demo (separate package or upgrade path)
- [-] Unity new Input System alternative input layer — pulled into v1 as Slice 11 (ADR-0018) after `Active Input Handling = Input System Package` projects hit a runtime `InvalidOperationException` from the legacy provider.
- [ ] Public extension API for buyer-authored `IGenerationStage`s (per ADR-0010)
