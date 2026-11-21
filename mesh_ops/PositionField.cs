// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using g3;

namespace gs
{
    /// <summary> Result of PositionField.Compute(). </summary>
    public enum PositionFieldStatus
    {
        Success = 0,
        /// <summary> no field, a failed cross field, a target edge length that is not positive and finite, or no usable vertex </summary>
        InvalidInput = 1
    }

    /// <summary>
    /// The position field of Jakob, Tarini, Panozzo and Sorkine-Hornung, "Instant Field-Aligned Meshes" (ACM TOG 34(6),
    /// SIGGRAPH Asia 2015), written from the paper (QUAD_REMESHING.md section 3.1 has the derivation and lists the own
    /// choices; no code of Instant Meshes, QuadriFlow or other implementations was read).
    ///
    /// Each vertex i of the mesh stores a point o_i in its tangent plane. Together with the orientation of the cross field q_i
    /// (direction 0 of CrossField) and the target edge length s it stands for the lattice { o_i + s (a q_i + b t_i) }, a and b
    /// integers, t_i = n_i x q_i. Computed by Gauss-Seidel sweeps on a hierarchy of coarser vertex sets (greedy matching of the
    /// closest neighbours, area weighted positions, normals and orientations), coarse to fine: the lattices of two neighbours are
    /// compared near the point of both tangent planes closest to the edge midpoint, rounded to the lattice of the one and then to
    /// the lattice of the other, and o_i moves by the mean misfit of its neighbours and back to the lattice point nearest to
    /// the vertex.
    ///
    /// Boundaries (and, when PreserveCreases is true, sharp interior crease edges above CreaseAngleDegrees) are lattice
    /// constraints: a smooth boundary or crease vertex keeps a lattice line (along the cross direction nearest to the feature
    /// tangent) through the vertex, so that the lattice points along it lie on the feature curve, and a corner (where the
    /// feature turns by more than CornerAngleDegrees, a junction of degree != 2, or a crease meeting a boundary) is a lattice
    /// point itself. Boundary and crease vertices are only merged along feature edges in the hierarchy; corners are never merged.
    ///
    /// Parallel and deterministic: the vertices of every level get a fixed greedy colouring (in vertex order, so no two
    /// neighbours share a colour); a sweep runs the colours one after the other, and the vertices of one colour only read
    /// vertices of other colours. Serial and parallel runs execute exactly the same updates, so the result is the same bit for
    /// bit for every thread count. Power of two scalings of the mesh (with the same scaling of TargetEdgeLength) give the same
    /// result bit for bit up to the scaling (no absolute tolerance enters). Compute() can be called again.
    ///
    /// Input: a successful CrossField on Mesh (its vertices with HasField get a position). The mesh and the field are not modified.
    /// </summary>
    public class PositionField
    {
        public DMesh3 Mesh;
        public CrossField Field;
        /// <summary> the lattice spacing s (the edge length of the quads), in the units of the mesh </summary>
        public double TargetEdgeLength = 0;
        /// <summary> Gauss-Seidel sweeps per level (a level stops early when a sweep changes nothing) </summary>
        public int Sweeps = 20;
        /// <summary> run the vertices of one colour in parallel (the result does not depend on it) </summary>
        public bool Parallel = true;
        /// <summary> a boundary or crease vertex where the feature curve turns by more than this angle is a corner (a lattice point) </summary>
        public double CornerAngleDegrees = 30;
        /// <summary>
        /// optional (default false: bit-identical to without this option): treat interior sharp crease edges (dihedral angle above
        /// CreaseAngleDegrees, from MeshTopology) as lattice constraints like open boundaries — smooth crease vertices keep a lattice
        /// line along the crease, and crease corners (turn above CornerAngleDegrees, junctions of degree != 2, or crease-boundary
        /// junctions) become fixed lattice points.
        /// </summary>
        public bool PreserveCreases = false;
        /// <summary> dihedral angle threshold in degrees for detecting sharp creases when PreserveCreases is true (must be in [0, 180]) </summary>
        public double CreaseAngleDegrees = 30;
        /// <summary>
        /// the regularization of the point closest to the edge midpoint on both tangent planes (dimensionless; own choice, see
        /// QUAD_REMESHING.md 3.1: 0.01 to 0.2 give the same results)
        /// </summary>
        public double MiddleRegularization = 0.05;

