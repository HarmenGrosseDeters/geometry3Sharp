# Spherical Fibonacci grids as procedural geometry fixtures

**Decision.** Build a small benchmark fixture around the existing `SphericalFibonacciPointSet.Point(i)` after owner approval. Its value is a repeatable input with independently measurable separation, coverage and almost ambiguous local connectivity. The study covers the spherical construction in plan #1424.

## Construction and prior art

Swinbank and Purser (2006) sought approximately isotropic resolution for atmospheric stencils, including at high latitudes. Their odd-sized grid places one sample at each of M equally spaced midpoint heights. Successive longitudes advance by 2π/φ, where φ is the golden ratio. Consecutive Fibonacci index offsets trace intersecting spiral families. Hardin, Michaels and Saff describe the centered indexing and distinguish this grid from the construction restricted to Fibonacci cardinalities. [Original paper](https://doi.org/10.1256/qj.05.227) · [Comparison paper](https://arxiv.org/abs/1607.04590)

The library indexes north to south with z = 1 − (2i+1)/M and longitude 2π frac(i/φ). For odd M this agrees with centered indexing up to reflection and rotation. On a unit sphere a full band has area 2π Δz, hence each height strip has area exactly 4π/M. Voronoi cells have a different boundary. Equal strip area alone gives no bound on the largest empty cap.

## Measurements

Save the Python listing below as `probe.py` and run `python3 probe.py` with NumPy and SciPy installed. The independent binary64 probe evaluates the forward formula. It computes every nearest-neighbour chord and the covering chord radius. For the latter, each triangular hull face gives an empty spherical cap centered on its unit outward normal. A face plane at distance h from the origin gives chord radius √(2−2h). The largest such cap measures the worst hole over the whole sphere. Divide lengths by L = √(4π/M) to compare resolutions.

| M | Minimum nearest / L | Maximum nearest / L | Cover / L | Smallest adjacent-face plane gap, sphere units | Worst absolute nearest-distance drift, original units |
|---:|---:|---:|---:|---:|---:|
| 65 | .87155 | .96562 | .76666 | 4.08e−3 | 6.37e−7 |
| 257 | .87206 | .97998 | .76887 | 2.23e−4 | 7.95e−7 |
| 1025 | .87221 | .98306 | .76941 | 2.06e−5 | 7.99e−7 |
| 4097 | .87224 | .99636 | .76954 | 2.96e−7 | 8.25e−7 |

Drift compares each site's identity distance with its transformed distance divided by scale, across the eight transforms specified by the C# harness's `Similarity.All()`, reproduced in Python. At millimetre scale, adding the UTM northing places points near 5.7 million before subtraction. Use the harness's resolution-aware tolerance when comparing lengths. This probe measures the construction independently of the C# operations.

## Engineering implications

- **Delaunay.** At M=4097 the zero-based sites 1185, 1240, 1274 and 1329 form adjacent hull triangles. The fourth site misses the first triangle's plane by 2.96e−7 radii. Coplanarity on the sphere would put all four on one circle. For a planar predicate fixture, stereographically project the quartet from a pole away from it. The C# run below verifies the resulting diagonal. General 3D tetrahedral Delaunay has a separate, exact cosphericity degeneracy in the ideal input.
- **Remeshing.** The hull provides a closed procedural triangular input. After a remesh with spherical projection, measure radial error, manifold closure and coverage. A particular triangulation is too restrictive as an acceptance criterion near a diagonal tie.
- **CVT.** Use the measured covering radius as a baseline for the largest hole during relaxation. Measure the area-weighted squared-distance energy separately to assess progress toward centroidality. A spacing measurement alone cannot certify a CVT.
- **Cost and guarantee.** Site generation takes O(M) time and constant auxiliary space when streamed. A proposed first fixture uses 257 sites for spacing and the recorded quartet for predicate stress. Allow roughly one engineering day for an A/B against the existing sampling fixtures, including timing in C#. The strip-area identity is analytic. The spacing and cover ranges above are finite measurements for four sizes. Operator performance, bitwise cross-platform repeatability and all-size bounds remain open.


## C# operator run

On Linux x64, .NET SDK 10.0.100, library revision `3c4d93ac`, the probe called `Point` and `NearestPoint` for every site. It independently found each nearest other site by exhaustive pairwise comparison. Both normalized spacing extrema agreed with the Python table to its displayed precision. The inverse mapping returned the original index for all 5,444 sites (65/65, 257/257, 1025/1025 and 4097/4097). This round-trip exercises queries exactly at the generated sites.

For north-pole stereographic projection, `DelaunayMesh2.Compute` returned two triangles sharing the edge **1185–1329**. An independent rational determinant on the round-tripped binary64 coordinates gave orientation −0.0086046652522 for (1185, 1240, 1274) and in-circle determinant −1.2755165072e−8 with 1329 as the query (rounded here). Matching signs put 1329 inside that circumcircle, ruling out the competing diagonal 1240–1274.

## Reproduction

For the C# listing, create a temporary net10.0 console project outside the checkout, reference `geometry3Sharp_netstandard.csproj`, replace Program.cs and run `dotnet run -c Release`. The final four lines are the projected coordinates in site order.

### C# operator probe

```csharp
using System;
using System.Linq;
using g3;

foreach (int n in new[] {65,257,1025,4097}) {
    var grid = new SphericalFibonacciPointSet(n);
    var points = Enumerable.Range(0,n).Select(grid.Point).ToArray();
    var distances = Enumerable.Repeat(double.PositiveInfinity,n).ToArray();
    for (int i=0;i<n;i++)
        for (int j=0;j<i;j++) {
            double d = points[i].Distance(points[j]);
            distances[i] = Math.Min(distances[i],d);
            distances[j] = Math.Min(distances[j],d);
        }
    int hits = Enumerable.Range(0,n).Count(i => grid.NearestPoint(points[i],true)==i);
    double unit = Math.Sqrt(4*Math.PI/n);
    Console.WriteLine($"{n} {distances.Min()/unit:F7} {distances.Max()/unit:F7} {hits}");
}
var sphere = new SphericalFibonacciPointSet(4097);
int[] ids = {1185,1240,1274,1329};
var plane = ids.Select(sphere.Point).Select(p=>new Vector2d(p.x/(1-p.z),p.y/(1-p.z))).ToArray();
var result = new DelaunayMesh2().Compute(plane);
if (result.Status != Triangulation2Status.Success || result.Mesh.TriangleCount != 2)
    throw new Exception("Quartet triangulation failed");
foreach (var t in result.Mesh.Triangles) {
    var indices = new[]{t.a,t.b,t.c}.Select(v=>ids[Array.IndexOf(result.Mesh.InputToVertex.ToArray(),v)]);
    Console.WriteLine(string.Join(" ",indices));
}
foreach (var p in plane) Console.WriteLine($"{p.x:R} {p.y:R}");
```

### Independent geometry measurements

```python
"""Run with Python 3, NumPy and SciPy. All inputs are procedural."""
import numpy as np
from scipy.spatial import ConvexHull, cKDTree
from scipy.spatial.transform import Rotation


def nearest(points):
    return cKDTree(points).query(points, k=2)[0][:, 1]


identity = np.eye(3)
utm = np.array([632117.25, 5712043.5, 113.75])
rotated = Rotation.from_euler("ZX", [31, 17], degrees=True).as_matrix()
yaw = Rotation.from_euler("y", 23, degrees=True).as_matrix()
transforms = [(1, identity, 0), (1, identity, utm), (1/1024, identity, 0),
              (1024, identity, 0), (.001, identity, 0), (1000, identity, 0),
              (1, rotated, 0), (.001, yaw, utm)]

for count in (65, 257, 1025, 4097):
    index = np.arange(count)
    z = 1 - (2 * index + 1) / count
    angle = 2 * np.pi * np.remainder(index / ((1 + np.sqrt(5)) / 2), 1)
    radius = np.sqrt(1 - z*z)
    points = np.column_stack((radius*np.cos(angle), radius*np.sin(angle), z))
    spacing = nearest(points)
    unit = np.sqrt(4*np.pi/count)
    hull = ConvexHull(points)
    # Each outward support normal is a spherical Voronoi vertex.
    cover = np.sqrt(2 * (1 + hull.equations[:, 3].max()))
    # Distance of the neighbouring triangle's opposite vertex from this plane.
    gaps = []
    for face, adjacent in enumerate(hull.neighbors):
        for other in adjacent:
            if other <= face:
                continue
            opposite, = set(hull.simplices[other]) - set(hull.simplices[face])
            normal = hull.equations[face]
            gaps.append((abs(points[opposite] @ normal[:3] + normal[3]),
                         sorted(set(hull.simplices[face]) | set(hull.simplices[other]))))
    errors = [np.max(abs(nearest(scale*(points @ rotation.T)+shift)/scale-spacing))
              for scale, rotation, shift in transforms]
    print(count, *(round(x/unit, 5) for x in (min(spacing), max(spacing), cover)),
          min(gaps), max(errors))
```

### Exact determinant check

Save the C# console output as `run.log` before running this listing.

```python
from fractions import Fraction
from itertools import permutations
from pathlib import Path

lines = Path('run.log').read_text().splitlines()
points = [[Fraction(float(v)) for v in line.split()] for line in lines[-4:]]
rows = [[x, y, x*x + y*y, 1] for x, y in points]

def determinant(rows):
    from math import prod
    total = 0
    for p in permutations(range(len(rows))):
        inversions = sum(a > b for i,a in enumerate(p) for b in p[i+1:])
        total += (-1)**inversions * prod(row[j] for row,j in zip(rows,p))
    return total

orient = determinant([[x,y,1] for x,y in points[:3]])
incircle = determinant(rows)
print(float(orient),float(incircle), "inside", orient*incircle>0)
```
