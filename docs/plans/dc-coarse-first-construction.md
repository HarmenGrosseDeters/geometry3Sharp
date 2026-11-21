# Plan: dual contouring coarse-first construction (opt-in sparse grid with implicit bounds)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Other: dual contouring coarse-first construction"
- **Kind:** parked (idea, opt-in, not scheduled; trigger: callers needing very fine DC grids N >= 512 where dense uniform sampling becomes the memory/time bottleneck)
- **Tier:** M/L (interval/Lipschitz culling of octree cells + topological/self-intersection check preserved)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `DualContouring` with exact Hermite data (#991), intersection-free repair (#552, #572), and adaptive octree
  simplification (`DualContouring.Simplify`, default on with opt-out; `AdaptiveDualContouring`, #995, #1003) is merged
  ([DC_ADAPTIVE_SIMPLIFICATION.md](../design/DC_ADAPTIVE_SIMPLIFICATION.md), [ROADMAP.md](../ROADMAP.md#later--research)).
- **Measured upper bound** (`DC_ADAPTIVE_SIMPLIFICATION.md` lines 110-117, at `N = 256`, tolerance `0.25`):
  - Grid sampling and edge crossings: **8-15 %** of total time.
  - Vertex QEFs: 36-40 %.
  - Exact self-intersection check (now run once on the 30x-smaller simplified mesh): 17-24 %.
  - Clustering: 26-31 %.
- Therefore a sparse grid alone saves at most **8-15 %** at `N = 256` (below the cost/complexity bar at `N = 256`), and
  **requires sound bounds on the implicit function** (a known Lipschitz constant $L$ so $|f(c)| > L \cdot r_{\text{cell}}$
  certifies no zero crossing in the cell, or an interval evaluator $F([x_0, x_1], [y_0, y_1], [z_0, z_1])$).

## Trigger / decision needed

A concrete workload requiring `N >= 512` or `1024` (where dense $O(N^3)$ sampling and memory dominate — e.g. high-res
TPMS lattices or thin-walled CAD SDFs) where the implicit supplies a Lipschitz bound (which `TpmsLattice` and
`MeshSignedDistanceGrid` / exact distance fields already know!) or an interval evaluator.

## Goal

Provide an opt-in top-down coarse-first octree construction in `DualContouring` that skips empty and flat cells using a
caller-supplied Lipschitz bound or interval evaluator, cutting peak memory from $O(N^3)$ to $O(N^2)$ and accelerating
`N >= 512` runs while preserving the exact self-intersection-free guarantee.

## Scope

- New optional interface / callback (e.g. `IBoundedImplicit3d` / `Func<AxisAlignedBox3d, Interval1d>` or
  `double LipschitzBound` on `DualContouring` / `AdaptiveDualContouring`)
- `mesh_generators/DualContouring.cs` (opt-in top-down sparse octree builder; dense uniform path unchanged when off)
- `mesh_generators/TpmsLattice.cs` (optional wire-up / benchmark consumer — `TpmsLattice` already has exact Lipschitz
  bounds!)
- `tests/geometry3Sharp.Tests/DualContouringTests.cs` (coarse-first vs dense output equivalence on sphere, box, gyroid;
  no missed thin features when Lipschitz bound is valid; self-intersection-free; invariance; determinism; reuse)
- `benchmarks/DualContouringBenchmark/` (time & peak memory at `N = 128, 256, 512`)

## Non-goals

Default dense path (no bound supplied) must stay bit-identical; never prune a cell without a mathematically sound bound
(no heuristic sampling skips that could drop thin components); never break the intersection-free guarantee.

## Interface

Proposed additive opt-in on `DualContouring`:
- `double LipschitzConstant = 0` (`<= 0` = off; when `> 0`, a cell of half-diagonal $r$ with $|f(\text{center})| > L \cdot r$
  is skipped without subdividing)
- `Func<AxisAlignedBox3d, Interval1d>? IntervalBoundsF` (optional tighter interval evaluator; if `0` is outside the
  interval, the cell is skipped)

## Look before you build

1. Library: `mesh_generators/DualContouring.cs`, `docs/design/DC_ADAPTIVE_SIMPLIFICATION.md`,
   `docs/design/CONTOURING_TOPOLOGY.md` (interval arithmetic + Lipschitz culling already studied there!),
   `mesh_generators/TpmsLattice.cs` (Lipschitz bounds), `math/Interval1d.cs`.
2. Literature: Ju, Losasso, Schaefer, Warren (SIGGRAPH 2002, Dual Contouring on octrees); Plantinga & Vegter (2004,
   interval-arithmetic adaptive octree contouring); Keeter (ACM TOG 2020, "Massively Parallel Rendering of Complex
   Closed-Form Implicit Surfaces" — interval pruning of octrees).

## Steps (one PR, clean commits)

1. Measure the dense-sampling share and peak memory at `N = 512` on sphere, box, and gyroid (`TpmsLattice`) to confirm
   the upper bound exceeds 30 % of wall time or > 50 % of peak memory before coding.
2. Implement top-down octree subdivision skipping provably sign-constant subtrees (`|f(c)| > L * r` or `0 ∉ F(box)`),
   feeding active leaf cells into the existing QEF + clustering + exact self-intersection check.
3. Verify bit-identical (or zero-missed-component, same topology, closed/manifold/finite) output vs dense path on
   procedural shapes; test `Similarity.All()`, determinism, reuse, and a mutant that disables the Lipschitz guard (breaks
   on a thin shell).

## Acceptance

- Off-path bit-identical to master (`LipschitzConstant = 0`, `IntervalBoundsF = null`).
- At `N = 256` and `N = 512` (command + before/after table in PR): peak memory reduced by >= 50 %, wall time reduced
  (target >= 25 % at `N = 512`), 0 missed components, 0 self-intersections (`NotCleanReason == None`), closed & manifold.
- All PR tests <= 5 s; full local CI green.

## Sources and licences

Ju et al. (2002), Plantinga & Vegter (2004), Keeter (2020) — papers only; no GPL code.

## Budget and stop rule

1 M session. Stop at step 1 if even at `N = 512` the dense grid + memory stays well under user budgets or if QEF +
clustering still accounts for > 85 % of wall time.

## Risks and open questions

- A caller supplying an **underestimated** Lipschitz constant will silently miss surface pieces: document clearly in
  XML docs that soundness depends on `L >= sup |∇f|` over the domain, and keep it strictly opt-in.
