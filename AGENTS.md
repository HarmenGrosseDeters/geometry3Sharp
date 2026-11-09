# Instructions for coding agents

These rules apply to every agent working in this repository (Claude Code, GitHub Copilot, Codex and others).
The full rules are in [CLAUDE.md](CLAUDE.md), and how we work in [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md).
Read both before you change anything. If anything below conflicts with them, they take precedence.

## Essentials

- Answer the user in German. Code, comments, commit messages, PRs and docs stay in English.
- Times for the user are given in the Europe/Berlin time zone (CET / CEST, as in Amsterdam), not in UTC, eg
  "08:40 (Berlin)". Times in logs, commits and machine output stay as they are.
- Choose a tier (S/M/L, docs/AGENT_WORKFLOW.md §1) and work only within the task brief: its scope, non-goals and
  acceptance criteria.
- Every change comes with tests, if possible ones that fail on the old code; check them by breaking the change.
  - New or changed operations also get the invariance, reuse and determinism tests described in CLAUDE.md.
  - Success-path tests check the result (closed, manifold, finite), not only the status.
- **No claim without output.** Every performance or quality number names the command that produced it and comes
  with before/after values (`benchmarks/`).
- Before every push, run the local CI steps listed in CLAUDE.md. Every push gets a PR, and red CI is fixed right
  away.
- Before a merge, the PR gets an independent review (a different agent or model, given the diff but not the
  author's reasoning) with [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md).
- Cost controls (docs/AGENT_WORKFLOW.md §8) are binding: report only results, decisions or blockers; pause when
  nothing is on the critical path; long runs go to runner sessions as harness-tracked background tasks.
- Stay API compatible with upstream 1.0.324.
- No mesh files in the repository; generate test meshes procedurally.
- Licenses: implement from papers, or port only from MIT / Apache / BSD sources. Never use GPL code (CGAL, parts of
  TetWild). List sources in docs/SOURCES.md.

## Restricted areas

- **Exact arithmetic and `mesh_ops/ExactMesh*`** (`math/Exact*`, `math/AdaptivePredicates.cs`, implicit points,
  error bounds) are tier L. They always need a bounds review, and the mutation workflow must stay above its
  thresholds.
- **Clean-room work.** The 2D spatial index ([docs/design/QUADTREE2D_SPEC.md](docs/design/QUADTREE2D_SPEC.md)) and
  2D meshing ([docs/design/MESHING2D_SPEC.md](docs/design/MESHING2D_SPEC.md)) may only be implemented from the
  spec and published papers.
  - Never read, open or port Triangle / Triangle.NET code or the owner's existing quadtree.
  - An agent that has seen that code must not implement these items.
- **Company code** (C:\Projekte\Base) is never a source.
- **Mutation tooling** (`.github/workflows/mutation.yml`, `tests/mutation/`): name new test classes and files so they
  match a shard's existing `test-case-filter` and workflow `paths`, and do not touch the tooling. An edit is not free:
  a change to `mutation.yml` in a region that can change how a shard runs (the Stryker step, runner, setup, matrix,
  workflow `env`) runs every shard in full (4-5 h of runners; an edit of the trigger, `max-parallel`, a reporting step or
  a cache key runs nothing by itself, tests/mutation/README.md), and a change to a shard's `stryker-*.json` gives it a new cache
  key, so that shard runs in full. If a test cannot
  match, extending a filter or `paths` is allowed; say so and why in the PR. Other changes to the workflow or the
  scripts go through the coordinator, as every PR's mutation gate depends on them.
