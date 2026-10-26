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

## Experiment: exact envelope instead of the distance bounds

`MeshEnvelope` (exact polyhedral envelope, clipping the query against the prisms) was tried as the check (not
kept). Test case: 40 x 40 terrain (3200 triangles) of the same kind, tolerance 0.003.

| check | result triangles | time |
|---|---|---|
| distance bounds | 283 | 0.84 s |
| envelope | 370 | 91 s (10553 exact queries) |

Reduced triangles span many prisms. No single prism holds them, so the floating-point filters cannot decide,
and the exact clipping costs 40-55 ms per query. Sampling points on the triangle first rejected only a few of
the queries.
