# RemeshThinWallSliverStudy (measurement only, no library change)

Linux container, 4 cores, net10.0 Release, library master c57dd5d2, g3cli remesh settings (20 passes, smoothing 0.5, projection,
30 degree creases, `MinTriangleQuality` 0.2 in every run). Thingi10K meshes fetched by `benchmarks/RemeshBenchmark/fetch_data.py`.

```
dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=thin [extra=1] [c=1,1.5,2] [wtf=3,4] [only=<regex>]
dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=big [run=1 rounds=2 c=1,2 haus=1] [wtfrun=2,3,4]
dotnet run -c Release --project benchmarks/RemeshThinWallSliverStudy -- mode=slivers [dev=0.03,0.1,0.3] [depth=3]
```

## A. Size rule L = min(target, c sqrt(r t))  (r = wall curvature radius, t = thickness)

Procedural shells (known t and r): plate (r = inf), tube (r = 1), bent tube (tight quarter torus, r = 0.25..0.5), t 0.025..0.1,
targets 5..30 t. Result: volume ratio / new self-intersection pairs.

| config | tube r/t 20 and 10 | bend r/t 5..20 | triangles vs FromWallThickness(1) |
|---|---|---|---|
| default, si | folds (vol 0.15..2.8, up to 821 pairs); si: 0 pairs, vol 0.7..8.6 | same | 0.01..0.03x |
| FromWallThickness(1) | vol 1.000, 0 pairs | vol 1.000, 0 pairs | 1x (49312 / 12816 / 35346 / 9818 tris) |
| rule c=1 | vol 0.998..1.006, 0 pairs, clean | vol 1.000..1.001, 0 pairs, clean | 0.05 / 0.09 / 0.05 / 0.10x |
| rule c=1.5 | fails in 6 of 8 cases (vol 0.24..0.78, 38..876 pairs) | | |
| rule c=2 (the hypothesis) | fails (vol 0.13..0.17, 427..821 pairs) except where the global target is below the rule | | |

- The flat plate never folds (rim creases are constrained), so it does not discriminate.
- Uniform `FromWallThickness(f)` at target 10 t: ok up to f = 5 (tube t 0.05, bend t 0.025), 4 (tube t 0.1), 3 (bend t 0.05, r/t 5);
  fails one step later. So the safe edge is about 3..5 t, and about 1.1..1.3 sqrt(r t); not 2 sqrt(r t).
- Thinner walls (r/t 100, 400; `extra=1`): rule c=1 allows L = 7..20 t and fails (tube r/t 100: vol 0.994 but 53 new pairs; r/t 400:
  vol 0.39, 30091 pairs; bend t 0.005: vol 0.50). Default fails as well.
- Predicted count (area x 4/(sqrt3 L^2) over the input) is 0.8..0.9x the actual count on the procedural bodies.

### 135363 (target 11.6, wall 0.75, 2612 input triangles, area 6945)

Thickness (area quantiles) 5 %: 0.75, 50 %: 0.86, 95 %: 2.79. Feasibility (unique mutual opposite side, ray along -n, antiparallel within 30 deg):

| distance < m t | mutual, return within 0.25 d | within 0.5 d | strict (same triangle) |
|---:|---:|---:|---:|
| 1.5 | 52 % | 57 % | 49 % |
| 2 | 56 % | 61 % | 49 % |
| 3 | 57 % | 64 % | 50 % |

(68 % of the area has an antiparallel first hit; 100 % has a hit.) About 40 % of the area has no unique mutual opposite side within 3 t.

| run (135363) | tris (predicted) | volume | new pairs | haus/target | s |
|---|---:|---:|---:|---:|---:|
| FromWallThickness(1), earlier PR | 1,159,814 (548,616) | 1.001 | 17,916 | 0.04 | 322 |
| FromWallThickness(2) | 1,105,276 (472,298) | 1.001 | 22,887 | 0.70 | 245 |
| FromWallThickness(3) | 1,394,734 (447,883) | 1.011 | 32,998 | 1.78 | 280 |
| FromWallThickness(4) | 1,117,508 (433,856) | 1.025 | 33,693 | 2.33 | 276 |
| rule c=1, curvature raw (rounds 0) | 144,886 (22,601) | 1.017 | 14,360 | 0.73 | 79 |
| rule c=2, raw | 55,248 (7,210) | 0.784 | 5,935 | 2.89 | 43 |
| rule c=1, 2 smoothing rounds | 19,964 (6,292) | 0.899 | 1,471 | 2.55 | 9 |
| rule c=2, 2 rounds | 8,022 (1,574) | 1.450 | 1,581 | 7.13 | 2 |

The count of the thickness fields hardly depends on f: it is dominated by surface where t is at the 1e-3 diagonal floor
(predicted 434k at f = 4 against 549k at f = 1). The rule with a curvature estimated from the 2612-triangle input is not usable
(counts 3..6x the prediction, wall lost).

## B. Pinned slivers (10 crease cases, robust setting, sliver = a triangle below 0.5 or above 170 degrees)

891 slivers, 781 with 2+ pins (3 pins: 304 of them). Search: pins, feature curves and patch boundary fixed; (a) free corner moved
(sampled in its star, projected on the input surface / its curve), (b) edge flips (depth 3) + vertex removal with best polygon
triangulation, 1-ring and 2-ring patch, (c) both. Score = worst min angle over the changed triangles (the sliver counts while it
stays). A new triangle must keep its orientation and its centroid within the guard x target of the input surface.
Share of the 781 slivers (>= 10 / >= 20 degrees):

| guard | (a) move | (b) flips + removal, 1 ring | (b) 2 rings | (c) both, 2 rings | best of all |
|---|---|---|---|---|---|
| 0.03 target | 0 / 0 % | 2 / 1 % | 2 / 1 % | 3 / 1 % | 3 / 1 % |
| 0.1 target | 0 / 0 % | 8 / 1 % | 8 / 1 % | 8 / 2 % | 8 / 2 % |
| 0.3 target | 0 / 0 % | 8 / 1 % | 8 / 2 % | 8 / 2 % | 8 / 2 % |

Per case (guard 0.1): best >= 10 / >= 20: 104582 10 / 1 %, 125069 15 / 6 %, 135363 1 / 0, 196126 6 / 0, 105686 0 / 0, 78295 0 / 0,
93370 42 / 17 %, 299291 0 / 0, 39460 7 / 0, 1053376 0 / 0. Search time 191 s in total (4 cores, serial).

Remaining (best < 20) by type, guard 0.1: 2 pins 0.1..0.3 target 342 (44 %), 3 pins 304 (39 %), 2 pins < 0.1 target 55 (7 %),
free corner on a curve 36 (5 %), crease wedge < 20 degrees at a pin 29 (4 %), other 2.

| pin pair distance / target | slivers | best >= 10 | best >= 20 |
|---|---:|---:|---:|
| < 0.05 | 198 | 0 | 0 |
| 0.05..0.1 | 10 | 1 | 0 |
| 0.1..0.3 | 464 | 41 | 6 |
| 0.3..0.6 | 46 | 7 | 3 |
| >= 0.6 | 63 | 14 | 4 |

Caveats: the flip search is depth 3 (capped 20000 states), the move search is a grid plus refinement, not an exact optimum; the
projection of curve vertices uses the remesher's own curve targets.
