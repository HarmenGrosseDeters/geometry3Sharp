using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class DMesh3PropertyTests
    {
        readonly ITestOutputHelper output;
        static readonly int[] Seeds = { 7, 29, 101, 503, 1709, 4093, 8191, 65537 };
        static readonly Vector3d Bary = new Vector3d(0.5, 0.25, 0.25);
        const int Steps = 128;
        const int TriangleCap = 256;

        public DMesh3PropertyTests(ITestOutputHelper output) { this.output = output; }

        enum Op { AppendVertex, Attach, Disconnected, Move, Attributes, SplitBoundary, SplitInterior,
            Flip, Collapse, Poke, RemoveTriangle, RemoveIsolated, RemoveVertex, Compact, Copy }

        sealed class Command
        {
            public Op Kind;
            public int A = -1, B = -1, Group;
            public bool RemoveIsolated;
            public Vector3d Position, Position1, Position2;
            public override string ToString() => Kind + " a=" + A + " b=" + B + " group=" + Group +
                " removeIsolated=" + RemoveIsolated + " p=" + Point(Position) +
                (Kind == Op.Disconnected ? " p1=" + Point(Position1) + " p2=" + Point(Position2) : "") +
                " split=0.5 bary=" + Point(Bary) + " preserveManifold=true";
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
        }

        sealed class Snapshot
        {
            public readonly Dictionary<int, Vertex> Vertices = new Dictionary<int, Vertex>();
            public readonly Dictionary<int, Face> Faces = new Dictionary<int, Face>();
            public readonly Dictionary<int, Index4i> Edges = new Dictionary<int, Index4i>();
            public bool Normals, Colors, UVs, Groups;
            public int Timestamp, ShapeTimestamp;
            public Snapshot(DMesh3 mesh)
            {
                Normals = mesh.HasVertexNormals; Colors = mesh.HasVertexColors;
                UVs = mesh.HasVertexUVs; Groups = mesh.HasTriangleGroups;
                Timestamp = mesh.Timestamp; ShapeTimestamp = mesh.ShapeTimestamp;
                foreach (int v in mesh.VertexIndices().OrderBy(v => v))
                    Vertices.Add(v, new Vertex { P = mesh.GetVertex(v),
                        N = Normals ? mesh.GetVertexNormal(v) : Vector3f.Zero,
                        C = Colors ? mesh.GetVertexColor(v) : Vector3f.Zero,
                        UV = UVs ? mesh.GetVertexUV(v) : Vector2f.Zero });
                foreach (int t in mesh.TriangleIndices().OrderBy(t => t))
                    Faces.Add(t, new Face(mesh.GetTriangle(t), Groups ? mesh.GetTriangleGroup(t) : -1));
                foreach (int e in mesh.EdgeIndices().OrderBy(e => e)) Edges.Add(e, mesh.GetEdge(e));
            }
            public string Dump()
            {
                var text = new StringBuilder();
                text.AppendLine("channels=" + Normals + "," + Colors + "," + UVs + "," + Groups);
                foreach (var v in Vertices.OrderBy(p => p.Key))
                    text.AppendLine("v " + v.Key + " p=" + Point(v.Value.P) + " n=" + Point(v.Value.N) +
                        " c=" + Point(v.Value.C) + " uv=" + Number(v.Value.UV.x) + "," + Number(v.Value.UV.y));
                foreach (var t in Faces.OrderBy(p => p.Key))
                    text.AppendLine("t " + t.Key + " " + t.Value.V.a + "," + t.Value.V.b + "," + t.Value.V.c + " g=" + t.Value.Group);
                foreach (var e in Edges.OrderBy(p => p.Key))
                    text.AppendLine("e " + e.Key + " " + e.Value.a + "," + e.Value.b + "," + e.Value.c + "," + e.Value.d);
                return text.ToString();
            }
        }

        sealed class State
        {
            public DMesh3 Mesh;
            public readonly StringBuilder Log = new StringBuilder();
            public readonly List<Command> Trace = new List<Command>();
            public readonly Dictionary<Op, int> Success = new Dictionary<Op, int>();
            public Dictionary<int, int> LogicalVertices = new Dictionary<int, int>();
            public Dictionary<int, int> LogicalFaces = new Dictionary<int, int>();
            public int NextVertex, NextFace;
            public bool Closed;
            public State(DMesh3 mesh, string context, bool closed = false)
            {
                Mesh = mesh; Closed = closed;
                foreach (int v in mesh.VertexIndices().OrderBy(v => v)) LogicalVertices[v] = NextVertex++;
                foreach (int t in mesh.TriangleIndices().OrderBy(t => t)) LogicalFaces[t] = NextFace++;
                Log.AppendLine(context).Append(new Snapshot(mesh).Dump());
            }
        }

        static string Number(double x) => x.ToString("R", CultureInfo.InvariantCulture);
        static string Point(Vector3d p) => Number(p.x) + "," + Number(p.y) + "," + Number(p.z);
        static Index2i Pair(int a, int b) => a < b ? new Index2i(a, b) : new Index2i(b, a);
        static bool Has(Index3i t, int v) => t.a == v || t.b == v || t.c == v;
        static string Oriented(Index3i t)
        {
            int j = t.a < t.b && t.a < t.c ? 0 : (t.b < t.c ? 1 : 2);
            return t[j] + "," + t[(j + 1) % 3] + "," + t[(j + 2) % 3];
        }
        static int Other(Index3i t, int a, int b) => Enumerable.Range(0, 3).Select(j => t[j]).Single(v => v != a && v != b);
        static void Require(bool condition, string message, State state)
        {
            if (!condition) Assert.True(false, message + "\n" + state.Log);
        }
        static void Equal<T>(T expected, T actual, string message, State state) => Require(EqualityComparer<T>.Default.Equals(expected, actual),
            message + " expected=" + expected + " actual=" + actual, state);
        static void SameIDs(IEnumerable<int> expected, IEnumerable<int> actual, string message, State state) =>
            Require(expected.OrderBy(v => v).SequenceEqual(actual.OrderBy(v => v)), message, state);

        static Dictionary<Index2i, List<int>> Incidence(Dictionary<int, Face> faces)
        {
            var result = new Dictionary<Index2i, List<int>>();
            foreach (var face in faces.OrderBy(p => p.Key)) {
                Index3i t = face.Value.V;
                for (int j = 0; j < 3; ++j) {
                    Index2i key = Pair(t[j], t[(j + 1) % 3]);
                    if (!result.ContainsKey(key)) result.Add(key, new List<int>());
                    result[key].Add(face.Key);
                }
            }
            return result;
        }

        // The oracle derives links and incidence from triangles, never from DMesh3 adjacency.
        static bool SingleFan(int v, Dictionary<int, Face> faces, Dictionary<Index2i, List<int>> edges)
        {
            int[] incident = faces.Where(p => Has(p.Value.V, v)).Select(p => p.Key).OrderBy(t => t).ToArray();
            if (incident.Length == 0) return true;
            var local = edges.Where(p => p.Key.a == v || p.Key.b == v).ToArray();
            if (local.Any(p => p.Value.Count > 2)) return false;
            int boundary = local.Count(p => p.Value.Count == 1);
            if (boundary != 0 && boundary != 2) return false;
            var visited = new HashSet<int> { incident[0] };
            var todo = new Queue<int>(); todo.Enqueue(incident[0]);
            while (todo.Count != 0) {
                int t = todo.Dequeue();
                foreach (var edge in local.Where(p => p.Value.Contains(t)))
                    foreach (int next in edge.Value)
                        if (visited.Add(next)) todo.Enqueue(next);
            }
            return visited.Count == incident.Length;
        }

        static void Validate(State state, bool allowBowtie = false)
        {
            DMesh3 m = state.Mesh;
            Require(m.CheckValidity(allowBowtie, FailMode.ReturnOnly), "CheckValidity", state);
            var s = new Snapshot(m);
            Equal(m.VertexCount, s.Vertices.Count, "enumerated vertex count", state);
            Equal(m.TriangleCount, s.Faces.Count, "enumerated triangle count", state);
            Equal(m.EdgeCount, s.Edges.Count, "enumerated edge count", state);
            SameIDs(s.Vertices.Keys, Enumerable.Range(0, m.MaxVertexID).Where(m.IsVertex), "vertex ID holes", state);
            SameIDs(s.Faces.Keys, Enumerable.Range(0, m.MaxTriangleID).Where(m.IsTriangle), "triangle ID holes", state);
            SameIDs(s.Edges.Keys, Enumerable.Range(0, m.MaxEdgeID).Where(m.IsEdge), "edge ID holes", state);
            var incidence = Incidence(s.Faces);
            Equal(incidence.Count, m.EdgeCount, "triangle-derived edge count", state);
            var actualEdges = new Dictionary<Index2i, int>();
            foreach (var edge in s.Edges) {
                Index4i e = edge.Value;
                Require(e.a != e.b && s.Vertices.ContainsKey(e.a) && s.Vertices.ContainsKey(e.b), "edge endpoints", state);
                Index2i key = Pair(e.a, e.b);
                Require(!actualEdges.ContainsKey(key) && incidence.ContainsKey(key), "unique triangle-derived edge", state);
                actualEdges.Add(key, edge.Key);
                List<int> expected = incidence[key];
                Require(expected.Count == 1 || expected.Count == 2, "edge manifoldness", state);
                Require(e.c >= 0, "primary adjacent triangle", state);
                SameIDs(expected, new[] { e.c, e.d }.Where(t => t >= 0), "reciprocal edge triangles", state);
                Equal(expected.Count == 1, m.IsBoundaryEdge(edge.Key), "boundary edge", state);
                Equal(edge.Key, m.FindEdge(e.a, e.b), "FindEdge forward", state);
                Equal(edge.Key, m.FindEdge(e.b, e.a), "FindEdge reverse", state);
                if (expected.Count == 2) {
                    Index3i first = s.Faces[expected[0]].V, second = s.Faces[expected[1]].V;
                    int direction1 = Enumerable.Range(0, 3).Single(j => Pair(first[j], first[(j + 1) % 3]) == key);
                    int direction2 = Enumerable.Range(0, 3).Single(j => Pair(second[j], second[(j + 1) % 3]) == key);
                    Equal(first[direction1], second[(direction2 + 1) % 3], "opposite shared-edge orientation", state);
                }
            }
            foreach (var face in s.Faces) {
                Index3i t = face.Value.V, te = m.GetTriEdges(face.Key);
                Require(t.a != t.b && t.b != t.c && t.c != t.a, "distinct face vertices", state);
                for (int j = 0; j < 3; ++j) {
                    Require(s.Vertices.ContainsKey(t[j]), "live face vertex", state);
                    Equal(actualEdges[Pair(t[j], t[(j + 1) % 3])], te[j], "ordered triangle edge", state);
                }
                Equal(3, new[] { te.a, te.b, te.c }.Distinct().Count(), "distinct face edges", state);
                Equal(Enumerable.Range(0, 3).Any(j => incidence[Pair(t[j], t[(j + 1) % 3])].Count == 1),
                    m.IsBoundaryTriangle(face.Key), "boundary triangle", state);
            }
            foreach (var vertex in s.Vertices) {
                int v = vertex.Key;
                Require(vertex.Value.P.IsFinite, "finite position", state);
                if (s.Normals) Require(Finite(vertex.Value.N), "finite normal", state);
                if (s.Colors) Require(Finite(vertex.Value.C), "finite color", state);
                if (s.UVs) Require(Finite(vertex.Value.UV.x) && Finite(vertex.Value.UV.y), "finite UV", state);
                int[] triangles = s.Faces.Where(p => Has(p.Value.V, v)).Select(p => p.Key).ToArray();
                int[] edges = actualEdges.Where(p => p.Key.a == v || p.Key.b == v).Select(p => p.Value).ToArray();
                SameIDs(triangles, m.VtxTrianglesItr(v), "vertex triangle incidence", state);
                var unoriented = new List<int>(); var oriented = new List<int>();
                Equal(MeshResult.Ok, m.GetVtxTriangles(v, unoriented, false), "unoriented incidence status", state);
                Equal(MeshResult.Ok, m.GetVtxTriangles(v, oriented, true), "oriented incidence status", state);
                SameIDs(triangles, unoriented, "unoriented vertex triangles", state);
                SameIDs(triangles, oriented, "oriented vertex triangles", state);
                SameIDs(edges, m.GetVtxEdges(v), "vertex edge incidence", state);
                Equal(triangles.Length, m.GetVtxTriangleCount(v), "vertex triangle count", state);
                Equal(edges.Length, m.GetVtxEdgeCount(v), "vertex edge count", state);
                SameIDs(incidence.Keys.Where(p => p.a == v || p.b == v).Select(p => p.a == v ? p.b : p.a),
                    m.VtxVerticesItr(v), "vertex neighbors", state);
                Equal(incidence.Any(p => (p.Key.a == v || p.Key.b == v) && p.Value.Count == 1),
                    m.IsBoundaryVertex(v), "boundary vertex", state);
                if (!allowBowtie) Require(SingleFan(v, s.Faces, incidence), "independent vertex manifoldness", state);
            }
            bool closed = s.Faces.Count > 0 && incidence.Values.All(ts => ts.Count == 2);
            Equal(closed, m.IsClosed(), "triangle-derived closedness", state);
            Equal(closed, m.CachedIsClosed, "cached closedness", state);
            if (state.Closed) {
                Require(closed, "closed fixture stays closed", state);
                Equal(2, m.VertexCount - m.EdgeCount + m.TriangleCount, "closed box Euler characteristic", state);
                Require(Connected(s), "closed box stays connected", state);
            }
            CheckBounds(state, s);
        }

        static bool Connected(Snapshot s)
        {
            if (s.Vertices.Count == 0) return true;
            var reached = new HashSet<int> { s.Vertices.Keys.Min() };
            var todo = new Queue<int>(); todo.Enqueue(reached.Single());
            var edges = Incidence(s.Faces).Keys.ToArray();
            while (todo.Count > 0) {
                int v = todo.Dequeue();
                foreach (var e in edges.Where(e => e.a == v || e.b == v)) {
                    int next = e.a == v ? e.b : e.a;
                    if (reached.Add(next)) todo.Enqueue(next);
                }
            }
            return reached.Count == s.Vertices.Count;
        }
        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        static bool Finite(Vector3f v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static void CheckBounds(State state, Snapshot snapshot)
        {
            AxisAlignedBox3d expected = AxisAlignedBox3d.Empty;
            foreach (var v in snapshot.Vertices.Values) expected.Contain(v.P);
            Equal(expected.Min, state.Mesh.CachedBounds.Min, "cached bounds min", state);
            Equal(expected.Max, state.Mesh.CachedBounds.Max, "cached bounds max", state);
            Equal(expected.Min, state.Mesh.GetBounds().Min, "fresh bounds min", state);
            Equal(expected.Max, state.Mesh.GetBounds().Max, "fresh bounds max", state);
        }

        static Vertex Attributes(Vector3d p, int tag)
        {
            int k = 1 + tag % 23;
            return new Vertex { P = p, N = new Vector3f(k, k + 1, k + 2).Normalized,
                C = new Vector3f(k / 32f, (k + 1) / 32f, (k + 2) / 32f), UV = new Vector2f(k / 16f, -k / 32f) };
        }
        static NewVertexInfo Info(Vertex v) => new NewVertexInfo { v = v.P, n = v.N, c = v.C, uv = v.UV,
            bHaveN = true, bHaveC = true, bHaveUV = true };
        static int Add(DMesh3 mesh, Vector3d p, int tag) => mesh.AppendVertex(Info(Attributes(p, tag)));
        static int AddFace(DMesh3 mesh, int a, int b, int c, int group)
        {
            int t = mesh.AppendTriangle(a, b, c, group);
            Assert.True(t >= 0, "procedural fixture triangle rejected");
            return t;
        }
        static int[] AddGrid(DMesh3 mesh, int n, double offset)
        {
            int[] v = new int[(n + 1) * (n + 1)];
            for (int y = 0; y <= n; ++y)
                for (int x = 0; x <= n; ++x) v[y * (n + 1) + x] = Add(mesh, new Vector3d(offset + x, y, 0), mesh.MaxVertexID);
            for (int y = 0; y < n; ++y)
                for (int x = 0; x < n; ++x) {
                    int a = v[y * (n + 1) + x], b = v[y * (n + 1) + x + 1];
                    int c = v[(y + 1) * (n + 1) + x], d = v[(y + 1) * (n + 1) + x + 1];
                    AddFace(mesh, a, b, d, 10 + y * n + x);
                    AddFace(mesh, a, d, c, 40 + y * n + x);
                }
            return v;
        }
        static DMesh3 Fixture(string name, bool attributes = true)
        {
            var m = new DMesh3(attributes, attributes, attributes, attributes);
            if (name == "isolated") { Add(m, Vector3d.Zero, 1); Add(m, new Vector3d(2, 3, 4), 2); }
            else if (name == "triangle") AddFace(m, Add(m, Vector3d.Zero, 1), Add(m, Vector3d.AxisX, 2), Add(m, Vector3d.AxisY, 3), 17);
            else if (name == "grid") AddGrid(m, 3, 0);
            else if (name == "box") {
                Vector3d[] points = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
                    new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0, 1, 1) };
                foreach (Vector3d p in points) Add(m, p, m.VertexCount + 1);
                int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
                    new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
                    new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };
                for (int j = 0; j < faces.Length; ++j) AddFace(m, faces[j][0], faces[j][1], faces[j][2], 80 + j);
            } else Assert.Equal("empty", name);
            return m;
        }

        static void SameVertex(Vertex expected, Vertex actual, Snapshot channels, State state, string message)
        {
            Equal(expected.P, actual.P, message + " position", state);
            if (channels.Normals) Equal(expected.N, actual.N, message + " normal", state);
            if (channels.Colors) Equal(expected.C, actual.C, message + " color", state);
            if (channels.UVs) Equal(expected.UV, actual.UV, message + " UV", state);
        }
        static void SameSnapshot(Snapshot expected, DMesh3 mesh, State state, bool timestamps = false)
        {
            var actual = new Snapshot(mesh);
            Equal(expected.Normals, actual.Normals, "normal channel", state);
            Equal(expected.Colors, actual.Colors, "color channel", state);
            Equal(expected.UVs, actual.UVs, "UV channel", state);
            Equal(expected.Groups, actual.Groups, "group channel", state);
            SameIDs(expected.Vertices.Keys, actual.Vertices.Keys, "snapshot vertices", state);
            SameIDs(expected.Faces.Keys, actual.Faces.Keys, "snapshot faces", state);
            SameIDs(expected.Edges.Keys, actual.Edges.Keys, "snapshot edges", state);
            foreach (var v in expected.Vertices) SameVertex(v.Value, actual.Vertices[v.Key], expected, state, "snapshot v=" + v.Key);
            foreach (var t in expected.Faces) Equal(t.Value, actual.Faces[t.Key], "snapshot face=" + t.Key, state);
            foreach (var e in expected.Edges) Equal(e.Value, actual.Edges[e.Key], "snapshot edge=" + e.Key, state);
            if (timestamps) {
                Equal(expected.Timestamp, actual.Timestamp, "rejection timestamp", state);
                Equal(expected.ShapeTimestamp, actual.ShapeTimestamp, "rejection shape timestamp", state);
            }
        }
        static void SameFaces(Dictionary<int, Face> expected, DMesh3 mesh, State state)
        {
            SameIDs(expected.Keys, mesh.TriangleIndices(), "operation face IDs", state);
            foreach (var f in expected) {
                Equal(Oriented(f.Value.V), Oriented(mesh.GetTriangle(f.Key)), "oriented operation face=" + f.Key, state);
                if (mesh.HasTriangleGroups) Equal(f.Value.Group, mesh.GetTriangleGroup(f.Key), "operation face group=" + f.Key, state);
            }
        }
        static Vertex Weighted(Vertex a, Vertex b, Vertex c, Vector3d weights)
        {
            double x = weights.x, y = weights.y, z = weights.z;
            return new Vertex { P = x * a.P + y * b.P + z * c.P,
                N = new Vector3f(x * a.N.x + y * b.N.x + z * c.N.x, x * a.N.y + y * b.N.y + z * c.N.y,
                    x * a.N.z + y * b.N.z + z * c.N.z).Normalized,
                C = new Vector3f(x * a.C.x + y * b.C.x + z * c.C.x, x * a.C.y + y * b.C.y + z * c.C.y,
                    x * a.C.z + y * b.C.z + z * c.C.z),
                UV = new Vector2f(x * a.UV.x + y * b.UV.x + z * c.UV.x, x * a.UV.y + y * b.UV.y + z * c.UV.y) };
        }

        void Execute(State state, Command command, bool mustSucceed = true)
        {
            try { ExecuteCore(state, command, mustSucceed); }
            catch {
                output.WriteLine(state.Log.ToString());
                throw;
            }
        }

        void ExecuteCore(State state, Command command, bool mustSucceed)
        {
            DMesh3 m = state.Mesh;
            var before = new Snapshot(m);
            AxisAlignedBox3d warmBounds = m.CachedBounds;
            bool warmClosed = m.CachedIsClosed;
            state.Trace.Add(command);
            string entry = "step=" + (state.Trace.Count - 1) + " " + command;
            state.Log.AppendLine(entry); output.WriteLine(entry);
            var vertices = new Dictionary<int, Vertex>(before.Vertices);
            var faces = new Dictionary<int, Face>(before.Faces);
            var createdVertices = new List<int>();
            bool success = true, partialRemovalFailure = false;
            MeshResult result = MeshResult.Ok;
            int a = command.A, b = command.B;
            Action<int, Vertex> created = (id, vertex) => {
                Require(id >= 0 && !before.Vertices.ContainsKey(id) && !createdVertices.Contains(id), "fresh distinct vertex ID", state);
                createdVertices.Add(id); vertices.Add(id, vertex);
            };
            Action<int, Face> createdFace = (id, face) => {
                Require(id >= 0 && !before.Faces.ContainsKey(id) && !faces.ContainsKey(id), "fresh distinct face ID", state);
                faces.Add(id, face);
            };
            switch (command.Kind) {
                case Op.AppendVertex: {
                    Vertex value = Attributes(command.Position, command.Group);
                    created(m.AppendVertex(Info(value)), value); break;
                }
                case Op.Disconnected: {
                    Vector3d[] positions = { command.Position, command.Position1, command.Position2 };
                    int[] ids = new int[3];
                    for (int j = 0; j < 3; ++j) {
                        Vertex value = Attributes(positions[j], command.Group + j);
                        ids[j] = m.AppendVertex(Info(value)); created(ids[j], value);
                    }
                    createdFace(m.AppendTriangle(ids[0], ids[1], ids[2], command.Group), new Face(new Index3i(ids[0], ids[1], ids[2]), command.Group));
                    break;
                }
                case Op.Attach: {
                    Vertex value = Attributes(command.Position, command.Group);
                    int v = m.AppendVertex(Info(value)); created(v, value);
                    createdFace(m.AppendTriangle(b, a, v, command.Group), new Face(new Index3i(b, a, v), command.Group)); break;
                }
                case Op.Move: {
                    m.SetVertex(a, command.Position);
                    Vertex value = vertices[a]; value.P = command.Position; vertices[a] = value; break;
                }
                case Op.Attributes: {
                    Vertex value = Attributes(vertices[a].P, command.Group);
                    m.SetVertexNormal(a, value.N); m.SetVertexColor(a, value.C); m.SetVertexUV(a, value.UV);
                    vertices[a] = value;
                    if (b >= 0) { m.SetTriangleGroup(b, command.Group); faces[b] = new Face(faces[b].V, command.Group); }
                    break;
                }
                case Op.SplitBoundary:
                case Op.SplitInterior: {
                    Index2i ev = m.GetEdgeV(a); Index2i et = m.GetEdgeT(a);
                    result = m.SplitEdge(a, out DMesh3.EdgeSplitInfo info, 0.5);
                    success = result == MeshResult.Ok;
                    if (!success) break;
                    bool boundary = et.b == DMesh3.InvalidID;
                    Equal(command.Kind == Op.SplitBoundary, boundary, "split fixture branch", state);
                    Equal(boundary, info.bIsBoundary, "split boundary info", state);
                    Vertex expected = Weighted(vertices[ev.a], vertices[ev.b], default(Vertex), new Vector3d(0.5, 0.5, 0));
                    created(info.vNew, expected);
                    int[] old = boundary ? new[] { et.a } : new[] { et.a, et.b };
                    int[] added = boundary ? new[] { info.eNewT2 } : new[] { info.eNewT2, info.eNewT3 };
                    for (int k = 0; k < old.Length; ++k) {
                        Face f = before.Faces[old[k]];
                        int j = Enumerable.Range(0, 3).Single(i => Pair(f.V[i], f.V[(i + 1) % 3]) == Pair(ev.a, ev.b));
                        int x = f.V[j], y = f.V[(j + 1) % 3], z = f.V[(j + 2) % 3];
                        // Which child keeps the old ID depends on the first face's oriented edge.
                        Index3i primary = before.Faces[et.a].V;
                        int first = Enumerable.Range(0, 3).Single(i => Pair(primary[i], primary[(i + 1) % 3]) == Pair(ev.a, ev.b));
                        int replaced = primary[(first + 1) % 3];
                        Index3i retained = f.V;
                        for (int i = 0; i < 3; ++i) if (retained[i] == replaced) retained[i] = info.vNew;
                        faces[old[k]] = new Face(retained, f.Group);
                        Index3i child = y == replaced ? new Index3i(info.vNew, y, z) : new Index3i(x, info.vNew, z);
                        createdFace(added[k], new Face(child, f.Group));
                    }
                    int[] newEdges = boundary ? new[] { info.eNewBN, info.eNewCN } : new[] { info.eNewBN, info.eNewCN, info.eNewDN };
                    Require(newEdges.Distinct().Count() == newEdges.Length && newEdges.All(e => m.IsEdge(e) && !before.Edges.ContainsKey(e)), "split new edges", state);
                    if (boundary) { Equal(-1, info.eNewDN, "boundary split no second opposing edge", state); Equal(-1, info.eNewT3, "boundary split no fourth face", state); }
                    Equal(before.Edges.Count + (boundary ? 2 : 3), m.EdgeCount, "split edge delta", state);
                    Require(m.FindEdge(ev.a, info.vNew) >= 0 && m.FindEdge(ev.b, info.vNew) >= 0 && m.FindEdge(ev.a, ev.b) == -1, "split replaces old edge by two segments", state);
                    break;
                }
                case Op.Flip: {
                    Index2i ev = m.GetEdgeV(a), et = m.GetEdgeT(a);
                    result = m.FlipEdge(a, out DMesh3.EdgeFlipInfo info); success = result == MeshResult.Ok;
                    if (!success) break;
                    Index3i primary = before.Faces[et.a].V;
                    int j = Enumerable.Range(0, 3).Single(i => Pair(primary[i], primary[(i + 1) % 3]) == Pair(ev.a, ev.b));
                    int x = primary[j], y = primary[(j + 1) % 3], c = primary[(j + 2) % 3];
                    int d = Other(before.Faces[et.b].V, x, y);
                    Equal(a, info.eID, "flip edge info", state); Equal(x, info.v0, "flip first endpoint", state); Equal(y, info.v1, "flip second endpoint", state);
                    Equal(c, info.ov0, "flip first opposite", state); Equal(d, info.ov1, "flip second opposite", state);
                    Equal(et.a, info.t0, "flip first face info", state); Equal(et.b, info.t1, "flip second face info", state);
                    faces[et.a] = new Face(new Index3i(c, d, y), faces[et.a].Group);
                    faces[et.b] = new Face(new Index3i(d, c, x), faces[et.b].Group);
                    Equal(-1, m.FindEdge(x, y), "old diagonal gone", state); Equal(a, m.FindEdge(c, d), "new diagonal reuses edge ID", state);
                    Equal(before.Edges.Count, m.EdgeCount, "flip edge count", state); break;
                }
                case Op.Collapse: {
                    int edge = m.FindEdge(a, b);
                    Index2i et = m.GetEdgeT(edge);
                    result = m.CollapseEdge(a, b, out DMesh3.EdgeCollapseInfo info); success = result == MeshResult.Ok;
                    if (!success) break;
                    Equal(a, info.vKept, "collapse keep", state); Equal(b, info.vRemoved, "collapse remove", state);
                    Equal(edge, info.eCollapsed, "collapse edge info", state); Equal(et.b == -1, info.bIsBoundary, "collapse boundary", state);
                    SameIDs(new[] { et.a, et.b }.Where(t => t >= 0), new[] { info.tRemoved0, info.tRemoved1 }.Where(t => t >= 0), "collapse removed-face info", state);
                    vertices.Remove(b);
                    foreach (int t in before.Faces.Keys) {
                        Index3i tv = before.Faces[t].V;
                        if (Has(tv, a) && Has(tv, b)) faces.Remove(t);
                        else if (Has(tv, b)) {
                            for (int j = 0; j < 3; ++j) if (tv[j] == b) tv[j] = a;
                            faces[t] = new Face(tv, before.Faces[t].Group);
                        }
                    }
                    int[] removedEdges = info.bIsBoundary ? new[] { info.eCollapsed, info.eRemoved0 } : new[] { info.eCollapsed, info.eRemoved0, info.eRemoved1 };
                    Require(removedEdges.Distinct().Count() == removedEdges.Length && removedEdges.All(e => !m.IsEdge(e)), "collapse removed edges", state);
                    Equal(before.Edges.Count - removedEdges.Length, m.EdgeCount, "collapse edge delta", state);
                    Require(m.IsEdge(info.eKept0) && (info.bIsBoundary || m.IsEdge(info.eKept1)), "collapse kept edges", state);
                    break;
                }
                case Op.Poke: {
                    Face f = faces[a];
                    result = m.PokeTriangle(a, Bary, out DMesh3.PokeTriangleInfo info); success = result == MeshResult.Ok;
                    if (!success) break;
                    created(info.new_vid, Weighted(vertices[f.V.a], vertices[f.V.b], vertices[f.V.c], Bary));
                    faces[a] = new Face(new Index3i(f.V.a, f.V.b, info.new_vid), f.Group);
                    createdFace(info.new_t1, new Face(new Index3i(f.V.b, f.V.c, info.new_vid), f.Group));
                    createdFace(info.new_t2, new Face(new Index3i(f.V.c, f.V.a, info.new_vid), f.Group));
                    int[] es = { info.new_edges.a, info.new_edges.b, info.new_edges.c };
                    Require(es.Distinct().Count() == 3 && es.All(e => m.IsEdge(e) && !before.Edges.ContainsKey(e)), "poke new edges", state);
                    for (int j = 0; j < 3; ++j) Equal(Pair(f.V[j], info.new_vid), Pair(m.GetEdgeV(es[j]).a, m.GetEdgeV(es[j]).b), "poke radial edge", state);
                    Equal(before.Edges.Count + 3, m.EdgeCount, "poke edge delta", state); break;
                }
                case Op.RemoveTriangle: {
                    result = m.RemoveTriangle(a, command.RemoveIsolated, true); success = result == MeshResult.Ok;
                    if (!success) break;
                    faces.Remove(a);
                    if (command.RemoveIsolated)
                        foreach (int v in new[] { before.Faces[a].V.a, before.Faces[a].V.b, before.Faces[a].V.c })
                            if (!faces.Values.Any(f => Has(f.V, v))) vertices.Remove(v);
                    break;
                }
                case Op.RemoveIsolated:
                case Op.RemoveVertex: {
                    bool removeTriangles = command.Kind == Op.RemoveVertex;
                    MeshResult expectedResult = MeshResult.Ok;
                    if (removeTriangles) faces = PredictVertexRemoval(before, m, a, state, out expectedResult);
                    result = m.RemoveVertex(a, removeTriangles, true);
                    Equal(expectedResult, result, "vertex removal result", state);
                    success = result == MeshResult.Ok;
                    if (!success) {
                        int removed = before.Faces.Count - faces.Count;
                        partialRemovalFailure = removed > 0;
                        if (partialRemovalFailure) {
                            Equal(before.Timestamp + removed, m.Timestamp, "partial removal timestamp", state);
                            Equal(before.ShapeTimestamp + removed, m.ShapeTimestamp, "partial removal shape timestamp", state);
                        }
                        break;
                    }
                    vertices.Remove(a); break;
                }
                case Op.Compact: {
                    Compact(state, before);
                    state.Log.AppendLine("result=Ok compact V=" + m.VertexCount + " T=" + m.TriangleCount + " E=" + m.EdgeCount);
                    Validate(state); Count(state, command.Kind); return;
                }
                case Op.Copy: {
                    var copy = new DMesh3(m);
                    SameSnapshot(before, copy, state);
                    var copyState = new State(copy, "copy checkpoint\n" + state.Log, state.Closed);
                    Validate(copyState);
                    if (copy.VertexCount > 0) {
                        int v = copy.VertexIndices().Min(); Vector3d p = copy.GetVertex(v);
                        copy.SetVertex(v, p + new Vector3d(0.125, -0.25, 0.5));
                        if (copy.HasVertexColors) copy.SetVertexColor(v, new Vector3f(0.75f, 0.5f, 0.25f));
                        if (copy.HasTriangleGroups && copy.TriangleCount > 0) copy.SetTriangleGroup(copy.TriangleIndices().Min(), 12345);
                        SameSnapshot(before, m, state, true);
                        m.SetVertex(v, p + new Vector3d(-0.125, 0.25, -0.5));
                        Equal(p + new Vector3d(0.125, -0.25, 0.5), copy.GetVertex(v), "source edit does not change copy", state);
                        m.SetVertex(v, p);
                        Validate(copyState);
                    }
                    break;
                }
                default: throw new InvalidOperationException("unknown command");
            }
            state.Log.AppendLine("result=" + result + " createdVertices=" + string.Join(",", createdVertices) +
                " live V=" + m.VertexCount + " T=" + m.TriangleCount + " E=" + m.EdgeCount);
            if (mustSucceed) Require(success, "deterministic/eligible command must succeed", state);
            if (!success && !partialRemovalFailure) SameSnapshot(before, m, state, true);
            else {
                SameIDs(vertices.Keys, m.VertexIndices(), "operation vertex IDs/count", state);
                var actual = new Snapshot(m);
                foreach (var v in vertices) SameVertex(v.Value, actual.Vertices[v.Key], before, state, "operation v=" + v.Key);
                SameFaces(faces, m, state);
                foreach (int v in before.Vertices.Keys.Except(vertices.Keys)) Require(!m.IsVertex(v), "removed vertex no longer live", state);
                foreach (int t in before.Faces.Keys.Except(faces.Keys)) Require(!m.IsTriangle(t), "removed face no longer live", state);
                UpdateLogical(state, before, actual);
                if (success) Count(state, command.Kind);
            }
            Validate(state);
            Require(m.TriangleCount <= TriangleCap, "live triangle budget", state);
        }

        static Dictionary<int, Face> PredictVertexRemoval(Snapshot before, DMesh3 mesh, int vertex, State state, out MeshResult result)
        {
            var faces = new Dictionary<int, Face>(before.Faces);
            var edges = Incidence(faces);
            result = MeshResult.Ok;
            foreach (Face face in faces.Values.Where(f => Has(f.V, vertex))) {
                int j = Enumerable.Range(0, 3).Single(i => face.V[i] == vertex);
                int a = face.V[(j + 1) % 3], b = face.V[(j + 2) % 3];
                if (edges[Pair(a, b)].Count > 1 && edges.Any(e => e.Value.Count == 1 &&
                    (e.Key.a == a || e.Key.b == a || e.Key.a == b || e.Key.b == b))) {
                    result = MeshResult.Failed_WouldCreateBowtie;
                    state.Log.AppendLine("removeVertex initial guard rejects; removedFaces=none");
                    return faces;
                }
            }
            var order = new List<int>();
            mesh.GetVtxTriangles(vertex, order, true);
            SameIDs(faces.Where(f => Has(f.Value.V, vertex)).Select(f => f.Key), order, "compound removal incident order coverage", state);
            state.Log.AppendLine("removeVertex orderedFaces=" + string.Join(",", order));
            // RemoveVertex deletes incident triangles sequentially, not transactionally. Predict only
            // face deletion and triangle-derived links; no adjacency/refcount editor is reproduced.
            foreach (int triangle in order) {
                Index3i removed = faces[triangle].V;
                var next = new Dictionary<int, Face>(faces);
                next.Remove(triangle);
                var nextEdges = Incidence(next);
                if (Enumerable.Range(0, 3).Any(j => !SingleFan(removed[j], next, nextEdges))) {
                    result = MeshResult.Failed_WouldCreateBowtie;
                    state.Log.AppendLine("removeVertex blockedFace=" + triangle + " removedFaces=" +
                        string.Join(",", before.Faces.Keys.Except(faces.Keys).OrderBy(t => t)));
                    return faces;
                }
                faces = next;
            }
            return faces;
        }

        static void Count(State state, Op op) { if (!state.Success.ContainsKey(op)) state.Success[op] = 0; ++state.Success[op]; }
        static void UpdateLogical(State state, Snapshot before, Snapshot after)
        {
            foreach (int v in before.Vertices.Keys.Except(after.Vertices.Keys)) state.LogicalVertices.Remove(v);
            foreach (int v in after.Vertices.Keys.Except(before.Vertices.Keys).OrderBy(v => v)) state.LogicalVertices.Add(v, state.NextVertex++);
            foreach (int t in before.Faces.Keys.Except(after.Faces.Keys)) state.LogicalFaces.Remove(t);
            foreach (int t in after.Faces.Keys.Except(before.Faces.Keys).OrderBy(t => t)) state.LogicalFaces.Add(t, state.NextFace++);
            SameIDs(after.Vertices.Keys, state.LogicalVertices.Keys, "logical vertex lifetime", state);
            SameIDs(after.Faces.Keys, state.LogicalFaces.Keys, "logical face lifetime", state);
        }
        static void Compact(State state, Snapshot before)
        {
            DMesh3 m = state.Mesh;
            var oldLogicalV = new Dictionary<int, int>(state.LogicalVertices);
            var oldLogicalT = new Dictionary<int, int>(state.LogicalFaces);
            DMesh3.CompactInfo info = m.CompactInPlace(true);
            Require(info.MapV != null && m.IsCompact, "compaction map and compactness", state);
            var mappedV = new Dictionary<int, int>();
            var after = new Snapshot(m);
            foreach (var v in before.Vertices) {
                int next = info.MapV[v.Key];
                state.Log.AppendLine("compact MapV " + v.Key + " -> " + next);
                Require(m.IsVertex(next) && !mappedV.ContainsKey(next), "bijective compaction vertex map", state);
                mappedV.Add(next, oldLogicalV[v.Key]);
                SameVertex(v.Value, after.Vertices[next], before, state, "mapped vertex=" + v.Key);
            }
            SameIDs(mappedV.Keys, after.Vertices.Keys, "compaction covers every vertex", state);
            var expectedFaces = new Dictionary<string, KeyValuePair<int, Face>>();
            foreach (var f in before.Faces) {
                Index3i mapped = new Index3i(info.MapV[f.Value.V.a], info.MapV[f.Value.V.b], info.MapV[f.Value.V.c]);
                expectedFaces.Add(Oriented(mapped), new KeyValuePair<int, Face>(oldLogicalT[f.Key], new Face(mapped, f.Value.Group)));
            }
            Equal(before.Faces.Count, after.Faces.Count, "compaction triangle count", state);
            Equal(before.Edges.Count, after.Edges.Count, "compaction edge count", state);
            var mappedT = new Dictionary<int, int>();
            foreach (var f in after.Faces) {
                string key = Oriented(f.Value.V);
                Require(expectedFaces.ContainsKey(key), "mapped oriented face", state);
                var expected = expectedFaces[key];
                if (before.Groups) Equal(expected.Value.Group, f.Value.Group, "compacted face group", state);
                mappedT.Add(f.Key, expected.Key); expectedFaces.Remove(key);
            }
            Equal(0, expectedFaces.Count, "compaction covers every face", state);
            state.LogicalVertices = mappedV; state.LogicalFaces = mappedT;
        }

        static Command Cmd(Op kind, int a = -1, int b = -1, Vector3d? position = null, int group = 19, bool isolated = false)
        {
            Vector3d p = position ?? Vector3d.Zero;
            return new Command { Kind = kind, A = a, B = b, Position = p, Position1 = p + Vector3d.AxisX,
                Position2 = p + Vector3d.AxisY, Group = group, RemoveIsolated = isolated };
        }
        static Command Transform(Command command, Similarity similarity) => new Command {
            Kind = command.Kind, A = command.A, B = command.B, Group = command.Group, RemoveIsolated = command.RemoveIsolated,
            Position = similarity.Apply(command.Position), Position1 = similarity.Apply(command.Position1), Position2 = similarity.Apply(command.Position2)
        };
        static int[] Eligible(State state, Op op)
        {
            var s = new Snapshot(state.Mesh); var edges = Incidence(s.Faces);
            if (op == Op.SplitBoundary || op == Op.SplitInterior)
                return s.Edges.Where(e => edges[Pair(e.Value.a, e.Value.b)].Count == (op == Op.SplitBoundary ? 1 : 2)).Select(e => e.Key).OrderBy(e => e).ToArray();
            if (op == Op.Flip)
                return s.Edges.Where(e => {
                    List<int> ts = edges[Pair(e.Value.a, e.Value.b)];
                    return ts.Count == 2 && !edges.ContainsKey(Pair(Other(s.Faces[ts[0]].V, e.Value.a, e.Value.b), Other(s.Faces[ts[1]].V, e.Value.a, e.Value.b)));
                }).Select(e => e.Key).OrderBy(e => e).ToArray();
            if (op == Op.RemoveTriangle)
                return s.Faces.Keys.Where(t => {
                    Index3i tv = s.Faces[t].V;
                    // This is the documented guard, expressed through triangle-derived incidence.
                    return Enumerable.Range(0, 3).All(j => !edges.Any(e => (e.Key.a == tv[j] || e.Key.b == tv[j]) && e.Value.Count == 1) ||
                        edges[Pair(tv[j], tv[(j + 1) % 3])].Count == 1 || edges[Pair(tv[j], tv[(j + 2) % 3])].Count == 1);
                }).OrderBy(t => t).ToArray();
            if (op == Op.RemoveIsolated)
                return s.Vertices.Keys.Where(v => !s.Faces.Values.Any(f => Has(f.V, v))).OrderBy(v => v).ToArray();
            if (op == Op.RemoveVertex)
                return s.Vertices.Keys.Where(v => s.Faces.Values.Any(f => Has(f.V, v)) && s.Faces.Values.Where(f => Has(f.V, v)).All(f => {
                    int j = Enumerable.Range(0, 3).Single(i => f.V[i] == v); int a = f.V[(j + 1) % 3], b = f.V[(j + 2) % 3];
                    return edges[Pair(a, b)].Count == 1 || (!edges.Any(e => (e.Key.a == a || e.Key.b == a) && e.Value.Count == 1) &&
                        !edges.Any(e => (e.Key.a == b || e.Key.b == b) && e.Value.Count == 1));
                })).OrderBy(v => v).ToArray();
            if (op == Op.Collapse)
                return s.Edges.Where(e => {
                    int keep = e.Value.a, remove = e.Value.b;
                    var affected = s.Faces.Where(f => Has(f.Value.V, remove)).ToArray();
                    var replacement = new Dictionary<int, Face>(s.Faces);
                    foreach (var f in affected) {
                        if (Has(f.Value.V, keep)) replacement.Remove(f.Key);
                        else { Index3i t = f.Value.V; for (int j = 0; j < 3; ++j) if (t[j] == remove) t[j] = keep; replacement[f.Key] = new Face(t, f.Value.Group); }
                    }
                    if (replacement.Count == 0 || replacement.Values.Select(f => string.Join(",", new[] { f.V.a, f.V.b, f.V.c }.OrderBy(v => v))).Distinct().Count() != replacement.Count) return false;
                    var nextEdges = Incidence(replacement);
                    return s.Vertices.Keys.Where(v => v != remove).All(v => SingleFan(v, replacement, nextEdges)) &&
                        replacement.Values.Any(f => Has(f.V, keep)) && affected.Any(f => !Has(f.Value.V, keep));
                }).Select(e => e.Key).OrderBy(e => e).ToArray();
            throw new InvalidOperationException("no eligibility rule for " + op);
        }

        void Prelude(State state)
        {
            Execute(state, Cmd(Op.AppendVertex, position: new Vector3d(20, 2, 3), group: 3));
            int isolated = state.Mesh.VertexIndices().Single(v => state.Mesh.GetVertex(v) == new Vector3d(20, 2, 3));
            Execute(state, Cmd(Op.Move, isolated, position: new Vector3d(20.125, 2.25, 3.5)));
            Execute(state, Cmd(Op.Attributes, isolated, state.Mesh.TriangleCount > 0 ? state.Mesh.TriangleIndices().Min() : -1, group: 21));
            Execute(state, Cmd(Op.RemoveIsolated, isolated));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(30, 0, 0), group: 31));
            int single = state.Mesh.TriangleIndices().Single(t => state.Mesh.GetTriangleGroup(t) == 31);
            Execute(state, Cmd(Op.RemoveTriangle, single, isolated: false));
            foreach (int v in Eligible(state, Op.RemoveIsolated)) Execute(state, Cmd(Op.RemoveIsolated, v));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(35, 0, 0), group: 32));
            single = state.Mesh.TriangleIndices().Single(t => state.Mesh.GetTriangleGroup(t) == 32);
            Execute(state, Cmd(Op.RemoveTriangle, single, isolated: true));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(40, 0, 0), group: 33));
            single = state.Mesh.TriangleIndices().Single(t => state.Mesh.GetTriangleGroup(t) == 33);
            int remove = state.Mesh.GetTriangle(single).a;
            Execute(state, Cmd(Op.RemoveVertex, remove));
            foreach (int v in Eligible(state, Op.RemoveIsolated)) Execute(state, Cmd(Op.RemoveIsolated, v));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(50, 0, 0), group: 34));
            int tid = state.Mesh.TriangleIndices().Single(t => state.Mesh.GetTriangleGroup(t) == 34);
            Index3i t0 = state.Mesh.GetTriangle(tid);
            Execute(state, Cmd(Op.Attach, t0.a, t0.b, new Vector3d(50, -1, 0), 35));
            int boundary = state.Mesh.FindEdge(t0.b, t0.c);
            Execute(state, Cmd(Op.SplitBoundary, boundary));
            int boundaryVertex = state.LogicalVertices.OrderByDescending(p => p.Value).First().Key;
            Execute(state, Cmd(Op.Collapse, t0.b, boundaryVertex));
            Execute(state, Cmd(Op.SplitBoundary, state.Mesh.FindEdge(t0.b, t0.c)));
            int diagonal = state.Mesh.FindEdge(t0.a, t0.b);
            Execute(state, Cmd(Op.Flip, diagonal));
            Execute(state, Cmd(Op.SplitInterior, diagonal));
            int newVertex = state.LogicalVertices.OrderByDescending(p => p.Value).First().Key;
            Index2i splitEdge = state.Mesh.GetEdgeV(diagonal);
            int keep = splitEdge.a == newVertex ? splitEdge.b : splitEdge.a;
            Execute(state, Cmd(Op.Collapse, keep, newVertex));
            Execute(state, Cmd(Op.Poke, state.Mesh.TriangleIndices().Min()));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(54, 0, 0), group: 36));
            Execute(state, Cmd(Op.Disconnected, position: new Vector3d(58, 0, 0), group: 37));
            int deleted = state.Mesh.TriangleIndices().Single(t => state.Mesh.GetTriangleGroup(t) == 36);
            Execute(state, Cmd(Op.RemoveTriangle, deleted, isolated: true));
            Require(!state.Mesh.IsCompact, "deletion leaves gaps before compaction", state);
            Execute(state, Cmd(Op.Compact));
            Execute(state, Cmd(Op.Copy));
            foreach (Op op in Enum.GetValues(typeof(Op))) Require(state.Success.ContainsKey(op) && state.Success[op] > 0, "successful prelude coverage: " + op, state);
        }

        Command Choose(State state, Random random, int index)
        {
            DMesh3 m = state.Mesh;
            Op op = m.TriangleCount >= TriangleCap - 2 ? Op.RemoveTriangle : (Op)random.Next(15);
            int Pick(int[] ids) => ids[random.Next(ids.Length)];
            int tag = 100 + index;
            Vector3d fresh = new Vector3d(64 + index * 2, random.Next(-8, 9) / 4.0, random.Next(-8, 9) / 4.0);
            if (op == Op.AppendVertex || op == Op.Disconnected) return Cmd(op, position: fresh, group: tag);
            if (op == Op.Attach) {
                int[] eligible = Eligible(state, Op.SplitBoundary);
                if (eligible.Length > 0) {
                    int e = Pick(eligible); Index2i ev = m.GetEdgeV(e); Index2i et = m.GetEdgeT(e); Index3i t = m.GetTriangle(et.a);
                    int j = Enumerable.Range(0, 3).Single(i => Pair(t[i], t[(i + 1) % 3]) == Pair(ev.a, ev.b));
                    return Cmd(op, t[j], t[(j + 1) % 3], fresh, tag);
                }
            } else if (op == Op.Move || op == Op.Attributes) {
                int[] ids = m.VertexIndices().OrderBy(v => v).ToArray();
                if (ids.Length > 0) {
                    int v = Pick(ids); int[] ts = m.TriangleIndices().OrderBy(t => t).ToArray();
                    Vector3d p = m.GetVertex(v) + new Vector3d(random.Next(-2, 3) / 16.0, random.Next(-2, 3) / 16.0, random.Next(-2, 3) / 16.0);
                    return Cmd(op, v, ts.Length > 0 ? Pick(ts) : -1, p, tag);
                }
            } else if (op == Op.Poke) {
                int[] ids = m.TriangleIndices().OrderBy(t => t).ToArray();
                if (ids.Length > 0) return Cmd(op, Pick(ids));
            } else if (op == Op.Compact || op == Op.Copy) return Cmd(op);
            else {
                int[] ids = Eligible(state, op);
                if (ids.Length > 0) {
                    int id = Pick(ids);
                    if (op == Op.Collapse) { Index2i ev = m.GetEdgeV(id); return Cmd(op, ev.a, ev.b); }
                    return Cmd(op, id, isolated: random.Next(2) == 0);
                }
            }
            if (m.TriangleCount >= TriangleCap - 2) {
                int[] removableVertices = Eligible(state, Op.RemoveVertex);
                if (removableVertices.Length > 0) return Cmd(Op.RemoveVertex, Pick(removableVertices));
                return Cmd(Op.Compact);
            }
            return Cmd(Op.Disconnected, position: fresh, group: tag);
        }

        public static IEnumerable<object[]> StatefulCases()
        {
            foreach (string fixture in new[] { "empty", "isolated", "triangle", "grid", "box" })
                foreach (int seed in Seeds) yield return new object[] { fixture, seed };
        }

        [Theory]
        [MemberData(nameof(StatefulCases))]
        public void SeededMutations_ValidateEffectsIncidenceAttributesAndReplay(string fixture, int seed)
        {
            var original = Fixture(fixture);
            var state = new State(original, "seed=" + seed + " fixture=" + fixture);
            output.WriteLine(state.Log.ToString()); Validate(state); Prelude(state);
            int suffixLength = Steps - state.Trace.Count;
            Require(suffixLength > 0, "prelude leaves a bounded random suffix", state);
            var random = new Random(seed);
            for (int j = 0; j < suffixLength; ++j) Execute(state, Choose(state, random, j), false);
            Equal(Steps, state.Trace.Count, "bounded complete mutation sequence", state);
            var expected = new Snapshot(state.Mesh);
            var replay = new State(Fixture(fixture), "replay seed=" + seed + " fixture=" + fixture);
            foreach (Command command in state.Trace) Execute(replay, command, false);
            SameSnapshot(expected, replay.Mesh, replay);
            Require(state.LogicalVertices.OrderBy(p => p.Key).SequenceEqual(replay.LogicalVertices.OrderBy(p => p.Key)), "deterministic logical vertex identities", replay);
            Require(state.LogicalFaces.OrderBy(p => p.Key).SequenceEqual(replay.LogicalFaces.OrderBy(p => p.Key)), "deterministic logical face identities", replay);
            var replayBefore = new Snapshot(replay.Mesh);
            Execute(state, Cmd(Op.AppendVertex, position: new Vector3d(-10, -20, -30)));
            SameSnapshot(replayBefore, replay.Mesh, state, true);
        }

        [Theory]
        [InlineData("isolated", true)]
        [InlineData("isolated", false)]
        [InlineData("triangle", true)]
        [InlineData("triangle", false)]
        public void Compact_LiveIsolatedVertexAboveDeletionHole_PreservesMappedPayload(string fixture, bool attributes)
        {
            foreach (Similarity similarity in Similarity.All()) {
                DMesh3 mesh = Fixture(fixture, attributes);
                foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, similarity.Apply(mesh.GetVertex(v)));
                var state = new State(mesh, "minimal isolated compaction fixture=" + fixture + " attributes=" + attributes +
                    " similarity=" + similarity.Name);
                output.WriteLine(state.Log.ToString()); Validate(state);
                int survivor;
                if (fixture == "triangle") {
                    Execute(state, Cmd(Op.AppendVertex, position: similarity.Apply(new Vector3d(8, 2, 3)), group: 47));
                    survivor = mesh.MaxVertexID - 1;
                    Execute(state, Cmd(Op.RemoveTriangle, 0, isolated: true));
                } else {
                    survivor = 1;
                    Execute(state, Cmd(Op.RemoveIsolated, 0));
                }
                SameIDs(new[] { survivor }, mesh.VertexIndices(), "one retained isolated source vertex", state);
                Equal(0, mesh.TriangleCount, "minimal compaction no triangles", state);
                Equal(0, mesh.EdgeCount, "minimal compaction no adjacency", state);
                Require(!mesh.IsVertex(0) && !mesh.IsCompact, "deletion hole below isolated source", state);
                var beforeCompact = new Snapshot(mesh);
                int identity = state.LogicalVertices[survivor];
                Execute(state, Cmd(Op.Compact));
                var compacted = new Snapshot(mesh);
                SameIDs(new[] { 0 }, mesh.VertexIndices(), "isolated vertex moves into lowest hole", state);
                Equal(identity, state.LogicalVertices[0], "MapV retains isolated logical identity", state);
                Equal(beforeCompact.Normals, compacted.Normals, "isolated compaction normal channel", state);
                Equal(beforeCompact.Colors, compacted.Colors, "isolated compaction color channel", state);
                Equal(beforeCompact.UVs, compacted.UVs, "isolated compaction UV channel", state);
                Equal(beforeCompact.Groups, compacted.Groups, "isolated compaction group channel", state);
                SameVertex(beforeCompact.Vertices[survivor], compacted.Vertices[0], beforeCompact, state, "isolated compacted payload");
            }
        }

        static DMesh3 NoncyclicFan()
        {
            var mesh = new DMesh3(true, true, true, true);
            Vector3d[] points = { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(1, 1, 0),
                new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0) };
            for (int j = 0; j < points.Length; ++j) Add(mesh, points[j], 50 + j);
            AddFace(mesh, 0, 1, 2, 71);
            AddFace(mesh, 0, 4, 5, 72);
            AddFace(mesh, 0, 2, 3, 73);
            AddFace(mesh, 0, 3, 4, 74);
            AddFace(mesh, 0, 5, 1, 75);
            return mesh;
        }

        [Fact]
        public void RemoveVertex_NoncyclicFanOrder_LateRejectionRetainsExactDeletedPrefix()
        {
            foreach (Similarity similarity in Similarity.All()) {
                DMesh3 initial = NoncyclicFan();
                foreach (int v in initial.VertexIndices()) initial.SetVertex(v, similarity.Apply(initial.GetVertex(v)));
                var state = new State(new DMesh3(initial), "minimal partial vertex removal similarity=" + similarity.Name);
                output.WriteLine(state.Log.ToString()); Validate(state);
                Require(state.Mesh.VtxTrianglesItr(0).SequenceEqual(new[] { 0, 2, 1, 4, 3 }),
                    "noncyclic removal order removes adjacent prefix then encounters a middle face", state);
                Require(Eligible(state, Op.RemoveVertex).Contains(0), "initial compound guard permits pole removal", state);
                Execute(state, Cmd(Op.RemoveVertex, 0), false);
                SameIDs(new[] { 1, 3, 4 }, state.Mesh.TriangleIndices(), "only prefix faces 0 and 2 were removed", state);
                SameIDs(Enumerable.Range(0, 6), state.Mesh.VertexIndices(), "late rejection retains every vertex", state);
                Equal(7, state.Mesh.EdgeCount, "partial removal exact edge count", state);
                Equal(0, state.Mesh.VtxEdgesItr(2).Count(), "newly isolated rim vertex remains live", state);
                Require(state.Mesh.IsBoundaryVertex(0), "retained pole has one open fan", state);
                Require(!state.Success.ContainsKey(Op.RemoveVertex), "partial rejection is not counted as success", state);
                var replay = new State(new DMesh3(initial), "minimal partial vertex removal replay similarity=" + similarity.Name);
                Execute(replay, Cmd(Op.RemoveVertex, 0), false);
                SameSnapshot(new Snapshot(state.Mesh), replay.Mesh, replay);
                Require(state.LogicalFaces.OrderBy(p => p.Key).SequenceEqual(replay.LogicalFaces.OrderBy(p => p.Key)),
                    "partial removal replay retains logical face identities", replay);
            }
        }

        [Fact]
        public void AttributeFreeFixture_AllSuccessfulOperationsRemainValid()
        {
            // Group tags are used only for prelude selection, so this trace is recorded with attributes,
            // then replayed with all optional channels absent.
            var source = new State(Fixture("triangle"), "attribute-free trace source");
            Prelude(source);
            var target = new State(Fixture("triangle", false), "attribute-free replay");
            foreach (Command command in source.Trace) Execute(target, command);
            Require(!target.Mesh.HasVertexNormals && !target.Mesh.HasVertexColors && !target.Mesh.HasVertexUVs && !target.Mesh.HasTriangleGroups,
                "mutations do not enable absent channels", target);
        }

        [Fact]
        public void CompleteSuccessfulTrace_OpenMutations_UsesEverySimilarity()
        {
            DMesh3 initial = Fixture("triangle");
            var baseline = new State(new DMesh3(initial), "complete affine trace");
            Prelude(baseline);
            var expected = new Snapshot(baseline.Mesh);
            foreach (Similarity similarity in Similarity.All()) {
                var mesh = new DMesh3(initial);
                foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, similarity.Apply(mesh.GetVertex(v)));
                var state = new State(mesh, "complete affine trace similarity=" + similarity.Name);
                foreach (Command command in baseline.Trace) Execute(state, Transform(command, similarity));
                var actual = new Snapshot(state.Mesh);
                SameIDs(expected.Vertices.Keys, actual.Vertices.Keys, "affine vertex IDs", state);
                SameIDs(expected.Edges.Keys, actual.Edges.Keys, "affine edge IDs", state);
                SameFaces(expected.Faces, mesh, state);
                foreach (var edge in expected.Edges) Equal(edge.Value, actual.Edges[edge.Key], "affine edge incidence", state);
                foreach (var v in expected.Vertices) {
                    Vector3d back = similarity.Inverse(actual.Vertices[v.Key].P);
                    if (similarity.IsExact)
                        for (int j = 0; j < 3; ++j) Equal(BitConverter.DoubleToInt64Bits(v.Value.P[j]), BitConverter.DoubleToInt64Bits(back[j]), "affine dyadic bitwise position", state);
                    else Require(back.Distance(v.Value.P) <= 64 * similarity.Resolution(64), "affine mapped-back position", state);
                    Equal(v.Value.N, actual.Vertices[v.Key].N, "affine stored normal payload", state);
                    Equal(v.Value.C, actual.Vertices[v.Key].C, "affine color", state);
                    Equal(v.Value.UV, actual.Vertices[v.Key].UV, "affine UV", state);
                }
                Require(baseline.Success.OrderBy(p => p.Key).SequenceEqual(state.Success.OrderBy(p => p.Key)), "affine operation success counts", state);
                Require(baseline.LogicalVertices.OrderBy(p => p.Key).SequenceEqual(state.LogicalVertices.OrderBy(p => p.Key)), "affine logical vertex map", state);
                Require(baseline.LogicalFaces.OrderBy(p => p.Key).SequenceEqual(state.LogicalFaces.OrderBy(p => p.Key)), "affine logical face map", state);
            }
        }

        [Fact]
        public void ClosedBox_TopologyPreservingTrace_IsInvariantAndDeterministic()
        {
            DMesh3 initial = Fixture("box");
            var baseline = new State(new DMesh3(initial), "closed dyadic trace", true);
            Validate(baseline);
            int edge = Eligible(baseline, Op.Flip).First();
            Execute(baseline, Cmd(Op.Flip, edge));
            Execute(baseline, Cmd(Op.SplitInterior, edge));
            int added = baseline.LogicalVertices.OrderByDescending(p => p.Value).First().Key;
            Index2i ev = baseline.Mesh.GetEdgeV(edge);
            Execute(baseline, Cmd(Op.Collapse, ev.a == added ? ev.b : ev.a, added));
            Execute(baseline, Cmd(Op.Poke, baseline.Mesh.TriangleIndices().Min()));
            Execute(baseline, Cmd(Op.Move, 0, position: new Vector3d(-0.125, 0.25, 0.5)));
            Execute(baseline, Cmd(Op.Attributes, 1, 0, group: 22));
            Execute(baseline, Cmd(Op.Compact)); Execute(baseline, Cmd(Op.Copy));
            var expected = new Snapshot(baseline.Mesh);
            foreach (Similarity similarity in Similarity.All()) {
                // Normals have a float representation. Keep their values unchanged here: this test
                // exercises affine position propagation, not a bitwise claim about rotated float normals.
                var transformed = new DMesh3(initial);
                foreach (int v in transformed.VertexIndices()) transformed.SetVertex(v, similarity.Apply(transformed.GetVertex(v)));
                var state = new State(transformed, "closed trace similarity=" + similarity.Name, true);
                foreach (Command command in baseline.Trace) {
                    Command mapped = Transform(command, similarity);
                    Execute(state, mapped);
                }
                var actual = new Snapshot(state.Mesh);
                SameIDs(expected.Vertices.Keys, actual.Vertices.Keys, "invariant vertex IDs", state);
                SameFaces(expected.Faces, state.Mesh, state);
                foreach (var e in expected.Edges) Equal(e.Value, actual.Edges[e.Key], "invariant edge incidence", state);
                foreach (var v in expected.Vertices) {
                    Vector3d back = similarity.Inverse(actual.Vertices[v.Key].P);
                    if (similarity.IsExact)
                        for (int j = 0; j < 3; ++j) Equal(BitConverter.DoubleToInt64Bits(v.Value.P[j]), BitConverter.DoubleToInt64Bits(back[j]), "dyadic bitwise position", state);
                    else Require(back.Distance(v.Value.P) <= 64 * similarity.Resolution(4), "mapped-back position", state);
                    if (expected.Normals) Equal(v.Value.N, actual.Vertices[v.Key].N, "invariant stored normal", state);
                    Equal(v.Value.C, actual.Vertices[v.Key].C, "invariant color", state); Equal(v.Value.UV, actual.Vertices[v.Key].UV, "invariant UV", state);
                }
            }
            var repeat = new State(new DMesh3(initial), "closed repeated trace", true);
            foreach (Command command in baseline.Trace) Execute(repeat, command);
            SameSnapshot(expected, repeat.Mesh, repeat);
        }

        void Reject(State state, string description, Action action)
        {
            var before = new Snapshot(state.Mesh);
            AxisAlignedBox3d warm = state.Mesh.CachedBounds;
            state.Log.AppendLine(description); output.WriteLine(description);
            try { action(); SameSnapshot(before, state.Mesh, state, true); Validate(state); }
            catch {
                output.WriteLine(state.Log.ToString());
                throw;
            }
        }

        [Fact]
        public void EarlyRejections_InvalidIDsBoundaryFlipDuplicateAndNonmanifold_DoNotEdit()
        {
            var state = new State(Fixture("grid"), "early rejection matrix"); Validate(state);
            Reject(state, "SplitEdge invalid e=-1", () => Equal(MeshResult.Failed_NotAnEdge, state.Mesh.SplitEdge(-1, out _), "invalid split", state));
            Reject(state, "FlipEdge invalid e=-1", () => Equal(MeshResult.Failed_NotAnEdge, state.Mesh.FlipEdge(-1, out _), "invalid flip", state));
            Reject(state, "CollapseEdge keep=-1 remove=0", () => Equal(MeshResult.Failed_NotAnEdge, state.Mesh.CollapseEdge(-1, 0, out _), "invalid collapse", state));
            Reject(state, "PokeTriangle tid=-1", () => Equal(MeshResult.Failed_NotATriangle, state.Mesh.PokeTriangle(-1, Bary, out _), "invalid poke", state));
            Reject(state, "RemoveTriangle tid=-1 preserve=true", () => Equal(MeshResult.Failed_NotATriangle, state.Mesh.RemoveTriangle(-1, false, true), "invalid remove triangle", state));
            Reject(state, "RemoveVertex vid=-1 preserve=true", () => Equal(MeshResult.Failed_NotAVertex, state.Mesh.RemoveVertex(-1, false, true), "invalid remove vertex", state));
            Reject(state, "AppendTriangle vertices=-1,0,1", () => Equal(DMesh3.InvalidID, state.Mesh.AppendTriangle(-1, 0, 1), "negative append result is not MeshResult", state));
            int boundary = Eligible(state, Op.SplitBoundary).First();
            Reject(state, "FlipEdge boundary e=" + boundary, () => Equal(MeshResult.Failed_IsBoundaryEdge, state.Mesh.FlipEdge(boundary, out _), "boundary flip", state));
            int interior = Eligible(state, Op.SplitInterior).First(); Index2i iv = state.Mesh.GetEdgeV(interior);
            int third = state.Mesh.VertexIndices().First(v => v != iv.a && v != iv.b);
            Reject(state, "AppendTriangle third face on edge=" + interior + " vertices=" + iv.a + "," + iv.b + "," + third,
                () => Equal(DMesh3.NonManifoldID, state.Mesh.AppendTriangle(iv.a, iv.b, third), "nonmanifold append", state));
            int duplicate = state.Mesh.GetEdgeT(interior).a; Index3i tri = state.Mesh.GetTriangle(duplicate);
            Reject(state, "AppendTriangle duplicate tid=" + duplicate, () => Equal(DMesh3.NonManifoldID, state.Mesh.AppendTriangle(tri), "duplicate with occupied interior edge", state));
            Reject(state, "RemoveVertex referenced vid=0 removeAll=false preserve=true", () =>
                Assert.Throws<NotImplementedException>(() => state.Mesh.RemoveVertex(0, false, true)));
        }

        [Fact]
        public void ManifoldRemovalGuard_RejectsMiddleOfBoundaryFan_AndBowtieIsSeparate()
        {
            var m = new DMesh3(true, true, true, true);
            int center = Add(m, Vector3d.Zero, 1);
            int[] rim = { Add(m, new Vector3d(1, 0, 0), 2), Add(m, new Vector3d(1, 1, 0), 3),
                Add(m, new Vector3d(0, 1, 0), 4), Add(m, new Vector3d(-1, 1, 0), 5) };
            AddFace(m, center, rim[0], rim[1], 1);
            int middle = AddFace(m, center, rim[1], rim[2], 2);
            AddFace(m, center, rim[2], rim[3], 3);
            var state = new State(m, "boundary three-face fan"); Validate(state);
            Reject(state, "RemoveTriangle middle=" + middle + " removeIsolated=false preserve=true", () =>
                Equal(MeshResult.Failed_WouldCreateBowtie, m.RemoveTriangle(middle, false, true), "manifold triangle removal guard", state));
            Reject(state, "RemoveVertex middle rim=" + rim[1] + " removeAll=true preserve=true", () =>
                Equal(MeshResult.Failed_WouldCreateBowtie, m.RemoveVertex(rim[1], true, true), "manifold vertex removal guard", state));

            var bowtie = new DMesh3(true, true, true, true);
            int c = Add(bowtie, Vector3d.Zero, 1);
            AddFace(bowtie, c, Add(bowtie, Vector3d.AxisX, 2), Add(bowtie, Vector3d.AxisY, 3), 5);
            AddFace(bowtie, c, Add(bowtie, -Vector3d.AxisX, 4), Add(bowtie, -Vector3d.AxisY, 5), 6);
            var relaxed = new State(bowtie, "explicit bowtie, not part of strict family");
            Require(!bowtie.CheckValidity(false, FailMode.ReturnOnly), "strict validity rejects bowtie", relaxed);
            Require(bowtie.IsBowtieVertex(c), "bowtie characterized", relaxed);
            Require(!SingleFan(c, new Snapshot(bowtie).Faces, Incidence(new Snapshot(bowtie).Faces)), "independent disconnected vertex link", relaxed);
            Validate(relaxed, true); Require(!bowtie.IsClosed(), "bowtie is intentionally open", relaxed);
        }

        [Fact]
        public void Copy_ReplacesWarmState_A_B_A_MatchesFreshInstances()
        {
            DMesh3 a = Fixture("box"), b = Fixture("grid", false);
            var state = new State(Fixture("triangle"), "Copy A/B/A reuse");
            foreach (DMesh3 source in new[] { a, b, a }) {
                AxisAlignedBox3d warmBounds = state.Mesh.CachedBounds; bool warmClosed = state.Mesh.CachedIsClosed;
                state.Log.AppendLine("Copy source\n" + new Snapshot(source).Dump());
                state.Mesh.Copy(source);
                state.Closed = ReferenceEquals(source, a);
                SameSnapshot(new Snapshot(new DMesh3(source)), state.Mesh, state); Validate(state);
                int v = state.Mesh.VertexIndices().Min(); Vector3d original = source.GetVertex(v);
                state.Mesh.SetVertex(v, original + new Vector3d(2, 3, 4));
                Equal(original, source.GetVertex(v), "Copy does not alias source", state); Validate(state);
            }
        }
    }
}
