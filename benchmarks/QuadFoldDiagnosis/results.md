# QuadFoldDiagnosis (total harness time 1106 s, net10.0 Release)

## 0. The copy agrees with the library
status, inverted count, SAT decisions, splits and all offsets of the copy (library settings) equal the library's on 54 / 54 runs.
Clean (independent check of the gate) with the copy: 25; library status Success: 25.

## (a) Budget vs feasibility
SatTotalBudget x10 (3M decisions; SatMaxAttempts and the round limit x10, the per-attempt budget stays 100k) and x10 with the per-attempt budget x10 as well; x100 (30M, per attempt 10M) on the rows still not clean.

| shape | scale / ratio | tris | library status | folds after flow | folds before SAT | folds at end | x10 status (clean) | x10 s | decisions | SAT solved / failed | x10 + attempt x10 status (clean) | s | x100 status (clean) | s | decisions |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sphere | 0.8 / 0.5 | 19936 | BudgetExceeded | 225 | 25 | 22 | OrientationFolds (no) | 1.63 | 1470582 | 10 / 13 | BudgetExceeded (no) | 3.04 | BudgetExceeded (no) | 25.3 | 30000001 |
| sphere | 1.25 / 0.5 | 8204 | BudgetExceeded | 103 | 17 | 13 | OrientationFolds (no) | 0.98 | 1098354 | 5 / 9 | BudgetExceeded (no) | 2.97 | BudgetExceeded (no) | 29.3 | 30000001 |
| sphere | 1 / 0.35 | 26016 | BudgetExceeded | 135 | 22 | 15 | OrientationFolds (no) | 1.81 | 1465189 | 10 / 9 | BudgetExceeded (no) | 4.22 | BudgetExceeded (no) | 39.5 | 30000001 |
| torus | 0.8 / 0.5 | 21964 | OrientationFolds | 135 | 2 | 1 | OrientationFolds (no) | 0.32 | 200182 | 1 / 1 | Success (clean) | 0.47 | - | - | - |
| torus | 1.25 / 0.5 | 8964 | BudgetExceeded | 71 | 11 | 10 | OrientationFolds (no) | 0.76 | 1020850 | 4 / 5 | BudgetExceeded (no) | 2.10 | BudgetExceeded (no) | 20.8 | 30000001 |
| torus | 1.5 / 0.5 | 6196 | OrientationFolds | 95 | 6 | 1 | OrientationFolds (no) | 0.18 | 180216 | 4 / 1 | Success (clean) | 0.35 | - | - | - |
| bumpy | 1 / 0.5 | 35832 | BudgetExceeded | 348 | 27 | 20 | OrientationFolds (no) | 1.51 | 997068 | 17 / 7 | BudgetExceeded (no) | 3.54 | BudgetExceeded (no) | 31.0 | 30000001 |
| bumpy | 0.8 / 0.5 | 55724 | BudgetExceeded | 765 | 50 | 48 | OrientationFolds (no) | 4.45 | 2885875 | 25 / 20 | BudgetExceeded (no) | 4.28 | BudgetExceeded (no) | 34.3 | 30000001 |
| bumpy | 1.25 / 0.5 | 22810 | BudgetExceeded | 370 | 22 | 14 | OrientationFolds (no) | 0.99 | 853044 | 9 / 13 | BudgetExceeded (no) | 2.92 | BudgetExceeded (no) | 25.9 | 30000001 |
| bumpy | 1 / 0.35 | 73048 | BudgetExceeded | 749 | 202 | 194 | BudgetExceeded (no) | 3.94 | 3000001 | 19 / 18 | BudgetExceeded (no) | 3.08 | BudgetExceeded (no) | 20.4 | 30000001 |
| bumpy | 1 / 0.7 | 18266 | BudgetExceeded | 295 | 14 | 5 | OrientationFolds (no) | 0.85 | 707327 | 7 / 5 | BudgetExceeded (no) | 3.37 | OrientationFolds (no) | 26.6 | 25767490 |
| filleted-box | 0.8 / 0.5 | 28486 | BudgetExceeded | 63 | 8 | 8 | OrientationFolds (no) | 1.29 | 1310885 | 0 / 8 | BudgetExceeded (no) | 2.58 | BudgetExceeded (no) | 27.0 | 30000001 |
| noisy-sphere | 1 / 0.5 | 12776 | BudgetExceeded | 236 | 23 | 16 | OrientationFolds (no) | 2.34 | 2014062 | 11 / 9 | BudgetExceeded (no) | 2.93 | BudgetExceeded (no) | 28.6 | 30000001 |
| noisy-sphere | 0.8 / 0.5 | 19936 | BudgetExceeded | 165 | 60 | 49 | OrientationFolds (no) | 2.95 | 2872701 | 14 / 37 | BudgetExceeded (no) | 3.03 | BudgetExceeded (no) | 21.0 | 30000001 |
| noisy-sphere | 1.5 / 0.5 | 5706 | BudgetExceeded | 136 | 35 | 28 | OrientationFolds (no) | 1.13 | 1459584 | 12 / 20 | BudgetExceeded (no) | 2.65 | BudgetExceeded (no) | 25.5 | 30000001 |
| noisy-sphere | 1 / 0.35 | 26016 | BudgetExceeded | 263 | 276 | 271 | BudgetExceeded (no) | 3.90 | 3000001 | 22 / 31 | BudgetExceeded (no) | 4.35 | BudgetExceeded (no) | 27.8 | 30000001 |
| noisy-sphere | 1 / 0.7 | 6506 | BudgetExceeded | 201 | 154 | 154 | BudgetExceeded (no) | 2.50 | 3000001 | 11 / 47 | BudgetExceeded (no) | 2.55 | BudgetExceeded (no) | 25.2 | 30000001 |
| capsule | 1 / 0.5 | 11042 | BudgetExceeded | 326 | 132 | 126 | BudgetExceeded (no) | 3.70 | 3000001 | 8 / 26 | BudgetExceeded (no) | 3.48 | BudgetExceeded (no) | 29.1 | 30000001 |
| capsule | 0.8 / 0.5 | 17052 | BudgetExceeded | 502 | 124 | 124 | BudgetExceeded (no) | 3.89 | 3000001 | 6 / 33 | BudgetExceeded (no) | 3.43 | BudgetExceeded (no) | 31.6 | 30000001 |
| capsule | 1.25 / 0.5 | 6988 | BudgetExceeded | 315 | 67 | 65 | BudgetExceeded (no) | 3.13 | 3000001 | 13 / 18 | BudgetExceeded (no) | 3.23 | BudgetExceeded (no) | 29.4 | 30000001 |
| capsule | 1.5 / 0.5 | 4802 | BudgetExceeded | 130 | 27 | 23 | OrientationFolds (no) | 2.41 | 2419167 | 8 / 15 | BudgetExceeded (no) | 3.27 | BudgetExceeded (no) | 27.7 | 30000001 |
| capsule | 1 / 0.35 | 22404 | BudgetExceeded | 306 | 80 | 74 | BudgetExceeded (no) | 3.93 | 3000001 | 20 / 31 | BudgetExceeded (no) | 3.81 | BudgetExceeded (no) | 37.0 | 30000001 |
| capsule | 1 / 0.7 | 5576 | BudgetExceeded | 307 | 54 | 49 | BudgetExceeded (no) | 3.11 | 3000001 | 9 / 31 | BudgetExceeded (no) | 3.07 | BudgetExceeded (no) | 28.1 | 30000001 |
| capped-cylinder | 1 / 0.5 | 13012 | BudgetExceeded | 479 | 189 | 188 | BudgetExceeded (no) | 2.55 | 3000001 | 5 / 43 | BudgetExceeded (no) | 2.54 | BudgetExceeded (no) | 26.8 | 30000001 |
| capped-cylinder | 0.8 / 0.5 | 20342 | BudgetExceeded | 270 | 170 | 169 | BudgetExceeded (no) | 4.07 | 3000001 | 12 / 31 | BudgetExceeded (no) | 4.36 | BudgetExceeded (no) | 31.6 | 30000001 |
| capped-cylinder | 1.25 / 0.5 | 8290 | BudgetExceeded | 106 | 17 | 10 | OrientationFolds (no) | 1.06 | 1075428 | 6 / 8 | BudgetExceeded (no) | 3.34 | BudgetExceeded (no) | 31.2 | 30000001 |
| capped-cylinder | 1.5 / 0.5 | 5700 | BudgetExceeded | 167 | 27 | 27 | OrientationFolds (no) | 2.04 | 1967031 | 12 / 13 | BudgetExceeded (no) | 2.90 | BudgetExceeded (no) | 31.2 | 30000001 |
| capped-cylinder | 1 / 0.35 | 26606 | BudgetExceeded | 187 | 53 | 52 | BudgetExceeded (no) | 3.85 | 3000001 | 7 / 34 | BudgetExceeded (no) | 2.94 | BudgetExceeded (no) | 28.2 | 30000001 |
| capped-cylinder | 1 / 0.7 | 6576 | OrientationFolds | 241 | 8 | 2 | OrientationFolds (no) | 0.20 | 161930 | 5 / 2 | OrientationFolds (no) | 0.55 | OrientationFolds (no) | 0.6 | 509651 |

