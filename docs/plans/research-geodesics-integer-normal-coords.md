# Plan: integer (normal) coordinates for exact intrinsic triangulations (research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Geodesics/param: integer (normal) coordinates"
- **Kind:** research study (not needed by current operations; trigger: an operation requiring a combinatorially exact common subdivision of intrinsic and extrinsic triangulations)
- **Tier:** L (new intrinsic triangulation data structure: integer normal coordinates + round-about ordering + exact predicates; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Recorded in [parameterization-geodesics.md](../roadmap/parameterization-geodesics.md#open-for-the-future-2026-11-15)
  and [research.md](../roadmap/research.md#open-research-questions):
  - Current intrinsic triangulation infrastructure (`mesh_ops/IntrinsicTriangulationRefinement.cs`, signpost-based
    intrinsic triangulations, Sharp, Soliman, Crane 2019) uses floating-point signpost angles + area checks, which
    suffice for all current geodesic / heat / refinement operations.
  - Known limitation of floating-point signposts: after long sequences of intrinsic edge flips or on nearly degenerate
    triangles, floating-point angle drift can cause the traced correspondence (common subdivision between intrinsic and
    extrinsic meshes) to fail or produce inconsistent combinatorial crossings.
  - **Integer (normal) coordinates** (Gillespie, Sharp, Crane, ACM TOG / SIGGRAPH 2021, *Integer Coordinates for
    Intrinsic Geometry Processing*) represent every intrinsic edge combinatorially by exact non-negative integers
    (normal coordinates + roundabouts), guaranteeing a combinatorially exact common subdivision with zero tracing drift.

## Trigger / decision needed

A concrete downstream feature that requires extracting a **combinatorially exact common subdivision** between an
intrinsic triangulation and the original `DMesh3` (e.g., exact texture/UV transfer after intrinsic Delaunay refinement,
exact geodesic mesh cutting, or certified intrinsic surface simplification — Phase 5 #9 Liu et al. 2023).

## Goal

Study how integer normal coordinates (Gillespie, Sharp, Crane 2021) fit onto geometry3Sharp's existing
`IntrinsicTriangulationRefinement` and exact predicates (`BigInteger` / `Exact*`), measuring memory/time overhead and
identifying exact-arithmetic requirements before any implementation.

## Scope

- Phase 1 (study only): `docs/design/INTRINSIC_INTEGER_COORDINATES.md` (data structures, flip/split update formulas,
  exact-vs-double edge-length policy, common-subdivision extraction, comparison against signposts on degenerate inputs).
- Phase 2 (only after owner go): new `mesh_ops/IntegerIntrinsicTriangulation.cs` (additive; existing signpost code
  untouched), tests in `tests/geometry3Sharp.Tests/IntegerIntrinsicTriangulationTests.cs`.

## Non-goals

Do not break or slow down existing `IntrinsicTriangulationRefinement` / `MeshGeodesicPath`; never port GPL code
(`geometry-central` is MIT — verify per-file license headers before reading if consulted, and record in `SOURCES.md`).

## Interface

Phase 1: none. Phase 2 (conditional): additive `IntegerIntrinsicTriangulation` with `FlipToDelaunay()`,
`Refine()`, `ExtractCommonSubdivision()` -> `DMesh3` + exact barycentric/parent-face correspondence.

## Look before you build

1. Library: `mesh_ops/IntrinsicTriangulationRefinement.cs`, `mesh_ops/MeshGeodesicPath.cs`,
   `mesh_ops/MeshGeodesicBezier.cs`, `benchmarks/IntrinsicBenchmark/`, `math/CoreMath.cs`, `math/BigInteger` / exact
   predicates.
2. Literature & permissive references:
   - Gillespie, Sharp, Crane (ACM TOG / SIGGRAPH 2021), "Integer Coordinates for Intrinsic Geometry Processing" (paper +
     MIT-licensed `geometry-central` implementation — verify headers before any read).
   - Sharp, Soliman, Crane (ACM TOG / SIGGRAPH 2019), "Navigating Intrinsic Triangulations" (signposts — baseline).
   - Liu, Gillespie, Chislett, Sharp, Jacobson, Crane (SIGGRAPH 2023), "Surface Simplification using Intrinsic Error
     Metrics" (Phase 5 #9 consumer).

## Steps (Phase 1 study, one docs PR)

1. Read Gillespie, Sharp, Crane (2021) in full; verify whether integer normal coordinates can overflow `long` (`Int64`)
   on high-aspect-ratio Thingi10K meshes (check when `BigInteger` fallback is needed).
2. Identify every floating-point failure case in our current signpost implementation (sliver/needle meshes, long flip
   chains, extreme scales `Similarity.All()`) that integer coordinates would eliminate.
3. Design the common-subdivision extraction and estimate memory/time overhead vs signposts on `IntrinsicBenchmark`.
4. Commit `docs/design/INTRINSIC_INTEGER_COORDINATES.md` with go/no-go verdict for the owner.

## Acceptance

- Study documents exact integer update rules (flips, splits, roundabouts), `Int64` overflow analysis, exact failure
  reproducers of current signposts, and complexity/effort estimate for Phase 2.

## Sources and licences

Gillespie, Sharp, Crane (2021) paper; `geometry-central` (MIT — verify per-file license headers before reading, add
notice to `docs/sources/parameterization-and-geodesics.md` if ported).

## Budget and stop rule

1 L research session for Phase 1. Stop Phase 2 unless a concrete caller needs exact common subdivision or signpost
failures appear in real workflows.

## Risks and open questions

- Integer normal coordinates guarantee *combinatorial* exactness of the common subdivision, while intrinsic edge lengths
  themselves remain floating-point (irrational square roots from law of cosines) — clarify in the study what is exact
  vs floating-point so determinism claims stay honest.
