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

**Owner decision, 2026-11-01: all four proposals below were accepted first; proposals 1 and 2 are superseded the same day by the
owner's final decision (section 3b): SR on a grid plus SRR's restore, pixel width w = tol** (`MESHING2D_DECISIONS.md`, D14).

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

## 3b. The full texts change the recommendation (2026-11-01, after the owner's decision)

Read in full after the owner allowed the hosts: Packer, *Extending the power of snap rounding variants*, CCCG 2007; Hershberger,
*Snap rounding: a cautionary tale*, SoCG 2025; Belussi, Migliorini, Negri, Pelagatti, *Snap rounding with restore*, ACM TSAS 2016.
Not read: the TU Delft thesis (the server resets the connection) and the ISRBD journal paper itself (paywalled; its statements below
come from Packer 2007 and Belussi et al., who cite it).

**What they say:**

1. **ISRBD's bound is coarse.** The drift bound δ must be at least about 3 times SR's largest deviation (Packer 2007, footnote 3;
   Belussi et al., Observation 1, citing Packer 2008). Plain ISR can drift Θ(n²) pixels (Packer 2007, footnote 2).
2. **ISRBD collapses at small angles.** Its drift bound holds for each segment, but the topological node at a small angle (a segment
   tangent to a polyline, two polygon boundaries close together) can drift far. Polygon faces then split or merge, and a polygon
   with holes can become invalid (Belussi et al., section 5.2, Figures 1 and 2). Our inputs have exactly these configurations: the
   small-angle apexes of the corpus, F-12, F-14, F-18.
3. **SRR keeps SR's bound.** It snap-rounds, then removes every critical pixel (a hot pixel crossed by an edge whose ends lie outside
   it) by *spreading*: the edge is rerouted through a pixel of its original segment's envelope next to the critical pixel, so that it
   no longer crosses it. It snaps only if no free pixel exists. Its guarantees: fixed precision, the output within SR's bound (the
   Minkowski sum of the segment with one pixel), topology kept up to collapse, and no critical pixel left. That is the same
   separation as ISR (every vertex at least half a pixel from every non-incident edge). Its complexity is O(n⁴ log n) in the worst
   case and O(n² log n) on geographic data. In their experiments it snaps less, collapses less and stays closer to the input than
   ISRBD (sections 5 and 6).
4. **Do not change the contact rules.** Hershberger's "relaxed snap rounding" changed only which pixel contacts count. It kept the
   deviation and topology proofs, yet broke the property "a segment snaps to a hot pixel iff it contacts it" (his Lemma 3). The
   Boolean built on it produced weakly simple polygons a year later, and the repair is a costly post-pass. SR's proofs (Guibas and
   Marimont) rest on disjoint axis-aligned pixels that contract uniformly. Hot regions of radius tol centred at the vertices
   (proposal 2 of section 3) overlap and are not pixels, so none of these proofs carry over. That would be an unproven variant of
   exactly the kind the cautionary tale warns about.

**Revised recommendation (for the owner; it supersedes proposals 1 and 2 of section 3):**

- **SR on a grid of pixel width w = tol, followed by SRR's restore.** Every SR proof stays valid. Movement is at most w/2 per axis
  (0.71 tol). In the output, every vertex is at least w/2 from every non-incident edge and at least w from every other vertex.
