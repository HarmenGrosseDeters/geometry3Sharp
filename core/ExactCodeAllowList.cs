// Allow-list for the banned-API rule in the exact-arithmetic code (BannedSymbols.txt, RS0030; an error in
// math/Exact*.cs, math/AdaptivePredicates.cs and mesh_ops/ExactMesh*.cs, see .editorconfig).
//
// Parallel loops are banned there because a floating-point reduction across threads depends on the schedule, and
// the analyzer cannot see what a loop body aggregates. Each entry below allows the parallel loops of one member after
// review: every loop writes only its own index (or a thread-local list) and aggregates only integers (Interlocked on
// int / long), no floating-point value is reduced across threads.
//
// An entry covers the whole member, so a new parallel loop in an allowed member is not caught; a new member needs a
// new entry, and the review checks it (docs/REVIEW_CHECKLIST.md). Never add FMA, float or floating-point
// accumulation here.
using System.Diagnostics.CodeAnalysis;

// ExactMeshArrangement: per-index conversions, per-triangle preparation / triangulation / input triangles, per-mesh
// box trees, candidate pairs (thread-local lists, int count), per-triangle plane buckets, per-pair intersection
// (int counters), point intervals per index and disjoint point clusters (int count)
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshArrangement.PointsToVectors(System.Collections.Generic.IList{System.Int32})~g3.Vector3d[]", Justification = "reviewed: per-index writes, no floating-point reduction")]
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshArrangement.merge_points", Justification = "reviewed: per-index intervals, disjoint clusters, int count only")]
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshArrangement.Compute", Justification = "reviewed: per-triangle preparation and triangulation, no floating-point reduction")]
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshArrangement.build_triangles", Justification = "reviewed: per-triangle writes, no floating-point reduction")]
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshArrangement.find_intersections(g3.PhaseClock)", Justification = "reviewed: per-mesh trees, thread-local candidate lists, per-triangle buckets, per-pair intersections; int counts only")]

// ExactMeshBoolean.classify: per-piece coplanar stacks, per-patch winding numbers (thread-local candidate lists)
[assembly: SuppressMessage("ApiDesign", "RS0030", Scope = "member", Target = "~M:g3.ExactMeshBoolean.classify(System.Boolean[]@,System.Boolean[]@)", Justification = "reviewed: per-piece and per-patch writes, integer winding numbers, no floating-point reduction")]
