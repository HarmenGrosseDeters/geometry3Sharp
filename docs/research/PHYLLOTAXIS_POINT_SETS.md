# Vogel discs as deterministic meshing inputs

**Decision after the production check: keep the four-point pair as a diagnostic. Park a permanent CI fixture.** Vogel's planar model offers both a regular spacing baseline and a way to isolate a difficult edge decision using four indexed points. It complements the previous spherical area study. The other proposed stress families, such as branching tube unions, need multiple operators to agree before a discrepancy can be localized.

## Construction and prior art

Vogel describes visible spiral families and where their order changes. For this probe, sample indices from 0 through N−1 at angle iπ(3−√5) and radius √(i/N) on a unit disc. The exact golden angle avoids the cumulative drift of a rounded degree constant. This is a planar Fermat spiral. Each successive squared radius increases by 1/N, so the corresponding concentric annuli have exactly equal area π/N, including the last annulus up to the unit boundary. This identity says nothing about the area of individual Voronoi cells. Ridley's subsequent packing study motivates measuring local separation independently. Primary references are in the [source register](../sources/meshing-and-repair.md#phyllotaxis).

## Measurements

Save the Python listing below as `probe.py` and run `python probe.py` with NumPy and SciPy installed. The offline probe generates all coordinates in binary64. For each site it measures the distance to its nearest other site, then multiplies by √N to compare densities. The transform column gives the maximum pointwise relative change in distance after dividing out scale, over the eight maps specified by the repository's Similarity.All(). Distances use the full 3D image of the planar disc under tilt.

| N | Min | Median | Max | Worst transform error | Smallest normalized incircle determinant |
|---:|---:|---:|---:|---:|---:|
| 64 | 1 | 1.6785 | 1.7826 | 4.01e−6 | 6.23e−3 |
| 256 | 1 | 1.6782 | 1.7325 | 8.08e−6 | 7.98e−4 |
| 1024 | 1 | 1.6951 | 1.7640 | 1.64e−5 | 1.24e−4 |
| 4096 | 1 | 1.6880 | 1.7625 | 3.42e−5 | 3.41e−5 |

The center and first off-center point are exactly 1/√N apart in real arithmetic. The measured minimum attained this value at all four sizes. The table provides empirical extrema for those sizes only. Translation to large coordinates after millimetre scaling consumes significant digits in differences. A single machine-epsilon tolerance would reject valid transformed inputs.

For each interior edge of SciPy's planar Delaunay triangulation, take the union of its two adjacent triangles, translate the fourth site to the origin, divide by the quadruple diameter, and evaluate the absolute lifted 3×3 determinant. Zero indicates cocircularity for a noncollinear triple. At N=4096 the smallest value belonged to indices 2165, 2254, 2309 and 2398. The natural case is close enough to identify a candidate edge but still far from roundoff at unit scale.

For a stronger paired fixture, keep the first three points fixed and project the fourth radially onto their circumcircle. Multiply its radius by 1−10⁻¹² or 1+10⁻¹² before rounding the coordinates. Exact rational evaluation of the resulting binary64 coordinates gave determinant signs +1 and −1 respectively in the listed order. This pair supplies a two-sided correctness oracle for a four-site triangulation. Recompute the reference sign after a rounded transform because the perturbation can be smaller than a coordinate ulp.

## Integration decision and cost

Use the four-site pair for a local edge-choice check and the 256-site disc for a full DelaunayMesh2 run. Check point retention, orientation, Euler's triangle count 2N−2−h (h hull vertices) and local empty-circle signs. Use Similarity.All2D() for planar triangulations. The study used an independent triangulation to select candidate quadruples and measured input geometry only. The production check below evaluates the proposed cases. Generation takes O(N) time and storage. Estimated integration effort is half a day for the two fixtures, exact sign checks and timing against an ordinary-case baseline.

For Remesher, the disc gives an open triangulated patch with a separately identifiable boundary. For CVT, the same points provide a starting configuration whose clipped-cell energy can be measured before and after relaxation. Park both integrations until an experiment controls the boundary and measures the relevant output. Equal-area annuli alone cannot certify remeshing quality or CVT convergence.

## Production check and controls

