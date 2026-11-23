using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The ring check of issue #1441 itself (Remesher.collapse_turns_from_ring, flip_turns_from_ring) on built
    /// neighbourhoods; the remeshing results are in RemesherRingTurnGuardTests.
    /// </summary>
    public class RemesherRingTurnCheckTests
    {
        static DMesh3 JitteredSphere(int seed, double jit) => RemesherRingTurnGuardTests.JitteredSphere(seed, jit);
        static int Inward(DMesh3 m, Vector3d center) => RemesherRingTurnGuardTests.Inward(m, center);

        static readonly Matrix3d Tilt = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);

        // A strip along a crease (the x axis, tilted): a narrow face A (width wA) and a wide face B (width wB) whose normals
        // are `degrees` apart, n irregular steps. Returns the crease vertex ids in order.
        static DMesh3 CreaseStrip(int n, double wA, double wB, double degrees, out int[] crease)
        {
            var m = new DMesh3();
            var rnd = new Random(5);
            double phi = (180 - degrees) * Math.PI / 180;      // the angle between the two faces, inside the material
            var dA = new Vector3d(0, 1, 0);
            var dB = new Vector3d(0, Math.Cos(phi), -Math.Sin(phi));
            crease = new int[n + 1];
            int[] a = new int[n + 1], b = new int[n + 1];
            double x = 0;
            for (int i = 0; i <= n; ++i) {
                var p = new Vector3d(x, 0, 0);
                crease[i] = m.AppendVertex(Tilt * p);
                a[i] = m.AppendVertex(Tilt * (p + wA * dA + new Vector3d(0.3 * rnd.NextDouble(), 0, 0)));
                b[i] = m.AppendVertex(Tilt * (p + wB * dB + new Vector3d(0.3 * rnd.NextDouble(), 0, 0)));
                x += 0.5 + rnd.NextDouble();
            }
            for (int i = 0; i < n; ++i) {
                // face A (normal +z before the tilt), face B (normal `degrees` from it)
                m.AppendTriangle(crease[i], crease[i + 1], a[i + 1]); m.AppendTriangle(crease[i], a[i + 1], a[i]);
                m.AppendTriangle(crease[i + 1], crease[i], b[i]); m.AppendTriangle(crease[i + 1], b[i], b[i + 1]);
            }
            return m;
        }

        // A triangle that does not turn is never rejected, however far its face is from the ring normal: along a crease
        // of 85 degrees with a narrow face and one 160 times as wide, the narrow face is about 80 degrees from the ring
        // normal (the wide face has the weight cos 85 = 0.09, and still 14 times the weighted area), beyond the bound
        // of 70. Collapses along the crease keep every triangle in its face.
        [Theory]
        [InlineData(85)]        // both faces in the ring normal
        [InlineData(60)]
        [InlineData(120)]       // each face its own reference
        [InlineData(170)]       // a thin wedge
        public void CollapseAlongCrease_IsNotRejected(double degrees)
        {
            DMesh3 m = CreaseStrip(40, 0.25, 40, degrees, out int[] crease);
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            int tested = 0;
            for (int i = 1; i + 2 < crease.Length; ++i) {
                int a = crease[i], b = crease[i + 1];
                Index2i et = m.GetEdgeT(m.FindEdge(a, b));
                foreach (Vector3d to in new[] { 0.5 * (m.GetVertex(a) + m.GetVertex(b)), m.GetVertex(a), m.GetVertex(b), Vector3d.Lerp(m.GetVertex(a), m.GetVertex(b), 0.3) }) {
                    Vector3d pos = to;
                    Assert.False(r.collapse_turns_from_ring(a, b, ref pos, et.a, et.b), $"{degrees}: crease edge {i}");
                    tested++;
                }
            }
            Assert.Equal(4 * 38, tested);
        }

        // ... and a flip in the narrow face (its two triangles and the two after the flip lie in one plane) is not either
        [Theory]
        [InlineData(85)]
        [InlineData(120)]
        public void FlipInAFaceAtACrease_IsNotRejected(double degrees)
        {
            DMesh3 m = CreaseStrip(40, 0.25, 40, degrees, out int[] crease);
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            int tested = 0;
            foreach (int eid in m.EdgeIndices()) {
                if (m.IsBoundaryEdge(eid)) continue;
                Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid), ov = m.GetEdgeOpposingV(eid);
                if (crease.Contains(ev.a) && crease.Contains(ev.b)) continue;      // (the crease edges themselves)
                Vector3d n0 = m.GetTriNormal(et.a), n1 = m.GetTriNormal(et.b);
                if (n0.Dot(n1) < 0.999) continue;       // the edges inside a face
                Assert.False(r.flip_turns_from_ring(ev.a, ev.b, ov.a, ov.b, et.a), $"{degrees}: edge {eid}");
                tested++;
            }
            Assert.Equal(158, tested);
        }

        // A pillow: a curved top and bottom sheet (z = +-(gap + bulge (1 - x^2 - y^2))) over an n x n grid, sharing the
        // rim vertices, so the ring of a rim vertex has triangles of both sheets, facing against each other.
        static DMesh3 Pillow(int n, double gap, double bulge, out List<int> rim, out int[,] top)
        {
            var m = new DMesh3();
            top = new int[n + 1, n + 1];
            int[,] bottom = new int[n + 1, n + 1];
            rim = new List<int>();
            var rnd = new Random(11);
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j) {
                    bool onRim = i == 0 || j == 0 || i == n || j == n;
                    double x = -1 + 2.0 * i / n, y = -1 + 2.0 * j / n;
                    if (onRim == false) { x += 0.3 / n * (rnd.NextDouble() - 0.5); y += 0.3 / n * (rnd.NextDouble() - 0.5); }
                    double z = onRim ? 0 : gap + bulge * (1 - x * x) * (1 - y * y);
                    top[i, j] = m.AppendVertex(Tilt * new Vector3d(x, y, z));
                    bottom[i, j] = onRim ? top[i, j] : m.AppendVertex(Tilt * new Vector3d(x, y, -z));
                    if (onRim) rim.Add(top[i, j]);
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    if ((i == n - 1 && j == 0) || (i == 0 && j == n - 1)) {
                        // (the other diagonal in these two corners: no triangle with three rim vertices)
                        m.AppendTriangle(top[i, j], top[i + 1, j], top[i, j + 1]); m.AppendTriangle(top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]);
                        m.AppendTriangle(bottom[i, j], bottom[i, j + 1], bottom[i + 1, j]); m.AppendTriangle(bottom[i + 1, j], bottom[i, j + 1], bottom[i + 1, j + 1]);
                        continue;
                    }
                    m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]); m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                    m.AppendTriangle(bottom[i, j], bottom[i + 1, j + 1], bottom[i + 1, j]); m.AppendTriangle(bottom[i, j], bottom[i, j + 1], bottom[i + 1, j + 1]);
                }
            return m;
        }

        // moving vertex v to pos leaves one of its triangles (other than those with vertex other) without an area
        static bool CreatesFlat(DMesh3 m, int v, int other, Vector3d pos)
        {
            foreach (int t in m.VtxTrianglesItr(v)) {
                Index3i tv = m.GetTriangle(t);
                if (tv.a == other || tv.b == other || tv.c == other)
                    continue;
                Vector3d a = tv.a == v ? pos : m.GetVertex(tv.a), b = tv.b == v ? pos : m.GetVertex(tv.b), c = tv.c == v ? pos : m.GetVertex(tv.c);
                if (MeshRefinerBase.TriangleQuality(ref a, ref b, ref c) < 1e-6)
                    return true;
            }
            return false;
        }

        // At the rim of a thin part the two sides face against each other and their normals nearly cancel in the sum
        // over the ring: the reference of a triangle is the sum over the triangles that face with it, its own side. So
        // the small turns of the collapses and flips of a curved thin sheet are not measured against the other side.
        [Fact]
        public void ThinSheet_OtherSideIsNotAReference()
        {
            DMesh3 m = Pillow(10, 0.01, 0.15, out List<int> rim, out int[,] top);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            var r = new RingProbe(m) { MinTriangleQuality = 0.2 };
            int collapses = 0, flips = 0, rimCollapses = 0;
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid), ov = m.GetEdgeOpposingV(eid);
                bool ra = rim.Contains(ev.a), rb = rim.Contains(ev.b);
                if (ra == false && rb == false && (rim.Contains(ov.a) || rim.Contains(ov.b)) == false)
                    continue;       // an edge with the rim in its ring: one end on it, or an opposite vertex
                if (ra != rb) {
                    // from the sheet onto the rim vertex (as with a constrained rim)
                    Vector3d pos = m.GetVertex(ra ? ev.a : ev.b);
                    int moved = ra ? ev.b : ev.a;
                    if (CreatesFlat(m, moved, ra ? ev.a : ev.b, pos))
                        continue;       // (a diagonal: three rim vertices in a line; Remesher.collapse_creates_flat)
                    Assert.False(r.collapse_turns_from_ring(ev.a, ev.b, ref pos, et.a, et.b), $"collapse of edge {eid}");
                    collapses++;
                }
                if (ra && rb) {
                    // along the rim, to the midpoint: the triangles of both sheets around the two vertices change a
                    // little, each within its sheet
                    Vector3d mid = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
                    if (CreatesFlat(m, ev.a, ev.b, mid) == false && CreatesFlat(m, ev.b, ev.a, mid) == false) {
                        Assert.False(r.collapse_turns_from_ring(ev.a, ev.b, ref mid, et.a, et.b), $"rim collapse of edge {eid}");
                        rimCollapses++;
                    }
                    continue;
                }
                // the flips the flip test passes and that leave two triangles with an area
                Vector3d vA = m.GetVertex(ev.a), vB = m.GetVertex(ev.b), vC = m.GetVertex(ov.a), vD = m.GetVertex(ov.b);
                if (r.FlipTestRejects(ev.a, ev.b, ov.a, ov.b, et.a)
                    || Math.Min(MeshRefinerBase.TriangleQuality(ref vC, ref vD, ref vB), MeshRefinerBase.TriangleQuality(ref vD, ref vC, ref vA)) < 1e-6)
                    continue;
                Assert.False(r.flip_turns_from_ring(ev.a, ev.b, ov.a, ov.b, et.a), $"flip of edge {eid}");
                flips++;
            }
            Assert.Equal((132, 188, 40), (collapses, flips, rimCollapses));
        }

        // A fan around a centre vertex on a sphere cap, with one thin triangle standing tilted to the surface: the
        // collapse that turns it to face inward passes the flip test (it compares the triangle with itself) and is
        // rejected by the ring check; the collapse that does not move it passes both.
        [Fact]
        public void TiltedThinTriangle_CollapseThatTurnsItInwardIsRejected()
        {
            FindTiltedCollapse(out DMesh3 m, out int a, out int b, out Vector3d inwardPos, out Index2i et);
            var r = new RingProbe(m) { MinTriangleQuality = 0.2 };
            Vector3d pos = inwardPos;
            Assert.False(r.FlipTestRejects(a, b, pos, et));        // the gap of the issue
            Assert.True(r.collapse_turns_from_ring(a, b, ref pos, et.a, et.b));
            // the mesh after that collapse has an inward triangle
            DMesh3 after = new DMesh3(m);
            Assert.Equal(MeshResult.Ok, after.CollapseEdge(b, a, out _));
            after.SetVertex(b, pos);
            Assert.Equal(0, Inward(m, Vector3d.Zero));
            Assert.True(Inward(after, Vector3d.Zero) > 0);
        }

        // The ring is that of both vertices of the edge: the check does not depend on which of them is named first.
        // (Over all edges of a mesh of the family in which the flip test passes collapses that the ring check rejects.)
        [Fact]
        public void Check_DoesNotDependOnTheOrderOfTheVertices()
        {
            FindTiltedCollapse(out DMesh3 m, out _, out _, out _, out _);
            var r = new RingProbe(m) { MinTriangleQuality = 0.2 };
            int collapsesRejected = 0, flipsRejected = 0, edges = 0;
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid), ov = m.GetEdgeOpposingV(eid);
                Vector3d pos = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
                bool c0 = r.collapse_turns_from_ring(ev.a, ev.b, ref pos, et.a, et.b), c1 = r.collapse_turns_from_ring(ev.b, ev.a, ref pos, et.b, et.a);
                Assert.True(c0 == c1, $"collapse of edge {eid}");
                bool f0 = r.flip_turns_from_ring(ev.a, ev.b, ov.a, ov.b, et.a), f1 = r.flip_turns_from_ring(ev.b, ev.a, ov.a, ov.b, et.a);
                Assert.True(f0 == f1, $"flip of edge {eid}");
                // (nor on which of the edge's two triangles is the first: the old normal is that of the pair)
                Assert.True(f0 == r.flip_turns_from_ring(ev.a, ev.b, ov.b, ov.a, et.b), $"flip of edge {eid}, second triangle first");
                edges++;
                if (c0) collapsesRejected++;
                if (f0) flipsRejected++;
            }
            Assert.Equal((882, 17, 43), (edges, collapsesRejected, flipsRejected));
        }

        // The old normal of a flip is that of the pair, whichever of its triangles is named first: a triangle in a flat
        // region with a wall on its edge (88 degrees from it), flipped into two triangles 72 degrees from the flat region.
        // They are farther from it than the pair (51 degrees), though nearer than the wall alone.
        [Fact]
        public void Flip_OldNormalIsThatOfThePair()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(Tilt * new Vector3d(0, 0, 0)), b = m.AppendVertex(Tilt * new Vector3d(1, 0, 0));
            int c = m.AppendVertex(Tilt * new Vector3d(0.5, 1, 0)), d = m.AppendVertex(Tilt * new Vector3d(0.5, -0.05, 2));
            int p = m.AppendVertex(Tilt * new Vector3d(-6, 6, 0)), q = m.AppendVertex(Tilt * new Vector3d(7, 6, 0));
            int flat = m.AppendTriangle(a, b, c), wall = m.AppendTriangle(b, a, d);
            m.AppendTriangle(a, c, p); m.AppendTriangle(b, q, c);
            var r = new RingProbe(m) { MinTriangleQuality = 0.2 };
            Assert.False(r.FlipTestRejects(a, b, c, d, flat));      // (the flip test passes it)
            Assert.True(r.flip_turns_from_ring(a, b, c, d, flat));
            Assert.True(r.flip_turns_from_ring(a, b, d, c, wall));
            Assert.True(r.flip_turns_from_ring(b, a, d, c, wall));
        }

        sealed class RingProbe : Remesher
        {
            public RingProbe(DMesh3 m) : base(m) { }
            public bool FlipTestRejects(int a, int b, int c, int d, int t0) => flip_inverts_normals(a, b, c, d, t0);
            public bool FlipTestRejects(int a, int b, Vector3d pos, Index2i et)
                => collapse_creates_flip_or_invalid(a, b, ref pos, et.a, et.b) || collapse_creates_flip_or_invalid(b, a, ref pos, et.a, et.b);
        }

        // the first collapse of the family (seed 1, jitter 0.3) that the flip test passes and that leaves an inward
        // triangle: found by trying every short edge of the mesh before each pass of a remesher without the checks
        static void FindTiltedCollapse(out DMesh3 mesh, out int a, out int b, out Vector3d pos, out Index2i et)
        {
            DMesh3 m = JitteredSphere(1, 0.3);
            Remesher r = RemesherRingTurnGuardTests.Make(m, false, 0, false);
            for (int pass = 0; pass < 6; ++pass) {
                if (Inward(m, Vector3d.Zero) == 0) {
                    var probe = new RingProbe(m);
                    foreach (int eid in m.EdgeIndices()) {
                        Index2i ev = m.GetEdgeV(eid);
                        Vector3d p = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
                        Index2i t = m.GetEdgeT(eid);
                        if (probe.FlipTestRejects(ev.a, ev.b, p, t))
                            continue;
                        DMesh3 after = new DMesh3(m);
                        if (after.CollapseEdge(ev.b, ev.a, out _) != MeshResult.Ok)
                            continue;
                        after.SetVertex(ev.b, p);
                        if (Inward(after, Vector3d.Zero) > 0) {
                            mesh = m; a = ev.a; b = ev.b; pos = p; et = t;
                            return;
                        }
                    }
                }
                r.BasicRemeshPass();
            }
            throw new InvalidOperationException("no such collapse");
        }
    }
}
