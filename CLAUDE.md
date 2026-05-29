# CLAUDE.md — orientation for future Claude Code sessions

You are reading this because you are continuing development on **Hex World Studio**, a Unity Asset Store product. Read this file first, then check [`FEATURES.md`](FEATURES.md), [`DECISIONS.md`](DECISIONS.md), and (when it exists) `ARCHITECTURE.md`. Only then begin work.

---

## What we're building

A **procedural hex world generator for Unity** — a code-first asset that generates believable, Civ-style hex maps (continents, biomes, rivers, resources, starting positions) for indie devs making 4X, turn-based strategy, or hex-grid games.

- Provisional product name: **Hex World Studio** (not finalized).
- Target sale price: **$39.99**, launching at **$29.99** with the Asset Store new-release discount.
- Scope is *only* world generation + data layer + custom editor + clean runtime API. It is **not** a combat framework, **not** a city builder, **not** a full 4X engine.

## Technical baseline (non-negotiable unless explicitly revisited)

| Area | Decision |
|---|---|
| Authoring Unity version | **6000.0.68f1** (Unity 6.0 LTS) |
| Minimum supported Unity for buyers | **6000.0.58f2** (first patch with CVE-2025-59489 fix) |
| Versions to test before launch | 6000.0.68f1, latest 6.0 LTS patch, latest 6.1 LTS patch, latest 6.3 LTS patch |
| Render pipeline | URP primary. Built-in via manual material conversion acceptable but not v1 priority. **HDRP is out of scope for v1.** |
| Language | C# only |
| Dependencies | **Zero** third-party plugins or paid Asset Store dependencies. The asset must drop into any project. |
| Platforms (v1) | Editor + standalone (Windows/Mac/Linux). Mobile and WebGL are stretch for v1.1. |
| Art style | Low-poly 3D demo scenes. Custom-modified Kenney Hexagon Kit (CC0) plus original Asset Forge work. Attribution lives in [`Third-Party Notices.txt`](Third-Party%20Notices.txt). |

**Never reference, install, or recommend Unity editor versions older than `6000.0.58f2`.** Versions before that contain CVE-2025-59489 (CWE-426 untrusted search path, CVSS 8.4). See [`DECISIONS.md`](DECISIONS.md) ADR-0001 for context.

## Architectural principles

1. **Data and rendering are completely separate.** The generator produces a pure-data `WorldMap`. A separate visualization layer renders it. Buyers must be able to use the data without our renderer and swap our renderer without touching our data.
2. **ScriptableObject-driven content.** Biomes, resources, tile features, and world presets are ScriptableObjects. Buyers add content from the inspector, not by editing code.
3. **Deterministic generation.** Same seed + same settings = byte-identical output. Always. Critical for multiplayer, daily challenges, reproducibility.
4. **Public API stable; internals can change.** Public-facing types get XML doc comments. Everything else is `internal` by default so we can refactor without breaking buyers.
5. **No magic.** No reflection-based wiring, no unmarked side effects, no clever metaprogramming. Any file should be understandable in 60 seconds.
6. **Tests over hopes.** Critical pipeline stages and determinism get unit tests via Unity Test Framework.

## Documentation discipline (mandatory)

Four files in the repo root, maintained in lockstep with the code:

- [`CLAUDE.md`](CLAUDE.md) — this file. Update whenever project state meaningfully changes.
- [`FEATURES.md`](FEATURES.md) — living inventory. **Add features here before implementing them.** Status legend: `[ ] planned`, `[~] in progress`, `[x] done`, `[!] blocked`, `[-] descoped`.
- [`DECISIONS.md`](DECISIONS.md) — architectural decision log. Add an entry whenever a meaningful choice is made (date, decision, alternatives, reasoning).
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — subsystem layout, pipeline order, code-layout map. Update whenever a subsystem's shape or contract changes.

**Hard rule: docs update in the same commit set as the work that triggered them.** If a prompt or exchange produces code, an ADR-worthy decision, a workflow change, or a clarification that future sessions will need to know about, the relevant docs are updated in the same set of commits as the code. No "I'll update docs later" — later is now. This applies equally to ADR additions, FEATURES status flips, ARCHITECTURE notes, and CLAUDE.md project-state refreshes.

Plus: XML doc comments on every public type and member from the moment it's created.

## Workflow rules

