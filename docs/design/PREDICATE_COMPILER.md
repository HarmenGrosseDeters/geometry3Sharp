# Predicate compiler (experiment, step 1)

**Status:** research spike. Nothing in the library uses the generated code. Nothing is entered in the roadmap.

## Hypothesis

A sign predicate is written once, as a polynomial formula. A generator derives from it, by machine:
- a floating-point filter with a proven error bound,
- the exact stages, with expansion lengths known statically.

If that works, one small tool gives both speed and correctness, and new error bounds no longer need a manual derivation
and review. The idea follows FPG (A. Meyer, S. Pion, *FPG: A code generator for fast and certified geometric
predicates*, RNC 2008) and Attene's indirect predicates (2020). Both were used for the idea only; no code was read.

**Step 1** (this document) is the cheap, abortable check: generate only `Orient2d` and `InCircle`, then compare them with
the hand-written `ExactPredicates`, both on the signs (they must be identical) and on speed.

## What was built

| file | content |
|---|---|
| `tools/PredicateCompiler/Compiler.cs` | parser, error analysis (exact rationals), C# emitter, about 600 lines |
| `tools/PredicateCompiler/predicates.spec` | the two formulas (12 lines) |
| `math/Generated/GeneratedPredicates.cs` | generated output (internal, `GeneratedPredicates`) |
| `math/Generated/PredicateRuntime.cs` | hand-written expansion arithmetic on stack buffers (no workspace, no allocation) |
| `tests/geometry3Sharp.Tests/GeneratedPredicatesTests.cs` | up-to-date check, oracle tests, invariance, mutation-checked |
| `benchmarks/PredicateComparison` | the generated predicates as an additional contender, plus direct-call timing |

The generated stages of each predicate:

1. **Filter (stage A).** It tests `|det| > C m + fl(A + 1) U`:
   - `m` is the permanent. It is computed in floating point, with abs taken at the products.
   - `A` is a polynomial in the magnitudes. It covers the absolute errors of products that underflow.
   - `C`, the polynomial `A` and the power of two `U` are derived from the formula by the rules in the header of
     `Compiler.cs`. The error model is round to nearest, with `|n| <= 2^-1075` for products that underflow.
   - With this absolute term the filter has no separate underflow guard.
2. **Range gate.** All inputs must be 0 or lie in [2^-100, 2^100]. Otherwise the predicate goes to BigInteger.
3. **Stage B.** The formula is evaluated exactly on the rounded leaf differences. If all tails are 0, this is the
   exact sign. Otherwise the stage compares the exact value with its derived bound `cB P`.
4. **Stage C.** It adds to B the first-order term of the tails: the derivative times the tails, propagated as dual
   numbers in floating point. Its bound is derived the same way. Terms of order 2 and higher are bounded by
   `((1 + u)^D - 1 - D u) P`.
5. **Stage D.** The formula is evaluated exactly with two-component leaf differences.

Stages B and C compare their expansions exactly against the bound, instead of using Shewchuk's floating-point estimate.
This costs an expansion sum, but it avoids a separate bound for the estimate.

## Results

### Derived constants against Shewchuk's

The generator derives these constants from the formula alone, in units of u = 2^-53. They are leading terms; the
generator rounds the exact rationals up.

| | filter (A) | stage B | stage C |
|---|---|---|---|
| orient2d, generated | 3 u | 2 u | 7 u² |
| orient2d, Shewchuk / `ExactPredicates` | (3 + 16u) u | (2 + 12u) u | (9 + 64u) u² |
| incircle, generated | 10 u | 4 u | 42 u² |
| incircle, Shewchuk / `ExactPredicates` | (10 + 96u) u | (4 + 48u) u | (44 + 576u) u² |

The machine derivation reproduces the hand-derived leading constants. Stage C comes out slightly tighter. Stage C is
compared exactly, so the estimate error that Shewchuk includes in his constant is not needed there.

### Correctness

`dotnet run -c Release` in `benchmarks/PredicateComparison` (20 000 cases per row):
- every scenario has 0 wrong signs: nearly degenerate, exactly degenerate on the grid, exactly degenerate with inexact
  differences, and the Kettner grid;
