# Marching-cubes ordered batching

Bounded procedural comparison for the S31 ordering fix in PR #365. The existing benchmarks did not directly
exercise `MarchingCubes`, so this executable measures that generator without importing mesh assets or adding
packages. It measures `Generate()` only; correctness checks and explicit collections happen outside the timer.
The allocation counter includes allocations by worker threads.

Cases:
- `sphere`: a cheap, approximately uniform field.
- `skewed`: a sphere perturbed by a deterministic trigonometric field whose upper band is more expensive, to
  exercise the wait for the slowest slab in a batch.

Each mode warms up once, then prints each sample as CSV. A serial reference supplies an exact, ID-independent
SHA-256 digest of oriented triangles. Every result must be closed, valid, finite and match that digest. The CSV
also reports whether the vertex/triangle IDs match serial output; this is expected to be false for the old
parallel implementation and true for the ordered one. Root mode is the default SingleLerp; the tests cover
all root-finding modes.

## Command and comparison boundary

```powershell
dotnet build benchmarks\MarchingCubesBenchmark -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks\MarchingCubesBenchmark -c Release --no-build -- 48 3
```

Arguments are the nominal cells across the box and repeats. The actual cell count retains the generator's
`floor(extent / CubeSize) + 1` rule. Run a small sample first; defaults are bounded. Disable tiered compilation
for both versions to avoid comparing different warm-up/JIT stages.

The checked-in measurement used the same harness, build mode and machine in **before / after / after / before**
order, rebuilding between versions. `before` uses only `mesh_generators/MarchingCubes.cs` from
`99f9bd3d207f8a7906212e0300a17456f3d7219c`; `after` uses the ordered batching and corrected per-axis dimension
validation in this PR. The source bytes were saved and restored in `finally` around these temporary substitutions.
No other implementation file was changed for the comparison. To reproduce without modifying a working tree,
copy this benchmark into a separate checkout of the baseline and run the same command in both checkouts.

## Observed results

Raw command output: [before-1.csv](results/before-1.csv), [after-1.csv](results/after-1.csv),
[after-2.csv](results/after-2.csv), [before-2.csv](results/before-2.csv).
Runtime/OS/processor count and commands: [environment.txt](results/environment.txt).
Medians below pool the six samples per case/mode from that command; time ranges are shown because this Windows
machine is shared, not a dedicated performance runner. These are not CI performance thresholds.

| Field | Mode | Before median ms (range) | After median ms (range) | Before allocated bytes | After allocated bytes |
|---|---|---:|---:|---:|---:|
| sphere | serial | 13.890 (12.508–17.369) | 12.338 (10.917–15.925) | 2,120,792 | 2,120,792 |
| sphere | parallel | 73.772 (46.334–110.320) | 10.236 (9.291–13.730) | 2,193,760 | 5,443,708 |
| skewed | serial | 152.691 (134.313–201.618) | 174.335 (158.049–237.274) | 2,121,080 | 2,121,080 |
| skewed | parallel | 89.677 (58.530–107.042) | 62.143 (56.199–86.912) | 2,196,208 | 5,444,032 |

The tested parallel cases do not show a batching throughput regression, but this is not a guarantee for other
fields or resolutions. The serial skewed case also varies despite using the same traversal, illustrating why
these measurements must not be extrapolated. Ordered publication visibly costs more temporary allocation;
the batches bound the number of buffered slabs, not their surface complexity. A sufficiently imbalanced field
can still leave workers waiting at a batch boundary. No additional algorithmic optimization is included here.
