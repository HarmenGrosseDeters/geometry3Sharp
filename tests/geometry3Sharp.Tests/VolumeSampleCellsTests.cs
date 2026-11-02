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
    /// The sample cells of the volume IoU check (VolumeSampleCells, MeshRepairMetrics.VolumeIoUAtLeast): the exact
    /// triangle-box test, the cover of the union of both solids, the stratified estimate, invariance, determinism,
    /// reuse and the fallback for open meshes.
    /// </summary>
    public class VolumeSampleCellsTests
    {
        readonly ITestOutputHelper output;
        public VolumeSampleCellsTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d c, double r, int n = 16)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Union(params DMesh3[] parts)
        {
            var m = new DMesh3();
            var editor = new MeshEditor(m);
            foreach (DMesh3 p in parts)
                editor.AppendMesh(p);
            return m;
        }

        static DMesh3 Reversed(DMesh3 m)
        {
            m = new DMesh3(m);
            m.ReverseOrientation(false);
            return m;
        }

        static AxisAlignedBox3d Root(DMesh3 a, DMesh3 b)
        {
            AxisAlignedBox3d root = MeshRepairMetrics.TriangleBounds(a);
            root.Contain(MeshRepairMetrics.TriangleBounds(b));
            root.Expand(0.01 * root.DiagonalLength);
            return root;
        }

        static VolumeSampleCells Cells(DMesh3 a, DMesh3 b, AxisAlignedBox3d root, bool serial = false, bool keepKnown = false,
            int budget = VolumeSampleCells.DefaultChildBudget)
        {
            var sa = MeshRepairMetrics.SolidReference.Unoriented(a);
            var sb = MeshRepairMetrics.SolidReference.Unoriented(b);
            return new VolumeSampleCells(a, sa.Tree, b, sb.Tree, root, budget, VolumeSampleCells.DefaultMaxDepth, serial, keepKnown);
        }

        // ---------------------------------------------------------------- the exact triangle-box test

        [Fact]
        public void TriangleTouchesBox_AgreesWithTheSatOnSmallIntegers()
        {
            // on small integer coordinates the floating separating axis test (WindingNumberOctree.TriangleBoxOverlap,
            // closed comparisons) computes exactly: an oracle for the exact test, including degenerate triangles and
            // every kind of contact on a coarse grid
            var rnd = new Random(5);
            int touching = 0, degenerate = 0;
            Func<double> c = () => rnd.Next(-6, 7);
            for (int n = 0; n < 40000; ++n) {
                var a = new Vector3d(c(), c(), c());
                var b = new Vector3d(c(), c(), c());
                var t = rnd.Next(4) == 0 ? a + rnd.Next(-2, 3) * (b - a) : new Vector3d(c(), c(), c());     // collinear quarter
                var min = new Vector3d(rnd.Next(-4, 3), rnd.Next(-4, 3), rnd.Next(-4, 3));
                var max = min + new Vector3d(rnd.Next(1, 4), rnd.Next(1, 4), rnd.Next(1, 4));
                var box = new AxisAlignedBox3d(min, max);
                Vector3d centre = 0.5 * (min + max), h = 0.5 * (max - min);
                bool expected = WindingNumberOctree.TriangleBoxOverlap(a - centre, b - centre, t - centre, h);
                Assert.Equal(expected, VolumeSampleCells.TriangleTouchesBox(a, b, t, box));
                if (expected) touching++;
                if ((b - a).Cross(t - a).LengthSquared == 0) degenerate++;
            }
            // (both outcomes, and degenerate triangles, are well represented)
            Assert.InRange(touching, 4000, 36000);
            Assert.True(degenerate > 5000, "degenerate " + degenerate);
        }

        [Fact]
        public void TriangleTouchesBox_ExactAtContacts()
        {
            var box = new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One);
            double up = 1 + MathUtil.Epsilon;     // the next double above 1 (Epsilon = 2^-52)
            Func<Vector3d, Vector3d, Vector3d, bool> t = (a, b, c) => VolumeSampleCells.TriangleTouchesBox(a, b, c, box);
            // in the plane of a face: touches; one ulp beyond: does not
            Assert.True(t(new Vector3d(0.2, 0.2, 1), new Vector3d(0.8, 0.2, 1), new Vector3d(0.5, 0.8, 1)));
            Assert.False(t(new Vector3d(0.2, 0.2, up), new Vector3d(0.8, 0.2, up), new Vector3d(0.5, 0.8, up)));
            // only a vertex at a corner
            Assert.True(t(new Vector3d(1, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 3)));
            // an edge through the box's edge (1, 1, z) from outside, and one passing a tiny bit outside (x + y = 2 + 2^-40)
            Assert.True(t(new Vector3d(2, 0, 0.5), new Vector3d(0, 2, 0.5), new Vector3d(2, 2, 0.5)));
            double e = Math.Pow(2, -40);
            Assert.False(t(new Vector3d(2, e, 0.5), new Vector3d(e, 2, 0.5), new Vector3d(2, 2, 0.5)));
            // the triangle's plane cuts off a corner: x + y + z = 3 touches at (1, 1, 1), 3 + 2^-50 (a large triangle) not;
            // the coordinate and edge axes do not separate it
            Assert.True(t(new Vector3d(3, 0, 0), new Vector3d(0, 3, 0), new Vector3d(0, 0, 3)));
            double f = 3 + Math.Pow(2, -50);
            Assert.False(t(new Vector3d(f, 0, 0), new Vector3d(0, f, 0), new Vector3d(0, 0, f)));
            // degenerate: a segment through the box, a point inside, a point outside, a segment past a corner
            Assert.True(t(new Vector3d(-1, 0.5, 0.5), new Vector3d(2, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)));
            Assert.True(t(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)));
            Assert.False(t(new Vector3d(up, 0.5, 0.5), new Vector3d(up, 0.5, 0.5), new Vector3d(up, 0.5, 0.5)));
            Assert.False(t(new Vector3d(2 + e, 0, 0.5), new Vector3d(0, 2 + e, 0.5), new Vector3d(1 + e / 2, 1 + e / 2, 0.5)));
            // far from the origin (UTM), the same contact and miss, one ulp of 2^20 apart
            var off = new Vector3d(1 << 20, 1 << 20, 0);
            var far = new AxisAlignedBox3d(off, off + Vector3d.One);
            double ulp = Math.Pow(2, -32);
            Assert.True(VolumeSampleCells.TriangleTouchesBox(off + new Vector3d(1, 0.2, 0.2), off + new Vector3d(1, 0.8, 0.2), off + new Vector3d(1, 0.5, 0.8), far));
            Assert.False(VolumeSampleCells.TriangleTouchesBox(off + new Vector3d(1 + ulp, 0.2, 0.2), off + new Vector3d(1 + ulp, 0.8, 0.2),
                off + new Vector3d(1 + ulp, 0.5, 0.8), far));
        }

        [Fact]
        public void ExactWinding_AgreesWithTheFastWindingNumber()
        {
            // nested shells, a box with a face in the planes of axis rays, and touching boxes: the exact winding number at
            // random points away from the surfaces equals the rounded fast winding number
            var meshes = new[] {
                Union(Sphere(Vector3d.Zero, 2), Reversed(Sphere(Vector3d.Zero, 1)), Sphere(Vector3d.Zero, 0.5)),
                Union(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.25, 0.25, 0.25), new Vector3d(0.75, 0.75, 0.75))),
                Union(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1))),
            };
            var rnd = new Random(9);
            foreach (DMesh3 m in meshes) {
                var tree = new DMeshAABBTree3(m, true);
                AxisAlignedBox3d root = MeshRepairMetrics.TriangleBounds(m);
                root.Expand(0.01 * root.DiagonalLength);
                var seen = new HashSet<int>();
                for (int n = 0; n < 2000; ++n) {
                    Vector3d p = root.Min + new Vector3d(rnd.NextDouble() * root.Width, rnd.NextDouble() * root.Height, rnd.NextDouble() * root.Depth);
                    double fw = tree.FastWindingNumber(p);
                    if (Math.Abs(fw - Math.Round(fw)) > 0.1) continue;      // near the surface
                    int w;
                    Assert.True(VolumeSampleCells.ExactWinding(m, tree, p, root, out w));
                    Assert.Equal((int)Math.Round(fw), w);
                    seen.Add(w);
                }
                Assert.True(seen.Count >= 2);
            }
            // on the grid: points in the planes of faces and on the lines of edges (degenerate rays are retried)
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            var bt = new DMeshAABBTree3(box, true);
            var rb = new AxisAlignedBox3d(-Vector3d.One, 2 * Vector3d.One);
            int wi;
            Assert.True(VolumeSampleCells.ExactWinding(box, bt, new Vector3d(0.5, 0.5, 0.5), rb, out wi));
            Assert.Equal(1, wi);
            Assert.True(VolumeSampleCells.ExactWinding(box, bt, new Vector3d(1.5, 0.5, 1), rb, out wi));
            Assert.Equal(0, wi);
            Assert.True(VolumeSampleCells.ExactWinding(Reversed(box), new DMeshAABBTree3(Reversed(box), true), new Vector3d(0.5, 0.5, 0.5), rb, out wi));
            Assert.Equal(-1, wi);
        }

        [Fact]
        public void ShareInterval_HoldsEveryRatioOfTheTwoIntervals()
        {
            // the interval of p = (Ko + S u q) / (K + S u) is that of all (u, q) in the Wilson intervals of the union
            // share and the only-one share, each at BonferroniZ(4): checked against a dense scan, with p rising and
            // falling in u (q K above and below Ko), and without known volume (only q's interval, at z)
            double zEach = MeshRepairMetrics.BonferroniZ(4);
            foreach (var c in new[] { new { K = 0.8, Ko = 0.01, S = 0.4 }, new { K = 0.8, Ko = 0.5, S = 0.4 }, new { K = 0.0, Ko = 0.0, S = 1.0 } }) {
                int done = 3125, union = 1400, onlyOne = 40;
                double pLow, pHigh;
                MeshRepairMetrics.share_interval(done, union, onlyOne, c.K, c.Ko, c.S, 4, out pLow, out pHigh);
                double uLow, uHigh, qLow, qHigh;
                MeshRepairMetrics.wilson(union, done, zEach, out uLow, out uHigh);
                MeshRepairMetrics.wilson(onlyOne, union, c.K > 0 ? zEach : 4, out qLow, out qHigh);
                if (c.K == 0) {
                    Assert.Equal(qLow, pLow);
                    Assert.Equal(qHigh, pHigh);
                    continue;
                }
                double min = double.MaxValue, max = double.MinValue;
                for (int i = 0; i <= 50; ++i)
                    for (int j = 0; j <= 50; ++j) {
                        double u = uLow + (uHigh - uLow) * i / 50, q = qLow + (qHigh - qLow) * j / 50;
                        double p = (c.Ko + c.S * u * q) / (c.K + c.S * u);
                        Assert.InRange(p, pLow - 1e-15, pHigh + 1e-15);
                        min = Math.Min(min, p); max = Math.Max(max, p);
                    }
                Assert.Equal(min, pLow, 1e-14);
                Assert.Equal(max, pHigh, 1e-14);
            }
        }

        [Fact]
        public void BonferroniZ_SplitsTheTail()
        {
            // two intervals at z' miss together at most as often as one at z: z' = 4.17 for z = 4, 2.24 for 1.96
            Assert.InRange(MeshRepairMetrics.BonferroniZ(4), 4.16, 4.18);
            Assert.InRange(MeshRepairMetrics.BonferroniZ(1.959964), 2.240, 2.242);
        }

        // ---------------------------------------------------------------- the region covers the union

        public static IEnumerable<object[]> CoverCases()
        {
            foreach (string name in new[] { "nested", "touching", "thin shell", "coplanar", "on grid faces", "disjoint" })
                yield return new object[] { name };
        }

        // the two solids of a case, and its root box (the one of VolumeIoUAtLeast, or one with the faces on grid planes)
        static void Case(string name, out DMesh3 a, out DMesh3 b, out AxisAlignedBox3d root)
        {
            var one = Vector3d.One;
            switch (name) {
                case "nested":       // a shell (a sphere with a cavity) and a sphere through the cavity
                    a = Union(Sphere(Vector3d.Zero, 2), Reversed(Sphere(Vector3d.Zero, 1)));
                    b = Sphere(new Vector3d(0.4, 0, 0), 1.5);
                    break;
                case "touching":     // two boxes touching along a face, and the box of both
                    a = Union(Box(Vector3d.Zero, one), Box(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1)));
                    b = Box(Vector3d.Zero, new Vector3d(2, 1, 1));
                    break;
                case "thin shell":
                    a = Box(Vector3d.Zero, new Vector3d(1, 1, 1e-3));
                    b = Box(Vector3d.Zero, new Vector3d(1, 1, 1.1e-3));
                    break;
                case "coplanar":     // faces in the same planes, and a box that ends inside the other
                    a = Box(Vector3d.Zero, one);
                    b = Box(new Vector3d(0, 0, 0), new Vector3d(1, 0.5, 0.75));
                    break;
                case "on grid faces":
                    a = Box(one, 3 * one);
                    b = Box(new Vector3d(2, 1, 1), new Vector3d(3, 3, 3));
                    root = new AxisAlignedBox3d(Vector3d.Zero, 4 * one);
                    return;
                default:             // disjoint components far apart
                    a = Union(Sphere(Vector3d.Zero, 1), Sphere(new Vector3d(40, 0, 0), 1));
                    b = Sphere(new Vector3d(40.1, 0, 0), 1);
                    break;
            }
            root = Root(a, b);
        }

        [Theory]
        [MemberData(nameof(CoverCases))]
        public void Cells_CoverTheUnion(string name)
        {
            DMesh3 a, b;
            AxisAlignedBox3d root;
            Case(name, out a, out b, out root);
            Assert.True(ExactMeshBoolean.IsWindingClosed(a) && ExactMeshBoolean.IsWindingClosed(b));
            Func<Vector3d, bool> inA = MeshRepairMetrics.SolidInside(a), inB = MeshRepairMetrics.SolidInside(b);
            Func<Vector3d, int> cls = (p) => (inA(p) ? 1 : 0) | (inB(p) ? 2 : 0);
            var cells = Cells(a, b, root, keepKnown: true);
            output.WriteLine($"{name}: known {cells.Known.Count}, sampled {cells.Sampled.Count}, depth {cells.Depth}, " +
                $"known union {cells.KnownUnion:G4}, sampled {cells.SampledVolume:G4}");
            Assert.True(cells.Sampled.Count > 0);
            // (thin and far apart solids have no cell inside both at the cell size the budget allows)
            if (name != "thin shell" && name != "disjoint")
                Assert.Contains(cells.Known, k => k.Value == 3);
            Assert.Equal(0, cells.Unclassified);
            // known and sampled cells tile the root box
            double vol = cells.Known.Sum(k => k.Key.Volume) + cells.SampledVolume;
            Assert.Equal(root.Volume, vol, 1e-12 * root.Volume);
            Assert.Equal(cells.KnownBoth + cells.KnownOnlyA + cells.KnownOnlyB, cells.Known.Where(k => k.Value != 0).Sum(k => k.Key.Volume), 1e-12 * root.Volume);
            // every point of a classified cell, on its faces and corners too, is in the solids of its class
            var rnd = new Random(3);
            foreach (var k in cells.Known) {
                AxisAlignedBox3d bx = k.Key;
                var pts = new List<Vector3d>() { bx.Center };
                for (int n = 0; n < 8; ++n)
                    pts.Add(new Vector3d((n & 1) == 0 ? bx.Min.x : bx.Max.x, (n & 2) == 0 ? bx.Min.y : bx.Max.y, (n & 4) == 0 ? bx.Min.z : bx.Max.z));
                for (int n = 0; n < 2; ++n)
                    pts.Add(bx.Min + new Vector3d(rnd.NextDouble() * bx.Width, rnd.NextDouble() * bx.Height, rnd.NextDouble() * bx.Depth));
                foreach (Vector3d p in pts)
                    Assert.True(cls(p) == k.Value, $"{name}: cell {bx} of class {k.Value}, point {p} is {cls(p)}");
            }
            // points of the union are in a sampled cell, or in a known cell of their class (which follows from the
            // above and the tiling, checked directly on random points)
            for (int n = 0; n < 3000; ++n) {
                Vector3d p = root.Min + new Vector3d(rnd.NextDouble() * root.Width, rnd.NextDouble() * root.Height, rnd.NextDouble() * root.Depth);
                int c = cls(p);
                if (c == 0) continue;
                bool covered = cells.Sampled.Any(s => s.Contains(p)) || cells.Known.Any(k => k.Value == c && k.Key.Contains(p));
                Assert.True(covered, $"{name}: {p} of class {c} not covered");
            }
        }

        [Fact]
        public void Cells_ExactlyOnFaces_AreSampledNotClassified()
        {
            // root [0,4]^3, box [1,3]^3: its faces lie on the planes of the grid from level 1 on. A cell with a face in
            // such a plane touches the box (closed): sampled at the last level, never classified as inside or outside
            DMesh3 a = Box(Vector3d.One, 3 * Vector3d.One);
            var root = new AxisAlignedBox3d(Vector3d.Zero, 4 * Vector3d.One);
            var cells = Cells(a, a, root, keepKnown: true);
            foreach (var k in cells.Known) {
                AxisAlignedBox3d bx = k.Key;
                // inside cells lie in the open box, outside cells do not meet the closed box
                if (k.Value == 3)
                    Assert.True(bx.Min.x > 1 && bx.Min.y > 1 && bx.Min.z > 1 && bx.Max.x < 3 && bx.Max.y < 3 && bx.Max.z < 3, $"{bx}");
                else {
                    Assert.Equal(0, k.Value);
                    Assert.True(bx.Max.x < 1 || bx.Max.y < 1 || bx.Max.z < 1 || bx.Min.x > 3 || bx.Min.y > 3 || bx.Min.z > 3, $"{bx}");
                }
            }
            // the sampled cells hold the surface: their volume is the shell of 2 cells' width at the last level
            double h = 4 / Math.Pow(2, cells.Depth);
            Assert.Equal(Math.Pow(2 + 2 * h, 3) - Math.Pow(2 - 2 * h, 3), cells.SampledVolume, 1e-9);
            Assert.Equal(Math.Pow(2 - 2 * h, 3), cells.KnownBoth, 1e-9);
        }

        // ---------------------------------------------------------------- the estimate

        static bool AtLeast(DMesh3 input, DMesh3 result, out double iou, MeshRepairMetrics.VolumeCheckStats stats = null, bool cells = true,
            int seed = 1, double minIoU = 0.99, int samples = 100000)
        {
            return MeshRepairMetrics.VolumeIoUAtLeast(new MeshRepairMetrics.SolidReference(input), MeshRepairMetrics.SolidReference.Unoriented(result),
                minIoU, out iou, samples, seed, 4, stats, cells);
        }

        [Fact]
        public void Estimate_KnownVolumeIsExact_AndTheIntervalIsNarrower()
        {
            // a box against the same box with its top at h: IoU = h. The cells are used, the interior counts with its
            // exact volume, and the answer needs fewer samples, or with all of them (close to the threshold) gives a
            // narrower interval
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            foreach (double h in new[] { 0.989, 0.9895, 0.9905, 0.991 }) {
                DMesh3 result = Box(Vector3d.Zero, new Vector3d(1, 1, h));
                var with = new MeshRepairMetrics.VolumeCheckStats();
                var without = new MeshRepairMetrics.VolumeCheckStats();
                double iouWith, iouWithout;
                bool okWith = AtLeast(input, result, out iouWith, with);
                bool okWithout = AtLeast(input, result, out iouWithout, without, false);
                output.WriteLine($"h {h}: with {iouWith} [{with.IoULow}, {with.IoUHigh}] {with.Samples}; without {iouWithout} [{without.IoULow}, {without.IoUHigh}] {without.Samples}");
                Assert.True(with.Cells);
                Assert.False(without.Cells);
                Assert.Equal(h > 0.99, okWith);
                Assert.InRange(h, with.IoULow, with.IoUHigh);
                Assert.True(with.Samples <= without.Samples);
                if (with.Samples == without.Samples)
                    Assert.True(with.IoUHigh - with.IoULow < 0.75 * (without.IoUHigh - without.IoULow));
                // the known part: the cells inside both lie in [0,1]^2 x [0,h], with most of the volume
                Assert.InRange(with.KnownBoth, 0.6 * h, h);
                Assert.Equal(with.KnownBoth, with.KnownUnion);
            }
        }

        [Fact]
        public void Estimate_ClearCases_StopEarlierWithCells()
        {
            // the same solid, a slightly and a clearly different one: the cells give the same answers with at most as many
            // samples (a single sphere fills about half of its box, so both hit the union about equally often; the cells
            // gain from the known interior), and the slightly different one, which the region decides only after several
            // rounds, needs fewer
            DMesh3 input = Sphere(Vector3d.Zero, 1, 24);
            foreach (double shift in new[] { 0, 0.003, 0.2 }) {
                DMesh3 r = Sphere(new Vector3d(shift, 0, 0), 1, 24);
                var with = new MeshRepairMetrics.VolumeCheckStats();
                var without = new MeshRepairMetrics.VolumeCheckStats();
                double i1, i2;
                bool o1 = AtLeast(input, r, out i1, with), o2 = AtLeast(input, r, out i2, without, false);
                output.WriteLine($"shift {shift}: {with.Samples} ({with.UnionSamples}) vs {without.Samples} ({without.UnionSamples})");
                Assert.Equal(o2, o1);
                Assert.True(with.Cells);
                Assert.True(with.Samples <= without.Samples);
                if (shift == 0.003)
                    Assert.True(with.Samples < without.Samples, $"{with.Samples} vs {without.Samples}");
            }
        }

        [Fact]
        public void Estimate_FarSmallComponent_FallsBackToTheRegion()
        {
            // a small component far away (review): the root box is long and thin, the cells of the last level are large,
            // and they would sample far more volume than the region of the components' boxes; the region is used then
            var far = new Vector3d(1e6, 0, 0);
            DMesh3 input = Union(Sphere(Vector3d.Zero, 1), Sphere(far, 0.05));
            DMesh3 result = Union(Sphere(new Vector3d(0.02, 0, 0), 1), Sphere(far, 0.05));     // IoU about 0.97
            var with = new MeshRepairMetrics.VolumeCheckStats();
            var without = new MeshRepairMetrics.VolumeCheckStats();
            double i1, i2;
            bool ok = AtLeast(input, result, out i1, with);
            AtLeast(input, result, out i2, without, false);
            Assert.True(with.CellsRejected);
            Assert.False(with.Cells);
            Assert.False(ok);
            Assert.True(with.UnionSamples >= without.UnionSamples / 2, $"{with.UnionSamples} vs {without.UnionSamples}");
            Assert.InRange(i1, 0.95, 0.985);
        }

        [Fact]
        public void SampleRegion_Volume_IsThatOfTheUnion()
        {
            var boxes = new List<AxisAlignedBox3d>() {
                new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)),
                new AxisAlignedBox3d(Vector3d.One, new Vector3d(3, 3, 3)),         // overlaps the first in a unit cube
                new AxisAlignedBox3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 1, 1)),     // inside the first
                new AxisAlignedBox3d(new Vector3d(10, 0, 0), new Vector3d(11, 1, 2)) };
            Assert.Equal(8 + 8 - 1 + 2, new MeshRepairMetrics.SampleRegion(boxes).Volume(), 12);
            Assert.Equal(8, new MeshRepairMetrics.SampleRegion(boxes[0]).Volume());
        }

        [Fact]
        public void Estimate_EarlyAnswers_AndIntervalsHoldTheTrueIoU()
        {
            // the combined interval of the stop rule (known volume, two Wilson intervals, corners) over many seeds: no early
            // answer on the wrong side, and the interval at the stop misses the true IoU (h) rarely (z = 4: never expected)
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            foreach (double h in new[] { 0.97, 0.995 }) {
                DMesh3 result = Box(Vector3d.Zero, new Vector3d(1, 1, h));
                int misses = 0, early = 0;
                for (int seed = 1; seed <= 150; ++seed) {
                    var st = new MeshRepairMetrics.VolumeCheckStats();
                    double iou;
                    bool ok = AtLeast(input, result, out iou, st, seed: seed);
                    Assert.True(st.Cells && st.KnownUnion > 0);
                    Assert.Equal(h > 0.99, ok);
                    if (st.Samples < 100000) early++;
                    if (h < st.IoULow || h > st.IoUHigh) misses++;
                }
                output.WriteLine($"h {h}: early {early}, misses {misses}");
                Assert.True(early > 100);
                Assert.True(misses <= 1, "misses " + misses);
            }
        }

        [Fact]
        public void Estimate_AllKnownCases()
        {
            // results that agree with the input or lose it entirely, and an input without a solid hit only through the
            // known cells: no NaN where the cells know the input's solid
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            double iou;
            Assert.True(AtLeast(input, new DMesh3(input), out iou));
            Assert.Equal(1.0, iou);
            Assert.False(AtLeast(input, Box(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6)), out iou));
            Assert.Equal(0.0, iou);
            // half of the input: the cells known inside only the input decide; the interval at the stop holds the IoU
            foreach (double h in new[] { 0.5, 0.97 }) {
                var half = new MeshRepairMetrics.VolumeCheckStats();
                Assert.False(AtLeast(input, Box(Vector3d.Zero, new Vector3d(1, 1, h)), out iou, half));
                Assert.True(half.Cells && (h > 0.9 || half.KnownUnion > half.KnownBoth));
                Assert.InRange(h, half.IoULow, half.IoUHigh);
                Assert.InRange(iou, half.IoULow, half.IoUHigh);
            }
            // an empty result: the region path (no cells for no triangles), as before
            var st = new MeshRepairMetrics.VolumeCheckStats();
            Assert.False(AtLeast(input, new DMesh3(), out iou, st));
            Assert.False(st.Cells);
        }

        // ---------------------------------------------------------------- fallback B

        [Fact]
        public void OpenMeshes_UseTheRegion_BitIdentical()
        {
            // an open input (a box without its top) or an open result: the generalized winding number is not constant in
            // the cells; the region of sample_region is used, with the same answer and estimate as without the cells
            DMesh3 open = Box(Vector3d.Zero, Vector3d.One);
            var top = open.TriangleIndices().Where(t => open.GetTriCentroid(t).z > 0.99).ToList();
            foreach (int t in top) open.RemoveTriangle(t);
            Assert.False(ExactMeshBoolean.IsWindingClosed(open));
            DMesh3 closed = Box(Vector3d.Zero, new Vector3d(1, 1, 0.995));
            foreach (var pair in new[] { Tuple.Create(open, closed), Tuple.Create(closed, open) }) {
                var st = new MeshRepairMetrics.VolumeCheckStats();
                double iou, iouRegion;
                bool ok = AtLeast(pair.Item1, pair.Item2, out iou, st);
                bool okRegion = AtLeast(pair.Item1, pair.Item2, out iouRegion, null, false);
                Assert.False(st.Cells);
                Assert.Equal(okRegion, ok);
                Assert.Equal(BitConverter.DoubleToInt64Bits(iouRegion), BitConverter.DoubleToInt64Bits(iou));
            }
        }

        // ---------------------------------------------------------------- invariance, determinism, reuse

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Estimate_InvariantUnderSimilarity(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            DMesh3 input = Union(Sphere(Vector3d.Zero, 1), Box(new Vector3d(1.5, -0.5, -0.5), new Vector3d(2.5, 0.5, 0.5)));
            DMesh3 result = Union(Sphere(new Vector3d(0.004, 0, 0), 1), Box(new Vector3d(1.5, -0.5, -0.5), new Vector3d(2.5, 0.5, 0.5)));
            var st0 = new MeshRepairMetrics.VolumeCheckStats();
            var st1 = new MeshRepairMetrics.VolumeCheckStats();
            double iou0, iou1;
            bool ok0 = AtLeast(input, result, out iou0, st0);
            bool ok1 = AtLeast(Invariance.Transformed(input, s), Invariance.Transformed(result, s), out iou1, st1);
            Assert.True(st0.Cells && st1.Cells);
            Assert.True(ok0);
            Assert.Equal(ok0, ok1);
            if (s.IsExact) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(iou0), BitConverter.DoubleToInt64Bits(iou1));
                Assert.Equal(st0.Samples, st1.Samples);
                Assert.Equal(st0.SampledCells, st1.SampledCells);
                Assert.Equal(st0.KnownUnion * Math.Pow(s.Scale, 3), st1.KnownUnion);
            } else {
                // other cells and samples: the same estimate up to sampling noise
                Assert.InRange(iou1 - iou0, -0.003, 0.003);
            }
        }

        [Fact]
        public void Cells_ParallelEqualsSerial_AndRepeatable()
        {
            DMesh3 a, b;
            AxisAlignedBox3d root;
            Case("nested", out a, out b, out root);
            var serial = Cells(a, b, root, serial: true, keepKnown: true);
            for (int run = 0; run < 3; ++run) {
                var par = Cells(a, b, root, keepKnown: true);
                Assert.Equal(serial.Sampled, par.Sampled);
                Assert.Equal(serial.Known.Select(k => k.Key), par.Known.Select(k => k.Key));
                Assert.Equal(serial.Known.Select(k => k.Value), par.Known.Select(k => k.Value));
                Assert.Equal(serial.KnownBoth, par.KnownBoth);
                Assert.Equal(serial.KnownOnlyA, par.KnownOnlyA);
                Assert.Equal(serial.KnownOnlyB, par.KnownOnlyB);
                Assert.Equal(serial.SampledVolume, par.SampledVolume);
                Assert.Equal(serial.ChildTests, par.ChildTests);
            }
            // the estimate (parallel classification of the samples into their own slots) is repeatable
            double i0, i1;
            bool o0 = AtLeast(a, b, out i0, minIoU: 0.5), o1 = AtLeast(a, b, out i1, minIoU: 0.5);
            Assert.Equal(o0, o1);
            Assert.Equal(BitConverter.DoubleToInt64Bits(i0), BitConverter.DoubleToInt64Bits(i1));
        }

        [Fact]
        public void Estimate_ReusedInputSolid_EqualsFresh()
        {
            // the input's SolidReference (its tree and its closedness, cached) is reused for several results, as
            // MeshRobustRepair does: a result after another gives what a fresh reference gives
            DMesh3 input = Sphere(Vector3d.Zero, 1);
            DMesh3 r1 = Sphere(new Vector3d(0.004, 0, 0), 1), open = Box(Vector3d.Zero, Vector3d.One);
            foreach (int t in open.TriangleIndices().Take(2).ToList()) open.RemoveTriangle(t);
            var reused = new MeshRepairMetrics.SolidReference(input);
            foreach (DMesh3 r in new[] { r1, open, r1 }) {
                double iouReused, iouFresh;
                bool a = MeshRepairMetrics.VolumeIoUAtLeast(reused, MeshRepairMetrics.SolidReference.Unoriented(r), 0.99, out iouReused);
                bool b = AtLeast(input, r, out iouFresh);
                Assert.Equal(b, a);
                Assert.Equal(BitConverter.DoubleToInt64Bits(iouFresh), BitConverter.DoubleToInt64Bits(iouReused));
            }
        }

        // ---------------------------------------------------------------- MeshRobustRepair

        [Fact]
        public void RobustRepair_UsesTheCells_SameResult()
        {
            // two overlapping boxes (closed, self-intersecting): the volume checks of the exact stage run on the cells
            // and decide as the region does; the result is clean
            DMesh3 input = Union(Box(Vector3d.Zero, Vector3d.One), Box(new Vector3d(0.5, 0.3, 0.2), new Vector3d(1.5, 1.2, 1.4)));
            var with = new MeshRobustRepair(new DMesh3(input));
            var without = new MeshRobustRepair(new DMesh3(input)) { SampleCellsInVolumeCheck = false };
            with.Apply();
            without.Apply();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(with.Mesh, true));
            Assert.True(with.VolumeCheckStats.Count > 0);
            Assert.True(with.VolumeCheckStats.All(v => v.Cells));
            Assert.True(without.VolumeCheckStats.All(v => v.Cells == false));
            Assert.Equal(without.UsedExactResolve, with.UsedExactResolve);
            Assert.Equal(without.FallbackReason, with.FallbackReason);
            Assert.Equal("", Invariance.Difference(without.Mesh, with.Mesh));
            Assert.InRange(with.RepairVolumeIoU, 0.99, 1);
            // reuse: the same instance on another input gives what a fresh instance gives (the input's solid and the
            // statistics are not carried over)
            DMesh3 other = Union(Sphere(Vector3d.Zero, 1), Sphere(new Vector3d(1.2, 0, 0), 1));
            with.Mesh = new DMesh3(other);
            with.Apply();
            var fresh = new MeshRobustRepair(new DMesh3(other));
            fresh.Apply();
            Assert.Equal(fresh.VolumeCheckStats.Count, with.VolumeCheckStats.Count);
            Assert.Equal(BitConverter.DoubleToInt64Bits(fresh.RepairVolumeIoU), BitConverter.DoubleToInt64Bits(with.RepairVolumeIoU));
            Assert.Equal("", Invariance.Difference(fresh.Mesh, with.Mesh));
        }
    }
}
