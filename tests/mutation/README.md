# Mutation tests

[Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) (Apache-2.0) makes small changes
(mutants) to the code and runs the tests against each one. A mutant that no test notices "survives". A
surviving mutant marks a test gap: a change to that code that the tests would let through. See
[docs/QA.md](../../docs/QA.md), section 2.2.

## Scope

[stryker-config.json](stryker-config.json) covers the exact arithmetic:
- `math/ExactPredicates.cs`
- `math/ExactPoint3.cs`
- `math/AdaptivePredicates.cs`

The tests that run against the mutants are `ExactPredicatesTests`, `ExactImplicitPointTests` and
`ExactBooleanBuildingBlockTests`. These are the unit tests of these files; the boolean and repair tests are
too slow to run against every mutant.

## Running

The [mutation workflow](../../.github/workflows/mutation.yml) runs on pull requests that change the mutated
files or their tests, every Monday, and on demand through "Run workflow". It fails when the score drops below
the `break` threshold. The HTML report is uploaded as an artifact, and a summary appears in the job summary.

To run it locally, from the repository root:

```
dotnet tool restore
cd tests/mutation
dotnet stryker --config-file stryker-config.json
```

The report goes to `tests/mutation/StrykerOutput/<date>/reports/`. A run takes about 50 minutes on 4 cores,
most of it in the predicate tests with subnormal inputs.

## Baseline (October 2026)

The score was **83.4 %**: 973 mutants killed, 4 timeouts (which count as killed), 177 survived and 17 not
covered.

| File | Killed | Survived | No coverage |
|---|---|---|---|
| `AdaptivePredicates.cs` | 393 | 50 | 0 |
| `ExactPredicates.cs` | 458 | 74 | 1 |
| `ExactPoint3.cs` | 122 (+4 timeouts) | 53 | 16 |

The `break` threshold is 80. Raise it as tests kill more mutants; never lower it to get a run green.

The surviving mutants fall into these groups. Each group is a test gap to close with differential tests
against exact evaluation (QA.md 2.4):

1. **The filter error bounds can shrink without a test failing.**
   - In `ExactPredicates`, these mutants survive: removing terms of the bounds of orient3d, incircle and
     insphere; `+` turned into `-`; and `*` turned into `/` in the permanents.
   - In `AdaptivePredicates`, `- ResultErrBound * |det|` survives.
   - A smaller bound lets the filter return a sign that the exact evaluation would contradict.
   - The tests use configurations that are either clearly decided or exactly degenerate. They need inputs
     whose floating-point determinant is wrong: points a few ulps off a line, plane, circle or sphere, at
     several magnitudes, compared with the exact sign.
2. **The underflow guards** (`permanent > UnderflowLimit`, `sum > UnderflowLimit`): mutants that flip or
   remove these comparisons survive. They need inputs whose products are subnormal and whose filter would
   otherwise accept.
3. **Coincident-point shortcuts** (`a == e || b == e || ...`): the `||` to `&&` mutants survive. Only some
   positions of the repeated point are tested.
4. **`ExactPoint3`**:
   - not tested, or only partly:
     - `ToDouble` near the limits of the double range: subnormal results, rounding to zero, the sign of
       zero, the shift for negative exponents;
     - the zero-W constructor (not covered at all);
     - the `is_safe` checks;
     - `GetHashCode`.
   - Some of these mutants are equivalent: `>>` and `>>>` on a masked value are the same, and so are `|=`
     and `^=` on a cleared bit. Record them as equivalent.
5. **`AdaptivePredicates`**:
   - The `onlyD` (stage D only) path and the `StageDCount` counters are not checked.
   - The merge order in the expansion sums (line 284) is not checked either. Shewchuk's sums need the merge by
     magnitude for non-overlapping output; a test with expansions of very different magnitudes would show it.

Some mutants are equivalent: the code still computes the same result. An example is `det > bound` against
`det >= bound` where the bound is never hit exactly. Mark such cases in the code with
`// Stryker disable once <mutator>: <reason>` instead of writing a test for them.
