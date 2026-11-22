# Plan: SIGGRAPH 2026 contouring upgrades (SDF Dual Contouring and Subgrid Marching Tetrahedra)

- **Board row:** [OPEN_TOPICS.md](../OPEN_TOPICS.md), row "Other: SIGGRAPH 2026 contouring" (candidate research/implementation items #2 and #3 from SIGGRAPH 2026 survey PR `#1292`; distinct from draft plan PR `#1276` coarse-first sparse octree culling and PR `#1289` contouring topology certificate proof for options B/C)
- **Kind:** research study + prototype (read papers first, check licenses, prototype on procedural thin-feature / sharp-edge SDF benchmarks before committing to public APIs)
- **Tier:** M/L (paper-only study + opt-in contouring algorithms preserving exact self-intersection-free guarantees)
- **Plan status:** draft, 2026-11-21 (Berlin); updated 2026-11-22 (Berlin): the phase 1 study is done ([CONTOURING_SDF_SUBGRID_2026-11.md](../research/CONTOURING_SDF_SUBGRID_2026-11.md), PR `#1305`); its option 1 is built (`TwoLevelMarchingTetrahedra`, `TpmsLattice.TwoLevelSheet`, PR `#1345`, owner go 2026-11-22); option 2 (certificate per level) is in progress: its internal certificate record merged (PR `#1422`), the certificate itself is the next PR. Options 3 to 6 wait on an owner decision.

## Current state

- `DualContouring` (`mesh_generators/DualContouring.cs`) currently requires exact Hermite data (surface points + gradients/normals on sign-changing edges) to recover sharp creases and corners cleanly. When contouring discrete sampled SDF grids (`MeshSignedDistanceGrid`, `MeshOffset`, `CachingMeshSDF`), finite-difference gradients blur across creases and corners.
- Thin features thinner than the grid cell size remain a known limitation of grid-based contouring ([CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md), `TpmsLattice`, `MeshOffset`). Draft plan PR `#1289` tracks the certified topology-certificate proof for thin features, while PR `#1276` tracks coarse-first octree culling for large grids (`N >= 512`).
- Two SIGGRAPH 2026 papers flagged in PR `#1292` directly target these two gaps:
  1. ***Dual Contouring of Signed Distance Data*** (SIGGRAPH 2026) — recovers sharp edges and corners directly from **discrete SDF values on a grid** without requiring analytic gradients or exact Hermite intersection normals.
  2. ***Subgrid Marching Tetrahedra*** (SIGGRAPH 2026) — captures **sub-cell thin sheets and small features** without globally refining the entire 3D grid.

## Trigger / decision needed

- Update 2026-11-22: the paper study and the prototype measurements are done (see the plan status); the two bullets below are kept as the original trigger.
- Read both published papers (once full PDFs / arXiv preprints are accessible) and verify license terms before opening any companion code (never GPL/LGPL or unlicensed code).
- Decide after a short paper study whether the two methods should ship as two separate opt-in PRs (recommended: one for SDF-only sharp-feature QEF in `DualContouring`, one for subgrid thin-feature extraction).

## Goal

1. **SDF-only sharp-feature Dual Contouring:** Enable `DualContouring` to recover sharp creases and corners directly from discrete SDF grids (`MeshSignedDistanceGrid` / `MeshOffset`) without analytic normals, keeping the existing Hermite QEF path bit-identical.
2. **Subgrid Marching Tetrahedra:** Evaluate and (if mathematically sound and compatible with our intersection-free guarantees) implement an opt-in subgrid thin-feature contourer for thin-walled CAD parts, `MeshOffset`, and `TpmsLattice`.

## Scope

- Phase 1 (study): `docs/design/` short study note comparing both papers' assumptions, numerical conditioning, and interaction with `ContouringTopologyCertificate` and `DualContouring`'s intersection-free guarantee.
- Phase 2a (SDF Dual Contouring): `mesh_generators/DualContouring.cs` (opt-in SDF-only sharp-feature solver when `Implicit` provides SDF values without exact normals), `tests/geometry3Sharp.Tests/` (sharp cube / tetrahedron / thin-crease SDF grids, Hausdorff & normal error vs finite-difference DC, self-intersection-free, similarity invariance, determinism, reuse), `benchmarks/DualContouringHermite/`.
- Phase 2b (Subgrid Marching Tetrahedra): new opt-in generator or mode in `mesh_generators/`, `tests/geometry3Sharp.Tests/` (thin plates, thin shells, `TpmsLattice` walls thinner than `h`, closed/manifold/finite/intersection-free), `benchmarks/MarchingCubesBenchmark/` or `benchmarks/ContouringTopology/`.

## Non-goals

- Do **not** alter the default Hermite `DualContouring` or `MarchingCubes` outputs (existing hashes and tests stay bit-identical).
- Do **not** weaken the self-intersection-free or manifoldness guarantees of `DualContouring` / `MeshOffset`.
- Do **not** couple this work with the certified topology-certificate proof tracked in PR `#1289` or coarse-first octree culling in PR `#1276`.

## Interface

Proposed additive opt-ins (finalized after Phase 1 paper study, `1.0.324`-compatible):
- On `DualContouring`: opt-in SDF-only sharp-feature vertex placement mode for discrete `DenseGridTrilinearImplicit` / `MeshSignedDistanceGrid` inputs (default unchanged).
- For Subgrid Marching Tetrahedra: standalone opt-in mesh generator class (e.g. `SubgridMarchingTetrahedra`) implementing the standard `MeshGenerator` pattern so `MarchingCubes` and `DualContouring` remain untouched.

## Look before you build

1. Library: `mesh_generators/DualContouring.cs`, `mesh_generators/MarchingCubes.cs`, `mesh_generators/TpmsLattice.cs`, `mesh_ops/MeshOffset.cs`, `spatial/MeshSignedDistanceGrid.cs`, [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md), [DC_INTERSECTION_FREE.md](../design/DC_INTERSECTION_FREE.md), [DC_ADAPTIVE_SIMPLIFICATION.md](../design/DC_ADAPTIVE_SIMPLIFICATION.md).
2. Literature: *Dual Contouring of Signed Distance Data* (SIGGRAPH 2026); *Subgrid Marching Tetrahedra* (SIGGRAPH 2026); Ju et al. (SIGGRAPH 2002); Schaefer & Warren (2004). Implement from published papers; check licenses before reading any code (never GPL).

## Steps (split into study + up to two clean PRs)

1. **Paper study:** Read both SIGGRAPH 2026 papers, verify preconditions (e.g. whether *Dual Contouring of Signed Distance Data* assumes true Euclidean `|∇f| = 1` SDF values vs general smooth implicits, and how *Subgrid Marching Tetrahedra* handles topological ambiguities / self-intersections), and write a concise design note.
2. **Prototype & measure:** Benchmark SDF-only sharp-feature DC on discrete SDF grids of cubes, beveled CAD brackets, and `MeshOffset` shells (`N = 32, 64, 128`) against current finite-difference `DualContouring` (Hausdorff distance, max normal error, sharp-edge dihedral accuracy, self-intersection count, runtime).
3. **Implement opt-in PR(s):** Ship only what clears the acceptance gates; add invariance, determinism, reuse, and mutant-verified tests.

## Acceptance

- Default `DualContouring` and `MarchingCubes` paths stay bit-identical to master.
- **SDF Dual Contouring:** On discrete SDF grids of sharp-edged solids (`N = 64`), reduces Hausdorff error along creases/corners by **>= 3x** vs finite-difference `DualContouring` while producing **0 self-intersections**, closed/manifold/finite output, and `2^k` scale invariance.
- **Subgrid Marching Tetrahedra:** Recovers thin plates/shells of thickness `0.25h–0.5h` without topological breaks or holes (closed, manifold, finite, 0 self-intersections) at `< 2x` the cost of standard marching tetrahedra at grid resolution `N`.
- Full local CI green (`dotnet pack` `1.0.324` API validation, `net48` build, tests <= 5 s).

## Sources and licences

- *Dual Contouring of Signed Distance Data* (SIGGRAPH 2026) and *Subgrid Marching Tetrahedra* (SIGGRAPH 2026) — paper-only unless companion code is verified MIT/Apache/BSD (never GPL).

## Budget and stop rule

Half-session paper study first. Stop either track immediately if (a) full paper details are not yet published/accessible, (b) the algorithm depends on ML/neural components or an incompatible license, or (c) prototype measurements fail to beat existing `DualContouring` / adaptive octree refinement (`WindingNumberOctree`) on accuracy/cost.

## Risks and open questions

- **Non-Euclidean implicits:** *Dual Contouring of Signed Distance Data* likely relies on the Eikonal property `|∇f| = 1` (true SDFs); for general implicits (like `TpmsLattice` or winding fields), the method may degrade and must stay strictly opt-in for SDF inputs.
- **Manifoldness & intersection-free guarantee:** Any subgrid tetrahedralization must preserve `NotCleanReason == None` and closedness on shared cell faces so `MeshOffset` and `TpmsLattice` do not regress.
