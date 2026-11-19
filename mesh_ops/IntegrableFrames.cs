// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
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
        /// <summary> no triangle without a field singularity, or the solver found no usable step (the result is the uniform spacing) </summary>
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
    /// PositionField.SpacingFactors / RotationAngles (QuadRemesher.SizeAdaptation does that). Boundary vertices keep rotation 0 and
    /// the triangles at singularities are left out. Serial and deterministic: the same input gives the same output bit for bit; power of
    /// two scalings of the mesh with the same scaling of TargetEdgeLength give the same output bit for bit. Compute() can be called again.
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

        // test hook: the first trial step is this multiple of the Gauss-Newton step (an overshoot that the backtracking has to halve)
        internal double InitialStepFactor = 1;
        // test hook: solve the scale-only problem with the rotation unknown as well (2 per vertex, its normal matrix is the scalar one plus a diagonal), the layout before the scalar path
        internal bool ForceTwoUnknowns;
        // test hook: unknowns per vertex of the last Compute() (1: sigma only, 2: sigma and phi)
        internal int UnknownsPerVertex;

        public IntegrableFramesStatus Status = IntegrableFramesStatus.Success;
        public string Error;
        /// <summary> per vertex id (length MaxVertexID): the spacing factor (1 for a vertex without a field) and the rotation in radians </summary>
        public double[] Spacing, Rotation;
        /// <summary> the sum of |w| over the triangles used (lattice units) before and after: the dislocations the position field would need are about this many </summary>
        public double CurlBefore, CurlAfter;
        /// <summary> triangles used, Gauss-Newton steps taken, the smallest and largest spacing factor </summary>
        public int TrianglesUsed, StepsTaken;
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
            CurlBefore = CurlAfter = 0; UnknownsPerVertex = 0; TrianglesUsed = 0; StepsTaken = 0; MinSpacing = MaxSpacing = 1; MaxRotationUsedDegrees = 0; Seconds = 0;
            if (Mesh == null || Field == null || !Field.Success) return Fail(IntegrableFramesStatus.InvalidInput, "a successful cross field is required");
            double s = TargetEdgeLength;
            if (!(s > 0) || double.IsInfinity(s)) return Fail(IntegrableFramesStatus.InvalidInput, "TargetEdgeLength must be positive and finite");
            if (Iterations < 1 || RefactorEvery < 1 || !(Damping > 0) || !(MaxRotationDegrees >= 0) || !(MaxSpacingRatio >= 1) || !(StopTolerance >= 0))
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
            var used = new bool[n]; int nUsed = 0;
            foreach (int[] c in tri) foreach (int i in c) if (!used[i]) { used[i] = true; nUsed++; }
            if (m == 0) return Fail(IntegrableFramesStatus.NoSolution, "no triangle without a singularity of the field");

            var sig = new double[n]; var phi = new double[n];
            double maxPhi = MaxRotationDegrees * Math.PI / 180, maxSig = Math.Log(MaxSpacingRatio);
            bool useRot = UseRotation && maxPhi > 0;
            double cost = Cost(tri, edgeVec, sig, phi, useRot, out CurlBefore);
            CurlAfter = CurlBefore;
            if (double.IsNaN(cost) || double.IsInfinity(cost)) { Spacing = null; Rotation = null; return Fail(IntegrableFramesStatus.NoSolution, "the edge vectors are not finite in units of TargetEdgeLength"); }
            // unknowns per vertex: sigma, and phi only if the rotation is solved for (a scale-only solve has no use for phi: half the unknowns)
            int u = useRot || ForceTwoUnknowns ? 2 : 1;
            UnknownsPerVertex = u;
            SparseCholesky chol = null; bool refactor = true;
            var w = new Vector2d[m]; var B = new double[m][];
            for (int step = 0; step < Iterations; ++step) {
                Linearize(tri, edgeVec, sig, phi, useRot, w, B);
                var rhs = new double[u * n];
                for (int t = 0; t < m; ++t)
                    for (int k = 0; k < 3; ++k) {
                        int v = tri[t][k]; double[] b = B[t];
                        rhs[u * v] -= b[4 * k + 0] * w[t].x + b[4 * k + 2] * w[t].y;
                        if (u == 2) rhs[2 * v + 1] -= b[4 * k + 1] * w[t].x + b[4 * k + 3] * w[t].y;
                    }
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
                    for (int i = 0; i < u * n; ++i) chol.Add(i, i, Damping);
                    if (!chol.Factorize()) break;
                    refactor = false;
                }
                var d = new double[u * n];
                chol.Solve(rhs, d);
                double dm = 0; for (int i = 0; i < n; ++i) if (used[i]) dm += d[u * i]; dm /= nUsed;
                for (int i = 0; i < n; ++i) d[u * i] = used[i] ? d[u * i] - dm : 0;      // vertices of no used triangle keep spacing 1
                // backtracking: halve a step that does not lower the cost
                double alpha = InitialStepFactor, newCost = cost, newCurl = CurlAfter; bool accepted = false;
                var sig2 = new double[n]; var phi2 = new double[n];
                for (int tries = 0; tries < 8; ++tries) {
                    for (int i = 0; i < n; ++i) {
                        sig2[i] = Math.Max(-maxSig, Math.Min(maxSig, sig[i] + alpha * d[u * i]));
                        phi2[i] = (bnd[i] || !useRot || u != 2) ? 0 : Math.Max(-maxPhi, Math.Min(maxPhi, phi[i] + alpha * d[2 * i + 1]));
                    }
                    newCost = Cost(tri, edgeVec, sig2, phi2, useRot, out newCurl);
                    if (newCost < cost) { accepted = true; break; }
                    alpha *= 0.5;
                }
                if (!accepted) { if (fresh) break; refactor = true; continue; }       // old factors: try again with new ones
                double gain = (cost - newCost) / cost;
                Array.Copy(sig2, sig, n); Array.Copy(phi2, phi, n);
                if (alpha != InitialStepFactor || (step + 1) % RefactorEvery == 0) refactor = true;
                cost = newCost; CurlAfter = newCurl; StepsTaken++;
                if (gain < StopTolerance) break;
            }
            // the gauge: mean log spacing 0 (a step keeps the mean, a clamp may not)
            double mean = 0; for (int i = 0; i < n; ++i) if (used[i]) mean += sig[i]; mean /= nUsed;
            for (int i = 0; i < n; ++i) if (used[i]) sig[i] -= mean;
            Cost(tri, edgeVec, sig, phi, useRot, out CurlAfter);
            double smin = double.MaxValue, smax = double.MinValue, pmax = 0;
            for (int i = 0; i < n; ++i) {
                int v = toVertex[i];
                Spacing[v] = Math.Exp(sig[i]); Rotation[v] = phi[i];
                smin = Math.Min(smin, Spacing[v]); smax = Math.Max(smax, Spacing[v]); pmax = Math.Max(pmax, Math.Abs(phi[i]));
                if (!(Spacing[v] > 0) || double.IsInfinity(Spacing[v]) || double.IsNaN(Rotation[v])) { Spacing = null; Rotation = null; return Fail(IntegrableFramesStatus.NoSolution, "the solver left the finite range"); }
            }
            MinSpacing = smin; MaxSpacing = smax; MaxRotationUsedDegrees = pmax * 180 / Math.PI;
            Seconds = sw.Elapsed.TotalSeconds;
            return Status;
        }

        // the cost 1/2 sum |w|^2 (returned) and the sum of |w| (curl)
        static double Cost(List<int[]> tri, List<Vector2d[]> a, double[] sig, double[] phi, bool useRot, out double curl)
        {
            double cost = 0; curl = 0;
            for (int t = 0; t < tri.Count; ++t) {
                int[] c = tri[t]; Vector2d ws = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) ws += EdgeTerm(a[t][e], sig, phi, c[e], c[(e + 1) % 3], useRot);
                cost += 0.5 * ws.LengthSquared; curl += ws.Length;
            }
            return cost;
        }

        static Vector2d EdgeTerm(Vector2d a, double[] sig, double[] phi, int i, int j, bool useRot)
        {
            double g = Math.Exp(-0.5 * (sig[i] + sig[j]));
            if (!useRot) return new Vector2d(g * a.x, g * a.y);
            double pe = 0.5 * (phi[i] + phi[j]), cs = Math.Cos(pe), sn = Math.Sin(pe);
            return new Vector2d(g * (cs * a.x + sn * a.y), g * (-sn * a.x + cs * a.y));      // the axes turn by +phi (PositionField.RotationAngles): the coordinates by -phi
        }

        // the residual w per triangle and the 2 x 2 blocks of the Jacobian per vertex slot: [dwx/dsigma, dwx/dphi, dwy/dsigma, dwy/dphi]
        static void Linearize(List<int[]> tri, List<Vector2d[]> a, double[] sig, double[] phi, bool useRot, Vector2d[] w, double[][] B)
        {
            for (int t = 0; t < tri.Count; ++t) {
                int[] c = tri[t]; var ce = new Vector2d[3]; Vector2d ws = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) { ce[e] = EdgeTerm(a[t][e], sig, phi, c[e], c[(e + 1) % 3], useRot); ws += ce[e]; }
                w[t] = ws;
                var b = new double[12];
                for (int k = 0; k < 3; ++k) {
                    // vertex slot k starts edge k and ends edge k - 1; d term / d sigma_e = -term / 2 per end point, d term / d phi_e = -Rot90(term) / 2
                    Vector2d e1 = ce[k], e2 = ce[(k + 2) % 3];
                    b[4 * k + 0] = -0.5 * (e1.x + e2.x);
                    b[4 * k + 2] = -0.5 * (e1.y + e2.y);
                    if (useRot) { b[4 * k + 1] = 0.5 * (e1.y + e2.y); b[4 * k + 3] = -0.5 * (e1.x + e2.x); }
                }
                B[t] = b;
            }
        }
    }
}
