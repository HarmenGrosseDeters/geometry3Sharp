# Silent-success audit (2026-11-25)

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

Consolidation phase, step 2 (owner 2026-11-25). Read and probe only: no library change. Fault class 1 of
[REVIEW_CHECKLIST.md](../REVIEW_CHECKLIST.md): an operation reports success or a clean status while its result is wrong, because its own check
compares against a reference that is wrong in the same way, or a tolerance hides a real error. Earlier cases: #1548, #1479, #1465, #1376,
PR #1488, PR #1546.

## 1. Method

1. Inventory of the public operations that report success or a clean status (result enums, `bool Apply()` / `Compute()`, `Status`,
   `*Reason` strings, `NotCleanReason`, `Certified*` / `Converged` flags), found by grep over the library and `tools/g3cli`.
2. For each: what the success claim checks in code, against which reference, with which tolerance, and a risk class:
   - **(a)** the reference is derived from the same possibly wrong data as the result;
   - **(b)** a tolerance or allowance pooled over the result, or relative to something that can be large;
   - **(c)** a check of topology only, while the doc or name suggests geometric correctness;
   - **(d)** a doc promise not kept in some mode;
   - **(e)** a fallback that discards a better answer;
   - **(f)** a status set before a later stage that can still fail or change the result.
3. A small procedural probe per risk, in a scratch console project against the library (not committed). Only reproduced results are
   reported as defects; the rest is marked "not reproduced" (probed, held) or "unprobed" (a suspicion from reading).

"Documented" means the class comment states the limitation; it is listed because a caller who checks only the status misses it.

## 2. Results, ranked