On plan revision 0878d4f, a temporary .NET 10 Release driver called `new DelaunayMesh2().Compute(points)` on each array, checked `Mesh.IsValid(out reason)` and exported sorted input-index triples. Input coordinates were passed with 17 significant decimal digits. Both four-point cases returned Success and two triangles. In the order (2165, 2254, 2309, projected 2398), the shared diagonal was 0–3 for the inner sample and 1–2 for the outer sample, as required by the exact sign. A fixed-diagonal implementation would fail one member of this pair.

The N=256 Vogel disc produced 489 triangles. An ordinary control from `np.random.default_rng(42).random((256,2))` had 495. Both retained every site and both unordered triangle sets matched SciPy Delaunay exactly. Compare with `{tuple(sorted(t)) for t in Delaunay(points).simplices}`. Existing DelaunayMesh2Tests already run 600 procedurally generated inputs against the meshing oracle and check exact ties and canonical diagonals. The Vogel pair supplies a compact, readable edge-flip diagnostic. This experiment found no defect and established no additional fault detection for the full disc over existing coverage. Keep the pair in the research note and defer adding CI cases until a perturbation or real regression distinguishes them.

## Reproduction of point generation and the exact sign oracle

```python
"""Offline input study. Run with Python, NumPy and SciPy."""
import numpy as np
from scipy.spatial import cKDTree, Delaunay
from scipy.spatial.transform import Rotation


def spacing(points):
    return cKDTree(points).query(points, k=2)[0][:, 1]


def closest_circle(points):
    mesh = Delaunay(points)
    best = (float("inf"), ())
    for face, neighbors in enumerate(mesh.neighbors):
        for neighbor in neighbors:
            if neighbor <= face:
                continue
            ids = np.union1d(mesh.simplices[face], mesh.simplices[neighbor])
            quad = points[ids]
            delta = quad[:3] - quad[3]
            diameter = np.linalg.norm(quad[:, None] - quad, axis=2).max()
            delta /= diameter
            lifted = np.column_stack((delta, (delta ** 2).sum(axis=1)))
            best = min(best, (abs(np.linalg.det(lifted)), tuple(map(int, ids))))
    return best


angle = np.pi * (3 - np.sqrt(5))
utm = np.array([632117.25, 5712043.5, 113.75])
transforms = [(s, np.eye(3), 0) for s in [1, 1/1024, 1024, .001, 1000]]
tilt = Rotation.from_euler("xz", [17, 31], degrees=True).as_matrix()
transforms.extend([(1, np.eye(3), utm), (1, tilt, 0),
                   (.001, Rotation.from_euler("y", 23, degrees=True).as_matrix(), utm)])

for n in [64, 256, 1024, 4096]:
    i = np.arange(n)
    xy = np.column_stack((np.cos(angle*i), np.sin(angle*i))) * np.sqrt(i/n)[:, None]
    xyz = np.column_stack((xy, np.zeros(n)))
    d = spacing(xyz)
    errors = [np.max(abs(spacing(s * (xyz @ rot.T) + t) / s / d - 1))
              for s, rot, t in transforms]
    print(n, np.sqrt(n)*d.min(), np.sqrt(n)*np.median(d), np.sqrt(n)*d.max(),
          max(errors), closest_circle(xy))

# Move the last sample of the closest natural quadruple across its circumcircle.
# Fraction evaluates the sign for the actual binary64 inputs.
from fractions import Fraction


def exact_sign(a, b, c, q):
    rows = []
    for point in (a, b, c):
        x, y = [Fraction(float(u)) - Fraction(float(v)) for u, v in zip(point, q)]
        rows.append((x, y, x*x + y*y))
    value = sum(rows[0][j] * (rows[1][(j+1)%3] * rows[2][(j+2)%3]
                              - rows[1][(j+2)%3] * rows[2][(j+1)%3])
                for j in range(3))
    return int(value > 0) - int(value < 0)


a, b, c, q = xy[[2165, 2254, 2309, 2398]]
edges = np.array([b-a, c-a])
center = a + np.linalg.solve(2*edges, (edges**2).sum(axis=1))
radius = np.linalg.norm(a-center)
direction = (q-center) / np.linalg.norm(q-center)
for offset in [-1e-12, 1e-12]:
    candidate = center + radius*(1+offset)*direction
    print("relative radius offset", offset, "exact determinant sign", exact_sign(a,b,c,candidate))

```
