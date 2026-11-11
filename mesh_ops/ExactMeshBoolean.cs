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
    /// solid where it is above 0.5, evaluated at every output piece (it varies within a patch: see Performance of open
    /// inputs below); then the result is generally not closed, also along edges where an open boundary
    /// of an input meets other pieces and the kept pieces run the edge the same way (ConflictEdges). An inside-out mesh (winding
    /// number -1) has an empty solid. A mesh with inconsistently oriented triangles (flipped faces) is first
    /// oriented consistently per component (triangles connected across edges with two triangles, by vertex
    /// position), keeping the orientation of the larger part by area (ReorientedTriangles); components that
    /// cannot be oriented are left as they are. Where solids touch along an edge, the result is
    /// non-manifold there when welded by position, by design (as in Manifold): the triangles at such an edge are split
    /// into manifold pairs with copies of its vertices (NonManifoldEdges); Contacts counts the positions with two or more
    /// vertices, so that a caller that welds (eg writes STL) knows. Two different exact points that round to one double
    /// would be such a contact as well, one the exact solid does not have: SafeRounding separates them (see
    /// MeshRoundingRepair.InitialFalseContacts) and RoundingClean requires that none is left. Degenerate (zero-area) input
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
    /// coordinates, open meshes when AllowOpenMeshes is false, or a result that is not clean with StrictRounding; it throws InvalidOperationException in the
    /// (not expected) case that no non-degenerate ray is found. Result has no normals, colors, UVs or groups.
    /// Compute() can be called again after changing the meshes or the operation.
    ///
    /// Cost: exact predicates behind floating-point filters (BigInteger only where the filters cannot decide);
    /// slower than floating-point methods, the phases run in parallel.
    ///
    /// Performance of open inputs: an open mesh's generalized winding number is evaluated at every output piece (one
    /// fast-winding query per piece and open mesh), not once per patch as the exact winding numbers of closed meshes;
    /// on the open Thingi10K cohort of docs/design/LEVY_CLASSIFICATION.md (21 models, 63 operations) that costs 4.3
    /// times the total time of the per-patch evaluation (classification 18 times; up to 10 times on single
    /// operations, about 100 s). Closed inputs are not affected.
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
            NotComputed = 3,
            /// <summary>
            /// StrictRounding: the rounded result is not clean within the relative bound (see Error: the defects
            /// left, where, and the drift the smallest rejected edit needed); Result is null
            /// </summary>
            RoundingNotClean = 4
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
        /// 1e-12 of the squared diagonal; the diagonal of the result's largest connected component, here and below) the
        /// rounding left, by local edits checked with exact predicates on the doubles
        /// (MeshRoundingRepair: contractions of short edges, flips, tiny vertex moves, removal of closed components that
        /// keep a sliver and are tiny or thinner than RoundingMaxDisplacement (1e-5) of the diagonal, counted in Rounding.RemovedComponents;
        /// a thin one that holds real volume for its area is kept and counted in Rounding.ScaleDisparity, not clean). Input vertices are kept
        /// where possible, and sheets that touch at duplicated vertices still touch, at the same points; vertices of
        /// different exact points that rounded to one double (a contact the rounding made, not one of the exact solid)
        /// are moved apart by a few units in the last place where that adds no defect (Rounding.InitialFalseContacts,
        /// RemainingFalseContacts; Contacts counts the positions with several vertices left). Not every case
        /// can be repaired: Compute() still succeeds (Status Success) if defects remain (unless StrictRounding); RoundingClean tells whether the
        /// result is verified free of both, Rounding has the counts. On by default (since October 2026; before, off):
        /// on the Thingi10K benchmark it makes about 1.6 times as many results clean, for about 1.2 to 1.9 times the time
        /// of the boolean (benchmarks/MeshRepairBenchmark README). Off, the new vertices are only rounded to the nearest
        /// double.
        /// </summary>
        public bool SafeRounding = true;
        /// <summary>
        /// SafeRounding's tolerance far from the origin (MeshRoundingRepair.UlpFactor, AbsoluteTolerance): beyond
        /// RoundingMaxDisplacement of the diagonal, up to this many units in the last place of the largest coordinate, or this absolute
        /// distance (docs/design/SAFE_ROUNDING.md section 5)
        /// </summary>
        public double RoundingUlpFactor = 256, RoundingAbsoluteTolerance = 0;
        /// <summary>
        /// SafeRounding's relative bound (MeshRoundingRepair.MaxDisplacement): the longest edge contracted and the
        /// farthest the surface moves, relative to the diagonal of the largest connected component (not of the
        /// bounding box of all: a small part far away does not widen it). (A larger value also switches off sooner
        /// what applies where the coordinates are coarse: there the ulp term must exceed this one)
        /// </summary>
        public double RoundingMaxDisplacement = 1e-5;
        /// <summary>
        /// SafeRounding's effort budget (MeshRoundingRepair.MaxPairTests): no edit is evaluated once the exact triangle
        /// pair tests of its evaluations exceed it. Past it, the repair stops editing (Rounding.EvaluationLimitReached) and the result is valid with the
        /// defects left counted; it bounds the time on inputs it cannot repair. Default 5M
        /// </summary>
        public long RoundingMaxPairTests = 5000000;
        /// <summary>
        /// SafeRounding within the relative bound only (no ulp or absolute term); if its result is not clean, Compute()
        /// fails with Status RoundingNotClean instead of returning a result with defects (Result null; Rounding and
        /// ResultExactVertices stay set, for the diagnosis). Off by default. Not strict, where the coordinates are coarse
        /// (see RoundingUlpFactor), input vertices may move within the bound like the others.
        /// </summary>
        public bool StrictRounding = false;
        /// <summary> the repair run by SafeRounding (its counts), or null </summary>
        public MeshRoundingRepair Rounding;
        /// <summary>
        /// SafeRounding ran and its result has no self-intersections, no degenerate triangles and no contacts made by the
        /// rounding (exact check; contacts at duplicated vertices of one exact point, where solids touch, are not
        /// intersections; vertices of different exact points that the rounding put at one position are, until the repair
        /// separates them). False with Status Success means that defects remain (Rounding.RemainingIntersections,
        /// RemainingDegenerate, RemainingFalseContacts).
        /// </summary>
        public bool RoundingClean { get { return Rounding != null && Rounding.IsClean; } }

        /// <summary>
        /// positions in Result where two or more vertices lie: where the solid touches itself (at a point, or along an edge
        /// split into manifold pairs, see the class comment), plus contacts the rounding made and SafeRounding left
        /// (Rounding.RemainingFalseContacts). Result is manifold with its duplicated vertices; welded by position (eg
        /// written to STL and read back) it is not manifold there. 0 for a result without contacts
        /// </summary>
        public int Contacts;
        /// <summary>
        /// compute ResultExactVertices: the exact coordinates of the result's vertices, before they are rounded
        /// </summary>
        public bool ComputeExactVertices = false;
        /// <summary>
        /// if ComputeExactVertices: per vertex id of Result, its exact (rational) coordinates in the input's
        /// coordinate system; Result's coordinates are these rounded to the nearest double, except the vertices that split
        /// edges where solids touch: their entries are the exact midpoints of the edges, their coordinates the midpoints
        /// of the rounded ends (which can differ from the rounded exact midpoint by an ulp). With SafeRounding (the default)
        /// the entries are those before the repair, for the vertex ids that remain: the repair moves some vertices by tiny
        /// amounts, so that their entries no longer round to Result's coordinates. Set SafeRounding = false where the
        /// rounded exact coordinates are needed.
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
        // open meshes with fewer boundary segments than this are classified by the Antipodal Method (an exact ray and a
        // sum over the boundary segments, Martens, Trettner, Bessmeltsev 2026), the others by fast winding numbers
        // (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md section 5.1: the threshold where both cost about the same); for tests
        internal int AntipodalMaxBoundarySegments = 1000;
        // and only open meshes with fewer input triangles than this: the exact ray's cost grows with the mesh (the segment
        // query through it), the fast winding number's about with its logarithm; on the A/B of the 21 open models
        // (benchmarks/BooleanComparison/perf, #539) the Antipodal Method won up to 68k triangles per mesh (0.22-0.77 of
        // the classification time) and lost from 112k (1.05-1.42) and at 1.56M (1.30); for tests
        internal int AntipodalMaxTriangles = 100000;
        // the early-out of Difference and Intersection (T3a; active zones, Rossignac and Voelcker 1989): their result lies
        // inside the first mesh, so where it is closed, triangles of the other meshes wholly outside it are culled from the
        // arrangement (ExactMeshArrangement.CullOutsideOf; the same exact solid); for tests
        internal bool CullOutsideA = true;
        // for tests and benchmarks: per input mesh after Compute, the number of its boundary segments (BoundarySegments),
        // -1 for a mesh with exact winding numbers, -2 for an open mesh with AntipodalMaxTriangles triangles or more (not
        // counted: it goes to fast winding numbers whatever its boundary)
        internal int[] OpenBoundarySegments;
        // for tests and benchmarks: keep, per arrangement piece (Arrangement.Output), the thresholded winding numbers of the
        // open meshes in front of it (pairs mesh, value; null for a piece that is not a representative), in
        // OpenWindingPerPiece after Compute
        internal bool RecordOpenWinding = false;
        internal int[][] OpenWindingPerPiece;
        // for tests: triangles per batch of the arrangement's candidate pair search
        internal long CandidateBatchSize = 1 << 24;
        // for tests: no floating-point filters in the arrangement
        internal bool DisableFilters = false;
        // for tests: all exact points of the arrangement as BigInteger (ExactMeshArrangement.FixedWidthPoints)
        internal bool FixedWidthPoints = true;
        // for tests: the arrangement of the last Compute
        internal ExactMeshArrangement Arrangement { get { return arr; } }
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
        [MethodImpl(JitHints.Optimize)]
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
        /// input is null, has non-finite coordinates, or is open while AllowOpenMeshes is false, or if the result is not
        /// clean with StrictRounding (Status RoundingNotClean); otherwise true
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
            SplitBowtieVertices = 0;
            ReorientedTriangles = Contacts = 0;
            ExactWindingA = ExactWindingB = false;
            ExactWinding = null;
            PhaseMilliseconds = new Dictionary<string, long>();
            PhaseAllocatedBytes = new Dictionary<string, long>();
            Counts = new Dictionary<string, long>();
            var inputClock = new PhaseClock(PhaseMilliseconds, PhaseAllocatedBytes);
            arr = null;
            bounds.Clear();
            windingTrees.Clear();
            antipodalSegments.Clear();
            antipodalFar.Clear();
            OpenWindingPerPiece = null;
            antipodalUndecided = 0;
            antipodalSnapped = 0;
            OpenBoundarySegments = null;
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
            arr = new ExactMeshArrangement(inputs) { SelfIntersections = true, DisableFilters = DisableFilters, CandidateBatchSize = CandidateBatchSize,
                FixedWidthPoints = FixedWidthPoints };
            // (the built-in Difference and Intersection only: a WindingPredicate may keep points outside the first mesh;
            // the first mesh closed, so that the exact ray decides inside and outside)
            if (CullOutsideA && WindingPredicate == null && (Operation == Operations.Difference || Operation == Operations.Intersection)
                && inputs.Count > 1 && ExactWinding[0]) {
                arr.CullOutsideOf = 0;
                arr.CullPrepare = () => { BigInteger[] min, max; mesh_bounds(0, out min, out max); };
                arr.CullTest = cull_outside_a;
            }
            arr.Compute();
            IntersectingPairs = arr.IntersectingPairs - arr.SelfIntersectingPairs;
            SelfIntersectingPairs = arr.SelfIntersectingPairs;
            foreach (var kv in arr.PhaseMilliseconds)
                PhaseMilliseconds[kv.Key] = kv.Value;
            foreach (var kv in arr.PhaseAllocatedBytes)
                PhaseAllocatedBytes[kv.Key] = kv.Value;
            Counts["candidate_pairs"] = arr.CandidatePairs;
            Counts["t3a_culled"] = arr.CulledCount;
            Counts["t3a_cull_tests"] = arr.CullTests;
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
            Counts["antipodal_meshes"] = antipodalSegments.Count;
            Counts["antipodal_undecided"] = antipodalUndecided;
            Counts["antipodal_snapped"] = antipodalSnapped;
            Result = assemble(keep, flip);
            clock.Lap("assembly");
            Counts["removed_vertices"] = RemovedVertices;
            if (SafeRounding) {
                bool[] fixedV = resultFixed;
                int[] pointId = resultPointId;
                Rounding = new MeshRoundingRepair(Result) {
                    SharedPositionsTouch = true, FixedVertexF = (v) => v < fixedV.Length && fixedV[v], FixedWhereCoarse = false,
                    PointIdF = (v) => v < pointId.Length ? pointId[v] : -1,
                    // (the input points are doubles, so the rounding leaves them where they are: the triangles with three
                    // of them are exact triangles of the arrangement)
                    ExactVertexF = (v) => v < fixedV.Length && fixedV[v],
                    UlpFactor = StrictRounding ? 0 : RoundingUlpFactor, AbsoluteTolerance = StrictRounding ? 0 : RoundingAbsoluteTolerance,
                    MaxDisplacement = RoundingMaxDisplacement, MaxPairTests = RoundingMaxPairTests
                };
                Rounding.Apply();
                clock.Lap("rounding");
                Counts["rounding_intersections"] = Rounding.InitialIntersections;
                Counts["rounding_degenerate"] = Rounding.InitialDegenerate;
                Counts["rounding_edits"] = Rounding.Collapses + Rounding.Flips + Rounding.Moves + Rounding.Zips + Rounding.VertexRemovals;
                Counts["rounding_removed_triangles"] = Rounding.RemovedTriangles;
                Counts["rounding_removed_components"] = Rounding.RemovedComponents;
                Counts["rounding_vertex_removals"] = Rounding.VertexRemovals;
                Counts["rounding_false_contacts"] = Rounding.InitialFalseContacts;
                Counts["rounding_scale_disparity"] = Rounding.ScaleDisparity;
                Counts["rounding_scale_not_finite"] = Rounding.ScaleNotFinite ? 1 : 0;
                if (StrictRounding && Rounding.IsClean == false) {
                    Error = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "rounding not clean within the bound {0:G3}: {1} intersecting pairs, {2} degenerate triangles, {7} contacts made by the rounding left, eg at {3:R} {4:R} {5:R}; the smallest edit rejected by the bound needed {6:G3}; {8} components kept that the bound took as thin (scale disparity){9}",
                        Rounding.MaxLength, Rounding.RemainingIntersections, Rounding.RemainingDegenerate,
                        Rounding.RemainingDefectAt.x, Rounding.RemainingDefectAt.y, Rounding.RemainingDefectAt.z, Rounding.SmallestRejectedDrift,
                        Rounding.RemainingFalseContacts, Rounding.ScaleDisparity,
                        Rounding.ScaleNotFinite ? "; the scale is not finite (a diagonal overflows), every edit rejected" : "");
                    Result = null;
                    Status = BooleanStatus.RoundingNotClean;
                    return false;
                }
            }
            Contacts = count_contacts(Result);
            Counts["contacts"] = Contacts;
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

        [MethodImpl(JitHints.Optimize)]
        bool fail(BooleanStatus status, string error)
        {
            Status = status;
            Error = error;
            return false;
        }


        /// <summary>
        /// The boundary of the solid of a mesh (where its winding number is positive): self-intersections,
        /// nested shells and duplicated faces are resolved exactly, the result with SafeRounding (the default).
        /// Returns null if Compute() fails.
        /// </summary>
        public static DMesh3 Resolve(DMesh3 mesh)
        {
            return Resolve(mesh, true);
        }

        /// <summary> as Resolve(mesh), with SafeRounding of the result if safeRounding </summary>
        [MethodImpl(JitHints.Optimize)]
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
            gParallel.ForRange(n, (i) => representative[i] = coplanar_stack(output[i], out stack[i]));

            // patches: pieces of one mesh connected across edges that are not on an intersection curve, with
            // exactly two pieces of that mesh (then they are oriented consistently, the meshes are winding
            // closed); the winding numbers of the closed meshes are the same on the front sides of all pieces of a patch
            long[] edgeKeys; int[] edgeTri;
            group_edges(n, (i) => new Index3i(output[i].A, output[i].B, output[i].C), out edgeKeys, out edgeTri);
            int[] parent = new int[n];
            for (int i = 0; i < n; ++i)
                parent[i] = i;
            Func<int, int> find = (x) => find_root(parent, x);
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
            }
            // the open meshes with few boundary segments and triangles are evaluated by the Antipodal Method (its segments and the box
            // the exact ray needs, computed here: the parallel evaluation only reads), the others by fast winding numbers
            OpenBoundarySegments = new int[nm];
            for (int m = 0; m < nm; ++m) {
                OpenBoundarySegments[m] = -1;
                if (ExactWinding[m])
                    continue;
                if (arr.Meshes[m].TriangleCount >= AntipodalMaxTriangles) {
                    OpenBoundarySegments[m] = -2;
                    continue;
                }
                Vector3d[] segments = BoundarySegments(arr.Meshes[m]);
                OpenBoundarySegments[m] = segments.Length / 2;
                if (segments.Length / 2 < AntipodalMaxBoundarySegments) {
                    antipodalSegments[m] = antipodal_grid(segments);
                    antipodalFar[m] = far_points(m, 64);
                }
            }
            // the fast-winding trees of the other open meshes, concurrently (each is independent, built from its own
            // mesh; the dictionary is filled serially afterwards, in mesh order)
            var built = new DMeshAABBTree3[openMeshes.Count];
            gParallel.ForRange(openMeshes.Count, (k) => {
                if (windingTrees.ContainsKey(openMeshes[k]) == false && antipodalSegments.ContainsKey(openMeshes[k]) == false)
                    built[k] = new_winding_tree(openMeshes[k]);
            });
            for (int k = 0; k < openMeshes.Count; ++k)
                if (built[k] != null)
                    windingTrees[openMeshes[k]] = built[k];
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
            // (the closed meshes per patch: their winding numbers are integers, the same in front of every piece of a
            // patch)
            var noOpen = new List<int>();
            gParallel.ForRange(firsts.Count, () => new List<int>(), (k, candidates) => {
                int i = firsts[k];
                patchWinding[find_const(parent, i)] = patch_winding(i, stack[i], closedTree, noOpen, candidates);
            });
            // the open meshes per piece: their generalized winding numbers vary continuously, also within a patch (a
            // patch of one mesh can reach from inside an open mesh to outside it through a hole of that mesh, around the
            // end of an intersection curve that the hole cuts off), so the threshold at 1/2 is taken at every piece
            int[][] openWinding = null;
            if (openMeshes.Count > 0) {
                openWinding = new int[n][];
                gParallel.ForRange(n, (i) => {
                    if (representative[i])
                        openWinding[i] = open_winding(i, stack[i], openMeshes);
                });
            }
            OpenWindingPerPiece = RecordOpenWinding ? openWinding : null;

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
                if (openWinding != null) {
                    w = openWinding[i];
                    for (int k = 0; k < w.Length; k += 2)
                        add(w[k], w[k + 1]);
                }
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

        // the stack of piece ot (null if its source has no coplanar triangles that contain it), and whether the
        // piece is output from it (its source is the stack's first triangle)
        [MethodImpl(JitHints.Optimize)]
        bool coplanar_stack(ExactMeshArrangement.OutputTriangle ot, out List<int> stack)
        {
            stack = null;
            var src = arr.Triangles[ot.Source];
            if (src.Coplanar.Count == 0)
                return true;
            bool representative = true;
            int c = centroid(ot);
            foreach (int ui in src.Coplanar) {
                if (inside_coplanar(src, arr.Triangles[ui], c)) {
                    if (stack == null)
                        stack = new List<int>() { ot.Source };
                    stack.Add(ui);
                    if (ui < ot.Source)
                        representative = false;
                }
            }
            return representative;
        }

        static readonly int[] noWinding = new int[0];

        // the root of x in the union-find forest parent, without changing it (thread-safe for reading)
        [MethodImpl(JitHints.Optimize)]
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
            // (the exact point and its doubles once per patch, not per candidate mesh)
            var center = new Center(arr.Exact(p));
            center.Input = arr.ToInputVector(center.P);
            Vector3d pv = center.Input;
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
                    wm = front_winding(i, m, arr.Trees[m], rng, stack, center);
                } else {
                    wm = threshold_generalized(front_generalized_winding(i, m, stack));
                }
                if (wm != 0) {
                    sparse.Add(m);
                    sparse.Add(wm);
                }
            }
            return sparse.ToArray();
        }

        // the nonzero thresholded generalized winding numbers of the open meshes in front of piece i (pairs mesh,
        // winding number), as patch_winding takes them
        [MethodImpl(JitHints.Optimize)]
        int[] open_winding(int i, List<int> stack, List<int> openMeshes)
        {
            List<int> sparse = null;
            Center center = null;
            foreach (int m in openMeshes) {
                Vector3d[] segments;
                double w;
                if (antipodalSegments.TryGetValue(m, out segments)) {
                    // (the exact centroid once per piece, for all meshes on this path)
                    if (center == null) {
                        center = new Center(centroid_point(arr.Output[i], false));
                        center.Input = arr.ToInputVector(center.P);
                    }
                    w = antipodal_front_winding(i, m, stack, segments, center);
                } else
                    w = front_generalized_winding(i, m, stack);
                int wm = threshold_generalized(w);
                if (wm != 0) {
                    if (sparse == null)
                        sparse = new List<int>();
                    sparse.Add(m);
                    sparse.Add(wm);
                }
            }
            return sparse == null ? noWinding : sparse.ToArray();
        }

        // generalized winding numbers are thresholded at 1/2: ceil(w - 1/2) > 0 iff w > 1/2, and it commutes with adding
        // the integer changes across a piece (clamped, so that adding the changes cannot overflow; NaN gives 0)
        static int threshold_generalized(double w)
        {
            double g = Math.Ceiling(w - 0.5);
            return (g > 1e6) ? 1000000 : (g < -1e6) ? -1000000 : double.IsNaN(g) ? 0 : (int)g;
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
            sort_by_key(edgeKeys, edgeTri);
        }

        // the root of x in the union-find forest parent, halving the path
        static int find_root(int[] parent, int x)
        {
            while (parent[x] != x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        // the corner slot 3 t + k of point p in triangle t
        static int slot_of(List<Index3i> tris, int t, int p)
        {
            Index3i f = tris[t];
            return 3 * t + ((f.a == p) ? 0 : (f.b == p) ? 1 : 2);
        }

        /// <summary>
        /// sorts keys (non-negative) and items by the keys, stably (equal keys keep the order of their items): a radix
        /// sort by 11-bit digits (a small count array), skipping the digits that are the same in all keys. (Array.Sort of two arrays is
        /// not stable, and the runtime's generic sort over these types starts as unoptimized code.)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void sort_by_key(long[] keysIn, int[] itemsIn)
        {
            long[] keys = keysIn;
            int[] items = itemsIn;
            int n = keys.Length;
            long[] k2 = new long[n];
            int[] i2 = new int[n];
            const int Bits = 11, Digits = 1 << Bits, Mask = Digits - 1;
            var count = new int[Digits + 1];
            for (int shift = 0; shift < 64; shift += Bits) {
                Array.Clear(count, 0, count.Length);
                for (int i = 0; i < n; ++i)
                    count[(int)((keys[i] >> shift) & Mask) + 1]++;
                bool constant = false;
                for (int d = 0; d < Digits && constant == false; ++d)
                    constant = count[d + 1] == n;
                if (constant)
                    continue;
                for (int d = 0; d < Digits; ++d)
                    count[d + 1] += count[d];
                for (int i = 0; i < n; ++i) {
                    int at = count[(int)((keys[i] >> shift) & Mask)]++;
                    k2[at] = keys[i];
                    i2[at] = items[i];
                }
                long[] tk = keys; keys = k2; k2 = tk;
                int[] ti = items; items = i2; i2 = ti;
            }
            // (after an odd number of passes the sorted data is in the other arrays)
            if (keys != keysIn) {
                Array.Copy(keys, keysIn, n);
                Array.Copy(items, itemsIn, n);
            }
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


        [MethodImpl(JitHints.Optimize)]
        int centroid(ExactMeshArrangement.OutputTriangle ot)
        {
            return arr.AddPoint(centroid_point(ot, true));
        }

        // the exact centroid of a piece (not added to the arrangement); canonical (reduced by the gcd) only if asked for:
        // the ray of front_crossings needs only its value
        [MethodImpl(JitHints.Optimize)]
        ExactPoint3 centroid_point(ExactMeshArrangement.OutputTriangle ot, bool canonical)
        {
            ExactPoint3 a = arr.Exact(ot.A), b = arr.Exact(ot.B), c = arr.Exact(ot.C);
            BigInteger wbc = b.W * c.W, wac = a.W * c.W, wab = a.W * b.W;
            if (canonical == false)
                return ExactPoint3.CreateUnreduced(
                    a.X * wbc + b.X * wac + c.X * wab,
                    a.Y * wbc + b.Y * wac + c.Y * wab,
                    a.Z * wbc + b.Z * wac + c.Z * wab,
                    3 * a.W * wbc);
            return ExactPoint3.Create(
                a.X * wbc + b.X * wac + c.X * wab,
                a.Y * wbc + b.Y * wac + c.Y * wab,
                a.Z * wbc + b.Z * wac + c.Z * wab,
                3 * a.W * wbc);
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
        int front_winding(int i, int mesh, TriangleBoxTree tree, Random rng, List<int> stack, Center center)
        {
            int farIndex;
            return front_crossings(i, mesh, tree, rng, stack, center, true, null, out farIndex);
        }

        /// <summary>
        /// front_winding's signed crossings of the segment from the piece's centroid to the far point in front, and the far
        /// point used (its index in far, -1 without far). outsideBoxIsZero: a point outside the mesh's box has
        /// winding number 0 (true for a closed mesh; an open mesh's crossings are counted wherever the point is)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        int front_crossings(int i, int mesh, TriangleBoxTree tree, Random rng, List<int> stack, Center center, bool outsideBoxIsZero,
            FarPoints far, out int farIndex, int firstAttempt = 0)
        {
            farIndex = -1;
            // (i < 0: the crossings at the point itself, a point on no triangle of the mesh: no source triangle to skip and
            // no front side, t3a_cull)
            int source = i >= 0 ? arr.Output[i].Source : -1;
            var src = i >= 0 ? arr.Triangles[source] : null;
            List<int> tris = arr.MeshTriangles[mesh];

            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            ExactPoint3 P = center.P;
            // outside the box of the mesh: winding number 0
            for (int k = 0; k < 3 && outsideBoxIsZero; ++k)
                if (P[k] < min[k] * P.W || P[k] > max[k] * P.W)
                    return 0;
            BigInteger ext = BigInteger.One;
            for (int k = 0; k < 3 && far == null; ++k)
                ext = BigInteger.Max(ext, max[k] - min[k] + 1);

            Vector3d pv = center.Input;
            // pad: the rounding of the point to double (relative 1e-16), with a wide margin
            double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
            // grid coordinates of the point as doubles, for the filtered plane sides
            Vector3d g = center.Grid();
            double gx = g.x, gy = g.y, gz = g.z;
            for (int attempt = firstAttempt; attempt < firstAttempt + 200; ++attempt) {
                ExactPoint3 Rp;
                double rx, ry, rz;
                Vector3d rv;
                int j = -1;
                if (far != null) {
                    // (one of the mesh's precomputed far points, FarPoints, chosen by a hash of the seed, the piece and the
                    // attempt: deterministic in any thread order, no generator per piece)
                    ulong h = unchecked((ulong)(uint)Seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)i * 0xBF58476D1CE4E5B9UL ^ (ulong)(uint)attempt * 0x94D049BB133111EBUL);
                    h = (h ^ (h >> 31)) * 0xD6E8FEB86659FD93UL;
                    h ^= h >> 32;
                    j = (int)(h % (ulong)far.R.Length);
                    Rp = far.R[j];
                    rx = far.Grid[j].x; ry = far.Grid[j].y; rz = far.Grid[j].z;
                    rv = far.Input[j];
                } else {
                    Rp = far_point(min, max, ext, rng);
                    rx = (double)Rp.X; ry = (double)Rp.Y; rz = (double)Rp.Z;
                    rv = Vector3d.Zero;
                }
                // the segment must start into the front side of the piece: the sign of n . (R - P), in doubles where the
                // error bound decides (R is far: almost always), else exact
                if (src != null) {
                    int frontSign = DisableFilters ? 2 : front_side_filtered(src.PlaneApprox, rx, ry, rz, gx, gy, gz);
                    if (frontSign == 2) {
                        BigInteger front = src.NX * (Rp.X * P.W - P.X) + src.NY * (Rp.Y * P.W - P.Y) + src.NZ * (Rp.Z * P.W - P.Z);
                        frontSign = front.Sign;
                    }
                    if (frontSign <= 0)
                        continue;
                }
                if (far == null)
                    rv = arr.ToInputVector(Rp);
                Vector3d gp = new Vector3d(gx, gy, gz), gr = new Vector3d(rx, ry, rz);

                int winding = 0;
                bool degenerate = false;
                long sides = 0, exactSides = 0;
                tree.QuerySegment(pv, rv, pad, (k) => {
                    if (degenerate) return;
                    int ti = tris[k];
                    var U = arr.Triangles[ti];
                    if (U.Degenerate || ti == source || (stack != null && stack.Contains(ti))) return;
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
                    // (the corners' exact points only when a filter fails: most are integer points with double
                    // coordinates, whose doubles the arrangement keeps)
                    int c0 = U.Corners[0], c1 = U.Corners[1], c2 = U.Corners[2];
                    Vector3d d0 = corner_double(c0), d1 = corner_double(c1), d2 = corner_double(c2);
                    int o0 = orient3d_filter(ref gp, ref gr, ref d0, ref d1);
                    int o1 = orient3d_filter(ref gp, ref gr, ref d1, ref d2);
                    int o2 = orient3d_filter(ref gp, ref gr, ref d2, ref d0);
                    if (o0 == 2 || o1 == 2 || o2 == 2) {
                        // (the corners' exact points once, for the orientations the filter did not decide)
                        ExactPoint3 e0 = arr.Exact(c0), e1 = arr.Exact(c1), e2 = arr.Exact(c2);
                        if (o0 == 2) o0 = orient3d_exact(P, Rp, e0, e1);
                        if (o1 == 2) o1 = orient3d_exact(P, Rp, e1, e2);
                        if (o2 == 2) o2 = orient3d_exact(P, Rp, e2, e0);
                    }
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
                if (degenerate == false) {
                    farIndex = j;
                    return winding;
                }
            }
            throw new InvalidOperationException("ExactMeshBoolean: no non-degenerate ray found");
        }

        // the point a patch's winding numbers are evaluated at, with its doubles (computed once per patch)
        sealed class Center
        {
            public readonly ExactPoint3 P;
            public Vector3d Input;      // ToInputVector(P)
            Vector3d grid;
            bool hasGrid;
            public Center(ExactPoint3 p) { P = p; }
            // the grid coordinates as doubles, without the tiny-coordinate check of Grid (for the Antipodal boundary sum)
            public Vector3d GridDouble()
            {
                return new Vector3d(ExactPoint3.ToDouble(P.X, P.W, 0), ExactPoint3.ToDouble(P.Y, P.W, 0), ExactPoint3.ToDouble(P.Z, P.W, 0));
            }
            // the grid coordinates as doubles (grid_coord), when first needed
            public Vector3d Grid()
            {
                if (hasGrid == false) {
                    grid = new Vector3d(grid_coord(P.X, P.W), grid_coord(P.Y, P.W), grid_coord(P.Z, P.W));
                    hasGrid = true;
                }
                return grid;
            }
        }

        // a far point for the ray of front_crossings: centre of the mesh's box plus a random direction (one axis at full length
        // 1000, the others in -1000..1000) times 4 times the box's extent, so the segment leaves the box
        static ExactPoint3 far_point(BigInteger[] min, BigInteger[] max, BigInteger ext, Random rng)
        {
            int axis = rng.Next(3);
            long[] dir = new long[3];
            for (int k = 0; k < 3; ++k)
                dir[k] = rng.Next(-1000, 1001);
            dir[axis] = (rng.Next(2) == 0) ? -1000 : 1000;
            BigInteger[] R = new BigInteger[3];
            for (int k = 0; k < 3; ++k)
                R[k] = (min[k] + max[k]) / 2 + dir[k] * ext * 4;
            return ExactPoint3.FromInteger(R[0], R[1], R[2]);
        }

        // far points of a mesh, computed once (exact, as doubles in grid units and in input coordinates): the per-piece rays of
        // the Antipodal Method pick one of them instead of constructing a new one per attempt (no exact arithmetic per ray)
        sealed class FarPoints
        {
            public ExactPoint3[] R;
            public Vector3d[] Grid, Input;
        }

        FarPoints far_points(int mesh, int count)
        {
            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            // (the extent of the box of all meshes: every centroid lies within it, so the segment to a far point leaves the
            // mesh's box for good, whatever the direction; with the mesh's own extent, a piece of a much larger mesh far away
            // could see the line come back into the box beyond the far point)
            BigInteger ext = BigInteger.One;
            for (int m = 0; m < arr.Meshes.Count; ++m) {
                BigInteger[] mmin, mmax;
                mesh_bounds(m, out mmin, out mmax);
                for (int k = 0; k < 3; ++k)
                    ext = BigInteger.Max(ext, BigInteger.Max(mmax[k], max[k]) - BigInteger.Min(mmin[k], min[k]) + 1);
            }
            var rng = new Random(unchecked(Seed + 7919 * (mesh + 1)));
            var f = new FarPoints() { R = new ExactPoint3[count], Grid = new Vector3d[count], Input = new Vector3d[count] };
            for (int j = 0; j < count; ++j) {
                f.R[j] = far_point(min, max, ext, rng);
                f.Grid[j] = new Vector3d((double)f.R[j].X, (double)f.R[j].Y, (double)f.R[j].Z);
                f.Input[j] = arr.ToInputVector(f.R[j]);
            }
            return f;
        }

        // plane sides evaluated by front_winding, and how many of them exactly (the filter could not decide)
        long windingSides, windingExactSides;

        /// <summary>
        /// sign of n . (r - g) for the normal n = pa[0..2] (doubles with relative error at most 2^-52 each,
        /// InputTriangle.PlaneApprox) and points r, g given by doubles with relative error at most 2^-52 each; 2 if the error
        /// bound does not decide (also for overflow or tiny values, and for coordinates below TinyCoordinate)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int front_side_filtered(double[] pa, double rx, double ry, double rz, double gx, double gy, double gz)
        {
            if (tiny(rx) || tiny(ry) || tiny(rz) || tiny(gx) || tiny(gy) || tiny(gz))
                return 2;
            double a = pa[0] * (rx - gx), b = pa[1] * (ry - gy), c = pa[2] * (rz - gz);
            double mag = Math.Abs(pa[0]) * (Math.Abs(rx) + Math.Abs(gx)) + Math.Abs(pa[1]) * (Math.Abs(ry) + Math.Abs(gy))
                + Math.Abs(pa[2]) * (Math.Abs(rz) + Math.Abs(gz));
            // per term: 2^-52 (coefficient) + 2^-52 (each coordinate) + 2^-53 (difference) + 2^-53 (product), of mag;
            // the sum adds 2 * 2^-53 of mag: below 6 * 2^-52 * mag, taken generously
            double bound = 1e-14 * mag;
            if (!(mag < double.MaxValue) || mag < 1e-290)
                return 2;
            double v = a + b + c;
            if (v > bound) return 1;
            if (v < -bound) return -1;
            return 2;
        }

        /// <summary>
        /// sign of A x + B y + C z - D for a plane given by doubles pa[0..3] (each with relative error at most
        /// 2^-52, ExactMeshArrangement.InputTriangle.PlaneApprox) at a point given by doubles (relative error at
        /// most 2^-52 each); 2 if the error bound does not decide (also for overflow or tiny values, and for
        /// coordinates below TinyCoordinate, where the relative error of the rounding is not bounded)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        DMeshAABBTree3 winding_tree(int mesh)
        {
            DMeshAABBTree3 tree;
            if (windingTrees.TryGetValue(mesh, out tree) == false)
                windingTrees[mesh] = tree = new_winding_tree(mesh);
            return tree;
        }

        // the stored winding tree of an open mesh after Compute, null if none (FastWindingParallelBuildTests)
        internal DMeshAABBTree3 StoredWindingTree(int mesh)
        {
            return windingTrees.TryGetValue(mesh, out DMeshAABBTree3 tree) ? tree : null;
        }

        // a tree with its fast-winding cache built (reads only the mesh: safe to call concurrently for different meshes)
        DMeshAABBTree3 new_winding_tree(int mesh)
        {
            var tree = new DMeshAABBTree3(arr.Meshes[mesh], true);
            tree.FastWindingNumber(Vector3d.Zero);
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

        // per open mesh evaluated by the Antipodal Method: its boundary segments (pairs of input points, BoundarySegments)
        readonly Dictionary<int, Vector3d[]> antipodalSegments = new Dictionary<int, Vector3d[]>();
        // and its far points for the rays (far_points)
        readonly Dictionary<int, FarPoints> antipodalFar = new Dictionary<int, FarPoints>();

        /// <summary>
        /// Generalized winding number of an open mesh just in front of output piece i by the Antipodal Method (Martens,
        /// Trettner, Bessmeltsev, The Antipodal Method, 2026, eq. 6 and 9): the exact signed crossings chi of a ray from the
        /// piece's centroid into its front side (front_crossings: the triangles of the piece's stack skipped, so chi is the
        /// count just in front of the piece), plus 1/(4 pi) times the sum over the mesh's boundary segments (a, b) of the
        /// signed area 2 atan2(A.(B x x1), 1 + A.B + A.x1 + B.x1) of the spherical triangle of A, B (the unit vectors from
        /// the centroid to a and b) and x1, the direction opposite to the ray (Van Oosterom and Strackee). The boundary sum
        /// is continuous across the mesh's own triangles, so no correction for the triangles the piece lies on is needed
        /// (unlike the fast winding number on the surface). Exact up to the rounding of the boundary sum; no far-field
        /// approximation. A ray whose opposite direction passes through a boundary segment (the term is undefined) is
        /// replaced by another. A value within the error bound of the sum of a half-integer is returned as that
        /// half-integer (in front of a flat part of an open sheet the value is exactly k + 1/2: the tie of the threshold is
        /// then decided by its rule, not by the rounding of the sum, so the same with every ray). A true value within the
        /// bound of k + 1/2 but not equal to it is read as k + 1/2 too (the threshold gives k): the one place where the
        /// result is not exact; the bound is about 1e-13 per boundary segment (1e-10 at the switch's 1000), more for a
        /// centroid next to a segment, at most 1e-6 in all (a ray with a larger bound, or with an ill-conditioned term, is
        /// replaced; on or next to a segment the centroid is undecided anyway). The bound depends on the ray: an exact tie is snapped with every
        /// ray, a value within the bound of a tie but not on it can be snapped with one ray and not with another (one Seed
        /// gives one result). The sum uses portable_atan2, so the value is the same bits on CoreCLR and .NET Framework.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        double antipodal_front_winding(int i, int mesh, List<int> stack, Vector3d[] segments, Center center)
        {
            // (in grid coordinates, relative to the arrangement's centre: their rounding is relative to the model's size,
            // not to its distance from the origin, eg UTM coordinates; the segments are converted once, antipodal_grid)
            Vector3d p = center.GridDouble();
            double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
            FarPoints far = antipodalFar[mesh];
            for (int attempt = 0; attempt < 20; ++attempt) {
                int j;
                // (attempts continue the hash sequence of front_crossings: a new far point each time)
                int chi = front_crossings(i, mesh, arr.Trees[mesh], null, stack, center, false, far, out j, 200 * attempt);
                Vector3d x1 = -(far.Grid[j] - p).Normalized;
                double sum = 0, err = 0, abs = 0;
                bool degenerate = false;
                for (int k = 0; k < segments.Length; k += 2) {
                    Vector3d a = segments[k] - p, b = segments[k + 1] - p;
                    double la = a.Length, lb = b.Length;
                    // the error of the unit vectors: the rounding of p and of the ends (relative 2^-52 of their L1 size, as
                    // a distance), divided by the distance, plus the normalization; num and den are sums of a few products
                    // of unit vectors, so their errors are a small multiple of it (taken generously: 16 times)
                    double ea = 2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) / la;
                    double eb = 2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) / lb;
                    double tol = 16 * (ea + eb + 1e-15);
                    if (!(tol < 0.01)) {
                        // (the centroid on or next to an end of the segment: undecided whatever the ray)
                        System.Threading.Interlocked.Increment(ref antipodalUndecided);
                        return 0.5;
                    }
                    a /= la;
                    b /= lb;
                    if (1 + a.Dot(b) <= tol) {
                        // (a and b opposite: the centroid on or next to the segment, undecided whatever the ray)
                        System.Threading.Interlocked.Increment(ref antipodalUndecided);
                        return 0.5;
                    }
                    double num = a.Dot(b.Cross(x1)), den = 1 + a.Dot(b) + a.Dot(x1) + b.Dot(x1);
                    // the branch cut of 2 atan2(num, den) is num = 0, den < 0 (the ray passes through the segment, where the
                    // exact crossing count and the rounded sum could disagree by 1), and both near 0 is the singular point:
                    // within the error, another ray
                    if (Math.Abs(num) <= tol && den <= tol) {
                        degenerate = true;
                        break;
                    }
                    // (portable_atan2, not Math.Atan2: the runtime's atan2 differs by an ulp between runtimes, and a value
                    // next to the edge of the snap below could then be decided differently)
                    // the error of the term: d atan2(y, x) is at most (|dy| + |dx|) / hypot(x, y), times 2 (first order,
                    // valid while tol is small against the hypot: the bound of 1e-6 below on the whole sum keeps every
                    // tol / hypot below 3e-6; num and den both small, the ray nearly through an end of the segment, makes
                    // the term ill-conditioned and the bound large, so another ray is taken there)
                    double termError = 4 * tol / Math.Sqrt(num * num + den * den);
                    double theta = 2 * portable_atan2(num, den);
                    sum += theta;
                    abs += Math.Abs(theta);
                    // plus twice the error of portable_atan2 (PortableAtan2Error)
                    err += termError + 2 * PortableAtan2Error;
                }
                if (degenerate == false) {
                    // (the recursive summation: at most n u times the sum of the absolute terms)
                    err += 2.3e-16 * (segments.Length / 2) * abs;
                    double w = chi + sum / MathUtil.FourPI;
                    // (and the roundings of the division by 4 pi (also of 4 pi itself) and of the addition of chi)
                    double bound = err / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
                    // (a bound above 1e-6 would make the snap below a guess: another ray; the bound is about 1e-13 per
                    // segment for a centroid not next to one)
                    if (!(bound <= 1e-6))
                        continue;
                    // a value within the error of a half-integer is that half-integer: just in front of a flat part of an
                    // open sheet the winding number is exactly k + 1/2, and the rounding of the sum must not decide the
                    // threshold (ceil(w - 1/2) reads k + 1/2 as k, whatever the ray)
                    double snapped = snap_half(w, bound);
                    if (snapped != w)
                        System.Threading.Interlocked.Increment(ref antipodalSnapped);
                    return snapped;
                }
            }
            // (no usable ray: undecided, which the threshold reads as outside)
            System.Threading.Interlocked.Increment(ref antipodalUndecided);
            return 0.5;
        }

        // w, or the half-integer nearest to w (floor(w) + 1/2, exact for |w| below 2^52) if it is within bound of it
        internal static double snap_half(double w, double bound)
        {
            double half = Math.Floor(w) + 0.5;
            return Math.Abs(w - half) <= bound ? half : w;
        }

        // the bound of the absolute error of portable_atan2 (its result is at most pi; measured below 1e-15 against
        // Math.Atan2, PortableAtan2Tests; taken with a wide margin)
        internal const double PortableAtan2Error = 4e-15;

        /// <summary>
        /// atan2(y, x) from +, -, *, / and the square root only (IEEE, correctly rounded; the CoreCLR and .NET Framework
        /// JITs do not contract them to FMA; x87 on 32-bit .NET Framework rounds to 53 bits and differs only in the
        /// subnormal range, which the caller does not reach), so the same bits on these runtimes, unlike Math.Atan2. Reduced to atan(t), t in [0, 1], halved
        /// twice by atan(t) = 2 atan(t / (1 + sqrt(1 + t^2))) to |v| at most tan(pi / 16) (v^2 below 0.04), then the
        /// Taylor series to v^25 (the rest below 1e-17 v). atan2(+-0, +-0) is 0; otherwise the signs and quadrants of Math.Atan2.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double portable_atan2(double y, double x)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y);
            if (ax == 0 && ay == 0)
                return 0;
            bool swap = ay > ax;
            double t = swap ? ax / ay : ay / ax;
            double u = t / (1 + Math.Sqrt(1 + t * t));
            double v = u / (1 + Math.Sqrt(1 + u * u));
            double v2 = v * v;
            // sum over k of (-1)^k v^(2k+1) / (2k+1), k = 0..12, by Horner in v^2
            double s = 1.0 / 25;
            s = s * -v2 + 1.0 / 23; s = s * -v2 + 1.0 / 21; s = s * -v2 + 1.0 / 19; s = s * -v2 + 1.0 / 17;
            s = s * -v2 + 1.0 / 15; s = s * -v2 + 1.0 / 13; s = s * -v2 + 1.0 / 11; s = s * -v2 + 1.0 / 9;
            s = s * -v2 + 1.0 / 7; s = s * -v2 + 1.0 / 5; s = s * -v2 + 1.0 / 3; s = s * -v2 + 1;
            double a = 4 * (v * s);
            if (swap)
                a = Math.PI / 2 - a;
            if (x < 0)
                a = Math.PI - a;
            // (the sign bit of y, as Math.Atan2: atan2(-0, -1) is -pi)
            return BitConverter.DoubleToInt64Bits(y) < 0 ? -a : a;
        }

        // the cull test of the early-out (ExactMeshArrangement.CullTest): input triangle ti, whose box meets no box of the
        // first mesh, is wholly outside it (winding number 0 or less: the predicates of Difference and Intersection need
        // it positive): the exact crossings of a ray from its first corner, which lies on no triangle of the first mesh
        bool cull_outside_a(int ti)
        {
            var center = new Center(arr.Exact(arr.Triangles[ti].Corners[0]));
            center.Input = arr.ToInputVector(center.P);
            var rng = new Random(unchecked(Seed + 1000003 * ti + 7));
            int farIndex;
            return front_crossings(-1, 0, arr.Trees[0], rng, null, center, true, null, out farIndex) <= 0;
        }

        // pieces whose Antipodal value could not be decided (the centroid on or next to a boundary segment, or no usable ray)
        long antipodalUndecided;
        // values the Antipodal Method snapped to a half-integer (snap_half)
        long antipodalSnapped;

        // the boundary segments of a mesh in grid coordinates (relative to the arrangement's centre, as doubles)
        Vector3d[] antipodal_grid(Vector3d[] segments)
        {
            var g = new Vector3d[segments.Length];
            for (int k = 0; k < segments.Length; ++k)
                g[k] = new Vector3d((double)arr.GridValue(segments[k].x, 0), (double)arr.GridValue(segments[k].y, 1), (double)arr.GridValue(segments[k].z, 2));
            return g;
        }

        /// <summary>
        /// The boundary of a mesh as an oriented 1-chain: its vertices welded by exact position (-0 as 0), each edge
        /// counted +1 for every triangle that runs it from the smaller to the larger welded id and -1 the other way, and
        /// every edge with a nonzero count returned that many times, oriented by its sign (pairs a, b of input
        /// points, in the order the edges first appear). Opposite half-edges cancel; a same-orientation duplicate
        /// triangle doubles its edges, a non-manifold fan leaves its odd ones. Linear in the triangles (hash maps),
        /// deterministic. Empty for a mesh that is closed in the winding number sense.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static Vector3d[] BoundarySegments(DMesh3 mesh)
        {
            var ids = new Dictionary<Vector3d, int>();
            var pos = new List<Vector3d>();
            var net = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            var order = new List<long>();
            var welded = new int[3];
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int c = 0; c < 3; ++c) {
                    Vector3d v = mesh.GetVertex(t[c]);
                    if (v.x == 0) v.x = 0;      // -0.0
                    if (v.y == 0) v.y = 0;
                    if (v.z == 0) v.z = 0;
                    int k;
                    if (ids.TryGetValue(v, out k) == false) {
                        k = pos.Count;
                        ids[v] = k;
                        pos.Add(v);
                    }
                    welded[c] = k;
                }
                for (int e = 0; e < 3; ++e) {
                    int u = welded[e], w = welded[(e + 1) % 3];
                    if (u == w)
                        continue;
                    long key = ((long)Math.Min(u, w) << 32) | (uint)Math.Max(u, w);
                    int count;
                    if (net.TryGetValue(key, out count) == false) {
                        count = 0;
                        order.Add(key);
                    }
                    net[key] = count + ((u < w) ? 1 : -1);
                }
            }
            var segments = new List<Vector3d>();
            foreach (long key in order) {
                int count = net[key];
                int lo = (int)(key >> 32), hi = (int)(key & 0xFFFFFFFF);
                for (int r = 0; r < Math.Abs(count); ++r) {
                    segments.Add(pos[count > 0 ? lo : hi]);
                    segments.Add(pos[count > 0 ? hi : lo]);
                }
            }
            return segments.ToArray();
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

        // grid_double of the exact point of id, without computing it as BigIntegers where it is an integer point with
        // double coordinates (the same doubles: grid_double converts W = 1 points exactly then)
        [MethodImpl(JitHints.Optimize)]
        Vector3d corner_double(int id)
        {
            Vector3d d;
            return arr.TryExactDouble(id, out d) ? d : grid_double(arr.Exact(id));
        }

        // the grid coordinates of the point as doubles (each with relative error at most 2^-52, or NaN, see grid_coord)
        [MethodImpl(JitHints.Optimize)]
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
        [MethodImpl(JitHints.Optimize)]
        internal static double grid_coord(BigInteger num, BigInteger den)
        {
            double r = ExactPoint3.ToDouble(num, den, 0);
            return (num.IsZero == false && !(Math.Abs(r) >= TinyCoordinate)) ? double.NaN : r;
        }

        static bool tiny(double v) { return v != 0 && !(Math.Abs(v) >= TinyCoordinate); }

        /// <summary>
        /// the floating-point filter of orient3d(a, b, c, d): the doubles da..dd approximate the points (relative
        /// error at most 2^-52 per coordinate); 2 if the error bound does not decide (or DisableFilters), then
        /// orient3d_exact decides
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        int orient3d_filter(ref Vector3d da, ref Vector3d db, ref Vector3d dc, ref Vector3d dd)
        {
            PredicateStats.Call(PredicateStats.Kind.WindingOrient3d);
            return DisableFilters ? 2 : orient3d_filtered(ref da, ref db, ref dc, ref dd);
        }

        // the exact stage of orient3d_filter (when it gave 2)
        [MethodImpl(JitHints.Optimize)]
        static int orient3d_exact(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
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
        [MethodImpl(JitHints.Optimize)]
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
            var tris = new List<Index3i>(arr.Output.Count);
            var source = new List<int>(arr.Output.Count);
            var flipped = new List<bool>(arr.Output.Count);
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
            Func<int, int> find = (x) => find_root(parent, x);
            Action<int, int> join = (x, y) => {
                int rx = find(x), ry = find(y);
                if (rx != ry) parent[rx] = ry;
            };
            // slot of point p in triangle t
            Func<int, int, int> slot = (t, p) => slot_of(tris, t, p);

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
                list.Sort();        // (the first triangle is the reference half-plane; sort_by_key keeps them in order already)
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

            // with an open input, the per-piece classification (front_generalized_winding) can keep pieces whose fans
            // around a vertex meet only at the vertex (the kept region's boundary, {w = 1/2}, does not run along mesh
            // edges): each such vertex gets one copy per fan, so that the result is a valid mesh without bowties
            if (Array.IndexOf(ExactWinding, false) >= 0)
                SplitBowtieVertices = split_bowties(result, vertexPoint);

            // input vertices (kept by SafeRounding where possible) and exact coordinates
            resultFixed = new bool[vertexPoint.Count];
            for (int vid = 0; vid < vertexPoint.Count; ++vid)
                resultFixed[vid] = vertexPoint[vid] >= 0 && inputPoints[vertexPoint[vid]];
            // the exact point of each vertex, as an id: the arrangement's point id (equal exact points have one id), and
            // for a split vertex the midpoint of its edge, one id per edge beyond the point ids (copies at one edge share
            // it). Vertices at one double position with different ids are a contact the rounding made (SafeRounding)
            resultPointId = new int[vertexPoint.Count];
            var midpointId = new Dictionary<Index2i, int>();
            for (int vid = 0; vid < vertexPoint.Count; ++vid) {
                if (vertexPoint[vid] >= 0) {
                    resultPointId[vid] = vertexPoint[vid];
                } else {
                    Index2i uv = splitPoints[-2 - vertexPoint[vid]];
                    var key = new Index2i(Math.Min(uv.a, uv.b), Math.Max(uv.a, uv.b));
                    int id;
                    if (midpointId.TryGetValue(key, out id) == false)
                        midpointId[key] = id = arr.PointCount + midpointId.Count;
                    resultPointId[vid] = id;
                }
            }
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

        // vertices with an open input whose fans were split (assemble): for tests
        internal int SplitBowtieVertices;

        // every bowtie vertex of mesh (in vertex order) keeps the fan with its smallest triangle id; each other fan, in
        // the order of its smallest triangle id, gets a new vertex at the same position (vertexPoint extended): the same
        // result on every run. Splitting the fans at one vertex makes no new bowtie at another. Returns the vertices split
        [MethodImpl(JitHints.Optimize)]
        internal static int split_bowties(DMesh3 mesh, List<int> vertexPoint)
        {
            int split = 0, nv = mesh.MaxVertexID;
            var parent = new Dictionary<int, int>();
            var fans = new SortedDictionary<int, List<int>>();
            for (int vid = 0; vid < nv; ++vid) {
                if (mesh.IsVertex(vid) == false || mesh.IsBowtieVertex(vid) == false)
                    continue;
                // the fans: the triangles at vid joined across the edges at vid with two triangles
                parent.Clear();
                foreach (int tid in mesh.VtxTrianglesItr(vid))
                    parent[tid] = tid;
                foreach (int eid in mesh.VtxEdgesItr(vid)) {
                    Index2i et = mesh.GetEdgeT(eid);
                    if (et.b != DMesh3.InvalidID) {
                        int ra = root_of(parent, et.a), rb = root_of(parent, et.b);
                        if (ra != rb)
                            parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }
                // (each root is the smallest triangle id of its fan: roots only ever point to smaller ids)
                fans.Clear();
                foreach (int tid in new List<int>(parent.Keys)) {
                    int r = root_of(parent, tid);
                    if (fans.TryGetValue(r, out List<int> fan) == false)
                        fans[r] = fan = new List<int>();
                    fan.Add(tid);
                }
                if (fans.Count < 2)
                    continue;
                split++;
                bool first = true;
                foreach (List<int> fan in fans.Values) {
                    if (first) {
                        first = false;
                        continue;
                    }
                    int copy = mesh.AppendVertex(mesh.GetVertex(vid));
                    vertexPoint.Add(vertexPoint[vid]);
                    fan.Sort();
                    foreach (int tid in fan) {
                        Index3i t = mesh.GetTriangle(tid);
                        if (t.a == vid) t.a = copy;
                        else if (t.b == vid) t.b = copy;
                        else t.c = copy;
                        mesh.SetTriangle(tid, t, false);
                    }
                }
            }
            return split;
        }

        static int root_of(Dictionary<int, int> parent, int x)
        {
            while (parent[x] != x)
                x = parent[x];
            return x;
        }

        // positions with two or more vertices (Contacts)
        static int count_contacts(DMesh3 mesh)
        {
            var seen = new Dictionary<Vector3d, int>();
            int contacts = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                // (-0.0 and 0.0 are one position: Vector3d's equality compares with ==, and double's hash is the same
                // for both, on .NET Framework as on .NET)
                int n;
                seen.TryGetValue(p, out n);
                if (n == 1)
                    contacts++;
                seen[p] = n + 1;
            }
            return contacts;
        }

        // per vertex of the last assembled result: is it an input vertex; the id of its exact point
        bool[] resultFixed;
        int[] resultPointId;

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
        [MethodImpl(JitHints.Optimize)]
        static bool can_add_edge(DMesh3 mesh, int a, int b)
        {
            int eid = mesh.FindEdge(a, b);
            return eid == DMesh3.InvalidID || (mesh.IsBoundaryEdge(eid) && runs_existing(mesh, a, b) == false);
        }

        // the edge (a, b) of the mesh exists and a triangle at it runs a -> b
        [MethodImpl(JitHints.Optimize)]
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
