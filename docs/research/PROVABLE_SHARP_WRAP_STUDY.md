# Provably valid sharp-feature recovery on the alpha wrap (HP 4, study)

Status: **research spike, 2026-11-22; options for the owner, nothing built in the library.** Plan #1280, context #1237.
Prototypes and the upper-bound measurement: `benchmarks/SharpFeatureProbe` (mode `bound`, section 6). Sources are papers
only; no CGAL, Triangle, TetGen or geodesic-library code was read, and no external code was run.

## 1. The question and the short answer

Can creases and corners be recovered on the output of `MeshAlphaWrap` with a proof that the result stays valid (closed,
2-manifold, free of self-intersections, the input inside, within an offset band), and what is the gain?

- **Validity without the band is provable, and cheaply**: closed, manifold, free of self-intersections and the input
  inside are an invariant of exactly checked local operations (section 4). The opt-in pass K (`AlphaWrapFeaturePass`,
  #736) has this structure; four small gaps separate today's code from a written theorem (4.3), and the offset band is
  a fifth, separate item (K checks it at vertices only). Measured on the whole pipeline (K, then the simplification
  with its own exact tests): all 270 wrap-based results of this study are closed, manifold, free of self-intersections
  and cross the input nowhere.
- **Recovery is not provable for a hill-climbing pass**, and the measured room above K is small on clean inputs: K
  recovers 94.7 % of the crease length within 0.25 offsets (median over the 40 clean CAD-like cases; 94.5 % over the 10
  at the origin), against 100 % for the oracle; a single checked corner move adds the corners (0 -> 0.81-0.86). What K
  does not reach has three causes, and none of them is cured by a proof technique: features smaller than about 4 alpha
  (the wrap never enters them), the input's features being unknown on dirty inputs (a soup: K 0.000-0.006, because it
  reads creases from shared edges; overlapping boxes: 0.80-0.82 against 0.94-0.96 with the union's creases given), and
  corners.
- **Where a recovery proof is available, the wrap is not needed**: the provable constructions (protecting balls in a
  weighted Delaunay triangulation, or the direct mitered offset) need a clean piecewise-linear complex with known feature
  curves. On exactly those inputs the direct mitered offset is exact and passes the exact validity check on 64 of 65
  cases here, with 4-8 times fewer triangles and a true band [1, 1.73] offsets.
- **Leaning** (section 8): no weighted-Delaunay wrap and no dual-contouring hybrid. Write the validity theorem for K and
  close its gaps (option 1); take the measured cheap levers into the K follow-ups #1237 (option 2: corners, a
  feature source for dirty inputs, a recovery report); the certified direct mitered offset for clean inputs is a
  separate owner decision (option 3, a new operation).

## 2. What "valid" and "recovered" must mean

Notation: the input I (a triangle set), the offset d > 0 and alpha a of the wrap, dist(x) the distance to I.

**Two different sharp surfaces.** The Euclidean offset surface {dist = d} is sharp only at the *concave* creases of the
input (moved out; every point of that crease line is at distance exactly d). At a convex crease and corner it is round.
The *mitered* offset (every face plane moved out by d) is sharp at convex creases too, but its crease line is
d / sin(t / 2) from the input edge (t the interior dihedral angle; 1.41 d at a 90 degree edge, 1.73 d at a box corner)
and unbounded as t goes to 0, so it needs a cap (K and this study: 4 d). [MESH_OFFSET.md](../design/MESH_OFFSET.md)
section 1 makes the same distinction. A sharp convex crease is therefore never "within the offset": the band of a
sharp result is [c d, K d] with K >= the miter factor of the sharpest recovered crease.

**The target theorem** splits into two statements of very different difficulty.

- **(V) Validity.** The result R is a closed, oriented 2-manifold triangle mesh without self-intersections, I lies in
  the bounded component of the complement of R, and b_lo d <= dist(x) <= b_hi d for every point x of R.
- **(R) Recovery.** For every crease segment and corner of a *feature network* N that satisfies stated preconditions
  (crease angle in a range, clearance and feature size above a multiple of a and d), R contains a chain of edges within
  e of it (and a vertex within e of the corner).

