# SelfIntersectionBenchmark

Cost of the exact self-intersection checks, `Reducer.PreventSelfIntersections` and
`Remesher.PreventSelfIntersections`, on generic, tilted and axis-flat meshes. Every case prints the time and a
fingerprint of the result (FNV-1a over every vertex coordinate bit and every triangle), so two builds can be checked
for bit-identical results.

```
dotnet run -c Release --project benchmarks/SelfIntersectionBenchmark -- [reducer] [remesher] [runs=3] [only=<regex>]
    [thingi=<file>] [thingi_passes=5] [thingi_runs=1]
```

Build with `-p:G3PredicateStats=true` to also print the Orient2d / Orient3d calls and the share that reaches their
exact stage (slower: the counters are interlocked).

To compare two commits, build the benchmark in a `git worktree` of each (copy this directory and the
`InternalsVisibleTo` line of `geometry3Sharp_netstandard.csproj` into an older one) and run them one after another,
alone on the machine.

## Cases

Generated in the code (no mesh files):
- **reducer_harness_\***: the four cases of the review harness of #239, constructed exactly as given there: a plate of
  n x n unit cells in z = 0 (`HarnessPlate`, jitter = the full width, boundary vertices not moved across the
  boundary), optionally rotated by 37 degrees about (1, 2, 3) (`HarnessTilt`), reduced to a tenth of its triangles.
- **reducer_jitter_plate_z0**: 50 x 50 unit cells in z = 0, interior vertices moved by up to +-0.3 in x and y, reduced
  5000 -> 500 (`_to50`: -> 50).
- **reducer_jitter_plate_tilted**: the same plate rotated out of every axis plane (37 degrees about (1, 2, 3)).
- **reducer_integer_plate**: 100 x 100 unit cells with integer coordinates in z = 0, 20000 -> 2000.
- **reducer_bumpy_sphere**: a generic curved mesh (`Sphere3Generator_NormalizedCube`, 40 vertices per edge, bumps),
  18252 -> 1000.
- **reducer_box_grid**: a closed box with regular face grids (`GridBox3Generator`, 30 vertices per edge), every triangle
  in an axis plane, 10092 -> 600.
- **remesher_\***: the Remesher with the settings of `g3cli remesh` (smoothing 0.5, projection onto the input, boundary
  and 30 degree creases kept), target edge length = the mean edge length of the input, 10 passes, without and with
  (`_si`) `PreventSelfIntersections`, on the bumpy sphere, the z = 0 and tilted plates (40 x 40) and the box grid.
- **thingi=\<file\>**: a mesh read at run time, the Remesher as above for `thingi_passes` passes. Thingi10K 135363, for
  example, is fetched with

  ```
  curl -L -o <dir>/135363.stl https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/135363.stl
  ```

  (Thingi10K models carry their own licenses: never commit them.)

## Where the time goes (before the filter)

Reducer, `-p:G3PredicateStats=true` and `dotnet-trace` (cpu sampling), branch `fix/reducer-closed-box` before
`TriangleTriangleFilter`:

| case | Orient3d calls | of them exact stage | Orient2d calls | of them exact stage | profile |
|---|---:|---:|---:|---:|---|
| jitter plate z = 0 | 1442280 | 1442280 (100 %) | 12226245 | 969802 | about 40 % of the check in the exact Orient3d stage (all results exactly 0), about 40 % in the grid query |
| jitter plate tilted | 99653263 | 72009245 (72 %) | 38147713 | 5138 | about 70 % of the check in the exact Orient3d stage (nearly coplanar neighbours), 12 % in the grid query |
| integer plate | 7183921 | 7183921 (100 %) | 62034218 | 8019232 | as z = 0 |
| box grid | 39729382 | 9597152 (24 %) | 58445612 | 14422708 | exact Orient3d and the 3D tests of axis-flat pairs |
| bumpy sphere | 3324432 | 0 | 1031696 | 0 | the filters of the predicates decide everything |

