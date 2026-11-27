using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The fail-fast budget and the cancellation of ExactMeshBoolean (docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 5):
    /// the predicted piece count (Euler, before the triangulation) equals the pieces, MaxPieces stops with BudgetExceeded,
    /// Cancellation with Cancelled, both without a result and with a reusable instance. (The class name keeps the mutation
    /// shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsBudget
    {
        static DMesh3 Sphere(Vector3d c, double r, int ev = 8)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
            return m;
        }

        /// <summary>
        /// the knot of the tail case (BooleanComparison knot): an open tube wound inside the unit ball, cutting through
        /// itself; segments rings of ring vertices, the path winding turns times
        /// </summary>
        internal static DMesh3 Knot(int segments, int ring, double turns, double tube, int seed)
        {
            var rand = new Random(seed);
            const int K = 4;
            var fa = new double[K]; var pa = new double[K]; var fb = new double[K]; var pb = new double[K];
            for (int k = 0; k < K; ++k) {
                fa[k] = 1 + 6 * rand.NextDouble(); pa[k] = 2 * Math.PI * rand.NextDouble();
                fb[k] = 1 + 6 * rand.NextDouble(); pb[k] = 2 * Math.PI * rand.NextDouble();
            }
            var centres = new Vector3d[segments + 1];
            for (int s = 0; s <= segments; ++s) {
                double t = (double)s / segments;
                double theta = Math.PI * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t)), phi = 2 * Math.PI * turns * t;
                for (int k = 0; k < K; ++k) {
                    theta += 0.15 * Math.Sin(2 * Math.PI * fa[k] * t + pa[k]);
                    phi += 0.4 * Math.Sin(2 * Math.PI * fb[k] * t + pb[k]);
                }
                double r = 1 + 0.1 * Math.Sin(2 * Math.PI * 5 * t);
                centres[s] = r * new Vector3d(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta));
            }
            var m = new DMesh3();
            Vector3d tan0 = (centres[1] - centres[0]).Normalized;
            Vector3d nrm = Math.Abs(tan0.z) < 0.9 ? tan0.Cross(Vector3d.AxisZ).Normalized : tan0.Cross(Vector3d.AxisX).Normalized;
            for (int s = 0; s <= segments; ++s) {
                Vector3d tan = (centres[Math.Min(s + 1, segments)] - centres[Math.Max(s - 1, 0)]).Normalized;
                nrm = (nrm - nrm.Dot(tan) * tan).Normalized;
                Vector3d bin = tan.Cross(nrm);
                for (int j = 0; j < ring; ++j) {
                    double ang = 2 * Math.PI * j / ring;
                    m.AppendVertex(centres[s] + tube * (Math.Cos(ang) * nrm + Math.Sin(ang) * bin));
                }
            }
            for (int s = 0; s < segments; ++s)
                for (int j = 0; j < ring; ++j) {
                    int a = s * ring + j, b = s * ring + (j + 1) % ring;
                    m.AppendTriangle(a, b, b + ring);
                    m.AppendTriangle(a, b + ring, a + ring);
                }
            return m;
        }

        // the knot and its pairing (rotated about its centre, shifted by a quarter of its extent)
        internal static (DMesh3, DMesh3) KnotPair(int segments, int ring, double turns, double tube, int seed)
        {
            DMesh3 a = Knot(segments, ring, turns, tube, seed), b = new DMesh3(a);
            AxisAlignedBox3d bounds = a.CachedBounds;
            MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
            return (a, b);
        }

        // cases with interior points, points on the edges of input triangles, coplanar faces, a degenerate triangle,
        // self-intersections, open inputs and culled triangles
        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "spheres", Ops.Union };
            yield return new object[] { "spheres", Ops.Difference };     // (the cull of B outside A)
            yield return new object[] { "coplanar boxes", Ops.Union };
            yield return new object[] { "touching boxes", Ops.Intersection };
            yield return new object[] { "degenerate", Ops.Union };
            yield return new object[] { "vertex on face", Ops.Union };
            yield return new object[] { "knot", Ops.Difference };
            yield return new object[] { "far", Ops.Intersection };
        }

        static (DMesh3, DMesh3) Inputs(string name)
        {
            switch (name) {
                case "spheres": return (Sphere(Vector3d.Zero, 1), Sphere(new Vector3d(0.6, 0.3, 0.2), 0.8));
                case "coplanar boxes": return (Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0.25, 0), new Vector3d(1.5, 0.75, 1)));
                case "touching boxes": return (Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(1, 0.2, 0.2), new Vector3d(2, 0.8, 0.8)));
                case "degenerate": {
                    DMesh3 a = Sphere(Vector3d.Zero, 1);
                    // (a collinear sliver crossing the other sphere)
                    int v0 = a.AppendVertex(new Vector3d(0.2, 0.2, -2)), v1 = a.AppendVertex(new Vector3d(0.2, 0.2, 0)), v2 = a.AppendVertex(new Vector3d(0.2, 0.2, 2));
                    a.AppendTriangle(v0, v1, v2);
                    return (a, Sphere(new Vector3d(0.3, 0, 0), 0.9));
                }
                case "knot": return KnotPair(80, 6, 8, 0.35, 3);
                case "vertex on face": {
                    // the corner (1, 1, 1) of the box lies exactly inside the face x + y + z = 3 of the tetrahedron: the
                    // segments of the box's triangles there end at their own corner (a point the triangulation leaves out)
                    var t = new DMesh3();
                    int p0 = t.AppendVertex(new Vector3d(3, 0, 0)), p1 = t.AppendVertex(new Vector3d(0, 3, 0)), p2 = t.AppendVertex(new Vector3d(0, 0, 3)), p3 = t.AppendVertex(new Vector3d(3, 3, 3));
                    t.AppendTriangle(p0, p2, p1); t.AppendTriangle(p0, p1, p3); t.AppendTriangle(p1, p2, p3); t.AppendTriangle(p2, p0, p3);
                    return (Box(Vector3d.Zero, Vector3d.One), t);
                }
                case "far": return (Sphere(Vector3d.Zero, 1), Sphere(new Vector3d(5, 0, 0), 1));
            }
            throw new ArgumentException(name);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, Action<ExactMeshBoolean> configure = null)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true };
            configure?.Invoke(s);
            s.Compute();
            return s;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void PredictedPieces_AreThePieces(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean s = Computed(a, b, op);
            Assert.Equal(Status.Success, s.Status);
            Assert.Equal(s.Counts["pieces"], s.Counts["predicted_pieces"]);
            // (the far spheres: every triangle of either outside the other, culled by the Intersection: no pieces)
            if (name == "far")
                Assert.Equal(0, s.Counts["pieces"]);
            else
                Assert.True(s.Counts["pieces"] > 0);
            // (and a second Compute of the instance: the same count again)
            s.Compute();
            Assert.Equal(s.Counts["pieces"], s.Counts["predicted_pieces"]);
        }

        [Fact]
        public void PredictedPieces_CountEdgePointsOnceAndInteriorPointsTwice()
        {
            // a triangle split by one point inside it (3 pieces) and one by a point on its edge (2 pieces): a box whose
            // corner pierces a face of the other box, and one whose edge lies on a face; the pieces of each touched
            // input triangle against the formula are checked through the sum
            (DMesh3 a, DMesh3 b) = (Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.3, 0.3, 0.5), new Vector3d(0.6, 0.6, 1.5)));
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true };
            arr.Compute();
            Assert.Equal(arr.Output.Count, arr.PredictedPieces);
            int inner = 0, edge = 0;
            for (int ti = 0; ti < arr.Triangles.Count; ++ti) {
                var t = arr.Triangles[ti];
                foreach (int p in t.Points.Select(arr.Canonical).Distinct().Except(new[] { t.Corners.a, t.Corners.b, t.Corners.c })) {
                    bool onEdge = false;
                    for (int k = 0; k < 3; ++k)
                        onEdge |= arr.Orient(t, t.Corners[k], t.Corners[(k + 1) % 3], p) == 0;
                    if (onEdge) edge++; else inner++;
                }
            }
            Assert.True(inner > 0 && edge > 0, $"inner {inner}, edge {edge}");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PredictedPieces_SameUnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Inputs("knot");
            ExactMeshBoolean plain = Computed(a, b, Ops.Difference);
            ExactMeshBoolean moved = Computed(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Ops.Difference);
            Assert.Equal(moved.Counts["pieces"], moved.Counts["predicted_pieces"]);
            if (sim.IsExact)
                Assert.Equal(plain.Counts["predicted_pieces"], moved.Counts["predicted_pieces"]);
        }

        [Fact]
        public void MaxPieces_AtTheCount_Succeeds_BelowIt_StopsWithoutResult_AndTheInstanceComputesAgain()
        {
            (DMesh3 a, DMesh3 b) = Inputs("spheres");
            ExactMeshBoolean free = Computed(a, b, Ops.Union);
            long pieces = free.Counts["pieces"];
            ExactMeshBoolean at = Computed(a, b, Ops.Union, (s) => s.MaxPieces = pieces);
            Assert.Equal(Status.Success, at.Status);
            Assert.Equal("", Invariance.Difference(free.Result, at.Result));

            var below = new ExactMeshBoolean(a, b, Ops.Union) { MaxPieces = pieces - 1 };
            Assert.False(below.Compute());
            Assert.Equal(Status.BudgetExceeded, below.Status);
            Assert.Null(below.Result);
            Assert.Equal(pieces, below.Counts["predicted_pieces"]);
            Assert.False(below.Counts.ContainsKey("pieces"));
            Assert.Contains("MaxPieces", below.Error);
            // (the same instance without the limit: the same result as a fresh one)
            below.MaxPieces = 0;
            Assert.True(below.Compute());
            Assert.Equal(Status.Success, below.Status);
            Assert.Equal("", Invariance.Difference(free.Result, below.Result));
        }

        [Fact]
        public void MaxPieces_InIntersectEach_PerBody()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 1);
            var bodies = new List<DMesh3> { Sphere(new Vector3d(0.9, 0, 0), 0.4), Sphere(new Vector3d(5, 0, 0), 0.4) };
            long near = Computed(a, bodies[0], Ops.Intersection).Counts["pieces"], far = Computed(a, bodies[1], Ops.Intersection).Counts["pieces"];
            Assert.True(far < near - 1);
            var status = new Status[2];
            DMesh3[] each = ExactMeshBoolean.IntersectEach(a, bodies, (s) => s.MaxPieces = near - 1, status);
            Assert.Equal(Status.BudgetExceeded, status[0]);
            Assert.Null(each[0]);
            Assert.Equal(Status.Success, status[1]);
            Assert.NotNull(each[1]);
        }

        [Fact]
        public void EarlyEndpointBudget_RefusesBeforeCrossings_ThenUsesExactPredictionOnReuse()
        {
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 b = Box(new Vector3d(0.25, 0.25, 0.25), new Vector3d(1.25, 1.25, 1.25));
            ExactMeshBoolean free = Computed(a, b, Ops.Union);
            Assert.Equal(Status.Success, free.Status);
            long prediction = free.Counts["predicted_pieces"];
            var limited = new ExactMeshBoolean(a, b, Ops.Union) { MaxPieces = 24 };
            Assert.False(limited.Compute());
            Assert.Equal(prediction, limited.Counts["predicted_pieces"]);
            Assert.False(limited.PhaseMilliseconds.ContainsKey("merge.endpoint_budget"));

            limited.EnableEarlyPieceBudgetCheck = true;
            Assert.False(limited.Compute());
            Assert.Equal(Status.BudgetExceeded, limited.Status);
            Assert.Null(limited.Result);
            Assert.InRange(limited.Counts["piece_lower_bound"], 25, prediction);
            Assert.Contains("at least", limited.Error);
            Assert.False(limited.Counts.ContainsKey("predicted_pieces"));
            Assert.False(limited.Counts.ContainsKey("pieces"));
            Assert.False(limited.PhaseMilliseconds.ContainsKey("merge.crossings"));

            limited.MaxPieces = prediction;
            Assert.True(limited.Compute(), limited.Error);
            Assert.Equal(prediction, limited.Counts["predicted_pieces"]);
            Assert.False(limited.Counts.ContainsKey("piece_lower_bound"));
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(limited.Result));
            Assert.Equal("", Invariance.Difference(free.Result, limited.Result));

            limited.MaxPieces = 0;
            Assert.True(limited.Compute(), limited.Error);
            Assert.False(limited.PhaseMilliseconds.ContainsKey("merge.endpoint_budget"));
            Assert.Equal(prediction, limited.Counts["predicted_pieces"]);
            Assert.Equal("", Invariance.Difference(free.Result, limited.Result));
        }

        [Fact]
        public void EarlyEndpointBudget_UsesExactPredictionWhenEndpointsDoNotProveRefusal()
        {
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 b = Box(new Vector3d(0.3, 0.3, 0.5), new Vector3d(0.6, 0.6, 1.5));
            ExactMeshBoolean free = Computed(a, b, Ops.Union);
            Assert.Equal(Status.Success, free.Status);
            long prediction = free.Counts["predicted_pieces"];
            var limited = new ExactMeshBoolean(a, b, Ops.Union) {
                EnableEarlyPieceBudgetCheck = true, MaxPieces = prediction - 1
            };
            Assert.False(limited.Compute());
            Assert.Equal(Status.BudgetExceeded, limited.Status);
            Assert.Equal(prediction, limited.Counts["predicted_pieces"]);
            Assert.False(limited.Counts.ContainsKey("piece_lower_bound"));
            Assert.True(limited.PhaseMilliseconds.ContainsKey("merge.crossings"));
        }

        [Fact]
        public void EarlyEndpointBudget_DoesNotCountCulledSources()
        {
            var limited = new ExactMeshBoolean(Box(Vector3d.Zero, Vector3d.One),
                Box(2 * Vector3d.One, 3 * Vector3d.One), Ops.Intersection) {
                EnableEarlyPieceBudgetCheck = true, MaxPieces = 1
            };
            Assert.True(limited.Compute(), limited.Error);
            Assert.Equal(0, limited.Counts["predicted_pieces"]);
            Assert.Equal(24, limited.Counts["t3a_culled"]);
            Assert.False(limited.Counts.ContainsKey("piece_lower_bound"));
            Assert.Equal(0, limited.Result.TriangleCount);
        }

        [Theory]
        [InlineData("coplanar boxes", Ops.Union)]
        [InlineData("vertex on face", Ops.Union)]
        [InlineData("degenerate", Ops.Union)]
        [InlineData("touching boxes", Ops.Intersection)]
        public void EarlyEndpointBudget_AgreesWithExactGuardOnDifferentIntersectionKinds(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean free = Computed(a, b, op);
            Assert.Equal(Status.Success, free.Status);
            long prediction = free.Counts["predicted_pieces"];
            long budget = Math.Max(1, prediction / 2);
            var limited = Computed(a, b, op, s => {
                s.EnableEarlyPieceBudgetCheck = true;
                s.MaxPieces = budget;
            });
            Assert.Equal(prediction > budget ? Status.BudgetExceeded : Status.Success, limited.Status);
            if (limited.Counts.TryGetValue("piece_lower_bound", out long bound))
                Assert.InRange(bound, budget + 1, prediction);
            else
                Assert.Equal(prediction, limited.Counts["predicted_pieces"]);

            limited.MaxPieces = Math.Max(1, prediction);
            Assert.True(limited.Compute(), limited.Error);
            Assert.Equal(prediction, limited.Counts["predicted_pieces"]);
            Assert.Equal("", Invariance.Difference(free.Result, limited.Result));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void EarlyEndpointBudget_RefusalIsStableAcrossFramesAndSchedules(string name)
        {
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 b = Box(new Vector3d(0.25, 0.25, 0.25), new Vector3d(1.25, 1.25, 1.25));
            Similarity s = Similarity.ByName(name);
            long Run(DMesh3 x, DMesh3 y)
            {
                var limited = new ExactMeshBoolean(x, y, Ops.Union) { EnableEarlyPieceBudgetCheck = true, MaxPieces = 24 };
                Assert.False(limited.Compute());
                Assert.Equal(Status.BudgetExceeded, limited.Status);
                return limited.Counts["piece_lower_bound"];
            }
            DMesh3 x = Invariance.Transformed(a, s), y = Invariance.Transformed(b, s);
            long parallel = Run(x, y);
            bool serial = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                Assert.Equal(parallel, Run(x, y));
            } finally {
                gParallel.SerialOnThisThread = serial;
            }
            if (s.IsExact)
                Assert.Equal(Run(a, b), parallel);
        }

        [Fact]
        public void Cancellation_BeforeCompute_StopsWithoutResult_AndTheInstanceComputesAgain()
        {
            (DMesh3 a, DMesh3 b) = Inputs("spheres");
            ExactMeshBoolean free = Computed(a, b, Ops.Union);
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel();
                var s = new ExactMeshBoolean(a, b, Ops.Union) { Cancellation = cts.Token };
                Assert.False(s.Compute());
                Assert.Equal(Status.Cancelled, s.Status);
                Assert.Null(s.Result);
                s.Cancellation = CancellationToken.None;
                Assert.True(s.Compute());
                Assert.Equal("", Invariance.Difference(free.Result, s.Result));
            }
        }

        [Fact]
        public void Cancellation_InTheClassification_StopsWithoutResult()
        {
            // the winding predicate is called during the classification (from one thread): cancelling there stops the
            // boolean after it, with no result
            (DMesh3 a, DMesh3 b) = Inputs("spheres");
            using (var cts = new CancellationTokenSource()) {
                int calls = 0;
                var s = new ExactMeshBoolean(a, b, Ops.Union) { Cancellation = cts.Token };
                s.WindingPredicate = (w) => { if (++calls == 3) cts.Cancel(); return w[0] > 0 || w[1] > 0; };
                Assert.False(s.Compute());
                Assert.Equal(Status.Cancelled, s.Status);
                Assert.Null(s.Result);
                Assert.True(s.Counts.ContainsKey("pieces"));     // (it got past the arrangement)
            }
        }

        [Fact]
        public void Cancellation_DuringTheArrangement_StopsSoon()
        {
            // a knot whose boolean takes seconds, cancelled after 100 ms: Compute returns Cancelled well before the end
            // (the knots are open tubes: stated, or the boolean would refuse them at once instead of running)
            (DMesh3 a, DMesh3 b) = KnotPair(400, 8, 24, 0.6, 1);
            Assert.False(ExactMeshBoolean.IsWindingClosed(a));
            using (var cts = new CancellationTokenSource()) {
                var s = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true, Cancellation = cts.Token };
                cts.CancelAfter(100);
                var sw = Stopwatch.StartNew();
                Assert.False(s.Compute());
                Assert.Equal(Status.Cancelled, s.Status);
                Assert.Null(s.Result);
                Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");
            }
            // the arrangement on its own: Stopped, and no output
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel();
                var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, Cancellation = cts.Token };
                arr.Compute();
                Assert.Equal(ExactMeshArrangement.StopReason.Cancelled, arr.Stopped);
                Assert.Empty(arr.Output);
            }
        }
    }
}
