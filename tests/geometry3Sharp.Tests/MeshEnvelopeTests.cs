using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Exact containment in the polyhedral envelope (MeshEnvelope). The guarantee (contained => every point within
    /// epsilon of the mesh) is checked against distances to the mesh; "not contained" against the uncovered
    /// witness point; the exact path against queries that no single prism decides.
    /// </summary>
    public class MeshEnvelopeTests
    {
        static DMesh3 BumpySphere(int edgeVertices = 10)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.15 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5)));
            }
            return m;
        }

        /// <summary> flat grid in z = 0 on [0, n h]^2; cells inside the hole (cell index range) are left out </summary>
        static DMesh3 Plane(int n, double h, int holeFrom = -1, int holeTo = -1)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) ids[i, j] = m.AppendVertex(new Vector3d(i * h, j * h, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    if (i >= holeFrom && i < holeTo && j >= holeFrom && j < holeTo) continue;
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        static Vector3d Normal(Vector3d a, Vector3d b, Vector3d c) { return (b - a).Cross(c - a).Normalized; }

        /// <summary> the largest distance from the triangle to the mesh, sampled (barycentric grid) </summary>
        static double SampledDistance(DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c, int n = 12)
        {
            double worst = 0;
            for (int i = 0; i <= n; ++i)
                for (int j = 0; i + j <= n; ++j) {
                    Vector3d p = a + ((double)i / n) * (b - a) + ((double)j / n) * (c - a);
                    int tid = tree.FindNearestTriangle(p);
                    worst = Math.Max(worst, Math.Sqrt(MeshQueries.TriDistanceSqr(tree.Mesh, tid, p)));
                }
            return worst;
        }

        [Fact]
        public void InputTriangles_AreContained()
        {
            DMesh3 mesh = BumpySphere();
            var env = new MeshEnvelope(mesh, 1e-3);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Assert.Equal(mesh.TriangleCount, env.PrismCount);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                Assert.True(env.ContainsTriangle(a, b, c), $"triangle {t}");
                Assert.True(env.ContainsSegment(a, b));
                Assert.True(env.Contains((a + b + c) / 3));
            }
        }

        [Fact]
        public void Plane_OffsetAlongTheNormal()
        {
            DMesh3 mesh = Plane(10, 0.1);
            double eps = 0.01;
            var env = new MeshEnvelope(mesh, eps);
            Vector3d a = new Vector3d(0.13, 0.21, 0), b = new Vector3d(0.71, 0.33, 0), c = new Vector3d(0.42, 0.87, 0);
            foreach (double z in new[] { 0.0, 0.5 * eps, 0.79 * eps, -0.79 * eps }) {
                Vector3d dz = new Vector3d(0, 0, z);
                Assert.True(env.ContainsTriangle(a + dz, b + dz, c + dz), $"z = {z / eps} eps");
            }
            foreach (double z in new[] { 0.81 * eps, 1.0 * eps, -1.5 * eps }) {
                Vector3d dz = new Vector3d(0, 0, z);
                Assert.False(env.ContainsTriangle(a + dz, b + dz, c + dz), $"z = {z / eps} eps");
            }
            // one corner lifted: the triangle leaves the slab only there
            Assert.False(env.ContainsTriangle(a, b, c + new Vector3d(0, 0, 0.9 * eps)));
            Assert.True(env.ContainsTriangle(a, b, c + new Vector3d(0, 0, 0.7 * eps)));
        }

        [Fact]
        public void Plane_LateralOffsetAtTheBorder()
        {
            DMesh3 mesh = Plane(10, 0.1);
            double eps = 0.01, lat = MeshEnvelope.LateralFraction * eps;
            var env = new MeshEnvelope(mesh, eps);
            Assert.True(env.Contains(new Vector3d(1 + 0.95 * lat, 0.5, 0)));
            Assert.False(env.Contains(new Vector3d(1 + 1.05 * lat, 0.5, 0)));
            Assert.False(env.Contains(new Vector3d(1 + eps, 0.5, 0)));
            Assert.True(env.ContainsSegment(new Vector3d(0.5, 0.5, 0), new Vector3d(1 + 0.9 * lat, 0.5, 0.3 * eps)));
            Assert.False(env.ContainsSegment(new Vector3d(0.5, 0.5, 0), new Vector3d(1 + 1.2 * lat, 0.5, 0)));
        }

        [Fact]
        public void TriangleAcrossManyPrisms_NeedsTheExactPath()
        {
            // no single prism holds the big triangle, the union does
            DMesh3 mesh = Plane(10, 0.1);
            var env = new MeshEnvelope(mesh, 0.01);
            Vector3d a = new Vector3d(0.05, 0.05, 0.004), b = new Vector3d(0.95, 0.1, -0.003), c = new Vector3d(0.3, 0.9, 0.002);
            Assert.True(env.ContainsTriangle(a, b, c));
            Assert.Equal(1, env.ExactQueries);
            Assert.Null(env.LastWitness);
        }

        [Fact]
        public void Hole_WiderThanTheEnvelope_IsNotCovered()
        {
            // a hole of one cell (0.1): the prisms reach 0.554 eps into it from each side
            DMesh3 mesh = Plane(10, 0.1, 4, 5);
            var holeCenter = new Vector3d(0.45, 0.45, 0);
            foreach (double eps in new[] { 0.01, 0.05 }) {
                var env = new MeshEnvelope(mesh, eps);
                Vector3d a = new Vector3d(0.2, 0.2, 0), b = new Vector3d(0.8, 0.3, 0), c = new Vector3d(0.4, 0.8, 0);
                bool covered = 2 * MeshEnvelope.LateralFraction * eps > 0.1 * Math.Sqrt(2);  // the corners of the hole too
                if (eps == 0.01) {
                    Assert.False(env.ContainsTriangle(a, b, c));
                    Assert.True(env.Contains(a) && env.Contains(b) && env.Contains(c), "corners inside: decided exactly");
                    Assert.NotNull(env.LastWitness);
                    Assert.False(env.Contains(env.LastWitness.Value), "the witness is outside");
                    Assert.True(env.LastWitness.Value.Distance(holeCenter) < 0.1, "in the hole");
                    Assert.False(env.ContainsSegment(new Vector3d(0.3, 0.45, 0), new Vector3d(0.6, 0.45, 0)));
                } else {
                    Assert.False(covered);      // 0.554 * 0.05 * 2 = 0.055 < 0.1: the hole's middle stays open
                    Assert.False(env.Contains(holeCenter));
                    Assert.True(env.ContainsSegment(new Vector3d(0.4 + 0.02, 0.45, 0), new Vector3d(0.5 - 0.02, 0.45, 0)) == false);
                }
            }
            // a hole narrower than twice the lateral offset is covered
            var wide = new MeshEnvelope(mesh, 0.1);
            Assert.True(wide.ContainsTriangle(new Vector3d(0.35, 0.35, 0), new Vector3d(0.55, 0.38, 0), new Vector3d(0.42, 0.56, 0)));
        }

        [Fact]
        public void DegenerateQueries_SegmentsAndPoints()
        {
            DMesh3 mesh = Plane(10, 0.1, 4, 5);
            var env = new MeshEnvelope(mesh, 0.01);
            Vector3d a = new Vector3d(0.1, 0.45, 0), b = new Vector3d(0.3, 0.45, 0), c = new Vector3d(0.9, 0.45, 0);
            Assert.True(env.ContainsTriangle(a, b, b));
            Assert.True(env.ContainsTriangle(a, a, a));
            Assert.False(env.ContainsTriangle(a, b, c), "collinear across the hole");
            Assert.True(env.ContainsTriangle(a, b, new Vector3d(0.2, 0.45, 0)), "collinear, left of the hole");
        }

        [Fact]
        public void Fuzz_ContainedMeansWithinEpsilon_NotContainedHasAWitness()
        {
            DMesh3 mesh = BumpySphere(8);
            double eps = 0.02;
            var env = new MeshEnvelope(mesh, eps);
            var exactOnly = new MeshEnvelope(mesh, eps) { SkipFilters = true };
            var tree = new DMeshAABBTree3(mesh, true);
            var rand = new Random(17);
            var tris = mesh.TriangleIndices().ToList();
            int inside = 0, outside = 0, exact = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            for (int k = 0; k < 400; ++k) {
                mesh.GetTriVertices(tris[rand.Next(tris.Count)], ref a, ref b, ref c);
                Vector3d n = Normal(a, b, c);
                double scale = 0.5 + 2 * rand.NextDouble();       // up to 2.5 times the size: across prisms
                Vector3d ctr = (a + b + c) / 3;
                Func<Vector3d, Vector3d> jitter = (p) => ctr + scale * (p - ctr) + (rand.NextDouble() - 0.5) * 1.6 * eps * n;
                Vector3d qa = jitter(a), qb = jitter(b), qc = jitter(c);
                int exactBefore = env.ExactQueries;
                bool r = env.ContainsTriangle(qa, qb, qc);
                Assert.Equal(r, exactOnly.ContainsTriangle(qa, qb, qc));
                if (env.ExactQueries > exactBefore) exact++;
                if (r) {
                    inside++;
                    double d = SampledDistance(tree, qa, qb, qc);
                    Assert.True(d <= eps * (1 + 1e-9), $"query {k}: contained but {d / eps} eps away");
                } else {
                    outside++;
                    if (env.ExactQueries > exactBefore && env.LastWitness.HasValue)
                        Assert.False(env.Contains(env.LastWitness.Value), $"query {k}: the witness is inside");
                }
            }
            Assert.True(inside > 50 && outside > 50 && exact > 20, $"{inside} inside, {outside} outside, {exact} exact (exercised)");
        }

        [Fact]
        public void ExactPath_AgreesWithTheFilters()
        {
            // the same queries decided with and without the floating-point filters, including queries parallel
            // to the prisms' slabs (decided exactly by constant inequalities)
            DMesh3 mesh = Plane(10, 0.1);
            double eps = 0.01;
            var filtered = new MeshEnvelope(mesh, eps);
            var exact = new MeshEnvelope(mesh, eps) { SkipFilters = true };
            Vector3d a = new Vector3d(0.13, 0.21, 0), b = new Vector3d(0.71, 0.33, 0), c = new Vector3d(0.42, 0.87, 0);
            foreach (double z in new[] { 0.0, 0.5, 0.79, -0.79, 0.81, 0.9, -1.5 }) {
                Vector3d dz = new Vector3d(0, 0, z * eps);
                bool expected = Math.Abs(z) < 0.8;
                Assert.Equal(expected, filtered.ContainsTriangle(a + dz, b + dz, c + dz));
                Assert.Equal(expected, exact.ContainsTriangle(a + dz, b + dz, c + dz));
                Assert.Equal(expected, exact.ContainsSegment(a + dz, b + dz));
                Assert.Equal(expected, exact.Contains(c + dz));
            }
            Assert.True(exact.ExactQueries > 20);
        }

        [Theory]
        [InlineData("rotated")]
        [InlineData("rotated, scale 1e-3, utm")]
        public void Filters_DecideCorrectlyAtTheBoundary(string name)
        {
            // points and tiny triangles stepping through the top of a slab in steps of a few ulps: the filtered
            // queries must agree with the exact ones (the filters may only pass on undecided cases)
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = Invariance.Transformed(Plane(4, 0.25), sim);
            double eps = 0.01 * sim.Scale;
            var filtered = new MeshEnvelope(mesh, eps);
            var exact = new MeshEnvelope(mesh, eps) { SkipFilters = true };
            Vector3d p0 = sim.Apply(new Vector3d(0.4, 0.55, 0)), n = sim.ApplyDirection(Vector3d.AxisZ);
            // the boundary along n, by bisection on the exact point test
            double lo = 0, hi = 2 * eps;
            for (int k = 0; k < 200 && hi - lo > 0; ++k) {
                double mid = 0.5 * (lo + hi);
                if (mid == lo || mid == hi) break;
                if (exact.Contains(p0 + mid * n)) lo = mid; else hi = mid;
            }
            Assert.True(lo > 0.7 * eps && lo < 0.81 * eps, $"{lo / eps}");
            int insideCount = 0, outsideCount = 0;
            double step = Math.Max(4 * (hi - lo), 1e-15 * Math.Abs(p0.x));
            Vector3d t1 = sim.ApplyDirection(new Vector3d(0.01, 0, 0)) * sim.Scale, t2 = sim.ApplyDirection(new Vector3d(0, 0.01, 0)) * sim.Scale;
            for (int k = -40; k <= 40; ++k) {
                Vector3d q = p0 + (lo + k * step) * n;
                bool e = exact.Contains(q);
                Assert.Equal(e, filtered.Contains(q));
                if (e) insideCount++; else outsideCount++;
                Assert.Equal(exact.ContainsTriangle(q, q + t1, q + t2), filtered.ContainsTriangle(q, q + t1, q + t2));
            }
            Assert.True(insideCount > 10 && outsideCount > 10, $"{insideCount} / {outsideCount} (exercised)");
        }

        [Fact]
        public void LongTriangle_OutwardEdgeNormalsBoundTheSides()
        {
            // without the outward normal of each edge, the 8 uniform directions alone would let the prism bulge
            // out beyond the middle of an edge that is not along one of them
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0.9, 0.3, 0)));
            double eps = 0.01, lat = MeshEnvelope.LateralFraction * eps;
            var env = new MeshEnvelope(m, eps);
            Vector3d mid = new Vector3d(0.45, 0.15, 0), outward = new Vector3d(-0.3, 0.9, 0).Normalized;
            Assert.True(env.Contains(mid + 0.95 * lat * outward));
            Assert.False(env.Contains(mid + 1.1 * lat * outward));
            var exact = new MeshEnvelope(m, eps) { SkipFilters = true };
            Assert.False(exact.Contains(mid + 1.1 * lat * outward));
        }

        [Fact]
        public void ParallelQueries_SameAnswersAsSerial()
        {
            DMesh3 mesh = BumpySphere(8);
            var env = new MeshEnvelope(mesh, 0.02);
            var rand = new Random(5);
            var tris = mesh.TriangleIndices().ToList();
            var queries = new List<Vector3d[]>();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            for (int k = 0; k < 300; ++k) {
                mesh.GetTriVertices(tris[rand.Next(tris.Count)], ref a, ref b, ref c);
                Vector3d n = Normal(a, b, c), ctr = (a + b + c) / 3;
                double scale = 0.5 + 2 * rand.NextDouble(), lift = (rand.NextDouble() - 0.5) * 0.03;
                queries.Add(new[] { ctr + scale * (a - ctr) + lift * n, ctr + scale * (b - ctr), ctr + scale * (c - ctr) - lift * n });
            }
            bool[] serial = queries.Select(q => env.ContainsTriangle(q[0], q[1], q[2])).ToArray();
            bool[] points = queries.Select(q => env.Contains(q[0])).ToArray();
            for (int round = 0; round < 3; ++round) {
                var par = new bool[queries.Count];
                var parPoints = new bool[queries.Count];
                System.Threading.Tasks.Parallel.For(0, queries.Count, (k) => {
                    par[k] = env.ContainsTriangle(queries[k][0], queries[k][1], queries[k][2]);
                    parPoints[k] = env.Contains(queries[k][0]);
                });
                Assert.Equal(serial, par);
                Assert.Equal(points, parPoints);
            }
            Assert.Contains(true, serial);
            Assert.Contains(false, serial);
        }

        [Fact]
        public void CollinearAndSliverInputTriangles_InGeneralPosition()
        {
            // a collinear triangle and a sliver with coordinates in general position: an edge cross product
            // would be rounding noise in an arbitrary direction
            var m = new DMesh3();
            Vector3d p0 = new Vector3d(0, 0, 0), p1 = new Vector3d(0.1, 0.2, 0.3), p2 = new Vector3d(0.3, 0.6, 0.9);
            Vector3d s2 = new Vector3d(0.3, 0.6 + 1e-9, 0.9 - 2e-9);
            m.AppendTriangle(m.AppendVertex(p0), m.AppendVertex(p1), m.AppendVertex(p2));
            m.AppendTriangle(m.AppendVertex(p0 + Vector3d.AxisX), m.AppendVertex(p2 + Vector3d.AxisX), m.AppendVertex(s2 + Vector3d.AxisX));
            double eps = 0.01;
            var env = new MeshEnvelope(m, eps);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Assert.True(env.ContainsTriangle(p0, p1, p2));
            Assert.True(env.ContainsTriangle(p0 + Vector3d.AxisX, p2 + Vector3d.AxisX, s2 + Vector3d.AxisX));
            Vector3d dir = (p2 - p0).Normalized, side = dir.Cross(Vector3d.AxisZ).Normalized;
            Vector3d mid = 0.5 * (p0 + p2);
            Assert.True(env.Contains(mid + 0.5 * eps * side));
            Assert.False(env.Contains(mid + 1.01 * eps * side));
            Assert.False(env.Contains(mid + 1.01 * eps * dir.Cross(side)));
        }

        [Fact]
        public void TinyCoordinates()
        {
            // far below 1 but above the subnormal range: works; an epsilon near the subnormal range: TooThin
            DMesh3 mesh = Invariance.Transformed(Plane(4, 0.25), new Similarity("tiny", 1e-200, Matrix3d.Identity, Vector3d.Zero));
            var env = new MeshEnvelope(mesh, 1e-202);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Vector3d p = new Vector3d(0.3e-200, 0.6e-200, 0);
            Assert.True(env.Contains(p + new Vector3d(0, 0, 0.5e-202)));
            Assert.False(env.Contains(p + new Vector3d(0, 0, 1.0e-202)));
            Assert.True(env.ContainsTriangle(p, p + new Vector3d(0.2e-200, 0, 0), p + new Vector3d(0, 0.2e-200, 0.3e-202)));
            Assert.Equal(EnvelopeStatus.TooThin, new MeshEnvelope(mesh, 1e-290).Status);
        }

        [Fact]
        public void PieceLimit_AnswersFalse()
        {
            DMesh3 mesh = Plane(10, 0.1);
            var env = new MeshEnvelope(mesh, 0.01) { MaxExactPieces = 2 };
            Vector3d a = new Vector3d(0.05, 0.05, 0.004), b = new Vector3d(0.95, 0.1, -0.003), c = new Vector3d(0.3, 0.9, 0.002);
            Assert.False(env.ContainsTriangle(a, b, c));
            Assert.Equal(1, env.AbortedQueries);
            env.MaxExactPieces = 100000;
            Assert.True(env.ContainsTriangle(a, b, c));
            Assert.Equal(1, env.AbortedQueries);
        }

        [Fact]
        public void TinyNormals_DoNotMakeThePrismUnbounded()
        {
            // Vector3d.Normalized returns zero below a length of 2.2e-16 (absolute): a sliver of height 1e-17 or a
            // triangle with edges of 1e-16 must still get a bounded prism
            var sliver = new DMesh3();
            sliver.AppendTriangle(sliver.AppendVertex(Vector3d.Zero), sliver.AppendVertex(new Vector3d(1, 0, 0)), sliver.AppendVertex(new Vector3d(0.5, 1e-17, 0)));
            foreach (bool skip in new[] { false, true }) {
                var env = new MeshEnvelope(sliver, 0.01) { SkipFilters = skip };
                Assert.Equal(EnvelopeStatus.Ready, env.Status);
                Assert.False(env.Contains(new Vector3d(-0.0101, -0.0101, 0.0101)), "0.0175 from the sliver");
                Assert.False(env.Contains(new Vector3d(0.5, 0, 0.0101)));
                Assert.True(env.Contains(new Vector3d(0.5, 0, 0.005)));
            }
            var small = new DMesh3();
            small.AppendTriangle(small.AppendVertex(Vector3d.Zero), small.AppendVertex(new Vector3d(1e-16, 0, 0)), small.AppendVertex(new Vector3d(0, 1e-16, 0)));
            foreach (bool skip in new[] { false, true }) {
                var env = new MeshEnvelope(small, 1e-10) { SkipFilters = skip };
                Assert.Equal(EnvelopeStatus.Ready, env.Status);
                Assert.False(env.Contains(new Vector3d(1.0099e-10, 1.0099e-10, 1.0099e-10)), "1.75e-10 from the triangle");
                Assert.True(env.Contains(new Vector3d(3e-17, 3e-17, 0.5e-10)));
            }
        }

        [Fact]
        public void PlaneScaledBy2PowMinus56_SameAnswersBitForBit()
        {
            // a unit-size mesh at 1.4e-17: edges below the absolute 2.2e-16 of Vector3d.Normalized
            var sim = new Similarity("scale 2^-56", Math.Pow(2, -56), Matrix3d.Identity, Vector3d.Zero);
            DMesh3 mesh = Plane(10, 0.1);
            double eps = 0.01;
            var env0 = new MeshEnvelope(mesh, eps);
            var env = new MeshEnvelope(Invariance.Transformed(mesh, sim), sim.Scale * eps);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            var rand = new Random(3);
            int inside = 0;
            for (int k = 0; k < 200; ++k) {
                Vector3d p = new Vector3d(1.2 * rand.NextDouble() - 0.1, 1.2 * rand.NextDouble() - 0.1, 0.03 * (rand.NextDouble() - 0.5));
                bool r0 = env0.Contains(p);
                Assert.Equal(r0, env.Contains(sim.Apply(p)));
                Vector3d q = p + new Vector3d(0.05, 0.02, 0.002), w = p + new Vector3d(0.01, 0.06, -0.003);
                Assert.Equal(env0.ContainsTriangle(p, q, w), env.ContainsTriangle(sim.Apply(p), sim.Apply(q), sim.Apply(w)));
                if (r0) inside++;
            }
            Assert.True(inside > 20 && inside < 180, $"{inside} inside (exercised)");
        }

        [Fact]
        public void UnderflowingEdges_StillBuild()
        {
            // coordinates around 1e-160: squared lengths underflow to 0
            DMesh3 mesh = Invariance.Transformed(Plane(4, 0.25), new Similarity("1e-160", 1e-160, Matrix3d.Identity, Vector3d.Zero));
            var env = new MeshEnvelope(mesh, 1e-162);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Vector3d p = new Vector3d(0.3e-160, 0.6e-160, 0);
            Assert.True(env.Contains(p + new Vector3d(0, 0, 0.5e-162)));
            Assert.False(env.Contains(p + new Vector3d(0, 0, 1.0e-162)));
        }

        [Fact]
        public void Overflow_GivesAStatus_NoException()
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(1.7e308, 0, 0)), m.AppendVertex(new Vector3d(1.7e308, 1e300, 0)), m.AppendVertex(new Vector3d(1.6e308, 0, 1e300)));
            var env = new MeshEnvelope(m, 1e308);
            Assert.Equal(EnvelopeStatus.OutOfRange, env.Status);
            Assert.False(env.Contains(new Vector3d(1.7e308, 0, 0)));
        }

        [Fact]
        public void OverflowingEdge_GivesAStatus()
        {
            // the edge v0 -> v1 overflows to inf: without its normals the prism would reach about 1e306 sideways
            double c = Math.Cos(Math.PI / 8), s = Math.Sin(Math.PI / 8);
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(-c, -s, 0) * 1e308), m.AppendVertex(new Vector3d(c, s, 0) * 1e308),
                m.AppendVertex(new Vector3d(-c * 1e308, -0.5e308, 0)));
            var env = new MeshEnvelope(m, 1e300);
            Assert.Equal(EnvelopeStatus.OutOfRange, env.Status);
            Assert.False(env.Contains(1e306 * new Vector3d(-s, c, 0)));
            // non-finite vertices too, and a box whose component sum overflows is still fine
            var bad = new DMesh3();
            bad.AppendTriangle(bad.AppendVertex(Vector3d.Zero), bad.AppendVertex(new Vector3d(double.NaN, 0, 0)), bad.AppendVertex(new Vector3d(0, 1, 0)));
            Assert.Equal(EnvelopeStatus.OutOfRange, new MeshEnvelope(bad, 0.01).Status);
            var big = new DMesh3();
            big.AppendTriangle(big.AppendVertex(new Vector3d(1e308, 1e308, 0)), big.AppendVertex(new Vector3d(1e308 - 1e293, 1e308, 0)), big.AppendVertex(new Vector3d(1e308, 1e308 - 1e293, 0)));
            var benv = new MeshEnvelope(big, 1e297);
            Assert.Equal(EnvelopeStatus.Ready, benv.Status);
            Assert.True(benv.Contains(new Vector3d(1e308, 1e308, 0)));
        }

        [Fact]
        public void NonFiniteQueries_AreNotContained()
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)));
            var env = new MeshEnvelope(m, 0.01);
            Vector3d a = new Vector3d(0.2, 0.2, 0);
            foreach (Vector3d q in new[] { new Vector3d(double.PositiveInfinity, 0.2, 0), new Vector3d(0.2, double.NaN, 0), new Vector3d(0, 0, double.NegativeInfinity) }) {
                Assert.False(env.Contains(q));
                Assert.False(env.ContainsSegment(a, q));
                Assert.False(env.ContainsTriangle(a, q, new Vector3d(0.3, 0.2, 0)));
            }
        }

        [Fact]
        public void Statuses()
        {
            DMesh3 mesh = Plane(2, 1);
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new MeshEnvelope(mesh, 0).Status);
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new MeshEnvelope(mesh, double.NaN).Status);
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new MeshEnvelope(mesh, double.PositiveInfinity).Status);
            Assert.False(new MeshEnvelope(mesh, -1).Contains(Vector3d.Zero));
            Assert.Equal(EnvelopeStatus.Empty, new MeshEnvelope(new DMesh3(), 1).Status);
            // UTM coordinates: 1e-9 is below the rounding of the prisms
            DMesh3 utm = Invariance.Transformed(mesh, Similarity.Utm);
            var thin = new MeshEnvelope(utm, 1e-9);
            Assert.Equal(EnvelopeStatus.TooThin, thin.Status);
            Assert.False(thin.Contains(utm.GetVertex(0)));
            Assert.Equal(EnvelopeStatus.Ready, new MeshEnvelope(utm, 1e-3).Status);
        }

        [Fact]
        public void DegenerateInputTriangles_GetAPrism()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(2, 0, 0));
            int d = m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(a, b, c);      // collinear
            m.AppendTriangle(a, c, d);
            var env = new MeshEnvelope(m, 0.01);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Assert.True(env.ContainsTriangle(Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(2, 0, 0)));
            Assert.True(env.Contains(new Vector3d(1.5, -0.004, 0.004)));
        }

        // ---------------------------------------------------------------- invariance, reuse

        static List<(Vector3d, Vector3d, Vector3d, bool)> OffsetQueries(DMesh3 mesh)
        {
            // input triangles (contained) and the same grown by 1.3 around their centroid (across neighbouring
            // prisms; the expected flag is not used)
            var list = new List<(Vector3d, Vector3d, Vector3d, bool)>();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices().Where(t => t % 7 == 0)) {
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                list.Add((a, b, c, true));
                Vector3d ctr = (a + b + c) / 3;
                list.Add((ctr + 1.3 * (a - ctr), ctr + 1.3 * (b - ctr), ctr + 1.3 * (c - ctr), false));    // decided below
            }
            return list;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Results_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = BumpySphere(8);
            double eps = 0.01;
            var env0 = new MeshEnvelope(mesh, eps);
            DMesh3 moved = Invariance.Transformed(mesh, sim);
            var env = new MeshEnvelope(moved, sim.Scale * eps);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            int checkedCount = 0;
            foreach (int t in mesh.TriangleIndices().Where(t => t % 5 == 0)) {
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d n = Normal(a, b, c), ctr = (a + b + c) / 3;
                var queries = new List<(Vector3d, Vector3d, Vector3d)>() {
                    (a, b, c),
                    (a + 0.4 * eps * n, b + 0.4 * eps * n, c + 0.4 * eps * n),
                    (a + 1.2 * eps * n, b + 1.2 * eps * n, c + 1.2 * eps * n),
                    (ctr + 1.5 * (a - ctr), ctr + 1.5 * (b - ctr) + 0.3 * eps * n, ctr + 1.5 * (c - ctr)),
                };
                foreach (var q in queries) {
                    bool r0 = env0.ContainsTriangle(q.Item1, q.Item2, q.Item3);
                    bool r = env.ContainsTriangle(sim.Apply(q.Item1), sim.Apply(q.Item2), sim.Apply(q.Item3));
                    if (sim.IsExact || q.Item1 == a) Assert.Equal(r0, r);
                    checkedCount++;
                }
                Assert.True(env.ContainsTriangle(sim.Apply(a + 0.4 * eps * n), sim.Apply(b + 0.4 * eps * n), sim.Apply(c + 0.4 * eps * n)));
                Assert.False(env.ContainsTriangle(sim.Apply(a + 1.2 * eps * n), sim.Apply(b + 1.2 * eps * n), sim.Apply(c + 1.2 * eps * n)));
            }
            if (sim.IsExact) Assert.Equal(env0.ExactQueries, env.ExactQueries);
            Assert.True(checkedCount > 50);
        }

        [Fact]
        public void Reuse_SameAnswersTwice_MeshUnchanged()
        {
            DMesh3 mesh = BumpySphere(8);
            var before = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList();
            var env = new MeshEnvelope(mesh, 0.01);
            var q = OffsetQueries(mesh);
            var first = q.Select(x => env.ContainsTriangle(x.Item1, x.Item2, x.Item3)).ToList();
            var second = q.Select(x => env.ContainsTriangle(x.Item1, x.Item2, x.Item3)).ToList();
            Assert.Equal(first, second);
            Assert.Contains(true, first);
            Assert.Contains(false, first);
            Assert.Equal(before, mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList());
        }
    }
}
