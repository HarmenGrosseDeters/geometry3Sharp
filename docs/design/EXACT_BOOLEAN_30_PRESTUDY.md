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

[filled from the run below]

## 6. Positioning

[filled below]

## 7. Sources

[filled below]

## 8. Proposed 3.0 targets for the owner

[filled below]
