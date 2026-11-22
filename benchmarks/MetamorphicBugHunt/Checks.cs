using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Independent checks of a result: they use the DMesh3 container (vertices, triangles) and nothing of the operations under
    /// test. Every check returns "" when it holds, else the reason.
    /// </summary>
    public static class Checks
    {
        /// <summary> finite, every edge shared by exactly two triangles with opposite directions, every vertex one fan </summary>
        public static string ClosedManifold(DMesh3 m)
        {
            if (m == null) return "null";
            if (m.TriangleCount == 0) return "empty";
            foreach (int v in m.VertexIndices()) if (!m.GetVertex(v).IsFinite) return "non-finite vertex " + v;
            var dir = new Dictionary<(int, int), int>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                if (tv.a == tv.b || tv.b == tv.c || tv.a == tv.c) return "degenerate index triangle " + t;
                for (int k = 0; k < 3; ++k) {
                    var e = (tv[k], tv[(k + 1) % 3]);
                    dir.TryGetValue(e, out int c);
                    dir[e] = c + 1;
                }
            }
            foreach (var kv in dir) {
                if (kv.Value != 1) return $"directed edge {kv.Key} used {kv.Value} times";
                if (!dir.ContainsKey((kv.Key.Item2, kv.Key.Item1))) return $"open edge {kv.Key}";
            }
            // one fan per vertex: the triangles at a vertex are connected through its edges
            var at = new Dictionary<int, List<Index3i>>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    if (!at.TryGetValue(tv[k], out var l)) at[tv[k]] = l = new List<Index3i>();
                    l.Add(new Index3i(tv[k], tv[(k + 1) % 3], tv[(k + 2) % 3]));
                }
            }
            foreach (var kv in at) {
                var next = new Dictionary<int, int>();
                foreach (Index3i t in kv.Value) next[t.b] = t.c;
                int start = kv.Value[0].b, cur = start, n = 0;
                do { cur = next[cur]; ++n; } while (cur != start && n <= kv.Value.Count);
                if (n != kv.Value.Count) return $"bowtie vertex {kv.Key}";
            }
            return "";
        }

        public static double Volume(DMesh3 m)
        {
            // relative to the first vertex: translation does not cancel the result
            Vector3d o = Vector3d.Zero;
            foreach (int v in m.VertexIndices()) { o = m.GetVertex(v); break; }
            double s = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a) - o, b = m.GetVertex(tv.b) - o, c = m.GetVertex(tv.c) - o;
                s += a.Dot(b.Cross(c));
            }
            return s / 6;
        }

        public static double Area(DMesh3 m)
        {
            double s = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                s += (m.GetVertex(tv.b) - m.GetVertex(tv.a)).Cross(m.GetVertex(tv.c) - m.GetVertex(tv.a)).Length;
            }
            return s / 2;
        }

        public static int Euler(DMesh3 m)
        {
            var edges = new HashSet<(int, int)>();
            var verts = new HashSet<int>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) { int a = tv[k], b = tv[(k + 1) % 3]; edges.Add(a < b ? (a, b) : (b, a)); verts.Add(a); }
            }
            return verts.Count - edges.Count + m.TriangleCount;
        }

        /// <summary> the winding number of the mesh about p: the sum of the signed solid angles (Van Oosterom and Strackee 1983) </summary>
        public static double Winding(DMesh3 m, Vector3d p)
        {
            double s = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a) - p, b = m.GetVertex(tv.b) - p, c = m.GetVertex(tv.c) - p;
                double la = a.Length, lb = b.Length, lc = c.Length;
                double num = a.Dot(b.Cross(c));
                double den = la * lb * lc + a.Dot(b) * lc + b.Dot(c) * la + c.Dot(a) * lb;
                s += 2 * Math.Atan2(num, den);
            }
            return s / (4 * Math.PI);
        }

        public static double DistanceTo(DMesh3 m, Vector3d p)
        {
            double best = double.MaxValue;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                var tri = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
                var d = new DistPoint3Triangle3(p, tri);
                best = Math.Min(best, d.GetSquared());
            }
            return Math.Sqrt(best);
        }

        public static AxisAlignedBox3d Bounds(params DMesh3[] meshes)
        {
            var b = AxisAlignedBox3d.Empty;
            foreach (DMesh3 m in meshes) foreach (int v in m.VertexIndices()) b.Contain(m.GetVertex(v));
            return b;
        }

        /// <summary>
        /// Winding-number sampling: at random points of the inputs' bounds, the result is inside exactly where the predicate of the
        /// inputs' inside states says. Points closer than margin (relative to the bounds' diagonal) to an input surface are skipped.
        /// </summary>
        public static string SameSolid(DMesh3 result, DMesh3[] inputs, Func<bool[], bool> inside, int samples, int seed, double margin = 1e-4, DMesh3[] toleranced = null)
        {
            AxisAlignedBox3d box = Bounds(inputs);
            double diag = box.Diagonal.Length;
            box.Expand(0.05 * diag);
            var rng = new Random(seed);
            int bad = 0; string first = "";
            for (int i = 0; i < samples; ++i) {
                var p = new Vector3d(box.Min.x + rng.NextDouble() * box.Width, box.Min.y + rng.NextDouble() * box.Height, box.Min.z + rng.NextDouble() * box.Depth);
                var states = new bool[inputs.Length];
                for (int k = 0; k < inputs.Length; ++k) states[k] = Winding(inputs[k], p) > 0.5;
                bool want = inside(states);
                double w = (result == null || result.TriangleCount == 0) ? 0 : Winding(result, p);
                bool got = w > 0.5;
                bool integral = Math.Abs(w - Math.Round(w)) < 1e-6;
                if (want == got && integral && Math.Round(w) <= 1 && Math.Round(w) >= 0) continue;
                bool near = false;
                foreach (DMesh3 m in inputs) if (DistanceTo(m, p) < margin * diag) near = true;
                if (toleranced != null) foreach (DMesh3 m in toleranced) if (DistanceTo(m, p) < margin * diag) near = true;
                if (near) continue;
                if (bad++ == 0) first = $"at ({p.x:R}, {p.y:R}, {p.z:R}): winding {w:R}, expected {(want ? 1 : 0)}";
            }
            return bad == 0 ? "" : $"{bad} of {samples} samples wrong, first {first}";
        }

        /// <summary> closed and manifold by the independent check, and clean by the library's own check </summary>
        public static string Solid(DMesh3 m, bool selfIntersections = true)
        {
            string r = ClosedManifold(m);
            if (r != "") return r;
            return MeshRobustRepair.NotCleanReason(m, selfIntersections);
        }

        /// <summary> brute-force count of intersecting triangle pairs that share no vertex (small meshes only) </summary>
        public static int CrossingPairs(DMesh3 m, int maxTriangles = 4000)
        {
            if (m.TriangleCount > maxTriangles) return -1;
            var tris = m.TriangleIndices().ToArray();
            var boxes = new AxisAlignedBox3d[tris.Length];
            var T = new Triangle3d[tris.Length];
            var I = new Index3i[tris.Length];
            for (int i = 0; i < tris.Length; ++i) {
                I[i] = m.GetTriangle(tris[i]);
                T[i] = new Triangle3d(m.GetVertex(I[i].a), m.GetVertex(I[i].b), m.GetVertex(I[i].c));
                boxes[i] = AxisAlignedBox3d.Empty; boxes[i].Contain(T[i].V0); boxes[i].Contain(T[i].V1); boxes[i].Contain(T[i].V2);
            }
            int n = 0;
            for (int i = 0; i < tris.Length; ++i)
                for (int j = i + 1; j < tris.Length; ++j) {
                    if (!boxes[i].Intersects(boxes[j])) continue;
                    bool share = false;
                    for (int a = 0; a < 3; ++a) for (int b = 0; b < 3; ++b) if (I[i][a] == I[j][b]) share = true;
                    if (share) continue;
                    if (ProperlyCross(T[i], T[j])) ++n;
                }
            return n;
        }

        // an edge of one triangle passes strictly through the interior of the other (orientation signs, double precision with a
        // relative margin: a count of clear crossings, not an exact predicate)
        static bool ProperlyCross(Triangle3d s, Triangle3d t) => EdgeThrough(s, t) || EdgeThrough(t, s);

        static bool EdgeThrough(Triangle3d s, Triangle3d t)
        {
            Vector3d[] sv = { s.V0, s.V1, s.V2 };
            for (int k = 0; k < 3; ++k) {
                Vector3d p = sv[k], q = sv[(k + 1) % 3];
                double scale = Math.Max((t.V1 - t.V0).Length, (q - p).Length);
                double eps = 1e-9 * scale * scale * scale;
                double dp = Orient(t.V0, t.V1, t.V2, p), dq = Orient(t.V0, t.V1, t.V2, q);
                if (!((dp > eps && dq < -eps) || (dp < -eps && dq > eps))) continue;
                double a = Orient(p, q, t.V0, t.V1), b = Orient(p, q, t.V1, t.V2), c = Orient(p, q, t.V2, t.V0);
                if ((a > eps && b > eps && c > eps) || (a < -eps && b < -eps && c < -eps)) return true;
            }
            return false;
        }

        static double Orient(Vector3d a, Vector3d b, Vector3d c, Vector3d d) => (b - a).Cross(c - a).Dot(d - a);
    }
}
