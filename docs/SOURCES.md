# Sources

`geometry3Sharp` is under the **Boost Software License 1.0**. New code in this fork is written from published papers; no code is taken over from other libraries unless it is under a compatible permissive license (Boost, MIT, BSD, Apache-2.0) and named here. Code under the GPL or LGPL (e.g., CGAL) is never used as a source.

To avoid merge conflicts across parallel PRs, sources are organized by domain under [docs/sources/](sources/README.md). Add new citations to the matching domain file below.

## Domain Catalogs

| Domain | File |
|---|---|
| Exact arithmetic, booleans, rounding & `CoreMath` | [sources/exact-arithmetic-and-booleans.md](sources/exact-arithmetic-and-booleans.md) |
| Meshing (2D, tet, quad), alpha wrap, contouring, offset, TPMS, remeshing & deformation | [sources/meshing-and-repair.md](sources/meshing-and-repair.md) |
| Geodesics, intrinsic triangulations, UV parameterization, atlases & `SparseCholesky` | [sources/geodesics-and-parameterization.md](sources/geodesics-and-parameterization.md) |
| Terrain, DGM/TIN, point clouds, 2D quadtrees, Clipper2, analysis, metrics, batch queries & CCD | [sources/terrain-analysis-and-spatial.md](sources/terrain-analysis-and-spatial.md) |
| Tools, analyzers, benchmarks, coverage, Z3/FPTaylor pilots, fuzzing & metamorphic testing | [sources/tools-and-verification.md](sources/tools-and-verification.md) |

---

## Historical meshing recheck tools and data

Moved to [sources/meshing-and-repair.md#historical-meshing-recheck-tools-and-data](sources/meshing-and-repair.md#historical-meshing-recheck-tools-and-data).
