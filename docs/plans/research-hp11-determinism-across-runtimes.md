# Plan: bit-for-bit determinism across runtimes and CPUs (HP 11 — umbrella research plan)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Platform/QA/CI: determinism across runtimes and CPUs (HP 11)"
- **Kind:** research study / multi-stage program (stages 0-1 merged; next: stage 2 + #929 local net48/net10 run + optional stage 3 floating-point operations)
- **Tier:** L (exact-code closed-world proofs, cross-runtime JIT/ABI/CPU verification, `DeterminismHarness`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Hard problem 11 ([DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md),
  [research.md HP 11](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model)):
  - **Completed:**
    - Proof method & owner definition (2026-11-13: "proof" = machine-checked reduction to explicit assumptions T1-T5 and
      numbered obligations; timings/counters outside the claim).
    - Probes & schedule adversary (#786, #810), `CoreMath` correctly rounded transcendentals (#617, #836, #1060, #1066),
      cast audit #804 (#973), seeded `ExactPoint3` hash fix (#789).
    - **Stage 0** (exact kernel closed world, #871) and **Stage 1** (`ExactTriangulation2`, `ExactPlanarSimplify`,
      `SnapRounding2`, `TriangleBoxTree`, #1076) merged.
  - **In flight / queued in §3 & §7 (linked separately):**
    - Stage 1 obligations O2-O5 (`platform-determinism-stage-1-obligations`, PR #1240).
    - **Stage 2** (`ExactMeshArrangement`, `ExactMeshBoolean`, `platform-determinism-stage-2`, PR #1241).
    - **#929 (owner's local run):** `net48` vs `net10` runtime comparison (and re-measurement of net10-only optimizations
      on net48).
  - **Open research beyond Stage 2 (`DETERMINISM_PROOF.md` §6.1 & §6.4):**
    - **Stage 3 (floating-point operations without P4 exactness):** e.g. `MarchingCubes`, `Reducer`, `Remesher`,
      `TetMeshImprover`, linear solvers (`SparseCholesky`) — proving internal determinism (L0) and cross-platform
      bit-identity (L1, libm-free) where floating-point rounding order matters.
    - **Cross-architecture evidence:** `win-x64` (`net10`, `net48`, `net48` 32-bit), `linux-x64`, `linux-arm64`,
      `osx-arm64` (and future .NET 11 JIT) under T1-T5 probes.

## Trigger / decision needed

1. Complete Stage 1 obligations (PR #1240), Stage 2 (PR #1241), and owner's local #929 measurement first.
2. Owner decides whether to extend the closed-world determinism claim to **Stage 3** (floating-point operations such as
   `MarchingCubes` / `Reducer` / `QuadRemesher`) or stop after Stage 2 (exact boolean pipeline fully covered).

## Goal

Provide the end-to-end roadmap from Stage 2 + #929 to an optional Stage 3 floating-point determinism proof across
`net48` / `net10` and x64 / ARM64, making explicit what is statically proven vs empirically probed.

## Scope

- Study / architecture update in `docs/design/DETERMINISM_PROOF.md` (recording #929 findings + Stage 3 feasibility).
- `tools/DeterminismHarness/` (new cross-runtime/cross-CPU probe cases for Stage 3 candidates: `MarchingCubes`,
  `Reducer`, `SparseCholesky`, `TetMeshImprover`).
- `.github/workflows/determinism.yml` (cross-platform verification runs; macOS arm64 opt-in).

## Non-goals

Do not pursue formal compiler verification (Option F, CompCert-style — decided no-go 2026-11-13) or IL schedule
exploration (Option E, no-go); do not replace `System.Math` globally with `CoreMath` (owner 2026-11-11 libm rule: only
where cross-platform bit-identity is promised and a harness diff is measured).

## Interface

None public.

## Look before you build

1. Library: `docs/design/DETERMINISM_PROOF.md` §§1-8, `tools/DeterminismHarness/`, `tools/analyzers/G3ExactCodeAnalyzers/`,
   `math/CoreMath.cs`, issue #929.
2. Literature: `DETERMINISM_PROOF.md` §3 & §9 (NonDex, DPJ, CORE-MATH, IEEE 754-2019 reproducibility, Blelloch et al.
   2012 internal determinism).

## Steps (after Stage 2 & #929)

1. Incorporate #929 (`net48` vs `net10`) and full `determinism.yml` (`linux-x64`, `win-x64`, `linux-arm64`, `osx-arm64`)
   results into `DETERMINISM_PROOF.md` — catalog any remaining cross-runtime/CPU diffs (e.g. `SparseCholesky` FMA or
   `libm` calls in non-exact operations).
2. Run a Stage 3 feasibility probe on `MarchingCubes` and `Reducer`: measure how many external APIs, parallel reductions,
   and `libm` calls would need registration in `DeterministicApis.txt`.
3. Present Stage 3 cost/benefit to the owner (go/no-go decision before adding Stage 3 files to `.editorconfig` scope).

## Acceptance

- `DETERMINISM_PROOF.md` updated with complete cross-platform probe matrix (`net48` 32/64, `net10` x64/arm64, macOS)
  and explicit Stage 3 go/no-go table.
- Zero unexplained bit-differences on any operation claiming L0/L1 determinism.

## Sources and licences

CORE-MATH / CoreMathSharp (MIT, already in `docs/SOURCES.md`); published papers only.

## Budget and stop rule

Stage 3 feasibility probe: 1 short session after Stage 2 + #929 finish. Stop if `MarchingCubes` / `Reducer` require
replacing hot-loop floating-point reductions or disabling SIMD/FMA with > 15 % slowdown for no concrete certification
caller.

## Risks and open questions

- On ARM64 vs x64, JIT contraction of `a * b + c` to FMA outside the exact-code analyzer scope (`G3E0001`) can produce
  1-ulp differences in floating-point operations: Stage 3 must check whether `BannedSymbols` / FMA rules must expand too.
