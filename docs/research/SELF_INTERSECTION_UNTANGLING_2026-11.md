# Local self-intersection untangling as a repair step (spike, 2026-11)

Study and measurement only: no library change. The question was whether a local untangling, which moves vertices and
keeps the connectivity, should run in `MeshRobustRepair` or `SolidBooleanPipeline` before the alpha wrap, because a
wrap rounds edges and changes the geometry by about alpha.

Benchmark: [benchmarks/SelfIntersectionUntangle](../../benchmarks/SelfIntersectionUntangle/README.md) (library
`dc572439`, 2026-11-22, Linux container, 4 cores, `net10.0`, Release). Sources:
[sources/meshing-and-repair.md](../sources/meshing-and-repair.md#local-self-intersection-untangling-research-spike-benchmarksselfintersectionuntangle-not-library-code).

## Result in short

- **No-go for untangling as a step before the wrap.** On 21 procedural self-intersecting solids in 2 frames, closed
  and with a hole (84 runs), `MeshRobustRepair` never reaches the solidify or the wrap: the exact resolve
  (`ExactMeshBoolean.Resolve`) repairs all of them, and its surface lies on the input's (distance at most the 1e-4
  measuring tolerance). On the Thingi10K 400 set, the recorded reason the exact stage failed for the meshes that were
  solidified or wrapped is never "self-intersections" (table below): they are open or not closed in the winding
  sense, which a method that keeps the connectivity cannot fix.
- **The premise does not hold on these inputs.** Untangling does not change less than the wrap. A simple prototype
  untangles 40 of 42 closed runs (exact check), but it has to move surface by about the penetration depth: its result
  lies 1.5 % of the diagonal from the input in the median and up to 17 %, against 0.27 % and at most 0.37 % for the
  wrap and 0 for the exact resolve.
- **What untangling does give is the topology and the connectivity.** The exact resolve keeps the solid where the
  winding number is positive, so an inverted region (a face pushed through the opposite wall) becomes a hole and an
  overlap is merged: only 14 of 34 repaired runs have the topology of the intent, against 34 of 34 untangled ones. None
  of the methods recovers the intent's geometry (the damage itself dominates the distance).
- **Sign information is necessary.** Local pushes alone (the minimum translation per intersecting triangle pair)
  untangle 22 of 42; with a global sign (the winding number in front of each triangle) 40 of 42. Diffusing the
  displacement changes no count.
- **Options** are in the last section. Leaning: nothing in the library now.

## 1. What the library already does (building blocks)

| Building block | What it gives | Relevance |
|---|---|---|
| `MeshRepairMetrics.SelfIntersectionPairs` / `HasSelfIntersections`, `MeshRobustRepair.NotCleanReason` / `FindDefects` | the exact intersecting triangle pairs; the judge of every result | used as the prototype's detection and as its only success criterion |
| `ExactMeshBoolean.Resolve` (stage 2 of `MeshRobustRepair`) | the boundary of the solid "winding number > 0", exactly, input triangles kept (Zhou et al. 2016) | the real baseline: it removes self-intersections with no surface deviation, but changes connectivity and, for inverted regions, topology |
| `SolidBooleanPipeline` / `ExactMeshBoolean` | take a closed self-intersecting operand as its winding solid; the pipeline refuses open, inconsistently oriented, inside-out or thin inputs | a closed self-intersecting input is never wrapped; it is refused only where a whole cell is inverted (measured below: 3 of 21) |
| `MeshWindingNumberSolidify`, `MeshAlphaWrap` | remeshing fallbacks (about a cell; alpha and offset) | reached only when the exact stage fails |
| `DMeshAABBTree3.FastWindingNumber` | winding number at a point | the sign of the prototype's push |
| `MeshRoundingRepair`, the closed separation prototype of the snap-rounding experiment ([SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md)) | vertex moves under an exact check after rounding | the same pattern (move, then let the exact check decide) at the scale of ulps; not a general untangler |
| `Reducer.PreventSelfIntersections`, `RemesherPro`, the El Topo notes in [RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md) | keep an intersection-free mesh intersection-free; proximity repulsion as a soft minimum separation for motion | they assume an intersection-free start; nothing in them removes an existing intersection |
| `FoldoverFreeParameterization` | 2D untangling of UV triangles (Garanzha et al. 2021) | flat domain, fixed boundary: no 3D counterpart |

## 2. Prior art (how others solve it)

What could be read: both ACM pages answer 403 and the authors' PDF hosts (`wonjongg.me`, `minarcik.com`) are blocked by
this environment's network policy; nothing was worked around. So the two papers of the brief are described from their
abstracts, the 2026 project page (which shows the two energies) and the prose of the repositories' READMEs. Their
method sections, results tables and limitations were **not** read and need a reading before any implementation brief.