- every scale has 0 wrong signs: 1, 1e6, 1e-10, 1e-160, 1e-300, 1e150, 1e200.

In `GeneratedPredicatesTests` the generated predicates equal the hand-written BigInteger oracle and `ExactPredicates`. This holds:
- on the same classes of inputs;
- on inputs whose rounded differences are degenerate (stage B is 0 and the tails decide);
- on subnormal partial products times large factors;
- on 100 000 inputs with random magnitudes between 2^-1080 and 2^400;
- for every frame of `Similarity.All()`.

**Mutation check.** 18 mutants were tried, plus 4 after the review fixes (all killed except as noted below):
- each error bound reduced by 100×, and the filter constant reduced to 1 u (orient2d) or 2 u (incircle);
- the absolute term dropped;
- one term dropped from stage C;
- one leaf rounded in stage D;
- the range gate removed;
- four faults in the runtime.

All of these are killed. Two kinds of mutant survive:
- Removing one of stage C's two comparisons only makes a call slower: it falls through to stage D, which is exact.
- An incircle filter constant between 2.2 u and 10 u cannot be told apart by tests. The largest error found in 2M random
  exactly cocircular inputs is 2.2 u of the permanent. Tests can only probe errors that inputs actually reach, so the
  soundness of a bound rests on its derivation. This is the case for a bounds review, or for a certificate (idea 2 of
  the proposal).

The independent review (an agent that got the code, not the derivation) found:
- **A derivation fault in the generator.** The rounding depth of the permanent was taken as `max + 1` at products. It
  must be `dx + dy + 1`, because the factors' roundings multiply. The incircle stage B and C constants were 1 ulp too
  small (depth 5 instead of 7). No input that breaks this was found, since cB has a lot of slack, but the bound was not
  proven. This is fixed, and `StageConstants_CoverTheRoundingsOfThePermanent` checks the constants against an
  independent derivation. It kills the old constants.
- **Non-homogeneous formulas.** They would get wrong signs from the BigInteger fallback, which scales all inputs by one
  power of two. Formulas without a leaf difference, and input names that clash with generated locals, gave code that
  does not compile. The generator now rejects all three (`Compiler_RejectsUnsupportedFormulas`).
- **Test gaps:**
  - Nothing showed whether stages B and C decide at all (`StagesBAndC_DecideMostNearlyDegenerateCases`).
  - The range gate's boundaries were not tested (`RangeGate_Boundaries`).
  - The filter was emitted twice, and only one copy was tested; the public entry now calls the filter.

So the generator's rules are themselves exact-arithmetic code that needs the bounds review. The review burden moves from
every predicate to the generator, once, but it does not disappear.

During the work one more fault was found and fixed: "stage B is exactly 0, so the result is 0". It is wrong when the
tails are not 0. `RoundedDifferencesDegenerate_TailsDecide` now covers it, and the mutant is killed.

### Speed

`dotnet run -c Release` in `benchmarks/PredicateComparison`, section "direct calls". The numbers are ns per call
(median of 7 × 1M calls), hand-written `ExactPredicates` / generated, from one session on the same machine. They
vary by about 10 % between runs.

| | hand-written | generated | |
|---|---|---|---|
| orient2d general | 3.7 | 3.8 | equal |
| orient2d nearly collinear | 19.5 | 28.1 | +44 % |
| orient2d degenerate (2^k) | 229 | 297 | +30 % |
| incircle general | 58.3 | 55.4 | equal |
| incircle nearly cocircular | 916 | 1001 | +9 % |
| incircle degenerate (2^k) | 2580 | 2526 | equal |

Before stage C was generated, the generated code was 2.2-2.5× slower on the nearly degenerate cases. Stage C closed
most of that gap.

The remaining gap is in the exact stages:
- Stage D recomputes the whole formula with two-component leaves. Shewchuk's stage D only adds the tail terms to B.
- The exact comparisons of B and C cost an expansion sum each.

## Verdict for step 1

