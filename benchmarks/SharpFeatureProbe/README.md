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

### Guarantee audit (variant G, independent of the pipeline's certification)

Exact triangle-triangle tests (`ExactTriangleIntersection`) and nearest distances on sample points (corners, edge midpoints, centroid); T1 20 meshes / 6 procedural.
Raw wrap: 0 vertices < offset, 0 triangles cross the input (the class guarantee "the input is strictly inside" holds). Simplified wrap (A): T1 346k vertices, 1 below 0.5 offset (minimum 0.352),
0 inside the input; 692k triangles, 247 cross the input exactly (all 20 meshes), 10 have a sample point inside the input's solid (3 meshes); procedural: 29 of 68.8k cross (6/6).
The raw wrap's triangles already come within 0.007 offset of the input between their vertices (sagitta; 49k of 2.67M triangles have a sample < 0.5 offset), so a simplification within 0.5 offset can cross it.

## Variant KR: the library's sharp-feature pass (2026-11-12, PR #736)

`KR` runs `AlphaWrapFeaturePass` (`MeshRobustRepair.AlphaWrapSharpFeatures`, opt-in) on the raw wrap, then the pipeline's
simplification with the input kept clear; every variant now also gets the sharp-feature study's class metric (`EdgeClass`,
`ec_*`: the input's sharp edges by convex / concave, at the edge and on the offset surface's miter line; recall within 1 / 3
offsets) and the exact count of result triangles crossing the input (`fin_cross_input`). The simplification of every variant
keeps the input clear, as the library pipeline does since the contact fix.

```
dotnet run -c Release --project benchmarks/SharpFeatureProbe -- out.jsonl proc:all <T1 meshes> -- A,KR
```

