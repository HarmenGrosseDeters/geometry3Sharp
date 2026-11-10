# DualContouringIntersections

Upper bound for intersection-free dual contouring (Phase 5 item 4, design note
[docs/design/DC_INTERSECTION_FREE.md](../../docs/design/DC_INTERSECTION_FREE.md)). Measures today's `DualContouring` /
`WindingContouring`: self-intersections before and after the repair, vertices the repair moves, left-out non-manifold
triangles, the time of the exact check, and per dual polygon Wang's envelope criterion (concave envelope vertices,
computed with `ExactPredicates.Orient3d`), with the class of every triangle in an intersecting pair. Uses the internal
hook `DualContouring.CollectDiagnostics` (off by default, no effect on the result). The hook itself costs time
(a second, serial check of all pairs), so times come from separate `--timing` runs with the hook off; the check and
repair time is `DualContouring.ResolveMilliseconds` (its own timer, without the diagnostics), summed over all `Generate`
passes of a run (the adaptive path runs several on one instance).

```
dotnet run -c Release --project benchmarks/DualContouringIntersections -- wind.jsonl mesh:all <T1 meshes>... -- 64,128
dotnet run -c Release --project benchmarks/DualContouringIntersections -- imp.jsonl imp:<shape>@<seed>... -- 24,48,96
dotnet run -c Release --project benchmarks/DualContouringIntersections -- wind_t.jsonl --timing mesh:all <T1 meshes>... -- 64,128
python3 benchmarks/DualContouringIntersections/analyze.py wind.jsonl imp.jsonl wind_t.jsonl imp_t.jsonl [--per-input]
```

Inputs: the lever 6 set of `benchmarks/SharpFeatureProbe` (the 20 T1 Thingi10K meshes 96675 96773 64983 43551 1582399
57084 104640 94664 73088 1582404 37731 57714 116871 109459 472011 37111 113418 79961 1038438 66112, fetched with
`benchmarks/LevyClassification/fetch_thingi.py`, not committed; the 6 procedural CAD solids) through
`MeshWindingNumberSolidify` (adaptive, cell = diagonal / res), and the implicit shapes rotbox, boxminussphere,
twoboxes, slab, cone, gyroid (seeds 0-19: random rotation and grid shift) through dense `DualContouring`. 4 cores,
one process, 2026-11-10.

## Result

| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) | moved / vertices | multi-patch polys | 1 / 2 concave (of uniform single-patch quads) | SI triangles in / out / multi-patch / transition | covered / all pairs |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 9 | 149 | 0 | 0 | 0 (0) | 149 / 161019 | 0.65 % | 2.27 % / 0.020 % | 20 / 0 / 74 / 40 | 0 / 149 |
| T1 | 128 | 20 | 6 | 101 | 0 | 1 | 12 (1) | 110 / 432218 | 0.28 % | 1.86 % / 0.006 % | 14 / 2 / 77 / 20 | 0 / 101 |
| imp:boxminussphere | 24 | 20 | 5 | 32 | 0 | 0 | 0 (0) | 29 / 23458 | 2.33 % | 6.33 % / 0.061 % | 1 / 2 / 29 / 0 | 0 / 32 |
| imp:boxminussphere | 48 | 20 | 4 | 30 | 0 | 0 | 0 (0) | 29 / 95682 | 1.08 % | 3.17 % / 0.037 % | 0 / 0 / 27 / 0 | 0 / 30 |
| imp:boxminussphere | 96 | 20 | 11 | 74 | 0 | 0 | 0 (0) | 67 / 387456 | 0.52 % | 1.63 % / 0.015 % | 2 / 1 / 70 / 0 | 0 / 74 |
| imp:cone | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 14 / 10976 | 1.86 % | 7.93 % / 0.028 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:cone | 48 | 20 | 7 | 64 | 0 | 0 | 0 (0) | 60 / 44614 | 1.05 % | 3.86 % / 0.018 % | 0 / 0 / 60 / 0 | 0 / 64 |
| imp:cone | 96 | 20 | 8 | 131 | 0 | 0 | 0 (0) | 73 / 179628 | 0.46 % | 2.04 % / 0.015 % | 2 / 1 / 88 / 0 | 0 / 131 |
| imp:gyroid | 24 | 20 | 20 | 906 | 0 | 0 | 0 (0) | 926 / 56988 | 81.18 % | 18.20 % / 0.809 % | 16 / 25 / 868 / 0 | 0 / 906 |
| imp:gyroid | 48 | 20 | 20 | 1857 | 0 | 0 | 1708 (20) | 1616 / 283302 | 69.87 % | 6.84 % / 0.135 % | 13 / 16 / 1851 / 0 | 0 / 1857 |
| imp:gyroid | 96 | 20 | 20 | 4055 | 0 | 0 | 96 (13) | 2426 / 1190410 | 3.62 % | 6.15 % / 0.045 % | 21 / 17 / 2893 / 0 | 0 / 4055 |
| imp:rotbox | 24 | 20 | 2 | 12 | 0 | 0 | 0 (0) | 10 / 14924 | 2.32 % | 5.96 % / 0.103 % | 0 / 0 / 10 / 0 | 0 / 12 |
| imp:rotbox | 48 | 20 | 1 | 3 | 0 | 0 | 0 (0) | 4 / 61060 | 1.16 % | 3.09 % / 0.032 % | 0 / 0 / 4 / 0 | 0 / 3 |
| imp:rotbox | 96 | 20 | 6 | 30 | 0 | 0 | 0 (0) | 32 / 247330 | 0.63 % | 1.51 % / 0.015 % | 0 / 0 / 33 / 0 | 0 / 30 |
| imp:slab | 24 | 20 | 7 | 60 | 0 | 0 | 0 (0) | 59 / 15418 | 55.30 % | 13.18 % / 0.377 % | 0 / 0 / 65 / 0 | 0 / 60 |
| imp:slab | 48 | 20 | 18 | 549 | 0 | 0 | 10104 (18) | 534 / 85814 | 50.13 % | 4.65 % / 0.137 % | 2 / 4 / 537 / 0 | 0 / 549 |
| imp:slab | 96 | 20 | 4 | 27 | 0 | 0 | 0 (0) | 24 / 409052 | 18.00 % | 2.25 % / 0.017 % | 2 / 2 / 21 / 0 | 0 / 27 |
| imp:twoboxes | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 15 / 14532 | 3.85 % | 10.94 % / 0.122 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:twoboxes | 48 | 20 | 5 | 21 | 0 | 0 | 0 (0) | 23 / 60186 | 1.84 % | 5.50 % / 0.058 % | 2 / 2 / 20 / 0 | 0 / 21 |
| imp:twoboxes | 96 | 20 | 3 | 30 | 0 | 0 | 0 (0) | 26 / 246250 | 0.93 % | 2.99 % / 0.030 % | 0 / 0 / 25 / 0 | 0 / 30 |
| mesh:procedural | 64 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 41400 | 0.00 % | 0.10 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |
| mesh:procedural | 128 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 164508 | 0.00 % | 0.04 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |

vertices not strictly inside their cell: 0 of 4226225; quads with 3 or 4 concave vertices: 0
stages of 116871.stl at 128: final SI 63, contour (with hole fill) 63, after cleanup 63, non-manifold triangles left out 12, last DC pass SI left 0