- **Input coordinates (proposal 3):** with the production default (tolerance = the data's grid, O11) the input vertices already sit
  at pixel centres and do not move. Off-grid input moves by at most w/2 per axis; that is inherent to any grid rounding. An
  alternative is Hershberger's stable SR, where pins do not attract segments, but Belussi et al. note that it does not remove
  critical pixels.
- **Gap band (proposal 4), in pixel terms:** features that share a pixel merge; vertex-edge distances below w/2 do not survive
  (the edge then passes through the vertex); features more than 2w apart (L∞) never interact; in between, the grid decides. To be
  documented as O11's relative bound.
- **Semantics as in section 3a:** drift reported (bounded by w/2 per axis by construction), a strict mode, merge below the
  tolerance.

**Owner decision, 2026-11-01: the revised recommendation is approved, with pixel width w = tol** (relayed by the coordinator;
`MESHING2D_DECISIONS.md`, D14).

## 3c. The gates, measured on master (2026-11-01)

The rows marked `m2.snap` were never run, because `rows` skips pending rows. Run with the current mesher (the automatic or explicit
tolerance merge of `ConstrainedDelaunayMesh2`, no snap rounding; a local, uncommitted switch let `rows` take pending rows):

| Row | Tolerance | Oracle | Size | Reading |
|---|---|---|---|---|
| F-10/a-c, F-11/a-d, F-12/a, F-13 | automatic | ok | 4 to 512 triangles | already met by the automatic merge |
| F-12/b | automatic | ok | 5 035 vertices | the Steiner limit stops it, as the row expects |
| F-14/a-e | automatic | ok | 173 to 3 284 vertices | met |
| F-15 (snap cycle) | 11/1024 | ok | 7 vertices | met |
| F-17 (tolerance larger than a hole) | 2^-10 | ok | 8 vertices | met |
| F-20 | automatic | ok | 520 vertices | met |
| F-18/b (wall 2^-4 away, kept) | 2^-7 | ok | 221 vertices | met |
| **F-18/a** (wall 2^-8 away, merged) | 2^-7 | ok | **2 885 vertices** | **passes only because the oracle allows a merge but does not demand it**: 13x F-18/b's size, so the wall is not merged and the gap is refined |
| **F-22/b** (sliver gone at 2^-29) | 2^-29 | ok | **20 007 vertices** | **the same as F-22/a without tolerance**: the sliver is not gone, the Steiner limit stops it; the oracle does not check it |
| **F-16** (shared piece S-B) | 2^-7 | **fails** | 100 009 vertices (at most 2 000) | the one real failure: refinement runs to the limit |

**What this means:**

- **The gates do not test what `m2.snap` is for.** Two rows (F-18/a, F-22/b) pass while missing their stated intent, because the
  oracle treats a merge within the tolerance as allowed, not required. Before building, the gates need sharpening: a vertex bound
  for F-18/a and F-22/b, and a check that the merged feature is gone (no triangle in the gap). Each needs a self-check that rejects
  today's output.
- **Topology up to collapse, per row** (the coordinator's question; expected under SR + restore with pixel w):
  - **Collapse intended:** F-11 and F-15 (points merge), F-17 (the hole vanishes; strict mode: an error naming the hole), F-18/a and
    F-22/b (wall and sliver merge onto the border), F-12 (the doubled border becomes one), F-14 (the stacked squares become one).
  - **Kept:** F-18/b, F-10 at distances above the band, F-20 (the polyline keeps its order along the long edges).
  - **Strict mode** reports each collapse with its location and the drift it needed, instead of performing it (aligned with
    SafeRounding's strict mode).
- **Pixel width and O11.** On-grid input merges with a segment only if the segment enters its pixel: guaranteed below w/2, never
  above about 2w. F-18/a's wall lies at exactly tol/2. With w = tol it sits on a pixel boundary, and the half-open pixel rule decides
  (here: not merged). With w = 2 tol it merges, and the movement bound becomes tol per axis, O11's wording. But then data on the
  tolerance grid no longer sits at pixel centres. O11's "merge within tol" and "tol = output grid" cannot both hold under SR. **The
  owner chooses:**
  - **w = tol** (the coordinator's recommendation; **chosen by the owner on 2026-11-01**): on-grid data stays put, the property valued in D14. O11's guarantee becomes "merge
    below tol/2, never above 2 tol (L∞), the grid decides in between". F-18/a's wall at exactly tol/2 then lies in the band, so its gate
    must not demand the merge.
  - **w = 2 tol:** merge guaranteed below tol, movement up to tol per axis. On-grid data moves by up to tol. F-18/a's gate then demands
    the merge.
- **F-16 may not need snap rounding at all.** Its failure is over-refinement (the hand-over's item 2), and the current merge may
  already give the shared piece. To be checked before building.

## 3d. Questions for the clean-room comparison session

The owner implemented snap rounding in his own fork of Triangle.NET. This session never reads that fork. A separate comparison session
may answer the questions below **in specification terms only**: behaviour, cases, results, parameter values, test inputs described in
words. No code, no data structures, no file or class names.

1. **Variant and grid:** which snap-rounding variant does it implement (plain SR, ISR, a restore or spread step, something else)?
   Is the grid tied to the tolerance (pixel = tol, 2 tol, something else), and where is it anchored (the origin, the data's bounding
   box, the first point)?
2. **Contact rule:** which pixel sides and corners count as touched (half-open as in Hobby, closed, or something else)? Was a
   symmetric rule tried, and what happened?
3. **Input on the grid:** do input points that already lie on the grid stay exactly where they are? What happens to input off the
   grid?
4. **Separation:** does the output guarantee a minimum distance between a vertex and a non-incident edge? If yes, how large, and how
   is it reached (iteration, spreading, snapping)?
5. **Drift:** what is the largest movement seen in practice, in units of the tolerance? Is it bounded or reported?
6. **Collapse:** what happens to a hole smaller than the tolerance, to two nearly coincident borders, and to a narrow wedge at a small
   angle? Are polygons that collapse to lines or points reported, removed or kept?
7. **Failures:** which inputs failed or gave surprising results (no termination, too many iterations, topology changes, invalid
   polygons, a result that changes when snapped again)? How were they resolved?
8. **Cost:** run times or iteration counts on the largest inputs (number of points, time), and where the time went.
9. **Test inputs:** which test inputs, described in words (kind of geometry, sizes, tolerance), showed the important cases?
10. **Interaction with the mesher:** does snapping run before the triangulation, or inside it? Does quality refinement afterwards
    ever undo a merge or meet a slim triangle created by snapping?

## 4. Plan once the owner has answered

- Read ISRBD, stable SR, SRR and the cautionary tale in full, then fix the variant and record it in `MESHING2D_PROVENANCE.md` and `docs/SOURCES.md`.
- Upper-bound check before building (AGENT_WORKFLOW section 8): the gates' sizes (F-20 up to 20 000 vertices, F-14 40 stacked squares) against a grid-indexed implementation.
- Build it as a step before exact noding, at the explicit tolerance only. The automatic tolerance keeps today's merge.
- Oracle: the displacement bound and the separation become checks (accept and reject self-checks, mutation), against the band of section 3.4.
- Gates: F-10 to F-18, F-20, F-22/b; F-15 (the snap cycle) and F-16 (the shared piece S-B) are the ones #201 failed.
