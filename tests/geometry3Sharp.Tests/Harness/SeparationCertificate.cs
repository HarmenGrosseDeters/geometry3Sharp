using System;
using System.Collections.Generic;
using System.Numerics;
using g3;

namespace geometry3Sharp.Tests
{

    // The d-separation certificate of Milenkovic and Sacks (CAD 2019, section 1), checked exactly on a mesh with double
    // coordinates: every pair of disjoint features (a vertex and a triangle, two edges, sharing no vertex; vertices at one
    // position count as one, the contacts of SharedPositionsTouch) is farther apart than d = 2e, e = √3/2 ulp(M), the most
    // rounding a further time moves a vertex (M the largest coordinate). Then rounding the vertices once more (eg a
    // conversion or a transformation followed by rounding) cannot make triangles intersect. Within a triangle (a skinny
    // triangle) nothing is required, as in the paper: that is the degenerate check's part.
    // Each pair's squared distance is compared with d² exactly (dyadic arithmetic on the doubles: point-segment,
    // point-triangle by the projection into the plane or the edges, segment-segment by the nearest points of the lines when
    // both lie strictly inside, else the endpoints); a pair not farther apart than d counts as uncertified.
    public sealed class SeparationCertificate
    {
        public int Pairs, Uncertified;
        public bool Capped;
        public static Action<string> Log;
        public double D;

