// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Boundary First Flattening with cone singularities (Sawhney and Crane 2017, cones): a conformal
    /// flattening of a closed surface (or of a surface with boundary) in which the curvature is concentrated
    /// at a few cone vertices, so that the rest becomes flat with little area distortion.
    ///
    /// 1. Cones: given (Cones), or placed greedily (Ben-Chen, Gotsman and Bunin 2008; Springborn, Schröder and
    ///    Pinkall 2008): solve the discrete Yamabe equation C u = -K for the log scale factors u with u = 0 at
    ///    the cones and on the boundary (C the cotan Laplacian, K the angle defects), and make the vertex of
    ///    largest |u| a cone, until max |u| &lt;= MaxScaleFactor or MaxCones. On a closed surface the first cone
    ///    is where the scale factor of the uniform curvature flow is largest. The curvature that each cone gets
    ///    is ConeCurvature = K + C u at the cone; all other interior vertices become flat.
    /// 2. Cut through the cones into a disc (MeshDiscCut with the cones as required vertices).
    /// 3. BFF of the cut disc with the scale factors u prescribed on its boundary (Dirichlet-to-Neumann map):
    ///    both sides of each seam get the same lengths, and the angles around cut vertices sum to 2 pi except
    ///    at the cones, so the seams match up to a rigid motion.
    ///
    /// Cone sets that can not be flattened fail with InvalidConstraints: a cone that would get a curvature of
    /// 2 pi or more (no angle left, eg MaxCones = 1 on a sphere), or a closed surface of Euler characteristic
    /// other than 0 without cones. Degenerate triangles count with their angles (a cap adds about pi), so
    /// slivers do not attract cones.
    ///
    /// On a cube, the eight corners become cones and the faces are unfolded without distortion. The result is
    /// conformal and flip-free in practice, but not guaranteed to be globally injective (check with UVOverlap;
    /// the parts can overlap for long, winding seams).
    /// </summary>
    public class ConeFlattening
    {
        public DMesh3 Mesh;
        /// <summary> cone vertex ids (interior vertices); null: placed automatically </summary>
        public IList<int> Cones;
        /// <summary> automatic placement: stop when the largest |log scale factor| is at most this </summary>
        public double MaxScaleFactor = 0.2;
        /// <summary> automatic placement: at most this many cones </summary>
        public int MaxCones = 64;
        /// <summary> seams prefer sharp edges (see MeshDiscCut.FeatureWeight) </summary>
        public double FeatureWeight = 0;
        public BoundaryFirstFlattening.ExtensionModes Extension = BoundaryFirstFlattening.ExtensionModes.Conformal;
        /// <summary> scale the result so that the UV area equals the surface area </summary>
        public bool ScaleToSurfaceArea = true;

        /// <summary> UVs per vertex id of CutMesh, status and distortion (null before Compute) </summary>
        public ParameterizationResult Result;
        /// <summary> the cut (seams through the cones) </summary>
        public MeshDiscCut Cut;
        /// <summary> the cut disc that the UVs belong to (Cut.CutMesh) </summary>
        public DMesh3 CutMesh { get { return Cut != null ? Cut.CutMesh : null; } }
        /// <summary> cone vertex ids of Mesh after Compute() </summary>
        public List<int> ConeVertices = new List<int>();
        /// <summary> curvature (angle defect) of the flat metric at each cone, by vertex id of Mesh </summary>
        public Dictionary<int, double> ConeCurvature = new Dictionary<int, double>();
        /// <summary> log scale factor u by vertex id of Mesh (0 at cones and on the boundary) </summary>
        public double[] ScaleFactors;
        /// <summary> largest |u| of the final cone set </summary>
        public double MaxAbsScaleFactor;

        public ConeFlattening(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ConeVertices = new List<int>(); ConeCurvature = new Dictionary<int, double>();
            ScaleFactors = null; Cut = null; MaxAbsScaleFactor = 0;
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.Valid)
                return Result = pm.Failure();
            if (pm.Components != 1)
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidTopology,
                    "mesh has " + pm.Components + " connected components; flatten each one separately");
            int N = pm.N;

            // angle defects and vertex areas; degenerate triangles (caps, needles) count with their angles
            // (a cap has an angle of about pi), else their vertices would get false curvature
            double[] angleSum = new double[N], area = new double[N];
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                Index3i t = pm.Tris[ti];
                if (f.Degenerate) {
                    Vector3d ang = DegenerateAngles(pm.Pos[t.a], pm.Pos[t.b], pm.Pos[t.c]);
                    for (int j = 0; j < 3; ++j) angleSum[t[j]] += ang[j];
                    continue;
                }
                for (int j = 0; j < 3; ++j) {
                    angleSum[t[j]] += Math.Atan2(1, f.Cot[j]);
                    area[t[j]] += f.Area / 3;
                }
            }
            double[] K = new double[N];
            for (int i = 0; i < N; ++i)
                K[i] = pm.IsBoundary[i] ? 0 : 2 * Math.PI - angleSum[i];

            bool[] isCone = new bool[N];
            var cones = new List<int>();
            if (Cones != null) {
                foreach (int vid in Cones) {
                    int i = vid >= 0 && vid < pm.ToIndex.Length ? pm.ToIndex[vid] : -1;
                    if (i < 0)
                        return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "cone " + vid + " is not a vertex of a triangle");
                    if (pm.IsBoundary[i])
                        return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "cone " + vid + " is on the boundary");
                    if (!isCone[i]) { isCone[i] = true; cones.Add(i); }
                }
            }

            double[] u = new double[N];
            bool closed = pm.BoundaryLoops.Count == 0;
            if (closed && cones.Count == 0) {
                // first cone: largest |u| of the flow to uniform curvature (Neumann problem, u up to a constant)
                double Ktot = 0;
                foreach (double k in K) Ktot += k;
                double rhsScale = Ktot / pm.SurfaceArea;
                double[] rhs = new double[N];
                for (int i = 0; i < N; ++i) rhs[i] = -(K[i] - rhsScale * area[i]);
                bool[] pin = new bool[N];
                pin[0] = true;
                if (!solve(pm, pin, rhs, u))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "cone placement: Laplacian could not be factorized");
                double mean = 0;
                for (int i = 0; i < N; ++i) mean += area[i] * u[i];
                mean /= pm.SurfaceArea;
                int best = -1;
                for (int i = 0; i < N; ++i)
                    if (best < 0 || Math.Abs(u[i] - mean) > Math.Abs(u[best] - mean)) best = i;
                // a closed surface needs cones unless its total curvature is 0 (genus 1)
                if (Math.Abs(Ktot) > 1e-6 && Cones != null)
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                        "a closed surface with Euler characteristic " + Math.Round(Ktot / (2 * Math.PI)) + " can not be flattened without cones");
                // a flat torus needs no cones
                if (Cones == null && (Math.Abs(Ktot) > 1e-6 || Math.Abs(u[best] - mean) > MaxScaleFactor)) {
                    isCone[best] = true; cones.Add(best);
                } else {
                    for (int i = 0; i < N; ++i) u[i] -= mean;
                }
            }

            // Yamabe equation with u = 0 at the cones and on the boundary; greedy: add the vertex of largest |u|
            double[] negK = new double[N];
            for (int i = 0; i < N; ++i) negK[i] = -K[i];
            while (cones.Count > 0 || !closed) {
                bool[] fix = new bool[N];
                for (int i = 0; i < N; ++i) fix[i] = isCone[i] || pm.IsBoundary[i];
                if (!solve(pm, fix, negK, u))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "cone placement: Laplacian could not be factorized");
                int best = -1;
                for (int i = 0; i < N; ++i)
                    if (!fix[i] && (best < 0 || Math.Abs(u[i]) > Math.Abs(u[best]))) best = i;
                if (Cones != null || best < 0 || Math.Abs(u[best]) <= MaxScaleFactor || cones.Count >= MaxCones)
                    break;
                isCone[best] = true; cones.Add(best);
            }
            if (!pm.AllFinite(u, u))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "scale factors are not finite");

            // curvature at the cones: K + C u
            double[] Cu = new double[N];
            pm.ForEachCotanEdge((i, j, w) => { double d = w * (u[i] - u[j]); Cu[i] += d; Cu[j] -= d; });
            ScaleFactors = new double[Mesh.MaxVertexID];
            for (int i = 0; i < N; ++i) {
                ScaleFactors[pm.ToVertex[i]] = u[i];
                MaxAbsScaleFactor = Math.Max(MaxAbsScaleFactor, Math.Abs(u[i]));
            }
            foreach (int i in cones) {
                ConeVertices.Add(pm.ToVertex[i]);
                ConeCurvature[pm.ToVertex[i]] = K[i] + Cu[i];
            }
            // a cone of curvature 2 pi or more has no angle left: the cone set can not be flattened
            foreach (var kv in ConeCurvature)
                if (!(kv.Value < 2 * Math.PI - 1e-6))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                        "cone " + kv.Key + " would get curvature " + kv.Value + " >= 2 pi (no angle left); use more cones");

            // cut through the cones and flatten the disc with the scale factors on its boundary
            Cut = new MeshDiscCut(Mesh) { RequiredVertices = ConeVertices, FeatureWeight = FeatureWeight };
            if (!Cut.Compute())
                return Result = ParameterizationResult.Failure(Cut.Status, "cut through the cones: " + Cut.Error);
            DMesh3 disc = Cut.CutMesh;
            var bff = new BoundaryFirstFlattening(disc) {
                BoundaryMode = BoundaryFirstFlattening.BoundaryModes.TargetScaleFactors,
                TargetScaleFactors = new Dictionary<int, double>(),
                Extension = Extension,
                ScaleToSurfaceArea = ScaleToSurfaceArea,
            };
            foreach (int vid in disc.VertexIndices())
                if (disc.IsBoundaryVertex(vid))
                    bff.TargetScaleFactors[vid] = ScaleFactors[Cut.CutToInputVertex[vid]];
            Result = bff.Compute();
            return Result;
        }

        /// <summary>
        /// corner angles of a (nearly) degenerate triangle from its edge vectors: exact for collinear corners
        /// (0, 0, pi); with a zero-length edge, pi/2 at its two ends; pi/3 each if all corners coincide
        /// </summary>
        internal static Vector3d DegenerateAngles(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            Vector3d[] p = { p0, p1, p2 };
            double[] len = { (p1 - p2).Length, (p2 - p0).Length, (p0 - p1).Length };   // opposite corner i
            int zero = 0;
            for (int i = 0; i < 3; ++i) if (!(len[i] > 0)) zero++;
            if (zero >= 2) return new Vector3d(Math.PI / 3, Math.PI / 3, Math.PI / 3);
            Vector3d a = Vector3d.Zero;
            if (zero == 1) {
                for (int i = 0; i < 3; ++i) a[i] = len[i] > 0 ? Math.PI / 2 : 0;
                return a;
            }
            for (int i = 0; i < 3; ++i)
                a[i] = ParamMath.Angle(p[(i + 1) % 3] - p[i], p[(i + 2) % 3] - p[i]);
            return a;
        }

        // minimizes 1/2 u^T C u - b^T u with u = 0 at the fixed vertices
        static bool solve(ParamMesh pm, bool[] fix, double[] b, double[] u)
        {
            var q = new ConstrainedQuadratic(pm.N, fix, new double[pm.N]);
            pm.ForEachCotanEdge((i, j, w) => { q.Add(i, i, w); q.Add(j, j, w); q.Add(i, j, -w); });
            if (!q.Factorize())
                return false;
            q.Solve(b, u);
            return true;
        }
    }
}
