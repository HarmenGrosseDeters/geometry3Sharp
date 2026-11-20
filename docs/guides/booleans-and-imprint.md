# Booleans & Surface Imprinting

This guide covers exact mesh booleans (`ExactMeshBoolean`), lazy boolean chains (`ExactMesh`), soup-to-solid boolean pipelines (`SolidBooleanPipeline`), exact surface imprinting (`ExactMeshImprint`), and result validation for downstream solid workflows.

---

## Choosing a Boolean or Cut Class

| Goal | Use | Output & key properties |
|---|---|---|
| Exact union, intersection, difference, XOR, or n-ary winding predicate on meshes | `g3.ExactMeshBoolean` | New `DMesh3` in `Result`; exact arrangement, `SafeRounding` on by default |
| Resolve self-intersections, nested shells, and duplicate faces in one mesh | `ExactMeshBoolean.Resolve(mesh)` | New clean `DMesh3`; reorients inconsistent components by area majority |
| Chain several booleans with a single final rounding pass | `g3.ExactMesh` | Lazy expression evaluated in one exact n-ary pass (`Round`, `ToBoolean`) |
| Exact boolean of closed triangle soups → small clean solid without manual pre-repair | `g3.SolidBooleanPipeline` | Closed solid + `SolidBooleanReport` (boolean + guarded `Reducer` pass) |
| One body against many (`A ∩ Bi` or `A − Bi` per body) | `ExactMeshBoolean.IntersectEach` / `DifferenceEach` | Parallel per-body array; bit-identical to individual runs |
| Cut intersection curves of a cutter into a target without removing anything | `g3.ExactMeshImprint` | Target mesh with split triangles, kept vertex IDs, `CurveEdges`, `SourceTriangle` |
| Approximate boolean of heavily broken/open meshes | `gs.MeshWindingNumberBoolean` | Remeshed closed surface via adaptive octree winding numbers |

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

Putting multiple operands into a single n-ary operation (`A − (B1 ∪ … ∪ Bn)`) runs one arrangement and rounds only once, which is both faster and produces fewer triangles than chaining binary booleans.

For closed first meshes, `Difference` and `Intersection` automatically cull operand triangles wholly outside the relevant solid (`Counts["t3a_culled"]`). Diagnostic counts (`IntersectingPairs`, `SelfIntersectingPairs`, `Patches`) describe the arrangement after culling; use `Union` or `MeshRobustRepair` to inspect self-intersections across an entire operand.

### Batch One-Against-Many (`IntersectEach` / `DifferenceEach`)

```csharp
var statuses = new ExactMeshBoolean.BooleanStatus[tools.Count];
DMesh3[] intersections = ExactMeshBoolean.IntersectEach(terrain, tools, b => b.SafeRounding = true, statuses);
DMesh3[] differences = ExactMeshBoolean.DifferenceEach(part, tools);
```

Each entry is bit-identical to running an independent boolean against `terrain` / `part`, while validating and orienting the base mesh only once and running bodies in parallel.

### Input Orientation & Open Meshes

- **Inconsistent orientation (`Orientation`):** Closed inputs with flipped triangles are refused by default (`Orientation = Refuse`, `Status = InconsistentOrientation`) because inside vs. outside is ambiguous. Set `Orientation = AreaMajority` to reorient each component by its larger area part (`ReorientedTriangles`). `ExactMeshBoolean.Resolve(mesh)` always reorients as a repair operation.
- **Open inputs (`AllowOpenMeshes`):** Enabled by default (`true`). Open inputs are classified by generalized winding number (`> 0.5`; `ExactWinding[i]` is `false`), using the exact Antipodal Method below 20,000 boundary segments (hierarchical above 1,000 segments) and fast winding numbers above that cap. Set `AllowOpenMeshes = false` to reject open inputs with `Status = OpenInput`. Where an open boundary meets other sheets along an edge with conflicting orientations, pieces are left unjoined (`ConflictEdges`, always `0` for closed inputs).

