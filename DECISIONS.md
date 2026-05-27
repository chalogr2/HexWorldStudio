# DECISIONS — architectural decision log

Append-only log of meaningful architectural choices. New entries go at the **bottom**. Each entry: date, decision, alternatives considered, reasoning. Do not edit past entries; if a decision is reversed, add a new entry that supersedes the old one and link both.

Format: lightweight ADR. Number sequentially.

---

## ADR-0001 — Unity editor version policy

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
Author Hex World Studio in **Unity 6000.0.68f1** (Unity 6.0 LTS). Declare **6000.0.58f2** as the minimum supported editor version for buyers. Before launch, verify compatibility on: 6000.0.68f1 (dev), the latest 6.0 LTS patch, the latest 6.1 LTS patch, and the latest 6.3 LTS patch.

**Alternatives considered:**

- *Support older 6.0 LTS patches (anything < 6000.0.58f2).* Rejected. Those versions contain **CVE-2025-59489** (CWE-426 untrusted search path, CVSS 8.4). Recommending vulnerable editor versions to buyers would be irresponsible.
- *Support Unity 2022 LTS.* Rejected for v1. URP 17 features and Unity 6 API ergonomics meaningfully improve the asset; back-porting would cost engineering time better spent on features.
- *Author on the very latest 6.3 LTS patch.* Rejected. Authoring on the oldest supported `.x` line and forward-verifying gives us the widest practical buyer compatibility for the same effort.

**Reasoning:**
The whole Unity 6.0 LTS train ≥ 6000.0.58f2 patches CVE-2025-59489. Authoring on 6000.0.68f1 sits comfortably above the patch line while staying on 6.0 LTS, which the majority of professional Unity teams are on or migrating to. Buyers on 6.1 and 6.3 LTS get supported paths via forward compatibility testing.

---

## ADR-0002 — Render pipeline scope for v1

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
**URP is the primary and only first-class render pipeline for v1.** Built-in Render Pipeline compatibility is acceptable via manual material conversion documentation but is not a v1 deliverable priority. **HDRP is out of scope for v1** and may be revisited for v1.1 or as a separate SKU.

**Alternatives considered:**

- *Ship URP + Built-in shipping templates from day one.* Rejected for v1. Doubles the shader/material maintenance surface and the QA matrix for marginal addressable-market gain — most active hex/4X buyers are on URP.
- *Ship URP + HDRP.* Rejected. HDRP's lighting model and material complexity are mismatched with the low-poly Civ-style aesthetic. Would inflate the package size, demand HDRP-specific demo scenes, and split design focus.
- *Render-pipeline-agnostic via shader graph variants.* Rejected. Adds significant authoring cost and complicates the buyer's first-run experience. Better to do one pipeline excellently.

**Reasoning:**
The architectural principle "data and rendering are completely separate" already means buyers can swap our renderer entirely. URP-only on the shipped renderer is the smallest, most focused v1 that still demos the product well. Buyers on Built-in or HDRP can either follow our migration cookbook or replace the visualization layer with their own — which is exactly the integration story we want to advertise.

---

## ADR-0003 — Dependency policy

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
**Zero third-party plugin or paid Asset Store dependencies.** Hex World Studio depends only on Unity itself and a curated subset of first-party Unity packages (Unity Test Framework, Input System, etc., to be finalized in a manifest review). No NuGet, no npm, no submodules. Adding any new dependency — first-party or otherwise — requires an explicit decision recorded as a new ADR.

**Alternatives considered:**

- *Depend on a popular noise library (e.g., third-party FastNoise).* Rejected. Means buyers either need the library installed already, get forced to install it via Package Manager, or hit DLL conflicts. We can write or vendor what we need under our own namespace.
- *Depend on a UI Toolkit / inspector helper asset for the custom editor.* Rejected. Same reason. Unity's native UI Toolkit + IMGUI is sufficient.
- *Allow optional integrations behind compile flags.* Deferred. Possibly worth revisiting post-launch for popular paid assets (e.g., specific terrain/AI assets), but only as opt-in extensions, never as hard dependencies.

**Reasoning:**
Frictionless install is a major selling point for Asset Store buyers. Every dependency increases the surface area for buyer-side conflicts, version mismatches, and licensing complications. Vendored or first-party implementations keep us in control of correctness, performance, and breakage surface. Any future dependency must clear a high bar and be documented here.

---

## ADR-0004 — `HexTile` is a class, not a struct

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
`HexTile` is a `class` (reference type) and is stored in `WorldMap` as a `HexTile[]` indexed by tile index (derived from `HexCoord`).

