using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// Readers and writers: write -> read round trips of every triangle format, in memory. Relations: the round trip keeps the
    /// solid (counts, closed and manifold, volume; positions bit for bit where the format stores doubles), a second round trip
    /// is the identity on the first (write -> read -> write gives the same bytes), a mesh with gaps in its ids writes the same
    /// file as its compact copy, a 2^k scale of the input gives the 2^k scale of the read mesh, two meshes in one file keep
    /// their counts, an orientation flip stays a flip.
    /// </summary>
    public static class IoStage
    {
        const string S = "io";

        public struct Format
        {
            public string Name, Ext; public bool Binary; public int Digits;
            public bool ExactPositions;   // the format stores doubles (or shortest round-trip decimals)
            public bool KeepsVertices;    // false: STL (a soup, welded by the reader)
            public bool HasTriangles;     // false: xyz
        }

        public static readonly Format[] Formats = {
            new Format { Name = "obj", Ext = "obj", Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "off", Ext = "off", Digits = 15, ExactPositions = false, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "stl-ascii", Ext = "stl", Digits = 15, ExactPositions = false, KeepsVertices = false, HasTriangles = true },
            new Format { Name = "stl-binary", Ext = "stl", Binary = true, Digits = 15, ExactPositions = false, KeepsVertices = false, HasTriangles = true },
            new Format { Name = "ply-ascii", Ext = "ply", Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "ply-binary", Ext = "ply", Binary = true, Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "ply-float", Ext = "ply", Binary = true, Digits = 7, ExactPositions = false, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "g3mesh", Ext = "g3mesh", Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "3mf", Ext = "3mf", Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "glb", Ext = "glb", Digits = 15, ExactPositions = false, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "gltf", Ext = "gltf", Digits = 15, ExactPositions = false, KeepsVertices = true, HasTriangles = true },
            new Format { Name = "xyz", Ext = "xyz", Digits = 15, ExactPositions = true, KeepsVertices = true, HasTriangles = false },
        };

        public static byte[] Write(Format f, IList<DMesh3> meshes, out IOWriteResult res, Action<WriteOptions> tweak = null)
        {
            var streams = new Dictionary<string, MemoryStream>();
            var w = new StandardMeshWriter();
            w.OpenStreamF = name => { var ms = new MemoryStream(); streams[name] = ms; return ms; };
            w.CloseStreamF = s => { };
            WriteOptions o = WriteOptions.Defaults;
            o.bWriteBinary = f.Binary; o.RealPrecisionDigits = f.Digits;
            if (tweak != null) { o.bPerVertexNormals = o.bPerVertexColors = o.bPerVertexUVs = o.bWriteGroups = true; }
            res = w.Write("mesh." + f.Ext, meshes.Select(m => new WriteMesh(m)).ToList(), o);
            return streams.TryGetValue("mesh." + f.Ext, out var main) ? main.ToArray() : new byte[0];
        }

        public static List<DMesh3> Read(Format f, byte[] bytes, out IOReadResult res)
        {
            var b = new DMesh3Builder();
            res = StandardMeshReader.ReadFile(new MemoryStream(bytes), f.Ext, ReadOptions.Defaults, b);
            return b.Meshes;
        }

        static DMesh3 RoundTrip(Format f, DMesh3 m, out string problem, out byte[] bytes)
        {
            problem = "";
            bytes = Write(f, new[] { m }, out IOWriteResult wr);
            if (wr.code != IOCode.Ok) { problem = "write " + wr.code + " " + wr.message; return null; }
            List<DMesh3> l = Read(f, bytes, out IOReadResult rr);
            if (rr.code != IOCode.Ok) { problem = "read " + rr.code + " " + rr.message; return null; }
            if (l.Count != 1) { problem = "meshes read: " + l.Count; return l.Count > 0 ? l[0] : null; }
            return l[0];
        }

        static string SameBits(DMesh3 a, DMesh3 b, double scale = 1)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"counts {a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount}";
            if (a.MaxVertexID != b.MaxVertexID) return "max vertex id";
            foreach (int v in a.VertexIndices()) {
                if (!b.IsVertex(v)) return "vertex id " + v;
                Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v);
                if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {v}: {p.x:R} {p.y:R} {p.z:R} vs {q.x:R} {q.y:R} {q.z:R}";
            }
            foreach (int t in a.TriangleIndices()) {
                if (!b.IsTriangle(t)) return "triangle id " + t;
                Index3i x = a.GetTriangle(t), y = b.GetTriangle(t);
                if (x.a != y.a || x.b != y.b || x.c != y.c) return $"triangle {t}";
            }
            return "";
        }

        static double MaxDiff(DMesh3 a, DMesh3 b)
        {
            if (a.VertexCount != b.VertexCount) return double.PositiveInfinity;
            double d = 0;
            var ia = a.VertexIndices().ToArray(); var ib = b.VertexIndices().ToArray();
            for (int i = 0; i < ia.Length; ++i) d = Math.Max(d, (a.GetVertex(ia[i]) - b.GetVertex(ib[i])).Length);
            return d;
        }

        public static DMesh3 WithGaps(DMesh3 m)
        {
            // the same solid with unused vertex ids and a removed and re-added triangle: ids are not dense
            var g = new DMesh3();
            var map = new Dictionary<int, int>();
            int k = 0;
            var junk = new List<int>();
            foreach (int v in m.VertexIndices()) {
                if (k++ % 3 == 0) junk.Add(g.AppendVertex(new Vector3d(1e3 + k, -7, 5)));
                map[v] = g.AppendVertex(m.GetVertex(v));
            }
            int e0 = g.AppendVertex(new Vector3d(50, 0, 0)), e1 = g.AppendVertex(new Vector3d(51, 0, 0)), e2 = g.AppendVertex(new Vector3d(50, 1, 0));
            int first = g.AppendTriangle(e0, e1, e2);   // a triangle that is removed again, before the real ones: triangle ids start at 1
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                g.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
            g.RemoveTriangle(first, true, false);
            foreach (int v in junk) g.RemoveVertex(v, true, false);
            return g;
        }

        static DMesh3 WithAttributes(DMesh3 m)
        {
            var a = new DMesh3(true, true, true, true);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                int id = a.AppendVertex(new NewVertexInfo(p, new Vector3f(0, 0.6f, 0.8f), new Vector3f((v % 5) / 4f, (v % 3) / 2f, (v % 2)), new Vector2f((v % 7) / 8f, (v % 4) / 4f)));
            }
            foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); a.AppendTriangle(tv, 1 + t % 3); }
            return a;
        }

        static void Attributes()
        {
            DMesh3 a = WithAttributes(Shapes.Sphere(Vector3d.Zero, 1, 4));
            DMesh3 plain = Shapes.Box(new Vector3d(8, 0, 0), Vector3d.One);
            foreach (Format f in Formats) {
                if (f.Name != "obj" && !f.Name.StartsWith("ply") && f.Name != "glb" && f.Name != "gltf" && f.Name != "g3mesh") continue;
                string c = f.Name + " | attributes";
                try {
                    foreach (bool gaps in new[] { false, true }) {
                        DMesh3 src = a;
                        if (gaps) { src = new DMesh3(a); int j = src.AppendVertex(new NewVertexInfo(new Vector3d(9, 9, 9), Vector3f.AxisX, Vector3f.One, Vector2f.Zero)); int k = src.AppendVertex(new NewVertexInfo(new Vector3d(1, 9, 9), Vector3f.AxisX, Vector3f.One, Vector2f.Zero)); int l = src.AppendVertex(new NewVertexInfo(new Vector3d(9, 1, 9), Vector3f.AxisX, Vector3f.One, Vector2f.Zero));
                            int t0 = src.AppendTriangle(j, k, l); src.RemoveTriangle(t0, true, false); var cp = new DMesh3(); src = Reverse(src); }
                        byte[] bytes = Write(f, new[] { src }, out IOWriteResult wr, o => { });
                        if (wr.code != IOCode.Ok) { Program.Report(S, c + " write", wr.code + " " + wr.message); continue; }
                        List<DMesh3> l2 = Read(f, bytes, out IOReadResult rr);
                        if (rr.code != IOCode.Ok || l2.Count != 1) { Program.Report(S, c + " read", rr.code + " " + rr.message + " meshes " + l2.Count); continue; }
                        DMesh3 r = l2[0];
                        string g = gaps ? " (reversed vertex order)" : "";
                        Program.Report(S, c + " counts" + g, r.VertexCount == a.VertexCount && r.TriangleCount == a.TriangleCount ? "" : $"{r.VertexCount}/{r.TriangleCount}");
                        Program.Report(S, c + " has normals" + g, r.HasVertexNormals ? "" : "lost");
                        Program.Report(S, c + " has colors" + g, r.HasVertexColors ? "" : "lost");
                        Program.Report(S, c + " has uvs" + g, r.HasVertexUVs ? "" : "lost");
                        // match vertices by position (the order may be reversed)
                        var byPos = new Dictionary<(float, float, float), int>();
                        foreach (int v in src.VertexIndices()) { Vector3d p = src.GetVertex(v); byPos[((float)p.x, (float)p.y, (float)p.z)] = v; }
                        string bad = "";
                        foreach (int v in r.VertexIndices()) {
                            Vector3d p = r.GetVertex(v);
                            if (!byPos.TryGetValue(((float)p.x, (float)p.y, (float)p.z), out int sv)) { bad = "vertex not found " + v; break; }
                            if (r.HasVertexColors && (r.GetVertexColor(v) - src.GetVertexColor(sv)).Length > 0.01f) { bad = $"color of {v}: {r.GetVertexColor(v)} vs {src.GetVertexColor(sv)}"; break; }
                            if (r.HasVertexUVs && (r.GetVertexUV(v) - src.GetVertexUV(sv)).Length > 1e-6f) { bad = $"uv of {v}: {r.GetVertexUV(v)} vs {src.GetVertexUV(sv)}"; break; }
                            if (r.HasVertexNormals && (r.GetVertexNormal(v) - src.GetVertexNormal(sv)).Length > 1e-6f) { bad = $"normal of {v}"; break; }
                        }
                        Program.Report(S, c + " values" + g, bad);
                        Program.Report(S, c + " closed" + g, Checks.ClosedManifold(r));
                        if (f.Name == "obj" || f.Name == "g3mesh") Program.Report(S, c + " groups" + g, r.HasTriangleGroups && r.TriangleIndices().Select(t => r.GetTriangleGroup(t)).Distinct().Count() == 3 ? "" : "groups lost: " + (r.HasTriangleGroups ? r.TriangleIndices().Select(t => r.GetTriangleGroup(t)).Distinct().Count() : -1));
                    }
                    // a mesh with attributes next to one without
                    byte[] mixed = Write(f, new[] { a, plain }, out IOWriteResult wm, o => { });
                    if (wm.code != IOCode.Ok) { Program.Report(S, c + " mixed write", wm.code + " " + wm.message); continue; }
                    List<DMesh3> lm = Read(f, mixed, out IOReadResult rm);
                    Program.Report(S, c + " mixed read", rm.code == IOCode.Ok ? "" : rm.code + " " + rm.message);
                    if (rm.code == IOCode.Ok) {
                        Program.Report(S, c + " mixed triangles", lm.Sum(x => x.TriangleCount) == a.TriangleCount + plain.TriangleCount ? "" : lm.Sum(x => x.TriangleCount) + "");
                        Program.Report(S, c + " mixed volume", Program.Rel("volume", lm.Sum(x => Checks.Volume(x)), Checks.Volume(a) + Checks.Volume(plain), 1e-5));
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        // the same mesh with the vertices appended in reverse order (attributes follow)
        static DMesh3 Reverse(DMesh3 m)
        {
            var r = new DMesh3(m.HasVertexNormals, m.HasVertexColors, m.HasVertexUVs, m.HasTriangleGroups);
            var map = new Dictionary<int, int>();
            foreach (int v in m.VertexIndices().Reverse()) {
                if (m.GetVtxEdgeCount(v) == 0) continue;
                map[v] = r.AppendVertex(new NewVertexInfo(m.GetVertex(v), m.GetVertexNormal(v), m.GetVertexColor(v), m.GetVertexUV(v)));
            }
            foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); r.AppendTriangle(new Index3i(map[tv.a], map[tv.b], map[tv.c]), m.GetTriangleGroup(t)); }
            return r;
        }

        static void Extremes()
        {
            DMesh3 m = Shapes.Sphere(new Vector3d(0.1, 0.2, 0.3), 1.0, 4);
            foreach (Format f in Formats) {
                if (!f.ExactPositions || !f.HasTriangles) continue;
                foreach (int e in new[] { -300, -100, -40, 40, 100, 300 }) {
                    double s = Math.Pow(2, e);
                    string c = f.Name + " | scale 2^" + e;
                    try {
                        DMesh3 src = Shapes.Map(m, q => q * s);
                        DMesh3 r = RoundTrip(f, src, out string p, out _);
                        if (p != "") { Program.Info(S, c, p); continue; }
                        Program.Report(S, c + " bit for bit", SameBits(src, r));
                    }
                    catch (Exception ex) { Program.Report(S, c, "threw " + ex.GetType().Name + ": " + ex.Message); }
                }
            }
        }

        public static void Run()
        {
            Attributes();
            Extremes();
            var inputs = new (string, DMesh3)[] {
                ("box", Shapes.Box(new Vector3d(0.25, -0.5, 0.125), new Vector3d(1, 0.75, 0.5))),
                ("sphere", Shapes.Sphere(new Vector3d(0.1, 0.2, 0.3), 1.0, 6)),
                ("torus", Shapes.Torus(new Vector3d(0, 0, 0), 1.0, 0.3, 12, 8)),
                ("rotated star", Shapes.Rotate(Shapes.Prism(Shapes.Star(5, 1.0, 0.4), -0.3, 0.3), new Vector3d(1, 2, 3), 37)),
                ("irrational tet", Shapes.Tet(new Vector3d(Math.PI, Math.E, Math.Sqrt(2)), new Vector3d(1.0 / 3, 0.1, 0.7), new Vector3d(-1e-7, 2e5, 1 / 7.0), new Vector3d(1e-3, -1.0 / 9, 123456.789))),
            };
            foreach (Format f in Formats) {
                foreach (var (name, mesh) in inputs) {
                    string c = f.Name + " | " + name;
                    DMesh3 r;
                    try { r = RoundTrip(f, mesh, out string p, out byte[] bytes); Program.Report(S, c + " | round trip status", p); if (r == null) continue;
                        if (!f.HasTriangles) {
                            Program.Report(S, c + " | points kept", r.VertexCount == mesh.VertexCount ? "" : $"{r.VertexCount} of {mesh.VertexCount}");
                            Program.Report(S, c + " | points bit for bit", MaxDiff(mesh, r) == 0 ? "" : "max difference " + MaxDiff(mesh, r).ToString("R"));
                            continue;
                        }
                        Program.Report(S, c + " | triangle count", r.TriangleCount == mesh.TriangleCount ? "" : $"{r.TriangleCount} vs {mesh.TriangleCount}");
                        Program.Report(S, c + " | vertex count", r.VertexCount == mesh.VertexCount ? "" : $"{r.VertexCount} vs {mesh.VertexCount}");
                        Program.Report(S, c + " | closed manifold", Checks.ClosedManifold(r));
                        Program.Report(S, c + " | compact", r.IsCompact ? "" : "the read mesh has gaps");
                        double vol = Checks.Volume(mesh), rv = Checks.Volume(r);
                        Program.Report(S, c + " | volume", Program.Rel("volume", rv, vol, f.ExactPositions ? 1e-13 : 1e-5));
                        if (f.KeepsVertices) {
                            if (f.ExactPositions) Program.Report(S, c + " | bit for bit", SameBits(mesh, r));
                            else Program.Report(S, c + " | positions close", MaxDiff(mesh, r) <= 1e-6 * Checks.Bounds(mesh).MaxDim * 100 ? "" : "max difference " + MaxDiff(mesh, r).ToString("R"));
                        }
                        // second round trip: the identity on the first
                        DMesh3 r2 = RoundTrip(f, r, out string p2, out byte[] bytes2);
                        Program.Report(S, c + " | second round trip status", p2);
                        if (r2 != null) {
                            // glTF stores float positions relative to the bounding-box centre (documented): the centre of the read mesh is
                            // another double, so the second round trip rounds again; STL stores the facet normal of the positions it was
                            // given, which the first read rounded. Both stay within the format's precision: an INFO, and a bound
                            bool lossy = f.Name == "glb" || f.Name == "gltf";
                            string same = SameBits(r, r2);
                            if (lossy) { if (same != "") Program.Info(S, c + " | second round trip", "float positions about another centre: " + same); Program.Report(S, c + " | second round trip close", MaxDiff(r, r2) <= 1e-6 * Checks.Bounds(mesh).MaxDim ? "" : "max difference " + MaxDiff(r, r2).ToString("R")); }
                            else Program.Report(S, c + " | second round trip same mesh", same);
                            bool sameBytes = bytes.SequenceEqual(bytes2);
                            // (3MF: a zip archive, its entries carry the time of the write, so the bytes depend on the clock)
                            if (f.Ext == "3mf") { }
                            else if (lossy || f.Ext == "stl") { if (!sameBytes) Program.Info(S, c + " | second write", "other bytes (normals or the centre of rounded positions)"); }
                            else Program.Report(S, c + " | second write same bytes", sameBytes ? "" : $"bytes differ ({bytes.Length} vs {bytes2.Length})");
                        }
                        // gaps in the ids: the same file as the compact copy
                        DMesh3 gaps = WithGaps(mesh);
                        DMesh3 rg = RoundTrip(f, gaps, out string pg, out byte[] gb);
                        Program.Report(S, c + " | gaps status", pg);
                        if (rg != null) {
                            Program.Report(S, c + " | gaps counts", rg.TriangleCount == mesh.TriangleCount && rg.VertexCount == mesh.VertexCount ? "" : $"{rg.VertexCount}/{rg.TriangleCount} vs {mesh.VertexCount}/{mesh.TriangleCount}");
                            Program.Report(S, c + " | gaps closed manifold", Checks.ClosedManifold(rg));
                            Program.Report(S, c + " | gaps volume", Program.Rel("volume", Checks.Volume(rg), rv, 1e-13));
                        }
                        // 2^k scale
                        foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                            DMesh3 rs = RoundTrip(f, Shapes.Map(mesh, q => q * s), out string ps, out _);
                            Program.Report(S, c + $" | scale {s} status", ps);
                            if (rs == null) continue;
                            string same = SameBits(r, rs, s);
                            // documented: ascii STL and OFF write a fixed number of digits after the point, glTF and 3MF? floats
                            if (f.Name == "off" || f.Name == "stl-ascii") { if (same != "") Program.Info(S, c + $" | scale {s}", "fixed decimals: " + same); else Program.Report(S, c + $" | scale {s} bit for bit", ""); }
                            else Program.Report(S, c + $" | scale {s} bit for bit", same);
                        }
                        // flip
                        var flipped = new DMesh3(mesh); flipped.ReverseOrientation();
                        DMesh3 rf = RoundTrip(f, flipped, out string pf, out _);
                        Program.Report(S, c + " | flip status", pf);
                        if (rf != null) Program.Report(S, c + " | flip volume", Program.Rel("volume", Checks.Volume(rf), -rv, 1e-13));
                        // two meshes in one file
                        DMesh3 far = Shapes.Map(mesh, q => q + new Vector3d(16, 0, 0));
                        byte[] two = Write(f, new[] { mesh, far }, out IOWriteResult w2);
                        if (w2.code != IOCode.Ok) { Program.Report(S, c + " | two meshes write", w2.code + " " + w2.message); continue; }
                        List<DMesh3> l2 = Read(f, two, out IOReadResult r2r);
                        if (r2r.code != IOCode.Ok) { Program.Report(S, c + " | two meshes read", r2r.code + " " + r2r.message); continue; }
                        int tris = l2.Sum(x => x.TriangleCount);
                        Program.Report(S, c + " | two meshes triangles", tris == 2 * mesh.TriangleCount ? "" : $"{tris} vs {2 * mesh.TriangleCount} in {l2.Count} meshes");
                        double v2 = l2.Sum(x => Checks.Volume(x));
                        Program.Report(S, c + " | two meshes volume", Program.Rel("volume", v2, 2 * rv, 1e-6));
                        foreach (DMesh3 x in l2) Program.Report(S, c + " | two meshes closed", Checks.ClosedManifold(x));
                    }
                    catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
                }
            }
        }
    }
}
