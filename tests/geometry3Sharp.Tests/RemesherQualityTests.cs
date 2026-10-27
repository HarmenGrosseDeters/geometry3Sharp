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
            int flatBefore = 0, selfBefore = 0, selfAfter = 0, n = 0;
            foreach (var (name, input) in Inputs())
                foreach (double f in new[] { 0.15, 0.3 }) {
                    double edge = f * MeanEdge(input);
                    DMesh3 before = Remesh(input, edge, 0);
                    DMesh3 after = Remesh(input, edge, 0.2);
                    n++;
                    if (before.TriangleIndices().Any(t => Q(before, t) < 1e-3)) flatBefore++;
                    if (MeshRepairMetrics.HasSelfIntersections(before)) selfBefore++;
                    if (MeshRepairMetrics.HasSelfIntersections(after)) selfAfter++;
                    // the result: closed, no bowties, oriented consistently, no flat triangles, on the input
                    Assert.True(after.IsClosed(), name);
                    Assert.Empty(MeshIterators.BowtieVertices(after));
                    Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(after));
                    double qmin = after.TriangleIndices().Min(t => Q(after, t));
                    Assert.True(qmin >= 1e-3, $"{name}, edge {f} x mean: a triangle of quality {qmin}");
                    // (not at the apex of a cone: a smooth apex is no feature, both variants round it off)
                    if (name.StartsWith("cone") == false) {
                        var hd = new MeshHausdorffDistance(input, after) { Tolerance = 0.01 * edge };
                        Assert.True(hd.Compute());
                        Assert.True(hd.UpperBound < 0.5 * edge, $"{name}: {hd.UpperBound} from the input");
                    }
                }
            // without the option, these inputs gave flat triangles and more self-intersections
            Assert.True(flatBefore > 0, $"{flatBefore} of {n} without the option had flat triangles");
            Assert.True(selfAfter < selfBefore, $"self-intersecting results: {selfAfter} with, {selfBefore} without the option");
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
