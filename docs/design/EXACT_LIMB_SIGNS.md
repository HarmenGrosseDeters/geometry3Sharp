# Exact sign stages on stack limbs (candidates B and D of the exact-number study)

Status: built (2026-11-13): `math/ExactLimbSigns.cs`, its seven call sites in `ExactMeshBoolean` (B, sections 1 to
4) and its two in `ExactMeshArrangement` (D, section 5). Study: branch `runs/exact-numbers`, `REPORT.md` sections 3
(P3b), 5b and 5e (the probes).
Sections 6 and 7 measure candidate C's classification part again; neither probe is built (section 6's form was
declined, section 7's is a no-go).

## 1. What and why

The classification of `ExactMeshBoolean` decides three kinds of signs exactly where its floating-point filters do not:

| stage | expression | call sites |
|---|---|---|
| plane side | `A X + B Y + C Z - D W` (a triangle's plane, a homogeneous point) | `interior_side`, `local_side`, `side`, `front_crossings` (two) |
| normal dot | `NX UX + NY UY + NZ UZ` | `carried_crossings` (two) |
| orientation | the 4 x 4 determinant of four homogeneous points | `orient3d` (through `orient3d_exact`) |

Today each is a BigInteger expression: every product and sum is a heap object. On the slow tail these stages are a
large part of the operation and most of its allocation (42327, one thread, the collections a stage triggers included:
side 12.6 %, orientation 4.9 % of the operation; 13 of 21 GB).

`ExactLimbSigns` evaluates the same expressions with the library's `FixedArith` (sign-magnitude limbs, a stack arena):
the operands are copied from their BigInteger storage into stack limbs per call, the products and sums live in a
stack arena, and only the sign leaves. The integers are the same, so the signs are the same; nothing is allocated.

## 2. The kernels

- `load`: `BigInteger.TryWriteBytes` (two's complement, little endian) into `limbs_of(v)` limbs, sign-extended; a
  negative value is negated in place (invert, add one) to its magnitude. `limbs_of(v)` is the byte count of the two's
  complement rounded up to limbs, so the write always fits.
- `PlaneSide` (with `W`, and for an integer point, `W = 1`: the far point of a ray), `Dot`: the products and sums in
  the order of the expression.
- `Det4`: the expansion along the first row with 3 x 3 minors, as `ExactMeshArrangement.Det4`.
- Scratch space. `FixedArena` never frees; a product takes the sum of its factors' limbs, a sum or difference at most
  one limb more than its longer operand (and results are trimmed, so these are upper bounds). `Take` throws when the
  arena is exhausted, so an error here is an exception, not a write outside the buffer. With `total` the sum of the
  operands' limbs (`limbs_of`, at least their lengths):
  - plane side: the four products take `total` limbs in all; the three sums take at most `max + 1`, `max + 2` and
    `max + 3` limbs, where `max` is the longest product, at most `total`: the need is at most `4 total + 6`. The
    integer-point form (three products, the last operand subtracted as it is) and the dot (three products, two sums:
    `3 total + 3`) need less. Allocated: `4 total + 16` (`SideScratch`), 10 limbs of margin.
  - `Det4`, with `a`, `b`, `c`, `d` the limbs of the widest entry of each row. The expansion is along the first row,
    so a minor is built from the rows `b`, `c`, `d`: its three terms take two products of `c + d`, their difference
    `c + d + 1` and the product with an entry of `b`, `b + c + d + 1`: `3 (4 (c + d) + b + 2)`; the two sums of the
    terms take `b + c + d + 2` and `b + c + d + 3`: a minor needs `5 b + 14 (c + d) + 11` and has at most
    `b + c + d + 3` limbs. The four minors: `20 b + 56 (c + d) + 44`. The four products with the first row:
    `4 (a + b + c + d + 3)`. The three sums of the four terms (the first term is added to zero, which takes nothing):
    `3 (a + b + c + d + 3) + 1 + 2 + 3`. In all at most `7 a + 27 b + 63 (c + d) + 71`, for rows of one width `w`
    `160 w + 71`. Allocated: `8 a + 28 b + 64 (c + d) + 96` (`DeterminantScratch`; `164 w + 96`), a margin of
    `a + b + c + d + 25` limbs.
  The kernels report the limbs they took from the arena to the tests (an `out` overload), which check that number
  against these bounds on every call they make: on the random operands, with one wide operand or row beside small
  ones, and at the largest operands (`LargestOperands_StayInsideTheDerivedScratchNeed`: all bits set, and rows of
  25 limbs of random bits, whose determinant takes at least 4000 of the 4071 limbs derived; the difference is the
  limbs of sums that do not carry, since an operand of `n` limbs has its sign bit clear).
- Stack limits. A call uses the BigInteger expression when its stack need is above a limit; so does netstandard2.0
  (no `TryWriteBytes` on a span).
  - plane side and dot: the sum of the operands' limbs above `MaxOperandLimbs` = 320 (eight operands of 2560 bits,
    or one wide operand beside small ones). The stack of a call is at most 320 limbs of operands and 1296 of scratch
    space: 13 KB.
  - `Det4`: the operands' limbs and the scratch space together above `MaxDeterminantLimbs` = 4608: 36 KB. Rows of
    one width are on the stack up to 25 limbs (1600 bits); a wide first row costs 12 limbs per limb, a wide second
    row 32, a wide third or fourth row 68.
  The kernels are leaves: they call only `FixedArith`, which does not recurse and has no stack buffers of its own, so
  a call adds its own buffers once to the stack of its caller. The callers are the classification loops of
  `ExactMeshBoolean` (`interior_side`, `local_side`, `side`, `front_crossings`, `carried_crossings`, `orient3d`),
  which run on the calling thread or on thread-pool threads (1.5 MB of stack by default) and do not recurse through
  these calls. The limits are compared in `long`, so operands of any size take the BigInteger path without an
  overflow of the sizes.

Exact code: integer arithmetic only, no floating point, no parallel loop (`math/Exact*.cs` in `.editorconfig`).

## 3. Measured

Before = master 683fb2c, after = the branch on it. 4-core cloud container, .NET SDK 10.0.115, Release, workstation
GC; 5 alternating runs, medians (range of the 5 runs in brackets); wall time with collections, the collector's pause
and the allocation separately. Rows: branch `runs/exact-numbers`, `results/b2` (the probe before the build, with a
prototype of the kernels on c0f8aa9: `REPORT.md` section 5b, the same picture).

| stratum | operation | before | after | change | GC pause | allocated |
|---|---|---|---|---|---|---|
| slow tail | 42327 with its rotated copy, union | 24.3 s (24.2-25.1) | 20.7 s (19.9-20.8) | -15.1 % | 6.1 -> 3.3 s | 20.4 -> 7.6 GB |
| slow tail | 42327, intersection | 23.9 s (23.5-24.4) | 19.8 s (19.2-21.1) | -17.0 % | 6.0 -> 3.0 s | 20.2 -> 7.3 GB |
| slow tail | 42327, difference | 23.6 s (22.6-24.1) | 19.7 s (19.2-20.2) | -16.5 % | 6.1 -> 3.2 s | 20.2 -> 7.4 GB |
| slow tail | 107554, union | 21.2 s (20.4-21.4) | 20.0 s (19.5-20.8) | -5.6 % | 5.1 -> 4.3 s | 10.7 -> 9.2 GB |
| slow tail | 107554, intersection | 16.5 s (15.6-16.8) | 15.7 s (15.5-16.0) | -4.8 % | 4.9 -> 4.2 s | 10.0 -> 8.5 GB |
| slow tail | 107554, difference | 16.2 s (15.6-16.5) | 15.7 s (15.3-16.0) | -2.6 % | 4.9 -> 4.0 s | 10.0 -> 8.5 GB |
| normal | 20 pairs, 60 operations (sum of the medians) | 6.00 s (5.83-6.25) | 5.84 s (5.77-5.97) | -2.7 %, inside the run-to-run range | 0.11 -> 0.11 s | 2.36 -> 2.36 GB |
| normal | 13 models with a rotated copy, 39 operations | 4.15 s (3.99-4.43) | 4.02 s (3.83-4.17) | -3.3 %, inside the range | 0.10 -> 0.10 s | 1.48 -> 1.48 GB |

Result hashes and statuses are equal in every run, and in the 61 units of the #711 class (one run each; they do not
reach these stages: 280.5 -> 288.0 s, allocation 243.4 -> 242.6 GB, single runs). On 42327 the ranges of before and
after do not overlap; about three quarters of its gain is the collector's pause (12.8 GB less allocation), and on
107554 nearly all of it. On one thread (the probe) the orientation stage drops from 3.4 to 1.9 s and the side stage
only from 9.0 to 8.1 s: loading eight operands per call costs about what the BigInteger temporaries cost. The CPU
gain of the side stage (2 x in the micro benchmark) needs the limbs stored with the points (candidate C, below).

