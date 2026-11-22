# Magnitude robustness of the geodesic and intrinsic classes and of DualContouring (2026-11)

Status: research study (tier S: this note and a probe project; **no library change**), 2026-11-22, on master `e875d747`.
Probes: [benchmarks/MagnitudeRobustness](../../benchmarks/MagnitudeRobustness/README.md); raw output of every number below:
`benchmarks/MagnitudeRobustness/results/master_e875d747.txt` (Linux x64, net10.0, 4 cores).
Issues: #1377 (false success outside an undocumented range), #1376 (`DualContouring` under a value scale), #1352 (the area
checks of `IntrinsicCommonSubdivision`, and the flips of `FlipToDelaunay` at small scales).
Follows metamorphic bug hunt 2 (PR #1378, `docs/research/METAMORPHIC_BUG_HUNT_2_2026-11.md` on its branch), finding F3 and F2.
Rule it serves: [qa/testing-and-invariance.md, 2.5a](../qa/testing-and-invariance.md#25a-supported-input-range-correct-inside-fail-gracefully-outside-owner-2026-11-09)
(inside the range correct, outside a status, never a false success).

**Question.** Why do these classes give another result, sometimes with a success status, when the input is scaled by 2^e far
from 1, and what is one consistent way to end that: (a) an exact internal rescaling by a power of two, (b) scale-free
formulas, (c) a checked range with a refusal, or (d) documentation only?

## 1. Result in eight lines

1. **One root for the mesh classes: a fourth power of a length.** The product of four lengths in Kahan's area formula
   (`IntrinsicTriangulation.area_from_lengths`), and the length of a cross product (`Vector3d.Length` squares components that
   are already squares). Both leave the normal doubles when an edge is outside about 2^-255 .. 2^255. Section 3.
2. **The limit is on the edge length, not on the coordinate**: the exact range on the test sphere (edges 0.23 to 0.45) is
   2^-254 .. 2^257, on the finer sphere of the quad remesher 2^-249 .. 2^256.
3. **Behind the fourth powers sits a second layer**: absolute thresholds on quantities that have a dimension (`1e-250`,
   `1e-280`, `1e-300`, `1e-12`, `1e-24`), and squared lengths at 2^+-512. Fixing only the area formula (a local experiment,
   section 5) moves the exact range of the triangulation to about 2^-507 .. 2^510 and **widens** the false-success band of
   `VectorHeatMethod`.
4. **Option (a) removes both layers at once.** A wrapper outside the library (the largest coordinate into [1, 2) by a power
   of two, run, lengths scaled back) gives the unit result **bit for bit at every tested exponent from 2^-1010 to 2^1020** for
   all twelve mesh operations. Its cost is one copy of the vertices: 0.24, 1.0 and 4.6 ms for 3176, 13256 and 54152 vertices,
   at most 6 % of the cheapest operation (the flips alone) and 0.1 % to 3 % of the others. Section 4.
5. **A new finding inside today's range**: `MeshGeodesicDistance.TracePath` is 2^k exact only from 2^-31 to 2^50 on the test
   input (the polyline has another number of points outside). Its tracing compares lengths with absolute constants. The
   wrapper makes it exact everywhere. Section 3.3.
6. **`DualContouring` is a different problem: a ratio of two scales.** What must not be small is the gradient, values over
   geometry; below 2.2e-16 the Hermite normal is set to zero. A power-of-two scaling of the stored values repairs the relation
   "c f has the level set of f" over the whole range and changes no bit on five ordinary fields, but it does **not** repair a
   field whose values are small only near the surface (exp(-40 r^2) at the level 1e-20): that needs a relative normalization,
   option (b). A prototype of (b) changes no existing test and none of 21 ordinary cases, and gives the level 1e-20 as
   540 triangles with a volume 0.06 % below the analytic one (section 6.4). Section 6.
7. **`DualContouring` at 2^300 and 2^400** (not diagnosed in #1377): the simplification's turned-over test
   `n0.Dot(n1) > 0` (`AdaptiveDualContouring.cs` 428) multiplies two cross products, a fourth power again; from 2^262 it is
   `inf - inf = NaN` for some triangles, and 7 of 57 valid clusters are rejected. The result is a closed contour of the same
   surface with 168 triangles in place of 132 (volume 4.1964 against 4.1875): less simplified, not wrong. With `Simplify` off
   the result is exact up to 2^512. Section 6.3.
8. **Recommendation** (section 8): (a) on entry for every class that takes a mesh, (b) only in `DualContouring`'s normal and
   crossing tests, (c) as the backstop where the scaled input is still not representable, and rows in the 2.5a table for all.
   Three points are owner decisions (section 9).

## 2. Method

- **Relation**: the input scaled by 2^e gives the scaled result bit for bit, or a failure status. A run is classified as
  `exact`, `status X`, `threw X` or `another` (a success with another result; the relative deviation is given to one digit).
  The comparison covers every output value: distances, path points, edge lengths, counts, indices.
- **Exponents**: every multiple of 10 from -1070 to 1020, and every integer in 25 .. 65, 225 .. 300 and 500 .. 545 of both
  signs (the quad remesher: every integer in 245 .. 270). An exponent whose scaled input is not exact (subnormal coordinates)
  is reported as such and not judged.
- **Inputs**: the sphere of 152 vertices at (0.1, 0.2, 0.3) of the hunt; a sphere of 728 vertices for the quad remesher; the
  sheared 3 x 3 grid of the comment on #1352 for the flips and the common subdivision; for `DualContouring` the distance to a
  sphere, f(p) = |p - (0.03, -0.02, 0.05)| - 1, on [-1.5, 1.5]^3.
- **Root cause**: the expression was read in the source, then its range was predicted from the edge lengths of the input
  (stage `roots`) and compared with the first exponent of the sweep that is not `exact`. A cause that is read but not
  confirmed by such a prediction or by an experiment is marked "read".
- **Option (a)** was measured with a wrapper in the probe (`Program.Prescaled`, `DcStage.Prescaled`), so the library is
  untouched. A prescaling inside a class can do less work than the wrapper (no second mesh), so the wrapper's time is an upper
  bound.
- **Option (b)** was measured once, on the central formula, with a local patch that is not part of this PR
  (`benchmarks/MagnitudeRobustness/experiments/area_from_lengths.patch`).

## 3. Root cause per class

### 3.1 The fourth powers

| Expression | Where | Leaves the normal doubles when |
|---|---|---|
| `(a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c))`, 16 area^2 from the sides | `mesh_ops/IntrinsicTriangulation.cs` 909 (`area_from_lengths`; `angle_from_lengths` 916 and `cotan_from_lengths` 923 call it) | an edge is below about 2^-255 (subnormal, then 0 from 2^-268) or above 2^255 (infinite) |
| `ExactInterval.Square(uy * wz - uz * wy) + ...`, the squared cross product | `mesh_ops/PolyhedralGeodesicDistance.cs` 128 (the degenerate-triangle test) | the same |
| `Math.Sqrt(cx * cx + cy * cy + cz * cz) / L`, the height of a triangle | `mesh_ops/PolyhedralGeodesicDistance.cs` 192 (`Place`) | the same |
| `e1.Cross(e2).Length`, twice the area | `parameterization/MeshParameterization.cs` 243 (`TriangleFrame`, used by `ParamMesh`, `CrossField`, `QuadRemesher`) | the same |
| `n0.Dot(n1) > 0`, two cross products | `mesh_generators/AdaptiveDualContouring.cs` 428 | an edge of the contour is above about 2^258 (NaN from `inf - inf`) |

Prediction against measurement, the sphere of 152 vertices (edges 2^-2.11 .. 2^-1.16, products 2^-6.1 .. 2^-4.7): some
product is subnormal for e <= -254, every product is 0 for e <= -268, every product is infinite for e >= 258. The sweep:
`MeshGeodesicDistance` is exact from 2^-254 to 2^257, `DelaunayRefine` from 2^-254 to 2^257, `MeshGeodesicPath` from 2^-254 to
2^256, `PolyhedralGeodesicDistance` from 2^-252 to 2^257. The hunt's "-240 .. 250" were its tested exponents.

What the formulas return outside (stage `roots`, one triangle with an angle of 45.273 degrees and a cotangent of 0.9905):

| Scale | Product | Area | Angle | Cotangent |
|---|---|---|---|---|
| 2^-250 .. 2^256 | normal | exact | 45.273 | 0.990521 |
| 2^-260 | subnormal | 12 digits | 45.273 | 0.990521 |
| 2^-266 | subnormal | 3 digits | 45.237 | 0.991761 |
| 2^-280 | 0 | 0 | 0 | 1e30 (the degenerate branch) |
| 2^260, 2^300 | infinite | infinite | 90.000 | 0 |
| 2^520 | infinite | infinite | NaN | NaN (b^2 + c^2 - a^2 is `inf - inf`) |

So above 2^257 every angle is 90 degrees and every cotangent weight 0: `DelaunayRefine` sees a mesh that meets any angle
bound and returns `Success` with `MinAngle` 90; below 2^-268 every triangle is degenerate with weights of +-1e30.

### 3.2 Per class

"Exact" is the range of tested exponents with the scaled result bit for bit. "False success" lists the bands where the
status is a success and the result is another one. Lines are in the files at `e875d747`.

| Class | Exact | False success outside | Honest outside | Root |
|---|---|---|---|---|
| `IntrinsicTriangulation.FlipToDelaunay` (#1352) | 2^-269 .. 2^255 (sheared grid) | everywhere else: other flips (the method has no status) | -- | `area_from_lengths` 909 through `OppositeCotan` |
| `IntrinsicTriangulation.DelaunayRefine` | 2^-254 .. 2^257 | 2^-267 .. 2^-255 (up to 1e-1); 2^258 .. 2^512 (`Success`, `MinAngle` 90); 2^-1010 .. 2^-536 (`Success`) | `Incomplete` at 2^-535 .. 2^-268 and from 2^513 | 909; from 2^513 `b * b + c * c - a * a` (917) |
| `ExtractCommonSubdivision` (#1352) | 2^-256 .. 2^255 | 2^256 .. 2^511: `Success` with another point count; 2^-269 .. 2^-257: 1e-15 | `Inconsistent` below 2^-269 and from 2^512 | 909; the checks at `IntrinsicCommonSubdivision.cs` 403 to 405 and 436 to 439: `err > 1e-6` is false for NaN, and `Math.Max(area, 1e-300)` is an absolute floor on an area |
| `MeshGeodesicDistance` | 2^-254 .. 2^257 | 2^-535 .. 2^-268: `true` with NaN distances; 2^-266 .. 2^-255: up to 1e-1 | `false` below 2^-535 and from 2^258 | 909 (also with `IntrinsicDelaunay = false`, line 210); the NaN: masses of 0 and weights of 1e30; `u[i] > 1e-280` (388) is an absolute test on a value of dimension 1 / length^2 (read) |
| `MeshGeodesicDistance.TracePath` | **2^-31 .. 2^50** | everywhere else where `Compute` is true: another number of path points | as `Compute` | absolute constants in the tracing: `DistanceSquared(p) > 1e-24` (724), `rate < -1e-15` (675), `area2 < 1e-300` (611) (read; confirmed only by the fact that the prescaled run is exact) |
| `PolyhedralGeodesicDistance` | 2^-252 .. 2^257 | **2^258 .. 2^513: `Ok`, distances more than 10 % off**; 2^-265 .. 2^-253 | `DegenerateTriangles` below 2^-265 and from 2^516, `PartiallyReached` at 2^514, 2^515 | 128 and 192. The class comment says "away from overflow and the subnormal range"; the limit is at edges of 1e77, not 1e154 |
| `MeshGeodesicPath` | 2^-254 .. 2^256 | 2^-262 .. 2^-255 | `TraceFailed` outside, `NoPath` from 2^514 | 909 |
| `MeshGeodesicBezier` | 2^-253 .. 2^256 | 2^-260 .. 2^-254 | `TraceFailed`, `Stuck`, `NoPath` | 909 |
| `VectorHeatMethod` (`TransportVector`, `ComputeLogMap`) | 2^-253 .. **2^242** | 2^243 .. 2^257 (1e-15 growing to NaN); 2^-267 .. 2^-254 | `InvalidOperationException` (not documented) outside | 909; above 2^242: the solutions have the dimension 1 / length^2, so `phi[i] > 1e-250` (312, absolute) fires and raises the diffusion time, and `y * y` (385) is 1 / length^4 |
| `QuadRemesher` | 2^-249 .. 2^256 | 2^-265 .. 2^-250; 2^257 | `FieldFailed` outside | `TriangleFrame`, `MeshParameterization.cs` 243 |
| `DualContouring` | section 6 | | | |

### 3.3 The finding inside the range: `TracePath`

`MeshGeodesicDistance.TracePath` is the only operation of the sweep that is not 2^k exact inside 2^-200 .. 2^200. It is exact
from 2^-31 to 2^50 on the test input; outside, the path has another number of points. `MeshGeodesicDistance`'s tests run the
2^k scales of `Similarity.All()`, which stay inside that band. The paths were not compared for their geometry here (the
count differs, so the comparison stops); the constants above merge or keep nearly coincident points, so the expected
difference is in duplicates, not in the route. Not filed; see section 9.

## 4. Option (a): exact prescaling, measured

The wrapper: `k = ILogB(largest |coordinate|)`; every coordinate times 2^-k (`ScaleB`, exact); the operation with its length
parameters times 2^-k; every length of the result times 2^k. After the scaling the largest coordinate is in [1, 2) and an
edge is between 2^-52 (the resolution of the coordinates) and 4, so a fourth power is between 2^-208 and 2^8.

**Exactness** (stage `prescale`): `exact` at every tested exponent from 2^-1010 to 2^1020 for `MeshGeodesicDistance` (both
modes), `TracePath`, `PolyhedralGeodesicDistance` with `TracePath`, `MeshGeodesicPath`, `MeshGeodesicBezier`,
`FlipToDelaunay`, `DelaunayRefine`, `ExtractCommonSubdivision`, both `VectorHeatMethod` calls and `QuadRemesher`. Below
2^-1020 the input itself has subnormal coordinates.

**What it changes inside today's range**: the result for a mesh becomes the result of that mesh scaled into [1, 2). Where a
class is 2^k exact today these are the same bits, so nothing changes; a result changes only where the class breaks the 2^k
rule today. In the sweep that is `TracePath` alone.

**Cost** (stage `cost`; ms, median of 5; the two plain columns are the same call measured before and after):

| Operation | 3176 vertices: plain / prescaled / plain | 13256 vertices | 54152 vertices |
|---|---|---|---|
| `MeshGeodesicDistance` (build and one source) | 36.0 / 31.5 / 39.7 | 160 / 167 / 147 | 883 / 947 / 993 |
| `PolyhedralGeodesicDistance` | 161 / 181 / 164 | 1504 / 1517 / 1512 | 5189 / 5680 / 5803 |
| `MeshGeodesicPath` | 7.2 / 9.5 / 6.4 | 45.8 / 35.2 / 33.7 | 231 / 201 / 211 |
| `IntrinsicTriangulation` and `FlipToDelaunay` | 5.1 / 5.3 / 4.1 | 17.9 / 17.8 / 18.1 | 111 / 86 / 82 |
| `DelaunayRefine(25)` | 7.2 / 8.8 / 9.5 | 24.7 / 28.9 / 33.5 | 143 / 168 / 155 |
| `VectorHeatMethod.TransportVector` | 92.7 / 75.3 / 67.5 | 426 / 442 / 430 | 2696 / 2419 / 2753 |
| `QuadRemesher` | 76.4 / 61.2 / 62.5 | 400 / 413 / 384 | -- |
| the scaled copy alone | 0.24 | 1.0 | 4.6 |

The two plain columns differ by up to 30 %, so this A/B does not resolve the overhead. What is measured is the copy: 4.6 ms
at 54152 vertices, 5.6 % of the cheapest operation (the flips, 82 ms) and 0.1 % to 3 % of the others. Inside a class the
copy can be avoided where the class copies the positions anyway (`PolyhedralGeodesicDistance` holds `vx`, `vy`, `vz`).

**Where (a) needs care.**
- Classes that expose their triangulation (`MeshGeodesicPath.Triangulation`, `VectorHeatMethod.Triangulation`,
  `MeshGeodesicBezier.Triangulation`): a prescaling at the class entry would hand out a triangulation in scaled units. So
  the scaling belongs in `IntrinsicTriangulation` itself (the lengths stored times 2^-k; `Length`, `TriangleArea`,
  `GetTriangleLayout` and the tracing return caller units). That is one place for eight of the twelve operations of the sweep.
- A mesh whose parts differ by more than about 2^200 in size is not repaired by one scale: that is disparity, the other
  column of the 2.5a table, and stays for option (c).
- The scaling is exact only while no scaled coordinate becomes subnormal (a coordinate more than 2^1022 below the largest).

## 5. Option (b) on the central formula: a local experiment

`area_from_lengths` with a rare path (the patch file, 10 lines): if the longest side is outside 2^-250 .. 2^250, the three
sides times a power of two, the same formula, the area scaled back.

- **Bits inside the range**: 0 of 458752 areas differ from today's formula at 2^-255 .. 2^250, and 0 of 131072 differ from
  the scaled unit area at 2^-500 and 2^500 (stage `formula`). Time: 15.9 and 12.3 ns per call against 16.6 and 12.4 ns, no
  difference above the noise.
- **The sweep with the patch applied** (`results/experiment_area_from_lengths_e875d747.txt`):

| Class | Exact today | Exact with the patch | Outside, with the patch |
|---|---|---|---|
| `FlipToDelaunay` | 2^-269 .. 2^255 | 2^-534 .. 2^510 | other flips, no status |
| `ExtractCommonSubdivision` | 2^-256 .. 2^255 | 2^-535 .. 2^510 (one exponent 1e-15 off) | `Inconsistent`: no false success left |
| `DelaunayRefine` | 2^-254 .. 2^257 | 2^-507 .. 2^512 | `Success` with another result at 2^-532 .. 2^-508 and below 2^-536 |
| `MeshGeodesicPath` | 2^-254 .. 2^256 | 2^-507 .. 2^510 | `Success` with another length at 2^-531 .. 2^-508 |
| `MeshGeodesicDistance` | 2^-254 .. 2^257 | 2^-266 .. 2^510 | `false` at 2^-513 .. 2^-267 (better), `true` with wrong values at 2^-535 .. 2^-514 |
| `VectorHeatMethod.TransportVector` | 2^-253 .. 2^242 | 2^-255 .. 2^242 | **worse**: wrong vectors at 2^-511 .. 2^-256 and NaN at 2^256 .. 2^490, where it threw before |
| `PolyhedralGeodesicDistance`, `QuadRemesher` | | unchanged (their own expressions) | |

Reading: one formula fixed shows the next limit (squared lengths at 2^+-512, the absolute thresholds of the solvers), with
the same kind of false success in a new band. Option (b) is therefore a list of sites per class (at least the five of 3.1,
lines 312 and 385 of `VectorHeatMethod`, 388, 611, 675 and 724 of `MeshGeodesicDistance`, 403 and 436 of
`IntrinsicCommonSubdivision`), each with its own review, and it still ends in a range that needs a check. And the area
patch **must not land alone**: it turns an exception of `VectorHeatMethod` into wrong values.

## 6. `DualContouring`

### 6.1 Root causes

`DualContouring` has two scales, the geometry (`Bounds`, `CellSize`) and the values of `Implicit`. Stage `dcbig` separates
them (cell 0.25, `Simplify` off unless stated):

| What is scaled by 2^e | Result | Root |
|---|---|---|
| the values, e <= -52 (default: empty; `Simplify` off: 2408 triangles, moved vertices) | another | `exact_gradient(pe).Normalized` (`DualContouring.cs` 326; also 316, 328): `Vector3d.Normalized` returns zero below the absolute `MathUtil.Epsilon` = 2.2e-16. The gradient is values over geometry |
| the geometry, values of order 1, e >= 100 (tested from 2^100; the rule predicts it from about 2^52) | 295 of 296 vertices moved; default: empty | the same line: the gradient is 2^-e |
| the values, e <= -45 with `ExactHermite` off; field and geometry together, e <= -48 | other quad diagonals, the same vertices | `Math.Abs(fb - fa) > MathUtil.Epsilon` (311, and 2413 for the face vertices): an absolute test on a difference of values |
| the values, e <= -150 | empty | the grid stores `float` (792, 1400, 1407): values below 1.4e-45 are 0 |
| the values, e >= 130 with `ExactHermite` off | all vertices moved | the float grid again: `clamp_value` (827, 1407) clamps at 2^127, the interpolated crossing is then 0.5 |
| field and geometry, e >= 262, `Simplify` on | 168 triangles in place of 132, closed, the same surface | `n0.Dot(n1) > 0`, `AdaptiveDualContouring.cs` 428 (section 6.3) |
| field and geometry or the values alone, e >= 513 | vertices moved by up to 0.13 cells; default: empty from the values alone | squared lengths and squared gradients overflow (the general limit of double geometry) |

Measured ranges on the distance field: under a value scale the default is exact from 2^-51 to 2^500; under a uniform scale
from 2^-47 to 2^261.

### 6.2 What a value prescaling does and does not do

The wrapper `DcStage.Prescaled` scales the geometry by the power of two of the largest coordinate of `Bounds` and the values
(and `IsoValue`) by the power of two of the largest `|f - IsoValue|` on the grid nodes.

- **The relation of #1376 holds**: exact for the values times 2^e at every tested e from -1020 to 1020, for the default,
  `Simplify` off and `ExactHermite` off; exact under the uniform scale from 2^-1000 to 2^1020.
- **No change at ordinary scale** on the five fields tried (sphere, 1000 times the sphere, box, torus, a quartic), three
  option sets each: the wrapper's mesh is the plain mesh bit for bit. This is a sample, not a proof: a field with
  `|fb - fa|` or a gradient within a factor 2^k of 2.2e-16 somewhere would change.
- **It does not repair a field that is small only near its surface.** exp(-40 r^2), contoured at a level L (the gradient at
  the surface is 80 r L):

| Level | Gradient at the surface | `MarchingCubes` (bisection) volume | `DualContouring` | with the values prescaled |
|---|---|---|---|---|
| 1e-10 | 6.1e-9 | 1.8057 | 276 triangles, 1.8232 | the same |
| 1e-16 | 7.7e-15 | 3.6719 | 540 triangles, 3.7064 | the same |
| 1e-20 | 8.6e-19 | 5.1394 | **0 triangles** | **0 triangles** |
| 1e-30 | 1.1e-28 | 9.4647 | 0 triangles | 0 triangles |

  The largest value of this field is 1, so a scale taken from the maximum changes nothing. Only a relative rule helps: the
  normal from the gradient divided by its largest component first (option b), and `|fb - fa|` compared with
  `max(|fa|, |fb|)` or simply with 0.
- **Cost**: the geometry scaling is free (28.6 against 27.0 ms and 125 against 141 ms, inside the noise); the value scan of
  the wrapper costs one more evaluation per grid node, which a version inside the class does not need (the grid is sampled
  anyway).

### 6.3 The difference at 2^300 and 2^400

The counters of `AdaptiveDualContouring` on the distance field, cell 0.25:

| Scale | Clusters | Merged vertices | Check rounds | Rejected clusters | Result | Closed | Volume / s^3 |
|---|---|---|---|---|---|---|---|
| 1 .. 2^261 | 57 | 285 | 1 | 0 | 68 vertices, 132 triangles | yes | 4.187532 |
| 2^262, 2^300, 2^400 | 50 | 260 | 2 | 7 | 86 vertices, 168 triangles | yes | 4.196413 |

The check that rejects a cluster whose triangle "turned over against the uniform contour" computes
`n0.Dot(n1)` of two unnormalized cross products. Each normal is a squared length, the dot product a fourth power; from
2^262 some products are infinite with opposite signs, the sum is NaN, and `!(NaN > 0)` rejects the cluster. So above 2^262
the contour is valid and less simplified. This is not a false success in the sense of 2.5a (the result is a correct contour),
but it is another result, and one line removes it: compare the signs of the dot product of the two normals scaled by a
power of two each, or test `n0.Dot(n1 / |n1|)` after a scale-free normalization.

### 6.4 Option (b) prototyped (follow-up, 2026-11-22)

After the owner's "measure first" for decision 2. The prototype is `experiments/dual_contouring_relative.patch` (33 lines in
`DualContouring.cs`, one in `AdaptiveDualContouring.cs`), applied in a scratch worktree only:

- **The normal** (lines 326 and 328; the `HermiteF` normal at 316 is left alone): `Vector3d.Normalized` as today where the
  length is above 2.2e-16 and finite, so the bits stay; otherwise the gradient times the power of two that brings its largest
  component into [1, 2), then normalized.
- **The crossing tests** (311, 2413): `fb != fa` in place of `Math.Abs(fb - fa) > MathUtil.Epsilon`.
- **The turned-over test** (`AdaptiveDualContouring.cs` 428): the dot product of the two normals, each scaled by its power of
  two.
- Not in the prototype: the `float` grid (values below 1.4e-45 or above 2^127 stay a limit).

**Existing tests: none changes.** `tests/geometry3Sharp.Tests` (net10.0, `Category!=Nightly`): 17588 passed, 0 failed, 2
skipped, on master and with the patch; the 17547 distinct test names have the same outcome one by one. Of these, 777 tests
in 37 classes are the contouring, offset, solidify, terrain-contouring and lattice classes, among them the pinned hashes of
`ContouringQualityLongHashTests` (5), `DualContouringSimplifyDefaultTests` (22), `DualContouringExactHermiteTests` (16),
`AdaptiveDualContouringTests` (42), `MeshOffsetTests` (21) and `TpmsLatticeTests` (120). `tests/g3cli.Tests`: 674 passed
with the patch. Not run: the Nightly tier, the Clipper2 tests, the benchmark goldens (`Meshing2Benchmark`, no contouring).

**The probe's cases** (stage `dchash`, 99 cases as "counts, volume, hash", run on both builds;
`results/dchash_master_e875d747.txt` against `results/dchash_dcbig_experiment_dual_contouring_relative_e875d747.txt`):
64 are identical, 35 change, and every one of the 35 is a case that is wrong or degraded today.

| Group | Cases | Result with the patch |
|---|---|---|
| ordinary scale: 7 fields x 3 option sets | 21 | identical, bit for bit |
| the other cases that are right today (value, geometry and uniform scales inside today's range, the levels 1e-5 to 1e-16) | 43 | identical, bit for bit |
| values x 2^-52 .. 2^-100, default | 3 | empty today; now the unit mesh, bit for bit (504 triangles, volume 4.186889) |
| values x 2^-140, default | 1 | empty today; now 504 triangles, volume 4.187110 (the float grid is subnormal there: not the unit bits) |
| geometry x 2^60 .. 2^200 with values of order 1, default | 3 | empty today; now the unit mesh |
| field and geometry x 2^262, 2^300, 2^400, default | 3 | 542 triangles today; now the unit mesh (the cluster counters are those of the unit run: 57 clusters, 0 rejected) |
| the same value and geometry scales with `Simplify` off | 7 | the vertices move back: volume about 4.2036 (the unit run's) in place of about 4.1262 |
| `ExactHermite` off, values x 2^-48 .. 2^-140 and field and geometry x 2^-48 .. 2^-100 | 8 | volume 4.1957 (the unit run's) in place of 4.12 to 4.14 |
| exp(-40 r^2), levels 1e-20, 1e-30, 1e-40 | 6 | next table |
| exp(-40 r^2), `ExactHermite` off, levels 1e-16 to 1e-40 | 4 | next table: **worse** |

exp(-40 r^2) (cell 0.125):

| Level | Analytic volume | Default today | Default with the patch | `Simplify` off today / patch | `ExactHermite` off today / patch |
|---|---|---|---|---|---|
| 1e-16 | 3.7026 | 3.7064 | the same | the same | 3.8918 / 4.1146 |
| 1e-20 | 5.1745 | **0 triangles** | 540 triangles, **5.1714** | 5.1056 / 5.1902 | 5.0102 / 5.6477 |
| 1e-30 | 9.5061 | 0 triangles | 540 triangles, 9.4534 | 9.4244 / 9.5251 | 9.5768 / 10.3674 |
| 1e-40 | 14.6357 | 0 triangles | 888 triangles, 14.6084 | 14.5408 / 14.6581 | 14.5456 / 15.7067 |

So the level 1e-20 comes out as a closed mesh of 540 triangles whose volume is 0.06 % below the analytic one, where today
the mesh is empty. The one regression is `ExactHermite = false` on this field: there the crossing is the linear
interpolation of two grid values of an exponential, which is far off (the values differ by orders of magnitude along one
edge); today's `|fb - fa| > 2.2e-16` test puts those crossings at the middle of the edge, which happens to be nearer
(3 % against 9 % at 1e-20). `fb != fa` is what the class comment describes; if this case matters, the crossing test can stay
absolute for `ExactHermite = false` and the patch then changes only the normal there. The default (`ExactHermite = true`,
bisection) does not use that crossing.

**Reading.** On the evidence here the change is output-neutral for ordinary fields (no test, no pinned hash, none of 21
ordinary cases changes) and repairs #1376 except for the float grid. It is a sample: a field with a gradient between 0 and
2.2e-16 on some edges of an otherwise ordinary contour would change there, from a zero normal to the true one.

## 7. Prior art

How others handle the magnitude range (papers and documentation only; no code was read; details and what could not be
opened in [sources/tools-and-verification.md](../sources/tools-and-verification.md#magnitude-robustness-study)):

- **A documented range without a check.** Shewchuk 1997 sets overflow and underflow aside and states that his predicates do
  not overflow or underflow for inputs with exponents in [-142, 201]; he names the exponent range as the main limit of the
  expansion approach. This is option (d).
- **A check that falls back.** Bartels, Fisikopoulos and Weiser 2022 show that a filter that ignores underflow can certify
  a wrong sign (2D orientation near 2^-800 returns 0 for points that are not collinear) and build filters with range checks
  or an underflow guard at a measured cost of about 4 %. This is option (c), with an exact stage behind it.
- **Prescaling by a power of the radix, which is exact.** The reference BLAS `DNRM2` (Blue 1978, Anderson 2017) keeps
  separate accumulators for large and small entries, scaled by powers of the radix so that no square overflows or
  underflows. LAPACK's `xGEEQUB` restricts its scale factors to powers of the radix: "barring over- and underflow, scaling
  by these factors introduces no additional rounding errors". Kahan's note on the area of a triangle, the source of the
  formula in 3.1, sets over- and underflow aside "since we can evade them by scaling the data". This is option (a).
- **A fixed integer grid.** Snap rounding and the exact-computation literature assume coordinates on an integer lattice of a
  fixed size (Devillers and Guigue 2005, summarizing Greene and Yao, Hobby, Guibas and Marimont; Hobby 1999 itself was not
  opened). The range is the caller's task there.
- **Parameters relative to the mesh's own length.** The heat method chooses t = m h^2 with h the mean edge length because
  "h^2 Delta is invariant with respect to scale" (Crane, Weischedel, Wardetzky 2013). This is option (b) for a parameter;
  the library follows it (`TimeFactor`), the absolute thresholds of section 3 are where it does not.
- **In this library**: `MeshRobustRepair` works in an exactly normalized frame
  ([design/ROBUST_REPAIR_SCALE_FREE.md](../design/ROBUST_REPAIR_SCALE_FREE.md), option A, built), `ContourLoft` evaluates
  its costs on copies scaled with `ExactPoint3.ScaleB` and is exact from 2^-320 to 2^200 in the hunt, `Reducer` uses a
  power-of-two frame for its quadrics. Option (a) is the library's own established answer.

## 8. Options and recommendation

The same four options for every class. "Bits" says whether the tests' 2^k invariance stays bit for bit inside today's range.

| Option | What it is | Bits inside today's range | Cost | Ends false success |
|---|---|---|---|---|
| (a) prescale on entry | coordinates times 2^-k, k from the largest coordinate; results scaled back | unchanged wherever the class is 2^k exact today (measured: all but `TracePath`) | one pass over the vertices (4.6 ms at 54152 vertices); one mechanism, one test per class (the sweep of this note as a theory) | yes, 2^-1010 .. 2^1020 measured |
| (b) scale-free formulas | each expression rewritten (a power-of-two normalization per call, relative thresholds) | unchanged for the area formula (measured); a relative threshold changes results near the old absolute one | no time (measured for the area); at least 13 sites in 7 files, each reviewed, and the next layer behind each | only together with (c) (section 5) |
| (c) checked range | an early test, eg the longest and shortest edge inside 2^-250 .. 2^250, and the class's failure status | unchanged | one pass; **a new refusal is an owner decision**; classes without a status (`FlipToDelaunay`, `VectorHeatMethod`, the `IntrinsicTriangulation` constructor) need an exception | yes, by refusing |
| (d) document only | a range in the class comment and a row in the 2.5a table | unchanged | none | no |

Per class, the cheapest option that keeps the bits and ends the false success:

| Class | Root | Option | Cost | Issue |
|---|---|---|---|---|
| `IntrinsicTriangulation` (flips, `DelaunayRefine`, tracing) | 909 | (a) in the constructor: lengths stored times 2^-k, the accessors in caller units | one `ScaleB` per accessor call; medium (the file is touched by #1379) | #1377, #1352 (flips) |
| `ExtractCommonSubdivision` | 909; 403 to 405, 436 to 439 | (a) through the triangulation, and the two checks made NaN-safe (`!(err <= 1e-6)`) with a zero test in place of the `1e-300` floor | small | #1352 |
| `MeshGeodesicPath`, `MeshGeodesicBezier` | 909 | (a) through the triangulation; `Path` and `Length` scaled back | none beyond the triangulation | #1377 |
| `VectorHeatMethod` | 909; 312, 385 | (a) through the triangulation: the solutions then have unit scale, so the absolute `1e-250` is harmless | none beyond the triangulation | #1377 |
| `MeshGeodesicDistance` with `TracePath` | 909, 210; 388; 611, 675, 724 | (a) at the class entry (it also has a mode without the triangulation, and traces on the mesh): a scaled copy of the positions, `Distance` and the path scaled back | one copy, about 0.5 % | #1377; the `TracePath` finding |
| `PolyhedralGeodesicDistance` | 128, 192 | (a) in the copy to `vx`, `vy`, `vz` it already makes; `Distance` and the paths scaled back | no extra pass | #1377 |
| `QuadRemesher` (`CrossField`, `PositionField`) | `MeshParameterization.cs` 243 | (a) at the entry of `QuadRemesher`; `TargetEdgeLength`, `SizeFunction` arguments and `Result` scaled | one copy, about 0.3 % | #1377 |
| `DualContouring`, value scale | 316, 326, 328; 311, 2413; 792, 1400 | (b) for the normal and the two `|fb - fa|` tests, and a power-of-two scale of the stored values taken from the sampled grid | small; output changes only where the gradient or a difference is below 2.2e-16 today | #1376 |
| `DualContouring`, above 2^262 | `AdaptiveDualContouring.cs` 428 | (b): the sign of the dot product of normalized normals | one line | #1377 (its last two lines) |
| every class above | | (d): a row in the 2.5a table and the range in the class comment, after the fix | docs | #1377 |
| disparity inside one input (parts 2^200 apart) | | (c) | owner decision | -- |

**Recommendation: (a) as the rule, at the lowest class that owns the lengths.** Reasons: it is exact, so the 2^k tests stay
bit for bit; it was measured to end every false success of the sweep over the whole double range, including the second
layer that (b) exposes and the `TracePath` finding; it costs one pass; it is what `MeshRobustRepair`, `ContourLoft` and
`Reducer` already do; and it is one mechanism to review, not thirteen sites. (b) is needed only in `DualContouring`, where the
problem is a ratio of two scales and no single prescaling can remove it. (c) stays as the backstop for what no scaling
represents (disparity, subnormal results), with the class's existing failure status. (d) follows for every class.

A helper would keep it uniform: one internal function that returns the exponent of the largest coordinate of a mesh, and the
`ExactPoint3.ScaleB` calls at entry and exit. The test per class is the sweep of this note: the scaled result bit for bit at
2^+-280, 2^+-600 and 2^+-1000, failing on master, plus the standing `Similarity.All()` test.

### What the fix of #1352 should do

1. **Not the area formula alone.** The experiment of section 5 shows it: `ExtractCommonSubdivision` and the flips become
   exact to 2^+-510, but `VectorHeatMethod` returns wrong vectors and NaN where it threw before.
2. **Independent of every decision below, and safe to land first**: make the two area checks NaN-safe
   (`IntrinsicCommonSubdivision.cs` 405 and 439: `!(err <= 1e-6)` in place of `err > 1e-6`; the maximum of the second
   check is NaN as soon as one error is). Measured with a local patch (`experiments/common_subdivision_nan_safe.patch`, not
   applied): the false `Success` of 2^256 .. 2^511 becomes `Inconsistent`, and the answers from 2^-1070 to 2^255 are
   unchanged. The `1e-300` floor (`Math.Max(area, 1e-300)`) should become a test for a zero area; that part was not
   measured (on the grid of the sweep the small scales already answer `Inconsistent`, from the flips).
3. **The range itself**: by the prescaling in the `IntrinsicTriangulation` constructor (recommended, with #1377), or, if the
   owner prefers a refusal, `Inconsistent` from `ExtractCommonSubdivision` when the longest edge is outside 2^-250 .. 2^250.
   `FlipToDelaunay` has no status, so only the prescaling helps the 57 against 25 points of the comment on #1352.
4. **Coordination**: `IntrinsicTriangulation.cs` is touched by #1379; item 2 is in `IntrinsicCommonSubdivision.cs` only.

## 9. Owner decisions

1. **Option (a) for the mesh classes.** It changes output inside today's range only where a class is not 2^k exact today;
   measured, that is `MeshGeodesicDistance.TracePath` outside 2^-31 .. 2^50 (another number of path points). Go or not, and
   whether `IntrinsicTriangulation` may store scaled lengths behind accessors in caller units.
2. **`DualContouring`, relative normal and crossing tests (b).** It changes output where the gradient or a node difference
   is below 2.2e-16 today: fields that give an empty or a moved mesh now. Measured since (section 6.4): no existing test changes, 21 ordinary cases are bit
   identical, and the only regression is `ExactHermite = false` on exp(-40 r^2).
3. **A refusal for what scaling cannot represent** (disparity above about 2^200 inside one mesh; classes without a status):
   which status or exception, or documented as a limit.
4. **`TracePath` inside the range**: a new issue of its own, or part of #1377 (it is repaired by (a) either way).
5. **The simplification above 2^262**: a one-line fix with #1376, or documented (the result is valid).

How others solve it: section 7 (a documented range: Shewchuk; a check with a fallback: Bartels et al.; radix-power
prescaling: BLAS, LAPACK, Kahan; relative parameters: Crane et al.).

## 10. What was not done

- No library change and no test: the probe is the evidence. The tests come with the fixes ("one per class at e = +-280,
  failing on master", #1377).
- One input per class. The ranges move with the edge lengths (section 3.1); a mesh with edges of very different length has a
  narrower exact range than the spheres.
- `TracePath`'s other results were not compared geometrically (section 3.3).
- The cost A/B has a spread of up to 30 % between two runs of the same call; only the copy time is a resolved number.
- Other classes that use `Cross(...).Length` or a squared area were not swept (the remeshers, the hole fillers, the UV
  atlas, the deformers): the 2.5a table gives 2^-200 .. 2^200 for those that have a row, which is inside the fourth-power
  limit.
- The relative-normal change of `DualContouring` is prototyped as a patch file (section 6.4), without the float grid and
  without tests of its own.