        /// <summary>
        /// optional (default null: the lattice spacing is TargetEdgeLength everywhere): a factor per vertex id, at least MaxVertexID long, finite and
        /// positive, for the spacing at that vertex (TargetEdgeLength times the factor; a size field, see IntegrableFrames). Coarser levels
        /// of the hierarchy take the area weighted mean. Without it the result is bit for bit the one of a uniform spacing.
        /// </summary>
        public double[] SpacingFactors;
        /// <summary>
        /// optional (default null): a rotation in radians per vertex id (at least MaxVertexID long, finite) of the lattice orientation about the vertex normal,
        /// on top of the cross field's direction 0 (see IntegrableFrames). Boundary vertices should have 0: their orientation is snapped to the boundary.
        /// </summary>
        public double[] RotationAngles;

        /// <summary> vertices per parallel work item (the result does not depend on it; tests make it small) </summary>
        internal int ChunkSize = 512;

        public PositionFieldStatus Status = PositionFieldStatus.Success;
        public string Error;
        /// <summary> number of levels of the hierarchy (1 = no coarsening) and the colours of the finest level </summary>
        public int Levels, Colors;
        /// <summary> sweeps run over all levels </summary>
        public int SweepsRun;

        internal Level L0;
        internal List<Level> LevelList;
        internal int[] ToVertex;      // compact index -> vertex id
        internal int[] ToCompact;     // vertex id -> compact index (-1: none)
        internal Vector3d[] Origins;  // o_i of level 0

        public PositionField(DMesh3 mesh, CrossField field) { Mesh = mesh; Field = field; }

        public bool Success { get { return Status == PositionFieldStatus.Success; } }

        /// <summary> vertex vid has a position (the last Compute() succeeded and the vertex has a field) </summary>
        public bool HasPosition(int vid) { return ToCompact != null && vid >= 0 && vid < ToCompact.Length && ToCompact[vid] >= 0; }
        /// <summary> the lattice point o of vertex vid (in its tangent plane, within s / sqrt 2 of the vertex); only for vertices with HasPosition </summary>
        public Vector3d Origin(int vid) { return Origins[ToCompact[vid]]; }
        /// <summary> (only for vertices with HasPosition) the unit orientation q of the lattice at vid (cross direction 0 of the field; at a boundary or crease vertex the feature direction is exact) </summary>
        public Vector3d Orientation(int vid) { return L0.Q[ToCompact[vid]]; }

        internal sealed class Level
        {
            public int N;
            public Vector3d[] V, Nrm, Q, T, D, Cb;
            public double[] Area;
            public byte[] Kind;                  // 0 interior, 1 boundary or crease (lattice line), 2 corner (lattice point)
            public int[] AS, AJ;                 // adjacency (CSR)
            public bool[] AB;                    // slot is a feature edge (boundary, or crease when PreserveCreases is true)
            public Vector3d[] X, Bq;             // per slot: the middle point and the representative of the neighbour's orientation
            public int[] ColorStart, ColorOrder;
            public int[] ToCoarse;
            public double[] Sc;                  // spacing factor per vertex (null: 1)
        }

