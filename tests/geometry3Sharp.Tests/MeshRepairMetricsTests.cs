using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Metrics for repair results without a reference: exact self-intersections (also between neighbours)
    /// and the volume agreement with the input's winding number solid.
    /// </summary>
    public class MeshRepairMetricsTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static DMesh3 Tris(params Vector3d[] v)
        {
            DMesh3 m = new DMesh3();
            for (int i = 0; i < v.Length; ++i) m.AppendVertex(v[i]);
            for (int i = 0; i + 2 < v.Length; i += 3) m.AppendTriangle(i, i + 1, i + 2);
            return m;
        }

        [Fact]
        public void ClosedBox_NoSelfIntersections()
        {
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(Box(Vector3d.Zero, Vector3d.One)));
            var sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(sphere));
        }

        [Fact]
        public void SelfIntersectionsAtMost_StopsAboveTheLimit()
        {
            // two overlapping spheres: many pairs. Up to the count, the full count; below it, more than the limit
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(1.2, 0, 0));
            MeshEditor.Append(m, b);
            int n = MeshRepairMetrics.SelfIntersections(m);
            Assert.True(n > 50);
            Assert.Equal(n, MeshRepairMetrics.SelfIntersectionsAtMost(m, n));
            Assert.Equal(n, MeshRepairMetrics.SelfIntersectionsAtMost(m, 10 * n));
            // (more than the limit; parallel workers can each add a few pairs before they see the count)
            Assert.True(MeshRepairMetrics.SelfIntersectionsAtMost(m, 10) > 10);
            Assert.Equal(n, MeshRepairMetrics.SelfIntersectionsAtMost(m, int.MaxValue));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersectionsAtMost(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(), 0));
        }

        [Fact]
        public void TwoOverlappingBoxes_Intersect()
        {
            DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(m, Box(0.5 * Vector3d.One, 1.5 * Vector3d.One));
            Assert.True(MeshRepairMetrics.SelfIntersections(m) > 0);
            // touching only at a face (separate vertices): touching counts too (not a valid solid surface)
            DMesh3 t = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(t, Box(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1)));
            Assert.True(MeshRepairMetrics.SelfIntersections(t) > 0);
            // apart
            DMesh3 d = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(d, Box(new Vector3d(1.01, 0, 0), new Vector3d(2, 1, 1)));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(d));
        }

        [Fact]
        public void SharedVertex_PiercingCountedTouchingAtTheVertexNot()
        {
            // two triangles sharing vertex 0; the second pierces the first away from the shared vertex
            DMesh3 m = new DMesh3();
            int s = m.AppendVertex(Vector3d.Zero);
            int a = m.AppendVertex(new Vector3d(2, 0, 0)), b = m.AppendVertex(new Vector3d(0, 2, 0));
            int c = m.AppendVertex(new Vector3d(1, 0.2, -1)), d = m.AppendVertex(new Vector3d(0.2, 1, 1));
            m.AppendTriangle(s, a, b);
            m.AppendTriangle(s, c, d);
            Assert.Equal(1, MeshRepairMetrics.SelfIntersections(m));
            // same, but the second triangle only touches at s (above the plane)
            DMesh3 n = new DMesh3();
            s = n.AppendVertex(Vector3d.Zero);
            a = n.AppendVertex(new Vector3d(2, 0, 0)); b = n.AppendVertex(new Vector3d(0, 2, 0));
            c = n.AppendVertex(new Vector3d(-1, 0.2, 1)); d = n.AppendVertex(new Vector3d(0.2, -1, 1));
            n.AppendTriangle(s, a, b);
            n.AppendTriangle(s, c, d);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(n));
        }

        [Fact]
        public void SharedVertex_CoplanarOverlappingCorners()
        {
            DMesh3 m = new DMesh3();
            int s = m.AppendVertex(Vector3d.Zero);
            int a = m.AppendVertex(new Vector3d(2, 0, 0)), b = m.AppendVertex(new Vector3d(0, 2, 0));
            int c = m.AppendVertex(new Vector3d(1, 1, 0)), d = m.AppendVertex(new Vector3d(-1, 2, 0));
            m.AppendTriangle(s, a, b);
            m.AppendTriangle(s, c, d);      // coplanar, its corner overlaps the first one's
            Assert.Equal(1, MeshRepairMetrics.SelfIntersections(m));
            DMesh3 n = new DMesh3();
            s = n.AppendVertex(Vector3d.Zero);
            a = n.AppendVertex(new Vector3d(2, 0, 0)); b = n.AppendVertex(new Vector3d(0, 2, 0));
            c = n.AppendVertex(new Vector3d(-1, 2, 0)); d = n.AppendVertex(new Vector3d(-2, 0, 0));
            n.AppendTriangle(s, a, b);
            n.AppendTriangle(s, c, d);      // coplanar, side by side
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(n));
        }

        [Fact]
        public void SharedEdge_FlatFoldCounted()
        {
            // a fold of 180 degrees: the second triangle lies on the first
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(1, 0, 0));
            int c = m.AppendVertex(new Vector3d(0.5, 1, 0)), d = m.AppendVertex(new Vector3d(0.3, 0.8, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(a, d, b);      // consistent orientation would put d on the other side
            Assert.Equal(1, MeshRepairMetrics.SelfIntersections(m));
            // a regular flat pair
            DMesh3 n = new DMesh3();
            a = n.AppendVertex(Vector3d.Zero); b = n.AppendVertex(new Vector3d(1, 0, 0));
            c = n.AppendVertex(new Vector3d(0.5, 1, 0)); d = n.AppendVertex(new Vector3d(0.5, -1, 0));
            n.AppendTriangle(a, b, c);
            n.AppendTriangle(b, a, d);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(n));
        }

        [Fact]
        public void DegenerateTriangles_Skipped()
        {
            DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
            int a = m.AppendVertex(new Vector3d(-0.5, 0.5, 0.5)), b = m.AppendVertex(new Vector3d(0.5, 0.5, 0.5)), c = m.AppendVertex(new Vector3d(1.5, 0.5, 0.5));
            m.AppendTriangle(a, b, c);      // collinear: zero area
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
        }

        [Fact]
        public void VolumeIoU_IdenticalShiftedAndLostParts()
        {
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            double missing, extra;
            Assert.Equal(1.0, MeshRepairMetrics.VolumeIoU(box, new DMesh3(box), out missing, out extra), 6);
            // shifted by half: overlap 1/2, union 3/2
            DMesh3 shifted = Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1));
            double iou = MeshRepairMetrics.VolumeIoU(box, shifted, out missing, out extra);
            Assert.InRange(iou, 1.0 / 3 - 0.01, 1.0 / 3 + 0.01);
            Assert.InRange(missing, 0.49, 0.51);
            Assert.InRange(extra, 0.49, 0.51);
            // a part lost: two boxes, the result keeps one
            DMesh3 two = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(two, Box(new Vector3d(2, 0, 0), new Vector3d(3, 1, 1)));
            iou = MeshRepairMetrics.VolumeIoU(two, box, out missing, out extra);
            Assert.InRange(iou, 0.49, 0.51);
            Assert.InRange(missing, 0.49, 0.51);
            Assert.True(extra < 1e-9);
        }

        [Fact]
        public void VolumeIoU_AgainstAReferenceSolid()
        {
            // union of two unit boxes overlapping by half, against the analytic union
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One), b = Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1));
            var u = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = 0.05 };
            Assert.True(u.Compute());
            Func<Vector3d, bool> inside = (p) => p.x > 0 && p.x < 1.5 && p.y > 0 && p.y < 1 && p.z > 0 && p.z < 1;
            double missing, extra;
            double iou = MeshRepairMetrics.VolumeIoU(inside, new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1.5, 1, 1)), u.Result, out missing, out extra);
            Assert.True(iou > 0.995, $"IoU {iou}");
            // an empty result: nothing in common
            Assert.Equal(0, MeshRepairMetrics.VolumeIoU(inside, new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One), new DMesh3(), out missing, out extra));
            Assert.Equal(1.0, missing, 9);
            // no reference solid: not defined
            Assert.True(double.IsNaN(MeshRepairMetrics.VolumeIoU((p) => false, new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One), u.Result, out missing, out extra)));
        }

        [Fact]
        public void VolumeIoU_OpenInputUsesItsWindingNumber()
        {
            // a box with the top removed: its winding number solid is (about) the box
            DMesh3 open = Box(Vector3d.Zero, Vector3d.One);
            foreach (int t in open.TriangleIndices().ToArray())
                if (open.GetTriCentroid(t).z > 0.99) open.RemoveTriangle(t);
            double missing, extra;
            double iou = MeshRepairMetrics.VolumeIoU(open, Box(Vector3d.Zero, Vector3d.One), out missing, out extra);
            Assert.True(iou > 0.95, $"IoU {iou}");
        }
    
        [Fact]
        public void VolumeIoU_InsideOutInputCountsAsItsSolid()
        {
            // inside out (winding number -1 inside), closed and open: the repaired, outward box is the same
            // solid (IoU 0 when the raw winding number was the reference)
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 inverted = new DMesh3(box);
            inverted.ReverseOrientation(false);
            DMesh3 invertedOpen = new DMesh3(inverted);
            foreach (int t in invertedOpen.TriangleIndices().ToArray())
                if (invertedOpen.GetTriCentroid(t).z > 0.99) invertedOpen.RemoveTriangle(t);
            double missing, extra;
            Assert.True(MeshRepairMetrics.VolumeIoU(inverted, box, out missing, out extra) > 0.99);
            Assert.True(MeshRepairMetrics.VolumeIoU(invertedOpen, box, out missing, out extra) > 0.95);
            // the input is not changed
            Assert.True(MeshMeasurements.VolumeArea(inverted, inverted.TriangleIndices(), inverted.GetVertex).x < 0);
        }

        static DMesh3 Sphere(Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        [Fact]
        public void VolumeIoU_ComponentsFarApart_LostOneDetected()
        {
            // two unit spheres 1000 apart fill 2e-5 of the common bounds: sampled there, no sample hit them and a
            // result without one of them was accepted (NaN, "no solid"); sampled near the components, it is IoU 0.5
            DMesh3 input = Sphere(Vector3d.Zero);
            MeshEditor.Append(input, Sphere(new Vector3d(1000, 0, 0)));
            DMesh3 one = Sphere(Vector3d.Zero);
            double missing, extra, iou;
            Assert.InRange(MeshRepairMetrics.VolumeIoU(input, one, out missing, out extra), 0.45, 0.55);
            Assert.InRange(missing, 0.45, 0.55);
            Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(input, one, 0.99, out iou));
            Assert.InRange(iou, 0.4, 0.6);
            // the result with both is accepted
            Assert.True(MeshRepairMetrics.VolumeIoU(input, new DMesh3(input), out missing, out extra) > 0.999);
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(input, new DMesh3(input), 0.99, out iou));
        }

        [Fact]
        public void VolumeIoU_UnusedVertexFarAway_Ignored()
        {
            // an unused vertex (eg from an OBJ file) far away made the bounds 170x larger: no sample in the solid, NaN
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            input.AppendVertex(new Vector3d(100, 100, 100));
            DMesh3 result = Box(Vector3d.Zero, Vector3d.One);
            double missing, extra, iou;
            Assert.True(MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra) > 0.999);
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(input, result, 0.99, out iou));
            Assert.True(iou > 0.99);
            // shifted by a tenth: detected
            DMesh3 shifted = Box(new Vector3d(0.1, 0, 0), new Vector3d(1.1, 1, 1));
            Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(input, shifted, 0.99, out iou));
        }

        [Fact]
        public void VolumeIoUAtLeast_SmallInputSolid_LargeResultRejected()
        {
            // input solid 1e-5 of the result's: the first round had no sample in the input and stopped early with
            // NaN, accepted (for most seeds); now rejected for every seed
            DMesh3 input = Box(Vector3d.Zero, 0.02 * Vector3d.One);
            DMesh3 result = Box(Vector3d.Zero, Vector3d.One);
            for (int seed = 1; seed <= 20; ++seed) {
                double iou;
                Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(input, result, 0.99, out iou, seed: seed), $"seed {seed}");
                Assert.True(iou < 0.01, $"seed {seed}: {iou}");
            }
            // a solid so small that no sample of the IoU hits it: 0, not NaN ("no solid")
            double missing, extra;
            DMesh3 tiny = Box(Vector3d.Zero, 0.005 * Vector3d.One);
            Assert.Equal(0, MeshRepairMetrics.VolumeIoU(tiny, result, out missing, out extra));
            Assert.True(double.IsNaN(missing));
            Assert.True(MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra) < 0.01);
        }

        [Fact]
        public void VolumeIoU_TriangleSoup_SampledEverywhereItsSolidIs()
        {
            // a box as 12 separate triangles (12 components): the region near the components must still cover
            // the interior, which no single triangle encloses
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 soup = new DMesh3();
            foreach (int t in box.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                box.GetTriVertices(t, ref a, ref b, ref c);
                soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            }
            DMesh3 far = new DMesh3(soup);
            MeshEditor.Append(far, Box(new Vector3d(500, 0, 0), new Vector3d(501, 1, 1)));
            DMesh3 expected = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(expected, Box(new Vector3d(500, 0, 0), new Vector3d(501, 1, 1)));
            double missing, extra;
            Assert.True(MeshRepairMetrics.VolumeIoU(soup, box, out missing, out extra) > 0.999);
            Assert.True(MeshRepairMetrics.VolumeIoU(far, expected, out missing, out extra) > 0.999);
            Assert.InRange(MeshRepairMetrics.VolumeIoU(far, box, out missing, out extra), 0.45, 0.55);
            // every point of the soup's solid is in the sampled region (its center is half an edge from all triangles)
            var region = MeshRepairMetrics.sample_region(soup, null);
            var tree = new DMeshAABBTree3(soup, true);
            for (int i = 0; i <= 10; ++i)
                for (int j = 0; j <= 10; ++j)
                    for (int k = 0; k <= 10; ++k) {
                        Vector3d p = new Vector3d(i, j, k) / 10;
                        if (tree.FastWindingNumber(p) > 0.5)
                            Assert.True(region.Contains(p), $"{p}");
                    }
        }

        [Fact]
        public void VolumeIoU_ManyComponentsAndAnUnusedVertex_SampledInTheTriangleBounds()
        {
            // more components than MaxSampledComponents: sampled in the bounds of the triangles, which do not
            // include the unused vertex
            DMesh3 input = new DMesh3();
            for (int i = 0; i < MeshRepairMetrics.MaxSampledComponents + 6; ++i)
                MeshEditor.Append(input, Box(new Vector3d(2 * i, 0, 0), new Vector3d(2 * i + 1, 1, 1)));
            DMesh3 result = new DMesh3(input);
            input.AppendVertex(new Vector3d(1e4, 1e4, 1e4));
            double missing, extra, iou;
            Assert.True(MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra) > 0.999);
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(input, result, 0.99, out iou));
        }

        [Fact]
        public void VolumeIoUAtLeast_SmallReferenceSolid_NotDecidedBeforeASampleHitsIt()
        {
            // reference solid 1.25e-4 of the box: often no sample in it in the first round, which ended the check
            // with NaN (accepted); with all samples some are in it
            Func<Vector3d, bool> small = (p) => p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x <= 0.05 && p.y <= 0.05 && p.z <= 0.05;
            DMesh3 result = Box(Vector3d.Zero, Vector3d.One);
            for (int seed = 1; seed <= 20; ++seed) {
                double iou;
                Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(small, result.CachedBounds, result, 0.99, out iou, seed: seed), $"seed {seed}");
                Assert.True(iou < 0.01, $"seed {seed}: {iou}");
            }
        }

        [Fact]
        public void VolumeIoUAtLeast_OpenSheet_NoSolidAccepted()
        {
            DMesh3 sheet = Tris(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY);
            double iou;
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(sheet, Box(Vector3d.Zero, Vector3d.One), 0.99, out iou));
            Assert.True(double.IsNaN(iou));
        }
    }
}
