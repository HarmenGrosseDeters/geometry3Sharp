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

## Result (2026-11-13, after #800, on master 22df4488 + #853; cloud container, 4 threads, median of 3)

Summary over the 32 cases per method (8 inputs x offset / inset x 2 cells):

| | MeshOffset | SDF + MC |
|---|---|---|
| band certified at half a cell | 32 / 32 | 15 / 32 (every inset except the sphere's, chamfered creases, bound up to 0.37 abs(d) at cell abs(d) / 4; and the octant and hollow offsets) |
| `NotCleanReason` empty | 32 / 32 (31 / 32 before #800: the chamfer inset at abs(d) / 8 had a degenerate triangle) | 25 / 32 (degenerate triangles) |
| self-intersection free | 32 / 32 (exact: covered by construction or checked) | 32 / 32 (measured, no guarantee) |
| triangles | 0.95x - 1.07x of SDF + MC | |
| time | 1.1x - 6.3x of SDF + MC, median 2.1x | |

Time against the first run (#799, 2026-11-13, 0.6x - 4.7x, median 1.6x): MeshOffset takes about the same (29.3 s summed over the
32 cases now, 30.2 s on master without #800 in the same session), but SDF + MC got faster on master in between (eg vnotch
at abs(d) / 4: 119 -> 74 ms, hollow at abs(d) / 8: 779 -> 575 ms), so the ratio grew; #800 itself does not change the time
(median ratio 2.31x on master without it, 2.06x with it, the same session).

Raw table:

| input | d / diag | cell / abs(d) | method | ms | tris | clean | SI-free | band / abs(d) | certified (tol cell/2) |
|---|---|---|---|---|---|---|---|---|---|
| sphere | 0.05 | 0.25 | MeshOffset | 654 | 27592 | yes | True | 0.1250 | True |
| sphere | 0.05 | 0.25 | SDF+MC | 347 | 27512 | yes | True | 0.1250 | True |
| sphere | 0.05 | 0.125 | MeshOffset | 2081 | 110316 | yes | True | 0.0625 | True |
| sphere | 0.05 | 0.125 | SDF+MC | 862 | 110264 | degenerate triangles | True | 0.0625 | True |
| sphere | -0.05 | 0.25 | MeshOffset | 306 | 13652 | yes | True | 0.1250 | True |
| sphere | -0.05 | 0.25 | SDF+MC | 178 | 13600 | yes | True | 0.1250 | True |
| sphere | -0.05 | 0.125 | MeshOffset | 1299 | 54596 | yes | True | 0.0625 | True |
| sphere | -0.05 | 0.125 | SDF+MC | 793 | 54532 | yes | True | 0.0625 | True |
| box | 0.05 | 0.25 | MeshOffset | 296 | 29864 | yes | True | 0.1248 | True |
| box | 0.05 | 0.25 | SDF+MC | 128 | 28772 | yes | True | 0.1249 | True |
| box | 0.05 | 0.125 | MeshOffset | 1471 | 118928 | yes | True | 0.0625 | True |
| box | 0.05 | 0.125 | SDF+MC | 712 | 117320 | yes | True | 0.0625 | True |
| box | -0.05 | 0.25 | MeshOffset | 187 | 13404 | yes | True | 0.1246 | True |
| box | -0.05 | 0.25 | SDF+MC | 112 | 12564 | yes | True | 0.3708 | False |
| box | -0.05 | 0.125 | MeshOffset | 830 | 53224 | yes | True | 0.0623 | True |
| box | -0.05 | 0.125 | SDF+MC | 672 | 51928 | yes | True | 0.1854 | False |
| lshape | 0.05 | 0.25 | MeshOffset | 300 | 27188 | yes | True | 0.1248 | True |
| lshape | 0.05 | 0.25 | SDF+MC | 151 | 26732 | yes | True | 0.1249 | True |
| lshape | 0.05 | 0.125 | MeshOffset | 1394 | 108696 | yes | True | 0.0625 | True |
| lshape | 0.05 | 0.125 | SDF+MC | 678 | 109972 | degenerate triangles | True | 0.0625 | True |
| lshape | -0.05 | 0.25 | MeshOffset | 227 | 10712 | yes | True | 0.1246 | True |
| lshape | -0.05 | 0.25 | SDF+MC | 139 | 10332 | yes | True | 0.3708 | False |
| lshape | -0.05 | 0.125 | MeshOffset | 739 | 42648 | yes | True | 0.0624 | True |
| lshape | -0.05 | 0.125 | SDF+MC | 599 | 43772 | degenerate triangles | True | 0.1854 | False |
| octant | 0.05 | 0.25 | MeshOffset | 369 | 34864 | yes | True | 0.1249 | True |
| octant | 0.05 | 0.25 | SDF+MC | 181 | 34844 | yes | True | 0.3359 | False |
| octant | 0.05 | 0.125 | MeshOffset | 1715 | 139434 | yes | True | 0.0625 | True |
| octant | 0.05 | 0.125 | SDF+MC | 909 | 139412 | yes | True | 0.1505 | False |
| octant | -0.05 | 0.25 | MeshOffset | 249 | 17328 | yes | True | 0.1250 | True |
| octant | -0.05 | 0.25 | SDF+MC | 173 | 18188 | degenerate triangles | True | 0.3708 | False |
| octant | -0.05 | 0.125 | MeshOffset | 1111 | 69312 | yes | True | 0.0623 | True |
| octant | -0.05 | 0.125 | SDF+MC | 902 | 71084 | degenerate triangles | True | 0.1854 | False |
| slot | 0.05 | 0.25 | MeshOffset | 414 | 31600 | yes | True | 0.1249 | True |
| slot | 0.05 | 0.25 | SDF+MC | 167 | 31468 | yes | True | 0.1250 | True |
| slot | 0.05 | 0.125 | MeshOffset | 1688 | 127444 | yes | True | 0.0625 | True |
| slot | 0.05 | 0.125 | SDF+MC | 879 | 126112 | yes | True | 0.0625 | True |
| slot | -0.05 | 0.25 | MeshOffset | 238 | 14100 | yes | True | 0.1249 | True |
| slot | -0.05 | 0.25 | SDF+MC | 163 | 13988 | yes | True | 0.3708 | False |
| slot | -0.05 | 0.125 | MeshOffset | 985 | 57172 | yes | True | 0.0625 | True |
| slot | -0.05 | 0.125 | SDF+MC | 869 | 56120 | yes | True | 0.1854 | False |
| vnotch | 0.05 | 0.25 | MeshOffset | 465 | 31088 | yes | True | 0.1249 | True |
| vnotch | 0.05 | 0.25 | SDF+MC | 74 | 29848 | yes | True | 0.1250 | True |
| vnotch | 0.05 | 0.125 | MeshOffset | 2013 | 124416 | yes | True | 0.0625 | True |
| vnotch | 0.05 | 0.125 | SDF+MC | 408 | 122504 | yes | True | 0.0625 | True |
| vnotch | -0.05 | 0.25 | MeshOffset | 208 | 13872 | yes | True | 0.1248 | True |
| vnotch | -0.05 | 0.25 | SDF+MC | 71 | 12952 | yes | True | 0.3708 | False |
| vnotch | -0.05 | 0.125 | MeshOffset | 1014 | 54980 | yes | True | 0.0624 | True |
| vnotch | -0.05 | 0.125 | SDF+MC | 329 | 53660 | yes | True | 0.1854 | False |
| chamfer | 0.05 | 0.25 | MeshOffset | 371 | 28708 | yes | True | 0.1250 | True |
| chamfer | 0.05 | 0.25 | SDF+MC | 81 | 27732 | yes | True | 0.1250 | True |
| chamfer | 0.05 | 0.125 | MeshOffset | 1698 | 114476 | yes | True | 0.0625 | True |
| chamfer | 0.05 | 0.125 | SDF+MC | 429 | 113028 | yes | True | 0.0625 | True |
| chamfer | -0.05 | 0.25 | MeshOffset | 218 | 12952 | yes | True | 0.1235 | True |
| chamfer | -0.05 | 0.25 | SDF+MC | 79 | 12228 | yes | True | 0.1939 | False |
| chamfer | -0.05 | 0.125 | MeshOffset | 933 | 51546 | yes | True | 0.0616 | True |
| chamfer | -0.05 | 0.125 | SDF+MC | 362 | 50360 | yes | True | 0.0645 | False |
| hollow | 0.05 | 0.25 | MeshOffset | 575 | 37684 | yes | True | 0.1249 | True |
| hollow | 0.05 | 0.25 | SDF+MC | 108 | 37540 | yes | True | 0.1247 | True |
| hollow | 0.05 | 0.125 | MeshOffset | 2605 | 150232 | yes | True | 0.0625 | True |
| hollow | 0.05 | 0.125 | SDF+MC | 575 | 150208 | yes | True | 0.1680 | False |
| hollow | -0.05 | 0.25 | MeshOffset | 480 | 28888 | yes | True | 0.1250 | True |
| hollow | -0.05 | 0.25 | SDF+MC | 84 | 29572 | degenerate triangles | True | 0.3708 | False |
| hollow | -0.05 | 0.125 | MeshOffset | 2131 | 114872 | yes | True | 0.0625 | True |
| hollow | -0.05 | 0.125 | SDF+MC | 581 | 116620 | degenerate triangles | True | 0.1854 | False |