- **Correctness and bound derivation: the idea holds, with a caveat.** A 12-line formula gives predicates that are sign-identical with
  the hand-written ones. The error constants match or beat the hand-derived ones, with no manual derivation. This is the
  part that removes the per-bound review burden. The generator's rules still have to be reviewed, once. The review
  found a fault in them (see above), which supports idea 2: certificates checked independently.
- **Speed on double-input predicates: no gain.** The filter is at parity. The exact stages are 0-45 % slower. For
  `orient2d` and `incircle` Shewchuk's hand-written code is already close to optimal, so a generator cannot win much
  here. Getting to parity needs one more transformation: stage D as B plus the tail terms (the expansion of
  `F(d + t) - F(d)`). This is mechanical.
- **Not tested yet: the place where the hypothesis would pay off.** The proposal named the cost of `BigInteger` in the
  n-ary booleans (around 30 % each of classification, triangulation and GC). That cost comes from the predicates on
  implicit points (`ExactMeshArrangement`), not from these two.

## Step 2: an implicit-point predicate in fixed-width integers

**Where the cost is.** A CPU trace of `BooleanComparison nary grid 10 onepass` shows that the cost of `BigInteger` is
allocation and GC: its constructor and the threads paused for GC take most of the time, while the multiplications
themselves take under 1 %. (Sampled thread time is unreliable under GC, see `docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`.)

A temporary probe, not committed, measured the exact stage of the arrangement's `InCircle`:
- **Calls:** on grid 10, 1.4M exact calls go through `BigInteger` and only 93k through the existing double path. Almost
  all of them involve rational points (W ≠ 1).
- **Sizes**, median / max on grid 10:

  | value | median | max |
  |---|---|---|
  | coordinates | 101 bits | 109 bits |
  | weights | 47 bits | 54 bits |
  | rows (x, y) | 157 bits | 265 bits |
  | lifts | 207 bits | 319 bits |
  | determinant | 371 bits | 833 bits |
- **Time:** about 10-15 s of thread time is spent in that path, a large part of the triangulation phase.

**What was built:**
- **`ipredicate` in the generator.** Inputs are integers with bit bounds. Every node gets a proven bound: a sum or
  difference is below 2^(max + 1), a product below 2^(bx + by). Each value is stored in exactly the 64-bit limbs that
  this bound needs in two's complement.
- **`ArrInCircle`** is the arrangement's determinant with rows `(A s, B s, A² + B²)`. Its inputs are coordinates
  below 2^112 and weights below 2^58. The result is below 2^920 (15 limbs), and the widest product has 15 limbs.
- **`IntegerRuntime`** holds the hand-written limb arithmetic on stack buffers. Every operation checks that its result
  fits and throws otherwise, so a wrong bound fails loudly instead of returning a wrong result.
  - On .NET it uses `Math.BigMul` and `BigInteger.TryWriteBytes`, without allocation.
  - On `netstandard2.0` the same arithmetic runs with portable replacements: a 64 × 64 bit product from 32-bit halves,
    and inputs read through `ToByteArray`, which is one allocation per input. The replacements are compiled on every
    target, so the .NET tests check them against the .NET operations.
- **`ExactMeshArrangement.InCircle`** now tries the generated predicate first. It falls back to `BigInteger` when an
  input exceeds its bound.

**Results.** `nary <case> onepass`, median of 4 alternating rounds, against the state with the `AddPoint` fix. The
results are identical: same triangles and the same volume, bit for bit.

| | before | after | |
|---|---|---|---|
| grid 10, triangulation | 1888 ms | 1495 ms | −21 % |
| grid 10, total | 6573 ms | 6071 ms | −8 % |
| grid 5, total | 578 ms | 561 ms | −3 % |
| spheres 20 | | | unchanged (10 exact calls) |

Both changes together against master, 3 rounds on grid 10: 6652 -> 6006 ms (−9.7 %).

**Tests.**
- The fixed-width result equals the BigInteger determinant on 50 000 inputs of every size up to the bounds, including
  0 and 2^b − 1, and on exactly cocircular inputs.
- Inputs at the bounds and just beyond are tested.
- The runtime refuses overflow.
- 10 mutants are all killed: two in the generated code, and in the runtime the carries, negation, sign extension,
  the Fits boundary and the overflow checks.

