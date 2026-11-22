# The cost of the rounding: box neighbours, `near`, tree and setup (study, 2026-11-22)

Status: **research note, no library code.** Owner go 2026-11-22 for a study of hard problem 3, parts (b) to (d)
([roadmap/research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), plan #1258):
measurements and options only, no default or output change, no library speed PR. Boolean speed levers are stopped
(owner 2026-11-16), so every lever below is an option with numbers; the owner decides whether one is built
(section 9).

The tools are in `benchmarks/SnapRoundingExperiment` (`gen`, `cull`, `SplitBoxes.cs`, `trace/instrument_phases.py`,
`trace/run_ab_phases.sh`, `trace/summarize_phases.py`); raw output in `trace/results/cost_*_2026-11-22.*`. Measured on
master `57623890`, 4 cores, the cloud container of the session, .NET SDK 10.0.115. The procedural cases are generated
in code; the Thingi10K models were fetched at run time and are not committed.

Not repeated here (measured and closed in [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) sections 5.1 to 5.5,
[EXTREME_RUNTIME_BOOLEANS.md](../perf/EXTREME_RUNTIME_BOOLEANS.md) section 9 and
[EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md)): the certified empty ball, clearance
skins, the stop at the evaluation limit (#725), node culling by the query's axes, the exact plane-side test, wide trees
and SIMD filters, and lever 1a (no tree of all triangles), which is part (d).

## 1. Result in short

- **The figures of 46529be still hold in size.** On the 33 rotated Thingi10K operations the rounding is 3-77 % of a
  boolean (median 22 %, 58.0 of 226.5 s = 26 %; then 1-69 %, median 23 %, 27 %). The booleans are 20 % faster in the
  sum, the rounding 25 %. 519511 union: 4.1 of 12.2 s (then 4.7 of 17.0 s).
- **Part (b) is mostly pair tests on the box hits, not their enumeration.** With 300 to 1500 box hits per query
  triangle, listing the hits is 19-37 % of the first search; the rest is the pair test on each hit, of which none
  intersects.
- **Upper bounds, end to end** (the part set to zero): the first search −5.5 % of the 33 operations and −31 % of a
  procedural set built to be the worst case; `near` −8.9 % and −2.8 %; tree and setup −1.5 % and −1.7 %.
- **(b), the one candidate that pays: the separating axes of section 5.3, computed once per triangle** and used in the
  full searches only (normally the first). Inside the rounding: −3.5 % of all 48 operations, −8.9 % on the procedural thin set, −5.0 % on the
  clean Thingi10K results, nothing on the dirty ones; 48 of 48 result hashes the same. Section 5.3 measured −1.6 % for
  the same rule with the axes computed per pair in every pair test.
- **(b), tighter volumes do not pay.** Split-clipped sub-boxes (spatial splits) cut the box hits and the pair tests to
  a third to a seventh on most thin-triangle results, and lose end to end as prototyped (+3.5 %, one round; clear on
  the Thingi10K results, inside the noise on the procedural ones): the sub-boxes and their tree are built for all
  triangles, and a query walks the larger tree once per sub-box. An 18-DOP per triangle removes fewer pairs than
  the axes at a similar cost.
- **(b) and (c), a new finding: on one model the library's box tree is slow.** On 102375 a query visits 1123
  nodes of the Morton-ordered tree for 53 hits; a median-split tree visits 230. With the median split forced in the
  rounding, its three booleans take 5-26 % less (9.2 to 6.9 s for the union), same hashes. The median build is too
  slow to be the fix (other large results take up to 3 times as long), and why the Morton order fails on this model is
  not diagnosed.
- **(c), `near` has three costs, and the largest is the linear list.** The list of triangles changed since the tree of
  the changed triangles was built (about 100 entries per query) is 4.2 % of the 33 operations, the start tree 3.3 % (more
  than half of it on 102375), the rebuilds of the changed tree 0.9 %. Keeping the boxes of the list instead of
  computing them per query takes 17.7 % off the rounding of the 24 operations with many evaluations (62.9 to 51.8 s;
  their booleans −6.9 %, of which 5 points are the rounding), 48 of 48 hashes the same.
- **(d) has no lever beyond lever 1a.** Tree and setup over all triangles are 1.5-2.0 % of the sets (10-15 %
  on 1063860, where 98 % of the triangles are exact).

## 2. Cases and method

- **Thingi10K, rotated** (the set of SNAP_ROUNDING_3D.md 4.1, for a like-for-like comparison): 67709, 42327, 1368057,
  102375, 498458, 486343 (open or self-intersecting inputs; "dirty") and 519511, 1517923, 1063860, 668056, 119829
  ("clean"), each with the copy of `BooleanComparison export`, three operations: 33.
- **Procedural** (`SnapRoundingExperiment gen`, `ProceduralThin.cs`): shapes with long thin triangles on flat faces,
  each with the same rotated, shifted copy, three operations each: 15.

  | shape | what it is | triangles of A | result of the union | box hits per query |
  |---|---|---:|---:|---:|
  | `gear:500` | prism over a gear outline of 500 teeth, height 2 | 8 000 | 169 396 | 630 |
  | `cyl:8000` | prism over a regular 8000-gon | 32 000 | 88 032 | 1506 |
  | `fins:400` | base plate with 400 thin fins (a union of boxes) | 6 412 | 473 914 | 449 |
  | `plate:24` | plate with 24 x 24 holes of 24 sides | 57 612 | 154 594 | 30 |
  | `sphere:60` | the control: a cube sphere | 41 772 | 61 880 | 15 |

  The first three reproduce the pattern of 519511 union (316 hits); `plate` is a result with many edits, `sphere` one
  with few rounded triangles.
- **Baseline:** `BooleanComparison profile --repeat 1 --safe-rounding <case>`, the default runtime settings, one run
  (`cost_baseline`).
- **Trace:** `trace/instrument_phases.py` patches `MeshRoundingRepair.cs` in a throw-away worktree: the phases of
  `Apply`, the tree builds, the searches, and `near` split into its three sources. The old `instrument.py` matches
  46529be only. The trace and every A/B ran with `DOTNET_TieredPGO=0` (the measurement hazard of
  [hotspots.md](../roadmap/hotspots.md), #825); the booleans are 3-5 % slower that way than the baseline. The timers
  inside `near` (four per query) inflate it: 21.2 s in the trace against 15.1 s with one timer per query.
- **The first search outside the library:** `SnapRoundingExperiment cull` repeats it on the rounded result with other
  candidate sets (4 threads, the second round of each variant). Every variant must report the same intersecting pairs.
- **A/B inside the rounding:** the same worktree with a switch per candidate (`G3_FIRST_SEARCH`, `G3_TREE_MEDIAN`,
  `G3_NEAR_PENDING`), one process per case and variant in turn, per operation the faster round; the result hash of
  every round is compared with the base. The base runs first for each case.
- Shares are of wall time at 4 threads and include the collections the code causes.

## 3. Where the rounding time goes on master

### 3.1 The share of the rounding

| group | operations | rounding share of the boolean | rounding of the booleans | on 46529be |
|---|---:|---|---:|---:|
| Thingi10K rotated, all | 33 | 3-77 %, median 22 % | 58.0 of 226.5 s (26 %) | 76.9 of 282.2 s (27 %) |
| 519511 | 3 | 34 / 13 / 17 % | 4.1 / 0.9 / 1.2 s of 12.2 / 6.8 / 6.8 s | 4.7 / 1.1 / 1.6 s of 17.0 / 10.5 / 11.3 s |
| 1517923 | 3 | 20 / 21 / 23 % | 0.7 / 0.3 / 0.4 s of 3.4 / 1.5 / 1.7 s | 0.8 / 0.5 / 0.5 s |
| 67709 | 3 | 22 / 15 / 14 % | 3.3 / 1.9 / 1.7 s of 15.3 / 13.0 / 12.3 s | 2.9 / 2.3 / 2.0 s of 7.9 / 5.1 / 4.7 s |
| 42327 | 3 | 26 / 11 / 12 % | 5.6 / 1.7 / 1.8 s of 21.7 / 15.8 / 15.3 s | 10.2 / 3.1 / 2.9 s |
| procedural (traced run) | 15 | 15-66 %, median 42 % | 49.9 of 121.7 s (41 %) | - |

- The 15 clean Thingi10K operations give the same result hashes as on 46529be, and 6 of the 18 dirty ones; the others
  changed with #725, #755 and later rounding work.
- 67709 takes twice as long as on 46529be, outside the rounding (the classification; #837 saw it at 21.5 s under
  option A). Not part of this study.

### 3.2 The parts of the rounding

`summarize_phases.py phases` (the traced run; "setup" is the anchors, the component scales, the range scan, the
position map and the exact flags; "passes and later" is everything after the first search):

| operation | queried triangles | boolean | rounding | first search | first tree | setup | passes and later | of it `near` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| gear_500 union | 161 091 | 8.08 s | 3.43 s | 3.17 s | 0.10 s | 0.11 s | 0.05 s | 0.00 s |
| cyl_8000 intersection | 43 922 | 14.42 s | 5.98 s | 4.97 s | 0.01 s | 0.02 s | 0.98 s | 0.15 s |
| fins_400 union | 467 517 | 14.57 s | 7.90 s | 7.20 s | 0.29 s | 0.20 s | 0.21 s | 0.00 s |
| plate_24 difference | - | 4.85 s | 3.19 s | 0.17 s | 0.03 s | 0.04 s | 2.95 s | 1.36 s |
| sphere_60 union | 2 628 | 0.29 s | 0.08 s | 0.01 s | 0.02 s | 0.04 s | 0.01 s | 0.00 s |
| 519511 union | 359 263 | 11.92 s | 4.81 s | 4.26 s | 0.16 s | 0.20 s | 0.19 s | 0.01 s |
| 1517923 difference | 43 211 | 2.10 s | 0.52 s | 0.39 s | 0.03 s | 0.05 s | 0.04 s | 0.00 s |
| 1063860 union | 8 657 | 2.45 s | 0.61 s | 0.18 s | 0.10 s | 0.16 s | 0.18 s | 0.00 s |
| 668056 union | 185 940 | 3.35 s | 1.08 s | 0.73 s | 0.08 s | 0.15 s | 0.11 s | 0.00 s |
| 119829 union | - | 4.03 s | 3.03 s | 0.03 s | 0.08 s | 0.16 s | 2.76 s | 1.91 s |
| 67709 union | - | 15.83 s | 4.14 s | 0.10 s | 0.04 s | 0.04 s | 3.98 s | 2.07 s |
| 42327 union | 61 916 | 22.28 s | 6.43 s | 0.46 s | 0.07 s | 0.04 s | 5.85 s | 1.85 s |
| 1368057 union | 67 878 | 20.36 s | 7.90 s | 1.75 s | 0.11 s | 0.10 s | 5.95 s | 1.02 s |
| 102375 union | 19 991 | 9.35 s | 6.49 s | 0.23 s | 0.10 s | 0.17 s | 5.98 s | 4.23 s |
| 498458 union | 187 376 | 12.50 s | 3.76 s | 0.43 s | 0.10 s | 0.13 s | 3.09 s | 1.65 s |

All 48 rows are in `cost_phases_*`. Summed over the 33 Thingi10K operations the first search is 19 % of the rounding
and the passes with what follows them 76 %; over the 15 procedural ones 76 % and 19 %.

### 3.3 The first search: hits, enumeration, pair tests

`cull`, the unions, 4 threads; "enumeration" lists the box hits and tests no pair:

| union | queries | box hits per query | pair tests | intersecting | search | enumeration alone |
|---|---:|---:|---:|---:|---:|---:|
| gear_500 | 161 099 | 630 | 54.0M | 0 | 1.99 s | 0.45 s (23 %) |
| cyl_8000 | 55 632 | 1506 | 47.2M | 0 | 1.37 s | 0.26 s (19 %) |
| fins_400 | 467 529 | 449 | 109.6M | 0 | 5.07 s | 1.46 s (29 %) |
| plate_24 | 83 486 | 30 | 1.4M | 82 | 0.13 s | 0.05 s |
| sphere_60 | 2 628 | 15 | 0.02M | 0 | 0.01 s | 0.00 s |
| 519511 | 359 333 | 316 | 62.9M | 0 | 2.95 s | 0.94 s (32 %) |
| 1517923 | 43 474 | 182 | 4.7M | 0 | 0.19 s | 0.04 s (23 %) |
| 1063860 | 8 727 | 355 | 2.8M | 0 | 0.10 s | 0.02 s |
| 668056 | 186 022 | 85 | 10.6M | 0 | 0.56 s | 0.21 s (37 %) |
| 1368057 | 71 637 | 843 | 33.2M | 162 321 | 1.21 s | 0.30 s (25 %) |

- The range of 46529be (20-355 hits, 855 on 1368057) is unchanged: 15-1506 here, 316 on 519511 union.
- The enumeration is 19-37 % of the search. So "the box-neighbour
  enumeration" of the roadmap is, in time, two thirds to four fifths pair tests on hits that are not neighbours. A
  better enumeration alone can save at most the pair tests and its own part; a cheaper rejection of a hit saves the
  pair tests only.

## 4. Upper bounds, before any prototype

`summarize_phases.py bound` (the traced run): the boolean with the part taken out completely.

| set | booleans | rounding | first search = 0 | first tree and setup = 0 | `near` = 0 |
|---|---:|---:|---:|---:|---:|
| Thingi10K rotated, 33 | 237.2 s | 68.1 s | −5.5 % | −1.5 % | −8.9 % (−6.6 % with one timer per query) |
| procedural, 15 | 121.7 s | 49.9 s | −31.3 % | −1.7 % | −2.8 % |

Per operation the largest: first search 62 % of gear_500 intersection and 36 % of 519511 union; `near` 47 % of 119829
union and 45 % of 102375 union; tree and setup 10-15 % of 1063860 union and difference. On the dirty Thingi10K
operations the first search is 0-9 % and the bound is in `near` and the rest of the evaluations.

These bounds decide the scale: on the 33 Thingi10K operations no single part of (b) to (d) can reach 10 % of the summed
booleans (together 16 %); over all 48 operations the first search is bounded by 14 % and the three parts by 23 %. (b)
matters on results with hundreds of box hits per query, (c) on results with many accepted edits.

## 5. Part (b): candidates for the first search

### 5.1 Library and literature check

In the library: `TriangleBoxTree` (Morton order above 4096 boxes, median split below, leaves of 8; no oriented or
k-DOP volume anywhere in the library), `TriangleTriangleFilter` (the projection filter in front of the exact pair
test), the separating-axis lemma of SNAP_ROUNDING_3D.md 5.3 with its Z3 check, and the `cull` prototype with its own
median-split tree. Literature (papers only; [S] abstract or snippet read, [M] from memory, not re-read):

| idea | source | what it says for this problem |
|---|---|---|
| Sub-boxes of large primitives | M. Ernst, G. Greiner, *Early split clipping for bounding volume hierarchies*, IEEE Symposium on Interactive Ray Tracing 2007 [S]; M. Stich, H. Friedrich, A. Dietrich, *Spatial splits in bounding volume hierarchies*, HPG 2009 [S]; H. Dammertz, A. Keller, *The edge volume heuristic*, 2008 [S] | the boxes of large primitives are refined by recursive split clipping before the build, "more than a factor of three" for ray tracing; a primitive is then referenced several times |
| Fixed-direction slabs | J. Klosowski, M. Held, J. Mitchell, H. Sowizral, K. Zikan, *Efficient collision detection using bounding volume hierarchies of k-DOPs*, IEEE TVCG 4(1), 1998 [S] | a k-DOP is the box plus slabs along fixed directions; the overlap test is interval comparisons |
| Oriented boxes | S. Gottschalk, M. Lin, D. Manocha, *OBBTree*, SIGGRAPH 1996 [M]; S. Woop et al., *Exploiting local orientation similarity for efficient ray traversal of hair and fur*, HPG 2014 [M] | tight on tilted and thin geometry; the overlap test is 15 axes with products, so each needs a rounding margin |
| Dead space in spatial joins | D. Šidlauskas et al., *Improving spatial data processing by clipping minimum bounding boxes*, ICDE 2018 (through [CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md) row R) | clip points on tree nodes; closed there by analogy with the node culling of 5.3 |
| Tree quality of a Morton build | C. Lauterbach, M. Garland, S. Sengupta, D. Luebke, D. Manocha, *Fast BVH construction on GPUs*, CGF 28(2), 2009 [S] | the Morton build is an order of magnitude faster and "can reduce ray tracing performance by up to half" against a surface-area build; their hybrid uses the Morton order at the top only |

No source found for a self-join of rounded triangles in particular (terms: "snap rounding candidate pairs", "self
intersection broad phase thin triangles", "spatial join oriented bounding volume exact"). No source code was opened.

### 5.2 The candidates

| candidate | rule | why it is conservative |
|---|---|---|
| `box+axesf` | section 5.3: a pair is dropped if the two triangles are apart along one of their 8 axes (normal and the in-plane edge normals), here with the axes and intervals of every triangle computed once and kept flat (20 doubles per triangle) | the lemma of 5.3 (any vector is a valid axis; the comparison has a margin of 8u; checked with Z3). A build needs its guard against subnormal products (5.3) |
| `box+dop` | an 18-DOP: per triangle the intervals along x±y, x±z, y±z, each sum rounded outwards by one ulp; a pair is dropped if two intervals are apart | a sum of two doubles is within one ulp, so the widened interval contains the exact one |
| `split` | at most k sub-boxes per triangle: its box cut into slabs along its longest axis, each sub-box around the part of the triangle in the slab (`SplitBoxes.cs`); a tree over all sub-boxes, a query per sub-box of the query triangle, a pair tested once | the parts cover the triangle, so two triangles with a common point have two sub-boxes that overlap; the crossings of the edges with the cuts are computed in doubles and widened by 32u of the larger coordinate (the bound is derived in the file; not checked with Z3) |
| `split+axesf`, `split+dop` | both | both |
| tree quality | the same hits from a tree built by a median split of the centroids instead of the Morton order | the same boxes; only the grouping differs |
| a grid or hash of cells | not built: over whole boxes it lists the same hits as the tree; over sub-boxes its count of hits is `split`'s (the `split-enum` column bounds it), and one cell size does not fit results whose triangles span three orders of size (box diagonals 0.2 to 53 on 42327) | - |
| oriented boxes at the tree nodes | not built: 15 axes per node pair with a margin each; section 5.3 measured the cheaper form (the query's 4 axes against node boxes) as "costs more than it saves" | - |

### 5.3 The first search outside the library

`cull`, unions, 4 threads; time of the search, box hits per query, pair tests left. The builds are not in the times:
the axes of all triangles take 2 to 115 ms (`build_ms_axesf`), the DOPs 1 to 95 ms, the sub-boxes and their tree 0.04
to 1.0 s for k = 8.

| union | box (the library) | enumeration alone | box+axesf | box+dop | split, k = 8 | split+axesf |
|---|---:|---:|---:|---:|---:|---:|
| gear_500 | 1.99 s, 630, 54.0M | 0.45 s | 0.94 s, 4.2M | 1.19 s, 19.3M | 1.20 s, 120, 10.2M | 1.15 s, 4.1M |
| cyl_8000 | 1.37 s, 1506, 47.2M | 0.26 s | 0.55 s, 1.0M | 0.52 s, 7.1M | 0.59 s, 258, 7.7M | 0.42 s, 0.7M |
| fins_400 | 5.07 s, 449, 109.6M | 1.46 s | 2.91 s, 8.5M | 3.40 s, 39.5M | 4.62 s, 129, 31.0M | 4.02 s, 8.5M |
| plate_24 | 0.13 s, 30, 1.4M | 0.05 s | 0.12 s, 0.7M | 0.10 s, 0.8M | 0.21 s, 18, 0.8M | 0.24 s, 0.7M |
| 519511 | 2.95 s, 316, 62.9M | 0.94 s | 1.65 s, 3.5M | 1.52 s, 7.3M | 2.86 s, 48, 9.0M | 2.93 s, 3.2M |
| 1517923 | 0.19 s, 182, 4.7M | 0.04 s | 0.11 s, 0.6M | 0.10 s, 1.3M | 0.18 s, 51, 1.3M | 0.17 s, 0.6M |
| 668056 | 0.56 s, 85, 10.6M | 0.21 s | 0.38 s, 1.5M | 0.33 s, 2.2M | 0.57 s, 22, 2.1M | 0.76 s, 1.4M |
| 1368057 | 1.21 s, 843, 33.2M | 0.30 s | 0.72 s, 1.7M | 0.97 s, 18.7M | 2.95 s, 573, 22.3M | 2.63 s, 1.6M |
| 102375 | 0.17 s, 53, 0.65M | 0.18 s | 0.16 s, 0.2M | 0.15 s, 0.35M | 0.39 s, 35, 0.44M | 0.39 s, 0.2M |

Every variant reports the same intersecting pairs on all 16 unions (`same_pairs`; `cost_cull`). Nine of the unions
have no intersecting pair, so the evidence of the pair sets rests on the other seven (4 to 162 321 pairs); the
enumeration-only variants test no pair.

- **Axes:** 86-98 % of the pair tests go on the thin-triangle results, and the search takes 32-60 % less. What is left
  is the enumeration and the axis test itself, about equal.
- **DOP:** 44-88 % of the pair tests; as fast as the axes where the result's faces are near the fixed directions
  (519511, 668056), slower where they are not (gear, fins, 1368057). It could run in front of the axes; not measured.
- **Sub-boxes:** the hits drop to a third to a seventh (630 to 120, 316 to 48; only 843 to 573 on 1368057), and the
  enumeration gets two to three times slower: eight queries per triangle in a tree of five times the boxes (`split-enum` 0.92 against 0.45 s on gear_500,
  2.55 against 0.94 s on 519511). k = 2, 4, 16 on gear_500 and 519511 (`cost_parts`): 1.53 / 1.37 / 1.54 s and 2.76 /
  2.35 / 3.31 s; no k beats the axes on the plain boxes (0.94 and 1.65 s).

### 5.4 Inside the rounding

`run_ab_phases.sh`, the booleans as a whole, per operation the faster round (three rounds for the base and the axes,
one for the others):

| group | operations | base: booleans, rounding | axes | split | split+axes |
|---|---:|---:|---:|---:|---:|
| procedural thin (gear, cyl, fins, plate) | 12 | 111.2 s, 45.7 s | **−8.9 %**, rounding −21.3 % | +1.3 %, rounding −2.6 % | −1.0 %, rounding −1.5 % |
| procedural control (sphere) | 3 | 0.7 s, 0.2 s | −3.0 % (0.02 s: noise) | +4.4 % (noise) | +51.8 % (0.4 s; one round, not looked into) |
| Thingi10K rotated, clean | 15 | 48.7 s, 15.3 s | **−5.0 %**, rounding −12.7 % | +8.0 %, rounding +26.3 % | +9.1 %, rounding +28.2 % |
| Thingi10K rotated, dirty | 18 | 180.1 s, 50.3 s | +0.3 %, rounding −1.7 % | +3.6 %, rounding +11.1 % | +3.5 %, rounding +10.1 % |
| all | 48 | 340.6 s, 111.5 s | **−3.5 %**, rounding −11.3 % | +3.5 %, rounding +7.6 % | +2.9 %, rounding +8.0 % |
| result hashes equal to the base | | | 48 of 48 (three rounds) | 48 of 48 | 48 of 48 |

(The split columns compare with the base of the same round; their base sums are 114.3, 0.8, 50.5, 183.7 and 349.3 s.)

The axes per operation, where the gain is: gear_500 6.97 / 7.82 / 6.67 to 5.98 / 5.86 / 5.06 s; fins_400 union 14.33
to 11.80 s; cyl_8000 union 14.74 to 13.99 s; 519511 union 11.42 to 10.07 s (rounding 4.49 to 3.13 s); 668056 union
3.31 to 2.95 s. Eight operations are 2-9 % slower in their faster round, seven of them within the spread of their base
rounds; the one that is not, 119829 intersection (0.23 to 0.25 s), has the same rounding time with and without the
axes (0.05 s).

- **Against section 5.3:** there the prefilter computed its eight axes for every pair and ran in every pair test of the
  rounding: −1.6 % in the sum, +12 to +30 % on 67709. Computed once per triangle and used in the full searches only, it
  costs 2-115 ms up front (as measured in `cull`) and nothing in the evaluations.
- **The sub-boxes lose** once their build for all triangles is inside the measurement, also together with the axes:
  on the Thingi10K results the rounding takes 11-26 % longer (the base rounds spread by about 1 %); on the procedural
  thin set +1.3 % and −1.0 % are noise. One round, and the prototype inside the rounding allocates a set and a list
  per query triangle where `cull` uses a stamp array, which counts against it; `cull` itself (5.3) shows no k at which
  the sub-boxes beat the axes on plain boxes.
- **What the axes leave:** they reach about 30 % of the bound of section 4 on the thin-triangle results (−8.9 of −31 % on the
  procedural set, −5.0 % on the clean Thingi10K operations, where the bound is −17 %). The rest is the
  enumeration of the hits and the axis test on each.

### 5.5 Tree quality: one model stands out

`cull --visits --serial` (`cost_visits`): the nodes a query of the first search visits, the same hits.

| union | box hits per query | nodes visited: library tree (Morton) | median split | enumeration, 1 thread: library | median split |
|---|---:|---:|---:|---:|---:|
| 102375 | 53 | **1123** | 230 | **0.57 s** | 0.08 s |
| 42327 | 82 | 235 | 293 | 0.27 s | 0.28 s |
| 519511 | 316 | 471 | 532 | 3.46 s | 3.52 s |
| gear_500 | 630 | 454 | 804 | 1.65 s | 1.88 s |
| 498458 | 26 | 104 | 100 | 0.34 s | 0.33 s |

- On four of these five unions the two trees enumerate at about the same speed (the Morton tree is the better one on
  gear_500). At 4 threads on all 16 unions (`enum` against `own-enum` in `cost_cull`, one run) 102375 stands out with
  6.5 times; the others are within a factor of 1.8 either way (1063860 15 against 27 ms, 42327 110 against 77 ms). On
  102375 the Morton tree visits five times the nodes; its triangles are of one size (box diagonal 1.5 in the median,
  3.6 at most, the mesh 173), so it is not a case of a few large boxes. Not diagnosed.
- The same tree serves `near`: on 102375 union 2.9 of the 4.2 s of `near` are the start tree (45 µs per query against
  9-21 µs on the other dirty results with as many hits, section 6).
- **Probe inside the rounding** (`G3_TREE_MEDIAN=1`: every tree the rounding builds uses the library's median split;
  three rounds): 102375 union 9.21 to 6.85 s, intersection 3.93 to 3.72 s, difference 5.31 to 4.68 s (rounding 6.39 to
  3.77, 2.76 to 2.44, 3.42 to 2.75 s); the same hashes. On the other results the probe loses, because the library's
  median build is serial and slow on large inputs (fins_400 union: rounding 7.6 to 28.8 s, the boolean 2.5 times; its
  intersection 2.9 times; all 48 +24.6 %, one round, 48 of
  48 hashes the same). So the gain on 102375 is the bound of a better tree there; a fix needs a build that is as fast
  as the Morton one, and first the reason why the Morton order fails on this model.
- `TriangleBoxTree` also serves the boolean's own searches; whether they are slow on 102375 too was not measured.

## 6. Part (c): `near`

`near` returns the current triangles whose box overlaps a query box, from three sources: the tree of the start
(triangles changed since are skipped), a tree of the changed triangles (rebuilt when more than 256 changed since its
build), and a linear list of the triangles changed since that build. `summarize_phases.py near` (traced; the timers
inflate the times, section 2):

| operation | `near` | queries | per query | tree of the start | changed tree: builds | its queries | linear list | list length |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 119829 union | 1.91 s | 107 743 | 17.7 µs | 0.19 s | **0.78 s** (264 builds of 7365 triangles) | 0.04 s | **0.88 s** | 130 |
| 498458 union | 1.65 s | 78 363 | 21.0 µs | 0.19 s | **0.74 s** (223 of 7097) | 0.04 s | 0.65 s | 124 |
| 67709 union | 2.07 s | 210 905 | 9.8 µs | 0.25 s | 0.11 s (85 of 1145) | 0.05 s | **1.60 s** | 122 |
| 42327 union | 1.85 s | 138 443 | 13.3 µs | 0.54 s | 0.05 s | 0.08 s | **1.13 s** | 126 |
| 102375 union | 4.23 s | 94 429 | 44.8 µs | **2.92 s** | 0.08 s | 0.20 s | 1.00 s | 130 |
| 1368057 union | 1.02 s | 26 572 | 38.4 µs | 0.69 s (753 hits per query) | 0.03 s | 0.04 s | 0.26 s | 123 |
| 486343 union | 0.47 s | 52 601 | 8.9 µs | 0.15 s | 0.00 s | 0.01 s | 0.28 s | 66 |
| plate_24 difference | 1.36 s | 104 481 | 13.0 µs | 0.27 s | 0.20 s (103 of 3263) | 0.03 s | 0.83 s | 112 |
| **33 Thingi10K operations** | 21.2 s (8.9 % of the booleans) | | | 7.9 s (3.3 %) | 2.1 s (0.9 %) | 0.8 s | 10.0 s (4.2 %) | |

All 24 operations with 0.15 s or more of `near` are in `cost_phases_*`.

- **The linear list is the largest part.** A query computes the box of each listed triangle from the mesh (98 on
  average over the 33 operations, 110-130 where `near` is heavy; 60-70 ns each), 36 times per accepted edit on 67709. The diagnosis left open in section 5.5 of
  SNAP_ROUNDING_3D.md ("the changed-triangle tree and its linear list are the suspects") is: the list first, the
  rebuilds second, and only where many edits are accepted.
- **The rebuilds of the changed tree** matter where thousands of triangles are changed before the tree of the start is
  rebuilt (119829, 498458, plate): every 256 changes the tree of all changed triangles is rebuilt, 7000 of them.
- **The tree of the start** is the cost on 102375 (section 5.5) and where a query has hundreds of hits (1368057).

**Candidate: keep the boxes of the list.** `G3_NEAR_PENDING=cached` computes the boxes of the list once per value of
the version counter (which `mark_dirty` raises) and compares six doubles per entry in a query; the same triangles in
the same order. It is correct only while every edit marks its triangles before the next `near`; the removal of a
triangle alone does not raise the counter, so a build has to state and test that invariant. Checked with the library's own check of every `near` against a scan of all triangles
(`CheckOnThisThread`, here `G3_CHECK_NEAR=1`) on the three operations of 486343 and of sphere_60: no difference, the
same hashes.

`run_ab_phases.sh` with `VARIANTS="base pending"`, two rounds, the eight cases with many evaluations (`cost_ab_pending`):

| operation | boolean | with the kept boxes | rounding | with the kept boxes |
|---|---:|---:|---:|---:|
| 119829 union | 4.07 s | 3.41 s | 3.02 s | 2.41 s |
| 67709 union | 17.43 s | 14.62 s | 4.24 s | 2.60 s |
| 67709 intersection | 14.01 s | 12.66 s | 2.75 s | 1.93 s |
| 42327 union | 24.56 s | 22.27 s | 6.65 s | 5.26 s |
| 102375 union | 9.22 s | 8.41 s | 6.28 s | 5.38 s |
| 1368057 union | 20.98 s | 21.14 s | 8.00 s | 7.51 s |
| 498458 union | 12.70 s | 12.40 s | 3.68 s | 3.28 s |
| 486343 union | 2.64 s | 2.21 s | 2.00 s | 1.59 s |
| plate_24 difference | 4.72 s | 3.87 s | 3.05 s | 2.19 s |
| **sum of the 24 operations** | 215.6 s | 200.7 s (−6.9 %) | 62.9 s | 51.8 s (−17.7 %) |

- 24 of 24 hashes are the same in both rounds, and the other 24 operations of the study in one round (48 of 48).
- The rounding loses 11.1 s, which is the linear list of the trace (10.0 s on the Thingi10K operations, 1.7 s on the
  procedural ones, inflated by the timers). The booleans lose 14.9 s; the 3.8 s beyond the rounding are run-to-run
  variation outside it (67709 union alone: 2.8 s less with 1.6 s less rounding). The safe figure is the rounding's:
  5.1 % of these 24 booleans, 3.3 % of all 48.
- No operation's rounding gets slower by more than 0.01 s.

**Not built: the changed tree by levels.** The logarithmic method (J. Bentley, J. Saxe, *Decomposable searching
problems I: static-to-dynamic transformation*, J. Algorithms 1, 1980 [M]) keeps trees of doubling sizes and rebuilds
only the small ones often. With one level between the list and the changed tree (4096 triangles), the 264 builds of
7365 triangles on 119829 union become 264 builds of about 2000 and 16 of 7365: about a third of the 0.78 s, for one
more tree query per `near`. Bound (all of the builds): 0.9 % of the 33 operations, 19 % of 119829 union and 6 % of
498458 union; the estimate of two thirds of it: 13 % and 4 %.

## 7. Part (d): tree and setup over all triangles

| | Thingi10K rotated, 33 | procedural, 15 |
|---|---:|---:|
| anchors and input boundary | 0.6 s | 0.2 s |
| component scales | 0.7 s | 0.3 s |
| finite and range scans | 0.7 s | 0.3 s |
| position map | 0.5 s | 0.3 s |
| exact flags | 0.1 s | 0.1 s |
| first tree | 1.5 s | 1.0 s |
| sum, of the booleans | 4.0 of 229 s (1.7 %) | 2.2 of 110 s (2.0 %) |

(The first traced run, default runtime settings; `cost_phases_default`.) The parts are of one size, none stands out,
and the sum is below 2 % of both sets; it is 11-15 % only where nearly every triangle is exact (1063860). This is
lever 1a of [EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md](EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md) (sections 4.2 and 10), which
has the prototype and the argument per scan and measured −11 % on its seam stratum. Nothing to add for these sets, and
no prototype here.

## 8. How others solve it

- **Thin and large primitives in box trees:** ray tracing splits their boxes before or during the build (early split
  clipping, spatial splits); measured here, it loses for a one-shot self-join, where the tree is built once and
  queried once per triangle.
- **Tilted geometry in collision detection:** oriented boxes or k-DOPs at the nodes (OBBTree, k-DOP trees). The
  pair-level form, separating axes of the two triangles, is what section 5.3 proved and what pays here.
- **Fast against good trees:** a Morton build with a better build at the top or bottom (the hybrid of Lauterbach et
  al.) is the standard answer to a Morton tree that is slow on some inputs.
- **A changing set under a static index:** the logarithmic method, or an index with insertion; the rounding uses a
  two-level form of it (tree, changed tree, list).
- Not found: a published treatment of the candidate search of a rounding step.

## 9. Options for the owner

Gains are of the whole boolean at 4 threads, per operation the faster round. "Identical" means the result hashes of the
48 operations.

| # | Lever | Gain (measured) | Risk | Output | Size |
|---|---|---|---|---|---|
| 1 | **Cached separating axes in the first search** (5.4): the lemma of SNAP_ROUNDING_3D.md 5.3, the axes once per triangle, in the full searches only (not in the evaluations) | −3.5 % of the 48 operations; −8.9 % procedural thin (gear −14 to −25 %), −5.0 % clean Thingi10K (519511 union −12 %), 0 on dirty results and on the control | a floating-point margin in the rounding (8u, checked with Z3; the guard against subnormal products is still to build): bounds review and a mutation run of the changed lines; 160 bytes per triangle for the time of the search | identical, 48 of 48 in three rounds | S-M: about 80 lines in `MeshRoundingRepair.cs`, its tests (invariance, reuse, determinism, a mutant per margin) |
| 2 | **Boxes of the linear list in `near` kept** (6) | rounding −17.7 % on the 24 operations with many evaluations, their booleans −5 % by the rounding alone (−6.9 % measured); 119829 union −15 %, 67709 union −9 % by the rounding alone; about −3.3 % of the 48 operations; 0 where few edits are evaluated | the boxes must follow every change of the mesh; the library's scan check covers it (`CheckOnThisThread`), and it must run in a test | identical, 48 of 48 (24 in two rounds) | S: about 30 lines, no new bound |
| 3 | **Tree quality on 102375** (5.5): first the diagnosis, in `benchmarks/` | bound by a probe: −5 to −26 % on that model's three operations (rounding −12 to −41 %); no other model of the 16 shows the pattern | the cause is not known; a fix changes `TriangleBoxTree`, which every exact search uses (exact file, exact review), and must not slow the build | identical in the probe (48 of 48, one round); every caller must be independent of the order of the hits | S for the diagnosis; M-L for a build |
| 4 | The changed tree by levels (6) | not built; bound 0.9 % of the 33 operations, 19 % of 119829 union (estimate: two thirds of it) | a third structure in `near` | identical by construction (the same set) | M |
| 5 | Split-clipped sub-boxes; 18-DOP; grid; oriented boxes (5.2-5.4) | sub-boxes +3.5 % as prototyped (a loss, one round); DOP below the axes; the others not built, bounded by these | | identical | **not recommended** |
| 6 | Tree and setup over all triangles (7) | bound 1.5-2 % of both sets | | | **lever 1a**, nothing new |
| 0 | Build nothing | | | | the speed stop as it stands |

Leaning: under the speed stop, nothing here is urgent: (b) to (d) together are bounded by 16 % of the summed booleans
on the 33 Thingi10K operations (23 % on all 48), and options 1 and 2 together reach about 7 % of the 48 operations (3.5 and 3.3 %; they act on
different results, so the gains add). If levers are built: option 2 first (the smallest change, no new bound, the
library's own check covers it), then option 1 (the only candidate of (b) that pays; measured inside the rounding, its
rule already proved). Option 3 should start as a diagnosis, because a slow tree on some inputs would also cost outside
the rounding.

Open, not measured: the same A/B at 1 and 12 threads; the axes in front of the pair tests of the incremental searches;
the DOP in front of the axes; the boolean's own tree searches on 102375.

Built after the owner's go of 2026-11-22: option 2 (#1406, measured on the build: −3.3 % of the 48 operations, rounding
−10.4 %) and option 1 (`TriangleSeparatingAxes`, −3.2 %, rounding −12.5 %); the tables are in the BooleanComparison
README. Options 3 and 4 are open.

## 10. Reproduce

```sh
dotnet build benchmarks/BooleanComparison -c Release && dotnet build benchmarks/SnapRoundingExperiment -c Release
BC="dotnet benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll"
SR="dotnet benchmarks/SnapRoundingExperiment/bin/Release/net10.0/SnapRoundingExperiment.dll"
# cases: procedural, and the Thingi10K raw meshes by id (not committed), each with the rotated copy
for s in gear:500 cyl:8000 fins:400 plate:24 sphere:60; do $SR gen $s cases/proc/$(echo $s | tr : _); done
for id in 67709 42327 1368057 102375 498458 486343 519511 1517923 1063860 668056 119829; do $BC export $id.stl cases/rot/$id; done
$BC profile --repeat 1 --safe-rounding cases/rot/*                                   # the baseline (3.1)

# the first search outside the library (3.3, 5.3, 5.5)
$SR cull cases/proc/gear_500 union --variants box,enum,own-enum,box+axesf,box+dop,split,split-enum,split+axesf
$SR cull cases/rot/102375 union --serial --visits --variants enum,own-enum

# the trace and the A/B (a throw-away worktree; nothing of it is committed to the library)
git worktree add --detach /tmp/wt 57623890
python3 benchmarks/SnapRoundingExperiment/trace/instrument_phases.py /tmp/wt/mesh_ops/MeshRoundingRepair.cs /tmp/wt/mesh_ops/ExactMeshArrangement.cs
dotnet build /tmp/wt/benchmarks/BooleanComparison -c Release -o /tmp/tool
export DOTNET_TieredPGO=0
G3_ROUNDING_PHASES=1 dotnet /tmp/tool/BooleanComparison.dll profile --repeat 1 --safe-rounding cases/rot/* > p.jsonl 2> p.err
cd benchmarks/SnapRoundingExperiment/trace
python3 summarize_phases.py phases p.jsonl p.err; python3 summarize_phases.py bound p.jsonl p.err; python3 summarize_phases.py near p.jsonl p.err
./run_ab_phases.sh /tmp/tool ab 3 cases/proc/* cases/rot/*
python3 summarize_phases.py absum ab 3 axes; python3 summarize_phases.py ab ab/base ab/axes 3
```
