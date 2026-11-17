# alpha-defaults-800: summary

Meshes with a row in all 2 configurations (A, F): **800 of 800**. Times are the benchmark's own (ms) unless marked wall (wall clock per process, from `wall.tsv`).

## Guarantees, timeouts and crashes

| config | rows | results | timeouts | crashes | closed | manifold | no self-intersection (exact) | valid | wrap not Success |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 800 | 800 | 0 | 0 | 800 | 800 | 800 | 800 | 0 |
| F (PullPinchPoints) | 800 | 800 | 0 | 0 | 800 | 800 | 800 | 800 | 0 |

A timeout is a row with `timeout_or_crash` and wall time at or above 400 s, a crash any other such row.

## F (PullPinchPoints) against A

Meshes with a result in both: 800.

Guarantees lost against A (A had it, F (PullPinchPoints) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 800 | 1.0000 | - | 0.9821-1.0223 | - | - | 602 |
| hausdorff_hi | 800 | 1.0000 | 1.2905 | 0.8234-1.2905 | 89 | 69 | 642 |
| p01 | 800 | 1.0000 | 0.9776 | 0.9776-1.0089 | 108 | 92 | 600 |
| min_angle | 800 | 1.0000 | 0.4627 | 0.4627-3.4588 | 55 | 62 | 683 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

## Raw wrap: the derived bounds of the class doc

Meshes with the raw-wrap metrics in every configuration: 800. Below bound = triangles with a min angle below asin(min(offset, alpha - offset) / 2 alpha); separation = the smallest distance between two vertices of the triangulation; vertex distances to the input (AABB tree), all relative to the offset. Measured on MeshAlphaWrap's result, before the sharp-feature pass of B.

| config | meshes with a triangle below the bound | triangles below | min angle (min) | max angle (max) | vertex dist max (max) | vertex dist min (min) | separation (min) | raw closed + no bowties | manifold flips (sum) | raw triangles (sum) | wrap_ms (sum) | pipe_ms (sum) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 13 | 24 | 1.009 | 171.68 | 11.347 | 1.0000 | 1.0002 | 800 | 2693 | 120915668 | 2106666 | 4340086 |
| F (PullPinchPoints) | 2 | 2 | 2.766 | 170.16 | 11.347 | 1.0000 | 1.0004 | 800 | 386 | 120914474 | 2090194 | 4329125 |

### F (PullPinchPoints) against A, per mesh (15 meshes differ in the count below the bound or in clean; fewer below: 13)

All meshes: `raw_bounds_per_mesh.tsv` next to this summary.

| mesh | below A / F | min angle A / F | vertex dist max A / F | separation A / F | clean A / F | raw tris A / F | wrap_ms A / F |
|---|---|---|---|---|---|---|---|
| 39368.stl | 0 / 1 | 3.140 / 2.766 | 1.000 / 2.000 | 1.049 / 1.110 | True / True | 339534 / 339528 | 10824 / 10618 |
| 42179.stl | 1 / 0 | 2.634 / 3.257 | 1.000 / 1.998 | 1.004 / 1.013 | True / True | 235586 / 235524 | 4775 / 4610 |
| 51705.stl | 4 / 0 | 1.863 / 3.530 | 1.000 / 1.982 | 1.086 / 1.015 | True / True | 228864 / 228786 | 6877 / 6046 |
| 64891.stl | 2 / 0 | 2.306 / 3.373 | 1.000 / 1.964 | 1.028 / 1.004 | True / True | 93396 / 93410 | 2848 / 2827 |
| 70046.stl | 1 / 0 | 2.483 / 5.988 | 1.000 / 1.837 | 1.340 / 1.280 | True / True | 100684 / 100688 | 1944 / 1868 |
| 70563.stl | 6 / 0 | 2.676 / 4.676 | 1.000 / 1.871 | 1.000 / 1.189 | True / True | 157184 / 157190 | 3701 / 3766 |
| 102793.stl | 2 / 0 | 2.112 / 4.082 | 1.000 / 1.990 | 1.044 / 1.044 | True / True | 65752 / 65782 | 1582 / 1586 |
| 111959.stl | 1 / 0 | 2.692 / 3.708 | 11.347 / 11.347 | 1.004 / 1.044 | True / True | 414388 / 414904 | 10740 / 10653 |
| 117004.stl | 2 / 0 | 2.245 / 3.522 | 1.000 / 1.992 | 1.075 / 1.084 | True / True | 127048 / 127110 | 2338 / 2616 |
| 175914.stl | 1 / 0 | 1.180 / 3.324 | 1.000 / 1.994 | 1.020 / 1.019 | True / True | 320986 / 321176 | 9039 / 9679 |
| 318843.stl | 1 / 0 | 2.139 / 3.586 | 1.000 / 1.997 | 1.132 / 1.006 | True / True | 90062 / 90098 | 2117 / 2079 |
| 498456.stl | 1 / 0 | 2.841 / 3.225 | 1.000 / 1.992 | 1.001 / 1.007 | True / True | 88086 / 87964 | 2971 / 2753 |
| 860049.stl | 0 / 1 | 2.948 / 2.773 | 1.000 / 1.996 | 1.030 / 1.008 | True / True | 841346 / 841584 | 16788 / 17369 |
| 1514502.stl | 1 / 0 | 2.280 / 3.618 | 1.000 / 1.997 | 1.033 / 1.018 | True / True | 123228 / 123326 | 3423 / 3216 |
| 1681122.stl | 1 / 0 | 1.009 / 3.490 | 1.000 / 1.998 | 1.015 / 1.006 | True / True | 97362 / 97452 | 1691 / 1811 |

## Time

| config | pipe_ms sum | pipe_ms median | repair_ms sum | repair_ms median | wrap_ms sum | wall sum (s) | wall median (s) |
|---|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 4340086 | 4606 | 6062657 | 6714 | 2106666 | 12167 | 13.12 |
| F (PullPinchPoints) | 4329125 | 4607 | 6056875 | 6673 | 2090194 | 12149 | 13.11 |

F (PullPinchPoints) pipe_ms sum / A: 0.997.

## Meshes above 20 s

Listed when the benchmark's own repair time (`repair_ms`) or pipeline time (`pipe_ms`) is above 20 s, or the wall clock of the process is (wall includes start-up and the warm-up).

- A (defaults): 125: 37111.stl, 37275.stl, 37788.stl, 38111.stl, 39247.stl, 39368.stl, 39498.stl, 40009.stl, 40118.stl, 40923.stl, 42179.stl, 42823.stl, 44314.stl, 47732.stl, 47878.stl, 48558.stl, 48559.stl, 49742.stl, 49901.stl, 49998.stl, 50309.stl, 51350.stl, 51352.stl, 51705.stl, 51909.stl, 55928.stl, 60513.stl, 61198.stl, 66112.stl, 66129.stl, 67989.stl, 68339.stl, 69261.stl, 71920.stl, 71991.stl, 72780.stl, 76198.stl, 77319.stl, 78968.stl, 79937.stl, 84042.stl, 91280.stl, 91684.stl, 91734.stl, 94900.stl, 95763.stl, 97674.stl, 97859.stl, 100728.stl, 102028.stl, 105171.stl, 106327.stl, 111959.stl, 112537.stl, 112825.stl, 114437.stl, 119238.stl, 130049.stl, 135071.stl, 135220.stl, 139429.stl, 162285.stl, 175914.stl, 196124.stl, 213388.stl, 222687.stl, 222695.stl, 224108.stl, 233335.stl, 239183.stl, 248188.stl, 255241.stl, 270322.stl, 270401.stl, 318843.stl, 331803.stl, 366249.stl, 439140.stl, 441719.stl, 462523.stl, 466329.stl, 472008.stl, 472012.stl, 472092.stl, 498456.stl, 500097.stl, 500099.stl, 500115.stl, 520646.stl, 535629.stl, 548877.stl, 565652.stl, 591242.stl, 591243.stl, 636794.stl, 668421.stl, 669965.stl, 677340.stl, 677342.stl, 679615.stl, 702390.stl, 702425.stl, 779998.stl, 860049.stl, 1013015.stl, 1038438.stl, 1043436.stl, 1053378.stl, 1063853.stl, 1083348.stl, 1083350.stl, 1085685.stl, 1085686.stl, 1179045.stl, 1312964.stl, 1368067.stl, 1368068.stl, 1368069.stl, 1417959.stl, 1432740.stl, 1473393.stl, 1496712.stl, 1514502.stl, 1514905.stl, 1591710.stl
- F (PullPinchPoints): 124: 37275.stl, 37788.stl, 39247.stl, 39368.stl, 39498.stl, 40009.stl, 40118.stl, 40923.stl, 42179.stl, 42823.stl, 44314.stl, 47732.stl, 47878.stl, 48558.stl, 48559.stl, 49742.stl, 49901.stl, 49998.stl, 50309.stl, 51350.stl, 51705.stl, 51909.stl, 55928.stl, 60513.stl, 61198.stl, 66112.stl, 66129.stl, 67989.stl, 68339.stl, 69261.stl, 71920.stl, 71991.stl, 72780.stl, 76198.stl, 77319.stl, 78968.stl, 79937.stl, 84042.stl, 90794.stl, 91280.stl, 91684.stl, 91734.stl, 94900.stl, 95763.stl, 97674.stl, 97859.stl, 100728.stl, 102028.stl, 105171.stl, 106327.stl, 111959.stl, 112537.stl, 112825.stl, 114437.stl, 119238.stl, 130049.stl, 135071.stl, 135220.stl, 139429.stl, 162285.stl, 175914.stl, 196124.stl, 213388.stl, 222687.stl, 222695.stl, 224108.stl, 233335.stl, 239183.stl, 248188.stl, 255241.stl, 270322.stl, 270401.stl, 318843.stl, 331803.stl, 366249.stl, 439140.stl, 441719.stl, 462523.stl, 466329.stl, 472008.stl, 472012.stl, 472092.stl, 498456.stl, 500097.stl, 500099.stl, 500115.stl, 520646.stl, 535629.stl, 548877.stl, 565652.stl, 591242.stl, 591243.stl, 636794.stl, 668421.stl, 669965.stl, 677340.stl, 677342.stl, 679615.stl, 702390.stl, 702394.stl, 702425.stl, 779998.stl, 860049.stl, 1013015.stl, 1038438.stl, 1043436.stl, 1053378.stl, 1063853.stl, 1083348.stl, 1083350.stl, 1085685.stl, 1085686.stl, 1179045.stl, 1312964.stl, 1368067.stl, 1368068.stl, 1368069.stl, 1417959.stl, 1432740.stl, 1473393.stl, 1496712.stl, 1514502.stl, 1514905.stl, 1591710.stl