The table is the first version of the kernels (af87765). The limits and the determinant's scratch space of section 2
came with the review; against af87765 on the slow tail (5 alternating runs, the same conditions, `results/b3`) the
medians of the six operations change by -2.2 to +3.0 % with overlapping ranges, the allocation is the same to the
megabyte (no call of these models moved to the BigInteger path) and the hashes are equal.

`centroid_point` on stack limbs with the result converted back to a BigInteger point (candidate C, part 1) was probed
on the prototype and not built: slow tail -5.2 to +3.6 % per operation with overlapping ranges (4 cores, wall time
with collections, median of 5), normal strata unchanged, one thread 0.8 to 4.2 % slower; allocation -0.3 GB per
operation. The conversion back costs what the temporaries cost (`results/c`).

What points kept as limbs could remove at most (candidate C, part 2; one thread, timers on, a region's time with the
collections it triggers; study `REPORT.md` section 5d, `results/c3`): the exact point construction of the
arrangement is 2.4 % / 4.9 % of the normal stratum's time (20 pairs / 13 models with a rotated copy), half of it
arithmetic and 40 % the gcd reduction, and 5.7 % / 1.7 % on 107554 / 42327; the centroid is 6.0 % / 7.5 % of the
tail's time and 10.9 % / 23.2 % of its allocation; the loads of the stages of this note about 1.7 % / 7.1 %. On the
normal stratum the bound itself is at the run-to-run range, so the construction on limbs is not built. On the tail
three quarters of the bound are the classification (the centroid and the planes' coefficients kept as limbs); that
is parked until lever 1 (#767) has landed, which changes how often these stages run.

## 4. Tests

- `ExactLimbSignsTests`: the three kernels against the BigInteger expressions on operands of every width (small, at the
  limb boundaries, up to the stack limits and beyond them), with exact zeros and values one unit off; for every call
  the path it took (the stack, or the BigInteger expression: counted by the sum of the limbs, which selects it) and
  the scratch limbs it used against the derived need; the limits themselves (a sum of 320 limbs on the stack and 321
  not, for the three forms; 4608 limbs of the determinant on the stack and 4611 not; one operand of 300 limbs in each
  position; one wide row in each position, on both sides of the limit); all operands at the limit with all bits set;
  a common power of two of the plane or the point keeps the sign, a negative factor reverses it; no allocation inside
  the limit.
- `ExactBooleanLimbSignsTests` (existing entry points only, so the tests also build against the base library): open
  inputs (crossing grids, a sphere with holes) with the filters switched off, so that every decision takes an exact
  stage: the result is the filtered run's mesh triangle for triangle, for the three operations, and the union
  allocates 17 / 34 / 55 MB against 73 / 160 / 158 MB with the BigInteger expressions (bounds 40 / 80 / 100 MB); the
  same input far from the origin (the planes' offsets large against their normals).
- The existing tests of `ExactMeshBoolean` (filtered against unfiltered results, the invariance over
  `Similarity.All()`, reuse, parallel against serial) cover the operation, whose results do not change.

## 5. The arrangement's Orient and InCircle beyond the fixed width (candidate D)

`ExactMeshArrangement.Orient` and `InCircle` decide on fixed-width points with `FixedArith` (4 limbs per coordinate).
For points beyond that width their last stage was a BigInteger determinant: `Det3` of the rows `(v[i], v[j], v.W)`
for the orientation in the plane of the axes `i`, `j`, and for the in-circle test `Det3` of a row
`(A s, B s, A^2 + B^2)` per point `v` of the triangle against the fourth point `d`, with `A = v[i] d.W - d[i] v.W`,
`B` likewise with `j`, `s = v.W d.W` (the derivation is the comment in `InCircle`). On 55928 with its rotated copy
this stage runs 12.4 million times in one operation and owns nearly half of its allocation.

`ExactLimbSigns.Orient2` and `ExactLimbSigns.InCircle` evaluate the same determinants on stack limbs, with `load`
and `det3` of section 2. A call that does not run on the stack (the limit below, netstandard2.0) evaluates the
BigInteger determinant inside the kernel, as `Det4` does (`Orient2Big`, `InCircleBig` with the arrangement's former
`incircle_row`): a caller cannot take a call beyond the limit for a zero. The two call sites only multiply by the
projection's sign.

Scratch space (the rules of section 2: a product takes the sum of its factors' limbs, a sum or difference at most one
limb more than its longer operand):
- `Orient2`, with `a`, `b`, `c` the limbs of the widest entry of each row: one 3 x 3 determinant, as a minor of
  `Det4`: at most `5 a + 14 (b + c) + 11`. Allocated: `6 a + 15 (b + c) + 32` (`OrientScratch`).
- `InCircle`. For a point `v` of the triangle let `t` be the limbs of its widest entry plus those of `d`'s widest
  entry. Its row: `A` takes two products of at most `t` limbs and their difference of `t + 1`: `3 t + 1`, `B` the
  same, `s` at most `t`; `A s` and `B s` at most `2 t + 1` each, `A^2` and `B^2` `2 t + 2` each, their sum
  `2 t + 3`: `17 t + 11` for the row, whose entries have at most `2 t + 1`, `2 t + 1` and `2 t + 3` limbs. The
  determinant of the rows of `p`, `q`, `r` (with `t_p`, `t_q`, `t_r`, and `u = 2 (t_q + t_r) + 4`): each of its first
  two terms takes two products of at most `u` limbs, their difference `u + 1` and the product with an entry of the
  first row `2 t_p + u + 2`: `4 u + 2 t_p + 3`; the third term two products of `u - 2`, their difference `u - 1` and
  the product `2 t_p + u + 2`: `4 u + 2 t_p - 3`; the two sums of the terms `2 t_p + u + 3` and `2 t_p + u + 4`:
  `14 u + 10 t_p + 10 = 10 t_p + 28 (t_q + t_r) + 66`. With the three rows: at most
  `27 t_p + 45 (t_q + t_r) + 99`, for points of one width `m` (so `t = 2 m`) `234 m + 99`. Allocated:
  `28 t_p + 46 (t_q + t_r) + 128` (`InCircleScratch`).
- The limit is the one of `Det4`: operands and scratch space together at most `MaxDeterminantLimbs` = 4608 limbs
  (36 KB of stack), compared in `long`. Rows of one width are on the stack up to 101 limbs for `Orient2` and points
  of one width up to 17 limbs (1088 bits) for `InCircle`. The widest call of the measured models has 12 limbs for
  the two widest operands together (about 1,500 limbs of scratch space).
- The kernels are leaves: they call nothing but `FixedArith`, so the 36 KB are taken once on top of the caller of
  `Orient` / `InCircle` (the triangulation, `ExactMeshBoolean`, `ExactPlanarSimplify`), whoever that is.

Tests: `ExactLimbSignsTestsPlanar` (the kernels and their BigInteger forms against references written in the test:
the 3 x 3 expansion for the orientation and, for the in-circle test, the 4 x 4 determinant of the lifted points,
which is another formula than the kernel's; in the three projections, with collinear and cocircular points by
construction and one unit off them, other homogeneous factors, the path and the scratch use of every call; signs
known in advance (a counter-clockwise triangle, a point inside, on and outside its circle); the limits at 4608 and
4609 limbs and one wide row or point in each position on both sides of the limit, in each projection and with a
wider entry in the axis the determinant does not read; dense operands at the widest uniform size, which take at
least `33 w` of `33 w + 11` and `117 t` of `117 t + 99` limbs; translation, 2^k scale and homogeneous factors keep
the signs; no allocation) and `ExactBooleanWidePointTests` (through `ExactMeshBoolean` with every exact point kept
as BigIntegers and the filters off, so that every Orient and InCircle on a constructed point takes this stage: the
default mesh, triangle for triangle, for the three operations, under `Similarity.All()`, on a reused instance and
with the loops serial; the union allocates 7 / 11 / 28 / 17 MB against 20 / 32 / 73 / 36 MB with the BigInteger
determinants).

Measured: before = the kernels of sections 1 to 4 (d555e22), after = this section on it. 4-core cloud container,
.NET SDK 10.0.115, Release, workstation GC; alternating runs, medians (range in brackets); wall time with
collections, the collector's pause and the allocation separately. Rows: branch `runs/exact-numbers`, `results/d2`
(the probe before the build, a prototype on c0f8aa9: `results/d`, 371.0 -> 318.8 s on 55928) and `results/d4`.

| stratum | operation | runs | before | after | change | GC pause | allocated |
|---|---|---|---|---|---|---|---|
| slow tail | 55928 with its rotated copy, difference | 5 | 352.8 s (349.1-376.2) | 310.4 s (291.8-331.3) | -12.0 % | 78.4 -> 63.0 s | 180.1 -> 93.8 GB |
| slow tail | 107554, union | 5 | 23.5 s (23.2-25.1) | 23.5 s (22.1-24.3) | -0.1 % | 5.0 -> 5.1 s | 9.2 -> 8.8 GB |
| slow tail | 107554, intersection | 5 | 17.9 s (17.6-18.2) | 17.6 s (17.2-18.3) | -1.2 % | 4.7 -> 4.6 s | 8.5 -> 8.0 GB |
| slow tail | 107554, difference | 5 | 18.1 s (17.2-18.5) | 18.1 s (17.5-19.3) | 0.0 % | 4.7 -> 4.7 s | 8.5 -> 8.0 GB |
| slow tail | 42327, union | 21 | 24.4 s (23.0-27.9) | 24.7 s (23.2-27.8) | +1.2 % | 3.7 -> 3.7 s | 7.6 -> 7.4 GB |
| slow tail | 42327, intersection | 21 | 22.6 s (20.7-24.7) | 22.3 s (20.6-26.5) | -1.3 % | 3.7 -> 3.5 s | 7.3 -> 7.1 GB |
| slow tail | 42327, difference | 21 | 22.7 s (20.7-25.2) | 23.0 s (21.1-27.6) | +1.3 % | 3.6 -> 3.8 s | 7.4 -> 7.2 GB |
| normal | 20 pairs, 60 operations (sum of the medians) | 5 | 6.79 s (6.72-7.09) | 6.70 s (6.68-7.06) | -1.3 %, inside the run-to-run range | 0.14 -> 0.15 s | 2.36 -> 2.36 GB |
| normal | 13 models with a rotated copy, 39 operations | 5 | 4.50 s (4.38-4.74) | 4.43 s (4.30-4.66) | -1.5 %, inside the range | 0.13 -> 0.13 s | 1.48 -> 1.48 GB |

Result hashes and statuses are equal in every run. On 55928 (4 cores, collections included) the ranges of before
and after do not overlap; 15 s of the 42 s are the collector's pause (86 GB less allocation). On 107554 and on the
normal strata nothing changes beyond the run-to-run range: their points stay inside the fixed width, the stage is
hardly reached. 42327 does not reach it either (its allocation drops by 0.2 GB); its rows are 21 processes per
side (the 5 of the series with 107554 before it in the process, 5 with 107554 after it, 8 with 42327 alone, 3 of
`results/d4`), because its classification time spreads between processes: 12 to 16 s for the same work (all
counters, the allocation and the hash are equal), in 3 of 33 processes before and 5 of 33 after the median of the
three operations is 14.4 s or more, with more CPU time and the same collector's pause. In the first series of 5
that put three such processes on the after side (+2.1 / +9.1 / +10.3 % per operation), in the series with the
order of the models reversed two on the before side (the sum of the six operations 129.7 -> 124.1 s). The spread
is in the base and is not an effect of this change: it comes from the tiered JIT's dynamic PGO (#825: one union per
process, 11 of 40 processes at 14.4 s or more by default, 0 of 40 with `DOTNET_TieredPGO=0`).

## 6. Candidate C's classification part, measured again after lever 1 (2026-11-15): a go, for the cheap form

Section 3 parked the classification part of candidate C (the centroid and the planes' coefficients kept as limbs)
until lever 1 (#813) had landed. Measured on master 079e8258 (with #772, #813 and #893), 4-core cloud container,
.NET 10.0.15, Release, `DOTNET_TieredPGO=0` in every run (#825). Rows, scripts and the two local patches: branch
`runs/exact-numbers`, `results/c4` (bound), `results/c5` (probe), `c/`.

**Upper bound** (one thread on core 0, no concurrent GC; timers around `centroid_point` and around the operand
loads of the four kernels of section 2, the timers' own cost of 23 ns per timestamp subtracted; a region's time
includes the collections its allocation triggers; the whole region removed, which no implementation reaches):

| stratum | one-thread time | centroid | operand loads | bound | centroid's allocation |
|---|---|---|---|---|---|
| slow tail, 107554 with its rotated copy, 3 operations | 95.6 s | 6.7 s, 7.0 % | 2.1 s, 2.2 % | 9.2 % | 3.1 of 23.3 GB, 12.9 % |
| slow tail, 42327, 3 operations | 160.5 s | 12.4 s, 7.9 % | 12.7 s, 8.1 % | 16.0 % | 6.1 of 20.7 GB, 28.9 % |
| normal, 20 pairs, 60 operations | 8.9 s | 0.13 % | 0.02 % | 0.16 % | 0.1 % |
| normal, 13 models with a rotated copy, 39 operations | 6.2 s | 0.16 % | 0.01 % | 0.17 % | 0.1 % |

Lever 1 did not change how often these stages run on the two tail models: 2.39 and 3.78 million centroids, 7.9 and
50.1 million kernel calls; the counters of the classification (exact centroids, exact plane sides) are the ones of
13 November. The loads are half of
the kernels' time (12.7 of 27.4 s on 42327): `load` copies an operand through `TryWriteBytes` and turns two's
complement into a magnitude.

**Probe** (one binary, switched by an environment variable; not built, the patch is on the data branch):
- `fast`: `load` reads the operand's magnitude from `BigInteger`'s own limbs (its private fields `_bits` and
  `_sign`, through `UnsafeAccessor`; .NET keeps a sign and little-endian 32-bit limbs) instead of `TryWriteBytes`;
- `fast + centroid`: and the unreduced centroid of `centroid_point` on stack limbs, only its four results become
  BigIntegers (candidate C, part 1, which gained nothing with the old loads).

Three runs per setting, the settings rotated; medians, the range in brackets; the sum of the stratum's operations.

| stratum | setting | one thread, with collections | change | 4 cores, with collections | change | allocated |
|---|---|---|---|---|---|---|
| tail, 107554 | base | 99.7 s (93.0-99.8) | | 47.4 s (46.4-48.1) | | 23.3 GB |
| | fast | 99.1 s (96.6-104.9) | -0.6 % | 46.6 s (46.5-48.9) | -1.7 % | 23.3 GB |
| | fast + centroid | 94.2 s (93.0-99.0) | -5.5 % | 47.5 s (44.4-49.8) | +0.2 % | 22.5 GB |
| tail, 42327 | base | 153.7 s (152.2-161.6) | | 56.7 s (56.4-58.3) | | 20.7 GB |
| | fast | 148.0 s (139.8-155.0) | -3.7 % | 54.8 s (53.1-57.5) | -3.3 % | 20.7 GB |
| | fast + centroid | 143.4 s (139.4-147.6) | -6.7 % | 53.8 s (52.3-56.5) | -5.1 % | 19.9 GB |
| normal, 20 pairs | base | 8.26 s (7.89-8.71) | | 5.34 s (4.85-5.67) | | 2.26 GB |
| | fast | 8.14 s (7.80-8.24) | -1.5 % | 5.12 s (4.66-5.21) | -4.1 % | 2.26 GB |
| | fast + centroid | 8.68 s (7.29-8.80) | +5.1 % | 5.02 s (4.75-5.32) | -6.0 % | 2.26 GB |
| normal, 13 models with a rotated copy | base | 5.96 s (5.75-6.20) | | 2.93 s (2.90-2.98) | | 1.35 GB |
| | fast | 5.96 s (5.66-6.43) | 0.0 % | 2.83 s (2.75-2.95) | -3.4 % | 1.35 GB |
| | fast + centroid | 5.78 s (5.69-6.58) | -3.1 % | 3.03 s (2.74-3.06) | +3.3 % | 1.35 GB |

Result hashes and statuses are equal in all 72 runs (6 tail operations, 60 and 39 normal ones per run).

- Slow tail, one thread, collections included: the two models together 253.4 -> 237.6 s, -6.3 %. On 42327 the gain
  is outside the run-to-run range (the slowest run with the probe, 147.6 s, is below the fastest without, 152.2 s);
  its classification goes from 94.2 to 81.9 s. On 107554 the median is 5.5 % lower, but the ranges overlap (base
  93.0 to 99.8 s): not confirmed with three runs.
- Slow tail, 4 cores, collections included: 42327 -5.1 % (the ranges touch: 52.3-56.5 against 56.4-58.3), 107554
  no change.
- Normal strata, one thread and 4 cores: no change beyond the run-to-run range in either direction (every pair of
  ranges overlaps). The centroid runs 700 and 500 times in these strata and the kernels 1,200 and 800 times, so the
  probe cannot move them; the medians of 8 s and 3 s processes scatter by several percent.
- The probe reaches under half of the bound. The centroid still allocates its four results (0.9 of its 6.1 GB go
  on 42327) and the other consumers of a centre (`ToInputVector`, the ray of `front_crossings`, `carried_crossings`)
  still take BigIntegers.

**Recommendation: go for the cheap form, no-go for the restructuring.**

- Go: the two changes of the probe as one PR, about 80 lines in `math/ExactLimbSigns.cs` and three in
  `centroid_point`. Measured: -6.3 % of the slow tail's one-thread time on its two classification-heavy models
  (-6.7 % confirmed on 42327, -5.1 % there on 4 cores), nothing on the normal stratum. The 5 % line of the decision
  is met on the tail, with three runs per setting; the PR measures again with five.
- The open point is the owner's: `fast` reads private fields of `BigInteger`. They are `_sign` and `_bits` on
  .NET 10.0.15 (checked by reflection), but nothing promises that they stay. The PR would
  check the layout once at start (a value written through the public API against the fields read) and keep
  `TryWriteBytes` otherwise, so a changed runtime loses the gain and not the result; netstandard2.0 keeps the
  BigInteger expressions as today. Without that access the `fast` half (3.7 % of 42327) needs limb copies kept
  beside the planes and the centres, which is the restructuring below.
- No-go: limbs threaded through `Center` and its consumers and stored with the planes. It would have to bring the
  rest of the bound (at most 4 % of 107554 and 9 % of 42327, one thread), touches about ten call sites of the
  classification, and no probe of it exists. It is not worth building on these numbers.
- For the determinism proof: `ExactLimbSigns.cs` is outside the stage 0 scope today; when a later stage takes it
  in, the private-field access and the layout check become entries of its ledger.

Owner 2026-11-15: no access to `BigInteger`'s private fields. The cheap form is not built; section 7 measures the
route through the public API.

## 7. Limbs carried beside the planes and the centres, through the public API (2026-11-15): a no-go

The owner declined the access to `BigInteger`'s private fields of section 6 (2026-11-15: no `UnsafeAccessor`, no
reflection) and asked for one capped probe of the route that works on every target: the limbs converted once, at
creation, through the public API and carried beside the BigIntegers. This is the restructuring section 6 had no
probe of.

**Probe** (one binary, switched by an environment variable; not built; master 1f5e5e9, 235 added lines in three
files, the patch is `c/probe_carried_limbs.patch` on the data branch):
- `carried`: a plane's four coefficients and a centre's four coordinates are converted once (`TryWriteBytes`, the
  `load` of section 2) into one limb array kept with the `PlaneData` and the `Center`. Plane side (`local_side`,
  the two sites of `front_crossings`) and the normal dot (six sites) read these arrays; only add, subtract,
  multiply, sign and compare on limbs, the BigIntegers stay for every other consumer. The far integer point of a
  plane side and the operands of `Det4` are still loaded per call.
- `carried + centroid`: and the unreduced centroid on stack limbs as in section 6; its four results become the
  centre's BigIntegers and, without a conversion, its carried limbs.

With `carried + centroid` switched on, the 883 exact Boolean tests pass. Measured in a 4-core cloud container,
.NET 10.0.15, Release, `DOTNET_TieredPGO=0`; five runs per setting, the three settings back to back in each of five
rounds, their order rotated; medians, the range in brackets; the sum of the stratum's operations, collections
included. Rows and scripts: `results/c7`, `scripts/c7_probe.sh`, `analyze_c7.py`, `analyze_c7_rounds.py`.

| stratum | setting | one thread | change | its classification | 4 cores | change | allocated |
|---|---|---|---|---|---|---|---|
| tail, 107554 | base | 119.7 s (115.8-121.9) | | 59.6 s (57.2-62.0) | 52.3 s (49.1-56.7) | | 20.3 GB |
| | carried | 122.6 s (112.4-125.0) | +2.4 % | 61.3 s (55.8-61.8) | 56.3 s (52.2-59.0) | +7.6 % | 20.5 GB |
| | carried + centroid | 115.4 s (111.4-124.0) | -3.6 % | 56.9 s (55.3-62.4) | 54.4 s (48.3-58.1) | +4.0 % | 19.8 GB |
| tail, 42327 | base | 198.5 s (192.6-213.0) | | 119.5 s (116.5-130.1) | 68.0 s (65.3-76.7) | | 18.3 GB |
| | carried | 199.0 s (184.0-207.6) | +0.2 % | 115.9 s (105.9-125.9) | 69.8 s (63.6-72.7) | +2.7 % | 18.5 GB |
| | carried + centroid | 188.6 s (178.1-199.8) | -5.0 % | 107.9 s (101.3-113.2) | 71.4 s (60.1-73.9) | +5.0 % | 17.6 GB |
| normal, 20 pairs | base | 10.42 s (9.85-11.53) | | 0.51 s | 6.21 s (5.97-6.70) | | 2.26 GB |
| | carried | 10.71 s (10.40-11.88) | +2.8 % | 0.51 s | 6.51 s (5.98-6.80) | +4.9 % | 2.26 GB |
| | carried + centroid | 10.60 s (9.58-11.31) | +1.8 % | 0.48 s | 6.63 s (5.69-6.91) | +6.9 % | 2.26 GB |
| normal, 13 models with a rotated copy | base | 7.83 s (6.77-8.00) | | 0.28 s | 3.46 s (3.29-3.63) | | 1.35 GB |
| | carried | 7.88 s (6.96-8.15) | +0.6 % | 0.29 s | 3.49 s (3.28-3.86) | +0.8 % | 1.35 GB |
| | carried + centroid | 7.71 s (6.83-8.28) | -1.6 % | 0.28 s | 3.78 s (3.15-3.90) | +9.2 % | 1.35 GB |

Result hashes and statuses are equal in all 120 runs (3,150 operations, all `Success`).

This container is slower than the one of section 6 (42327 without the probe: 198.5 s against 153.7 s) and less
steady: the phases outside the classification, whose code is the same in all three settings, differ between the
settings of one round by -6 to +9 % on the tail models on one thread and by up to 20 % on 4 cores. A difference of
the total time below that is not measured here.

- `carried` alone, one thread: no gain in the median on either model (+2.4 %, +0.2 %), every pair of ranges
  overlaps. What the saved loads bring is not visible beside the conversion at creation and the scatter (the
  classification of 42327 is 3 % lower in the median, inside the range); it allocates 0.2 GB more.
- `carried + centroid`, one thread: 42327 -5.0 % in the median, at the 5 % line, and not confirmed: the ranges
  overlap (178.1-199.8 s against 192.6-213.0 s). Its classification, the only phase whose code differs, is
  confirmed lower, 119.5 -> 107.9 s (-9.7 %; the slowest run with the probe is below the fastest without), and the
  total is lower in each of the five rounds (-2.5 to -10.1 %). 107554 -3.6 %, inside the range. The two models
  together 318.2 -> 304.0 s, -4.5 %; section 6's probe with the private fields had -6.3 %.
- 4 cores: no gain; the medians are 4 and 5 % higher with the probe, inside the ranges.
- Normal strata: no change beyond the run-to-run range, one thread and 4 cores. Their classification is 0.2 to
  0.5 s per run and does not move; the differences of the medians sit in the phases with the same code.
- Allocation with `carried + centroid`: -0.5 GB on 107554, -0.7 GB on 42327 (the centroid's intermediates), nothing
  on the normal strata.
- What the probe leaves of section 6's bound (9.2 % / 16.0 % of the one-thread time, measured there and not again):
  about 6 % of 107554 and 11 % of 42327. It is the centroid's own operand loads, its arithmetic and its four
  BigInteger results (the bound counted the whole centroid as removed), the far point of a plane side and the
  operands of `Det4`. Reaching the loads would mean limbs stored with every `ExactPoint3`, the construction on
  limbs that section 3 did not build.
- netstandard2.0: not measured. The kernels of section 2 exist only under `#if NET`; a number on that target needs
  them ported (a `ToByteArray` load, no `TryWriteBytes`) and a probe built against that library, more than the 30
  minutes set for it, and with this result it would not change the decision.

**Recommendation: no-go.** The line was a gain of about 5 % of the slow tail's one-thread time, confirmed outside
the run-to-run range on 42327, without a regression on the normal strata. The carried limbs alone gain nothing;
with the limb centroid the median reaches the line on 42327 but is not confirmed there, stays under it on 107554
and on the two models together, and 4 cores show nothing. The effect on the classification is real, and a steadier
machine could put the total a little above or below 5 %; either way it is two models of the slow tail, for 235
lines of exact code, a limb array kept with every plane and centre, and twelve call sites. The classification part
of candidate C is closed with this; the BigInteger operands stay, and the kernels of sections 2 and 5 keep loading
them per call.

## 8. Sources

Own work on the library's `FixedArith` (`math/FixedPoint3.cs`); the stages and their filters are those of
`ExactMeshBoolean` (docs/design/EXACT_BOOLEAN_TAIL_55928.md).