Fallbacks 29 (BudgetExceeded 26, OrientationFolds 3, other 0).
BudgetExceeded rows clean with x10: 0 / 26 (time of those: median NaN s, max 0.00 s); with the attempt budget x10 too: 0 / 26; either: 0.
BudgetExceeded rows: x10 total time 64.8 s (library 19.9 s incl. stage 2). x10 outcome: OrientationFolds 15, BudgetExceeded 11.
x100 run on 26 BudgetExceeded rows: clean 0; BudgetExceeded 25, OrientationFolds 1.
OrientationFolds rows clean with x10: 2 / 3.

clean per shape (library -> x10 either variant -> x100):
- sphere: 3 / 6 -> 3 -> 3
- torus: 3 / 6 -> 5 -> 5
- torus-align10: 6 / 6 -> 6 -> 6
- bumpy: 1 / 6 -> 1 -> 1
- filleted-box: 5 / 6 -> 5 -> 5
- noisy-sphere: 1 / 6 -> 1 -> 1
- capsule: 0 / 6 -> 0 -> 0
- genus2: 6 / 6 -> 6 -> 6
- capped-cylinder: 0 / 6 -> 0 -> 0

## (b) Single take-back of a flow correction, on the folds right after the flow (original triangles, before subdivision)
A correction is the value of an edge after the flow (and the greedy balancing of the fixed variables) minus the clamped position-field offset. Edge level: the edge is reset to its raw offset (both components); unit level: one component moves one unit back toward the raw offset. The fold counts as un-inverted if the two determinants of its offset pairs that contain the changed edge are both >= 0 (the sum-zero constraint is not kept: diagnostic only). 'folds' are triangles with det < 0 after the flow (before subdivision, greedy and SAT).

