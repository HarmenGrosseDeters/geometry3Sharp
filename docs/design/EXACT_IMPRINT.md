# Exact imprint: cutting intersection curves into a target mesh, exactly

Status: design note (tier L, step 1), 2026-11-16. No library code yet. Owner go for the project: 2026-11-16
([roadmap/exact-boolean.md](../roadmap/exact-boolean.md), "After 3.0", item 7). The note is reviewed by the coordinator
before the implementation starts; the implementation is one PR (section 7).

## 0. Summary

The exact imprint cuts the intersection curves of a *cutter* mesh into a *target* mesh: the target's triangles are
split along the curves, and nothing else changes. It replaces the float `MeshMeshCut` (failures: `ROBUSTNESS_SWEEP.md` R6)
as the robust way to do this. It is not a boolean: nothing is classified or removed.

The idea in one line: **run `ExactMeshArrangement` on {target, cutter} without self-intersections, triangulate only the
target's touched triangles, skip the classification, assemble the target's pieces with the target's own vertex ids, and
round the new vertices with a curve-preserving variant of SafeRounding.** Most of the machinery exists; the new parts are
(a) a "triangulate only these meshes" filter in the arrangement, (b) the assembly that keeps the target's vertices and
topology, (c) a locked-edge extension of `MeshRoundingRepair`, (d) the API class `ExactMeshImprint`.

Expected cost: about 30 to 60 % of an `ExactMeshBoolean` on the same pair (no classification, no cutter triangulation,
no `SimplifyFlatRegions`), with the rounding as the part that decides (section 6).

## 1. Building blocks: what exists, what is new

Searched in the library (`mesh_ops/`, `math/`, `intersection/`, the tests) on master 2014e65.

| Need | Existing piece | Verdict |
|---|---|---|
| Pair search between the meshes | `ExactMeshArrangement.find_intersections`, `TriangleBoxTree`, `TouchOnly` (`touch_only`), `SelfIntersections = false` (cross-mesh pairs only) | reuse as is |
| Intersection segments and points as implicit points (edge-plane, three planes), merged points | `AddSegment` / `AddPoint`, `PointKey`, `merge_points`, `Canonical`, `ExactPoint3` | reuse as is |
| Coplanar overlap (cutter triangle in the plane of a target triangle) | `InputTriangle.Coplanar`, coplanar-edge crossings (`SegmentsPointImplicit`), constraint segments of the overlap outline | reuse; the semantics is an option (3.4) |
| Triangulation of a touched triangle (exact constrained Delaunay in a projection, segments split at the points on them) | `triangulate_touched`, `ExactTriangulation2`, `ConstraintEdges` (the curve edges as point-id pairs) | reuse; new: a filter so that only the target's triangles are triangulated |
| Early-out for triangles that cannot matter | `CullOutsideOf` / `CullTest` (Boolean) | not used (it culls by inside / outside of a closed mesh); a cutter triangle with no pair is already free |
| Inside / outside classification | `ExactMeshBoolean.classify`, winding numbers, rays | **not needed** (v1). It is 66 to 82 % of the time of the slow booleans, and the main reason the imprint is cheaper |
| Output assembly: corner slots joined across edges, so duplicated vertices of non-manifold contacts stay split (`assemble`) | `ExactMeshBoolean.assemble` (pattern) | new variant: target topology instead of solid topology (section 4.1) |
| New vertices on flat regions removed (`SimplifyFlatRegions`, `ExactPlanarSimplify`) | exists | **off for the imprint**: every new point is on a curve and must stay |
| Rounding with a guarantee | `MeshRoundingRepair` (`SafeRounding`): bound `MaxDisplacement` 1e-5 of the diagonal, drift rule, `FixedVertexF`, exact verification of the doubles, `IsClean`, `Counts` | reuse with an extension: locked edges (4.3) |
| Exact vertex coordinates | `ComputeExactVertices` / `ResultExactVertices` | reuse |
| Status, budget, cancellation, counts | `BooleanStatus`, `MaxPieces` / `PredictedPieces`, `Cancellation`, `Counts`, `PhaseMilliseconds` | reuse the patterns, own enum |
| Invariance / reuse / determinism harness | `Harness/Invariance.cs` (`Similarity.All()`), `InvarianceTests`, `ExactArrangementScalingTests` | reuse |
| Float baseline and its tests | `MeshMeshCut`, `MeshMeshCutContactTests` (+ `Base`), `RefuseContact`, `PlaneCutScaleTests` | oracle and R6 cases |

