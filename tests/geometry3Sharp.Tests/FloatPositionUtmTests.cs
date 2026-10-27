using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Positions cast to float inside double operations (docs/ROBUSTNESS_SWEEP.md S3, S4, S5): at UTM coordinates a float
    /// is off by up to 0.03 (x ~ 6e5) and 0.25 (y ~ 5.7e6). MeshUtil.ScaleMesh, MeshLoopClosure and
    /// MeshInsertProjectedPolygon now map positions through their Frame3f in double.
    /// </summary>
    public class FloatPositionUtmTests
    {
        static readonly Vector3d Utm = Similarity.Utm.Translation;

        [Fact]
        public void ScaleMesh_AtUtm_KeepsTheVertices()
        {
            // scale 1 must not move anything; scale 2 doubles the offsets from the frame origin
            DMesh3 m = Defects.Sphere(Utm + new Vector3d(0.123, 0.456, 0.789), 1, 6);
            var f = new Frame3f(Utm);
            Vector3d o = f.Origin;
            DMesh3 same = new DMesh3(m), doubled = new DMesh3(m);
            MeshUtil.ScaleMesh(same, f, Vector3f.One);
            MeshUtil.ScaleMesh(doubled, f, 2 * Vector3f.One);
            foreach (int v in m.VertexIndices()) {
                Assert.True(same.GetVertex(v).Distance(m.GetVertex(v)) < 1e-9, $"vertex {v} moved by {same.GetVertex(v).Distance(m.GetVertex(v)):E2}");
                Vector3d expected = o + 2 * (m.GetVertex(v) - o);
                Assert.True(doubled.GetVertex(v).Distance(expected) < 1e-9, $"vertex {v}: {doubled.GetVertex(v)} vs {expected}");
            }
        }

        static DMesh3 ClosedFlat(Vector3d offset)
        {
            // a sphere with its top cut off, closed flat against a plane above it (Close_Flat projects along the frame's Y)
            Vector3d c = offset + new Vector3d(0.123, 0.456, 0.789);
            DMesh3 m = Defects.Sphere(c, 1, 8);
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).z > c.z + 0.6) m.RemoveTriangle(t, true, false);
            m = new DMesh3(m, true);
            var plane = new Frame3f(c + new Vector3d(0, 0, 1.25), Vector3d.AxisZ);
            plane = new Frame3f(plane.Origin, Vector3f.AxisZ, 1);
            var close = new MeshLoopClosure(m, new MeshBoundaryLoops(m).Loops[0]) { FlatClosePlane = plane };
            Assert.True(close.Close());
            ResultChecks.AssertValid(m);
            return m;
        }

        [Fact]
        public void LoopClosure_AtUtm_SameAsAtTheOrigin()
        {
            // the extrusion and the loop smoothing project onto the plane: in float that moved every new vertex by up to
            // 0.25 at UTM coordinates; now the result is the one at the origin, translated
            DMesh3 a = ClosedFlat(Vector3d.Zero), b = ClosedFlat(Utm);
            MeshTransforms.Translate(b, -Utm);
            Assert.Equal(a.VertexCount, b.VertexCount);
            double worst = a.VertexIndices().Max(v => a.GetVertex(v).Distance(b.GetVertex(v)));
            // (the plane is a Frame3f: its float origin is rounded differently in the two placements, by up to 4e-6 in z)
            Assert.True(worst < 1e-5, $"vertices differ by up to {worst:E2}");
        }

        static DMesh3 FlatGrid(Vector3d origin, int n, double size)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(origin + new Vector3d(size * i / n, size * j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        [Fact]
        public void InsertProjectedPolygon_AtUtm_VerticesOnThePolygon()
        {
            // a square inserted into a flat grid: every inserted vertex lies on the square's edges
            Vector3d origin = Utm + new Vector3d(0.123, 0.456, 0);
            DMesh3 m = FlatGrid(origin, 20, 2.0);
            Vector3d center = origin + new Vector3d(1, 1, 0);
            var square = new DCurve3(new List<Vector3d> {
                center + new Vector3d(-0.37, -0.41, 0), center + new Vector3d(0.43, -0.39, 0),
                center + new Vector3d(0.41, 0.38, 0), center + new Vector3d(-0.36, 0.42, 0) }, true);
            var tree = new DMeshAABBTree3(m, true);
            int seed = tree.FindNearestTriangle(center);
            var frame = new Frame3f(center, Vector3d.AxisZ);
            var insert = new MeshInsertProjectedPolygon(m, square, frame, seed) { RemovePolygonInterior = false };
            Assert.True(insert.Insert());
            double worst = 0;
            foreach (int v in insert.InsertedPolygonVerts) {
                Vector3d p = m.GetVertex(v);
                double d = double.MaxValue;
                for (int k = 0; k < 4; ++k) {
                    var seg = new Segment3d(square[k], square[(k + 1) % 4]);
                    d = Math.Min(d, seg.DistanceSquared(p));
                }
                worst = Math.Max(worst, Math.Sqrt(d));
            }
            Assert.True(worst < 1e-6, $"inserted vertices up to {worst:E2} off the polygon");
        }
    }
}
