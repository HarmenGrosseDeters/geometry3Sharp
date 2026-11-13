# Arrangement allocations (boolean study lever 4, part one)

A/B of `ExactMeshBoolean` unions for the allocation cuts in `ExactMeshArrangement` / `ExactTriangulation2`. The cases
are those of docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 8: the 36 normal unions (Thingi10K models with their rotated,
shifted copy from `BooleanComparison export`, `cross150`, `sph300`) and its tail (1041432, 519507, 668056, 668057,
1088051). Thingi10K meshes are fetched at run time, never committed.

| file | what |
|---|---|
| `ab.py` | per case, two tools one after the other, each one process `profile --repeat R --median --safe-rounding --ops union`; resumable |
| `abrounds.py` | the rounds pooled: per case the median of all runs, sums, GC pause and allocations (means), hash check |
| `ab_env.py`, `gcrep.py` | the upper bound: one tool under default GC, `DOTNET_GCgen0size=0x40000000` and `DOTNET_gcServer=1` |
| `results/` | raw rows (4-core cloud container, 13 November 2026) |

**Run order matters.** An A/A run (the same build twice, `results/aa_order_bias.jsonl`) gave the second process of each
pair +6 % per-case median on small cases (100-400 ms). So the final A/B runs both orders: rounds 1-2 base first, rounds
3-4 (tail: round 2) candidate first, pooled. (The single-order A/B of `benchmarks/PredicateComparison/underflow` ran the
candidate second, so its gains are, if anything, understated.)

    python3 ab.py r1.jsonl 5 <base tool> <cand tool> <cases>...      # base first
    python3 ab.py r3.jsonl 5 <cand tool> <base tool> <cases>...      # candidate first
    python3 abrounds.py <base tool> <cand tool> r1.jsonl r2.jsonl r3.jsonl r4.jsonl

**Upper bound** (master + #753, one tool, median of 5; GC pause stated separately; `gc_bound_*.jsonl`):

| set | default wall | GC pause | 1 GB gen0 budget | server GC |
|---|---|---|---|---|
| normal 36 | 17 015 ms | 7.9 % | -7.5 % | -5.2 % |
| tail 5 | 80 785 ms | 6.2 % | -3.2 % | -1.6 % |

**Result** (master `51d6eae7` against the change, both orders pooled; wall includes GC; all result hashes equal):

| set | wall before (ms) | after | GC pause before (ms) | after | allocated MB before | after |
|---|---|---|---|---|---|---|
| normal 36 (4 rounds x 5 runs) | 17 163 | 16 988 (-1.0 %) | 1 094 | 1 026 (-6.2 %) | 7 847 | 7 414 (-5.5 %) |
| tail 5 (2 rounds x 5 runs) | 83 939 | 81 569 (-2.8 %) | 4 500 | 3 934 (-12.6 %) | 15 434 | 14 261 (-7.6 %) |

Per case on the normal 36 the median ratio is -0.3 %. 7 cases are more than 5 % slower and 5 more than 5 % faster,
all of them small (under 400 ms), where single runs vary by ±10-20 % on this host. Of the tail, 668057 is +5.4 % in this
run and was -4.7 % and -2.1 % in two earlier ones; the others are 0 to -5 %.

What the change removes, in the allocation profile (dotnet-trace `gc-verbose`, union of 112826):
- the `List<int>` per `TriangleBoxTree.Query(box, Action)` call (rounding's queries): 2.6-6.5 % of the bytes;
- a `double[4]` per box of `BoxGrid2` (segment and point grids of the touched triangles): 1.5 %;
- a `List<int>` per point group in `merge_cluster`: about 2 %;
- the growth of `ExactTriangulation2`'s containers (sized from the number of points: 1 + 2 n triangles): part of its 5 %.

The largest single source, `MeshRoundingRepair.rebuild_tree` (8.6 % of the bytes: a new `TriangleBoxTree` per rounding
round, plus its box list), is not changed here: a tree rebuild that reuses the arrays belongs with its caller, in the snap
rounding work.
