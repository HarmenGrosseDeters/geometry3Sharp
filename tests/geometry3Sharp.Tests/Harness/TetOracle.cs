using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Independent validity oracle for tetrahedral meshes (docs/design/TET_MESHING.md section 7): shares no code with
    /// Delaunay3, ExactPredicates or TetMeshQuality. Doubles become integers (mantissa shifted to a common exponent),
    /// orientation and volume are BigInteger determinants, faces are identified by their coordinates, not by indices.
    /// </summary>
    public static class TetOracle
    {
        static BigInteger ToInt(double x, int minExp)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            int exp = e - 1075;                       // x = m * 2^exp
            BigInteger v = (BigInteger)m << (exp - minExp);
            return bits < 0 ? -v : v;
        }

        static int MinExp(IEnumerable<Vector3d> pts)
        {
            int min = int.MaxValue;
            foreach (var p in pts)
                foreach (double x in new[] { p.x, p.y, p.z }) {
                    if (x == 0) continue;
                    long bits = BitConverter.DoubleToInt64Bits(x);
                    int e = (int)((bits >> 52) & 0x7FF);
                    if (e == 0) e = 1;
                    // lowest set bit of the mantissa
                    long m = bits & 0xFFFFFFFFFFFFFL; if (((bits >> 52) & 0x7FF) != 0) m |= 1L << 52;
                    int tz = 0; while ((m & 1) == 0) { m >>= 1; tz++; }
                    min = Math.Min(min, e - 1075 + tz);
                }
            return min == int.MaxValue ? 0 : min;
        }

        static BigInteger Det(BigInteger[] a, BigInteger[] b, BigInteger[] c)
        {
            return a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0]);
        }

        static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

        static (double, double, double) Key(Vector3d p) => (p.x, p.y, p.z);

        // a directed triangle by coordinates, rotated so the smallest vertex is first
        static ((double, double, double), (double, double, double), (double, double, double)) Tri(Vector3d a, Vector3d b, Vector3d c)
        {
            var k = new[] { Key(a), Key(b), Key(c) };
            int s = 0;
            for (int i = 1; i < 3; ++i) if (Comparer<(double, double, double)>.Default.Compare(k[i], k[s]) < 0) s = i;
            return (k[s], k[(s + 1) % 3], k[(s + 2) % 3]);
        }

        /// <summary>
        /// Null if valid, else the violation: distinct vertices, every tet exactly positive, every directed facet by
        /// coordinates either has its reverse (interior) or is a boundary triangle, the boundary triangles equal to
        /// the triangles of surface (if not null) as directed coordinate triples, and the exact identity
        /// sum(6 V of the tets) = sum(det of the boundary triangles with the origin) (divergence theorem, exact).
        /// </summary>
        public static string Check(TetMesh3 m, DMesh3 surface)
        {
            if (m.Vertices.Distinct().Count() != m.Vertices.Count) return "two vertices coincide";
            int minExp = MinExp(m.Vertices);
            var P = m.Vertices.Select(v => new[] { ToInt(v.x, minExp), ToInt(v.y, minExp), ToInt(v.z, minExp) }).ToList();
            BigInteger six = 0;
            var directed = new Dictionary<((double, double, double), (double, double, double), (double, double, double)), int>();
            foreach (Index4i t in m.Tets) {
                BigInteger d = Det(Sub(P[t.b], P[t.a]), Sub(P[t.c], P[t.a]), Sub(P[t.d], P[t.a]));
                if (d.Sign <= 0) return "a tet is not exactly positive";
                six += d;
                int[,] F = { { 1, 2, 3 }, { 0, 3, 2 }, { 0, 1, 3 }, { 0, 2, 1 } };
                for (int i = 0; i < 4; ++i) {
                    var key = Tri(m.Vertices[t[F[i, 0]]], m.Vertices[t[F[i, 1]]], m.Vertices[t[F[i, 2]]]);
                    if (directed.ContainsKey(key)) return "a directed facet occurs twice";
                    directed[key] = 1;
                }
            }
            var bnd = new HashSet<((double, double, double), (double, double, double), (double, double, double))>();
            foreach (var k in directed.Keys) {
                var rev = Tri(ToV(k.Item1), ToV(k.Item3), ToV(k.Item2));
                if (!directed.ContainsKey(rev)) bnd.Add(k);
            }
            var given = new HashSet<((double, double, double), (double, double, double), (double, double, double))>();
            BigInteger surf = 0;
            foreach (Index3i t in m.BoundaryTriangles) {
                given.Add(Tri(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c]));
                surf += Det(P[t.a], P[t.b], P[t.c]);
            }
            if (!bnd.SetEquals(given) || given.Count != m.BoundaryTriangles.Count) return "boundary triangles differ from the facets without a partner";
            if (six != surf) return "volume identity violated";      // implied by the matching above (it telescopes): a check of this oracle itself
            if (surface != null) {
                var wrap = new HashSet<((double, double, double), (double, double, double), (double, double, double))>();
                foreach (int tid in surface.TriangleIndices()) {
                    Index3i t = surface.GetTriangle(tid);
                    wrap.Add(Tri(surface.GetVertex(t.a), surface.GetVertex(t.b), surface.GetVertex(t.c)));
                }
                if (!wrap.SetEquals(given)) return "boundary differs from the wrap surface";
            }
            return null;
        }

        static Vector3d ToV((double, double, double) k) => new Vector3d(k.Item1, k.Item2, k.Item3);
    }
}
