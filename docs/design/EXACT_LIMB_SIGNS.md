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
- `PlaneSide`, `Dot`: the products and sums in the order of the expression.
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

## 3. Measured (the probe, before the build)

Commit set: library c0f8aa9 with the study's instrumentation and the prototype of these kernels at the seven call
sites; 4-core cloud container, .NET SDK 10.0.115, Release, workstation GC; 5 alternating runs, medians; wall time
with collections, the collector's pause and the allocation separately. The PR repeats it on its own base.

| stratum | operation | before | after | change | GC pause | allocated |
|---|---|---|---|---|---|---|
| slow tail | 42327 with its rotated copy, union | 34.5 s | 29.6 s | -14.0 % | 6.7 -> 3.5 s | 23.0 -> 10.2 GB |
| slow tail | 42327, intersection | 26.9 s | 22.3 s | -17.1 % | 6.7 -> 3.5 s | 20.6 -> 7.8 GB |
| slow tail | 42327, difference | 27.0 s | 22.9 s | -15.3 % | 6.6 -> 3.4 s | 20.6 -> 7.8 GB |
| slow tail | 107554, the three operations | 26.4 / 17.1 / 17.4 s | 26.2 / 16.4 / 16.6 s | -0.8 / -4.2 / -4.7 % | 5.0 -> 4.3 s | 10.0 -> 8.5 GB |
| normal | 20 pairs, 60 operations | 6.32 s | 6.14 s | -2.8 % (inside the run-to-run range) | 0.12 -> 0.12 s | 2.36 -> 2.36 GB |
| normal | 13 models with a rotated copy, 39 operations | 4.14 s | 4.22 s | +2.0 % (inside the range) | 0.12 -> 0.11 s | 1.48 -> 1.48 GB |

Result hashes, statuses and counts are equal in every run. About two thirds of the gain on 42327 is the collector's
pause. On one thread the orientation stage drops from 3.4 to 1.9 s and the side stage only from 9.0 to 8.1 s: loading
eight operands per call costs about what the BigInteger temporaries cost. The CPU gain of the side stage (2 x in the
micro benchmark) needs the limbs stored with the points, which is candidate C on the same representation.

## 4. Tests

- `ExactLimbSignsTests`: the three kernels against the BigInteger expressions on operands of every width (small, at the
  limb boundaries, up to the stack limit and beyond it), with exact zeros and values one unit off; all operands at the
  limit with all bits set (the scratch space); a common power of two of the plane or the point keeps the sign, a
  negative factor reverses it; no allocation inside the limit.
- `ExactBooleanLimbSignsTests` (existing entry points only, so the tests also build against the base library): open
  inputs (crossing grids, a sphere with holes) with the filters switched off, so that every decision takes an exact
  stage: the result is the filtered run's mesh triangle for triangle, for the three operations, and the union
  allocates 17 / 34 / 55 MB against 73 / 160 / 158 MB with the BigInteger expressions (bounds 40 / 80 / 100 MB).
- The existing tests of `ExactMeshBoolean` (filtered against unfiltered results, the invariance over
  `Similarity.All()`, reuse, parallel against serial) cover the operation, whose results do not change.

## 5. Sources

Own work on the library's `FixedArith` (`math/FixedPoint3.cs`); the stages and their filters are those of
`ExactMeshBoolean` (docs/design/EXACT_BOOLEAN_TAIL_55928.md).
