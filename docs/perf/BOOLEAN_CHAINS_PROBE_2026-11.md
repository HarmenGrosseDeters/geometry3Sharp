# Boolean chains: step 3 of the ABC chains and the self-join of certified operands (2026-11-19)

Measurement only (owner task 2026-11-19), data branch `runs/boolean-chains`; no PR to master. The library hooks are the patch
`benchmarks/BooleanComparison/perf/chain_probe.patch` (applied on the runner, never committed to a library file); the driver is
`BooleanComparison chain-probe*` (`ChainProbeRun.cs`), the summary `data/chain_probe.py`, the workflow the probe variant of
`abc-chained.yml` on this branch. Rows (result rows and model ids only, no mesh): `perf/chains_probe_2026-11-19/`.
Runs on GitHub-hosted `ubuntu-latest` (4 cores), 90 ABC chains (30 per variant), `ours` = each step on the previous rounded result
(`SafeRounding`), median of 3 repetitions per configuration, result hashes checked (bit-identical in every experiment).

## 1. Step 3 of the ABC chains (ours 1.1-2.2x slower than Geogram / CGAL)

Run 44458632683 (`off` = no probe active). Median over chains, ms (sum over chains in brackets, s):

| phase | step 1 (n=90) | step 2 (n=88) | step 3 (n=88) | step 3 / step 2 (median of per-chain ratios) |
|---|---|---|---|---|
| pairs | 24 (2.4) | 32 (3.5) | 45 (5.0) | 2.7 |
| planes | 2 (0.2) | 2 (0.9) | 30 (3.2) | 9.6 |
| **intersect** | 1 (0.8) | 4 (12.7) | **335 (41.2)** | 28 |
| merge | 8 (1.2) | 9 (5.3) | 118 (12.1) | 5.7 |
| triangulation | 4 (0.9) | 10 (13.4) | 136 (20.2) | 12 |
| classification | 8 (2.6) | 14 (18.2) | 183 (23.4) | 8 |
| rounding | 36 (3.4) | 36 (14.5) | 42 (47.5) | 3.3 |
| whole boolean | 140 (16.4) | 143 (75.9) | 1185 (158.6) | 5.6 |

**What grows:** intersect (26 % of the summed step-3 time, 28 % of the median chain), then classification, triangulation, merge. Rounding is the
largest share of the *sum* (30 %) only because of a few heavy chains (the top 10 % of chains are 39 % of the step-3 time); the median chain is
intersect-bound. The pair search (`intersections.pairs`) is 2-4 % of step 3: it does not grow.

**Why:** 89 % of the candidate pairs of step 3 really intersect (step 2: 49 %): R holds A's surface again, so every triangle of A meets the
identical or neighbouring triangle of R. Input points are welded by position, so these pairs share corners. Serial CPU of the intersect phase
(probe run `p2s`, per-pair timestamps, 88 chains; 85 % of it is coplanar pairs):

| class of pair (step 3) | pairs | share of the intersect phase |
|---|---|---|
| coplanar, shares 1 corner, **trivial** (the clips are corners / boundary edges only) | 3.93 M | 37 % |
| coplanar, shares 2 corners, trivial | 1.18 M | 11 % |
| coplanar, identical triangle (3 corners), trivial | 0.66 M | 10 % |
| coplanar, shares 0 corners, empty (boxes overlap, nothing meets) | 0.85 M | 10 % |
| coplanar, adds a real segment / point | 0.90 M | 17 % |
| non-coplanar, meeting at a point / along a segment | 3.67 M / 1.22 M | 8 % / 6 % |
| apart (prefilter, construction) | 0.5 M | 1 % |

**Trivial coplanar pairs are 57 % of the intersect phase, about 15 % of the step-3 time** (scaled to the `off` times), and make 4.9 % more CPU
downstream: 1.48 M of the 2.05 M touched triangles of step 3 are touched only by corner points and boundary edges (`merge` / `triangulation`
run the full path on them). The exact fallbacks are not the cost: the winding sides that go exact are the same in step 2 and 3 (17 M).
By the placement of C the coplanar share of the CPU time is 21-33 % (partial overlap 33, face contact 23, mirror 21, ulp-shift 24).