| shape | runs with folds | folds after flow (sum of 6) | folds before SAT (sum, after subdivision + greedy) | fold clusters (3D, 1.5 s) | corrections per fold 0 / 1 / 2 / 3 | un-inverted by one edge take-back | by one unit take-back | by either | fold with 0 corrections |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 6 / 6 | 742 | 77 | 234 | 0 / 6 / 641 / 95 | 100 % (742) | 96 % (712) | 100 % (742) | 0 |
| torus | 6 / 6 | 638 | 42 | 239 | 1 / 11 / 589 / 37 | 100 % (636) | 98 % (623) | 100 % (636) | 1 |
| torus-align10 | 6 / 6 | 418 | 9 | 190 | 0 / 5 / 409 / 4 | 100 % (417) | 100 % (417) | 100 % (417) | 0 |
| bumpy | 6 / 6 | 2725 | 322 | 725 | 0 / 14 / 2374 / 337 | 100 % (2722) | 98 % (2663) | 100 % (2722) | 0 |
| filleted-box | 6 / 6 | 294 | 13 | 135 | 0 / 6 / 273 / 15 | 100 % (294) | 99 % (292) | 100 % (294) | 0 |
| noisy-sphere | 6 / 6 | 1147 | 556 | 313 | 1 / 11 / 895 / 240 | 100 % (1144) | 90 % (1034) | 100 % (1144) | 1 |
| capsule | 6 / 6 | 1886 | 484 | 231 | 0 / 18 / 1719 / 149 | 100 % (1884) | 97 % (1836) | 100 % (1885) | 0 |
| genus2 | 6 / 6 | 965 | 46 | 383 | 1 / 16 / 879 / 69 | 100 % (964) | 98 % (949) | 100 % (964) | 1 |
| capped-cylinder | 6 / 6 | 1450 | 464 | 253 | 0 / 14 / 1287 / 149 | 100 % (1450) | 95 % (1381) | 100 % (1450) | 0 |
| **all** | 54 / 54 | 10265 | 2013 | 2703 | 3 / 101 / 9066 / 1095 | 100 % (10253) | 97 % (9907) | 100 % (10254) | 3 |


Persistent folds: a fold after the flow is persistent if a triangle that came out of its subdivision is still inverted before the SAT (after subdivision and greedy moves). Take-back statistics for the persistent ones only:

| shape | folds after flow | persistent | persistent un-inverted by an edge take-back | by a unit take-back | by either | persistent with 0 corrections | corrections per persistent fold 0 / 1 / 2 / 3 | folds before SAT (subdivided triangles) | of those with a parent that was not a fold after the flow |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 742 | 50 | 100 % (50) | 86 % (43) | 100 % (50) | 0 | 0 / 0 / 33 / 17 | 77 | 11 |
| torus | 638 | 34 | 100 % (34) | 91 % (31) | 100 % (34) | 0 | 0 / 0 / 28 / 6 | 42 | 3 |
| torus-align10 | 418 | 8 | 100 % (8) | 100 % (8) | 100 % (8) | 0 | 0 / 0 / 8 / 0 | 9 | 1 |
| bumpy | 2725 | 255 | 100 % (255) | 89 % (228) | 100 % (255) | 0 | 0 / 0 / 168 / 87 | 322 | 33 |
| filleted-box | 294 | 8 | 100 % (8) | 75 % (6) | 100 % (8) | 0 | 0 / 0 / 3 / 5 | 13 | 1 |
| noisy-sphere | 1147 | 179 | 100 % (179) | 65 % (116) | 100 % (179) | 0 | 0 / 0 / 100 / 79 | 556 | 75 |
| capsule | 1886 | 396 | 100 % (396) | 94 % (371) | 100 % (396) | 0 | 0 / 0 / 327 / 69 | 484 | 40 |
| genus2 | 965 | 38 | 100 % (38) | 84 % (32) | 100 % (38) | 0 | 0 / 0 / 21 / 17 | 46 | 2 |
| capped-cylinder | 1450 | 249 | 100 % (249) | 82 % (204) | 100 % (249) | 0 | 0 / 0 / 184 / 65 | 464 | 37 |
| **all** | 10265 | 1217 | 100 % (1217) | 85 % (1039) | 100 % (1217) | 0 | 0 / 0 / 872 / 345 | 2013 | 203 |

Same, by whether the run ends clean in the library (folds after flow of the 25 clean / 29 fallback rows):
- clean rows (25): 2574 folds after flow, 100 % (2571) by an edge take-back, 98 % (2532) by a unit take-back, 100 % (2571) by either; folds before SAT 111.
- fallback rows (29): 7691 folds after flow, 100 % (7682) by an edge take-back, 96 % (7375) by a unit take-back, 100 % (7683) by either; folds before SAT 1902.

## (c) Where the folds sit
Distance in units of the target edge length s, 3D, from the fold triangle's centroid to the nearest singular face of the cross field (centroid), and on capsule / capped cylinder to the cap-cylinder seam (capsule: arc length on the surface; capped cylinder: 3D distance to the two edge circles). 'all' is the same measure over all triangles of the input (the share of the surface that lies within the radius), so the ratio fold / all shows concentration.

