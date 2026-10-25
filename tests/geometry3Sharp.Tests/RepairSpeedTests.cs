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
    /// Speedups of MeshRobustRepair and its building blocks: the hierarchical TriangleHashGrid (Reducer
    /// with PreventSelfIntersections), the sequential volume check (MeshRepairMetrics.VolumeIoUAtLeast) and
    /// the phase timings of MeshRobustRepair. The results must stay those of the slower versions.
    /// </summary>
    public class RepairSpeedTests
    {
        static AxisAlignedBox3d TriBox(Vector3d a, Vector3d b, Vector3d c)
        {
            var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c);
            return box;
        }

        static bool Overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            for (int d = 0; d < 3; ++d)
                if (a.Min[d] > b.Max[d] || b.Min[d] > a.Max[d]) return false;
            return true;
        }

        [Fact]
        public void TriangleHashGrid_QueryFindsExactlyTheOverlappingTriangles()
        {
            var rnd = new Random(3);
            var grid = new TriangleHashGrid(Vector3d.Zero, 0.1);
            var boxes = new Dictionary<int, AxisAlignedBox3d>();
            for (int step = 0; step < 4000; ++step) {
                int tid = rnd.Next(1500);
                if (rnd.NextDouble() < 0.25) {
                    grid.Remove(tid);
                    boxes.Remove(tid);
                    continue;
                }
                // sizes from a fraction of a cell to most of the domain (several levels)
                Vector3d p = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 10;
                double s = Math.Pow(10, -2 + 3 * rnd.NextDouble());
                Vector3d q = p + s * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                var box = new AxisAlignedBox3d(p); box.Contain(q);
                grid.Insert(tid, box);
                boxes[tid] = box;
            }
            Assert.True(grid.LevelCount > 2);
            for (int k = 0; k < 300; ++k) {
                Vector3d p = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 10;
                double s = Math.Pow(10, -2 + 3 * rnd.NextDouble());
                var query = new AxisAlignedBox3d(p); query.Contain(p + s * Vector3d.One);
                var found = new HashSet<int>();
                grid.Query(query, (tid) => Assert.True(found.Add(tid)));       // each once
                foreach (var kv in boxes)
                    Assert.Equal(Overlap(kv.Value, query), found.Contains(kv.Key));
                foreach (int tid in found) {
                    AxisAlignedBox3d box;
                    Assert.True(grid.TryGetBox(tid, out box));
                    Assert.Equal(boxes[tid], box);
                }
            }
        }

        [Fact]
        public void TriangleHashGrid_LargeTrianglesAreNotVisitedByEveryQuery()
        {
            // many small triangles and a few hundred large ones (more than MaxCellsPerTriangle cells each),
            // as late in a reduction: a query far from the large ones must not visit them (the single-level
            // grid kept them in a list that every query went through)
            var grid = new TriangleHashGrid(Vector3d.Zero, 0.1);
            var rnd = new Random(5);
            for (int i = 0; i < 10000; ++i) {
                Vector3d p = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 20;
                grid.Insert(i, TriBox(p, p + new Vector3d(0.05, 0, 0), p + new Vector3d(0, 0.05, 0.02)));
            }
            const int large0 = 100000;
            for (int i = 0; i < 600; ++i) {
                Vector3d p = new Vector3d(30 + 5 * rnd.NextDouble(), 30 + 5 * rnd.NextDouble(), 30 + 5 * rnd.NextDouble());
                grid.Insert(large0 + i, TriBox(p, p + new Vector3d(2, 0, 0), p + new Vector3d(0, 2, 0.5)));
            }
            int visited = 0, visitedLarge = 0;
            var query = TriBox(new Vector3d(5, 5, 5), new Vector3d(5.1, 5, 5), new Vector3d(5, 5.1, 5.05));
            grid.Query(query, (tid) => { visited++; if (tid >= large0) visitedLarge++; });
            Assert.Equal(0, visitedLarge);
            Assert.True(visited < 50, "visited " + visited);
            // a query among the large ones finds them
            var found = new HashSet<int>();
            grid.Query(TriBox(new Vector3d(32, 32, 32), new Vector3d(32.1, 32, 32), new Vector3d(32, 32.1, 32)), (tid) => found.Add(tid));
            Assert.Contains(found, (tid) => tid >= large0);
        }

        [Fact]
        public void TriangleHashGrid_FlatAndTouchingBoxesOverlap()
        {
            // axis-aligned triangles have flat boxes; boxes that only touch count as overlapping (closed boxes)
            var grid = new TriangleHashGrid(Vector3d.Zero, 0.5);
            grid.Insert(0, TriBox(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY));
            grid.Insert(1, TriBox(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, 1, 0)));
            grid.Insert(2, TriBox(new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(0, 1, 1)));
            var found = new List<int>();
            grid.Query(TriBox(new Vector3d(0.2, 0.2, 0), new Vector3d(1, 0.2, 0), new Vector3d(0.2, 0.6, 0)), (tid) => found.Add(tid));
            found.Sort();
            Assert.Equal(new List<int>() { 0, 1 }, found);
        }

        [Fact]
        public void TriangleHashGrid_NonFiniteBoxesAreVisitedByEveryQuery()
        {
            var grid = new TriangleHashGrid(Vector3d.Zero, 1);
            grid.Insert(1, TriBox(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY));
            grid.Insert(2, new AxisAlignedBox3d(new Vector3d(double.NaN, 0, 0), 1));
            grid.Insert(3, TriBox(Vector3d.Zero, new Vector3d(1e300, 0, 0), Vector3d.AxisY));
            var found = new HashSet<int>();
            grid.Query(TriBox(new Vector3d(0.5, 0.2, 0), new Vector3d(0.6, 0.2, 0), new Vector3d(0.5, 0.3, 0)), (tid) => found.Add(tid));
            Assert.Equal(new HashSet<int>() { 1, 2, 3 }, found);
            grid.Remove(2); grid.Remove(3);
            found.Clear();
            grid.Query(TriBox(new Vector3d(0.5, 0.2, 0), new Vector3d(0.6, 0.2, 0), new Vector3d(0.5, 0.3, 0)), (tid) => found.Add(tid));
            Assert.Equal(new HashSet<int>() { 1 }, found);
        }

        [Fact]
        public void ReducerPreventSelfIntersections_SameResultAsBefore()
        {
            // the grid only finds the candidates; the exact test decides: a reduction with a grid of any cell
            // size gives the same mesh (here: a coarse sphere inside a fine one, close to it, so that the check
            // rejects collapses)
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            DMesh3 inner = new Sphere3Generator_NormalizedCube() { Radius = 0.97, EdgeVertices = 6 }.Generate().MakeDMesh();
            inner.ReverseOrientation();
            MeshEditor.Append(m, inner);
            var r = new Reducer(m) { PreventSelfIntersections = true };
            r.ReduceToTriangleCount(300);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            Assert.True(m.TriangleCount < 2000);
        }

        // random integer in [-2^bits, 2^bits]
        static BigInteger RandomInt(Random rnd, int bits)
        {
            BigInteger v = BigInteger.Zero;
            for (int k = 0; k < bits; k += 30)
                v = (v << 30) + rnd.Next(1 << 30);
            v >>= Math.Max(0, ((bits + 29) / 30) * 30 - bits);
            return rnd.Next(2) == 0 ? v : -v;
        }

        static Vector3d Approx(ExactPoint3 p)
        {
            return new Vector3d(ExactPoint3.ToDouble(p.X, p.W, 0), ExactPoint3.ToDouble(p.Y, p.W, 0), ExactPoint3.ToDouble(p.Z, p.W, 0));
        }

        // random points: integers of various sizes, a rational one (the centroid of three), and nearly
        // degenerate configurations (a point close to the plane of three others)
        static ExactPoint3[] RandomConfiguration(Random rnd)
        {
            int bits = 10 + rnd.Next(55);
            var p = new ExactPoint3[4];
            for (int k = 0; k < 3; ++k)
                p[k] = ExactPoint3.FromInteger(RandomInt(rnd, bits), RandomInt(rnd, bits), RandomInt(rnd, bits));
            int kind = rnd.Next(3);
            if (kind == 0) {
                p[3] = ExactPoint3.FromInteger(RandomInt(rnd, bits), RandomInt(rnd, bits), RandomInt(rnd, bits));
            } else {
                // q = a + s (b - a) + t (c - a) with rational s, t: on the plane, then moved by a tiny offset
                BigInteger den = 1 + BigInteger.Abs(RandomInt(rnd, 20));
                BigInteger s = RandomInt(rnd, 20), t = RandomInt(rnd, 20);
                BigInteger[] q = new BigInteger[3];
                for (int k = 0; k < 3; ++k) {
                    BigInteger a = p[0][k], b = p[1][k], c = p[2][k];
                    q[k] = a * den + s * (b - a) + t * (c - a);
                }
                if (kind == 2)
                    q[rnd.Next(3)] += rnd.Next(-2, 3);
                p[3] = ExactPoint3.Create(q[0], q[1], q[2], den);
            }
            // shuffle, so that the rational point is in any position
            for (int k = 3; k > 0; --k) {
                int j = rnd.Next(k + 1);
                var tmp = p[k]; p[k] = p[j]; p[j] = tmp;
            }
            return p;
        }

        [Fact]
        public void ExactBooleanOrientFilter_AgreesWithTheExactSign()
        {
            var rnd = new Random(11);
            int decided = 0, n = 20000;
            for (int iter = 0; iter < n; ++iter) {
                ExactPoint3[] p = RandomConfiguration(rnd);
                var m = new BigInteger[4, 4];
                for (int k = 0; k < 4; ++k) {
                    m[k, 0] = p[k].X; m[k, 1] = p[k].Y; m[k, 2] = p[k].Z; m[k, 3] = p[k].W;
                }
                int exact = ExactMeshArrangement.Det4(m).Sign;
                Vector3d a = Approx(p[0]), b = Approx(p[1]), c = Approx(p[2]), d = Approx(p[3]);
                int f = ExactMeshBoolean.orient3d_filtered(ref a, ref b, ref c, ref d);
                if (f != 2) {
                    Assert.Equal(exact, f);
                    decided++;
                }
            }
            // the generic third of the cases is decided by the filter
            Assert.True(decided > n / 4, "decided " + decided);
        }

        [Fact]
        public void ExactBooleanPlaneSideFilter_AgreesWithTheExactSign()
        {
            var rnd = new Random(12);
            int decided = 0, n = 20000;
            for (int iter = 0; iter < n; ++iter) {
                ExactPoint3[] p = RandomConfiguration(rnd);
                ExactPoint3 a = p[0], b = p[1], c = p[2], q = p[3];
                if (a.W.IsOne == false || b.W.IsOne == false || c.W.IsOne == false)
                    continue;       // the planes are through input (integer) points
                BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                BigInteger A = uy * vz - uz * vy, B = uz * vx - ux * vz, C = ux * vy - uy * vx, D = A * a.X + B * a.Y + C * a.Z;
                int exact = (A * q.X + B * q.Y + C * q.Z - D * q.W).Sign;
                double[] pa = { (double)A, (double)B, (double)C, (double)D };
                Vector3d qd = Approx(q);
                int f = ExactMeshBoolean.plane_side_filtered(pa, qd.x, qd.y, qd.z);
                if (f != 2) {
                    Assert.Equal(exact, f);
                    decided++;
                }
            }
            Assert.True(decided > n / 8, "decided " + decided);
        }

        [Fact]
        public void ExactBooleanWinding_FiltersDecideMostPlaneSides()
        {
            // the classification casts a ray per patch; the plane sides of the triangles along it are
            // decided in doubles unless nearly degenerate
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.7, 0.3, 0.1));
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute());
            long sides = op.Counts["winding_plane_sides"], exact = op.Counts["winding_plane_sides_exact"];
            Assert.True(sides > 0);
            Assert.True(exact < sides / 10, $"exact {exact} of {sides}");
        }

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        [Fact]
        public void VolumeIoUAtLeast_ClearCasesNeedFewSamples()
        {
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            var ti = new DMeshAABBTree3(input, true);
            int calls = 0;
            Func<Vector3d, bool> inside = (p) => { System.Threading.Interlocked.Increment(ref calls); return ti.IsInside(p); };
            double iou;
            // same solid
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(inside, input.CachedBounds, Box(Vector3d.Zero, Vector3d.One), 0.99, out iou));
            Assert.Equal(1.0, iou);
            Assert.True(calls <= 100000 / 8, "calls " + calls);
            // half of it
            calls = 0;
            Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(inside, input.CachedBounds, Box(Vector3d.Zero, new Vector3d(1, 1, 0.5)), 0.99, out iou));
            Assert.InRange(iou, 0.45, 0.55);
            Assert.True(calls <= 100000 / 8, "calls " + calls);
            // the mesh overload agrees
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(input, Box(Vector3d.Zero, Vector3d.One), 0.99, out iou));
            Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(input, Box(Vector3d.Zero, new Vector3d(1, 1, 0.5)), 0.99, out iou));
        }

        [Fact]
        public void VolumeIoUAtLeast_CloseToTheThresholdIsVolumeIoU()
        {
            // IoU about 0.99: all samples are needed, the answer and the estimate are those of VolumeIoU
            DMesh3 input = Box(Vector3d.Zero, Vector3d.One);
            foreach (double h in new[] { 0.989, 0.99, 0.991 }) {
                DMesh3 result = Box(Vector3d.Zero, new Vector3d(1, 1, h));
                double missing, extra, iou;
                double reference = MeshRepairMetrics.VolumeIoU(input, result, out missing, out extra);
                bool ok = MeshRepairMetrics.VolumeIoUAtLeast(input, result, 0.99, out iou);
                Assert.Equal(reference, iou);
                Assert.Equal(reference >= 0.99, ok);
            }
        }

        [Fact]
        public void VolumeIoUAtLeast_NoSolidInTheInputIsAccepted()
        {
            // a single open triangle encloses nothing: NaN, accepted (as MeshRobustRepair did with VolumeIoU)
            DMesh3 input = new DMesh3();
            input.AppendVertex(Vector3d.Zero); input.AppendVertex(Vector3d.AxisX); input.AppendVertex(Vector3d.AxisY);
            input.AppendTriangle(0, 1, 2);
            double iou;
            Assert.True(MeshRepairMetrics.VolumeIoUAtLeast(input, Box(Vector3d.Zero, Vector3d.One), 0.99, out iou));
            Assert.True(double.IsNaN(iou));
            Assert.False(MeshRepairMetrics.VolumeIoUAtLeast(new DMesh3(), Box(Vector3d.Zero, Vector3d.One), 0.99, out iou));
        }

        [Fact]
        public void HasSelfIntersections_AgreesWithTheCount()
        {
            DMesh3 two = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(two, Box(0.5 * Vector3d.One, 1.5 * Vector3d.One));
            DMesh3 apart = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(apart, Box(new Vector3d(1.01, 0, 0), new Vector3d(2, 1, 1)));
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            // many intersecting pairs: a sphere and a copy of it, slightly shifted
            DMesh3 spheres = new DMesh3(sphere);
            DMesh3 shifted = new DMesh3(sphere);
            MeshTransforms.Translate(shifted, new Vector3d(0.01, 0.02, 0));
            MeshEditor.Append(spheres, shifted);
            foreach (DMesh3 m in new[] { two, apart, sphere, spheres, new DMesh3() })
                Assert.Equal(MeshRepairMetrics.SelfIntersections(m) > 0, MeshRepairMetrics.HasSelfIntersections(m));
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(two));
        }

        static double BruteForceWinding(DMesh3 m, Vector3d p)
        {
            double sum = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in m.TriangleIndices()) {
                m.GetTriVertices(tid, ref a, ref b, ref c);
                sum += MathUtil.TriSolidAngle(a, b, c, ref p) / MathUtil.FourPI;
            }
            return sum;
        }

        [Fact]
        public void FastWindingNumber_LeafTrianglesForEveryTreeLayout()
        {
            // the leaf triangles' corners are copied into arrays for the evaluation: with the far-field
            // approximation off (huge FWNBeta) every triangle is evaluated from them, which must give the
            // plain sum; for both tree layouts (leaf lists from the top-down split and from one-rings) and
            // after a rebuild for a changed mesh
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshEditor.Append(m, Box(new Vector3d(2, 0, 0), new Vector3d(3, 1, 1)));
            var rnd = new Random(4);
            foreach (var strategy in new[] { DMeshAABBTree3.BuildStrategy.TopDownMidpoint, DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings }) {
                var tree = new DMeshAABBTree3(m) { FWNBeta = 1e10 };
                tree.Build(strategy);
                for (int k = 0; k < 100; ++k) {
                    Vector3d p = new Vector3d(-1.5 + 5 * rnd.NextDouble(), -1.5 + 3 * rnd.NextDouble(), -1.5 + 3 * rnd.NextDouble());
                    Assert.Equal(BruteForceWinding(m, p), tree.FastWindingNumber(p), 9);
                }
            }
            var t2 = new DMeshAABBTree3(m, true) { FWNBeta = 1e10 };
            Assert.Equal(BruteForceWinding(m, Vector3d.Zero), t2.FastWindingNumber(Vector3d.Zero), 9);
            MeshTransforms.Translate(m, new Vector3d(10, 0, 0));
            t2.Build();
            foreach (Vector3d p in new[] { Vector3d.Zero, new Vector3d(10, 0, 0), new Vector3d(12.5, 0.5, 0.5) })
                Assert.Equal(BruteForceWinding(m, p), t2.FastWindingNumber(p), 9);
        }

        [Fact]
        public void MeshRobustRepair_ReportsPhaseTimes()
        {
            // clean input: repair, check and volume; two overlapping boxes as one mesh: also the exact resolve
            var robust = new MeshRobustRepair(Box(Vector3d.Zero, Vector3d.One));
            Assert.True(robust.Apply());
            Assert.Contains("repair", robust.PhaseMilliseconds.Keys);
            Assert.Contains("check", robust.PhaseMilliseconds.Keys);
            Assert.Contains("volume", robust.PhaseMilliseconds.Keys);
            Assert.DoesNotContain("solidify", robust.PhaseMilliseconds.Keys);

            DMesh3 two = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(two, Box(0.5 * Vector3d.One, 1.5 * Vector3d.One));
            robust = new MeshRobustRepair(new DMesh3(two));
            Assert.True(robust.Apply());
            Assert.Contains("exact", robust.PhaseMilliseconds.Keys);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(robust.Mesh));

            robust = new MeshRobustRepair(new DMesh3(two)) { ExactResolve = false };
            Assert.True(robust.Apply());
            Assert.True(robust.UsedSolidify);
            Assert.Contains("solidify", robust.PhaseMilliseconds.Keys);
            Assert.Contains(robust.PhaseMilliseconds.Keys, (k) => k.StartsWith("solidify."));
        }
    }
}
