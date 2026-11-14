# MeshOffsetBenchmark

`MeshOffset` (stage A1 of [docs/design/MESH_OFFSET.md](../../docs/design/MESH_OFFSET.md)) against the signed distance
grid + marching cubes path of `g3cli offset` ([OffsetSurfaceExample](../../docs/examples/OffsetSurfaceExample.cs)), on
procedural inputs only (a sphere, the six CAD solids of `benchmarks/SharpFeatureProbe/Procedural.cs`, a hollow cube).
Both results get the same band certificate (`MeshOffset.CertifyBand`, tolerance half a cell, 6 splits), so the band
column compares like with like: a proven upper bound of |sd(x) - d| over the result.

```
dotnet build benchmarks/MeshOffsetBenchmark -c Release
dotnet run -c Release --no-build --project benchmarks/MeshOffsetBenchmark -- 3        # repetitions; add "serial" for one thread
```

## Result (2026-11-13, branch claude/mesh-offset-a1 on master 0d90b838, cloud container, 4 threads, median of 3)

Summary over the 32 cases per method (8 inputs x offset / inset x 2 cells):

| | MeshOffset | SDF + MC |
|---|---|---|
| band certified at half a cell | 32 / 32 | 15 / 32 (every inset except the sphere's, chamfered creases, bound up to 0.37 abs(d) at cell abs(d) / 4; and the octant and hollow offsets) |
| `NotCleanReason` empty | 31 / 32 (chamfer inset at abs(d) / 8: a degenerate triangle; 32 / 32 after #800, see PR #853) | 25 / 32 (degenerate triangles) |
| self-intersection free | 32 / 32 (exact: covered by construction or checked) | 32 / 32 (measured, no guarantee) |
| triangles | 0.95x - 1.07x of SDF + MC | |
| time | 0.6x - 4.7x of SDF + MC, median 1.6x | |

Raw table:

| input | d / diag | cell / abs(d) | method | ms | tris | clean | SI-free | band / abs(d) | certified (tol cell/2) |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 0.05 | 0.25 | MeshOffset | 645 | 27592 | yes | True | 0.1250 | True |
| sphere | 0.05 | 0.25 | SDF+MC | 308 | 27512 | yes | True | 0.1250 | True |
| sphere | 0.05 | 0.125 | MeshOffset | 1999 | 110316 | yes | True | 0.0625 | True |
| sphere | 0.05 | 0.125 | SDF+MC | 1079 | 110264 | degenerate triangles | True | 0.0625 | True |
| sphere | -0.05 | 0.25 | MeshOffset | 281 | 13652 | yes | True | 0.1250 | True |
| sphere | -0.05 | 0.25 | SDF+MC | 199 | 13600 | yes | True | 0.1250 | True |
| sphere | -0.05 | 0.125 | MeshOffset | 1176 | 54596 | yes | True | 0.0625 | True |
| sphere | -0.05 | 0.125 | SDF+MC | 1058 | 54532 | yes | True | 0.0625 | True |
| box | 0.05 | 0.25 | MeshOffset | 340 | 29864 | yes | True | 0.1248 | True |
| box | 0.05 | 0.25 | SDF+MC | 178 | 28772 | yes | True | 0.1249 | True |
| box | 0.05 | 0.125 | MeshOffset | 1440 | 118928 | yes | True | 0.0625 | True |
| box | 0.05 | 0.125 | SDF+MC | 869 | 117320 | yes | True | 0.0625 | True |
| box | -0.05 | 0.25 | MeshOffset | 179 | 13404 | yes | True | 0.1246 | True |
| box | -0.05 | 0.25 | SDF+MC | 197 | 12564 | yes | True | 0.3708 | False |
| box | -0.05 | 0.125 | MeshOffset | 736 | 53224 | yes | True | 0.0623 | True |
| box | -0.05 | 0.125 | SDF+MC | 855 | 51928 | yes | True | 0.1854 | False |
| lshape | 0.05 | 0.25 | MeshOffset | 278 | 27188 | yes | True | 0.1248 | True |
| lshape | 0.05 | 0.25 | SDF+MC | 178 | 26732 | yes | True | 0.1249 | True |
| lshape | 0.05 | 0.125 | MeshOffset | 1273 | 108696 | yes | True | 0.0625 | True |
| lshape | 0.05 | 0.125 | SDF+MC | 984 | 109972 | degenerate triangles | True | 0.0625 | True |
| lshape | -0.05 | 0.25 | MeshOffset | 215 | 10712 | yes | True | 0.1246 | True |
| lshape | -0.05 | 0.25 | SDF+MC | 225 | 10332 | yes | True | 0.3708 | False |
| lshape | -0.05 | 0.125 | MeshOffset | 765 | 42648 | yes | True | 0.0624 | True |
| lshape | -0.05 | 0.125 | SDF+MC | 930 | 43772 | degenerate triangles | True | 0.1854 | False |
| octant | 0.05 | 0.25 | MeshOffset | 363 | 34864 | yes | True | 0.1249 | True |
| octant | 0.05 | 0.25 | SDF+MC | 252 | 34844 | yes | True | 0.3359 | False |
| octant | 0.05 | 0.125 | MeshOffset | 1734 | 139434 | yes | True | 0.0625 | True |
| octant | 0.05 | 0.125 | SDF+MC | 1256 | 139412 | yes | True | 0.1505 | False |
| octant | -0.05 | 0.25 | MeshOffset | 247 | 17328 | yes | True | 0.1250 | True |
| octant | -0.05 | 0.25 | SDF+MC | 233 | 18188 | degenerate triangles | True | 0.3708 | False |
| octant | -0.05 | 0.125 | MeshOffset | 981 | 69312 | yes | True | 0.0623 | True |
| octant | -0.05 | 0.125 | SDF+MC | 1613 | 71084 | degenerate triangles | True | 0.1854 | False |
| slot | 0.05 | 0.25 | MeshOffset | 350 | 31600 | yes | True | 0.1249 | True |
| slot | 0.05 | 0.25 | SDF+MC | 236 | 31468 | yes | True | 0.1250 | True |
| slot | 0.05 | 0.125 | MeshOffset | 1584 | 127444 | yes | True | 0.0625 | True |
| slot | 0.05 | 0.125 | SDF+MC | 1227 | 126112 | yes | True | 0.0625 | True |
| slot | -0.05 | 0.25 | MeshOffset | 200 | 14100 | yes | True | 0.1249 | True |
| slot | -0.05 | 0.25 | SDF+MC | 236 | 13988 | yes | True | 0.3708 | False |
| slot | -0.05 | 0.125 | MeshOffset | 949 | 57172 | yes | True | 0.0625 | True |
| slot | -0.05 | 0.125 | SDF+MC | 1132 | 56120 | yes | True | 0.1854 | False |
| vnotch | 0.05 | 0.25 | MeshOffset | 425 | 31088 | yes | True | 0.1249 | True |
| vnotch | 0.05 | 0.25 | SDF+MC | 119 | 29848 | yes | True | 0.1250 | True |
| vnotch | 0.05 | 0.125 | MeshOffset | 1916 | 124416 | yes | True | 0.0625 | True |
| vnotch | 0.05 | 0.125 | SDF+MC | 529 | 122504 | yes | True | 0.0625 | True |
| vnotch | -0.05 | 0.25 | MeshOffset | 241 | 13872 | yes | True | 0.1248 | True |
| vnotch | -0.05 | 0.25 | SDF+MC | 107 | 12952 | yes | True | 0.3708 | False |
| vnotch | -0.05 | 0.125 | MeshOffset | 1055 | 54980 | yes | True | 0.0624 | True |
| vnotch | -0.05 | 0.125 | SDF+MC | 542 | 53660 | yes | True | 0.1854 | False |
| chamfer | 0.05 | 0.25 | MeshOffset | 371 | 28708 | yes | True | 0.1250 | True |
| chamfer | 0.05 | 0.25 | SDF+MC | 95 | 27732 | yes | True | 0.1250 | True |
| chamfer | 0.05 | 0.125 | MeshOffset | 1708 | 114476 | yes | True | 0.0625 | True |
| chamfer | 0.05 | 0.125 | SDF+MC | 548 | 113028 | yes | True | 0.0625 | True |
| chamfer | -0.05 | 0.25 | MeshOffset | 212 | 12952 | yes | True | 0.1235 | True |
| chamfer | -0.05 | 0.25 | SDF+MC | 93 | 12228 | yes | True | 0.1939 | False |
| chamfer | -0.05 | 0.125 | MeshOffset | 966 | 51546 | degenerate triangles | True | 0.0616 | True |
| chamfer | -0.05 | 0.125 | SDF+MC | 545 | 50360 | yes | True | 0.0645 | False |
| hollow | 0.05 | 0.25 | MeshOffset | 560 | 37684 | yes | True | 0.1249 | True |
| hollow | 0.05 | 0.25 | SDF+MC | 142 | 37540 | yes | True | 0.1247 | True |
| hollow | 0.05 | 0.125 | MeshOffset | 2388 | 150232 | yes | True | 0.0625 | True |
| hollow | 0.05 | 0.125 | SDF+MC | 779 | 150208 | yes | True | 0.1680 | False |
| hollow | -0.05 | 0.25 | MeshOffset | 518 | 28888 | yes | True | 0.1250 | True |
| hollow | -0.05 | 0.25 | SDF+MC | 110 | 29572 | degenerate triangles | True | 0.3708 | False |
| hollow | -0.05 | 0.125 | MeshOffset | 2121 | 114872 | yes | True | 0.0625 | True |
| hollow | -0.05 | 0.125 | SDF+MC | 713 | 116620 | degenerate triangles | True | 0.1854 | False |
