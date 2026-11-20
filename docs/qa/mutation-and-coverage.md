# Mutation Testing & Changed-Line Coverage

Back to [QA Hub](README.md) · [docs/QA.md](../QA.md)

---

## 2.2 Mutation testing in CI for the exact parts

The exact arithmetic is the base of everything else, and a wrong error bound in it fails only on rare
inputs. Stryker.NET (Apache-2.0) mutates `math/ExactPredicates.cs`, `math/ExactPoint3.cs` and
`math/AdaptivePredicates.cs`, and runs the predicate tests against each mutant.

- A workflow reports the mutation score and the surviving mutants. Since 2026-11-16 (#958, the Exact Boolean 3.0 process) it runs in full on
  master Monday to Saturday for the nightly group (`boolean-*`, `arrangement-*`, `rounding-*`: 24 shards) and on Sunday for the weekly group (the
  other shards and the arithmetic job), and on demand (`shards=all` or a list); on ready PRs only incrementally for the configs they touch, and a PR
  makes no full shard run, except the merge gate of a PR that touches exact arithmetic ([tests/mutation/README.md](../../tests/mutation/README.md), "3.0 process").
  A PR that changes a mutated file runs incrementally: Stryker mutates only the changed lines, the other mutants keep their result from the
  baseline of the last full run (its report, kept in the Actions cache, matched by a line diff), and the break
  threshold applies to the combined score of the shard; without a baseline the shard runs in full.
- A score below the break threshold fails a full run (a run for changed tests only reports). It starts at the measured score (rounded down, less one
  point for timeouts that vary between runners) and only goes up.
- Surviving mutants are either killed with a new test or recorded as equivalent, with a reason.
- The score is 87.05 % (October 2026, from 83.4 % at the start); the break threshold is 86. Details are in
  [tests/mutation/README.md](../../tests/mutation/README.md).
- **The exact mesh pipeline** (October 2026): a second job, `exact-mesh`, mutates `mesh_ops/ExactMeshBoolean.cs`,
  `ExactMeshArrangement.cs`, `ExactPlanarSimplify.cs`, `ExactTriangulation2.cs` and `MeshRoundingRepair.cs` in six
  shards that run in parallel (1 to 5 hours each; since then split further: `tests/mutation` has 45 mesh configs, `rounding-1` .. `-13` among them, #843, #1006). The survivors led to three new test classes
  (`MeshRoundingRepairStrategyTests`, `ExactTriangulation2Tests`, `ExactBooleanFilterTests`) and cases in
  `ExactPlanarSimplifyTests`: each edit of the rounding repair on its own, the triangulation against the definition
  of a constrained Delaunay triangulation, the boolean's own filters against exact signs. Scores per shard, break
  thresholds, the equivalent groups and the open gaps are in [tests/mutation/README.md](../../tests/mutation/README.md).
- Next targets:
  - the open gaps of the mesh shards (README);
  - the result checks of the repair, such as `NotCleanReason` and `MeshRepairMetrics`;
  - the open survivors in `AdaptivePredicates`.
- A survivor in a result check is a silent-success risk.
- This does not replace the manual mutation check that CLAUDE.md asks for: it covers code the manual
  check never revisits.

## 2.6 Coverage in CI

- coverlet.collector (MIT, pinned 6.0.4) collects Cobertura coverage in its own workflow, `.github/workflows/coverage.yml`,
  **not on pull requests**: coverlet makes the suites about 3x slower (a first version in the PR jobs took 59 min for
  the core fast tier alone, and eight parallel shards were limited by the runner quota), so the gate runs cyclically
  (owner decision 2026-11-10): every night on master (dispatched at 03:45 UTC, the `schedule` cron is the fallback, see
  section 2.12) and on demand (`workflow_dispatch`, optional `ref`
  input: a branch is then checked against the last evaluated master run) and once on a pull request that changes the workflow or the gate (that run reports but never fails: its findings belong to other pull requests). The existing Microsoft.NET.Test.Sdk 17.8.0
  supplies the integration; Windows/net48 runs do not collect coverage.
- The `core` jobs run the core suite in four shards by test class (`tools/coverage/shard_filter.py`), **including the
  Slow tier** (nobody waits for this run), without Nightly; the `gate` job runs the CLI and Clipper2 package suites and
  merges the reports (per line the maximum of the hits; several reports per suite are shards). Two tests with a
  wall-clock budget (`RobustnessSweepTests.Sweep_SameResultUnderSimilarity`, `UVAtlasTests.UVOverlap_MixedSizes_NotQuadratic`)
  fail under the instrumentation and are excluded **in this run only**; the normal CI jobs keep enforcing them. A failing
  test does not fail a collection step (a missing report fails the gate); `SingleHit` in `tests/coverage.runsettings` (one hit per line is all the gate needs) avoids coverlet's per-hit counters, which made one shard of the first run exceed 150 min; `--blame-hang-timeout` names a hanging test.
- `tools/changed_line_coverage.py` requires **100% of the changed executable lines** in the core library,
  `addons/geometry3Sharp.Clipper2` and `tools/g3cli` to have at least one hit across those runs. Existing unmodified gaps do
  not fail it. There is no whole-library or branch-coverage threshold.
- The comparison base is the HEAD of the last run that evaluated its lines (its `gate-result` artifact, uploaded for exit
  code 0 and 1, not after incomplete evidence), else the master of 24 hours ago. With `--attribute` the report names, per
  file, the pull request that introduced each uncovered line (git blame, then the first-parent merge that brought the
  commit in, else the `(#N)` of a squash subject, else the short hash). The job summary lists them and the scheduled run
  fails when there are any. A nightly report blocks no pull request: uncovered lines become follow-up work (a test) for the owning session. There
  is no exemption marker in the code; the only exception is the reviewed list below.
- **Unmeasurable lines** (`tools/coverage/unmeasurable_lines.json`, owner decision 2026-11-11): the one place where an
  uncovered changed line is excused, for a line that no coverage run (net10.0) can hit: a branch that runs on .NET
  Framework only, the catch of a platform probe, an invariant guard. Dead code is deleted, not listed; invariant guards are the
  exception, kept on purpose: the two in `ExactMeshBoolean.cs` (deleting them needs the exact-code review and the 6 h
  Boolean shards for no gain) and five in `DualContouring.cs` (a hot, subtle file where deleting a guard would turn a
  broken invariant into a silent wrong result), and, from #959 (owner 2026-11-16), the walk and insertion guards of `Delaunay3.cs`
  (point location and the star-shape growth, which the exact predicates make unreachable) and the requeue and lemma-check
  branches of the round carving in `MeshAlphaWrap.cs`, each with its reason in the list. An entry is `{file, text, after, reason, approved_in}`: the line is the one with that stripped
  text whose nearest previous non-blank lines are `after` (a list, nearest first), unique in the file, so a new line of
  the same text elsewhere cannot take its place and edits elsewhere do not matter; it excuses that one line only;
  `approved_in` is the PR (`#123`) whose review approved the entry. The list is checked on every run, not only for
  changed lines: an entry whose line is gone or not unique, is not an executable line of the reports any more, or is covered
  now **fails the gate** (exit 1, no stale entries). A malformed list, or an explicitly given list that does not exist, is
  exit 2. The summary counts the excused lines next to the covered ones.
- Limits, stated so that nobody reads more into a green run than it says: (1) only lines with a sequence point are
  measured; changed lines without one (continuation lines of a statement, code in a conditional-compilation branch the
  test build does not compile such as `#if NETSTANDARD2_0`, methods with `[ExcludeFromCodeCoverage]`) are listed as
  `unmeasured_lines` in the JSON and counted in the summary, not failed; (2) hits are merged by maximum per file and
  line over all class records and suites, so a line that several class records list (field initializers copied into each
  constructor, generic instantiations, iterator state machines) counts as covered when one of them ran; (3) a renamed
  or moved file is checked as new code (`--no-renames`), its old gaps are reported and attributed by `git blame` to the
  PRs that wrote them; (4) a finding is reported once: the next run starts at the HEAD of this one, so lines nobody
  covered do not come back unless the work-list from the artifact is kept; (5) `--expect-shards core=4` makes a core
  shard without a report incomplete evidence (exit 2), but a test class that matches no shard filter would be missed
  (it shows as uncovered lines, never as a false pass).
- The base is only accepted from a run whose HEAD is an ancestor of master (a manual run of a branch on master does not
  move it). Added files and renames are checked as added lines; deleted lines are ignored. Coverlet's class-level line records
  define executable lines, so comments and whitespace within a measured file need no hits. Missing, malformed or empty suite reports fail the gate (exit 2). A changed source file that no report lists (`files_without_executable_lines` in the JSON; the first run found `core/ExactCodeAllowList.cs`, a data table) has no executable code, because coverlet lists every method of the instrumented assemblies, hit or not: it is reported and does not fail the run. Tests, benchmark code, documentation examples, assembly metadata
  and build tools are outside this gate. The unit tests of the gate run in the `linux` job of `ci.yml`.
- Optional `--slow-coverage-dir` (a second core report over `Category=Slow` only) reports lines that only the Slow tier
  hits as `covered_only_by_slow`; the cyclic run does not need it, because it collects the Slow tier with the rest.
- Local reproduction from a clean committed checkout, using the same SDK as CI:

  ```sh
  rm -rf test-results
  dotnet test tests/geometry3Sharp.Tests -c Release --filter "Category!=Nightly" --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/core
  dotnet test tests/g3cli.Tests -c Release --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/cli
  dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory test-results/clipper2
  python3 -m unittest discover -s tools -p 'test_changed_line_coverage.py' -v
  python3 tools/changed_line_coverage.py --base origin/master --attribute --coverage-dir test-results --json test-results/changed-line-coverage.json
  ```

  (the core run takes about an hour with coverage; `--filter` on the classes of the changed files is enough locally).
  Exit codes: 0 covered/no changed executable lines, 1 uncovered lines, 2 incomplete or invalid evidence. Use a fresh
  results directory: a stale report left in it would mask a gap. Coverage demonstrates execution, not assertion quality;
  mutation gates remain.