**Upper bound of the top phase** (intersect): 26-28 % of step 3 if free. A bit-identical lever with no new error bound that decides the
trivial coplanar pairs before the clips (exact orientation tests of the arrangement's own `Orient` on input corners, see section 3) can remove
at most 57 % of the phase (15 % of step 3) plus the downstream 5 %: **bound about 20 % of step 3**.

## 2. EMBER lever 2: skip the self-join of operands that are certified results

The self-join (`self_pairs`) is timed per operand in place (wall time in the parallel run). Skipped by a probe switch on the certified operand;
the hash of every result is the same.

| setting | operand | self-join, median ms | share of the boolean (median chain / sum) | measured saving of skipping (sum of times) |
|---|---|---|---|---|
| chain step 1 | A, B raw | 11.5 / 2.4 | 8.5 / 1.7 % | not certifiable (raw) |
| chain step 2 | R1 (library result) | 16.1 | 4.1 % / 1.9 % | -1.6 % (median per chain -3.1 %) |
| chain step 3 | R2 (library result) | 15.7 | 1.4 % / 1.0 % | -0.9 % (median per chain -1.4 %) |
| ExactMesh expression, steps 2 / 3 (run 44470195190, 90 chains) | each of 3 sources | 9.6-12.6 | 2.9-3.5 % each, about 10 % of the median boolean for 3 | all sources skipped: -4.1 % / -2.6 % of the sum (median per chain -13 % / -10 %) |

In an expression chain (`ExactMesh`, stage 1) the intermediate results are never operands: one n-ary arrangement over the leaf meshes, so only a leaf
that is itself a library result (`FromDMesh(x.Round())`) could carry a certificate. Procedural chains (small clean shapes, 1 thread, 18 chains each,
`perf/chains_probe_2026-11-19/procedural/`): self-join 6-10 % per source of the n-ary boolean, 10-12 % of a chain's step 1-2; noisy, an upper
bound for small inputs only.

**Certificate:** can come only from the library: `RoundingClean && NotCleanReason == None` of a result the library computed, bound to the
instance (not to a caller flag). `DMesh3` has `ShapeTimestamp`; a `ConditionalWeakTable<DMesh3, certificate(ShapeTimestamp)>` would be invalidated by any
edit. A copy of the mesh carries none. This is new API-free internal state, but it is also new exact-code surface.

## 3. The lever found: decide trivial coplanar pairs before clipping (step 3)

`probe_fast_trivial` in the patch (probe only): for a coplanar pair that shares corners (by point id) the pair meets only in the shared corners / the shared
edge, so it adds nothing but the coplanar partners, if
- 3 shared corners: always (identical triangle);
- 2 shared corners: the two third corners lie strictly on opposite sides of the shared edge (`Orient(T, a, b, c) * Orient(T, a, b, d) < 0`);
- 1 shared corner: no boundary ray of one triangle lies in the closed wedge of the other at the shared corner (4 x 2 `Orient` tests).
Only the arrangement's existing exact in-plane `Orient` on input points is used: no new predicate, no new error bound. The pair is then recorded
(`AddCoplanar`, `IntersectingPairs`) without the six clips; the triangles that were only "touched" by such pairs stay untouched (their `Points` /
`Segments` would only hold corners / boundary edges).

Checks (probe `fastchk`: the decision is compared with the clips' outcome of every pair): **105 773 pairs on the procedural chains
(the ABC rerun repeats the check): 0 mismatches, 0 trivial pairs missed**, result hash equal to the unmodified run in every case (48 of 48 procedural runs).

| chains | change of the step-3 boolean | 
|---|---|
| procedural (box shapes, flat faces, 16 chains), 4 threads | -26.6 % (serial -26.7 %) |
| procedural, 1 thread (15 chains) | -22.7 % (serial -22.2 %) |
| **ABC, GitHub runner** | see "ABC result of the pre-decision" below |

## 4. Report (time split, bound, lean)

| item | bound | measured | lean |
|---|---|---|---|
| 1. step 3: top phase intersect | 26-28 % of step 3 | trivial coplanar pairs 15 % + downstream 5 % | **go** (bit-identical, no new bound), pending the ABC number of section 3 |
| 2. lever 2, chains | 1.4-4 % (certified operand) | -0.9 % / -1.6 % | no-go |
| 2. lever 2, ExactMesh expressions | 3 % per source, 9-10 % for 3 | -2.6 % / -4.1 % (sum), -10 / -13 % (median chain), only for sources that are library results | no-go (below 10 % of the time, certificate only for re-fed results) |

### Plan for the go (one library PR, after the coordinator's and the owner's go)

Files: `mesh_ops/ExactMeshArrangement.cs` (`intersect_coplanar`: the pre-decision before the clips; nothing else), tests in
`tests/geometry3Sharp.Tests` (a new test class for the decision), `docs/perf/` (the benchmark numbers, `benchmarks/BooleanComparison/README.md`).
Exactness argument: the decision uses only `Orient` on input corner points (exact, existing); for shared corners the intersection of two coplanar triangles is
characterised by the wedge / edge-side conditions above (every other case takes the old path), so the omitted clips would add only the shared
corners or the shared boundary edge, which `prepare_touched` / `triangulate_touched` already treat as no-ops (corners removed from `Points`,
boundary constraints). Tests: (1) a property test over random coplanar triangle pairs sharing 1, 2, 3 corners (grid points, degenerate wedges, collinear rays): the decision implies
that all six clip results are corners / the shared edge, and "undecided" otherwise (an oracle with the old clips); (2) result hash equality of booleans on
flat-faced procedural chains (the step-3 setting `R op A`, `Similarity.All()` invariance, 2^k scales bit for bit), including reuse and the parallel-equals-serial determinism test;
(3) mutants: flip each of the 3 conditions, the tests must fail. Review: exact-code review of the file (REVIEW_CHECKLIST) and the incremental mutation run of the changed lines.
Risk: the triangles that stay untouched change `predict_pieces` / `touchedPoints` counts, not the result (hash equal here); the review must check the
`ConstraintEdges` consumers.

Coordination (coordinator, 2026-11-19): lever 2 no-go accepted (below the 10 % bar). The lever of section 3 goes to the owner after the ABC number; the library
PR does not start before the owner's go. #1158 (`ear_clip` in `ExactPlanarSimplify`, another file and stage) is also in `ExactMeshBoolean`; no overlap
with `intersect_coplanar` expected.

## 5. Reproduce

`perf/chains_probe_2026-11-19/README` is this page; commands: `abc_chained.py run ... --methods ours_probe | ours_probe_expr`
(workflow `abc-chained.yml` on `runs/boolean-chains`, `methods` input), `BooleanComparison chain-probe-proc abc-ucd <seed> 2 <reps>` with
`G3_PROC_N=24 G3_PROC_SHAPE=2` for the procedural chains, `data/chain_probe.py summary <rows>...`.

Notes: the publish job of the workflow could not push to the branch it runs from (three runs); rows are committed from the artifacts by hand, the last run pushes
to `runs/boolean-chains-rows`. Run 44470885321 (the first with the pre-decision, 90 ABC chains) finished, but its artifact could not be downloaded from the
session (the proxy denies the blob host), so its numbers are replaced by the smaller rerun below.
