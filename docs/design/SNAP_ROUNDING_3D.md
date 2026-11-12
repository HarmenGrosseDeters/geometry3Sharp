# Practical 3D snap rounding and the cost of rounding: leads, falsification runs, recommendation

Status: **research note, 2026-11-12** (hard open problems 1 and 3 of
[roadmap/research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), taken together on the
owner's priority "3 together with the leads of 1"). No library code. The tools are in
`benchmarks/SnapRoundingExperiment` (`trace/`, `cull`, `lattice`) and `tools/Z3Pilot/separating_axis.py`; the owner
decides on the options of section 8.

Measured on master `46529be` (D1, #513, included), 4 cores, the cloud container of the session. Meshes are fetched at run
time (Thingi10K) and never committed.

## 1. Result in short

- **The roadmap's figure is out of date.** SafeRounding is 1-69 % of a boolean on the rotated cases today (median 23 %,
  33 operations; sum 76.9 of 282.2 s = 27 %), not 72-86 %. 519511 union: 4.7 of 17.0 s.
- **There is no sub-linear rounding scheme to be had from the leads, and none is needed for what the time is spent on.**
  The cost has two unrelated shapes:
  - *clean large results:* 60-80 % of the rounding is the first full search, and that is the enumeration of box
    neighbours (85-850 box hits per query triangle), not exact arithmetic;
  - *dirty rotated results:* the evaluations of proposed edits, and on the two worst cases (1368057 and 42327 union)
    work **after the evaluation limit was reached**.
- **The certified empty ball (a lower distance bound) is closed as a filter.** The pair tests it could clear are 0-33 %
  of the rounding time, 8 % in the median of the traced operations, before the cost of the certificate.
- **Its directional form, a separating-axis prefilter, is correct (lemma checked with Z3) and not worth building.** It
  removes 86-97 % of the exact pair tests of the first search but only 4-52 % of its time with cached axes, and as a
  real A/B inside the rounding it changes the sum by -1.6 % (noise), with all 33 result hashes identical.
- **One lever with a real gain was found on the way:** stopping the passes at the evaluation limit. Rounding -33 % in
  the sum (1368057 union 17.3 to 3.1 s, 42327 union 10.3 to 3.0 s), the booleans -9 %, all 33 result hashes identical.
  What changes is `SmallestRejectedDrift` on results that reached the limit: an owner decision (section 8, option 1).
- **Idea 3 is not falsified; it is the lead of problem 1 that survives.** After D1, SafeRounding leaves 29 of 198
  generated clusters and 6 of 114 UTM cases. A random search over representable positions with the triangles
  unchanged makes **29 of these 35 clean** within the drift that is left, in a median of 0.6 s; moves of a single
  vertex that each remove a defect already reach 15. So most leftovers are local minima of the repair's own narrow
  proposals, not cases without a solution.
- **No guarantee.** Nothing found, in the literature or here, gives "always clean within the bound" at practical cost;
  Diazzi et al. 2023 call the conversion to floating point without flips "an unsolved problem (though rare in
  practice)".

## 2. What was read, and what is not repeated

Read: the hard-problems section and the open research questions of research.md, ideas 1 and 3 of
[CROSS_FIELD_GEOMETRY.md](../research/CROSS_FIELD_GEOMETRY.md), [SAFE_ROUNDING.md](SAFE_ROUNDING.md) (D′, sections 3,
6, 8), [SAFE_ROUNDING_D1_DRIFT.md](SAFE_ROUNDING_D1_DRIFT.md), [ROUNDING_EXACT_PAIRS.md](ROUNDING_EXACT_PAIRS.md),
[PAIR_SEARCH.md](PAIR_SEARCH.md), [SELF_INTERSECTION_PATCH_CERTIFICATE.md](SELF_INTERSECTION_PATCH_CERTIFICATE.md),
[TRITRI_PREFILTER.md](TRITRI_PREFILTER.md), [hotspots.md](../roadmap/hotspots.md),
[EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md), [exact-boolean.md](../roadmap/exact-boolean.md).

