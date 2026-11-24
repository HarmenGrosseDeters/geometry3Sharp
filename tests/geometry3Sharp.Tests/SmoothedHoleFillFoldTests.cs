using System;
using System.Collections.Generic;
using System.Linq;
using g3;
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

        // box corner: super-ellipsoid sphere, triangles within 0.8 of (1,1,1) removed
        internal static DMesh3 BoxCorner()
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 24 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) {
                Vector3d d = m.GetVertex(v);
                double s = Math.Pow(Math.Pow(Math.Abs(d.x), 14) + Math.Pow(Math.Abs(d.y), 14) + Math.Pow(Math.Abs(d.z), 14), 1.0 / 14);
                m.SetVertex(v, d / s);
            }
            Vector3d c = new Vector3d(1, 1, 1);
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).Distance(c) < 0.8) m.RemoveTriangle(t, true, false);
            return new DMesh3(m, true);
        }

        internal static double MeanBoundaryEdge(DMesh3 m)
        {
            var l = new MeshBoundaryLoops(m).Loops[0];
            double s = 0; foreach (int e in l.Edges) { Index2i ev = m.GetEdgeV(e); s += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); }
            return s / l.EdgeCount;
        }


        // max angle (deg) between the normals of the two triangles at a seam edge (fill vs input); 180 = folded back
        internal static double SeamAngle(DMesh3 m, HashSet<int> fill)
        {
            double worst = 0;
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e);
                if (et.b == DMesh3.InvalidID) continue;
                if (fill.Contains(et.a) == fill.Contains(et.b)) continue;
                double ang = Vector3d.AngleD(m.GetTriNormal(et.a), m.GetTriNormal(et.b));
                worst = Math.Max(worst, ang);
            }
            return worst;
        }

        static DMesh3 Grid(int nu, int nv, bool wrapU, Func<int, int, Vector3d> pos, Func<Vector3d, bool> removeCentroid)
        {
            var m = new DMesh3();
            int cu = wrapU ? nu : nu + 1;
            for (int j = 0; j <= nv; ++j) for (int i = 0; i < cu; ++i) m.AppendVertex(pos(i, j));
            Func<int, int, int> id = (i, j) => (j * cu) + (i % cu);
            for (int j = 0; j < nv; ++j) for (int i = 0; i < nu; ++i) {
                int a = id(i, j), b = id(i + 1, j), c = id(i + 1, j + 1), d = id(i, j + 1);
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            foreach (int t in m.TriangleIndices().ToList())
                if (removeCentroid(m.GetTriCentroid(t))) m.RemoveTriangle(t, true, false);
            return new DMesh3(m, true);
        }

        internal static DMesh3 SphereCap()
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 20 }; g.Generate();
            DMesh3 m = g.MakeDMesh();
            foreach (int t in m.TriangleIndices().ToList()) { Vector3d c = m.GetTriCentroid(t); if (c.z > 0.55 * c.Length) m.RemoveTriangle(t, true, false); }
            return new DMesh3(m, true);
        }

        internal static DMesh3 TubeSlot() => Grid(64, 40, true, (i, j) => {
            double th = 2 * Math.PI * i / 64, z = -1.5 + 3.0 * j / 40; return new Vector3d(Math.Cos(th), Math.Sin(th), z);
        }, c => { double th = Math.Atan2(c.y, c.x); return Math.Abs(th) < 0.5 && Math.Abs(c.z) < 0.35 + 0.1 * Math.Sin(7 * th); });

        internal static DMesh3 PlateNotch() => Grid(40, 40, false, (i, j) => {
            double x = -1 + 2.0 * i / 40, y = -1 + 2.0 * j / 40; return new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(3 * y));
        }, c => (Math.Abs(c.x) < 0.45 && Math.Abs(c.y) < 0.45) && !(c.x > 0.05 && c.y > 0.05));

        internal static EdgeLoop SmallestLoop(DMesh3 m) => new MeshBoundaryLoops(m).Loops.OrderBy(l => l.VertexCount).First();


        static DMesh3 Build(string n) => n == "corner" ? BoxCorner() : n == "cap" ? SphereCap() : n == "slot" ? TubeSlot() : PlateNotch();
        static bool IsClosedCase(string n) => n == "corner" || n == "cap";

        static double MeanEdge(EdgeLoop loop)
        {
            DMesh3 m = loop.Mesh; double s = 0;
            foreach (int e in loop.Edges) { Index2i ev = m.GetEdgeV(e); s += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); }
            return s / loop.EdgeCount;
        }

        // the issue's settings: ConstrainToHoleInterior, SmoothAlpha = 1, TargetEdgeLength f * mean boundary edge
        static bool Fill(DMesh3 m, double f, out SmoothedHoleFill fill)
        {
            EdgeLoop loop = SmallestLoop(m);
            fill = new SmoothedHoleFill(m, loop) { TargetEdgeLength = f * MeanEdge(loop), SmoothAlpha = 1, ConstrainToHoleInterior = true };
            return fill.Apply();
        }

        static void AssertSound(string name, DMesh3 m, SmoothedHoleFill fill, int loopsBefore, string what)
        {
            Assert.True(Invariance.AllFinite(m), what + ": finite");
            Assert.Equal(loopsBefore - 1, new MeshBoundaryLoops(m).Count);
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly), what + ": valid");
            if (IsClosedCase(name))
                Assert.Equal("", MeshRobustRepair.NotCleanReasons(m));
            else
                Assert.False(MeshRepairMetrics.HasSelfIntersections(m), what + ": self-intersections");
            double seam = SeamAngle(m, new HashSet<int>(fill.FillTriangles));
            Assert.True(seam <= 170, $"{what}: seam fold {seam:F1} degrees");
        }

        // the cases of the issue; the rows with f >= 1 (and some below) self-intersect or fold on master
        public static IEnumerable<object[]> SoundCases()
        {
            foreach (double f in new[] { 0.5, 0.75, 1.0, 1.5, 2.0, 4.0 }) yield return new object[] { "corner", f };
            foreach (double f in new[] { 0.35, 0.5, 0.75, 1.0, 1.5, 2.0 }) yield return new object[] { "cap", f };
            foreach (double f in new[] { 0.5, 1.5, 2.0 }) yield return new object[] { "slot", f };
            foreach (double f in new[] { 0.35, 1.0, 2.0 }) yield return new object[] { "plate", f };
        }

        [Theory]
        [MemberData(nameof(SoundCases))]
        public void Fill_HasNoFoldAndNoSelfIntersection(string name, double f)
        {
            DMesh3 m = Build(name);
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m), "input");
            int loops = new MeshBoundaryLoops(m).Count;
            Assert.True(Fill(m, f, out SmoothedHoleFill fill));
            AssertSound(name, m, fill, loops, $"{name} f={f}");
        }

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
            Assert.Equal("", Invariance.GeometricDifference(before, m));
            Assert.Equal(loop.Vertices.OrderBy(v => v), new MeshBoundaryLoops(m).Loops.OrderByDescending(l => l.VertexCount).First().Vertices.OrderBy(v => v));
            Assert.Equal(2, new MeshBoundaryLoops(m).Count);          // the hole and the stray triangle
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
