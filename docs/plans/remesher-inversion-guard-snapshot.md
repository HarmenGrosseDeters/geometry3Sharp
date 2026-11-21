# Plan: `Remesher` `RejectInvertingPasses` cheaper snapshot (owner options)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Other: Remesher RejectInvertingPasses cheaper snapshot"
- **Kind:** parked (owner options parked 2026-11-17; trigger: owner chooses option (f)+(c) or (f)+(a), or passes get faster so the guard share grows)
- **Tier:** M (touches `Remesher` / `RemesherPro` and for (a) `DMesh3` core edge ops; must stay bit-identical in rollback decisions)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Measured and closed as study #1070 ([INVERSION_GUARD_SNAPSHOT.md](../design/INVERSION_GUARD_SNAPSHOT.md), hard
  problem 14 closed 2026-11-17):
  - Snapshot copy alone is at most 8.6 % of a run (10.4 % including GC pauses in 1 of 9 configs, <= 9.5 % in the other
    8), so snapshot-only replacements fail the 10 % bar.
  - Incremental volume tracking (e) is neither decision-identical near tolerance nor cheaper (passes move almost every
    vertex).
  - **Two combined options remain parked for an owner pick** — combining **(f) maintained component labels across edge
    operations** (removing the union-find in sign-count) with either **(a) an undo log inside `DMesh3` edge operations**
    or **(c) reused snapshot buffers**:
    - Combined ceiling (copy + union-find + extra GC pause): > 10 % in 4 of 9 configs (`RemesherPro` `pro_fastest` on
      `sphere_refine` / `bumpy_uniform`: 12.3-15.2 %; `pro_iter` on `bumpy_uniform`: 12.1-13.0 %; `Remesher` on
      `noisy_coarsen`: 9.9-10.6 %; <= 9.5 % in the other 5). Actual net saving is lower after subtracting maintenance /
      logging / memcpy costs.

## Trigger / decision needed

Owner picks between keeping the guard as-is or prototyping one of the two parked options:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Keep as-is (leaning)** | Zero risk to `DMesh3`; guard overhead stays 6-15 % only when `RejectInvertingPasses` is on | Snapshot + union-find cost remains on inverted-pass-guarded runs |
| B. **(f) + (c): maintained component labels + reused snapshot buffers** | Avoids union-find and snapshot allocation/GC without touching `DMesh3` core ops; decision bit-identical | Medium M PR; needs per-edge-op component-label invariant proof (collapse/split/flip) |
| C. **(f) + (a): maintained component labels + undo log inside `DMesh3`** | Avoids full mesh byte-copy too (keeps only vertex buffer ~15 % + undo records) | Invasive: adds hot-path logging hooks inside shared core `DMesh3` (risks regressing unguarded `DMesh3` callers) |

How others solve it: Persistent data structures (Driscoll, Sarnak, Sleator, Tarjan 1989) move snapshot cost onto
writes (option C); undo/redo transaction logs in mesh kernels (OpenMesh / PMP / Blender BMesh undo) log topological
edits per atomic op; double-buffered / pooled snapshots (option B) eliminate GC pauses while keeping mesh edits free of
logging branches; divergence-theorem volume tracking is used when approximate rollback decisions are acceptable.

## Goal

If triggered by owner pick B or C, verify the net gain of (f) + (c) (or (f) + (a)) with a micro-probe first, then ship
a bit-identical faster inversion guard with zero regression on unguarded `Remesher` / `DMesh3` runs.

## Scope

- `mesh_ops/Remesher.cs`, `mesh_ops/RemesherPro.cs` (`begin_inversion_guard`, `end_inversion_guard`, component labels)
- Option C only: `mesh/DMesh3_edits.cs` (guarded behind an optional undo sink, null when unused)
- `tests/geometry3Sharp.Tests/Remesher*Tests.cs` (bit-identical rollback decisions on closed, open, multi-component,
  inverting, and non-inverting meshes)
- `benchmarks/RemeshBenchmark/` (the 9 configurations of `INVERSION_GUARD_SNAPSHOT.md`)

## Non-goals

Never change rollback decisions (bit-identical output vs current master unless owner explicitly relaxes tolerance for
option (e)); never slow down unguarded `Remesher` or `DMesh3` callers.

## Interface

None public (internal to `Remesher` / `RemesherPro`).

## Look before you build

1. Library: `docs/design/INVERSION_GUARD_SNAPSHOT.md`, `mesh_ops/Remesher.cs`, `mesh_ops/RemesherPro.cs`,
   `mesh/DMesh3_edits.cs`, `benchmarks/RemeshBenchmark/`.
2. Literature: Driscoll et al. (JCSS 1989, persistent data structures).

## Steps (if B or C is chosen; one PR, clean commits)

1. Measure net gain of (f) alone (union-find share vs label-maintenance cost on the 9 benchmark configs) — stop here if
   net < 5 %.
2. Measure net gain of (c) (reused `DMesh3` backing buffers) before considering (a); avoid touching `DMesh3` hot paths
   if (f) + (c) captures most of the ceiling.
3. Add stress tests verifying maintained component labels equal fresh union-find after every split/collapse/flip pass
   (including component merge/disconnect edge cases), plus invariance, determinism, reuse.

## Acceptance

- Bit-identical outputs and identical rollback counts across all 9 benchmark configs and existing `Remesher*Tests`.
- Order-balanced median-of-5 A/B on `benchmarks/RemeshBenchmark`: >= 10 % gain on the 4 target configs, never slower
  (<= 0 % regression) on unguarded runs or the other 5 configs.

## Sources and licences

Driscoll et al. (1989) paper; own code.

## Budget and stop rule

0.5-day probe for (f) net gain first. Stop immediately if (f) + (c) net speedup is below 8 % on the 4 `RemesherPro`
configs (not worth the component-label maintenance complexity).

## Risks and open questions

- Edge collapses / splits can split a single connected component into two (pinch disconnect) or merge components —
  component label maintenance must handle splits conservatively (re-label affected component only, or fall back to
  union-find on a pinch).
