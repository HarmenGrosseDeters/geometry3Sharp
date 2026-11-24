# Imprint sliver cleanup at near contact (#1034)

Status: design note and implementation, 2026-11-17. Issue #1034; evidence: `docs/research/IMPRINT_CORPUS_2026-11.md` (run 44017929170:
`exact_imprint` clean on 18 of 200 operations of `ulp-shift` and 8 of 200 of `ulp-shift-few`, every other one closed, carrying
the curve, `NotCleanReason` "degenerate triangles").

## 1. Diagnosis

A cutter one to five ulps from the target (the target shifted by a few ulps of its largest coordinate) meets the target near
every edge whose dihedral angle lets the shifted surfaces cross. The exact curve then runs within about 1e-16 to 1e-7 (relative
to the diagonal) of target edges and target vertices. The repair (`MeshRoundingRepair`, with the imprint's locks,
`EXACT_IMPRINT.md` 4.2) contracts most of the slivers this makes. What it leaves (procedural probe: a rotated box shifted along z
and a capped cylinder, 1 to 5 ulps, both directions, 24 of 96 operations not clean) are slivers with a corner that is a curve
vertex **on a split target edge**, 1e-16 to 3e-11 from a target vertex (or from another vertex of the same piece). The natural
edit, contracting that curve vertex into the target vertex, is refused by one rule: a vertex with a locked part of a split target
edge may vanish only if the kept vertex has the same lock (`try_collapse`, "a rule-locked edge at a moves with the contraction to b").
The parts of split edges are locked by a fixed rule (`LockedEdgeF` over the set of parts made by the assembly), so the lock
cannot follow the contraction: the edge the contraction leaves behind (from the kept vertex to the next part's end) is not in the
fixed set. The curve edges have no such problem: they are a mutable set (`LockedEdges`) whose locks move with a contraction.

## 2. The change

The parts of split target edges become a mutable lock set (`MeshRoundingRepair.RuleLockedEdges`, internal) with the same
meaning as before; `LockedEdgeF` keeps the target's own edges (both ends target vertices). The repair runs as today. **Only if
its passes and position search leave a defect**, a final stage runs the passes and the search once more in which the locks
of `RuleLockedEdges` follow a contraction as the curve locks do: the lock of an edge at the vanishing vertex moves to the kept
vertex, a contracted part is gone (counted in `Counts["target_edge_parts_contracted"]`, as today). Every edit of the stage is
accepted by the same exact rule as every other edit (no new intersection, no new degenerate triangle, one defect fewer, the
drift bound of the whole repair).

**What may move:** new vertices (curve vertices, the new vertices of split edges), by contractions within the bound, as today.
**What stays exact:** the target's vertices (`FixedHard`: never moved, never removed), the target's own edges between two target
vertices (`LockedEdgeF`), the untouched triangles, the topology (closed and manifold stay so; the link condition of every
contraction), the boundary loops (a boundary vertex vanishes only along a boundary edge). **What changes:** a part of a split
target edge may now be contracted at a vertex that has another part, and the remaining part then ends at the kept vertex: a split
target edge is a chain of parts from one of its target vertices to the other, as before, and its parts lie within the drift bound
of the edge (a contraction moves them by at most its length). Where the kept vertex is not on the edge (the cylinder cases: a
target vertex 1e-16 from a curve vertex on a neighbouring edge), the chain passes through it, as a snap-rounded segment is routed
through a hot pixel it passes (Hobby 1999).

**Why the curve stays carried:** the curve edges are `LockedEdges`, untouched by the change: a contraction of another edge moves
their locks to the kept vertex, so the curve passes through it (moved by at most the contracted length); a curve edge itself is
contracted only as today (`AllowCurveContraction`, counted in `curve_edges_contracted`). `CurveEdges` is read from the lock set
after the repair, so every listed curve edge is an edge of `Result` (G5 of `EXACT_IMPRINT.md`, unchanged).

**No output change for clean results:** the stage runs only where the result is not clean without it (the condition is the
defect lists after the search, which are what `RemainingDegenerate` and `RemainingIntersections` count, `RemoveTinyComponents`
being off with `FixedHard`). Before the stage, the new set gives the same answers as the old rule (the parts of split edges,
asked by the same contractions, flips, zips and vertex removals), so every result that is clean today is bit for bit the same.
The internal switch `ExactMeshImprint.SliverCleanup` (default on) gives the repair without the stage, for the tests;
`LegacyPartLocks` gives the locks of before the change (one `LockedEdgeF` over the fixed set), and a test checks that the repair without
the stage equals it bit for bit. A target with defects of its own near the cut (a degenerate triangle or a self-intersection that a
piece touches) leaves a defect in every run, so the stage runs there each time: more passes from the same evaluation budget, and a
result that was not clean may change (it stays within the bound and the accept rule).

Counts (`ExactMeshImprint.Counts`): `sliver_cleanup_stage` (1 if the stage ran), `split_edge_reroutes_through_target_vertex` and
`split_edge_reroutes_through_new_vertex` (the stage's contractions that moved a part's lock to a kept vertex off its edge, per
contraction), and as before `target_edge_parts_contracted` and `curve_edges_contracted`. The drift used is `Rounding.MaxDrift`
against `Rounding.MaxLength`.
`ExactMeshBoolean` does not set `RuleLockedEdges`: its outputs do not change. No exact file, predicate or error bound changes.

## 3. Prior art (papers only)

- Snap rounding routes every segment that passes through a hot pixel through the pixel's centre, which bounds how far a segment
  moves and keeps the arrangement's topology: J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13,
  1999; L. Guibas, D. Marimont, *Rounding arrangements dynamically*, IJCGA 8, 1998; D. Halperin, E. Packer, *Iterated snap
  rounding*, CGTA 23, 2002 (a vertex near an edge splits it; the drift accumulates: E. Packer, *Iterated snap rounding with bounded
  drift*, CGTA 40, 2008). The change is the 3D analogue for the parts of the target's edges, with the bounded-drift rule of
  `MeshRoundingRepair` instead of a grid.
- Edge contraction that keeps feature edges as chains (a vertex on a feature edge contracts only along it, or the feature
  label moves to the kept vertex): H. Hoppe, *Progressive meshes*, SIGGRAPH 1996 (sharp edges kept through edge collapses)
  [from memory]; the link condition: T. K. Dey, H. Edelsbrunner, S. Guha, D. V. Nekhayev, *Topology preserving edge
  contraction*, 1999.
- The accept rule after rounding: V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, CAD 108, 2019
  (as in `SAFE_ROUNDING.md`).

Searched first in the library: `MeshRoundingRepair` (locks, `FixedHard`, the position search), `SafeRounding`, the exact
predicates, `ExactMeshImprint`'s assembly. No new predicate is needed; the fix is in the lock bookkeeping.

## 4. Considered and not taken

- A contraction that keeps the lock only along the split edge itself (both ends on it): clean on 85 of 96 probe operations
  instead of 96 (the cylinder's curve vertices 1e-16 from a target vertex that is not on their edge).
- A second `MeshRoundingRepair.Apply` after the first: its drift bound would restart from the repaired mesh, so the total
  displacement could exceed `MaxDisplacement`; the stage inside one `Apply` keeps the one bound.
- Snapping the curve onto the target's edges before the rounding (an exact pre-pass): new exact code and a new bound for a case
  the rounding already handles once the lock can follow.

## 5. Tests, numbers and the known limit

**Known limit.** The stage removes slivers only where an edit the repair already has can remove them once the locks follow.
On the capped cylinder snapped to a grid of 2^-24 (procedural, libm-free), 39 of the 40 operations that are not clean without the
stage become clean; the one left (shifted by -1 ulp along z, the copy cut by the original) has one sliver whose short edge
(1.4e-11, a curve edge and a part of a split edge) cannot be contracted: `DMesh3.CollapseEdge` refuses it (the link condition),
and the other edits at the sliver add a degenerate triangle or an intersection or improve nothing. The result stays closed, carries
the curve and is reported not clean, as before (test `KnownLimit_ALinkConditionLeavesOneSliver_...`). Removing such a sliver needs
an edit the repair does not have (eg a flip of the long edge, which is a locked part of a split target edge).

Since #1410 (`ExactPoint3.ToDouble` rounds to nearest instead of truncating) that cylinder instance is clean, and the test pins the same limit on
the rotated box translated far from the origin (632117.25, 5712043.5, 113.75) and shifted by +1 ulp along z, cut into its shifted copy: one sliver of area
1.5e-21 is left, its shortest edge (2.6e-11) is refused by `CollapseEdge` (`Failed_InvalidNeighbourhood`) in both directions. The numbers of the paragraph above
(39 of 40) were measured before that change.

The stage is fragile on ulp-shifted near-duplicates in any frame, and the conversion moves where; it does not introduce the fragility. Measured as
unclean runs out of 48 (shifts of 1 to 12 ulps, both cut directions), master f11323390 against the #1410 branch. At the origin, all shapes and axes: master 1
(the cylinder along z, the limit above), branch 0. Far from the origin with positive coordinates: box along z master 0, branch 10; cylinder along z master 0,
branch 48 (4 to 8 slivers each); cylinder along x master 0, branch 2 (6 ulps: the evaluation limit, 23 slivers). The same frame with the translation negated:
box along z master 24, branch 10; cylinder master 0, branch as with positive coordinates. Mixed signs: as positive coordinates on the branch (the new rounding is
sign-symmetric). Only z translated: cylinder along x master 0, branch 24. Reading: truncation is a directed rounding (down for positive, up for negative
coordinates), and it kept near-duplicate copies on one consistent side only for one sign of the coordinates, so the weakness existed before and depended on the
frame; the rounding to nearest moves it around for the box (10 of 48 in every frame) and is a real shift for the cylinder. Only callers of `ExactMeshImprint`
are affected (the follow stage is set only by the imprint; `ExactMeshBoolean` and `SolidBooleanPipeline` do not use it): 162 near-coincident Boolean runs per
library, far from the origin and at it, had no failed `Compute` on either side, and not `RoundingClean` far from the origin was 3 on master, 0 on the branch.
No test pins the cylinder cases yet; they belong to an issue for the stage, not to the conversion.

The test inputs avoid `Math.Sin` / `Cos` (they differ in the last bit between net48 and net10, `DETERMINISM.md` C1): the rotated
box is given by its corners as literals, the cylinder is snapped to a grid of 2^-24.

Procedural 1-ulp and few-ulp shifted pairs (rotated box, capped cylinder): clean per `NotCleanReason`, closed, every `CurveEdges`
entry an edge of `Result`, target vertices bit for bit; fail with `SliverCleanup = false` (the old repair). Invariance over
`Similarity.All()`, reuse, repeated runs; the stage does not run on results clean without it (bit for bit equal with the switch off). Corpus
numbers before and after (`thingi-contact.yml`, the classes of run 44017929170): in the PR.
