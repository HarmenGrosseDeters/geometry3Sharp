# Changes for users of geometry3Sharp 1.0.324

This fork keeps the public API of upstream geometry3Sharp 1.0.324 (commit `ece3364`), but a number of existing
methods give different results: almost all of them are bug fixes. This page lists the changes that existing
code can notice. New classes are not listed (see the README and [OPERATIONS.md](OPERATIONS.md)).

## Results that change

| Class / member | 1.0.324 | Now |
|---|---|---|
| `OpenCylinderGenerator`, `CappedCylinderGenerator`, `ConeGenerator`, `VerticalGeneralizedCylinderGenerator` (and `Radial3DArrowGenerator`) | triangles faced inward with the default `Clockwise = false` (negative volume) | they face outward like the other generators. Drop any `ReverseOrientation()` or `Clockwise = true` that compensated for it |
| `Reducer` | boundary edges collapsed to their midpoint; plain `OptimalPoint` solve | with `PreserveBoundaryShape` (default) boundary planes are added to the quadrics (`BoundaryQuadricWeight = 100`) and boundary edges keep an endpoint; `QuadricRegularization = 1e-6`. Results differ slightly on closed meshes and noticeably along boundaries (kept exactly, coarser) |
| `Reducer` subclasses | quadrics in absolute coordinates | `vertQuadrics` and the quadrics passed to `OnEdgeCollapse` are relative to `quadricOrigin` (the mesh bounds center); use `plane_quadric` / `evaluate` |
| `DMesh3.GetBounds()`, `CachedBounds`, `DGraph2/3.GetBounds()` of an empty mesh / graph | a zero-size box at the origin | `AxisAlignedBox3d.Empty` (negative extents). `MeshSignedDistanceGrid`, `MeshWindingNumberGrid`, `MeshScalarSamplingGrid` and `CachingMeshSDF` throw an `ArgumentException` on an empty mesh |
| `DMesh3.AllocateTriangleGroup()` | skipped an id (6 on a mesh whose groups are 0..4) | returns the next unused id (5) |
| `MeshTransforms.Rotate(mesh, origin, Quaternionf)` | positions only | also rotates vertex normals, in double precision |
| `MeshMeasurements.MassProperties(..., bodyCoords)` | `bodyCoords` was ignored | honored |
| `Remesher` flips | only to improve the valence | also an edge of a degenerate (zero-area) triangle, when both new triangles are proper and face the same way (removes slivers that no collapse can remove, eg between pinned corners or along feature curves) |
| `RemesherPro` queued passes (`FastestRemesh`, `SharpEdgeReprojectionRemesh`; also `MinimalHoleFill`, `AutoHoleFill`, `MeshAutoRepair` hole filling) | the edges queued by the parallel smooth / projection passes were processed in thread order, so results differed from run to run | processed in a fixed order (sorted, walked with a prime step): the same result every run, also with parallel passes; results differ from 1.0.324 (also serial ones) |
| `RemesherPro` projection passes (`TrackedProjectionPass`, face-aligned `TrackedFaceProjectionPass`) | a vertex counted as moved above an absolute 1e-6, vertices with summed face weights below 1e-8 were not projected, and the face-aligned pass rotated the triangles in a float frame (meshes far from the origin, eg UTM coordinates, were remeshed into about 4 times the triangles) | the tolerance is relative to `MinEdgeLength` (and a few ulps of the coordinates), any positive weight counts, the rotation is in double: the same result for a mesh scaled by 2^k, and far from the origin as at the origin |
| `MeshBoundaryLoops` | bowtie or inconsistently oriented boundaries often became spans | decomposed into loops (rewritten walk; somewhat slower on meshes with very many holes) |
| `CurveSampler2` on closed curves | the first point was repeated as the last sample | not repeated |
| `PolyLine2DCurve.ParamLength` | `VertexCount` | `VertexCount - 1` (segment i is `[i, i+1]`); `Polygon2DCurve.SampleT` wraps to vertex 0 |
| `Arc2d.Transform`, `Circle2d.Transform` with a mirroring transform | orientation kept | `IsReversed` flips |
| `GeneralPolygon2d.Contains(Polygon2d)` | accepted polygons overlapping a hole | rejects them |
| `PolySimplification2` | `PreserveStraightSegLen` (a length) was compared with squared lengths | squared before the comparison; duplicate vertices removed |
| `GridImplicits3d.Bounds()` | one cell too large | the grid's extent |
| `Gradient` of `DenseGridTrilinearImplicit`, `CachingGridImplicit3d`, `CachingMeshSDF` | per grid cell | per unit length (divided by the cell size) |
| `ImplicitIntersection3d.Bounds()` | union of the boxes | intersection |
| `MeshScalarSamplingGrid` outside the narrow band | zeros | the last known value |
| `GridIndexing` `ToGrid` / `ToBlock` | truncation (wrong cell for negative coordinates) | floor |
| `Snapping.SnapToIncrementLow/High` on exact multiples | moved to the next increment | value kept |
| `Units` miles | 1609.34 m | 1609.344 m |
| `Util.IsBinaryStream` | a stream shorter than the search length counted as binary | not binary (affects STL ascii / binary detection of files under 500 bytes) |
| `PlanarComplex.Add(polygon)` | kept a reference to the caller's polygon | stores a copy |
| `OBJReader`, `OFFReader` with face indices out of range | OBJ: a normal or UV index outside the file's normals / UVs threw (`StandardMeshReader`: `GenericReaderError`). OFF: only triangles were read (the first three indices of a polygon); a negative vertex index threw, an index past the vertices dropped the triangle silently | OBJ: the vertex gets the default normal / UV. OFF: polygons are fan-triangulated, and a polygon with any index outside the file's vertices is skipped as a whole, with a warning |
| `gSerialization.Restore(DMesh3, ...)` (`.g3mesh` reader) and `Restore(DVector<...>, ...)` | stored buffers and counts taken over as they are; a truncated array was filled with zeros | inconsistent mesh data (buffer sizes, edge / triangle references, duplicate edges) and counts beyond the stream throw (the reader reports `GenericReaderError`); a truncated array throws `EndOfStreamException`. Files written by `gSerialization.Store` read as before |
| `MeshSignedDistanceGrid` with `UseParallel` (default) and no `Spatial` | a grid point at equal distance from several triangles kept the one whose thread came first (`ClosestTriGrid`), and the sweep that follows propagates from that triangle, so values varied between runs; the serial version computed cell indices with `/ dx`, the parallel one with `* (1/dx)` | the lowest triangle id, as in the serial computation, and `* (1/dx)` in both: the parallel grid equals the serial one. With `Spatial` set (another algorithm) nothing changes |
| `MarchingCubesPro` with `ParallelCompute` (default) | a grid corner's field value was a double on its first evaluation and a float afterwards, and an edge's vertex depended on which of its two cells computed it (the snap near the iso value takes the first corner); with parallel slabs both depended on the thread schedule | float values throughout and a fixed corner order per edge: the parallel output equals the serial one. Edge vertices that used the double value move by about ulp(f) / \|f2 - f1\| of a cell; near-iso snaps can move to the edge's other end |
| `MeshPlaneCut.Cut` | cut edges shorter than an absolute `DegenerateEdgeTol` = 1e-6 were collapsed (a mesh of about 1e-6 lost its cut loop; fine meshes at millimetre scale lost cut edges); vertices within `MathUtil.Epsilon` of the plane counted as on it (below the rounding of the signed distance at UTM coordinates: slivers), and an on-plane vertex with a positive sign was removed with its whole one-ring | while `DegenerateEdgeTol` has its default `MathUtil.ZeroTolerancef`, the collapse length is `RelativeDegenerateEdgeTol` (1e-6) times the diagonal of the bounds of the vertices with edges (`EffectiveDegenerateEdgeTol`): smaller meshes collapse less, larger ones more (a 1000 m terrain: edges up to 1 mm, was 1 µm); another `DegenerateEdgeTol` is used as given. On the plane: within 16 ulps of the largest coordinate, with sign 0, so the vertex is kept |
| Mesh readers (OBJ, OFF, STL, PLY, 3MF, glTF / glB) with a NaN or infinite vertex coordinate (eg `1e400`, `nan`) | read as is on .NET Core in every format; on .NET Framework, text values beyond the double range (`1e400`) threw or skipped the line, depending on the reader | `FileParsingError` with the defaults, on every framework (text values beyond the double range are read as infinity first); `ReadOptions.AllowNonFiniteCoordinates = true` keeps such vertices. The check is on the final position (after 3MF / glTF transforms); normals, colors, UVs and the internal `.g3mesh` format are not checked. The glTF reader now also passes the caller's `ReadOptions` on |
| `IntrinsicTriangulation.DelaunayRefine` on open meshes with small boundary angles (also `MeshGeodesicDistance` with its default `SplitObtuseBoundaryEdges`, and `MeshGeodesicDistance` / `VectorHeatMethod` with `IntrinsicRefine`) | concentric shells at absolute powers of two | at powers of two times the mean input edge length: the result no longer depends on the unit (4 % difference in geodesic distances between a mesh in metres and in millimetres). Split positions near such corners change |
| `MergeCoincidentEdges`, `RemoveDuplicateTriangles` (and the crack and duplicate steps of `MeshAutoRepair`) | threw an `ArgumentException` when the boundary edge midpoints (triangle centroids) spanned less than 64 times `MergeDistance` (`VertexTolerance`), eg when the only open boundary is one short crack; `MeshAutoRepair` swallowed it, and the hole filler closed such cracks with extra triangles. `RemoveDuplicateTriangles` looped forever when a triangle had more than 1024 duplicate candidates | such cracks are merged and such duplicates removed; results elsewhere are unchanged |
| `MeshAutoRepair` tolerances | absolute: `RepairTolerance = 1e-6` (crack merging), `MinEdgeLengthTol = 1e-4` (final short edge collapse), so a model in µm, m or km, or far from the origin, was repaired differently than in mm | relative to the diagonal of the bounds of the triangles (`ToleranceMode = Relative`, `RelativeRepairTolerance = 1e-8`, `RelativeMinEdgeLength = 1e-6`, at most 1/100 of the median edge length, at least 16 rounding units of the largest coordinate): the same as before for a model of diagonal 100. A tolerance set to a value other than its 1.0.324 default (`RepairTolerance = x`, `MinEdgeLengthTol = x`) is absolute and used as given (set to exactly the old default it counts as not set: use `ToleranceMode = Absolute` for that) (`RepairTolerance` is the merge distance only; the degenerate hole loop check of the smoothed fill and the occlusion test offset of `RemoveMode` stay relative); `ToleranceMode = Absolute` gives the 1.0.324 values. `EffectiveRepairTolerance` / `EffectiveMinEdgeLength` report what the last `Apply()` used. Also `MeshRobustRepair`, which runs it |
| `MeshRepairOrientation.SolveGlobalOrientation` | skipped triangles with area below the absolute 1e-6 (in µm units all of them: the orientation was not solved) | below `DegenerateAreaFactor = 1e-10` times the squared diagonal of the triangles (the same for a model of diagonal 100). The evidence of the parallel ray casts is summed in a fixed order (it depended on the thread schedule) |
| `RoundRectGenerator.GetBorderLoop`, `RevolveGenerator` cap normals, `TrivialBox3Generator` normals | see the commit history | corrected |

## Signatures that change (recompile needed)

| Member | 1.0.324 | Now |
|---|---|---|
| `Util.ParseInt` | returned `float` | returns `int` |
| `DSubmesh3.WantComponents`, `ComputeTriMaps`, `OverrideGroupID` | fields | properties; setting one after construction recomputes the submesh (and discards edits to `SubMesh`) |
| `Remesher.vModifiedV` (protected) | `BitArray` | `bool[]` |
| `DMeshAABBTree3.box_centers`, `box_extents` (protected) | `DVector<Vector3f>` | `DVector<Vector3d>` (no misses far from the origin, eg UTM) |
| `DVector.Clear()` | | `[Obsolete]` (warning) |

## Performance

`DMeshAABBTree3` stores its boxes in double precision (correct far from the origin, about 5-15% slower nearest
triangle queries, twice the box memory). `Reducer` is about 5-10% slower (boundary quadrics, regularized
solve). `FastWindingNumber` is a bit faster.
