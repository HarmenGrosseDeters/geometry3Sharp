# SmallInputWrapBenchmark

`MeshRobustRepair` on small inputs that fall through to the alpha wrap fallback (#636): the 62-byte MeshFuzzPilot repro (an
octahedron of 8 triangles at 2.4e-35 whose vertex 3 sits on vertex 2) and six jittered octahedra / tetrahedra (4-8 triangles) from
`new Random(5)`: index i of the first 300 draws moves one or two vertices to a grid point of {-1, -0.5, ..., 1}^3; the six that reach
the wrap are listed by name in the source. Each input runs with `SmallInputWrapTriangles = 0` (the diag / 300 wrap of every size,
"old") and with the default 64 ("new").

    dotnet run --project benchmarks/SmallInputWrapBenchmark -c Release

CSV columns: seconds of `Apply()`, wrap triangles (before the simplification), result triangles, alpha and offset of the wrap and the
two directed maximum vertex distances, all relative to the input's bounding box diagonal. Committed run: `results-2026-11-12.csv`
(container with 4 cores, single run, expect about ±15 % noise on the seconds; the triangle counts and distances are deterministic).

| input | old s | old wrap tris | new s | new wrap tris | new alpha / diag | new offset / diag | result to input old / new | input to result old / new |
|---|---|---|---|---|---|---|---|---|
| 62 B repro | 10.56 | 204 222 | 0.37 | 232 | 0.118 | 0.0118 | 0.00038 / 0.0127 | 0.00020 / 0.0068 |
| tet-jitter-6 | 3.92 | 135 834 | 0.10 | 144 | 0.177 | 0.0177 | 0.00038 / 0.0196 | 0.00009 / 0.0045 |
| oct-jitter-22 | 6.74 | 179 816 | 0.19 | 192 | 0.131 | 0.0131 | 0.00038 / 0.0142 | 0.00020 / 0.0085 |
| tet-jitter-24 | 4.93 | 135 054 | 0.12 | 102 | 0.204 | 0.0204 | 0.00038 / 0.0221 | 0.00013 / 0.0063 |
| tet-jitter-25 | 3.45 | 125 368 | 0.05 | 154 | 0.155 | 0.0155 | 0.00037 / 0.0170 | 0.00024 / 0.0089 |
| tet-jitter-28 | 2.85 | 102 472 | 0.07 | 152 | 0.125 | 0.0125 | 0.00039 / 0.0131 | 0.00033 / 0.0126 |
| tet-jitter-34 | 5.35 | 161 284 | 0.15 | 166 | 0.174 | 0.0174 | 0.00038 / 0.0188 | 0.00015 / 0.0072 |

The new wrap is coarser by design (alpha a quarter of the median edge length): the result stays within about 1.15 offsets of the
input, and the offset is 35 to 60 times the old one.
