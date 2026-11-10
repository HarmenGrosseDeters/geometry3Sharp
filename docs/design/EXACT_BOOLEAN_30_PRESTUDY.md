# Exact Boolean 3.0: pre-study (measurements, upper bounds, targets)

Status 2026-11-10: pre-study done (measurements, benchmark-level probes, this note); **no library code**. Started by the
coordinator on the owner's request; the idea is in [roadmap/exact-boolean.md](../roadmap/exact-boolean.md) ("Exact
Boolean 3.0 (idea)"). Proposed targets for the owner: [section 8](#8-proposed-30-targets-for-the-owner).

Everything below was measured on the 4-core cloud machine (16 GB) with nothing else running, on master c70e08b unless
stated otherwise. Code and raw rows: [benchmarks/Boolean30Prestudy](../../benchmarks/Boolean30Prestudy/README.md); the
probes are modes of `BooleanComparison` (`Prestudy.cs`, `PrestudyFwn.cs`, public and internal API, no library change).
Thingi10K models are fetched at run time (`benchmarks/LevyClassification/fetch_thingi.py`), never committed.

Library building blocks checked first (AGENT_WORKFLOW section 3): `gParallel.ForRange` (all parallel loops of the
boolean), the phase clocks and per-phase allocation counters of `ExactMeshBoolean` / `ExactMeshArrangement`
(`PhaseMilliseconds`, `PhaseAllocatedBytes`), `ComputeExactVertices`, the n-ary constructor with a winding predicate,
`DMeshAABBTree3.FastWindingNumber` and its protected tree arrays, `ExactCodeAllowList`; the existing data of
[LEVY_CLASSIFICATION.md](LEVY_CLASSIFICATION.md) and the open-operand A/B of branch `claude/open-operand-per-piece`
(Boolean session).

## 1. Parallel scaling

**Setup.** 73 models: the 42-model 2.0 benchmark set (the final speed A/B of 2026-11-08, with the slow tail 519511,
1517923, 119829 / 119830) plus the 31-model stratified random sample of the Lévy study (12 open, 15 self-intersecting,
4 unknown), #385 pairing (B = A rotated 20° and shifted), union / intersection / difference, SafeRounding on, one timed
run per op after the JIT warm-up, at 1, 2 and 4 cores (`taskset` and `DOTNET_PROCESSOR_COUNT`). Command:
`scaling_job.sh`, then `analyze_scaling.py` ([results/scaling_summary.md](../../benchmarks/Boolean30Prestudy/results/scaling_summary.md)).

**Determinism.** The result hashes are equal at 1, 2 and 4 cores on all 219 ops.

| phase (exclusive) | 1 core s | 2 cores s | 4 cores s | speedup 1 -> 4 | Amdahl parallel fraction | share at 4 cores | bound: phase scales perfectly on 4 cores |
|---|---|---|---|---|---|---|---|
| input (closedness, orientation of open inputs) | 48.6 | 37.2 | 36.9 | 1.31 | 0.32 | 7.0 % | 4.7 % |
| setup (grid points, triangles) | 21.0 | 17.2 | 16.2 | 1.30 | 0.31 | 3.1 % | 2.1 % |
| intersections | 483.4 | 265.5 | 163.6 | 2.95 | 0.88 | 31.0 % | 8.1 % |
| - of it pairs (box-tree self-join + exact tests) | 405.0 | 217.6 | 123.8 | 3.27 | 0.93 | 23.4 % | 4.3 % |
| merge (coincident points) | 71.6 | 55.5 | 53.4 | 1.34 | 0.34 | 10.1 % | 6.7 % |
| - of it merge.points | 57.2 | 44.3 | 43.6 | 1.31 | 0.32 | 8.3 % | 5.6 % |
| triangulation (CDT per triangle) | 114.6 | 65.1 | 47.9 | 2.39 | 0.78 | 9.1 % | 3.6 % |
| classification | 103.8 | 76.4 | 74.8 | 1.39 | 0.37 | 14.2 % | 9.2 % |
| simplify (flat regions) | 21.6 | 18.8 | 19.8 | 1.09 | 0.11 | 3.8 % | 2.7 % |
| assembly (without simplify) | 32.6 | 25.3 | 25.4 | 1.28 | 0.29 | 4.8 % | 3.3 % |
| rounding (SafeRounding) | 188.7 | 111.3 | 90.2 | 2.09 | 0.70 | 17.1 % | 8.1 % |
| **total** | **1085.9** | **672.4** | **528.2** | **2.06** | **0.68** | 100 % | 48.6 % |

