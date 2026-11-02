# How we work with agents

The goal is better results at a bounded cost, with as little process as the task allows. Two findings from the
research and from our own PRs shape it:
- Quality comes from **external verification**, not from the model checking itself. Compilers, tests, mutation runs,
  benchmarks and a reviewer that did not write the code all count as external. Self-correction without external
  feedback is unreliable (Huang et al. 2023, "Large Language Models Cannot Self-Correct Reasoning Yet").
- **Narrow tasks** with a fixed scope, interface and acceptance criteria give far less slop than open ones
  ("make feature X").

Large role-play setups (MetaGPT, ChatDev) cost a lot for mixed gains. We use only the separations that measurably
help: specification vs implementation, and implementation vs review and acceptance tests.

## 1. Tiers: the process scales with the risk

| Tier | Examples | Process |
|---|---|---|
| **S** | docs, comments, a one-line fix, benchmark README | One session. The author re-reads the diff. No extra review. Code changes run the local CI steps; docs-only changes (only `*.md`) run no CI, locally or on GitHub. |
| **M** | a bug fix or small feature outside exact code | All of S, plus: tests that fail on the old code (mutation check), the CLAUDE.md tests (invariance / reuse / determinism) and **one** independent review agent. |
| **L** | exact arithmetic, error bounds, `ExactMesh*`, a new module, spec-driven work | All of M, plus: a design doc or spec first, reviewed by the owner; **acceptance against references** ([strategy](design/ACCEPTANCE_STRATEGY.md): oracle, differential, golden master); a bounds review; mutation runs; benchmark A/B numbers. |

When in doubt, choose the higher tier for exact code and the lower tier for everything else.

## 2. The task brief (scaffold)

Every worker session gets a brief with these fields, and nothing open-ended:

- **Goal**: one sentence, and the user-visible result.
- **Scope**: the files or classes it may change.
- **Non-goals**: what it must not change, for example the API (compatible with upstream 1.0.324).
- **Interface**: signatures, status / result types, and allocation rules (eg zero allocation per query).
- **Acceptance**: the tests and metrics with numbers. Examples: "spec §2.3 example holds exactly"; "height query
  ≤ 2.5 µs on the §4.2 data"; "no regression in BooleanComparison clean counts".
- **Sources**: allowed and forbidden ones (papers, MIT/BSD/Apache; never GPL; clean-room rules).
- **Budget and stop rule**: for example "at most N rounds without a measured gain"; "stop after tasks 1-7"; a
  hand-over document when stopping early.
- **Report**: one PR per feature, and numbers with the command that produced them. The worker reports to the
  coordinator and does not merge.
- **One PR per feature, not per sub-step** (owner, 2026-11-01): build a feature as several clean commits in one PR,
  each commit reviewable and green on its own. A design doc goes into the first code PR; its approval stays a separate
  step (a message or comment) before code is built on it. Keep a PR reviewable in one pass; when it grows beyond that,
  split by feature, not by step. Stack a PR on another only when the base is ready and its mutation run has passed:
  stacking on an unmerged base cost retargets, duplicate CI and a skipped mutation job on 2026-11-01.

## 3. Execution-grounded loop

- The inner loop is fast: filtered `dotnet test` runs and a small benchmark subset. The full local CI steps run
  before a push that changes existing library code; for changes to tools, tests or new files only, run the filtered
  tests, the builds of all targets including net48, and `dotnet pack` (its API validation against 1.0.324). GitHub
  CI runs the full suite on every push anyway. Docs-only pushes skip all of it.
- **Upper bound before a performance spike**: measure the share of the targeted work and multiply by the possible
  speedup first (a probe or a micro benchmark, minutes). If that bound is below the measurement noise, the stop rule
  applies without full A/B runs (`run_classes.sh` and the like).
- **Look before you build** (owner, 2026-11-01): before designing an optimization or a new algorithm, do two short
  checks and record them in the design note or PR:
  1. **The library:** search it for existing solutions and building blocks (predicates and filters, `tools/
     PredicateCompiler`, spatial indexes, the "Shared hotspots across sessions" table in ROADMAP.md, OPERATIONS.md).
     Today's examples: the boolean's kernel B and the self-intersection filter hit the same exact-`Orient3d` hotspot,
     and the envelope reused the indirect predicates.
  2. **The literature:** papers, standards and documentation on the problem (e.g. the normal-cone culling for
     self-intersection checks, OGC SFA for polygon nesting, snap rounding). The license rules apply: implement from
     papers, port only MIT / Apache / BSD, never GPL code, never Triangle / Triangle.NET. Cite the sources in
     docs/SOURCES.md.
