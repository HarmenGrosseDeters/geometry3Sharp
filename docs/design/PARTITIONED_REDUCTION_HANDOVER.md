# Partitioned reduction (Alpha Wrap 2.0 option A): hand-over for Reducer / Remesher 2.0

Status 2026-11-09: shipped in `MeshRobustRepair.SimplifyWithin` (#432, master dfc93fdb), internal. This note is the
starting point of the "Reducer / Remesher 2.0" milestone (owner 2026-11-08, `docs/roadmap/alpha-wrap-and-repair.md`):
the slab reduction and the #405 guard as options of the general `Reducer`, the slab partition for the `Remesher` with
`PreventSelfIntersections`, defaults re-evaluated. Code: `mesh_ops/PartitionedReduction.cs` (class doc has the full
argument), tests: `tests/geometry3Sharp.Tests/PartitionedReductionTests.cs`.

## How it works

1. **Partition.** `Slabs` (8, a constant) slabs along the longest box axis, cut at the quantiles of the vertex
   coordinates, each shrunk by half a gap (`GapEdgeLengths` = 1 mean edge length) on both sides, so the closed slabs
   are disjoint. The slabs are unbounded in the other two axes.
2. **Ownership.** A triangle with all three corners in slab i belongs to region i; every other triangle is frozen.
3. **Regions in parallel.** Each region is reduced in its own `DMesh3` copy: its triangles plus every frozen triangle
   whose closed box meets the slab, with all edges and corners of the frozen ones fully constrained
   (`MeshConstraints`). A `SeededReducer` (quadrics, origin and fold-over references from the whole mesh) runs
   `ReduceToMaxQuadricError`; a collapse of two free vertices is refused if its new position leaves the slab
   (`LimitToBox`); a collapse onto a pinned vertex keeps it where it is.
4. **Stitch** in region order: the frozen triangles, then each region's own triangles; quadrics and fold-over
   references are carried.
5. Two rounds, the second with the cuts shifted by half a slab (K + 1 slabs, covering the first round's gaps), then
   one serial `SeededReducer` pass on the whole mesh with the carried data.

## Invariants (what the tests pin down)

- **Confinement**: every triangle a region can create lies in its slab; frozen triangles do not change; so the exact
  self-intersection check of each copy sees every triangle that a new triangle could meet. This needs the frozen
  triangles *by box overlap*, not only those sharing a vertex (`FrozenObstacle_WithoutSharedVertex_IsSeen` kills
  that mutant), and `overlaps` with `<=` (touching boxes count).
- **Determinism**: the partition depends only on the mesh and the constant slab count, never on the core count; no
  shared mutable state across regions; stitch in region order. Same result for 1/2/4/8 threads and repeated runs.
- **Seeding**: one slab and no round equals one serial `Reducer` run bit for bit (`OneSlabNoRound_SameAsSerial`).
- **Guarantees** stay with the caller: `SimplifyWithin` keeps the result only if clean and certified (Hausdorff).

## Pitfalls found on the way

- **Pinned-vertex quadrics** (the first prototype): a free vertex collapsed onto a pinned one adds its quadric to it;
  the stitch dropped that, later collapses were costed too low, and 26 of 100 Thingi10K wraps failed the
  certification. Fix: a pinned vertex inside the region's slab takes that region's quadric and fold-over reference
  (at most one slab contains it). Test: `PinnedVertices_CarryTheirQuadric` (runs on the pre-#408 wraps with the loose
  0.05 guard; under the 0.3 guard too few collapses reach a pinned vertex on small wraps to show the loss).
- **Gap lost in rounding**: with coordinates huge relative to the edge length, `cut ± gap/2` rounds back to `cut`,
  adjacent closed slabs share a plane, and a vertex on it would be free in two regions (a crack). Guard: the shrunk
  slabs must be strictly disjoint, else one serial run (`SerialFallbacks`; tested with `GapEdgeLengths = 0`).
- **Cancel before setup**: a region cancelled before its Reducer set up has no quadrics; the seeds are used.
- Contract differences to one `Reducer` run: `CancelF` is called from several threads; region exceptions surface as
  `AggregateException`; regions run with `PreserveBoundaryShape = false`; the result has no normals / colours / UVs /
  groups. A general API must decide each of these.

## What changes for a general Reducer

- Expose as an option (eg `Reducer.Partitioned` or a `PartitionedReduction` public class) only with the same
  determinism statement; the slab count must stay a setting, not `ProcessorCount`.
- Boundaries: open meshes need `PreserveBoundaryShape` inside the regions (boundary edges in a region are its own;
  frozen boundary edges are already fixed).
- Targets other than `ReduceToMaxQuadricError`: a triangle-count target must be split across regions (eg by owned
  triangle share) and then met by the serial pass; the result then depends on that split (still deterministic).
- External constraints of the caller must be mapped into each copy and merged with the frozen ones.
- Attributes (normals, UVs, groups) must be carried through the copies and the stitch.
- The #405 guard (`MinCollapseTriangleQuality` 0.3, `CollapseQualityAgainstWorst`) is independent of the partition;
  for general meshes its default needs its own measurement (on wraps it removed every cap > 170°).

## What changes for the Remesher

`Remesher` already has `PreventSelfIntersections` (collapse, flip and smoothing checks) and parallel smoothing. A slab
partition needs, in addition to the above:
- **Splits** stay in the slab automatically (midpoints of owned edges); **flips** too (same four vertices).
- **Smoothing / projection moves** must be limited to the slab (as `LimitToBox` for collapses), or vertices near the
  slab boundary frozen for the moves.
- The edge-length targets must not depend on the region (they are global already).
- The rounds: a remesh pass is iterative (split / collapse / flip / smooth); the partition would wrap each pass, with
  the shifted second partition covering the gaps, and the serial pass at the end.

## Measurements to start from

- Serial reducer profile (#422, 5 raw wraps 26k-327k triangles): 56-70 µs per collapse; the exact self-intersection
  check 56-63 % of the reduction, the bowtie check 4-9 %, the core 35-38 %, GC ~7 %. #422's bit-identical levers gave
  0.906 (T3, 100 meshes).
- Option A on the final base (T8, 100 T1 meshes, local 4 cores, warm, alone, master 164b273c vs + #432): reduction
  paired median 0.543 (p10 0.405, p90 0.884), pipeline 0.859; speed budget met 43 → 78 / 100; triangles −3 %
  (median), p01 11.66° → 11.49°, band and Hausdorff unchanged; 100 / 100 guarantees.
- Final 800 run (master dfc93fdb, library defaults, warm, one process at a time, local 4 cores): guarantees 800 / 800,
  final 0 triangles < 0.5° and 0 > 170°, p01 median 11.34° (min 10.62°), fidelity not worse than before 2.0; 611 / 800
  within the speed budget (time / budget median 0.841, p90 1.123, max 1.62). In the 189 over the budget the reduction
  is 17 % of the time, the wrap itself about 60 % (intersection 14 %, insert 13 %, touch 12 %, the pinch passes 18 %),
  the certification 7 %: further speed is mostly outside the reducer.
- Raw rows: T1-T3 and Q1 on `runs/alpha-wrap-2`; T4-T8 were local (not committed).
