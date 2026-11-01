# Predicate comparison

Compares implementations of orient2d, incircle and orient3d with an exact reference: the doubles of a case
are written as integers `m * 2^e` on a common exponent and the determinant is evaluated in `BigInteger`.
Exceptions and NaN results count as wrong signs. Results and discussion: `docs/PREDICATES.md`.

Scenarios (20 000 cases each, fixed seeds):
- nearly degenerate: c = a + t (b - a) / four points on a circle / d = a + u (b - a) + v (c - a), every
  coordinate moved by up to 4 ulp
- exactly degenerate on an integer grid (collinear, on circles of radius 5k through integer points, coplanar)
- exactly degenerate with inexact differences: multiples 2^k u of one vector (collinear, and as four collinear
  points also cocircular), multiples of u and v (coplanar); these need the last adaptive stage
- the grid of Kettner et al. ("Classroom examples of robustness problems in geometric computations"):
  (0.5 + i ulp, 0.5 + j ulp) against a line, a circle and a plane through it
- all of them multiplied by 1, 1e6, 1e-10, 1e-160, 1e-300, 1e150, 1e200

Then the time for 200 000 calls in general position and nearly degenerate.

    dotnet run -c Release                      # g3 and naive double
    dotnet run -c Release -- 5000              # fewer cases
    dotnet run -c Release -p:TriangleNetProject=/path/to/triangle.net/src/Triangle/Triangle.csproj

The last one also runs Triangle.NET's `RobustPredicates` (not part of this repository; only referenced when
the property is given).

## Coplanar triangle intersection

    dotnet run -c Release -- triangles

`IntrTriangle3Triangle3` against `ExactTriangleIntersection.Intersects` on coplanar slivers (shared vertex, shared
edge), the half-integer lattice of #193 and random triangles in general position: false negatives and positives of
`Test()` / `Intersects()` / `Find()` (`ReportCoplanarIntersection`), `Find()` results without a contact point, and ns
per pair (best of 5). 1 November 2026, 4 cores (with another benchmark running, so times are rough), master `6ce2ad3`
→ the exact coplanar path:

| family (20000 pairs) | false negatives master | after | Find without contact master | after | ns per pair master | after |
|---|---|---|---|---|---|---|
| slivers, shared vertex | 265 / 265 / 6400 | 0 / 0 / 0 | 9398 | 0 | 402 / 395 / 960 | 2539 / 1805 / 12200 |
| slivers, shared edge | 0 / 0 / 4230 | 0 / 0 / 0 | 9516 | 0 | 347 / 397 / 529 | 1564 / 1103 / 8063 |
| half-integer lattice | 1317 / 1317 / 178 | 0 / 0 / 0 | 0 | 0 | 330 / 245 / 417 | 2065 / 1576 / 13494 |
| general position | – | – | – | – | 338 / 256 / 467 | 336 / 289 / 630 |

No false positives in either. The general-position `Find()` pays for the parallel test that now comes first (three
normalized cross products).
