# Tools & CI Utilities

This directory contains the user-facing CLI/MCP server, build-time Roslyn analyzers, code generators, fuzzing/verification harnesses, and Python CI gate scripts.

---

## Applications, Analyzers & Harnesses

| Directory | Purpose | Details |
|---|---|---|
| [g3cli/](g3cli/README.md) | Headless CLI and MCP server (`g3cli mcp`) exposing mesh, 2D, terrain, and analysis operations with JSON output | [g3cli/README.md](g3cli/README.md) |
| `analyzers/G3ExactCodeAnalyzers/` | Build-time Roslyn analyzers enforcing exact-arithmetic and determinism rules (`G3E0001–3`, `G3D0001–9`) | [CLAUDE.md](../CLAUDE.md), [DETERMINISM_PROOF.md](../docs/design/DETERMINISM_PROOF.md) |
| [PredicateCompiler/](PredicateCompiler/README.md) | Code generator and error-bound derivation tool for fixed-width, indirect, and double fallback predicates | [PredicateCompiler/README.md](PredicateCompiler/README.md) |
| [DeterminismHarness/](DeterminismHarness/README.md) | Cross-runtime (`net48` vs `net10.0`) bit-for-bit determinism harness and probes | [DeterminismHarness/README.md](DeterminismHarness/README.md) |
| [MeshFuzzPilot/](MeshFuzzPilot/README.md) | Coverage-guided fuzzing harness (SharpFuzz + libFuzzer) for mesh operations and findings log | [MeshFuzzPilot/README.md](MeshFuzzPilot/README.md) |
| [ReaderFuzzPilot/](ReaderFuzzPilot/README.md) | Coverage-guided fuzzing harness for all mesh and geometry file readers | [ReaderFuzzPilot/README.md](ReaderFuzzPilot/README.md) |
| [Z3Pilot/](Z3Pilot/README.md) | SMT verification pilot scripts using Z3 for predicate, rounding-drift, and separating-axis proofs | [Z3Pilot/README.md](Z3Pilot/README.md) |
| [FPTaylorPilot/](FPTaylorPilot/README.md) | Floating-point error-bound verification pilot using FPTaylor | [FPTaylorPilot/README.md](FPTaylorPilot/README.md) |
| [coverage/](coverage/README.md) | Changed-line coverage gate configuration, validation data, and unmeasurable-line allowlist | [coverage/README.md](coverage/README.md) |
| `NativeAotSpike/` | NativeAOT C-ABI interop feasibility prototype (`G3Native`, C driver, `run_spike.sh`) | [NATIVE_AOT_FEASIBILITY.md](../docs/design/NATIVE_AOT_FEASIBILITY.md) |
| `runtime-watch/` | Input case definitions (`cases.json`) for the nightly extreme-runtime watch job | [QA.md](../docs/QA.md#33-runtime-watch-extreme-cases-nightly) |

---

## CI & QA Scripts (`tools/*.py`)

Each script has matching unit tests (`tools/test_*.py`) run in CI.

| Script | Purpose |
|---|---|
| `ci_scope.py` | Determines from changed files whether a PR must run library suites, `g3cli`, or Windows benchmark checks |
| `gate_check.py` / `gate_files.txt` | Flags PRs modifying gate/rule files so they require an explicit `gate-change` label |
| `review_triggers.py` | Matches a git diff against fault-class triggers in [docs/REVIEW_CHECKLIST.md](../docs/REVIEW_CHECKLIST.md) |
| `new_tests_on_base.py` | Runs newly added test methods against the base branch library to verify regression/bugfix coverage |
| `trx_durations.py` | Inspects `.trx` test logs to enforce the 5 s / 10 s test-time rules and `SLOW_BACKLOG.txt` |
| `changed_line_coverage.py` | Checks coverlet reports for changed executable C# lines in the nightly coverage workflow |
| `blame_summary.py` | Summarizes in-flight tests when a blamed `dotnet test` run hangs or crashes |
| `runtime_watch.py` | Times known extreme boolean/wrap cases per `master` commit to detect runtime regressions |
| `schedule_guard.py` | Prevents duplicate/stale daily runs across scheduled workflows (`nightly`, `coverage`, `mutation`) |
| `certificate_hand_mutants.py` | Applies hand-crafted mutants to validate the certifying checker of `ExactMeshBoolean` |