So the hypothesis "an exact orientation of 0 takes the slow path" holds for the axis-flat meshes (every Orient3d of
two triangles in one axis plane is exactly 0 and goes to the exact stage). The tilted plate is the more expensive
case: its Orient3d values are not 0 but within the filter's error bound.

With the filter (`intersection/TriangleTriangleFilter.cs`), the same runs: z = 0 plate 0 Orient3d calls (1793433
Orient2d, 1220 exact), tilted plate 799 Orient3d calls (135059530 Orient2d, 157685 exact), integer plate 0 Orient3d
calls. What is left on the tilted plate is the number of candidate pairs: 7.7 million pairs from the grid for 2372
checked collapses, 40 times the z = 0 plate (190363 for 2323), as the reduction of the tilted plate (quadric errors
of rounding noise, not exactly 0) makes long thin triangles with large boxes.

## Results

Linux container (4 cores), `net10.0`, Release, alone on the machine, minimum of 3 runs (all runs in the last column of
the raw output). master = 62f0f2b; box fix = `fix/reducer-closed-box` before the filter; filter = with
`TriangleTriangleFilter` in the Reducer. The fingerprints are identical in all three builds for every case.

| case | triangles | master | box fix | filter | filter / master |
|---|---|---:|---:|---:|---:|
| jitter plate z = 0 | 5000 -> 500 | 272 ms | 748 ms | 155 ms | 0.57 |
| jitter plate z = 0 | 5000 -> 50 | 270 ms | 783 ms | 188 ms | 0.70 |
| jitter plate tilted | 5000 -> 500 | 49636 ms | 52695 ms | 5271 ms | 0.11 |
| jitter plate tilted | 5000 -> 50 | 50608 ms | 51356 ms | 5783 ms | 0.11 |
| integer plate | 20000 -> 2000 | 1099 ms | 4014 ms | 591 ms | 0.54 |
| bumpy sphere | 18252 -> 1000 | 540 ms | 785 ms | 509 ms | 0.94 |
| box grid | 10092 -> 600 | 4944 ms | 8237 ms | 3165 ms | 0.64 |

### The cases of the review harness of #239

Same setup, `reducer runs=3 only=harness`. Fingerprints identical in all three builds.

| case | triangles | master | box fix | filter | filter / master | harness of #239 (other machine, not alone): master / box fix |
|---|---|---:|---:|---:|---:|---|
| `HarnessPlate(50, 0.5)` | 5000 -> 500 | 13743 ms | 94749 ms | 7451 ms | 0.54 | 10.372 s / 77.262 s |
| `HarnessTilt(HarnessPlate(50, 0.5))` | 5000 -> 499 | 20023 ms | 20914 ms | 2356 ms | 0.12 | 12.754 s / 14.384 s |
| `HarnessTilt(HarnessPlate(50))` | 5000 -> 499 | 49180 ms | 47294 ms | 6289 ms | 0.13 | 33.151 s / 37.544 s |
| `HarnessPlate(100)` | 20000 -> 1999 | 1316 ms | 3563 ms | 554 ms | 0.42 | 0.872 s / 3.211 s |

Exact stages with the filter (`-p:G3PredicateStats=true`, one run): `HarnessPlate(50, 0.5)` 0 Orient3d calls,
191058999 Orient2d (0.2 % exact); the tilted plates 7071 and 2239 Orient3d calls, 60.9 and 140.3 million Orient2d
(0.0 and 0.1 % exact); `HarnessPlate(100)` 0 Orient3d calls. What remains is the number of candidate pairs from the
grid (dotnet-trace on `HarnessPlate(50, 0.5)`: the filter about half of the check's samples, the grid query most of the rest): the box fix sends every
pair with touching or overlapping closed boxes to the tests, and the reductions make long thin triangles. Not
addressed here.

The Remesher follows.
