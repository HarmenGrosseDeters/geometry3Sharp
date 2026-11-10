# RemeshPreventBenchmark

Time and result hash of `Remesher` with `PreventSelfIntersections` (20 passes, smoothing 0.5, projection, 30 degree
creases, as `g3cli remesh`) on procedural tubes (no mesh files). A speedup must give the same mesh bit for bit, so each
row carries a hash of the result (vertex coordinates as raw bits, triangles in index order); a run whose repeats differ
stops with an error, and two builds are compared by their hash columns.

```
dotnet run -c Release --project benchmarks/RemeshPreventBenchmark [repeats]
```

The first run of a case is cold (JIT) and is reported separately from the median of the others. Linux container,
4 cores, `net10.0`, Release; timings vary by about 10 % between runs.

## Cell size of the triangle grid (2026-11-10)

The self-intersection check keeps the triangles in a `TriangleHashGrid` whose cells were as large as the *target*
edge length (`MaxEdgeLength`), but not below the mean edge. With a sizing field (`RemeshSizing.FromWallThickness`) the
target is the global upper bound (0.8 here) while the triangles of a thin wall are 0.05-0.1 long: a cell held hundreds
of triangles and every query visited all of them (`TriangleHashGrid.Query` was 60 % of the CPU time, 26 % of it in
`visit`). The cell is now at most twice the mean edge. The queries' answers do not depend on the cell size (only which
candidates they visit), so the result is the same bit for bit; a triangle larger than 16 cells goes to a coarser grid
level as before.

| case | hash (before = after) | cold ms before | cold ms after | warm ms before | warm ms after |
|---|---|---|---|---|---|
| thin tube (wall 0.1), wall-thickness sizing, target 0.8 | `C4EC64E5A82DC245` | 4623 | 3457 | 3728 | 2208 |
| thin tube (wall 0.1), uniform target 0.2 | `C177081A8E85E433` | 354 | 330 | 348 | 343 |
| thick tube (wall 0.5), uniform target 0.3 | `CEECC659975E91B1` | 122 | 117 | 109 | 115 |
| thick tube (wall 0.5), uniform target 1.5 (coarsening) | `B5974F46CA01A4D7` | 43 | 36 | 41 | 29 |
| thin tube, uniform target 0.2, check off | `03362745616D38E7` | 65 | 66 | 66 | 81 |

After the change `Query` is 12 % of the CPU time (was 60 %); the rest is flat (ProcessEdge 24 %, vertex move
check 14 %, smoothing 8 %, projection 7 %).
