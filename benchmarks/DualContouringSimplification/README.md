# DualContouringSimplification

Triangle count against error and time: uniform dual contouring (`AdaptiveDualContouring` with `Tolerance` 0, which is the
uniform contour with the default exact Hermite data) against the simplified contour at tolerances 0.1, 0.25 and 0.5 finest cells.
Design: [docs/design/DC_ADAPTIVE_SIMPLIFICATION.md](../../docs/design/DC_ADAPTIVE_SIMPLIFICATION.md).

```
dotnet run --project benchmarks/DualContouringSimplification -c Release -- [--res 32,64,128,256] [--shapes csg,bracket,sphere,torus] [--tol 0.1,0.25,0.5] [--ref 320]
```

Shapes (negative inside, composed with min / max): `csg` box half extent 1 intersected with a sphere of radius 1.35, minus
three axis cylinders of radius 0.55; `bracket` an L bracket of two plates with a flat-ended boss, a through hole, a slot and a
chamfered corner; `sphere` radius 1; `torus` radii 1 and 0.35. N is the number of finest cells across the cube that holds the
shape (the finest cell is 2 e / N).

Columns. `vs uniform`: triangles of the uniform contour divided by this row's. `mean err`, `max err`: distance from dense points on
the true surface (the vertices of a bisection MarchingCubes contour with 320 cells across the cube) to the mesh, in finest cells
(the uniform contour's own error is in the `tolerance` 0 rows; the finest grid's resolution is why it is not 0 on sharp features).
`mean abs f`, `max abs f`: |f| at the mesh vertices, in finest cells. `ms total`: Generate, best of two for N up to 128
(one run at 256), 4 cores; `ms simplify`: its part for the merge and the check. `clusters`: merged clusters by block level
(2, 4, 8, ... cells wide). `rounds / rejected`: rounds of the topology and intersection check and the clusters it took back.
Every result of the table is closed, manifold, without degenerate triangles and without self-intersections
(`MeshRobustRepair.NotCleanReason` is empty; a row would end with `NOT CLEAN`).

## Result (measured with the command above, 4 cores)

