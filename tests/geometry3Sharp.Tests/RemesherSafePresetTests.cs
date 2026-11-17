using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.ApplySafePreset (opt-in; defaults unchanged): MinTriangleQuality 0.2 and PreventSelfIntersections, the
    /// combination of docs/perf/REMESHER_PHASE2_MEASURED.md that made no new self-intersecting pair (about 19x the time
    /// of the default on Thingi10K).
    /// </summary>
    public class RemesherSafePresetTests
    {
        static double MeanEdge(DMesh3 m) =>
            m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        // an extruded gear with fan caps (as in RemesherSelfIntersectionTests): at its mean edge length the default remesher
        // folds the flank and rim triangles through each other
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
                m.AppendTriangle(cb, b[j], b[i]); m.AppendTriangle(ct, t[i], t[j]);
                m.AppendTriangle(b[i], b[j], t[j]); m.AppendTriangle(b[i], t[j], t[i]);
            }
            return m;
        }

        // the g3cli remesh settings (20 passes, smoothing 0.5, projection onto the input, 30 degree creases), with the preset or not
        static DMesh3 Remesh(DMesh3 input, double edge, bool preset, bool parallel = true, bool pro = false)
        {
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            Setup(r, input, edge, preset, parallel);
            Run(r, pro);
            return mesh;
        }

        static void Setup(Remesher r, DMesh3 input, double edge, bool preset, bool parallel)
        {
            r.SmoothSpeedT = 0.5;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            if (preset) r.ApplySafePreset();
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(edge);
        }

        static void Run(Remesher r, bool pro)
        {
            for (int k = 0; k < 20; ++k) {
                if (pro) ((RemesherPro)r).RemeshIteration();
                else r.BasicRemeshPass();
            }
        }

        static DMesh3 Input() => Gear(24, 0.2, 3);

        [Fact]
        public void Preset_SetsExactlyTheMeasuredOptions()
        {
            var r = new Remesher(new DMesh3());
            Assert.Equal(0, r.MinTriangleQuality);              // the defaults stay
            Assert.False(r.PreventSelfIntersections);
            Remesher before = new Remesher(new DMesh3());
            r.ApplySafePreset();
            Assert.Equal(0.2, r.MinTriangleQuality);
            Assert.Equal(0.2, Remesher.SafePresetMinTriangleQuality);
            Assert.True(r.PreventSelfIntersections);
            // nothing else changes: every other public field of the remesher keeps its default
            foreach (var f in typeof(Remesher).GetFields().Where(f => !f.IsStatic && !f.IsLiteral && !f.IsInitOnly
                         && f.Name != nameof(Remesher.MinTriangleQuality) && f.Name != nameof(Remesher.PreventSelfIntersections)
                         && (f.FieldType.IsPrimitive || f.FieldType.IsEnum)))
                Assert.True(Equals(f.GetValue(before), f.GetValue(r)), f.Name);
            var pro = new RemesherPro(new DMesh3());
            pro.ApplySafePreset();
            Assert.Equal(0.2, pro.MinTriangleQuality);
            Assert.True(pro.PreventSelfIntersections);
        }

        [Fact]
        public void Preset_LaterAssignmentOverrides()
        {
            var r = new Remesher(new DMesh3());
            r.ApplySafePreset();
            r.MinTriangleQuality = 0.1;
            r.PreventSelfIntersections = false;
            Assert.Equal(0.1, r.MinTriangleQuality);
            Assert.False(r.PreventSelfIntersections);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FoldingGear_PresetMakesNoSelfIntersections_DefaultDoes(bool pro)
        {
            DMesh3 input = Input();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            double edge = MeanEdge(input);
            Assert.True(MeshRepairMetrics.SelfIntersections(Remesh(input, edge, false, pro: pro)) > 0, "the default result has pairs");
            DMesh3 mesh = Remesh(input, edge, true, pro: pro);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
            Assert.True(mesh.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(mesh));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            Assert.True(mesh.Vertices().All(v => v.IsFinite));
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(mesh, true));
            Assert.NotEqual(input.TriangleCount, mesh.TriangleCount);
        }

        // closed thin-walled tube (outer radius R, wall w): outer and inner wall joined by annular rims at both ends
        // (benchmarks/RemeshQualityBenchmark)
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

        [Fact]
        public void FoldedWalls_QualityAloneLeavesPairs_PresetDoesNot()
        {
            // the mechanism of the measurement note: an edge length 16 times the wall folds the walls through each other (the
            // default and MinTriangleQuality 0.2 alone leave pairs); the preset blocks the folds (the result is not the
            // input's wall: the Hausdorff distance is not checked, as for PreventSelfIntersections itself)
            DMesh3 input = Tube(16, 4, 1, 0.05, 3);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            DMesh3 qOnly = new DMesh3(input);
            var r = new Remesher(qOnly) { MinTriangleQuality = 0.2 };
            Setup(r, input, 0.8, false, true);
            Run(r, false);
            Assert.True(MeshRepairMetrics.SelfIntersections(qOnly) > 0, "quality 0.2 alone leaves pairs");
            Assert.True(MeshRepairMetrics.SelfIntersections(Remesh(input, 0.8, false)) > 0, "the default leaves pairs");
            DMesh3 mesh = Remesh(input, 0.8, true);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
            Assert.True(mesh.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(mesh));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
            Assert.True(mesh.Vertices().All(v => v.IsFinite));
        }

        [Fact]
        public void InputPairs_AreNotRemoved_NoneAreAdded()
        {
            // an input with pairs (a second gear shifted through the first): the preset keeps the result from getting more pairs
            DMesh3 a = Input(), b = Input();
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(0.9, 0.0, 0.1));
            var input = new DMesh3(a);
            MeshEditor.Append(input, b);
            int pairsIn = MeshRepairMetrics.SelfIntersections(input);
            Assert.True(pairsIn > 0);
            DMesh3 mesh = Remesh(input, MeanEdge(input), true);
            Assert.True(MeshRepairMetrics.SelfIntersections(mesh) <= pairsIn);
            Assert.True(mesh.Vertices().All(v => v.IsFinite));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Input();
            double edge = MeanEdge(input);
            DMesh3 m0 = Remesh(input, edge, true);
            DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(input, s), s.ApplyLength(edge), true), s);
            Assert.True(Invariance.AllFinite(back));
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(back));
            Assert.True(back.IsClosed(), s.Name);
        }

        [Fact]
        public void Reuse_PassesRepeatedOnOneInstance_StayClean()
        {
            // the remesher has no Compute(): the instance is reused by running 20 more passes on it; the result stays free of pairs
            DMesh3 input = Input();
            DMesh3 mesh = new DMesh3(input);
            var r = new Remesher(mesh);
            Setup(r, input, MeanEdge(input), true, true);
            Run(r, false);
            int tris = mesh.TriangleCount;
            Run(r, false);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
            Assert.True(mesh.IsClosed());
            Assert.True(tris > 0 && mesh.TriangleCount > 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            DMesh3 input = Input();
            double edge = MeanEdge(input);
            DMesh3 serial = Remesh(input, edge, true, parallel: false, pro: pro);
            for (int k = 0; k < 2; ++k)
                Assert.Equal("", Invariance.Difference(serial, Remesh(input, edge, true, parallel: true, pro: pro)));
        }
    }
}
