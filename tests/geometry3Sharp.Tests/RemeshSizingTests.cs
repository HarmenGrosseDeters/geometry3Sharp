using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.TargetEdgeLengthF (opt-in local sizing) and RemeshSizing.FromWallThickness: with a target edge length
    /// far above the wall thickness the remeshers fold the walls of a thin-walled tube through each other and end far
    /// off the volume (benchmarks/RemeshQualityBenchmark, tubes); with the edges kept below the local wall thickness
    /// the result keeps the volume and the surface.
    /// </summary>
    public class RemeshSizingTests
    {
        static double MeanEdge(DMesh3 m) =>
            m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        static double Volume(DMesh3 m)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                v += a.Dot(b.Cross(c));
            }
            return v / 6;
        }

        // closed thin-walled tube (as in benchmarks/RemeshQualityBenchmark): outer radius R, wall w, height H
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

        // as g3cli remesh (20 passes, smoothing 0.5, projection, 30 degree creases, min_quality 0.2)
        static DMesh3 Remesh(DMesh3 input, double target, Func<Vector3d, double> field, bool parallel = true, bool prevent = false)
        {
            DMesh3 mesh = new DMesh3(input);
            var r = new Remesher(mesh) { SmoothSpeedT = 0.5, MinTriangleQuality = 0.2, PreventSelfIntersections = prevent };
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            r.TargetEdgeLengthF = field;
            for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
            return mesh;
        }

        [Fact]
        public void Default_IsNull()
        {
            Assert.Null(new Remesher(new DMesh3()).TargetEdgeLengthF);
        }

        static DMesh3 Square()
        {
            // a flat square [0,2] x [0,1], 8 x 4 cells, boundary kept
            var m = new DMesh3();
            int nx = 8, ny = 4;
            int[,] v = new int[nx + 1, ny + 1];
            for (int i = 0; i <= nx; ++i)
                for (int j = 0; j <= ny; ++j)
                    v[i, j] = m.AppendVertex(new Vector3d(2.0 * i / nx, 1.0 * j / ny, 0));
            for (int i = 0; i < nx; ++i)
                for (int j = 0; j < ny; ++j) {
                    m.AppendTriangle(v[i, j], v[i + 1, j], v[i + 1, j + 1]);
                    m.AppendTriangle(v[i, j], v[i + 1, j + 1], v[i, j + 1]);
                }
            return m;
        }

        static double MeanEdgeWhere(DMesh3 m, Func<Vector3d, bool> f)
        {
            var l = m.EdgeIndices().Select(e => m.GetEdgeV(e)).Where(ev => f(0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b))))
                .Select(ev => m.GetVertex(ev.a).Distance(m.GetVertex(ev.b))).ToList();
            return l.Average();
        }

        [Fact]
        public void Field_ShorterEdgesWhereItAsks_CappedByTheGlobalTarget()
        {
            // the field asks for a quarter of the target on the left half, for 10 times the target on the right
            // (capped: the global target) and gives NaN at the top (the global target)
            DMesh3 input = Square();
            double target = 0.1;
            Func<Vector3d, double> field = p => p.y > 0.8 ? double.NaN : (p.x < 1 ? 0.25 * target : 10 * target);
            DMesh3 m = Remesh(input, target, field);
            double left = MeanEdgeWhere(m, p => p.x < 0.9 && p.y < 0.7), right = MeanEdgeWhere(m, p => p.x > 1.1 && p.y < 0.7);
            Assert.InRange(left, 0.66 * 0.25 * target, 1.33 * 0.25 * target);
            Assert.InRange(right, 0.66 * target, 1.33 * target);
            double top = MeanEdgeWhere(m, p => p.x > 1.1 && p.y > 0.9);
            Assert.InRange(top, 0.66 * target, 1.33 * target);
            // without the field: the global target everywhere
            DMesh3 g = Remesh(input, target, null);
            Assert.InRange(MeanEdgeWhere(g, p => p.x < 0.9 && p.y < 0.7), 0.66 * target, 1.33 * target);
            // the result: one disc, manifold, consistently oriented, finite, still flat on z = 0
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(m));
            Assert.True(m.Vertices().All(v => v.IsFinite && v.z == 0));
            Assert.Equal(1, new MeshBoundaryLoops(m).Count);
        }

        [Fact]
        public void ThinTube_KeepsTheVolume()
        {
            // wall 0.1, target 0.8: without the field the walls fold through each other (volume 0.23 x, hundreds of pairs
            // in the benchmark); with edges at the wall thickness the volume and the surface stay
            DMesh3 input = Tube(32, 4, 1, 0.1, 3);
            double v0 = Volume(input);
            DMesh3 without = Remesh(input, 0.8, null);
            Assert.True(Math.Abs(Volume(without) / v0 - 1) > 0.2, $"without the field: volume {Volume(without) / v0} x");
            var field = RemeshSizing.FromWallThickness(input, 1.0);
            Assert.NotNull(field);
            DMesh3 mesh = Remesh(input, 0.8, field);
            Assert.InRange(Volume(mesh) / v0, 0.98, 1.02);
            Assert.True(mesh.IsClosed());
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            Assert.True(mesh.Vertices().All(v => v.IsFinite));
            var hd = new MeshHausdorffDistance(input, mesh) { Tolerance = 0.002 };
            Assert.True(hd.Compute());
            Assert.True(hd.UpperBound < 0.05, $"{hd.UpperBound} from the input");
        }

        [Fact]
        public void ThinTube_WithPreventSelfIntersections_Clean()
        {
            DMesh3 input = Tube(16, 4, 1, 0.1, 3);
            DMesh3 mesh = Remesh(input, 0.8, RemeshSizing.FromWallThickness(input, 1.0), true, true);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(mesh, true));
            Assert.InRange(Volume(mesh) / Volume(input), 0.98, 1.02);
        }

        [Fact]
        public void FromWallThickness_ValuesFromTheThickness()
        {
            DMesh3 input = Tube(32, 4, 1, 0.1, 3);
            var field = RemeshSizing.FromWallThickness(input, 0.5);
            // on the outer wall, half way up: half the wall (InscribedSphere, within the faceting)
            double t = field(new Vector3d(1, 0, 1.5));
            Assert.InRange(t, 0.04, 0.051);
            // the floor: 1e-3 of the diagonal
            var floor = RemeshSizing.FromWallThickness(input, 1e-6);
            Assert.Equal(1e-3 * input.CachedBounds.DiagonalLength, floor(new Vector3d(1, 0, 1.5)), 12);
            // an open mesh: no thickness, NaN (the global target)
            DMesh3 open = Square();
            var none = RemeshSizing.FromWallThickness(open, 1.0);
            Assert.True(none == null || double.IsNaN(none(new Vector3d(1, 0.5, 0))));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Tube(16, 4, 1, 0.1, 3);
            DMesh3 moved = Invariance.Transformed(input, s);
            var f0 = RemeshSizing.FromWallThickness(input, 1.0);
            var f1 = RemeshSizing.FromWallThickness(moved, 1.0);
            // the field scales with the input (bit for bit for 2^k)
            foreach (Vector3d p in input.Vertices()) {
                double a = f0(p), b = f1(s.Apply(p)) / s.Scale;
                if (s.IsExact)
                    Assert.Equal(a, b);
                else
                    Assert.True(Math.Abs(a - b) <= 1e-4 * a, $"{s.Name}: field {a} vs {b}");     // (rounding of the far or rotated coordinates)
            }
            DMesh3 m0 = Remesh(input, 0.8, f0, false);
            DMesh3 back = Invariance.MappedBack(Remesh(moved, s.ApplyLength(0.8), f1, false), s);
            // the remesh: bit for bit at 2^10; at 2^-10 the Remesher with projection differs without the field too
            // (4673 vs 4659 vertices for this tube at target 0.1; benchmarks/RemeshBenchmark README, scale invariance)
            if (s.IsExact && s.Scale > 1)
                Assert.Equal("", Invariance.Difference(m0, back));
            Assert.True(Math.Abs(back.TriangleCount - m0.TriangleCount) <= 0.03 * m0.TriangleCount, $"{s.Name}: {back.TriangleCount} vs {m0.TriangleCount}");
            Assert.InRange(Volume(back) / Volume(input), 0.98, 1.02);
        }

        [Fact]
        public void ParallelEqualsSerial_RepeatedRuns()
        {
            DMesh3 input = Tube(16, 4, 1, 0.1, 3);
            var field = RemeshSizing.FromWallThickness(input, 1.0);
            DMesh3 serial = Remesh(input, 0.8, field, false);
            for (int k = 0; k < 2; ++k)
                Assert.Equal("", Invariance.Difference(serial, Remesh(input, 0.8, field, true)));
        }
    }
}
