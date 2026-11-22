using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The patch bound of TriangleDeviationBounds (target triangles under a triangle, along its normal) and its use in
    /// MeshHausdorffDistance (PatchBound): it must never be below the largest distance of a point of the triangle
    /// (oracle: dense sampling of the triangle, exact point-triangle distances), also over holes, folds, the other side
    /// of thin plates and inconsistently oriented targets; and it must be tight over flat fine targets, where the other
    /// bounds need many subdivisions.
    /// </summary>
    public class HausdorffPatchBoundTests
    {
        // grid over [x0,x1] x [y0,y1] with n x n vertices, z = h(x,y); skip(i, j): leave out the two triangles of cell (i, j);
        // flip(i, j): reverse their orientation (inconsistent orientation); down: all triangles reversed
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h = null,
            Func<int, int, bool> skip = null, Func<int, int, bool> flip = null, bool down = false, DMesh3 into = null)
        {
            DMesh3 mesh = into ?? new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = x0 + (x1 - x0) * i / (n - 1), y = y0 + (y1 - y0) * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h == null ? 0 : h(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    if (skip != null && skip(i, j)) continue;
                    bool f = down ^ (flip != null && flip(i, j));
                    if (f) {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i + 1, j]);
                        mesh.AppendTriangle(vid[i, j], vid[i, j + 1], vid[i + 1, j + 1]);
                    } else {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    }
                }
            return mesh;
        }

        // largest distance to the target over the triangle, sampled densely (a lower bound of the true value)
        static double SampledMax(DMesh3 target, DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c, int n = 40)
        {
            double m = 0;
            for (int i = 0; i <= n; ++i)
                for (int j = 0; i + j <= n; ++j) {
                    Vector3d p = ((n - i - j) * a + i * b + j * c) / n;
                    int t = tree.FindNearestTriangle(p);
                    m = Math.Max(m, Math.Sqrt(MeshQueries.TriDistanceSqr(target, t, p)));
                }
            return m;
        }

        static double Patch(TriangleDeviationBounds bounds, DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c)
        {
            return bounds.patch_bound(a, b, c, tree.FindNearestTriangle(a), tree.FindNearestTriangle(b), tree.FindNearestTriangle(c));
        }

        // random triangles over the target's box (heights in [zlo, zhi]); checks the patch bound against sampling;
        // returns how many got a finite bound
        static int CheckSound(DMesh3 target, int seed, int count, double zlo, double zhi, double size, double tilt = 0.2)
        {
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            var rand = new Random(seed);
            AxisAlignedBox3d box = target.CachedBounds;
            int finite = 0;
            for (int k = 0; k < count; ++k) {
                Func<Vector3d> pt = () => new Vector3d(box.Min.x + rand.NextDouble() * box.Width, box.Min.y + rand.NextDouble() * box.Height,
                    zlo + rand.NextDouble() * (zhi - zlo));
                Vector3d a = pt(), d1 = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, tilt * (rand.NextDouble() - 0.5)),
                    d2 = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, tilt * (rand.NextDouble() - 0.5));
                Vector3d b = a + size * d1, c = a + size * d2;
                // both orientations: the bound must not depend on the side the normal points to
                foreach (bool swap in new[] { false, true }) {
                    double u = swap ? Patch(bounds, tree, a, c, b) : Patch(bounds, tree, a, b, c);
                    if (double.IsPositiveInfinity(u)) continue;
                    finite++;
                    double s = SampledMax(target, tree, a, b, c);
                    Assert.True(u >= s - 1e-12, $"seed {seed} #{k}: patch bound {u} below the sampled max {s}; {a} {b} {c}");
                }
            }
            return finite;
        }

        [Fact]
        public void FlatGrid_BoundIsTheHeight()
        {
            DMesh3 target = Grid(0, 0, 1, 1, 41);
            var tree = new DMeshAABBTree3(target, true);
            // (about 1000 grid triangles under it)
            var bounds = new TriangleDeviationBounds(target) { MaxPatchTriangles = 4096 };
            // a large triangle over many grid triangles, tilted: heights 0.01, 0.02, 0.005
            Vector3d a = new Vector3d(0.1, 0.1, 0.01), b = new Vector3d(0.9, 0.2, 0.02), c = new Vector3d(0.3, 0.85, 0.005);
            double u = Patch(bounds, tree, a, b, c);
            // (measured along the triangle's normal, so 0.02 / cos of its tilt against the grid: 1.00015 * 0.02)
            Assert.True(u >= 0.02 && u < 0.02 * 1.0002, "patch bound " + u);
            // the other bounds of UpperBound are far above it (they need subdivisions)
            int na = tree.FindNearestTriangle(a), nb = tree.FindNearestTriangle(b), nc = tree.FindNearestTriangle(c);
            double old = bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0);
            Assert.True(old > 0.1, "without the patch bound " + old);
            bounds.PatchBound = true;
            Assert.Equal(u, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0));
            // (not tried when the other bounds are below the threshold)
            Assert.Equal(old, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 1));
            // PatchMinTargetSize: tried only where the grid's triangles (size 0.025: the square root of the doubled area) are at
            // least that many thresholds in size
            bounds.PatchMinTargetSize = 1;
            Assert.Equal(u, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0.0249));
            Assert.Equal(old, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0.0251));
            bounds.PatchMinTargetSize = 2;
            Assert.Equal(u, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0.0124));
            Assert.Equal(old, bounds.UpperBound(a, b, c, 0.01, 0.02, 0.005, na, nb, nc, 0.0126));
        }

        [Fact]
        public void Flat_Hole_Fold_Inconsistent_Plate_AlwaysSound()
        {
            // a hole of 3 x 3 cells in the middle
            DMesh3 hole = Grid(0, 0, 1, 1, 21, skip: (i, j) => i >= 9 && i <= 11 && j >= 9 && j <= 11);
            Assert.True(CheckSound(hole, 1, 300, -0.05, 0.05, 0.5) > 50);
            // a single missing triangle (a hole smaller than a cell) under most query triangles
            DMesh3 pin = new DMesh3(Grid(0, 0, 1, 1, 21));
            pin.RemoveTriangle(2 * (10 * 20 + 10));
            Assert.True(CheckSound(pin, 2, 300, -0.05, 0.05, 0.5) > 50);
            // inconsistent orientation: a checkerboard of flipped cells, and a flipped block
            Assert.True(CheckSound(Grid(0, 0, 1, 1, 21, flip: (i, j) => i >= 8 && i <= 12 && j >= 8 && j <= 12), 3, 300, -0.05, 0.05, 0.5) > 50);
            // (every edge inconsistent: never covered)
            Assert.Equal(0, CheckSound(Grid(0, 0, 1, 1, 21, flip: (i, j) => ((i + j) & 1) == 0), 4, 200, -0.05, 0.05, 0.5));
            // a thin plate: top up, bottom down 0.01 below (not connected); query triangles on both sides and in between
            DMesh3 plate = Grid(0, 0, 1, 1, 21);
            Grid(0, 0, 1, 1, 17, h: (x, y) => -0.01, down: true, into: plate);
            Assert.True(CheckSound(plate, 5, 400, -0.06, 0.05, 0.5) > 50);
            // a fold: a ridge that overhangs (z = 0 plane plus a sheet that goes up and folds back over the plane)
            DMesh3 fold = Grid(0, 0, 1, 1, 21);
            Grid(0.2, 0.2, 0.8, 0.8, 9, h: (x, y) => 0.02 + 0.3 * x * (1 - x), down: true, into: fold);
            Grid(0.3, 0.3, 0.7, 0.7, 9, h: (x, y) => 0.03, into: fold);
            Assert.True(CheckSound(fold, 6, 400, -0.05, 0.12, 0.5) > 50);
            // bumpy height fields at several amplitudes: curved, positive and negative projections
            foreach (double amp in new[] { 0.002, 0.02, 0.2 }) {
                DMesh3 bumpy = Grid(0, 0, 1, 1, 31, h: (x, y) => amp * Math.Sin(9 * x) * Math.Cos(7 * y));
                CheckSound(bumpy, 7 + (int)(amp * 1000), 300, -amp - 0.03, amp + 0.03, 0.4);
            }
            // query triangles nearly perpendicular to the targets (normals almost in their plane)
            DMesh3 wavy = Grid(0, 0, 1, 1, 31, h: (x, y) => 0.01 * Math.Sin(9 * x) * Math.Cos(7 * y));
            CheckSound(wavy, 12, 300, -0.05, 0.05, 0.3, 50);
            CheckSound(hole, 13, 300, -0.05, 0.05, 0.3, 50);
            // a soup (every triangle with its own vertices: no shared edges, all boundary): never covered, and two
            // grids that share only a corner vertex (a bowtie)
            DMesh3 soup = new DMesh3();
            DMesh3 g = Grid(0, 0, 1, 1, 11);
            foreach (int t in g.TriangleIndices()) {
                Index3i tv = g.GetTriangle(t);
                soup.AppendTriangle(soup.AppendVertex(g.GetVertex(tv.a)), soup.AppendVertex(g.GetVertex(tv.b)), soup.AppendVertex(g.GetVertex(tv.c)));
            }
            Assert.Equal(0, CheckSound(soup, 14, 200, -0.05, 0.05, 0.5));
            DMesh3 bowtie = Grid(0, 0, 0.5, 0.5, 6);
            DMesh3 other = Grid(0.5, 0.5, 1, 1, 6);
            var map = new Dictionary<int, int>();
            foreach (int v in other.VertexIndices()) {
                Vector3d p = other.GetVertex(v);
                map[v] = (p == new Vector3d(0.5, 0.5, 0)) ? bowtie.VertexIndices().First(u => bowtie.GetVertex(u) == p) : bowtie.AppendVertex(p);
            }
            foreach (int t in other.TriangleIndices()) {
                Index3i tv = other.GetTriangle(t);
                Assert.True(bowtie.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]) >= 0);
            }
            Assert.True(MeshIterators.BowtieVertices(bowtie).Any());
            CheckSound(bowtie, 15, 300, -0.05, 0.05, 0.4);
            // a closed sphere: triangles inside, outside and through it
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 0.5, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(0.5, 0.5, 0));
            CheckSound(sphere, 11, 400, -0.6, 0.6, 0.6);
        }

        // target triangles T1 = (a, b, c) and T2 over the edge ab on the same side as c (T2 = (a, b, d): inconsistent
        // orientation; (b, a, d): folded back, negative in the projection), so nothing covers the side of ab away
        // from c. The query triangle crosses ab below the tent a-d-b, at height 0.001
        static double TentCase(bool folded, out double sampled)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)),
                c = m.AppendVertex(new Vector3d(0.5, 1, 0)), d = m.AppendVertex(new Vector3d(0.5, 0.3, 0));
            Assert.True(m.AppendTriangle(a, b, c) >= 0);
            Assert.True((folded ? m.AppendTriangle(b, a, d) : m.AppendTriangle(a, b, d)) >= 0);
            var tree = new DMeshAABBTree3(m, true);
            Vector3d p0 = new Vector3d(0.35, -0.05, 0.001), p1 = new Vector3d(0.65, -0.05, 0.001), p2 = new Vector3d(0.5, 0.17, 0.001);
            sampled = SampledMax(m, tree, p0, p1, p2);
            return Patch(new TriangleDeviationBounds(m), tree, p0, p1, p2);
        }

        [Fact]
        public void UncoveredParts_GiveNoBound()
        {
            double s;
            // inconsistent orientation across the shared edge: both triangles on one side of it
            Assert.True(double.IsPositiveInfinity(TentCase(false, out s)));
            Assert.True(s > 0.04);
            // folded back
            Assert.True(double.IsPositiveInfinity(TentCase(true, out s)));
            // a hole (one missing triangle) wholly inside the query triangle: its edges meet no edge of it
            DMesh3 pin = Grid(0, 0, 1, 1, 21);
            pin.RemoveTriangle(2 * (10 * 20 + 10));
            var tree = new DMeshAABBTree3(pin, true);
            var bounds = new TriangleDeviationBounds(pin);
            Vector3d q0 = new Vector3d(0.3, 0.3, 0), q1 = new Vector3d(0.8, 0.35, 0), q2 = new Vector3d(0.45, 0.8, 0);
            Assert.True(SampledMax(pin, tree, q0, q1, q2, 200) > 0.005);
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, q0, q1, q2)));
            // the same without the hole: covered, the bound is 0
            DMesh3 full = Grid(0, 0, 1, 1, 21);
            var ftree = new DMeshAABBTree3(full, true);
            // (plus the rounding slack, about 1e-12 of the size)
            double u0 = Patch(new TriangleDeviationBounds(full), ftree, q0, q1, q2);
            Assert.True(u0 >= 0 && u0 < 1e-10, "covered: " + u0);
        }

        [Fact]
        public void SteepTargetTriangles_AreLeftOut()
        {
            // a flat strip with one cell replaced by a steep fin (heights 0.2 over a projected width 1e-4): the patch
            // leaves the fin out, so the query triangle over it is not covered
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)),
                c = m.AppendVertex(new Vector3d(1, 0.5, 0)), d = m.AppendVertex(new Vector3d(0, 0.5, 0)),
                e = m.AppendVertex(new Vector3d(0, 0.5001, 0.2)), f = m.AppendVertex(new Vector3d(1, 0.5001, 0.2)),
                g = m.AppendVertex(new Vector3d(1, 1, 0.2)), h = m.AppendVertex(new Vector3d(0, 1, 0.2));
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            m.AppendTriangle(d, c, f); m.AppendTriangle(d, f, e);       // the fin, slope 2000
            m.AppendTriangle(e, f, g); m.AppendTriangle(e, g, h);
            var tree = new DMeshAABBTree3(m, true);
            Vector3d p0 = new Vector3d(0.3, 0.3, 0.1), p1 = new Vector3d(0.7, 0.3, 0.1), p2 = new Vector3d(0.5, 0.7, 0.1);
            Assert.True(double.IsPositiveInfinity(Patch(new TriangleDeviationBounds(m), tree, p0, p1, p2)));
            // a gentler fin (projected width 0.01, slope 20) is used, and the bound holds
            m.SetVertex(e, new Vector3d(0, 0.51, 0.2)); m.SetVertex(f, new Vector3d(1, 0.51, 0.2));
            tree = new DMeshAABBTree3(m, true);
            double u = Patch(new TriangleDeviationBounds(m), tree, p0, p1, p2);
            Assert.False(double.IsPositiveInfinity(u));
            Assert.True(u >= SampledMax(m, tree, p0, p1, p2) - 1e-12);
        }

        [Fact]
        public void ThinQueryTriangles_SoundOrNoBound()
        {
            // bounds review counterexample: a sliver (angle 2e-12 at p1) over a tilted plane z = 0.001 + 0.01 x made of
            // two large triangles; the rounding of the clip points at that corner gave a bound 1.6e-6 below the true
            // maximum (at p1, 0.0109994500). Thin triangles are left out now, and UpperBound never goes below the
            // corner distances
            Func<double, double, double> h = (x, y) => 0.001 + 0.01 * x;
            var m = new DMesh3();
            int A = m.AppendVertex(new Vector3d(-1.5, -1.5, h(-1.5, -1.5))), B = m.AppendVertex(new Vector3d(3.5, -1.5, h(3.5, -1.5))),
                C = m.AppendVertex(new Vector3d(3.5, 3.5, h(3.5, 3.5))), D = m.AppendVertex(new Vector3d(-1.5, 3.5, h(-1.5, 3.5)));
            m.AppendTriangle(A, B, C); m.AppendTriangle(A, C, D);
            var tree = new DMeshAABBTree3(m, true);
            var bounds = new TriangleDeviationBounds(m) { PatchBound = true };
            Func<Vector3d, double> dist = (p) => Math.Sqrt(MeshQueries.TriDistanceSqr(m, tree.FindNearestTriangle(p), p));
            foreach (double thin in new[] { 2e-12, 1e-9, 1e-6, 1e-4, 1e-3, 1e-2 }) {
                Vector3d p0 = Vector3d.Zero, p1 = new Vector3d(1, 0, 0), p2 = new Vector3d(-0.095 * thin, thin, 0);
                double d0 = dist(p0), d1 = dist(p1), d2 = dist(p2);
                // the distance to the plane is convex: the max over the triangle is at a corner
                double truth = Math.Max(d0, Math.Max(d1, d2));
                double u = bounds.UpperBound(p0, p1, p2, d0, d1, d2, tree.FindNearestTriangle(p0), tree.FindNearestTriangle(p1), tree.FindNearestTriangle(p2), 0);
                Assert.True(u >= truth, $"thin {thin}: bound {u} below {truth}");
                double pb = Patch(bounds, tree, p0, p1, p2);
                Assert.True(double.IsPositiveInfinity(pb) || pb >= truth, $"thin {thin}: patch bound {pb} below {truth}");
                if (thin < 1e-4) Assert.True(double.IsPositiveInfinity(pb), $"thin {thin}: not left out");
            }
        }

        [Fact]
        public void RandomSlivers_OverTiltedPlanes_SoundOrNoBound()
        {
            // second bounds review: random slivers (angles 1e-12 to 1e-1, any rotation) over planes of slope 1e-3 to 1;
            // the old code (no shape limit, no shaped slack) gave bounds below the truth for about 1 in 70 of the
            // slivers below 1e-8 (up to 1.3e-6). The truth is at a corner (the distance to a plane is convex)
            var rand = new Random(17);
            int finite = 0;
            for (int k = 0; k < 3000; ++k) {
                double thin = Math.Pow(10, -12 + 11 * rand.NextDouble()), sx = -0.5 + 2 * rand.NextDouble();
                double slope = Math.Pow(10, -3 + 3 * rand.NextDouble()), hz = Math.Pow(10, -6 + 6 * rand.NextDouble());
                double ga = 2 * Math.PI * rand.NextDouble();
                Func<double, double, double> h = (x, y) => hz + slope * (Math.Cos(ga) * x + Math.Sin(ga) * y);
                var m = new DMesh3();
                int A = m.AppendVertex(new Vector3d(-5, -5, h(-5, -5))), B = m.AppendVertex(new Vector3d(6, -5, h(6, -5))),
                    C = m.AppendVertex(new Vector3d(6, 6, h(6, 6))), D = m.AppendVertex(new Vector3d(-5, 6, h(-5, 6)));
                m.AppendTriangle(A, B, C); m.AppendTriangle(A, C, D);
                double ang = 2 * Math.PI * rand.NextDouble(), c = Math.Cos(ang), sn = Math.Sin(ang);
                Func<double, double, Vector3d> rot = (x, y) => new Vector3d(c * x - sn * y, sn * x + c * y, 0);
                Vector3d p0 = rot(0, 0), p1 = rot(1, 0), p2 = rot(sx, thin * (0.5 + 1.5 * rand.NextDouble()));
                var tree = new DMeshAABBTree3(m, true);
                Func<Vector3d, double> dist = (p) => Math.Sqrt(MeshQueries.TriDistanceSqr(m, tree.FindNearestTriangle(p), p));
                double truth = Math.Max(dist(p0), Math.Max(dist(p1), dist(p2)));
                double pb = Patch(new TriangleDeviationBounds(m), tree, p0, p1, p2);
                if (double.IsPositiveInfinity(pb)) continue;
                finite++;
                Assert.True(pb >= truth, $"#{k} thin {thin}: patch bound {pb} below {truth} by {truth - pb}");
            }
            Assert.True(finite > 500, "finite: " + finite);
        }

        [Fact]
        public void Degenerate_And_FarTriangles_GiveNoBound()
        {
            DMesh3 target = Grid(0, 0, 1, 1, 11);
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            Vector3d a = new Vector3d(0.2, 0.2, 0.01);
            // zero area, collinear
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, a, a, a)));
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, a, a + Vector3d.AxisX * 0.1, a + Vector3d.AxisX * 0.2)));
            // beyond the target's edge (not covered)
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, new Vector3d(0.8, 0.8, 0), new Vector3d(1.3, 0.8, 0), new Vector3d(0.8, 1.2, 0))));
            // vertical (seen edge on, every target triangle projects to a segment)
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, new Vector3d(0.2, 0.2, 0.1), new Vector3d(0.6, 0.2, 0.1), new Vector3d(0.4, 0.2, 0.5))));
            // more target triangles under it than MaxPatchTriangles
            bounds.MaxPatchTriangles = 10;
            Assert.True(double.IsPositiveInfinity(Patch(bounds, tree, new Vector3d(0.1, 0.1, 0), new Vector3d(0.9, 0.1, 0), new Vector3d(0.5, 0.9, 0))));
        }

        [Fact]
        public void Hausdorff_WithPatchBound_SameInterval_FewerSubdivisions()
        {
            // a coarse mesh over a fine one: the reduced-wrap situation
            DMesh3 fine = Grid(0, 0, 1, 1, 101, h: (x, y) => 0.001 * Math.Sin(13 * x + 5 * y));
            DMesh3 coarse = Grid(0, 0, 1, 1, 5, h: (x, y) => 0.002);
            double tol = 1e-5;
            var plain = new MeshHausdorffDistance(coarse, fine) { Symmetric = false, Tolerance = tol };
            var patch = new MeshHausdorffDistance(coarse, fine) { Symmetric = false, Tolerance = tol, PatchBound = true };
            Assert.True(plain.Compute() && plain.Converged);
            Assert.True(patch.Compute() && patch.Converged);
            // true distance about 0.003 (the coarse plane at 0.002 over the dips to -0.001)
            Assert.True(patch.LowerBound <= plain.UpperBound + 1e-12 && plain.LowerBound <= patch.UpperBound + 1e-12,
                $"{plain.LowerBound}-{plain.UpperBound} / {patch.LowerBound}-{patch.UpperBound}");
            Assert.True(patch.UpperBound - patch.LowerBound <= tol);
            Assert.True(patch.Subdivisions * 10 < plain.Subdivisions, $"subdivisions {patch.Subdivisions} / {plain.Subdivisions}");
            // the bound itself is off by default
            Assert.False(new MeshHausdorffDistance(coarse, fine).PatchBound);
            Assert.False(new TriangleDeviationBounds(fine).PatchBound);
        }

        [Fact]
        public void Hausdorff_WithPatchBound_Repeatable_AlsoConcurrent()
        {
            // the bounds run in parallel inside MeshHausdorffDistance; the patch bound keeps no state between calls,
            // so repeated and concurrent runs (several distances at once, sharing the meshes) give the same result
            DMesh3 fine = Grid(0, 0, 1, 1, 61, h: (x, y) => 0.002 * Math.Sin(11 * x) * Math.Cos(9 * y));
            DMesh3 coarse = Grid(0, 0, 1, 1, 7, h: (x, y) => 0.001);
            var tree = new DMeshAABBTree3(fine, true);
            Func<double[]> run = () => {
                var h = new MeshHausdorffDistance(coarse, fine) { Symmetric = false, SpatialB = tree, Tolerance = 1e-5, PatchBound = true };
                h.Compute();
                return new[] { h.LowerBound, h.UpperBound, h.Subdivisions, h.WorstPoint.x, h.WorstPoint.y, h.WorstPoint.z };
            };
            double[] first = run();
            Assert.Equal(first, run());
            var results = new double[4][];
            System.Threading.Tasks.Parallel.For(0, 4, i => results[i] = run());
            foreach (double[] r in results)
                Assert.Equal(first, r);
        }

        // alpha wrap of a box (large flat faces): many small triangles where the simplification makes a few large ones
        static void BoxWrap(out DMesh3 wrap, out double offset)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), NoSharedVertices = false }.Generate().MakeDMesh();
            var w = new MeshAlphaWrap(box) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute());
            wrap = w.Result; offset = w.UsedOffset;
        }

        [Fact]
        public void SimplifyWithin_AlphaWrap_CertifiedWithFewSubdivisions()
        {
            DMesh3 wrap; double offset;
            BoxWrap(out wrap, out offset);
            int sub;
            DMesh3 simp = MeshRobustRepair.SimplifyWithin(wrap, 0.5 * offset, 0.5 * offset, true, () => false, out sub);
            Assert.NotNull(simp);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(simp));
            Assert.True(simp.TriangleCount < wrap.TriangleCount / 4, $"{simp.TriangleCount} of {wrap.TriangleCount}");
            // the certification needs few subdivisions (535 of 7424; without the patch bound 129644, 17 per wrap triangle)
            Assert.True(sub < wrap.TriangleCount / 4, $"{sub} subdivisions, {wrap.TriangleCount} wrap triangles");
            // and holds: checked with the other bounds alone
            var hd = new MeshHausdorffDistance(simp, wrap) { Symmetric = false, Tolerance = 0.01 * offset };
            Assert.True(hd.Compute() && hd.Converged);
            Assert.True(hd.UpperBound <= 0.5 * offset, $"hausdorff {hd.UpperBound}, offset {offset}");
            // the same again (no state kept between calls), the wrap not changed
            int sub2;
            DMesh3 again = MeshRobustRepair.SimplifyWithin(wrap, 0.5 * offset, 0.5 * offset, true, () => false, out sub2);
            Assert.Equal(sub, sub2);
            Assert.Equal("", Invariance.Difference(simp, again));
        }

        [Fact]
        public void PatchBound_InvariantUnderSimilarities()
        {
            DMesh3 fine = Grid(0, 0, 1, 1, 31, h: (x, y) => 0.003 * Math.Sin(9 * x) * Math.Cos(7 * y));
            var rand = new Random(3);
            var tris = new List<Vector3d[]>();
            for (int k = 0; k < 40; ++k) {
                Vector3d a = new Vector3d(0.1 + 0.4 * rand.NextDouble(), 0.1 + 0.4 * rand.NextDouble(), 0.004 * rand.NextDouble());
                tris.Add(new[] { a, a + new Vector3d(0.3 * rand.NextDouble(), 0.05, 0.002), a + new Vector3d(0.05, 0.3 * rand.NextDouble(), -0.002) });
            }
            Func<DMesh3, Func<Vector3d, Vector3d>, double[]> bounds = (target, f) => {
                var tree = new DMeshAABBTree3(target, true);
                var b = new TriangleDeviationBounds(target);
                return tris.Select(t => Patch(b, tree, f(t[0]), f(t[1]), f(t[2]))).ToArray();
            };
            double[] b0 = bounds(fine, p => p);
            Assert.True(b0.Count(double.IsInfinity) < 10, "covered: " + b0.Count(x => !double.IsInfinity(x)));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 t = new DMesh3(fine);
                MeshTransforms.PerVertexTransform(t, s.Apply);
                double[] bt = bounds(t, s.Apply);
                for (int k = 0; k < b0.Length; ++k) {
                    if (s.IsExact)
                        Assert.Equal(s.ApplyLength(b0[k]), bt[k]);       // bit for bit, scaled
                    else if (double.IsInfinity(b0[k]) == false && double.IsInfinity(bt[k]) == false)
                        Assert.True(Math.Abs(bt[k] / s.Scale - b0[k]) <= 1e-6 * 0.004 + 1e3 * s.Resolution(1), $"{s.Name} #{k}: {bt[k] / s.Scale} vs {b0[k]}");
                }
                // (rounding near the margins may decide a few differently)
                Assert.True(Math.Abs(bt.Count(double.IsInfinity) - b0.Count(double.IsInfinity)) <= 2, s.Name);
            }
        }

        // ---- PatchTouchingEdges: boundary edges of the patch that touch the triangle from outside (point 4 of patch_bound) ----

        static double Touching(DMesh3 target, DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c, bool touching = true)
        {
            return Patch(new TriangleDeviationBounds(target) { PatchTouchingEdges = touching, MaxPatchTriangles = 4096 }, tree, a, b, c);
        }

        // finite with the option only, never below the sampled distance, and at most slack above it. (a, b, c) is oriented as the
        // target; the reversed triangle sees the target from below and gets no bound, as without the option
        static void AssertTouching(DMesh3 target, DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c, double slack, string what)
        {
            Assert.True(double.IsPositiveInfinity(Touching(target, tree, a, b, c, false)), what + ": finite without the option");
            double u = Touching(target, tree, a, b, c), s = SampledMax(target, tree, a, b, c);
            // (2e-8: a point on the target has a sampled distance of up to about 1e-8, the square root of the rounding of its square)
            Assert.True(u >= s - 2e-8 && u <= s + slack, $"{what}: bound {u}, sampled {s}");
            Assert.True(double.IsPositiveInfinity(Touching(target, tree, a, c, b)), what + ": reversed");
        }

        [Fact]
        public void TouchingEdges_AlongBoundaryAndCrease_FiniteAndSound()
        {
            DMesh3 plate = Grid(0, 0, 1, 1, 21);
            var tree = new DMeshAABBTree3(plate, true);
            foreach (double h in new[] { 0, 0.01 }) {
                // an edge on the plate's boundary, a corner on it, the whole plate's half (two edges on it, both corners)
                AssertTouching(plate, tree, new Vector3d(0.1, 0, h), new Vector3d(0.9, 0, h), new Vector3d(0.5, 0.6, h), 1e-7, "edge on the boundary");
                AssertTouching(plate, tree, new Vector3d(0.5, 0, h), new Vector3d(0.9, 0.5, h), new Vector3d(0.2, 0.7, h), 1e-7, "corner on the boundary");
                AssertTouching(plate, tree, new Vector3d(0, 0, h), new Vector3d(1, 0, h), new Vector3d(1, 1, h), 1e-7, "half of the plate");
            }
            // tilted against the plate, the low edge on the boundary
            AssertTouching(plate, tree, new Vector3d(0.1, 0, 0), new Vector3d(0.9, 0, 0), new Vector3d(0.5, 0.6, 0.02), 2e-5, "tilted");
            // a cube: a face's half, its edges on the creases (the neighbouring faces are seen edge on and left out)
            DMesh3 cube = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
            foreach (int v in cube.VertexIndices()) {
                Vector3d p = cube.GetVertex(v);
                cube.SetVertex(v, p / Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            }
            var cubeTree = new DMeshAABBTree3(cube, true);
            AssertTouching(cube, cubeTree, new Vector3d(-1, -1, 1), new Vector3d(1, -1, 1), new Vector3d(1, 1, 1), 1e-7, "half of a face");
            AssertTouching(cube, cubeTree, new Vector3d(1, -1, -1), new Vector3d(1, 1, -1), new Vector3d(1, 0.25, 0.5), 1e-7, "an edge on a crease");
            // a hole next to the triangle: one of its edges on the triangle's edge, the hole outside
            DMesh3 hole = Grid(0, 0, 1, 1, 21, skip: (i, j) => i == 10 && j == 10);
            var holeTree = new DMeshAABBTree3(hole, true);
            AssertTouching(hole, holeTree, new Vector3d(0.2, 0.5, 0), new Vector3d(0.5, 0.1, 0), new Vector3d(0.8, 0.5, 0), 1e-7, "a hole outside");
        }

        [Fact]
        public void TouchingEdges_RandomTrianglesOnTheBoundary_Sound()
        {
            DMesh3 plate = Grid(0, 0, 1, 1, 21, h: (x, y) => 0.002 * Math.Sin(9 * x) * Math.Cos(7 * y) * y);
            var tree = new DMeshAABBTree3(plate, true);
            var bounds = new TriangleDeviationBounds(plate) { PatchTouchingEdges = true, MaxPatchTriangles = 4096 };
            var rand = new Random(17);
            int finite = 0;
            for (int k = 0; k < 300; ++k) {
                // two corners on the boundary y = 0 (height 0 there), or one, or a corner a little outside or inside of it
                double off = (k % 3 == 2) ? 1e-3 * (rand.NextDouble() - 0.5) : 0;
                Vector3d a = new Vector3d(rand.NextDouble(), off, 0);
                Vector3d b = (k % 3 == 0) ? new Vector3d(rand.NextDouble(), 0, 0) : new Vector3d(rand.NextDouble(), 0.6 * rand.NextDouble(), 0.01 * (rand.NextDouble() - 0.5));
                Vector3d c = new Vector3d(rand.NextDouble(), 0.05 + 0.6 * rand.NextDouble(), 0.01 * (rand.NextDouble() - 0.5));
                foreach (bool swap in new[] { false, true }) {
                    double u = swap ? Patch(bounds, tree, a, c, b) : Patch(bounds, tree, a, b, c);
                    if (double.IsPositiveInfinity(u)) continue;
                    finite++;
                    double s = SampledMax(plate, tree, a, b, c);
                    Assert.True(u >= s - 1e-12, $"#{k}: bound {u} below the sampled max {s}; {a} {b} {c}");
                }
            }
            // (the reversed triangles get none)
            Assert.True(finite > 100, "finite: " + finite);
        }

        [Fact]
        public void TouchingEdges_UncoveredParts_GiveNoBound()
        {
            DMesh3 plate = Grid(0, 0, 1, 1, 21);
            var tree = new DMeshAABBTree3(plate, true);
            // across the boundary: a corner outside by much, by a little (1e-6 of its size, above the margin of 1e-9)
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, new Vector3d(0.1, 0.2, 0), new Vector3d(0.9, 0.2, 0), new Vector3d(0.5, -0.3, 0))));
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, new Vector3d(0.1, 0.2, 0), new Vector3d(0.9, 0.2, 0), new Vector3d(0.5, -1e-6, 0))));
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, new Vector3d(0.1, -1e-6, 0), new Vector3d(0.9, -1e-6, 0), new Vector3d(0.5, 0.6, 0))));
            // an edge on the boundary, the triangle on the outside: every boundary edge only touches it, but nothing is under it
            Vector3d o0 = new Vector3d(0.1, 0, 0), o1 = new Vector3d(0.5, -0.6, 0), o2 = new Vector3d(0.9, 0, 0);
            Assert.True(SampledMax(plate, tree, o0, o1, o2) > 0.5);
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, o0, o1, o2)));
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, o0, o2, o1)));
            // a hole inside the triangle with one edge on the triangle's edge: its other edges are inside
            DMesh3 hole = Grid(0, 0, 1, 1, 21, skip: (i, j) => i == 10 && j == 10);
            var holeTree = new DMeshAABBTree3(hole, true);
            Vector3d h0 = new Vector3d(0.2, 0.5, 0), h1 = new Vector3d(0.8, 0.5, 0), h2 = new Vector3d(0.5, 0.9, 0);
            Assert.True(SampledMax(hole, holeTree, h0, h1, h2) > 0.01);
            Assert.True(double.IsPositiveInfinity(Touching(hole, holeTree, h0, h1, h2)));
            // the hole reaches into the triangle by d: by less than half the margin (3e-10 here) the bound is finite and must
            // cover the strip over the hole (the term 2 margin shape of point 4); by more there is no bound
            Vector3d s0 = new Vector3d(0.2, 0.55 - 1e-10, 0), s1 = new Vector3d(0.8, 0.55 - 1e-10, 0);
            double us = Touching(hole, holeTree, s0, s1, h2);
            Assert.True(us >= 1e-10 && us < 1e-8, "bound over the strip: " + us);
            Assert.True(double.IsPositiveInfinity(Touching(hole, holeTree, new Vector3d(0.2, 0.55 - 4e-10, 0), new Vector3d(0.8, 0.55 - 4e-10, 0), h2)));
            // a boundary edge through a corner region: outside of no single edge line
            Assert.True(double.IsPositiveInfinity(Touching(plate, tree, new Vector3d(0.5, -0.01, 0), new Vector3d(0.9, 0.5, 0), new Vector3d(0.1, 0.5, 0))));
        }

        [Fact]
        public void TouchingEdges_InvariantUnderSimilarities()
        {
            DMesh3 plate = Grid(0, 0, 1, 1, 21);
            var tris = new List<Vector3d[]> {
                new[] { new Vector3d(0.1, 0, 0.01), new Vector3d(0.9, 0, 0.01), new Vector3d(0.5, 0.6, 0.01) },
                new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0) },
                new[] { new Vector3d(0.5, 0, 0.002), new Vector3d(0.9, 0.5, 0.001), new Vector3d(0.2, 0.7, 0.003) },
                new[] { new Vector3d(0.1, 0.2, 0), new Vector3d(0.9, 0.2, 0), new Vector3d(0.5, -0.3, 0) } };
            Func<DMesh3, Func<Vector3d, Vector3d>, double[]> bounds = (target, f) => {
                var tree = new DMeshAABBTree3(target, true);
                return tris.Select(t => Touching(target, tree, f(t[0]), f(t[1]), f(t[2]))).ToArray();
            };
            double[] b0 = bounds(plate, p => p);
            Assert.Equal(1, b0.Count(double.IsInfinity));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 t = new DMesh3(plate);
                MeshTransforms.PerVertexTransform(t, s.Apply);
                double[] bt = bounds(t, s.Apply);
                for (int k = 0; k < b0.Length; ++k) {
                    if (s.IsExact)
                        Assert.Equal(s.ApplyLength(b0[k]), bt[k]);       // bit for bit, scaled
                    else if (double.IsInfinity(b0[k]) == false && double.IsInfinity(bt[k]) == false)
                        Assert.True(Math.Abs(bt[k] / s.Scale - b0[k]) <= 1e-6 * 0.01 + 1e3 * s.Resolution(1), $"{s.Name} #{k}: {bt[k] / s.Scale} vs {b0[k]}");
                }
                // (the triangle across the boundary gets no bound in any frame)
                Assert.True(double.IsPositiveInfinity(bt[3]), s.Name);
            }
        }
    }
}
