using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #742: the certified bounds of MeshHausdorffDistance.CertifiedUpper (CertifiedTriangleBounds, TriangleDeviationBounds' certified
    /// clip), each against exact arithmetic (ExactDyadic, BigInteger): never below the exact value, NaN as +infinity.
    /// docs/design/HAUSDORFF_CERTIFIED_UPPER.md
    /// </summary>
    public class CertifiedTriangleBoundsTests
    {
        static ExactDyadic D(double x) => ExactDyadic.Of(x);

        static DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
            return m;
        }

        // a random double of magnitude 2^e (either sign)
        static double R(Random rnd, int e) => (rnd.NextDouble() + 0.5) * Math.Pow(2, e) * (rnd.Next(2) == 0 ? 1 : -1);

        [Fact]
        public void K_IsOneOverSqrt3_RoundedUp()
        {
            ExactDyadic k = D(CertifiedTriangleBounds.K), below = D(ExactInterval.Down(CertifiedTriangleBounds.K));
            Assert.True(ExactDyadic.Compare(D(3) * k * k, D(1)) >= 0);
            // (the nearest double: the one below is too small)
            Assert.True(ExactDyadic.Compare(D(3) * below * below, D(1)) < 0);
        }

        /// <summary> the clamp gives s, t >= 0 with s + t <= 1 exactly: barycentrics near the edge s + t = 1 at every exponent, NaN </summary>
        [Fact]
        public void Clamp_SumAtMostOne_Exactly()
        {
            var rnd = new Random(7);
            var cases = new List<(double, double)> { (double.NaN, 0.5), (0.5, double.NaN), (2, 3), (-1, 2), (1, 1e-300), (0.5, 0.5),
                (0.75, 0.25), (1e-17, 1), (0.3, 0.7), (0.1, 0.9) };
            for (int i = 0; i < 20000; ++i) {
                double s = Math.Pow(2, -rnd.Next(0, 60)) * rnd.NextDouble();
                double t = 1 - s;
                // (around the edge, by a few ulps either way)
                long bits = BitConverter.DoubleToInt64Bits(t) + rnd.Next(-4, 5);
                cases.Add((s, BitConverter.Int64BitsToDouble(bits)));
                cases.Add((BitConverter.Int64BitsToDouble(bits), s));
            }
            foreach (var (w1, w2) in cases) {
                CertifiedTriangleBounds.Clamp(w1, w2, out double s, out double t);
                Assert.True(s >= 0 && t >= 0 && s <= 1, $"{w1:R} {w2:R}");
                Assert.True(ExactDyadic.Compare(D(s) + D(t), D(1)) <= 0, $"{w1:R} {w2:R}: {s:R} + {t:R} > 1");
            }
        }

        /// <summary> B1 is never below the exact distance (exact integer point-triangle distance): random triangles and points at
        /// exponents from the subnormal range to 2^500, points at heights 1e-1 .. 1e-300 above a triangle's interior, offset
        /// coordinates; and tight for ordinary inputs </summary>
        [Fact]
        public void PointTriangleUpper_NeverBelowTheExactDistance()
        {
            var rnd = new Random(11);
            int n = 0;
            foreach (int e in new[] { -1070, -1040, -600, -60, -20, 0, 20, 300, 500 })
                for (int i = 0; i < 60; ++i) {
                    Vector3d v0 = new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e)), v1 = new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e)),
                        v2 = new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e)), x = new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e));
                    check(x, v0, v1, v2); n++;
                }
            foreach (double off in new[] { 0.0, 1e3, 5.7e6, 1.3 * Math.Pow(2, 52) })
                foreach (double h in new[] { 1e-1, 1e-3, 1e-6, 1e-9, 1e-12, 1e-300 })
                    for (int i = 0; i < 25; ++i) {
                        Vector3d v0 = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) + off;
                        Vector3d v1 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                        Vector3d v2 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                        Vector3d nrm = (v1 - v0).Cross(v2 - v0).Normalized;
                        Vector3d x = v0 + 0.3 * (v1 - v0) + 0.3 * (v2 - v0) + h * nrm;
                        double u = check(x, v0, v1, v2); n++;
                        // tight: within a few ulps of the coordinates of the height (no 2^-26 E cancellation, which the float
                        // distance has: off by 1e-10 at h = 1e-6, #742)
                        double ulp = Math.Pow(2, -52) * Math.Max(1, off + 2);
                        // (plus the float barycentrics' error times the triangle's condition, first order: about 2^-52 E / sin of its
                        // smallest angle; 1e-11 for these random triangles of size 1, against the 2^-26 E = 1.5e-8 of the cancellation)
                        Assert.True(u <= h * (1 + 1e-12) + 64 * ulp + 1e-11, $"off {off} h {h}: {u:R}");
                    }
            Assert.True(n > 1000);
        }

        static double check(Vector3d x, Vector3d v0, Vector3d v1, Vector3d v2)
        {
            Triangle3d tri = new Triangle3d(v0, v1, v2);
            DistPoint3Triangle3.DistanceSqr(ref x, ref tri, out Vector3d closest, out Vector3d bary);
            double u = CertifiedTriangleBounds.PointTriangleUpper(x, v0, v1, v2, bary.y, bary.z);
            Assert.True(gs.CertifiedPointMeshDistance.AtLeastDistance(Tri(v0, v1, v2), x, u), $"{x} to ({v0}, {v1}, {v2}): {u:R} below the distance");
            // any barycentrics give a valid bound too (the corners, NaN, out of range)
            foreach (var (w1, w2) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (double.NaN, double.NaN), (0.9, 0.9), (-3.0, 7.0) }) {
                double uw = CertifiedTriangleBounds.PointTriangleUpper(x, v0, v1, v2, w1, w2);
                Assert.True(gs.CertifiedPointMeshDistance.AtLeastDistance(Tri(v0, v1, v2), x, uw), $"bary ({w1}, {w2}): {uw:R}");
            }
            return u;
        }

        [Fact]
        public void NaN_IsInfinity()
        {
            Vector3d nan = new Vector3d(double.NaN, 0, 0), o = Vector3d.Zero, ax = Vector3d.AxisX, ay = Vector3d.AxisY;
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.PointTriangleUpper(nan, o, ax, ay, 0.2, 0.2));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.PointTriangleUpper(o, nan, ax, ay, 0.2, 0.2));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.LengthUpper(nan, o));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.CoveringUpper(nan, ax, ay, double.NaN, 1, 1));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.CutRounding(nan, ax, 0.5, nan));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.max(1, double.NaN));
            Assert.Equal(double.PositiveInfinity, CertifiedTriangleBounds.up(double.NaN));
            var piece = new TriangleDeviationBounds.Piece(3);
            piece.Add(o, 0); piece.Add(ax, 0); piece.Add(nan, 0);
            Assert.False(TriangleDeviationBounds.certified_clip(piece, new Vector3d(0.5, 0, 0), Vector3d.AxisX, out _, out _));
        }

        /// <summary> B3: the rounding allowance of a cut point covers its exact distance from a + lambda (b - a) (L1, exact) </summary>
        [Fact]
        public void CutRounding_CoversTheExactError()
        {
            var rnd = new Random(13);
            foreach (int e in new[] { -1070, -1050, -300, -30, 0, 30, 52, 400 })
                for (int i = 0; i < 400; ++i) {
                    Vector3d a = new Vector3d(R(rnd, e), R(rnd, e), R(rnd, e)), b = a + new Vector3d(R(rnd, e - rnd.Next(0, 40)), R(rnd, e), R(rnd, e));
                    double lambda = (i % 7 == 0) ? 1 : (i % 11 == 0) ? 0 : rnd.NextDouble();
                    Vector3d x0 = a + lambda * (b - a);
                    // the stored point as computed, and moved a few ulps up and down per component (the allowance must hold on
                    // both sides of the enclosure, not only for a point near its middle)
                    int shift = (i % 3 == 0) ? 0 : (i % 3 == 1) ? 4 : -4;
                    Vector3d x = x0;
                    for (int k = 0; k < 3; ++k) {
                        double v = x0[k];
                        for (int s = 0; s < Math.Abs(shift); ++s) v = shift > 0 ? ExactInterval.Up(v) : ExactInterval.Down(v);
                        x[k] = v;
                    }
                    double rho = CertifiedTriangleBounds.CutRounding(a, b, lambda, x);
                    ExactDyadic sum = D(0);
                    for (int k = 0; k < 3; ++k) {
                        ExactDyadic err = D(x[k]) - (D(a[k]) + D(lambda) * (D(b[k]) - D(a[k])));
                        sum = sum + (err.Sign < 0 ? -err : err);
                    }
                    Assert.True(ExactDyadic.Compare(sum, D(rho)) <= 0, $"e {e} shift {shift}: rho {rho:R}");
                }
        }

        /// <summary> B3: a cut point stands for the exact point at the same parameter between its parents' exact points, which are within
        /// their off of the stored ones: its off covers (1 - lambda) off_a + lambda off_b, with the large off at either end </summary>
        [Fact]
        public void CutPoint_Off_CoversBothParents()
        {
            var rnd = new Random(37);
            for (int it = 0; it < 400; ++it) {
                var piece = new TriangleDeviationBounds.Piece(3);
                double big = 1e-3 * (1 + rnd.NextDouble());
                // (off 0 at one corner of the cut edges, a large off at the other; which end alternates)
                bool first = it % 2 == 0;
                piece.Add(new Vector3d(0, 0, 0), first ? big : 0);
                piece.Add(new Vector3d(1, 0, 0), first ? 0 : big);
                piece.Add(new Vector3d(0, 1, 0), first ? 0 : big);
                double[] dist = { -(0.1 + rnd.NextDouble()), 0.1 + rnd.NextDouble(), 0.1 + rnd.NextDouble() };
                TriangleDeviationBounds.split(piece, dist, out var pos, out var neg);
                int cuts = 0;
                for (int k = 0; k < 3; ++k) {
                    int k1 = (k + 1) % 3;
                    double da = dist[k], db = dist[k1];
                    if (!((da > 0 && db < 0) || (da < 0 && db > 0))) continue;
                    double lambda = da / (da - db);
                    Vector3d c = piece.P[k] + lambda * (piece.P[k1] - piece.P[k]);
                    int j = pos.P.IndexOf(c);
                    Assert.True(j >= 0 && neg.P.Contains(c));
                    ExactDyadic need = (D(1) - D(lambda)) * D(piece.Off[k]) + D(lambda) * D(piece.Off[k1]);
                    Assert.True(ExactDyadic.Compare(D(pos.Off[j]), need) >= 0, $"it {it}: off {pos.Off[j]:R} for lambda {lambda:R}");
                    cuts++;
                }
                Assert.Equal(2, cuts);
            }
        }

        /// <summary> B3: the two pieces of the certified clip cover the piece: random triangles and planes, planes through or near a
        /// corner (zero and near-zero signs). Points of the triangle (exact orientation tests) lie in one of the pieces whose true
        /// vertices are the corners and the exact cut points; each stored cut point is within its off of the exact one </summary>
        [Fact]
        public void CertifiedClip_PiecesCoverThePiece()
        {
            var rnd = new Random(17);
            int covered = 0;
            for (int it = 0; it < 400; ++it) {
                double sc = Math.Pow(2, rnd.Next(-30, 30));
                Vector3d p0 = sc * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), 0), p1 = sc * new Vector3d(1 + rnd.NextDouble(), rnd.NextDouble(), 0),
                    p2 = sc * new Vector3d(rnd.NextDouble(), 1 + rnd.NextDouble(), 0);
                if (orient(p0, p1, p2).Sign <= 0) { Vector3d t = p1; p1 = p2; p2 = t; }
                Vector3d[] corners = { p0, p1, p2 };
                double ang = rnd.NextDouble() * Math.PI * 2;
                Vector3d normal = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0);
                Vector3d origin = (it % 3 == 0) ? corners[it % 3 == 0 ? rnd.Next(3) : 0]
                    : (it % 3 == 1) ? corners[rnd.Next(3)] + sc * 1e-15 * normal : sc * new Vector3d(0.3 + rnd.NextDouble(), 0.3 + rnd.NextDouble(), 0);
                var piece = new TriangleDeviationBounds.Piece(3);
                foreach (Vector3d c in corners) piece.Add(c, 0);
                Assert.True(TriangleDeviationBounds.certified_clip(piece, origin, normal, out var pos, out var neg));
                var truePos = exact_vertices(pos, corners, origin, normal);
                var trueNeg = exact_vertices(neg, corners, origin, normal);
                for (int k = 0; k < 40; ++k) {
                    Vector3d y = p0 + rnd.NextDouble() * (p1 - p0) + rnd.NextDouble() * (p2 - p0);
                    if (!inside(new[] { D(p0.x), D(p1.x), D(p2.x) }, new[] { D(p0.y), D(p1.y), D(p2.y) }, y)) continue;
                    Assert.True(inside(truePos, y) || inside(trueNeg, y), $"it {it}: {y} in neither piece");
                    covered++;
                }
            }
            Assert.True(covered > 4000);
        }

        /// <summary> B3 at depth 2: a piece with rounded cut points (nonzero off) clipped again. Each new cut point is within its off of the
        /// exact point on the exact edge (the parents' exact points, at the clip's parameter); the two exact pieces cover the exact piece
        /// (exact dyadic sample points: combinations of its exact vertices with dyadic weights) </summary>
        [Fact]
        public void CertifiedClip_DepthTwo_OffAndCoverage()
        {
            var rnd = new Random(29);
            int checkedPieces = 0;
            for (int it = 0; it < 300; ++it) {
                double sc = Math.Pow(2, rnd.Next(-30, 30));
                Vector3d p0 = sc * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), 0), p1 = sc * new Vector3d(1 + rnd.NextDouble(), rnd.NextDouble(), 0),
                    p2 = sc * new Vector3d(rnd.NextDouble(), 1 + rnd.NextDouble(), 0);
                if (orient(p0, p1, p2).Sign <= 0) { Vector3d t = p1; p1 = p2; p2 = t; }
                var root = new TriangleDeviationBounds.Piece(3);
                root.Add(p0, 0); root.Add(p1, 0); root.Add(p2, 0);
                var rootExact = new List<(ExactDyadic, ExactDyadic)> { (D(p0.x), D(p0.y)), (D(p1.x), D(p1.y)), (D(p2.x), D(p2.y)) };
                Vector3d n1 = unit(rnd), o1 = sc * new Vector3d(0.4 + 0.4 * rnd.NextDouble(), 0.4 + 0.4 * rnd.NextDouble(), 0);
                Assert.True(TriangleDeviationBounds.certified_clip(root, o1, n1, out var a, out var b));
                foreach (var (piece, pieceExact) in new[] { (a, clip_exact(root, rootExact, o1, n1, a)), (b, clip_exact(root, rootExact, o1, n1, b)) }) {
                    if (piece.Count < 3) continue;
                    // a second plane through a point near the piece's centre, or through one of its cut points
                    Vector3d o2 = (it % 2 == 0) ? piece.P[rnd.Next(piece.Count)] : sc * new Vector3d(0.5 + 0.2 * rnd.NextDouble(), 0.5 + 0.2 * rnd.NextDouble(), 0);
                    Vector3d n2 = unit(rnd);
                    Assert.True(TriangleDeviationBounds.certified_clip(piece, o2, n2, out var c, out var d));
                    var ce = clip_exact(piece, pieceExact, o2, n2, c);
                    var de = clip_exact(piece, pieceExact, o2, n2, d);
                    assert_covered(pieceExact, ce, de, rnd, $"it {it}");
                    checkedPieces++;
                }
            }
            Assert.True(checkedPieces > 300);
        }

        /// <summary> B3 with signs that no plane gives: the split of convex polygons (up to 8 points, on a circle) by arbitrary signed
        /// distances (zeros, alternating signs); the pieces still cover the polygon (exact) </summary>
        [Fact]
        public void Split_AnySignPattern_Covers()
        {
            var rnd = new Random(31);
            int alternating = 0;
            for (int it = 0; it < 600; ++it) {
                int n = 3 + rnd.Next(6);
                var angles = new List<double>();
                for (int k = 0; k < n; ++k) angles.Add(rnd.NextDouble() * 2 * Math.PI);
                angles.Sort();
                Vector3d[] q = new Vector3d[n];
                for (int k = 0; k < n; ++k) q[k] = new Vector3d(Math.Cos(angles[k]), Math.Sin(angles[k]), 0);
                if (!strictly_convex(q)) continue;
                var piece = new TriangleDeviationBounds.Piece(n);
                var exact = new List<(ExactDyadic, ExactDyadic)>();
                foreach (Vector3d p in q) { piece.Add(p, 0); exact.Add((D(p.x), D(p.y))); }
                double[] dist = new double[n];
                int changes = 0;
                for (int k = 0; k < n; ++k) dist[k] = (rnd.Next(5) == 0) ? 0 : (rnd.NextDouble() - 0.5) * Math.Pow(2, -rnd.Next(0, 60));
                for (int k = 0; k < n; ++k) if (dist[k] * dist[(k + 1) % n] < 0) changes++;
                if (changes > 2) alternating++;
                TriangleDeviationBounds.split(piece, dist, out var pos, out var neg);
                assert_covered(exact, split_exact(piece, exact, dist, pos), split_exact(piece, exact, dist, neg), rnd, $"it {it}");
            }
            Assert.True(alternating > 100, alternating + " patterns no plane gives");
        }

        // clip_exact for given distances
        static List<(ExactDyadic, ExactDyadic)> split_exact(TriangleDeviationBounds.Piece parent, List<(ExactDyadic, ExactDyadic)> parentExact,
            double[] dist, TriangleDeviationBounds.Piece piece)
        {
            int n = parent.Count;
            var result = new List<(ExactDyadic, ExactDyadic)>();
            for (int j = 0; j < piece.Count; ++j) {
                Vector3d p = piece.P[j];
                int same = -1;
                for (int k = 0; k < n; ++k) if (parent.P[k] == p && parent.Off[k] == piece.Off[j]) { same = k; break; }
                if (same >= 0) { result.Add(parentExact[same]); continue; }
                bool cut = false;
                for (int k = 0; k < n && !cut; ++k) {
                    int k1 = (k + 1) % n;
                    double da = dist[k], db = dist[k1];
                    if (!((da > 0 && db < 0) || (da < 0 && db > 0))) continue;
                    double lambda = da / (da - db);
                    if (!(lambda >= 0)) lambda = 0;
                    if (lambda > 1) lambda = 1;
                    Vector3d a = parent.P[k], b = parent.P[k1];
                    if (a + lambda * (b - a) != p) continue;
                    ExactDyadic l = D(lambda);
                    result.Add((parentExact[k].Item1 + l * (parentExact[k1].Item1 - parentExact[k].Item1),
                        parentExact[k].Item2 + l * (parentExact[k1].Item2 - parentExact[k].Item2)));
                    cut = true;
                }
                Assert.True(cut, "a point of neither the parent nor a cut");
            }
            return result;
        }

        static bool strictly_convex(Vector3d[] q)
        {
            int n = q.Length, sign = 0;
            for (int i = 0; i < n; ++i) {
                int o = orient(q[i], q[(i + 1) % n], q[(i + 2) % n]).Sign;
                if (o == 0 || (sign != 0 && o != sign)) return false;
                sign = o;
            }
            return true;
        }

        static Vector3d unit(Random rnd)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            return new Vector3d(Math.Cos(ang), Math.Sin(ang), 0);
        }

        // the exact vertices of a piece of the clip of parent (whose exact vertices are parentExact, in order): a point taken from the
        // parent is its exact vertex; a cut point is the exact point on the exact edge at the clip's parameter, and its stored point is
        // checked to be within its off of it (L1, exact)
        static List<(ExactDyadic, ExactDyadic)> clip_exact(TriangleDeviationBounds.Piece parent, List<(ExactDyadic, ExactDyadic)> parentExact,
            Vector3d origin, Vector3d normal, TriangleDeviationBounds.Piece piece)
        {
            int n = parent.Count;
            var result = new List<(ExactDyadic, ExactDyadic)>();
            for (int j = 0; j < piece.Count; ++j) {
                Vector3d p = piece.P[j];
                int same = -1;
                for (int k = 0; k < n; ++k) if (parent.P[k] == p && parent.Off[k] == piece.Off[j]) { same = k; break; }
                if (same >= 0) { result.Add(parentExact[same]); continue; }
                bool cut = false;
                for (int k = 0; k < n && !cut; ++k) {
                    int k1 = (k + 1) % n;
                    Vector3d a = parent.P[k], b = parent.P[k1];
                    double da = normal.Dot(a - origin), db = normal.Dot(b - origin);
                    if (!((da > 0 && db < 0) || (da < 0 && db > 0))) continue;
                    double lambda = da / (da - db);
                    if (!(lambda >= 0)) lambda = 0;
                    if (lambda > 1) lambda = 1;
                    if (a + lambda * (b - a) != p) continue;
                    ExactDyadic l = D(lambda);
                    ExactDyadic ex = parentExact[k].Item1 + l * (parentExact[k1].Item1 - parentExact[k].Item1);
                    ExactDyadic ey = parentExact[k].Item2 + l * (parentExact[k1].Item2 - parentExact[k].Item2);
                    ExactDyadic dx = D(p.x) - ex, dy = D(p.y) - ey;
                    ExactDyadic l1 = (dx.Sign < 0 ? -dx : dx) + (dy.Sign < 0 ? -dy : dy);
                    Assert.True(ExactDyadic.Compare(l1, D(piece.Off[j])) <= 0, "cut point outside its off");
                    result.Add((ex, ey)); cut = true;
                }
                Assert.True(cut, "a point of neither the parent nor a cut");
            }
            return result;
        }

        // dyadic convex combinations of the piece's exact vertices lie in one of the two exact pieces
        static void assert_covered(List<(ExactDyadic, ExactDyadic)> piece, List<(ExactDyadic, ExactDyadic)> a, List<(ExactDyadic, ExactDyadic)> b,
            Random rnd, string what)
        {
            int n = piece.Count;
            int orientation = area_sign(piece);
            for (int s = 0; s < 60; ++s) {
                // weights k_i / 2^10, summing to 1
                int[] w = new int[n]; int left = 1024;
                for (int i = 0; i < n - 1; ++i) { w[i] = rnd.Next(0, left + 1); left -= w[i]; }
                w[n - 1] = left;
                ExactDyadic x = D(0), y = D(0);
                for (int i = 0; i < n; ++i) { x = x + D(w[i] / 1024.0) * piece[i].Item1; y = y + D(w[i] / 1024.0) * piece[i].Item2; }
                Assert.True(inside_exact(a, x, y, orientation) || inside_exact(b, x, y, orientation), what + ": a point of the piece in neither part");
            }
        }

        static int area_sign(List<(ExactDyadic, ExactDyadic)> poly)
        {
            ExactDyadic s = D(0);
            for (int i = 0; i < poly.Count; ++i) {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                s = s + (p.Item1 * q.Item2 - q.Item1 * p.Item2);
            }
            return s.Sign >= 0 ? 1 : -1;
        }

        // (x, y) in the closed convex polygon of the given orientation; a polygon of fewer than 3 points, or of zero area, contains a
        // point only on it (checked as collinear and between)
        static bool inside_exact(List<(ExactDyadic, ExactDyadic)> poly, ExactDyadic x, ExactDyadic y, int orientation)
        {
            int n = poly.Count;
            if (n == 0) return false;
            for (int i = 0; i < n; ++i) {
                var p = poly[i]; var q = poly[(i + 1) % n];
                ExactDyadic o = (q.Item1 - p.Item1) * (y - p.Item2) - (q.Item2 - p.Item2) * (x - p.Item1);
                if (o.Sign * orientation < 0) return false;
            }
            return n >= 3;
        }

        // the exact vertices of a piece of the triangle: its corners as they are, its cut points at the parameter the clip used (on the
        // exact edge); checks that each stored cut point is within its off of it
        static (ExactDyadic[] x, ExactDyadic[] y) exact_vertices(TriangleDeviationBounds.Piece piece, Vector3d[] corners, Vector3d origin, Vector3d normal)
        {
            var xs = new List<ExactDyadic>(); var ys = new List<ExactDyadic>();
            for (int j = 0; j < piece.Count; ++j) {
                Vector3d p = piece.P[j];
                if (Array.IndexOf(corners, p) >= 0 && piece.Off[j] == 0) { xs.Add(D(p.x)); ys.Add(D(p.y)); continue; }
                // a cut point: on the edge (a, b) whose rounded cut it is (the same parameter as the clip)
                bool found = false;
                for (int k = 0; k < 3 && !found; ++k) {
                    Vector3d a = corners[k], b = corners[(k + 1) % 3];
                    double da = normal.Dot(a - origin), db = normal.Dot(b - origin);
                    if (!((da > 0 && db < 0) || (da < 0 && db > 0))) continue;
                    double lambda = da / (da - db);
                    if (!(lambda >= 0)) lambda = 0;
                    if (lambda > 1) lambda = 1;
                    if (a + lambda * (b - a) != p) continue;
                    ExactDyadic ex = D(a.x) + D(lambda) * (D(b.x) - D(a.x)), ey = D(a.y) + D(lambda) * (D(b.y) - D(a.y));
                    ExactDyadic dx = D(p.x) - ex, dy = D(p.y) - ey;
                    ExactDyadic l1 = (dx.Sign < 0 ? -dx : dx) + (dy.Sign < 0 ? -dy : dy);
                    Assert.True(ExactDyadic.Compare(l1, D(piece.Off[j])) <= 0, "cut point off");
                    xs.Add(ex); ys.Add(ey); found = true;
                }
                Assert.True(found, "cut point not on an edge");
            }
            return (xs.ToArray(), ys.ToArray());
        }

        static ExactDyadic orient(Vector3d a, Vector3d b, Vector3d c)
            => (D(b.x) - D(a.x)) * (D(c.y) - D(a.y)) - (D(b.y) - D(a.y)) * (D(c.x) - D(a.x));

        static bool inside((ExactDyadic[] x, ExactDyadic[] y) poly, Vector3d p) => inside(poly.x, poly.y, p);

        // p in the closed convex polygon (counter-clockwise; fewer than 3 points: measure zero, never)
        static bool inside(ExactDyadic[] xs, ExactDyadic[] ys, Vector3d p)
        {
            int n = xs.Length;
            if (n < 3) return false;
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                ExactDyadic o = (xs[j] - xs[i]) * (D(p.y) - ys[i]) - (ys[j] - ys[i]) * (D(p.x) - xs[i]);
                if (o.Sign < 0) return false;
            }
            return true;
        }

        /// <summary> B5: the covering radius bound is not below the covering radius (exact: the circumradius of an acute triangle,
        /// half the longest edge otherwise; squared): random triangles, near-right ones (right angles perturbed by an ulp) </summary>
        [Fact]
        public void CoveringUpper_NotBelowTheCoveringRadius()
        {
            var rnd = new Random(19);
            for (int it = 0; it < 6000; ++it) {
                double sc = Math.Pow(2, rnd.Next(-500, 500));
                Vector3d p0, p1, p2;
                if (it % 2 == 0) {
                    p0 = sc * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    p1 = sc * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    p2 = sc * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                } else {
                    // a right angle at p0, a coordinate moved by an ulp or two
                    double a = 1 + rnd.NextDouble(), b = 1 + rnd.NextDouble();
                    p0 = Vector3d.Zero; p1 = sc * new Vector3d(a, 0, 0); p2 = sc * new Vector3d(0, b, 0);
                    long bits = BitConverter.DoubleToInt64Bits(p2.x == 0 ? sc : p2.x);
                    p2.x = (rnd.Next(3) - 1) * BitConverter.Int64BitsToDouble(bits + rnd.Next(0, 3)) * Math.Pow(2, -60);
                }
                double e01 = CertifiedTriangleBounds.LengthUpper(p0, p1), e12 = CertifiedTriangleBounds.LengthUpper(p1, p2),
                    e20 = CertifiedTriangleBounds.LengthUpper(p2, p0);
                double cov = CertifiedTriangleBounds.CoveringUpper(p0, p1, p2, e01, e12, e20);
                ExactDyadic q01 = sq(p0, p1), q12 = sq(p1, p2), q20 = sq(p2, p0);
                bool acute = (q12 + q20 - q01).Sign > 0 && (q01 + q20 - q12).Sign > 0 && (q01 + q12 - q20).Sign > 0;
                ExactDyadic c2 = D(cov) * D(cov);
                if (acute) {
                    // R^2 = q01 q12 q20 / (4 |cross|^2) <= cov^2
                    ExactDyadic cx = cross2(p0, p1, p2);
                    Assert.True(ExactDyadic.Compare(D(4) * cx * c2, q01 * q12 * q20) >= 0, $"acute {it}: {cov:R}");
                } else {
                    ExactDyadic l2 = q01; if (ExactDyadic.Compare(q12, l2) > 0) l2 = q12; if (ExactDyadic.Compare(q20, l2) > 0) l2 = q20;
                    Assert.True(ExactDyadic.Compare(D(4) * c2, l2) >= 0, $"not acute {it}: {cov:R}");
                }
            }
        }

        /// <summary> the certified cascade (TriangleDeviationBounds.Certified) against exact sampling: a horizontal triangle with dyadic
        /// corners (dyadic samples lie exactly on it) over random roofs and valleys of two or three target triangles, whose distance has
        /// interior maxima and makes the cascade split; the bound is never below a sample's exact distance </summary>
        [Fact]
        public void CertifiedCascade_NotBelowExactSamples()
        {
            var rnd = new Random(23);
            int splits = 0;
            for (int it = 0; it < 150; ++it) {
                var target = new DMesh3();
                int s0 = target.AppendVertex(new Vector3d(0, -2, 0)), s1 = target.AppendVertex(new Vector3d(0, 2, 0));
                int l = target.AppendVertex(new Vector3d(-2, 0.3 * rnd.NextDouble(), 2 * (rnd.NextDouble() - 0.3)));
                int r = target.AppendVertex(new Vector3d(2, -0.3 * rnd.NextDouble(), 2 * (rnd.NextDouble() - 0.3)));
                target.AppendTriangle(s0, s1, l);
                target.AppendTriangle(s1, s0, r);
                if (it % 2 == 0) target.AppendTriangle(s1, r, target.AppendVertex(new Vector3d(1.5, 2.5, 2 * rnd.NextDouble() - 0.5)));
                var spatial = new DMeshAABBTree3(target, true);
                double z = 0.5 + rnd.Next(0, 16) / 16.0;
                Vector3d p0 = new Vector3d(-1 + rnd.Next(0, 8) / 16.0, -0.5, z), p1 = new Vector3d(1 - rnd.Next(0, 8) / 16.0, -0.5 + rnd.Next(0, 4) / 16.0, z),
                    p2 = new Vector3d(rnd.Next(-8, 8) / 16.0, 0.75, z);
                var bounds = new TriangleDeviationBounds(target) { Certified = true };
                Vector3d[] ps = { p0, p1, p2 };
                var d = new double[3]; var nt = new int[3];
                for (int k = 0; k < 3; ++k) {
                    nt[k] = spatial.FindNearestTriangle(ps[k]);
                    Index3i tv = target.GetTriangle(nt[k]);
                    Triangle3d tri = new Triangle3d(target.GetVertex(tv.a), target.GetVertex(tv.b), target.GetVertex(tv.c));
                    Vector3d x = ps[k];
                    DistPoint3Triangle3.DistanceSqr(ref x, ref tri, out _, out Vector3d bary);
                    d[k] = CertifiedTriangleBounds.PointTriangleUpper(x, tri.V0, tri.V1, tri.V2, bary.y, bary.z);
                }
                if (!(nt[0] == nt[1] && nt[1] == nt[2])) splits++;
                double u = bounds.UpperBound(p0, p1, p2, d[0], d[1], d[2], nt[0], nt[1], nt[2], 0);
                // (dyadic samples of the triangle at a 1/64 spacing, inside by exact orientation tests)
                for (int i = -64; i <= 64; i += 4)
                    for (int j = -32; j <= 48; j += 4) {
                        Vector3d y = new Vector3d(i / 64.0, j / 64.0, z);
                        if (!inside(new[] { D(p0.x), D(p1.x), D(p2.x) }, new[] { D(p0.y), D(p1.y), D(p2.y) }, y)) continue;
                        Assert.True(gs.CertifiedPointMeshDistance.AtLeastDistance(target, y, u), $"it {it}: {u:R} below the distance of {y}");
                    }
            }
            Assert.True(splits > 50, splits + " with corners nearest to different triangles");
        }

        // the exact distances of dyadic samples of the horizontal triangle (p0, p1, p2) at a 1/64 spacing are at most u
        static void AssertSamplesWithin(DMesh3 target, Vector3d p0, Vector3d p1, Vector3d p2, double u, string what)
        {
            double z = p0.z;
            for (int i = -128; i <= 128; ++i)
                for (int j = -128; j <= 128; ++j) {
                    Vector3d y = new Vector3d(i / 64.0, j / 64.0, z);
                    if (!inside(new[] { D(p0.x), D(p1.x), D(p2.x) }, new[] { D(p0.y), D(p1.y), D(p2.y) }, y)) continue;
                    if ((i + j) % 3 != 0 && y != p0 && y != p1 && y != p2) continue;
                    Assert.True(gs.CertifiedPointMeshDistance.AtLeastDistance(target, y, u), $"{what}: {u:R} below the distance of {y}");
                }
        }

        /// <summary> B6: one nearest triangle for all corners: the largest corner bound (convexity), not a smaller one </summary>
        [Fact]
        public void CertifiedCascade_SingleNearestTriangle_IsTheLargestCorner()
        {
            DMesh3 target = Tri(new Vector3d(-4, -4, 0), new Vector3d(4, -4, 0), new Vector3d(0, 4, 0));
            var bounds = new TriangleDeviationBounds(target) { Certified = true };
            Vector3d p0 = new Vector3d(-1, -1, 0.25), p1 = new Vector3d(1, -1, 1), p2 = new Vector3d(0, 1, 2);
            double u = bounds.UpperBound(p0, p1, p2, 0.25, 1, 2, 0, 0, 0, 0);
            Assert.True(u >= 2 && u < 2 + 1e-12, u.ToString("R"));
        }

        /// <summary> B4: u1 is a corner's bound plus the longer of its edges. The other corners' bounds are loose (a far triangle), so u1
        /// is the smallest term: a shorter edge would put it below the distance of the far corner </summary>
        [Fact]
        public void CertifiedCascade_U1_UsesTheLongerEdge()
        {
            // the target: the plane x = 0 (a large triangle) and a far small triangle
            var target = new DMesh3();
            target.AppendTriangle(target.AppendVertex(new Vector3d(0, -8, -8)), target.AppendVertex(new Vector3d(0, 8, -8)), target.AppendVertex(new Vector3d(0, 0, 8)));
            target.AppendTriangle(target.AppendVertex(new Vector3d(100, 0, 0)), target.AppendVertex(new Vector3d(101, 0, 0)), target.AppendVertex(new Vector3d(100, 1, 0)));
            var bounds = new TriangleDeviationBounds(target) { Certified = true };
            // p0 on the plane (distance 0), p1 at distance 2 across the longer edge, p2 at 0.5 across the shorter one; p1 and p2 given
            // the loose bound 1000 to the far triangle
            Vector3d p0 = new Vector3d(0, 0, 0), p1 = new Vector3d(2, 0, 0), p2 = new Vector3d(0.5, 1, 0);
            double u = bounds.UpperBound(p0, p1, p2, 0, 1000, 1000, 0, 1, 1, 0);
            AssertSamplesWithin(target, p0, p1, p2, u, "u1");
            Assert.True(u < 3, u.ToString("R"));
        }

        /// <summary> B5: u2 is the largest corner bound plus a covering radius bound, which for a near-equilateral triangle is the
        /// circumradius (about l / sqrt 3), not half the longest edge: corners on three tiny target triangles, the centre farther from
        /// all of them than half an edge </summary>
        [Fact]
        public void CertifiedCascade_U2_CoversTheCircumradius()
        {
            Vector3d p0 = new Vector3d(-1, -0.5625, 0), p1 = new Vector3d(1, -0.5625, 0), p2 = new Vector3d(0, 1.1875, 0);
            var target = new DMesh3();
            foreach (Vector3d c in new[] { p0, p1, p2 })
                target.AppendTriangle(target.AppendVertex(c), target.AppendVertex(c + new Vector3d(1e-3, 0, 0)), target.AppendVertex(c + new Vector3d(0, 0, 1e-3)));
            var bounds = new TriangleDeviationBounds(target) { Certified = true };
            double u = bounds.UpperBound(p0, p1, p2, 0, 0, 0, 0, 1, 2, 0);
            AssertSamplesWithin(target, p0, p1, p2, u, "u2");
            Assert.True(u < 1.3, u.ToString("R"));
        }

        /// <summary> B2: a piece's point that stands for an exact point within off of it adds that off, also where it equals a corner by
        /// value (a cut point at lambda = 0) </summary>
        [Fact]
        public void CertifiedMaxDistance_AddsEveryPointsOff()
        {
            DMesh3 target = Tri(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var bounds = new TriangleDeviationBounds(target) { Certified = true };
            Vector3d c0 = new Vector3d(0.25, 0.25, 1), c1 = new Vector3d(0.5, 0.25, 1), c2 = new Vector3d(0.25, 0.5, 1);
            var corners = new TriangleDeviationBounds.Corners { P0 = c0, P1 = c1, P2 = c2, D0 = 1, D1 = 1, D2 = 1, N0 = 0, N1 = 0, N2 = 0 };
            var piece = new TriangleDeviationBounds.Piece(3);
            piece.Add(c0, 0); piece.Add(c1, 0.5); piece.Add(new Vector3d(0.3, 0.3, 1), 0.25);
            double m = bounds.certified_max_distance(piece, 0, ref corners);
            Assert.True(m >= 1.5 && m < 1.5 + 1e-12, m.ToString("R"));
            var interior = new TriangleDeviationBounds.Piece(1);
            interior.Add(new Vector3d(0.3, 0.3, 1), 0.25);
            double mi = bounds.certified_max_distance(interior, 0, ref corners);
            Assert.True(mi >= 1.25 && mi < 1.25 + 1e-12, mi.ToString("R"));
        }

        static ExactDyadic sq(Vector3d a, Vector3d b)
        {
            ExactDyadic s = D(0);
            for (int k = 0; k < 3; ++k) { ExactDyadic d = D(b[k]) - D(a[k]); s = s + d * d; }
            return s;
        }

        static ExactDyadic cross2(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            ExactDyadic ax = D(p1.x) - D(p0.x), ay = D(p1.y) - D(p0.y), az = D(p1.z) - D(p0.z);
            ExactDyadic bx = D(p2.x) - D(p0.x), by = D(p2.y) - D(p0.y), bz = D(p2.z) - D(p0.z);
            ExactDyadic cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return cx * cx + cy * cy + cz * cz;
        }
    }
}
