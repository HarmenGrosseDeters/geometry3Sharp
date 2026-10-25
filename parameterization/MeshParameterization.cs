// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> outcome of a parameterization (or UV packing) </summary>
    public enum ParameterizationStatus
    {
        Success = 0,
        /// <summary> no triangles, unused triangles referencing missing vertices, or bowtie boundary vertices </summary>
        InvalidMesh = 1,
        /// <summary> the method does not support the topology (eg closed surface, several components, not a disc) </summary>
        InvalidTopology = 2,
        /// <summary> pinned vertices or boundary targets are invalid </summary>
        InvalidConstraints = 3,
        /// <summary> a linear system could not be solved (degenerate input) </summary>
        SolverFailed = 4
    }


    /// <summary>
    /// Result of a mesh parameterization: UV coordinates per vertex, status and distortion measures.
    /// </summary>
    public class ParameterizationResult
    {
        public ParameterizationStatus Status = ParameterizationStatus.Success;
        /// <summary> description of the failure, or null </summary>
        public string Error;
        /// <summary>
        /// UV per vertex id (length MaxVertexID); vertices without triangles get (0,0). Null on failure.
        /// </summary>
        public Vector2d[] UV;
        /// <summary> iterations of an iterative method (ARAP, SLIM), 0 otherwise </summary>
        public int Iterations;
        /// <summary> false if an iterative method stopped at its iteration limit </summary>
        public bool Converged = true;
        /// <summary> final energy of the method (ARAP and SLIM energies are per unit surface area) </summary>
        public double Energy;
        /// <summary> distortion of the UV map (null on failure) </summary>
        public ParameterizationDistortion Distortion;

        public bool Succeeded { get { return Status == ParameterizationStatus.Success; } }

        /// <summary> write the UVs into the vertex UVs of the mesh (enables them if needed) </summary>
        public void ApplyToMesh(DMesh3 mesh)
        {
            if (UV == null)
                throw new InvalidOperationException("ParameterizationResult.ApplyToMesh: no UVs (" + Status + ")");
            if (!mesh.HasVertexUVs)
                mesh.EnableVertexUVs(Vector2f.Zero);
            foreach (int vid in mesh.VertexIndices())
                if (vid < UV.Length)
                    mesh.SetVertexUV(vid, (Vector2f)UV[vid]);
        }

        internal static ParameterizationResult Failure(ParameterizationStatus status, string error)
        {
            return new ParameterizationResult() { Status = status, Error = error, Converged = false };
        }
    }


    /// <summary>
    /// Distortion of a UV map relative to the surface, from the singular values s1 &gt;= s2 of the Jacobian of
    /// each triangle (s2 &lt; 0 if the UV triangle is flipped). Means are weighted by surface area.
    /// Degenerate surface triangles are skipped.
    /// </summary>
    public class ParameterizationDistortion
    {
        /// <summary> triangles measured (non-degenerate on the surface) </summary>
        public int Triangles;
        /// <summary> triangles with zero or negative UV area, ie orientation not preserved </summary>
        public int FlippedTriangles;
        /// <summary> UV area / surface area </summary>
        public double AreaScale;
        /// <summary> quasi-conformal distortion s1/|s2| (1 for a conformal map) </summary>
        public double MaxConformal, MeanConformal;
        /// <summary> |UV angle - surface angle| at the triangle corners, radians </summary>
        public double MaxAngleError, MeanAngleError;
        /// <summary> |log(s1*|s2| / AreaScale)|: area distortion up to the global scale (0 for an area preserving map) </summary>
        public double MaxAreaDistortion, MeanAreaDistortion;
        /// <summary> mean of s1^2 + s2^2 + s1^-2 + s2^-2 (4 for an isometry, infinity if a triangle is flipped) </summary>
        public double SymmetricDirichlet;
        /// <summary> mean of (s1-1)^2 + (s2-1)^2 (0 for an isometry) </summary>
        public double ARAPEnergy;

        /// <summary> distortion of the given UVs (indexed by vertex id) </summary>
        public static ParameterizationDistortion Compute(DMesh3 mesh, IList<Vector2d> uv)
        {
            var d = new ParameterizationDistortion();
            double area3 = 0, areaUV = 0;
            var tris = new List<int>();
            var areas = new List<double>();
            var sv = new List<Vector2d>();
            double angleSum = 0; int angleCount = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d p0 = mesh.GetVertex(t.a), p1 = mesh.GetVertex(t.b), p2 = mesh.GetVertex(t.c);
                TriangleFrame f = new TriangleFrame(p0, p1, p2);
                if (f.Degenerate)
                    continue;
                Vector2d u0 = uv[t.a], u1 = uv[t.b], u2 = uv[t.c];
                double signedUV = 0.5 * (u1 - u0).Cross(u2 - u0);
                if (!(signedUV > 0))
                    d.FlippedTriangles++;
                Matrix2d J = f.Jacobian(u0, u1, u2);
                double s1, s2;
                ParamMath.SignedSingularValues(J, out s1, out s2);
                tris.Add(tid); areas.Add(f.Area); sv.Add(new Vector2d(s1, s2));
                area3 += f.Area;
                areaUV += signedUV;
                // corner angles
                Vector2d[] q = { u0, u1, u2 };
                Vector3d[] p = { p0, p1, p2 };
                for (int i = 0; i < 3; ++i) {
                    double a3 = ParamMath.Angle(p[(i + 1) % 3] - p[i], p[(i + 2) % 3] - p[i]);
                    double auv = ParamMath.Angle(q[(i + 1) % 3] - q[i], q[(i + 2) % 3] - q[i]);
                    double err = Math.Abs(auv - a3);
                    if (double.IsNaN(err)) err = Math.PI;
                    d.MaxAngleError = Math.Max(d.MaxAngleError, err);
                    angleSum += err * f.Area; angleCount++;
                }
            }
            d.Triangles = tris.Count;
            if (d.Triangles == 0 || area3 <= 0)
                return d;
            d.AreaScale = areaUV / area3;
            double scale = Math.Abs(d.AreaScale) > 0 ? Math.Abs(d.AreaScale) : 1;
            double conf = 0, areaD = 0, sd = 0, arap = 0;
            for (int i = 0; i < tris.Count; ++i) {
                double s1 = sv[i].x, s2 = Math.Abs(sv[i].y), A = areas[i];
                double c = s2 > 0 ? s1 / s2 : double.PositiveInfinity;
                double ad = s1 * s2 > 0 ? Math.Abs(Math.Log(s1 * s2 / scale)) : double.PositiveInfinity;
                d.MaxConformal = Math.Max(d.MaxConformal, c);
                d.MaxAreaDistortion = Math.Max(d.MaxAreaDistortion, ad);
                conf += A * c; areaD += A * ad;
                if (sv[i].y > 0)
                    sd += A * (s1 * s1 + s2 * s2 + 1 / (s1 * s1) + 1 / (s2 * s2));
                else
                    sd = double.PositiveInfinity;
                arap += A * ((s1 - 1) * (s1 - 1) + (sv[i].y - 1) * (sv[i].y - 1));
            }
            d.MeanConformal = conf / area3;
            d.MeanAreaDistortion = areaD / area3;
            d.MeanAngleError = angleSum / (3 * area3);
            d.SymmetricDirichlet = sd / area3;
            d.ARAPEnergy = arap / area3;
            return d;
        }

        /// <summary> distortion of the vertex UVs of the mesh </summary>
        public static ParameterizationDistortion Compute(DMesh3 mesh)
        {
            if (!mesh.HasVertexUVs)
                throw new ArgumentException("ParameterizationDistortion.Compute: mesh has no vertex UVs");
            Vector2d[] uv = new Vector2d[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices())
                uv[vid] = (Vector2d)mesh.GetVertexUV(vid);
            return Compute(mesh, uv);
        }
    }


    /// <summary>
    /// A triangle in an isometric 2D frame (p0 = origin, p1 on the x axis), with the gradient of the linear
    /// interpolant and the cotangents of its angles.
    /// </summary>
    internal struct TriangleFrame
    {
        public double Area;
        public bool Degenerate;
        public Vector2d P1, P2;
        // d/dx and d/dy of the linear interpolant: df/dx = Dx . (f0,f1,f2)
        public Vector3d Dx, Dy;
        // cotangent of the angle at corner i (opposite edge (i+1, i+2))
        public Vector3d Cot;

        public TriangleFrame(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            Vector3d e1 = p1 - p0, e2 = p2 - p0;
            double l1 = e1.Length;
            double area2 = e1.Cross(e2).Length;
            double maxLenSqr = Math.Max(e1.LengthSquared, Math.Max(e2.LengthSquared, (p2 - p1).LengthSquared));
            Degenerate = !(area2 > 1e-12 * maxLenSqr) || l1 == 0;
            Area = 0.5 * area2;
            if (Degenerate) {
                P1 = P2 = Vector2d.Zero; Dx = Dy = Cot = Vector3d.Zero;
                return;
            }
            double x2 = e1.Dot(e2) / l1, y2 = area2 / l1;
            P1 = new Vector2d(l1, 0);
            P2 = new Vector2d(x2, y2);
            Dx = new Vector3d(-1 / l1, 1 / l1, 0);
            Dy = new Vector3d((x2 / l1 - 1) / y2, -x2 / (l1 * y2), 1 / y2);
            Vector3d a = p0, b = p1, c = p2;
            Cot = new Vector3d((b - a).Dot(c - a) / area2, (c - b).Dot(a - b) / area2, (a - c).Dot(b - c) / area2);
        }

        public Vector2d Corner(int i) { return i == 0 ? Vector2d.Zero : (i == 1 ? P1 : P2); }

        /// <summary> Jacobian of the map from the frame to the UVs: rows (u,v), columns (x,y) </summary>
        public Matrix2d Jacobian(Vector2d u0, Vector2d u1, Vector2d u2)
        {
            return new Matrix2d(
                Dx.x * u0.x + Dx.y * u1.x + Dx.z * u2.x, Dy.x * u0.x + Dy.y * u1.x + Dy.z * u2.x,
                Dx.x * u0.y + Dx.y * u1.y + Dx.z * u2.y, Dy.x * u0.y + Dy.y * u1.y + Dy.z * u2.y);
        }
    }


    internal static class ParamMath
    {
        /// <summary> unsigned angle between two vectors </summary>
        public static double Angle(Vector3d a, Vector3d b) { return Math.Atan2(a.Cross(b).Length, a.Dot(b)); }
        public static double Angle(Vector2d a, Vector2d b) { return Math.Atan2(Math.Abs(a.Cross(b)), a.Dot(b)); }

        /// <summary>
        /// J = Rot(phi) diag(s1, s2) Rot(theta) with s1 &gt;= |s2|, s2 &lt; 0 iff det J &lt; 0.
        /// The closest rotation to J is Rot(phi + theta).
        /// </summary>
        public static void SignedSVD(Matrix2d J, out double s1, out double s2, out double phi, out double theta)
        {
            double E = 0.5 * (J.m00 + J.m11), F = 0.5 * (J.m00 - J.m11);
            double G = 0.5 * (J.m10 + J.m01), H = 0.5 * (J.m10 - J.m01);
            double Q = Math.Sqrt(E * E + H * H), R = Math.Sqrt(F * F + G * G);
            s1 = Q + R; s2 = Q - R;
            double a1 = Math.Atan2(G, F), a2 = Math.Atan2(H, E);
            theta = 0.5 * (a2 - a1);
            phi = 0.5 * (a2 + a1);
        }

        public static void SignedSingularValues(Matrix2d J, out double s1, out double s2)
        {
            double phi, theta;
            SignedSVD(J, out s1, out s2, out phi, out theta);
        }

        /// <summary> angle of the rotation closest to J (Frobenius norm) </summary>
        public static double ClosestRotationAngle(Matrix2d J)
        {
            return Math.Atan2(J.m10 - J.m01, J.m00 + J.m11);
        }
    }


    /// <summary>
    /// The triangles of a mesh with compact vertex indices, triangle frames, boundary loops and topology,
    /// shared by the parameterization methods.
    /// </summary>
    internal class ParamMesh
    {
        public DMesh3 Mesh;
        public int N;                    // vertices used by triangles
        public int[] ToIndex;            // vertex id -> index (-1 unused)
        public int[] ToVertex;
        public Vector3d[] Pos;           // relative to the bounding box center
        public List<Index3i> Tris = new List<Index3i>();
        public List<TriangleFrame> Frames = new List<TriangleFrame>();
        public double SurfaceArea;
        public List<int[]> BoundaryLoops = new List<int[]>();   // indices, in the direction of the triangles (interior on the left)
        public bool[] IsBoundary;
        public int Components;
        public int EulerCharacteristic;

        public ParameterizationStatus Status = ParameterizationStatus.Success;
        public string Error;

        public ParamMesh(DMesh3 mesh)
        {
            Mesh = mesh;
            ToIndex = new int[mesh.MaxVertexID];
            for (int i = 0; i < ToIndex.Length; ++i) ToIndex[i] = -1;
            var verts = new List<int>();
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j)
                    if (ToIndex[t[j]] < 0) { ToIndex[t[j]] = verts.Count; verts.Add(t[j]); }
                Tris.Add(new Index3i(ToIndex[t.a], ToIndex[t.b], ToIndex[t.c]));
            }
            N = verts.Count;
            ToVertex = verts.ToArray();
            if (Tris.Count == 0) {
                Status = ParameterizationStatus.InvalidMesh; Error = "mesh has no triangles";
                return;
            }
            Vector3d center = mesh.CachedBounds.Center;
            Pos = new Vector3d[N];
            for (int i = 0; i < N; ++i)
                Pos[i] = mesh.GetVertex(ToVertex[i]) - center;
            foreach (Index3i t in Tris) {
                TriangleFrame f = new TriangleFrame(Pos[t.a], Pos[t.b], Pos[t.c]);
                Frames.Add(f);
                if (!f.Degenerate)
                    SurfaceArea += f.Area;
            }
            if (!(SurfaceArea > 0)) {
                Status = ParameterizationStatus.InvalidMesh; Error = "all triangles are degenerate";
                return;
            }

            // edges and boundary half-edges
            var edges = new Dictionary<long, int>();
            for (int ti = 0; ti < Tris.Count; ++ti) {
                Index3i t = Tris[ti];
                for (int j = 0; j < 3; ++j) {
                    int a = t[j], b = t[(j + 1) % 3];
                    long key = a < b ? (long)a * N + b : (long)b * N + a;
                    int c;
                    edges.TryGetValue(key, out c);
                    edges[key] = c + 1;
                }
            }
            EulerCharacteristic = N - edges.Count + Tris.Count;

            // union-find over triangles sharing edges -> components
            int[] parent = new int[N];
            for (int i = 0; i < N; ++i) parent[i] = i;
            Func<int, int> find = null;
            find = (x) => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            foreach (Index3i t in Tris) {
                int ra = find(t.a), rb = find(t.b), rc = find(t.c);
                parent[rb] = ra; parent[find(rc)] = find(ra);
            }
            var roots = new HashSet<int>();
            for (int i = 0; i < N; ++i) roots.Add(find(i));
            Components = roots.Count;

            int[] nextBoundary = new int[N];
            for (int i = 0; i < N; ++i) nextBoundary[i] = -1;
            IsBoundary = new bool[N];
            for (int ti = 0; ti < Tris.Count; ++ti) {
                Index3i t = Tris[ti];
                for (int j = 0; j < 3; ++j) {
                    int a = t[j], b = t[(j + 1) % 3];
                    long key = a < b ? (long)a * N + b : (long)b * N + a;
                    if (edges[key] != 1)
                        continue;
                    if (nextBoundary[a] >= 0) {
                        Status = ParameterizationStatus.InvalidMesh;
                        Error = "vertex " + ToVertex[a] + " is a bowtie on the boundary";
                        return;
                    }
                    nextBoundary[a] = b;
                    IsBoundary[a] = true;
                }
            }
            bool[] visited = new bool[N];
            for (int i = 0; i < N; ++i) {
                if (nextBoundary[i] < 0 || visited[i])
                    continue;
                var loop = new List<int>();
                int v = i;
                while (!visited[v]) {
                    visited[v] = true;
                    loop.Add(v);
                    v = nextBoundary[v];
                    if (v < 0) {
                        Status = ParameterizationStatus.InvalidMesh; Error = "boundary is not a closed loop (inconsistent orientation?)";
                        return;
                    }
                }
                if (v != i) {
                    Status = ParameterizationStatus.InvalidMesh; Error = "boundary loops are not simple";
                    return;
                }
                BoundaryLoops.Add(loop.ToArray());
            }
        }

        public bool Valid { get { return Status == ParameterizationStatus.Success; } }

        /// <summary> one connected component with at least one boundary loop </summary>
        public bool RequireOpenComponent()
        {
            if (!Valid) return false;
            if (Components != 1) {
                Status = ParameterizationStatus.InvalidTopology;
                Error = "mesh has " + Components + " connected components; parameterize each one separately";
                return false;
            }
            if (BoundaryLoops.Count == 0) {
                Status = ParameterizationStatus.InvalidTopology;
                Error = "mesh is closed; cut it open first";
                return false;
            }
            return true;
        }

        /// <summary> one connected component with disc topology (one boundary loop, Euler characteristic 1) </summary>
        public bool RequireDisc()
        {
            if (!RequireOpenComponent()) return false;
            if (BoundaryLoops.Count != 1 || EulerCharacteristic != 1) {
                Status = ParameterizationStatus.InvalidTopology;
                Error = "mesh is not a topological disc (" + BoundaryLoops.Count + " boundary loops, Euler characteristic " + EulerCharacteristic + ")";
                return false;
            }
            return true;
        }

        public ParameterizationResult Failure()
        {
            return ParameterizationResult.Failure(Status, Error);
        }

        /// <summary> cotan Laplacian (positive semidefinite) entries a,b,w: Sum w (f_a - f_b)^2 / 2 = Dirichlet energy </summary>
        public void ForEachCotanEdge(Action<int, int, double> f)
        {
            for (int ti = 0; ti < Tris.Count; ++ti) {
                TriangleFrame fr = Frames[ti];
                if (fr.Degenerate) continue;
                Index3i t = Tris[ti];
                for (int j = 0; j < 3; ++j)
                    f(t[(j + 1) % 3], t[(j + 2) % 3], 0.5 * fr.Cot[j]);
            }
        }

        public double UVArea(double[] u, double[] v)
        {
            double a = 0;
            foreach (Index3i t in Tris)
                a += 0.5 * ((u[t.b] - u[t.a]) * (v[t.c] - v[t.a]) - (u[t.c] - u[t.a]) * (v[t.b] - v[t.a]));
            return a;
        }

        /// <summary> scale the UVs uniformly about their centroid so that their area equals the surface area </summary>
        public void ScaleToSurfaceArea(double[] u, double[] v)
        {
            double a = UVArea(u, v);
            if (!(Math.Abs(a) > 0)) return;
            double s = Math.Sqrt(SurfaceArea / Math.Abs(a));
            double cu = 0, cv = 0;
            for (int i = 0; i < N; ++i) { cu += u[i]; cv += v[i]; }
            cu /= N; cv /= N;
            for (int i = 0; i < N; ++i) { u[i] = (u[i] - cu) * s; v[i] = (v[i] - cv) * s; }
        }

        public bool AllFinite(double[] u, double[] v)
        {
            for (int i = 0; i < N; ++i)
                if (!MathUtil.IsFinite(u[i]) || !MathUtil.IsFinite(v[i])) return false;
            return true;
        }

        public ParameterizationResult MakeResult(double[] u, double[] v)
        {
            var r = new ParameterizationResult();
            r.UV = new Vector2d[Mesh.MaxVertexID];
            for (int i = 0; i < N; ++i)
                r.UV[ToVertex[i]] = new Vector2d(u[i], v[i]);
            r.Distortion = ParameterizationDistortion.Compute(Mesh, r.UV);
            return r;
        }

        /// <summary> signed UV area of each triangle, times 2 </summary>
        public static double SignedArea2(double[] u, double[] v, Index3i t)
        {
            return (u[t.b] - u[t.a]) * (v[t.c] - v[t.a]) - (u[t.c] - u[t.a]) * (v[t.b] - v[t.a]);
        }
    }


    /// <summary>
    /// A quadratic form 1/2 x^T M x - b^T x over variables of which some are fixed; minimized by
    /// M_ff x_f = b_f - M_fc x_c with a sparse Cholesky factorization that is reused for new b and
    /// new values of the fixed variables (the fixedValue array is read at each Solve).
    /// </summary>
    internal class ConstrainedQuadratic
    {
        public readonly int NumVars;
        bool[] isFixed;
        double[] fixedValue;
        int[] freeIndex;
        int numFree;
        SparseCholesky chol;
        // entries M_fc: free index, fixed variable, value
        List<int> cFree = new List<int>(), cFixed = new List<int>();
        List<double> cVal = new List<double>();

        public ConstrainedQuadratic(int numVars, bool[] isFixed, double[] fixedValue, int blockSize = 1)
        {
            NumVars = numVars;
            this.isFixed = isFixed;
            this.fixedValue = fixedValue;
            freeIndex = new int[numVars];
            for (int i = 0; i < numVars; ++i)
                freeIndex[i] = isFixed[i] ? -1 : numFree++;
            // fixed variables break up blocks, so only use blocks if none of them are partially fixed
            bool blocksOk = blockSize > 1 && numFree % blockSize == 0;
            for (int i = 0; blocksOk && i < numVars; i += blockSize)
                for (int s = 1; s < blockSize; ++s)
                    if (isFixed[i + s] != isFixed[i]) { blocksOk = false; break; }
            chol = new SparseCholesky(numFree, blocksOk ? blockSize : 1);
        }

        public int NumFree { get { return numFree; } }
        public string Error { get { return chol.Error; } }

        public void Clear()
        {
            chol.Clear();
            cFree.Clear(); cFixed.Clear(); cVal.Clear();
        }

        /// <summary> add v to M[i,j] and M[j,i] (once per pair) </summary>
        public void Add(int i, int j, double v)
        {
            int fi = freeIndex[i], fj = freeIndex[j];
            if (fi >= 0 && fj >= 0)
                chol.Add(fi, fj, v);
            else if (fi >= 0) {
                cFree.Add(fi); cFixed.Add(j); cVal.Add(v);
            } else if (fj >= 0) {
                cFree.Add(fj); cFixed.Add(i); cVal.Add(v);
            }
        }

        public bool Factorize()
        {
            if (numFree == 0) return true;
            return chol.Factorize();
        }

        /// <summary> minimizer for the linear term b (per variable, may be null); fixed variables get their values </summary>
        public void Solve(double[] b, double[] x)
        {
            double[] rhs = new double[numFree];
            if (b != null)
                for (int i = 0; i < NumVars; ++i)
                    if (freeIndex[i] >= 0)
                        rhs[freeIndex[i]] = b[i];
            for (int k = 0; k < cFree.Count; ++k)
                rhs[cFree[k]] -= cVal[k] * fixedValue[cFixed[k]];
            double[] y = new double[numFree];
            if (numFree > 0)
                chol.Solve(rhs, y);
            for (int i = 0; i < NumVars; ++i)
                x[i] = freeIndex[i] >= 0 ? y[freeIndex[i]] : fixedValue[i];
        }
    }
}
