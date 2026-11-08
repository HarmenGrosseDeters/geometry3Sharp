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

`trace.sh` + `trace_split.py` (dotnet-trace thread-time samples, waits dropped, CPU time on all threads), 12 ops of
the levers build, `results/trace_split_levers.jsonl`. Shares of the classification CPU time:

| kind | examples | exact rays | per-patch setup | `AddPoint` of centroids (across parts) | fast winding build / eval | rest |
|---|---|---|---|---|---|---|
| closed, many patches (6e4-2.3e5) | 1087134, 107543, 105693, 111170, 113685 | 33-60 % | 27-37 % | 29-57 % | - | 5-21 % |
| open, many patches (3e4-1.8e5) | 113906, 107554 | - | 16-56 % | 13-56 % | 1-2 % / 30-81 % | 1-11 % |
| open, huge, few patches (1e2-2e3) | 112798, 1005586 | - | 1-5 % | 0-3 % | **69-82 %** / 1-5 % | 17-21 % |
| huge, almost no patches | 120628, 103354 | 2-18 % | 5 % | 1-2 % | - | 77-93 % (per piece) |

Two findings outside the Lévy question:
- `ExactMeshArrangement.AddPoint` of the exact centroid (`ExactMeshBoolean.centroid`, called per patch in
  `patch_winding` and per piece with a coplanar stack in `coplanar_stack`) hashes a large rational point under a lock.
  Nothing downstream needs the point in the arrangement: `patch_winding` reads its exact value back at once,
  `coplanar_stack` passes its id to `arr.Orient`.
- The fast-winding cache of an open mesh (`DMeshAABBTree3.build_fast_winding_cache`) is built serially, once per open
  mesh, before the parallel patch loop; it unions HashSets of all triangles below every node (O(n log n)).

## 3. Upper bound of Lévy-style propagation

Propagation replaces the exact rays and the per-patch setup (centroid, candidate query) of closed meshes, about 85-95 %
of the classification on closed many-patch ops, i.e. at most the classification share: about 10-25 % of the
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

### 4.1 Phase 2 (one PR after the merge queue clears, on master)

1. **Local exact centroids instead of `AddPoint`.** `patch_winding` uses the `ExactPoint3` directly;
   `coplanar_stack` gets an `Orient` variant of `ExactMeshArrangement` that takes a local exact point (with its double
   approximation for the filter) instead of a point id. A centroid that coincides with an existing point gives
   orientation 0 in the exact test as the id comparison does today, so the result is unchanged.
   - Expected: bit-identical results; about 30-55 % of the classification time on patch-heavy ops (the `AddPoint`
     share above).
   - `Counts["points"]` gets smaller (it counts the centroids today); a diagnostic counter, stated in the PR.
   - Exact code (`mesh_ops/ExactMesh*`): tier L, full exact review with docs/REVIEW_CHECKLIST.md, the mutation shard of
     the touched files must stay above its threshold.
   - Acceptance: result hashes identical to master on both samples (`job.sh` with both sample files, the probe writes
     `hash`), the boolean test suites, the invariance test over `Similarity.All()` unchanged, a test pinning
     `coplanar_stack` with coincident and coplanar overlapping triangles.
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
