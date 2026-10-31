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

## Next steps (if continued), ranked

1. **One implicit-point predicate** (for example orient2d on an LPI point, the projection of an edge-plane
   intersection). Generate it as a filter plus a fixed-size expansion, and measure it against the current
   `BigInteger` path in `BooleanComparison` (n-ary). This is the real test of the speed claim. The risk is the
   270+ bits of the implicit points (`docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`).
2. Stage D as B plus the tail terms, to reach parity on `orient2d` / `incircle`. Then `orient3d` / `insphere` from the
   same spec. The degree limit of 6 allows insphere (degree 5).
3. A Roslyn source generator instead of the console tool, once the format settles. This needs no JIT, so it also runs
   under Unity IL2CPP.
4. Certificates (idea 2): the derived bound together with its derivation trace, checked by a small independent checker.

**Stop rule:** if step 1 above does not cut the n-ary boolean time by at least 10 % (the bar of
`docs/perf/EXACT_BOOLEAN_PERFORMANCE.md`), stop the speed track and keep only the bound derivation as a review aid.
