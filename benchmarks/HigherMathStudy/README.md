# HigherMathStudy: probes of the three "higher mathematics" ideas

The probes of the research note [docs/research/HIGHER_MATH_STUDY_2026-11.md](../../docs/research/HIGHER_MATH_STUDY_2026-11.md):
an integral-geometry certificate for the exact Booleans, multi-modular zero tests, and triangle-soup orientation as Z2
synchronization. Nothing here is library code, and nothing here is a gate. The inputs are procedural. The note has the method,
the tables and the options. This file has the commands and the layout.

```
dotnet build benchmarks/HigherMathStudy -c Release
cd benchmarks/HigherMathStudy
dotnet bin/Release/net10.0/HigherMathStudy.dll valuation > results/valuation.txt
dotnet bin/Release/net10.0/HigherMathStudy.dll bigint 1 > results/bigint_cases.txt
G3_Z2_ITERATIONS=3000 dotnet bin/Release/net10.0/HigherMathStudy.dll orient > results/orient.txt
```

The BigInteger profile (idea 2) uses `dotnet-trace` (`dotnet tool install -g dotnet-trace`):

```
dotnet-trace collect --profile dotnet-sampled-thread-time -o hm_degenerate.nettrace -- dotnet bin/Release/net10.0/HigherMathStudy.dll bigint 5 degenerate
dotnet-trace convert --format speedscope -o hm_degenerate hm_degenerate.nettrace
python3 fold_speedscope.py hm_degenerate.speedscope.json          # the BigInteger share
python3 fold_speedscope.py hm_degenerate.speedscope.json --top    # the hotspots (innermost g3 frame)
```

(the same with `normal` for the general-position stratum). The zero share of `orient3d`'s exact stage needs temporary counters
in the library: apply `results/orient3d_zero_counters.patch` to `math/ExactPredicates.cs`, build with `-p:RunAnalyzers=false`
(the counters use `Stopwatch`, which the determinism analyzer refuses there), run with `G3_PROBE_ORIENT=1 ... bigint 1 degenerate`,
and revert the patch. It was never committed to the library.

Measured 2026-11-25 in a Linux container, 4 cores (Xeon 2.1 GHz), .NET 10.0.115, single runs. `valuation` takes about 1 minute,
`bigint 1` about 6 s, `orient` a few seconds (30 s with `G3_Z2_ITERATIONS=30000`).

## Layout

| file | what |
|---|---|
| `Valuation.cs` | Idea 1. Runs Union and Intersection with `ComputeExactVertices` and `SafeRounding = false`. Checks V(U) + V(I) = V(A) + V(B) exactly, with rational volumes (divergence theorem, a generic dyadic apex). Checks χ likewise. Injects 7 defects into the exact union and reports which ones the identity catches |
| `BigIntWorkload.cs` | Idea 2. The profiled workload: two spheres in general position (stratum normal); coplanar boxes and the 9 `DegenerateFamilies` at 2^-20 and 2^-40, each against a shifted copy and against itself (stratum degenerate) |
| `fold_speedscope.py` | Folds a speedscope thread-time profile into the inclusive BigInteger share of `ExactMeshBoolean.Compute`, and lists the hotspots |
| `Orientation.cs` | Idea 3. Unwelded procedural soups with known flips (#1548's PolyCube(4,3,2) at k = 5, 3, 2 and six noisier variants). A signed triangle graph at a weld tolerance, with all-pairs or radial-neighbour links at non-manifold edges. BFS, Z2 spectral synchronization, the library's weld + `MeshRepairOrientation`, `MeshAutoRepair` and the steps of `WindingContouring.OrientedCopy`, each scored by the area share oriented correctly |
| `results/` | the raw outputs quoted in the note, and the temporary counter patch |
