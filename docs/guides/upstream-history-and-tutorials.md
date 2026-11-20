# Upstream History, Tutorials, and Unity Interop

This repository is an actively maintained fork of [gradientspace/geometry3Sharp](https://github.com/gradientspace/geometry3Sharp) (`master`, NuGet package `1.0.324`, commit `ece3364`), keeping API compatibility with upstream `1.0.324` while adding exact geometry, modern .NET targets, and extensive mesh-processing pipelines.

This page preserves the original upstream tutorial links, historical project showcase, credits, and Unity interop details that used to live in the root `README.md`.

## Upstream Tutorials (Gradientspace Blog)

Several introductory tutorials for the core `DMesh3` API were published by Ryan Schmidt on the Gradientspace blog. Their core concepts still apply to this fork (see [UPSTREAM_CHANGES.md](../UPSTREAM_CHANGES.md) for bug fixes and behavior improvements since `1.0.324`):

- [Creating meshes, Mesh File I/O, Ray/Mesh Intersection and Nearest-Point](http://www.gradientspace.com/tutorials/2017/7/20/basic-mesh-creation-with-g3sharp) — `DMesh3` basics, `StandardMeshReader`, `DMeshAABBTree3` ray and point queries, and custom traversals.
- [Mesh Simplification with Reducer class](http://www.gradientspace.com/tutorials/2017/8/30/mesh-simplification) — `Reducer`, `DMesh3.CheckValidity`, and `MeshConstraints`.
- [Remeshing and Mesh Constraints](http://www.gradientspace.com/tutorials/2018/7/5/remeshing-and-constraints) — `Remesher`, projection targets, `MeshConstraints`, and Unity remeshing animations.
- [Voxelization/Signed Distance Fields and Marching Cubes Remeshing](http://www.gradientspace.com/tutorials/2017/11/21/signed-distance-fields-tutorial) — `MeshSignedDistanceGrid`, `MarchingCubes`, `DenseGridTrilinearImplicit`, and 3D lattices.
- [3D Bitmaps, Minecraft Cubes, and Mesh Winding Numbers](http://www.gradientspace.com/tutorials/2017/12/14/3d-bitmaps-and-minecraft-meshes) — `Bitmap3`, `VoxelSurfaceGenerator`, and `DMeshAABBTree3` mesh winding numbers.
- [Implicit Surface Modeling](http://www.gradientspace.com/tutorials/2018/2/20/implicit-surface-modeling) — implicit primitives, voxel/level-set/functional booleans, offsets, and blending.
- [DMesh3: A Dynamic Indexed Triangle Mesh](http://www.gradientspace.com/tutorials/dmesh3) — deep dive into `DMesh3` internal data structures and operations.
- [Surfacing Point Sets with Fast Winding Numbers](http://www.gradientspace.com/tutorials/2018/9/14/point-set-fast-winding) — Fast Mesh/PointSet Winding Numbers and how to use the g3Sharp implementation.

## Related Upstream & Community Branches

- Upstream `gradientspace/geometry3Sharp` has a separate [`dotnet8` branch](https://github.com/gradientspace/geometry3Sharp/tree/dotnet8) with breaking API changes (including removal of `float` vector/math types). This fork intentionally stays API-compatible with `1.0.324` (`net10.0` + `netstandard2.0`).
- A dedicated Unity-focused fork is maintained by the ViRGIS project at [ViRGIS-Team/ViRGiS-Geometry](https://github.com/ViRGIS-Team/ViRGiS-Geometry).

## Historical Projects Using Upstream geometry3Sharp

- [Gradientspace Cotangent](https://www.cotangent.io/) — 3D printing and mesh repair/modeling tool.
- [Nia Technologies NiaFit](https://niatech.org/technology/niafit/) — 3D-printed prosthetic and orthotic design.
- [OrthoVR Project](https://orthovrproject.org/) — 3D-printed lower-leg prosthetic design in VR.
- [Archform](https://www.archform.co/) — clear dental aligner design/planning app.

## Unity Interop (`G3_USING_UNITY`)

`geometry3Sharp` targets `netstandard2.0` (compatible with modern Unity runtimes) and supports transparent conversion with Unity types when the `G3_USING_UNITY` scripting define symbol is enabled in Unity's **Player Settings**.

Once `G3_USING_UNITY` is defined, `float` types convert implicitly between `g3` and Unity:

```csharp
Vector3 unityVec;
Vector3f g3Vec;
unityVec = g3Vec;
g3Vec = unityVec;
```

`float` → `double` conversions are implicit, while `double` → `float` requires an explicit cast:

```csharp
Vector3d g3Vecd = gameObject.transform.position;
gameObject.transform.position = (Vector3)g3Vecd;
```

Supported interop types: `Vector2`, `Vector3`, `Quaternion`, `Ray`, `Color`, and `Bounds` (with `AxisAlignedBox3f`). Note that implicit conversions do not apply directly inside mixed arithmetic expressions (`Vector3f + Vector3` requires casting one operand).

## Credits

- Original library created by Ryan Schmidt ([gradientspace](http://www.gradientspace.com)).
- Many foundational data structures and algorithms were ported from WildMagic5 and GTEngine by David Eberly at [Geometric Tools](https://www.geometrictools.com/) (Boost Software License 1.0).
- `MeshSignedDistanceGrid` was implemented based on C++ [SDFGen](https://github.com/christopherbatty/SDFGen) by Christopher Batty and Robert Bridson.
- All additional algorithms, papers, and permissive third-party sources introduced in this fork are documented in [SOURCES.md](../SOURCES.md).
