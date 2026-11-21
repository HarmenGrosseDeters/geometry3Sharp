# Plan: delete merged / superseded remote branches (owner action)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Platform/QA/CI: delete merged / superseded branches"
- **Kind:** owner decision / owner action (decided 2026-11-17; owner executes the deletion)
- **Tier:** S (branch hygiene + optional repo setting; no code change)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only. No branches deleted by this PR.

## Current state

- Decision recorded in [ROADMAP.md](../ROADMAP.md#open-decisions-owner) (owner 2026-11-17): the coordinator lists
  candidates (branches whose PRs are merged or closed, plus `claude/int256-spike`, `claude/collinear-shortcut`, and
  `claude/geogram-boolean-comparison-wip`), and the **owner deletes them**; `runs/*` data branches stay.
- Live count on 2026-11-21 13:39 (Berlin) via `gh api --paginate repos/HarmenGrosseDeters/geometry3Sharp/branches`:
  **149 remote branches total**:
  - `1` default branch (`master`) — **keep**
  - `65` data branches (`runs/*`) — **keep** (per owner rule 2026-11-17)
  - `9` plan draft-PR branches (`plan/*`, growing as plan PRs open) — **keep** while draft PRs are open
  - `9` active open-PR branches — **keep**
  - `65` **deletion candidates** (merged/closed PR branches, superseded `claude/*` / `agents/*` spikes, interim
    `bench/*` snapshots, plus two legacy upstream branches `UnityPackage_Net3p5` and `UnityPackage_Net4p5` that should
    be explicitly confirmed keep vs delete).

## Trigger / decision needed

Owner confirms the exact candidate policy and runs the delete script locally (or turns on GitHub auto-delete for future
merges):

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Delete merged/closed PR branches + named superseded spikes, keep `UnityPackage_*` and `bench/*` tags/branches (leaning)** | Cleans ~55+ dead branches immediately; preserves upstream Unity branches and benchmark snapshots | ~5 min owner run; deleted branches can be restored from GitHub PR pages or commit SHAs if ever needed |
| B. **Delete all 65 non-protected, non-`runs/*`, non-open-PR branches (except `UnityPackage_*`)** | Maximum cleanup (~63 deleted) | Check `bench/*` first to confirm their numbers are already in `docs/perf/` or `runs/*` |
| C. **Also enable GitHub "Automatically delete head branches" in repo settings** | Prevents future buildup after every PR merge | Zero manual maintenance going forward; does not touch `runs/*` (which have no PRs) |

How others solve it: GitHub provides the built-in repository setting "Automatically delete head branches" for merged
PRs; GitHub Actions `stale-branch` workflows or `gh` scripts prune closed-unmerged spike branches older than N days;
established OSS repos (e.g. dotnet/runtime, libigl, Manifold) keep long-lived release/data branches under a protected
prefix (`release/*`, `runs/*`) and auto-delete feature branches on merge.

## Goal

Clean up merged and superseded remote branches without losing any `runs/*` data, open PR branch, or unrecorded
benchmark snapshot, and close the §2 row.

## Scope

- No production code.
- Candidate-listing script (below, run locally by the owner — not committed unless desired under `tools/`).
- After deletion: `docs/OPEN_TOPICS.md`, `docs/ROADMAP.md`, `docs/roadmap/exact-boolean.md` (close item).

## Non-goals

Never delete `master`, `runs/*`, any branch with an open PR, or `plan/*` draft PRs. Sessions do not delete remote
branches.

## Interface

None.

## Look before you build

1. Library / repo: `gh api repos/HarmenGrosseDeters/geometry3Sharp/branches`, `gh pr list --state all`.
2. Prior art: GitHub "Automatically delete head branches" repo setting.

## Steps (owner executes)

1. Generate the categorized candidate list (read-only audit):
   ```bash
   python3 - <<'PY'
   import json, subprocess
   repo = "HarmenGrosseDeters/geometry3Sharp"
   branches = subprocess.check_output(["gh","api","--paginate",f"repos/{repo}/branches","--jq",".[].name"]).decode().splitlines()
   open_heads = set(subprocess.check_output(["gh","pr","list","-R",repo,"--state","open","--limit","300","--json","headRefName","-q",".[].headRefName"]).decode().splitlines())
   keep_prefixes = ("runs/", "plan/", "UnityPackage_")
   cands = [b for b in branches if b != "master" and b not in open_heads and not b.startswith(keep_prefixes)]
   print(f"Total candidates: {len(cands)}")
   for b in sorted(cands): print(b)
   PY
   ```
2. Review the printed list (especially `bench/*` and any `claude/*` without a PR) and remove any branch to keep.
3. Delete the reviewed list:
   ```bash
   while read -r b; do
     gh api -X DELETE "repos/HarmenGrosseDeters/geometry3Sharp/git/refs/heads/$b" && echo "deleted $b"
   done < candidates_to_delete.txt
   ```
4. Optionally enable "Automatically delete head branches" in GitHub repo Settings -> General -> Pull Requests.
5. Close the row in `docs/OPEN_TOPICS.md` and `docs/ROADMAP.md` (one docs commit).

## Acceptance

- Zero merged-PR branches left on `origin`; all `runs/*`, `plan/*`, `master`, and open-PR branches intact.
- Board row removed from `OPEN_TOPICS.md` §2 and `ROADMAP.md` "Open decisions".

## Sources and licences

None.

## Budget and stop rule

10 min owner action + 5 min docs closeout.

## Risks and open questions

- Confirm whether `bench/alpha-wrap-2-*` and `bench/thingi10k-full-interim-20261104` are still referenced by any
  benchmark README or script before deleting them (grep `docs/` and `benchmarks/` for `bench/` first).
