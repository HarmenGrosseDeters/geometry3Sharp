using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> ExactMeshImprint (docs/design/EXACT_IMPRINT.md): the guarantees G1 to G5 on procedural pairs. </summary>
    public class ExactMeshImprintTests
    {
        internal static DMesh3 Box(Vector3d c, Vector3d h, Matrix3d rot)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, h) }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices().ToArray())
                m.SetVertex(vid, rot * m.GetVertex(vid) + c);
            return m;
        }
        internal static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }
        static Matrix3d Rot() => Matrix3d.AxisAngleD(Vector3d.AxisZ, 29) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);

        // target and cutter after the similarity (exact for the powers of two)
        internal static void Pair(Similarity s, out DMesh3 target, out DMesh3 cutter)
        {
            target = Box(s.Apply(Vector3d.Zero), s.ApplyLength(1) * Vector3d.One, s.Rotation);
            cutter = Box(s.Apply(new Vector3d(0.3, 0.2, 0.1)), s.ApplyLength(0.7) * Vector3d.One, s.Rotation * Rot());
        }

        static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter, Action<ExactMeshImprint> configure = null)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            configure?.Invoke(imprint);
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            Assert.True(imprint.Result.CheckValidity(true, FailMode.ReturnOnly));
            return imprint;
        }

        static double SignedArea(DMesh3 m, int tid, int axis)
        {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
            Vector3d n = (b - a).Cross(c - a);
            return n[axis] / 2;
        }

        // G1: the target's vertices, untouched triangles and the orientation of the pieces
        internal static void AssertKeepsTarget(DMesh3 target, ExactMeshImprint imprint)
        {
            DMesh3 r = imprint.Result;
            foreach (int vid in target.VertexIndices()) {
                Assert.True(r.IsVertex(vid));
                Assert.Equal(target.GetVertex(vid), r.GetVertex(vid));
            }
            foreach (int tid in target.TriangleIndices()) {
                bool kept = r.IsTriangle(tid) && imprint.SourceTriangle[tid] == tid && r.GetTriangle(tid) == target.GetTriangle(tid);
                if (kept) continue;
                // split: every piece of it lies on the same side (its normal turns the same way)
                Vector3d n = target.GetTriNormal(tid);
                foreach (int pid in r.TriangleIndices().Where(p => imprint.SourceTriangle[p] == tid)) {
                    Assert.True(r.GetTriNormal(pid).Dot(n) > 0.99, "a piece is turned over");
                }
            }
            foreach (int pid in r.TriangleIndices())
                Assert.True(imprint.SourceTriangle[pid] >= 0 && target.IsTriangle(imprint.SourceTriangle[pid]));
        }

        [Fact]
        public void CubeCutByTiltedCube_ClosedManifoldKeepsTheTarget()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Result.TriangleCount > target.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(imprint.Result, true));
            Assert.True(imprint.Result.IsClosed());
            AssertKeepsTarget(target, imprint);
            Assert.True(imprint.CurveEdges.Count > 8);
            Assert.True(imprint.RoundingClean);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(imprint.Result));
        }

        [Fact]
        public void Disjoint_ResultIsACopy()
        {
            var target = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity);
            var cutter = Box(new Vector3d(10, 0, 0), Vector3d.One, Matrix3d.Identity);
            var imprint = Run(target, cutter);
            Assert.Equal(target.TriangleCount, imprint.Result.TriangleCount);
            Assert.Empty(imprint.CurveEdges);
            AssertKeepsTarget(target, imprint);
        }

        [Fact]
        public void InvalidInput()
        {
            Assert.False(new ExactMeshImprint().Compute());
            var bad = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var imprint = new ExactMeshImprint() { Target = bad, Cutter = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity) };
            Assert.False(imprint.Compute());
            Assert.Equal(ExactMeshImprint.ImprintStatus.InvalidInput, imprint.Status);
            Assert.Null(imprint.Result);
        }

        // ---- invariance, reuse, determinism ----

        public static IEnumerable<object[]> Similarities() { foreach (var s in Similarity.All()) yield return new object[] { s.Name }; }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Invariance_ExactPiecesAreTheSameUnderEverySimilarity(string name)
        {
            Similarity s = Similarity.All().First(x => x.Name == name);
            DMesh3 t0, c0, t1, c1;
            Pair(Similarity.Identity, out t0, out c0);
            Pair(s, out t1, out c1);
            var a = Run(t0, c0, i => i.SafeRounding = false);
            var b = Run(t1, c1, i => i.SafeRounding = false);
            Assert.Equal(a.Counts["pieces"], b.Counts["pieces"]);
            Assert.Equal(a.Counts["new_vertices"], b.Counts["new_vertices"]);
            Assert.Equal(a.Counts["curve_edges"], b.Counts["curve_edges"]);
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            // scalings by a power of two are exact: the rounded vertices are the same bit for bit, scaled
            if (s.IsRotated == false && s.Translation == Vector3d.Zero && Math.Log(s.Scale, 2) % 1 == 0) {
                foreach (int v in a.Result.VertexIndices())
                    Assert.Equal(s.Apply(a.Result.GetVertex(v)), b.Result.GetVertex(v));
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_SameResult_AndAfterChangingTheCutter()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
            Assert.True(imprint.Compute());
            DMesh3 first = imprint.Result;
            var edges = new HashSet<long>(imprint.CurveEdges);
            Assert.True(imprint.Compute());
            Assert.True(Same(first, imprint.Result));
            Assert.Equal(edges, imprint.CurveEdges);
            imprint.Cutter = Box(new Vector3d(5, 5, 5), Vector3d.One, Matrix3d.Identity);
            Assert.True(imprint.Compute());
            Assert.Equal(target.TriangleCount, imprint.Result.TriangleCount);
            Assert.Empty(imprint.CurveEdges);
            imprint.Cutter = cutter;
            Assert.True(imprint.Compute());
            Assert.True(Same(first, imprint.Result));
        }

        static bool Same(DMesh3 a, DMesh3 b) =>
            a.MaxVertexID == b.MaxVertexID && a.MaxTriangleID == b.MaxTriangleID
            && a.VertexIndices().All(v => b.IsVertex(v) && a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => b.IsTriangle(t) && a.GetTriangle(t) == b.GetTriangle(t));

        [Fact]
        public void Determinism_SerialEqualsParallel_AndRepeatedRuns()
        {
            DMesh3 target = Sphere(Vector3d.Zero, 1, 12), cutter = Sphere(new Vector3d(0.6, 0.2, 0.1), 0.8, 12);
            var reference = Run(target, cutter);
            for (int i = 0; i < 3; ++i) {
                var again = Run(target, cutter);
                Assert.True(Same(reference.Result, again.Result));
                Assert.Equal(reference.CurveEdges, again.CurveEdges);
                Assert.Equal(reference.SourceTriangle, again.SourceTriangle);
            }
            gParallel.SerialOnThisThread = true;
            try {
                var serial = Run(target, cutter);
                Assert.True(Same(reference.Result, serial.Result));
                Assert.Equal(reference.CurveEdges, serial.CurveEdges);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // ---- the curves are the arrangement's ----

        static string Key(ExactPoint3 p) => p.ToString();

        [Fact]
        public void CurveEdges_EqualTheArrangementsIntersectionCurves()
        {
            DMesh3 target = Sphere(Vector3d.Zero, 1, 10), cutter = Box(new Vector3d(0.3, 0.2, 0.1), new Vector3d(0.7, 0.7, 0.7), Rot());
            var imprint = Run(target, cutter, i => { i.SafeRounding = false; i.ComputeExactVertices = true; });
            var fromImprint = new HashSet<string>();
            foreach (long k in imprint.CurveEdges) {
                string a = Key(imprint.ResultExactVertices[(int)(k >> 32)]), b = Key(imprint.ResultExactVertices[(int)(k & 0xFFFFFFFF)]);
                fromImprint.Add(string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a);
            }
            var arr = new ExactMeshArrangement(new List<DMesh3> { target, cutter }) { TriangulateMeshes = new[] { true, false } };
            arr.Compute();
            var fromArrangement = new HashSet<string>();
            foreach (long k in arr.ConstraintEdges) {
                string a = Key(arr.ToInputExact(arr.Exact((int)(k >> 32)))), b = Key(arr.ToInputExact(arr.Exact((int)(k & 0xFFFFFFFF))));
                fromArrangement.Add(string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a);
            }
            Assert.True(fromArrangement.Count > 20);
            Assert.Equal(fromArrangement.OrderBy(x => x), fromImprint.OrderBy(x => x));
        }

        [Fact]
        public void ProjectedAreaOfThePiecesEqualsTheSourcesExactly()
        {
            // the exact vertices: the signed area projected onto the dominant plane of the source is rational and additive
            DMesh3 target = Box(Vector3d.Zero, Vector3d.One, Matrix3d.Identity), cutter = Box(new Vector3d(0.3, 0.2, 0.1), new Vector3d(0.7, 0.7, 0.7), Rot());
            var imprint = Run(target, cutter, i => { i.SafeRounding = false; i.ComputeExactVertices = true; });
            var sums = new Dictionary<int, System.Numerics.BigInteger[]>();
            foreach (int pid in imprint.Result.TriangleIndices()) {
                int src = imprint.SourceTriangle[pid];
                Vector3d n = target.GetTriNormal(src);
                int axis = Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z) ? 0 : Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2;
                int i1 = (axis + 1) % 3, i2 = (axis + 2) % 3;
                Index3i t = imprint.Result.GetTriangle(pid);
                var v = new[] { imprint.ResultExactVertices[t.a], imprint.ResultExactVertices[t.b], imprint.ResultExactVertices[t.c] };
                // twice the area as a fraction over the product of the denominators: sum with a common denominator of all
                System.Numerics.BigInteger Det(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c)
                {
                    // (b - a) x (c - a) on coordinates i1, i2, each p = (X/W): multiply through by wa*wb*wc
                    System.Numerics.BigInteger wa = a.W, wb = b.W, wc = c.W;
                    System.Numerics.BigInteger b1 = b[i1] * wa - a[i1] * wb, b2 = b[i2] * wa - a[i2] * wb, c1 = c[i1] * wa - a[i1] * wc, c2 = c[i2] * wa - a[i2] * wc;
                    // = (wa*wb) (b-a)_1 ... : (b-a)_1 = b1 / (wa wb), (c-a)_1 = c1 / (wa wc): det = (b1 c2 - b2 c1) / (wa^2 wb wc)
                    return (b1 * c2 - b2 * c1);
                }
                // normalize to a rational (num, den) and add exactly
                var num = Det(v[0], v[1], v[2]);
                var den = v[0].W * v[0].W * v[1].W * v[2].W;
                if (sums.TryGetValue(src, out var acc) == false) { sums[src] = new[] { num, den }; continue; }
                acc[0] = acc[0] * den + num * acc[1];
                acc[1] = acc[1] * den;
            }
            foreach (var kv in sums) {
                Index3i t = target.GetTriangle(kv.Key);
                Vector3d a = target.GetVertex(t.a), b = target.GetVertex(t.b), c = target.GetVertex(t.c);
                Vector3d n = target.GetTriNormal(kv.Key);
                int axis = Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z) ? 0 : Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2;
                int i1 = (axis + 1) % 3, i2 = (axis + 2) % 3;
                double det = (b[i1] - a[i1]) * (c[i2] - a[i2]) - (b[i2] - a[i2]) * (c[i1] - a[i1]);
                // the sum equals det exactly: num / den == det (det is a double, exactly representable here: box corners)
                var d = new System.Numerics.BigInteger(det);
                Assert.Equal(d * kv.Value[1], kv.Value[0]);
            }
            Assert.True(sums.Count > 4);
        }
    }
}