# Integer (normal) coordinates for intrinsic triangulations: research spike (2026-11-22)

Study for plan #1284 (OPEN_TOPICS section 5, "Geodesics/param: integer (normal) coordinates"). No library code
changes; the prototype and all numbers are in
[benchmarks/IntrinsicIntegerCoordinates](../../benchmarks/IntrinsicIntegerCoordinates/README.md).

**Question.** Would integer normal coordinates, in place of or next to today's signposts, make the common
subdivision and the intrinsic Delaunay flips / refinement exact and robust, and at what cost?

**Short answer.** Yes for the common subdivision, partly for the rest:

- The connectivity of the common subdivision becomes exact for whatever triangulation the flips and insertions
  produced: 324 of 324 prototype runs pass their checks (section 4 says what these cover), where the traced
  subdivision of today succeeds in 188 of 324 and 11 of those successes contain false crossings.
- The flips and the refinement themselves do not become exact. The Delaunay test and the edge lengths stay floating
  point (the paper says so too). They become robust in the sense that no rounding can break the integers.
- Cost on procedural meshes: flips up to about 30 % slower (16 % on the largest), 8 bytes more per halfedge, the
  subdivision 10 to 46 times faster than today's tracing, insertions up to 10 times slower where edges are crossed
  often.

My leaning is at the end: fix the false crossings now (small), build the integer layer for flips and boundary
splits when the owner wants a robust common subdivision, and the insertion part only with a consumer.

## 1. The method, in our words

Source: Gillespie, Sharp, Crane, *Integer Coordinates for Intrinsic Geometry Processing*, ACM TOG (SIGGRAPH Asia)
2021, read as arXiv:2106.00220v1 (paper text only; no code was read, see "Sources and licences").

Signposts (Sharp, Soliman, Crane 2019; what `IntrinsicTriangulation` has today) store, per intrinsic halfedge, a
direction at its tail. The correspondence with the input mesh is recovered by walking: every intrinsic edge is
traced over the input triangles in floating point. Integer coordinates turn this around: the *input* edges are
curves drawn on the *intrinsic* triangulation, and each intrinsic edge only counts them.

| Stored | Where | Meaning |
|---|---|---|
| normal coordinate `n` | per intrinsic edge | how many input edges cross it; `-1` if it lies on an input edge |
| roundabout `r` | per halfedge leaving an input vertex | index (counter-clockwise around the tail) of the first input edge at or after the halfedge |
| length | per intrinsic edge | as today, floating point |

Because input edges are geodesics and do not cross each other, the counts on the three edges of a triangle fix how
the curves run through it: `c` arcs cut off each corner, and at most one corner has `e` arcs that start at it and
cross the opposite edge (paper eqs. 1 and 2). From this:

- **Flip.** The count of the new diagonal is a closed formula in the corner and emanating counts of the two old
  triangles (paper eq. 3), and the new roundabout is the neighbour's plus the arcs between them (eq. 4). No geometry.
- **Tracing.** A curve entering a triangle through crossing number `p` of an edge leaves through the left edge,
  the right edge, or ends at the opposite vertex, decided by comparing `p` with `c` (paper algorithm 1). So every
  input edge can be followed across the intrinsic triangles with integer comparisons only. This is the common
  subdivision's connectivity.
- **Positions.** The triangles a curve crosses are unfolded into the plane, the curve is the straight segment
  between its end vertices, and the crossings are its intersections with the unfolded edges. Floating point; the
  order along the curve is already known, so a computed position that contradicts it is clamped, not believed.
- **Insertion.** The new vertex lies in one region between the arcs of its triangle. Which one is a
  floating-point decision (side tests against the arcs, whose chords need the unfolding above); the counts of the
  three new edges follow from the region. Any region gives valid integers, so rounding can move the point across
  an arc but cannot produce an inconsistent state. Splitting an edge that lies on an input edge is purely
  combinatorial (paper eq. 6); other edge splits are an insertion plus a flip.

What is exact and what is not:

| Part | Signposts today | With integer coordinates |
|---|---|---|
| Subdivision connectivity | from floating-point walks; checked by areas, can fail or carry false crossings | exact for the current triangulation, by construction |
| Subdivision positions | floating point | floating point, ordered by the integers |
| Delaunay test, flip choice | floating point with a tolerance | the same (unchanged) |
| Edge lengths | floating point | the same |
| Location of an inserted vertex | floating-point walk, can fail | floating-point region test, cannot fail, may pick the neighbouring region |
| Tangent directions (vector heat method) | signposts | still signposts: integers carry no angles |

