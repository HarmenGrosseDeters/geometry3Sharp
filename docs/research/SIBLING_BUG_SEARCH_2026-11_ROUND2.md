# Sibling bug search S1, round 2 (2026-11-25)

Back to [Research notes](README.md) · round 1: [SIBLING_BUG_SEARCH_2026-11.md](SIBLING_BUG_SEARCH_2026-11.md)

Second pass of the search for further places in the library with the same fault as known bugs (coordinator request after round 1). Library code, not tests; nothing was changed
in the library. Base: master 36d19843 (net10.0, linux x64, glibc). Every candidate was tried with a procedural repro on that master; a pattern match without a failing repro is
listed as unconfirmed with the reason.

## 1. Result

| Class | Confirmed | Issue |
|---|---|---|
| 7 Stale state and reuse | `MeshHausdorffDistance` keeps the trees of its first `Compute()`; after `MeshA` or `MeshB` is replaced the old tree answers (0.10 instead of 13.86, converged) | #1614 |
| 8 Untrusted input | `PolygonFont2d.ReadFont` allocates from sizes in the file before validating (12 bytes cost 954 MB, 16 bytes 1.5 GB, `int.MaxValue` an `OutOfMemoryException`); the reader is not in `ReaderFuzzTests` | #1615 |
| #1612 (round 1), site `TpmsLattice.cs:635` | reproduced: with `Enclosure` the same problem scaled by 2^k is refused at largest coordinates 2^29 and 2^31, accepted at 2^28, 2^30, 2^32 | comment on #1612 |
| Leads of the silent-success audit (`MeshDeviation`, `TerrainSafetyLines` Guaranteed, `MeshOffset` band, `IntrinsicCommonSubdivision`) | none | section 2 |
| 3 Degenerate input, 4 Open / non-manifold / bowtie, 10 Complexity, 11, 12 | none | sections 3 to 6 |

## 2. The unprobed leads of `docs/qa/silent-success-audit.md` section 3.8

