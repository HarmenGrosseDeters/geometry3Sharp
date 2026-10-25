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
                ExactPoint3 x = arr.Exact(id);
                Vector3d a = arr.Approx(id);
                double e = arr.ApproxError(id);
                Assert.True(e >= 0 && double.IsInfinity(e) == false);
                for (int k = 0; k < 3; ++k) {
                    double xk = ExactPoint3.ToDouble(x[k], x.W, 0);
                    // (the rounding of the exact value to double: below 1 ulp)
                    Assert.True(Math.Abs(a[k] - xk) <= e + 2.3e-16 * Math.Abs(xk), $"point {id} ({arr.KindOf(id)}): {a[k]} vs {xk}, bound {e}");
                }
            }
            if (seed % 4 < 3)
                Assert.Contains(ExactMeshArrangement.PointKind.Planes, kinds);
            Assert.Contains(ExactMeshArrangement.PointKind.EdgePlane, kinds);
            if (seed % 4 == 3)
                Assert.Contains(ExactMeshArrangement.PointKind.Segments, kinds);
        }

        static void AssertWithinBound(ExactMeshArrangement arr, int id)
        {
            ExactPoint3 x = arr.Exact(id);
            Vector3d a = arr.Approx(id);
            double e = arr.ApproxError(id);
            for (int k = 0; k < 3; ++k) {
                double xk = ExactPoint3.ToDouble(x[k], x.W, 0);
                Assert.True(Math.Abs(a[k] - xk) <= e + 2.3e-16 * Math.Abs(xk), $"point {id} ({arr.KindOf(id)}): {a[k]} vs {xk}, bound {e}");
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
                var pts = t.Points.Select(arr.Canonical).Concat(t.Corners).Distinct().ToList();
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
