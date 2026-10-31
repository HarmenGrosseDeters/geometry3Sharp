using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Random edit sequences on a DMesh3, checked after every step against a plain model of the mesh. The model and
    /// its feasibility rules come from the triangle list, by the definition of manifoldness (every edge in at most two
    /// triangles, the triangles around a vertex form one fan); they never ask DMesh3 for adjacency or copy its guards.
    /// Commands pick any live element, so rejected operations are tested too.
    /// </summary>
    public class DMesh3PropertyTests
    {
        readonly ITestOutputHelper output;
        static readonly int[] Seeds = { 7, 29, 101, 503, 1709, 4093, 8191, 65537 };
        static readonly Vector3d Bary = new Vector3d(0.5, 0.25, 0.25);
        const int Steps = 160;
        const int TriangleCap = 256;

        public DMesh3PropertyTests(ITestOutputHelper output) { this.output = output; }

        enum Op { AppendVertex, Disconnected, Attach, Move, Attributes, Split, Flip, Collapse, Poke,
            RemoveTriangle, RemoveIsolated, RemoveVertex, Compact, Rebuild, Restore }

        static readonly Op[] AllOps = (Op[])Enum.GetValues(typeof(Op));
        static readonly Op[] ClosedOps = { Op.Move, Op.Attributes, Op.Split, Op.Flip, Op.Collapse, Op.Poke, Op.Compact, Op.Rebuild, Op.Restore };

        sealed class Command
        {
            public Op Kind;
            public int A = -1, B = -1, Group;
            public bool Flag;
            public Vector3d Position, Position1, Position2;
            public override string ToString() => Kind + " a=" + A + " b=" + B + " group=" + Group + " flag=" + Flag + " p=" +
                Position.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                Position.y.ToString("R", CultureInfo.InvariantCulture) + "," + Position.z.ToString("R", CultureInfo.InvariantCulture);
        }

        struct Vertex
        {
            public Vector3d P;
            public Vector3f N, C;
            public Vector2f UV;
        }

        struct Face
        {
            public Index3i V;
            public int Group;
            public Face(Index3i v, int group) { V = v; Group = group; }
            public string Key => Oriented(V) + " g" + Group;
        }

        sealed class Snapshot
        {
            public readonly Dictionary<int, Vertex> Vertices = new Dictionary<int, Vertex>();
            public readonly Dictionary<int, Face> Faces = new Dictionary<int, Face>();
            public readonly Dictionary<int, Index4i> Edges = new Dictionary<int, Index4i>();
            public readonly bool Normals, Colors, UVs, Groups;
            public Snapshot(DMesh3 mesh)
            {
                Normals = mesh.HasVertexNormals; Colors = mesh.HasVertexColors;
                UVs = mesh.HasVertexUVs; Groups = mesh.HasTriangleGroups;
                foreach (int v in mesh.VertexIndices())
                    Vertices.Add(v, new Vertex { P = mesh.GetVertex(v),
                        N = Normals ? mesh.GetVertexNormal(v) : Vector3f.Zero,
                        C = Colors ? mesh.GetVertexColor(v) : Vector3f.Zero,
                        UV = UVs ? mesh.GetVertexUV(v) : Vector2f.Zero });
                foreach (int t in mesh.TriangleIndices())
                    Faces.Add(t, new Face(mesh.GetTriangle(t), Groups ? mesh.GetTriangleGroup(t) : -1));
                foreach (int e in mesh.EdgeIndices()) Edges.Add(e, mesh.GetEdge(e));
            }
        }

        sealed class State
        {
            public readonly DMesh3 Mesh;
            public readonly bool Closed;
            public readonly List<Command> Trace = new List<Command>();
            public readonly Dictionary<Op, int> Attempts = new Dictionary<Op, int>(), Successes = new Dictionary<Op, int>();
            public State(DMesh3 mesh, bool closed = false) { Mesh = mesh; Closed = closed; }
        }

        static Index2i Pair(int a, int b) => a < b ? new Index2i(a, b) : new Index2i(b, a);
        static bool Has(Index3i t, int v) => t.a == v || t.b == v || t.c == v;
        static IEnumerable<int> Corners(Index3i t) { yield return t.a; yield return t.b; yield return t.c; }
        static IEnumerable<Index2i> Sides(Index3i t) { yield return Pair(t.a, t.b); yield return Pair(t.b, t.c); yield return Pair(t.c, t.a); }
        static string Oriented(Index3i t)
        {
            int j = t.a < t.b && t.a < t.c ? 0 : (t.b < t.c ? 1 : 2);
            return t[j] + "," + t[(j + 1) % 3] + "," + t[(j + 2) % 3];
        }
        static int Other(Index3i t, int a, int b) => Corners(t).Single(v => v != a && v != b);
        static int[] Sorted(IEnumerable<int> ids) => ids.OrderBy(i => i).ToArray();

        static Dictionary<int, List<Index3i>> Stars(IEnumerable<Index3i> triangles)
        {
            var stars = new Dictionary<int, List<Index3i>>();
            foreach (Index3i t in triangles)
                foreach (int v in Corners(t)) {
                    if (!stars.TryGetValue(v, out List<Index3i> star)) stars.Add(v, star = new List<Index3i>());
                    star.Add(t);
                }
            return stars;
        }

        // The triangles around v are connected through their edges at v.
        static bool SingleFan(int v, List<Index3i> star)
        {
            var reached = new HashSet<int> { 0 };
            var todo = new Stack<int>(); todo.Push(0);
            while (todo.Count > 0) {
                Index3i t = star[todo.Pop()];
                for (int i = 0; i < star.Count; ++i)
                    if (!reached.Contains(i) && Corners(t).Any(w => w != v && Has(star[i], w))) { reached.Add(i); todo.Push(i); }
            }
            return reached.Count == star.Count;
        }

        /// <summary>The definition of a manifold triangle list (with boundaries), from the triangles alone.</summary>
        static bool Manifold(List<Index3i> triangles)
        {
            var uses = new Dictionary<Index2i, int>();
            foreach (Index3i t in triangles)
                foreach (Index2i side in Sides(t)) {
                    uses.TryGetValue(side, out int n);
                    if (n == 2) return false;
                    uses[side] = n + 1;
                }
            return Stars(triangles).All(s => SingleFan(s.Key, s.Value));
        }

        /// <summary>
        /// The link condition for a topology-preserving edge collapse (Dey, Edelsbrunner, Guha, Nekhayev 1999): what
        /// the links of a and b have in common is exactly the link of the edge ab, the opposite vertices. A boundary is
        /// handled there by closing the surface with a dummy vertex joined to every boundary edge.
        /// </summary>
        static bool LinkCondition(List<Index3i> triangles, int a, int b)
        {
            const int dummy = -2;
            var uses = new Dictionary<Index2i, int>();
            foreach (Index3i t in triangles)
                foreach (Index2i side in Sides(t)) { uses.TryGetValue(side, out int n); uses[side] = n + 1; }
            List<Index3i> closed = triangles.Concat(uses.Where(u => u.Value == 1).Select(u => new Index3i(u.Key.a, u.Key.b, dummy))).ToList();
            var linkA = new HashSet<int>(); var linkB = new HashSet<int>(); var linkAB = new HashSet<int>();
            var edgesA = new HashSet<Index2i>(); var edgesB = new HashSet<Index2i>();
            foreach (Index3i t in closed) {
                bool hasA = Has(t, a), hasB = Has(t, b);
                if (hasA && hasB) linkAB.Add(Other(t, a, b));
                if (hasA) { int[] o = Corners(t).Where(v => v != a).ToArray(); linkA.UnionWith(o); edgesA.Add(Pair(o[0], o[1])); }
                if (hasB) { int[] o = Corners(t).Where(v => v != b).ToArray(); linkB.UnionWith(o); edgesB.Add(Pair(o[0], o[1])); }
            }
            linkA.IntersectWith(linkB);
            return linkA.SetEquals(linkAB) && !edgesA.Overlaps(edgesB);
        }

        /// <summary>
        /// The mesh's adjacency, bookkeeping and caches agree with its triangle list, and the triangle list is manifold.
        /// </summary>
        public static void CheckTopology(DMesh3 m)
        {
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly), "CheckValidity");
            var s = new Snapshot(m);
            Assert.Equal(s.Vertices.Count, m.VertexCount);
            Assert.Equal(s.Faces.Count, m.TriangleCount);
            Assert.Equal(s.Edges.Count, m.EdgeCount);
            var incidence = new Dictionary<Index2i, List<int>>();
            foreach (var face in s.Faces)
                foreach (Index2i side in Sides(face.Value.V)) {
                    if (!incidence.TryGetValue(side, out List<int> ts)) incidence.Add(side, ts = new List<int>());
                    ts.Add(face.Key);
                }
            Assert.Equal(incidence.Count, m.EdgeCount);
            var neighbours = s.Vertices.Keys.ToDictionary(v => v, v => new List<int>());
            var vertexEdges = s.Vertices.Keys.ToDictionary(v => v, v => new List<int>());
            foreach (var edge in s.Edges) {
                Index4i e = edge.Value;
                Assert.True(incidence.TryGetValue(Pair(e.a, e.b), out List<int> expected), "edge without a triangle");
                Assert.True(expected.Count <= 2, "edge in more than two triangles");
                Assert.Equal(Sorted(expected), Sorted(new[] { e.c, e.d }.Where(t => t >= 0)));
                Assert.True(e.c >= 0, "first edge triangle");
                Assert.Equal(expected.Count == 1, m.IsBoundaryEdge(edge.Key));
                Assert.Equal(edge.Key, m.FindEdge(e.a, e.b));
                Assert.Equal(edge.Key, m.FindEdge(e.b, e.a));
                neighbours[e.a].Add(e.b); neighbours[e.b].Add(e.a);
                vertexEdges[e.a].Add(edge.Key); vertexEdges[e.b].Add(edge.Key);
                if (expected.Count == 2) {
                    // two triangles traverse their shared edge in opposite directions
                    Index3i t0 = s.Faces[expected[0]].V, t1 = s.Faces[expected[1]].V;
                    int d0 = Enumerable.Range(0, 3).Single(j => Pair(t0[j], t0[(j + 1) % 3]) == Pair(e.a, e.b));
                    int d1 = Enumerable.Range(0, 3).Single(j => Pair(t1[j], t1[(j + 1) % 3]) == Pair(e.a, e.b));
                    Assert.Equal(t0[d0], t1[(d1 + 1) % 3]);
                }
            }
            foreach (var face in s.Faces) {
                Index3i t = face.Value.V, te = m.GetTriEdges(face.Key);
                for (int j = 0; j < 3; ++j) {
                    Assert.True(s.Vertices.ContainsKey(t[j]), "triangle on a dead vertex");
                    Index2i ev = m.GetEdgeV(te[j]);
                    Assert.Equal(Pair(t[j], t[(j + 1) % 3]), Pair(ev.a, ev.b));
                }
            }
            var stars = Stars(s.Faces.Values.Select(f => f.V));
            var starIds = s.Vertices.Keys.ToDictionary(v => v, v => new List<int>());
            foreach (var face in s.Faces) foreach (int v in Corners(face.Value.V)) starIds[v].Add(face.Key);
            foreach (var vertex in s.Vertices) {
                int v = vertex.Key;
                Assert.True(vertex.Value.P.IsFinite, "finite position");
                Assert.Equal(Sorted(starIds[v]), Sorted(m.VtxTrianglesItr(v)));
                var oriented = new List<int>();
                Assert.Equal(MeshResult.Ok, m.GetVtxTriangles(v, oriented, true));
                Assert.Equal(Sorted(starIds[v]), Sorted(oriented));
                Assert.Equal(Sorted(vertexEdges[v]), Sorted(m.VtxEdgesItr(v)));
                Assert.Equal(Sorted(neighbours[v]), Sorted(m.VtxVerticesItr(v)));
                Assert.Equal(starIds[v].Count, m.GetVtxTriangleCount(v));
                if (stars.TryGetValue(v, out List<Index3i> star)) Assert.True(SingleFan(v, star), "bowtie vertex " + v);
            }
            bool closed = s.Faces.Count > 0 && incidence.Values.All(ts => ts.Count == 2);
            Assert.Equal(closed, m.IsClosed());
            Assert.Equal(closed, m.CachedIsClosed);
            AxisAlignedBox3d bounds = AxisAlignedBox3d.Empty;
            foreach (var v in s.Vertices.Values) bounds.Contain(v.P);
            Assert.Equal(bounds.Min, m.CachedBounds.Min);
            Assert.Equal(bounds.Max, m.CachedBounds.Max);
        }

        static void CheckClosed(DMesh3 m)
        {
            Assert.True(m.IsClosed(), "a closed mesh stays closed");
            Assert.Equal(2, m.VertexCount - m.EdgeCount + m.TriangleCount);
            var reached = new HashSet<int> { m.VertexIndices().Min() };
            var todo = new Stack<int>(reached);
            while (todo.Count > 0)
                foreach (int next in m.VtxVerticesItr(todo.Pop()))
                    if (reached.Add(next)) todo.Push(next);
            Assert.Equal(m.VertexCount, reached.Count);
        }

        static Vertex Attributes(Vector3d p, int tag)
        {
            int k = 1 + tag % 23;
            return new Vertex { P = p, N = new Vector3f(k, k + 1, k + 2).Normalized,
                C = new Vector3f(k / 32f, (k + 1) / 32f, (k + 2) / 32f), UV = new Vector2f(k / 16f, -k / 32f) };
        }
        static int Add(DMesh3 mesh, Vertex v) => mesh.AppendVertex(new NewVertexInfo { v = v.P, n = v.N, c = v.C, uv = v.UV,
            bHaveN = true, bHaveC = true, bHaveUV = true });
        static int Add(DMesh3 mesh, Vector3d p, int tag) => Add(mesh, Attributes(p, tag));

        static DMesh3 Fixture(string name, bool attributes = true)
        {
            var m = new DMesh3(attributes, attributes, attributes, attributes);
            if (name == "isolated") { Add(m, Vector3d.Zero, 1); Add(m, new Vector3d(2, 3, 4), 2); }
            else if (name == "triangle") m.AppendTriangle(Add(m, Vector3d.Zero, 1), Add(m, Vector3d.AxisX, 2), Add(m, Vector3d.AxisY, 3), 17);
            else if (name == "grid") {
                const int n = 3;
                for (int y = 0; y <= n; ++y)
                    for (int x = 0; x <= n; ++x) Add(m, new Vector3d(x, y, 0), m.MaxVertexID);
                for (int y = 0; y < n; ++y)
                    for (int x = 0; x < n; ++x) {
                        int a = y * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                        m.AppendTriangle(a, b, d, 10 + y * n + x);
                        m.AppendTriangle(a, d, c, 40 + y * n + x);
                    }
            } else if (name == "box") {
                for (int k = 0; k < 8; ++k)
                    Add(m, new Vector3d((k + 1) / 2 % 2, k / 2 % 2, k / 4), k + 1);
                int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
                    new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
                    new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };
                for (int j = 0; j < faces.Length; ++j) m.AppendTriangle(faces[j][0], faces[j][1], faces[j][2], 80 + j);
                Assert.True(m.IsClosed(), "box fixture");
            } else Assert.Equal("empty", name);
            return m;
        }

        static DMesh3 Transformed(DMesh3 source, Similarity similarity)
        {
            var mesh = new DMesh3(source);
            foreach (int v in source.VertexIndices()) mesh.SetVertex(v, similarity.Apply(source.GetVertex(v)));
            return mesh;
        }

        static void SameVertex(Vertex expected, Vertex actual, Snapshot channels, bool derived, double tolerance)
        {
            if (derived) {
                Assert.True(expected.P.Distance(actual.P) <= tolerance, "interpolated position");
                if (channels.Normals) Assert.True((expected.N - actual.N).Length <= 1e-5f, "interpolated normal");
                if (channels.Colors) Assert.True((expected.C - actual.C).Length <= 1e-5f, "interpolated color");
                if (channels.UVs) Assert.True((expected.UV - actual.UV).Length <= 1e-5f, "interpolated UV");
            } else {
                Assert.Equal(expected.P, actual.P);
                if (channels.Normals) Assert.Equal(expected.N, actual.N);
                if (channels.Colors) Assert.Equal(expected.C, actual.C);
                if (channels.UVs) Assert.Equal(expected.UV, actual.UV);
            }
        }

        static void SameSnapshot(Snapshot expected, DMesh3 mesh)
        {
            var actual = new Snapshot(mesh);
            Assert.Equal(expected.Normals, actual.Normals);
            Assert.Equal(expected.Colors, actual.Colors);
            Assert.Equal(expected.UVs, actual.UVs);
            Assert.Equal(expected.Groups, actual.Groups);
            Assert.Equal(Sorted(expected.Vertices.Keys), Sorted(actual.Vertices.Keys));
            Assert.Equal(Sorted(expected.Faces.Keys), Sorted(actual.Faces.Keys));
            Assert.Equal(Sorted(expected.Edges.Keys), Sorted(actual.Edges.Keys));
            foreach (var v in expected.Vertices) SameVertex(v.Value, actual.Vertices[v.Key], expected, false, 0);
            foreach (var t in expected.Faces) Assert.Equal(t.Value.Key, actual.Faces[t.Key].Key);
            foreach (var e in expected.Edges) Assert.Equal(e.Value, actual.Edges[e.Key]);
        }

        static Vertex Weighted(Vertex a, Vertex b, Vertex c, Vector3d w)
        {
            return new Vertex { P = w.x * a.P + w.y * b.P + w.z * c.P,
                N = new Vector3f(w.x * a.N.x + w.y * b.N.x + w.z * c.N.x, w.x * a.N.y + w.y * b.N.y + w.z * c.N.y,
                    w.x * a.N.z + w.y * b.N.z + w.z * c.N.z).Normalized,
                C = new Vector3f(w.x * a.C.x + w.y * b.C.x + w.z * c.C.x, w.x * a.C.y + w.y * b.C.y + w.z * c.C.y,
                    w.x * a.C.z + w.y * b.C.z + w.z * c.C.z),
                UV = new Vector2f(w.x * a.UV.x + w.y * b.UV.x + w.z * c.UV.x, w.x * a.UV.y + w.y * b.UV.y + w.z * c.UV.y) };
        }

        /// <summary>
        /// Runs one command. The expectation (what the mesh must look like afterwards, and whether the operation is
        /// possible) is computed from the triangle list before the call. Which child of a split face keeps the old ID is
        /// not specified, so faces are compared by their oriented vertices and group; faces the operation does not touch
        /// keep their ID.
        /// </summary>
        void Apply(State state, Command c)
        {
            DMesh3 m = state.Mesh;
            state.Trace.Add(c);
            output.WriteLine("step " + (state.Trace.Count - 1) + ": " + c);
            var before = new Snapshot(m);
            var vertices = new Dictionary<int, Vertex>(before.Vertices);
            List<Face> faces = before.Faces.Values.ToList();
            var derived = new HashSet<int>();
            int noGroup(int g) => before.Groups ? g : -1;
            int a = c.A, b = c.B;
            bool ok = true;
            bool? possible = true;      // null: the operation may refuse, see RemoveVertex
            bool keepsIDs = true;
            switch (c.Kind) {
                case Op.AppendVertex: {
                    Vertex value = Attributes(c.Position, c.Group);
                    vertices.Add(Add(m, value), value);
                    break;
                }
                case Op.Disconnected: {
                    Vector3d[] points = { c.Position, c.Position1, c.Position2 };
                    int[] ids = new int[3];
                    for (int j = 0; j < 3; ++j) {
                        Vertex value = Attributes(points[j], c.Group + j);
                        ids[j] = Add(m, value); vertices.Add(ids[j], value);
                    }
                    Assert.True(m.AppendTriangle(ids[0], ids[1], ids[2], c.Group) >= 0, "separate triangle");
                    faces.Add(new Face(new Index3i(ids[0], ids[1], ids[2]), noGroup(c.Group)));
                    break;
                }
                case Op.Attach: {
                    // a is followed by b in a triangle; a new triangle across that edge is possible only on a boundary
                    Vertex value = Attributes(c.Position, c.Group);
                    int v = Add(m, value); vertices.Add(v, value);
                    possible = faces.Count(f => Sides(f.V).Contains(Pair(a, b))) == 1;
                    int t = m.AppendTriangle(b, a, v, c.Group);
                    ok = t >= 0;
                    if (!ok) Assert.Equal(DMesh3.NonManifoldID, t);
                    else faces.Add(new Face(new Index3i(b, a, v), noGroup(c.Group)));
                    break;
                }
                case Op.Move: {
                    m.SetVertex(a, c.Position);
                    Vertex value = vertices[a]; value.P = c.Position; vertices[a] = value;
                    break;
                }
                case Op.Attributes: {
                    Vertex value = Attributes(vertices[a].P, c.Group);
                    if (m.HasVertexNormals) m.SetVertexNormal(a, value.N);
                    if (m.HasVertexColors) m.SetVertexColor(a, value.C);
                    if (m.HasVertexUVs) m.SetVertexUV(a, value.UV);
                    vertices[a] = value;
                    if (b >= 0 && m.HasTriangleGroups) {
                        Face face = before.Faces[b];
                        faces[faces.FindIndex(f => f.Key == face.Key)] = new Face(face.V, c.Group);
                        m.SetTriangleGroup(b, c.Group);
                    }
                    break;
                }
                case Op.Split: {
                    Index4i e = before.Edges[a];
                    ok = m.SplitEdge(a, out DMesh3.EdgeSplitInfo info, 0.5) == MeshResult.Ok;
                    if (!ok) break;
                    vertices.Add(info.vNew, Weighted(vertices[e.a], vertices[e.b], default(Vertex), new Vector3d(0.5, 0.5, 0)));
                    derived.Add(info.vNew);
                    var split = new List<Face>();
                    foreach (Face f in faces) {
                        if (!Has(f.V, e.a) || !Has(f.V, e.b)) { split.Add(f); continue; }
                        int j = Enumerable.Range(0, 3).Single(i => Pair(f.V[i], f.V[(i + 1) % 3]) == Pair(e.a, e.b));
                        int x = f.V[j], y = f.V[(j + 1) % 3], z = f.V[(j + 2) % 3];
                        split.Add(new Face(new Index3i(x, info.vNew, z), f.Group));
                        split.Add(new Face(new Index3i(info.vNew, y, z), f.Group));
                    }
                    faces = split;
                    break;
                }
                case Op.Flip: {
                    Index4i e = before.Edges[a];
                    possible = false;
                    if (e.d >= 0) {
                        Index3i t0 = before.Faces[e.c].V, t1 = before.Faces[e.d].V;
                        int v0 = Other(t0, e.a, e.b), v1 = Other(t1, e.a, e.b);
                        possible = !faces.Any(f => Has(f.V, v0) && Has(f.V, v1));
                    }
                    ok = m.FlipEdge(a, out DMesh3.EdgeFlipInfo _) == MeshResult.Ok;
                    if (ok && possible == true) {
                        Index3i t0 = before.Faces[e.c].V, t1 = before.Faces[e.d].V;
                        int j = Enumerable.Range(0, 3).Single(i => Pair(t0[i], t0[(i + 1) % 3]) == Pair(e.a, e.b));
                        int x = t0[j], y = t0[(j + 1) % 3], v0 = t0[(j + 2) % 3], v1 = Other(t1, x, y);
                        faces = faces.Where(f => !Has(f.V, x) || !Has(f.V, y)).ToList();
                        faces.Add(new Face(new Index3i(v0, v1, y), before.Faces[e.c].Group));
                        faces.Add(new Face(new Index3i(v1, v0, x), before.Faces[e.d].Group));
                    }
                    break;
                }
                case Op.Collapse: {
                    List<Face> next = faces.Where(f => !Has(f.V, a) || !Has(f.V, b)).Select(f => {
                        Index3i t = f.V;
                        for (int j = 0; j < 3; ++j) if (t[j] == b) t[j] = a;
                        return new Face(t, f.Group);
                    }).ToList();
                    possible = LinkCondition(faces.Select(f => f.V).ToList(), a, b);
                    ok = m.CollapseEdge(a, b, out DMesh3.EdgeCollapseInfo _) == MeshResult.Ok;
                    if (ok) { faces = next; vertices.Remove(b); }
                    break;
                }
                case Op.Poke: {
                    Face f = before.Faces[a];
                    ok = m.PokeTriangle(a, Bary, out DMesh3.PokeTriangleInfo info) == MeshResult.Ok;
                    if (!ok) break;
                    vertices.Add(info.new_vid, Weighted(vertices[f.V.a], vertices[f.V.b], vertices[f.V.c], Bary));
                    derived.Add(info.new_vid);
                    faces.RemoveAt(faces.FindIndex(g => g.Key == f.Key));
                    faces.Add(new Face(new Index3i(f.V.a, f.V.b, info.new_vid), f.Group));
                    faces.Add(new Face(new Index3i(f.V.b, f.V.c, info.new_vid), f.Group));
                    faces.Add(new Face(new Index3i(f.V.c, f.V.a, info.new_vid), f.Group));
                    break;
                }
                case Op.RemoveTriangle: {
                    Face f = before.Faces[a];
                    List<Face> next = faces.Where(g => g.Key != f.Key).ToList();
                    // on a manifold mesh, removal is possible exactly when the rest has no bowtie
                    possible = Manifold(next.Select(g => g.V).ToList());
                    ok = m.RemoveTriangle(a, c.Flag, true) == MeshResult.Ok;
                    if (ok) {
                        faces = next;
                        if (c.Flag)
                            foreach (int v in Corners(f.V))
                                if (!faces.Any(g => Has(g.V, v))) vertices.Remove(v);
                    }
                    break;
                }
                case Op.RemoveIsolated: {
                    ok = m.RemoveVertex(a, false, true) == MeshResult.Ok;
                    if (ok) vertices.Remove(a);
                    break;
                }
                case Op.RemoveVertex: {
                    // Ok must mean exactly this result, without a bowtie. RemoveVertex may refuse a safe removal, as its
                    // guard is conservative, but then the mesh must be unchanged.
                    possible = null;
                    List<Face> next = faces.Where(f => !Has(f.V, a)).ToList();
                    ok = m.RemoveVertex(a, true, true) == MeshResult.Ok;
                    if (ok) {
                        Assert.True(Manifold(next.Select(f => f.V).ToList()), "RemoveVertex reported Ok but its result has a bowtie");
                        faces = next; vertices.Remove(a);
                    }
                    break;
                }
                case Op.Compact: {
                    DMesh3.CompactInfo info = m.CompactInPlace(true);
                    Assert.True(m.IsCompact, "compact");
                    var mapped = new Dictionary<int, Vertex>();
                    foreach (var v in vertices) mapped.Add(info.MapV[v.Key], v.Value);
                    vertices = mapped;
                    faces = faces.Select(f => new Face(new Index3i(info.MapV[f.V.a], info.MapV[f.V.b], info.MapV[f.V.c]), f.Group)).ToList();
                    keepsIDs = false;
                    break;
                }
                case Op.Rebuild: {
                    m.RebuildFromEdgeRefcounts();
                    SameSnapshot(before, m);
                    break;
                }
                case Op.Restore: {
                    // restores into the mesh that already holds the same data, which is a reused destination
                    using (var stream = new MemoryStream()) {
                        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) gSerialization.Store(m, writer);
                        stream.Position = 0;
                        using (var reader = new BinaryReader(stream)) gSerialization.Restore(m, reader);
                    }
                    SameSnapshot(before, m);
                    break;
                }
                default: throw new InvalidOperationException("unknown command");
            }
            state.Attempts.TryGetValue(c.Kind, out int attempts); state.Attempts[c.Kind] = attempts + 1;
            if (ok) { state.Successes.TryGetValue(c.Kind, out int successes); state.Successes[c.Kind] = successes + 1; }
            if (possible.HasValue) Assert.True(possible.Value == ok, c.Kind + ": possible=" + possible + " but ok=" + ok);

            var after = new Snapshot(m);
            Assert.Equal(Sorted(vertices.Keys), Sorted(after.Vertices.Keys));
            double tolerance = 8 * MathUtil.Epsilon * vertices.Values.Select(v => v.P.MaxAbs).DefaultIfEmpty(0).Max();
            foreach (var v in vertices) SameVertex(v.Value, after.Vertices[v.Key], before, derived.Contains(v.Key), tolerance);
            string[] expectedFaces = faces.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedFaces, after.Faces.Values.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
            if (keepsIDs) {
                var keys = new HashSet<string>(expectedFaces);
                foreach (var t in before.Faces)
                    if (keys.Contains(t.Value.Key)) Assert.Equal(t.Value.Key, after.Faces[t.Key].Key);
            }
            CheckTopology(m);
            if (state.Closed) CheckClosed(m);
        }

        static Command Choose(DMesh3 m, Random random, int index, Op[] ops, Op atCap)
        {
            Op op = m.TriangleCount >= TriangleCap ? atCap : ops[random.Next(ops.Length)];
            int[] vertices = Sorted(m.VertexIndices()), triangles = Sorted(m.TriangleIndices()), edges = Sorted(m.EdgeIndices());
            int Pick(int[] ids) => ids[random.Next(ids.Length)];
            var c = new Command { Kind = op, Group = 100 + index, Flag = random.Next(2) == 0,
                Position = new Vector3d(64 + index * 2, random.Next(-8, 9) / 4.0, random.Next(-8, 9) / 4.0) };
            c.Position1 = c.Position + Vector3d.AxisX; c.Position2 = c.Position + Vector3d.AxisY;
            switch (op) {
                case Op.AppendVertex: case Op.Disconnected: case Op.Compact: case Op.Rebuild: case Op.Restore: return c;
                case Op.Attach: case Op.Split: case Op.Flip: case Op.Collapse:
                    if (edges.Length == 0) break;
                    c.A = Pick(edges);
                    if (op == Op.Collapse) {
                        Index2i ev = m.GetEdgeV(c.A);
                        c.A = c.Flag ? ev.a : ev.b; c.B = c.Flag ? ev.b : ev.a;
                    } else if (op == Op.Attach) {
                        Index2i ev = m.GetEdgeV(c.A);
                        Index3i t = m.GetTriangle(m.GetEdgeT(c.A).a);
                        int j = Enumerable.Range(0, 3).Single(i => Pair(t[i], t[(i + 1) % 3]) == Pair(ev.a, ev.b));
                        c.A = t[j]; c.B = t[(j + 1) % 3];
                    }
                    return c;
                case Op.Move: case Op.Attributes:
                    if (vertices.Length == 0) break;
                    c.A = Pick(vertices);
                    c.B = triangles.Length > 0 ? Pick(triangles) : -1;
                    if (op == Op.Move) c.Position = m.GetVertex(c.A) + new Vector3d(random.Next(-2, 3) / 16.0, random.Next(-2, 3) / 16.0, random.Next(-2, 3) / 16.0);
                    return c;
                case Op.Poke: case Op.RemoveTriangle:
                    if (triangles.Length == 0) break;
                    c.A = Pick(triangles);
                    return c;
                case Op.RemoveVertex:
                    if (vertices.Length == 0) break;
                    c.A = Pick(vertices);
                    return c;
                case Op.RemoveIsolated:
                    int[] isolated = vertices.Where(v => m.GetVtxEdgeCount(v) == 0).ToArray();
                    if (isolated.Length == 0) break;
                    c.A = Pick(isolated);
                    return c;
            }
            c.Kind = Op.Disconnected;
            return c;
        }

        State Run(DMesh3 mesh, int seed, Op[] ops, Op atCap, bool closed = false)
        {
            var state = new State(mesh, closed);
            CheckTopology(mesh);
            var random = new Random(seed);
            for (int j = 0; j < Steps; ++j) Apply(state, Choose(mesh, random, j, ops, atCap));
            return state;
        }

        public static IEnumerable<object[]> StatefulCases()
        {
            foreach (string fixture in new[] { "empty", "isolated", "triangle", "grid", "box" })
                for (int i = 0; i < Seeds.Length; ++i) yield return new object[] { fixture, Seeds[i], i % 2 == 0 };
        }

        [Theory]
        [MemberData(nameof(StatefulCases))]
        public void SeededEdits_MatchModel_AndRepeatedRunIsIdentical(string fixture, int seed, bool attributes)
        {
            State first = Run(Fixture(fixture, attributes), seed, AllOps, Op.RemoveTriangle);
            State second = Run(Fixture(fixture, attributes), seed, AllOps, Op.RemoveTriangle);
            Assert.Equal(first.Trace.Select(c => c.ToString()), second.Trace.Select(c => c.ToString()));
            Assert.Equal("", Invariance.Difference(first.Mesh, second.Mesh));
            SameSnapshot(new Snapshot(first.Mesh), second.Mesh);
        }

        [Fact]
        public void SeededEdits_ExerciseEveryOperationAndMostSucceed()
        {
            var attempts = new Dictionary<Op, int>();
            var successes = new Dictionary<Op, int>();
            foreach (object[] args in StatefulCases()) {
                State state = Run(Fixture((string)args[0], (bool)args[2]), (int)args[1], AllOps, Op.RemoveTriangle);
                foreach (Op op in AllOps) {
                    attempts.TryGetValue(op, out int a); state.Attempts.TryGetValue(op, out int da); attempts[op] = a + da;
                    successes.TryGetValue(op, out int s); state.Successes.TryGetValue(op, out int ds); successes[op] = s + ds;
                }
            }
            foreach (Op op in AllOps) {
                output.WriteLine(op + ": " + successes[op] + " of " + attempts[op]);
                Assert.True(attempts[op] >= 100, op + " attempts " + attempts[op]);
                // these may be refused by their definition, depending on the element that was picked
                double share = op == Op.Attach || op == Op.Flip ? 0.25 : op == Op.Collapse || op == Op.RemoveTriangle || op == Op.RemoveVertex ? 0.5 : 1;
                Assert.True(successes[op] >= share * attempts[op], op + ": " + successes[op] + " of " + attempts[op]);
            }
        }

        [Theory]
        [InlineData(7)] [InlineData(101)] [InlineData(1709)] [InlineData(8191)]
        public void ClosedBox_TopologyPreservingEdits_KeepEulerCharacteristicClosednessAndConnectivity(int seed)
        {
            State state = Run(Fixture("box"), seed, ClosedOps, Op.Collapse, true);
            Assert.True(state.Successes[Op.Collapse] >= 5, "collapses: " + state.Successes[Op.Collapse]);
            Assert.True(state.Successes[Op.Flip] >= 5, "flips: " + state.Successes[Op.Flip]);
        }

        [Theory]
        [InlineData("triangle", 7)]
        [InlineData("grid", 29)]
        [InlineData("box", 101)]
        public void SeededEdits_AllSimilarities_GiveTheSameTopologyAndMappedPositions(string fixture, int seed)
        {
            State baseline = Run(Fixture(fixture), seed, AllOps, Op.RemoveTriangle);
            var expected = new Snapshot(baseline.Mesh);
            foreach (Similarity similarity in Similarity.All()) {
                var state = new State(Transformed(Fixture(fixture), similarity));
                foreach (Command c in baseline.Trace)
                    Apply(state, new Command { Kind = c.Kind, A = c.A, B = c.B, Group = c.Group, Flag = c.Flag,
                        Position = similarity.Apply(c.Position), Position1 = similarity.Apply(c.Position1), Position2 = similarity.Apply(c.Position2) });
                var actual = new Snapshot(state.Mesh);
                Assert.Equal(Sorted(expected.Vertices.Keys), Sorted(actual.Vertices.Keys));
                Assert.Equal(Sorted(expected.Edges.Keys), Sorted(actual.Edges.Keys));
                foreach (var t in expected.Faces) Assert.Equal(t.Value.Key, actual.Faces[t.Key].Key);
                foreach (var e in expected.Edges) Assert.Equal(e.Value, actual.Edges[e.Key]);
                double size = expected.Vertices.Values.Select(v => v.P.MaxAbs).DefaultIfEmpty(1).Max();
                foreach (var v in expected.Vertices) {
                    Vector3d back = similarity.Inverse(actual.Vertices[v.Key].P);
                    if (similarity.IsExact)
                        for (int j = 0; j < 3; ++j)
                            Assert.Equal(BitConverter.DoubleToInt64Bits(v.Value.P[j]), BitConverter.DoubleToInt64Bits(back[j]));
                    else Assert.True(back.Distance(v.Value.P) <= 256 * similarity.Resolution(size), similarity.Name + " position " + v.Key);
                    Assert.Equal(v.Value.N, actual.Vertices[v.Key].N);
                    Assert.Equal(v.Value.C, actual.Vertices[v.Key].C);
                    Assert.Equal(v.Value.UV, actual.Vertices[v.Key].UV);
                }
            }
        }

        [Theory]
        [InlineData("isolated", true)]
        [InlineData("isolated", false)]
        [InlineData("triangle", true)]
        [InlineData("triangle", false)]
        [InlineData("detached", true)]
        [InlineData("detached", false)]
        public void Compact_LiveIsolatedVertexAboveDeletionHole_KeepsItsDataAndCanTakeATriangle(string fixture, bool attributes)
        {
            foreach (Similarity similarity in Similarity.All()) {
                DMesh3 mesh = Transformed(Fixture(fixture == "detached" ? "triangle" : fixture, attributes), similarity);
                var state = new State(mesh);
                Vector3d far = similarity.Apply(new Vector3d(8, 2, 3));
                int survivor;
                if (fixture == "triangle") {
                    // the isolated vertex has no adjacency list yet
                    survivor = 3;
                    Apply(state, new Command { Kind = Op.AppendVertex, Position = far, Group = 47 });
                    Apply(state, new Command { Kind = Op.RemoveTriangle, A = 0, Flag = true });
                } else if (fixture == "detached") {
                    // the isolated vertex has an empty adjacency list left over from its triangle
                    survivor = 2;
                    Apply(state, new Command { Kind = Op.RemoveTriangle, A = 0, Flag = false });
                    Apply(state, new Command { Kind = Op.RemoveIsolated, A = 0 });
                    Apply(state, new Command { Kind = Op.RemoveIsolated, A = 1 });
                } else {
                    survivor = 1;
                    Apply(state, new Command { Kind = Op.RemoveIsolated, A = 0 });
                }
                Assert.Equal(new[] { survivor }, Sorted(mesh.VertexIndices()));
                Assert.False(mesh.IsCompact);
                Vector3d position = mesh.GetVertex(survivor);
                Apply(state, new Command { Kind = Op.Compact });
                Assert.Equal(new[] { 0 }, Sorted(mesh.VertexIndices()));
                Assert.Equal(position, mesh.GetVertex(0));

                var compacted = new Snapshot(mesh);
                Apply(state, new Command { Kind = Op.Compact });
                SameSnapshot(compacted, mesh);

                Vector3d offset = similarity.ApplyDirection(Vector3d.AxisX);
                int p = mesh.AppendVertex(position + offset), q = mesh.AppendVertex(position + similarity.ApplyDirection(Vector3d.AxisY));
                Assert.True(mesh.AppendTriangle(0, p, q) >= 0, "the moved vertex takes a triangle");
                Assert.Equal(2, mesh.GetVtxEdgeCount(0));
                CheckTopology(mesh);
            }
        }

        [Fact]
        public void Collapse_ClosedTetrahedron_IsRefusedOnEveryEdge()
        {
            // no seeded run gets down to a tetrahedron, and a collapse would leave two triangles on the same three vertices
            var mesh = new DMesh3(true, true, true, true);
            for (int k = 0; k < 4; ++k) Add(mesh, new Vector3d(k == 1 ? 1 : 0, k == 2 ? 1 : 0, k == 3 ? 1 : 0), k);
            int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 1, 2, 3 }, new[] { 2, 0, 3 } };
            foreach (int[] f in faces) Assert.True(mesh.AppendTriangle(f[0], f[1], f[2], 5) >= 0);
            var state = new State(mesh, true);
            CheckTopology(mesh);
            foreach (int e in Sorted(mesh.EdgeIndices())) {
                Index2i ev = mesh.GetEdgeV(e);
                Apply(state, new Command { Kind = Op.Collapse, A = ev.a, B = ev.b });
                Apply(state, new Command { Kind = Op.Collapse, A = ev.b, B = ev.a });
            }
            Assert.False(state.Successes.ContainsKey(Op.Collapse), "a tetrahedron cannot be collapsed");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Rebuild_MeshWithHolesAndIsolatedVertex_IsUnchangedAndStaysEditable(bool attributes)
        {
            foreach (Similarity similarity in Similarity.All()) {
                DMesh3 mesh = Transformed(Fixture("grid", attributes), similarity);
                var state = new State(mesh);
                Apply(state, new Command { Kind = Op.RemoveTriangle, A = 0, Flag = true });
                Apply(state, new Command { Kind = Op.AppendVertex, Position = similarity.Apply(new Vector3d(9, 9, 9)), Group = 3 });
                var expected = new Snapshot(mesh);
                for (int repeat = 0; repeat < 2; ++repeat) {
                    mesh.RebuildFromEdgeRefcounts();
                    CheckTopology(mesh);
                    SameSnapshot(expected, mesh);
                }
                // the free lists and vertex refcounts of the rebuilt mesh work
                foreach (int t in Sorted(mesh.TriangleIndices())) mesh.RemoveTriangle(t, true, false);
                Assert.Equal(1, mesh.VertexCount);
                CheckTopology(mesh);
            }
        }

        [Fact]
        public void RebuildFromEdgeRefcounts_KeepsLiveVerticesWithoutEdges()
        {
            var mesh = new DMesh3();
            for (int i = 0; i < 4; ++i) mesh.AppendVertex(new Vector3d(i, i * i, 0));
            Assert.Equal(0, mesh.AppendTriangle(0, 1, 2));
            for (int repeat = 0; repeat < 2; ++repeat) {
                mesh.RebuildFromEdgeRefcounts();
                Assert.Equal(4, mesh.VertexCount);
                Assert.True(mesh.IsVertex(3));
                Assert.Equal(0, mesh.GetVtxEdgeCount(3));
                Assert.Equal(new Vector3d(3, 9, 0), mesh.GetVertex(3));
                Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            }
            // the isolated vertex still takes an edge
            Assert.Equal(1, mesh.AppendTriangle(2, 1, 3));
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));

            var isolated = new DMesh3();
            isolated.AppendVertex(Vector3d.Zero);
            isolated.AppendVertex(Vector3d.One);
            isolated.RebuildFromEdgeRefcounts();
            Assert.Equal(2, isolated.VertexCount);
            Assert.True(isolated.CheckValidity(true, FailMode.ReturnOnly));
        }

        [Fact]
        public void RemoveVertex_SafeRemoval_DoesNotDependOnTheOrderOfTheFan()
        {
            // The center of an open disk of five triangles can be removed. The edge list of the vertex is not a walk around
            // the fan: checked triangle by triangle, the removal failed halfway with two triangles gone.
            var mesh = new DMesh3();
            Vector3d[] points = { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(1, 1, 0),
                new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0) };
            foreach (Vector3d p in points) mesh.AppendVertex(p);
            int[][] disk = { new[] { 1, 2 }, new[] { 4, 5 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 1 } };
            foreach (int[] rim in disk) Assert.True(mesh.AppendTriangle(0, rim[0], rim[1]) >= 0);
            Assert.Equal(MeshResult.Ok, mesh.RemoveVertex(0, true, true));
            Assert.False(mesh.IsVertex(0));
            Assert.Equal(0, mesh.TriangleCount);
            CheckTopology(mesh);
        }
    }
}
