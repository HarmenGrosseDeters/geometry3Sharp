using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshConstraintUtil.PinSharpVertices: the apex of a cone with 12 or more slices is no junction of creases (its
    /// side edges are too flat), so PreserveBoundaryAndFeatures does not keep it and the remeshers round it off by
    /// 1.4..2.8 target edge lengths (benchmarks/RemeshQualityBenchmark). Pinned by its angle deficit, it stays.
    /// </summary>
    public class PinSharpVerticesTests
    {
        static double MeanEdge(DMesh3 m)
        {
            return m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });
        }

        static DMesh3 Cone(int slices, double height = 2) =>
            new ConeGenerator() { BaseRadius = 1f, Height = (float)height, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Cylinder(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Box() =>
            new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), EdgeVertices = 3, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Sphere() =>
            new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();

        static Vector3d Apex(DMesh3 cone) => cone.Vertices().OrderByDescending(v => v.y).First();

        // as g3cli remesh (20 passes, smoothing 0.5, projection, 30 degree creases), with the sharp vertices pinned
        static DMesh3 Remesh(DMesh3 input, double edge, double minQuality, bool pin, bool parallel = true, bool pro = false)
        {
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            if (pin)
                MeshConstraintUtil.PinSharpVertices(r);
            r.SetTargetEdgeLength(edge);
            for (int k = 0; k < 20; ++k) {
                if (pro) ((RemesherPro)r).RemeshIteration();
                else r.BasicRemeshPass();
            }
            return mesh;
        }

        [Fact]
        public void Cone_ApexPinned_OthersNot()
        {
            foreach (int s in new[] { 12, 16, 24, 32 }) {
                DMesh3 cone = Cone(s);
                var cons = new MeshConstraints();
                Assert.Equal(1, MeshConstraintUtil.PinSharpVertices(cons, cone));
                int apex = cone.VertexIndices().First(v => cone.GetVertex(v) == Apex(cone));
                Assert.True(cons.GetVertexConstraint(apex).Fixed);
            }
            // smooth or creased inputs: nothing to pin (box corners are junctions of creases, already fixed)
            Assert.Equal(0, MeshConstraintUtil.PinSharpVertices(new MeshConstraints(), Cylinder(16)));
            Assert.Equal(0, MeshConstraintUtil.PinSharpVertices(new MeshConstraints(), Sphere()));
            var boxCons = new MeshConstraints();
            DMesh3 box = Box();
            MeshConstraintUtil.PreserveBoundaryAndFeatures(boxCons, box, 30);
            Assert.Equal(0, MeshConstraintUtil.PinSharpVertices(boxCons, box));
        }

        [Fact]
        public void Threshold_ByTheConeAngle()
        {
            // a flat cone (height 0.3: half angle 73.3 degrees, deficit 360 (1 - sin 73.3) = 15.2 degrees); the rim is
            // a feature curve (its vertices have a deficit of 29 degrees from the crease) and left to its constraint
            DMesh3 flat = Cone(24, 0.3);
            MeshConstraints creases() { var c = new MeshConstraints(); MeshConstraintUtil.PreserveBoundaryAndFeatures(c, flat, 30); return c; }
            Assert.Equal(0, MeshConstraintUtil.PinSharpVertices(creases(), flat));
            Assert.Equal(1, MeshConstraintUtil.PinSharpVertices(creases(), flat, 15));
            Assert.Equal(0, MeshConstraintUtil.PinSharpVertices(creases(), flat, 16));
            // without the curve constraints the rim vertices count too
            Assert.Equal(25, MeshConstraintUtil.PinSharpVertices(new MeshConstraints(), flat, 15));
        }

        [Fact]
        public void Remeshed_ApexKept()
        {
            // without pinning the apex is rounded off by 1.4..2.8 target edges (benchmarks/RemeshQualityBenchmark)
            foreach (int s in new[] { 12, 16, 24, 32 })
                foreach (double f in new[] { 0.1, 0.3, 0.6 })
                    foreach (double q in new[] { 0, 0.2 }) {
                        DMesh3 input = Cone(s);
                        double edge = f * MeanEdge(input);
                        Vector3d apex = Apex(input);
                        DMesh3 mesh = Remesh(input, edge, q, true);
                        string name = $"cone{s}_x{f} q{q}";
                        Assert.Contains(mesh.Vertices(), v => v == apex);
                        Assert.True(mesh.IsClosed(), name);
                        Assert.Empty(MeshIterators.BowtieVertices(mesh));
                        Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
                        Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
                        Assert.True(mesh.Vertices().All(v => v.IsFinite), name);
                        var hd = new MeshHausdorffDistance(input, mesh) { Tolerance = 0.01 * edge };
                        Assert.True(hd.Compute());
                        Assert.True(hd.UpperBound < 0.5 * edge, $"{name}: {hd.UpperBound / edge} target from the input");
                        if (f == 0.1 && q == 0) {
                            // the reason: without the pin it is rounded off
                            var hd0 = new MeshHausdorffDistance(input, Remesh(input, edge, q, false)) { Tolerance = 0.01 * edge };
                            Assert.True(hd0.Compute());
                            Assert.True(hd0.UpperBound > edge, $"{name}: {hd0.UpperBound / edge} target without the pin");
                        }
                    }
        }

        [Fact]
        public void RemesherPro_ApexKept()
        {
            DMesh3 input = Cone(24);
            DMesh3 mesh = Remesh(input, 0.15 * MeanEdge(input), 0.2, true, pro: true);
            Assert.Contains(mesh.Vertices(), v => v == Apex(input));
            Assert.True(mesh.IsClosed());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Cone(16);
            DMesh3 moved = Invariance.Transformed(input, s);
            // the same vertex pinned (angles only)
            var c0 = new MeshConstraints(); var c1 = new MeshConstraints();
            Assert.Equal(1, MeshConstraintUtil.PinSharpVertices(c0, input));
            Assert.Equal(1, MeshConstraintUtil.PinSharpVertices(c1, moved));
            Assert.Equal(c0.VertexConstraintsItr().Select(kv => kv.Key).ToArray(), c1.VertexConstraintsItr().Select(kv => kv.Key).ToArray());
            double edge = 0.15 * MeanEdge(input);
            DMesh3 m0 = Remesh(input, edge, 0.2, true, false);
            DMesh3 back = Invariance.MappedBack(Remesh(moved, s.ApplyLength(edge), 0.2, true, false), s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            Assert.True(back.Vertices().Min(v => v.Distance(Apex(input))) < 1e-9 * Math.Max(1, s.Resolution(3) / MathUtil.Epsilon), s.Name);
        }

        [Fact]
        public void ParallelEqualsSerial_RepeatedRuns()
        {
            DMesh3 input = Cone(24);
            double edge = 0.15 * MeanEdge(input);
            DMesh3 serial = Remesh(input, edge, 0.2, true, false);
            for (int k = 0; k < 2; ++k)
                Assert.Equal("", Invariance.Difference(serial, Remesh(input, edge, 0.2, true, true)));
        }
    }
}
