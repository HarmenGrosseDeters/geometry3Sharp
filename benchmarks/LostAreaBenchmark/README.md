# LostAreaBenchmark

The cost of the lost-area report of `MeshRobustRepair` (`MeasureLostArea`, #1479) against the repair itself, on procedural inputs (no mesh
files): `Apply()` with the report off and on, in fresh copies of the same input, alternating; the first round is the warm-up; the columns are
the medians over the repetitions (default 7, `-- 9` for 9) and the median of the paired difference.

    dotnet run --project benchmarks/LostAreaBenchmark -c Release -- 9

Inputs: the unit box with a slab through it from wall to wall (24 triangles, two interior walls, exact resolve), a sphere with a hole
(1.4k and 99k triangles, the plain repair closes the hole), two overlapping unit spheres of 11.5k triangles each (exact resolve).

## Results (net10.0, 2026-11-24, a shared Windows machine under load, 9 repetitions; the report is the library's with the area-based sampling and the distance from the nearest point; an earlier run on a quieter machine gave 14 to 97 %)

| input | triangles | path | repair ms | with report ms | extra ms | extra % | lost share |
|---|---:|---|---:|---:|---:|---:|---:|
| box with an interior wall | 24 | ExactResolve | 38.2 | 44.8 | 9.6 | 25.0 | 0.238 |
| sphere with a hole | 1450 | Repair | 63.4 | 127.3 | 28.0 | 44.1 | 0 |
| two overlapping spheres | 23064 | ExactResolve | 440.6 | 955.3 | 483.8 | 109.8 | 0.200 |
| sphere with a hole | 99110 | Repair | 753.5 | 1052.1 | 299.8 | 39.8 | 0 |

The report costs a copy of the input, a tree of the result and about 8 to 16 nearest-triangle queries per input triangle (at most 500k
queries in all, more only for triangles of more than the mean area): 25 to 110 % of the repair's time on these inputs, so it is opt-in. The
exact resolve of a clean-ish input is fast, which makes the same report relatively dearer there. Timings are noisy on a shared machine
(the paired difference is the more stable number); halving `MeshRobustRepairLostArea.SamplesTarget` roughly halves the report's cost.
