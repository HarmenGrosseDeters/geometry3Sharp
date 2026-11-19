# Coarse scalar size field: raw results (cloud container, 4 cores, 2026-11-19)

Command: dotnet run -c Release --project benchmarks/QuadCoarseScale -- only=<shape> det=1 (small shapes); big=110000 quick=1 minN=800 (big sphere, gate 20 %); big=110000 quick=1 minN=5000 fine=0 gate=0 (big sphere, size field applied whenever the curl drops). 'solve' excludes the hierarchy build (shared with the position field in production). The big sphere has 120806 input vertices here (Off 378 irregular; the design doc's 110k run has 352).

## small shapes

### sphere (input V 6390, s 0.1, singular faces 8)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.00 | 0.26 | 0.04 | 0.30 | - |
| library Scale (full, 2 unknowns / vertex) | 12780 | yes | 64.9 -> 29.1 | 40 | 97.1 | 9.4 | 0.07 | 0.54-1.12 | 0.39 | 0.50 | 0.04 | 0.93 | - |
<!-- sphere: hierarchy build 0.01 s (levels, colouring), level sizes 6390 3381 1799 963 519 275 144 78 42 -->
| level 0 (fine, scalar full solve) | 6390 | yes | 64.9 -> 29.1 | 40 | 97.1 | 9.4 | 0.07 | 0.54-1.12 | 0.11 | 0.05 | 0.00 | 0.17 | sigma same, mesh same |
| level 1 (3381 coarse) | 3381 | yes | 64.9 -> 49.0 | 48 | 96.1 | 10.8 | 0.08 | 0.66-1.05 | 0.02 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 1 (3381 coarse) + 5 GS | 3381 | yes | 64.9 -> 44.1 | 54 | 96.0 | 11.0 | 0.07 | 0.61-1.05 | 0.03 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 1 (3381 coarse) smoothed P x1 | 3381 | yes | 64.9 -> 36.7 | 41 | 97.1 | 9.7 | 0.07 | 0.58-1.11 | 0.14 | 0.05 | 0.00 | 0.19 | sigma same, mesh same |
| level 1 (3381 coarse) smoothed P x1 + 5 GS | 3381 | yes | 64.9 -> 32.9 | 46 | 97.2 | 9.6 | 0.07 | 0.55-1.11 | 0.15 | 0.04 | 0.00 | 0.20 | sigma same, mesh same |
| level 1 (3381 coarse) smoothed P x2 + 5 GS | 3381 | yes | 64.9 -> 34.1 | 40 | 96.9 | 10.0 | 0.07 | 0.56-1.10 | 0.38 | 0.05 | 0.00 | 0.43 | sigma same, mesh same |
| level 2 (1799 coarse) | 1799 | no | 64.9 -> 55.0 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.08 | sigma same, mesh same |
| level 2 (1799 coarse) + 5 GS | 1799 | yes | 64.9 -> 49.2 | 46 | 96.6 | 10.3 | 0.07 | 0.64-1.05 | 0.02 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 2 (1799 coarse) smoothed P x1 | 1799 | yes | 64.9 -> 40.1 | 41 | 96.8 | 9.7 | 0.07 | 0.63-1.09 | 0.04 | 0.05 | 0.00 | 0.10 | sigma same, mesh same |
| level 2 (1799 coarse) smoothed P x1 + 5 GS | 1799 | yes | 64.9 -> 36.9 | 36 | 97.4 | 9.7 | 0.07 | 0.56-1.09 | 0.06 | 0.07 | 0.00 | 0.13 | sigma same, mesh same |
| level 2 (1799 coarse) smoothed P x2 + 5 GS | 1799 | yes | 64.9 -> 34.2 | 34 | 97.7 | 8.9 | 0.07 | 0.55-1.12 | 0.24 | 0.05 | 0.00 | 0.29 | sigma same, mesh same |
| level 3 (963 coarse) | 963 | no | 64.9 -> 59.1 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.08 | sigma same, mesh same |
| level 3 (963 coarse) + 5 GS | 963 | no | 64.9 -> 52.8 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 3 (963 coarse) smoothed P x1 | 963 | yes | 64.9 -> 46.2 | 53 | 96.5 | 9.4 | 0.07 | 0.65-1.08 | 0.03 | 0.06 | 0.00 | 0.10 | sigma same, mesh same |
| level 3 (963 coarse) smoothed P x1 + 5 GS | 963 | yes | 64.9 -> 41.7 | 50 | 96.5 | 10.8 | 0.07 | 0.56-1.08 | 0.06 | 0.08 | 0.03 | 0.17 | sigma same, mesh same |
| level 3 (963 coarse) smoothed P x2 + 5 GS | 963 | yes | 64.9 -> 38.7 | 46 | 96.5 | 10.6 | 0.07 | 0.55-1.10 | 0.07 | 0.05 | 0.00 | 0.13 | sigma same, mesh same |
| level 4 (519 coarse) | 519 | no | 64.9 -> 61.2 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 4 (519 coarse) + 5 GS | 519 | no | 64.9 -> 54.7 | 75 | 94.8 | 14.6 | 0.07 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 4 (519 coarse) smoothed P x1 | 519 | yes | 64.9 -> 51.7 | 50 | 95.7 | 11.0 | 0.08 | 0.70-1.06 | 0.02 | 0.07 | 0.00 | 0.09 | sigma same, mesh same |
| level 4 (519 coarse) smoothed P x1 + 5 GS | 519 | yes | 64.9 -> 46.2 | 56 | 96.5 | 10.9 | 0.07 | 0.59-1.06 | 0.03 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 4 (519 coarse) smoothed P x2 + 5 GS | 519 | yes | 64.9 -> 43.1 | 46 | 96.8 | 10.5 | 0.07 | 0.57-1.07 | 0.03 | 0.05 | 0.00 | 0.09 | sigma same, mesh same |

### torus (input V 7026, s 0.1, singular faces 12)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.00 | 0.06 | 0.00 | 0.06 | - |
| library Scale (full, 2 unknowns / vertex) | 14052 | yes | 72.2 -> 34.3 | 35 | 98.0 | 9.2 | 0.15 | 0.59-1.83 | 0.16 | 0.21 | 0.00 | 0.38 | - |
<!-- torus: hierarchy build 0.01 s (levels, colouring), level sizes 7026 3713 1979 1065 576 308 164 87 47 -->
| level 0 (fine, scalar full solve) | 7026 | yes | 72.2 -> 34.3 | 35 | 98.0 | 9.2 | 0.15 | 0.59-1.83 | 0.06 | 0.06 | 0.00 | 0.13 | sigma same, mesh same |
| level 1 (3713 coarse) | 3713 | yes | 72.2 -> 51.8 | 50 | 96.6 | 10.6 | 0.18 | 0.70-1.52 | 0.02 | 0.08 | 0.00 | 0.11 | sigma same, mesh same |
| level 1 (3713 coarse) + 5 GS | 3713 | yes | 72.2 -> 40.8 | 46 | 97.0 | 10.3 | 0.14 | 0.65-1.79 | 0.05 | 0.07 | 0.00 | 0.11 | sigma same, mesh same |
| level 1 (3713 coarse) smoothed P x1 | 3713 | yes | 72.2 -> 40.2 | 35 | 97.6 | 10.3 | 0.14 | 0.63-1.70 | 0.12 | 0.06 | 0.00 | 0.18 | sigma same, mesh same |
| level 1 (3713 coarse) smoothed P x1 + 5 GS | 3713 | yes | 72.2 -> 36.0 | 34 | 97.9 | 8.7 | 0.19 | 0.60-1.82 | 0.12 | 0.06 | 0.00 | 0.19 | sigma same, mesh same |
| level 1 (3713 coarse) smoothed P x2 + 5 GS | 3713 | yes | 72.2 -> 37.5 | 52 | 96.7 | 10.6 | 0.24 | 0.61-1.82 | 0.51 | 0.06 | 0.00 | 0.57 | sigma same, mesh same |
| level 2 (1979 coarse) | 1979 | no | 72.2 -> 59.9 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.08 | sigma same, mesh same |
| level 2 (1979 coarse) + 5 GS | 1979 | yes | 72.2 -> 45.3 | 52 | 96.7 | 10.7 | 0.16 | 0.68-1.74 | 0.03 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 2 (1979 coarse) smoothed P x1 | 1979 | yes | 72.2 -> 43.2 | 40 | 97.4 | 9.2 | 0.16 | 0.69-1.64 | 0.06 | 0.07 | 0.01 | 0.13 | sigma same, mesh same |
| level 2 (1979 coarse) smoothed P x1 + 5 GS | 1979 | yes | 72.2 -> 38.3 | 44 | 97.3 | 10.1 | 0.23 | 0.63-1.80 | 0.06 | 0.06 | 0.00 | 0.12 | sigma same, mesh same |
| level 2 (1979 coarse) smoothed P x2 + 5 GS | 1979 | yes | 72.2 -> 37.9 | 49 | 97.1 | 10.1 | 0.23 | 0.62-1.80 | 0.13 | 0.06 | 0.00 | 0.20 | sigma same, mesh same |
| level 3 (1065 coarse) | 1065 | no | 72.2 -> 64.8 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.07 | sigma same, mesh same |
| level 3 (1065 coarse) + 5 GS | 1065 | yes | 72.2 -> 48.8 | 53 | 96.8 | 10.8 | 0.15 | 0.69-1.72 | 0.02 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 3 (1065 coarse) smoothed P x1 | 1065 | yes | 72.2 -> 49.6 | 47 | 96.9 | 10.2 | 0.16 | 0.76-1.47 | 0.02 | 0.07 | 0.00 | 0.09 | sigma same, mesh same |
| level 3 (1065 coarse) smoothed P x1 + 5 GS | 1065 | yes | 72.2 -> 40.9 | 48 | 97.3 | 11.1 | 0.20 | 0.65-1.78 | 0.04 | 0.06 | 0.00 | 0.10 | sigma same, mesh same |
| level 3 (1065 coarse) smoothed P x2 + 5 GS | 1065 | yes | 72.2 -> 40.1 | 53 | 97.0 | 10.8 | 0.15 | 0.64-1.79 | 0.05 | 0.06 | 0.00 | 0.12 | sigma same, mesh same |
| level 4 (576 coarse) | 576 | no | 72.2 -> 67.4 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.06 | sigma same, mesh same |
| level 4 (576 coarse) + 5 GS | 576 | yes | 72.2 -> 51.4 | 68 | 95.9 | 11.7 | 0.16 | 0.70-1.70 | 0.02 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 4 (576 coarse) smoothed P x1 | 576 | yes | 72.2 -> 55.5 | 56 | 96.0 | 10.8 | 0.15 | 0.85-1.33 | 0.02 | 0.06 | 0.00 | 0.08 | sigma same, mesh same |
| level 4 (576 coarse) smoothed P x1 + 5 GS | 576 | yes | 72.2 -> 43.4 | 49 | 96.8 | 10.6 | 0.15 | 0.66-1.79 | 0.03 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 4 (576 coarse) smoothed P x2 + 5 GS | 576 | yes | 72.2 -> 42.0 | 52 | 97.1 | 10.6 | 0.20 | 0.64-1.80 | 0.04 | 0.06 | 0.00 | 0.10 | sigma same, mesh same |
| level 5 (308 coarse) | 308 | no | 72.2 -> 69.0 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.03 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 5 (308 coarse) + 5 GS | 308 | yes | 72.2 -> 53.2 | 54 | 96.1 | 10.9 | 0.20 | 0.70-1.68 | 0.02 | 0.07 | 0.00 | 0.09 | sigma same, mesh same |
| level 5 (308 coarse) smoothed P x1 | 308 | no | 72.2 -> 61.3 | 76 | 94.9 | 12.7 | 0.16 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 5 (308 coarse) smoothed P x1 + 5 GS | 308 | yes | 72.2 -> 47.5 | 61 | 96.6 | 11.6 | 0.17 | 0.69-1.74 | 0.02 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |
| level 5 (308 coarse) smoothed P x2 + 5 GS | 308 | yes | 72.2 -> 45.4 | 52 | 97.4 | 9.9 | 0.17 | 0.68-1.75 | 0.03 | 0.06 | 0.00 | 0.09 | sigma same, mesh same |

### bumpy (input V 17918, s 0.06, singular faces 16)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.00 | 0.17 | 0.01 | 0.18 | - |
| library Scale (full, 2 unknowns / vertex) | 35836 | no | 138.6 -> 140.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.36 | 0.51 | 0.01 | 0.87 | - |
<!-- bumpy: hierarchy build 0.04 s (levels, colouring), level sizes 17918 9464 5035 2702 1455 781 420 223 119 65 35 -->
| level 0 (fine, scalar full solve) | 17918 | no | 138.6 -> 140.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.18 | 0.13 | 0.01 | 0.32 | sigma same, mesh same |
| level 1 (9464 coarse) | 9464 | no | 138.6 -> 154.7 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.09 | 0.19 | 0.01 | 0.28 | sigma same, mesh same |
| level 1 (9464 coarse) + 5 GS | 9464 | no | 138.6 -> 151.5 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.12 | 0.17 | 0.01 | 0.29 | sigma same, mesh same |
| level 1 (9464 coarse) smoothed P x1 | 9464 | no | 138.6 -> 152.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.34 | 0.16 | 0.01 | 0.51 | sigma same, mesh same |
| level 1 (9464 coarse) smoothed P x1 + 5 GS | 9464 | no | 138.6 -> 151.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.44 | 0.14 | 0.01 | 0.59 | sigma same, mesh same |
| level 1 (9464 coarse) smoothed P x2 + 5 GS | 9464 | no | 138.6 -> 156.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 1.35 | 0.15 | 0.01 | 1.50 | sigma same, mesh same |
| level 2 (5035 coarse) | 5035 | no | 138.6 -> 157.6 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.06 | 0.15 | 0.01 | 0.22 | sigma same, mesh same |
| level 2 (5035 coarse) + 5 GS | 5035 | no | 138.6 -> 152.9 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.07 | 0.14 | 0.01 | 0.22 | sigma same, mesh same |
| level 2 (5035 coarse) smoothed P x1 | 5035 | no | 138.6 -> 159.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.13 | 0.16 | 0.01 | 0.29 | sigma same, mesh same |
| level 2 (5035 coarse) smoothed P x1 + 5 GS | 5035 | no | 138.6 -> 157.6 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.20 | 0.15 | 0.01 | 0.35 | sigma same, mesh same |
| level 2 (5035 coarse) smoothed P x2 + 5 GS | 5035 | no | 138.6 -> 157.5 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.48 | 0.15 | 0.01 | 0.65 | sigma same, mesh same |
| level 3 (2702 coarse) | 2702 | no | 138.6 -> 155.3 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.03 | 0.15 | 0.01 | 0.19 | sigma same, mesh same |
| level 3 (2702 coarse) + 5 GS | 2702 | no | 138.6 -> 149.8 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.09 | 0.14 | 0.01 | 0.24 | sigma same, mesh same |
| level 3 (2702 coarse) smoothed P x1 | 2702 | no | 138.6 -> 165.2 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.07 | 0.18 | 0.02 | 0.28 | sigma same, mesh same |
| level 3 (2702 coarse) smoothed P x1 + 5 GS | 2702 | no | 138.6 -> 162.4 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.11 | 0.17 | 0.01 | 0.29 | sigma same, mesh same |
| level 3 (2702 coarse) smoothed P x2 + 5 GS | 2702 | no | 138.6 -> 162.9 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.22 | 0.14 | 0.01 | 0.37 | sigma same, mesh same |
| level 4 (1455 coarse) | 1455 | no | 138.6 -> 151.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.02 | 0.15 | 0.01 | 0.18 | sigma same, mesh same |
| level 4 (1455 coarse) + 5 GS | 1455 | no | 138.6 -> 145.6 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.06 | 0.15 | 0.01 | 0.22 | sigma same, mesh same |
| level 4 (1455 coarse) smoothed P x1 | 1455 | no | 138.6 -> 164.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.03 | 0.14 | 0.01 | 0.18 | sigma same, mesh same |
| level 4 (1455 coarse) smoothed P x1 + 5 GS | 1455 | no | 138.6 -> 160.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.08 | 0.17 | 0.01 | 0.25 | sigma same, mesh same |
| level 4 (1455 coarse) smoothed P x2 + 5 GS | 1455 | no | 138.6 -> 162.9 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.12 | 0.16 | 0.01 | 0.29 | sigma same, mesh same |
| level 5 (781 coarse) | 781 | no | 138.6 -> 147.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.03 | 0.15 | 0.01 | 0.19 | sigma same, mesh same |
| level 5 (781 coarse) + 5 GS | 781 | no | 138.6 -> 141.8 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.06 | 0.15 | 0.01 | 0.22 | sigma same, mesh same |
| level 5 (781 coarse) smoothed P x1 | 781 | no | 138.6 -> 159.2 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.03 | 0.14 | 0.01 | 0.18 | sigma same, mesh same |
| level 5 (781 coarse) smoothed P x1 + 5 GS | 781 | no | 138.6 -> 154.5 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.07 | 0.14 | 0.01 | 0.22 | sigma same, mesh same |
| level 5 (781 coarse) smoothed P x2 + 5 GS | 781 | no | 138.6 -> 158.8 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.08 | 0.16 | 0.01 | 0.26 | sigma same, mesh same |
| level 6 (420 coarse) | 420 | no | 138.6 -> 143.9 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.02 | 0.15 | 0.01 | 0.17 | sigma same, mesh same |
| level 6 (420 coarse) + 5 GS | 420 | no | 138.6 -> 138.9 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.06 | 0.14 | 0.01 | 0.21 | sigma same, mesh same |
| level 6 (420 coarse) smoothed P x1 | 420 | no | 138.6 -> 153.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.02 | 0.15 | 0.01 | 0.18 | sigma same, mesh same |
| level 6 (420 coarse) smoothed P x1 + 5 GS | 420 | no | 138.6 -> 148.0 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.06 | 0.16 | 0.01 | 0.23 | sigma same, mesh same |
| level 6 (420 coarse) smoothed P x2 + 5 GS | 420 | no | 138.6 -> 152.1 | 174 | 95.9 | 10.8 | 0.10 | 1.00-1.00 | 0.07 | 0.14 | 0.01 | 0.22 | sigma same, mesh same |

### filleted-box (input V 9092, s 0.08, singular faces 8)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.00 | 0.08 | 0.00 | 0.08 | - |
| library Scale (full, 2 unknowns / vertex) | 18184 | no | 29.0 -> 31.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.24 | 0.32 | 0.00 | 0.56 | - |
<!-- filleted-box: hierarchy build 0.03 s (levels, colouring), level sizes 9092 4790 2552 1373 736 398 214 111 61 -->
| level 0 (fine, scalar full solve) | 9092 | no | 29.0 -> 31.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.09 | 0.07 | 0.00 | 0.16 | sigma same, mesh same |
| level 1 (4790 coarse) | 4790 | no | 29.0 -> 35.2 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.03 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 1 (4790 coarse) + 5 GS | 4790 | no | 29.0 -> 32.3 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.05 | 0.07 | 0.00 | 0.12 | sigma same, mesh same |
| level 1 (4790 coarse) smoothed P x1 | 4790 | no | 29.0 -> 38.2 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.17 | 0.07 | 0.00 | 0.24 | sigma same, mesh same |
| level 1 (4790 coarse) smoothed P x1 + 5 GS | 4790 | no | 29.0 -> 35.3 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.21 | 0.08 | 0.00 | 0.29 | sigma same, mesh same |
| level 1 (4790 coarse) smoothed P x2 + 5 GS | 4790 | no | 29.0 -> 37.5 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.67 | 0.06 | 0.00 | 0.73 | sigma same, mesh same |
| level 2 (2552 coarse) | 2552 | no | 29.0 -> 35.8 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.08 | sigma same, mesh same |
| level 2 (2552 coarse) + 5 GS | 2552 | no | 29.0 -> 31.4 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.03 | 0.08 | 0.00 | 0.12 | sigma same, mesh same |
| level 2 (2552 coarse) smoothed P x1 | 2552 | no | 29.0 -> 39.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.06 | 0.08 | 0.00 | 0.14 | sigma same, mesh same |
| level 2 (2552 coarse) smoothed P x1 + 5 GS | 2552 | no | 29.0 -> 37.1 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.08 | 0.07 | 0.00 | 0.16 | sigma same, mesh same |
| level 2 (2552 coarse) smoothed P x2 + 5 GS | 2552 | no | 29.0 -> 37.5 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.16 | 0.08 | 0.00 | 0.24 | sigma same, mesh same |
| level 3 (1373 coarse) | 1373 | no | 29.0 -> 35.6 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.01 | 0.07 | 0.00 | 0.09 | sigma same, mesh same |
| level 3 (1373 coarse) + 5 GS | 1373 | no | 29.0 -> 30.3 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.03 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 3 (1373 coarse) smoothed P x1 | 1373 | no | 29.0 -> 41.4 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.02 | 0.07 | 0.00 | 0.09 | sigma same, mesh same |
| level 3 (1373 coarse) smoothed P x1 + 5 GS | 1373 | no | 29.0 -> 37.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.07 | 0.06 | 0.00 | 0.14 | sigma same, mesh same |
| level 3 (1373 coarse) smoothed P x2 + 5 GS | 1373 | no | 29.0 -> 39.2 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.09 | 0.08 | 0.00 | 0.18 | sigma same, mesh same |
| level 4 (736 coarse) | 736 | no | 29.0 -> 34.2 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.01 | 0.08 | 0.00 | 0.09 | sigma same, mesh same |
| level 4 (736 coarse) + 5 GS | 736 | no | 29.0 -> 28.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.02 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 4 (736 coarse) smoothed P x1 | 736 | no | 29.0 -> 41.0 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.02 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 4 (736 coarse) smoothed P x1 + 5 GS | 736 | no | 29.0 -> 36.4 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.03 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 4 (736 coarse) smoothed P x2 + 5 GS | 736 | no | 29.0 -> 38.6 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.04 | 0.07 | 0.00 | 0.11 | sigma same, mesh same |
| level 5 (398 coarse) | 398 | no | 29.0 -> 32.9 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.01 | 0.08 | 0.00 | 0.09 | sigma same, mesh same |
| level 5 (398 coarse) + 5 GS | 398 | no | 29.0 -> 27.8 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.02 | 0.07 | 0.00 | 0.10 | sigma same, mesh same |
| level 5 (398 coarse) smoothed P x1 | 398 | no | 29.0 -> 38.7 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.01 | 0.09 | 0.00 | 0.10 | sigma same, mesh same |
| level 5 (398 coarse) smoothed P x1 + 5 GS | 398 | no | 29.0 -> 33.6 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.03 | 0.07 | 0.01 | 0.10 | sigma same, mesh same |
| level 5 (398 coarse) smoothed P x2 + 5 GS | 398 | no | 29.0 -> 36.2 | 16 | 99.1 | 4.9 | 0.19 | 1.00-1.00 | 0.04 | 0.08 | 0.01 | 0.12 | sigma same, mesh same |

### noisy-sphere (input V 6390, s 0.1, singular faces 8)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.00 | 0.05 | 0.00 | 0.05 | - |
| library Scale (full, 2 unknowns / vertex) | 12780 | yes | 99.3 -> 71.7 | 67 | 95.8 | 12.5 | 0.19 | 0.58-1.10 | 0.11 | 0.17 | 0.00 | 0.29 | - |
<!-- noisy-sphere: hierarchy build 0.01 s (levels, colouring), level sizes 6390 3393 1811 972 521 280 150 83 45 -->
| level 0 (fine, scalar full solve) | 6390 | yes | 99.3 -> 71.7 | 67 | 95.8 | 12.5 | 0.19 | 0.58-1.10 | 0.04 | 0.05 | 0.00 | 0.10 | sigma same, mesh same |
| level 1 (3393 coarse) | 3393 | no | 99.3 -> 83.7 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 1 (3393 coarse) + 5 GS | 3393 | yes | 99.3 -> 77.0 | 79 | 95.0 | 13.1 | 0.19 | 0.61-1.10 | 0.03 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 1 (3393 coarse) smoothed P x1 | 3393 | yes | 99.3 -> 75.4 | 56 | 96.5 | 12.3 | 0.20 | 0.57-1.15 | 0.10 | 0.06 | 0.00 | 0.16 | sigma same, mesh same |
| level 1 (3393 coarse) smoothed P x1 + 5 GS | 3393 | yes | 99.3 -> 69.0 | 59 | 96.0 | 12.3 | 0.19 | 0.55-1.16 | 0.13 | 0.06 | 0.00 | 0.19 | sigma same, mesh same |
| level 1 (3393 coarse) smoothed P x2 + 5 GS | 3393 | yes | 99.3 -> 68.6 | 50 | 96.4 | 11.0 | 0.21 | 0.55-1.20 | 0.65 | 0.07 | 0.00 | 0.72 | sigma same, mesh same |
| level 2 (1811 coarse) | 1811 | no | 99.3 -> 89.6 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.06 | sigma same, mesh same |
| level 2 (1811 coarse) + 5 GS | 1811 | no | 99.3 -> 80.9 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 2 (1811 coarse) smoothed P x1 | 1811 | yes | 99.3 -> 78.9 | 63 | 95.4 | 11.6 | 0.19 | 0.63-1.13 | 0.04 | 0.05 | 0.00 | 0.09 | sigma same, mesh same |
| level 2 (1811 coarse) smoothed P x1 + 5 GS | 1811 | yes | 99.3 -> 71.8 | 60 | 96.3 | 11.5 | 0.20 | 0.57-1.14 | 0.05 | 0.09 | 0.00 | 0.15 | sigma same, mesh same |
| level 2 (1811 coarse) smoothed P x2 + 5 GS | 1811 | yes | 99.3 -> 70.8 | 57 | 96.4 | 11.9 | 0.19 | 0.56-1.16 | 0.16 | 0.05 | 0.00 | 0.21 | sigma same, mesh same |
| level 3 (972 coarse) | 972 | no | 99.3 -> 93.4 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.06 | sigma same, mesh same |
| level 3 (972 coarse) + 5 GS | 972 | no | 99.3 -> 83.7 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.02 | 0.05 | 0.00 | 0.06 | sigma same, mesh same |
| level 3 (972 coarse) smoothed P x1 | 972 | no | 99.3 -> 83.5 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.02 | 0.04 | 0.04 | 0.10 | sigma same, mesh same |
| level 3 (972 coarse) smoothed P x1 + 5 GS | 972 | yes | 99.3 -> 75.3 | 69 | 95.3 | 13.0 | 0.19 | 0.59-1.12 | 0.03 | 0.05 | 0.00 | 0.08 | sigma same, mesh same |
| level 3 (972 coarse) smoothed P x2 + 5 GS | 972 | yes | 99.3 -> 73.3 | 64 | 95.9 | 12.4 | 0.19 | 0.57-1.13 | 0.05 | 0.04 | 0.00 | 0.09 | sigma same, mesh same |
| level 4 (521 coarse) | 521 | no | 99.3 -> 95.8 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.01 | 0.06 | 0.00 | 0.07 | sigma same, mesh same |
| level 4 (521 coarse) + 5 GS | 521 | no | 99.3 -> 85.5 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.07 | sigma same, mesh same |
| level 4 (521 coarse) smoothed P x1 | 521 | no | 99.3 -> 89.4 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.01 | 0.05 | 0.00 | 0.06 | sigma same, mesh same |
| level 4 (521 coarse) smoothed P x1 + 5 GS | 521 | no | 99.3 -> 79.6 | 92 | 93.2 | 15.7 | 0.21 | 1.00-1.00 | 0.02 | 0.04 | 0.00 | 0.07 | sigma same, mesh same |
| level 4 (521 coarse) smoothed P x2 + 5 GS | 521 | yes | 99.3 -> 77.6 | 76 | 94.6 | 14.9 | 0.19 | 0.61-1.10 | 0.03 | 0.05 | 0.00 | 0.09 | sigma same, mesh same |

## big sphere, library gate (20 %)

### big-sphere (input V 120806, s 0.023, singular faces 8)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.00 | 1.26 | 0.14 | 1.40 | - |
| library Scale (full, 2 unknowns / vertex) | 241612 | yes | 292.9 -> 223.8 | 283 | 99.0 | 5.2 | 0.16 | 0.47-1.02 | 15.10 | 16.18 | 0.07 | 31.35 | - |
<!-- big-sphere: hierarchy build 0.36 s (levels, colouring), level sizes 120806 63817 34095 18319 9851 5289 2843 1528 820 435 237 127 67 36 -->
| level 0 (fine, scalar full solve) | 120806 | yes | 292.9 -> 223.8 | 283 | 99.0 | 5.2 | 0.16 | 0.47-1.02 | 4.22 | 1.16 | 0.08 | 5.47 | sigma same |
| level 1 (63817 coarse) | 63817 | no | 292.9 -> 274.1 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.80 | 1.08 | 0.08 | 1.95 | sigma same |
| level 1 (63817 coarse) + 5 GS | 63817 | no | 292.9 -> 269.9 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 1.23 | 1.05 | 0.07 | 2.36 | sigma same |
| level 1 (63817 coarse) smoothed P x1 + 5 GS | 63817 | no | 292.9 -> 245.1 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 6.22 | 1.13 | 0.07 | 7.42 | sigma same |
| level 1 (63817 coarse) smoothed P x2 + 5 GS | 63817 | no | 292.9 -> 256.9 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 16.53 | 1.04 | 0.08 | 17.64 | sigma same |
| level 2 (34095 coarse) | 34095 | no | 292.9 -> 281.7 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.52 | 1.05 | 0.09 | 1.66 | sigma same |
| level 2 (34095 coarse) + 5 GS | 34095 | no | 292.9 -> 277.1 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.99 | 1.01 | 0.21 | 2.21 | sigma same |
| level 2 (34095 coarse) smoothed P x1 + 5 GS | 34095 | no | 292.9 -> 256.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 2.19 | 1.11 | 0.11 | 3.41 | sigma same |
| level 2 (34095 coarse) smoothed P x2 + 5 GS | 34095 | no | 292.9 -> 252.3 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 4.40 | 1.13 | 0.09 | 5.62 | sigma same |
| level 3 (18319 coarse) | 18319 | no | 292.9 -> 287.3 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.32 | 1.12 | 0.09 | 1.53 | sigma same |
| level 3 (18319 coarse) + 5 GS | 18319 | no | 292.9 -> 282.5 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.60 | 1.09 | 0.07 | 1.76 | sigma same |
| level 3 (18319 coarse) smoothed P x1 + 5 GS | 18319 | no | 292.9 -> 266.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 1.24 | 1.24 | 0.07 | 2.55 | sigma same |
| level 3 (18319 coarse) smoothed P x2 + 5 GS | 18319 | no | 292.9 -> 259.2 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 1.82 | 1.26 | 0.10 | 3.19 | sigma same |
| level 4 (9851 coarse) | 9851 | no | 292.9 -> 290.3 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.17 | 1.13 | 0.07 | 1.36 | sigma same |
| level 4 (9851 coarse) + 5 GS | 9851 | no | 292.9 -> 285.2 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.71 | 1.20 | 0.10 | 2.01 | sigma same |
| level 4 (9851 coarse) smoothed P x1 + 5 GS | 9851 | no | 292.9 -> 274.1 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.86 | 1.02 | 0.07 | 1.95 | sigma same |
| level 4 (9851 coarse) smoothed P x2 + 5 GS | 9851 | no | 292.9 -> 269.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 1.23 | 1.07 | 0.17 | 2.47 | sigma same |
| level 5 (5289 coarse) | 5289 | no | 292.9 -> 292.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.22 | 1.05 | 0.08 | 1.35 | sigma same |
| level 5 (5289 coarse) + 5 GS | 5289 | no | 292.9 -> 286.7 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.63 | 1.10 | 0.07 | 1.81 | sigma same |
| level 5 (5289 coarse) smoothed P x1 + 5 GS | 5289 | no | 292.9 -> 281.2 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.77 | 1.06 | 0.09 | 1.92 | sigma same |
| level 5 (5289 coarse) smoothed P x2 + 5 GS | 5289 | no | 292.9 -> 275.9 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.76 | 1.10 | 0.07 | 1.92 | sigma same |
| level 6 (2843 coarse) | 2843 | no | 292.9 -> 292.6 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.15 | 0.99 | 0.08 | 1.22 | sigma same |
| level 6 (2843 coarse) + 5 GS | 2843 | no | 292.9 -> 287.4 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.66 | 1.24 | 0.08 | 1.99 | sigma same |
| level 6 (2843 coarse) smoothed P x1 + 5 GS | 2843 | no | 292.9 -> 284.8 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.65 | 1.13 | 0.07 | 1.85 | sigma same |
| level 6 (2843 coarse) smoothed P x2 + 5 GS | 2843 | no | 292.9 -> 282.3 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.76 | 1.22 | 0.11 | 2.09 | sigma same |
| level 7 (1528 coarse) | 1528 | no | 292.9 -> 293.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.20 | 1.41 | 0.07 | 1.68 | sigma same |
| level 7 (1528 coarse) + 5 GS | 1528 | no | 292.9 -> 287.8 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.67 | 1.13 | 0.09 | 1.90 | sigma same |
| level 7 (1528 coarse) smoothed P x1 + 5 GS | 1528 | no | 292.9 -> 287.5 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.68 | 1.11 | 0.17 | 1.95 | sigma same |
| level 7 (1528 coarse) smoothed P x2 + 5 GS | 1528 | no | 292.9 -> 287.2 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.76 | 1.16 | 0.15 | 2.06 | sigma same |
| level 8 (820 coarse) | 820 | no | 292.9 -> 293.1 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.17 | 1.10 | 0.08 | 1.35 | sigma same |
| level 8 (820 coarse) + 5 GS | 820 | no | 292.9 -> 287.9 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.90 | 1.09 | 0.09 | 2.07 | sigma same |
| level 8 (820 coarse) smoothed P x1 + 5 GS | 820 | no | 292.9 -> 288.2 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.85 | 1.10 | 0.07 | 2.03 | sigma same |
| level 8 (820 coarse) smoothed P x2 + 5 GS | 820 | no | 292.9 -> 288.4 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.79 | 1.40 | 0.09 | 2.28 | sigma same |

## big sphere, forced apply (gate=0)

### big-sphere (input V 120806, s 0.023, singular faces 8)

| variant | unknowns | applied | curl before -> after | irregular V | quads % | corner p95 | Hausdorff / s | spacing range | solve (s) | position (s) | extract (s) | total (s) | repeat bits |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Off | 0 | - | 0.0 -> 0.0 | 378 | 98.7 | 5.7 | 0.16 | 1.00-1.00 | 0.00 | 1.28 | 0.11 | 1.39 | - |
| library Scale (full, 2 unknowns / vertex) | 241612 | yes | 292.9 -> 223.8 | 283 | 99.0 | 5.2 | 0.16 | 0.47-1.02 | 14.91 | 16.10 | 0.08 | 31.09 | - |
<!-- big-sphere: hierarchy build 0.41 s (levels, colouring), level sizes 120806 63817 34095 18319 9851 5289 2843 1528 820 435 237 127 67 36 -->
| level 1 (63817 coarse) | 63817 | yes | 292.9 -> 274.1 | 346 | 98.8 | 5.7 | 0.16 | 0.64-1.01 | 0.88 | 1.09 | 0.09 | 2.06 | sigma same |
| level 1 (63817 coarse) + 5 GS | 63817 | yes | 292.9 -> 269.9 | 352 | 98.8 | 5.7 | 0.16 | 0.59-1.01 | 1.19 | 1.09 | 0.14 | 2.43 | sigma same |
| level 1 (63817 coarse) smoothed P x1 + 5 GS | 63817 | yes | 292.9 -> 245.1 | 327 | 99.0 | 5.5 | 0.16 | 0.50-1.02 | 6.13 | 1.02 | 0.07 | 7.21 | sigma same |
| level 1 (63817 coarse) smoothed P x2 + 5 GS | 63817 | yes | 292.9 -> 256.9 | 321 | 98.9 | 5.5 | 0.16 | 0.53-1.01 | 16.49 | 1.17 | 0.16 | 17.81 | sigma same |
| level 2 (34095 coarse) | 34095 | yes | 292.9 -> 281.7 | 358 | 98.9 | 5.9 | 0.16 | 0.69-1.01 | 0.53 | 1.09 | 0.09 | 1.71 | sigma same |
| level 2 (34095 coarse) + 5 GS | 34095 | yes | 292.9 -> 277.1 | 357 | 98.8 | 5.8 | 0.16 | 0.62-1.02 | 0.94 | 1.09 | 0.08 | 2.11 | sigma same |
| level 2 (34095 coarse) smoothed P x1 + 5 GS | 34095 | yes | 292.9 -> 256.0 | 324 | 98.9 | 5.6 | 0.16 | 0.52-1.02 | 2.21 | 1.03 | 0.07 | 3.31 | sigma same |
| level 2 (34095 coarse) smoothed P x2 + 5 GS | 34095 | yes | 292.9 -> 252.3 | 317 | 99.0 | 5.6 | 0.16 | 0.53-1.01 | 4.33 | 1.09 | 0.17 | 5.59 | sigma same |
| level 3 (18319 coarse) | 18319 | yes | 292.9 -> 287.3 | 373 | 98.7 | 5.8 | 0.16 | 0.80-1.01 | 0.28 | 1.18 | 0.08 | 1.53 | sigma same |
| level 3 (18319 coarse) + 5 GS | 18319 | yes | 292.9 -> 282.5 | 364 | 98.8 | 5.8 | 0.16 | 0.65-1.02 | 0.83 | 1.09 | 0.08 | 2.00 | sigma same |
| level 3 (18319 coarse) smoothed P x1 + 5 GS | 18319 | yes | 292.9 -> 266.0 | 351 | 98.8 | 5.7 | 0.16 | 0.55-1.02 | 1.18 | 1.04 | 0.11 | 2.32 | sigma same |
| level 3 (18319 coarse) smoothed P x2 + 5 GS | 18319 | yes | 292.9 -> 259.2 | 334 | 98.9 | 5.6 | 0.16 | 0.54-1.02 | 1.97 | 1.12 | 0.07 | 3.16 | sigma same |
| level 4 (9851 coarse) | 9851 | yes | 292.9 -> 290.3 | 363 | 98.7 | 5.6 | 0.16 | 0.87-1.01 | 0.20 | 1.22 | 0.08 | 1.50 | sigma same |
| level 4 (9851 coarse) + 5 GS | 9851 | yes | 292.9 -> 285.2 | 368 | 98.8 | 5.8 | 0.16 | 0.66-1.02 | 0.63 | 1.07 | 0.07 | 1.77 | sigma same |
| level 4 (9851 coarse) smoothed P x1 + 5 GS | 9851 | yes | 292.9 -> 274.1 | 348 | 98.8 | 5.7 | 0.16 | 0.58-1.02 | 0.77 | 1.25 | 0.06 | 2.08 | sigma same |
| level 4 (9851 coarse) smoothed P x2 + 5 GS | 9851 | yes | 292.9 -> 269.0 | 344 | 98.9 | 5.6 | 0.16 | 0.55-1.02 | 1.10 | 1.12 | 0.09 | 2.31 | sigma same |
| level 5 (5289 coarse) | 5289 | yes | 292.9 -> 292.0 | 393 | 98.7 | 5.9 | 0.16 | 0.94-1.01 | 0.23 | 1.21 | 0.10 | 1.54 | sigma same |
| level 5 (5289 coarse) + 5 GS | 5289 | yes | 292.9 -> 286.7 | 382 | 98.8 | 5.8 | 0.16 | 0.67-1.03 | 0.75 | 1.08 | 0.07 | 1.89 | sigma same |
| level 5 (5289 coarse) smoothed P x1 + 5 GS | 5289 | yes | 292.9 -> 281.2 | 359 | 98.8 | 5.6 | 0.16 | 0.62-1.03 | 0.68 | 1.10 | 0.27 | 2.05 | sigma same |
| level 5 (5289 coarse) smoothed P x2 + 5 GS | 5289 | yes | 292.9 -> 275.9 | 339 | 98.8 | 5.6 | 0.16 | 0.59-1.03 | 0.84 | 1.08 | 0.10 | 2.01 | sigma same |
