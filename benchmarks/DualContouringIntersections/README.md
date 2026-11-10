# DualContouringIntersections

Upper bound for intersection-free dual contouring (Phase 5 item 4, design note
[docs/design/DC_INTERSECTION_FREE.md](../../docs/design/DC_INTERSECTION_FREE.md)). Measures today's `DualContouring` /
`WindingContouring`: self-intersections before and after the repair, vertices the repair moves, left-out non-manifold
triangles, the time of the exact check, and per dual polygon Wang's envelope criterion (concave envelope vertices,
computed with `ExactPredicates.Orient3d`), with the class of every triangle in an intersecting pair. Uses the internal
hook `DualContouring.CollectDiagnostics` (off by default, no effect on the result).

```
dotnet run -c Release --project benchmarks/DualContouringIntersections -- wind.jsonl mesh:all <T1 meshes>... -- 64,128
dotnet run -c Release --project benchmarks/DualContouringIntersections -- imp.jsonl imp:<shape>@<seed>... -- 24,48,96
python3 benchmarks/DualContouringIntersections/analyze.py wind.jsonl imp.jsonl [--per-input]
```

Inputs: the lever 6 set of `benchmarks/SharpFeatureProbe` (the 20 T1 Thingi10K meshes 96675 96773 64983 43551 1582399
57084 104640 94664 73088 1582404 37731 57714 116871 109459 472011 37111 113418 79961 1038438 66112, fetched with
`benchmarks/LevyClassification/fetch_thingi.py`, not committed; the 6 procedural CAD solids) through
`MeshWindingNumberSolidify` (adaptive, cell = diagonal / res), and the implicit shapes rotbox, boxminussphere,
twoboxes, slab, cone, gyroid (seeds 0-19: random rotation and grid shift) through dense `DualContouring`. 4 cores,
one process, 2026-11-10.

## Result

| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) | moved / vertices | multi-patch polys | 1 / 2 concave | SI triangles in / out / multi-patch / transition | covered pairs | check+repair share of DC time |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 9 | 149 | 0 | 0 | 0 (0) | 149 / 161019 | 0.65 % | 2.22 % / 0.020 % | 20 / 0 / 74 / 40 | 0 | 72 % |
| T1 | 128 | 20 | 6 | 101 | 0 | 1 | 12 (1) | 110 / 432218 | 0.28 % | 1.85 % / 0.006 % | 14 / 2 / 77 / 20 | 0 | 72 % |
| imp:boxminussphere | 24 | 20 | 5 | 32 | 0 | 0 | 0 (0) | 29 / 23458 | 2.33 % | 6.18 % / 0.060 % | 1 / 2 / 29 / 0 | 0 | 96 % |
| imp:boxminussphere | 48 | 20 | 4 | 30 | 0 | 0 | 0 (0) | 29 / 95682 | 1.08 % | 3.13 % / 0.037 % | 0 / 0 / 27 / 0 | 0 | 92 % |
| imp:boxminussphere | 96 | 20 | 11 | 74 | 0 | 0 | 0 (0) | 67 / 387456 | 0.52 % | 1.62 % / 0.015 % | 2 / 1 / 70 / 0 | 0 | 86 % |
| imp:cone | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 14 / 10976 | 1.86 % | 7.78 % / 0.027 % | 0 / 0 / 14 / 0 | 0 | 97 % |
| imp:cone | 48 | 20 | 7 | 64 | 0 | 0 | 0 (0) | 60 / 44614 | 1.05 % | 3.82 % / 0.018 % | 0 / 0 / 60 / 0 | 0 | 87 % |
| imp:cone | 96 | 20 | 8 | 131 | 0 | 0 | 0 (0) | 73 / 179628 | 0.46 % | 2.03 % / 0.014 % | 2 / 1 / 88 / 0 | 0 | 84 % |
| imp:gyroid | 24 | 20 | 20 | 906 | 0 | 0 | 0 (0) | 926 / 56988 | 81.18 % | 3.43 % / 0.152 % | 16 / 25 / 868 / 0 | 0 | 82 % |
| imp:gyroid | 48 | 20 | 20 | 1857 | 0 | 0 | 1708 (20) | 1616 / 283302 | 69.87 % | 2.06 % / 0.041 % | 13 / 16 / 1851 / 0 | 0 | 86 % |
| imp:gyroid | 96 | 20 | 20 | 4055 | 0 | 0 | 96 (13) | 2426 / 1190410 | 3.62 % | 5.93 % / 0.043 % | 21 / 17 / 2893 / 0 | 0 | 83 % |
| imp:rotbox | 24 | 20 | 2 | 12 | 0 | 0 | 0 (0) | 10 / 14924 | 2.32 % | 5.82 % / 0.101 % | 0 / 0 / 10 / 0 | 0 | 89 % |
| imp:rotbox | 48 | 20 | 1 | 3 | 0 | 0 | 0 (0) | 4 / 61060 | 1.16 % | 3.05 % / 0.031 % | 0 / 0 / 4 / 0 | 0 | 75 % |
| imp:rotbox | 96 | 20 | 6 | 30 | 0 | 0 | 0 (0) | 32 / 247330 | 0.63 % | 1.50 % / 0.015 % | 0 / 0 / 33 / 0 | 0 | 89 % |
| imp:slab | 24 | 20 | 7 | 60 | 0 | 0 | 0 (0) | 59 / 15418 | 55.30 % | 5.89 % / 0.168 % | 0 / 0 / 65 / 0 | 0 | 80 % |
| imp:slab | 48 | 20 | 18 | 549 | 0 | 0 | 10104 (18) | 534 / 85814 | 50.13 % | 2.32 % / 0.068 % | 2 / 4 / 537 / 0 | 0 | 89 % |
| imp:slab | 96 | 20 | 4 | 27 | 0 | 0 | 0 (0) | 24 / 409052 | 18.00 % | 1.85 % / 0.014 % | 2 / 2 / 21 / 0 | 0 | 86 % |
| imp:twoboxes | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 15 / 14532 | 3.85 % | 10.52 % / 0.117 % | 0 / 0 / 14 / 0 | 0 | 96 % |
| imp:twoboxes | 48 | 20 | 5 | 21 | 0 | 0 | 0 (0) | 23 / 60186 | 1.84 % | 5.40 % / 0.057 % | 2 / 2 / 20 / 0 | 0 | 87 % |
| imp:twoboxes | 96 | 20 | 3 | 30 | 0 | 0 | 0 (0) | 26 / 246250 | 0.93 % | 2.96 % / 0.030 % | 0 / 0 / 25 / 0 | 0 | 86 % |
| mesh:procedural | 64 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 41400 | 0.00 % | 0.10 % / 0.000 % | 0 / 0 / 0 / 0 | 0 | 81 % |
| mesh:procedural | 128 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 164508 | 0.00 % | 0.04 % / 0.000 % | 0 / 0 / 0 / 0 | 0 | 84 % |

vertices not strictly inside their cell: 0 of 4226225; quads with 3 or 4 concave vertices: 0

Columns: raw SI = intersecting triangle pairs of the last `Generate` before the repair (`SelfIntersectionsFound`); final
SI = `MeshRepairMetrics.SelfIntersections` of the returned mesh; moved = vertices the repair moved off their QEF
point; multi-patch / concave = shares of the dual polygons; covered pairs = intersecting pairs with both triangles
inside their envelopes (the criterion says none can exist). Whole `MeshWindingNumberSolidify` time on T1: 83.7 s, of
which the check and repair 48.2 s (58 %); implicit runs: 112 s, 94.5 s (84 %).

Findings (details in the design note): the repair never failed (412 runs), but it is 58-97 % of the time; the one bad
final result (116871 at 128) comes from 12 left-out non-manifold triangles whose holes `FillSmallHoles` closes with 63
intersecting pairs; thin sheets leave up to 10104 non-manifold triangles out (open results); intersections need a
multi-patch, transition or against-criterion polygon (0 covered pairs of 8159).