| input | res | runs | total s | DC phases s | check + repair s | share of DC | share of total |
|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 9.1 | 4.5 | 1.4 | 30 % | 15 % |
| T1 | 128 | 20 | 24.4 | 13.1 | 4.0 | 30 % | 16 % |
| imp:boxminussphere | 24 | 20 | 0.3 | 0.2 | 0.1 | 46 % | 40 % |
| imp:boxminussphere | 48 | 20 | 1.0 | 1.0 | 0.4 | 36 % | 34 % |
| imp:boxminussphere | 96 | 20 | 4.1 | 4.0 | 1.8 | 43 % | 43 % |
| imp:cone | 24 | 20 | 0.1 | 0.1 | 0.0 | 51 % | 39 % |
| imp:cone | 48 | 20 | 0.4 | 0.3 | 0.2 | 51 % | 45 % |
| imp:cone | 96 | 20 | 1.7 | 1.6 | 0.7 | 45 % | 43 % |
| imp:gyroid | 24 | 20 | 0.6 | 0.6 | 0.2 | 41 % | 39 % |
| imp:gyroid | 48 | 20 | 2.9 | 2.9 | 1.4 | 48 % | 47 % |
| imp:gyroid | 96 | 20 | 13.5 | 13.5 | 6.0 | 45 % | 44 % |
| imp:rotbox | 24 | 20 | 0.3 | 0.2 | 0.1 | 41 % | 37 % |
| imp:rotbox | 48 | 20 | 0.7 | 0.7 | 0.3 | 41 % | 38 % |
| imp:rotbox | 96 | 20 | 3.6 | 3.5 | 1.2 | 34 % | 33 % |
| imp:slab | 24 | 20 | 0.1 | 0.1 | 0.1 | 64 % | 52 % |
| imp:slab | 48 | 20 | 0.8 | 0.8 | 0.5 | 57 % | 53 % |
| imp:slab | 96 | 20 | 4.3 | 4.2 | 1.7 | 41 % | 41 % |
| imp:twoboxes | 24 | 20 | 0.2 | 0.2 | 0.1 | 30 % | 26 % |
| imp:twoboxes | 48 | 20 | 0.5 | 0.5 | 0.2 | 51 % | 45 % |
| imp:twoboxes | 96 | 20 | 2.7 | 2.7 | 0.9 | 35 % | 35 % |
| mesh:procedural | 64 | 6 | 2.4 | 1.3 | 0.6 | 47 % | 25 % |
| mesh:procedural | 128 | 6 | 7.8 | 4.6 | 1.7 | 36 % | 21 % |

Columns: raw SI = intersecting triangle pairs of the last `Generate` before the repair (`SelfIntersectionsFound`); final
SI = `MeshRepairMetrics.SelfIntersections` of the returned mesh; moved = vertices the repair moved off their QEF
point; multi-patch = share of all dual polygons; 1 / 2 concave = share of the uniform single-patch quads (all four cells
of the same size, one patch each); covered / all pairs = intersecting pairs with both triangles inside their envelopes
(the criterion says none can exist) / all pairs, each counted once. Timing table: hook off, total = the whole
`MeshWindingNumberSolidify` or dense `Generate`.

Findings (details in the design note): the repair never failed (412 runs); it is 30 % of the dual contouring time on
T1 (15-16 % of the whole solidify) and 30-64 % of dense DC; the one bad final result (116871 at 128) comes from 12
left-out non-manifold triangles whose holes `FillSmallHoles` closes with 63 intersecting pairs; thin sheets leave up
to 10104 non-manifold triangles out (open results); every intersection involves a multi-patch, transition or
against-criterion polygon (0 covered of 8159 pairs).

## Option A (envelope triangulation, check of the uncovered triangles only)

A/B against the #541 head (`compare.py`, two repetitions alternating, hook off):

