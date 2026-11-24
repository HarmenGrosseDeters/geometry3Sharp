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
contraction), `sliver_cleanup_neutral_contractions` (the stage's contractions accepted only because they keep the counts, section 5), and as before `target_edge_parts_contracted` and `curve_edges_contracted`. The drift used is `Rounding.MaxDrift`
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

Since #1410 (`ExactPoint3.ToDouble` rounds to nearest instead of truncating) that cylinder instance is clean, and the same limit showed on the rotated box translated
far from the origin (632117.25, 5712043.5, 113.75) and shifted by +1 ulp along z, cut into its shifted copy: one sliver of area 1.5e-21 left, its shortest
edge (2.6e-11) refused by `CollapseEdge` (`Failed_InvalidNeighbourhood`) in both directions. The numbers of the paragraph above (39 of 40) were measured before that change.

**What #1410 exposed (measured for #1489, `benchmarks/ImprintSliverSweep`).** Unclean runs out of 48 (shifts of 1 to 12 ulps, both signs, both cut
directions), the frames of the harness: at the origin, translated by (632117.25, 5712043.5, 113.75), the translation negated, mixed signs, only z translated.

| shape, axis | origin | translated | negated | mixed | z only |
|---|---:|---:|---:|---:|---:|
| truncating `ToDouble` (before #1410): box z | 0 | 0 | 24 | 0 | 0 |
| cylinder z | 1 | 0 | 0 | 0 | 0 |
| cylinder x | 0 | 0 | 0 | 0 | 0 |
| nearest (#1410, before #1489): box z | 0 | 10 | 10 | 10 | 2 |
| cylinder z | 0 | 48 | 48 | 48 | 2 |
| cylinder x | 0 | 2 | 2 | 2 | 24 |
| **nearest, neutral contractions (this change): box z** | 0 | 0 | 0 | 0 | 0 |
| **cylinder z** | 0 | 0 | 0 | 0 | 0 |
| **cylinder x** | 0 | 2 | 2 | 2 | 0 |

**Diagnosis: one mechanism, a plateau.** In every left-over cluster the sliver cannot be removed by a single edit that removes a defect, but by two, the
first of which keeps the counts (`SmallestRejectedDrift` is infinite in all 48 cylinder runs: the drift bound refuses nothing). The box (10 runs, one sliver
each): a split point of a target edge, 2.6e-11 from the target corner T, with a valence-3 curve vertex inside the thin triangle (T, split point, apex) that the
rounding put 1.2e-10 off the needle's plane. The contraction of the split point into T is refused (the three-cycle T, split point, apex encloses the valence-3
vertex: link condition, shown as "new intersection" by the exact test that runs first); the contraction of the valence-3 vertex only turns the sliver into the
needle triangle (T, split point, apex), whose area (1.1e-11) is itself below the degenerate area (1.3e-11): one defect before and after, refused as "no gain".
The cylinder: all slivers sit at the two cap centres (target vertices of valence 12); 4 to 8 curve vertices within 1e-14 to 4e-9 of the centre are left, and every
contraction of one into the centre is "no gain" or "new intersection" until a neighbour has gone first. Refusals over the slivers of the 48 cylinder-z runs
(288 slivers, every edit the passes try, after the stage): a contraction that makes a target vertex vanish: always refused (fixed); flips and zips at an edge or
triangle that carries a lock: refused (locked); contractions of curve vertices: "no gain" and "new intersection"; the 26 x 8 moves of the position search: "no
gain" or "new intersection", none accepted. The link condition is checked last, so it is reached only by edits that pass the exact test.

**Why truncation avoided it** (partly verified). With truncation the rounding left fewer intersecting pairs to repair (cylinder z, translated: 97 against 141 on
average per run, box 6 against 11), and in the translated box runs the passes were clean without the stage (0 of 48 runs entered it, against 10). The
frame dependence (negated box 24, mixed signs 0) is not a per-sign rule; the side of the target's edges on which each curve vertex lands was not traced further.

**The change (#1489).** In the final stage only, a contraction that keeps the counts is accepted as well (`MeshRoundingRepair.AcceptNeutralContractionsInStage`,
set by `ExactMeshImprint`; `ExactMeshBoolean` does not set it). Invariants: (1) every other rule of the edit is unchanged: the drift bound, no triangle turned over,
no more degenerate triangles and no more intersecting pairs than before (each tested separately, so "keeps the counts" means equal counts), locks, link condition;
(2) termination: a contraction removes a vertex and every other edit still has to remove a defect, so (defects, vertices) falls lexicographically with every
accepted edit; (3) results that are clean without the stage are bit for bit the same (the stage does not run on them); (4) deterministic, no randomness, no rollback:
a neutral contraction is kept and leaves the mesh no worse by the counts, within the drift bound. A result that was not clean may change (more of them become
clean; a neutral contraction that no gain follows stays). The tests of the rounding, imprint, Boolean, Metamorphic, invariance and exact-conversion areas (6863) run with it: no pinned count or hash moved; the two pins of the known limit became the property.

*How this differs from the plateau variant that the owner declined (SNAP_ROUNDING_3D.md section 10, no-go 2026-11-15).* That rule took sideways *position
moves* of one vertex by random draws, with a journal and a rollback, as a general search after the passes, for the Boolean's leftovers (a gain on 1 of 16 239 cohort
operations, 3 to 9 of 19 generated clusters, success depending on seed and budget). This one takes no position moves, draws nothing, needs no rollback and
runs only in the imprint's final stage, only for contractions, only where the result is not clean without it; its gain is deterministic: 202 of the 208 unclean runs of the sweep above
(all cells with a translation) become clean, and no cell gets worse. The costs of the no-go (a rollback in the repair, a random walk, the cohort run) do not apply; its termination
requirement is the lexicographic argument above.

