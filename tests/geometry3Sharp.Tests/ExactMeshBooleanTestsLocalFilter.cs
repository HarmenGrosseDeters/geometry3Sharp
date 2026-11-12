using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// L3 of the tail case (docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 4): the triangulation's Orient and InCircle
    /// get a second filter on the points' coordinates relative to the triangle's first corner (each the exact difference,
    /// rounded once). The predicates' values are exact, so the results must be bit-identical with and without it; it
    /// must decide where the approximations' absolute errors are large against the points' distances (a knot and a copy
    /// rotated by 1e-12), and never decide a sign that the exact stage does not give. (The class name keeps the mutation
    /// shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsLocalFilter
    {
        // a small knot (dense self-intersections; its coordinates near 0 make a fine grid, so the points from three planes
        // exceed the fixed width and reach the BigInteger stage) and a copy rotated by angle about a point near its
        // centre: for a tiny angle every triangle meets its copy almost in its plane, and the points of the copies'
        // intersections lie closer together than the approximations' absolute errors, as in 55928's nearly duplicate
        // regions
        internal static (DMesh3, DMesh3) NearCopy(int segments, double angle)
        {
            DMesh3 a = ExactMeshBooleanTestsBudget.Knot(segments, 6, 4, 0.35, 3), b = new DMesh3(a);
            MeshTransforms.Rotate(b, a.CachedBounds.Center + new Vector3d(0.01, 0.02, 0.005), Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, angle * 180 / Math.PI));
            return (a, b);
        }

        static long Directed(int a, int b) { return ((long)a << 32) | (uint)b; }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, bool local)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, LocalTriangulationFilters = local };
            Assert.True(s.Compute(), s.Error);
            return s;
        }

        [Theory]
        [InlineData(Ops.Union, 0.35)]
        [InlineData(Ops.Difference, 0.35)]
        [InlineData(Ops.Union, 1e-12)]
        [InlineData(Ops.Difference, 1e-12)]
        [InlineData(Ops.Intersection, 1e-12)]
        [InlineData(Ops.Union, 1e-7)]
        public void LocalFilter_SameResult_AndDecidesWhereThePointsAreDense(Ops op, double angle)
        {
            (DMesh3 a, DMesh3 b) = NearCopy(30, angle);
            ExactMeshBoolean on = Computed(a, b, op, true), off = Computed(a, b, op, false);
            Assert.Equal("", Invariance.Difference(off.Result, on.Result));
            Assert.Equal(off.Counts["pieces"], on.Counts["pieces"]);
            Assert.Equal(0, off.Counts["l3_local_decided"]);
            Assert.Equal(0, off.Counts["l3_local_points"]);
            Assert.True(Invariance.AllFinite(on.Result));
            Assert.Equal(0, off.Counts["l3_construction_zeros"]);
            // the BigInteger stage is reached either way. In the plain knot its Orient calls are true zeros, collinear
            // points of the segments, which the constructions decide (no point converted); the local filter decides
            // where the first filter's absolute errors exceed the points' distances (the near copy, 1e4 and more here)
            Assert.True(on.Counts["l3_construction_zeros"] > 1000, $"{on.Counts["l3_construction_zeros"]} construction zeros");
            if (angle < 1e-3) {
                Assert.True(on.Counts["l3_local_points"] > 1000, $"{on.Counts["l3_local_points"]} local points");
                Assert.True(on.Counts["l3_local_decided"] > 5000, $"{on.Counts["l3_local_decided"]} decided");
            }
        }

        [Fact]
        public void LocalFilter_AgreesWithTheExactStage_IncludingCollinearAndCocircularPoints()
        {
            // every decision of the local filter against the exact predicate, on the densest triangles of the near copy:
            // triples with two ends of a segment and a third point (the points on the segment give exact zeros, which a
            // filter must never decide), random triples and quadruples, and quadruples of a piece and its neighbour's
            // far corner (the Delaunay tests, often nearly cocircular)
            (DMesh3 a, DMesh3 b) = NearCopy(30, 1e-12);
            ExactMeshBoolean s = Computed(a, b, Ops.Union, true);
            ExactMeshArrangement arr = s.Arrangement;
            var byTriangle = new Dictionary<int, List<ExactMeshArrangement.OutputTriangle>>();
            foreach (var ot in arr.Output)
                (byTriangle.TryGetValue(ot.Source, out var l) ? l : (byTriangle[ot.Source] = new List<ExactMeshArrangement.OutputTriangle>())).Add(ot);
            var rand = new Random(7);
            int zeros = 0, decided = 0, tested = 0;
            long constructionZeros = 0;
            foreach (int ti in byTriangle.Keys.OrderByDescending(k => byTriangle[k].Count).ThenBy(k => k).Take(6)) {
                var t = arr.Triangles[ti];
                var local = new ExactMeshArrangement.LocalPoints(arr, t);
                int[] pts = byTriangle[ti].SelectMany(o => new[] { o.A, o.B, o.C }).Distinct().OrderBy(p => p).ToArray();
                Func<int, int, int, int> exactOrient = (p, q, r) => { arr.DisableFilters = true; try { return arr.Orient(t, p, q, r); } finally { arr.DisableFilters = false; } };
                Func<int, int, int, int, int> exactInCircle = (p, q, r, x) => { arr.DisableFilters = true; try { return arr.InCircle(t, p, q, r, x); } finally { arr.DisableFilters = false; } };
                foreach (var seg in t.Segments) {
                    int sp = arr.Canonical(seg.P), sq = arr.Canonical(seg.Q);
                    foreach (int r in pts) {
                        if (r == sp || r == sq) continue;
                        int e = exactOrient(sp, sq, r);
                        long before = local.Decided;
                        Assert.Equal(e, arr.Orient(t, sp, sq, r, local));
                        if (e == 0) {
                            zeros++;
                            Assert.Equal(before, local.Decided);
                        }
                        // (the triples of a segment's ends and a point on it: zeros by their constructions)
                        tested++;
                    }
                }
                for (int k = 0; k < 3000; ++k) {
                    int p = pts[rand.Next(pts.Length)], q = pts[rand.Next(pts.Length)], r = pts[rand.Next(pts.Length)], x = pts[rand.Next(pts.Length)];
                    Assert.Equal(exactOrient(p, q, r), arr.Orient(t, p, q, r, local));
                    if (p != q && q != r && r != p && exactOrient(p, q, r) != 0)
                        Assert.Equal(exactInCircle(p, q, r, x), arr.InCircle(t, p, q, r, x, local));
                    tested++;
                }
                // a piece and the far corner of a piece across one of its edges
                var pieces = byTriangle[ti];
                var edgeOwner = new Dictionary<long, int>(EdgeKeyComparer.Instance);
                for (int k = 0; k < pieces.Count; ++k) {
                    var o = pieces[k];
                    edgeOwner[Directed(o.A, o.B)] = k; edgeOwner[Directed(o.B, o.C)] = k; edgeOwner[Directed(o.C, o.A)] = k;
                }
                for (int k = 0; k < pieces.Count; ++k) {
                    var o = pieces[k];
                    int nb;
                    if (edgeOwner.TryGetValue(Directed(o.B, o.A), out nb) == false || nb == k) continue;
                    var n = pieces[nb];
                    int far = (n.A != o.A && n.A != o.B) ? n.A : (n.B != o.A && n.B != o.B) ? n.B : n.C;
                    int c = exactOrient(o.A, o.B, o.C) > 0 ? 1 : -1;
                    int[] abc = c > 0 ? new[] { o.A, o.B, o.C } : new[] { o.B, o.A, o.C };
                    Assert.Equal(exactInCircle(abc[0], abc[1], abc[2], far), arr.InCircle(t, abc[0], abc[1], abc[2], far, local));
                    tested++;
                }
                decided += (int)local.Decided;
                constructionZeros += local.Zeros;
            }
            Assert.True(constructionZeros > 50, $"{constructionZeros} zeros by the constructions");
            Assert.True(zeros > 100, $"{zeros} collinear triples");
            Assert.True(decided > tested / 10, $"{decided} of {tested} decided by the local filter");
        }

        [Fact]
        public void LocalFilter_ExactlyCocircularAndCollinearPoints_LeftToTheExactStage()
        {
            // two triangles far apart, one with a coordinate of 1e-30, so the grid is fine and the second triangle's grid
            // coordinates are huge: the approximations' absolute errors are far larger than a grid unit. Near its first
            // corner o, rational points with denominators beyond the fixed width (the BigInteger stage) on a circle of
            // radius 1/16 and on a line, 1000 grid units from o: the first filter cannot decide them, the local one must
            // decide the points off the circle and the line, and must leave the exactly cocircular and collinear ones to
            // the exact stage, whose rounding of the local coordinates (2^-52 of 1000) exceeds the circle's terms
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(-5e5, -5e5, 1e-30)); mesh.AppendVertex(new Vector3d(-5e5 + 1, -5e5, 0)); mesh.AppendVertex(new Vector3d(-5e5, -5e5 + 1, 0));
            mesh.AppendVertex(new Vector3d(5e5, 5e5, 0)); mesh.AppendVertex(new Vector3d(5e5 + 1, 5e5, 0)); mesh.AppendVertex(new Vector3d(5e5, 5e5 + 1, 0));
            mesh.AppendTriangle(0, 1, 2);
            mesh.AppendTriangle(3, 4, 5);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
            arr.Compute();
            var t = arr.Triangles[1];
            Assert.Equal(2, t.Axis);
            ExactPoint3 o = arr.Exact(t.Corners[0]);
            Assert.True(o.W.IsOne);
            System.Numerics.BigInteger cx = o.X + 1000, cy = o.Y + 700;
            // (n^2 - m^2, 2 m n) / (n^2 + m^2) is on the unit circle; scaled by 1/16 and by (1 + 2^-10) for a point outside
            System.Numerics.BigInteger n = System.Numerics.BigInteger.Pow(2, 131) + 1;
            Func<System.Numerics.BigInteger, bool, int> onCircle = (m, outside) => {
                System.Numerics.BigInteger w = n * n + m * m, f = outside ? 1025 : 1024;
                return arr.AddPoint(ExactPoint3.Create(16 * 1024 * cx * w + f * (n * n - m * m), 16 * 1024 * cy * w + f * 2 * m * n, 16 * 1024 * o.Z * w, 16 * 1024 * w));
            };
            int p0 = onCircle(n / 7, false), p1 = onCircle(n / 2, false), p2 = onCircle(2 * n + 3, false), p3 = onCircle(5 * n + 7, false), q = onCircle(n / 3 + 1, true);
            // (o + (1000, 700) + j / 256 (3, 5), in exact arithmetic with the denominator d: collinear)
            System.Numerics.BigInteger d = System.Numerics.BigInteger.Pow(2, 210) + 1;
            Func<int, int, int> onLine = (j, off) => arr.AddPoint(ExactPoint3.Create(cx * d + 3 * j * (d >> 8) + off * (d >> 10), cy * d + 5 * j * (d >> 8), o.Z * d, d));
            int l0 = onLine(1, 0), l1 = onLine(2, 0), l2 = onLine(4, 0), l3 = onLine(3, 1);
            foreach (int id in new[] { p0, p1, p2, p3, q, l0, l1, l2, l3 }) {
                Assert.False(arr.IsExactFixed(id), $"point {id} fixed, o.X bits {o.X.ToByteArray().Length * 8}");
                Assert.True(arr.ApproxError(id) > 1e6, $"approximation error {arr.ApproxError(id)}");
            }
            var local = new ExactMeshArrangement.LocalPoints(arr, t);
            Assert.Equal(0, arr.InCircle(t, p0, p1, p2, p3, local));
            Assert.Equal(0, arr.InCircle(t, p1, p2, p3, p0, local));
            Assert.Equal(0, arr.Orient(t, l0, l1, l2, local));
            Assert.Equal(0, arr.Orient(t, l2, l0, l1, local));
            Assert.Equal(0, local.Decided);
            int o012 = arr.Orient(t, p0, p1, p2, local);
            Assert.NotEqual(0, o012);
            Assert.Equal(-o012, arr.InCircle(t, p0, p1, p2, q, local));
            Assert.NotEqual(0, arr.Orient(t, l0, l1, l3, local));
            Assert.True(local.Decided >= 3, $"{local.Decided} decided");
            // the same as the exact stage alone
            arr.DisableFilters = true;
            Assert.Equal(-o012, arr.InCircle(t, p0, p1, p2, q));
            Assert.Equal(o012, arr.Orient(t, p0, p1, p2));
        }

        [Fact]
        public void ConstructionZeros_NotFromAPlaneParallelToTheTriangle()
        {
            // a triangle t, a coplanar one u whose plane keeps its own key (ShareCoplanarConstructions off), and three
            // vertical triangles: the points of u's plane and two of the vertical planes all lie on u's plane, which is
            // t's, but they are not collinear; a plane shared by three points is evidence of collinearity only where it
            // meets t's plane in a line. And three points of one vertical plane are collinear (on its line in t)
            var mesh = new DMesh3();
            Func<Vector3d, Vector3d, Vector3d, int> tri = (a, b, c) => mesh.AppendTriangle(mesh.AppendVertex(a), mesh.AppendVertex(b), mesh.AppendVertex(c));
            tri(new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(0, 10, 0));        // t
            tri(new Vector3d(1, 1, 0), new Vector3d(9, 1, 0), new Vector3d(1, 9, 0));          // u, coplanar
            // (the vertical planes meet u's plane outside t and u: their points there are new, constructed with u's plane)
            tri(new Vector3d(12, -5, -5), new Vector3d(12, 40, -5), new Vector3d(12, 0, 40));      // x = 12
            tri(new Vector3d(-5, 12, -5), new Vector3d(40, 12, -5), new Vector3d(0, 12, 40));      // y = 12
            tri(new Vector3d(30, 0, -5), new Vector3d(0, 30, -5), new Vector3d(15, 15, 40));       // x + y = 30
            tri(new Vector3d(-5, 20, -5), new Vector3d(40, 20, -5), new Vector3d(0, 20, 40));      // y = 20
            var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh }) { SelfIntersections = true, ShareCoplanarConstructions = false };
            arr.Compute();
            var t = arr.Triangles[0];
            int pu = 4 * 1, px = 4 * 2, py = 4 * 3, pd = 4 * 4, p6 = 4 * 5;
            Assert.Equal(6, arr.Triangles.Count);
            Assert.NotEqual(arr.CanonicalPlane(4 * 0), arr.CanonicalPlane(pu));
            int a = arr.PlanesPointImplicit(pu, px, py), b = arr.PlanesPointImplicit(pu, px, pd), c = arr.PlanesPointImplicit(pu, py, pd);
            var local = new ExactMeshArrangement.LocalPoints(arr, t);
            // (constructed with u's plane: the case the guard is for)
            Assert.True(arr.OnPlaneByConstruction(a, pu) && arr.OnPlaneByConstruction(b, pu) && arr.OnPlaneByConstruction(c, pu));
            Assert.False(local.CollinearByConstruction(a, b, c));
            Assert.NotEqual(0, arr.Orient(t, a, b, c, local));
            arr.DisableFilters = true;
            Assert.NotEqual(0, arr.Orient(t, a, b, c));
            arr.DisableFilters = false;
            // (on x = 12: (12, 12), (12, 18), (12, 20), from u's plane and x = 12 with another plane each)
            int d = arr.PlanesPointImplicit(pu, px, p6);
            Assert.True(local.CollinearByConstruction(a, b, d));
            Assert.Equal(0, arr.Orient(t, a, b, d, local));
            arr.DisableFilters = true;
            Assert.Equal(0, arr.Orient(t, a, b, d));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void LocalFilter_SameResult_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = NearCopy(20, 1e-12);
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean on = Computed(ta, tb, Ops.Difference, true), off = Computed(ta, tb, Ops.Difference, false);
            Assert.Equal("", Invariance.Difference(off.Result, on.Result));
            if (sim.IsExact) {
                ExactMeshBoolean plain = Computed(a, b, Ops.Difference, true);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(on.Result, sim)));
            }
        }

        [Fact]
        public void LocalFilter_SerialEqualsParallel_RepeatedAndReused()
        {
            (DMesh3 a, DMesh3 b) = NearCopy(30, 1e-12);
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, Ops.Difference, true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute());
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                Assert.Equal(serial.Counts["l3_local_decided"], reused.Counts["l3_local_decided"]);
                Assert.Equal(serial.Counts["l3_local_points"], reused.Counts["l3_local_points"]);
            }
        }
    }
}
