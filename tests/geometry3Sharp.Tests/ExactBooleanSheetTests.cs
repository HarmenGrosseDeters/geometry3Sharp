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
        public void SheetInThePlaneOfAFace_OnlyTouches_IsRefusedAsUncut_WhateverTheSideAndTheStatedUncutSide()
        {
            // the sheet lies on the top face of the cube: no piece of it is inside, so it separates nothing. Its contact would mark the whole cube: with the
            // normals up the cube would be behind it (removed, Success), with the normals down in front of it (kept, Success, as if no cut was asked): both
            // are refused (owner decision on #1211, 2026-11-22), whatever UncutSide says
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            foreach (bool up in new[] { true, false })
                foreach (var side in new[] { ExactMeshBoolean.SheetUncutSides.Refuse, ExactMeshBoolean.SheetUncutSides.Outside, ExactMeshBoolean.SheetUncutSides.Inside }) {
                    ExactMeshBoolean op = Run(cube, Plate(1, -1, 2, -1, 2, up), side);
                    AssertRefused(op, Refusal.UncutComponents, "full coplanar sheet, up " + up + ", UncutSide " + side);
                    Assert.Equal(1, op.SheetWitnessMesh);
                    Assert.True(op.SheetWitnessTriangle >= 0 && op.SheetWitnessTriangle < 2);       // (a triangle of the plate, which has two)
                    Assert.Equal(1, op.SheetUncutCells);
                    Assert.Contains("only touches", op.Error);
                    Assert.Null(op.Result);
                }
            // a full coplanar sheet on each of the six faces, both ways: whichever piece of the workpiece comes first, the sheet only touches
            foreach (int axis in new[] { 0, 1, 2 })
                foreach (double at in new[] { 0.0, 1.0 })
                    foreach (bool plus in new[] { true, false }) {
                        Func<double, double, Vector3d> at2 = (u, v) => axis == 0 ? new Vector3d(at, u, v) : axis == 1 ? new Vector3d(u, at, v) : new Vector3d(u, v, at);
                        var normal = new Vector3d(axis == 0 ? (plus ? 1 : -1) : 0, axis == 1 ? (plus ? 1 : -1) : 0, axis == 2 ? (plus ? 1 : -1) : 0);
                        DMesh3 face = Page(at2(-1, -1), at2(2, -1), at2(2, 2), at2(-1, 2), normal);
                        AssertRefused(Run(cube, face), Refusal.UncutComponents, "full sheet on face " + axis + "/" + at + ", normal plus " + plus);
                    }
            // a patch inside the face (the rim on the face), both ways, and a plate that covers a part of the face and goes on outside the cube
            AssertRefused(Run(cube, Plate(1, 0.25, 0.75, 0.25, 0.75, true)), Refusal.UncutComponents, "patch on the face, up");
            AssertRefused(Run(cube, Plate(1, 0.25, 0.75, 0.25, 0.75, false)), Refusal.UncutComponents, "patch on the face, down");
            // a sheet that separates (a piece inside) is still a cut, with the plate's part on the face as well
            Cut(cube, Plate(0.5, -1, 2, -1, 2), 0.5, "plate through the cube");
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

        // a hollow block (the cube [0,10]^3 and a cavity [4,6]^3 with inward normals) and a sheet of two components that mark its material from both
        // sides: a plate z = 2 through the block (the material above it is in front, label 0: the cells of the outer shell) and a cap on the cavity's
        // top face (a pyramid in the material above it, the rim on that face, its normals pointing down into the pocket: the material outside the cap
        // is behind it, label 1: the cells of the cavity's shell). The two shells are not joined by an edge, so these are two cells of one region of
        // material with different labels: the nesting of the shells would be needed to see it. The boxes of the shells nest, so a sheet that puts both
        // labels on them is refused (SidesInconsistent), also where it is consistent; shells that cannot nest (disjoint boxes) are independent
        static DMesh3 HollowBlock() { return Joined(Box(0, 0, 0, 10, 10, 10), Reversed(Box(4, 4, 4, 6, 6, 6))); }

        static DMesh3 CapOnTheCavity(bool intoThePocket)
        {
            var m = new DMesh3();
            int b0 = m.AppendVertex(new Vector3d(4.5, 4.5, 6)), b1 = m.AppendVertex(new Vector3d(5.5, 4.5, 6)), b2 = m.AppendVertex(new Vector3d(5.5, 5.5, 6)),
                b3 = m.AppendVertex(new Vector3d(4.5, 5.5, 6)), a = m.AppendVertex(new Vector3d(5, 5, 7));
            m.AppendTriangle(b0, b1, a); m.AppendTriangle(b1, b2, a); m.AppendTriangle(b2, b3, a); m.AppendTriangle(b3, b0, a);      // (normals away from the pocket)
            return intoThePocket ? Reversed(m) : m;
        }

        [Fact]
        public void HollowBlock_BothLabelsOnNestedShells_IsRefused_NotAGarbledResult()
        {
            DMesh3 plate = Plate(2, -1, 11, -1, 11);
            // the contradiction: the material outside the cap is behind the cap (label 1) and in front of the plate (label 0)
            ExactMeshBoolean bad = Run(HollowBlock(), Joined(plate, CapOnTheCavity(true)));
            AssertRefused(bad, Refusal.SidesInconsistent, "plate and a cap that points into the pocket");
            Assert.Equal(1, bad.SheetConflictingCells);
            // the cap the other way (the material outside it in front, label 0, as in front of the plate): consistent, but both labels occur on the
            // outer shell (the plate cuts it) and the cavity's shell may be inside it: refused, conservatively (the documented limit)
            AssertRefused(Run(HollowBlock(), Joined(plate, CapOnTheCavity(false))), Refusal.SidesInconsistent, "plate and a cap that points away: the limit");
            // a plane through the block and the cavity (a legitimate cut of a hollow body): also refused, for the same reason
            AssertRefused(Run(HollowBlock(), Plate(5, -1, 11, -1, 11)), Refusal.SidesInconsistent, "a plate through the cavity: the limit");
        }

        [Fact]
        public void HollowBlock_TheCavityShellFirst_IsRefusedAsWell()
        {
            // (the outer shell is the later one: the nesting test holds in both directions)
            DMesh3 block = Joined(Reversed(Box(4, 4, 4, 6, 6, 6)), Box(0, 0, 0, 10, 10, 10));
            AssertRefused(Run(block, Joined(Plate(2, -1, 11, -1, 11), CapOnTheCavity(true))), Refusal.SidesInconsistent, "cavity shell first");
        }

        // a square patch in the plane z = z, with its rim on the face of the block there (the cavity's top face z = 6, the block's top face z = 10): a
        // coplanar patch labels the whole cell of the workpiece that it lies on, by the side its normal points to
        static DMesh3 Patch(double z, double x0, double x1, bool up) { return Plate(z, x0, x1, x0, x1, up); }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HollowBlock_OneLabelOnEachNestedShell_IsRefused_InEitherShellOrder(bool cavityFirst)
        {
            // the cavity's top face patched with the normal up (the material above it is in front: label 0 on the cavity's shell only) and the block's top
            // face patched with the normal up too (the material is behind it: label 1 on the block's shell only): the two cells may be one region
            DMesh3 outer = Box(0, 0, 0, 10, 10, 10), cavity = Reversed(Box(4, 4, 4, 6, 6, 6));
            DMesh3 block = cavityFirst ? Joined(cavity, outer) : Joined(outer, cavity);
            DMesh3 sheet = Joined(Patch(6, 4, 6, true), Patch(10, 0, 10, true));
            ExactMeshBoolean op = Run(block, sheet);
            AssertRefused(op, Refusal.SidesInconsistent, "label 0 on the cavity, 1 on the block, cavity first " + cavityFirst);
            Assert.True(op.SheetWitnessTriangle >= 0 && op.SheetWitnessTriangle2 >= 0 && op.SheetWitnessTriangle != op.SheetWitnessTriangle2);
            // the same label on both (the block's top face patched the other way) is no contradiction, but patches on the faces only touch the block: refused as
            // an uncut sheet (they separate nothing), not as a cut that keeps the whole hollow block
            ExactMeshBoolean same = Run(block, Joined(Patch(6, 4, 6, true), Patch(10, 0, 10, false)));
            AssertRefused(same, Refusal.UncutComponents, "patches on the faces of both shells, one label");
            Assert.Equal(2, same.SheetUncutCells);       // (the material of the block and of its cavity: two cells)
        }

        [Fact]
        public void SiblingCavities_WithDifferentLabels_InABlockWhoseShellIsUnmarked_AreRefused()
        {
            // two cavities with disjoint boxes (they cannot nest in each other), a cap on each, one pointing into its pocket (label 1 outside it) and
            // one away (label 0): the material outside the caps is one region with both labels. The block's shell carries no mark, so the refusal is
            // the uncut cell of it (the nesting test pairs each cavity only with the block): this test pins that dependency
            DMesh3 block = Joined(Joined(Box(0, 0, 0, 10, 10, 10), Reversed(Box(1, 1, 1, 3, 3, 3))), Reversed(Box(6, 6, 6, 8, 8, 8)));
            Func<double, bool, DMesh3> cap = (c, into) => {
                var m = new DMesh3();
                int b0 = m.AppendVertex(new Vector3d(c + 0.5, c + 0.5, c + 2)), b1 = m.AppendVertex(new Vector3d(c + 1.5, c + 0.5, c + 2)), b2 = m.AppendVertex(new Vector3d(c + 1.5, c + 1.5, c + 2)),
                    b3 = m.AppendVertex(new Vector3d(c + 0.5, c + 1.5, c + 2)), a = m.AppendVertex(new Vector3d(c + 1, c + 1, c + 3));
                m.AppendTriangle(b0, b1, a); m.AppendTriangle(b1, b2, a); m.AppendTriangle(b2, b3, a); m.AppendTriangle(b3, b0, a);
                return into ? Reversed(m) : m;
            };
            ExactMeshBoolean op = Run(block, Joined(cap(1, true), cap(6, false)));
            Assert.True(op.Status == Status.SheetRefused, op.Status + " " + op.Error);
            Assert.NotEqual(Refusal.None, op.SheetReason);
            Assert.Null(op.Result);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void HollowBlock_Refusals_AreInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            DMesh3 plate = Plate(2, -1, 11, -1, 11);
            foreach (var sheet in new[] { Joined(plate, CapOnTheCavity(true)), Joined(plate, CapOnTheCavity(false)), Plate(5, -1, 11, -1, 11) }) {
                ExactMeshBoolean op = Run(Invariance.Transformed(HollowBlock(), s), Invariance.Transformed(sheet, s));
                Assert.True(op.Status == Status.SheetRefused, frame + ": " + op.Status + " " + op.Error);
                // (a frame that rounds the coordinates moves the cap's rim off the cavity's face: then the rim is inside the workpiece, also a refusal)
                if (s.IsExact)
                    Assert.Equal(Refusal.SidesInconsistent, op.SheetReason);
            }
            // shells that cannot nest: fine in every frame
            ExactMeshBoolean ok = Run(Invariance.Transformed(TwoCubes(new Vector3d(0, 0, 0), new Vector3d(5, 0, 0)), s), Invariance.Transformed(Plate(0.5, -1, 7, -1, 2), s));
            Assert.True(ok.Status == Status.Success, frame + ": " + ok.Status + " " + ok.SheetReason + " " + ok.Error);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }

        [Fact]
        public void TwoShells_WhoseBoxesDoNotNest_AreIndependent_AndBothLabelsAreFine()
        {
            // two cubes with disjoint boxes, both cut by one plate: both labels occur on the two shells, which cannot nest: fine (the hollow block
            // above, whose shells nest, is refused)
            DMesh3 two = TwoCubes(new Vector3d(0, 0, 0), new Vector3d(5, 0, 0));
            ExactMeshBoolean both = Run(two, Plate(0.5, -1, 7, -1, 2));
            Assert.True(both.Status == Status.Success, both.Status + " " + both.SheetReason + " " + both.Error);
            Assert.Equal(1.0, Volume(both.Result), 12);
        }

        // a page: the quad with the edge a0-a1 on the line, extending to b1-b0, normal on the given side
        static DMesh3 Page(Vector3d a0, Vector3d a1, Vector3d b1, Vector3d b0, Vector3d normal)
        {
            var s = new Soup();
            Vector3d n = (a1 - a0).Cross(b1 - a0);
            if (n.Dot(normal) > 0)
                s.Quad(a0, a1, b1, b0);
            else
                s.Quad(a0, b0, b1, a1);
            return s.Mesh;
        }

        // four half-planes from the line y = z = 0.5 (along x from -1 to 2) with alternating labels: a checkerboard of the four quarters
        // (+y, +z, -y, -z pages: the normals +z, +y, -z, -y), each line edge cut at other points of x
        static DMesh3 HalfPlane(double[] cuts, Vector3d dir, Vector3d normal)
        {
            var xs = new List<double> { -1 };
            xs.AddRange(cuts);
            xs.Add(2);
            DMesh3 m = null;
            for (int i = 0; i + 1 < xs.Count; ++i) {
                var a0 = new Vector3d(xs[i], 0.5, 0.5);
                var a1 = new Vector3d(xs[i + 1], 0.5, 0.5);
                DMesh3 q = Page(a0, a1, a1 + dir * 2, a0 + dir * 2, normal);
                m = m == null ? q : Joined(m, q);
            }
            return m;
        }

        static DMesh3 Cross(double[] cutsPy, double[] cutsPz, double[] cutsMy, double[] cutsMz)
        {
            DMesh3 result = HalfPlane(cutsPy, new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
            result = Joined(result, HalfPlane(cutsPz, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0)));
            result = Joined(result, HalfPlane(cutsMy, new Vector3d(0, -1, 0), new Vector3d(0, 0, -1)));
            return Joined(result, HalfPlane(cutsMz, new Vector3d(0, 0, -1), new Vector3d(0, -1, 0)));
        }

        [Fact]
        public void FourPagesAtOneLine_SameEdgeKeys_NotSurface_DifferentSubdivision_IsTheCheckerboard()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            var none = new double[0];
            // (the four pages with the same edge: four triangles at one edge: not a surface)
            AssertRefused(Run(cube, Cross(none, none, none, none)), Refusal.NotSurface, "four pages, one edge key");
            // each page's line edge cut at other places: the pages meet along a line subdivided differently. After the arrangement four pieces meet at
            // every piece of the line, and their sides alternate (0, 1, 0, 1): the labelling is consistent, the result is the exact point set: two
            // opposite quarters of the cube (the ones the normals point into): volume 1/2, closed, with the quarters touching along the line
            ExactMeshBoolean op = Run(cube, Cross(new[] { 0.5 }, new[] { 0.25, 1.25 }, none, new[] { 0.75 }));
            Assert.True(op.Status == Status.Success, op.Status + " " + op.SheetReason + " " + op.Error);
            Assert.Equal(0.5, Volume(op.Result), 12);
            Assert.True(ExactMeshBoolean.IsWindingClosed(op.Result));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result).Replace("self-intersections", ""));
        }

        [Fact]
        public void ASheetOutsideTheWorkpiece_TouchingItAtAnEdgeOrAVertex_CutsNothing()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // plates in the plane of the top face, outside the cube, touching it along the edge x = 1 and at the corner (1, 1, 1): no piece of
            // them is inside the cube, so the sheet marks nothing: refused as an uncut part by default, the stated side decides
            foreach (DMesh3 plate in new[] { Plate(1, 1, 3, 0, 1), Plate(1, 1, 3, 1, 3) }) {
                AssertRefused(Run(cube, plate), Refusal.UncutComponents, "outside, touching");
                ExactMeshBoolean outside = Run(cube, plate, ExactMeshBoolean.SheetUncutSides.Outside);
                Assert.True(outside.Status == Status.Success, outside.Status + " " + outside.SheetReason + " " + outside.Error);
                Assert.Equal(1.0, Volume(outside.Result), 12);
                ExactMeshBoolean inside = Run(cube, plate, ExactMeshBoolean.SheetUncutSides.Inside);
                Assert.True(inside.Status == Status.Success, inside.Status + " " + inside.SheetReason + " " + inside.Error);
                Assert.Equal(0, inside.Result.TriangleCount);
            }
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
        public void SheetFoldedOntoAFace_WithAPieceInside_IsStillACut()
        {
            // a patch on the top face of the cube (x in [0, 0.5], normals up) folded down into a wall at x = 0.5 (normals +x): the patch only touches the
            // surface, the wall is inside; the whole sheet separates the left half (behind the wall, under the patch: inside the tool) from the right half
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 patch = Page(new Vector3d(0, -1, 1), new Vector3d(0.5, -1, 1), new Vector3d(0.5, 2, 1), new Vector3d(0, 2, 1), new Vector3d(0, 0, 1));
            DMesh3 wall = Page(new Vector3d(0.5, -1, 1), new Vector3d(0.5, 2, 1), new Vector3d(0.5, 2, -1), new Vector3d(0.5, -1, -1), new Vector3d(1, 0, 0));
            ExactMeshBoolean op = Run(cube, Joined(patch, wall));
            Assert.True(op.Status == Status.Success, op.Status + " " + op.SheetReason + " " + op.Error);
            Assert.Equal(0.5, Volume(op.Result), 12);
            ResultChecks.AssertClosed(op.Result);
        }

        [Fact]
        public void SheetPartlyInThePlaneOfAFace_TheRimOnTheSurface_OnlyTouches_IsRefused()
        {
            // the plate z = 1 over x in [0.5, 2] touches the top face of the cube on a part of it, its rim (x = 0.5) runs on the face: a contact only
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            AssertRefused(Run(cube, Plate(1, 0.5, 2, -1, 2, true)), Refusal.UncutComponents, "partial contact, up");
            AssertRefused(Run(cube, Plate(1, 0.5, 2, -1, 2, false)), Refusal.UncutComponents, "partial contact, down");
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

        // ---- a part of the sheet that floats inside a cell (no edge in common with the surface or the rest of the sheet) -----------------------------

        static DMesh3 Joined(DMesh3 a, DMesh3 b)
        {
            DMesh3 m = new DMesh3(a);
            var map = new int[b.MaxVertexID];
            foreach (int v in b.VertexIndices())
                map[v] = m.AppendVertex(b.GetVertex(v));
            foreach (int t in b.TriangleIndices()) {
                Index3i f = b.GetTriangle(t);
                m.AppendTriangle(map[f.a], map[f.b], map[f.c]);
            }
            return m;
        }

        static void AssertNothingInsideOut(ExactMeshBoolean op, string what)
        {
            if (op.Status == Status.Success) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
                Assert.True(Volume(op.Result) >= 0, what + ": negative volume");
            }
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(0.7)]
        public void ABubbleOfTheSheetFloatingInACell_IsRefused_NeverInsideOut(double z)
        {
            // the plate z = 0.5 (up) and a closed box with outward normals at z .. z + 0.2, a separate component of the sheet: behind the plate (z = 0.1) its inside is
            // marked 1 in a cell that is also the region behind the plate, and the cell outside the box is marked 0 by the box: a contradiction that no shared edge
            // shows. In the kept half (z = 0.7) it would be a correct cavity, but it is refused as well: the cells are joined across edges only
            DMesh3 sheet = Joined(Plate(0.5, -1, 2, -1, 2), Box(0.25, 0.25, z, 0.75, 0.75, z + 0.2));
            foreach (var side in new[] { ExactMeshBoolean.SheetUncutSides.Refuse, ExactMeshBoolean.SheetUncutSides.Outside, ExactMeshBoolean.SheetUncutSides.Inside }) {
                ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), sheet, side);
                AssertRefused(op, Refusal.FloatingComponent, "bubble at " + z + ", " + side);
                Assert.Equal(1, op.SheetWitnessMesh);
                Assert.True(op.SheetWitnessTriangle >= 0);
            }
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(0.7)]
        public void AnInvertedBubbleOfTheSheet_FloatingInACell_IsRefused_WithTheFrontMarkAsWitness(double z)
        {
            // the bubble with inward normals: its inside is in front of its pieces (a 0 mark, no 1 mark: the witness is the front mark), the same
            // refusal as for the outward bubble, whose floating cell carries a 1 mark only
            DMesh3 sheet = Joined(Plate(0.5, -1, 2, -1, 2), Reversed(Box(0.25, 0.25, z, 0.75, 0.75, z + 0.2)));
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), sheet);
            AssertRefused(op, Refusal.FloatingComponent, "inverted bubble at " + z);
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.True(op.SheetWitnessTriangle >= 0);
            Assert.True(op.SheetConflictingCells >= 1);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.SheetUncutSides.Refuse)]
        [InlineData(ExactMeshBoolean.SheetUncutSides.Outside)]
        [InlineData(ExactMeshBoolean.SheetUncutSides.Inside)]
        public void ALoneClosedSheetInsideTheCube_IsRefused_WhateverTheUncutSide(ExactMeshBoolean.SheetUncutSides side)
        {
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), Box(0.25, 0.25, 0.25, 0.75, 0.75, 0.75), side);
            AssertRefused(op, Refusal.FloatingComponent, "lone box " + side);
            AssertNothingInsideOut(op, "lone box");
        }

        [Fact]
        public void ABubbleOfAnotherSheet_AloneInACell_IsRefused()
        {
            // the same with the bubble as a sheet of its own: in its pass it floats too
            var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2), Box(0.25, 0.25, 0.1, 0.75, 0.75, 0.3) }, Ops.Difference) {
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(op.Compute());
            Assert.Equal(Status.SheetRefused, op.Status);
            AssertNothingInsideOut(op, "two sheets");
        }

        // ---- together with the orientation modes ---------------------------------------------------------------------------------------------

        [Fact]
        public void ClosedToolWithFlippedFaces_AndASheet_RefusedByDefault_OrientedByNestingOrAreaMajority()
        {
            // the tool box [0.5,1.5] x [0,1] x [-0.5,0.75] with its top face flipped (it is closed once the face is turned): Orientation is for the solids,
            // the sheet is used as given and cuts as before (the cube minus the lower half minus the tool's part in the upper half: 0.5 - 0.5 * 0.25)
            DMesh3 tool = Box(0.5, 0, -0.5, 1.5, 1, 0.75);
            foreach (int t in tool.TriangleIndices().ToArray()) {
                Index3i f = tool.GetTriangle(t);
                if (tool.GetVertex(f.a).z == 0.75 && tool.GetVertex(f.b).z == 0.75 && tool.GetVertex(f.c).z == 0.75)
                    tool.SetTriangle(t, new Index3i(f.a, f.c, f.b));
            }
            Func<ExactMeshBoolean.OrientationModes, ExactMeshBoolean> run = mode => {
                var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2), tool }, Ops.Difference) {
                    Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Solid },
                    Orientation = mode
                };
                op.Compute();
                return op;
            };
            ExactMeshBoolean refused = run(ExactMeshBoolean.OrientationModes.Refuse);
            Assert.Equal(Status.InconsistentOrientation, refused.Status);
            Assert.Contains("input mesh 2", refused.Error);
            Assert.Equal(Refusal.None, refused.SheetReason);
            foreach (var mode in new[] { ExactMeshBoolean.OrientationModes.ByNesting, ExactMeshBoolean.OrientationModes.AreaMajority }) {
                ExactMeshBoolean op = run(mode);
                Assert.True(op.Status == Status.Success, mode + ": " + op.Status + " " + op.Error);
                ResultChecks.AssertClosed(op.Result);
                Assert.Equal(0.5 - 0.5 * 0.25, Volume(op.Result), 12);
                Assert.Equal(2, op.ReorientedTriangles);        // (the flipped face's two triangles; none of the sheet's)
            }
        }

        [Fact]
        public void ASheetIsNeverOrientedByNesting_OrCountedInsideOut()
        {
            // a closed box with its normals inward as the sheet: with the role Sheet it is used as given (its inside is in front of it, outside the tool), not
            // turned by nesting and not counted as inside-out
            DMesh3 inner = Reversed(Box(0.25, 0.25, 0.25, 0.75, 0.75, 0.75));
            foreach (var mode in new[] { ExactMeshBoolean.OrientationModes.Refuse, ExactMeshBoolean.OrientationModes.ByNesting }) {
                ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), inner, ExactMeshBoolean.SheetUncutSides.Refuse, o => { o.Orientation = mode; o.CountInsideOutCells = true; });
                Assert.True(op.Status == Status.Success || op.SheetReason == Refusal.FloatingComponent, mode + ": " + op.Status + " " + op.Error);
                Assert.Equal(0, op.InsideOutCells);
                Assert.Equal(0, op.ReorientedTriangles);
            }
        }

        // ---- the plumbing around the labels (pinned for the mutation gate of the sheet role) -----------------------------------------------------

        static ExactMeshBoolean RunAll(DMesh3[] meshes, params ExactMeshBoolean.InputRole[] roles)
        {
            var op = new ExactMeshBoolean(meshes, Ops.Difference) { Roles = roles.ToList() };
            op.Compute();
            return op;
        }

        // as RunAll, with the open inputs that have the role Solid classified by the generalized winding number
        static ExactMeshBoolean RunAllOpen(DMesh3[] meshes, params ExactMeshBoolean.InputRole[] roles)
        {
            var op = new ExactMeshBoolean(meshes, Ops.Difference) { Roles = roles.ToList(), AllowOpenMeshes = true };
            op.Compute();
            return op;
        }

        const ExactMeshBoolean.InputRole SolidRole = ExactMeshBoolean.InputRole.Solid, SheetRole = ExactMeshBoolean.InputRole.Sheet;

        [Fact]
        public void AnOpenSheet_IsNoOpenInput_AnOpenSolidNextToASheetIs()
        {
            // AllowOpenMeshes = false refuses the open meshes that are classified by generalized winding numbers. A sheet is open by its role and is not one of them
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            ExactMeshBoolean op = Run(cube, Plate(0.5, -1, 2, -1, 2), ExactMeshBoolean.SheetUncutSides.Refuse, o => o.AllowOpenMeshes = false);
            Assert.True(op.Status == Status.Success, op.Status + " " + op.Error);
            Assert.Equal(0.5, Volume(op.Result), 12);
            ResultChecks.AssertClosed(op.Result);
            // an open solid is still refused, next to an open sheet and next to a closed one (the role of one mesh does not excuse another)
            foreach (DMesh3 sheet in new[] { Plate(0.5, -1, 2, -1, 2), Box(-1, -1, -1, 2, 2, 0.5) }) {
                var open = new ExactMeshBoolean(new[] { cube, sheet, Plate(0.75, -1, 2, -1, 2) }, Ops.Difference) {
                    Roles = new List<ExactMeshBoolean.InputRole> { SolidRole, SheetRole, SolidRole },
                    AllowOpenMeshes = false
                };
                Assert.False(open.Compute());
                Assert.Equal(Status.OpenInput, open.Status);
                Assert.Equal(Refusal.None, open.SheetReason);
            }
        }

        [Fact]
        public void Witnesses_AreMinusOne_BeforeACompute_AndAfterASuccess()
        {
            var fresh = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5), Ops.Difference);
            Assert.Equal(-1, fresh.SheetWitnessMesh);
            Assert.Equal(-1, fresh.SheetWitnessTriangle);
            Assert.Equal(-1, fresh.SheetWitnessTriangle2);
            Assert.Equal(Refusal.None, fresh.SheetReason);
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2));
            Assert.True(op.Status == Status.Success, op.Error);
            Assert.Equal(-1, op.SheetWitnessMesh);
            Assert.Equal(-1, op.SheetWitnessTriangle);
            Assert.Equal(-1, op.SheetWitnessTriangle2);
        }

        [Theory]
        [InlineData(0, double.NaN)]
        [InlineData(1, double.NaN)]
        [InlineData(2, double.NaN)]
        [InlineData(0, double.PositiveInfinity)]
        [InlineData(1, double.NegativeInfinity)]
        [InlineData(2, double.PositiveInfinity)]
        public void SheetWithANonFiniteCoordinate_InOneComponentOnly_IsInvalidInput(int component, double value)
        {
            DMesh3 plate = Plate(0.5, -1, 2, -1, 2);
            Vector3d v = plate.GetVertex(3);
            v[component] = value;
            plate.SetVertex(3, v);
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), plate);
            Assert.Equal(Status.InvalidInput, op.Status);
            Assert.Contains("non-finite", op.Error);
            Assert.Equal(Refusal.None, op.SheetReason);
            Assert.Null(op.Result);
        }

        [Fact]
        public void SheetTrianglesWithTwoCornersAtOnePoint_AreNoEdgesOfTheSurfaceCheck()
        {
            // the plate and three triangles without area next to it, each with two corners at the same point p (vertices of their own) and a third corner
            // of its own: the edge from p to p is no edge (three of them would be an edge with three triangles), and the other two edges of each run
            // through the same two points in opposite directions. The sheet is a surface, and the plate cuts as before
            DMesh3 sheet = Plate(0.5, -1, 2, -1, 2);
            var p = new Vector3d(4, 0, 0.5);
            for (int k = 0; k < 3; ++k) {
                int a = sheet.AppendVertex(p), b = sheet.AppendVertex(p), c = sheet.AppendVertex(new Vector3d(5, k, 0.5));
                Assert.True(sheet.AppendTriangle(a, b, c) >= 0);
            }
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), sheet);
            Assert.True(op.Status == Status.Success, op.Status + " " + op.SheetReason + " " + op.Error);
            Assert.Equal(0.5, Volume(op.Result), 12);
            ResultChecks.AssertClosed(op.Result);
        }

        [Fact]
        public void NotSurface_AndOrientationInconsistent_NameTheLowestTriangleAtTheEdge()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // a plate away from the junction (triangles 0 and 1), then three half-planes at the line x = y = 0.5 whose triangles have the line as their
            // second or third edge (triangles 2, 3 and 4, then their other halves): the edge is used by 2, 3 and 4, and 2 is named
            DMesh3 m = Plate(-3, -1, 2, -1, 2);
            Vector3d a = new Vector3d(0.5, 0.5, -1), b = new Vector3d(0.5, 0.5, 2);
            Vector3d[] far = { new Vector3d(2, 0.5, 2), new Vector3d(0.5, 2, 2), new Vector3d(-1, -1, 2) };
            Vector3d[] farLow = { new Vector3d(2, 0.5, -1), new Vector3d(0.5, 2, -1), new Vector3d(-1, -1, -1) };
            var top = new int[3]; var ia = new int[3];
            for (int k = 0; k < 3; ++k) {
                ia[k] = m.AppendVertex(a);
                int ib = m.AppendVertex(b);
                top[k] = m.AppendVertex(far[k]);
                // (the line a-b is the edge from the second corner to the third (k = 0, 2) or from the third to the first (k = 1))
                Assert.Equal(2 + k, k == 1 ? m.AppendTriangle(ib, top[k], ia[k]) : m.AppendTriangle(top[k], ib, ia[k]));
            }
            for (int k = 0; k < 3; ++k)
                Assert.True((k == 1 ? m.AppendTriangle(ia[k], top[k], m.AppendVertex(farLow[k])) : m.AppendTriangle(top[k], ia[k], m.AppendVertex(farLow[k]))) >= 0);
            ExactMeshBoolean op = Run(cube, m);
            AssertRefused(op, Refusal.NotSurface, "Y junction after a plate");
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.Equal(2, op.SheetWitnessTriangle);
            Assert.Contains("input 1 (a sheet)", op.Error);
            Assert.Contains("triangle 2", op.Error);

            // three triangles at one edge after the plate, the edge as their first, second or third edge, in either direction: triangle 2 is named
            for (int corner = 0; corner < 3; ++corner)
                foreach (bool forward in new[] { true, false }) {
                    DMesh3 y = Plate(-3, -1, 2, -1, 2);
                    for (int k = 0; k < 3; ++k) {
                        int[] v = { y.AppendVertex(forward ? a : b), y.AppendVertex(forward ? b : a), y.AppendVertex(far[k]) };
                        Assert.Equal(2 + k, y.AppendTriangle(v[(3 - corner) % 3], v[(4 - corner) % 3], v[(5 - corner) % 3]));
                    }
                    ExactMeshBoolean junction = Run(cube, y);
                    AssertRefused(junction, Refusal.NotSurface, "junction, edge " + corner + ", forward " + forward);
                    Assert.Equal(2, junction.SheetWitnessTriangle);
                }

            // a 3 x 3 grid with the two triangles of the middle cell (8 and 9) flipped: the lowest triangle at an edge that two triangles run through in
            // the same direction is 2 (the cell at x < 0, whose first triangle has the edge x = 0 as its second edge)
            DMesh3 plate = Plate(0.5, -1, 2, -1, 2, true, 3);
            foreach (int t in new[] { 8, 9 }) {
                Index3i f = plate.GetTriangle(t);
                Vector3d c = (plate.GetVertex(f.a) + plate.GetVertex(f.b) + plate.GetVertex(f.c)) / 3;
                Assert.True(Math.Abs(c.x - 0.5) < 0.5 && Math.Abs(c.y - 0.5) < 0.5, "a triangle of the middle cell");
                plate.SetTriangle(t, new Index3i(f.a, f.c, f.b));
            }
            ExactMeshBoolean flipped = Run(cube, plate);
            AssertRefused(flipped, Refusal.OrientationInconsistent, "flipped cell");
            Assert.Equal(1, flipped.SheetWitnessMesh);
            Assert.Equal(2, flipped.SheetWitnessTriangle);
            Assert.Contains("triangle 2", flipped.Error);
        }

        [Fact]
        public void OpenWindingRecord_OnlyWhenAsked_AndTheOpenStepIsTimed()
        {
            // an open solid next to the cube (classified by generalized winding numbers): its per-piece record is kept only with RecordOpenWinding
            foreach (bool record in new[] { false, true }) {
                var op = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Cup(0.25, 0.25, 1.25, 0.75, true) }, Ops.Difference) { AllowOpenMeshes = true, RecordOpenWinding = record };
                Assert.True(op.Compute(), op.Error);
                Assert.Equal(record, op.OpenWindingPerPiece != null);
                Assert.True(op.PhaseMilliseconds.ContainsKey("classification.open_us"));
                Assert.False(op.PhaseMilliseconds.ContainsKey("classification.sheets_us"));
            }
        }

        [Fact]
        public void TheSheetStep_IsTimed_OnlyWithASheet()
        {
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2));
            Assert.True(op.Status == Status.Success, op.Error);
            long us;
            Assert.True(op.PhaseMilliseconds.TryGetValue("classification.sheets_us", out us));
            Assert.True(us >= 0 && us < 60L * 1000 * 1000);
            Assert.False(op.PhaseMilliseconds.ContainsKey("_us"));
            // (the counters of the last sheet's pass: the pieces with a side in the cube, and their cells)
            Assert.True(op.Counts["sheet_pieces"] >= 12 + 2, "sheet_pieces " + op.Counts["sheet_pieces"]);
            var solids = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5), Ops.Difference);
            Assert.True(solids.Compute(), solids.Error);
            Assert.False(solids.PhaseMilliseconds.ContainsKey("classification.sheets_us"));
            Assert.False(solids.Counts.ContainsKey("sheet_pieces"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SheetInTheSharedFaceOfTwoShells_KeepsTheShellInFrontOfIt(bool normalPlusX)
        {
            // two cubes of one mesh that share the face x = 1 (the workpiece's winding is 1 on both sides of it) and a sheet in the plane of that face: the
            // piece of the face is no wall of the workpiece but a piece of the sheet, with the labels 0 and 1 on its sides, in either order against the
            // orientation of the piece (the piece is the first cube's triangle, the sheet's triangle runs with it or against it)
            DMesh3 two = TwoCubes(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
            DMesh3 sheet = Page(new Vector3d(1, -1, -1), new Vector3d(1, 2, -1), new Vector3d(1, 2, 2), new Vector3d(1, -1, 2), new Vector3d(normalPlusX ? 1 : -1, 0, 0));
            ExactMeshBoolean op = Cut(two, sheet, 1.0, "shared face, normal +x " + normalPlusX);
            // (behind the sheet is removed: with the normal +x the cube at x > 1 stays)
            Assert.Equal(normalPlusX ? 1.5 : 0.5, Centroid(op.Result).x, 12);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AFlapOfTheWorkpiece_TwoCoincidentTrianglesOfOppositeOrientation_IsNoWallAndNoShell(bool flapFirst)
        {
            // the cube and a flap: one triangle twice, with opposite orientations (a closed shell without volume, away from the cube and the sheet). The
            // workpiece's winding number is 0 on both sides of the flap (+1 and -1 across its two triangles), so it is no piece of the workpiece's
            // boundary: no wall, no shell of its own (a second shell would make the cube's cut depend on the nesting), no cell that the sheet misses
            var flap = new DMesh3();
            int a = flap.AppendVertex(new Vector3d(3, 0, 0.25)), b = flap.AppendVertex(new Vector3d(4, 0, 0.25)), c = flap.AppendVertex(new Vector3d(3, 1, 0.25));
            Assert.True(flap.AppendTriangle(a, b, c) >= 0);
            Assert.True(flap.AppendTriangle(a, c, b) >= 0);
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 workpiece = flapFirst ? Joined(flap, cube) : Joined(cube, flap);
            ExactMeshBoolean op = Cut(workpiece, Plate(0.5, -1, 2, -1, 2), 0.5, "cube and flap, flap first " + flapFirst);
            BoundingBox(op.Result, out Vector3d min, out Vector3d max);
            Assert.Equal(1.0, max.x);
            // (the cube's two cells and the one outside it, as without the flap)
            Assert.Equal(3, op.Counts["sheet_cells"]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AFlapOnAFaceOfTheWorkpiece_ThePieceIsAWallWithTheInteriorOnItsOneSide(bool inwardFirst)
        {
            // a flap (one triangle twice, with opposite orientations) in the top face of the cube, before the cube in the mesh: the piece there is a
            // triangle of the flap, with three triangles of the workpiece on it. With the flap's downward triangle first, the piece looks into the cube:
            // the winding number is 1 in front of it and 0 behind it (+1 - 2 across the other two). It is a wall, and only its front is in the cube
            var flap = new DMesh3();
            int a = flap.AppendVertex(new Vector3d(0.25, 0.25, 1)), b = flap.AppendVertex(new Vector3d(0.75, 0.25, 1)), c = flap.AppendVertex(new Vector3d(0.25, 0.75, 1));
            Assert.True((inwardFirst ? flap.AppendTriangle(a, c, b) : flap.AppendTriangle(a, b, c)) >= 0);
            Assert.True((inwardFirst ? flap.AppendTriangle(a, b, c) : flap.AppendTriangle(a, c, b)) >= 0);
            DMesh3 workpiece = Joined(flap, Box(0, 0, 0, 1, 1, 1));
            foreach (bool up in new[] { true, false }) {
                ExactMeshBoolean op = Run(workpiece, Plate(0.5, -1, 2, -1, 2, up));
                Assert.True(op.Status == Status.Success, "up " + up + ": " + op.Status + " " + op.SheetReason + " " + op.Error);
                Assert.Equal(0.5, Volume(op.Result), 12);
                Assert.True(ExactMeshBoolean.IsWindingClosed(op.Result));
                Assert.Equal(up ? 0.75 : 0.25, Centroid(op.Result).z, 12);
                Assert.Equal(3, op.Counts["sheet_cells"]);
            }
        }

        [Fact]
        public void SheetOnAFaceAgainstTheFacesOrientation_FoldedIntoTheWorkpiece_MarksTheCellInFrontOfIt()
        {
            // the folded sheet of SheetFoldedOntoAFace_WithAPieceInside_IsStillACut, reversed: the patch on the top face has its normals down (against the
            // face's triangle, which is the piece), so the cube under it is in front of the sheet, and the wall's normals point to -x: the left half is in
            // front of both and stays
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 patch = Page(new Vector3d(0, -1, 1), new Vector3d(0.5, -1, 1), new Vector3d(0.5, 2, 1), new Vector3d(0, 2, 1), new Vector3d(0, 0, 1));
            DMesh3 wall = Page(new Vector3d(0.5, -1, 1), new Vector3d(0.5, 2, 1), new Vector3d(0.5, 2, -1), new Vector3d(0.5, -1, -1), new Vector3d(1, 0, 0));
            ExactMeshBoolean kept = Cut(cube, Joined(patch, wall), 0.5, "folded");
            Assert.Equal(0.75, Centroid(kept.Result).x, 12);
            ExactMeshBoolean other = Cut(cube, Reversed(Joined(patch, wall)), 0.5, "folded, reversed");
            Assert.Equal(0.25, Centroid(other.Result).x, 12);
        }

        [Fact]
        public void TwoLayersOfOneSheet_OverEachOther_BothMark()
        {
            // one sheet of two plates in the plane z = 0.5, one over the other (no vertex in common: 3 and 4 units wide), with opposite normals: every piece
            // in the cube is a triangle of each layer, the cell above is in front of one layer and behind the other: refused. (Every layer of a piece marks,
            // not only the last one.)
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            foreach (bool firstUp in new[] { true, false }) {
                ExactMeshBoolean op = Run(cube, Joined(Plate(0.5, -1, 2, -1, 2, firstUp), Plate(0.5, -1.5, 2.5, -1.5, 2.5, firstUp == false)));
                AssertRefused(op, Refusal.SidesInconsistent, "two layers, opposite, first up " + firstUp);
                Assert.Equal(2, op.SheetConflictingCells);
                // (the front mark is the first layer's triangle or the second's, by the normals; the back mark the other's)
                Assert.InRange(op.SheetWitnessTriangle, 0, 3);
                Assert.InRange(op.SheetWitnessTriangle2, 0, 3);
                Assert.NotEqual(op.SheetWitnessTriangle < 2, op.SheetWitnessTriangle2 < 2);
            }
            // two layers with the same normals: one cut
            Cut(cube, Joined(Plate(0.5, -1, 2, -1, 2), Plate(0.5, -1.5, 2.5, -1.5, 2.5)), 0.5, "two layers, same normals");
            // three layers, two up and one down: the witness of the front mark is the first of the layers that mark the side (the lower triangle index)
            ExactMeshBoolean three = Run(cube, Joined(Joined(Plate(0.5, -1, 2, -1, 2), Plate(0.5, -1.5, 2.5, -1.5, 2.5)), Plate(0.5, -2, 3, -2, 3, false)));
            AssertRefused(three, Refusal.SidesInconsistent, "three layers");
            int lower = Math.Min(three.SheetWitnessTriangle, three.SheetWitnessTriangle2), upper = Math.Max(three.SheetWitnessTriangle, three.SheetWitnessTriangle2);
            Assert.InRange(lower, 0, 1);
            Assert.InRange(upper, 4, 5);
            // (the same reversed, two down and one up: there the first cell has the two layers' front marks, here their back marks)
            ExactMeshBoolean reversed = Run(cube, Joined(Joined(Plate(0.5, -1, 2, -1, 2, false), Plate(0.5, -1.5, 2.5, -1.5, 2.5, false)), Plate(0.5, -2, 3, -2, 3)));
            AssertRefused(reversed, Refusal.SidesInconsistent, "three layers, reversed");
            Assert.InRange(Math.Min(reversed.SheetWitnessTriangle, reversed.SheetWitnessTriangle2), 0, 1);
            Assert.InRange(Math.Max(reversed.SheetWitnessTriangle, reversed.SheetWitnessTriangle2), 4, 5);
            Assert.NotEqual(three.SheetWitnessTriangle < 2, reversed.SheetWitnessTriangle < 2);
        }

        // sheets for the box [0,2] x [0,1] x [0,1] with one refusal each (the sheets do not meet each other)
        static DMesh3 RefusedSheet(Refusal reason)
        {
            switch (reason) {
                case Refusal.RimInsideWorkpiece: return Plate(0.25, 0.75, 1.25, 0.25, 0.75);                         // a patch inside: free edges
                case Refusal.SidesInconsistent: return Extruded(new[] { 0.5, -1 }, new[] { 0.5, 2 }, new[] { 3.0, 2 }, new[] { 3.0, -2 }, new[] { 1.5, -2 }, new[] { 1.5, 1.5 });
                case Refusal.FloatingComponent: return Box(0.75, 0.25, 0.5, 1.25, 0.75, 0.75);                       // a closed sheet inside
                default: return Plate(5, -1, 3, -1, 2);                                                           // far away: the box is uncut
            }
        }

        [Theory]
        [InlineData(Refusal.SidesInconsistent, Refusal.RimInsideWorkpiece, 2)]
        [InlineData(Refusal.RimInsideWorkpiece, Refusal.SidesInconsistent, 1)]
        [InlineData(Refusal.UncutComponents, Refusal.RimInsideWorkpiece, 2)]
        [InlineData(Refusal.FloatingComponent, Refusal.RimInsideWorkpiece, 2)]
        [InlineData(Refusal.UncutComponents, Refusal.SidesInconsistent, 2)]
        [InlineData(Refusal.SidesInconsistent, Refusal.UncutComponents, 1)]
        [InlineData(Refusal.UncutComponents, Refusal.FloatingComponent, 2)]
        [InlineData(Refusal.FloatingComponent, Refusal.UncutComponents, 1)]
        [InlineData(Refusal.SidesInconsistent, Refusal.FloatingComponent, 1)]
        [InlineData(Refusal.FloatingComponent, Refusal.SidesInconsistent, 1)]
        public void TwoRefusedSheets_TheReasonNamed_IsTheFreeEdge_ThenTheMarks_ThenTheUncutPart_AndTheFirstSheetAmongEquals(Refusal first, Refusal second, int expectedMesh)
        {
            DMesh3 box = Box(0, 0, 0, 2, 1, 1);
            // (each sheet alone is refused for its reason)
            AssertRefused(Run(box, RefusedSheet(first)), first, "first alone");
            AssertRefused(Run(box, RefusedSheet(second)), second, "second alone");
            ExactMeshBoolean op = RunAll(new[] { box, RefusedSheet(first), RefusedSheet(second) }, SolidRole, SheetRole, SheetRole);
            AssertRefused(op, expectedMesh == 1 ? first : second, first + " then " + second);
            Assert.Equal(expectedMesh, op.SheetWitnessMesh);
        }

        [Theory]
        [InlineData(0, true, true)]
        [InlineData(1, true, true)]
        [InlineData(2, true, true)]
        [InlineData(3, true, true)]
        [InlineData(0, false, true)]
        [InlineData(1, false, true)]
        [InlineData(2, false, true)]
        [InlineData(3, false, true)]
        [InlineData(0, true, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(3, true, false)]
        [InlineData(0, false, false)]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(3, false, false)]
        public void RimInsideTheWorkpiece_TheWitnessIsTheStartOfTheFreeEdge_AndATriangleOfTheSheet(int quarterTurns, bool up, bool lowHalf)
        {
            // the half plate z = 0.5 whose free edge runs through the middle of the cube, turned by quarter turns about the cube's axis: the free edge inside
            // the cube is one segment between two faces of the cube; the witness is the corner the edge starts at, in the direction of the sheet's triangle
            // (counterclockwise seen from where the normal points to: the plate is on the left of its rim)
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            Func<Vector3d, Vector3d> turn = p => {
                double x = p.x - 0.5, y = p.y - 0.5;
                for (int k = 0; k < quarterTurns; ++k) { double t = x; x = -y; y = t; }
                return new Vector3d(x + 0.5, y + 0.5, p.z);
            };
            // (lowHalf: the plate at x < 0.5, its rim is the second edge of its first triangle; else at x > 0.5, the rim the third edge of its second)
            DMesh3 plate = lowHalf ? Plate(0.5, -1, 0.5, -1, 2, up) : Plate(0.5, 0.5, 2, -1, 2, up);
            foreach (int v in plate.VertexIndices())
                plate.SetVertex(v, turn(plate.GetVertex(v)));
            ExactMeshBoolean op = Run(cube, plate);
            AssertRefused(op, Refusal.RimInsideWorkpiece, "half plate");
            // (unturned, normals up: the plate is at x < 0.5, its rim x = 0.5 runs towards +y, and enters the cube at y = 0)
            Vector3d start = turn(new Vector3d(0.5, up == lowHalf ? 0 : 1, 0.5));
            Assert.Equal(start, op.SheetWitnessPoint);
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.InRange(op.SheetWitnessTriangle, 0, 1);
            Assert.Contains("free edge of sheet 1", op.Error);
        }

        [Theory]
        [InlineData(0, 1, 2)]
        [InlineData(1, 2, 0)]
        [InlineData(2, 0, 1)]
        [InlineData(0, 2, 1)]
        [InlineData(2, 1, 0)]
        [InlineData(1, 0, 2)]
        public void RimInsideTheWorkpiece_OneTriangleInside_TheWitnessIsTheStartOfItsFirstFreeEdge_AsAnyOfItsThreeEdges(int i, int j, int k)
        {
            // one triangle inside the cube, its corners in every order: all three edges are free, the one named is the edge of the sheet's first two
            // vertices (the first in the order of the arrangement's points), which is the first, second or third edge of the triangle by the order of
            // its corners: the witness is the corner that edge starts at
            Vector3d[] p = { new Vector3d(0.25, 0.25, 0.5), new Vector3d(0.75, 0.25, 0.5), new Vector3d(0.25, 0.75, 0.25) };
            var sheet = new DMesh3();
            foreach (Vector3d q in p)
                sheet.AppendVertex(q);
            Assert.True(sheet.AppendTriangle(i, j, k) >= 0);
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), sheet);
            AssertRefused(op, Refusal.RimInsideWorkpiece, "one triangle inside");
            // (the edge between the vertices 0 and 1 runs from 0 to 1 in the even orders of the corners)
            bool even = (i + 1) % 3 == j;
            Assert.Equal(even ? p[0] : p[1], op.SheetWitnessPoint);
            Assert.Equal(0, op.SheetWitnessTriangle);
        }

        [Fact]
        public void Witnesses_OfAPieceThatIsNotTheSheetsOrTheWorkpiecesTriangle_NameNoTriangle()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // a free edge of the sheet on a piece that is the triangle of an open solid under it (the same patch as mesh 1, a solid, and as mesh 2, the sheet):
            // the sheet is named, a triangle is not (the piece's triangle is not the sheet's)
            DMesh3 patch = Plate(0.5, 0.25, 0.75, 0.25, 0.75);
            ExactMeshBoolean rim = RunAllOpen(new[] { cube, patch, new DMesh3(patch) }, SolidRole, SolidRole, SheetRole);
            AssertRefused(rim, Refusal.RimInsideWorkpiece, "the sheet over an open solid");
            Assert.Equal(2, rim.SheetWitnessMesh);
            Assert.Equal(-1, rim.SheetWitnessTriangle);
            Assert.Equal(0.5, rim.SheetWitnessPoint.z);
            // an uncut cell: the witness is a triangle of the workpiece (the ball's: 12 triangles) ...
            ExactMeshBoolean uncut = Run(Box(-0.125, -0.125, 0.25, 0.125, 0.125, 0.5), Plate(0, -1, 1, -1, 1));
            AssertRefused(uncut, Refusal.UncutComponents, "uncut");
            Assert.Equal(1, uncut.SheetWitnessMesh);
            Assert.InRange(uncut.SheetWitnessTriangle, 0, 11);
            Assert.Contains("not touched by sheet 1", uncut.Error);
            // ... or none, where the uncut cell is the inside of another tool that floats in a cell (its pieces are that tool's triangles)
            ExactMeshBoolean floating = RunAll(new[] { cube, Plate(0.5, -1, 2, -1, 2), Box(0.25, 0.25, 0.1, 0.75, 0.75, 0.3) }, SolidRole, SheetRole, SolidRole);
            AssertRefused(floating, Refusal.UncutComponents, "floating tool");
            Assert.Equal(1, floating.SheetWitnessMesh);
            Assert.Equal(-1, floating.SheetWitnessTriangle);
            Assert.Equal(1, floating.SheetUncutCells);
        }

        [Fact]
        public void FloatingComponent_TheWitnessIsATriangleOfTheFloatingPart_ByItsFrontOrItsBackMark()
        {
            // the plate (triangles 0 and 1 of the sheet) and a bubble (triangles 2 to 13) that floats in a cell: the witness is a triangle of the bubble,
            // whether the bubble's inside is behind its pieces (a back mark only) or in front of them (a front mark only)
            foreach (bool inverted in new[] { false, true }) {
                DMesh3 bubble = Box(0.25, 0.25, 0.1, 0.75, 0.75, 0.3);
                ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), Joined(Plate(0.5, -1, 2, -1, 2), inverted ? Reversed(bubble) : bubble));
                AssertRefused(op, Refusal.FloatingComponent, "bubble, inverted " + inverted);
                Assert.InRange(op.SheetWitnessTriangle, 2, 13);
                // (two local cells without a wall of the workpiece: the bubble's inside and the cell around it, which no edge joins to the plate's)
                Assert.Equal(2, op.SheetConflictingCells);
                Assert.Contains("2 part(s) of sheet 1 float", op.Error);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FloatingComponent_ABubbleWithAnEdgeOnAFaceOfTheWorkpiece_OnlyItsInsideFloats(bool inverted)
        {
            // the plate (triangles 0 and 1) and a prism (2 to 9) above it whose one edge lies in the face x = 0 of the cube: around that edge the cell
            // outside the prism is joined to the cube's wall, so only the prism's inside floats. It has one mark, the back mark of the prism's triangles
            // (their normals point away from it) or, inverted, their front mark: the witness is a triangle of the prism in both cases. (The inverted
            // prism is under the plate: the cell around it is behind its triangles and behind the plate, which agrees)
            var s = new Soup();
            double z = inverted ? 0.25 : 0.75;
            Vector3d a0 = new Vector3d(0, 0.25, z), b0 = new Vector3d(0.25, 0.25, z + 0.125), c0 = new Vector3d(0.25, 0.25, z - 0.125);
            Vector3d a1 = new Vector3d(0, 0.75, z), b1 = new Vector3d(0.25, 0.75, z + 0.125), c1 = new Vector3d(0.25, 0.75, z - 0.125);
            s.Tri(a0, c0, b0);                       // y = 0.25 (normal -y)
            s.Tri(a1, b1, c1);                       // y = 0.75
            s.Quad(a0, b0, b1, a1);                  // the upper side
            s.Quad(a0, a1, c1, c0);                  // the lower side
            s.Quad(b0, c0, c1, b1);                  // x = 0.25
            Assert.True(Volume(s.Mesh) > 0, "the prism's normals point outwards");
            ExactMeshBoolean op = Run(Box(0, 0, 0, 1, 1, 1), Joined(Plate(0.5, -1, 2, -1, 2), inverted ? Reversed(s.Mesh) : s.Mesh));
            AssertRefused(op, Refusal.FloatingComponent, "prism on a face, inverted " + inverted);
            Assert.Equal(1, op.SheetWitnessMesh);
            Assert.InRange(op.SheetWitnessTriangle, 2, 9);
            Assert.Equal(1, op.SheetConflictingCells);
            Assert.Contains("1 part(s) of sheet 1 float", op.Error);
        }

        [Fact]
        public void OnlyTouching_TheWitnessIsTheFirstTouchingTriangleOfTheSheet_AndTheCellsOfTheWorkpieceAreCounted()
        {
            // two patches on the top face of the cube (triangles 0, 1 and 2, 3 of the sheet): the witness is a triangle of the first, the uncut cell is the cube
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            ExactMeshBoolean op = Run(cube, Joined(Plate(1, 0.125, 0.25, 0.125, 0.25), Plate(1, 0.5, 0.75, 0.5, 0.75)));
            AssertRefused(op, Refusal.UncutComponents, "two patches on a face");
            Assert.Contains("only touches", op.Error);
            Assert.InRange(op.SheetWitnessTriangle, 0, 3);
            Assert.Equal(1, op.SheetUncutCells);
            // a plate under the whole bottom face, whose first triangle (y > x) is the workpiece's first piece: the witness is the sheet's triangle on that
            // piece (the plate's second, y > x as well), not the one on the last touching piece
            foreach (bool up in new[] { true, false }) {
                ExactMeshBoolean bottom = Run(cube, Plate(0, -1, 2, -1, 2, up));
                AssertRefused(bottom, Refusal.UncutComponents, "plate on the bottom face");
                Assert.Equal(1, bottom.SheetWitnessTriangle);
            }
        }
        // ---- an open tool without a role: OpenInputsWithoutEffect (#1211) ---------------------------------------------------------

        static ExactMeshBoolean Plain(Ops op, params DMesh3[] meshes)
        {
            var b = new ExactMeshBoolean(meshes, op) { AllowOpenMeshes = true };
            Assert.True(b.Compute(), b.Error);
            return b;
        }

        static readonly int[] None = new int[0];

        [Fact]
        public void OpenWithoutEffect_ASheetWithoutARole_CutsNothing_AndIsListed()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            Assert.Empty(new ExactMeshBoolean(cube, Plate(0.5, -1, 2, -1, 2), Ops.Difference) { AllowOpenMeshes = true }.OpenInputsWithoutEffect);      // before Compute()
            foreach (bool up in new[] { true, false }) {
                ExactMeshBoolean b = Plain(Ops.Difference, cube, Plate(0.5, -1, 2, -1, 2, up, 2));
                Assert.Equal(1.0, Volume(b.Result), 12);        // the silent case: Success, the cube unchanged
                Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
                Assert.Equal(new[] { 1 }, b.OpenInputsWithoutEffect);
                Assert.False(b.ExactWinding[1]);
            }
            // a sheet that ends inside, a disc wholly inside, and a sheet on the top face (the cube behind it or in front of it)
            Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, Plate(0.5, -1, 0.5, -1, 2)).OpenInputsWithoutEffect);
            Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, Plate(0.5, 0.25, 0.75, 0.25, 0.75)).OpenInputsWithoutEffect);
            Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, Plate(1, -1, 2, -1, 2, true)).OpenInputsWithoutEffect);
            Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, Plate(1, -1, 2, -1, 2, false)).OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_NotListed_ClosedTool_SheetRole_ToolThatCuts_ToolOutside()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // a closed tool
            Assert.Empty(Plain(Ops.Difference, cube, Box(0.25, 0.25, 0.5, 0.75, 0.75, 2)).OpenInputsWithoutEffect);
            // the same sheet with the role: it cuts
            ExactMeshBoolean role = Run(cube, Plate(0.5, -1, 2, -1, 2));
            Assert.True(role.Status == Status.Success, role.Error);
            Assert.Equal(0.5, Volume(role.Result), 12);
            Assert.Empty(role.OpenInputsWithoutEffect);
            // an open tool whose winding number is above 1/2 in the cube: a deep cup with its opening above (it removes its inside)
            ExactMeshBoolean cup = Plain(Ops.Difference, cube, Cup(0.25, 0.25, 3, 0.25, true));
            Assert.Equal(1 - 0.5 * 0.5 * 0.75, Volume(cup.Result), 12);
            Assert.False(cup.ExactWinding[1]);
            Assert.Empty(cup.OpenInputsWithoutEffect);
            // a sheet that does not reach the cube: nothing depends on it
            Assert.Empty(Plain(Ops.Difference, cube, Plate(2, -1, 2, -1, 2)).OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_UnionAndIntersection()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // Union: a sheet matters where it is outside the others. Floating outside, or crossing: listed; wholly inside: not
            Assert.Equal(new[] { 1 }, Plain(Ops.Union, cube, Plate(2, -1, 2, -1, 2)).OpenInputsWithoutEffect);
            Assert.Equal(new[] { 1 }, Plain(Ops.Union, cube, Plate(0.5, -1, 2, -1, 2)).OpenInputsWithoutEffect);
            Assert.Empty(Plain(Ops.Union, cube, Plate(0.5, 0.25, 0.75, 0.25, 0.75)).OpenInputsWithoutEffect);
            // Intersection: a sheet matters where it is inside the others; the result is empty, as for an input that encloses nothing
            ExactMeshBoolean cross = Plain(Ops.Intersection, cube, Plate(0.5, -1, 2, -1, 2));
            Assert.Equal(0, cross.Result.TriangleCount);
            Assert.Equal(new[] { 1 }, cross.OpenInputsWithoutEffect);
            Assert.Empty(Plain(Ops.Intersection, cube, Plate(2, -1, 2, -1, 2)).OpenInputsWithoutEffect);
            // the deep cup has an effect in both
            Assert.Empty(Plain(Ops.Union, cube, Cup(0.25, 0.25, 3, 0.25, true)).OpenInputsWithoutEffect);
            Assert.Empty(Plain(Ops.Intersection, cube, Cup(0.25, 0.25, 3, 0.25, true)).OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_SeveralTools_AndAnOpenFirstMesh()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), tool = Box(0.25, 0.25, 0.25, 0.75, 0.75, 2);
            // a sheet hidden inside a closed tool: a closed operand would vanish there as well
            Assert.Empty(Plain(Ops.Difference, cube, tool, Plate(0.5, 0.375, 0.625, 0.375, 0.625)).OpenInputsWithoutEffect);
            // a sheet that crosses the cube next to that tool
            ExactMeshBoolean b = Plain(Ops.Difference, cube, tool, Plate(0.125, -1, 2, -1, 2));
            Assert.Equal(new[] { 2 }, b.OpenInputsWithoutEffect);
            Assert.Equal(1 - 0.5 * 0.5 * 0.75, Volume(b.Result), 12);
            // two sheets: both, ascending; the closed tool between them is not
            Assert.Equal(new[] { 1, 3 }, Plain(Ops.Difference, cube, Plate(0.875, -1, 2, -1, 2), tool, Plate(0.125, -1, 2, -1, 2)).OpenInputsWithoutEffect);
            // the first mesh a sheet: it encloses nothing, the Difference is empty, and it is listed (index 0)
            ExactMeshBoolean first = Plain(Ops.Difference, Plate(0.5, -1, 2, -1, 2), cube);
            Assert.Equal(0, first.Result.TriangleCount);
            Assert.Equal(new[] { 0 }, first.OpenInputsWithoutEffect);
            // an open first mesh that does enclose (the deep cup) minus a box: not listed
            Assert.Empty(Plain(Ops.Difference, Cup(0.25, 0.25, 3, 0.25, true), Box(0, 0, 0.5, 1, 1, 0.75)).OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_OnlyOpenInputsWithoutARole_AreCandidates()
        {
            // the same tool twice: each of the two could be left out without a change, and at its pieces the result depends on it
            // where the twin is taken away too. That is the definition's case, but only an open input without a role is asked
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), tool = Box(0.25, 0.25, 0.5, 0.75, 0.75, 2);
            ExactMeshBoolean closedTwins = Plain(Ops.Difference, cube, tool, new DMesh3(tool));
            Assert.Equal(1 - 0.5 * 0.5 * 0.5, Volume(closedTwins.Result), 12);
            Assert.Empty(closedTwins.OpenInputsWithoutEffect);
            // a sheet with its role, twice
            DMesh3 plate = Plate(0.5, -1, 2, -1, 2);
            ExactMeshBoolean sheetTwins = RunAll(new[] { cube, plate, new DMesh3(plate) }, ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet, ExactMeshBoolean.InputRole.Sheet);
            Assert.True(sheetTwins.Status == Status.Success, sheetTwins.Status + " " + sheetTwins.SheetReason + " " + sheetTwins.Error);
            Assert.Equal(0.5, Volume(sheetTwins.Result), 12);
            Assert.Empty(sheetTwins.OpenInputsWithoutEffect);
            // open twins that do cut (the deep cup twice): either could be left out, so both are listed
            DMesh3 cup = Cup(0.25, 0.25, 3, 0.25, true);
            ExactMeshBoolean cupTwins = Plain(Ops.Difference, cube, cup, new DMesh3(cup));
            Assert.Equal(1 - 0.5 * 0.5 * 0.75, Volume(cupTwins.Result), 12);
            Assert.Equal(new[] { 1, 2 }, cupTwins.OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_AnEffectSeenOnlyAtAnotherMeshsPieces_OnTheFrontSide()
        {
            // a tool made open by a stray sheet: its closed part (a box) swallows a cavity of the second mesh x. None of the
            // tool's own pieces is kept; the effect shows at the cavity's faces, whose front side is inside the tool
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 x = Box(-0.5, -0.5, -0.5, 1.5, 1.5, 1.5);
            MeshEditor.Append(x, Reversed(Box(0.125, 0.125, 0.125, 0.375, 0.375, 0.375)));
            MeshEditor.Append(x, Reversed(Box(0.625, 0.625, 0.625, 0.875, 0.875, 0.875)));
            DMesh3 m = Plate(0.25, 0.1875, 0.3125, 0.1875, 0.3125);
            MeshEditor.Append(m, Box(0.5625, 0.5625, 0.5625, 0.9375, 0.9375, 0.9375));
            ExactMeshBoolean without = Plain(Ops.Difference, cube, x);
            Assert.Equal(2 * 0.015625, Volume(without.Result), 12);     // the two cavities
            ExactMeshBoolean b = Plain(Ops.Difference, cube, x, m);
            Assert.False(b.ExactWinding[2]);
            Assert.Equal(0.015625, Volume(b.Result), 12);               // one of them is gone: an effect
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
            Assert.Empty(b.OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_CoplanarStacks()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), tool = Box(0.25, 0.25, 0.25, 0.75, 0.75, 2);
            // an open tool whose walls and bottom lie in the cube's faces removes all of it: pieces are lost that no piece of
            // the tool replaces (an effect through the pieces that are no longer kept)
            ExactMeshBoolean all = Plain(Ops.Difference, cube, Cup(0, 0.5, 3, 0.5, true));
            Assert.Equal(0, all.Result.TriangleCount);
            Assert.Empty(all.OpenInputsWithoutEffect);
            // a plate in the bottom face of a closed tool, either way up, as the third or the second input, and wider than the face
            foreach (bool up in new[] { true, false }) {
                ExactMeshBoolean after = Plain(Ops.Difference, cube, tool, Plate(0.25, 0.25, 0.75, 0.25, 0.75, up));
                Assert.Equal(new[] { 2 }, after.OpenInputsWithoutEffect);
                Assert.Equal(1 - 0.5 * 0.5 * 0.75, Volume(after.Result), 12);
                Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, Plate(0.25, 0.25, 0.75, 0.25, 0.75, up), tool).OpenInputsWithoutEffect);
                Assert.Equal(new[] { 2 }, Plain(Ops.Difference, cube, tool, Plate(0.25, 0.125, 0.875, 0.125, 0.875, up)).OpenInputsWithoutEffect);
            }
        }

        [Fact]
        public void OpenWithoutEffect_ThreeInputs_Intersection_AndNextToASheetWithItsRole()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), column = Box(0.25, 0.25, -1, 0.75, 0.75, 2);
            // Intersection of three: the plate matters inside both others; the result is empty (without the plate it is 0.25)
            ExactMeshBoolean i3 = Plain(Ops.Intersection, cube, column, Plate(0.5, -1, 2, -1, 2));
            Assert.Equal(0, i3.Result.TriangleCount);
            Assert.Equal(new[] { 2 }, i3.OpenInputsWithoutEffect);
            // a plate outside the column: nothing depends on it
            Assert.Empty(Plain(Ops.Intersection, cube, column, Plate(0.5, 0.8, 0.95, 0.8, 0.95)).OpenInputsWithoutEffect);
            // the deep cup encloses: an effect
            ExactMeshBoolean cup = Plain(Ops.Intersection, cube, column, Cup(0.25, 0.25, 3, 0.25, true));
            Assert.Equal(0.5 * 0.5 * 0.75, Volume(cup.Result), 12);
            Assert.Empty(cup.OpenInputsWithoutEffect);
            // a sheet with its role (it removes z < 0.5) and a plate without one: listed in the part that stays, not in the removed part
            var solid = ExactMeshBoolean.InputRole.Solid;
            ExactMeshBoolean kept = RunAllOpen(new[] { cube, Plate(0.5, -1, 2, -1, 2), Plate(0.75, -1, 2, -1, 2) }, solid, ExactMeshBoolean.InputRole.Sheet, solid);
            Assert.True(kept.Status == Status.Success, kept.Error);
            Assert.Equal(0.5, Volume(kept.Result), 12);
            Assert.Equal(new[] { 2 }, kept.OpenInputsWithoutEffect);
            ExactMeshBoolean removed = RunAllOpen(new[] { cube, Plate(0.5, -1, 2, -1, 2), Plate(0.25, -1, 2, -1, 2) }, solid, ExactMeshBoolean.InputRole.Sheet, solid);
            Assert.True(removed.Status == Status.Success, removed.Error);
            Assert.Equal(0.5, Volume(removed.Result), 12);
            Assert.Empty(removed.OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_IsEmptyAfterARunThatFailsAfterTheClassification()
        {
            // a rod to 1e300 and a floating plate: the union lists the plate; with StrictRounding the run fails in the rounding
            // (the scale is not finite), after the classification, and the list is empty with the failed status
            DMesh3 rod = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1e300, 1, 1)) }.Generate().MakeDMesh();
            DMesh3 plate = Plate(5, -1, 2, -1, 2);
            var b = new ExactMeshBoolean(rod, plate, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(new[] { 1 }, b.OpenInputsWithoutEffect);
            b.StrictRounding = true;
            Assert.False(b.Compute());
            Assert.Equal(Status.RoundingNotClean, b.Status);
            Assert.Empty(b.OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_NotEvaluatedWithAWindingPredicate()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), plate = Plate(0.5, -1, 2, -1, 2);
            int calls = 0;
            var b = new ExactMeshBoolean(new[] { cube, plate }, w => { ++calls; return w[0] > 0 && w[1] <= 0; }) { AllowOpenMeshes = true };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1.0, Volume(b.Result), 12);
            Assert.Empty(b.OpenInputsWithoutEffect);
            // the predicate is called for the two sides of each piece only (this input has no coplanar pieces)
            Assert.Equal(2 * b.Counts["pieces"], calls);
        }

        [Fact]
        public void OpenWithoutEffect_Reuse_ComputeTwice_AndAfterAChangedTool()
        {
            var b = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Plate(0.5, -1, 2, -1, 2), Ops.Difference) { AllowOpenMeshes = true };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(new[] { 1 }, b.OpenInputsWithoutEffect);
            string first = Fingerprint(b.Result);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(new[] { 1 }, b.OpenInputsWithoutEffect);       // not appended to
            Assert.Equal(first, Fingerprint(b.Result));
            b.MeshB = Box(-1, -1, 0.5, 2, 2, 3);
            Assert.True(b.Compute(), b.Error);
            Assert.Empty(b.OpenInputsWithoutEffect);
            Assert.Equal(0.5, Volume(b.Result), 12);
            // a failed run clears it too
            b.MeshB = Plate(0.5, -1, 2, -1, 2);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(new[] { 1 }, b.OpenInputsWithoutEffect);
            b.AllowOpenMeshes = false;
            Assert.False(b.Compute());
            Assert.Equal(Status.OpenInput, b.Status);
            Assert.Empty(b.OpenInputsWithoutEffect);
        }

        [Fact]
        public void OpenWithoutEffect_Invariance_AndDeterminism()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), plate = Plate(0.5, -1, 2, -1, 2, true, 3), cup = Cup(0.25, 0.25, 3, 0.25, true);
            ExactMeshBoolean p0 = Plain(Ops.Difference, cube, plate), c0 = Plain(Ops.Difference, cube, cup);
            foreach (Similarity s in Similarity.All()) {
                ExactMeshBoolean p = Plain(Ops.Difference, Invariance.Transformed(cube, s), Invariance.Transformed(plate, s));
                // the list says exactly that the result is the one without the sheet: listed where the cube comes back, not listed
                // where the sheet changed the result. (In the frame far from the origin the rotated sheet is not planar in
                // doubles, its generalized winding number crosses 1/2 at some pieces, and the result is a wrong, open mesh:
                // an effect, so the sheet is not listed there. Every other frame gives the cube.)
                double volume = Volume(p.Result) / (s.Scale * s.Scale * s.Scale);
                bool unchanged = MeshRobustRepair.NotCleanReason(p.Result) == "" && Math.Abs(volume - 1) < 1e-9;
                Assert.True((p.OpenInputsWithoutEffect.Count == 1) == unchanged, s.Name + ": " + volume);
                if (s != Similarity.RotatedUtmMillimetre)
                    Assert.Equal(new[] { 1 }, p.OpenInputsWithoutEffect);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(p0.Result, s), p.Result));
                ExactMeshBoolean c = Plain(Ops.Difference, Invariance.Transformed(cube, s), Invariance.Transformed(cup, s));
                Assert.True(c.OpenInputsWithoutEffect.Count == 0, s.Name);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(c0.Result, s), c.Result));
            }
            gParallel.SerialOnThisThread = true;
            try {
                ExactMeshBoolean serial = Plain(Ops.Difference, cube, plate);
                Assert.Equal(new[] { 1 }, serial.OpenInputsWithoutEffect);
                Assert.Equal(Fingerprint(p0.Result), Fingerprint(serial.Result));
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 3; ++run)
                Assert.Equal(new[] { 1 }, Plain(Ops.Difference, cube, plate).OpenInputsWithoutEffect);
        }
        [Fact]
        public void OpenInputError_NamesTheInputsAndTheWaysOut()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1), plate = Plate(0.5, -1, 2, -1, 2);
            var one = new ExactMeshBoolean(cube, plate, Ops.Difference) { AllowOpenMeshes = false };
            Assert.False(one.Compute());
            Assert.Equal(Status.OpenInput, one.Status);
            Assert.StartsWith("input mesh 1 is not closed (", one.Error);
            Assert.Contains("role Sheet (Roles)", one.Error);
            Assert.Contains("AllowOpenMeshes", one.Error);
            Assert.Contains("OpenInputsWithoutEffect", one.Error);
            // several, by index; a closed mesh and a sheet with its role are not named
            var more = new ExactMeshBoolean(new[] { plate, cube, Plate(0.25, -1, 2, -1, 2), Plate(0.75, -1, 2, -1, 2) }, Ops.Union) { AllowOpenMeshes = false };
            Assert.False(more.Compute());
            Assert.StartsWith("input meshes 0, 2, 3 are not closed (", more.Error);
            var role = new ExactMeshBoolean(new[] { cube, Plate(0.25, -1, 2, -1, 2), Plate(0.75, -1, 2, -1, 2) }, Ops.Difference) {
                AllowOpenMeshes = false, Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            Assert.False(role.Compute());
            Assert.Equal(Status.OpenInput, role.Status);
            Assert.StartsWith("input mesh 2 is not closed (", role.Error);
        }
    }
}