What the boolean does that the imprint must not copy: `ReorientedTriangles` (the imprint keeps the target's orientation),
welding by position (`VertexPoints` welds input vertices at one position to one point; the imprint maps back to vertex ids
and must not weld the target, see 4.1), and the cutter's own triangulation.

**The one change to the exact core** (the only edit in `ExactMeshArrangement`, so the exact-code review applies): an
internal `TriangulateMeshes` mask (default: all). `Compute()` runs `triangulate(ti)` only for triangles of the listed
meshes; the others stay as one untriangulated piece and are not output. `predict_pieces` and `MaxPieces` count only the
listed meshes. Triangulation is per input triangle and independent, so the target's pieces are bit-identical to what
the boolean's arrangement gives for the same triangle (the property the equality test of section 5 checks). No new
arithmetic, no new bound, no new parallel loop.

## 2. Prior art (papers only; no code read)

- **White, Saigal, Owen, 2004**, *An imprint and merge algorithm incorporating geometric tolerances for conformal meshing of
  misaligned assemblies*, IJNME 59(14) (author list as cited in `sources-surveys.md`; the search found it under White).
  The classic engineering imprint: boundary edges of faces are discretized and intersected with size tolerances, then merged.
  Tolerant, not exact; it is the behaviour of `MeshMeshCut` (absolute `VertexSnapTol`) and the thing to *not* reproduce.
  It does define the use case: conformal interfaces for a volume mesher. Read the abstract only.
- **Smith, Tautges, Wilson, 2010**, faceted sealing (cited in the Gao et al. 2025 survey entry). Same use case, tolerance based.
- **Campen and Kobbelt, 2010**, *Exact and Robust (Self-)Intersections for Polygonal Meshes*, CGF 29(2). An exact co-refinement
  by an octree and BSPs with fixed precision, with the imprint as a by-product of booleans; the output is exact (rational
  or plane-based). Abstract read.
- **Zhou, Grinspun, Zorin, Jacobson, 2016**, *Mesh Arrangements for Solid Geometry* (SIGGRAPH). The arrangement `ExactMeshArrangement`
  implements; the imprint is the arrangement before the winding-number step.
- **Cherchi, Livesu, Scateni, Attene, 2020**, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*, and **Cherchi,
  Pellacini, Attene, Livesu, 2022**, *Interactive and Robust Mesh Booleans*. Implicit points with filters (ours); they keep the
  arrangement's output as a conformal mesh and classify afterwards. Their pipeline has "the arrangement" as a separable stage,
  which is the same cut we make. As far as the abstracts say, rounding the implicit points back to floats is outside their
  guarantees (the gap SafeRounding closes for us; the papers' text was not read for this).
- **Lévy, 2024 / 2025**, *Exact predicates, exact constructions and combinatorics for mesh CSG* (arXiv 2405.12949, TOG 2025). Computes
  the co-refinement (exact intersection points, the "Weiler model") as a first stage and separates it from the classification
  and the output. Confirms the staging. Abstract read only; no accompanying code was opened.
- **Milenkovic and Sacks, 2019**, geometric rounding and feature separation in meshes (CAD); **Devillers, Lazard, Lenhart, 2018**
  (rounding meshes in 3D, worst case O(n^13)); **Hobby, 1999** (snap rounding). Already in `SOURCES.md`; they are the base of
  SafeRounding and of the rounding question (section 4).

**What others do that we do not copy:** a tolerance (White, Smith: merge within a distance) and the classification-free output
as soup. **What nothing found does:** an imprint whose output guarantees (target orientation, vertices, topology, curve edges
present as mesh edges after rounding) are stated and checked against the rounding. That is the new part, and it is the
rounding extension, not the arrangement. No GPL / MPL / LGPL source was opened. New `SOURCES.md` rows: Campen and Kobbelt
2010, Lévy 2025, White et al. 2004 (reference only).

## 3. API

Compatible with upstream 1.0.324: only new public types, no change to an existing signature (`dotnet pack` validates).
`MeshMeshCut` is not touched (the deprecation is decided only after the imprint exists and has run).

### 3.1 Class

```csharp
public class ExactMeshImprint
{
    public enum ImprintStatus { Success = 0, InvalidInput = 1, NotComputed = 2, RoundingNotClean = 3, BudgetExceeded = 4, Cancelled = 5 }

    // inputs (not modified) and options: public fields, as ExactMeshBoolean
    public DMesh3 Target;
    public List<DMesh3> Cutters;              // or Cutter for the common single mesh
    public CoplanarModes CoplanarMode;        // 3.4
    public ContactModes ContactMode;          // 3.4
    public bool LabelCurves;                  // 3.3
    public bool SafeRounding = true, StrictRounding;   // and RoundingMaxDisplacement, RoundingUlpFactor,
                                                       // RoundingAbsoluteTolerance, RoundingMaxPairTests as there
    public bool ComputeExactVertices;
    public long MaxPieces;
    public System.Threading.CancellationToken Cancellation;

    public bool Compute();                    // true iff Status == Success; callable again
    // results
    public ImprintStatus Status;
    public string Error;
    public DMesh3 Result;                     // the imprinted target (a new mesh), null on failure
    public int[] SourceTriangle;              // per Result triangle id: its Target triangle
    public HashSet<long> CurveEdges;          // 3.3: undirected Result vertex pairs (EdgeKey)
    public int[] CurveId;                     // 3.3, with LabelCurves
    public MeshRoundingRepair Rounding;       // and RoundingClean, as ExactMeshBoolean
    public ExactPoint3[] ResultExactVertices; // with ComputeExactVertices
    public Dictionary<string, long> Counts;   // splits, new vertices, curve edges, contact points, rounding counts
}
```

Same shape as `ExactMeshBoolean` (inputs as fields, `Compute()`, `Result`, `Status`, `Error`, `Counts`). A static
convenience `ExactMeshImprint.Imprint(DMesh3 target, DMesh3 cutter)` returns the result or null. Several cutters
(`List<DMesh3> Cutters`) are the same arrangement with more cutter meshes, and cost nothing extra in the design; v1
exposes the list (one cutter is the common case, the cutters do not cut each other into the target's pieces *unless they
intersect on the target*: curves of all cutters are cut in, and the crossing points of cutter curves inside a target triangle
are vertices, exactly as in the arrangement).

"In place" (as `MeshMeshCut`): `Result` is a new mesh; `ApplyTo(DMesh3 target)` / `Compute(inPlace: true)` replaces the
content of `Target` (`Target.Copy`-style, vertex ids kept by construction). Owner question Q2.

### 3.2 Output: the target alone, split

`Result` is the target with its triangles split:
- vertices: **every target vertex keeps its id and its coordinates**; new vertices get the next ids in a deterministic order
  (by the exact point order `ComparePoints`, not by thread-dependent point ids). Vertices the target had but no triangle
  uses stay (an isolated vertex is kept).
- triangles: an untouched target triangle keeps its triangle id, its vertices and its orientation. A touched triangle is
  replaced by its pieces: the piece list is in a deterministic order (by the triangulation's order, as the arrangement),
  the pieces get new triangle ids; **the triangle group id of the source triangle is copied to its pieces** (`SetTriangleGroup`;
  the target's groups survive). `SourceTriangle[tid]`: per result triangle, the target triangle it came from (an `int[]`).
- orientation: all pieces are oriented as their source triangle. No reorientation (the boolean's `ReorientedTriangles`
  step is not run).
- normals / colors / UVs: not carried (as `ExactMeshBoolean`; vertex attributes of new vertices have no obvious value).
  A caller that needs them interpolates from `SourceTriangle` and the exact barycentrics, see Q3 (parked).
- topology kept: a target edge that is shared in `Target` is shared in `Result` (also where it is split by new vertices); a
  boundary edge stays a boundary edge (split by vertices, still boundary); a seam where two vertices at the same position are
  not welded stays unwelded, in the exact point and after rounding (4.1). So **closed and manifold target gives a closed and manifold
  result**, an open target gives a result with the same boundary loops (split by vertices).

### 3.3 The curve edges

`CurveEdges`: the result's edges that lie on the intersection curves (an edge set of `Result` edge ids, or a
`HashSet<long>` of vertex pairs, plus `CurveEdgeGroup` option). It comes from the arrangement's `ConstraintEdges` (point-id
pairs), mapped to result vertices. Options:
- `LabelCurves = true` (default false): additionally an `int[] CurveId` per curve edge, the id of a connected curve
  (chain of curve edges through vertices of curve valence 2; a curve that closes, or ends at the target's boundary). The
  cheap union-find over `CurveEdges`. A group id on the *triangles* per side of the curve is **not** in v1 (that is the
  classification; Q1).
- Curve edges are edges of both pieces on either side. A curve that runs along an existing target edge (the cutter's
  intersection with the target touches the target edge, e.g. a target edge lying in a cutter face) produces a curve edge
  that is a target edge: it is reported in `CurveEdges` too (the imprint "is" there).

### 3.4 Coincident and coplanar parts

The arrangement splits at the outline of a coplanar overlap and at the overlap of coincident triangles. For the imprint:
- `CoplanarMode` (an enum): **`Outline` (default)**: the boundary of the overlap region (cutter triangle edges inside the target
  triangle, target edges inside the cutter triangle) is imprinted, so the overlap region is a union of result triangles
  (what the arrangement gives, and the curve of a boolean with a coplanar contact). `Ignore`: coplanar pairs are not cut
  (the curve of the *transversal* intersection only; the safe equivalent of what `MeshMeshCut` does, which imprints only
  intersection segments). Both are exact decisions.
- Vertices of the cutter on the target's faces and edges (contact) are curves of zero length: a point where the cutter
  touches the target at a vertex imprints that **point** as a vertex of the target (a vertex inside the face, connected by the
  constrained triangulation). This is the contact class `RefuseContact` refuses in the float cut; the exact imprint handles it
  and says so in `Counts["contact_points"]`. `ContactMode { Imprint (default), Ignore }` (ignore: points that are not
  endpoints of a curve edge are not inserted). Whether touching a target at a single point should imprint that vertex is
  a judgement call: default Imprint (the arrangement's behaviour, and conformal meshes need it), Q4.
- Coincident triangles (same plane, same area): in `Outline` mode no new vertices result; the target is unchanged there.

### 3.5 Open and defective input

- **Open target:** allowed (the point of imprinting a CAD patch). Boundary loops preserved (3.2). No inside / outside needed.
- **Open or self-intersecting cutter:** allowed; the cutter's own self-intersections are not computed (`SelfIntersections =
  false`), only the cutter's intersection with the target. Where the cutter overlaps itself at the target, the same curve is
  found twice and merged (equal point ids, `merge_points`). A cutter with degenerate (zero-area) triangles: those are
  ignored (as the arrangement: `Degenerate`).
- **Self-intersecting or defective target:** the target is **not** repaired (the imprint "keeps its shape"). Its own
  self-intersections are not computed and not removed; they stay as they are in the result. An imprint on a target that
  self-intersects is exact for the pairs target/cutter; two pieces of the target that cross each other each get their cuts
  from the cutter independently. `Result` is then not a manifold-embedded surface, and `Counts["target_self_intersections"]`
  is not computed (a full search; off). An option `CheckTargetSelfIntersections` (default false) runs it for the diagnosis.
- Degenerate target triangles stay as they are (a zero-area target triangle is kept and not split). Not finite coordinates:
  `InvalidInput`, `Target` / `Cutter` null: `InvalidInput`.
- Empty imprint (no intersection): `Success`, `Result` a copy of the target, `CurveEdges` empty.

## 4. Output and rounding

### 4.1 Exact vertices and topology

Every output vertex is an exact point (an input double, or an implicit point of the arrangement). The assembly follows the
boolean's corner-slot pattern, with different joins: a corner slot (a triangle's corner at a point) is joined to the slot of
the neighbour across an edge that the **target** shares (two target triangles on the edge, by the target's edge, not by
position). Slots that stay unjoined are separate vertices, so unwelded seams stay seams and duplicated target vertices keep their ids. A
slot at a point that is an input vertex position takes the vertex id of the source triangle's corner. Result: no vertex
merging beyond what the target already has, and the new vertices of a split shared edge are shared.

### 4.2 Rounding of the new vertices

A new vertex is rational. Rounding to the nearest double moves it by up to half an ulp of each coordinate. The effects:
1. the vertex is no longer exactly on the target's plane: the surface moves by at most the rounding error (one to a few ulp
   of the coordinates, relative about 1e-16 of the coordinate magnitude); that is the "target moves off its surface" cost, and
   it is of the same size as the output of `ExactMeshBoolean` with `SafeRounding`;
2. nearby new vertices can coincide after rounding (degenerate triangles) or cross others (tiny self-intersections of the
   result with itself, or with other target parts within a ulp);
3. a new vertex of a split target edge is no longer exactly on the edge; the two triangles at the edge share the vertex, so
   no gap opens (closed and manifold are combinatorial: they hold exactly, not only up to rounding).

SafeRounding (`MeshRoundingRepair`) repairs 2 by contractions, flips and tiny moves, verified with exact predicates on the doubles,
within `MaxDisplacement` (1e-5 of the diagonal), and keeps input vertices (`FixedVertexF`) where possible. Used by the imprint
with these changes:

1. **Fixed vertices:** all target vertices are fixed (`FixedVertexF`): the imprint "keeps the vertices it already has", also
   where the coordinates are coarse (`FixedWhereCoarse = true`).
2. **Locked edges (new, in `MeshRoundingRepair`):** a `LockedEdgeF(int a, int b)` predicate. A locked edge is not flipped,
   not zipped across, and not contracted, and no edit moves its end vertices so that the edge stops being an edge. Locked:
   all curve edges (`CurveEdges`), and, to keep the target's topology, the edges between fixed vertices. Why: without it a
   flip can remove a curve edge (the curve is then no longer in the mesh), and a contraction can merge the two sides of a
   boundary. The edits that remain (contractions of non-curve edges at new vertices, tiny vertex moves of new vertices) are
   the ones around the *interior* of the pieces. A vertex of a locked edge that is new may still move along nothing (it is
   locked in place) or be repaired only by moving *other* vertices: consequence, **more defects can remain than in the boolean**;
   `RoundingClean` tells, and Status handling below is the guard.
3. **No repair of pre-existing defects:** edits only where an imprint triangle or vertex is in the defect's neighbourhood. The
   target's own intersections and degenerate triangles are not counted as defects of the imprint (the initial counts are
   taken of the target alone, `Counts["target_initial_intersections"]`; clean means *no more than the target had*).
   `IsClean` for the imprint = no new intersection, no new degenerate triangle (the threshold area of `NotCleanReason`, relative
   to the largest component), no false contacts. For a clean target this is the boolean's `IsClean`.
4. Statuses: `Success` with `RoundingClean` false means defects remain (as the boolean, Counts say which). `StrictRounding`
   gives `RoundingNotClean` and `Result = null`.

**Guarantees** (stated, each tested in section 5):
- G1 Combinatorial: closed and manifold target gives a closed, manifold result (exact, independent of rounding); boundary loops of an
  open target are the same loops, split by vertices; orientation of every piece equals its source triangle's; target vertex ids
  and coordinates unchanged; untouched triangles unchanged.
- G2 Exactness before rounding: the set of imprinted curves (as exact points and constraint edges) equals the arrangement's
  intersection curves of target and cutter (the boolean's `ConstraintEdges` restricted to the target's triangles).
- G3 Geometric: with rounding, every result point is within `max(MaxDisplacement * diagonal, UlpFactor * ulp)` of the target's
  surface (the same bound as SafeRounding). For results where no edit was made the distance is at most a rounding unit.
- G4 Verified: with `SafeRounding`, if `RoundingClean`, the result has no self-intersection and no degenerate triangle that the
  target had not (exact check on the doubles).
- G5 Curve edges: if `RoundingClean`, every curve edge of the exact imprint is an edge of `Result`, or its two ends
  have been merged by an edit that is reported (`Counts["curve_edges_contracted"]`); with locked edges the second case is 0 unless
  `AllowCurveContraction` (default false, then a defect stays instead).

What is **not** guaranteed: that the rounded curve lies on the cutter (the rounded curve is near the exact one, within the same
bound; the cutter is not modified, so a re-imprint of the result with the same cutter would find new, tiny curves: the
imprint is idempotent only in exact arithmetic, `ComputeExactVertices` gives exact coordinates for that use); that a result
of an imprint on a non-manifold target is manifold.

### 4.3 Why not exact output only

Exact rational output (`ResultExactVertices`) is offered (`ComputeExactVertices`) but not the default: every consumer in
the library takes `DMesh3` with doubles. The Boolean-4.0 idea (exact output with provenance) is parked; the imprint's exact
points are the same data and would use it if it is ever built.

## 5. Test plan

The rules of `CLAUDE.md` apply: each test fails on a mutant, invariance over `Similarity.All()` (the 2^k scales bit for
bit), a reuse test, determinism, time limits (5 s per test; the corpus-like checks use procedural inputs).

1. **Invariance** (`ExactImprintInvarianceTests`): for each procedural pair, `Similarity.All()` (Identity, UTM, 2^k small and
   large, millimetre, kilometre, rotated, rotated + UTM + mm): the combinatorics (triangle count, vertex count, curve edge
   count, status) are equal; for the exact power-of-two scalings the result is equal bit for bit after scaling back
   (`Invariance.cs` comparison); rotations equal up to the rounding resolution (`Resolution(size)`), the combinatorics equal
   (the exact decisions are similarity invariant for exact similarities).
2. **Reuse:** `Compute()` twice on one instance (and after changing `Target` / `Cutter`) gives equal results and equal counts;
   a failed first compute (invalid input) does not leak into the second.
3. **Determinism:** parallel equals serial (`gParallel` on and off) and 10 repeated runs: identical result meshes (vertex
   order by the exact point order, as in 3.2) and `CurveEdges`. A salted `HashSalt` run (as `ExactMeshArrangement` tests) gives the
   same result.
4. **Procedural cases** (all generated, no mesh files): cube cut by a plane quad, by a sphere, by a tilted cube; two spheres;
   torus and cylinder; the transversal grid cut; cutter vertex on a target face, on a target edge, on a target vertex; cutter edge on a face; coplanar
   overlap (both `CoplanarMode`s); coincident triangles; cutter passing exactly through target vertices; many-curve
   crossings (cutter curves crossing inside a target triangle); an open target (a plane patch with a boundary crossed by the cutter); an open
   cutter (a single triangle, a strip); a cutter with a self-intersection; a degenerate cutter triangle; a duplicate-seam target. Each success
   path checks the result with `MeshRobustRepair.NotCleanReason` (closed, manifold, finite), not only the status; open
   targets check "same boundary loops".
5. **The R6 failures of `MeshMeshCut`** (`ROBUSTNESS_SWEEP.md` R6; scale 1e-3 and 2^-10: "unsortable vertex"; 2^-24: exception in
   `find_intersections`; rotated: 498 vs 504 triangles; rotated + 1e-3 + UTM: hang) and the contact cases of `MeshMeshCutContactTests`:
   the same inputs (extract the generators from that test file into a shared `Harness` helper) cut by `ExactMeshImprint`:
   success, same triangle count at every similarity, and the random-closed-cuts-of-a-cube class of issue 856 (a few hundred
   seeds, small cubes: 0 failures, 0 scale dependence where the float cut had 0.2 % / 0.8 %).
6. **Comparison to the boolean's own curves** (the oracle by definition that shares the arrangement): for each pair, the
   imprint's curve set equals the arrangement's intersection curves: build an `ExactMeshArrangement` of {target, cutter}, take `ConstraintEdges`,
   and compare as sets of exact point pairs with the imprint's `CurveEdges` mapped to exact points
   (`ResultExactVertices`). Also: the union of the imprint's pieces, rounded or not, has equal total *exact* area to the
   target (no area gained or lost: sum of exact triangle areas of the pieces of each source triangle equals the source's
   area, over rationals); and the boolean's result pieces from the target (the pieces that `ExactMeshBoolean` keeps of mesh A)
   are a subset of the imprint's pieces (same exact triangles).
7. **Independent oracle** (shares no code): the float `MeshMeshCut` on the inputs where it works (generic position, scale 1) must
   give the same triangle count of the cut (up to its documented failure classes), and a brute-force check: every result
   vertex that is new lies on the cutter within the rounding bound (point-triangle distance), every cutter-target
   intersection point sampled (random points on cutter triangles intersected with target triangles in exact arithmetic over
   rationals) lies on a curve edge of the result.
8. **Rounding tests:** target vertices and untouched triangles unchanged bit for bit; boundary loops equal; curve edges present
   after rounding (G5) on the dense cases (a cutter with many nearly coincident curve vertices: the 2^-20 spacing cases of
   `RoundingDenseTessellationTests`); pre-existing target self-intersections left unchanged and not counted; `StrictRounding` status.
9. **Input validation**, **budget** (`MaxPieces`), **cancellation** (as the boolean's tests).
10. **`ReaderFuzzTests`:** not applicable (no reader).
11. **Mutations** (checked by hand per rule, CI `New tests on base` is not applicable: the class is new; the label is `feature`): break the
    filter (triangulate the cutter too: the piece count test fails), swap the orientation of pieces (G1 test), weld the seams
    (duplicate-seam test), drop the locked edges (flip removes a curve edge in the dense case), drop the fixed vertices,
    use `Ignore` for contact (contact tests), skip the exact tie decisions (the coplanar / contact cases), the group copy.

**Mutation shard plan:** a new shard `ExactImprint` (`tests/mutation`, config in `.github/workflows/mutation.yml` paths):
mutated files: `mesh_ops/ExactMeshImprint.cs`, the new assembly code, and the locked-edge changes in `MeshRoundingRepair.cs` (the edit-selection code, joined with the
existing rounding shard filter); tests: the classes above. The change to `ExactMeshArrangement` (the mask) is covered by the
existing arrangement shard plus the equality test (item 6). The PR does not touch error bounds or predicates, so
CLAUDE.md's "mutation gates a merge" applies only to the exact-code diff of the mask (incremental run: a few lines). The full shard
runs nightly on master and its survivors are tasks of the next PR (owner 2026-11-13).

**Reviews:** an independent review agent (diff, no reasoning) with `REVIEW_CHECKLIST.md` and an exact-code review of the
mask change in the arrangement, before the PR is marked ready.

## 6. Performance

Expected cost against `ExactMeshBoolean` on the same pair, per phase (the boolean's phases: setup, intersections, merge,
triangulation, classification, simplify, assemble, rounding):

| Phase | Boolean | Imprint | Why |
|---|---|---|---|
| setup, intersections, merge | full | cross-mesh pairs only | the boolean also runs the self-join of each operand (`SelfIntersections`; 10.6 of the 12.1 s of the pairs phase at 1 thread in probe A, #967), which the imprint does not (3.5) |
| triangulation | all touched triangles of both meshes | only the target's | the cutter's share is skipped (about half when the meshes are similar in size) |
| classification | 66 to 82 % of the slow ops | none | not needed |
| simplify flat regions | yes | no | |
| assemble | yes | simpler (target joins only) | |
| rounding | on the whole result | on the whole result, with more locked edges | the repair loop's cost follows the triangle count (T1b profile); the exact intersection search of the result (up to 76 % of rounding time) is the same |

Estimate (an upper bound, **not measured**): imprint = 30 to 60 % of the boolean's time when the classification is not the
dominant phase (closed meshes: classification is a smaller share), and 5 to 20 % where it is (open inputs; the slow tail).
Per the rule of AGENT_WORKFLOW section 3 the share is a statement about a stratum: the imprint's saving is the classification and the
cutter's triangulation, so it is large on classification-bound operations (open and large meshes) and about half on
others. The rounding of the whole result is the part that does not shrink; with `SafeRounding = false` the imprint is
the arrangement alone.

**Benchmark plan** (`benchmarks/ExactImprint`, a console project like `benchmarks/BooleanComparison`, built in CI as every benchmark):
(1) the same pairs as the BooleanComparison's study set (procedural: sphere / cube / torus pairs at 1k to 1M triangles; the
Thingi10K pairs only locally, the meshes are not committed), imprint against `ExactMeshBoolean` Union on each
pair: per phase time (`PhaseMilliseconds`), pieces, curve edges, rounding edits, 1 / 4 / 12 threads. (2) imprint against the float
`MeshMeshCut` on the generic cases (to state the price of exactness, and the failures the float cut has). (3) the upper
bound first (AGENT_WORKFLOW section 3): run the boolean with the classification timed alone on the study set; the imprint's saving is that share
plus the cutter's triangulation share, measured before the code is written, and put into the benchmark README. Results with the command
that produced them, in the PR.

## 7. Effort

One PR (owner 2026-11-01: one PR per feature, several clean commits), reviewable in one pass:

| Commit | Content | Size |
|---|---|---|
| 1 | arrangement: `TriangulateMeshes` mask + the equality test against the boolean's pieces (exact-code review) | small |
| 2 | `MeshRoundingRepair`: `LockedEdgeF`, no-pre-existing-defect counting, with its own tests | medium |
| 3 | `ExactMeshImprint`: Compute, assembly (4.1), curves, options, statuses, counts | medium-large |
| 4 | tests: invariance, reuse, determinism, procedural, R6, curve comparison, rounding | large |
| 5 | benchmark `benchmarks/ExactImprint`, README with numbers; `docs/OPERATIONS.md`, `SOURCES.md`, ROADMAP, exact-boolean.md items updated; the shard in `mutation.yml` | medium |

Estimate: 4 to 6 sessions of work including the review round, 1 session of review fixes. The risk is commit 2 (the rounding with
locked edges: more defects may remain, and the dense curve cases decide whether G5 holds without `AllowCurveContraction`);
the fallback, if the locked-edge repair is not clean on the dense cases, is to ship v1 with G5 reported (`curve_edges_contracted`,
default allow) instead of guaranteed, and say so. The deprecation decision for `MeshMeshCut` / `MeshBoolean` is a separate step after
a release with the imprint, not part of this PR.

## 8. Open questions for the owner

Kept to the ones that are the owner's (everything else above is decided by default and can be changed in review):

- **Q1, output semantics: classification.** v1 outputs the target split and the curve edges only, no inside / outside side label
  and no "remove the part inside the cutter" (`MeshMeshCut.RemoveContained`). Is that enough for the replacement of
  `MeshMeshCut` / `MeshBoolean`'s uses, or should v1 include per-piece labels (inside / outside the cutter, by the winding number of
  the cutter, which brings the classification back and about doubles the cost)? Recommendation: no; labels as a follow-up on
  demand (`ExactMeshBoolean` already gives the booleans).
- **Q2, API shape: new class or an extension of `MeshMeshCut`.** Recommendation: a new class `ExactMeshImprint` (a `new`, public
  type; `MeshMeshCut` unchanged), with `Result` as a new mesh and an `ApplyTo` for the in-place use. The alternative is an
  `Exact` flag on `MeshMeshCut` (in place, same type), which keeps the `CutVertices` / `AppendSegments` / `ColorFaces`
  members but ties the new semantics to the old class and its options (`VertexSnapTol`).
- **Q3, vertex attributes.** Normals, colors and UVs of the new vertices are not carried in v1 (as the boolean). Acceptable?
- **Q4, contact default.** A cutter vertex on a target face is imprinted as a point vertex (the arrangement's and conformal
  meshing's behaviour), where the float cut refuses or fails. Default `Imprint`; the alternative is to ignore isolated
  contact points.
- **Q5, rounding vs curve edges.** G5 as stated (curve edges kept, a defect left instead of a curve contraction) or
  allowed to contract (the default of SafeRounding). Recommendation: keep, with the fallback of section 7.

How others solve the open points (prior-art line for the options): Q1 Cherchi 2022 and Lévy 2025 classify after the co-refinement
as a separate stage; Q2 nothing found beyond the papers' staging (co-refinement as a separate stage; no library code or documentation of a copyleft library was consulted); Q5 White 2004 and Smith 2010 merge curves within a tolerance, i.e. contract; nothing
found keeps the curve edges across a rounding with a guarantee.