| input | res | runs | total s base / cand | ratio | DC s base / cand | ratio | check + repair s base / cand | ratio |
|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 8.8 / 8.2 | 0.92 | 4.5 / 3.6 | 0.79 | 1.25 / 0.20 | 0.16 |
| T1 | 128 | 20 | 23.9 / 21.9 | 0.92 | 12.7 / 10.5 | 0.82 | 4.14 / 0.87 | 0.21 |
| imp:boxminussphere | 24 | 20 | 0.2 / 0.1 | 0.50 | 0.2 / 0.1 | 0.42 | 0.11 / 0.01 | 0.14 |
| imp:boxminussphere | 48 | 20 | 0.9 / 0.7 | 0.74 | 0.9 / 0.6 | 0.73 | 0.35 / 0.07 | 0.22 |
| imp:boxminussphere | 96 | 20 | 3.9 / 3.1 | 0.79 | 3.8 / 3.0 | 0.79 | 1.67 / 0.39 | 0.23 |
| imp:cone | 24 | 20 | 0.1 / 0.1 | 0.59 | 0.1 / 0.0 | 0.41 | 0.04 / 0.00 | 0.06 |
| imp:cone | 48 | 20 | 0.4 / 0.3 | 0.86 | 0.3 / 0.3 | 0.82 | 0.15 / 0.03 | 0.20 |
| imp:cone | 96 | 20 | 1.6 / 1.3 | 0.80 | 1.5 / 1.2 | 0.79 | 0.71 / 0.17 | 0.24 |
| imp:gyroid | 24 | 20 | 0.7 / 0.4 | 0.68 | 0.6 / 0.4 | 0.66 | 0.23 / 0.22 | 0.93 |
| imp:gyroid | 48 | 20 | 2.9 / 3.4 | 1.19 | 2.8 / 3.4 | 1.19 | 1.39 / 1.39 | 0.99 |
| imp:gyroid | 96 | 20 | 14.1 / 11.4 | 0.81 | 14.0 / 11.3 | 0.81 | 6.30 / 2.73 | 0.43 |
| imp:rotbox | 24 | 20 | 0.2 / 0.2 | 0.87 | 0.2 / 0.1 | 0.85 | 0.08 / 0.02 | 0.23 |
| imp:rotbox | 48 | 20 | 0.7 / 0.6 | 0.80 | 0.7 / 0.6 | 0.79 | 0.30 / 0.07 | 0.23 |
| imp:rotbox | 96 | 20 | 2.8 / 2.4 | 0.88 | 2.7 / 2.4 | 0.88 | 1.14 / 0.36 | 0.31 |
| imp:slab | 24 | 20 | 0.1 / 0.2 | 1.12 | 0.1 / 0.1 | 1.15 | 0.07 / 0.04 | 0.63 |
| imp:slab | 48 | 20 | 1.1 / 0.9 | 0.90 | 1.0 / 0.9 | 0.89 | 0.51 / 0.38 | 0.76 |
| imp:slab | 96 | 20 | 3.9 / 3.3 | 0.84 | 3.9 / 3.3 | 0.84 | 1.72 / 0.70 | 0.41 |
| imp:twoboxes | 24 | 20 | 0.1 / 0.1 | 0.75 | 0.1 / 0.1 | 0.66 | 0.06 / 0.00 | 0.07 |
| imp:twoboxes | 48 | 20 | 0.7 / 0.4 | 0.66 | 0.6 / 0.4 | 0.65 | 0.21 / 0.05 | 0.23 |
| imp:twoboxes | 96 | 20 | 2.5 / 2.2 | 0.87 | 2.4 / 2.1 | 0.87 | 1.07 / 0.24 | 0.23 |
| mesh:procedural | 64 | 6 | 2.4 / 2.1 | 0.87 | 1.2 / 0.9 | 0.74 | 0.55 / 0.08 | 0.15 |
| mesh:procedural | 128 | 6 | 8.2 / 6.3 | 0.77 | 4.9 / 3.4 | 0.69 | 1.74 / 0.14 | 0.08 |

```
python3 benchmarks/DualContouringIntersections/compare.py --base base_w1.jsonl base_w2.jsonl base_i1.jsonl base_i2.jsonl --cand cand_w1.jsonl cand_w2.jsonl cand_i1.jsonl cand_i2.jsonl
```

Covered 90 % of the triangles (7.59 M of 8.45 M), 135887 forced diagonals, 1278 edge vertices; 0 covered pairs of 8053
intersecting pairs; 0 left after the repair in all 412 runs. Details in the design note, section 5.

## Option B (face vertices on ambiguous faces and refinement transitions)

A/B against master with option A (`compare.py`, two repetitions alternating, hook off):

