# Metamorphic bug hunt 2: wrap, meshers, contouring, loft, I/O, geodesics (2026-11)

Status: research spike (tier S: this note, new stages of one benchmark harness, tests that pin what holds; no library change),
2026-11-22, on master `42e33ebb` (started on `57623890`; the findings are the same on both).
Harness: [benchmarks/MetamorphicBugHunt](../../benchmarks/MetamorphicBugHunt/README.md), stages `io`, `loft`, `contour2`,
`geodesic`, `intrinsic`, `quad`, `wrap2`, `mesh2d2`, `tet`, `robust`, `findings2`; raw output of the run described here:
`benchmarks/MetamorphicBugHunt/results/hunt2_master_42e33ebb.txt` (Linux x64, net10.0, 4 cores, 75 s; the FAIL, INFO and STAGE
lines only).
Tests: `tests/geometry3Sharp.Tests/MetamorphicBugHunt2Tests.cs` (84 cases; locally 16 s together, the slowest 1.4 s) with
`Harness/IndependentChecks.cs`.
Follows [METAMORPHIC_BUG_HUNT_2026-11.md](METAMORPHIC_BUG_HUNT_2026-11.md) (hunt 1: chains of Boolean, repair, remesh, tet,
wrap, contouring, 2D mesher; one defect, #1337) and uses its method and its independent checks. Catalogue of relations:
[design/METAMORPHIC_RELATIONS.md](../design/METAMORPHIC_RELATIONS.md).

**Question.** Do the alpha wrap and `PassageWrap`, `AlphaWrapFeaturePass`, the tet mesher, the quad remesher, the 2D mesher,
the contouring (`MarchingCubes`, `DualContouring`, `TpmsLattice`), `ContourLoft`, the readers and writers, the geodesic
family and the intrinsic triangulation report a wrong result as success, on procedural inputs, under the relations
`Similarity.All()` (2^k bit for bit), reordering, orientation flip, duplicated components, documented contract against the
result, and `Compute()` reuse?

Out of scope (other sessions): `MeshPostRepairCleanup`, `SolidBooleanPipeline`, `ExactMeshBoolean`, `ExactMeshArrangement`.

## 1. Result in six lines

1. **Three findings**, filed as #1375, #1376 and #1377. F1: `TpmsLattice` with a `DomainMesh` whose vertex ids are not dense returns `Ok` with the lattice of
   another solid. F2: `DualContouring` returns an empty mesh, or a shifted surface, for a field whose values are small (the
   same level set as the field times a constant). F3: several classes without a documented magnitude range report success
   with a wrong result outside about 2^-240 .. 2^250 (the "false success outside the range" class of
   [qa/testing-and-invariance.md](../qa/testing-and-invariance.md#25a-supported-input-range-correct-inside-fail-gracefully-outside-owner-2026-11-09)).
   Section 4.
2. **Everything else holds**: 9496 checks pass; the 37 failures of the run are the three findings (stage `findings2`). No
   call above 20 s on a valid input (F1 produced one run of more than 10 minutes, section 4.1).
3. Two of the three findings came from **relations that are not in the brief's list** and that hunt 1 did not run: "a mesh
   with gaps in its ids is the same input as its compact copy" (F1) and "a field times a positive constant has the same
   level set" (F2). The third came from extending the 2^k relation from 2^10 to 2^300 (F3).
4. The relations of the brief found **no defect** in any of the eleven areas: 2^k bit for bit, the similarities, vertex and
   triangle reordering, orientation flip, a far duplicate, the documented contracts and `Compute()` reuse all hold
   (section 3).
5. Several checks fired on **documented limits or on expectations of the harness** (section 5), among them one that looked
   like a defect for an hour (a two-point geodesic Bezier curve longer than its geodesic: the documented cut locus case, O2).
6. `AlphaWrapFeaturePass` (HP4 option 1, merged in #1339) keeps every guarantee it states on 30 wraps of 10 inputs, by a
   check that shares no code with it.

## 2. Method

As hunt 1 (its section 2): small procedural inputs (`Shapes.cs`; new here: a tube with a through hole, an L-shaped prism, open
and stretched grids, terrains, jittered closed surfaces, contour polygons, distance and density fields), every Success checked
by `Checks.cs` (closed and manifold from the directed edges and vertex fans, volume, area, Euler characteristic, the winding
number by solid angles, brute-force distances). New independent checks:

| Check | How |
|---|---|
| pins of `PassageWrap` | the winding number of 41 points per axis segment is 0 and their brute-force distance to the wrap is at least the radius |
| Delaunay property of `QualityMesh2` | a brute-force in-circle test in double with a relative margin, across every edge that is not a subsegment (`Conforming`: across every edge) |
| `ConstrainedMesh2` structure | `Neighbours` against the directed edges, every subsegment an edge, every boundary edge a subsegment, the triangles start at their smallest id |
| intrinsic triangulation | the sum of the triangle areas, V - E + F, the corner angle sum of every input vertex, the triangle inequalities, a traced edge's length and its distance to the surface |
| geodesic distances | between the straight distance and a Dijkstra distance over the edges (own implementation) |
| tet report | the smallest dihedral angle recounted from the tets against `Targets.WorstMinDihedral` |
| polygon mesh | directed edges of the faces (as hunt 1) |

**A firing check is a finding only if the documentation promises otherwise** (hunt 1, section 2). Every first firing was read
against the class comment and the guide. Three candidates were dropped that way (section 5, items 1 to 3).

The feature pass is internal: the harness reaches it by reflection, the tests through `InternalsVisibleTo`.

## 3. Relations tried

| Stage | Operations | Cases | Passed | Failed | Time |
|---|---|---|---|---|---|
| `io` | `StandardMeshReader` / `StandardMeshWriter`: obj, off, stl (ascii, binary), ply (ascii, binary, float), g3mesh, 3mf, glb, gltf, xyz | 12 formats x 5 meshes, attributes, 2^-300 .. 2^300 | 1404 | 0 | 0.8 s |
| `loft` | `ContourLoft`, `ContourLoftStack` | 8 contour pairs x 2 costs x normalized x caps; 6 stacks x saddle | 2666 | 0 | 3.0 s |
| `contour2` | `MarchingCubes`, `DualContouring`, `TpmsLattice` | 5 fields x 3 methods; 6 types x 3 styles, fractions, divisions | 594 | 0 | 9.4 s |
| `geodesic` | `PolyhedralGeodesicDistance` and `TracePath`, `MeshGeodesicDistance`, `MeshGeodesicPath`, `MeshGeodesicBezier`, `VectorHeatMethod` | 8 meshes x 3 sources | 823 | 0 | 2.7 s |
| `intrinsic` | `IntrinsicTriangulation`, `DelaunayRefine`, `ExtractCommonSubdivision` | 9 meshes x 3 angle bounds | 602 | 0 | 0.6 s |
| `quad` | `QuadRemesher` | 6 meshes x 7 option sets | 496 | 0 | 10.0 s |
| `wrap2` | `PassageWrap`, `AlphaWrapFeaturePass`, options of `MeshAlphaWrap` | 5 passages x simplify; 10 inputs x 3 alpha / offset; 5 inputs x 8 options | 1550 | 0 | 20.4 s |
| `mesh2d2` | `QualityMesh2` | 5 domains x 8 option sets | 575 | 0 | 0.3 s |
| `tet` | `MeshTetrahedralization` | 5 inputs x 9 option sets | 721 | 0 | 22.7 s |
| `robust` | id gaps, field x 2^k, scales 2^-320 .. 2^400 over 14 operations | 2 meshes; 9 factors; 19 exponents | 57 | 0 | 3.4 s |
| `findings2` | the reduced cases of section 4 and the observations of section 6 | | 8 | 37 (F1: 2, F2: 9, F3: 26) | 2.5 s |
| total | | | 9496 | 37 | 75 s |

Relations per area ("holds" means on every case of the stage; the details are in the stage files):

| Relation of the brief | Where | Result |
|---|---|---|
| 2^k, bit for bit (2^-10, 2^10) | every stage | holds everywhere: every reader and writer that stores doubles, `ContourLoft` and the stack, `MarchingCubes`, `DualContouring`, `TpmsLattice`, the polyhedral and the heat distance, FlipOut and Bezier lengths, the intrinsic flips and refinement, `QuadRemesher` with every option (also the pure-quad status), `PassageWrap`, the feature pass, the wrap with every option, the tet mesher with every option, `QualityMesh2` with every option (the same triangle list) |
| the other members of `Similarity.All()` | `loft`, `geodesic`, `intrinsic`, `wrap2` (passages), `contour2` (the lattice frame), tests | holds within the resolution of the transformed coordinates (the status, the solid, the cost or distance scaled) |
| 2^k far beyond 2^10 | `robust` (exponents -320 .. 400) | the scaled result from 2^-100 to 2^100 for every operation (`VectorHeatMethod`: compared to 1e-6, the others bit for bit), with two exceptions: `DualContouring` is exact at 2^-30 and not at 2^-48 (**F2**), and `PassageWrap` answers `InvalidInput` at 2^-100 on the tube (the wrap's documented range, section 4.3); outside about 2^-240 .. 2^250: **F3** |
| vertex and triangle reordering | `geodesic` (the same distances to 1e-12), `intrinsic` (the same Delaunay edge lengths), `wrap2`, `tet`, `quad`, `mesh2d2`, `contour2` (the lattice's domain mesh) | holds as far as promised: the same result where the method is unique, a valid result of the same band elsewhere (O3 for the quad remesher) |
| start vertex, direction, list order of contours and pins | `loft` (start vertex, swap, reversal, order of the contours of a slice, upside down), `wrap2` (reversed axes, pin order, a pin twice: the same wrap bit for bit) | holds |
| orientation flip, mirror | `io` (the flip is kept), `geodesic`, `intrinsic`, `loft`, `contour2` (mirrored field, negated field with `InsideIsLess` flipped: the same mesh), `tet`, `wrap2`, `quad`; an inside-out `DomainMesh` is refused | holds |
| a far duplicate | `loft` (stack: twice the volume and triangles), `contour2` (twice the triangles, the same ids pattern), `geodesic` (the same distances on the first part bit for bit, the second unreached: NaN for the heat method as documented), `mesh2d2` (two domains in one input), `tet`, `quad`, `io` (two meshes in one file) | holds |
| contract against the result | every stage: closed, manifold, finite, `NotCleanReason`, the input inside, the offset kept, pins clear, angle and size bounds outside `BadTriangles`, Delaunay, the tet volume in the wrap's band, a failure without a mesh | holds, except **F1** |
| `Compute()` reuse | every class with a `Compute()` / `Generate()` / `Apply()`; other sources or options in between for the geodesic classes | holds |
| serial = parallel, one thread, repeated runs | `contour2`, `quad`, `wrap2`, `tet` | holds |
| id gaps (new) | `io`, `robust`: wrap, tet and `PassageWrap` (the same result bit for bit), `QuadRemesher` (the status and a manifold result: its mesh depends on the vertex order, O3), `MeshRobustRepair` with `AlphaWrapSharpFeatures`, every geodesic class, the intrinsic triangulation | holds, except `TpmsLattice.DomainMesh`: **F1** |
| field x constant (new) | `robust`: `MarchingCubes` at 2^-300 .. 2^300, `DualContouring` at 2^-40, 2^-20, 2^20, 2^100 | holds; `DualContouring` from 2^-52 down: **F2** |
| a metric of the surface (new) | `geodesic`: symmetry, triangle inequality, d(A u B) = min, the distance does not change when every triangle is split in its plane | holds (1e-11 relative) |

## 4. The findings

### 4.1 F1 (#1375): `TpmsLattice` reads a `DomainMesh` with the ids of its compact copy

- **Severity**: high for the feature. A silent wrong `Ok` at ordinary scale; every mesh that went through an edit that removes
  vertices (`Reducer`, `MeshEditor`, a repair) has id gaps.
- **Relation**: a mesh with id gaps is the same solid as its compact copy.
- **Minimal input**: the box `[-1, 1]^3` (`TrivialBox3Generator`, 12 triangles) rebuilt with one extra vertex appended first
  and removed again, so that its vertex ids are 1 to 8. `IsClosed`, `CheckValidity` and `NotCleanReason` pass, the volume is 8.
  `new TpmsLattice { DomainMesh = mesh, CellSize = 0.5, Divisions = 8, VolumeFraction = 0.5, Style = TpmsStyle.SkeletalLow }`:

| Domain mesh | Status | Triangles | Volume | Bounds |
|---|---|---|---|---|
| compact box | Ok | 43216 | 3.8609 | `[-1, 1]^3` |
| id gap, the removed vertex was at (0, 0, 0) | Ok | 22648 | 1.5569 | inside the box |
| id gap, the removed vertex was at (3, 0, 0) | Ok | 18336 | 1.0013 | x up to 2.84: the lattice leaves the domain |
| `new DMesh3(mesh, true)` of either | Ok | 43216 | 3.8609 | `[-1, 1]^3` |

  With a sphere as the domain and a gap after every third vertex (stale positions about 1000 away) one `Compute()` did not end
  within 40 s in a probe (not in the harness): the scrambled domain has a huge bounding box.
- **Violated contract**: "clipped to a box or to the inside of a closed mesh" and `DomainMesh`: "the inside of a closed manifold
  mesh in world coordinates"; the result is the lattice in another solid, with `Status.Ok`.
- **Cause** (read, not fixed): `mesh_generators/TpmsLattice.cs` 513 to 515. `local = new DMesh3(DomainMesh, true)` is a compact
  copy; the loop `foreach (int vid in local.VertexIndices()) local.SetVertex(vid, toLocal * (DomainMesh.GetVertex(vid) - Origin))`
  reads the original with the copy's ids, so every vertex after the first gap gets the position of another, or of a removed,
  vertex.
- **Suggested fix**: transform the copy's own vertices, `toLocal * (local.GetVertex(vid) - Origin)`. The pattern occurs only
  here in the areas of this hunt (searched).
- **Reproducer** (fails on master; harness: stage `findings2`, lines `H2-F1`):

```csharp
[Fact]
public void TpmsLattice_DomainMeshWithAnIdGap_IsTheSameDomain()
{
    DMesh3 box = new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
    var gaps = new DMesh3();
    int unused = gaps.AppendVertex(new Vector3d(3, 0, 0));
    foreach (int v in box.VertexIndices()) gaps.AppendVertex(box.GetVertex(v));
    foreach (int t in box.TriangleIndices()) { Index3i tv = box.GetTriangle(t); gaps.AppendTriangle(tv.a + 1, tv.b + 1, tv.c + 1); }
    gaps.RemoveVertex(unused, true, false);
    Assert.Equal("", MeshRobustRepair.NotCleanReason(gaps));
    TpmsLattice Run(DMesh3 domain) { var l = new TpmsLattice { DomainMesh = domain, CellSize = 0.5, Divisions = 8, VolumeFraction = 0.5, Style = TpmsStyle.SkeletalLow }; l.Compute(); return l; }
    TpmsLattice a = Run(box), b = Run(gaps);
    Assert.Equal(TpmsStatus.Ok, b.Status);
    Assert.Equal("", IndependentChecks.SameBits(a.Mesh, b.Mesh));
}
```

### 4.2 F2 (#1376): `DualContouring` loses the surface of a field with small values

- **Severity**: medium to low. Silent (no status, no exception, an empty or a moved mesh) at ordinary geometry; it needs a field
  whose gradient at the surface is below 2.2e-16: a density or probability field at a low level, a field in small units. A signed
  distance field has gradient 1 and is not affected, so `MeshOffset` and the mesh contouring are safe.
- **Relation**: f and c f (c > 0, iso value 0) have the same level set. `MarchingCubes` gives the same mesh bit for bit for every
  c from 2^-300 to 2^300.
- **Minimal input**: f(p) = c (|p - (0.03, -0.02, 0.05)| - 1), `Bounds` `[-1.5, 1.5]^3`, `CellSize` 0.125, `InsideIsLess`.

| c | `MarchingCubes` | `DualContouring` (Simplify, the default) | Simplify off |
|---|---|---|---|
| 1 .. 2^-50 | 2404 triangles, volume 4.15002 | 504 triangles, volume 4.18689 | 2408 triangles, volume 4.20362 |
| 2^-52, 2^-60, 2^-140 | the same | **0 triangles** | 2408 triangles, volume 4.12617 (4.12173 at 2^-140): the vertices moved |
| 2^-150 and below | the same | 0 triangles | 0 triangles |

  A case without an artificial factor: exp(-40 r^2) with `InsideIsLess = false`. At the level 1e-10 (a sphere of radius 0.759,
  volume 1.830) `MarchingCubes` with bisection gives volume 1.806 and `DualContouring` 1.823; at 1e-30 (radius 1.314, volume
  9.506) `MarchingCubes` gives 4184 triangles of volume 9.465 and `DualContouring` 0 triangles.
  The same root shows under a uniform scale 2^k of field and geometry: at k = -48 and below the result is no longer the scaled
  mesh (at 2^-60: the same vertices and triangle count, other quad diagonals, volume 0.05 to 0.17 % off), and from 2^-150 down the
  mesh is empty; the class comment calls these steps scale invariant. These are 10 of the 12 `DualContouring` lines of F3.
- **Violated contract**: the class comment promises the contour of `Implicit` at `IsoValue` with normals from "the
  central-difference gradient of Implicit" and puts no condition on the size of the field values.
- **Cause** (read, not fixed): `mesh_generators/DualContouring.cs` 316, 326 and 328 normalize the Hermite normal with
  `Vector3d.Normalized`, which returns zero below the absolute `MathUtil.Epsilon` (the comment at `exact_gradient` handles the
  geometric scale, not the scale of the values); lines 311 and 2413 compare `|fb - fa|` with `MathUtil.Epsilon`; and the grid
  stores the node values as `float` (1400, 1407), which underflow to 0 below 1.4e-45.
- **Suggested fix**: normalize the gradient scale free (divide by its largest component first, then by the length, with a check
  for an exact zero); make the two `|fb - fa|` tests relative to `max(|fa|, |fb|)`; store the node values times a power of two
  taken from the largest `|f - IsoValue|` of the grid. Or, as the smaller step, document the range of field values and report a
  result outside it (a counter like `NonManifoldTriangles`, or an `ArgumentException`), since an output change is the owner's call.
- **Reproducer** (fails on master; harness: `findings2`, lines `H2-F2`):

```csharp
[Theory]
[InlineData(-60, true)]
[InlineData(-60, false)]
[InlineData(-200, true)]
public void DualContouring_FieldTimesAConstant_SameLevelSet(int exponent, bool simplify)
{
    var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
    DMesh3 Run(double c)
    {
        var dc = new DualContouring { Implicit = new ImplicitFunction(p => c * ((p - new Vector3d(0.03, -0.02, 0.05)).Length - 1), box),
            Bounds = box, CellSize = 0.125, InsideIsLess = true, Simplify = simplify };
        dc.Generate();
        return dc.Mesh;
    }
    Assert.Equal("", IndependentChecks.SameBits(Run(1), Run(Math.Pow(2, exponent))));
}
```

  (`ImplicitFunction`: any `BoundedImplicitFunction3d` over a delegate, as `Field` in `MetamorphicBugHunt2Tests`.)

### 4.3 F3 (#1377): success with a wrong result outside an undocumented magnitude range

- **Severity**: low. No real input is there. It is the class that
  [qa/testing-and-invariance.md, 2.5a](../qa/testing-and-invariance.md#25a-supported-input-range-correct-inside-fail-gracefully-outside-owner-2026-11-09)
  names: outside the supported range an operation fails with a status, "no false success". The classes below have no range in
  their class comment and no row in the 2.5a table.
- **Relation**: the input scaled by 2^e gives the scaled result bit for bit, or a failure status. Input: the sphere mesh of 152
  vertices at (0.1, 0.2, 0.3); e in -320, -280, -260, -240, -200, -150, -140, -100, -60, -48, -30, 30, 60, 100, 200, 250, 260, 300,
  400. The first six classes below give the scaled result at every tested e from -240 to 250.

| Class | e = -260 | e <= -280 | e >= 260 |
|---|---|---|---|
| `MeshGeodesicDistance` | `true`, another result | `true`, NaN distances on the source's component | `false` (fine) |
| `PolyhedralGeodesicDistance` | `Ok`, 5e-11 relative off | `DegenerateTriangles` (fine) | `Ok`, a distance 1.6e-3 relative off |
| `IntrinsicTriangulation.DelaunayRefine` | `Success`, another result | `Incomplete` (fine) | `Success` with `MinAngle` 90 degrees |
| `VectorHeatMethod.TransportVector` | a transported unit vector of length 0 | `InvalidOperationException` (not documented) | `InvalidOperationException` |
| `MeshGeodesicPath` | `Success`, another length | `TraceFailed` (fine) | `TraceFailed` (fine) |
| `QuadRemesher` | `Success`, another mesh | `FieldFailed` (fine) | `FieldFailed` (fine) |
| `DualContouring` | (below 2^-30: F2) | an empty mesh (from 2^-150) | another mesh at 2^300 and 2^400 |

  14 of the 26 FAIL lines are the first six classes, 12 are `DualContouring` (10 of them the root of F2, two above 2^260).
  `PolyhedralGeodesicDistance` says "away from overflow and the subnormal range"; its wrong `Ok` starts at coordinates of 1e78.
- **Cause** (the first six classes): fourth powers of lengths (squared areas, products of squared lengths) leave the normal
  doubles at about 2^+-256. `DualContouring` above 2^260: not diagnosed.
- **Suggested fix**: one range statement for the intrinsic family (about 2^-240 .. 2^250) in the class comments and the 2.5a
  table, and an early check that returns the existing failure status (`false`, `DegenerateTriangles`, `InvalidInput`,
  an `ArgumentException` for `VectorHeatMethod` and `IntrinsicTriangulation`). Or a power-of-two normalization on entry, as
  `ContourLoft` does (it is bit exact from 2^-320 to 2^200 here, then `CapFailed`).
- **Held in the same sweep**: the wrap, `PassageWrap` and the tet mesher (`InvalidInput` / `WrapFailed` outside 2^+-128, as
  documented; the wrap also refuses a nonzero coordinate below 2^-128, such as `sin(pi) * 2^-100` of the tube),
  `MarchingCubes` (exact from 2^-320 to 2^400), `TpmsLattice` (the same), `ContourLoft`, `QualityMesh2` (the documented
  `ArgumentException` above 1e70).
- **Reproducer**: harness `findings2`, lines `H2-F3` (26 lines, one per class and exponent).

## 5. Checks that fired on documented limits, or on expectations of the harness

1. **A geodesic Bezier curve of two control points was longer than the geodesic** (0.49870 against 0.49836 with four segments,
   equal with one or two). The two vertices lie on a symmetry plane of the sphere mesh: there are two shortest geodesics of the
   same length, and the sample points come from both. Documented ("the curve is not unique where a geodesic between two of the
   intermediate points is not (cut locus)"). A probe over random vertex pairs of four meshes did not show it. Kept as O2.
2. **`MarchingCubes` with `LinearEdges` throws** `InvalidOperationException` on the torus and the two blobs: a crossing on a
   grid corner. Documented on the mode (it never repairs a violation).
3. **A `TpmsLattice` sheet at `Divisions = 8` has half the requested volume** (gyroid: 0.138 for 0.3). The wall is 0.7 cubes
   thick; the class asks for two. The fraction converges with the resolution (0.287 at 16, 0.294 at 32), and
   `RealizedVolumeFraction` reports it. Below about 0.1 at 8 divisions: `ContouringFailed`, "the lattice is empty".
4. **STL and glTF are not idempotent in their bytes**: STL writes the facet normal of the positions it is given (the first read
   rounded them); glTF stores float positions about the bounding box centre, which is another double for the read mesh. Both
   stay within the format's precision (documented for glTF); the exact formats write the same bytes again.
5. **ascii STL and OFF are not 2^k exact**: a fixed number of digits after the point (documented on `RealPrecisionDigits`).
6. **`g3mesh` keeps the id gaps of a mesh**, every other format writes compact lists. Both read back as the same solid.
7. **The wrap, the tet mesher, the quad remesher and the feature pass are not the same mesh for a permuted or flipped input**
   (the feature pass: 58 of 60 runs):
   ties and the rounding of the distance queries follow the order of the input's triangles. Every guarantee holds for the
   variant, the volume stays within the band. Not promised (the classes promise determinism and 2^k).
8. **`MarchingCubes` and `DualContouring` of a mirrored field are not the mirrored mesh** (volume up to 7e-4 relative off for
   marching cubes): the cube's triangulation table and the QEF are not mirror symmetric.
9. **A `TpmsLattice` with a permuted domain mesh differs by rounding** (1e-15): the nearest triangle of the distance query.
10. **`QualityMesh2` with `MinAngle = 33` on a star with tips of 32.3 degrees refines nothing** and lists all 12 triangles in
    `BadTriangles`: triangles whose small angle lies between two constrained edges, and those whose shortest edge spans such an
    angle, are not refined. Reported, as documented.
11. **`QualityMesh2` gives a few triangles more or less for duplicated or reordered input points** (150 against 148): the ids
    of the Steiner points shift. Valid, Delaunay and within the bounds each time.
12. **Polyhedral distances at UTM coordinates differ by 5e-7 relative** for a mesh of a millimetre: the transformed coordinates
    are rounded by that much (`Similarity.Resolution`). The same for the length of a traced path against its distance.
13. **`ContourLoft` refuses** a height of 1e-12 or 1e6 times the width (`CapFailed`, degenerate triangles; documented), contours
    whose planes are not separated, a repeated vertex, zero area, and reports a self-intersecting optimal band for offset,
    pinched or spiral contours. `ContourLoftStack` reports `Unsupported` for a contour without a counterpart in an end slice
    ("zero thickness") and `CrossingContours` for touching or identical contours. None is silent.

## 6. Observations (no issue)

- **O2: the two-point geodesic Bezier curve at the cut locus** (section 5, item 1). The curve is a polyline through points of two
  geodesics and its `Length` depends on the number of samples. If one curve per call is wanted: evaluate every parameter on the
  first geodesic found (keep its segments) instead of searching again in the triangulation that the earlier samples changed.
- **O3: the quad remesher's size for one solid depends on the vertex order.** A gridded box (area / edge^2 = 208) gives 143
  faces with 11 dropped faces as generated and when flipped, 196 and 185 faces with 0 and 1 dropped for two permutations; the
  pure-quad attempt succeeds for one order and ends with `BudgetExceeded` or `OrientationFolds` for another (torus,
  cylinder). Not promised otherwise, and the same fact as the tie-order spike of the tet mesher
  ([TET_TIE_ORDER_2026-11.md](TET_TIE_ORDER_2026-11.md)): a restart over a few relabelings would be a cheap lever for the
  pure-quad success rate.
- **O4: `PassageWrap` survives a wrong a priori bound.** With the safety factor of the bound set to 1.6 by hand (alpha too
  coarse) every result still passes the independent pin check: the a posteriori measurement and the fallback do what the class
  comment says. (A hand mutant of the mutation check of the tests; equivalent by design.)

## 7. What is left

Time-box: about five hours. The relations of the brief were exhausted without a defect after the first three; the findings
came in the last hour from two new relations.

1. **The id-gap relation on the rest of the library.** It is cheap (one helper, one comparison) and found F1 in its first run.
   Not run: `MeshOffset`, `Remesher`, `Reducer`, `MeshMeshCut`, `MeshPlaneCut`, the hole fillers, the terrain tools, the UV
   atlas, the deformers. A search for `new DMesh3(x, true)` followed by a read of `x` by id is the static form.
2. **The value-scale relation on other fields**: `MeshOffset`'s own field is a distance (safe); the winding-number contouring
   and every caller that passes a non-distance `Implicit` to `DualContouring` are open.
3. **Ranges for the classes of F3** (the QA 2.0 range task): the measured numbers are in the `robust` stage.
4. **`ContourLoftStack` beyond the six stacks here**: branching with holes, many slices with changing topology. The degenerate
   sweep (about 40 inputs, section 5 item 13) found only documented refusals.
5. **Readers on hostile input**: only write then read was run; `ReaderFuzzTests` owns the rest.
6. **Larger inputs and other core counts** (hunt 1, section 7, items 4 and 5): unchanged.
7. **`PolyMeshIO`, `TetMeshWriter`, `HeightGridReader` / `Writer`, `SVGWriter`**: not run.

**Reading for the next hunt.** The eleven areas hold under the standing relations: those are the relations their own tests
already run (`Similarity.All()`, reuse, determinism are rules of CLAUDE.md). The defects were where a relation is true of the
problem but nobody wrote it down for the class: the ids of a mesh are not part of the solid, the size of a field's values is
not part of its level set. So the next hunt should start from "what may I change in the input without changing the answer"
per class, not from the list of the last hunt. And every candidate needs the control step: the best-looking firing of this
hunt (O2) was a documented limit.

## 8. Sources

As hunt 1: metamorphic testing after Chen, Cheung, Yiu 1998 and the surveys cited in
[METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md); the solid angle of a triangle after Van Oosterom and Strackee 1983
(`Checks.Winding`, `IndependentChecks.Winding`, written from the paper). The in-circle determinant is the textbook one. No code
of another library was read or used. Listed in [sources/tools-and-verification.md](../sources/tools-and-verification.md).