**Alternatives considered:**

- *`HexTile` as a `struct` in a flat array.* Strongest cache locality, zero GC, classic ECS-friendly layout. Rejected because: (a) buyers iterating `worldMap.Tiles` would silently get copies when they expected references, a real footgun; (b) any future field addition that pushes the struct over cache-line size silently regresses perf without any compiler signal; (c) `ref` returns are awkward in public API surface and many buyers won't reach for them.
- *`HexTile` as a `class` heap-stored individually, accessed via dictionary.* Rejected. Adds GC pressure and pointer chasing without a benefit our use case needs.

**Reasoning:**
For sub-200×200 maps (~40k tiles) the cache-locality advantage of struct storage is real but bounded — generation runs once and visualization batches by biome anyway, so per-tile iteration isn't the hot path. The ergonomic advantage of reference semantics is permanent: buyers passing tiles into their own gameplay code, holding references for unit attachment, and extending the type via composition all work cleanly. Easier to evolve over time, which was the explicit priority.

---

## ADR-0005 — `WorldMap` mutability is a documentation-only contract

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
`WorldMap` is a normal mutable class. We do not freeze it, do not return a readonly facade, and do not install runtime guards. The contract for what is safe to mutate is documented in `Documentation/Concepts/DataModelContract.md` and reinforced via `<remarks>` XML doc blocks on the relevant properties.

**Alternatives considered:**

- *Readonly-struct facade over the data.* Rejected. Adds API surface, blocks legitimate buyer modifications, and would require a parallel mutable "builder" type for our own pipeline.
- *Immutable post-construction (e.g., `WorldMap.Freeze()`).* Rejected. Buyers commonly want to edit tile state post-generation (replace a biome, swap a resource, paint over a region). A freeze step would force them to deep-clone first, defeating the point.
- *Mark dimensions/topology as `readonly` fields and leave per-tile state mutable.* Considered. Likely worth doing for fields that genuinely never change after construction (`dimensions`, `seed`); the rest stays mutable by contract. Implementation detail to revisit when `WorldMap` is actually written.

**Reasoning:**
The brief frames buyers as people who will extend and modify our output. Locking them out would require a parallel mutable API. Documentation is cheaper and matches how successful Unity assets typically draw the line. The cost is that misuse causes silent corruption rather than a compile error; we mitigate via clear concept docs and diagnostic overlays that re-read live state.

---

## ADR-0006 — Dual river representation: edge-based and tile-based

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
Rivers are represented two ways inside a single `RiverNetwork`:

- **Non-navigable** rivers as **edge segments** on hex seams (small streams, headwaters, tributaries).
- **Navigable** rivers as **tile occupants** (a tile *is* the river).

Transitions between the two forms along a river's flow are explicit nodes in `RiverNetwork`; the hydrology pass decides where each transition lands based on accumulated flow, slope, and biome rules.

**Alternatives considered:**

- *Edge-only.* Visually accurate for Civ-style maps, but cannot represent rivers as gameplay-traversable water (no "sail upriver" affordance) and forces awkward tile-pair lookups for any logic concerning "the river".
- *Tile-only.* Simpler data model; lets every river be navigable; but produces unrealistic wide-streams-everywhere visuals and eats a tile of land for every headwater stream.
- *Edge-only with a per-edge "navigability" flag.* Considered. Rejected because gameplay code (pathfinding, unit placement) needs a tile-level answer to "can a boat stop here" — bolting that onto edges turns into a permanent translation layer.

**Reasoning:**
Both representations have legitimate uses inside a single map: small streams want edges, major rivers want tiles. Modeling each in its natural form keeps each renderer and each gameplay query simple. The cost is a slightly more complex `RiverNetwork` (two collections + transition nodes) and a hydrology pass that has to decide the transition criteria. Both are acceptable.

---

## ADR-0007 — Default content set targets Civilization VII parity, extensible via ScriptableObjects

**Date:** 2026-05-26
**Status:** Accepted

**Decision:**
The default `BiomeDefinition` and `TileFeatureDefinition` content set shipped with Hex World Studio covers the full terrain / biome catalog of base Civilization VII (e.g., Plains, Grassland, Tundra, Desert, Tropical, Coast, Ocean, Snow / Ice, plus terrain modifiers like Hills and Mountains, and features like Forest, Rainforest, Marsh, Floodplains, Oasis). Buyers extend the catalog by authoring additional `BiomeDefinition` / `TileFeatureDefinition` ScriptableObjects — no engine-code change required. Fantasy biomes (volcanic wastes, magical forests, frozen seas, etc.) are a first-class supported use case, not an afterthought.