| shape | N | tolerance | triangles | vs uniform | mean err | max err | mean abs f | max abs f | ms total | ms simplify | clusters (by level) | rounds / rejected |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| csg | 32 | 0 | 6768 | 1.00x | 0.006 | 0.057 | 0.012 | 0.048 | 82 | 0 | - | 0 / 0 |
| csg | 32 | 0.1 | 1824 | 3.71x | 0.012 | 0.067 | 0.029 | 0.059 | 80 | 45 | 816/8 | 1 / 0 |
| csg | 32 | 0.25 | 1584 | 4.27x | 0.020 | 0.218 | 0.028 | 0.193 | 99 | 35 | 672/32 | 1 / 0 |
| csg | 32 | 0.5 | 816 | 8.29x | 0.066 | 0.309 | 0.053 | 0.394 | 44 | 23 | 240/104 | 1 / 0 |
| csg | 64 | 0 | 28944 | 1.00x | 0.003 | 0.018 | 0.005 | 0.017 | 57 | 0 | - | 0 / 0 |
| csg | 64 | 0.1 | 5616 | 5.15x | 0.013 | 0.095 | 0.017 | 0.089 | 151 | 54 | 752/656 | 3 / 248 |
| csg | 64 | 0.25 | 1824 | 15.87x | 0.025 | 0.125 | 0.046 | 0.117 | 120 | 17 | 80/816/8 | 1 / 0 |
| csg | 64 | 0.5 | 1824 | 15.87x | 0.025 | 0.125 | 0.046 | 0.117 | 115 | 102 | 80/816/8 | 1 / 0 |
| csg | 128 | 0 | 114000 | 1.00x | 0.003 | 0.031 | 0.003 | 0.079 | 338 | 0 | - | 0 / 0 |
| csg | 128 | 0.1 | 9468 | 12.04x | 0.011 | 0.059 | 0.017 | 0.073 | 493 | 184 | 1596/2168/224 | 3 / 404 |
| csg | 128 | 0.25 | 6300 | 18.10x | 0.026 | 0.230 | 0.023 | 0.135 | 547 | 165 | 972/944/584 | 3 / 404 |
| csg | 128 | 0.5 | 3546 | 32.15x | 0.042 | 0.340 | 0.040 | 0.187 | 542 | 109 | 360/444/769 | 3 / 159 |
| csg | 256 | 0 | 461232 | 1.00x | 0.005 | 0.012 | 0.001 | 0.013 | 1721 | 0 | - | 0 / 0 |
| csg | 256 | 0.1 | 28320 | 16.29x | 0.009 | 0.065 | 0.010 | 0.068 | 2648 | 587 | 2153/7871/1080/144 | 5 / 1035 |
| csg | 256 | 0.25 | 14784 | 31.20x | 0.026 | 0.209 | 0.018 | 0.134 | 2461 | 540 | 1562/1518/2096/240 | 5 / 846 |
| csg | 256 | 0.5 | 15264 | 30.22x | 0.036 | 0.260 | 0.017 | 0.252 | 2438 | 504 | 1674/1470/1592/368 | 5 / 1190 |
| bracket | 32 | 0 | 5012 | 1.00x | 0.003 | 0.172 | 0.005 | 0.108 | 9 | 0 | - | 0 / 0 |
| bracket | 32 | 0.1 | 1014 | 4.94x | 0.004 | 0.172 | 0.021 | 0.116 | 17 | 10 | 118/87/5 | 2 / 20 |
| bracket | 32 | 0.25 | 794 | 6.31x | 0.006 | 0.200 | 0.020 | 0.216 | 32 | 15 | 121/95/5 | 2 / 18 |
| bracket | 32 | 0.5 | 504 | 9.94x | 0.014 | 0.482 | 0.019 | 0.352 | 18 | 7 | 111/101/6 | 2 / 6 |
| bracket | 64 | 0 | 18974 | 1.00x | 0.002 | 0.259 | 0.003 | 0.059 | 31 | 0 | - | 0 / 0 |
| bracket | 64 | 0.1 | 1536 | 12.35x | 0.006 | 0.280 | 0.025 | 0.100 | 50 | 16 | 191/119/88/5 | 3 / 20 |
| bracket | 64 | 0.25 | 1022 | 18.57x | 0.007 | 0.280 | 0.031 | 0.222 | 47 | 15 | 222/123/89/5 | 3 / 20 |
| bracket | 64 | 0.5 | 776 | 24.45x | 0.012 | 0.410 | 0.030 | 0.341 | 91 | 57 | 111/133/95/5 | 2 / 17 |
| bracket | 128 | 0 | 75488 | 1.00x | 0.003 | 0.027 | 0.002 | 0.035 | 207 | 0 | - | 0 / 0 |
| bracket | 128 | 0.1 | 2660 | 28.38x | 0.006 | 0.097 | 0.023 | 0.099 | 367 | 133 | 723/130/181/74/5 | 4 / 63 |
| bracket | 128 | 0.25 | 1718 | 43.94x | 0.012 | 0.163 | 0.029 | 0.176 | 382 | 139 | 252/211/118/88/5 | 5 / 78 |
| bracket | 128 | 0.5 | 1192 | 63.33x | 0.015 | 0.318 | 0.036 | 0.350 | 312 | 51 | 70/205/126/89/5 | 4 / 46 |
| bracket | 256 | 0 | 301758 | 1.00x | 0.004 | 0.027 | 0.001 | 0.035 | 1308 | 0 | - | 0 / 0 |
| bracket | 256 | 0.1 | 6162 | 48.97x | 0.008 | 0.085 | 0.020 | 0.088 | 1756 | 391 | 1673/717/240/125/74/5 | 5 / 83 |
| bracket | 256 | 0.25 | 2660 | 113.44x | 0.014 | 0.175 | 0.033 | 0.165 | 1732 | 359 | 92/712/140/181/74/5 | 5 / 64 |
| bracket | 256 | 0.5 | 1706 | 176.88x | 0.026 | 0.364 | 0.044 | 0.320 | 1835 | 423 | 50/221/221/119/88/5 | 5 / 75 |
| sphere | 32 | 0 | 7980 | 1.00x | 0.005 | 0.021 | 0.013 | 0.022 | 22 | 0 | - | 0 / 0 |
| sphere | 32 | 0.1 | 1932 | 4.13x | 0.011 | 0.050 | 0.035 | 0.053 | 32 | 10 | 824/48 | 1 / 0 |
| sphere | 32 | 0.25 | 540 | 14.78x | 0.046 | 0.180 | 0.090 | 0.169 | 38 | 7 | 56/216 | 1 / 0 |
| sphere | 32 | 0.5 | 540 | 14.78x | 0.046 | 0.180 | 0.090 | 0.169 | 19 | 5 | 56/216 | 1 / 0 |
| sphere | 64 | 0 | 31932 | 1.00x | 0.003 | 0.014 | 0.007 | 0.012 | 93 | 0 | - | 0 / 0 |
| sphere | 64 | 0.1 | 3948 | 8.09x | 0.017 | 0.080 | 0.029 | 0.078 | 95 | 29 | 1128/632/48 | 1 / 0 |
| sphere | 64 | 0.25 | 1932 | 16.53x | 0.024 | 0.080 | 0.052 | 0.080 | 127 | 19 | 72/824/48 | 1 / 0 |
| sphere | 64 | 0.5 | 540 | 59.13x | 0.094 | 0.356 | 0.152 | 0.300 | 130 | 17 | 0/56/216 | 1 / 0 |
| sphere | 128 | 0 | 127740 | 1.00x | 0.003 | 0.013 | 0.003 | 0.016 | 468 | 0 | - | 0 / 0 |
| sphere | 128 | 0.1 | 7596 | 16.82x | 0.013 | 0.073 | 0.026 | 0.052 | 582 | 171 | 240/3216/128 | 1 / 0 |
| sphere | 128 | 0.25 | 2508 | 50.93x | 0.053 | 0.182 | 0.069 | 0.135 | 500 | 132 | 24/360/776/48 | 1 / 0 |
| sphere | 128 | 0.5 | 1932 | 66.12x | 0.056 | 0.182 | 0.088 | 0.142 | 575 | 66 | 24/72/824/48 | 1 / 0 |
| sphere | 256 | 0 | 510636 | 1.00x | 0.004 | 0.018 | 0.002 | 0.015 | 1894 | 0 | - | 0 / 0 |
| sphere | 256 | 0.1 | 19260 | 26.51x | 0.017 | 0.098 | 0.019 | 0.069 | 2594 | 479 | 552/6048/2360 | 1 / 0 |
| sphere | 256 | 0.25 | 7548 | 67.65x | 0.029 | 0.150 | 0.045 | 0.116 | 2497 | 557 | 96/240/3168/152 | 1 / 0 |
| sphere | 256 | 0.5 | 2796 | 182.63x | 0.111 | 0.401 | 0.111 | 0.250 | 2434 | 454 | 0/72/480/752/48 | 1 / 0 |
| torus | 32 | 0 | 4432 | 1.00x | 0.013 | 0.051 | 0.025 | 0.057 | 8 | 0 | - | 0 / 0 |
| torus | 32 | 0.1 | 3824 | 1.16x | 0.015 | 0.088 | 0.027 | 0.096 | 16 | 7 | 144 | 1 / 0 |
| torus | 32 | 0.25 | 1280 | 3.46x | 0.033 | 0.142 | 0.059 | 0.115 | 11 | 4 | 432 | 1 / 0 |
| torus | 32 | 0.5 | 768 | 5.77x | 0.058 | 0.414 | 0.087 | 0.238 | 11 | 3 | 328/32 | 1 / 0 |
| torus | 64 | 0 | 18208 | 1.00x | 0.006 | 0.029 | 0.012 | 0.032 | 56 | 0 | - | 0 / 0 |
| torus | 64 | 0.1 | 7632 | 2.39x | 0.014 | 0.097 | 0.021 | 0.068 | 52 | 23 | 1552/40 | 1 / 0 |
| torus | 64 | 0.25 | 3216 | 5.66x | 0.034 | 0.240 | 0.042 | 0.177 | 43 | 14 | 1256/224 | 1 / 0 |
| torus | 64 | 0.5 | 1328 | 13.71x | 0.074 | 0.405 | 0.084 | 0.185 | 47 | 12 | 216/424 | 1 / 0 |
| torus | 128 | 0 | 71616 | 1.00x | 0.004 | 0.017 | 0.006 | 0.017 | 227 | 0 | - | 0 / 0 |
| torus | 128 | 0.1 | 16464 | 4.35x | 0.010 | 0.093 | 0.017 | 0.086 | 345 | 91 | 6552/552 | 2 / 96 |
| torus | 128 | 0.25 | 4896 | 14.63x | 0.031 | 0.219 | 0.044 | 0.104 | 343 | 38 | 424/1832/32 | 1 / 0 |
| torus | 128 | 0.5 | 3376 | 21.21x | 0.058 | 0.461 | 0.060 | 0.304 | 315 | 104 | 96/1336/192 | 1 / 0 |
| torus | 256 | 0 | 289232 | 1.00x | 0.005 | 0.020 | 0.003 | 0.017 | 1178 | 0 | - | 0 / 0 |
| torus | 256 | 0.1 | 30032 | 9.63x | 0.013 | 0.088 | 0.015 | 0.052 | 1637 | 390 | 6896/6384/176 | 2 / 48 |
| torus | 256 | 0.25 | 14656 | 19.73x | 0.028 | 0.234 | 0.028 | 0.145 | 1588 | 346 | 648/5432/816 | 2 / 96 |
| torus | 256 | 0.5 | 5504 | 52.55x | 0.063 | 0.423 | 0.066 | 0.185 | 1587 | 329 | 136/728/1776/32 | 1 / 0 |

