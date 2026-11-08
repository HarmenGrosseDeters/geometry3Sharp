# Classification in the exact boolean: Lévy-style propagation and cheaper levers (phase 1)

Status 2026-11-08: phase 1 done (data, profile, upper bound, literature); no library code. Coordinator assessment
2026-11-08: recommendation agreed; phase 2 waits for the merge queue (#397 ... #426). Supersedes the hand-over note
[LEVY_CLASSIFICATION_HANDOVER.md](LEVY_CLASSIFICATION_HANDOVER.md). Literature: [LEVY_CLASSIFICATION_LITERATURE.md](LEVY_CLASSIFICATION_LITERATURE.md)
(Lévy 2025 / Geogram, Zhou 2016, Cherchi 2022, EMBER, Manifold, GWN) and
[LEVY_CLASSIFICATION_WINDING2026.md](LEVY_CLASSIFICATION_WINDING2026.md) (Antipodal Method, Xie et al. 2026).
Data and tools: [benchmarks/LevyClassification](../../benchmarks/LevyClassification/).

## 1. Question

Would a classification by propagation (Lévy 2024/25: radial sort around each intersection edge, winding numbers
propagated across sheets, one exact ray per connected component) bring the self-intersecting, unknown and open
cohorts of the Exact Boolean 2.0 milestone closer to the 5 s target? Today `ExactMeshBoolean.classify` casts one exact
ray per (patch, candidate closed mesh) and evaluates a fast generalized winding number (Barill 2018) per patch for open
meshes (see the hand-over note for the code path).

## 2. Measurements

All on the 4-core cloud machine, #385 pairing (B = A rotated and shifted), union / intersection / difference,
SafeRounding on, one run per operation. Builds: `master` (library of efa2309) and `levers` (53665116 = #409 + #412 +
#406 + #419, the queued speed levers). Probe: `benchmarks/LevyClassification` (public API only).

| sample | models x ops | build | Compute s | classification share | ops > 5 s | still > 5 s if classification = 0 |
|---|---|---|---|---|---|---|
| slow tail (slowest by classification in the #385 interim rows, plus 8 random) | 26 x 3 | master | 1035 | 19.7 % | 52 | 49 |
| | | levers | 841 | 24.3 % | 53 | 49 |
| seeded random sample of the three cohorts | 31 x 3 | master | 572 | 9.7 % | 19 | 17 |
| | | levers | 334 | 16.7 % | 17 | 14 |

Per cohort (random sample, levers): open 36 ops, 18.8 %, 14 > 5 s; self-intersecting 45 ops, 9.1 %, **0** > 5 s;
unknown 12 ops, 12.7 %, 3 > 5 s. 12 of the 17 slow random ops are the Thingi10K family 1087xxx/1088xxx (open,
about 0.7 M triangles per input, about 300 patches).

Phase mix of the slow-tail ops above 5 s (levers): classification 24 %, rounding 22 %, intersections 15 %,
triangulation 11 %, merge 10 %, input 7 %, assembly 6 %. No phase dominates; an op of 20-50 s needs a factor 4-10
over the whole pipeline.

Commands: `bash benchmarks/LevyClassification/job.sh <work dir> [sample_random.txt]`, then `python3
benchmarks/LevyClassification/analyze.py <work dir>/out[_sample_random]`; rows and summaries in
`benchmarks/LevyClassification/results/` (`summary.md`, `random/summary.md`).

### 2.1 Where the classification time goes (traces)

`trace.sh` + `trace_split.py` (dotnet-trace thread-time samples; waits and spinning on contended locks
(`Monitor.Enter_Slowpath`) dropped; time on all threads), 12 ops of the levers build,
`results/trace_split_levers.jsonl`. Shares of the classification time:

| kind | examples | exact rays | per-patch setup | `AddPoint` of centroids (across parts) | fast winding build / eval | rest |
|---|---|---|---|---|---|---|
| closed, many patches (6e4-2.3e5) | 1087134, 107543, 105693, 111170, 113685 | 68-86 % | 7-15 % | 2-4 % | - | 4-21 % |
| open, many patches (3e4-1.8e5) | 113906, 107554 | - | 5-17 % | 1-3 % | 1-3 % / 66-92 % | 2-13 % |
| open, huge, few patches (1e2-2e3) | 112798, 1005586 | - | 1-3 % | 0-2 % | **70-82 %** / 1-5 % | 17-21 % |
| huge, almost no patches | 120628, 103354 | 2-16 % | 4-5 % | 1 % | - | 80-93 % (per piece) |

Correction (2026-11-08): a first version of `trace_split.py` counted spinning on contended locks as work. It gave
`AddPoint` 29-57 % of the classification of patch-heavy ops; 93 % of those samples were `Monitor.Enter_Slowpath`.
The A/B of the local-centroid prototype (4.1, item 1) measured the real gain, which is much smaller. Shares from
thread-time samples are estimates; only A/B timings count.

Two findings outside the Lévy question:
- `ExactMeshArrangement.AddPoint` of the exact centroid (`ExactMeshBoolean.centroid`, called per patch in
  `patch_winding` and per piece with a coplanar stack in `coplanar_stack`) takes a lock of the sharded point index,
  which is contended on patch-heavy ops. Nothing downstream needs the point in the arrangement: `patch_winding` reads
  its exact value back at once, `coplanar_stack` passes its id to `arr.Orient`.
- The fast-winding cache of an open mesh (`DMeshAABBTree3.build_fast_winding_cache`) is built serially, once per open
  mesh, before the parallel patch loop; it unions HashSets of all triangles below every node (O(n log n)).

## 3. Upper bound of Lévy-style propagation

Propagation replaces the exact rays and the per-patch setup (centroid, candidate query) of closed meshes, about 85-95 %
of the classification on closed many-patch ops (trace estimate), i.e. at most the classification share: about 10-25 % of the
Compute time, on the closed self-intersecting and unknown ops with many patches. It does not apply to open operands:
their generalized winding number is not integer-consistent across patches (the jumps are exact, the value inside a
cell is not constant), which the literature confirms (Zhou 2016 calls robust open handling "elusive"; Lévy, Zhou and
EMBER require closed / PWN inputs; the GWN-propagation heuristic of branch `claude/exact-boolean-gwn-propagation` was
not merged). Open operands are most of the slow tail.

Even with classification at zero, 2-4 ops of each sample drop below 5 s (table above). **Decision: no Lévy-style
propagation now** (coordinator 2026-11-08); the numbers above close the question. If the remaining phases get much
faster later, the share grows and the question can be reopened with these tools.

How others solve it (prior art, see the literature notes): all exact methods with code cast rays per connected
component, not per patch (Lévy / Geogram, Manifold) or none at all (Zhou 2016), and propagate by ±1 or XOR across
sheets using the radial order; only Cherchi 2022 casts one ray per patch, like us. EMBER classifies with short
segments to local reference points of its kd-subdivision, a property of that architecture. None handles open inputs
exactly.

## 4. Recommendation

### 4.1 Phase 2 (one PR after the merge queue clears, on master): ways 1 and 2; the local centroids only optionally

1. **Local exact centroids instead of `AddPoint`: measured, small gain; low priority.** `patch_winding` uses the
   `ExactPoint3` directly; `coplanar_stack` gets an `Orient` variant of `ExactMeshArrangement` that takes a local exact
   point and its double approximation (same filter and error bound as `AddPoint` stores) instead of a point id. A
   centroid that coincides with an existing point gives orientation 0 in the exact test, as the id comparison does
   today.
   - Prototype ([prototype/addpoint_local_centroid.diff](../../benchmarks/LevyClassification/prototype/addpoint_local_centroid.diff),
     not library code), `prototype/addpoint_job.sh`, `results/proto_addpoint.jsonl`: result hashes **equal on all
     171 ops** of both samples (57 models x 3 ops); `Counts["points"]` unchanged (it is taken before the
     classification). Classification time over all 171 ops -2.1 % (254.8 -> 249.5 s), Compute -1.3 %. On the
     patch-heavy ops, two rounds:

     | model | op | patches | classification ms base r1 / r2 | local centroid r1 / r2 |
     |---|---|---|---|---|
     | 1087134 | intersection | 227906 | 10874 / 11428 | 10439 / 10688 |
     | 107543 | intersection | 161025 | 4243 / 4330 | 3362 / 3748 |
     | 107554 | difference | 177704 | 5139 / 5532 | 4445 / 4768 |
     | 113906 | intersection | 34168 | 5900 / 5661 | 4895 / 5139 |
     | 111170 | intersection | 57487 | 2088 / 2785 | 1736 / 2224 |
     | 105693 | intersection | 60850 | 2724 / 2879 | 2509 / 2681 |
     | 113685 | intersection | 58254 | 2210 / 2742 | 2079 / 2268 |

     That is -4 to -20 % of the classification on these ops, below 1 s each; under 1 % of their Compute time.
   - It touches exact code (`mesh_ops/ExactMesh*`: tier L, full exact review, the mutation shard of the touched
     files). For this gain the cost is hard to justify on its own; it can ride along with a later change of the same
     functions (e.g. propagation, if reopened), or be dropped. Acceptance if built: hashes equal on both samples
     (`job.sh` with both sample files, the probe writes `hash`), the boolean suites, invariance over
     `Similarity.All()`, a test pinning `coplanar_stack` with coincident and coplanar overlapping triangles.
2. **Fast-winding tree, ways 1 and 2.** Way 1: build the trees of all open meshes concurrently in `classify` (a member
   already on the `ExactCodeAllowList`; each tree is independent; the dictionary is filled serially afterwards).
   Way 2: in `build_fast_winding_cache` build the two child subtrees concurrently in the top levels (each subtree
   writes only its own boxes; the per-node work and its summation order are the same as serial). No floating-point
   reduction across threads, so the result is deterministic by construction.
   - Prototype ([prototype/fwn_parallel.diff](../../benchmarks/LevyClassification/prototype/fwn_parallel.diff), not
     library code), A/B `prototype/proto_job.sh`, two rounds, means, `results/proto_fwn.jsonl`:

     | model | op | Compute ms base / way 1 / ways 1+2 | classification ms base / way 1 / ways 1+2 | hashes |
     |---|---|---|---|---|
     | 112798 | intersection | 21165 / 18542 / 20644 | 7250 / 5144 / 4580 | equal |
     | 1005586 | intersection | 26325 / 23541 / 21425 | 8466 / 5925 / 4903 | equal |
     | 117959 | intersection | 21696 / 20606 / 19824 | 5952 / 4436 / 3817 | equal |
     | 113906 | intersection | 14648 / 13344 / 14608 | 5790 / 5099 / 5970 | equal |
     | 1087141 | intersection | 19626 / 19176 / 18422 | 3243 / 2550 / 2281 | equal |

     Classification -29 to -42 % on the tree-bound cases (ways 1+2; way 2 adds less than the factor 3 I estimated:
     the top-level unions of large HashSets stay serial), no gain on the evaluation-bound 113906. Compute totals
     vary by ±2 s between rounds (other phases), so the classification column is the measure.
   - Acceptance: a determinism test (serial = parallel bit for bit over repeated runs and thread counts, for
     `DMeshAABBTree3.FastWindingNumber` and the boolean of an open input), result hashes equal on both samples.
3. Optional in the same PR if the data justifies it: the bottom-up combination of the far-field coefficients (Barill
   2018, translate the child moments to the parent centre) would make the cache build O(n) without HashSets, but
   changes the floating-point values of the public `FastWindingNumber` and can flip thresholded open classifications
   in rare cases: only with an owner decision.

### 4.2 Later option (owner decision, after a patent check)

Generalized winding numbers of open operands as an exact signed ray count plus a per-boundary-edge sum (Martens,
Trettner, Bessmeltsev, "The Antipodal Method", TOG 45(4) 2026, CC BY 4.0, code MIT: portable with attribution;
Xie, Hafner, Wojtan, TOG 45(4) 2026, CC BY 4.0, code unlicensed: paper only). No fast-winding precomputation, no
far-field approximation, a symbolically perturbed ray consistent with our exact ray; but O(boundary edges) per query
without a hierarchy, so soups (1e5 boundary edges x 1e5 patches) would keep Barill behind an adaptive switch. It
changes open results (the approximation goes away). The milestone only asks "no degenerate triangles and no crash"
for open inputs, so its value is speed only (it removes the tree build of the 1087xxx family entirely).

## 5. What no known lever reaches

`python3 benchmarks/LevyClassification/exceptions.py results/levers.jsonl results/random/levers.jsonl` lists every op
above 5 s with all levers (70 ops of both samples): input size, time, µs per input triangle and the phase mix
([results/exceptions_levers.md](../../benchmarks/LevyClassification/results/exceptions_levers.md)). For the
coordinator's question whether the target should become size-dependent (like Alpha Wrap's max(5 s, k µs × triangles)):
- Input size alone is a weak predictor: log-log correlation of time with input triangles 0.75, with arrangement pieces
  (`Counts["pieces"]`, output of the intersection step) 0.87.
- With max(5 s, k µs × input triangles), k = 10 leaves 43 of the 70 ops over target, k = 20 still 27; the small,
  heavily self-intersecting models (45k-136k triangles, 5e4-2.1e5 self-intersections, 8-20 s: 113685, 1059579, 105693,
  107543, 107545, 107554, 113906) stay over any plausible k.
- With max(5 s, k µs × pieces), k = 15 leaves 22 ops (11 models), k = 20 leaves 11 ops (7 models).
So a rule on the arrangement size (pieces or intersecting pairs) fits the data better than one on the input size, but
it is a measure of the result's complexity, known only after the intersection step.

## 6. Reuse and sources

- Library building blocks used by the recommendations: `ExactPoint3`, the filtered `Orient` of
  `ExactMeshArrangement`, `gParallel`, `DMeshAABBTree3`, the exact ray of `front_winding`.
- Sources: Lévy 2025 (TOG 44(5)), Geogram (BSD-3, read; nothing ported), Zhou et al. 2016, Cherchi et al. 2022 (paper),
  Trettner et al. 2022 EMBER (paper, ACM licence), Barill et al. 2018, Martens et al. 2026 (CC BY 4.0, MIT code),
  Xie et al. 2026 (CC BY 4.0, paper only). Phase 2 adds to docs/SOURCES.md what it implements.
