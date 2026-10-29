# Instructions for coding agents

These rules apply to every agent working in this repository (Claude Code, GitHub Copilot, Codex and others).
The full rules are in [CLAUDE.md](CLAUDE.md), and how we work in [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md).
Read both before you change anything. If anything below conflicts with them, they take precedence.

## Essentials

- Answer the user in German. Code, comments, commit messages, PRs and docs stay in English.
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
- **Mutation tooling** (`.github/workflows/mutation.yml`, `tests/mutation/`) is being reworked; do not change it
  unless that is your task.
