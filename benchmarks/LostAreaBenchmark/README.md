# LostAreaBenchmark

The cost of the lost-area report of `MeshRobustRepair` (`MeasureLostArea`, #1479) against the repair itself, on procedural inputs (no mesh
files): `Apply()` with the report off and on, in fresh copies of the same input, alternating; the first round is the warm-up; the columns are
the medians over the repetitions (default 7, `-- 9` for 9) and the median of the paired difference.

    dotnet run --project benchmarks/LostAreaBenchmark -c Release -- 9

Inputs: the unit box with a slab through it from wall to wall (24 triangles, two interior walls, exact resolve), a sphere with a hole
(1.4k and 99k triangles, the plain repair closes the hole), two overlapping unit spheres of 11.5k triangles each (exact resolve).

## Results (net10.0, 2026-11-24, a shared Windows machine, 9 repetitions; the report is the library's with the area-based sampling)

| input | triangles | path | repair ms | with report ms | extra ms | extra % | lost share |
|---|---:|---|---:|---:|---:|---:|---:|
| box with an interior wall | 24 | ExactResolve | 15.0 | 18.3 | 2.1 | 13.9 | 0.238 |
| sphere with a hole | 1450 | Repair | 57.4 | 70.9 | 15.3 | 26.6 | 0 |
| two overlapping spheres | 23064 | ExactResolve | 270.1 | 538.8 | 261.9 | 97.0 | 0.200 |
| sphere with a hole | 99110 | Repair | 660.0 | 852.7 | 184.7 | 28.0 | 0 |

The report costs a copy of the input, a tree of the result and about 8 to 16 nearest-triangle queries per input triangle (at most 500k
queries in all, more only for triangles of more than the mean area): 14 to 97 % of the repair's time on these inputs, so it is opt-in. The
exact resolve of a clean-ish input is fast, which makes the same report relatively dearer there. Timings are noisy on a shared machine
(the paired difference is the more stable number); halving `MeshRobustRepairLostArea.SamplesTarget` roughly halves the report's cost.
