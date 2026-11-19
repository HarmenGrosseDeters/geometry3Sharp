# QuadFoldDiagnosis (total harness time 273 s, net10.0 Release)

## 0. The copy agrees with the library
status, inverted count, SAT decisions, splits and all offsets of the copy (library settings) equal the library's on 54 / 54 runs.
Clean (independent check of the gate) with the copy: 25; library status Success: 25.

## (a) Budget vs feasibility
SatTotalBudget x10 (3M decisions; SatMaxAttempts and the round limit x10, the per-attempt budget stays 100k) and x10 with the per-attempt budget x10 as well.

| shape | scale / ratio | tris | library status | folds after flow | folds before SAT | folds at end | x10 status (clean) | x10 s | decisions | SAT solved / failed | x10 + attempt x10 status (clean) | s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| sphere | 0.8 / 0.5 | 19936 | BudgetExceeded | 225 | 25 | 22 | OrientationFolds (no) | 1.56 | 1470582 | 10 / 13 | BudgetExceeded (no) | 2.96 |
| sphere | 1.25 / 0.5 | 8204 | BudgetExceeded | 103 | 17 | 13 | OrientationFolds (no) | 1.00 | 1098354 | 5 / 9 | BudgetExceeded (no) | 3.13 |
| sphere | 1 / 0.35 | 26016 | BudgetExceeded | 135 | 22 | 15 | OrientationFolds (no) | 1.80 | 1465189 | 10 / 9 | BudgetExceeded (no) | 3.84 |
| torus | 0.8 / 0.5 | 21964 | OrientationFolds | 135 | 2 | 1 | OrientationFolds (no) | 0.31 | 200182 | 1 / 1 | Success (clean) | 0.37 |
| torus | 1.25 / 0.5 | 8964 | BudgetExceeded | 71 | 11 | 10 | OrientationFolds (no) | 0.79 | 1020850 | 4 / 5 | BudgetExceeded (no) | 2.03 |
| torus | 1.5 / 0.5 | 6196 | OrientationFolds | 95 | 6 | 1 | OrientationFolds (no) | 0.17 | 180216 | 4 / 1 | Success (clean) | 0.24 |
| bumpy | 1 / 0.5 | 35832 | BudgetExceeded | 348 | 27 | 20 | OrientationFolds (no) | 1.40 | 997068 | 17 / 7 | BudgetExceeded (no) | 3.33 |
| bumpy | 0.8 / 0.5 | 55724 | BudgetExceeded | 765 | 50 | 48 | OrientationFolds (no) | 4.01 | 2885875 | 25 / 20 | BudgetExceeded (no) | 3.85 |
| bumpy | 1.25 / 0.5 | 22810 | BudgetExceeded | 370 | 22 | 14 | OrientationFolds (no) | 1.00 | 853044 | 9 / 13 | BudgetExceeded (no) | 3.04 |
| bumpy | 1 / 0.35 | 73048 | BudgetExceeded | 749 | 202 | 194 | BudgetExceeded (no) | 3.85 | 3000001 | 19 / 18 | BudgetExceeded (no) | 3.39 |
| bumpy | 1 / 0.7 | 18266 | BudgetExceeded | 295 | 14 | 5 | OrientationFolds (no) | 0.88 | 707327 | 7 / 5 | BudgetExceeded (no) | 3.39 |
| filleted-box | 0.8 / 0.5 | 28486 | BudgetExceeded | 63 | 8 | 8 | OrientationFolds (no) | 1.37 | 1310885 | 0 / 8 | BudgetExceeded (no) | 2.84 |
| noisy-sphere | 1 / 0.5 | 12776 | BudgetExceeded | 236 | 23 | 16 | OrientationFolds (no) | 2.46 | 2014062 | 11 / 9 | BudgetExceeded (no) | 2.84 |
| noisy-sphere | 0.8 / 0.5 | 19936 | BudgetExceeded | 165 | 60 | 49 | OrientationFolds (no) | 2.63 | 2872701 | 14 / 37 | BudgetExceeded (no) | 2.92 |
| noisy-sphere | 1.5 / 0.5 | 5706 | BudgetExceeded | 136 | 35 | 28 | OrientationFolds (no) | 1.16 | 1459584 | 12 / 20 | BudgetExceeded (no) | 2.62 |
| noisy-sphere | 1 / 0.35 | 26016 | BudgetExceeded | 263 | 276 | 271 | BudgetExceeded (no) | 3.91 | 3000001 | 22 / 31 | BudgetExceeded (no) | 4.42 |
| noisy-sphere | 1 / 0.7 | 6506 | BudgetExceeded | 201 | 154 | 154 | BudgetExceeded (no) | 2.41 | 3000001 | 11 / 47 | BudgetExceeded (no) | 2.94 |
| capsule | 1 / 0.5 | 11042 | BudgetExceeded | 326 | 132 | 126 | BudgetExceeded (no) | 3.99 | 3000001 | 8 / 26 | BudgetExceeded (no) | 3.54 |
| capsule | 0.8 / 0.5 | 17052 | BudgetExceeded | 502 | 124 | 124 | BudgetExceeded (no) | 3.87 | 3000001 | 6 / 33 | BudgetExceeded (no) | 3.30 |
| capsule | 1.25 / 0.5 | 6988 | BudgetExceeded | 315 | 67 | 65 | BudgetExceeded (no) | 3.07 | 3000001 | 13 / 18 | BudgetExceeded (no) | 3.23 |
| capsule | 1.5 / 0.5 | 4802 | BudgetExceeded | 130 | 27 | 23 | OrientationFolds (no) | 2.31 | 2419167 | 8 / 15 | BudgetExceeded (no) | 3.59 |
| capsule | 1 / 0.35 | 22404 | BudgetExceeded | 306 | 80 | 74 | BudgetExceeded (no) | 3.98 | 3000001 | 20 / 31 | BudgetExceeded (no) | 4.19 |
| capsule | 1 / 0.7 | 5576 | BudgetExceeded | 307 | 54 | 49 | BudgetExceeded (no) | 3.16 | 3000001 | 9 / 31 | BudgetExceeded (no) | 3.05 |
| capped-cylinder | 1 / 0.5 | 13012 | BudgetExceeded | 479 | 189 | 188 | BudgetExceeded (no) | 2.79 | 3000001 | 5 / 43 | BudgetExceeded (no) | 2.56 |
| capped-cylinder | 0.8 / 0.5 | 20342 | BudgetExceeded | 270 | 170 | 169 | BudgetExceeded (no) | 4.52 | 3000001 | 12 / 31 | BudgetExceeded (no) | 4.89 |
| capped-cylinder | 1.25 / 0.5 | 8290 | BudgetExceeded | 106 | 17 | 10 | OrientationFolds (no) | 1.21 | 1075428 | 6 / 8 | BudgetExceeded (no) | 3.46 |
| capped-cylinder | 1.5 / 0.5 | 5700 | BudgetExceeded | 167 | 27 | 27 | OrientationFolds (no) | 1.94 | 1967031 | 12 / 13 | BudgetExceeded (no) | 2.86 |
| capped-cylinder | 1 / 0.35 | 26606 | BudgetExceeded | 187 | 53 | 52 | BudgetExceeded (no) | 3.96 | 3000001 | 7 / 34 | BudgetExceeded (no) | 2.95 |
| capped-cylinder | 1 / 0.7 | 6576 | OrientationFolds | 241 | 8 | 2 | OrientationFolds (no) | 0.26 | 161930 | 5 / 2 | OrientationFolds (no) | 0.50 |

