# Plan: patch-guided nested dissection ordering for CholeskyAnalysis (opt-in)

- **Board row:** Not yet listed in [OPEN_TOPICS.md](../OPEN_TOPICS.md) (candidate follow-up from SIGGRAPH 2026 survey PR `#1292` and [SparseCholeskyBenchmark/README.md](../../benchmarks/SparseCholeskyBenchmark/README.md#what-is-left-2026-11-18))
- **Kind:** high-ROI performance follow-up (opt-in ordering; changing the default changes floating-point rounding bits of `L` and `Solve()` and therefore requires an owner decision after before/after measurements)
- **Tier:** M/L (deterministic graph/patch partitioning + nested dissection in `solvers/CholeskyAnalysis.cs`, keeping default minimum-degree ordering bit-identical)
- **Plan status:** draft, 2026-11-21 (Berlin). Plan only.

## Current state

- [`CholeskyAnalysis`](../../solvers/CholeskyAnalysis.cs) is shared by both [`SparseCholesky`](../../solvers/SparseCholesky.cs) and [`SparseHermitianCholesky`](../../solvers/SparseHermitianCholesky.cs), powering `CrossField`, `IntegrableFrames` / `QuadRemesher`, Heat Method / Geodesics, Biharmonic Deformation, ARAP, and Harmonic Parameterization.
- SIMD vector kernels (`SparseCholeskyKernels`, `SparseHermitianCholeskyKernels`), parallel solves (`#1136`, `CholeskySolveSchedule`), and parallel numeric factorization (`#1141`, `CholeskyNumericPlan`) are already merged.
- Measured remaining bottlenecks in [benchmarks/SparseCholeskyBenchmark/README.md](../../benchmarks/SparseCholeskyBenchmark/README.md#what-is-left-2026-11-18) (55k–61k vertex systems, 4 cores):
  - **Minimum-degree (AMD) ordering:** `0.22–0.26 s` serial (`16–30%` of total `CrossField` time after parallel factorize/solve).
  - **AMD fill and elimination-tree depth:** root supernode holds `36–56%` of numeric work (`68%` critical path).
  - The repository's own upper-bound estimate already records nested dissection at **`1.8x` fewer numeric flops** and **`49%` vs. `68%` critical path** on those systems.
- SIGGRAPH 2026 *"Fast Sparse Matrix Permutation for Mesh-Based Direct Solvers"* (evaluated in PR `#1292`) shows that patch-guided nested dissection on mesh graphs outperforms general-purpose METIS/AMD in ordering speed while matching or improving fill and tree balance.

## Trigger / decision needed

- **Opt-in phase:** Ready to start as a benchmark spike + opt-in `CholeskyOrdering` API (zero impact on existing golden hashes in `CholeskyAnalysisTests` and `SparseCholeskyBenchmark`).
- **Default switch (separate decision):** Only after measured A/B numbers on `SparseCholeskyBenchmark`, `CrossFieldBenchmark`, `BiharmonicDeformationBenchmark`, and `ParameterizationBenchmark` prove a clear end-to-end win without regressions on small/irregular graphs, and the owner explicitly approves changing factor/solution rounding bits.

## Goal

Add a deterministic, allocation-lean **patch-guided nested dissection** ordering to `CholeskyAnalysis` as an opt-in mode (`CholeskyOrdering.NestedDissection`, keeping `CholeskyOrdering.MinimumDegree` as the bit-identical default), cutting ordering time, `nnz(L)`, numeric flops, and parallel critical-path depth on medium-to-large mesh systems (`N >= 10k`).

## Scope

- `solvers/CholeskyAnalysis.cs` (add `CholeskyOrdering` enum and deterministic nested-dissection / patch-guided separator ordering; keep `MinimumDegree` path bit-identical)
- `solvers/SparseCholesky.cs` and `solvers/SparseHermitianCholesky.cs` (opt-in ordering property/parameter, defaulting to `MinimumDegree`; preserve upstream `1.0.324` API compatibility)
- `tests/geometry3Sharp.Tests/CholeskyNestedDissectionTests.cs` (residual accuracy `||Ax - b|| / ||b||`, bit-for-bit determinism across repeated runs and thread counts, disconnected/isolated/non-manifold/block-size `B = 1, 2, 3` graphs, `MinimumDegree` golden-hash preservation, reuse)
- `benchmarks/SparseCholeskyBenchmark/` (add before/after table comparing `MinimumDegree` vs. `NestedDissection` for ordering ms, `nnz(L)`, factorize ms, solve ms, and `CrossFieldBenchmark` end-to-end time)
- `docs/sources/` (cite paper in sources index; paper-only unless external code license is verified permissive MIT/Apache/BSD)

## Non-goals

- Do **not** change the default `MinimumDegree` ordering or alter any existing golden hash in `CholeskyAnalysisTests` / `SparseCholeskyBenchmark` without an explicit owner decision.
- Do **not** pull in GPL/LGPL partitioning libraries (e.g. Scotch/METIS/ParMETIS code).
- Do **not** modify `QuadRemesher` or `CrossField` defaults in the same PR (keep solver PR isolated from active quad PRs `#1229–#1293`).

## Interface

Additive opt-in API (100% compatible with upstream `1.0.324`):
- `public enum CholeskyOrdering { MinimumDegree = 0, NestedDissection = 1 }` (default `MinimumDegree`)
- `public CholeskyOrdering Ordering { get; set; } = CholeskyOrdering.MinimumDegree;` on `SparseCholesky` and `SparseHermitianCholesky` (applied on `Factorize()`).

## Look before you build

1. Library: `solvers/CholeskyAnalysis.cs`, `solvers/CholeskyNumericPlan.cs`, `solvers/CholeskySolveSchedule.cs`, `solvers/SparseCholesky.cs`, `solvers/SparseHermitianCholesky.cs`, `benchmarks/SparseCholeskyBenchmark/README.md`.
2. Literature: George (1973, Nested Dissection); Karypis & Kumar (1998, multilevel graph partitioning); Amestoy, Davis & Duff (1996, AMD); SIGGRAPH 2026 *"Fast Sparse Matrix Permutation for Mesh-Based Direct Solvers"* (audited in PR `#1292`). Implement from published papers (or verified MIT/Apache/BSD code only; never GPL).

## Steps (one PR, clean commits)

1. **Upper-bound spike:** Prototype deterministic patch-coarsening + multilevel bisection / vertex-separator nested dissection (switching to local minimum-degree on small leaves, e.g. `<= 128` blocks) and measure ordering time, `nnz(L)`, numeric flops, factorize ms, and solve ms on `SparseCholeskyBenchmark` (`grid2d bs=1/2`, `grid3d`, `hermitian grid2d`) and `CrossFieldBenchmark` (`55k–61k` vertices, 1 and 4 cores).
2. **Integrate opt-in API:** Wire `CholeskyOrdering.NestedDissection` into `CholeskyAnalysis`, `SparseCholesky`, and `SparseHermitianCholesky` with strict deterministic tie-breaking (vertex index order only; no hash-iteration order dependence).
3. **Tests & verification:** Add unit/property tests covering `B = 1, 2, 3`, disconnected components, tiny graphs (`N = 1..32`), non-SPD failure parity, bit-identical determinism across thread counts, and golden-hash invariance when `Ordering == MinimumDegree`.

## Acceptance

- `Ordering == MinimumDegree` (default) produces bit-identical `Perm`, `L`, and `Solve()` hashes to master.
- `Ordering == NestedDissection` achieves `||Ax - b||_2 / ||b||_2 <= 1e-12` on all benchmark SPD/HPD systems, is bit-for-bit deterministic across serial and parallel runs, and reduces total `Factorize()` (ordering + numeric) by **>= 25%** and `nnz(L)` / numeric flops by **>= 30%** on `grid2d bs=2 n=120000` and `grid3d n=27000` (with command + before/after table recorded in `benchmarks/SparseCholeskyBenchmark/README.md`).
- Full local CI green (`dotnet pack` `1.0.324` API validation, `net48` build, tests <= 5 s).

## Sources and licences

- George (1973), Karypis & Kumar (1998), Amestoy–Davis–Duff (1996), SIGGRAPH 2026 *"Fast Sparse Matrix Permutation for Mesh-Based Direct Solvers"* — implement from published papers; check license before ever inspecting any companion repository (never GPL).

## Budget and stop rule

1 M/L session. Stop after step 1 if patch-guided nested dissection fails to beat AMD on total `Factorize()` time (ordering + numeric) on `N >= 40k` 2D/3D mesh systems or increases `nnz(L)` on 2D surface meshes; record the measured numbers in the PR and hand off.

## Risks and open questions

- **Small / sparse-irregular graphs:** Nested dissection has higher constant overhead than AMD on small graphs (`N < 2,000`); using AMD on subgraphs below a leaf threshold (e.g. `<= 256` blocks) avoids regression.
- **Bit-exact golden tests:** Switching callers (`CrossField`, `BiharmonicDeformation`, `HeatMethod`) to `NestedDissection` by default will change rounding bits and must be a separate owner decision after this opt-in PR lands.