        public PositionFieldStatus Compute()
        {
            Status = PositionFieldStatus.Success; Error = null; Levels = 0; Colors = 0; SweepsRun = 0;
            L0 = null; LevelList = null; ToVertex = null; ToCompact = null; Origins = null;
            if (Mesh == null || Field == null || !Field.Success) return Fail("a successful cross field is required");
            double s = TargetEdgeLength;
            if (!(s > 0) || double.IsInfinity(s)) return Fail("TargetEdgeLength must be positive and finite");
            if (!(MiddleRegularization >= 1e-6) || !(CornerAngleDegrees >= 0) || !(CreaseAngleDegrees >= 0 && CreaseAngleDegrees <= 180))
                return Fail("invalid settings (MiddleRegularization must be at least 1e-6, CornerAngleDegrees >= 0, CreaseAngleDegrees in [0, 180])");

            int maxId = Mesh.MaxVertexID;
            ToCompact = new int[maxId];
            var list = new List<int>();
            for (int v = 0; v < maxId; ++v) {
                ToCompact[v] = -1;
                if (Mesh.IsVertex(v) && Field.HasField(v)) { ToCompact[v] = list.Count; list.Add(v); }
            }
            ToVertex = list.ToArray();
            int n = ToVertex.Length;
            if (n == 0) return Fail("no vertex has a field");
            if (SpacingFactors != null) {
                if (SpacingFactors.Length < maxId) return Fail("SpacingFactors is shorter than the vertex ids of the mesh");
                foreach (int v in ToVertex) if (!(SpacingFactors[v] > 0) || double.IsInfinity(SpacingFactors[v])) return Fail("SpacingFactors must be positive and finite");
            }
            if (RotationAngles != null) {
                if (RotationAngles.Length < maxId) return Fail("RotationAngles is shorter than the vertex ids of the mesh");
                foreach (int v in ToVertex) if (double.IsNaN(RotationAngles[v]) || double.IsInfinity(RotationAngles[v])) return Fail("RotationAngles must be finite");
            }

            Level f = BuildFinest(n);
            var levels = new List<Level> { f };
            while (levels[levels.Count - 1].N > 64 && levels.Count < 32) {
                Level c = Coarsen(levels[levels.Count - 1]);
                if (c.N > 0.9 * levels[levels.Count - 1].N) break;
                levels.Add(c);
            }
            LevelList = levels;
            foreach (Level l in levels) Prepare(l);
            Levels = levels.Count; Colors = f.ColorStart.Length - 1;

            Level top = levels[levels.Count - 1];
            Vector3d[] O = (Vector3d[])top.V.Clone();
            for (int i = 0; i < top.N; ++i) O[i] = Round(O[i], top, i, s);
            SweepsRun += Smooth(top, O, s);
            for (int l = levels.Count - 2; l >= 0; --l) {
                Level L = levels[l];
                var Of = new Vector3d[L.N];
                for (int i = 0; i < Of.Length; ++i) {
                    Vector3d o = O[L.ToCoarse[i]];
                    o -= L.Nrm[i].Dot(o - L.V[i]) * L.Nrm[i];
                    if (PreserveCreases && L.Kind[i] == 1) o -= L.Cb[i].Dot(o - L.V[i]) * L.Cb[i];
                    Of[i] = Round(o, L, i, s);
                }
                O = Of;
                SweepsRun += Smooth(L, O, s);
            }
            for (int i = 0; i < O.Length; ++i)
                if (!O[i].IsFinite) { L0 = null; LevelList = null; return Fail("the position field is not finite (degenerate input or a target edge length far below the size of the mesh)"); }
            L0 = f; Origins = O;
            return Status;
        }

        PositionFieldStatus Fail(string error) { Status = PositionFieldStatus.InvalidInput; Error = error; return Status; }

        // ------------------------------------------------------------------------------------------------ levels

