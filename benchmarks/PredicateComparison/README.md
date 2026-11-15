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

## Filter underflow term

    dotnet run -c Release -- filters

ns per call of `Orient3d`, `InCircle` and `InSphere` in general position (the filter decides every call), best of 5
runs of 2 000 000 calls, one thread. Until 12 November 2026 each of the three filters added `sum * 2^-1066` to its
bound on every call; that product is subnormal for every sum below 2^44 and costs a microcode assist on x86. Now the
term is added only for sums of 2^256 and more, where it can change the bound (the proof is at
`ExactPredicates.with_underflow`); the bound, and so every decision, is the same double as before.

4-core cloud container (Xeon 2.1 GHz), master `8612d56` against the change, two runs each (the second shown):

| coordinates | orient3d before | after | incircle before | after | insphere before | after |
|---|---|---|---|---|---|---|
| [-1, 1] | 52.2 | 7.0 | 55.2 | 9.1 | 70.3 | 28.1 |
| [-1, 1] * 2^-40 | 52.7 | 7.6 | 58.4 | 8.7 | 75.4 | 28.1 |
| [-1, 1] * 2^40 | 53.6 | 7.6 | 55.7 | 9.2 | 70.7 | 28.7 |

**Effect on `ExactMeshBoolean`** (`underflow/ab.py`: per case base and candidate alternately, each one process
`BooleanComparison profile --repeat 5 --median --safe-rounding --ops union`; `underflow/abrep.py` the table; raw rows in
`underflow/results/`). The cases are those of docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 8 (36 normal unions:
Thingi10K models with their rotated, shifted copy, `cross150`, `sph300`) and its tail (the four 5.3-5.6 s models and
1088051, about 90 s; 1088138 is of the same group and was not run). 4 cores; median of 5 per case; the sums are of
the per-case medians.

| set | wall before (ms) | after | pair search (`intersections.pairs`) before | after | allocated MB before / after | GC pause ms before / after | result hashes |
|---|---|---|---|---|---|---|---|
| normal, 36 unions | 21 949 | 19 847 (-9.6 %) | 4 301 | 2 232 (-48 %) | 7 897 / 7 872 | 1 600 / 1 613 | all equal |
| tail, 5 unions | 105 535 | 99 487 (-5.7 %) | 7 464 | 3 041 (-59 %) | 15 506 / 15 504 | 5 232 / 5 362 | all equal |

- The pair search is dominated by the self-join's `touch_only` on adjacent triangles (6 million pairs for `sph300`),
  one or two `Orient3d` calls each; the filter was most of their cost.
- Largest gains: 1063860 -33 %, cross150 -30 %, 1722409 -28 %, 1041432 (tail) -21 %.
- No case is slower. Single runs of the small cases (100-400 ms) vary by up to ±18 % in either direction; the three
  that came out slower in one run (1005281, 281457, 669966) were measured again in four rounds (20 runs each side):
  medians 344.5 / 335.5, 143 / 139 and 200.5 / 184.5 ms before / after (`small_x4_2026-11-12.jsonl`).
- Allocations are unchanged; the GC pause differences are run-to-run noise.