**Alternatives considered:**

- *Bespoke default biome set unrelated to any reference game.* Rejected. Civ VII is the dominant mental model for our buyer (4X / hex-grid devs). Mapping to its catalog gives buyers immediate familiarity and a known-good baseline to extend from.
- *Hardcoded `enum Biome` (or equivalent) in code.* Rejected. Adding a biome would require an asset update from us, defeating the SO content model and blocking fantasy use cases. Already excluded by ADR-0003-adjacent reasoning, but worth pinning explicitly so the constraint isn't lost.
- *Default set strictly larger than Civ VII (kitchen-sink).* Considered. Rejected for v1: more biomes inflate authoring/testing surface and dilute the "Civ-like out of the box" pitch. Extra biomes belong in buyer projects or in a separate content pack later.

**Reasoning:**
Two design pressures meet here. (1) Buyers building Civ-likes want a default that "just works" and matches the genre's vocabulary. (2) Buyers building fantasy 4X / hex-grid games want to swap, mix, or wholly replace the biome set without touching our code. The ScriptableObject content model serves both: ship a Civ VII–parity default set, but treat every biome as data that buyers can add to, override, or ignore.

**Implementation note:**
When the default biome SOs are actually authored in a later slice, the exact Civ VII terrain enumeration must be verified against the current live game. This ADR commits to parity as the design target; it does not lock the enumeration in code or in this document.

---

## ADR-0008 — Session workflow: chain slices within a single session

**Date:** 2026-05-27
**Status:** Accepted (supersedes the per-session-stop instruction in the Slice 1 briefing)

**Decision:**
For the remainder of v1 development, Claude Code sessions are expected to span multiple slices rather than one slice per session. The propose-before-implementing gate is preserved per slice — each slice still gets a written proposal and an explicit approval before code lands — but completing one slice does not end the session. Subsequent slices proceed within the same session unless the user explicitly asks for a hard break.

**Alternatives considered:**

- *One slice per session (the original Slice 1 briefing default).* Used for Slice 1. Gave a clean review pace and a forced stop after each push, but at the cost of ~5–10 minutes of doc-rereading at the start of every session plus an artificial break between slices whose work naturally chains (e.g., a data type and the first tests that consume it).
- *Drop the propose-before-implementing gate.* Rejected. The gate is the load-bearing part of the workflow contract: it catches scope, naming, and design drift before code lands, and surfaces ambiguity early. The right adjustment is to keep the gate but allow chained sessions.

**Reasoning:**
Once the project's design rhythm is established, per-session re-onboarding overhead outweighs the review-pacing benefit. Sessions can run long without losing context; the prompt cache and the unchanged project docs do the heavy lifting. The user retains full control — any "stop here" instruction or natural pause still ends the session — but the default shifts to continuity.

**Implementation note:**
`CLAUDE.md` workflow rules updated in the same commit to add a "Chain slices within a session" bullet; the "Current project state" section was refreshed to reflect Slice 1 outcomes.

---

## ADR-0009 — Map topology: cylindrical wrap on the horizontal axis

**Date:** 2026-05-27
**Status:** Accepted

**Decision:**
Maps generated by Hex World Studio are rectangular (`Width × Height` tiles) and topologically a cylinder: the horizontal axis wraps so the rightmost column connects seamlessly to the leftmost column, while the vertical axis is bounded (the top and bottom rows are hard edges representing the poles). Matches the Civilization map convention.

Wrap is enforced inside `WorldMap` via a `Normalize(HexCoord)` operation that maps any axial coordinate to its canonical in-range representative (Q wrapped via the offset-column modulus). `HexCoord` itself remains unchanged and unaware of wrap; wrap is a map-level topological property, not a coordinate-level one. Buyers receive the wrap automatically through `WorldMap.TryGetTile`, `WorldMap.Contains`, `WorldMap.Normalize`, and `WorldMap.IndexOf`. Buyer code that constructs raw `HexCoord` values and indexes its own data structures must call `WorldMap.Normalize` if it wants the same semantics.

Tiles with R outside `[0, Height)` are out-of-world. `TryGetTile` returns `false` for those; `Contains` returns `false`; no exception is thrown. Q is always in bounds via wrap, so a coordinate is "out of world" only on the vertical axis.

**Alternatives considered:**

