using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using static geometry3Sharp.Tests.SmoothedHoleFillFoldBaseTests;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>#1507: SmoothedHoleFill (ConstrainToHoleInterior, SmoothAlpha = 1) must not fold over the seam or self-intersect.</summary>
    public class SmoothedHoleFillFoldTests
    {
        // fill triangle count and total area of the clean first attempts, measured on master and with the fix
        const int PinnedCornerTriangles = 364, PinnedCapTriangles = 1156;
        const double PinnedCornerArea = 21.62314108599864, PinnedCapArea = 12.519208818219392;

        public static IEnumerable<object[]> InvarianceCases()
        {
            foreach (string name in new[] { "corner", "cap" })
                foreach (Similarity s in Similarity.All()) yield return new object[] { name, s.Name };
        }

        [Theory]
        [MemberData(nameof(InvarianceCases))]
        public void Fill_SoundInEveryFrame_ExactScalesBitForBit(string name, string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            const double f = 1.5;                       // a fold on master
            DMesh3 reference = Build(name);
            Assert.True(Fill(reference, f, out _));

            DMesh3 m = Invariance.Transformed(Build(name), s);
            int loops = new MeshBoundaryLoops(m).Count;
            Assert.True(Fill(m, f, out SmoothedHoleFill fill));
            AssertSound(name, m, fill, loops, $"{name} {s.Name}");
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(m, s)));
        }

        [Theory]
        [InlineData("corner", 1.5)]
        [InlineData("cap", 0.5)]
        public void Fill_ReuseAndRepeat_SameResult(string name, double f)
        {
            DMesh3 a = Build(name), b = Build(name);
            Assert.True(Fill(a, f, out SmoothedHoleFill fa));
            Assert.True(Fill(b, f, out _));
            Assert.Equal("", Invariance.Difference(a, b));

            // one instance on a second mesh (FillLoop unset: the loop is found again)
            DMesh3 c = Build(name);
            fa.Mesh = c; fa.FillLoop = null;
            Assert.True(fa.Apply());
            Assert.Equal("", Invariance.Difference(a, c));
        }

        // the first attempt of a clean case is kept: the fill of master (the numbers were taken on master)
        [Theory]
        [InlineData("corner", 0.75)]
        [InlineData("cap", 1.0)]
        public void Fill_CleanCase_IsTheFillOfTheFirstAttempt(string name, double f)
        {
            DMesh3 m = Build(name);
            int before = m.TriangleCount;
            Assert.True(Fill(m, f, out SmoothedHoleFill fill));
            var pinned = new Dictionary<string, (int fill, double area)> {
                ["corner"] = (PinnedCornerTriangles, PinnedCornerArea), ["cap"] = (PinnedCapTriangles, PinnedCapArea) };
            Assert.Equal(pinned[name].fill, fill.FillTriangles.Length);
            Assert.True(Math.Abs(Invariance.Area(m) - pinned[name].area) < 1e-9 * pinned[name].area, $"area {Invariance.Area(m):R}");
        }

        // a stray triangle through the cap: every fill intersects it, so Apply() gives up and the hole is as before
        [Fact]
        public void Fill_ThatCannotBeSound_ReturnsFalseAndLeavesTheHole()
        {
            DMesh3 m = SphereCap();
            // a triangle standing through the middle of the hole
            m.AppendTriangle(m.AppendVertex(new Vector3d(-0.2, 0, 0.1)), m.AppendVertex(new Vector3d(0.2, 0, 0.1)), m.AppendVertex(new Vector3d(0, 0, 1.2)));
            // normals that differ from the ones the fill recomputes around the hole
            m.EnableVertexNormals(Vector3f.AxisZ);
            foreach (int v in m.VertexIndices().ToList()) m.SetVertexNormal(v, new Vector3f(1, 0.1f * (v % 7), 0.05f * (v % 5)).Normalized);
            EdgeLoop loop = new MeshBoundaryLoops(m).Loops.OrderByDescending(l => l.VertexCount).First();
            var before = new DMesh3(m);
            var fill = new SmoothedHoleFill(m, loop) { TargetEdgeLength = 1.0 * MeanEdge(loop), SmoothAlpha = 1, ConstrainToHoleInterior = true };
            Assert.False(fill.Apply());
            Assert.Empty(fill.FillTriangles);
            // the input is as before: every vertex and triangle with its id and coordinates, nothing else
            Assert.Equal(before.VertexCount, m.VertexCount);
            Assert.Equal(before.TriangleCount, m.TriangleCount);
            foreach (int v in before.VertexIndices()) Assert.True(m.IsVertex(v) && m.GetVertex(v) == before.GetVertex(v), "vertex " + v);
            foreach (int tid in before.TriangleIndices()) Assert.True(m.IsTriangle(tid) && m.GetTriangle(tid) == before.GetTriangle(tid), "triangle " + tid);
            Assert.True(m.HasVertexNormals);
            foreach (int v in before.VertexIndices()) Assert.True(m.GetVertexNormal(v) == before.GetVertexNormal(v), "normal of vertex " + v);
            Assert.Equal("", Invariance.GeometricDifference(before, m));
            Assert.Equal(loop.Vertices.OrderBy(v => v), new MeshBoundaryLoops(m).Loops.OrderByDescending(l => l.VertexCount).First().Vertices.OrderBy(v => v));
            Assert.Equal(2, new MeshBoundaryLoops(m).Count);          // the hole and the stray triangle
        }


        // a thin wedge (faces 177 degrees apart in normal) with the hole along its edge: the fill must follow the input's own sharpness
        static DMesh3 KnifeEdge()
        {
            double tn = Math.Tan(1.5 * Math.PI / 180);
            var m = new DMesh3();
            int nx = 40, ny = 10;
            int[,] idA = new int[nx + 1, ny + 1], idB = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j) for (int i = 0; i <= nx; ++i) {
                double x = -1 + 2.0 * i / nx, y = 1.0 * j / ny;
                idA[i, j] = m.AppendVertex(new Vector3d(x, y, y * tn));
                idB[i, j] = j == 0 ? idA[i, j] : m.AppendVertex(new Vector3d(x, y, -y * tn));
            }
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) {
                bool hole = Math.Abs(-1 + 2.0 * (i + 0.5) / nx) < 0.3 && j < 2;      // the hole in face A along the edge; face B stays
                if (hole == false) {
                    m.AppendTriangle(idA[i, j], idA[i + 1, j], idA[i + 1, j + 1]); m.AppendTriangle(idA[i, j], idA[i + 1, j + 1], idA[i, j + 1]);
                }
                m.AppendTriangle(idB[i + 1, j], idB[i, j], idB[i + 1, j + 1]); m.AppendTriangle(idB[i, j], idB[i, j + 1], idB[i + 1, j + 1]);
            }
            return new DMesh3(m, true);
        }

        [Theory]
        [InlineData(0.5)] [InlineData(1.0)] [InlineData(2.0)]
        public void Fill_AtAKnifeEdge_IsNotRejectedForTheInputsOwnSharpness(double f)
        {
            DMesh3 m = KnifeEdge();
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m), "input");
            EdgeLoop loop = new MeshBoundaryLoops(m).Loops.OrderBy(l => l.VertexCount).First();      // the hole, not the border
            double s = 0; foreach (int e in loop.Edges) { Index2i ev = m.GetEdgeV(e); s += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); }
            var fill = new SmoothedHoleFill(m, loop) { TargetEdgeLength = f * s / loop.EdgeCount, SmoothAlpha = 1, ConstrainToHoleInterior = true };
            Assert.True(fill.Apply(), "a fill of the knife edge");
            double seam = SeamAngle(m, new HashSet<int>(fill.FillTriangles));
            Assert.True(seam > 170, $"the fixture has a seam beyond the absolute limit: {seam:F1}");
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m));
        }

        [Fact]
        public void FillIsSound_SkipsIdsThatAreNoTriangle_AndFiniteLargeCoordinates()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(0, 1, 0));
            int t = m.AppendTriangle(a, b, c);
            Assert.True(SmoothedHoleFill.FillIsSound(m, new[] { t, 12345, -1 }));
            // finite coordinates whose sum overflows
            DMesh3 big = new DMesh3();
            double h = 1e308;
            int p = big.AppendVertex(new Vector3d(h, h, h)), q = big.AppendVertex(new Vector3d(-h, h, h)), r = big.AppendVertex(new Vector3d(h, -h, h));
            int tb = big.AppendTriangle(p, q, r);
            Assert.True(SmoothedHoleFill.FillIsSound(big, new[] { tb }));
        }

        // a fold with no intersection: the fill triangle lies back over its neighbour at 179.9 degrees
        [Fact]
        public void FillIsSound_SeamFoldWithoutIntersection_IsNotSound()
        {
            DMesh3 m = new DMesh3();
            int v0 = m.AppendVertex(new Vector3d(0, 0, 0)), v1 = m.AppendVertex(new Vector3d(1, 0, 0)), v2 = m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(v0, v1, v2);
            int apex = m.AppendVertex(new Vector3d(0.5, 1, 0.001));
            int fold = m.AppendTriangle(v1, v0, apex);
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m));
            Assert.False(SmoothedHoleFill.FillIsSound(m, new[] { fold }));
            m.SetVertex(apex, new Vector3d(0.5, -1, 0.001));        // the same triangle continuing the neighbour
            Assert.True(SmoothedHoleFill.FillIsSound(m, new[] { fold }));
        }
    }
}
