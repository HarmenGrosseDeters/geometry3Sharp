// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3.CoreMathPort;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> Result of IntegrableFrames.Compute(). </summary>
    public enum IntegrableFramesStatus
    {
        Success = 0,
        /// <summary> no mesh, a failed cross field, a target edge length that is not positive and finite, or invalid settings </summary>
        InvalidInput = 1,
        /// <summary> no vertex with a field, no triangle without a field singularity, non-finite edge vectors, or the initial Cholesky factorization failed (the result stays the uniform spacing) </summary>
        NoSolution = 2
    }

    /// <summary>
    /// A size field (and optionally a small rotation of the cross field) that makes the lattice of the position field closer to a
    /// lattice that exists. A coframe of unit spacing along a cross field closes up (a lattice of constant spacing exists) only where the
    /// surface is flat, so on a curved surface the position field (PositionField) has to absorb the rest in dislocations (a triangle
    /// with a valence 5 vertex), about 60 of them for a sphere of radius 1 at spacing 0.1, against 8 singularities of the field
    /// (docs/design/QUAD_REMESHING.md 4.5). With a spacing that varies smoothly (by a factor of about 2 across a sphere: the cells shrink
    /// towards the cones, as on a cube sphere) and a rotation of up to some 10 degrees, most of that disappears.
    ///
    /// The method: for every triangle without a singularity of the field, w is the sum over its three edges of the edge vector in the
    /// averaged, aligned cross frame of the edge, in units of the target edge length (a vector of lattice units that is zero where the
    /// frames close up; its sum over a loop is the Burgers vector of the loop). With a log spacing sigma_i and a rotation phi_i per
    /// vertex, the edge contributes exp(-sigma_e) Rot(phi_e) a_e (sigma_e and phi_e the means of the two end points). Damped Gauss-Newton
    /// on the sum of |w|^2, every step a sparse Cholesky solve of (J^T J + Damping I) d = -J^T w (Levenberg: the fixed point is the
    /// minimum whatever Damping is), the mean of sigma kept at 0 (the gauge: a constant spacing would make w small for nothing), a
    /// step that does not decrease the cost is halved. The scale and rotation freedom of the global parametrization of Bommes, Zimmer,
    /// Kobbelt, "Mixed-Integer Quadrangulation" (SIGGRAPH 2009) is the idea, the formulation is ours (written from the paper's
    /// idea, no code; docs/SOURCES.md).
    ///
    /// Input: a successful CrossField of Mesh and TargetEdgeLength. Output: Spacing and Rotation per vertex id, for
    /// PositionField.SpacingFactors / RotationAngles (QuadRemesher.SizeAdaptation / SizeFunction do that). Boundary vertices keep
    /// rotation 0 (eliminated from the normal equations), open-boundary spacing is regularized by BoundarySpacingWeight, and the
    /// triangles at singularities are left out. An optional SizeFunction supplies desired local edge lengths (with weight SizeWeight;
    /// when active, the mean-log-spacing = 0 gauge is omitted so the user's absolute scale is preserved). Serial and deterministic:
    /// the same input gives the same output bit for bit; power of two scalings of the mesh with the same scaling of TargetEdgeLength
    /// (and of SizeFunction) give the same output bit for bit. Compute() can be called again.
    /// </summary>
    public class IntegrableFrames
    {
        public DMesh3 Mesh;
        public CrossField Field;
        /// <summary> the edge length of the quads (the mean spacing: the mean of log Spacing is 0) </summary>
        public double TargetEdgeLength = 0;
        /// <summary> false: only the spacing is solved for (the rotation stays 0; one unknown per vertex instead of two, since 2026-11-19) </summary>
        public bool UseRotation = true;
        /// <summary> Gauss-Newton steps at most; stops earlier when a step lowers the cost by less than StopTolerance (relative) </summary>
        public int Iterations = 40;
        public double StopTolerance = 0.001;
        /// <summary>
        /// the normal matrix J^T J + Damping I is factorized only at every RefactorEvery-th step (and after a step that needed backtracking);
        /// the steps in between solve with the older factors and the current gradient J^T w (a descent direction for any positive definite matrix)
        /// </summary>
        public int RefactorEvery = 8;
        /// <summary> the Levenberg damping of the normal equations (dimensionless) </summary>
        public double Damping = 0.01;
        /// <summary> the rotation of a vertex stays within this angle (degrees) </summary>
        public double MaxRotationDegrees = 45;
        /// <summary> every step is clamped to [1 / MaxSpacingRatio, MaxSpacingRatio]; the shift to mean log spacing 0 can widen that, so the largest spacing is at most MaxSpacingRatio^2 times the smallest </summary>
        public double MaxSpacingRatio = 2.5;
        /// <summary>
        /// regularization weight pulling boundary vertices' log spacing toward 0 (uniform spacing 1) on open meshes, preventing
        /// boundary quads from expanding to MaxSpacingRatio on curved open rims (eg sphere-cap). 0 disables it; has no effect on closed meshes
        /// or on boundary vertices that have an explicit SizeFunction target.
        /// </summary>
        public double BoundarySpacingWeight = 0.05;
        /// <summary>
        /// optional: desired local edge length in mesh units at each vertex (null = off, uniform mean TargetEdgeLength).
        /// Evaluated serially at each vertex with a field, so it need not be thread-safe (must be deterministic for a deterministic result).
        /// Values that are NaN, not positive, or infinite mean "no size constraint at this vertex" and are counted in InvalidSizeSamples.
        /// Valid values are converted to log(h / TargetEdgeLength), clamped to [-log(MaxSpacingRatio), log(MaxSpacingRatio)], and pulled
        /// with weight SizeWeight. When at least one used vertex has a valid sample (and SizeWeight &gt; 0), the mean-log-spacing = 0 gauge
        /// is omitted so the user's absolute sizing scale is preserved.
        /// </summary>
        public Func<Vector3d, double> SizeFunction;
        /// <summary> weight of the user SizeFunction target term 0.5 * SizeWeight * (sigma_i - target_i)^2 per vertex with a valid sample (dimensionless; must be finite and &gt;= 0; 0 disables the target term) </summary>
        public double SizeWeight = 1.0;

        // test hook: the first trial step is this multiple of the Gauss-Newton step (an overshoot that the backtracking has to halve)
        internal double InitialStepFactor = 1;
        // test hook: solve the scale-only problem with the rotation unknown as well (2 per vertex, its normal matrix is the scalar one plus a diagonal), the layout before the scalar path
        internal bool ForceTwoUnknowns;
        // test hook: unknowns per vertex of the last Compute() (1: sigma only, 2: sigma and phi)
        internal int UnknownsPerVertex;
        // true when SizeFunction had at least one valid sample on a used vertex and SizeWeight > 0
        internal bool SizeTargetApplied;

        public IntegrableFramesStatus Status = IntegrableFramesStatus.Success;
        public string Error;
        /// <summary> per vertex id (length MaxVertexID): the spacing factor (1 for a vertex without a field) and the rotation in radians </summary>
        public double[] Spacing, Rotation;
        /// <summary> the sum of |w| over the triangles used (lattice units) before and after: the dislocations the position field would need are about this many </summary>
        public double CurlBefore, CurlAfter;
        /// <summary> triangles used, Gauss-Newton steps taken, the smallest and largest spacing factor </summary>
        public int TrianglesUsed, StepsTaken;
        /// <summary> vertices with a field where SizeFunction returned a valid (positive, finite) or invalid (NaN, non-positive, infinite) value; both 0 when SizeFunction is null; reset by Compute() </summary>
        public int ValidSizeSamples, InvalidSizeSamples;
        /// <summary> valid SizeFunction samples whose log ratio to TargetEdgeLength was clamped to [-log(MaxSpacingRatio), log(MaxSpacingRatio)]: the requested size is not reached there (a target more than MaxSpacingRatio times larger or smaller than TargetEdgeLength is silently limited); a subset of ValidSizeSamples; reset by Compute() </summary>
        public int ClampedSizeSamples;
        public double MinSpacing = 1, MaxSpacing = 1;
        /// <summary> the largest rotation in degrees </summary>
        public double MaxRotationUsedDegrees;
        public double Seconds;

        public IntegrableFrames(DMesh3 mesh, CrossField field) { Mesh = mesh; Field = field; }

        public bool Success { get { return Status == IntegrableFramesStatus.Success; } }

        IntegrableFramesStatus Fail(IntegrableFramesStatus s, string error) { Status = s; Error = error; return s; }

        public IntegrableFramesStatus Compute()
        {
            Status = IntegrableFramesStatus.Success; Error = null; Spacing = null; Rotation = null;
            CurlBefore = CurlAfter = 0; UnknownsPerVertex = 0; SizeTargetApplied = false; TrianglesUsed = 0; StepsTaken = 0; ValidSizeSamples = InvalidSizeSamples = ClampedSizeSamples = 0; MinSpacing = MaxSpacing = 1; MaxRotationUsedDegrees = 0; Seconds = 0;
            if (Mesh == null || Field == null || !Field.Success) return Fail(IntegrableFramesStatus.InvalidInput, "a successful cross field is required");
            double s = TargetEdgeLength;
            if (!(s > 0) || double.IsInfinity(s)) return Fail(IntegrableFramesStatus.InvalidInput, "TargetEdgeLength must be positive and finite");
            if (Iterations < 1 || RefactorEvery < 1 || !(Damping > 0) || double.IsInfinity(Damping) || !(MaxRotationDegrees >= 0) || double.IsInfinity(MaxRotationDegrees)
                || !(MaxSpacingRatio >= 1) || double.IsInfinity(MaxSpacingRatio) || !(StopTolerance >= 0) || double.IsInfinity(StopTolerance)
                || !(BoundarySpacingWeight >= 0) || double.IsInfinity(BoundarySpacingWeight) || !(SizeWeight >= 0) || double.IsInfinity(SizeWeight))
                return Fail(IntegrableFramesStatus.InvalidInput, "invalid settings");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            int maxId = Mesh.MaxVertexID;
            var toVertex = new List<int>(); var toC = new int[maxId];
            for (int v = 0; v < maxId; ++v) { toC[v] = -1; if (Mesh.IsVertex(v) && Field.HasField(v)) { toC[v] = toVertex.Count; toVertex.Add(v); } }
            int n = toVertex.Count;
            Spacing = new double[maxId]; Rotation = new double[maxId];
            for (int v = 0; v < maxId; ++v) Spacing[v] = 1;
            if (n == 0) return Fail(IntegrableFramesStatus.NoSolution, "no vertex has a field");

            var V = new Vector3d[n]; var N = new Vector3d[n]; var Q = new Vector3d[n]; var bnd = new bool[n];
            for (int i = 0; i < n; ++i) { int v = toVertex[i]; V[i] = Mesh.GetVertex(v); N[i] = Field.VertexNormal(v); Q[i] = Field.VertexDirection(v, 0); bnd[i] = Mesh.IsBoundaryVertex(v); }
            var singular = new HashSet<int>(Field.SingularFaces);
            var tri = new List<int[]>(); var edgeVec = new List<Vector2d[]>();
            foreach (int t in Mesh.TriangleIndices()) {
                if (singular.Contains(t)) continue;
                Index3i tv = Mesh.GetTriangle(t);
                int[] c = { toC[tv.a], toC[tv.b], toC[tv.c] };
                if (c[0] < 0 || c[1] < 0 || c[2] < 0) continue;
                Vector3d qa = Q[c[0]], na = N[c[0]];
                var a = new Vector2d[3]; bool ok = true;
                for (int e = 0; e < 3 && ok; ++e) {
                    int i = c[e], j = c[(e + 1) % 3];
                    Vector3d q0, q1, u0, u1;
                    PositionField.CompatOrient(qa, na, Q[i], N[i], out q0, out q1);
                    PositionField.CompatOrient(qa, na, Q[j], N[j], out u0, out u1);
                    Vector3d qe = q1 + u1; double ql = qe.Length;
                    if (!(ql > 1e-9)) { ok = false; break; }
                    qe /= ql;
                    Vector3d ev = (V[j] - V[i]) / s;
                    a[e] = new Vector2d(ev.Dot(qe), ev.Dot(na.Cross(qe)));
                    if (double.IsNaN(a[e].x) || double.IsNaN(a[e].y) || double.IsInfinity(a[e].x) || double.IsInfinity(a[e].y)) ok = false;
                }
                if (!ok) continue;
                tri.Add(c); edgeVec.Add(a);
            }
            int m = tri.Count;
            TrianglesUsed = m;
            var ef = new EdgeFactors(tri);
            var used = new bool[n]; int nUsed = 0; int nUsedBnd = 0;
            foreach (int[] c in tri) foreach (int i in c) if (!used[i]) { used[i] = true; nUsed++; if (bnd[i]) nUsedBnd++; }
            if (m == 0) return Fail(IntegrableFramesStatus.NoSolution, "no triangle without a singularity of the field");

            var sig = new double[n]; var phi = new double[n];
            double maxPhi = MaxRotationDegrees * Math.PI / 180, maxSig = CoreMath.Log(MaxSpacingRatio);
            bool useRot = UseRotation && maxPhi > 0;

            double[] targetSig = null; bool[] hasTarget = null;
            int validUsedTargets = 0;
            if (SizeFunction != null) {
                targetSig = new double[n]; hasTarget = new bool[n];
                for (int i = 0; i < n; ++i) {
                    double h = SizeFunction(V[i]);
                    if (h > 0 && !double.IsInfinity(h)) {
                        double lg = CoreMath.Log(h / s);
                        if (lg > maxSig || lg < -maxSig) ClampedSizeSamples++;
                        targetSig[i] = Math.Max(-maxSig, Math.Min(maxSig, lg));
                        hasTarget[i] = true;
                        ValidSizeSamples++;
                        if (used[i]) validUsedTargets++;
                    } else InvalidSizeSamples++;
                }
            }
            double ws = (hasTarget != null && validUsedTargets > 0) ? SizeWeight : 0;
            SizeTargetApplied = ws > 0;
            double wb = nUsedBnd > 0 ? BoundarySpacingWeight : 0;

            // CurlBefore is always the curl of the uniform lattice at TargetEdgeLength (ws = 0 here so large SizeWeight cannot overflow before sig = targetSig)
            double cost = Cost(tri, edgeVec, ef, sig, phi, used, bnd, wb, hasTarget, targetSig, 0, useRot, out CurlBefore);
            CurlAfter = CurlBefore;
            if (double.IsNaN(cost) || double.IsInfinity(cost)) { Spacing = null; Rotation = null; return Fail(IntegrableFramesStatus.NoSolution, "the edge vectors are not finite in units of TargetEdgeLength"); }
            if (ws > 0) {
                for (int i = 0; i < n; ++i) if (hasTarget[i]) sig[i] = targetSig[i];
                cost = Cost(tri, edgeVec, ef, sig, phi, used, bnd, wb, hasTarget, targetSig, ws, useRot, out CurlAfter);
                if (double.IsNaN(cost) || double.IsInfinity(cost)) { Spacing = null; Rotation = null; return Fail(IntegrableFramesStatus.NoSolution, "the sized edge vectors are not finite in units of TargetEdgeLength"); }
            }
            // unknowns per vertex: sigma, and phi only if the rotation is solved for (a scale-only solve has no use for phi: half the unknowns)
            int u = useRot || ForceTwoUnknowns ? 2 : 1;
            UnknownsPerVertex = u;
            SparseCholesky chol = null; bool refactor = true; bool factorFailed = false;
            double[] gaugeVec = null; double gaugeDenom = 0;
            var w = new Vector2d[m]; var B = new double[m][];
            for (int step = 0; step < Iterations && cost > 0; ++step) {
                Linearize(tri, edgeVec, ef, sig, phi, bnd, useRot, w, B);
                var rhs = new double[u * n];
                for (int t = 0; t < m; ++t)
                    for (int k = 0; k < 3; ++k) {
                        int v = tri[t][k]; double[] b = B[t];
                        rhs[u * v] -= b[4 * k + 0] * w[t].x + b[4 * k + 2] * w[t].y;
                        if (u == 2) rhs[2 * v + 1] -= b[4 * k + 1] * w[t].x + b[4 * k + 3] * w[t].y;
                    }
                if (ws > 0)
                    for (int i = 0; i < n; ++i)
                        if (used[i] && hasTarget[i]) rhs[u * i] -= ws * (sig[i] - targetSig[i]);
                if (wb > 0)
                    for (int i = 0; i < n; ++i)
                        if (used[i] && bnd[i] && (ws == 0 || !hasTarget[i])) rhs[u * i] -= wb * sig[i];
                bool fresh = false;
                if (chol == null || refactor) {
                    fresh = true;
                    if (chol == null) chol = new SparseCholesky(u * n, u); else chol.Clear();
                    for (int t = 0; t < m; ++t) {
                        double[] b = B[t];
                        for (int k = 0; k < 3; ++k)
                            for (int l = k; l < 3; ++l)
                                for (int ca = 0; ca < u; ++ca)
                                    for (int cb = 0; cb < u; ++cb) {
                                        if (k == l && cb > ca) continue;       // the entries of one block are added once for the off-diagonal pairs
                                        chol.Add(u * tri[t][k] + ca, u * tri[t][l] + cb, b[4 * k + ca] * b[4 * l + cb] + b[4 * k + 2 + ca] * b[4 * l + 2 + cb]);
                                    }
                    }
                    if (ws > 0)
                        for (int i = 0; i < n; ++i)
                            if (used[i] && hasTarget[i]) chol.Add(u * i, u * i, ws);
                    if (wb > 0)
                        for (int i = 0; i < n; ++i)
                            if (used[i] && bnd[i] && (ws == 0 || !hasTarget[i])) chol.Add(u * i, u * i, wb);
                    for (int i = 0; i < u * n; ++i) chol.Add(i, i, Damping);
                    if (!chol.Factorize()) { factorFailed = true; break; }
                    if (ws == 0 && wb > 0) {
                        // KKT projection direction v = H^-1 1 for the exact gauge constraint sum_{used} d_sigma = 0
                        var ones = new double[u * n];
                        for (int i = 0; i < n; ++i) if (used[i]) ones[u * i] = 1;
                        gaugeVec = new double[u * n];
                        chol.Solve(ones, gaugeVec);
                        gaugeDenom = 0;
                        for (int i = 0; i < n; ++i) if (used[i]) gaugeDenom += gaugeVec[u * i];
                    }
                    refactor = false;
                }
                var d = new double[u * n];
                chol.Solve(rhs, d);
                if (ws > 0) {
                    // user SizeFunction anchors the absolute spacing scale: keep unused vertices fixed and do not shift mean(sigma)
                    for (int i = 0; i < n; ++i) {
                        if (!used[i]) d[u * i] = 0;
                        if (u == 2 && (!used[i] || bnd[i])) d[2 * i + 1] = 0;
                    }
                } else {
                    bool kktApplied = false;
                    if (wb > 0 && gaugeDenom > 0 && !double.IsInfinity(gaugeDenom)) {
                        double sumD = 0; for (int i = 0; i < n; ++i) if (used[i]) sumD += d[u * i];
                        double lam = sumD / gaugeDenom;
                        if (!double.IsNaN(lam) && !double.IsInfinity(lam)) {
                            for (int i = 0; i < n; ++i) {
                                d[u * i] = used[i] ? d[u * i] - lam * gaugeVec[u * i] : 0;
                                if (u == 2) d[2 * i + 1] = used[i] && !bnd[i] ? d[2 * i + 1] - lam * gaugeVec[2 * i + 1] : 0;
                            }
                            kktApplied = true;
                        }
                    }
                    if (!kktApplied) {
                        double dm = 0; for (int i = 0; i < n; ++i) if (used[i]) dm += d[u * i]; dm /= nUsed;
                        for (int i = 0; i < n; ++i) d[u * i] = used[i] ? d[u * i] - dm : 0;      // vertices of no used triangle keep spacing 1
                    }
                }
                // backtracking: halve a step that does not lower the cost
                double alpha = InitialStepFactor, newCost = cost, newCurl = CurlAfter; bool accepted = false;
                var sig2 = new double[n]; var phi2 = new double[n];
                for (int tries = 0; tries < 8; ++tries) {
                    for (int i = 0; i < n; ++i) {
                        sig2[i] = Math.Max(-maxSig, Math.Min(maxSig, sig[i] + alpha * d[u * i]));
                        phi2[i] = (bnd[i] || !useRot || u != 2) ? 0 : Math.Max(-maxPhi, Math.Min(maxPhi, phi[i] + alpha * d[2 * i + 1]));
                    }
                    newCost = Cost(tri, edgeVec, ef, sig2, phi2, used, bnd, wb, hasTarget, targetSig, ws, useRot, out newCurl);
                    if (newCost < cost) { accepted = true; break; }
                    alpha *= 0.5;
                }
                if (!accepted) { if (fresh) break; refactor = true; continue; }       // old factors: try again with new ones
                double gain = cost > 0 ? (cost - newCost) / cost : 0;
                Array.Copy(sig2, sig, n); Array.Copy(phi2, phi, n);
                if (alpha != InitialStepFactor || (step + 1) % RefactorEvery == 0) refactor = true;
                cost = newCost; CurlAfter = newCurl; StepsTaken++;
                if (gain < StopTolerance) break;
            }
            if (factorFailed && StepsTaken == 0) {
                SizeTargetApplied = false; CurlAfter = CurlBefore;
                Seconds = sw.Elapsed.TotalSeconds;
                return Fail(IntegrableFramesStatus.NoSolution, "the normal equations could not be factorized: " + chol.Error);
            }
            // without a user size field, enforce the gauge mean log spacing = 0 (a step keeps the mean, a clamp may not)
            if (ws == 0) {
                double mean = 0; for (int i = 0; i < n; ++i) if (used[i]) mean += sig[i]; mean /= nUsed;
                for (int i = 0; i < n; ++i) if (used[i]) sig[i] -= mean;
                Cost(tri, edgeVec, ef, sig, phi, used, bnd, wb, hasTarget, targetSig, ws, useRot, out CurlAfter);
            }
            double smin = double.MaxValue, smax = double.MinValue, pmax = 0;
            for (int i = 0; i < n; ++i) {
                int v = toVertex[i];
                Spacing[v] = CoreMath.Exp(sig[i]); Rotation[v] = phi[i];
                smin = Math.Min(smin, Spacing[v]); smax = Math.Max(smax, Spacing[v]); pmax = Math.Max(pmax, Math.Abs(phi[i]));
                if (!(Spacing[v] > 0) || double.IsInfinity(Spacing[v]) || double.IsNaN(Rotation[v])) { Spacing = null; Rotation = null; return Fail(IntegrableFramesStatus.NoSolution, "the solver left the finite range"); }
            }
            MinSpacing = smin; MaxSpacing = smax; MaxRotationUsedDegrees = pmax * 180 / Math.PI;
            Seconds = sw.Elapsed.TotalSeconds;
            return Status;
        }

        // the cost 1/2 sum |w|^2 (+ 1/2 ws sum_{target} (sig - target)^2 + 1/2 wb sum_{unconstrained bnd} sig^2) and the pure curl sum |w|
        static double Cost(List<int[]> tri, List<Vector2d[]> a, EdgeFactors ef, double[] sig, double[] phi, bool[] used, bool[] bnd, double wb, bool[] hasTarget, double[] targetSig, double ws, bool useRot, out double curl)
        {
            double cost = 0; curl = 0;
            ef.Fill(sig, phi, useRot);
            for (int t = 0; t < tri.Count; ++t) {
                Vector2d ws0 = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) ws0 += ef.Term(a[t][e], 3 * t + e, useRot);
                cost += 0.5 * ws0.LengthSquared; curl += ws0.Length;
            }
            if (ws > 0)
                for (int i = 0; i < sig.Length; ++i)
                    if (used[i] && hasTarget[i]) { double diff = sig[i] - targetSig[i]; cost += 0.5 * ws * diff * diff; }
            if (wb > 0)
                for (int i = 0; i < sig.Length; ++i)
                    if (used[i] && bnd[i] && (ws == 0 || !hasTarget[i])) cost += 0.5 * wb * sig[i] * sig[i];
            return cost;
        }

        // The factors of the edge terms, once per undirected edge and evaluation of (sig, phi): g = exp(-(sig_i + sig_j) / 2) and the cosine and
        // sine of (phi_i + phi_j) / 2. An interior edge is in two triangles, which evaluated them twice (the exp and the sin / cos are the cost of
        // the solver since the correctly rounded CoreMath, #1713). The same bits: the factors are symmetric in i and j, and IEEE addition is commutative.
        // Local to one Compute (built from its triangles), so a second Compute builds its own.
        internal sealed class EdgeFactors
        {
            readonly int[] slotEdge;           // triangle slot 3 t + e -> undirected edge
            readonly int[] ei, ej;             // the ends of each edge (as first met)
            readonly double[] g, cs, sn;

            internal EdgeFactors(List<int[]> tri)
            {
                slotEdge = new int[3 * tri.Count];
                var index = new Dictionary<long, int>();
                var a = new List<int>(); var b = new List<int>();
                for (int t = 0; t < tri.Count; ++t)
                    for (int e = 0; e < 3; ++e) {
                        int i = tri[t][e], j = tri[t][(e + 1) % 3];
                        long key = ((long)Math.Min(i, j) << 32) | (uint)Math.Max(i, j);
                        if (!index.TryGetValue(key, out int id)) { id = a.Count; index[key] = id; a.Add(i); b.Add(j); }
                        slotEdge[3 * t + e] = id;
                    }
                ei = a.ToArray(); ej = b.ToArray();
                g = new double[ei.Length]; cs = new double[ei.Length]; sn = new double[ei.Length];
            }

            internal int EdgeCount { get { return ei.Length; } }

            internal void Fill(double[] sig, double[] phi, bool useRot)
            {
                for (int k = 0; k < ei.Length; ++k) {
                    int i = ei[k], j = ej[k];
                    g[k] = CoreMath.Exp(-0.5 * (sig[i] + sig[j]));
                    if (useRot) { double pe = 0.5 * (phi[i] + phi[j]); cs[k] = CoreMath.Cos(pe); sn[k] = CoreMath.Sin(pe); }
                }
            }

            internal Vector2d Term(Vector2d a, int slot, bool useRot)
            {
                int k = slotEdge[slot];
                double gk = g[k];
                if (!useRot) return new Vector2d(gk * a.x, gk * a.y);
                double c = cs[k], s = sn[k];
                return new Vector2d(gk * (c * a.x + s * a.y), gk * (-s * a.x + c * a.y));      // the axes turn by +phi (PositionField.RotationAngles): the coordinates by -phi
            }
        }

        // the residual w per triangle and the 2 x 2 blocks of the Jacobian per vertex slot: [dwx/dsigma, dwx/dphi, dwy/dsigma, dwy/dphi]
        // boundary vertices keep phi = 0, so their phi Jacobian columns are zeroed out and decoupled in the normal equations
        static void Linearize(List<int[]> tri, List<Vector2d[]> a, EdgeFactors ef, double[] sig, double[] phi, bool[] bnd, bool useRot, Vector2d[] w, double[][] B)
        {
            ef.Fill(sig, phi, useRot);
            for (int t = 0; t < tri.Count; ++t) {
                int[] c = tri[t]; var ce = new Vector2d[3]; Vector2d ws = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) { ce[e] = ef.Term(a[t][e], 3 * t + e, useRot); ws += ce[e]; }
                w[t] = ws;
                var b = new double[12];
                for (int k = 0; k < 3; ++k) {
                    // vertex slot k starts edge k and ends edge k - 1; d term / d sigma_e = -term / 2 per end point, d term / d phi_e = -Rot90(term) / 2
                    Vector2d e1 = ce[k], e2 = ce[(k + 2) % 3];
                    b[4 * k + 0] = -0.5 * (e1.x + e2.x);
                    b[4 * k + 2] = -0.5 * (e1.y + e2.y);
                    if (useRot && !bnd[c[k]]) { b[4 * k + 1] = 0.5 * (e1.y + e2.y); b[4 * k + 3] = -0.5 * (e1.x + e2.x); }
                }
                B[t] = b;
            }
        }
    }
}
