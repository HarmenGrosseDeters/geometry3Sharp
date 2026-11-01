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
        static int CheckSound(DMesh3 target, int seed, int count, double zlo, double zhi, double size)
        {
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            var rand = new Random(seed);
            AxisAlignedBox3d box = target.CachedBounds;
            int finite = 0;
            for (int k = 0; k < count; ++k) {
                Func<Vector3d> pt = () => new Vector3d(box.Min.x + rand.NextDouble() * box.Width, box.Min.y + rand.NextDouble() * box.Height,
                    zlo + rand.NextDouble() * (zhi - zlo));
                Vector3d a = pt(), d1 = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, 0.2 * (rand.NextDouble() - 0.5)),
                    d2 = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, 0.2 * (rand.NextDouble() - 0.5));
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
            CheckSound(Grid(0, 0, 1, 1, 21, flip: (i, j) => ((i + j) & 1) == 0), 4, 200, -0.05, 0.05, 0.5);
            // a thin plate: top up, bottom down 0.01 below (not connected); query triangles on both sides and in between
            DMesh3 plate = Grid(0, 0, 1, 1, 21);
            Grid(0, 0, 1, 1, 17, h: (x, y) => -0.01, down: true, into: plate);
            Assert.True(CheckSound(plate, 5, 400, -0.06, 0.05, 0.5) > 50);
            // a fold: a ridge that overhangs (z = 0 plane plus a sheet that goes up and folds back over the plane)
            DMesh3 fold = Grid(0, 0, 1, 1, 21);
            Grid(0.2, 0.2, 0.8, 0.8, 9, h: (x, y) => 0.02 + 0.3 * x * (1 - x), down: true, into: fold);
            Grid(0.3, 0.3, 0.7, 0.7, 9, h: (x, y) => 0.03, into: fold);
            CheckSound(fold, 6, 400, -0.05, 0.12, 0.5);
            // bumpy height fields at several amplitudes: curved, positive and negative projections
            foreach (double amp in new[] { 0.002, 0.02, 0.2 }) {
                DMesh3 bumpy = Grid(0, 0, 1, 1, 31, h: (x, y) => amp * Math.Sin(9 * x) * Math.Cos(7 * y));
                CheckSound(bumpy, 7 + (int)(amp * 1000), 300, -amp - 0.03, amp + 0.03, 0.4);
            }
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
            Assert.Equal(0, Patch(new TriangleDeviationBounds(full), ftree, q0, q1, q2));
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
    }
}
