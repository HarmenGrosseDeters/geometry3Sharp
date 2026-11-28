# Plan: Boolean Scaling Hygiene (`4B-0`) & Density-Gated `UnionMany` (`4A-2`); `4C-1` Dropped

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 4)
- **Kind:** Phase 1 (`4B-0`) → Phase 2 (`4A-2`); Spikes `4C-0` & `4A-2-0` **completed 2026-11-28**, **`4C-1` dropped (NO-GO)**; `4A-1` cell-graph DFS is bundled with Phase 3 ([boolean-multimaterial-partition-spike-2b0.md](boolean-multimaterial-partition-spike-2b0.md)); `4B-1` linear coplanar BVH is parked.
- **Tier:** S/L (`4B-0` is a small hygiene change, but touches `mesh_ops/ExactMeshArrangementRegions.cs` under `mesh_ops/ExactMesh*`, so it triggers the incremental mutation gate and exact-code review per `AGENTS.md`) + S/M (`4A-2` `SolidBooleanPipeline.UnionMany`)
- **Plan status:** spike-verified, ready for implementation (`4B-0`, `4A-2`), 2026-11-28 (Berlin). See [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md).

## Current state (Post-Spike `4C-0` & `4A-2-0`)

- **Spike `4C-0` (`abc-chained` post-`#1744`, Actions run `46138598811`, `chunk=0000`, `chains_per_variant=12`, `threads=all`):**
  - Clean chains: `ours` **32/36** all-steps-clean (0 crashes, 0 failures; `77/77` volumes agree with Geogram within `3.4e-11`) vs. Geogram **3/36** (21 crashes) vs. CGAL EPECK **1/36** (27 degenerate, 11 rejected, 6 failed).
  - Speed across all 77 shared steps: `ours / geogram` median **`0.49×`** (Step 1 `0.36×`, Step 2 `0.66×`, Step 3 median `1.58×` yet total wall time `18,865 ms` vs `28,641 ms = 0.66×`).
  - Phase breakdown across all 35 Step-3 chains (`69,225 ms`): `rounding` `33.2%` (dominated by `iud` sub-ulp sliver cleanup where Geogram/CGAL crash), `intersections.intersect` `18.7%` (cross-mesh coplanar pairs; `31.1%` on Step 2), `classification` `15.4%` (directly helped by `4A-1`), `triangulation` `7.9%`, `merge` `7.6%`, `intersections.planes` `7.8%`, while **`intersections.pairs` (cross + self-join combined) is only `2.9%` on Step 3 (`4.6%` on Step 2, `7.8%` on Step 1)**.
  - **Verdict: `4C-1` (content-stamped self-join skip) is NO-GO** — fails its `>= 15%` stop rule by >5×.
- **Spike `4A-2-0` (`UnionMany` density predictor, uncommitted local scratch probe on `master` `2959dfaa`, 24 procedural cases in 8 families):**
  - `batch16` (Morton-ordered groups of 16 + balanced tree) is **3.3×–11.0× faster** on dense packings (`6.4×–11.0×` on the three largest: `dense104_192`: `20,081 → 3,115 ms = 0.16×`; `dense_cyls_96`: `13,818 → 1,696 ms = 0.12×`; `dense_boxes_144`: `30,364 → 2,758 ms = 0.09×`), 100% clean, volume error `< 1e-10`.
  - **Adversarial bug caught:** Naive `aabb_deg` fails on cubic `lattice_216` (`aabb_deg = 18.0`, yet batching is `1.81×` slower); BOTH `aabb_deg` and 4×4×4 `subbox_deg` fail on thin diagonal rods `diag_rods_144` (`aabb_deg = subbox_deg = 140.0`, yet batching is `2.05×` slower!).
  - **Verified fix (`Sphere ∧ PCA-OBB`):** Tight bounding sphere around `ContOrientedBox3.Box.Center` + 15-axis SAT on `ContOrientedBox3` (`containment/ContBox3.cs`) achieves **24 / 24 perfect separation** (`max sparse = 6.0` on `dense104_12`, `lattice_216 = 5.0`, `diag_rods_144 = 5.1`, `helix = 2.8`; vs. `min dense = 11.6` on `dense_boxes_24`, `dense103_48 = 12.4`, `dense104_48 = 14.2`, `dense104_192 = 33.9`), costing only `1–38 ms`.
  - **Verdict: `4A-2` is GO** with locked routing rule `N >= 24 && meanDegree(Sphere ∧ PCA-OBB) >= 11.0`.
- **`#1630` (`4B-0`):** `#1744` (`RegionClipping` default ON) left one small review defect: `ExactMeshArrangementRegions.flat_edge` checks `rep[ti] == rep[oi]` only, so equal planes split by `round_key` or overflowing `MaxPlaneCandidates = 8` (both in `mesh_ops/ExactMeshArrangement.cs`) miss region joins (order-dependent, never unsound). Full linear pair-stage (`4B-1`) remains parked.

## Goal

