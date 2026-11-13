## The classification's exact sign stages on stack limbs (candidate B of the exact-number study)

Owner decision 2026-11-12 (roadmap/exact-boolean.md, "Exact number representation beyond `BigInteger`"): after P2 (#769)
the candidates B, C, D, each after a probe that confirms a whole-operation gain above noise. The coordinator accepted B
on its probe. This is B.

### What changes

Three kinds of signs that `ExactMeshBoolean`'s classification decides exactly where its filters do not were BigInteger
expressions, every product and sum a heap object:

| stage | expression | call sites |
|---|---|---|
| plane side | `A X + B Y + C Z - D W` (and `- D` for an integer point) | `interior_side`, `local_side`, `side`, `front_crossings` (two) |
| normal dot | `NX UX + NY UY + NZ UZ` | `carried_crossings` (two) |
| orientation | the 4 x 4 determinant of four homogeneous points | `orient3d` (through `orient3d_exact`) |

`math/ExactLimbSigns.cs` evaluates the same expressions with the library's `FixedArith` (sign-magnitude limbs, a stack
arena): the operands are copied from their BigInteger storage into stack limbs per call, products and sums live in the
arena, only the sign leaves. The same integers, so the same signs and the same results; nothing is allocated.

A call whose stack need is above a limit keeps the BigInteger expression, and so does netstandard2.0:

- plane side and dot: the sum of the operands' limbs above 320 (eight operands of 2560 bits, or one wide operand beside
  small ones); at most 13 KB of stack per call;
- determinant: operands and scratch space together above 4608 limbs, 36 KB of stack per call. The scratch space
  follows the rows (the expansion is along the first row): `8 a + 28 b + 64 (c + d) + 96` limbs for the widest entries
  `a`, `b`, `c`, `d` of the four rows; rows of one width are on the stack up to 25 limbs (1600 bits).

Not changed: the filters before the stages, the control flow, `antipodal_value`, the L1 / L2 tracing, the far points.
`centroid_point` (a construction, its result is kept) stays BigInteger: its probe showed no gain (below).

API: no public change (`dotnet pack` validates against 1.0.324). Design note: `docs/design/EXACT_LIMB_SIGNS.md` (the
scratch need is derived there: at most `4 total + 6` for a side, `7 a + 27 b + 63 (c + d) + 71` for the determinant).

The branch has master 9cd2fd8 merged in (a conflict in the offsets of the shards `mesh-boolean-2..4` after #759; the
offsets are master's split points mapped onto the merged file).

### Measured

Before = master 683fb2c, after = this branch at af87765 (the first version of the kernels). 4-core cloud container,
.NET SDK 10.0.115, Release, workstation GC; 5 alternating runs, medians (range of the 5 runs in brackets); wall time
with collections, the collector's pause and the allocation separately. Rows: branch `runs/exact-numbers`, `results/b2`.
(The probe before the build, a prototype of the kernels on c0f8aa9, gave the same picture: `REPORT.md` section 5b.)

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

- Hash gate: result hashes and statuses are equal in every run above (tail 6 / 6 operations, normal 60 / 60 and
  39 / 39), and hash, status and triangle count in 61 of 61 units of the #711 class (one run each; those units do not
  reach these stages: 280.5 -> 288.0 s, allocation 243.4 -> 242.6 GB, single runs).
- Slow tail, 4 cores, collections included: 42327 gains 15 to 17 % per operation, the ranges of before and after do
  not overlap; about three quarters of the gain is the collector's pause (12.8 GB less allocation). 107554 gains 3 to
  6 %, most or all of it the collector's pause; its ranges overlap in part for the union and the difference.
- Normal stratum, 4 cores, collections included: no change beyond the run-to-run range, allocation unchanged: the
  stages are hardly reached there.
- On one thread (the probe) the orientation stage drops from 3.4 to 1.9 s on 42327, the side stage only from 9.0 to
  8.1 s: loading eight operands per call costs about what the BigInteger temporaries cost. Its CPU gain (2 x in the
  micro benchmark) needs the limbs stored with the points.

