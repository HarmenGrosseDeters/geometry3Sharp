# Exact sign stages on stack limbs (candidate B of the exact-number study)

Status: built (2026-11-13): `math/ExactLimbSigns.cs` and its seven call sites in `ExactMeshBoolean`. Study: branch
`runs/exact-numbers`, `REPORT.md` sections 3 (P3b) and 5b (the probe).

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

## 5. Sources

Own work on the library's `FixedArith` (`math/FixedPoint3.cs`); the stages and their filters are those of
`ExactMeshBoolean` (docs/design/EXACT_BOOLEAN_TAIL_55928.md).
