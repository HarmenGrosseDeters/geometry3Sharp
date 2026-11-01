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
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.self_pairs(g3.TriangleBoxTree[],System.Collections.Generic.List{System.Int64})~System.Int32", Justification = "reviewed: per-work-item pair lists, appended in item order; int count only")]
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshArrangement.find_intersections(g3.PhaseClock)", Justification = "reviewed: per-mesh trees, thread-local candidate lists, per-triangle buckets, per-pair intersections (locked lists, normalized later); int counts only")]

// ExactMeshBoolean.classify: per-piece coplanar stacks, per-patch winding numbers (thread-local candidate lists)
[assembly: SuppressMessage("Reliability", "G3E0002", Scope = "member", Target = "~M:g3.ExactMeshBoolean.classify(System.Boolean[]@,System.Boolean[]@)", Justification = "reviewed: per-piece and per-patch writes, integer winding numbers, no floating-point reduction")]