| Lead | What was read or run | Verdict |
|---|---|---|
| `MeshDeviation` certified interval, `Converged` dropped | `MeshDeviation.Compute` copies `LowerBound` / `UpperBound` of `MeshHausdorffDistance` and does not expose `Converged`; `MaxSubdivisions` of the Hausdorff run is the default 20 000 000 and cannot be set from `MeshDeviation` | not reproducible: the interval stays valid when wide, and a run that stops at 20 M subdivisions is out of reach for a procedural input in the time box. The per-triangle count `TriangleBoundsUnconverged` is reported. Unconfirmed |
| `TerrainSafetyLines` Guaranteed: Success with `Certified` false | `TerrainSafety.cs:765-771`: `return Certified \|\| RequireCertified == false`; `RequireCertified` (default false) makes `Compute` fail with `NotCertified` | documented option, the flag `Certified` is the result. Not a defect |
| `MeshOffset`: `BandCertified` and `SelfIntersectionFree` do not gate Success, the band is one-sided | class comment | documented in the class comment (`SelfIntersectionFree` and `BandCertified` are reported, "the other side ... is measured, not proven") |
| `IntrinsicCommonSubdivision`: area sums that can cancel | `IntrinsicCommonSubdivision.cs:108-118, 377-389` | the extraction has further checks (a face without area, false crossings, every input edge matched on both sides, #1326); the area check is not the only one. Unconfirmed |

## 3. Classes 3 and 4: degenerate, open, non-manifold and bowtie input

**Run.** Parameterization (`LSCM`, `ARAP`, `SLIM`, spectral conformal, `BFF`, `TutteEmbedding`) on a plain grid, a grid with a vertex moved onto a neighbour, a grid with a collinear row, a
grid with a 1e-300 sliver, one triangle, an empty mesh and a bowtie: no exception, no non-finite UV, an independent check of the UV orientation (no negative or zero-area UV triangle) passes where
`Success` is returned; empty and bowtie meshes return `InvalidMesh`. Then 11 operations (`MeshOffset`, `MeshWindingNumberSolidify`, `MeshAlphaWrap`, `CrossField`, `QuadRemesher`,
`MeshTetrahedralization`, `MeshGeodesicDistance`, `MeshRobustRepair`, `ExactMeshBoolean`, `SolidBooleanPipeline`, `MeshWallThickness`) on: empty, one triangle, a box with a face removed, a box with a
duplicate triangle (non-manifold edge), a box with a vertex moved onto a neighbour, two boxes touching in a corner, a box at scale 1e-300. No exception and no non-finite coordinate.

**Read as defined behaviour, not defects.**
- `ExactMeshBoolean` and `SolidBooleanPipeline` on two boxes touching in one corner: `Success`, and `NotCleanReason` "self-intersections": documented (a result with contacts is valid, `Clean` false, `OnlyContacts`).
- `MeshWindingNumberSolidify` of one open triangle: `Compute()` true with an empty `Result`; the documented false cases (empty input, cell size, grid too large, cancelled) do not include it. Unconfirmed: the winding number of one triangle is below the iso value everywhere, so "no solid" is the result of the definition.
- `MeshWallThickness` of one open triangle: NaN thickness, `Compute()` true. Unconfirmed (no doc statement either way).
- `MeshTetrahedralization` of one triangle or an open box: `Success`: it wraps the surface first (documented alpha-wrap based); not checked further.

## 4. Class 7: stale state and reuse

**Run.** For `MeshOffset`, `MeshWindingNumberSolidify`, `MeshAlphaWrap`, `MeshWallThickness`, `MeshDeviation`, `MeshHausdorffDistance`, `MeshGeodesicDistance`, `MeshRobustRepair`, `CrossField`: compute on mesh A (a unit
box or a sphere), replace the mesh by B (an open box, an empty mesh, a sphere, a distant box), compute, compare the summary (status, counts, bounds, sizes) with a fresh instance on B.
Differences: `MeshHausdorffDistance` (**#1614**), `MeshWindingNumberSolidify.WindingEvaluations` (7984 against 8118: a counter of a parallel run, not checked for run-to-run noise on a fresh instance), `MeshGeodesicDistance`
(`Compute` is false on an empty B in both, `Distance` keeps the array of the previous run: stale output).
**Grep.** Assignments of built trees to public fields inside `Compute()`: `MeshTrimLoop.cs:83` (`Spatial` when null, upstream; `Mesh` is not replaceable there), `MeshSpatialSort.cs`. `MeshWallThickness.cs:249`
keys its own tree on `(Mesh, ShapeTimestamp)`: the pattern for the fix of #1614.

## 5. Class 8: untrusted input

**Searched.** The readers of `io/` against the targets of `ReaderFuzzTests` (all covered: OBJ, OFF, PLY, STL, XYZ, GLTF, 3MF, BinaryG3, HeightGrid, PolyMeshIO); `MiniJson` (depth limit 256); the `ReadInt32` sizes of
`gSerialization`, `TransformSequence`, `PolygonFont2d`, `BinaryG3ReaderWriter`, `STLReader`. The list and polygon loops of `gSerialization.Restore` grow with the data; its byte reader chunks (`gSerialization.cs:676-682`).
**Confirmed.** `PolygonFont2d.Restore` (`ReadBytes(buflen)`, `new GeneralPolygon2d[numpolys]`): #1615.
**Not searched.** The CLI and MCP server of `tools/g3cli` (path and URI handling).

## 6. Classes 10, 11, 12

- **10 Complexity.** Time at 20k and 80k triangles, with and without a far outlier triangle (1e7 away): `MergeCoincidentEdges`, `RemoveDuplicateTriangles`, `MeshRepairOrientation`, `MeshAutoRepair`,
  `MeshBoundaryLoops`, `DMeshAABBTree3` build, `MeshConnectedComponents`: ratios 0.9 to 5.0 for a 4x input (quadratic would be 16): linear on that input class. The heavy operations (`MeshRobustRepair`,
  `ExactMeshBoolean`, the wraps) were not timed here; their complexity is covered by `docs/perf/` and the existing benchmark gates.
- **11 Compatibility and documentation.** API compatibility is the `dotnet pack` validation and was not repeated. A different check of the class (a documented command that selects no test, class 12 item): every
  `FullyQualifiedName~X` of the `.md` files (14 tokens that a word search did not find) against `dotnet test --list-tests` (22 968 tests): all select tests except
  `HeatGeodesicOracleTests` in `docs/plans/geodesics-oracle-follow-ups.md`, a draft plan for a test class that does not exist yet (plan status "draft"): not a defect.
- **12 Test oracles and generators.** Only the documented filter commands above; no harness, generator or fuzzer was reviewed.

## 7. Where a tool or a test would catch each class

- Class 7: a generic reuse test for every public class with a `Compute()` or `Apply()` and a replaceable `Mesh`-like field: compute A, replace, compute B, compare with a fresh instance (the sweep of section 4 is
  60 lines). Could live in `tests/.../Harness`.
- Class 8: a rule for `ReaderFuzzTests`: every public method that takes a `Stream` or `BinaryReader` and is not in the fuzz list fails a test (reflection over the library); today `PolygonFont2d.ReadFont` is the one that
  `grep "ReadInt32"` finds outside the fuzzed readers. A second rule: a `ReadBytes(x)` or `new T[x]` whose `x` comes from `ReadInt32()` in the same method is flagged (an analyzer).
- `#1612` sites: the analyzer rule of round 1 (a libm value converted to an integer or a power of two).
