# Literature & Third-Party Sources

`geometry3Sharp` is licensed under the **Boost Software License 1.0**.

## License Policy

- New algorithms are implemented from published academic papers and public specifications.
- Third-party code is ported or vendored **only** under compatible permissive licenses (Boost, MIT, BSD, Apache-2.0) and recorded in the domain catalogs below.
- Code under GPL or LGPL (e.g., CGAL, GPL parts of TetWild) and clean-room restricted code (Triangle / Triangle.NET, proprietary code) is **never** read or used as a source.

## Domain Catalogs

| File | Coverage |
|---|---|
| [exact-arithmetic-and-booleans.md](exact-arithmetic-and-booleans.md) | Exact booleans, exact predicates, SafeRounding, deterministic parallel building, decimal parsing/formatting, `CoreMath` |
| [meshing-and-repair.md](meshing-and-repair.md) | Alpha wrap, tetrahedral meshing, quad remeshing (`DPolyMesh`), 2D meshing & historical rechecks, dual contouring, offset, TPMS, remesher safeguards, ARAP & biharmonic deformation |
| [geodesics-and-parameterization.md](geodesics-and-parameterization.md) | Exact & polyhedral geodesics, intrinsic triangulations, vector heat, UV parameterization, fixed-boundary untangling, seams/atlas packing, `SparseCholesky`, preconditioner prototypes of `MultiscaleCholeskyProbe` |
| [terrain-analysis-and-spatial.md](terrain-analysis-and-spatial.md) | 2D loose quadtrees, minimal enclosing circle, analysis (`WallThickness`, `Deviation`, `PrimitiveFit3`), Hausdorff, fidelity metrics, terrain/slope/DGM, point clouds, Clipper2, batch queries, CCD |
| [tools-and-verification.md](tools-and-verification.md) | Test harness, build-time analyzers, benchmarks, solid topology study, coverage gate, Z3 SMT & FPTaylor pilots, fuzzing, metamorphic testing, cross-field research, forks |
