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
            Assert.Equal(r.MaxTriangleID, imprint.SourceTriangle.Length);
            for (int pid = 0; pid < r.MaxTriangleID; ++pid)
                if (r.IsTriangle(pid) == false) Assert.Equal(-1, imprint.SourceTriangle[pid]);
            foreach (int pid in r.TriangleIndices()) {
                int src = imprint.SourceTriangle[pid];
                Assert.True(src >= 0 && target.IsTriangle(src));
                // the piece lies on its source (up to the rounding)
                Index3i st = target.GetTriangle(src), pt = r.GetTriangle(pid);
                Vector3d c = (r.GetVertex(pt.a) + r.GetVertex(pt.b) + r.GetVertex(pt.c)) / 3;
                double d = new DistPoint3Triangle3(c, new Triangle3d(target.GetVertex(st.a), target.GetVertex(st.b), target.GetVertex(st.c))).Get();
                Assert.True(d < 1e-6 * target.CachedBounds.DiagonalLength, "the source of a piece is not where it is: " + d);
            }
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

        // ---- topology cases ----

        static DMesh3 Grid(int n, double size, double z = 0)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i * size / n - size / 2, j * size / n - size / 2, z));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
            return m;
        }

        static List<Vector3d> BoundaryPositions(DMesh3 m)
        {
            var set = new HashSet<Vector3d>();
            foreach (int eid in m.BoundaryEdgeIndices()) { Index2i ev = m.GetEdgeV(eid); set.Add(m.GetVertex(ev.a)); set.Add(m.GetVertex(ev.b)); }
            return set.OrderBy(v => v.x).ThenBy(v => v.y).ThenBy(v => v.z).ToList();
        }

        [Fact]
        public void OpenTarget_BoundaryLoopsKept_CutterOpenToo()
        {
            DMesh3 target = Grid(6, 4);
            // an open cutter: a vertical strip (two triangles) crossing the plate, and a self-intersecting pair of them
            var cutter = new DMesh3();
            foreach (var p in new[] { new Vector3d(-1.3, -2.7, -1), new Vector3d(1.7, -2.2, -1), new Vector3d(1.7, 2.9, 1), new Vector3d(-1.3, 2.4, 1) }) cutter.AppendVertex(p);
            cutter.AppendTriangle(0, 1, 2); cutter.AppendTriangle(0, 2, 3);
            var crossing = new DMesh3(cutter);
            MeshTransforms.Rotate(crossing, Vector3d.Zero, new Quaterniond(Vector3d.AxisZ, 40));
            MeshEditor.Append(cutter, crossing);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Result.TriangleCount > target.TriangleCount);
            Assert.Equal(BoundaryPositions(target), BoundaryPositions(imprint.Result));
            Assert.Equal(target.BoundaryEdgeIndices().Count(), imprint.Result.BoundaryEdgeIndices().Count() - SplitBoundaryEdges(target, imprint));
            AssertKeepsTarget(target, imprint);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(imprint.Result));
            Assert.NotEmpty(imprint.CurveEdges);
        }

        // boundary edges of the target that a curve ends on are split: the result has that many more
        static int SplitBoundaryEdges(DMesh3 target, ExactMeshImprint imprint)
        {
            int extra = 0;
            foreach (int eid in target.BoundaryEdgeIndices()) {
                Index2i ev = target.GetEdgeV(eid);
                Vector3d a = target.GetVertex(ev.a), b = target.GetVertex(ev.b);
                int onEdge = imprint.Result.VertexIndices().Count(v => v >= target.MaxVertexID && new Segment3d(a, b).DistanceSquared(imprint.Result.GetVertex(v)) < 1e-20);
                extra += onEdge;
            }
            return extra;
        }

        [Fact]
        public void UnweldedSeam_StaysASeam()
        {
            // two triangles side by side with duplicated vertices along their common edge (a seam), crossed by a cutter
            var target = new DMesh3();
            int a0 = target.AppendVertex(new Vector3d(0, 0, 0)), b0 = target.AppendVertex(new Vector3d(2, 0, 0)), c0 = target.AppendVertex(new Vector3d(0, 2, 0));
            int a1 = target.AppendVertex(new Vector3d(2, 0, 0)), b1 = target.AppendVertex(new Vector3d(0, 2, 0)), c1 = target.AppendVertex(new Vector3d(2, 2, 0));
            target.AppendTriangle(a0, b0, c0); target.AppendTriangle(a1, c1, b1);
            var cutter = Box(new Vector3d(1, 1, 0), new Vector3d(0.3, 0.3, 1), Matrix3d.Identity);
            var imprint = Run(target, cutter);
            // the seam is still two boundary edges at every point: the mesh stays two sheets that touch
            Assert.True(imprint.Result.TriangleCount > 2);
            int seamVertices = imprint.Result.VertexIndices().Count(v => Math.Abs(imprint.Result.GetVertex(v).x + imprint.Result.GetVertex(v).y - 2) < 1e-12);
            // each new vertex on the seam exists twice (once per sheet): the number is even
            Assert.Equal(0, seamVertices % 2);
            // no edge is shared across the seam: every edge between pieces of different source triangles is... none (the sources are the two sheets)
            foreach (int eid in imprint.Result.EdgeIndices()) {
                Index2i et = imprint.Result.GetEdgeT(eid);
                if (et.b < 0) continue;
                Assert.Equal(imprint.SourceTriangle[et.a], imprint.SourceTriangle[et.b]);
            }
            Assert.True(imprint.Result.BoundaryEdgeIndices().Count() >= 4);
            AssertKeepsTarget(target, imprint);
        }

        [Fact]
        public void TriangleGroups_AreCopiedToThePieces()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            target.EnableTriangleGroups();
            foreach (int t in target.TriangleIndices()) target.SetTriangleGroup(t, 100 + t % 7);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Result.HasTriangleGroups);
            foreach (int p in imprint.Result.TriangleIndices())
                Assert.Equal(target.GetTriangleGroup(imprint.SourceTriangle[p]), imprint.Result.GetTriangleGroup(p));
        }

        [Fact]
        public void DegenerateTargetTriangle_NextToACutEdge_IsKeptAndTheCrackIsCounted()
        {
            // a flat target: t1 = (u, v, x) and t2 = (v, u, y) share the edge u-v; t2 is split at w on that edge and the gap
            // closed by the degenerate triangle (v, u, w). The cutter crosses the edge: the degenerate triangle is kept, not split
            // (the crack is the documented limit), and the result is still a valid mesh with the target's vertices
            var target = new DMesh3();
            int u = target.AppendVertex(new Vector3d(0, 0, 0)), v = target.AppendVertex(new Vector3d(4, 0, 0)), x = target.AppendVertex(new Vector3d(1, 2, 0));
            int y = target.AppendVertex(new Vector3d(3, -2, 0)), w = target.AppendVertex(new Vector3d(2, 0, 0));
            int t1 = target.AppendTriangle(u, v, x), t2a = target.AppendTriangle(v, w, y), t2b = target.AppendTriangle(w, u, y), d = target.AppendTriangle(v, u, w);
            Assert.True(t1 >= 0 && t2a >= 0 && t2b >= 0 && d >= 0);
            Assert.Equal(0, target.BoundaryEdgeIndices().Count(e => target.GetEdgeV(e).a == u && target.GetEdgeV(e).b == v));
            var cutter = Box(new Vector3d(1, 0.2, 0), new Vector3d(0.3, 0.5, 1), Matrix3d.Identity);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Counts["unsplit_degenerate_points"] > 0);
            Assert.True(imprint.Result.IsTriangle(d), "the degenerate triangle is kept");
            Assert.Equal(target.GetTriangle(d), imprint.Result.GetTriangle(d));
            AssertKeepsTarget(target, imprint);
        }

        [Fact]
        public void ContactAndCoplanar_ArePointsAndOutlines()
        {
            // a cutter vertex on a target face (contact), and a cutter face coplanar with it: both imprinted, exactly
            var target = Grid(2, 4);
            var cutter = new DMesh3();
            cutter.AppendVertex(new Vector3d(0.3, 0.3, 0)); cutter.AppendVertex(new Vector3d(2.5, 0.5, 1)); cutter.AppendVertex(new Vector3d(0.5, 2.5, 1));
            cutter.AppendTriangle(0, 1, 2);
            var touch = Run(target, cutter);
            Assert.True(touch.Result.VertexCount > target.VertexCount, "the touching vertex is a vertex of the target");
            Assert.Contains(touch.Result.VertexIndices(), v => touch.Result.GetVertex(v) == new Vector3d(0.3, 0.3, 0));
            AssertKeepsTarget(target, touch);
            var flat = new DMesh3();
            flat.AppendVertex(new Vector3d(-0.7, -0.6, 0)); flat.AppendVertex(new Vector3d(1.3, -0.9, 0)); flat.AppendVertex(new Vector3d(0.2, 1.1, 0));
            flat.AppendTriangle(0, 1, 2);
            var coplanar = Run(target, flat);
            Assert.True(coplanar.Result.TriangleCount > target.TriangleCount);
            // the outline of the flat triangle is on edges of the result
            Assert.True(coplanar.CurveEdges.Count >= 3);
            AssertKeepsTarget(target, coplanar);
        }

        [Fact]
        public void TargetTJunction_StaysATJunction_ResultIsTheTargetsTopology()
        {
            // a vertex of the target on the interior of a neighbour's edge (a crack-free T): the cutter cuts both; no new crack opens
            var target = new DMesh3();
            int a = target.AppendVertex(new Vector3d(0, 0, 0)), b = target.AppendVertex(new Vector3d(4, 0, 0)), c = target.AppendVertex(new Vector3d(0, 4, 0));
            int m = target.AppendVertex(new Vector3d(2, 0, 0)), d = target.AppendVertex(new Vector3d(2, -3, 0));
            target.AppendTriangle(a, b, c);
            target.AppendTriangle(m, d, b);          // touches the long edge a-b at m (a T-junction)
            var cutter = Box(new Vector3d(1, 0.6, 0), new Vector3d(0.4, 0.4, 1), Matrix3d.Identity);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Result.TriangleCount > 2);
            AssertKeepsTarget(target, imprint);
        }

        [Fact]
        public void TargetWithASelfIntersection_IsNotRepaired_AndTheDiagnosisCountsIt()
        {
            // two crossing quads as the target, a cutter elsewhere: the target's crossing stays, counted on request
            var target = Grid(2, 4);
            var cross = Grid(2, 4);
            MeshTransforms.Rotate(cross, Vector3d.Zero, new Quaterniond(Vector3d.AxisX, 90));
            MeshEditor.Append(target, cross);
            int before = MeshRepairMetrics.SelfIntersections(target);
            Assert.True(before > 0);
            var cutter = Box(new Vector3d(1.2, 0.3, 0.2), new Vector3d(0.3, 0.3, 0.3), Matrix3d.Identity);
            var imprint = Run(target, cutter, i => i.CheckTargetSelfIntersections = true);
            Assert.Equal(before, imprint.Counts["target_self_intersections"]);
            AssertKeepsTarget(target, imprint);
            Assert.True(imprint.Result.TriangleCount > target.TriangleCount);
        }

        [Fact]
        public void BudgetCancellationAndStrictRounding()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            var budget = new ExactMeshImprint() { Target = target, Cutter = cutter, MaxPieces = 5 };
            Assert.False(budget.Compute());
            Assert.Equal(ExactMeshImprint.ImprintStatus.BudgetExceeded, budget.Status);
            Assert.Null(budget.Result);
            var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            var cancelled = new ExactMeshImprint() { Target = target, Cutter = cutter, Cancellation = cts.Token };
            Assert.False(cancelled.Compute());
            Assert.Equal(ExactMeshImprint.ImprintStatus.Cancelled, cancelled.Status);
        }

        [Fact]
        public void ApplyTo_ReplacesTheTarget_AndLabelCurvesNumbersThem()
        {
            DMesh3 target, cutter;
            Pair(Similarity.Identity, out target, out cutter);
            var imprint = Run(target, cutter, i => i.LabelCurves = true);
            Assert.Equal(imprint.CurveEdges.Count, imprint.CurveId.Count);
            Assert.True(imprint.CurveId.Values.Distinct().Count() >= 1);
            var copy = new DMesh3(target);
            Assert.True(imprint.ApplyTo(copy));
            Assert.True(Same(copy, imprint.Result));
            Assert.Null(ExactMeshImprint.Imprint(null, cutter));
            Assert.NotNull(ExactMeshImprint.Imprint(target, cutter));
        }

        // ---- the failures of MeshMeshCut (docs/ROBUSTNESS_SWEEP.md R6, issue 856): random closed cuts of a cube, at every scale ----

        static DMesh3 RandomCutter(Random rnd, Similarity s)
        {
            Vector3d c = new Vector3d(rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 1.6 - 0.8);
            Vector3d h = new Vector3d(0.2 + rnd.NextDouble() * 0.6, 0.2 + rnd.NextDouble() * 0.6, 0.2 + rnd.NextDouble() * 0.6);
            Matrix3d r = Matrix3d.AxisAngleD(Vector3d.AxisZ, rnd.NextDouble() * 90) * Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 90);
            return Box(s.Apply(c), s.ApplyLength(1) * h, s.Rotation * r);
        }

        [Fact]
        public void RandomClosedCutsOfACube_AreCleanAndScaleInvariant()
        {
            var rnd = new Random(856);
            var scalings = new[] { Similarity.Identity, Similarity.Pow2Small, Similarity.Pow2Large, Similarity.Millimetre,
                new Similarity("2^-24", Math.Pow(2, -24), Matrix3d.Identity, Vector3d.Zero), Similarity.RotatedUtmMillimetre };
            for (int seed = 0; seed < 40; ++seed) {
                var local = new Random(rnd.Next());
                int state = local.Next();
                long pieces = -1;
                foreach (var s in scalings) {
                    var one = new Random(state);
                    DMesh3 target = Box(s.Apply(Vector3d.Zero), s.ApplyLength(1) * Vector3d.One, s.Rotation);
                    DMesh3 cutter = RandomCutter(one, s);
                    var imprint = Run(target, cutter);
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(imprint.Result, true));
                    Assert.True(imprint.Result.IsClosed(), "seed " + seed + " " + s.Name);
                    if (pieces < 0) pieces = imprint.Counts["pieces"];
                    Assert.True(imprint.Counts["pieces"] == pieces, "seed " + seed + " " + s.Name + ": " + imprint.Counts["pieces"] + " vs " + pieces);
                }
            }
        }

        [Fact]
        public void PlaneThroughATargetVertex_AndACutterVertexOnAFace_AreExact()
        {
            // the contact class that RefuseContact refuses: exact here, at every scaling
            foreach (var s in Similarity.All().Where(x => x.IsRotated == false)) {
                DMesh3 target = Box(s.Apply(Vector3d.Zero), s.ApplyLength(1) * Vector3d.One, Matrix3d.Identity);
                var cutter = new DMesh3();
                // a tetrahedron with a vertex on the face x = 1 of the cube and the rest outside
                cutter.AppendVertex(s.Apply(new Vector3d(1, 0.2, 0.3)));
                cutter.AppendVertex(s.Apply(new Vector3d(2, 0.5, 0.1)));
                cutter.AppendVertex(s.Apply(new Vector3d(2, -0.3, 0.6)));
                cutter.AppendVertex(s.Apply(new Vector3d(2.3, 0.1, -0.4)));
                foreach (var t in new[] { (0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3) }) cutter.AppendTriangle(t.Item1, t.Item2, t.Item3);
                var imprint = Run(target, cutter);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(imprint.Result, true));
                Assert.Equal(target.VertexCount + 1, imprint.Result.VertexCount);
            }
        }

        [Fact]
        public void SourceTriangle_AfterRoundingEdits_StaysRight()
        {
            // a cutter edge that passes 1e-12 from a vertex of the target: needles that the repair contracts (edits reuse and free triangle ids)
            var target = Grid(4, 4);
            var cutter = new DMesh3();
            cutter.AppendVertex(new Vector3d(-0.5, 0.5, -1)); cutter.AppendVertex(new Vector3d(0.5 + 1e-12, 0.5, 1)); cutter.AppendVertex(new Vector3d(0.5, -1.5, 0.3));
            cutter.AppendTriangle(0, 1, 2);
            var imprint = Run(target, cutter);
            AssertKeepsTarget(target, imprint);
            Assert.True(imprint.Counts["rounding_edits"] > 0, "the case must make the repair edit");
        }

        [Fact]
        public void CurveAlongATargetEdge_IsACurveEdgeWithoutASplit()
        {
            // a cutter triangle that meets the grid along one of its edges (the edge from (-1, 0, 0) to (0, 0, 0)), nothing else
            var target = Grid(4, 4);
            var cutter = new DMesh3();
            cutter.AppendVertex(new Vector3d(-1, 0, 0)); cutter.AppendVertex(new Vector3d(0, 0, 0)); cutter.AppendVertex(new Vector3d(-0.5, 0, 1));
            cutter.AppendTriangle(0, 1, 2);
            var imprint = Run(target, cutter, i => i.LabelCurves = true);
            int a = target.VertexIndices().First(v => target.GetVertex(v) == new Vector3d(-1, 0, 0)), b = target.VertexIndices().First(v => target.GetVertex(v) == new Vector3d(0, 0, 0));
            Assert.Contains(ExactMeshArrangement.EdgeKey(a, b), imprint.CurveEdges);
            Assert.Equal(imprint.CurveEdges.Count, imprint.CurveId.Count);
            Assert.Equal(target.TriangleCount, imprint.Result.TriangleCount);
        }

        [Fact]
        public void TargetVertexOnTheEdgeOfASplitTriangle_IsReused_NoDuplicateVertex()
        {
            // vertex m of one triangle lies on the long edge a-b of another; the cutter crosses the long triangle: the piece corner at m is m itself
            var target = new DMesh3();
            int a = target.AppendVertex(new Vector3d(0, 0, 0)), b = target.AppendVertex(new Vector3d(4, 0, 0)), c = target.AppendVertex(new Vector3d(0, 4, 0));
            int m = target.AppendVertex(new Vector3d(2, 0, 0)), d = target.AppendVertex(new Vector3d(2, -3, 0));
            target.AppendTriangle(a, b, c);
            target.AppendTriangle(m, d, b);
            // a cutter that passes exactly through m and across the first triangle
            var cutter = new DMesh3();
            cutter.AppendVertex(new Vector3d(2, 0, -1)); cutter.AppendVertex(new Vector3d(2, 0, 1)); cutter.AppendVertex(new Vector3d(1, 3, 0));
            cutter.AppendTriangle(0, 1, 2);
            var imprint = Run(target, cutter);
            Assert.True(imprint.Counts["target_vertex_points"] > 0);
            var positions = imprint.Result.VertexIndices().Select(v => imprint.Result.GetVertex(v)).ToList();
            Assert.Equal(positions.Count, positions.Distinct().Count());
        }

        [Fact]
        public void TargetWithHolesInItsIds_KeepsIdsAndSources()
        {
            var target = Grid(5, 4);
            // remove a few triangles and their isolated vertices so that ids have holes, then cut
            foreach (int t in new[] { 3, 17, 22 }) target.RemoveTriangle(t, true);
            Assert.True(target.MaxTriangleID > target.TriangleCount);
            var cutter = Box(new Vector3d(0.1, 0.2, 0), new Vector3d(1.1, 0.9, 1), Rot());
            var imprint = Run(target, cutter);
            AssertKeepsTarget(target, imprint);
            Assert.Equal(BoundaryPositions(target).Count <= BoundaryPositions(imprint.Result).Count, true);
        }
    }
}