The paper states that exact predicates do not carry over, because an intrinsic length depends on arbitrarily many
input lengths. The same holds here: a flipped length is a nested square root of the input coordinates, so the
exact kernel (`math/Exact*`) does not apply and "exact intrinsic Delaunay" is not on offer from this method.

## 2. Fit to the library

Building blocks that exist (`mesh_ops/IntrinsicTriangulation*.cs`, `IntrinsicCommonSubdivision.cs`):

- The halfedge arrays (`hV`, `hTwin`, `hLen`, `hAngle`) and the slot moves of `FlipEdge`, `insert_in_face` and
  `split_edge`. Two `int` arrays next to them are enough; the prototype does exactly this from outside, through the
  public API.
- The result type `IntrinsicCommonSubdivision` (faces with input triangle, intrinsic triangle, barycentric
  corners). An integer extraction can fill the same type, so the API stays compatible with 1.0.324 and no default
  changes.
- `walk` / `trace_to` locate inserted vertices on the input today, and `insert_in_face` refuses the point when
  that walk does not arrive. With integers the location comes from the region's polygon instead (paper appendix
  B), so that dependency would go. Not prototyped, and no such refusal occurred in the runs here.
- Nothing in `VectorHeatMethod`, `MeshGeodesicDistance`, `MeshGeodesicPath` or `ExactMeshGeodesicDistance` needs
  the subdivision; they are not affected. The vector heat method needs the signposts, so signposts stay (a hybrid,
  which the paper also proposes).

Constraints:

- **CPU only, netstandard2.0 and net10.0:** plain `int` arithmetic and `double` square roots; nothing
  target-specific.
- **libm rule G3E0004:** the unfolding needs only `+ - * /` and `Math.Sqrt` (IEEE-exact), no trigonometric
  functions, so it is portable without `CoreMath`.
- **Bit-for-bit determinism:** today's code is serial and uses `CoreMath`, so it is already deterministic. The
  integers add that the connectivity no longer depends on the rounding of walks. Measured: the 2^-30 scale gives the
  same counts as the identity in 81 of 81 groups (27 meshes x 3 steps). A rotated or far-offset copy gives the same counts in 54 of 81;
  the rest differ because the flips differ at ties (grids) or an inserted point falls on the other side of an arc.
  That is a property of the floating-point Delaunay test, not of the representation, and integers do not remove it.
- **Integer width:** the sum of all `n` is the number of crossing vertices of the subdivision, which is indexed by
  `int` anyway, so `int` holds every `n` whenever the subdivision can be stored. The flip formula adds about ten
  of them; a real implementation computes it in `long` and checks. Largest `n` seen: 236 (section 4).
- **Not exact code:** the new arithmetic is integer counting, not error-bounded predicates, so the exact-code rules
  (banned symbols, mutation gate) do not apply; a normal tier-M review does.

## 3. Upper bound: how often does today's representation fail?

`dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates -- signpost`; raw output in
`benchmarks/IntrinsicIntegerCoordinates/results/run_2026-11-22.txt`. 27 adversarial procedural meshes (938 to 3818
vertices: 169-degree planes, ring spheres with spikes and nearly coincident vertices, sheared cylinders and tori,
grids stretched up to 10^6, cap triangles down to 10^-12, a folded strip), each under four similarity transforms
(identity, rotated, scaled by 2^-30, rotated and moved by 10^6). `ExtractCommonSubdivision` after `FlipToDelaunay`,
and after `DelaunayRefine(25)`:

| Step | Runs failed (`Inconsistent`) | Meshes with a failure | Reasons |
|---|---|---|---|
| flips only | 7 of 108 (6 %) | 3 of 27 | crossing traces 5, trace lost 2 |
| refined 25° | 16 of 108 (15 %) | 8 of 27 | trace lost 7, crossing traces 7, trace off its triangle 2 |

Details that matter:

- All 23 failures are reported (`Status = Inconsistent`), as the guide promises. The `Success` results of this
  stage (101 after flips, 92 after refinement) have no crossing vertex with other than four edges (the check of
  section 4); they were not compared with an integer result after refinement, which the prototype does not have.
- Three failures after flips are on the grids stretched by 10^4 and 10^6. These need no flip as given, but their
  rotated copies flip at ties (71, 370 and 436 flips in the failing runs).
- Eight of the nine meshes with a failure fail only for some of the four placements in at least one step. The
  traced result depends on where the mesh lies.
