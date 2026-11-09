# SharpFeatureProbe

Measure-first probe for sharp features of the alpha wrap (Alpha Wrap 3.0 lever 6, owner 2026-11-09). No library change
(only an `InternalsVisibleTo` entry for `SimplifyWithin`). Sharp features gate the tet meshing.

```
dotnet run -c Release --project benchmarks/SharpFeatureProbe -- out.jsonl <mesh.stl>... proc:all [-- A,R,B,C1,C2,C3,D1]
python3 benchmarks/SharpFeatureProbe/analyze.py out.jsonl [more.jsonl] [--per-mesh]
```

Inputs: the 20 meshes of the T1 sweep (Thingi10K, fetched with `benchmarks/LevyClassification/fetch_thingi.py`, not
committed; ids in `results/tables.md`) and six procedural CAD-like solids (`Procedural.cs`: box, L-shape, cube minus an
octant, slot, V-notch, chamfered block). Warm process (a warm-up pipeline run first), one process, 4 cores.

| variant | what |
|---|---|
| A | the current pipeline: `MeshAlphaWrap` 300 / 3000, `SimplifyWithin` 0.5 offset (library defaults, quality pass off) |
| R | the raw wrap of A (guarantee reference) |
| B | `MeshWindingNumberSolidify` (adaptive dual contouring on the winding number), cell size searched (up to 4 runs) for A's triangle count; its default post-processing (cleanup, reduction) |
| C1 | post-pass on A: each vertex moves toward the QEF point of the planes of the input triangles within 1 alpha (Ju et al. 2002, singular values < 0.1 cut off, the vertex as mass point), planes shifted by one offset (the miter of the offset surface), 2 rounds, step 1 / 0.5 / 0.25 |
| C2 | the same with planes through the input itself (the vertex may come down to 0.1 offset from it) |
| C3 | C1 on the raw wrap, then the pipeline's simplification |
| D1 | A with half the alpha, same offset: the bound for a locally smaller alpha near creases (old option 4); only where the raw wrap has <= 120k triangles (9 of 20) |

A snap move is accepted only if, for the ring triangles: no exact self-intersection (`ExactTriangleIntersection`, the grid of
`AlphaWrapQualityPass`), no new hit with the input, normals turn <= 60°, min angle >= min(old, 10°), max angle <= max(old, 150°),
and the vertex keeps >= 0.9 offset (C1) / 0.1 offset (C2) from the input. Serial, fixed vertex order, deterministic.

Metrics (`MeshFidelityMetrics`, reference = the input): sharp-edge Chamfer and edge F-score (edges > 30°), "in offsets" =
Chamfer · size / offset; **crease normal**: on both faces of a sampled input crease, at 2 offsets (and 1 alpha) from the edge, the angle
between the face normal and the normal of the nearest result triangle (3000 random creases). Guarantees: `NotCleanReasons`
(closed, manifold, no self-intersections), vertex distance to the input (min over the offset, <1 means closer than the offset),
whether the result intersects the input (`hits input`), angles.

## Result (2026-11-09; raw rows not committed, tables in `results/tables.md`)

T1, 20 meshes, medians (paired ratios against A):

| variant | triangles | time s | clean | hits input | edge Chamfer (offsets) | edge F 0.1 % | edge F 0.5 % | crease normal 2δ mean / p90 | min angle / p01 | tris <0.5° / >170° |
|---|---|---|---|---|---|---|---|---|---|---|
| A | 1 | 4.1 | 20/20 | 20/20 | 4.75 | 0.61 | 0.938 | 14.9° / 55° | 4.7° / 11.5° | 0 / 0 |
| R (raw) | 4.0 | 2.8 | 20/20 | 0/20 | 4.73 | 0.61 | 0.939 | 14.7° / 54° | 4.7° / 20.1° | 0 / 0 |
| B (DC) | 0.70 | 13.9 (search 20.7) | 19/20 | 20/20 (on the input) | 0.68 | 0.98 | 0.992 | 2.8° / 4.5° | 0.09° / 1.8° | 24 / 17765 |
| C1 | 1 | 4.8 | 20/20 | 20/20 | 3.28 | 0.75 | 0.958 | 8.0° / 23° | 4.7° / 11.1° | 0 / 0 |
| C2 | 1 | 5.5 | 20/20 | 20/20 | 2.82 | 0.76 | 0.953 | 10.7° / 43° | 4.7° / 10.6° | 0 / 0 |
| C3 | 0.84 (1 fallback) | 5.8 | 20/20 | 17/20 | 3.21 | 0.76 | 0.965 | 7.9° / 22° | 4.7° / 11.3° | 0 / 0 |
| D1 (9 meshes) | 1.71 | 10.2 | 9/9 | 9/9 | 2.34 | 0.70 | 0.998 | 7.1° / 17.5° | 10.1° / 11.2° | 0 / 0 |

Paired edge-Chamfer ratio to A: B 0.22 (19 better, 0 worse), C1 0.87, C2 0.68, C3 0.86, D1 0.75. Crease-normal ratio: B 0.19, C1 0.58, C2 0.78, C3 0.56.
Procedural CAD solids (6): B is exact (Chamfer 0.0024 of A, 30-160 triangles, but 35 triangles > 170°, min angle 0.9°); A, R at 4.75-1.8 offsets;
C1 / C3 halve the crease normal error (5.8° → 2.2°) at equal Chamfer, C2 halves the Chamfer; D1 0.74.

Findings: (1) B is the only variant that is sharp (3.5x lower Chamfer, edge F 0.1 % 0.98), but its triangles have no angle control (min 0.09°, 17765 triangles > 170°
over the 20 meshes), it lies on the input (no offset, it is not outside it), one of 20 results self-intersects (116871) and it needs 3x the time. (2) The vertex post-pass
recovers about 17-42 % of B's Chamfer gain and about 40 % of its edge F at 0.1 %, with every guarantee and angle floor unchanged, at +15-35 % time: it can only move
vertices, but a crease needs vertices *on* it and edges along it. (3) The simplified wrap A touches the input (20/20, a chord across a convex crease; the raw wrap never does, minimum vertex
distance 0.35 offset after simplification): independent of this work, worth a note on the offset guarantee. (4) A smaller alpha helps linearly and costs 1.7x triangles and 2.5x time (D1).
