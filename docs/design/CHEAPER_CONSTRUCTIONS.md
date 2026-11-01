# Cheaper exact constructions and pair tests in the arrangement

Status: **approved by the owner (decisions 1-3 as proposed); steps B and A1 built, section 12** (tier L, [AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md)). Nothing
in the library is changed yet. Base: #248 (`claude/pair-search`, on #223). This document has the step-0
measurements (section 2), what they change in the plan (section 3), the design (section 4), the bounds derived by
the generator (section 5), and the plan with its stop rule (section 6).

**Decisions for the owner** (section 7 has the details):
1. Order of the steps: the pair-test steps (A, B) first, the constructions (C-E) after, because the measurement
   puts the larger and cheaper gain there.
2. Step A2 (coplanar neighbours sharing a *vertex*) is not in the brief, but measures 7-8 % against 0.4-0.7 % for
   the shared *edge* of the brief. It needs either a small internal entry point in `ExactTriangleIntersection`
   (stage R's file) or a copy of its 2D helpers in the arrangement.
3. The Orient3d kernel (B) goes into `ExactPredicates.orient3d_exact` (every caller gains), without a per-triangle
   plane cache at first. The cache is a measured variant (B2), not the default.

## 1. Goal

Make the exact work of `ExactMeshArrangement` cheaper, with **bit-identical results**: the exact constructions
(triangle and edge planes, the implicit points, the line directions, the centroid) in fixed width with the gcd on
limbs instead of `BigInteger`, and as their first users a fast exact kernel for `Orient3d` on input points and an
exact shortcut for coplanar neighbours in the pair search. No API change; netstandard2.0 / C# 7.3.

## 2. Step 0: measured

**Method** (as in "Where the time goes" of [perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md)):
a temporary probe (separate worktree, not committed) with `gParallel.SerialOnThisThread = true` around `Compute()`,
run under `taskset -c 0` with `DOTNET_PROCESSOR_COUNT=1`, `profile --repeat 1` over every case of a class. Each probed
scope is timed with `Stopwatch.GetTimestamp`; a `[ThreadStatic]` stack gives the inclusive time, the time outside
any other probed scope ("top") and the exclusive time. Value sizes are the bit lengths of the results, bucketed by
limbs (`ceil(bits / 64)`). The probe costs time itself: its class totals are 1-38 % above an unprobed serial build
of the same base (most on `nreal`, where the per-call `Orient3d` bookkeeping weighs most). **The shares below are of
the probe's total and so are lower bounds**; per-call times of small scopes include about 30-50 ns of probe
overhead.

```
~/serial_run.sh <tool> <out>      # every class, one thread on one core (the script is in section 9)
python3 analyze.py <base out> <probe out>
```

Serial totals (unprobed / probed): `cgal` 21.25 / 24.58 s, Thingi10K 20.71 / 23.47 s, `cherchi` 38.39 / 38.86 s,
n-ary (grid 5, grid 10, spheres 20) 15.50 / 16.30 s, `nreal` (new, section 8) 6.65 / 9.14 s.

### 2.1 Constructions (inclusive, so including those nested in exact predicate stages)

| kind | `cgal` | Thingi10K | `cherchi` | n-ary | `nreal` | per call |
|---|---|---|---|---|---|---|
| triangle plane (`compute_planes`) | 772 ms, 3.1 % | 706 ms, 3.0 % | 2734 ms, 7.0 % | 214 ms, 1.3 % | 237 ms, 2.6 % | 5.6-9.1 µs |
| of it: `N`, `D` | 128 ms | 123 ms | 284 ms | 36 ms | 33 ms | 1.0 µs |
| of it: edge planes for `PlaneApprox` | 454 ms | 395 ms | 1841 ms | 120 ms | 142 ms | 3.1-6.1 µs |
| exact edge planes (`compute_edge_planes`) | - | - | - | 53 ms | 7 ms | 4-8 µs |
| implicit points (`compute_exact`) | 1789 ms, 7.3 % | 1977 ms, 8.4 % | 1756 ms, 4.5 % | 941 ms, 5.8 % | 1165 ms, 12.7 % | 5-8 µs (EdgePlane) |
| of it: the reduction (`ExactPoint3.Create`) | 956 ms, 3.9 % | 919 ms, 3.9 % | 818 ms, 2.1 % | 302 ms, 1.9 % | 396 ms, 4.3 % | 2.0-3.7 µs |
| line directions (`Direction`) | 467 ms, 1.9 % | 662 ms, 2.8 % | 818 ms, 2.1 % | 891 ms, 5.5 % | 354 ms, 3.9 % | 0.7-0.9 µs |
| centroid (`ExactMeshBoolean.centroid`) | 19 ms | 18 ms | 58 ms | 1327 ms, 8.1 % | 26 ms | 4.3 µs (n-ary) |
| **sum** | **12.4 %** | **14.3 %** | **13.8 %** | **22.3 %** | **19.3 %** | |

- The implicit points are almost all `EdgePlane` points on real inputs; `Segments` and `Planes` points occur on
  n-ary (324 / 261 ms) and rarely on `nreal`.
- **The reduction is always needed** (W changed in 100 % of 257k / 375k / 233k / 150k point constructions), but
  **the gcd is mostly a power of two**: point gcd median 47 bits (p90 115, p99 208-285 on real inputs, 111 / 372 / 424
  on n-ary); its odd part is 1 in 43 % and has a median of 2 bits (p90 24-54, p99 47-103). The grid unit is below
  the inputs' own resolution, so the plane values carry common trailing zeros. A limb gcd that shifts out the
  common trailing zeros first and runs a binary gcd on the small odd parts is the natural design (section 4.4).
- The centroid's gcd is 1 in 73-81 % on real inputs and 49 % on n-ary.

**Value sizes** (share of the values with that many limbs; numerators `x, y, z` / denominator or `D`):

| value | `cgal` | Thingi10K | n-ary |
|---|---|---|---|
| triangle normal `N` (≤ 2 / 3 / 4 limbs) | 75 / 18 / 7 % | 81 / 16 / 3 % | 100 / 0 / 0 % |
| triangle `D` (≤ 3 / 4 / 5 / 6 limbs) | 68 / 22 / 5 / 5 % | 80 / 17 / 1 / 2 % | 100 % ≤ 3 |
| EdgePlane point before reduction (≤ 4 / 5 / 6 / 7 limbs) | 76 / 14 / 3 / 6 % | 80 / 10 / 8 / 2 % | 100 % ≤ 4 |
| EdgePlane point after reduction (≤ 4 / 5 limbs) | 98 / 2 % | 99.7 / 0.3 % | 99.9 % ≤ 4 |
| direction (≤ 4 / 5 / 6 / 7 limbs) | 76 / 13 / 4 / 7 % | 86 / 11 / 1 / 2 % | 100 % ≤ 5 |

So the input grid coordinates fit one limb (`N` ≤ 128 bits for 75-100 %), and almost every reduced point fits the
4-limb `FixedPoint3`.

### 2.2 The exact stages of `Orient3d` (`ExactPredicates.orient3d_exact`, on input points)

| | `cgal` | Thingi10K | `cherchi` | n-ary | `nreal` |
|---|---|---|---|---|---|
| exact stages | 7.73M | 6.97M | 216 | 2.61M | 4.13M |
| result 0 | 90.4 % | 88.4 % | 100 % | 100 % | 93.7 % |
| time (inclusive) | 1478 ms, 6.0 % | 1178 ms, 5.0 % | 1 ms | 498 ms, 3.1 % | 773 ms, 8.5 % |
| per stage | 191 ns | 169 ns | | 191 ns | 187 ns |
| differences ≤ 62 bits (result ≠ 0 / = 0) | 100 / 91.4 % | 100 / 98.7 % | - / 100 % | - / 100 % | 99.6 / 99.5 % |

"Differences" are the coordinate differences `b - a, c - a, d - a` in units of the lowest set bit of all twelve
coordinates. They are 48-62 bits on CAD inputs (float coordinates converted to double), so one limb covers 91-100 %
of the stages, and the products of a 3×3 determinant fit 3 limbs (section 5).

### 2.3 `IntersectsBeyondShared` in `touch_only` (the pair search within a mesh)

The quick test of `touch_only` fails whenever a non-shared corner is coplanar with the other triangle, and the pair
goes to `ExactTriangleIntersection.IntersectsBeyondShared`:

| | `cgal` | Thingi10K | `nreal` |
|---|---|---|---|
| calls, time (inclusive) | 2.34M, 4242 ms, **17.0 %** | 1.94M, 4107 ms, **15.2 %** | 0.90M, 2143 ms, **23.4 %** |
| shared edge, coplanar (the brief's shortcut) | 239k, 164 ms, **0.7 %** | 113k, 99 ms, **0.4 %** | 54k, 37 ms, **0.4 %** |
| shared vertex, both other corners coplanar | 505k, 2067 ms, **8.3 %** | 373k, 1800 ms, **6.7 %** | 174k, 692 ms, **7.6 %** |
| the rest (shared vertex not coplanar; no shared vertex) | 1.59M, 2122 ms, 8.5 % | 1.45M, 2309 ms, 8.6 % | 0.67M, 1463 ms, 16.0 % |
| of the coplanar ones: touch only | 100 % | 100 % | 99.4 % (shared edge) |

(`cherchi`: 2.06M calls, 1412 ms, 3.6 %, almost all with one shared vertex. Shared edges reach the call only when
coplanar: 0 non-coplanar shared-edge calls in every class.)

- A coplanar shared-edge pair costs about 0.7 µs (`IntersectsBeyondShared` already decides it with one
  `Orient3d` and two `orient2d`); the shortcut of the brief saves only the repeated `Orient3d` and the call
  overhead: **at most 0.4-0.7 %**.
- A coplanar shared-vertex pair costs about 4 µs: `IntersectsBeyondShared` runs two `SegmentIntersectsTriangle` (each
  with a degeneracy test and two `Orient3d` that are exactly 0, so exact stages) and two more `Orient3d` before its
  2D corner test. About six exact `Orient3d` stages per pair, all known to be 0 from the coplanarity.

## 3. What the numbers change

| candidate (section 4) | bound, serial, `cgal` / Thingi10K | expected, serial | brief |
|---|---|---|---|
| A1 coplanar shared edge in `touch_only` | 0.7 / 0.4 % | ~0.5 % | yes |
| A2 coplanar shared vertex in `touch_only` | 8.3 / 6.7 % | 5-7 % | **new** |
| B Orient3d kernel on input points | 6.0 / 5.0 % | 3-4 % (less after A2, which removes part of the stages) | yes |
| C implicit points on FixedArith, gcd on limbs | 7.3 / 8.4 % | 3-5 % | yes |
| D triangle planes, edge-plane approximations, directions in fixed width | 5.0 / 5.8 % | 2-4 % | yes |
| E centroid on FixedArith | 0.1 / 0.1 % (n-ary 8.1 %) | n-ary 3-5 % | yes |

On 4 cores the pair search scales better than the rest, so A and B gain less there than serially (the
hand-over: pair search 29 % on 4 cores against 50 % serially). The stop rule is measured on 4 cores (section 6).

## 4. Design

### 4.1 A: coplanar neighbours in `touch_only`

`one_side_except_shared(T, U)` stops at the first corner of `U` with orientation 0. It is changed to return the
orientations of the non-shared corners it computed (a small struct, no allocation), so that `touch_only` knows
without new predicates whether **all** non-shared corners of `U` are coplanar with `T`. Then (both triangles are
non-degenerate there: `self_pairs` skips degenerate ones):

- **A1, shared edge `a b`, far corners `c` of T and `d` of U, `d` coplanar:** the triangles meet only in the edge iff
  `c` and `d` lie strictly on opposite sides of the line `a b` in the plane:
  `orient2d(a, b, c) · orient2d(a, b, d) < 0` in the projection along T's dominant normal axis. This is the shared-edge
  branch of `IntersectsBeyondShared` without its `Orient3d` (known to be 0) and without the call; same projection
  rule (`dominant_axis` of the double cross product), same predicates, so the same result by construction.
- **A2, one shared vertex `s`, the other corners `p, q` of T and `u, w` of U, `u, w` coplanar with T:** all five
  points lie in T's plane, so `p, q` are coplanar with U as well (U is non-degenerate and spans the same plane).
  Every `Orient3d` that `IntersectsBeyondShared` evaluates for this pair is then exactly 0: the two in each
  `SegmentIntersectsTriangle` (`ds = dt = 0`) and `Orient3d(s, p, q, u)`, `Orient3d(s, p, q, w)`. The shortcut is
  that function's code path with those values substituted: the coplanar branch of
  `SegmentIntersectsTriangle(p, q, U)` and `(u, w, T)` (2D point-in-triangle and segment tests in `projection_axis`),
  then the 2D corner test. Its non-degeneracy test `is_degenerate` (three `orient2d`) agrees with
  `InputTriangle.Degenerate` (one exact `orient2d` on the dominant axis) for every triangle the arrangement keeps,
  so it is skipped too.
- **Where the code lives.** The 2D helpers (`projection_axis`, `point_in_triangle_2d`, `segments_intersect_2d`,
  `inside_corner`, `same_ray`, `dominant_axis`) are private in `intersection/ExactTriangleIntersection.cs`, which
  belongs to stage R (#249, #251). Option (a): stage R adds an internal
  `IntersectsBeyondSharedCoplanar(Index3i, ref Triangle3d, Index3i, ref Triangle3d)` that takes the coplanarity as
  known, a refactoring of its existing branch with no change of behaviour; the arrangement calls it. Option (b): a
  copy of the helpers in the arrangement (about 60 lines). (a) keeps one implementation and is preferred; it needs
  the coordinator.
- **Equality with the current result** is by construction (the same predicates on the same values, in the same
  order, with the known zeros substituted), and is checked by a differential test against `IntersectsBeyondShared`
  on generated coplanar fans and strips (shared edge and shared vertex; collinear corners, corners on the other
  triangle's edges, same rays, nested corners, folds that overlap) and by `exact_hashes.sh`.

### 4.2 B: the Orient3d kernel on input points

In `ExactPredicates.orient3d_exact`, before the adaptive stages (math/ExactPredicates.cs is exact code: full review,
no FMA, no `float`, no parallel loop):

1. `e` = the lowest set bit over the twelve coordinates (from the bits of the doubles; a zero coordinate does not
   count). Every coordinate and every difference is an integer multiple of `2^e`.
2. Each difference `b.x - a.x` etc. exactly as a 64-bit integer in units of `2^e`: `TwoDiff` gives `hi + lo` exactly
   (two doubles, both multiples of `2^e`), and `hi · 2^-e`, `lo · 2^-e` are exact integer-valued doubles; admitted iff
   `|hi · 2^-e| < 2^62` (then the sum fits: `|lo| ≤ ulp(hi) / 2`). Otherwise, or if `e` is below the double range
   where `2^-e` is not a double (`e < -1023`), the existing adaptive / `BigInteger` path runs unchanged. The test
   is written so that an infinite `hi` (an overflowing difference) or NaN fails it.
3. The determinant `w · (u × v)` in sign-magnitude limbs, unrolled: the three minors `u_y v_z - u_z v_y` in 2 limbs
   (each product a 64 × 64 -> 128-bit `MulHigh`, the difference < 2^126), the three terms minor × difference in 3 limbs,
   their sum in 3 limbs (< 6 · 2^186 < 2^189, section 5), and its sign. 6 + 6 `MulHigh` (64 × 64 bits) and a handful
   of carries:
   expected 15-30 ns against 170-190 ns now.
4. The sign is that of `orient3d_bigint` (the same determinant up to the positive factor `2^(3(e - emin))`, with
   `emin ≤ e` the scale `orient3d_bigint` uses), so the result is the same as today's. TwoDiff needs strict IEEE
   double rounding, which `AdaptivePredicates` assumes already.

**B2, a cached plane (the brief's "plane in fixed width"), only as a measured variant:** the minors are the normal
of the triangle `(a, b, c)`, the same for all queries against it. A per-triangle cache in the arrangement (the normal
in 2 limbs plus the scale, published like `PlaneData`) would save the 6 minor products per query, but needs storage
and publication per triangle (438k / 337k triangles have exact stages on `cgal` / Thingi10K), and the cached normal is
only valid for queries with the same `e`. Built only if B leaves a measurable share.

### 4.3 C: the implicit points on FixedArith

`compute_exact` gets a fixed-width path that is taken when all inputs of the construction are fixed-width points
(input points always are) and its planes are fixed-width (section 4.5):

- `EdgePlane` (always an edge of input points against a triangle plane: its only caller is `plane_points`), `Segments`
  (four input points, from `clip_into`) and `Planes` (the triangle's own plane and two segment lines, each a triangle
  plane or an edge plane: TTT, TTE or TEE; three edge planes cannot occur) evaluated with `FixedArith` on a stack arena, in
  the same order of operations as the `BigInteger` code (the value is the same integer either way; the order only
  matters for the bounds, which are derived for this order in section 5);
- the reduction on limbs (4.4), the sign normalization (`W > 0`), then the canonical point **directly as a
  `FixedPoint3`** if every component fits 255 bits (as `FixedPoint3.TryCreate` decides today), including `IsDouble`
  and `D` computed from the limbs; otherwise materialized as `ExactPoint3` and stored as today (fallback);
- `Segments` with `W = 1` inputs simplifies `Det3` to `(s - r) × (p - r)`-like terms, but keeps the code's expansion
  of `ExactPoint3.Det3` so that one bounds table covers both;
- the arena size per construction is the derived scratch (section 5, column "limbs taken"), a constant per kind. The
  spec writes the values without the code's products by `W = 1` and without the recomputed differences of
  `edge_plane` (`q - p`, and for edges 1 and 2 a difference the spec does not name); a kernel that mirrors the code
  literally takes a few limbs more, so the kernels either skip those operations or size their arena with a margin,
  and a test checks the arena use against the constant;
- the public `EdgePlanePoint` / `PlanesPoint` (any rational points, any `Plane`, unbounded sizes) stay on
  `BigInteger`.

### 4.4 The gcd on limbs

`g = gcd(|x|, |y|, |z|, w)`, then `x / g` etc.:

1. `t` = the smallest number of trailing zero bits over the four nonzero components (`TrailingZeroCount` on the
   lowest nonzero limb; portable loop on netstandard2.0); shift all four right by `t` (exact).
2. The odd part: binary gcd (Stein) of the shifted components, starting from the smallest, with an early exit when
   the running gcd is 1 (43 % of the cases after step 1). Operands are trimmed sign-magnitude limbs; the running gcd
   is at most as long as the shortest component, usually 1 limb (median 2 bits).
3. Division by the odd gcd `h` when `h > 1`: `h` fits one limb in ≥ 99 % of the cases (p99 of the odd part 47-103
   bits on real inputs, 209 on n-ary): a one-limb divisor uses `x / h` limb by limb with a 128 / 64 division
   (`Math.DivRem(UInt128)` is not on netstandard2.0, so a portable 128 / 64 division, tested against `BigInteger`); a
   longer `h` falls back to `BigInteger` division for that point.
4. Result: the same canonical point as `ExactPoint3.Create` (the gcd is unique), so bit-identical.

### 4.5 D: triangle planes, edge-plane approximations and directions in fixed width

- `compute_planes` computes `N`, `D` and the three edge planes with `FixedArith` from the corners' limbs (input points,
  one limb each in the common case), and the 16 doubles of `PlaneApprox` from the limbs by a conversion that is
  **bit-identical to `(double)BigInteger`** on the platform's runtime semantics that the error bound assumes
  (relative error < 2^-52). It is tested against `(double)BigInteger` on random values of every length up to 12
  limbs; if .NET Framework's conversion differs from .NET's, the filter bound still holds (both have relative error
  < 2^-52) and the results stay exact, but the doubles may differ between the two runtimes, as they may today.
- The exact plane is kept in limbs (a reference-free struct in `PlaneData`, up to 3 + 4 limbs for `N`, `D` and
  4 + 5 limbs per edge plane by section 5), and the `BigInteger` `Plane` is materialized only for the users that stay
  on `BigInteger` (`same_plane`, the boolean's dot products, the fallback constructions).
- `Direction` (`N_T × N_U`, 5 limbs; `N_T × E_U`, 6 limbs, from `line_direction` with an edge-plane line) is computed
  from the limbs; `compare_along` already loads the direction into
  limbs (#223, "caching the direction limbs" is then free: the direction is born in limbs).
- Inputs beyond one limb (grids wider than 64 bits) take the general trimmed `FixedArith`, whose lengths cannot
  overflow by construction; beyond `FixedPoint3` (fallback points) the `BigInteger` code as today.

### 4.6 E: the centroid

`ExactMeshBoolean.centroid` on the three fixed-width points: `wbc = W_b W_c` etc., the sums, `w = 3 W_a W_bc`, then
the limb gcd (4.4, whose odd part is the whole gcd here: 1 in 49-81 %) and `AddPoint` with the limbs (its
`FixedKey` path already takes limbs). Fallback to today's code when a point is not fixed-width.

## 5. Bounds (derived by the generator)

Derived by `tools/PredicateCompiler --table` (branch `claude/indirect-predicates-filter`, `4d9f820`, unchanged) from
[cheaper_constructions.spec](cheaper_constructions.spec), which writes every construction in the order of the code.
Input points of the constructions: grid coordinates `|v| ≤ m = 2^64 - 1` (one limb; wider inputs take the general
path, 4.5). Centroid: the components of `FixedPoint3`, `m = 2^255 - 1`. Kernel B: the differences in units of `2^e`,
`|d| ≤ 2^62 - 1`. The generator needs one `sign` line per entry; for a construction it lists the outputs, and the
row "the determinant" is the first output (`x`, or `N_x`; the others have the same shape). "Limbs taken" is the
length model of the general `FixedArith`; the unrolled kernel B uses the value limbs.

<!-- generated by tools/PredicateCompiler --table; do not edit -->
| predicate | values | bound | bits | limbs | limbs taken by FixedArith |
|---|---|---|---|---|---|
| `TrianglePlane` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `TrianglePlane` | `ux`, `uy`, `uz`, `vx`, `vy`, `vz` | `2 m` | 65 | 2 | 2 |
| `TrianglePlane` | `uy * vz` and 5 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `TrianglePlane` | the determinant (sign factor 1) | `8 m^2` | 131 | 3 | 4 |
| `TrianglePlane` | `a.x * nx` and 2 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `TrianglePlane` | `(a.x * nx) + (a.y * ny)` | `16 m^3` | 196 | 4 | 5 |
| `TrianglePlane` | `d` (a sign factor) | `24 m^3` | 197 | 4 | 5 |
| `EdgePlane` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `EdgePlane` | `ux`, `uy`, `uz`, `vx`, `vy`, `vz` | `2 m` | 65 | 2 | 2 |
| `EdgePlane` | `uy * vz` and 5 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `EdgePlane` | `nx`, `ny`, `nz` | `8 m^2` | 131 | 3 | 4 |
| `EdgePlane` | `uy * nz` and 5 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `EdgePlane` | the determinant (sign factor 1) | `32 m^3` | 197 | 4 | 5 |
| `EdgePlane` | `a.x * ex` and 2 more of its shape | `32 m^4` | 261 | 5 | 5 |
| `EdgePlane` | `(a.x * ex) + (a.y * ey)` | `64 m^4` | 262 | 5 | 6 |
| `EdgePlane` | `f` (a sign factor) | `96 m^4` | 263 | 5 | 6 |
| `PointEdgeTrianglePlane` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `PointEdgeTrianglePlane` | `ux`, `uy`, `uz`, `vx`, `vy`, `vz` | `2 m` | 65 | 2 | 2 |
| `PointEdgeTrianglePlane` | `uy * vz` and 5 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `PointEdgeTrianglePlane` | `nx`, `ny`, `nz` | `8 m^2` | 131 | 3 | 4 |
| `PointEdgeTrianglePlane` | `a.x * nx` and 8 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `PointEdgeTrianglePlane` | `(a.x * nx) + (a.y * ny)` and 2 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `PointEdgeTrianglePlane` | `d` and 2 more like `((nx * p.x) + (ny * p.y)) + (nz * p.z)` | `24 m^3` | 197 | 4 | 5 |
| `PointEdgeTrianglePlane` | `sp`, `sq` | `48 m^3` | 198 | 4 | 5 |
| `PointEdgeTrianglePlane` | `sp * q.x` and 5 more of its shape | `48 m^4` | 262 | 5 | 5 |
| `PointEdgeTrianglePlane` | the determinant (sign factor 1) | `96 m^4` | 263 | 5 | 6 |
| `PointEdgeTrianglePlane` | `w` (a sign factor) | `96 m^3` | 199 | 4 | 5 |
| `PointSegments` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `PointSegments` | `s.y - p.y` and 3 more of its shape | `2 m` | 65 | 2 | 2 |
| `PointSegments` | `r.x * (s.y - p.y)` and 3 more of its shape | `2 m^2` | 129 | 3 | 3 |
| `PointSegments` | `(r.x * (s.y - p.y)) - (r.y * (s.x - p.x))` and 1 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `PointSegments` | `p.y * s.x` and 3 more of its shape | `m^2` | 128 | 2 | 2 |
| `PointSegments` | `(p.y * s.x) - (s.y * p.x)` and 1 more of its shape | `2 m^2` | 129 | 3 | 3 |
| `PointSegments` | `sp`, `sq` | `6 m^2` | 131 | 3 | 4 |
| `PointSegments` | `sp * q.x` and 5 more of its shape | `6 m^3` | 195 | 4 | 4 |
| `PointSegments` | the determinant (sign factor 1) | `12 m^3` | 196 | 4 | 5 |
| `PointSegments` | `w` (a sign factor) | `12 m^2` | 132 | 3 | 4 |
| `PointThreeTrianglePlanes` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `PointThreeTrianglePlanes` | `u1x`, `u1y`, `u1z`, `v1x`, `v1y`, `v1z`, `u2x`, `u2y`, `u2z`, `v2x`, `v2y`, `v2z`, `u3x`, `u3y`, `u3z`, `v3x`, `v3y`, `v3z` | `2 m` | 65 | 2 | 2 |
| `PointThreeTrianglePlanes` | `u1y * v1z` and 17 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `PointThreeTrianglePlanes` | `n1x`, `n1y`, `n1z`, `n2x`, `n2y`, `n2z`, `n3x`, `n3y`, `n3z` | `8 m^2` | 131 | 3 | 4 |
| `PointThreeTrianglePlanes` | `a1.x * n1x` and 8 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `PointThreeTrianglePlanes` | `(a1.x * n1x) + (a1.y * n1y)` and 2 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `PointThreeTrianglePlanes` | `d1`, `d2`, `d3` | `24 m^3` | 197 | 4 | 5 |
| `PointThreeTrianglePlanes` | `n2y * n3z` and 17 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `PointThreeTrianglePlanes` | `c23x`, `c23y`, `c23z`, `c31x`, `c31y`, `c31z`, `c12x`, `c12y`, `c12z` | `128 m^4` | 263 | 5 | 6 |
| `PointThreeTrianglePlanes` | `n1x * c23x` and 2 more of its shape | `1024 m^6` | 394 | 7 | 8 |
| `PointThreeTrianglePlanes` | `(n1x * c23x) + (n1y * c23y)` | `2048 m^6` | 395 | 7 | 8 |
| `PointThreeTrianglePlanes` | `w` (a sign factor) | `3072 m^6` | 396 | 7 | 8 |
| `PointThreeTrianglePlanes` | `d1 * c23x` and 8 more of its shape | `3072 m^7` | 460 | 8 | 9 |
| `PointThreeTrianglePlanes` | `(d1 * c23x) + (d2 * c31x)` and 2 more of its shape | `6144 m^7` | 461 | 8 | 9 |
| `PointThreeTrianglePlanes` | the determinant (sign factor 1) | `9216 m^7` | 462 | 8 | 9 |
| `PointTriangleTwoEdgePlanes` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `PointTriangleTwoEdgePlanes` | `u1x`, `u1y`, `u1z`, `v1x`, `v1y`, `v1z`, `u2x`, `u2y`, `u2z`, `v2x`, `v2y`, `v2z`, `u3x`, `u3y`, `u3z`, `v3x`, `v3y`, `v3z` | `2 m` | 65 | 2 | 2 |
| `PointTriangleTwoEdgePlanes` | `u1y * v1z` and 17 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `PointTriangleTwoEdgePlanes` | `e1x`, `e1y`, `e1z`, `n2x`, `n2y`, `n2z`, `n3x`, `n3y`, `n3z` | `8 m^2` | 131 | 3 | 4 |
| `PointTriangleTwoEdgePlanes` | `a1.x * e1x` and 2 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `PointTriangleTwoEdgePlanes` | `(a1.x * e1x) + (a1.y * e1y)` | `16 m^3` | 196 | 4 | 5 |
| `PointTriangleTwoEdgePlanes` | `f1` | `24 m^3` | 197 | 4 | 5 |
| `PointTriangleTwoEdgePlanes` | `u2y * n2z` and 11 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `PointTriangleTwoEdgePlanes` | `e2x`, `e2y`, `e2z`, `e3x`, `e3y`, `e3z` | `32 m^3` | 197 | 4 | 5 |
| `PointTriangleTwoEdgePlanes` | `a2.x * e2x` and 5 more of its shape | `32 m^4` | 261 | 5 | 5 |
| `PointTriangleTwoEdgePlanes` | `(a2.x * e2x) + (a2.y * e2y)` and 1 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `PointTriangleTwoEdgePlanes` | `f2`, `f3` | `96 m^4` | 263 | 5 | 6 |
| `PointTriangleTwoEdgePlanes` | `e2y * e3z` and 5 more of its shape | `1024 m^6` | 394 | 7 | 8 |
| `PointTriangleTwoEdgePlanes` | `c23x`, `c23y`, `c23z` | `2048 m^6` | 395 | 7 | 8 |
| `PointTriangleTwoEdgePlanes` | `e1z * e3y` and 11 more of its shape | `256 m^5` | 328 | 6 | 7 |
| `PointTriangleTwoEdgePlanes` | `c31x`, `c31y`, `c31z`, `c12x`, `c12y`, `c12z` | `512 m^5` | 329 | 6 | 7 |
| `PointTriangleTwoEdgePlanes` | `e1x * c23x` and 2 more of its shape | `16384 m^8` | 526 | 9 | 10 |
| `PointTriangleTwoEdgePlanes` | `(e1x * c23x) + (e1y * c23y)` | `32768 m^8` | 527 | 9 | 10 |
| `PointTriangleTwoEdgePlanes` | `w` (a sign factor) | `49152 m^8` | 528 | 9 | 10 |
| `PointTriangleTwoEdgePlanes` | `f1 * c23x` and 2 more of its shape | `49152 m^9` | 592 | 10 | 11 |
| `PointTriangleTwoEdgePlanes` | `f2 * c31x` and 5 more of its shape | `49152 m^9` | 592 | 10 | 11 |
| `PointTriangleTwoEdgePlanes` | `(f1 * c23x) + (f2 * c31x)` and 2 more of its shape | `98304 m^9` | 593 | 10 | 11 |
| `PointTriangleTwoEdgePlanes` | the determinant (sign factor 1) | `147456 m^9` | 594 | 10 | 11 |
| `PointTwoTrianglePlanesEdgePlane` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `PointTwoTrianglePlanesEdgePlane` | `u1x`, `u1y`, `u1z`, `v1x`, `v1y`, `v1z`, `u2x`, `u2y`, `u2z`, `v2x`, `v2y`, `v2z`, `u3x`, `u3y`, `u3z`, `v3x`, `v3y`, `v3z` | `2 m` | 65 | 2 | 2 |
| `PointTwoTrianglePlanesEdgePlane` | `u1y * v1z` and 17 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `PointTwoTrianglePlanesEdgePlane` | `e1x`, `e1y`, `e1z`, `e2x`, `e2y`, `e2z`, `n3x`, `n3y`, `n3z` | `8 m^2` | 131 | 3 | 4 |
| `PointTwoTrianglePlanesEdgePlane` | `a1.x * e1x` and 5 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `PointTwoTrianglePlanesEdgePlane` | `(a1.x * e1x) + (a1.y * e1y)` and 1 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `PointTwoTrianglePlanesEdgePlane` | `f1`, `f2` | `24 m^3` | 197 | 4 | 5 |
| `PointTwoTrianglePlanesEdgePlane` | `u3y * n3z` and 5 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `PointTwoTrianglePlanesEdgePlane` | `e3x`, `e3y`, `e3z` | `32 m^3` | 197 | 4 | 5 |
| `PointTwoTrianglePlanesEdgePlane` | `a3.x * e3x` and 2 more of its shape | `32 m^4` | 261 | 5 | 5 |
| `PointTwoTrianglePlanesEdgePlane` | `(a3.x * e3x) + (a3.y * e3y)` | `64 m^4` | 262 | 5 | 6 |
| `PointTwoTrianglePlanesEdgePlane` | `f3` | `96 m^4` | 263 | 5 | 6 |
| `PointTwoTrianglePlanesEdgePlane` | `e2y * e3z` and 11 more of its shape | `256 m^5` | 328 | 6 | 7 |
| `PointTwoTrianglePlanesEdgePlane` | `c23x`, `c23y`, `c23z`, `c31x`, `c31y`, `c31z` | `512 m^5` | 329 | 6 | 7 |
| `PointTwoTrianglePlanesEdgePlane` | `e1y * e2z` and 5 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `PointTwoTrianglePlanesEdgePlane` | `c12x`, `c12y`, `c12z` | `128 m^4` | 263 | 5 | 6 |
| `PointTwoTrianglePlanesEdgePlane` | `e1x * c23x` and 2 more of its shape | `4096 m^7` | 460 | 8 | 9 |
| `PointTwoTrianglePlanesEdgePlane` | `(e1x * c23x) + (e1y * c23y)` | `8192 m^7` | 461 | 8 | 9 |
| `PointTwoTrianglePlanesEdgePlane` | `w` (a sign factor) | `12288 m^7` | 462 | 8 | 9 |
| `PointTwoTrianglePlanesEdgePlane` | `f1 * c23x` and 5 more of its shape | `12288 m^8` | 526 | 9 | 10 |
| `PointTwoTrianglePlanesEdgePlane` | `(f1 * c23x) + (f2 * c31x)` and 2 more of its shape | `24576 m^8` | 527 | 9 | 10 |
| `PointTwoTrianglePlanesEdgePlane` | `f3 * c12x` and 2 more of its shape | `12288 m^8` | 526 | 9 | 10 |
| `PointTwoTrianglePlanesEdgePlane` | the determinant (sign factor 1) | `36864 m^8` | 528 | 9 | 10 |
| `DirectionTwoTriangles` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DirectionTwoTriangles` | `u1x`, `u1y`, `u1z`, `v1x`, `v1y`, `v1z`, `u2x`, `u2y`, `u2z`, `v2x`, `v2y`, `v2z` | `2 m` | 65 | 2 | 2 |
| `DirectionTwoTriangles` | `u1y * v1z` and 11 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DirectionTwoTriangles` | `n1x`, `n1y`, `n1z`, `n2x`, `n2y`, `n2z` | `8 m^2` | 131 | 3 | 4 |
| `DirectionTwoTriangles` | `n1y * n2z` and 5 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `DirectionTwoTriangles` | the determinant (sign factor 1) | `128 m^4` | 263 | 5 | 6 |
| `DirectionTriangleEdge` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DirectionTriangleEdge` | `u1x`, `u1y`, `u1z`, `v1x`, `v1y`, `v1z`, `u2x`, `u2y`, `u2z`, `v2x`, `v2y`, `v2z` | `2 m` | 65 | 2 | 2 |
| `DirectionTriangleEdge` | `u1y * v1z` and 11 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DirectionTriangleEdge` | `n1x`, `n1y`, `n1z`, `n2x`, `n2y`, `n2z` | `8 m^2` | 131 | 3 | 4 |
| `DirectionTriangleEdge` | `u2y * n2z` and 5 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `DirectionTriangleEdge` | `e2x`, `e2y`, `e2z` | `32 m^3` | 197 | 4 | 5 |
| `DirectionTriangleEdge` | `n1y * e2z` and 5 more of its shape | `256 m^5` | 328 | 6 | 7 |
| `DirectionTriangleEdge` | the determinant (sign factor 1) | `512 m^5` | 329 | 6 | 7 |
| `Centroid` | every input component | `m = 2^255 - 1` | 255 | 4 | |
| `Centroid` | `wbc`, `wac`, `wab` | `m^2` | 510 | 8 | 8 |
| `Centroid` | `wbc * a.x` and 2 more of its shape | `m^3` | 765 | 12 | 12 |
| `Centroid` | `(wbc * a.x) + (wac * b.x)` | `2 m^3` | 766 | 12 | 13 |
| `Centroid` | the determinant (sign factor 1) | `3 m^3` | 767 | 12 | 13 |
| `Centroid` | `a.w + a.w` | `2 m` | 256 | 4 | 5 |
| `Centroid` | `w3` | `3 m` | 257 | 5 | 5 |
| `Centroid` | `w` (a sign factor) | `3 m^3` | 767 | 12 | 13 |
| `Orient3dInputDiffs` | every input component | `m = 2^62 - 1` | 62 | 1 | |
| `Orient3dInputDiffs` | `u.y * v.z` and 5 more of its shape | `m^2` | 124 | 2 | 2 |
| `Orient3dInputDiffs` | `m1`, `m2`, `m3` | `2 m^2` | 125 | 2 | 3 |
| `Orient3dInputDiffs` | `t1`, `t2`, `t3` | `2 m^3` | 187 | 3 | 3 |
| `Orient3dInputDiffs` | `t1 + t2` | `4 m^3` | 188 | 3 | 4 |
| `Orient3dInputDiffs` | the determinant | `6 m^3` | 189 | 3 | 4 |
| `TrianglePlane` | scratch of one call (sum over the values; lengths only: 84) | | | 59 | 70 |
| `EdgePlane` | scratch of one call (sum over the values; lengths only: 165) | | | 100 | 120 |
| `PointEdgeTrianglePlane` | scratch of one call (sum over the values; lengths only: 271) | | | 156 | 177 |
| `PointSegments` | scratch of one call (sum over the values; lengths only: 115) | | | 85 | 93 |
| `PointThreeTrianglePlanes` | scratch of one call (sum over the values; lengths only: 908) | | | 467 | 547 |
| `PointTriangleTwoEdgePlanes` | scratch of one call (sum over the values; lengths only: 1298) | | | 625 | 723 |
| `PointTwoTrianglePlanesEdgePlane` | scratch of one call (sum over the values; lengths only: 1103) | | | 546 | 635 |
| `DirectionTwoTriangles` | scratch of one call (sum over the values; lengths only: 195) | | | 123 | 150 |
| `DirectionTriangleEdge` | scratch of one call (sum over the values; lengths only: 288) | | | 168 | 204 |
| `Centroid` | scratch of one call (sum over the values; lengths only: 112) | | | 105 | 109 |
| `Orient3dInputDiffs` | scratch of one call (sum over the values; lengths only: 44) | | | 33 | 38 |
<!-- end of the generated table -->

Reading of the table for the design:
- triangle plane: `N` 3 limbs (`8 m^2`, 131 bits), `D` 4 limbs (`24 m^3`, 197 bits);
- edge plane: `E` 4 limbs (`32 m^3`), `F` 5 limbs (`96 m^4`);
- EdgePlane point on a triangle plane: `x` 5 limbs (`96 m^4`, 263 bits), `w` 4 limbs (`96 m^3`): before the
  reduction a point can exceed `FixedPoint3`'s 255 bits, so the construction works in the arena and only the reduced
  point is admitted (2 % of the points stay beyond 4 limbs after the reduction on `cgal`, section 2.1);
- Segments point: `x` 4 limbs (`12 m^3`), `w` 3 limbs;
- Planes point: three triangle planes `x` 8 limbs (`9216 m^7`), `w` 7 limbs; two triangle planes and an edge plane
  `x` 9 limbs (`36864 m^8`), `w` 8 limbs; a triangle plane and two edge planes (the worst reachable case) `x` 10 limbs
  (`147456 m^9`, 594 bits), `w` 9 limbs; scratch 625 / 723 limbs (5.0 / 5.8 KB on the stack, the largest arena of this
  design);
- direction of two triangle planes: 5 limbs (`128 m^4`); of a triangle plane and an edge plane: 6 limbs (`512 m^5`,
  within the 8 limbs `compare_along_fixed` accepts);
- centroid: 12 limbs (`3 m^3`, 767 bits);
- kernel B: minors 2 limbs (`2 m^2`, 125 bits), terms and sum 3 limbs (`6 m^3`, 189 bits).

The independent review of these bounds (section 10) re-derives every row by hand from the code and the spec.

## 6. Plan, measurement and stop rule

Each step is one commit series with its tests, measured A/B on 4 cores (`perf/run_classes.sh`, two alternating
rounds, with the new `nreal` class) and checked with `perf/exact_hashes.sh` (0 differences in 263 runs) before the
next one starts. A step that gains nothing measurable is reverted and documented here, as #248's step B was.

1. **A1 + A2** (pair search). Tests: the differential test of 4.1; `touch_only` against the original on random and
   adversarial coplanar meshes; the mutation shard of the arrangement.
2. **B** (kernel). Tests: against `orient3d_bigint` on random inputs of every difference length up to the admission
   limit, all differences at `±(2^62 - 1)`, exactly coplanar inputs, subnormal and huge exponents (fallback), mixed
   exponents; the portable `MulHigh` path; a mutation run on `math/ExactPredicates.cs`.
3. **C + 4.4** (points, gcd). Tests: the limb gcd against `BigInteger.GreatestCommonDivisor` (powers of two, odd parts
   of 1-12 limbs, zeros); every construction against the `BigInteger` code on random inputs at the bounds; whole
   booleans with the fixed path forced off, bit-identical; the invariance tests over `Similarity.All()`.
4. **D** (planes, directions). Tests: the limb -> double conversion against `(double)BigInteger`; planes and
   directions against `BigInteger`.
5. **E** (centroid), measured on n-ary and `nreal`.

**Stop rule** (the brief's): at least 3 % on `cgal` + Thingi10K together on 4 cores, or a clear n-ary gain the
owner accepts. If the steps together stay below it, the library changes are not merged and this document records the
numbers.

**Acceptance:** `exact_hashes.sh` 0 differences; `run_classes.sh` A/B two rounds plus `nreal`; independent review of
the code and of every bound; mutation runs (arrangement shards, `mesh-fixed`, and the new code); the analyzers of
#225 (no FMA / `float` / parallel loop in the exact files; no new parallel loop is planned); netstandard2.0 / C# 7.3;
no API change; the full local CI list of CLAUDE.md.

## 7. The decisions in detail

1. **Order.** The brief lists the constructions first and the kernel and shortcut as their first users. Measured, A2
   and B do not depend on C-E (B works on input doubles; A2 on the existing predicates) and have the larger expected
   gain on `cgal` / Thingi10K; C-E carry most of the new bounds and code. Proposed: A, B, then C, D, E.
2. **A2** is the new item. Without it, A1 alone stays below 1 % and would be dropped by the stop rule. Option (a) of
   4.1 needs a refactoring in stage R's file; option (b) duplicates 60 lines of exact 2D code.
3. **B without a cache.** The brief's "plane in fixed width" for the kernel becomes the measured variant B2: the
   per-call kernel is simpler (no per-triangle state, every caller of `Orient3d` gains, including
   `IntersectsBeyondShared`, `Reducer` and `MeshRepairMetrics`), and the minors are about a third of its cost.

## 8. New n-ary class from real data: `nreal`

Generated by [perf/nreal.py](../../benchmarks/BooleanComparison/perf/nreal.py) (new `BooleanComparison components`
and `array` commands; `profile` runs the one-pass union of all `N*.off` of a case directory). Never committed.

- **15 multi-component Thingi10K meshes**, each split into its connected components (3-26 inputs, 3k-58k triangles):
  closed, edge-manifold, without duplicated faces, with self-intersections (overlapping shells), drawn with a fixed
  seed. One (68731) unites to an empty result (its shells are inverted); it is kept, since the selection is
  reproducible and it still runs the whole pipeline.
- **3 arrays of one part**: 8 rotated, shifted copies of the CGAL meshes `fandisk`, `pinion`, `anchor` on a ring,
  each overlapping its neighbours.
- `perf/cases-nreal.txt`; `run_classes.sh` runs it as a class of its own (`nreal.jsonl`), `summary.py` reports it.

Base (head of #248, 4 cores, `profile --repeat 3`, two rounds): 18 runs, total 2.21 / 2.42 s, median per case
86 / 103 ms, GC 3 %, 61 MB per run; the pair search is 37 % of it. Serially it is the class with the largest share
of coplanar pair tests (section 2.3).

## 9. Reproduction

Data as in section "How to reproduce" of the hand-over (cgal and `cherchi` with `data/fetch.py`, the 32 Thingi10K
cases exported from their meshes), checked against the reference: `exact_hashes.sh` on the base gives
`263 runs before, 263 after, 0 differ`. The probe and the serial script are temporary and not committed; the probe
instruments `compute_planes`, `compute_edge_planes`, `compute_exact` (with `ExactPoint3.Create` separately),
`centroid`, the `Direction` constructions, the exact stages of `Orient` / `InCircle` / `ComparePoints` /
`compare_along` and of the winding `orient3d`, `orient3d_exact`, and the `IntersectsBeyondShared` call of
`touch_only` (with the coplanarity of the pair classified by extra `Orient3d` calls outside the timed scopes).

```sh
# serial_run.sh <tool dir> <out dir>
export PROBE_SERIAL=1 DOTNET_PROCESSOR_COUNT=1          # PROBE_SERIAL: gParallel.SerialOnThisThread around Compute()
for c in cases-cgal.txt cases-thingi.txt cases-cherchi.txt cases-nreal.txt; do
  taskset -c 0 dotnet $T profile --repeat 1 $P/$c > $O/$c.jsonl; done
taskset -c 0 dotnet $T profile --repeat 1 grid:5 grid:10 spheres:20 > $O/nary.jsonl
```

## 10. Review of the bounds

An independent agent (given the spec, the generated table, section 4.2 and the code, not this document's reasoning)
re-derived every row with its own evaluator of the value and length rules: **all bounds, bits, limbs and scratch
numbers confirmed**, and the spec matches the code's order and grouping for every entry. Kernel B confirmed: every
coordinate is a multiple of `2^e` (subnormals included), TwoDiff gives `hi`, `lo` as multiples of `2^e`,
`|hi · 2^-e| < 2^62` implies `|difference| ≤ 2^62 - 2^8`, minors < 2^125, sum < 2^189, same sign as `orient3d_bigint`.
Its findings, all fixed in this version:
- the direction `N_T × E` (from `line_direction` with an edge-plane line) was missing: added, 6 limbs;
- the "worst case" of three edge planes and the EdgePlane point on an edge plane cannot occur in the arrangement:
  replaced by the reachable TEE and TTE cases (TEE: `x` 10, `w` 9 limbs, matching the reviewer's derivation);
- the fallback of kernel B stated exactly (`e < -1023`; infinity and NaN fail the admission); the scale factor against
  `orient3d_bigint` is `2^(3(e - emin))`;
- 6 + 6 `MulHigh`, not 9 + 6;
- scratch-only differences between the spec and a literal transcription of the code (`W = 1` products, recomputed
  differences in `edge_plane`) and the public `EdgePlanePoint` / `PlanesPoint` (stay on `BigInteger`), section 4.3.

## 11. Later queue

- **GC trace on `cherchi` (4 cores, GC 23 % of the wall time) after the constructions:** `dotnet-trace collect
  --profile gc-verbose` with the analyzer of the hand-over; CSR / struct arrays for the per-triangle collections are
  proposed only if they are a large share of the promoted bytes (hand-over section 0, item 5).
- Small items from the reviews, only where free: the direction limbs in `compare_along` (4.5 makes it free), the
  per-item stack in `SelfPairs` (#248).

## 12. Results

**Machine noise.** On this machine the same build varies by up to 2× between processes (microbenchmark of one
predicate: 65 to 147 ns for the same code in two processes), so the class runs below are reported with the sum over
cases of each case's minimum over all rounds, next to the round totals. The serial probe and microbenchmarks carry
more weight.

**Step B (Orient3d kernel), commit 0e13d86: kept.**
- `perf/exact_hashes.sh`: `263 runs before, 263 after, 0 differ`.
- 4 cores, `~/ab.sh` = `exact_hashes.sh`, then `run_classes.sh` A B A B (A = head of #248, B = 0e13d86):

  | class | A rounds | B rounds | sum of per-case minima A -> B |
  |---|---|---|---|
  | `cgal` | 9.92 / 9.98 s | 9.52 / 9.98 s | 9.54 -> 9.31 s (−2.4 %) |
  | Thingi10K | 9.09 / 9.06 s | 8.85 / 9.21 s | 8.79 -> 8.67 s (−1.4 %) |
  | `nreal` | 2.30 / 2.24 s | 2.35 / 2.27 s | 2.15 -> 2.16 s (+0.8 %) |
  | `cherchi` | 22.35 / 21.22 s | 21.66 / 21.36 s | (no exact Orient3d stages) |

- Serial (one core, one thread), 6 alternating rounds of `cgal` and Thingi10K (`profile --repeat 3`): sum of per-case
  minima `cgal` 15.57 -> 15.70 s (+0.8 %), Thingi10K 15.31 -> 15.11 s (−1.4 %); round means −0.4 % / −1.4 %.
- Serial probe on `cgal` (the step-0 probe with B merged): the exact stage costs 260 -> 176 ns per call in place
  (7.73M calls, 2008 -> 1362 ms, about 2 % of the serial time); the machine was slower than in section 2 at the time
  (31.5 s against 24.6 s for the same probed build).
- Microbenchmark (exactly coplanar float inputs, `ExactPredicates.Orient3dExact`): 218 ns -> 65-70 ns at best. In place
  the kernel takes about 135 ns (cache and branch effects of the real inputs).
- Tests: `ExactOrient3dKernelTests` (random inputs of every difference length, coplanar float inputs, the 2^62 bound,
  TwoDiff with a nonzero low part, extreme exponents, invariance over `Similarity.All()`, the portable trailing-zero
  count); 11 of 11 hand mutants killed (carries, borrows, negations, admission bound and range, the lowest-bit
  exponent, the zero test of the sign, the portable trailing zeros).
- Independent code review: no defect; the arithmetic, the admission (NaN, infinity, subnormals, the exponent range)
  and the sign convention confirmed.

**Step A1 (coplanar shared edge), commit 3e4af50:** built and tested (`ExactTouchOnlyCoplanarTests`: more than 1000
shared-edge pairs on generated coplanar patches against `IntersectsBeyondShared`, folds, invariance over
`Similarity.All()`); independent review: same points, corner order, projection and polarity as the original branch.
Its measured bound is 0.4-0.7 % (section 2.3), below what the class runs can resolve; it is measured together with A2.

**Step D1 (triangle planes and edge-plane approximations in fixed width), commit 1dc39cc: kept.**
- `perf/exact_hashes.sh`: `263 runs before, 263 after, 0 differ`.
- 4 cores, `~/ab.sh` (A = 974ced0, i.e. B + A1; B = 1dc39cc):

  | class | A rounds | B rounds | sum of per-case minima A -> B |
  |---|---|---|---|
  | `cgal` | 9.63 / 9.53 s | 9.05 / 8.69 s | 9.16 -> 8.48 s (−7.5 %) |
  | Thingi10K | 8.74 / 8.53 s | 8.81 / 8.54 s | 8.35 -> 8.28 s (−0.8 %) |
  | `nreal` | 2.33 / 2.18 s | 2.32 / 2.08 s | 2.10 -> 2.05 s (−2.3 %) |
  | `cherchi` | 22.76 / 23.19 s | 21.33 / 19.61 s | 21.98 -> 19.55 s (−11.1 %) |

  `cgal` + Thingi10K: 17.51 -> 16.76 s (−4.3 %), above the 3 % bar on its own.
- Serial, 4 alternating rounds (`~/serial_ab2.sh`): sum of per-case minima `cgal` 15.53 -> 15.03 s (−3.2 %), Thingi10K
  15.48 -> 14.64 s (−5.4 %).
- Tests: `ExactPlanesFixedWidthTests` (the plane, axis, sign and the 16 approximations against the BigInteger code for
  corners of every size up to 2^255 - 1, degenerate triples; the limb -> double conversion against `(double)BigInteger`
  up to 17 limbs and beyond 2^1024; the arrangement's planes with fixed width on and off; invariance over
  `Similarity.All()`); 9 of 10 hand mutants killed (the survivor, `bits <= 52` for `bits <= 53`, is equivalent: the
  other branch gives the same exact value at 53 bits).

**Step A2 (coplanar neighbours with a shared vertex or edge), commits 8a11fa4 + cf775b1 (+ d6143f7 tests): kept.**
- `ExactTriangleIntersection.IntersectsBeyondSharedCoplanar` (internal, additive, agreed with stage R after #249
  merged): the coplanar branches of `IntersectsBeyondShared` and `SegmentIntersectsTriangle` without the Orient3d
  calls that are 0. `touch_only` takes it when all non-shared corners of one triangle lie in the other's plane
  (`one_side_except_shared` now evaluates all non-shared corners). It replaces A1's copied shared-edge branch, which
  used the floating-point `dominant_axis` and so predated #249's exact `plane_axis`.
- `perf/exact_hashes.sh`: `263 runs before, 263 after, 0 differ`.
- 4 cores, `~/ab.sh` (A = d17ba68, i.e. D1 merged with master; B = cf775b1); sum of per-case minima: `cgal` 8.43 ->
  8.55 s (+1.4 %), Thingi10K 8.18 -> 7.82 s (−4.5 %), `nreal` 2.05 -> 1.95 s (−4.7 %), `cherchi` 19.51 -> 19.26 s
  (−1.3 %).
- Serial, 4 rounds: `cgal` 14.94 -> 14.80 s (−0.9 %), Thingi10K 14.50 -> 14.82 s (+2.2 %), `nreal` 4.20 -> 3.81 s
  (−9.3 %).
- Below the step-0 bound (7-8 % serially): step B had already made the skipped exact stages cheaper (190 -> about
  135 ns each in place), and the coplanar path keeps its 2D tests and projection-axis choices. The gain shows on
  `nreal` (coplanar CAD neighbours in many self-pairs); on `cgal` / Thingi10K it is within the noise.
- Independent review: no defect; it added the tests for a shared vertex with one corner in the plane and one off it
  (both corner orders) and for the #249 sliver through the coplanar entry. Hand mutants of the new zero handling in
  `one_side_except_shared`: 2 of 2 killed.

**Finding for step C (the gcd on limbs):** the design of 4.4 assumed that a binary gcd is cheap because the odd part of
the gcd is small. It is not: the cost of a binary gcd follows the operands (150-216 bits after the common power of two
is shifted out), not the result. A prototype limb binary gcd took about 4 µs for the three gcds of a point, the same as
`BigInteger` (4-5 µs, where every operation costs about 1 µs of overhead at these sizes). A real gain needs Lehmer's
algorithm (single-word cofactor steps, with a multi-limb division as fallback), considerably more new exact code
for an expected 2-4 % serially, 1-2 % on 4 cores.

**Owner decision: no Lehmer; step C stops** (the track is A + B + D). Reasons: a binary gcd costs what its operands
cost, not what its small result suggests; `BigInteger` costs about 1 µs per operation at these sizes, so the gcd is
mostly overhead that only a Lehmer-type limb gcd would remove; and that is a lot of new exact code (and review,
mutation and bounds work) for an expected 1-2 % on 4 cores.

**Step D2 (directions in fixed width): not built** (process rule 4, upper bound first). The directions are 1.9 / 2.8 %
of the serial time on `cgal` / Thingi10K (section 2.1), and their exact values are almost never needed
(`compare_along` decides 99.95 % of its calls with the filter), so only the three doubles would get cheaper: at most
about half of the share, 1-1.4 % serially and less on 4 cores, below the noise of the class runs.