Ops above 5 s: 69 / 29 / 22 at 1 / 2 / 4 cores. Per cohort at 4 cores: 2.0 set 2.37x (classification 8 %, rounding
19 %, intersections 38 %), open 1.88x (classification 20 %, input 13 %, merge 13 %), self-intersecting 1.93x (rounding
22 %, merge 17 %), unknown 2.01x. The open cohort is measured before Lévy phase 2 (#501, parallel fast-winding tree
build) and before the per-piece fix of open operands; with the fix, classification is 81 % of the open cohort's time
(section 5).

**Amdahl.** The serial part, sum of t1 (1 - f) over the phases, is 342 s, 65 % of today's 4-core time: the current
code is at most about 1.5x faster than on 4 cores with any number of cores (projection with the fitted fractions:
435 s on 8, 389 s on 16, 354 s on 64 cores). The fitted fractions are a model (memory bandwidth, the shared point index
and the GC also limit), not a measurement beyond 4 cores.

**Where the serial time is** (code reading, `mesh_ops/ExactMesh*`, matched to the phase clocks):

| phase | serial part | deterministic parallel design | bound at 4 cores | value on many cores |
|---|---|---|---|---|
| merge.points | `Array.Sort` of the order keys of all points (millions), the serial sweep that forms clusters | parallel sample sort / merge sort of (key, index) pairs with ties by index (a total order, so any schedule gives the same permutation); clusters by a parallel prefix over "starts a new cluster" | 5.6 % | high: the sort is the largest serial block of the arrangement |
| classification | the fast-winding tree build of open meshes (#501 makes it partly parallel); `group_edges` (radix sort) and the union-find over pieces; the per-piece sweep that sums the winding numbers | radix sort by digit with per-thread counts and a fixed prefix (deterministic); union-find by the min-label propagation over edges in rounds, or per spatial block then a merge in block order; the final sweep is per patch independent once the patch values are known | 9.2 % (13 % open) | high for open operands |
| input | `IsWindingClosed` and `oriented` (dictionary welding by position, BFS per component) | a sort of (position, vertex) pairs instead of the dictionary (parallel sort as above), components by min-label rounds | 4.7 % (8.8 % open) | medium |
| rounding | `MeshRoundingRepair`: the self-intersection search is parallel, the edits run in a serial loop | the edits are local: a fixed spatial partition (cells larger than the edit's reach), independent cells edited in parallel in two colour classes, then the boundary cells; same edit order within a cell; results equal only if the cell order fixes every tie (a design question: today's order is global) | 8.1 % | medium; it changes the edit order, so bit identity with today's results needs care |
| assembly + simplify | building the result mesh, the flat-region retriangulation (`ExactPlanarSimplify`) | the regions are independent: per region in parallel, results appended in region order | 6.0 % | medium |
| intersections (rest) | box-tree build (trees 1.87x), the planes scan | parallel tree build by subtrees (as #501 does for the fast-winding tree) | 3.8 % | low-medium |

All designs keep the exact-code rules: no floating-point reduction across threads (only integer counts, permutations and
per-item results), fixed partitions, merges in a fixed order; any loop in `mesh_ops/ExactMesh*` needs the review and the
`ExactCodeAllowList` entry. None of them changes a result except the rounding partition.

**How others do it.** EMBER (Trettner et al. 2022) gets about 5.5x on 8 cores from an adaptive kd subdivision with work
stealing over independent local arrangements and no global structure (repo notes on the paper; the PDF host was not
reachable in this session); Cherchi et al. 2022 parallelize the arrangement with TBB (no thread sweep published);
Manifold uses TBB with a serial triangulation. EMBER's numbers say that a local, subdivision-based architecture scales;
our global point index and the global sorts are what a many-core design would have to remove.

**Reading.** No single parallel lever is worth more than about 9 % on 4 cores; together they are 49 % (the ideal 4-core
time t1 / 4 = 271 s against 528 s). On 4 cores the phases' own algorithms (topics 3-5 and the 2.0 levers) matter more;
the parallel work is the lever for many-core machines, where the serial phases above set the floor.

## 2. Memory

**Setup.** 8 large models (#385 pairing, 0.18-3.66 M input triangles A + B): 1005586, 112798, 117959 (huge, open),
1087144, 1230687, 1041432, 519511, 1517923; all three ops, one process per op (`memory_job.sh`, `prestudy-memory`):
first the arrangement alone (retained heap after `Compute`, points by representation), then the full boolean
(allocations per phase, GC, peak working set); then the same ops with a 512 MB gen0 budget and with server GC
([results/memory_summary.md](../../benchmarks/Boolean30Prestudy/results/memory_summary.md)).

| measure | value |
|---|---|
| arrangement retained | 390-580 B per input triangle (1.4 GB at 3.66 M); 1559 B on the heavily intersecting 519511 |
| peak working set of a boolean | 1.0-1.7 KB per input triangle (6.1 GB for 1005586 union): a 16 GB machine ends near 10 M input triangles |
| points | 0.5-0.6 per input triangle, almost all input vertices (1005586: 1.83 M of 2.0 M); all stored as 4-limb `FixedPoint3` (about 160 B) |
| allocated per op | 3-4 KB per input triangle on the large open inputs (12.9 GB for 1005586 union), 25-30 KB on 519511 |
| largest phases (allocations) | classification on huge open inputs (3.5 GB on 1005586: the HashSets of the fast-winding cache build), rounding 1.5-2.8 GB, input orientation 1.6 GB, assembly 1.8 GB |
| GC pauses | 7.5 % of the summed time (2-2.6 s on 26-38 s ops; 11 % on 519511) |
| GC knobs (upper bound of less allocation) | 512 MB gen0 -0.9 %, server GC -3.7 % of the summed time (single runs, noise about ±3 %) |

Estimated layout per point: approximation 24 B, error 8 B, key 32 B, reference 4 B, fixed-width point about 160 B, index
entry about 48 B (about 275 B); per input triangle: the `InputTriangle` object about 88 B plus 72 B of coordinates.

| lever | bound | time |
|---|---|---|
| input points without a `FixedPoint3` (they are doubles: the coordinates are already in `inputCoords`) | about -80 B per input triangle, about 20 % of the retained arrangement | about 0 |
| `InputTriangle` as struct-of-arrays (fields in arrays, the lazily allocated lists in side tables) | about -70 B per triangle, about 17 % | about 0, maybe less GC copying |
| reuse of the largest buffers / own pools | at most the GC share, 4-8 %; the knobs reached -1 to -4 % | below the 10 % bar of the open "Boolean allocations" item |
| O(n) bottom-up fast-winding coefficients (LEVY_CLASSIFICATION 4.1 item 3; owner decision: the FWN values change) | removes the largest single allocation on huge open inputs (3.5 GB of 12.9 GB) | part of the classification time |

**Reading.** Memory decides the largest model that fits (about 10 M input triangles in 16 GB), not the speed. The first
two levers together would cut the arrangement by about a third at no time cost.

**How others do it.** Plane-based kernels (EMBER, Nehring-Wirxel et al. 2021) store integer planes per face and no
vertices; trueform 2026 reports a peak of 0.21-1.3 GB on 100k-1M-triangle pairs (vendor paper). Cherchi et al. and
Lévy do not report memory.

## 3. N-ary booleans in one pass against chained binary ones

**Setup.** `prestudy-csg`: left-deep CSG trees of n randomly rotated primitives (boxes, or boxes / spheres / cylinders),
operations cycling union, union, difference, intersection, n = 4, 8, 16, 32, 64, seeds 1-3; and 6 Thingi triples
(A ∪ B) − C of clean models, normalized, B and C rotated and shifted. Chained: n − 1 `ExactMeshBoolean` with SafeRounding;
one pass: one `ExactMeshBoolean` over all inputs with the tree's predicate on the winding numbers. Reference: the
one-pass exact vertices rounded to nearest; distances by the plane distance near the surface
([results/csg_summary.md](../../benchmarks/Boolean30Prestudy/results/csg_summary.md)).

| class | one pass / chained time | result triangles chained / one pass | clean | Hausdorff of chained to exact (of diag) |
|---|---|---|---|---|
| Thingi triples (4k-270k input triangles) | 0.72-1.08 (one rounding instead of two) | 1.00-1.02 | all | ≤ 1.1e-14 (one pass the same) |
| n = 4 | 0.4-0.7 | 1.1-1.9 | all | ≤ 1.4e-15 |
| n = 8 | 0.8-1.4 | 1.1-2.6 | all | ≤ 3.6e-16 |
| n = 16 | 2.0-3.4 | 1.1-3.0 | all | ≤ 9.8e-15 |
| n = 32 | 3.9-15 | 1.4-5.6 | all | ≤ 2.7e-16 |
| n = 64 | 20-110 (boxes_64_1: 56.6 s against 0.52 s) | 1.2-6.1 | all | ≤ 3.3e-15 |

- **Correctness and rounding.** Every result is clean in both variants, no chained step is unclean, SafeRounding made no
  edit on the procedural trees; the chained drift is a few units in the last place. Rounding error is not a reason for
  one-pass evaluation at these depths.
- **Size.** Chained results carry up to 6x more triangles: after rounding, coplanar faces are not exactly coplanar any
  more, so `SimplifyFlatRegions` cannot merge them in the later steps.
- **Time.** One pass loses from n = 16 on: it builds the full arrangement of all operands (boxes_64_1: 1.13 M pieces,
  478 k patches from 768 input triangles) and casts one exact ray per patch and candidate mesh (classification 52.2 of
  56.6 s), while a chain shrinks the intermediate result at each step.

Upper bounds: Lévy-style propagation (one ray per component) removes at most the classification (boxes_64_1 56.6 ->
about 4.4 s, still 8x the chain: the triangulation of the full arrangement alone is 3.7 s). The lever for n-ary is
pruning: drop the pieces whose cells cannot change the tree's indicator before triangulation and classification (EMBER
idea 3), or a tree-aware evaluation (one pass per small subtree, chained above it).

**How others do it.** Zhou et al. 2016 and Cherchi et al. 2022 evaluate any number of operands on one arrangement
(Cherchi: 500 stones minus a vase, 7.49 s variadic against 7.59 s pairwise: their operands barely overlap each other);
Lévy 2025 chains for OpenSCAD trees; trueform 2026 reports n-ary medians for N = 4 / 16 / 64 (EMBER 61 / 352 / 1429 ms,
Manifold 71 / 354 / 1467 ms; vendor paper). Our dense, mutually overlapping trees are the worst case for a one-pass
arrangement.

## 4. An exact-output option

**Measured** (`prestudy-exact`, 12 models x 3 ops, each op rounded / unrounded / unrounded with exact vertices in one
process; [results/exact_summary.md](../../benchmarks/Boolean30Prestudy/results/exact_summary.md)):
- cost saved by not rounding: the summed time 186.7 -> 133.0 s (-29 %), per op 0-72 % (1041432 union 7.4 -> 3.0 s,
  519511 union 16.7 -> 10.1 s, 119829 union 3.4 -> 1.0 s);
- `ComputeExactVertices` costs nothing measurable (133.2 s): the exact points exist in the arrangement already;
- the unrounded output rounded to nearest is not clean on 23 of the 33 non-empty ops (degenerate triangles; 1087144
  union not closed): exact output is useful only for a consumer that stays exact;
- size: numerators up to 175-300 bits, W up to 170-295 bits, 120-590 bits per vertex in the mean (four `BigInteger`s,
  about 100-200 B against 24 B); 35-99 % of the vertices of a union are input doubles.

**What consumers need.** A chained boolean or a tet mesher (Diazzi-style recovery, [TET_MESHING.md](TET_MESHING.md))
cannot take these rationals as plain input: the planes of triangles with 230-bit rational corners and their
three-plane intersections grow to thousands of bits per step. The way out is the plane-based view (prior art: Bernstein
and Fussell 2009, Campen and Kobbelt 2010, Nehring-Wirxel et al. 2021 for iterated CSG, EMBER 2022): every output
triangle lies in the plane of an input triangle and every output vertex is an input vertex, an LPI (segment of input
points, plane of input points) or a TPI (three input planes). An exact result that keeps this provenance, constructions
over the *original* double inputs, can be intersected again with the same bounded degrees, and its coplanar faces stay
exactly coplanar (so the chained size growth of section 3 goes away). `ExactMeshArrangement` has these point kinds
already (`PointKind`), but the input path builds its points and planes from doubles.

**API sketch** (additive only; 1.0.324 stays compatible):

```csharp
public sealed class ExactMesh                         // new
{
    public IReadOnlyList<DMesh3> Sources;             // the original double meshes the constructions refer to
    public int TriangleCount, VertexCount;
    public Index3i GetTriangle(int t);
    public ExactPoint3 GetVertex(int v);              // rational value (computed on demand)
    public int SourcePlane(int t);                    // (source mesh, triangle) the triangle lies in
    public DMesh3 Round(MeshRoundingRepair options);  // today's SafeRounding result
    public static ExactMesh FromDMesh(DMesh3 m);
}
// ExactMeshBoolean (new members only)
public bool ExactOutput;                              // skip rounding, fill ExactResult
public ExactMesh ExactResult;
public ExactMeshBoolean(ExactMesh a, ExactMesh b, Operations op);
public ExactMeshBoolean(IEnumerable<ExactMesh> meshes, Func<int[], bool> windingPredicate);
```

Cost: a chain of k booleans saves k − 1 roundings (29 % of a step here) and keeps simplification exact. Effort: tier L
(the arrangement's input path from constructions, predicates on nested constructions with the same degrees,
`ExactPlanarSimplify` and the classification on provenance planes); the tet mesher would need predicates on implicit
points (it has `Orient3dLPI` / `TPI`).

## 5. Fast open-operand classification: batched / dual-tree fast winding (variant B)

**Baseline.** Since the open-operand fix (branch `claude/open-operand-per-piece`, bfaa0158, on Lévy phase 2 #501) an
open operand is classified per representative piece: one `DMeshAABBTree3.FastWindingNumber` query per piece and open
mesh. On the 21 open models of the Lévy samples it costs 4.34x in total, and classification becomes 81 % of the open
cohort's time (the Boolean session's A/B, `benchmarks/LevyClassification/results/open_per_piece/`). Variant B (its
roadmap item): evaluate the far field once per patch or cluster of pieces, the near field per piece.

**Setup.** `prestudy-fwn` (`PrestudyFwn.cs`), built against the library of bfaa0158 (`fwn_job.sh`), 8 open models of the
cohort: the queries are the double centroids of all arrangement pieces (as the per-piece path), against both open
meshes. Per mesh: the library's queries (serial, on a fixed subsample of at most 1e5 queries, scaled); a copy of the
same traversal over the same tree with counters (leaf triangle terms = near, node expansions = far); a dual-tree count
(target tree over the queries, a pair (target t, source s) far when |c_t − c_s| > β (R_s + r_t), else the smaller
side is split; near target leaves fall back to the single-query traversal); unit costs of a triangle term (about 80 ns)
and an order-2 expansion (about 70 ns) from a micro benchmark
([results/fwn_summary.md](../../benchmarks/Boolean30Prestudy/results/fwn_summary.md)).

| model | queries | library queries, serial s (both meshes) | near terms / query | far terms / query | near share of the work |
|---|---|---|---|---|---|
| 100030 | 5.5 k | 0.3 | 279-318 | 40-41 | 0.86-0.87 |
| 112536 | 117 k | 6.1 | 506-592 | 112-117 | 0.85-0.87 |
| 112535 | 103 k | 4.1 | 338-347 | 101-114 | 0.80-0.81 |
| 107554 | 1.01 M | 67 | 477-548 | 149-179 | 0.77-0.78 |
| 122796 | 639 k | 18 | 101-105 | 168-185 | 0.38-0.41 |
| 1087141 | 1.57 M | 244 | 1318-1365 | 322-387 | 0.79-0.83 |
| 1087144 | 1.58 M | 254 | 1325-1368 | 327-379 | 0.79-0.82 |
| 113906 | 756 k | 368 | 2604-3045 | 826-926 | 0.77-0.78 |

**Upper bound.** A batched or dual-tree scheme can share the far field and the traversal, not the near field (each
query point has its own exact triangle terms). With free multipole-to-local translations it saves at most the far share:
22 % of the per-piece query work weighted by time (13-23 % per model, about 60 % on 122796, the only far-dominated
one). The query work is about all of the per-piece classification (113906: 368 s serial, about 93 s per op on 4
cores, against a classification of 94 s per op in the A/B), so B gains at most about 18 % of the open cohort's time.
A real dual tree with the pair criterion does worse than the bound: its near work is 1.1-1.6x that of the single
queries, because a source node that is far for each point of a cluster but not for the cluster as a whole is opened.

**A cheaper accuracy first (probed and rejected).** β = 1 first, accepted where w is farther than a margin from every
value at which the thresholded front winding number can flip (0, 1/2, 1: the own-mesh correction moves w by 1/2),
else β = 2 again: β = 1 cuts the near terms 2-4x, but on all models but the smallest 52-97 % of the queries lie within 0.1 of a flip
value (the pieces are on or next to the open surfaces), so the cost is 0.59-1.52x today's, and 14-694 accepted queries
per mesh flip against β = 2 at margin 0.1 (0 only on one mesh of the smallest model; more at 0.05). Not a lever.

**Reading.** The per-piece path is expensive because each query sits on or next to a surface and needs hundreds to
thousands of exact triangle terms; batching does not touch that. The levers that can reach the 1087xxx / 113906 tail
change what is evaluated: (C) the exact Antipodal Method (Martens et al. 2026: an exact ray plus a sum over the boundary
edges, no near field; [LEVY_CLASSIFICATION.md](LEVY_CLASSIFICATION.md) 4.2, owner decision and patent check; its
cost is O(boundary edges) per query, to be counted on the soups before anything else), or fewer queries (one per input
triangle where all its pieces provably lie on one side of every open boundary). **Recommendation: do not build B.**

**How others do it.** Barill et al. 2018 (the fast winding numbers we use) evaluate single queries; dual-tree / FMM
evaluation of winding numbers or solid angles appears for dense query grids (e.g. GWN contouring), where the queries are
far from the surface; no exact method handles open operands (Zhou 2016, Cherchi 2022, Lévy 2025, EMBER all require
closed / PWN input; LEVY_CLASSIFICATION_LITERATURE.md).

## 6. Positioning

**Same machine** (4 cores, `compare.py -j 1`, ours warm with SafeRounding, Manifold from pip, CGAL 6.0 EPECK as the
external `cgal_bool`; the fixed 100-pair CGAL sample of `thingi-pairs` (#418, EMBER protocol, 1k-100k triangles per
operand) and the first 10 pairs of `thingi-pairs-size` (100k-1M);
[results/positioning_summary.md](../../benchmarks/Boolean30Prestudy/results/positioning_summary.md)):

| class | method | clean | empty (consistent) | unclean | median ms | geomean ms | p95 ms | max ms |
|---|---|---|---|---|---|---|---|---|
| thingi-pairs (300 ops) | ours | 276 | 24 | 0 | 221 | 196 | 669 | 1095 |
| | CGAL EPECK | 225 | 24 | 51 degenerate triangles | 81 | 63 | 364 | 529 |
| | Manifold | 196 | 24 | 80 degenerate triangles | 39 | 34 | 107 | 155 |
| thingi-pairs-size (30 ops) | ours | 29 | 1 | 0 | 2349 | 2412 | 4929 | 5589 |
| | CGAL EPECK | 10 | 1 | 19 degenerate triangles | 1372 | 1488 | 3567 | 3628 |
| | Manifold | 6 | 1 | 23 degenerate triangles | 404 | 418 | 930 | 1065 |

Ours is the only method whose results are all clean (the oracle counts triangles below 1e-12 diag² as degenerate; the
others' volumes are exact to 1e-13). Speed: 3.1x CGAL EPECK and 5.8x Manifold (geomean) on the 1k-100k class, 1.6x
CGAL and 5.8x Manifold on the size class. Our phase mix there: intersections 34-37 %, rounding 20-21 %, classification
8-13 %, setup 8-10 %, assembly 7-10 %.

**Published numbers** (papers and project pages only; collected 2026-11-10, sources in section 7; different hardware,
statistics and end points, so only orders of magnitude compare):

| method | data | hardware | number | output |
|---|---|---|---|---|
| EMBER (Trettner et al. 2022) | 1000 Thingi10K pairs, 1k-100k faces (the protocol of our `thingi-pairs`) | 8 cores | geomean 3.6 ms (CGAL 5.4 711 ms, Mesh Arrangements 3164 ms, QuickCSG 17.9 ms); about 5.5x on 8 cores | plane-based polygon soup with T-junctions, input quantized to 26 bits; no float mesh (repo notes on the paper: the PDF host was not reachable in this session) |
| trueform 2026 (vendor paper) | 1000 Thingi10K pairs, 100k-1M per operand | M4 Max, all threads | medians: trueform 15.7 ms, Manifold 118, EMBER 143.8, Cherchi 416.9, CGAL EPICK corefinement 493.6, Geogram 1644 ms | quantized input, rounded output, topology repaired by vote |
| Cherchi et al. 2022 | 3814 Thingi10K pairs (1k-? faces, no rotation) | 12 cores | 4.5 min in all (about 71 ms per boolean, derived), libigl 28.3 min; 21.6 M triangles in 19.5 s | exact arrangement written as floats, no snap rounding |
| Lévy 2025 | Thingi10K repair, ThingiCSG | not stated | dragon-bunny 2.7 s (Zhou 3.9, Cherchi 1.3); plane-based integer methods 50-100x faster than his | exact, not rounded |
| Manifold | OpenSCAD benchmark (M2 Max) | 1.3-6 cores used | 5-30x faster than CGAL corefinement + Nef | floating point, manifold topology guaranteed, geometry not exact |

Mapped through Manifold, which both runs share (our machine 404 ms median on the size class against 118 ms on the M4
Max: a rough factor 3.4, different pair sets), our size-class median of 2349 ms is about 690 ms in trueform's units:
about 4.8x EMBER, 1.7x Cherchi, 1.4x CGAL EPICK, 5.8x Manifold, 44x trueform. None of the published methods produces
doubles certified free of self-intersections; that guarantee (our SafeRounding, 20 % of our time here) and the exact
handling of self-intersecting and open inputs are what we pay for. The architecture assessment of 2.0 (exact-boolean.md,
owner decisions 2026-11-07) stands: the gap to EMBER / trueform is their quantized plane-based kernel and the local
subdivision (an opt-in second kernel would be an owner decision, after a patent check for EMBER), not a missing tweak.

## 7. Sources

Papers and project pages only (no GPL code read; EMBER paper only). Bernstein, Fussell, *Fast, exact, linear booleans*,
SGP 2009. Campen, Kobbelt, *Exact and robust (self-)intersections for polygonal meshes*, Eurographics 2010.
Nehring-Wirxel, Trettner, Kobbelt, *Fast exact booleans for iterated CSG using octree-embedded BSPs*, CAD 135, 2021
(arXiv 2103.02486). Trettner, Nehring-Wirxel, Kobbelt, *EMBER: Exact Mesh Booleans via Efficient & Robust Local
Arrangements*, TOG 41(4), 2022. Cherchi, Pellacini, Attene, Livesu, *Interactive and Robust Mesh Booleans*, TOG 41(6),
2022 (arXiv 2205.14151). Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG*, TOG 2025 (arXiv
2405.12949). Zhou, Grinspun, Zorin, Jacobson, *Mesh Arrangements for Solid Geometry*, TOG 35(4), 2016. Barill,
Dickson, Schmidt, Levin, Jacobson, *Fast Winding Numbers for Soups and Clouds*, TOG 37(4), 2018. Martens, Trettner,
Bessmeltsev, *The Antipodal Method*, TOG 45(4), 2026. Sajovic, Knez, trueform (arXiv 2607.15905, 2026, vendor paper).
Manifold README and wiki (Apache-2.0 project; documentation read). M. Kintel, openscad-benchmark (README). Nothing
is ported; docs/SOURCES.md gets entries when something is built.

## 8. Proposed 3.0 targets for the owner

The bounds rank the levers; a target is a floor, not a stop (AGENT_WORKFLOW section 8). All results stay bit-identical
unless a target says otherwise, and every parallel change is deterministic by construction (hashes equal at 1, 2, 4 and
8 cores; integer reductions only; the exact-code review and `ExactCodeAllowList` entries).

| # | target (measure, data) | levers in order of bound / effort | bound | tier, owner decision |
|---|---|---|---|---|
| T1 | **Scaling:** on the 73-model set of section 1, the serial floor sum t1 (1 − f) halves (342 -> ≤ 170 s) and the 4-core time drops by ≥ 20 % (528 -> ≤ 420 s); speedup 1 -> 4 cores ≥ 2.5x (today 2.06x); measured again on an 8- or 16-core machine before more is promised | parallel deterministic sort in `merge_points` (5.6 %); classification setup: radix sort, union-find, the open-mesh tree build after #501 (9.2 %); input orientation by a sort instead of the dictionary (4.7 %); `ExactPlanarSimplify` and assembly per region (6 %); box-tree build (part of 3.8 %) | 27 % at 4 cores, the floor 342 -> about 155 s | L (exact files); bit-identical |
| T1b | the SafeRounding edits in parallel over a fixed spatial partition | rounding 8.1 % at 4 cores, more on many cores | 8 % | L; **owner:** the edit order changes, so results change (within the same guarantees) |
| T2 | **Memory:** the arrangement retains ≤ 300 B per input triangle (today 390-580) and the peak working set ≤ 1.2 KB per input triangle (today 1.0-1.7 KB), no time regression; the largest model on a 16 GB machine from about 10 M to about 13 M input triangles | input points without a `FixedPoint3`; `InputTriangle` as struct-of-arrays; then the bottom-up fast-winding coefficients | -35 % retained | L (exact files); the last lever is the open owner decision of LEVY_CLASSIFICATION 4.1 item 3 |
| T3 | **n-ary:** keep chained evaluation as the recommendation; build nothing for one-pass now | (if wanted later) pruning of pieces that cannot change the tree's indicator + propagation; target then: one pass ≤ chained time for n ≤ 64 on the procedural trees of section 3 | today one pass is 20-110x slower at n = 64 | owner: is n-ary CSG a use case? |
| T4 | **Exact output:** an additive `ExactMesh` result and input (section 4 sketch): a chain of k booleans with k − 1 roundings fewer, results equal to the one-pass exact result, coplanar faces merged exactly, no growth of the construction degree along the chain | constructions over the original inputs (plane-based provenance) through the arrangement's input path | -29 % per chained step (rounding), chained result size -10 to -80 % | L, 4-8 weeks; **owner:** a use case (chained CSG, tet meshing) before it is built |
| T5 | **Open operands:** no batched / dual-tree winding (B: ≤ 22 % of the query work); first count the cost of the exact Antipodal Method (boundary edges per query on the 1087xxx / 113906 soups) and the share of queries that one query per input triangle could replace | then C or the query reduction, judged against the per-piece path | per-piece classification is 81 % of the open cohort | **owner:** C needs the patent check (LEVY_CLASSIFICATION 4.2) |
| T6 | **Positioning:** stay the only method with clean results on `thingi-pairs`; close the gap to CGAL EPECK on the 1k-100k class from 3.1x to ≤ 2x (geomean) with T1 and the 2.0 rounding levers; no target against EMBER / trueform without a second kernel | T1, rounding search levers (hard open problem 3) | | owner: a quantized plane-based opt-in kernel stays out of scope unless decided |

Order proposed: T1 (bit-identical, largest bound at no change of results), T2 (independent, memory only), T5's
counting step (an hour, decides C), then T1b and T4 as owner decisions; T3 only with a use case.

Prior art for the options (AGENT_WORKFLOW section 2): parallel scaling without a global structure: EMBER (local kd
subdivision, work stealing); memory: plane-based kernels store planes, not points (EMBER, Nehring-Wirxel 2021);
n-ary: Zhou 2016 and Cherchi 2022 one arrangement, Lévy 2025 chained, trueform pairwise accumulation; exact output:
plane-based provenance (Bernstein-Fussell 2009, Campen-Kobbelt 2010, Nehring-Wirxel 2021); open operands: no exact
method in the literature but the Antipodal Method (2026).
