using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact parts of ExactMeshBoolean: number conversion, predicates on homogeneous points (checked
    /// against ExactPredicates on doubles), plane intersection points and the constrained triangulation.
    /// </summary>
    public class ExactBooleanBuildingBlockTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(-0.1)]
        [InlineData(1e-300)]
        [InlineData(4.9e-324)]      // smallest subnormal
        [InlineData(-1.7976931348623157e308)]
        [InlineData(123456.789)]
        public void Decompose_IsExact(double v)
        {
            BigInteger m; int e;
            ExactPoint3.Decompose(v, out m, out e);
            Assert.True(m.IsZero || !m.IsEven);
            Assert.Equal(v, ExactPoint3.ToDouble(m, BigInteger.One, e));
        }

        [Fact]
        public void ToDouble_RoundsQuotientsToNearUlp()
        {
            var rand = new Random(3);
            for (int i = 0; i < 1000; ++i) {
                long a = rand.Next(1, int.MaxValue) * (long)rand.Next(1, 1000), b = rand.Next(1, int.MaxValue);
                double expected = (double)a / b;
                double got = ExactPoint3.ToDouble(a, b, 0);
                Assert.True(Math.Abs(got - expected) <= 2 * Math.Abs(expected) * 1.2e-16, $"{a}/{b}: {got} vs {expected}");
            }
            // huge numbers and scales do not overflow
            BigInteger big = BigInteger.Pow(10, 400);
            Assert.Equal(1.0, ExactPoint3.ToDouble(big, big, 0));
            Assert.Equal(Math.Pow(2, -1060), ExactPoint3.ToDouble(1, 1, -1060));
        }

        [Fact]
        public void Create_IsCanonical()
        {
            ExactPoint3 a = ExactPoint3.Create(2, 4, 6, 2), b = ExactPoint3.Create(-3, -6, -9, -3), c = ExactPoint3.FromInteger(1, 2, 3);
            Assert.Equal(c, a);
            Assert.Equal(c, b);
            Assert.Equal(c.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(c, ExactPoint3.Create(1, 2, 3, 2));
            Assert.Throws<ArgumentException>(() => ExactPoint3.Create(1, 2, 3, 0));
        }


        // an arrangement of a few random triangles, so that the predicates can be compared on its points
        static ExactMeshArrangement Arrangement(List<Vector3d> pts, out ExactMeshArrangement.InputTriangle tri)
        {
            DMesh3 mesh = new DMesh3();
            foreach (Vector3d p in pts)
                mesh.AppendVertex(p);
            mesh.AppendTriangle(0, 1, 2);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
            arr.Compute();
            tri = arr.Triangles[0];
            return arr;
        }

        [Fact]
        public void Orient_And_InCircle_MatchExactPredicates()
        {
            var rand = new Random(11);
            for (int iter = 0; iter < 300; ++iter) {
                // points in the plane z = 0 (projection drops z), some on a coarse grid to get exact ties
                Func<Vector3d> next = () => (iter % 2 == 0)
                    ? new Vector3d(rand.Next(-3, 4), rand.Next(-3, 4), 0)
                    : new Vector3d(rand.NextDouble() * 1e6 - 5e5, rand.NextDouble() * 1e-3, 0);
                var pts = new List<Vector3d>() { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), next(), next(), next(), next() };
                // keep the triangle orientation, swap the winding sometimes
                if (iter % 3 == 0) { var tmp = pts[1]; pts[1] = pts[2]; pts[2] = tmp; }
                ExactMeshArrangement.InputTriangle tri;
                var arr = Arrangement(pts, out tri);
                int sign = ExactPredicates.Orient2d(pts[0].xy, pts[1].xy, pts[2].xy);
                int[] id = Enumerable.Range(0, pts.Count).Select(k => arr.VertexPoints[0][k]).ToArray();
                for (int k = 0; k < 20; ++k) {
                    int a = rand.Next(pts.Count), b = rand.Next(pts.Count), c = rand.Next(pts.Count), d = rand.Next(pts.Count);
                    Assert.Equal(sign * ExactPredicates.Orient2d(pts[a].xy, pts[b].xy, pts[c].xy), arr.Orient(tri, id[a], id[b], id[c]));
                    // both are the determinant sign; the arrangement's is relative to the triangle orientation
                    Assert.Equal(sign * ExactPredicates.InCircle(pts[a].xy, pts[b].xy, pts[c].xy, pts[d].xy), arr.InCircle(tri, id[a], id[b], id[c], id[d]));
                }
            }
        }

        [Fact]
        public void PlanesPoint_LiesOnAllThreePlanes()
        {
            var rand = new Random(5);
            for (int iter = 0; iter < 50; ++iter) {
                var pts = new List<Vector3d>();
                for (int k = 0; k < 9; ++k)
                    pts.Add(new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * Math.Pow(10, rand.Next(-5, 6)));
                DMesh3 mesh = new DMesh3();
                foreach (Vector3d p in pts) mesh.AppendVertex(p);
                mesh.AppendTriangle(0, 1, 2); mesh.AppendTriangle(3, 4, 5); mesh.AppendTriangle(6, 7, 8);
                var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
                arr.Compute();
                var planes = arr.Triangles.Select(t => t.P).ToArray();
                int id = arr.PlanesPoint(planes[0], planes[1], planes[2]);
                ExactPoint3 x = arr.Exact(id);
                foreach (var pl in planes)
                    Assert.True((pl.A * x.X + pl.B * x.Y + pl.C * x.Z - pl.D * x.W).IsZero);
                // the same point from the planes in another order is the same point id
                Assert.Equal(id, arr.PlanesPoint(planes[2], planes[0], planes[1]));
                // an edge crossing a plane: the direct formula gives the same point as three planes
                var t0 = arr.Triangles[0];
                for (int k = 0; k < 3; ++k) {
                    int p = t0.Corners[k], q = t0.Corners[(k + 1) % 3];
                    var pl = planes[1];
                    var P = arr.Exact(p); var Q = arr.Exact(q);
                    var sp = pl.A * P.X + pl.B * P.Y + pl.C * P.Z - pl.D; var sq = pl.A * Q.X + pl.B * Q.Y + pl.C * Q.Z - pl.D;
                    if (sp.Sign * sq.Sign >= 0) continue;
                    Assert.Equal(arr.PlanesPoint(t0.P, t0.EdgePlanes[k], pl), arr.EdgePlanePoint(p, q, pl));
                }
            }
        }


        [Fact]
        public void Triangulation_PointsAndConstraints()
        {
            var rand = new Random(7);
            for (int iter = 0; iter < 20; ++iter) {
                // grid points (many collinear and cocircular), in an outer triangle
                var pts = new List<Vector3d>() { new Vector3d(0, 0, 0), new Vector3d(16, 0, 0), new Vector3d(0, 16, 0) };
                var seen = new HashSet<Vector2d>(pts.Select(p => p.xy));
                while (pts.Count < 40) {
                    var p = new Vector3d(rand.Next(0, 17), rand.Next(0, 17), 0);
                    if (p.x + p.y <= 16 && seen.Add(p.xy)) pts.Add(p);
                }
                DMesh3 mesh = new DMesh3();
                foreach (Vector3d p in pts) mesh.AppendVertex(p);
                mesh.AppendTriangle(0, 1, 2);
                var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
                arr.Compute();
                var tri = arr.Triangles[0];
                int[] id = Enumerable.Range(0, pts.Count).Select(k => arr.VertexPoints[0][k]).ToArray();
                var tr = new ExactTriangulation2(id[0], id[1], id[2], (a, b, c) => arr.Orient(tri, a, b, c), (a, b, c, d) => arr.InCircle(tri, a, b, c, d));
                for (int k = 3; k < pts.Count; ++k)
                    tr.InsertPoint(id[k]);
                // points are found by walking from the last insertion, not by searching all triangles
                Assert.Equal(0, tr.LinearSearches);
                // and the triangulation is kept Delaunay while they are inserted (before MakeDelaunay)
                {
                    var t0 = tr.Triangles().ToList();
                    var opp = new Dictionary<long, int>();
                    foreach (Index3i t in t0)
                        for (int k = 0; k < 3; ++k)
                            opp[((long)t[k] << 32) | (uint)t[(k + 1) % 3]] = t[(k + 2) % 3];
                    foreach (Index3i t in t0)
                        for (int k = 0; k < 3; ++k)
                            if (opp.TryGetValue(((long)t[(k + 1) % 3] << 32) | (uint)t[k], out int d))
                                Assert.True(arr.InCircle(tri, t[k], t[(k + 1) % 3], t[(k + 2) % 3], d) <= 0, "not Delaunay after the insertions");
                }
                tr.MakeDelaunay();
                // non-crossing constraints between random point pairs, without points on them
                var constraints = new List<Tuple<int, int>>();
                for (int tries = 0; tries < 200 && constraints.Count < 6; ++tries) {
                    int a = rand.Next(pts.Count), b = rand.Next(pts.Count);
                    if (a == b) continue;
                    bool ok = true;
                    // (closed box test: AxisAlignedBox2d.Contains is strict)
                    double x0 = Math.Min(pts[a].x, pts[b].x), x1 = Math.Max(pts[a].x, pts[b].x);
                    double y0 = Math.Min(pts[a].y, pts[b].y), y1 = Math.Max(pts[a].y, pts[b].y);
                    for (int k = 0; k < pts.Count && ok; ++k)
                        if (k != a && k != b && ExactPredicates.Orient2d(pts[a].xy, pts[b].xy, pts[k].xy) == 0
                            && pts[k].x >= x0 && pts[k].x <= x1 && pts[k].y >= y0 && pts[k].y <= y1)
                            ok = false;
                    foreach (var c in constraints) {
                        if (!ok) break;
                        int c0 = c.Item1, c1 = c.Item2;
                        if (c0 == a || c0 == b || c1 == a || c1 == b) continue;
                        int o1 = ExactPredicates.Orient2d(pts[a].xy, pts[b].xy, pts[c0].xy), o2 = ExactPredicates.Orient2d(pts[a].xy, pts[b].xy, pts[c1].xy);
                        int o3 = ExactPredicates.Orient2d(pts[c0].xy, pts[c1].xy, pts[a].xy), o4 = ExactPredicates.Orient2d(pts[c0].xy, pts[c1].xy, pts[b].xy);
                        if (o1 * o2 <= 0 && o3 * o4 <= 0) ok = false;
                    }
                    if (ok) constraints.Add(Tuple.Create(a, b));
                }
                foreach (var c in constraints)
                    tr.InsertConstraint(id[c.Item1], id[c.Item2]);
                // the crossed edges are found by walking along the constraint, not by searching all edges
                Assert.Equal(0, tr.LinearSearches);
                tr.MakeDelaunay();

                var tris = tr.Triangles().ToList();
                // Euler: T = 2V - B - 2 with B points on the boundary
                int boundary = pts.Count(p => p.y == 0 || p.x == 0 || p.x + p.y == 16);
                Assert.Equal(2 * pts.Count - boundary - 2, tris.Count);
                double area = 0;
                var edges = new HashSet<long>();
                foreach (Index3i t in tris) {
                    Assert.Equal(1, arr.Orient(tri, t.a, t.b, t.c));
                    Vector2d a = arr.PointToVector(t.a).xy, b = arr.PointToVector(t.b).xy, c = arr.PointToVector(t.c).xy;
                    area += 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                    edges.Add(ExactMeshArrangement.EdgeKey(t.a, t.b)); edges.Add(ExactMeshArrangement.EdgeKey(t.b, t.c)); edges.Add(ExactMeshArrangement.EdgeKey(t.c, t.a));
                }
                Assert.Equal(128.0, area, 9);
                foreach (var c in constraints)
                    Assert.Contains(ExactMeshArrangement.EdgeKey(id[c.Item1], id[c.Item2]), edges);
                // constrained Delaunay: across every edge that is not a constraint, the opposite vertex is not inside the circle
                var constrainedKeys = new HashSet<long>(constraints.Select(c => ExactMeshArrangement.EdgeKey(id[c.Item1], id[c.Item2])));
                var opposite = new Dictionary<long, int>();
                foreach (Index3i t in tris)
                    for (int k = 0; k < 3; ++k)
                        opposite[((long)t[k] << 32) | (uint)t[(k + 1) % 3]] = t[(k + 2) % 3];
                foreach (Index3i t in tris)
                    for (int k = 0; k < 3; ++k) {
                        int a = t[k], b = t[(k + 1) % 3];
                        if (constrainedKeys.Contains(ExactMeshArrangement.EdgeKey(a, b))) continue;
                        if (opposite.TryGetValue(((long)b << 32) | (uint)a, out int d))
                            Assert.True(arr.InCircle(tri, a, b, t[(k + 2) % 3], d) <= 0, "not Delaunay");
                    }
            }
        }
        static DMesh3 RotatedBox(Vector3d center, double size, Quaterniond rotation)
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(center, new Vector3d(size, size, size)) }.Generate().MakeDMesh();
            MeshTransforms.Rotate(mesh, center, rotation);
            return mesh;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Arrangement_ThreeMeshes_PiecesAreClosedAndCoverTheInput(int seed)
        {
            // three mutually intersecting boxes: triple points, where segments of two meshes cross in a triangle of the third
            var rand = new Random(seed);
            Func<Quaterniond> rot = () => new Quaterniond(new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized, rand.NextDouble() * 360);
            var meshes = new List<DMesh3>() {
                RotatedBox(Vector3d.Zero, 1, rot()), RotatedBox(new Vector3d(0.7, 0.2, 0.1), 0.8, rot()), RotatedBox(new Vector3d(0.2, 0.7, -0.3), 0.9, rot())
            };
            var arr = new ExactMeshArrangement(meshes);
            arr.Compute();
            Assert.True(arr.SegmentCrossings > 0);

            for (int m = 0; m < meshes.Count; ++m) {
                // every directed edge of the pieces of one mesh has exactly one opposite
                var directed = new Dictionary<long, int>();
                foreach (var ot in arr.Output.Where(o => o.Mesh == m)) {
                    foreach (var e in new[] { Tuple.Create(ot.A, ot.B), Tuple.Create(ot.B, ot.C), Tuple.Create(ot.C, ot.A) }) {
                        long key = ((long)e.Item1 << 32) | (uint)e.Item2;
                        directed[key] = directed.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
                foreach (var kv in directed) {
                    long rev = ((kv.Key & 0xFFFFFFFF) << 32) | (kv.Key >> 32);
                    Assert.Equal(1, kv.Value);
                    Assert.True(directed.ContainsKey(rev), "open edge in the pieces of mesh " + m);
                }
                // the pieces of each input triangle have its area and orientation
                foreach (int ti in arr.MeshTriangles[m]) {
                    var src = arr.Triangles[ti];
                    Vector3d a = arr.PointToVector(src.Corners[0]), b = arr.PointToVector(src.Corners[1]), c = arr.PointToVector(src.Corners[2]);
                    Vector3d n = (b - a).Cross(c - a);
                    double area = 0;
                    foreach (var ot in arr.Output.Where(o => o.Source == ti)) {
                        Vector3d pa = arr.PointToVector(ot.A), pb = arr.PointToVector(ot.B), pc = arr.PointToVector(ot.C);
                        double signed = 0.5 * (pb - pa).Cross(pc - pa).Dot(n) / n.Length;
                        Assert.True(signed > -1e-12);
                        area += signed;
                    }
                    Assert.Equal(0.5 * n.Length, area, 9);
                }
            }
        }

        [Fact]
        public void Filters_MatchExact_OnRationalPoints()
        {
            // a triangle in the plane z = 0, and rational points whose double approximations are inexact
            foreach (bool filters in new[] { true, false }) {
                ExactMeshArrangement.InputTriangle tri;
                var arr = Arrangement(new List<Vector3d>() { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) }, out tri);
                arr.DisableFilters = !filters;
                Func<long, long, long, int> pt = (x, y, w) => arr.AddPoint(ExactPoint3.Create(x, y, 0, w));
                // on the circle of radius 5/3: cocircular
                int c0 = pt(5, 0, 3), c1 = pt(3, 4, 3), c2 = pt(0, 5, 3), c3 = pt(-4, 3, 3), c4 = pt(-5, 0, 3);
                Assert.Equal(0, arr.InCircle(tri, c0, c1, c2, c3));
                Assert.Equal(0, arr.InCircle(tri, c1, c2, c3, c4));
                Assert.Equal(1, arr.InCircle(tri, c0, c1, c2, pt(1, 1, 3)));
                Assert.Equal(-1, arr.InCircle(tri, c0, c1, c2, pt(16, 0, 3)));
                // collinear: (1/3, 1/7) + k (2/3, 3/7)
                int l0 = pt(7, 3, 21), l1 = pt(21, 12, 21), l2 = pt(35, 21, 21);
                Assert.Equal(0, arr.Orient(tri, l0, l1, l2));
                Assert.Equal(1, arr.Orient(tri, l0, l1, pt(7, 4, 21)));
                // keys along d = (25, -1, 0): 25 * 7/25 - 7 = 0 for (7/25, 7) and the origin (in doubles 8.9e-16)
                int k0 = pt(7, 175, 25), k1 = pt(0, 0, 1);
                Assert.Equal(0, arr.compare_along(25, -1, 0, k0, k1));
                Assert.Equal(-1, arr.compare_along(25, -1, 0, pt(7, 176, 25), k1));
            }
        }
    }
}