- For reference, the paper reports for its own signpost implementation on 7696 manifold Thingi10k meshes a success
  rate of 96.0 % after Delaunay flips and 69.1 % after refinement, against 100 % with integer coordinates. Our
  procedural numbers are of the same kind; we have not run a corpus.

So the gain is not small, and the spike went on to the prototype.

## 4. Prototype: integers on the same triangulations

`benchmarks/IntrinsicIntegerCoordinates/NormalCoordinates.cs` keeps `n` and `r` next to a library
`IntrinsicTriangulation`. The library executes every flip and insertion (so both representations describe the
identical triangulation, lengths bit for bit); the layer updates its integers and extracts the subdivision from
them. Implemented from the paper: flips, roundabouts, tracing, unfolding, polygons, insertion into a face. Not
implemented: edge splits, the refinement loop, the input location of inserted vertices.

What "valid" means. Every run checks:

- integers: each input edge, traced by integers, ends at the vertex its roundabouts name at both ends; every
  crossing and every input edge is used exactly once;
- complex: the polygons form a consistently oriented conforming complex with the input's Euler characteristic and
  boundary; every crossing vertex has four edges;
- geometry: the polygon areas sum to the mesh area (relative 10^-6, the tolerance of the library's own check;
  measured at most 2.9 x 10^-9);
- against the library, where the traced result is a `Success` without false crossings: the same numbers of
  crossings and faces and the same crossing positions (measured within 3.1 x 10^-10 of the bounding box diagonal).

Limits of these checks:

- Whether an inserted point went into the right region is covered by the area check and, in the insertion step,
  by the comparison with the library. In the near-edge step the library never succeeds, so only the area check
  judges the regions there.
- The end-vertex check compares vertex ids, which identifies an input edge only because `DMesh3` has no parallel
  edges.
- The meshes without flips (stretched grids as given, grid with ties, accordion) exercise nothing; the flip
  formula is exercised by the sheared and clustered meshes (largest `n` 71) and by the insertion steps.

Nine deliberate errors (flip count, flip roundabout, insertion count, trace rule, region test, unfolding side,
positions, polygon winding, fan order) are each caught: `benchmarks/IntrinsicIntegerCoordinates/mutants.sh`, output
in `results/mutants.txt`.

`dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates -- integer`, same 27 meshes and 4
transforms; "insertions" are V/2 random points in random triangles after the flips, then flips again:

| Step | Integer layer valid | Traced `Success` | of these with false crossings | Largest `n` | Longest input edge (crossings) | Positions clamped |
|---|---|---|---|---|---|---|
| flips | 108 of 108 | 101 of 108 | 0 | 71 | 41 | 0 |
| flips + insertions | 108 of 108 | 87 of 108 | 11 | 224 | 146 | 45 |
| flips + insertions, every fifth point 10^-7 from an edge | 108 of 108 | 0 of 108 | 0 | 236 | 166 | 3309 |

- Where the traced subdivision succeeds without false crossings (177 runs), both give the same number of
  crossings and faces, in every run.
