# Findings from the issue-tracker cases (docs/QA.md 2.11)

Cases of the `*IssueCasesTests` classes that we fail, or that showed a limitation worth recording. Each failing case
is pinned in its class's strict `KnownFailures` list with the finding id. Nothing here is fixed yet: each entry has
the analysis so far and fix ideas with their cost, for whoever takes it up.

## Mesh repair

### R1: crossing components taken for nested ones

- Test: `MeshRepairIssueCasesTests.ThinFold_NearlyParallelOverlap_Resolved` (3 and 10 degrees), pinned.
- Issue: https://github.com/MeshInspector/MeshLib/issues/5567 (thin folds between faces that point almost the same way).
- Input: two outward boxes [-2,2] x [-2,2] x [-0.2,0.2], one of them rotated about y by 3 (or 10) degrees, in one mesh.
- Wrong result: `MeshRobustRepair` returns a clean mesh of volume 0.839 (10 degrees: 2.77) instead of the union's
  7.239: the difference A \ B. `RepairVolumeIoU` reports 1, so the volume check does not catch it. At 30 degrees the
  result is right (11.52).
- Cause: `MeshAutoRepair` with `PostRepairCleanup` runs `MeshPostRepairCleanup.FindInvertedComponents`
  (mesh_ops/MeshPostRepairCleanup.cs, about line 282). For a closed component i whose box lies inside the box of
  another closed component j, it counts j as enclosing i when the majority of a few interior samples of i lie inside j
  (`2 * votes > samples.Count`). Its comment states the assumption: "the interiors of two components are disjoint or
  nested". Crossing components break it: at 3 degrees the thin box's box lies inside the rotated one's box, most of
  its samples are inside, so it is taken for a cavity and flipped. The exact resolve then keeps the solid with a
  positive winding number, A \ B. The volume check compares with `MeshRepairMetrics.SolidReference(input)`, which
  orients the input's components by the same nesting rule, so both sides agree on the wrong solid.
- Fix ideas:
  1. Count j as enclosing i only if their surfaces do not intersect (`DMeshAABBTree3.TestIntersection` between the
     two component trees, or the exact `MeshRepairMetrics` self-intersection pairs restricted to the pair). Crossing
     components keep the sign of their own volume. Cost: small code change; one tree-tree test per candidate pair
     (only pairs with nested boxes are tested today). Risk: changes repair results on real meshes with crossing parts
     (Thingi10K assemblies), so it needs the `benchmarks/MeshRepairBenchmark` A/B on the 400 set and the holdout.
  2. Require all samples (not the majority) inside j. Cheaper, but the majority vote exists for samples near thin
     features (see the comment), so it would trade one failure for another.
  3. For the volume check, build the input's reference solid from the winding number of the input as given
     (`Unoriented`) when its components intersect: it would then reject the wrong result and fall back to the
     exact resolve of the input (which gives the union). Cost: small; risk: more fallbacks.

### R2: a large hole fill is rejected by `MeshRobustRepair`'s volume check (limitation, not pinned)

- Tests: `QuadHoleWhoseDiagonalIsAnExistingEdge`, `HoleNextToANearZeroAreaTriangle_PatchStaysLocal`,
  `SmallAndLargeModel_SmoothedFill_SameRefinement` use `MeshAutoRepair` for this reason.
- Input: an open mesh with a large hole, eg a sphere without the cap z > 0.8, or a tetrahedron without two faces.
- Result: `MeshAutoRepair` closes it cleanly, but `MeshRobustRepair` compares that with the winding number solid of
  the open input, which a large hole blurs: IoU 0.974 for the cap (below `MinVolumeIoU` 0.99), 0.397 for the
  tetrahedron. It then solidifies the input instead: remeshed, and for the tetrahedron a blob of volume 0.80 instead of
  the closed tetrahedron's 2.
- This is the documented design (the winding number solid is the reference), so it is recorded, not pinned.
- Ideas: skip or relax the volume check where the repair only added triangles inside the input's boundary loops (the
  added triangles are the fills: their solid differs from the open input's by construction); or compare only away
  from the holes. Cost: medium (the check would need the fill triangles from `MeshAutoRepair`); risk: a bad fill
  would no longer be caught by the IoU.

### Checked: Thingi10K 48950 end to end

`MeshRobustRepair.Apply()` on Thingi10K 48950 (fetched at run time from the Hugging Face mirror, not committed), on
#317 (alpha wrap B3, `RefineNonManifold`) merged with master (7b quality pass): wrap fallback, simplified and quality
pass kept, result clean, 42336 triangles, smallest angle 2.08 degrees, no triangle below 0.5 degrees (41 s; scratch
harness, angles from atan2 of cross and dot products).