**The evaluation limit of the stage (owner decision, 2026-11-24).** Cylinder x in the translated frames at +-6 ulps (2 runs of 48 per frame) reached the limit (10000 plus
50 per initial defect) with 23 slivers: the position search proposes 26 x 8 moves per corner, and the neutral contractions needed only 25 more evaluations (15 801 used at the
limit, 15 826 in all). From the start of the stage the limit is 10000 plus 400 per initial defect (`MeshRoundingRepair.StageMaxEvaluationsPerDefect`, set by the imprint to
`ExactMeshImprint.StageEvaluationsPerDefect`, internal, 400). Only the stage gets it: a run that is clean without the stage, a run that reaches the limit before the
stage (the stage does not start), the Boolean and every other caller keep the limit of 50 (pinned: `MaxEvaluations` of each). All cells of the sweep are 0 with it.
Cost: the limit is a ceiling, not a spend: runs that end by themselves use the same evaluations (measured: three runs left unclean on purpose, the stage without neutral
contractions, use 865, 8 808 and 14 984 evaluations at either limit, the same time within noise). The worst case is a stage that spends the whole budget
and stays unclean: its evaluations can grow from 10000 + 50 D to 10000 + 400 D (3.5 times for D = 116 initial defects, at most 8 times for large D), at about 6 microseconds
per evaluation (15 801 evaluations in a 96 ms run), i.e. 0.3 s for D = 116 in the worst case; I could not construct a case that does spend it (every unclean
case ended by itself: nothing left to propose). The pair-test budget (`MaxPairTests`, 5M, unchanged) bounds the work independently.
The cases between 1 and 12 ulps are a sample, not a proof; the shifts above 12 ulps and other shapes were not swept.

The test inputs avoid `Math.Sin` / `Cos` (they differ in the last bit between net48 and net10, `DETERMINISM.md` C1): the rotated
box is given by its corners as literals, the cylinder is snapped to a grid of 2^-24.

Procedural 1-ulp and few-ulp shifted pairs (rotated box, capped cylinder): clean per `NotCleanReason`, closed, every `CurveEdges`
entry an edge of `Result`, target vertices bit for bit; fail with `SliverCleanup = false` (the old repair). Invariance over
`Similarity.All()`, reuse, repeated runs; the stage does not run on results clean without it (bit for bit equal with the switch off). Corpus
numbers before and after (`thingi-contact.yml`, the classes of run 44017929170): in the PR.
