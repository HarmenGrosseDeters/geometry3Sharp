using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding's work outside the edits (the T1b profile, docs/roadmap/exact-boolean.md): the per-triangle scans
    /// (anchors, finiteness, the input range, the degenerate triangles, the tree's boxes) run over blocks of 4096 triangle
    /// ids in parallel, and remove_tiny_components walks the components with its own depth-first search instead of
    /// MeshConnectedComponents. Both are meant to give the same result bit for bit: the components in the same order and
    /// each in the same order (so the area and volume sums are the same), the scans' flags and lists in id order. The
    /// inputs here have more than one block, defects in several blocks and tiny components with and without a defect.
    /// </summary>
    public class RoundingSerialWorkTests
    {
        const double Needle = 3e-10;

        // a closed tetrahedron with legs a at p
        static void AppendTetrahedron(DMesh3 m, Vector3d p, double a)
        {
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + a * Vector3d.AxisX), v2 = m.AppendVertex(p + a * Vector3d.AxisY), v3 = m.AppendVertex(p + a * Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
        }

        // split edge (u, v) at a needle's distance from u: two degenerate triangles
        static void Poke(DMesh3 m, int u, int v)
        {
            int e = m.FindEdge(u, v);
            Vector3d p = m.GetVertex(u), q = m.GetVertex(v);
            Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out DMesh3.EdgeSplitInfo info));
            m.SetVertex(info.vNew, p + Needle * (q - p).Normalized);
        }

        // three spheres in a row (about 3 x 2900 triangles: more than one block of ids), each with needles (every 37th edge
        // split near its first end); between them tiny closed tetrahedra: `poked` of them with a needle (removed as tiny),
        // `plain` without (no defect: kept)
        static DMesh3 Field(int poked = 3, int plain = 2)
        {
            var m = new DMesh3();
            for (int i = 0; i < 3; ++i) {
                DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 23 }.Generate().MakeDMesh();
                foreach (int eid in s.EdgeIndices().ToArray()) {
                    if (eid % 37 != 0)
                        continue;
                    Index2i ev = s.GetEdgeV(eid);
                    Poke(s, ev.a, ev.b);
                }
                MeshTransforms.Translate(s, new Vector3d(2.5 * i, 0, 0));
                new MeshEditor(m).AppendMesh(s);
            }
            for (int k = 0; k < poked + plain; ++k) {
                int first = m.MaxVertexID;
                AppendTetrahedron(m, new Vector3d(1.25 + 2.5 * (k % 2), 1.5 + 0.01 * k, 0), 1e-4);
                if (k < poked)
                    Poke(m, first + 1, first + 2);
            }
            return m;
        }

        static MeshRoundingRepair Repair(DMesh3 m)
        {
            var r = new MeshRoundingRepair(m);
            r.Apply();
            return r;
        }

        static List<int[]> Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Components.Select(x => x.Indices).ToList();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ConnectedComponents_SameOrderAsMeshConnectedComponents(int seed)
        {
            // with holes (triangles removed) and ids reused by later appends, several components, more than one block
            DMesh3 m = Field();
            var rnd = new Random(seed);
            foreach (int t in m.TriangleIndices().ToArray())
                if (rnd.NextDouble() < 0.02 * seed)
                    m.RemoveTriangle(t, true, false);
            for (int k = 0; k < 3 * seed; ++k)
                AppendTetrahedron(m, new Vector3d(-3 - k, 0, 0), 0.5);
            List<int[]> expected = Components(m);
            var got = MeshRoundingRepair.connected_components(m).Select(c => c.ToArray()).ToList();
            Assert.True(expected.Count > 5);
            Assert.Equal(expected.Count, got.Count);
            for (int i = 0; i < expected.Count; ++i)
                Assert.Equal(expected[i], got[i]);
        }

        [Fact]
        public void Field_Clean_TinyDefectiveRemoved_TinyPlainKept()
        {
            DMesh3 m = Field();
            Assert.True(m.MaxTriangleID > 2 * 4096);
            int components = Components(m).Count;
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.IsClean);
            Assert.True(r.InitialDegenerate > 100);
            // the poked tetrahedra are tiny and hold a defect: removed; the plain ones have none: kept
            Assert.Equal(3, r.RemovedComponents);
            Assert.Equal(components - 3, Components(m).Count);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        [Fact]
        public void Field_SerialEqualsParallel_Repeated_ApplyTwice()
        {
            DMesh3 start = Field(), serial = new DMesh3(start);
            gParallel.SerialOnThisThread = true;
            MeshRoundingRepair rs;
            try {
                rs = Repair(serial);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 3; ++run) {
                DMesh3 parallel = new DMesh3(start);
                MeshRoundingRepair rp = Repair(parallel);
                Assert.Equal("", Invariance.Difference(serial, parallel));
                Assert.Equal(rs.InitialDegenerate, rp.InitialDegenerate);
                Assert.Equal(rs.RemovedComponents, rp.RemovedComponents);
                Assert.Equal(rs.MaxDrift, rp.MaxDrift);
            }
            // the same instance twice
            var r = new MeshRoundingRepair(new DMesh3(start));
            Assert.True(r.Apply());
            r.Mesh = new DMesh3(start);
            Assert.True(r.Apply());
            Assert.Equal("", Invariance.Difference(serial, r.Mesh));
            Assert.Equal(rs.RemovedComponents, r.RemovedComponents);
        }

        [Fact]
        public void Field_Similarities_CleanAndPow2BitIdentical()
        {
            DMesh3 start = Field(), m0 = new DMesh3(start);
            MeshRoundingRepair r0 = Repair(m0);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(start, sim);
                MeshRoundingRepair r = Repair(m);
                Assert.True(r.IsClean && Invariance.AllFinite(m) && m.IsClosed(), sim.ToString());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
                Assert.Equal(r0.RemovedComponents, r.RemovedComponents);
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, sim)));
                    Assert.Equal(r0.Collapses, r.Collapses);
                    Assert.Equal(r0.InitialDegenerate, r.InitialDegenerate);
                }
            }
        }

        [Fact]
        public void IntersectionsAcrossBlocks_CountedAsTheMetric()
        {
            // small tetrahedra appended last (in the last block of ids) that cross the surfaces of the spheres (whose
            // triangles are in the first blocks): the initial pair search, through the tree of all triangles' boxes,
            // finds the pairs MeshRepairMetrics finds
            DMesh3 m = Field(0, 0);
            // (holes first, away from the tetrahedra: the tree's slots are then not the triangle ids)
            foreach (int t in m.TriangleIndices().ToArray())
                if (t % 50 == 7 && m.GetTriCentroid(t).z < -0.5)
                    m.RemoveTriangle(t, true, false);
            for (int i = 0; i < 3; ++i)
                AppendTetrahedron(m, new Vector3d(2.5 * i + 0.97, 0.013, 0.011), 0.06);
            Assert.True(m.MaxTriangleID - 1 >= 2 * 4096);
            int expected = gs.MeshRepairMetrics.SelfIntersections(m);
            Assert.True(expected >= 3);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(expected, r.InitialIntersections);
        }

        [Theory]
        [InlineData(4095)]
        [InlineData(4096)]
        public void PiercingTriangleAtABlockBorder_PairsCountedAsTheMetric(int at)
        {
            // one triangle with the id `at` (the last slot of the first block of the tree, or the first of the second)
            // through the surface of the first sphere near (1, 0, 0), whose triangles have smaller and larger ids
            DMesh3 m = Field(0, 0);
            m.RemoveTriangle(at, true, false);
            int a = m.AppendVertex(new Vector3d(0.93, 0.004, 0.003)), b = m.AppendVertex(new Vector3d(1.07, 0.03, 0.003)), c = m.AppendVertex(new Vector3d(1.07, -0.02, 0.04));
            Assert.Equal(at, m.AppendTriangle(a, b, c));
            int expected = gs.MeshRepairMetrics.SelfIntersections(m);
            Assert.True(expected >= 1);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(expected, r.InitialIntersections);
        }

        // an isolated triangle (its own three vertices) with the id `at` (a freed id is reused first; -1: appended at the
        // end, in the last block), one coordinate of one of its vertices set to x: no other triangle sees that vertex
        static DMesh3 WithIsolatedTriangle(int at, double x)
        {
            DMesh3 m = Field(0, 0);
            Assert.True(m.MaxTriangleID > 2 * 4096);
            if (at >= 0)
                m.RemoveTriangle(at, true, false);
            int a = m.AppendVertex(new Vector3d(0, -3, 0)), b = m.AppendVertex(new Vector3d(0.5, -3, 0)), c = m.AppendVertex(new Vector3d(0, -3, 0.5));
            int t = m.AppendTriangle(a, b, c);
            Assert.Equal(at >= 0 ? at : m.MaxTriangleID - 1, t);
            m.SetVertex(c, new Vector3d(0, x, 0.5));
            return m;
        }

        // ids at the end of the first block, at the start of the second, and the last id
        public static IEnumerable<object[]> NotFiniteCases() =>
            from x in new[] { double.NaN, double.PositiveInfinity } from at in new[] { 4095, 4096, -1 } select new object[] { x, at };

        [Theory]
        [MemberData(nameof(NotFiniteCases))]
        public void NotFiniteInOneTriangle_ScaleNotFinite_NotTouched(double x, int at)
        {
            DMesh3 m = WithIsolatedTriangle(at, x), before = new DMesh3(m);
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.ScaleNotFinite);
            Assert.False(r.IsClean);
            // (by bits: NaN != NaN)
            Assert.Equal(before.MaxVertexID, m.MaxVertexID);
            Assert.Equal(before.TriangleCount, m.TriangleCount);
            for (int v = 0; v < m.MaxVertexID; ++v)
                for (int c = 0; c < 3; ++c)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(before.GetVertex(v)[c]), BitConverter.DoubleToInt64Bits(m.GetVertex(v)[c]));
        }

        public static IEnumerable<object[]> OutsideRangeCases() =>
            from x in new[] { 1e-320, 1e61 } from at in new[] { 4095, 4096, -1 } select new object[] { x, at };

        [Theory]
        [MemberData(nameof(OutsideRangeCases))]
        public void OutsideRangeInOneTriangle_Reported_NotTouched(double x, int at)
        {
            // a subnormal coordinate, or the largest magnitude, only in one triangle at a block border or at the end
            DMesh3 m = WithIsolatedTriangle(at, x), before = new DMesh3(m);
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.OutsideRange);
            Assert.False(r.IsClean);
            Assert.Equal("", Invariance.Difference(before, m));
        }

        [Fact]
        public void LargestMagnitudeInsideTheRange_InTheLastTriangle_NotOutside()
        {
            // 1e59 is inside the range (it makes the rest tiny against it; only the range is checked here)
            MeshRoundingRepair r = Repair(WithIsolatedTriangle(-1, 1e59));
            Assert.False(r.OutsideRange);
        }

        [Fact]
        public void NoPasses_TinyDegenerateComponentsRemovedByTheirDefect()
        {
            // without passes no vertex is touched: the poked tetrahedra are removed because they hold a degenerate
            // triangle (the bad list), the plain ones are kept
            DMesh3 m = Field();
            int components = Components(m).Count;
            var r = new MeshRoundingRepair(m) { MaxPasses = 0 };
            r.Apply();
            Assert.Equal(3, r.RemovedComponents);
            Assert.Equal(components - 3, Components(m).Count);
            Assert.True(r.RemainingDegenerate > 0);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
        }
    }
}
