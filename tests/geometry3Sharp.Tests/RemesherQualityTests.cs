using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.MinTriangleQuality (opt-in): with feature curves kept (PreserveBoundaryAndFeatures, as g3cli remesh), the
    /// remesher left flat triangles (curve vertices smoothed off their curve and projected onto another part of it,
    /// collapses joining two vertices of a straight feature over a third, splits onto a vertex lying on the edge)
    /// and self-intersections (collapses and flips that flip a normal). Faceted cylinders, cones and boxes: their
    /// rims and edges are feature curves.
    /// </summary>
    public class RemesherQualityTests
    {
        static double Q(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            return MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
        }

        static double MeanEdge(DMesh3 m)
        {
            return m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });
        }

        static DMesh3 Cylinder(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Cone(int slices) =>
            new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Taper(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.3f, Height = 1f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Box(int edgeVertices) =>
            new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();

        // as g3cli remesh with its defaults (20 passes, smoothing 0.5, projection onto the input, 30 degree creases)
        static DMesh3 Remesh(DMesh3 input, double edge, double minQuality, bool parallel = true, bool pro = false)
        {
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(edge);
            if (pro)
                for (int k = 0; k < 20; ++k) ((RemesherPro)r).RemeshIteration();
            else
                for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
            return mesh;
        }

        static IEnumerable<(string, DMesh3)> Inputs()
        {
            foreach (int s in new[] { 8, 10, 12, 16, 24 }) {
                yield return ("cylinder " + s, Cylinder(s));
                yield return ("cone " + s, Cone(s));
                yield return ("taper " + s, Taper(s));
            }
            foreach (int e in new[] { 3, 4 })
                yield return ("box " + e, Box(e));
        }

        [Fact]
        public void Default_IsOff()
        {
            Assert.Equal(0, new Remesher(new DMesh3()).MinTriangleQuality);
        }

        [Fact]
        public void Primitives_NoFlatTriangles_FewerSelfIntersections()
        {
            // per case: no flat triangles; in all fewer self-intersecting pairs than without the option. (Not per case:
            // the option creates far fewer intersections during the passes, but which survive the last one is
            // chance, and projection moves across the rim of a flat cap can intersect without flipping a normal; in
            // benchmarks/RemeshQualityBenchmark 9 of 150 primitives end with a few more pairs, see docs/ROADMAP.md)
            int flatBefore = 0, selfBefore = 0, selfAfter = 0, n = 0;
            foreach (var (name, input) in Inputs())
                foreach (double f in new[] { 0.15, 0.3 }) {
                    double edge = f * MeanEdge(input);
                    DMesh3 before = Remesh(input, edge, 0);
                    DMesh3 after = Remesh(input, edge, 0.2);
                    n++;
                    if (before.TriangleIndices().Any(t => Q(before, t) < 1e-3)) flatBefore++;
                    int pairsBefore = MeshRepairMetrics.SelfIntersections(before), pairsAfter = MeshRepairMetrics.SelfIntersections(after);
                    selfBefore += pairsBefore;
                    selfAfter += pairsAfter;
                    // the result: closed, no bowties, oriented consistently, no flat triangles, on the input
                    Assert.True(after.IsClosed(), name);
                    Assert.Empty(MeshIterators.BowtieVertices(after));
                    Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(after));
                    double qmin = after.TriangleIndices().Min(t => Q(after, t));
                    Assert.True(qmin >= 1e-3, $"{name}, edge {f} x mean: a triangle of quality {qmin}");
                    // on the input: within half an edge, except at the apex of a cone, which both variants round off
                    // (a smooth apex is no feature); there not more than an edge farther than without the option
                    var hd = new MeshHausdorffDistance(input, after) { Tolerance = 0.01 * edge };
                    Assert.True(hd.Compute());
                    if (name.StartsWith("cone")) {
                        var hd0 = new MeshHausdorffDistance(input, before) { Tolerance = 0.01 * edge };
                        Assert.True(hd0.Compute());
                        Assert.True(hd.UpperBound < hd0.UpperBound + edge, $"{name}: {hd.UpperBound} from the input, {hd0.UpperBound} without the option");
                    } else
                        Assert.True(hd.UpperBound < 0.5 * edge, $"{name}: {hd.UpperBound} from the input");
                }
            // without the option, these inputs gave flat triangles and more self-intersections
            Assert.True(flatBefore > 0, $"{flatBefore} of {n} without the option had flat triangles");
            Assert.True(selfAfter < selfBefore, $"self-intersecting pairs: {selfAfter} with, {selfBefore} without the option");
        }

        [Fact]
        public void FeatureCurves_StayOnTheInputRims()
        {
            // the cap rims of a cylinder (y = 0 and y = 2) are feature curves: every vertex on a rim stays on the
            // input's rim polygon (a curve vertex smoothed with the surface would leave it)
            DMesh3 input = Cylinder(12);
            DMesh3 mesh = Remesh(input, 0.15 * MeanEdge(input), 0.2);
            var rimSegments = new List<Segment3d>();
            foreach (int e in input.EdgeIndices()) {
                Index2i ev = input.GetEdgeV(e);
                Vector3d a = input.GetVertex(ev.a), b = input.GetVertex(ev.b);
                bool rim = Math.Abs(a.y - b.y) < 1e-12 && (Math.Abs(a.y) < 1e-12 || Math.Abs(a.y - 2) < 1e-12)
                    && new Vector2d(a.x, a.z).Length > 0.5 && new Vector2d(b.x, b.z).Length > 0.5;
                if (rim)
                    rimSegments.Add(new Segment3d(a, b));
            }
            Assert.NotEmpty(rimSegments);
            int onRim = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                if ((Math.Abs(p.y) > 1e-9 && Math.Abs(p.y - 2) > 1e-9) || new Vector2d(p.x, p.z).Length < 0.9) continue;
                double d = rimSegments.Min(s => Math.Sqrt(s.DistanceSquared(p)));
                Assert.True(d < 1e-9, $"{p} is {d} off the rim");
                onRim++;
            }
            Assert.True(onRim > 24, $"{onRim} rim vertices");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Cylinder(12);
            double edge = 0.15 * MeanEdge(input);
            DMesh3 m0 = Remesh(input, edge, 0.2, false);
            DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(input, s), s.ApplyLength(edge), 0.2, false), s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            Assert.True(Math.Abs(back.TriangleCount - m0.TriangleCount) <= 0.03 * m0.TriangleCount, $"{s.Name}: {back.TriangleCount} vs {m0.TriangleCount} triangles");
            double dist = Invariance.VertexDistance(m0, back);
            // (other rounding can tip decisions at ties: about 0.1 edge without the option too)
            Assert.True(dist < 0.25 * edge, $"{s.Name}: remeshed surfaces {dist} apart");
            Assert.True(back.TriangleIndices().Min(t => Q(back, t)) >= 1e-3, s.Name);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            // the guarded moves are checked against the positions before the pass (smoothing and projection are
            // buffered), so the order and the thread timing do not matter
            foreach (DMesh3 input in new[] { Cone(12), Box(4) }) {
                double edge = 0.15 * MeanEdge(input);
                DMesh3 serial = Remesh(input, edge, 0.2, false, pro);
                for (int k = 0; k < 2; ++k)
                    Assert.Equal("", Invariance.Difference(serial, Remesh(input, edge, 0.2, true, pro)));
            }
        }

        // an extruded gear: a CAD-like part with many feature curves and junctions, extra (collinear) points on the
        // flanks, and caps as fans around their centres (long thin triangles)
        static DMesh3 Gear(int teeth, double height, int pointsPerSide)
        {
            var poly = new List<Vector2d>();
            for (int k = 0; k < teeth; ++k) {
                double a0 = 2 * Math.PI * k / teeth, da = 2 * Math.PI / teeth;
                foreach (var (f, r) in new[] { (0.0, 0.8), (0.2, 1.0), (0.5, 1.0), (0.7, 0.8) })
                    poly.Add(r * new Vector2d(Math.Cos(a0 + f * da), Math.Sin(a0 + f * da)));
            }
            var outline = new List<Vector2d>();
            for (int i = 0; i < poly.Count; ++i)
                for (int k = 0; k < pointsPerSide; ++k)
                    outline.Add(Vector2d.Lerp(poly[i], poly[(i + 1) % poly.Count], (double)k / pointsPerSide));
            var m = new DMesh3();
            int n = outline.Count;
            int cb = m.AppendVertex(new Vector3d(0, 0, 0)), ct = m.AppendVertex(new Vector3d(0, 0, height));
            int[] b = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
            int[] t = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, height))).ToArray();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(cb, b[j], b[i]);
                m.AppendTriangle(ct, t[i], t[j]);
                m.AppendTriangle(b[i], b[j], t[j]);
                m.AppendTriangle(b[i], t[j], t[i]);
            }
            return m;
        }

        [Theory]
        [InlineData(12, 0.3, 3)]
        [InlineData(24, 0.2, 3)]
        [InlineData(24, 0.2, 1)]
        public void Gear_NoFlatTrianglesNoSelfIntersections(int teeth, double height, int pointsPerSide)
        {
            // without the option: up to 172 triangles below 1e-3 and 1266 self-intersecting pairs (collapses and
            // flips that flip a normal)
            DMesh3 input = Gear(teeth, height, pointsPerSide);
            Assert.True(input.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            foreach (double f in new[] { 0.5, 1.0, 2.0 }) {
                double edge = f * MeanEdge(input);
                DMesh3 mesh = Remesh(input, edge, 0.2);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, true));
                double qmin = mesh.TriangleIndices().Min(tid => Q(mesh, tid));
                Assert.True(qmin >= 1e-3, $"edge {f} x mean: a triangle of quality {qmin}");
                // without the option: flat triangles and self-intersecting pairs here; with it, none
                DMesh3 before = Remesh(input, edge, 0);
                Assert.True(MeshRepairMetrics.SelfIntersections(mesh) <= MeshRepairMetrics.SelfIntersections(before));
            }
        }

        [Fact]
        public void EdgeOverAVertexOnIt_NotSplit()
        {
            // a free edge A-B with a vertex M on it (the flat triangle A-M-B, as a remeshed straight feature can
            // leave): splitting A-B at its midpoint would put the new vertex onto M, an edge of length zero
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(2, 0, 0));
            int mid = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(1, 1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, mid);
            var r = new Remesher(m) {
                MinTriangleQuality = 0.2, EnableFlips = false, EnableCollapses = false, EnableSmoothing = false
            };
            r.SetTargetEdgeLength(1.2);       // MaxEdgeLength 1.6 < |AB| = 2
            r.BasicRemeshPass();
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                Assert.True(m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) > 1e-9, $"edge {ev} has length zero");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ProjectionThatFlipsATriangle_Rejected(bool pro)
        {
            // a hexagon fan with its rim pinned; the target is the plane x = 1, beside it: projecting the centre
            // there would put it outside the rim and flip the triangles on the far side
            var m = new DMesh3();
            int center = m.AppendVertex(Vector3d.Zero);
            int[] rim = Enumerable.Range(0, 6).Select(k => m.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 3), Math.Sin(k * Math.PI / 3), 0))).ToArray();
            for (int k = 0; k < 6; ++k)
                m.AppendTriangle(center, rim[k], rim[(k + 1) % 6]);
            var plane = new DMesh3();
            int p0 = plane.AppendVertex(new Vector3d(1, -5, -5)), p1 = plane.AppendVertex(new Vector3d(1, 5, -5));
            int p2 = plane.AppendVertex(new Vector3d(1, 5, 5)), p3 = plane.AppendVertex(new Vector3d(1, -5, 5));
            plane.AppendTriangle(p0, p1, p2);
            plane.AppendTriangle(p0, p2, p3);
            Remesher r = pro ? new RemesherPro(m) : new Remesher(m);
            r.MinTriangleQuality = 0.2;
            r.EnableFlips = r.EnableCollapses = r.EnableSplits = r.EnableSmoothing = false;
            MeshConstraintUtil.FixAllBoundaryEdges(r);
            r.SetProjectionTarget(new MeshProjectionTarget(plane));
            if (pro)
                ((RemesherPro)r).RemeshIteration();
            else
                r.BasicRemeshPass();
            foreach (int tid in m.TriangleIndices())
                Assert.True(m.GetTriNormal(tid).z > 0, $"triangle {tid} flipped");
            // the full step would put it onto a rim vertex: the half step is taken (the next pass gets closer)
            Assert.Equal(new Vector3d(0.5, 0, 0), m.GetVertex(center));
        }

        // records, for every buffered pass, the (not flat) triangles whose orientation the pass reversed
        class FlipCountingRemesher : Remesher
        {
            public int Flipped;
            public FlipCountingRemesher(DMesh3 m) : base(m) { }
            protected override void ApplyVertexBuffer(bool bParallel)
            {
                var before = new Dictionary<int, Vector3d>();
                foreach (int t in Mesh.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    Mesh.GetTriVertices(t, ref a, ref b, ref c);
                    if (MeshRefinerBase.TriangleQuality(ref a, ref b, ref c) >= 1e-6)
                        before[t] = (b - a).Cross(c - a);
                }
                base.ApplyVertexBuffer(bParallel);
                foreach (var kv in before) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    Mesh.GetTriVertices(kv.Key, ref a, ref b, ref c);
                    if (!((b - a).Cross(c - a).Dot(kv.Value) > 0))
                        Flipped++;
                }
            }
        }

        [Fact]
        public void BufferedPasses_FlipNoTriangle()
        {
            // each move is checked against the old positions of its neighbours, but neighbours move together: the
            // buffered smoothing and projection passes revert the moves of triangles they would still flip
            int flipped = 0, flippedWithout = 0;
            foreach (var (name, input) in Inputs())
                foreach (double quality in new[] { 0.0, 0.2 }) {
                    DMesh3 mesh = new DMesh3(input);
                    var r = new FlipCountingRemesher(mesh) { SmoothSpeedT = 0.5, MinTriangleQuality = quality };
                    r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
                    MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
                    r.SetTargetEdgeLength(0.15 * MeanEdge(input));
                    for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
                    if (quality > 0) flipped += r.Flipped; else flippedWithout += r.Flipped;
                }
            Assert.Equal(0, flipped);
            Assert.True(flippedWithout > 0, "without the option the passes flip triangles here");
        }

        [Fact]
        public void PrimitivesWithoutApex_AllVerticesOnTheInput()
        {
            // a rejected projection is retried with a shorter step: the vertices end on the input (an unguarded
            // rejection left vertices up to 3 edges inside a cone)
            foreach (var (name, input) in Inputs().Where(x => x.Item1.StartsWith("cone") == false)) {
                double edge = 0.15 * MeanEdge(input);
                DMesh3 mesh = Remesh(input, edge, 0.2);
                var spatial = new DMeshAABBTree3(input, true);
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d p = mesh.GetVertex(v);
                    double d = Math.Sqrt(MeshQueries.TriDistanceSqr(input, spatial.FindNearestTriangle(p), p));
                    Assert.True(d < 1e-6 * edge, $"{name}: vertex {v} is {d / edge} edges off the input");
                }
            }
        }

        [Fact]
        public void SmoothingMove_BelowTheQuality_Rejected()
        {
            // a fan whose centre, smoothed towards the average of its (pinned) rim, would make a sliver: the rim is
            // a thin rhombus, the centre starts at a good position near one end
            var m = new DMesh3();
            int center = m.AppendVertex(new Vector3d(0.6, 0, 0));
            int[] rim = { m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0.6, 0.3, 0)),
                m.AppendVertex(new Vector3d(-6, 0, 0)), m.AppendVertex(new Vector3d(0.6, -0.3, 0)) };
            for (int k = 0; k < 4; ++k)
                m.AppendTriangle(center, rim[k], rim[(k + 1) % 4]);
            Func<double, double> qminAfter = (quality) => {
                DMesh3 c = new DMesh3(m);
                var r = new Remesher(c) { MinTriangleQuality = quality, SmoothSpeedT = 1, EnableFlips = false, EnableCollapses = false, EnableSplits = false };
                MeshConstraintUtil.FixAllBoundaryEdges(r);
                r.BasicRemeshPass();
                return c.TriangleIndices().Min(t => Q(c, t));
            };
            double qmin0 = m.TriangleIndices().Min(t => Q(m, t));
            Assert.True(qminAfter(0) < 0.2 && qminAfter(0) < qmin0, $"without the option the move makes a poor triangle ({qminAfter(0)})");
            Assert.Equal(qmin0, qminAfter(0.2));
        }

        [Fact]
        public void FlatTriangle_ItsVertexMayMove()
        {
            // a fan with its centre exactly on a rim edge (a flat triangle, no orientation): the smoothing move
            // that repairs it is not rejected as a flip
            var m = new DMesh3();
            int center = m.AppendVertex(new Vector3d(0.5, 0, 0));
            int[] rim = { m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)),
                m.AppendVertex(new Vector3d(1, 1, 0)), m.AppendVertex(new Vector3d(0, 1, 0)) };
            for (int k = 0; k < 4; ++k)
                m.AppendTriangle(center, rim[k], rim[(k + 1) % 4]);
            var r = new Remesher(m) { MinTriangleQuality = 0.2, SmoothSpeedT = 0.5, EnableFlips = false, EnableCollapses = false, EnableSplits = false };
            MeshConstraintUtil.FixAllBoundaryEdges(r);
            for (int k = 0; k < 5; ++k) r.BasicRemeshPass();
            Assert.True(m.TriangleIndices().Min(t => Q(m, t)) > 0.5, $"{m.GetVertex(center)}");
        }

        [Fact]
        public void CurveVertex_SmoothedAlongItsCurve()
        {
            // a vertex on a feature curve moves towards the middle of its two curve neighbours (and stays on the
            // curve), not towards the average of all its neighbours (here shifted along the curve: the outer rows
            // are offset), and an edge with another constraint does not count as a curve edge: a strip whose middle
            // row is a crease
            var m = new DMesh3();
            int[,] v = new int[5, 3];
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 5; ++i) {
                    double x = (i == 2 && j == 1) ? 1.3 : (j == 1 ? i : i + 0.45);       // the curve vertex, off the middle
                    v[i, j] = m.AppendVertex(new Vector3d(x, j, j == 1 ? 0 : 0.8));
                }
            for (int j = 0; j < 2; ++j)
                for (int i = 0; i < 4; ++i) {
                    m.AppendTriangle(v[i, j], v[i + 1, j], v[i + 1, j + 1]);
                    m.AppendTriangle(v[i, j], v[i + 1, j + 1], v[i, j + 1]);
                }
            var r = new Remesher(m) { MinTriangleQuality = 0.2, SmoothSpeedT = 1, EnableFlips = false, EnableCollapses = false, EnableSplits = false };
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.Constraints.SetOrUpdateEdgeConstraint(m.FindEdge(v[2, 1], v[2, 2]), EdgeConstraint.NoFlips);
            r.BasicRemeshPass();
            Vector3d p = m.GetVertex(v[2, 1]);
            Assert.True(p.Distance(new Vector3d(2, 1, 0)) < 1e-9, $"{p}");
        }

        [Fact]
        public void Cap_FlippedWhenThatDoublesTheQuality()
        {
            // a cap (its apex near the long edge) on an edge whose flip gives two good triangles, with valences that
            // would not ask for the flip; and a cap in a quad that is not convex (the new diagonal outside it, the
            // flip would turn a triangle over): not flipped
            Func<double, bool> flipped = (dx) => {
                var m = new DMesh3();
                int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(2, 0, 0));
                int c = m.AppendVertex(new Vector3d(1, 0.05, 0)), d = m.AppendVertex(new Vector3d(dx, dx > 2 ? -0.1 : -1, 0));
                m.AppendTriangle(a, b, c);
                m.AppendTriangle(b, a, d);
                var r = new Remesher(m) { MinTriangleQuality = 0.2, EnableCollapses = false, EnableSplits = false, EnableSmoothing = false };
                r.BasicRemeshPass();
                return m.FindEdge(a, b) == DMesh3.InvalidID;
            };
            Assert.True(flipped(1), "the cap is flipped away");
            Assert.False(flipped(5), "no flip that turns a triangle over");
        }

        [Theory]
        [InlineData(12, 0.3, 3)]
        [InlineData(24, 0.2, 3)]
        public void RemesherPro_Gear_NoFlatTrianglesNoSelfIntersections(int teeth, double height, int pointsPerSide)
        {
            // RemesherPro processes only queued edges: the edges around a vertex whose move was rejected are queued
            DMesh3 input = Gear(teeth, height, pointsPerSide);
            foreach (double f in new[] { 0.5, 1.0, 2.0 }) {
                DMesh3 mesh = Remesh(input, f * MeanEdge(input), 0.2, true, true);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, true));
                Assert.True(mesh.TriangleIndices().Min(tid => Q(mesh, tid)) >= 1e-3, $"edge {f} x mean");
            }
        }

        [Fact]
        public void RemesherPro_RejectedMove_QueuesItsEdges()
        {
            // RemesherPro processes only queued edges: when every step of a projection would flip a triangle (the
            // target plane far beside a pinned hexagon fan), the six edges of the vertex are queued for the next
            // iteration (eg to flip them)
            var m = new DMesh3();
            int center = m.AppendVertex(Vector3d.Zero);
            int[] rim = Enumerable.Range(0, 6).Select(k => m.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 3), Math.Sin(k * Math.PI / 3), 0))).ToArray();
            for (int k = 0; k < 6; ++k)
                m.AppendTriangle(center, rim[k], rim[(k + 1) % 6]);
            var plane = new DMesh3();
            int p0 = plane.AppendVertex(new Vector3d(10, -50, -50)), p1 = plane.AppendVertex(new Vector3d(10, 50, -50));
            int p2 = plane.AppendVertex(new Vector3d(10, 50, 50)), p3 = plane.AppendVertex(new Vector3d(10, -50, 50));
            plane.AppendTriangle(p0, p1, p2);
            plane.AppendTriangle(p0, p2, p3);
            var r = new RemesherPro(m) { MinTriangleQuality = 0.2 };
            r.EnableFlips = r.EnableCollapses = r.EnableSplits = r.EnableSmoothing = false;
            MeshConstraintUtil.FixAllBoundaryEdges(r);
            r.SetProjectionTarget(new MeshProjectionTarget(plane));
            r.RemeshIteration();
            Assert.Equal(Vector3d.Zero, m.GetVertex(center));
            Assert.True(r.QueuedEdgeCount >= 6, $"{r.QueuedEdgeCount} edges queued");
        }

        [Fact]
        public void RemesherPro_NoFlatTriangles()
        {
            foreach (var (name, input) in Inputs()) {
                double edge = 0.15 * MeanEdge(input);
                DMesh3 mesh = Remesh(input, edge, 0.2, true, true);
                Assert.True(mesh.IsClosed(), name);
                double qmin = mesh.TriangleIndices().Min(t => Q(mesh, t));
                Assert.True(qmin >= 1e-3, $"{name}: a triangle of quality {qmin}");
            }
        }
    }
}