Reading. At 0.25 finest cells the simplified contour has 3.5 to 113 times fewer triangles (CSG part 4x at N=32, 16x at 64,
18x at 128, 31x at 256) at a mean distance to the true surface of 0.01 to 0.05 cells; the maximum is a fraction of the tolerance
plus the sagitta of the largest blocks on curved parts (sphere, torus). The gain grows with N because the surface cells of a
flat or smooth part grow with N squared and the merged ones with N. The thin parts of the CSG part (cylinder walls, the creases
at the cylinder bores) keep fine cells. Time is the uniform time plus 32 to 43 % at N=256 (the base contour is still uniform; see the before and after table below).

## Time: before (#995) and now

The first version contoured uniformly with the exact self-intersection check, then merged and checked again. Now the check runs once on the
merged mesh (the uniform contour is repaired only if it has intersections of its own), the clustering allocates less, and orientation is checked.
Tolerance 0.25, same machine, `ms total` as above (best of two for N up to 128):

| shape | N | uniform ms | before (#995) ms | now ms | before vs uniform | now vs uniform |
|---|---|---|---|---|---|---|
| csg | 32 | 82 | 136 | 99 | +66% | +21% |
| csg | 64 | 57 | 141 | 120 | +147% | +111% |
| csg | 128 | 338 | 647 | 547 | +91% | +62% |
| csg | 256 | 1721 | 3035 | 2461 | +76% | +43% |
| bracket | 32 | 9 | 22 | 32 | +144% | +256% |
| bracket | 64 | 31 | 65 | 47 | +110% | +52% |
| bracket | 128 | 207 | 474 | 382 | +129% | +85% |
| bracket | 256 | 1308 | 2143 | 1732 | +64% | +32% |
| sphere | 32 | 22 | 24 | 38 | +9% | +73% |
| sphere | 64 | 93 | 168 | 127 | +81% | +37% |
| sphere | 128 | 468 | 755 | 500 | +61% | +7% |
| sphere | 256 | 1894 | 3196 | 2497 | +69% | +32% |
| torus | 32 | 8 | 16 | 11 | +100% | +38% |
| torus | 64 | 56 | 104 | 43 | +86% | -23% |
| torus | 128 | 227 | 430 | 343 | +89% | +51% |
| torus | 256 | 1178 | 2036 | 1588 | +73% | +35% |

Where the time goes (`--phases`, tolerance 0.25, ms; `intersections` includes the emission of the polygons of the uniform contour, whose own
check is skipped; `simplify` is the whole merge with its check). A sparse grid would only save `sample` and `crossings` (8-15 %):

| shape | N | tol | sample | crossings | hermite | vertices | intersections | mesh | simplify | total ms |
|---|---|---|---|---|---|---|---|---|---|---|
| csg | 64 | 0.25 | 7 | 3 | 39 | 54 | 76 | 0 | 53 | 236 |
| csg | 128 | 0.25 | 45 | 14 | 45 | 267 | 109 | 2 | 199 | 682 |
| csg | 256 | 0.25 | 180 | 78 | 131 | 1152 | 337 | 7 | 539 | 2427 |
| bracket | 64 | 0.25 | 9 | 1 | 20 | 6 | 11 | 0 | 15 | 65 |
| bracket | 128 | 0.25 | 25 | 13 | 30 | 142 | 66 | 1 | 157 | 438 |
| bracket | 256 | 0.25 | 211 | 98 | 115 | 763 | 233 | 6 | 370 | 1798 |
| sphere | 64 | 0.25 | 1 | 2 | 8 | 76 | 30 | 0 | 19 | 138 |
| sphere | 128 | 0.25 | 11 | 11 | 24 | 214 | 85 | 2 | 180 | 530 |
| sphere | 256 | 0.25 | 91 | 97 | 98 | 1282 | 461 | 8 | 453 | 2494 |

## Triangle quality (`--quality`)

Smallest angle of each triangle (degrees): 1st percentile, mean, share below 5 and 10 degrees, share with an angle above 150 degrees.
Tolerance 0 is the uniform contour.

| shape | N | tolerance | triangles | min angle p01 | min angle mean | below 5 deg | below 10 deg | max angle above 150 deg |
|---|---|---|---|---|---|---|---|---|
| csg | 64 | 0 | 28944 | 17.0 | 41.1 | 0.8 % | 0.8 % | 0.1 % |
| csg | 64 | 0.25 | 1824 | 4.3 | 37.5 | 1.3 % | 2.0 % | 3.3 % |
| csg | 64 | 0.5 | 1824 | 4.3 | 37.5 | 1.3 % | 2.0 % | 3.3 % |
| csg | 128 | 0 | 114000 | 21.6 | 41.4 | 0.3 % | 0.4 % | 0.0 % |
| csg | 128 | 0.25 | 6300 | 1.9 | 33.8 | 3.4 % | 4.8 % | 3.6 % |
| csg | 128 | 0.5 | 3546 | 1.9 | 34.1 | 3.4 % | 4.6 % | 2.7 % |
| bracket | 64 | 0 | 18974 | 27.7 | 42.6 | 0.0 % | 0.1 % | 0.0 % |
| bracket | 64 | 0.25 | 1022 | 10.4 | 34.8 | 0.4 % | 0.9 % | 1.4 % |
| bracket | 64 | 0.5 | 776 | 10.5 | 33.5 | 0.3 % | 0.5 % | 0.8 % |
| bracket | 128 | 0 | 75488 | 29.0 | 43.2 | 0.0 % | 0.0 % | 0.0 % |
| bracket | 128 | 0.25 | 1718 | 7.9 | 34.9 | 0.3 % | 1.3 % | 0.9 % |
| bracket | 128 | 0.5 | 1192 | 7.8 | 34.5 | 0.3 % | 1.6 % | 1.3 % |
| sphere | 64 | 0 | 31932 | 28.0 | 41.8 | 0.0 % | 0.0 % | 0.0 % |
| sphere | 64 | 0.25 | 1932 | 20.9 | 40.0 | 0.0 % | 0.0 % | 0.0 % |
| sphere | 64 | 0.5 | 540 | 23.4 | 43.9 | 0.0 % | 0.0 % | 0.0 % |
| sphere | 128 | 0 | 127740 | 28.1 | 42.0 | 0.1 % | 0.1 % | 0.0 % |
| sphere | 128 | 0.25 | 2508 | 10.1 | 39.1 | 0.0 % | 0.0 % | 1.9 % |
| sphere | 128 | 0.5 | 1932 | 21.1 | 41.3 | 0.0 % | 0.0 % | 0.0 % |
| torus | 64 | 0 | 18208 | 27.4 | 41.7 | 0.1 % | 0.1 % | 0.0 % |
| torus | 64 | 0.25 | 3216 | 14.4 | 38.5 | 0.0 % | 0.0 % | 0.0 % |
| torus | 64 | 0.5 | 1328 | 20.5 | 39.7 | 0.0 % | 0.0 % | 0.0 % |
| torus | 128 | 0 | 71616 | 28.1 | 42.1 | 0.0 % | 0.0 % | 0.0 % |
| torus | 128 | 0.25 | 4896 | 11.6 | 41.8 | 0.0 % | 1.0 % | 0.0 % |
| torus | 128 | 0.5 | 3376 | 13.3 | 39.0 | 0.0 % | 0.0 % | 0.9 % |

The simplified contour has a lower mean smallest angle (34-40 against 41-43 degrees) and, on the CSG part with its creases, more slivers
(3-5 % of the triangles below 10 degrees against under 1 %); on smooth shapes (sphere, torus) it stays close to the uniform contour.

The upper bound measured before the implementation (ideal merge, no topology test) was 3.7x / 7x / 10x for the CSG part at
N = 32 / 64 / 128 at 0.1 cells; the real result at 0.1 is 3.7x / 4.2x / 11.1x (the take-back of the check costs at N=64; the
rounds column shows it).

## Eigen solver reuse (allocation fix)

`SymmetricEigenSolver` sized its Givens list to `maxIterations * (size - 1)` and `solve_qef` built one solver per call, so
57 % of the bytes of an AdaptiveDualContouring run were that list (about 3 KB per QEF; 196 KB per solver with 4096
iterations in the fit classes and PointCloudNormals). Now the list grows on demand, the sort buffer is a field (a `Solve`
allocates nothing), and DualContouring / AdaptiveDualContouring keep one solver per worker (`gParallel.ForRange` with a
local). `--alloc [--serial]` (N = 256, tolerance 0.25, min time of 3 runs after a warm-up, 4-core container, two rounds
alternating before / after, the better of the two):

| shape | threads | ms before | ms after | change | allocated MB before | after | mesh hash (before = after) |
|---|---|---|---|---|---|---|---|
| csg | 1 | 3441 | 2954 | -14 % | 1911 | 790 | 531304BD640BA5E8 |
| bracket | 1 | 2687 | 2250 | -16 % | 1285 | 539 | DA2938A298988D9F |
| sphere | 1 | 3077 | 2754 | -11 % | 2025 | 807 | 045365CE274438E8 |
| csg | 4 | 2319 | 2021 | -13 % | 1911 | 790 | 531304BD640BA5E8 |
| bracket | 4 | 1627 | 1282 | -21 % | 1286 | 539 | DA2938A298988D9F |
| sphere | 4 | 2279 | 1942 | -15 % | 2025 | 807 | 045365CE274438E8 |

(The hash is of this benchmark's own vertex and triangle bits; it is the same before and after on every row. The GC pause
column of `--alloc` is the pause during the third run; at 1 thread it fell from 510 to 348 ms (bracket) and 878 to 748 ms (sphere); csg is within noise (748 / 782 to 758 / 705 ms), and
the 4-thread pauses vary by run.)
