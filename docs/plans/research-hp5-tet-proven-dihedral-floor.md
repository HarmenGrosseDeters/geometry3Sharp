# Plan: tet meshing — proven dihedral floor on 100 % of inputs (HP 5, P5(b) — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Tet: proven dihedral floor on 100 % of inputs (HP 5, P5(b))"
- **Kind:** research study (498 of 500 sample meshes reach >= 10° at default; trigger: a caller requiring a mathematical 100 % dihedral-angle guarantee on arbitrary inputs)
- **Tier:** L (new lattice-based mesher: Labelle & Shewchuk 2007 Isosurface Stuffing + exact predicate/cut-cell proof; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Hard problem 5 / Stage P5(b) ([TET_MESHING.md §§17.3, 17.6, 17.8, 17.11](../design/TET_MESHING.md),
  [research.md HP 5](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)):
  - Current `MeshTetrahedralization` (alpha wrap at `diag/40` + Delaunay refinement + `TetMeshImprover` with rescue
    phase #1072): **498 of 500** Thingi10K sample meshes have every tet >= 10° (100 % exact positive validity; only
    `38781` at 9.4° and `282598` at 9.5° stay below 10°; 40/40 on the subset reach >= 10°).
  - P5(a) (`Targets.MinDihedralMet` / `RadiusEdgeMet`) already certifies each run's actual angles a posteriori.
  - **Theoretical wall (`TET_MESHING.md` §17.3 & §17.6):** Delaunay refinement proves radius-edge bounds only without
    acute input angles (Shewchuk 1998), and stage 2 is hill climbing — neither can prove a 10° dihedral floor on 100 %
    of arbitrary wrap surfaces. The **only known method with a proven dihedral floor on arbitrary domains** is a
    **lattice / BCC method** (Labelle & Shewchuk, SIGGRAPH 2007, *Isosurface Stuffing*: uniform BCC lattice guarantees
    dihedral angles in **[10.7°, 164.8°]**; graded octree variant also has proven bounds, though weaker), at the cost
    of giving up exact wrap-boundary conformity (the boundary approximates a signed distance / winding-number isosurface).

## Trigger / decision needed

A hard FEM / certification requirement where P5(a)'s per-run check + 99.6 % empirical pass rate (498/500) is not enough
and a **mathematically proven** dihedral lower bound (>= 10°) on 100 % of inputs is required, AND the caller accepts a
grid/isosurface boundary (no exact input-face conformity).

## Goal

Write a rigorous design study for P5(b) (Isosurface Stuffing on BCC / graded octree lattice over our winding-number /
SDF / wrap field), verifying the exact conditions of Labelle & Shewchuk (2007) including floating-point vs exact
warping, boundary fidelity, and tet count vs `MeshTetrahedralization`.

## Scope

- Phase 1 (study only): `docs/design/TET_ISOSURFACE_STUFFING_STUDY.md` (read Labelle & Shewchuk 2007 in full — both
  uniform and graded sections; check exact arithmetic requirements for the warp/snap cut points and the dihedral bound
  proof under finite precision; compare on the 40-mesh Thingi10K subset + `38781` and `282598`).
- Phase 2 (only after owner go): new opt-in class `mesh_ops/LatticeTetMesher.cs` (or mode on `MeshTetrahedralization`),
  `TetMesh3` output, tests in `tests/geometry3Sharp.Tests/LatticeTetMesherTests.cs`.

## Non-goals

Do not replace `MeshTetrahedralization` (P1-P4) as the default mesher; never port GPL code (e.g. Quartet is public
domain/MIT — verify exact license before reading, or implement strictly from the Labelle & Shewchuk 2007 paper!).

## Interface

Phase 1: none. Phase 2 (conditional): additive `LatticeTetMesher` returning `TetMesh3` (compatible with P2 writers:
Medit, Gmsh, VTK, boundary markers, `TetMeshQuality.CheckTargets`).

## Look before you build

1. Library: `TET_MESHING.md` §§17.3, 17.6, `TetMesh3`, `TetMeshQuality` (exact positive orientation + dihedral checks),
   `MeshSignedDistanceGrid`, `DMeshAABBTree3.FastWindingNumber`, `WindingContouring`, `MeshAlphaWrap`.
2. Literature:
   - Labelle & Shewchuk (ACM TOG / SIGGRAPH 2007), "Isosurface Stuffing: Fast Tetrahedral Meshes with Good Dihedral
     Angles" (uniform BCC: 10.7°-164.8°; graded octree: check exact angle theorem and balancing condition).
   - Bridson, Teran, Molino, Fedkiw (2005), "Adaptive physics based tetrahedral mesh generation using level sets".
   - Cheng, Dey, Shewchuk (2012), *Delaunay Mesh Generation* (for comparison with sliver exudation bounds).

## Steps (Phase 1 study, one docs PR)

1. Read Labelle & Shewchuk (2007) in full (specifically the snap-parameter $\alpha_{\text{long}}, \alpha_{\text{short}}$
   proof, the graded octree 2:1 balancing rule, and how floating-point rounding of cut points affects the 10.7° theorem).
2. Analyze which implicit field feeds it cleanly for dirty soups: signed distance to the alpha wrap (exact 1-Lipschitz,
   watertight!) vs `FastWindingNumber - 0.5` (works without wrap, no Lipschitz bound).
3. Prototype uniform BCC stuffing on `38781`, `282598`, and 10 subset meshes (scratch only) to measure worst dihedral,
   tet count, boundary Hausdorff deviation, and wall time vs `MeshTetrahedralization` at `alpha/40`.
4. Deliver `docs/design/TET_ISOSURFACE_STUFFING_STUDY.md` with go/no-go recommendation.

## Acceptance

- Study verifies all theorem conditions of Labelle & Shewchuk (2007) under IEEE 754 `double` (or states where exact
  predicates are needed), and includes a head-to-head table (min/max dihedral, tet count, deviation, time) on `38781`,
  `282598`, and the procedural shapes.

## Sources and licences

Labelle & Shewchuk (2007) paper only (do not port any third-party code without explicit license check and owner go).

## Budget and stop rule

1 L research session for Phase 1. Stop if uniform BCC requires > 10x the tet count of `MeshTetrahedralization` to match
`alpha/40` surface fidelity and graded octree fails to keep a >= 10° proven bound.

## Risks and open questions

- The 10.7° theorem assumes exact real cut points along BCC grid edges; snapping to `double` near a grid vertex needs a
  finite-precision epsilon check so degenerate tets cannot appear in extreme coordinates (`Similarity.All()` / UTM).
