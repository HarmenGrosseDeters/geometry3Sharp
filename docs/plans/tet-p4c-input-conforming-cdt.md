# Plan: Tet P4(c) — input-conforming CDT for clean inputs (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Tet: P4(c), input-conforming CDT for clean inputs"
- **Kind:** owner decision
- **Tier:** L (3-4 months estimated in TET_MESHING.md §17.3; exact arithmetic, 3D CDT recovery, bounds review, mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- P4(a) (`MaxSurfaceDeviation`, local alpha, [TET_MESHING.md §17.13](../design/TET_MESHING.md)) is merged: achieves the
  surface fidelity of uniform alpha/80 at about half the tets while keeping all wrap guarantees.
- P4(b) (`CreaseAngleDegrees`, S1-F crease recovery via `TetFeatureSurface`, §17.14, #1196) is built: recovers 15/15
  procedural CAD solids (concave creases 2.9-5.6x closer, 1.5-4.0 offsets, 1.2-2.2x time) and 19/24 Thingi10K meshes,
  falling back cleanly to S1-R when recovery does not finish.
- P4(c) (option (b) of §4: a true 3D Constrained Delaunay Tetrahedralization conforming exactly to clean manifold
  input surfaces, zero offset) is still open for an owner decision now that P4(a) and P4(b) are measured.
- Cost & risk estimate from §17.3: **3-4 months, high risk** (exact 3D segment/facet recovery, Schönhardt polyhedron
  Steiner insertion, cavity retetrahedralization, acute-angle termination, exact arithmetic + full mutation shards).

## Trigger / decision needed

Owner chooses whether to park/drop P4(c) now that P4(a) and P4(b) exist, or invest in a separate 3D CDT module:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Park / not now (leaning)** | Keep wrap + P4(a) local alpha + P4(b) creases as the tet mesher; revisit P4(c) only when a caller explicitly requires zero-offset exact boundary conformity (e.g. multi-material FEM assemblies with shared interfaces) | Zero cost now; CAD inputs keep a small offset (~diag/600) and rounded acute/3-plane corners |
| B. **Stage 0 design study first (1-2 weeks, no code)** | Survey exact 3D CDT algorithms (Si & Gärtner 2011, Diazzi et al. 2023 paper only, Shewchuk 2002) and check reuse of our `Delaunay3` + `ExactMeshArrangement` before committing 3-4 months | 1 L session for design doc; avoids committing blind |
| C. **Build P4(c) now as an opt-in path for clean closed manifold inputs** | Zero-offset boundary-conforming tet mesh on clean inputs (e.g. exact-boolean outputs), with fallback to wrap on dirty inputs | 3-4 months, tier L (exact arithmetic, new mutation shards, large review surface) |

How others solve it: TetGen (Si, ACM TOMS 2015, AGPL — never read code) uses 3D CDT + Shewchuk/Si facet recovery with
Steiner points; Diazzi, Panozzo, Vaxman, Attene (ACM TOG 2023, "Constrained Delaunay Tetrahedrizations: A Robust and
Practical Approach", GPL/LGPL reference code — **paper only, never read code**) computes exact 3D CDTs using indirect
predicates (very close to our exact kernel); fTetWild (Hu et al. 2020, MPL-2.0 — paper only) avoids exact conformity
altogether via an envelope; CGAL `Mesh_3` (GPL — paper only) uses restricted Delaunay with 1D protecting balls rather
than exact input CDT.

## Goal

Decide whether exact input-conforming 3D CDT (P4(c)) is parked or starts with a Stage 0 design study based on Diazzi et
al. (2023) and Si & Gärtner (2011).

## Scope

- Option A: `docs/OPEN_TOPICS.md`, `docs/design/TET_MESHING.md`, `docs/ROADMAP.md` (move to §4 Parked).
- Option B: new `docs/design/TET_CDT_DESIGN.md` (stage 0 study, architecture, predicate list, benchmark plan vs
  `MeshTetrahedralization` + fTetWild; no production code).
- Option C (only after B is approved by owner): new `mesh_ops/ConstrainedDelaunay3.cs`, exact-predicate additions if
  needed (`tools/PredicateCompiler`), `tests/geometry3Sharp.Tests/ConstrainedDelaunay3Tests.cs`, mutation shard config.

## Non-goals

Never read or port TetGen (AGPL) or CGAL (GPL) code. Never replace the alpha-wrap path for dirty/open inputs.

## Interface

Option B/C only: opt-in mode on `MeshTetrahedralization` (e.g. `BoundaryMode = AlphaWrap | ConformingCDT` with explicit
precondition check `NotCleanReason` and status `InputNotClean` or opt-in fallback).

## Look before you build

1. Library: `comp_geom/Delaunay3.cs`, `mesh_ops/ExactMeshArrangement.cs`, `math/AdaptivePredicates.cs`,
   `math/FixedPoint3.cs`, `tools/PredicateCompiler`, `mesh_ops/TetFeatureSurface.cs` (P4(b) conforming flips).
2. Literature (papers only): Diazzi, Panozzo, Vaxman, Attene (ACM TOG 2023, GPL/LGPL code — paper only); Si & Gärtner
   (2011, "3D boundary recovery by constrained Delaunay tetrahedralization", IJNME); Shewchuk (2002, "Constrained
   Delaunay Tetrahedralizations and Provably Good Boundary Recovery", IMR).

## Steps (if B is chosen)

1. Read Diazzi et al. (2023) paper only (never open the GPL/LGPL reference code) and map its exact predicate
   requirements against our `math/Exact*`.
2. Map input preconditions (clean closed manifold, exact-boolean output) and missing predicates / symbolic perturbation.
3. Produce `docs/design/TET_CDT_DESIGN.md` with milestones, oracle acceptance strategy, and go/no-go gate for the owner.

## Acceptance

- Option A: docs diff reviewed.
- Option B: design doc reviewed by owner with explicit predicate count, termination proof sketch, and effort estimate.
- Option C (future): 100 % exact input-face conformity + positive exact tet volumes on clean Thingi10K / procedural
  CAD suite; invariance, determinism, reuse; incremental mutation gate green.

## Sources and licences

Diazzi et al. (2023), Si & Gärtner (2011), Shewchuk (2002) — **papers only**. Never read or port Diazzi CDT
(GPL/LGPL), TetGen (AGPL), or CGAL (GPL) code.

## Budget and stop rule

Option A: 5 min. Option B: 1 L session (design doc only). Stop if predicate count / Schönhardt recovery complexity
exceeds the 3-4 month budget or duplicates P4(a)+P4(b) for all known users.

## Risks and open questions

- Clean-input precondition: most Thingi10K meshes are dirty, so P4(c) serves CAD / post-exact-boolean meshes specifically.
- Mutation cost: new exact-code files will require new Stryker shards (coordinator approval needed).
