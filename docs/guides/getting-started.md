# Getting Started with geometry3Sharp

This guide covers project setup, target frameworks, namespaces, core `DMesh3` conventions, and the common operation-object pattern used across the library.

---

## Project File & Target Frameworks

Always reference **`geometry3Sharp_netstandard.csproj`**:

- **Targets:** `net10.0` and `netstandard2.0` (compatible with .NET Framework 4.6.1+ / 4.8 and Unity).
- **Zero external dependencies:** the core package has no third-party runtime dependencies.
- **API compatibility:** stays public-API-compatible with upstream `geometry3Sharp 1.0.324` (validated during `dotnet pack`).
- **XML docs & symbols:** `dotnet pack` ships XML API documentation (`.xml`) and symbols (`.snupkg`).

> **Note on `geometry3Sharp.csproj`:** The legacy `.NET 4.6` project file in the repository root is outdated and omits the `gs` mesh-processing classes (such as `MeshAutoRepair` and newer operations). Do not reference it for new work.

### Optional Add-on & CLI

| Component | Path | Purpose |
|---|---|---|
| Core library | `geometry3Sharp_netstandard.csproj` | 2D/3D geometry, exact booleans, repair, meshing, geodesics, UV, spatial queries, terrain |
| Clipper2 add-on | [addons/geometry3Sharp.Clipper2](../../addons/geometry3Sharp.Clipper2/README.md) | Robust 2D polygon booleans, polyline clipping, offsets, and Minkowski sums |
| Headless CLI & MCP | [tools/g3cli](../../tools/g3cli/README.md) | Run operations on mesh/GeoJSON/DGM files with JSON output or as an MCP server (`g3cli mcp`) |

---

## Namespaces (`g3` vs. `gs`)

Core types live in `g3`, while many higher-level mesh-processing operations live in `gs` (repair, alpha wrap, solidify, hole filling, wall thickness, deviation, Hausdorff and geodesic distances, intrinsic triangulations, offset, terrain). Include both in files that process meshes:

```csharp
using g3;
using gs;
```

---

## Core Conventions & Patterns

### 1. `DMesh3` Basics

`DMesh3` is the primary dynamic indexed triangle mesh:

- **Manifold edges:** every edge has at most two adjacent triangles. Calling `AppendTriangle` where an edge would become non-manifold returns a negative ID (`DMesh3.NonManifoldID`) instead of corrupting topology.
- **Sparse reference-counted IDs:** removing vertices or triangles leaves gaps in index space. Iterate with `mesh.VertexIndices()` and `mesh.TriangleIndices()` rather than `0..VertexCount`.
- **Copying and compacting:** `new DMesh3(other)` makes a full copy preserving IDs; `new DMesh3(other, bCompact: true)` copies and removes ID gaps.
- **Double positions, float attributes:** vertex positions are `Vector3d` (double); optional normals, colors, and UVs are `float`.

### 2. Operation-Object Lifecycle

Most non-trivial operations follow the same pattern:

1. Construct the operation with its input mesh(es) or geometry.
2. Set public option fields or properties.
3. Call `Compute()`, `Apply()`, or `Cut()`.
4. Check the `bool` return value and inspect `Status`, `Error`, or diagnostic fields before using the result.

Most operations report failures via status enums and error strings rather than throwing exceptions on geometric failure cases.

### 3. In-Place vs. New Output Mesh

| Mutability | Typical classes | How to preserve the input |
|---|---|---|
| **In-place** (mutates input) | `MeshAutoRepair` (`repair.Mesh` is compacted), `Remesher`, `Reducer`, smoothers, hole fillers, `MeshPlaneCut` | Pass `new DMesh3(mesh)` |
| **New mesh in `Result`** | `ExactMeshBoolean`, `ExactMesh`, `SolidBooleanPipeline`, `ExactMeshImprint`, `MeshRobustRepair` (`robust.Mesh`), `MeshWindingNumberSolidify`, `MeshAlphaWrap`, `MeshOffset`, `QuadRemesher`, `UVAtlasGenerator` | Input is left unchanged (read-only) |

Always read `repair.Mesh` / `robust.Mesh` after repair operations rather than assuming the original variable was compacted in place.

---

## Building Meshes Programmatically

You can construct a `DMesh3` directly with `AppendVertex` / `AppendTriangle`, or use procedural mesh generators (`TrivialBox3Generator`, `GridBox3Generator`, `Sphere3Generator_NormalizedCube`, `CappedCylinderGenerator`, `ConeGenerator`, `TubeGenerator`):

```csharp
using g3;

// Direct construction
var tet = new DMesh3();
int v0 = tet.AppendVertex(new Vector3d(0, 0, 0));
int v1 = tet.AppendVertex(new Vector3d(1, 0, 0));
int v2 = tet.AppendVertex(new Vector3d(0, 1, 0));
int v3 = tet.AppendVertex(new Vector3d(0, 0, 1));
tet.AppendTriangle(v0, v2, v1);   // outward counterclockwise winding
tet.AppendTriangle(v0, v1, v3);
tet.AppendTriangle(v1, v2, v3);
tet.AppendTriangle(v2, v0, v3);

// Procedural generator
DMesh3 box = new GridBox3Generator { EdgeVertices = 5 }.Generate().MakeDMesh();
```

Compiled example: [MeshConstructionExample.cs](../examples/MeshConstructionExample.cs).

---

## Unity Interop (`G3_USING_UNITY`)

Because `geometry3Sharp_netstandard.csproj` targets `netstandard2.0`, it works in Unity projects. Defining `G3_USING_UNITY` in Unity's scripting defines enables implicit `float` conversions (and explicit `double` → `float` casts) between `g3` types (`Vector2f/d`, `Vector3f/d`, `Quaternionf/d`, `Ray3f`, `Colorf`, `AxisAlignedBox3f`) and their Unity equivalents.

---

## Next Steps

- [File Formats & Precision](file-formats.md) — reading/writing meshes, point clouds, height grids, quad meshes, and tet meshes.
- [Operations Index](../OPERATIONS.md) — full task-to-class matrix with compiled examples.
- [Changes vs. Upstream 1.0.324](../UPSTREAM_CHANGES.md) — behavioral improvements and bug fixes if migrating from upstream.
