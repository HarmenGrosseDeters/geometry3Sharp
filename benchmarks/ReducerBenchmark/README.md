# ReducerBenchmark

Cost of the `Reducer`'s certified deviation check (`MaxDeviation`, `TriangleDeviationBounds`). Each mesh is
reduced twice:
- **with check**: with `MaxDeviation`, until no collapse stays within the tolerance;
- **without check**: without it, to the same triangle count.

The difference in time is the most a faster containment check could save. The deviation is the certified
one-sided Hausdorff distance from the result to the input (`MeshHausdorffDistance`, upper bound, to 1 % of the
tolerance).

```
dotnet run -c Release --project benchmarks/ReducerBenchmark
```

Meshes (generated in the code, no mesh files):
- **bumpy sphere**: a unit sphere (`Sphere3Generator_NormalizedCube`, 130 vertices per edge) with smooth bumps.
- **terrain**: a 300 x 300 heightfield on the unit square, with hills and two ripple frequencies.

The tolerance is relative to the bounding box diagonal. Run on a Linux container, `net10.0`, Release.

## Results (October 2026)

| mesh | triangles | tolerance | with check: triangles | time with check | time without, same count | check share | deviation without check |
|---|---|---|---|---|---|---|---|
| bumpy sphere | 199692 | 0.001 | 1670 | 34476 ms | 1073 ms | 97 % | 1.68 x tolerance (with check: 1.01) |
| bumpy sphere | 199692 | 0.003 | 512 | 17813 ms | 934 ms | 95 % | 1.82 x tolerance (with check: 1.01) |
| bumpy sphere | 199692 | 0.01 | 152 | 12821 ms | 1038 ms | 92 % | 1.55 x tolerance (with check: 1.00) |
| terrain | 180000 | 0.001 | 3123 | 24206 ms | 963 ms | 96 % | 2.83 x tolerance (with check: 1.01) |
| terrain | 180000 | 0.003 | 923 | 13522 ms | 905 ms | 93 % | 2.35 x tolerance (with check: 1.01) |
| terrain | 180000 | 0.01 | 121 | 11808 ms | 950 ms | 92 % | 3.28 x tolerance (with check: 1.00) |

Findings:
- The check takes 92-97 % of the reduction time: reducing with it is 12-32 times slower.
- It is needed. Without it, the same triangle count deviates 1.5-3.3 times the tolerance.
- With it, the result stays within the tolerance. 1.01 is the 1 % accuracy of the measurement.

## Exact envelope instead of the distance bounds

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- queries
```

Compares the cost of one query: the triangles of a reduction with the distance bounds check are checked again,
with `TriangleDeviationBounds.IsWithin` and with `MeshEnvelope.ContainsTriangle` at the same tolerance. These
are the triangles the envelope would have to accept if it replaced the check.

Terrain 40 x 40 (3200 triangles), tolerance 0.003, 951 reduced triangles:

| check | within | time per triangle |
|---|---|---|
| distance bounds (`IsWithin`) | 951 | 62 µs |
| envelope (`ContainsTriangle`) | 625 | 7109 µs (728 exact) |

Findings:
- Reduced triangles span many prisms, and no single prism holds them. The floating-point filters cannot decide,
  so the exact clipping runs, about 115 times slower than the distance bounds.
- The envelope accepts fewer triangles, because its prisms are thinner than the tolerance ball.
- An earlier try of the envelope as the `Reducer`'s check (not kept) took 91 s instead of 0.84 s on this
  terrain. Sampling points on the triangle first rejected only a few of the queries.

## Profiling the distance bounds check

Terrain of the same kind, 150 x 150 cells (45000 triangles), tolerance 0.003, reduced to 903 triangles in
about 5.3 s:

- 94 % of the checked collapses are accepted (22300 of 23670). Each acceptance certifies the 6-8 new triangles
  around the vertex: 155800 `IsWithin` calls, 1.36 million nearest-triangle searches, about 34 µs per call.
- Of the reduction time, `IsWithin` takes 74 %: the polygon bounds 31 %, nearest-triangle searches 30 %,
  separating planes 6 %.

Tried, without a gain (same result, 903 triangles):

| change | time |
|---|---|
| none (parallel check of the new triangles) | 5.3 s |
| serial check with early exit | 2.8 s instead of 2.5 s (20000 triangles): slower |
| `MaxDeviationDepth` 8 / 6 / 4 instead of 10 | 5.2 / 5.5 / 5.5 s (depth 4: 913 triangles) |
| cache of the nearest-triangle searches by point | 5.8-6.3 s |

The check is not slow because of deep subdivision or repeated searches, but because every accepted collapse
certifies all its new triangles. A large speedup would need a different certification. The exact envelope is
not that either: even with floating-point filters in its clipping, about 20-50 µs per triangle can be expected
(estimate, not measured). Its gain would be exactness and platform independence, not speed.
