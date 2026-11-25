# AlphaWrapFixStarBenchmark

The manifold step of `MeshAlphaWrap` (`make_manifold` / `fix_star`) at stars with many cells (issue #1607). A sphere (6 edge vertices per cube
side) is wrapped at `RelativeAlpha = 1e5`, `RelativeOffset = 1e6` with `MaxSteinerPoints` far below what the alpha needs, so the box corners have
stars of tens of thousands of cells. The old step moved one cell inside per iteration and recomputed the star's components and rescanned every
candidate each time (quadratic); the new one does the same moves in one pass (union-find over the inside components, a heap of the candidates, a
spanning forest of the outside cells to find the pieces a move cuts off from the box boundary).

    dotnet run --project benchmarks/AlphaWrapFixStarBenchmark -c Release [-- --quick]

Before (master 36d19843) and after, net10.0 Linux, Release; `moves` and `triangles` are the same in both (the result is bit for bit the same,
`AlphaWrapFixStarTests`):

| MaxSteinerPoints | total before | total after | manifold ms before | manifold ms after | moves | cells visited after |
|---|---|---|---|---|---|---|
| 2000 | 0.48 s | 0.51 s | 26 | 31 | 0 | 0 |
| 5000 | 0.61 s | 0.39 s | 117 | 113 | 10021 | 14146 |
| 10000 | 1.02 s | 0.55 s | 536 | 141 | 16480 | 43493 |
| 20000 | 2.51 s | 0.67 s | 1908 | 109 | 40548 | 74873 |
| 40000 | 12.51 s | 1.05 s | 11853 | 162 | 80152 | 168428 |
| 80000 | 51.77 s | 1.85 s | 50391 | 348 | 109828 | 330200 |
| 160000 | 48.71 s | 3.25 s | 45780 | 274 | 15652 | 84306 |

The cells visited before were about 38 million at 20000 points (counted on an instrumented copy). The time that is left is the wrap phase.
