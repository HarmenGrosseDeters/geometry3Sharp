# Booleans & Surface Imprinting

This guide covers exact mesh booleans (`ExactMeshBoolean`), lazy boolean chains (`ExactMesh`), soup-to-solid boolean pipelines (`SolidBooleanPipeline`), exact surface imprinting (`ExactMeshImprint`), and result validation for downstream solid workflows.

---

## Choosing a Boolean or Cut Class

| Goal | Use | Output & key properties |
|---|---|---|
| Exact union, intersection, difference, XOR, or n-ary winding predicate on meshes | `g3.ExactMeshBoolean` | New `DMesh3` in `Result`; exact arrangement, `SafeRounding` on by default |
| Resolve self-intersections, nested shells, and duplicate faces in one mesh | `ExactMeshBoolean.Resolve(mesh)` | New clean `DMesh3`; reorients inconsistent components by area majority |
| Chain several booleans with a single final rounding pass | `g3.ExactMesh` | Lazy expression evaluated in one exact n-ary pass (`Round`, `ToBoolean`) |
| Exact boolean of closed triangle soups → small clean solid without manual pre-repair | `gs.SolidBooleanPipeline` | Closed solid + `SolidBooleanReport` (boolean + guarded `Reducer` pass) |
| One body against many (`A ∩ Bi` or `A − Bi` per body) | `ExactMeshBoolean.IntersectEach` / `DifferenceEach` | Parallel per-body array; bit-identical to individual runs |
| Cut intersection curves of a cutter into a target without removing anything | `g3.ExactMeshImprint` | Target mesh with split triangles, kept vertex IDs, `CurveEdges`, `SourceTriangle` |
| Approximate boolean of heavily broken/open meshes | `gs.MeshWindingNumberBoolean` | Remeshed closed surface via adaptive octree winding numbers |