Fallbacks 29 (BudgetExceeded 26, OrientationFolds 3, other 0).
BudgetExceeded rows clean with x10: 0 / 26 (time of those: median NaN s, max 0.00 s); with the attempt budget x10 too: 0 / 26; either: 0.
BudgetExceeded rows: x10 total time 65.0 s (library 19.5 s incl. stage 2). x10 outcome: OrientationFolds 15, BudgetExceeded 11.
OrientationFolds rows clean with x10: 2 / 3.

clean per shape (library -> x10 either variant):
- sphere: 3 / 6 -> 3
- torus: 3 / 6 -> 5
- torus-align10: 6 / 6 -> 6
- bumpy: 1 / 6 -> 1
- filleted-box: 5 / 6 -> 5
- noisy-sphere: 1 / 6 -> 1
- capsule: 0 / 6 -> 0
- genus2: 6 / 6 -> 6
- capped-cylinder: 0 / 6 -> 0

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


How selective is the take-back? Over all folds: 21518 corrections, of which 53 % (11369) are individually implicating at edge level and 49 % (10612) (sum of corrected components: units counted per fold) at unit level. Folds by (corrections, implicating edge corrections): (0,0): 3, (1,0): 5, (1,1): 96, (2,0): 4, (2,1): 8919, (2,2): 143, (3,1): 445, (3,2): 327, (3,3): 323.

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
