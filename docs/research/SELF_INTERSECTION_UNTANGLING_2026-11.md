# Local self-intersection untangling as a repair step (spike, 2026-11)

Study and measurement only: no library change. The question was whether a local untangling, which moves vertices and
keeps the connectivity, should run in `MeshRobustRepair` or `SolidBooleanPipeline` before the alpha wrap, because a
wrap rounds edges and changes the geometry by about alpha.

Benchmark: [benchmarks/SelfIntersectionUntangle](../../benchmarks/SelfIntersectionUntangle/README.md) (library
`dc572439`, 2026-11-22, Linux container, 4 cores, `net10.0`, Release). Sources:
[sources/meshing-and-repair.md](../sources/meshing-and-repair.md#local-self-intersection-untangling-research-spike-benchmarksselfintersectionuntangle-not-library-code).

## Result in short

- **No-go for untangling the input** (a step before the exact resolve, or instead of it). On 21 procedural
  self-intersecting solids, closed (2 frames, 42 runs) and with a hole (1 frame, 2 holes, 42 runs), `MeshRobustRepair`
  never reaches the solidify or the wrap: its exact resolve repairs all of them, and on the closed runs its surface
  lies within 0.11 % of the diagonal of the input's (the plain `ExactMeshBoolean.Resolve`, where it is clean: within
  the 1e-4 measuring tolerance). On the Thingi10K 400 set, the recorded reason the exact stage failed for the meshes
  that were solidified or wrapped is never "self-intersections" (table below): they are open or not closed in the
  winding sense, which a method that keeps the connectivity cannot fix.
- **One place is left open, and it is the one before the wrap: the solidify's own output.** In that table all 31
  wrapped meshes were wrapped because the solidify's contour, a closed mesh, intersects itself. A small procedural
  probe (thin walls solidified coarsely) gives 3 such contours in 72 runs; the prototype untangles all 3 in one round,
  moving vertices by at most 7e-11 of the diagonal: they are contacts, not deep crossings. Three cases decide nothing;
  whether the 31 real ones are of that kind needs a run on them (option D).
- **The premise does not hold on these inputs.** Untangling does not change less than the wrap. A simple prototype
  untangles 40 of 42 closed runs (exact check), but it has to move surface by about the penetration depth: its result
  lies 1.5 % of the diagonal from the input in the median and up to 17 %, against 0.27 % and at most 0.37 % for the
  wrap and at most 0.11 % for the repair (exact resolve).
- **What untangling does give is the topology and the connectivity.** The exact resolve keeps the solid where the
  winding number is positive, so an inverted region (a face pushed through the opposite wall) becomes a hole and an
  overlap is merged: only 14 of 34 repaired runs have the topology of the intent, against 34 of 34 untangled ones. None
  of the methods recovers the intent's geometry (the damage itself dominates the distance).
- **This prototype needs sign information.** Local pushes alone (the minimum translation per intersecting triangle pair)
  untangle 22 of 42; with a global sign (the winding number in front of each triangle) 40 of 42. Diffusing the
  displacement changes no count.
- **Where it adds something beyond the exact resolve:** the resolve already gives the intended solid in 11 of 21
  cases (unions, folds, coarse parts). In the other 10 winding > 0 is not the intent (6 inverted regions, 3 overlaps
  that should stay apart, 1 heavy noise): untangling keeps the topology in all 10 and is closer to the intent in 4.
- **Options** are in the last section. Leaning: nothing in the library now.

## 1. What the library already does (building blocks)

