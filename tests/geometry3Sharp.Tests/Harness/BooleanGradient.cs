using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Research prototype (docs/research/EXACT_BOOLEAN_GRADIENTS_2026-11.md), test-only: the gradient of the volume and
    /// the area of an ExactMeshBoolean result (the exact result, before the rounding) with respect to the input vertex
    /// positions.
    ///
    /// Volume (exact, a rational): every kept piece P lies in its source triangle T and moves with it (the velocity of a
    /// point y of T is sum_i lambda_i(y) dx_i). By the Hadamard / Reynolds formula dV = integral over the result surface of
    /// v . n; the intersection curves have zero area and drop out, so the implicit points are never differentiated:
    ///   dV/dx_i = sum over kept P with source T containing i of s_P lambda_i^T(centroid(P)) N_P
    /// (N_P the piece's area vector, oriented like T; s_P = -1 if the piece is flipped; lambda affine, so its integral over
    /// P is area times the value at the centroid). Exact in BigRational: the corners are the arrangement's exact points.
    ///
    /// Area (double, no proven bound in this prototype): a flat piece's area changes only through the in-plane normal
    /// velocity of its boundary, dA_P = integral over the boundary of P of v . nu. On an edge of T (and on edges inside T) v
    /// is T's own velocity; on a seam with a triangle U the seam point moves so that it stays on both planes, and its
    /// in-plane normal velocity is (w_U - w_T cos theta) / (n_U . nu), with w the normal velocity of each plane: this is
    /// where the other mesh's vertices enter, with the factor 1 / sin theta.
    /// </summary>
    internal sealed class BooleanGradient
    {
        public BigRational Volume;                          // world units
        public readonly Dictionary<long, BigRational[]> VolumeGradient = new Dictionary<long, BigRational[]>();
        public double Area;
        public readonly Dictionary<long, Vector3d> AreaGradient = new Dictionary<long, Vector3d>();
        public int KeptPieces, SeamEdges, DegenerateEdges;
        public int ScaleExp;

        public static long Key(int mesh, int vid) => ((long)mesh << 32) | (uint)vid;

        public BigRational[] VolumeGradientAt(int mesh, int vid)
            => VolumeGradient.TryGetValue(Key(mesh, vid), out BigRational[] g) ? g : new[] { Zero, Zero, Zero };

        public Vector3d AreaGradientAt(int mesh, int vid)
            => AreaGradient.TryGetValue(Key(mesh, vid), out Vector3d g) ? g : Vector3d.Zero;

        /// <summary> sum over the vertices of dV/dx . delta (delta per mesh, indexed by vertex id) </summary>
        public BigRational VolumeDirectional(Vector3d[][] delta)
        {
            BigRational r = Zero;
            for (int m = 0; m < delta.Length; ++m)
                if (delta[m] != null)
                    for (int v = 0; v < delta[m].Length; ++v) {
                        BigRational[] g = VolumeGradientAt(m, v);
                        r = r + g[0] * BigRational.Of(delta[m][v].x) + g[1] * BigRational.Of(delta[m][v].y) + g[2] * BigRational.Of(delta[m][v].z);
                    }
            return r;
        }

        public double AreaDirectional(Vector3d[][] delta)
        {
            double r = 0;
            for (int m = 0; m < delta.Length; ++m)
                if (delta[m] != null)
                    for (int v = 0; v < delta[m].Length; ++v)
                        r += AreaGradientAt(m, v).Dot(delta[m][v]);
            return r;
        }

        static readonly BigRational Zero = new BigRational(BigInteger.Zero, BigInteger.One);

        public static BooleanGradient Compute(DMesh3 a, DMesh3 b, Ops op, bool area = true)
        {
            var bo = new ExactMeshBoolean(a, b, op) { RecordCertificate = true, ComputeExactVertices = true };
            if (bo.Compute() == false)
                throw new InvalidOperationException("boolean failed: " + bo.Error);
            ExactMeshArrangement arr = bo.Arrangement;
            bool[] keep = bo.CertificateKeep, flip = bo.CertificateFlip;
            var g = new BooleanGradient { ScaleExp = arr.ScaleExp };
            // point id -> vertex id, per mesh
            var vertexOf = new Dictionary<int, int>[arr.Meshes.Count];
            for (int m = 0; m < arr.Meshes.Count; ++m) {
                vertexOf[m] = new Dictionary<int, int>();
                foreach (int vid in arr.Meshes[m].VertexIndices())
                    vertexOf[m][arr.Canonical(arr.VertexPoints[m][vid])] = vid;
            }
            var tri = new Tri[arr.Triangles.Count];
            for (int t = 0; t < tri.Length; ++t) {
                var it = arr.Triangles[t];
                Index3i c = it.Corners;
                var x = new Tri { Mesh = it.Mesh, P = new[] { R(arr.Exact(c.a)), R(arr.Exact(c.b)), R(arr.Exact(c.c)) } };
                x.Vid = new[] { vertexOf[it.Mesh][arr.Canonical(c.a)], vertexOf[it.Mesh][arr.Canonical(c.b)], vertexOf[it.Mesh][arr.Canonical(c.c)] };
                x.N = Cross(Sub(x.P[1], x.P[0]), Sub(x.P[2], x.P[0]));
                x.NN = Dot(x.N, x.N);
                tri[t] = x;
            }
            BigRational vol = Zero, third = new BigRational(1, 3), half = new BigRational(1, 2), sixth = new BigRational(1, 6);
            double areaGrid = 0;
            var ag = new Dictionary<long, Vector3d>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (keep[i] == false) continue;
                g.KeptPieces++;
                var ot = arr.Output[i];
                Tri T = tri[ot.Source];
                Q3[] p = { R(arr.Exact(ot.A)), R(arr.Exact(ot.B)), R(arr.Exact(ot.C)) };
                BigRational s = flip[i] ? new BigRational(-1, 1) : new BigRational(1, 1);
                Q3 np = Scale(Cross(Sub(p[1], p[0]), Sub(p[2], p[0])), half);
                vol = vol + s * sixth * Dot(p[0], Cross(p[1], p[2]));
                Q3 cen = Scale(Add(Add(p[0], p[1]), p[2]), third);
                BigRational[] lam = Bary(T, cen);
                for (int k = 0; k < 3; ++k) {
                    long key = Key(T.Mesh, T.Vid[k]);
                    if (g.VolumeGradient.TryGetValue(key, out BigRational[] acc) == false)
                        g.VolumeGradient[key] = acc = new[] { Zero, Zero, Zero };
                    BigRational w = s * lam[k];
                    acc[0] = acc[0] + w * np.X; acc[1] = acc[1] + w * np.Y; acc[2] = acc[2] + w * np.Z;
                }
                if (area == false) continue;
                areaGrid += Math.Sqrt(D(Dot(np, np)));
                Vector3d nT = Unit(V(T.N));
                for (int k = 0; k < 3; ++k) {
                    Q3 e0 = p[k], e1 = p[(k + 1) % 3];
                    Vector3d dir = V(Sub(e1, e0));
                    double len = dir.Length;
                    Vector3d nu = Unit(dir.Cross(nT));
                    Q3 mid = Scale(Add(e0, e1), half);
                    BigRational[] lt = Bary(T, mid);
                    int u = OnBoundary(T, e0, e1) ? -1 : Seam(tri, ot.Source, T, e0, e1, mid);
                    if (u == -2) g.DegenerateEdges++;
                    if (u < 0) {
                        for (int j = 0; j < 3; ++j) Add(ag, Key(T.Mesh, T.Vid[j]), (len * D(lt[j])) * nu);
                        continue;
                    }
                    g.SeamEdges++;
                    Tri U = tri[u];
                    Vector3d nU = Unit(V(U.N));
                    double cos = nT.Dot(nU), sn = nU.Dot(nu);
                    BigRational[] lu = Bary(U, mid);
                    for (int j = 0; j < 3; ++j) {
                        Add(ag, Key(T.Mesh, T.Vid[j]), (-cos / sn * len * D(lt[j])) * nT);
                        Add(ag, Key(U.Mesh, U.Vid[j]), (1 / sn * len * D(lu[j])) * nU);
                    }
                }
            }
            int e = arr.ScaleExp;
            g.Volume = vol * Pow2(3 * e);
            BigRational f = Pow2(2 * e);
            foreach (BigRational[] acc in g.VolumeGradient.Values)
                for (int k = 0; k < 3; ++k) acc[k] = acc[k] * f;
            g.Area = ExactPoint3.ScaleB(areaGrid, 2 * e);
            foreach (var kv in ag) g.AreaGradient[kv.Key] = ExactPoint3.ScaleB(1.0, e) * kv.Value;
            return g;
        }

        sealed class Tri { public int Mesh; public int[] Vid; public Q3[] P; public Q3 N; public BigRational NN; }

        // barycentric coordinates of a point of T's plane
        static BigRational[] Bary(Tri T, Q3 y)
        {
            var l = new BigRational[3];
            for (int k = 0; k < 3; ++k)
                l[k] = Dot(Cross(Sub(T.P[(k + 1) % 3], y), Sub(T.P[(k + 2) % 3], y)), T.N) / T.NN;
            return l;
        }

        // the segment lies on an edge line of T
        static bool OnBoundary(Tri T, Q3 a, Q3 b)
        {
            for (int k = 0; k < 3; ++k) {
                Q3 d = Sub(T.P[(k + 1) % 3], T.P[k]);
                if (IsZero(Cross(d, Sub(a, T.P[k]))) && IsZero(Cross(d, Sub(b, T.P[k]))))
                    return true;
            }
            return false;
        }

        // the triangle U (not coplanar with T) whose plane holds the segment and that contains its midpoint; -1 if none
        static int Seam(Tri[] tri, int source, Tri T, Q3 a, Q3 b, Q3 mid)
        {
            int found = -1;
            for (int u = 0; u < tri.Length; ++u) {
                if (u == source) continue;
                Tri U = tri[u];
                if (U.NN.Sign == 0 || IsZero(Cross(U.N, T.N))) continue;
                if (Dot(U.N, Sub(a, U.P[0])).Sign != 0 || Dot(U.N, Sub(b, U.P[0])).Sign != 0) continue;
                BigRational[] l = Bary(U, mid);
                if (l[0].Sign < 0 || l[1].Sign < 0 || l[2].Sign < 0) continue;
                // the segment lies in two triangles (an edge of the other mesh on T's plane, a contact): no seam moves
                // it to first order on both sides; the prototype takes T's own velocity (the separated branch)
                if (found >= 0) return -2;
                found = u;
            }
            return found;
        }

        static void Add(Dictionary<long, Vector3d> d, long key, Vector3d v) { d.TryGetValue(key, out Vector3d x); d[key] = x + v; }

        // rationals
        internal readonly struct Q3 { public readonly BigRational X, Y, Z; public Q3(BigRational x, BigRational y, BigRational z) { X = x; Y = y; Z = z; } }
        static Q3 R(ExactPoint3 p) => new Q3(new BigRational(p.X, p.W), new BigRational(p.Y, p.W), new BigRational(p.Z, p.W));
        static Q3 Add(Q3 a, Q3 b) => new Q3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        static Q3 Sub(Q3 a, Q3 b) => new Q3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        static Q3 Scale(Q3 a, BigRational s) => new Q3(a.X * s, a.Y * s, a.Z * s);
        static BigRational Dot(Q3 a, Q3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Q3 Cross(Q3 a, Q3 b) => new Q3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static bool IsZero(Q3 a) => a.X.Sign == 0 && a.Y.Sign == 0 && a.Z.Sign == 0;
        static Vector3d V(Q3 a) => new Vector3d(D(a.X), D(a.Y), D(a.Z));
        static Vector3d Unit(Vector3d v) => v / v.Length;
        internal static double D(BigRational q) => ExactPoint3.ToDouble(q.N, q.D, 0);
        internal static BigRational Pow2(int k) => k >= 0 ? new BigRational(BigInteger.One << k, BigInteger.One) : new BigRational(BigInteger.One, BigInteger.One << -k);
    }
}
