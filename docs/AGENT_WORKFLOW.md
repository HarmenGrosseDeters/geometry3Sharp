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

## Credo: correct by construction, fast by design

- Correctness is not negotiable; a speedup that changes results needs a guarantee and the owner.
- Complexity and data structures right from the start; constant factors later, bit-identical.
- Pay for exactness only where needed: a floating-point filter, exact on the close cases.
- A time budget against a reference is part of the brief; measure the upper bound before the design.

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

- **Prior-art check at decision points** (owner, 2026-11-03): a short check of how others solve it (about 15 min,
  3-5 sources: papers, standards such as IFC / OGC, the approach of established libraries; for copyleft or clean-room
  sources the approach only, never code). Required when (1) options go to the owner for a decision, (2) a finding hits
  a fundamental limit (representation, precision, termination, complexity, the spec's definitions), not a local defect,
  or (3) a new mechanism is introduced. Not for local bugfixes, guards, tests or measurements. Every option list to the
  owner carries a line "how others solve it" with sources, or "nothing found" with the terms searched. The coordinator
  passes no decision question on without that line. (Case: the UTM precision limit of the terrain LocalFrame went to
  the owner as three options without it; local frames with an origin are standard (IFC map conversion, relative-to-centre
  coordinates), and the follow-up, rounding the output to a grid with guarantees, has an established answer in snap
  rounding.)

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
     PredicateCompiler`, spatial indexes, the "Shared hotspots across sessions" table in [roadmap/hotspots.md](roadmap/hotspots.md), OPERATIONS.md).
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

- One session per PR-sized topic. Prefer fresh sessions over long ones. At most 5 implementation sessions at
  once, at most 2 of them large (tier L), plus one review session ([ROADMAP.md](ROADMAP.md#session-limits)).
- The plan lives in the repository ([ROADMAP.md](ROADMAP.md), details in [roadmap/](roadmap/)), not in chat. Results live in PRs and READMEs.
- Roadmap upkeep (owner 2026-11-12, after the audit #649 found the roadmap days behind):
  - A PR that starts, finishes or changes the scope of a roadmap item updates that item in the same PR: status,
    date, PR numbers, and the headline numbers (totals only). The reviewer checks it (REVIEW_CHECKLIST section 11).
  - The coordinator updates "Now" in [ROADMAP.md](ROADMAP.md) in the same step as each merge, a new tier-L topic, an
    owner decision that changes direction or targets, and a closed-out phase; owner decisions are recorded with
    their date, not left in chat.
  - Once a day, with the morning summary, the coordinator compares "Now" and the open items against the merged PRs
    and open issues since the last check and fixes any drift in one docs commit. A status that cannot be settled
    from git and GitHub goes to the owner as a question, not into the docs as "(status unclear)".
  - Every owner decision is written into the roadmap the moment it is made, in every place that lists the item:
    "Open decisions", "Next", "Later" and the subpage. Closing an item in one place and leaving it open in another
    is drift.
  - The daily check covers every section of [ROADMAP.md](ROADMAP.md), "Open decisions" and "Later" included, and
    the subpages' "Open" sections. Each pending item is checked against the owner's recorded decisions, not only
    against merges (owner 2026-11-12, after an audit found five Exact Boolean decisions, the oldest from 2026-11-01,
    still listed as pending).
- A session that stops before its work is complete writes a hand-over document: what was tried, with numbers;
  ranked open ideas; guard rails (example: [perf/EXACT_BOOLEAN_PERFORMANCE.md](perf/EXACT_BOOLEAN_PERFORMANCE.md)).
- A bugfix must not run into a fundamental problem that a planned concept will solve (owner 2026-11-02). Before
  fixing, check the roadmap ([Phase 5](roadmap/research.md#phase-5-candidates-from-recent-research), [shared hotspots and open
  tasks](roadmap/hotspots.md)) for a concept that covers the same
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
- Full runs after the merge (owner 2026-11-09): only the incremental run (changed lines against the master baseline)
  gates a PR. A PR that would need full shard runs (no master baseline, a changed config or mutation tooling, an
  integration PR) merges on green CI and review; the coordinator cancels its PR mutation run and dispatches the full
  run of the same shards on master, which seeds the baselines and is the gate (below a threshold: a follow-up PR with
  tests; a revert only for a real defect). Reason: a full rounding part takes up to 5 h, and the PR run and the master
  baseline would test nearly the same tree twice.
- Stryker runs locally once per shard, to set its threshold; after that the CI mutation run on the PR is the gate.
  Since #230 a PR's run is incremental (only the mutants of the changed lines, the rest from the baseline of the last
  full run, [tests/mutation/README.md](../tests/mutation/README.md)); a shard without a cached baseline still runs in
  full, so merge master into a PR before marking it ready. A new push cancels a running mutation run (a full one takes
  4-5 h): while one runs on your PR, collect fixes and push once, and merge master only when needed (a conflict,
  or once before ready), not after every merge on master. Hand mutants of generators and rules stay (fast and
  targeted).
- The coordinator's check-in interval follows the CI time (hours for mutation runs, not minutes). Workers report in
  one line, and sessions are archived when their PRs are merged.
- Messages (owner 2026-11-08): sessions write to the coordinator only for results, decisions needed or blockers, not
  for progress; several items go in one message. A session with nothing to do while its PRs wait pauses (no
  check-ins, no polling) until the coordinator messages it.
- Merge queues behind the mutation gate (owner 2026-11-08): when several ready PRs touch the same shards, one
  integration branch from master merges them all and gets one mutation run for the affected shards; if it is green,
  one PR from the integration branch merges into master (owner 2026-11-08: so master gets exactly the tree the gate
  tested, including any conflict resolutions; the queued PRs then show as merged). Survivors are attributed by line
  to the PR that changed it. A gate
  whose new thresholds come from a run on exactly the PR's code may be decided on that run without a rerun after a
  thresholds-only commit (#403).
- Near the usage limit, finish running work only and start nothing new.
- Medium sessions on a mid-size model (Sonnet, owner 2026-11-08) take bounded, well-specified tasks (benchmark
  classes, gates, small fixes). They escalate hard or risky decisions (correctness arguments, error bounds, licences,
  protocol choices) to the coordinator with the options and their leaning, instead of guessing; the coordinator asks
  them proactively, too.
- Runner sessions (owner 2026-11-08): long runs (benchmarks, corpus sweeps, A/B timings, CGAL or other black-box
  comparisons) go, whenever possible and useful, to a separate runner session on a small model (Haiku) instead of the
  implementing session. It runs jobs only: no code, docs or design decisions, no PRs; it takes jobs only from its
  implementing session and the coordinator, one job at a time on its own machine (so timings are alone on the machine),
  and reports summary tables; raw results go to a data branch `runs/<topic>` (no PR, never merged; no meshes, no
  foreign outputs). The implementing session writes each job completely (commit, build and run commands, analysis
  script), because the runner executes literally and asks instead of improvising; analysis logic lives in committed
  scripts. Baseline and candidate of a timing comparison run in the same job on the same runner. Not for diagnosis,
  profiling interpretation or anything that needs judgement; small probes stay in the implementing session. The
  coordinator starts the runner on request.
  The owner's machine (owner 2026-11-08; i7-8700K, 6 cores / 12 threads, 16 GB) can serve as a runner: the owner
  starts a Claude Code session there (desktop app in the repository folder, or `claude remote-control`), which then
  takes runner jobs under the same rules. Good for long result-only runs (CsCheck campaigns, fuzzing, local Stryker
  runs to set thresholds) and for relative timings with baseline and candidate in the same job; acceptance timings
  stay on the cloud reference machine. Never a self-hosted GitHub runner for this public repository (fork PRs could
  run code on it).
  Hand-over to the owner's machine goes only through the repository (owner 2026-11-08): no trust cards
  (confirm_repository_trust) for cloud sessions, because a session allowed to reach the owner's machine is then
  limited to trusted devices for good and normal cloud sessions can no longer message it, while the messaging to the
  local session still did not work. Instead: jobs are files on a `runs/` branch (`jobs/JOB-<n>.md`, complete and
  literal); the local session waits for a new job file with one harness-tracked background task (a `git fetch`
  loop that exits when the file appears; waiting costs nothing) and starts it; results and a `REPORT-<n>.md` go to
  the same branch, and the coordinator reads them there at its check-ins. A message to a cloud session is sent
  only if the local session's tools allow it.
  On the owner's machine (no container recycling) a job step longer than the 2-h limit of a harness-tracked task may
  run as a detached process (owner 2026-11-08): record its PIDs and start time, keep every step resumable, and write
  `.exit` / `.end` files when it ends; the local session evaluates when the owner asks or when the step has ended.
  For local Stryker runs, `--open-report` shows the live HTML dashboard in the owner's browser.
  **Local tasks as issues** (owner 2026-11-11): small and medium tasks for a session on the owner's machine are
  GitHub issues with the label `local`, each with the complete §2 brief. The local session works one as follows:
  (1) branch `local/<issue>-<name>` from the current master when it starts (no branch or draft PR prepared in
  advance: it would go stale); (2) `CLAUDE.md` applies; (3) the files the cloud sessions hold are locked (during
  Exact Boolean 3.0: `mesh_ops/ExactMesh*`, the exact files of `.editorconfig`, `math/Exact*`,
  `math/AdaptivePredicates.cs`, and the files of open `claude/t1-*` and T3a PRs); a task that seems to need one stops
  and comments on the issue; (4) a draft PR with `Closes #<issue>`, pushed after the local checks of "Before every
  push"; the coordinator subscribes, reviews and merges; (5) no Stryker kill measurements on the owner's machine (CI is
  the gate). Result-only runs (fuzzing campaigns) report on the issue and put raw output on a `runs/` branch, as
  above; findings become `bugfix` issues.
  A job runs as a harness-tracked background task (the Bash tool's `run_in_background`, at most 2 h per chunk), not
  under `nohup`: an untracked process let the runner's machine be recycled three times in an hour (2026-11-08).
  Restarts: a worker restart kills running processes, and a replaced container loses local files. So jobs are
  resumable (one result line or file per case; a rerun skips finished cases; a timing job reruns an interrupted
  pair), the runner pushes partial raw results to its data branch about every hour, keeps a self check-in
  (`send_later`, every 15 min, owner 2026-11-08) while a job runs, and on every wake, including a restart notice, checks
  whether the job's process still runs and its output still grows; if not, it resumes the job and reports the
  restart and the cases rerun.
- Budget (owner 2026-11-02). A turn re-reads the whole context, so cost grows with context size, not with the work:
  - Small, bounded tasks (reading a paper, a license check, one measurement) go to a fresh short session with a
    precise brief, not to a long-running one.
  - One long topic per session; at a task boundary push everything, write the hand-over into the PR or a doc, and
    `/compact` or start fresh. Context over about 500k tokens is a reason to do so.
  - A session with nothing on the critical path pauses (idle costs nothing); the coordinator wakes it by message.
  - No parallel subagents for implementation; a subagent only for an independent review or a bounded search.
  - Measure on the smallest sample that can decide (tens of cases before hundreds), and grow it only if the decision
    needs it; long runs in the background, one report at the end.
  - Avoid CI round-trips: merge master once before ready, not after every merge on master; for disjoint PRs the
    coordinator may verify several together (master plus the PRs, local build and full suite) and merge them in one go.
  - Reports are short and only on milestones, decisions or blockers; no status-only messages.
  - Standing cost rules (owner 2026-11-08, for all future work):
    - Model by task: long runs on a Haiku runner, bounded medium tasks on Sonnet, design, exact code and their
      reviews on the large model.
    - Reviews: the first review is a fresh agent on the full diff; re-reviews of fixes resume that reviewer with the
      delta only.
    - Mutation gate: no push while a full mutation run is in flight on the PR (it cancels it); queued PRs on the same
      shards share one combined gate; a thresholds-only commit is gated on the run that measured the same code
      (see the merge-queue rule above).
    - Waiting is free, polling is not: sessions with nothing on the critical path pause until messaged; check-ins
      follow the length of the run they wait for; the coordinator ignores events about superseded PR heads.
    - Before building: an upper bound or a small probe decides whether the work is worth it ("not worth it now" is a
      valid result for the owner).
    - Targets are a floor, not a stop (owner 2026-11-09): when a target is met, the remaining levers are still
      measured (upper bound) and every one with a noticeable gain at reasonable effort is built, in order of gain per
      effort, until the bounds show nothing worth building. The stop rule only drops levers without a measurable gain.