| Building block | What it gives | Relevance |
|---|---|---|
| `MeshRepairMetrics.SelfIntersectionPairs` / `HasSelfIntersections`, `MeshRobustRepair.NotCleanReason` / `FindDefects` | the exact intersecting triangle pairs; the judge of every result | used as the prototype's detection and as its only success criterion |
| `ExactMeshBoolean.Resolve` (stage 2 of `MeshRobustRepair`) | the boundary of the solid "winding number > 0", exactly, input triangles kept (Zhou et al. 2016) | the real baseline: it removes self-intersections with no surface deviation, but changes connectivity and, for inverted regions, topology |
| `SolidBooleanPipeline` / `ExactMeshBoolean` | take a closed self-intersecting operand as its winding solid; the pipeline refuses open, inconsistently oriented or inside-out inputs (thin ones only with `MinThickness` and `ThinInputs = Refuse`) | a closed self-intersecting input is never wrapped; it is refused only where a whole cell is inverted (measured below: 3 of 21) |
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
  25(3), 2006 (move vertices so that the intersection contours shrink). As remembered, both start from the observation
  that a local test cannot tell the two sides apart (not verified against the texts); the measurement below shows it for
  the local variant of this prototype.
- **Exact resolution instead of motion:** M. Attene, *Direct repair of self-intersecting meshes*, Graphical Models 76,
  2014 (outer hull, connectivity changes); Q. Zhou et al., *Mesh Arrangements for Solid Geometry*, 2016 (what
  `ExactMeshBoolean.Resolve` implements). The established libraries known to us (CGAL `autorefine` and corefinement,
  libigl / Cork, Manifold) take this route or refuse; no connectivity-preserving untangler was found in them (from their
  documentation as remembered, not searched exhaustively).
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

