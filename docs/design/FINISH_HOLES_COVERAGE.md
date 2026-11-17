# Procedural coverage of FinishHoles

The accepted fallback is a hand-built contour with one missing quad: an open box,
with or without a closed tetrahedron crossing the missing patch. Both variants
exercise the fill; the second requires a negative self-intersection verdict.
`DualContouringEnvelopeTests.FinishHoles_MissingQuad_ExactOracleAndSimilarities`
checks the filled output and its exact intersection count over `Similarity.All()`.
The exact scales preserve the mesh bit for bit.

## Natural grid search (2026-11-17)

Every entry below reports `NonManifoldTriangles = 0` for **each** seed and
resolution in its row. Repeated identical touching-box inputs are included in
the run count. No mesh files or external data were used.

| Input | Seeds / variants | Resolutions | Runs | Dropped triangles per run |
| --- | --- | --- | --- | --- |
| `MeshRobustRepairTests.ThinPlates` | 1..8 | 4, 6, 8, 12, 16 | 40 | 0 |
| Two boxes touching at a vertex | 8 repeats | 4, 6, 8, 12, 16 | 40 | 0 |
| Two boxes touching at a face | 8 repeats | 4, 6, 8, 12, 16 | 40 | 0 |
| Edge-touching boxes, triangle id removed from both | ids 1..8 | 4, 6, 8, 12, 16 | 40 | 0 |
| Initially edge-touching boxes, both rotated about their centers around Z | 7, 14, ..., 56 degrees | 4, 6, 8, 12, 16 | 40 | 0 |
| Random overlapping boxes on a 4 x 4 x 4 lattice | 1..32 | 4, 6, 8 | 96 | 0 |
| Random rotated boxes on that lattice | 1..32 | 4, 6, 8 | 96 | 0 |
| Random open boxes on that lattice | 1..32 | 4, 6, 8 | 96 | 0 |

The touching pairs have half extents (1,1,1), with centers (0,0,0) and
(2,2,2), (2,0,0), or (2,2,0), respectively. The lattice probes use
`Random(seed)`, traverse x/y/z in that order from 0 through 3, and keep a box
when `NextDouble() <= 0.5`. Overlapping boxes have half extents 0.55;
the other two kinds use 0.35. Rotated boxes use axis (1,2,3).Normalized and
`180 * NextDouble()` degrees. Open boxes remove triangle ids 0 through 11
independently when `NextDouble() < 0.25`. Each run constructs a fresh
`MeshWindingNumberSolidify` with `Adaptive = false`, the listed `GridResolution`,
`StopIfSelfIntersecting = true`, and `KeepRawContour = true`, then calls
`Compute()`. All 488 calls returned true.

## Why use the hand-built fallback?

This is a limitation of this coverage strategy, not a newly discovered defect.
On the uniform grid, a non-manifold dual edge would join the same pair of cell
patch vertices more than twice across their common face. A non-ambiguous face
has just one contour segment. On an ambiguous face the two inside corners stay
separate unless connected through the rest of a cell. If they are connected,
the cell has exactly one surface patch under the contourer's rule (outside
corners also connect across ambiguous diagonals).

`UniformCornerPatterns_JoinedAmbiguousCornersHaveOnePatch` exhaustively checks
that local fact for all 256 corner-sign patterns, including all 36 joined
ambiguous-face occurrences. Its graph transitive closure is independent of the
library's union-find implementation. Thus a face needing two segments between
the same patch pair has single-patch cells on both sides: the #572 face-vertex
path separates those segments before triangle insertion. Quad diagonals join
opposite cells around one unique primal edge, and fan-center edges use unique
ids, so neither introduces another repeated-edge rejection.

If the QEF point is inadmissible, `place_face_vertex` tries other interior
points, ultimately the centroid of the allowed face triangle. When the actual arithmetic produces a strictly interior centroid on the correct
side of the diagonal, face placement succeeds. The associated edge point
similarly has an interior fallback. The argument assumes successful actual
face/edge placement, consistent node signs and unique valid cell/face/edge ids;
the mere existence of some representable interior point is insufficient.
Changing the sampled winding values, using open inputs, or coarsening these
normalized grids does not bypass this construction. The missing-quad fallback
explicitly represents the contour state that this construction prevents in
these ordinary uniform-grid cases.

This is **not** a claim of unreachability at arbitrary floating-point ranges,
collapsed grid intervals, incomplete candidate-cell sets, or adaptive transitions.
Those are outside the uniform normalized solidify search above. The local
exhaustion also does not prove the absence of self-intersections: FinishHoles
must still check any patch it adds exactly.

Prior art checked: Ju, Losasso, Schaefer and Warren, *Dual Contouring of Hermite
Data* (2002), uses Hermite/QEF vertices; Schaefer, Ju and Warren, *Manifold Dual
Contouring* (2007), distinguishes surface patches; Ju and Udeshi,
*Intersection-free contouring on an octree grid* (2006), motivates the face/edge
construction (through Wang's summary, as recorded in `SOURCES.md`). These are
context for the existing construction, not a proof of this implementation;
the local lemma and source inspection above supply the bounded explanation.
The graph test checks this mathematical lemma; it does not directly test the
library connectivity implementation.
No external implementation code was read or ported.

## Additional grid fault injection

The existing `DualContouring.FailFaceVertexForTests` seam gives actual grid
contours with refused face vertices. At resolution 24, ThinPlates seed 1 reports
4 dropped triangles and 0 fill intersections; seed 3 reports 8 and 41.
`SolidifyHoleFillTests.RefusedFaceVertices_*` checks closure, manifoldness,
finiteness, independent exact output counts, similarities, repeated runs and
instance reuse. Rotations can remove the ambiguous faces, so only exact scales
must preserve positive refusal counts. This is supplemental coverage, explicitly
not a naturally occurring refusal.

## Mutation checks

Each of these temporary library edits fails the new missing-quad test in all
eight similarity frames; all edits are restored afterward:

- Skip `FillSmallHolesChecked` in `FinishHoles`.
- Return zero without the exact check after `FillSmallHoles`.
- Invert the Boolean verdict returned by `FinishHoles`.

The direct verdict assertions matter: the non-adaptive solidify caller ignores
the Boolean return and separately observes the intersection count. Grid-only
tests therefore cannot detect the inverted-verdict mutant.