### `SafeRounding` & `StrictRounding`

Nearly coincident inputs can create exact vertices that round to colliding `double` positions, leaving slivers or self-intersections. `SafeRounding` (on by default) runs `MeshRoundingRepair` after rounding and verifies the output exactly (`RoundingClean`):

- **Displacement bound:** Every untouched triangle is the exact rounded output. Repaired edits stay within `Rounding.MaxDrift <= RoundingMaxDisplacement * diagonal` (default `1e-5` of the largest connected component's diagonal, plus a few ULPs far from the origin controlled by `RoundingUlpFactor` or `RoundingAbsoluteTolerance`). Edits exceeding the bound are rejected.
- **Effort budget:** Controlled by `RoundingMaxPairTests` (default 5M exact pair tests, scaling with initial defects via `RoundingPairTestsDefectScale` and `RoundingPairTestsMaxGrowth`).
- **Strict mode:** Set `StrictRounding = true` so an unclean result fails with `Status = RoundingNotClean` (no `Result`) rather than returning remaining defects (`Rounding.RemainingIntersections`, `RemainingDegenerate`, `RemainingFalseContacts`, `ScaleDisparity`).
- **Speed / exact output:** Set `SafeRounding = false` for plain rounding (faster, or when consuming `ResultExactVertices` directly).

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

### By-Design Behavior (Not Defects)

- **Open inputs yield open results:** The region where generalized winding number `> 0.5` is generally not closed; boundary edges remain where pieces of the open input survive. Close inputs first (`MeshRobustRepair`, `MeshAlphaWrap`) or set `AllowOpenMeshes = false` if a closed solid is required.
- **Edge-touching solids duplicate shared vertices:** Two solids sharing only an edge (e.g., two boxes meeting along an edge) are represented in memory as a closed, manifold `DMesh3` with separate vertex copies along the contact (`NonManifoldEdges`, `Contacts`). Welding by position (e.g., writing STL) creates a 4-triangle edge. Call `MeshRobustRepair.SeparateTouchingVertices(mesh, fraction)` if a strictly manifold welded mesh is required. By contrast, *false contacts* (distinct exact vertices that rounded onto the same `double`) are separated automatically by `SafeRounding` (`Rounding.RemainingFalseContacts == 0`).

---

## Validating Boolean Results (Solid Workflows)

For workflows that feed boolean outputs into downstream solid tools, check results in this order:

1. **Validate or repair inputs first:** Run `MeshRobustRepair` (or verify `MeshRobustRepair.NotCleanReason(mesh) == ""`) and compute with `AllowOpenMeshes = false` so open inputs fail explicitly (`OpenInput`).
2. **Check status and rounding:** Require `Status == Success` and `RoundingClean == true` (or set `StrictRounding = true`).
3. **Check `Contacts` and `NonManifoldEdges` before position-welding formats:** Face contacts merge cleanly; edge/vertex contacts carry duplicated vertices. Use `MeshRobustRepair.SeparateTouchingVertices` when strictly manifold welded output is needed.
4. **Negative-volume components are voids:** An inner cavity shell is oriented inward and has negative signed volume. Sum signed volumes across connected components rather than taking absolute volumes per component.
5. **Thin slabs are valid pieces of the exact solid:** Slabs down to `1e-8` of input size are preserved accurately. `SafeRounding` removes a closed component only if it retains degenerate triangles and is thinner than `RoundingMaxDisplacement` (`Rounding.RemovedComponents`); thin components holding real volume for their area are kept (`Rounding.ScaleDisparity`, `RoundingClean = false`). Filter tiny pieces by volume yourself if they are unwanted.
6. **Do not repair an open boolean result and validate volume against the open mesh:** An open mesh has no exact interior volume (its signed volume depends on the coordinate origin). Close inputs before the boolean rather than wrapping an open boolean result.
7. **Unreferenced or unwelded vertices do not affect the geometry:** Closed triangle soups give the same boolean solid as welded meshes.

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

- **Namespace & classes:** `g3.SolidBooleanPipeline`, `g3.SolidBooleanReport`
- **Input / mutability:** Read-only inputs (repaired on copies if requested); writes new `DMesh3` to `Result`.
- **Guarantees & limits:** Combines exact boolean (`AllowOpenMeshes = false`, `StrictRounding = true`) with a guarded `Reducer` pass (`PreventSelfIntersections = true`, `MinTriangleQuality = 0.02`, optional certified `MaxDeviation`).
- **Further reading:** [SolidBooleanBenchmark README](../../benchmarks/SolidBooleanBenchmark/README.md)

```csharp
using g3;

var pipeline = new SolidBooleanPipeline(new[] { soupA, soupB }, ExactMeshBoolean.Operations.Union) {
    TargetFraction = 0.25
};
if (pipeline.Compute()) {
    DMesh3 solid = pipeline.Result;
    SolidBooleanReport report = pipeline.Report;   // stage timings, NotCleanReason, StopReason
} else {
    Console.WriteLine($"{pipeline.Status}: {pipeline.Error}");
}
```

### Key Options

- **`InputRepair`:** Default `Refuse` fails with `OpenInput` if any input fails `ExactMeshBoolean.IsWindingClosed`. Opt-in `AutoRepair` repairs small holes on a copy (use only for solids, not open cutting sheets); `AutoRepairThenRobust` adds `MeshRobustRepair` as fallback (`Report.RepairedInputs`).
- **Reduction targets:** Set one of `TargetTriangleCount`, `TargetFraction`, `MinEdgeLength`, or `MaxQuadricError`. `Report.StopReason` and `StopDetail` explain why reduction stopped if the target was not reached.
- **Contact-line pairing (`PairContacts` / `PairContactsMode`):** Touching solids limit reduction along contact lines. Opt-in `PairContacts = true` (`PairContactsMode.On`) collapses twin edges along contact lines together (`Reducer.PairCoincidentVertices`) while preserving all guards (`Report.PairedCollapses`; cannot combine with `MaxDeviationTwoSided`). `PairContactsMode.Auto` retries with pairing only when the first pass leaves contacts and is not clean (`Report.PairPassRan`, `PairPassKept`).

---

## Exact Surface Imprinting (`ExactMeshImprint`)

- **Namespace & class:** `g3.ExactMeshImprint`
- **Input / mutability:** Read-only target and cutter; writes split target into `Result` while preserving target vertex IDs, orientation, and topology.
- **Guarantees & limits:** Computes exact intersection curves (including contact and coplanar overlaps) without classifying or deleting triangles. Rounds new vertices with `SafeRounding` (target vertices fixed, curve edges locked, verified by `RoundingClean`). Handles open targets and cutters; does not repair pre-existing self-intersections in the target.
- **Further reading:** [EXACT_IMPRINT.md](../design/EXACT_IMPRINT.md)

Key outputs: `Result`, `SourceTriangle` (maps output triangles back to input target triangles), and `CurveEdges` (with optional `LabelCurves` numbering each curve).

---

## Host GC Tuning for Very Large Booleans

Large exact booleans allocate heavily during classification. Workstation GC (the default for console/desktop .NET apps) can spend a substantial share of wall time in GC pauses on very large inputs. Applications running large booleans can enable Server GC in their host configuration (this is a host setting, not a library setting):

- Project file: `<ServerGarbageCollection>true</ServerGarbageCollection>`
- `runtimeconfig.json`: `"System.GC.Server": true`
- Environment variable: `DOTNET_gcServer=1`

Note that Server GC increases peak memory usage and can be slightly slower on small inputs. See [EXTREME_RUNTIME_BOOLEANS.md](../perf/EXTREME_RUNTIME_BOOLEANS.md) for measured trade-offs.