| input | res | runs | total s base / cand | ratio | DC s base / cand | ratio | check + repair s base / cand | ratio |
|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 7.4 / 7.4 | 1.00 | 3.3 / 3.3 | 1.00 | 0.18 / 0.17 | 0.93 |
| T1 | 128 | 20 | 19.2 / 20.7 | 1.08 | 9.4 / 10.0 | 1.07 | 0.82 / 0.50 | 0.62 |
| imp:boxminussphere | 24 | 20 | 0.1 / 0.1 | 1.30 | 0.1 / 0.1 | 1.46 | 0.01 / 0.01 | 0.89 |
| imp:boxminussphere | 48 | 20 | 0.6 / 0.6 | 1.00 | 0.6 / 0.6 | 1.00 | 0.07 / 0.07 | 1.11 |
| imp:boxminussphere | 96 | 20 | 2.7 / 2.8 | 1.04 | 2.6 / 2.8 | 1.04 | 0.40 / 0.36 | 0.91 |
| imp:cone | 24 | 20 | 0.0 / 0.0 | 0.96 | 0.0 / 0.0 | 0.80 | 0.00 / 0.00 | 0.33 |
| imp:cone | 48 | 20 | 0.2 / 0.3 | 1.23 | 0.2 / 0.2 | 1.34 | 0.03 / 0.03 | 1.13 |
| imp:cone | 96 | 20 | 1.2 / 1.2 | 1.01 | 1.1 / 1.1 | 1.01 | 0.15 / 0.14 | 0.91 |
| imp:gyroid | 24 | 20 | 0.4 / 0.4 | 1.06 | 0.4 / 0.4 | 1.09 | 0.20 / 0.21 | 1.06 |
| imp:gyroid | 48 | 20 | 2.8 / 2.9 | 1.05 | 2.7 / 2.9 | 1.05 | 1.26 / 1.29 | 1.03 |
| imp:gyroid | 96 | 20 | 9.6 / 9.9 | 1.04 | 9.5 / 9.9 | 1.04 | 2.21 / 2.25 | 1.02 |
| imp:rotbox | 24 | 20 | 0.1 / 0.2 | 1.33 | 0.1 / 0.1 | 1.38 | 0.01 / 0.01 | 1.06 |
| imp:rotbox | 48 | 20 | 0.4 / 0.5 | 1.25 | 0.4 / 0.5 | 1.28 | 0.07 / 0.07 | 1.01 |
| imp:rotbox | 96 | 20 | 2.0 / 2.2 | 1.10 | 1.9 / 2.1 | 1.10 | 0.29 / 0.31 | 1.09 |
| imp:slab | 24 | 20 | 0.1 / 0.1 | 1.27 | 0.1 / 0.1 | 1.35 | 0.04 / 0.05 | 1.05 |
| imp:slab | 48 | 20 | 0.8 / 1.0 | 1.18 | 0.8 / 0.9 | 1.19 | 0.35 / 0.42 | 1.20 |
| imp:slab | 96 | 20 | 3.1 / 3.0 | 0.98 | 3.0 / 2.9 | 0.98 | 0.68 / 0.62 | 0.91 |
| imp:twoboxes | 24 | 20 | 0.1 / 0.1 | 0.91 | 0.1 / 0.1 | 0.88 | 0.00 / 0.01 | 4.14 |
| imp:twoboxes | 48 | 20 | 0.4 / 0.3 | 0.75 | 0.4 / 0.3 | 0.71 | 0.07 / 0.04 | 0.59 |
| imp:twoboxes | 96 | 20 | 2.0 / 2.0 | 1.02 | 1.9 / 2.0 | 1.01 | 0.25 / 0.21 | 0.84 |
| mesh:procedural | 64 | 6 | 1.8 / 2.0 | 1.14 | 0.8 / 1.0 | 1.24 | 0.07 / 0.07 | 1.02 |
| mesh:procedural | 128 | 6 | 5.5 / 5.8 | 1.04 | 2.9 / 3.5 | 1.19 | 0.12 / 0.13 | 1.08 |

