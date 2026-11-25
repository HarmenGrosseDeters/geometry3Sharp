using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1575: an unwelded, partly flipped soup with holes about one triangle wide (wider than alpha) was returned by MeshRobustRepair as a clean
    /// success that was a thin shell around the surface (1.5 % of the volume, a million wrap triangles for 2k input triangles): the alpha ball
    /// passes through the holes and wraps both sides of the sheet. Procedural input: the repro of the issue (RepairSoups.Issue: two crossing
    /// spheres, every 4th triangle flipped, 8 holes, jitter, duplicates, fins), at a coarser wrap (alpha = diagonal / 100, which shows the
    /// same defect on the base and keeps the test short). Public members only (these tests run against the base library too).
    /// </summary>
    public class RobustRepairHollowWrapTests
    {
        readonly ITestOutputHelper output;
        public RobustRepairHollowWrapTests(ITestOutputHelper output) { this.output = output; }

        public const double RelativeAlpha = 100;

        static MeshRobustRepair Run(DMesh3 soup, double relativeAlpha = RelativeAlpha)
        {
            var r = new MeshRobustRepair(soup) { SolidifyResolution = 80, WrapRelativeAlpha = relativeAlpha, WrapRelativeOffset = 10 * relativeAlpha };
            r.Apply();
            return r;
        }

        // sphere of radius 0.5 whose triangles above the plane z = cut (by centroid) are removed: an open bowl, its rim as long as the sphere's circle
        public static DMesh3 Bowl(int edgeVertices, double cut)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 0.5, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            var remove = new List<int>();
            foreach (int tid in m.TriangleIndices())
                if (m.GetTriCentroid(tid).z > cut)
                    remove.Add(tid);
            foreach (int tid in remove)
                m.RemoveTriangle(tid, true);
            return m;
        }

        [Fact]
        public void IssueRepro_Sphere_IsASolid_NotAHollowShell()
        {
            DMesh3 soup = RepairSoups.MakeSoup(RepairSoupParams.Issue("sphere", 10), out _, out double known);
            Assert.Equal(2132, soup.TriangleCount);
            MeshRobustRepair r = Run(soup);
            double volume = RepairSoups.Volume(r.Mesh), area = RepairSoups.Area(r.Mesh);
            output.WriteLine($"path {r.ResultPath} ok-reason '{r.ResultReason}' volume {volume} known {known} area {area} raw {r.AlphaWrapTriangles}");
            Assert.True(r.ResultReason == "", r.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh, true));
            Assert.True(r.Mesh.IsClosed());
            Assert.True(r.Mesh.VertexIndices().All(v => MathUtil.IsFinite(r.Mesh.GetVertex(v).x + r.Mesh.GetVertex(v).y + r.Mesh.GetVertex(v).z)));
            Assert.True(r.UsedAlphaWrap, "the repro reaches the alpha wrap");
            // a solid of the known volume: the wrap lies outside the input by up to alpha + offset (a hollow shell had 4 % of it)
            double outside = area * (r.AlphaWrap.UsedAlpha + r.AlphaWrap.UsedOffset);
            Assert.InRange(volume, 0.97 * known, known + outside);
        }

        // a flat n x n grid of unit-square size, open (no solid)
        public static DMesh3 Plate(int n)
        {
            var plate = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    plate.AppendVertex(new Vector3d(i / (double)n, j / (double)n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    plate.AppendTriangle(a, b, d);
                    plate.AppendTriangle(a, d, c);
                }
            return plate;
        }

        [Fact]
        public void OpenBowl_IsNotAClean_HollowShell()
        {
            // a spherical cap with a rim far longer than any hole: both sides of it are wrapped, a thin shell of a few % of the volume under it, not a repair
            DMesh3 bowl = Bowl(8, -0.2);
            Assert.False(bowl.IsClosed());
            var r = new MeshRobustRepair(bowl) { SolidifyResolution = 80, WrapRelativeAlpha = 50, WrapRelativeOffset = 500 };
            bool ok = r.Apply();
            double volume = RepairSoups.Volume(r.Mesh), area = RepairSoups.Area(r.Mesh);
            output.WriteLine($"ok {ok} path {r.ResultPath} reason '{r.ResultReason}' volume {volume} area {area}");
            Assert.True(r.UsedAlphaWrap, "the bowl reaches the alpha wrap");
            bool shell = volume <= 2 * area * r.AlphaWrap.UsedOffset;
            Assert.True(shell, "setup: the wrap is a thin shell");
            Assert.False(ok, "a thin shell around an open surface was returned as a success");
            Assert.NotEqual("", r.ResultReason);
        }
    }
}