        Level BuildFinest(int n)
        {
            var L = new Level { N = n };
            L.V = new Vector3d[n]; L.Nrm = new Vector3d[n]; L.Q = new Vector3d[n]; L.Area = new double[n];
            L.D = new Vector3d[n]; L.Kind = new byte[n];
            for (int i = 0; i < n; ++i) {
                int v = ToVertex[i];
                L.V[i] = Mesh.GetVertex(v); L.Nrm[i] = Field.VertexNormal(v); L.Q[i] = Field.VertexDirection(v, 0);
                if (RotationAngles != null && RotationAngles[v] != 0) L.Q[i] = Math.Cos(RotationAngles[v]) * L.Q[i] + Math.Sin(RotationAngles[v]) * L.Nrm[i].Cross(L.Q[i]);
            }
            if (SpacingFactors != null) {
                L.Sc = new double[n];
                for (int i = 0; i < n; ++i) L.Sc[i] = SpacingFactors[ToVertex[i]];
            }
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(t);
                int a = ToCompact[tv.a], b = ToCompact[tv.b], c = ToCompact[tv.c];
                if (a < 0 || b < 0 || c < 0) continue;
                double area = (L.V[b] - L.V[a]).Cross(L.V[c] - L.V[a]).Length / 6;
                L.Area[a] += area; L.Area[b] += area; L.Area[c] += area;
            }
            // adjacency, boundary, and (when PreserveCreases) sharp crease constraints
            HashSet<int> creaseEdges = null;
            if (PreserveCreases) {
                var topo = new MeshTopology(Mesh) { CreaseAngle = CreaseAngleDegrees };
                topo.Compute();
                creaseEdges = topo.CreaseEdges;
            }
            var start = new int[n + 1];
            var nbs = new List<int>(); var bnd = new List<bool>();
            var bnbs = new List<int>[n];
            var cnbs = PreserveCreases ? new List<int>[n] : null;
            for (int i = 0; i < n; ++i) {
                int v = ToVertex[i];
                start[i] = nbs.Count;
                foreach (int e in Mesh.VtxEdgesItr(v)) {
                    Index2i ev = Mesh.GetEdgeV(e);
                    int u = ev.a == v ? ev.b : ev.a;
                    int j = ToCompact[u];
                    if (j < 0) continue;
                    bool isB = Mesh.IsBoundaryEdge(e);
                    bool isC = creaseEdges != null && creaseEdges.Contains(e);
                    nbs.Add(j); bnd.Add(isB || isC);
                    if (isB) { if (bnbs[i] == null) bnbs[i] = new List<int>(); bnbs[i].Add(j); }
                    else if (isC) { if (cnbs[i] == null) cnbs[i] = new List<int>(); cnbs[i].Add(j); }
                }
            }
            start[n] = nbs.Count;
            L.AS = start; L.AJ = nbs.ToArray(); L.AB = bnd.ToArray();
            double cosCorner = Math.Cos(CornerAngleDegrees * Math.PI / 180);
            for (int i = 0; i < n; ++i) {
                List<int> fnbs = bnbs[i];
                if (fnbs != null) {
                    // a boundary vertex that also meets a crease, or a non-simple boundary vertex, is a fixed lattice point
                    if ((cnbs != null && cnbs[i] != null) || fnbs.Count != 2) { L.Kind[i] = 2; continue; }
                } else if (cnbs != null && cnbs[i] != null) {
                    fnbs = cnbs[i];
                    if (fnbs.Count != 2) { L.Kind[i] = 2; continue; }     // crease junction (degree >= 3) or crease endpoint (degree 1): lattice point
                } else {
                    continue;
                }
                Vector3d pu = L.V[fnbs[0]], pw = L.V[fnbs[1]], p = L.V[i];
                Vector3d d = pw - pu; d -= L.Nrm[i].Dot(d) * L.Nrm[i];
                double dl = d.Length;
                Vector3d e0 = p - pu, e1 = pw - p;
                double l0 = e0.Length, l1 = e1.Length;
                if (!(dl > 0) || !(l0 > 0) || !(l1 > 0)) { L.Kind[i] = 2; continue; }
                L.D[i] = d / dl;
                L.Kind[i] = (byte)(e0.Dot(e1) / (l0 * l1) < cosCorner ? 2 : 1);
            }
            return L;
        }

