# Mutation tests

[Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) (Apache-2.0) makes small changes
(mutants) to the code and runs the tests against each one. A mutant that no test notices "survives". A
surviving mutant marks a test gap: a change to that code that the tests would let through. See
[docs/QA.md](../../docs/QA.md), section 2.2.

## Scope

Two jobs of the [mutation workflow](../../.github/workflows/mutation.yml): `exact-arithmetic` (this section and
the next ones) and `exact-mesh` (the exact mesh pipeline, [below](#exact-mesh-pipeline-stryker-mesh-json)).

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

**Current (October 2026, with `PredicateDifferentialTests` and `ExactPoint3ConversionTests`): 87.05 %**
(GitHub runner, 37 minutes).

The `break` threshold is 86: the measured score rounded down, less one point, because timeouts (which count as
killed) vary between runners. Raise it as tests kill more mutants; never lower it to get a run green.

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

The calls of `PredicateStats` (profiling counters, removed by the compiler unless the library is built with
`-p:G3PredicateStats=true`) are excluded with `ignore-methods` (in all configs, also the `stryker-mesh-*.json`
ones below): removing them changes nothing.

Mark equivalent mutants in the code with `// Stryker disable once <mutator>: <reason>` instead of writing a test
for them. This happens when the files in `math/Exact*` are next changed anyway; they are being worked on in
other PRs.

## Exact mesh pipeline (stryker-mesh-*.json)

The `exact-mesh` job mutates the exact mesh pipeline, in shards that run in parallel (a matrix job, one runner
each). Each shard runs the unit tests of its file (the `test-case-filter` of its config):

| Shard | Mutated | Tests |
|---|---|---|
| `rounding` | `mesh_ops/MeshRoundingRepair.cs` | `MeshRoundingRepairTests`, `MeshRoundingRepairStrategyTests`, `ExactMeshBooleanTests`, `ExactBooleanSurvivorTests`, `ExactBooleanIssueCasesTests`, `ExactBooleanByDesignTests` |
| `boolean-1`, `boolean-2` | `mesh_ops/ExactMeshBoolean.cs`, split at `front_winding` | `ExactMeshBooleanTests`, `ExactMeshBooleanNaryTests`, `ExactBooleanOrientationTests`, `ExactBooleanReuseTests`, `ExactBooleanEdgeOrderTests`, the `ExactVertices` tests of `MeshRoundingRepairTests`, `BooleanPerformanceTests`, `ExactBooleanOpenInputTests`, `ExactBooleanSurvivorTests`, `ExactBooleanIssueCasesTests`, `ExactBooleanByDesignTests`; `boolean-2` also `ExactBooleanFilterTests` |
| `arrangement-1`, `arrangement-2` | `mesh_ops/ExactMeshArrangement.cs`, split at `build_triangles` | `ExactFilterBoundTests`, `ExactArrangementScalingTests`, `ExactMeshBooleanTests`, `ExactBooleanEdgeOrderTests`, `ExactBooleanBuildingBlockTests`, the `ExactVertices` tests, `BooleanPerformanceTests`, `ExactImplicitPointTests` |
| `planar` | `mesh_ops/ExactPlanarSimplify.cs`, `mesh_ops/ExactTriangulation2.cs` | `ExactPlanarSimplifyTests`, `ExactTriangulation2Tests`, `ExactBooleanBuildingBlockTests`, `ExactMeshBooleanTests` |

On a pull request, the workflow's `changes` job starts `exact-mesh` only if one of these files, their tests or
the configs changed (not this README) (and `exact-arithmetic` only for its own files); schedule and "Run workflow" start both. A new
test class for these files goes into the filter of its shard, the workflow's `paths` and the `mesh` pattern of the
`changes` job. The job also prints the surviving and uncovered mutants to its log (step "Surviving mutants").

Things to know:
- **Stryker instruments the whole library**, not only the mutated files (about 97 000 mutants are created and all
  but the shard's are then ignored). The tests run on instrumented code, so a shard takes 1 to 5 hours on a
  GitHub runner, most of it in the boolean tests. Locally, `dotnet stryker --config-file stryker-mesh-<shard>.json`
  in this directory.
- **The splits are character offsets**, not lines: Stryker.NET reads `File.cs{a..b}` as a text span. The offsets
  are at the start of a method (`build_triangles`, `front_winding`); the second shard runs to the end of the file.
  After larger changes to the file, move the offset back to the start of that method (both shards of the file
  must be updated together), else mutants near the boundary are tested twice or not at all.
  `MutationConfigTests` fails when an offset is not at the start of its method (its attribute line, if any).
- **Memory:** a mutant can make a loop allocate without end. `DOTNET_GCHeapHardLimit` (6 GB per process) and
  `concurrency: 2` turn that into an OutOfMemoryException in the test host (the mutant is killed); without them
  the first run lost its runner.
- **Timeouts** count as killed, and their number varies between runs: the first run of `ExactMeshBoolean.cs`
  (one shard, on a busier runner) had 215 timeouts and 85.5 %, the second (two shards) 63 timeouts and 71 %
  together; `arrangement-1` had 164 and then 263. A mutant that makes the boolean loop times out, and each
  timeout costs the test time plus `additional-timeout`, which is most of the run time of the arrangement
  shards. They use 10 s (`arrangement-2` took 5 h 14 min with 30 s, close to the job's 350 minutes, and 1 h 50
  min with 10 s, with fewer timeouts, 356 against 461: those mutants really loop). The other shards keep 30 s: with
  10 s, slow but finishing tests timed out, and timeouts that are not loops hide survivors (`rounding` went from
  10 to 108 timeouts and from 59.9 % to 72.6 %, `boolean-2` from 7 to 119 and from 65.6 % to 80.8 %). The thresholds are the score rounded down, less two
  points, and less half the share of timeouts in shards with many of them (`arrangement-1` 32 %,
  `arrangement-2` 43 %), so that a quieter runner that turns timeouts into survivors does not fail the job.

### Scores (October 2026)

Mutation score per shard (killed and timeouts over all tested and uncovered mutants), on GitHub runners:

| Shard | Before | After | Break |
|---|---|---|---|
| `rounding` | 54.1 % (370 killed, 17 timeouts, 264 survived, 64 no coverage) | 59.9 % (418 killed, 10 timeouts, 231 survived, 56 no coverage) | 57 |
| `boolean-1` | 86.5 % (317 killed, 55 timeouts, 45 survived, 13 no coverage) | 80.0 % (334 killed, 10 timeouts, 73 survived, 13 no coverage) | 77 |
| `boolean-2` | 60.8 % (382 killed, 8 timeouts, 199 survived, 53 no coverage) | 65.6 % (454 killed, 7 timeouts, 161 survived, 81 no coverage) | 63 |
| `arrangement-1` | 83.0 % (516 killed, 164 timeouts, 113 survived, 26 no coverage) | 85.8 % (440 killed, 263 timeouts, 103 survived, 13 no coverage) | 67 |
| `arrangement-2` | 94.7 % (668 killed, 354 timeouts, 29 survived, 28 no coverage) | 95.2 % (566 killed, 461 timeouts, 24 survived, 28 no coverage) | 72 |
| `planar` | 60.0 % (348 killed, 12 timeouts, 189 survived, 51 no coverage) | 69.0 % (402 killed, 12 timeouts, 157 survived, 29 no coverage) | 67 |

"Before" is the first run with the existing tests (for the two boolean shards the run with the split; for the
arrangement the first run whose span matched), "after" the run with the tests below, on the merged master of
27 October. `boolean-1` went down: `ExactMeshBoolean.cs` changed in between (the classification of open input
was reworked), and its timeouts dropped from 55 to 10 (mutants that timed out before now finish and survive);
`boolean-2` has more uncovered mutants for the same reason (new code that no test of the shard reaches yet).
The scores vary between runs on the same code: a run of the arrangement shards with a 10 s timeout gave
`arrangement-1` 94.7 % (529 killed, 247 timeouts, 30 survived) and `arrangement-2` 95.2 %.

### What the survivors showed

New tests, written from the survivors:
- **`MeshRoundingRepairStrategyTests`** (`rounding`). The tests of the whole repair passed with the edge flip, the
  zip of two caps or the move of a cap's apex broken, because another edit or a later pass fixed the mesh as well,
  and no test looked at the counts (`Flips`, `Zips`, `Moves`) or at where the vertices went. Each edit now has a
  hand-built mesh where only it can fix the defect: the counts, the topology and the positions are checked. Also
  the removal of thin closed components against thick and open ones (`maxLength / sum` for `maxLength * sum`, and
  `||` for `&&` in the closed check, survived), and `Apply` twice.
- **`ExactTriangulation2Tests`** (`planar`). The class was only tested through the boolean, which never hands it
  collinear points, points on the outer edges or invalid input. Now: random grid points (many collinear, a third
  on the outer edges) and non-crossing constraints with exact integer predicates, checked against the definition
  of a constrained Delaunay triangulation (tiling, constraints present, locally Delaunay); a constraint across many
  edges; the documented exceptions.
- **`ExactPlanarSimplifyTests`** (`planar`): a box standing on another, turned by 45 degrees (the difference is
  the box: 8 vertices, every crossing point removed), and the retriangulated regions are Delaunay.
- **`ExactBooleanFilterTests`** (`boolean-2`). The floating-point filters of `ExactMeshBoolean` (`orient3d_filtered`,
  `plane_side_filtered`) were only reached through the booleans. Now: where a filter decides, its sign must hold
  for every exact input within the documented error of the doubles, at all corners of the error boxes, for points
  moved off the plane by growing powers of two (the first decided ones are close to the bound); it must decide for
  separated points, and not for tiny, NaN or infinite ones.
- The `ExactVertices` tests of `MeshRoundingRepairTests` cover the exact result coordinates (`ComputeExactVertices`,
  `ToInputExact` with scaled input): they were in no filter of the boolean and arrangement shards.

Groups of survivors that are equivalent or practically equivalent (no test for them):
- **Filter bounds** (`orient3d_filtered`, `planes_approx`, `segment_point` and the other bounds in
  `ExactMeshArrangement`): mutants that drop a term or shrink a factor of a worst-case bound. The bounds are
  Hadamard and Shewchuk bounds with margin; the corner tests above pass even with the input-error factor of
  `orient3d_filtered` divided by three. A wrong sign needs an input constructed for that worst case (as found for
  the InSphere permanent, see above). Mutants that make a filter decide wrongly by more are killed.
- **Performance only**: the point-location walk of `ExactTriangulation2` (its pseudo-random start, the step limit,
  the linear-search fallback), the dirty-triangle tree and the retry sets of `MeshRoundingRepair`, the
  `queued`/`pending` sets of the Delaunay flips, hash and key bit operations, `Counts` keys and statistics
  counters (`windingExactSides`, `LinearSearches`).
- **Guards that the preconditions or earlier checks make unreachable**: in `ExactTriangulation2.crosses` the
  shared-end test and `o1 * o2 >= 0` (with no point on a constraint a collinear end is never inside the segment),
  `s == 0` in `walk_crossings` (no vertex on a constraint), `sc < sd` against `sc <= sd` in `try_zip` (equal values
  returned before), the `tid < 0` undo of `try_zip`; in `ExactPlanarSimplify` the sign normalization of the plane
  (either sign is a canonical form), the orientation of a group (opposite orientations of one plane never meet in
  one fan of a closed result), the reverse direction in `is_link_edge`.
- **The pass loop of `MeshRoundingRepair.Apply`**: one pass more or less, or a sum of the counts that is zero
  for other edits, ends with the same mesh, because a pass without edits changes nothing.

Open (real gaps, not yet tested):
- `MeshRoundingRepair`: the contraction to the midpoint of an edge (both ends fail, the midpoint not), and
  `overlaps`, which is never called (dead code, reported).
- `ExactPlanarSimplify`: the convexity check before a Delaunay flip (`&&` for `||` survives: no test has a
  non-convex quad whose diagonal fails the incircle test).
- `ExactMeshBoolean`: the generalized winding numbers of open input (`front_generalized_winding`, the 1/2
  threshold), which another change is reworking; edges with more than two triangles (`pair_around_edge`, the
  order around the edge for ties); the fallback for rays through an edge (`front_winding`, no coverage).
- `ExactMeshArrangement` (`arrangement-2`): the clipping of coplanar triangles into each other (`clip_into`, `|=`
  against `&=`, the corner index), the strict and non-strict orientation tests of the segment crossings, and the
  padding and slack of the candidate boxes (larger boxes only cost time). A third of its mutants time out: a
  broken arrangement makes the boolean loop, which counts as killed but makes the shard slow.

**Second pass (28 October 2026, run 40623882573 on the master state).** Read from the job logs (step "Surviving
mutants"; the artifacts are not reachable from the cloud sessions):
- `rounding` 59.9 % (406 killed, 22 timeouts, 231 survived, 56 no coverage). 30 of the 56 uncovered mutants were the
  unused `overlaps` (removed in #185). New tests (`ExactBooleanSurvivorTests`): `Apply` on an empty mesh (the early
  return was uncovered), and an instance reused on another mesh: without `touched.Clear()` a thin closed component
  without defects was removed because the vertices with the same ids had been edited in the first run. Equivalent or
  practically equivalent, no test: `changed.Clear()` at the start (reassigned before its first read), `>=` against
  `>` on the sums of the edit counts (they only decide whether to recount the pairs, see above), `area(t) <= areaEps`
  (equal only when the area is exactly the bound), the dirty-triangle tree and `version` (performance only, see
  above). Not equivalent, but untested by design: the comparer of the sort of the defects (an order of equally
  plausible edits, a heuristic without a specified result). Open: `--pass` (the loop ignores `MaxPasses` and retries
  every defect in every pass; a test with `MaxPasses = 1` on a mesh that needs two passes would kill it), the stale
  boundary state on reuse with another mesh (`inputBoundary`, `inputBoundaryEdges`, `slidTowards`), the `-0.0`
  normalization of the position keys (`p.x + 0.0`), the bowtie and boundary guards of the contraction, the guards of
  `try_zip` and the moves (`fix_cap`, lines 839-915 of that run), whose effects need hand-built meshes per guard.
- `boolean-2` 65.6 %: the paths of open input that cannot be paired around an edge (`ConflictEdges`, the
  `append_triangle` fallback with copies of vertices, `can_add_edge`) had no coverage because `ExactBooleanOpenInputTests`
  was in no filter; it is now in both boolean shards. The exact midpoints of split edges (`ComputeExactVertices` where
  the surface is connected around both ends of a touching edge) had no coverage: `ExactBooleanSurvivorTests` checks
  them, also with two ends of different denominators. Equivalent: the filter bounds (see above), the `>>` against `>>>`
  of non-negative edge keys. Diagnostics only (public, unchecked): the `Counts` keys, `exactSides--`.
- The new boolean test classes (`ExactBooleanIssueCasesTests`, `ExactBooleanByDesignTests`) are in the filters of
  `rounding`, `boolean-1` and `boolean-2`.

Set `break` from a measured score as above and only raise it.
