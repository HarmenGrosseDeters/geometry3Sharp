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

The tests that run against the mutants are `ExactPredicatesTests`, `ExactImplicitPointTests`,
`ExactBooleanBuildingBlockTests`, `PredicateDifferentialTests` and `ExactPoint3ConversionTests`. A new test
class for these files must be added to the `test-case-filter` and to the paths of the workflow. These are
the unit tests of these files; the boolean and repair tests are
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

Many survivors are **equivalent**. A mutant that only makes a filter decide less often (`>` against `>=`,
`onlyD` forced to `true`, a larger bound, `permanent >= UnderflowLimit`) leaves the result unchanged, because
the fallback computes the exact sign.

**Follow-up measurement (`PredicateDifferentialTests`, `ExactPoint3ConversionTests`).** The groups below were
first read as test gaps. Differential tests aimed at them changed the picture:

1. **Filter error bounds (`ExactPredicates`): one real gap, now closed; the rest practically equivalent.**
   - `PredicateDifferentialTests` compares nearly degenerate configurations with exact integer evaluation.
     The points are a few ulps off a line, plane, circle or sphere, with differences of very different
     magnitudes, far from the origin.
   - These random configurations alone killed none of the 74 survivors in `ExactPredicates`.
   - The InSphere permanent was a real gap. Mutants such as `cdp * bezp` to `cdp / bezp` shrink the bound
     where one z difference is large and the others are 0, and the filter then returns wrong signs.
     `InSphere_OnePointFarOutOfPlane_OthersNearlyCoplanar` builds such inputs: one point far along z, one far
     along x, two nearly on a line in the plane z = 0. With it, the file has 467 killed and 65 survived
     (before: 458 and 74). The score of the file went from 85.9 % to 87.6 %.
   - Of the 11 InSphere survivors left:
     - `-det > bound` to `+det > bound` cannot be reached, because the line before already returns for
       `det > bound`.
     - Four change the underflow guards (`permanent < UnderflowLimit` or `>=`); see 2.
     - Six mutate the subnormal error term (`... * UnderflowUnit`). It matters only in a narrow band of
       inputs whose products are just above the underflow range. That is untested, but no realistic input
       reaches it.
   - The survivors in `Orient2d`, `Orient3d` and `InCircle` are of the same kinds.
   - Shewchuk's bounds are worst-case bounds with a margin of several times the typical error. A bound
     made smaller by one term gives a wrong sign only on inputs constructed for that worst case, as for
     the permanent above.
2. **Underflow guards:**
   - `>=` instead of `>` differs only when the permanent equals `UnderflowLimit` exactly. Equivalent.
   - `<` lets the filter decide only for tiny permanents (below `UnderflowLimit`). There the bound rests on
     the absolute subnormal term alone, which item 1 leaves untested. The mutant is equivalent if that term is
     sound. Its margin (`UnderflowUnit`) is 256 times the smallest subnormal.
3. **Input checks in `ExactPredicates`:**
   - The `throw` for NaN in `Expansion.in_range` is equivalent: NaN fails the range check, and the integer
     path throws anyway.
   - The `MinMagnitude` / `MaxMagnitude` boundaries only choose between two exact paths. Equivalent.
4. **Coincident-point shortcuts** (`a == e || ...`): the shortcuts save time, and the exact paths give 0 for
   coincident points as well. Equivalent.
5. **`ExactPoint3`: a real gap, now closed for the most part.**
   - `ExactPoint3ConversionTests` checks `ToDouble`, `ScaleB`, `BitLength`, `Decompose`, `Create` and
     `ApproxVector3d` exactly, in integer arithmetic, over the whole double range: subnormal results,
     overflow, the sign of zero, quotients of very different sizes.
   - The file's mutants: killed 126 before, 154 after; without coverage 16 before, 2 after. The file's score
     went from 65 % to 81 %. (The second run counts 190 mutants instead of 195, because Stryker's
     "block already covered" filter removes a different number of them.)
   - The remaining survivors are mostly equivalent:
     - exception messages;
     - `>>` against `>>>` on a masked value;
     - `|=` against `^=` on a cleared bit;
     - `GetHashCode`, whose arithmetic only needs to be deterministic.
6. **`AdaptivePredicates`**:
   - The `StageDCount` counters are not checked.
   - The merge order in the expansion sums (lines 284 and 287) is not checked either. Shewchuk's sums need
     the merge by magnitude for non-overlapping output; a test with expansions of very different magnitudes
     would show it.
   - Open.

Mark equivalent mutants in the code with `// Stryker disable once <mutator>: <reason>` instead of writing a test
for them. This happens when the files in `math/Exact*` are next changed anyway; they are being worked on in
other PRs.