- **False crossings (a finding about today's code).** In 11 runs `ExtractCommonSubdivision` reports `Success`
  with one to three extra subdivision vertices, 16 in all, each with three edges instead of four. The code path
  that produces this shape (`IntrinsicCommonSubdivision.cs`, the crossing loop and the `along` branch): a trace
  crosses an input edge of its head vertex just outside the snap tolerance, the crossing becomes a vertex, and the
  piece from there to the head is taken as running along that input edge. The faces still tile both
  triangulations (the area check passes), but the connectivity is not that of the common subdivision.
  Geometrically harmless, combinatorially wrong. It contradicts the sentence in
  [guides/geodesics-and-uv.md](../guides/geodesics-and-uv.md) that the operation reports `Inconsistent` "rather
  than emitting corrupt faces"; option 1 below is the fix. It occurred only after raw `InsertVertex` calls, not
  after `FlipToDelaunay` or `DelaunayRefine` (0 of 101 and 0 of 92).
- **Near-edge insertions.** A point at a barycentric distance of 10^-7 from an intrinsic edge is a legal argument
  of `InsertVertex`, and `DelaunayRefine` only snaps points within 10^-9. With such points the traced subdivision
  fails in all 108 runs. The integer layer stays valid; 3309 of about 3 million crossing positions had to be
  clamped to the integer order. (`InsertVertex` itself refused 231 of about 69 000 points in this step, in sliver
  triangles left by earlier near-edge points; that is its strict-inside test and has nothing to do with either
  representation.)
- "Positions clamped" counts crossings whose unfolded position contradicts the integer order or leaves its edge.
  Zero after flips alone. This is where floating point and the combinatorics disagree; signposts fail there,
  integers clamp.

## 5. Cost

`dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates -- cost`. Linux container, .NET 10, Release,
median of 5 (flips) or 3 runs after a warm-up; the container is shared, times below 5 ms vary by a factor of 2
between runs. Stratum: adversarial procedural meshes with many flips, not typical inputs.

| Mesh | V | Flips | Flips: library | Flips: + integers | V/2 insertions: library | V/2 insertions: + integers | Subdivision: traced | Subdivision: integers | Crossings |
|---|---|---|---|---|---|---|---|---|---|
| obtuse plane 12 x 120 | 1513 | 1368 | 0.7 ms | 0.7 ms | 1.7 ms | 1.9 ms | 14.1 ms | 1.3 ms | 1368 |
| ring sphere 160 x 24 | 3818 | 3480 | 2.0 ms | 2.6 ms | 3.5 ms | 3.7 ms | 54.0 ms | 5.0 ms | 3480 |
| sheared torus s=9 | 1280 | 6400 | 2.6 ms | 3.0 ms | 0.8 ms | 2.2 ms | 73.5 ms | 3.2 ms | 20 480 |
| obtuse plane 40 x 800 | 32 441 | 31 560 | 18.3 ms | 17.2 ms | 27.3 ms | 39.8 ms | 784 ms | 17.0 ms | 31 560 |
| sheared torus 200 x 160 | 32 000 | 800 000 | 321 ms | 371 ms | 31.7 ms | 306 ms | 15 571 ms | 633 ms | 3 072 000 |

- **Flips:** the integer update costs between nothing measurable and about 30 % on top of a library flip (which
  keeps computing its signposts here); on the two large meshes, the only rows above the timing noise, -6 % and
  +16 %. The flip counts are identical by construction; integers do not change which edges flip.
- **Memory:** two `int` per halfedge (8 bytes) next to the 24 bytes of today's halfedge arrays (`hV`, `hTwin`,
  `hLen`, `hAngle`; the triangulation also keeps the input layout and per-vertex arrays). The subdivision itself
  dominates when it is extracted (3 million crossings on the large torus). The paper reports the subdivision
  growing 20 times on average after refinement, and one case running out of memory.
- **Common subdivision:** 10 to 46 times faster than the traced one. Not all of that is the representation:
  today's extraction builds an arrangement per input triangle with dictionaries and sorts by angle, and the
  prototype does not yet attribute polygons to input triangles or compute barycentric corners (linear work, but
  not free). The connectivity alone takes a tenth to a half of the integer column.
- **Insertions:** the integer side must unfold whole curves to find the region (about 1 to 6 curves per point
  here, by binary search over the nested arcs; the paper unfolds all curves of the face). About 10 times a library
  insertion on the large torus, where every curve is long; 1 to 3 times on the others (millisecond timings). The
  paper reports the same direction for refinement (4 min against 1.5 min on its largest example).

## 6. How others solve it (prior art)

- **Explicit overlay** (Fisher, Springborn, Bobenko, Schröder, *An Algorithm for the Construction of Intrinsic
  Delaunay Triangulations with Applications to Digital Geometry Processing*, 2006): stores the subdivision itself
  and updates it at every flip. Always consistent, but every flip pays for the crossings, and insertions were not
  covered; the 2021 paper reports it at 100 % for flips only.
- **Signposts** (Sharp, Soliman, Crane 2019): what we have. Cheap flips and insertions, the correspondence by
  floating-point tracing, with the failure rates of section 3.
- **Integer coordinates** (Gillespie, Sharp, Crane 2021): this study. Normal coordinates themselves are older:
  Kneser and Haken's normal curves and surfaces, and the algorithms on compressed normal curves (Schaefer,
  Sedgwick, Štefankovič, *Algorithms for Normal Curves and Surfaces*, 2002; Erickson, Nayyeri, *Tracing Compressed
  Curves in Triangulated Surfaces*, 2013).
