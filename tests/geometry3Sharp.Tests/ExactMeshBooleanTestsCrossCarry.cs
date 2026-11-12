using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// T5 PR 2 (docs/design/EXACT_BOOLEAN_T5_CROSS_CARRY.md): L2's tracing over clusters of edge-adjacent input triangles,
    /// the crossing count carried between pieces of different input triangles. The new filters are checked against exact
    /// values (the projection from the far point, the plane side with absolute errors, the hull prefilter for more than four
    /// points, the gradient bound per cluster); the classification against the pieces' own rays (verify mode) and against
    /// the tracing within each triangle, on open meshes whose triangles are mostly one piece each. (The class name keeps the
    /// mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsCrossCarry
    {
        // (exact values of doubles scaled by 2^S)
        const int S = 400;
        static BigInteger Q(double v)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            if (m.IsZero == false && e + S < 0)
                throw new ArgumentException("Q: " + v + " needs a larger scale S");
            return m.IsZero ? BigInteger.Zero : (m << (e + S));
        }

        // the double moved by f times the error the filters allow (2.3e-16 max_k |x_k| per coordinate), signs at random
        static BigInteger[] Moved(Vector3d x, double f, Random r)
        {
            BigInteger e = Q(f * 2.3e-16 * Math.Max(Math.Abs(x.x), Math.Max(Math.Abs(x.y), Math.Abs(x.z))));
            return new[] { Q(x.x) + (r.Next(2) == 0 ? e : -e), Q(x.y) + (r.Next(2) == 0 ? e : -e), Q(x.z) + (r.Next(2) == 0 ? e : -e) };
        }

        [Fact]
        public void Projection_BoxContainsTheExactProjectionOfPointsWithinTheirError()
        {
            // pi(x) = (x_i - R_i, x_j - R_j) / (x_k - R_k) for points of a box and a far point 4000 extents away (as
            // far_points places them), the points and R moved by up to their allowed error: the exact projection lies
            // within the computed one's bound. Coordinates near 2^48, not integers, so every subtraction rounds; some
            // points at the box's far corner, where |x_k - R_k| is smallest
            var r = new Random(5);
            int checks = 0;
            for (int trial = 0; trial < 400; ++trial) {
                double ext = Math.Pow(2, 20 + r.Next(28));
                var c = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * ext * 3;
                int axis = r.Next(3);
                var dir = new Vector3d(r.Next(-1000, 1001), r.Next(-1000, 1001), r.Next(-1000, 1001));
                dir[axis] = r.Next(2) == 0 ? -1000 : 1000;
                Vector3d R = c + 4 * ext * dir;
                var segs = new Vector3d[64];
                for (int q = 0; q < segs.Length; ++q) {
                    segs[q] = c + new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * ext;
                    if (q % 8 == 0)
                        segs[q][axis] = c[axis] + 0.5 * ext * Math.Sign(dir[axis]);
                }
                var pr = new ExactMeshBoolean.SegmentProjection(segs, R);
                Assert.True(pr.Valid);
                int k = pr.Axis, i = (k + 1) % 3, j = (k + 2) % 3;
                foreach (Vector3d x in segs) {
                    double px, py, pe;
                    Assert.True(pr.Project(x, out px, out py, out pe));
                    BigInteger[] X = Moved(x, 0.95, r), Rx = Moved(R, 0.95, r);
                    BigInteger d = X[k] - Rx[k];
                    foreach (var (n, v) in new[] { (X[i] - Rx[i], px), (X[j] - Rx[j], py) }) {
                        // n / d within [v - pe, v + pe], exactly: (v - pe) d <= n <= (v + pe) d for d > 0
                        BigInteger lo = Q(v - pe) * d, hi = Q(v + pe) * d, nn = n << S;
                        if (d.Sign < 0) { BigInteger t = lo; lo = hi; hi = t; }
                        Assert.True(lo <= nn && nn <= hi, $"trial {trial}: projection outside its bound");
                        checks++;
                    }
                }
            }
            Assert.True(checks > 10000);
        }

        [Fact]
        public void Projection_QueryReturnsEverySegmentThatMeetsTheHull()
        {
            // segments through a point of the hull of three corners and a far point R (a point on a segment from R to a
            // point of the triangle) must be returned by the query of the corners, wherever on the hull they meet it
            var r = new Random(7);
            for (int trial = 0; trial < 300; ++trial) {
                double ext = Math.Pow(2, 10 + r.Next(30));
                var R = new Vector3d(4000 * ext, r.Next(-1000, 1001) * 4 * ext, r.Next(-1000, 1001) * 4 * ext);
                Vector3d[] t = { Rnd(r, ext), Rnd(r, ext), Rnd(r, ext) };
                var segs = new List<Vector3d>();
                var through = new List<int>();
                for (int q = 0; q < 40; ++q) {
                    // (a point of the triangle, then towards R by l; the segment a short one through it, or ending at it)
                    double w0 = r.NextDouble(), w1 = r.NextDouble() * (1 - w0);
                    Vector3d y = t[0] + w0 * (t[1] - t[0]) + w1 * (t[2] - t[0]);
                    double l = r.NextDouble() < 0.5 ? 0 : r.NextDouble() * 0.9;
                    Vector3d x = (1 - l) * y + l * R;
                    Vector3d d = 1e-3 * ext * new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                    segs.Add(x - d); segs.Add(q % 3 == 0 ? x : x + d);
                    through.Add(2 * q);
                    // (and one far away from it)
                    segs.Add(x + 10 * ext * new Vector3d(0, 1, 1)); segs.Add(x + 10 * ext * new Vector3d(0, 1.1, 1));
                }
                var pr = new ExactMeshBoolean.SegmentProjection(segs.ToArray(), R);
                int[] found = pr.Query(t);
                Assert.NotNull(found);
                Assert.Equal(found.OrderBy(s => s).ToArray(), found);
                foreach (int s in through.Where(s => s % 4 == 0))
                    Assert.Contains(s, found);
                Assert.True(found.Length < segs.Count / 2, $"{found.Length} of {segs.Count / 2}");
            }
        }

        static Vector3d Rnd(Random r, double ext) { return new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * ext; }

        [Fact]
        public void PlaneSideAbs_IsTheExactSignAtPointsWithinTheError()
        {
            // a plane with integer coefficients (as InputTriangle.P) given by its doubles, points near it given by doubles
            // with an absolute error e: where plane_side_abs decides, it is the sign at every point within e, here at the
            // corners of the error box
            var r = new Random(11);
            int decided = 0, undecided = 0;
            for (int trial = 0; trial < 3000; ++trial) {
                var A = new BigInteger[] { r.Next(-1 << 20, 1 << 20), r.Next(-1 << 20, 1 << 20), r.Next(-1 << 20, 1 << 20) };
                var P0 = new BigInteger[] { r.Next(-1 << 24, 1 << 24), r.Next(-1 << 24, 1 << 24), r.Next(-1 << 24, 1 << 24) };
                BigInteger D = A[0] * P0[0] + A[1] * P0[1] + A[2] * P0[2];
                double[] pa = { (double)A[0], (double)A[1], (double)A[2], (double)D };
                double e = Math.Pow(2, -r.Next(40)) * (1 << 10);
                // (a point at a distance of the order of e from the plane, along its normal)
                double t = (r.NextDouble() - 0.5) * 8 * e / Math.Sqrt((double)(A[0] * A[0] + A[1] * A[1] + A[2] * A[2]));
                var x = new Vector3d((double)P0[0] + t * (double)A[0] + r.NextDouble(), (double)P0[1] + t * (double)A[1], (double)P0[2] + t * (double)A[2]);
                int s = ExactMeshBoolean.plane_side_abs(pa, x, e);
                if (s == 2) { undecided++; continue; }
                decided++;
                for (int c = 0; c < 8; ++c) {
                    BigInteger[] X = { Q(x.x + ((c & 1) != 0 ? e : -e)), Q(x.y + ((c & 2) != 0 ? e : -e)), Q(x.z + ((c & 4) != 0 ? e : -e)) };
                    int exact = (A[0] * X[0] + A[1] * X[1] + A[2] * X[2] - (D << S)).Sign;
                    Assert.Equal(s, exact);
                }
            }
            Assert.True(decided > 500 && undecided > 100, $"{decided} decided, {undecided} undecided");
        }

        [Fact]
        public void HullPrefilter_FivePoints_KeepsEverySegmentThatPiercesATriangleOfTwoCentroidsAndTheFarPoint()
        {
            // two input triangles sharing an edge (a, b, c) and (b, a, d), a far point R, the centroids p of the first and q
            // of the second; boundary segments that pierce the triangle (p, q, R) exactly must not be dropped. Integer
            // coordinates (scaled by 3, so that the centroids are integers); d also nearly in the plane of a, b, c (a
            // sliver hull); segments near the triangle's edges
            var r = new Random(13);
            int pierced = 0, dropped = 0;
            for (int trial = 0; trial < 400; ++trial) {
                double ext = Math.Pow(2, 16 + r.Next(20));
                Func<Vector3d> pt = () => new Vector3d(Math.Round((r.NextDouble() - 0.5) * ext), Math.Round((r.NextDouble() - 0.5) * ext), Math.Round((r.NextDouble() - 0.5) * ext));
                Vector3d a = pt(), b = pt(), c = pt(), d = pt();
                if (trial % 3 == 0) {
                    // (d by a little off the plane of a, b, c)
                    d = a + (b - a) * 0.3 - (c - a) * 0.6;
                    d = new Vector3d(Math.Round(d.x), Math.Round(d.y), Math.Round(d.z) + r.Next(-2, 3));
                }
                var R = new Vector3d(4000 * ext, Math.Round(r.Next(-1000, 1001) * 4 * ext), Math.Round(r.Next(-1000, 1001) * 4 * ext));
                Vector3d p3 = a + b + c, q3 = b + a + d;        // 3 p and 3 q
                var segs = new List<Vector3d>();
                for (int s = 0; s < 60; ++s) {
                    // (a point of the triangle (p, q, R), often near one of its edges, and a segment through it)
                    // (barycentric weights of p, q and R; R's small, since R is far; every other one near an edge)
                    double wr = 1e-6 * r.NextDouble() * (s % 4 == 1 ? 1e-6 : 1);
                    double w0 = r.NextDouble() * (1 - wr);
                    if (s % 4 == 3) w0 = (1 - wr) * (1 - 1e-7 * r.NextDouble());
                    double w1 = 1 - wr - w0;
                    Vector3d x = (w0 * p3 + w1 * q3) / 3 + wr * R;
                    Vector3d dir = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * ext * 0.1;
                    segs.Add(Round(x - dir)); segs.Add(Round(x + dir));
                }
                Vector3d[] v = { a, b, c, d, R };
                var keep = ExactMeshBoolean.pierce_prefilter(v, segs.ToArray()).ToHashSet();
                BigInteger[] P = I3(p3), Qc = I3(q3), RR = I3(3 * R);
                for (int s = 0; s < segs.Count; s += 2) {
                    BigInteger[] g0 = I3(3 * segs[s]), g1 = I3(3 * segs[s + 1]);
                    if (Pierces(P, Qc, RR, g0, g1) == false)
                        continue;
                    pierced++;
                    if (keep.Contains(s) == false) dropped++;
                }
            }
            Assert.True(pierced > 1000, $"{pierced} pierced");
            Assert.Equal(0, dropped);
        }

        static Vector3d Round(Vector3d x) { return new Vector3d(Math.Round(x.x), Math.Round(x.y), Math.Round(x.z)); }
        static BigInteger[] I3(Vector3d x) { return new[] { new BigInteger(x.x), new BigInteger(x.y), new BigInteger(x.z) }; }
        static int Orient(BigInteger[] a, BigInteger[] b, BigInteger[] c, BigInteger[] d)
        {
            BigInteger[] u = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, v = { c[0] - a[0], c[1] - a[1], c[2] - a[2] }, w = { d[0] - a[0], d[1] - a[1], d[2] - a[2] };
            return (u[0] * (v[1] * w[2] - v[2] * w[1]) - u[1] * (v[0] * w[2] - v[2] * w[0]) + u[2] * (v[0] * w[1] - v[1] * w[0])).Sign;
        }
        // the segment g0-g1 crosses the triangle (p, q, r) at an interior point, exactly
        static bool Pierces(BigInteger[] p, BigInteger[] q, BigInteger[] r, BigInteger[] g0, BigInteger[] g1)
        {
            int s0 = Orient(p, q, r, g0), s1 = Orient(p, q, r, g1);
            if (s0 == 0 || s1 == 0 || s0 == s1)
                return false;
            int t0 = Orient(g0, g1, p, q), t1 = Orient(g0, g1, q, r), t2 = Orient(g0, g1, r, p);
            return t0 != 0 && t0 == t1 && t1 == t2;
        }

        [Fact]
        public void HullPrefilter_DropsOnlySegmentsExactlyOutsideTheHull()
        {
            // the generalised prefilter on five and six points (two triangles sharing an edge or a corner, and a far
            // point): a dropped segment has both ends exactly strictly outside a plane through three of the points with
            // all others on its other side or in it, the doubles moved within their allowed error
            var r = new Random(17);
            int dropped = 0;
            for (int trial = 0; trial < 300; ++trial) {
                double ext = Math.Pow(2, 30 + r.Next(18));
                Vector3d a = Rnd(r, ext), b = Rnd(r, ext), c = Rnd(r, ext), d = Rnd(r, ext), e = Rnd(r, ext);
                var R = new Vector3d(4000 * ext, r.Next(-1000, 1001) * 4.0 * ext, r.Next(-1000, 1001) * 4.0 * ext);
                Vector3d[] v = trial % 2 == 0 ? new[] { a, b, c, d, R } : new[] { a, b, c, d, e, R };
                var segs = new Vector3d[60];
                for (int q = 0; q < segs.Length; q += 2) {
                    Vector3d x = v[r.Next(v.Length - 1)] + Rnd(r, ext) * 0.3;
                    segs[q] = x; segs[q + 1] = x + Rnd(r, ext) * 0.2;
                }
                var keep = ExactMeshBoolean.pierce_prefilter(v, segs).ToHashSet();
                var V = v.Select(x => Moved(x, 0.9, r)).ToArray();
                for (int q = 0; q < segs.Length; q += 2) {
                    if (keep.Contains(q)) continue;
                    dropped++;
                    Assert.True(ExactlyOutsideHull(V, Moved(segs[q], 0.9, r), Moved(segs[q + 1], 0.9, r)), $"trial {trial} segment {q}");
                }
            }
            Assert.True(dropped > 1000, $"{dropped} dropped");
        }

        static bool ExactlyOutsideHull(BigInteger[][] V, BigInteger[] p, BigInteger[] q)
        {
            int n = V.Length;
            for (int i = 0; i < n; ++i)
                for (int j = i + 1; j < n; ++j)
                    for (int k = j + 1; k < n; ++k) {
                        int sign = 0;
                        bool support = true;
                        for (int o = 0; o < n && support; ++o) {
                            if (o == i || o == j || o == k) continue;
                            int so = Orient(V[i], V[j], V[k], V[o]);
                            if (so == 0) continue;
                            support = sign == 0 || so == sign;
                            sign = so;
                        }
                        if (support && sign != 0 && Orient(V[i], V[j], V[k], p) == -sign && Orient(V[i], V[j], V[k], q) == -sign)
                            return true;
                    }
            return false;
        }

        [Fact]
        public void ClusterBound_IsAtLeastThePerSegmentBound_ForBoxesInTheCluster()
        {
            // the gradient bound with the segments split at a cluster's box C, for boxes inside C, never below the bound
            // summed over every segment; and finite where that one is finite and the box is not near a segment
            var r = new Random(19);
            int finite = 0, split = 0, infinite = 0;
            for (int trial = 0; trial < 400; ++trial) {
                double ext = Math.Pow(2, r.Next(-10, 40));
                var g = new Vector3d[2 * 200];
                for (int s = 0; s < g.Length; s += 2) {
                    g[s] = Rnd(r, ext) * 4; g[s + 1] = g[s] + Rnd(r, ext) * 0.05;
                }
                Vector3d c0 = Rnd(r, ext);
                double cs = ext * Math.Pow(2, -r.Next(1, 8));
                double[] C = { c0.x, c0.y, c0.z, c0.x + cs, c0.y + cs, c0.z + cs };
                if (trial % 2 == 0)
                    // (some segments just outside C, closer than its near distance 1e-3 s: they must be summed per box,
                    // where a box next to them gives an infinite bound)
                    for (int s = 0; s < 10; s += 2) {
                        double sz = Math.Max(Math.Abs(c0.x), Math.Max(Math.Abs(c0.y), Math.Abs(c0.z))) + cs;
                        g[s] = new Vector3d(c0.x + cs + 1e-4 * sz * r.NextDouble(), c0.y + r.NextDouble() * cs, c0.z + r.NextDouble() * cs);
                        g[s + 1] = g[s] + new Vector3d(0, 1e-3 * cs, 0);
                    }
                var b = ExactMeshBoolean.boundary_bound(g);
                var cb = ExactMeshBoolean.cluster_bound(C, b);
                if (cb.Near.Length < 200) split++;
                for (int q = 0; q < 20; ++q) {
                    double[] box = new double[6];
                    for (int k = 0; k < 3; ++k) {
                        double lo = C[k] + r.NextDouble() * cs, hi = lo + r.NextDouble() * (C[k + 3] - lo);
                        box[k] = lo; box[k + 3] = hi;
                    }
                    if (q % 4 == 0) {
                        // (a box at C's side next to the near segments)
                        box[0] = C[3] - 1e-6 * cs; box[3] = C[3];
                    }
                    double plain = ExactMeshBoolean.gradient_bound(box, b), withC = ExactMeshBoolean.gradient_bound(box, b, cb);
                    Assert.True(withC >= plain, $"trial {trial}: {withC} < {plain}");
                    if (double.IsInfinity(plain)) infinite++;
                    if (double.IsInfinity(plain) == false) {
                        finite++;
                        Assert.True(withC < double.PositiveInfinity);
                    }
                }
            }
            Assert.True(finite > 1000 && split > 100 && infinite > 100, $"{finite} finite, {split} split, {infinite} infinite");
        }

        [Theory]
        [InlineData("identity")]
        [InlineData("utm")]
        public void InputSegment_PadCoversTheExactCentroids_AlsoInAnotherTrianglesFrame(string name)
        {
            // the query segment of a step: both centroids from their corners in one frame (here also the frame of another
            // input triangle, as a step across an edge uses p's), mapped to input coordinates; each exact centroid in
            // input coordinates, (X / W + c) 2^s, lies within the pad of its double, exactly. On the near copy (pieces far
            // smaller than the triangles) and translated far from the origin (utm: the grid's centre c large)
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, 1e-12);
            a = Invariance.Transformed(a, sim); b = Invariance.Transformed(b, sim);
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var arr = s.Arrangement;
            int checks = 0, tight = 0;
            for (int i = 0; i + 1 < arr.Output.Count; i += 5) {
                // (the frame of the next piece's triangle, which is often another one)
                var frame = new ExactMeshBoolean.LocalFrame(s, arr.Triangles[arr.Output[i + 1].Source]);
                var cp = new ExactMeshBoolean.Center(s, arr.Output[i], frame);
                var cq = new ExactMeshBoolean.Center(s, arr.Output[i + 1], frame);
                double ap, aq, pad;
                Vector3d xp = cp.Approx(out ap), xq = cq.Approx(out aq), ip, iq;
                if (s.input_segment(xp, ap, xq, aq, out ip, out iq, out pad) == false)
                    continue;
                foreach (var (c, v) in new[] { (cp, ip), (cq, iq) }) {
                    ExactPoint3 x = c.P;
                    for (int k = 0; k < 3; ++k) {
                        // ((X_k + c_k W) 2^s / W against v_k +- pad, all scaled by 2^S)
                        BigInteger num = (x[k] + arr.Center[k] * x.W) << (S + arr.ScaleExp);
                        BigInteger lo = (Q(v[k]) - Q(pad)) * x.W, hi = (Q(v[k]) + Q(pad)) * x.W;
                        Assert.True(lo <= num && num <= hi, $"piece {i} coordinate {k}: {v[k]} +- {pad}");
                        BigInteger loT = (Q(v[k]) - Q(pad / 4)) * x.W, hiT = (Q(v[k]) + Q(pad / 4)) * x.W;
                        if (num < loT || num > hiT) tight++;
                        checks++;
                    }
                }
            }
            Assert.True(checks > 1000, $"{checks} checks");
            // (the bound is used: without the translation, some centroid's error exceeds a quarter of the pad)
            if (name == "identity")
                Assert.True(tight > 0, "no centroid near the pad");
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        public void CrossCarry_InconsistentlyOrientedSheet_SameThresholdsAsOwnRays(Ops op)
        {
            // the sheet with every seventh triangle flipped, which the boolean orients consistently first
            // (ReorientedTriangles): the steps between the reoriented triangles and their neighbours meet both pieces' own
            // triangles (the sheet's own winding number is evaluated at its pieces), decided from their corners
            // (interior_side), the same thresholds as the pieces' own rays
            (DMesh3 a, DMesh3 b) = SheetAndCap(24);
            foreach (int t in a.TriangleIndices().Where(t => t % 7 == 3).ToList())
                a.ReverseTriOrientation(t);
            ExactMeshBoolean own = Computed(a, b, op, false, false);
            ExactMeshBoolean across = Computed(a, b, op, true, true, verify: true);
            Assert.Equal(0, across.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, across.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], across.OpenWindingPerPiece[i]);
            Assert.True(across.Counts["l2_cross_steps"] > 500, $"{across.Counts["l2_cross_steps"]} cross steps");
            Assert.True(across.ReorientedTriangles > 0);
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        public void CrossCarry_FoldedFlaps_SameThresholdsAsOwnRays(Ops op)
        {
            // a flat sheet with flaps folded onto some of its triangles: a flap (b, a, d) on the edge a-b of the triangle
            // (a, b, c), d in its plane on c's side, so the flap lies in that plane with the opposite orientation. A step
            // between a piece of the triangle and one of the flap meets both pieces' own triangles with both ends in
            // their planes and opposite normals: degenerate, as the general test decides (review of #675: the shortcut
            // took the other end as in front and counted no crossing)
            (DMesh3 a, DMesh3 b) = SheetAndCap(16, 0);
            foreach (int t in a.TriangleIndices().Where(t => t % 9 == 4).ToList()) {
                Index3i f = a.GetTriangle(t);
                Vector3d pa = a.GetVertex(f.a), pb = a.GetVertex(f.b), pc = a.GetVertex(f.c);
                int d = a.AppendVertex(pa + 0.375 * (pb - pa) + 0.5 * (pc - pa));
                a.AppendTriangle(f.b, f.a, d);
            }
            ExactMeshBoolean own = Computed(a, b, op, false, false);
            ExactMeshBoolean across = Computed(a, b, op, true, true, verify: true);
            Assert.Equal(0, across.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, across.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], across.OpenWindingPerPiece[i]);
            Assert.True(across.Counts["l2_cross_fallbacks"] > 0, "no step met a folded flap");
        }

        // an open, gently curved sheet (a grid of n x n quads) and an open cap of a sphere through it: most of the sheet's
        // triangles are one piece each, as on the slow open models of the T5 count
        internal static (DMesh3, DMesh3) SheetAndCap(int n = 40, double amplitude = 0.15)
        {
            var sheet = new DMesh3();
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j) {
                    double x = (double)i / n * 2 - 1, y = (double)j / n * 2 - 1;
                    sheet.AppendVertex(new Vector3d(x, y, amplitude * Math.Sin(2.1 * x) * Math.Cos(1.7 * y)));
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    int v = i * (n + 1) + j;
                    sheet.AppendTriangle(v, v + n + 1, v + 1);
                    sheet.AppendTriangle(v + 1, v + n + 1, v + n + 2);
                }
            DMesh3 cap = new Sphere3Generator_NormalizedCube() { Radius = 0.45, EdgeVertices = 10 }.Generate().MakeDMesh();
            MeshTransforms.Translate(cap, new Vector3d(0.2, -0.1, 0.05));
            foreach (int t in cap.TriangleIndices().ToList())
                if (cap.GetTriCentroid(t).z < -0.25)
                    cap.RemoveTriangle(t, true, false);
            return (sheet, cap);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, bool traced, bool across, bool verify = false, int cluster = 1 << 14)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedOpenWinding = traced, TracedAcrossEdges = across, PerPatchOpenWinding = false,
                VerifyTracedOpenWinding = verify, RecordOpenWinding = true, TraceClusterPieces = cluster };
            Assert.True(s.Compute(), s.Error);
            return s;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                yield return new object[] { op };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void CrossCarry_SameThresholdsAsOwnRaysAndAsWithinTriangles(Ops op)
        {
            (DMesh3 a, DMesh3 b) = SheetAndCap();
            ExactMeshBoolean own = Computed(a, b, op, false, false);
            ExactMeshBoolean within = Computed(a, b, op, true, false);
            ExactMeshBoolean across = Computed(a, b, op, true, true, verify: true);
            Assert.All(across.TracedMismatchMargins, (m) => Assert.True(m <= 2e-6, $"margin {m}"));
            Assert.Equal(0, across.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, across.Result));
            Assert.Equal("", Invariance.Difference(within.Result, across.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], across.OpenWindingPerPiece[i]);
            // (the result: closed where the inputs give it, finite, the same mesh as without the lever)
            Assert.True(across.Result.TriangleCount > 0);
            foreach (int v in across.Result.VertexIndices())
                Assert.True(across.Result.GetVertex(v).IsFinite);
            // it carries across: the sheet's single-piece triangles reached by cross steps (the cap's larger blocks take
            // their own references, TraceCrossMaxPieces), fewer references than within triangles
            Assert.True(across.Counts["l2_cross_steps"] > 2000, $"{across.Counts["l2_cross_steps"]} cross steps");
            Assert.True(across.Counts["l2_direct"] * 3 < within.Counts["l2_direct"] * 2, $"{across.Counts["l2_direct"]} direct, within triangles {within.Counts["l2_direct"]}");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void CrossCarry_SameAsOwnRays_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = SheetAndCap(24);
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean own = Computed(ta, tb, Ops.Union, false, false), across = Computed(ta, tb, Ops.Union, true, true, verify: true);
            Assert.Equal(0, across.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, across.Result));
            if (sim.IsExact) {
                ExactMeshBoolean plain = Computed(a, b, Ops.Union, true, true);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(across.Result, sim)));
                foreach (string c in new[] { "l2_traced", "l2_carried", "l2_direct", "l2_cross_steps" })
                    Assert.Equal(plain.Counts[c], across.Counts[c]);
            }
        }

        [Fact]
        public void CrossCarry_SerialEqualsParallel_RepeatedReused_AnyClusterSize()
        {
            (DMesh3 a, DMesh3 b) = SheetAndCap(32);
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, Ops.Difference, true, true, cluster: 64);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true, PerPatchOpenWinding = false, TraceClusterPieces = 64, RecordOpenWinding = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute());
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                foreach (string c in new[] { "l2_traced", "l2_carried", "l2_direct", "l2_cross_steps", "l2_cross_fallbacks", "l2_clusters", "l2_exact_centroids" })
                    Assert.Equal(serial.Counts[c], reused.Counts[c]);
            }
            Assert.True(serial.Counts["l2_clusters"] > 10);
            // (another cap: other references, the same thresholds on this case; each value is an Antipodal evaluation)
            ExactMeshBoolean large = Computed(a, b, Ops.Difference, true, true);
            Assert.Equal("", Invariance.Difference(serial.Result, large.Result));
            for (int i = 0; i < large.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(large.OpenWindingPerPiece[i], serial.OpenWindingPerPiece[i]);
            Assert.True(large.Counts["l2_clusters"] < serial.Counts["l2_clusters"]);
        }
    }
}