- *Toroidal (both axes wrap).* Rejected. Breaks the globe mental model — sailing north should not deposit you at the south pole. Civ ships cylindrical for this reason.
- *No wrap (rectangular with hard edges on all four sides).* Rejected. Buyers building Civ-likes overwhelmingly expect horizontal wrap for the globe illusion. Forcing them to implement wrap on top of our data model defeats the "drop in and ship a 4X" pitch.
- *Configurable per-map (cylindrical vs no-wrap).* Considered. Useful long-term, but adds a topology setting every stage and renderer must respect. v1 locks in cylindrical; revisit if there is documented buyer demand for plane-only maps.
- *Encode wrap inside `HexCoord` (auto-normalize on construction).* Rejected. `HexCoord` is engine-agnostic and unaware of map size. Putting wrap in the coord type would couple every `HexCoord` to a map instance — a significant complication that buys nothing the map-level wrap doesn't already provide.

**Reasoning:**
Cylindrical wrap is the Civ-style default and meets the brief's mental model. Concentrating the wrap in `WorldMap` keeps `HexCoord` and `HexCoordExtensions` topology-agnostic and dependency-free, preserving Slice 1's architectural cleanliness, while still giving buyers a single safe entry point (`TryGetTile`) that handles wrap transparently. Vertical non-wrap matches real-globe geography and avoids the topological foot-gun of a torus.

**Implementation note:**
`Normalize` operates by round-tripping through odd-r offset (ADR-confirmed default), reducing the column with a true mathematical mod into `[0, Width)`, and converting back. R is left untouched, so `Normalize` of an out-of-vertical-bounds coord still has R out of bounds — pair with `Contains` for full validation.

**API shape (immediate consequences):**
The lookup API is `bool TryGetTile(HexCoord coord, out HexTile tile)`, not `HexTile GetTile(HexCoord coord)`. The latter would either return `null` (footgun) or throw (heavy for routine boundary checks); neither matches the cylindrical model's actual semantics (R out of bounds is a normal query outcome, not an error). Buyers always pair `TryGetTile` with a bool check; no nulls leak out of the data layer.

---

## ADR-0010 — Generation algorithms are pluggable; each pipeline-stage role accepts any `IGenerationStage` implementation

**Date:** 2026-05-27
**Status:** Accepted

**Decision:**
Every pipeline stage role (continent shape, elevation field, climate, biome assignment, hydrology, resource placement, starting positions, post-process) is filled by an implementation of `IGenerationStage`. Multiple alternative implementations can exist for the same role and are selected at generation time via `WorldGenerationSettings` (initially) and `WorldPreset` ScriptableObjects (later). The intent is to ship the full set of Civilization-style map-generation algorithms over time — at minimum the canonical map types (Continents, Continents Plus, Pangaea, Archipelago, Fractal, Inland Sea, Island Plates, Lakes, Highlands, Small Continents, and any others present in base Civ VII) — without rearchitecting.

**Alternatives considered:**

- *Hardcode one algorithm per stage.* Rejected. Civ-likes are built on diverse map types; locking in one shape per stage forces every buyer to fork our code or ship without the algorithm they actually want.
- *Heavy plugin architecture with discovery / dependency-injection / service-locator.* Rejected for v1. Violates the "no magic" principle in `CLAUDE.md`. The pluggable model is just an interface and an array — no reflection, no service locator, no runtime discovery.
- *Public extensibility from day one (buyers register their own `IGenerationStage`s).* Deferred. The interface is `internal` for v1; a formal buyer-extension API is a v1.1 candidate. Internal pluggability does not require public-API stability yet.

**Reasoning:**
The pipeline already uses `IGenerationStage` as an internal interface (Slice 4). Treating each stage role as a slot that accepts any implementation costs nothing extra in v1 (we ship one default per slot) and gives a clean path to add Civ-style algorithm variants over time. Buyer-side extensibility can be opened up later without breaking earlier slices.

