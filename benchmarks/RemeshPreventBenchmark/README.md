# RemeshPreventBenchmark

Time and result hash of `Remesher` with `PreventSelfIntersections` (20 passes, smoothing 0.5, projection, 30 degree
creases, as `g3cli remesh`) on procedural tubes (no mesh files). A speedup must give the same mesh bit for bit, so each
row carries a hash of the result (vertex coordinates as raw bits, triangles in index order); a run whose repeats differ
stops with an error, and two builds are compared by their hash columns.

```
dotnet run -c Release --project benchmarks/RemeshPreventBenchmark [repeats]
```

The first run of a case is cold (JIT) and is reported separately from the median of the others. Linux container,
4 cores, `net10.0`, Release (numbers measured on top of master f3f3f50b); timings vary by about 10 % between runs.

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
| thin tube (wall 0.1), wall-thickness sizing, target 0.8 | `C4EC64E5A82DC245` | 4592 | 3298 | 3623 | 2156 |
| thin tube (wall 0.1), uniform target 0.2 | `C177081A8E85E433` | 331 | 365 | 326 | 308 |
| thick tube (wall 0.5), uniform target 0.3 | `CEECC659975E91B1` | 105 | 105 | 107 | 106 |
| thick tube (wall 0.5), uniform target 1.5 (coarsening) | `B5974F46CA01A4D7` | 30 | 29 | 29 | 30 |
| thin tube, uniform target 0.2, check off | `03362745616D38E7` | 45 | 49 | 46 | 48 |

After the change `Query` is 12 % of the CPU time (was 60 %); the rest is flat (ProcessEdge 24 %, vertex move
check 14 %, smoothing 8 %, projection 7 %).

## Safe preset on a box (#1611, 2026-11-25)

A unit box of 12 triangles remeshed to 1 % of its diagonal with `ApplySafePreset()` (30 passes, smoothing 0.5, projection,
30 degree creases): 50,982 triangles, result hash `6EA0AA89354C0BFC`, 17-19 s warm on 4 cores (net10.0, Release, master
cb72ffde); the row `unit box, safe preset, ...` of the table above. The cost is linear in the triangle count.

Where the time goes (temporary Stopwatch counters around the phases, not kept; the first run is 18.8 s; the shares overlap
where noted):

| part | time | share | what it is |
|---|---|---|---|
| edge-operation checks (`new_triangles_intersect`, 207k calls from collapse, flip, split) | 5.9 s | 31 % | 803k grid queries (5.5 s of it); 16.1M candidates (about 20 per query), 12.8M pair tests (the other 3.3M are excluded first); the pair tests are 2.6 s with the timers (about 1 s of timer cost), `TriangleHashGrid.Query` itself about 2.9 s (15 %) |
| `apply_moves_without_intersections` (buffered smoothing and projection moves) | 9.1 s | 48 % | first round over the AABB tree (already parallel) 4.7 s; `ensure_triangle_grid` (a rebuild per pass) 1.8 s; the rest (sorted sets, later rounds) about 2.6 s |
| smoothing, projection and the other edge-operation work | about 4 s | 21 % | |

No single cause: the number of queries and pair tests is the inherent work of checking every changed triangle against its
neighbours (a query box does not grow with the pass and unchanged triangles are not re-tested; a candidate list of 20 is about
one triangle and its ring). The upper bound of a free `TriangleHashGrid.Query` is its own cost, about 2.9 s (15 %). A flat
cell array in place of the `Dictionary<long, List<int>>`, plus an incremental grid update in place of the per-pass rebuild
(1.8 s), would save about 10-15 % (18.8 s to about 16 s) with a bit-identical output, and would also help `Reducer`,
`MeshEnvelope` and `AlphaWrapFeaturePass`, which use the grid. It is recorded as an option and not built: it does not change the
20 s verdict of the issue.