Diagnostics (hook on): `NonManifoldTriangles` 0 in all 412 runs (A: 11920 in 52 runs), final self-intersecting results
0 (A: 1), 9959 face vertices (6287 clamped), 0 covered pairs. `--refine-clamped` measures the alternative of refining
around clamped face vertices (design note, section 6; the switch was used for this measurement and then removed).
Deviation of the result from the input (winding runs: `dev_max_cells`, `dev_gt002/01/02`).

Diagnostics of option B (hook on):

| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) | moved / vertices | multi-patch polys | 1 / 2 concave (of uniform single-patch quads) | SI triangles in / out / multi-patch / transition | covered / all pairs |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 64 | 20 | 4 | 121 | 0 | 0 | 0 (0) | 76 / 165010 | 0.63 % | 2.26 % / 0.021 % | 0 / 0 / 74 / 0 | 0 / 121 |
| T1 | 128 | 20 | 5 | 74 | 0 | 0 | 0 (0) | 65 / 435227 | 0.28 % | 1.86 % / 0.006 % | 0 / 0 / 77 / 0 | 0 / 74 |
| imp:boxminussphere | 24 | 20 | 4 | 29 | 0 | 0 | 0 (0) | 27 / 23472 | 2.33 % | 6.33 % / 0.061 % | 0 / 0 / 28 / 0 | 0 / 29 |
| imp:boxminussphere | 48 | 20 | 4 | 30 | 0 | 0 | 0 (0) | 29 / 95717 | 1.08 % | 3.17 % / 0.037 % | 0 / 0 / 27 / 0 | 0 / 30 |
| imp:boxminussphere | 96 | 20 | 10 | 72 | 0 | 0 | 0 (0) | 64 / 387514 | 0.52 % | 1.63 % / 0.015 % | 0 / 0 / 70 / 0 | 0 / 72 |
| imp:cone | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 14 / 10979 | 1.86 % | 7.93 % / 0.028 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:cone | 48 | 20 | 7 | 64 | 0 | 0 | 0 (0) | 60 / 44622 | 1.05 % | 3.86 % / 0.018 % | 0 / 0 / 60 / 0 | 0 / 64 |
| imp:cone | 96 | 20 | 8 | 129 | 0 | 0 | 0 (0) | 68 / 179654 | 0.46 % | 2.04 % / 0.015 % | 0 / 0 / 88 / 0 | 0 / 129 |
| imp:gyroid | 24 | 20 | 20 | 869 | 0 | 0 | 0 (0) | 868 / 57062 | 81.18 % | 18.20 % / 0.809 % | 1 / 0 / 860 / 0 | 0 / 869 |
| imp:gyroid | 48 | 20 | 20 | 1836 | 0 | 0 | 0 (0) | 1583 / 285980 | 69.87 % | 6.84 % / 0.135 % | 5 / 0 / 1844 / 0 | 0 / 1836 |
| imp:gyroid | 96 | 20 | 20 | 4026 | 0 | 0 | 0 (0) | 2381 / 1191069 | 3.62 % | 6.15 % / 0.045 % | 1 / 0 / 2889 / 0 | 0 / 4026 |
| imp:rotbox | 24 | 20 | 2 | 12 | 0 | 0 | 0 (0) | 10 / 14939 | 2.32 % | 5.96 % / 0.103 % | 0 / 0 / 10 / 0 | 0 / 12 |
| imp:rotbox | 48 | 20 | 1 | 3 | 0 | 0 | 0 (0) | 4 / 61079 | 1.16 % | 3.09 % / 0.032 % | 0 / 0 / 4 / 0 | 0 / 3 |
| imp:rotbox | 96 | 20 | 6 | 30 | 0 | 0 | 0 (0) | 32 / 247366 | 0.63 % | 1.51 % / 0.015 % | 0 / 0 / 33 / 0 | 0 / 30 |
| imp:slab | 24 | 20 | 7 | 60 | 0 | 0 | 0 (0) | 59 / 15443 | 55.30 % | 13.18 % / 0.377 % | 0 / 0 / 65 / 0 | 0 / 60 |
| imp:slab | 48 | 20 | 18 | 548 | 0 | 0 | 0 (0) | 528 / 101538 | 50.13 % | 4.65 % / 0.137 % | 2 / 0 / 537 / 0 | 0 / 548 |
| imp:slab | 96 | 20 | 3 | 24 | 0 | 0 | 0 (0) | 20 / 409110 | 18.00 % | 2.25 % / 0.017 % | 0 / 0 / 21 / 0 | 0 / 24 |
| imp:twoboxes | 24 | 20 | 3 | 14 | 0 | 0 | 0 (0) | 15 / 14549 | 3.85 % | 10.94 % / 0.122 % | 0 / 0 / 14 / 0 | 0 / 14 |
| imp:twoboxes | 48 | 20 | 5 | 18 | 0 | 0 | 0 (0) | 18 / 60220 | 1.84 % | 5.50 % / 0.058 % | 0 / 0 / 20 / 0 | 0 / 18 |
| imp:twoboxes | 96 | 20 | 3 | 30 | 0 | 0 | 0 (0) | 26 / 246323 | 0.93 % | 2.99 % / 0.030 % | 0 / 0 / 25 / 0 | 0 / 30 |
| mesh:procedural | 64 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 41418 | 0.00 % | 0.10 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |
| mesh:procedural | 128 | 6 | 0 | 0 | 0 | 0 | 0 (0) | 0 / 164518 | 0.00 % | 0.04 % / 0.000 % | 0 / 0 / 0 / 0 | 0 / 0 |

