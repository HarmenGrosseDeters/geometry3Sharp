# Documentation Hub

Welcome to the documentation for **geometry3Sharp** (`HarmenGrosseDeters/geometry3Sharp`). Use the sections below to jump straight to the right page for your task.

---

## For Library Users

| Document | Purpose |
|---|---|
| [../README.md](../README.md) | Overview, quickstart, build targets, file formats, and feature summary |
| [OPERATIONS.md](OPERATIONS.md) | Task-to-class guide, repair/boolean contracts, validation checklist, and worked examples |
| [examples/](examples/) | Compiled, test-verified C# examples (`DocExamplesTests.cs`) for the main operations |
| [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md) | Behavioral changes and bug fixes compared to upstream `geometry3Sharp 1.0.324` |
| [guides/upstream-history-and-tutorials.md](guides/upstream-history-and-tutorials.md) | Original Gradientspace tutorials, Unity interop (`G3_USING_UNITY`), and upstream credits |
| [../addons/geometry3Sharp.Clipper2/README.md](../addons/geometry3Sharp.Clipper2/README.md) | Optional `geometry3Sharp.Clipper2` package for robust 2D polygon booleans, offsets, and Minkowski sums |

## For CLI & MCP Users

| Document | Purpose |
|---|---|
| [../tools/g3cli/README.md](../tools/g3cli/README.md) | Headless CLI (`g3cli`) with JSON output and MCP server (`g3cli mcp`) |

## For Contributors & Coding Agents

| Document | Purpose |
|---|---|
| [../CLAUDE.md](../CLAUDE.md) | Binding project rules, local CI commands, test time limits, and review/merge gates |
| [../AGENTS.md](../AGENTS.md) | Compact summary of rules and restricted / clean-room areas for all coding agents |
| [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | Tiered workflow (S/M/L), task briefs, independent review, and cost controls |
| [REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md) | Fault-class review checklist and diff triggers (`tools/review_triggers.py`) |
| [QA.md](QA.md) | Quality assurance strategy, test tiers, mutation testing, coverage, fuzzing, and local SDK setup |
| [ROBUSTNESS_SWEEP.md](ROBUSTNESS_SWEEP.md) | Upstream robustness sweep findings (linked to `RobustnessSweepTests`) |
| [SOURCES.md](SOURCES.md) | Academic papers, algorithms, and permissive third-party sources (Boost/MIT/BSD/Apache only) |
| [../tools/README.md](../tools/README.md) | Index of analyzers, predicate compiler, fuzzers, verification pilots, and CI scripts |
| [../benchmarks/README.md](../benchmarks/README.md) | Index of all performance, quality, and acceptance benchmarks |

## Architecture, Performance & Research

| Directory / Document | Purpose |
|---|---|
| [design/](design/) | Detailed algorithm specifications, error-bound derivations, and system design notes (109 documents) |
| [perf/README.md](perf/README.md) | Measured performance analyses, profiling reports, and scaling studies |
| [research/README.md](research/README.md) | Prior-art surveys, comparative studies (e.g. Geogram, plane-based kernels), and research reports |
| [ISSUE_CASE_FINDINGS.md](ISSUE_CASE_FINDINGS.md) | Hard cases reproduced from external geometry-library issue trackers |
| [Analyse-2026-10.md](Analyse-2026-10.md) | Historical October 2026 upstream bug audit (German; scheduled for English archive migration) |
| [PREDICATES.md](PREDICATES.md) | Historical October 2026 exact-predicate inventory (German; scheduled for English archive migration) |

## Planning & Status

| Document | Purpose |
|---|---|
| [OPEN_TOPICS.md](OPEN_TOPICS.md) | Single-board overview of all currently open items, owner decisions, and documented limits |
| [ROADMAP.md](ROADMAP.md) | Project direction, milestones, active work index, and session coordination rules |
| [roadmap/](roadmap/) | Detailed domain roadmaps (`exact-boolean`, `alpha-wrap-and-repair`, `meshing2d`, `terrain`, `parameterization-geodesics`, `platform`, `research`, `sources-surveys`, `hotspots`, `history`) |
