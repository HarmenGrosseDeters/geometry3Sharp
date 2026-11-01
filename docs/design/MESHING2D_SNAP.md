# 2D meshing: `m2.snap`, look before you build

Status: design note, 2026-11-01, before any code. It records the two checks of "Look before you build" (AGENT_WORKFLOW section 3)
for the plan's hand-over item 1 (D14: iterated snap rounding at the explicit tolerance), and the questions they raise for the owner.
Implementation starts after #233, #252 and #265 are merged; the gates are the catalogue rows F-10 to F-18, F-20 and F-22/b
(F-19 is `m4.wedge`).

## 1. The library

| Building block | Where | Use for `m2.snap` |
|---|---|---|
| Automatic-tolerance merge: points within the tolerance merge (`MergeNearPoints`, a grid of cell 2 tol), a vertex within the tolerance of a piece becomes its vertex (`InsertPiece`, the apex rule, `WithinTolerance`), no split point near another constraint (`NearOtherSource`) | `comp_geom/ConstrainedDelaunayMesh2.cs` | The O11 rules already exist at the automatic tolerance (about 2^-41 of the magnitude). They are local and give no separation guarantee: the chains stay within a tube, but a vertex may end up closer than the tolerance to a non-incident edge (D1, D14). Reuse the queries; the guarantee must come from the new step |
| Exact noding: crossings computed exactly, rounded, the mesh re-established on the rounded points; split points on the original input segment (O12) | `comp_geom/DelaunayKernel2.Constraints.cs`, `ConstrainedDelaunayMesh2.cs` | Unchanged at tolerance 0 (F-21, F-22/a). Snapping runs before it, on the input segments, and hands over a noded set |
| Exact 2D predicates (`Orient2d`, in-circle), adaptive | `math/ExactPredicates.cs`, `math/AdaptivePredicates.cs` | Which segments pass through a hot region, and the order of snapped vertices along a segment |
| Point and segment indexes | `spatial/PointHashGrid2d.cs`, `SegmentHashGrid.cs`, `SegmentQuadtree2d.cs`, `QuadtreeExactQuery2d.cs` | Hot-region queries (the segments through a region, the regions on a segment) |
| `Arrangement2d` (upstream) | `comp_geom/Arrangement2d.cs` | Not reusable: an absolute `VertexSnapTol` and double arithmetic, no guarantee |
| `ExactMeshBoolean.SafeRounding`, `MeshRoundingRepair` (stage R; with decision D′ in #251, `docs/design/SAFE_ROUNDING.md`) | `mesh_ops/` | No snap rounding: exact vertices are rounded, then repaired by local edits after Milenkovic and Sacks 2019. D′ adds a drift budget per vertex: every edit carries a proven bound δ, an edit beyond `maxLength` is refused, the largest drift is reported (`MaxDrift`), and what cannot be repaired within the bound is reported, not hidden. The code is 3D (`DMesh3`) and not reused; its **semantics** are the model for `m2.snap` (section 3a) |
| Clipper2 add-on | `addons/geometry3Sharp.Clipper2` | Integer coordinates, no snap rounding; not a building block |

Nothing in the library does snap rounding. `docs/SOURCES.md` already cites Hobby 1999 and Halperin and Packer 2002, as background for the 3D rounding.

## 2. The literature

| Work | What it gives | Bearing on O11 |
|---|---|---|
| D. H. Greene, F. F. Yao, *Finite-resolution computational geometry*, FOCS 1986 | Rounding an arrangement to a grid while keeping its topology | The problem statement |
| J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13(4), 1999 | Snap rounding (SR): pixels of a unit grid containing a vertex or crossing are hot; every segment through a hot pixel is routed through its centre | Each output segment stays within half a pixel (in each axis) of its input. But a vertex can end up arbitrarily close to a non-incident edge |
| L. Guibas, D. Marimont, *Rounding arrangements dynamically*, IJCGA 8(2), 1998 | Dynamic SR. The properties: the output stays within the Minkowski sum of the input with a pixel, no new crossings, the topology kept up to collapse | The displacement bound of SR |
| M. Goodrich, L. Guibas, J. Hershberger, P. Tanenbaum, *Snap rounding line segments efficiently in two and three dimensions*, SoCG 1997; M. de Berg, D. Halperin, M. Overmars, *An intersection-sensitive algorithm for snap rounding*, CGTA 36(3), 2007 | Output-sensitive SR algorithms | Running time. The input sizes here (up to 10^5 segments) favour a simple sweep or grid over a theoretically optimal method |
| D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 23(2), 2002 | ISR: repeat until every vertex is at least half a pixel from every non-incident edge | The separation that D14 wants. **Drift:** a segment can move far from its input, because each round snaps to hot pixels the previous round created. That conflicts with O11's "geometry moves by at most the tolerance" |
| E. Packer, *Iterated snap rounding with bounded drift*, SoCG 2006; CGTA 40(3), 2008 | ISRBD: ISR with a parameter that bounds the drift | The variant that can meet O11's displacement bound. Its exact trade-off (which separation is given up when the bound binds) is to be read in the paper before building |
| J. Hershberger, *Stable snap rounding*, SoCG 2011; CGTA 46(4), 2013 | SR whose output does not change when rounded again | An alternative to ISR. Its guarantees are to be read in the paper before choosing |
| J. Hershberger, *Snap rounding: a cautionary tale*, SoCG 2025 (LIPIcs 332, 57) | Experience with SR in an IC design system: features natural in theory were stumbling blocks in practice | Must be read before building. The full text is CC-BY, but its host (drops.dagstuhl.de) is not reachable from this session |
| A. Belussi, S. Migliorini, M. Negri, G. Pelagatti, *Snap rounding with restore: an algorithm for producing robust geometric datasets*, ACM TSAS 2(1), 2016 | SRR: the same goal as ISR and ISRBD, with better approximation and better preservation of topology; GIS data | The closest application (GIS, shared borders). Read before choosing. Its host (iris.univr.it) is not reachable from this session either |
| V. Milenkovic, *Shortest path geometric rounding*, Algorithmica 27, 2000 | Rounding by shortest paths in the free space | Background |
| V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, CAD 108, 2019 | d-separation: a mesh whose disjoint features are more than 2e apart rounds safely. Local edits, then an expansion by linear programs, with an exact certificate | The method behind stage R's repair (read there in full). In 2D the same certificate (every vertex more than d from every non-incident edge) is ISR's separation; it is the natural exact check of the result |
| JTS Topology Suite, `SnapRoundingNoder` and `SnappingNoder` (documentation) | Grid SR with hot pixels for overlay. Separately, snapping vertices within a tolerance, without guarantees | Practice in GIS. Documentation only; no code is taken (GEOS is LGPL; JTS is EPL/EDL, but papers come first) |

## 3. What the checks change: four conflicts between ISR and O11

**Owner decision, 2026-11-01: all four proposals below are accepted** (recorded in `MESHING2D_DECISIONS.md`, D14): ISRBD, hot regions of
radius tol centred at the vertices, untouched vertices keep their input coordinates, the gap band documented.

D14 approved ISR at the explicit tolerance. The checks show four places where plain ISR and O11 disagree. These are for the owner to settle before building:

1. **Drift (O11: moves at most the tolerance).** ISR's drift is unbounded in the worst case; SR's is half a pixel. *Proposal:* the
   bounded-drift variant (ISRBD), or SR plus a final exact check that falls back to the 1.4 mm rule of O11 ("where needed to produce a valid mesh").
2. **Where hot regions are (O11: a point within the tolerance of a segment becomes its vertex).** SR's hot pixels are grid cells.
   A segment that passes within the tolerance of a vertex need not pass through that vertex's pixel. A pixel's half-width is pixel/2, so with pixel = tol only distances up to tol/2 are certain. F-18/a (wall 2^-8 from the border at tolerance 2^-7) lies exactly on that boundary. *Proposal:* hot regions centred at the vertices, of radius tol (O11's wording), with ISR's iteration and ISRBD's drift bound on top. That is snap rounding to vertices, not to a grid.
3. **Grid or input coordinates.** Grid SR moves every vertex to a pixel centre, even an input vertex that needs no snap. O11's production default ("tolerance = output rounding grid, 1 cm") reads as if a grid output is wanted, while D1 and Q9 keep input coordinates elsewhere. *Question:* is the output at an explicit tolerance on a grid (pixel = tol, at most 0.71 tol of movement), or do untouched vertices keep their coordinates (proposal 2)?
4. **Gaps (O11: gaps larger than the tolerance are kept).** No rule with a finite region can merge everything below tol and keep everything above it: two vertices at 0.9 tol from a segment and 1.1 tol from each other interact. *Proposal:* document a band. Below tol, merged; above 2 tol, kept; between, either (F-18: 2^-8 merged, 2^-4 kept, at 2^-7). This is the "relative bound" O11 asks the implementation to document.

## 3a. One model for callers: aligned with SafeRounding (coordinator, 2026-11-01)

The boolean's rounding (stage R) and `m2.snap` share no code (3D edits on `DMesh3` against noding on the 2D kernel). But callers should
see one model. Requirements for `m2.snap`, following `SAFE_ROUNDING.md` (#251, and its section 5 on the tolerance options, approved by
the owner on 2026-11-01):

- **Tolerance options:** a magnitude-aware default (the boolean: max(1e-5 diag, 64 ulp(max |coord|)); the mesher's automatic
  tolerance is 2^-41 of the magnitude, kept for automatic mode) and an optional absolute tolerance (O11's explicit `SnapTolerance`).
- **Bounded drift, reported:** a per-vertex drift with a proven bound per step (ISRBD's bound in 2D), refused beyond the limit;
  the largest drift is in the result (like `MaxDrift`).
- **Strict mode:** a failure status that gives the location and the drift that would be needed, instead of moving more.
- **Weld or merge below the tolerance:** points closer than the tolerance merge (O11), as the boolean's weld edit with δ = 0.

The coordinator recommends to the owner, for the four questions of section 3: ISRBD, hot regions of radius tol, untouched vertices
keep their input coordinates, the gap band documented. Stage R's experience feeds the guarantees and the reporting, not the
algorithm.

## 3b. Reading status (2026-11-01)

What ISRBD does is known so far only from secondary sources: it finds the segments that would drift too far under ISR and adds a few
extra hot pixels, joined by zero-length edges, to hold them; its output can be non-canonical. The bound itself and the separation it
keeps are not yet read. Not reachable from this session's network: the ISRBD paper (ACM DL, Elsevier), Packer's *Extending the power
of snap rounding variants* (CCCG 2007, cccg.ca), the TU Delft MSc thesis *Snap rounding polygons with a triangulation* (2023,
3d.bk.tudelft.nl), the cautionary tale (drops.dagstuhl.de) and SRR (iris.univr.it). **No code before these are read**: the variant
is implemented from the paper, not from summaries.

## 4. Plan once the owner has answered

- Read ISRBD, stable SR, SRR and the cautionary tale in full, then fix the variant and record it in `MESHING2D_PROVENANCE.md` and `docs/SOURCES.md`.
- Upper-bound check before building (AGENT_WORKFLOW section 8): the gates' sizes (F-20 up to 20 000 vertices, F-14 40 stacked squares) against a grid-indexed implementation.
- Build it as a step before exact noding, at the explicit tolerance only. The automatic tolerance keeps today's merge.
- Oracle: the displacement bound and the separation become checks (accept and reject self-checks, mutation), against the band of section 3.4.
- Gates: F-10 to F-18, F-20, F-22/b; F-15 (the snap cycle) and F-16 (the shared piece S-B) are the ones #201 failed.
