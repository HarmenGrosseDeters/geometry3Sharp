using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T6: seeded CsCheck property campaign for MeshAlphaWrap (net10.0 only; run protocol and environment variables in
    /// CampaignRunner). Cases are printable scenes: a point cloud, a box, a sphere, a sheet, two boxes (apart or nearly touching), coplanar
    /// points, collinear points with one off, clustered duplicate points or a sliver tetrahedron, of size 2^-6 .. 2^6, at the origin or
    /// moved far from it (up to 2^14 sizes), wrapped at relative alpha 5 .. 40 and relative offset 100 .. 2000, with and without the
    /// non-manifold refinement. Properties: the guarantees of AlphaWrapTests.CheckWrap (closed, 2-manifold, outward, no
    /// self-intersections, on the offset surface, in the band, the input inside); the same wrap again on the same instance and on a new one is
    /// bit-identical; the input scaled by 2^k gives the same wrap scaled by 2^k bit for bit.
    /// Large campaigns (not in CI): G3_PROPERTY_SAMPLES=500 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~AlphaWrapCampaign".
    /// Each failing seed found becomes a fixed example test.
    /// </summary>
    [Trait("Category", CampaignRunner.Category)]
    public class AlphaWrapCampaignTests
    {
        readonly ITestOutputHelper log;
        public AlphaWrapCampaignTests(ITestOutputHelper log) { this.log = log; }

        public sealed class Scene
        {
            public int Kind;        // 0 points, 1 box, 2 sphere, 3 sheet, 4 two boxes, 5 coplanar points, 6 collinear points + 1, 7 clustered duplicates, 8 sliver tetrahedron
            public int Exp;         // half size 2^Exp
            public int N;           // points, 4 .. 40
            public int Gap;         // two boxes: gap 2^-Gap of the size, 1 .. 12; 0: apart by 4 sizes
            public int Far;         // moved by 2^Far sizes along (1, 0.7, 0.3), -1: not moved
            public int RelAlpha;    // 5 .. 40
            public int RelOffset;   // 100 .. 2000
            public bool Refine;
            public int Seed;

            public override string ToString()
            {
                string[] kinds = { "points", "box", "sphere", "sheet", "boxes", "coplanar", "collinear", "clusters", "sliver" };
                return $"{kinds[Kind]}(2^{Exp}, n {N}" + (Kind == 4 ? (Gap == 0 ? ", apart" : $", gap 2^-{Gap}") : "") + (Far >= 0 ? $", far 2^{Far}" : "") +
                       $", alpha 1/{RelAlpha}, offset 1/{RelOffset}, {(Refine ? "refine" : "no refine")}, seed {Seed})";
            }

            static double Unit(ref uint s) { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / (double)0x1000000; }

            static DMesh3 Box(Vector3d c, Vector3d h) => new TrivialBox3Generator() { Box = new Box3d(c, h) }.Generate().MakeDMesh();

            /// <summary> the input: a mesh (Kind 1..4, 8) or a point list (the others) </summary>
            public void Build(int shift, out DMesh3 mesh, out List<Vector3d> points)
            {
                double h = Math.ScaleB(1.0, Exp);
                uint state = (uint)Seed * 2654435761u + 977u;
                mesh = null; points = new List<Vector3d>();
                switch (Kind) {
                    case 0:
                        for (int i = 0; i < N; ++i) points.Add(h * new Vector3d(2 * Unit(ref state) - 1, 2 * Unit(ref state) - 1, 2 * Unit(ref state) - 1));
                        break;
                    case 1: mesh = Box(Vector3d.Zero, new Vector3d(h, 0.6 * h, 0.4 * h)); break;
                    case 2: {
                        mesh = new Sphere3Generator_NormalizedCube() { Radius = h, EdgeVertices = 5 }.Generate().MakeDMesh();
                        break;
                    }
                    case 3: {
                        mesh = new DMesh3();
                        int a = mesh.AppendVertex(new Vector3d(-h, -h, 0)), b = mesh.AppendVertex(new Vector3d(h, -h, 0)), c = mesh.AppendVertex(new Vector3d(0, h, 0));
                        mesh.AppendTriangle(a, b, c);
                        break;
                    }
                    case 4: {
                        double gap = Gap == 0 ? 4 * h : 2 * h * (1 + Math.ScaleB(1.0, -Gap));
                        DMesh3 m1 = Box(Vector3d.Zero, new Vector3d(h, h, h)), m2 = Box(new Vector3d(gap, 0, 0), new Vector3d(h, h, h));
                        mesh = new DMesh3();
                        foreach (DMesh3 m in new[] { m1, m2 }) {
                            var map = new int[m.MaxVertexID];
                            foreach (int v in m.VertexIndices()) map[v] = mesh.AppendVertex(m.GetVertex(v));
                            foreach (int t in m.TriangleIndices()) { Index3i tri = m.GetTriangle(t); mesh.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]); }
                        }
                        break;
                    }
                    case 5:
                        for (int i = 0; i < N; ++i) points.Add(h * new Vector3d(2 * Unit(ref state) - 1, 2 * Unit(ref state) - 1, 0));
                        break;
                    case 6:
                        for (int i = 0; i < N; ++i) points.Add(h * new Vector3d(2 * Unit(ref state) - 1, 0, 0));
                        points.Add(new Vector3d(0, h, 0));
                        break;
                    case 7:
                        for (int i = 0; i < N; ++i) {
                            Vector3d c = h * new Vector3d(i % 3 - 1, (i / 3) % 3 - 1, 0);
                            // (every fourth point is an exact duplicate of the one before)
                            points.Add(i % 4 == 3 ? points[i - 1] : c + Math.ScaleB(h, -20) * new Vector3d(Unit(ref state), Unit(ref state), Unit(ref state)));
                        }
                        break;
                    default: {
                        mesh = new DMesh3();
                        int a = mesh.AppendVertex(new Vector3d(-h, -h, -h)), b = mesh.AppendVertex(new Vector3d(h, -h, -h)), c = mesh.AppendVertex(new Vector3d(-h, h, -h));
                        int d = mesh.AppendVertex(new Vector3d(h / 4, h / 4, -h + Math.ScaleB(h, -10)));
                        mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(a, b, d); mesh.AppendTriangle(a, d, c); mesh.AppendTriangle(b, c, d);
                        break;
                    }
                }
                Vector3d move = Far >= 0 ? Math.ScaleB(h, Far) * new Vector3d(1, 0.7, 0.3) : Vector3d.Zero;
                double s = Math.ScaleB(1.0, shift);
                if (mesh != null) foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, s * (mesh.GetVertex(v) + move));
                for (int i = 0; i < points.Count; ++i) points[i] = s * (points[i] + move);
            }

            public MeshAlphaWrap Wrap(int shift = 0)
            {
                Build(shift, out DMesh3 mesh, out List<Vector3d> points);
                var w = mesh != null ? new MeshAlphaWrap(mesh) : new MeshAlphaWrap(points);
                if (mesh != null && points.Count > 0) w.Points.AddRange(points);
                w.RelativeAlpha = RelAlpha; w.RelativeOffset = RelOffset; w.RefineNonManifold = Refine;
                return w;
            }
        }

        static IList<Vector3d> Samples(MeshAlphaWrap w)
        {
            var l = new List<Vector3d>(w.Points);
            if (w.Mesh != null) l.AddRange(AlphaWrapTests.Vertices(w.Mesh));
            return l;
        }

        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        static string SameMesh(DMesh3 a, DMesh3 b, double scale = 1)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"{a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount} vertices/triangles";
            var va = a.VertexIndices().ToList(); var vb = b.VertexIndices().ToList();
            for (int i = 0; i < va.Count; ++i) {
                Vector3d p = a.GetVertex(va[i]), q = b.GetVertex(vb[i]);
                if (Bits(p.x * scale) != Bits(q.x) || Bits(p.y * scale) != Bits(q.y) || Bits(p.z * scale) != Bits(q.z)) return $"vertex {i} differs: {p} * {scale} vs {q}";
            }
            var ta = a.TriangleIndices().ToList(); var tb = b.TriangleIndices().ToList();
            for (int i = 0; i < ta.Count; ++i) if (a.GetTriangle(ta[i]) != b.GetTriangle(tb[i])) return $"triangle {i} differs";
            return "";
        }

        static string FirstFrame(Exception e)
        {
            string line = (e.StackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains("CheckWrap") || l.Contains("AlphaWrapTests")) ?? (e.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "";
            return line.Trim();
        }

        /// <summary> the property: the guarantees, the repeat on the same instance and on a new one, the 2^k scaled input </summary>
        static string Property(Scene s, int[] shifts)
        {
            MeshAlphaWrap w = s.Wrap();
            if (!w.Compute())
                return "Compute failed: " + w.Status + " " + w.Error;
            try { AlphaWrapTests.CheckWrap(w, Samples(w)); }
            catch (Xunit.Sdk.XunitException e) { return "guarantee: " + e.Message.Split('\n')[0] + " at " + FirstFrame(e); }
            catch (Exception e) { return "exception in the check: " + e.GetType().Name + " " + e.Message.Split('\n')[0] + " at " + FirstFrame(e); }
            DMesh3 first = w.Result;
            if (!w.Compute()) return "the second Compute on the same instance failed: " + w.Status;
            string d = SameMesh(first, w.Result);
            if (d != "") return "the second Compute on the same instance differs: " + d;
            MeshAlphaWrap w2 = s.Wrap();
            if (!w2.Compute()) return "a new instance failed: " + w2.Status;
            d = SameMesh(first, w2.Result);
            if (d != "") return "a new instance differs: " + d;
            foreach (int shift in shifts) {
                MeshAlphaWrap ws = s.Wrap(shift);
                if (!ws.Compute()) return "the input scaled by 2^" + shift + " failed: " + ws.Status;
                d = SameMesh(first, ws.Result, Math.ScaleB(1.0, shift));
                if (d != "") return "the input scaled by 2^" + shift + " differs: " + d;
            }
            return "";
        }

        static readonly Gen<Scene> GenScene =
            Gen.Select(Gen.Select(Gen.Int[0, 8], Gen.Int[-6, 6], Gen.Int[4, 40], Gen.Int[0, 12], Gen.Int[-1, 14], (k, e, n, g, f) => (k, e, n, g, f)),
                       Gen.Select(Gen.OneOfConst(5, 6, 8, 10, 20, 40), Gen.OneOfConst(100, 600, 2000), Gen.Bool, Gen.Int[0, 100000], (a, o, r, sd) => (a, o, r, sd)),
                (x, y) => new Scene { Kind = x.k, Exp = x.e, N = x.n, Gap = x.g, Far = x.f, RelAlpha = y.a, RelOffset = y.o, Refine = y.r, Seed = y.sd });

        static readonly int[] Shifts = { -9, 11 };

        /// <summary>
        /// Flat input at a coarse alpha (found by this campaign): the manifold step moved the huge cells with box corners above
        /// and below a plane of Steiner points inside (box corners on the result, Failed). Seeds 77, 104 and 129 of 30 coplanar
        /// points at relative alpha 5, and seed 1 of 32, with and without the refinement at non-manifold spots
        /// </summary>
        [Theory]
        [InlineData(1, 32, 5, true)]
        [InlineData(1, 32, 5, false)]
        [InlineData(77, 30, 5, true)]
        [InlineData(104, 30, 5, true)]
        [InlineData(129, 30, 5, true)]
        [InlineData(129, 30, 5, false)]
        [InlineData(100, 30, 8, false)]
        public void FlatInput_AtACoarseAlpha_Wraps(int seed, int n, int relAlpha, bool refine)
        {
            var s = new Scene { Kind = 5, Exp = 0, N = n, Far = -1, RelAlpha = relAlpha, RelOffset = 600, Refine = refine, Seed = seed };
            MeshAlphaWrap w = s.Wrap();
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            AlphaWrapTests.CheckWrap(w, Samples(w));
            Assert.Equal(0, w.Counts["result_box_corners"]);
        }

        /// <summary> the retry path of the flat input (box corners on the first result) is also repeatable and scale invariant: 2^k scaled input, a second Compute and a new instance give the same bits </summary>
        [Theory]
        [InlineData(129, false)]
        [InlineData(129, true)]
        public void FlatInput_AtACoarseAlpha_IsRepeatableAndScaleInvariant(int seed, bool refine)
        {
            var s = new Scene { Kind = 5, Exp = 0, N = 30, Far = -1, RelAlpha = 5, RelOffset = 600, Refine = refine, Seed = seed };
            string d = Property(s, Shifts);
            Assert.Equal("", d);
        }

        [Fact]
        public void Wrap()
        {
            CampaignRunner.Run(log, nameof(AlphaWrapCampaignTests), "alphawrap", GenScene, s => Property(s, Shifts), CampaignRunner.Samples(20), 201u);
        }
    }
}
