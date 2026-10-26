using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Intrinsic Delaunay triangulations (signposts), the heat method on them and the vector heat method
    /// (parallel transport, scalar extension, log map).
    /// </summary>
    public class IntrinsicGeodesicsTests
    {
        readonly ITestOutputHelper output;
        public IntrinsicGeodesicsTests(ITestOutputHelper output) { this.output = output; }

        // ---------------------------------------------------------------- meshes

        /// <summary>
        /// Plane with rows of vertices that are close together (dy) and shifted by half a column against
        /// each other: every triangle has an angle of about 2 atan(h / 2dy), 169 degrees for dy = h / 20.
        /// </summary>
        internal static DMesh3 ObtusePlane(int nx, int ny, double h, double dy)
        {
            DMesh3 mesh = new DMesh3();
            var rows = new List<int[]>();
            for (int j = 0; j <= ny; ++j) {
                bool odd = (j % 2) == 1;
                int count = odd ? nx : nx + 1;
                int[] row = new int[count];
                for (int i = 0; i < count; ++i)
                    row[i] = mesh.AppendVertex(new Vector3d(i * h + (odd ? 0.5 * h : 0), j * dy, 0));
                rows.Add(row);
            }
            for (int j = 0; j < ny; ++j) {
                int[] A = rows[j], B = rows[j + 1];
                if (j % 2 == 0) {       // A has nx+1, B (shifted) nx vertices
                    for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i]);
                    for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i + 1], B[i + 1], B[i]);
                } else {                // A (shifted) nx, B nx+1
                    for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], B[i + 1], B[i]);
                    for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i + 1]);
                }
            }
            return mesh;
        }

        /// <summary>
        /// Sphere from latitude rings, alternate rings shifted by half a step: with many rings and few
        /// vertices per ring nearly all triangles are very obtuse.
        /// </summary>
        internal static DMesh3 RingSphere(double R, int rings, int perRing)
        {
            DMesh3 mesh = new DMesh3();
            int north = mesh.AppendVertex(new Vector3d(0, 0, R));
            var ring = new List<int[]>();
            for (int k = 1; k < rings; ++k) {
                double th = Math.PI * k / rings;
                int[] r = new int[perRing];
                for (int i = 0; i < perRing; ++i) {
                    double ph = 2 * Math.PI * (i + ((k % 2 == 1) ? 0.5 : 0)) / perRing;
                    r[i] = mesh.AppendVertex(new Vector3d(R * Math.Sin(th) * Math.Cos(ph), R * Math.Sin(th) * Math.Sin(ph), R * Math.Cos(th)));
                }
                ring.Add(r);
            }
            int south = mesh.AppendVertex(new Vector3d(0, 0, -R));
            Action<int, int, int> add = (a, b, c) => {
                Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b), pc = mesh.GetVertex(c);
                if ((pb - pa).Cross(pc - pa).Dot(pa + pb + pc) < 0) { int t = b; b = c; c = t; }
                mesh.AppendTriangle(a, b, c);
            };
            for (int i = 0; i < perRing; ++i) {
                add(north, ring[0][i], ring[0][(i + 1) % perRing]);
                add(south, ring[rings - 2][i], ring[rings - 2][(i + 1) % perRing]);
            }
            for (int k = 0; k + 1 < ring.Count; ++k) {
                int[] A = ring[k], B = ring[k + 1];
                // ring k+1 (index k+1 -> latitude k+2) is shifted against ring k by half a step
                bool bShiftedForward = ((k + 2) % 2 == 1);
                for (int i = 0; i < perRing; ++i) {
                    int i1 = (i + 1) % perRing;
                    if (bShiftedForward) {
                        // B[i] lies between A[i] and A[i+1]
                        add(A[i], A[i1], B[i]);
                        add(A[i1], B[i1], B[i]);
                    } else {
                        // A[i] lies between B[i] and B[i+1]
                        add(B[i], B[i1], A[i]);
                        add(B[i1], A[i1], A[i]);
                    }
                }
            }
            return mesh;
        }

        internal static DMesh3 GoodSphere(double R, int n)
        {
            var gen = new Sphere3Generator_NormalizedCube() { Radius = R, EdgeVertices = n };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, -mesh.CachedBounds.Center * 0);
            return mesh;
        }

        internal static DMesh3 Grid(double size, int n, Func<double, double, double> height)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = -size / 2 + size * i / (n - 1), y = -size / 2 + size * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, height(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    if ((i + j) % 2 == 0) {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    } else {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]);
                        mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    }
                }
            return mesh;
        }

        internal static int NearestVertex(DMesh3 m, Vector3d p) { return m.VertexIndices().OrderBy(v => m.GetVertex(v).DistanceSquared(p)).First(); }

        internal static double SphereDistance(Vector3d a, Vector3d b, double R)
        {
            double c = MathUtil.Clamp(a.Dot(b) / (a.Length * b.Length), -1, 1);
            return R * Math.Acos(c);
        }

        static Vector3d Rotate(Vector3d v, Vector3d axis, double a)
        {
            return v * Math.Cos(a) + axis.Cross(v) * Math.Sin(a) + axis * axis.Dot(v) * (1 - Math.Cos(a));
        }

        static double AngleBetween(Vector3d a, Vector3d b)
        {
            return Math.Acos(MathUtil.Clamp(a.Normalized.Dot(b.Normalized), -1, 1));
        }

        // mean and max absolute error of the distances of the vertices that pass the filter
        internal static void Errors(DMesh3 mesh, double[] dist, Func<int, double> exact, Func<int, bool> use, out double mean, out double max)
        {
            mean = 0; max = 0; int n = 0;
            foreach (int v in mesh.VertexIndices()) {
                if (use(v) == false) continue;
                double e = Math.Abs(dist[v] - exact(v));
                mean += e; max = Math.Max(max, e); n++;
            }
            mean /= n;
        }

        // ---------------------------------------------------------------- intrinsic triangulation

        [Fact]
        public void FlipToDelaunay_ObtusePlane_KeepsTheGeometry()
        {
            DMesh3 mesh = ObtusePlane(20, 200, 1.0, 0.05);
            var T = new IntrinsicTriangulation(mesh);
            Assert.Equal(0, T.MollificationDelta);
            Assert.False(T.IsDelaunay());
            double area0 = Enumerable.Range(0, T.TriangleCount).Sum(t => T.TriangleArea(t));
            double[] angleSum0 = Enumerable.Range(0, T.VertexCount).Select(i => T.AngleSum(i)).ToArray();

            int flips = T.FlipToDelaunay();
            Assert.True(flips > 1000, $"{flips} flips");
            Assert.True(T.IsDelaunay());
            // all interior cotan weights non-negative (boundary edges cannot be flipped)
            int negBefore = 0;
            var T0 = new IntrinsicTriangulation(mesh);
            for (int h = 0; h < T0.HalfedgeCount; ++h)
                if (T0.Twin(h) >= 0 && T0.CotanWeight(h) < 0) negBefore++;
            Assert.True(negBefore > 1000, $"{negBefore}");
            for (int h = 0; h < T.HalfedgeCount; ++h)
                if (T.Twin(h) >= 0) Assert.True(T.CotanWeight(h) > -1e-9, $"weight {T.CotanWeight(h)}");

            // same surface: area and angle sums unchanged; in the plane every intrinsic edge is the
            // straight segment between its vertices
            double area1 = Enumerable.Range(0, T.TriangleCount).Sum(t => T.TriangleArea(t));
            Assert.Equal(area0, area1, 9);
            double maxAngleSum = 0, maxLen = 0;
            for (int i = 0; i < T.VertexCount; ++i) {
                double s = 0; int h0 = T.VertexHalfedge(i), h = h0;
                do { s += T.CornerAngle(h); h = T.NextAroundVertex(h); } while (h >= 0 && h != h0);
                maxAngleSum = Math.Max(maxAngleSum, Math.Abs(s - angleSum0[i]));
            }
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                double d = mesh.GetVertex(T.VertexID(T.Tail(h))).Distance(mesh.GetVertex(T.VertexID(T.Head(h))));
                maxLen = Math.Max(maxLen, Math.Abs(d - T.Length(h)));
            }
            Assert.True(maxAngleSum < 1e-9, $"angle sums changed by {maxAngleSum}");
            Assert.True(maxLen < 1e-9, $"edge lengths off by {maxLen}");
        }

        [Fact]
        public void Signposts_ConsistentAfterFlips()
        {
            DMesh3 mesh = RingSphere(1, 60, 12);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.FlipToDelaunay() > 100);
            double maxErr = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                int n = T.NextAroundVertex(h);
                if (n < 0) continue;
                int i = T.Tail(h);
                double expected = IntrinsicTriangulation.wrap(T.Signpost(h) + T.AngleScale(i) * T.CornerAngle(h));
                double d = Math.Abs(IntrinsicTriangulation.wrap(T.Signpost(n) - expected + Math.PI) - Math.PI);
                maxErr = Math.Max(maxErr, d);
            }
            Assert.True(maxErr < 1e-9, $"signposts off by {maxErr}");
        }

        [Fact]
        public void Signposts_BoundaryVertexWithAngleSumAbove2Pi()
        {
            // a boundary vertex v with a ruffled fan of 9 triangles of 50 degrees (angle sum 450 degrees), and
            // a triangle beyond the last rim edge, whose flip creates an edge at v. Its signpost is past 2 pi;
            // wrapped to [0, 2 pi) it pointed into the first triangles of the fan and TraceEdge went astray.
            DMesh3 mesh = new DMesh3();
            int v = mesh.AppendVertex(Vector3d.Zero);
            int nT = 9;
            double gamma = 50 * Math.PI / 180, da = 36 * Math.PI / 180;
            // rim points at distance 1, alternately above and below the plane, consecutive ones gamma apart
            double phi0 = Math.Acos(Math.Sqrt((Math.Cos(gamma) + 1) / (Math.Cos(da) + 1)));
            var rim = new List<int>();
            for (int k = 0; k <= nT; ++k) {
                double a = k * da, ph = (k % 2 == 0 ? 1 : -1) * phi0;
                rim.Add(mesh.AppendVertex(new Vector3d(Math.Cos(a) * Math.Cos(ph), Math.Sin(a) * Math.Cos(ph), Math.Sin(ph))));
            }
            for (int k = 0; k < nT; ++k) Assert.True(mesh.AppendTriangle(v, rim[k], rim[k + 1]) >= 0);
            Vector3d mid = 0.5 * (mesh.GetVertex(rim[nT - 1]) + mesh.GetVertex(rim[nT]));
            Assert.True(mesh.AppendTriangle(rim[nT], rim[nT - 1], mesh.AppendVertex(mid + 0.3 * mid.Normalized)) >= 0);

            // v becomes the head of the flipped halfedge (k in FlipEdge) or the head of its twin (l)
            foreach (bool reverse in new[] { false, true }) {
                var T = new IntrinsicTriangulation(mesh);
                int iv = T.VertexIndex(v);
                Assert.True(T.IsBoundaryVertex(iv));
                Assert.Equal(nT * gamma, T.AngleSum(iv), 9);
                int h = -1;
                for (int k = 0; k < T.HalfedgeCount; ++k)
                    if (T.VertexID(T.Tail(k)) == rim[nT - 1] && T.VertexID(T.Head(k)) == rim[nT] && T.Twin(k) >= 0) h = k;
                if (reverse) h = T.Twin(h);     // v is then the other end of the flipped edge
                Assert.True(T.FlipEdge(h));
                int hv = T.VertexID(T.Tail(h)) == v ? h : T.Twin(h);
                Assert.Equal(v, T.VertexID(T.Tail(hv)));

                // around v counter-clockwise from its first boundary edge the signposts increase by the corner angles
                int hh = T.VertexHalfedge(iv), count = 0;
                double expected = 0;
                while (hh >= 0) {
                    Assert.Equal(expected, T.Signpost(hh), 9);
                    expected += T.CornerAngle(hh);
                    hh = T.NextAroundVertex(hh);
                    count++;
                }
                Assert.Equal(nT + 1, count);
                Assert.True(T.Signpost(hv) > 2 * Math.PI);
                List<Vector3d> path = T.TraceEdge(hv);
                Assert.True(path[path.Count - 1].Distance(mesh.GetVertex(T.VertexID(T.Head(hv)))) < 1e-9);
            }
        }

        [Fact]
        public void TraceEdge_FollowsTheSurfaceToItsHead()
        {
            // intrinsic edges of very obtuse meshes (closed, and with a boundary), traced over the input
            // mesh from their signposts
            foreach (DMesh3 mesh in new[] { RingSphere(1, 120, 16), ObtusePlane(10, 60, 1.0, 0.05) }) {
                bool sphere = mesh.IsClosed();
                var T = new IntrinsicTriangulation(mesh);
                T.FlipToDelaunay();
                double maxEnd = 0, maxLen = 0; int crossing = 0;
                for (int h = 0; h < T.HalfedgeCount; ++h) {
                    List<Vector3d> path = T.TraceEdge(h);
                    if (path.Count > 2) crossing++;
                    maxEnd = Math.Max(maxEnd, path[path.Count - 1].Distance(mesh.GetVertex(T.VertexID(T.Head(h)))));
                    double len = 0;
                    for (int k = 1; k < path.Count; ++k) len += path[k].Distance(path[k - 1]);
                    maxLen = Math.Max(maxLen, Math.Abs(len - T.Length(h)));
                    // on the surface: on the sphere's polyhedron (inside the unit ball, close to it), in the plane
                    foreach (Vector3d p in path)
                        Assert.True(sphere ? (p.Length <= 1 + 1e-12 && p.Length > 0.9) : Math.Abs(p.z) < 1e-12);
                }
                Assert.True(crossing > 300, $"{crossing} edges cross input edges");
                Assert.True(maxEnd < 1e-9, $"trace ends {maxEnd} from the head vertex");
                Assert.True(maxLen < 1e-9, $"trace length off by {maxLen}");
            }
        }

        [Fact]
        public void CotanLaplacian_LinearPrecisionAndNoNegativeWeights()
        {
            // flat mesh: the intrinsic Delaunay Laplacian of a linear function vanishes at interior vertices
            DMesh3 mesh = ObtusePlane(12, 120, 1.0, 0.05);
            var T = new IntrinsicTriangulation(mesh);
            T.FlipToDelaunay();
            SymmetricSparseMatrix L = T.CotanLaplacian();
            double[] f = new double[T.VertexCount], Lf = new double[T.VertexCount];
            for (int i = 0; i < f.Length; ++i) {
                Vector3d p = mesh.GetVertex(T.VertexID(i));
                f[i] = 2 * p.x - 3 * p.y + 1;
            }
            L.Multiply(f, Lf);
            for (int i = 0; i < f.Length; ++i)
                if (T.IsBoundaryVertex(i) == false)
                    Assert.True(Math.Abs(Lf[i]) < 1e-8, $"L f = {Lf[i]}");
            // off-diagonals: minus the weights, non-negative except at boundary edges opposite obtuse angles
            var boundaryPairs = new HashSet<Index2i>();
            for (int h = 0; h < T.HalfedgeCount; ++h)
                if (T.Twin(h) < 0) boundaryPairs.Add(new Index2i(Math.Min(T.Tail(h), T.Head(h)), Math.Max(T.Tail(h), T.Head(h))));
            foreach (var kv in L.NonZeros())
                if (kv.Key.a != kv.Key.b && boundaryPairs.Contains(kv.Key) == false)
                    Assert.True(kv.Value <= 1e-9, $"positive off-diagonal {kv.Value}");
        }

        [Fact]
        public void Mollification_DegenerateTriangles()
        {
            // zero-area triangles (a vertex on the opposite edge, and two coincident vertices)
            DMesh3 mesh = Grid(4, 5, (x, y) => 0);
            int v = NearestVertex(mesh, new Vector3d(0, 0, 0));
            Vector3d p = mesh.GetVertex(v);
            int nb = mesh.VtxVerticesItr(v).First();
            mesh.SetVertex(v, mesh.GetVertex(nb));
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.MollificationDelta > 0);
            for (int t = 0; t < T.TriangleCount; ++t) Assert.True(T.TriangleArea(t) > 0);
            T.FlipToDelaunay();
            Assert.True(T.IsDelaunay());
            var vhm = new VectorHeatMethod(mesh);
            var log = vhm.ComputeLogMap(NearestVertex(mesh, new Vector3d(-2, -2, 0)));
            foreach (int u in mesh.VertexIndices()) Assert.False(double.IsNaN(log[u].x));
        }

        // ---------------------------------------------------------------- heat method distance

        [Fact]
        public void HeatDistance_ObtusePlane_IntrinsicIsAccurate()
        {
            // 169 degree triangles: the cotan Laplacian of the mesh has large negative weights and the
            // heat method fails on it; on the intrinsic Delaunay triangulation it is accurate
            DMesh3 mesh = ObtusePlane(20, 400, 1.0, 0.05);
            int src = NearestVertex(mesh, new Vector3d(10, 10, 0));
            Vector3d ps = mesh.GetVertex(src);
            Func<int, double> exact = v => mesh.GetVertex(v).Distance(ps);
            Func<int, bool> inner = v => mesh.GetVertex(v).x >= 1 && mesh.GetVertex(v).x <= 19;

            var geo = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = true };
            Assert.True(geo.Compute(new[] { src }));
            double mean, max;
            Errors(mesh, geo.Distance, exact, inner, out mean, out max);
            output.WriteLine($"intrinsic: mean {mean} max {max}");
            Assert.True(mean < 0.1, $"intrinsic mean error {mean}");
            Assert.True(max < 1.0, $"intrinsic max error {max}");

            var ext = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = false };
            Assert.True(ext.Compute(new[] { src }));
            double emean, emax;
            Errors(mesh, ext.Distance, exact, inner, out emean, out emax);
            output.WriteLine($"extrinsic: mean {emean} max {emax}");
            Assert.True(emean > 10 * mean, $"extrinsic {emean} vs intrinsic {mean}");

            // intrinsic Delaunay refinement (vertex insertion) brings the error down further
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var refined = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
            Assert.True(refined.Compute(new[] { src }));
            double rmean, rmax;
            Errors(mesh, refined.Distance, exact, inner, out rmean, out rmax);
            output.WriteLine($"refined: mean {rmean} max {rmax} ({refined.Refinement}, {sw.ElapsedMilliseconds} ms)");
            Assert.Equal(IntrinsicRefinementStatus.Success, refined.Refinement.Status);
            Assert.True(rmean < 0.5 * mean, $"refined {rmean} vs intrinsic {mean}");
            Assert.True(rmax < 0.5 * max, $"refined max {rmax} vs intrinsic {max}");
        }

        [Fact]
        public void HeatDistance_ObtuseSphere_IntrinsicIsAccurate()
        {
            DMesh3 mesh = RingSphere(1, 160, 24);
            int src = NearestVertex(mesh, new Vector3d(1, 0, 0));
            Vector3d ps = mesh.GetVertex(src);
            Func<int, double> exact = v => SphereDistance(mesh.GetVertex(v), ps, 1);
            Func<int, bool> use = v => exact(v) < 2.8;

            var geo = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = true };
            Assert.True(geo.Compute(new[] { src }));
            double mean, max;
            Errors(mesh, geo.Distance, exact, use, out mean, out max);
            var ext = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = false };
            Assert.True(ext.Compute(new[] { src }));
            double emean, emax;
            Errors(mesh, ext.Distance, exact, use, out emean, out emax);
            output.WriteLine($"intrinsic: mean {mean} max {max}; extrinsic: mean {emean} max {emax}");
            Assert.True(mean < 0.05 && max < 0.1, $"intrinsic mean {mean} max {max}");
            Assert.True(emean > 4 * mean, $"extrinsic {emean} vs intrinsic {mean}");
        }

        [Fact]
        public void HeatDistance_GoodMeshes_IntrinsicSameAccuracy()
        {
            DMesh3 sphere = GoodSphere(1, 24);
            int src = NearestVertex(sphere, new Vector3d(0, 0, 1));
            Vector3d ps = sphere.GetVertex(src);
            var geo = new MeshGeodesicDistance(sphere) { IntrinsicDelaunay = true };
            Assert.True(geo.Compute(new[] { src }));
            double mean, max;
            Errors(sphere, geo.Distance, v => SphereDistance(sphere.GetVertex(v), ps, 1), v => true, out mean, out max);
            Assert.True(max < 0.05, $"max error {max}");
            Assert.True(geo.IntrinsicFlips >= 0);

            // the path tracing still works on the mesh
            var path = geo.TracePath(NearestVertex(sphere, new Vector3d(1, 0, 0)));
            Assert.Equal(src, NearestVertex(sphere, path[path.Count - 1]));
        }

        // ---------------------------------------------------------------- vector heat method

        [Fact]
        public void Transport_ObtusePlane_IsConstant()
        {
            // in the plane parallel transport is the identity; signposts after thousands of flips must
            // reproduce it exactly
            DMesh3 mesh = ObtusePlane(20, 200, 1.0, 0.05);
            var vhm = new VectorHeatMethod(mesh);
            int src = NearestVertex(mesh, new Vector3d(10, 5, 0));
            Vector3d dir = new Vector3d(0.6, 0.8, 0);
            Vector2d[] res = vhm.TransportVector(src, vhm.Triangulation.FromWorld(src, dir));
            double maxErr = 0;
            foreach (int v in mesh.VertexIndices())
                maxErr = Math.Max(maxErr, vhm.Triangulation.ToWorld(v, res[v]).Distance(dir));
            Assert.True(maxErr < 1e-6, $"transport error {maxErr}");
        }

        [Fact]
        public void Transport_Sphere_AlongGreatCircles()
        {
            DMesh3 mesh = GoodSphere(1, 24);
            int src = NearestVertex(mesh, new Vector3d(0, 0, 1));
            Vector3d pn = mesh.GetVertex(src).Normalized;
            var vhm = new VectorHeatMethod(mesh);
            Vector2d[] res = vhm.TransportVector(src, new Vector2d(1, 0));
            Vector3d e1 = vhm.Triangulation.ToWorld(src, new Vector2d(1, 0));
            double maxErr = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v).Normalized;
                double th = Math.Acos(MathUtil.Clamp(p.Dot(pn), -1, 1));
                if (th < 0.05 || th > 2.4) continue;      // the antipode is the cut locus
                Vector3d expected = Rotate(e1, pn.Cross(p).Normalized, th);
                Vector3d got = vhm.Triangulation.ToWorld(v, res[v]);
                Assert.Equal(1.0, res[v].Length, 9);
                maxErr = Math.Max(maxErr, AngleBetween(expected - expected.Dot(p) * p, got));
            }
            output.WriteLine($"max angle error {maxErr * 180 / Math.PI} deg");
            Assert.True(maxErr < 4 * Math.PI / 180, $"max angle error {maxErr * 180 / Math.PI} deg");
        }

        [Fact]
        public void ExtendScalar_NearestSourceValue()
        {
            DMesh3 mesh = Grid(20, 41, (x, y) => 0);
            int a = NearestVertex(mesh, new Vector3d(-5, 0, 0)), b = NearestVertex(mesh, new Vector3d(5, 3, 0));
            var vhm = new VectorHeatMethod(mesh);
            double[] f = vhm.ExtendScalar(new[] { new KeyValuePair<int, double>(a, 1), new KeyValuePair<int, double>(b, 2) });
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                double da = p.Distance(mesh.GetVertex(a)), db = p.Distance(mesh.GetVertex(b));
                if (Math.Abs(da - db) < 3) continue;
                Assert.True(Math.Abs(f[v] - (da < db ? 1 : 2)) < 0.05, $"{p}: {f[v]}");
            }
        }

        // ---------------------------------------------------------------- log map

        [Fact]
        public void LogMap_Plane_IsTheOffset()
        {
            DMesh3 mesh = Grid(20, 41, (x, y) => 0);
            int src = NearestVertex(mesh, new Vector3d(3, 2, 0));
            Vector3d ps = mesh.GetVertex(src);
            var vhm = new VectorHeatMethod(mesh);
            Vector2d[] log = vhm.ComputeLogMap(src);
            Assert.Equal(Vector2d.Zero, log[src]);
            double maxRel = 0;
            foreach (int v in mesh.VertexIndices()) {
                if (mesh.IsBoundaryVertex(v)) continue;
                Vector3d d = mesh.GetVertex(v) - ps;
                double err = vhm.Triangulation.ToWorld(src, log[v]).Distance(d);
                // a few percent of the distance plus half an edge length (heat method accuracy)
                Assert.True(err < 0.04 * d.Length + 0.25, $"{d}: {vhm.Triangulation.ToWorld(src, log[v])}");
                if (d.Length > 5) maxRel = Math.Max(maxRel, err / d.Length);
            }
            output.WriteLine($"max relative error {maxRel}");
        }

        [Fact]
        public void LogMap_ObtusePlane_Robust()
        {
            DMesh3 mesh = ObtusePlane(20, 400, 1.0, 0.05);
            int src = NearestVertex(mesh, new Vector3d(10, 10, 0));
            Vector3d ps = mesh.GetVertex(src);
            var vhm = new VectorHeatMethod(mesh);
            Vector2d[] log = vhm.ComputeLogMap(src);
            double sum = 0; int n = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                // the heat method bends the distance within a few edge lengths of the boundary corners
                if (p.x < 2 || p.x > 18 || p.y < 2 || p.y > 18) continue;
                Vector3d d = p - ps;
                double err = vhm.Triangulation.ToWorld(src, log[v]).Distance(d);
                Assert.True(err < 0.06 * d.Length + 0.6, $"{d}: {vhm.Triangulation.ToWorld(src, log[v])}");
                sum += err; n++;
            }
            output.WriteLine($"mean error {sum / n}");
            Assert.True(sum / n < 0.15);
        }

        [Fact]
        public void LogMap_Sphere_PolarCoordinates_BetterThanMeshLocalParam()
        {
            DMesh3 mesh = GoodSphere(1, 24);
            int src = NearestVertex(mesh, new Vector3d(0, 0, 1));
            Vector3d pn = mesh.GetVertex(src).Normalized;
            var vhm = new VectorHeatMethod(mesh);
            Vector2d[] log = vhm.ComputeLogMap(src);

            // the discrete exponential map of MeshLocalParam (Schmidt et al. 2006), for comparison
            var mlp = new MeshLocalParam(mesh.MaxVertexID, v => (Vector3f)mesh.GetVertex(v),
                v => (Vector3f)mesh.GetVertex(v).Normalized, v => mesh.VtxVerticesItr(v));
            Frame3f frame = new Frame3f((Vector3f)mesh.GetVertex(src), (Vector3f)pn);
            mlp.ComputeToMaxDistance(frame, mesh.GetTriangle(mesh.VtxTrianglesItr(src).First()), 10f);

            double maxDist = 0, maxAng = 0, mlpDist = 0, mlpAng = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v).Normalized;
                double th = Math.Acos(MathUtil.Clamp(p.Dot(pn), -1, 1));
                if (th > 2.4 || v == src) continue;
                Vector3d radial = p - p.Dot(pn) * pn;     // direction at the source towards p
                maxDist = Math.Max(maxDist, Math.Abs(log[v].Length - th));
                Vector2f uv = mlp.GetUV(v);
                mlpDist = Math.Max(mlpDist, Math.Abs(uv.Length - th));
                if (th < 0.2) continue;
                maxAng = Math.Max(maxAng, AngleBetween(vhm.Triangulation.ToWorld(src, log[v]), radial));
                mlpAng = Math.Max(mlpAng, AngleBetween((Vector3d)frame.X * uv.x + (Vector3d)frame.Y * uv.y, radial));
            }
            output.WriteLine($"log map: distance {maxDist}, angle {maxAng * 180 / Math.PI} deg; MeshLocalParam: {mlpDist}, {mlpAng * 180 / Math.PI} deg");
            Assert.True(maxDist < 0.05, $"distance error {maxDist}");
            Assert.True(maxAng < 8 * Math.PI / 180, $"angle error {maxAng * 180 / Math.PI} deg");
            Assert.True(maxDist < 0.5 * mlpDist && maxAng < mlpAng);
        }

        [Fact]
        public void LogMap_Bump_SymmetricAndTransportConsistent()
        {
            // log_y(x) is minus the transport of log_x(y) along the geodesic from x to y, and both have the
            // same length
            DMesh3 mesh = Grid(10, 51, (x, y) => 1.5 * Math.Exp(-(x * x + y * y) / 4));
            var vhm = new VectorHeatMethod(mesh);
            int[] pts = {
                NearestVertex(mesh, new Vector3d(-3, -2, 0)), NearestVertex(mesh, new Vector3d(3, -1, 0)),
                NearestVertex(mesh, new Vector3d(0.5, 3, 0)), NearestVertex(mesh, new Vector3d(-2, 2.5, 0))
            };
            var logs = pts.Select(p => vhm.ComputeLogMap(p)).ToArray();
            double maxLen = 0, maxAng = 0;
            for (int a = 0; a < pts.Length; ++a)
                for (int b = 0; b < pts.Length; ++b) {
                    if (a == b) continue;
                    Vector2d lab = logs[a][pts[b]], lba = logs[b][pts[a]];
                    maxLen = Math.Max(maxLen, Math.Abs(lab.Length - lba.Length) / lab.Length);
                    Vector2d moved = vhm.TransportVector(pts[a], lab)[pts[b]];
                    maxAng = Math.Max(maxAng, Vector2d.AngleR(moved.Normalized, -lba.Normalized));
                }
            output.WriteLine($"length asymmetry {maxLen}, angle {maxAng * 180 / Math.PI} deg");
            Assert.True(maxLen < 0.02, $"length asymmetry {maxLen}");
            Assert.True(maxAng < 3 * Math.PI / 180, $"angle {maxAng * 180 / Math.PI} deg");
        }

        // ---------------------------------------------------------------- sparse Cholesky

        [Fact]
        public void SparseCholesky_SolvesAndIsComponentwiseAccurate()
        {
            // random sparse SPD matrix: compare with the dense Cholesky solve
            var rnd = new Random(7);
            int n = 60;
            var dense = new DenseMatrix(n, n);
            var entries = new List<KeyValuePair<Index2i, double>>();
            for (int k = 0; k < 150; ++k) {
                int i = rnd.Next(n), j = rnd.Next(n);
                if (i == j) continue;
                double w = rnd.NextDouble();
                dense[i, j] -= w; dense[j, i] -= w; dense[i, i] += w; dense[j, j] += w;
                entries.Add(new KeyValuePair<Index2i, double>(new Index2i(i, j), -w));
                entries.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), w));
                entries.Add(new KeyValuePair<Index2i, double>(new Index2i(j, j), w));
            }
            for (int i = 0; i < n; ++i) {
                dense[i, i] += 0.1;
                entries.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), 0.1));
            }
            var chol = new SparseCholesky(n, entries);
            Assert.True(chol.IsValid);
            double[] b = Enumerable.Range(0, n).Select(i => rnd.NextDouble() - 0.5).ToArray();
            double[] x = new double[n], xd = new double[n], tmp = new double[n];
            chol.Solve(b, x);
            var dc = new CholeskyDecomposition(dense);
            Assert.True(dc.Compute());
            dc.Solve(b, xd, tmp);
            for (int i = 0; i < n; ++i) Assert.Equal(xd[i], x[i], 9);

            // heat on a path of 800 vertices, (I + t L) u = delta: u decays by a fixed ratio per vertex
            // down to 1e-280, and every entry is accurate (CG would only resolve 1e-16 of the maximum)
            int m = 800; double t = 0.25;
            var path = new List<KeyValuePair<Index2i, double>>();
            for (int i = 0; i < m; ++i) path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), 1));
            for (int i = 0; i + 1 < m; ++i) {
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i + 1), -t));
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), t));
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i + 1, i + 1), t));
            }
            var pc = new SparseCholesky(m, path);
            double[] rhs = new double[m], u = new double[m];
            rhs[0] = 1;
            pc.Solve(rhs, u);
            // ratio r of t r^2 - (1 + 2t) r + t = 0, the decaying root
            double ratio = ((1 + 2 * t) - Math.Sqrt((1 + 2 * t) * (1 + 2 * t) - 4 * t * t)) / (2 * t);
            for (int i = 1; i < 380; ++i)
                Assert.Equal(ratio, u[i + 1] / u[i], 9);
            Assert.True(u[379] < 1e-200 && u[379] > 0);
            Assert.False(new SparseCholesky(2, new[] { new KeyValuePair<Index2i, double>(new Index2i(0, 0), -1),
                new KeyValuePair<Index2i, double>(new Index2i(1, 1), 1) }).IsValid);
        }

        // ---------------------------------------------------------------- review fixes

        static DMesh3 Strip(int n)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n + 1, 3];
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j < 3; ++j) vid[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < 2; ++j) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        [Fact]
        public void VectorHeat_LongerDiffusionTimeOnlyForTheQueryThatUnderflows()
        {
            // 800 edge lengths: heat from one end underflows at the other, so that query diffuses longer; a
            // later query from the middle must not inherit it
            DMesh3 mesh = Strip(800);
            int end = NearestVertex(mesh, new Vector3d(0, 1, 0)), mid = NearestVertex(mesh, new Vector3d(400, 1, 0));
            var vhm = new VectorHeatMethod(mesh);
            var fresh = new VectorHeatMethod(mesh);
            Vector2d[] expected = fresh.ComputeLogMap(mid);
            double t0 = fresh.DiffusionTime;

            double[] f = vhm.ExtendScalar(new[] { new KeyValuePair<int, double>(end, 1) });
            Assert.True(vhm.DiffusionTime > t0, $"{vhm.DiffusionTime} vs {t0}");
            Assert.Equal(1, f[NearestVertex(mesh, new Vector3d(800, 1, 0))], 6);

            Vector2d[] log = vhm.ComputeLogMap(mid);
            Assert.Equal(t0, vhm.DiffusionTime);
            foreach (int v in mesh.VertexIndices())
                Assert.True(log[v].Distance(expected[v]) < 1e-9, $"{v}: {log[v]} vs {expected[v]}");
        }

        [Fact]
        public void LogMap_BoundarySource_EveryNeighbourExact()
        {
            DMesh3 mesh = Grid(20, 11, (x, y) => 0);
            foreach (Vector3d p in new[] { new Vector3d(0, -10, 0), new Vector3d(-10, 4, 0), new Vector3d(-10, -10, 0) }) {
                int src = NearestVertex(mesh, p);
                Assert.True(mesh.IsBoundaryVertex(src));
                var vhm = new VectorHeatMethod(mesh);
                Vector2d[] log = vhm.ComputeLogMap(src);
                foreach (int k in mesh.VtxVerticesItr(src)) {
                    Vector3d d = mesh.GetVertex(k) - mesh.GetVertex(src);
                    Vector3d got = vhm.Triangulation.ToWorld(src, log[k]);
                    Assert.True(got.Distance(d) < 1e-9, $"source {p}, neighbour {d}: {got}");
                }
            }
        }

        [Fact]
        public void VectorHeat_TimeFactorChange_KeepsTheTriangulation()
        {
            DMesh3 mesh = GoodSphere(1, 10);
            var vhm = new VectorHeatMethod(mesh);
            int src = mesh.VertexIndices().First();
            vhm.ComputeLogMap(src);
            IntrinsicTriangulation T = vhm.Triangulation;
            double t1 = vhm.DiffusionTime;
            vhm.TimeFactor = 2;
            vhm.ComputeLogMap(src);
            Assert.Same(T, vhm.Triangulation);
            Assert.Equal(2 * t1, vhm.DiffusionTime, 12);
        }

        [Fact]
        public void InvalidUse_ClearErrors()
        {
            DMesh3 mesh = Grid(4, 5, (x, y) => 0);
            int isolated = mesh.AppendVertex(new Vector3d(9, 9, 9));
            var vhm = new VectorHeatMethod(mesh);
            int src = mesh.VertexIndices().First();
            Assert.Throws<ArgumentException>(() => vhm.Triangulation.ToWorld(isolated, new Vector2d(1, 0)));
            Assert.Throws<ArgumentException>(() => vhm.Triangulation.FromWorld(isolated, Vector3d.AxisX));
            Assert.Throws<ArgumentException>(() => vhm.Triangulation.ToWorld(mesh.MaxVertexID + 5, new Vector2d(1, 0)));
            Assert.Throws<ArgumentException>(() => vhm.ComputeLogMap(isolated));
            vhm.TimeFactor = -1;
            Assert.Throws<ArgumentException>(() => vhm.ComputeLogMap(src));
            vhm.TimeFactor = 1;
            Assert.Equal(Vector2d.Zero, vhm.ComputeLogMap(src)[src]);
        }

        [Fact]
        public void VectorHeat_FailedFactorization_ClearErrorAndRecovers()
        {
            // a diffusion time of 1e300 edge lengths squared: mass + t L is not numerically positive definite
            DMesh3 mesh = Grid(4, 5, (x, y) => 0);
            int src = mesh.VertexIndices().First();
            var vhm = new VectorHeatMethod(mesh) { TimeFactor = 1e300 };
            var e = Assert.Throws<InvalidOperationException>(() => vhm.ComputeLogMap(src));
            Assert.Contains("heat operator", e.Message);
            Assert.Throws<InvalidOperationException>(() => vhm.ComputeLogMap(src));     // not cached as valid

            // back to a sane time: the same results as a fresh instance
            vhm.TimeFactor = 1;
            Vector2d[] log = vhm.ComputeLogMap(src), expected = new VectorHeatMethod(mesh).ComputeLogMap(src);
            foreach (int v in mesh.VertexIndices())
                Assert.True(log[v].Distance(expected[v]) < 1e-12);
        }

        [Fact]
        public void VectorHeat_TriangulationGetter_DoesNotTouchTheHeatOperators()
        {
            DMesh3 mesh = Strip(800);
            var vhm = new VectorHeatMethod(mesh);
            vhm.ExtendScalar(new[] { new KeyValuePair<int, double>(NearestVertex(mesh, new Vector3d(0, 1, 0)), 1) });
            double t = vhm.DiffusionTime;
            Assert.NotNull(vhm.Triangulation);
            Assert.Equal(t, vhm.DiffusionTime);     // still the time the last query used
            vhm.TimeFactor = 0;
            Assert.NotNull(vhm.Triangulation);      // needs no diffusion time
        }

        [Fact]
        public void ToWorld_FoldedFan_EdgesExactAndClosingCornerTheShortWay()
        {
            // an interior saddle vertex whose fan folds back in the projection into its tangent plane: the third
            // rim point lies (projected) before the second one
            DMesh3 mesh = new DMesh3();
            int v = mesh.AppendVertex(Vector3d.Zero);
            double[] deg = { 0, 110, 30, 150, 230, 310 }, z = { 0, 1, -1, 0, 0, 0 };
            var rim = new List<int>();
            for (int k = 0; k < deg.Length; ++k) {
                double a = deg[k] * Math.PI / 180;
                rim.Add(mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), z[k])));
            }
            for (int k = 0; k < rim.Count; ++k) Assert.True(mesh.AppendTriangle(v, rim[k], rim[(k + 1) % rim.Count]) >= 0);
            var T = new IntrinsicTriangulation(mesh);
            int iv = T.VertexIndex(v);
            Vector3d e1, e2, n;
            T.GetTangentBasis(v, out e1, out e2, out n);
            Func<Vector3d, double> proj = d => Math.Atan2(d.Dot(e2), d.Dot(e1));

            // the edges in signpost order, with their projected directions; the projection must fold
            var edges = new List<KeyValuePair<double, Vector3d>>();
            int h0 = T.VertexHalfedge(iv), h = h0;
            do {
                edges.Add(new KeyValuePair<double, Vector3d>(T.Signpost(h), mesh.GetVertex(T.VertexID(T.Head(h)))));
                h = T.NextAroundVertex(h);
            } while (h != h0);
            bool folds = false;
            for (int k = 1; k < edges.Count; ++k)
                if (IntrinsicTriangulation.wrap(proj(edges[k].Value) - proj(edges[k - 1].Value)) > Math.PI) folds = true;
            Assert.True(folds);

            Func<double, Vector3d> toWorld = a => T.ToWorld(v, new Vector2d(Math.Cos(a), Math.Sin(a)));
            foreach (var kv in edges) {
                Vector3d d = kv.Value - kv.Value.Dot(n) * n;
                Assert.True(AngleBetween(toWorld(kv.Key), d) < 1e-9, $"edge at signpost {kv.Key}");
            }
            // the corner between the last and the first edge maps into the short arc between their projections
            double last = edges[edges.Count - 1].Key;
            double aLast = proj(edges[edges.Count - 1].Value), aFirst = proj(edges[0].Value);
            double arc = IntrinsicTriangulation.wrap(aFirst - aLast);
            Assert.True(arc < Math.PI);
            Vector3d mid = toWorld(0.5 * (last + 2 * Math.PI));
            double off = IntrinsicTriangulation.wrap(proj(mid) - aLast);
            Assert.True(off > 0 && off < arc, $"closing corner maps to {off} outside (0, {arc})");
        }

        [Fact]
        public void SparseCholesky_SolveLog_BeyondTheDoubleRange()
        {
            // heat on a path of 3000 vertices, (I + t L) u = delta: u decays by a fixed ratio per vertex, down to
            // about 1e-1250, far below the smallest double; the log-domain substitutions keep every entry exact
            int m = 3000; double t = 0.25;
            var path = new List<KeyValuePair<Index2i, double>>();
            for (int i = 0; i < m; ++i) path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), 1));
            for (int i = 0; i + 1 < m; ++i) {
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i + 1), -t));
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), t));
                path.Add(new KeyValuePair<Index2i, double>(new Index2i(i + 1, i + 1), t));
            }
            var pc = new SparseCholesky(m, path);
            Assert.True(pc.IsValid);
            double[] logB = new double[m], logU = new double[m];
            for (int i = 0; i < m; ++i) logB[i] = double.NegativeInfinity;
            logB[0] = 0;
            Assert.True(pc.SolveLog(logB, logU));
            double ratio = ((1 + 2 * t) - Math.Sqrt((1 + 2 * t) * (1 + 2 * t) - 4 * t * t)) / (2 * t);
            for (int i = 1; i < m - 100; ++i)
                Assert.Equal(Math.Log(ratio), logU[i + 1] - logU[i], 9);
            Assert.True(logU[m - 1] < -2000);

            // the same as Solve where that does not underflow
            double[] b = new double[m], u = new double[m];
            b[0] = 1;
            pc.Solve(b, u);
            for (int i = 0; i < 100; ++i) Assert.Equal(Math.Log(u[i]), logU[i], 9);

            // not an M-matrix (a positive off-diagonal entry): refused
            var notM = new SparseCholesky(2, new[] {
                new KeyValuePair<Index2i, double>(new Index2i(0, 0), 2), new KeyValuePair<Index2i, double>(new Index2i(1, 1), 2),
                new KeyValuePair<Index2i, double>(new Index2i(0, 1), 1) });
            Assert.True(notM.IsValid);
            Assert.False(notM.SolveLog(new[] { 0.0, double.NegativeInfinity }, new double[2]));
        }
    }
}
