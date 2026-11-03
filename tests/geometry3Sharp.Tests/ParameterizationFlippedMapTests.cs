using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// LSCM and ARAP must not report Success for a map with flipped UV triangles (finding P1 in
    /// docs/ISSUE_CASE_FINDINGS.md; issue https://github.com/GeometryCollective/boundary-first-flattening/issues/97).
    /// Through the public API without the new status, so it also runs against libraries without it.
    /// </summary>
    public class ParameterizationFlippedMapTests
    {
        static DMesh3 OpenCylinder(int sides, int rings)
        {
            var m = new DMesh3();
            for (int j = 0; j <= rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    double a = 2 * Math.PI * i / sides;
                    m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0.6 * j));
                }
            for (int j = 0; j < rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            return m;
        }

        // UV triangles whose signed area is not positive (the surface triangles all have area)
        static int NotPositive(DMesh3 m, Vector2d[] uv)
        {
            int n = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                Vector2d a = uv[tri.a], b = uv[tri.b], c = uv[tri.c];
                if ((b - a).x * (c - a).y - (b - a).y * (c - a).x <= 0) n++;
            }
            return n;
        }

        [Fact]
        public void LowPolyCylinder_LscmAndArapWithFlippedMap_NotSuccess()
        {
            // an open cylinder of 8 sides and 3 rings (an annulus without a seam): LSCM collapses the unpinned ring and
            // ARAP keeps flipped triangles; neither may say Success, and a map they keep shows the flips
            DMesh3 m = OpenCylinder(8, 3);
            foreach (var (name, r) in new[] { ("lscm", new LSCMParameterization(m).Compute()), ("arap", new ARAPParameterization(m).Compute()) }) {
                Assert.True(r.Status != ParameterizationStatus.Success, $"{name}: Success with a flipped map");
                Assert.False(r.Succeeded, name);
                if (r.UV != null)
                    Assert.True(NotPositive(m, r.UV) > 0, $"{name}: the kept map has no flipped triangle");
            }
        }
    }
}
