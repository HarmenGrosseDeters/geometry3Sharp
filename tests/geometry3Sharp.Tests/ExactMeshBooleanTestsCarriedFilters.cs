using System;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The filters of L2's carried tests on the centroids taken from their corners in the input triangle's frame
    /// (docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 3): orient3d_filtered_err_near, whose rows keep a short segment
    /// far from the two near points well conditioned, checked against exact signs at all corners of the absolute error
    /// boxes; and the classification, which must compute the exact centroid only where a filter fails. (The class name
    /// keeps the mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsCarriedFilters
    {
        // (the exact values scaled by 2^S: enough for the exponents of these tests' doubles, down to about 2^-200)
        const int S = 400;

        static BigInteger Q(double v)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            if (m.IsZero == false && e + S < 0)
                throw new ArgumentException("Q: " + v + " needs a larger scale S");
            return m.IsZero ? BigInteger.Zero : (m << (e + S));
        }

        // the sign that orient3d_filtered_err_near(p, q, a, b) certifies, for every exact input within the absolute error
        // e of each coordinate (the determinant is affine in each coordinate: the corners of the boxes are the worst case)
        static void AssertAtCorners(Vector3d[] v, double[] e, int f, string what)
        {
            var lo = new BigInteger[4, 3]; var hi = new BigInteger[4, 3];
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 3; ++j) {
                    BigInteger q = Q(v[i][j]), d = Q(e[i]);
                    lo[i, j] = q - d; hi[i, j] = q + d;
                }
            var X = new BigInteger[4, 3];
            for (int mask = 0; mask < 4096; ++mask) {
                for (int i = 0; i < 4; ++i)
                    for (int j = 0; j < 3; ++j)
                        X[i, j] = ((mask >> (3 * i + j)) & 1) == 0 ? lo[i, j] : hi[i, j];
                // det(q - p, a - p, b - p), orient3d's sign convention: -sign
                BigInteger ux = X[1, 0] - X[0, 0], uy = X[1, 1] - X[0, 1], uz = X[1, 2] - X[0, 2];
                BigInteger vx = X[2, 0] - X[0, 0], vy = X[2, 1] - X[0, 1], vz = X[2, 2] - X[0, 2];
                BigInteger wx = X[3, 0] - X[0, 0], wy = X[3, 1] - X[0, 1], wz = X[3, 2] - X[0, 2];
                BigInteger det = wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
                if (-det.Sign != f)
                    Assert.Fail($"{what} mask={mask}: filter {f}, exact {-det.Sign}");
            }
        }

        [Fact]
        public void Near_SignHoldsAtAllCornersOfTheErrorBoxes_AndDecidesWhereTheOtherFormCannot()
        {
            // p and q close together near the origin of the frame, a short segment a-b far away (as a boundary segment
            // against the edge p-q of L2's pierce test), b moved off the plane of p, q, a by growing powers of two: the
            // first offsets where the filter decides lie within a factor of about 2 of its bound
            var r = new Random(5);
            int decided = 0, otherFormDecided = 0;
            foreach (double far in new[] { 1e3, 1e8, 1e14 })
                foreach (double size in new[] { 1e-3, 1.0 })
                    for (int iter = 0; iter < 2; ++iter) {
                        Func<double, Vector3d> rnd = (k) => k * new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                        Vector3d p = rnd(size), q = p + rnd(size);
                        Vector3d a = far * new Vector3d(1, 0.3, -0.2) + rnd(1), s = rnd(1);
                        // (errors: the near points' as L2's centroids have them, the far ends' as the frame's rounding)
                        double ep = 2.3e-16 * 4 * size, eq = ep, ea = 2.3e-16 * 2 * far, eb = ea;
                        Vector3d n = (q - p).Cross(a - p).Normalized;
                        Vector3d b0 = a + s;
                        b0 -= n.Dot(b0 - p) * n;
                        int found = 0;
                        for (int j = -70; j <= 0 && found < 2; ++j) {
                            double sign = ((j + iter) % 2 == 0) ? 1 : -1;
                            Vector3d b = b0 + sign * Math.Pow(2, j) * n;
                            int f = ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref b, ep, eq, ea, eb);
                            if (f == 2)
                                continue;
                            AssertAtCorners(new[] { p, q, a, b }, new[] { ep, eq, ea, eb }, f, $"far={far} size={size} iter={iter} j={j}");
                            if (ExactMeshBoolean.orient3d_filtered_err(ref a, ref b, ref p, ref q, ea, eb, ep, eq) != 2)
                                otherFormDecided++;
                            found++;
                            decided++;
                        }
                    }
            Assert.True(decided > 18, $"{decided} decided");
            // (the point of the form: the other one decides only a few of these)
            Assert.True(otherFormDecided < decided / 2, $"{otherFormDecided} of {decided} also by the other form");
        }

        [Fact]
        public void PiercePrefilter_DropsOnlySegmentsExactlyOutsideAFace_AlsoForSliverFaces()
        {
            // the tetrahedron of a triangle a, b, c with c by 2^h off the line a-b (angles down to 2e-12 at coordinates
            // near 2^48, not integers, so the subtractions round) and a far point, half of the time exactly in the plane
            // of a, b, c; segments with an end 1e-8 S inside that plane and the other near it. The exact points differ
            // from the doubles by up to 0.9 of the error the filter allows (2.3e-16 max_k |x_k| per coordinate, signs at
            // random). A segment may be dropped only if both its ends lie exactly strictly outside one face. Review of
            // #633: the margin 1e-9 S |n|_1 alone dropped such segments for sliver faces, the rounding of n does not
            // shrink with |n|
            var r = new Random(2);
            int dropped = 0, kept = 0;
            Func<double> big = () => (r.NextDouble() * 2 - 1) * Math.Pow(2, 48);
            foreach (int h in new[] { 4, 12, 16, 24, 40 })
                for (int trial = 0; trial < 60; ++trial) {
                    // (odd trials: integer coordinates, so that the far point below is exactly in the plane of a, b, c)
                    bool integer = trial % 2 == 1;
                    Func<double, double> co = (x) => integer ? Math.Round(x) : x;
                    var a = new Vector3d(co(big()), co(big()), co(big()));
                    var d = new Vector3d(co(big()), co(big()), co(big()));
                    var b = a + d;
                    double k = r.NextDouble();
                    var c = new Vector3d(co(a.x + k * d.x) + Math.Pow(2, h), co(a.y + k * d.y), co(a.z + k * d.z));
                    var far = new Vector3d(3e16, 2.5e16, -4e16);
                    // (odd trials: the far point exactly in the plane of a, b, c, so that face's orientation is 0 and only
                    // the rounding decides its computed sign; below 2^53, exact)
                    var R = integer ? a + 2 * (b - a) - 3 * (c - a) : far;
                    Vector3d[] v = { a, b, c, R };
                    var n = (b - a).Cross(c - a).Normalized;
                    if (n.Dot(far - a) > 0) n = -n;
                    var segs = new Vector3d[40];
                    double size = 4e16 * 3;
                    for (int q = 0; q < segs.Length; q += 2) {
                        Vector3d onT = a + r.NextDouble() * (b - a);
                        segs[q] = onT - 1e-8 * size * n;
                        segs[q + 1] = onT + (r.NextDouble() * 1e-6) * size * n + (r.NextDouble() - 0.5) * 1e-3 * size * new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());
                    }
                    // (the exact points: each double moved by 0.9 of its allowed error per coordinate, signs at random;
                    // in the odd trials the doubles themselves, so that R stays in the plane)
                    Func<Vector3d, BigInteger[]> exact = (x) => {
                        if (integer) return new[] { Q(x.x), Q(x.y), Q(x.z) };
                        BigInteger e = Q(0.9 * 2.3e-16 * Math.Max(Math.Abs(x.x), Math.Max(Math.Abs(x.y), Math.Abs(x.z))));
                        return new[] { Q(x.x) + (r.Next(2) == 0 ? e : -e), Q(x.y) + (r.Next(2) == 0 ? e : -e), Q(x.z) + (r.Next(2) == 0 ? e : -e) };
                    };
                    var V = v.Select(exact).ToArray();
                    var G = segs.Select(exact).ToArray();
                    var keep = ExactMeshBoolean.pierce_prefilter(v, segs).ToHashSet();
                    for (int q = 0; q < segs.Length; q += 2) {
                        if (keep.Contains(q)) { kept++; continue; }
                        dropped++;
                        Assert.True(ExactlyOutsideAFace(V, G[q], G[q + 1]), $"h={h} trial={trial} segment {q}: dropped, not exactly outside a face");
                    }
                }
            Assert.True(kept > 0);
            Assert.True(dropped > 100, $"{dropped} dropped");
        }

        // both ends strictly outside one face of the tetrahedron V, exactly
        static bool ExactlyOutsideAFace(BigInteger[][] V, BigInteger[] p, BigInteger[] q)
        {
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 0, 1, 3, 2 }, new[] { 1, 2, 3, 0 }, new[] { 2, 0, 3, 1 } };
            foreach (var f in faces) {
                BigInteger[] a = V[f[0]], b = V[f[1]], c = V[f[2]], o = V[f[3]];
                BigInteger[] u = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, w = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
                BigInteger[] n = { u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0] };
                Func<BigInteger[], int> side = (x) => (n[0] * (x[0] - a[0]) + n[1] * (x[1] - a[1]) + n[2] * (x[2] - a[2])).Sign;
                int so = side(o);
                if (so == 0) continue;
                if (side(p) == -so && side(q) == -so)
                    return true;
            }
            return false;
        }

        [Fact]
        public void AntipodalValue_WithAPointError_BoundsTheValueAtTheDisplacedPoint()
        {
            // the boundary sum at p with pointError d must bound the value at every point within d of p (the same ray
            // direction x1, so the crossing count is the same where neither evaluation is at the branch cut): an open
            // square boundary, p near its plane, d far above the rounding (1e-9 to 1e-6), so a bound without d fails
            var segments = new[] {
                new Vector3d(-1, -1, 0), new Vector3d(1, -1, 0), new Vector3d(1, -1, 0), new Vector3d(1, 1, 0),
                new Vector3d(1, 1, 0), new Vector3d(-1, 1, 0), new Vector3d(-1, 1, 0), new Vector3d(-1, -1, 0) };
            var r = new Random(4);
            var x1 = new Vector3d(0.2, 0.3, 1).Normalized;
            int compared = 0;
            foreach (double d in new[] { 1e-9, 1e-8, 1e-7, 1e-6 })
                for (int k = 0; k < 200; ++k) {
                    var p = new Vector3d(1.6 * r.NextDouble() - 0.8, 1.6 * r.NextDouble() - 0.8, 0.02 * (r.NextDouble() - 0.5));
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    double w, bound, rb;
                    if (ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segments, out w, out bound, out rb, 1.7320508075688774 * d, 0, 1e-4) != 0)
                        continue;
                    var dir = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5).Normalized;
                    var q = p + d * dir;
                    double wq, bq, rq;
                    if (ExactMeshBoolean.antipodal_value(q, Math.Abs(q.x) + Math.Abs(q.y) + Math.Abs(q.z), x1, 0, segments, out wq, out bq, out rq, 0, 0, 1e-6) != 0)
                        continue;
                    Assert.True(Math.Abs(w - wq) <= bound + bq, $"d={d}: |{w} - {wq}| > {bound} + {bq}");
                    // (and the bound without the point's error is that of the point itself, within the factor 1.001)
                    double w0, b0, r0;
                    if (ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segments, out w0, out b0, out r0, 0, 0, 1e-6) == 0)
                        Assert.True(rb <= 1.001 * b0 && b0 <= 1.001 * rb, $"{rb} against {b0}");
                    compared++;
                }
            Assert.True(compared > 400, $"{compared} compared");
        }

        [Fact]
        public void Near_ExactlyCoplanar_Undecided()
        {
            // four points in one plane (z = 3, exactly): the determinant is 0, which no filter may decide
            var p = new Vector3d(0.25, 0.5, 3); var q = new Vector3d(0.75, 0.125, 3);
            var a = new Vector3d(1e10, 3e9, 3); var b = new Vector3d(1e10 + 2, 3e9 - 1, 3);
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref b, 1e-16, 1e-16, 1e-6, 1e-6));
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref b, 0, 0, 0, 0));
            // NaN, infinite and negative errors: undecided
            var nan = new Vector3d(double.NaN, 0, 0);
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref nan, 0, 0, 0, 0));
            var off = new Vector3d(1e10 + 2, 3e9 - 1, 4);
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref off, -1, 0, 0, 0));
            Assert.NotEqual(2, ExactMeshBoolean.orient3d_filtered_err_near(ref p, ref q, ref a, ref off, 1e-16, 1e-16, 1e-6, 1e-6));
        }

        [Fact]
        public void LocalCentroid_AndItsGridCoordinates_WithinTheirBounds()
        {
            // every centroid taken from its corners in the triangle's frame, and its grid coordinates, against the exact
            // centroid, exactly: |x_k - X_k / W| <= e per coordinate. On the near copy (fine grid, coordinates far from
            // the origin, pieces far smaller than the triangles) the roundings are as large as the bounds allow
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, 1e-12);
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var arr = s.Arrangement;
            int checkedPieces = 0, tight = 0;
            ExactMeshBoolean.LocalFrame frame = null;
            int source = -1;
            for (int i = 0; i < arr.Output.Count; i += 7) {
                var piece = arr.Output[i];
                if (piece.Source != source) {
                    source = piece.Source;
                    frame = new ExactMeshBoolean.LocalFrame(s, arr.Triangles[source]);
                }
                var c = new ExactMeshBoolean.Center(s, piece, frame);
                double le, ge;
                Vector3d l = c.Local(out le), g = c.Approx(out ge);
                ExactPoint3 x = c.P, o = arr.Exact(arr.Triangles[source].Corners[0]);
                if (double.IsNaN(l.x + l.y + l.z))
                    continue;
                for (int k = 0; k < 3; ++k) {
                    // (exact rationals: X_k / W - o_k against l_k +- le, and X_k / W against g_k +- ge, all scaled by 2^S W)
                    BigInteger xk = x[k] << S, ok = (o[k] * x.W) << S;
                    Assert.True(Within(xk - ok, x.W, l[k], le), $"piece {i} local {k}: {l[k]} +- {le}");
                    Assert.True(Within(xk, x.W, g[k], ge), $"piece {i} grid {k}: {g[k]} +- {ge}");
                    // (the bound is used: the rounding is more than a tenth of it somewhere)
                    if (!Within(xk - ok, x.W, l[k], le / 10)) tight++;
                }
                checkedPieces++;
            }
            Assert.True(checkedPieces > 200, $"{checkedPieces} pieces");
            Assert.True(tight > 0, "no rounding near the bound");
        }

        // v / w (v scaled by 2^S, w > 0) within e of d, exactly
        static bool Within(BigInteger v, BigInteger w, double d, double e)
        {
            return (Q(d) - Q(e)) * w <= v && v <= (Q(d) + Q(e)) * w;
        }

        [Theory]
        [InlineData(1e-12, Ops.Union)]
        [InlineData(1e-9, Ops.Difference)]
        public void Traced_DenseNearCopy_SameThresholdsAsOwnRays(double angle, Ops op)
        {
            // a knot and a copy rotated by a tiny angle (ExactMeshBooleanTestsLocalFilter.NearCopy): the pieces of a
            // triangle lie far closer together than the approximations' absolute errors, so the carried tests decide on
            // the centroids from their corners in the triangle's frame, near the limit of their error bounds; every traced
            // value is compared with the piece's own ray (verify mode)
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, angle);
            var own = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = false, RecordOpenWinding = true };
            Assert.True(own.Compute(), own.Error);
            var traced = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, RecordOpenWinding = true, VerifyTracedOpenWinding = true };
            Assert.True(traced.Compute(), traced.Error);
            Assert.All(traced.TracedMismatchMargins, (m) => Assert.True(m <= 2e-6, $"margin {m}"));
            Assert.Equal("", Invariance.Difference(own.Result, traced.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], traced.OpenWindingPerPiece[i]);
            Assert.True(traced.Counts["l2_carried"] > 0);
        }

        [Theory]
        [InlineData("knot", Ops.Difference)]
        [InlineData("near a hole", Ops.Union)]
        [InlineData("open spheres", Ops.Intersection)]
        public void Traced_ExactCentroidsOnlyWhereAFilterFails_SameResult(string name, Ops op)
        {
            // the traced classification takes the centroids from their corners; the exact centroid is computed for few
            // pieces (the pieces' own rays, the exact stages), and the thresholds are those of the pieces' own rays
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsTraced.Inputs(name);
            var own = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = false, RecordOpenWinding = true };
            Assert.True(own.Compute(), own.Error);
            var traced = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, RecordOpenWinding = true };
            Assert.True(traced.Compute(), traced.Error);
            Assert.Equal("", Invariance.Difference(own.Result, traced.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], traced.OpenWindingPerPiece[i]);
            long pieces = traced.Counts["l2_traced"] + traced.Counts["l2_carried"] + traced.Counts["l2_direct"];
            long exact = traced.Counts["l2_exact_centroids"];
            Assert.True(exact > 0 && exact * 4 < pieces, $"{exact} exact centroids for {pieces} traced pieces");
        }
    }
}
