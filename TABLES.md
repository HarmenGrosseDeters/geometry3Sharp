| set | cell | runs | uncertified cells: blocks -> grown | certified runs: stage 1, blocks, grown | wrong results | wrong and certified | grown blocks (cells added) | contour ms | certificate ms: blocks -> grown | of it blocks ms: blocks -> grown |
|---|---|---|---|---|---|---|---|---|---|---|
| CAD solids | / 64 | 6 | 0 -> 0 | 5, 6, 6 | 0 | 0 | 0 (0) | 686-1113 | 241-254 -> 181-230 | 37-52 -> 24-37 |
| T1 clean | / 64 | 14 | 437 -> 301 | 2, 9, 12 | 2 | 0 | 7 (1482) | 2877-3049 | 744-852 -> 1005-1063 | 54-101 -> 235-281 |
| CAD solids | / 128 | 6 | 0 -> 0 | 6, 6, 6 | 0 | 0 | 0 (0) | 2706-2964 | 251-355 -> 267-356 | 0 -> 0 |
| T1 clean | / 128 | 14 | 651 -> 381 | 4, 8, 10 | 3 | 0 | 11 (1745) | 5264-5463 | 1208-1273 -> 1385-1448 | 65-150 -> 321-354 |

| set | cell | cells left in right runs, by reason | in wrong runs |
|---|---|---|---|
| CAD solids | / 64 | - | - |
| T1 clean | / 64 | - | transition 50, not a ball 220, level-set count 31 |
| CAD solids | / 128 | - | - |
| T1 clean | / 128 | level-set count 72 | not a ball 295, level-set count 10, contour count 4 |

| input | cell | result | uncertified cells: blocks -> grown | left by |
|---|---|---|---|---|
| 104640.stl | / 128 | right | 72 -> 72 | level-set count 72 |
| 116871.stl | / 64 | wrong | 265 -> 251 | not a ball 220, level-set count 31 |
| 116871.stl | / 128 | wrong | 404 -> 215 | not a ball 205, level-set count 10 |
| 472011.stl | / 128 | wrong | 90 -> 90 | not a ball 90 |
| 79961.stl | / 128 | wrong | 4 -> 4 | contour count 4 |
| 96773.stl | / 64 | wrong | 50 -> 50 | transition 50 |
