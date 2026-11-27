using System;
using System.Collections.Generic;
using System.Linq;
using BoolCmp;
using g3;
using Xunit;
using ChainResultT = BoolCmp.ContactTransforms.ChainResult;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact placements of the thingi-contact benchmark class (benchmarks/BooleanComparison/ContactTransforms.cs):
    /// procedural meshes, no data. The point of the class is that nothing rounds, so every test compares coordinates bit for bit.
    /// </summary>
    public class ThingiContactTransformTests
    {
        // a closed box with sides that are multiples of 1/64, so that all placements are exactly representable
        static DMesh3 Box(double cx, double cy, double cz, double sx, double sy, double sz)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(new Vector3d(cx, cy, cz), new Vector3d(sx / 2, sy / 2, sz / 2)) };
            return g.Generate().MakeDMesh();
        }

        static double Get(Vector3d v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        static IEnumerable<Vector3d> Verts(DMesh3 m) => m.VertexIndices().Select(m.GetVertex);
        // the neighbours of a finite double by its bits (Math.BitIncrement does not exist on net48)
        static double Up(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x >= 0 ? 1 : -1));
        static double Down(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - (x > 0 ? 1 : -1));
        static double Max(DMesh3 m, int axis) => Verts(m).Max(v => Get(v, axis));
        static double Min(DMesh3 m, int axis) => Verts(m).Min(v => Get(v, axis));

        static bool BitEqual(DMesh3 a, DMesh3 b)
        {
            var va = a.VertexIndices().ToArray(); var vb = b.VertexIndices().ToArray();
            if (va.Length != vb.Length || a.TriangleCount != b.TriangleCount) return false;
            for (int i = 0; i < va.Length; ++i) {
                Vector3d p = a.GetVertex(va[i]), q = b.GetVertex(vb[i]);
                if (BitConverter.DoubleToInt64Bits(p.x) != BitConverter.DoubleToInt64Bits(q.x) || BitConverter.DoubleToInt64Bits(p.y) != BitConverter.DoubleToInt64Bits(q.y)
                    || BitConverter.DoubleToInt64Bits(p.z) != BitConverter.DoubleToInt64Bits(q.z)) return false;
            }
            return a.Triangles().SequenceEqual(b.Triangles());
        }

        // A: 0.75 x 0.5 x 0.5 box, B: 0.5 x 0.75 x 0.625 box, both already normalized (longest side 0.75 in [0.5, 1), centred at the origin): Build leaves them unchanged
        static DMesh3 A() => Box(0, 0, 0, 0.75, 0.5, 0.5);
        static DMesh3 B() => Box(0, 0, 0, 0.5, 0.75, 0.625);

        [Theory]
        [InlineData(0, 1)] [InlineData(0, -1)] [InlineData(1, 1)] [InlineData(1, -1)] [InlineData(2, 1)] [InlineData(2, -1)]
        public void FaceContact_PutsTheFacesIntoOnePlane_BitEqual(int axis, int sign)
        {
            ContactResult r = ContactTransforms.Build(A(), B(), ContactVariant.FaceContact, axis, sign, 0);
            Assert.Null(r.Error);
            Assert.Equal("face", r.Info["kind"]);
            Assert.Equal(axis, r.Info["contact_axis"]);
            Assert.Equal(sign, r.Info["contact_sign"]);
            // the contact plane: A's extreme face and B's opposite extreme face have bit-equal coordinates
            double pa = sign > 0 ? Max(r.A, axis) : Min(r.A, axis), pb = sign > 0 ? Min(r.B, axis) : Max(r.B, axis);
            Assert.Equal(BitConverter.DoubleToInt64Bits(pa), BitConverter.DoubleToInt64Bits(pb));
            Assert.Equal(pa, (double)r.Info["plane"]);
            // touching only: B lies on the other side of the plane
            if (sign > 0) Assert.True(Min(r.B, axis) >= Max(r.A, axis)); else Assert.True(Max(r.B, axis) <= Min(r.A, axis));
            // the placement is a translation by the recorded shift, and every sum is exact: the original plus the shift is the placed coordinate
            double[] t = (double[])r.Info["shift"];
            DMesh3 orig = B();
            var o = orig.VertexIndices().ToArray(); var p = r.B.VertexIndices().ToArray();
            for (int i = 0; i < o.Length; ++i)
                for (int k = 0; k < 3; ++k) {
                    Assert.True(ContactTransforms.ExactSum(Get(orig.GetVertex(o[i]), k), t[k], out double s));
                    Assert.Equal(s, Get(r.B.GetVertex(p[i]), k));
                }
            Assert.True(ContactTransforms.Volume(r.A) > 0 && ContactTransforms.Volume(r.B) > 0);
            Assert.Equal(1, Math.Sign(r.B.TriangleCount - 11));            // 12 triangles, unchanged
        }

        [Fact]
        public void FaceContact_WithoutPlanarFaces_IsAnExactBoundingBoxContact()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 0.375, EdgeVertices = 8 }.Generate().MakeDMesh();
            foreach (int vid in sphere.VertexIndices().ToArray()) {            // single precision coordinates, as the Thingi10K STL models have
                Vector3d v = sphere.GetVertex(vid);
                sphere.SetVertex(vid, new Vector3d((float)v.x, (float)v.y, (float)v.z));
            }
            for (int axis = 0; axis < 3; ++axis)
                foreach (int dir in new[] { 1, -1 }) {                    // one vertex alone reaches each of the six extremes: no planar face anywhere
                    int apex = sphere.VertexIndices().OrderByDescending(v => dir * Get(sphere.GetVertex(v), axis)).First();
                    Vector3d p = sphere.GetVertex(apex);
                    sphere.SetVertex(apex, axis == 0 ? new Vector3d(p.x + dir * 0.0625, p.y, p.z) : axis == 1 ? new Vector3d(p.x, p.y + dir * 0.0625, p.z) : new Vector3d(p.x, p.y, p.z + dir * 0.0625));
                }
            ContactResult r = ContactTransforms.Build(sphere, new DMesh3(sphere), ContactVariant.FaceContact, 2, 1, 0);
            Assert.Null(r.Error);
            Assert.Equal("bbox", r.Info["kind"]);
            double pa = Max(r.A, 2), pb = Min(r.B, 2);
            Assert.Equal(BitConverter.DoubleToInt64Bits(pa), BitConverter.DoubleToInt64Bits(pb));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void PartialOverlap_EmbedsByAPowerOfTwoFraction(int depth)
        {
            ContactResult r = ContactTransforms.Build(A(), B(), ContactVariant.PartialOverlap, 2, 1, depth);
            Assert.Null(r.Error);
            Assert.Equal(depth, r.Info["depth"]);
            double h = Max(B(), 2) - Min(B(), 2);                          // 0.625
            double top = Max(r.A, 2);
            Assert.Equal(top - Math.Pow(2, -depth) * h, Min(r.B, 2));      // B's near face is inside A by 2^-depth of its extent
            Assert.Equal(depth == 0 ? "face" : "embedded", r.Info["kind"]);
            if (depth == 0) {
                // flush: B's far face lies in A's top plane, both faces are planar and overlap
                Assert.Equal(top, Max(r.B, 2));
                Assert.Equal("face", r.Info["kind"]);
            }
            // overlapping volumes: the boxes interpenetrate in the axis
            Assert.True(Min(r.B, 2) < top);
        }

        [Theory]
        [InlineData(0, 1)] [InlineData(1, -1)] [InlineData(2, 1)]
        public void UlpShift_MovesEveryVertexByTheSameExactStep(int axis, int sign)
        {
            ContactResult r = ContactTransforms.Build(A(), B(), ContactVariant.UlpShift, axis, sign, 0);
            Assert.Null(r.Error);
            int ax = (int)r.Info["shift_axis"];
            Assert.Equal(axis, ax);
            double ulp = (double)r.Info["ulp"];
            double maxAbs = Verts(r.A).Max(v => Math.Abs(Get(v, ax)));
            Assert.Equal(Up(maxAbs) - maxAbs, ulp);
            var va = r.A.VertexIndices().ToArray(); var vb = r.B.VertexIndices().ToArray();
            for (int i = 0; i < va.Length; ++i) {
                Vector3d a = r.A.GetVertex(va[i]), b = r.B.GetVertex(vb[i]);
                for (int k = 0; k < 3; ++k) {
                    double expected = k == ax ? Get(a, k) + sign * ulp : Get(a, k);
                    Assert.True(ContactTransforms.ExactSum(Get(a, k), k == ax ? sign * ulp : 0, out double s));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(Get(b, k)));
                    Assert.Equal(s, Get(b, k));
                }
            }
            // the largest coordinate moved by one representable step
            double top = sign > 0 ? Max(r.A, ax) : Min(r.A, ax);
            if (Math.Abs(top) == maxAbs)
                Assert.Equal(sign > 0 ? Up(top) : Down(top), sign > 0 ? Max(r.B, ax) : Min(r.B, ax));
        }

        [Theory]
        [InlineData(0, 2)] [InlineData(1, 3)] [InlineData(2, 4)] [InlineData(3, 5)]
        public void UlpShiftFew_UsesTwoToFiveUlps(int param, int ulps)
        {
            ContactResult r = ContactTransforms.Build(A(), B(), ContactVariant.UlpShiftFew, 2, 1, param);
            Assert.Null(r.Error);
            Assert.Equal(ulps, r.Info["ulps"]);
            double maxAbs = Verts(r.A).Max(v => Math.Abs(v.z));
            Assert.Equal(maxAbs + ulps * (double)r.Info["ulp"], Max(r.B, 2));
        }

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 3)]
        public void Mirror_ReflectsExactlyAndKeepsTheOrientation(int axis, int rank)
        {
            // an asymmetric closed mesh: a box with one corner pulled out
            DMesh3 m = Box(0.125, 0.0625, -0.25, 0.75, 0.5, 0.625);
            int vid = m.VertexIndices().First();
            m.SetVertex(vid, m.GetVertex(vid) + new Vector3d(-0.0625, -0.125, -0.0625));
            ContactResult r = ContactTransforms.Build(m, m, ContactVariant.Mirror, axis, 1, rank);
            Assert.Null(r.Error);
            int ax = (int)r.Info["mirror_axis"];
            double c = (double)r.Info["plane"];
            Assert.Contains(Verts(r.A), v => Get(v, ax) == c);                        // the plane goes through a vertex coordinate of A
            var va = r.A.VertexIndices().ToArray(); var vb = r.B.VertexIndices().ToArray();
            for (int i = 0; i < va.Length; ++i)
                for (int k = 0; k < 3; ++k) {
                    double x = Get(r.A.GetVertex(va[i]), k), y = Get(r.B.GetVertex(vb[i]), k);
                    if (k == ax) { Assert.True(ContactTransforms.ExactSum(2 * c, -x, out double s)); Assert.Equal(s, y); }
                    else Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y));
                    if (k == ax && x == c) Assert.Equal(c, y);                         // a vertex on the plane stays
                }
            Assert.True(r.B.IsClosed());
            Assert.Equal(ContactTransforms.Volume(r.A), ContactTransforms.Volume(r.B), 12);   // outward orientation kept: same positive volume
            Assert.True(ContactTransforms.Volume(r.B) > 0);
        }

        [Fact]
        public void AnInexactPlacementIsRefused_NotRounded()
        {
            // a vertex at 1e-30 in every coordinate: a shift of order 1 cannot be exact for it
            DMesh3 b = B();
            b.SetVertex(b.VertexIndices().First(), new Vector3d(1e-30, 1e-30, 1e-30));
            DMesh3 a = A();
            ContactResult r = ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0);
            // the contact axis needs a shift of order 1 for A and B centred at the origin with different extents: not exact for the tiny vertex
            Assert.NotNull(r.Error);
            if (r.Error == null) {
                // (if a placement was found it is exact: the tiny vertex moved by a sum that is exact)
                double[] t = (double[])r.Info["shift"];
                Assert.True(ContactTransforms.ExactSum(1e-30, t[0], out _) && ContactTransforms.ExactSum(1e-30, t[1], out _) && ContactTransforms.ExactSum(1e-30, t[2], out _));
            } else
                Assert.Null(r.B);
            Assert.False(ContactTransforms.ExactSum(1.0, 1e-30, out _));
            Assert.True(ContactTransforms.ExactSum(0.5, 0.25, out double s) && s == 0.75);
            Assert.False(ContactTransforms.ExactSum(0.1, 0.2, out _));
        }

        // ---- the snap of the region-clipping corpus gate (Build(..., snapOnInexact: true), #1630): a placement that is not exact for the coordinates is made exact by a snap to 2^-46

        static DMesh3 Bx(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new TrivialBox3Generator { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();

        // (the embedded B is moved by a fraction 2^-depth of its extent, a multiple of the grid divided by 2^depth: its grid is finer by that factor)
        static void AssertOnTheGrid(DMesh3 m, double fraction = 1)
        {
            double g = ContactTransforms.SnapGrid * fraction;
            foreach (Vector3d v in Verts(m))
                Assert.True(ContactTransforms.RoundToGrid(v.x, g) == v.x && ContactTransforms.RoundToGrid(v.y, g) == v.y && ContactTransforms.RoundToGrid(v.z, g) == v.z);
        }

        // the faces touch: the extreme face of A along the contact axis and the near face of B are the same plane, bit for bit
        static void AssertFaceContact(ContactResult r)
        {
            int ax = (int)r.Info["contact_axis"], sg = (int)r.Info["contact_sign"];
            double pa = sg > 0 ? Max(r.A, ax) : Min(r.A, ax), pb = sg > 0 ? Min(r.B, ax) : Max(r.B, ax);
            Assert.Equal(pa, pb);
            Assert.Equal(pa, (double)r.Info["plane"]);
        }

        [Fact]
        public void ContactShiftNotExact_IsPlacedAfterTheSnap_WithTheContactKept()
        {
            // arbitrary doubles on every axis: the difference of two planes needs more than 53 bits in every direction
            DMesh3 a = Bx(0.15, 0.65, 0.2, 0.3, 0.85, 0.7), b = Bx(0.35, 0.1, 0.2, 0.9, 0.65, 0.45);
            ContactResult first = ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0);
            Assert.StartsWith("contact shift not exact", first.Error);
            Assert.Null(first.B);
            ContactResult r = ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0, true);
            Assert.Null(r.Error);
            Assert.Equal(ContactTransforms.SnapGrid, (double)r.Info["snapped"]);
            Assert.StartsWith("contact shift not exact", (string)r.Info["inexact_before"]);
            Assert.Equal("face", r.Info["kind"]);
            AssertFaceContact(r);
            AssertOnTheGrid(r.A); AssertOnTheGrid(r.B);
            Assert.True(r.B.IsClosed() && ContactTransforms.Volume(r.B) > 0);
        }

        [Fact]
        public void TranslationNotExact_IsPlacedAfterTheSnap_WithTheContactKept()
        {
            // dyadic boxes, but a vertex of B at 1e-30: no shift of order 1 is exact for it
            DMesh3 a = Bx(-0.375, -0.25, -0.25, 0.375, 0.25, 0.25), b = Bx(-0.25, -0.375, -0.3125, 0.25, 0.375, 0.3125);
            b.SetVertex(b.VertexIndices().First(), new Vector3d(1e-30, 1e-30, 1e-30));
            ContactResult first = ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0);
            Assert.StartsWith("translation not exact", first.Error);
            ContactResult r = ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0, true);
            Assert.Null(r.Error);
            Assert.Equal("translation not exact", r.Info["inexact_before"]);
            Assert.Equal("face", r.Info["kind"]);
            AssertFaceContact(r);
            AssertOnTheGrid(r.A); AssertOnTheGrid(r.B);
        }

        [Fact]
        public void EmbeddingDepthNotExact_IsPlacedAfterTheSnap_WithTheDepthKept()
        {
            DMesh3 a = Bx(0.2, 0.65, 0.3, 0.7, 0.85, 0.85), b = Bx(0.45, 0.3, 0.1, 0.7, 0.45, 0.7);
            ContactResult first = ContactTransforms.Build(a, b, ContactVariant.PartialOverlap, 2, 1, 1);
            Assert.StartsWith("embedding depth not exact", first.Error);
            ContactResult r = ContactTransforms.Build(a, b, ContactVariant.PartialOverlap, 2, 1, 1, true);
            Assert.Null(r.Error);
            Assert.StartsWith("embedding depth not exact", (string)r.Info["inexact_before"]);
            Assert.Equal("embedded", r.Info["kind"]);
            Assert.Equal(1, r.Info["depth"]);
            // B is embedded by half of its extent along the axis: its near face is at the plane of A's face minus the depth, bit for bit
            int ax = (int)r.Info["contact_axis"], sg = (int)r.Info["contact_sign"];
            double h = Max(r.B, ax) - Min(r.B, ax), pA = (double)r.Info["plane_a"], plane = (double)r.Info["plane"];
            Assert.Equal(pA - sg * 0.5 * h, plane);
            Assert.Equal(plane, sg > 0 ? Min(r.B, ax) : Max(r.B, ax));
            AssertOnTheGrid(r.A); AssertOnTheGrid(r.B, 0.5);
        }

        [Fact]
        public void TheSnapIsOptInAndLeavesExactPlacementsAlone()
        {
            // exact placements are the same with and without the option (no snap, no key), and without the option an inexact one stays an error
            ContactResult plain = ContactTransforms.Build(A(), B(), ContactVariant.FaceContact, 2, 1, 0);
            ContactResult withSnap = ContactTransforms.Build(A(), B(), ContactVariant.FaceContact, 2, 1, 0, true);
            Assert.Null(withSnap.Error);
            Assert.False(withSnap.Info.ContainsKey("snapped"));
            Assert.True(BitEqual(plain.A, withSnap.A) && BitEqual(plain.B, withSnap.B));
            DMesh3 tiny = B();
            tiny.SetVertex(tiny.VertexIndices().First(), new Vector3d(1e-30, 1e-30, 1e-30));
            Assert.NotNull(ContactTransforms.Build(A(), tiny, ContactVariant.FaceContact, 2, 1, 0).Error);
            // (only the face contacts: the other variants are never snapped)
            Assert.Equal(ContactTransforms.Build(A(), B(), ContactVariant.UlpShift, 2, 1, 0).Error, ContactTransforms.Build(A(), B(), ContactVariant.UlpShift, 2, 1, 0, true).Error);
        }

        [Fact]
        public void AnIsolatedVertexIsNotPartOfTheSolid()
        {
            DMesh3 a = A();
            a.AppendVertex(new Vector3d(0, 0, 0.5));                  // above the top face (0.25), used by no triangle
            ContactResult r = ContactTransforms.Build(a, B(), ContactVariant.FaceContact, 2, 1, 0);
            Assert.Null(r.Error);
            Assert.Equal("face", r.Info["kind"]);
            double top = r.A.TriangleIndices().Max(t => new[] { r.A.GetTriangle(t).a, r.A.GetTriangle(t).b, r.A.GetTriangle(t).c }.Max(v => r.A.GetVertex(v).z));
            Assert.Equal(top, (double)r.Info["plane"]);                // the plane of the box top, not of the isolated vertex (which is higher)
            Assert.True(Max(r.A, 2) > top);
        }

        [Fact]
        public void TinyInputIsRefusedNotRounded()
        {
            // normalization needs a power-of-two scale: a degenerate (zero-size) mesh is refused
            DMesh3 point = new DMesh3();
            point.AppendVertex(Vector3d.Zero); point.AppendVertex(Vector3d.Zero); point.AppendVertex(Vector3d.Zero);
            point.AppendTriangle(0, 1, 2);
            Assert.NotNull(ContactTransforms.Build(point, B(), ContactVariant.FaceContact, 0, 1, 0).Error);
        }

        [Theory]
        [InlineData(ContactVariant.FaceContact)] [InlineData(ContactVariant.PartialOverlap)] [InlineData(ContactVariant.UlpShift)]
        [InlineData(ContactVariant.UlpShiftFew)] [InlineData(ContactVariant.Mirror)]
        public void Invariance_PowerOfTwoScalesGiveTheSameResultBitForBit(ContactVariant variant)
        {
            // the transform normalizes to a longest side in [0.5, 1) by a power of two: inputs that differ by 2^k scales give the same pair
            ContactResult reference = ContactTransforms.Build(A(), B(), variant, 1, -1, 2);
            Assert.Null(reference.Error);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = A(), b = B();
                foreach (DMesh3 m in new[] { a, b })
                    foreach (int vid in m.VertexIndices().ToArray())
                        m.SetVertex(vid, s.Apply(m.GetVertex(vid)));
                ContactResult r = ContactTransforms.Build(a, b, variant, 1, -1, 2);
                if (s.IsExact && s.Translation == Vector3d.Zero) {
                    Assert.Null(r.Error);
                    Assert.True(BitEqual(reference.A, r.A), s.Name + " A");
                    Assert.True(BitEqual(reference.B, r.B), s.Name + " B");
                } else if (r.Error == null && (variant == ContactVariant.FaceContact || variant == ContactVariant.PartialOverlap)) {
                    // any other similarity (translations, rotations, scales that are not powers of two) may refuse the placement, but never rounds it:
                    // the contact plane of an accepted placement is bit-equal
                    int ax = (int)r.Info["contact_axis"], sg = (int)r.Info["contact_sign"];
                    if ((int)r.Info["depth"] < 0) {
                        double pa = sg > 0 ? Max(r.A, ax) : Min(r.A, ax), pb = sg > 0 ? Min(r.B, ax) : Max(r.B, ax);
                        Assert.Equal(BitConverter.DoubleToInt64Bits(pa), BitConverter.DoubleToInt64Bits(pb));
                    }
                }
            }
        }

        [Fact]
        public void Reuse_AndDeterminism_TheSameInputsGiveBitIdenticalPairs()
        {
            foreach (ContactVariant variant in Enum.GetValues(typeof(ContactVariant))) {
                ContactResult x = ContactTransforms.Build(A(), B(), variant, 0, 1, 1), y = ContactTransforms.Build(A(), B(), variant, 0, 1, 1);
                Assert.Null(x.Error);
                Assert.True(BitEqual(x.A, y.A) && BitEqual(x.B, y.B), variant.ToString());
            }
        }

        [Fact]
        public void TheInputsAreNotModified()
        {
            DMesh3 a = A(), b = B(), a0 = A(), b0 = B();
            ContactTransforms.Build(a, b, ContactVariant.FaceContact, 2, 1, 0);
            Assert.True(BitEqual(a, a0) && BitEqual(b, b0));
        }

        // C: a box on the other side; the chain of the abc-chained class places B and C against A
        static DMesh3 C() => Box(0, 0, 0, 0.625, 0.5, 0.75);

        [Fact]
        public void BuildChain_PlacesBAndCInTheFrameOfA_Exactly()
        {
            ChainResultT r = ContactTransforms.BuildChain(A(), B(), C(), ContactVariant.FaceContact, 2, 1, 0, ContactVariant.FaceContact, 2, -1, 0);
            Assert.Null(r.Error);
            Assert.True(BitEqual(r.A, A()));                                                  // A is already normalized: unchanged
            // B on the +z side, C on the -z side of A, both touching in the plane of a face of A
            Assert.Equal(Max(r.A, 2), Min(r.B, 2));
            Assert.Equal(Min(r.A, 2), Max(r.C, 2));
            Assert.Equal("face", r.Info["b_kind"]);
            Assert.Equal("face", r.Info["c_kind"]);
            Assert.Equal(1, r.Info["b_contact_sign"]);
            Assert.Equal(-1, r.Info["c_contact_sign"]);
            Assert.True(Min(r.B, 2) >= Max(r.A, 2) && Max(r.C, 2) <= Min(r.A, 2));            // B and C touch A only
            // the single placements give the same meshes (the second one does not change the frame)
            Assert.True(BitEqual(r.B, ContactTransforms.Build(A(), B(), ContactVariant.FaceContact, 2, 1, 0).B));
            Assert.True(BitEqual(r.C, ContactTransforms.Build(A(), C(), ContactVariant.FaceContact, 2, -1, 0).B));
        }

        [Fact]
        public void BuildChain_NormalizesAnyFrameOfA_AndKeepsIt()
        {
            // A far from the origin and not unit sized: the first placement normalizes it, the second must not change it again
            DMesh3 a = Box(3, -5, 2.5, 6, 4, 3);
            ChainResultT r = ContactTransforms.BuildChain(a, B(), C(), ContactVariant.PartialOverlap, 0, 1, 1, ContactVariant.FaceContact, 1, -1, 0);
            Assert.Null(r.Error);
            Assert.True(ContactTransforms.SameVertices(r.A, ContactTransforms.Build(a, B(), ContactVariant.PartialOverlap, 0, 1, 1).A));
            Assert.Equal("embedded", r.Info["b_kind"]);
        }

        [Fact]
        public void BuildChain_SingleVariantsOfTheSecondPlacementAreOffsetsOfA()
        {
            // the model C has another vertex count than A: the second placement must be built from a copy of A, bit for bit
            DMesh3 sphereC = new Sphere3Generator_NormalizedCube { Radius = 0.375, EdgeVertices = 6 }.Generate().MakeDMesh();
            Assert.NotEqual(A().VertexCount, sphereC.VertexCount);
            foreach (ContactVariant v in new[] { ContactVariant.UlpShift, ContactVariant.UlpShiftFew, ContactVariant.Mirror }) {
                ChainResultT r = ContactTransforms.BuildChain(A(), B(), sphereC, ContactVariant.FaceContact, 0, 1, 0, v, 1, 1, 1);
                Assert.Null(r.Error);
                Assert.True(BitEqual(r.C, ContactTransforms.Build(A(), A(), v, 1, 1, 1).B), v.ToString());
                Assert.Equal(A().VertexCount, r.C.VertexCount);
            }
        }

        [Fact]
        public void BuildChain_ReportsWhichPlacementIsInexact()
        {
            DMesh3 tiny = B();
            tiny.SetVertex(tiny.VertexIndices().First(), new Vector3d(1e-30, 1e-30, 1e-30));
            ChainResultT r = ContactTransforms.BuildChain(A(), tiny, C(), ContactVariant.FaceContact, 2, 1, 0, ContactVariant.FaceContact, 2, -1, 0);
            Assert.StartsWith("B: ", r.Error);
            Assert.Null(r.B);
            ChainResultT r2 = ContactTransforms.BuildChain(A(), B(), tiny, ContactVariant.FaceContact, 2, 1, 0, ContactVariant.FaceContact, 2, -1, 0);
            Assert.StartsWith("C: ", r2.Error);
        }

        [Fact]
        public void BuildChain_IsDeterministicAndDoesNotModifyTheInputs()
        {
            DMesh3 a = A(), b = B(), c = C();
            ChainResultT x = ContactTransforms.BuildChain(a, b, c, ContactVariant.PartialOverlap, 1, 1, 0, ContactVariant.Mirror, 0, 1, 2);
            ChainResultT y = ContactTransforms.BuildChain(a, b, c, ContactVariant.PartialOverlap, 1, 1, 0, ContactVariant.Mirror, 0, 1, 2);
            Assert.Null(x.Error);
            Assert.True(BitEqual(x.A, y.A) && BitEqual(x.B, y.B) && BitEqual(x.C, y.C));
            Assert.True(BitEqual(a, A()) && BitEqual(b, B()) && BitEqual(c, C()));
        }

        [Fact]
        public void VariantNamesRoundTrip()
        {
            foreach (string n in ContactTransforms.VariantNames)
                Assert.Equal(n, ContactTransforms.VariantNames[(int)ContactTransforms.ParseVariant(n)]);
            Assert.Throws<ArgumentException>(() => ContactTransforms.ParseVariant("rotated"));
        }
    }
}
