# Plan: Boolean Scaling Hygiene (`4C-0`, `4B-0`) & Density-Gated `UnionMany` / Self-Join Skip (`4A-2`, `4C-1`)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 4)
- **Kind:** Phase 1 (`4C-0`, `4B-0`) → Phase 2 (`4A-2`, conditional `4C-1`); `4A-1` cell-graph DFS is bundled with Phase 3 ([boolean-multimaterial-partition-spike-2b0.md](boolean-multimaterial-partition-spike-2b0.md)); `4B-1` linear coplanar BVH is parked.
- **Tier:** S (`4C-0` measurement, `4B-0` hygiene) + S/M (`4A-2` `SolidBooleanPipeline.UnionMany`) + M (`4C-1` content-stamped self-join skip, gated on `4C-0`)
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- **`#1466` (Dense N-ary Union, [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md)):** Per-patch winding scales as $\approx 0.28 N k^3$ (64% of 4.6 s at 192 dense balls), *and* 95% of the single arrangement's 861k pieces are interior waste. A grouped batch tree (`g ≈ 16` → pairwise tree) runs in ~1.2 s (beats both 1-pass and pairwise tree) with +19–21% triangles and identical volume to `1e-9`, while 1-pass wins 3–4× on sparse inputs (`helix`, `lattice`). Box-overlap degree as the route predictor was not yet measured (`NARY_UNION_DENSE_2026-11.md §9`).
- **`#1630` (Coplanar Region Clipping, [COPLANAR_REGION_CLIPPING.md](../design/COPLANAR_REGION_CLIPPING.md)):** `#1744` (`RegionClipping` default ON, 2026-11-28) already cut 2-ulp ring 481 from 27.5 s → 1.4 s (ring 961 = 5.3 s) and grid boxes to 0.35–0.44×. One small defect remains from `#1744`'s review: `flat_edge` checks `rep[ti] == rep[oi]` only, so equal planes split by `round_key` or overflowing `MaxPlaneCandidates = 8` miss region joins (order-dependent, never unsound). Full linear pair-stage (`4B-1` Region-Proxy BVH) is parked behind a real-workload trigger.
- **`abc-chained` Step 3 ([BOOLEAN_COPLANAR_PREDECISION.md](../perf/BOOLEAN_COPLANAR_PREDECISION.md)):** The 2026-11-17 Geogram comparison (1.1–2.2× slower on step 3) predates `PreDecideCoplanar` (−17%/−27% on step 3), `#1744` (`RegionClipping`), and `#1720` (rounding separating axes). Step 3 has not been re-measured on current `master`.

## Goal

1. **Stage `4C-0` (Mandatory measurement spike — zero code edits):** Re-run `abc-chained` on current `master` (post-`#1744`, post-`#1720`) with per-phase timing to measure whether any step-3 gap to Geogram/CGAL remains and whether `4C-1` clears the $\ge 15\%$ bar.
2. **Stage `4B-0` (Tier S hygiene):** Close `#1744`'s `MaxPlaneCandidates = 8` / `round_key` bucket-overflow gap in `ExactMeshArrangementRegions.flat_edge` via a lazy `same_plane` fallback so `RegionOf` is strictly operand-order independent.
3. **Stage `4A-2-0` → `4A-2` (Phase 2 — Tier S/M):** Measure box-overlap degree predictor across dense and sparse families, then add explicit `SolidBooleanPipeline.UnionMany` (never a silent reroute inside `ExactMeshBoolean`!) that stays single-pass on sparse/chain inputs and runs spatially grouped `g ≈ 16` batched unions on dense packings.
4. **Stage `4C-1` (Phase 2 — Conditional, gated on `4C-0`):** Only if `4C-0` confirms self-join takes $\ge 15\%$ of chained/batched wall time, add tamper-proof content-stamped clean-mesh self-join skip (EMBER lever 2; see [boolean-ember-lever-2.md](boolean-ember-lever-2.md)).

## Scope

- **`4C-0`:** Runner benchmark only (`benchmarks/BooleanComparison` `chain-ab` on ABC chunk 0000); record numbers in `docs/perf/`.
- **`4B-0`:** `mesh_ops/ExactMeshArrangementRegions.cs` (`flat_edge`), `tests/geometry3Sharp.Tests/ExactCoplanarRegionTests.cs`.
- **`4A-2-0` + `4A-2`:**
  - Mini-probe in `benchmarks/ProceduralSphereUnionProbe/` measuring `box_overlap_degree` across `dense104`, `dense103`, `helix`, `lattice`, and non-sphere packings (random overlapping boxes & cylinders).
  - `mesh_ops/SolidBooleanPipeline.cs` (`UnionMany` / `BatchUnionMode`, `RouteStep` recording `"BatchUnion"` with batch size and degree, deterministic Morton spatial ordering).
  - `tests/geometry3Sharp.Tests/SolidBooleanUnionManyTests.cs`.
