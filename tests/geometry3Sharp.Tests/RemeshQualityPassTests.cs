using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RemeshQualityPass: the worst-first quality post-pass (Hu et al. 2017, crease operations after Wang et al. 2019).
    /// Inputs are procedural: a remeshed box and a remeshed cylinder with needles made by off-centre edge splits (every new
    /// point on the surface, on a crease edge on the crease), so the start is within any band of the reference and every
    /// operation the pass makes has to keep it there.
    /// </summary>
    public class RemeshQualityPassTests
    {
        // ---------------- fixtures ----------------

        static DMesh3 Remeshed(DMesh3 input, double target, int passes = 10, double featureAngle = 30)
        {
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { SmoothSpeedT = 0.5 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, featureAngle);
            r.SetTargetEdgeLength(target);
            for (int k = 0; k < passes; ++k) r.BasicRemeshPass();
            return m;
        }

        static DMesh3 BoxInput() => new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.3, 0.15)), NoSharedVertices = false }.Generate().MakeDMesh();

        static DMesh3 CylinderInput() => new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1, Slices = 48, NoSharedVertices = false }.Generate().MakeDMesh();

        // a house: a 1 x 0.6 x 0.3 box with a roof whose two faces meet at the ridge at 24 degrees (a crease at the
        // CreaseAngleDegrees = 20 of the test that the normal-turn guard alone does not protect: a flip across it turns the
        // triangles by less than 30 degrees)
        static DMesh3 HouseInput()
        {
            double h = 0.3 * Math.Tan(12 * MathUtil.Deg2Rad);
            var m = new DMesh3();
            Vector3d[] v = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 0.6, 0), new Vector3d(0, 0.6, 0),
                new Vector3d(0, 0, 0.3), new Vector3d(1, 0, 0.3), new Vector3d(1, 0.6, 0.3), new Vector3d(0, 0.6, 0.3),
                new Vector3d(0, 0.3, 0.3 + h), new Vector3d(1, 0.3, 0.3 + h) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[][] faces = { new[] { 0, 3, 2 }, new[] { 0, 2, 1 }, new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
                new[] { 3, 0, 4 }, new[] { 3, 4, 7 }, new[] { 4, 8, 7 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 5, 6, 9 },
                new[] { 4, 5, 9 }, new[] { 4, 9, 8 }, new[] { 7, 8, 9 }, new[] { 7, 9, 6 } };
            foreach (int[] f in faces) Assert.True(m.AppendTriangle(f[0], f[1], f[2]) >= 0);
            Vector3d c = m.CachedBounds.Center;
            foreach (int t in m.TriangleIndices()) Assert.True(m.GetTriNormal(t).Dot(m.GetTriCentroid(t) - c) > 0, "outward");
            Assert.True(m.IsClosed());
            return m;
        }

        static readonly Lazy<DMesh3> HouseRemeshed = new Lazy<DMesh3>(() => Remeshed(HouseInput(), 0.05, 10, 20));

        static readonly Lazy<DMesh3> BoxRemeshed = new Lazy<DMesh3>(() => Remeshed(BoxInput(), 0.08));
        static readonly Lazy<DMesh3> CylinderRemeshed = new Lazy<DMesh3>(() => Remeshed(CylinderInput(), 0.12));

        // `count` edges split at 2-8 % of their length from one end (needles next to the end); the new points are on the
        // edges, so on the surface of a polyhedral mesh and on the crease for a crease edge
        static DMesh3 Slivered(DMesh3 mesh, int count, int seed)
        {
            DMesh3 m = new DMesh3(mesh);
            var rnd = new Random(seed);
            for (int k = 0; k < count; ++k) {
                int[] edges = m.EdgeIndices().ToArray();
                int e = edges[rnd.Next(edges.Length)];
                Index2i ev = m.GetEdgeV(e);
                double f = 0.02 + 0.06 * rnd.NextDouble();
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(e, out info) != MeshResult.Ok) continue;
                m.SetVertex(info.vNew, a + f * (b - a));
            }
            return m;
        }

        static double MinAngle(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            return RemeshQualityPass.MinAngle(a, b, c);
        }

        static double MeshMinAngle(DMesh3 m) => m.TriangleIndices().Min(t => MinAngle(m, t));

        static double P01(DMesh3 m) => TriangleQualityStatistics.Compute(m).MinAngle.Worst1Percent;

        // one-sided (from -> to) Hausdorff distance, an upper bound
        static double Hausdorff(DMesh3 from, DMesh3 to, double tol)
        {
            var hd = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = tol };
            Assert.True(hd.Compute() && hd.Converged);
            return hd.UpperBound;
        }

        // the edges whose triangles meet at more than 30 degrees (and boundary edges)
        static List<Segment3d> SharpEdges(DMesh3 m, double degrees = 30)
        {
            double cos = Math.Cos(degrees * MathUtil.Deg2Rad);
            var l = new List<Segment3d>();
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e), ev = m.GetEdgeV(e);
                if (et.b < 0 || m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < cos)
                    l.Add(new Segment3d(m.GetVertex(ev.a), m.GetVertex(ev.b)));
            }
            return l;
        }

        // the largest distance of the samples of `from` (17 per segment) to the segments `to`
        static double CurveDistance(List<Segment3d> from, List<Segment3d> to)
        {
            double worst = 0;
            foreach (Segment3d s in from)
                for (int i = 0; i <= 16; ++i) {
                    Vector3d p = s.PointBetween(i / 16.0);
                    worst = Math.Max(worst, to.Min(t => t.DistanceSquared(p)));
                }
            return Math.Sqrt(worst);
        }

        static void AssertCleanResult(DMesh3 before, DMesh3 m, DMesh3 reference, RemeshQualityResult r, double delta)
        {
            Assert.True(m.IsClosed(), "closed");
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly), "valid, manifold");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(Invariance.AllFinite(m));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            // the smallest angle never drops; the reported values are the mesh's
            Assert.True(MeshMinAngle(m) >= MeshMinAngle(before), $"min angle {MeshMinAngle(before)} -> {MeshMinAngle(m)}");
            Assert.Equal(MeshMinAngle(m), r.MinAngleAfter);
            Assert.Equal(MeshMinAngle(before), r.MinAngleBefore);
            Assert.Equal(P01(m), r.Worst1PercentAfter);
            Assert.True(r.StartWithinBand && r.ResultWithinBand);
            Assert.Equal(delta, r.CertifiedBand);
            // within the band of the reference (the certificate, checked independently)
            Assert.True(Hausdorff(m, reference, 0.01 * delta) <= 1.01 * delta, "mesh -> reference within the band");
        }

        // ---------------- tests ----------------

        /// <summary>
        /// a remeshed box with needles at its edges and on its faces: improved worst first, clean, the smallest angle not
        /// lower, the 1 % rank much higher, within the band, the box's creases kept within the band both ways
        /// </summary>
        [Fact]
        public void Box_Needles_Improved_Clean_InBand_CreasesKept()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            Assert.True(P01(start) < 10, $"fixture: needles {P01(start)}");
            DMesh3 m = new DMesh3(start);
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = delta }.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, r.Status);
            AssertCleanResult(start, m, reference, r, delta);
            Assert.True(r.Worst1PercentAfter > r.Worst1PercentBefore + 15, $"1 % rank {r.Worst1PercentBefore} -> {r.Worst1PercentAfter}");
            Assert.True(r.CreaseInsertions + r.CreaseRemovals > 0, "crease operations made");
            Assert.True(m.TriangleCount <= Math.Floor(start.TriangleCount * 1.05));
            List<Segment3d> refCreases = SharpEdges(reference), outCreases = SharpEdges(m);
            Assert.True(CurveDistance(outCreases, refCreases) <= delta, "every crease of the result on a crease of the box");
            Assert.True(CurveDistance(refCreases, outCreases) <= delta, "every crease of the box kept");
            // the corners (three crease edges) keep their position
            var have = new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));
            foreach (int v in reference.VertexIndices())
                Assert.Contains(reference.GetVertex(v), have);
        }

        // caps on the ridge: for every ridge edge, the opposite vertex on one side (not on a crease) pulled to 5 % of its
        // distance from the edge's midpoint, in its roof plane, where its triangles keep their orientation: a flip across the
        // ridge would then raise the smallest angle a lot
        static DMesh3 Capped(DMesh3 mesh, DMesh3 reference)
        {
            DMesh3 m = new DMesh3(mesh);
            Vector3d r0 = reference.GetVertex(8), r1 = reference.GetVertex(9);
            var ridge = new Segment3d(r0, r1);
            List<Segment3d> creases = SharpEdges(reference, 20);
            bool onRidge(Vector3d p) => ridge.DistanceSquared(p) < 1e-20;
            bool onCrease(Vector3d p) => creases.Any(sg => sg.DistanceSquared(p) < 1e-20);
            int pulled = 0;
            foreach (int e in m.EdgeIndices().ToList()) {
                Index2i ev = m.GetEdgeV(e);
                if (!onRidge(m.GetVertex(ev.a)) || !onRidge(m.GetVertex(ev.b))) continue;
                int c = m.GetEdgeOpposingV(e).a;
                if (onCrease(m.GetVertex(c))) continue;
                Vector3d mid = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b)), old = m.GetVertex(c), q = mid + 0.05 * (old - mid);
                var normals = m.VtxTrianglesItr(c).ToDictionary(t => t, t => m.GetTriNormal(t));
                m.SetVertex(c, q);
                if (m.VtxTrianglesItr(c).Any(t => m.GetTriNormal(t).Dot(normals[t]) < 0.9)) m.SetVertex(c, old);
                else pulled++;
            }
            Assert.True(pulled > 3, $"fixture: {pulled} caps");
            return m;
        }

        /// <summary>
        /// the house's ridge (24 degrees, creases at 20) survives: no flip across it, every split and removal on it keeps it a crease (the
        /// creases of the result on the reference's and every reference crease kept, both within the band)
        /// </summary>
        [Fact]
        public void House_ShallowRidge_Kept()
        {
            DMesh3 reference = HouseInput();
            DMesh3 start = Capped(Slivered(HouseRemeshed.Value, 120, 3), reference);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(start));
            // (a band wide enough to allow flips across the ridge: only the crease set keeps them off it)
            double delta = 2e-2 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta, CreaseAngleDegrees = 20 };
            var r = pass.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, r.Status);
            AssertCleanResult(start, m, reference, r, delta);
            Assert.True(r.CreaseInsertions + r.CreaseRemovals > 0, $"{r.CreaseInsertions} {r.CreaseRemovals}");
            List<Segment3d> refCreases = SharpEdges(reference, 20), outCreases = SharpEdges(m, 20);
            // the straight creases are kept exactly (up to rounding): every new crease vertex is put onto them
            double size = reference.CachedBounds.DiagonalLength;
            Assert.True(CurveDistance(refCreases, outCreases) <= 1e-12 * size, $"every crease of the house kept: {CurveDistance(refCreases, outCreases) / size}");
            Assert.True(CurveDistance(outCreases, refCreases) <= 1e-12 * size, $"no new crease: {CurveDistance(outCreases, refCreases) / size}");
            // the pass's crease set after its operations is exactly the edges along the reference's creases
            List<Segment3d> refAll = SharpEdges(reference, 20);
            bool along(Vector3d p, Vector3d q) => refAll.Any(sg => sg.DistanceSquared(p) < 1e-24 && sg.DistanceSquared(q) < 1e-24 && sg.DistanceSquared(0.5 * (p + q)) < 1e-24);
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                Assert.Equal(along(m.GetVertex(ev.a), m.GetVertex(ev.b)), pass.IsCrease(ev.a, ev.b));
            }
            // the same without splits (the caps on the ridge then go to the flips, which must leave the ridge alone)
            DMesh3 ns = new DMesh3(start);
            var rs = new RemeshQualityPass(ns, reference) { MaxDeviation = delta, CreaseAngleDegrees = 20, EnableSplits = false }.Compute();
            AssertCleanResult(start, ns, reference, rs, delta);
            Assert.True(rs.Flips > 0 && rs.CreaseRemovals > 0);
            Assert.True(CurveDistance(refCreases, SharpEdges(ns, 20)) <= 1e-12 * size, $"without splits: {CurveDistance(refCreases, SharpEdges(ns, 20)) / size}");
        }

        /// <summary>
        /// a jittered equilateral lattice (smallest angles 40-55 degrees) with the target at 60: every triangle is a
        /// candidate, nearly every collapse, flip or move would lower some angle; the smallest angle does not drop (an operation
        /// is made only if the smallest angle of its triangles rises)
        /// </summary>
        [Fact]
        public void Lattice_NoOperationLowersTheSmallestAngleOfItsTriangles()
        {
            var rnd = new Random(5);
            DMesh3 m = new DMesh3();
            int n = 14;
            var id = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = i + 0.5 * j, y = j * Math.Sqrt(3) / 2;
                    bool inner = i > 0 && j > 0 && i < n && j < n;
                    id[i, j] = m.AppendVertex(new Vector3d(x + (inner ? 0.12 * (rnd.NextDouble() - 0.5) : 0), y + (inner ? 0.12 * (rnd.NextDouble() - 0.5) : 0), 0));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(id[i, j], id[i + 1, j], id[i, j + 1]);
                    m.AppendTriangle(id[i + 1, j], id[i + 1, j + 1], id[i, j + 1]);
                }
            DMesh3 reference = new DMesh3(m);
            DMesh3 start = new DMesh3(m);
            double[] before = start.TriangleIndices().Select(t => MinAngle(start, t)).OrderBy(a => a).ToArray();
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = 1e-3, TargetMinAngleDegrees = 60 }.Compute();
            double[] after = m.TriangleIndices().Select(t => MinAngle(m, t)).OrderBy(a => a).ToArray();
            Assert.True(r.RejectedByAngle > 0);
            Assert.True(after[0] >= before[0], $"{before[0]} -> {after[0]}");
        }

        /// <summary>
        /// the certified flags are exact: with a start partly outside the band (a few vertices lifted off the box), the
        /// triangles the pass reports as certified are exactly those TriangleDeviationBounds certifies, the start is reported
        /// outside the band, and the triangles the pass made are all certified
        /// </summary>
        [Fact]
        public void PartlyOutOfBandStart_CertifiedFlagsMatchAnIndependentCheck()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            // 5 vertices away from the box's edges lifted by 3 delta off their face
            var rnd = new Random(7);
            List<Segment3d> edges = SharpEdges(reference);
            foreach (int v in start.VertexIndices().Where(v => edges.Min(sg => sg.DistanceSquared(start.GetVertex(v))) > 0.01).OrderBy(v => rnd.Next()).Take(5).ToList()) {
                Vector3d p = start.GetVertex(v);
                int t = new DMeshAABBTree3(reference, true).FindNearestTriangle(p);
                start.SetVertex(v, p + 3 * delta * reference.GetTriNormal(t));
            }
            DMesh3 m = new DMesh3(start);
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta };
            var r = pass.Compute();
            Assert.False(r.StartWithinBand);
            Assert.True(r.StartUncertifiedTriangles > 0);
            var tree = new DMeshAABBTree3(reference, true);
            var bounds = new TriangleDeviationBounds(reference) { PatchBound = true };
            int uncertified = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                bool within = bounds.IsWithin(tree, a, b, c, delta, 10);
                Assert.Equal(within, pass.IsCertified(t));
                if (!within) uncertified++;
            }
            Assert.Equal(uncertified, r.UncertifiedTriangles);
            Assert.True(r.UncertifiedTriangles <= r.StartUncertifiedTriangles);
            Assert.Equal(r.UncertifiedTriangles == 0, r.ResultWithinBand);
        }

        /// <summary>
        /// the same on a cylinder (curved: the band decides which relocations and flips are allowed). The reference is the
        /// remeshed cylinder itself, which the needles' points lie on
        /// </summary>
        [Fact]
        public void Cylinder_Needles_Improved_Clean_InBand()
        {
            DMesh3 reference = CylinderRemeshed.Value;
            DMesh3 start = Slivered(CylinderRemeshed.Value, 60, 2);
            double delta = 2e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = delta }.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, r.Status);
            AssertCleanResult(start, m, reference, r, delta);
            Assert.True(r.Worst1PercentAfter > r.Worst1PercentBefore + 10, $"1 % rank {r.Worst1PercentBefore} -> {r.Worst1PercentAfter}");
            Assert.True(CurveDistance(SharpEdges(m), SharpEdges(reference)) <= delta);
        }

        /// <summary>
        /// a band far below the facet size of the curved reference: relocations and flips that leave the surface are refused
        /// (RejectedByBand), and the result is still within the band
        /// </summary>
        [Fact]
        public void TinyBand_RefusesWhatLeavesTheSurface_ResultInBand()
        {
            DMesh3 reference = CylinderRemeshed.Value;
            DMesh3 start = Slivered(reference, 60, 2);
            double delta = 1e-7 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = delta }.Compute();
            Assert.True(r.RejectedByBand > 0);
            AssertCleanResult(start, m, reference, r, delta);
            // (a wider band allows more)
            DMesh3 wide = new DMesh3(start);
            var rw = new RemeshQualityPass(wide, reference) { MaxDeviation = 1e4 * delta }.Compute();
            Assert.True(rw.Flips + rw.Collapses + rw.Relocations > r.Flips + r.Collapses + r.Relocations, $"{rw.Flips} {rw.Collapses} {rw.Relocations} {rw.Splits} vs {r.Flips} {r.Collapses} {r.Relocations} {r.Splits}; p01 {r.Worst1PercentAfter} {rw.Worst1PercentAfter}");
        }

        /// <summary>
        /// TwoSided: every reference triangle stays within the band of the result as well (the reverse Hausdorff distance);
        /// the refusal itself is checked in Coverage_* and TwoSided_AcceptRefuses*
        /// </summary>
        [Fact]
        public void TwoSided_KeepsTheReverseDirection()
        {
            DMesh3 reference = CylinderRemeshed.Value;
            DMesh3 start = Slivered(reference, 60, 2);
            double delta = 4e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 one = new DMesh3(start), two = new DMesh3(start);
            var r1 = new RemeshQualityPass(one, reference) { MaxDeviation = delta }.Compute();
            var r2 = new RemeshQualityPass(two, reference) { MaxDeviation = delta, TwoSided = true }.Compute();
            AssertCleanResult(start, two, reference, r2, delta);
            Assert.Equal(RemeshQualityStatus.Improved, r2.Status);
            double back2 = Hausdorff(reference, two, 0.01 * delta);
            Assert.True(back2 <= 1.01 * delta, $"reference -> result {back2 / delta} delta");
            Assert.Equal(RemeshQualityStatus.Improved, r1.Status);
        }

        /// <summary>
        /// the coverage refuses an operation that takes away the only triangles a reference triangle is near (a spike cut
        /// off), and accepts one that keeps the reference covered; after a commit its sets follow the new triangle ids
        /// </summary>
        [Fact]
        public void Coverage_RefusesCuttingASpike_AcceptsAFlatChange()
        {
            DMesh3 m = SpikeGrid(out int v);
            DMesh3 reference = new DMesh3(m);
            double eps = 0.1;
            var grid = new TriangleHashGrid(m.CachedBounds.Min, 1.5);
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                grid.Insert(t, AlphaWrapQualityPass.box(a, b, c));
            }
            var cov = new RemeshQualityCoverage(reference, m, grid, eps, 10);
            Assert.Equal(0, cov.Uncovered);
            // the spike vertex moved down to z = 0: its ring flat
            var ring = new HashSet<int>(m.VtxTrianglesItr(v));
            var flat = new List<Triangle3d>();
            foreach (int t in ring) {
                Index3i tv = m.GetTriangle(t);
                Vector3d[] p = { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                for (int k = 0; k < 3; ++k) if (tv[k] == v) p[k] = new Vector3d(2, 2, 0);
                flat.Add(new Triangle3d(p[0], p[1], p[2]));
            }
            Assert.False(cov.Check(m, ring, flat));
            Assert.Throws<InvalidOperationException>(() => cov.Commit(ring, ring));
            // a flip on the flat part far from the spike changes nothing the reference needs
            // (vertex ids of the grid: i + 5 j)
            int e = m.FindEdge(0, 6);
            Index2i et = m.GetEdgeT(e);
            var two = new HashSet<int> { et.a, et.b };
            var flipped = new List<Triangle3d> {
                new Triangle3d(m.GetVertex(0), m.GetVertex(1), m.GetVertex(5)),
                new Triangle3d(m.GetVertex(1), m.GetVertex(6), m.GetVertex(5)) };
            Assert.True(cov.Check(m, two, flipped));
            DMesh3.EdgeFlipInfo info;
            Assert.Equal(MeshResult.Ok, m.FlipEdge(e, out info));
            cov.Commit(two, new[] { info.t0, info.t1 });
            // every set holds only current triangles, and each reference triangle is still within eps of its set
            foreach (int T in reference.TriangleIndices()) {
                int[] d = cov.DependenciesOf(T);
                Assert.NotNull(d);
                Assert.All(d, x => Assert.True(m.IsTriangle(x)));
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                reference.GetTriVertices(T, ref a, ref b, ref c);
                var tris = d.Select(x => { Index3i tv = m.GetTriangle(x); return new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c)); }).ToList();
                Assert.True(ReducerReverseCoverage.Within(a, b, c, tris, eps, 10, new List<int>()));
            }
            // and the spike is still refused after the commit
            Assert.False(cov.Check(m, ring, flat));
        }

        // a 4 x 4 grid of unit squares at z = 0 with its centre vertex lifted to a spike of height 1
        static DMesh3 SpikeGrid(out int apex)
        {
            DMesh3 m = new DMesh3();
            int[,] g = new int[5, 5];
            for (int j = 0; j < 5; ++j)
                for (int i = 0; i < 5; ++i)
                    g[i, j] = m.AppendVertex(new Vector3d(i, j, (i == 2 && j == 2) ? 1 : 0));
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 4; ++i) {
                    m.AppendTriangle(g[i, j], g[i + 1, j], g[i + 1, j + 1]);
                    m.AppendTriangle(g[i, j], g[i + 1, j + 1], g[i, j + 1]);
                }
            apex = g[2, 2];
            return m;
        }

        /// <summary>
        /// the pass's guard with TwoSided: flattening the spike (the apex moved to z = 0) keeps the new triangles within 0.8 of
        /// the reference (at most 0.71 from the spike's faces) but leaves the apex 1 from the result: refused by the
        /// coverage, accepted one-sided
        /// </summary>
        [Fact]
        public void TwoSided_AcceptRefusesFlatteningASpike()
        {
            foreach (bool twoSided in new[] { true, false }) {
                DMesh3 m = SpikeGrid(out int v);
                var pass = new RemeshQualityPass(m, new DMesh3(m)) { MaxDeviation = 0.8, TwoSided = twoSided };
                pass.Prepare();
                var ring = new HashSet<int>(m.VtxTrianglesItr(v));
                var ids = new List<Index3i>(); var flat = new List<Triangle3d>();
                foreach (int t in ring) {
                    Index3i tv = m.GetTriangle(t);
                    Vector3d[] p = { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                    for (int k = 0; k < 3; ++k) if (tv[k] == v) p[k] = new Vector3d(2, 2, 0);
                    ids.Add(tv); flat.Add(new Triangle3d(p[0], p[1], p[2]));
                }
                bool ok = pass.accept(ids, flat, ring, new List<KeyValuePair<Vector3d, Vector3d>>());
                Assert.Equal(!twoSided, ok);
                Assert.Equal(twoSided ? 1 : 0, pass.Counters.RejectedByCoverage);
                Assert.Equal(twoSided ? 1 : 0, pass.Counters.RejectedByBand);
            }
        }

        /// <summary>
        /// a closed tetrahedron with needles (an extra component of a remesh output): no collapse folds it into a doubled
        /// triangle, and Compute returns normally (a smoke test: the refusal of such a collapse, by the link guard and by a
        /// MeshResult other than Ok, is not reached by the other guards on this input)
        /// </summary>
        [Fact]
        public void Tetrahedron_NeedleNotCollapsed_NoThrow()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0));
            int c = m.AppendVertex(new Vector3d(0.5, 1, 0)), d = m.AppendVertex(new Vector3d(0.03, 0.02, 0.01));     // d next to a: needles
            m.AppendTriangle(a, c, b); m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); m.AppendTriangle(c, a, d);
            Assert.True(m.IsClosed());
            foreach (bool creases in new[] { true, false }) {
                DMesh3 x = new DMesh3(m);
                var r = new RemeshQualityPass(x, new DMesh3(m)) { MaxDeviation = 0.1, CreaseAngleDegrees = 180, EnableCreaseOperations = creases }.Compute();
                Assert.NotEqual(RemeshQualityStatus.InvalidInput, r.Status);
                Assert.Equal(4, x.TriangleCount);
                Assert.True(x.IsClosed() && x.CheckValidity(false, FailMode.ReturnOnly));
                Assert.Equal(0, r.Collapses + r.CreaseRemovals);
            }
        }

        /// <summary>
        /// the breadth-first, piece-bounded certificate of the coverage gives the answer of the depth-first one
        /// (ReducerReverseCoverage.Within) when the piece bound is off, on random triangles and candidate sets
        /// </summary>
        [Fact]
        public void CoverageWithin_SameAnswerAsTheDepthFirstCascade()
        {
            var rnd = new Random(3);
            Vector3d rv(double s) => new Vector3d(s * rnd.NextDouble(), s * rnd.NextDouble(), 0.05 * rnd.NextDouble());
            int yes = 0, no = 0;
            for (int k = 0; k < 400; ++k) {
                Vector3d p0 = rv(1), p1 = rv(1), p2 = rv(1);
                var tris = new List<Triangle3d>();
                int n = 1 + rnd.Next(6);
                for (int i = 0; i < n; ++i) { Vector3d o = rv(1.2) - new Vector3d(0.1, 0.1, 0); tris.Add(new Triangle3d(o, o + rv(0.8), o + rv(0.8))); }
                double eps = 0.05 + 0.3 * rnd.NextDouble();
                int depth = rnd.Next(7);
                bool dfs = ReducerReverseCoverage.Within(p0, p1, p2, tris, eps, depth, new List<int>());
                bool bfs = RemeshQualityCoverage.Within(p0, p1, p2, tris, eps, depth, new List<int>());
                Assert.Equal(dfs, bfs);
                if (dfs) yes++; else no++;
                // the piece bound only refuses more
                if (!bfs) Assert.False(RemeshQualityCoverage.Within(p0, p1, p2, tris, eps, depth, 16, new List<int>()));
            }
            Assert.True(yes > 40 && no > 40, $"{yes} {no}");
        }

        /// <summary>
        /// the coverage after a collapse and a split (triangle ids freed and reused): every set holds only current triangles and
        /// still certifies its reference triangle
        /// </summary>
        [Fact]
        public void Coverage_AfterCollapseAndSplit_SetsStayValid()
        {
            DMesh3 m = SpikeGrid(out int apex);
            DMesh3 reference = new DMesh3(m);
            double eps = 0.3;
            var grid = new TriangleHashGrid(m.CachedBounds.Min, 1.5);
            void regrid() { foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); grid.Remove(t); grid.Insert(t, AlphaWrapQualityPass.box(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c))); } }
            regrid();
            var cov = new RemeshQualityCoverage(reference, m, grid, eps, 10);
            List<Triangle3d> geometry(IEnumerable<int> ids) => ids.Select(x => { Index3i tv = m.GetTriangle(x); return new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c)); }).ToList();
            // collapse the boundary-free edge (6, 7) into 6 at its midpoint (flat region, ids 2 triangles freed)
            int keep = 6, rem = 7;
            var removed = new HashSet<int>(m.VtxTrianglesItr(keep).Concat(m.VtxTrianglesItr(rem)));
            Vector3d q = 0.5 * (m.GetVertex(keep) + m.GetVertex(rem));
            Index2i et = m.GetEdgeT(m.FindEdge(keep, rem));
            var newTris = new List<Triangle3d>();
            foreach (int t in removed) {
                if (t == et.a || t == et.b) continue;
                Index3i tv = m.GetTriangle(t);
                Vector3d[] p = { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                for (int k = 0; k < 3; ++k) if (tv[k] == keep || tv[k] == rem) p[k] = q;
                newTris.Add(new Triangle3d(p[0], p[1], p[2]));
            }
            Assert.True(cov.Check(m, removed, newTris));
            DMesh3.EdgeCollapseInfo ci;
            Assert.Equal(MeshResult.Ok, m.CollapseEdge(keep, rem, out ci));
            m.SetVertex(keep, q);
            foreach (int t in removed) grid.Remove(t);
            cov.Commit(removed, m.VtxTrianglesItr(keep).ToList());
            regrid();
            // split edge (11, 12) at its midpoint: two new triangle ids (one reuses a freed id)
            int e = m.FindEdge(11, 12);
            Index2i st = m.GetEdgeT(e);
            var sremoved = new HashSet<int> { st.a, st.b };
            Vector3d mid = 0.5 * (m.GetVertex(11) + m.GetVertex(12));
            var split = new List<Triangle3d>();
            foreach (int t in sremoved) {
                Index3i tv = m.GetTriangle(t);
                int o = tv.a != 11 && tv.a != 12 ? tv.a : tv.b != 11 && tv.b != 12 ? tv.b : tv.c;
                split.Add(new Triangle3d(m.GetVertex(11), mid, m.GetVertex(o))); split.Add(new Triangle3d(mid, m.GetVertex(12), m.GetVertex(o)));
            }
            Assert.True(cov.Check(m, sremoved, split));
            DMesh3.EdgeSplitInfo si;
            Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out si));
            cov.Commit(sremoved, m.VtxTrianglesItr(si.vNew).ToList());
            foreach (int T in reference.TriangleIndices()) {
                int[] d = cov.DependenciesOf(T);
                Assert.NotNull(d);
                Assert.All(d, x => Assert.True(m.IsTriangle(x)));
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                reference.GetTriVertices(T, ref a, ref b, ref c);
                Assert.True(ReducerReverseCoverage.Within(a, b, c, geometry(d), eps, 10, new List<int>()), $"T {T}");
            }
        }

        /// <summary>
        /// with the corners free (internal PinCreaseCorners = false, a crease vertex anywhere may be removed along its
        /// creases) every crease edge of the result still lies within the band of the box's edges: the collapses' new crease
        /// edges went through the crease band
        /// </summary>
        [Fact]
        public void FreeCorners_CreaseEdgesStayInTheCreaseBand()
        {
            DMesh3 reference = BoxInput();
            double size = reference.CachedBounds.DiagonalLength;
            // (a band between a third of a corner needle's short edge, what cutting the corner moves the surface, and three
            // quarters of it, what it moves the crease: only the crease band refuses)
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 }) {
                DMesh3 start = Slivered(BoxRemeshed.Value, 80, seed);
                double delta = 1.6e-3 * size;
                DMesh3 m = new DMesh3(start);
                var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta, PinCreaseCorners = false };
                var r = pass.Compute();
                AssertCleanResult(start, m, reference, r, delta);
                Assert.True(r.CreaseRemovals > 0);
                var outCreases = m.EdgeIndices().Select(e => m.GetEdgeV(e)).Where(ev => pass.IsCrease(ev.a, ev.b))
                    .Select(ev => new Segment3d(m.GetVertex(ev.a), m.GetVertex(ev.b))).ToList();
                Assert.True(CurveDistance(outCreases, SharpEdges(reference)) <= delta, $"seed {seed}: {CurveDistance(outCreases, SharpEdges(reference)) / delta} delta");
            }
        }

        // a shallow tent (sides sloping 0.1 down from the ridge, so a 11 degree crease) along a ridge polyline, a strip of
        // half-width w on both sides; at a bend the side points sit on the bisector, w / cos(turn / 2) out
        static DMesh3 Tent(Vector3d[] ridge, double w, double slope)
        {
            var m = new DMesh3();
            int n = ridge.Length;
            int[] P = new int[n], L = new int[n], R = new int[n];
            for (int i = 0; i < n; ++i) {
                Vector3d din = i > 0 ? (ridge[i] - ridge[i - 1]).Normalized : (ridge[1] - ridge[0]).Normalized;
                Vector3d dout = i < n - 1 ? (ridge[i + 1] - ridge[i]).Normalized : din;
                Vector3d d = (din + dout).Normalized;
                Vector3d nrm = new Vector3d(-d.y, d.x, 0);
                double f = 1 / Math.Max(0.2, din.Dot(d));
                P[i] = m.AppendVertex(ridge[i]);
                L[i] = m.AppendVertex(ridge[i] + f * w * nrm - new Vector3d(0, 0, slope * w));
                R[i] = m.AppendVertex(ridge[i] - f * w * nrm - new Vector3d(0, 0, slope * w));
            }
            void tri(int a, int b, int c)
            {
                Vector3d nn = (m.GetVertex(b) - m.GetVertex(a)).Cross(m.GetVertex(c) - m.GetVertex(a));
                if (nn.z > 0) m.AppendTriangle(a, b, c); else m.AppendTriangle(a, c, b);
            }
            for (int i = 0; i + 1 < n; ++i) {
                tri(P[i], P[i + 1], L[i + 1]); tri(P[i], L[i + 1], L[i]);
                tri(P[i], R[i], R[i + 1]); tri(P[i], R[i + 1], P[i + 1]);
            }
            return m;
        }

        /// <summary>
        /// a crease collapse whose new crease edge cuts a bend of the crease is refused by the crease band, also where the
        /// surface band would allow it. The ridge of a shallow tent turns by 60 degrees at k (a corner, pinned) and by 40 at r
        /// (sliding), with |kr| = 0.1 and |rx| = 0.4. Removing r into k (the needle on the short crease edge kr) makes the
        /// crease edge kx, about 0.05 off the bend, while the surface moves by about 0.006: at a band of 0.02 only the crease band
        /// refuses it. With a band of 1 the collapse is made (the precondition)
        /// </summary>
        [Fact]
        public void CreaseCollapse_RefusedWhenTheNewCreaseEdgeCutsABend()
        {
            double c40 = Math.Cos(40 * MathUtil.Deg2Rad), s40 = Math.Sin(40 * MathUtil.Deg2Rad);
            Vector3d k = Vector3d.Zero, r = new Vector3d(0.1, 0, 0), x = r + 0.4 * new Vector3d(c40, s40, 0), x2 = x + 0.4 * new Vector3d(c40, s40, 0);
            Vector3d j = k + 0.6 * new Vector3d(-Math.Cos(60 * MathUtil.Deg2Rad), -Math.Sin(60 * MathUtil.Deg2Rad), 0);
            DMesh3 tent = Tent(new[] { j - 0.6 * new Vector3d(Math.Cos(60 * MathUtil.Deg2Rad), Math.Sin(60 * MathUtil.Deg2Rad), 0), j, k, r, x, x2 }, 0.3, 0.1);
            Assert.True(tent.CheckValidity(false, FailMode.ReturnOnly));
            int rv = tent.VertexIndices().First(v => tent.GetVertex(v) == r);
            RemeshQualityResult run(double delta, out DMesh3 m, out RemeshQualityPass pass)
            {
                m = new DMesh3(tent);
                pass = new RemeshQualityPass(m, new DMesh3(tent)) { MaxDeviation = delta, CreaseAngleDegrees = 5, MaxPasses = 1,
                    EnableFlips = false, EnableSplits = false, EnableRelocation = false };
                return pass.Compute();
            }
            var wide = run(1, out DMesh3 mw, out RemeshQualityPass pw);
            Assert.False(mw.IsVertex(rv), "precondition: with a band of 1 the bend vertex is removed");
            var narrow = run(0.02, out DMesh3 mn, out RemeshQualityPass pn);
            Assert.True(mn.IsVertex(rv) && mn.GetVertex(rv) == r, "the bend vertex stays");
            Assert.True(narrow.RejectedByBand > 0);
            // and every crease edge of the result is on the ridge or the boundary within the band
            var outCreases = mn.EdgeIndices().Select(e => mn.GetEdgeV(e)).Where(ev => pn.IsCrease(ev.a, ev.b)).Select(ev => new Segment3d(mn.GetVertex(ev.a), mn.GetVertex(ev.b))).ToList();
            Assert.True(CurveDistance(outCreases, SharpEdges(tent, 5)) <= 0.02);
        }

        /// <summary> the angle measure does not depend on the corner order, bit for bit (the never-lower guarantee rests on it) </summary>
        [Fact]
        public void MinAngle_IndependentOfTheCornerOrder()
        {
            var rnd = new Random(11);
            for (int k = 0; k < 2000; ++k) {
                Vector3d a = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                Vector3d b = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                // slivers: c near the segment ab
                Vector3d c = a + rnd.NextDouble() * (b - a) + 1e-7 * rnd.NextDouble() * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                double m = RemeshQualityPass.MinAngle(a, b, c), x = RemeshQualityPass.MaxAngle(a, b, c);
                foreach (var (p, q, r) in new[] { (b, c, a), (c, a, b), (b, a, c), (a, c, b), (c, b, a) }) {
                    Assert.Equal(m, RemeshQualityPass.MinAngle(p, q, r));
                    Assert.Equal(x, RemeshQualityPass.MaxAngle(p, q, r));
                }
            }
            Assert.Equal(60, RemeshQualityPass.MinAngle(Vector3d.Zero, Vector3d.AxisX, new Vector3d(0.5, Math.Sqrt(3) / 2, 0)), 12);
            Assert.Equal(0, RemeshQualityPass.MinAngle(Vector3d.Zero, Vector3d.Zero, Vector3d.AxisX));
        }

        /// <summary>
        /// the coverage certificate: a large triangle whose corners are near small candidate triangles but whose interior
        /// is far from all of them is not covered (the covering radius matters); with a candidate under all of it, it is
        /// </summary>
        [Fact]
        public void CoverageWithin_CornersNearButInteriorFar_NotCovered()
        {
            Vector3d p0 = Vector3d.Zero, p1 = new Vector3d(1, 0, 0), p2 = new Vector3d(0, 1, 0);
            double s = 0.05;
            Triangle3d at(Vector3d p) => new Triangle3d(p, p + new Vector3d(s, 0, 0), p + new Vector3d(0, s, 0));
            var corners = new List<Triangle3d> { at(p0), at(p1), at(p2) };
            Assert.False(RemeshQualityCoverage.Within(p0, p1, p2, corners, 0.1, 10, new List<int>()));
            var under = new List<Triangle3d> { new Triangle3d(new Vector3d(-1, -1, 0.05), new Vector3d(3, -1, 0.05), new Vector3d(-1, 3, 0.05)) };
            var used = new List<int>();
            Assert.True(RemeshQualityCoverage.Within(p0, p1, p2, under, 0.1, 10, used));
            Assert.Equal(new[] { 0 }, used.Distinct().ToArray());
            // a cover by two halves: needs a subdivision; with no levels left it is refused
            var halves = new List<Triangle3d> { new Triangle3d(new Vector3d(-1, -1, 0.05), new Vector3d(0.5, -1, 0.05), new Vector3d(-1, 3, 0.05)),
                new Triangle3d(new Vector3d(0.5, -1, 0.05), new Vector3d(3, -1, 0.05), new Vector3d(-1, 3, 0.05)) };
            Assert.True(RemeshQualityCoverage.Within(p0, p1, p2, halves, 0.1, 10, new List<int>()));
            Assert.False(RemeshQualityCoverage.Within(p0, p1, p2, halves, 0.1, 0, new List<int>()));
            Assert.False(RemeshQualityCoverage.Within(p0, p1, p2, halves, 0.1, 10, 1, new List<int>()));
        }

        /// <summary>
        /// crease detection: an edge whose triangles meet at more than CreaseAngleDegrees is a crease only if it lies on a
        /// crease of the reference (a fold of the mesh, eg a lifted vertex, is not one and is not pinned)
        /// </summary>
        [Fact]
        public void Creases_AFoldOfTheMeshIsNotACrease()
        {
            DMesh3 reference = BoxInput();
            DMesh3 m = new DMesh3(BoxRemeshed.Value);
            // the vertex nearest the top face's centre lifted by 0.05 (its ring folds by more than 30 degrees)
            Vector3d top = new Vector3d(0, 0, reference.CachedBounds.Max.z);
            int v = m.VertexIndices().OrderBy(x => m.GetVertex(x).DistanceSquared(top)).First();
            m.SetVertex(v, m.GetVertex(v) + new Vector3d(0, 0, 0.05));
            double cos = Math.Cos(30 * MathUtil.Deg2Rad);
            int folds = 0;
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = 1e-3 };
            pass.Prepare();
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e), ev = m.GetEdgeV(e);
                bool sharp = m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < cos;
                bool onBox = SharpEdges(reference).Any(sg => sg.DistanceSquared(m.GetVertex(ev.a)) < 1e-18 && sg.DistanceSquared(m.GetVertex(ev.b)) < 1e-18);
                Assert.Equal(sharp && onBox, pass.IsCrease(ev.a, ev.b));
                if (sharp && !onBox) folds++;
            }
            Assert.True(folds > 0, "the fixture has folds");
            Assert.Equal(RemeshQualityPass.Free, pass.VertexClass(v));
        }

        /// <summary>
        /// a relocation is made only if it raises the smallest angle of the vertex's triangles: a fan whose centroid would
        /// lower it is left as it is (the other operations off)
        /// </summary>
        [Fact]
        public void Relocation_NotMadeIfItLowersTheSmallestAngle()
        {
            double[] deg = { 111, 142, 188, 225, 250, 339 }, rad = { 0.9, 2.5, 2.0, 1.2, 1.8, 1.25 };
            DMesh3 m = new DMesh3();
            int v = m.AppendVertex(Vector3d.Zero);
            var ring = deg.Select((d, i) => m.AppendVertex(new Vector3d(rad[i] * Math.Cos(d * MathUtil.Deg2Rad), rad[i] * Math.Sin(d * MathUtil.Deg2Rad), 0))).ToArray();
            for (int i = 0; i < ring.Length; ++i) m.AppendTriangle(v, ring[i], ring[(i + 1) % ring.Length]);
            // (precondition: the centroid of the ring is worse than the vertex's place)
            Vector3d q = ring.Aggregate(Vector3d.Zero, (acc, r) => acc + m.GetVertex(r)) / ring.Length;
            double at(Vector3d p) => Enumerable.Range(0, ring.Length).Min(i => RemeshQualityPass.MinAngle(p, m.GetVertex(ring[i]), m.GetVertex(ring[(i + 1) % ring.Length])));
            Assert.True(at(q) < at(Vector3d.Zero) - 3 && at(Vector3d.Zero) < 30, $"{at(Vector3d.Zero)} {at(q)}");
            var r1 = new RemeshQualityPass(m, new DMesh3(m)) { MaxDeviation = 1, EnableFlips = false, EnableCollapses = false, EnableSplits = false }.Compute();
            Assert.Equal(0, r1.Relocations);
            Assert.Equal(Vector3d.Zero, m.GetVertex(v));
            Assert.True(r1.RejectedByAngle > 0);
        }

        /// <summary>
        /// a flip that raises the smallest angle but turns the triangles' normals by more than 30 degrees is not made (two
        /// triangles meeting at 53 degrees along their shared edge, not a crease at CreaseAngleDegrees = 60)
        /// </summary>
        [Fact]
        public void Flip_NotMadeIfItTurnsTheNormalsTooMuch()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(1, 0, 0));
            int c = m.AppendVertex(new Vector3d(0.5, 0.4, -0.2)), d = m.AppendVertex(new Vector3d(0.5, -0.4, -0.2));
            m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, d);
            double cur = Math.Min(RemeshQualityPass.MinAngle(m.GetVertex(a), m.GetVertex(b), m.GetVertex(c)), RemeshQualityPass.MinAngle(m.GetVertex(b), m.GetVertex(a), m.GetVertex(d)));
            double flipped = Math.Min(RemeshQualityPass.MinAngle(m.GetVertex(c), m.GetVertex(a), m.GetVertex(d)), RemeshQualityPass.MinAngle(m.GetVertex(d), m.GetVertex(b), m.GetVertex(c)));
            Assert.True(flipped > cur + 5, "precondition: the flip raises the smallest angle");
            RemeshQualityResult run(double turn)
            {
                var p = new RemeshQualityPass(new DMesh3(m), new DMesh3(m)) { MaxDeviation = 1, CreaseAngleDegrees = 60, TargetMinAngleDegrees = 60,
                    EnableRelocation = false, EnableCollapses = false, EnableSplits = false, MaxNormalTurnDegrees = turn };
                return p.Compute();
            }
            Assert.Equal(0, run(30).Flips);
            Assert.Equal(1, run(40).Flips);     // (the turn is 34 degrees)
        }

        /// <summary>
        /// a flip whose new edge pierces a small vertical sheet under the ridge of the two triangles (an improving flip
        /// otherwise) is refused by the exact test; without the guard it is made
        /// </summary>
        [Fact]
        public void Flip_RefusedIfTheNewEdgePiercesTheMesh()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0.1)), b = m.AppendVertex(new Vector3d(1, 0, 0.1));
            int c = m.AppendVertex(new Vector3d(0.5, 0.4, 0)), d = m.AppendVertex(new Vector3d(0.5, -0.4, 0));
            m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, d);
            // the sheet in the plane y = 0, below the ridge, around the point (0.5, 0, 0) the new edge (c, d) passes
            int s0 = m.AppendVertex(new Vector3d(0.45, 0, 0.02)), s1 = m.AppendVertex(new Vector3d(0.55, 0, 0.02)), s2 = m.AppendVertex(new Vector3d(0.5, 0, -0.05));
            m.AppendTriangle(s0, s1, s2);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            RemeshQualityResult run(bool guard, out DMesh3 x)
            {
                x = new DMesh3(m);
                return new RemeshQualityPass(x, new DMesh3(m)) { MaxDeviation = 1, CreaseAngleDegrees = 60, TargetMinAngleDegrees = 60, PreventSelfIntersections = guard,
                    EnableRelocation = false, EnableCollapses = false, EnableSplits = false }.Compute();
            }
            var with = run(true, out DMesh3 kept);
            Assert.Equal(0, with.Flips);
            Assert.True(with.RejectedByIntersection > 0);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(kept));
            var without = run(false, out DMesh3 flipped);
            Assert.Equal(1, without.Flips);                                    // (the precondition: the flip is wanted)
            Assert.True(MeshRepairMetrics.SelfIntersections(flipped) > 0);
        }

        /// <summary>
        /// a relocation that would lower the smallest angle of its ring by less than MinGainDegrees (0.05 degrees here) is
        /// not made either: the rule is a gain, not a tolerance
        /// </summary>
        [Fact]
        public void Relocation_NotMadeIfItLowersTheRingAngleSlightly()
        {
            double[] deg = { 111, 142, 188, 225, 250, 339 }, rad = { 0.9, 2.5, 2.0, 1.2, 1.8, 1.25 };
            var ringPts = deg.Select((dd, i) => new Vector3d(rad[i] * Math.Cos(dd * MathUtil.Deg2Rad), rad[i] * Math.Sin(dd * MathUtil.Deg2Rad), 0)).ToArray();
            Vector3d q = ringPts.Aggregate(Vector3d.Zero, (acc, r) => acc + r) / ringPts.Length;
            double at(Vector3d p) => Enumerable.Range(0, ringPts.Length).Min(i => RemeshQualityPass.MinAngle(p, ringPts[i], ringPts[(i + 1) % ringPts.Length]));
            // the vertex on the segment (0, q) where its ring's angle is 0.05 above the centroid's (bisection)
            double lo = 0, hi = 1, target = at(q) + 0.05;
            for (int k = 0; k < 60; ++k) { double mid = 0.5 * (lo + hi); if (at(mid * q) > target) lo = mid; else hi = mid; }
            Vector3d p0 = lo * q;
            Assert.InRange(at(p0) - at(q), 0.01, 0.1);
            DMesh3 m = new DMesh3();
            int v = m.AppendVertex(p0);
            var ring = ringPts.Select(x => m.AppendVertex(x)).ToArray();
            for (int i = 0; i < ring.Length; ++i) m.AppendTriangle(v, ring[i], ring[(i + 1) % ring.Length]);
            var r1 = new RemeshQualityPass(m, new DMesh3(m)) { MaxDeviation = 1, EnableFlips = false, EnableCollapses = false, EnableSplits = false }.Compute();
            Assert.Equal(0, r1.Relocations);
            Assert.Equal(p0, m.GetVertex(v));
        }

        /// <summary>
        /// the crease edges a crease collapse makes are the removed vertex's other crease edges, moved to the kept vertex,
        /// and they go through the crease band: on the box, removing a crease vertex next to a corner into its neighbour
        /// along one edge gives the chord to the corner's ... (here: a vertex on an edge, its two crease neighbours)
        /// </summary>
        [Fact]
        public void CollapseChords_AreTheRemovedVertexsOtherCreaseEdges_AndGoThroughTheBand()
        {
            DMesh3 reference = BoxInput();
            DMesh3 m = new DMesh3(BoxRemeshed.Value);
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = 1e-3 };
            pass.Prepare();
            // a crease vertex with exactly two crease neighbours u, w
            int v = m.VertexIndices().First(x => m.VtxVerticesItr(x).Count(y => pass.IsCrease(x, y)) == 2);
            var nb = m.VtxVerticesItr(v).Where(y => pass.IsCrease(v, y)).ToArray();
            int u = nb[0], w = nb[1];
            var chords = new List<KeyValuePair<Vector3d, Vector3d>>();
            pass.collapse_chords(u, v, m.GetVertex(u), chords);
            Assert.Single(chords);
            Assert.Equal(m.GetVertex(u), chords[0].Key);
            Assert.Equal(m.GetVertex(w), chords[0].Value);
            // (along a straight edge the chord is on the crease; moved 0.01 off it, it is not)
            Assert.True(pass.chord_on_feature(chords[0].Key, chords[0].Value));
            Vector3d off = new Vector3d(0, 0, 0.01);
            Assert.False(pass.accept(new List<Index3i>(), new List<Triangle3d>(), new HashSet<int>(),
                new List<KeyValuePair<Vector3d, Vector3d>> { new KeyValuePair<Vector3d, Vector3d>(chords[0].Key + off, chords[0].Value + off) }));
            Assert.Equal(1, pass.Counters.RejectedByCrease);
        }

        /// <summary>
        /// a triangle of exactly zero area (a vertex on the segment of two of its ring neighbours) is repaired: it has no
        /// normal, so its new triangle is held to the ring's normal instead of being refused
        /// </summary>
        [Fact]
        public void ZeroAreaTriangle_IsRepaired()
        {
            DMesh3 m = new DMesh3();
            int n = 6;
            var id = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) id[i, j] = m.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) { m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]); m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]); }
            // vertex (3, 3) onto the midpoint of its neighbours (2, 3) and (3, 4): the triangle (2,3)-(3,3)-(3,4) is flat
            int v = id[3, 3];
            m.SetVertex(v, 0.5 * (m.GetVertex(id[2, 3]) + m.GetVertex(id[3, 4])));
            Assert.Equal(0, MeshMinAngle(m));
            Assert.True(m.TriangleIndices().Where(t => m.GetTriArea(t) > 0).All(t => m.GetTriNormal(t).z > 0), "the other triangles face up");
            DMesh3 start = new DMesh3(m);
            var r = new RemeshQualityPass(m, new DMesh3(start)) { MaxDeviation = 1e-3 }.Compute();
            Assert.Equal(0, r.MinAngleBefore);
            Assert.True(r.MinAngleAfter > 10, $"{r.MinAngleAfter}");
            Assert.True(m.TriangleIndices().All(t => m.GetTriNormal(t).z > 0.99), "all face up");
            Assert.True(r.ResultWithinBand);
            // the same with relocations only, and with collapses only: each repairs it on its own (the ring's normal stands in
            // for the flat triangle's)
            foreach (bool move in new[] { true, false }) {
                DMesh3 x = new DMesh3(start);
                var rx = new RemeshQualityPass(x, new DMesh3(start)) { MaxDeviation = 1e-3, EnableFlips = false, EnableSplits = false,
                    EnableRelocation = move, EnableCollapses = !move }.Compute();
                Assert.True(rx.MinAngleAfter > 0, $"{(move ? "relocation" : "collapse")}: {rx.MinAngleAfter}");
                Assert.True(x.TriangleIndices().All(t => x.GetTriNormal(t).z > 0.99));
            }
        }

        /// <summary> a long skew crease segment over small cells is put into the cells along it only (no box blow-up) </summary>
        [Fact]
        public void CreaseGrid_LongSkewSegment_StaysSmall()
        {
            // a reference with many tiny sharp edges (a jagged strip) and one long diagonal boundary edge
            DMesh3 r = new DMesh3();
            int k = 400;
            for (int i = 0; i <= k; ++i) { r.AppendVertex(new Vector3d(i * 0.0025, 0, (i % 2) * 0.0025)); r.AppendVertex(new Vector3d(i * 0.0025, 0.0025, (i % 2) * 0.0025)); }
            for (int i = 0; i < k; ++i) { r.AppendTriangle(2 * i, 2 * i + 2, 2 * i + 3); r.AppendTriangle(2 * i, 2 * i + 3, 2 * i + 1); }
            int far = r.AppendVertex(new Vector3d(0, 1, 1));
            r.AppendTriangle(2 * k + 1, far, 1);     // (a long thin triangle: its edges are long skew boundary edges)
            var pass = new RemeshQualityPass(new DMesh3(r), r) { MaxDeviation = 1e-3 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pass.Prepare();
            Assert.True(sw.Elapsed.TotalSeconds < 2, $"{sw.Elapsed.TotalSeconds} s");
            Assert.True(pass.FeatureCellCount < 200000, $"{pass.FeatureCellCount} cells");
            // the cells along the long edge still find it
            Vector3d mid = 0.5 * (r.GetVertex(1) + r.GetVertex(far));
            Assert.True(pass.chord_on_feature(mid - 0.01 * (r.GetVertex(far) - r.GetVertex(1)), mid + 0.01 * (r.GetVertex(far) - r.GetVertex(1))));
        }

        /// <summary> CancelF = null means never cancelled; repeated Computes do not grow the mesh past MaxTriangleGrowth of the first </summary>
        [Fact]
        public void NullCancel_AndGrowthAcrossComputes()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            // (no collapses: the count only grows, by the splits the needles want, far more than the bound)
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta, CancelF = null, MaxTriangleGrowth = 0.002, MaxPasses = 1, EnableCollapses = false };
            int splits = 0;
            for (int k = 0; k < 4; ++k) {
                var r = pass.Compute();
                Assert.NotEqual(RemeshQualityStatus.Cancelled, r.Status);
                splits += r.Splits + r.CreaseInsertions;
            }
            Assert.True(splits > 0);
            Assert.True(m.TriangleCount <= Math.Floor(start.TriangleCount * 1.002), $"{start.TriangleCount} -> {m.TriangleCount}");
        }

        /// <summary> the exact self-intersection guard: a new triangle through another one is refused, a free one accepted </summary>
        [Fact]
        public void Accept_RefusesANewTriangleThroughTheMesh()
        {
            DMesh3 reference = BoxInput();
            DMesh3 m = new DMesh3(BoxRemeshed.Value);
            double size = reference.CachedBounds.DiagonalLength;
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = size };
            pass.Prepare();
            // a triangle that pierces the box's top face (z = 0.15 at the box's centre)
            Vector3d c = reference.CachedBounds.Center;
            var through = new Triangle3d(c + new Vector3d(-0.01, 0, -0.1), c + new Vector3d(0.01, 0, -0.1), c + new Vector3d(0, 0, 0.2));
            var ids = new List<Index3i> { new Index3i(int.MaxValue - 3, int.MaxValue - 2, int.MaxValue - 1) };
            Assert.False(pass.accept(ids, new List<Triangle3d> { through }, new HashSet<int>(), new List<KeyValuePair<Vector3d, Vector3d>>()));
            Assert.Equal(1, pass.Counters.RejectedByIntersection);
            var inside = new Triangle3d(c + new Vector3d(-0.01, 0, -0.01), c + new Vector3d(0.01, 0, -0.01), c + new Vector3d(0, 0, 0.01));
            Assert.True(pass.accept(ids, new List<Triangle3d> { inside }, new HashSet<int>(), new List<KeyValuePair<Vector3d, Vector3d>>()));
            // two new triangles of one operation that cross each other (no shared vertex) are refused too
            var x1 = new Triangle3d(c + new Vector3d(-0.01, 0, -0.01), c + new Vector3d(0.01, 0, -0.01), c + new Vector3d(0, 0, 0.01));
            var x2 = new Triangle3d(c + new Vector3d(0, -0.01, -0.005), c + new Vector3d(0, 0.01, -0.005), c + new Vector3d(0, 0, 0.02));
            int before = pass.Counters.RejectedByIntersection;
            Assert.False(pass.accept(new List<Index3i> { new Index3i(int.MaxValue - 3, int.MaxValue - 2, int.MaxValue - 1), new Index3i(int.MaxValue - 6, int.MaxValue - 5, int.MaxValue - 4) },
                new List<Triangle3d> { x1, x2 }, new HashSet<int>(), new List<KeyValuePair<Vector3d, Vector3d>>()));
            Assert.Equal(before + 1, pass.Counters.RejectedByIntersection);
            // a new crease edge off the box's creases is refused by the crease band (checked first)
            var narrow = new RemeshQualityPass(m, reference) { MaxDeviation = 0.01 };
            narrow.Prepare();
            var chord = new List<KeyValuePair<Vector3d, Vector3d>> { new KeyValuePair<Vector3d, Vector3d>(reference.CachedBounds.Min, reference.CachedBounds.Max) };
            Assert.False(narrow.accept(ids, new List<Triangle3d> { inside }, new HashSet<int>(), chord));
            Assert.Equal(1, narrow.Counters.RejectedByCrease);
            Assert.Equal(0, narrow.Counters.RejectedByIntersection);
            // with the guard off the piercing one passes (the band is the whole box)
            var off = new RemeshQualityPass(m, reference) { MaxDeviation = size, PreventSelfIntersections = false };
            off.Prepare();
            Assert.True(off.accept(ids, new List<Triangle3d> { through }, new HashSet<int>(), new List<KeyValuePair<Vector3d, Vector3d>>()));
        }

        /// <summary>
        /// the crease band: a chord along an edge of the box is on its creases, one that cuts a corner by more than the band
        /// is not, one that cuts it by less is; a chord across a face is not (it is near no crease in its middle)
        /// </summary>
        [Fact]
        public void CreaseBand_ChordsOnAndOffTheBoxEdges()
        {
            DMesh3 reference = BoxInput();
            double delta = 0.01;
            var pass = new RemeshQualityPass(new DMesh3(BoxRemeshed.Value), reference) { MaxDeviation = delta };
            pass.Prepare();
            Vector3d o = reference.CachedBounds.Min;     // a corner of the box (extent 1 x 0.6 x 0.3)
            Vector3d x = Vector3d.AxisX, y = Vector3d.AxisY, z = Vector3d.AxisZ;
            Assert.True(pass.chord_on_feature(o + 0.1 * x, o + 0.9 * x));
            // across the corner in the bottom face: from s x to s y, whose midpoint is s / 2 from both edges
            Assert.True(pass.chord_on_feature(o + 0.018 * x, o + 0.018 * y));      // midpoint 0.009 off
            Assert.False(pass.chord_on_feature(o + 0.022 * x, o + 0.022 * y));     // midpoint 0.011 off
            Assert.False(pass.chord_on_feature(o + 0.1 * x, o + 0.1 * x + 0.6 * y));     // across the bottom face
            Assert.False(pass.chord_on_feature(o + 0.1 * x, o + 0.1 * x + 0.05 * y));    // an end off every crease
            Assert.True(pass.chord_on_feature(o, o + 0.3 * z));
            // within the band of a crease but not on it: a chord 0.009 above the bottom edge, parallel to it
            Assert.True(pass.chord_on_feature(o + 0.009 * z, o + 0.009 * z + 0.9 * x));
            Assert.False(pass.chord_on_feature(o + 0.011 * z, o + 0.011 * z + 0.9 * x));
        }

        /// <summary>
        /// invariance over Similarity.All(): at the 2^k scales the result is the same bit for bit (mapped back); in every
        /// frame it is clean, within the band and the smallest angle is not lower
        /// </summary>
        [Fact]
        public void Invariance_OverSimilarities()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 m0 = new DMesh3(start);
            var r0 = new RemeshQualityPass(m0, reference) { MaxDeviation = delta }.Compute();
            // (TwoSided against the remeshed box, whose triangles the needles' points lie on: the coverage of a reference much
            // coarser than the mesh, the 12 triangles of the box, needs more pieces than a certificate may use)
            DMesh3 tref = BoxRemeshed.Value;
            DMesh3 t0 = new DMesh3(start);
            var rt0 = new RemeshQualityPass(t0, tref) { MaxDeviation = delta, TwoSided = true }.Compute();
            Assert.True(rt0.Status == RemeshQualityStatus.Improved && rt0.ResultWithinBand && rt0.StartWithinBand);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 ref1 = Invariance.Transformed(reference, s), start1 = Invariance.Transformed(start, s);
                // (TwoSided too: the coverage cascade)
                DMesh3 t = new DMesh3(start1);
                var rt = new RemeshQualityPass(t, Invariance.Transformed(tref, s)) { MaxDeviation = s.ApplyLength(delta), TwoSided = true }.Compute();
                if (s.IsExact) Assert.Equal("", Invariance.Difference(t0, Invariance.MappedBack(t, s)));
                Assert.True(rt.Status == RemeshQualityStatus.Improved && rt.ResultWithinBand && t.IsClosed(), s.Name);
                Assert.True(MeshMinAngle(t) >= MeshMinAngle(start1), s.Name);
                DMesh3 m = new DMesh3(start1);
                var r = new RemeshQualityPass(m, ref1) { MaxDeviation = s.ApplyLength(delta) }.Compute();
                if (s.IsExact) {
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, s)));
                    Assert.Equal(r0.Worst1PercentAfter, r.Worst1PercentAfter);
                    Assert.Equal(r0.Flips + r0.Collapses + r0.Splits + r0.Relocations + r0.CreaseInsertions + r0.CreaseRemovals,
                        r.Flips + r.Collapses + r.Splits + r.Relocations + r.CreaseInsertions + r.CreaseRemovals);
                }
                Assert.True(m.IsClosed() && Invariance.AllFinite(m), s.Name);
                Assert.True(r.Status == RemeshQualityStatus.Improved && r.ResultWithinBand, s.Name);
                Assert.True(MeshMinAngle(m) >= MeshMinAngle(start1), s.Name);
                Assert.True(r.Worst1PercentAfter > r.Worst1PercentBefore + 15, $"{s.Name}: {r.Worst1PercentBefore} -> {r.Worst1PercentAfter}");
            }
        }

        /// <summary>
        /// a second Compute on the same instance finds nothing left to do once the first converged (and changes nothing), and
        /// two runs on equal inputs give the same mesh (serial, a fixed order)
        /// </summary>
        [Fact]
        public void Reuse_And_Determinism()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta, MaxPasses = 100 };
            var r1 = pass.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, r1.Status);
            Assert.True(r1.Passes < 100, "converged");
            DMesh3 once = new DMesh3(m);
            var r2 = pass.Compute();
            Assert.Equal(RemeshQualityStatus.Unchanged, r2.Status);
            // a first Compute stopped early (one pass), then a second on the same instance: the same as two fresh instances
            DMesh3 a = new DMesh3(start), b = new DMesh3(start);
            var pa = new RemeshQualityPass(a, reference) { MaxDeviation = delta, MaxPasses = 1 };
            var ra1 = pa.Compute();
            var ra2 = pa.Compute();
            new RemeshQualityPass(b, reference) { MaxDeviation = delta, MaxPasses = 1 }.Compute();
            var rb2 = new RemeshQualityPass(b, reference) { MaxDeviation = delta, MaxPasses = 1 }.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, ra2.Status);
            Assert.Equal("", Invariance.Difference(a, b));
            Assert.Equal(rb2.Flips + rb2.Collapses + rb2.Splits + rb2.Relocations + rb2.CreaseInsertions + rb2.CreaseRemovals,
                ra2.Flips + ra2.Collapses + ra2.Splits + ra2.Relocations + ra2.CreaseInsertions + ra2.CreaseRemovals);
            Assert.Equal(rb2.RejectedByBand, ra2.RejectedByBand);
            Assert.Equal("", Invariance.Difference(once, m));
            Assert.Equal(r1.Worst1PercentAfter, r2.Worst1PercentBefore);
            Assert.True(r2.StartWithinBand && r2.ResultWithinBand);
            DMesh3 again = new DMesh3(start);
            new RemeshQualityPass(again, reference) { MaxDeviation = delta, MaxPasses = 100 }.Compute();
            Assert.Equal("", Invariance.Difference(once, again));
        }

        /// <summary> cancelled part way: Cancelled, and what was done keeps every guard </summary>
        [Fact]
        public void Cancel_PartlyImproved_StillClean()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(start);
            int calls = 0;
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = delta, CancelF = () => ++calls > 40 }.Compute();
            Assert.Equal(RemeshQualityStatus.Cancelled, r.Status);
            Assert.True(r.Flips + r.Collapses + r.Splits + r.Relocations + r.CreaseInsertions + r.CreaseRemovals > 0, "some work before the cancel");
            AssertCleanResult(start, m, reference, r, delta);
        }

        /// <summary> invalid input: nothing is done </summary>
        [Fact]
        public void InvalidInput_NothingDone()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 20, 1);
            foreach (double delta in new[] { 0, -1, double.NaN, double.PositiveInfinity }) {
                DMesh3 m = new DMesh3(start);
                Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(m, reference) { MaxDeviation = delta }.Compute().Status);
                Assert.Equal("", Invariance.Difference(start, m));
            }
            Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(new DMesh3(start), new DMesh3()) { MaxDeviation = 0.01 }.Compute().Status);
            Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(new DMesh3(), reference) { MaxDeviation = 0.01 }.Compute().Status);
            DMesh3 same = new DMesh3(start);
            Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(same, same) { MaxDeviation = 0.01 }.Compute().Status);
            DMesh3 nan = new DMesh3(start);
            nan.SetVertex(nan.VertexIndices().First(), new Vector3d(double.NaN, 0, 0));
            DMesh3 nanCopy = new DMesh3(nan);
            Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(nan, reference) { MaxDeviation = 0.01 }.Compute().Status);
            Assert.Equal(RemeshQualityStatus.InvalidInput, new RemeshQualityPass(new DMesh3(start), nanCopy) { MaxDeviation = 0.01 }.Compute().Status);
        }

        /// <summary>
        /// Constraints: the constrained edges are the creases (never flipped, split only on the crease) and Fixed vertices
        /// keep their position and stay; crease operations off: every crease vertex keeps its position
        /// </summary>
        [Fact]
        public void Constraints_FixedVerticesStay_CreaseOperationsOff_CreaseVerticesStay()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            // the vertices of the 20 worst triangles fixed, the sharp edges constrained
            DMesh3 m = new DMesh3(start);
            var c = new MeshConstraints();
            double cos = Math.Cos(30 * MathUtil.Deg2Rad);
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e);
                if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < cos) c.SetOrUpdateEdgeConstraint(e, new EdgeConstraint(EdgeRefineFlags.NoFlip));
            }
            var fixedV = new Dictionary<int, Vector3d>();
            foreach (int t in m.TriangleIndices().OrderBy(t => MinAngle(m, t)).ThenBy(t => t).Take(20))
                foreach (int v in new[] { m.GetTriangle(t).a, m.GetTriangle(t).b, m.GetTriangle(t).c }) {
                    c.SetOrUpdateVertexConstraint(v, VertexConstraint.Pinned);
                    fixedV[v] = m.GetVertex(v);
                }
            var r = new RemeshQualityPass(m, reference) { MaxDeviation = delta, Constraints = c }.Compute();
            Assert.Equal(RemeshQualityStatus.Improved, r.Status);
            AssertCleanResult(start, m, reference, r, delta);
            foreach (var kv in fixedV) {
                Assert.True(m.IsVertex(kv.Key));
                Assert.Equal(kv.Value, m.GetVertex(kv.Key));
            }
            Assert.True(CurveDistance(SharpEdges(reference), SharpEdges(m)) <= delta);
            // a second Compute on the same instance keeps the pass's crease set (the constraints' edge ids are stale by now)
            var pass = new RemeshQualityPass(m, reference) { MaxDeviation = delta, Constraints = c, MaxPasses = 1 };
            pass.Compute();
            var again = pass.Compute();
            AssertCleanResult(start, m, reference, again, delta);
            foreach (var kv in fixedV) Assert.Equal(kv.Value, m.GetVertex(kv.Key));
            Assert.True(CurveDistance(SharpEdges(reference), SharpEdges(m)) <= 1e-12 * reference.CachedBounds.DiagonalLength, "the box's edges stay exact");

            DMesh3 off = new DMesh3(start);
            var crease = new Dictionary<int, Vector3d>();
            foreach (int e in off.EdgeIndices()) {
                Index2i et = off.GetEdgeT(e), ev = off.GetEdgeV(e);
                if (off.GetTriNormal(et.a).Dot(off.GetTriNormal(et.b)) < cos) { crease[ev.a] = off.GetVertex(ev.a); crease[ev.b] = off.GetVertex(ev.b); }
            }
            var ro = new RemeshQualityPass(off, reference) { MaxDeviation = delta, EnableCreaseOperations = false }.Compute();
            Assert.Equal(0, ro.CreaseInsertions + ro.CreaseRemovals);
            AssertCleanResult(start, off, reference, ro, delta);
            foreach (var kv in crease) {
                Assert.True(off.IsVertex(kv.Key));
                Assert.Equal(kv.Value, off.GetVertex(kv.Key));
            }
            Assert.True(ro.Worst1PercentAfter < r.Worst1PercentAfter || ro.Worst1PercentAfter < new RemeshQualityPass(new DMesh3(start), reference) { MaxDeviation = delta }.Compute().Worst1PercentAfter,
                "the crease operations are what lifts the needles at the box's edges");
        }

        /// <summary> the operations can be switched off one by one: each switch stops its kind </summary>
        [Fact]
        public void EnableSwitches_StopTheirOperations()
        {
            DMesh3 reference = BoxInput();
            DMesh3 start = Slivered(BoxRemeshed.Value, 60, 1);
            double delta = 1e-3 * reference.CachedBounds.DiagonalLength;
            RemeshQualityResult run(Action<RemeshQualityPass> set)
            {
                var p = new RemeshQualityPass(new DMesh3(start), reference) { MaxDeviation = delta };
                set(p);
                return p.Compute();
            }
            var all = run(p => { });
            Assert.True(all.Flips > 0 && all.Collapses + all.CreaseRemovals > 0 && all.Splits + all.CreaseInsertions > 0 && all.Relocations > 0);
            Assert.Equal(0, run(p => p.EnableFlips = false).Flips);
            var nc = run(p => p.EnableCollapses = false);
            Assert.Equal(0, nc.Collapses + nc.CreaseRemovals);
            var ns = run(p => p.EnableSplits = false);
            Assert.Equal(0, ns.Splits + ns.CreaseInsertions);
            Assert.Equal(0, run(p => p.EnableRelocation = false).Relocations);
            Assert.Equal(RemeshQualityStatus.Unchanged, run(p => p.MaxPasses = 0).Status);
            // with a wide band the corners (three crease edges) still keep their position
            DMesh3 wide = new DMesh3(start);
            new RemeshQualityPass(wide, reference) { MaxDeviation = 0.05 }.Compute();
            var have = new HashSet<Vector3d>(wide.VertexIndices().Select(v => wide.GetVertex(v)));
            foreach (int v in reference.VertexIndices())
                Assert.Contains(reference.GetVertex(v), have);
        }
    }
}
