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
