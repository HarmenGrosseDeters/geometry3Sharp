using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Checks of a result that share no code with the operations under test: they read the vertices and triangles of the DMesh3
    /// container and nothing else (docs/research/METAMORPHIC_BUG_HUNT_2026-11.md, section 2; the same checks as
    /// benchmarks/MetamorphicBugHunt/Checks.cs). Each returns "" when it holds, else the reason. Use them next to
    /// MeshRobustRepair.NotCleanReason (the library's own word on a mesh), not in its place.
    /// </summary>
    public static class IndependentChecks
    {
        /// <summary> finite, every directed edge once with its reverse present, one fan of triangles per vertex </summary>
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

        /// <summary> the signed volume, relative to the first vertex (a translation does not cancel digits) </summary>
        public static double Volume(DMesh3 m)
        {
            Vector3d o = Vector3d.Zero;
            foreach (int v in m.VertexIndices()) { o = m.GetVertex(v); break; }
            double s = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                s += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o));
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

        /// <summary> vertices - edges + triangles of the used vertices </summary>
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
                s += 2 * Math.Atan2(a.Dot(b.Cross(c)), la * lb * lc + a.Dot(b) * lc + b.Dot(c) * la + c.Dot(a) * lb);
            }
            return s / (4 * Math.PI);
        }

        /// <summary> the brute-force distance of p from the triangles of the mesh </summary>
        public static double Distance(DMesh3 m, Vector3d p)
        {
            double best = double.MaxValue;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                best = Math.Min(best, new DistPoint3Triangle3(p, new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c))).GetSquared());
            }
            return Math.Sqrt(best);
        }

        /// <summary> every vertex of the input has the winding number 1 in the closed mesh around it </summary>
        public static string Encloses(DMesh3 outer, DMesh3 input)
        {
            foreach (int v in input.VertexIndices()) {
                double w = Winding(outer, input.GetVertex(v));
                if (!(w > 0.5)) return $"input vertex {v} has the winding number {w:R} in the result";
            }
            return "";
        }

        /// <summary> the same vertex and triangle ids, the coordinates of x times scale equal to those of y (compared with ==: "bit for bit" up to the sign of a zero) </summary>
        public static string SameBits(DMesh3 x, DMesh3 y, double scale = 1)
        {
            if (x == null || y == null) return x == y ? "" : "one mesh is null";
            if (x.VertexCount != y.VertexCount || x.TriangleCount != y.TriangleCount) return $"{x.VertexCount} vertices / {x.TriangleCount} triangles vs {y.VertexCount} / {y.TriangleCount}";
            if (x.MaxVertexID != y.MaxVertexID || x.MaxTriangleID != y.MaxTriangleID) return "id spaces differ";
            foreach (int v in x.VertexIndices()) {
                if (!y.IsVertex(v)) return "vertex id " + v;
                Vector3d p = x.GetVertex(v) * scale, q = y.GetVertex(v);
                if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {v}: {p.x:R} {p.y:R} {p.z:R} vs {q.x:R} {q.y:R} {q.z:R}";
            }
            foreach (int t in x.TriangleIndices()) {
                if (!y.IsTriangle(t)) return "triangle id " + t;
                Index3i a = x.GetTriangle(t), b = y.GetTriangle(t);
                if (a.a != b.a || a.b != b.b || a.c != b.c) return "triangle " + t;
            }
            return "";
        }

        /// <summary> the same solid with unused vertex ids and a removed triangle id: ids are not dense; map gives old to new vertex ids </summary>
        public static DMesh3 WithIdGaps(DMesh3 m, out Dictionary<int, int> map)
        {
            var g = new DMesh3();
            map = new Dictionary<int, int>();
            var junk = new List<int>();
            int k = 0;
            foreach (int v in m.VertexIndices()) {
                if (k++ % 3 == 0) junk.Add(g.AppendVertex(new Vector3d(1e3 + k, -7, 5)));
                map[v] = g.AppendVertex(m.GetVertex(v));
            }
            int e0 = g.AppendVertex(new Vector3d(50, 0, 0)), e1 = g.AppendVertex(new Vector3d(51, 0, 0)), e2 = g.AppendVertex(new Vector3d(50, 1, 0));
            int first = g.AppendTriangle(e0, e1, e2);
            foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); g.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            g.RemoveTriangle(first, true, false);
            foreach (int v in junk) g.RemoveVertex(v, true, false);
            return g;
        }
    }
}
