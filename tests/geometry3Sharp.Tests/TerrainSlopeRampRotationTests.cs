using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1539: the around-the-corner ramp (TerrainSlopeRampTests.AroundCorner) rotated about z by 127, 205 and 271 degrees.
    /// The map's faces along the road's end edge are slivers a few ulps wide. The rounding turns some of their triangles
    /// over (clockwise) or lays them over a neighbour's (an edge run in the same direction by two triangles; DMesh3 takes
    /// that), and the next triangle on that edge is refused: "SlopeMap: non-manifold realization". After the two realizations
    /// of the design (with and without the merge) the strict one drops such slivers and stitches the neighbours.
    /// </summary>
    [Collection("Terrain301")]       // (shares the design of 301 with TerrainSlopeRampRotationRegressionTests)
    public class TerrainSlopeRampRotationTests
    {
        // ---- append_faces on its own: a sliver the rounding laid over a neighbour ----

        // vertices of the mesh: the long edge e0 e1 on y = 0 (coordinates up to 4: ulp 8.9e-16, a sliver is up to 3.6e-15 high), the apex x below it, m1 just below the edge (a sliver (e1, e0, m1)
        // over x's triangle: counter-clockwise, running along e1 -> e0 as the triangle (e1, e0, x) does), m2 just above it
        static DMesh3 Setup(out int e0, out int e1, out int x, out int m1, out int m2)
        {
            var mesh = new DMesh3(false, false, false, true);
            e0 = mesh.AppendVertex(new Vector3d(0, 0, 0)); e1 = mesh.AppendVertex(new Vector3d(4, 0, 0)); x = mesh.AppendVertex(new Vector3d(2, -2, 0));
            m1 = mesh.AppendVertex(new Vector3d(1, -1e-15, 0)); m2 = mesh.AppendVertex(new Vector3d(2.5, 1e-15, 0));
            return mesh;
        }

        static int Append(DMesh3 mesh, List<Index3i> tris, bool strict, bool zip = false)
        {
            // (a face per triangle, its corners the mesh's vertex ids)
            var faces = tris.Select((t, i) => new KeyValuePair<int, List<int>>(i, new List<int> { t.a, t.b, t.c })).ToList();
            return SlopeMap.append_faces(mesh, faces, (v) => new List<Index3i> { new Index3i(v[0], v[1], v[2]) }, (v) => v, strict, 0, zip);
        }

        [Fact]
        public void AppendFaces_Strict_DropsASliverOverANeighbour_AndStitchesIt()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // x's triangle, the sliver over it (m1 below the edge), the sliver on the other side (m2 above it, a legal neighbour)
            var tris = new List<Index3i> { new Index3i(e1, e0, x), new Index3i(e1, e0, m1), new Index3i(e0, e1, m2) };
            // as before: the third triangle on the edge is refused
            var plain = Setup(out e0, out e1, out x, out m1, out m2);
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(plain, tris, false));
            // strict: the sliver is dropped (the other side of the edge keeps its triangle), every triangle counter-clockwise, manifold
            Assert.Equal(1, Append(mesh, tris, true));
            Assert.Equal(2, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Assert.Equal(1, ExactPredicates.Orient2d(mesh.GetVertex(tv.a).xy, mesh.GetVertex(tv.b).xy, mesh.GetVertex(tv.c).xy));
            }
        }

        [Fact]
        public void AppendFaces_Strict_DropsATurnedOverSliver_NotTheDefault()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // (e0, e1, m1): m1 is below the edge, clockwise: the default appends it (it lies over x's triangle), strict drops it
            var tris = new List<Index3i> { new Index3i(e0, e1, m1) };
            Assert.Equal(0, Append(mesh, tris, false));
            Assert.Equal(1, mesh.TriangleCount);
            var strict = Setup(out e0, out e1, out x, out m1, out m2);
            Assert.Equal(1, Append(strict, tris, true));
            Assert.Equal(0, strict.TriangleCount);
        }

        [Fact]
        public void AppendFaces_Strict_DoesNotDropATriangleThatIsNotASliver()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // m3 is 0.5 below the edge: (e1, e0, m3) lies over x's triangle and is no sliver, and (e0, e1, m3) is clockwise and no sliver
            int m3 = mesh.AppendVertex(new Vector3d(1, -0.5, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh, new List<Index3i> { new Index3i(e1, e0, x), new Index3i(e1, e0, m3) }, true));
            var mesh2 = Setup(out e0, out e1, out x, out m1, out m2);
            m3 = mesh2.AppendVertex(new Vector3d(1, -0.5, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh2, new List<Index3i> { new Index3i(e0, e1, m3) }, true));
            // the bound grows with the merge radius: 1e-3 of radius lets a 1e-4 sliver go
            var mesh3 = Setup(out e0, out e1, out x, out m1, out m2);
            int m4 = mesh3.AppendVertex(new Vector3d(1, -1e-4, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh3, new List<Index3i> { new Index3i(e0, e1, m4) }, true));
            Assert.Equal(1, SlopeMap.append_faces(mesh3, new List<KeyValuePair<int, List<int>>> { new KeyValuePair<int, List<int>>(0, new List<int> { e0, e1, m4 }) },
                (v) => new List<Index3i> { new Index3i(v[0], v[1], v[2]) }, (v) => v, true, 1e-3));
        }

        // the bound 2 (ulp(M) + radius) + 16 u L (u = 2^-53) of is_sliver, with the true height just below and just above it
        [Fact]
        public void IsSliver_DecidesAtItsBound()
        {
            double big = Math.Pow(2, 22), h = Math.Pow(2, -29);       // M = 2^22 + 4: ulp 2^-30, 2 ulp = 2^-29; 16 u L = 2^-47
            Func<double, double, double, double, bool> sliver = (x0, y0, hh, r) =>
                SlopeMap.is_sliver(new Vector2d(x0, y0), new Vector2d(x0 + 4, y0), new Vector2d(x0 + 2, y0 - hh), r);
            Assert.True(sliver(big, 0, h, 0));
            Assert.True(sliver(big, 0, h + Math.Pow(2, -50), 0));      // inside the margin for the evaluation
            Assert.False(sliver(big, 0, h + Math.Pow(2, -40), 0));
            Assert.False(sliver(big, 0, 2 * h, 0));
            // M = 4, ulp 2^-50 (2 ulp = 2^-49), 16 u L = 2^-47: the bound is 1.25 * 2^-47
            Assert.True(sliver(0, 0, Math.Pow(2, -47), 0));
            Assert.False(sliver(0, 0, Math.Pow(2, -45), 0));
            // with a merge radius: 2 radius + 2 ulp + 2^-47
            Assert.True(sliver(0, 0, 1.9e-6, 1e-6));
            Assert.False(sliver(0, 0, 2.1e-6, 1e-6));
            // a point is a sliver
            Assert.True(SlopeMap.is_sliver(new Vector2d(1, 1), new Vector2d(1, 1), new Vector2d(1, 1), 0));
        }

        // a strip of slivers of different faces along one line (#1539, 119 degrees: the kept sliver of a face whose diagonal is the long
        // edge of the dropped slivers of another): the long edge E0 E1 of the dropped (E1, M1, E0) and (M1, M2, E0) is the edge of
        // the triangle N below it, and the kept sliver (E0, M2, M1) of the face above has the diagonal E0 M1 with the triangle (E0, M1, Y),
        // so splitting N at M1 meets a third triangle on E0 M1. Strict mode removes the kept sliver too and splits the triangles of
        // both sides at the vertices of the strip.
        [Fact]
        public void AppendFaces_Strict_ZipsAStripOfSliversOfTwoFaces()
        {
            var mesh = new DMesh3(false, false, false, true);
            int e0 = mesh.AppendVertex(new Vector3d(0, 0, 0)), e1 = mesh.AppendVertex(new Vector3d(10, 0, 0));
            int x = mesh.AppendVertex(new Vector3d(5, -5, 0)), y = mesh.AppendVertex(new Vector3d(5, 5, 0));
            int m1 = mesh.AppendVertex(new Vector3d(7, 0, 0)), m2 = mesh.AppendVertex(new Vector3d(4, -1e-15, 0));
            var tris = new List<Index3i> {
                new Index3i(e1, e0, x),      // N, below the line
                new Index3i(e1, m1, e0),     // dropped: exactly collinear
                new Index3i(e0, m2, m1),     // the kept sliver, below the diagonal e0 m1
                new Index3i(e0, m1, y),      // above the diagonal
                new Index3i(m1, e1, y) };
            Func<DMesh3, bool> fine = (mm) => {
                foreach (int t in mm.TriangleIndices()) {
                    Index3i tv = mm.GetTriangle(t);
                    if (ExactPredicates.Orient2d(mm.GetVertex(tv.a).xy, mm.GetVertex(tv.b).xy, mm.GetVertex(tv.c).xy) <= 0) return false;
                }
                return mm.CheckValidity(true, FailMode.ReturnOnly) && MeshRepairMetrics.InconsistentlyOrientedEdges(mm) == 0;
            };
            var plain = new DMesh3(mesh);
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(plain, tris, false));
            var strict = new DMesh3(mesh);
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(new DMesh3(mesh), tris, true));      // the strict stitch does not meet it either
            Append(strict, tris, true, true);
            Assert.True(fine(strict));
            // closed along the line: the only boundary is the outer one (the 4 edges of the quadrilateral e0 x e1 y)
            var boundary = strict.EdgeIndices().Where(strict.IsBoundaryEdge).Select(e => ordered(strict.GetEdgeV(e).a, strict.GetEdgeV(e).b)).ToList();
            Assert.Equal(4, boundary.Count);       // the outer ring: e0 x, x e1, e1 y, y e0
            Assert.False(boundary.Contains(ordered(e0, e1)));
            // (N is split at m1; m2 lost its triangles and is left out, no T-junction)
            Assert.True(strict.FindEdge(m1, x) >= 0);
            Assert.Equal(4, strict.TriangleCount);
        }

        // a vertex of the strip that is a corner of a real triangle elsewhere: splitting the edge there would pinch the mesh at it (two
        // boundary loops through one vertex): the zip refuses (the plain stitch splits, and leaves the bowtie)
        [Fact]
        public void AppendFaces_Zip_RefusesASplitThatPinchesTheMesh()
        {
            var mesh = new DMesh3(false, false, false, true);
            int p = mesh.AppendVertex(new Vector3d(0, 0, 0)), q = mesh.AppendVertex(new Vector3d(10, 0, 0));
            int x = mesh.AppendVertex(new Vector3d(5, -5, 0)), v = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int a = mesh.AppendVertex(new Vector3d(6, 2, 0)), b = mesh.AppendVertex(new Vector3d(4, 2, 0));
            var tris = new List<Index3i> { new Index3i(q, p, x), new Index3i(q, v, p), new Index3i(v, a, b) };    // below the line; dropped (collinear); touches v from above
            var stitched = new DMesh3(mesh);
            Append(stitched, tris, true);                                                       // strict (stitch): v is pinched, no refusal
            Assert.True(stitched.VtxEdgesItr(v).Count(stitched.IsBoundaryEdge) > 2);
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(new DMesh3(mesh), tris, true, true));
        }

        // ---- the flip share rerun (#1539, angle 301) ----

        [Fact]
        public void FlipExtra_IsZeroAtScaleOne_AndScalesWithTheFrame()
        {
            double exy = 5.684341886080802e-14, s = 1.0;
            Assert.Equal(0.0, TerrainSlopeDesign.FlipExtra(exy, s, 1));
            // the F - 1 further shares, rounded up: at least (F - 1) FlipShare, and 2^k exy gives 2^k times as much, bit for bit
            foreach (double f in new[] { 1024.0, 2048.0, 1048576.0 }) {
                double e = TerrainSlopeDesign.FlipExtra(exy, s, f);
                Assert.True(e >= (f - 1) * TerrainSlopeDesign.FlipShare(exy, s));
                Assert.Equal(1024 * e, TerrainSlopeDesign.FlipExtra(1024 * exy, s, f));
                Assert.Equal(e / 4096, TerrainSlopeDesign.FlipExtra(exy / 4096, s, f));
            }
        }

        [Fact]
        public void FlipScaleFor_IsAPowerOfTwoAboveTwiceTheRatio_WithinTheRange()
        {
            double lo = Math.Pow(2, TerrainSlopeDesign.MinFlipScaleLog2), hi = Math.Pow(2, TerrainSlopeDesign.MaxFlipScaleLog2);
            Assert.Equal(lo, TerrainSlopeDesign.FlipScaleFor(7.4, 1));            // 301: 7.4 shares, the minimum
            Assert.Equal(lo, TerrainSlopeDesign.FlipScaleFor(lo / 2, 1));         // 2 ratio = lo: not above it
            Assert.Equal(2 * lo, TerrainSlopeDesign.FlipScaleFor(lo / 2 + 1, 1));
            Assert.Equal(8192.0, TerrainSlopeDesign.FlipScaleFor(Math.Pow(2, 12), 1));
            Assert.Equal(16384.0, TerrainSlopeDesign.FlipScaleFor(Math.Pow(2, 12) + 1, 1));
            Assert.Equal(hi, TerrainSlopeDesign.FlipScaleFor(1e30, 1));           // capped
            Assert.Equal(hi, TerrainSlopeDesign.FlipScaleFor(hi, 1));
            Assert.Equal(2 * lo, TerrainSlopeDesign.FlipScaleFor(7.4, lo));       // above the last scale
            Assert.Equal(hi, TerrainSlopeDesign.FlipScaleFor(7.4, hi));           // (the cap stays: the caller stops there)
            Assert.Equal(lo, TerrainSlopeDesign.FlipScaleFor(double.NaN, 1));
        }

        [Fact]
        public void MayMerge_CountsTheExtraShare()
        {
            Assert.True(TerrainSlopeDesign.MayMerge(1.0, 0, 1.0));
            Assert.False(TerrainSlopeDesign.MayMerge(1.0, 0, 0.999));
            Assert.False(TerrainSlopeDesign.MayMerge(1.0, 0.5, 1.4));       // 1 + 0.5 > 1.4: no room for merges and flips
            Assert.True(TerrainSlopeDesign.MayMerge(1.0, 0.5, 1.6));
        }

        [Fact]
        public void FlipExtraBudgets_AddTheExtraOnlyInARerun()
        {
            Assert.Equal(3.0, TerrainSlopeDesign.WithFlipExtra(3.0, 0));
            Assert.True(TerrainSlopeDesign.WithFlipExtra(3.0, 0.5) >= 3.5);
            Assert.True(TerrainSlopeDesign.WithFlipExtra(3.0, 0.5) < 3.5000001);
            Assert.Equal(3.0, TerrainSlopeDesign.BandRoom(3.0, 0));
            Assert.True(TerrainSlopeDesign.BandRoom(3.0, 0.5) <= 2.5);
            Assert.True(TerrainSlopeDesign.BandRoom(3.0, 0.5) > 2.4999999);
        }

        // the band radius with the room that a scaled flip share leaves under the lambda bound is smaller (the call site in build passes BandRoom)
        [Fact]
        public void BandMergeRadius_ShrinksWithTheRoomOfAScaledFlipShare()
        {
            double exy = 1e-13, ez = 1e-12, sMax = 1, minFeature = 1e-6, slack = 1, bound = 1e-6, extra = 5e-7;
            double full = TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, minFeature, slack, TerrainSlopeDesign.BandRoom(bound, 0));
            double less = TerrainSlopeDesign.BandMergeRadius(exy, ez, sMax, minFeature, slack, TerrainSlopeDesign.BandRoom(bound, extra));
            Assert.True(full > 4e-7 && full <= 5e-7, "full " + full);
            Assert.True(less < full / 1.9 && less > 0, "less " + less);
        }

        // 301: the plain attempts fail with a refused flip, the rerun at F = 2^10 passes (the shared design, see the regression tests)
        [Fact, Trait("Category", "Slow")]
        public void AroundCorner_301_RerunsWithAScaledFlipShare()
        {
            var d = TerrainSlopeRampRotationRegressionTests.Reference301.Value;
            Assert.Equal(Math.Pow(2, TerrainSlopeDesign.MinFlipScaleLog2), d.UsedFlipScale);
            Assert.True(d.NeedleFlips > 0);
            Assert.True(d.UsedToeLowering <= d.Tolerance);
            Assert.True(d.UsedToeLowering >= d.MergeLambdaRequired);
            // the lowering the flips need counts the F - 1 further shares (4.39e-9 = 2.50e-9 + 1.89e-9 at 301)
            Assert.True(d.MergeLambdaRequired >= (d.UsedFlipScale - 1) * d.BaseFlipShare, "need " + d.MergeLambdaRequired);
        }

        // 3000 times the share (5.7e-6, still within the lambda bound): the lowering the flips need is at least the extra share, which dominates it
        // (Slow: one design of 301, 7 s alone)
        [Fact, Trait("Category", "Slow")]
        public void AroundCorner_301_LargeExtraShare_RaisesTheNeededLowering()
        {
            var d = TerrainSlopeRampRotationRegressionTests.Make(301);
            d.FlipScaleTestFactor = 3000;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(d.NeedleFlips > 0);
            Assert.True(d.MergeLambdaRequired >= (3000 * d.UsedFlipScale - 1) * d.BaseFlipShare, "need " + d.MergeLambdaRequired);
        }

        // a rerun whose extra share leaves no room for the merge under the lambda bound (3e6 times the share: 5.7e-3, within Tolerance) runs
        // without merges and flips: mayMerge counts the extra share (Nightly: the first attempts, then one rerun, 12 to 13 s alone, and no
        // cheaper input triggers the rerun; the check is the run itself)
        [Fact, Trait("Category", "Nightly")]
        public void AroundCorner_301_ExtraShareWithoutRoom_RunsWithoutFlips()
        {
            var d = TerrainSlopeRampRotationRegressionTests.Make(301);
            d.FlipScaleTestFactor = 3e6;
            d.Compute();
            Assert.Equal(1024.0, d.UsedFlipScale);
            Assert.Equal(0, d.NeedleFlips);
        }

        // where the scaled share would not fit Tolerance, the rerun is refused by the budget and the design keeps its SlopeViolation with the
        // diagnostics of the plain run (Slow: the first attempts, the refused rerun, then the same instance again without the rerun: the
        // reuse of an instance after a rerun, and the plain run to compare with). The ratio is scaled so that F reaches the cap 2^20,
        // and the budget refuses it there
        [Fact, Trait("Category", "Slow")]
        public void AroundCorner_301_RerunRefusedByTheBudget_KeepsTheSlopeViolation_AndReuses()
        {
            var d = TerrainSlopeRampRotationRegressionTests.Make(301);
            d.FlipRatioTestFactor = 1e12;
            d.FlipScaleTestFactor = 1e12;       // F x 1e12 shares of 1.8e-12: more than Tolerance 1e-2 at every F
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, d.Status);
            Assert.Contains("triangles failed the exact check", d.Message);
            Assert.Equal(1048576.0, d.UsedFlipScale);      // tried once, at the cap, refused
            Assert.NotEmpty(d.ProblemTriangles);
            string message = d.Message;
            var problems = d.ProblemTriangles.ToList();
            var altitudes = d.AttemptProblemAltitudes.Select(l => l.ToList()).ToList();
            double[] numbers = { d.MergeLambdaRequired, d.NeedleFlips, d.MergedPositions, d.MaxSlopeRatio, d.UsedSlopeMargin, d.UsedToeLowering, d.UsedMinFeatureSize, d.FacetAngle, d.FanTolerance, d.UsedRampShare, d.UsedMergeRadius, d.UsedBandMergeRadius };
            Assert.True(d.UsedSlopeMargin > 0 && d.UsedMinFeatureSize > 0);

            // the same instance again, without the rerun: the plain run's diagnostics, which the refused rerun restored
            d.DisableFlipRerun = true;
            Assert.False(d.Compute());
            Assert.Equal(1.0, d.UsedFlipScale);
            Assert.Equal(message, d.Message);
            Assert.Equal(problems, d.ProblemTriangles);
            Assert.Equal(altitudes.Count, d.AttemptProblemAltitudes.Count);
            for (int i = 0; i < altitudes.Count; ++i) Assert.Equal(altitudes[i], d.AttemptProblemAltitudes[i]);
            double[] again = { d.MergeLambdaRequired, d.NeedleFlips, d.MergedPositions, d.MaxSlopeRatio, d.UsedSlopeMargin, d.UsedToeLowering, d.UsedMinFeatureSize, d.FacetAngle, d.FanTolerance, d.UsedRampShare, d.UsedMergeRadius, d.UsedBandMergeRadius };
            Assert.Equal(numbers, again);
        }

        static Index2i ordered(int a, int b) { return a < b ? new Index2i(a, b) : new Index2i(b, a); }

        // 96 degrees: a real triangle above the edge a b over a sliver that was appended first, which runs along a -> b as well
        [Fact]
        public void AppendFaces_Strict_RemovesTheSliverUnderARealTriangle()
        {
            var mesh = new DMesh3(false, false, false, true);
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(6.4, 0, 0));
            int q = mesh.AppendVertex(new Vector3d(3, 1e-15, 0)), p = mesh.AppendVertex(new Vector3d(2, 3, 0));
            var tris = new List<Index3i> { new Index3i(a, b, q), new Index3i(a, b, p) };       // the sliver first, then the real triangle
            var plain = new DMesh3(mesh);
            Append(plain, tris, false);
            Assert.True(MeshRepairMetrics.InconsistentlyOrientedEdges(plain) > 0);              // as before: both stay, one over the other
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(new DMesh3(mesh), tris, true));       // strict alone: a real triangle over a sliver is a defect
            var strict = new DMesh3(mesh);
            Assert.Equal(1, Append(strict, tris, true, true));
            Assert.Equal(2, strict.TriangleCount);                                              // the real triangle, split at q
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(strict));
            Assert.True(strict.FindEdge(q, p) >= 0);
            foreach (int t in strict.TriangleIndices()) {
                Index3i tv = strict.GetTriangle(t);
                Assert.Equal(1, ExactPredicates.Orient2d(strict.GetVertex(tv.a).xy, strict.GetVertex(tv.b).xy, strict.GetVertex(tv.c).xy));
            }
        }
    }
}