| shape | folds | singularities per run (median) | median dist to singularity | folds within 1 s / 2 s / 3 s | all triangles within 1 s / 2 s / 3 s | final folds of fallbacks within 2 s (n) | seam: folds within 1 s / 2 s | seam: all triangles within 1 s / 2 s |
|---|---|---|---|---|---|---|---|---|
| sphere | 742 | 8 | 4.5 | 4 % (30) / 14 % (103) / 26 % (194) | 3 % (253) / 10 % (965) / 22 % (2173) | 12 % (6) (50) | - | - |
| torus | 638 | 12 | 3.9 | 6 % (39) / 16 % (99) / 32 % (202) | 3 % (316) / 14 % (1345) / 31 % (3069) | 25 % (3) (12) | - | - |
| torus-align10 | 418 | 0 | - | - | - | - | - | - |
| bumpy | 2725 | 16 | 9.2 | 2 % (65) / 5 % (139) / 9 % (245) | 2 % (145) / 4 % (410) / 8 % (745) | 8 % (22) (281) | - | - |
| filleted-box | 294 | 8 | 4.9 | 11 % (33) / 18 % (53) / 31 % (90) | 2 % (181) / 7 % (663) / 14 % (1331) | 100 % (8) (8) | - | - |
| noisy-sphere | 1147 | 8 | 4.7 | 4 % (51) / 15 % (168) / 28 % (318) | 3 % (252) / 11 % (1035) / 23 % (2284) | 32 % (167) (518) | - | - |
| capsule | 1886 | 10 | 8.8 | 3 % (56) / 6 % (121) / 10 % (191) | 2 % (185) / 5 % (501) / 9 % (941) | 10 % (45) (461) | 11 % (209) / 23 % (435) | 15 % (1538) / 31 % (3118) |
| genus2 | 965 | 20 | 4.0 | 6 % (58) / 18 % (169) / 31 % (300) | 3 % (315) / 13 % (1201) / 28 % (2671) | - | - | - |
| capped-cylinder | 1450 | 8 | 6.0 | 3 % (40) / 8 % (117) / 14 % (207) | 3 % (263) / 9 % (936) / 18 % (1769) | 35 % (158) (448) | 14 % (201) / 27 % (389) | 14 % (1394) / 29 % (2853) |


The same for the folds that survive subdivision and greedy moves (triangles with det < 0 just before the SAT; centroids of the subdivided triangles):

| shape | folds before SAT | within 1 s / 2 s / 3 s of a singularity | seam: within 1 s / 2 s | clusters (1.5 s) |
|---|---|---|---|---|
| sphere | 77 | 6 % (5) / 14 % (11) / 22 % (17) | - | 35 |
| torus | 42 | 2 % (1) / 17 % (7) / 40 % (17) | - | 29 |
| torus-align10 | 9 | - | - | 9 |
| bumpy | 322 | 5 % (15) / 8 % (26) / 11 % (35) | - | 136 |
| filleted-box | 13 | 77 % (10) / 77 % (10) / 77 % (10) | - | 5 |
| noisy-sphere | 556 | 17 % (92) / 31 % (171) / 44 % (244) | - | 95 |
| capsule | 484 | 7 % (34) / 10 % (47) / 14 % (69) | 10 % (47) / 19 % (94) | 138 |
| genus2 | 46 | 4 % (2) / 15 % (7) / 24 % (11) | - | 32 |
| capped-cylinder | 464 | 23 % (107) / 34 % (159) / 40 % (185) | 7 % (32) / 16 % (73) | 79 |

Recurrence across the 6 variants: a fold cluster (3D single linkage 1.5 s) of one variant 'recurs' in another if that variant has a fold triangle within 2 x s0 (the base target length) of the cluster's centroid. Expected = share of the surface (triangle centroids of the cluster's own mesh) within 2 s0 of any fold of the other variant, i.e. what a random place would give. The cross field and the input mesh differ per variant.

| shape | fold clusters | observed recurrences per cluster (of 5 others) | expected by chance | clusters recurring in >= 1 | in >= 3 | variants with folds |
|---|---|---|---|---|---|---|
| sphere | 234 | 2.39 | 2.31 | 94 % (220) | 50 % (116) | 6 / 6 |
| torus | 239 | 2.13 | 2.12 | 92 % (221) | 37 % (89) | 6 / 6 |
| torus-align10 | 190 | 2.44 | 1.50 | 95 % (181) | 50 % (95) | 6 / 6 |
| bumpy | 725 | 2.73 | 2.58 | 97 % (702) | 57 % (412) | 6 / 6 |
| filleted-box | 135 | 1.27 | 0.88 | 72 % (97) | 16 % (21) | 6 / 6 |
| noisy-sphere | 313 | 3.22 | 3.13 | 98 % (307) | 77 % (241) | 6 / 6 |
| capsule | 231 | 3.75 | 3.49 | 99 % (229) | 86 % (198) | 6 / 6 |
| genus2 | 383 | 1.82 | 1.72 | 87 % (334) | 28 % (107) | 6 / 6 |
| capped-cylinder | 253 | 3.25 | 3.02 | 99 % (251) | 74 % (187) | 6 / 6 |

## (d) Flow re-solve with the implicated corrections penalized (at most 2 re-solves, step cost of an implicated (edge, component, direction) max(c, 100) x 10)
Implicated: on every fold after the flow, the balanced edges whose single take-back (edge level, or unit level where that works) un-inverts it; the same flow is solved again from the same start values (capacity loop from 2) with those steps dearer; the penalties accumulate over the re-solves. Same SAT budget (300k decisions) as the library.

