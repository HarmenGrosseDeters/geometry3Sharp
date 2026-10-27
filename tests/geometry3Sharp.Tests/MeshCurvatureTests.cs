using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshCurvature (Meyer et al. 2003): analytic surfaces (sphere, cylinder, plane, torus), convergence under
    /// refinement, Gauss-Bonnet, degenerate triangles, boundaries and bowties, scale invariance.
    /// </summary>
    public class MeshCurvatureTests
    {
        // icosphere: the icosahedron, subdivided sub times (midpoints projected to the sphere)
        internal static DMesh3 Icosphere(double r, int sub)
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            var m = new DMesh3();
            var vs = new[] { new Vector3d(-1, t, 0), new Vector3d(1, t, 0), new Vector3d(-1, -t, 0), new Vector3d(1, -t, 0),
                new Vector3d(0, -1, t), new Vector3d(0, 1, t), new Vector3d(0, -1, -t), new Vector3d(0, 1, -t),
                new Vector3d(t, 0, -1), new Vector3d(t, 0, 1), new Vector3d(-t, 0, -1), new Vector3d(-t, 0, 1) };
            foreach (var v in vs) m.AppendVertex(r * v.Normalized);
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            for (int i = 0; i < f.Length; i += 3) m.AppendTriangle(f[i], f[i + 1], f[i + 2]);
            for (int s = 0; s < sub; ++s) {
                var next = new DMesh3();
                foreach (Vector3d v in m.Vertices()) next.AppendVertex(v);
                var mid = new Dictionary<(int, int), int>();
                Func<int, int, int> M = (a, b) => {
                    var key = a < b ? (a, b) : (b, a);
                    int id;
                    if (mid.TryGetValue(key, out id) == false) {
                        id = next.AppendVertex(r * (0.5 * (m.GetVertex(a) + m.GetVertex(b))).Normalized);
                        mid[key] = id;
                    }
                    return id;
                };
                foreach (int tid in m.TriangleIndices()) {
                    Index3i tt = m.GetTriangle(tid);
                    int ab = M(tt.a, tt.b), bc = M(tt.b, tt.c), ca = M(tt.c, tt.a);
                    next.AppendTriangle(tt.a, ab, ca); next.AppendTriangle(ab, tt.b, bc);
                    next.AppendTriangle(ca, bc, tt.c); next.AppendTriangle(ab, bc, ca);
                }
                m = next;
            }
            return m;
        }

        // open cylinder of radius r and height h, nu vertices per ring, nv strips; every other ring rotated by half a
        // step (antiprism bands, so the triangulation is not aligned with the principal directions)
        internal static DMesh3 Cylinder(double r, double h, int nu, int nv)
        {
            var m = new DMesh3();
            for (int j = 0; j <= nv; ++j)
                for (int i = 0; i < nu; ++i) {
                    double u = 2 * Math.PI * (i + 0.5 * (j % 2)) / nu;
                    m.AppendVertex(new Vector3d(r * Math.Cos(u), r * Math.Sin(u), h * j / nv));
                }
            for (int j = 0; j < nv; ++j)
                for (int i = 0; i < nu; ++i) {
                    int a = j * nu + i, b = j * nu + (i + 1) % nu, c = a + nu, d = b + nu;
                    if (j % 2 == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                }
            return m;
        }

        // n x n grid on the unit square, interior vertices jittered in the plane, then tilted out of the xy plane
        static DMesh3 JitteredPlane(int n, int seed)
        {
            var rnd = new Random(seed);
            var m = new DMesh3();
            Matrix3d rot = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    if (i > 0 && j > 0 && i < n && j < n) {
                        x += (rnd.NextDouble() - 0.5) * 0.6 / n;
                        y += (rnd.NextDouble() - 0.5) * 0.6 / n;
                    }
                    m.AppendVertex(rot * new Vector3d(x, y, 0) + new Vector3d(0.3, -0.2, 0.1));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c);
                    m.AppendTriangle(a, c, d);
                }
            return m;
        }

        static double MaxError(DMesh3 m, MeshCurvature c, double h, double k, out double maxK)
        {
            double maxH = 0;
            maxK = 0;
            foreach (int v in m.VertexIndices()) {
                if (c.Valid[v] == false) continue;
                maxH = Math.Max(maxH, Math.Abs(c.Mean[v] - h));
                maxK = Math.Max(maxK, Math.Abs(c.Gaussian[v] - k));
            }
            return maxH;
        }

        static void AssertFinite(MeshCurvature c)
        {
            for (int v = 0; v < c.Mean.Length; ++v) {
                Assert.True(double.IsFinite(c.Mean[v]) && double.IsFinite(c.Gaussian[v]) && double.IsFinite(c.MixedArea[v])
                    && double.IsFinite(c.AngleDeficit[v]) && c.MeanCurvatureNormal[v].IsFinite, $"vertex {v}");
            }
        }

        [Fact]
        public void Sphere_MeanAndGaussian_ConvergeUnderRefinement()
        {
            // H = 1 / r, K = 1 / r^2: the largest error over all vertices drops by about 4 per subdivision (second order;
            // from the second subdivision on, the first is exact for H by symmetry)
            double r = 2;
            double prevH = double.MaxValue, prevK = double.MaxValue;
            for (int sub = 2; sub <= 4; ++sub) {
                DMesh3 m = Icosphere(r, sub);
                var c = MeshCurvature.Compute(m);
                AssertFinite(c);
                Assert.Equal(m.VertexCount, c.ValidVertices);
                double maxK;
                double maxH = MaxError(m, c, 1 / r, 1 / (r * r), out maxK);
                if (sub > 2) {
                    Assert.True(maxH < prevH / 3.5, $"sub {sub}: H error {maxH} vs {prevH}");
                    Assert.True(maxK < prevK / 3.5, $"sub {sub}: K error {maxK} vs {prevK}");
                }
                prevH = maxH; prevK = maxK;
                // Gauss-Bonnet: the deficits sum to 2 pi chi = 4 pi; the mixed areas to the mesh's area
                Assert.Equal(4 * Math.PI, m.VertexIndices().Sum(v => c.AngleDeficit[v]), 9);
                double area = m.TriangleIndices().Sum(t => m.GetTriArea(t));
                Assert.Equal(area, m.VertexIndices().Sum(v => c.MixedArea[v]), 9);
            }
            // 2562 vertices: H within 1e-5 / r, K within 2e-3 / r^2 (relative)
            Assert.True(prevH * r < 1e-5, $"{prevH * r}");
            Assert.True(prevK * r * r < 2e-3, $"{prevK * r * r}");
        }

        [Fact]
        public void Cylinder_GaussianZero_MeanHalfInverseRadius()
        {
            // K = 0 (the bands are developable up to rounding), H = 1 / (2 r) converging with second order; the rims
            // are boundary vertices (not Valid)
            double r = 1.5, prev = double.MaxValue;
            foreach (int n in new[] { 8, 16, 32, 64 }) {
                DMesh3 m = Cylinder(r, 3, n, n / 2);
                var c = MeshCurvature.Compute(m);
                AssertFinite(c);
                Assert.Equal(2 * n, c.BoundaryVertices);
                Assert.Equal(m.VertexCount - 2 * n, c.ValidVertices);
                double maxK;
                double maxH = MaxError(m, c, 1 / (2 * r), 0, out maxK);
                Assert.True(maxK * r * r < 1e-11, $"n {n}: K {maxK}");
                Assert.True(maxH < prev / 3.5, $"n {n}: H error {maxH} vs {prev}");
                prev = maxH;
            }
            Assert.True(prev * r < 1e-3, $"{prev * r}");
        }

        [Fact]
        public void Plane_ZeroEverywhere_BoundaryOneSided()
        {
            DMesh3 m = JitteredPlane(12, 3);
            var c = MeshCurvature.Compute(m);
            AssertFinite(c);
            Assert.Equal(11 * 11, c.ValidVertices);
            Assert.Equal(4 * 12, c.BoundaryVertices);
            foreach (int v in m.VertexIndices()) {
                // relative to the inverse edge length (1 / 12)
                Assert.True(Math.Abs(c.Mean[v]) < 1e-12, $"{v}: H {c.Mean[v]}");
                bool corner = m.GetVtxEdgeCount(v) <= 3;
                // boundary: deficit against pi, 0 along a straight edge, pi / 2 at the four corners
                Assert.Equal(corner ? Math.PI / 2 : 0, c.AngleDeficit[v], 12);
                if (corner == false)
                    Assert.True(Math.Abs(c.Gaussian[v]) < 1e-9, $"{v}: K {c.Gaussian[v]}");
            }
        }

        [Fact]
        public void Torus_GaussBonnetAndSigns()
        {
            double R = 2, r = 0.5;
            DMesh3 m = Torus(R, r, 96, 48);
            var c = MeshCurvature.Compute(m);
            AssertFinite(c);
            Assert.Equal(m.VertexCount, c.ValidVertices);
            Assert.Equal(0, m.VertexIndices().Sum(v => c.AngleDeficit[v]), 9);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                double rho = Math.Sqrt(p.x * p.x + p.y * p.y), cosv = (rho - R) / r;
                // K = cos v / (r (R + r cos v)), H = (R + 2 r cos v) / (2 r (R + r cos v))
                double k = cosv / (r * (R + r * cosv)), h = (R + 2 * r * cosv) / (2 * r * (R + r * cosv));
                Assert.True(Math.Abs(c.Gaussian[v] - k) < 0.02 / (r * r), $"{v}: K {c.Gaussian[v]} vs {k}");
                Assert.True(Math.Abs(c.Mean[v] - h) < 0.01 / r, $"{v}: H {c.Mean[v]} vs {h}");
            }
            // orientation: H changes sign, K does not
            foreach (int t in m.TriangleIndices().ToArray()) m.ReverseTriOrientation(t);
            var flipped = MeshCurvature.Compute(m);
            foreach (int v in m.VertexIndices()) {
                Assert.Equal(-c.Mean[v], flipped.Mean[v], 12);
                Assert.Equal(c.Gaussian[v], flipped.Gaussian[v], 12);
            }
        }

        [Fact]
        public void MixedAreaAndDeficit_MatchExistingHelpers()
        {
            // MeshWeights.VoronoiArea is the same mixed area; MeshUtil.DiscreteGaussCurvature the negative deficit
            DMesh3 m = Torus(2, 0.7, 40, 17);
            var c = MeshCurvature.Compute(m);
            foreach (int v in m.VertexIndices()) {
                Assert.Equal(MeshWeights.VoronoiArea(m, v), c.MixedArea[v], 12);
                Assert.Equal(-MeshUtil.DiscreteGaussCurvature(m, v), c.AngleDeficit[v], 12);
            }
        }

        [Fact]
        public void DegenerateTriangles_FiniteAndGaussBonnetKept()
        {
            DMesh3 sphere = Icosphere(1, 2);
            var before = MeshCurvature.Compute(sphere);
            // a collinear sliver: the triangles (a, b, c), (b, a, d) at an edge become (a, m, c), (m, b, c), the sliver
            // (a, b, m) with m the midpoint of a b, and (b, a, d); the surface is the same
            DMesh3 sliver = new DMesh3(sphere);
            int eid = sliver.EdgeIndices().First();
            Index2i ev = sliver.GetEdgeV(eid), et = sliver.GetEdgeT(eid);
            Index3i t0 = sliver.GetTriangle(et.a);
            int a = ev.a, b = ev.b;
            if (IndexUtil.find_tri_ordered_edge(a, b, ref t0) == DMesh3.InvalidID) { a = ev.b; b = ev.a; }
            int tc = IndexUtil.find_tri_ordered_edge(a, b, ref t0) == DMesh3.InvalidID ? et.b : et.a, td = tc == et.a ? et.b : et.a;
            int cv = IndexUtil.find_tri_other_vtx(a, b, sliver.GetTriangle(tc)), dv = IndexUtil.find_tri_other_vtx(a, b, sliver.GetTriangle(td));
            Assert.Equal(MeshResult.Ok, sliver.RemoveTriangle(tc, false, false));
            Assert.Equal(MeshResult.Ok, sliver.RemoveTriangle(td, false, false));
            int mv = sliver.AppendVertex(0.5 * (sliver.GetVertex(a) + sliver.GetVertex(b)));
            Assert.True(sliver.AppendTriangle(a, mv, cv) >= 0);
            Assert.True(sliver.AppendTriangle(mv, b, cv) >= 0);
            Assert.True(sliver.AppendTriangle(a, b, mv) >= 0);
            Assert.True(sliver.AppendTriangle(b, a, dv) >= 0);
            Assert.True(sliver.IsClosed());
            var cs = MeshCurvature.Compute(sliver);
            AssertFinite(cs);
            Assert.Equal(1, cs.DegenerateTriangles);
            Assert.Equal(4 * Math.PI, sliver.VertexIndices().Sum(v => cs.AngleDeficit[v]), 9);
            Assert.Equal(0, cs.AngleDeficit[mv], 12);
            // vertices away from the change keep their values
            foreach (int v in sphere.VertexIndices()) {
                if (v == a || v == b || v == cv || v == dv) continue;
                Assert.Equal(before.Mean[v], cs.Mean[v], 12);
                Assert.Equal(before.Gaussian[v], cs.Gaussian[v], 12);
            }

            // coincident corners: an edge split with the new vertex on an endpoint (two triangles with a zero-length edge)
            DMesh3 coincident = new DMesh3(sphere);
            int e2 = coincident.EdgeIndices().Skip(7).First();
            Index2i ev2 = coincident.GetEdgeV(e2);
            DMesh3.EdgeSplitInfo info;
            Assert.Equal(MeshResult.Ok, coincident.SplitEdge(e2, out info));
            coincident.SetVertex(info.vNew, coincident.GetVertex(ev2.a));
            var cc = MeshCurvature.Compute(coincident);
            AssertFinite(cc);
            Assert.Equal(2, cc.DegenerateTriangles);
            Assert.Equal(4 * Math.PI, coincident.VertexIndices().Sum(v => cc.AngleDeficit[v]), 9);
            // the two coincident vertices share the deficit of the original vertex
            Assert.Equal(before.AngleDeficit[ev2.a], cc.AngleDeficit[ev2.a] + cc.AngleDeficit[info.vNew], 12);

            // all triangles degenerate (a flat fan), and a single zero-area triangle: no area, nothing Valid, no NaN
            var flat = new DMesh3();
            for (int i = 0; i < 5; ++i) flat.AppendVertex(new Vector3d(i, 0, 0));
            flat.AppendTriangle(0, 1, 2); flat.AppendTriangle(0, 2, 3); flat.AppendTriangle(0, 3, 4);
            var cf = MeshCurvature.Compute(flat);
            AssertFinite(cf);
            Assert.Equal(0, cf.ValidVertices);
            Assert.Equal(3, cf.DegenerateTriangles);
            var point = new DMesh3();
            for (int i = 0; i < 3; ++i) point.AppendVertex(new Vector3d(1, 2, 3));
            point.AppendTriangle(0, 1, 2);
            var cp = MeshCurvature.Compute(point);
            AssertFinite(cp);
            Assert.Equal(0, cp.ValidVertices);
            // pi / 3 per corner
            Assert.Equal(Math.PI - Math.PI / 3, cp.AngleDeficit[0], 12);
        }

        [Fact]
        public void Bowtie_NotValid_Finite()
        {
            // two tetrahedra sharing a vertex
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero);
            foreach (double s in new[] { 1.0, -1.0 }) {
                int a = m.AppendVertex(new Vector3d(s, 0, 0)), b = m.AppendVertex(new Vector3d(s, s, 0)), c = m.AppendVertex(new Vector3d(s, 0, s));
                int[][] faces = { new[] { 0, b, a }, new[] { 0, a, c }, new[] { 0, c, b }, new[] { a, b, c } };
                foreach (var f in faces) {
                    if (s < 0) Array.Reverse(f);
                    Assert.True(m.AppendTriangle(f[0], f[1], f[2]) >= 0);
                }
            }
            Assert.True(m.IsBowtieVertex(0));
            var c0 = MeshCurvature.Compute(m);
            AssertFinite(c0);
            Assert.False(c0.Valid[0]);
            Assert.Equal(1, c0.BowtieVertices);
            Assert.Equal(m.VertexCount - 1, c0.ValidVertices);
        }

        [Fact]
        public void Invariance_SimilarityAll()
        {
            DMesh3 m = Torus(2, 0.6, 30, 14);
            MeshEditor.Append(m, Icosphere(0.5, 2));
            var c0 = MeshCurvature.Compute(m);
            foreach (Similarity s in Similarity.All()) {
                var c = MeshCurvature.Compute(Invariance.Transformed(m, s));
                Assert.Equal(c0.ValidVertices, c.ValidVertices);
                foreach (int v in m.VertexIndices()) {
                    double h = c.Mean[v] * s.Scale, k = c.Gaussian[v] * s.Scale * s.Scale, a = c.MixedArea[v] / (s.Scale * s.Scale);
                    if (s.IsExact) {
                        Assert.True(h.Equals(c0.Mean[v]) && k.Equals(c0.Gaussian[v]) && a.Equals(c0.MixedArea[v])
                            && c.AngleDeficit[v].Equals(c0.AngleDeficit[v]), $"{s}: vertex {v}");
                        continue;
                    }
                    // UTM: coordinates of 6e6 with a size of 5 keep about 9 digits of the edge vectors
                    double tol = s.Translation == Vector3d.Zero ? 1e-9 : 1e-4;
                    Assert.True(Math.Abs(h - c0.Mean[v]) < tol * 3, $"{s}: H {h} vs {c0.Mean[v]}");
                    Assert.True(Math.Abs(k - c0.Gaussian[v]) < tol * 30, $"{s}: K {k} vs {c0.Gaussian[v]}");
                    Assert.True(Math.Abs(a - c0.MixedArea[v]) < tol * 0.1, $"{s}: A {a} vs {c0.MixedArea[v]}");
                }
            }
        }
    }
}
