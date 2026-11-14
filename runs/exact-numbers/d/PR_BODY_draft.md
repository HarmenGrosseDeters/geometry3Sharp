## The arrangement's Orient and InCircle beyond the fixed width on stack limbs (candidate D of the exact-number study)

Owner decision 2026-11-12 (roadmap/exact-boolean.md, "Exact number representation beyond `BigInteger`"): after P2 (#769)
the candidates B (#772), C, D, each after a probe that confirms a whole-operation gain above noise. The coordinator
accepted D on its probe and on the measurement of the built version. This is D, on the kernels of #772.

### What changes

`ExactMeshArrangement.Orient` and `InCircle` decide on fixed-width points with `FixedArith` (4 limbs per coordinate).
For points beyond that width their last stage was a BigInteger 3 x 3 determinant (`ExactPoint3.Det3`): of the rows
`(v[i], v[j], v.W)` for the orientation, and of a row `(A s, B s, A^2 + B^2)` per point of the triangle for the
in-circle test (`A = v[i] d.W - d[i] v.W`, `B` likewise with `j`, `s = v.W d.W`). On 55928 with its rotated copy that
stage runs 12.4 million times in one operation and owns nearly half of its allocation.

`ExactLimbSigns.Orient2` and `ExactLimbSigns.InCircle` evaluate the same determinants on stack limbs, with `load` and
`det3` of #772. The same integers, so the same signs and results; nothing is allocated. A call whose operands and
scratch space together are above 4608 limbs (36 KB of stack, the limit of `Det4`) evaluates the BigInteger determinant
inside the kernel (`Orient2Big`, `InCircleBig`, as `Det4` does), and so does netstandard2.0. Points of one width are on the stack up to 17 limbs (1088 bits) for `InCircle` and rows
up to 101 limbs for `Orient2`; the widest call of the measured models has 12 limbs for the two widest operands.

Not changed: the filters and the fixed-width stages before it, the control flow. `ExactMeshArrangement` only loses
code: its two BigInteger determinants and `incircle_row` moved into the kernels' BigInteger forms, the call sites
multiply the kernel's sign by the projection's. API: no public change (`dotnet pack` validates against 1.0.324). Design note:
`docs/design/EXACT_LIMB_SIGNS.md`, section 5 (the scratch need is derived there: at most `5 a + 14 (b + c) + 11` for
`Orient2`, `27 t_p + 45 (t_q + t_r) + 99` for `InCircle`).

### Measured

Before = #772 (d555e22), after = this branch on it. 4-core cloud container, .NET SDK 10.0.115, Release, workstation
GC; alternating runs, medians (range in brackets); wall time with collections, the collector's pause and the
allocation separately. Rows: branch `runs/exact-numbers`, `results/d2`, `results/d4`.

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

- Hash gate: result hashes and statuses are equal in every run above.
- 55928 (slow tail, 4 cores, collections included): -12.0 %, the ranges of before and after do not overlap; 15 s of
  the 42 s are the collector's pause (86 GB less allocation).
- 107554, 42327 and the normal strata (4 cores, collections included): no change beyond the run-to-run range; their
  points stay inside the fixed width.
- 42327 has 21 processes per side, not 5: its classification time spreads between processes (12 to 16 s for the same
  work: all counters, the allocation and the hash are equal), on both sides (3 of 33 processes before, 5 of 33 after
  with a median of 14.4 s or more). The first series of 5 had three such processes on the after side (+2.1 / +9.1 /
  +10.3 % per operation), the series with the models in the other order two on the before side. The spread is in
  the base and comes from the tiered JIT's dynamic PGO (#825: 11 of 40 processes by default, 0 of 40 with
  `DOTNET_TieredPGO=0`); the coordinator judged D on the pooled rows.

### Tests

- `ExactLimbSignsTestsPlanar` (7): the kernels and their BigInteger forms against references written in the test (the
  3 x 3 expansion for the orientation; for the in-circle test the 4 x 4 determinant of the lifted points, another
  formula than the kernel's), in the three projections, with collinear and cocircular points by construction and one
  unit off them, other homogeneous factors; for every call the path it took and the scratch limbs used against the
  derived need. Signs known in advance (a counter-clockwise triangle; a point inside, on and outside its circle), on
  both paths. The limits: 4608 limbs on the stack and 4609 not, one wide row or point in each position on both sides
  of the limit, in each projection and with a wider entry in the axis the determinant does not read. The largest
  operands: dense rows at the widest uniform size take at least `33 w` of the `33 w + 11` limbs derived for `Orient2`
  and `117 t` of `117 t + 99` for `InCircle` (the need is not padded). Translation, a 2^k scale and homogeneous
  factors keep the signs; no allocation inside the limits.
- `ExactBooleanWidePointTests` (through `ExactMeshBoolean` with every exact point kept as BigIntegers and the filters
  off, so that every `Orient` and `InCircle` on a constructed point takes this stage): the default run's mesh triangle
  for triangle, for the three operations; under `Similarity.All()`; on a reused instance; with the loops serial
  against parallel. The union allocates 7 / 11 / 28 / 17 MB against 20 / 32 / 73 / 36 MB with the BigInteger
  determinants (bounds 12 / 20 / 45 / 25 MB, on the calling thread with the loops serial; asserted on .NET only, the
  netstandard2.0 library keeps BigInteger). These rows fail on the base.
- Hand mutants (49), each applied and the tests run, all killed by a failing test, none by a build error (script and
  output: branch `runs/exact-numbers`, `d/hand_mutants_d.sh`, `d/hand_mutants_d.txt`):
  - the arithmetic on limbs (11): the in-circle row (`A` added instead of subtracted, `B` from the wrong axis, the
    factor `s` dropped or taken from one point, `B^2` dropped, `A^2 - B^2`), two rows exchanged, a row against the
    wrong point, an operand loaded from the wrong axis; the orientation's columns exchanged, a `W` replaced;
  - the scratch space (10): each coefficient and constant of `OrientScratch` and `InCircleScratch` lowered, a point
    not counted, the fourth point's width not added or taken from two of its three entries, a row's widest entry
    taken from two of three. Each ends in `FixedArena`'s exception or in the tests' check of the scratch use;
  - the limits (8): for each kernel the last sum excluded, one limb more, the operands not counted, never on the
    stack;
  - the result and the high-water mark (3);
  - the call sites (6, by `ExactBooleanWidePointTests`): two points exchanged, the axes exchanged, the fourth point
    exchanged with the third, the projection's sign dropped;
  - the BigInteger forms (11): beyond the limit a zero or the opposite sign, for each kernel; columns or rows
    exchanged; the row's `A`, `s`, `A^2 + B^2`; the `W = 1` shortcut's `B` and its condition.
- Mutation: the offsets of the shards `mesh-arrangement-2..4` follow the file (it got shorter; only the spans move, so
  these shards run incrementally: `select_shards.py` gives `source` for `arrangement-1..4`). The kernels and their
  BigInteger forms are in the shard `mesh-fixed`, whose filter names `ExactLimbSignsTestsPlanar`; that is a filter
  change, so `fixed` runs in full, which is the gate for this exact arithmetic (51 minutes on #772). `mutation.yml`
  paths cover the new test file.
- Left out on purpose: `ExactBooleanWidePointTests` in the filter of `mesh-arrangement-2` (it kills the six call-site
  mutants above). A filter change makes that shard run in full on the PR, which the 3.0 rule excludes; the term can
  join with the next change of that config on master.

### Review (exact code)

An agent with the diff and `docs/REVIEW_CHECKLIST.md`, without the author's reasoning, on the first version (the
kernels returned false beyond the limit and the two call sites evaluated the BigInteger determinant):

- Verified by its own derivation: the signs against the removed expressions (row and column order, `t.Sign`), both
  scratch bounds (`5 a + 14 (b + c) + 11`, `27 t_p + 45 (t_q + t_r) + 99`; a model of the arena rules on 400,000
  cases peaked at 96 % of the need), the operands counted in the limit, the sizes of the `stackalloc`s, `load` on
  0, +-2^63, -2^64, 2^64 - 1. No sign or bound defect.
- Its findings, all taken:
  - a kernel that returns false with a sign of 0 is a trap for a later caller (checklist: silent success): the
    kernels now evaluate the BigInteger form themselves and return the sign, as `Det4`;
  - the fallback code sat in the shard `mesh-arrangement-2` without the test that reaches it: it is now in
    `ExactLimbSigns.cs` (shard `mesh-fixed`) with its tests;
  - the in-circle test compared the kernel with the library's own BigInteger rows: the reference is now the 4 x 4
    determinant of the lifted points, and `KnownSigns` fixes the absolute signs;
  - the limit tests ran in one projection: now in the three, with a wide entry in the unread axis;
  - `total` was an `int` under a comment that promised no overflow for any size: the sizes are summed in `long`;
  - the design note's statement about the callers, the header comment, two assert messages.

A second agent on the final commit (the diff against master only), on what changed since: the BigInteger forms
against the removed code of `ExactMeshArrangement` (textually the old rows, the `W = 1` shortcut, the argument order
at the call sites), the `long` sizes and the casts at the `stackalloc`s, the netstandard2.0 body, `[SkipLocalsInit]`
(`load` and `FixedArith` write every limb they read), the test references on 200,000 small cases with both signs of
`W`, the shard offsets (characters, at the `[MethodImpl]` lines of `build_triangles`, `candidates`,
`triangulate_touched`), the design note against the code. No defect in the kernels or the call sites. Its findings,
taken: the filter change of `mesh-arrangement-2` would run that shard in full (left out, above); `select_shards.py`
maps a filter term to a file, so `mesh-fixed` names the new class.

### Local checks

The full list of CLAUDE.md on a1f0d24a (this change on master 2fea6ac7), 4-core cloud container, every step with exit
code 0: library tests net10.0 12970 passed, 0 failed, 2 skipped (18 m 31 s); g3cli tests 566 passed; `dotnet pack` with the API
validation against 1.0.324 and the package smoke test; the net48 build of the library tests; Clipper2 tests 96
passed, its pack, smoke test and net48 build; the 39 benchmark projects and `tools/PredicateCompiler` build. The head
adds only the two filter terms of the mutation configs to it: on it the tool tests of ci.yml and of `tests/mutation`
pass (7), the library builds for both targets, and `MutationConfigTests`, `ExactLimbSignsTests*` and
`ExactBooleanWidePointTests` pass (33). The net48 tests themselves run only on GitHub (no .NET Framework here).

### Review notes (exact code)

- No floating point in the change. What to check: the two determinants against the BigInteger ones they replace (row
  and column order, the factor `t.Sign` at the call sites), the scratch-space bounds and the limit (design note,
  section 5), the fallback on a false return.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01AaGT5CdtKaF4p9ewMMrjAm