- **W. Jang, Y. Jung, G. Lee, S. Lee, *Instant Self-Intersection Repair for 3D Meshes*, ACM TOG 44(4), art. 143,
  SIGGRAPH 2025, DOI 10.1145/3731427.** Abstract: static meshes have no motion or penetration-depth cue, so local
  contact handling is turned into a global repair by "local signed tangent-point energies and their gradient
  diffusion", solved with a momentum-based optimizer; connectivity is kept. README (prose only): a PyTorch / CUDA
  pipeline with a BVH collision query, at most 60 iterations by default, stops when the collision count is 0, and
  writes "the mesh at the iteration with fewest collisions", so success is a floating-point collision count, not an
  exact check. **Licence: none stated** (no LICENSE file, no licence section): unlicensed, paper only, code not read.
- **J. Minarčík, M. Liu, K. Crane, M. Li, *Untangling Surfaces via Shape and Mesh Repulsion*, ACM TOG 45(4), art. 163,
  SIGGRAPH 2026, DOI 10.1145/3811382.** Project page: a shape-level "Gaussian self-contact energy"
  `E_G = ∬ w(x, y) / ε² · exp(−|x − y|² / ε²) dx dy` over pairs that are close in space (the page does not define `w`),
  and a mesh-level penalty `E_M = Σ max(0, −φ(0, A − B))` over primitive pairs, the penetration depth as the distance
  of the origin to the Minkowski difference. Abstract: no change of connectivity; boundaries, non-manifold
  configurations and several objects are covered. README: CUDA only (NVIDIA GPU, cuDSS), a 343-case benchmark that
  includes the 60 cases of the 2025 paper. **Licence: Apache-2.0** (LICENSE and README), port-eligible under the project
  rules; the README notes that the binary links CGAL (GPL edition), so any CGAL-derived part stays off limits. No
  source file was read.
- **Tangent-point energy as a barrier:** C. Yu, C. Brakensiek, H. Schumacher, K. Crane, *Repulsive Surfaces*, ACM TOG
  40(6), 2021 (arXiv 2107.01664). The energy is infinite for a surface that is not embedded, so it keeps a clean surface
  clean and does not untangle; hence the "signed" and "local" variants of the 2025 paper.
- **The sign from global intersection analysis** (cited from memory, not re-read): D. Baraff, A. Witkin, M. Kass,
  *Untangling Cloth*, ACM TOG 22(3), 2003 (closed intersection curves decide which region is on the wrong side);
  P. Volino, N. Magnenat-Thalmann, *Resolving Surface Collisions through Intersection Contour Minimization*, ACM TOG
  25(3), 2006 (move vertices so that the intersection contours shrink). Both state that a local test cannot tell the
  two sides apart, which the measurement below shows for the local variant.
- **Exact resolution instead of motion:** M. Attene, *Direct repair of self-intersecting meshes*, Graphical Models 76,
  2014 (outer hull, connectivity changes); Q. Zhou et al., *Mesh Arrangements for Solid Geometry*, 2016 (what
  `ExactMeshBoolean.Resolve` implements). Established libraries (CGAL `autorefine` and corefinement, libigl / Cork,
  Manifold) take this route or refuse; none of them ships a connectivity-preserving untangler.
