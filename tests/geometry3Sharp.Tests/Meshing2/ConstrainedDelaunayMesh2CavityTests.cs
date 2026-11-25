using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Verifies that the linear-time cavity earcut recovery path (Livesu et al. 2022) produces the exact same
    /// constrained Delaunay triangulation as Sloan's flip recovery on generic and degenerate cavities (#1401).
    /// </summary>
    public class ConstrainedDelaunayMesh2CavityTests
    {
        static ConstrainedTriangulation2Result ComputeWithThreshold(ConstrainedMesh2Input input, int threshold)
        {
            DelaunayKernel2.CavityEarcutThresholdForTests = threshold;
            try { return new ConstrainedDelaunayMesh2().Compute(input); }
            finally { DelaunayKernel2.CavityEarcutThresholdForTests = 0; }
        }

        static (HashSet<string> tris, HashSet<string> subs) CanonicalGeometry(ConstrainedMesh2 m)
        {
            string PtKey(int i) =>
                BitConverter.DoubleToInt64Bits(m.Vertices[i].x) + "," + BitConverter.DoubleToInt64Bits(m.Vertices[i].y);
            var tris = new HashSet<string>();
            foreach (var t in m.Triangles) {
                string[] ks = { PtKey(t.a), PtKey(t.b), PtKey(t.c) };
                Array.Sort(ks, StringComparer.Ordinal);
                tris.Add(ks[0] + "|" + ks[1] + "|" + ks[2]);
            }
            var subs = new HashSet<string>();
            foreach (var s in m.Subsegments) {
                string ka = PtKey(s.A), kb = PtKey(s.B);
                if (string.CompareOrdinal(ka, kb) > 0) (ka, kb) = (kb, ka);
                subs.Add(ka + "|" + kb + "|" + s.Marker);
            }
            return (tris, subs);
        }

        [Fact]
        public void ForcedCavityEarcut_MatchesForcedSloan_OnBundleAndDeepReflexCavity()
        {
            var inputs = new[] {
                ConstrainedDelaunayMesh2BundleTests.Bundle(80),
                ConstrainedDelaunayMesh2BundleTests.DeepReflexCavityWithHangingSpurs(100)
            };
            foreach (var input in inputs) {
                var earcut = ComputeWithThreshold(input, -1);         // force cavity earcut for all k >= 1
                var sloan = ComputeWithThreshold(input, int.MaxValue); // force half-edge Sloan for all k
                Assert.Equal(Triangulation2Status.Success, earcut.Status);
                Assert.Equal(Triangulation2Status.Success, sloan.Status);
                var (et, es) = CanonicalGeometry(earcut.Mesh);
                var (st, ss) = CanonicalGeometry(sloan.Mesh);
                Assert.Equal(st, et);
                Assert.Equal(ss, es);
            }
        }

        static List<int> WalkCrossedHalfEdges(DelaunayKernel2 k, int u, int w)
        {
            // Find the triangle incident to u whose opposite edge crosses u -> w, then walk to w.
            int e = -1;
            foreach (int h in k.Star(u)) {
                int t = h / 3;
                if (k.IsGhost(t)) continue;
                int b = k.V[3 * t + ((h % 3) + 1) % 3];
                int c = k.V[3 * t + ((h % 3) + 2) % 3];
                if (k.OrientV(u, b, w) > 0 && k.OrientV(u, c, w) < 0) {
                    e = 3 * t + ((h % 3) + 1) % 3; // b -> c (right -> left across u -> w)
                    break;
                }
            }
            Assert.True(e >= 0, "Expected u -> w to cross an initial triangle opposite u");
            var crossed = new List<int> { e };
            while (true) {
                int f = k.H[e];
                int t = f / 3;
                Assert.False(k.IsGhost(t), "Walk left the triangulation before reaching w");
                int d = k.V[3 * t + ((f % 3) + 2) % 3];
                if (d == w) break;
                int o = k.OrientV(u, w, d);
                Assert.NotEqual(0, o);
                // If d is to the right of u -> w (o < 0), next crossed edge is d -> left = Prev(f); else right -> d = Next(f).
                e = o < 0 ? 3 * t + ((f % 3) + 2) % 3 : 3 * t + ((f % 3) + 1) % 3;
                crossed.Add(e);
            }
            return crossed;
        }

        static (DelaunayKernel2 kernel, int u, int w) BuildKernel(ConstrainedMesh2Input input, int uInput, int wInput)
        {
            int n = input.Points.Count;
            var x = new double[n]; var y = new double[n];
            for (int i = 0; i < n; ++i) { x[i] = input.Points[i].x; y[i] = input.Points[i].y; }
            var order = DelaunayKernel2.InsertionOrder(x, y);
            var rank = new int[n];
            var xs = new double[n]; var ys = new double[n];
            for (int r = 0; r < n; ++r) { xs[r] = x[order[r]]; ys[r] = y[order[r]]; rank[order[r]] = r; }
            var k = new DelaunayKernel2(xs, ys, order);
            Assert.True(k.Build(System.Threading.CancellationToken.None));
            k.BeginConstraints();
            return (k, k.Vertex[rank[uInput]], k.Vertex[rank[wInput]]);
        }

        static void AssertAllRealTrianglesStrictlyPositive(DelaunayKernel2 k)
        {
            int realCount = 0;
            for (int t = 0; t < k.TriangleSlots; ++t) {
                if (k.IsGhost(t)) continue;
                ++realCount;
                Assert.True(k.OrientV(k.V[3 * t], k.V[3 * t + 1], k.V[3 * t + 2]) > 0,
                    "Triangle " + t + " is degenerate or inverted");
            }
            Assert.True(realCount > 0);
        }

        [Fact]
        public void RecoverEdgeCavity_RestoresLocalDelaunayImmediately_WithoutFinalPass()
        {
            // Deep cavity with > 80 crossed edges (naturally hits RecoverEdgeCavity with k > 64).
            // Tests DelaunayKernel2.RecoverEdge directly so ConstrainedDelaunayMesh2's final NonDelaunayEdges pass
            // cannot mask a missing Lawson restore of the earcut diagonals.
            var input = ConstrainedDelaunayMesh2BundleTests.DeepReflexCavityWithHangingSpurs(50);
            var (k, u, w) = BuildKernel(input, 0, 1);
            var crossed = WalkCrossedHalfEdges(k, u, w);
            Assert.True(crossed.Count > 64, "Expected k > 64 crossed edges to exercise RecoverEdgeCavity, got " + crossed.Count);

            int he = k.RecoverEdge(u, w, crossed);
            Assert.True(he >= 0);
            Assert.Equal(u, k.V[he]);
            Assert.Equal(w, k.V[3 * (he / 3) + ((he % 3) + 1) % 3]);
            k.SetConstraint(he, 0);
            // Immediately after RecoverEdge, every unconstrained edge in the cavity must already be locally Delaunay.
            Assert.Empty(k.NonDelaunayEdges());
            AssertAllRealTrianglesStrictlyPositive(k);
        }

        [Fact]
        public void RecoverEdgeCavity_CollinearChainVertices_ProducesPositiveTrianglesRestoresDelaunayAndMatchesSloan()
        {
            // Cavity with k >= 85 crossed edges where both upper (y = +0.5) and lower (y = -0.5) chains have shallow
            // reflex end ramps, long runs of exact collinear vertices, and a shallow central peak at x = 22 (y = +-0.6).
            // The shallow profile keeps every vertex on the Delaunay cavity boundary of u-w; cutting the central peak
            // makes both ia (x = 23) and ic (x = 21) exact collinear (Orient == 0) far from the end waves, killing
            // > 0 -> >= 0 mutants on initial enqueue, ia re-enqueue, and ic re-enqueue.
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.0));    // 0: u
            input.Points.Add(new Vector2d(46.0, 0.0));   // 1: w
            static double ProfileY(int i)
            {
                if (i == 1 || i == 45) return 0.05;
                if (i == 2 || i == 44) return 0.12;
                if (i == 3 || i == 43) return 0.22;
                if (i == 4 || i == 42) return 0.35;
                if (i == 22) return 0.6;
                return 0.5;
            }
            for (int i = 1; i <= 45; ++i)
                input.Points.Add(new Vector2d(i, ProfileY(i)));
            for (int i = 1; i <= 45; ++i)
                input.Points.Add(new Vector2d(i + 0.25, -ProfileY(i)));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));

            // 1. Direct kernel check: RecoverEdgeCavity with k > 64 leaves zero non-Delaunay edges and strictly positive triangles.
            var (k, u, w) = BuildKernel(input, 0, 1);
            var crossed = WalkCrossedHalfEdges(k, u, w);
            Assert.True(crossed.Count >= 85, "Expected all ramp/plateau/peak vertices in cavity (k >= 85), got " + crossed.Count);
            int he = k.RecoverEdge(u, w, crossed);
            Assert.True(he >= 0);
            k.SetConstraint(he, 0);
            Assert.Empty(k.NonDelaunayEdges());
            AssertAllRealTrianglesStrictlyPositive(k);

            // 2. Raw earcut check (before Lawson flips): every ear cut by TriangulateHalfCavity must be strictly positive
            // (kills > 0 -> >= 0 mutations on initial enqueue and ia/ic re-enqueue, which Lawson flips could otherwise unwind).
            var (kRaw, uRaw, wRaw) = BuildKernel(input, 0, 1);
            var crossedRaw = WalkCrossedHalfEdges(kRaw, uRaw, wRaw);
            DelaunayKernel2.SkipEarcutRestoreForTests = true;
            try {
                int heRaw = kRaw.RecoverEdge(uRaw, wRaw, crossedRaw);
                Assert.True(heRaw >= 0);
                AssertAllRealTrianglesStrictlyPositive(kRaw);
            } finally {
                DelaunayKernel2.SkipEarcutRestoreForTests = false;
            }

            // 3. Full ConstrainedDelaunayMesh2: earcut matches forced Sloan and passes Oracle M2.
            var earcut = ComputeWithThreshold(input, -1);
            var sloan = ComputeWithThreshold(input, int.MaxValue);
            Assert.Equal(Triangulation2Status.Success, earcut.Status);
            Assert.Equal(Triangulation2Status.Success, sloan.Status);
            var (et, es) = CanonicalGeometry(earcut.Mesh);
            var (st, ss) = CanonicalGeometry(sloan.Mesh);
            Assert.Equal(st, et);
            Assert.Equal(ss, es);

            var pts = new double[2 * input.Points.Count];
            for (int i = 0; i < input.Points.Count; ++i) { pts[2 * i] = input.Points[i].x; pts[2 * i + 1] = input.Points[i].y; }
            var mi = Generators.Literal(pts);
            mi.Segments.Add(new SegmentInput(0, 1, 1, false));
            var report = Oracle.Check(mi, new ConstrainedDelaunayMesh2Candidate().Run(mi), new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void RecoverEdgeCavity_TrueHangingSpurInsideCavity_MatchesSloanAndRestoresDelaunay()
        {
            // Construct a constrained spur root=(5,3) -- tip=(5,1) whose BOTH incident Delaunay triangles
            // (root, botL, tip) and (tip, botR, root) cross u=(0,0) -- w=(10,0), so the spur lies strictly inside
            // the upper half-cavity and exercises internalMates matching and hanging-edge collinearity (Orient(root, tip, root) == 0).
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.0));  // 0: u
            input.Points.Add(new Vector2d(10.0, 0.0)); // 1: w
            input.Points.Add(new Vector2d(5.0, 3.0));  // 2: spur root
            input.Points.Add(new Vector2d(5.0, 1.0));  // 3: spur tip
            input.Points.Add(new Vector2d(2.0, -2.0)); // 4: botL
            input.Points.Add(new Vector2d(8.0, -2.0)); // 5: botR
            input.Segments.Add(new Mesh2Segment(2, 3, 2)); // hanging spur inserted first
            input.Segments.Add(new Mesh2Segment(0, 1, 1)); // diagonal crossing all triangles incident to the spur

            var earcut = ComputeWithThreshold(input, -1);
            var sloan = ComputeWithThreshold(input, int.MaxValue);
            Assert.Equal(Triangulation2Status.Success, earcut.Status);
            Assert.Equal(Triangulation2Status.Success, sloan.Status);
            var (et, es) = CanonicalGeometry(earcut.Mesh);
            var (st, ss) = CanonicalGeometry(sloan.Mesh);
            Assert.Equal(st, et);
            Assert.Equal(ss, es);

            var pts = new double[2 * input.Points.Count];
            for (int i = 0; i < input.Points.Count; ++i) { pts[2 * i] = input.Points[i].x; pts[2 * i + 1] = input.Points[i].y; }
            var mi = Generators.Literal(pts);
            foreach (var s in input.Segments) mi.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            var report = Oracle.Check(mi, new ConstrainedDelaunayMesh2Candidate().Run(mi), new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void ForcedCavityEarcut_PowerOfTwoScaleInvariance_IsBitIdenticalToIdentity()
        {
            double[] scales = { Math.Pow(2.0, -10), Math.Pow(2.0, 10) };
            foreach (var raw in new[] { ConstrainedDelaunayMesh2BundleTests.Bundle(40, spacing: 1e-2), ConstrainedDelaunayMesh2BundleTests.DeepReflexCavityWithHangingSpurs(80) }) {
                var idRes = ComputeWithThreshold(raw, -1);
                Assert.Equal(Triangulation2Status.Success, idRes.Status);
                foreach (double sc in scales) {
                    var scaled = new ConstrainedMesh2Input();
                    foreach (var p in raw.Points) scaled.Points.Add(new Vector2d(sc * p.x, sc * p.y));
                    foreach (var seg in raw.Segments) scaled.Segments.Add(seg);
                    var scRes = ComputeWithThreshold(scaled, -1);
                    Assert.Equal(Triangulation2Status.Success, scRes.Status);
                    Assert.Equal(idRes.Mesh.Vertices.Count, scRes.Mesh.Vertices.Count);
                    for (int i = 0; i < idRes.Mesh.Vertices.Count; ++i) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(sc * idRes.Mesh.Vertices[i].x), BitConverter.DoubleToInt64Bits(scRes.Mesh.Vertices[i].x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(sc * idRes.Mesh.Vertices[i].y), BitConverter.DoubleToInt64Bits(scRes.Mesh.Vertices[i].y));
                    }
                    Assert.Equal(idRes.Mesh.Triangles.Count, scRes.Mesh.Triangles.Count);
                    for (int i = 0; i < idRes.Mesh.Triangles.Count; ++i)
                        Assert.Equal(idRes.Mesh.Triangles[i], scRes.Mesh.Triangles[i]);
                    Assert.Equal(idRes.Mesh.Subsegments.Count, scRes.Mesh.Subsegments.Count);
                    for (int i = 0; i < idRes.Mesh.Subsegments.Count; ++i) {
                        Assert.Equal(idRes.Mesh.Subsegments[i].A, scRes.Mesh.Subsegments[i].A);
                        Assert.Equal(idRes.Mesh.Subsegments[i].B, scRes.Mesh.Subsegments[i].B);
                        Assert.Equal(idRes.Mesh.Subsegments[i].Marker, scRes.Mesh.Subsegments[i].Marker);
                    }
                }
            }
        }
    }
}
