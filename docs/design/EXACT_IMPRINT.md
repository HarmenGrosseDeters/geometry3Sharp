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

**The one change to the exact core** (the only edit in `ExactMeshArrangement`; the coplanar and contact switches of an earlier draft are cut from v1, see 3.4; the exact-code review applies): an
internal `TriangulateMeshes` mask (default: all). `Compute()` runs `triangulate(ti)` only for triangles of the listed
meshes; the others stay as one untriangulated piece and are not output. `predict_pieces` and `MaxPieces` count only the
listed meshes. Triangulation is per input triangle and independent, so the target's pieces are bit-identical to what
an arrangement of the same meshes with `SelfIntersections = false` and no mask gives for the same triangle (the property the
equality test of section 5 checks; the boolean's arrangement runs with `SelfIntersections = true`, so it equals the imprint only on inputs without
self-intersections and T-junctions, where the comparison with the boolean is made, with `SimplifyFlatRegions` off). No new
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
    public List<DMesh3> Cutters;              // plus `Cutter { set }`: sets Cutters to the one mesh (stated once here)
    public bool LabelCurves;                  // 3.3
    public bool CheckTargetSelfIntersections; // 3.5
    public bool AllowCurveContraction = true; // 4.2
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
    public HashSet<long> CurveEdges;          // 3.3: undirected Result vertex pairs (ExactMeshArrangement.EdgeKey)
    public Dictionary<long,int> CurveId;      // 3.3, with LabelCurves: per curve edge key
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

"In place" (as `MeshMeshCut`): `Result` is a new mesh; `ApplyTo(DMesh3 target)` replaces the content of `target` by `Result`
(`Compute()` itself never modifies `Target`). Owner question Q2.

Building `Result` (DMesh3 cannot be built with chosen ids): copy `Target` (ids kept, holes kept), then for each split
triangle `RemoveTriangle` and `AppendTriangle` its pieces, and `AppendVertex` for the new vertices in the deterministic order below.
Untouched triangles keep their ids by construction. `SourceTriangle` has length `Result.MaxTriangleID`.

### 3.2 Output: the target alone, split

`Result` is the target with its triangles split:
- vertices: **every target vertex keeps its id and its coordinates** (also where the arrangement welded vertices at one position into one point: the assembly picks the vertex id per source-triangle corner, not per point); new vertices get the next ids in a deterministic order
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

