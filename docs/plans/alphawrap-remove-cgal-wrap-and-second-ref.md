# Plan: remove `external/cgal_wrap.cpp` and evaluate a second alpha-wrap reference

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Alpha wrap: remove external/cgal_wrap.cpp, then a second reference (alecjacobson/alpha-wrap)"
- **Kind:** parked (kept while comparisons use CGAL; second reference "not now", owner 2026-11-17)
- **Tier:** S (PR 1: remove `cgal_wrap.cpp` and its benchmark hook; PR 2: M benchmark-only harness for `alecjacobson/alpha-wrap`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp` invokes `CGAL::alpha_wrap_3` via CGAL's documented public API
  only (GPL; never compiled/distributed, never read CGAL source; used solely for black-box comparisons via `CGAL_WRAP`
  in `benchmarks/MeshRepairBenchmark/Program.cs:694-698` and `benchmarks/MeshRepairBenchmark/README.md:25,582-587`).
- Alpha Wrap 3.0 is complete (owner 2026-11-17); once no active comparison needs CGAL's alpha wrap, removing
  `external/cgal_wrap.cpp` eliminates the last GPL-linking helper from the repository
  ([alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md#other-open-items)).
- Second reference candidate: `alecjacobson/alpha-wrap` (permissive license — verify LICENSE file before any use;
  [sources-surveys.md](../roadmap/sources-surveys.md#open-for-the-future-2026-11-17) item 8: "not scheduled, owner: not now").

## Trigger / decision needed

- **PR 1 (remove `cgal_wrap.cpp`):** owner confirms no further CGAL alpha-wrap comparisons are planned (e.g., after any
  final raw-size-budget 800-mesh check).
- **PR 2 (second reference):** owner wants an independent permissive black-box alpha-wrap reference for future quality
  or speed comparisons.

## Goal

(PR 1) Delete `benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp` and its `cgalwrap` hook while keeping archived
historical results intact; (PR 2, optional) wire a permissive second black-box reference into `MeshRepairBenchmark`.

## Scope

- **PR 1:** `benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp` (delete), `benchmarks/MeshRepairBenchmark/Program.cs`
  (remove `cgalwrap` runner branch), `benchmarks/MeshRepairBenchmark/run_all.sh`, `benchmarks/MeshRepairBenchmark/README.md`
  (mark historical CGAL tables as archived), `docs/roadmap/alpha-wrap-and-repair.md`, `docs/OPEN_TOPICS.md`.
- **PR 2 (only if triggered):** new external wrapper script/harness under `benchmarks/MeshRepairBenchmark/`, license
  entry in `docs/sources/meshing-and-repair.md`.

## Non-goals

No library code changes; never read or port CGAL source; never commit third-party source code or meshes.

## Interface

Benchmark CLI only (`cgalwrap` mode removed in PR 1).

## Look before you build

1. Library / repo: `benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp`, `Program.cs:694`, `README.md:582`,
   `docs/roadmap/sources-surveys.md`.
2. External reference: `alecjacobson/alpha-wrap` (verify its LICENSE file and bundled dependencies before reading or
   running — per `sources-surveys.md` rules).

## Steps (one PR for removal, separate PR if second ref is ever wanted)

1. Verify with `grep -rn cgal_wrap` that no workflow (`.github/workflows/`) or active script references `cgal_wrap.cpp`.
2. Delete `external/cgal_wrap.cpp`, remove the `cgalwrap` path in `Program.cs` / `run_all.sh`, annotate historical
   tables in `README.md` as archived, and update `OPEN_TOPICS.md` / `alpha-wrap-and-repair.md`.
3. Build `benchmarks/MeshRepairBenchmark/MeshRepairBenchmark.csproj -c Release` to verify zero compile errors.

## Acceptance

- PR 1: `dotnet build benchmarks/MeshRepairBenchmark/MeshRepairBenchmark.csproj -c Release` succeeds; `grep -rn cgal_wrap`
  finds only archived historical table notes.
- PR 2 (if ever triggered): LICENSE verified permissive and recorded in `docs/SOURCES.md`; reproducible A/B table on
  the 20 T1 + 6 CAD procedural meshes in `benchmarks/MeshRepairBenchmark/README.md`.

## Sources and licences

Confirm `alecjacobson/alpha-wrap` LICENSE is MIT/BSD/Apache (and check third-party submodules) before any PR 2 work.

## Budget and stop rule

PR 1: 15 min (Tier S). PR 2: 1 short session only if requested by the owner.

## Risks and open questions

- Confirm whether the §2 raw-size budget decision (`alphawrap-raw-size-budget-default`) needs a final CGAL comparison
  before deleting `cgal_wrap.cpp`.
