# alpha-defaults-800: summary

Meshes with a row in all 4 configurations (A, C, D, E): **49 of 49**. Times are the benchmark's own (ms) unless marked wall (wall clock per process, from `wall.tsv`).

## Guarantees, timeouts and crashes

| config | rows | results | timeouts | crashes | closed | manifold | no self-intersection (exact) | valid | wrap not Success |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 49 | 49 | 0 | 0 | 49 | 49 | 49 | 49 | 0 |
| C (budget 300k) | 49 | 49 | 0 | 0 | 49 | 49 | 49 | 49 | 0 |
| D (budget 300k, coarsening <= 1.5) | 49 | 49 | 0 | 0 | 49 | 49 | 49 | 49 | 0 |
| E (budget 300k, coarsening <= 1.2) | 49 | 49 | 0 | 0 | 49 | 49 | 49 | 49 | 0 |

A timeout is a row with `timeout_or_crash` and wall time at or above 400 s, a crash any other such row.

## C (budget 300k) against A

Meshes with a result in both: 49.

Guarantees lost against A (A had it, C (budget 300k) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 49 | 0.5156 | - | 0.0565-0.9463 | - | - | 0 |
| hausdorff_hi | 49 | 1.3822 | 47.1462 | 1.0015-47.1462 | 49 | 0 | 0 |
| p01 | 49 | 1.0110 | 0.9869 | 0.9869-1.1226 | 10 | 39 | 0 |
| min_angle | 49 | 1.0024 | 0.3682 | 0.3682-2.0809 | 21 | 28 | 0 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

Budget active (coarsening > 1): 49 meshes, median factor 1.597, max 3.000.

## D (budget 300k, coarsening <= 1.5) against A

Meshes with a result in both: 49.

Guarantees lost against A (A had it, D (budget 300k, coarsening <= 1.5) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 49 | 0.6059 | - | 0.3434-0.9463 | - | - | 0 |
| hausdorff_hi | 49 | 1.2612 | 39.2115 | 1.0003-39.2115 | 49 | 0 | 0 |
| p01 | 49 | 1.0109 | 0.9904 | 0.9904-1.0993 | 10 | 39 | 0 |
| min_angle | 49 | 1.0228 | 0.3682 | 0.3682-1.8328 | 20 | 29 | 0 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

Budget active (coarsening > 1): 49 meshes, median factor 1.500, max 1.500.

## E (budget 300k, coarsening <= 1.2) against A

Meshes with a result in both: 49.

Guarantees lost against A (A had it, E (budget 300k, coarsening <= 1.2) does not): closed 0, manifold 0, no_self_int 0, valid 0.

| metric | n | median ratio | worst ratio | range (min-max) | worse | better | identical |
|---|---:|---:|---:|---|---:|---:|---:|
| triangles | 49 | 0.8025 | - | 0.5769-1.0229 | - | - | 0 |
| hausdorff_hi | 49 | 1.1132 | 2.6675 | 0.9957-2.6675 | 47 | 2 | 0 |
| p01 | 49 | 1.0061 | 0.7765 | 0.7765-1.1324 | 12 | 37 | 0 |
| min_angle | 49 | 1.0153 | 0.1724 | 0.1724-2.6985 | 20 | 29 | 0 |

Worse / better: Hausdorff bound (upper, relative to the diagonal) lower is better; p01 and min angle higher is better; triangles have no direction (their row counts only identical). Ratios are candidate / A (worst = largest Hausdorff ratio, smallest angle ratio).

Budget active (coarsening > 1): 49 meshes, median factor 1.200, max 1.200.

## Time

| config | pipe_ms sum | pipe_ms median | repair_ms sum | repair_ms median | wrap_ms sum | wall sum (s) | wall median (s) |
|---|---:|---:|---:|---:|---:|---:|---:|
| A (defaults) | 662722 | 10006 | 756906 | 12120 | 357470 | 1338 | 21.56 |
| C (budget 300k) | 223190 | 4344 | 304627 | 5786 | 109143 | 772 | 13.34 |
| D (budget 300k, coarsening <= 1.5) | 304627 | 5173 | 388123 | 6932 | 159294 | 878 | 15.45 |
| E (budget 300k, coarsening <= 1.2) | 462136 | 6870 | 549042 | 8685 | 250290 | 1083 | 16.95 |

C (budget 300k) pipe_ms sum / A: 0.337.

D (budget 300k, coarsening <= 1.5) pipe_ms sum / A: 0.460.

E (budget 300k, coarsening <= 1.2) pipe_ms sum / A: 0.697.

## Meshes above 20 s

Listed when the benchmark's own repair time (`repair_ms`) or pipeline time (`pipe_ms`) is above 20 s, or the wall clock of the process is (wall includes start-up and the warm-up).

- A (defaults): 30: 37275.stl, 39368.stl, 40118.stl, 42330.stl, 44314.stl, 49901.stl, 49998.stl, 51909.stl, 66112.stl, 69261.stl, 78968.stl, 84042.stl, 91280.stl, 97859.stl, 106327.stl, 139429.stl, 162285.stl, 175914.stl, 239183.stl, 442386.stl, 466329.stl, 500097.stl, 500099.stl, 500115.stl, 520646.stl, 535629.stl, 679615.stl, 860049.stl, 131090.stl, 677342.stl
- C (budget 300k): 11: 39368.stl, 49998.stl, 69261.stl, 91280.stl, 97859.stl, 106327.stl, 239183.stl, 466329.stl, 500097.stl, 500115.stl, 860049.stl
- D (budget 300k, coarsening <= 1.5): 13: 39368.stl, 40118.stl, 49998.stl, 69261.stl, 91280.stl, 97859.stl, 106327.stl, 239183.stl, 466329.stl, 500097.stl, 500115.stl, 535629.stl, 860049.stl
- E (budget 300k, coarsening <= 1.2): 18: 39368.stl, 40118.stl, 44314.stl, 49998.stl, 51909.stl, 69261.stl, 78968.stl, 91280.stl, 97859.stl, 106327.stl, 175914.stl, 239183.stl, 466329.stl, 500097.stl, 500115.stl, 520646.stl, 535629.stl, 860049.stl

