// Allow-list for the parallel-loop rule G3E0002 in the exact-arithmetic code (tools/analyzers/G3ExactCodeAnalyzers;
// an error in the files listed in .editorconfig).
//
// Parallel loops are banned there because a floating-point reduction across threads depends on the schedule, and an
// analyzer cannot see what a loop body aggregates. Each entry below allows the parallel loops of one member after
// review. G3E0002 is a rule of its own: an entry switches off only it, the FMA, float and Interlocked-on-double bans
// (G3E0001, G3E0003, RS0030) still hold in these members.
//
// Reviewed (October 2026): no loop reduces a floating-point value across threads; the only cross-thread aggregates are
// Interlocked.Add / Increment on int or long counters. Most loops write their own index or a thread-local list. Not
// all: in find_intersections and Compute, intersect() appends to shared per-triangle lists under a lock, new exact
// points get ids by Interlocked.Increment (schedule-dependent) in a locked sharded index, and exact_ref caches
// lazily (a benign race that stores the same value). The output is deterministic because it is normalized
// afterwards: segments and points are sorted by ComparePoints, and the canonical point of a cluster is chosen by
// PointKey, not by id. A change there must keep that normalization (ExactBooleanDeterminismTests).
//
// An entry covers the whole member, so a new parallel loop in an allowed member is not caught; a new member needs a
// new entry, and the review checks it (docs/REVIEW_CHECKLIST.md).
using System.Diagnostics.CodeAnalysis;

// ExactMeshArrangement: per-index conversions, per-triangle preparation / triangulation / input triangles, per-mesh
// box trees, candidate pairs (thread-local lists, int count; within a mesh by the self-join's work items, each into
// its own list), per-triangle plane buckets, per-pair intersection
// (locked per-triangle lists, int counters), point intervals per index and disjoint point clusters (int count)
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.PointsToVectors(System.Collections.Generic.IList{System.Int32})~g3.Vector3d[]", Justification = "reviewed: per-index writes, no floating-point reduction")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.merge_points", Justification = "reviewed: per-index intervals, disjoint clusters, int count only")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.Compute", Justification = "reviewed: per-triangle preparation and triangulation (points in a locked index, normalized later), no floating-point reduction")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.build_triangles", Justification = "reviewed: per-triangle writes, no floating-point reduction")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.self_pairs(System.Collections.Generic.List{g3.AxisAlignedBox3d}[],g3.TriangleBoxTree[],System.Collections.Generic.List{System.Int64})~System.Int32", Justification = "reviewed: per-work-item pair lists, appended in item order; int count only")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.cull(System.Collections.Generic.List{g3.AxisAlignedBox3d}[],g3.TriangleBoxTree[])", Justification = "reviewed: per mesh of CullOutsideOf in turn (serially), per-triangle box tests into per-triangle flags (the triangles missing the mesh box flagged directly), then one exact point test (CullTest) per connected set into its root's flag; the union-find and the counts run serially; CullPrepare runs serially before the loops")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.find_intersections(g3.PhaseClock)", Justification = "reviewed: per-mesh trees, thread-local candidate lists, per-triangle buckets, per-pair intersections (locked lists, normalized later); int counts only")]

// TriangleBoxTree: Morton codes per box, the sort of (code, box index) by runs sorted apart and merged pairwise, the subtrees below a fixed depth, the leaf boxes
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.TriangleBoxTree.#ctor(System.Collections.Generic.List{g3.AxisAlignedBox3d})", Justification = "reviewed: leaf boxes written per index")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.TriangleBoxTree.morton_order(System.Int32)", Justification = "reviewed: one Morton code per box into its own slot; the box over all centres is a serial min / max")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.TriangleBoxTree.sort_codes_parallel(System.Int64[],System.Int32)", Justification = "reviewed: integer keys; runs of fixed ranges sorted apart, merged pairwise into disjoint ranges by (code, box index), a total order, so the result does not depend on the schedule")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.TriangleBoxTree.build_parallel(System.Int32)", Justification = "reviewed: subtrees of fixed ranges into their own node arrays, renumbered serially in the order of the serial build; boxes are min / max (exact), no floating-point reduction")]

// ExactMeshBoolean.each (IntersectEach / DifferenceEach): independent booleans over the bodies
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshBoolean.each(g3.DMesh3,System.Collections.Generic.IReadOnlyList{g3.DMesh3},g3.ExactMeshBoolean.Operations,System.Action{g3.ExactMeshBoolean},g3.ExactMeshBoolean.BooleanStatus[])~g3.DMesh3[]", Justification = "reviewed: one independent boolean per body (its own instance; the prepared first mesh only read), results and exceptions into per-body slots; no shared state, no floating-point reduction")]

// ExactMeshBoolean.prepare_inputs: one preparation (closedness, orientation) per input mesh, into its own slot
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshBoolean.prepare_inputs(System.Collections.Generic.List{g3.DMesh3})~g3.ExactMeshBoolean.PreparedInput[]", Justification = "reviewed: one independent preparation per input mesh (only read), results into per-mesh slots; no shared state, no floating-point reduction")]

// ExactMeshBoolean.classify: per-piece coplanar stacks, per-patch winding numbers (thread-local candidate lists)
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshBoolean.classify(System.Boolean[]@,System.Boolean[]@)", Justification = "reviewed: per-piece and per-patch writes, integer winding numbers (the open meshes' generalized ones, by fast winding numbers or by the Antipodal Method's exact ray and boundary sum, each summed within its own piece in a fixed order, thresholded per piece), no floating-point reduction across threads; the boundary segments and boxes of the Antipodal meshes computed serially before the loop, the fast-winding trees of the other open meshes one per task and stored serially afterwards; the loop only reads them")]