(R) presupposes that N is defined. For a clean closed polyhedron N is the crease and corner set of the mitered offset.
For a dirty input (a soup, overlapping components, holes) N has no definition from the input's connectivity: the
creases of two overlapping boxes are intersection lines, not edges. This is the risk the plan names, and section 6
measures it.

## 3. Building blocks in the library (searched first)

| building block | gives | missing for (V) / (R) |
|---|---|---|
| `MeshAlphaWrap` (Portaneri et al. 2022; notes in [ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md)) | closed, manifold, no self-intersection, input strictly inside, vertices at d | rounds concave creases by the empty alpha ball, chords convex ones; `AlphaField` (local alpha) exists |
| `AlphaWrapFeaturePass` (pass K, #736) | plane snap, crease points, crease snap, splits, flips; every operation checked exactly (new triangles against the mesh and the input, the swept tetrahedra against the input), angle floors | no corners; features only from input edges with two triangles; no recovery statement; band only at vertices (>= 0.9 d) |
| `Reducer.KeepClearOf`, `Reducer.TrianglesTouch`, `Reducer.TetrahedraMeet` | the exact "does not touch / does not sweep over the input" tests, shared by the simplification | - |
| `ExactTriangleIntersection`, `TriangleTriangleFilter.IntersectsBeyondShared` | exact triangle-triangle tests with shared simplices | - |
| `MeshRobustRepair.NotCleanReasons` | the a-posteriori validity check (closed, manifold, self-intersections) | - |
| `MeshEnvelope` / `PolyhedralEnvelope` | "triangle within e of a mesh" | an inner approximation of the envelope; it cannot certify "at least c d away" (MESH_OFFSET.md section 2) |
| `PassageWrap` ([PASSAGE_WRAP.md](../design/PASSAGE_WRAP.md)) | the pattern "a bound from the carving rule plus a measured certificate" | - |
| `Delaunay3` (exact, unweighted) | the wrap's triangulation | no weighted (regular) triangulation, no power predicate, no hidden points: needed by protecting balls |
| `DualContouring` with #552 / #572, `TetFeatureSurface` (#1196) | sharp vertices from Hermite data; flips and Steiner splits to recover creases in a tet mesh | topology not certified; no offset implicit for a mitered surface |
| `MeshOffset` (stage A1) | Euclidean offset with a band | mitered offsets are named as a separate item (its 9.4) |

A point-triangle distance with a stated rounding bound is missing (also noted in MESH_OFFSET.md): it is what a certified
band needs (4.3).

## 4. Validity: a theorem about exactly checked local operations

### 4.1 Statement

Let R be a closed 2-manifold triangle mesh without self-intersections and without degenerate triangles, with I in the
bounded component of its complement (the raw wrap). Apply a sequence of operations, each a vertex move, an edge split
(a new vertex on the edge, then a move) or an edge flip, and keep an operation only if

0. (collapses are not covered here: the simplification after K has its own tests, `Reducer.KeepClearOf` with the swept
   tetrahedra of #760; a pipeline theorem cites both)
1. (combinatorics) a flip does not create an edge that exists;
2. (embedding) the new triangles are not degenerate, intersect each other only in shared vertices and edges, and
   intersect the unchanged triangles only in shared vertices and edges;
3. (sweep) no triangle of I meets the swept region: the union of the tetrahedra (old triangle, new vertex) for a move,
   the tetrahedron of the four vertices for a flip;

all decided exactly. Then the result is again a closed 2-manifold without self-intersections with I in the bounded
component. **No property of the order, the targets or the number of operations is used**: any heuristic may choose them.

### 4.2 Proof sketch

Each operation replaces a triangulated disk D (the star of the vertex, the two triangles of the edge) by a disk D'
with the same boundary cycle. (1) keeps the complex a 2-manifold (a move and a split never change that). By (2), D' is
embedded and meets R \ D only along the common boundary, so R' = (R \ D) + D' is an embedded closed surface. For the
enclosure, move the vertex linearly from p to x: the surfaces in between differ from R only inside the union S of the
tetrahedra conv(a, b, p, x) over the link edges (a, b), which are the tetrahedra (old triangle, new vertex). A point
outside S is on no intermediate surface, so its winding number with respect to R and R' is the same; by (3) every
point of I is outside S (I is closed and no input triangle meets S), so I stays in the bounded component. For a flip
the two triangle pairs bound the tetrahedron of the four vertices, and the same argument applies. Induction over the
sequence. (The intermediate surfaces need not be embedded: only the winding number of points outside S is used.)

### 4.3 What pass K has, and the gaps to a written theorem

K's `try_move`, its split and its flip check (2) and (3) with `TriangleTriangleFilter.IntersectsBeyondShared`,
`Reducer.TrianglesTouch` and `Reducer.TetrahedraMeet` (exact predicates), plus angle floors and a normal-turn limit that
the proof does not need. The gaps:

1. **Degenerate rest triangles are skipped** in the mesh test (`clear`), and the theorem assumes none. The raw wrap can
   contain near-degenerate triangles ([ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md)); an exactly
   degenerate one must be tested as a segment (as `Reducer.TrianglesTouch` does for input triangles since #746) or
   make the pass skip its neighbourhood.
2. **`TetrahedraMeet` tests faces exactly but containment by corners and centroids** ("one vertex strictly inside").
   An input triangle that meets a tetrahedron either crosses a face or lies inside with all its corners, so the test
   is complete if every input triangle's corners are tested; this needs one sentence of proof and a test, not new code,
   unless the corner test is sampled.
3. **Non-degeneracy of the new triangles is decided in floating point** (`unit_normal`: a normal length threshold),
   while 4.1 asks for an exact decision. An exactly degenerate new triangle that passes the threshold would enter the
   mesh and then be skipped as a rest triangle (gap 1). An exact collinearity test (`ExactTriangleIntersection.IsDegenerate`)
   on the new triangles closes it.
4. **The pipeline.** K's results are simplified afterwards (edge collapses). The statement a user sees is about the
   pipeline, so the written theorem has to include the Reducer's checked collapse (same pattern, already built).
5. **The band.** (V) asks b_lo d <= dist <= b_hi d on all of R. K checks only vertices, in floating point
   (`far_enough`, 0.9 d). Measured on the triangles' sample points (corners, edge midpoints, centroids), the wrap-based
   results come as close as 0.003 d to the input (the chord over a convex crease, known since the contact audit of
   #498) and reach 10.75 d, just under a + d = 11 d (an unresolved concave crease). A certified band needs (a) a point-triangle
   distance with a rounding bound and (b) a subdivision argument (dist is 1-Lipschitz: a triangle within r of a point at
   distance D is at least D - r away). Lower band only where the caller needs it; it costs triangles on convex creases.

So "provably valid" in the sense of (V) without the band is small code (gaps 1 and 3), a proof text and tests on K,
about days; it is not what today's code has to the letter. The band is a small new
check with an error bound (a tier-L review for the bound, small in code).

## 5. Recovery: where a proof can come from

### 5.1 Not from hill-climbing

K accepts or rejects each operation on the wrap's given triangulation. Whether the operation that would complete a
crease passes the angle floors and the exact tests depends on the positions of the wrap's Steiner points, for which the
wrap gives only "on the offset surface, facets with an empty ball of radius <= a". No precondition on the *input* alone
implies acceptance. Measured: 39 % of K's attempted operations are rejected (median, clean cases), and a second round
of the unmodified pass after the corners gains nothing (variant KCK, section 6; the pass rebuilds its labels, so this
is not a retry that knows the first round's crease vertices). A recovery statement for K can only be
**a posteriori**: report, per crease of N, the covered length. That is cheap, exact enough, and honest.

### 5.2 Constructive routes with a proof (prior art in section 7)

- **(a) Protecting balls and a weighted Delaunay triangulation** (Cheng-Dey-Ramos; Cheng-Dey-Shewchuk; Dey-Levine).
  Crease curves and corners are covered by balls, treated as weighted points; no refinement point is ever inserted
  inside a ball, so the crease edges are in the weighted triangulation, for any input angle. To use it *inside* the
  wrap, three things beyond a 3D regular triangulation (exact power predicate, hidden points) are needed: (1) the
  crease edges must end up on the carved boundary, which for convex creases means the carving must run against the
  mitered offset solid, not the Euclidean one (the miter line is outside {dist <= d}; the wrap carves it away); no
  distance-like oracle for the mitered offset of a dirty input exists; (2) at concave creases the empty alpha ball
  cannot reach the crease, so alpha must shrink locally with a lower bound tied to the ball radii, and the wrap's
  termination argument must be redone for that; (3) the theory needs a piecewise smooth complex with known feature
  curves: a clean input. Months of new infrastructure, and its precondition is exactly the input class of (b).
- **(b) The direct mitered offset with an exact certificate.** For a clean closed polyhedron move every vertex by the
  shortest s with n_i . s = d over the planes of its triangles (three independent planes: the corner point); the
  connectivity is unchanged. The result is exactly on the offset planes, so recovery is by construction; it is valid
  iff it has no self-intersection, which is true when d is below a local feature size and is *decided exactly* by
  `NotCleanReasons` plus the enclosure test. It fails only where it should (features below about 2 d, capped miters,
  vertices with more than three inconsistent planes), and the caller falls back to the wrap with K. The wrap is the
  fallback, not the base. This is the oracle O of section 6.
- **(c) A constructive crease operation on the wrap ("planar strips").** After K's plane snap the wrap's vertices near a
  crease lie on one of two offset planes. Remove the triangles between the last vertices on plane 1 and the last on
  plane 2 and triangulate the two planar polygons between those boundary chains and the crease line. Planar
  triangulations do not self-intersect, lie on the offset planes (band exact), and exist whenever the two chains
  project to simple polygons: a per-feature precondition that is checkable, with K's exact test as the backstop.
  Recovery is then guaranteed for every crease whose precondition check passes, which is again a posteriori, but with a
  clear failure reason per crease. The bound on its gain is the gap between K and O.
- **(d) Dual contouring in crease bands, stitched into the wrap.** Needs an implicit for the mitered offset, a certified
  DC topology (not available, [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md)) and an exact stitch. Dominated
  by (b) on clean inputs and by (c) on the wrap.

## 6. Upper bound, measured before any build

`SharpFeatureProbe bound` (no library change). 65 procedural cases at alpha = diagonal / 300, offset = diagonal / 3000
(a = 10 d): the six solids of the probe, three gears (teeth 20, 4 and 1.6 alphas wide), a block with filleted edges,
each also rotated (37 degrees about (1,2,3)), far from the origin (translated by (1e6, -2e6, 3e6), about 1e5 sizes) and
both; dirty inputs (triangle soups, two overlapping boxes); sweeps of the concave and convex crease angle and of a
slot's width in alphas.

- **Oracle O**: the direct mitered offset of the clean reference (5.2 b). Its sharp edges and corners are the feature
  network N every variant is measured against, so O scores 1 / 0 by construction; what is measured on O is its validity.
- **A** the pipeline (wrap, simplification). **K** pass K, then the simplification. **KC** K plus a corner step: each
  corner of N is offered to the nearest wrap vertices through the pass's own `TryMove` (all exact checks apply).
  **KCK** KC plus a second round of K. **KO / KOC** (dirty inputs) K with the clean reference as the pass's input: the
  bound for "the features of a dirty input are known". (K's checks then run against the reference; that is sound here
  only because the dirty input lies in the reference's closed solid, and every result is measured against the real
  input. A real feature source must keep the clearance against the real input.)
- Metrics, in offsets: **cov t** = share of N's length within t offsets of a sharp edge (> 30 degrees) of the result;
  **fh** = that distance (the directed feature Hausdorff distance, max and p95, clamped at 2 a = 20); **corner** = share
  of N's corners with a result vertex within 0.25; **prec** = share of the result's sharp edges within 0.25 of N; **band**
  = distance of the result's sample points to the input; validity = `NotCleanReasons`, exact crossings of the input,
  vertices inside the input.

Clean solids at the origin (10 cases; medians, worst case in brackets; the three other poses are in
[bound_tables.md](../../benchmarks/SharpFeatureProbe/results/bound_tables.md); their coverage and precision medians
agree within 0.02, the triangle counts and times do not: the rotated inputs give about half the triangles):

| variant | valid | triangles | cov 0.1 | cov 0.25 | cov 1 | fh p95 | fh max | corner | prec | band min / max | time s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|
| A | 10/10 | 14165 | 0.000 | 0.001 [0.000] | 0.900 | 2.84 | 11.10 [20] | 0.00 | 0.00 | 0.00 / 10.75 | 5.0 |
| K | 10/10 | 4288 | 0.931 | 0.945 [0.488] | 0.991 | 0.29 | 7.75 [20] | 0.00 | 0.91 | 0.04 / 10.75 | 6.2 |
| KC | 10/10 | 4148 | 0.941 | 0.954 [0.507] | 0.992 | 0.20 | 7.22 [20] | 0.86 [0.48] | 0.92 | 0.04 / 10.75 | 6.2 |
| KCK | 10/10 | 4175 | 0.941 | 0.953 [0.511] | 0.991 | 0.21 | 7.31 [20] | 0.86 [0.47] | 0.92 | 0.04 / 10.75 | 6.7 |
| O | 10/10 | 558 | 1 | 1 | 1 | 0 | 0 | 1 | 1 | 1.00 / 1.73 | 0.0 |

Dirty inputs (5 cases: 3 soups, 2 poses of the two overlapping boxes). The soups have exactly coincident vertices, so
their KO rows equal the clean K rows by construction: they show that welding is all a soup needs, nothing more. Only
the two-box rows are an independent measurement, and there the "known features" are the union's boundary, ie the
result of a boolean:

| variant | valid | cov 0.25 | corner | fh max |
|---|---|---:|---:|---:|
| A | 5/5 | 0.000 | 0.00 | 11.14 |
| K | 5/5 | 0.006 [0.000] (soups 0.000-0.006, two boxes 0.80-0.82) | 0.00 | 11.14 |
| KO | 5/5 | 0.944 [0.920] | 0.01 | 6.20 |
| KOC | 5/5 | 0.959 [0.954] | 0.92 [0.75] | 5.57 |

Sweeps (K, cov 0.25): concave crease opening 20 / 30 / 45 / 60 / 90 / 120 degrees: 0.88 / 0.92 / 0.94 / 0.95 / 0.95 /
0.92; convex crease 20 / 30-120 / 150 degrees: 0.60 / 0.89-0.91 / 0.17 (150: at the 30 degree sharpness threshold, and
capped miters at its 15 degree base edges; 20: the oracle's crease is capped at 4 offsets while the planes meet 5.76
offsets out, farther apart than K's 1-offset gate, so that crease cannot score and the 0.60 is the other creases: the
two ends of the angle sweep measure the cap, not K); slot width 0.1 / 0.25-1 / 2 / 4 / 8 alphas: 0.60 / 0.76-0.77 / 0.89 / 0.95 /
0.95 (below 2 alphas the wrap does not enter the slot: fh 20); gear teeth 20 / 4 / 1.6 alphas: 0.92 / 0.91 / 0.45-0.49.

Findings:

1. **Validity holds everywhere it is checked**: 270 of 270 wrap-based results (A, K, KC, KCK, KO, KOC) are closed,
   manifold, free of self-intersections, cross the input in 0 triangles and have no vertex inside it. These are
   pipeline results (after the simplification), consistent with the invariant of section 4 and the Reducer's tests;
   they do not prove it.
2. **The room above K on clean inputs is about 5 % of the crease length at 0.25 offsets (0.9 % at 1 offset), the corners,
   and the tail of fh** (median max 7.75 offsets: crease ends at concave corners and unresolved small features).
3. **Corners are cheap**: one checked move per corner is accepted for 7932 of 8226 corners (96 %) in the raw wrap; 0.81-
   0.86 survive the simplification (it does not know them: a pinned-vertex list would keep the rest). The move does not
   pull the crease ends in (cov +0.01), and a second round of the unmodified K does nothing: the remaining 4-5 % need an operation K
   does not have (5.2 c) or are below the wrap's resolution.
4. **Feature size, not proof technique, limits the wrap**: below about 4 alphas of feature size K degrades (0.89 at 2,
   0.77 at 1, 0.45-0.49 on 1.6-alpha gear teeth). Only a smaller (local) alpha changes that; it is the D1 lever of the
   2026-11-09 probe.
5. **On dirty inputs the missing piece is the feature network, not the recovery**: a soup has no edge with two
   triangles, so K finds no crease and does nothing (0.000-0.006; welding coincident vertices first would restore the
   clean result); with overlapping components it misses the intersection creases (0.80-0.82 against 0.94-0.96 with the
   union's creases given: + 0.14, two rows of one shape). The sample is small and its second half presupposes a
   boolean; it shows the direction, not a size. K's gate "near the
   miter line of an input sharp edge" could not be switched off for a measurement without a library change (its
   segment grid is not built for a large radius), so "creases from plane pairs alone" stays unmeasured.
6. **Rotation and distance from the origin do not change the recovery** (coverage and precision medians within 0.02;
   all valid; the rotated inputs wrap to about half the triangles, the wrap's matter, not K's). K's thresholds are
   relative, and 1e5 sizes from the origin leaves 11 digits.
7. **The oracle is valid on 64 of 65 cases** (exactly checked; the one failure is the slot 0.1 alpha = 1 offset wide,
   where the offset walls cross: self-intersections, 6 triangles cross the input), with 4-8 times fewer triangles than K
   and a band of [1.00, 1.73] offsets ([0.52, 4.0] where the 4-offset miter cap applies, at creases below 29 degrees).

Stratum and cost boundary: procedural CAD-like solids only (planar faces, clean references); no Thingi10K run, because
the T1 numbers of K exist (#736: edge F 0.61 -> 0.875, concave within 1 offset 0.02 -> 0.75) and the oracle is undefined
there. Times are wall time of the pipeline in one process, 4 cores, with the metrics excluded.

## 7. How others solve it (prior-art check, papers only)

- Portaneri, Rouxel-Labbe, Hemmer, Cohen-Steiner, Alliez, *Alpha Wrapping with an Offset*, ACM TOG 41(4), 2022: the
  wrap; sharp features are outside its claims. Later offset papers state the same about it: wrapping "is not an
  offsetting method" and does not claim feature preservation (Zint et al. 2023; Cao et al. 2024).
- M. Beeren, *Improving 3D alpha wrapping by preserving concave features and reducing mesh complexity*, MSc thesis, TU
  Delft, 2026 (abstract only read; its code was not opened): concave features need a small global alpha; it adapts the
  refinement locally. Heuristic, no recovery guarantee stated in the abstract. Matches finding 4 (local alpha).
- Cheng, Dey, Ramos, *Delaunay refinement for piecewise smooth complexes*, SODA 2007 / DCG 2010; Cheng, Dey, Shewchuk,
  *Delaunay Mesh Generation*, 2012 (the chapter on piecewise smooth complexes); Dey, Levine, *Delaunay meshing of
  piecewise smooth complexes without expensive predicates*, Algorithms 2(4), 2009: protecting balls as weighted points,
  termination and topology recovery for any input angle; the input is a clean complex with known feature curves.
- Kobbelt, Botsch, Schwanecke, Seidel, *Feature sensitive surface extraction from volume data*, SIGGRAPH 2001; Attene,
  Falcidieno, Rossignac, Spagnuolo, *Sharpen&Bend*, IEEE TVCG 11(2), 2005: feature samples plus flips and splits on an
  existing mesh; the family of pass K; no validity or recovery guarantee.
- Zint, Maruani, Rouxel-Labbe, Alliez, *Feature-Preserving Offset Mesh Generation from Topology-Adapted Octrees*, CGF
  42(5), 2023; Cao et al., *Robust and Feature-Preserving Offset Meshing*, arXiv 2412.15564, 2024 (mitered offsets): sharp
  offsets by dual contouring / point-to-plane constructions, not by a wrap; validity by construction of the contouring
  plus repair, no recovery theorem.
- Wen et al., *Feature-Preserving Mesh Repair via Restricted Power Diagram*, SIGGRAPH 2025: feature lines kept by a
  restricted power diagram (weighted points again); validity needs a separate check.

Nothing found that proves feature recovery on an alpha wrap (searched: "alpha wrapping sharp feature guarantee",
"wrap protecting balls", "feature-preserving offset meshing"). The field's answer to "provable" is the weighted Delaunay
protection on clean complexes; its answer to dirty inputs is heuristic.

## 8. Options, ranked

| # | option | measured bound | effort | risk | constraints |
|---|---|---|---|---|---|
| 1 | **Write theorem (V) for pass K and close its gaps** (4.3: degenerate rest triangles, exact non-degeneracy of new triangles, the containment sentence with a test, the pipeline statement with the Reducer; optionally a certified lower band) | the guarantee itself; results unchanged (the band check costs triangles at convex creases only if switched on) | 2-4 days without the band; + about 1 week for the band (a new error bound: tier L review) | low | exact predicates only, serial: deterministic and 2^k-invariant as K is |
| 2 | **K follow-ups from this bound** (extends #1237): corner step with the checked move and the corners pinned in the simplification (corner 0 -> 0.81-0.96); a feature source that does not need the input's connectivity (weld: soups 0.00 -> the clean 0.92-0.96; creases of plane pairs for overlapping parts: 0.80-0.82 -> at most 0.94-0.96, unmeasured without the union); a recovery report per crease (the a-posteriori form of (R)) | clean: cov 0.945 -> 0.954, corners; soups: from nothing to the clean result; overlapping parts: at most + 0.14 | 1-2 weeks, tier M, one PR | medium-low (spurious creases on noisy soups: the gate needs a replacement; measure precision on T1) | opt-in stays; no default change |
| 3 | **Direct mitered offset with an exact certificate for clean closed inputs**, the wrap with K as the fallback (5.2 b) | cov 1, corners 1, band [1, 1.73], 4-8x fewer triangles; valid on 64 / 65 here | 1-2 weeks, tier M-L (new public operation or an option of `MeshOffset`: owner decision; vertices with more than three planes and capped miters need a rule) | medium (only clean inputs; the trigger "CAD wrapping" may already be served by using the input itself) | exact check a posteriori; deterministic; plane-intersection points in double need the rounding argument of the check, not of the construction |
| 4 | Planar-strip crease operation on the wrap (5.2 c) | at most cov 0.954 -> 1.0 and the fh tail, on features above 4 alphas | 2-3 weeks, tier L | medium-high (the precondition may fail where it matters, near corners) | as K |
| 5 | Local alpha at creases (the wrap's `AlphaField`) | the feature-size limit (finding 4); D1 of the first probe: Chamfer 0.75x at 1.7x triangles, 2.5x time | 2-3 weeks (TET_MESHING P4(a)) | medium-high (separation and angle bounds with a variable alpha are open) | - |
| 6 | Protecting balls / weighted Delaunay in the wrap (5.2 a) | the same as option 3, on the same clean inputs | months (regular triangulation, power predicate, a mitered oracle, new termination proof) | high | exact power predicate via `tools/PredicateCompiler` is feasible; everything else is new |
| 7 | Dual contouring in crease bands, stitched (5.2 d) | below option 3 | 1-2 months | high (DC topology not certified) | - |

**Leaning.** Option 1 now (it turns "works empirically" into a stated guarantee at almost no cost and answers the
"provably valid" half of HP 4). Option 2 when #1237 is scheduled: the corner step, welding before the feature search
(a soup gets no features at all today), and the report; the feature source for overlapping parts needs its own
measurement on more shapes first (two rows here). Option 3 is the
only route to *guaranteed recovery*, and it does not involve the wrap: decide it as a mitered-offset item (MESH_OFFSET
9.4), if a consumer needs exact creases on clean CAD. Options 4-7: not now; the plan's stop rule applies to 6 (months of
weighted-Delaunay infrastructure for a gain that option 3 gives directly on the same inputs).

The honest form of the HP 4 guarantee is then: *valid always (theorem V); recovered where the report says so; exact on
clean inputs through the direct construction*. An a-priori recovery theorem on dirty inputs is not on offer from any
known method.

## 9. Reproduce

```
dotnet build benchmarks/SharpFeatureProbe -c Release
dotnet run --no-build -c Release --project benchmarks/SharpFeatureProbe -- bound bound.jsonl box lshape octant slot vnotch chamfer gear finegear microgear fillet \
  box@rot ... fillet@rotfar twobox twobox@rotfar soup:lshape soup:gear soup:gear@rotfar wedge:20 ridge:20 ... ridge:150 notch:0.1 ... notch:8
python3 benchmarks/SharpFeatureProbe/bound_tables.py bound.jsonl
```

The full case list and the output are in [bound_tables.md](../../benchmarks/SharpFeatureProbe/results/bound_tables.md)
(master ed93ef26, 4-core container, about 100 minutes for the 65 cases; raw rows not committed).
