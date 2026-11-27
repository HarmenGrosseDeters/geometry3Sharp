# Plan: `MeshRobustRepair` F2 — prevent silent loss of small separable parts

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Rounding/repair: MeshRobustRepair F2"
- **Kind:** parked (not now, owner 2026-11-17; trigger: owner go when reporting lost parts via F1 is not enough and callers want them preserved)
- **Tier:** M (touches `MeshRobustRepair` + `MeshRoundingRepair`: careful regression check on the 800-mesh repair corpus)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Scale-free repair study ([ROBUST_REPAIR_SCALE_FREE_STUDY.md](../design/ROBUST_REPAIR_SCALE_FREE_STUDY.md), #1095):
  - Full scale-free rewrite (HP 9) is a **no-go** (on 800 Thingi10K meshes, 252 strict misses; scale oracles fix at most
    5 of them).
  - **F1 (report lost parts) is built (#1101, owner 2026-11-17):** `MeshRobustRepairLostParts` runs at the end of
    `Apply()` for multi-part inputs (>= 2 padded parts, >= 4 triangles) and reports `LostFarPart`, `LostFarOpenPart`,
    `LostSeparatePart` so the 29 silent losses no longer return `Apply() == true` silently.
  - **F2 (prevent the loss) was parked (owner 2026-11-17: "F2 not now"):** per-part references in rows 7, 12 (tiny test
    and drift bound) and 13, plus far-part mode for open inputs.
  - Known risks from the study (§5 & review): (1) F2 still ends in `Apply() == false` with `ScaleDisparity` while
    `NotCleanReason` remains scene-relative (the "D" cells); (2) `MeshRoundingRepair.cs:899-912` documents an earlier
    regression when the area reference shrank; (3) tighter per-part bounds reject more rounding edits and cause more
    solidify / alpha-wrap fallbacks.

## Trigger / decision needed

Owner go specifically requesting that small separable parts in multi-scale scenes be **kept** (not just flagged via F1),
and owner decision on whether `NotCleanReason` / criterion D (per-component degeneracy) should also become opt-in.

## Goal

Preserve small separable components (>= 4 triangles) in multi-scale inputs during `MeshRobustRepair` without regressing
clean rate or inflating fallback counts on single-scale meshes.

## Scope

- `mesh_ops/MeshRobustRepair.cs` (per-part references in rows 7, 12, 13; far-part mode extended to open parts)
- `mesh_ops/MeshRoundingRepair.cs` (per-part tiny-component threshold and drift bound; carefully guard lines 899-912)
- `tests/geometry3Sharp.Tests/RobustRepair*Tests.cs` (procedural multi-scale assemblies covering the 29 lost-part shapes
  from study §3.1, incl. open parts and `sw_v0_r16_f8`)
- `benchmarks/MeshRepairBenchmark/` (800-mesh A/B: strict criterion, part criterion, fallback counts, time)

## Non-goals

No full local-tolerance rewrite (option C of the study, no-go); no silent weakening of `NotCleanReason` without an
explicit owner-approved opt-in (criterion D).

## Interface

Either default inside `MeshRobustRepair` for multi-part inputs with scale disparity (only after 800-mesh A/B approval)
or opt-in `MeshRobustRepair.PreserveSmallParts` (bool, default false initially).

## Look before you build

1. Library: `MeshRobustRepairLostParts` (#1101), `MeshRoundingRepair.cs:899-912`, study §3.1 / §5 (`ROBUST_REPAIR_SCALE_FREE_STUDY.md`),
   benchmark harness on `runs/scale-free-study`.
2. Prior art: per-component bounding-box normalization (standard in CAD / IFC multi-part repair, e.g., MeshLab /
   OpenCascade shape healing per connected shell before assembly).

## Steps (one PR, clean commits)

1. Compare F2 vs B1/B2 (part-wise repair with scale-only frames + exact union verification, study §5): study noted F2
   costs about as much as B1 with less gain — confirm with the owner whether B1+B2 is preferred over F2 if triggered.
2. Build the 29-case procedural multi-scale test suite (both closed and open small components) — tests fail on current
   code (which reports `LostFarPart` / drops them).
3. Implement per-part references (or B1 if chosen) without regressing the area-reference case at `MeshRoundingRepair.cs:899-912`.
4. Run full 800-mesh Thingi10K A/B on a runner (strict criterion, part criterion, `Apply()` flips, solidify/wrap
   fallback delta, wall time); present table to owner before marking ready.

## Acceptance

- Procedural multi-scale suite: small separable parts preserved (closed, manifold, finite, not dropped); invariance
  (`Similarity.All()`), determinism, reuse tests green.
- 800-mesh A/B (command + raw CSV on `runs/`): 0 regressions on single-part meshes; fallback count increase documented
  and accepted by owner.
- Incremental mutation gate on rounding shards (`rounding-1..13`) if `MeshRoundingRepair.cs` changes.

## Sources and licences

Own design (`ROBUST_REPAIR_SCALE_FREE_STUDY.md`); no external code ports.

## Budget and stop rule

1 M session + 1 runner A/B job. Stop if 800-mesh A/B increases solidify/wrap fallbacks by > 2 % or causes any clean ->
unclean regression on single-part Thingi10K meshes.

## Risks and open questions

- Study §5 explicitly flags that B1 (part-wise repair with scale-only frames + exact union verification) may give better
  return per effort than F2; re-check B1 vs F2 with the owner before coding.
