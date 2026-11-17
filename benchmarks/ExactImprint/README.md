# ExactImprint benchmark

Cost of `ExactMeshImprint` against `ExactMeshBoolean` on the same procedural pairs (design: [EXACT_IMPRINT.md](../../docs/design/EXACT_IMPRINT.md) section 6).
No mesh files: two spheres (`Sphere3Generator_NormalizedCube`), the cutter offset or small.

```
dotnet run -c Release --project benchmarks/ExactImprint -- bound 32 64 128        # the boolean's phases (upper bound of the saving)
dotnet run -c Release --project benchmarks/ExactImprint -- compare 5 32 64 128    # imprint against boolean, median of 5 runs
```

Results of 2026-11-16 (a cloud container, cores not pinned, .NET 10; wall milliseconds, median of 5 after a warm-up):

| case | triangles | boolean | imprint | imprint / boolean | imprint without rounding | pieces | curve edges |
|---|---|---|---|---|---|---|---|
| offset sphere | 23 064 | 89 | 52 | 0.58 | 36 | 12 596 | 532 |
| offset sphere | 95 256 | 197 | 89 | 0.45 | 67 | 49 796 | 1 084 |
| offset sphere | 387 096 | 700 | 374 | 0.53 | 215 | 197 916 | 2 184 |
| small sphere | 23 064 | 41 | 22 | 0.54 | 14 | 12 224 | 346 |
| small sphere | 95 256 | 140 | 78 | 0.56 | 53 | 49 036 | 703 |
| small sphere | 387 096 | 627 | 273 | 0.44 | 243 | 196 388 | 1 415 |

The boolean's phases on the offset pair (`bound`, one run, ms: total, setup + intersections + merge, triangulation, classification, simplification, rounding):
23 064 triangles 147, 63, 7, 9, 3, 36; 95 256: 522, 136, 20, 39, 8, 242; 387 096: 1235, 530, 51, 155, 27, 218.
Classification is 6 to 13 % of the boolean on closed pairs, so the saving is mostly the skipped self-join of the operands, the cutter's
triangulation and the restricted rounding. On open inputs, where classification is 66 to 82 % of the slow tail, it is larger (not measured here).
Single runs on a shared container: read the ratios, not the milliseconds.
