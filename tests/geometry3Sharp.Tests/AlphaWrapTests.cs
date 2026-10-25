using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Alpha wrapping (MeshAlphaWrap) and its building blocks: the exact InSphere predicate, the 3D Delaunay
    /// triangulation (Delaunay3) and the distance queries (AlphaWrapOracle).
    /// </summary>
    public class AlphaWrapTests
    {
        // ---- InSphere ----

        static int InSphereInt(long[][] p)
        {
            // rows (x, y, z, x^2 + y^2 + z^2, 1) relative to e; sign convention as ExactPredicates.InSphere
            BigInteger[,] m = new BigInteger[4, 4];
            for (int i = 0; i < 4; ++i) {
                BigInteger x = new BigInteger(p[i][0]) - p[4][0], y = new BigInteger(p[i][1]) - p[4][1], z = new BigInteger(p[i][2]) - p[4][2];
                m[i, 0] = x; m[i, 1] = y; m[i, 2] = z; m[i, 3] = x * x + y * y + z * z;
            }
            BigInteger det = Det4(m);
            BigInteger[,] o = new BigInteger[3, 3];
            for (int i = 0; i < 3; ++i)
                for (int k = 0; k < 3; ++k) o[i, k] = new BigInteger(p[i + 1][k]) - p[0][k];
            BigInteger orient = o[0, 0] * (o[1, 1] * o[2, 2] - o[1, 2] * o[2, 1]) - o[0, 1] * (o[1, 0] * o[2, 2] - o[1, 2] * o[2, 0])
                + o[0, 2] * (o[1, 0] * o[2, 1] - o[1, 1] * o[2, 0]);
            // for a positively oriented tetrahedron this determinant is positive inside the sphere
            return -det.Sign;
        }

        static BigInteger Det4(BigInteger[,] m)
        {
            BigInteger det = 0;
            for (int c = 0; c < 4; ++c) {
                BigInteger[,] s = new BigInteger[3, 3];
                for (int i = 1; i < 4; ++i) {
                    int cc = 0;
                    for (int k = 0; k < 4; ++k) {
                        if (k == c) continue;
                        s[i - 1, cc++] = m[i, k];
                    }
                }
                BigInteger minor = s[0, 0] * (s[1, 1] * s[2, 2] - s[1, 2] * s[2, 1]) - s[0, 1] * (s[1, 0] * s[2, 2] - s[1, 2] * s[2, 0])
                    + s[0, 2] * (s[1, 0] * s[2, 1] - s[1, 1] * s[2, 0]);
                det += ((c & 1) == 0 ? 1 : -1) * m[0, c] * minor;
            }
            return det;
        }

        [Fact]
        public void InSphere_SignConvention()
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.AxisX, c = Vector3d.AxisY, d = Vector3d.AxisZ;
            Assert.Equal(1, ExactPredicates.Orient3d(a, b, c, d));
            Assert.Equal(1, ExactPredicates.InSphere(a, b, c, d, new Vector3d(0.25, 0.25, 0.25)));
            Assert.Equal(-1, ExactPredicates.InSphere(a, b, c, d, new Vector3d(2, 2, 2)));
            Assert.Equal(0, ExactPredicates.InSphere(a, b, c, d, new Vector3d(1, 1, 1)));     // cube corner: cospherical
            // reversed orientation reverses the sign
            Assert.Equal(-1, ExactPredicates.InSphere(b, a, c, d, new Vector3d(0.25, 0.25, 0.25)));
        }

        [Fact]
        public void InSphere_MatchesExactIntegerArithmetic_NearDegenerate()
        {
            // nearly cospherical integer points far from the origin: the filter fails, the exact path decides
            var rnd = new Random(5);
            long off = 1L << 36;
            int checkedNear = 0;
            for (int it = 0; it < 4000; ++it) {
                // points on a sphere of integer radius around an integer center (Pythagorean quadruples), moved by -1..1
                long[][] sph = { new long[] { 1, 2, 2 }, new long[] { 2, 1, -2 }, new long[] { -2, 2, 1 }, new long[] { 2, -2, 1 }, new long[] { -1, -2, 2 } };
                long scale = rnd.Next(1, 1 << 20);
                long[] center = { off + rnd.Next(1000), off + rnd.Next(1000), off + rnd.Next(1000) };
                long[][] p = new long[5][];
                for (int i = 0; i < 5; ++i)
                    p[i] = new long[] { center[0] + scale * sph[i][0] + rnd.Next(-1, 2), center[1] + scale * sph[i][1], center[2] + scale * sph[i][2] + rnd.Next(-1, 2) };
                Vector3d[] v = p.Select(q => new Vector3d(q[0], q[1], q[2])).ToArray();
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) == 0) continue;
                int expect = InSphereInt(p);
                Assert.Equal(expect, ExactPredicates.InSphere(v[0], v[1], v[2], v[3], v[4]));
                Assert.Equal(expect, ExactPredicates.InSphereExact(v[0], v[1], v[2], v[3], v[4]));
                checkedNear++;
            }
            Assert.True(checkedNear > 3000);
            // random (well separated) points: filter path
            for (int it = 0; it < 4000; ++it) {
                long[][] p = Enumerable.Range(0, 5).Select(_ => new long[] { rnd.Next(-1000, 1000), rnd.Next(-1000, 1000), rnd.Next(-1000, 1000) }).ToArray();
                Vector3d[] v = p.Select(q => new Vector3d(q[0], q[1], q[2])).ToArray();
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) == 0) continue;
                Assert.Equal(InSphereInt(p), ExactPredicates.InSphere(v[0], v[1], v[2], v[3], v[4]));
            }
        }


        // ---- Delaunay3 ----

        [Fact]
        public void Delaunay3_RandomPoints_ValidAndDelaunay()
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1, -2, -3), new Vector3d(4, 3, 2));
            var dt = new Delaunay3(box);
            Assert.Null(dt.Validate(true));
            var rnd = new Random(2);
            for (int i = 0; i < 400; ++i) {
                Vector3d p = new Vector3d(-1 + 5 * rnd.NextDouble(), -2 + 5 * rnd.NextDouble(), -3 + 5 * rnd.NextDouble());
                Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(p, -1, 0, out int v));
                Assert.Equal(8 + i, v);
            }
            Assert.Null(dt.Validate(true));
            Assert.Equal(box.Volume, dt.CellIndices().Sum(c => dt.CellVolume(c)), 9);
            Assert.True(dt.CellIndices().All(c => dt.CellVolume(c) > 0));
        }

        [Fact]
        public void Delaunay3_GridPoints_CosphericalDegeneracies()
        {
            // a regular grid: every cube of the grid has 8 cospherical points, many coplanar facets
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(5, 5, 5)));
            var rnd = new Random(3);
            var pts = new List<Vector3d>();
            for (int i = 1; i < 5; ++i)
                for (int j = 1; j < 5; ++j)
                    for (int k = 1; k < 5; ++k) pts.Add(new Vector3d(i, j, k));
            pts = pts.OrderBy(_ => rnd.Next()).ToList();
            foreach (Vector3d p in pts)
                Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(p, -1, 0, out int v));
            Assert.Null(dt.Validate(true));
            Assert.Equal(125.0, dt.CellIndices().Sum(c => dt.CellVolume(c)), 9);
        }

        [Fact]
        public void Delaunay3_RejectsDuplicatesCloseAndOutsidePoints()
        {
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(new Vector3d(0.5, 0.5, 0.5), -1, 0, out int v0));
            int cells = dt.CellCount;
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(new Vector3d(0.5, 0.5, 0.5), -1, 0, out int v1));
            Assert.Equal(-1, v1);
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(new Vector3d(0.5, 0.5, 0.51), -1, 0.1, out v1));
            Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(new Vector3d(0.5, 0.5, 0.51), -1, 0.001, out v1));
            Assert.Equal(Delaunay3.InsertResult.Outside, dt.Insert(new Vector3d(1, 0.5, 0.5), -1, 0, out v1));     // on the box
            Assert.Equal(Delaunay3.InsertResult.Outside, dt.Insert(new Vector3d(2, 0.5, 0.5), -1, 0, out v1));
            Assert.Equal(Delaunay3.InsertResult.TooClose, dt.Insert(Vector3d.Zero + 1e-300 * Vector3d.One, -1, 0.01, out v1));
            Assert.Null(dt.Validate(true));
            Assert.Equal(10, dt.VertexCount);
            Assert.True(dt.CellCount > cells);
        }

        [Fact]
        public void Delaunay3_InsertReportsRemovedAndAddedCells()
        {
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One));
            var removed = new List<int>(); var added = new List<int>();
            dt.Insert(new Vector3d(0.3, 0.4, 0.5), -1, 0, out int v, removed, added);
            Assert.Equal(6, removed.Count);        // all six initial cells are in conflict (cospherical corners)
            Assert.Equal(12, added.Count);         // one cell per box triangle
            Assert.True(added.All(c => dt.IsCell(c) && dt.VertexIndexIn(c, v) >= 0));
        }


        // ---- AlphaWrapOracle ----

        [Fact]
        public void Oracle_FirstEntry_MatchesSampling()
        {
            var tris = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                // a degenerate (collinear) triangle and a point
                new Vector3d(2, 0, 0), new Vector3d(2.5, 0, 0), new Vector3d(3, 0, 0) };
            var o = new AlphaWrapOracle(tris, new List<Vector3d> { new Vector3d(0, 3, 0) });
            Assert.Equal(1, o.Triangles); Assert.Equal(1, o.Segments); Assert.Equal(1, o.Points);
            Assert.Equal(0.45, o.FirstEntry(new Vector3d(0.2, 0.2, 1), new Vector3d(0.2, 0.2, -1), 0.1), 12);
            var rnd = new Random(4);
            for (int k = 0; k < 3000; ++k) {
                Vector3d a = new Vector3d(4 * rnd.NextDouble() - 1, 5 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1);
                Vector3d b = new Vector3d(4 * rnd.NextDouble() - 1, 5 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1);
                double r = 0.05 + 0.3 * rnd.NextDouble();
                double t = o.FirstEntry(a, b, r);
                double ts = -1;
                for (int s = 0; s <= 2000; ++s)
                    if (o.Distance(a + (s / 2000.0) * (b - a)) <= r) { ts = s / 2000.0; break; }
                if (ts < 0) {
                    // sampling can miss a grazing entry: then the entry point is (nearly) on the surface
                    if (t >= 0) Assert.True(o.Distance(a + t * (b - a)) <= r * (1 + 1e-9));
                    continue;
                }
                Assert.True(t >= 0 && t <= ts + 1e-12, $"entry {t}, sampled {ts}");
                Assert.True(ts - t <= 1.0 / 2000 + 1e-9 || o.Distance(a + t * (b - a)) <= r * (1 + 1e-9));
                Assert.InRange(o.Distance(a + t * (b - a)), 0, r * (1 + 1e-9));
            }
        }

        [Fact]
        public void Oracle_TetrahedronTouches_IsConservative()
        {
            var tris = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                new Vector3d(0, 0, 1), new Vector3d(0, 0, 1), new Vector3d(1, 1, 1) };     // second one degenerate
            var o = new AlphaWrapOracle(tris, new List<Vector3d> { new Vector3d(1, 1, 0.5) });
            var rnd = new Random(6);
            int touching = 0, apart = 0;
            for (int k = 0; k < 3000; ++k) {
                Vector3d c = new Vector3d(1.5 * rnd.NextDouble() - 0.25, 1.5 * rnd.NextDouble() - 0.25, 1.5 * rnd.NextDouble() - 0.25);
                Vector3d[] v = Enumerable.Range(0, 4).Select(_ => c + 0.5 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)).ToArray();
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) < 0) { var t = v[0]; v[0] = v[1]; v[1] = t; }
                double margin = 0.01;
                bool touches = o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin);
                // sampled distance of the tetrahedron (barycentric grid)
                double dmin = double.MaxValue;
                int n = 12;
                for (int i = 0; i <= n; ++i)
                    for (int j = 0; i + j <= n; ++j)
                        for (int l = 0; i + j + l <= n; ++l) {
                            Vector3d p = (i * v[0] + j * v[1] + l * v[2] + (n - i - j - l) * v[3]) / n;
                            dmin = Math.Min(dmin, o.Distance(p));
                        }
                if (!touches) {
                    Assert.True(dmin > margin, $"reported apart, sampled distance {dmin}");
                    apart++;
                } else touching++;
                if (dmin == 0) Assert.True(touches);
            }
            Assert.True(touching > 100 && apart > 100, $"{touching} touching, {apart} apart");
        }


        [Fact]
        public void Oracle_ManyPrimitives_PruningIsConservative()
        {
            // a sphere as a soup with random orientations (the node slabs average the triangle normals) plus a
            // flat patch: entries and contacts against brute force over all primitives
            var rnd = new Random(9);
            DMesh3 s = Sphere(1, 10);
            var tris = new List<Vector3d>();
            foreach (int tid in s.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                s.GetTriVertices(tid, ref a, ref b, ref c);
                if (rnd.Next(2) == 0) { tris.Add(a); tris.Add(b); tris.Add(c); } else { tris.Add(a); tris.Add(c); tris.Add(b); }
            }
            for (int i = 0; i < 10; ++i)
                for (int j = 0; j < 10; ++j) {
                    Vector3d p = new Vector3d(1.5 + 0.1 * i, 0.1 * j, 0);
                    tris.Add(p); tris.Add(p + 0.1 * Vector3d.AxisX); tris.Add(p + 0.1 * Vector3d.AxisY);
                }
            var o = new AlphaWrapOracle(tris, new List<Vector3d>());
            var single = Enumerable.Range(0, tris.Count / 3).Select(k => new AlphaWrapOracle(tris.GetRange(3 * k, 3), new List<Vector3d>())).ToList();
            for (int k = 0; k < 400; ++k) {
                Vector3d a = new Vector3d(4 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5);
                Vector3d b = a + 0.8 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                double r = 0.01 + 0.05 * rnd.NextDouble();
                double t = o.FirstEntry(a, b, r);
                double tb = -1;
                foreach (var so in single) {
                    double ts = so.FirstEntry(a, b, r);
                    if (ts >= 0 && (tb < 0 || ts < tb)) tb = ts;
                }
                Assert.Equal(tb, t);
                Vector3d[] v = { a, b, a + 0.3 * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), 0), a + 0.3 * new Vector3d(0, rnd.NextDouble(), rnd.NextDouble()) };
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) == 0) continue;
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) < 0) { var x = v[0]; v[0] = v[1]; v[1] = x; }
                bool touches = o.TetrahedronTouches(v[0], v[1], v[2], v[3], r);
                bool touchesBrute = single.Any(so => so.TetrahedronTouches(v[0], v[1], v[2], v[3], r));
                Assert.Equal(touchesBrute, touches);
            }
        }


        // ---- MeshAlphaWrap ----

        static DMesh3 Tris(params Vector3d[] v)
        {
            DMesh3 m = new DMesh3();
            for (int i = 0; i < v.Length; ++i) m.AppendVertex(v[i]);
            for (int i = 0; i + 2 < v.Length; i += 3) m.AppendTriangle(i, i + 1, i + 2);
            return m;
        }

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(double r, int n)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        }

        static double SignedVolume(DMesh3 m)
        {
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
        }

        /// <summary>
        /// The guarantees: closed, 2-manifold, consistently oriented outwards, no self-intersections; all
        /// vertices on the offset surface; every point of the result farther than 0 and at most offset + 2 alpha
        /// from the input (offset + alpha, except where the manifold step added cells); the input inside (winding number 1 at input points).
        /// </summary>
        static void CheckWrap(MeshAlphaWrap w, IList<Vector3d> inputSamples, bool expectSuccess = true)
        {
            if (expectSuccess)
                Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            DMesh3 r = w.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount >= 4);
            Assert.True(r.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(r));
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            foreach (int eid in r.EdgeIndices()) {
                Index2i et = r.GetEdgeT(eid), ev = r.GetEdgeV(eid);
                Assert.NotEqual(edge_dir(r.GetTriangle(et.a), ev.a, ev.b), edge_dir(r.GetTriangle(et.b), ev.a, ev.b));
            }
            Assert.True(SignedVolume(r) > 0);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));

            // distances to the input, measured with an independent brute-force reference
            var oracle = Oracle(w);
            double off = w.UsedOffset, alpha = w.UsedAlpha;
            foreach (int vid in r.VertexIndices())
                Assert.Equal(off, oracle(r.GetVertex(vid)), off * 1e-6);
            var rnd = new Random(7);
            foreach (int tid in r.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                r.GetTriVertices(tid, ref a, ref b, ref c);
                for (int s = 0; s < 3; ++s) {
                    double u = rnd.NextDouble(), v = rnd.NextDouble();
                    if (u + v > 1) { u = 1 - u; v = 1 - v; }
                    double d = oracle(a + u * (b - a) + v * (c - a));
                    Assert.True(d > 0 && d <= off + 2 * alpha, $"result point at distance {d}, offset {off}, alpha {alpha}");
                }
            }
            // the input is enclosed
            var spatial = new DMeshAABBTree3(r, true);
            foreach (Vector3d p in inputSamples)
                Assert.True(spatial.WindingNumber(p) > 0.99, "input point outside of the wrap");
        }

        static bool edge_dir(Index3i t, int a, int b)
        {
            for (int j = 0; j < 3; ++j)
                if (t[j] == a) return t[(j + 1) % 3] == b;
            return false;
        }

        // distance to the input (triangles via g3's DistPoint3Triangle3 for non-degenerate ones, segments, points)
        static Func<Vector3d, double> Oracle(MeshAlphaWrap w)
        {
            var tris = new List<Triangle3d>(); var pts = new List<Vector3d>(w.Points);
            var segs = new List<Segment3d>();
            if (w.Mesh != null) {
                foreach (int tid in w.Mesh.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    w.Mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    if ((b - a).Cross(c - a).Length > 1e-9 * Math.Max(1e-300, Math.Max((b - a).LengthSquared, (c - a).LengthSquared)))
                        tris.Add(new Triangle3d(a, b, c));
                    else {
                        segs.Add(new Segment3d(a, b)); segs.Add(new Segment3d(b, c)); segs.Add(new Segment3d(c, a));
                    }
                }
                foreach (int vid in w.Mesh.VertexIndices())
                    if (w.Mesh.GetVtxEdgeCount(vid) == 0) pts.Add(w.Mesh.GetVertex(vid));
            }
            return (p) => {
                double d = double.MaxValue;
                foreach (var t in tris) d = Math.Min(d, Math.Sqrt(new DistPoint3Triangle3(p, t).GetSquared()));
                foreach (var s in segs) d = Math.Min(d, Math.Sqrt(s.DistanceSquared(p)));
                foreach (var q in pts) d = Math.Min(d, p.Distance(q));
                return d;
            };
        }

        static List<Vector3d> Vertices(DMesh3 m) { return m.VertexIndices().Select(v => m.GetVertex(v)).ToList(); }

        [Fact]
        public void Wrap_ClosedSphere()
        {
            DMesh3 s = Sphere(1, 6);
            var w = new MeshAlphaWrap(s);
            Assert.True(w.Compute());
            CheckWrap(w, Vertices(s));
            Assert.Equal(s.CachedBounds.DiagonalLength / 20, w.UsedAlpha, 12);
            Assert.Equal(s.CachedBounds.DiagonalLength / 600, w.UsedOffset, 12);
            // tight: the wrap of a convex input is close to the offset surface (volume of the offset sphere)
            double vol = SignedVolume(w.Result), offsetVol = 4.0 / 3 * Math.PI * Math.Pow(1 + w.UsedOffset, 3);
            Assert.InRange(vol, 0.9 * offsetVol, 1.001 * offsetVol);
        }

        [Fact]
        public void Wrap_BoxWithSharpEdges_SmallAlpha()
        {
            DMesh3 b = Box(Vector3d.Zero, new Vector3d(1, 0.5, 0.25));
            var w = new MeshAlphaWrap(b) { RelativeAlpha = 40, RelativeOffset = 200 };
            Assert.True(w.Compute());
            CheckWrap(w, Vertices(b));
        }

        [Fact]
        public void Wrap_OpenMesh_SingleTriangle()
        {
            // an open sheet without volume: the wrap is a thin closed shell around it
            DMesh3 t = Tris(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var w = new MeshAlphaWrap(t) { Alpha = 0.2, Offset = 0.02 };
            Assert.True(w.Compute());
            CheckWrap(w, new[] { new Vector3d(0.2, 0.2, 0), new Vector3d(0.01, 0.01, 0), new Vector3d(0.98, 0.01, 0) });
            Assert.Null(w.Triangulation.Validate(true));      // Delaunay also after all insertions
            Assert.InRange(SignedVolume(w.Result), 0.5 * 2 * 0.02 * 0.9, 0.5 * 2 * 0.02 * 2);
        }

        [Fact]
        public void Wrap_OpenSphereWithHole()
        {
            // alpha smaller than the hole: the inside is wrapped too (two layers); larger: the hole is closed
            DMesh3 s = Sphere(1, 8);
            foreach (int tid in s.TriangleIndices().Where(t => s.GetTriCentroid(t).z > 0.6).ToList())
                s.RemoveTriangle(tid);
            var small = new MeshAlphaWrap(s) { Alpha = 0.1, Offset = 0.02 };
            Assert.True(small.Compute());
            CheckWrap(small, Vertices(s));
            var large = new MeshAlphaWrap(s) { Alpha = 1.5, Offset = 0.02 };
            Assert.True(large.Compute());
            CheckWrap(large, Vertices(s));
            double sphereVol = 4.0 / 3 * Math.PI;
            Assert.True(SignedVolume(small.Result) < 0.3 * sphereVol);
            Assert.True(SignedVolume(large.Result) > 0.8 * sphereVol);
            Assert.True(new DMeshAABBTree3(large.Result, true).WindingNumber(Vector3d.Zero) > 0.99);
            Assert.True(new DMeshAABBTree3(small.Result, true).WindingNumber(Vector3d.Zero) < 0.01);
        }

        [Fact]
        public void Wrap_TriangleSoup_DuplicatesDegeneratesAndNonManifold()
        {
            // two overlapping boxes, one duplicated, unwelded (soup), plus degenerate triangles and a fin of three
            // triangles on one edge (non-manifold)
            DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(m, Box(0.5 * Vector3d.One, 1.5 * Vector3d.One));
            MeshEditor.Append(m, Box(0.5 * Vector3d.One, 1.5 * Vector3d.One));
            DMesh3 soup = new DMesh3();
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            }
            Action<Vector3d, Vector3d, Vector3d> add = (a, b, c) => soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            add(new Vector3d(2, 0, 0), new Vector3d(2.5, 0, 0), new Vector3d(3, 0, 0));     // collinear
            add(new Vector3d(2, 1, 0), new Vector3d(2, 1, 0), new Vector3d(2, 1, 0));       // a point
            add(new Vector3d(2, 2, 0), new Vector3d(2, 2, 1), new Vector3d(3, 2, 0));       // three triangles on edge (2,2,0)-(2,2,1)
            add(new Vector3d(2, 2, 0), new Vector3d(2, 2, 1), new Vector3d(1.5, 2.5, 0));
            add(new Vector3d(2, 2, 0), new Vector3d(2, 2, 1), new Vector3d(2.2, 1.4, 0));
            var w = new MeshAlphaWrap(soup) { RelativeAlpha = 30 };
            Assert.True(w.Compute());
            Assert.Equal(2, w.Counts["input_segments"] + w.Counts["input_points"]);
            Assert.Null(w.Triangulation.Validate(false));
            var samples = Vertices(soup);
            CheckWrap(w, samples);
            // the union of the boxes is inside, also where they overlap
            var spatial = new DMeshAABBTree3(w.Result, true);
            Assert.True(spatial.WindingNumber(new Vector3d(0.75, 0.75, 0.75)) > 0.99);
        }

        [Fact]
        public void Wrap_PointCloud()
        {
            var rnd = new Random(8);
            var pts = new List<Vector3d>();
            for (int i = 0; i < 1500; ++i) {
                Vector3d d = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                if (d.Length > 1e-3) pts.Add(d.Normalized);
            }
            var w = new MeshAlphaWrap(pts) { RelativeAlpha = 10, RelativeOffset = 300 };
            Assert.True(w.Compute());
            CheckWrap(w, pts);
            Assert.Equal(pts.Count, (int)w.Counts["input_points"]);
            // alpha above the point spacing: one closed surface around the sphere, which contains its center
            Assert.True(new DMeshAABBTree3(w.Result, true).WindingNumber(Vector3d.Zero) > 0.99);
            var comps = new MeshConnectedComponents(w.Result);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
        }

        [Fact]
        public void Wrap_PointCloudAsMeshVertices_AndSinglePoint()
        {
            // vertices without triangles are input points
            DMesh3 m = new DMesh3();
            m.AppendVertex(new Vector3d(1, 2, 3));
            var w = new MeshAlphaWrap(m) { Alpha = 0.5, Offset = 0.1 };
            Assert.True(w.Compute());
            CheckWrap(w, new[] { new Vector3d(1, 2, 3) });
            // relative parameters need an input of nonzero size
            var rel = new MeshAlphaWrap(m);
            Assert.False(rel.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, rel.Status);
            Assert.Null(rel.Result);
        }

        [Fact]
        public void Wrap_CoplanarAndCollinearInputs()
        {
            // points in a plane (no volume) and a set of degenerate triangles along a line
            var pts = new List<Vector3d>();
            for (int i = 0; i < 10; ++i)
                for (int j = 0; j < 10; ++j) pts.Add(new Vector3d(0.1 * i, 0.1 * j, 0));
            var w = new MeshAlphaWrap(pts) { Alpha = 0.15, Offset = 0.02 };
            Assert.True(w.Compute());
            CheckWrap(w, pts);
            DMesh3 line = Tris(new Vector3d(0, 0, 0), new Vector3d(0.5, 0, 0), new Vector3d(1, 0, 0),
                new Vector3d(1, 0, 0), new Vector3d(1, 0, 0), new Vector3d(2, 0, 0));
            var wl = new MeshAlphaWrap(line) { Alpha = 0.2, Offset = 0.05 };
            Assert.True(wl.Compute());
            CheckWrap(wl, new[] { new Vector3d(0.3, 0, 0), new Vector3d(1.7, 0, 0) });
            Assert.Equal(2, wl.Counts["input_segments"]);
        }

        [Fact]
        public void Wrap_FarFromOrigin()
        {
            DMesh3 s = Sphere(10, 5);
            MeshTransforms.Translate(s, new Vector3d(500000, 5000000, 100));
            var w = new MeshAlphaWrap(s) { RelativeAlpha = 10 };
            Assert.True(w.Compute());
            CheckWrap(w, Vertices(s));
        }

        [Fact]
        public void Wrap_ThinGapNarrowerThanAlphaIsClosed()
        {
            // two boxes with a gap of 0.1: alpha 0.2 bridges it, alpha 0.02 enters it
            DMesh3 m = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            MeshEditor.Append(m, Box(new Vector3d(1.1, 0, 0), new Vector3d(2.1, 1, 1)));
            Vector3d gap = new Vector3d(1.05, 0.5, 0.5);
            var coarse = new MeshAlphaWrap(m) { Alpha = 0.3, Offset = 0.01 };
            Assert.True(coarse.Compute());
            CheckWrap(coarse, Vertices(m));
            Assert.True(new DMeshAABBTree3(coarse.Result, true).WindingNumber(gap) > 0.99);
            var fine = new MeshAlphaWrap(m) { Alpha = 0.03, Offset = 0.01 };
            Assert.True(fine.Compute());
            Assert.True(new DMeshAABBTree3(fine.Result, true).WindingNumber(gap) < 0.01);
            var comps = new MeshConnectedComponents(fine.Result);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
        }

        [Fact]
        public void Wrap_InvalidInputs()
        {
            var empty = new MeshAlphaWrap(new DMesh3());
            Assert.False(empty.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.EmptyInput, empty.Status);
            DMesh3 nan = Tris(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(double.NaN, 1, 0));
            var wn = new MeshAlphaWrap(nan);
            Assert.False(wn.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, wn.Status);
            Assert.NotNull(wn.Error);
            var neg = new MeshAlphaWrap(Sphere(1, 4)) { RelativeAlpha = -1 };
            Assert.False(neg.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, neg.Status);
        }

        [Fact]
        public void Wrap_PointLimitAndCancel()
        {
            DMesh3 s = Sphere(1, 6);
            var w = new MeshAlphaWrap(s) { MaxSteinerPoints = 30 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
            // still a valid wrap, only coarser (vertices can be box corners, so no distance checks)
            Assert.True(w.Result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(w.Result));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(w.Result));
            var spatial = new DMeshAABBTree3(w.Result, true);
            Assert.True(Vertices(s).All(p => spatial.WindingNumber(p) > 0.99));

            var c = new MeshAlphaWrap(s) { RelativeAlpha = 100, CancelF = () => true };
            Assert.False(c.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Cancelled, c.Status);
            Assert.Null(c.Result);
        }

        [Fact]
        public void Wrap_IsDeterministic()
        {
            DMesh3 s = Sphere(1, 5);
            MeshEditor.Append(s, Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1.5, 1.2, 1.1)));
            var a = new MeshAlphaWrap(s) { RelativeAlpha = 25 };
            var b = new MeshAlphaWrap(s) { RelativeAlpha = 25 };
            a.Compute(); b.Compute();
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            Assert.True(a.Result.VertexIndices().All(v => a.Result.GetVertex(v) == b.Result.GetVertex(v)));
        }

        [Theory]
        [InlineData(34)]
        [InlineData(44)]
        [InlineData(71)]
        [InlineData(92)]
        public void Wrap_RandomSoup_ManifoldStepKeepsBoxCornersOff(int seed)
        {
            // random overlapping triangles: non-manifold spots where the manifold step must move cells inside.
            // Moving all outside cells around a vertex inside put big cells reaching the box corners on the
            // result (vertices hundreds of offsets away from the input).
            var rg = new Random(seed);
            var m = new DMesh3();
            int nt = 3 + rg.Next(30);
            Func<Vector3d> jitter = () => new Vector3d(rg.NextDouble() - 0.5, rg.NextDouble() - 0.5, rg.NextDouble() - 0.5);
            for (int t = 0; t < nt; ++t) {
                Vector3d c = new Vector3d(rg.NextDouble(), rg.NextDouble(), rg.NextDouble());
                double sz = 0.05 + 0.4 * rg.NextDouble();
                int a = m.AppendVertex(c + sz * jitter()), b = m.AppendVertex(c + sz * jitter()), d = m.AppendVertex(c + sz * jitter());
                m.AppendTriangle(a, b, d);
            }
            var w = new MeshAlphaWrap(m) { RelativeAlpha = 10 + rg.Next(60), RelativeOffset = 100 + rg.Next(1000) };
            Assert.True(w.Compute());
            Assert.Equal(0, w.Counts["result_box_corners"]);
            CheckWrap(w, Vertices(m));
        }

        [Fact]
        public void Wrap_NearlyTouchingBoxes()
        {
            // a gap at the order of alpha along an edge: where the wrap pinches, the manifold step fills it
            DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(m, Box(new Vector3d(1.02, 1.02, 0), new Vector3d(2, 2, 1)));     // boxes touching nearly at an edge
            var w = new MeshAlphaWrap(m) { Alpha = 0.05, Offset = 0.005 };
            Assert.True(w.Compute());
            CheckWrap(w, Vertices(m));
        }
    }
}