- **Minimum separation after an intersection-free state:** D. Harmon et al., *Interference-Aware Geometric Modeling*,
  2011; T. Brochu, R. Bridson, El Topo, 2009 (both already in the repository's notes).

## 3. The prototype (benchmark only)

`benchmarks/SelfIntersectionUntangle/Untangler.cs`, floating point, own formulation of the two ideas above; it moves
vertices only. Per round:

1. the exactly intersecting triangle pairs (`SelfIntersectionPairs`); none left: stop;
2. *signed push* (the global signal): the winding number just in front of a triangle should be 0. Where it is at least 1
   the triangle is buried in other material and retreats against its normal; where it is at most −1 it bounds an inverted
   region and advances. A quarter of its shortest edge per round;
3. *local push* (the mesh-level signal) for the pairs the signed push does not move: the smallest separating
   translation of the two triangles over the 17 separating-axis candidates, half to each, plus a margin; pairs that share
   a vertex or an edge (folds) pull their vertices towards the centroid of their one-ring;
4. optional *diffusion*: 4 averaging sweeps carry the displacement to the surrounding vertices.

After a clean state, a *pull-back* bisects the fraction of the total displacement and keeps the smallest one that is
still clean. Every success, also every step of the pull-back, is decided by `MeshRobustRepair.NotCleanReason == ""`
(exact self-intersection check), never by the pushes. Variants: `local` (steps 1, 3) and `signed` (1-3), each without
and with diffusion.

## 4. Measurement

Corpus: 21 closed, consistently oriented solids whose only defect is that they intersect themselves (the benchmark
checks this at every start, and that every intent is clean), in the canonical frame and under a generic rotation: 42
runs. Classes: `parts` (interpenetrating components and a helix tube whose turns overlap), `pushed` (a region of a fine
mesh pushed through the opposite side), `flap` (folded triangles and a folded ridge), `coplanar` (near-coplanar and
coplanar overlaps), `cad` (coarse parts with a face, a pocket floor or a corner pushed through). 17 cases have an
*intent*: the clean mesh the defect was injected into. The builders of `benchmarks/SolidBooleanAudit` are reused.

Distances are `MeshHausdorffDistance` upper bounds in units of the input's diagonal, measured to 1e-4.
`h(result→input)`: how far the result's surface lies from the input's. `h(input→result)`: how far input surface lies
from the result (surface that was removed or moved).

Clean results per class (`dotnet run --project benchmarks/SelfIntersectionUntangle -c Release`, raw output
`results/run1.txt`, tables by `summarize.py`):

| method | cad | coplanar | flap | parts | pushed | all |
|---|---|---|---|---|---|---|
| untangle local | 8 / 10 | 4 / 6 | 8 / 8 | 2 / 12 | 0 / 6 | 22 / 42 |
| untangle local + diffusion | 8 / 10 | 4 / 6 | 8 / 8 | 2 / 12 | 0 / 6 | 22 / 42 |
| untangle signed | 10 / 10 | 6 / 6 | 8 / 8 | 10 / 12 | 6 / 6 | 40 / 42 |
| untangle signed + diffusion | 10 / 10 | 6 / 6 | 8 / 8 | 10 / 12 | 6 / 6 | 40 / 42 |
| `MeshRobustRepair` (path: exact resolve in all 42) | 10 / 10 | 6 / 6 | 8 / 8 | 12 / 12 | 6 / 6 | 42 / 42 |
| `ExactMeshBoolean.Resolve` alone | 10 / 10 | 6 / 6 | 6 / 8 | 12 / 12 | 6 / 6 | 40 / 42 |
| solidify alone (resolution 128) | 10 / 10 | 6 / 6 | 8 / 8 | 12 / 12 | 6 / 6 | 42 / 42 |
| alpha wrap alone (1/300, 1/3000) | 10 / 10 | 6 / 6 | 8 / 8 | 12 / 12 | 6 / 6 | 42 / 42 |

Deviation of the clean results:

| method | clean | median h(result→input) | max h(result→input) | median h(input→result) | median H(intent) | topology of the intent kept | median ms |
|---|---|---|---|---|---|---|---|
| untangle signed | 40 | 0.0151 | 0.1715 | 0.0159 | 0.0394 | 34 / 34 | 42 |
| untangle local | 22 | 0.0060 | 0.0974 | 0.0113 | 0.0152 | 22 / 22 | 34 |
| `MeshRobustRepair` | 42 | 0.0001 | 0.0011 | 0.0521 | 0.0864 | 14 / 34 | 36 |
| solidify alone | 42 | 0.0030 | 0.0094 | 0.0546 | 0.0865 | 13 / 34 | 532 |
| alpha wrap alone | 42 | 0.0027 | 0.0037 | 0.0046 | 0.0743 | 26 / 34 | 1680 |

Readings:

- **The repair does not need the wrap here.** All 42 runs end in the exact resolve. With one triangle removed (a hole
  at the intersection or far from it, 42 runs, `-- open`, `results/open.txt`) all 42 again end in the exact resolve,
  clean. The plain `Resolve` leaves 2 runs not clean (the sphere with 10 % of its vertices moved 1.5 edges); the
  repair's further rounds finish them.
- **Untangling moves more surface than any baseline.** 18 of the 40 untangled runs stay within 1 % of the diagonal and
  7 within 0.1 %; the wrap stays within 0.37 % in all 42. The size of the move is the penetration depth, which the
  input fixes: the pull-back already takes the smallest clean fraction along the path. The two failures are the
  half-overlapping boxes: separating them needs a move of 19 % of the diagonal, and the centroid sign of a 12-triangle
  box says nothing.
- **Topology is the difference.** Where a region is inverted (pushed plates, the pocket floor, the sphere cap) the
  exact resolve cuts a through-hole (Euler characteristic 0 instead of 2), where parts overlap it merges them, and the
  noisy sphere becomes a genus-3 surface. The untangled mesh has the input's connectivity, so vertex attributes and
  correspondences survive.
- **Nobody recovers the intent.** Of the 34 runs with an intent that both finish, the untangled result is closer to
  the intent by more than 10 % in 8, farther in 6 and within 10 % in 20; the median volume error is 3.8 % (untangled)
  against 5.9 % (repair). Deep cases go wrong in volume: the pocket floor pushed through by 2 comes back with 1.36
  times the intent's volume, the box whose corner was pushed through with 0.08 times (repair: 0.92 and 0.16).
- **The sign decides.** `local` fails wherever an intersection curve is a closed loop around a region on the wrong
  side (all of `pushed`, the spheres, the helix): the per-pair minimum translation has no consistent direction there.
  Diffusion changes no count and no median beyond the third digit.
- **The pipeline** (`-- pipeline`, `results/pipeline.txt`: each solid united with a box far away, defaults): 17 of 21
  are `Success` and clean, 1 is `Success` with `Report.NotCleanReason` "self-intersections" (the same noisy sphere the
  plain `Resolve` leaves not clean), and 3 are refused with `InsideOutInput` (both pocket floors and the box corner: a
  whole cell of the input is inverted). No input is wrapped: the pipeline reaches `MeshRobustRepair` only for open
  inputs with `AutoRepairThenRobust`. All 4 are untangled clean by the signed variant, and all 4 are also repaired
  clean by `MeshRobustRepair` (exact resolve), which the caller can run today.
- **Open inputs:** after untangling, 37 of the 42 holed runs are repaired by the first stage (`MeshAutoRepair`, input
  triangles kept), 3 by the exact resolve, 2 are not untangled. Again no solidify and no wrap either way.
- Time: the same order as the repair on these sizes (12 to 3104 triangles); the wrap takes 0.8 to 4.5 s.

**Upper bound on real meshes.** The recorded stages of the Thingi10K 400 set
(`benchmarks/MeshRepairBenchmark/cases/robust_stages_2026-10-25.csv`, an older build; no mesh was fetched for this
spike) give the reason the exact stage failed for every mesh that went on:

| reached | meshes | exact: not closed | exact: not closed (winding number) | exact: volume differs from the input | exact: degenerate triangles | exact: self-intersections |
|---|---|---|---|---|---|---|
| solidify | 75 | 18 | 18 | 26 | 13 | 0 |
| alpha wrap | 31 | 15 | 10 | 2 | 4 | 0 |

An untangler closes no hole, so the 61 "not closed" meshes are out of its reach. The other 45 (6 of the 31 wraps) are
an upper bound, not an estimate: whether they are manifold enough to untangle, and whether the volume check would
accept the result, is not known without running them.

Limits of this measurement: procedural solids of at most 3104 triangles; a simple prototype, not the papers'
methods (their texts could not be read); one hole per open case; no scale frames (the prototype is not scale-invariant
work, only the judge is exact).

## 5. Go / no-go and options

**No-go** for the question as asked (an untangling step before the wrap in `MeshRobustRepair` or the pipeline): the wrap
is not reached because of self-intersections, and on the inputs where untangling works it deviates more than the wrap
and far more than the exact resolve that already runs. The pipeline's own gap on this corpus is 4 of 21 inputs (3
refused as inside-out, 1 result not clean), and `MeshRobustRepair` on the operand closes it without a new mechanism.