- **Propose before implementing.** Output a design document and wait for confirmation before writing code. Same for major changes.
- **Chain slices within a session.** Sessions are expected to span the rest of v1 development, not one slice per session (see ADR-0008). The propose-before-implementing gate is preserved per slice — each slice still gets a written proposal and an explicit approval before code lands — but completing one slice does not end the session. Move on to the next slice unless the user explicitly asks for a hard break.
- **Small commits, Conventional Commits format** (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`). One logical change per commit. The git log should read like a changelog.
- **No code without a place to put it.** Propose folder layout first.
- **Stop and ask** on ambiguity, principle conflicts, or scope expansion.
- **No new dependencies** (Unity packages, NuGet, npm, anything) without explicit approval.

## Repository layout

```
Hex Tile World Generator/                ← repo root
├── README.md, CLAUDE.md, FEATURES.md, DECISIONS.md, LICENSE, Third-Party Notices.txt
├── Assets/
│   ├── HexWorldStudio/                  ← everything that ships in the Asset Store package
│   │   ├── Runtime/                     ← public + internal runtime code; data model
│   │   ├── Editor/                      ← custom editor windows, drawers, importers
│   │   ├── Examples/                    ← demo scenes, sample ScriptableObjects, sample scripts
│   │   ├── Documentation/               ← buyer-facing docs that ship inside the package
│   │   ├── ThirdParty/                  ← Kenney-derived models + per-asset notices
│   │   └── Tests/{Runtime,Editor}/      ← Unity Test Framework tests
│   ├── Scenes/                          ← Unity-template SampleScene (untouched, may be removed later)
│   └── Settings/                        ← URP renderer + quality assets (project-level)
├── Packages/                            ← UPM manifest (will be slimmed before launch)
└── ProjectSettings/, UserSettings/, Library/, Logs/  ← Unity project state
```

Assembly definitions (`.asmdef`) for Runtime, Editor, Tests/Runtime, Tests/Editor will be added when the first script lands in each folder — creating empty asmdefs causes Unity warnings.

## Current project state

- Unity project at **6000.0.68f1** with URP 17.0.4 and Test Framework 1.6.0.
- Git on `main`, tracking `origin/main` at <https://github.com/chalogr2/Hex-Tile-World-Generator>.
- Four assembly definitions in place: `HexWorldStudio.Runtime`, `HexWorldStudio.Editor`, `HexWorldStudio.Tests.Runtime`, `HexWorldStudio.Tests.Editor`. Runtime assembly grants `InternalsVisibleTo` to both test assemblies.
- **Slice 1** (coordinate primitives): `HexCoord`, `HexCoordExtensions`, `HexDirection`.
- **Slice 2** (deterministic RNG): `RngService` (PCG XSH-RR-64-32, splittable via `Fork()` and `Fork(label)`).
- **Slice 3** (data model + cylindrical map): `HexTile`, `WorldMap`, `CoordinateSpace`. Horizontal wrap is enforced inside `WorldMap`; vertical axis is bounded. `TryGetTile` is the safe primary lookup.
- **Slice 4** (public entry point + pipeline scaffolding): `WorldGenerationSettings`, `HexWorldGenerator`, internal `IGenerationStage` interface, internal `GenerationContext`. Default pipeline initially empty. ADR-0010 (pluggable algorithms) and ADR-0011 (three-axis biome/feature/elevation-level tile model) recorded.
- **Slice 5** (first real stages): `Perlin` noise source (2D + 3D + cylindrical-friendly fBm), `ContinentShapeStage` (percentile-threshold algorithm honoring `WorldGenerationSettings.LandRatio`), `ElevationFieldStage` (fBm Perlin normalized to [0, 1]), `ElevationLevelQuantizationStage` (default N=6 levels + median-smoothing pass). `HexTile.ElevationLevel` field added (-1 sentinel).
- **Slice 6** (climate + biomes): `BiomeDefinition` ScriptableObject + 7 default biome assets under `Assets/HexWorldStudio/Content/Biomes/` (Ocean, Coast, Snow, Tundra, Arid, Temperate, Tropical). `ClimateStage` produces latitude-driven temperature (with elevation lapse and Perlin perturbation) and prevailing-wind moisture (east-to-west transport with orographic precipitation and rain shadow — ADR-0012). `BiomeAssignmentStage` maps water tiles to Coast / Ocean by neighbor topology and land tiles by first-match envelope. `HexTile.Biome` field added. `WorldGenerationSettings.Biomes` (5-arg ctor) wires content into the pipeline; `Generate` validates Ocean / Coast / Land roles are present. Biome content is fully modular: any IReadOnlyList<BiomeDefinition> works, default 7 are replaceable wholesale.
- **Slice 7** (URP rendering + tile features): Pointy-top hex orientation locked (ADR-0013). `Runtime/Rendering/`: `HexMesh` procedural mesh, `BiomeMaterialCache` URP/Lit per-biome material cache, `WorldRenderer` MonoBehaviour with `MeshCollider` per tile and `MaterialPropertyBlock` color flow, `WorldGeneratorBootstrap` demo wrapper. `Examples/QuickStart/QuickStartScene.unity` hand-authored. `TileFeatureDefinition` SO + 8 default feature assets under `Assets/HexWorldStudio/Content/Features/` (Grassland, Savannah, Desert, Snowfield, Forest, Hill, Mountain, Rocky). `BiomeDefinition.AllowedFeatures` wires features per biome. `TileFeaturePlacementStage` picks per-tile features via Gaussian-falloff elevation weighting per ADR-0014. `HexTile.Feature` added. Default pipeline now ends at feature placement; renderer prefers feature color over biome color.
- Cumulative tests: ~274 cases (268 EditMode + 6 PlayMode — first PlayMode tests of the project), all green under headless Unity 6000.0.68f1.
- Next work is whatever sits at the top of `FEATURES.md` planning order; see `ARCHITECTURE.md` for the pipeline ordering and code-layout map.

## What to do at the start of a new session

1. Read this file.
2. Read [`FEATURES.md`](FEATURES.md) to see what's planned, in progress, blocked.
3. Read recent entries in [`DECISIONS.md`](DECISIONS.md) for the latest architectural context.
4. Skim recent commits (`git log --oneline -20`) for what the last session shipped.
5. Confirm the user's task before touching code.