        // greedy matching, closest pairs first (ties by vertex ids); boundary vertices only match along boundary edges, corners and
        // vertices without neighbours stay alone
        Level Coarsen(Level f)
        {
            int n = f.N;
            var cand = new List<EdgeKey>();
            for (int i = 0; i < n; ++i) {
                if (f.Kind[i] == 2) continue;
                for (int k = f.AS[i]; k < f.AS[i + 1]; ++k) {
                    int j = f.AJ[k];
                    if (j <= i || f.Kind[j] == 2) continue;
                    if (f.Kind[i] != f.Kind[j]) continue;
                    if (f.Kind[i] == 1 && !f.AB[k]) continue;
                    cand.Add(new EdgeKey { Len = (f.V[i] - f.V[j]).LengthSquared, I = i, J = j });
                }
            }
            cand.Sort((x, y) => { int c = x.Len.CompareTo(y.Len); if (c != 0) return c; c = x.I.CompareTo(y.I); return c != 0 ? c : x.J.CompareTo(y.J); });
            int[] map = new int[n];
            for (int i = 0; i < n; ++i) map[i] = -1;
            var pi = new List<int>(); var pj = new List<int>();
            foreach (EdgeKey e in cand)
                if (map[e.I] < 0 && map[e.J] < 0) { map[e.I] = map[e.J] = pi.Count; pi.Add(e.I); pj.Add(e.J); }
            for (int i = 0; i < n; ++i) if (map[i] < 0) { map[i] = pi.Count; pi.Add(i); pj.Add(-1); }
            int nc = pi.Count;
            var c2 = new Level { N = nc };
            if (f.Sc != null) c2.Sc = new double[nc];
            c2.V = new Vector3d[nc]; c2.Nrm = new Vector3d[nc]; c2.Q = new Vector3d[nc]; c2.Area = new double[nc]; c2.D = new Vector3d[nc]; c2.Kind = new byte[nc];
            for (int k = 0; k < nc; ++k) {
                int i = pi[k], j = pj[k];
                if (j < 0) { if (f.Sc != null) c2.Sc[k] = f.Sc[i]; c2.V[k] = f.V[i]; c2.Nrm[k] = f.Nrm[i]; c2.Q[k] = f.Q[i]; c2.Area[k] = f.Area[i]; c2.D[k] = f.D[i]; c2.Kind[k] = f.Kind[i]; continue; }
                double a = f.Area[i] + f.Area[j];
                if (!(a > 0)) { a = 2; }   // degenerate area: plain averages
                double wi = f.Area[i] > 0 || f.Area[j] > 0 ? f.Area[i] : 1, wj = f.Area[i] > 0 || f.Area[j] > 0 ? f.Area[j] : 1;
                c2.Area[k] = f.Area[i] + f.Area[j];
                if (f.Sc != null) c2.Sc[k] = (wi * f.Sc[i] + wj * f.Sc[j]) / a;
                c2.V[k] = (wi * f.V[i] + wj * f.V[j]) / a;
                Vector3d nn = wi * f.Nrm[i] + wj * f.Nrm[j];
                double nl = nn.Length;
                c2.Nrm[k] = nl > 0 ? nn / nl : f.Nrm[i];
                Vector3d qa, qb;
                CompatOrient(f.Q[i], f.Nrm[i], f.Q[j], f.Nrm[j], out qa, out qb);
                Vector3d q = qa * wi + qb * wj; q -= c2.Nrm[k].Dot(q) * c2.Nrm[k];
                double ql = q.Length;
                c2.Q[k] = ql > 0 ? q / ql : f.Q[i];
                c2.Kind[k] = f.Kind[i];
                if (f.Kind[i] == 1) {
                    Vector3d d = f.D[i] + (f.D[i].Dot(f.D[j]) >= 0 ? f.D[j] : -f.D[j]);
                    d -= c2.Nrm[k].Dot(d) * c2.Nrm[k];
                    double dl = d.Length;
                    if (dl > 0) c2.D[k] = d / dl; else c2.D[k] = f.D[i];
                }
            }
            // adjacency of the clusters in the order of the fine vertices (deterministic)
            var start = new int[nc + 1];
            var lists = new List<int>[nc]; var blists = new List<bool>[nc];
            for (int k = 0; k < nc; ++k) { lists[k] = new List<int>(); blists[k] = new List<bool>(); }
            for (int i = 0; i < n; ++i) {
                int a = map[i];
                for (int k = f.AS[i]; k < f.AS[i + 1]; ++k) {
                    int b = map[f.AJ[k]];
                    if (a == b) continue;
                    int at = lists[a].IndexOf(b);
                    if (at < 0) { lists[a].Add(b); blists[a].Add(f.AB[k]); }
                    else if (f.AB[k]) blists[a][at] = true;
                }
            }
            var aj = new List<int>(); var ab = new List<bool>();
            for (int k = 0; k < nc; ++k) { start[k] = aj.Count; aj.AddRange(lists[k]); ab.AddRange(blists[k]); }
            start[nc] = aj.Count;
            c2.AS = start; c2.AJ = aj.ToArray(); c2.AB = ab.ToArray();
            f.ToCoarse = map;
            return c2;
        }