How others solve it: exact resolution with a change of connectivity (Attene 2014; Zhou et al. 2016; CGAL, Cork,
Manifold), which is what stage 2 does; connectivity-preserving untangling exists only as research code on GPUs
(Jang et al. 2025, unlicensed; Minarčík et al. 2026, Apache-2.0) and decides success by a floating-point collision count.

Options for the owner (a new option and any default are the owner's call; nothing below is built):

| | Option | Gain measured here | Cost / risk |
|---|---|---|---|
| A | **Nothing in the library** (leaning). Keep the benchmark and this note. | - | none |
| B | A standalone opt-in operation (not in the repair chain) for callers who must keep connectivity and topology, eg attributes or morph targets: the signed push with a displacement cap, success only by `NotCleanReason`, the input returned otherwise. | topology of the intent kept in 34 of 34 against 14 of 34; 40 of 42 clean | a new floating-point mechanism with no guarantee (2 failures; volume 0.08 to 1.36 times the intent's); needs the two papers read first, the invariance / reuse / determinism tests, and a real-mesh run; tier L |
| C | The same as an opt-in first stage of `MeshRobustRepair` (before the exact resolve, not before the wrap), with a cap such as 1 % of the diagonal. | under that cap 18 of 42 runs would be taken, the rest fall through unchanged | as B, plus a second mechanism competing with the exact resolve inside the repair, and a result that depends on the cap |
| D | Before B or C: a runner job on the Thingi10K 400 set that runs the prototype on the 45 meshes of the upper bound and counts those that end clean within the volume check. | decides whether B or C has any real-mesh case | one run; the benchmark needs a file-input mode first |

Leaning: A. D only if the owner sees a use for connectivity-preserving repair; B before C, because C puts an
unguaranteed stage in front of a guaranteed one. If the method sections of the two papers are wanted, the hosts
`wonjongg.me` and `minarcik.com` need to be allowed for a reading session, or the PDFs supplied.