`CurveEdges`: the result's edges that lie on the intersection curves (a `HashSet<long>` of undirected vertex pairs, `ExactMeshArrangement.EdgeKey`). It comes from the arrangement's `ConstraintEdges` (point-id
pairs), mapped to result vertices. Options:
- `LabelCurves = true` (default false): additionally a `Dictionary<long,int> CurveId` per curve edge (key of `CurveEdges`), the number of a connected curve
  (chain of curve edges through vertices of curve valence 2; a curve that closes, or ends at the target's boundary). The
  cheap union-find over `CurveEdges`. A group id on the *triangles* per side of the curve is **not** in v1 (that is the
  classification; Q1).
- On an unwelded seam of the target, a curve edge exists once per side (each with its own vertices); `CurveEdges` holds both.
- Curve edges are edges of both pieces on either side. A curve that runs along an existing target edge (the cutter's
  intersection with the target touches the target edge, e.g. a target edge lying in a cutter face) produces a curve edge
  that is a target edge: it is reported in `CurveEdges` too (the imprint "is" there).

### 3.4 Coincident and coplanar parts

v1 has the arrangement's behaviour, without switches (a switch would need new arrangement code and review; follow-up, Q4):
- **Coplanar overlap:** the boundary of the overlap region (cutter triangle edges inside the target triangle, target edges
  inside the cutter triangle) is imprinted, so the overlap region is a union of result triangles. Exact.
- **Contact:** a cutter vertex or edge touching a target face or edge is imprinted as a vertex or curve (a point vertex
  inside the face, connected by the constrained triangulation). This is the class `RefuseContact` refuses in the float cut;
  the exact imprint handles it and counts it in `Counts["contact_points"]`.
- **Coincident triangles** (same triangle): no new vertices. Partially overlapping coplanar triangles give the outline curves above.

### 3.5 Open and defective input

- **Open target:** allowed (the point of imprinting a CAD patch). Boundary loops preserved (3.2). No inside / outside needed.
- **Open or self-intersecting cutter:** allowed; the cutter's own self-intersections are not computed (`SelfIntersections =
  false`), only the cutter's intersection with the target. Where the cutter overlaps itself at the target, the same curve is
  found twice and merged (equal point ids, `merge_points`). A cutter with degenerate (zero-area) triangles: those are
  ignored (as the arrangement: `Degenerate`).
- **T-junctions of the target** (a vertex on the interior of a neighbour's edge): not split (`SelfIntersections = false`). A new
  vertex on such a long edge appears only in the pieces of the triangle that owns the whole edge, the crack of the target stays a crack
  (it is not shared in `Target` either), and G1's "shared edges stay shared" covers the target's shared edges only. Tested (section 5, item 4).
- **Several cutters:** the arrangement also searches pairs between cutters (they are separate meshes) and merges the points found; that costs time and
  gives no output (cutter-cutter segments land on cutter triangles, which are not triangulated). A cutter-cutter pair mask is a later optimization.
- **Self-intersecting or defective target:** the target is **not** repaired (the imprint "keeps its shape"). Its own
  self-intersections are not computed and not removed; they stay as they are in the result. An imprint on a target that
  self-intersects is exact for the pairs target/cutter; two pieces of the target that cross each other each get their cuts
  from the cutter independently. `Result` is then not a manifold-embedded surface, and `Counts["target_self_intersections"]`
  is not computed (a full search; off). An option `CheckTargetSelfIntersections` (default false) runs it for the diagnosis.
- Degenerate target triangles: the arrangement drops them, the assembly keeps them with their id and corners, **unsplit**. Where a split
  triangle shares an edge with a degenerate one and the cutter puts new vertices on that edge, the degenerate triangle does not get them: the
  target's topology opens a crack there (`Counts["unsplit_degenerate_points"]` counts the points), so G1's "closed stays closed" is stated for
  targets without a degenerate triangle at the cut (the second option of the coordinator's review; splitting the degenerate neighbour into a
  fan needs a triangulation of the pinched collinear polygon where the sides' points coincide, and is a follow-up). Tested (a counted crack).
  Not finite coordinates: `InvalidInput`, `Target` or a cutter null: `InvalidInput`.
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

1. **Hard fixed vertices (new mode):** today `FixedVertexF` is a preference (a fixed vertex can still be removed by a contraction, moved
   by the false-contact separation fallback, or lost with a tiny component). The imprint needs a hard mode, `FixedHard`:
   fixed vertices are never moved, never removed (a contraction keeps the fixed end, and is refused if both ends are fixed), the
   separation fallback does not move them, and `RemoveTinyComponents` is off. Cost: more defects can remain than in the boolean (counted).
2. **Locked edges (new, in `MeshRoundingRepair`):** a `LockedEdgeF(int a, int b)` predicate. A locked edge is not flipped,
   not zipped across, and not contracted, and no flip, zip or strip zip removes it. Locked: all curve edges, and the edges of the target's boundary and of its shared
   edges between two fixed vertices (so that the target's topology stays). A contraction of a *curve* edge is allowed only if
   `AllowCurveContraction` (default true) and the edge is shorter than the bound; it is then counted in `Counts["curve_edges_contracted"]`
   and the curve loses that edge (its ends become one vertex; the curve stays connected). Other edits (contractions of non-curve
   edges at new vertices, tiny moves of new vertices) are unrestricted by the lock. Without the lock a flip can remove a curve edge
   (the curve is then no longer in the mesh). Consequence: more defects can remain than in the boolean; `RoundingClean` tells.
3. **Pre-existing defects, as built:** the first design (a `PairProvenance` predicate over result triangles, and a `Scope` predicate) is
   dropped: the repair already skips the exact pair search for two *exact triangles* (all three vertices input doubles, `ExactVertexF`, `SharedPositionsTouch`;
   `docs/design/ROUNDING_EXACT_PAIRS.md`). The imprint sets `ExactVertexF` to the target's vertices, so untouched triangles and pieces with three
   target corners are never tested against each other: the restricted scope of the design is the existing skip, and untouched-untouched pairs are
   pre-existing by construction (a target that intersects itself keeps its own intersections, not seen, not counted). Pairs with a piece triangle are
   tested; where the target itself intersects there, the repair may still edit pieces (documented in the class comment; it stays within the bound).
   Target T-junction cracks and seams are not defects of the repair. For a clean target this is the boolean's `IsClean`. `FixedHard`, `LockedEdges`,
   `LockedEdgeF` and `AllowLockedContraction` are the extensions of `MeshRoundingRepair` (a locked edge is never flipped, zipped or removed; a contraction
   of a curve edge is counted in `LockedContractions`; the locks of the vanishing vertex move to the kept one).
4. Statuses: `Success` with `RoundingClean` false means defects remain (as the boolean, Counts say which). `StrictRounding`
   gives `RoundingNotClean` and `Result = null`.

As built, after the review: the pieces' edges that lie on an edge of their source triangle (the parts of a split target edge) are also locked (`LockedEdgeF`),
so that a flip cannot mix the sheets on both sides of a target edge; `SourceTriangle` is re-derived after the repair (a triangle the repair did not edit
keeps its source, an edited one takes the nearest of the sources at its vertices, ids not in use are -1); a point equal to a target vertex (a vertex on the
edge of a split triangle) reuses that vertex (`Counts["target_vertex_points"]`); `HasCracks` is true where new vertices met a degenerate triangle that is not split;
a curve along an edge of an unsplit triangle is in `CurveEdges`.

**Guarantees** (stated, each tested in section 5):
- G1 Combinatorial: closed and manifold target gives a closed, manifold result (exact, independent of rounding); boundary loops of an
  open target are the same loops, split by vertices; orientation of every piece equals its source triangle's; target vertex ids
  and coordinates unchanged (with `FixedHard`, 4.2.1: also after rounding, bit for bit); untouched triangles unchanged;
  degenerate target triangles kept (but not split, 3.5: the closed guarantee is for targets without one at the cut).
- G2 Exactness before rounding: the set of imprinted curves (as exact points and constraint edges) equals the arrangement's
  intersection curves of target and cutter (the boolean's `ConstraintEdges` restricted to the target's triangles).
- G3 Geometric: with rounding, every result point is within `max(MaxDisplacement * diagonal, UlpFactor * ulp)` of the target's
  surface (the same bound as SafeRounding); with `FixedHard` only new vertices move (tiny moves within the bound, or the
  contractions of curve edges); target vertices never move. For results where no edit was made the distance is at most a rounding unit.
- G4 Verified: with `SafeRounding`, if `RoundingClean`, no pair with a piece triangle (these are all the pairs that can be new)
  self-intersects unless its source triangles intersect in the target, and no piece is degenerate unless its source is (exact check on the doubles).
- G5 Curve edges: every curve edge of the exact imprint is an edge of `Result`, or was contracted by a counted edit
  (`curve_edges_contracted`, each shorter than the SafeRounding bound); with `AllowCurveContraction = false` the second case is 0 and a
  degenerate curve triangle stays as a defect instead (then `StrictRounding` fails on the dense cases, by design). Two curve
  vertices that are different exact points but round to one double are exactly the case the contraction exists for.

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
4. **Procedural cases** (all generated, no mesh files; each within the 5 s test limit, the many-seed and dense ones sized down with the same power, `Slow` only
   with a one-line reason, anything beyond 10 s goes to a benchmark or the `Nightly` tier): cube cut by a plane quad, by a sphere, by a tilted cube; two spheres;
   torus and cylinder; the transversal grid cut; cutter vertex on a target face, on a target edge, on a target vertex; cutter edge on a face; coplanar
   overlap; coincident triangles; cutter passing exactly through target vertices; many-curve
   crossings (cutter curves crossing inside a target triangle); an open target (a plane patch with a boundary crossed by the cutter); an open
   cutter (a single triangle, a strip); a cutter with a self-intersection; a degenerate cutter triangle; a duplicate-seam target; a target T-junction crossed by the cutter; a degenerate target triangle in the cutter's path; a target with
   a self-intersection crossed by the cutter (pre-existing defects not repaired, not counted); non-finite and out-of-range coordinates (`InvalidInput` / `OutsideRange`). Each success
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
   target (no area gained or lost: the signed area of the pieces of each source triangle, projected onto the source triangle's dominant coordinate plane, sums
   to that of the source: rational and additive, because the pieces are coplanar with the source; the 3D area has a square root and does not add). The oracle is an `ExactMeshArrangement` of the same meshes with `SelfIntersections = false`, no mask.
   For inputs without self-intersections and T-junctions also: the pieces of mesh A that `ExactMeshBoolean` keeps (with
   `SimplifyFlatRegions = false`, `SafeRounding = false`, orientation aside) are a subset of the imprint's pieces. Plus a
   mask test on the arrangement itself (untriangulated meshes give no output, the triangulated ones equal the unmasked run, and the
   scaling invariance of both).
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
    fixed-vertex mode soft (target vertex moved / removed), the group copy, `SourceTriangle` off by one, the seam join (join by position
    instead of by target edge), keeping degenerate target triangles.

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
| rounding | on the whole result | only pairs with a piece triangle (4.2.3), with more locked edges | the exact intersection search of the boolean's result is up to 76 % of its rounding time; the imprint's follows the touched region |

**Measured** (`benchmarks/ExactImprint`, 2026-11-16, this container, not pinned, median of 5 runs after a warm-up; the commands are in the benchmark README):
the imprint of a sphere by an offset sphere is 0.45 to 0.58 of the time of `ExactMeshBoolean` Union of the same pair (23k, 95k and 387k triangles:
52 / 89 / 374 ms against 89 / 197 / 700 ms), and 0.44 to 0.56 for a small cutter sphere. Where it goes (the boolean's phases on the same pairs, `bound` mode, one
run): classification is only 6 to 13 % of the boolean on these closed pairs, simplification 2 %, triangulation 4 to 5 %; the rest of the saving is the self-join
of each operand (the arrangement alone: 183 against 317 ms at 387k triangles, noisy), the cutter's triangulation and the restricted rounding. The earlier estimate
(30 to 60 %) was for classification-bound (open) inputs and is not measured here; closed pairs land at about half. The rounding stays the phase that does not shrink with
the cutter (the `no-rounding` column of the benchmark: 36 / 67 / 215 ms against 52 / 89 / 374 ms with it).

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
| 2 | `MeshRoundingRepair`: `LockedEdgeF`, `FixedHard`, tests of their own | medium-large |
| 3 | `ExactMeshImprint`: Compute, assembly (4.1), curves, options, statuses, counts | medium-large |
| 4 | tests: invariance, reuse, determinism, procedural, R6, curve comparison, rounding | large |
| 5 | benchmark `benchmarks/ExactImprint`, README with numbers; `docs/OPERATIONS.md`, `SOURCES.md`, ROADMAP, exact-boolean.md items updated; the shard in `mutation.yml`; `ExactMeshImprint.cs` is not exact code (no predicates of its own, no new bound): it is not added to `.editorconfig` / `ExactCodeAllowList` unless it calls exact files directly, in which case it is, in this commit | medium |

Estimate: 4 to 6 sessions of work including the review round, 1 session of review fixes. The risk is commit 2 (the rounding with
locked edges: more defects may remain, and the dense curve cases decide whether G5 holds without `AllowCurveContraction`);
the fallback, if the locked-edge repair is not clean on the dense cases, is to ship v1 with G5 reported (`curve_edges_contracted`,
default allow) instead of guaranteed, and say so. The deprecation decision for `MeshMeshCut` / `MeshBoolean` is a separate step after
the imprint has been merged and has run in the nightly / corpus (no release is planned, owner 2026-11-16); not part of this PR.

## 8. Questions for the owner: decided

Decided by the coordinator on 2026-11-16, recommendations taken (a new class and its own defaults: no API break, no change to existing outputs):

- **Q1:** no inside / outside labels in v1 (the imprint is the target split and its curve edges; `ExactMeshBoolean` gives booleans; labels as a follow-up on demand).
- **Q2:** a new class `ExactMeshImprint` with `Result` and `ApplyTo`; `MeshMeshCut` unchanged.
- **Q3:** no vertex attributes (normals, colors, UVs) in v1.
- **Q4:** the coplanar / contact ignore switches are a follow-up (they need new arrangement code and review).
- **Q5:** `AllowCurveContraction = true` (a counted contraction of a curve edge below the bound where the rounding needs it).

How others solve the open points (prior-art line for the options): Q1 Cherchi 2022 and Lévy 2025 classify after the co-refinement
as a separate stage; Q2 nothing found beyond the papers' staging (co-refinement as a separate stage; no library code or documentation of a copyleft library was consulted); Q5 White 2004 and Smith 2010 merge curves within a tolerance, i.e. contract; nothing
found keeps the curve edges across a rounding with a guarantee.
