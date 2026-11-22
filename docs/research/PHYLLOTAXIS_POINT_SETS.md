# Fibonacci point sets for geometry regression tests

**Keep the Fibonacci and seeded random fixtures.** Of the ten studies in PR #1424, González's spherical area experiment gives an inexpensive independent reference value for arbitrarily oriented inputs. The same angular sequence generates planar Delaunay stress cases. This makes the family immediately testable before investing in a solver or choosing tolerances for a production operation.

## Construction and prior art

González (2010, sections 2–4) compares cap area from weighted latitude–longitude samples with equal-weight Fibonacci samples. Latitude bands have unequal areas on an equal-angle grid. On the Fibonacci sphere, equal steps in height give bands of exactly equal area because the unit sphere's area element is dφ dz. Equal band area alone gives no guarantee about the error on a cap crossing those bands. The paper studies error over cap positions and sizes. Here we fix the cap size and vary its axis. [Paper](https://arxiv.org/pdf/0912.4540)

For index i from 0 to N−1, use u=(i+½)/N and azimuth iπ(3−√5). The planar Vogel-style spiral uses radius √u, making each annulus equal in area. The spherical version uses height 1−2u and horizontal radius √(1−z²). Both avoid a singular sample at a pole or centre. The spherical sequence agrees with the paper's centred indexing up to a rigid motion or reflection.

## Measurements

Run from the repository root with NumPy and SciPy installed:

```sh
OPENBLAS_NUM_THREADS=1 python3 benchmarks/phyllotaxis_study.py
```

Each spacing range is the observed minimum and maximum over points of the Euclidean nearest-neighbour distance multiplied by √N. Sphere distances are chords. For 128 unit axes sampled by normalizing Gaussian triples (seed 1424), count points whose dot product with the axis is at least ½. The exact surface fraction is (1−½)/2 = ¼. RMSE below is in fractions of total sphere area. The comparator has the same point count, midpoint latitudes, equally spaced meridians and normalized cosine-latitude weights.

| N | Disc spacing | Sphere spacing | Latitude × longitude | Grid RMSE | Fibonacci RMSE |
|---:|---:|---:|---:|---:|---:|
| 253 | 1.546–1.732 | 3.091–3.474 | 11 × 23 | .007290 | .005240 |
| 1081 | 1.546–1.756 | 3.092–3.484 | 23 × 47 | .002185 | .001877 |
| 4095 | 1.546–1.762 | 3.092–3.532 | 45 × 91 | .000725 | .000674 |

Fibonacci has smaller RMS error at each sampled budget. The relative improvement ranges from 7% to 28%. These three measurements do not establish a convergence exponent, a covering radius or a worst-case spacing bound.

Recomputing every nearest distance after the eight motions specified in the repository's Similarity.All() and dividing by the scale gave maximum absolute changes below 9.0e−7 in unit-radius coordinates. This includes the disc embedded in 3D. The combined millimetre scale and UTM translation loses precision when storing coordinates. Preserve the local plane for planar triangulation or use All2D().

## Degeneracies and integration decision

Among pairs of adjacent planar Delaunay triangles, the smallest absolute incircle determinant after dividing coordinates by the quartet diameter was 9.71e−4, 8.63e−5 and 6.40e−5 respectively. At N=4095, indices 2165, 2254, 2309 and 2398 reproduce the last case. A small residual flags a candidate for diagonal-flip tests. It is not an exact tie. For a controlled boundary case, use four cardinal points of the unit circle, then move one radially inward or outward by one representable step. Accept either legal diagonal on the tie and verify the sign off the tie.

Generation costs O(N) time and storage. The cap count costs O(128N). The cap area and equal-area strata are exact references. The measurements below evaluate finite inputs and five relaxation steps.

## Consumer A/B on 253 points

Run from the repository root with .NET 10, NumPy and SciPy:

```sh
dotnet build benchmarks/PhyllotaxisConsumers -c Release
python3 benchmarks/PhyllotaxisConsumers/measure.py
```

Compare the golden-angle construction with three area-uniform independent random draws (seeds 1424–1426) on each domain. The library already contains a spherical generator, SphericalFibonacciPointSet.Point. Its longitude convention reflects the spiral used here. Reuse that generator for a future library-side fixture.

| Disc measurement | Fibonacci | Random, range across three seeds |
|---|---:|---:|
| Valid Delaunay triangles | 483 | 480–488 |
| Shortest Delaunay edge | .09720 | .00440–.01113 |
| Mean squared distance to nearest site, initial | .002145 | .004069–.004553 |
| Same distance after five Lloyd steps | .002067 | .002237–.002326 |

DelaunayMesh2 used all sites and its exact local-legality validator passed on all four meshes. A separate cardinal quartet selected the expected diagonal at the exact tie and on either side after moving one coordinate by one representable step. For CVT we approximated disc-clipped Lloyd centroids with 131072 uniform-area Sobol samples per step, and scored with an independently scrambled set of the same size. This is a numerical reference experiment. The Delaunay and remeshing measurements use the actual C# library.

For each spherical point set, orient the convex hull outward and apply five serial Remesher passes with radial unit-sphere projection and target edge length √(8π/(√3 N)). Every mesh passed connectivity validity, closure and Euler characteristic 2 at every pass. Vertex radius error stayed below 4e−16. Count inward or degenerate faces by checking the sign of the face normal dotted with one vertex position.

| Maximum count in any of five passes | Fibonacci | Seed 1424 | Seed 1425 | Seed 1426 |
|---|---:|---:|---:|---:|
| PreventNormalFlips off | 2 | 13 | 7 | 7 |
| PreventNormalFlips on | 0 | 5 | 2 | 1 |

The unguarded Fibonacci sphere had 0, 0, 1, 2 and 0 inward faces after successive passes. Checking only the final mesh misses this event. The edge-length coefficient of variation rose from .1345 to .1685 (.1612 with the guard), while random starts fell from .490–.506 to .251–.258 without the guard. Remeshing improved edge uniformity on these random starts and slightly degraded the more uniform Fibonacci input. The normal-flip guard applies to particular local edits. The experiment does not isolate which edit, smoothing or projection stage caused the remaining folds.

Use Fibonacci for a repeatable well-spaced baseline and retain seeded random cases for close pairs and remeshing stress. Record the orientation after each pass. These comparisons cover one point budget and five serial passes.
