using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Refusal = g3.ExactMeshBoolean.SheetRefusal;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The sheet role of ExactMeshBoolean (layer B of the side labelling, ExactMeshCells): an open, oriented cutting surface as the tool
    /// of a Difference. The part of the workpiece behind the sheet (the side its normals point away from) is inside the tool; every
    /// cell of the workpiece gets one label from the sheet pieces around it or the boolean refuses, with a reason and a witness. All
    /// meshes here are procedural.
    /// </summary>
    public class ExactBooleanSheetTests
    {
        // ---- builders -------------------------------------------------------------------------------------------------------------

        /// <summary> vertices by position, triangles by corners: the shared corners are shared vertices </summary>
        sealed class Soup
        {
            public readonly DMesh3 Mesh = new DMesh3();
            readonly Dictionary<Vector3d, int> ids = new Dictionary<Vector3d, int>();
            public int V(Vector3d p)
            {
                int id;
                if (ids.TryGetValue(p, out id)) return id;
                return ids[p] = Mesh.AppendVertex(p);
            }
            public void Tri(Vector3d a, Vector3d b, Vector3d c) { Mesh.AppendTriangle(V(a), V(b), V(c)); }
            /// <summary> the quad a b c d (counterclockwise seen from where its normal points), as two triangles </summary>
            public void Quad(Vector3d a, Vector3d b, Vector3d c, Vector3d d) { Tri(a, b, c); Tri(a, c, d); }
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var s = new Soup();
            Vector3d p000 = new Vector3d(x0, y0, z0), p100 = new Vector3d(x1, y0, z0), p110 = new Vector3d(x1, y1, z0), p010 = new Vector3d(x0, y1, z0);
            Vector3d p001 = new Vector3d(x0, y0, z1), p101 = new Vector3d(x1, y0, z1), p111 = new Vector3d(x1, y1, z1), p011 = new Vector3d(x0, y1, z1);
            s.Quad(p000, p010, p110, p100);       // bottom (normal -z)
            s.Quad(p001, p101, p111, p011);       // top
            s.Quad(p000, p100, p101, p001);       // y = y0 (normal -y)
            s.Quad(p010, p011, p111, p110);       // y = y1
            s.Quad(p000, p001, p011, p010);       // x = x0 (normal -x)
            s.Quad(p100, p110, p111, p101);       // x = x1
            return s.Mesh;
        }

        static DMesh3 Reversed(DMesh3 m)
        {
            var c = new DMesh3(m);
            c.ReverseOrientation(false);
            return c;
        }

        /// <summary> the plane z = z in [x0, x1] x [y0, y1], n x n cells, the normal +z (up) or -z </summary>
        static DMesh3 Plate(double z, double x0, double x1, double y0, double y1, bool up = true, int n = 1)
        {
            var s = new Soup();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    double xa = x0 + (x1 - x0) * i / n, xb = x0 + (x1 - x0) * (i + 1) / n, ya = y0 + (y1 - y0) * j / n, yb = y0 + (y1 - y0) * (j + 1) / n;
                    s.Quad(new Vector3d(xa, ya, z), new Vector3d(xb, ya, z), new Vector3d(xb, yb, z), new Vector3d(xa, yb, z));
                }
            return up ? s.Mesh : Reversed(s.Mesh);
        }

        /// <summary>
        /// a cup around the axis x = y = 0.5: the walls are the frustum from the half width hb at height zb to ht at zt (four quads), with
        /// the bottom if bottom; the normals point away from the axis (and down at the bottom), so the cup's inside is behind the sheet; lid: the
        /// closed solid (the lid at zt, normal up)
        /// </summary>
        static DMesh3 Cup(double zb, double hb, double zt, double ht, bool bottom, bool lid = false)
        {
            var s = new Soup();
            Func<double, double, double, Vector3d> P = (dx, dy, z) => new Vector3d(0.5 + dx, 0.5 + dy, z);
            double[][] dir = { new[] { -1.0, -1 }, new[] { 1.0, -1 }, new[] { 1.0, 1 }, new[] { -1.0, 1 } };
            for (int k = 0; k < 4; ++k) {
                double[] d0 = dir[k], d1 = dir[(k + 1) % 4];
                s.Quad(P(hb * d0[0], hb * d0[1], zb), P(hb * d1[0], hb * d1[1], zb), P(ht * d1[0], ht * d1[1], zt), P(ht * d0[0], ht * d0[1], zt));
            }
            if (bottom)
                s.Quad(P(-hb, -hb, zb), P(-hb, hb, zb), P(hb, hb, zb), P(hb, -hb, zb));
            if (lid)
                s.Quad(P(-ht, -ht, zt), P(ht, -ht, zt), P(ht, ht, zt), P(-ht, ht, zt));
            return s.Mesh;
        }

        /// <summary> a square tube of half width h around the axis x = y = 0.5, from z0 to z1, the normals pointing away from the axis (flip: towards it); caps: closed </summary>
        static DMesh3 Tube(double h, double z0, double z1, bool flip, bool caps = false)
        {
            DMesh3 c = Cup(z0, h, z1, h, false, false);
            if (caps) {
                var s = new Soup();
                foreach (int t in c.TriangleIndices()) {
                    Index3i f = c.GetTriangle(t);
                    s.Tri(c.GetVertex(f.a), c.GetVertex(f.b), c.GetVertex(f.c));
                }
                Func<double, double, double, Vector3d> P = (dx, dy, z) => new Vector3d(0.5 + dx, 0.5 + dy, z);
                s.Quad(P(-h, -h, z0), P(-h, h, z0), P(h, h, z0), P(h, -h, z0));
                s.Quad(P(-h, -h, z1), P(h, -h, z1), P(h, h, z1), P(-h, h, z1));
                c = s.Mesh;
            }
            return flip ? Reversed(c) : c;
        }

        /// <summary> the polyline extruded over z in [-1, 2]; the normals point to the right of the direction of travel </summary>
        static DMesh3 Extruded(params double[][] xy)
        {
            var s = new Soup();
            for (int k = 0; k + 1 < xy.Length; ++k) {
                s.Quad(new Vector3d(xy[k][0], xy[k][1], -1), new Vector3d(xy[k + 1][0], xy[k + 1][1], -1),
                    new Vector3d(xy[k + 1][0], xy[k + 1][1], 2), new Vector3d(xy[k][0], xy[k][1], 2));
            }
            return s.Mesh;
        }

        /// <summary> [0,3] x [0,3] x [0,1] without the through hole (1,2) x (1,2): a closed solid of genus 1 </summary>
        static DMesh3 RingSolid()
        {
            var op = new ExactMeshBoolean(Box(0, 0, 0, 3, 3, 1), Box(1, 1, -1, 2, 2, 2), Ops.Difference);
            Assert.True(op.Compute(), op.Error);
            return op.Result;
        }

        static double Volume(DMesh3 m) => m == null ? 0 : MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static ExactMeshBoolean Run(DMesh3 workpiece, DMesh3 sheet, ExactMeshBoolean.SheetUncutSides uncut = ExactMeshBoolean.SheetUncutSides.Refuse, Action<ExactMeshBoolean> configure = null)
        {
            var op = new ExactMeshBoolean(new[] { workpiece, sheet }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet },
                SheetUncutSide = uncut
            };
            configure?.Invoke(op);
            op.Compute();
            return op;
        }

        static ExactMeshBoolean Cut(DMesh3 workpiece, DMesh3 sheet, double expectedVolume, string what)
        {
            ExactMeshBoolean op = Run(workpiece, sheet);
            Assert.True(op.Status == Status.Success, what + ": " + op.Status + " " + op.SheetReason + " " + op.Error);
            ResultChecks.AssertClosed(op.Result, what);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
            Assert.True(op.Result.Vertices().All(v => v.IsFinite), what + ": not finite");
            Assert.Equal(expectedVolume, Volume(op.Result), 12);
            return op;
        }

        static void AssertRefused(ExactMeshBoolean op, Refusal reason, string what)
        {
            Assert.True(op.Status == Status.SheetRefused, what + ": " + op.Status + " " + op.Error);
            Assert.True(reason == op.SheetReason, what + ": " + op.SheetReason + " " + op.Error);
            Assert.Null(op.Result);
            Assert.False(string.IsNullOrEmpty(op.Error));
        }

        // ---- the cube and a flat sheet --------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(true, 0.5)]
        [InlineData(false, 0.5)]
        public void CubeMinusFlatSheet_HalfOfTheCube_BothOrientations(bool up, double expected)
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // (normals up: below the sheet is inside the tool, so the upper half stays; normals down: the lower half stays)
            ExactMeshBoolean op = Cut(cube, Plate(0.5, -1, 2, -1, 2, up), expected, "plate");
            BoundingBox(op.Result, out Vector3d min, out Vector3d max);
            Assert.Equal(up ? 0.5 : 0.0, min.z);
            Assert.Equal(up ? 1.0 : 0.5, max.z);
            // (the two cells of the cube and the one outside it)
            Assert.Equal(3, op.Counts["sheet_cells"]);
        }

        static void BoundingBox(DMesh3 m, out Vector3d min, out Vector3d max)
        {
            min = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue);
            max = -min;
            foreach (Vector3d v in m.Vertices()) {
                min = new Vector3d(Math.Min(min.x, v.x), Math.Min(min.y, v.y), Math.Min(min.z, v.z));
                max = new Vector3d(Math.Max(max.x, v.x), Math.Max(max.y, v.y), Math.Max(max.z, v.z));
            }
        }

        [Fact]
        public void Sheet_PartitionsTheWorkpiece_MetamorphicWithTheReversedSheet()
        {
            var cases = new Dictionary<string, KeyValuePair<DMesh3, DMesh3>> {
                { "plate", new KeyValuePair<DMesh3, DMesh3>(Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2, true, 2)) },
                { "bowl", new KeyValuePair<DMesh3, DMesh3>(Box(0, 0, 0, 1, 1, 1), Cup(0.25, 0.25, 1.25, 0.75, true)) },
                { "tube", new KeyValuePair<DMesh3, DMesh3>(Box(0, 0, 0, 1, 1, 1), Tube(0.25, -1, 2, false)) },
                { "diagonal", new KeyValuePair<DMesh3, DMesh3>(Box(0, 0, 0, 1, 1, 1), DiagonalPlate()) },
            };
            foreach (var kv in cases) {
                DMesh3 w = kv.Value.Key, s = kv.Value.Value;
                ExactMeshBoolean a = Run(w, s), b = Run(w, Reversed(s));
                Assert.True(a.Status == Status.Success && b.Status == Status.Success, kv.Key + ": " + a.Error + b.Error);
                Assert.Equal(Volume(w), Volume(a.Result) + Volume(b.Result), 12);
                // (the two results share the cut faces and no volume)
                var inter = new ExactMeshBoolean(a.Result, b.Result, Ops.Intersection);
                Assert.True(inter.Compute(), inter.Error);
                Assert.Equal(0.0, Volume(inter.Result), 12);
                var union = new ExactMeshBoolean(a.Result, b.Result, Ops.Union);
                Assert.True(union.Compute(), union.Error);
                Assert.Equal(Volume(w), Volume(union.Result), 12);
            }
        }

        // ---- the closed tool as the oracle: a sheet closed far outside gives the same result as the closed tool ----------------------

        static void AssertSameSolid(DMesh3 x, DMesh3 y, string what)
        {
            Assert.Equal(Volume(x), Volume(y), 12);
            var inter = new ExactMeshBoolean(x, y, Ops.Intersection);
            Assert.True(inter.Compute(), inter.Error);
            Assert.Equal(Volume(x), Volume(inter.Result), 12);
        }

        [Fact]
        public void ClosedToolOracle_SheetClosedOutsideGivesTheClosedDifference()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            var pairs = new[] {
                new { Name = "plate", Sheet = Plate(0.5, -1, 2, -1, 2), Closed = Box(-1, -1, -1, 2, 2, 0.5) },
                new { Name = "bowl", Sheet = Cup(0.25, 0.25, 1.25, 0.75, true), Closed = Cup(0.25, 0.25, 1.25, 0.75, true, true) },
                new { Name = "shallow bowl", Sheet = Cup(0.75, 0.25, 1.25, 0.5, true), Closed = Cup(0.75, 0.25, 1.25, 0.5, true, true) },
                new { Name = "tube", Sheet = Tube(0.25, -1, 2, false), Closed = Tube(0.25, -1, 2, false, true) },
            };
            foreach (var p in pairs) {
                ExactMeshBoolean sheet = Run(cube, p.Sheet);
                Assert.True(sheet.Status == Status.Success, p.Name + ": " + sheet.Error);
                var closed = new ExactMeshBoolean(cube, p.Closed, Ops.Difference);
                Assert.True(closed.Compute(), closed.Error);
                AssertSameSolid(sheet.Result, closed.Result, p.Name);
            }
        }

        // ---- bowls, tubes, buckets ----------------------------------------------------------------------------------------------------

        [Fact]
        public void Bowl_DeepAndShallow_BothCutCorrectly()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // deep: the cup (half width 0.25 at z = 0.25 to 0.75 at z = 1.25) removes 13/24 of the cube; its rim is above the cube
            Cut(cube, Cup(0.25, 0.25, 1.25, 0.75, true), 1 - 13.0 / 24, "deep bowl");
            Cut(cube, Reversed(Cup(0.25, 0.25, 1.25, 0.75, true)), 13.0 / 24, "deep bowl, the other side");
            // shallow: from z = 0.75 (half width 0.25) to the rim at 1.25 (0.5); it removes the integral of (0.5 + t)^2 over [0, 0.25]
            double removed = (0.75 * 0.75 * 0.75 - 0.5 * 0.5 * 0.5) / 3;
            Cut(cube, Cup(0.75, 0.25, 1.25, 0.5, true), 1 - removed, "shallow bowl");
            Cut(cube, Reversed(Cup(0.75, 0.25, 1.25, 0.5, true)), removed, "shallow bowl, the other side");
        }

        [Fact]
        public void Tube_TwoRims_ByTheOrientation()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            Cut(cube, Tube(0.25, -1, 2, false), 0.75, "tube, normals outwards");
            Cut(cube, Tube(0.25, -1, 2, true), 0.25, "tube, normals inwards");
        }

        [Fact]
        public void Bucket_AgainstATallBox()
        {
            // the bucket (half width 0.25, bottom at z = 1, rim at z = 5) in the box [0,1]^2 x [0,4]: it removes 0.5 * 0.5 * 3
            DMesh3 tall = Box(0, 0, 0, 1, 1, 4);
            Cut(tall, Cup(1, 0.25, 5, 0.25, true), 4 - 0.75, "bucket");
            Cut(tall, Reversed(Cup(1, 0.25, 5, 0.25, true)), 0.75, "bucket, the other side");
        }

        // ---- the sheet through an edge, a vertex; contact; the rim on the surface ------------------------------------------------------

        static DMesh3 DiagonalPlate()
        {
            // the plane x = y holds the edges (0,0,z) and (1,1,z) of the cube; the normal (1,-1,0)/sqrt 2
            var s = new Soup();
            s.Quad(new Vector3d(-1, -1, -1), new Vector3d(2, 2, -1), new Vector3d(2, 2, 2), new Vector3d(-1, -1, 2));
            return s.Mesh;
        }

        static DMesh3 TwoVertexPlate()
        {
            // the plane 2x = y + z holds the cube's vertices (0,0,0) and (1,1,1) and cuts through the interior; the normal (2,-1,-1)
            Vector3d a = new Vector3d(0, 1, -1), b = new Vector3d(1, 1, 1);
            var s = new Soup();
            s.Quad(-3 * a - 3 * b, 3 * a - 3 * b, 3 * a + 3 * b, -3 * a + 3 * b);
            return s.Mesh;
        }

        [Fact]
        public void SheetThroughTwoEdgesOfTheWorkpiece_HalfOfTheCube()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 plate = DiagonalPlate();
            Cut(cube, plate, 0.5, "through the edges");
            Cut(cube, Reversed(plate), 0.5, "through the edges, the other side");
        }

        [Fact]
        public void SheetThroughTwoVerticesOfTheWorkpiece_HalfOfTheCube()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 plate = TwoVertexPlate();
            // (the normal of the quad is (2,-1,-1) or its opposite: the volumes of the two sides are equal, the halves differ)
            ExactMeshBoolean a = Cut(cube, plate, 0.5, "through the vertices");
            ExactMeshBoolean b = Cut(cube, Reversed(plate), 0.5, "through the vertices, the other side");
            Assert.True(Math.Abs(Centroid(a.Result).x - Centroid(b.Result).x) > 0.01, "the halves are different");
        }

        static Vector3d Centroid(DMesh3 m)
        {
            Vector3d c = Vector3d.Zero;
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, d = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref d);
                double vol = a.Dot(b.Cross(d)) / 6;
                c += vol * (a + b + d) / 4;
                v += vol;
            }
            return c / v;
        }

        [Fact]
        public void RimExactlyOnTheWorkpiecesSurface_IsAccepted()
        {
            // the sheet is trimmed to the cube: its rim lies in the faces of the cube (and its corners on the cube's edges); it separates, so it is a cut
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            Cut(cube, Plate(0.5, 0, 1, 0, 1), 0.5, "trimmed to the cube");
            Cut(cube, Plate(0.5, 0, 1, 0, 1, false), 0.5, "trimmed to the cube, the other side");
            // trimmed to a part of one face: the rim runs along a face, and its two ends on edges of the cube
            Cut(cube, Plate(0.5, 0, 1, 0, 1, true, 3), 0.5, "trimmed to the cube, three by three");
        }

        [Fact]
        public void SheetInThePlaneOfAFace_LabelsByContact()
        {
            // the sheet lies on the top face of the cube with its normals up: the cube is behind it, so it is inside the tool (nothing is left);
            // with normals down the cube is in front of it (outside the tool): the cube stays
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            ExactMeshBoolean up = Run(cube, Plate(1, -1, 2, -1, 2, true));
            Assert.True(up.Status == Status.Success, up.Error);
            Assert.Equal(0.0, Volume(up.Result), 12);
            Cut(cube, Plate(1, -1, 2, -1, 2, false), 1, "contact, outside the tool");
        }

        // ---- refusals --------------------------------------------------------------------------------------------------------------

        [Fact]
        public void RimInsideTheWorkpiece_Refused_WithTheFreeEdge()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            ExactMeshBoolean op = Run(cube, Plate(0.5, -1, 0.5, -1, 2, true, 2));
            AssertRefused(op, Refusal.RimInsideWorkpiece, "half sheet");
            Assert.Equal(0.5, op.SheetWitnessPoint.x, 12);
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.Equal(0.5, op.SheetWitnessPoint.z, 12);
            // a sheet that ends inside the cube from one side only: the same
            AssertRefused(Run(cube, Plate(0.5, 0.25, 0.75, 0.25, 0.75, true)), Refusal.RimInsideWorkpiece, "a patch inside");
        }

        [Fact]
        public void Ring_TheCutArmDoesNotSeparate_SidesInconsistent_WithWitnesses()
        {
            // the rectangle x = 1.5, y in [-0.5, 1.5], z in [-0.5, 1.5] cuts the arm y in [0,1] of the ring completely: the ring minus it is connected
            var s = new Soup();
            s.Quad(new Vector3d(1.5, -0.5, -0.5), new Vector3d(1.5, 1.5, -0.5), new Vector3d(1.5, 1.5, 1.5), new Vector3d(1.5, -0.5, 1.5));
            ExactMeshBoolean op = Run(RingSolid(), s.Mesh);
            AssertRefused(op, Refusal.SidesInconsistent, "ring");
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.InRange(op.SheetWitnessTriangle, 0, 1);
            Assert.InRange(op.SheetWitnessTriangle2, 0, 1);
            Assert.True(op.SheetConflictingCells >= 1);
            // the same without the hole, a cube: it separates
            ExactMeshBoolean arm = Run(Box(0, 0, 0, 3, 1, 1), s.Mesh);
            Assert.True(arm.Status == Status.Success, arm.Error);
            Assert.Equal(1.5, Volume(arm.Result), 12);
        }

        [Fact]
        public void Spiral_TheMiddleCellIsBothInsideAndOutside_SidesInconsistent()
        {
            DMesh3 box = Box(0, 0, 0, 2, 1, 1);
            DMesh3 spiral = Extruded(new[] { 0.5, -1 }, new[] { 0.5, 2 }, new[] { 3.0, 2 }, new[] { 3.0, -2 }, new[] { 1.5, -2 }, new[] { 1.5, 1.5 });
            ExactMeshBoolean op = Run(box, spiral);
            AssertRefused(op, Refusal.SidesInconsistent, "spiral");
            Assert.True(op.SheetWitnessTriangle >= 0 && op.SheetWitnessTriangle2 >= 0 && op.SheetWitnessTriangle != op.SheetWitnessTriangle2);
            // each leg alone separates: x = 0.5 leaves [0.5, 2], x = 1.5 with the other normal leaves [0, 1.5]
            Cut(box, Extruded(new[] { 0.5, -1 }, new[] { 0.5, 2 }), 1.5, "first leg");
        }

        [Fact]
        public void SlotInTheSheet_RimInsideTheWorkpiece_Refused()
        {
            // the plate z = 0.5 over [-1,2]^2 with a slot (x in [-1, 0.5], y in [0.25, 0.75]) cut into it from the left: the slot's three edges are inside the cube
            var s = new Soup();
            double z = 0.5;
            Func<double, double, Vector3d> P = (x, y) => new Vector3d(x, y, z);
            s.Quad(P(-1, -1), P(2, -1), P(2, 0.25), P(-1, 0.25));
            s.Quad(P(-1, 0.75), P(2, 0.75), P(2, 2), P(-1, 2));
            s.Quad(P(0.5, 0.25), P(2, 0.25), P(2, 0.75), P(0.5, 0.75));
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), s.Mesh);
            AssertRefused(op, Refusal.RimInsideWorkpiece, "slot");
            Vector3d w = op.SheetWitnessPoint;
            Assert.Equal(0.5, w.z, 12);
            Assert.True(w.x == 0.5 || w.y == 0.25 || w.y == 0.75, "the witness is an end of an edge of the slot");
        }

        [Fact]
        public void DiscAndSphere_UncutComponent_RefusedOrByTheStatedSide()
        {
            // the disc z = 0 (radius 1, an octagon) with its normals up, a sphere above it that it does not cut
            var s = new Soup();
            Vector3d[] ring = { new Vector3d(1, 0, 0), new Vector3d(0.75, 0.75, 0), new Vector3d(0, 1, 0), new Vector3d(-0.75, 0.75, 0), new Vector3d(-1, 0, 0), new Vector3d(-0.75, -0.75, 0), new Vector3d(0, -1, 0), new Vector3d(0.75, -0.75, 0) };
            for (int k = 0; k < 8; ++k)
                s.Tri(Vector3d.Zero, ring[k], ring[(k + 1) % 8]);
            DMesh3 disc = s.Mesh;
            DMesh3 ball = Box(-0.125, -0.125, 0.25, 0.125, 0.125, 0.5);
            ExactMeshBoolean refused = Run(ball, disc);
            AssertRefused(refused, Refusal.UncutComponents, "uncut");
            Assert.Equal(1, refused.SheetUncutCells);
            // (the ball is above the disc, in front of it: outside the tool, kept, or by the stated side)
            ExactMeshBoolean outside = Run(ball, disc, ExactMeshBoolean.SheetUncutSides.Outside);
            Assert.True(outside.Status == Status.Success, outside.Error);
            Assert.Equal(Volume(ball), Volume(outside.Result), 12);
            ExactMeshBoolean inside = Run(ball, disc, ExactMeshBoolean.SheetUncutSides.Inside);
            Assert.True(inside.Status == Status.Success, inside.Error);
            Assert.Equal(0, inside.Result.TriangleCount);
            // the same disc far from the ball (the sheet's pieces are all outside it)
            Assert.Equal(Refusal.UncutComponents, Run(Box(5, 5, 5, 6, 6, 6), disc).SheetReason);
        }

        [Fact]
        public void TwoShellsOneCutOneNot_UncutComponents_EvenWithAStatedSide()
        {
            // two separate cubes in one mesh; the plate cuts the first only. The second may be a part of its own or a cavity of the first: this version
            // does not look at the nesting and refuses, whatever UncutSide says
            DMesh3 two = TwoCubes(new Vector3d(0, 0, 0), new Vector3d(5, 0, 0));
            DMesh3 plate = Plate(0.5, -1, 2, -1, 2);
            foreach (var side in new[] { ExactMeshBoolean.SheetUncutSides.Refuse, ExactMeshBoolean.SheetUncutSides.Outside, ExactMeshBoolean.SheetUncutSides.Inside }) {
                ExactMeshBoolean op = Run(two, plate, side);
                AssertRefused(op, Refusal.UncutComponents, "two cubes " + side);
                Assert.Equal(1, op.SheetUncutCells);
            }
            // both cut: fine
            ExactMeshBoolean both = Run(two, Plate(0.5, -1, 7, -1, 2));
            Assert.True(both.Status == Status.Success, both.Error);
            Assert.Equal(1.0, Volume(both.Result), 12);
        }

        static DMesh3 TwoCubes(Vector3d a, Vector3d b)
        {
            var m = Box(a.x, a.y, a.z, a.x + 1, a.y + 1, a.z + 1);
            DMesh3 n = Box(b.x, b.y, b.z, b.x + 1, b.y + 1, b.z + 1);
            var map = new int[n.MaxVertexID];
            foreach (int v in n.VertexIndices())
                map[v] = m.AppendVertex(n.GetVertex(v));
            foreach (int t in n.TriangleIndices()) {
                Index3i f = n.GetTriangle(t);
                m.AppendTriangle(map[f.a], map[f.b], map[f.c]);
            }
            return m;
        }

        [Fact]
        public void YJunction_ThreeHalfPlanesMeetingInALine_NotSurface()
        {
            // three half-planes (the sheets of z = 0.5: x < 0.5, y = 0.5 above and below) share the edge x = 0.5, y = 0.5 -> as a vertical line of the cube
            var s = new Soup();
            Vector3d a = new Vector3d(0.5, 0.5, -1), b = new Vector3d(0.5, 0.5, 2);
            s.Tri(a, b, new Vector3d(2, 0.5, 2));
            s.Tri(a, new Vector3d(2, 0.5, 2), new Vector3d(2, 0.5, -1));
            s.Tri(a, b, new Vector3d(0.5, 2, 2));
            s.Tri(a, new Vector3d(0.5, 2, 2), new Vector3d(0.5, 2, -1));
            // (the third half-plane has copies of the two vertices of the line: a DMesh3 refuses a third triangle at an edge, an unwelded soup keeps it; the check welds by position)
            DMesh3 m = s.Mesh;
            int a2 = m.AppendVertex(a), b2 = m.AppendVertex(b), c2 = m.AppendVertex(new Vector3d(-1, -1, 2)), d2 = m.AppendVertex(new Vector3d(-1, -1, -1));
            Assert.True(m.AppendTriangle(a2, b2, c2) >= 0);
            Assert.True(m.AppendTriangle(a2, c2, d2) >= 0);
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), s.Mesh);
            AssertRefused(op, Refusal.NotSurface, "Y junction");
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.True(op.SheetWitnessTriangle >= 0);
        }

        [Fact]
        public void FlippedTriangle_OrientationInconsistent_NotRepaired()
        {
            // a 3 x 3 grid whose middle cell has both triangles flipped
            DMesh3 plate = Plate(0.5, -1, 2, -1, 2, true, 3);
            int count = 0;
            var flip = new List<int>();
            foreach (int t in plate.TriangleIndices()) {
                Index3i f = plate.GetTriangle(t);
                Vector3d c = (plate.GetVertex(f.a) + plate.GetVertex(f.b) + plate.GetVertex(f.c)) / 3;
                if (Math.Abs(c.x - 0.5) < 0.5 && Math.Abs(c.y - 0.5) < 0.5)
                    flip.Add(t);
            }
            Assert.Equal(2, flip.Count);
            foreach (int t in flip) {
                Index3i f = plate.GetTriangle(t);
                plate.SetTriangle(t, new Index3i(f.a, f.c, f.b));
                ++count;
            }
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), plate);
            AssertRefused(op, Refusal.OrientationInconsistent, "flipped cell");
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.Equal(0, op.ReorientedTriangles);
        }

        [Fact]
        public void TJunctionSheet_PatchesMeetingAtAVertexOfAnEdgeOfAnother_AreOneSurface()
        {
            // z = 0.5: the quad A (y up to 0.5) and the quads B1, B2 (from y = 0.5, split at x = 0.5): the vertex (0.5, 0.5) lies in the interior of A's edge,
            // and no input edge is used twice there: the sheet is a surface by geometry, not by connectivity
            var s = new Soup();
            Func<double, double, Vector3d> P = (x, y) => new Vector3d(x, y, 0.5);
            s.Quad(P(-1, -1), P(2, -1), P(2, 0.5), P(-1, 0.5));
            s.Quad(P(-1, 0.5), P(0.5, 0.5), P(0.5, 2), P(-1, 2));
            s.Quad(P(0.5, 0.5), P(2, 0.5), P(2, 2), P(0.5, 2));
            Cut(Box(0, 0, 0, 1, 1, 1), s.Mesh, 0.5, "T junction");
        }

        [Fact]
        public void SheetWithAHoleInsideTheWorkpiece_RimInside_Refused()
        {
            // a plate with a square hole in the middle of the cube: the hole's rim is inside
            var s = new Soup();
            Func<double, double, Vector3d> P = (x, y) => new Vector3d(x, y, 0.5);
            s.Quad(P(-1, -1), P(2, -1), P(2, 0.25), P(-1, 0.25));
            s.Quad(P(-1, 0.75), P(2, 0.75), P(2, 2), P(-1, 2));
            s.Quad(P(-1, 0.25), P(0.25, 0.25), P(0.25, 0.75), P(-1, 0.75));
            s.Quad(P(0.75, 0.25), P(2, 0.25), P(2, 0.75), P(0.75, 0.75));
            AssertRefused(Run(Box(0, 0, 0, 1, 1, 1), s.Mesh), Refusal.RimInsideWorkpiece, "hole");
        }

        // ---- roles ------------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(Ops.Union, Refusal.InUnion)]
        [InlineData(Ops.Intersection, Refusal.InIntersection)]
        public void Sheet_InUnionOrIntersection_Refused(Ops operation, Refusal reason)
        {
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2) }, operation) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(op.Compute());
            AssertRefused(op, reason, operation.ToString());
        }

        [Fact]
        public void Sheet_AsTheFirstOperand_Refused()
        {
            var op = new ExactMeshBoolean(new[] { Plate(0.5, -1, 2, -1, 2), Box(0, 0, 0, 1, 1, 1) }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid }
            };
            Assert.False(op.Compute());
            AssertRefused(op, Refusal.AsFirstOperand, "first");
        }

        [Fact]
        public void Sheet_WithAWindingPredicate_Refused_AndAnOpenWorkpiece()
        {
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2) }, (w) => w[0] > 0 && w[1] <= 0) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(op.Compute());
            AssertRefused(op, Refusal.WithWindingPredicate, "predicate");
            // an open workpiece
            AssertRefused(Run(Plate(0.5, -1, 2, -1, 2), Plate(0.25, -1, 2, -1, 2)), Refusal.WorkpieceNotClosed, "open workpiece");
        }

        [Fact]
        public void Roles_InvalidLists_AreInvalidInput_AndDefaultsAreSolid()
        {
            var op = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5), Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            op.Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, (ExactMeshBoolean.InputRole)7 };
            Assert.False(op.Compute());
            Assert.Equal(Status.InvalidInput, op.Status);
            // an empty or short list, or null: all solids (the usual boolean)
            op.Roles = new List<ExactMeshBoolean.InputRole>();
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1 - 0.125, Volume(op.Result), 12);
            Assert.Equal(Refusal.None, op.SheetReason);
            // a sheet in the second position that is also closed is still a sheet: a closed surface has no rim, but it is used by its labels: the box
            // here cuts the first one into an inside and an outside cell
            op.Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1 - 0.125, Volume(op.Result), 12);
        }

        // ---- invariance, reuse, determinism -------------------------------------------------------------------------------------------

        static string Fingerprint(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':');
                foreach (int v in new[] { f.a, f.b, f.c }) {
                    Vector3d p = m.GetVertex(v);
                    sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(' ');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        [Fact]
        public void Invariance_OverTheSimilarities_PowersOfTwoBitForBit()
        {
            // the cube and the deep cup: the labels do not depend on the frame; the 2^k scales give the same result, scaled, bit for bit
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), cup = Cup(0.25, 0.25, 1.25, 0.75, true);
            ExactMeshBoolean r0 = Run(cube, cup);
            Assert.True(r0.Status == Status.Success, r0.Error);
            foreach (Similarity s in Similarity.All()) {
                ExactMeshBoolean r = Run(Invariance.Transformed(cube, s), Invariance.Transformed(cup, s));
                Assert.True(r.Status == Status.Success, s.Name + ": " + r.Status + " " + r.Error);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Result));
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(r0.Result, s), r.Result));
                else {
                    double expected = Invariance.Volume(r0.Result) * s.Scale * s.Scale * s.Scale;
                    Assert.True(Math.Abs(Invariance.Volume(r.Result) - expected) <= 1e-4 * expected, s.Name + ": the volume");
                }
            }
            // a refusal is the same in every frame
            DMesh3 half = Plate(0.5, -1, 0.5, -1, 2, true, 2);
            foreach (Similarity s in Similarity.All()) {
                ExactMeshBoolean r = Run(Invariance.Transformed(cube, s), Invariance.Transformed(half, s));
                Assert.Equal(Refusal.RimInsideWorkpiece, r.SheetReason);
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_SameResult_AndAfterAChangedSheet()
        {
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2) }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.True(op.Compute(), op.Error);
            string first = Fingerprint(op.Result);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(first, Fingerprint(op.Result));
            // another sheet on the same instance: a refusal, then a result again, with no state left over
            op.Meshes[1] = Plate(0.5, -1, 0.5, -1, 2);
            Assert.False(op.Compute());
            Assert.Equal(Refusal.RimInsideWorkpiece, op.SheetReason);
            Assert.Null(op.Result);
            op.Meshes[1] = Plate(0.5, -1, 2, -1, 2);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(Refusal.None, op.SheetReason);
            Assert.Equal(-1, op.SheetWitnessTriangle);
            Assert.Equal(first, Fingerprint(op.Result));
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_AndRepeatedRuns()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), cup = Cup(0.25, 0.25, 1.25, 0.75, true), plate = Plate(0.5, -1, 2, -1, 2, true, 3);
            foreach (DMesh3 sheet in new[] { cup, plate }) {
                string serial;
                gParallel.SerialOnThisThread = true;
                try {
                    ExactMeshBoolean s = Run(cube, sheet);
                    Assert.True(s.Status == Status.Success, s.Error);
                    serial = Fingerprint(s.Result);
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
                for (int run = 0; run < 4; ++run) {
                    ExactMeshBoolean p = Run(cube, sheet);
                    Assert.True(p.Status == Status.Success, p.Error);
                    Assert.Equal(serial, Fingerprint(p.Result));
                }
            }
        }

        // a refusal names the same witness in every run (the arrangement's piece order is deterministic)
        [Fact]
        public void Determinism_RefusalWitness_SameEveryRun()
        {
            DMesh3 box = Box(0, 0, 0, 2, 1, 1);
            DMesh3 spiral = Extruded(new[] { 0.5, -1 }, new[] { 0.5, 2 }, new[] { 3.0, 2 }, new[] { 3.0, -2 }, new[] { 1.5, -2 }, new[] { 1.5, 1.5 });
            ExactMeshBoolean first = Run(box, spiral);
            for (int run = 0; run < 4; ++run) {
                ExactMeshBoolean r = Run(box, spiral);
                Assert.Equal(first.SheetWitnessTriangle, r.SheetWitnessTriangle);
                Assert.Equal(first.SheetWitnessTriangle2, r.SheetWitnessTriangle2);
                Assert.Equal(first.Error, r.Error);
            }
        }

        // ---- a closed second tool and two sheets ----------------------------------------------------------------------------------------

        [Fact]
        public void SheetAndAClosedTool_TheDifferenceOfBoth_AlsoWhereTheToolIsInTheRemovedHalf()
        {
            // the cube minus the sheet z = 0.5 (up: the upper half stays) minus a closed box that reaches from the removed half into the kept one
            // (its pieces in the removed half lie in the tool's cell: the sheet's label is theirs too)
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2), Box(0.5, 0, -0.5, 1.5, 1, 0.75) }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid }
            };
            Assert.True(op.Compute(), op.Error);
            ResultChecks.AssertClosed(op.Result);
            Assert.Equal(0.5 - 0.5 * 0.25, Volume(op.Result), 12);
            // the other side of the sheet: the lower half stays, minus the tool's part in it
            op.Meshes[1] = Plate(0.5, -1, 2, -1, 2, false);
            Assert.True(op.Compute(), op.Error);
            ResultChecks.AssertClosed(op.Result);
            Assert.Equal(0.5 - 0.5 * 0.5, Volume(op.Result), 12);
        }

        [Fact]
        public void ClosedToolFloatingInACell_NeitherTouchingTheSheetNorTheWorkpiece_Refused()
        {
            // a part of a tool inside the workpiece that touches nothing has no cell here (it needs the nesting of the shells): refused, whichever cell it is in
            foreach (double z in new[] { 0.1, 0.7 }) {
                var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2), Box(0.25, 0.25, z, 0.75, 0.75, z + 0.2) }, Ops.Difference) {
                    Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid },
                    SheetUncutSide = ExactMeshBoolean.SheetUncutSides.Outside
                };
                Assert.False(op.Compute());
                AssertRefused(op, Refusal.UncutComponents, "floating tool at " + z);
            }
        }

        [Fact]
        public void TwoSheets_EachCutsTheWorkpiece_TheCellsBehindEitherAreRemoved()
        {
            // z = 0.25 (up) and x = 0.75 (normal -x: behind it, at x > 0.75, is the tool): removed are z < 0.25 and x > 0.75
            var s = new Soup();
            s.Quad(new Vector3d(0.75, -1, -1), new Vector3d(0.75, -1, 2), new Vector3d(0.75, 2, 2), new Vector3d(0.75, 2, -1));
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.25, -1, 2, -1, 2), s.Mesh }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.True(op.Compute(), op.Error);
            ResultChecks.AssertClosed(op.Result);
            Assert.Equal(0.75 * 0.75 * 1.0, Volume(op.Result), 12);
        }

        [Fact]
        public void ClosedToolTouchingTheSheetAlongAnEdge_ItsInsideIsInTheCellOfItsOutside()
        {
            // a triangular prism (an edge on the plate z = 0.25, the apex above) inside the upper half: around the edge the tool's inside is a wedge that no
            // piece of the sheet bounds, it is the tool's pieces that join it to the cell; the prism leaves 0.75 - 0.125 * 0.5
            var s = new Soup();
            Func<double, double, double, Vector3d> P = (x, y, z) => new Vector3d(x, y, z);
            Vector3d a0 = P(0.25, 0.5, 0.25), b0 = P(0.25, 0.75, 0.75), c0 = P(0.25, 0.25, 0.75);
            Vector3d a1 = P(0.75, 0.5, 0.25), b1 = P(0.75, 0.75, 0.75), c1 = P(0.75, 0.25, 0.75);
            s.Tri(a0, c0, b0);                       // x = 0.25 (normal -x)
            s.Tri(a1, b1, c1);                       // x = 0.75
            s.Quad(a0, b0, b1, a1);                  // the side towards +y
            s.Quad(a0, a1, c1, c0);                  // the side towards -y
            s.Quad(c0, c1, b1, b0);                  // the top
            Assert.True(Volume(s.Mesh) > 0, "the prism is a solid");
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.25, -1, 2, -1, 2), s.Mesh }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid }
            };
            Assert.True(op.Compute(), op.SheetReason + " " + op.Error);
            ResultChecks.AssertClosed(op.Result);
            Assert.Equal(0.75 - Volume(s.Mesh), Volume(op.Result), 12);
        }

        [Fact]
        public void TwoSheets_TheSecondOnesRimIsInside_TheWitnessNamesTheSecond()
        {
            // the first sheet separates the cube; the second is a patch inside it, with a free edge all around: its own pass refuses, the first sees its pieces as no wall
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.25, -1, 2, -1, 2), Plate(0.75, 0.25, 0.75, 0.25, 0.75) }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(op.Compute());
            AssertRefused(op, Refusal.RimInsideWorkpiece, "second sheet");
            Assert.Equal(2, op.SheetWitnessMesh);
        }

        [Fact]
        public void SheetPartlyInThePlaneOfAFace_TheRimOnTheSurface_LabelsTheCellByContact()
        {
            // the plate z = 1 over x in [0.5, 2] touches the top face of the cube on a part of it, its rim (x = 0.5) runs on the face: a contact only
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            ExactMeshBoolean up = Run(cube, Plate(1, 0.5, 2, -1, 2, true));
            Assert.True(up.Status == Status.Success, up.SheetReason + " " + up.Error);
            Assert.Equal(0.0, Volume(up.Result), 12);
            Cut(cube, Plate(1, 0.5, 2, -1, 2, false), 1, "partial contact, outside the tool");
        }

        [Fact]
        public void WorkpieceWithAFaceSharedByTwoOfItsShells_IsNoWall()
        {
            // two cubes of one mesh that share the face x = 1 (two coincident triangles of opposite orientation: a stack with winding 1 on both sides):
            // the plate z = 0.5 leaves the two upper halves, 1 in all, and the shared face is no wall of the cells
            DMesh3 two = TwoCubes(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
            ExactMeshBoolean op = Run(two, Plate(0.5, -1, 3, -1, 2));
            Assert.True(op.Status == Status.Success, op.SheetReason + " " + op.Error);
            ResultChecks.AssertClosed(op.Result);
            Assert.Equal(1.0, Volume(op.Result), 12);
        }
    }
}
