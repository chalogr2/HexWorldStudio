# FEATURES — living inventory

Every feature, planned or implemented, lives here. **Add a feature here before writing code for it.**

## Status legend

- `[ ]` planned
- `[~]` in progress
- `[x]` done
- `[!]` blocked
- `[-]` descoped

---

## Generation Pipeline

- [ ] Deterministic seeded RNG service (same seed + settings → byte-identical output)
- [ ] Continent/landmass shape generation (configurable: Pangaea, archipelago, continents, custom)
- [ ] Elevation field generation
- [ ] Climate model: temperature based on latitude + elevation
- [ ] Climate model: moisture / precipitation based on prevailing winds + distance from water
- [ ] Biome assignment from temperature/moisture/elevation lookup (Whittaker-style)
- [ ] River generation along elevation gradients, terminating at oceans or lakes
- [ ] Resource placement honoring biome rules and minimum-distance constraints
- [ ] Starting-position selection (configurable player count, fairness/distance heuristics)
- [ ] Map post-processing pass (smoothing, error correction, isolated-tile cleanup)
- [ ] Single-pass pipeline orchestrator with cancellation support

## Data Model

- [ ] `WorldMap` — pure-data root container
- [ ] `HexTile` — per-tile data (axial coords, elevation, temperature, moisture, biome, features, resource)
- [ ] Axial / cube / offset coordinate utilities and conversions
- [ ] Neighbor / range / line-of-sight queries
- [ ] River network representation (edge-based, not tile-based)
- [ ] JSON / binary serialization with a stable schema version

## ScriptableObject content

- [ ] `BiomeDefinition` SO (climate envelope, base color, allowed features, allowed resources)
- [ ] `ResourceDefinition` SO (placement rules, rarity, biome compatibility)
- [ ] `TileFeatureDefinition` SO (forests, hills, marshes — visual + gameplay tags)
- [ ] `WorldPreset` SO (named bundles of generation settings — "Earth-like", "Archipelago", "Frozen", etc.)
- [ ] Default content set: 8–12 biomes, 6–10 resources, core feature set

## Custom Editor

- [ ] Generator window with seed, size, preset, biome/resource selection
- [ ] Live preview (small-map regeneration on parameter change)
- [ ] Side-by-side seed comparison
- [ ] Export generated map as asset (ScriptableObject) or JSON
- [ ] Inspector property drawers for axial coordinates, biome references, resource references
- [ ] Diagnostic overlay (elevation, temperature, moisture, biome, river-flow, resources — toggleable)

## Runtime API

- [ ] `HexWorldGenerator.Generate(WorldGenerationSettings)` entry point
- [ ] Async / coroutine-friendly variant with progress callback
- [ ] Public read-only access to `WorldMap` and `HexTile`
- [ ] Public events for pipeline stages (for buyers who want to hook in)
- [ ] XML doc comments on every public type and member

## Rendering / Visualization

- [ ] URP demo renderer: instanced hex meshes per biome
- [ ] Low-poly biome materials (URP shader graphs or simple lit shaders)
- [ ] River mesh generation (low-poly ribbons along edges)
- [ ] Resource prop placement
- [ ] Camera rig for demo scenes

## Demo Scenes

- [ ] "Quick Start" scene: minimal generator + camera, one click to regenerate
- [ ] "Earth-like" preset showcase
- [ ] "Archipelago" preset showcase
- [ ] "Frozen World" preset showcase
- [ ] "API only — no renderer" scene proving data/rendering separation

## Tests

- [ ] EditMode tests for axial / cube / offset coordinate math
- [ ] EditMode tests for neighbor / range / line queries
- [ ] EditMode tests for determinism (same seed + settings → identical `WorldMap`)
- [ ] EditMode tests for biome classification edge cases
- [ ] EditMode tests for river termination invariants
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
- [ ] FAQ + troubleshooting

## Marketing Assets

- [ ] Asset Store listing copy (title, short description, long description, keywords)
- [ ] Hero image (1950×1300)
- [ ] Icon (160×160)
- [ ] Screenshot set (5–8 images)
- [ ] Demo video (60–90s)
- [ ] Asset Store landing-page mock for internal review

## Stretch — v1.1

- [ ] Mobile platform support (iOS/Android performance pass)
- [ ] WebGL platform support
- [ ] Built-in render pipeline shipping templates
- [ ] HDRP demo (separate package or upgrade path)