Corpus: 21 closed, consistently oriented solids whose only defect is that they intersect themselves (3 of them with
a whole cell inverted, which `FindDefects` also reports as inside-out components), in the canonical frame and under a
generic rotation: 42 runs. The benchmark checks this at every start in both frames, and that every intent is clean and
has the given mesh's vertex and triangle counts. The rotation rounds the coordinates, so the coplanar cases are exactly
coplanar only in the canonical frame (the pair counts differ between the frames). Classes: `parts` (interpenetrating components and a helix tube whose turns overlap), `pushed` (a region of a fine
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

| method | clean | median h(result→input) | max h(result→input) | median h(input→result) | median H(intent) | topology of the intent kept | median ms (all 42 runs) |
|---|---|---|---|---|---|---|---|
| untangle signed | 40 | 0.0151 | 0.1715 | 0.0159 | 0.0394 | 34 / 34 | 44 |
| untangle local | 22 | 0.0060 | 0.0974 | 0.0113 | 0.0152 | 22 / 22 | 34 |
| `MeshRobustRepair` | 42 | 0.0001 | 0.0011 | 0.0521 | 0.0864 | 14 / 34 | 38 |
| solidify alone | 42 | 0.0030 | 0.0094 | 0.0546 | 0.0865 | 13 / 34 | 546 |
| alpha wrap alone | 42 | 0.0027 | 0.0037 | 0.0046 | 0.0743 | 26 / 34 | 1813 |

Readings:

- **The repair does not need the wrap here.** All 42 runs end in the exact resolve. With one triangle removed (a hole
  at the intersection or far from it, 42 runs, `-- open`, `results/open.txt`) all 42 again end in the exact resolve,
  clean. The plain `Resolve` leaves 2 runs not clean (the sphere with 10 % of its vertices moved 1.5 edges); the
  repair's further rounds finish them.
- **Untangling moves more surface than any baseline.** The largest vertex move is within 1 % of the diagonal in 12 of
  the 40 untangled runs and within 0.1 % in 4, and reaches 24 % (by `h(result→input)`: 18 and 7 runs, at most 17 %);
  the wrap stays within 0.37 % in all 42. The size of the move is the penetration depth, which the
  input fixes: the pull-back already takes the smallest clean fraction along the path. The two failures are the
  half-overlapping boxes: separating them needs a move of 1 in a diagonal of √27, 19 % (arithmetic, not measured), and
  the centroid sign of a 12-triangle box says nothing.
- **Topology is the difference.** Where a region is inverted (pushed plates, the pocket floor, the sphere cap) the
  exact resolve cuts a through-hole (Euler characteristic 0 instead of 2), where parts overlap it merges them, and the
  noisy sphere becomes a genus-3 surface. The untangled mesh has the input's connectivity, so vertex attributes and
  correspondences survive.
- **Nobody recovers the intent.** Of the 34 runs with an intent that both finish, the untangled result is closer to
  the intent by more than 10 % in 8, farther in 6 and within 10 % in 20; the median volume error is 3.8 % (untangled)
  against 5.9 % (repair). Deep cases go wrong in volume: the pocket floor pushed through by 2 comes back with 1.36
  times the intent's volume (1.32 in the rotated frame), the box whose corner was pushed through with 0.08 times (repair: 0.92 and 0.16).
- **The sign decides.** `local` fails wherever an intersection curve is a closed loop around a region on the wrong
  side (all of `pushed`, the spheres, the helix, the overlapping boxes): the per-pair minimum translation has no
  consistent direction there. On the pocket floor pushed through by 2 it removes every intersection and leaves the
  mesh inside-out, which only the full `NotCleanReason` catches. Diffusion changes no count and the medians by at most
  0.0003.
- **The pipeline** (`-- pipeline`, `results/pipeline.txt`: each solid united with a box far away, defaults): 17 of 21
  are `Success` and clean, 1 is `Success` with `Report.NotCleanReason` "self-intersections" (the noisy sphere the
  plain `Resolve` also leaves not clean; the report says `OnlyContacts`, 0 crossing triangles), and 3 are refused with `InsideOutInput` (both pocket floors and the box corner: a
  whole cell of the input is inverted). No input is wrapped: the pipeline reaches `MeshRobustRepair` only for open
  inputs with `AutoRepairThenRobust`. All 4 are untangled clean by the signed variant, and all 4 are also repaired
  clean by `MeshRobustRepair` (exact resolve), which the caller can run today.
- **Open inputs:** after untangling, 37 of the 42 holed runs are repaired by the first stage (`MeshAutoRepair`, input
  triangles kept), 3 by the exact resolve, 2 are not untangled. Again no solidify and no wrap either way.
- Time: the same order as the repair on these sizes (12 to 3104 triangles); the wrap takes 0.7 to 4.4 s.

**Does the exact resolve already give the intended solid?** Per case, canonical frame (the rotated frame agrees in
every "kept / changed"; last table of `summarize.py`):

| group | cases | exact resolve (the repair) | what untangling adds |
|---|---|---|---|
| overlap that is meant as a union (no intent mesh: the boxes by half, both sphere pairs, the coplanar boxes) | 4 | the exact union, by definition the intended solid | nothing: it has to pull the parts apart (0.5 to 12 % of the diagonal; the boxes by half are not untangled) |
| folds and flaps with the solid intact (one vertex moved on the sphere and in the flat face, 10 % of the vertices by 0.8 edges, the folded ridge) | 4 | intended solid: topology kept, volume 0.99 to 1.00 of the intent | nothing (distance to the intent within 10 % of the repair's, or worse) |
| coarse CAD parts where no cell is cut off (boss edge, box corner, bracket) | 3 | topology kept; the volume is that of the damaged input (0.16 to 0.96) | nothing (volume 0.08 to 0.97, distance to the intent within 10 %) |
| **inverted regions: a face pushed through the opposite wall** (sphere cap, the three pushed plates, both pocket floors) | 6 | **not the intent: a through-hole** (Euler characteristic 0 instead of 2), volume 0.86 to 0.94 | the topology in 6 of 6; closer to the intent in 2 (plates: 0.019 against 0.040, 0.059 against 0.127), equal in 3, farther in 1 (pocket floor by 2: 0.185 against 0.121, volume 1.36) |
| overlap that is meant to stay apart (boxes overlapping by 1e-6, both helix tubes) | 3 | **not the intent: merged** (one component instead of two; a handle on the helix) | the topology in 3 of 3; closer in 2 (boxes: 0.0001 against 0.17; helix by 0.1 r: 0.017 against 0.020), equal in 1 |
| heavy noise (10 % of the vertices by 1.5 edges) | 1 | **not the intent: genus 3**, volume 1.00 | the topology; the distance to the intent is 12 % worse |

So the exact resolve gives the intended solid in 11 of 21 cases and untangling adds nothing there. It adds the
topology in the other 10 (winding > 0 is not the intent: inverted regions, parts merged that should stay apart), and a
geometry closer to the intent in 4 of those 10.

**Upper bound on real meshes.** The recorded stages of the Thingi10K 400 set
(`benchmarks/MeshRepairBenchmark/cases/robust_stages_2026-10-25.csv`, an older build; no mesh was fetched for this
spike) give the reason the exact stage failed for every mesh that went on:

| reached | meshes | exact: not closed | exact: not closed (winding number) | exact: volume differs from the input | exact: degenerate triangles | exact: self-intersections | solidify: self-intersections |
|---|---|---|---|---|---|---|---|
| solidify | 75 | 18 | 18 | 26 | 13 | 0 | - |
| alpha wrap | 31 | 15 | 10 | 2 | 4 | 0 | 31 |

*Untangling the input:* an untangler closes no hole, so the 61 "not closed" meshes are out of its reach. The other 45
(6 of the 31 wraps) are an upper bound, not an estimate: whether they are manifold enough to untangle, and whether the
volume check would accept the result, is not known without running them. (The first stage's reason was
"self-intersections" for 22 of the 75 and 6 of the 31; the exact stage then failed for the reasons above.)

*Untangling the solidify's output:* every one of the 31 wraps was taken because the solidify's contour intersects
itself. That contour is a closed mesh, so this is the one stage where an untangler has the input it needs, and its
upper bound is all 31 wraps (8 % of the 400 set). Probe (`-- solidify`, `results/solidify.txt`): 12 thin procedural
inputs (plates, crossing plates, fins, a comb, a hollow box, tubes, open sheets) in 2 frames at resolutions 24, 48 and
96, 72 solidifies. 3 results are closed with self-intersections as their only defect (4 to 20 intersecting pairs in
2222 to 79408 triangles); the signed prototype untangles all 3 in one round, with a largest vertex move of 2e-13 to
7e-11 of the diagonal (2e-11 to 7e-9 of a cell). These are contacts of the contour with itself, the kind
`MeshRobustRepair.SeparateTouchingVertices` and `MeshRoundingRepair` address at their stages, not interpenetrations.
Three cases are no basis for a decision, and the solidify inside `MeshRobustRepair` stops before its cleanup
(`StopIfSelfIntersecting`), so the real contours can differ from these.

Limits of this measurement: procedural solids of at most 3104 triangles; a simple prototype, not the papers'
methods (their texts could not be read); one hole per open case, in the rotated frame only; the pipeline mode in the
canonical frame only; no scale frames (the prototype is not scale-invariant work, only the judge is exact).

## 5. Go / no-go and options

**No-go for untangling the input** in `MeshRobustRepair` or the pipeline: the exact resolve already repairs every
self-intersecting solid of the corpus, the wrap is not reached because the *input* intersects itself, and where
untangling works it deviates more than the wrap and far more than the exact resolve. **Open, with a cheap next
measurement, for untangling the solidify's contour**, which is the stage directly before the wrap: all 31 recorded
wraps come from there, and the 3 procedural contours of that kind untangle with moves below 1e-10 of the diagonal. The pipeline's own gap on this corpus is 4 of 21 inputs (3
refused as inside-out, 1 result not clean), and `MeshRobustRepair` on the operand closes it without a new mechanism.

How others solve it: exact resolution with a change of connectivity (Attene 2014; Zhou et al. 2016; CGAL, Cork,
Manifold), which is what stage 2 does. Connectivity-preserving untangling was found only as research code on GPUs
(Jang et al. 2025, unlicensed, whose README decides success by a collision count; Minarčík et al. 2026, Apache-2.0,
success criterion not read); nothing else found (two searches: the two titles with "signed tangent-point energy", "gradient diffusion" and
"code"; not an exhaustive search).

Options for the owner (a new option and any default are the owner's call; nothing below is built):

| | Option | Gain measured here | Cost / risk |
|---|---|---|---|
| A | **Nothing in the library now** (leaning, together with D). Keep the benchmark and this note. | - | none |
| B | A standalone opt-in operation (not in the repair chain) for callers who must keep connectivity and topology, eg attributes or morph targets: the signed push with a displacement cap, success only by `NotCleanReason`, the input returned otherwise. | topology of the intent kept in 34 of 34 against 14 of 34; 40 of 42 clean | a new floating-point mechanism with no guarantee (2 failures; volume 0.08 to 1.36 times the intent's); needs the two papers read first, the invariance / reuse / determinism tests, and a real-mesh run; tier L |
| C | The same as an opt-in first stage of `MeshRobustRepair` (before the exact resolve), with a cap on the vertex move such as 1 % of the diagonal. | under that cap 12 of 42 runs would be taken, the rest fall through unchanged | as B, plus a second mechanism competing with the exact resolve inside the repair, and a result that depends on the cap |
| D | **Measure first** (leaning): a runner job on the Thingi10K meshes that reach the wrap. For each, take the solidify's self-intersecting contour as `MeshRobustRepair` has it, run the prototype and the existing contact separation (`SeparateTouchingVertices`) on it, and count the contours that end clean, with the size of the move. The same job can run the prototype on the 45 inputs of the first upper bound. | decides E, and whether B or C has any real-mesh case | one run; the benchmark needs a file-input mode and a hook on the contour (a benchmark-side copy of the stage, no library change) |
| E | Only if D says so: an opt-in step in `MeshRobustRepair` between the solidify and the wrap that separates the contour's contacts under a small cap, judged by `NotCleanReason`, the wrap as before otherwise. | upper bound: the 31 wraps of 400 (their median Hausdorff distance in that run was 2.4 % of the diagonal; a solidify deviates by about a cell); procedural: 3 of 3 contours, moves below 1e-10 of the diagonal | if the contours are contacts, the existing separation may already do it and no new mechanism is needed; if they are deep crossings, the numbers of section 4 apply and it is a no-go |

Leaning: A now and D as the next step, because D costs one run and is the only measurement that touches the wrap
path. B and C only if the owner sees a use for connectivity-preserving repair; B before C, because C puts an
unguaranteed stage in front of a guaranteed one. If the method sections of the two papers are wanted, the hosts
`wonjongg.me` and `minarcik.com` need to be allowed for a reading session, or the PDFs supplied.

## 6. Option D (decided 2026-11-22: A now, D as the measurement)

Set up in `benchmarks/SelfIntersectionUntangle/Contours.cs` and the manual workflow
`.github/workflows/untangle-contours.yml` (see the benchmark README): the contour is taken from the repair through
public members, so nothing in the library changes. First procedural rows through the repair itself
(`-- contour-probe`): 2 of 114 runs stop the solidify on a self-intersecting contour; both are untangled clean (largest
move 4e-11 and 1.2e-4 of the diagonal) and their finished solidify is clean, the contact separation fixes neither, and
one of the two is already clean after the solidify's own cleanup. A caution for option E: these inputs are open, and
the finished solidify lies 5.1 % of the diagonal from them against 0.37 % for the wrap. A clean solidify instead of the
wrap is then the worse repair, so the real-mesh run reports that distance next to the counts. The numbers of the
36 Thingi10K meshes follow after the first run of the workflow.