- **`4C-1` (conditional on `4C-0`):** `mesh_ops/ExactMeshArrangement.cs`, `mesh_ops/ExactMeshBoolean.cs`, `mesh_ops/SolidBooleanPipeline.cs` (content/timestamp-verified clean-output stamp invalidated on any `DMesh3` mutation).

## Non-goals

- Do **not** silently reroute `ExactMeshBoolean.Union` to a pairwise/grouped tree (keep `ExactMeshBoolean` strictly single-arrangement exact).
- Do **not** build `4B-1` (Region-Proxy 3D BVH + 2D boundary-edge pairing) unless a real corpus/CAD benchmark hits coplanar `intersect > 50%` and `> 10 s`.
- Do **not** start `4C-1` before `4C-0` produces post-`#1744` numbers.

## Interface

- **`4B-0`:** No public API change.
- **`4A-2` (on `SolidBooleanPipeline`, NOT `ExactMeshBoolean`):**
  - `public enum BatchUnionModes { Off = 0, Auto = 1, ForceBatched = 2 }`
  - `public BatchUnionModes BatchUnion = BatchUnionModes.Off;` (default `Off` preserves bit-identical behavior; `Auto` uses single-pass when mean `box_overlap_degree <= threshold` and `g ≈ 16` Morton-ordered batches + balanced tree above threshold, documenting `+19–21%` triangles and multi-pass rounding in XML docs and `Report.Route`).

## Look before you build

1. Library: `mesh_ops/ExactMeshArrangementRegions.cs` (`flat_edge`, `MaxPlaneCandidates = 8`), `mesh_ops/SolidBooleanPipeline.cs`, `benchmarks/ProceduralSphereUnionProbe/`, `benchmarks/BooleanComparison/`.
2. Prior research: [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md), [COPLANAR_REGION_CLIPPING.md](../design/COPLANAR_REGION_CLIPPING.md), [BOOLEAN_COPLANAR_PREDECISION.md](../perf/BOOLEAN_COPLANAR_PREDECISION.md), [EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](../research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md), [boolean-ember-lever-2.md](boolean-ember-lever-2.md).

## Steps (separate clean PRs per stage)

1. **PR 1 (`4C-0` + `4B-0`):**
   - Run `BooleanComparison chain-ab` on current `master` (4 reps, ABBA) and publish post-`#1744` baseline table.
   - Add lazy `same_plane(T.P, O.P)` fallback in `ExactMeshArrangementRegions.flat_edge` (guarded by cheap double normal-alignment prefilter so non-coplanar edges pay zero `BigInteger` cost) + regression test pinning >8 distinct planes in one bucket and `round_key` boundary splits (verify identical `RegionOf` regardless of triangle order).
2. **PR 2 (`4A-2-0` → `4A-2`):**
   - Run `4A-2-0` mini-probe to pick and validate the `box_overlap_degree` threshold across spheres, boxes, cylinders, `helix`, and `lattice`.
   - Implement `SolidBooleanPipeline.BatchUnion` (deterministic Morton sort + `g = 16` batches + balanced reduction tree + `Report.Route` transparency) with full invariance, determinism, reuse, and volume-agreement (`< 1e-9`) tests.
3. **PR 3 (`4C-1`, only if `4C-0` passes gate):**
   - Implement content-stamped clean-mesh self-join skip (reusing [boolean-ember-lever-2.md](boolean-ember-lever-2.md)) with bit-identical verification and mutation tests ensuring any edit to a stamped mesh invalidates the stamp.

## Acceptance

- **`4C-0`:** Published per-phase table on current `master` with exact command and raw output; explicit go/no-go recorded for `4C-1`.
- **`4B-0`:** `RegionOf` is invariant under arbitrary input triangle permutations even with >8 planes per rounded bucket; zero slowdown on non-coplanar spheres (`benchmarks/CoplanarRegionClipping`).
- **`4A-2`:** `BatchUnion = Auto` achieves $\ge 2.5\times$ speedup on 192-ball `dense104` (beating or matching pairwise tree), $0\%$ regression on `helix` 192 and `lattice` 216 (stays single-pass), closed/clean output with volume matching single-pass to `< 1e-9`, deterministic across thread counts.
- **`4C-1` (if built):** $\ge 15\%$ speedup on chained/batched steps, bit-identical hashes vs. off, zero unsound skip on mutated meshes.

## Sources and licences

Internal exact arrangement & pipeline; Manifold `BatchBoolean` and EMBER lever 2 cited conceptually from public docs/papers only (no code ports).

## Budget and stop rule

- `4C-0`: 1 runner run (1–2 h). Stop `4C-1` if post-`#1744` self-join share is `< 15%` on `abc-chained` and batched unions.
- `4B-0`: 0.5 day (Tier S).
- `4A-2-0` + `4A-2`: 1 M session. Stop `Auto` routing if no single `box_overlap_degree` threshold avoids >10% regression on sparse families (expose explicit `ForceBatched` only in that case).
