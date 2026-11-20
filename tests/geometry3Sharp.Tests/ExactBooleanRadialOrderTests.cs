using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.radial_order, the exact cyclic order of the pieces at an edge that pair_around_edge was split into:
    /// direct tests on pages (single triangles) that share an edge, with the expected order computed independently from
    /// double-precision angles of well-separated half-planes, ties, near-equal half-planes, frames, and the frozen results of
    /// booleans that pair pieces around non-manifold edges (the refactoring must not change a bit of them).
    /// </summary>
    public class ExactBooleanRadialOrderTests
    {
        // a page: the triangle (e0, e1, p) of its own mesh, wound e0 -> e1 or e1 -> e0
        static DMesh3 Page(Vector3d e0, Vector3d e1, Vector3d p, bool forward)
        {
            var m = new DMesh3();
            m.AppendVertex(e0); m.AppendVertex(e1); m.AppendVertex(p);
            if (forward) m.AppendTriangle(0, 1, 2); else m.AppendTriangle(1, 0, 2);
            return m;
        }

        sealed class Setup
        {
            public ExactMeshArrangement Arr;
            public List<Index3i> Tris = new List<Index3i>();
            public int P0, P1;      // the point ids of e0 and e1
            public bool LowIsE0(ExactMeshBoolean.RadialOrder o) { return o.Low == P0; }
        }

        // the pages in the frame f; p[k] = the third point of page k (in the unframed coordinates)
        static Setup Build(Similarity f, Vector3d e0, Vector3d e1, Vector3d[] p, bool[] forward)
        {
            var meshes = new List<DMesh3>();
            for (int k = 0; k < p.Length; ++k)
                meshes.Add(Page(f.Apply(e0), f.Apply(e1), f.Apply(p[k]), forward[k]));
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            var s = new Setup { Arr = arr };
            for (int k = 0; k < p.Length; ++k) {
                Index3i t = meshes[k].GetTriangle(0);
                int[] vp = arr.VertexPoints[k];
                s.Tris.Add(new Index3i(vp[t.a], vp[t.b], vp[t.c]));
            }
            s.P0 = arr.VertexPoints[0][0]; s.P1 = arr.VertexPoints[0][1];
            Assert.NotEqual(s.P0, s.P1);
            for (int k = 1; k < p.Length; ++k)
                Assert.True(arr.VertexPoints[k][0] == s.P0 && arr.VertexPoints[k][1] == s.P1, "the ends of the pages are merged");
            return s;
        }

        // a point at the angle (radians) around the axis from e0 to e1 (angle 0 towards refDir), at the distance r from the axis
        static Vector3d AtAngle(Vector3d e0, Vector3d e1, Vector3d refDir, double angle, double r)
        {
            Vector3d axis = (e1 - e0).Normalized;
            Vector3d a = (refDir - axis * refDir.Dot(axis)).Normalized, b = axis.Cross(a);
            return e0 + 0.3 * (e1 - e0) + r * (Math.Cos(angle) * a + Math.Sin(angle) * b);
        }

        // the pages in the order of increasing angle around low -> high, from the half-plane of page 0 (simple, for separated planes)
        static int[] ExpectedOrder(Vector3d lo, Vector3d hi, Vector3d[] pFramed)
        {
            Vector3d axis = (hi - lo).Normalized;
            Func<Vector3d, Vector3d> perp = x => { Vector3d d = x - lo; return d - axis * d.Dot(axis); };
            Vector3d a = perp(pFramed[0]).Normalized, b = axis.Cross(a);
            var angle = new double[pFramed.Length];
            for (int k = 0; k < pFramed.Length; ++k) {
                Vector3d d = perp(pFramed[k]);
                double t = Math.Atan2(d.Dot(b), d.Dot(a));
                angle[k] = (t < -1e-9) ? t + 2 * Math.PI : Math.Max(t, 0);
            }
            return Enumerable.Range(0, pFramed.Length).OrderBy(k => angle[k]).ThenBy(k => k).ToArray();
        }

        sealed class Result
        {
            public ExactMeshBoolean.RadialOrder Order;
            public int[] Expected;
            public Setup Setup;
        }

        static Result Run(Similarity f, Vector3d e0, Vector3d e1, Vector3d[] p, bool[] forward)
        {
            Setup s = Build(f, e0, e1, p, forward);
            var list = Enumerable.Range(0, p.Length).ToList();
            ExactMeshBoolean.RadialOrder o = ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P0, s.P1, true);
            Vector3d lo = f.Apply(s.LowIsE0(o) ? e0 : e1), hi = f.Apply(s.LowIsE0(o) ? e1 : e0);
            return new Result { Order = o, Expected = ExpectedOrder(lo, hi, p.Select(f.Apply).ToArray()), Setup = s };
        }

        static readonly Vector3d AxisZ0 = new Vector3d(0, 0, 0), AxisZ1 = new Vector3d(0, 0, 4);
        static readonly Vector3d Tilt0 = new Vector3d(1, 2, 3), Tilt1 = new Vector3d(4, 1, 7);

        static Vector3d[] Pages(Vector3d e0, Vector3d e1, double[] degrees)
        {
            Vector3d refDir = Math.Abs((e1 - e0).Normalized.z) > 0.9 ? Vector3d.AxisX : Vector3d.AxisZ;
            return degrees.Select((d, k) => AtAngle(e0, e1, refDir, d * Math.PI / 180, 1 + 0.25 * k)).ToArray();
        }

        static void AssertOrder(Result r, int n)
        {
            Assert.Equal(n, r.Order.Pieces.Length);
            // the axis goes from the smaller to the larger end by ComparePoints (not by id)
            Assert.True(r.Setup.Arr.ComparePoints(r.Order.Low, r.Order.High) < 0);
            Assert.Equal(r.Expected, r.Order.Pieces);
            Assert.Equal(n, r.Order.Direction.Length);
            Assert.DoesNotContain(true, r.Order.SameHalfPlane);
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { new[] { 0.0, 100, 250 } };
            yield return new object[] { new[] { 0.0, 250, 100 } };
            yield return new object[] { new[] { 0.0, 90, 180, 270 } };
            yield return new object[] { new[] { 40.0, 300, 130, 210 } };
            yield return new object[] { new[] { 200.0, 10, 80, 150, 320 } };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Order_AxisAligned_MatchesAngles(double[] degrees)
        {
            var forward = degrees.Select((d, k) => k % 2 == 0).ToArray();
            AssertOrder(Run(Similarity.Identity, AxisZ0, AxisZ1, Pages(AxisZ0, AxisZ1, degrees), forward), degrees.Length);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Order_Tilted_MatchesAngles(double[] degrees)
        {
            var forward = degrees.Select((d, k) => k % 3 != 0).ToArray();
            AssertOrder(Run(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, degrees), forward), degrees.Length);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Order_FarFromTheOrigin_MatchesAngles(double[] degrees)
        {
            // coordinates around 1e7 with binary fractions
            var shift = new Vector3d(10000000.5, -20000000.25, 5000000.125);
            Vector3d e0 = Tilt0 + shift, e1 = Tilt1 + shift;
            var forward = degrees.Select((d, k) => k % 2 == 1).ToArray();
            AssertOrder(Run(Similarity.Identity, e0, e1, Pages(e0, e1, degrees), forward), degrees.Length);
        }

        [Fact]
        public void Direction_FollowsTheWindingOfEachPage()
        {
            double[] deg = { 0, 90, 180, 270 };
            bool[] forward = { true, false, false, true };
            Result r = Run(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), forward);
            AssertOrder(r, 4);
            // +1 if the page runs low -> high; page k runs e0 -> e1 if forward[k]
            bool lowIsE0 = r.Setup.LowIsE0(r.Order);
            for (int k = 0; k < 4; ++k)
                Assert.Equal((forward[r.Order.Pieces[k]] == lowIsE0) ? 1 : -1, r.Order.Direction[k]);
        }

        [Fact]
        public void Order_DoesNotDependOnWhichEndIsU()
        {
            double[] deg = { 0, 100, 200, 300 };
            Setup s = Build(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), new[] { true, true, false, false });
            var list = new List<int> { 0, 1, 2, 3 };
            var a = ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P0, s.P1, true);
            var b = ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P1, s.P0, true);
            Assert.Equal(a.Low, b.Low);
            Assert.Equal(a.High, b.High);
            Assert.Equal(a.Pieces, b.Pieces);
            Assert.Equal(a.Direction, b.Direction);
        }

        [Fact]
        public void Order_StartsAtTheFirstPieceOfTheList()
        {
            double[] deg = { 0, 100, 200, 300 };
            Setup s = Build(Similarity.Identity, AxisZ0, AxisZ1, Pages(AxisZ0, AxisZ1, deg), new[] { true, true, false, false });
            // the same pieces listed from another one: a rotation of the cyclic order
            var o0 = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1, 2, 3 }, s.P0, s.P1, false);
            var o2 = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 2, 0, 1, 3 }, s.P0, s.P1, false);
            Assert.Equal(2, o2.Pieces[0]);
            int shift = Array.IndexOf(o0.Pieces, 2);
            for (int k = 0; k < 4; ++k)
                Assert.Equal(o0.Pieces[(k + shift) % 4], o2.Pieces[k]);
        }

        [Fact]
        public void OnePieceAndTwoPieces_AreOrdered()
        {
            Setup s = Build(Similarity.Identity, AxisZ0, AxisZ1, Pages(AxisZ0, AxisZ1, new[] { 0.0, 120 }), new[] { true, false });
            var one = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 1 }, s.P0, s.P1, true);
            Assert.Equal(new[] { 1 }, one.Pieces);
            Assert.False(one.SameHalfPlane[0]);
            Assert.Equal(new[] { 0, 1 }, ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1 }, s.P0, s.P1, true).Pieces);
            Assert.Equal(new[] { 1, 0 }, ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 1, 0 }, s.P0, s.P1, true).Pieces);
        }

        [Fact]
        public void Tie_TwoPiecesInOneHalfPlane_AreNeighboursByIdAndReported()
        {
            // axis z from (0,0,0) to (0,0,4); pages 1 and 3 in the half-plane towards (-3, 4) (different distances and heights)
            Vector3d[] p = { new Vector3d(1, 0, 1), new Vector3d(-3, 4, 1), new Vector3d(0, -1, 1), new Vector3d(-6, 8, 2.5) };
            Result r = Run(Similarity.Identity, AxisZ0, AxisZ1, p, new[] { true, false, true, true });
            if (r.Setup.LowIsE0(r.Order)) {
                Assert.Equal(new[] { 0, 1, 3, 2 }, r.Order.Pieces);
                Assert.Equal(new[] { false, false, true, false }, r.Order.SameHalfPlane);
            } else {
                Assert.Equal(new[] { 0, 2, 1, 3 }, r.Order.Pieces);
                Assert.Equal(new[] { false, false, false, true }, r.Order.SameHalfPlane);
            }
            Assert.Equal(r.Order.Pieces, r.Expected);

            // the tie also when the pages wind the other way, and in the first piece's own half-plane (a page coincident with page 0)
            Vector3d[] q = { p[0], p[1], p[0], p[2] };
            Result c = Run(Similarity.Identity, AxisZ0, AxisZ1, q, new[] { true, false, false, true });
            Assert.Equal(new[] { 0, 2 }, c.Order.Pieces.Take(2).ToArray());
            Assert.Equal(new[] { false, true }, c.Order.SameHalfPlane.Take(2).ToArray());
            Assert.False(c.Order.SameHalfPlane[2] || c.Order.SameHalfPlane[3]);

            // the half-plane opposite to the first one (angle pi): pages 1 and 2 tie there
            Vector3d[] o = { p[0], new Vector3d(-2, 0, 1), new Vector3d(-1, 0, 3), p[1] };
            Result h = Run(Similarity.Identity, AxisZ0, AxisZ1, o, new[] { true, true, false, true });
            if (h.Setup.LowIsE0(h.Order)) {
                Assert.Equal(new[] { 0, 3, 1, 2 }, h.Order.Pieces);
                Assert.Equal(new[] { false, false, false, true }, h.Order.SameHalfPlane);
            } else {
                Assert.Equal(new[] { 0, 1, 2, 3 }, h.Order.Pieces);
                Assert.Equal(new[] { false, false, true, false }, h.Order.SameHalfPlane);
            }
        }

        [Fact]
        public void Tie_NotReportedUnlessAsked()
        {
            Vector3d[] p = { new Vector3d(1, 0, 1), new Vector3d(-3, 4, 1), new Vector3d(-6, 8, 2) };
            Setup s = Build(Similarity.Identity, AxisZ0, AxisZ1, p, new[] { true, true, false });
            var o = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1, 2 }, s.P0, s.P1, false);
            Assert.Null(o.SameHalfPlane);
            var t = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1, 2 }, s.P0, s.P1, true);
            Assert.Equal(t.Pieces, o.Pieces);
            Assert.Equal(1, t.SameHalfPlane.Count(x => x));
        }

        [Fact]
        public void NearlyEqualHalfPlanes_AreOrderedExactly()
        {
            // pages 1 and 2 differ from page 0 by 2^-45 sideways: far below what the angle of a double sees
            double eps = Math.Pow(2, -45);
            Vector3d[] p = { new Vector3d(1, 0, 1), new Vector3d(1, eps, 2), new Vector3d(1, -eps, 3), new Vector3d(-1, 0, 1) };
            Setup s = Build(Similarity.Identity, AxisZ0, AxisZ1, p, new[] { true, true, true, true });
            var o = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1, 2, 3 }, s.P0, s.P1, true);
            // counterclockwise seen from +z when low is the end at z = 0: +y is the larger angle from +x
            Assert.Equal(s.LowIsE0(o) ? new[] { 0, 1, 3, 2 } : new[] { 0, 2, 3, 1 }, o.Pieces);
            Assert.DoesNotContain(true, o.SameHalfPlane);
        }

        [Fact]
        public void Order_IsTheSameInEveryFrame()
        {
            double[] deg = { 0, 75, 160, 255, 320 };
            var forward = new[] { true, false, true, true, false };
            Result baseline = Run(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), forward);
            AssertOrder(baseline, 5);
            int checkedExact = 0;
            foreach (Similarity f in Similarity.All()) {
                Result r = Run(f, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), forward);
                AssertOrder(r, 5);      // every frame: the separated angles keep their order
                // the frames that keep the input exact (scales by 2^k, the translation by binary fractions): the same bit for bit
                if (f.IsExact || f == Similarity.Utm) {
                    Assert.Equal(baseline.Order.Pieces, r.Order.Pieces);
                    Assert.Equal(baseline.Order.Direction, r.Order.Direction);
                    Assert.Equal(baseline.Setup.LowIsE0(baseline.Order), r.Setup.LowIsE0(r.Order));
                    checkedExact++;
                }
            }
            Assert.True(checkedExact >= 4, checkedExact.ToString());
        }

        // pair_around_edge on a boolean instance whose arrangement is the one of the setup (the method reads only arr)
        static List<KeyValuePair<int, int>> Pair(Setup s, List<int> list)
        {
            var b = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(2, 2, 2, 3, 3, 3), Ops.Union);
            typeof(ExactMeshBoolean).GetField("arr", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(b, s.Arr);
            return b.pair_around_edge(s.Tris, list, s.P0, s.P1);
        }

        [Fact]
        public void Pairing_AdjacentPiecesOfTheSameWinding_AreNotPaired()
        {
            // four pages at 0, 90, 180, 270 degrees; pages 0 and 3 run e0 -> e1, pages 1 and 2 run e1 -> e0: the pages 1 and 2 are
            // neighbours of one winding (a pair needs opposite windings), and 3, 0 are neighbours of one winding as well
            double[] deg = { 0, 90, 180, 270 };
            Setup s = Build(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), new[] { true, false, false, true });
            var pairs = Pair(s, new List<int> { 0, 1, 2, 3 });
            var o = ExactMeshBoolean.radial_order(s.Arr, s.Tris, new List<int> { 0, 1, 2, 3 }, s.P0, s.P1, false);
            Assert.Single(pairs);
            // the piece running low -> high is the key, its predecessor in the order the value
            if (s.LowIsE0(o)) Assert.Equal(new KeyValuePair<int, int>(3, 2), pairs[0]);
            else Assert.Equal(new KeyValuePair<int, int>(2, 3), pairs[0]);
            // all four with the windings alternating: two pairs
            Setup t = Build(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), new[] { true, false, true, false });
            var two = Pair(t, new List<int> { 0, 1, 2, 3 });
            Assert.Equal(2, two.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, two.SelectMany(x => new[] { x.Key, x.Value }).OrderBy(x => x).ToArray());
            // three pieces of one winding: no pair
            Setup u = Build(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, new[] { 0.0, 120, 240 }), new[] { true, true, true });
            Assert.Empty(Pair(u, new List<int> { 0, 1, 2 }));
        }

        [Fact]
        public void Axis_FollowsComparePointsNotTheIds()
        {
            // an edge whose end with the larger id is the smaller point by ComparePoints
            double[] deg = { 0, 100, 200, 300 };
            Setup s = Build(Similarity.Identity, Tilt0, Tilt1, Pages(Tilt0, Tilt1, deg), new[] { true, false, true, false });
            if (s.Arr.ComparePoints(s.P0, s.P1) < 0)
                s = Build(Similarity.Identity, Tilt1, Tilt0, Pages(Tilt1, Tilt0, deg), new[] { true, false, true, false });
            Assert.True(s.P0 < s.P1 && s.Arr.ComparePoints(s.P0, s.P1) > 0, "ids and ComparePoints disagree");
            var list = new List<int> { 0, 1, 2, 3 };
            foreach (var o in new[] { ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P0, s.P1, true), ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P1, s.P0, true) }) {
                Assert.Equal(s.P1, o.Low);
                Assert.Equal(s.P0, o.High);
                // a page that runs P0 -> P1 runs High -> Low
                for (int k = 0; k < 4; ++k) {
                    Index3i f = s.Tris[o.Pieces[k]];
                    bool runsP0P1 = (f.a == s.P0 && f.b == s.P1) || (f.b == s.P0 && f.c == s.P1) || (f.c == s.P0 && f.a == s.P1);
                    Assert.Equal(runsP0P1 ? -1 : 1, o.Direction[k]);
                }
            }
            // the order against the angles, seen from Low = P1
            // (the points of the pages are those of the swapped setup: Low = P1 is the point at Tilt0 there)
            Assert.Equal(ExpectedOrder(Tilt0, Tilt1, Pages(Tilt1, Tilt0, deg)), ExactMeshBoolean.radial_order(s.Arr, s.Tris, list, s.P0, s.P1, false).Pieces);
        }

        // ---- the booleans that pair pieces around non-manifold edges: frozen results ----

        static string Print(DMesh3 m)
        {
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append('/').Append(m.MaxTriangleID).Append('|');
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        static DMesh3 Tetra(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var m = new DMesh3();
            foreach (Vector3d v in new[] { a, b, c, d }) m.AppendVertex(v);
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(0, 3, 2);
            double vol = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d p = Vector3d.Zero, q = Vector3d.Zero, r = Vector3d.Zero;
                m.GetTriVertices(t, ref p, ref q, ref r);
                vol += p.Dot(q.Cross(r)) / 6;
            }
            if (vol < 0) m.ReverseOrientation();
            return m;
        }

        static string Union(Similarity f, DMesh3[] meshes, Similarity back = null)
        {
            var framed = new List<DMesh3>();
            foreach (DMesh3 m in meshes) {
                var c = new DMesh3(m);
                foreach (int v in c.VertexIndices().ToArray())
                    c.SetVertex(v, f.Apply(c.GetVertex(v)));
                framed.Add(c);
            }
            var b = new ExactMeshBoolean(framed, Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Result.IsClosed());
            DMesh3 res = b.Result;
            if (back != null) {
                res = new DMesh3(res);
                foreach (int v in res.VertexIndices().ToArray())
                    res.SetVertex(v, back.Inverse(res.GetVertex(v)));
            }
            return Print(res) + "/" + b.NonManifoldEdges;
        }

        // solids that meet in one line, with 4 to 8 pieces at it (two boxes at an edge, four quadrant boxes of different heights,
        // three wedges, tilted tetrahedra)
        static IEnumerable<DMesh3[]> Solids()
        {
            yield return new[] { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1) };
            yield return new[] { Box(0, 0, 0, 1, 1, 1), Box(-1, 0, 0, 0, 1, 2), Box(0, -1, 0, 1, 0, 3), Box(-1, -1, 0, 0, 0, 1.5) };
            yield return new[] { Box(0, 0, 0, 1, 1, 1), Box(-1, -1, 0, 0, 0, 2), Box(0, -1, 0, 1, 0, 3) };
            Vector3d u = new Vector3d(0.125, 0.25, 0), v = new Vector3d(1.5, 1.25, 1.75);
            Vector3d[] w = { new Vector3d(2.0, -0.5, 0.25), new Vector3d(-1.5, 1.5, 0.5), new Vector3d(-0.5, -2, 1.0), new Vector3d(1.0, 2.5, -0.75) };
            yield return new[] { Tetra(u, v, w[0], w[1]), Tetra(u, v, w[2], w[3]) };
            yield return new[] { Tetra(u, v, w[0], w[1]), Tetra(u, v, w[2], w[3]), Tetra(u, v, new Vector3d(0.5, -3, -2), new Vector3d(-2, -0.5, 3)) };
        }

        static double Ulp(double x, int s)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits + ((x > 0) == (s > 0) ? 1 : -1));
        }

        // tetrahedra (u, v, a, b) and (u, v, c, d) that touch only along the oblique edge u-v, c a few ulps outside the face (u, v, b):
        // 8 pieces at the edge, two half-planes that differ by about 1e-16 radians (as ExactBooleanEdgeOrderTests)
        static IEnumerable<DMesh3[]> NearCoincidentSolids(int count)
        {
            var rand = new Random(7);
            int cases = 0;
            for (int it = 0; it < 400 && cases < count; ++it) {
                Vector3d u = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                Vector3d v = u + new Vector3d(1 + rand.NextDouble(), 0.7 + rand.NextDouble(), 0.3 + rand.NextDouble());
                Vector3d a = new Vector3d(3 * rand.NextDouble(), 3 * rand.NextDouble(), 3 * rand.NextDouble());
                Vector3d b = new Vector3d(3 * rand.NextDouble(), 3 * rand.NextDouble(), 3 * rand.NextDouble());
                int sa = ExactPredicates.Orient3d(u, v, b, a);
                if (sa == 0)
                    continue;
                int k = rand.Next(3);
                Vector3d c = b;
                for (int dir = 0; dir < 2 && ExactPredicates.Orient3d(u, v, b, c) != -sa; ++dir) {
                    c = b;
                    for (int step = 0; step < 30 && ExactPredicates.Orient3d(u, v, b, c) != -sa; ++step)
                        c[k] = Ulp(c[k], (dir == 0) ? 1 : -1);
                }
                if (ExactPredicates.Orient3d(u, v, b, c) != -sa)
                    continue;
                Vector3d n = (v - u).Cross(b - u).Normalized;
                Vector3d d = 0.5 * (u + v) + n * 2.0 * ((ExactPredicates.Orient3d(u, v, b, u + n) == -sa) ? 1 : -1);
                int sc = ExactPredicates.Orient3d(u, v, c, a);
                if (sc == 0 || ExactPredicates.Orient3d(u, v, b, d) != -sa || ExactPredicates.Orient3d(u, v, c, d) != -sc)
                    continue;
                cases++;
                yield return new[] { Tetra(u, v, a, b), Tetra(u, v, c, d) };
            }
        }

        [Fact]
        public void BooleanResults_NearCoincidentHalfPlanes_AreFrozen()
        {
            var prints = new List<string>();
            foreach (DMesh3[] solids in NearCoincidentSolids(24))
                prints.Add(Union(Similarity.Identity, solids));
            Assert.Equal(24, prints.Count);
            string[] frozen = {
                "A097CB5316F2BB1E/1", "3EB31D4E2833040F/1", "03A43AA05D534156/1", "A3E311B797FDCAEE/1",
                "64E5B55F035C6741/1", "15E2B322ED34A8E5/1", "C713FB8249ADED22/1", "02C3B0637A1F187B/1",
                "69EFEBFD8CF49277/1", "6A925633AD00B16D/1", "3EDB79797547DDB6/1", "0A16E6BD4CF431B1/1",
                "8845D4CF8431F72B/1", "0277AC3E7D848987/1", "1F3C214B6C04981B/1", "639BCB4EFD484409/1",
                "DB2CC9CA726CE985/1", "A9A1ACDF9CD2CB1F/1", "4A0ADB1EE95C8756/1", "DCA264523BC5A631/1",
                "69AE6EB7A7340401/1", "D027A15DCB406756/1", "57B999F1E9945A0B/1", "4BE73DE5BFBA4086/1",
            };
            Assert.Equal(string.Join("|", frozen), string.Join("|", prints));
        }

        [Fact]
        public void BooleanResults_AreTheSameInTheExactFrames()
        {
            // mapped back, the results in the frames with exact coordinates (2^k scales, the translation) equal the identity's bit for bit
            // (the box sets touch along edges only: no new vertex, so no rounding; the tetrahedra have non-dyadic coordinates, where a
            // translation rounds the input, so only the per-frame fingerprints above cover them)
            foreach (DMesh3[] solids in Solids().Take(3)) {
                string baseline = Union(Similarity.Identity, solids, Similarity.Identity);
                foreach (Similarity f in new[] { Similarity.Utm, Similarity.Pow2Small, Similarity.Pow2Large })
                    Assert.Equal(baseline, Union(f, solids, f));
            }
        }

        // The prints were taken from the library before radial_order was split out of pair_around_edge. The frames:
        // identity, utm (translation), 2^10 (scale), rotated (inexact coordinates, still deterministic).
        [Fact]
        public void BooleanResults_AroundNonManifoldEdges_AreFrozen()
        {
            var prints = new List<string>();
            foreach (DMesh3[] solids in Solids())
                foreach (Similarity f in new[] { Similarity.Identity, Similarity.Utm, Similarity.Pow2Large, Similarity.Rotated })
                    prints.Add(Union(f, solids));
            string[] frozen = {
                "64FF02BDB71218C2/1", "A6E9C3A6E053DC25/1", "7B1E8F98AC8E6513/1", "846987B7A178B0C1/1",
                "30233F6E4FB1280C/1", "3C0C77B39916C194/1", "392375ADEC234331/1", "3BB605C3E9E8069C/6",
                "55992D5B2CE399A3/0", "FCDE0A34522DC324/0", "BEA58D6A49966EE3/0", "AB8BB4993CAD5A74/2",
                "607FACD73175C0DA/0", "BC118017A1633BFD/0", "EA7BD3BD5621B64E/0", "5CFB5C25F3C31A43/0",
                "AD22A4F804EE576B/0", "3D717AB1BDC7A9D2/0", "D1654F03DCB588F6/0", "E05B686C7BC012B8/0",
            };
            Assert.Equal(string.Join("\n", frozen), string.Join("\n", prints));
        }
    }
}
