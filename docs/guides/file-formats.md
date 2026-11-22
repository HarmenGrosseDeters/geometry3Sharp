# File Formats & I/O

`geometry3Sharp` supports standard triangle mesh formats through `StandardMeshReader` and `StandardMeshWriter`, plus specialized readers and writers for point clouds, quad/polygon meshes, tetrahedral meshes, height grids (DGM/DTM), and 2D vector geometry.

---

## Triangle Mesh & Point Cloud Formats

- **Namespace & classes:** `g3.StandardMeshReader`, `g3.StandardMeshWriter`, `ReadOptions`, `WriteOptions`
- **Error handling:** `WriteMesh` returns `IOWriteResult` (`IOCode` + message) without throwing; `StandardMeshReader.ReadMesh` returns `null` on read failure (and throws if a file reads cleanly but contains no mesh).
- **Non-finite coordinates:** by default (`ReadOptions.AllowNonFiniteCoordinates = false`), NaN or infinite vertex coordinates (including text values overflowing `double`) fail with `IOCode.FileParsingError` on every .NET runtime. Set `AllowNonFiniteCoordinates = true` to accept them.
- **Example:** [MeshIOExample.cs](../examples/MeshIOExample.cs)

```csharp
using g3;

DMesh3 mesh = StandardMeshReader.ReadMesh("scan.glb");
if (mesh != null) {
    IOWriteResult res = StandardMeshWriter.WriteMesh("scan.3mf", mesh, WriteOptions.Defaults);
    if (res.code != IOCode.Ok)
        Console.WriteLine(res.message);
}
```

### Supported Formats

| Format | Read | Write | Key features & defaults |
|---|---|---|---|
| `.obj` | yes | yes | Materials (`.mtl`), face groups, UVs, vertex colors extension |
| `.stl` | yes | yes | ASCII and binary (`WriteOptions.bWriteBinary`) |
| `.off` | yes | yes | Fan-triangulates polygons; skips polygons with out-of-range indices with a warning |
| `.g3mesh` | yes | yes | Packed binary `DMesh3` (`gSerialization`); validates counts and topology on restore, rejects non-finite coordinates (unless `AllowNonFiniteCoordinates`) |
| `.xyz` | yes | yes | ASCII point cloud (`x y z` or `x y z nx ny nz`), vertex-only `DMesh3` |
| `.ply` | yes | yes | ASCII & binary (LE/BE read, LE write); normals, colors, UVs, point sets |
| `.3mf` | yes | yes | Core spec; build items, components, transforms; converts to mm by default |
| `.glb` / `.gltf` | yes | yes | glTF 2.0; node transforms applied, primitives as face groups, normals/colors/UVs |

### Precision, Units & Large Coordinates (UTM)

- **PLY (`PLYReader` / `PLYWriter`):** positions are written as `double` unless `WriteOptions.RealPrecisionDigits <= 7`. Face-less `.ply` files load as vertex-only `DMesh3` point sets, which `MeshAlphaWrap(mesh)` accepts directly.
- **glTF / GLB (`GLTFReader` / `GLTFWriter`):** glTF stores `float32` vertex positions. To preserve precision at large coordinates (such as UTM), `GLTFWriter` writes vertices relative to each mesh's bounding-box center and stores the center in the node translation. `GLTFReader` applies the node transform in `double`. Coordinates are not axis-converted (glTF is Y-up in meters). Materials, Draco, and meshopt are not supported.
- **3MF (`ThreeMFReader` / `ThreeMFWriter`):** `ThreeMFReader.ConvertToMillimeters` is `true` by default. `ThreeMFWriter` writes coordinates unscaled in its configured `Unit` (millimeter by default).
- **XYZ (`XYZReader` / `XYZWriter`):** reads `x y z` or `x y z nx ny nz` per line into a vertex-only `DMesh3`; `XYZWriter` writes round-trip exact decimal values.

---

## Specialized Geometry Formats

| Domain | Classes | Formats | Notes |
|---|---|---|---|
| Quad / polygon meshes | `g3.PolyMeshIO` | `.obj`, `.ply` (ASCII & binary) | Preserves whole quads and n-gons (`DPolyMesh`); used by `QuadRemesher` |
| Tetrahedral meshes | `gs.TetMeshWriter` | `.mesh` (Medit), `.msh` (Gmsh 4.1 / 2.2), `.vtk` (legacy ASCII) | Writers only (no readers); exports boundary patch markers & region refs |
| Height grids (DGM / DTM) | `g3.HeightGridReader`, `g3.HeightGridWriter` | `.xyz` (regular lattice), `.asc` (ESRI ASCII Grid) | Status-based error reporting, no-data gaps; feeds `TerrainGridTin` |
| 2D vector export | `g3.SVGWriter` | `.svg` | Exports 2D curves, polygons, and polylines |
| Triangle 2D meshes (CLI) | `g3cli mesh2d` | `.poly`, `.node`, `.ele` | Exchange format for 2D constrained/quality meshing ([g3cli README](../../tools/g3cli/README.md)) |

### Further Reading

- [Getting Started](getting-started.md) — `DMesh3` basics and project setup.
- [Terrain & Point Clouds](terrain-and-pointclouds.md) — `HeightGrid`, `TerrainGridTin`, and point cloud reconstruction.
- [2D & Tetrahedral Meshing](meshing-2d-and-tet.md) — `TetMeshWriter`, `TetMeshMarkers`, and `QualityMesh2`.
