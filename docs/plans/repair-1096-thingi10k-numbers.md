# Plan: Thingi10K numbers for the #1096 cases (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Rounding/repair: Thingi10K numbers for the #1096 cases"
- **Kind:** owner decision
- **Tier:** S (runner measurement + docs only; no library code change)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Issue #1096 is closed (2026-11-18), answered on master by #1114 (`MeshRobustRepair.MeasureResultVolumeIoU` /
  `ResultVolumeIoU` + `ResultPath`, opt-in, default off, `Apply()` unchanged; `g3cli repair --measure-result-iou`).
- Procedural reproducers (thin open walls, open surfaces with holes far larger than alpha, single open sheets) explain
  why `ResultVolumeIoU` is low even though Hausdorff distance is small in both directions
  ([alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md#open-for-the-future-2026-11-15)).
- The three Thingi10K meshes cited in #1096 — **77958, 79136, 87345** — were not run with `ResultVolumeIoU` and
  `fm_oviou` (`OuterVolumeIoU`) because no mesh files live in the repository and PR tests have no network.
- Earlier numbers on those three meshes: extra volume 0.69 to 3.5x input, missing 0.1 to 0.69, result-to-input <= 0.44 %
  and input-to-result 2.5 to 6.3 % of diagonal.

## Trigger / decision needed

Owner chooses between:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Close without running (leaning)** | Procedural explanation (#1114) stands; the three Thingi10K ids remain historical notes | Zero runner time; slight chance one of the three real meshes has an extra mechanism beyond thin wall / large hole |
| B. **Run `BENCH_REPAIR=robust BENCH_FIDELITY=0` on the 3 ids on a runner** | Records exact `ResultVolumeIoU`, `fm_oviou`, `ResultPath` and Hausdorff distances for 77958, 79136, 87345 in `benchmarks/MeshRepairBenchmark/README.md` | ~10 min runner job (`runs/repair-1096`); if one mesh shows a genuine defect, opens a separate `bugfix` issue |

How others solve it: Thingi10K repair benchmarks (Hu et al. 2018 TetWild, Portaneri et al. 2022 Alpha Wrapping, Attene
2010 MeshFix, Jacobson et al. 2013 GWN) distinguish watertight-input volume error from open-soup surface Hausdorff error,
and report both metrics rather than gating open inputs on volume IoU; procedural minimal reproducers are standard when
corpus files cannot be redistributed.

## Goal

Settle whether the three Thingi10K meshes of #1096 need real-mesh `ResultVolumeIoU` / `OuterVolumeIoU` confirmation or
whether the procedural explanation closes the topic.

## Scope

- Option A: `docs/OPEN_TOPICS.md`, `docs/roadmap/alpha-wrap-and-repair.md` (close row).
- Option B: `benchmarks/MeshRepairBenchmark/Program.cs` (only if `MeasureResultVolumeIoU` is not yet wired into the
  benchmark CSV), `benchmarks/MeshRepairBenchmark/README.md`, raw CSV on branch `runs/repair-1096` (never merged),
  `docs/OPEN_TOPICS.md`, `docs/roadmap/alpha-wrap-and-repair.md`.

## Non-goals

No library code change; no mesh files committed; no volume-guard rejection of alpha-wrap results (rejected 2026-11-17).

## Interface

None.

## Look before you build

1. Library: `MeshRobustRepair.MeasureResultVolumeIoU`, `ResultVolumeIoU`, `ResultPath`, `MeshFidelityMetrics.OuterVolumeIoU`,
   `benchmarks/MeshRepairBenchmark/`.
2. Prior art: see "How others solve it" above.

## Steps (one PR, clean commits)

1. Owner picks A or B.
2. If A: remove the row from `docs/OPEN_TOPICS.md` and note the decision in `docs/roadmap/alpha-wrap-and-repair.md`.
3. If B: check `benchmarks/MeshRepairBenchmark` prints `ResultVolumeIoU` and `fm_oviou`; run on ids `77958,79136,87345`
   on a runner (`BENCH_REPAIR=robust BENCH_FIDELITY=0`); push raw output to `runs/repair-1096`; add the 3-row table
   with command to `benchmarks/MeshRepairBenchmark/README.md` and close the board row.

## Acceptance

- Option A: docs-only diff reviewed.
- Option B: command + raw 3-row table (`id`, `ResultPath`, `ResultVolumeIoU`, `fm_oviou`, two-sided Hausdorff, time)
  recorded in `benchmarks/MeshRepairBenchmark/README.md`; if any mesh contradicts the thin-wall / large-hole explanation,
  a new issue is filed with a procedural reproducer.

## Sources and licences

Thingi10K meshes fetched at runner time only, never committed (license rule in CLAUDE.md).

## Budget and stop rule

Option A: 5 min. Option B: one short runner job (< 15 min) + one docs commit; stop immediately after recording the table.

## Risks and open questions

- None; if one of the 3 meshes exhibits an unexpected mismatch, it becomes a separate bugfix brief.