vertices not strictly inside their cell: 0 of 4252809; quads with 3 or 4 concave vertices: 0

Sharpness and the refinement alternative (winding solidify, hook off, median of two runs, the result's vertices to the
input in cells, after the default cleanup and reduction; A = master with option A, the same benchmark built against it;
"refining" = the measurement switch that refined around clamped face vertices in the adaptive path):

| set | variant | time s | triangles | max deviation (cells) | vertices > 0.02 / 0.1 / 0.2 cells | final SI runs |
|---|---|---|---|---|---|---|
| RotatedBox (cell 0.25) | A (master) | 0.65 | 658 | 0.143 | 16 / 5 / 0 of 331 | 0 |
| RotatedBox | B | 0.65 | 658 | 0.143 | 16 / 5 / 0 of 331 | 0 |
| RotatedBox | B, refining around clamped face vertices | 0.65 | 658 | 0.143 | 16 / 5 / 0 of 331 | 0 |
| T1 diagonal / 64 (20 meshes) | A | 7.9 | 70844 | 0.666 | 12197 / 2272 / 599 of 35404 | 0 |
| T1 diagonal / 64 | B | 6.8 | 68208 | 0.666 | 11796 / 2261 / 603 of 34086 | 0 |
| T1 diagonal / 64 | B, refining | 7.7 | 71352 | 0.666 | 12224 / 2267 / 598 of 35658 | 0 |
| T1 diagonal / 128 | A | 20.0 | 154178 | 0.539 | 23996 / 2445 / 583 of 77085 | 1 |
| T1 diagonal / 128 | B | 18.8 | 150774 | 0.539 | 23632 / 2439 / 586 of 75381 | 0 |
| T1 diagonal / 128 | B, refining | 20.7 | 155348 | 0.539 | 23992 / 2422 / 579 of 77666 | 0 |
| 6 procedural solids, 64 / 128 | A, B, B refining | 1.3-1.4 / 5.2-5.5 | 296 / 230 | 0.183 / 0.030 | 7 / 2 / 0, 1 / 0 / 0 | 0 |

The T1 solidify is 6-14 % faster with B in this second run (6.8 / 18.8 s against 7.9 / 20.0 s; the A/B table above,
run alternating, gives 1.00 / 1.08): less refinement for left-out quads, against the face test of every polygon. The
large T1 deviations (0.5-0.67 cells) are the reduction's tolerance and the same in all variants.
