// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Boolean operations (union, intersection, difference) of two triangle meshes, with exact arithmetic:
    /// the intersection curves, the retriangulation and the inside/outside decisions are computed exactly
    /// (ExactMeshArrangement), so touching, coplanar and nearly degenerate configurations give a closed
    /// result, at any scale. Only the output vertex coordinates are rounded to double.
    ///
    /// The solid of a mesh is where its winding number is positive (Zhou et al., "Mesh Arrangements for Solid
    /// Geometry", 2016), so self-intersecting meshes, nested and overlapping shells and duplicated faces are
    /// handled. The triangles of both meshes are split along all intersections, also of a mesh with itself.
    /// For each patch of pieces between intersection curves, the winding numbers of both meshes on its front
    /// side are counted exactly along a ray (signed crossings); the back side differs by the orientations of
    /// the triangles at the patch. A piece is part of the result if the operation is true on one side and
    /// false on the other, oriented towards the false side. Pieces where several triangles coincide are
    /// output once.
    ///
    /// The winding numbers are exact for meshes that are closed in the winding number sense (every edge used
    /// equally often in both directions; non-manifold edges and self-intersections are allowed). For other
    /// (open) meshes the generalized winding number is used (Jacobson et al. 2013, fast approximation), with the
    /// solid where it is above 0.5; then the result is generally not closed, also along edges where an open boundary
    /// of an input meets other pieces and the kept pieces run the edge the same way (ConflictEdges). An inside-out mesh (winding
    /// number -1) has an empty solid. A mesh with inconsistently oriented triangles (flipped faces) is first
    /// oriented consistently per component (triangles connected across edges with two triangles, by vertex
    /// position), keeping the orientation of the larger part by area (ReorientedTriangles); components that
    /// cannot be oriented are left as they are. Degenerate (zero-area) input
    /// triangles are dropped. The rounding of new vertices can create tiny self-intersections, and intersection
    /// points that nearly coincide give degenerate slivers: SafeRounding repairs both after the rounding
    /// (MeshRoundingRepair, verified with exact predicates on the doubles), ComputeExactVertices gives the
    /// exact rational coordinates of the vertices instead.
    /// New vertices that lie exactly inside a flat region of the result or on a straight edge between two flat
    /// regions (eg the crossings of the edges of overlapping coplanar faces) are removed and the regions
    /// retriangulated (SimplifyFlatRegions, ExactPlanarSimplify), so that repeated booleans do not split the
    /// faces again and again at rounded points.
    ///
    /// Usage: construct with the two meshes and the operation, call Compute(), and read Result (a new mesh;
    /// the inputs are not modified). Compute() returns false and sets Error for null inputs, non-finite
    /// coordinates, or open meshes when AllowOpenMeshes is false; it throws InvalidOperationException in the
    /// (not expected) case that no non-degenerate ray is found. Result has no normals, colors, UVs or groups.
    /// Compute() can be called again after changing the meshes or the operation.
    ///
    /// Cost: exact predicates behind floating-point filters (BigInteger only where the filters cannot decide);
    /// slower than floating-point methods, the phases run in parallel.
    /// Prefer this class over <see cref="MeshBoolean"/> (floating-point, not robust, union of closed meshes
    /// only). Use <see cref="gs.MeshWindingNumberBoolean"/> instead when a remeshed, volumetric result is
    /// acceptable and very broken input (triangle soups) must give a closed mesh.
    ///
    /// Implemented from the papers cited here and in ExactMeshArrangement, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class ExactMeshBoolean
    {
        /// <summary>
        /// the boolean operation; Difference is A minus B. With more than two meshes (Meshes): Union is inside
        /// any of them, Intersection inside all of them, Difference inside the first and none of the others.
        /// </summary>
        public enum Operations { Union = 0, Intersection = 1, Difference = 2 }

        /// <summary> result code of Compute() </summary>
        public enum BooleanStatus
        {
            /// <summary> Result is set (possibly empty, eg the intersection of disjoint meshes) </summary>
            Success = 0,
            /// <summary> no input mesh, a null mesh, or non-finite coordinates (see Error) </summary>
            InvalidInput = 1,
            /// <summary>
            /// a mesh is not closed in the winding number sense while AllowOpenMeshes is false (after flipped
            /// triangles are reoriented, see ReorientedTriangles)
            /// </summary>
            OpenInput = 2,
            /// <summary> Compute() was not called yet </summary>
            NotComputed = 3
        }

        /// <summary> the input meshes of A op B (not modified); unused if Meshes is set </summary>
        public DMesh3 MeshA, MeshB;
        /// <summary>
        /// the input meshes of an operation on any number of meshes, in one arrangement (not modified). If null,
        /// the inputs are MeshA and MeshB. Mesh i is entry i of the winding number vector of WindingPredicate.
        /// </summary>
        public List<DMesh3> Meshes;
        /// <summary> the operation computed by Compute() (unless WindingPredicate is set) </summary>
        public Operations Operation;
        /// <summary>
        /// if set, decides instead of Operation where the result's solid is (Zhou et al. 2016): called with the
        /// winding numbers of all input meshes at a point (entry i for mesh i, in the order of Meshes, or A, B),
        /// returns true for inside. The winding numbers are exact integers for closed meshes; for open meshes the
        /// generalized winding number w is rounded to ceil(w - 1/2) (so w > 1/2 is at least 1). Called on both
        /// sides of every patch, from one thread; the array is reused and must not be modified or kept. A point
        /// far from all meshes has all winding numbers 0, so a predicate that is true there gives an unbounded
        /// solid and no closed result. Examples: AtLeast(k), (w) => (w[0] > 0) != (w[1] > 0) (symmetric difference).
        /// </summary>
        public Func<int[], bool> WindingPredicate;

        /// <summary> seed of the random ray directions (the result does not depend on it) </summary>
        public int Seed = 31337;

        /// <summary> the result of Compute(): a new mesh (closed and manifold for closed inputs), or null if Compute() failed </summary>
        public DMesh3 Result;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> result code of Compute(); Success iff Compute() returned true </summary>
        public BooleanStatus Status = BooleanStatus.NotComputed;
        /// <summary> pairs of triangles of different meshes that intersect </summary>
        public int IntersectingPairs;
        /// <summary> pairs of triangles of the same mesh that intersect (other than at shared vertices and edges) </summary>
        public int SelfIntersectingPairs;
        /// <summary> edges of the result where solids touch (more than two pieces); split into manifold pairs </summary>
        public int NonManifoldEdges;
        /// <summary>
        /// triangles that could not be added to the result mesh and got copies of some of their vertices: at an edge with
        /// more than two triangles that could not be split, or at an edge they would run the same way as the triangle
        /// there (see ConflictEdges); expected to be 0 for closed inputs
        /// </summary>
        public int NonManifoldTriangles;
        /// <summary>
        /// edges where the kept pieces cannot all be joined as neighbours that run the edge in opposite directions (two
        /// that run it the same way, or pieces left without such a neighbour around the edge), so that the result is open
        /// along the edge (and consistently oriented). Only with open inputs: where an open boundary of an input lies on an
        /// edge that other pieces meet, the jumps of the winding numbers across the pieces around the edge do not add up
        /// to zero, and no inside/outside choice for the regions around the edge fits all the pieces. 0 for closed inputs.
        /// </summary>
        public int ConflictEdges;
        /// <summary> edges where the solid touches itself and that were split per pair of triangles (see assemble) </summary>
        public int SplitNonManifoldEdges;
        /// <summary> number of patches classified </summary>
        public int Patches;
        /// <summary>
        /// input triangles flipped to orient their component consistently (see Compute; 0 for consistently oriented
        /// inputs)
        /// </summary>
        public int ReorientedTriangles;
        /// <summary>
        /// remove new vertices (not input vertices) that lie exactly inside a flat region of the result or on a
        /// straight edge between two flat regions, and retriangulate there (ExactPlanarSimplify). Without it,
        /// coplanar overlapping faces leave the crossings of their edges and diagonals in the result, which
        /// repeated booleans split at again (the number of triangles grows quickly).
        /// </summary>
        public bool SimplifyFlatRegions = true;
        /// <summary> vertices removed by SimplifyFlatRegions </summary>
        public int RemovedVertices;
        /// <summary> if false, Compute() fails for meshes that are not closed in the winding number sense </summary>
        public bool AllowOpenMeshes = true;
        /// <summary>
        /// after rounding the vertices to double, repair the self-intersections and degenerate triangles (area below
        /// 1e-12 of the squared diagonal) the rounding left, by local edits checked with exact predicates on the doubles
        /// (MeshRoundingRepair: contractions of short edges, flips, tiny vertex moves, removal of closed components that
        /// keep a sliver and are tiny or thinner than 1e-5 of the diagonal, counted in Rounding.RemovedComponents). Input vertices are kept
        /// where possible, and sheets that touch at duplicated vertices still touch, at the same points. Not every case
        /// can be repaired: Compute() still succeeds (Status Success) if defects remain; RoundingClean tells whether the
        /// result is verified free of both, Rounding has the counts.
        /// </summary>
        public bool SafeRounding = false;
        /// <summary> the repair run by SafeRounding (its counts), or null </summary>
        public MeshRoundingRepair Rounding;
        /// <summary>
        /// SafeRounding ran and its result has no self-intersections and no degenerate triangles (exact check; contacts
        /// at duplicated vertices, where solids touch, are not intersections). False with Status Success means that
        /// defects remain (Rounding.RemainingIntersections, RemainingDegenerate).
        /// </summary>
        public bool RoundingClean { get { return Rounding != null && Rounding.IsClean; } }
        /// <summary>
        /// compute ResultExactVertices: the exact coordinates of the result's vertices, before they are rounded
        /// </summary>
        public bool ComputeExactVertices = false;
        /// <summary>
        /// if ComputeExactVertices: per vertex id of Result, its exact (rational) coordinates in the input's
        /// coordinate system; Result's coordinates are these rounded to the nearest double, except the vertices that split
        /// edges where solids touch: their entries are the exact midpoints of the edges, their coordinates the midpoints
        /// of the rounded ends (which can differ from the rounded exact midpoint by an ulp). With SafeRounding the entries are those before the
        /// repair, for the vertex ids that remain (the repair moves some vertices by tiny amounts).
        /// </summary>
        public ExactPoint3[] ResultExactVertices;
        /// <summary>
        /// per input mesh: winding numbers exact (closed mesh, after flipped triangles are reoriented) or generalized
        /// (open mesh)
        /// </summary>
        public bool ExactWindingA, ExactWindingB;
        /// <summary> the same for every input mesh (in the order of Meshes, or A, B), after Compute() </summary>
        public bool[] ExactWinding;
        // for tests: use generalized winding numbers also for closed meshes
        internal bool ForceGeneralizedWinding = false;
        // for tests: triangles per batch of the arrangement's candidate pair search
        internal long CandidateBatchSize = 1 << 24;
        // for tests: no floating-point filters in the arrangement
        internal bool DisableFilters = false;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> sizes of the intermediate results, for diagnostics </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        /// <summary> bytes allocated per phase (by all threads; 0 on netstandard2.0), for profiling </summary>
        internal Dictionary<string, long> PhaseAllocatedBytes = new Dictionary<string, long>();


        /// <summary> set up the boolean of meshes a and b; call Compute() to run it </summary>
        public ExactMeshBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }

        /// <summary>
        /// set up an operation on any number of meshes in one arrangement: union (inside any), intersection
        /// (inside all) or difference (inside the first and none of the others); call Compute() to run it
        /// </summary>
        public ExactMeshBoolean(IEnumerable<DMesh3> meshes, Operations op)
        {
            Meshes = new List<DMesh3>(meshes ?? new DMesh3[0]);
            Operation = op;
        }

        /// <summary>
        /// set up the solid of a winding number predicate over any number of meshes (see WindingPredicate), eg
        /// AtLeast(2) for the points inside at least two of the meshes; call Compute() to run it
        /// </summary>
        public ExactMeshBoolean(IEnumerable<DMesh3> meshes, Func<int[], bool> windingPredicate)
            : this(meshes, Operations.Union)
        {
            WindingPredicate = windingPredicate;
        }

        /// <summary>
        /// predicate for WindingPredicate: inside at least k of the meshes (winding number positive). k = 1 is the
        /// union, k = number of meshes the intersection, a larger k gives an empty result; k &lt;= 0 is true
        /// everywhere (no surface between inside and outside: an empty result).
        /// </summary>
        public static Func<int[], bool> AtLeast(int k)
        {
            return (w) => {
                int count = 0;
                for (int i = 0; i < w.Length; ++i)
                    if (w[i] > 0 && ++count >= k)
                        return true;
                return k <= 0;
            };
        }

        /// <summary> union of any number of meshes in one arrangement; null if Compute() fails </summary>
        public static DMesh3 Union(IEnumerable<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operations.Union);
            return op.Compute() ? op.Result : null;
        }

        /// <summary> intersection of any number of meshes in one arrangement; null if Compute() fails </summary>
        public static DMesh3 Intersection(IEnumerable<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operations.Intersection);
            return op.Compute() ? op.Result : null;
        }


        ExactMeshArrangement arr;

        /// <summary>
        /// Compute the boolean into Result. Returns false (and sets Error and Status) if there is no input, an
        /// input is null, has non-finite coordinates, or is open while AllowOpenMeshes is false; otherwise true
        /// (Status Success). An empty result (for example the intersection of disjoint meshes) is a success with
        /// an empty Result mesh.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public bool Compute()
        {
            Result = null;
            Error = null;
            Status = BooleanStatus.NotComputed;
            Rounding = null;
            ResultExactVertices = null;
            // (the meshes or the operation may have changed since the last call)
            IntersectingPairs = SelfIntersectingPairs = NonManifoldEdges = NonManifoldTriangles = SplitNonManifoldEdges = ConflictEdges = Patches = RemovedVertices = 0;
            ReorientedTriangles = 0;
            ExactWindingA = ExactWindingB = false;
            ExactWinding = null;
            PhaseMilliseconds = new Dictionary<string, long>();
            PhaseAllocatedBytes = new Dictionary<string, long>();
            Counts = new Dictionary<string, long>();
            var inputClock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            arr = null;
            bounds.Clear();
            windingTrees.Clear();
            List<DMesh3> inputs = Meshes ?? new List<DMesh3>() { MeshA, MeshB };
            if (inputs.Count == 0)
                return fail(BooleanStatus.InvalidInput, "no input meshes");
            foreach (DMesh3 m in inputs)
                if (m == null)
                    return fail(BooleanStatus.InvalidInput, "input mesh is null");
            foreach (DMesh3 m in inputs) {
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    // (per coordinate: the sum of finite coordinates can overflow)
                    if (finite(v.x) == false || finite(v.y) == false || finite(v.z) == false)
                        return fail(BooleanStatus.InvalidInput, "input meshes have non-finite coordinates");
                }
            }
            // inconsistently oriented inputs (flipped triangles, as from broken files) are oriented consistently per
            // component first, on copies: their winding numbers are not meaningful otherwise (the generalized
            // winding number jumps the wrong way across a flipped triangle, so next to it the inside reads as outside)
            inputs = new List<DMesh3>(inputs);
            ExactWinding = new bool[inputs.Count];
            for (int m = 0; m < inputs.Count; ++m) {
                bool closed = IsWindingClosed(inputs[m]);
                if (closed == false) {
                    DMesh3 o = oriented(inputs[m]);
                    if (o != inputs[m]) {
                        inputs[m] = o;
                        closed = IsWindingClosed(o);
                    }
                }
                ExactWinding[m] = closed && ForceGeneralizedWinding == false;
            }
            ExactWindingA = ExactWinding[0];
            ExactWindingB = inputs.Count > 1 && ExactWinding[1];
            if (AllowOpenMeshes == false && Array.IndexOf(ExactWinding, false) >= 0)
                return fail(BooleanStatus.OpenInput, "input meshes must be closed (every edge used equally often in both directions)");

            inputClock.Lap("input");
            arr = new ExactMeshArrangement(inputs) { SelfIntersections = true, DisableFilters = DisableFilters, CandidateBatchSize = CandidateBatchSize };
            arr.Compute();
            IntersectingPairs = arr.IntersectingPairs - arr.SelfIntersectingPairs;
            SelfIntersectingPairs = arr.SelfIntersectingPairs;
            foreach (var kv in arr.PhaseMilliseconds)
                PhaseMilliseconds[kv.Key] = kv.Value;
            foreach (var kv in arr.PhaseAllocatedBytes)
                PhaseAllocatedBytes[kv.Key] = kv.Value;
            Counts["candidate_pairs"] = arr.CandidatePairs;
            Counts["intersecting_pairs"] = arr.IntersectingPairs;
            Counts["points"] = arr.PointCount;
            Counts["merged_points"] = arr.MergedPoints;
            Counts["pieces"] = arr.Output.Count;

            var clock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            bool[] keep, flip;
            windingSides = windingExactSides = 0;
            classify(out keep, out flip);
            clock.Lap("classification");
            Counts["winding_plane_sides"] = windingSides;
            Counts["winding_plane_sides_exact"] = windingExactSides;
            Result = assemble(keep, flip);
            clock.Lap("assembly");
            Counts["removed_vertices"] = RemovedVertices;
            if (SafeRounding) {
                bool[] fixedV = resultFixed;
                Rounding = new MeshRoundingRepair(Result) { SharedPositionsTouch = true, FixedVertexF = (v) => v < fixedV.Length && fixedV[v] };
                Rounding.Apply();
                clock.Lap("rounding");
                Counts["rounding_intersections"] = Rounding.InitialIntersections;
                Counts["rounding_degenerate"] = Rounding.InitialDegenerate;
                Counts["rounding_edits"] = Rounding.Collapses + Rounding.Flips + Rounding.Moves + Rounding.Zips;
                Counts["rounding_removed_triangles"] = Rounding.RemovedTriangles;
                Counts["rounding_removed_components"] = Rounding.RemovedComponents;
            }
            Status = BooleanStatus.Success;
            return true;
        }

        /// <summary>
        /// The mesh with each component (triangles connected across edges with exactly two triangles, by vertex
        /// position, so unwelded copies of a vertex count as the same) oriented consistently, keeping the
        /// orientation of the larger part by area (ties within a relative 1e-9: of its first triangle); mesh itself if
        /// nothing changes.
        /// Components that cannot be oriented (Moebius strips) are left as they are.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        DMesh3 oriented(DMesh3 mesh)
        {
            // vertices welded by position
            var ids = new Dictionary<Vector3d, int>();
            var pos = new int[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                if (v.x == 0) v.x = 0;      // -0.0
                if (v.y == 0) v.y = 0;
                if (v.z == 0) v.z = 0;
                int k;
                if (ids.TryGetValue(v, out k) == false) {
                    k = ids.Count;
                    ids[v] = k;
                }
                pos[vid] = k;
            }
            Func<int, Index3i> tri = (t) => { Index3i f = mesh.GetTriangle(t); return new Index3i(pos[f.a], pos[f.b], pos[f.c]); };
            // the triangles at each undirected edge (by welded vertices)
            var edgeTris = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
            foreach (int t in mesh.TriangleIndices()) {
                Index3i f = tri(t);
                for (int k = 0; k < 3; ++k) {
                    int u = f[k], v = f[(k + 1) % 3];
                    if (u == v)
                        continue;
                    long key = ExactMeshArrangement.EdgeKey(u, v);
                    List<int> l;
                    if (edgeTris.TryGetValue(key, out l) == false)
                        edgeTris[key] = l = new List<int>(2);
                    l.Add(t);
                }
            }
            int maxT = mesh.MaxTriangleID;
            var flipT = new bool[maxT];
            var seen = new bool[maxT];
            var component = new List<int>();
            var stack = new Stack<int>();
            List<int> toFlip = null;
            foreach (int root in mesh.TriangleIndices()) {
                if (seen[root])
                    continue;
                component.Clear();
                bool orientable = true;
                seen[root] = true;
                stack.Push(root);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    component.Add(t);
                    Index3i f = tri(t);
                    for (int k = 0; k < 3; ++k) {
                        int u = f[k], v = f[(k + 1) % 3];
                        if (u == v)
                            continue;
                        List<int> l = edgeTris[ExactMeshArrangement.EdgeKey(u, v)];
                        if (l.Count != 2)
                            continue;
                        int o = (l[0] == t) ? l[1] : l[0];
                        if (o == t)
                            continue;
                        // the neighbour runs the edge u -> v the same way: one of the two is flipped
                        bool want = flipT[t] ^ runs(tri(o), u, v);
                        if (seen[o]) {
                            if (flipT[o] != want)
                                orientable = false;
                            continue;
                        }
                        seen[o] = true;
                        flipT[o] = want;
                        stack.Push(o);
                    }
                }
                if (orientable == false)
                    continue;
                double flippedArea = 0, keptArea = 0;
                foreach (int t in component) {
                    if (flipT[t]) flippedArea += mesh.GetTriArea(t); else keptArea += mesh.GetTriArea(t);
                }
                // (nearly equal areas: the first triangle keeps its orientation, so that the choice does not depend on
                // the rounding of the areas, eg under a rotation)
                bool invert = flippedArea - keptArea > 1e-9 * (flippedArea + keptArea);
                foreach (int t in component)
                    if (flipT[t] != invert)
                        (toFlip ?? (toFlip = new List<int>())).Add(t);
            }
            if (toFlip == null)
                return mesh;
            var copy = new DMesh3(mesh, false, MeshComponents.None);
            foreach (int t in toFlip)
                copy.ReverseTriOrientation(t);
            ReorientedTriangles += toFlip.Count;
            return copy;
        }

        static bool finite(double x)
        {
            return double.IsNaN(x) == false && double.IsInfinity(x) == false;
        }

        bool fail(BooleanStatus status, string error)
        {
            Status = status;
            Error = error;
            return false;
        }


        /// <summary>
        /// The boundary of the solid of a mesh (where its winding number is positive): self-intersections,
        /// nested shells and duplicated faces are resolved exactly. Returns null if Compute() fails.
        /// </summary>
        public static DMesh3 Resolve(DMesh3 mesh)
        {
            return Resolve(mesh, false);
        }

        /// <summary> as Resolve(mesh), with SafeRounding of the result if safeRounding </summary>
        public static DMesh3 Resolve(DMesh3 mesh, bool safeRounding)
        {
            var op = new ExactMeshBoolean(mesh, new DMesh3(), Operations.Union) { SafeRounding = safeRounding };
            return op.Compute() ? op.Result : null;
        }


        /// <summary>
        /// Every edge is used equally often in both directions (by vertex position, so unwelded copies of a
        /// vertex count as the same). Then the winding number is an integer, the same along every ray.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static bool IsWindingClosed(DMesh3 mesh)
        {
            // fast path: every edge of the mesh has two triangles that run through it in opposite directions
            // (then the edges are also balanced by position, merging vertices keeps the sums)
            bool byIds = true;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) { byIds = false; break; }
                Index2i ev = mesh.GetEdgeV(eid);
                if (runs(mesh.GetTriangle(et.a), ev.a, ev.b) == runs(mesh.GetTriangle(et.b), ev.a, ev.b)) { byIds = false; break; }
            }
            if (byIds)
                return true;

            var ids = new Dictionary<Vector3d, int>();
            Func<int, int> id = (vid) => {
                Vector3d v = mesh.GetVertex(vid);
                if (v.x == 0) v.x = 0;      // -0.0
                if (v.y == 0) v.y = 0;
                if (v.z == 0) v.z = 0;
                int k;
                if (ids.TryGetValue(v, out k) == false) {
                    k = ids.Count;
                    ids[v] = k;
                }
                return k;
            };
            var count = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                int[] v = { id(t.a), id(t.b), id(t.c) };
                for (int k = 0; k < 3; ++k) {
                    int x = v[k], y = v[(k + 1) % 3];
                    if (x == y) continue;
                    // +1 for x < y, -1 for the reverse direction
                    long key = (x < y) ? (((long)x << 32) | (uint)y) : (((long)y << 32) | (uint)x);
                    int d = (x < y) ? 1 : -1;
                    int c0;
                    count.TryGetValue(key, out c0);
                    count[key] = c0 + d;
                }
            }
            foreach (int c in count.Values)
                if (c != 0)
                    return false;
            return true;
        }


        // the winding numbers at a point: winding[m] for every mesh m, of which only the meshes in touched can
        // be nonzero (the others are 0)
        [MethodImpl(JitHints.Optimize)]
        bool inside(int[] winding, List<int> touched)
        {
            if (WindingPredicate != null)
                return WindingPredicate(winding);
            int count = 0;
            foreach (int m in touched)
                if (winding[m] > 0)
                    count++;
            switch (Operation) {
                case Operations.Union: return count > 0;
                case Operations.Intersection: return count == winding.Length;
                default: return winding[0] > 0 && count == 1;
            }
        }


        [MethodImpl(JitHints.Optimize)]
        void classify(out bool[] keep, out bool[] flip)
        {
            List<ExactMeshArrangement.OutputTriangle> output = arr.Output;
            int n = output.Count;
            keep = new bool[n];
            flip = new bool[n];

            // stacks: the input triangles that contain a piece (its source, and coplanar overlapping triangles);
            // the piece is output only from the stack's first triangle
            var stack = new List<int>[n];
            bool[] representative = new bool[n];
            gParallel.ForRange(n, (i) => {
                var ot = output[i];
                var src = arr.Triangles[ot.Source];
                representative[i] = true;
                if (src.Coplanar.Count == 0)
                    return;
                int c = centroid(ot);
                foreach (int ui in src.Coplanar) {
                    if (inside_coplanar(src, arr.Triangles[ui], c)) {
                        if (stack[i] == null)
                            stack[i] = new List<int>() { ot.Source };
                        stack[i].Add(ui);
                        if (ui < ot.Source)
                            representative[i] = false;
                    }
                }
            });

            // patches: pieces of one mesh connected across edges that are not on an intersection curve, with
            // exactly two pieces of that mesh (then they are oriented consistently, the meshes are winding
            // closed); the winding numbers are the same on the front sides of all pieces of a patch
            long[] edgeKeys; int[] edgeTri;
            group_edges(n, (i) => new Index3i(output[i].A, output[i].B, output[i].C), out edgeKeys, out edgeTri);
            int[] parent = new int[n];
            for (int i = 0; i < n; ++i)
                parent[i] = i;
            Func<int, int> find = (x) => {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            var atEdge = new List<int>();
            for (int g0 = 0, g1 = 0; g0 < edgeKeys.Length; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < edgeKeys.Length && edgeKeys[g1] == edgeKeys[g0]) g1++;
                if (arr.ConstraintEdges.Contains(edgeKeys[g0]))
                    continue;
                // the pieces at the edge by mesh (stable insertion sort, the groups are small)
                atEdge.Clear();
                for (int g = g0; g < g1; ++g) {
                    int t = edgeTri[g], k = atEdge.Count;
                    atEdge.Add(t);
                    while (k > 0 && output[atEdge[k - 1]].Mesh > output[t].Mesh) {
                        atEdge[k] = atEdge[k - 1];
                        k--;
                    }
                    atEdge[k] = t;
                }
                for (int s0 = 0, s1 = 0; s0 < atEdge.Count; s0 = s1) {
                    s1 = s0 + 1;
                    while (s1 < atEdge.Count && output[atEdge[s1]].Mesh == output[atEdge[s0]].Mesh) s1++;
                    if (s1 - s0 != 2)
                        continue;
                    int a = atEdge[s0], b = atEdge[s0 + 1];
                    if (same_stack(stack[a], stack[b]) == false)
                        continue;
                    int ra = find(a), rb = find(b);
                    if (ra != rb)
                        parent[ra] = rb;
                }
            }

            // winding numbers in front of each patch, sparse (the nonzero ones, as pairs mesh, winding number):
            // a closed mesh has winding number 0 outside its box, so only the meshes whose box contains the
            // piece are evaluated. Computed in parallel from the first representative piece of each patch (in the
            // order of the pieces), each with a random number generator of its own (the winding numbers are exact,
            // the random rays do not change them).
            int nm = arr.Meshes.Count;
            var closedBoxes = new List<AxisAlignedBox3d>();
            var openMeshes = new List<int>();
            for (int m = 0; m < nm; ++m) {
                closedBoxes.Add(ExactWinding[m] ? input_bounds(m) : AxisAlignedBox3d.Empty);
                if (ExactWinding[m] == false)
                    openMeshes.Add(m);
                // (the caches that the parallel evaluation reads)
                BigInteger[] bmin, bmax;
                if (ExactWinding[m])
                    mesh_bounds(m, out bmin, out bmax);
                else
                    winding_tree(m);
            }
            var closedTree = new TriangleBoxTree(closedBoxes);
            var patchWinding = new int[n][];
            var firsts = new List<int>();
            for (int i = 0; i < n; ++i) {
                if (representative[i] == false)
                    continue;
                int root = find(i);
                if (patchWinding[root] == null) {
                    patchWinding[root] = noWinding;
                    firsts.Add(i);
                }
            }
            Patches = firsts.Count;
            gParallel.ForRange(firsts.Count, () => new List<int>(), (k, candidates) => {
                int i = firsts[k];
                patchWinding[find_const(parent, i)] = patch_winding(i, stack[i], closedTree, openMeshes, candidates);
            });

            int[] winding = new int[nm];
            bool[] isTouched = new bool[nm];
            var touched = new List<int>();
            Action<int, int> add = (m, d) => {
                if (isTouched[m] == false) {
                    isTouched[m] = true;
                    touched.Add(m);
                }
                winding[m] += d;
            };
            for (int i = 0; i < n; ++i) {
                if (representative[i] == false)
                    continue;
                var ot = output[i];
                int[] w = patchWinding[find(i)];
                for (int k = 0; k < w.Length; k += 2)
                    add(w[k], w[k + 1]);
                bool inFront = inside(winding, touched);
                // back side: crossing the stack from the back to the front changes the winding number of a
                // mesh by -1 for each of its triangles oriented like the piece, +1 for opposite ones
                if (stack[i] == null) {
                    add(ot.Mesh, 1);
                } else {
                    var src = arr.Triangles[ot.Source];
                    foreach (int ti in stack[i]) {
                        var U = arr.Triangles[ti];
                        BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                        add(U.Mesh, (dot.Sign > 0) ? 1 : -1);
                    }
                }
                bool inBack = inside(winding, touched);
                keep[i] = inFront != inBack;
                flip[i] = inFront;
                foreach (int m in touched) {
                    winding[m] = 0;
                    isTouched[m] = false;
                }
                touched.Clear();
            }
        }

        static readonly int[] noWinding = new int[0];

        // the root of x in the union-find forest parent, without changing it (thread-safe for reading)
        static int find_const(int[] parent, int x)
        {
            while (parent[x] != x)
                x = parent[x];
            return x;
        }

        // the nonzero winding numbers (pairs mesh, winding number) in front of piece i (a patch's first
        // representative piece) with its stack; candidates: a buffer
        [MethodImpl(JitHints.Optimize)]
        int[] patch_winding(int i, List<int> stack, TriangleBoxTree closedTree, List<int> openMeshes, List<int> candidates)
        {
            var ot = arr.Output[i];
            int p = centroid(ot);
            // (no error bound needed: the box corners are input doubles, the map from grid to input
            // coordinates is exact and its rounding to double monotone, so a point in the exact box rounds
            // into the double box; the pad only widens the query, front_winding tests the box exactly)
            Vector3d pv = arr.ToInputVector(arr.Exact(p));
            double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
            candidates.Clear();
            closedTree.Query(new AxisAlignedBox3d(pv - pad * Vector3d.One, pv + pad * Vector3d.One), candidates);
            candidates.AddRange(openMeshes);
            candidates.Sort();
            // (a generator per patch: the patches are evaluated in parallel)
            var rng = new Random(unchecked(Seed + 1000003 * i));
            var sparse = new List<int>();
            foreach (int m in candidates) {
                int wm;
                if (ExactWinding[m]) {
                    wm = front_winding(i, m, arr.Trees[m], rng, stack, p);
                } else {
                    // generalized winding numbers are thresholded at 1/2: ceil(w - 1/2) > 0 iff w > 1/2,
                    // and it commutes with adding the integer changes across the patch
                    // (clamped, so that adding the changes across the patch cannot overflow; NaN gives 0)
                    double g = Math.Ceiling(front_generalized_winding(i, m, stack) - 0.5);
                    wm = (g > 1e6) ? 1000000 : (g < -1e6) ? -1000000 : double.IsNaN(g) ? 0 : (int)g;
                }
                if (wm != 0) {
                    sparse.Add(m);
                    sparse.Add(wm);
                }
            }
            return sparse.ToArray();
        }

        // the undirected edges of n triangles, sorted: edgeKeys[k] is the edge, edgeTri[k] the triangle; equal
        // edges are adjacent
        [MethodImpl(JitHints.Optimize)]
        static void group_edges(int n, Func<int, Index3i> tri, out long[] edgeKeys, out int[] edgeTri)
        {
            edgeKeys = new long[3 * n];
            edgeTri = new int[3 * n];
            for (int i = 0; i < n; ++i) {
                Index3i t = tri(i);
                edgeKeys[3 * i] = ExactMeshArrangement.EdgeKey(t.a, t.b);
                edgeKeys[3 * i + 1] = ExactMeshArrangement.EdgeKey(t.b, t.c);
                edgeKeys[3 * i + 2] = ExactMeshArrangement.EdgeKey(t.c, t.a);
                edgeTri[3 * i] = edgeTri[3 * i + 1] = edgeTri[3 * i + 2] = i;
            }
            Array.Sort(edgeKeys, edgeTri);
        }

        [MethodImpl(JitHints.Optimize)]
        static bool same_stack(List<int> a, List<int> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            var set = new HashSet<int>(a);
            foreach (int x in b)
                if (set.Contains(x) == false)
                    return false;
            return true;
        }


        int centroid(ExactMeshArrangement.OutputTriangle ot)
        {
            ExactPoint3 a = arr.Exact(ot.A), b = arr.Exact(ot.B), c = arr.Exact(ot.C);
            BigInteger wbc = b.W * c.W, wac = a.W * c.W, wab = a.W * b.W;
            return arr.AddPoint(ExactPoint3.Create(
                a.X * wbc + b.X * wac + c.X * wab,
                a.Y * wbc + b.Y * wac + c.Y * wab,
                a.Z * wbc + b.Z * wac + c.Z * wab,
                3 * a.W * wbc));
        }

        // point p (in the common plane of t and u) is in the closed triangle u
        [MethodImpl(JitHints.Optimize)]
        bool inside_coplanar(ExactMeshArrangement.InputTriangle t, ExactMeshArrangement.InputTriangle u, int p)
        {
            int s = arr.Orient(t, u.Corners[0], u.Corners[1], u.Corners[2]);
            for (int k = 0; k < 3; ++k)
                if (arr.Orient(t, u.Corners[k], u.Corners[(k + 1) % 3], p) * s < 0)
                    return false;
            return true;
        }


        /// <summary>
        /// Winding number of the mesh just in front of output piece i (on the side its source triangle's normal
        /// points to): signed crossings of a segment from the piece's centroid to a far point in front, without
        /// the triangles of the piece's stack. Exact; a new random direction is used if the segment passes
        /// through an edge or vertex.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        int front_winding(int i, int mesh, TriangleBoxTree tree, Random rng, List<int> stack, int p)
        {
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            List<int> tris = arr.MeshTriangles[mesh];

            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            ExactPoint3 P = arr.Exact(p);
            // outside the box of the mesh: winding number 0
            for (int k = 0; k < 3; ++k)
                if (P[k] < min[k] * P.W || P[k] > max[k] * P.W)
                    return 0;
            BigInteger ext = BigInteger.One;
            for (int k = 0; k < 3; ++k)
                ext = BigInteger.Max(ext, max[k] - min[k] + 1);

            Vector3d pv = arr.ToInputVector(P);
            // pad: the rounding of the point to double (relative 1e-16), with a wide margin
            double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
            // grid coordinates of the point as doubles, for the filtered plane sides
            double gx = grid_coord(P.X, P.W), gy = grid_coord(P.Y, P.W), gz = grid_coord(P.Z, P.W);
            for (int attempt = 0; attempt < 200; ++attempt) {
                int axis = rng.Next(3);
                long[] dir = new long[3];
                for (int k = 0; k < 3; ++k)
                    dir[k] = rng.Next(-1000, 1001);
                dir[axis] = (rng.Next(2) == 0) ? -1000 : 1000;
                BigInteger[] R = new BigInteger[3];
                for (int k = 0; k < 3; ++k)
                    R[k] = (min[k] + max[k]) / 2 + dir[k] * ext * 4;
                ExactPoint3 Rp = ExactPoint3.FromInteger(R[0], R[1], R[2]);
                // the segment must start into the front side of the piece
                BigInteger front = src.NX * (Rp.X * P.W - P.X) + src.NY * (Rp.Y * P.W - P.Y) + src.NZ * (Rp.Z * P.W - P.Z);
                if (front.Sign <= 0)
                    continue;
                Vector3d rv = arr.ToInputVector(Rp);
                double rx = (double)Rp.X, ry = (double)Rp.Y, rz = (double)Rp.Z;
                Vector3d gp = new Vector3d(gx, gy, gz), gr = new Vector3d(rx, ry, rz);

                int winding = 0;
                bool degenerate = false;
                long sides = 0, exactSides = 0;
                tree.QuerySegment(pv, rv, pad, (k) => {
                    if (degenerate) return;
                    int ti = tris[k];
                    var U = arr.Triangles[ti];
                    if (U.Degenerate || ti == ot.Source || (stack != null && stack.Contains(ti))) return;
                    // sides of the plane: in doubles where the error bound decides, else exact
                    sides++;
                    int sr = DisableFilters ? 2 : plane_side_filtered(U.PlaneApprox, rx, ry, rz);
                    if (sr == 2) {
                        sr = (U.P.A * Rp.X + U.P.B * Rp.Y + U.P.C * Rp.Z - U.P.D).Sign;
                        exactSides++;
                    }
                    if (sr == 0) { degenerate = true; return; }
                    sides++;
                    int sp = DisableFilters ? 2 : plane_side_filtered(U.PlaneApprox, gx, gy, gz);
                    if (sp == 2) {
                        sp = (U.P.A * P.X + U.P.B * P.Y + U.P.C * P.Z - U.P.D * P.W).Sign;
                        exactSides++;
                    }
                    // sp == 0: the point is in the plane but not in the triangle (it would be in the stack)
                    if (sp == 0 || sp == sr) return;
                    ExactPoint3 c0 = arr.Exact(U.Corners[0]), c1 = arr.Exact(U.Corners[1]), c2 = arr.Exact(U.Corners[2]);
                    Vector3d d0 = grid_double(c0), d1 = grid_double(c1), d2 = grid_double(c2);
                    int o0 = orient3d(P, Rp, c0, c1, ref gp, ref gr, ref d0, ref d1);
                    int o1 = orient3d(P, Rp, c1, c2, ref gp, ref gr, ref d1, ref d2);
                    int o2 = orient3d(P, Rp, c2, c0, ref gp, ref gr, ref d2, ref d0);
                    if (o0 == 0 || o1 == 0 || o2 == 0) {
                        // the line passes through an edge or vertex: only a problem if it is the triangle's
                        if ((o0 >= 0 && o1 >= 0 && o2 >= 0) || (o0 <= 0 && o1 <= 0 && o2 <= 0))
                            degenerate = true;
                        return;
                    }
                    // leaving through the front side (towards the normal) counts +1, entering -1
                    if (o0 == o1 && o1 == o2)
                        winding += sr;
                });
                System.Threading.Interlocked.Add(ref windingSides, sides);
                System.Threading.Interlocked.Add(ref windingExactSides, exactSides);
                if (degenerate == false)
                    return winding;
            }
            throw new InvalidOperationException("ExactMeshBoolean: no non-degenerate ray found");
        }

        // plane sides evaluated by front_winding, and how many of them exactly (the filter could not decide)
        long windingSides, windingExactSides;

        /// <summary>
        /// sign of A x + B y + C z - D for a plane given by doubles pa[0..3] (each with relative error at most
        /// 2^-52, ExactMeshArrangement.InputTriangle.PlaneApprox) at a point given by doubles (relative error at
        /// most 2^-52 each); 2 if the error bound does not decide (also for overflow or tiny values, and for
        /// coordinates below TinyCoordinate, where the relative error of the rounding is not bounded)
        /// </summary>
        internal static int plane_side_filtered(double[] pa, double x, double y, double z)
        {
            if (tiny(x) || tiny(y) || tiny(z))
                return 2;
            double a = pa[0] * x, b = pa[1] * y, c = pa[2] * z, d = pa[3];
            double mag = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);
            // per term: 2^-52 (coefficient) + 2^-52 (coordinate) + 2^-53 (product); the sum adds 3 * 2^-53 of
            // mag: below 8 * 2^-52 * mag, taken generously
            double bound = 1e-14 * mag;
            if (!(mag < double.MaxValue) || mag < 1e-290)
                return 2;
            double v = a + b + c - d;
            if (v > bound) return 1;
            if (v < -bound) return -1;
            return 2;
        }

        readonly Dictionary<int, DMeshAABBTree3> windingTrees = new Dictionary<int, DMeshAABBTree3>();

        // the tree of an open mesh for its fast winding numbers (built when first needed: not thread-safe)
        DMeshAABBTree3 winding_tree(int mesh)
        {
            DMeshAABBTree3 tree;
            if (windingTrees.TryGetValue(mesh, out tree) == false) {
                windingTrees[mesh] = tree = new DMeshAABBTree3(arr.Meshes[mesh], true);
                tree.FastWindingNumber(Vector3d.Zero);
            }
            return tree;
        }

        /// <summary>
        /// Generalized winding number of an open mesh just in front of output piece i: the fast winding number at
        /// the piece's centroid without the triangles the centroid lies on (they are evaluated directly by the
        /// tree, so subtracting the same terms removes them), each of which adds -1/2 in front (+1/2 behind)
        /// if oriented like the piece, the opposite otherwise.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        double front_generalized_winding(int i, int mesh, List<int> stack)
        {
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            DMesh3 m = arr.Meshes[mesh];
            DMeshAABBTree3 tree = winding_tree(mesh);
            // (the generalized winding number is approximate: the centroid of the corners in doubles is enough)
            Vector3d pv = (arr.PointToVector(ot.A) + arr.PointToVector(ot.B) + arr.PointToVector(ot.C)) / 3;
            double w = tree.FastWindingNumber(pv);
            List<int> on = stack ?? new List<int>() { ot.Source };
            foreach (int ti in on) {
                var U = arr.Triangles[ti];
                if (U.Mesh != mesh || U.Degenerate)
                    continue;
                Index3i t = m.GetTriangle(U.Tid);
                w -= MathUtil.TriSolidAngle(m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c), ref pv) / MathUtil.FourPI;
                BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                w -= (dot.Sign > 0) ? 0.5 : -0.5;
            }
            return w;
        }

        readonly Dictionary<int, BigInteger[][]> bounds = new Dictionary<int, BigInteger[][]>();

        // box of the input coordinates of a mesh (Empty for a mesh without vertices)
        [MethodImpl(JitHints.Optimize)]
        AxisAlignedBox3d input_bounds(int mesh)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (int vid in arr.Meshes[mesh].VertexIndices())
                box.Contain(arr.Meshes[mesh].GetVertex(vid));
            return box;
        }

        [MethodImpl(JitHints.Optimize)]
        void mesh_bounds(int mesh, out BigInteger[] min, out BigInteger[] max)
        {
            BigInteger[][] b;
            if (bounds.TryGetValue(mesh, out b) == false) {
                b = new[] { new BigInteger[3], new BigInteger[3] };
                bool first = true;
                // (from the extreme doubles: the grid map is monotone)
                Vector3d lo = Vector3d.Zero, hi = Vector3d.Zero;
                foreach (int vid in arr.Meshes[mesh].VertexIndices()) {
                    Vector3d v = arr.Meshes[mesh].GetVertex(vid);
                    for (int k = 0; k < 3; ++k) {
                        if (first || v[k] < lo[k]) lo[k] = v[k];
                        if (first || v[k] > hi[k]) hi[k] = v[k];
                    }
                    first = false;
                }
                for (int k = 0; k < 3 && first == false; ++k) {
                    b[0][k] = arr.GridValue(lo[k], k);
                    b[1][k] = arr.GridValue(hi[k], k);
                }
                bounds[mesh] = b;
            }
            min = b[0]; max = b[1];
        }

        // the grid coordinates of the point as doubles (each with relative error at most 2^-52, or NaN, see grid_coord)
        static Vector3d grid_double(ExactPoint3 p)
        {
            if (p.W.IsOne)
                return new Vector3d((double)p.X, (double)p.Y, (double)p.Z);
            return new Vector3d(grid_coord(p.X, p.W), grid_coord(p.Y, p.W), grid_coord(p.Z, p.W));
        }

        /// <summary>
        /// Nonzero coordinates smaller than this are not used by the filters: near the subnormal range the
        /// rounding to double has no relative error bound (it can even flush to 0), and squares in the bounds
        /// underflow. Above it every nonzero difference of two coordinates is at least 2^-352 and its square normal.
        /// </summary>
        internal const double TinyCoordinate = 4.909093465297727e-91;     // 2^-300

        // num / den (den > 0) as double, NaN if it is nonzero but below TinyCoordinate (the filters then decline)
        internal static double grid_coord(BigInteger num, BigInteger den)
        {
            double r = ExactPoint3.ToDouble(num, den, 0);
            return (num.IsZero == false && !(Math.Abs(r) >= TinyCoordinate)) ? double.NaN : r;
        }

        static bool tiny(double v) { return v != 0 && !(Math.Abs(v) >= TinyCoordinate); }

        /// <summary>
        /// orient3d(a, b, c, d) with a floating-point filter: the doubles da..dd approximate the points (relative
        /// error at most 2^-52 per coordinate); exact if the error bound does not decide (or DisableFilters)
        /// </summary>
        int orient3d(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, ref Vector3d da, ref Vector3d db, ref Vector3d dc, ref Vector3d dd)
        {
            PredicateStats.Call(PredicateStats.Kind.WindingOrient3d);
            if (DisableFilters == false) {
                int s = orient3d_filtered(ref da, ref db, ref dc, ref dd);
                if (s != 2)
                    return s;
            }
            PredicateStats.ExactStage(PredicateStats.Kind.WindingOrient3d);
            return orient3d(a, b, c, d);
        }

        /// <summary>
        /// sign of the homogeneous 4x4 determinant of orient3d for points given approximately by doubles (relative
        /// error at most 2^-52 per coordinate, positive weights), or 2 if the error bound does not decide (also for
        /// coordinates below TinyCoordinate, NaN or infinite ones).
        /// det4 [a 1; b 1; c 1; d 1] = -det3 [b-a; c-a; d-a]; the rows u, v, w of the 3x3 determinant are
        /// computed with absolute errors eu, ev, ew (inputs and subtraction), which change the determinant by at
        /// most (|u|+eu)(|v|+ev)(|w|+ew) - |u||v||w| (expanding it multilinearly, Hadamard's inequality for
        /// each term); the evaluation adds at most 1e-15 times the permanent (Shewchuk's orient3d bound).
        /// </summary>
        internal static int orient3d_filtered(ref Vector3d a, ref Vector3d b, ref Vector3d c, ref Vector3d d)
        {
            if (tiny(a.x) || tiny(a.y) || tiny(a.z) || tiny(b.x) || tiny(b.y) || tiny(b.z)
                || tiny(c.x) || tiny(c.y) || tiny(c.z) || tiny(d.x) || tiny(d.y) || tiny(d.z))
                return 2;
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
            double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
            // per coordinate: 2^-52 of each input (relative) and 2^-53 of the difference, taken as 3 * 2^-52 of
            // the sum of the inputs' magnitudes
            const double rel = 3 * 2.220446049250313e-16;
            double ax = Math.Abs(a.x), ay = Math.Abs(a.y), az = Math.Abs(a.z);
            double eu = rel * norm(Math.Abs(b.x) + ax, Math.Abs(b.y) + ay, Math.Abs(b.z) + az);
            double ev = rel * norm(Math.Abs(c.x) + ax, Math.Abs(c.y) + ay, Math.Abs(c.z) + az);
            double ew = rel * norm(Math.Abs(d.x) + ax, Math.Abs(d.y) + ay, Math.Abs(d.z) + az);
            double nu = norm(ux, uy, uz), nv = norm(vx, vy, vz), nw = norm(wx, wy, wz);
            // (nu+eu)(nv+ev)(nw+ew) - nu nv nw, expanded (positive terms, no cancellation)
            double perturb = eu * nv * nw + nu * ev * nw + nu * nv * ew + eu * ev * nw + eu * nv * ew + nu * ev * ew + eu * ev * ew;
            double bound = 1.01 * (perturb + 1e-15 * permanent);
            if (!(bound < double.MaxValue) || !(Math.Abs(det) < double.MaxValue) || permanent < 1e-280)
                return 2;
            if (det > bound) return -1;
            if (det < -bound) return 1;
            return 2;
        }

        // Euclidean norm, rounded up (relative 1e-15)
        static double norm(double x, double y, double z)
        {
            return Math.Sqrt(x * x + y * y + z * z) * (1 + 1e-15);
        }

        // sign of the 4x4 determinant of homogeneous points (consistent orientation, used for crossing tests)
        [MethodImpl(JitHints.Optimize)]
        static int orient3d(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                m[k, 0] = pts[k].X; m[k, 1] = pts[k].Y; m[k, 2] = pts[k].Z; m[k, 3] = pts[k].W;
            }
            return ExactMeshArrangement.Det4(m).Sign;
        }


        /// <summary>
        /// The kept pieces as a mesh. Where more than two pieces meet at an edge (solids touching along the edge),
        /// each piece is paired with its neighbour around the edge on its solid side, and the edge vertices are
        /// duplicated per pair, so that the result is closed and manifold. Likewise vertices where solids touch.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        DMesh3 assemble(bool[] keep, bool[] flip)
        {
            var tris = new List<Index3i>();
            var source = new List<int>();
            var flipped = new List<bool>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (keep[i] == false)
                    continue;
                var ot = arr.Output[i];
                tris.Add(flip[i] ? new Index3i(ot.A, ot.C, ot.B) : new Index3i(ot.A, ot.B, ot.C));
                source.Add(ot.Source);
                flipped.Add(flip[i]);
            }
            // (per point id; the points of the pieces are below PointCount)
            var inputPoints = new bool[arr.PointCount];
            for (int m = 0; m < arr.VertexPoints.Length; ++m)
                foreach (int vid in arr.Meshes[m].VertexIndices())
                    inputPoints[arr.Canonical(arr.VertexPoints[m][vid])] = true;
            if (SimplifyFlatRegions) {
                // new points inside flat regions and on straight edges between them are not needed
                var clock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
                var simplify = new ExactPlanarSimplify(arr, tris, source, flipped);
                simplify.Run(inputPoints);
                tris = simplify.Result();
                RemovedVertices = simplify.Removed;
                clock.Lap("simplify");
            }

            // corner slots (3 per triangle) joined across the edges where the triangles are neighbours
            int[] parent = new int[3 * tris.Count];
            for (int k = 0; k < parent.Length; ++k)
                parent[k] = k;
            Func<int, int> find = (x) => {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            Action<int, int> join = (x, y) => {
                int rx = find(x), ry = find(y);
                if (rx != ry) parent[rx] = ry;
            };
            // slot of point p in triangle t
            Func<int, int, int> slot = (t, p) => 3 * t + ((tris[t].a == p) ? 0 : (tris[t].b == p) ? 1 : 2);

            long[] edgeKeys; int[] edgeTri;
            group_edges(tris.Count, (t) => tris[t], out edgeKeys, out edgeTri);
            var list = new List<int>();
            // the pairs at the edges with more than two pieces
            var nonManifold = new List<KeyValuePair<long, List<KeyValuePair<int, int>>>>();
            for (int g0 = 0, g1 = 0; g0 < edgeKeys.Length; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < edgeKeys.Length && edgeKeys[g1] == edgeKeys[g0]) g1++;
                int u = (int)(edgeKeys[g0] >> 32), v = (int)(edgeKeys[g0] & 0xFFFFFFFF);
                if (g1 - g0 == 2) {
                    // two pieces that run the edge the same way are not neighbours there (see ConflictEdges)
                    if (runs(tris[edgeTri[g0]], u, v) != runs(tris[edgeTri[g0 + 1]], u, v)) {
                        join(slot(edgeTri[g0], u), slot(edgeTri[g0 + 1], u));
                        join(slot(edgeTri[g0], v), slot(edgeTri[g0 + 1], v));
                    } else {
                        ConflictEdges++;
                    }
                    continue;
                }
                list.Clear();
                for (int g = g0; g < g1; ++g)
                    list.Add(edgeTri[g]);
                list.Sort();        // (Array.Sort above is not stable: the first triangle is the reference half-plane)
                var pairs = pair_around_edge(tris, list, u, v);
                foreach (var pair in pairs) {
                    join(slot(pair.Key, u), slot(pair.Value, u));
                    join(slot(pair.Key, v), slot(pair.Value, v));
                }
                // (pieces left without a neighbour: an odd number, or more running one way than the other)
                if (g1 - g0 > 2 && 2 * pairs.Count < g1 - g0)
                    ConflictEdges++;
                if (g1 - g0 > 2) {
                    NonManifoldEdges++;
                    nonManifold.Add(new KeyValuePair<long, List<KeyValuePair<int, int>>>(edgeKeys[g0], pairs));
                }
            }

            // coordinates of the points used (rounded from the exact points, in parallel)
            var used = new List<int>();
            var usedIndex = new int[inputPoints.Length];
            for (int k = 0; k < usedIndex.Length; ++k)
                usedIndex[k] = -1;
            foreach (Index3i t in tris)
                for (int k = 0; k < 3; ++k)
                    if (usedIndex[t[k]] < 0) {
                        usedIndex[t[k]] = used.Count;
                        used.Add(t[k]);
                    }
            Vector3d[] coords = arr.PointsToVectors(used);

            // vertices: one per class of corner slots
            DMesh3 result = new DMesh3();
            // per vertex: its point, or for the split vertices -2 - their index into splitPoints (the edge's ends)
            var vertexPoint = new List<int>();
            var splitPoints = new List<Index2i>();
            // (vertex of each class of corner slots, by its root slot; -1 before it has one)
            int[] vmap = new int[3 * tris.Count];
            for (int k = 0; k < vmap.Length; ++k)
                vmap[k] = -1;
            int[] vids = new int[3 * tris.Count];
            for (int t = 0; t < tris.Count; ++t) {
                for (int k = 0; k < 3; ++k) {
                    int root = find(3 * t + k), vid = vmap[root];
                    if (vid < 0) {
                        vid = result.AppendVertex(coords[usedIndex[tris[t][k]]]);
                        vertexPoint.Add(tris[t][k]);
                        vmap[root] = vid;
                    }
                    vids[3 * t + k] = vid;
                }
            }

            // where the solid touches itself along an edge and its surface is connected around both ends, the pairs
            // at the edge get the same two vertices (the ends cannot be duplicated), which would give an edge with
            // more than two triangles: then the edge is split for each pair at a new vertex of its own (at the
            // midpoint, the same position for all pairs, like other contacts), so that every edge has two triangles
            var splitAt = new Dictionary<int, int>();       // slot 3 t + k (edge from corner k to k + 1) -> vertex
            // in a deterministic order (the new vertices are numbered in it): the edges and their pairs were found in
            // the order of point ids, which depends on the thread schedule; by the slot of the edge in its first
            // triangle, the pairs by their first triangle
            var nmOrder = new List<KeyValuePair<int, int>>();      // (sort key, index into nonManifold)
            for (int i = 0; i < nonManifold.Count; ++i) {
                if (nonManifold[i].Value.Count == 0)
                    continue;       // (no pairs: all pieces run the same way, nothing to split)
                int u = (int)(nonManifold[i].Key >> 32), v = (int)(nonManifold[i].Key & 0xFFFFFFFF);
                int t0 = int.MaxValue;
                foreach (var pair in nonManifold[i].Value)
                    t0 = Math.Min(t0, Math.Min(pair.Key, pair.Value));
                int ku = slot(t0, u) - 3 * t0, kv = slot(t0, v) - 3 * t0;
                nmOrder.Add(new KeyValuePair<int, int>(3 * t0 + (((ku + 1) % 3 == kv) ? ku : kv), i));
                nonManifold[i].Value.Sort((x, y) => Math.Min(x.Key, x.Value).CompareTo(Math.Min(y.Key, y.Value)));
            }
            nmOrder.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (var ord in nmOrder) {
                var nm = nonManifold[ord.Value];
                int u = (int)(nm.Key >> 32), v = (int)(nm.Key & 0xFFFFFFFF);
                var byVertices = new Dictionary<long, List<KeyValuePair<int, int>>>(EdgeKeyComparer.Instance);
                var groups = new List<List<KeyValuePair<int, int>>>();      // (in the order of their first pair)
                foreach (var pair in nm.Value) {
                    long key = ExactMeshArrangement.EdgeKey(vids[slot(pair.Key, u)], vids[slot(pair.Key, v)]);
                    List<KeyValuePair<int, int>> group;
                    if (byVertices.TryGetValue(key, out group) == false) {
                        byVertices[key] = group = new List<KeyValuePair<int, int>>();
                        groups.Add(group);
                    }
                    group.Add(pair);
                }
                foreach (var group in groups) {
                    if (group.Count < 2)
                        continue;
                    SplitNonManifoldEdges++;
                    foreach (var pair in group) {
                        Vector3d mid = 0.5 * (coords[usedIndex[u]] + coords[usedIndex[v]]);
                        int m = result.AppendVertex(mid);
                        vertexPoint.Add(-2 - splitPoints.Count);
                        splitPoints.Add(new Index2i(u, v));
                        foreach (int t in new[] { pair.Key, pair.Value }) {
                            int ku = slot(t, u) - 3 * t, kv = slot(t, v) - 3 * t;
                            int k = ((ku + 1) % 3 == kv) ? ku : kv;     // the edge (u, v) or (v, u) of t
                            splitAt[3 * t + k] = m;
                        }
                    }
                }
            }

            // Without edges of more than two pieces and without two pieces running an edge the same way, no triangle runs
            // an edge of the mesh the same way as the triangle already there (the vertices of an edge are those of
            // one pair of pieces), so append_triangle's checks before AppendTriangle cannot fail: skipped
            bool simple = NonManifoldEdges == 0 && ConflictEdges == 0;
            var poly = new List<int>();
            for (int t = 0; t < tris.Count; ++t) {
                poly.Clear();
                int first = -1;
                for (int k = 0; k < 3; ++k) {
                    poly.Add(vids[3 * t + k]);
                    int m;
                    if (splitAt.TryGetValue(3 * t + k, out m)) {
                        if (first < 0) first = poly.Count;
                        poly.Add(m);
                    }
                }
                if (first < 0) {
                    if (simple == false || result.AppendTriangle(poly[0], poly[1], poly[2]) < 0)
                        append_triangle(result, poly[0], poly[1], poly[2], vertexPoint);
                    continue;
                }
                // fan from a split vertex: it is on one edge, so no fan triangle is degenerate
                int n = poly.Count;
                for (int j = 1; j + 1 < n; ++j)
                    append_triangle(result, poly[first], poly[(first + j) % n], poly[(first + j + 1) % n], vertexPoint);
            }

            // input vertices (kept by SafeRounding where possible) and exact coordinates
            resultFixed = new bool[vertexPoint.Count];
            for (int vid = 0; vid < vertexPoint.Count; ++vid)
                resultFixed[vid] = vertexPoint[vid] >= 0 && inputPoints[vertexPoint[vid]];
            ResultExactVertices = null;
            if (ComputeExactVertices) {
                var exact = new ExactPoint3[vertexPoint.Count];
                for (int vid = 0; vid < vertexPoint.Count; ++vid) {
                    if (vertexPoint[vid] >= 0) {
                        exact[vid] = arr.ToInputExact(arr.Exact(vertexPoint[vid]));
                    } else {
                        Index2i uv = splitPoints[-2 - vertexPoint[vid]];
                        ExactPoint3 pu = arr.ToInputExact(arr.Exact(uv.a)), pv = arr.ToInputExact(arr.Exact(uv.b));
                        exact[vid] = ExactPoint3.Create(pu.X * pv.W + pv.X * pu.W, pu.Y * pv.W + pv.Y * pu.W, pu.Z * pv.W + pv.Z * pu.W, 2 * pu.W * pv.W);
                    }
                }
                ResultExactVertices = exact;
            }
            return result;
        }

        // per vertex of the last assembled result: is it an input vertex
        bool[] resultFixed;

        [MethodImpl(JitHints.Optimize)]
        void append_triangle(DMesh3 result, int a, int b, int c, List<int> vertexPoint)
        {
            // (a triangle that would run an edge of the mesh the same way as the triangle there is not added there
            // either: the pieces' corners can be joined around both ends of an edge where they are not neighbours)
            if (runs_existing(result, a, b) == false && runs_existing(result, b, c) == false && runs_existing(result, c, a) == false
                && result.AppendTriangle(a, b, c) >= 0)
                return;
            // (an edge with more than two triangles that could not be split, or one that the triangle would run the
            // same way as the triangle there: kept, with copies of as few of its vertices as possible, so that it keeps
            // the neighbours it can have and no vertex is left without triangles)
            NonManifoldTriangles++;
            int[] f = { a, b, c };
            foreach (int mask in new[] { 1, 2, 4, 3, 5, 6, 7 }) {
                bool ok = true;
                for (int k = 0; k < 3 && ok; ++k) {
                    int k1 = (k + 1) % 3;
                    if ((mask & (1 << k)) == 0 && (mask & (1 << k1)) == 0)
                        ok = can_add_edge(result, f[k], f[k1]);
                }
                if (ok == false)
                    continue;
                int[] g = new int[3];
                for (int k = 0; k < 3; ++k) {
                    g[k] = f[k];
                    if ((mask & (1 << k)) != 0) {
                        g[k] = result.AppendVertex(result.GetVertex(f[k]));
                        vertexPoint.Add(vertexPoint[f[k]]);
                    }
                }
                result.AppendTriangle(g[0], g[1], g[2]);
                return;
            }
        }

        // a triangle can run a -> b: no edge (a, b) yet, or one with one triangle that runs it b -> a
        static bool can_add_edge(DMesh3 mesh, int a, int b)
        {
            int eid = mesh.FindEdge(a, b);
            return eid == DMesh3.InvalidID || (mesh.IsBoundaryEdge(eid) && runs_existing(mesh, a, b) == false);
        }

        // the edge (a, b) of the mesh exists and a triangle at it runs a -> b
        static bool runs_existing(DMesh3 mesh, int a, int b)
        {
            int eid = mesh.FindEdge(a, b);
            if (eid == DMesh3.InvalidID)
                return false;
            Index2i et = mesh.GetEdgeT(eid);
            return runs(mesh.GetTriangle(et.a), a, b) || (et.b != DMesh3.InvalidID && runs(mesh.GetTriangle(et.b), a, b));
        }

        // pairs of triangles that are neighbours across edge (u, v): for two triangles the pair, for more each
        // triangle with the next one around the edge on its back (solid) side, which runs the other way
        [MethodImpl(JitHints.Optimize)]
        List<KeyValuePair<int, int>> pair_around_edge(List<Index3i> tris, List<int> list, int u, int v)
        {
            var pairs = new List<KeyValuePair<int, int>>();
            if (list.Count < 2)
                return pairs;
            if (list.Count == 2) {
                pairs.Add(new KeyValuePair<int, int>(list[0], list[1]));
                return pairs;
            }
            // the triangles' half-planes in the order of increasing angle around the axis u -> v (exact: half-planes
            // can differ by less than the rounding of the points), from the half-plane of the first triangle
            // the axis from the smaller point to the larger one by ComparePoints, not by id (ids depend on the thread
            // schedule; the order of equal half-planes, by triangle, would not reverse with the axis)
            if (arr.ComparePoints(u, v) > 0) { int tmp = u; u = v; v = tmp; }
            ExactPoint3 U = arr.Exact(u), V = arr.Exact(v);
            BigInteger[] axis = scaled_difference(V, U);
            var dir = new Dictionary<int, BigInteger[]>();
            var half = new Dictionary<int, int>();
            BigInteger[] r = null, y = null;
            foreach (int t in list) {
                Index3i f = tris[t];
                BigInteger[] d = scaled_difference(arr.Exact(f.a + f.b + f.c - u - v), U);
                if (r == null) {
                    r = d;
                    y = cross(axis, r);
                }
                dir[t] = d;
                // 0: the first half-plane, 1: angle in (0, pi), 2: pi, 3: (pi, 2 pi)
                int s = dot(y, d).Sign;
                half[t] = (s > 0) ? 1 : (s < 0) ? 3 : (dot(cross(axis, d), y).Sign > 0) ? 0 : 2;
            }
            var sorted = new List<int>(list);
            sorted.Sort((a, b) => {
                int c = half[a].CompareTo(half[b]);
                if (c == 0 && (half[a] & 1) == 1)
                    c = -dot(cross(axis, dir[a]), dir[b]).Sign;      // b at a larger angle than a: positive
                return (c != 0) ? c : a.CompareTo(b);
            });
            // a triangle running u -> v has its normal towards increasing angle: its solid is at decreasing angle
            var used = new HashSet<int>();
            int n = sorted.Count;
            for (int k = 0; k < n; ++k) {
                int t = sorted[k];
                if (runs(tris[t], u, v) == false || used.Contains(t))
                    continue;
                int next = sorted[(k - 1 + n) % n];
                if (runs(tris[next], v, u) && used.Contains(next) == false) {
                    pairs.Add(new KeyValuePair<int, int>(t, next));
                    used.Add(t); used.Add(next);
                }
            }
            return pairs;
        }

        // p - q times the positive factor p.W q.W
        static BigInteger[] scaled_difference(ExactPoint3 p, ExactPoint3 q)
        {
            return new[] { p.X * q.W - q.X * p.W, p.Y * q.W - q.Y * p.W, p.Z * q.W - q.Z * p.W };
        }

        static BigInteger[] cross(BigInteger[] a, BigInteger[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        static BigInteger dot(BigInteger[] a, BigInteger[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        static bool runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }
    }
}
