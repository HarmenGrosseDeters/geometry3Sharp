# Biharmonic displacement benchmark (#886)

The procedural grid fixes the left boundary and displaces the right boundary in-plane and sinusoidally out of plane. The timer includes validation, assembly, factorization, the three solves, residual check and output copying; grid generation is outside it. The program checks finite output and exact handles. The allocation figure is cumulative managed allocation, not peak memory. The hash covers vertex IDs/positions and triangle IDs/indices, not mesh attributes.

Run on Linux x64, .NET SDK 10.0.403, 2026-11-15, base `41e6c0f0` with this implementation. One run per size, with environment variable `DOTNET_GCHeapHardLimit=0xC0000000`; raw output in `results.txt`:

| Grid | Status | Time | Peak process working set |
| --- | --- | ---: | ---: |
| 100 x 100 | Success | 498.5 ms | 114 MB |
| 400 x 250 | Success | 4193.1 ms | 852 MB |

```
dotnet run --project benchmarks/BiharmonicDeformationBenchmark -c Release -- 100 100
dotnet run --project benchmarks/BiharmonicDeformationBenchmark -c Release -- 400 250
```

These are single measurements of this mesh and include one direct solve, not a general speed comparison with ARAP. The explicit matrix and factorization can require considerably more memory for other topologies; the assembly contribution cap does not limit factorization fill.