**Coverage (from the review).** The bounds 112 / 58 are fitted to the n-ary grid. On the inputs of the exact
boolean tests, about half of the exact `InCircle` calls fall back to `BigInteger`: their coordinates have 155-211
bits and their weights 102-155. The −21 % holds for grid-like inputs. A second, wider variant (for example 212 / 156
bits, about 33 limbs) would cover them; it has not been measured on the real-world classes yet.

**`Orient` was tried and dropped.** The arrangement's `Orient` on rational points was generated the same way
(`ArrOrient`, 285 bits, 5 limbs). On grid 10, 2.9M of its 4.3M exact stages took the fixed-width path and 0.26M fell
back. The total time did not change (median 6306 -> 6430 ms, within the noise), so it was removed again.

Per call, for typical values (100-bit coordinates, 50-bit weights):

| part | ns |
|---|---|
| `BigInteger` `Det3` | 811 |
| generated version | 520 |
| of which 9 × `Fits` | 75 |
| of which 9 × `Load` (`TryWriteBytes`) | 208 |
| of which 12 × `Mul` | 401 |

- For values of this size `BigInteger` is only about 1.5 times slower.
- The runtime's overhead dominates: loading costs about 23 ns per value, and the generic multiplication copies the
  magnitudes.
- Two things would cut this:
  - limbs cached once per point;
  - multiplications of fixed shape, unrolled by the generator.
- Even then the gain would be at most about 0.4 s of 6.3 s, because `Orient` is a small share of the time.

**Allocations and GC (gc-verbose trace, grid 10):**
- **Allocations:** with `InCircle` in fixed width they drop from 8.3 to 5.8 GB (−30 %). `incircle_row` and `Det3`,
  which were 29 % of the bytes before, are gone.
- **GC pauses:** they stay at about 2.8 s. Short-lived garbage is cheap; the pauses come from the long-lived exact
  points (four `BigInteger`s each), which every collection has to keep.
- **The remaining allocation sites:** `Orient` (about 20 %), the ray tests of the classification (`front_winding`,
  13 %), `ComparePoints` (8 %), point location, `centroid`.

**Verdict for step 2.**
- **The approach works.** The generator derives the widths, the result is exact and allocates nothing, and the phase
  it replaces gets 21 % faster.
- **On its own it stays just under the 10 % bar** on the total (−8 %). The arrangement's other exact stages still use
  `BigInteger`:
  - `Orient` on rational points (18.5M calls, 21 % of them exact on the n-ary grid);
  - `ComparePoints` in the merge;
  - the construction of the exact points (with gcd).

  The `Orient` attempt shows the limit of predicate-by-predicate work: where the values are small, `BigInteger` is
  not much slower. The large lever is to store the exact points in fixed width instead of `BigInteger`. That removes
  the long-lived digit arrays behind the GC pauses and the loading cost of every predicate. It is idea 1 of
  `docs/perf/EXACT_BOOLEAN_PERFORMANCE.md` (`Int384` spike), and the generator can supply the predicates and the bit
  bounds for it.

## Next steps (if continued), ranked

1. **The arrangement's `Orient` and `ComparePoints` on rational points** as `ipredicate`s, the same way as
   `ArrInCircle` (step 2). Together with step 2 this decides the speed track on the n-ary grid. Related work to read
   first: B. Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG* (2024/2025, Geogram, BSD).
   It also evaluates predicates on homogeneous points without allocation; check its license and approach before
   reading any code.
2. Stage D as B plus the tail terms, to reach parity on `orient2d` / `incircle`. Then `orient3d` / `insphere` from the
   same spec. The degree limit of 6 allows insphere (degree 5).
3. A Roslyn source generator instead of the console tool, once the format settles. This needs no JIT, so it also runs
   under Unity IL2CPP.
4. Certificates (idea 2): the derived bound together with its derivation trace, checked by a small independent checker.

**Stop rule:** if step 1 above does not cut the n-ary boolean time by at least 10 % (the bar of
`docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`), stop the speed track and keep only the bound derivation as a review aid.
