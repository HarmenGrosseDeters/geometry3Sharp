using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Paths that the scheduled changed-line coverage run reported as not executed by the ordinary tier (round 3): the
    /// two-child nodes of DMeshAABBTree3.collect_triangles, the degenerate-edge collapse of MeshPlaneCut at extreme tolerances,
    /// the outside-of-the-star answer of MeshAlphaWrap, and the projection of ExactMeshBoolean's boundary segments from a far
    /// point with a query point behind that point. Every test checks a result against an independent computation.
    /// </summary>
    public class CoverageRound3Tests
    {
        // ---------------------------------------------------------------- DMeshAABBTree3.collect_triangles

        // the protected collectors of the tree, called from a subclass
        sealed class ExposedTree : DMeshAABBTree3
        {
            public ExposedTree(DMesh3 mesh) : base(mesh, true) { }
            public int Boxes => BoxCountForTests;
            public int Root => root_index;
            public List<int> CollectList(int box)
            {
                var l = new List<int>();
                collect_triangles(box, l);
                return l;
            }
            public HashSet<int> CollectHash(int box)
            {
                var h = new HashSet<int>();
                collect_triangles(box, h);
                return h;
            }
        }

        [Fact]
        public void CollectTriangles_ListOverload_EveryBoxGivesItsTrianglesOnce_AsTheHashOverload()
        {
            // the cache build of the fast winding number (threshold 1) always has a list for a node with two children, so only a
            // direct call descends through such nodes: the collection below each box equals the hash version's, without repeats
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 7 }.Generate().MakeDMesh();
            var tree = new ExposedTree(mesh);
            Assert.True(tree.Boxes > 20);
            List<int> all = tree.CollectList(tree.Root);
            Assert.Equal(mesh.TriangleCount, all.Count);
            Assert.Equal(mesh.TriangleIndices().OrderBy(t => t).ToArray(), all.OrderBy(t => t).ToArray());
            int twoChildBoxes = 0;
            for (int b = 0; b < tree.Boxes; ++b) {
                List<int> l = tree.CollectList(b);
                Assert.Equal(l.Count, l.Distinct().Count());
                HashSet<int> h = tree.CollectHash(b);
                Assert.True(h.SetEquals(l), $"box {b}");
                if (l.Count > 4)
                    twoChildBoxes++;
            }
            Assert.True(twoChildBoxes > 5);
        }

        // ---------------------------------------------------------------- MeshPlaneCut

        // a fan of six triangles around c in the plane z = 0 (scaled), c split into c and c2 at the same place: two zero-area
        // triangles join the two halves of the fan along the edge c - c2 of length 0
        static DMesh3 FanWithAZeroLengthEdge(double scale)
        {
            var m = new DMesh3();
            int c = m.AppendVertex(Vector3d.Zero), c2 = m.AppendVertex(Vector3d.Zero);
            var r = new int[6];
            for (int i = 0; i < 6; ++i)
                r[i] = m.AppendVertex(scale * new Vector3d(Math.Cos(i * Math.PI / 3), Math.Sin(i * Math.PI / 3), 0));
            for (int i = 0; i < 6; ++i)
                Assert.True(m.AppendTriangle(i < 3 ? c : c2, r[i], r[(i + 1) % 6]) >= 0);
            Assert.True(m.AppendTriangle(c, r[3], c2) >= 0);
            Assert.True(m.AppendTriangle(c2, r[0], c) >= 0);
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            return m;
        }

        static double EdgeLength(DMesh3 m, int e)
        {
            Index2i ev = m.GetEdgeV(e);
            return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e160)]
        public void PlaneCut_ZeroLengthCutEdge_IsCollapsedAlsoWhereTheSquaredLengthsOverflow(double scale)
        {
            // coordinates above 1e154 square to infinity: the edge test then runs on the half difference scaled by its largest
            // component, and an edge of length exactly 0 (no largest component) is not longer than the tolerance. The ring edges
            // are longer than the tolerance (1e-6 of the diagonal) and stay, the zero-length edge is collapsed: the same
            // fan of six triangles at both scales
            DMesh3 m = FanWithAZeroLengthEdge(scale);
            Assert.True(m.EdgeIndices().Any(e => EdgeLength(m, e) == 0));
            var cut = new MeshPlaneCut(m, Vector3d.Zero, new Vector3d(0, 0, 1));
            Assert.True(cut.Cut());
            Assert.Equal(6, m.TriangleCount);
            Assert.Equal(7, m.VertexCount);
            Assert.Equal(12, m.EdgeCount);
            foreach (int e in m.EdgeIndices())
                Assert.True(EdgeLength(m, e) > 0, "a zero-length edge is left");
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
        }

        // ---------------------------------------------------------------- MeshAlphaWrap

        [Fact]
        public void AlphaWrap_LocalIndex_AnswersFalseForACellOutsideTheStar_AndTheStampedPositionForOneInside()
        {
            // local_index maps a cell to its position in the star being fixed while the cell's stamp is the star's; the
            // star_components and the manifold step only ask for neighbours across facets of the star, so a cell without the
            // stamp (or beyond the array) is answered directly here, on arrays set up as fix_star sets them
            var w = new MeshAlphaWrap(new[] { Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ });
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            int[] stamp = new int[10], pos = new int[10];
            int value = 3;
            stamp[4] = value; pos[4] = 7;
            stamp[5] = value - 1; pos[5] = 2;
            typeof(MeshAlphaWrap).GetField("localStamp", f).SetValue(w, stamp);
            typeof(MeshAlphaWrap).GetField("localPos", f).SetValue(w, pos);
            typeof(MeshAlphaWrap).GetField("localStampValue", f).SetValue(w, value);
            MethodInfo local_index = typeof(MeshAlphaWrap).GetMethod("local_index", f);
            // (a cell beyond the array)
            object[] args = { stamp.Length + 5, 0 };
            Assert.Equal(false, local_index.Invoke(w, args));
            Assert.Equal(-1, args[1]);
            // (a cell whose stamp is not the current one)
            args = new object[] { 5, 0 };
            Assert.Equal(false, local_index.Invoke(w, args));
            Assert.Equal(-1, args[1]);
            // (a stamped cell: its recorded position)
            args = new object[] { 4, 0 };
            Assert.Equal(true, local_index.Invoke(w, args));
            Assert.Equal(7, args[1]);
        }

        // ---------------------------------------------------------------- ExactMeshBoolean.SegmentProjection

        [Fact]
        public void SegmentProjection_QueryPointBehindTheFarPoint_IsNotProjected_AndTheQueryDeclines()
        {
            // segments on one side of the plane x = R_x (the axis along which R is farthest), a far point R at x = 4000: a point
            // on the other side of that plane does not project (the projection would not map hulls to hulls), the query then
            // returns null (every segment is a candidate), and a query of points on the segments' side returns the ones near
            var r = new Random(3);
            var segs = new List<Vector3d>();
            for (int s = 0; s < 40; ++s) {
                var a = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                segs.Add(a); segs.Add(a + 0.01 * new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()));
            }
            var R = new Vector3d(4000, 100, 50);
            var pr = new ExactMeshBoolean.SegmentProjection(segs.ToArray(), R);
            Assert.True(pr.Valid);
            Assert.Equal(0, pr.Axis);

            double px, py, pe;
            Assert.True(pr.Project(new Vector3d(0.1, 0.1, 0.1), out px, out py, out pe));
            Assert.True(pe > 0 && pe < 1e-3);
            Assert.False(pr.Project(new Vector3d(5000, 0, 0), out px, out py, out pe));
            Assert.True(double.IsNaN(pe));
            // (on the plane itself: not away from it either)
            Assert.False(pr.Project(new Vector3d(4000, 0, 0), out px, out py, out pe));

            Assert.Null(pr.Query(new[] { new Vector3d(5000, 0, 0) }));
            Assert.Null(pr.Query(new[] { new Vector3d(0, 0, 0), new Vector3d(0.2, 0.1, 0), new Vector3d(4500, 1, 1) }));
            // (the same corners without the point behind R: a proper subset of the segments, ascending, containing the one
            // through the first corner)
            Vector3d corner = segs[10];
            int[] near = pr.Query(new[] { corner, corner + new Vector3d(0.001, 0, 0), corner + new Vector3d(0, 0.001, 0) });
            Assert.NotNull(near);
            Assert.Contains(10, near);
            Assert.True(near.Length < segs.Count / 2);
            Assert.Equal(near.OrderBy(s => s).ToArray(), near);
        }

        // ---------------------------------------------------------------- ExactMeshBoolean.LocalFrame.InPlane

        // a flat open sheet z = 0 over [-2, 2]^2 in n x n cells
        static DMesh3 Sheet(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(-2 + 4.0 * i / n, -2 + 4.0 * j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        [Fact]
        public void LocalFrame_InPlane_OfAnInputTrianglesOwnFrame_IsTrueExactlyForTheSheetsBoundarySegmentsInItsPlane()
        {
            // the boundary segments of the open sheet z = 0 lie in the plane of every sheet triangle and in no plane of the
            // box [-1, 1]^3 (its faces are at x, y, z = +-1); the frame of an input triangle answers for its own plane
            DMesh3 sheet = Sheet(4), box = Box(-1, -1, -1, 1, 1, 1);
            var s = new ExactMeshBoolean(sheet, box, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var exact = (Dictionary<int, ExactPoint3[]>)typeof(ExactMeshBoolean).GetField("antipodalExact", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
            Assert.Single(exact);
            int m = exact.Keys.Single();
            int segments = exact[m].Length / 2;
            Assert.Equal(16, segments);
            var arr = s.Arrangement;
            int sheetTriangles = 0, boxTriangles = 0;
            foreach (var tri in arr.Triangles) {
                if (tri.Degenerate)
                    continue;
                var frame = new ExactMeshBoolean.LocalFrame(s, tri);
                bool onSheet = tri.Mesh == m;
                if (onSheet) sheetTriangles++; else boxTriangles++;
                for (int t = 0; t < 2 * segments; t += 2) {
                    Assert.Equal(onSheet, frame.InPlane(m, t));
                    // (cached: the same answer again)
                    Assert.Equal(onSheet, frame.InPlane(m, t));
                }
            }
            Assert.Equal(sheet.TriangleCount, sheetTriangles);
            Assert.Equal(12, boxTriangles);
        }

        // ---------------------------------------------------------------- input coordinates beyond a double's sum

        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 OpenSphere(Vector3d c, double r, int ev, Vector3d dir, double cut)
        {
            DMesh3 m = Sphere(c, r, ev);
            foreach (int t in m.TriangleIndices().ToList())
                if ((m.GetTriCentroid(t) - c).Normalized.Dot(dir.Normalized) > cut)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        static DMesh3 ScaledAndMoved(DMesh3 m, double scale, Vector3d offset)
        {
            DMesh3 r = new DMesh3(m);
            foreach (int v in r.VertexIndices().ToList())
                r.SetVertex(v, r.GetVertex(v) * scale + offset);
            return r;
        }

        [Fact]
        public void TracedAcrossEdges_InputCoordinatesWhoseSumOverflows_StepsFallBackToTheOwnRaysWithTheSameResult()
        {
            // a step between two input triangles asks M's box tree for the triangles near the segment in input coordinates
            // (input_segment); coordinates near 1e308 make the sum of the segment's ends infinite, the box-tree query declines,
            // and every step falls back to the piece's own ray. At the unit scale the same steps are found by the tree. The
            // classification at the large scale equals the per-piece one (an independent path, every piece with its own ray)
            DMesh3 a = Sphere(Vector3d.Zero, 16, 5), b = OpenSphere(new Vector3d(4.8, 6.4, 8), 11.2, 6, new Vector3d(0, 0, 1), 0.7);
            var unit = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(unit.Compute(), unit.Error);
            Assert.True(unit.Counts["l2_cross_steps"] > 0);
            Assert.Equal(0, unit.Counts["l2_cross_fallbacks"]);
            Assert.True(unit.Counts["l2_cross_candidates"] > 0);

            double f = Math.Pow(2.0, 1000), offset = 1.0e308;
            var o = new Vector3d(offset, offset, offset);
            DMesh3 A = ScaledAndMoved(a, f, o), B = ScaledAndMoved(b, f, o);
            var big = new ExactMeshBoolean(A, B, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(big.Compute(), big.Error);
            Assert.True(big.Counts["l2_cross_steps"] > 0);
            Assert.Equal(big.Counts["l2_cross_steps"], big.Counts["l2_cross_fallbacks"]);
            Assert.Equal(0, big.Counts["l2_cross_candidates"]);
            Assert.True(Invariance.AllFinite(big.Result) && big.Result.TriangleCount > 0);

            var perPiece = new ExactMeshBoolean(A, B, Ops.Union) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = false };
            Assert.True(perPiece.Compute(), perPiece.Error);
            Assert.Equal(perPiece.Result.TriangleCount, big.Result.TriangleCount);
            Assert.Equal("", Invariance.Difference(perPiece.Result, big.Result));
            // (reuse: the instance computes the same again)
            Assert.True(big.Compute(), big.Error);
            Assert.Equal("", Invariance.Difference(perPiece.Result, big.Result));
        }
    }
}
