using System;
using System.Collections.Generic;
using System.Threading;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class MeshMiteredOffsetTests
    {
        readonly ITestOutputHelper output;
        public MeshMiteredOffsetTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Vector3d min, Vector3d max) => DegenerateFamilies.Box(min, max);

        static double Volume(DMesh3 m)
        {
            Vector2d va = MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex);
            return va.x;
        }

        static double AxisAlignedAreaShare(DMesh3 m)
        {
            double total = 0, aligned = 0;
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d n = (b - a).Cross(c - a);
                double len = n.Length;
                if (len <= 0) continue;
                Vector3d u = n / len;
                double area = 0.5 * len;
                total += area;
                if (Math.Max(Math.Abs(u.x), Math.Max(Math.Abs(u.y), Math.Abs(u.z))) > 1.0 - 1e-9)
                    aligned += area;
            }
            return total > 0 ? aligned / total : 0;
        }

        static void AssertClean(DMesh3 m, string label)
        {
            Assert.NotNull(m);
            Assert.True(m.TriangleCount > 0, label + ": empty");
            foreach (int vid in m.VertexIndices())
                Assert.True(m.GetVertex(vid).IsFinite, label + ": non-finite vertex");
            string reason = MeshRobustRepair.NotCleanReason(m, checkSelfIntersections: true);
            if (reason != "") {
                var pairs = MeshRepairMetrics.SelfIntersectionPairs(m);
                var crossings = SolidBooleanPipeline.CrossingTrianglesOfAll(m);
                Assert.True(false, $"{label}: not clean: {reason} (tris={m.TriangleCount}, verts={m.VertexCount}, pairs={pairs.Count}, crossings={crossings.Count})");
            }
        }

        static DMesh3 LShapeSolid()
        {
            var poly = new List<Vector2d> {
                new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5),
                new Vector2d(5, 5), new Vector2d(5, 10), new Vector2d(0, 10)
            };
            var m = new DMesh3();
            int n = poly.Count;
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 0));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 4));
            int[][] quads = { new[] { 0, 1, 2, 3 }, new[] { 0, 3, 4, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[2], q[1]);
                m.AppendTriangle(q[0], q[3], q[2]);
                m.AppendTriangle(n + q[0], n + q[1], n + q[2]);
                m.AppendTriangle(n + q[0], n + q[2], n + q[3]);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(i, j, n + j);
                m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        static DMesh3 OpenFlatPlate(double x0, double x1, double y0, double y1, double z)
        {
            var m = new DMesh3();
            int v0 = m.AppendVertex(new Vector3d(x0, y0, z));
            int v1 = m.AppendVertex(new Vector3d(x1, y0, z));
            int v2 = m.AppendVertex(new Vector3d(x1, y1, z));
            int v3 = m.AppendVertex(new Vector3d(x0, y1, z));
            m.AppendTriangle(v0, v1, v2);
            m.AppendTriangle(v0, v2, v3);
            return m;
        }

        // Open 90-degree CAD L-bracket: horizontal plate [0, lenX] x [0, lenY] at z = 0 + vertical wall at x = 0, z in [0, lenZ].
        // Consistent normals: horizontal faces -z (down/out), vertical faces -x (left/out) so crease along x=0, z=0 is convex on +.
        static DMesh3 OpenLBracket(double lenX = 2.0, double lenY = 1.0, double lenZ = 2.0)
        {
            var m = new DMesh3();
            int v00 = m.AppendVertex(new Vector3d(0, 0, 0));
            int v01 = m.AppendVertex(new Vector3d(0, lenY, 0));
            int vx0 = m.AppendVertex(new Vector3d(lenX, 0, 0));
            int vx1 = m.AppendVertex(new Vector3d(lenX, lenY, 0));
            int vz0 = m.AppendVertex(new Vector3d(0, 0, lenZ));
            int vz1 = m.AppendVertex(new Vector3d(0, lenY, lenZ));
            // Horizontal plate normal = (0, 0, -1): (v01 - v00) x (vx0 - v00) = (0,1,0) x (1,0,0) = (0,0,-1)
            m.AppendTriangle(v00, v01, vx1);
            m.AppendTriangle(v00, vx1, vx0);
            // Vertical wall normal = (-1, 0, 0): (vz0 - v00) x (v01 - v00) = (0,0,1) x (0,1,0) = (-1,0,0)
            m.AppendTriangle(v00, vz0, vz1);
            m.AppendTriangle(v00, vz1, v01);
            return m;
        }

        // Open-top 5-face box [0, s]^3 (missing z = s top face), outward normals.
        static DMesh3 OpenTopBox(double s = 2.0)
        {
            DMesh3 b = Box(Vector3d.Zero, new Vector3d(s, s, s));
            var remove = new List<int>();
            foreach (int tid in b.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, c = Vector3d.Zero, d = Vector3d.Zero;
                b.GetTriVertices(tid, ref a, ref c, ref d);
                if (Math.Abs(a.z - s) < 1e-12 && Math.Abs(c.z - s) < 1e-12 && Math.Abs(d.z - s) < 1e-12)
                    remove.Add(tid);
            }
            foreach (int tid in remove)
                b.RemoveTriangle(tid, bRemoveIsolatedVertices: false, bPreserveManifold: false);
            return b;
        }

        // Triangular prism wedge with an acute apex angle at x = 0 (angleDeg degrees between upper and lower sloped faces).
        static DMesh3 AcuteWedge(double angleDeg, double length = 2.0, double depth = 1.0)
        {
            double halfRad = 0.5 * angleDeg * Math.PI / 180.0;
            double halfH = length * Math.Tan(halfRad);
            var m = new DMesh3();
            int a0 = m.AppendVertex(new Vector3d(0, 0, 0));
            int b0 = m.AppendVertex(new Vector3d(length, 0, -halfH));
            int c0 = m.AppendVertex(new Vector3d(length, 0, halfH));
            int a1 = m.AppendVertex(new Vector3d(0, depth, 0));
            int b1 = m.AppendVertex(new Vector3d(length, depth, -halfH));
            int c1 = m.AppendVertex(new Vector3d(length, depth, halfH));
            // y = 0 front (-y normal) and y = depth back (+y normal)
            m.AppendTriangle(a0, b0, c0);
            m.AppendTriangle(a1, c1, b1);
            // bottom sloped face
            m.AppendTriangle(a0, a1, b1);
            m.AppendTriangle(a0, b1, b0);
            // top sloped face
            m.AppendTriangle(a0, c0, c1);
            m.AppendTriangle(a0, c1, a1);
            // vertical back face at x = length (+x normal)
            m.AppendTriangle(b0, b1, c1);
            m.AppendTriangle(b0, c1, c0);
            return m;
        }

        [Fact]
        public void Outward_UnitCube_PreservesSharp90DegreeCornersAndExactVolume()
        {
            var op = new MeshMiteredOffset {
                Mesh = Box(Vector3d.Zero, Vector3d.One),
                Mode = MeshMiteredOffset.OffsetModes.Outward,
                Distance = 0.25
            };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(MeshMiteredOffset.OffsetStatus.Success, op.Status);
            AssertClean(op.Result, "outward cube");

            AxisAlignedBox3d bb = op.Result.CachedBounds;
            Assert.Equal(-0.25, bb.Min.x, 9); Assert.Equal(-0.25, bb.Min.y, 9); Assert.Equal(-0.25, bb.Min.z, 9);
            Assert.Equal(1.25, bb.Max.x, 9); Assert.Equal(1.25, bb.Max.y, 9); Assert.Equal(1.25, bb.Max.z, 9);
            Assert.Equal(3.375, Volume(op.Result), 8); // (1 + 2*0.25)^3 = 1.5^3
            Assert.True(AxisAlignedAreaShare(op.Result) > 1.0 - 1e-9, "all cube offset faces must remain planar axis-aligned");
        }

        [Fact]
        public void Inward_UnitCube_ShrinksCrisply_AndDeepInsetReturnsEmpty()
        {
            var op = new MeshMiteredOffset {
                Mesh = Box(Vector3d.Zero, Vector3d.One),
                Mode = MeshMiteredOffset.OffsetModes.Inward,
                Distance = 0.2
            };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(MeshMiteredOffset.OffsetStatus.Success, op.Status);
            AssertClean(op.Result, "inward cube");

            AxisAlignedBox3d bb = op.Result.CachedBounds;
            Assert.Equal(0.2, bb.Min.x, 9); Assert.Equal(0.2, bb.Min.y, 9); Assert.Equal(0.2, bb.Min.z, 9);
            Assert.Equal(0.8, bb.Max.x, 9); Assert.Equal(0.8, bb.Max.y, 9); Assert.Equal(0.8, bb.Max.z, 9);
            Assert.Equal(0.216, Volume(op.Result), 8); // 0.6^3
            Assert.True(AxisAlignedAreaShare(op.Result) > 1.0 - 1e-9);

            // Deep inset past half thickness cleanly consumes the solid
            op.Distance = 0.6;
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(MeshMiteredOffset.OffsetStatus.Empty, op.Status);
            Assert.Equal(0, op.Result.TriangleCount);
        }

        [Fact]
        public void Outward_And_Inward_NonConvexLShape_PreservesConvexAndConcaveCreases()
        {
            DMesh3 lshape = LShapeSolid();

            var outOp = new MeshMiteredOffset { Mesh = lshape, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.5 };
            Assert.True(outOp.Compute(), outOp.Error);
            AssertClean(outOp.Result, "outward L-shape");
            // Outer [-0.5, 10.5]^2 (11x11) minus notch [5.5, 10.5]^2 (5x5), height 5 => (121 - 25) * 5 = 480
            Assert.Equal(480.0, Volume(outOp.Result), 6);
            Assert.True(AxisAlignedAreaShare(outOp.Result) > 1.0 - 1e-9);

            var inOp = new MeshMiteredOffset { Mesh = lshape, Mode = MeshMiteredOffset.OffsetModes.Inward, Distance = 0.5 };
            Assert.True(inOp.Compute(), inOp.Error);
            AssertClean(inOp.Result, "inward L-shape");
            // Outer [0.5, 9.5]^2 (9x9) minus notch [4.5, 9.5]^2 (5x5), height 3 => (81 - 25) * 3 = 168
            Assert.Equal(168.0, Volume(inOp.Result), 6);
            Assert.True(AxisAlignedAreaShare(inOp.Result) > 1.0 - 1e-9);
        }

        [Fact]
        public void DoubleSided_OpenCADSheets_FlatPlate_LBracket_And_OpenTopBox()
        {
            // 1. Flat plate [0,1]^2 at z=0 thickened by +-0.1 => exact slab volume 0.2
            var plateOp = new MeshMiteredOffset {
                Mesh = OpenFlatPlate(0, 1, 0, 1, 0),
                Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                Distance = 0.1
            };
            Assert.True(plateOp.Compute(), plateOp.Error);
            AssertClean(plateOp.Result, "flat plate");
            Assert.Equal(0.2, Volume(plateOp.Result), 8);
            Assert.Equal(-0.1, plateOp.Result.CachedBounds.Min.z, 9);
            Assert.Equal(0.1, plateOp.Result.CachedBounds.Max.z, 9);

            // 2. Open 90-degree CAD L-bracket thickened by +-0.1 => exact mitered volume 0.80
            var lOp = new MeshMiteredOffset {
                Mesh = OpenLBracket(2.0, 1.0, 2.0),
                Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                Distance = 0.1
            };
            Assert.True(lOp.Compute(), lOp.Error);
            AssertClean(lOp.Result, "open L-bracket");
            Assert.Equal(0.80, Volume(lOp.Result), 7);
            AxisAlignedBox3d lb = lOp.Result.CachedBounds;
            Assert.Equal(-0.1, lb.Min.x, 9); Assert.Equal(2.0, lb.Max.x, 9);
            Assert.Equal(-0.1, lb.Min.z, 9); Assert.Equal(2.0, lb.Max.z, 9);
            Assert.True(AxisAlignedAreaShare(lOp.Result) > 1.0 - 1e-9);

            // 3. Open-top 5-face box [0,2]^3 thickened by +-0.1 => exact mitered shell volume 4.008
            var uOp = new MeshMiteredOffset {
                Mesh = OpenTopBox(2.0),
                Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                Distance = 0.1
            };
            Assert.True(uOp.Compute(), uOp.Error);
            AssertClean(uOp.Result, "open-top box");
            Assert.Equal(4.008, Volume(uOp.Result), 6);
            Assert.True(AxisAlignedAreaShare(uOp.Result) > 1.0 - 1e-9);

            // 4. Large self-overlapping thickening: d = 0.5 leaves crisp [0.5, 1.5]^2 x [0.5, 2.0] cavity (volume 3*3*2.5 - 1*1*1.5 = 21.0)
            uOp.Distance = 0.5;
            Assert.True(uOp.Compute(), uOp.Error);
            AssertClean(uOp.Result, "open-top box d=0.5");
            Assert.Equal(21.0, Volume(uOp.Result), 5);
            Assert.True(AxisAlignedAreaShare(uOp.Result) > 1.0 - 1e-9);

            // 5. Very large thickening d = 1.2 closes inner cavity completely into [-1.2, 3.2]^2 x [-1.2, 2.0] (volume 4.4^2 * 3.2 = 61.952)
            uOp.Distance = 1.2;
            Assert.True(uOp.Compute(), uOp.Error);
            AssertClean(uOp.Result, "open-top box d=1.2");
            Assert.Equal(61.952, Volume(uOp.Result), 5);
            Assert.True(AxisAlignedAreaShare(uOp.Result) > 1.0 - 1e-9);
        }

        static DMesh3 RegularTetrahedron()
        {
            var m = new DMesh3();
            int v0 = m.AppendVertex(new Vector3d(1, 1, 1));
            int v1 = m.AppendVertex(new Vector3d(1, -1, -1));
            int v2 = m.AppendVertex(new Vector3d(-1, 1, -1));
            int v3 = m.AppendVertex(new Vector3d(-1, -1, 1));
            m.AppendTriangle(v0, v1, v2);
            m.AppendTriangle(v0, v3, v1);
            m.AppendTriangle(v0, v2, v3);
            m.AppendTriangle(v1, v3, v2);
            return m;
        }

        [Fact]
        public void AcuteWedge_And_Tetrahedron_MiterLimit_BevelClampsSpike()
        {
            DMesh3 wedge = AcuteWedge(15.0);
            double d = 0.1;

            var clamped = new MeshMiteredOffset {
                Mesh = wedge,
                Mode = MeshMiteredOffset.OffsetModes.Outward,
                Distance = d,
                MiterLimit = 2.0
            };
            Assert.True(clamped.Compute(), clamped.Error);
            AssertClean(clamped.Result, "clamped acute wedge");
            // Bevel cap restricts apex extrusion in -x to MiterLimit * d = 0.2
            Assert.True(clamped.Result.CachedBounds.Min.x >= -2.0 * d - 1e-6,
                $"expected bevel clamp >= -0.2, got {clamped.Result.CachedBounds.Min.x}");

            var sharp = new MeshMiteredOffset {
                Mesh = wedge,
                Mode = MeshMiteredOffset.OffsetModes.Outward,
                Distance = d,
                MiterLimit = 10.0
            };
            Assert.True(sharp.Compute(), sharp.Error);
            AssertClean(sharp.Result, "sharp acute wedge");
            // Exact miter reaches -d / sin(7.5 deg) ~= -0.766
            Assert.True(sharp.Result.CachedBounds.Min.x < -0.7,
                $"expected sharp miter < -0.7, got {sharp.Result.CachedBounds.Min.x}");

            // Regular tetrahedron: edge miter ratio is sqrt(3) ~= 1.732 < 2.0, while 3-face corner ratio is 3.0 > 2.0.
            // Tests corner-only bevel half-space clamping when no edge is individually bevelled.
            DMesh3 tet = RegularTetrahedron();
            double r0 = Math.Sqrt(3.0);
            var tetSharp = new MeshMiteredOffset { Mesh = tet, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.2, MiterLimit = 4.0 };
            Assert.True(tetSharp.Compute(), tetSharp.Error);
            AssertClean(tetSharp.Result, "sharp tet");
            double maxRSharp = 0;
            foreach (int vid in tetSharp.Result.VertexIndices())
                maxRSharp = Math.Max(maxRSharp, tetSharp.Result.GetVertex(vid).Length);
            Assert.Equal(r0 + 3.0 * 0.2, maxRSharp, 6);

            var tetBevel = new MeshMiteredOffset { Mesh = tet, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.2, MiterLimit = 2.0 };
            Assert.True(tetBevel.Compute(), tetBevel.Error);
            AssertClean(tetBevel.Result, "bevelled tet");
            double maxRBevel = 0;
            foreach (int vid in tetBevel.Result.VertexIndices())
                maxRBevel = Math.Max(maxRBevel, tetBevel.Result.GetVertex(vid).Length);
            Assert.True(maxRBevel < maxRSharp - 0.1, $"expected corner bevel to truncate tet tip: bevel={maxRBevel}, sharp={maxRSharp}");
            Assert.True(Volume(tetBevel.Result) < Volume(tetSharp.Result));
        }

        [Fact]
        public void CompareWithAlphaWrap_OpenCADBracket_CrisperFeaturesAndFarFewerTriangles()
        {
            DMesh3 bracket = OpenLBracket(2.0, 1.0, 2.0);
            double d = 0.05;

            var miter = new MeshMiteredOffset {
                Mesh = bracket,
                Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                Distance = d
            };
            Assert.True(miter.Compute(), miter.Error);
            AssertClean(miter.Result, "miter L-bracket");

            var wrap = new MeshAlphaWrap(bracket) { Offset = d, Alpha = 3.0 * d };
            Assert.True(wrap.Compute(), wrap.Error);
            AssertClean(wrap.Result, "alpha wrap L-bracket");

            double miterPlanar = AxisAlignedAreaShare(miter.Result);
            double wrapPlanar = AxisAlignedAreaShare(wrap.Result);
            double miterVol = Volume(miter.Result);
            double wrapVol = Volume(wrap.Result);
            output.WriteLine($"[Standalone Bracket d={d}] MiteredOffset: tris={miter.Result.TriangleCount}, planar={miterPlanar:P2}, vol={miterVol:F6} | AlphaWrap: tris={wrap.Result.TriangleCount}, planar={wrapPlanar:P2}, vol={wrapVol:F6}");

            // Exact volume of thickened L-bracket at d=0.05: (2.05 * 0.1 + 1.95 * 0.1) * 1.0 = 0.40
            Assert.Equal(0.40, miterVol, 7);
            Assert.True(miterPlanar > 1.0 - 1e-9, $"miter planar share={miterPlanar}");
            Assert.True(wrapPlanar < 0.90, $"expected alpha wrap rounding on edges/corners, got planar share={wrapPlanar}");
            Assert.True(miter.Result.TriangleCount * 5 < wrap.Result.TriangleCount,
                $"expected far fewer triangles than AlphaWrap: miter={miter.Result.TriangleCount}, wrap={wrap.Result.TriangleCount}");

            // End-to-end SolidBooleanPipeline comparison: Union of closed base block with open CAD L-bracket
            DMesh3 baseBlock = Box(new Vector3d(0, 0, -0.5), new Vector3d(2, 1, 0));
            var miterPipe = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = d
            };
            Assert.True(miterPipe.Compute(), miterPipe.Error);
            Assert.True(miterPipe.Report.Clean, miterPipe.Report.NotCleanReason);

            // Compare against AlphaWrap-wrapped bracket unioned with the same base block
            var wrapPipe = new SolidBooleanPipeline(baseBlock, wrap.Result, ExactMeshBoolean.Operations.Union);
            Assert.True(wrapPipe.Compute(), wrapPipe.Error);
            Assert.True(wrapPipe.Report.Clean, wrapPipe.Report.NotCleanReason);

            double miterPipePlanar = AxisAlignedAreaShare(miterPipe.Result);
            double wrapPipePlanar = AxisAlignedAreaShare(wrapPipe.Result);
            output.WriteLine($"[Pipeline Union d={d}] ThickenOpenSheet: tris={miterPipe.Result.TriangleCount}, planar={miterPipePlanar:P2}, vol={Volume(miterPipe.Result):F6} | AlphaWrap+Bool: tris={wrapPipe.Result.TriangleCount}, planar={wrapPipePlanar:P2}, vol={Volume(wrapPipe.Result):F6}");

            Assert.True(miterPipePlanar > 1.0 - 1e-9);
            Assert.True(wrapPipePlanar < 0.96);
            Assert.True(miterPipe.Result.TriangleCount * 5 < wrapPipe.Result.TriangleCount,
                $"pipeline union tris: miter={miterPipe.Result.TriangleCount}, wrap={wrapPipe.Result.TriangleCount}");
        }

        [Fact]
        public void SolidBooleanPipeline_ThickenOpenSheet_Union_Intersection_Difference()
        {
            // Base CAD solid block [0, 2] x [0, 1] x [-0.5, 0] (volume 1.0)
            DMesh3 baseBlock = Box(new Vector3d(0, 0, -0.5), new Vector3d(2, 1, 0));
            // Open L-bracket sitting at z = 0 and rising along x = 0 to z = 1.5
            DMesh3 bracket = OpenLBracket(2.0, 1.0, 1.5);

            // 0. Default InputRepair = Refuse still fails with OpenInput
            var refused = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union);
            Assert.False(refused.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, refused.Status);

            // 1. Union: base block + thickened L-bracket (half-thickness 0.1 => exact total volume 1.50)
            var unionPipe = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(unionPipe.Compute(), unionPipe.Error);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, unionPipe.Status);
            Assert.True(unionPipe.Report.Clean, unionPipe.Report.NotCleanReason);
            Assert.Single(unionPipe.Report.RepairedInputs);
            Assert.Equal("MeshMiteredOffset", unionPipe.Report.RepairedInputs[0].Method);
            Assert.Equal(1.50, Volume(unionPipe.Result), 6);
            Assert.True(AxisAlignedAreaShare(unionPipe.Result) > 1.0 - 1e-9);

            // 2. Intersection: box [-0.5, 1.0] x [0, 1] x [-0.5, 1.0] intersect thickened L-bracket (half-thickness 0.1 => exact 0.40)
            var interPipe = new SolidBooleanPipeline(Box(new Vector3d(-0.5, 0, -0.5), new Vector3d(1.0, 1.0, 1.0)), bracket, ExactMeshBoolean.Operations.Intersection) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(interPipe.Compute(), interPipe.Error);
            Assert.True(interPipe.Report.Clean, interPipe.Report.NotCleanReason);
            Assert.Equal("MeshMiteredOffset", interPipe.Report.RepairedInputs[0].Method);
            Assert.Equal(0.40, Volume(interPipe.Result), 6);

            // 3. Difference: block [-0.5, 2.5] x [0, 1] x [-0.5, 1.5] (volume 6.0) minus open vertical fin plate at x = 1, z in [0, 2]
            //    thickened by +-0.1 => cuts crisp slot x in [0.9, 1.1], y in [0, 1], z in [0, 1.5] (volume 0.30) => remaining 5.70
            var fin = new DMesh3();
            int f0 = fin.AppendVertex(new Vector3d(1.0, -0.2, 0.0));
            int f1 = fin.AppendVertex(new Vector3d(1.0, 1.2, 0.0));
            int f2 = fin.AppendVertex(new Vector3d(1.0, 1.2, 2.0));
            int f3 = fin.AppendVertex(new Vector3d(1.0, -0.2, 2.0));
            fin.AppendTriangle(f0, f1, f2);
            fin.AppendTriangle(f0, f2, f3);
            var diffPipe = new SolidBooleanPipeline(Box(new Vector3d(-0.5, 0, -0.5), new Vector3d(2.5, 1.0, 1.5)), fin, ExactMeshBoolean.Operations.Difference) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(diffPipe.Compute(), diffPipe.Error);
            Assert.True(diffPipe.Report.Clean, diffPipe.Report.NotCleanReason);
            Assert.Single(diffPipe.Report.RepairedInputs);
            Assert.Equal("MeshMiteredOffset", diffPipe.Report.RepairedInputs[0].Method);
            Assert.Equal(5.70, Volume(diffPipe.Result), 6);
            Assert.True(AxisAlignedAreaShare(diffPipe.Result) > 1.0 - 1e-9);

            // 4. Explicit InputRole.Sheet is still respected even when InputRepair = ThickenOpenSheet (cuts half-space, not a slot)
            var cuttingSheet = new DMesh3();
            int s0 = cuttingSheet.AppendVertex(new Vector3d(1.0, -0.2, -1.0));
            int s1 = cuttingSheet.AppendVertex(new Vector3d(1.0, 1.2, -1.0));
            int s2 = cuttingSheet.AppendVertex(new Vector3d(1.0, 1.2, 2.0));
            int s3 = cuttingSheet.AppendVertex(new Vector3d(1.0, -0.2, 2.0));
            // Normal points -x: (s1-s0)x(s2-s0) = (0,1.4,0)x(0,1.4,3) = (+4.2,0,0) -> reverse so normal is -x (keeps x < 1)
            cuttingSheet.AppendTriangle(s0, s2, s1);
            cuttingSheet.AppendTriangle(s0, s3, s2);
            var sheetPipe = new SolidBooleanPipeline(Box(new Vector3d(-0.5, 0, -0.5), new Vector3d(2.5, 1.0, 1.5)), cuttingSheet, ExactMeshBoolean.Operations.Difference) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1,
                InputRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.True(sheetPipe.Compute(), sheetPipe.Error);
            Assert.Empty(sheetPipe.Report.RepairedInputs);
            Assert.Equal(3.0, Volume(sheetPipe.Result), 6); // keeps [-0.5, 1.0] x [0, 1] x [-0.5, 1.5] = 1.5 * 1 * 2 = 3.0

            // 5. Relative default thickness (OpenSheetHalfThickness = 0) works cleanly
            var relPipe = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                RelativeOpenSheetHalfThickness = 0.02
            };
            Assert.True(relPipe.Compute(), relPipe.Error);
            Assert.True(relPipe.Report.Clean, relPipe.Report.NotCleanReason);
            Assert.Equal("MeshMiteredOffset", relPipe.Report.RepairedInputs[0].Method);

            // 6. Invalid ThickenOpenSheet parameters fail cleanly with OpenInput
            var badThick = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = -0.1
            };
            Assert.False(badThick.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, badThick.Status);

            // 7. Closed inputs are never altered by ThickenOpenSheet
            var closedPipe = new SolidBooleanPipeline(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)), ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(closedPipe.Compute(), closedPipe.Error);
            Assert.Empty(closedPipe.Report.RepairedInputs);
            Assert.Equal(1.5, Volume(closedPipe.Result), 7);
        }

        [Fact]
        public void Invariance_AllSimilarities_And_Reuse_And_Determinism()
        {
            DMesh3 bracket = OpenLBracket(2.0, 1.0, 2.0);
            double d = 0.1;

            var baseline = new MeshMiteredOffset {
                Mesh = bracket,
                Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                Distance = d
            };
            Assert.True(baseline.Compute(), baseline.Error);
            AssertClean(baseline.Result, "baseline");
            double v0 = Volume(baseline.Result);

            foreach (Similarity sim in Similarity.All()) {
                var op = new MeshMiteredOffset {
                    Mesh = Invariance.Transformed(bracket, sim),
                    Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                    Distance = sim.ApplyLength(d)
                };
                Assert.True(op.Compute(), sim.Name + ": " + op.Error);
                AssertClean(op.Result, sim.Name);
                double vs = Volume(op.Result) / (sim.Scale * sim.Scale * sim.Scale);
                Assert.True(Math.Abs(vs - v0) <= 1e-6 * v0, $"{sim.Name}: scaled volume {vs} vs {v0}");
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(baseline.Result, Invariance.MappedBack(op.Result, sim)));
            }

            // SolidBooleanPipeline with ThickenOpenSheet across Similarity.All() (explicit thickness is rotation-invariant)
            DMesh3 baseBlock = Box(new Vector3d(0, 0, -0.5), new Vector3d(2, 1, 0));
            double pipeThick = 0.06;
            var pipeBase = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = pipeThick
            };
            Assert.True(pipeBase.Compute(), pipeBase.Error);
            Assert.True(pipeBase.Report.Clean, pipeBase.Report.NotCleanReason);
            double pv0 = Volume(pipeBase.Result);
            foreach (Similarity sim in Similarity.All()) {
                var pipeSim = new SolidBooleanPipeline(Invariance.Transformed(baseBlock, sim), Invariance.Transformed(bracket, sim), ExactMeshBoolean.Operations.Union) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    OpenSheetHalfThickness = sim.ApplyLength(pipeThick)
                };
                Assert.True(pipeSim.Compute(), "pipeline " + sim.Name + ": " + pipeSim.Error);
                Assert.True(pipeSim.Report.Clean, "pipeline " + sim.Name + ": " + pipeSim.Report.NotCleanReason);
                double pvs = Volume(pipeSim.Result) / (sim.Scale * sim.Scale * sim.Scale);
                Assert.True(Math.Abs(pvs - pv0) <= 1e-5 * pv0, $"pipeline {sim.Name}: scaled volume {pvs} vs {pv0}");
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(pipeBase.Result, Invariance.MappedBack(pipeSim.Result, sim)));
            }

            // RelativeOpenSheetHalfThickness is 2^k bit-exact invariant on exact scaling frames
            var relBase = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                RelativeOpenSheetHalfThickness = 0.02
            };
            Assert.True(relBase.Compute(), relBase.Error);
            foreach (Similarity sim in Similarity.All()) {
                if (!sim.IsExact) continue;
                var relSim = new SolidBooleanPipeline(Invariance.Transformed(baseBlock, sim), Invariance.Transformed(bracket, sim), ExactMeshBoolean.Operations.Union) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                    RelativeOpenSheetHalfThickness = 0.02
                };
                Assert.True(relSim.Compute(), "rel " + sim.Name + ": " + relSim.Error);
                Assert.Equal("", Invariance.Difference(relBase.Result, Invariance.MappedBack(relSim.Result, sim)));
            }

            // Reuse on one instance across different modes and distances
            var reuse = new MeshMiteredOffset { Mesh = Box(Vector3d.Zero, Vector3d.One), Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(reuse.Compute(), reuse.Error);
            DMesh3 firstOut = new DMesh3(reuse.Result);
            reuse.Mode = MeshMiteredOffset.OffsetModes.Inward;
            reuse.Distance = 0.2;
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.Equal(0.216, Volume(reuse.Result), 8);
            reuse.Mode = MeshMiteredOffset.OffsetModes.Outward;
            reuse.Distance = 0.25;
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.Equal("", Invariance.Difference(firstOut, reuse.Result));

            // Determinism across repeated runs on the 5-face open-top box
            DMesh3 openBox = OpenTopBox(2.0);
            var det1 = new MeshMiteredOffset { Mesh = openBox, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            var det2 = new MeshMiteredOffset { Mesh = openBox, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            Assert.True(det1.Compute(), det1.Error);
            Assert.True(det2.Compute(), det2.Error);
            Assert.Equal("", Invariance.Difference(det1.Result, det2.Result));
        }

        [Fact]
        public void ErrorAndValidationPaths()
        {
            // Null / empty
            var op = new MeshMiteredOffset { Mesh = null, Distance = 0.1 };
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, op.Status);

            op.Mesh = new DMesh3();
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, op.Status);

            // Invalid Distance / MiterLimit
            op.Mesh = Box(Vector3d.Zero, Vector3d.One);
            op.Distance = 0;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, op.Status);

            op.Distance = -0.1;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, op.Status);

            op.Distance = double.NaN;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, op.Status);

            op.Distance = 0.1;
            op.MiterLimit = 0.5;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, op.Status);
            op.MiterLimit = 4.0;

            // Open mesh in Outward / Inward => NotClosed
            op.Mesh = OpenLBracket();
            op.Mode = MeshMiteredOffset.OffsetModes.Outward;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.NotClosed, op.Status);

            op.Mode = MeshMiteredOffset.OffsetModes.Inward;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.NotClosed, op.Status);

            // Inside-out closed mesh => InsideOut
            DMesh3 flipped = Box(Vector3d.Zero, Vector3d.One);
            flipped.ReverseOrientation();
            op.Mesh = flipped;
            op.Mode = MeshMiteredOffset.OffsetModes.Outward;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InsideOut, op.Status);

            // Non-finite vertex => InvalidInput
            DMesh3 bad = Box(Vector3d.Zero, Vector3d.One);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            op.Mesh = bad;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, op.Status);

            // Pre-cancelled token => Cancelled
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel();
                op.Mesh = Box(Vector3d.Zero, Vector3d.One);
                op.Cancellation = cts.Token;
                Assert.False(op.Compute());
                Assert.Equal(MeshMiteredOffset.OffsetStatus.Cancelled, op.Status);
            }
        }
    }
}
