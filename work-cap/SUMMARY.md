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

# Round 3: a cap over all improvement operations (owner decision 2026-11-19: the split-only cap is dropped)

Option: `MeshTetrahedralization.MaxImproveVisitsPerVertex` (default 0 = off, bit for bit). A visit is one `improve(id)` call on a bad tet (it tries edge removal, face removal,
contraction, split, smoothing and boundary moves on it). At most cap x (stage-1 vertices) visits over main and rescue passes together; when spent the improvement ends
(the worst tets are visited first in each pass), `improve_work_cap_reached=1`, the run does not fail. Counted, never wall-clock. Tests: `TetImproveWorkCapTests` (`Visits_*`).

Why visits: in the baseline traces the improver's time follows the visits closely (0.2-0.4 ms per visit; 46017 at floor 20: 223k visits, 91 s, of them 110k visits / 62 s in the
main phase; 58012 at floor 30: 969k visits, 253 s). The slow meshes need 10-40 visits per vertex at floor 20 and 5-185 at floor 30 (median of all meshes: 0.2 and 5.3).
Setup as round 2 (500 meshes, 300 s timeout, same-run baseline), run 44586500696, data in `work-cap/2026-11-19-run44586500696/`.

| floor | budget (visits/vertex) | cap reached (meshes) | improver > 20 s | timeouts | improver time sum | saved | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum (base) / added max on one mesh (share of its tets) |
|---|---|---|---|---|---|---|---|---|
| 20 | 0 (master) | - | 3 | 0 | 549 s | - | - | 4912 |
| 20 | 10 | 7 | 1 | 0 | 458 s | 91 s (16.6 %) | 1.94 / 1 | 6456 / +993 (3.3 %) |
| 20 | 5 | 17 | **0** | 0 | 387 s | 162 s (29.5 %) | 1.94 / 3 | 8822 / +1956 (6.5 %) |
| 20 | 2 | 36 | **0** | 0 | 299 s | 250 s (45.6 %) | 5.62 / 14 | 17184 / +5189 (17.3 %) |
| 30 | 0 (master) | - | 118 | 7 | 8516 s | - | - | 113131 |
| 30 | 10 | 138 | 59 | 0 | 5331 s | 3545 s (41.6 %) | 7.04 / 36 | 202356 / +997 (8.5 %) |
| 30 | 5 | 238 | 12 | 0 | 3686 s | 5000 s (58.7 %) | 8.83 / 82 | 231911 / +2278 (11.2 %) |
| 30 | 2 | 427 | **0** | 0 | 1655 s | 6918 s (81.2 %) | 10.4 / 293 | 527823 / +6452 (20.8 %) |

## Findings

1. **Floor 20 reaches 0 meshes above 20 s at 5 visits per vertex** (and at 2; 10 leaves 1). 5 acts on 17 of 500 meshes, saves 30 % of the improver time, and costs up to
   1.94 degrees of worst angle on a mesh (3 meshes lose more than 0.5), with 3910 more tets below the floor in total (base 4912; at most 6.5 % of one mesh's tets). Smallest tested
   level with 0 slow meshes: 5 (between 5 and 10 was not measured).
2. **Floor 30 only reaches 0 at 2**, which is a different trade: 293 of 493 meshes lose more than 0.5 degrees (max 10.4) and the tets below the floor go from 113k to 528k. At 5: 12 meshes
   stay above 20 s, 82 lose > 0.5 degrees (max 8.8), 59 % of the improver time saved; at 10: 59 stay above 20 s. Floor 30 is a quality target the improver does not reach on most meshes anyway
   (median 43 tets below it in the baseline); the cap trades what is left of that for time.
3. Compared with the split-only cap (round 2), a budget over all operations is the lever that matches the time (the split cap at 8 saved 13 % at floor 20 and left 2 slow meshes).
4. The budget is a count of visits per stage-1 vertex; it scales with mesh size, so a given level means the same work per vertex on a small and on a large mesh.

## Caveats

- The cap changes results wherever it is reached. Default off is bit for bit (tested, `Counts` included). Worst-first order inside a pass means a spent budget leaves the best-ranked tets improved, but
  in the last pass the visited set is a prefix of the sorted bad list.
- Timing is noisy on shared runners (baseline slow count: 3-4 at floor 20, 107-118 at floor 30 over the three runs); counts of visits and angles are deterministic.
- 5 is not an optimum, only the smallest of 2/5/10 that brings floor 20 to 0. A level between 5 and 10 (say 7) would cost less quality and may leave 0-1 slow meshes; not measured.