The review's changes (the limits, the scratch space of the determinant) against af87765, slow tail, 4 cores, wall time
with collections, 5 alternating runs on the same day (the machine was about 8 % slower than on the day of the table;
rows: `results/b3`):

| operation | af87765 | with the review's changes | change of the median | allocated |
|---|---|---|---|---|
| 107554, union | 22.0 s (21.8-23.7) | 22.5 s (20.6-24.0) | +2.5 % | 9.45 -> 9.45 GB |
| 107554, intersection | 17.7 s (16.8-18.7) | 17.3 s (17.1-18.2) | -2.2 % | 8.72 -> 8.72 GB |
| 107554, difference | 17.6 s (16.8-18.9) | 18.0 s (17.2-21.3) | +2.1 % | 8.69 -> 8.69 GB |
| 42327, union | 23.0 s (22.7-24.4) | 23.5 s (23.0-26.3) | +2.2 % | 7.80 -> 7.80 GB |
| 42327, intersection | 22.2 s (21.8-22.8) | 21.9 s (21.6-22.9) | -1.3 % | 7.51 -> 7.51 GB |
| 42327, difference | 22.4 s (20.9-24.0) | 23.0 s (21.4-24.8) | +3.0 % | 7.58 -> 7.58 GB |

No change beyond the run-to-run range (the ranges overlap in every row), the allocation is the same to the megabyte
(no call of these models moved to the BigInteger path under the new limits), the hashes are equal in all 30 runs.

Candidate C, part 1 (`centroid_point` on stack limbs, the result converted back to a BigInteger point), probed on the
prototype and not built: slow tail -5.2 to +3.6 % per operation with overlapping ranges, normal strata unchanged, one
thread 0.8 to 4.2 % slower; the conversion back costs what the temporaries cost (`results/c`).

### Tests

- `ExactLimbSignsTests` (8): the kernels against the BigInteger expressions on operands of every width (small, at the
  limb boundaries, up to the stack limits and beyond them), with exact zeros and values one unit off, and for the
  integer-point side cases in which `D` decides the sign. For every call the tests check the path it took (the stack
  or the BigInteger expression, through an `out` overload that reports the scratch limbs used; the cases beyond a limit
  are counted by the sum that selects the path) and the scratch use against the derived need. The limits themselves:
  a sum of 320 limbs on the stack and 321 not, for the three forms; 4608 limbs of the determinant on the stack and
  4611 not; one operand of 300 limbs in each of the eight positions beside operands of one limb; one wide row in each
  of the four positions, on both sides of the limit. The largest operands: all bits set, and rows of 25 limbs of
  random bits, whose determinant takes at least 4000 of the 4071 limbs derived (the need is not padded). A common power
  of two of the plane or the point keeps the sign, a negative factor reverses it; no allocation inside the limits.
- `ExactBooleanLimbSignsTests` (6; through existing entry points only, so they also build against the base library):
  open inputs (crossing grids, also far from the origin; a sphere with holes) with the filters switched off, so that
  every decision takes an exact stage: the result is the filtered run's mesh triangle for triangle, for the three
  operations, and the union allocates 17 / 34 / 55 MB against 73 / 160 / 158 MB on the base (bounds 40 / 80 / 100 MB;
  a bound on allocation, measured on the calling thread with the parallel loops serial, so it does not depend on the
  machine or on tests running beside it). The four allocation rows fail on the base.
- The existing tests of `ExactMeshBoolean` (filtered against unfiltered results, the invariance over
  `Similarity.All()`, reuse, parallel against serial) cover the operation, whose results do not change. No new
  invariance, reuse or determinism test: the kernels are static functions on integers without state or a parallel
  path.
