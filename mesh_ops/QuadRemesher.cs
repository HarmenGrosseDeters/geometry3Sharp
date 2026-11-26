// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> Result of QuadRemesher.Compute(). </summary>
    public enum QuadRemeshStatus
    {
        Success = 0,
        /// <summary> no mesh, a TargetEdgeLength that is not positive and finite, invalid CreaseAngleDegrees, or incompatible options </summary>
        InvalidInput = 1,
        /// <summary> the cross field failed (see FieldStatus: InvalidMesh, NotManifold, SolverFailed) </summary>
        FieldFailed = 2,
        /// <summary> the position field failed </summary>
        PositionFieldFailed = 3,
        /// <summary> the extraction found no face (the target edge length is far above or far below the size of the mesh edges) </summary>
        NoFaces = 4
    }

    /// <summary> QuadRemesher.SizeAdaptation: what the position field may vary to close up the lattice on a curved surface (IntegrableFrames). </summary>
    public enum SizeAdaptationMode
    {
        /// <summary> uniform spacing along the cross field (the default; the output of the stage 2 method) </summary>
        Off = 0,
        /// <summary> a smooth size field: the spacing varies by a factor of about 2 across a sphere </summary>
        Scale = 1,
        /// <summary> the size field and a rotation of the field of up to some 10 degrees (fewest irregular vertices on closed smooth shapes) </summary>
        ScaleAndRotation = 2
    }

    /// <summary> Result of the pure-quad attempt of QuadRemesher (PureQuads): the first check that failed, see PureQuads </summary>
    public enum PureQuadStatus
    {
        /// <summary> PureQuads is false (or Compute() failed before the attempt) </summary>
        NotRequested = 0,
        /// <summary> every check passed: Result is the mesh of quads only </summary>
        Success = 1,
        /// <summary> the repaired integer map still has triangles of negative orientation (InvertedTriangles, exact integer determinant) </summary>
        OrientationFolds = 2,
        /// <summary> the extracted faces include a quad that faces away from the surface (FlippedQuads) </summary>
        FlippedFaces = 3,
        /// <summary> the extracted mesh is not a manifold of quads only (an unpaired triangle, a face that is not a quad, a non-manifold edge or vertex) </summary>
        NotManifold = 4,
        /// <summary> the flow of the integer corrections could not be completed, the lattice offsets are out of range, or the mesh has a boundary (open meshes are not supported by the pure-quad attempt) </summary>
        FlowInfeasible = 5,
        /// <summary> the repair used up a deterministic budget (the decisions, attempts or rounds of the SAT, the edge splits) before the map was free of folds </summary>
        BudgetExceeded = 6
    }

    /// <summary>
    /// Quad-dominant remeshing by an instant field-aligned mesh: the cross field (CrossField, Knöppel et al. 2013), the position
    /// field (PositionField, Jakob et al. 2015) and the extraction of the polygon mesh from the integer lattice offsets with a
    /// cleanup, after the same paper (written from the paper; docs/design/QUAD_REMESHING.md stage 2). Mostly quads with a few
    /// triangles, pentagons and hexagons near the singularities.
    ///
    /// The extraction: two neighbouring vertices whose lattice points are the same are merged (union-find over the edges of the
    /// mesh), neighbours with lattice points one step apart become an edge of the polygon graph, and the faces are traced in the
    /// tangent planes. See Cleanup for the repairs that follow.
    ///
    /// Input: a manifold triangle mesh (open boundaries — and, when PreserveCreases is true, sharp interior creases above
    /// CreaseAngleDegrees — are lattice constraints, see PositionField) and TargetEdgeLength, the edge length of the quads.
    /// Field: a CrossField of the mesh that was computed successfully; if null, one is computed with CurvatureAlignment,
    /// AlignCreases (or PreserveCreases, which implies it) and CreaseAngleDegrees (default: the smoothest field). The mesh is not
    /// modified. Result: a DPolyMesh (outward as the input's orientation; vertices are on the tangent planes of the input, not
    /// projected). Deterministic: the same input gives the same output bit for bit, serial or parallel; power of two scalings of
    /// the mesh and of TargetEdgeLength give the same polygon mesh up to the scaling. Compute() can be called again.
    ///
    /// Pure quads (opt-in, PureQuads = true): after the extraction the integer offsets of the position field are repaired (a flow
    /// for the regularity, subdivision, local SAT; QuadFlowRepair, QUAD_REMESHING.md stage 3B) and a mesh of quads only is
    /// extracted. If a check fails, the repair is tried once more with the fixed variables of the flow freed (the edges whose cycle
    /// has a holonomy take the value of a relaxation first; QUAD_REMESHING.md 4.4d). A pure-quad mesh is used only if every check
    /// passes; otherwise Result is the quad-dominant mesh of stage 2 and PureQuadStatus says which check of the first attempt failed
    /// (never silent).
    /// </summary>
    public class QuadRemesher
    {
        public DMesh3 Mesh;
        /// <summary> optional: a computed cross field of Mesh </summary>
        public CrossField Field;
        /// <summary> the edge length of the quads </summary>
        public double TargetEdgeLength = 0;
        /// <summary> Gauss-Seidel sweeps per level of the position field </summary>
        public int Sweeps = 20;
        public bool Parallel = true;
        /// <summary> faces with more sides are not part of the result (a hole, normally none) </summary>
        public int MaxFaceSides = 6;
        /// <summary> the repairs after the extraction (collapse of short edges, valence 2 vertices, dangling edges) </summary>
        public bool Cleanup = true;
        /// <summary> edges whose lattice points do not agree (the two roundings are further apart than this, in units of the edge length) do not vote </summary>
        public double MismatchLimit = 0.5;
        /// <summary> see PositionField.MiddleRegularization </summary>
        public double MiddleRegularization = 0.05;
        /// <summary> see PositionField.CornerAngleDegrees: a boundary vertex where the boundary turns by more than this angle (degrees, 0..180) is pinned as a lattice point; with PreserveCreases it also classifies the crease corners (a crease vertex where the crease turns by more than this angle is a fixed lattice point) </summary>
        public double CornerAngleDegrees = 30;
        /// <summary> see CrossField.CurvatureAlignment (used when Field is null; ignored when a pre-computed Field is supplied, but its value is always validated: non-negative and finite, else InvalidInput): 0 gives the smoothest field, about 1 follows principal curvature directions where the surface is anisotropic </summary>
        public double CurvatureAlignment = 0;
        /// <summary> see CrossField.AlignCreases (used when Field is null): align a cross direction with crease edges whose dihedral angle exceeds CreaseAngleDegrees. PreserveCreases implies it: with PreserveCreases = true and a null Field the computed field aligns creases whatever this flag says </summary>
        public bool AlignCreases = false;
        /// <summary>
        /// optional (default false: bit-identical to without this option): preserve sharp creases (dihedral angle above
        /// CreaseAngleDegrees) as quad edges and corners: PositionField makes crease curves lattice lines and crease corners
        /// (turn above CornerAngleDegrees, junctions) fixed lattice points. When Field is null the cross field is computed with
        /// AlignCreases on (see AlignCreases). Not combined with PureQuads (InvalidInput: the pure-quad repair does not keep the feature curves,
        /// so a "successful" pure-quad result would silently lose the creases). After the extraction the crease vertices are moved onto the
        /// creases of the input and the corner vertices onto its corners (if within 0.25 and 0.35 edge lengths), the links along each crease
        /// join consecutive vertices only, and an edge along a curved crease whose middle is more than 0.08 edge lengths from the crease is
        /// split at the crease (at most 3 % of the faces; each split turns the two faces at it into pentagons, so up to 6 % of the faces and a
        /// result with 90 % quads keeps 84 %): the feature Hausdorff distance of the stage 4 gate depends less on the lattice spacing along a
        /// rim (issue #1452). Each step is kept only if it drops no more faces than the result before it. On a shape thinner than 0.25 edge
        /// lengths a vertex can snap to the crease of the other side. Not covered: a crease edge across a corner
        /// (a mesh triangle with all three edges creases, as the remesher can leave at a rim) is followed only by chance.
        /// </summary>
        public bool PreserveCreases = false;
        /// <summary>
        /// the one dihedral angle threshold (degrees, 0..180, default 30) for sharp creases: it is used by the computed cross field (when Field is null and
        /// AlignCreases or PreserveCreases is true) and by PositionField's crease constraints (when PreserveCreases is true)
        /// </summary>
        public double CreaseAngleDegrees = 30;

        /// <summary>
        /// Off (default): the quads have the edge length TargetEdgeLength everywhere. Scale or ScaleAndRotation: a size field (and a rotation
        /// of the cross field) from IntegrableFrames lets the lattice close up on curved surfaces, which halves the irregular vertices of
        /// closed smooth shapes (sphere, torus) at the price of edge lengths that vary by a factor of about 2 (docs/design/QUAD_REMESHING.md
        /// 4.5). It is used only if it lowers the curl by SizeAdaptationMinGain (otherwise the result is the uniform one); on shapes with sharp
        /// features (a filleted box) and with boundaries it may not help or may worsen the counts. Default Off: the output is then bit for bit the
        /// one without the option.
        /// </summary>
        public SizeAdaptationMode SizeAdaptation = SizeAdaptationMode.Off;
        /// <summary>
        /// optional: desired local edge length in mesh units at each point (null = off; default output is bit-identical to without this option).
        /// Solved through IntegrableFrames (see IntegrableFrames.SizeFunction; compatible with RemeshSizing.FromWallThickness).
        /// Applied whenever IntegrableFrames succeeds with at least one valid sample (and SizeWeight &gt; 0), without requiring the
        /// SizeAdaptationMinGain curl reduction. When SizeAdaptation is Off, solves for spacing only (no rotation); when SizeAdaptation is
        /// ScaleAndRotation, also allows rotation. Can be combined with PureQuads (the repair uses the local spacing).
        /// Not applied (Status stays Success, SizeAdaptationApplied is false) when SizeWeight is 0, when no valid sample falls on a vertex of a usable triangle, or when
        /// IntegrableFrames finds no solution; the sample counts (including the clamped ones) are on Frames after Compute().
        /// </summary>
        public Func<Vector3d, double> SizeFunction;
        /// <summary>
        /// optional: an IntegrableFrames for its solver settings (Iterations, Damping, BoundarySpacingWeight, SizeWeight, ...); the remesher sets
        /// its Mesh, Field, TargetEdgeLength, UseRotation (from SizeAdaptation) and SizeFunction and computes it, so after Compute() it holds
        /// the spacing, the rotation and the curl
        /// </summary>
        public IntegrableFrames Frames;
        /// <summary> the size field is applied only if the curl (IntegrableFrames.CurlAfter) is at most (1 - this) times CurlBefore </summary>
        public double SizeAdaptationMinGain = 0.2;
        /// <summary> of the last Compute(): the size field was applied, the curl before and after, the solver time in seconds, the smallest and largest spacing factor </summary>
        public bool SizeAdaptationApplied;
        public double CurlBefore, CurlAfter, SizeSeconds, MinSpacing = 1, MaxSpacing = 1;

        internal int ChunkSize = 512;

        /// <summary>
        /// Off (default): Result is the quad-dominant mesh of stage 2, bit for bit as without this option. On: the pure-quad repair
        /// runs after the stage 2 extraction; if it passes every check (in this order: the flow completes, the repair stays in its
        /// budget, no triangle of the integer map is inverted (exact), the extracted mesh is a manifold of quads only, no quad is flipped)
        /// Result is the mesh of quads only and PureQuadStatus is Success. If a check fails, the attempt is repeated once with the fixed
        /// variables of its flow freed (internal, QUAD_REMESHING.md 4.4d), and the mesh of that second attempt is used if it passes every
        /// check. If both fail, Result is the stage 2 mesh,
        /// identical to PureQuads = false, Status is still Success (a failed pure-quad attempt is not a failed remeshing) and
        /// PureQuadStatus names the first failed check of the first attempt (the counters and area ratios are the first attempt's too). Closed meshes only (a mesh with a boundary gives FlowInfeasible). The attempt succeeds on some meshes only (on shapes with long sharp
        /// features or strong curvature changes the integer map often has folds that the repair does not remove): test
        /// PureQuadStatus. Costs a few times the stage 2 time when the first attempt succeeds; an input that needs the second attempt costs
        /// about twice the attempt more (5 to 20 times the stage 2 time on the gate runs; QUAD_REMESHING.md, stage 3B and 4.4d).
        /// </summary>
        public bool PureQuads = false;
        /// <summary> the outcome of the pure-quad attempt of the last Compute(): NotRequested when PureQuads is false </summary>
        public PureQuadStatus PureQuadStatus = PureQuadStatus.NotRequested;
        /// <summary>
        /// DIAGNOSTIC ONLY, of the last Compute() with PureQuads = true (NaN otherwise, and when the attempt ended before or in the flow: an invalid input, a boundary, a failed setup, a flow without a solution): the lattice area of the integer
        /// map of the pure-quad repair over the geometric area of the input (QuadFlowRepair.AreaRatio): about 1 for a map that covers the surface once, lower for a map
        /// that lost lattice cells. It is not a fallback reason and does not change PureQuadStatus or Result; the value right after the flow is PureQuadAreaRatioAfterFlow
        /// </summary>
        public double PureQuadAreaRatio = double.NaN, PureQuadAreaRatioAfterFlow = double.NaN;
        /// <summary> the detection counters of the last pure-quad attempt: triangles of the repaired integer map with a negative determinant, and extracted quads that face away from the surface </summary>
        public int InvertedTriangles, FlippedQuads;
        /// <summary> seconds of the pure-quad attempt of the last Compute(): the offsets and the flow, then the subdivision and the local repair (the extraction is not counted here) </summary>
        public double FlowSeconds, RepairSeconds;
        /// <summary> test hook: false switches off the sign penalty of the flow cost (the plain marginal cost, which lets the sphere fold) </summary>
        internal bool SignPenalty = true;
        /// <summary> the last pure-quad attempt (profile and offsets for tests and benchmarks) </summary>
        internal QuadFlowRepair PureQuadRepair;
        /// <summary> test and benchmark hook: false skips the second pure-quad attempt (the behaviour before it existed) </summary>
        internal bool PureQuadRetry = true;
        /// <summary> of the last Compute(): the status of the first pure-quad attempt, and whether the second attempt ran (PureQuadRepair is the attempt that is reported) </summary>
        internal PureQuadStatus PureQuadFirstStatus = PureQuadStatus.NotRequested;
        internal bool PureQuadRetried;

        public QuadRemeshStatus Status = QuadRemeshStatus.Success;
        public string Error;
        public CrossFieldStatus FieldStatus = CrossFieldStatus.Success;
        public DPolyMesh Result;
        /// <summary> seconds of the last Compute(): the cross field (0 if given), the position field, the extraction with its cleanup </summary>
        public double FieldSeconds, PositionSeconds, ExtractSeconds;
        /// <summary>
        /// the position field of the last Compute(). For a mesh whose longest edge is outside [1, 2) the remesher runs on a copy scaled by a
        /// power of two (see Compute()): then this field refers to that copy and is in its unit scale, not in the units of Mesh
        /// (Result is in the units of Mesh)
        /// </summary>
        public PositionField Positions;

        public QuadRemesher(DMesh3 mesh) { Mesh = mesh; }

        public bool Success { get { return Status == QuadRemeshStatus.Success; } }

        /// <summary>
        /// Runs the remesher. The mesh is processed in a unit scale (#1377): a copy with the coordinates times 2^-k, k the binary
        /// exponent of the longest edge, so that a mesh scaled by 2^j gives the same quads bit for bit at every scale and the fourth
        /// powers of the parameterization stay in the double range; Result, SizeFunction and TargetEdgeLength are in the units of the mesh.
        /// Not for a mesh with edge lengths more than 2^200 apart (InvalidInput), nor with a given Field or Frames (they belong to Mesh):
        /// those run in the units of the mesh as before. Positions and the pure-quad repair refer to the scaled copy.
        /// </summary>
        public QuadRemeshStatus Compute()
        {
            if (Mesh != null && Mesh.TriangleCount > 0 && Field == null && Frames == null && TargetEdgeLength > 0 && !double.IsInfinity(TargetEdgeLength)) {
                if (IntrinsicTriangulation.HasEdgeDisparity(Mesh)) {
                    ResetResults();
                    return Fail(QuadRemeshStatus.InvalidInput, "edge lengths more than 2^200 apart");
                }
                int k = IntrinsicTriangulation.EdgeExponent(Mesh);
                if (k != 0) return ComputeScaled(k);
            }
            return ComputeCore();
        }

        QuadRemeshStatus ComputeScaled(int k)
        {
            DMesh3 caller = Mesh;
            double target = TargetEdgeLength;
            Func<Vector3d, double> size = SizeFunction;
            var scaled = new DMesh3(caller);
            foreach (int vid in caller.VertexIndices()) {
                Vector3d p = caller.GetVertex(vid);
                scaled.SetVertex(vid, new Vector3d(ExactPoint3.ScaleB(p.x, -k), ExactPoint3.ScaleB(p.y, -k), ExactPoint3.ScaleB(p.z, -k)));
            }
            double targetU = ExactPoint3.ScaleB(target, -k);
            if (!(targetU > 0)) {
                ResetResults();
                return Fail(QuadRemeshStatus.InvalidInput, "TargetEdgeLength is below the range of the mesh");
            }
            Mesh = scaled; TargetEdgeLength = targetU;
            if (size != null)
                SizeFunction = p => ExactPoint3.ScaleB(size(new Vector3d(ExactPoint3.ScaleB(p.x, k), ExactPoint3.ScaleB(p.y, k), ExactPoint3.ScaleB(p.z, k))), -k);
            try {
                ComputeCore();
            } finally {
                Mesh = caller; TargetEdgeLength = target; SizeFunction = size;
            }
            if (Result != null)
                for (int v = 0; v < Result.VertexCount; ++v) {
                    Vector3d p = Result.GetVertex(v);
                    Result.SetVertex(v, new Vector3d(ExactPoint3.ScaleB(p.x, k), ExactPoint3.ScaleB(p.y, k), ExactPoint3.ScaleB(p.z, k)));
                }
            return Status;
        }

        // the state of a run that has not started (every result field of the last Compute)
        void ResetResults()
        {
            Status = QuadRemeshStatus.Success; Error = null; FieldStatus = CrossFieldStatus.Success; Result = null; Positions = null;
            FieldSeconds = PositionSeconds = ExtractSeconds = 0;
            CollapsedEdges = RemovedValence2 = DroppedFaces = 0;
            SizeAdaptationApplied = false; CurlBefore = CurlAfter = SizeSeconds = 0; MinSpacing = MaxSpacing = 1;
            PureQuadStatus = PureQuadFirstStatus = PureQuadStatus.NotRequested; PureQuadRetried = false; PureQuadAreaRatio = PureQuadAreaRatioAfterFlow = double.NaN; InvertedTriangles = FlippedQuads = 0; FlowSeconds = RepairSeconds = 0; PureQuadRepair = null;
        }

        QuadRemeshStatus ComputeCore()
        {
            ResetResults();
            if (Mesh == null || Mesh.TriangleCount == 0) return Fail(QuadRemeshStatus.InvalidInput, "no triangles");
            if (!(TargetEdgeLength > 0) || double.IsInfinity(TargetEdgeLength)) return Fail(QuadRemeshStatus.InvalidInput, "TargetEdgeLength must be positive and finite");
            if (!(CornerAngleDegrees >= 0 && CornerAngleDegrees <= 180)) return Fail(QuadRemeshStatus.InvalidInput, "CornerAngleDegrees must be in [0, 180]");
            if (!(CurvatureAlignment >= 0) || double.IsInfinity(CurvatureAlignment)) return Fail(QuadRemeshStatus.InvalidInput, "CurvatureAlignment must be non-negative and finite");
            if (!(CreaseAngleDegrees >= 0 && CreaseAngleDegrees <= 180)) return Fail(QuadRemeshStatus.InvalidInput, "CreaseAngleDegrees must be in [0, 180]");
            if (PreserveCreases && PureQuads) return Fail(QuadRemeshStatus.InvalidInput, "PreserveCreases and PureQuads cannot be combined (the pure-quad repair does not keep the feature curves)");
            if (SizeAdaptation != SizeAdaptationMode.Off) {
                if (SizeAdaptation != SizeAdaptationMode.Scale && SizeAdaptation != SizeAdaptationMode.ScaleAndRotation) return Fail(QuadRemeshStatus.InvalidInput, "unknown SizeAdaptation");
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CrossField cf = Field;
            if (cf == null) {
                cf = new CrossField(Mesh) { CurvatureAlignment = CurvatureAlignment, AlignCreases = AlignCreases || PreserveCreases, CreaseAngleDegrees = CreaseAngleDegrees };
                cf.Compute();
                FieldSeconds = sw.Elapsed.TotalSeconds;
            }
            if (!cf.Success) { FieldStatus = cf.Status; return Fail(QuadRemeshStatus.FieldFailed, cf.Error); }
            var pf = new PositionField(Mesh, cf) {
                TargetEdgeLength = TargetEdgeLength, Sweeps = Sweeps, Parallel = Parallel, CornerAngleDegrees = CornerAngleDegrees,
                MiddleRegularization = MiddleRegularization, PreserveCreases = PreserveCreases,
                CreaseAngleDegrees = CreaseAngleDegrees, ChunkSize = ChunkSize
            };
            if (SizeAdaptation != SizeAdaptationMode.Off || SizeFunction != null) {
                if (SizeAdaptation != SizeAdaptationMode.Off && SizeAdaptation != SizeAdaptationMode.Scale && SizeAdaptation != SizeAdaptationMode.ScaleAndRotation) return Fail(QuadRemeshStatus.InvalidInput, "unknown SizeAdaptation");
                IntegrableFrames fr = Frames ?? new IntegrableFrames(Mesh, cf);
                fr.Mesh = Mesh; fr.Field = cf; fr.TargetEdgeLength = TargetEdgeLength;
                fr.UseRotation = SizeAdaptation == SizeAdaptationMode.ScaleAndRotation;
                fr.SizeFunction = SizeFunction;
                if (fr.Compute() == IntegrableFramesStatus.InvalidInput) return Fail(QuadRemeshStatus.InvalidInput, fr.Error);
                SizeSeconds = fr.Seconds;
                CurlBefore = fr.CurlBefore; CurlAfter = fr.CurlAfter;
                bool apply = fr.Success && (fr.SizeTargetApplied || (SizeAdaptation != SizeAdaptationMode.Off && fr.CurlBefore > 0 && fr.CurlAfter <= (1 - SizeAdaptationMinGain) * fr.CurlBefore));
                if (apply) {
                    pf.SpacingFactors = fr.Spacing; pf.RotationAngles = fr.Rotation;
                    SizeAdaptationApplied = true; MinSpacing = fr.MinSpacing; MaxSpacing = fr.MaxSpacing;
                }
            }
            sw.Restart();
            Positions = pf;
            if (pf.Compute() != PositionFieldStatus.Success) return Fail(QuadRemeshStatus.PositionFieldFailed, pf.Error);
            PositionSeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            Result = Extract(pf);
            ExtractSeconds = sw.Elapsed.TotalSeconds;
            if (Result.FaceCount == 0) { Result = null; return Fail(QuadRemeshStatus.NoFaces, "no face was extracted"); }
            if (PureQuads) {
                var rep = new QuadFlowRepair(Mesh, cf, pf, TargetEdgeLength) { SignPenalty = SignPenalty };
                PureQuadRepair = rep;
                PureQuadStatus = PureQuadFirstStatus = rep.Run();
                FlowSeconds = rep.FlowSeconds; RepairSeconds = rep.RepairSeconds;
                // the second attempt, only after a first one that is not Success and whose flow has fixed variables (a mesh with a boundary
                // or a failed setup has none and would fail the same way): the same repair with the fixed variables of the flow freed
                // (QuadFlowRepair.FreeFrozenEdges). It is used only if it is Success; otherwise everything reported is the first attempt's
                if (PureQuadRetry && PureQuadStatus != PureQuadStatus.Success && rep.FrozenEdges > 0) {
                    var second = new QuadFlowRepair(Mesh, cf, pf, TargetEdgeLength) { SignPenalty = SignPenalty, FreeFrozenEdges = true };
                    PureQuadRetried = true;
                    PureQuadStatus secondStatus = second.Run();
                    FlowSeconds += second.FlowSeconds; RepairSeconds += second.RepairSeconds;
                    if (secondStatus == PureQuadStatus.Success) { rep = second; PureQuadRepair = second; PureQuadStatus = secondStatus; }
                }
                InvertedTriangles = rep.InvertedTriangles; FlippedQuads = rep.FlippedQuads;
                PureQuadAreaRatio = rep.AreaRatio; PureQuadAreaRatioAfterFlow = rep.AreaRatioAfterFlow;
                if (PureQuadStatus == PureQuadStatus.Success) Result = rep.Result;
            }
            return Status;
        }

        QuadRemeshStatus Fail(QuadRemeshStatus s, string error) { Status = s; Error = error; return s; }

        // ------------------------------------------------------------------------------------------------ extraction

        internal sealed class PolyGraph
        {
            public List<Vector3d> Pos = new List<Vector3d>();
            public List<Vector3d> Nrm = new List<Vector3d>();
            public List<List<int>> Adj = new List<List<int>>();
            public List<byte> Kind = new List<byte>();      // 0 interior, 1 boundary or crease, 2 corner
            public List<double> Spc = new List<double>();   // the lattice spacing factor of the vertex (1: uniform spacing; the size field of SizeAdaptation)
            public int Add(Vector3d p, Vector3d n, byte kind) { Pos.Add(p); Nrm.Add(n); Adj.Add(new List<int>()); Kind.Add(kind); Spc.Add(1.0); return Pos.Count - 1; }
            public bool Has(int a, int b) { return Adj[a].Contains(b); }
            public void Link(int a, int b) { if (a == b || Adj[a].Contains(b)) return; Adj[a].Add(b); Adj[b].Add(a); }
            public void Unlink(int a, int b) { Adj[a].Remove(b); Adj[b].Remove(a); }
        }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

        DPolyMesh Extract(PositionField pf)
        {
            PositionField.Level L = pf.L0;
            int n = L.N;
            int[] uf = new int[n];
            for (int i = 0; i < n; ++i) uf[i] = i;
            var unitA = new List<int>(); var unitB = new List<int>();
            for (int i = 0; i < n; ++i)
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) {
                    int j = L.AJ[k];
                    if (j < i) continue;
                    int dq, dt; double mismatch;
                    pf.SlotOffset(i, k, out dq, out dt, out mismatch);
                    if (mismatch > MismatchLimit) continue;
                    if (dq == 0 && dt == 0) { int ri = Find(uf, i), rj = Find(uf, j); if (ri != rj) uf[Math.Max(ri, rj)] = Math.Min(ri, rj); }
                    else if (Math.Abs(dq) + Math.Abs(dt) == 1) { unitA.Add(i); unitB.Add(j); }
                }
            var g = new PolyGraph();
            int[] id = new int[n];
            for (int i = 0; i < n; ++i) id[i] = -1;
            // a cluster is at the mean lattice point of its members; the members of the highest kind decide (a corner of the boundary
            // is a lattice point itself, a boundary vertex is on its lattice line: interior members must not pull them off)
            var sumP = new List<Vector3d>(); var sumN = new List<Vector3d>(); var cnt = new List<int>(); var kinds = new List<byte>(); var sumSpc = new List<double>();
            for (int pass = 0; pass < 2; ++pass)
                for (int i = 0; i < n; ++i) {
                    int r = Find(uf, i);
                    if (pass == 0) {
                        if (id[r] < 0) { id[r] = sumP.Count; sumP.Add(Vector3d.Zero); sumN.Add(Vector3d.Zero); cnt.Add(0); kinds.Add(0); sumSpc.Add(0); }
                        int c0 = id[r];
                        if (!PreserveCreases) sumN[c0] += L.Nrm[i];
                        if (L.Kind[i] > kinds[c0]) kinds[c0] = L.Kind[i];
                    } else {
                        int c = id[r];
                        if (L.Kind[i] != kinds[c]) continue;
                        if (PreserveCreases) sumN[c] += L.Nrm[i];
                        sumP[c] += pf.Origins[i]; cnt[c]++; sumSpc[c] += L.Sc == null ? 1.0 : L.Sc[i];
                    }
                }
            for (int c = 0; c < sumP.Count; ++c) {
                Vector3d nn = sumN[c]; double nl = nn.Length;
                int gi = g.Add(sumP[c] / cnt[c], nl > 0 ? nn / nl : sumN[c], kinds[c]);
                if (L.Sc != null) g.Spc[gi] = sumSpc[c] / cnt[c];
            }
            for (int u = 0; u < unitA.Count; ++u) {
                int a = id[Find(uf, unitA[u])], b = id[Find(uf, unitB[u])];
                if (a != b) g.Link(a, b);
            }
            CollapsedEdges = 0; RemovedValence2 = 0;
            if (Cleanup) CleanupGraph(g, pf.TargetEdgeLength, CollapseLength, out CollapsedEdges, out RemovedValence2, PreserveCreases);
            int dropped;
            if (PreserveCreases) {
                // issue #1452: the lattice graph does not follow the creases exactly (see CreaseIndex): the crease vertices are moved onto
                // them, the links along each crease are made to join consecutive vertices only, and an edge along a curved crease that
                // is too long for the curve is split at the crease
                var creases = new CreaseIndex(Mesh, CreaseAngleDegrees, CornerAngleDegrees, pf.TargetEdgeLength);
                if (creases.Count > 0) return BuildWithCreaseRepair(g, creases, MaxFaceSides, out DroppedFaces, planarize: !SkipCrossingRepair);
            }
            DPolyMesh poly = BuildFaces(g, MaxFaceSides, out dropped);
            if (dropped > 0 && !SkipCrossingRepair) poly = TryPlanarize(g, MaxFaceSides, poly, ref dropped);     // (g is pruned by BuildFaces: the same edges)
            DroppedFaces = dropped;
            return poly;
        }

        // ---- creases (PreserveCreases, issue #1452)

        /// <summary> the fraction of the faces that the crease repair may split at most (each split turns two quads into pentagons) </summary>
        internal const double CreaseSplitBudget = 0.03;

        /// <summary>
        /// BuildFaces after the crease repair of CreaseIndex (snap, links, splits). The repair can merge two faces above maxSides (a removed
        /// chord) or a split can grow one above it; each step is kept only if it drops no more faces than the result before it (the plain graph
        /// first), else the result before it stays. At most CreaseSplitBudget of the faces are split, the farthest edges first, so a result with
        /// 90 % quads keeps 84 % and more (up to 6 % of the faces become pentagons).
        /// </summary>
        /// <summary> (tests) off: the crossing edges of the graph are not repaired (#1674) </summary>
        internal bool SkipCrossingRepair;

        internal static DPolyMesh BuildWithCreaseRepair(PolyGraph g, CreaseIndex creases, int maxSides, out int droppedFaces, double splitBudget = CreaseSplitBudget, bool planarize = true)
        {
            PolyGraph plainGraph = CopyGraph(g);
            creases.Snap(g);
            creases.RepairLinks(g);
            int droppedRepaired, droppedPlain;
            DPolyMesh repaired = BuildFaces(g, maxSides, out droppedRepaired);
            DPolyMesh plain = BuildFaces(plainGraph, maxSides, out droppedPlain);
            DPolyMesh result; PolyGraph resultGraph;
            // (the graphs the faces were built from, for the repair of crossing edges below: BuildFaces only prunes dangling edges from them; the repaired graph is copied before the splits change it, if it dropped a face)
            PolyGraph repairedGraph = null;
            if (droppedRepaired > droppedPlain) { droppedFaces = droppedPlain; result = plain; resultGraph = plainGraph; }
            else {
                droppedFaces = droppedRepaired; result = repaired; resultGraph = repairedGraph = droppedRepaired > 0 && planarize ? CopyGraph(g) : g;
                if (creases.SplitLongEdges(g, (int)(splitBudget * repaired.FaceCount))) {
                    int droppedRefined;
                    DPolyMesh refined = BuildFaces(g, maxSides, out droppedRefined);
                    if (droppedRefined <= droppedRepaired) { droppedFaces = droppedRefined; result = refined; resultGraph = g; }
                }
            }
            // an input without a hole must not get one: edges that cross each other make the faces of the extraction fold (issue #1674)
            if (planarize && droppedFaces > 0) {
                // the chosen graph first, then the graphs of the other results, if they are not the one tried (the splits changed g after the repaired result was built)
                var graphs = new List<PolyGraph> { resultGraph };
                if (repairedGraph != null && repairedGraph != resultGraph && repairedGraph != g) graphs.Add(repairedGraph);
                if (plainGraph != resultGraph) graphs.Add(plainGraph);
                result = TryPlanarize(graphs, maxSides, result, ref droppedFaces);
            }
            return result;
        }

        /// <summary> TryPlanarize of each graph in turn until no face is dropped (each improvement is the result the next is compared with) </summary>
        internal static DPolyMesh TryPlanarize(IList<PolyGraph> graphs, int maxSides, DPolyMesh current, ref int dropped)
        {
            foreach (PolyGraph graph in graphs) {
                if (dropped == 0) break;
                current = TryPlanarize(graph, maxSides, current, ref dropped);
            }
            return current;
        }

        /// <summary>
        /// The faces of a copy of the graph without its crossing edges (RemoveCrossings), kept only if fewer faces are dropped than in the
        /// result so far (current, with dropped faces); else current.
        /// </summary>
        internal static DPolyMesh TryPlanarize(PolyGraph graph, int maxSides, DPolyMesh current, ref int dropped)
        {
            PolyGraph c = CopyGraph(graph);
            if (RemoveCrossings(c) == 0) return current;
            int d;
            DPolyMesh r = BuildFaces(c, maxSides, out d);
            if (d >= dropped) return current;
            dropped = d;
            return r;
        }

        /// <summary>
        /// Unlinks the longer edge of every pair of graph edges that cross each other (no common vertex, the segments cross properly in the plane of
        /// the mean normal of their ends, and are no farther apart than a fifth of the shorter one): the lattice extraction can link two
        /// vertices over a valence-3 vertex of another patch that lies on or next to the link, whose own third link then crosses it. The
        /// faces are traced from the order of the neighbours around each vertex, which such a crossing breaks (a loop that visits vertices twice
        /// is dropped and the surface opens). The edges of a pair are compared by length, then by their vertex numbers, and all are
        /// removed at the end, so the result does not depend on the order of the search. An edge between two crease or boundary vertices (Kind above 0)
        /// is a link of a chain: if that is the longer one, the other goes instead, and if both are, neither (the chain stays connected).
        /// Returns the number of edges removed.
        /// </summary>
        internal static int RemoveCrossings(PolyGraph g)
        {
            int nv = g.Pos.Count;
            var ea = new List<int>(); var eb = new List<int>();
            double sumLen = 0;
            for (int i = 0; i < nv; ++i) foreach (int j in g.Adj[i]) if (i < j) { ea.Add(i); eb.Add(j); sumLen += (g.Pos[i] - g.Pos[j]).Length; }
            int ne = ea.Count;
            if (ne < 2 || !(sumLen > 0)) return 0;
            double cell = 2 * sumLen / ne;
            var cells = new Dictionary<long, List<int>>();
            for (int e = 0; e < ne; ++e) {
                Vector3d p = g.Pos[ea[e]], q = g.Pos[eb[e]];
                double pad = CrossingDistance * (p - q).Length;       // the other edge's crossing point can be that far from this one (EdgesCross), in any direction
                long x0 = (long)Math.Floor((Math.Min(p.x, q.x) - pad) / cell), x1 = (long)Math.Floor((Math.Max(p.x, q.x) + pad) / cell);
                long y0 = (long)Math.Floor((Math.Min(p.y, q.y) - pad) / cell), y1 = (long)Math.Floor((Math.Max(p.y, q.y) + pad) / cell);
                long z0 = (long)Math.Floor((Math.Min(p.z, q.z) - pad) / cell), z1 = (long)Math.Floor((Math.Max(p.z, q.z) + pad) / cell);
                if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 4096) continue;       // an edge that long is no lattice edge
                for (long x = x0; x <= x1; ++x) for (long y = y0; y <= y1; ++y) for (long z = z0; z <= z1; ++z) {
                    long key = (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
                    List<int> l; if (!cells.TryGetValue(key, out l)) { l = new List<int>(); cells[key] = l; }
                    l.Add(e);
                }
            }
            var remove = new HashSet<int>();
            var tested = new HashSet<long>();
            foreach (var kv in cells) {
                List<int> l = kv.Value;
                for (int i = 0; i < l.Count; ++i)
                    for (int j = i + 1; j < l.Count; ++j) {
                        int e = Math.Min(l[i], l[j]), f = Math.Max(l[i], l[j]);
                        if (!tested.Add((long)e * ne + f)) continue;
                        int a = ea[e], b = eb[e], c = ea[f], d = eb[f];
                        if (a == c || a == d || b == c || b == d) continue;
                        if (EdgesCross(g, a, b, c, d)) {
                            int drop = LongerEdge(g, ea, eb, e, f), keep = drop == e ? f : e;
                            // a link between two boundary / crease vertices is part of a chain: the other edge goes (neither if both are links)
                            if (IsChainLink(g, ea[drop], eb[drop])) { if (IsChainLink(g, ea[keep], eb[keep])) continue; drop = keep; }
                            remove.Add(drop);
                        }
                    }
            }
            foreach (int e in remove.OrderBy(x => x)) g.Unlink(ea[e], eb[e]);
            return remove.Count;
        }

        static bool IsChainLink(PolyGraph g, int a, int b) { return g.Kind[a] >= 1 && g.Kind[b] >= 1; }

        static int LongerEdge(PolyGraph g, List<int> ea, List<int> eb, int e, int f)
        {
            double le = (g.Pos[ea[e]] - g.Pos[eb[e]]).LengthSquared, lf = (g.Pos[ea[f]] - g.Pos[eb[f]]).LengthSquared;
            return le > lf ? e : lf > le ? f : Math.Min(e, f);
        }

        /// <summary> two edges cross only if their crossing points are no farther apart than this part of the shorter edge </summary>
        const double CrossingDistance = 0.2;

        static bool EdgesCross(PolyGraph g, int a, int b, int c, int d)
        {
            Vector3d n = g.Nrm[a] + g.Nrm[b] + g.Nrm[c] + g.Nrm[d];
            double nl = n.Length;
            if (!(nl > 1.0)) return false;           // the normals of the ends do not agree (a sharp edge or two sheets): no plane to project on
            n /= nl;
            Vector3d pa = g.Pos[a], pb = g.Pos[b], pc = g.Pos[c], pd = g.Pos[d];
            Vector3d ab = pb - pa, cd = pd - pc;
            double o1 = n.Dot(ab.Cross(pc - pa)), o2 = n.Dot(ab.Cross(pd - pa)), o3 = n.Dot(cd.Cross(pa - pc)), o4 = n.Dot(cd.Cross(pb - pc));
            if (!((o1 > 0 && o2 < 0 || o1 < 0 && o2 > 0) && (o3 > 0 && o4 < 0 || o3 < 0 && o4 > 0))) return false;      // (signs, not products: no underflow)
            // the point of the crossing on each, along the segment: their distance in the direction of the normal
            double u = o1 / (o1 - o2);        // on (c, d)
            Vector3d x = pa + (o3 / (o3 - o4)) * ab, y = pc + u * cd;
            double near = CrossingDistance * Math.Min(ab.Length, cd.Length);
            return (x - y).Length <= near;
        }

        static PolyGraph CopyGraph(PolyGraph g)
        {
            var c = new PolyGraph();
            for (int i = 0; i < g.Pos.Count; ++i) { c.Add(g.Pos[i], g.Nrm[i], g.Kind[i]); c.Spc[i] = g.Spc[i]; }
            for (int i = 0; i < g.Pos.Count; ++i) foreach (int j in g.Adj[i]) if (i < j) c.Link(i, j);
            return c;
        }

        /// <summary>
        /// The creases of the input (MeshTopology with CreaseAngleDegrees) as chains of segments: each chain runs between two vertices where
        /// the crease ends or meets others (degree other than 2), or is a closed loop, with the arc length along it. Used to correct the
        /// lattice graph of PositionField at creases, which the extraction draws only approximately: a crease vertex lies on a straight
        /// lattice line (up to 0.12 s off a curved or facetted crease), a corner cluster is the mean of merged lattice points (up to 0.14 s off
        /// its corner), two lattice points of a stretched crease can be joined over a vertex between them (a chord) while two consecutive ones are
        /// not joined, and a quad edge along a curved crease can be so long that its middle is more than 0.1 s from the curve.
        /// </summary>
        internal sealed class CreaseIndex
        {
            readonly List<Vector3d> A = new List<Vector3d>(), B = new List<Vector3d>();
            readonly List<int> chainOf = new List<int>();
            readonly List<double> arc0 = new List<double>();          // arc length at A along the chain
            readonly List<double> chainLength = new List<double>();
            readonly List<bool> chainClosed = new List<bool>();
            readonly List<Vector3d> corners = new List<Vector3d>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            readonly double s, cell;

            public int Count { get { return A.Count; } }

            public CreaseIndex(DMesh3 mesh, double creaseAngleDegrees, double cornerAngleDegrees, double edgeLength)
            {
                s = edgeLength; cell = Math.Max(2 * edgeLength, double.Epsilon);
                var topo = new MeshTopology(mesh) { CreaseAngle = creaseAngleDegrees };
                topo.Compute();
                var nb = new SortedDictionary<int, List<int>>();       // vertex -> crease edges
                foreach (int eid in topo.CreaseEdges.OrderBy(e => e)) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    if (!nb.ContainsKey(ev.a)) nb[ev.a] = new List<int>();
                    if (!nb.ContainsKey(ev.b)) nb[ev.b] = new List<int>();
                    nb[ev.a].Add(eid); nb[ev.b].Add(eid);
                }
                double cosCorner = Math.Cos(cornerAngleDegrees * Math.PI / 180);
                foreach (var kv in nb) {
                    bool corner = kv.Value.Count != 2;
                    if (!corner) {
                        Index2i e0 = mesh.GetEdgeV(kv.Value[0]), e1 = mesh.GetEdgeV(kv.Value[1]);
                        Vector3d p = mesh.GetVertex(kv.Key);
                        Vector3d d0 = p - mesh.GetVertex(e0.a == kv.Key ? e0.b : e0.a), d1 = mesh.GetVertex(e1.a == kv.Key ? e1.b : e1.a) - p;
                        double l0 = d0.Length, l1 = d1.Length;
                        corner = !(l0 > 0 && l1 > 0) || d0.Dot(d1) / (l0 * l1) < cosCorner;
                    }
                    if (corner) corners.Add(mesh.GetVertex(kv.Key));
                }
                var used = new HashSet<int>();
                // open chains from the vertices of degree != 2, then the loops
                foreach (var kv in nb) {
                    if (kv.Value.Count == 2) continue;
                    foreach (int e in kv.Value) if (!used.Contains(e)) Walk(mesh, nb, used, kv.Key, e, false);
                }
                foreach (var kv in nb) {
                    foreach (int e in kv.Value) if (!used.Contains(e)) Walk(mesh, nb, used, kv.Key, e, true);
                }
                for (int i = 0; i < A.Count; ++i) InsertSegment(i);
            }

            // the cells the segment passes through (a grid traversal), so a long segment costs its length and not the volume of its box
            void InsertSegment(int i)
            {
                Vector3d a = A[i], d = B[i] - A[i];
                var c = new long[3]; var e = new long[3]; var step = new int[3]; var tMax = new double[3]; var tDelta = new double[3];
                long total = 1;
                for (int k = 0; k < 3; ++k) {
                    c[k] = (long)Math.Floor(a[k] / cell); e[k] = (long)Math.Floor(B[i][k] / cell);
                    total += Math.Abs(e[k] - c[k]);
                    if (d[k] > 0) { step[k] = 1; tMax[k] = ((c[k] + 1) * cell - a[k]) / d[k]; tDelta[k] = cell / d[k]; }
                    else if (d[k] < 0) { step[k] = -1; tMax[k] = (c[k] * cell - a[k]) / d[k]; tDelta[k] = -cell / d[k]; }
                    else { tMax[k] = tDelta[k] = double.PositiveInfinity; }
                }
                for (long n = 0; n < total; ++n) {
                    List<int> l; long key = Key(c[0], c[1], c[2]);
                    if (!cells.TryGetValue(key, out l)) { l = new List<int>(); cells[key] = l; }
                    l.Add(i);
                    int m = tMax[0] <= tMax[1] ? (tMax[0] <= tMax[2] ? 0 : 2) : (tMax[1] <= tMax[2] ? 1 : 2);
                    if (c[m] == e[m]) { m = -1; for (int k = 0; k < 3; ++k) if (c[k] != e[k] && (m < 0 || tMax[k] < tMax[m])) m = k; if (m < 0) break; }
                    c[m] += step[m]; tMax[m] += tDelta[m];
                }
            }

            void Walk(DMesh3 mesh, SortedDictionary<int, List<int>> nb, HashSet<int> used, int v0, int e0, bool closed)
            {
                int chain = chainLength.Count; double arc = 0; int v = v0, e = e0;
                while (true) {
                    used.Add(e);
                    Index2i ev = mesh.GetEdgeV(e);
                    int w = ev.a == v ? ev.b : ev.a;
                    Vector3d pa = mesh.GetVertex(v), pb = mesh.GetVertex(w);
                    A.Add(pa); B.Add(pb); chainOf.Add(chain); arc0.Add(arc);
                    arc += (pb - pa).Length;
                    v = w;
                    List<int> es = nb[v];
                    if (es.Count != 2) break;
                    int next = es[0] == e ? es[1] : es[0];
                    if (used.Contains(next)) break;
                    e = next;
                }
                chainLength.Add(arc); chainClosed.Add(closed || (v == v0));
            }

            static long Key(long x, long y, long z) { return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42); }
            Tuple<long, long, long> Cell(Vector3d p) { return Tuple.Create((long)Math.Floor(p.x / cell), (long)Math.Floor(p.y / cell), (long)Math.Floor(p.z / cell)); }

            /// <summary> the crease point nearest to p within maxDist: its segment, the parameter on it and the distance (false if none) </summary>
            public bool Nearest(Vector3d p, double maxDist, out int seg, out double t, out double dist)
            {
                seg = -1; t = 0; dist = double.MaxValue;
                var lo = Cell(new Vector3d(p.x - maxDist, p.y - maxDist, p.z - maxDist)); var hi = Cell(new Vector3d(p.x + maxDist, p.y + maxDist, p.z + maxDist));
                for (long x = lo.Item1; x <= hi.Item1; ++x) for (long y = lo.Item2; y <= hi.Item2; ++y) for (long z = lo.Item3; z <= hi.Item3; ++z) {
                    List<int> l;
                    if (!cells.TryGetValue(Key(x, y, z), out l)) continue;
                    foreach (int i in l) {
                        Vector3d ab = B[i] - A[i]; double l2 = ab.LengthSquared;
                        double tt = l2 > 0 ? Math.Max(0, Math.Min(1, (p - A[i]).Dot(ab) / l2)) : 0;
                        double d = (p - (A[i] + tt * ab)).Length;
                        if (d < dist || (d == dist && i < seg)) { dist = d; seg = i; t = tt; }
                    }
                }
                return seg >= 0 && dist <= maxDist;
            }

            Vector3d PointAt(int seg, double t) { return A[seg] + t * (B[seg] - A[seg]); }
            double ArcAt(int seg, double t) { return arc0[seg] + t * (B[seg] - A[seg]).Length; }

            /// <summary>
            /// Moves the crease vertices (kind 1) within 0.25 edge lengths of a crease onto it (the nearest point), and the corner vertices
            /// (kind 2) within 0.35 edge lengths of a corner of the creases onto it
            /// </summary>
            public int Snap(PolyGraph g)
            {
                int moved = 0;
                for (int v = 0; v < g.Pos.Count; ++v) {
                    if (g.Kind[v] < 1) continue;
                    Vector3d p = g.Pos[v];
                    if (g.Kind[v] == 2) {
                        Vector3d best = p; double bd = double.MaxValue;
                        foreach (Vector3d c in corners) { double d = (p - c).Length; if (d < bd) { bd = d; best = c; } }
                        if (bd > 0 && bd <= 0.35 * s) { g.Pos[v] = best; moved++; }
                        continue;
                    }
                    int seg; double t, dist;
                    if (Nearest(p, 0.25 * s, out seg, out t, out dist) && dist > 0) { g.Pos[v] = PointAt(seg, t); moved++; }
                }
                return moved;
            }

            // the graph vertices of kind 1 and 2 on each chain (within 0.05 s of it), by arc length
            List<List<KeyValuePair<double, int>>> VerticesByChain(PolyGraph g)
            {
                var lists = new List<List<KeyValuePair<double, int>>>();
                for (int c = 0; c < chainLength.Count; ++c) lists.Add(new List<KeyValuePair<double, int>>());
                for (int v = 0; v < g.Pos.Count; ++v) {
                    if (g.Kind[v] < 1) continue;
                    int seg; double t, dist;
                    if (!Nearest(g.Pos[v], 0.05 * s, out seg, out t, out dist)) continue;
                    lists[chainOf[seg]].Add(new KeyValuePair<double, int>(ArcAt(seg, t), v));
                }
                foreach (var l in lists) l.Sort((x, y) => { int c = x.Key.CompareTo(y.Key); return c != 0 ? c : x.Value.CompareTo(y.Value); });
                return lists;
            }

            /// <summary>
            /// The vertices on a crease in order of the arc length: consecutive ones are joined (if the crease between them is at most 2.5 edge
            /// lengths long), two that are joined with another vertex between them (a chord) are not. A closed chain wraps around.
            /// </summary>
            public int RepairLinks(PolyGraph g)
            {
                int changes = 0;
                var lists = VerticesByChain(g);
                for (int c = 0; c < lists.Count; ++c) {
                    var l = lists[c]; int n = l.Count;
                    if (n < 2) continue;
                    bool closed = chainClosed[c];
                    // chords first: a link between vertices that are not neighbours in the order (the shorter way round for a loop)
                    var pos = new Dictionary<int, int>();
                    for (int i = 0; i < n; ++i) pos[l[i].Value] = i;
                    for (int i = 0; i < n; ++i) {
                        int u = l[i].Value;
                        foreach (int w in g.Adj[u].ToArray()) {
                            int j;
                            if (!pos.TryGetValue(w, out j) || j <= i) continue;
                            int gap = j - i; if (closed) gap = Math.Min(gap, n - gap);
                            if (gap > 1 && l[j].Key - l[i].Key > 1e-9 * s && (g.Pos[u] - g.Pos[w]).Length > 0) { g.Unlink(u, w); changes++; }
                        }
                    }
                    int pairs = closed ? n : n - 1;
                    for (int i = 0; i < pairs; ++i) {
                        int u = l[i].Value, w = l[(i + 1) % n].Value;
                        if (u == w || g.Has(u, w)) continue;
                        double gapLen = (i + 1 < n) ? l[i + 1].Key - l[i].Key : chainLength[c] - l[i].Key + l[0].Key;
                        if (gapLen > 1e-9 * s && gapLen <= 2.5 * s) { g.Link(u, w); changes++; }
                    }
                }
                return changes;
            }

            // an edge between two crease vertices is split at the crease if its middle is farther than this from it (in edge lengths)
            const double SplitTolerance = 0.08;

            /// <summary>
            /// An edge between two consecutive vertices of a crease whose middle is farther than 0.08 edge lengths from the crease is split at the
            /// point of the crease nearest to its middle (the farthest first, at most budget splits, at most 6 rounds): the quads of a curved crease (a rim) are straight
            /// chords, and the lattice spacing along it varies by up to a factor 2, so a chord of 1.7 s on a circle of 4 s radius is 0.12 s off.
            /// The two faces at the edge get a corner (a quad becomes a pentagon). True if a vertex was added.
            /// </summary>
            public bool SplitLongEdges(PolyGraph g, int budget)
            {
                bool any = false;
                for (int round = 0; round < 6 && budget > 0; ++round) {
                    // the candidates of this round, the farthest from the crease first (ties by the vertex ids), as many as the budget allows
                    var cand = new List<(double d, int a, int b, int seg, double t)>();
                    var lists = VerticesByChain(g);
                    for (int ci = 0; ci < lists.Count; ++ci) {
                        var l = lists[ci];
                        int n = l.Count, pairs = chainClosed[ci] ? n : n - 1;
                        for (int i = 0; i < pairs && n > 1; ++i) {
                            int a = l[i].Value, b = l[(i + 1) % n].Value;
                            if (a == b || !g.Has(a, b)) continue;
                            double gap = i + 1 < n ? l[i + 1].Key - l[i].Key : chainLength[ci] - l[i].Key + l[0].Key;
                            // the point of the chord farthest from the crease, of the quarter points and the middle (the chord is off most near the middle)
                            int seg = -1; double t = 0, d = 0;
                            foreach (double f in new[] { 0.25, 0.5, 0.75 }) {
                                int sg; double tt, dd;
                                if (Nearest(g.Pos[a] + f * (g.Pos[b] - g.Pos[a]), 0.5 * s, out sg, out tt, out dd) && dd > d) { d = dd; seg = sg; t = tt; }
                            }
                            if (seg < 0 || d <= SplitTolerance * s) continue;
                            double rel = ArcAt(seg, t) - l[i].Key;
                            if (rel < 0 && chainClosed[ci]) rel += chainLength[ci];
                            if (!(rel > 0.2 * gap && rel < 0.8 * gap)) continue;
                            cand.Add((d, a, b, seg, t));
                        }
                    }
                    if (cand.Count == 0) break;
                    cand.Sort((x, y) => { int c = y.d.CompareTo(x.d); if (c != 0) return c; c = x.a.CompareTo(y.a); return c != 0 ? c : x.b.CompareTo(y.b); });
                    foreach (var c in cand) {
                        if (budget <= 0) break;
                        if (!g.Has(c.a, c.b)) continue;
                        Vector3d nn = g.Nrm[c.a] + g.Nrm[c.b]; double nl = nn.Length;
                        int p = g.Add(PointAt(c.seg, c.t), nl > 0 ? nn / nl : g.Nrm[c.a], 1);
                        g.Spc[p] = 0.5 * (g.Spc[c.a] + g.Spc[c.b]);
                        g.Unlink(c.a, c.b); g.Link(c.a, p); g.Link(p, c.b);
                        any = true; budget--;
                    }
                }
                return any;
            }
        }

        /// <summary> removes the vertices of valence 1 and the chains they leave; true if there was one </summary>
        internal static bool PruneDangling(PolyGraph g)
        {
            bool any = false;
            var stack = new Stack<int>();
            for (int k = 0; k < g.Adj.Count; ++k) if (g.Adj[k].Count == 1) stack.Push(k);
            while (stack.Count > 0) {
                int k = stack.Pop();
                if (g.Adj[k].Count != 1) continue;
                int o = g.Adj[k][0];
                any = true;
                g.Unlink(k, o);
                if (g.Adj[o].Count == 1) stack.Push(o);
            }
            return any;
        }

        /// <summary>
        /// edges shorter than this (in units of the edge length, of the local spacing with SizeAdaptation) are collapsed by the cleanup: their end points are one point. Default 0.6 (owner
        /// 2026-11-19; it was 0.4): collapses short boundary edges (open patch 32 -> 17 irregular vertices before #1179, 9 / 99.2 % quads after #1179),
        /// and closed shapes change by at most 2 irregular vertices (QUAD_REMESHING.md 4.2)
        /// </summary>
        public double CollapseLength = 0.6;
        /// <summary> short edges collapsed and valence 2 vertices removed by the last Compute() </summary>
        public int CollapsedEdges, RemovedValence2;
        /// <summary> faces of the traced graph that are not in the result (the outer loops of an open mesh, folded faces, faces above MaxFaceSides) </summary>
        public int DroppedFaces;

        // The paper's cleanup: collapse of near-coincident points (edges shorter than CollapseLength s, shortest first; a corner
        // of the boundary keeps its position), removal of valence 2 vertices that are not on the boundary (the two edges become
        // one), and the dangling edges that these leave; repeated while something changes.
        internal static void CleanupGraph(PolyGraph g, double s, double collapseLength, out int collapsedEdges, out int removedValence2, bool preserveCreases = false)
        {
            collapsedEdges = 0; removedValence2 = 0;
            double lim0 = collapseLength * s;       // the limit is relative to the local spacing (a factor 1 without a size field)
            for (int round = 0; round < 8; ++round) {
                bool changed = false;
                var cand = new List<(double, int, int)>();
                for (int a = 0; a < g.Pos.Count; ++a)
                    foreach (int b in g.Adj[a])
                        if (a < b) { double l = (g.Pos[a] - g.Pos[b]).Length; if (l < lim0 * Math.Min(g.Spc[a], g.Spc[b])) cand.Add((l, a, b)); }
                cand.Sort();
                foreach (var c in cand) {
                    int a = c.Item2, b = c.Item3;
                    if (!g.Has(a, b) || (g.Pos[a] - g.Pos[b]).Length >= lim0 * Math.Min(g.Spc[a], g.Spc[b])) continue;
                    if (g.Kind[a] == 2 && g.Kind[b] == 2) continue;
                    if (g.Kind[b] > g.Kind[a]) { int t = a; a = b; b = t; }
                    // the vertex of the higher kind keeps its place (a boundary/crease vertex stays on its lattice line, a corner on its point)
                    Vector3d p = g.Kind[a] > g.Kind[b] ? g.Pos[a] : 0.5 * (g.Pos[a] + g.Pos[b]);
                    g.Pos[a] = p;
                    g.Spc[a] = 0.5 * (g.Spc[a] + g.Spc[b]);
                    if (!(preserveCreases && g.Kind[a] > g.Kind[b])) {
                        Vector3d nn = g.Nrm[a] + g.Nrm[b]; double nl = nn.Length; if (nl > 0) g.Nrm[a] = nn / nl;
                    }
                    foreach (int x in g.Adj[b].ToArray()) { g.Unlink(b, x); if (x != a) g.Link(a, x); }
                    collapsedEdges++; changed = true;
                }
                for (int v = 0; v < g.Pos.Count; ++v)
                    if (g.Adj[v].Count == 2 && g.Kind[v] == 0) {
                        int a = g.Adj[v][0], b = g.Adj[v][1];
                        g.Unlink(v, a); g.Unlink(v, b); g.Link(a, b);
                        removedValence2++; changed = true;
                    }
                if (PruneDangling(g)) changed = true;
                if (!changed) break;
            }
        }

        // ------------------------------------------------------------------------------------------------ faces

        /// <summary>
        /// the faces of the polygon graph: for every vertex the neighbours sorted counter-clockwise in the tangent plane (normal Nrm),
        /// every directed edge is traced to the face on its left; the faces are turned as a whole to agree with the normals (a majority vote); faces that
        /// face away from the normals (the outer loops of an open graph, folded faces) and faces with more than maxSides sides are
        /// dropped (counted in dropped). Dangling edges are pruned first. Vertices without a face are not in the result.
        /// </summary>
        internal static DPolyMesh BuildFaces(PolyGraph g, int maxSides, out int dropped)
        {
            PruneDangling(g);
            int nv = g.Pos.Count;
            // neighbours around each vertex, counter-clockwise about the normal, from the first neighbour
            var order = new int[nv][];
            for (int k = 0; k < nv; ++k) {
                List<int> nb = g.Adj[k];
                if (nb.Count == 0) { order[k] = new int[0]; continue; }
                Vector3d nk = g.Nrm[k];
                Vector3d d0 = g.Pos[nb[0]] - g.Pos[k]; Vector3d e = d0 - d0.Dot(nk) * nk;
                double el = e.Length; if (el > 0) e /= el;
                Vector3d b = nk.Cross(e);
                var keys = new double[nb.Count]; var ids = nb.ToArray();
                for (int u = 0; u < ids.Length; ++u) { Vector3d d = g.Pos[ids[u]] - g.Pos[k]; keys[u] = Math.Atan2(d.Dot(b), d.Dot(e)); }
                Array.Sort(keys, ids);
                order[k] = ids;
            }
            var used = new bool[nv][];
            for (int k = 0; k < nv; ++k) used[k] = new bool[order[k].Length];
            var faces = new List<int[]>();
            for (int u = 0; u < nv; ++u)
                for (int s0 = 0; s0 < order[u].Length; ++s0) {
                    if (used[u][s0]) continue;
                    var face = new List<int>();
                    int a = u, sa = s0; bool closed = true;
                    while (!used[a][sa]) {
                        used[a][sa] = true; face.Add(a);
                        int b = order[a][sa];
                        int r = Array.IndexOf(order[b], a);
                        int sb = (r - 1 + order[b].Length) % order[b].Length;
                        a = b; sa = sb;
                        if (face.Count > 4 * maxSides + 64) { closed = false; break; }
                    }
                    if (closed && a == u && sa == s0) faces.Add(face.ToArray());
                }
            // orientation: face loops are traced with one winding; make it agree with the normals
            double agree = 0;
            var area = new double[faces.Count];
            for (int f = 0; f < faces.Count; ++f) {
                Vector3d nw = Vector3d.Zero, nsum = Vector3d.Zero; int[] fv = faces[f];
                Vector3d p0 = g.Pos[fv[0]];
                for (int k = 1; k + 1 < fv.Length; ++k) nw += (g.Pos[fv[k]] - p0).Cross(g.Pos[fv[k + 1]] - p0);
                foreach (int v in fv) nsum += g.Nrm[v];
                area[f] = nw.Dot(nsum);
                agree += Math.Sign(area[f]);
            }
            bool flip = agree < 0;
            var mesh = new DPolyMesh();
            dropped = 0;
            // the faces that stay, their loops turned to agree with the normals
            var kept = new List<int>(); var loops = new int[faces.Count][];
            for (int f = 0; f < faces.Count; ++f) {
                int[] fv = faces[f];
                double a = flip ? -area[f] : area[f];
                if (!(a > 0) || fv.Length < 3 || fv.Length > maxSides) { dropped++; continue; }     // the outer loop of an open mesh, a folded face, a hole
                if (flip) { fv = (int[])fv.Clone(); Array.Reverse(fv); }
                loops[f] = fv; kept.Add(f);
            }
            // A vertex whose kept faces form more than one fan (the faces touch only at the vertex: a degenerate star of the extraction, a hole or the outer
            // loop between them) is split into one copy per fan, numbered by the smallest face index of the fan: the polygon mesh is then manifold
            // at every vertex. A mesh without such a vertex is built exactly as before.
            var inc = new List<int>[nv];
            foreach (int f in kept) foreach (int v in loops[f]) { if (inc[v] == null) inc[v] = new List<int>(); inc[v].Add(f); }
            Dictionary<long, int> fanOf = null;      // (face, vertex) -> fan number above 0
            for (int v = 0; v < nv; ++v) {
                List<int> fs = inc[v];
                if (fs == null || fs.Count < 2) continue;
                var parent = new int[fs.Count];
                for (int i = 0; i < parent.Length; ++i) parent[i] = i;
                var byNb = new Dictionary<int, int>();
                for (int i = 0; i < fs.Count; ++i) {
                    int[] lv = loops[fs[i]]; int c = Array.IndexOf(lv, v);
                    foreach (int w in new[] { lv[(c + 1) % lv.Length], lv[(c + lv.Length - 1) % lv.Length] }) {
                        int o;
                        if (byNb.TryGetValue(w, out o)) { int ra = Find(parent, i), rb = Find(parent, o); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }
                        else byNb[w] = i;
                    }
                }
                int fans = 0;
                for (int i = 0; i < fs.Count; ++i) if (Find(parent, i) == i) fans++;
                if (fans < 2) continue;
                var number = new Dictionary<int, int>();     // root -> fan number, in the order of the faces
                for (int i = 0; i < fs.Count; ++i) {
                    int r = Find(parent, i), n;
                    if (!number.TryGetValue(r, out n)) { n = number.Count; number[r] = n; }
                    if (n > 0) { if (fanOf == null) fanOf = new Dictionary<long, int>(); fanOf[(long)fs[i] * nv + v] = n; }
                }
            }
            int[] remap = new int[nv];
            for (int k = 0; k < nv; ++k) remap[k] = -1;
            var copies = fanOf == null ? null : new Dictionary<long, int>();      // (vertex, fan) -> mesh vertex
            var buf = new List<int>();
            foreach (int f in kept) {
                int[] fv = loops[f];
                buf.Clear();
                for (int k = 0; k < fv.Length; ++k) {
                    int v = fv[k], fan;
                    if (fanOf != null && fanOf.TryGetValue((long)f * nv + v, out fan)) {
                        long key = (long)v * (nv + 1) + fan; int id;
                        if (!copies.TryGetValue(key, out id)) { id = mesh.AppendVertex(g.Pos[v].x, g.Pos[v].y, g.Pos[v].z); copies[key] = id; }
                        buf.Add(id);
                    } else {
                        if (remap[v] < 0) { remap[v] = mesh.AppendVertex(g.Pos[v].x, g.Pos[v].y, g.Pos[v].z); }
                        buf.Add(remap[v]);
                    }
                }
                mesh.AppendFace(buf, 0);
            }
            return mesh;
        }
    }
}
