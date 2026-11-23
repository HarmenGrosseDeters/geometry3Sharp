using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1441: with PreventNormalFlips or MinTriangleQuality, Remesher left triangles facing into a smooth sphere.
    /// The flip tests compare a changed triangle with its own normal before the operation (a turn of up to 90 degrees
    /// passes), and a thin triangle already stands tilted to a curved surface, so one collapse or flip turned it past
    /// 90 degrees from the surface. The ring check bounds the turn against the normal of the triangles around the edge
    /// (the check itself: RemesherRingTurnCheckTests). Public API only, so the file builds against the library before
    /// the fix (tools/new_tests_on_base.py).
    /// </summary>
    public class RemesherRingTurnGuardTests
    {
        internal const double H = 2.0 / 7;       // the edge length of the input sphere

        // a sphere around a centre (the unit sphere at the origin for the family of the issue)
        internal sealed class SphereTarget : IProjectionTarget
        {
            public Vector3d Center = Vector3d.Zero;
            public double Radius = 1;
            public Vector3d Project(Vector3d p, int id = -1) => Center + Radius * (p - Center).Normalized;
        }

        // the input of the issue: the normalized cube sphere with every vertex moved by up to jit * H / 2 per axis and
        // put back onto the sphere
        internal static DMesh3 JitteredSphere(int seed, double jit)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 8, Radius = 1 }.Generate().MakeDMesh();
            var rnd = new Random(seed);
            foreach (int v in m.VertexIndices()) {
                var d = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                m.SetVertex(v, (m.GetVertex(v) + d * (jit * H / 2)).Normalized);
            }
            return m;
        }

        // triangles with an area that face towards the centre (area in units of radius^2)
        internal static int Inward(DMesh3 m, Vector3d center, double radius = 1)
        {
            int n = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in m.TriangleIndices()) {
                m.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d normal = ((b - a) / radius).Cross((c - a) / radius);
                if (normal.Length * 0.5 > 1e-12 && normal.Dot((a + b + c) / 3 - center) < 0)
                    n++;
            }
            return n;
        }

        internal static Remesher Make(DMesh3 m, bool pro, double quality, bool preventFlips, bool parallel = false, SphereTarget target = null, double scale = 1)
        {
            Remesher r = pro ? new RemesherPro(m) : new Remesher(m);
            r.MinTriangleQuality = quality;
            r.PreventNormalFlips = preventFlips;
            r.EnableParallelSmooth = parallel;
            r.EnableParallelProjection = parallel;
            r.SetTargetEdgeLength(scale * 0.8 * H);
            r.SetProjectionTarget(target ?? new SphereTarget());
            return r;
        }

        static void Pass(Remesher r)
        {
            if (r is RemesherPro pro) pro.RemeshIteration(); else r.BasicRemeshPass();
        }

        // the inputs of the issue with every triangle facing outward (65 of the 80: 40 at 0.3, 25 at 0.45)
        static List<int> OutwardSeeds(double jit) => Enumerable.Range(1, 40).Where(s => Inward(JitteredSphere(s, jit), Vector3d.Zero) == 0).ToList();

        // The family of the issue: after no pass a triangle faces inward, and the result is a clean closed manifold.
        // On master: 61 of the 65 inputs with an inward triangle after some pass with MinTriangleQuality = 0.2 (15 after
        // the last), 55 (14) with PreventNormalFlips alone. PreventNormalFlips alone does not check the smoothing and
        // projection moves: there one input (seed 17 at 0.45) has inward triangles after passes 1 to 3, none at the end.
        [Theory]
        [InlineData(0.3, 0.2, false, 40, 0)]
        [InlineData(0.45, 0.2, false, 25, 0)]
        [InlineData(0.3, 0.2, true, 40, 0)]
        [InlineData(0.3, 0, true, 40, 0)]
        [InlineData(0.45, 0, true, 25, 1)]
        public void JitteredSphere_NoTriangleFacesInward(double jit, double quality, bool preventFlips, int inputs, int inputsInwardInBetween)
        {
            List<int> seeds = OutwardSeeds(jit);
            Assert.Equal(inputs, seeds.Count);
            var bad = new List<string>();
            var inBetween = new HashSet<int>();
            foreach (int seed in seeds) {
                DMesh3 m = JitteredSphere(seed, jit);
                Remesher r = Make(m, false, quality, preventFlips);
                for (int pass = 0; pass < 6; ++pass) {
                    r.BasicRemeshPass();
                    int n = Inward(m, Vector3d.Zero);
                    if (n > 0 && pass < 5) inBetween.Add(seed);
                    if (n > 0 && (pass == 5 || inputsInwardInBetween == 0)) bad.Add($"seed {seed} pass {pass}: {n}");
                }
                Assert.Equal(0, r.InvertingPassesRejected);
                Assert.True(Invariance.AllFinite(m), $"seed {seed}");
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly), $"seed {seed}");
                // closed, manifold, no degenerate triangle, consistently oriented, not inside out (the exact
                // self-intersection test in SameResultUnderSimilarity, for the time)
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m, false));
                // the remesh still does its work: the edges are near the target (splits above 4/3, collapses below 4/5)
                double mean = m.EdgeIndices().Average(e => m.GetVertex(m.GetEdgeV(e).a).Distance(m.GetVertex(m.GetEdgeV(e).b)));
                Assert.InRange(mean, 0.8 * 0.8 * H, 4.0 / 3 * 0.8 * H);
            }
            Assert.True(bad.Count == 0, string.Join("; ", bad));
            Assert.InRange(inBetween.Count, 0, inputsInwardInBetween);
        }

        // RemesherPro runs the same edge operations (ProcessEdge) from its queue. On master: 6 of these 40 inputs with an
        // inward triangle after some iteration.
        [Fact]
        public void JitteredSphere_RemesherPro_NoTriangleFacesInward()
        {
            var bad = new List<string>();
            foreach (int seed in OutwardSeeds(0.3)) {
                DMesh3 m = JitteredSphere(seed, 0.3);
                Remesher r = Make(m, true, 0.2, false);
                for (int pass = 0; pass < 6; ++pass) {
                    Pass(r);
                    int n = Inward(m, Vector3d.Zero);
                    if (n > 0) bad.Add($"seed {seed} pass {pass}: {n}");
                }
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m, false));
            }
            Assert.True(bad.Count == 0, string.Join("; ", bad));
        }

        static ulong Digest(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            void Add(ulong x) { for (int i = 0; i < 8; ++i) { h ^= (x >> (8 * i)) & 0xff; h *= 1099511628211UL; } }
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Add((ulong)v); Add((ulong)BitConverter.DoubleToInt64Bits(p.x)); Add((ulong)BitConverter.DoubleToInt64Bits(p.y)); Add((ulong)BitConverter.DoubleToInt64Bits(p.z));
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                Add((ulong)t); Add((ulong)tri.a); Add((ulong)tri.b); Add((ulong)tri.c);
            }
            return h;
        }

        // Without PreventNormalFlips and MinTriangleQuality the ring check does not run: the results are those of
        // master 3c4d93ac, bit for bit (vertex ids and coordinates, triangle ids and corners; only +, -, *, / and sqrt
        // on the way, so the digest does not depend on the platform). The inward triangles of the opt-out path stay.
        [Theory]
        [InlineData(false, 1, 0.3, 704, 0x65C99E9357AAE2FAUL)]
        [InlineData(false, 7, 0.45, 686, 0x04E8AFE4D21F247BUL)]
        [InlineData(true, 1, 0.3, 688, 0x58BDFC62D5862D58UL)]
        public void OptOut_ResultIsUnchanged(bool pro, int seed, double jit, int triangles, ulong digest)
        {
            DMesh3 m = JitteredSphere(seed, jit);
            Remesher r = Make(m, pro, 0, false);
            for (int pass = 0; pass < 6; ++pass)
                Pass(r);
            Assert.Equal((triangles, digest.ToString("X16")), (m.TriangleCount, Digest(m).ToString("X16")));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (int seed in new[] { 1, 2, 3 }) {       // (inward triangles on master in all three)
                DMesh3 m0 = JitteredSphere(seed, 0.3);
                DMesh3 m1 = Invariance.Transformed(m0, s);
                Remesher r0 = Make(m0, false, 0.2, false);
                var target = new SphereTarget { Center = s.Apply(Vector3d.Zero), Radius = s.Scale };
                Remesher r1 = Make(m1, false, 0.2, false, target: target, scale: s.Scale);
                for (int pass = 0; pass < 6; ++pass) {
                    r0.BasicRemeshPass();
                    r1.BasicRemeshPass();
                    Assert.True(Inward(m1, target.Center, target.Radius) == 0, $"{s.Name} seed {seed} pass {pass}");
                }
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m1));
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m1, s)));
            }
        }

        // The check keeps no state between passes: a remesher made anew for every pass gives the result of a single one.
        // (Its scratch state between two operations: RemesherRingTurnCheckTests.Check_SameAnswerFromAFreshInstance.)
        [Fact]
        public void Reuse_SameResultAsFreshInstancePerPass()
        {
            DMesh3 once = JitteredSphere(1, 0.3), perPass = new DMesh3(once);
            Remesher r = Make(once, false, 0.2, false);
            for (int pass = 0; pass < 6; ++pass) {
                r.BasicRemeshPass();
                Make(perPass, false, 0.2, false).BasicRemeshPass();
            }
            Assert.Equal("", Invariance.Difference(once, perPass));
            Assert.Equal(0, Inward(once, Vector3d.Zero));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            foreach (int seed in new[] { 1, 2 }) {
                DMesh3 serial = JitteredSphere(seed, 0.3);
                Remesher rs = Make(serial, pro, 0.2, false, parallel: false);
                for (int pass = 0; pass < 6; ++pass)
                    Pass(rs);
                Assert.Equal(0, Inward(serial, Vector3d.Zero));
                for (int k = 0; k < 3; ++k) {
                    DMesh3 parallel = JitteredSphere(seed, 0.3);
                    Remesher rp = Make(parallel, pro, 0.2, false, parallel: true);
                    for (int pass = 0; pass < 6; ++pass)
                        Pass(rp);
                    Assert.Equal("", Invariance.Difference(serial, parallel));
                }
            }
        }
    }
}
