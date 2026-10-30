# Ledger of deviations from upstream

Status: planned, not a completed ledger. 573 paths and 947 commits are too much for one pass: deliver in phases, per subsystem, tier
M rather than S, with the granularity the owner decides. The deliverable will be
`docs/UPSTREAM.md`; it does not exist in this checkout yet. The existing `docs/UPSTREAM_CHANGES.md` is a migration
note for users of 1.0.324, not an exhaustive inventory of additions and provenance.

## Task brief

- **Goal:** provide an evidence-linked account of what this fork changes and adds, suitable for preparing a later
  technical conversation with upstream maintainer rms80.
- **Tier:** S, documentation only. No new production code, tests, dependencies or benchmarks.
- **Scope:** `docs/UPSTREAM.md`, cross-links from `UPSTREAM_CHANGES.md`, `SOURCES.md`, `ROADMAP.md` and README where
  useful; correct directly related inventory statements only when supported by evidence.
- **Non-goals:** contacting rms80, filing upstream issues/PRs, merging upstream, promising upstream acceptance,
  asserting exhaustive API compatibility from a passing build, or re-reviewing every algorithm's correctness.
- **Interface:** stable ledger IDs, explicit baselines/as-of commits and links to authoritative evidence. Keep the
  existing migration-note path and detailed behavior tables intact rather than creating two competing copies.
- **Sources:** pinned upstream/fork history and file inventories, current public API/project metadata, existing
  source attributions, migration notes, operation docs, QA and linked test/PR evidence. Do not read restricted
  source to fill a documentation row; identify deferred restricted areas using metadata and approved docs.
- **Budget:** one bounded inventory pass and one evidence/link reconciliation pass, one documentation PR. Any
  unresolved attribution, compatibility claim or missing history is labelled unknown with a follow-up, not guessed.
- **Report:** comparison SHAs and commands, classification coverage, unresolved items and diff/link checks. No
  numerical claim about performance, quality or test coverage without the original command and output.

## Baselines and scope of truth

1. Baseline, checked on 2026-10-30: there is no `nuget-1.0.324` tag, and NuGet shows 1.0.324 at 2019-03-07. `ece3364` is a 2026 README
   commit; `7982934`..`ece3364` differs only in README, so the anchor is sound for code but is not the release commit. Say so in the ledger.
2. Current upstream HEAD equals `ece3364` today. Record a second baseline for the latest upstream revision actually inspected, if different. Distinguish
   "different from 1.0.324" from "still different from current upstream"; the ledger must not conflate them.
   If current upstream is not inspected, say so rather than claiming its fixes are missing.
3. Pin this fork's committed implementation snapshot. Exclude local uncommitted changes and planned work. Keep
   a separate pending section for plans, clearly not counted as shipped additions. The ledger's own doc commit
   does not move the implementation baseline recursively.
4. Use file status/rename inventories and non-whitespace diffs to discover changes, then commit history and
   tests/docs to establish intent. Treat bulk formatting, file movement and merge commits separately. A commit
   title is a lead, not sufficient proof of the behavior recorded.

Planned inventory commands, after resolving placeholders (not executed as an audit during planning):

```powershell
git rev-parse <upstream-baseline> <fork-snapshot>
git --no-pager diff --name-status -M <upstream-baseline> <fork-snapshot>
git --no-pager diff -w --stat <upstream-baseline> <fork-snapshot>
git --no-pager log --no-merges --format="%H %s" <upstream-baseline>..<fork-snapshot>
```

Read substantive diffs only for provenance-approved paths in bounded groups. Do not replace the inventory with
counts of commits or lines: one behavior can span many files and many commits can implement one change.

## Ledger structure

Start `docs/UPSTREAM.md` with scope/date, exact repository URLs and full baseline SHAs, inclusion rules, and a
short map of categories. Add rows grouped by subsystem, with these required fields:

| Field | Meaning |
|---|---|
| ID / category | Stable ID; bug fix, intentional behavior/default change, new operation/API, infrastructure, docs-only or unresolved |
| Affected surface | Public class/member or module and current file link |
| Upstream behavior / availability | What the pinned baseline does or lacks; unknown if not established |
| Fork behavior / availability | Observable change, limitation or addition, without marketing claims |
| Compatibility | Source, binary and behavioral impact distinguished; no blanket compatible/incompatible label |
| Origin / license | Own implementation or adaptation/port; immutable external source and `SOURCES.md` link |
| Evidence | Implementing commit/PR, regression or acceptance test, and docs/benchmark command where relevant |
| State | Present in the pinned snapshot, superseded, reverted, pending, or unresolved |
| Upstream relevance | Candidate for discussion, coupled to a fork-only subsystem, or undecided; not a promise to submit |

Use subsystem-level rows for new modules, method-level rows for behavior changes. A grouped row must still
map back to every substantive file delta it covers. Keep a compact coverage map for formatting/build/docs-only
paths and explicit unclassified items so the word "ledger" does not conceal unexplained changes.

Required categories:

- Correctness/robustness fixes to inherited operations, including orientation, scale/UTM handling, non-finite
  input, reader validation, state reuse and determinism. Link detailed user-visible differences to
  `UPSTREAM_CHANGES.md` instead of duplicating its long tables.
- Deliberate changed defaults, return/status semantics and documented limitations; do not present an API or
  policy change as a bug fix merely because it was desirable for this fork.
- Added geometry capabilities, exact Boolean/repair infrastructure, intrinsic/geodesic methods, terrain tools,
  measurements and formats, but only those present at the pinned implementation snapshot.
- Build/framework/package metadata, CLI/MCP, CI, QA harnesses and benchmarks, separated from runtime API additions.
- External attributions and pending provenance. DAP changes remain pending until individually adopted; an audit
  plan or the DAP root license is not evidence that a feature has been incorporated or cleared.

## Reconciliation and acceptance

- Cross-check `UPSTREAM_CHANGES.md`, `OPERATIONS.md`, `SOURCES.md` and the roadmap against the committed inventory.
  In particular, the current migration notes both say the API is preserved and list signatures requiring a
  recompile. Record this as a claim to reconcile against actual public/protected API and existing package-validation
  evidence; do not repeat blanket compatibility or silently delete the contrary table.
- Every substantive delta has a row/group or an explicit unresolved disposition; every claimed addition has
  implementation evidence at the pinned SHA. Planned, uncommitted and external-only work is never called shipped.
- Every imported/adapted method has provenance or an explicit unresolved marker; unknown does not mean own work.
- Verify local paths and commit/PR links, and ensure cross-links keep migration notes and provenance authoritative.
  Separate historical benchmark reports from current measured results. Existing validation logs may be cited
  with their commands and scope; do not imply they were rerun for this documentation task.
- End with a short, factual shortlist for future upstream discussion (small separable fixes first), open questions
  and dependencies. Do not send any message or create an upstream PR without a new request.

The ledger can start independently of the DAP audit and terrain metrics. When either is merged later, append its
actual commit, tests and source attribution; do not block the initial inventory on unfinished implementation.
Documentation-only validation consists of scope, evidence and link checks, not new performance measurements.