        public static SeparationCertificate Check(DMesh3 mesh, int maxPairs = 2000000)
        {
            var c = new SeparationCertificate();
            if (mesh.TriangleCount == 0) return c;
            AxisAlignedBox3d bounds = gs.MeshRepairMetrics.TriangleBounds(mesh);
            double ulp = Ulp(Math.Max(bounds.Min.MaxAbs, bounds.Max.MaxAbs));
            c.D = Math.Sqrt(3) * ulp;
            // vertices at one position count as one
            var canon = new Dictionary<Vector3d, int>();
            var id = new int[mesh.MaxVertexID];
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                p = new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);
                if (canon.TryGetValue(p, out int k) == false) canon[p] = k = v;
                id[v] = k;
            }
            var tids = new List<int>(mesh.TriangleIndices());
            var boxes = new List<AxisAlignedBox3d>();
            // (boxes widened by d plus an ulp of rounding: a pair whose boxes do not meet is farther apart than d)
            double pad = c.D + 2 * ulp;
            foreach (int t in tids) {
                AxisAlignedBox3d b = mesh.GetTriBounds(t);
                b.Expand(pad / 2);
                boxes.Add(b);
            }
            var tree = new TriangleBoxTree(boxes);
            var seen = new HashSet<(int, int, int, int, int, int)>();
            // (d² = 3 ulp², exact: the double D = √3 ulp is rounded and only pads the boxes, which have 2 ulps of slack)
            CertDyadic d2 = CertDyadic.Of(3) * CertDyadic.Of(ulp) * CertDyadic.Of(ulp);
            for (int i = 0; i < tids.Count && c.Pairs < maxPairs; ++i) {
                Index3i T = mesh.GetTriangle(tids[i]);
                tree.Query(boxes[i], (j) => {
                    if (j <= i) return;
                    Index3i U = mesh.GetTriangle(tids[j]);
                    // vertex-triangle pairs, both ways, and edge-edge pairs
                    for (int a = 0; a < 3; ++a) {
                        c.pair(mesh, id, seen, d2, new[] { T[a] }, new[] { U.a, U.b, U.c });
                        c.pair(mesh, id, seen, d2, new[] { U[a] }, new[] { T.a, T.b, T.c });
                        for (int b = 0; b < 3; ++b)
                            c.pair(mesh, id, seen, d2, new[] { T[a], T[(a + 1) % 3] }, new[] { U[b], U[(b + 1) % 3] });
                    }
                });
            }
            c.Capped = c.Pairs >= maxPairs;
            return c;
        }

        void pair(DMesh3 mesh, int[] id, HashSet<(int, int, int, int, int, int)> seen, CertDyadic d2, int[] f, int[] g)
        {
            foreach (int x in f)
                foreach (int y in g)
                    if (id[x] == id[y]) return;         // (not disjoint)
            int[] fs = canonical(id, f), gs = canonical(id, g);
            var key = (fs[0], fs.Length > 1 ? fs[1] : -1, gs[0], gs[1], gs.Length > 2 ? gs[2] : -1, f.Length);
            if (seen.Add(key) == false) return;
            Pairs++;
            Vector3d[] P = Array.ConvertAll(f, v => mesh.GetVertex(v)), Q = Array.ConvertAll(g, v => mesh.GetVertex(v));
            bool far = P.Length == 1 ? point_triangle_farther(V(P[0]), V(Q[0]), V(Q[1]), V(Q[2]), d2)
                                     : segments_farther(V(P[0]), V(P[1]), V(Q[0]), V(Q[1]), d2);
            if (far == false) {
                Uncertified++;
                if (Log != null) Log($"closer than d: {f.Length}-{g.Length} at {P[0]}");
            }
        }

        // ---- exact squared distances, compared with d² (no rounding anywhere) ----

        /// <summary> a unit in the last place of x (a power of two) </summary>
        public static double Ulp(double x)
        {
            if (!(x > 0) || double.IsInfinity(x)) return double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x) & 0x7FF0000000000000L;
            return Math.Max(BitConverter.Int64BitsToDouble(bits) * 2.220446049250313e-16, double.Epsilon);
        }

        internal static CertVector V(Vector3d p) => new CertVector(CertDyadic.Of(p.x), CertDyadic.Of(p.y), CertDyadic.Of(p.z));

        // |p - [a, b]|² > d²
        internal static bool point_segment_farther(CertVector p, CertVector a, CertVector b, CertDyadic d2)
        {
            CertVector e = b - a, w = p - a;
            CertDyadic t = w.Dot(e), den = e.Dot(e);
            if (t.Sign <= 0 || den.Sign == 0) return (w.Dot(w) - d2).Sign > 0;
            if ((t - den).Sign >= 0) { CertVector wb = p - b; return (wb.Dot(wb) - d2).Sign > 0; }
            // |w|² - t²/den > d²  <=>  |w|² den - t² > d² den
            return (w.Dot(w) * den - t * t - d2 * den).Sign > 0;
        }

        internal static bool point_triangle_farther(CertVector p, CertVector a, CertVector b, CertVector c, CertDyadic d2)
        {
            CertVector n = (b - a).Cross(c - a);
            CertDyadic nn = n.Dot(n);
            if (nn.Sign != 0) {
                // (the projection of p inside the triangle: the distance to its plane)
                int s0 = n.Dot((b - a).Cross(p - a)).Sign, s1 = n.Dot((c - b).Cross(p - b)).Sign, s2 = n.Dot((a - c).Cross(p - c)).Sign;
                if (s0 >= 0 && s1 >= 0 && s2 >= 0) {
                    CertDyadic h = n.Dot(p - a);
                    return (h * h - d2 * nn).Sign > 0;
                }
            }
            return point_segment_farther(p, a, b, d2) && point_segment_farther(p, b, c, d2) && point_segment_farther(p, c, a, d2);
        }

        internal static bool segments_farther(CertVector p0, CertVector p1, CertVector q0, CertVector q1, CertDyadic d2)
        {
            CertVector d1 = p1 - p0, dd = q1 - q0, r = p0 - q0;
            CertDyadic a = d1.Dot(d1), e = dd.Dot(dd), b = d1.Dot(dd), c = d1.Dot(r), f = dd.Dot(r);
            CertDyadic den = a * e - b * b;
            if (den.Sign > 0) {
                // the nearest points of the lines, s = (b f - c e) / den, t = (a f - b c) / den, both strictly inside
                CertDyadic sn = b * f - c * e, tn = a * f - b * c;
                if (sn.Sign > 0 && (sn - den).Sign < 0 && tn.Sign > 0 && (tn - den).Sign < 0) {
                    CertVector n = d1.Cross(dd);
                    CertDyadic h = n.Dot(r);
                    return (h * h - d2 * n.Dot(n)).Sign > 0;
                }
            }
            return point_segment_farther(p0, q0, q1, d2) && point_segment_farther(p1, q0, q1, d2)
                && point_segment_farther(q0, p0, p1, d2) && point_segment_farther(q1, p0, p1, d2);
        }

        static int[] canonical(int[] id, int[] f)
        {
            var r = Array.ConvertAll(f, v => id[v]);
            Array.Sort(r);
            return r;
        }

    }

    internal readonly struct CertVector
    {
        public readonly CertDyadic X, Y, Z;
        public CertVector(CertDyadic x, CertDyadic y, CertDyadic z) { X = x; Y = y; Z = z; }
        public static CertVector operator -(CertVector a, CertVector b) => new CertVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public CertDyadic Dot(CertVector o) => X * o.X + Y * o.Y + Z * o.Z;
        public CertVector Cross(CertVector o) => new CertVector(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    }

    // exact dyadic rationals M 2^E (the doubles and their sums and products)
    internal readonly struct CertDyadic
    {
        public readonly BigInteger M;
        public readonly int E;
        public CertDyadic(BigInteger m, int e) { M = m; E = e; }
        public static CertDyadic Of(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentException("not finite");
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & ((1L << 52) - 1);
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            return new CertDyadic(bits < 0 ? -mant : mant, exp - 1075);
        }
        static BigInteger Align(CertDyadic x, int e) => x.M << (x.E - e);
        public static CertDyadic operator +(CertDyadic x, CertDyadic y) { int e = Math.Min(x.E, y.E); return new CertDyadic(Align(x, e) + Align(y, e), e); }
        public static CertDyadic operator -(CertDyadic x, CertDyadic y) { int e = Math.Min(x.E, y.E); return new CertDyadic(Align(x, e) - Align(y, e), e); }
        public static CertDyadic operator *(CertDyadic x, CertDyadic y) => new CertDyadic(x.M * y.M, x.E + y.E);
        public int Sign => M.Sign;
    }
}
