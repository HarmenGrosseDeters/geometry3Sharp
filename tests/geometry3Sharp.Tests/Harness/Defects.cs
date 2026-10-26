using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Procedural defect meshes (docs/QA.md 2.1 and 2.5): the awkward inputs of the review checklist, and seeded random
    /// edits of clean meshes. Each case is small (a few hundred triangles), so that every operation can run over all of
    /// them in the normal suite.
    /// </summary>
    public static class Defects
    {
        /// <summary> a triangle soup: positions and index triples, before it becomes a DMesh3 </summary>
        public sealed class Soup
        {
            public List<Vector3d> P = new List<Vector3d>();
            public List<Index3i> T = new List<Index3i>();

            public static Soup From(DMesh3 mesh)
            {
                var s = new Soup();
                var map = new Dictionary<int, int>();
                foreach (int v in mesh.VertexIndices()) { map[v] = s.P.Count; s.P.Add(mesh.GetVertex(v)); }
                foreach (int t in mesh.TriangleIndices()) {
                    Index3i tv = mesh.GetTriangle(t);
                    s.T.Add(new Index3i(map[tv.a], map[tv.b], map[tv.c]));
                }
                return s;
            }

            public int AddVertex(Vector3d p) { P.Add(p); return P.Count - 1; }

            /// <summary> appends another soup (its indices shifted) </summary>
            public Soup Append(Soup other)
            {
                int n = P.Count;
                P.AddRange(other.P);
                foreach (Index3i t in other.T) T.Add(new Index3i(t.a + n, t.b + n, t.c + n));
                return this;
            }

            /// <summary>
            /// The DMesh3 of the soup. A triangle that DMesh3 rejects as non-manifold (a third triangle on an edge, or a
            /// duplicate) gets its own copies of its vertices, as the readers do, so every triangle of the soup is kept,
            /// but only geometrically on that edge (DMesh3 cannot hold a non-manifold edge); triangles with a repeated
            /// index are dropped (DMesh3 cannot hold them). Soup vertices left without a triangle are removed (a case
            /// that wants an unused vertex adds it to the mesh).
            /// </summary>
            public DMesh3 ToMesh()
            {
                var m = new DMesh3();
                foreach (Vector3d p in P) m.AppendVertex(p);
                foreach (Index3i t in T) {
                    if (t.a == t.b || t.b == t.c || t.a == t.c) continue;
                    if (m.AppendTriangle(t) < 0) {
                        int a = m.AppendVertex(P[t.a]), b = m.AppendVertex(P[t.b]), c = m.AppendVertex(P[t.c]);
                        m.AppendTriangle(new Index3i(a, b, c));
                    }
                }
                for (int v = 0; v < P.Count; ++v)
                    if (m.GetVtxEdgeCount(v) == 0) m.RemoveVertex(v);
                return m;
            }
        }

        public static DMesh3 Sphere(Vector3d c, double r, int n = 6)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static Soup SphereSoup(Vector3d c, double r, int n = 6) => Soup.From(Sphere(c, r, n));

        static Soup BoxSoup(Vector3d min, Vector3d max)
        {
            var s = new Soup();
            for (int i = 0; i < 8; ++i)
                s.AddVertex(new Vector3d((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            // outward orientation
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                s.T.Add(new Index3i(q[0], q[1], q[2]));
                s.T.Add(new Index3i(q[0], q[2], q[3]));
            }
            return s;
        }

        /// <summary> a named defect case </summary>
        public sealed class Case
        {
            public string Name;
            /// <summary> builds a fresh mesh on every call (operations may modify their input) </summary>
            public Func<DMesh3> Make;
            public override string ToString() => Name;
        }

        static Case C(string name, Func<DMesh3> make) => new Case() { Name = name, Make = make };

        /// <summary> the fixed cases: one per defect class of the checklist, and a clean sphere as the control </summary>
        public static IEnumerable<Case> Fixed()
        {
            yield return C("clean sphere", () => Sphere(Vector3d.Zero, 1));
            yield return C("empty", () => new DMesh3());
            yield return C("single triangle", () => {
                var s = new Soup(); s.AddVertex(Vector3d.Zero); s.AddVertex(Vector3d.AxisX); s.AddVertex(Vector3d.AxisY);
                s.T.Add(new Index3i(0, 1, 2)); return s.ToMesh();
            });
            yield return C("zero-area ears", () => {
                // an edge split whose new vertex sits on an end of the edge: two needle triangles of zero area
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                int e = m.EdgeIndices().First();
                Index2i ev = m.GetEdgeV(e);
                DMesh3.EdgeSplitInfo info;
                m.SplitEdge(e, out info);
                m.SetVertex(info.vNew, m.GetVertex(ev.a));
                return m;
            });
            yield return C("sliver cap", () => {
                // a poked triangle whose new vertex lies on one of its edges: a cap of zero area
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                int t = m.TriangleIndices().First();
                Index3i tv = m.GetTriangle(t);
                DMesh3.PokeTriangleInfo info;
                m.PokeTriangle(t, out info);
                m.SetVertex(info.new_vid, 0.5 * (m.GetVertex(tv.a) + m.GetVertex(tv.b)));
                return m;
            });
            yield return C("T-junction", () => {
                // one side of an edge split at its midpoint, the other side not: two boundary edges along one edge
                Soup s = SphereSoup(Vector3d.Zero, 1);
                Index3i t = s.T[0];
                int mid = s.AddVertex(0.5 * (s.P[t.a] + s.P[t.b]));
                s.T[0] = new Index3i(t.a, mid, t.c);
                s.T.Add(new Index3i(mid, t.b, t.c));
                return s.ToMesh();
            });
            yield return C("triangle soup", () => {
                // every triangle with its own vertices: coincident vertices everywhere
                Soup s = SphereSoup(Vector3d.Zero, 1), r = new Soup();
                foreach (Index3i t in s.T) {
                    int a = r.AddVertex(s.P[t.a]), b = r.AddVertex(s.P[t.b]), c = r.AddVertex(s.P[t.c]);
                    r.T.Add(new Index3i(a, b, c));
                }
                return r.ToMesh();
            });
            yield return C("interior bowtie", () => {
                // two spheres that share one vertex, at their poles (an odd EdgeVertices puts a vertex at the centre of
                // each cube face: a unique extreme vertex along x)
                Soup a = SphereSoup(Vector3d.Zero, 1, 5);
                int va = Enumerable.Range(0, a.P.Count).OrderByDescending(i => a.P[i].x).First();
                Soup b = SphereSoup(Vector3d.Zero, 1, 5);
                int vb = Enumerable.Range(0, b.P.Count).OrderBy(i => b.P[i].x).First();
                Vector3d shift = a.P[va] - b.P[vb];
                for (int i = 0; i < b.P.Count; ++i) b.P[i] += shift;
                int n = a.P.Count;
                a.Append(b);
                for (int i = 0; i < b.T.Count; ++i) {
                    Index3i t = a.T[a.T.Count - b.T.Count + i];
                    int fix(int v) => (v == vb + n) ? va : v;
                    a.T[a.T.Count - b.T.Count + i] = new Index3i(fix(t.a), fix(t.b), fix(t.c));
                }
                return a.ToMesh();
            });
            yield return C("touching boxes", () => BoxSoup(Vector3d.Zero, Vector3d.One).Append(BoxSoup(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1))).ToMesh());
            yield return C("overlapping spheres", () => SphereSoup(Vector3d.Zero, 1).Append(SphereSoup(new Vector3d(0.8, 0.3, 0.1), 0.9)).ToMesh());
            yield return C("nested and tiny", () => SphereSoup(Vector3d.Zero, 1).Append(SphereSoup(new Vector3d(0.1, 0, 0), 0.4))
                .Append(SphereSoup(new Vector3d(3, 0, 0), 1e-4, 3)).ToMesh());
            yield return C("unused far vertex", () => {
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                m.AppendVertex(new Vector3d(1e6, -2e6, 5e5));
                return m;
            });
            yield return C("open", () => {
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                foreach (int t in m.TriangleIndices().Where(t => m.GetTriCentroid(t).z > 0.8).ToList())
                    m.RemoveTriangle(t);
                return m;
            });
            yield return C("inside-out", () => { DMesh3 m = Sphere(Vector3d.Zero, 1); m.ReverseOrientation(); return m; });
            yield return C("partly flipped", () => {
                Soup s = SphereSoup(Vector3d.Zero, 1);
                for (int i = 0; i < s.T.Count; ++i)
                    if (s.P[s.T[i].a].x > 0.5) s.T[i] = new Index3i(s.T[i].b, s.T[i].a, s.T[i].c);
                return s.ToMesh();
            });
            // (the duplicates get their own vertices: coincident triangles, as the readers produce them)
            yield return C("duplicated triangles", () => {
                Soup s = SphereSoup(Vector3d.Zero, 1);
                int n = s.T.Count;
                for (int i = 0; i < n; i += 7) s.T.Add(s.T[i]);
                return s.ToMesh();
            });
            yield return C("non-manifold fin", () => {
                // a third triangle on an edge of a sphere, pointing outwards (with its own vertices: DMesh3 cannot hold a
                // non-manifold edge, so the fin touches the edge geometrically, as the readers produce it)
                Soup s = SphereSoup(Vector3d.Zero, 1);
                Index3i t = s.T[0];
                int tip = s.AddVertex(2 * (s.P[t.a] + s.P[t.b]));
                s.T.Add(new Index3i(t.a, t.b, tip));
                return s.ToMesh();
            });
            yield return C("flat", () => {
                // a closed mesh of zero volume: a sphere flattened onto z = 0
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                foreach (int v in m.VertexIndices().ToList()) { Vector3d p = m.GetVertex(v); m.SetVertex(v, new Vector3d(p.x, p.y, 0)); }
                return m;
            });
            yield return C("thin slab", () => {
                DMesh3 m = Sphere(Vector3d.Zero, 1);
                MeshTransforms.Scale(m, new Vector3d(1, 1, 1e-6), Vector3d.Zero);
                return m;
            });
            yield return C("open sheet", () => {
                Soup s = new Soup();
                int n = 8;
                for (int j = 0; j <= n; ++j)
                    for (int i = 0; i <= n; ++i) s.AddVertex(new Vector3d(i, j, 0.1 * Math.Sin(i + j)));
                for (int j = 0; j < n; ++j)
                    for (int i = 0; i < n; ++i) {
                        int a = j * (n + 1) + i;
                        s.T.Add(new Index3i(a, a + 1, a + n + 2));
                        s.T.Add(new Index3i(a, a + n + 2, a + n + 1));
                    }
                return s.ToMesh();
            });
        }

        /// <summary>
        /// Seeded random edits of a clean sphere (docs/QA.md 2.5): triangles flipped, removed or duplicated, a vertex moved
        /// onto a neighbour (a zero-length edge) or by a few ulps, a stray triangle across the inside.
        /// </summary>
        public static Case RandomEdits(int seed)
        {
            return C($"random edits {seed}", () => {
                var rnd = new Random(seed);
                Soup s = SphereSoup(Vector3d.Zero, 1);
                int edits = 1 + rnd.Next(12);
                for (int k = 0; k < edits; ++k) {
                    int i = rnd.Next(s.T.Count);
                    Index3i t = s.T[i];
                    switch (rnd.Next(6)) {
                        case 0: s.T[i] = new Index3i(t.b, t.a, t.c); break;
                        case 1: s.T.RemoveAt(i); break;
                        case 2: s.T.Add(t); break;
                        case 3: s.P[t.a] = s.P[t.b]; break;           // a zero-length edge
                        case 4: {
                            Vector3d p = s.P[t.c];
                            s.P[t.c] = new Vector3d(Nudge(p.x, rnd), Nudge(p.y, rnd), Nudge(p.z, rnd));
                            break;
                        }
                        default: {
                            Index3i u = s.T[rnd.Next(s.T.Count)];
                            s.T.Add(new Index3i(t.a, u.b, t.c));    // a triangle through the inside
                            break;
                        }
                    }
                }
                return s.ToMesh();
            });
        }

        // a few ulps (0 stays 0: its bit pattern minus one is a NaN)
        static double Nudge(double x, Random rnd) =>
            (x == 0) ? x : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + rnd.Next(-4, 5));

        public static IEnumerable<Case> All(int randomCases = 12) =>
            Fixed().Concat(Enumerable.Range(1, randomCases).Select(RandomEdits));

        public static IEnumerable<object[]> Names() => All().Select(c => new object[] { c.Name });

        public static Case ByName(string name) => All().First(c => c.Name == name);
    }
}
