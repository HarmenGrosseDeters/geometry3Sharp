using System.Diagnostics;
using System.Globalization;
using g3;
using gs;

namespace G3Demos.Demos
{
    /// <summary> one buffer set for the viewer (positions in single precision: only the drawing is single precision) </summary>
    public sealed class RenderMesh
    {
        // properties, lower case: System.Text.Json (the JS interop) serializes properties, not fields
        public float[] positions { get; set; }
        public int[] indices { get; set; }
        public string color { get; set; } = "#5b8def";
        public double opacity { get; set; } = 1;
        public bool lines { get; set; }
        public bool edges { get; set; }

        /// <summary> the triangles of m, positions divided by frameScale (so a 2^k scale does not move the camera) </summary>
        public static RenderMesh Of(DMesh3 m, string color, double opacity = 1, double frameScale = 1, bool edges = false)
        {
            var map = new int[m.MaxVertexID];
            var pos = new List<float>(m.VertexCount * 3);
            int n = 0;
            foreach (int vid in m.VertexIndices()) {
                map[vid] = n++;
                Vector3d v = m.GetVertex(vid) / frameScale;
                pos.Add((float)v.x); pos.Add((float)v.y); pos.Add((float)v.z);
            }
            var idx = new List<int>(m.TriangleCount * 3);
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                idx.Add(map[t.a]); idx.Add(map[t.b]); idx.Add(map[t.c]);
            }
            return new RenderMesh { positions = pos.ToArray(), indices = idx.ToArray(), color = color, opacity = opacity, edges = edges };
        }

