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

`IntrTriangle3Triangle3` against `ExactTriangleIntersection.Intersects`:
- families: coplanar slivers (at a shared vertex, at a shared edge), the half-integer lattice of #193, exactly coplanar pairs in oblique planes with full-mantissa coordinates (their rounded normals differ by some ulps), triangles tilted within 1e-8 of another's plane touching its edge, and random triangles in general position;
- measured: false negatives and false positives of `Test()` / `Intersects()` / `Find()` (`ReportCoplanarIntersection`), `Find()` results without a contact point, and ns per pair (best of 5).

Run on 1 November 2026, 4 cores, with another benchmark running at the same time, so the times are rough; master `6ce2ad3` → the exact coplanar path (#253):

| family | pairs | false neg. master | after | false pos. master | after | Find without contact master | after | ns per pair master | after |
|---|---|---|---|---|---|---|---|---|---|
| slivers, shared vertex | 20000 | 265 / 265 / 6400 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 | 9398 | 0 | 385 / 418 / 909 | 2174 / 1563 / 12404 |
| slivers, shared edge | 20000 | 0 / 0 / 4230 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 | 9516 | 0 | 351 / 295 / 356 | 1555 / 1108 / 8370 |
| half-integer lattice | 20000 | 1317 / 1317 / 178 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 | 0 | 0 | 300 / 229 / 359 | 2079 / 1608 / 12680 |
| oblique planes | 40000 | 11079 / 11079 / 0 | 0 / 0 / 0 | 618 / 618 / 0 | 0 / 0 / 0 | 0 | 0 | 254 / 209 / 335 | 2877 / 2149 / 14840 |
| tilted within 1e-8 | 18814 | 41 / 41 / 236 | 0 / 0 / 0 | 524 / 524 / 2475 | 0 / 0 / 0 | 0 | 0 | 308 / 264 / 357 | 1004 / 892 / 8861 |
| general position | 20000 | – | – | – | – | – | – | 322 / 270 / 488 | 445 / 346 / 676 |

What the numbers show:
- On these pairs the exact path is 4-40 times slower, most for `Find()`: its contact points come from BigInteger determinants.
- In general position the new route test costs about 0.1-0.2 µs per pair: the normals, the distances to the plane, and rarely an `Orient3d`.

### With `TriangleTriangleFilter` in the exact route

The exact route's closed test (`exact_intersects`) runs `TriangleTriangleFilter` (an axis projection decided with the
exact `Orient2d`, see `benchmarks/SelfIntersectionBenchmark`) before `ExactTriangleIntersection.Intersects`. Run on
1 November 2026, 4 cores, alone on the machine; before = master 19c95f6 plus #261 without this commit, after = with it.
Three rounds alternating before / after, the minimum of each column (ns per pair `Test()` / `Intersects()` / `Find()`).
Every family has 0 false negatives, 0 false positives and 0 `Find()` results without contact in both builds.

| family | before | after |
|---|---|---|
| slivers, shared vertex | 2625 / 1822 / 15248 | 1689 / 890 / 14053 |
| slivers, shared edge | 1896 / 1336 / 9884 | 1252 / 778 / 9457 |
| half-integer lattice | 2561 / 1947 / 14366 | 1282 / 701 / 13680 |
| oblique planes | 3813 / 2592 / 16684 | 2501 / 1721 / 16286 |
| tilted within 1e-8 | 1439 / 1253 / 10673 | 1310 / 1159 / 10944 |
| general position | 561 / 419 / 882 | 564 / 428 / 879 |

`Test()` and `Intersects()` on coplanar pairs take 0.5 to 0.7 of the time (the axis-plane and projected pairs no
longer reach the exact `Orient3d` stage); `Find()` is dominated by its contact points and changes little; general
position (not on the exact route) is unchanged. (The times are higher than in the table above, which was measured
with another benchmark running: this is a slower moment of the same container, both builds measured alike.)