- **No claim without output.** Every number in a PR names the command and its raw output (as
  `benchmarks/BooleanComparison/perf/` does). The reviewer re-runs a sample.
- Machine checks replace review where possible ([QA.md 4a](QA.md#4a-making-agent-work-verifiable), items 1-3):
  - new tests must fail on the old code (CI job `New tests on base`; required for PRs labelled `bugfix`);
  - package validation against 1.0.324 (enforced in `dotnet pack`);
  - banned APIs in exact code (enforced at build time: FMA, `float`, parallel loops outside the allow-list).

## 4. Review: separate and evidence-based

- The reviewer gets the diff, the spec / brief and [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md). It does not get the
  author's reasoning.
- A finding cites a concrete path: input → wrong result, ideally as a failing test. Findings without one are
  suggestions and do not block.
- **Learning loop**: every fault class found in review that is not yet on the checklist is added to it in the same
  PR.

## 5. Acceptance against references (tier L)

[Strategy](design/ACCEPTANCE_STRATEGY.md): an oracle by definition that shares no code with the
implementation, a differential against other libraries, then a golden master of accepted results. Independence comes from those references, not
from secret tests, which a public repository cannot keep.

## 6. Context and sessions

- One session per PR-sized topic. Prefer fresh sessions over long ones. One large session at a time.
- The plan lives in the repository ([ROADMAP.md](ROADMAP.md)), not in chat. Results live in PRs and READMEs.
- A session that stops before its work is complete writes a hand-over document: what was tried, with numbers;
  ranked open ideas; guard rails (example: [perf/EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md)).
- A bugfix must not run into a fundamental problem that a planned concept will solve (owner 2026-11-02). Before
  fixing, check [ROADMAP.md](ROADMAP.md) (Phase 5, Shared hotspots, Open tasks) for a concept that covers the same
  cause, eg inside / outside of broken inputs (Phase 5 #5), exact predicates and symbolic perturbation, scale-relative
  tolerances, envelope / `IsWithin` fidelity. If one exists: make the smallest fix that is correct on its own and
  compatible with that concept (no second, competing mechanism), say so in the PR, and leave the general solution to
  the concept; if no such small fix exists, pin the case and report it instead of building a local workaround.
- Waiting time is work time. While a session waits (on CI, a mutation run, a long benchmark, a review, a decision of
  the owner or a merge), it uses the time for work that does not touch the branch under test and that the next step
  needs anyway: the literature and building-block search for its next item (section 3), an upper-bound measurement,
  a profile, a diagnosis of a finding, tests or test data for the next step, or a draft of the docs. It does not
  start a second PR-sized topic, does not push to the branch whose CI or mutation run is running (a push cancels
  it), and reports results in the same one-line way. If there is nothing useful left, it says so and waits.

## 7. Anti-slop rules for the code itself

- No new abstraction, option or extension point without a current second user or a test that needs it.
- Every new option has a test and a line in the docs. Defaults are chosen by measurement (example: SafeRounding,
  #189).
- Prefer deleting to adding. Dead code found by mutation runs is removed (#185).
- Keep diffs small. A PR that grows beyond its brief is split.

## 8. Cost controls

- Mutation runs on ready PRs cover only the shards the PR touches, with full runs weekly; drafts run none. Thresholds
  only go up (a ratchet).
- Stryker runs locally once per shard, to set its threshold; after that the CI mutation run on the PR is the gate.
  Since #230 a PR's run is incremental (only the mutants of the changed lines, the rest from the baseline of the last
  full run, [tests/mutation/README.md](../tests/mutation/README.md)); a shard without a cached baseline still runs in
  full, so merge master into a PR before marking it ready. A new push cancels a running mutation run (a full one takes
  4-5 h): while one runs on your PR, collect fixes and push once, and merge master only when needed (a conflict,
  or once before ready), not after every merge on master. Hand mutants of generators and rules stay (fast and
  targeted).
- The coordinator's check-in interval follows the CI time (hours for mutation runs, not minutes). Workers report in
  one line, and sessions are archived when their PRs are merged.
- Near the usage limit, finish running work only and start nothing new.
