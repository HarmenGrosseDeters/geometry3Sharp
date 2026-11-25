# Plan: .NET 11 migration (CI leg + optional target evaluation)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Platform/QA/CI: .NET 11 migration"
- **Kind:** parked ("later", owner 2026-11-12; trigger: owner schedules it once .NET 11 SDK + Stryker/Coverlet support are stable on GitHub-hosted runners)
- **Tier:** M (build/CI/tooling + determinism & exact-code JIT verification; touches gate files -> `gate-change` label)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Recorded in [platform.md](../roadmap/platform.md#open) lines 107-113:
  - Library currently targets `net10.0` (LTS) + `netstandard2.0`; tests target `net10.0` + `net48`.
  - .NET 11 is a standard-term release (STS). Owner (2026-11-12): "migrate later, not scheduled."
  - Proposed policy already outlined in `platform.md`:
    1. First add a **.NET 11 SDK test matrix leg** in CI (running existing `net10.0` / `net11.0` tests).
    2. Add a `net11.0` library target **only if** it brings measurable gains (new intrinsics or runtime improvements
       backed by a benchmark); always keep `net10.0` and `netstandard2.0` and upstream `1.0.324` API compatibility.
    3. Verify analyzers (`BannedSymbols.txt`, `G3E0001-3`, `G3D0001-9`), Stryker.NET, Coverlet/XPlat coverage, and
       **bit-identical exact-code & 2^k invariance results** on the .NET 11 JIT (FMA contraction must stay banned).

## Trigger / decision needed

Owner schedules the .NET 11 check (typically once GitHub Actions `setup-dotnet` supports .NET 11 GA and Stryker.NET +
Coverlet + Roslyn analyzers run cleanly on it).

## Goal

Verify full build, test, analyzer, and bit-identical exact-arithmetic determinism on .NET 11 in CI, and decide via A/B
benchmark whether adding a `net11.0` library TFM pays off.

## Scope

- `.github/workflows/ci.yml`, `.github/workflows/determinism.yml` (add .NET 11 leg / probe — **gate files**: needs
  `gate-change` label via coordinator/owner)
- `geometry3Sharp_netstandard.csproj`, `addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj` (only if
  `net11.0` TFM is approved after benchmark)
- `tools/DeterminismHarness/` (run T1-T5 probes on .NET 11, update `docs/design/DETERMINISM_PROOF.md`)
- `tests/geometry3Sharp.Tests/*.csproj`, `tools/analyzers/G3ExactCodeAnalyzers/` (check Roslyn version compatibility)
- `docs/QA.md`, `docs/roadmap/platform.md`, `docs/OPEN_TOPICS.md`

## Non-goals

Never drop `net10.0` (LTS), `netstandard2.0` (Unity/.NET Framework), or `net48` test coverage; never break 1.0.324 API
compatibility (`dotnet pack` validation); never allow JIT FMA contraction in exact code.

## Interface

No public API changes (optional extra TFM `net11.0` in NuGet pack layout only if benchmarked faster).

## Look before you build

1. Library: `.github/workflows/ci.yml`, `.github/workflows/determinism.yml`, `tools/DeterminismHarness/Probes.cs`,
   `tools/analyzers/G3ExactCodeAnalyzers/`, `tools/gate_files.txt`.
2. External docs: .NET 11 release notes (JIT/RyuJIT changes, FP/FMA codegen, `BigInteger`, `Vector256`/`Vector512`,
   Stryker.NET / Coverlet compatibility).

## Steps (one PR, clean commits)

1. **Tooling & determinism probe (local/CI):** install .NET 11 SDK, build all projects + analyzers, run full test suite
   and `DeterminismHarness` probes (T1-T5) comparing bit-for-bit traces against `net10.0`.
2. **A/B benchmark (if considering `net11.0` TFM):** run `BooleanComparison` (T6 subset) and `Quadtree` / `TetMesh`
   microbenchmarks on `net10.0` vs `net11.0` (order-balanced, median of 5); present delta to owner.
3. Add the `.NET 11` CI leg (and `net11.0` TFM only if owner approves), verify `dotnet pack` + `PackageSmokeTest` +
   `Clipper2PackageSmokeTest`, update `DETERMINISM_PROOF.md` probe table and `docs/QA.md`.

## Acceptance

- Full test suite (`geometry3Sharp.Tests`, `g3cli.Tests`, `geometry3Sharp.Clipper2.Tests`), analyzers (`G3E*`, `G3D*`),
  and `dotnet pack` + smoke tests green on .NET 11.
- `DeterminismHarness` traces on .NET 11 are **bit-identical** to `net10.0` across all stage 0/1/2 cases (T1-T5 probes
  pass, zero FMA drift).
- Benchmark before/after table + command in PR if adding `net11.0` TFM.
- `gate-change` label reviewed and applied before merge.

## Sources and licences

.NET 11 official release notes / SDK; own code.

## Budget and stop rule

1 M session. Stop if Stryker.NET or Roslyn analyzers fail to load on .NET 11 SDK (wait for upstream tooling release
rather than disabling any analyzer).

## Risks and open questions

- New JIT optimizations in .NET 11 (e.g. aggressive FMA fusion or vectorization) could silently break exact-predicate
  rounding guarantees: `DeterminismHarness` + `ExactArithmeticTests` + `GeneratedPredicatesTests` are mandatory gates.
