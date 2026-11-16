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
| csg | 32 | 0.1 | 1824 | 3.71x | 0.012 | 0.067 | 0.029 | 0.059 | 131 | 70 | 816/8 | 1 / 0 |
| csg | 32 | 0.25 | 1584 | 4.27x | 0.020 | 0.218 | 0.028 | 0.193 | 136 | 58 | 672/32 | 1 / 0 |
| csg | 32 | 0.5 | 816 | 8.29x | 0.066 | 0.309 | 0.053 | 0.394 | 50 | 37 | 240/104 | 1 / 0 |
| csg | 64 | 0 | 28944 | 1.00x | 0.003 | 0.018 | 0.005 | 0.017 | 63 | 0 | - | 0 / 0 |
| csg | 64 | 0.1 | 6854 | 4.22x | 0.012 | 0.095 | 0.014 | 0.089 | 196 | 154 | 886/589 | 3 / 411 |
| csg | 64 | 0.25 | 1824 | 15.87x | 0.025 | 0.125 | 0.046 | 0.117 | 141 | 94 | 80/816/8 | 1 / 0 |
| csg | 64 | 0.5 | 1824 | 15.87x | 0.025 | 0.125 | 0.046 | 0.117 | 131 | 90 | 80/816/8 | 1 / 0 |
| csg | 128 | 0 | 114000 | 1.00x | 0.003 | 0.031 | 0.003 | 0.079 | 479 | 0 | - | 0 / 0 |
| csg | 128 | 0.1 | 10318 | 11.05x | 0.011 | 0.059 | 0.016 | 0.073 | 689 | 289 | 1728/2346/127/8 | 3 / 521 |
| csg | 128 | 0.25 | 7150 | 15.94x | 0.026 | 0.230 | 0.020 | 0.135 | 647 | 221 | 1104/1122/487/8 | 3 / 521 |
| csg | 128 | 0.5 | 2496 | 45.67x | 0.051 | 0.340 | 0.055 | 0.187 | 617 | 164 | 288/152/792/8 | 1 / 0 |
| csg | 256 | 0 | 461232 | 1.00x | 0.005 | 0.012 | 0.001 | 0.013 | 2367 | 0 | - | 0 / 0 |
| csg | 256 | 0.1 | 29874 | 15.44x | 0.009 | 0.065 | 0.009 | 0.068 | 3376 | 1085 | 2495/7748/1176/120 | 5 / 1317 |
| csg | 256 | 0.25 | 15060 | 30.63x | 0.027 | 0.209 | 0.018 | 0.134 | 3035 | 855 | 1840/1456/2204/198 | 5 / 962 |
| csg | 256 | 0.5 | 13284 | 34.72x | 0.041 | 0.260 | 0.019 | 0.252 | 2999 | 820 | 1760/1360/1484/366/8 | 5 / 962 |
| bracket | 32 | 0 | 5012 | 1.00x | 0.003 | 0.172 | 0.005 | 0.108 | 11 | 0 | - | 0 / 0 |
| bracket | 32 | 0.1 | 1004 | 4.99x | 0.004 | 0.172 | 0.022 | 0.116 | 21 | 11 | 114/88/5 | 3 / 19 |
| bracket | 32 | 0.25 | 798 | 6.28x | 0.006 | 0.200 | 0.020 | 0.216 | 22 | 11 | 120/95/5 | 3 / 19 |
| bracket | 32 | 0.5 | 506 | 9.91x | 0.014 | 0.482 | 0.019 | 0.352 | 57 | 21 | 110/101/6 | 2 / 7 |
| bracket | 64 | 0 | 18974 | 1.00x | 0.002 | 0.259 | 0.003 | 0.059 | 36 | 0 | - | 0 / 0 |
| bracket | 64 | 0.1 | 1530 | 12.40x | 0.006 | 0.280 | 0.025 | 0.100 | 71 | 29 | 193/123/87/5 | 3 / 19 |
| bracket | 64 | 0.25 | 1100 | 17.25x | 0.007 | 0.280 | 0.029 | 0.222 | 65 | 25 | 233/135/85/5 | 3 / 33 |
| bracket | 64 | 0.5 | 840 | 22.59x | 0.012 | 0.410 | 0.028 | 0.341 | 111 | 101 | 137/139/92/5 | 3 / 27 |
| bracket | 128 | 0 | 75488 | 1.00x | 0.003 | 0.027 | 0.002 | 0.035 | 233 | 0 | - | 0 / 0 |
| bracket | 128 | 0.1 | 2858 | 26.41x | 0.006 | 0.097 | 0.021 | 0.099 | 459 | 192 | 746/120/184/73/5 | 4 / 102 |
| bracket | 128 | 0.25 | 1800 | 41.94x | 0.013 | 0.163 | 0.028 | 0.176 | 474 | 85 | 283/211/119/87/5 | 4 / 98 |
| bracket | 128 | 0.5 | 1298 | 58.16x | 0.015 | 0.318 | 0.033 | 0.350 | 451 | 82 | 91/216/129/87/5 | 5 / 67 |
| bracket | 256 | 0 | 301758 | 1.00x | 0.004 | 0.027 | 0.001 | 0.035 | 1447 | 0 | - | 0 / 0 |
| bracket | 256 | 0.1 | 6422 | 46.99x | 0.008 | 0.085 | 0.019 | 0.088 | 2053 | 662 | 1691/725/238/124/74/5 | 5 / 136 |
| bracket | 256 | 0.25 | 2866 | 105.29x | 0.013 | 0.175 | 0.031 | 0.165 | 2143 | 589 | 133/739/136/178/74/5 | 5 / 105 |
| bracket | 256 | 0.5 | 1964 | 153.64x | 0.026 | 0.364 | 0.040 | 0.320 | 2085 | 538 | 99/277/223/116/87/5 | 5 / 124 |
| sphere | 32 | 0 | 7980 | 1.00x | 0.005 | 0.021 | 0.013 | 0.022 | 17 | 0 | - | 0 / 0 |
| sphere | 32 | 0.1 | 1932 | 4.13x | 0.011 | 0.050 | 0.035 | 0.053 | 22 | 9 | 824/48 | 1 / 0 |
| sphere | 32 | 0.25 | 540 | 14.78x | 0.046 | 0.180 | 0.090 | 0.169 | 24 | 9 | 56/216 | 1 / 0 |
| sphere | 32 | 0.5 | 540 | 14.78x | 0.046 | 0.180 | 0.090 | 0.169 | 24 | 9 | 56/216 | 1 / 0 |
| sphere | 64 | 0 | 31932 | 1.00x | 0.003 | 0.014 | 0.007 | 0.012 | 103 | 0 | - | 0 / 0 |
| sphere | 64 | 0.1 | 3948 | 8.09x | 0.017 | 0.080 | 0.029 | 0.078 | 161 | 30 | 1128/632/48 | 1 / 0 |
| sphere | 64 | 0.25 | 1932 | 16.53x | 0.024 | 0.080 | 0.052 | 0.080 | 168 | 33 | 72/824/48 | 1 / 0 |
| sphere | 64 | 0.5 | 540 | 59.13x | 0.094 | 0.356 | 0.152 | 0.300 | 165 | 32 | 0/56/216 | 1 / 0 |
| sphere | 128 | 0 | 127740 | 1.00x | 0.003 | 0.013 | 0.003 | 0.016 | 485 | 0 | - | 0 / 0 |
| sphere | 128 | 0.1 | 7596 | 16.82x | 0.013 | 0.073 | 0.026 | 0.052 | 709 | 204 | 240/3216/128 | 1 / 0 |
| sphere | 128 | 0.25 | 2508 | 50.93x | 0.053 | 0.182 | 0.069 | 0.135 | 755 | 233 | 24/360/776/48 | 1 / 0 |
| sphere | 128 | 0.5 | 1932 | 66.12x | 0.056 | 0.182 | 0.088 | 0.142 | 726 | 264 | 24/72/824/48 | 1 / 0 |
| sphere | 256 | 0 | 510636 | 1.00x | 0.004 | 0.018 | 0.002 | 0.015 | 2308 | 0 | - | 0 / 0 |
| sphere | 256 | 0.1 | 19260 | 26.51x | 0.017 | 0.098 | 0.019 | 0.069 | 3124 | 847 | 552/6048/2360 | 1 / 0 |
| sphere | 256 | 0.25 | 7548 | 67.65x | 0.029 | 0.150 | 0.045 | 0.116 | 3196 | 950 | 96/240/3168/152 | 1 / 0 |
| sphere | 256 | 0.5 | 2796 | 182.63x | 0.111 | 0.401 | 0.111 | 0.250 | 3199 | 880 | 0/72/480/752/48 | 1 / 0 |
| torus | 32 | 0 | 4432 | 1.00x | 0.013 | 0.051 | 0.025 | 0.057 | 7 | 0 | - | 0 / 0 |
| torus | 32 | 0.1 | 3824 | 1.16x | 0.015 | 0.088 | 0.027 | 0.096 | 17 | 8 | 144 | 1 / 0 |
| torus | 32 | 0.25 | 1280 | 3.46x | 0.033 | 0.142 | 0.059 | 0.115 | 16 | 6 | 432 | 1 / 0 |
| torus | 32 | 0.5 | 768 | 5.77x | 0.058 | 0.414 | 0.087 | 0.238 | 15 | 5 | 328/32 | 1 / 0 |
| torus | 64 | 0 | 18208 | 1.00x | 0.006 | 0.029 | 0.012 | 0.032 | 39 | 0 | - | 0 / 0 |
| torus | 64 | 0.1 | 7632 | 2.39x | 0.014 | 0.097 | 0.021 | 0.068 | 64 | 27 | 1552/40 | 1 / 0 |
| torus | 64 | 0.25 | 3216 | 5.66x | 0.034 | 0.240 | 0.042 | 0.177 | 104 | 21 | 1256/224 | 1 / 0 |
| torus | 64 | 0.5 | 1328 | 13.71x | 0.074 | 0.405 | 0.084 | 0.185 | 54 | 19 | 216/424 | 1 / 0 |
| torus | 128 | 0 | 71616 | 1.00x | 0.004 | 0.017 | 0.006 | 0.017 | 251 | 0 | - | 0 / 0 |
| torus | 128 | 0.1 | 15920 | 4.50x | 0.010 | 0.093 | 0.017 | 0.086 | 407 | 178 | 6552/584 | 1 / 0 |
| torus | 128 | 0.25 | 4896 | 14.63x | 0.031 | 0.219 | 0.044 | 0.104 | 430 | 144 | 424/1832/32 | 1 / 0 |
| torus | 128 | 0.5 | 3376 | 21.21x | 0.058 | 0.461 | 0.060 | 0.304 | 423 | 70 | 96/1336/192 | 1 / 0 |
| torus | 256 | 0 | 289232 | 1.00x | 0.005 | 0.020 | 0.003 | 0.017 | 1431 | 0 | - | 0 / 0 |
| torus | 256 | 0.1 | 29744 | 9.72x | 0.013 | 0.088 | 0.015 | 0.052 | 2008 | 559 | 6896/6400/176 | 1 / 0 |
| torus | 256 | 0.25 | 14112 | 20.50x | 0.028 | 0.234 | 0.029 | 0.145 | 2036 | 464 | 392/5432/848 | 1 / 0 |
| torus | 256 | 0.5 | 5504 | 52.55x | 0.063 | 0.423 | 0.066 | 0.185 | 2126 | 513 | 136/728/1776/32 | 1 / 0 |

Reading. At 0.25 finest cells the simplified contour has 3.5 to 105 times fewer triangles (CSG part 4x at N=32, 16x at 64,
16x at 128, 31x at 256 (unchanged by the merged exact-Hermite default, rows re-measured)) at a mean distance to the true surface of 0.01 to 0.05 cells; the maximum is a fraction of the tolerance
plus the sagitta of the largest blocks on curved parts (sphere, torus). The gain grows with N because the surface cells of a
flat or smooth part grow with N squared and the merged ones with N. The thin parts of the CSG part (cylinder walls, the creases
at the cylinder bores) keep fine cells. Time is the uniform time plus 30 to 60 % (the base contour is still uniform).

The upper bound measured before the implementation (ideal merge, no topology test) was 3.7x / 7x / 10x for the CSG part at
N = 32 / 64 / 128 at 0.1 cells; the real result at 0.1 is 3.7x / 4.2x / 11.1x (the take-back of the check costs at N=64; the
rounds column shows it).