Master 14a27e45 + the PR, one process, 4-core container, 300 / 3000, medians (rows: runs/alpha-wrap-3, aw3/features/kr_final.jsonl;
the study's probe variant on the same run: aw3/features/t1.jsonl):

| T1 (20) | A | KR | the study's probe K (same run) |
|---|---:|---:|---:|
| triangles / A | 1 | 0.722 | 0.721 |
| clean, exact crossings of the input | 20/20, 0 | 20/20, 0 | 20/20, 0 |
| pipeline time | 3.79 s | 5.06 s (pass 0.79 s) | |
| angle min / p01 | 4.67 / 11.5 | 5.53 / 11.0 | 5.53 / 11.0 |
| edge F 0.1 % | 0.61 | **0.875** | 0.876 |
| crease normal at 2 offsets | 14.7 deg | **4.67 deg** | 4.63 deg |
| concave crease on the miter line within 1 offset | 0.022 | **0.745** | 0.746 |
| convex within 1 offset | 0.832 | 0.957 | 0.957 |

Procedural solids (6): 6/6 clean, 0 crossings, 0.36x triangles, concave within 1 offset 0.016 -> 0.981, crease normal 5.8 -> 0.03
deg, the same operation counts as the study's probe on every solid. What it cannot do (the study): creases of features smaller
than alpha, corners (three planes).

## Mode `bound`: upper bound for provable sharp-feature recovery (2026-11-22, HP 4)

`Bound.cs`, for [PROVABLE_SHARP_WRAP_STUDY.md](../../docs/research/PROVABLE_SHARP_WRAP_STUDY.md) (plan #1280). No library change.

```
dotnet run -c Release --project benchmarks/SharpFeatureProbe -- bound out.jsonl <case>... [-- A,K,KC,KCK,KO,KOC,O]
python3 benchmarks/SharpFeatureProbe/bound_tables.py out.jsonl
```

Cases: `[soup:]<solid>[@rot|@far|@rotfar]` or `twobox[@...]`; solids: the six above, `gear`, `finegear`, `microgear`, `fillet`,
`wedge:<deg>`, `ridge:<deg>`, `notch:<alphas>`. The oracle `O` is the mitered offset of the clean reference, built by moving its
vertices; its sharp edges and corners are the network every variant is measured against. `KC` adds a corner step through the
pass's own checked move, `KO` gives the pass the clean reference of a dirty input.

Result (65 cases, master ed93ef26, tables in [results/bound_tables.md](results/bound_tables.md)): all 270 wrap-based results valid;
clean solids, crease length within 0.25 offsets of a sharp result edge: A 0.001, K 0.945, KC 0.954, O 1; corners A / K 0, KC 0.86;
soups K 0.00 (no shared edges; KO equals the clean K), two overlapping boxes K 0.80-0.82, KO 0.94-0.96; O valid on 64 / 65 (not on a slot one offset wide) with 4-8 times fewer triangles than K.

## Mode `varifold`: a kernel varifold distance as a quality measure (2026-11-22)

`Varifold.cs`, `VarifoldMeasure.cs`, for [VARIFOLD_WRAP_QUALITY_2026-11.md](../../docs/research/VARIFOLD_WRAP_QUALITY_2026-11.md).
No library change.

```
dotnet run -c Release --project benchmarks/SharpFeatureProbe -- varifold out.jsonl <item>... [-- sigmas=5,10,20,40 q=1]
python3 benchmarks/SharpFeatureProbe/varifold_tables.py out.jsonl [more.jsonl]
```

Items: a case of `bound` (plus `fin:<alphas>`, a thin wall, and `fslot`, a slot with filleted bottom edges): variants R (raw
wrap), A (pipeline), K (feature pass, then the simplification) against the oracle O and the input I; `floor:<case>` (O against
itself with another triangulation); `bevel:<w,...>`, `count:<w>`, `shift:<x,...>` (controlled damage of a prism, no wrap);
`pose:<case>` (similarities of both meshes); `selftest` (closed form: two parallel squares; exit code 1 above 1e-4). sigma in
offsets. The distance is `|S - T|^2 / (|S|^2 + |T|^2)` for the normal kernels mass (1), current (u), varifold (u^2), oriented2 and
oriented8 (((1 + u) / 2)^m), Gaussian in space; beside it the sampled Hausdorff distance, the mean distance, the mean normal
angle and the area ratio.

Before and after (16 clean cases, master 57623890, 4-core container; oriented8, sigma = 10 offsets, reference O; tables in
[results/varifold_tables.md](results/varifold_tables.md)):

| | plain wrap (A) | feature pass (K) | A / K median (min - max) |
|---|---:|---:|---:|
| varifold distance, median | 4.5e-4 | 9.8e-6 | 37.5 (3.2 - 81) |
| equivalent chamfer width, offsets | 0.8 - 3.4 | 0.16 - 0.5 (V-notches 0.8, 1.9) | |
| Hausdorff (sampled), offsets | 0.8 - 15 | 0.5 - 7 | 1.35 (1.0 - 2.2) |
| mean distance (Chamfer) | | | 32.5 (1.4 - 74) |
| mean normal angle | | | 23.1 (1.6 - 42) |
| area defect | | | 34.8 (9.6 - 390), smallest on results with 4 - 8 % of the creases missing |
| every varifold kernel against the input I | | | 0.99 - 1.14 (the offset dominates) |

Time of a fresh pair (median): 5.3 s at sigma = 10 offsets, 1.5 s at 20, 0.5 s at 40 (the pipeline: 4.9 s); sigma = 20 separates
as well as 10. The quadrature floor is 1e-6 (0.1 offsets of chamfer width); 2^k scales give the same value bit for bit, a
rotation, a translation or a scale alone agree to 1e-8, all three together at coordinates of 6e6 to 6e-6.

## Mode `soup`: creases and curvature without connectivity (2026-11-24)

Study [docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md](../../docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md): the
unoriented varifold of a broken soup (or a point cloud) for crease detection and curvature, against the welded dihedral,
`MeshPrincipalCurvature` after a weld, a PCA surface variation and a 2-jet fit. Procedural shapes only (`SoupShapes.cs`: box,
slot, vnotch, fillet, rbox), breakages in `SoupBreak.cs`, methods in `SoupFeatures.cs`, driver `SoupRun.cs`.

```
benchmarks/SharpFeatureProbe/run_soup.sh            # all runs (about 25 min on 4 cores), raw rows and tables in results/soup/
SharpFeatureProbe soup crease <out.jsonl> [tris=50000] [shapes=...] [breaks=clean;soup;nonconf;...] [c=2] [tol=0.3]
SharpFeatureProbe soup curv   <out.jsonl> [tris=50000] [shapes=fillet,rbox] [breaks=...] [eps=3,5]
SharpFeatureProbe soup cloud  <out.jsonl> [tris=50000] [shapes=fillet,rbox] [noise=0,0.05,0.2] [eps=3,5]
SharpFeatureProbe soup time   <out.jsonl> [sizes=10000,50000,100000,200000]
SharpFeatureProbe soup check  <out.jsonl>          # threads 1 / 4, repeated runs, the Similarity.All() list
python3 benchmarks/SharpFeatureProbe/soup_tables.py benchmarks/SharpFeatureProbe/results/soup
```

Result (median of box, slot, vnotch, fillet at 50k triangles; [results/soup/tables.md](results/soup/tables.md)): crease F1 of
the varifold two-plane test 0.998-1.000 on every breakage up to `mix:0.5` (welded dihedral: 0.56 on T-junctions, 0.08 with 20 %
flips, 0.03 at `mix:0.5`), 2.1 s at 143k triangles. Curvature: the varifold estimator beats weld + `MeshPrincipalCurvature` on
broken soups, the 2-jet fit beats or ties it everywhere. Bit-identical across threads and runs and under 2^k scales.

### `soup soupwrap`: the varifold crease input of the sharp-feature pass (2026-11-24, #1541)

```
SharpFeatureProbe soup soupwrap <out.jsonl> [tris=100000] [shapes=slot,vnotch,box,fillet] [breaks=...] [radius=3.5]
SharpFeatureProbe soup wrapbound <out.jsonl> [tris=100000]     # the cost bound of the design (before the build)
SharpFeatureProbe soup wrapbreak <out.jsonl> [tris=100000]     # the wrap's time per breakage (issue #1542)
```

For each case the wrap of the broken soup gets three passes:

| row | input of the pass | creases from |
|---|---|---|
| before | the soup | the dihedral |
| after | the soup | `VarifoldCreases` |
| oracle | the clean copy | its exact creases |

The ceiling is the clean copy wrapped. Metric: recall and precision of the result's sharp edges against the offset surface's
creases of the broken input, moved with the gaps (`MiterMetric`).

Result at 100k ([design/VARIFOLD_CREASE_INPUT.md](../../docs/design/VARIFOLD_CREASE_INPUT.md) section 9, raw rows
`results/soup/soupwrap.jsonl`):

- unwelded, T-junction, duplicated and flipped soups go from recall 0.84-0.98 to the ceiling (0.98-1.00);
- jittered soups with the fitted vertex planes (section 10) reach the oracle;
- the varifold costs 0.23-0.67 s, 9-19 % of the wrap;
- every result is clean.
