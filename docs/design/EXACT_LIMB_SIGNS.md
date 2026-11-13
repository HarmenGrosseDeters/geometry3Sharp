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
- Scratch space, in limbs, for operands of at most `w` limbs (`FixedArena` never frees; `Take` throws when it is
  exhausted, so an error here is an exception, not a write outside the buffer):
  - plane side and dot: the products take the sum of the operands' limbs, each sum at most a product and 3 limbs:
    `4 total + 16`;
  - `Det4`: a minor takes `33 w + 11` (six products of `2 w`, three differences of `2 w + 1`, three products of
    `3 w + 1`, two sums of at most `3 w + 3`), the four minors `132 w + 44`, the four products with the first row
    `4 w + 3` each, the four sums at most `4 w + 5` each: `164 w + 96`.
  `LargestOperands_FitTheScratchSpace` runs every kernel with all operands at the limit and all bits set.
- Operands wider than `MaxLimbs` = 40 limbs (2560 bits; `Det4` then takes 53 KB of stack) and netstandard2.0 (no
  `TryWriteBytes` on a span) use the BigInteger expression.

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
gain of the side stage (2 x in the micro benchmark) needs the limbs stored with the points, which is candidate C on
the same representation.

## 4. Tests

- `ExactLimbSignsTests`: the three kernels against the BigInteger expressions on operands of every width (small, at the
  limb boundaries, up to the stack limit and beyond it), with exact zeros and values one unit off; all operands at the
  limit with all bits set (the scratch space); a common power of two of the plane or the point keeps the sign, a
  negative factor reverses it; no allocation inside the limit.
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
