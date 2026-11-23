using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class RoundingCapFirstTests
    {
        // a step: a slab of thickness T under a block of height H, extruded along y. The outline in (x, z), counter-clockwise; the foot of
        // the riser, A, lies w beyond the corner C of the slab's top and e below the line of the top, so that (A, C, B) of the end faces
        // is a cap: three corners almost on one line, its only short edge A-C
        // (l: the length of the cap's long edge C-B; by default B is the end of the slab's top, with l another corner B' of the top lies
        // l from C and the cap is (A, C, B'))
        internal static DMesh3 Step(double w, double e, double L = 0.5, double T = 1e-4, double H = 0.25, double b = 1e-3, double W = 1,
            Func<Vector3d, Vector3d> f = null, double l = 0)
        {
            double X = L + w + b;
            var o = new List<Vector2d> {
                new Vector2d(0, -T), new Vector2d(X, -T), new Vector2d(X, H), new Vector2d(L + w, H),
                new Vector2d(L + w, -e), new Vector2d(L, 0) };
            if (l > 0)
                o.Add(new Vector2d(L - l, 0));
            o.Add(new Vector2d(0, 0));
            var m = new DMesh3();
            for (int s = 0; s < 2; ++s)
                foreach (Vector2d p in o) {
                    var q = new Vector3d(p.x, s * W, p.y);
                    m.AppendVertex(f == null ? q : f(q));
                }
            int n = o.Count;
            // Q0 Q1 Q2 Q3 A C B = 0 .. 6, or Q0 Q1 Q2 Q3 A C B' B = 0 .. 7
            var cap = new List<int[]> { new[] { 4, 5, 6 }, new[] { 4, 6, 0 }, new[] { 0, 1, 4 }, new[] { 1, 3, 4 }, new[] { 1, 2, 3 } };
            if (l > 0)
                cap.Add(new[] { 6, 7, 0 });
            foreach (int[] t in cap) {
                m.AppendTriangle(t[0], t[1], t[2]);
                m.AppendTriangle(n + t[0], n + t[2], n + t[1]);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(i, n + i, n + j);
                m.AppendTriangle(i, n + j, j);
            }
            return m;
        }

        // a cap with a long edge of a few hundred times the degenerate area (half the product of its shortest and longest edge over that
        // area: 761, 476, 190, 76 for l = 4e-4, 2.5e-4, 1e-4, 4e-5): the limit of a cap is 32, so it is flipped. With a limit of 1024
        // (the first version of this change) these were contracted: 2 contractions, drift w, the volume +6.25e-7 of 3.0e-4 (0.21 %)
        [Theory]
        [InlineData(4e-4)]
        [InlineData(2.5e-4)]
        [InlineData(1e-4)]
        [InlineData(4e-5)]
        public void Step_ACapOfAFewHundredTimesTheArea_IsFlippedNotContracted(double l)
        {
            DMesh3 m = Step(5e-6, 1e-12, l: l);
            Assert.True(m.IsClosed());
            double d = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            Assert.InRange(0.5 * 5e-6 * l / (1e-12 * d * d), 64, 900);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(2, r.Flips);
            Assert.Equal(0, r.Collapses + r.Moves + r.Zips + r.StripZips + r.VertexRemovals);
            Assert.True(r.MaxDrift <= 2e-12, r.MaxDrift.ToString("R"));
            Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) <= 1e-15, (r.VolumeAfter - r.VolumeBefore).ToString("R"));
            Assert.Equal("", Defects(m));
        }

        // below the limit the short edge is still contracted: the floor of the criterion, which a limit of 1 or 4 would take away (and
        // with it the contractions that remove needles in clusters of short edges). A ratio of 19: the known cost of the floor, the
        // volume changes by 6.25e-7 as before
        [Fact]
        public void Step_ACapBelowTheFloor_IsContracted()
        {
            DMesh3 m = Step(5e-6, 1e-12, l: 1e-5);
            double d = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            Assert.InRange(0.5 * 5e-6 * 1e-5 / (1e-12 * d * d), 15, 25);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(0, r.Flips);
            Assert.Equal(2, r.Collapses);
            Assert.InRange(r.VolumeAfter - r.VolumeBefore, 6.2e-7, 6.3e-7);
            Assert.Equal("", Defects(m));
        }

        static string Defects(DMesh3 m) => gs.MeshRobustRepair.NotCleanReasons(m);
        static double Volume(DMesh3 m) => RoundingThinVolumeTests.Volume(m);

        // the cap is flipped: no vertex moves, the volume stays. Before, the short edge A-C was contracted on both end faces (2
        // contractions, drift w): the foot of the riser moved by w and the volume changed by w H W / 2 (6.25e-7 of 3.0e-4 for
        // w = 5e-6)
        [Theory]
        [InlineData(2e-6, 1e-12)]
        [InlineData(5e-6, 1e-12)]
        [InlineData(8e-6, 1e-12)]
        [InlineData(5e-6, 0)]
        public void Step_TheCapIsFlipped_AndTheVolumeStays(double w, double e)
        {
            DMesh3 m = Step(w, e);
            Assert.True(m.IsClosed());
            double before = Volume(m);
            Assert.InRange(before, 3.0e-4, 3.002e-4);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(2, r.Flips);
            Assert.Equal(0, r.Collapses + r.Moves + r.Zips + r.StripZips + r.VertexRemovals);
            Assert.Equal(14, m.VertexCount);
            Assert.Equal("", Defects(m));
            Assert.True(m.IsClosed());
            // (the flips are in the planes of the end faces up to e: the drift is at most e, the volume changes by rounding only)
            Assert.True(r.MaxDrift <= 2e-12, r.MaxDrift.ToString("R"));
            Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) <= 1e-15, (r.VolumeAfter - r.VolumeBefore).ToString("R"));
            Assert.True(Math.Abs(Volume(m) - before) <= 1e-15);
        }

        // the limit of a cap: half the product of the shortest edge (A-C, w) and the longest (A-B', l + w) against 32 times the degenerate
        // area (1e-12 of the squared diagonal). Just below it the short edge is contracted, just above it the longest edge is flipped,
        // at the scales 1, 2^-10 and 2^10 alike (the decision is made on lengths and areas that scale by exact powers of two)
        [Theory]
        [InlineData("identity", 0.9, 2, 0)]
        [InlineData("identity", 1.1, 0, 2)]
        [InlineData("scale 2^-10", 0.9, 2, 0)]
        [InlineData("scale 2^-10", 1.1, 0, 2)]
        [InlineData("scale 2^10", 0.9, 2, 0)]
        [InlineData("scale 2^10", 1.1, 0, 2)]
        public void Step_AtTheLimitOfACap(string frame, double factor, int collapses, int flips)
        {
            const double w = 5e-6;
            Similarity s = Similarity.ByName(frame);
            Assert.True(s.IsExact);
            double diagonal = gs.MeshRepairMetrics.TriangleBounds(Step(w, 1e-12, l: 1e-5)).DiagonalLength;
            // (l + w is the longest edge: l + w is about 1.6e-5 times the factor, l more than w)
            double l = factor * 2 * 32 * 1e-12 * diagonal * diagonal / w - w;
            Assert.InRange(l, 7e-6, 2e-5);
            DMesh3 m = Invariance.Transformed(Step(w, 1e-12, l: l), s);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply(), s.Name);
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(collapses, r.Collapses);
            Assert.Equal(flips, r.Flips);
            Assert.Equal("", Defects(m));
        }

        // a cap whose flip is refused (the slab is so thin that the triangle (C, Q0, A) of the flip, w T / 2, would be degenerate itself):
        // the old order follows, and the short edge is contracted
        [Fact]
        public void Step_ARefusedFlip_FallsBackToTheContraction()
        {
            DMesh3 m = Step(6e-9, 1e-12);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(0, r.Flips);
            Assert.Equal(2, r.Collapses);
            Assert.Equal("", Defects(m));
        }

        // the refused flip of a cap is evaluated once: with a bound below every edge there is no contraction and no move either, the
        // caps stay, and the flip of the longest edge is not proposed a second time in the loop over the edges
        [Fact]
        public void Step_ARefusedFlip_IsEvaluatedOnce()
        {
            DMesh3 m = Step(6e-9, 1e-12);
            var r = new MeshRoundingRepair(m) { MaxDisplacement = 1e-13 };
            Assert.False(r.Apply());
            Assert.Equal(2, r.RemainingDegenerate);
            Assert.Equal(0, r.Flips + r.Collapses + r.Moves + r.Zips);
            Assert.Equal(14, m.VertexCount);
            Assert.Equal(1198L, r.Evaluations);
        }

        // a second run on the result changes nothing, and a second instance on a copy gives the same mesh
        [Fact]
        public void Step_Reuse()
        {
            DMesh3 m = Step(5e-6, 1e-12), copy = new DMesh3(m);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            var r2 = new MeshRoundingRepair(copy);
            Assert.True(r2.Apply());
            Assert.Equal("", Invariance.Difference(m, copy));
            Assert.Equal(r.VolumeAfter, r2.VolumeAfter);
            Assert.True(r.Apply());
            Assert.Equal(0, r.InitialDegenerate);
            Assert.Equal(0, r.Flips + r.Collapses + r.Moves + r.Zips);
            Assert.Equal(r.VolumeBefore, r.VolumeAfter);
            Assert.Equal("", Invariance.Difference(m, copy));
        }

        // the repair has parallel stages (the evaluation of the edits): serial and parallel runs, and repeated runs, give the same mesh
        [Theory]
        [InlineData(5e-6, 0)]
        [InlineData(5e-6, 1e-4)]
        public void Step_SerialAndParallelAndRepeatedRuns_GiveTheSameMesh(double w, double l)
        {
            DMesh3 first = null;
            string firstEdits = null;
            foreach (bool serial in new[] { true, false, true, false }) {
                DMesh3 m = Step(w, 1e-12, l: l);
                var r = new MeshRoundingRepair(m);
                gParallel.SerialOnThisThread = serial;
                try { Assert.True(r.Apply()); } finally { gParallel.SerialOnThisThread = false; }
                string edits = r.Flips + " " + r.Collapses + " " + r.Moves + " " + r.VolumeAfter.ToString("R");
                if (first == null) { first = m; firstEdits = edits; continue; }
                Assert.Equal(firstEdits, edits);
                Assert.Equal("", Invariance.Difference(first, m));
            }
            Assert.StartsWith("2 0 0", firstEdits);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Step_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            DMesh3 reference = Step(5e-6, 1e-12);
            var r0 = new MeshRoundingRepair(reference);
            Assert.True(r0.Apply());
            double cube = s.Scale * s.Scale * s.Scale;

            DMesh3 m = Invariance.Transformed(Step(5e-6, 1e-12), s);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply(), s.Name);
            Assert.Equal("", Defects(m));
            if (s.IsExact) {
                // a power-of-two scale: the same edits and the same mesh, bit for bit
                Assert.Equal(2, r.Flips);
                Assert.Equal(r0.VolumeAfter * cube, r.VolumeAfter);
                Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(m, s)));
                return;
            }
            // another frame rounds the corners of the cap off its line by more than e: where it is still degenerate it is flipped, and
            // no vertex moves
            if (s != Similarity.RotatedUtmMillimetre) {
                Assert.Equal(2, r.InitialDegenerate);
                Assert.Equal(2, r.Flips);
                Assert.Equal(0, r.Collapses + r.Moves + r.Zips + r.StripZips + r.VertexRemovals);
                Assert.Equal(14, m.VertexCount);
                Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) / cube <= 1e-9 * 3e-4, s.Name + " " + (r.VolumeAfter - r.VolumeBefore) / cube);
                return;
            }
            // (the limit of this step: rotated, at millimetre scale and far from the origin, the rounding of the input folds the cap
            // over its neighbour by 2e-8 of the diagonal. No triangle is degenerate; the mesh intersects itself, and the intersections
            // are resolved as before, by the contraction of A-C, with the volume change of that)
            Assert.Equal(0, r.InitialDegenerate);
            Assert.True(r.InitialIntersections > 0);
            Assert.Equal(0, r.Flips);
            Assert.Equal(2, r.Collapses);
            Assert.InRange((r.VolumeAfter - r.VolumeBefore) / cube, 6.2e-7, 6.4e-7);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