1. **Stage `4B-0` (small hygiene, exact-code review + incremental mutation gate):** Close `#1744`'s `MaxPlaneCandidates = 8` / `round_key` bucket-overflow gap in `ExactMeshArrangementRegions.flat_edge` via a lazy `same_plane` fallback so `RegionOf` is strictly operand-order independent.
2. **Stage `4A-2` (Phase 2 — Tier S/M, Spike `4A-2-0` passed):** Add explicit `SolidBooleanPipeline.UnionMany` / `BatchUnion` (never a silent reroute inside `ExactMeshBoolean`) using the spike-verified `N >= 24 && meanDegree(Sphere ∧ PCA-OBB) >= 11.0` predictor (`1–38 ms`) to stay single-pass on sparse/chain/diagonal-rod inputs and run Morton-ordered `g = 16` batched unions + balanced reduction tree on dense packings.
3. ~~Stage `4C-1`~~ — **Dropped (`NO-GO`)** after Spike `4C-0` measured `intersections.pairs` (cross + self combined) at `2.9%` on Step 3 (`4.6%` on Step 2), failing the `>= 15%` stop rule.

## Scope

- **`4B-0`:** `mesh_ops/ExactMeshArrangementRegions.cs` (`flat_edge`), `tests/geometry3Sharp.Tests/ExactCoplanarRegionTests.cs`.
- **`4A-2`:**
  - `mesh_ops/SolidBooleanPipeline.cs` (`BatchUnionModes`, `BatchUnion`, `Sphere ∧ PCA-OBB` degree predictor via `ContOrientedBox3` + 15-axis SAT, deterministic Morton spatial ordering, `g = 16` batches + balanced tree, `RouteStep` recording `"BatchUnion"` with batch size and mean degree).
  - `tests/geometry3Sharp.Tests/SolidBooleanUnionManyTests.cs` (including adversarial `lattice_216` and `diag_rods_144` sparse regressions!).

## Non-goals

- Do **not** silently reroute `ExactMeshBoolean.Union` to a pairwise/grouped tree (keep `ExactMeshBoolean` strictly single-arrangement exact).
- Do **not** build `4B-1` (Region-Proxy 3D BVH + 2D boundary-edge pairing) unless a real corpus/CAD benchmark hits coplanar `intersect > 50%` and `> 10 s`.
- Do **not** build `4C-1` (content-stamped self-join skip) — stopped by Spike `4C-0`.

## Interface

- **`4B-0`:** No public API change.
- **`4A-2` (on `SolidBooleanPipeline`, NOT `ExactMeshBoolean`):**
  - `public enum BatchUnionModes { Off = 0, Auto = 1, ForceBatched = 2 }`
  - `public BatchUnionModes BatchUnion = BatchUnionModes.Off;` (default `Off` preserves bit-identical behavior; `Auto` stays single-pass unless `N >= 24 && meanDegree(Sphere ∧ PCA-OBB) >= 11.0`, otherwise runs Morton-ordered `g = 16` batches + balanced tree, documenting multi-pass rounding and triangle count delta in XML docs and `Report.Route`).

## Look before you build

1. Library: `mesh_ops/ExactMeshArrangementRegions.cs` (`flat_edge`), `mesh_ops/ExactMeshArrangement.cs` (`MaxPlaneCandidates = 8`, `round_key`), `mesh_ops/SolidBooleanPipeline.cs`, `containment/ContBox3.cs` (`ContOrientedBox3`).
2. Spike results & prior research: [EXACT_BOOLEAN_SPIKES_2026-11.md](../research/EXACT_BOOLEAN_SPIKES_2026-11.md), [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md), [COPLANAR_REGION_CLIPPING.md](../design/COPLANAR_REGION_CLIPPING.md), [BOOLEAN_COPLANAR_PREDECISION.md](../perf/BOOLEAN_COPLANAR_PREDECISION.md).

## Steps (separate clean PRs per stage)

1. **PR 1 (`4B-0`):**
   - Add lazy `same_plane(T.P, O.P)` fallback in `ExactMeshArrangementRegions.flat_edge` (guarded by cheap double normal-alignment prefilter so non-coplanar edges pay zero `BigInteger` cost) + regression test pinning >8 distinct planes in one bucket and `round_key` boundary splits (verify identical `RegionOf` regardless of triangle order).
2. **PR 2 (`4A-2`):**
   - Implement `SolidBooleanPipeline.BatchUnion` using the spike-verified `Sphere ∧ PCA-OBB` degree predictor (`N >= 24 && meanDegree >= 11.0`), deterministic Morton sort + `g = 16` batches + balanced reduction tree + `Report.Route` transparency.
   - Add invariance, determinism, reuse, adversarial sparse regression (`helix`, `lattice_216`, `diag_rods_144` must stay single-pass!), and volume-agreement (`< 1e-9`) tests.

## Acceptance

- **`4B-0`:** `RegionOf` is invariant under arbitrary input triangle permutations even with >8 planes per rounded bucket; zero slowdown on non-coplanar spheres (`benchmarks/CoplanarRegionClipping`).
- **`4A-2`:** `BatchUnion = Auto` achieves $\ge 4\times$ speedup on 192-ball `dense104` and dense boxes/cylinders, $0\%$ regression on `helix` 192, `lattice` 216, and `diag_rods_144` (all stay single-pass via `Sphere ∧ PCA-OBB`), closed/clean output with volume matching single-pass to `< 1e-9`, deterministic across thread counts.

## Sources and licences

Internal exact arrangement, `ContOrientedBox3`, & pipeline; Manifold `BatchBoolean` cited conceptually from public docs only (no code ports).

## Budget and stop rule

- `4B-0`: 0.5 day (Tier S/L — small hygiene change touching `mesh_ops/ExactMesh*`, exact-code review + incremental mutation gate).
- `4A-2`: 1 M session.
- `4C-1`: **Stopped by Spike `4C-0`** (`intersections.pairs` = 2.9% on Step 3 < 15% bar).