| shape | scale / ratio | library status | folds after flow: solve 1 / 2 / 3 | folds before SAT: library -> penalized | status penalized (clean) | t library copy (s) | t penalized (s) | decisions lib -> pen | penalized (edge, comp, dir) |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 1 / 0.5 | Success | 122 / 70 / 38 | 4 -> 11 | BudgetExceeded (no) | 0.37 | 0.82 | 30941 -> 300001 | 218 |
| sphere | 0.8 / 0.5 | BudgetExceeded | 225 / 138 / 100 | 25 -> 15 | BudgetExceeded (no) | 0.46 | 0.70 | 300001 -> 300001 | 406 |
| sphere | 1.25 / 0.5 | BudgetExceeded | 103 / 67 / 51 | 17 -> 9 | BudgetExceeded (no) | 0.31 | 0.40 | 300001 -> 300001 | 206 |
| sphere | 1.5 / 0.5 | Success | 96 / 71 / 55 | 9 -> 5 | Success (clean) | 0.18 | 0.18 | 136016 -> 54703 | 184 |
| sphere | 1 / 0.35 | BudgetExceeded | 135 / 93 / 79 | 22 -> 14 | Success (clean) | 0.48 | 0.76 | 300001 -> 226286 | 267 |
| sphere | 1 / 0.7 | Success | 61 / 42 / 21 | 0 -> 0 | Success (clean) | 0.06 | 0.13 | 0 -> 0 | 133 |
| torus | 1 / 0.5 | Success | 85 / 58 / 42 | 2 -> 0 | Success (clean) | 0.08 | 0.22 | 85 -> 0 | 142 |
| torus | 0.8 / 0.5 | OrientationFolds | 135 / 109 / 63 | 2 -> 2 | Success (clean) | 0.28 | 0.32 | 200182 -> 80 | 271 |
| torus | 1.25 / 0.5 | BudgetExceeded | 71 / 48 / 40 | 11 -> 2 | Success (clean) | 0.33 | 0.22 | 300001 -> 62 | 124 |
| torus | 1.5 / 0.5 | OrientationFolds | 95 / 54 / 38 | 6 -> 4 | OrientationFolds (no) | 0.18 | 0.31 | 180216 -> 202678 | 171 |
| torus | 1 / 0.35 | Success | 188 / 142 / 103 | 21 -> 16 | BudgetExceeded (no) | 0.18 | 0.77 | 3749 -> 300001 | 347 |
| torus | 1 / 0.7 | Success | 64 / 26 / 9 | 0 -> 1 | Success (clean) | 0.04 | 0.08 | 0 -> 1751 | 82 |
| torus-align10 | 1 / 0.5 | Success | 85 / 47 / 33 | 1 -> 0 | Success (clean) | 0.07 | 0.16 | 10 -> 0 | 134 |
| torus-align10 | 0.8 / 0.5 | Success | 114 / 77 / 61 | 1 -> 1 | Success (clean) | 0.11 | 0.36 | 7 -> 1413 | 192 |
| torus-align10 | 1.25 / 0.5 | Success | 39 / 36 / 11 | 0 -> 0 | Success (clean) | 0.06 | 0.11 | 0 -> 0 | 75 |
| torus-align10 | 1.5 / 0.5 | Success | 17 / 11 / 8 | 0 -> 0 | Success (clean) | 0.03 | 0.12 | 0 -> 0 | 28 |
| torus-align10 | 1 / 0.35 | Success | 111 / 75 / 49 | 7 -> 2 | Success (clean) | 0.15 | 0.28 | 12373 -> 140 | 192 |
| torus-align10 | 1 / 0.7 | Success | 52 / 25 / 9 | 0 -> 0 | Success (clean) | 0.05 | 0.07 | 0 -> 0 | 67 |
| bumpy | 1 / 0.5 | BudgetExceeded | 348 / 241 / 176 | 27 -> 11 | OrientationFolds (no) | 0.79 | 1.51 | 300001 -> 236061 | 701 |
| bumpy | 0.8 / 0.5 | BudgetExceeded | 765 / 543 / 384 | 50 -> 13 | Success (clean) | 1.44 | 3.35 | 300001 -> 227107 | 1454 |
| bumpy | 1.25 / 0.5 | BudgetExceeded | 370 / 263 / 218 | 22 -> 25 | BudgetExceeded (no) | 0.50 | 1.03 | 300001 -> 300001 | 744 |
| bumpy | 1.5 / 0.5 | Success | 198 / 133 / 69 | 7 -> 7 | Success (clean) | 0.32 | 0.47 | 229444 -> 568 | 402 |
| bumpy | 1 / 0.35 | BudgetExceeded | 749 / 542 / 460 | 202 -> 131 | BudgetExceeded (no) | 1.24 | 3.30 | 300001 -> 300001 | 1613 |
| bumpy | 1 / 0.7 | BudgetExceeded | 295 / 183 / 113 | 14 -> 13 | BudgetExceeded (no) | 0.50 | 0.90 | 300001 -> 300001 | 525 |
| filleted-box | 1 / 0.5 | Success | 33 / 25 / 13 | 0 -> 0 | Success (clean) | 0.08 | 0.17 | 0 -> 0 | 59 |
| filleted-box | 0.8 / 0.5 | BudgetExceeded | 63 / 51 / 44 | 8 -> 8 | BudgetExceeded (no) | 0.37 | 0.69 | 300001 -> 300001 | 121 |
| filleted-box | 1.25 / 0.5 | Success | 56 / 38 / 27 | 0 -> 1 | Success (clean) | 0.05 | 0.11 | 0 -> 23 | 104 |
| filleted-box | 1.5 / 0.5 | Success | 26 / 11 / 11 | 1 -> 1 | Success (clean) | 0.03 | 0.05 | 19 -> 19 | 32 |
| filleted-box | 1 / 0.35 | Success | 49 / 38 / 62 | 2 -> 5 | Success (clean) | 0.17 | 0.36 | 46 -> 325 | 87 |
| filleted-box | 1 / 0.7 | Success | 67 / 51 / 46 | 2 -> 2 | Success (clean) | 0.07 | 0.22 | 19107 -> 213271 | 109 |
| noisy-sphere | 1 / 0.5 | BudgetExceeded | 236 / 125 / 89 | 23 -> 11 | BudgetExceeded (no) | 0.46 | 0.68 | 300001 -> 300001 | 404 |
| noisy-sphere | 0.8 / 0.5 | BudgetExceeded | 165 / 122 / 85 | 60 -> 54 | BudgetExceeded (no) | 1.16 | 3.09 | 300001 -> 300001 | 411 |
| noisy-sphere | 1.25 / 0.5 | Success | 146 / 99 / 74 | 8 -> 8 | Success (clean) | 0.07 | 0.45 | 17851 -> 264531 | 302 |
| noisy-sphere | 1.5 / 0.5 | BudgetExceeded | 136 / 86 / 61 | 35 -> 35 | BudgetExceeded (no) | 0.38 | 0.50 | 300001 -> 300001 | 268 |
| noisy-sphere | 1 / 0.35 | BudgetExceeded | 263 / 206 / 187 | 276 -> 203 | BudgetExceeded (no) | 1.92 | 5.17 | 300001 -> 300001 | 527 |
| noisy-sphere | 1 / 0.7 | BudgetExceeded | 201 / 136 / 87 | 154 -> 148 | BudgetExceeded (no) | 0.37 | 0.54 | 300001 -> 300001 | 520 |
| capsule | 1 / 0.5 | BudgetExceeded | 326 / 259 / 204 | 132 -> 74 | BudgetExceeded (no) | 0.65 | 1.21 | 300001 -> 300001 | 926 |
| capsule | 0.8 / 0.5 | BudgetExceeded | 502 / 306 / 266 | 124 -> 64 | BudgetExceeded (no) | 0.78 | 1.35 | 300001 -> 300001 | 1093 |
| capsule | 1.25 / 0.5 | BudgetExceeded | 315 / 142 / 50 | 67 -> 9 | BudgetExceeded (no) | 0.38 | 0.59 | 300001 -> 300001 | 588 |
| capsule | 1.5 / 0.5 | BudgetExceeded | 130 / 79 / 56 | 27 -> 14 | BudgetExceeded (no) | 0.42 | 0.47 | 300001 -> 300001 | 285 |
| capsule | 1 / 0.35 | BudgetExceeded | 306 / 177 / 133 | 80 -> 38 | BudgetExceeded (no) | 0.53 | 1.04 | 300001 -> 300001 | 527 |
| capsule | 1 / 0.7 | BudgetExceeded | 307 / 135 / 56 | 54 -> 6 | FlippedFaces (no) | 0.42 | 0.32 | 300001 -> 1022 | 511 |
| genus2 | 1 / 0.5 | Success | 145 / 113 / 56 | 5 -> 2 | Success (clean) | 0.43 | 0.50 | 216448 -> 1083 | 280 |
| genus2 | 0.8 / 0.5 | Success | 242 / 175 / 124 | 3 -> 4 | Success (clean) | 0.35 | 0.78 | 354 -> 94 | 447 |
| genus2 | 1.25 / 0.5 | Success | 164 / 115 / 84 | 16 -> 15 | BudgetExceeded (no) | 0.36 | 0.98 | 125627 -> 300001 | 299 |
| genus2 | 1.5 / 0.5 | Success | 59 / 37 / 23 | 0 -> 0 | Success (clean) | 0.08 | 0.15 | 0 -> 0 | 109 |
| genus2 | 1 / 0.35 | Success | 198 / 139 / 118 | 22 -> 18 | OrientationFolds (no) | 0.54 | 0.97 | 125633 -> 203476 | 380 |
| genus2 | 1 / 0.7 | Success | 157 / 91 / 58 | 0 -> 0 | Success (clean) | 0.10 | 0.26 | 0 -> 0 | 266 |
| capped-cylinder | 1 / 0.5 | BudgetExceeded | 479 / 397 / 290 | 189 -> 164 | BudgetExceeded (no) | 0.46 | 0.98 | 300001 -> 300001 | 1156 |
| capped-cylinder | 0.8 / 0.5 | BudgetExceeded | 270 / 173 / 116 | 170 -> 52 | BudgetExceeded (no) | 1.55 | 4.61 | 300001 -> 300001 | 477 |
| capped-cylinder | 1.25 / 0.5 | BudgetExceeded | 106 / 100 / 63 | 17 -> 7 | Success (no) | 0.40 | 0.21 | 300001 -> 11290 | 212 |
| capped-cylinder | 1.5 / 0.5 | BudgetExceeded | 167 / 93 / 85 | 27 -> 14 | BudgetExceeded (no) | 0.32 | 0.47 | 300001 -> 300001 | 257 |
| capped-cylinder | 1 / 0.35 | BudgetExceeded | 187 / 136 / 111 | 53 -> 56 | BudgetExceeded (no) | 0.60 | 1.27 | 300001 -> 300001 | 334 |
| capped-cylinder | 1 / 0.7 | OrientationFolds | 241 / 144 / 97 | 8 -> 13 | Success (no) | 0.20 | 0.41 | 161930 -> 238677 | 500 |