        /// <summary> the boundary edges of m (open edges) as red line segments </summary>
        public static RenderMesh BoundaryEdges(DMesh3 m, double frameScale = 1)
        {
            var pos = new List<float>();
            foreach (int eid in m.BoundaryEdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                foreach (int vid in new[] { ev.a, ev.b }) {
                    Vector3d v = m.GetVertex(vid) / frameScale;
                    pos.Add((float)v.x); pos.Add((float)v.y); pos.Add((float)v.z);
                }
            }
            return new RenderMesh { positions = pos.ToArray(), indices = new int[0], color = "#e11d48", lines = true };
        }
    }

    public enum RowState { Info, Good, Bad, Warn }

    public sealed class StatusRow
    {
        public string Name, Value;
        public RowState State;
        public StatusRow(string name, string value, RowState state = RowState.Info) { Name = name; Value = value; State = state; }
    }

    public sealed class StatusGroup
    {
        public string Title;
        public List<StatusRow> Rows = new List<StatusRow>();
        public StatusGroup(string title) { Title = title; }
        public StatusGroup Add(string name, string value, RowState state = RowState.Info) { Rows.Add(new StatusRow(name, value, state)); return this; }
        public StatusGroup Check(string name, bool ok, string okText = "yes", string badText = "NO") { Rows.Add(new StatusRow(name, ok ? okText : badText, ok ? RowState.Good : RowState.Bad)); return this; }
    }

    /// <summary> what a demo returns: what to draw, what to show, and a one-line verdict </summary>
    public sealed class DemoResult
    {
        public List<RenderMesh> Meshes = new List<RenderMesh>();
        public List<StatusGroup> Groups = new List<StatusGroup>();
        public string Headline = "";
        public RowState HeadlineState = RowState.Info;
        public string FitKey = "";
        /// <summary> a hash of the input coordinates (bit patterns), to tell a platform difference of the input from one of the operation </summary>
        public string InputHash = "";
        /// <summary> the same hash of the result mesh </summary>
        public string OutputHash = "";
    }

    /// <summary> the result checks the status panel shows for every mesh (the same checks a customer would run) </summary>
    public sealed class MeshCheck
    {
        public int Triangles, Vertices, BoundaryEdges, BowtieVertices, NonFiniteVertices, Components;
        public bool Closed, WindingClosed;
        public string NotCleanReasons = "";
        public double Volume, Area;
        public long Milliseconds;

        public bool Manifold => BowtieVertices == 0;
        public bool Finite => NonFiniteVertices == 0;
        public bool Clean => NotCleanReasons.Length == 0;

        public static MeshCheck Of(DMesh3 m, bool selfIntersections = true)
        {
            var sw = Stopwatch.StartNew();
            var c = new MeshCheck { Triangles = m.TriangleCount, Vertices = m.VertexCount };
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                if (!(double.IsFinite(v.x) && double.IsFinite(v.y) && double.IsFinite(v.z)))
                    c.NonFiniteVertices++;
                if (m.IsBowtieVertex(vid))
                    c.BowtieVertices++;
            }
            foreach (int eid in m.BoundaryEdgeIndices())
                c.BoundaryEdges++;
            c.Closed = m.IsClosed();
            c.WindingClosed = m.TriangleCount > 0 && ExactMeshBoolean.IsWindingClosed(m);
            var mcc = new MeshConnectedComponents(m);
            mcc.FindConnectedT();
            c.Components = mcc.Count;
            Vector2d va = MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex);
            c.Volume = va.x; c.Area = va.y;
            c.NotCleanReasons = m.TriangleCount == 0 ? "empty" : MeshRobustRepair.NotCleanReasons(m, selfIntersections);
            c.Milliseconds = sw.ElapsedMilliseconds;
            return c;
        }

        public StatusGroup ToGroup(string title, double expectedVolume = double.NaN, double volumeTolerance = 1e-9)
        {
            var g = new StatusGroup(title);
            g.Add("triangles / vertices", $"{Triangles} / {Vertices}");
            g.Check("closed (no boundary edges)", Closed, "yes", $"NO ({BoundaryEdges} boundary edges)");
            g.Check("closed (winding, IsWindingClosed)", WindingClosed);
            g.Check("manifold vertices", Manifold, "yes", $"NO ({BowtieVertices} bowties)");
            g.Check("finite", Finite, "yes", $"NO ({NonFiniteVertices} vertices)");
            g.Add("components", Components.ToString());
            g.Check("NotCleanReasons", Clean, "\"\" (clean)", "\"" + NotCleanReasons + "\"");
            g.Add("volume / area", $"{F(Volume)} / {F(Area)}");
            if (!double.IsNaN(expectedVolume)) {
                double rel = Math.Abs(Volume - expectedVolume) / Math.Max(Math.Abs(expectedVolume), 1e-300);
                g.Add("expected volume", $"{F(expectedVolume)} (rel. error {rel:E2})",
                    rel <= volumeTolerance ? RowState.Good : rel <= 1e-3 ? RowState.Warn : RowState.Bad);
            }
            g.Add("check time", $"{Milliseconds} ms");
            return g;
        }

        public static string F(double x) => x.ToString("G10", CultureInfo.InvariantCulture);
    }

    public static class Shapes
    {
        public static DMesh3 Box(Vector3d center, Vector3d halfExtents, int edgeVertices)
        {
            if (edgeVertices <= 2)
                return new TrivialBox3Generator() { Box = new Box3d(center, halfExtents) }.Generate().MakeDMesh();
            return new GridBox3Generator() { Box = new Box3d(center, halfExtents), EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        public static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        public static DMesh3 Torus(double major, double minor, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    double r = major + minor * Math.Cos(v);
                    m.AppendVertex(new Vector3d(r * Math.Cos(u), minor * Math.Sin(v), r * Math.Sin(u)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, c, b);
                    m.AppendTriangle(a, d, c);
                }
            if (MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x < 0)
                m.ReverseOrientation();
            return m;
        }

        public static void Transform(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            foreach (int vid in m.VertexIndices())
                m.SetVertex(vid, f(m.GetVertex(vid)));
        }

        /// <summary> FNV-1a over the bit patterns of the vertex coordinates of the given meshes </summary>
        public static string Hash(params DMesh3[] meshes)
        {
            ulong h = 14695981039346656037UL;
            foreach (DMesh3 m in meshes)
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    foreach (double d in new[] { v.x, v.y, v.z }) {
                        h ^= (ulong)BitConverter.DoubleToInt64Bits(d);
                        h *= 1099511628211UL;
                    }
                }
            return h.ToString("x16");
        }

        public static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
    }
}
