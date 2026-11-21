# Plan: determinism stage 1 obligations O2-O5

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Platform/QA/CI: determinism stage 1 obligations O2-O5"
- **Kind:** ready to start
- **Tier:** M (tests, analyzer rule); L review for any line in `mesh_ops/ExactMeshArrangement.cs` (`TriangleBoxTree`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

From [DETERMINISM_PROOF.md §5.4](../design/DETERMINISM_PROOF.md) "Open items" (O1 done, #1092):

| Item | What | Risk today |
|---|---|---|
| O2 | `TriangleBoxTree.ParallelBuildFrom`, `PermutationSeedForTests`, `PermutedTrees` are mutable `internal static`; no analyzer rule for mutable statics (G3D0007 of §6.2 is not built; the number is used by another rule) | a stray write changes speed and leaf order only (tests show results identical) |
| O3 | `SnapRounding2.Origin` is a public dictionary keyed by value tuples; an external consumer enumerating it is not covered | library consumer `ConstrainedDelaunayMesh2` only looks up |
| O4 | equivalent mutant: `merge_runs` tie-break `o[j] < o[i]` removable without a failing test | none (left run holds smaller indices); `fix_ties` attacked instead |
| O5 | the 12 harness cases `stage1.*` on Windows / net48 / arm64: first `determinism.yml` result not recorded in the note | evidence gap only |
| O6 | (not on the board row) `Equals` / `GetHashCode` inside generic containers invisible to the analyzer | reviewed by list diff |

## Trigger / decision needed

None. The board says "with stage 2"; this plan does them first as one small PR so stage 2 starts clean. O3 has an API
question (below) for the owner only if option b is wanted.

## Goal

O2-O5 closed: each item is either enforced by a rule / test, or recorded with its evidence in §5.4.

## Scope

- O2: `tools/analyzers/G3ExactCodeAnalyzers/DeterminismAnalyzers.cs` (new rule for writes to mutable statics in a scope
  outside members marked as test hooks, next free id G3D0010), rule tests in `DeterminismScopeAnalyzerTests`,
  `DeterministicApis.txt` registrations for the three knobs.
- O3: docs only (option a) or `comp_geom/SnapRounding2.cs` (option b).
- O4: `tests/geometry3Sharp.Tests/BoxTree*Tests.cs` (a direct test of `merge_runs` with crafted runs), or record it as
  equivalent in the Stryker ignore list of the shard (no: mutation tooling is coordinator-only) -> test or doc.
- O5: `docs/design/DETERMINISM_PROOF.md` §5.4 (results table with run id).

## Non-goals

No output change; no change to `mutation.yml` or `tests/mutation/`; O6 stays a review item.

## Interface

O3 option b: `SnapRounding2.Origin` keeps its type (API compatibility with 1.0.324 does not apply: added in this fork,
check `CompatibilitySuppressions.xml` / package validation); a documented sorted accessor `OriginsInOrder()` added.

## Look before you build

1. Library: G3E analyzers (pattern for scoped rules), `ExactCodeAllowList.cs`, `determinism.yml` artifacts.
2. Prior art: Roslyn analyzers for static mutable state (CA2211 "non-constant fields should not be visible"); NonDex for
   hash-order exposure (O3).

## Steps (one PR, clean commits)

1. O5: `gh run list --workflow determinism.yml`, download the artifact of the last green run, record the 12 cases per
   platform in §5.4 (run id, equal / not equal).
2. O2: rule + tests (positive, negative, hand mutant: a write from non-test code fails the build).
3. O4: test that calls the merge with equal keys across runs and checks the index order; check by removing the tie-break.
4. O3: option a (doc comment on `Origin`: "enumeration order is unspecified; sort by key for a stable order") unless
   the owner wants b.

## Acceptance

- `dotnet build` (all targets) green with the new rule; the rule's hand mutant fails the build.
- O4 test fails with `o[j] < o[i]` removed (shown in the PR).
- §5.4 lists O2-O5 as done with evidence; `DeterminismScopeAnalyzerTests` (checks the ledger) green.

## Sources and licences

None (own code).

## Budget and stop rule

One session, ~1 day. If the O2 rule fires on more than ~10 sites outside the three knobs, stop and list them for stage 2.

## Risks and open questions

- O4 needs `merge_runs` to be reachable from tests (`internal` + InternalsVisibleTo exists for the box tree tests).
- Touching `ExactMeshArrangement.cs` for O4 is not needed if the test calls the internal method; keep it file-free.
