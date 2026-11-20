# Quality Assurance and Test Strategy

The QA documentation is organized into modular topic pages under [docs/qa/](qa/README.md) to avoid merge conflicts and keep related policies together.

## Topic Index

| Page | Contents |
|---|---|
| [qa/README.md](qa/README.md) | Current QA baseline, fault-class history, and test tiers (Fast / Slow / Nightly) |
| [qa/testing-and-invariance.md](qa/testing-and-invariance.md) | Invariance, reuse, determinism, result validation, certifying boolean checker, supported input range, verifiable agent checks |
| [qa/mutation-and-coverage.md](qa/mutation-and-coverage.md) | Stryker mutation testing shards and nightly changed-line coverage gate |
| [qa/fuzzing-and-corpora.md](qa/fuzzing-and-corpora.md) | Fuzzing, regression corpora, Thingi10K/CAD datasets, failure minimization, reference parts, robustness sweep, external issue cases |
| [qa/ci-and-gates.md](qa/ci-and-gates.md) | API compatibility, gate-change check, scheduled runs & dispatch guard, Windows `net48` hang diagnosis, micro-benchmarks, runtime watch |
| [qa/local-setup.md](qa/local-setup.md) | Local .NET SDK setup and verification commands |

---

## Section Directory (Anchor Compatibility)

The short pointers below preserve existing section anchors referenced by `CLAUDE.md`, `docs/AGENT_WORKFLOW.md`, `docs/roadmap/`, and `tools/`.

### 1. Where we stand

See [qa/README.md](qa/README.md#1-where-we-stand).

### 2.5a Supported input range: correct inside, fail gracefully outside (owner 2026-11-09)

See [qa/testing-and-invariance.md](qa/testing-and-invariance.md#25a-supported-input-range-correct-inside-fail-gracefully-outside-owner-2026-11-09).

### 2.6 Coverage in CI

See [qa/mutation-and-coverage.md](qa/mutation-and-coverage.md#26-coverage-in-ci).

### 2.12 Scheduled runs: dispatch path and guard (2026-11-13)

See [qa/ci-and-gates.md](qa/ci-and-gates.md#212-scheduled-runs-dispatch-path-and-guard-2026-11-13).

### 3.3 Runtime watch (extreme cases, nightly)

See [qa/ci-and-gates.md](qa/ci-and-gates.md#33-runtime-watch-extreme-cases-nightly).

### 4a. Making agent work verifiable

See [qa/testing-and-invariance.md](qa/testing-and-invariance.md#4a-making-agent-work-verifiable).

### 5. Local setup

See [qa/local-setup.md](qa/local-setup.md#5-local-setup).
