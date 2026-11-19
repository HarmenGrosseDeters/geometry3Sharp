// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The local alpha (docs/design/TET_MESHING.md section 17.13): MeshAlphaWrap.AlphaField and MeshTetrahedralization.MaxSurfaceDeviation.
    /// Both default off, and then the output is as before (ExistingResult_IsBitForBitTheSameAsBeforeP2 pins the default). The wrap's promises that do not read alpha
    /// (closed, manifold, no self-intersection, the input inside) are re-checked with a field.
    /// </summary>
    public class TetMeshLocalAlphaTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)), new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))) }.Generate().MakeDMesh();

        /// <summary> a base plate with four teeth: gaps of 0.05 between the teeth (a little more than 1 alpha at diagonal / 20), exact union </summary>
        internal static DMesh3 Comb()
        {
            var parts = new List<DMesh3> { Box(0, 0, 0, 0.6, 0.12, 0.12) };
            for (int i = 0; i < 4; ++i) parts.Add(Box(0.01 + i * 0.15, 0.08, 0, 0.01 + i * 0.15 + 0.10, 0.5, 0.12));
            return ExactMeshBoolean.Union(parts);
        }

        // the options of the tests: a coarse base alpha keeps the runs short (the comb's gaps are bridged at it)
        const double BaseAlpha = 20;
        static MeshTetrahedralization Uniform(DMesh3 m, double alpha = BaseAlpha) => new MeshTetrahedralization(m) { RelativeAlpha = alpha };
        static MeshTetrahedralization Local(DMesh3 m, double devFraction = 150, int passes = 1) =>
            new MeshTetrahedralization(m) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = m.CachedBounds.DiagonalLength / devFraction, LocalAlphaPasses = passes };

        // the largest distance of area-weighted input samples to the boundary of a result
        static double MaxDeviation(MeshTetrahedralization tm)
        {
            var tree = new DMeshAABBTree3(tm.Result.ToSurfaceMesh(), true);
            double max = 0;
            foreach (Vector3d p in AlphaSizeField.Samples(tm.Mesh, 4000)) { tree.FindNearestTriangle(p, out double d2); max = Math.Max(max, Math.Sqrt(d2)); }
            return max;
        }

        static void Run(MeshTetrahedralization tm) { Assert.True(tm.Compute(), tm.Status.ToString()); Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status); }

        // ------------------------------------------------------------------------------------------------ off by default

        [Fact]
        public void Default_IsOff_AndSwitchedOffValuesGiveTheDefaultResult()
        {
            Assert.Equal(0.0, new MeshTetrahedralization(new DMesh3()).MaxSurfaceDeviation);
            Assert.Equal(1, new MeshTetrahedralization(new DMesh3()).LocalAlphaPasses);
            Assert.Null(new MeshAlphaWrap(new DMesh3()).AlphaField);
            DMesh3 input = Comb();
            var def = Uniform(input); Run(def);
            foreach (double off in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity }) {
                var tm = Uniform(input); tm.MaxSurfaceDeviation = off; Run(tm);
                Assert.Equal(def.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(def.Result.Tets, tm.Result.Tets);
                Assert.Equal(def.Result.BoundaryTriangles, tm.Result.BoundaryTriangles);
                Assert.Equal(def.Counts, tm.Counts);
            }
            Assert.Equal(new MeshTetrahedralization(input).SurfaceMinAngleDegrees, def.Wrap.RefineMinAngleDegrees);   // the default keeps its surface refinement
            Assert.False(def.Counts.ContainsKey("local_alpha_passes"));
            Assert.False(def.Counts.ContainsKey("local_alpha_fine_samples"));
        }

        [Fact]
        public void InputWithoutTriangles_IgnoresTheOption()
        {
            var pts = new DMesh3();
            var rnd = new Random(5);
            for (int i = 0; i < 60; ++i) pts.AppendVertex(new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()));
            var a = Uniform(pts); Run(a);
            var b = Uniform(pts); b.MaxSurfaceDeviation = 0.01; Run(b);
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.Equal(a.Counts, b.Counts);
        }

        // ------------------------------------------------------------------------------------------------ what it does

        [Fact]
        public void Deviation_IsAsLowAsAUniformAlphaThreeTimesFiner_WithFewerTets()
        {
            // measured (comb, alpha / 20): uniform alpha / 20 .. / 60: distance 0.078 .. 0.0146 of the diagonal at 4.7k .. 17k tets; local at diagonal / 100: 0.0141 at 9.9k
            DMesh3 input = Comb();
            var uni = Uniform(input); Run(uni);
            var loc = Local(input, 100); Run(loc);
            Assert.True(loc.Counts["local_alpha_fine_samples"] > 0);
            double du = MaxDeviation(uni), dl = MaxDeviation(loc);
            Assert.True(dl < 0.3 * du, "the local alpha did not follow the input closer: " + dl + " against " + du);
            var fine = Uniform(input, 3 * BaseAlpha); Run(fine);
            Assert.True(dl < 1.1 * MaxDeviation(fine), "the local alpha is farther than the uniform alpha / 3: " + dl + " against " + MaxDeviation(fine));
            Assert.True(loc.Result.Tets.Count < 0.75 * fine.Result.Tets.Count, loc.Result.Tets.Count + " tets against " + fine.Result.Tets.Count);
        }

        [Fact]
        public void ResultIsValid_TheInputInside_AndTheFloorIsMet()
        {
            DMesh3 input = Comb();
            foreach (int passes in new[] { 2 }) {          // (one pass: the identity case of the similarity test below checks the same invariants)
                var tm = Local(input, 150, passes); Run(tm);
                Assert.Equal(0.0, tm.Wrap.RefineMinAngleDegrees);       // the wrap's surface angle refinement is not used with a local alpha (17.13)
                TetMeshingStage2Tests.Invariants(tm);
                Assert.True(tm.Targets.MinDihedralMet, "worst dihedral " + tm.Targets.WorstMinDihedral);
                Assert.Equal(passes, tm.Counts["local_alpha_passes"]);
            }
        }

        [Fact]
        public void SphereAndBox_WhichHaveNoDetail_KeepTheirTets_AndAbove2OffsetsNothingIsRefined()
        {
            // the tolerance is not below the distance of the uniform wrap (about 1 offset): no sample is farther, no field, the second wrap is not run
            foreach (string name in new[] { "sphere", "box" }) {
                DMesh3 input = TetMeshingStage2Tests.Input(name);
                var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, MaxSurfaceDeviation = input.CachedBounds.DiagonalLength / 100 }; Run(tm);
                Assert.Equal(0, tm.Counts["local_alpha_fine_samples"]);
                Assert.Equal(MeshAlphaWrap.WrapStatus.Success, tm.WrapStatus);
            }
        }

        [Fact]
        public void ToleranceBelowTwoOffsets_IsRaisedToTwoOffsets()
        {
            DMesh3 input = Comb();
            var tiny = new MeshTetrahedralization(input) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = 1e-9 }; Run(tiny);
            var two = new MeshTetrahedralization(input) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = 2 * input.CachedBounds.DiagonalLength / 600 }; Run(two);
            Assert.Equal(two.Result.Tets, tiny.Result.Tets);
            Assert.Equal(two.Counts, tiny.Counts);
        }

        [Fact]
        public void MorePasses_NeverMakeTheDeviationLarger_AndTheCountsSaySo()
        {
            DMesh3 input = Comb();
            var one = Local(input, 150, 1); Run(one);
            var two = Local(input, 150, 2); Run(two);
            Assert.True(MaxDeviation(two) <= MaxDeviation(one) * 1.05, MaxDeviation(two) + " against " + MaxDeviation(one));
            Assert.True(two.Counts["local_alpha_fine_samples"] >= one.Counts["local_alpha_fine_samples"]);
            foreach (int bad in new[] { 0, 4 }) {
                var tm = Local(input, 150, bad); Run(tm);
                Assert.Equal(one.Result.Tets, tm.Result.Tets);       // out of range counts as 1
            }
        }

        // ------------------------------------------------------------------------------------------------ determinism, reuse, invariance

        [Fact]
        public void EveryMaxThreads_AndRepeatedRuns_GiveTheSameResult()
        {
            DMesh3 input = Comb();
            var serial = Local(input); serial.MaxThreads = 1; Run(serial);
            foreach (int threads in new[] { 0, 2, 4, 1 }) {
                var tm = Local(input); tm.MaxThreads = threads; Run(tm);
                Assert.Equal(serial.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(serial.Result.Tets, tm.Result.Tets);
                Assert.Equal(serial.Result.BoundaryTriangles, tm.Result.BoundaryTriangles);
                Assert.Equal(serial.Counts, tm.Counts);
            }
        }

        [Fact]
        public void Compute_OnTheSameInstanceTwice_GivesTheSameResult()
        {
            var tm = Local(Comb()); Run(tm);
            var verts = tm.Result.Vertices.ToList(); var tets = tm.Result.Tets.ToList(); var counts = new Dictionary<string, long>(tm.Counts);
            Run(tm);
            Assert.Equal(verts, tm.Result.Vertices);
            Assert.Equal(tets, tm.Result.Tets);
            Assert.Equal(counts, tm.Counts);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = Comb();
            var a = Local(input); Run(a);
            DMesh3 moved = Invariance.Transformed(input, s);
            var b = new MeshTetrahedralization(moved) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = s.ApplyLength(a.MaxSurfaceDeviation) }; Run(b);
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else
                Assert.True(b.Counts["local_alpha_fine_samples"] > 0);
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // ------------------------------------------------------------------------------------------------ cancellation

        [Fact]
        public void Cancelled_InTheLocalAlphaPasses_IsReportedAsCancelled()
        {
            DMesh3 input = Comb();
            // tm.Wrap is set when the first (uniform) wrap has run: cancelled from then on, so in the passes
            MeshTetrahedralization tm = null;
            tm = Local(input);
            tm.CancelF = () => tm.Wrap != null;
            Assert.False(tm.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, tm.Status);
            Assert.Null(tm.Result);
        }

        // ------------------------------------------------------------------------------------------------ the wrap with a field

        static MeshAlphaWrap Wrap(DMesh3 input, Func<Vector3d, double> field, double ra = 12)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = 300, AlphaField = field };
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            return w;
        }

        static void WrapPromises(MeshAlphaWrap w, DMesh3 input)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result, true));
            Assert.True(w.Result.IsClosed());
            Assert.False(MeshRepairMetrics.HasSelfIntersections(w.Result));
            Assert.Equal(0, AlphaWrapNoContactTests.Crossings(w.Result, input));
            var tree = new DMeshAABBTree3(input, true);
            foreach (int v in w.Result.VertexIndices()) {          // the vertices keep the offset from the input
                tree.FindNearestTriangle(w.Result.GetVertex(v), out double d2);
                Assert.True(Math.Sqrt(d2) >= 0.99 * w.UsedOffset);
            }
            foreach (int v in input.VertexIndices())               // the input is inside: the winding number of the result at its vertices is 1
                Assert.True(WindingInside(w.Result, input.GetVertex(v)));
        }

        static bool WindingInside(DMesh3 closed, Vector3d p)
        {
            double sum = 0;
            foreach (int t in closed.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; closed.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d A = a - p, B = b - p, C = c - p;
                double la = A.Length, lb = B.Length, lc = C.Length;
                sum += 2 * Math.Atan2(A.Dot(B.Cross(C)), la * lb * lc + A.Dot(B) * lc + B.Dot(C) * la + C.Dot(A) * lb);
            }
            return Math.Abs(sum / (4 * Math.PI) - 1) < 0.1;
        }

        [Fact]
        public void WrapWithAField_KeepsItsPromises_OnTheCombAndASphere()
        {
            foreach (DMesh3 input in new[] { Comb(), TetMeshingStage2Tests.Input("sphere") }) {
                double diag = input.CachedBounds.DiagonalLength, alpha = diag / 12;
                // a field that is fine in one half of the space and coarse in the other, with a slope of 0.25
                Func<Vector3d, double> field = p => Math.Max(alpha / 4, alpha / 4 + 0.25 * (p.x - input.CachedBounds.Center.x + 0.02 * diag) * 2);
                MeshAlphaWrap w = Wrap(input, field);
                WrapPromises(w, input);
                Assert.True(w.Result.TriangleCount > Wrap(input, null).Result.TriangleCount, "a finer alpha somewhere gives more triangles");
            }
        }

        [Fact]
        public void AField_AtLeastTheUniformAlpha_NaN_OrBelowTheFloor_IsClampedAsDocumented()
        {
            DMesh3 input = Comb();
            var uniform = Wrap(input, null);
            foreach (Func<Vector3d, double> f in new Func<Vector3d, double>[] { p => 1e9, p => double.NaN, p => double.PositiveInfinity, p => uniform.UsedAlpha }) {
                var w = Wrap(input, f);
                Assert.Equal(uniform.Result.TriangleCount, w.Result.TriangleCount);
                for (int v = 0; v < uniform.Result.MaxVertexID; ++v) Assert.Equal(uniform.Result.GetVertex(v), w.Result.GetVertex(v));
            }
            // below the floor (3 offsets) it is the floor, whatever the value
            var a = Wrap(input, p => 1e-12); var b = Wrap(input, p => 3 * uniform.UsedOffset);
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            for (int v = 0; v < a.Result.MaxVertexID; ++v) Assert.Equal(a.Result.GetVertex(v), b.Result.GetVertex(v));
            WrapPromises(a, input);
        }

        [Fact]
        public void WrapField_EveryMaxThreads_GivesTheSameResult()
        {
            DMesh3 input = Comb();
            double alpha = input.CachedBounds.DiagonalLength / 12;
            Func<Vector3d, double> field = p => p.y > 0.3 ? alpha : alpha / 3;
            var serial = new MeshAlphaWrap(input) { RelativeAlpha = 12, RelativeOffset = 300, AlphaField = field, MaxThreads = 1 }; Assert.True(serial.Compute());
            foreach (int t in new[] { 0, 3 }) {
                var w = new MeshAlphaWrap(input) { RelativeAlpha = 12, RelativeOffset = 300, AlphaField = field, MaxThreads = t }; Assert.True(w.Compute());
                Assert.Equal(serial.Result.TriangleCount, w.Result.TriangleCount);
                for (int v = 0; v < serial.Result.MaxVertexID; ++v) Assert.Equal(serial.Result.GetVertex(v), w.Result.GetVertex(v));
            }
        }

        // ------------------------------------------------------------------------------------------------ the field

        [Fact]
        public void SizeField_IsTheGradedEnvelope()
        {
            var pos = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(5, 5, 5) };
            var aj = new[] { 0.25, 1.0, 0.5 };                // the second is not finer than alpha0 = 1: not a sample of the field
            var f = new AlphaSizeField(pos, aj, 1.0, 0.25, 0.25);
            Assert.Equal(2, f.FineSamples);
            Assert.Equal(0.25, f.At(pos[0]));
            Assert.Equal(0.25 + 0.25 * 0.5, f.At(new Vector3d(0.5, 0, 0)), 12);          // the cone of the first sample
            Assert.Equal(0.5, f.At(new Vector3d(1, 0, 0)), 12);                          // 0.25 + 0.25 * 1: below alpha0, the cone counts
        }

        [Fact]
        public void Samples_AreDeterministicAreaWeighted_AndScaleBitForBit()
        {
            DMesh3 input = Comb();
            Vector3d[] a = AlphaSizeField.Samples(input, 500), b = AlphaSizeField.Samples(input, 500);
            Assert.Equal(a, b);
            DMesh3 big = Invariance.Transformed(input, Similarity.Pow2Large);
            Vector3d[] c = AlphaSizeField.Samples(big, 500);
            for (int i = 0; i < a.Length; ++i) Assert.Equal(1024 * a[i], c[i]);
            Assert.Empty(AlphaSizeField.Samples(new DMesh3(), 10));
            // every sample lies on the input
            var tree = new DMeshAABBTree3(input, true);
            foreach (Vector3d p in a) { tree.FindNearestTriangle(p, out double d2); Assert.True(Math.Sqrt(d2) < 1e-6 * input.CachedBounds.DiagonalLength); }   // (the exact union has sliver triangles: their points are only as exact as the barycentric sum)
        }
    }
}
