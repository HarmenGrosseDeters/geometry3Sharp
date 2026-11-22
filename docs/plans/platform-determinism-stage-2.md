# Plan: determinism proof stage 2 (ExactMeshArrangement, ExactMeshBoolean)

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Platform/QA/CI: determinism proof stage 2"
- **Kind:** ready to start
- **Tier:** L (scope is `mesh_ops/ExactMesh*`: bounds / exact-code review, incremental mutation gate)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): the implementation is open PR `#1370` (owner decisions of 2026-11-22: stage 2 for the exact result only, with `MeshRoundingRepair` as a named assumption; [ROADMAP.md](../ROADMAP.md), "Decided on 2026-11-22"). Of the two blockers named below, #1220 is merged (2026-11-22) and #1223 is still an open PR. The plan closes when #1370 merges.

## Current state

- Method and owner decisions: [DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md) §§1-8 (owner 2026-11-13: "proof" =
  machine-checked reduction to listed assumptions T1-T5 and obligations; timings are outside the claim).
- Stage 0 (exact kernel, #871) and stage 1 (`ExactTriangulation2`, `ExactPlanarSimplify`, `SnapRounding2`,
  `TriangleBoxTree`, #1076) are merged; analyzers G3D0001-9 in `tools/analyzers/G3ExactCodeAnalyzers`, list
  `DeterministicApis.txt`, rule tests `DeterminismScopeAnalyzerTests`, harness `tools/DeterminismHarness` (Stage1.cs),
  workflow `determinism.yml` (green 2026-11-19).
- Stage 1 left six assumed stage 2 members: S2-1 `ComparePoints` total order, S2-2 `Orient` / S2-3 `InCircleFiltered`
  exact signs, S2-4 `InputTriangle.P` exact plane (lazy cache, same value on every thread), S2-5 input triangle lookups,
  S2-6 the box-tree callbacks of callers.
- Size today: `ExactMeshArrangement.cs` 3 830 lines (27 parallel-loop matches), `ExactMeshBoolean.cs` 5 450 (12); 25
  `ExactMesh` entries in `core/ExactCodeAllowList.cs`. Design estimate §6.1: ~8 200 lines, 17 allow-list entries
  (grown since).
- Unblocked: Exact Boolean 3.0 complete 2026-11-18, maintenance mode. **But** open PRs touch the same file:
  #1220 (Orientation = ByNesting), #1223 (sheet role, draft). Stage 2 must start after they merge or close.
  Update 2026-11-22: #1220 is merged; #1223 is still open (no longer a draft); stage 2 started anyway as PR #1370.

## Trigger / decision needed

None for the method (decided). Scheduling: started (PR #1370, 2026-11-22); what remains is its CI and the incremental
mutation run, then the merge (OPEN_TOPICS.md row "determinism proof stage 2"). Within the PR, the P5 obligations
(renaming invariance of ids handed out in schedule order) decide whether option G (canonical result order) or C
(deterministic ids by construction) is needed (§7); that choice goes to the owner with numbers.

## Goal

A generated, checked claim: on runtimes satisfying T1-T5, `ExactMeshArrangement` and `ExactMeshBoolean` compute
functions of their inputs (schedule, hash seed, container order, sort ties, process do not matter), with a numbered
ledger of the remaining obligations.

## Scope

- `.editorconfig` (scope section: add both files; drop the `type ... ; TriangleBoxTree` restriction),
  `DeterministicApis.txt`, `core/ExactCodeAllowList.cs` (restate entries as forms P1-P5 with tests),
  `tools/analyzers/G3ExactCodeAnalyzers/*` only if a new construct needs a rule, `tools/DeterminismHarness/Stage2.cs`
  (new), `tests/geometry3Sharp.Tests/Determinism*Tests.cs`, `docs/design/DETERMINISM_PROOF.md` (new §5.5, ledger).
- Library code changes only where a site cannot be argued (expected: the two `.Values` enumerations and the
  `merge_points` ties, §6.3), each output-identical.

## Non-goals

No change of results (bit for bit on BooleanComparison and the harness), no API change, no option D/E/F, no new
parallelism, no speed work.

## Interface

None public. Internal test hooks only (off by default), as `PermutationSeedForTests`.

## Look before you build

1. Library: stage 1 machinery (type scopes, `[form]`, `[assumed]`, construct registrations), `OrderAdversaryTests`,
   `ExactBooleanDeterminismTests`, the harness `compare`.
2. Prior art (§3 of the note): NonDex (perturb unspecified orders), DPJ (deterministic-by-default with audited
   escapes), deterministic parallel ids by prefix sums (Blelloch et al., "Internally deterministic parallel
   algorithms", PPoPP 2012).

## Steps (one PR, clean commits)

1. Turn the scope on; collect the analyzer report (external members, hash enumerations, sorts, casts, `#if`).
2. Review and list external members; register sites with forms and tests; discharge S2-1..S2-6 (tests:
   `ComparePoints` totality and sort-order invariance; `InputTriangle.P` race test with N threads).
3. P5: id adversary hook (seeded permutation of the arrangement's id allocator) run over the harness; if results
   change only by order, propose G; if in content, propose C for that phase. Owner decides before code.
4. Harness `stage2.*` cases (parallel = serial = second process; order adversary with 3 seeds).
5. Ledger in §5.5 generated from the list; O-items for what stays open.

## Acceptance

- Library builds with the scope on (G3D0001-9 clean); `dotnet test --filter "FullyQualifiedName~Determinism"`.
- Harness: `dotnet run --project tools/DeterminismHarness -c Release -- run stage2.*` equal across serial / parallel /
  second process / 3 adversary seeds; `determinism.yml` run (gh workflow run) green on Windows, net48, arm64.
- BooleanComparison: no outcome or bit change (`benchmarks/BooleanComparison`, before/after commands in the PR).
- Hand mutants per registered form (as in §5.4) fail a named test; incremental mutation on arrangement-1..4,
  boolean-1..7 above threshold.

## Sources and licences

Papers only (above); no code ports.

## Budget and stop rule

One L session; if the registrations exceed ~2x stage 1 (153 external members) or P5 needs C in more than one phase,
stop with a hand-over and an owner question.

## Risks and open questions

- File churn from boolean PRs (#1220, #1223, follow-ups of #1210) invalidates pinned bodies: coordinate a freeze window.
- Hook overhead in hot loops: measure in the Boolean benchmark; compile hooks only into tests if above noise.