- Hand mutants (41), each applied and the tests run, all killed (script and output: branch `runs/exact-numbers`,
  `b/hand_mutants_b.sh`, `b/hand_mutants_b.txt`):
  - the arithmetic (11, by `ExactLimbSignsTests`): the `D W` term added instead of subtracted; a product replaced;
    the `D` term of the integer-point side dropped; the third product of the dot dropped; a negative operand not
    negated; the sign lost; one limb too few; no sign extension; a minor's sign; no alternating sign in the
    determinant; a wrong column of a minor;
  - the scratch space (9): the sides' `3 total` and `total` only; the determinant's first row (4 instead of 8 limbs
    per limb), second row (20 instead of 28), other rows (56 instead of 64), the fourth row not counted, no constant,
    a row's widest entry taken from two of its four entries (first row, fourth row). Each ends in `FixedArena`'s
    exception or in the tests' check of the scratch use, none in a write outside a buffer;
  - the limits (12): for each of the three forms of the sides and the dot the last sum excluded (`<`) and one limb
    more; the plane side always BigInteger; its limit on the widest operand instead of the sum (the old reading of
    the comment); the determinant's last sum excluded, three limbs more, the operands not counted, always BigInteger;
  - the high-water mark (5): each of the four kernels reporting 0 limbs used, and `FixedArena.Remaining` returning 0;
  - the call sites (4): the sign of `local_side` / `side` reversed and two points of `orient3d` exchanged (by
    `ExactBooleanLimbSignsTests`); the sign of `interior_side` reversed (`ExactBooleanOpenInputTests`) and the
    operands of a dot in `carried_crossings` exchanged (`ExactMeshBooleanTestsTraced`).
  - A first version passed the far point's `W = 1` as a constant at the call site in `front_crossings`; replacing it
    by 0 survived the whole boolean test set (a far point is 4000 box extents away, so `D` almost never decides its
    side). The side of an integer point is now a kernel of its own, whose test has cases where `D` decides.
- net48: the netstandard2.0 library keeps the BigInteger expressions, so the allocation bound of
  `ExactBooleanLimbSignsTests` is asserted on .NET only (it failed in the job `netstandard-b` on af87765 with 71 / 154 /
  155 / 62 MB; the results were equal); the path assertions of `ExactLimbSignsTests` expect the BigInteger path there.
- Mutation: `math/ExactLimbSigns.cs` joins the shard `mesh-fixed` with `ExactLimbSignsTests`; the offsets of the shards
  `mesh-boolean-1..4` follow the merged file (`MutationConfigTests`).

### Review (exact code), S1 to S4

- S1 (the limit of the sides and the dot is on the sum, the comment said per operand): the comments and the constant's
  name say so (`MaxOperandLimbs`), with the tests of the limit above.
- S2 (58 KB of stack per `Det4`): the scratch space follows the rows instead of 164 limbs per limb of the widest
  entry, and operands and scratch space together are limited to 4608 limbs, 36 KB; the sizes are compared in `long`.
  The kernels are leaves (they call only `FixedArith`), the callers are the classification loops, which do not recurse
  through them (design note, section 2).
- S3 (the `wide` counter counted cases that still took the stack): the tests count the cases beyond the limit by the
  sum of the limbs, and assert the path of every call.
- S4 (the derivation, the high-water mark): the derivation is in the design note; `FixedArena.Remaining` gives the
  kernels' `out` overloads the limbs used, and the tests compare them with the derived need on every call.

### Local checks

The full list of CLAUDE.md on e30fa68 (the review's changes with master 9cd2fd8 merged in), 4-core cloud container,
every step with exit code 0: library tests net10.0 11555 passed, 0 failed, 2 skipped (29 m 4 s, with the hand mutants
running beside them); g3cli tests 538 passed; `dotnet pack` with the API validation against 1.0.324 and the package
smoke test; the net48 build of the library tests; Clipper2 tests 72 passed, its pack, smoke test and net48 build; the
31 benchmark projects and `tools/PredicateCompiler` build. The head 93879ee adds only the `#if NET` around the
allocation bound of one test and the design note: on it the two test classes (14 tests, net10.0) pass and the net48
test project builds. The net48 tests themselves run only on GitHub (no .NET Framework here).

Master is merged in once (the conflict above); it is merged again only on a new conflict or right before the merge
(runner backlog rule).

### Review notes (exact code)

- No floating point in the change; what to check: `load` (two's complement to sign-magnitude, the limb count), the
  expressions against the BigInteger ones they replace, the scratch-space bounds and the two limits (design note,
  section 2, and the kernels' comments).
- `front_crossings`: the far point is an integer point, its side is `A X + B Y + C Z - D`, as the expression it
  replaces.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01AaGT5CdtKaF4p9ewMMrjAm