> **`MeshWindingNumberBoolean` and the nesting of closed shells (#1609):** each input is read as its oriented copy (`WindingContouring.OrientedCopy`), which takes the nesting by parity: a shell inside an odd number of others is a cavity, so an outward shell inside another (an insert in a housing) is reversed and hollow. `ComponentsFlippedByNestingA` / `ComponentsFlippedByNestingB` count those shells per input (0 for an input without one, and for a real cavity). If `MinVolumeIoU` (default 0.99, 0 or less: no refusal) is above 0 and the two readings of an input (parity and the plain union of its shells) differ in volume by more than `1 - MinVolumeIoU`, `Compute()` is false with `ResultReason` `NestingAmbiguous`; below that difference it only sets the counts. The judgement is per input (an input whose own two readings differ), not per result: the combined result is not compared with anything, and one ambiguous input refuses the whole operation. An input the operation does not read (the other one is empty) is not checked; an input without a nested outward shell is unchanged bit for bit. Limit (as `MeshRobustRepair`, #1566): an insert in a housing with both shells inside-out is not seen (only the housing is flagged: count 0, the hollow reading, no refusal). A caller that means the union takes it from `ExactMeshBoolean.Resolve`.

> **Deprecated upstream classes:** `MeshBoolean` and `MeshMeshCut` are marked `[Obsolete]`. They use floating-point snap tolerances (`VertexSnapTol = 1e-5` absolute) and are not robust across scales or coplanar contacts. Use `ExactMeshBoolean` and `ExactMeshImprint` instead.

---

## `ExactMeshBoolean`

- **Namespace & class:** `g3.ExactMeshBoolean`
- **Input / mutability:** Read-only inputs; writes a new `DMesh3` to `Result`.
- **Guarantees & limits:** Exact predicate and arrangement decisions; coplanar, touching, and nearly coincident faces are handled exactly. Only output coordinates are rounded to `double` (unless `ComputeExactVertices = true` is enabled for exact rational coordinates in `ResultExactVertices`). New vertices inside flat coplanar regions and on straight edges are simplified exactly so repeated booleans do not fragment faces.
- **Example:** [ExactMeshBooleanExample.cs](../examples/ExactMeshBooleanExample.cs)
- **Further reading:** [SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md), [BooleanComparison README](../../benchmarks/BooleanComparison/README.md)

### Basic Binary & N-ary Operations

```csharp
using g3;

// Binary difference: A minus B
var op = new ExactMeshBoolean(cube, bar, ExactMeshBoolean.Operations.Difference);
if (op.Compute() && op.RoundingClean) {
    DMesh3 result = op.Result;
} else {
    Console.WriteLine($"{op.Status}: {op.Error}");
}

// N-ary union, intersection, difference (first minus all others), or winding predicate
var unionAll = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union);
var atLeast2 = new ExactMeshBoolean(meshes, ExactMeshBoolean.AtLeast(2));
var xorOdd = new ExactMeshBoolean(meshes, w => w.Count(x => x > 0) % 2 == 1);
```

`ExactMeshArrangement` (the arrangement under the booleans): the point ids in its `Output` and `ConstraintEdges` are labels of one run (ids of input vertices, all of `VertexPoints`, are deterministic; ids of new points depend on the thread schedule; the geometry is deterministic); compare or sort through `Exact(id)` or `Canonical(id)`, never by id.

Putting multiple operands into a single n-ary operation (`A − (B1 ∪ … ∪ Bn)`) runs one arrangement and rounds only once. In the measured ball packings it never returned more triangles than chaining binary booleans (equal on a lattice, up to 21 % fewer on dense packings), and it is faster for sparse or chain-like inputs. For a union of many bodies that overlap each other densely it is slower than a tree of pairwise unions: see [Union of many overlapping bodies](#union-of-many-overlapping-bodies-one-call-or-a-tree).

For closed first meshes, `Difference` and `Intersection` automatically cull operand triangles wholly outside the relevant solid (`Counts["t3a_culled"]`). Diagnostic counts (`IntersectingPairs`, `SelfIntersectingPairs`, `Patches`) describe the arrangement after culling; use `Union` or `MeshRobustRepair` to inspect self-intersections across an entire operand.

### Union of many overlapping bodies: one call or a tree

Two ways to unite many closed bodies: all of them in one `ExactMeshBoolean` (one arrangement), or a balanced binary tree of pairwise unions ([UnionTreeExample](../examples/UnionTreeExample.cs)). The tree must be built from eager calls, `new ExactMeshBoolean(a, b, Union)` and `Compute()` per pair, as the example does: the lazy `ExactMesh.Union` composition (see the table above) is evaluated as one n-ary arrangement again and gives no tree.

Rule of thumb, measured on procedural ball packings with `StrictRounding = true`, net10.0 Release, on one Windows desktop with 12 logical cores that other sessions used only lightly (times +-20 %). The shape matters, not the absolute times:

- Sparse or chain-like inputs (each body overlaps a few others: helices, lattices, branching tubes): one call. It was 2.9 to 3.8 times faster than the tree in the measured cases (lattice of 216 balls 443 ms against 1305 ms; helix of 192 balls 288 ms against 1088 ms) and grows about linearly with the body count.
- Dense packings (a body overlaps many others): one call is measured to grow faster than linearly (about quadratically in the body count from 48 to 192 balls), and the tree is faster above a mean overlap degree (the number of other bodies a body overlaps) of about 12 to 14 on this machine.

| Dense packing, balls | mean overlap degree | one call | tree |
|---|---|---|---|
| 48 | 14.3 | 263 ms | 245 ms |
| 96 | 23.3 | 1124 ms | 545 ms |
| 192 | 34.0 | 4606 ms | 1229 ms |

The threshold is not a property of the input alone. It depends on the machine: the winding stage of one call is a parallel loop, the tree is many small calls, so core count and load change the ratio. A second and a third run of the same inputs (`scaling 3 dense104:24,48,96 helix:96`, counts and results identical) on a heavily loaded machine, and with `DOTNET_PROCESSOR_COUNT=4` on a moderately loaded one, moved the crossover towards one call: at 48 balls (degree 14.3) one call was faster than the tree (by 2.2x in the loaded reviewer's run, by 1.2x with 4 processors; 1.07x slower in the quiet run), and at 96 balls the time of one call over the tree was 1.0 (loaded reviewer run) and 1.6 (4 processors) against 2.1 in the quiet run (a 12-thread rerun under moderate load gave 0.84 and 2.1). So fewer free cores favoured one call in these runs. The cause was not established (a guess: the tree's many small parallel regions pay a fixed fork/join cost that a busy or small machine makes larger relative to the work), and the direction for more cores than 12 was not measured. Measure both on your own input and machine when it matters; the example shows how (time `UnionOneCall` and `UnionTree`).

Why one call grows (derived, then measured): a body cut by `k` neighbours is a circle arrangement on a sphere with `O(k^2)` faces, so the patches per body grow like `k^2` (derived; measured 0.4 to 0.5 `k^2`), and the classification casts one exact ray per patch and overlapping body (candidates per patch about `0.55 k`, measured). That makes about `0.28 N k^3` rays, with `k` itself rising with `N` in a dense packing. The constant 0.28 is a fit: it reproduces the measured candidates (2.1 M at 192 balls, 340 k at 96 against 2.21 M and 359 k), and it hides that the cost goes with the sum of `k_i^3` over bodies, not the mean degree cubed (at 192 balls the maximum degree is 86 against the mean 34). So the work is measured to grow roughly like `N k^3`, not proven to. Most of the cut surface (95 % of the pieces at 192 balls) lies inside the union and is discarded at the end; the tree avoids that by removing interior surface after every pairwise union.

The results differ. Checked with the probe's `scaling` mode on dense (two packings, seeds 104 and 103, 3 to 6 sizes of 12 to 192 balls), helix (24 to 192) and lattice (27 to 216) inputs, 17 sizes in all: both routes returned a closed mesh with an empty `MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true)`, and the volumes agreed to the 1e-9 the probe prints (rounded to 1e-9). The tree returns up to 21 % more triangles on dense packings and a different triangulation (the meshes are bit-identical only on the lattice), so choose by speed and do not expect the same mesh.

To estimate the overlap degree of your own input, count for each body the other bodies whose bounding box intersects its box and take the mean (`UnionTreeExample.MeanBoxOverlapDegree`). The threshold above is for balls and their true overlaps; box overlaps over-count (the 8 balls of the example overlap 6 others each, their boxes 7), so the box figure at which to switch is somewhat higher. It was not measured for boxes.

Basis: [NARY_UNION_DENSE_2026-11.md](../research/NARY_UNION_DENSE_2026-11.md), the probe [ProceduralSphereUnionProbe](../../benchmarks/ProceduralSphereUnionProbe/README.md), issue #1466. A winding propagation across patches (one ray per body) would remove the superlinear term and, as the windings would be the same integers, is expected to keep the output of one call; it is not implemented and not tested.

### Batch One-Against-Many (`IntersectEach` / `DifferenceEach`)

```csharp
var statuses = new ExactMeshBoolean.BooleanStatus[tools.Count];
DMesh3[] intersections = ExactMeshBoolean.IntersectEach(terrain, tools, b => b.SafeRounding = true, statuses);
DMesh3[] differences = ExactMeshBoolean.DifferenceEach(part, tools);
```

`terrain` and the tools are closed solids here: an open one is refused (`statuses[i]` is `OpenInput`) unless the lambda also sets `b.AllowOpenMeshes = true`.

Each entry is bit-identical to running an independent boolean against `terrain` / `part`, while validating and orienting the base mesh only once and running bodies in parallel.

### Input Orientation & Open Meshes

- **Inconsistent orientation (`Orientation`):** Closed inputs with flipped triangles are refused by default (`Orientation = Refuse`, `Status = InconsistentOrientation`) because inside vs. outside is ambiguous. Set `Orientation = AreaMajority` to reorient each component by its larger area part (`ReorientedTriangles`). `ExactMeshBoolean.Resolve(mesh)` always reorients as a repair operation.
- **Orientation by nesting (`Orientation = ByNesting`):** an input that is closed modulo 2 and embedded is oriented by the parity of the crossings of one exact ray per component (the solid is the odd-depth region: nested shells and consistently inside-out inputs come out right). An input that self-intersects and is inconsistently oriented is refused with `SelfIntersectingInconsistent`; in such a mesh a consistently oriented component that encloses a negative volume is refused with `InconsistentOrientation`.
- **Inside-out inputs (`CountInsideOutCells`):** `InsideOutCells` counts the closed components that are inside-out (the solid is empty or reversed) when `CountInsideOutCells` is set (off by default and then free; on, up to about 10 percent on 800k-triangle inputs, within noise below 200k). `SolidBooleanPipeline` sets it and refuses such an input by default (`InsideOutInput`, `AllowInsideOutInputs`). One exception: thin inside-out components are dropped, see [below](#thin-inside-out-components-droppedinsideoutcomponents). `Orientation = ByNesting` derives the enclosed solid only for an embedded input: a closed input that self-intersects keeps its given orientation, and the refusal then names its self-intersections instead of recommending `ByNesting`, and in every mode names an input that is a closed copy made by the input repair (`Report.RepairedInputs`). `MeshAutoRepair` closes an open, non-planar sheet with a fill of its boundary loop that the sheet crosses, so the copy self-intersects (and can be inside-out in part): the pipeline checks every `MeshAutoRepair` copy exactly and refuses one that self-intersects where the input did not (`OpenInput`; an open cutting surface is not a solid).
- **Open inputs (`AllowOpenMeshes`):** Off by default (`false`, since 2026-11-23; `true` before): an open input without the role `Sheet` fails with `Status = OpenInput`, and the error names the inputs. Set `AllowOpenMeshes = true` to state the generalized winding number rule. Open inputs are then classified by generalized winding number (`> 0.5`; `ExactWinding[i]` is `false`), using the exact Antipodal Method below 20,000 boundary segments (hierarchical above 1,000 segments) and fast winding numbers above that cap. On the Antipodal path a piece whose value is undecided is never read as outside: it is evaluated again with a wider error bound that is taken only where it decides the threshold, and a piece that nothing decides (a rim piece whose centroid lies within the rounding distance of a boundary edge, where the winding number takes every value between its two sides) gets the fast winding number, counted in `Counts["antipodal_unresolved"]` and `Counts["antipodal_fast_winding"]` (#1587, owner decision 2026-11-25; before, such pieces were dropped silently as outside). That value has no error bound, as on the fast-winding route, and it is a sum of `Math.Atan2` terms (through `MathUtil.TriSolidAngle`), each of which can differ by 1 ulp between runtimes (#1577; #1627 moves it to `CoreMath`), so a value near 1/2 can be classified differently on another runtime; on one runtime it is the same in every run, serial or parallel. Which pieces reach that branch depends on the rays (the `Seed`) and on the evaluation levers (per patch, traced), as for every value near a tie. Where an open boundary meets other sheets along an edge with conflicting orientations, pieces are left unjoined (`ConflictEdges`, always `0` for closed inputs).

### Thin Inside-Out Components (`DroppedInsideOutComponents`)

Owner decision 2026-11-25 (#1555): `SolidBooleanPipeline` drops a thin inside-out component of a closed input before the boolean instead of refusing the run with `InsideOutInput`. Typical case: two nearly coincident sheets of the wrong orientation, a residue that the rounding cannot resolve.

- **What is dropped:** a component (by edges) of a closed input that is not a sheet, closed and consistently oriented by its edges, with an exactly negative volume and a thickness `2|V|/A` below its bound (see **The bound**), where the rest of its input has a winding number of 0 or less at one of its vertices (exact; it lies outside the solid there). It is dropped from a copy (the caller's mesh is not changed) and listed in `Report.DroppedInsideOutComponents` (input, triangles, volume, area, thickness, bound, extent, centre).
- **The bound (#1567, owner decision 2026-11-27):** `1e-5` (the default `RoundingMaxDisplacement`) times the extent of the component itself (`Extent`; `Bound` = `1e-5 * Extent`). The extent is the largest distance from the component's first vertex (lowest vertex index) to its other vertices, between half its diameter and its diameter; it scales exactly with 2^k and moves only by rounding under a rotation or a translation, so the decision does not depend on the orientation (the axis-aligned box diagonal did, by up to sqrt(3) for a flat plate). A residue is two nearly coincident sheets, thin by its own extent; a part that is not (a real 10 x 10 x 0.01 plate: 1e-3 of its extent) is refused with `InsideOutInput`, whatever the other components of its input or the other inputs are. Until then the bound was relative to the largest component of all closed inputs, and the plate was dropped silently next to a large input (`Success`, `Clean`) and refused next to a small one; `Bound` had that meaning and now has this one, `Extent` is new. It is fixed (`ConfigureBoolean` does not change it), and the magnitude is compared in doubles, the sign decided exactly. A rule in units in the last place of the coordinates was not used: a residue of a chain of booleans is as thick as that repair's drift whatever its ulps (see [SAFE_ROUNDING](../design/SAFE_ROUNDING.md), #1216), and a placement-dependent rule would decide by the position of the part.
- **What stays:** a cavity (every vertex inside the rest, also of a shell closed only by position, and also a thin one or one that touches the wall: vertices on the rest or within the touch distance of it are skipped); a component that is not thin (refused as before); every component of an input that has nothing else. Not with `AllowInsideOutInputs` or `Orientation = ByNesting`.
- **Runs that succeeded can change:** a thin inside-out lens that crosses a face of the solid used to give `Success` with a sliver of the lens cut out of the solid; it is now dropped and the solid stays whole. A thin lens inside an empty cavity is outside the solid (the winding number there is 0) and is dropped too. Since #1567 a run that master finished by dropping a compact component (thickness 1e-5 or more of its own extent, below 1e-5 of the size of a larger component) is refused with `InsideOutInput`. In the other direction one run changes from a refusal to a success: a thin cavity that was a candidate on master and stayed is now part of the rest, so a thin lens in the void of that cavity (winding 0 against the rest) is dropped (`Success`, listed in `Report.DroppedInsideOutComponents`) where master refused the run with `InsideOutInput`.
- **Limits:** a thin component that crosses the rest with every vertex inside it stays and is handled as before. (a) A residue more compact than about 1e5:1 (thickness 1e-5 or more of its own extent) is refused with `InsideOutInput` even when it lies within the rounding drift, which is bounded by 1e-5 of the model diagonal ([SAFE_ROUNDING](../design/SAFE_ROUNDING.md)), not of the residue's extent (example: in an input of diagonal 1000, a flipped lens 0.01 x 0.01 x 1e-6). Observed residues are about 6.5e-13 of the diagonal, so in practice only tiny compact ones are affected, and a refusal is the safe failure. (b) Any inside-out component, a real one too, is dropped when it is thinner than 1e-5 of its own extent (eg a flipped foil 100 x 100 x 0.0005: dropped with `Success` and `Clean`, as on master): check `Report.DroppedInsideOutComponents`, as `Status` and `Report.Clean` do not show a drop.
- **Cost:** one pass over the connected components of every closed input on every default run (about 140 ms on a closed sphere of 1.6M triangles, against about 30 ms for `IsWindingClosed`); the exact work runs only for thin candidates.

### Open Inputs That Enclose Nothing (`OpenInputsWithoutEffect`)

An open input without a role is classified by the generalized winding number above 1/2. A flat or shallow sheet usually stays below 1/2, so as the tool of a Difference it cuts nothing: the result is the first mesh, with `Success` (#1211). `ExactMeshBoolean.OpenInputsWithoutEffect` lists such inputs after a successful `Compute()` (indices as in `Meshes`, ascending; empty otherwise):

- **Listed:** an open input (not a `Sheet`) at one of whose pieces the result would depend on it (for a Difference: a piece of a tool inside the first mesh and outside the other tools), while no decision of the classification changes when its winding number is taken as 0: the result is the one of an input that encloses nothing. For the tool of a Difference or an operand of a Union that is the result without it; for an operand of an Intersection or the first mesh of a Difference it is the empty result. Decided with the integers of the classification; no tolerance.
- **Not listed:** closed inputs; a sheet with the role `Sheet`; an open tool that does cut (a deep cup); in a Difference, a sheet that does not reach the first mesh or a tool hidden inside another tool (a closed one would vanish there too).
- **Each by itself:** every listed input could be taken as empty alone. Two listed inputs can still have an effect together (the same open tool given twice: either could be left out, not both).
- **Limit:** the list says that the result is the one of an input that encloses nothing, not that the result is right. An open input that changes the result is not listed, also where the change is wrong (pieces kept where its winding number crosses 1/2, eg a flat sheet that is not planar in doubles after a rotation far from the origin). The result is then usually not closed: check it (`MeshRobustRepair.NotCleanReason`). Not evaluated with a `WindingPredicate`.
- **What to do:** give a cutting surface the role `Sheet` (next section), or use the swept tool (the recipe below) for a sheet that ends inside. With the default `AllowOpenMeshes = false` an open input without a role is refused, so the list can only be non-empty where the caller set `AllowOpenMeshes = true`; the `OpenInput` error names the inputs by index and these ways out. `SolidBooleanPipeline` sets it, and tries an open Difference tool without a stated role as a sheet first (its sheet route).

### Open Cutting Surface (Sheet Role)

An open cutting surface used as the tool of a Difference is stated, not repaired: `InputRoles` gives it the role `Sheet` (`ExactMeshBoolean.Roles` for the boolean alone). The first input must be closed; the part of it behind the sheet (the side the normals point away from) is inside the tool and removed, the part in front stays, decided exactly (no threshold, no repair, no reorientation of the sheet). It is refused, with `BooleanStatus` `SheetRefused`, `SheetReason` and a witness in `Error` (and the `SheetWitness*` fields of the boolean), where the sheet does not define the sides: in a Union or Intersection or as the first input, not an oriented manifold surface (`NotSurface`, `OrientationInconsistent`), a free edge of the sheet inside the first input (`RimInsideWorkpiece`; a rim exactly on its surface is a cut), a part of the first input in front of one piece and behind another (`SidesInconsistent`: a ring, a spiral; also two shells of the first input whose boxes nest, with a mark of each label on them: two cells of different shells are joined by no edge and may be one region of material, so a cut through a hollow body is refused, also where it is consistent, as the nesting of the shells is not looked at; shells with disjoint boxes are independent), a sheet that only touches the first input's surface (every piece of it that meets the first input lies on the surface: a patch or a full sheet on a face, no piece inside) separates nothing and is refused as `UncutComponents`, whatever `UncutSide` says (its contact would mark the whole input, which would then be removed or left as it is, with `Success`), and a part of it that no piece touches (`UncutComponents`, or the side stated with `UncutSide` when the first input has one shell; with several, whether the part is a cavity of a cut part needs the nesting of the shells and is not decided; and only when every part of the sheet shares an edge with the first input's surface or with a part that does: a closed bubble or any other part that floats inside the first input is refused as `FloatingComponent`, also where it would be right, because its cell may be the same region as a cell with the other label). A free edge of a piece that belongs to another tool or sheet shows as `SidesInconsistent`, not `RimInsideWorkpiece`. A part of another tool inside the first input that touches neither the sheet nor its surface is refused the same way. The cells are those of `mesh_ops/ExactMeshCells.cs`. `Orientation` (`Refuse`, `AreaMajority`, `ByNesting`) and the inside-out count apply to the solid inputs only: a sheet is never reoriented, oriented by nesting or counted as inside-out.

### A Sheet That Ends Short of the Surface

A sheet meant as a through cut can end a little short of the workpiece's surface (by about 0.1 % to 1 % of its size); the sheet role then refuses it with `RimInsideWorkpiece`, because a free edge inside the workpiece leaves "the side behind the sheet" undefined. `gs.MeshSheetExtension` ([SheetExtensionExample](../examples/SheetExtensionExample.cs)) continues the rim straight up to the surface, so that the sheet role can cut. It is opt-in: no operation of the library calls it, and without a call nothing changes.

**The diagnosis first.** `Diagnose()` builds nothing (no strip, no imprint, no cut) and says, per run of rim edges inside the workpiece: the largest and the mean depth of the rim (`MaxDepth`, `MeanDepth`, absolute and relative to the sheet's bounding box diagonal), the length of the rim that is inside (`InsideLength`, measured per rim edge: an edge can be inside while both its ends are outside; `InsideLengthRelative`, `InsideLengthShareOfRim`), `NoExitWithinLimit` where the rim is deeper than the limit (5 % of the diagonal by default, `MaxExtension`), `GrazingExit`, and the estimate `U`. A caller can report "the rim is N % of the diagonal inside the workpiece, at most M % deep" for a refused cut without trying the extension. The depths are sampled (`DiagnosisSamples` points per inside stretch): a lower bound of the largest depth. The diagnosis alone is often the answer: a rim 30 % of the diagonal deep is a sheet that ends inside, not one that ends short (use the swept cut below, or fix the input); a rim 1 % deep is the helper's case.

**What the extension builds.** For each free edge that meets the interior (chosen per edge, not per end: a straight rim across a round workpiece has both ends outside) every rim vertex is continued along the mean of the two outward normals in the planes of its rim triangles up to the first exit of that ray plus a small overshoot; one quad per rim edge, subdivided along the edge where its outer edge would pass back through the workpiece, oriented like the triangle it continues. `Compute()` returns the sheet with the strip added: a copy with the original vertex and triangle ids, positions and corners unchanged; the ids of the added vertices and triangles are not part of the contract. New triangles take the triangle group of the rim triangle they continue and new vertices copy the normal, colour and uv of the rim vertex they come from. A sheet with no rim edge inside the workpiece is returned unchanged (`Report.Extended` is `false`). Which rim edges meet the interior is decided exactly (exact winding number and an exact "on a triangle" test for the rim vertices and for the midpoint of each stretch of an edge), so a rim 1e-14 short of the surface is found as well as one 1 % short. Pass the other tools of the Difference in `OtherTools`: the strip may meet none of them.

**What is exact.** The construction is in doubles; what accepts it is exact: every new point is strictly outside the workpiece (the exact winding number is 0 and no triangle contains the point), the strip meets the workpiece's surface in exactly one connected curve per run and has no coplanar contact (`ExactMeshImprint` with `LabelCurves`; this refuses a thin free pin in a slot, which the sheet role alone would accept and cut through), the strip meets neither the sheet, nor itself, nor another tool (exact triangle intersection), and the sheet role of the exact boolean accepts the extended sheet (`VerifyWithSheetRole`, on by default; `Report.Cut` is that cut; with `false` Success means only that the helper's own checks hold, the sheet role was not asked). In doubles are the choice of the candidate points, the depth limit, the exit angle limit (`DeltaMin`, `AngleMargin`) and the test that no strip triangle is turned against the sheet triangle it continues; they refuse earlier than the exact checks would. A ray through an edge or a vertex of the workpiece, or a grazing exit, can only cause a refusal, never a wrong cut.

| Status of `Compute()` | Meaning |
|---|---|
| `Success` | `Result` is the extended sheet (`Report.Extended`), or the sheet unchanged where no rim edge meets the workpiece's interior |
| `DepthAboveLimit`, `NoExit`, `GrazingExit` | the rim is deeper than the limit (`Report.MaxDepthRelative` and the witness give the depth), a ray has no exit, or an exit is at a grazing angle |
| `NewPointNotOutside`, `OuterEdgeInside`, `InvertedQuad` | the continuation cannot be built: a ray runs on a face or through material after its exit, the outer edge cannot be got out of the workpiece, a notch narrower than the depth folds the quad over |
| `CurveRule`, `CoplanarContact`, `MeetsSheet`, `MeetsItself`, `MeetsOtherTool` | the exact checks of the strip: it pierces a pin or wall between two rim vertices, lies in a face's plane, or meets the sheet, itself or another tool |
| `BowtieVertex`, `NoDirection`, `SheetNotSurface`, `WorkpieceNotClosed`, `InvalidInput` | the input: a rim vertex with more than two rim edges (no per-wedge points are built), cancelling normals, an edge used by more than two triangles or a seam at a rim edge, an open workpiece, empty or non-finite input |
| `SheetRoleRefused`, `CutFailed`, `CutNotClean`, `UncertaintyAboveLimit` | the last check: the sheet role refuses the extended sheet (`Report.SheetReason`), the cut failed or is not clean, or `U` relative to the smaller part is above `MaxUOverSmallerPart` (off by default) |
| `GapBelowResolution` | the next surface behind an exit is closer than twice the least overshoot (a very narrow slot): the new point could not be kept short of it |

`NoExit`, `ImprintFailed`, `ConstructionFailed` and `CutFailed` are defensive: no input is known that produces them (a point inside a closed workpiece has an exit; the imprint, the append and the cut of finite inputs do not fail), and they are never Success. Every refusal has a witness: `Report.Witness` (text), `WitnessPoint`, the free edge and the rim vertex of the sheet it is about (`WitnessEdge`, `WitnessVertex`), and the run (`Report.Runs[i].Refusal`).

**What it does not guarantee.** That the sheet was meant to go through: that is intent, not geometry, and no rule can decide it (a body with a missing face used as a sheet would be continued into the pocket its caller probably meant; the helper takes the role you state and has no convention for a shell). That a curved sheet continues with its curvature: each rim triangle is continued in its plane. `Report.U` (and `Runs[i].U`, `URelative` to the smaller part of the cut once a cut exists, `URelativeToWorkpiece`) is a first-order ESTIMATE of how much the cut would differ for a continuation tilted by the sheet's own turning, not a bound; on the spike's curved sheets (a cylinder segment, a dome, a tray) the cut differed from the smooth surface by 24 % to 57 % of `U`. The measure of the sheet's size is the diagonal of its bounding box, which a rotation changes, so a rim close to the limit can be continued in one frame and refused in another; the strip at a corner of a curved rim depends on the frame by rounding (scalings by powers of two and repeated runs give the same sheet bit for bit). A rib on the wall that the strip cannot go round is refused (`InvertedQuad`) in some frames and cut correctly in others. What the rules enforce is weaker than "nothing but the band up to the first surface": a pin that stands beyond the first exit on a thin neck that attaches it to the exit wall is cut through (the strip's curve stays one component, and no continuation that ends outside could avoid the neck); the curve rule is topological. The helper's cut is checked for cleanliness too (`CutNotClean`: a workpiece with touching shells gave a cut with self-intersections), and a gap behind an exit below twice the least overshoot is refused (`GapBelowResolution`).

Cost, one machine (Windows, .NET 10), medians of runs after a warm-up, a planar sheet whose far edge crosses a sphere ([benchmarks/SheetExtensionProbe](../../benchmarks/SheetExtensionProbe/README.md), `results/cost.md`); the cut is the sheet role's Difference of the extended sheet, run as the last check. The diagnosis and the construction are linear in the sheet and dominated by the workpiece's box tree and exact winding cache plus the per-rim-edge queries; at 80,000 sheet triangles the diagnosis is a fifth of the cut. (A quadratic check of the sheet's edges, found in review, made it 3.3 s at 80,000 triangles before it was fixed.) The small rows are dominated by fixed costs and vary by a factor of two between runs:

| workpiece / sheet triangles | diagnosis ms | construct ms | verify ms | imprint (curve rule) ms | cut ms |
|---|---|---|---|---|---|
| 2,028 / 288 | 6.7 | 6.3 | 0.7 | 3.9 | 32.6 |
| 10,092 / 1,152 | 16.2 | 15.7 | 2.2 | 16.1 | 103.4 |
| 41,772 / 4,608 | 34.5 | 34.6 | 4.5 | 21.3 | 169.9 |
| 49,152 / 20,000 | 39.4 | 33.2 | 11.3 | 20.2 | 206.0 |
| 49,152 / 80,000 | 61.0 | 62.8 | 35.2 | 20.6 | 304.3 |

### Extruded Cut With an Open Sheet (Recipe)

The role `Sheet` needs a sheet that crosses the workpiece fully and refuses one that ends inside it (`RimInsideWorkpiece`). Where the cut is meant along a direction (a step, a slot, a pocket under the sheet), sweep the sheet to a closed tool and subtract that: [SweptCutExample.cs](../examples/SweptCutExample.cs). There is no class for it; the recipe is `MeshExtrudeMesh` and the closed `ExactMeshBoolean`:

1. State the direction `d`: where the removed material is, seen from the sheet (the side its normals point away from).
2. Check that every triangle of the sheet faces away from `d` (`normal · d < 0`); otherwise the swept tool folds over. The example refuses such a sheet. The test is in doubles: a triangle nearly parallel to `d` can pass or fail by rounding.
3. `MeshExtrudeMesh` on a copy of the sheet, with `ExtrudedPositionF = p + length * d` and `IsPositiveOffset = false`; `length` longer than the common box of both meshes is along `d`.
4. `ExactMeshBoolean` Difference with `AllowOpenMeshes = false` (the default, stated in the example), so a tool that did not close is refused (`OpenInput`), not guessed.

What it removes is everything under the sheet along `d`, exactly for the tool as built (its far vertices are rounded doubles, so the walls are parallel to `d` up to rounding):

| sheet (cube `[0,4]^3`, `d` down) | result |
|---|---|
| crosses fully at z = 2 | the upper half (32), as with the role `Sheet` |
| ends inside (x up to 2) | a step (48) |
| has a hole inside the cube | the pillar under the hole stays (40) |
| wholly inside (a disc) | a blind pocket from the bottom (56) |

Limits: the facing test is necessary, not sufficient. Parts of the sheet that lie above each other along `d` pass it, and the result is then the Difference with the union of their swept prisms; `SelfIntersectingPairs > 0` of the boolean shows it. A sheet that folds back along `d` (a bucket) is refused; use the role `Sheet` for it. Background and measurements: [OPEN_CUTTING_SURFACE_2026-11.md](../research/OPEN_CUTTING_SURFACE_2026-11.md), candidate D.

### `SafeRounding` & `StrictRounding`

Nearly coincident inputs can create exact vertices that round to colliding `double` positions, leaving slivers or self-intersections. `SafeRounding` (on by default) runs `MeshRoundingRepair` after rounding and verifies the output exactly (`RoundingClean`):

- **Displacement bound:** Every untouched triangle is the exact rounded output. Repaired edits stay within `Rounding.MaxDrift <= RoundingMaxDisplacement * diagonal` (default `1e-5` of the largest connected component's diagonal, plus a few ULPs far from the origin controlled by `RoundingUlpFactor` or `RoundingAbsoluteTolerance`). Edits exceeding the bound are rejected.
- **Effort budget:** Controlled by `RoundingMaxPairTests` (default 5M exact pair tests, scaling with initial defects via `RoundingPairTestsDefectScale` and `RoundingPairTestsMaxGrowth`).
- **Strict mode:** Set `StrictRounding = true` so an unclean result fails with `Status = RoundingNotClean` (no `Result`) rather than returning remaining defects (`Rounding.RemainingIntersections`, `RemainingDegenerate`, `RemainingFalseContacts`, `ScaleDisparity`).
- **`RoundingClean` is not `NotCleanReason(Result) == ""` where the exact solid touches itself:** `RoundingClean` exempts a position where the exact result touches itself (the duplicated vertices of one exact point, counted by `Contacts`) and requires that the rounding made none (vertices of different exact points at one position, `RemainingFalseContacts`) and that nothing else touches or crosses. `NotCleanReason` counts every touching pair, so it says `"self-intersections"` at such a position while `RoundingClean` is true (see "Touching solids" under "What `NotCleanReason(result)` covers"). Examples with exact coordinates: two unit boxes united along an edge (`Contacts == 2`) or at a corner (`Contacts == 1`), and the procedural plate case of #1431; both are pinned in `ExactBooleanInputContactsTests`.
- **Fixed corners of the last resort:** For a degenerate triangle that no other edit removes, the repair's joint stage moves two corners together (`MeshRoundingRepair.JointMoves`); where the free corners alone cannot, it also moves a corner that is an input vertex of the Boolean (`FixedVertexF` is a preference there, `JointMovesWithFixedCorner` counts it), by at most the displacement bound, and tries free pairs with two step sizes. The work of its searches that make no move is capped for the stage (about 32 triangles it cannot repair: a mesh with hundreds of them does not add seconds per triangle). A result that was clean before is unchanged bit for bit; one that was `RoundingNotClean` for such a triangle can now succeed with an input vertex moved (design: `docs/design/SAFE_ROUNDING.md` section 9.1.1). `FixedHard = true` on a `MeshRoundingRepair` used directly keeps those vertices where they are.
- **Speed / exact output:** Set `SafeRounding = false` for plain rounding (faster, or when consuming `ResultExactVertices` directly).
- **`ResultExactVertices` entries that are not points:** `ExactMeshImprint.ResultExactVertices` has one entry per vertex id of `Result`, taken before the repair: a vertex that the repair removes keeps its point, and the entry of a free id (an id of the target that is not in use and that no new vertex took) is `default(ExactPoint3)`, whose `W` is 0; every point made by the library has `W > 0` (there is no other public test: `W` is a public field), so a caller recognises such an entry by `entry.W.IsZero` and must not convert it: `ExactPoint3.ToVector3d()` and `ExactPoint3.ToDouble` throw `DivideByZeroException` for it (since #1410; `ToDouble(0, 0, s)` gave 0 before, kept as an error on purpose: a zero from 0/0 would hide a caller's bug). `ExactMeshBoolean.ResultExactVertices` holds a point in every entry.
- **Rounding is to nearest:** An exact vertex is converted to the nearest `double` (`ExactPoint3.ToDouble`, correct rounding since #1410; it used to truncate toward zero). The old conversion pulled every rounded vertex of a convex shape around the origin slightly inward, which kept chained operations on such shapes (eg `A - (A n B)` for a sphere) free of point contacts by accident; with correct rounding they show duplicated vertices of one exact point (`Contacts`) more often, while `RoundingClean` stays true. Data far from the origin never had that bias. The contacts that show in a chain such as `A - (A n B)` are contacts of the exact solid, not made by the rounding: measured with `ComputeExactVertices` on the three cases that the tests allow (`ExactBooleanIssueCasesTests` sphere minus lens: 13 positions, `ExactCoplanarPreDecisionTests` step 3 of the chain, rotated frame, difference: 3, `FindInvertedComponentsExactTests` chain sliver: 4), every position with duplicated vertices holds vertices of one exact point (the solid touches itself there; the old truncation moved such vertices apart by an accident of its direction, which was not the exact solid), and the contacts the rounding itself makes of different exact points (53, 6 and 24 in those cases) are all separated by the repair.

### Input Order: What the Result Depends On

For one input the result is deterministic (repeated runs, any number of threads). Since #1464 it also does not depend on how
the input is numbered: the same triangles in another order, with their corners rotated, and the same vertices with other ids
give **the same mesh bit for bit, vertex, triangle and edge ids included, and the same status**, for `ExactMeshBoolean` (with and
without `SafeRounding`, with and without `SimplifyFlatRegions`) and for `SolidBooleanPipeline` where it uses its inputs as given
(no input repair, no reduction target). The kept pieces of the arrangement are put into an order that only their geometry gives
(an exact comparison of their points) before the vertices are numbered, so the rounding repair, which works through its defects
by id, always sees the same mesh. Tested on closed inputs in general position, on coplanar overlapping faces, on solids that
touch along an edge or at a vertex, on near-coincident and tangent inputs, on three inputs and on an open input that is consistently oriented. The
inside-out check (`CountInsideOutCells`) shoots its ray at a triangle chosen by position, not by input order, so its count is the same too.

A canonical order makes the outcome deterministic, not better: the repair can still end unclean on an input, for every order alike.

What this does not cover:

- **Another input.** Another triangulation of the same surface, a translation, a rotation or a scale that is not a power of two
  is another input. The exact result is the same solid; the mesh after the repair can differ within the rounding bound, and
  with it the volume of a result that is about as thin as that bound (`RoundingMaxDisplacement`) and, rarely, whether the
  repair ends clean. A scale by a power of two gives the same mesh, scaled, bit for bit.
- **Inputs that are reoriented.** A component whose triangles are not consistently oriented is oriented by the larger part of its
  area (open inputs, `Orientation = AreaMajority`); where the two parts are equal within 1e-9 the first triangle decides, and the
  areas are sums in the order of the triangles. Such an input can come out the other way round for another order.
- **The order of the operands.** `A ∪ B` and `B ∪ A` are the same solid; the same mesh is not promised.
- **`MeshRoundingRepair` used directly.** Its result depends on the ids of the mesh it is given (the order of its work lists and
  ties, the end of a contracted edge it keeps).
- **The undecided inside-out count.** `front_of_triangle` seeds the random direction of its ray with the id of the triangle it was given, so the retries (and the rare `InsideOutUndecided` outcome after 200 failed attempts) can still differ between orders. The winding number it reads, when a ray is found, is exact and the same.
- **The stages of the pipeline around the boolean**: the input repair and the reduction to a target triangle count were not
  made independent of the order.

One-time change against earlier versions: the vertex and triangle ids of every result and the corner order of its triangles
changed; a result that the repair edits is another valid result within the same bound; flat regions whose points lie on a
common circle can have other diagonals (their flips towards Delaunay are now decided exactly, not by the floating-point filter
alone). How often the repair ends clean hardly changes, and not in one direction: on the corpus of generated degenerate inputs
1287 instead of 1303 of 1458 results are clean, while a reference part that was lost before is kept and fewer known "not clean" rows
remain ([BOOLEAN_INPUT_ORDER_2026-11.md](../research/BOOLEAN_INPUT_ORDER_2026-11.md), section 8).

### Budgets & Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
    AllowOpenMeshes = true,
    MaxPieces = 5_000_000,   // checked before triangulation against Counts["predicted_pieces"]
    Cancellation = cts.Token
};
if (!op.Compute())
    Console.WriteLine($"{op.Status}: {op.Error} (predicted pieces: {op.Counts["predicted_pieces"]})");
```

`MaxPieces` aborts early with `Status = BudgetExceeded` once exact intersection counts exceed the limit. Cancelled runs return `Status = Cancelled` and the instance can be reused.

### Volume Gradient (`BooleanVolumeGradient`)

`BooleanVolumeGradient` gives the derivative of the volume of the exact result (before the rounding) with respect to
every input vertex position, for shape and topology optimization.

First run the boolean with `RetainClassification = true`. The option is off by default. It keeps, for each piece of the
arrangement, whether the piece was kept or flipped, its coplanar stack, and the winding numbers in front of the pieces
that are stacked. The memory is linear in the number of pieces, and the result does not change. Then:

```csharp
var boolean = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { RetainClassification = true };
boolean.Compute();
var g = new BooleanVolumeGradient(boolean);
if (g.Compute()) {
    Vector3d dVdx = g.Gradient[1][vid];          // input 1 (MeshB), vertex vid
    Vector3d err = g.GradientBound[1][vid];      // |exact - dVdx| <= err per component
}
```

Example: [BooleanVolumeGradientExample](../examples/BooleanVolumeGradientExample.cs). An optimizer that moves the vertices
of input 1 along the gradient until the result's volume hits a target (projected step `x += (Vtarget - V) g / |g|^2`,
capped, the realized branch at a flush contact, each iteration's contacts reported):
[BooleanVolumeOptimizerExample](../examples/BooleanVolumeOptimizerExample.cs). From the command line:
`g3cli boolean ... --volume-gradient out.json` ([mesh commands](../../tools/g3cli/docs/mesh-commands.md#volume-gradient---volume-gradient)). Method and measurements:
[EXACT_BOOLEAN_GRADIENTS_2026-11.md](../research/EXACT_BOOLEAN_GRADIENTS_2026-11.md).

**Contract.**
- `Compute()` returns false and sets `Error` in four cases:
  - the boolean did not succeed;
  - it was not run with `RetainClassification`;
  - one of its inputs is not closed, so the result has no volume;
  - a value or its bound is beyond the double range. The volume (size cubed) overflows from coordinates of about
    2^341, the gradient (size squared) from about 2^512. The arrays are still filled in this case. At the small end
    nothing fails: below coordinates of about 2^-341 for the volume (2^-511 for the gradient), the values are subnormal
    and their bounds absolute, of about 2^-1074.
- `Gradient[m]` has `MaxVertexID` entries for input `m`. The input index is 0 for `MeshA` and 1 for `MeshB`, or the index
  in the list of an n-ary boolean. A vertex that belongs to no kept piece has the entry 0.
- `Volume` is the volume of the exact result, and `VolumeBound` is its error bound.
- The output is bit-identical on every run: the computation is serial and uses no FMA.
- If the inputs are scaled by 2^k, the gradient and its bound scale by exactly 2^2k and the volume by exactly 2^3k, as
  long as no result underflows or overflows. The evaluation itself uses local frames (proof, step 1), so it is the same
  at every scale.
- The bounds are relative to each entry's own magnitude, also when the inputs span many orders of magnitude (tested
  with boxes of size 1 and 2^-400 in one boolean).
- The cost is linear in the number of kept pieces, about 3-4 µs per piece. It measured 15-142 % of the boolean's own time
  ([benchmarks/BooleanVolumeGradient](../../benchmarks/BooleanVolumeGradient/README.md)).

**Formula.**
A kept piece P of an input triangle T moves with T. So, by the Hadamard formula, the volume changes by the integral of
the normal velocity over the surface:

  dV/dx_i = Σ over the kept pieces P of a triangle T with corner i of s_P λ_i^T(centroid P) N_P

Here N_P is the area vector of P, oriented like T. s_P is -1 for a piece that the result reverses, and +1 otherwise.
λ^T are the barycentric coordinates in T.

The intersection curves have no area, so they do not contribute. This means no intersection point has to be
differentiated.

**The error bound (proof).**
1. *Corners, in a local frame per triangle.*
   - The exact points are the arrangement's, in grid units: the input is 2^e times (grid point + center).
   - Each term of input triangle T is evaluated in T's local frame. The coordinates are taken relative to T's first
     corner and scaled by 2^-S_T, where 2^S_T bounds T's edge coordinates (from the bit lengths of the exact
     differences), so T's edges have length at most about 1.
   - Each difference (q − p) 2^-S_T of two exact points is formed exactly in integers, (X_q W_p − X_p W_q) / (W_p W_q),
     and converted once with `ExactPoint3.ToDouble`, which is correctly rounded (by its documented method). The exact
     value then lies within half a spacing of the result x, and therefore within [Down(x), Up(x)], the two neighbouring
     doubles.
   - The interval is the single point x only when den = 1 and x is an integer equal to num, which the code checks.
   - The volume, V = Σ s_P N_P · p_0 / 3, uses the corner p_0 relative to the center, scaled by 2^-S, where 2^S bounds
     every coordinate of the kept pieces. Its N_P comes from the local frame, scaled exactly by 2^(2(S_T − S)) (step 4).
   - So no product is formed of quantities much larger or smaller than 1. The evaluation neither overflows nor loses a
     small triangle far from the center, however far apart the magnitudes of the inputs are.
2. *Terms and sums.*
   - Every operation of `ExactInterval` (+, −, ×, ÷) rounds to nearest and then widens the result by one spacing on both
     sides. So it encloses the exact result of the operation for all operand values in the operand intervals. The proof
     is in `math/ExactIntervals.cs`. A divisor interval that contains 0 gives the whole line.
   - Four exact identities are kept exact instead of widened: x + 0 = x, x · 0 = 0, x − x = 0 for a single point x,
     and 0 / b = 0 for a b that excludes 0. Otherwise an exact zero, such as a component of the normal of an
     axis-aligned face, would become ±2^-1074 and underflow when it is scaled.
   - By induction over the evaluation, each per-vertex sum interval [Lo, Hi] contains the exact derivative. These sums
     add the terms of the formula in piece order.
   - The same holds for the volume, Σ s_P p_0·(p_1×p_2)/6, and for the alternatives of the contact regions. No new error
     constant is involved.
3. *Value and bound.*
   - The value is m = fl(Lo/2 + Hi/2), and the bound is b = Up(fl(max(Hi − m, m − Lo))).
   - For d ≥ 0, fl(d) ≥ d − ulp(d)/2. `Up` then adds one spacing of fl(d), which is at least ulp(d)/2. The spacing of
     fl(d) is smaller than that only when fl(d) is a power of two 2^j and d is just above it. In that case the spacing of
     2^j upwards equals ulp(d), which is enough. So b ≥ d.
   - Every z in [Lo, Hi] therefore satisfies |z − m| ≤ max(Hi − m, m − Lo) ≤ b.
   - For a single point, Lo = Hi, the bound is 0.
   - A NaN or infinite end gives b = +∞.
4. *Scaling to the input's units.*
   - Each term interval is scaled by 2^(2(S_T + e)) into the input's units before it is added to its vertex. The volume
     is scaled by 2^(3(S + e)) at the end.
   - A scaling by a power of two is exact unless its result is subnormal or overflows. The code checks exactness by
     scaling back.
   - Where it is not exact, the interval's ends are widened by one spacing: ScaleB rounds once, by less than one spacing
     of its result. An end that overflows to infinity is at least as far out as the exact value.
   - For the volume's value and bound, the same check applies. If the result is subnormal, each of the two scalings
     rounds once, by at most 2^-1075, and the bound becomes Up(b · 2^k + 2^-1074).
   - An infinite bound means that the entry itself is beyond the double range: for the volume from coordinates of about
     2^341, for the gradient from about 2^512. Then `Compute()` fails with `Error`. Small coordinates fail nothing:
     subnormal results only make the bound absolute, at about 2^-1074.

**Kinks (contacts of positive area).**
- V is not differentiable only where faces of different inputs lie in one plane and overlap with positive area.
- An edge or a vertex on a face, or two edges crossing, change the overlap by o(t). V is differentiable there.
- At a contact, `Gradient` is the one-sided derivative of the branch that the exact result realizes:
  - a union merges touching solids, so it gives the overlapping side;
  - an intersection and a difference separate them, so they give the separated side;
  - for flush faces (same orientation), the result keeps the face of the first input, by input order. So A ∪ B and B ∪ A
    give the two different one-sided derivatives.
- `Contacts` lists each contact region, in a fixed order. For each region it gives the inputs involved, the number of
  stacked faces, the area, and `Alternatives`.
- Each alternative is a sparse change to `Gradient`. It belongs to another order of the faces along the plane's normal,
  and gives another one-sided derivative.
- For two faces there is exactly one alternative. The realized gradient and the gradient plus the alternative are then
  the two extremes of the Clarke generalized gradient.
- `Alternatives` stays empty in two cases: more than 4 stacked faces, or two faces of one input with the same
  orientation.
- With a `WindingPredicate`, the predicate is also called with the winding vectors of the other face orders. These are
  vectors that the classification itself does not produce, so the predicate must be defined for any vector of integers.

The area of the result is not covered:
- its gradient is not rational;
- it grows like 1/sin θ at nearly tangential seams;
- it also has kinks at edge-on-face contacts.

See the research note for details.

### By-Design Behavior (Not Defects)

- **Open inputs yield open results:** The region where generalized winding number `> 0.5` is generally not closed; boundary edges remain where pieces of the open input survive. Open inputs are refused by default; this applies with `AllowOpenMeshes = true`. Close inputs first (`MeshRobustRepair`, `MeshAlphaWrap`) if a closed solid is required.
- **Edge-touching solids duplicate shared vertices:** Two solids sharing only an edge (e.g., two boxes meeting along an edge) are represented in memory as a closed, manifold `DMesh3` with separate vertex copies along the contact (`NonManifoldEdges`, `Contacts`). Welding by position (e.g., writing STL) creates a 4-triangle edge. Call `MeshRobustRepair.SeparateTouchingVertices(mesh, fraction)` if a strictly manifold welded mesh is required. By contrast, *false contacts* (distinct exact vertices that rounded onto the same `double`) are separated automatically by `SafeRounding` (`Rounding.RemainingFalseContacts == 0`).
- **A result vertex with more than 32,766 triangles is split (a `DMesh3` limit):** the count of a vertex is a `short` (`RefCountVector`, part of the 1.0.324 API), so no `DMesh3` vertex has more triangles than that; an input cannot have such a vertex, a result can (a fan or cone apex that the operation keeps from two inputs, or a result of many pieces at one point). The assembly then takes the one-go builder out (`FromEdgeStructure` gives `null`) and appends the triangles one by one; the triangles that do not fit go to one copy of the vertex at the same position (the next one when that is full too), so the result is a valid `DMesh3` that is **open along the seam of the copy**, not closed, and no triangle is lost. It shows in `NonManifoldTriangles` (the triangles that took a copy, expected 0 for closed inputs without such a vertex), in `Contacts` (the copies are vertices at one position) and in `NotCleanReason` (boundary edges; its meaning is unchanged). `DroppedTriangles` counts triangles that could not be added at all (expected 0). A caller who needs a closed result there has to split the input, or weld by position and accept a vertex that `DMesh3` cannot hold. Pinned by `ExactBooleanVertexLimitTests` and `ExactBooleanVertexCopyTests` (the builder and `append_triangle` directly: a Boolean with such a vertex takes about a minute, 79 s for a cone of 65,532 triangles).

---

## Validating Boolean Results (Solid Workflows)

For workflows that feed boolean outputs into downstream solid tools, check results in this order:

1. **Validate or repair inputs first:** Run `MeshRobustRepair` (or verify `MeshRobustRepair.NotCleanReason(mesh) == ""`) and keep the default `AllowOpenMeshes = false`, so open inputs fail explicitly (`OpenInput`).
2. **Check status and rounding:** Require `Status == Success` and `RoundingClean == true` (or set `StrictRounding = true`). `RoundingClean` allows contacts of the exact solid (`Contacts`); `NotCleanReason` reports those as `"self-intersections"` (see the `SafeRounding` bullets above).
3. **Check `Contacts` and `NonManifoldEdges` before position-welding formats:** Face contacts merge cleanly; edge/vertex contacts carry duplicated vertices. Use `MeshRobustRepair.SeparateTouchingVertices` when strictly manifold welded output is needed.
4. **Negative-volume components are voids:** An inner cavity shell is oriented inward and has negative signed volume. Sum signed volumes across connected components rather than taking absolute volumes per component.
5. **Thin slabs are valid pieces of the exact solid:** A thin solid that the rounding repair leaves as it was is kept, down to a few units in the last place of its coordinates. `SafeRounding` removes a closed component that is tiny or thinner than `RoundingMaxDisplacement` only if it was thinner than 4 ulps of its largest coordinate, keeps a defect, or had its volume changed by more than a factor 2 by the repair (`Rounding.RemovedComponents`, listed in `Rounding.RemovedComponentInfos` with volume, thickness and reason). A removal of a solid that did not survive the repair and was at least 256 ulps and 1e-3 of the rounding bound thick (about 1e-8 of the diagonal) sets `Rounding.RemovedResolvable` and `RoundingClean = false` (with `StrictRounding`: `RoundingNotClean`); thinner removals, such as the slivers between copies a few ulps apart or the residues of an earlier rounding in a chain of booleans, are listed and leave the result clean. Thin components holding real volume for their area are kept (`Rounding.ScaleDisparity`, `RoundingClean = false`). Filter thin pieces yourself if they are unwanted (`SolidBooleanPipeline.MinThickness`).
6. **Do not repair an open boolean result and validate volume against the open mesh:** An open mesh has no exact interior volume (its signed volume depends on the coordinate origin). Close inputs before the boolean rather than wrapping an open boolean result.
7. **Unreferenced or unwelded vertices do not affect the geometry:** Closed triangle soups give the same boolean solid as welded meshes.
8. **Check the result itself with `MeshRobustRepair.NotCleanReason(result)`:** this is the check you have today, and it is cheap, about 0.1-0.6 times the boolean (`NotCleanReasons` / `FindDefects` list every failed check). See "What `NotCleanReason(result)` covers" below for what it says and what it does not.

### What `NotCleanReason(result)` covers

`NotCleanReason(result) == ""` says that the result mesh, as stored, is:

- not empty, closed, and manifold (no bowtie vertices);
- free of degenerate triangles (area below `1e-12` of the squared diagonal of the triangle bounds);
- consistently oriented, with no inside-out components;
- free of self-intersections: an **exact** search (`ExactTriangleIntersection`) on the doubles of the result, so it holds for the coordinates you will write or hand on, not only up to a tolerance.

What it does **not** say:

- **Nothing about the point set.** It looks at the result alone, not at the inputs. It does not say that the result is the union, intersection or difference of the inputs, nor that it lies within the rounding bound of the exact result. A solid that is missing (see the next point), moved, or wrongly selected still gives `""` if what is left is a clean mesh.
- **A removed component is not reported by it.** `SafeRounding` removes closed components that are tiny or thin and that it could not leave as they were (point 5 above). `NotCleanReason` sees the result alone, so it says `""` for a result that misses such a part. `RoundingClean` does report it: it is false when a removed component was a solid of at least 256 ulps and 1e-8 of the diagonal that the repair could not keep (`Rounding.RemovedResolvable`), and `Rounding.RemovedComponentInfos` lists every removed component (the audit case: a cube of edge 2^-11 next to a box of edge 1024).
- **Touching solids (edge or vertex contact) report "self-intersections".** The result stores the contact as manifold pairs of vertices that coincide in position (`Contacts`, `NonManifoldEdges`; see above), and the exact search counts those as intersections. A contact is not a defect of the boolean; compare with `Contacts` before you read it as one. Face contacts merge and do not show this. `ExactMeshBoolean.RoundingClean` exempts these contacts (see the `SafeRounding` bullets above).
- **Open inputs:** with `AllowOpenMeshes` the result is not a solid, and "closed" can fail by design.

`NotCleanReason` is a check of the result, not a certificate of the boolean. A test-only checker that recomputes the claim from the inputs exists for the test suite (`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`, [QA 2.4a](../qa/testing-and-invariance.md)); it costs several times the boolean and is not part of the library. Measurements: [BOOLEAN_CERTIFIED_MODE_2026-11.md](../research/BOOLEAN_CERTIFIED_MODE_2026-11.md) sections 4.1-4.4.

---

## Lazy Boolean Chains (`ExactMesh`)

- **Namespace & class:** `g3.ExactMesh` (`FromDMesh`, `Union`, `Difference`, `Intersection`, `Round`, `ToBoolean`)
- **Input / mutability:** Immutable lazy expression tree; evaluates into a new `DMesh3` on `Round()`.
- **Guarantees & limits:** Keeps intermediate faces in their exact original planes and runs a single n-ary exact boolean with one rounding pass at the end, avoiding cumulative rounding drift across chained operations.
- **Example:** [LazyExactMeshExample.cs](../examples/LazyExactMeshExample.cs)
- **Further reading:** [EXACT_BOOLEAN_40_DESIGN.md](../design/EXACT_BOOLEAN_40_DESIGN.md)

```csharp
using g3;

ExactMesh expr = ExactMesh.FromDMesh(cube).Difference(ExactMesh.FromDMesh(hole));
ExactMeshBoolean op = expr.ToBoolean();   // or expr.Round(out string error)
```

---

## Soup-to-Solid Pipeline (`SolidBooleanPipeline`)

- **Namespace & classes:** `gs.SolidBooleanPipeline`, `gs.SolidBooleanReport`
- **Input / mutability:** Read-only inputs (repaired on copies if requested); writes new `DMesh3` to `Result`.
- **Guarantees & limits:** Combines exact boolean (`AllowOpenMeshes = false`, `StrictRounding = true`) with a guarded `Reducer` pass (`PreventSelfIntersections = true`, `MinTriangleQuality = 0.02`, optional certified `MaxDeviation`).
- **Example:** [SolidBooleanPipelineExample.cs](../examples/SolidBooleanPipelineExample.cs)
- **Further reading:** [SolidBooleanBenchmark README](../../benchmarks/SolidBooleanBenchmark/README.md)

```csharp
using g3;
using gs;

var pipeline = new SolidBooleanPipeline(new[] { soupA, soupB }, ExactMeshBoolean.Operations.Union) {
    TargetFraction = 0.25
};
if (pipeline.Compute()) {
    SolidBooleanReport report = pipeline.Report;   // stage timings, NotCleanReason, StopReason
    switch (report.ResultKind) {
        case SolidBooleanResultKind.Solid:                // non-empty and clean
        case SolidBooleanResultKind.SolidWithContacts:    // the solids touch: valid, with duplicated vertices (report.OnlyContacts)
            DMesh3 solid = pipeline.Result;
            break;
        case SolidBooleanResultKind.SolidNotClean:        // another defect: report.NotCleanReason says which
            break;
        case SolidBooleanResultKind.EmptyExact:           // the exact result is empty: the solids do not overlap (after an InputGrid snap: see InputSnappedVertices)
            break;
        case SolidBooleanResultKind.EmptyAfterRounding:   // they overlap, but too thinly to keep: report.RoundingRemoved lists what was removed
            foreach (var removed in report.RoundingRemoved)
                Console.WriteLine($"removed {removed.Volume} of volume, thickness {removed.Thickness}");
            break;
    }
    if (report.InputsWithWindingOutsideZeroOne.Count > 0)
        Console.WriteLine("an input overlaps itself or is inside out: " + string.Join(", ", report.InputsWithWindingOutsideZeroOne));
} else {
    Console.WriteLine($"{pipeline.Status}: {pipeline.Error}");   // report.ResultKind is None
}
```

Do not read `Compute() && Report.Clean` as "the answer is right": an empty result and a result with contacts are `Success` with `Clean` false, and both can be correct. Read `Report.ResultKind` first. A compiled version of this check: `SolidBooleanPipelineExample.ReadTheResultKind`.

### The Kind of the Result (`Report.ResultKind`)

`SolidBooleanReport.ResultKind` (#1576) says what the last `Compute()` returned, as a fact next to `Status`. It judges nothing: `Status`, `Report.Clean`, `NotCleanReason` and `OnlyContacts` keep their meaning, and the kind is derived from them and from what the run removed.

| `ResultKind` | Meaning |
|---|---|
| `None` | no result: `Compute()` is false (`Status` says why), or it has not run |
| `Solid` | non-empty and clean (`Report.Clean`). A clean result from which the rounding removed some parts stays `Solid`: `Report.RoundingRemoved` (`Rounding.RemovedComponents`) lists them |
| `SolidWithContacts` | non-empty, and the only defects are contacts of the exact solid (`Report.OnlyContacts`; `NotCleanReason` says "self-intersections" at the touching points). A result whose crossings were not counted because the final check hit its pair cap (`SelfIntersectingTriangles` -1) is `SolidNotClean` even if its only defects are contacts (existing `OnlyContacts` behaviour) |
| `SolidNotClean` | non-empty with another defect: `NotCleanReason` says which, and with "self-intersections" `OnlyContacts` is false (a real crossing) |
| `EmptyExact` | empty, and nothing was removed: the exact result is empty (disjoint solids, or two boxes that touch in a plane, whose intersection has no volume). "Exact" means exact for the inputs as fed to the exact stage: with an `InputGrid` snap (or the Grid step) a thin overlap may have been closed by the snap, and the snapped solids only touch; `Report.InputSnappedVertices > 0` is the hint. `NotCleanReason` is "empty" as for any empty mesh |
| `EmptyAfterRounding` | empty, but the exact result had volume (or, for `ThinInputs = TreatAsEmpty`, possibly had volume) and the pipeline removed all of it: the rounding repair removed a component that its coordinates cannot resolve (`RoundingRemoved`: eg a 1e-12 thick overlap at coordinates of 0.5), or `MinThickness` removed it (`RemovedThinSolids`), or a thin input was treated as empty (`ThinInputs`; see below) |

Per route: the kind is read from the result that `Compute()` returns and from the report of that run. The Grid step (a retry that is the result), the fallback of the sheet role and an input repair give the kind of their own final result; a retry that was rejected leaves the first failure (`None`). An empty result of a Difference with a sheet is `EmptyExact` (nothing was removed). The rounding repair lists every closed component it removed (`RoundingRemoved`); an inside-out residue (`RemovalReasons.InsideOutResidue`, not a solid) does not make an empty result `EmptyAfterRounding`. With `ThinInputs = TreatAsEmpty` the exact stage never sees an input that is left out, so it cannot tell whether the result would have been empty: an empty result there is `EmptyAfterRounding` (the pipeline removed an input) if the result can shrink without that input (any operand of a Union, the first operand of a Difference, any input of an Intersection). The exact result may then have been empty too (an Intersection of a thin plate with a disjoint box is `EmptyAfterRounding`). A thin tool of a Difference cannot shrink the result when left out, so the exact result is a subset of the empty one: `EmptyExact`.

### Inputs With a Winding Number Outside {0, 1} (`Report.InputsWithWindingOutsideZeroOne`)

`Report.InputsWithWindingOutsideZeroOne` (#1576) lists the indices, as passed and ascending, of the closed inputs whose winding number is outside {0, 1} on some side of a piece of the exact arrangement: 2 or more where the input overlaps itself (two overlapping copies of a box in one input, a cavity wall given outward: the winding number inside the cavity is 2), -1 where it is inside out. It is read from the winding numbers that the classification computes anyway, so it costs nothing, and it is empty if none, for an open input or a sheet, if the exact stage did not run, and for pieces that the early-out of an Intersection or a Difference culled (outside the first input, or outside an input of an Intersection). It is also set for a run that fails after the classification (`InsideOutInput`, a `Verification` refusal, a `Cancelled` after the boolean), where `ResultKind` is `None`; it is empty for a failure of the boolean itself (`RoundingNotClean`, `OpenInput`, `BudgetExceeded`, `Cancelled` during the boolean). It is computed on the pieces the exact stage classified, with the early-out cull on (a stale winding value of an input could, in theory, flag a well-formed one with 3 or more inputs; no case found, and the results are not affected).

It flags self-overlapping or inside-out inputs, **not wrong results**: the result is the exact result for the inputs as given, in winding number semantics, and a flagged input is one whose meaning may differ from what the caller meant (an outward cavity wall is a union, not a cavity). Two overlapping boxes given on purpose in one input are flagged too. In the certification spike (research note `PIPELINE_CERTIFICATE_SPIKE_2026-11`, branch `research/pipeline-certificate-spike`) the same signal, measured with the spike's own witness (early-out cull off, before the drop of thin inside-out components), flagged all 20 silently wrong runs of its procedural corpus (a cavity wall given outward, a self-overlapping operand with a cavity given outward) and 31 correct ones, a precision of 39 %. A part that the pipeline drops before the boolean (eg the plate of #1567, see `DroppedInsideOutComponents`) is not flagged. `Status`, `Clean`, `NotCleanReason` and `OnlyContacts` are not changed by it.

### Key Options

- **Open tool of a Difference (sheet route, the default):** an open input of a Difference that is not the first and has no entry in `InputRoles` is tried as a sheet first (the [sheet role](#open-cutting-surface-sheet-role)): the first input is cut along it exactly and nothing is repaired. If the sheet role refuses, the run is done again with the inputs as solids, so `InputRepair` decides as before; with `Refuse` the status is `OpenInput` and `Error` ends with the sheet's refusal. Details: [the route and its report](#the-route-of-an-open-input-reportroute).
- **`InputRepair`:** Default `Refuse` fails with `OpenInput` if an input fails `ExactMeshBoolean.IsWindingClosed` and is not used as a sheet. Opt-in `AutoRepair` repairs small holes on a copy (use only for solids, not open cutting sheets); `AutoRepairThenRobust` adds `MeshRobustRepair` as fallback (`Report.RepairedInputs`). A `MeshAutoRepair` copy that self-intersects where the input as given does not (its fill crosses the input, eg the fill of an open sheet's rim) is refused with `OpenInput`, also with `AutoRepairThenRobust` (no fallback to `MeshRobustRepair`); an input that self-intersects as given is used as before.
- **The volume the rounding moved (`Report.RoundingVolumeBefore`, `RoundingVolumeAfter`):** the signed volume of the boolean's result before and after its rounding repair (`MeshRoundingRepair.VolumeBefore`, `VolumeAfter`, also on `ExactMeshBoolean.Rounding`). Equal when the repair changed nothing; both 0 when no rounding repair ran. The repair bounds the distance the surface moves, not the volume: on a thin solid the difference can be of the order of 1 % while the run is a clean success, see [ROUNDING_THIN_VOLUME_2026-11.md](../research/ROUNDING_THIN_VOLUME_2026-11.md). Compare the difference with the volume that matters to you (the result's, or the thin part's).
- **Verification of the result (`Verification`, opt-in):** `NoInventedSurface` refuses a result with surface that lies on none of the inputs as passed (a repair made it up), `RoundingVolume` refuses a rounding that moved the volume of a thin result; status `VerificationFailed`. Off by default, and then it costs nothing. See [Verification](#verification-of-the-result-verification).
- **Reduction targets:** Set one of `TargetTriangleCount`, `TargetFraction`, `MinEdgeLength`, or `MaxQuadricError`. `Report.StopReason` and `StopDetail` explain why reduction stopped if the target was not reached.
- **Contact-line pairing (`PairContacts` / `PairContactsMode`):** Touching solids limit reduction along contact lines. Opt-in `PairContacts = true` (`PairContactsMode.On`) collapses twin edges along contact lines together (`Reducer.PairCoincidentVertices`) while preserving all guards (`Report.PairedCollapses`; cannot combine with `MaxDeviationTwoSided`). `PairContactsMode.Auto` retries with pairing only when the first pass leaves contacts and is not clean (`Report.PairPassRan`, `PairPassKept`).


### The Route of an Open Input (`Report.Route`)

`Report.Route` lists what the pipeline did with open inputs, as steps in the order taken. Each `SolidBooleanRouteStep` has a `Name`, the `Input` it is about (or `-1`), a `Reason` and a `Change` (what the step changed), for a fallback the sheet's `SheetReason`, and for a grid step the cell `Grid`. The list is empty when no input was tried as a sheet, repaired or refused as open and no grid step ran.

| Step | Taken when | What it changes |
|---|---|---|
| `SheetRole` | An input of a Difference is not closed (`IsWindingClosed`; this includes a closed surface with flipped triangles, which the sheet role then refuses), is not the first, and `InputRoles` has no entry for it. | The input is handed to the boolean with the role `Sheet`: not repaired, the first input is cut along it. |
| `Grid` | By default: the sheet role refused an open tool it had routed itself with `RimInsideWorkpiece`, or a run ended with `RoundingNotClean` (the failure is in `Reason`); with `AutomaticSteps = Grid` also a sheet stated in `InputRoles` that refused. | The inputs are snapped to the cell in `Grid` and the run is done again; see [the Grid step](#the-grid-step-automaticsteps). |
| `Fallback` | The sheet role refused (`SheetReason`, the boolean's text in `Reason`), eg `RimInsideWorkpiece`, `NotSurface`, `OrientationInconsistent`, `SidesInconsistent`, `UncutComponents`, `FloatingComponent`, or `WorkpieceNotClosed` for an open first input. | The run is done again with the inputs as solids, as without the route. |
| `InputRepair` | An open input was closed on a copy (`InputRepair` is `AutoRepair` or `AutoRepairThenRobust`). | The repair method and the triangle counts, as in `Report.RepairedInputs`. |
| `Verification` | `Verification` is not `None` and a result exists. | Nothing: it says which checks ran with their numbers, and "refused" when a limit was exceeded; see [Verification](#verification-of-the-result-verification). |
| `Refused` | An open input is left: `InputRepair` is `Refuse`, or no allowed repair closed it, or `MeshAutoRepair` closed it into a self-intersecting copy (the input as given has none). | No result, status `OpenInput`. |

- **Stated roles are used as stated.** An entry in `InputRoles` (`Solid` or `Sheet`) switches the route off for that input: `Solid` gives the behaviour without the route (refused or repaired as an open input), `Sheet` gives the sheet role with its own refusal (`BooleanFailed`, `SheetReason`) and no fallback.
- **Several open tools** are all tried as sheets; one refusal sends the run to the fallback for all of them.
- **The orientation of the sheet is taken as given.** The part of the first input behind the sheet is removed. An open tool whose normals point inward (a bucket turned inside out) therefore removes the other part than its repaired, reoriented copy would, with `Success`. The `SheetRole` step shows it: after a cut its `Change` ends with the share of the first input's volume that the Difference removed ("removed 81.25 %" where 18.75 % was meant). State the role `Solid` for a tool that is a solid with a hole and may be oriented either way.
- **Why the orientation is followed (owner decision 2026-11-23, prior-art check):** every system found that gives "solid minus open surface" a side takes it from the surface's orientation or from an explicit side switch, and none reorients the cutter by a rule: SolidWorks Cut With Surface (a flip flag), Onshape Split (keep one side, an opposite-direction arrow), NX Trim Body (reverse the trim direction), MCUT (fragments above or below by the cut mesh's winding order), and the plane trims of CGAL (`clip` keeps the negative side) and Manifold (`TrimByPlane` keeps the side the normal points to). The others refuse an open tool or split without a side (CGAL `split`, OpenCASCADE Splitter); see [the study](../research/OPEN_CUTTING_SURFACE_2026-11.md), section 3.
- **Only a refusal of the sheet falls back.** Any other failure of the boolean with the sheet (`BudgetExceeded`, a strict rounding refusal) is the result of the run.
- **`MinThickness`:** an open tool without a stated role is measured as before (a flat sheet has no volume, so it is thin), and `ThinInputs` decides as before; a tool left out as thin is not routed. Only a stated sheet is not measured.
- **`ConfigureBoolean`** runs once more for the run after a refused sheet.
- **Unchanged:** inputs that pass `IsWindingClosed`, Union and Intersection, and an open first input with closed tools.
- **Cost of a refusal:** the sheet attempt is one more arrangement of the inputs before the run without the route; its time is part of `Report.BooleanMilliseconds`.

### The Grid Step (`AutomaticSteps`)

`AutomaticSteps` takes flags: `None` (no step), `GridBeforeFallback` (2), `GridAfterRoundingNotClean` (4) and `Grid` (1, which includes both and adds the retry of a stated sheet). **The default is `GridBeforeFallback | GridAfterRoundingNotClean`** (owner decision 2026-11-24: on private inputs the runs that the narrower default still refused with `RoundingNotClean` all succeeded with the grid retry, nearly all with the right volume). A failure is answered with one retry on a snapping grid:

- the sheet refusal `RimInsideWorkpiece` of an open tool that the pipeline routed to the sheet role itself (no entry in `InputRoles`): typically a rim that is meant to lie in a face of the first input but sits a few rounding units inside it, far from the origin; the capture distance is about one cell (see the known risk below). This is `GridBeforeFallback`, on by default. Its limit, measured: it helps where the rim lies in a face parallel to a coordinate plane, or where every rim vertex coincides with a vertex of the workpiece (the snap makes them identical in any orientation, also in a rotated frame); a rim with a vertex in the interior of a slanted face or on an edge of the workpiece (not at a vertex) keeps its refusal, because the snap rounds each coordinate on its own, so a face that is not parallel to a coordinate plane is not a plane of the grid. The retry comes before the `Fallback`, whatever `InputRepair` is, so the open tool is not closed by the repair when the grid cuts it exactly. `AutomaticSteps = None` gives the old behaviour (straight to the fallback; with `InputRepair = Refuse`, status `OpenInput`).
- the boolean's `RoundingNotClean` (`GridAfterRoundingNotClean`, on by default), on any route and also after the fallback: the strict rounding refused the result, and the run is done again on the snapped inputs. Where the first failure is the refusal of a stated sheet, this bit does not answer it.
- with `Grid` (opt-in; it includes both) also a sheet role that the caller stated in `InputRoles`. The default does not retry it, so a caller who states roles keeps the refusal of a stated sheet as before (the decision of the first review of this step, kept when `RoundingNotClean` was added to the default).

**What a result of the grid retry is.** A default `Success` can now come with `Report.Clean == false` and `Report.OnlyContacts == true` (a valid solid whose parts touch along contacts): a caller who needs a clean solid must check `Report.Clean`, not only `Status`. The result is within about one cell of the Boolean of the given inputs; a wall about 64 cells thick (the limit of the guard) can change its thickness by up to about 1.6 %. It is the Boolean of the SNAPPED inputs, not of the given ones: every vertex moved by up to half a cell per coordinate (within one cell of the given geometry), and the result can differ from the exact one by about that much (a volume by about the area times the cell). The route says so: the `Grid` step names the cell, the vertices moved and the largest relative size change. `Report.RoundingVolumeBefore` and `RoundingVolumeAfter` are those of the run that is the result (the retry), and the result can be reported as only contacts (`Report.OnlyContacts`, a valid solid whose parts touch along contacts). `AutomaticSteps = None` gives the exact run and its refusal; `GridBeforeFallback` keeps the sheet retry alone.

A run without a refused sheet is the run without the option, bit for bit.

The retry is the same run with the inputs snapped as `InputGrid` snaps them. The cell is taken from the data: a power of two, 2^12 times the rounding unit (ulp) of the largest absolute coordinate of the vertices that triangles use (a vertex that no triangle uses does not count) in all inputs. It scales with the inputs and moves a vertex by at most 4.5e-13 of that coordinate. [PIPELINE_GRID_STEP.md](../design/PIPELINE_GRID_STEP.md) derives it.

```csharp
var pipeline = new SolidBooleanPipeline(new[] { workpiece, openTool }, ExactMeshBoolean.Operations.Difference) {
    AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.Grid     // the default, GridBeforeFallback | GridAfterRoundingNotClean, leaves a stated sheet refused
};
bool ok = pipeline.Compute();
foreach (SolidBooleanRouteStep step in pipeline.Report.Route)   // "SheetRole", "Grid", "SheetRole"
    Console.WriteLine($"{step.Name}: {step.Reason} -> {step.Change}");
```

- **Only after a failure, once.** A run that succeeds is never retried. There is one retry per `Compute` with one cell.
- **The report:** `Report.Route` gets a step `Grid`: `Reason` is the failure it answers, `Change` the cell, the number of vertices the snap moved (`Report.InputSnappedVertices`) and the largest relative change of an input's size, `SheetReason` the refusal it answers (after a refused sheet), and `Grid` the cell as a number.
- **The result is checked like any other** (`Report.NotCleanReason`, `Report.Clean`).
- **If the retry fails too, the first failure is the result** (status, error and report of the run without the grid); the `Grid` step says that the retry failed and how. A cancelled retry is `Cancelled`.
- **Before the fallback:** a tool that the sheet route tried and that was refused with `RimInsideWorkpiece` is retried on the grid first, as a sheet. If the sheet is refused there too, the retry ends: the inputs are not tried as solids on the grid. The run then goes on as without the step, with the `Fallback` to solids off the grid, and a failure of that fallback is not retried (the step ran once).
- **Inputs on the cell already** (the snap would move no vertex) are not run again: the `Grid` step says so and the first failure is the result.
- **`InputGrid` set:** the step does not run; the caller's grid is used as stated.
- **Limit:** a snap makes coordinates equal. It cures a rim on a face parallel to a coordinate plane and vertices meant to coincide, not a rim a little inside a slanted face. As with `InputGrid`, features thinner than the cell can collapse; the cell follows the largest coordinate of all inputs, so a small input near the origin next to one far from it gets the coarse cell of the far one.
- **The default's cost:** a run that succeeds, with or without a sheet, costs nothing extra (the retry is reached only from a failure: the code path of a success does not touch it). A refused sheet costs at most one more boolean, and a run that ends with `RoundingNotClean` costs one more boolean on the snapped inputs (plus the measure of each input's size for the guard). A refusal other than `RimInsideWorkpiece` and `RoundingNotClean`, and a stated sheet, is not retried by the default.
- **Known risk of the default: a result on the grid is not the exact one, and the capture distance is about one cell, not a few ulps.** The snap moves the first input's faces by up to half a cell too, so a rim, gap or skin of up to about one cell (`2^12 ulp` of the largest coordinate: 3.8e-6 at coordinates of 5.7e6, 7.6e-6 at 1.2e7, 0.5 at 1e12) between a tool and a face is closed, also where it was meant. That can change the topology of the result. Pinned by tests, with `None` next to it: on a block of 0.1 far from the origin (coordinates about 6e5, 5.7e6, 1e2; cell 3.8e-6), a horizontal sheet that ends 1 or 1.5 micro-units short of the faces by design cuts the block in half by default (`None` with `Refuse`: `OpenInput`; with `AutoRepair`: nothing is cut); an open bucket that leaves a skin of 1e-6 under the top face gives an open pocket (one shell) by default and an enclosed cavity (two shells) with `None` and `AutoRepair`. The way out is `AutomaticSteps = None`. `Report.Route` shows the `Grid` step with its cell (`Grid`), the vertices moved and the largest relative size change (`Change`), so a result that went through the grid can be told from one that did not.
- **Guards:** the step is not run where the cell is more than 1/64 of the size of any input, so that the snap does not distort a small tool or a thin first input: the size is the thickness `2V/A` of a closed input (what `MinThickness` measures) and the width `2A/L` of an input that is not winding-closed (`L` the length of its boundary edges by position: the width of a strip; about 2.5 times the edge of an open box; half the side of a square sheet). The `Grid` step says which input stopped it and the first failure is the result. For scale: a block of 10 at 1.2e6 is at about 4e-7 of the share, a block of 0.1 at 5.7e6 at 2e-4, a plate of 1e-3 at 5.7e6 to 1.2e7 at 4e-3 to 8e-3 (the share 1/64 = 1.6e-2 is a judgement, the owner's call). A retry whose result is empty although the first input is not is rejected the same way (a cut that removes the whole first input is therefore not taken on the grid). Both guards apply to every retry, that after `RoundingNotClean` (default) as well as the sheet retry. The guard sees an open first input by its width; the default trigger cannot reach one (the sheet role refuses it with `WorkpieceNotClosed`), the opt-in `Grid` can.
- **Stated roles:** only a refusal of a sheet that the pipeline routed itself is answered by the default; the boolean names the refused sheet, and a sheet stated in `InputRoles` keeps its refusal (`Grid` retries it). The `RoundingNotClean` retry does not depend on the roles.
- **Cost:** the retry is one more run (`ConfigureBoolean` runs again for it: three booleans for a refused rim that the grid does not cure, the sheet, the grid and the fallback). The stage times of both runs (input check, repair, boolean) are in the report of the one that is the result. An undefined value of `AutomaticSteps` (a bit other than `Grid`, `GridBeforeFallback` and `GridAfterRoundingNotClean`) is `InvalidInput`.

### Verification of the Result (`Verification`)

The pipeline can return `Success` with a result that is wrong in a way its own report does not show. `Verification` (a flags value, default `None`) measures two such cases on the finished result, reports the numbers in the `Report` also when the result passes, and refuses the result (`PipelineStatus.VerificationFailed`, `Result` null, `Error` naming the check, the measured value and the limit, `Report` and its route kept) when a limit is exceeded. With `None` nothing is built or measured and the run is the run without the option, bit for bit. The checks run on the run that is the result (after a Grid retry or the fallback); a value with unknown bits, or a negative or NaN limit, is `InvalidInput`.

| Check | What it measures | Limit (default) | Report |
|---|---|---|---|
| `NoInventedSurface` | The share of the boolean result's area in triangles with a sample farther than a tolerance from every input as the caller passed it. | `MaxInventedSurfaceShare` (0: any invented triangle refuses; `PositiveInfinity` reports only) | `InventedSurfaceShare`, `InventedSurfaceArea`, `InventedTriangles`, `InventedSurfaceMaxDistance`, `InventedSurfaceTolerance` |
| `RoundingVolume` | The volume the rounding repair moved, relative to the volume before it: `RoundingVolumeChange` = abs(After - Before) / abs(Before) (+infinity for a Before of 0 and a change). | `MaxRoundingVolumeChange` (1e-3), with a floor (a share of the inputs, and the parts thinner than the rounding length) | `RoundingVolumeChange`, next to `RoundingVolumeBefore` and `RoundingVolumeAfter` |

```csharp
var pipeline = new SolidBooleanPipeline(new[] { workpiece, openTool }, ExactMeshBoolean.Operations.Difference) {
    InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair,
    Verification = SolidBooleanPipeline.VerificationModes.NoInventedSurface | SolidBooleanPipeline.VerificationModes.RoundingVolume
};
if (!pipeline.Compute() && pipeline.Status == SolidBooleanPipeline.PipelineStatus.VerificationFailed)
    Console.WriteLine(pipeline.Error);      // eg "NoInventedSurface: 0.033 of the result's area (2 triangles, area 4) lies farther than 2.1e-4 from every input as passed ..."
```

**`NoInventedSurface`.** A Boolean only keeps pieces of the surfaces of its inputs, so surface that lies on none of them was made up. The typical case (observed by the owner on private inputs): an open tool of a Difference that the sheet role refuses is closed by `InputRepair` (`MeshAutoRepair` fills its holes), and the result contains the lid that the repair invented (a bucket inside a block: the cavity has a lid that no input has). The check samples every triangle of the result at its three vertices, its centroid and its three edge midpoints; the triangle counts as invented when any sample is farther than the tolerance from every input, sheets and open inputs included, taken as the caller passed them (before the input repair, the grid snap and any reorientation). `InventedSurfaceShare` is the area of the invented triangles over the area of the result, `InventedSurfaceArea` that area and `InventedTriangles` their number (the step's text has both too), `InventedSurfaceMaxDistance` the largest distance of a sample to an input (for a sample that the fast path accepted, see The distance below, the exact distance to the triangle it accepted: an upper bound of the nearest distance, at most the tolerance; for a far sample the exact nearest distance).

- **The default limit is 0 (owner's call, after the review).** The tolerance is a real bound times 3, so a correct result has share exactly 0 and any invented triangle is evidence; a share limit such as 1e-3 mainly hides small patches (a lid of 1 on a workpiece of 1000). Set `MaxInventedSurfaceShare` above 0 to accept a share, to `PositiveInfinity` to measure and report only.
- **The tolerance** is what the pipeline itself may legitimately move a point by, times a safety factor of 3: the bound the rounding repair applied (`MeshRoundingRepair.MaxLength`: `RoundingMaxDisplacement` times the diagonal of the largest component of the mesh before the repair, the same for all components, so it is not the diagonal of the result after `MinThickness` or the repair removed that component), or 256 ulp (the repair's own ulp term, for rounding the exact points to doubles) of the largest used coordinate of the result and of the inputs if that is more (the distance to a reference triangle is computed at the magnitude of the reference's coordinates), plus the diagonal of the grid cell when `InputGrid` or the Grid step snapped the inputs (the result lies on the snapped surfaces; the snap moves a vertex by at most half that diagonal). It scales with the inputs: the share and the scaled tolerance are the same bit for bit under scaling by 2^k. `Report.InventedSurfaceTolerance` has the value; one that is NaN or infinite is a failed verification, never a pass.
- **The distance** to the inputs is the exact distance to the nearest reference triangle. A search of the AABB tree bounded by the tolerance decides the usual sample, one on an input, and a sample that it finds within the tolerance is within it for certain. The tree ranks triangles by a squared distance whose absolute error is about `eps E^2` for a triangle of size `E`, so for a triangle much larger than the tolerance (a big face with a tiny cube on it) the bounded search can return a wrong triangle; every sample that the fast path does not show to be within the tolerance is therefore decided by a second search that ranks by the exact closest point, over all inputs. Only far samples pay for it (on the ordinary test inputs none does).
- **What is measured.** The boolean's result after `MinThickness` and before the reduction. The reduction is guarded by its own `MaxDeviation` and repairs nothing; without `MaxDeviation` it has no bound that a tolerance could state. So when a reduction ran, `Result` itself is not checked: the check is of the Boolean result that the reduction started from. `MinThickness` removes whole solids and moves nothing.
- **A correct result is not flagged,** because every point of it lies on a surface of an input up to three moves, each bounded by what the tolerance holds: the rounding repair (its applied bound `MaxLength`), rounding exact points to doubles (256 ulp at the magnitude of the inputs) and the snap (the diagonal of the cell). Measured: boxes, spheres, coplanar, touching and contained solids, a sheet cut, a sheet cut through the Grid step far from the origin, `MinThickness`, the thin plates whose rounding moves vertices, and a tiny cube on the face of a box of 1e4 to 1e5 (also rotated) all have share 0.
- **What it can miss** (a sampled, one-sided check, not a proof): an invented region whose samples all lie within the tolerance of some input surface (the spacing of the samples is the size of a result triangle: a point inside a triangle can be farther from the inputs than the tolerance, by up to about a quarter of the longest edge, while all 7 samples pass, and the share counts whole triangles, so a triangle that is invented only in part is counted whole or not at all); invented surface that happens to coincide with another input's surface (a lid that lies in a face of the workpiece); and LOST surface: the check goes from the result to the inputs, so a repair or the rounding that removes surface, or a lost component, is invisible to it. A tolerance that includes the grid cell is wide: with the Grid step it is 3 x (max(`MaxLength`, 256 ulp) + sqrt(3) x cell) with a cell of 2^12 ulp of the largest coordinate, so at least about 22,000 ulp of it (3 x (256 + 7,094)); with `InputGrid` it is 3 x sqrt(3) times the caller's cell and more. Invented surface closer than that to an input is not seen on a snapped run.
- **A repaired hole counts as invented surface.** With `Verification = NoInventedSurface` the result is compared with the inputs as given. If the input repair closed a hole, the closing surface is not input surface, so the part of it that ends up in the result is counted, and the default limit `MaxInventedSurfaceShare = 0` refuses the run (`VerificationFailed`). That is intended for a cutting sheet that the fallback closed with a cap (the result is then usually wrong), but it also refuses a body with a pinhole (for example one missing triangle) whose repair is the obvious one: the result is then the right Boolean of the repaired body. The share cannot tell the two apart. A caller who accepts small hole repairs sets `MaxInventedSurfaceShare` to the share they accept and reads `Report.InventedSurfaceArea`, `InventedTriangles` and `InventedSurfaceMaxDistance` to see what was added, or repairs the inputs first and passes the repaired meshes. Without `Verification` nothing is measured or refused. Pinned in `SolidBooleanPipelineInventedSurfaceHoleTests`.
- **Cost:** one AABB tree per input and seven nearest-triangle queries per result triangle, serial: O(result triangles x log input triangles), about 6 microseconds per result triangle on ordinary inputs (measured on a machine loaded by other work). The numbers are in [the benchmark README](../../benchmarks/SolidBooleanBenchmark/README.md).

**`RoundingVolume`.** The rounding repair bounds how far the surface moves, not the volume: on a thin result a contraction across a thin strip can change the volume by about 1 % while the run is a clean success ([the study](../research/ROUNDING_THIN_VOLUME_2026-11.md)). The check takes dV = abs(`RoundingVolumeAfter` - `RoundingVolumeBefore`) of the run that is the result and fails when both hold: dV is more than `MaxRoundingVolumeChange` times abs(`RoundingVolumeBefore`) (a Before of 0 and any dV: the change is reported as `PositiveInfinity` and the step says "Before = 0"), and dV is more than a floor, after the change of the thin parts is taken out of dV (below). **The floor** is the share: 1e-6 of the smallest abs(volume) of the winding-closed inputs as passed (without such an input, 1e-9 of the cubed diagonal of the inputs; the sizes are those of the vertices that triangles use). It says that a change that is immaterial against the inputs is not refused however large its share of a tiny result; it is not a bound on slivers. **The thin parts, each by its own change.** The rounding repair records the closed components of the boolean's result as it found them, with their volume and area, and the pipeline matches the components after the repair to them by their vertices (the repair adds no vertex and edits within a component). A part is thin when 2 abs(volume) / area is at most its own rounding length ell = max(`RoundingMaxDisplacement` x the diagonal of the part, 256 ulp of its largest coordinate), both before and after the repair (a part that the repair removed is thin when it was thin before): the length that the repair's bound would have if the part were alone. A rounding that moves every vertex by at most ell changes the volume of a closed component of area A by at most ell x A, so such a part may lose or gain all of its volume legitimately (the repair removes a face-wide slab of 1e-9 to 1.5e-8, and a result slab of 1e-12 grows to 3e-12 by the rounding). The signed change of each thin part is taken out of dV; the rest, the change of the parts that are not thin, is compared with the limit and the floor, so **a thin part never hides a loss of a thick one**: the thin plates beside a separate 1 x 1 x 1e-6 or 10 x 10 x 1e-6 slab, near or far from the origin, are refused like the plates alone (an earlier version added length x area of every thin part to one floor: 100 times the plates' loss for the small slab, 0.029 of volume for the large one). The length is the part's own, not the largest component's: what is said of a part does not depend on the other parts. The step prints the number of thin parts, their change and the change of the rest. A part whose vertices name no single part before (a part that touches another one at a vertex only, a part that is open) takes nothing out, so the answer errs towards refusing. **What the check still catches:** a part that is thicker than its own length and whose volume the rounding moves by more than `MaxRoundingVolumeChange` (of the whole result) and more than the share floor (the thin plates: 0.7 %), a thick part that disappears, and a volume that is not finite (overflowing coordinates: the run fails as `VerificationFailed`, never passes; in practice the exact boolean refuses such coordinates first, `BooleanFailed`). What it can no longer catch: a loss inside a part that is itself thinner than its own length, however large its share. `RoundingVolumeChange` is not exactly 0 whenever a repair ran: a repair that contracts an edge (in a rotated frame, where the rotation split edges of the exact result) leaves another triangulation of the same surface whose signed volume differs in the last bits (about 2e-16 relative for two cubes), so read it against the limit, not against 0. If no rounding repair ran (`SafeRounding` off in `ConfigureBoolean`, or an early exit) the check passes with change 0 and the step says "no rounding".

- **Backed by:** the thin plates of `RoundingThinVolumeTests` (a union of two plates, one 2e-5 thick): the rounding moves 0.71 % of the volume, so the default limit refuses the result; a limit of 0.01 or `PositiveInfinity` passes it.
- **Before and After are of the whole boolean result,** the solids that `MinThickness` removes afterwards included, and a large solid in the result hides the relative loss of a thin part: in the plates the thin plate loses 1.85 % of its own volume, the whole result 0.7 %. The check is not a per-solid check.
- **By design it refuses a legitimate thin result whose rounding moved a visible share.** The loss of a contraction is about the bound times the area of the fan it pulls over, divided by 3, so relative to a solid of thickness t it is about the move over 3 t: a Difference that leaves a slab of 1e-3 whose rounding contracts an edge by 1e-5 loses about 0.3 % of it and is refused at the default limit although it is not wrong in any other sense. Raise `MaxRoundingVolumeChange` for such results, or read `RoundingVolumeChange` with `PositiveInfinity`.
- **Not covered:** the volume that `MinThickness` and the reduction change after the rounding (`RoundingVolumeBefore` and `After` describe the rounding only).
- **Cost:** passes that are not free: the floor asks each input for its winding closure as passed and measures its volume and area, the repair records its components before the first edit, and the thin parts are found by labelling the components of the boolean's result and measuring each one. Measured (two spheres, union, a machine loaded by other work): the whole check is 35 to 41 ms for 150,000 input and 98,000 result triangles (17 to 41 ms for 36,500 input and 24,700 result triangles), against a boolean of 470 to 690 ms: about 6 to 8 percent. `Report.VerificationMilliseconds` has the time of the run that is the result (not that of a retry or fallback run that was replaced). With `Verification = None` none of it runs (no pass, no allocation, no report field).

---

## Exact Surface Imprinting (`ExactMeshImprint`)

- **Namespace & class:** `g3.ExactMeshImprint`
- **Input / mutability:** Read-only target and cutter; writes split target into `Result` while preserving target vertex IDs, orientation, and topology.
- **Guarantees & limits:** Computes exact intersection curves (including contact and coplanar overlaps) without classifying or deleting triangles. Rounds new vertices with `SafeRounding` (target vertices fixed, curve edges locked, verified by `RoundingClean`). Handles open targets and cutters; does not repair pre-existing self-intersections in the target.
- **Example:** [SolidBooleanPipelineExample.cs](../examples/SolidBooleanPipelineExample.cs)
- **Further reading:** [EXACT_IMPRINT.md](../design/EXACT_IMPRINT.md)

Key outputs: `Result`, `SourceTriangle` (maps output triangles back to input target triangles), and `CurveEdges` (with optional `LabelCurves` numbering each curve).

---

## Host GC Tuning for Very Large Booleans

Large exact booleans allocate heavily during classification. Workstation GC (the default for console/desktop .NET apps) can spend a substantial share of wall time in GC pauses on very large inputs. Applications running large booleans can enable Server GC in their host configuration (this is a host setting, not a library setting):

- Project file: `<ServerGarbageCollection>true</ServerGarbageCollection>`
- `runtimeconfig.json`: `"System.GC.Server": true`
- Environment variable: `DOTNET_gcServer=1`

Note that Server GC increases peak memory usage and can be slightly slower on small inputs. See [EXTREME_RUNTIME_BOOLEANS.md](../perf/EXTREME_RUNTIME_BOOLEANS.md) for measured trade-offs.
