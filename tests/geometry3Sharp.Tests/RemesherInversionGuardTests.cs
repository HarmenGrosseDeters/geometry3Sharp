using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.RejectInvertingPasses: with a target edge length far above the wall thickness, the remeshers fold the
    /// walls through each other and the signed volume can drift through zero, so a closed mesh ends inside out
    /// (Thingi10K 135363: walls 0.75, target 11.6), with and without MinTriangleQuality. The guard rolls back every
    /// pass after which a closed component is inside out and counts it in InvertingPassesRejected.
    /// </summary>
    public class RemesherInversionGuardTests
    {
        // closed thin-walled tube (outer radius R, wall w): outer and inner wall joined by annular rims at both ends
        static DMesh3 Tube(int slices, int rings, double R, double w, double H)
        {
            var m = new DMesh3();
            int[,] o = new int[rings + 1, slices], i = new int[rings + 1, slices];
            for (int r = 0; r <= rings; ++r)
                for (int s = 0; s < slices; ++s) {
                    double a = 2 * Math.PI * s / slices, z = H * r / rings;
                    o[r, s] = m.AppendVertex(new Vector3d(R * Math.Cos(a), R * Math.Sin(a), z));
                    i[r, s] = m.AppendVertex(new Vector3d((R - w) * Math.Cos(a), (R - w) * Math.Sin(a), z));
                }
            for (int s = 0; s < slices; ++s) {
                int s1 = (s + 1) % slices;
                for (int r = 0; r < rings; ++r) {
                    m.AppendTriangle(o[r, s], o[r, s1], o[r + 1, s1]); m.AppendTriangle(o[r, s], o[r + 1, s1], o[r + 1, s]);
                    m.AppendTriangle(i[r, s], i[r + 1, s1], i[r, s1]); m.AppendTriangle(i[r, s], i[r + 1, s], i[r + 1, s1]);
                }
                m.AppendTriangle(o[rings, s], o[rings, s1], i[rings, s1]); m.AppendTriangle(o[rings, s], i[rings, s1], i[rings, s]);
                m.AppendTriangle(o[0, s], i[0, s1], o[0, s1]); m.AppendTriangle(o[0, s], i[0, s], i[0, s1]);
            }
            return m;
        }

        // the tube of Thingi10K 135363 (radius 6.9, walls 0.75, height 33) at its target edge length 11.6
        static DMesh3 Tube135363() => Tube(48, 1, 6.9, 0.75, 33);
        const double Target135363 = 11.6;

        // as g3cli remesh (20 passes, smoothing 0.5, projection onto the input, 30 degree creases)
        static Remesher Remesh(DMesh3 mesh, DMesh3 input, double edge, double minQuality, bool guard, bool pro,
            int passes = 20, bool parallel = true)
        {
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            r.RejectInvertingPasses = guard;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(edge);
            for (int k = 0; k < passes; ++k) {
                if (pro)
                    ((RemesherPro)r).RemeshIteration();
                else
                    r.BasicRemeshPass();
            }
            return r;
        }

        static bool InsideOut(DMesh3 mesh) => MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0;

        [Fact]
        public void Default_IsOn()
        {
            var r = new Remesher(new DMesh3());
            Assert.True(r.RejectInvertingPasses);
            Assert.Equal(0, r.InvertingPassesRejected);
            Assert.True(new RemesherPro(new DMesh3()).RejectInvertingPasses);
            // its loop lists would not be rolled back
            DMesh3 cyl = new OpenCylinderGenerator() { Slices = 8, NoSharedVertices = false }.Generate().MakeDMesh();
            Assert.False(new EdgeLoopRemesher(cyl, new MeshBoundaryLoops(cyl).Loops[0]).RejectInvertingPasses);
        }

        [Fact]
        public void ClosedComponentSigns_CountsBySign()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            Assert.Equal(new Index2i(1, 0), Remesher.ClosedComponentSigns(sphere));

            DMesh3 reversed = new DMesh3(sphere);
            reversed.ReverseOrientation();
            Assert.Equal(new Index2i(0, 1), Remesher.ClosedComponentSigns(reversed));

            // a sphere with a cavity: the inner, reversed sphere counts as negative
            DMesh3 cavity = new DMesh3(sphere);
            DMesh3 inner = new DMesh3(reversed);
            MeshTransforms.Scale(inner, 0.5, 0.5, 0.5);
            new MeshEditor(cavity).AppendMesh(inner);
            Assert.Equal(new Index2i(1, 1), Remesher.ClosedComponentSigns(cavity));

            // open: no closed component
            DMesh3 open = new DMesh3(sphere);
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.Equal(new Index2i(0, 0), Remesher.ClosedComponentSigns(open));

            // closed, but without a volume (a double-sided triangle)
            var flat = new DMesh3();
            int a = flat.AppendVertex(Vector3d.Zero), b = flat.AppendVertex(Vector3d.AxisX), c = flat.AppendVertex(Vector3d.AxisY);
            flat.AppendTriangle(a, b, c);
            flat.AppendTriangle(a, c, b);
            Assert.Equal(new Index2i(0, 0), Remesher.ClosedComponentSigns(flat));

            // a flat tetrahedron in a tilted plane: its volume is a rounding error (not 0), within the tolerance
            Vector3d P(double x, double y) => new Vector3d(x, y, 0.3 * x + 0.7 * y);
            var tet = new DMesh3();
            int v0 = tet.AppendVertex(P(0.1, 0.2)), v1 = tet.AppendVertex(P(1.3, 0.1)), v2 = tet.AppendVertex(P(0.4, 1.7)), v3 = tet.AppendVertex(P(0.6, 0.7));
            tet.AppendTriangle(v0, v1, v2); tet.AppendTriangle(v0, v3, v1); tet.AppendTriangle(v1, v3, v2); tet.AppendTriangle(v2, v3, v0);
            Assert.True(tet.IsClosed());
            Assert.NotEqual(0, MeshMeasurements.VolumeArea(tet, tet.TriangleIndices(), tet.GetVertex).x);
            Assert.Equal(new Index2i(0, 0), Remesher.ClosedComponentSigns(tet));
        }

        // every thin-walled tube of benchmarks/RemeshQualityBenchmark: without the guard some end inside out, with
        // it none, and every case that inverted without it reports rolled-back passes
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThinWalledTubes_NoInsideOutResult(bool pro)
        {
            int invertedWithout = 0;
            foreach (int slices in new[] { 16, 32, 64 })
                foreach (double w in new[] { 0.05, 0.1 }) {
                    DMesh3 input = Tube(slices, 4, 1, w, 3);
                    Assert.False(InsideOut(input));
                    foreach (double target in new[] { 0.5, 0.8, 1.2, 1.6 })
                        foreach (double q in new[] { 0, 0.2 }) {
                            string name = $"tube{slices}_w{w}_t{target}_q{q}";
                            DMesh3 without = new DMesh3(input), with = new DMesh3(input);
                            Remesh(without, input, target, q, false, pro);
                            Remesher r = Remesh(with, input, target, q, true, pro);
                            Assert.False(InsideOut(with), name);
                            Assert.True(with.IsClosed(), name);
                            if (InsideOut(without)) {
                                invertedWithout++;
                                Assert.True(r.InvertingPassesRejected > 0, name);
                            }
                        }
                }
            Assert.True(invertedWithout > 0);
        }

        [Theory]
        [InlineData(false, 0.0)]
        [InlineData(false, 0.2)]
        [InlineData(true, 0.0)]
        [InlineData(true, 0.2)]
        public void Tube135363_NotInsideOut(bool pro, double minQuality)
        {
            DMesh3 input = Tube135363();
            DMesh3 without = new DMesh3(input), with = new DMesh3(input);
            Remesh(without, input, Target135363, minQuality, false, pro);
            Assert.True(InsideOut(without));
            Remesher r = Remesh(with, input, Target135363, minQuality, true, pro);
            Assert.False(InsideOut(with));
            Assert.InRange(r.InvertingPassesRejected, 1, 20);
        }

        // a cavity (the reversed 135363 tube inside a box) that the remeshers turn inside out: one more positive and
        // one fewer negative component
        [Theory]
        [InlineData(false, 0.0)]
        [InlineData(true, 0.0)]
        [InlineData(true, 0.2)]
        public void Cavity_NotTurnedInsideOut(bool pro, double minQuality)
        {
            DMesh3 input = new GridBox3Generator() { Box = new Box3d(new Vector3d(0, 0, 16.5), new Vector3d(30, 30, 40)), EdgeVertices = 3, NoSharedVertices = false }.Generate().MakeDMesh();
            DMesh3 cavity = Tube(24, 4, 6.9, 0.75, 33);
            cavity.ReverseOrientation();
            new MeshEditor(input).AppendMesh(cavity);
            Assert.Equal(new Index2i(1, 1), Remesher.ClosedComponentSigns(input));
            Assert.False(InsideOut(input));

            DMesh3 without = new DMesh3(input), with = new DMesh3(input);
            Remesh(without, input, Target135363, minQuality, false, pro);
            Assert.Equal(new Index2i(2, 0), Remesher.ClosedComponentSigns(without));
            Assert.True(InsideOut(without));
            Remesher r = Remesh(with, input, Target135363, minQuality, true, pro);
            Assert.Equal(new Index2i(1, 1), Remesher.ClosedComponentSigns(with));
            Assert.False(InsideOut(with));
            Assert.True(r.InvertingPassesRejected > 0);
        }

        // a part and a cavity that change sign in the same pass (one + to -, one - to +) leave the counts of positive
        // and negative components unchanged: the components are compared one by one
        [Fact]
        public void PartAndCavityInvertedInOnePass_RolledBack()
        {
            DMesh3 input = new GridBox3Generator() { Box = new Box3d(new Vector3d(0, 0, 16.5), new Vector3d(30, 30, 40)), EdgeVertices = 3, NoSharedVertices = false }.Generate().MakeDMesh();
            DMesh3 cavity = Tube(24, 4, 6.9, 0.75, 33);
            cavity.ReverseOrientation();
            new MeshEditor(input).AppendMesh(cavity);
            DMesh3 part = Tube(24, 4, 6.9, 0.75, 33);
            MeshTransforms.Translate(part, new Vector3d(200, 0, 0));
            new MeshEditor(input).AppendMesh(part);
            Assert.Equal(new Index2i(2, 1), Remesher.ClosedComponentSigns(input));

            // the pass (without the guard) after which both are inside out, the counts unchanged
            int passes = 1;
            for (; passes <= 20; ++passes) {
                DMesh3 probe = new DMesh3(input);
                Remesh(probe, input, Target135363, 0, false, false, passes);
                if (MeshPostRepairCleanup.FindInvertedComponents(probe).Count == 2 && Remesher.ClosedComponentSigns(probe) == new Index2i(2, 1))
                    break;
            }
            Assert.True(passes <= 20);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Remesh(mesh, input, Target135363, 0, false, false, passes - 1);
            Assert.False(InsideOut(mesh));
            r.RejectInvertingPasses = true;
            r.BasicRemeshPass();
            Assert.Equal(1, r.InvertingPassesRejected);
            Assert.False(InsideOut(mesh));
        }

        // a coarse remesh flattens a 12-slice cone into a double layer of zero volume (pass 17); as an outer surface
        // (one fewer positive component; RemesherPro keeps this one) and as a cavity in a box (one fewer negative
        // component)
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void FlattenedComponent_RolledBack(bool asCavity, bool pro)
        {
            DMesh3 cone = new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = 12, NoSharedVertices = false }.Generate().MakeDMesh();
            double edge = 0.6 * cone.EdgeIndices().Average(e => { Index2i ev = cone.GetEdgeV(e); return cone.GetVertex(ev.a).Distance(cone.GetVertex(ev.b)); });
            DMesh3 input = cone;
            if (asCavity) {
                input = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(4, 4, 4)), EdgeVertices = 2, NoSharedVertices = false }.Generate().MakeDMesh();
                DMesh3 cavity = new DMesh3(cone);
                cavity.ReverseOrientation();
                new MeshEditor(input).AppendMesh(cavity);
            }
            Index2i signs = Remesher.ClosedComponentSigns(input);
            Assert.Equal(asCavity ? new Index2i(1, 1) : new Index2i(1, 0), signs);

            DMesh3 without = new DMesh3(input), with = new DMesh3(input);
            Remesh(without, input, edge, 0, false, pro);
            Assert.Equal(asCavity ? new Index2i(1, 0) : new Index2i(0, 0), Remesher.ClosedComponentSigns(without));
            Remesher r = Remesh(with, input, edge, 0, true, pro);
            Assert.Equal(signs, Remesher.ClosedComponentSigns(with));
            Assert.True(r.InvertingPassesRejected > 0);
        }

        // a pass rolled back leaves the mesh (ids, coordinates, edges) and the constraints as they were before it
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RolledBackPass_RestoresMeshAndConstraints(bool pro)
        {
            DMesh3 input = Tube135363();
            // the first pass that turns the mesh inside out (without the guard)
            int passes = 1;
            for (; passes <= 20; ++passes) {
                DMesh3 probe = new DMesh3(input);
                Remesh(probe, input, Target135363, 0, false, pro, passes);
                if (InsideOut(probe))
                    break;
            }
            Assert.True(passes <= 20);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Remesh(mesh, input, Target135363, 0, true, pro, passes - 1);
            Assert.Equal(0, r.InvertingPassesRejected);
            DMesh3 before = new DMesh3(mesh);
            var vc = Enumerable.Range(0, mesh.MaxVertexID).Select(v => r.Constraints.GetVertexConstraint(v)).ToList();
            var ec = Enumerable.Range(0, mesh.MaxEdgeID).Select(e => r.Constraints.GetEdgeConstraint(e)).ToList();

            for (int k = 1; k <= 2; ++k) {      // the pass after a rollback is rolled back again (same state)
                if (pro)
                    ((RemesherPro)r).RemeshIteration();
                else
                    r.BasicRemeshPass();
                Assert.Equal(k, r.InvertingPassesRejected);
                Assert.Equal(0, r.ModifiedEdgesLastPass);      // the mesh did not change
                Assert.Equal("", Invariance.Difference(before, mesh));
                Assert.Equal(before.MaxEdgeID, mesh.MaxEdgeID);
                for (int e = 0; e < mesh.MaxEdgeID; ++e) {
                    Assert.Equal(before.IsEdge(e), mesh.IsEdge(e));
                    if (mesh.IsEdge(e))
                        Assert.Equal(before.GetEdge(e), mesh.GetEdge(e));
                }
                // (the ids the pass created have no constraint either)
                for (int v = 0; v < mesh.MaxVertexID + 64; ++v)
                    Assert.True(r.Constraints.GetVertexConstraint(v).Equals(v < vc.Count ? vc[v] : VertexConstraint.Unconstrained), $"vertex {v}");
                for (int e = 0; e < mesh.MaxEdgeID + 64; ++e)
                    Assert.True(r.Constraints.GetEdgeConstraint(e).Equals(e < ec.Count ? ec[e] : EdgeConstraint.Unconstrained), $"edge {e}");
            }
        }

        // without an inversion the guard changes nothing (closed and open meshes, with and without MinTriangleQuality)
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NoInversion_SameResultAsWithoutTheGuard(bool pro)
        {
            DMesh3 cylinder = new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 1f, Height = 2f, Slices = 12, NoSharedVertices = false }.Generate().MakeDMesh();
            DMesh3 open = new DMesh3(cylinder);
            foreach (int t in open.TriangleIndices().Where(t => open.GetTriCentroid(t).y > 0.99).ToList())
                open.RemoveTriangle(t);         // without the top cap
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            foreach (DMesh3 input in new[] { cylinder, new DMesh3(open, true), sphere })
                foreach (double q in new[] { 0, 0.2 })
                    foreach (double edge in new[] { 0.15, 0.6 }) {
                        DMesh3 without = new DMesh3(input), with = new DMesh3(input);
                        Remesh(without, input, edge, q, false, pro);
                        Remesher r = Remesh(with, input, edge, q, true, pro);
                        Assert.Equal(0, r.InvertingPassesRejected);
                        Assert.Equal("", Invariance.Difference(without, with));
                    }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Tube135363();
            DMesh3 m0 = new DMesh3(input);
            Remesher r0 = Remesh(m0, input, Target135363, 0, true, false, parallel: false);
            DMesh3 transformed = Invariance.Transformed(input, s), m1 = new DMesh3(transformed);
            Remesher r1 = Remesh(m1, transformed, s.ApplyLength(Target135363), 0, true, false, parallel: false);
            Assert.False(InsideOut(m1), s.Name);
            if (s.IsExact) {
                Assert.Equal(r0.InvertingPassesRejected, r1.InvertingPassesRejected);
                Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m1, s)));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            DMesh3 input = Tube135363();
            DMesh3 serial = new DMesh3(input);
            Remesher rs = Remesh(serial, input, Target135363, 0.2, true, pro, parallel: false);
            Assert.True(rs.InvertingPassesRejected > 0);
            for (int k = 0; k < 2; ++k) {
                DMesh3 parallel = new DMesh3(input);
                Remesher rp = Remesh(parallel, input, Target135363, 0.2, true, pro);
                Assert.Equal(rs.InvertingPassesRejected, rp.InvertingPassesRejected);
                Assert.Equal("", Invariance.Difference(serial, parallel));
            }
        }
    }
}