Clean: library 25 / 54 -> penalized 25 / 54; newly clean 4, lost 4.
Folds after the flow, sum over the 54 runs: solve 1 10265, solve 2 6893, solve 3 5006. Folds before SAT: 2013 -> 1308.
Time (sum of Run() of the copy over the 54 runs): library settings 21.9 s, penalized 45.2 s; fallback rows 17.9 -> 36.4 s; clean rows (library) 4.0 -> 8.8 s.
clean per shape (library -> penalized):
- sphere: 3 / 6 -> 3
- torus: 3 / 6 -> 4
- torus-align10: 6 / 6 -> 6
- bumpy: 1 / 6 -> 2
- filleted-box: 5 / 6 -> 5
- noisy-sphere: 1 / 6 -> 1
- capsule: 0 / 6 -> 0
- genus2: 6 / 6 -> 4
- capped-cylinder: 0 / 6 -> 0

## (d2) Same, with the penalties taken from the persistent folds only (folds that survive subdivision and greedy moves); two re-solves, each carried through the greedy moves (the penalties accumulate); the SAT runs on the last one only

| shape | scale / ratio | library status | folds before SAT: library -> re-solve 1 -> re-solve 2 | penalized steps | status (clean) | decisions lib -> pen | t library copy (s) | t all three solves + SAT (s) |
|---|---|---|---|---|---|---|---|---|
| sphere | 1 / 0.5 | Success | 4 -> 2 -> 2 | 2 | Success (clean) | 30941 -> 22142 | 0.37 | 0.74 |
| sphere | 0.8 / 0.5 | BudgetExceeded | 25 -> 16 -> 10 | 34 | BudgetExceeded (no) | 300001 -> 300001 | 0.46 | 0.78 |
| sphere | 1.25 / 0.5 | BudgetExceeded | 17 -> 12 -> 11 | 23 | BudgetExceeded (no) | 300001 -> 300001 | 0.31 | 0.41 |
| sphere | 1.5 / 0.5 | Success | 9 -> 5 -> 6 | 15 | Success (clean) | 136016 -> 411 | 0.18 | 0.17 |
| sphere | 1 / 0.35 | BudgetExceeded | 22 -> 9 -> 5 | 43 | Success (clean) | 300001 -> 134046 | 0.48 | 0.84 |
| torus | 1 / 0.5 | Success | 2 -> 0 -> 0 | 1 | Success (clean) | 85 -> 0 | 0.08 | 0.22 |
| torus | 0.8 / 0.5 | OrientationFolds | 2 -> 0 -> 0 | 5 | Success (clean) | 200182 -> 0 | 0.28 | 0.39 |
| torus | 1.25 / 0.5 | BudgetExceeded | 11 -> 4 -> 2 | 6 | Success (clean) | 300001 -> 69681 | 0.33 | 0.27 |
| torus | 1.5 / 0.5 | OrientationFolds | 6 -> 4 -> 3 | 18 | OrientationFolds (no) | 180216 -> 203901 | 0.18 | 0.41 |
| torus | 1 / 0.35 | Success | 21 -> 6 -> 4 | 27 | BudgetExceeded (no) | 3749 -> 300001 | 0.18 | 0.87 |
| torus-align10 | 1 / 0.5 | Success | 1 -> 0 -> 0 | 1 | Success (clean) | 10 -> 0 | 0.07 | 0.20 |
| torus-align10 | 0.8 / 0.5 | Success | 1 -> 0 -> 0 | 1 | Success (clean) | 7 -> 0 | 0.11 | 0.29 |
| torus-align10 | 1 / 0.35 | Success | 7 -> 1 -> 1 | 6 | Success (clean) | 12373 -> 235 | 0.15 | 0.44 |
| bumpy | 1 / 0.5 | BudgetExceeded | 27 -> 10 -> 10 | 51 | Success (clean) | 300001 -> 5676 | 0.79 | 1.33 |
| bumpy | 0.8 / 0.5 | BudgetExceeded | 50 -> 22 -> 20 | 78 | BudgetExceeded (no) | 300001 -> 300001 | 1.44 | 3.34 |
| bumpy | 1.25 / 0.5 | BudgetExceeded | 22 -> 15 -> 12 | 38 | BudgetExceeded (no) | 300001 -> 300001 | 0.50 | 1.08 |
| bumpy | 1.5 / 0.5 | Success | 7 -> 6 -> 2 | 10 | Success (clean) | 229444 -> 9 | 0.32 | 0.55 |
| bumpy | 1 / 0.35 | BudgetExceeded | 202 -> 138 -> 87 | 425 | BudgetExceeded (no) | 300001 -> 300001 | 1.24 | 3.41 |
| bumpy | 1 / 0.7 | BudgetExceeded | 14 -> 12 -> 12 | 17 | BudgetExceeded (no) | 300001 -> 300001 | 0.50 | 1.09 |
| filleted-box | 0.8 / 0.5 | BudgetExceeded | 8 -> 6 -> 8 | 7 | BudgetExceeded (no) | 300001 -> 300001 | 0.37 | 0.71 |
| filleted-box | 1.5 / 0.5 | Success | 1 -> 1 -> 1 | 0 | Success (clean) | 19 -> 19 | 0.03 | 0.12 |
| filleted-box | 1 / 0.35 | Success | 2 -> 3 -> 1 | 5 | Success (clean) | 46 -> 17 | 0.17 | 0.54 |
| filleted-box | 1 / 0.7 | Success | 2 -> 0 -> 0 | 1 | Success (clean) | 19107 -> 0 | 0.07 | 0.12 |
| noisy-sphere | 1 / 0.5 | BudgetExceeded | 23 -> 10 -> 8 | 41 | BudgetExceeded (no) | 300001 -> 300001 | 0.46 | 0.71 |
| noisy-sphere | 0.8 / 0.5 | BudgetExceeded | 60 -> 46 -> 50 | 52 | BudgetExceeded (no) | 300001 -> 300001 | 1.16 | 3.10 |
| noisy-sphere | 1.25 / 0.5 | Success | 8 -> 5 -> 2 | 12 | Success (clean) | 17851 -> 4202 | 0.07 | 0.16 |
| noisy-sphere | 1.5 / 0.5 | BudgetExceeded | 35 -> 26 -> 21 | 54 | BudgetExceeded (no) | 300001 -> 300001 | 0.38 | 0.50 |
| noisy-sphere | 1 / 0.35 | BudgetExceeded | 276 -> 213 -> 197 | 150 | BudgetExceeded (no) | 300001 -> 300001 | 1.92 | 5.13 |
| noisy-sphere | 1 / 0.7 | BudgetExceeded | 154 -> 164 -> 146 | 156 | BudgetExceeded (no) | 300001 -> 300001 | 0.37 | 0.42 |
| capsule | 1 / 0.5 | BudgetExceeded | 132 -> 89 -> 62 | 313 | BudgetExceeded (no) | 300001 -> 300001 | 0.65 | 1.35 |
| capsule | 0.8 / 0.5 | BudgetExceeded | 124 -> 65 -> 70 | 284 | BudgetExceeded (no) | 300001 -> 300001 | 0.78 | 1.35 |
| capsule | 1.25 / 0.5 | BudgetExceeded | 67 -> 30 -> 26 | 123 | BudgetExceeded (no) | 300001 -> 300001 | 0.38 | 0.56 |
| capsule | 1.5 / 0.5 | BudgetExceeded | 27 -> 11 -> 14 | 65 | BudgetExceeded (no) | 300001 -> 300001 | 0.42 | 0.47 |
| capsule | 1 / 0.35 | BudgetExceeded | 80 -> 61 -> 40 | 152 | BudgetExceeded (no) | 300001 -> 300001 | 0.53 | 1.43 |
| capsule | 1 / 0.7 | BudgetExceeded | 54 -> 19 -> 8 | 78 | FlippedFaces (no) | 300001 -> 13589 | 0.42 | 0.31 |
| genus2 | 1 / 0.5 | Success | 5 -> 1 -> 1 | 4 | Success (clean) | 216448 -> 824 | 0.43 | 0.61 |
| genus2 | 0.8 / 0.5 | Success | 3 -> 2 -> 0 | 15 | Success (clean) | 354 -> 0 | 0.35 | 0.88 |
| genus2 | 1.25 / 0.5 | Success | 16 -> 8 -> 7 | 20 | Success (clean) | 125627 -> 2616 | 0.36 | 0.76 |
| genus2 | 1 / 0.35 | Success | 22 -> 10 -> 4 | 46 | Success (clean) | 125633 -> 14373 | 0.54 | 1.11 |
| capped-cylinder | 1 / 0.5 | BudgetExceeded | 189 -> 157 -> 134 | 292 | BudgetExceeded (no) | 300001 -> 300001 | 0.46 | 0.85 |
| capped-cylinder | 0.8 / 0.5 | BudgetExceeded | 170 -> 56 -> 49 | 74 | BudgetExceeded (no) | 300001 -> 300001 | 1.55 | 4.39 |
| capped-cylinder | 1.25 / 0.5 | BudgetExceeded | 17 -> 5 -> 3 | 17 | Success (no) | 300001 -> 6920 | 0.40 | 0.24 |
| capped-cylinder | 1.5 / 0.5 | BudgetExceeded | 27 -> 14 -> 11 | 37 | BudgetExceeded (no) | 300001 -> 300001 | 0.32 | 0.52 |
| capped-cylinder | 1 / 0.35 | BudgetExceeded | 53 -> 50 -> 40 | 67 | BudgetExceeded (no) | 300001 -> 300001 | 0.60 | 1.55 |
| capped-cylinder | 1 / 0.7 | OrientationFolds | 8 -> 6 -> 8 | 23 | BudgetExceeded (no) | 161930 -> 300001 | 0.20 | 0.54 |

Clean: library 25 / 54 -> 28 / 54; newly clean 4, lost 1. Folds before SAT, sum: 2013 -> 1320 -> 1098.
Time (sum): library copy 21.9 s -> 46.6 s; fallback rows 17.9 -> 37.5 s; clean rows (library) 4.0 -> 9.2 s.
- sphere: 3 / 6 -> 4
- torus: 3 / 6 -> 4
- torus-align10: 6 / 6 -> 6
- bumpy: 1 / 6 -> 2
- filleted-box: 5 / 6 -> 5
- noisy-sphere: 1 / 6 -> 1
- capsule: 0 / 6 -> 0
- genus2: 6 / 6 -> 6
- capped-cylinder: 0 / 6 -> 0
