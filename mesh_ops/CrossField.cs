// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3.CoreMathPort;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> Result of CrossField.Compute(). </summary>
    public enum CrossFieldStatus
    {
        Success = 0,
        /// <summary> no triangles, only degenerate ones, a vertex without a usable normal or tangent, or non-finite coordinates </summary>
        InvalidMesh = 1,
        /// <summary> a bowtie (non-manifold) vertex; split it first (the input is not modified) </summary>
        NotManifold = 2,
        /// <summary> the linear system could not be factorized (degenerate input) </summary>
        SolverFailed = 3
    }

    /// <summary>
    /// A smooth cross field (4-RoSy field: four tangent directions 90 degrees apart at each point) on a manifold
    /// triangle mesh, after Knöppel, Crane, Pinkall and Schröder, "Globally Optimal Direction Fields" (ACM TOG 32(4),
    /// SIGGRAPH 2013), in the vertex based discretization, written from the paper:
    ///   - each vertex i has a tangent frame (E_i, B_i = N_i x E_i): N_i is the area weighted normal and E_i the
    ///     projection of the edge to the neighbour with the lowest vertex id (the first neighbour whose edge is not
    ///     parallel to the normal, so it does not depend on the storage order of the mesh);
    ///   - the field at i is the complex number z_i = u_i^4, u_i a unit tangent vector in i's frame (|z_i| is a
    ///     relative confidence); the parallel transport along an edge ij adds rho_ij = theta_ji + pi - theta_ij,
    ///     where theta_ij is the angle of the edge ij in i's frame;
    ///   - the smoothness energy is E(z) = sum_ij w_ij |z_j - exp(4 i rho_ij) z_i|^2 with the cotangent weights w_ij
    ///     (mollified at degenerate triangles by ParamMesh, clamped to [1e-3, 1e4]); the smoothest field is the
    ///     eigenvector of the smallest eigenvalue of the Hermitian matrix of E against the lumped vertex areas M,
    ///     which is found by the Lanczos method on (K + eps M)^-1 M (inverse iteration with Krylov acceleration, one complex
    ///     Hermitian sparse Cholesky factorization per connected component via SparseHermitianCholesky);
    ///   - the singularity index of a face is read off the field around it: (sum of the wrapped differences
    ///     arg z_j - arg z_i - 4 rho_ij, plus 4 wrap(sum rho)) / 2 pi, an integer in units of 1/4 (FaceIndex).
    ///     On a closed surface the indices sum to the Euler characteristic (Poincare-Hopf), so a genus 0 surface has
    ///     at least eight singularities of index +1/4.
    ///
    /// Alignment (the paper's idea of a field that follows prescribed directions; the formulation here is our own): a
    /// prescribed cross at a vertex i is a unit b = exp(4 i theta) with a weight omega, and the energy gets the term
    /// omega |z_i - b|^2. Two kinds of terms, and so of problems per connected component:
    ///   - hard terms: the boundary edges (AlignBoundary) and the crease edges (AlignCreases, dihedral angle above
    ///     CreaseAngleDegrees, from MeshTopology): at both ends of the edge, b along the edge, omega = ConstraintWeight (large).
    ///     A component with hard terms has the one linear system (K + W + eps M) z = W b (one factorization, one solve), as in
    ///     the paper's constraints: |z| decays away from the data and vanishes at the singularities. An eigenproblem
    ///     with unit norm would let z shrink to 0 at the constrained vertices instead of winding along them;
    ///   - soft terms: curvature (CurvatureAlignment = s &gt; 0): at each vertex b along the principal direction of
    ///     MeshPrincipalCurvature, omega = min(ConstraintWeight, s (K1 - K2)^2 a_i^2 M_i), M_i the vertex area and
    ///     a_i = (K1 - K2) / (|K1| + |K2|) the anisotropy (0 at an umbilic, 1 for a cylinder: the curvature of a nearly
    ///     round surface is mostly noise in its direction, and gets a weight that the smoothness wins against). The weight does
    ///     not depend on the scale, so s is dimensionless. A component with soft terms only stays an eigenproblem, in
    ///     which the terms are omega |z_i - t b|^2 with one more real unknown t &gt;= 0 in the eigenvector (so the field is the
    ///     smoothest one turned towards the data, and ignores data that is too weak or in conflict; a linear
    ///     problem would be a blend of the lowest eigenvectors where the data is weak).
    /// Without terms the minimizer is only defined up to a rotation of the whole field by a constant angle (a phase of
    /// z); it is fixed by the average of z over each component, weighted with the vertex areas (an own choice).
    ///
    /// Not checked: the orientation of the triangles must be consistent, and unused vertices must have finite coordinates (they
    /// enter the bounding box that the coordinates are centered on). The accessors are valid after a successful Compute() only.
    ///
    /// Input: the vertex ids of Mesh that triangles use (others have no field, see HasField), manifold, orientation
    /// as DMesh3 keeps it. Several connected components get independent fields. Open boundaries are natural
    /// (Neumann) unless aligned. The mesh is not modified. Parallel and deterministic (same bits as serial): the same mesh gives the same
    /// field bit for bit; scaling by a power of two gives the same angles and indices bit for bit (the eigenvalue
    /// scales); a rotation or a translation the same up to rounding (the field of a near-degenerate eigenproblem,
    /// eg a plain sphere, up to its conditioning). Compute() can be called again after changing the settings or the mesh.
    ///
    /// For stage 2 of the quad remeshing (docs/design/QUAD_REMESHING.md): the field per vertex is VertexDirection
    /// (k = 0..3 for all four, VertexNormal, VertexTangent and VertexAngle for the frame), per face
    /// FaceDirection (k = 0..3).
    /// </summary>
    public class CrossField
    {
        /// <summary> the symmetry of the field: four directions per point </summary>
        public const int Symmetry = 4;

        public DMesh3 Mesh;

        /// <summary> s of the class comment: 0 gives the smoothest field, about 1 follows the principal directions where the surface is anisotropic </summary>
        public double CurvatureAlignment = 0;
        /// <summary> align a cross direction with the boundary edges </summary>
        public bool AlignBoundary = true;
        /// <summary> align a cross direction with the crease edges (dihedral angle above CreaseAngleDegrees) </summary>
        public bool AlignCreases = false;
        public double CreaseAngleDegrees = 30;
        /// <summary> omega of the boundary and crease terms (and the cap of the curvature terms), against cotangent weights of about 1 </summary>
        public double ConstraintWeight = 1000;
        /// <summary> rounds of averaging the curvature tensors over the one-ring before they are used (against noise; see MeshPrincipalCurvature) </summary>
        public int CurvatureSmoothing = 2;
        /// <summary> limit of the linear solves of the eigensolver (per component), and the relative eigenresidual |T x - theta x| / (theta |x|) at which it stops (T = (K + eps M)^-1 M) </summary>
        public int MaxIterations = 200;
        public double Tolerance = 1e-4;
        /// <summary> the seed of the random start vector of the eigensolver; the result does not depend on it (up to Tolerance), a test varies it </summary>
        public uint Seed = 0;

        // ------------------------------------------------------------------ results
        public CrossFieldStatus Status = CrossFieldStatus.Success;
        public string Error;
        /// <summary> iterations run; Converged: the last change was below Tolerance (a field that did not converge is still returned) </summary>
        public int Iterations;
        public bool Converged;
        /// <summary> the smoothness energy of the field per unit of its mass, E(z) / (z, M z) in units of 1 / area (the smallest eigenvalue for a field without alignment terms); the largest over the connected components </summary>
        public double Eigenvalue;
        public int Components;
        /// <summary> faces with an index other than 0, in face id order </summary>
        public List<int> SingularFaces = new List<int>();
        /// <summary> index (in units of 1/4) to the number of faces that have it, indices other than 0 only </summary>
        public SortedDictionary<int, int> IndexHistogram = new SortedDictionary<int, int>();
        /// <summary> the sum of the indices of all faces (an integer multiple of 1/4); chi on a closed surface </summary>
        public double IndexSum;

        double[] vAngle, vMag;
        Vector3d[] vN, vE;
        bool[] vHas;
        int[] fIndex;
        Vector3d[] fDir;

        public CrossField(DMesh3 mesh) { Mesh = mesh; }

        public bool Success { get { return Status == CrossFieldStatus.Success; } }

        /// <summary> vertex vid has a field (it belongs to a triangle and the last Compute() succeeded) </summary>
        public bool HasField(int vid) { return vHas != null && vid >= 0 && vid < vHas.Length && vHas[vid]; }
        /// <summary> the unit normal of the frame of vertex vid (area weighted) </summary>
        public Vector3d VertexNormal(int vid) { return vN[vid]; }
        /// <summary> the first axis E of the frame of vertex vid </summary>
        public Vector3d VertexTangent(int vid) { return vE[vid]; }
        /// <summary> the angle phi = arg(z) / 4 in (-pi/4, pi/4] of the field at vid, in its frame: direction 0 = cos(phi) E + sin(phi) N x E </summary>
        public double VertexAngle(int vid) { return vAngle[vid]; }
        /// <summary> |z| at vid, relative to the root mean square of its component (1 on average; small where the field is unsure, eg near singularities) </summary>
        public double VertexMagnitude(int vid) { return vMag[vid]; }
        /// <summary> the unit tangent vector of cross direction k (0..3, each a quarter turn about the normal further) at vertex vid </summary>
        public Vector3d VertexDirection(int vid, int k = 0)
        {
            double a = vAngle[vid] + k * (Math.PI / 2);
            return CoreMath.Cos(a) * vE[vid] + CoreMath.Sin(a) * vN[vid].Cross(vE[vid]);
        }

        /// <summary>
        /// the unit direction k (0..3) of the field on face tid, in the plane of the face (zero for a degenerate face): the
        /// corner fields are rotated into the face plane and averaged as 4-RoSy fields (weighted with |z|)
        /// </summary>
        public Vector3d FaceDirection(int tid, int k = 0)
        {
            Vector3d d = fDir[tid];
            if (k == 0 || d.LengthSquared == 0) return d;
            Index3i t = Mesh.GetTriangle(tid);
            Vector3d n = (Mesh.GetVertex(t.b) - Mesh.GetVertex(t.a)).Cross(Mesh.GetVertex(t.c) - Mesh.GetVertex(t.a));
            n /= n.Length;   // not Normalized: its absolute length threshold breaks the scale invariance
            double a = k * (Math.PI / 2);
            return CoreMath.Cos(a) * d + CoreMath.Sin(a) * n.Cross(d);
        }
        /// <summary> the singularity index of face tid in units of 1/4: 0 regular, 1 for a +1/4 cone (a valence 3 point of a quad mesh), -1 for -1/4 </summary>
        public int FaceIndex(int tid) { return fIndex[tid]; }

        public CrossFieldStatus Compute()
        {
            Status = CrossFieldStatus.Success; Error = null;
            Iterations = 0; Converged = false; Eigenvalue = double.NaN; Components = 0; IndexSum = 0;
            SingularFaces = new List<int>(); IndexHistogram = new SortedDictionary<int, int>();
            vAngle = null; vMag = null; vN = null; vE = null; vHas = null; fIndex = null; fDir = null;

            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.Valid)
                return Fail(pm.Error != null && pm.Error.Contains("bowtie") ? CrossFieldStatus.NotManifold : CrossFieldStatus.InvalidMesh, pm.Error);
            int N = pm.N;
            for (int i = 0; i < N; ++i)
                if (!pm.Pos[i].IsFinite) return Fail(CrossFieldStatus.InvalidMesh, "vertex " + pm.ToVertex[i] + " is not finite");

            // frames: normal, area, tangent
            var nrm = new Vector3d[N]; var mass = new double[N];
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                if (f.Degenerate) continue;
                Index3i t = pm.Tris[ti];
                Vector3d c = (pm.Pos[t.b] - pm.Pos[t.a]).Cross(pm.Pos[t.c] - pm.Pos[t.a]);
                for (int k = 0; k < 3; ++k) { nrm[t[k]] += c; mass[t[k]] += f.Area / 3; }
            }
            // edges (first seen order), their triangles, and the cotangent weights
            var edgeOf = new Dictionary<long, int>();
            var ea = new List<int>(); var eb = new List<int>(); var et0 = new List<int>(); var et1 = new List<int>();
            var triEdge = new int[3 * pm.Tris.Count];
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                Index3i t = pm.Tris[ti];
                for (int k = 0; k < 3; ++k) {
                    int a = t[k], b = t[(k + 1) % 3];
                    long key = a < b ? (long)a * N + b : (long)b * N + a;
                    int e;
                    if (!edgeOf.TryGetValue(key, out e)) {
                        e = ea.Count; edgeOf[key] = e;
                        ea.Add(Math.Min(a, b)); eb.Add(Math.Max(a, b)); et0.Add(ti); et1.Add(-1);
                    } else
                        et1[e] = ti;
                    triEdge[3 * ti + k] = e;
                }
            }
            int E = ea.Count;
            var w = new double[E];
            pm.ForEachCotanEdge((a, b, c) => {
                long key = a < b ? (long)a * N + b : (long)b * N + a;
                w[edgeOf[key]] += c;
            }, true);
            for (int e = 0; e < E; ++e)
                w[e] = Math.Min(Math.Max(w[e], 1e-3), 1e4);

            var vE_ = new Vector3d[N]; var vB = new Vector3d[N];
            var bestId = new int[N];
            for (int i = 0; i < N; ++i) {
                double l = nrm[i].Length;
                if (!(l > 0) || !(mass[i] > 0)) return Fail(CrossFieldStatus.InvalidMesh, "vertex " + pm.ToVertex[i] + " has no usable normal (only degenerate triangles)");
                nrm[i] /= l;
                bestId[i] = int.MaxValue;
            }
            for (int e = 0; e < E; ++e) {
                for (int s = 0; s < 2; ++s) {
                    int i = s == 0 ? ea[e] : eb[e], j = s == 0 ? eb[e] : ea[e];
                    Vector3d d = pm.Pos[j] - pm.Pos[i];
                    Vector3d p = d - d.Dot(nrm[i]) * nrm[i];
                    double pl = p.Length;
                    if (!(pl > 1e-6 * d.Length) || pm.ToVertex[j] >= bestId[i]) continue;
                    bestId[i] = pm.ToVertex[j]; vE_[i] = p / pl;
                }
            }
            for (int i = 0; i < N; ++i) {
                if (bestId[i] == int.MaxValue) return Fail(CrossFieldStatus.InvalidMesh, "vertex " + pm.ToVertex[i] + " has no edge in its tangent plane");
                vB[i] = nrm[i].Cross(vE_[i]);
            }
            var rho = new double[E];
            gParallel.ForRange(E, e => {     // each edge writes only its own entry
                int a = ea[e], b = eb[e];
                Vector3d d = pm.Pos[b] - pm.Pos[a];
                rho[e] = CoreMath.Atan2(-d.Dot(vB[b]), -d.Dot(vE_[b])) + Math.PI - CoreMath.Atan2(d.Dot(vB[a]), d.Dot(vE_[a]));
            });

            // connected components of the vertices: each gets its own matrix and eigenproblem (local index loc)
            var comp = new int[N];
            Components = LabelComponents(pm, comp);
            var loc = new int[N]; var compSize = new int[Components];
            for (int i = 0; i < N; ++i) loc[i] = compSize[comp[i]]++;
            var members = new int[Components][];
            for (int c = 0; c < Components; ++c) members[c] = new int[compSize[c]];
            for (int i = 0; i < N; ++i) members[comp[i]][loc[i]] = i;
            double meanMass = 0;
            for (int i = 0; i < N; ++i) meanMass += mass[i];
            meanMass /= N;

            // alignment terms: (vertex, b, omega), the cross at the vertex along b = exp(4 i theta)
            var termV = new List<int>(); var termRe = new List<double>(); var termIm = new List<double>(); var termW = new List<double>(); var termHard = new List<bool>();
            Action<int, Vector3d, double, bool> addTerm = (i, dir, omega, hard) => {
                Vector3d q = dir - dir.Dot(nrm[i]) * nrm[i];
                double ql = q.Length;
                if (!(ql > 0) || !(omega > 0)) return;
                double th = CoreMath.Atan2(q.Dot(vB[i]), q.Dot(vE_[i]));
                termV.Add(i); termRe.Add(CoreMath.Cos(Symmetry * th)); termIm.Add(CoreMath.Sin(Symmetry * th)); termW.Add(omega); termHard.Add(hard);
            };
            HashSet<long> creaseKeys = null;
            if (AlignCreases) {
                var topo = new MeshTopology(Mesh) { CreaseAngle = CreaseAngleDegrees, CorrectlyRoundedThreshold = true };
                topo.Compute();
                creaseKeys = new HashSet<long>();
                foreach (int eid in topo.CreaseEdges) {
                    Index2i ev = Mesh.GetEdgeV(eid);
                    int a = pm.ToIndex[ev.a], b = pm.ToIndex[ev.b];
                    creaseKeys.Add(a < b ? (long)a * N + b : (long)b * N + a);
                }
            }
            for (int e = 0; e < E; ++e) {
                bool boundary = et1[e] < 0;
                bool crease = creaseKeys != null && creaseKeys.Contains((long)ea[e] * N + eb[e]);
                if (!((boundary && AlignBoundary) || crease)) continue;
                Vector3d d = pm.Pos[eb[e]] - pm.Pos[ea[e]];
                addTerm(ea[e], d, ConstraintWeight, true); addTerm(eb[e], d, ConstraintWeight, true);
            }
            if (CurvatureAlignment > 0) {
                MeshPrincipalCurvature pc = MeshPrincipalCurvature.Compute(Mesh, CurvatureSmoothing);
                for (int i = 0; i < N; ++i) {
                    int v = pm.ToVertex[i];
                    if (!pc.Valid[v]) continue;
                    double kd = pc.K1[v] - pc.K2[v], ks = Math.Abs(pc.K1[v]) + Math.Abs(pc.K2[v]);
                    double aniso = ks > 0 ? kd / ks : 0;
                    addTerm(i, pc.Dir1[v], Math.Min(ConstraintWeight, CurvatureAlignment * kd * kd * aniso * aniso * mass[i]), false);
                }
            }
            // the kind of problem of each component: 0 the smoothest field (eigenproblem), 1 with soft terms only (the
            // eigenproblem with one more unknown t), 2 with hard terms (a linear problem, boundary and creases)
            var mode = new int[Components];
            for (int k = 0; k < termV.Count; ++k) mode[comp[termV[k]]] = Math.Max(mode[comp[termV[k]]], termHard[k] ? 2 : 1);

            // matrices: the connection energy + alignment terms + a small mass shift (interleaved re, im)
            var chol = new SparseHermitianCholesky[Components];
            for (int c = 0; c < Components; ++c) chol[c] = new SparseHermitianCholesky(compSize[c]);
            for (int e = 0; e < E; ++e) {
                int c = comp[ea[e]], a = loc[ea[e]], b = loc[eb[e]]; double we = w[e];
                double r = Symmetry * rho[e], cr = CoreMath.Cos(r), sr = CoreMath.Sin(r);
                SparseHermitianCholesky ch = chol[c];
                ch.Add(a, a, we, 0); ch.Add(b, b, we, 0);
                ch.Add(b, a, -we * cr, -we * sr);         // H[j,i] = -w exp(4 i rho_ij)
            }
            var compMeanMass = new double[Components];
            for (int i = 0; i < N; ++i) compMeanMass[comp[i]] += mass[i] / compSize[comp[i]];
            for (int i = 0; i < N; ++i) { SparseHermitianCholesky ch = chol[comp[i]]; int a = loc[i]; double sh = 1e-8 * mass[i] / compMeanMass[comp[i]]; ch.Add(a, a, sh, 0); }
            // omega |z_i - b|^2 = omega (|z_i|^2 - 2 Re(conj(b) z_i) + 1) in mode 2: the diagonal and the right hand side g = omega b;
            // omega |z_i - t b|^2 = omega (|z_i|^2 - 2 t Re(conj(b) z_i) + t^2) in mode 1: the diagonal, and t is eliminated (t = (g, z) / d,
            // d = sum of the omega): the matrix is the sparse one minus the rank one matrix g g^T / d, which the solves handle by the
            // Sherman-Morrison formula (a coupling row of t in the matrix would make the ordering quadratic)
            var dataRhs = new double[2 * N]; var tDiag = new double[Components];
            for (int k = 0; k < termV.Count; ++k) {
                int i = termV[k]; int c = comp[i]; int a = loc[i]; double om = termW[k];
                SparseHermitianCholesky ch = chol[c];
                ch.Add(a, a, om, 0);
                dataRhs[2 * i] += om * termRe[k]; dataRhs[2 * i + 1] += om * termIm[k];
                tDiag[c] += om;
            }
            for (int c = 0; c < Components; ++c)
                if (!chol[c].Factorize())
                    return Fail(CrossFieldStatus.SolverFailed, "the field matrix could not be factorized: " + chol[c].Error);

            // the eigenvector of the smallest eigenvalue of each component, by Lanczos iteration on (K + eps M)^-1 M
            var x = new double[2 * N]; var sign = new double[Components];
            Converged = true;
            for (int c = 0; c < Components; ++c) {
                int n = compSize[c];
                var g = new double[2 * n];
                for (int l = 0; l < n; ++l) { g[2 * l] = dataRhs[2 * members[c][l]]; g[2 * l + 1] = dataRhs[2 * members[c][l] + 1]; }
                SparseHermitianCholesky ch = chol[c];
                double[] xc;
                if (mode[c] == 2) {
                    xc = new double[2 * n];
                    ch.Solve(g, xc);
                    Iterations += 1;
                } else {
                    var mc = new double[n];
                    for (int l = 0; l < n; ++l) mc[l] = mass[members[c][l]];
                    Action<double[], double[]> solve = ch.Solve;
                    if (mode[c] == 1) {
                        var y0 = new double[2 * n];
                        ch.Solve(g, y0);
                        double den = tDiag[c] - Dot(g, y0);
                        if (!(den > 1e-12 * tDiag[c])) return Fail(CrossFieldStatus.SolverFailed, "the alignment weights are too large for the field matrix (rank one update)");
                        solve = (rhs, outv) => {
                            ch.Solve(rhs, outv);
                            double alpha = Dot(g, outv) / den;
                            for (int k = 0; k < outv.Length; ++k) outv[k] += alpha * y0[k];
                        };
                    }
                    double theta; int its; bool conv;
                    xc = SmallestEigenvector(solve, mc, Math.Min(MaxIterations, 40), MaxIterations, Tolerance, (uint)c + 0x10001u * Seed, out theta, out its, out conv);
                    Iterations += its; Converged &= conv;
                    if (mode[c] == 1) sign[c] = Dot(g, xc) < 0 ? -1 : 1;
                }
                for (int l = 0; l < n; ++l) { x[2 * members[c][l]] = xc[2 * l]; x[2 * members[c][l] + 1] = xc[2 * l + 1]; }
            }

            for (int k = 0; k < x.Length; ++k)
                if (double.IsNaN(x[k]) || double.IsInfinity(x[k])) return Fail(CrossFieldStatus.SolverFailed, "the field is not finite");
            Gauge(x, comp, mass, mode, sign, pm);
            // the smoothness energy per unit mass of each component
            var energy = new double[Components]; var norm = new double[Components];
            for (int e = 0; e < E; ++e) {
                int a = ea[e], b = eb[e];
                double r = Symmetry * rho[e], cr = CoreMath.Cos(r), sr = CoreMath.Sin(r);
                double dre = x[2 * b] - (cr * x[2 * a] - sr * x[2 * a + 1]), dim = x[2 * b + 1] - (sr * x[2 * a] + cr * x[2 * a + 1]);
                energy[comp[a]] += w[e] * (dre * dre + dim * dim);
            }
            for (int i = 0; i < N; ++i) norm[comp[i]] += mass[i] * (x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]);
            Eigenvalue = 0;
            for (int c = 0; c < Components; ++c) Eigenvalue = Math.Max(Eigenvalue, energy[c] / norm[c]);

            // per vertex results (vertex ids of the mesh)
            int nv = Mesh.MaxVertexID;
            vAngle = new double[nv]; vMag = new double[nv]; vN = new Vector3d[nv]; vE = new Vector3d[nv]; vHas = new bool[nv];
            var phi = new double[N]; var mag = new double[N];
            for (int i = 0; i < N; ++i) {
                int v = pm.ToVertex[i];
                phi[i] = CoreMath.Atan2(x[2 * i + 1], x[2 * i]) / Symmetry;
                mag[i] = Math.Sqrt(x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]);
                vAngle[v] = phi[i]; vMag[v] = mag[i]; vN[v] = nrm[i]; vE[v] = vE_[i]; vHas[v] = true;
            }
            // note: vN and vE are in the (center-shifted) coordinates of ParamMesh, which are the mesh's own up to a translation: vectors are the same

            // per face: index and direction
            fIndex = new int[Mesh.MaxTriangleID]; fDir = new Vector3d[Mesh.MaxTriangleID];
            // (in parallel: each face writes only its own entries; the lists are then built serially in face order)
            int[] tidArr = System.Linq.Enumerable.ToArray(Mesh.TriangleIndices());
            gParallel.ForRange(tidArr.Length, ti2 => {
                int tid = tidArr[ti2];
                Index3i t = pm.Tris[ti2];
                double sd = 0, sr = 0;
                for (int k = 0; k < 3; ++k) {
                    int i = t[k], j = t[(k + 1) % 3];
                    int e = triEdge[3 * ti2 + k];
                    double r = ea[e] == i ? rho[e] : -rho[e];
                    sr += r;
                    sd += Wrap(CoreMath.Atan2(x[2 * j + 1], x[2 * j]) - CoreMath.Atan2(x[2 * i + 1], x[2 * i]) - Symmetry * r);
                }
                fIndex[tid] = (int)Math.Round((sd + Symmetry * Wrap(sr)) / (2 * Math.PI));
                fDir[tid] = FaceDirectionFrom(pm, ti2, t, nrm, vE_, vB, phi, mag);
            });
            foreach (int tid in tidArr) {
                int idx = fIndex[tid];
                if (idx == 0) continue;
                SingularFaces.Add(tid); IndexSum += idx / (double)Symmetry;
                int cnt; IndexHistogram.TryGetValue(idx, out cnt); IndexHistogram[idx] = cnt + 1;
            }
            Status = CrossFieldStatus.Success;
            return Status;
        }


        // The eigenvector of the largest eigenvalue theta of T = A^-1 M (A the factorized matrix, M the diagonal mass of the
        // complex unknowns, mass[i] for both unknowns of vertex i), scaled to a mass weighted root mean square of 1. T is
        // self-adjoint for the M inner product, so this is the Lanczos method with full reorthogonalization and restarts
        // from the Ritz vector. It stops when the residual |T x - theta x|_M is below tol theta |x|_M, or after maxIt solves.
        static double[] SmallestEigenvector(Action<double[], double[]> solve, double[] mass, int krylov, int maxIt, double tol, uint seedOffset,
            out double thetaOut, out int iterations, out bool converged)
        {
            int n = mass.Length, dim = 2 * n;
            double total = 0;
            for (int i = 0; i < n; ++i) total += mass[i];
            Func<double[], double[], double> dot = (u, v) => {
                double s = 0;
                for (int i = 0; i < n; ++i) s += mass[i] * (u[2 * i] * v[2 * i] + u[2 * i + 1] * v[2 * i + 1]);
                return s;
            };
            ulong seed = 0x9E3779B97F4A7C15UL + seedOffset;
            var start = new double[dim];
            for (int k = 0; k < dim; ++k) start[k] = Next(ref seed) - 0.5;
            iterations = 0; converged = false; thetaOut = 1;
            var rhs = new double[dim];
            int m = Math.Max(1, Math.Min(krylov, dim));
            var best = start;
            while (iterations < maxIt && !converged) {
                var q = new List<double[]>();
                var alpha = new List<double>(); var beta = new List<double>();
                double nrm = Math.Sqrt(dot(start, start));
                var q0 = new double[dim];
                for (int k = 0; k < dim; ++k) q0[k] = start[k] / nrm;
                q.Add(q0);
                double theta = 1; double[] ritz = q0;
                for (int j = 0; j < m && iterations < maxIt; ++j) {
                    for (int i = 0; i < n; ++i) { rhs[2 * i] = mass[i] * q[j][2 * i]; rhs[2 * i + 1] = mass[i] * q[j][2 * i + 1]; }
                    var w = new double[dim];
                    solve(rhs, w);
                    ++iterations;
                    for (int pass = 0; pass < 2; ++pass)
                        for (int i = 0; i <= j; ++i) {
                            double c = dot(w, q[i]);
                            if (pass == 0 && i == j) alpha.Add(c);
                            for (int k = 0; k < dim; ++k) w[k] -= c * q[i][k];
                        }
                    double bj = Math.Sqrt(dot(w, w));
                    // Ritz pair of the tridiagonal matrix of size j + 1
                    int sz = j + 1;
                    double[] s = new double[sz]; double resid;
                    if (sz == 1) { theta = alpha[0]; s[0] = 1; }
                    else {
                        var tm = new double[sz * sz];
                        for (int i = 0; i < sz; ++i) {
                            tm[i * sz + i] = alpha[i];
                            if (i + 1 < sz) { tm[i * sz + i + 1] = beta[i]; tm[(i + 1) * sz + i] = beta[i]; }
                        }
                        var es = new SymmetricEigenSolver(sz, 1000);
                        es.Solve(tm, SymmetricEigenSolver.SortType.Decreasing);
                        theta = es.GetEigenvalue(0);
                        es.GetEigenvector(0, s);
                    }
                    resid = bj * Math.Abs(s[sz - 1]);
                    ritz = new double[dim];
                    for (int i = 0; i < sz; ++i)
                        for (int k = 0; k < dim; ++k) ritz[k] += s[i] * q[i][k];
                    if (resid <= tol * theta || bj <= 1e-14 * Math.Abs(theta)) { converged = true; break; }
                    var qn = new double[dim];
                    for (int k = 0; k < dim; ++k) qn[k] = w[k] / bj;
                    q.Add(qn); beta.Add(bj);
                }
                best = ritz; thetaOut = theta;
                start = ritz;
            }
            double scale = Math.Sqrt(total / dot(best, best));
            for (int k = 0; k < dim; ++k) best[k] *= scale;
            return best;
        }

        CrossFieldStatus Fail(CrossFieldStatus s, string error)
        {
            Status = s; Error = error;
            return s;
        }

        static Vector3d FaceDirectionFrom(ParamMesh pm, int ti, Index3i t, Vector3d[] nrm, Vector3d[] vE_, Vector3d[] vB, double[] phi, double[] mag)
        {
            if (pm.Frames[ti].Degenerate) return Vector3d.Zero;
            Vector3d pa = pm.Pos[t.a];
            Vector3d fn = (pm.Pos[t.b] - pa).Cross(pm.Pos[t.c] - pa);
            fn /= fn.Length;
            Vector3d fu = (pm.Pos[t.b] - pa) / (pm.Pos[t.b] - pa).Length, fv = fn.Cross(fu);
            double re = 0, im = 0, first = 0;
            for (int k = 0; k < 3; ++k) {
                int i = t[k];
                Vector3d d = CoreMath.Cos(phi[i]) * vE_[i] + CoreMath.Sin(phi[i]) * vB[i];
                double cs = nrm[i].Dot(fn);
                Vector3d q = cs > -0.5 ? MeshPrincipalCurvature.Rotate(d, nrm[i], nrm[i].Cross(fn), cs) : d - d.Dot(fn) * fn;
                double al = CoreMath.Atan2(q.Dot(fv), q.Dot(fu));
                if (k == 0) first = al;
                re += mag[i] * CoreMath.Cos(Symmetry * al); im += mag[i] * CoreMath.Sin(Symmetry * al);
            }
            double a = (re == 0 && im == 0) ? first : CoreMath.Atan2(im, re) / Symmetry;
            return CoreMath.Cos(a) * fu + CoreMath.Sin(a) * fv;
        }

        // the phase of a component without alignment terms, the sign of one with soft terms (t >= 0), and the scale of each
        // component to a mass weighted root mean square of 1
        static void Gauge(double[] x, int[] comp, double[] mass, int[] mode, double[] sign, ParamMesh pm)
        {
            int N = comp.Length, C = mode.Length;
            var sx = new double[C]; var sy = new double[C]; var sa = new double[C]; var sq = new double[C]; var tot = new double[C];
            var maxMag = new double[C];
            for (int i = 0; i < N; ++i) {
                int c = comp[i];
                double m = Math.Sqrt(x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]);
                sx[c] += mass[i] * x[2 * i]; sy[c] += mass[i] * x[2 * i + 1]; sa[c] += mass[i] * m; sq[c] += mass[i] * m * m; tot[c] += mass[i];
                maxMag[c] = Math.Max(maxMag[c], m);
            }
            // for the fallback: the vertex with the lowest id among those with at least half the largest magnitude
            var fbId = new int[C]; var fbIdx = new int[C];
            for (int c = 0; c < C; ++c) { fbId[c] = int.MaxValue; fbIdx[c] = -1; }
            for (int i = 0; i < N; ++i) {
                int c = comp[i];
                double m = Math.Sqrt(x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]);
                if (m >= 0.5 * maxMag[c] && pm.ToVertex[i] < fbId[c]) { fbId[c] = pm.ToVertex[i]; fbIdx[c] = i; }
            }
            var cr = new double[C]; var ci = new double[C];      // the factor (cr + i ci) that multiplies z of the component
            for (int c = 0; c < C; ++c) {
                double scale = 1 / Math.Sqrt(sq[c] / tot[c]);
                if (mode[c] == 2) { cr[c] = scale; ci[c] = 0; continue; }
                if (mode[c] == 1) { cr[c] = sign[c] * scale; ci[c] = 0; continue; }
                double g = Math.Sqrt(sx[c] * sx[c] + sy[c] * sy[c]);
                if (g > 1e-3 * sa[c]) { cr[c] = scale * sx[c] / g; ci[c] = -scale * sy[c] / g; continue; }
                int f = fbIdx[c];
                double m = Math.Sqrt(x[2 * f] * x[2 * f] + x[2 * f + 1] * x[2 * f + 1]);
                cr[c] = scale * x[2 * f] / m; ci[c] = -scale * x[2 * f + 1] / m;
            }
            for (int i = 0; i < N; ++i) {
                int c = comp[i];
                double re = x[2 * i], im = x[2 * i + 1];
                x[2 * i] = re * cr[c] - im * ci[c]; x[2 * i + 1] = re * ci[c] + im * cr[c];
            }
        }

        static int LabelComponents(ParamMesh pm, int[] comp)
        {
            int N = pm.N;
            int[] parent = new int[N];
            for (int i = 0; i < N; ++i) parent[i] = i;
            Func<int, int> find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            foreach (Index3i t in pm.Tris) {
                int ra = find(t.a), rb = find(t.b), rc = find(t.c);
                if (rb != ra) parent[rb] = ra;
                rc = find(t.c); ra = find(t.a);
                if (rc != ra) parent[rc] = ra;
            }
            var label = new int[N];
            for (int i = 0; i < N; ++i) label[i] = -1;
            int n = 0;
            for (int i = 0; i < N; ++i) {
                int r = find(i);
                if (label[r] < 0) label[r] = n++;
                comp[i] = label[r];
            }
            return n;
        }

        static double Next(ref ulong s)
        {
            s += 0x9E3779B97F4A7C15UL;
            ulong z = s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }

        static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; ++i) s += a[i] * b[i];
            return s;
        }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }
    }
}
