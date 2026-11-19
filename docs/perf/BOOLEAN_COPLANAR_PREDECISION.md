# Coplanar pair pre-decision (`ExactMeshArrangement.intersect_coplanar`, 2026-11-19)

Owner task of 2026-11-19 (step 3 of the ABC chains, `R op A`, where `R` already holds the surface of `A`). The diagnosis and the
upper bounds are on the data branch `runs/boolean-chains` (`docs/perf/BOOLEAN_CHAINS_PROBE_2026-11.md`, not merged).

## The change

In step 3 most pairs of the pair stage are coplanar pairs of triangles that share 1, 2 or 3 corners (the same surface twice). Before, each was
clipped six times (`clip_into`) and then added as a coplanar partner. Now `intersect_coplanar` first asks `meets_only_at_shared`, which decides
from the exact in-plane `Orient` of the shared corner ids (input points are welded, equal id = equal position) whether the two triangles meet
only in the shared corners or the shared edge:

| shared corners | meet only there iff |
|---|---|
| 3 | always |
| 2 | the two third corners lie strictly on opposite sides of the shared edge |
| 1 | the first ray of neither triangle's wedge at the apex lies in the closed wedge of the other (wedges oriented counter-clockwise) |
| 0 | never decided (old path) |

A decided pair does what the old path did for it, except that it adds no `Points` and no `Segments`: `IntersectingPairs` is incremented and
both triangles get `AddCoplanar`. The six clips would only have added corners of the triangle they go to, or its boundary edge between two corners.
Everything else (every undecided pair, every case on shared rays) takes the old path.

No new error bound, no new arithmetic: only `Orient` on input ids (exact, existing). Bit-identical results (hash equal in every test and every run below).

`ConstraintEdges` is the one thing the skip changes: a decided pair no longer records its shared edge as a constraint. Consumers:
`ExactMeshBoolean.join_pieces` (a missing constraint could only let two same-mesh pieces with equal stack be joined across the edge; the independent
review found no input where this happens: any non-coplanar cross-mesh pair around the edge still records it, and if all faces are coplanar the
neighbour overlaps and is clipped) and `ExactMeshImprint`, which reads contact from the clip segments and so keeps the old path
(`PreDecideCoplanar = false`; `ExactImprintAssemblyTests` guard it).

`ExactMeshArrangement.PreDecideCoplanar` and `ExactMeshBoolean.ArrangementPreDecideCoplanar` (both `internal`, default on) switch it off for the A/B and the tests.

## Measurements

Order-randomized A/B (`BooleanComparison chain-ab`, method `ours_ab`: per chain step, on and off alternate in random order, GC before each,
median of the repetitions, result hashes compared). GitHub runner (4 cores), ABC chunk 0000, 12 chains per variant (ucd, dui, iud), run 44521833434.
Sums over the chains of the median times:

| step | chains | on s | off s | change (sum) | median chain change | hashes differ |
|---|---|---|---|---|---|---|
| 1 | 36 | 7.75 | 7.08 | +9.3 % | +9.5 % | 0 |
| 2 | 35 | 35.03 | 34.86 | +0.5 % | +0.4 % | 0 |
| 3 | 35 | 71.29 | 84.27 | **-15.4 %** | **-22.3 %** | 0 |
| 1 to 3 | | 114.07 | 126.21 | **-9.6 %** | | |

Step 3 gains as the probe predicted (-17.2 % time-weighted in the probe). **Step 1 is 9 % slower, in an order-randomized comparison, in 36 chains**
(0.7 s of 7.1 s in sum, so 0.7 s of the 13 s gained): not noise. It is the cost of `meets_only_at_shared` on coplanar pairs that sharing corners
but are not decided (step 1 decides no pair); not diagnosed beyond that (no per-phase profile of step 1 was run). A follow-up could make the 1-shared-corner
test cheaper or skip it where the pair stage sees few shared ids; that is left out of this PR (no gate files, one change).

Procedural chains (`chain-ab-proc mixed`, seeds 1-3, 6 steps, 5 reps, local): hashes equal in all 18 steps, step times within noise (no coplanar chain
step shares enough corners for a gain, and none loses visibly).

## Tests (`ExactCoplanarPreDecisionTests`, 18, 3 s)

Property test against the old clips on random coplanar pairs sharing 1, 2 and 3 corners (grid points, collinear rays, 4 scale/mode cases x 700 pairs);
hand cases for each branch including shared first and last rays; a decided pair still counts and is a coplanar partner; `R op A` on flat boxes:
hashes and counts equal on/off, the flag reaches the arrangement and the decision fires (`ArrangementPreDecidedPairs`), `Similarity.All()`
invariance, power-of-two scales bit for bit, reuse (also with the flag toggled between two `Compute()` calls), parallel equals serial and repeated runs.
Mutants killed: shared==3 false; `<0` flipped; either wedge check dropped; `>=` to `>` in `in_wedge` (both); wedge normalisations flipped; shared==0 true;
the Boolean-to-arrangement forwarding removed.