        struct EdgeKey { public double Len; public int I, J; }

        // the second axis, the lattice line constraint of boundary vertices, the middle points, the colouring
        void Prepare(Level L)
        {
            int n = L.N;
            L.T = new Vector3d[n]; L.Cb = new Vector3d[n];
            for (int i = 0; i < n; ++i) {
                L.T[i] = L.Nrm[i].Cross(L.Q[i]);
                if (L.Kind[i] == 1) {
                    // snap the orientation to the boundary: the cross direction nearest to D becomes q (exactly along the boundary)
                    Vector3d d = L.D[i];
                    double dq = L.Q[i].Dot(d), dt = L.T[i].Dot(d);
                    Vector3d dperp = L.Nrm[i].Cross(d);
                    if (Math.Abs(dq) >= Math.Abs(dt)) { L.Q[i] = dq >= 0 ? d : -d; L.Cb[i] = dperp; }
                    else { L.Q[i] = dt >= 0 ? dperp : -dperp; L.Cb[i] = dperp; }     // the boundary line is along t here: still the perpendicular is removed (#1179)
                    L.T[i] = L.Nrm[i].Cross(L.Q[i]);
                }
            }
            int m = L.AJ.Length;
            L.X = new Vector3d[m]; L.Bq = new Vector3d[m];
            double eps = MiddleRegularization;
            Action<int> pre = i => {
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) {
                    int j = L.AJ[k];
                    Vector3d qa, qb;
                    CompatOrient(L.Q[i], L.Nrm[i], L.Q[j], L.Nrm[j], out qa, out qb);
                    L.Bq[k] = qb;
                    L.X[k] = MiddlePoint(L.V[i], L.Nrm[i], L.V[j], L.Nrm[j], eps);
                }
            };
            if (Parallel && n > 2 * ChunkSize) System.Threading.Tasks.Parallel.For(0, (n + ChunkSize - 1) / ChunkSize, c => { int hi = Math.Min(n, (c + 1) * ChunkSize); for (int i = c * ChunkSize; i < hi; ++i) pre(i); });
            else for (int i = 0; i < n; ++i) pre(i);
            // greedy colouring in vertex order
            var color = new int[n];
            var used = new List<int>();
            int nColors = 0;
            for (int i = 0; i < n; ++i) {
                used.Clear();
                for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) { int j = L.AJ[k]; if (j < i) used.Add(color[j]); }
                int c = 0;
                while (used.Contains(c)) c++;
                color[i] = c; if (c + 1 > nColors) nColors = c + 1;
            }
            var cs = new int[nColors + 1];
            for (int i = 0; i < n; ++i) cs[color[i] + 1]++;
            for (int c = 0; c < nColors; ++c) cs[c + 1] += cs[c];
            var fill = (int[])cs.Clone();
            var order = new int[n];
            for (int i = 0; i < n; ++i) order[fill[color[i]]++] = i;
            L.ColorStart = cs; L.ColorOrder = order;
        }

        // ------------------------------------------------------------------------------------------------ lattice algebra

        /// <summary> the representative of vertex 1's cross {+-q1, +-t1} nearest to q0 (q0 stays), returned as a, b </summary>
        internal static void CompatOrient(Vector3d q0, Vector3d n0, Vector3d q1, Vector3d n1, out Vector3d a, out Vector3d b)
        {
            Vector3d t1 = n1.Cross(q1);
            double dq = q0.Dot(q1), dt = q0.Dot(t1);
            a = q0;
            if (Math.Abs(dq) >= Math.Abs(dt)) b = dq >= 0 ? q1 : -q1;
            else b = dt >= 0 ? t1 : -t1;
        }

        /// <summary>
        /// the point of both tangent planes nearest to the edge midpoint: x = m + alpha n0 + beta n1 with the Gram system
        /// [1 + eps, c; c, 1 + eps] (alpha, beta) = (n0.(p0 - m), n1.(p1 - m)), c = n0.n1 (regularized: singular for parallel planes)
        /// </summary>
        internal static Vector3d MiddlePoint(Vector3d p0, Vector3d n0, Vector3d p1, Vector3d n1, double eps)
        {
            Vector3d m = 0.5 * (p0 + p1);
            double c = n0.Dot(n1), r0 = n0.Dot(p0 - m), r1 = n1.Dot(p1 - m);
            double d0 = 1 + eps, det = d0 * d0 - c * c;
            double alpha = (d0 * r0 - c * r1) / det, beta = (d0 * r1 - c * r0) / det;
            return m + alpha * n0 + beta * n1;
        }

        // the lattice point of (o, q, t) nearest to the vertex position v (both coordinates rounded)
        // the lattice spacing at vertex i of a level
        static double Spacing(Level L, int i, double s) { return L.Sc == null ? s : s * L.Sc[i]; }

        static Vector3d Round(Vector3d o, Level L, int i, double s)
        {
            s = Spacing(L, i, s);
            Vector3d d = L.V[i] - o;
            return o + s * (Math.Round(L.Q[i].Dot(d) / s) * L.Q[i] + Math.Round(L.T[i].Dot(d) / s) * L.T[i]);
        }

        // one update of o_i: the mean misfit of the neighbours, then the boundary constraint and the lattice point nearest to the vertex
        static Vector3d Update(Level L, Vector3d[] O, int i, double s)
        {
            int k0 = L.AS[i], k1 = L.AS[i + 1];
            if (k0 == k1 || L.Kind[i] == 2) return O[i];
            Vector3d oi = O[i], qi = L.Q[i], ti = L.T[i];
            double si = Spacing(L, i, s);
            Vector3d delta = Vector3d.Zero;
            for (int k = k0; k < k1; ++k) {
                int j = L.AJ[k];
                Vector3d x = L.X[k], b = L.Bq[k];
                Vector3d tb = L.Nrm[j].Cross(b);
                Vector3d d = x - oi;
                double sj = Spacing(L, j, s);
                Vector3d a = oi + si * (Math.Round(qi.Dot(d) / si) * qi + Math.Round(ti.Dot(d) / si) * ti);
                Vector3d d2 = a - O[j];
                Vector3d bb = O[j] + sj * (Math.Round(b.Dot(d2) / sj) * b + Math.Round(tb.Dot(d2) / sj) * tb);
                delta += bb - a;
            }
            Vector3d o = oi + delta / (k1 - k0);
            o -= L.Nrm[i].Dot(o - L.V[i]) * L.Nrm[i];
            if (L.Kind[i] == 1) o -= L.Cb[i].Dot(o - L.V[i]) * L.Cb[i];      // the lattice line through the vertex
            return Round(o, L, i, s);
        }

        int Smooth(Level L, Vector3d[] O, double s)
        {
            int sweepsRun = 0;
            int nc = L.ColorStart.Length - 1;
            int Chunk = Math.Max(1, ChunkSize);
            for (int it = 0; it < Sweeps; ++it) {
                bool changed = false;
                for (int c = 0; c < nc; ++c) {
                    int lo = L.ColorStart[c], hi = L.ColorStart[c + 1];
                    int chunks = (hi - lo + Chunk - 1) / Chunk;
                    var flags = new bool[chunks];
                    Action<int> run = ck => {
                        bool ch = false;
                        int e = Math.Min(hi, lo + (ck + 1) * Chunk);
                        for (int p = lo + ck * Chunk; p < e; ++p) {
                            int i = L.ColorOrder[p];
                            Vector3d o = Update(L, O, i, s);
                            if (o.x != O[i].x || o.y != O[i].y || o.z != O[i].z) { O[i] = o; ch = true; }
                        }
                        flags[ck] = ch;
                    };
                    if (Parallel && chunks > 1) System.Threading.Tasks.Parallel.For(0, chunks, run);
                    else for (int ck = 0; ck < chunks; ++ck) run(ck);
                    for (int ck = 0; ck < chunks; ++ck) if (flags[ck]) changed = true;
                }
                sweepsRun++;
                if (!changed) break;
            }
            return sweepsRun;
        }

        // ------------------------------------------------------------------------------------------------ lattice offsets of edges

        /// <summary>
        /// the integer offset (dq, dt) between the lattice points that the two ends of the level 0 adjacency slot k (from vertex i to
        /// AJ[k]) stand for, in the lattice of i: (0, 0) the same lattice point, (+-1, 0) or (0, +-1) neighbouring points.
        /// mismatch is the distance of the two lattice points near the middle point, in units of s (small where the lattices agree).
        /// </summary>
        internal void SlotOffset(int i, int k, out int dq, out int dt, out double mismatch)
        {
            Level L = L0; double s0 = TargetEdgeLength;
            int j = L.AJ[k];
            double s = Spacing(L, i, s0), sj = Spacing(L, j, s0);
            Vector3d x = L.X[k], b = L.Bq[k], tb = L.Nrm[j].Cross(b);
            Vector3d qi = L.Q[i], ti = L.T[i], oi = Origins[i], oj = Origins[j];
            Vector3d d = x - oi;
            double cq0 = Math.Round(qi.Dot(d) / s), ct0 = Math.Round(ti.Dot(d) / s);
            dq = dt = 0; mismatch = double.PositiveInfinity;
            // lattice coordinates beyond a billion (or not finite): no vote, and no integer overflow
            if (!(Math.Abs(cq0) < 1e9) || !(Math.Abs(ct0) < 1e9)) return;
            int kq0 = (int)cq0, kt0 = (int)ct0;
            Vector3d a = oi + s * (kq0 * qi + kt0 * ti);
            Vector3d d2 = a - oj;
            double cq1 = Math.Round(b.Dot(d2) / sj), ct1 = Math.Round(tb.Dot(d2) / sj);
            if (!(Math.Abs(cq1) < 1e9) || !(Math.Abs(ct1) < 1e9)) return;
            int kq1 = (int)cq1, kt1 = (int)ct1;
            Vector3d bb = oj + sj * (kq1 * b + kt1 * tb);
            dq = kq0 - kq1; dt = kt0 - kt1;
            mismatch = (a - bb).Length / s;
        }
    }
}
