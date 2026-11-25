# Plan: RDR stage 1b — true restricted Voronoi cells (Geogram measurement follow-up)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Geodesics/param: RDR stage 1b (restricted Voronoi cells)"
- **Kind:** parked ("not now", owner 2026-11-17; trigger: owner go when higher-quality isotropic/anisotropic surface remeshing or CVT is prioritized)
- **Tier:** M/L (new 3D restricted Voronoi clipping on surface meshes; exact/filtered plane clipping; optional port of ideas from Geogram BSD-3 with A/B)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Measured 2026-11-17 in [GEOGRAM_MEASUREMENTS_2026-11.md](../research/GEOGRAM_MEASUREMENTS_2026-11.md) (after
  [GEOGRAM_SURVEY_2026-11.md](../research/GEOGRAM_SURVEY_2026-11.md);
  [sources-surveys.md](../roadmap/sources-surveys.md#open-for-the-future-2026-11-17)):
  - Out of the top 3 Geogram measurements:
    1. Spectral conformal parameterization -> built as opt-in (#1057).
    2. Local feature size (lfs) yardstick -> metric only, not a bound.
    3. **True restricted Voronoi cells (RVD)** -> technical **go** for RDR stage 1b, **parked by owner (2026-11-17: "RDR
       stage 1b not now")**.
- Related literature already read in `sources-surveys.md`: normal-anisotropy RVD remeshing (arXiv 2404.15661, Lévy &
  Liu 2010 LpCVT), Geogram `voronoi/` (BSD-3, may be read and ported per owner 2026-11-03 with an A/B decision and
  license notice in `docs/SOURCES.md`).

## Trigger / decision needed

Owner go to build RVD-based remeshing / CVT (e.g. a caller wanting feature-preserving CVT remeshing with higher triangle
quality than edge-flip `Remesher`, or needing restricted Voronoi cells for surface sampling / dual extraction).

## Goal

Implement exact/robust restricted Voronoi diagram (RVD) cell clipping on a surface `DMesh3` (clipping input triangles
against bisector planes of a 3D Delaunay / k-NN site set) as stage 1b of RDR, enabling CVT / LpCVT surface remeshing
with a measured A/B against `Remesher`.

## Scope

- New `mesh_ops/RestrictedVoronoiDiagram.cs` (and optional CVT driver `mesh_ops/CvtRemesher.cs`)
- Reuse `comp_geom/Delaunay3.cs` (or `DMeshAABBTree3` / k-NN) + exact/filtered plane clipping (`math/AdaptivePredicates.cs`
  / `Plane3d`)
- `tests/geometry3Sharp.Tests/RestrictedVoronoiDiagramTests.cs`
- `benchmarks/RemesherBenchmark/` (A/B quality, Hausdorff deviation, min angle, wall time vs `Remesher` and Geogram)
- `docs/sources/meshing-and-repair.md` (add BSD-3 notice if any Geogram RVD code is ported; otherwise cite papers only)

## Non-goals

No change to existing `Remesher` or `RemesherPro` defaults; never port GPL code; never commit mesh files.

## Interface

Proposed additive API:
- `RestrictedVoronoiDiagram`: inputs `(DMesh3 surface, IReadOnlyList<Vector3d> sites)`, `Compute()` -> status + per-site
  clipped surface polygons (area, centroid, integrated normal, dual connectivity for remesh extraction).
- Options object + deterministic serial/parallel execution with `MaxThreads`.

## Look before you build

1. Library: `docs/research/GEOGRAM_MEASUREMENTS_2026-11.md`, `docs/research/GEOGRAM_SURVEY_2026-11.md`,
   `comp_geom/Delaunay3.cs`, `mesh_ops/Remesher.cs`, `mesh_ops/RemesherPro.cs`, `mesh_ops/MeshHausdorffDistance.cs`.
2. Literature & permissive sources:
   - Yan, Lévy, Liu, Sun, Wang (ACM TOG 2009, "Isotropic Remeshing with Fast and Exact Computation of Restricted Voronoi
     Diagrams").
   - Lévy & Liu (ACM TOG 2010, "Lp Centroidal Voronoi Tessellation and its Applications").
   - Geogram `src/lib/geogram/voronoi/` (BSD-3 — check per-file headers before reading; keep notice if ported).

## Steps (one PR, clean commits)

1. Re-read `GEOGRAM_MEASUREMENTS_2026-11.md` to confirm the exact scope and measured baseline of stage 1b.
2. Implement symbolic/filtered triangle-by-bisector clipping (Sutherland-Hodgman with exact tie-breaking or security
   radius from Yan et al. 2009) and verify partition-of-unity: sum of clipped cell areas == input surface area to ~1e-12.
3. Extract dual mesh and wire optional Lloyd/L-BFGS CVT iterations; run A/B against `Remesher` (safe preset) on
   procedural CAD + organic shapes and a Thingi10K subset.
4. Add tests: area conservation (partition of unity), topological validity of dual (closed, manifold, finite, no
   self-intersections on smooth closed shapes), invariance (`Similarity.All()`), determinism, reuse, hand mutants.

## Acceptance

- Partition-of-unity test: `|sum(cell areas) - surface area| / surface area < 1e-10` across procedural shapes and
  rotated/scaled frames.
- Dual extraction produces closed, manifold, finite meshes with measurably higher min-angle / triangle quality than
  `Remesher` at equal vertex count (before/after A/B table + command in PR).
- Every PR test <= 5 s; full local CI + independent review green.

## Sources and licences

Yan et al. (2009), Lévy & Liu (2010) papers; Geogram (BSD-3, verify per-file headers and record in `docs/SOURCES.md`
if ported).

## Budget and stop rule

1 M/L session. Stop if RVD clipping cannot handle thin sheets cleanly without the normal-bias reassignment of arXiv
2404.15661 or if clipping + Lloyd takes > 5x `Remesher` safe preset without a clear quality win.

## Risks and open questions

- Thin plates / close opposite sheets can cause a site on one side to steal Voronoi area on the opposite side (needs
  connected-component or normal-compatibility restriction during RVD traversal).
