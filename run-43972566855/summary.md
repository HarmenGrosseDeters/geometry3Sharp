# alpha-defaults-800: summary

Meshes with a row in all three configurations: **800 of 800**. Times are the benchmark's own (ms) unless marked wall (wall clock per process, from `wall.tsv`).

## Guarantees, timeouts and crashes

| config | rows | results | timeouts | crashes | closed | manifold | no self-intersection (exact) | valid | wrap not Success |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 800 | 800 | 0 | 0 | 800 | 800 | 800 | 800 | 0 |
| B (features) | 800 | 800 | 0 | 0 | 800 | 800 | 800 | 800 | 0 |
| C (budget 300k) | 800 | 800 | 0 | 0 | 800 | 800 | 800 | 800 | 0 |

A timeout is a row with `timeout_or_crash` and wall time at or above 400 s, a crash any other such row.

## B (features) against A

Meshes with a result in both: 800.

Guarantees lost against A (A had it, B (features) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 800 | 0.6689 | - | 0.1492-1.0974 | - | - | 6 |
| hausdorff_hi | 800 | 0.9188 | 2.1343 | 0.3764-2.1343 | 162 | 623 | 15 |
| p01 | 800 | 0.9712 | 0.7575 | 0.7575-1.0483 | 750 | 43 | 7 |
| min_angle | 800 | 1.0009 | 0.4525 | 0.4525-1.7064 | 321 | 430 | 49 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

Sharp-feature pass changed something (features not 0/0/0/0/0): 794 meshes.

## C (budget 300k) against A

Meshes with a result in both: 800.

Guarantees lost against A (A had it, C (budget 300k) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 800 | 1.0000 | - | 0.0565-1.0173 | - | - | 448 |
| hausdorff_hi | 800 | 1.0000 | 47.1462 | 0.5010-47.1462 | 307 | 45 | 448 |
| p01 | 800 | 1.0000 | 0.9797 | 0.9797-1.1226 | 86 | 265 | 449 |
| min_angle | 800 | 1.0000 | 0.3682 | 0.3682-2.2809 | 165 | 187 | 448 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

Budget active (coarsening > 1): 352 meshes, median factor 1.171, max 3.000.

## Time

| config | pipe_ms sum | pipe_ms median | repair_ms sum | repair_ms median | wrap_ms sum | wall sum (s) | wall median (s) |
|---|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 4412352 | 4646 | 5939151 | 6580 | 2129679 | 12555 | 13.36 |
| B (features) | 5851981 | 5840 | 7390609 | 7749 | 2128011 | 13687 | 14.30 |
| C (budget 300k) | 3445145 | 4290 | 4942385 | 6140 | 1593863 | 11277 | 12.23 |

B (features) pipe_ms sum / A: 1.326.

C (budget 300k) pipe_ms sum / A: 0.781.

## Meshes above 20 s

Listed when the benchmark's own repair time (`repair_ms`) or pipeline time (`pipe_ms`) is above 20 s, or the wall clock of the process is (wall includes start-up and the warm-up).

- A (defaults): 138: 37275.stl, 37788.stl, 38111.stl, 39247.stl, 39368.stl, 39498.stl, 40009.stl, 40085.stl, 40118.stl, 42179.stl, 43390.stl, 43766.stl, 44314.stl, 46519.stl, 46763.stl, 47878.stl, 48558.stl, 49742.stl, 49901.stl, 49998.stl, 51140.stl, 51705.stl, 51909.stl, 55928.stl, 60513.stl, 61198.stl, 64891.stl, 66112.stl, 68339.stl, 68743.stl, 69261.stl, 71920.stl, 71991.stl, 72087.stl, 72326.stl, 72780.stl, 77319.stl, 78968.stl, 82326.stl, 82380.stl, 84042.stl, 85758.stl, 90788.stl, 91280.stl, 91734.stl, 95763.stl, 96772.stl, 97674.stl, 97859.stl, 100139.stl, 104559.stl, 105171.stl, 106327.stl, 108785.stl, 111959.stl, 112825.stl, 112929.stl, 114437.stl, 119238.stl, 124185.stl, 135071.stl, 135220.stl, 139429.stl, 162285.stl, 170179.stl, 175914.stl, 196124.stl, 213388.stl, 222687.stl, 222695.stl, 224108.stl, 233335.stl, 237624.stl, 239183.stl, 248188.stl, 255241.stl, 270401.stl, 277710.stl, 318843.stl, 331803.stl, 366249.stl, 439140.stl, 441719.stl, 464847.stl, 466329.stl, 472011.stl, 472012.stl, 472092.stl, 472117.stl, 498456.stl, 500097.stl, 500099.stl, 500115.stl, 509315.stl, 511803.stl, 520646.stl, 535629.stl, 548877.stl, 565652.stl, 591242.stl, 591250.stl, 636794.stl, 649538.stl, 668421.stl, 677340.stl, 677342.stl, 679615.stl, 693472.stl, 702390.stl, 702425.stl, 780000.stl, 860049.stl, 1053378.stl, 1063853.stl, 1083348.stl, 1083350.stl, 1085685.stl, 1085686.stl, 1179045.stl, 1228190.stl, 1288650.stl, 1312954.stl, 1368068.stl, 1368069.stl, 1396906.stl, 1432740.stl, 1458699.stl, 1473393.stl, 1482988.stl, 1505033.stl, 1514502.stl, 1514905.stl, 1582379.stl, 1582438.stl, 1591710.stl, 1601406.stl, 1620057.stl, 1620064.stl
- B (features): 177: 37275.stl, 37788.stl, 38111.stl, 39247.stl, 39368.stl, 39498.stl, 40009.stl, 40085.stl, 40118.stl, 42179.stl, 42330.stl, 43390.stl, 43766.stl, 44314.stl, 44398.stl, 45813.stl, 46519.stl, 46763.stl, 47878.stl, 48367.stl, 48558.stl, 49742.stl, 49901.stl, 49998.stl, 51140.stl, 51350.stl, 51705.stl, 51909.stl, 55928.stl, 58111.stl, 60513.stl, 60568.stl, 61198.stl, 64891.stl, 66112.stl, 68339.stl, 68743.stl, 69259.stl, 69261.stl, 70044.stl, 70064.stl, 70563.stl, 71920.stl, 71991.stl, 72087.stl, 72326.stl, 72780.stl, 76051.stl, 76198.stl, 77319.stl, 78226.stl, 78968.stl, 82326.stl, 82380.stl, 84042.stl, 85758.stl, 90207.stl, 90286.stl, 90788.stl, 90794.stl, 91280.stl, 91684.stl, 91734.stl, 94240.stl, 94900.stl, 95763.stl, 97674.stl, 97859.stl, 100139.stl, 102028.stl, 104559.stl, 105171.stl, 106327.stl, 108785.stl, 109576.stl, 111959.stl, 112537.stl, 112825.stl, 112929.stl, 114437.stl, 116294.stl, 119238.stl, 124185.stl, 125892.stl, 129907.stl, 135071.stl, 135220.stl, 139429.stl, 162285.stl, 162898.stl, 170179.stl, 175914.stl, 196124.stl, 203282.stl, 213388.stl, 213888.stl, 222687.stl, 222695.stl, 224108.stl, 233335.stl, 237624.stl, 239183.stl, 248188.stl, 255241.stl, 270322.stl, 270401.stl, 277710.stl, 318843.stl, 331803.stl, 356074.stl, 366249.stl, 424211.stl, 439140.stl, 441719.stl, 442386.stl, 464847.stl, 466329.stl, 472011.stl, 472012.stl, 472092.stl, 472117.stl, 472134.stl, 498456.stl, 500097.stl, 500099.stl, 500115.stl, 509315.stl, 511803.stl, 520646.stl, 535629.stl, 548877.stl, 565652.stl, 591242.stl, 591250.stl, 636794.stl, 649538.stl, 668421.stl, 677340.stl, 677342.stl, 679615.stl, 693472.stl, 702390.stl, 702425.stl, 779998.stl, 780000.stl, 860049.stl, 1005289.stl, 1043436.stl, 1053378.stl, 1063853.stl, 1083348.stl, 1083350.stl, 1085685.stl, 1085686.stl, 1179045.stl, 1228190.stl, 1288650.stl, 1312954.stl, 1368067.stl, 1368068.stl, 1368069.stl, 1368076.stl, 1396906.stl, 1432740.stl, 1454017.stl, 1458699.stl, 1473393.stl, 1482988.stl, 1505033.stl, 1514502.stl, 1514905.stl, 1582379.stl, 1582438.stl, 1591710.stl, 1601406.stl, 1620057.stl, 1620064.stl
- C (budget 300k): 90: 37275.stl, 37788.stl, 38111.stl, 39368.stl, 39498.stl, 40009.stl, 42179.stl, 43766.stl, 46763.stl, 47878.stl, 49998.stl, 55928.stl, 61198.stl, 64891.stl, 66112.stl, 68339.stl, 69261.stl, 70044.stl, 71920.stl, 71991.stl, 72087.stl, 72780.stl, 77319.stl, 82326.stl, 85758.stl, 90788.stl, 91280.stl, 96772.stl, 97674.stl, 97859.stl, 100139.stl, 104559.stl, 111959.stl, 112537.stl, 112825.stl, 112929.stl, 114437.stl, 135071.stl, 135220.stl, 170179.stl, 196124.stl, 222687.stl, 222695.stl, 224108.stl, 233335.stl, 237624.stl, 239183.stl, 248188.stl, 255241.stl, 270401.stl, 277710.stl, 318843.stl, 331803.stl, 366249.stl, 441719.stl, 466329.stl, 472011.stl, 472012.stl, 472092.stl, 472117.stl, 498456.stl, 500097.stl, 500115.stl, 511803.stl, 548877.stl, 591242.stl, 649538.stl, 677342.stl, 702390.stl, 860049.stl, 1063853.stl, 1083348.stl, 1083350.stl, 1085685.stl, 1085686.stl, 1179045.stl, 1228190.stl, 1288650.stl, 1368068.stl, 1368069.stl, 1396906.stl, 1432740.stl, 1473393.stl, 1482988.stl, 1505033.stl, 1514502.stl, 1514905.stl, 1582438.stl, 1591710.stl, 1620057.stl