**Implementation note:**
- Slice 4 ships an empty default pipeline (zero stages).
- Slice 5 introduces the first two slots (continent shape, elevation field) each with one default implementation (pure Perlin threshold; Perlin elevation).
- Additional Civ-style algorithms per slot land in dedicated slices as they're prioritized, each registered alongside the default in `HexWorldGenerator`'s pipeline list.
- The exact Civ-style algorithm catalog is tracked in `FEATURES.md` under "Map type algorithms". The list is verified against the live Civilization VII at the time each algorithm is actually authored (same pattern as ADR-0007's biome enumeration commitment).

---

## ADR-0011 — Per-tile classification: three-axis Biome / TileFeature / ElevationLevel model

**Date:** 2026-05-27
**Status:** Accepted

**Decision:**
Per-tile classification uses three orthogonal axes:

1. **Biome** — climate band, defined by `BiomeDefinition` SO. The v1 starter set is **Ocean, Coast, Temperate, Arid, Tropical, Tundra, Snow** (7 biomes). Mediterranean ships as a later content drop. Biomes are assigned by `BiomeAssignmentStage` from temperature + moisture + land/water classification.
2. **TileFeature** — terrain detail within a biome, defined by `TileFeatureDefinition` SO. Features fall into three rough families: flat-terrain variants (`Grassland`, `Savannah`, `Desert`, `Snowfield`), vegetation (`Forest`, and likely `Jungle` / `Rainforest` for Tropical), and elevation-shape modifiers (`Hill`, `Mountain`, `Rocky`). Each `BiomeDefinition` lists which `TileFeatureDefinition`s are legal for it. Ocean and Coast biomes have no features.
3. **ElevationLevel** — discrete gameplay-facing integer step on `HexTile`. Values `0..N` (default `N = 5`). Derived from the continuous `Elevation` float by quantizing into bands with a smoothing pass that produces contiguous plateaus rather than per-tile noise. The default unit traversal rule is "move only to a tile within ±1 elevation level"; buyer gameplay code may override.

Per-biome feature allow-list as recorded by the user:

| Biome | Features |
|---|---|
| Ocean | (none) |
| Coast | (none) |
| Temperate | Grassland, Forest, Hill, Mountain, Rocky |
| Arid | Savannah, Desert, Hill, Mountain, Rocky |
| Tropical | Grassland, Forest, Hill, Mountain, Rocky (Jungle / Rainforest under consideration for Slice 6) |
| Tundra | Grassland, Forest, Hill, Mountain, Rocky |
| Snow | Snowfield, Forest, Hill, Mountain, Rocky |

Note: `Savannah` is the Arid-biome equivalent of `Grassland` and may be modelled as a renamed flag on the same shared `TileFeatureDefinition` archetype. The exact authoring shape lands when SOs land in Slice 6.

**Alternatives considered:**

- *Single `Biome` axis covering everything (Civ-V-style enum: Grassland, Plains, Desert, Tundra, Snow, Coast, Ocean, Mountain, Hill).* Rejected. Forces "Grassland-in-Tundra" and similar combinations to live in code rather than in content data, and loses the user's deliberate flat-vs-vegetated-vs-modifier distinction.
- *Two axes (Biome + Feature) without discrete elevation levels.* Considered. Continuous-only elevation is simpler but breaks the ±1 traversal rule: tiny continuous gaps between neighbours produce arbitrary movement blockers. Discrete levels make the traversal rule readable, configurable, and easy to communicate to buyer gameplay code.
- *Pre-bake elevation level into Biome (Mountain biome, Hill biome).* Rejected. Confounds climate with terrain shape — a Mountain-Tundra differs gameplay-wise from a Mountain-Tropical and should stay distinguishable.

**Reasoning:**
The user's Civ-style mental model naturally separates climate (Temperate, Arid, etc.) from terrain detail (Forest, Hill) from elevation gameplay rules. Encoding each axis separately keeps the distinctions clean, matches the buyer's vocabulary, and produces a content surface that is straightforward to author. Discrete elevation levels give a clean traversal contract and let Perlin-based generation explicitly target plateau-shaped terrain rather than fighting against the smoothness of raw noise.

**Implementation note:**
- The continuous `float Elevation` (in `[0, 1]`) is the rendering-friendly value and already exists on `HexTile` from Slice 3.
- The discrete `int ElevationLevel` field is added to `HexTile` in Slice 5 or 6 (when the elevation stage is authored) — choice of slice depends on whether the discretization stage is bundled with the noise pass or split into its own. Tracked in `FEATURES.md`.
- `ContinentShapeStage` (Slice 5) operates on raw noise + threshold and writes `LandState`; the discretization / smoothing pass for `ElevationLevel` is a separate stage that runs after `ElevationFieldStage`.
- `BiomeDefinition` SO authoring (Slice 6) carries an allow-list of `TileFeatureDefinition`s, and the feature-placement stage respects it.
- Per ADR-0007, the precise enumeration of biomes and features is verified against live Civilization VII at the time SOs are authored; this ADR commits the v1 starter set to what the user specified.
