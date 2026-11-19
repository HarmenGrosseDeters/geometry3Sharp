# Work cap of the improver (#1154): measurement summary

Option: `MeshTetrahedralization.MaxSplitAttemptsPerVertex` (default 0 = off, bit for bit as before). At most cap x (vertices of the stage-1 mesh) edges
are tried for a split over the whole improvement (counted in `TetMeshImprover.try_split_tet_core`, never wall-clock); once spent, splits are skipped,
the other operations go on, `Counts["improve_work_cap_reached"] = 1` (the key exists only then), the run never fails. Tests: `TetImproveWorkCapTests`.

Setup: the 500 Thingi meshes, `refine <id>.stl 40 600`, 6 GitHub shards, timeout 300 s, run 44550672720 (data: `work-cap/2026-11-19-run44550672720/`).
Baseline (cap 0) and the caps run in the same job on the same runner per mesh. Round 1 (run 44536377935, baseline only) gave the scale: split attempts per
vertex at floor 20: p50 0.19, p90 3.95, the 4 slow meshes 34-120; at floor 30: p50 14.7, p90 89, meshes above 20 s: p10 30, p50 85, p90 263.
Times are the improver's own clock (trace end line). Analyzer: `benchmarks/TetMeshBenchmark/compare/analyze_work_cap.py`.

## Result

| floor | cap | cap reached (meshes) | improver > 20 s | timeouts (300 s) | improver time sum | saved | worst angle lost: max / meshes > 0.5 deg | tets below floor (all meshes): sum / added vs base |
|---|---|---|---|---|---|---|---|---|
| 20 | 0 (master) | - | 4 | 0 | 533 s | - | - | 4912 / - |
| 20 | 8 | 36 | 2 | 0 | 464 s | 69 s (13.0 %) | 4.2 / 7 | 9840 / +4928 (max +2186 on one mesh, 7.3 % of its tets) |
| 20 | 24 | 13 | 3 | 0 | 473 s | 60 s (11.3 %) | 1.94 / 2 | 6482 / +1570 |
| 20 | 64 | 3 | 4 | 0 | 511 s | 22 s (4.1 %) | 1.94 / 1 | 5768 / +856 |
| 30 | 0 (master) | - | 107 | 7 | 8013 s | - | - | 113131 / - |
| 30 | 8 | 388 | 73 | 1 | 6665 s | 2519 s (31.4 %) | 7.17 / 132 | 172547 / +16812 (max +1008, 6.9 % of its tets) |
| 30 | 24 | 177 | 84 | 1 | 7484 s | 1873 s (23.4 %) | 5.69 / 42 | 165002 / +5271 |
| 30 | 64 | 79 | 106 | 3 | 7818 s | 1081 s (13.5 %) | 5.45 / 10 | 146706 / +2359 |

(Meshes above 20 s count a timeout as above. The baseline's slow count differs by one from round 1 (3 vs 4 at floor 20, 111 vs 107 at floor 30): run-to-run
timing noise near the 20 s line. Timeouts are counted as missing results in the saved-time column.)

## Findings

1. **No cap value tested brings floor 20 to 0 meshes above 20 s.** Cap 8 leaves 2 (46017: 91 s -> 74 s, 40011: 24 s -> 29 s), cap 24 leaves 3, cap 64 leaves 4.
   The smallest cap that does it is not among 8/24/64; a smaller one costs more quality (cap 8 already loses up to 4.2 degrees on a mesh and leaves 4928 more
   tets below the floor in total), and the case of 46017 suggests it would not reach 0 anyway (see 2).
2. **A split cap alone cannot remove the tail.** All the slow meshes run all 200 passes (50 main + 150 rescue) with and without the cap. After the cap is reached
   each pass still scans all tets for bad ones and tries edge removal, face removal, contraction and smoothing on every bad tet; that work is the larger
   part of the time left: 46017 (floor 20) needs 74 s at cap 8, 61 s at cap 24; at floor 30 the slowest meshes keep 50-60 % of their time at cap 8 (58012: 254 -> 129 s,
   591237: 214 -> 104 s, 170133: 212 -> 104 s) and 55-65 % at cap 64. The split attempts are therefore about half of the time of the worst meshes, not the 16.7 of 44 s of
   40011 alone that the per-attempt profile showed after #1184. A bound that covers the whole improver (all attempts, or a pass budget) is the next candidate.
3. **At floor 30 the cap is a trade, not a fix**: cap 8 saves 31 % of the improver time and takes the 7 timeouts to 1, but 132 meshes lose more than 0.5 degrees
   (max 7.2) and the tets left below the floor grow by 15 %; 107 -> 73 meshes above 20 s is still a long tail. Cap 64 acts on 79 meshes, loses > 0.5 degrees on 10 and
   hardly moves the tail (106 above 20 s).
4. **At floor 20** the cost is already small (533 s for 500 meshes, 4 slow); the cap saves up to 13 % at the price of up to 4.2 degrees on a mesh. Not worth it
   on its own.

## Caveats

- The cap changes results wherever it is reached (that is its purpose); default off is bit for bit (tested, including `Counts`).
- Timing of this kind is noisy on shared runners (the baseline's slow count moved by 1-4 between two runs); counts and angles are deterministic.
- The cap counts attempts per vertex of the stage-1 mesh; with a different mesh density the same number is a different budget. A per-tet or total-attempt
  variant would need its own measurement.
- Not done (outside this task): a cap on all improvement attempts or a pass budget; a value below 8 at floor 20.
- No PR (data branch). Gate files: none touched on master; on this branch only `.github/workflows/tet-sample.yml` and the benchmark files differ, as in `runs/tet-rescue-stop`.

## Review (independent, REVIEW_CHECKLIST classes 0, 1, 2, 5, 6, 7, 9, 11)

No blocking defect. Fixes after the review (commit after the measurement; the measured run used the code before them, results unchanged by them except as noted):
tests `TheBudget_IsExact` (exactly k attempts, budget above the use changes nothing; kills `>=` -> `>`) and `TheOption_OverflowAndTinyValues` replaced the monotonicity test (not a
real property); `try_split_tet` returns early once the budget is spent (saves the memo and ring-signature work in the remaining passes; the results are the same, "cap reached"
now also means "a split would have been tried after the budget was spent", so the measured counts of meshes where it was reached can differ marginally); doc wording.
Not for merge: `ImproveTrace`/`Trace` (data branch only, would add public API). If the option goes to master it needs a docs line (TET_MESHING.md, ROADMAP) and the net48/pack/full-test steps of CLAUDE.md.
