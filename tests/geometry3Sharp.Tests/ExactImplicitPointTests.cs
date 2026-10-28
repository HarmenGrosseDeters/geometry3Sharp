using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The implicit points of ExactMeshArrangement: their approximations are within their error bounds, the
    /// filtered predicates on them agree with the exact ones (also for exactly collinear points, whose
    /// approximations are not), and coincident points from different constructions get one id.
    /// </summary>
    public class ExactImplicitPointTests
    {
        static DMesh3 Box(Vector3d center, double size, Quaterniond rotation)
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(center, new Vector3d(size, size, size)) }.Generate().MakeDMesh();
            MeshTransforms.Rotate(mesh, center, rotation);
            return mesh;
        }

        static Quaterniond Rotation(Random rand, double maxAngle)
        {
            var axis = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized;
            return new Quaterniond(axis, rand.NextDouble() * maxAngle);
        }

        // arrangements with all kinds of implicit points: three rotated boxes (edge-plane points and triple points),
        // nearly coplanar boxes (ill-conditioned), coplanar boxes (crossing edges), at different scales and offsets
        public static IEnumerable<object[]> Cases()
        {
            for (int seed = 0; seed < 12; ++seed)
                yield return new object[] { seed };
        }

        static ExactMeshArrangement Build(int seed)
        {
            var rand = new Random(seed);
            double scale = Math.Pow(10, rand.Next(-6, 7));
            Vector3d offset = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * scale * ((seed % 3 == 0) ? 1e4 : 1);
            List<DMesh3> meshes;
            switch (seed % 4) {
                case 0:
                case 1:
                    meshes = new List<DMesh3>() { Box(Vector3d.Zero, 1, Rotation(rand, 360)), Box(new Vector3d(0.7, 0.2, 0.1), 0.8, Rotation(rand, 360)),
                        Box(new Vector3d(0.2, 0.7, -0.3), 0.9, Rotation(rand, 360)) };
                    break;
                case 2:
                    // nearly coplanar faces (rotations by tiny angles), and triple points of nearly parallel planes
                    meshes = new List<DMesh3>() { Box(Vector3d.Zero, 1, Rotation(rand, 1e-6)), Box(new Vector3d(0.5, 0.3, 0), 1, Rotation(rand, 1e-9)),
                        Box(new Vector3d(0.2, 0.6, 0.1), 0.9, Rotation(rand, 1e-3)), Box(new Vector3d(0.3, 0.1, 0.2), 1.1, Rotation(rand, 1e-4)) };
                    break;
                default:
                    // coplanar overlapping faces (crossing edges in common planes) and a rotated box through them
                    meshes = new List<DMesh3>() { Box(Vector3d.Zero, 1, Quaterniond.Identity), Box(new Vector3d(0.5, 0.3, 0), 1, new Quaterniond(Vector3d.AxisZ, 30)),
                        Box(new Vector3d(0.2, 0.6, 0.1), 0.9, Rotation(rand, 360)) };
                    break;
            }
            foreach (DMesh3 m in meshes) {
                MeshTransforms.Scale(m, scale);
                MeshTransforms.Translate(m, offset);
            }
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            return arr;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Approximations_AreWithinTheirErrorBounds(int seed)
        {
            var arr = Build(seed);
            var kinds = new HashSet<ExactMeshArrangement.PointKind>();
            for (int id = 0; id < arr.PointCount; ++id) {
                kinds.Add(arr.KindOf(id));
                AssertWithinBound(arr, id);
            }
            if (seed % 4 < 3)
                Assert.Contains(ExactMeshArrangement.PointKind.Planes, kinds);
            Assert.Contains(ExactMeshArrangement.PointKind.EdgePlane, kinds);
            if (seed % 4 == 3)
                Assert.Contains(ExactMeshArrangement.PointKind.Segments, kinds);
        }

        // v as an exact fraction num / 2^-exp (exp <= 0) with a common exponent for the comparison below
        static BigInteger Scaled(double v, int exp)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            return m.IsZero ? BigInteger.Zero : (m << (e - exp));
        }

        // |approximation - exact| <= bound, exactly (no rounding of the comparison)
        static void AssertWithinBound(ExactMeshArrangement arr, int id)
        {
            ExactPoint3 x = arr.Exact(id);
            Vector3d a = arr.Approx(id);
            double e = arr.ApproxError(id);
            Assert.True(e >= 0 && double.IsInfinity(e) == false);
            for (int k = 0; k < 3; ++k) {
                // |a W - X| <= e W, all scaled by 2^1100 to integers
                const int exp = -1100;
                BigInteger lhs = BigInteger.Abs(Scaled(a[k], exp) * x.W - (x[k] << -exp)), rhs = Scaled(e, exp) * x.W;
                Assert.True(lhs <= rhs, $"point {id} ({arr.KindOf(id)}): {a[k]} vs {ExactPoint3.ToDouble(x[k], x.W, 0)}, bound {e}");
            }
        }

        [Fact]
        public void Constructions_IllConditioned_ApproximationsWithinBounds()
        {
            // nearly dependent planes, edges nearly parallel to the plane, nearly parallel crossing segments: the
            // approximations are poor, the bounds must still hold
            var rand = new Random(17);
            Func<double, Vector3d> rnd = (s) => new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5) * s;
            int planes = 0, edges = 0, segments = 0;
            for (int iter = 0; iter < 300; ++iter) {
                double eps = Math.Pow(10, -rand.Next(2, 13));
                Vector3d dir = rnd(1).Normalized, c = rnd(2);
                DMesh3 mesh = new DMesh3();
                // three triangles whose planes nearly contain the direction dir (all through about the same line)
                for (int t = 0; t < 3; ++t) {
                    Vector3d side = dir.Cross(rnd(1)).Normalized;
                    Vector3d p0 = c + rnd(eps), p1 = p0 + dir + rnd(eps), p2 = p0 + side + rnd(eps);
                    int v0 = mesh.AppendVertex(p0), v1 = mesh.AppendVertex(p1), v2 = mesh.AppendVertex(p2);
                    mesh.AppendTriangle(v0, v1, v2);
                }
                // an edge nearly parallel to the first plane, and two nearly parallel segments in the plane z = 0
                Vector3d n0 = (mesh.GetVertex(1) - mesh.GetVertex(0)).Cross(mesh.GetVertex(2) - mesh.GetVertex(0)).Normalized;
                Vector3d along = n0.Cross(rnd(1)).Normalized;
                int e0 = mesh.AppendVertex(c - along + n0 * eps * rand.NextDouble()), e1 = mesh.AppendVertex(c + along - n0 * eps * rand.NextDouble());
                Vector3d s0 = new Vector3d(-1, rnd(eps).y, 0), s1 = new Vector3d(1, rnd(eps).y, 0);
                int q0 = mesh.AppendVertex(s0), q1 = mesh.AppendVertex(s1);
                int r0 = mesh.AppendVertex(new Vector3d(-1, s0.y + eps * (0.5 + rand.NextDouble()), 0)), r1 = mesh.AppendVertex(new Vector3d(1, s1.y - eps * (0.5 + rand.NextDouble()), 0));
                var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
                arr.Compute();
                Func<int, int> pt = (v) => arr.VertexPoints[0][v];

                var pl = arr.Triangles.Take(3).Select(t => t.P).ToArray();
                BigInteger det = pl[0].A * (pl[1].B * pl[2].C - pl[1].C * pl[2].B) - pl[0].B * (pl[1].A * pl[2].C - pl[1].C * pl[2].A)
                    + pl[0].C * (pl[1].A * pl[2].B - pl[1].B * pl[2].A);
                if (det.IsZero == false) {
                    AssertWithinBound(arr, arr.PlanesPointImplicit(0, 4, 8));
                    planes++;
                }
                Func<int, BigInteger> side0 = (v) => { var x = arr.Exact(pt(v)); return pl[0].A * x.X + pl[0].B * x.Y + pl[0].C * x.Z - pl[0].D * x.W; };
                if (side0(e0).Sign * side0(e1).Sign < 0) {
                    AssertWithinBound(arr, arr.EdgePlanePointImplicit(pt(e0), pt(e1), 0));
                    edges++;
                }
                // q0-q1 and r0-r1 cross (r starts above q and ends below)
                AssertWithinBound(arr, arr.SegmentsPointImplicit(pt(q0), pt(q1), pt(r0), pt(r1), 2));
                segments++;
            }
            Assert.True(planes > 250 && edges > 100 && segments == 300, $"{planes} {edges} {segments}");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FilteredPredicates_MatchExact_OnImplicitPoints(int seed)
        {
            var arr = Build(seed);
            var rand = new Random(seed);
            int collinear = 0;
            foreach (var t in arr.Triangles) {
                if (t.Segments.Count == 0)
                    continue;
                var pts = t.Points.Select(arr.Canonical).Concat(t.Corners.array).Distinct().ToList();
                Func<Func<int>, int> both = (f) => {
                    arr.DisableFilters = false;
                    int filtered = f();
                    arr.DisableFilters = true;
                    int exact = f();
                    arr.DisableFilters = false;
                    Assert.Equal(exact, filtered);
                    return exact;
                };
                // points on the segments: exactly collinear, but their approximations are not
                foreach (var s in t.Segments) {
                    int p = arr.Canonical(s.P), q = arr.Canonical(s.Q);
                    var plane = arr.PlaneOf(s.Line);
                    BigInteger dx = t.NY * plane.C - t.NZ * plane.B, dy = t.NZ * plane.A - t.NX * plane.C, dz = t.NX * plane.B - t.NY * plane.A;
                    foreach (int r in pts) {
                        if (both(() => arr.Orient(t, p, q, r)) == 0)
                            collinear++;
                        both(() => arr.compare_along(dx, dy, dz, p, r));
                    }
                }
                for (int k = 0; k < 40; ++k) {
                    int a = pts[rand.Next(pts.Count)], b = pts[rand.Next(pts.Count)], c = pts[rand.Next(pts.Count)], d = pts[rand.Next(pts.Count)];
                    both(() => arr.Orient(t, a, b, c));
                    both(() => arr.InCircle(t, a, b, c, d));
                    both(() => arr.ComparePoints(a, b));
                }
            }
            Assert.True(collinear > 0);
        }

        // reference: the sign of the 4x4 incircle determinant with rows (x w, y w, x^2 + y^2, w^2) of the exact points,
        // times the sign of the projection (the formula before the 3x3 form and the double path)
        static int ReferenceInCircle(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t, int[] ids)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            var m = new BigInteger[4, 4];
            for (int k = 0; k < 4; ++k) {
                ExactPoint3 v = arr.Exact(ids[k]);
                m[k, 0] = v[i] * v.W; m[k, 1] = v[j] * v.W; m[k, 2] = v[i] * v[i] + v[j] * v[j]; m[k, 3] = v.W * v.W;
            }
            BigInteger det = BigInteger.Zero;
            for (int c = 0; c < 4; ++c) {
                int c0 = (c == 0) ? 1 : 0, c1 = (c <= 1) ? 2 : 1, c2 = (c <= 2) ? 3 : 2;
                BigInteger minor = ExactPoint3.Det3(m[1, c0], m[1, c1], m[1, c2], m[2, c0], m[2, c1], m[2, c2], m[3, c0], m[3, c1], m[3, c2]);
                det += ((c % 2 == 0) ? m[0, c] : -m[0, c]) * minor;
            }
            return det.Sign * t.Sign;
        }

        static int ReferenceOrient(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t, int p, int q, int r)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            ExactPoint3 a = arr.Exact(p), b = arr.Exact(q), c = arr.Exact(r);
            return ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign * t.Sign;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ExactStages_MatchReferenceDeterminants(int seed)
        {
            // the exact stages of Orient and InCircle (DisableFilters): the adaptive double predicates for integer
            // points with double coordinates, the 3x3 integer determinant for the others, against the 4x4 / 3x3
            // homogeneous determinants; also nearly and exactly cocircular quadruples (four points of one
            // triangle's segments and corners)
            var arr = Build(seed);
            var rand = new Random(seed);
            int doubles = 0, rationals = 0, zeros = 0, candidates = 0;
            arr.DisableFilters = true;
            foreach (var t in arr.Triangles) {
                if (t.Segments.Count == 0)
                    continue;
                var pts = t.Points.Select(arr.Canonical).Concat(t.Corners.array).Distinct().ToList();
                // (and quadruples of integer points with double coordinates, where the triangle has four)
                var dbl = pts.Where(arr.IsExactDouble).ToList();
                if (dbl.Count >= 4)
                    candidates++;
                for (int k = 0; k < 80; ++k) {
                    var from = (k >= 60 && dbl.Count >= 4) ? dbl : pts;
                    int[] ids = { from[rand.Next(from.Count)], from[rand.Next(from.Count)], from[rand.Next(from.Count)], from[rand.Next(from.Count)] };
                    int expected = ReferenceInCircle(arr, t, ids);
                    Assert.Equal(expected, arr.InCircle(t, ids[0], ids[1], ids[2], ids[3]));
                    Assert.Equal(ReferenceOrient(arr, t, ids[0], ids[1], ids[2]), arr.Orient(t, ids[0], ids[1], ids[2]));
                    if (ids.All(arr.IsExactDouble)) doubles++; else rationals++;
                    if (expected == 0) zeros++;
                }
            }
            arr.DisableFilters = false;
            Assert.True((doubles > 0 || candidates == 0) && rationals > 0 && zeros > 0, $"{doubles} {rationals} {zeros}");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Orient_CollinearByConstruction_WithoutExactPoints(int seed)
        {
            // points on an edge (edge-plane points) and crossings of segments are on their lines by construction: Orient
            // gives 0 for them without computing the exact points. Every triple of a few triangles' points, with the
            // filters and without (only the exact stages); then all against the homogeneous determinant (which computes
            // the exact points): also no 0 for a triple that is not collinear (one end of the edge, a point of another).
            // On the boxes of Build (whose points mostly have their exact points after Compute) and on two spheres in
            // general position (whose points mostly do not)
            DMesh3 a = new Sphere3Generator_NormalizedCube() { EdgeVertices = 4 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            var rand = new Random(seed);
            MeshTransforms.Rotate(b, Vector3d.Zero, Rotation(rand, 360));
            MeshTransforms.Translate(b, new Vector3d(0.31, 0.27, 0.05) * (1 + rand.NextDouble()));
            var spheres = new ExactMeshArrangement(new List<DMesh3>() { a, b });
            spheres.Compute();
            int shortcut = 0, total = 0, zeros = 0, segmentsShortcut = 0;
            foreach (var arr in new[] { Build(seed), spheres }) {
                // (triangles with coplanar ones first: they have crossings of segments)
                var tris = arr.Triangles.Where(t => t.Segments.Count > 0).OrderByDescending(t => t.Coplanar.Count > 0).Take(6).ToList();
                var results = new List<(ExactMeshArrangement.InputTriangle, int, int, int, int)>();
                foreach (bool disable in new[] { false, true }) {
                    arr.DisableFilters = disable;
                    foreach (var t in tris) {
                        var pts = t.Points.Select(arr.Canonical).Concat(t.Corners.array).Distinct().Take(14).ToList();
                        foreach (int p in pts)
                            foreach (int q in pts)
                                foreach (int r in pts) {
                                    if (p == q || q == r || r == p)
                                        continue;
                                    // (the constructed points of the triple without their exact points, before and after)
                                    var open = new[] { p, q, r }.Where(x => arr.KindOf(x) == ExactMeshArrangement.PointKind.EdgePlane
                                        || arr.KindOf(x) == ExactMeshArrangement.PointKind.Segments).Where(x => arr.HasExactPoint(x) == false).ToList();
                                    int o = arr.Orient(t, p, q, r);
                                    if (o == 0 && open.Count > 0 && open.All(x => arr.HasExactPoint(x) == false)) {
                                        shortcut++;
                                        if (open.Any(x => arr.KindOf(x) == ExactMeshArrangement.PointKind.Segments))
                                            segmentsShortcut++;
                                    }
                                    results.Add((t, p, q, r, o));
                                }
                    }
                }
                arr.DisableFilters = false;
                foreach (var (t, p, q, r, o) in results)
                    Assert.Equal(ReferenceOrient(arr, t, p, q, r), o);
                total += results.Count;
                zeros += results.Count(x => x.Item5 == 0);
            }
            Assert.True(total > 1000 && zeros > shortcut && shortcut > 0, $"{total} {zeros} {shortcut}");
            // (the coplanar boxes of Build have crossings of segments)
            if (seed % 4 == 3)
                Assert.True(segmentsShortcut > 0, $"{segmentsShortcut}");

            // collinear points without a construction that says so go to the exact stage, which gives 0: three integer
            // points (the double path) and three rational points (W = 2, the integer determinant) on one line
            var arr2 = Build(seed);
            var t2 = arr2.Triangles.First(t => t.Degenerate == false);
            ExactPoint3 c0 = arr2.Exact(t2.Corners[0]), c1 = arr2.Exact(t2.Corners[1]);
            ExactPoint3 far = ExactPoint3.Create(3 * c1.X - 2 * c0.X, 3 * c1.Y - 2 * c0.Y, 3 * c1.Z - 2 * c0.Z, 1);
            ExactPoint3 half = ExactPoint3.Create(c0.X + c1.X, c0.Y + c1.Y, c0.Z + c1.Z, 2);
            ExactPoint3 third = ExactPoint3.Create(c0.X + 2 * c1.X, c0.Y + 2 * c1.Y, c0.Z + 2 * c1.Z, 3);
            int ifar = arr2.AddPoint(far), ihalf = arr2.AddPoint(half), ithird = arr2.AddPoint(third);
            Assert.Equal(0, arr2.Orient(t2, t2.Corners[0], t2.Corners[1], ifar));
            Assert.Equal(0, arr2.Orient(t2, ihalf, ithird, ifar));
            Assert.Equal(0, arr2.Orient(t2, t2.Corners[0], ihalf, ithird));
            Assert.NotEqual(0, arr2.Orient(t2, t2.Corners[0], ihalf, t2.Corners[2]));
        }

        [Fact]
        public void InCircle_CocircularPoints_BothExactStages()
        {
            // points on a circle of the plane z = 0: integer points (corners of the input, double path) and
            // intersection points with W != 1 on the circle x^2 + y^2 = 25 (3x3 integer path), exactly cocircular
            DMesh3 a = new DMesh3();
            foreach (var v in new[] { new Vector3d(-5, 0, 0), new Vector3d(5, 0, 0), new Vector3d(0, 5, 0), new Vector3d(3, -4, 0) })
                a.AppendVertex(v);
            a.AppendTriangle(0, 1, 2);
            a.AppendTriangle(0, 3, 1);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { a });
            arr.Compute();
            var tri = arr.Triangles[0];
            Func<int, int> pt = (v) => arr.VertexPoints[0][v];
            Assert.True(arr.ScaleExp == 0 && arr.Center.All(c => c.IsZero));
            // rational points on the circle (grid coordinates are the input coordinates here): (5 (q^2 - p^2), 10 p q, 0)
            // / (q^2 + p^2) for p / q = 2 / 3 and 1 / 7, and one just off it
            int r1 = arr.AddPoint(ExactPoint3.Create(5 * (9 - 4), 10 * 6, 0, 9 + 4));
            int r2 = arr.AddPoint(ExactPoint3.Create(5 * (49 - 1), 10 * 7, 0, 49 + 1));
            int off = arr.AddPoint(ExactPoint3.Create(5 * (49 - 1) - 1, 10 * 7, 0, 49 + 1));
            Assert.True(arr.IsExactDouble(pt(0)) && arr.IsExactDouble(pt(3)));
            Assert.False(arr.IsExactDouble(r1));
            foreach (bool disable in new[] { false, true }) {
                arr.DisableFilters = disable;
                Assert.Equal(0, arr.InCircle(tri, pt(0), pt(1), pt(2), pt(3)));
                Assert.Equal(0, arr.InCircle(tri, pt(0), pt(1), pt(2), r1));
                Assert.Equal(0, arr.InCircle(tri, r1, pt(1), r2, pt(0)));
                foreach (int[] ids in new[] { new[] { pt(0), pt(1), pt(2), pt(3) }, new[] { pt(0), pt(1), pt(2), off }, new[] { r1, pt(1), off, pt(0) },
                    new[] { pt(0), pt(1), pt(2), arr.AddPoint(ExactPoint3.FromInteger(0, 0, 0)) }, new[] { pt(0), pt(1), pt(2), arr.AddPoint(ExactPoint3.FromInteger(9, 0, 0)) } })
                    Assert.Equal(ReferenceInCircle(arr, tri, ids), arr.InCircle(tri, ids[0], ids[1], ids[2], ids[3]));
            }
            arr.DisableFilters = false;
            Assert.Equal(1, Math.Abs(arr.InCircle(tri, pt(0), pt(1), pt(2), off)));

            // integer points whose coordinates are not doubles take the integer path: p, 3 p and 0 are collinear,
            // their coordinates rounded to double are not (2^53 + 1 rounds to 2^53, 3 2^53 + 3 to 3 2^53 + 4)
            BigInteger big = (BigInteger.One << 53) + 1;
            int o = arr.AddPoint(ExactPoint3.FromInteger(0, 0, 0)), p1 = arr.AddPoint(ExactPoint3.FromInteger(big, 3, 0)),
                p3 = arr.AddPoint(ExactPoint3.FromInteger(3 * big, 9, 0)), p5 = arr.AddPoint(ExactPoint3.FromInteger(5 * big, 15, 0));
            Assert.True(arr.IsExactDouble(o) && arr.IsExactDouble(p1) == false && arr.IsExactDouble(p3) == false);
            foreach (bool disable in new[] { false, true }) {
                arr.DisableFilters = disable;
                Assert.Equal(0, arr.Orient(tri, o, p1, p3));
                Assert.Equal(0, arr.Orient(tri, p1, p3, p5));
            }
            arr.DisableFilters = false;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void OutputPoints_AreCanonicalAndDistinct(int seed)
        {
            AssertDistinctPoints(Build(seed));
        }

        static void AssertDistinctPoints(ExactMeshArrangement arr)
        {
            var ids = new HashSet<int>();
            foreach (var ot in arr.Output)
                foreach (int id in new[] { ot.A, ot.B, ot.C }) {
                    Assert.Equal(id, arr.Canonical(id));
                    ids.Add(id);
                }
            var seen = new HashSet<ExactPoint3>();
            foreach (int id in ids)
                Assert.True(seen.Add(arr.Exact(id)), "two point ids for one point");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(7)]
        public void MergedPoints_SameWithHashedAndSortedClusters(int seed)
        {
            // merge_points groups the points of a cluster by their exact points, or sorts large clusters with a point
            // beyond the double range: the same merges and the same canonical ids either way (coplanar boxes: many
            // coincident constructions; MaxHashedCluster below 0 sorts all clusters)
            var meshes = new List<DMesh3>();
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    meshes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0.45 * (i + j)), 1, Quaterniond.Identity));
            var rand = new Random(seed);
            meshes.Add(Box(new Vector3d(0.4, 0.5, 0.3), 1.1, Rotation(rand, 360)));
            string reference = null;
            foreach (int limit in new[] { 64, -1 }) {
                var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true, MaxHashedCluster = limit };
                arr.Compute();
                Assert.True(arr.MergedPoints > 3);
                AssertDistinctPoints(arr);
                // the pieces with their points (ids are assigned in parallel: by exact point and construction kind)
                string print = string.Join(";", arr.Output.Select(o => string.Join(",", new[] { o.A, o.B, o.C }.Select(id => { var x = arr.Exact(id); return arr.KindOf(id) + "@" + x.X + "," + x.Y + "," + x.Z + "," + x.W; }))))
                    + "|" + arr.MergedPoints;
                if (reference == null) reference = print;
                else Assert.Equal(reference, print);
            }
        }

        static ExactMeshArrangement.Plane P(long a, long b, long c, BigInteger d) { return new ExactMeshArrangement.Plane(a, b, c, d); }

        [Fact]
        public void SamePlane_IsExactProportionality()
        {
            BigInteger h = BigInteger.One << 40;
            // parallel planes one grid unit apart at a large offset (their approximations share a rounding bucket)
            Assert.False(ExactMeshArrangement.same_plane(P(0, 0, 3, 3 * h), P(0, 0, 3, 3 * (h + 1))));
            Assert.False(ExactMeshArrangement.same_plane(P(0, 0, 1, h), P(0, 0, 1, h + 1)));
            // the same plane scaled, also by a negative factor (opposite normals), and through the origin (D = 0)
            Assert.True(ExactMeshArrangement.same_plane(P(1, 2, 3, h), P(-2, -4, -6, -2 * h)));
            Assert.True(ExactMeshArrangement.same_plane(P(0, 0, 5, 0), P(0, 0, -1, 0)));
            Assert.False(ExactMeshArrangement.same_plane(P(0, 0, 1, 0), P(0, 1, 0, 0)));
            Assert.False(ExactMeshArrangement.same_plane(P(1, 1, 0, 0), P(1, -1, 0, 0)));
            Assert.False(ExactMeshArrangement.same_plane(P(0, 1, 1, 0), P(0, 1, 2, 0)));
        }

        [Fact]
        public void ParallelPlanesOneUnitApart_AreNotShared()
        {
            // two flat squares at z = 2^40 and z = 2^40 + 1 (grid units: integer input), each split by a slanted box
            // edge: their points are on different planes; with and without shared constructions the same output
            double h = Math.Pow(2, 40);
            Func<double, DMesh3> square = (z) => {
                var m = new DMesh3();
                m.AppendVertex(new Vector3d(-8, -8, z)); m.AppendVertex(new Vector3d(8, -8, z));
                m.AppendVertex(new Vector3d(8, 8, z)); m.AppendVertex(new Vector3d(-8, 8, z));
                m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3);
                return m;
            };
            var slanted = new DMesh3();
            slanted.AppendVertex(new Vector3d(-3, -1, h - 4)); slanted.AppendVertex(new Vector3d(5, 2, h + 6)); slanted.AppendVertex(new Vector3d(1, 6, h + 5));
            slanted.AppendTriangle(0, 1, 2);
            // (and a triangle far below, so that the grid center is far from the squares: their planes' offsets are
            // large, 2^41, and round to the same bucket key)
            var far = new DMesh3();
            far.AppendVertex(new Vector3d(0, 0, -3 * h)); far.AppendVertex(new Vector3d(1, 0, -3 * h)); far.AppendVertex(new Vector3d(0, 1, -3 * h));
            far.AppendTriangle(0, 1, 2);
            var meshes = new List<DMesh3>() { square(h), square(h + 1), slanted, far };
            string reference = null;
            foreach (bool share in new[] { false, true }) {
                var arr = new ExactMeshArrangement(meshes) { ShareCoplanarConstructions = share };
                arr.Compute();
                AssertDistinctPoints(arr);
                // every piece of a square lies in its plane
                foreach (var o in arr.Output.Where(o => o.Mesh < 2))
                    foreach (int id in new[] { o.A, o.B, o.C }) {
                        ExactPoint3 x = arr.ToInputExact(arr.Exact(id));
                        Assert.Equal(new BigInteger(h + o.Mesh) * x.W, x.Z);
                    }
                string print = string.Join(";", arr.Output.Select(o => o.Mesh + ":" + string.Join(",", new[] { o.A, o.B, o.C }.Select(id => {
                    var x = arr.Exact(id); return x.X + "," + x.Y + "," + x.Z + "," + x.W; }))));
                if (reference == null) reference = print;
                else Assert.Equal(reference, print);
            }
        }

        [Fact]
        public void TouchingBoxes_OppositeNormals_ShareConstructions()
        {
            // boxes stacked face to face (the top of one and the bottom of the other: one plane, opposite normals) and
            // a rotated box through both: the same output with and without shared constructions
            var meshes = new List<DMesh3>() { Box(new Vector3d(0, 0, 0), 0.5, Quaterniond.Identity), Box(new Vector3d(0, 0, 1), 0.5, Quaterniond.Identity),
                Box(new Vector3d(0.1, 0.2, 0.5), 0.6, new Quaterniond(new Vector3d(1, 2, 3).Normalized, 30)) };
            string reference = null;
            int shared = 0, separate = 0;
            foreach (bool share in new[] { false, true }) {
                var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true, ShareCoplanarConstructions = share };
                arr.Compute();
                AssertDistinctPoints(arr);
                if (share) shared = arr.PointCount; else separate = arr.PointCount;
                string print = string.Join(";", arr.Output.Select(o => o.Mesh + ":" + string.Join(",", new[] { o.A, o.B, o.C }.Select(id => {
                    var x = arr.Exact(id); return x.X + "," + x.Y + "," + x.Z + "," + x.W; }))));
                if (reference == null) reference = print;
                else Assert.Equal(reference, print);
            }
            Assert.True(shared < separate, $"{shared} {separate}");
        }

        [Theory]
        [InlineData(2, 0)]
        [InlineData(3, 1)]
        public void CoplanarTriangles_ShareConstructions_SameOutput(int n, int seed)
        {
            // a grid of boxes with coplanar faces and a rotated box: the points where an edge crosses one plane are
            // constructed once for all triangles in that plane (fewer points to merge); the pieces and their exact
            // points are the same as with a construction per triangle
            var meshes = new List<DMesh3>();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    meshes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0), 0.5, Quaterniond.Identity));
            meshes.Add(Box(new Vector3d(0.4, 0.45, 0.2), 0.8, Rotation(new Random(seed), 360)));
            meshes.Add(Box(new Vector3d(0.45 * n, 0.4, 0.1), 0.6, Quaterniond.Identity));
            string reference = null;
            int sharedPoints = 0, separatePoints = 0;
            foreach (bool share in new[] { false, true }) {
                var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true, ShareCoplanarConstructions = share };
                arr.Compute();
                AssertDistinctPoints(arr);
                for (int id = 0; id < arr.PointCount; ++id)
                    AssertWithinBound(arr, id);
                if (share) sharedPoints = arr.PointCount; else separatePoints = arr.PointCount;
                string print = string.Join(";", arr.Output.Select(o => o.Mesh + ":" + string.Join(",", new[] { o.A, o.B, o.C }.Select(id => {
                    var x = arr.Exact(id); return x.X + "," + x.Y + "," + x.Z + "," + x.W; }))));
                if (reference == null) reference = print;
                else Assert.Equal(reference, print);
            }
            Assert.True(sharedPoints < separatePoints, $"{sharedPoints} {separatePoints}");
        }

        [Fact]
        public void CoincidentConstructions_AreMerged()
        {
            // an edge of T runs through the common edge of U and V (in the planes z = 0 and y = 0): the point (1, 0, 0)
            // is where it crosses the plane of U, where it crosses the plane of V, and where the common edge crosses
            // the plane of T
            DMesh3 a = new DMesh3();
            a.AppendVertex(new Vector3d(0, 0, 0)); a.AppendVertex(new Vector3d(2, 0, 0));
            a.AppendVertex(new Vector3d(1, 1, 0)); a.AppendVertex(new Vector3d(1, 0, 1));
            a.AppendTriangle(0, 1, 2); a.AppendTriangle(1, 0, 3);
            DMesh3 b = new DMesh3();
            b.AppendVertex(new Vector3d(1, -1, -1)); b.AppendVertex(new Vector3d(1, 1, 1)); b.AppendVertex(new Vector3d(5, 3, -2));
            b.AppendTriangle(0, 1, 2);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { a, b });
            arr.Compute();
            Assert.True(arr.MergedPoints >= 2);
            AssertDistinctPoints(arr);
            var at = arr.Output.Where(o => o.Mesh == 0).SelectMany(o => new[] { o.A, o.B, o.C }).Distinct()
                .Where(id => arr.PointToVector(id) == new Vector3d(1, 0, 0)).ToList();
            Assert.Single(at);
            // the pieces of the two triangles of a still meet along their common edge (split at the point)
            var directed = new HashSet<long>();
            foreach (var ot in arr.Output.Where(o => o.Mesh == 0))
                foreach (var e in new[] { Tuple.Create(ot.A, ot.B), Tuple.Create(ot.B, ot.C), Tuple.Create(ot.C, ot.A) })
                    directed.Add(((long)e.Item1 << 32) | (uint)e.Item2);
            int p0 = arr.VertexPoints[0][0], p1 = arr.VertexPoints[0][1];
            Assert.Contains(((long)p0 << 32) | (uint)at[0], directed);
            Assert.Contains(((long)at[0] << 32) | (uint)p0, directed);
            Assert.Contains(((long)at[0] << 32) | (uint)p1, directed);
            Assert.Contains(((long)p1 << 32) | (uint)at[0], directed);
        }
    }
}