- **Consumers:** discrete conformal equivalence with Ptolemy flips (Gillespie, Springborn, Crane 2021) uses the
  same coordinates to carry the correspondence through its flips; intrinsic simplification (Liu, Gillespie,
  Chislett, Sharp, Jacobson, Crane 2023, roadmap Phase 5 #9) needs a correspondence that survives vertex removal,
  which the 2021 paper offers for inserted vertices only.
- **Exact arithmetic for the walks** is not used by anyone we found: the lengths are nested square roots, so it
  would need a real-algebraic number type, and the paper rules it out for the same reason.

## 7. Options

| # | Option | Gain | Effort | Risk |
|---|---|---|---|---|
| 0 | Leave as is | none; failures stay reported | none | the false crossings stay silent |
| 1 | Fix today's extraction: a crossing vertex that does not have four edges makes the result `Inconsistent` (or is repaired: a trace that crosses an input edge of its head vertex has arrived) | measured: the 11 silent cases of section 4 become reported or correct | S, one bugfix PR with the benchmark's valence check as a test | low; no API or default change |
| 2 | Integer layer for flips and boundary splits: `n` and `r` maintained in `FlipEdge` and in the split of a boundary edge (paper eq. 6, combinatorial), `ExtractCommonSubdivision` from the integers when nothing else was inserted, tracing otherwise | measured for flips: 101 of 108 to 108 of 108, 10 to 46 times faster, same result on every placement that flips alike. Expected, not prototyped: the same for the default pipeline (flips plus boundary splits) | M: about 400 lines in `mesh_ops` plus tests (the prototype is about 430); the paper's formulas are in the benchmark with checks | low to medium: flips get up to about 30 % slower unless the layer is opt-in; two extraction paths to keep in agreement |
| 3 | Option 2 plus insertion: region location, interior edge splits, input location of inserted vertices from the region | expected, not prototyped for refinement: refined subdivision from 92 of 108 to always valid (the paper reports 69.1 % to 100 % on its corpus). Measured for raw insertions: 87 of 108 (11 of them false) to 108 of 108 | L: region location and its geometry, edge split as insertion plus flip, new location code, removal of `trace_to` from the insert path; a refinement benchmark against today's | medium: insertions up to 10 times slower on heavily crossed triangulations; subdivision size after refinement (paper: 20 times the input on average) needs a size limit and a status; inserted positions can differ from today's in the last bits, which changes refinement output |
| 4 | Replace signposts by integers | none beyond 3 | L+ | high: the vector heat method needs tangent directions; not recommended |

Not offered: an exact intrinsic Delaunay test (section 1; no known method).

Owner decisions inside these: option 2 opt-in or always on (a flip-time cost for every user of
`IntrinsicTriangulation`, including the geodesic distance default); for option 3, whether refinement output may
change in the last bits and what the size limit of a refined subdivision is.

## 8. Leaning

1. **Option 1 now.** It is a defect of a public operation found by this spike, small, and independent of the rest.
   **Done (#1313, 2026-11-22):** a false crossing is reported as `Inconsistent`. On this benchmark the 11 runs
   change from `Success` to `Inconsistent` (flips + insertions: 76 of 108 `Success`, none with a false crossing);
   the other steps are unchanged. The tables above are the state before the fix.
   A second, rarer wrong `Success` found by the review of that fix (faces lost at coincident crossings: 21 wrong
   `Success` results in 300 000 single near-edge insertions of the generator in
   `IntrinsicCommonSubdivisionConsistencyTests`) is reported as `Inconsistent` since #1326; the counts of this
   benchmark are the same before and after.
2. **Option 2 when the owner wants a robust common subdivision**, as an opt-in layer first (no cost for the
   geodesic default). It is the cheap part of the paper, the measured cost is small, and it removes the
   extraction failures after flips in every run measured (the flips themselves still differ at ties).
3. **Option 3 only with a consumer** (texture or attribute transfer after refinement, geodesic cutting, intrinsic
   simplification). Most of today's failures are there, but so are the effort, the slowdown of insertions and the
   size problem, and no current operation needs a refined subdivision.

The plan's trigger ("an operation requiring a combinatorially exact common subdivision") still stands for
options 2 and 3; this study shows what such an operation would get and pay.

## Sources and licences

- Paper text only: arXiv:2106.00220v1 (arXiv non-exclusive licence), formulas re-derived and checked by the
  benchmark's invariants. One transcription doubt (the corner term of eq. 3 at the vertex `l`) was resolved by
  deriving the count in the quadrilateral, not from code.
- No source code of geometry-central, libigl, CGAL or any other geodesic or intrinsic-triangulation library was
  opened, in line with the session rule (paper only, even where the licence is MIT).
- Procedural meshes only; no mesh files, no network access at run time.