| # | Operation | Wrong result | Status | Class | Issue |
|---|---|---|---|---|---|
| 1 | MeshRobustRepair | a closed outward shell nested in another becomes a cavity: volume 3.904 instead of 8 (-51 %); `ExactMeshBoolean.Resolve` and `SolidBooleanPipeline` give 8 | `Apply()` true, `ResultReason` "", RepairVolumeIoU = ResultVolumeIoU = 1 | (a) | #1566 |
| 2 | SolidBooleanPipeline | a 10 x 10 x 0.01 inside-out part of input B is dropped when input A is of size 1000; with A of size 10 the same B is refused | Success, `Report.Clean` true (only `DroppedInsideOutComponents` shows it; documented, #1555) | (b) | #1567 |
| 3 | TpmsLattice | at Origin 1e13 (extent 1, 32 divisions) the returned world mesh has degenerate triangles and self-intersections | `Status` Ok: the clean check runs on the lattice frame, before `Origin + Rotation * v` | (f) | #1569 |

Already tracked, not re-filed: #1548 (volume reference of an unoriented soup, class (a)), #1479 (lost area of open meshes), #1465
(RoundingNotClean residue), #1376 (DualContouring scaled field), #1491 (Remesher.MinTriangleQuality leaves self-intersections).

## 3. Per operation

### 3.1 MeshRobustRepair

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `Apply()` true, result clean | `NotCleanReason`: closed, no bowties, no degenerate triangles (area 1e-12 of the squared diagonal), consistent orientation, no inside-out components (nesting parity), exact self-intersections | the result itself | exact, except the degenerate area | (c) | held for touching and crossing inner shells: they are self-intersections and go to the exact resolve |
| volume kept | volume IoU >= `MinVolumeIoU` (0.99) | input's winding number solid, inside-out parts oriented by the same nesting rule | 1 % pooled over the whole solid | (a), (b) | **reproduced** (#1566): a nested outward shell of 51 % of the volume is flipped into a cavity, IoU 1 |
| no lost part | lost-part checks (separable parts, far parts) | input components | part of >= 4 triangles, judged by its own diagonal | (b) | a part whose box meets another one is not separable; only the pooled IoU sees it (unprobed for a loss: no default stage lost a connected 0.01 plate, below) |
| solidify / wrap fallback | not checked for volume (documented) | none | a cell / alpha | (b), documented | not reproduced: a 0.01 plate through a cube face (cell 0.027) was kept by the exact resolve and, with `ExactResolve = false`, by the solidify (volume 8.015 vs 8.01) |
| pass-through of a clean input | `NotCleanReason` of the whole | itself | as above | (c) | held: an outward nested shell is reported "inside-out components", so not passed through |
| unoriented soup | see #1548 | winding volume of the soup | | (a) | tracked in #1548 |

### 3.2 MeshAutoRepair

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `Apply()` true | false only if cancelled; also true when the mesh is still open or erosion removed boundary triangles | none | none | documented (`<returns>`) | not probed: the doc says to check `Mesh.IsClosed()`; the CLI `repair --method auto` reports the output metrics |

### 3.3 ExactMeshBoolean

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `Status` Success | Result set; closed and manifold for closed inputs | exact arrangement | exact before rounding | (f), documented | not probed: without `StrictRounding` (default off) a Success can carry rounding defects; `RoundingClean` false says so, and `NotCleanReason` is needed for inside-out slivers (documented at `RoundingClean`) |
| SafeRounding | `MeshRoundingRepair` | exact result | `RoundingMaxDisplacement` 1e-5 of the largest component's diagonal | (b) | owner-local; #1216 (closed), #1465 open |

### 3.4 SolidBooleanPipeline

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `Status` Success, `Report.Clean` | `NotCleanReason` of the returned mesh | the result itself | exact | (c) | as 3.1 |
| inside-out input refused | `InsideOutInput`, except thin inside-out components dropped (#1555) | thickness 2V/A | 1e-5 of the largest component of ALL closed inputs | (b), documented | **reproduced** (#1567) |
| grid retry after `RoundingNotClean` (default on) | result checked like any other; guards on cell size vs thickness (1/64) | snapped inputs, not the given ones | one cell (2^12 ulp of the largest coordinate) | (e)/(b), documented | unprobed: the result is the boolean of the snapped inputs; Route reports the size change |
| verification of the reduction | see PR #1488 (pooled rounding allowance) | | | (b) | tracked in PR #1488 |

### 3.5 MeshAlphaWrap

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `WrapStatus.Success` | closed, no bowties, no box corners on the result, no rejected rule-1 Steiner points (else `Failed`) | the result itself | exact | (c) | not probed: "encloses the input" holds by construction (the inside cells are carved from outside); no check of distance to the input in the status |
| `PointLimitReached` | refinement stopped; result valid, less detail | | | documented | not probed |
| budget coarsening (`MaxEstimatedTriangles`) | alpha grows by `AlphaCoarsening`; cavities behind a narrow neck fill (documented, cap `MaxAlphaCoarsening`) | | unbounded without the cap | (b), documented | not probed |

### 3.6 MeshTetrahedralization / TetMeshImprover

| Claim | Check | Reference | Tolerance | Class | Probe |
|---|---|---|---|---|---|
| `TetStatus.Success` | the refiner's status; the improver runs after the status is set; `Targets` measured on the output | wrap | | (f) | not reproduced: sphere, thin box and cone at sliver bounds 10 / 20 / 30 degrees gave no inverted or zero-volume tet (exact `Orient3d`) and a clean boundary equal in volume to the tets |

### 3.7 Other mesh operations (from reading)

| Operation | Claim | Check | Class | Probe |
|---|---|---|---|---|
| MeshBoolean (legacy) | `Compute()` true | always true (documented; upstream code) | documented | not probed |
| QuadRemesher | `Status` Success | at least one face; `PureQuadStatus` says which check failed (never silent) | (c): no claim of closedness or deviation in the status | not probed |
| QualityMesh2 / ConstrainedDelaunayMesh2 | `Triangulation2Status.Success` | "a verified triangulation" (topology); `BadTriangles` lists only angle violations, not area or size violations next to locked segments (documented open item m3.locks); `SteinerLimitReached` is a flag, not a status | (c), documented | not probed |
| MeshHausdorffDistance | `Converged`, `UpperBound` | upper bound certified only with `CertifiedUpper` (off by default); `g3cli` reports `distance` = UpperBound without it | documented | not probed |
| MeshWindingNumberSolidify | `TopologyCertified` | certificate against the input's winding level set; fixed for the oriented copy in #935 | (a), fixed | not probed |

### 3.8 Long tail (from reading; a search agent's inventory, spot-checked)

"Probe" says what was run; the rest is a suspicion from the code, not a reproduced defect.

| Operation | Claim | What it checks (file) | Class | Probe |
|---|---|---|---|---|
| TpmsLattice | `Status` Ok | `NotCleanReason` on the lattice-frame mesh (TpmsLattice.cs:721), transform after it (749) | (f) | **reproduced** (#1569) at Origin 1e13; clean up to 1e12, rotated or not |
| PolyhedralGeodesicPath | Ok: "Points is the shortest path" | the walk reached a source; Length not compared with Distance | (a) | not reproduced: on a jittered sphere, cone and remeshed box (1067 targets) the polyline length equals Distance within 1.2e-15 |
| MeshDeviation | "certified interval" of the one-sided Hausdorff distance | drops `Converged`, `CertifiedUpper` not set (MeshDeviation.cs:279-289); triangle bounds without the certified mode | (d) | unprobed: the interval is still valid when wide; the upper end is good up to the floating-point distance queries (as MeshHausdorffDistance documents) |
| TerrainSafetyLines (Guaranteed) | Success | `Certified \|\| RequireCertified == false` (TerrainSafety.cs:771): Success with `Certified` false by default | (d) | unprobed |
| MeshOffset | Success | closed, manifold, finite, no bowtie; `BandCertified` and `SelfIntersectionFree` do not gate it; the band is one-sided | (c) | unprobed |
| UVAtlasGenerator | Success | exact overlap test on the double UVs; float UVs written can overlap (`FloatRoundingDefects`, reported) | (d), documented | unprobed |
| MeshParameterization (SLIM) | no flips | `RejectFlipped` not called on the SLIM path; flip-freedom checked on the start map only | (d) | unprobed |
| CrossField | Success | independent of `Converged` (documented) | (d), documented | unprobed |
| ArapMeshDeformer | `Converged` | step size below 1e-8, not a residual (documented) | (c), documented | unprobed |
| BiharmonicMeshDeformer | `Converged` | backward error relative to a sum of absolute terms | (b) | unprobed |
| IntrinsicCommonSubdivision | Success | per-triangle area sums within 1e-6; an overlap and a gap can cancel | (b) | unprobed |
| ExactMeshGeodesicDistance (internal) | Ok | propagation done; interval width not bounded | (b) | unprobed |
| PolyhedralGeodesicDistance | Ok | every vertex reached (accuracy measured, not certified, documented) | (c), documented | unprobed |
| PassageWrap | Success | clearance from a double distance with margin 1e-12 of the box diagonal; also after PointLimitReached | (b) | unprobed |
| PointCloudSurface / PointCloudNormals | Success | coverage and confidence measured against spacing and noise estimated from the same cloud; up to 1 % uncovered / 10 % unconfident | (a), (b) | unprobed |
| ContourLoft | Success | exact checks only with `CheckSelfIntersections` | (d), option | unprobed |
| ContouringSmoothCertificate (internal) | Certified | `Field` supplied by the caller, compared with the contour's implicit only at the nodes | (a), mild | unprobed |
| TerrainGridTin | Ok | convergence margin 64 u (1 + zmax) max(nx, ny), a heuristic | (b), mild | unprobed |
| No finding | | TetMeshImprover (exact `Orient3d` per edit), MeshEnvelope, CertifiedPointMeshDistance, ContourLoftStack, AdaptiveDualContouring, MeshSweepCollision | | |

### 3.9 g3cli

Every command that returns a JSON object exits 0 (Cli.cs, CommandSpec.cs); failures are `CommandException`s. No library bool is ignored with its
result then used unchecked. Degraded statuses that exit 0 with a warning only: the Hausdorff helper reports `distance` = the uncertified
UpperBound and `converged` without a warning when false (Commands.cs:95-109), and a failed Hausdorff is written as null in remesh, simplify, tet and
wrap; `geodesic path` TraceFailed (the written path may cut across the surface) is a warning; `offset --method mesh_offset` with
`BandCertified` false is "ok" with a warning; PointLimitReached in `tet` and `wrap` is a warning. `tpms` inherits #1569.

## 4. Follow-ups

- #1566 and #1567 (owner-local areas: no fix in this PR); #1569.
- Candidates for the next probe round: MeshDeviation and the g3cli Hausdorff helper (a non-converged or uncertified bound called certified), TerrainSafetyLines Guaranteed, MeshOffset band, SLIM flips.
- A regression test for each once the owner decides the fix (the probes in the issues are the test inputs, each well below 5 s).
- The pattern of #1566 and #1548: a volume reference that applies the same orientation rule as the repair. A second reference that does not
  (the plain nonzero winding solid of the input) would catch both; see the fix options in #1566.