Closed before and not measured again: options A and B (#313), kernel B, canonical ids, incremental pairs and the
budget (#327, #335), the projection filter (#397), the exact-pair skip (L1), the dirty tree, the patch certificate,
SIMD. `mesh_ops/ExactMesh*` and `math/Exact*` were not touched (locked during Exact Boolean 3.0).

## 3. Prior art (papers only; [V] full text read, [S] abstract or snippet)

No source code of CGAL, Triangle / Triangle.NET, TetGen or any GPL / LGPL / MPL project was opened.

| Topic | Source | What it gives |
|---|---|---|
| Guaranteed rounding of a 3D subdivision | S. Fortune, *Vertex-rounding a three-dimensional polyhedral subdivision*, DCG 22, 1999 [S] | a grid finer by about 4n, O(n^4) vertices in the worst case; Milenkovic and Sacks 2019 [V]: it "increases the complexity to n^4 in theory and in practice" |
| | O. Devillers, S. Lazard, W. Lenhart 2018 / 2020 (SAFE_ROUNDING.md section 3) | the only full guarantee, O(n^13) and more in the worst case |
| Status of the problem | B. Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG*, arXiv 2405.12949, 2024 [V] | snap rounding named as the main missing component |
| Rounding through a tetrahedral mesh | Y. Hu et al., *Fast tetrahedral meshing in the wild*, TOG 2020, arXiv 1908.03581 [V]; TetWild 2018 through its appendix | a vertex is rounded only if no incident tetrahedron inverts; "not guaranteed to succeed in the rounding phase"; vertices may move inside an envelope, so it is not a bounded rounding of a fixed surface |
| | L. Diazzi, D. Panozzo, A. Vaxman, M. Attene, *Constrained Delaunay tetrahedrization: a robust and practical approach*, TOG 2023, arXiv 2309.09805 [V] | nearest-double rounding keeps 93.22 % of 4408 Thingi10K models valid, a post-process of edge swaps 99.77 %; "converting implicit points to floating-point coordinates with no flips remains an unsolved problem (though rare in practice)"; a Schönhardt polyhedron inside one grid cell has no representable Steiner point |
| | L. Diazzi, M. Attene, *Convex polyhedral meshing for robust solid modeling*, TOG 2021, arXiv 2109.14434 [V, in part] | implicit points kept; the rounding problem avoided on purpose |
| Local repair in rationals, then floats | Liu, Ye, Meng, Liu, Chen, *Robust and fast local repair for intersecting triangle meshes*, CAD 2025 [S] | local regions resolved in rationals, embedded in local tetrahedral meshes, optimized back to floats; no guarantee or failure rate seen |
| Certificates reused under motion | L. Verlet, Phys. Rev. 159, 1967 (through the LAMMPS documentation) [S]; X. Zhang, M. Lee, Y. J. Kim, *Interactive continuous collision detection for non-convex polyhedra*, The Visual Computer 22, 2006 [S] | a neighbour list valid until a particle moved half the skin; a distance lower bound over a motion upper bound gives a safe step |
| Intersection-free simplification at scale | S. Oh et al., *PaMO*, CGF 2025, arXiv 2509.05595 [V, in part] | no incremental certificate: a full GPU BVH and all pairs after every iteration |
| Separating axes | S. Gottschalk, M. Lin, D. Manocha, *OBBTree*, SIGGRAPH 1996; T. Akenine-Möller, *Fast 3D triangle-box overlap testing*, JGT 6, 2001 [from memory, not re-read] | the separating-axis theorem as used in section 5.3 (any axis that separates the projections proves disjointness) |
| Hardness | V. Milenkovic, L. Nackman, *Finding compact coordinate representations for polygons and polyhedra*, SoCG 1990 [S] | rounding that keeps the combinatorial structure is NP-complete (the exact theorem was not verified) |

Not found (terms searched: "snap rounding 3D", "floating point output exact mesh boolean rounding guarantee", "robust
rounding arrangements", follow-ups of Sacks or Milenkovic after 2019, incremental spatial certificates for remeshing): a
method since 2018 with a no-new-intersection guarantee at practical cost, and a paper that carries a per-feature
separation bound across a sequence of local edits. The second is what idea 1 proposed; section 5 measures why it does
not pay here.

## 4. Where the rounding time goes today

### 4.1 Cases and method

Eleven Thingi10K models, each with the rotated, shifted copy of `BooleanComparison export`, three operations each (33
operations): the six rotated cases of hotspots.md (67709, 42327, 1368057, 102375, 498458, 486343: open or
self-intersecting inputs, the results are not clean) and five clean models (519511, 1517923, 1063860, 668056, 119829).

- Baseline: `BooleanComparison profile --repeat 1 --safe-rounding <case>`, raw output
  `trace/results/baseline_2026-11-12.jsonl`.
- Trace: `trace/instrument.py` patches `MeshRoundingRepair.cs` in a throw-away worktree (phase stopwatches, the first
  search once more serially split by kind of pair, every evaluation of a proposed edit with its near queries and pair
  tests). `trace/run_trace.sh`, `trace/summarize.py`, raw output `trace/results/trace*_2026-11-12.jsonl`.
- The trace's own bookkeeping inflates the parts it times, so shares are of the *untraced* rounding time and are an
  order of size.

### 4.2 The share of the rounding

| group | operations | rounding share of the boolean | rounding |
|---|---|---|---|
| all 33 | 33 | 1-69 %, median 23 %, 27 % of the summed time | 76.9 of 282.2 s |
| 519511 (520k result triangles) | 3 | 27 / 11 / 14 % | 4.7 / 1.1 / 1.6 s of 17.0 / 10.5 / 11.3 s |
| 1517923 | 3 | 13 / 13 / 9 % | 0.8 / 0.5 / 0.5 s |
| 67709 rotated (hotspots.md: 18.5 of 21.6 s) | 3 | 37 / 46 / 43 % | 2.9 / 2.3 / 2.0 s |
| 42327 rotated (hotspots.md: 36.8 of 50.8 s) | 3 | 32 / 14 / 12 % | 10.2 / 3.1 / 2.9 s |

On 519511 and 1517923 the rounding is no longer what keeps the operations above 5 s: without any rounding 519511
would still take 9-12 s.

### 4.3 Clean large results: the first search is box neighbours

`summarize.py search` (the first full search repeated serially; "apart" = the two triangles share no corner):

| case | queries | box hits per query | pairs apart | traversal | pair tests apart | pair tests with a shared corner | search in parallel | tree + setup |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 519511 union | 359 333 | 316 | 60.0M | 3.2 s | 7.1 s | 0.5 s | 3.6 s | 0.55 s |
| 519511 difference | 342 471 | 51 | 6.3M | 1.4 s | 1.1 s | 0.4 s | 1.0 s | 0.43 s |
| 1517923 difference | 43 255 | 255 | 5.5M | 0.12 s | 0.52 s | 0.06 s | 0.62 s | 0.08 s |
| 668056 union | 186 022 | 85 | 9.2M | 0.66 s | 1.16 s | 0.21 s | 0.98 s | 0.26 s |
| 1063860 union (98 % exact triangles) | 8 727 | 355 | 2.7M | 0.05 s | 0.26 s | 0.02 s | 0.19 s | 0.44 s |
| 1368057 union (not clean) | 71 638 | 855 | 32.4M | 0.92 s | 3.5 s | 0.15 s | 1.9 s | 0.23 s |

- The search already starts only from the triangles with a rounded corner (L1), so it is linear in those, times their
  box neighbours. A query returns hundreds of triangles because the results hold long thin triangles on tilted flat
  faces (CAD parts, the rotated copy): their axis boxes overlap in bulk, and almost none of these pairs intersects (0 of
  60M on 519511 union).
- A pair test costs 86-118 ns; 3-30 % of the pairs apart reach the exact stage (they are not decided by the
  projection filter).
- Where nearly every triangle is exact (1063860), the tree over all triangles and the linear setup (anchors, component
  scales, the position map) are larger than the search: 0.44 against 0.19 s of 0.85 s.

### 4.4 Dirty rotated results: evaluations, and work past the limit

`summarize.py trace` and `eval`:

| case | rounding | evaluations | of them past the limit | accepted | box hits per near query | `near` (share of the rounding) | pair tests apart (share) | pair tests with a shared corner (share) | passes outside `improves` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 486343 union | 1.9 s | 14 661 | 6 | 1 152 | 95 | 14 % | 48 % | 9 % | small |
| 67709 union | 2.9 s | 75 254 | 1 714 | 5 693 | 25 | 30 % | 26 % | 14 % | small |
| 102375 union | 5.0 s | 131 198 | 122 704 | 3 014 | 53 | 56 % | 14 % | 6 % | small |
| 42327 union | 10.2 s | 710 525 | 426 987 | 1 954 | 84 | 5 % | 10 % | 3 % | about 8 of 11.5 s |
| 1368057 union | 18.2 s | 3 992 940 | 3 991 858 | 610 | 872 | 1 % | 4 % | 0 % | about 14 of 18 s |
| 119829 union (clean, 13 447 edits) | 1.8 s | 13 466 | 0 | 13 447 | 15 | 52 % | 2 % | 8 % | small |

- Where the limit is not the story, the time is in `near` (15-62 % of the rounding, 4-30 µs per query) and in pair
  tests of triangles apart (26-52 %), at 150-400 ns per test including the trace.
- On 1368057 union and 42327 union three quarters of the rounding is spent after `EvaluationLimitReached`: the passes
  go on proposing edits, each is rejected at the limit check, and the only effect is `SmallestRejectedDrift`. #327 made
  this cheap for `try_move` only.
- Edits are local: 14 661 evaluations on 980 distinct places (486343 union), 9-25 evaluations per place on the dirty
  cases, 1.1 on 119829.

## 5. The leads of problem 3, each with its falsification run

### 5.1 The certified empty ball (a lower distance bound): closed

The lead: a completed BVH search gives a lower bound L on the distance of a moved primitive to everything it is not
incident to; with L > r + Δa + Δb the exact check is skipped.

Run (`G3_ROUNDING_TRACE_DIST=1`, `trace_dist_2026-11-12.jsonl`): for every pair test of an added triangle against a
triangle it shares no corner with, the distance of the two triangles. The optimistic bound of the filter is the time
of the tests with a positive distance, with a certificate that costs nothing:

| | share of the rounding time |
|---|---|
| 486343 (3 operations) | 32-33 % |
| 1368057 intersection | 21 % |
| 42327, 67709, 102375 (9 operations) | 3-14 %, median 8 % |
| 119829, 498458, 1368057 union and difference | 0-9 % |
| median of the 21 traced operations | 8 % |

A distance bound between two triangles (nine edge pairs and six corners) costs more than the pair test it replaces
(the projection filter decides in 86 ns), and a ball around a long thin triangle is the wrong shape (the report
#368 warned of it). On the clean large results there are no evaluations to speak of. **Closed by the stop rule: the
bound is below 10 % on most cases before the certificate is paid for.**

### 5.2 Clearance skins (idea 1): closed with 5.1

Reuse exists (9-25 evaluations per place), but what a skin would save is the `near` query and the tests of 5.1. The
tests are bounded by 5.1; the `near` time is a matter of the query itself (5.5), not of a certificate: on 119829, with
1.1 evaluations per place and nothing to reuse, `near` is still 52 % of the rounding.

### 5.3 The directional certificate: a separating-axis prefilter. Correct, measured, not worth building

**Rule.** Two triangles A and B (double corners). For a vector a with double components, for a corner x

    p~(x) = fl(fl(fl(a_x x_x) + fl(a_y x_y)) + fl(a_z x_z))
    s~(x) = fl(fl(fl(|a_x| |x_x|) + fl(|a_y| |x_y|)) + fl(|a_z| |x_z|))
    m~_T  = fl(Γ max_k s~(T_k)),   Γ = 8u,  u = 2^-53
    lo~_T = fl(min_k p~(T_k) − m~_T),   hi~_T = fl(max_k p~(T_k) + m~_T)

and the pair is *apart* if lo~_B > hi~_A or lo~_A > hi~_B for one of eight axes: each triangle's normal and the three
in-plane normals of its edges, all computed in doubles.

**Lemma.** If the pair is apart, A and B have no common point, so `IntersectsBeyondShared` is false and the pair test
may be skipped with the same result. It holds for any vector a: the axes need no error bound, only the comparison of
the projections does. Triangles that share a corner are never apart (the shared corner projects to one value on both
sides), so they need no special case.

**Proof, in the obligations of `tools/Z3Pilot/separating_axis.py`** (14 queries, 3.5 s; UNSAT with satisfiable
assumptions; `results/separating_axis_2026-11-12.json`):
- R1: |p~ − p| ≤ 4u S for S the sum of the absolute exact products, each operation within (1 + δ), |δ| ≤ u;
- R2: fl(Γ s~) ≥ 4u S, the margin computed in doubles covers R1;
- R3: with every exact projection within its margin and lo_B − m_B > hi_A + m_A, max p(A_k) < min p(B_k);
- G1: max a·A_k < min a·B_k implies no common point (free real coordinates).
Bridges, reviewed and not by Z3: rounding is monotonic (the comparison of the rounded bounds implies the real one);
fl(|a||x|) = |fl(a x)|; a NaN or infinite projection compares false. Assumption: the supported range of
`MeshRoundingRepair` ([2^-200, 2^200], no subnormal coordinate), in which the products neither underflow nor overflow.
Six mutants are SAT: a bound of 2u in R1, Γ = 2u, the margin of one corner only, a non-strict comparison (twice), one
corner left out of the maximum.

**Upper bound** (`SnapRoundingExperiment cull`, the first search of the rounded result outside the library, axes
cached per triangle and not counted: 170-320 ms in parallel for 520k triangles; `cull_2026-11-12.jsonl`; the set of
intersecting pairs is the same in every variant):

| case | box hits per query | pair tests: box | with the 8 axes | search: box | with the 8 axes | axes also at the tree nodes |
|---|---:|---:|---:|---:|---:|---:|
| 519511 union | 316 | 62.9M | 3.5M (−94 %) | 2.84 s | 1.83 s (−36 %) | 2.48 s, 212 hits |
| 519511 difference | 51 | 8.8M | 2.8M (−68 %) | 0.76 s | 0.73 s (−4 %) | 1.03 s, 39 hits |
| 1517923 difference | 255 | 6.0M | 0.71M (−88 %) | 0.21 s | 0.10 s (−52 %) | 0.19 s, 194 hits |
| 668056 union | 85 | 10.6M | 1.45M (−86 %) | 0.54 s | 0.37 s (−32 %) | 0.67 s, 62 hits |
| 1063860 union | 355 | 2.8M | 0.09M (−97 %) | 0.07 s | 0.06 s (−14 %) | 0.03 s, 76 hits |
| 1368057 union | 843 | 33.2M | 1.65M (−95 %) | 1.17 s | 0.76 s (−35 %) | 1.20 s, 593 hits |

- The exact side of the three corners to the other triangle's plane (`box+side`, no new bound) is slower than the
  projection filter it would precede.
- Culling tree nodes by the query triangle's four axes removes a quarter to a third of the hits and costs more than it
  saves, except where almost all triangles are exact (1063860).

**A/B inside the rounding** (`G3_SEPAXIS=1`: the eight axes computed per pair test, no cache, in front of
`intersects`; `trace/run_ab.sh`, two rounds, the faster round per operation; `ab_2026-11-12.jsonl`): the summed
rounding 73.9 to 72.8 s (−1.6 %), the booleans 272.6 to 271.3 s; 519511 union 4.5 to 3.5 s and 668056 union 1.48 to
1.19 s, 67709 +12 to +30 %, 486343 union −17 %; **33 of 33 result hashes identical**.

**Verdict.** The rule is sound and bit-identical, and the gain is inside the noise except on results with hundreds of
box hits per query, where it is about 1 s of 16 s. The remaining cost of the search is the enumeration of the box
hits, which no pair-level certificate touches. Not recommended as a work item; the lemma stays as a checked building
block.

### 5.4 Sub-linear rounding

The question of problem 3 was whether rounding can be sub-linear in the result size for local edits. Answer from the
data:
- *The edits are already local.* Each evaluation queries a tree; nothing in it is linear in the result.
- *The first search* is linear in the triangles with a rounded corner (L1) and cannot be less: each has new
  coordinates. A certificate inherited from the exact arrangement (the features are disjoint before rounding, every
  corner moves by at most √3/2 ulp) restricts the candidates to features closer than the rounding, which is the box
  overlap the search already uses.
- *The linear rest* is the setup and the tree over all triangles: 10-28 % of the rounding where few triangles are exact
  and up to about half where nearly all are (1063860: 0.44 of 0.85 s; 119829 union: 0.27 of 1.8 s). In absolute terms
  0.1-0.6 s per operation.

### 5.5 What the trace found that is not a lead of the list

| finding | measured | kind |
|---|---|---|
| Passes go on after the evaluation limit | A/B `G3_STOP_AT_LIMIT=1` (with the prefilter on, which alone is −1.6 %): rounding 73.9 to 49.5 s (−33 %), booleans 272.6 to 247.7 s (−9 %); 1368057 union 17.3 to 3.1 s (boolean 32.2 to 18.4 s), 42327 union 10.3 to 3.0 s, 42327 intersection and difference −35 to −40 %; 33 of 33 hashes identical | changes `SmallestRejectedDrift` where the limit is reached: owner decision |
| `near` is 15-62 % of the rounding where edits are evaluated | 4-30 µs per query against about 1 µs for a tree query of the first search; 119829 union 52 %, 102375 union 56 % | not diagnosed here (the changed-triangle tree and its linear list are the suspects); bit-identical lever |
| Tree and setup over all triangles where few are rounded | section 5.4 | bit-identical lever, small in seconds |

## 6. The leads of problem 1

### 6.1 What a guarantee could mean

- *Certify in advance that plain rounding is safe:* the d-separation of Milenkovic and Sacks. It exists as an exact
  check in the test harness (`SeparationCertificate`) and adds nothing to a result that the exact search verifies
  anyway.
- *Always reach a clean result within the bound:* existence is not the obstacle in principle (Devillers et al. round
  within 3/2 grid units, and `maxLength` = 1e-5 of the diagonal is about 2^36 ulps), the cost and the change of the
  complex are. With the triangles unchanged a solution need not exist (the Schönhardt example of Diazzi et al. for
  tetrahedral meshes; the NP-completeness result of Milenkovic and Nackman for structure-preserving rounding).
- So the practical question is the one of idea 3: *how often* does a clean configuration with the same triangles exist
  within the drift that is left, and can a search find it.

### 6.2 What is left after D1

`SnapRoundingExperiment run <case> safe` on the generated families (`clusters_safe_2026-11-12.jsonl`,
`utm_safe_2026-11-12.jsonl`): clusters 169 of 198 clean (165 before #316 and D1), UTM 108 of 114. All 29 cluster
leftovers are the densest ones (count 8). Their drift is no longer the common cause: 10 of the 29 used under 3 % of the
bound, 17 under half, 4 over 80 %.

### 6.3 The probe (idea 3, in its cheapest form)

`SnapRoundingExperiment lattice <case> 60`: after SafeRounding, a random search with the triangles unchanged. A step
moves one to three vertices of a defect triangle (or of a neighbour) by a random vector of a size between one ulp and
the drift left, so every position is a double; it is kept if the number of defects of the whole mesh (degenerate
triangles and intersecting pairs, as `NotCleanReasons` counts them) does not grow, no vertex is farther than the drift
left from where SafeRounding put it, and no triangle above 100 times the degenerate area turns over. "Clean" is
`MeshRobustRepair.NotCleanReasons` empty at the end. `lattice_2026-11-12.jsonl`.

| variant | clean of 35 | median time to clean | median evaluations |
|---|---:|---:|---:|
| up to 3 vertices, steps with equal defects kept (60 s) | **29** | 0.6 s | 380 |
| 1 vertex, steps with equal defects kept (30 s) | 28 | 1.2 s | 693 |
| up to 3 vertices, only steps that remove a defect (30 s) | 24 | 1.3 s | 750 |
| 1 vertex, only steps that remove a defect (30 s) | 15 | 0.3 s | 106 |

- Not clean: five count-8 clusters keep 2-7 intersecting pairs after 60 s, and one UTM case is an inside-out tiny
  component (no defect of the kind the search counts).
- The fourth row is within SafeRounding's own rule (one vertex, each step improves): 15 leftovers have a single-vertex
  move that the repair does not propose. Its moves go along the normal of the intersected triangle or away from the
  base of a cap, at a few fixed sizes.
- Steps that keep the defect count (a plateau) add 5-13 cases; moving several vertices at once adds little beyond
  that.
- Most clean ends used most of the drift left (the random walk wanders), so the probe says a solution exists within the
  bound, not how near one is.

**Limits of the probe.** Generated families only: the closed Thingi10K results with leftovers (6 of the 400 set in
SAFE_ROUNDING.md 8.4, and the unclean operations of the 16 239 cohort) were not run. The search checks the whole mesh
per step, which is only possible on 450 triangles. It is a random search: it proves existence where it ends clean and
nothing where it does not. The earlier probe of this kind (SAFE_ROUNDING.md 8.1: 5 of 33 clusters) ran before D1, when
two thirds of the clusters had spent their drift.

### 6.4 The rule this suggests, and what it needs before it is built

Candidate (for a design by the owner's choice, not built here): after the passes, for each remaining defect, propose
moves of one of its vertices to positions drawn from a fixed, seeded sequence within the drift left (deterministic:
the seed is the vertex id and the pass), each evaluated by the existing `improves()`.
- *No new intersection, no new degenerate triangle:* unchanged, it is `improves()`'s exact check.
- *The bound:* a move is a displacement edit of the reference rule (D1 note section 3): no new lemma.
- *Termination:* with the existing "must improve" rule unchanged (row four above); a plateau rule needs its own
  argument (a bound on the plateau steps per defect, or a potential), stated as a lemma first.
- *Cost:* bounded by the evaluation budget that exists.
- *Invariance:* the sizes are multiples of the ulp of the component and of the drift left, both 2^k-scale
  invariant; the directions must be so too (drawn in units of the bound).
Before a design: the same probe on the real leftovers (the D1 cohort list), which is the falsification run that would
kill it.

### 6.5 Z3 as the rule checker (research.md, step 3)

- (a) "a lower distance bound computed in doubles is a lower bound": not needed, the distance form is closed (5.1).
  Its directional replacement is R1-R3 of 5.3, checked.
- (b) "the certificate contains the moved primitive and both motions": G1 of 5.3 for the directional form, checked.
- (c) a bounded counterexample search for a local rounding rule: no new local rule is proposed here that decides
  without `improves()`; the candidate of 6.4 keeps the exact check, so there is nothing for Z3 to refute.

## 7. How others solve it

- Keep exact or implicit coordinates and do not round (Diazzi and Attene 2021; Cherchi et al.; our
  `ComputeExactVertices`).
- Round and verify, repeat on failure (Zhou et al. 2016; Diazzi et al. 2023 with edge swaps, 99.77 %; TetWild).
- Iterative snap rounding without a guarantee (Valque and Lazard 2025).
- Guaranteed constructions that change the complex (Fortune 1999; Devillers et al. 2020): not implemented anywhere we
  found.
- For the search cost: a full BVH and all pairs each time, on the GPU (PaMO 2025); nobody we found amortizes
  certificates across local edits.

## 8. Recommendation and options for the owner

| # | Option | Expected gain (measured) | Risk | Effort |
|---|---|---|---|---|
| 1 | **Stop the passes at the evaluation limit** (or make every `try_*` past the limit compute only its drift, as `try_move` does since #327) | rounding −33 %, booleans −9 % on the 33 operations; the two worst rotated cases 17.3 to 3.1 s and 10.3 to 3.0 s; meshes bit-identical | `SmallestRejectedDrift` (and `StrictRounding`'s message) is no longer the minimum over all proposals where the limit was reached; the drift-only variant keeps it at more code | S for the stop (a decision on the reported value), M for drift-only in every `try_*`; tier M, rounding mutation shards |
| 2 | **Seeded single-vertex position search after the passes** (6.4), preceded by the probe on the real D1 leftovers | on the generated leftovers 15 of 35 within today's rule, 28-29 with plateau steps; unknown on Thingi10K | results change (more clean results, other coordinates); the plateau variant needs a termination lemma; invariance of the random directions | probe on the cohort leftovers: one runner job and half a day; the build M-L (tier L: it changes rounding results) |
| 3 | `near` and the linear setup (5.5) | upper bounds: `near` 15-62 % of the rounding where edits are evaluated; tree and setup 0.1-0.6 s per large operation | none for results (bit-identical); the diagnosis of `near` is open | S-M each, after a profile of `near` |
| 4 | Separating-axis prefilter (5.3) | −1.6 % in the sum; about 1 s of 16 s on 519511 union | a new floating-point margin in the rounding (checked by Z3, needs the bounds review) | M; **not recommended** |
| 5 | Distance certificates, clearance skins, node culling (5.1, 5.2, 5.3) | below the stop rule | | **closed** |

Recommended order: 1 (decide the reported value), then the probe of 2 on the real leftovers before any design, then 3
if the 5 s target still needs it. Problem 3 should be restated in the roadmap: the open cost is the box-neighbour
enumeration on results with long thin triangles and the linear setup, not exact arithmetic and not the lack of a
certificate. Problem 1 stays open as a guarantee; its practical lead is option 2.

## 9. Reproduce

```sh
# cases (meshes are not committed): Thingi10K raw meshes by id, then the rotated pair
dotnet build benchmarks/BooleanComparison -c Release
dotnet benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll export <id>.stl cases/rot/<id>
dotnet benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll profile --repeat 1 --safe-rounding cases/rot/<id>   # baseline

# the trace and the A/B (a throw-away worktree; nothing of it is committed to the library)
git worktree add --detach /tmp/wt 46529bed
python3 benchmarks/SnapRoundingExperiment/trace/instrument.py /tmp/wt/mesh_ops/MeshRoundingRepair.cs
dotnet build /tmp/wt/benchmarks/BooleanComparison -c Release -o /tmp/tool
cd benchmarks/SnapRoundingExperiment/trace
./run_trace.sh /tmp/tool trace.jsonl 0 cases/rot/*          # 1 instead of 0: with the pair distances (slow)
./run_ab.sh /tmp/tool ab.jsonl 2 cases/rot/*
python3 summarize.py trace trace.jsonl baseline.jsonl; python3 summarize.py search trace.jsonl
python3 summarize.py eval trace.jsonl; python3 summarize.py ab ab.jsonl

# the culling prototype and the lattice probe
dotnet build benchmarks/SnapRoundingExperiment -c Release
dotnet benchmarks/SnapRoundingExperiment/bin/Release/net10.0/SnapRoundingExperiment.dll cull cases/rot/519511 union
dotnet benchmarks/SnapRoundingExperiment/bin/Release/net10.0/SnapRoundingExperiment.dll lattice cluster:Cluster:36:8:3 60

# the lemma
python3 -m pip install --require-hashes -r tools/Z3Pilot/requirements.txt
python3 tools/Z3Pilot/separating_axis.py
```

Case ids: 67709, 42327, 1368057, 102375, 498458, 486343, 519511, 1517923, 1063860, 668056, 119829. The leftover cases
of 6.2 are the lines with `"clean":false` in `clusters_safe_2026-11-12.jsonl` and `utm_safe_2026-11-12.jsonl`.
