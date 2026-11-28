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

        static double Volume(DMesh3 m) => Invariance.Volume(m);

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
                Assert.Fail($"{label}: not clean: {reason} (tris={m.TriangleCount}, verts={m.VertexCount}, pairs={pairs.Count}, crossings={crossings.Count})");
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

            // 6. Invalid ThickenOpenSheet parameters fail upfront with InvalidInput (also on closed inputs)
            var badThick = new SolidBooleanPipeline(baseBlock, bracket, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = -0.1
            };
            Assert.False(badThick.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, badThick.Status);

            var badMiterClosed = new SolidBooleanPipeline(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)), ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetMiterLimit = 0.5
            };
            Assert.False(badMiterClosed.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, badMiterClosed.Status);

            // 7. Closed inputs are never altered by ThickenOpenSheet
            var closedPipe = new SolidBooleanPipeline(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)), ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                OpenSheetHalfThickness = 0.1
            };
            Assert.True(closedPipe.Compute(), closedPipe.Error);
            Assert.Empty(closedPipe.Report.RepairedInputs);
            Assert.Equal(1.5, Volume(closedPipe.Result), 7);
        }

        static DMesh3 Unweld(DMesh3 src)
        {
            var dst = new DMesh3();
            foreach (int tid in src.TriangleIndices()) {
                Index3i t = src.GetTriangle(tid);
                int a = dst.AppendVertex(src.GetVertex(t.a));
                int b = dst.AppendVertex(src.GetVertex(t.b));
                int c = dst.AppendVertex(src.GetVertex(t.c));
                dst.AppendTriangle(a, b, c);
            }
            return dst;
        }

        static DMesh3 AcuteCone(int segments, double halfAngleDeg, double height = 2.0)
        {
            double r = height * Math.Tan(halfAngleDeg * Math.PI / 180.0);
            var m = new DMesh3();
            int apex = m.AppendVertex(new Vector3d(0, 0, height));
            int center = m.AppendVertex(new Vector3d(0, 0, 0));
            var ring = new int[segments];
            for (int i = 0; i < segments; ++i) {
                double ang = 2.0 * Math.PI * i / segments;
                ring[i] = m.AppendVertex(new Vector3d(r * Math.Cos(ang), r * Math.Sin(ang), 0));
            }
            for (int i = 0; i < segments; ++i) {
                int j = (i + 1) % segments;
                // Side face outward: (ring[i] -> ring[j] -> apex)
                m.AppendTriangle(ring[i], ring[j], apex);
                // Bottom face outward (-z): (center -> ring[j] -> ring[i])
                m.AppendTriangle(center, ring[j], ring[i]);
            }
            return m;
        }

        static DMesh3 HollowBox(double outer = 2.0, double inner = 1.0)
        {
            DMesh3 m = Box(new Vector3d(-outer, -outer, -outer), new Vector3d(outer, outer, outer));
            DMesh3 cav = Box(new Vector3d(-inner, -inner, -inner), new Vector3d(inner, inner, inner));
            cav.ReverseOrientation();
            var vmap = new Dictionary<int, int>();
            foreach (int vid in cav.VertexIndices())
                vmap[vid] = m.AppendVertex(cav.GetVertex(vid));
            foreach (int tid in cav.TriangleIndices()) {
                Index3i t = cav.GetTriangle(tid);
                m.AppendTriangle(vmap[t.a], vmap[t.b], vmap[t.c]);
            }
            return m;
        }

        [Fact]
        public void UnweldedSoups_And_InconsistentOpenSheets_MatchWeldedConsistentResults()
        {
            // 1. Unwelded STL-like closed cube produces exact outward offset (no missing edge/corner notches)
            DMesh3 cube = Box(Vector3d.Zero, Vector3d.One);
            var opWelded = new MeshMiteredOffset { Mesh = cube, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            var opSoup = new MeshMiteredOffset { Mesh = Unweld(cube), Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(opWelded.Compute(), opWelded.Error);
            Assert.True(opSoup.Compute(), opSoup.Error);
            AssertClean(opSoup.Result, "unwelded outward cube");
            Assert.Equal(3.375, Volume(opSoup.Result), 8);
            Assert.Equal("", Invariance.Difference(opWelded.Result, opSoup.Result));

            // 2. Unwelded open CAD L-bracket produces exact DoubleSided mitered shell
            DMesh3 bracket = OpenLBracket(2.0, 1.0, 2.0);
            var bWelded = new MeshMiteredOffset { Mesh = bracket, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            var bSoup = new MeshMiteredOffset { Mesh = Unweld(bracket), Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            Assert.True(bWelded.Compute(), bWelded.Error);
            Assert.True(bSoup.Compute(), bSoup.Error);
            AssertClean(bSoup.Result, "unwelded L-bracket");
            Assert.Equal(0.80, Volume(bSoup.Result), 7);
            Assert.Equal("", Invariance.Difference(bWelded.Result, bSoup.Result));

            // 3. Inconsistently oriented open CAD L-bracket (one plate flipped) produces identical DoubleSided result
            DMesh3 flippedSheet = new DMesh3(bracket);
            // Reverse the vertical wall triangles (tids 2 and 3)
            Index3i t2 = flippedSheet.GetTriangle(2);
            Index3i t3 = flippedSheet.GetTriangle(3);
            flippedSheet.RemoveTriangle(2, false, false);
            flippedSheet.RemoveTriangle(3, false, false);
            flippedSheet.AppendTriangle(t2.a, t2.c, t2.b);
            flippedSheet.AppendTriangle(t3.a, t3.c, t3.b);
            var bInconsistent = new MeshMiteredOffset { Mesh = flippedSheet, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            Assert.True(bInconsistent.Compute(), bInconsistent.Error);
            AssertClean(bInconsistent.Result, "inconsistent L-bracket");
            Assert.Equal(0.80, Volume(bInconsistent.Result), 7);
            Assert.True(AxisAlignedAreaShare(bInconsistent.Result) > 1.0 - 1e-9);
        }

        [Fact]
        public void HighValenceCone_MiterLimit_BevelClampsArbitraryValence()
        {
            // 18-sided acute pyramid/cone (valence 18 > old 12/16 cutoffs, circum-half-angle 15 deg):
            // face apothem half-angle has tan(theta) = tan(15 deg) * cos(pi/18) => exact tip = 2.0 + d / sin(theta)
            double d = 0.1;
            const int Segments = 18;
            DMesh3 cone = AcuteCone(Segments, 15.0, height: 2.0);
            var coneSharp = new MeshMiteredOffset { Mesh = cone, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = d, MiterLimit = 5.0 };
            Assert.True(coneSharp.Compute(), coneSharp.Error);
            AssertClean(coneSharp.Result, "18-sided sharp cone");
            double tanFace = Math.Tan(15.0 * Math.PI / 180.0) * Math.Cos(Math.PI / Segments);
            double sinFace = tanFace / Math.Sqrt(1.0 + tanFace * tanFace);
            double expectedTipZ = 2.0 + d / sinFace;
            Assert.True(Math.Abs(coneSharp.Result.CachedBounds.Max.z - expectedTipZ) < 1e-6,
                $"expected 18-sided cone sharp tip {expectedTipZ:F6}, got {coneSharp.Result.CachedBounds.Max.z:F6}");

            var coneBevel = new MeshMiteredOffset { Mesh = cone, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = d, MiterLimit = 2.0 };
            Assert.True(coneBevel.Compute(), coneBevel.Error);
            AssertClean(coneBevel.Result, "18-sided bevelled cone");
            // Bevel cap clamps tip cleanly at 2.0 + MiterLimit * d = 2.2 (must NOT collapse below 2.0 + d = 2.1)
            Assert.Equal(2.2, coneBevel.Result.CachedBounds.Max.z, 3);
            Assert.True(Volume(coneBevel.Result) < Volume(coneSharp.Result));
        }

        [Fact]
        public void BowtieVertex_RazorFold_And_MiterLimitOne()
        {
            // 1. Bowtie vertex: two cubes sharing (0,0,0) with second cube rotated 45 deg around Z
            DMesh3 bowtie = Box(new Vector3d(-1, -1, -1), Vector3d.Zero);
            DMesh3 c2 = Box(Vector3d.Zero, Vector3d.One);
            double ang = Math.PI / 4.0;
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            var vmap = new Dictionary<int, int>();
            foreach (int vid in c2.VertexIndices()) {
                Vector3d p = c2.GetVertex(vid);
                Vector3d rp = new Vector3d(ca * p.x - sa * p.y, sa * p.x + ca * p.y, p.z);
                vmap[vid] = bowtie.AppendVertex(rp);
            }
            foreach (int tid in c2.TriangleIndices()) {
                Index3i t = c2.GetTriangle(tid);
                bowtie.AppendTriangle(vmap[t.a], vmap[t.b], vmap[t.c]);
            }
            var bowtieOp = new MeshMiteredOffset { Mesh = bowtie, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.True(bowtieOp.Compute(), bowtieOp.Error);
            AssertClean(bowtieOp.Result, "bowtie cubes");
            // First cube [-1.1, 0.1]^3 and second rotated cube must both extend cleanly to their full offset bounds
            Assert.True(bowtieOp.Result.CachedBounds.Min.z <= -1.1 + 1e-6);
            Assert.True(bowtieOp.Result.CachedBounds.Max.z >= 1.1 - 1e-6);

            // 2. Exact 180-degree razor fold (z = 0 opposing coplanar sheets sharing edge x = 0, y in [0, 1])
            var exactFold = new DMesh3();
            int e0 = exactFold.AppendVertex(new Vector3d(0, 0, 0));
            int e1 = exactFold.AppendVertex(new Vector3d(0, 1, 0));
            int e2 = exactFold.AppendVertex(new Vector3d(1, 0, 0));
            int e3 = exactFold.AppendVertex(new Vector3d(1, 1, 0));
            int e4 = exactFold.AppendVertex(new Vector3d(0.8, 0, 0));
            int e5 = exactFold.AppendVertex(new Vector3d(0.8, 1, 0));
            exactFold.AppendTriangle(e0, e1, e3);
            exactFold.AppendTriangle(e0, e3, e2);
            exactFold.AppendTriangle(e1, e0, e4);
            exactFold.AppendTriangle(e1, e4, e5);
            var foldM1 = new MeshMiteredOffset { Mesh = exactFold, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1, MiterLimit = 1.0 };
            Assert.True(foldM1.Compute(), foldM1.Error);
            AssertClean(foldM1.Result, "exact 180-deg razor fold MiterLimit=1");
            Assert.Equal(-0.1, foldM1.Result.CachedBounds.Min.x, 5);

            var foldM2 = new MeshMiteredOffset { Mesh = exactFold, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1, MiterLimit = 2.0 };
            Assert.True(foldM2.Compute(), foldM2.Error);
            AssertClean(foldM2.Result, "exact 180-deg razor fold MiterLimit=2");
            Assert.Equal(-0.2, foldM2.Result.CachedBounds.Min.x, 5);

            // 3. MiterLimit = 1.0 on unit cube chamfers all edges & corners cleanly
            var chamfer = new MeshMiteredOffset {
                Mesh = Box(Vector3d.Zero, Vector3d.One),
                Mode = MeshMiteredOffset.OffsetModes.Outward,
                Distance = 0.2,
                MiterLimit = 1.0
            };
            Assert.True(chamfer.Compute(), chamfer.Error);
            AssertClean(chamfer.Result, "MiterLimit=1 cube");
            // Volume strictly between un-chamfered sharp offset 1.4^3 = 2.744 and input + 6 face prisms = 1 + 1.2 = 2.2
            double vChamfer = Volume(chamfer.Result);
            Assert.True(vChamfer > 2.2 && vChamfer < 2.744, $"unexpected chamfered cube volume {vChamfer}");
        }

        static DMesh3 ShallowOpenDome(double slope)
        {
            var m = new DMesh3();
            int c = m.AppendVertex(new Vector3d(0, 0, 0));
            int px = m.AppendVertex(new Vector3d(1, 0, -slope));
            int py = m.AppendVertex(new Vector3d(0, 1, -slope));
            int nx = m.AppendVertex(new Vector3d(-1, 0, -slope));
            int ny = m.AppendVertex(new Vector3d(0, -1, -slope));
            m.AppendTriangle(c, px, py);
            m.AppendTriangle(c, py, nx);
            m.AppendTriangle(c, nx, ny);
            m.AppendTriangle(c, ny, px);
            return m;
        }

        [Fact]
        public void GentlyCurvedSurfaces_NoBackwardSpikes()
        {
            // 1. Shallow open domes (s = 0.03 and s = 0.01): thickened by +-0.1 must stay tightly within [-slope - d, +d] (no backward spike!)
            foreach (double slope in new[] { 0.03, 0.01 }) {
                double d = 0.1;
                var domeOp = new MeshMiteredOffset {
                    Mesh = ShallowOpenDome(slope),
                    Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                    Distance = d,
                    MiterLimit = 4.0
                };
                Assert.True(domeOp.Compute(), $"dome s={slope}: {domeOp.Error}");
                AssertClean(domeOp.Result, $"dome s={slope}");
                AxisAlignedBox3d bb = domeOp.Result.CachedBounds;
                // Must not shoot backward below (-slope - d) or upward past +d * (1 + slope)
                Assert.True(bb.Min.z >= -slope - d - 1e-4, $"dome s={slope} backward spike: Min.z={bb.Min.z}");
                Assert.True(bb.Max.z <= d + 1e-3, $"dome s={slope} apex overshoot: Max.z={bb.Max.z}");
                Assert.True(bb.Max.z >= d - 1e-4, $"dome s={slope} missing corner cap: Max.z={bb.Max.z}");
            }

            // 2. Smooth subdivided sphere (normalized cube, EdgeVertices=3): Outward and Inward stay clean and close to analytic radii
            DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 1.0, EdgeVertices = 3 }.Generate().MakeDMesh();
            var spOut = new MeshMiteredOffset { Mesh = sphere, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.True(spOut.Compute(), spOut.Error);
            AssertClean(spOut.Result, "sphere outward");
            Assert.True(spOut.Result.CachedBounds.Max.x <= 1.15 && spOut.Result.CachedBounds.Max.x >= 1.09);

            var spIn = new MeshMiteredOffset { Mesh = sphere, Mode = MeshMiteredOffset.OffsetModes.Inward, Distance = 0.1 };
            Assert.True(spIn.Compute(), spIn.Error);
            AssertClean(spIn.Result, "sphere inward");
            Assert.True(spIn.Result.CachedBounds.Max.x <= 0.92 && spIn.Result.CachedBounds.Max.x >= 0.85);
        }

        [Fact]
        public void TouchingEdgeSolids_AttachedFin_And_Contacts()
        {
            // 1. Two closed cubes touching along a shared non-manifold edge ([0,1]^3 and [1,2]x[1,2]x[0,1])
            DMesh3 edgeTouch = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 secondCube = Box(new Vector3d(1, 1, 0), new Vector3d(2, 2, 1));
            var vmap = new Dictionary<int, int>();
            foreach (int vid in secondCube.VertexIndices())
                vmap[vid] = edgeTouch.AppendVertex(secondCube.GetVertex(vid));
            foreach (int tid in secondCube.TriangleIndices()) {
                Index3i t = secondCube.GetTriangle(tid);
                edgeTouch.AppendTriangle(vmap[t.a], vmap[t.b], vmap[t.c]);
            }
            var etOut = new MeshMiteredOffset { Mesh = edgeTouch, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.True(etOut.Compute(), etOut.Error);
            AssertClean(etOut.Result, "edge-touching cubes outward");
            // Two overlapping [-0.1, 1.1]^3 and [0.9, 2.1]^2 x [-0.1, 1.1]: overlap is [0.9,1.1]^2 x [-0.1,1.1] = 0.2*0.2*1.2 = 0.048
            // Total volume = 2 * 1.2^3 - 0.048 = 3.456 - 0.048 = 3.408
            Assert.Equal(3.408, Volume(etOut.Result), 5);

            var etIn = new MeshMiteredOffset { Mesh = edgeTouch, Mode = MeshMiteredOffset.OffsetModes.Inward, Distance = 0.1 };
            Assert.True(etIn.Compute(), etIn.Error);
            AssertClean(etIn.Result, "edge-touching cubes inward");
            // Two disjoint shrunk cubes 0.8^3 each => total volume 2 * 0.512 = 1.024
            Assert.Equal(1.024, Volume(etIn.Result), 5);

            // 4. Closed cube + attached open fin sharing a cube edge (via duplicated vertices) must be rejected as NotClosed
            DMesh3 cubePlusFin = Box(Vector3d.Zero, Vector3d.One);
            int fa = cubePlusFin.AppendVertex(new Vector3d(1, 0, 0));
            int fb = cubePlusFin.AppendVertex(new Vector3d(1, 1, 0));
            int fc = cubePlusFin.AppendVertex(new Vector3d(2, 0.5, 0));
            cubePlusFin.AppendTriangle(fa, fb, fc);
            var finOp = new MeshMiteredOffset { Mesh = cubePlusFin, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.False(finOp.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.NotClosed, finOp.Status);

            // 5. Exact touching contact: two disjoint cubes [0,1]^3 and [1.5, 2.5] x [1.5, 2.5] x [0, 1]
            //    offset outward by d = 0.25 => [-0.25, 1.25]^3 and [1.25, 2.75]^2 x [-0.25, 1.25] meet along the exact edge x=1.25, y=1.25
            DMesh3 contactCubes = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 cOther = Box(new Vector3d(1.5, 1.5, 0), new Vector3d(2.5, 2.5, 1));
            var cmap = new Dictionary<int, int>();
            foreach (int vid in cOther.VertexIndices()) cmap[vid] = contactCubes.AppendVertex(cOther.GetVertex(vid));
            foreach (int tid in cOther.TriangleIndices()) {
                Index3i t = cOther.GetTriangle(tid);
                contactCubes.AppendTriangle(cmap[t.a], cmap[t.b], cmap[t.c]);
            }
            var contactOp = new MeshMiteredOffset { Mesh = contactCubes, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(contactOp.Compute(), contactOp.Error);
            Assert.True(contactOp.RoundingClean);
            Assert.True(contactOp.Contacts > 0, $"expected exact edge contact Contacts > 0, got {contactOp.Contacts}");
            Assert.Equal(2.0 * 3.375, Volume(contactOp.Result), 6);
            Assert.Empty(SolidBooleanPipeline.CrossingTrianglesOfAll(contactOp.Result));
        }

        [Fact]
        public void HollowSolid_MultiComponentInsideOut_SliverTriangle_And_TinyDistance()
        {
            // 1. Hollow cube (outer [-2,2]^3 minus cavity [-1,1]^3, volume 64 - 8 = 56):
            //    Outward d=0.25 => outer [-2.25,2.25]^3 (4.5^3 = 91.125) minus shrunk cavity [-0.75,0.75]^3 (1.5^3 = 3.375) = 87.75
            //    Inward  d=0.25 => outer [-1.75,1.75]^3 (3.5^3 = 42.875) minus grown cavity [-1.25,1.25]^3 (2.5^3 = 15.625) = 27.25
            DMesh3 hollow = HollowBox(2.0, 1.0);
            var hollowOut = new MeshMiteredOffset { Mesh = hollow, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(hollowOut.Compute(), hollowOut.Error);
            AssertClean(hollowOut.Result, "hollow outward");
            Assert.Equal(87.75, Volume(hollowOut.Result), 5);

            var hollowIn = new MeshMiteredOffset { Mesh = hollow, Mode = MeshMiteredOffset.OffsetModes.Inward, Distance = 0.25 };
            Assert.True(hollowIn.Compute(), hollowIn.Error);
            AssertClean(hollowIn.Result, "hollow inward");
            Assert.Equal(27.25, Volume(hollowIn.Result), 5);

            // 2. Multi-component mesh with a large positive cube (+1000) and a separate inside-out cube (-8) => InsideOut
            DMesh3 multi = Box(Vector3d.Zero, new Vector3d(10, 10, 10));
            DMesh3 badCube = Box(new Vector3d(20, 0, 0), new Vector3d(22, 2, 2));
            badCube.ReverseOrientation();
            var vmap = new Dictionary<int, int>();
            foreach (int vid in badCube.VertexIndices()) vmap[vid] = multi.AppendVertex(badCube.GetVertex(vid));
            foreach (int tid in badCube.TriangleIndices()) {
                Index3i t = badCube.GetTriangle(tid);
                multi.AppendTriangle(vmap[t.a], vmap[t.b], vmap[t.c]);
            }
            var multiOp = new MeshMiteredOffset { Mesh = multi, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.False(multiOp.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InsideOut, multiOp.Status);

            // 3. Collinear sliver triangle attached to an open flat plate is ignored cleanly without corrupting the plate
            DMesh3 plateWithSliver = OpenFlatPlate(0, 1, 0, 1, 0);
            int mid = plateWithSliver.AppendVertex(new Vector3d(0.5, 0, 0));
            plateWithSliver.AppendTriangle(0, mid, 1); // exact collinear sliver along edge (0, 1)
            var sliverOp = new MeshMiteredOffset { Mesh = plateWithSliver, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.1 };
            Assert.True(sliverOp.Compute(), sliverOp.Error);
            AssertClean(sliverOp.Result, "plate with sliver");
            Assert.Equal(0.2, Volume(sliverOp.Result), 7);

            // 4. Stray coincident-vertex triangle far away at (1e10, 0, 0) does not pollute box extent
            DMesh3 cubeWithStray = Box(Vector3d.Zero, Vector3d.One);
            int sp0 = cubeWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            int sp1 = cubeWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            int sp2 = cubeWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            cubeWithStray.AppendTriangle(sp0, sp1, sp2);
            var strayOp = new MeshMiteredOffset { Mesh = cubeWithStray, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(strayOp.Compute(), strayOp.Error);
            AssertClean(strayOp.Result, "cube with far coincident triangle");
            Assert.Equal(3.375, Volume(strayOp.Result), 7);

            // 5. Closed cube whose edge is broken by a zero-area collinear bridge triangle fails with InvalidInput (never silently notches)
            DMesh3 bridgeCube = Box(Vector3d.Zero, Vector3d.One);
            // Find edge (0,0,0)-(1,0,0) on z=0 face and replace one triangle with two triangles + collinear bridge
            int vOrigin = -1, vX = -1, splitTid = -1, vOpp = -1;
            foreach (int vid in bridgeCube.VertexIndices()) {
                Vector3d p = bridgeCube.GetVertex(vid);
                if (p == new Vector3d(0, 0, 0)) vOrigin = vid;
                if (p == new Vector3d(1, 0, 0)) vX = vid;
            }
            foreach (int tid in bridgeCube.TriangleIndices()) {
                Index3i t = bridgeCube.GetTriangle(tid);
                if ((t.a == vOrigin && t.b == vX) || (t.b == vOrigin && t.c == vX) || (t.c == vOrigin && t.a == vX)) {
                    splitTid = tid;
                    vOpp = (t.a != vOrigin && t.a != vX) ? t.a : (t.b != vOrigin && t.b != vX) ? t.b : t.c;
                    break;
                }
            }
            Assert.True(splitTid >= 0);
            bridgeCube.RemoveTriangle(splitTid, false, false);
            int vm = bridgeCube.AppendVertex(new Vector3d(0.5, 0, 0));
            bridgeCube.AppendTriangle(vOrigin, vm, vOpp);
            bridgeCube.AppendTriangle(vm, vX, vOpp);
            bridgeCube.AppendTriangle(vOrigin, vX, vm); // collinear bridge restoring winding closure
            var bridgeOp = new MeshMiteredOffset { Mesh = bridgeCube, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.2 };
            Assert.False(bridgeOp.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, bridgeOp.Status);

            // 6. Extremely small Distance (1e-20 * extent) is refused as InvalidParameters (never silently returns un-offset input)
            var tinyIn = new MeshMiteredOffset { Mesh = Box(Vector3d.Zero, Vector3d.One), Mode = MeshMiteredOffset.OffsetModes.Inward, Distance = 1e-20 };
            Assert.False(tinyIn.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, tinyIn.Status);
            Assert.Null(tinyIn.Result);

            var tinyOut = new MeshMiteredOffset { Mesh = Box(Vector3d.Zero, Vector3d.One), Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 1e-20 };
            Assert.False(tinyOut.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, tinyOut.Status);
            Assert.Null(tinyOut.Result);

            // 7. Sub-ulp Distance at UTM coordinates (1e-11 at 5.7e6) is refused as InvalidParameters (never silently drops X/Y offsets)
            DMesh3 utmCube = Box(new Vector3d(632000, 5712000, 0), new Vector3d(632001, 5712001, 1));
            var utmTiny = new MeshMiteredOffset { Mesh = utmCube, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 1e-11 };
            Assert.False(utmTiny.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, utmTiny.Status);

            // 8. Closed mesh with a disjoint closed ultra-thin sliver box component refuses with InvalidInput (never leaves component un-offset)
            DMesh3 withSliverComp = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 sliverBox = Box(new Vector3d(2, 0, 0), new Vector3d(3, 1e-13, 1e-13));
            var smap = new Dictionary<int, int>();
            foreach (int vid in sliverBox.VertexIndices()) smap[vid] = withSliverComp.AppendVertex(sliverBox.GetVertex(vid));
            foreach (int tid in sliverBox.TriangleIndices()) {
                Index3i t = sliverBox.GetTriangle(tid);
                withSliverComp.AppendTriangle(smap[t.a], smap[t.b], smap[t.c]);
            }
            var sliverCompOp = new MeshMiteredOffset { Mesh = withSliverComp, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.1 };
            Assert.False(sliverCompOp.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, sliverCompOp.Status);

            // 9. SolidBooleanPipeline ThickenOpenSheet with relative thickness ignores stray coincident-vertex triangles
            DMesh3 plateWithStray = OpenFlatPlate(0, 1, 0, 1, 0);
            int st0 = plateWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            int st1 = plateWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            int st2 = plateWithStray.AppendVertex(new Vector3d(1e10, 0, 0));
            plateWithStray.AppendTriangle(st0, st1, st2);
            var strayPipe = new SolidBooleanPipeline(Box(new Vector3d(0, 0, -0.5), new Vector3d(1, 1, 0)), plateWithStray, ExactMeshBoolean.Operations.Union) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.ThickenOpenSheet,
                RelativeOpenSheetHalfThickness = 0.05
            };
            Assert.True(strayPipe.Compute(), strayPipe.Error);
            Assert.True(strayPipe.Report.Clean, strayPipe.Report.NotCleanReason);
            Assert.True(strayPipe.Result.CachedBounds.Max.z < 0.2, $"stray triangle inflated relative thickness: Max.z={strayPipe.Result.CachedBounds.Max.z}");
        }

        // Convex hip roof solid: base rectangle [-2, 4] x [-1, 1] at z = -sqrt(3), ridge from (0, 0, 0) to (2, 0, 0).
        // Two 60-deg side faces (nA, nB = (0, +-sqrt(3)/2, 1/2)) meet 30-deg hip faces at x < 0 (nC = (-1/2, 0, sqrt(3)/2)) and x > 2.
        static DMesh3 ConvexHipRoof()
        {
            double s3 = Math.Sqrt(3.0);
            // Ridge endpoints (0, 0, 0) and (2, 0, 0). At z = -s3:
            //   nA . (x, y, -s3) = 0 => y = 1; nB => y = -1
            //   nC . (x, y, -s3) = 0 => -0.5 x + 0.5*s3*(-s3) = 0 => x = -3; right hip => x = +5
            var m = new DMesh3();
            int r0 = m.AppendVertex(new Vector3d(0, 0, 0));
            int r1 = m.AppendVertex(new Vector3d(2, 0, 0));
            int b0 = m.AppendVertex(new Vector3d(-3, -1, -s3));
            int b1 = m.AppendVertex(new Vector3d(5, -1, -s3));
            int b2 = m.AppendVertex(new Vector3d(5, 1, -s3));
            int b3 = m.AppendVertex(new Vector3d(-3, 1, -s3));
            // Bottom (-z)
            m.AppendTriangle(b0, b3, b2);
            m.AppendTriangle(b0, b2, b1);
            // Side -y (outward normal (0, -s3/2, 1/2))
            m.AppendTriangle(b0, b1, r1);
            m.AppendTriangle(b0, r1, r0);
            // Side +y (outward normal (0, +s3/2, 1/2))
            m.AppendTriangle(b2, b3, r0);
            m.AppendTriangle(b2, r0, r1);
            // Left hip -x (outward normal (-1/2, 0, s3/2))
            m.AppendTriangle(b3, b0, r0);
            // Right hip +x (outward normal (+1/2, 0, s3/2))
            m.AppendTriangle(b1, b2, r1);
            return m;
        }

        [Fact]
        public void VeryAcuteCones_And_AsymmetricHipRoof()
        {
            // 1. Very acute cones whose exact apex exceeds boundLimit = 8.0 must bevel-clamp cleanly (never silently drop corner!)
            double d = 0.1;
            DMesh3 needleCone = AcuteCone(18, 7.0, height: 2.0);
            var needleOp = new MeshMiteredOffset { Mesh = needleCone, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = d, MiterLimit = 4.0 };
            Assert.True(needleOp.Compute(), needleOp.Error);
            AssertClean(needleOp.Result, "7-deg needle cone");
            Assert.Equal(2.0 + 4.0 * d, needleOp.Result.CachedBounds.Max.z, 3);

            DMesh3 sharpPyr = AcuteCone(4, 10.0, height: 2.0);
            var pyrOp = new MeshMiteredOffset { Mesh = sharpPyr, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = d, MiterLimit = 2.0 };
            Assert.True(pyrOp.Compute(), pyrOp.Error);
            AssertClean(pyrOp.Result, "10-deg 4-sided pyramid");
            Assert.Equal(2.0 + 2.0 * d, pyrOp.Result.CachedBounds.Max.z, 3);

            // 2. Asymmetric convex hip roof: ridge miter (0, 0, 2*d) would protrude 73% above 30-deg hip plane nC unless clipped
            DMesh3 roof = ConvexHipRoof();
            var roofOp = new MeshMiteredOffset { Mesh = roof, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = d, MiterLimit = 4.0 };
            Assert.True(roofOp.Compute(), roofOp.Error);
            AssertClean(roofOp.Result, "asymmetric hip roof");
            // Every output vertex must satisfy the shifted left hip plane (-0.5*x + sqrt(3)/2*z <= d) and right hip plane
            Vector3d nLeftHip = new Vector3d(-0.5, 0, 0.5 * Math.Sqrt(3.0));
            Vector3d nRightHip = new Vector3d(0.5, 0, 0.5 * Math.Sqrt(3.0));
            foreach (int vid in roofOp.Result.VertexIndices()) {
                Vector3d p = roofOp.Result.GetVertex(vid);
                Assert.True(nLeftHip.Dot(p) <= d + 1e-5, $"hip roof protrusion above left hip plane: {nLeftHip.Dot(p)} > {d}");
                Assert.True(nRightHip.Dot(p - new Vector3d(2, 0, 0)) <= d + 1e-5, $"hip roof protrusion above right hip plane");
            }
            // Exact ridge height must still reach 2*d = 0.2
            Assert.Equal(2.0 * d, roofOp.Result.CachedBounds.Max.z, 5);
        }

        [Fact]
        public void ExtremeHollowScales_And_OpenHemisphereRim()
        {
            // 1. Extreme scales 2^-500 and 2^500 on HollowBox(2, 1) stay clean and bit-exact under 2^k scaling
            DMesh3 hollow = HollowBox(2.0, 1.0);
            var hBase = new MeshMiteredOffset { Mesh = hollow, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = 0.25 };
            Assert.True(hBase.Compute(), hBase.Error);
            foreach (int exp in new[] { -500, 500 }) {
                var scaled = new DMesh3(hollow);
                foreach (int vid in scaled.VertexIndices()) {
                    Vector3d p = scaled.GetVertex(vid);
                    scaled.SetVertex(vid, new Vector3d(ExactPoint3.ScaleB(p.x, exp), ExactPoint3.ScaleB(p.y, exp), ExactPoint3.ScaleB(p.z, exp)));
                }
                var hOp = new MeshMiteredOffset { Mesh = scaled, Mode = MeshMiteredOffset.OffsetModes.Outward, Distance = ExactPoint3.ScaleB(0.25, exp) };
                Assert.True(hOp.Compute(), $"hollow 2^{exp}: {hOp.Error}");
                Assert.Equal(MeshMiteredOffset.OffsetStatus.Success, hOp.Status);
                Assert.True(hOp.RoundingClean);
                // Scale result back by 2^-exp and verify bit-for-bit identity with hBase
                var back = new DMesh3(hOp.Result);
                foreach (int vid in back.VertexIndices()) {
                    Vector3d p = back.GetVertex(vid);
                    back.SetVertex(vid, new Vector3d(ExactPoint3.ScaleB(p.x, -exp), ExactPoint3.ScaleB(p.y, -exp), ExactPoint3.ScaleB(p.z, -exp)));
                }
                Assert.Equal("", Invariance.Difference(hBase.Result, back));
            }

            // 4. Open curved hemisphere cut at z = 0 thickened DoubleSided: flush rim (no downward spikes past -d)
            DMesh3 fullSphere = new Sphere3Generator_NormalizedCube { Radius = 1.0, EdgeVertices = 3 }.Generate().MakeDMesh();
            var hemi = new DMesh3();
            var hmap = new Dictionary<int, int>();
            foreach (int tid in fullSphere.TriangleIndices()) {
                Index3i t = fullSphere.GetTriangle(tid);
                Vector3d a = fullSphere.GetVertex(t.a), b = fullSphere.GetVertex(t.b), c = fullSphere.GetVertex(t.c);
                if (a.z < -1e-9 || b.z < -1e-9 || c.z < -1e-9) continue;
                int ia = hmap.TryGetValue(t.a, out int ma) ? ma : (hmap[t.a] = hemi.AppendVertex(a));
                int ib = hmap.TryGetValue(t.b, out int mb) ? mb : (hmap[t.b] = hemi.AppendVertex(b));
                int ic = hmap.TryGetValue(t.c, out int mc) ? mc : (hmap[t.c] = hemi.AppendVertex(c));
                hemi.AppendTriangle(ia, ib, ic);
            }
            var hemiOp = new MeshMiteredOffset { Mesh = hemi, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = 0.05 };
            Assert.True(hemiOp.Compute(), hemiOp.Error);
            AssertClean(hemiOp.Result, "open hemisphere");
            Assert.True(hemiOp.Result.CachedBounds.Min.z >= -0.05 - 1e-3,
                $"open hemisphere rim spike below z=-0.05: Min.z={hemiOp.Result.CachedBounds.Min.z}");
        }

        static double MaxSurfaceDistance(DMesh3 a, DMesh3 b)
        {
            var treeA = new DMeshAABBTree3(a, autoBuild: true);
            var treeB = new DMeshAABBTree3(b, autoBuild: true);
            double maxD = 0;
            foreach (int vid in a.VertexIndices()) {
                double d2;
                int tid = treeB.FindNearestTriangle(a.GetVertex(vid), out d2);
                if (tid >= 0) maxD = Math.Max(maxD, Math.Sqrt(d2));
            }
            foreach (int vid in b.VertexIndices()) {
                double d2;
                int tid = treeA.FindNearestTriangle(b.GetVertex(vid), out d2);
                if (tid >= 0) maxD = Math.Max(maxD, Math.Sqrt(d2));
            }
            return maxD;
        }

        [Fact]
        public void Invariance_AllSimilarities_And_Reuse_And_Determinism()
        {
            // 1. DoubleSided open L-bracket across Similarity.All()
            DMesh3 bracket = OpenLBracket(2.0, 1.0, 2.0);
            double d = 0.1;
            var baseline = new MeshMiteredOffset { Mesh = bracket, Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = d };
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
                DMesh3 back = Invariance.MappedBack(op.Result, sim);
                double vs = Volume(op.Result) / (sim.Scale * sim.Scale * sim.Scale);
                Assert.True(Math.Abs(vs - v0) <= 1e-5 * v0, $"{sim.Name}: scaled volume {vs} vs {v0}");
                double surfDist = MaxSurfaceDistance(baseline.Result, back);
                Assert.True(surfDist < 1e-5, $"{sim.Name}: surface drift {surfDist}");
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(baseline.Result, back));
            }

            // 1b. Vertex/triangle permutation invariance (Invariance.Permuted)
            var permOp = new MeshMiteredOffset { Mesh = Invariance.Permuted(bracket, seed: 42), Mode = MeshMiteredOffset.OffsetModes.DoubleSided, Distance = d };
            Assert.True(permOp.Compute(), permOp.Error);
            AssertClean(permOp.Result, "permuted L-bracket");
            Assert.Equal(v0, Volume(permOp.Result), 7);
            Assert.True(MaxSurfaceDistance(baseline.Result, permOp.Result) < 1e-9);

            // 2. Outward and Inward closed cube across Similarity.All()
            DMesh3 cube = Box(Vector3d.Zero, Vector3d.One);
            foreach (var mode in new[] { MeshMiteredOffset.OffsetModes.Outward, MeshMiteredOffset.OffsetModes.Inward }) {
                double cd = 0.2;
                var cBase = new MeshMiteredOffset { Mesh = cube, Mode = mode, Distance = cd };
                Assert.True(cBase.Compute(), mode + " base: " + cBase.Error);
                AssertClean(cBase.Result, mode + " base");
                double cv0 = Volume(cBase.Result);
                foreach (Similarity sim in Similarity.All()) {
                    var op = new MeshMiteredOffset {
                        Mesh = Invariance.Transformed(cube, sim),
                        Mode = mode,
                        Distance = sim.ApplyLength(cd)
                    };
                    Assert.True(op.Compute(), $"{mode} {sim.Name}: {op.Error}");
                    AssertClean(op.Result, $"{mode} {sim.Name}");
                    DMesh3 back = Invariance.MappedBack(op.Result, sim);
                    double cvs = Volume(op.Result) / (sim.Scale * sim.Scale * sim.Scale);
                    Assert.True(Math.Abs(cvs - cv0) <= 1e-5 * cv0, $"{mode} {sim.Name}: scaled volume {cvs} vs {cv0}");
                    double surfDist = MaxSurfaceDistance(cBase.Result, back);
                    Assert.True(surfDist < 1e-5, $"{mode} {sim.Name}: surface drift {surfDist}");
                    if (sim.IsExact)
                        Assert.Equal("", Invariance.Difference(cBase.Result, back));
                }
            }

            // 3. SolidBooleanPipeline with ThickenOpenSheet across Similarity.All() (explicit thickness is rotation-invariant)
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

            // 4. Reuse across failure -> success, different meshes, and different modes matches fresh instances bit-for-bit
            var reuse = new MeshMiteredOffset { Mesh = null, Distance = -1.0 };
            Assert.False(reuse.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidInput, reuse.Status);

            reuse.Mesh = cube;
            reuse.Mode = MeshMiteredOffset.OffsetModes.Outward;
            reuse.Distance = 0.25;
            Assert.True(reuse.Compute(), reuse.Error);
            DMesh3 firstOut = new DMesh3(reuse.Result);

            reuse.Mesh = bracket;
            reuse.Mode = MeshMiteredOffset.OffsetModes.DoubleSided;
            reuse.Distance = d;
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.Equal("", Invariance.Difference(baseline.Result, reuse.Result));

            reuse.Mesh = cube;
            reuse.Mode = MeshMiteredOffset.OffsetModes.Inward;
            reuse.Distance = 0.2;
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.Equal(0.216, Volume(reuse.Result), 8);

            reuse.Mode = MeshMiteredOffset.OffsetModes.Outward;
            reuse.Distance = 0.25;
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.Equal("", Invariance.Difference(firstOut, reuse.Result));

            // 5. Determinism across repeated runs on the 5-face open-top box
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

            // Invalid Mode / Distance / MiterLimit
            op.Mesh = Box(Vector3d.Zero, Vector3d.One);
            op.Mode = (MeshMiteredOffset.OffsetModes)99;
            Assert.False(op.Compute());
            Assert.Equal(MeshMiteredOffset.OffsetStatus.InvalidParameters, op.Status);
            op.Mode = MeshMiteredOffset.OffsetModes.Outward;

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

            op.MiterLimit = 1e5;
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
