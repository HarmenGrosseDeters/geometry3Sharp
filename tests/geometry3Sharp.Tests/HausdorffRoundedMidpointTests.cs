using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #738: at coordinates far beyond the meshes' extent a subdivision midpoint is not representable. At z = 1.3 2^52 (an ulp is 1)
    /// two spheres of radius 1 and 1.01 are folded onto the levels z = -1, 0, 1, and the midpoint of z = -1 and 0 rounded onto a corner:
    /// a child triangle was its parent, refinement ran to MaxSubdivisions (minutes, GBs), and the rounded points, off the triangles by
    /// up to 0.5, gave a "certified" lower bound of 0.18 for a distance of 0.0099. Now (a) such meshes are measured on copies moved
    /// near 0 by an exact shift, (b) a triangle that cannot be split, or whose points may be off by more than the tolerance, is not
    /// refined further (its bound is kept), and (c) the bounds account for the rounding of subdivision points, so a certified lower
    /// bound never exceeds the distance. The reference is the same meshes shifted by the offset, exactly (Sterbenz: every z lies in
    /// [offset / 2, 2 offset]), measured near 0.
    /// </summary>
    public class HausdorffRoundedMidpointTests
    {
        static DMesh3 Sphere(double r, double z)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, r * m.GetVertex(v) + new Vector3d(0, 0, z));
            return m;
        }

        static DMesh3 Shifted(DMesh3 m, double z, int axis = 2)
        {
            var c = new DMesh3(m);
            Vector3d d = Vector3d.Zero; d[axis] = z;
            foreach (int v in c.VertexIndices().ToList()) c.SetVertex(v, c.GetVertex(v) - d);
            return c;
        }

        // the test hook (absent on the base, where the meshes are always measured as given)
        static void AsGiven(MeshHausdorffDistance h)
        {
            FieldInfo f = typeof(MeshHausdorffDistance).GetField("AsGivenForTests", BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(h, true);
        }

        static void AssertValid(MeshHausdorffDistance h, MeshHausdorffDistance reference, string what)
        {
            Assert.True(reference.Converged, what + ": reference");
            Assert.False(double.IsNaN(h.LowerBound) || double.IsNaN(h.UpperBound), what);
            // both intervals contain the distance: the certified lower bound is not above the reference's upper bound, the upper
            // bound not below its lower bound
            Assert.True(h.LowerBound <= reference.UpperBound, what + ": lower " + h.LowerBound + " above the distance " + reference.UpperBound);
            Assert.True(h.UpperBound >= reference.LowerBound, what + ": upper " + h.UpperBound + " below the distance " + reference.LowerBound);
            Assert.True(h.LowerBound >= 0 && h.LowerBound <= h.UpperBound, what);
        }

        public static TheoryData<double> Offsets() => new TheoryData<double> {
            1.3 * Math.Pow(2, 52), 1.7 * Math.Pow(2, 50), 1.1 * Math.Pow(2, 40), 1.5 * Math.Pow(2, 26), 1.5 * Math.Pow(2, 24), -1.3 * Math.Pow(2, 52) };

        /// <summary> the folded spheres converge, fast, to the result of the exactly shifted meshes (base: 0.18 .. 0.31 after
        /// minutes at 1.3 2^52) </summary>
        [Theory]
        [MemberData(nameof(Offsets))]
        public void FoldedSpheres_AtLargeOffsets_ConvergeToTheShiftedResult(double z)
        {
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            var reference = new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z));
            Assert.True(reference.Compute());
            var sw = Stopwatch.StartNew();
            // (the base ran into this cap: 200k subdivisions, seconds; the time bound is loose for a loaded machine, the subdivisions
            // are the deterministic measure)
            var h = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 100000 };
            Assert.True(h.Compute());
            sw.Stop();
            Assert.True(h.Subdivisions < 20000, h.Subdivisions + " subdivisions");
            Assert.True(sw.ElapsedMilliseconds < 4000, "took " + sw.ElapsedMilliseconds + " ms, " + h.Subdivisions + " subdivisions");
            Assert.True(h.Converged, "not converged: " + h.LowerBound + " .. " + h.UpperBound);
            AssertValid(h, reference, "z = " + z);
            Assert.True(h.UpperBound - h.LowerBound <= 1e-4 * 2.01 * Math.Sqrt(3) * 1.0001, "gap " + (h.UpperBound - h.LowerBound));
            // (the worst point is back at the meshes' coordinates, in their bounding box up to a rounding)
            Assert.InRange(h.WorstPoint.z, z - 1.01 - 2 * Math.Abs(z) * 1.2e-16, z + 1.01 + 2 * Math.Abs(z) * 1.2e-16);
        }

        /// <summary> the certified lower bound never exceeds the distance, also measured as given (the bounds' own accounting of
        /// rounded subdivision points, without the shifted copies), and that run ends after few subdivisions, unconverged:
        /// the folded points are off the triangles by up to 0.5, beyond the tolerance </summary>
        [Theory]
        [MemberData(nameof(Offsets))]
        public void CertifiedLowerBound_NeverAboveTheDistance(double z)
        {
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            var reference = new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z));
            Assert.True(reference.Compute());
            foreach (bool symmetric in new[] { true, false }) {
                var given = new MeshHausdorffDistance(a, b) { Symmetric = symmetric, MaxSubdivisions = 100000 };
                AsGiven(given);
                var sw = Stopwatch.StartNew();
                Assert.True(given.Compute());
                Assert.True(sw.ElapsedMilliseconds < 4000, "as given took " + sw.ElapsedMilliseconds + " ms");
                Assert.True(given.Subdivisions < 50000, "as given: " + given.Subdivisions + " subdivisions");
                var oneSidedRef = symmetric ? reference : new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z)) { Symmetric = false };
                if (!symmetric) Assert.True(oneSidedRef.Compute());
                AssertValid(given, oneSidedRef, "as given, z = " + z + ", symmetric " + symmetric);
                // (converged means the certified gap is within the tolerance, 1e-4 of the diagonal)
                if (given.Converged)
                    Assert.True(given.UpperBound - given.LowerBound <= 1e-4 * 2.01 * Math.Sqrt(3) * 1.0001, "converged with a gap of " + (given.UpperBound - given.LowerBound));
            }
        }

        // an n x n grid of the square [0, s]^2 at height z, its x and y rippled (the same for every z, so two grids differ only in z)
        static DMesh3 Grid(double s, double z, double x0 = 0, int n = 8)
        {
            var m = new DMesh3();
            var id = new int[n + 1, n + 1];
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j)
                    id[i, j] = m.AppendVertex(new Vector3d(x0 + s * (i + 0.3 * Math.Sin(3 * j + i)) / n, s * j / n, z));
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
                    m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
                }
            return m;
        }

        public static TheoryData<int, int> GridCases() => new TheoryData<int, int> {
            { 0, 49 }, { 0, 40 }, { 0, 20 }, { 0, 0 }, { -20, 30 }, { -30, 45 }, { -40, 49 }, { -40, 0 }, { 20, 48 } };

        /// <summary> closed form, independent of the library: two copies of a grid of size 2^k, one 2^(k-3) above the other, at
        /// height and x offset about 2^(k+m) (m up to 49: an ulp of the height is then at most 2^(k-3), so z + 2^(k-3) is exact; x
        /// rounds the same in both). Every point of either is exactly 2^(k-3) from the other, so the distance is 2^(k-3): the
        /// certified interval contains it and converges to it </summary>
        [Theory]
        [MemberData(nameof(GridCases))]
        public void ParallelGrids_ClosedForm_Contained(int k, int m)
        {
            double s = Math.Pow(2, k), d = Math.Pow(2, k - 3), z = 1.3 * Math.Pow(2, k + m);
            z = Math.Round(z / d) * d;                  // (a multiple of d, so z + d is exact)
            Assert.Equal(d, (z + d) - z);
            double x0 = 1.7 * Math.Pow(2, k + m);
            foreach (bool symmetric in new[] { true, false }) {
                // (capped: the base ran into the cap)
                var h = new MeshHausdorffDistance(Grid(s, z, x0), Grid(s, z + d, x0)) { Symmetric = symmetric, MaxSubdivisions = 100000 };
                Assert.True(h.Compute());
                Assert.True(h.LowerBound <= d && d <= h.UpperBound, $"k {k} m {m}: {h.LowerBound:R} .. {h.UpperBound:R}, distance {d:R}");
                Assert.True(h.Converged, $"k {k} m {m}");
                Assert.True(h.UpperBound - h.LowerBound <= 1e-3 * s, $"k {k} m {m}: gap {h.UpperBound - h.LowerBound}");
                var given = new MeshHausdorffDistance(Grid(s, z, x0), Grid(s, z + d, x0)) { Symmetric = symmetric, MaxSubdivisions = 100000 };
                AsGiven(given);
                Assert.True(given.Compute());
                Assert.True(given.LowerBound <= d && d <= given.UpperBound, $"as given, k {k} m {m}: {given.LowerBound:R} .. {given.UpperBound:R}");
            }
        }

        /// <summary> dense sampling, independent of the bounds: points on the triangles of A (a 12 x 12 barycentric grid on each,
        /// computed exactly near 0 and moved by the exact offset), their distance to every triangle of B, the largest; the certified
        /// upper bound is not below it (up to its rounding), and the certified lower bound not above the exactly shifted reference's
        /// upper bound. Folded spheres at large offsets and spheres at scales 2^-20 .. 2^-40 at offsets </summary>
        [Theory]
        [InlineData(0, 52)] [InlineData(0, 40)] [InlineData(-20, 45)] [InlineData(-30, 50)] [InlineData(-40, 30)] [InlineData(-40, 52)]
        public void DenseSampling_InsideTheCertifiedInterval(int k, int m)
        {
            double r = Math.Pow(2, k), z = 1.3 * Math.Pow(2, k + m);
            z = Math.Round(z / Math.Pow(2, k - 3)) * Math.Pow(2, k - 3);
            DMesh3 a = Sphere(r, z), b = Sphere(1.01 * r, z);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = false, MaxSubdivisions = 100000 };
            Assert.True(h.Compute());
            DMesh3 sa = Shifted(a, z), sb = Shifted(b, z);
            double sample = 0;
            foreach (int tid in sa.TriangleIndices()) {
                Index3i t = sa.GetTriangle(tid);
                Vector3d p0 = sa.GetVertex(t.a), p1 = sa.GetVertex(t.b), p2 = sa.GetVertex(t.c);
                const int n = 12;
                for (int i = 0; i <= n; ++i)
                    for (int j = 0; i + j <= n; ++j) {
                        Vector3d q = p0 + (i / (double)n) * (p1 - p0) + (j / (double)n) * (p2 - p0);
                        double best = double.MaxValue;
                        foreach (int u in sb.TriangleIndices()) {
                            Index3i tb = sb.GetTriangle(u);
                            var dq = new DistPoint3Triangle3(q, new Triangle3d(sb.GetVertex(tb.a), sb.GetVertex(tb.b), sb.GetVertex(tb.c)));
                            best = Math.Min(best, dq.Get());
                        }
                        sample = Math.Max(sample, best);
                    }
            }
            var reference = new MeshHausdorffDistance(sa, sb) { Symmetric = false };
            Assert.True(reference.Compute());
            Assert.True(h.UpperBound >= sample * (1 - 1e-9), $"k {k} m {m}: upper {h.UpperBound:R} below the sampled {sample:R}");
            Assert.True(h.LowerBound <= reference.UpperBound, $"k {k} m {m}: lower {h.LowerBound:R} above {reference.UpperBound:R}");
            Assert.True(h.Converged, $"k {k} m {m}");
        }

        /// <summary> subdivision points of subnormal coordinates (measured as given): the halving of their sums rounds (eg 3 2^-1074 / 2),
        /// which the rounding allowance covers; the distance to a plane 1 above is exactly 1 for every point of the triangle </summary>
        [Fact]
        public void SubnormalMidpoints_AsGiven_Certified()
        {
            double u = double.Epsilon;
            var a = new DMesh3();
            a.AppendTriangle(a.AppendVertex(new Vector3d(0, 0, 0)), a.AppendVertex(new Vector3d(3 * u, 0, 0)), a.AppendVertex(new Vector3d(0, 5 * u, 0)));
            var b = new DMesh3();
            b.AppendTriangle(b.AppendVertex(new Vector3d(-1, -1, 1)), b.AppendVertex(new Vector3d(2, -1, 1)), b.AppendVertex(new Vector3d(-1, 2, 1)));
            foreach (double tol in new[] { 0.0, 1e-300 }) {
                var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, MaxSubdivisions = 20000 };
                AsGiven(h);
                Assert.True(h.Compute());
                Assert.True(h.LowerBound <= 1 && h.UpperBound >= 1, h.LowerBound + " .. " + h.UpperBound);
                Assert.True(h.Subdivisions < 20000, h.Subdivisions + " subdivisions");
            }
        }

        // A: the triangle (0, Y, 0), (0, Y + 1, 0), (1, Y, 0) at Y = 1.3 2^52 (an ulp is 1: every midpoint in y rounds onto Y or
        // Y + 1); B: three small triangles 0.1 above A's corners. A's points far from its corners are farther from B than its
        // corners, and their rounded copies (y moved onto Y or Y + 1) lie nearer to B than they are
        static (DMesh3 a, DMesh3 b) CornersOnly(double Y)
        {
            var a = new DMesh3();
            a.AppendTriangle(a.AppendVertex(new Vector3d(0, Y, 0)), a.AppendVertex(new Vector3d(0, Y + 1, 0)), a.AppendVertex(new Vector3d(1, Y, 0)));
            var b = new DMesh3();
            foreach (var c in new[] { new Vector3d(0, Y, 0), new Vector3d(0, Y + 1, 0), new Vector3d(1, Y, 0) })
                b.AppendTriangle(b.AppendVertex(c + new Vector3d(-0.0625, 0, 0.1)), b.AppendVertex(c + new Vector3d(0.0625, 0, 0.1)), b.AppendVertex(c + new Vector3d(0, 0, 0.125)));
            return (a, b);
        }

        /// <summary> the distance's maximum lies inside A, where (measured as given) the subdivision points round towards B: the upper
        /// bound, with the rounding allowance of the rounded triangles and the bounds of those not refined further, still contains
        /// the distance of the exactly shifted meshes; and the certified lower bound stays above 0 (the input vertices, off 0) </summary>
        [Fact]
        public void InteriorMaximum_RoundedTowardsB_UpperStillContainsIt()
        {
            double Y = 1.3 * Math.Pow(2, 52);
            var (a, b) = CornersOnly(Y);
            var reference = new MeshHausdorffDistance(Shifted(a, Y, 1), Shifted(b, Y, 1)) { Symmetric = false };
            Assert.True(reference.Compute() && reference.Converged);
            Assert.True(reference.LowerBound > 0.3, "the reference's distance " + reference.LowerBound);    // (inside A, far from the corners)
            foreach (double tol in new[] { 0.0, 0.05 }) {
                var given = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, MaxSubdivisions = 100000 };
                AsGiven(given);
                Assert.True(given.Compute());
                Assert.True(given.UpperBound >= reference.LowerBound, $"tol {tol}: upper {given.UpperBound:R} below the distance {reference.LowerBound:R}");
                Assert.True(given.LowerBound <= reference.UpperBound, $"tol {tol}: lower {given.LowerBound:R} above {reference.UpperBound:R}");
                Assert.True(given.LowerBound > 0, $"tol {tol}: lower bound 0");
                Assert.True(given.Subdivisions < 50000, given.Subdivisions + " subdivisions");
                // and with the copies it converges to the reference
                var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, MaxSubdivisions = 100000 };
                Assert.True(h.Compute());
                Assert.True(h.LowerBound <= reference.UpperBound && h.UpperBound >= reference.LowerBound, $"tol {tol}: {h.LowerBound:R} .. {h.UpperBound:R}");
            }
        }

        /// <summary> a tolerance above the rounding allowance (0.75 > 0.5) on the folded spheres, measured as given: a triangle whose
        /// children are itself (a needle whose corners differ by one ulp of z) is not refined again, so the run ends after few
        /// subdivisions </summary>
        [Fact]
        public void UnsplittableTriangle_NotRefinedForever()
        {
            double z = 1.3 * Math.Pow(2, 52);
            foreach (double tol in new[] { 0.6, 0.75 }) {
                var given = new MeshHausdorffDistance(Sphere(1, z), Sphere(1.01, z)) { Tolerance = tol, MaxSubdivisions = 100000 };
                AsGiven(given);
                var sw = Stopwatch.StartNew();
                Assert.True(given.Compute());
                Assert.True(given.Subdivisions < 50000, $"tol {tol}: {given.Subdivisions} subdivisions, {sw.ElapsedMilliseconds} ms");
                Assert.True(given.LowerBound <= 0.0101 && given.UpperBound >= 0.0099, $"tol {tol}: {given.LowerBound:R} .. {given.UpperBound:R}");
            }
        }

        /// <summary> the same, at offsets where an ulp is 2^-8 .. 2^-4 of A's size and with tolerance 0: the triangles are refined
        /// until their points round, then kept with their bounds, which must include the rounding allowance of their corners
        /// (without it they bound the rounded points only, up to an ulp below the distance) </summary>
        [Theory]
        [InlineData(44)] [InlineData(46)] [InlineData(48)]
        public void InteriorMaximum_SmallUlp_UpperIncludesTheAllowance(int m)
        {
            double Y = 1.3 * Math.Pow(2, m);
            Y = Math.Round(Y);
            var (a, b) = CornersOnly(Y);
            var reference = new MeshHausdorffDistance(Shifted(a, Y, 1), Shifted(b, Y, 1)) { Symmetric = false, Tolerance = 1e-9 };
            Assert.True(reference.Compute() && reference.Converged);
            var given = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 1e-12, MaxSubdivisions = 100000 };
            AsGiven(given);
            Assert.True(given.Compute());
            Assert.True(given.UpperBound >= reference.LowerBound, $"m {m}: upper {given.UpperBound:R} below the distance {reference.LowerBound:R}");
            Assert.True(given.LowerBound <= reference.UpperBound, $"m {m}: lower {given.LowerBound:R} above {reference.UpperBound:R}");
        }

        /// <summary> A: (0, Y, 0), (1, Y, 0), (0.5, Y + 1, 0), Y even at 2^52 (an ulp is 1, and Y + 0.5 rounds to Y); B: a strip 0.1
        /// under A's base and a small triangle 0.1 above its apex. The midpoints of the two slanted edges round onto y = Y, so three
        /// of the four children lie on the base, all nearest the strip, and their bound (the largest corner distance, 0.1) holds for
        /// the rounded triangles only: the exact children reach y = Y + 0.5, about 0.5 from B. The upper bound must add the
        /// corners' rounding allowance, and keep the bounds of the children not refined further </summary>
        [Fact]
        public void RoundedChildrenNearestOneTriangle_UpperIncludesTheAllowance()
        {
            double Y = 2 * Math.Round(1.3 * Math.Pow(2, 51));
            Assert.Equal(Y, Y + 0.5);                  // (rounds to even: Y)
            var a = new DMesh3();
            a.AppendTriangle(a.AppendVertex(new Vector3d(0, Y, 0)), a.AppendVertex(new Vector3d(1, Y, 0)), a.AppendVertex(new Vector3d(0.5, Y + 1, 0)));
            var b = new DMesh3();
            b.AppendTriangle(b.AppendVertex(new Vector3d(-0.5, Y, 0.1)), b.AppendVertex(new Vector3d(1.5, Y, 0.1)), b.AppendVertex(new Vector3d(0.5, Y, 0.125)));
            b.AppendTriangle(b.AppendVertex(new Vector3d(0.4, Y + 1, 0.1)), b.AppendVertex(new Vector3d(0.6, Y + 1, 0.1)), b.AppendVertex(new Vector3d(0.5, Y + 1, 0.125)));
            var reference = new MeshHausdorffDistance(Shifted(a, Y, 1), Shifted(b, Y, 1)) { Symmetric = false, Tolerance = 1e-9 };
            Assert.True(reference.Compute() && reference.Converged);
            Assert.True(reference.LowerBound > 0.4, "the reference's distance " + reference.LowerBound);
            foreach (double tol in new[] { 1e-12, 0.01, 0.3, 0.75 }) {
                var given = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, MaxSubdivisions = 100000 };
                AsGiven(given);
                Assert.True(given.Compute());
                Assert.True(given.UpperBound >= reference.LowerBound, $"tol {tol}: upper {given.UpperBound:R} below the distance {reference.LowerBound:R}");
                Assert.True(given.LowerBound <= reference.UpperBound, $"tol {tol}: lower {given.LowerBound:R} above {reference.UpperBound:R}");
                Assert.True(given.Subdivisions < 1000, $"tol {tol}: {given.Subdivisions} subdivisions");
            }
        }

        /// <summary> a needle (two corners the same point, the third one ulp of y away) between two small triangles of B, measured
        /// as given with a tolerance (0.75) above the rounding allowance of its midpoint (0.5): its children are itself, and it is
        /// kept instead of being refined until MaxSubdivisions </summary>
        [Fact]
        public void Needle_ItsOwnChild_NotRefinedForever()
        {
            double Y = 1.3 * Math.Pow(2, 52);
            var a = new DMesh3();
            int p = a.AppendVertex(new Vector3d(0, Y, 0)), q = a.AppendVertex(new Vector3d(0, Y + 1, 0));
            a.AppendTriangle(p, a.AppendVertex(new Vector3d(0, Y, 0)), q);
            var b = new DMesh3();
            foreach (var c in new[] { new Vector3d(0, Y, 0), new Vector3d(0, Y + 1, 0) })
                b.AppendTriangle(b.AppendVertex(c + new Vector3d(-0.0625, 0, 0.1)), b.AppendVertex(c + new Vector3d(0.0625, 0, 0.1)), b.AppendVertex(c + new Vector3d(0, 0, 0.125)));
            var given = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 0.75, MaxSubdivisions = 100000 };
            AsGiven(given);
            Assert.True(given.Compute());
            Assert.True(given.Subdivisions < 1000, given.Subdivisions + " subdivisions");
            Assert.True(given.UpperBound >= 0.5, "upper " + given.UpperBound);      // (the needle's middle is 0.5 from B's triangles in y)
        }

        /// <summary> under every similarity (folded spheres at 1.5 2^26, on the shifted copies): the result of the plain input mapped by
        /// it, bit for bit where the similarity is exact; otherwise the intervals agree up to the coordinates' rounding </summary>
        [Fact]
        public void FoldedSpheres_UnderEverySimilarity()
        {
            double z = 1.5 * Math.Pow(2, 26);
            var h0 = new MeshHausdorffDistance(Sphere(1, z), Sphere(1.01, z)) { MaxSubdivisions = 100000 };
            Assert.True(h0.Compute() && h0.Converged);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
                MeshTransforms.PerVertexTransform(a, s.Apply);
                MeshTransforms.PerVertexTransform(b, s.Apply);
                var h = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 100000 };
                Assert.True(h.Compute(), s.Name);
                Assert.True(h.Converged, s.Name);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    // (the coordinates near 1e8 round by up to 1.5e-8 in the mapped frame)
                    double eps = 1e-6;
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }

        /// <summary> on the shifted path: computing twice on one instance, a fresh instance and a serial run give the same result </summary>
        [Fact]
        public void ShiftedPath_Reuse_Serial_Same()
        {
            double z = 1.3 * Math.Pow(2, 52);
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            // (capped: the base ran into the cap here)
            var h = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 100000 };
            Assert.True(h.Compute());
            Assert.True(h.Converged && h.Subdivisions < 20000, h.Subdivisions + " subdivisions");
            double lo = h.LowerBound, up = h.UpperBound; int sub = h.Subdivisions; Vector3d w = h.WorstPoint;
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(sub, h.Subdivisions); Assert.Equal(w, h.WorstPoint);
            var fresh = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 100000 };
            Assert.True(fresh.Compute());
            Assert.Equal(lo, fresh.LowerBound); Assert.Equal(up, fresh.UpperBound);
            gParallel.SerialOnThisThread = true;
            try {
                var serial = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 100000 };
                Assert.True(serial.Compute());
                Assert.Equal(lo, serial.LowerBound); Assert.Equal(up, serial.UpperBound); Assert.Equal(sub, serial.Subdivisions);
            } finally { gParallel.SerialOnThisThread = false; }
        }
    }
}
