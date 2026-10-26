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
| `MeshSignedDistanceGrid` with `UseParallel` (default) and no `Spatial` | a grid point at equal distance from several triangles kept the one whose thread came first (`ClosestTriGrid`), and the sweep that follows propagates from that triangle, so values varied between runs; the serial version computed cell indices with `/ dx`, the parallel one with `* (1/dx)` | the lowest triangle id, as in the serial computation, and `* (1/dx)` in both: the parallel grid equals the serial one. With `Spatial` set (another algorithm) nothing changes |
| `MarchingCubesPro` with `ParallelCompute` (default) | a grid corner's field value was a double on its first evaluation and a float afterwards, and an edge's vertex depended on which of its two cells computed it (the snap near the iso value takes the first corner); with parallel slabs both depended on the thread schedule | float values throughout and a fixed corner order per edge: the parallel output equals the serial one. Edge vertices that used the double value move by about ulp(f) / \|f2 - f1\| of a cell; near-iso snaps can move to the edge's other end |
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
