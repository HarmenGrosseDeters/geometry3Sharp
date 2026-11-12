# Classification by propagation (Exact Boolean 2.0): phase-1 hand-over

Superseded by [LEVY_CLASSIFICATION.md](LEVY_CLASSIFICATION.md) (phase 1 done 2026-11-08).

Status: superseded; phase 1 (#428) and phase 2 (#501) are done, see [LEVY_CLASSIFICATION.md](LEVY_CLASSIFICATION.md).
Status 2026-11-08: paused by the owner after reading only; no runs, no profile, no library code. This note saves the
next session the reading.

## What was read

CLAUDE.md, AGENTS.md, docs/AGENT_WORKFLOW.md, docs/REVIEW_CHECKLIST.md (first part), docs/roadmap/exact-boolean.md,
docs/perf/EXACT_BOOLEAN_PERFORMANCE.md sections 0-1, `ExactMeshBoolean.Compute` / `classify` and the functions it
calls, the body of draft #385.

## How classification works today (master cf19912)

`ExactMeshBoolean.classify`:
1. `coplanar_stack` per output piece (parallel): the input triangles that contain the piece's exact centroid
   (`centroid` adds a new exact point per piece), and whether the piece is its stack's representative.
2. Patches: union-find over pieces of one mesh joined across edges that are not `arr.ConstraintEdges` (intersection
   curves, including self-intersection curves), only where exactly two pieces of that mesh meet and their stacks are
   equal. So every self-intersection curve also cuts patches: self-intersecting inputs have many more patches.
3. Per patch (parallel, first representative piece): `patch_winding` evaluates, for each candidate mesh (closed meshes
   whose box contains the point via `closedTree`, plus all open meshes), either
   - `front_winding`: an exact ray from the centroid to a far point outside the mesh box (random direction, up to 200
     retries on degeneracy), `QuerySegment` over the mesh's whole box tree with filtered plane sides and
     `orient3d` filters, exact fallback; or
   - `front_generalized_winding` (open meshes): `DMeshAABBTree3.FastWindingNumber` at the double centroid, minus the
     stack's own triangles, thresholded at 1/2.
4. Per piece: front winding from the patch, back winding by the stack's signed jumps, `inside()` on both, keep/flip.

Cost model: one long ray per (patch, candidate mesh). A ray crosses the whole mesh box, so its cost grows with the
number of triangles near the ray (about n^(1/2)..n^(2/3) box visits), and patch count grows with the number of
intersection curves. Counters already reported: `Patches`, `Counts["winding_plane_sides"]`,
`Counts["winding_plane_sides_exact"]`, phase `classification` in `PhaseMilliseconds`.

## What propagation would replace

Across a constraint edge, the pieces around the edge in radial (angular) order bound wedges; the winding vector of a
wedge is the same on both pieces facing it, and crossing a piece of mesh m changes w_m by +-1 (by orientation and
stack). A radial sort per constraint edge (exact: orientation of the pieces' third vertices about the edge line, the
arrangement's indirect points) plus a BFS over patches would need one exact ray per connected component of the patch
graph (per mesh pair cluster), instead of one per patch and candidate mesh. Open meshes: the generalized winding number
is not integer-consistent across patches, so propagation there is the heuristic already tried and not merged
(branch `claude/exact-boolean-gwn-propagation`, no clear gain; roadmap Phase 2); for open inputs the gain must come
from fewer FastWindingNumber calls only where the jump is exact (closed meshes in the candidate list).

Sources to read next: Lévy 2024 (Geogram, BSD-3, `mesh_surface_intersection` radial sort and
`classify_components`-style propagation; porting allowed, list in docs/SOURCES.md); Cherchi et al. 2022 (paper);
Zhou et al. 2016 (mesh arrangements, winding propagation across cells, paper); EMBER (paper only, its idea 4 in the
roadmap); Manifold (Apache-2.0).

## Data and next steps (not started)

- Per-operation phase times: the #385 final JSONL is external (owner machine); the branch holds only the interim
  snapshot `benchmarks/Thingi10KFull/snapshots/20261104-interim/results/boolean.jsonl` and
  `analysis/20261104-interim/slowest_operations.csv` (1432 models). Check whether `phases_ms` there contains
  `classification`, then select slow ops (> 5 s) of the self-intersecting, unknown and open cohorts from it, or ask the
  owner/coordinator for the final JSONL's slow-op list.
- Upper bound: classification share on those ops, then the share of it spent in `patch_winding` rays (counter
  `winding_plane_sides` against patch count) versus `coplanar_stack` and union-find. Only the ray part is replaceable.
- Phase-1 deliverables per the brief: profile, upper bound, design note in docs/design, report to the coordinator.
