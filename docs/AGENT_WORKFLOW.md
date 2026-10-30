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
| **S** | docs, comments, a one-line fix, benchmark README | One session. Local CI steps. The author re-reads the diff. No extra review. |
| **M** | a bug fix or small feature outside exact code | All of S, plus: tests that fail on the old code (mutation check), the CLAUDE.md tests (invariance / reuse / determinism) and **one** independent review agent. |
| **L** | exact arithmetic, error bounds, `ExactMesh*`, a new module, spec-driven work | All of M, plus: a design doc or spec first, reviewed by the owner; **hidden acceptance tests** written by a separate agent from the spec; a bounds review; mutation runs; benchmark A/B numbers. |

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
- **Report**: one PR per topic, and numbers with the command that produced them. The worker reports to the
  coordinator and does not merge.

## 3. Execution-grounded loop

- The inner loop is fast: filtered `dotnet test` runs and a small benchmark subset. Run the full local CI steps
  before every push.
- **No claim without output.** Every number in a PR names the command and its raw output (as
  `benchmarks/BooleanComparison/perf/` does). The reviewer re-runs a sample.
- Machine checks replace review where possible ([QA.md 4a](QA.md#4a-making-agent-work-verifiable), items 1-3):
  - new tests must fail on the old code;
  - package validation against 1.0.324;
  - banned APIs in exact code.

## 4. Review: separate and evidence-based

- The reviewer gets the diff, the spec / brief and [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md). It does not get the
  author's reasoning.
- A finding cites a concrete path: input → wrong result, ideally as a failing test. Findings without one are
  suggestions and do not block.
- **Learning loop**: every fault class found in review that is not yet on the checklist is added to it in the same
  PR.

## 5. Hidden acceptance tests (tier L)

For spec-driven work, a separate agent writes acceptance tests from the spec alone: worked examples and failure
catalogues (MESHING2D_SPEC F-*, QUADTREE2D_SPEC D-*). The implementing session sees their results but may not
change them. This prevents tests that are tailored to the code.

## 6. Context and sessions

- One session per PR-sized topic. Prefer fresh sessions over long ones. One large session at a time.
- The plan lives in the repository ([ROADMAP.md](ROADMAP.md)), not in chat. Results live in PRs and READMEs.
- A session that stops before its work is complete writes a hand-over document: what was tried, with numbers;
  ranked open ideas; guard rails (example: [perf/EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md)).

## 7. Anti-slop rules for the code itself

- No new abstraction, option or extension point without a current second user or a test that needs it.
- Every new option has a test and a line in the docs. Defaults are chosen by measurement (example: SafeRounding,
  #189).
- Prefer deleting to adding. Dead code found by mutation runs is removed (#185).
- Keep diffs small. A PR that grows beyond its brief is split.

## 8. Cost controls

- Mutation runs on ready PRs cover only the shards the PR touches, with full runs weekly; drafts run none. Thresholds
  only go up (a ratchet).
- The coordinator's check-in interval follows the CI time (hours for mutation runs, not minutes). Workers report in
  one line, and sessions are archived when their PRs are merged.
- Near the usage limit, finish running work only and start nothing new.
