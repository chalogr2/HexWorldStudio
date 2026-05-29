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

- [x] Pure Perlin percentile-threshold (Slice 5 baseline / default)
- [ ] Continents
- [ ] Continents Plus
- [ ] Pangaea
- [ ] Archipelago
- [ ] Fractal
- [ ] Inland Sea
- [ ] Island Plates
- [ ] Lakes
- [ ] Highlands
- [ ] Small Continents
- [ ] Mirror
- [ ] Shuffle / random pick from the above
- [ ] Custom buyer-supplied `IGenerationStage` (formal extension API is v1.1 candidate)

Elevation, climate, hydrology, biome, and resource stages each get the same "pluggable per slot" treatment; additional implementations beyond the default are appended here as they are authored.

## Generation Pipeline

- [x] Deterministic seeded RNG service (same seed + settings → byte-identical output)
- [~] Continent/landmass shape generation — pure Perlin percentile-threshold baseline shipped in Slice 5 (honors `WorldGenerationSettings.LandRatio` to within one tile); Civ-style algorithm variants tracked in "Map type algorithms" still pending
- [x] Elevation field generation — fBm Perlin, cylindrical, normalized to [0, 1] (Slice 5)
- [x] Climate model: temperature based on latitude + elevation (Slice 6; lapse rate + Perlin perturbation; latitude-banded variants per ADR-0010 still pending)
- [x] Climate model: moisture / precipitation based on prevailing winds (Slice 6 per ADR-0012; east-to-west wind transport with orographic precipitation and rain shadow; latitude-banded wind variants per ADR-0010 still pending)
- [~] Biome assignment from temperature/moisture lookup (Slice 6 envelope-based first-match-wins assignment shipped; richer Whittaker-style or weighted variants per ADR-0010 still pending)
- [ ] River generation along elevation gradients, terminating at oceans or lakes
- [ ] Hydrology pass: decide navigable (tile) vs non-navigable (edge) river segments and transition points
- [ ] Resource placement honoring biome rules and minimum-distance constraints
- [ ] Starting-position selection (configurable player count, fairness/distance heuristics)
- [ ] Map post-processing pass (smoothing, error correction, isolated-tile cleanup)
- [~] Single-pass pipeline orchestrator with cancellation support (orchestrator + `IGenerationStage` + `GenerationContext` landed in Slice 4; default pipeline currently empty, stages append per slice; cancellation / progress / async variant deferred to a later slice)

## Data Model

- [~] `WorldMap` — pure-data root container (cylindrical wrap + tile array + lookup API in; serialization, river network, biome/feature/resource refs pending)
- [~] `HexTile` — per-tile data (coord, index, elevation, temperature, moisture, land state, discrete `ElevationLevel`, biome reference, and `TileFeature` reference all in; resource references land with the resource slice)
- [x] Discrete `ElevationLevel` field on `HexTile` (int, 0..N-1) plus the `ElevationLevelQuantizationStage` that derives it from the continuous `Elevation` field with median-smoothing over the wrap-aware 6-neighborhood (Slice 5; default `N = 6`, per-map configurability lands with `WorldPreset` content)
- [x] Axial / cube / offset coordinate utilities and conversions
- [~] Neighbor / range / line-of-sight queries (neighbor / range / line drawing landed; true LOS with obstacles requires `WorldMap` and is deferred)
- [ ] River network representation: edge-based non-navigable segments + tile-based navigable segments + explicit transition nodes
- [ ] JSON / binary serialization with a stable schema version

## ScriptableObject content

- [~] `BiomeDefinition` SO (climate envelope, base color, water/coastal flags shipped in Slice 6; allowed-features and allowed-resources lists land alongside the feature and resource slices)
- [ ] `ResourceDefinition` SO (placement rules, rarity, biome compatibility)
- [x] `TileFeatureDefinition` SO (Slice 7B; carries FeatureName, Color, PreferredElevationLevel, Weight, IsImpassable, MovementCostMultiplier per ADR-0014; 8 default features shipped; visual prefab references deferred to a polish slice)
- [ ] `WorldPreset` SO (named bundles of generation settings — "Earth-like", "Archipelago", "Frozen", etc.)
- [x] Default content set v1 — 7 starter biome `BiomeDefinition.asset` files shipped under `Assets/HexWorldStudio/Content/Biomes/` per ADR-0011: Ocean, Coast, Temperate, Arid, Tropical, Tundra, Snow. Climate envelopes exhaustively partition the (T, M) unit square for land tiles. Mediterranean and the full Civilization VII catalog remain the long-term parity target per ADR-0007.
- [x] Default tile-feature set v1 (Slice 7B): Grassland, Savannah, Desert, Snowfield, Forest, Hill, Mountain, Rocky shipped under `Assets/HexWorldStudio/Content/Features/`. Per-biome allow-lists wired per the ADR-0011 / ADR-0014 table. Jungle / Rainforest remain candidates for a future Tropical expansion.

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

- [~] URP demo renderer (Slice 7A): `WorldRenderer` MonoBehaviour produces one GameObject per tile, shared `HexMesh`, per-biome `Material` cache, per-tile `MaterialPropertyBlock` for color (feature color preferred over biome color), `MeshCollider` for Slice 9 raycasts. Chunked / instanced rendering deferred per FEATURES "Performance & scaling".
- [~] Low-poly biome materials — URP/Lit material per biome with `_BaseColor` set from `BiomeDefinition.Color` (Slice 7A). Custom URP shader graphs deferred.
- [ ] Non-navigable river ribbon meshes (along edges)
- [ ] Navigable river tile geometry/material (per-tile)
- [ ] Resource prop placement
- [ ] Camera rig for demo scenes

## Demo Scenes

- [~] "Quick Start" scene (Slice 7A): hand-authored `Examples/QuickStart/QuickStartScene.unity` ships with a Bootstrap GameObject pre-wired to the 7 default biome assets and the `WorldRenderer`. Press Play to see a 32×20 world. Regenerate button comes when settings UI lands.
- [ ] "Earth-like" preset showcase
- [ ] "Archipelago" preset showcase
- [ ] "Frozen World" preset showcase
- [ ] "API only — no renderer" scene proving data/rendering separation

## Tests

- [x] EditMode tests for axial / cube / offset coordinate math
- [x] EditMode tests for neighbor / range / line queries
- [ ] EditMode tests for determinism (same seed + settings → identical `WorldMap`)
- [ ] EditMode tests for biome classification edge cases
- [ ] EditMode tests for river termination invariants (incl. navigable↔non-navigable transition rules)
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

## Marketing Assets

- [ ] Asset Store listing copy (title, short description, long description, keywords)
- [ ] Hero image (1950×1300)
- [ ] Icon (160×160)
- [ ] Screenshot set (5–8 images)
- [ ] Demo video (60–90s)
- [ ] Asset Store landing-page mock for internal review

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
- [ ] Unity new Input System alternative input layer (mirroring the legacy-Input-based demo controls from Slice 8)
- [ ] Public extension API for buyer-authored `IGenerationStage`s (per ADR-0010)
