| input | alpha | offset | wrap tris | wrap s | wrap H | snapped H (safe) | naive clean? | safe moved fully | safe verdict |
|---|---|---|---:|---:|---:|---:|---|---:|---|
| box | d/20 | 0.001 | 1822 | 0.05 | 1.05e-03 | 5.18e-04 | no (degenerate triangles; self-int 97) | 97.2% | clean, self-int 0 |
| box | d/20 | 0.0001 | 5734 | 0.14 | 1.05e-04 | 1.12e-05 | no (degenerate triangles; self-int 448) | 96.9% | clean, self-int 0 |
| box | d/100 | 0.001 | 22744 | 0.20 | 1.05e-03 | 6.35e-04 | clean | 100.0% | clean, self-int 0 |
| box | d/100 | 0.0001 | 37318 | 0.50 | 1.05e-04 | 1.00e-04 | no (degenerate triangles; self-int 1004) | 98.1% | clean, self-int 0 |
| box_far | d/20 | 0.001 | 1862 | 0.19 | 1.05e-03 | 5.18e-04 | no (degenerate triangles; self-int 140) | 96.0% | clean, self-int 0 |
| box_far | d/20 | 0.0001 | 6342 | 0.19 | 1.05e-04 | 1.12e-05 | no (degenerate triangles; self-int 342) | 98.1% | clean, self-int 0 |
| box_far | d/100 | 0.001 | 22766 | 0.27 | 1.05e-03 | 5.07e-04 | no (degenerate triangles; self-int 8) | 100.0% | clean, self-int 0 |
| box_far | d/100 | 0.0001 | 36840 | 0.46 | 1.05e-04 | 2.80e-05 | no (degenerate triangles; self-int 1003) | 98.1% | clean, self-int 0 |
| cylinder | d/20 | 0.001 | 2648 | 0.10 | 1.05e-03 | 1.04e-03 | no (degenerate triangles; self-int 34) | 99.2% | clean, self-int 0 |
| cylinder | d/20 | 0.0001 | 8082 | 0.41 | 1.47e-04 | 5.96e-04 | no (degenerate triangles; self-int 151) | 99.3% | clean, self-int 0 |
| cylinder | d/100 | 0.001 | 20598 | 0.61 | 4.53e-03 | 1.27e-03 | no (degenerate triangles; self-int 14) | 100.0% | clean, self-int 0 |
| cylinder | d/100 | 0.0001 | 33622 | 1.32 | 3.04e-03 | 1.34e-03 | no (degenerate triangles; self-int 708) | 99.1% | clean, self-int 0 |
| sphere | d/20 | 0.001 | 2920 | 0.36 | 1.69e-03 | 1.03e-03 | clean | 100.0% | clean, self-int 0 |
| sphere | d/20 | 0.0001 | 13276 | 0.44 | 7.52e-04 | 6.52e-04 | clean | 100.0% | clean, self-int 0 |
| sphere | d/100 | 0.001 | 14294 | 0.31 | 1.28e-03 | 8.36e-04 | clean | 100.0% | clean, self-int 0 |
| sphere | d/100 | 0.0001 | 25056 | 0.44 | 4.35e-04 | 3.35e-04 | clean | 100.0% | clean, self-int 0 |
| plate1e-2 | d/20 | 0.001 | 2588 | 0.10 | 1.05e-03 | 1.12e-04 | no (degenerate triangles; self-int 118) | 97.4% | clean, self-int 0 |
| plate1e-2 | d/20 | 0.0001 | 4510 | 0.08 | 1.05e-04 | 1.43e-05 | no (degenerate triangles; self-int 283) | 97.4% | clean, self-int 0 |
| plate1e-2 | d/100 | 0.001 | 14528 | 0.30 | 1.05e-03 | 5.87e-04 | clean | 100.0% | clean, self-int 0 |
| plate1e-2 | d/100 | 0.0001 | 23514 | 0.78 | 3.96e-04 | 4.17e-04 | no (degenerate triangles; self-int 1004) | 97.7% | clean, self-int 0 |
| plate1e-3 | d/20 | 0.001 | 5802 | 0.18 | 1.05e-03 | 2.58e-04 | no (degenerate triangles; self-int 567) | 95.8% | clean, self-int 0 |
| plate1e-3 | d/20 | 0.0001 | 17838 | 0.32 | 3.79e-04 | 4.77e-04 | no (degenerate triangles; self-int 1002) | 93.4% | clean, self-int 0 |
| plate1e-3 | d/100 | 0.001 | 22816 | 0.39 | 1.05e-03 | 6.65e-04 | no (degenerate triangles; self-int 19) | 100.0% | clean, self-int 0 |
| plate1e-3 | d/100 | 0.0001 | 56914 | 0.72 | 8.75e-04 | 8.54e-04 | no (degenerate triangles; self-int 1004) | 98.6% | clean, self-int 0 |
| plate1e-4 | d/20 | 0.001 | 7730 | 0.19 | 1.05e-03 | 3.28e-04 | no (degenerate triangles; self-int 1001) | 93.9% | clean, self-int 0 |
| plate1e-4 | d/20 | 0.0001 | 49234 | 0.95 | 1.01e-03 | 1.93e-04 | no (degenerate triangles; self-int 1002) | 91.5% | clean, self-int 0 |
| plate1e-4 | d/100 | 0.001 | 29822 | 0.31 | 1.05e-03 | 1.50e-04 | no (degenerate triangles; self-int 55) | 99.9% | clean, self-int 0 |
| plate1e-4 | d/100 | 0.0001 | 191744 | 4.36 | 5.51e-04 | 5.56e-04 | no (degenerate triangles; self-int 1004) | 97.6% | clean, self-int 0 |
| lshape | d/20 | 0.001 | 2100 | 0.10 | 3.58e-02 | 3.52e-02 | no (degenerate triangles; self-int 196) | 95.3% | clean, self-int 0 |
| lshape | d/20 | 0.0001 | 5606 | 0.16 | 3.31e-02 | 3.31e-02 | no (degenerate triangles; self-int 193) | 98.5% | clean, self-int 0 |
| lshape | d/100 | 0.001 | 20292 | 0.30 | 9.59e-03 | 8.94e-03 | no (degenerate triangles; self-int 3) | 100.0% | clean, self-int 0 |
| lshape | d/100 | 0.0001 | 33250 | 0.59 | 8.45e-03 | 8.39e-03 | no (degenerate triangles; self-int 1003) | 97.9% | clean, self-int 0 |
| slot0.20 | d/20 | 0.001 | 2392 | 0.23 | 4.08e-02 | 4.01e-02 | no (degenerate triangles; self-int 202) | 95.6% | clean, self-int 0 |
| slot0.20 | d/20 | 0.0001 | 7230 | 0.17 | 3.65e-02 | 3.64e-02 | no (degenerate triangles; self-int 391) | 97.9% | clean, self-int 0 |
| slot0.20 | d/100 | 0.001 | 23970 | 0.41 | 9.25e-03 | 8.63e-03 | no (degenerate triangles; self-int 3) | 100.0% | clean, self-int 0 |
| slot0.20 | d/100 | 0.0001 | 42542 | 0.95 | 8.69e-03 | 8.62e-03 | no (degenerate triangles; self-int 1003) | 97.3% | clean, self-int 0 |
| slot0.05 | d/20 | 0.001 | 2010 | 0.03 | 1.63e-01 | 1.62e-01 | no (degenerate triangles; self-int 152) | 96.0% | clean, self-int 0 |
| slot0.05 | d/20 | 0.0001 | 5398 | 0.08 | 1.62e-01 | 1.62e-01 | no (degenerate triangles; self-int 297) | 97.7% | clean, self-int 0 |
| slot0.05 | d/100 | 0.001 | 24876 | 0.30 | 9.87e-03 | 9.19e-03 | no (degenerate triangles; self-int 14) | 100.0% | clean, self-int 0 |
| slot0.05 | d/100 | 0.0001 | 41978 | 0.53 | 8.88e-03 | 8.81e-03 | no (degenerate triangles; self-int 1004) | 97.7% | clean, self-int 0 |
| slot0.01 | d/20 | 0.001 | 2092 | 0.03 | 1.63e-01 | 1.62e-01 | no (degenerate triangles; self-int 132) | 96.9% | clean, self-int 0 |
| slot0.01 | d/20 | 0.0001 | 6026 | 0.07 | 1.62e-01 | 1.62e-01 | no (degenerate triangles; self-int 341) | 97.9% | clean, self-int 0 |
| slot0.01 | d/100 | 0.001 | 22796 | 0.18 | 1.63e-01 | 1.62e-01 | no (degenerate triangles; self-int 4) | 100.0% | clean, self-int 0 |
| slot0.01 | d/100 | 0.0001 | 36040 | 0.30 | 1.62e-01 | 1.62e-01 | no (degenerate triangles; self-int 1007) | 98.2% | clean, self-int 0 |
