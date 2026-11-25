# Practical 3D snap rounding and the cost of rounding: leads, falsification runs, recommendation

Status: **research note, 2026-11-12, continued 2026-11-15** (hard open problems 1 and 3 of
[roadmap/research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), taken together on the
owner's priority "3 together with the leads of 1"). No library code. The tools are in
`benchmarks/SnapRoundingExperiment` (`trace/`, `cull`, `lattice`) and `tools/Z3Pilot/separating_axis.py`,
`plateau_search.py`; the owner decides on the options of section 8; the recommendation of section 10 is decided
(owner 2026-11-15: no-go before the release).

Sections 1 to 9 are measured on master `46529be` (D1, #513, included), 4 cores, the cloud container of the session, and
are kept as measured. Two of their options were built since: option 1 as a stop of the passes at the evaluation limit
(#725, merged 2026-11-12), option 2 in its simple variant as the position search (#755, merged 2026-11-13,
[SAFE_ROUNDING.md](SAFE_ROUNDING.md) section 9). Where that changes a statement, the section says so. Section 10
(2026-11-15, master `db85c6e`) is the design of what is left of option 2, the plateau variant, with its probe on real
leftovers, its lemma and a go / no-go recommendation. Meshes are fetched at run time (Thingi10K) and never committed.

## 1. Result in short

- **The roadmap's figure is out of date.** SafeRounding is 1-69 % of a boolean on the rotated cases today (median 23 %,
  33 operations; sum 76.9 of 282.2 s = 27 %), not 72-86 %. 519511 union: 4.7 of 17.0 s.
- **There is no sub-linear rounding scheme to be had from the leads, and none is needed for what the time is spent on.**
  The cost has two unrelated shapes:
  - *clean large results:* the first full search is the largest part (59-98 % of the rounding on 519511, 1517923 and
    668056; 22-43 % on 1063860, where the tree and the setup over all triangles are larger), and it is the
    enumeration of box neighbours (20-355 box hits per query triangle, 316 on 519511 union), not exact arithmetic;
  - *dirty rotated results:* the evaluations of proposed edits, and on the two worst cases (1368057 and 42327 union)
    work **after the evaluation limit was reached**.
- **The certified empty ball (a lower distance bound) is closed as a filter.** The pair tests it could clear are 0-33 %
  of the rounding time, 8 % in the median of the traced operations, before the cost of the certificate.
- **Its directional form, a separating-axis prefilter, is correct (lemma checked with Z3) and not worth building.** It
  removes 68-97 % of the exact pair tests of the first search but only 4-52 % of its time with cached axes, and as a
  real A/B inside the rounding it changes the sum by -1.6 % (noise), with all 33 result hashes identical.
- **One lever with a real gain was found on the way:** stopping the passes at the evaluation limit. Rounding -33 % in
  the sum (1368057 union 17.3 to 3.1 s, 42327 union 10.3 to 3.0 s), the booleans -9 %, all 33 result hashes identical.
  What changes is `SmallestRejectedDrift` on results that reached the limit: an owner decision (section 8, option 1).
  Built (#725; measured as built: rounding −32 %, booleans −7 %, EXACT_BOOLEAN_PERFORMANCE.md).
- **Idea 3 is not falsified; it is the lead of problem 1 that survives.** After D1, SafeRounding leaves 29 of 198
  generated clusters and 6 of 114 UTM cases. A random search over representable positions with the triangles
  unchanged makes **29 of these 35 clean** within the drift that is left, in a median of 0.6 s; moves of a single
  vertex that each remove a defect already reach 15. On the real leftovers, the 5 operations of the 16 239 D1 cohort
  that the rounding leaves, single-vertex moves that each remove a defect make **4 of 5 clean**. So most leftovers are local minima of the repair's own narrow
  proposals, not cases without a solution. Built as an enumerated search (#755): 10 of the 35 and the same 4 of 5.
- **The plateau variant of that search is designed, checked and not recommended for now (section 10).** On what #755
  leaves, steps that keep the defect counts reach more (3-9 of the 19 generated leftovers the rule reaches, against
  none); on real results the gain is one operation of 16 239, and the cost is a rollback inside the repair. The
  termination lemma is checked with Z3.
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

Re-measured on master `57623890` (2026-11-22), with procedural cases and candidates for parts (b) to (d):
[ROUNDING_COST_2026-11.md](../research/ROUNDING_COST_2026-11.md). The hit counts below still hold; in time the first
search is 19-37 % enumeration and the rest pair tests on the hits, and the axes of section 5.3 pay once they are
computed per triangle instead of per pair (−3.5 % of 48 operations, hashes the same).

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
  box neighbours. Where a query returns hundreds of triangles (it returns 20-51 on four of the operations traced), it is because the results hold long thin triangles on tilted flat
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

- Where the limit is not the story, the time is in `near` (14-56 % of the rounding in the trace of 4.1, up to 62 % in the run with distances; 4-30 µs per query) and in pair
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
fl(|a||x|) = |fl(a x)|; a NaN or infinite projection compares false. Assumption: no product a_i x_i underflows to a subnormal or overflows (R1 is a relative error model). The supported
range of `MeshRoundingRepair` ([2^-200, 2^200], no subnormal coordinate) does not give this by itself: an in-plane
axis is a product of three coordinate differences and its components can come near 2^-860 at the low end of the range,
so a product with a coordinate can be subnormal. A build would scale each axis by a power of two (exact, and any
multiple of a separating axis separates) or answer "not apart" when a product is below 2^-1022; the prototype does
neither and was run on coordinates of ordinary size.
Six mutants are SAT: a bound of 2u in R1, Γ = 2u, the margin of one corner only, a non-strict comparison (twice), one
corner left out of the maximum.

**Upper bound** (`SnapRoundingExperiment cull`, the first search of the rounded result outside the library, axes
cached per triangle and not counted: 25-143 ms in parallel, 136 ms for the 520k triangles of 519511 union; `cull_2026-11-12.jsonl`; the set of
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
box hits per query, where it is about 1 s of the rounding (519511 union 4.5 to 3.5 s; the boolean, 16.36 to 16.32 s,
stays within the noise). The remaining cost of the search is the enumeration of the box
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
| `near` is 14-56 % of the rounding where edits are evaluated (5 % and less where the limit dominates) | 4-30 µs per query against about 1 µs for a tree query of the first search; 119829 union 52 %, 102375 union 56 % | not diagnosed here (the changed-triangle tree and its linear list are the suspects); bit-identical lever |
| Tree and setup over all triangles where few are rounded | section 5.4 | bit-identical lever, small in seconds |

**What the drift-only variant can reach (measured 2026-11-12 after the owner chose it).** The time of each kind of
proposal after the limit was reached (a scratch timer around every `try_*`, the same worktree; the timers add about
40 ns per call):

| case | passes | `try_move` (drift-only since #327) | `try_collapse` | `try_flip` | `try_zip`, `try_strip`, `try_slide` |
|---|---:|---:|---:|---:|---:|
| 1368057 union | 23.2 s | 9.8 s, 26.2M calls | 5.2 s, 3.0M | 1.0 s, 1.15M | 0.06 s |
| 42327 union | 14.4 s | 7.6 s, 20.0M calls | 0.07 s | 0.7 s, 0.75M | 0.01 s |

So the work past the limit is mostly `try_move`, which is drift-only already: about 370 ns per call, 20-26 million
calls. Making the other `try_*` drift-only can save at most the collapse and flip columns: up to about a quarter of the
passes on 1368057 union and about 5 % on 42327 union, against −82 % and −70 % of the rounding for the stop. Keeping
`SmallestRejectedDrift` bit for bit means walking the same 20-26 million proposals; the two aims do not fit together.
This goes back to the owner: the stop with `SmallestRejectedDrift` documented as "over the proposals made before the
limit" (a larger or equal value, only on results that report `EvaluationLimitReached`), or the small gain.

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
  that. (2026-11-24, #1465: a joint move of two corners that removes a defect, no plateau, is the last resort of the repair for a degenerate triangle
  with an edge of 1e-10, which no single corner can lift within the bound: SAFE_ROUNDING.md section 9.1; it repairs about half of the left-overs of two generated searches, counts in docs/research/ROUNDING_DEGENERATE_LEFT_2026-11.md section 10.3.)
- Most clean ends used most of the drift left (the random walk wanders), so the probe says a solution exists within the
  bound, not how near one is.

**The real leftovers (owner 2026-11-12: run the probe on the D1 cohort).** Of the 16 239 operations of the D1 cohort
run (branch `runs/d1-cohort`, `runs/d1/results/cand.jsonl`) 54 are unclean; 49 of them are rounding clean (contacts and
bowties of the inputs) and **5 are left by the rounding**, each with 1-3 degenerate triangles in a result of 40k-285k
triangles. The probe on these five (60 s, the defects counted at the moved vertices only, against a box tree grown by
the drift left; `lattice_cohort_2026-11-12.jsonl`):

| operation | triangles | drift used | defects | up to 3 vertices, plateau | 1 vertex, each step improves |
|---|---:|---:|---:|---|---|
| 837999 intersection | 40 510 | 0.02 | 3 | clean, 60 evaluations | clean, 15 evaluations, 3 moves |
| 837999 difference | 146 844 | 0.79 | 1 | clean, 29 | clean, 605 evaluations, 1 move |
| 838003 difference | 146 950 | 0.97 | 2 | 1 left after 60 s | 2 left (no improving move in 907k) |
| 958469 difference | 285 052 | 0.37 | 3 | clean, 159 | clean, 15 evaluations, 3 moves |
| 958470 difference | 274 934 | 0.54 | 2 | clean, 30 | clean, 40 evaluations, 2 moves |

Four of the five become clean (`NotCleanReasons` empty for the whole result) with one move per degenerate triangle,
within today's rule; the search itself takes under 0.1 s after the boolean. The moves are large (0.95 of the drift
left): a degenerate triangle needs its corner moved by about the bound, which the repair's own moves (a few fixed
sizes along one direction) do not try. 838003, which had used 97 % of its bound, stays.

**A fixed candidate set instead of random vectors** (`LATTICE_GRID=<n>`, one vertex, only steps that remove a defect,
20 s; `lattice_grid_2026-11-12.jsonl`): the 26 directions with components −1, 0, 1 and the sizes "drift left / 2^j",
j < n. With n = 8: 4 of the 5 cohort leftovers and 13 of the 35 generated ones clean; with n = 24: 4 of 5 and 17 of
35. So the search needs no random numbers: a small enumerated set of large steps does what the random search did
within today's rule.

**Limits of the probe.** The generated families and these five operations; the closed leftovers of the Thingi10K
self-unions (6 of the 400 set in SAFE_ROUNDING.md 8.4) were not run (run on 2026-11-15 with the whole 400 set: section 10.4). On the small cases the search checks the whole mesh
per step. It is a random search: it proves existence where it ends clean and
nothing where it does not. The earlier probe of this kind (SAFE_ROUNDING.md 8.1: 5 of 33 clusters) ran before D1, when
two thirds of the clusters had spent their drift.

### 6.4 The rule this suggests, and what it needs before it is built

Built since, without plateau steps and with an enumerated candidate set in place of the seeded sequence (#755,
SAFE_ROUNDING.md section 9). The plateau rule and its lemma are section 10.

Candidate (as proposed on 2026-11-12): after the passes, for each remaining defect, propose
moves of one of its vertices to positions drawn from a fixed, seeded sequence within the drift left (deterministic:
the seed is the vertex id and the pass), each evaluated by the existing `improves()`.
- *No new intersection, no new degenerate triangle:* unchanged, it is `improves()`'s exact check.
- *The bound:* a move is a displacement edit of the reference rule (D1 note section 3): no new lemma.
- *Termination:* with the existing "must improve" rule unchanged (row four above); a plateau rule needs its own
  argument (a bound on the plateau steps per defect, or a potential), stated as a lemma first.
- *Cost:* bounded by the evaluation budget that exists.
- *Invariance:* the sizes are multiples of the ulp of the component and of the drift left, both 2^k-scale
  invariant; the directions must be so too (drawn in units of the bound).
The probe on the real leftovers did not kill it (6.3: 4 of 5 within this rule).

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
| 2 | **Seeded single-vertex position search after the passes** (6.4) | on the generated leftovers 15 of 35 within today's rule, 28-29 with plateau steps; on the D1 cohort 4 of its 5 rounding leftovers within today's rule | results change (more clean results, other coordinates); the plateau variant needs a termination lemma; invariance of the random directions | the build M-L (tier L: it changes rounding results) |
| 3 | `near` and the linear setup (5.5) | upper bounds: `near` 14-56 % of the rounding where edits are evaluated; tree and setup 0.1-0.6 s per large operation | none for results (bit-identical); the diagnosis of `near` is open | S-M each, after a profile of `near` |
| 4 | Separating-axis prefilter (5.3) | −1.6 % in the sum; about 1 s of the 4.5 s rounding of 519511 union, its boolean within the noise | a new floating-point margin in the rounding (checked by Z3, needs the bounds review) | M; **not recommended** |
| 5 | Distance certificates, clearance skins, node culling (5.1, 5.2, 5.3) | below the stop rule | | **closed** |
| 2b | Plateau variant of option 2 (section 10, 2026-11-15) | 1 of 16 239 cohort operations, no clean result on the 400 self-unions, 3-9 of the 19 generated leftovers it reaches | a rollback inside the repair; success depends on seed and budget; no guarantee | M, tier L; **not built: no-go before the release, owner 2026-11-15** (10.8) |

Owner 2026-11-12: option 1 is built as the stop (#725; the drift-only variant was measured, section 5.5, and dropped:
most of the work past the limit is `try_move`, drift-only already), with `SmallestRejectedDrift` documented as taken
over the proposals made before the limit. Option 2 is built in its simple variant (#755: an enumerated single-vertex
search, only moves that remove a defect); its plateau variant "needs a termination lemma and a separate decision":
section 10.
Recommended order (2026-11-12): 1, then the design of 2, then 3
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

## 10. The plateau variant of the position search: design, probe, lemma, recommendation (2026-11-15)

### 10.1 Where option 2 stands

- Built (#755): after the passes, one corner of each defect that is left is moved to the first of 26 x 8 enumerated
  positions that `improves()` accepts, so every move removes a defect. 10 of the 35 generated leftovers and 4 of the
  5 cohort leftovers became clean; the cohort acceptance run (16 239 operations) changed no rounding-clean result.
- Left open on 2026-11-12: steps that keep the number of defects (a plateau). The probe of 6.3 reached 28-29 of 35
  with them against 15 without. They "need a termination lemma and a separate decision".
- What the rounding still leaves with #725 and #755 (the cohort row from its acceptance run on 36ff5bf, the other
  two measured here on master `db85c6e`):

  | set | results | left by the rounding with a defect |
  |---|---:|---:|
  | D1 cohort, closed models with a rotated copy (`runs/position-search-cohort`, `runs/ps/results/cand.jsonl`) | 16 239 | **1** (838003 difference: one degenerate triangle, 97 % of the bound used) |
  | generated leftovers of 6.2 (run again here: `plateau_safe_2026-11-15.jsonl`) | 35 | **19** (10 are clean, as #755 reports; 6 are rounding clean and not clean: contacts at shared positions or an inside-out tiny component, which the repair does not count as defects) |
  | Thingi10K 400 set, self-unions (open and self-intersecting inputs included; `thingi400_rounding_2026-11-15.jsonl`) | 400 | 34: 3 at the evaluation limit (102375, 1368057, 55928), 31 with 1-77 degenerate triangles (378 in all) and no intersecting pair |

  Two more cohort rows are not rounding clean with no defect left (37506 union, 59770 difference); they are no case
  for a search. Of the 34 self-unions, 31 are open results and the 3 closed ones (102375, 1368057, 357788) also keep
  contacts of the exact result, so none of them can become clean by removing what the rounding left
  (`plateau_thingi_base_2026-11-15.jsonl`). Before D1 and #755 the set had 38 rounding failures (SAFE_ROUNDING.md 8.4).

### 10.2 Prior art (papers only; [V] full text read, [S] abstract or snippet)

No source code was opened. How others solve "a local search is stuck in a state that no single step improves":

| Topic | Source | What it gives |
|---|---|---|
| Sideways moves in local search | B. Selman, H. Levesque, D. Mitchell, *A new method for solving hard satisfiability problems*, AAAI 1992 [S]; J. Frank, P. Cheeseman, J. Stutz, *When gravity fails: local search topology*, JAIR 7, 1997 [S] | GSAT takes the best flip also when it leaves the number of unsatisfied clauses unchanged; most of its time is spent on such plateaus; it ends by a cap on the flips (and restarts), not by a potential. The rule of 10.3 is this scheme with a rollback |
| A potential for untangling | L. Freitag, P. Plassmann, *Local optimization-based simplicial mesh untangling and improvement*, IJNME 49, 2000 [S] | one free vertex at a time, moved to maximize the smallest signed area or volume of its elements: a linear program with convex level sets, so the local step converges; no global convergence. It needs elements with a sign: a surface triangle that is degenerate or meets another one has none |
| Random perturbation with a guarantee | D. Halperin, C. Shelton, *A perturbation scheme for spherical arrangements*, CGTA 10, 1998 [S]; S. Funke, C. Klein, K. Mehlhorn, S. Schmitt, *Controlled perturbation for Delaunay triangulations*, SODA 2005 [S] | every input point is moved at random within a ball until all predicates are safe for the precision; the analysis bounds the probability of a failed draw, so the expected number of draws is finite. It perturbs the *input* by more than the rounding; our bound (1e-5 of the diagonal, of which a leftover has used up to 97 %) is given, not chosen |
| Rounding with the triangles changed | S. Valque, S. Lazard 2025; V. Milenkovic, E. Sacks 2019 (sections 3 and 7; SAFE_ROUNDING.md sections 7 and 8) | iterated snapping without a guarantee; separation by linear programs over several vertices at once (option A, closed in #313) |

Not found (terms searched: "plateau moves mesh untangling", "sideways moves geometric local search termination",
"random vertex perturbation rounding mesh self-intersection", "lattice point search mesh rounding"): a paper that
searches representable positions with a step rule and proves that it ends clean. The search ends by a budget everywhere
we looked.

### 10.3 The rule

After the built search has ended (a round accepted nothing), while defects remain, the effort limit is not reached and
at most `D` defects are left:

- *Chains.* For each defect, in the order of the built search, `S` chains. A chain draws candidates from an integer
  sequence (SplitMix64, as `TerrainSlopeDesign` uses it; seeded with the id of the defect's first triangle, the number
  of the chain and the round): a vertex (a corner of the defect's first triangle or, one time in three, a neighbour of
  one), one of the 26 directions and one of the 8 sizes of the built search, so p = pos(v) + (room(v) / 2^j) d.
  Boundary vertices, bowties and fixed vertices as in the built search.
- *Evaluation.* Every candidate goes through `try_move` / `improves()`: the drift bound from `proposed_drift`, no
  triangle turned over, the degenerate triangles at the vertex not more than before, the intersecting pairs at its
  triangles not more than before. With both counts lower in the sum it is an **improving step**, as today. With both
  counts equal it is a **sideways step** (today: rejected; `improves()` already has this exit for edits that remove a
  contact).
- *A sideways step* is applied and written to the chain's journal (the vertex and what the step changed, 10.5).
- *The chain ends* with an improving step: it is **committed**, its steps count as moves. Or after `B` draws or `K`
  sideways steps, or when the effort limit is reached: it is **rolled back**, the journal undone the last entry
  first.
- *Rounds.* A round that committed a chain is followed by the passes and the built search, then another round. A
  round that committed nothing ends the repair, with the mesh as it was before that round.

So the result differs from today's only where a chain ends in a move that removes a defect. Results that are rounding
clean today do not reach the rule.

### 10.4 The probe

`SnapRoundingExperiment lattice` on what SafeRounding leaves today (the built search included), one vertex per step,
candidates drawn from the 26 x 8 set. New switches: `LATTICE_STRICT=1` keeps a step only if it adds no defect (the
defects at the moved vertex after it are a subset of those before: it accepts no step that the counts of 10.3 reject;
the count rule of 10.3 itself, both counts equal, was not run), `LATTICE_STALL=<B>` ends the search after B evaluations without a step that removes a defect and puts the
steps since then back. The probe draws defects at random over the whole mesh and has one budget for all of them: it
prototypes the acceptance rule and the rollback, not the order of 10.3. Tables: `trace/summarize.py plateau`.

**The 19 generated leftovers the rule reaches** (three seeds, 60 s, no run at the time limit;
`plateau_generated_2026-11-15.jsonl` holds all 25 that are not clean, `summarize.py plateau <file>
plateau_safe_2026-11-15.jsonl` keeps the 19 that are not rounding clean). The probe counts contacts as intersecting
pairs, so on the other 6 it searches what the rule never would; they are left out:

| rule | candidates | evaluations without an improvement before the rollback | clean of 19, per seed | evaluations of a clean end: median, largest | vertices moved: median, largest |
|---|---|---:|---|---:|---:|
| improving only (by the sum of the counts, drawn at random) | 26 x 8 | 20 000 | 0 / 0 / 0 | | |
| **plateau, no defect added** | 26 x 8 | 500 | **4 / 5 / 3** | 331.5, 895 | 10, 18 |
| **plateau, no defect added** | 26 x 8 | 2 000 | **6 / 6 / 6** | 411, 1 902 | 13.5, 18 |
| **plateau, no defect added** | 26 x 8 | 20 000 | **9 / 8 / 9** | 831, 10 371 | 14.5, 32 |
| plateau, by the sum of the counts (the rule of 6.3) | 26 x 8 | 2 000 | 8 / 9 / 9 | 345.5, 2 062 | 14, 44 |
| plateau, no defect added | random vectors | 20 000 | 9 / 8 / 10 | 760, 20 299 | 14, 33 |
| plateau, no defect added, up to 3 vertices per step | 26 x 8 | 20 000 | 11 / 11 / 10 | 663.5, 13 698 | 14, 33 |

- Today's rule gains nothing from more draws: 0 of 19. The plateau is what gains: 3-6 with a budget of 500 to
  2 000, 8-9 with 20 000. The 2 000 row is clean on the same 5 cases in all three seeds and on 7 in at least one.
- Two of the 19 (Cluster:40:8:2 and 40:8:3) hold contacts besides their rounding defects; the probe ends clean
  there only because it removes those as well, so for the rule the rows are upper bounds by up to two.
- The stricter acceptance (no defect added) costs 2-3 cases against the sum rule of 6.3 at the same budget; random
  vectors add at most one, several vertices per step one to three. So the candidate set of the built search and one
  vertex per step carry most of it, as on 2026-11-12.
- A clean end moves a median of 10-15 vertices, and 17 of the 18 clean ends at 2 000 moved a vertex to within 3 % of
  the drift that was left: the walk uses the room it has.
- A walk can turn a sliver over (`improves()` lets triangles below twice the displacement turn): on
  utm:FoldedCluster:36:4:2, one of the 6 cases left out, two runs (the sum rule, and three vertices per step) end
  with no defect and a new inside-out tiny component. None of the one-vertex rows with "no defect added" shows it.

**The real leftovers.**

*The cohort* (`plateau_cohort_2026-11-15.jsonl`, `summarize.py leftovers`; the probe with `LATTICE_TARGET=degenerate`,
three seeds). 838003 difference, 146 950 triangles, one degenerate triangle, 3 % of the bound left:

| rule | budget | clean, of 3 seeds | evaluations to clean | vertices moved |
|---|---:|---:|---|---:|
| improving only | 2 000 | 0 | | |
| plateau, no defect added | 500 | 0 | | |
| plateau, no defect added | 2 000 | 2 | 901, 979 | 9 |
| plateau, no defect added | 20 000 | 3 | 901, 979, 3 800 | 9 |

The whole result is clean then (`NotCleanReasons` empty). It takes a chain of 895 to 3 790 sideways steps over 9
vertices, the farthest of which ends at the edge of the room it had: the triangle is opened by moving its neighbourhood, a
step at a time. This is the one operation of 16 239 the rule can gain.

*The Thingi10K self-unions* (`plateau_thingi_2026-11-15.jsonl`, seed 1, 120 s; the 31 results under the limit; the
count is of degenerate triangles, no step may add a defect of any kind):

| rule | budget | results with no degenerate triangle left, of 31 | degenerate triangles, of 378 | clean |
|---|---:|---:|---:|---:|
| improving only (no defect added) | 2 000 | 9 | 125 left | 0 |
| plateau, no defect added | 500 | 21 | 88 left | 0 |
| plateau, no defect added | 2 000 | 21 | 39 left | 0 |
| plateau, no defect added | 20 000 | 25 | 26 left | 0 |

- No result becomes clean in any variant: what the rounding left is not what keeps these results from being clean.
- The numbers are upper bounds for a build. The probe moves every vertex freely; the repair moves a boundary vertex
  only along the boundary and no bowtie, and these are open results. That the improving-only row already clears 9
  results that the built search left is this difference (and the order and number of the draws), not the plateau.
- The three results at the limit (10 064, 5 565 and 569 015 degenerate triangles left) are outside any search that
  the limit bounds.

**Limits of the probe.** A random walk with one budget for the whole mesh, not the per-defect chains of 10.3. The
acceptance is by the set of defects where a build would compare counts. Its budget counts evaluations: a candidate
with a zero direction, outside the room or with a triangle turned over is dropped before it is evaluated, where 10.3
and the lemma count every draw. The review of this section counted 1.1 to 1.8 draws per evaluation on seven cases, so
a budget of 2 000 evaluations is about 2 200 to 3 600 draws. On results above 2 000 triangles and in the
strict variants the defects are counted at the moved vertices only. The Thingi10K runs have one seed. The probe does
not model `MaxPairTests`, fixed vertices, boundary vertices or bowties.

### 10.5 The lemma

**Claim.** With the rule of 10.3 (a) the repair ends, after at most B S N0 (N0 + 1) / 2 draws of the plateau search for
N0 defects at its start, each an evaluation under the existing limits; (b) at every moment, also inside a chain, the
bound of the theorem of SAFE_ROUNDING.md section 1 holds; (c) no step raises the number of degenerate triangles or of
intersecting pairs, so "no new self-intersection" holds in the sense it holds today, as a count that never grows; (d) a
round that commits nothing leaves the positions as they were.

**Proof, in the obligations of `tools/Z3Pilot/plateau_search.py`** (28 queries and one search over the runs, under a
second; every obligation UNSAT, with its assumptions SAT where it has any; `results/plateau_search_2026-11-15.json`):
- T (a): the measure (N, q, ch, dr), N the number of defects, q = 1 while the round has committed a chain, ch the
  chains left in the round, dr the draws left in the chain, falls lexicographically in each of the five transitions (a
  rejected draw, a sideways step, a commit, the end of a chain, a new round) and stays within fixed bounds. P2: the
  longest run of this system, by a search over every draw at which a chain can commit for N0 <= 6, has exactly
  B S N0 (N0 + 1) / 2 draws (every round commits one chain at its last draw). `K` is not needed for (a): a sideways
  step uses a draw. It bounds how far a chain wanders.
- A (c): the counts of the whole mesh are those before, minus the defects at the moved vertex before, plus those after;
  with both local counts equal both global counts are equal, with a lower sum and neither higher the global sum falls.
- R (d): undoing a journal of up to three steps the last first restores every position, also when one vertex moved
  more than once.
- D (b): a step is applied only if its bound was checked, so the largest bound never passes L.

Bridges, reviewed and not by Z3:
- the lexicographic order on bounded tuples of naturals is well-founded;
- the rollback of a journal longer than three steps, by induction (undoing the last entry gives the state before the
  last step);
- a chain that is cut by the effort limit ends as any chain that is not committed, rolled back;
- a defect with no triangle at the moved vertex is unchanged by the step (it is a function of its triangles' corners),
  and `improves()` counts all defects at the vertex's triangles;
- a sideways step is a displacement edit of the reference rule (SAFE_ROUNDING_D1_DRIFT.md: "no anchor and no `c`
  changes"), so the bound of a triangle is a function of the positions and of values no step of a chain changes: the
  state after a rollback has the bounds it had, and every state in between was checked. No new drift lemma;
- the passes and the built search never raise N (SAFE_ROUNDING.md section 9).

Nine mutants are SAT: a sideways step that counts as a move of the round (the rounds then repeat with the same N: the
loop of #755 runs "until a search accepts nothing"); a rejected draw that is not counted; a sideways step that refunds
the draws and is not capped; a new round after a round without a commit; acceptance by the sum of the two counts, for the sideways and
for the improving step (a degenerate triangle traded for a new intersection: the probe's rule of 6.3); the journal
undone the first entry first (a vertex that moved twice ends at its middle position); a journal of the new positions;
a sideways step without the bound check.

**Not covered by the lemma, and the part a build must get right:** (d) is proved for positions, and (c) for the two
counts only (a sliver may turn over, as with every edit: 10.4). `try_move` and `improves()` also write:
- the position map (`atPosition`), the dirty marks of the trees and `exactAtStart`, `version` and the cache of the
  last evaluation;
- `touched`, which decides whether a tiny closed component counts as edited and is removed
  (`remove_tiny_components`): a rolled-back step that stays in it could remove a component with no commit;
- `pairsStale`, `changed` and `pending`, the work lists of the passes;
- `MaxDrift` (a running maximum), `Moves`, and through rejected draws `SmallestRejectedDrift`;
- `evaluations` and `pairTests`, and with them `EvaluationLimitReached`. These are not restored by design (the search
  can be what reaches the limit, as with #755), which a result then reports.

A rollback must restore each of the others, or the design must show that it is a function of the positions. The test
for it: on a case where no chain commits, the mesh, the removed components, `MaxDrift`, `SmallestRejectedDrift` and
the move counts are the same bit for bit as with the rule switched off, and `EvaluationLimitReached` differs only
where the rule's own draws reached the limit.

### 10.6 Design requirements

- *Invariance (`Similarity.All()`).* The candidates are those of the built search: constant directions, sizes
  room / 2^j, so p scales exactly with a 2^k scale; the sequence is integer and seeded by ids. The 2^k scales
  (`Pow2Small`, `Pow2Large`) must give the same result bit for bit, the other frames the same verdict within the
  harness's tolerance; a test over `Similarity.All()` on a case where a chain commits, and one where none does.
  Under a rotation the candidates are other points, as in the built search.
- *Determinism.* No `System.Random`, no time, no dependence on the thread count: the search is serial (the edit loop is
  0.54 % of an operation, T1b), the sequence is a function of (triangle id, chain, round). Repeated runs and runs with
  1 and n threads for the parallel parts of the repair give the same hash.
- *Reuse.* A second `Apply` on the instance gives the same result (the journal and the sequence state are reset).
- *Limits.* Draws are evaluations: they count against `MaxPairTests`; the chains have their own draw budget (B S per
  defect) in place of `MaxEvaluationsPerDefect` (50, far below a chain), and the rule runs only with at most `D`
  defects left, so the cost is bounded by B S D (D + 1) / 2 draws whatever the mesh.
- *Success path.* Tests check the result (`NotCleanReason` empty, the bound through `check_all_bounds`), not only the
  counts.
- *Exact code.* `MeshRoundingRepair` calls the exact predicates and is not in the exact list; no new floating-point
  bound is introduced (every decision is `improves()`'s).

### 10.7 Gain, cost, risk, effort

| | |
|---|---|
| **Gain, closed inputs** | at most 1 operation of 16 239 (838003 difference), and only with chains of about 900 to 3 800 evaluations; no other cohort result has a defect left |
| **Gain, Thingi10K self-unions** | no clean result of 400: the 31 results are open or keep contacts of the exact result. 21-25 of the 31 lose every degenerate triangle (9 with today's rule in the same probe; an upper bound: the probe moves boundary vertices freely, the repair does not) |
| **Gain, generated clusters** | 3-6 of the 19 leftovers the rule reaches with 500 to 2 000 evaluations without an improvement, 8-9 with 20 000 (upper bounds by up to two, 10.4); today's rule with the same draws: 0 |
| **Cost at run time** | only on results that are not rounding clean today and under the limit. A draw is an evaluation, about 40-130 µs in the table of 4.4 (rounding time over evaluations, an overestimate: it includes the first search): a chain of 2 000 draws is 0.1-0.3 s per defect and round. The worst case with `D` = 8 is B S D (D + 1) / 2 = 72 000 draws for B = 2 000, S = 1, some seconds, which is why `D` is part of the rule. Rounding-clean results: no cost |
| **What changes in results** | a clean end moved a median of 10-15 vertices (up to 32 on the generated cases, 9 on 838003), and nearly every walk reaches the drift that is left: `MaxDrift` of an affected result rises towards the bound, which it never passes. The built search moves one vertex per defect and keeps the drift (SAFE_ROUNDING.md section 9) |
| **Risk** | (1) the rollback: state besides the positions (10.5), in the file with the most expensive mutation gate (8 rounding shards); a missed piece changes results silently, the bit-identity test of 10.5 is the guard. (2) It is a random walk: whether a case ends clean depends on the seed and the budget (838003: 2 of 3 seeds at 2 000, 3 of 3 at 20 000; generated: 5 cases clean in all three seeds, 7 in some, at 2 000), so a small change of the input can flip a result between clean and not clean. Deterministic, not stable. (3) A long chain can turn slivers over, which the counts do not see (10.4). (4) No guarantee is gained: the lemma is termination and the invariants, not success |
| **Effort** | build M (3-4 days: the sideways exit of `improves()`, journal and rollback, the sequence, the option, tests for invariance, reuse, determinism, rollback identity), tier L process: independent review, the 263-hash gate, the cohort run of 16 239 operations on the runner as for #755, the rounding mutation shards (their offsets move) |

### 10.8 Recommendation

**No-go before the release. The design stays on file, with its lemma, for the case that dense clusters become a
user's problem.**

**Owner 2026-11-15 (through the coordinator): no-go before the release, no three-day spike; the rule stays a design.** The spike described below is not run.

- *The gain on real results is one operation.* On closed inputs the rounding leaves 1 of 16 239 results with a defect
  (0.006 %), and the rule may gain exactly that one, at a budget where the outcome still depends on the seed. On the
  400 self-unions it gains no clean result. The larger gain (3-9 of 19) is on generated clusters of eight nearly
  coincident parts, built to defeat the repair.
- *The cost is in the wrong place for the gain.* A rollback inside `MeshRoundingRepair`, tier L, the cohort run and
  the rounding shards, for a rule whose success is a random walk: deterministic and bounded, but with no guarantee
  and not stable under small changes of the input.
- *A no-go does not touch the release.* The rule runs only on results that report a defect today; every
  rounding-clean result is the same bit for bit with or without it. Built after the release it turns a result that
  is reported as not clean into a clean one, or leaves it: no clean output changes. So nothing has to be decided
  before the release for the sake of stable outputs.
- *Problem 1 is not closed by it either way.* With or without the rule there is no guarantee; the status of section 6.1
  stands.

The shape a spike would have had, kept for a later decision: three days, behind an internal switch that is off; B = 2 000,
K = B, S = 1, D = 8; stop rules fixed in advance: the cohort with no result newly unclean and every rounding-clean
result identical, 838003 difference clean, at least 5 of the 19 generated leftovers clean, the rollback identity test
of 10.5 green, the rounding time of the cohort within 2 %. A miss of any of them ends it.

How others solve it: a cap on the steps and restarts (GSAT), a potential where the elements have a sign (untangling),
random perturbation of the input until the predicates are safe (controlled perturbation); nobody we found searches
representable positions under a displacement bound with a proof of success (10.2).

Reproduce:

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
T="dotnet benchmarks/SnapRoundingExperiment/bin/Release/net10.0/SnapRoundingExperiment.dll"
# a generated leftover (the cases: "clean": false in plateau_safe_2026-11-15.jsonl), seed 1
LATTICE_MAX_MOVED=1 LATTICE_GRID=8 LATTICE_STRICT=1 LATTICE_STALL=2000 $T lattice cluster:Cluster:30:8:1 60 1
# today's rule with the same draws: LATTICE_PLATEAU=0 LATTICE_LOCAL=1 in place of LATTICE_STRICT=1
# a real leftover: only the degenerate triangles are searched
# (the committed rows name these cases bc:rot/838003:Difference and file:1037029.stl, without the local directories)
LATTICE_TARGET=degenerate LATTICE_MAX_MOVED=1 LATTICE_GRID=8 LATTICE_STRICT=1 LATTICE_STALL=2000 $T lattice bc:cases/rot/838003:Difference 120 1
LATTICE_TARGET=degenerate LATTICE_MAX_MOVED=1 LATTICE_GRID=8 LATTICE_STRICT=1 LATTICE_STALL=2000 $T lattice file:meshes/1037029.stl 120 1
R=benchmarks/SnapRoundingExperiment/trace/results
python3 benchmarks/SnapRoundingExperiment/trace/summarize.py plateau $R/plateau_generated_2026-11-15.jsonl $R/plateau_safe_2026-11-15.jsonl
python3 benchmarks/SnapRoundingExperiment/trace/summarize.py leftovers benchmarks/SnapRoundingExperiment/trace/results/plateau_thingi_2026-11-15.jsonl
python3 tools/Z3Pilot/plateau_search.py
```

The 400 set is `benchmarks/MeshRepairBenchmark/fetch_data.py --no-large` (fetched at run time, not committed); the base
state of a result is `lattice <case> 0`.
