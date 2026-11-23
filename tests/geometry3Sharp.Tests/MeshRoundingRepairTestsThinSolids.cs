using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Reasons = g3.MeshRoundingRepair.RemovalReasons;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1216: SafeRounding removed every closed component that was thinner than 1e-5 of the diagonal and had an edit anywhere, so a
    /// real sheet went for two contractions of 7.6e-11 and the boolean returned an empty result with Success. Owner decision
    /// 2026-11-22 (docs/research/ROUNDING_THIN_SOLIDS_2026-11.md, rule d1): an edited component that is tiny or thin by the bound
    /// goes only if it was thinner than 4 ulps of its largest coordinate before the edits, if a defect is left in it, or if the
    /// edits changed its volume by more than a factor 2; every removed component is reported, and a removal of a component the
    /// coordinates resolve makes the result not clean. All inputs are built in code (the sheet of the issue and its frames).
    /// </summary>
    public class MeshRoundingRepairTestsThinSolids
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            var x = new[] { x0, x1 }; var y = new[] { y0, y1 }; var z = new[] { z0, z1 };
            for (int i = 0; i < 8; i++) m.AppendVertex(new Vector3d(x[i & 1], y[(i >> 1) & 1], z[(i >> 2) & 1]));
            int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            for (int i = 0; i < f.Length; i += 3) m.AppendTriangle(f[i], f[i + 1], f[i + 2]);
            return m;
        }

        static void Turn(DMesh3 m, Quaterniond q)
        {
            foreach (int vid in m.VertexIndices().ToArray()) m.SetVertex(vid, q * m.GetVertex(vid));
        }

        // the repro of the issue, scaled: A = [0,L]^2 x [0,L/10] minus B = [-L/100, 1.01 L]^2 x [t, L/5]: a sheet of thickness t.
        // Frames: axis; rz (30 degrees about z); issue (B tilted by 1e-4 degrees about x through (L/2, L/2, t), then rz); gen (one
        // generic rotation); gena (gen, with B's sides shifted so that no edge of B passes over a corner of A)
        static (DMesh3 a, DMesh3 b) Sheet(double L, double t, string frame)
        {
            bool apart = frame == "gena";
            DMesh3 a = Box(0, 0, 0, L, L, L / 10), b = Box(-L / 100, apart ? -L / 50 : -L / 100, t, apart ? 1.03 * L : 1.01 * L, 1.01 * L, L / 5);
            if (frame == "issue") {
                var pivot = new Vector3d(L / 2, L / 2, t);
                Quaterniond tilt = Quaterniond.AxisAngleD(Vector3d.AxisX, 1e-4);
                foreach (int vid in b.VertexIndices().ToArray())
                    b.SetVertex(vid, pivot + tilt * (b.GetVertex(vid) - pivot));
            }
            if (frame == "issue" || frame == "rz") {
                Quaterniond rz = Quaterniond.AxisAngleD(Vector3d.AxisZ, 30);
                Turn(a, rz); Turn(b, rz);
            } else if (frame == "gen" || frame == "gena") {
                Quaterniond q = Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23);
                Turn(a, q); Turn(b, q);
            }
            return (a, b);
        }

        static ExactMeshBoolean Difference(DMesh3 a, DMesh3 b, bool safe = true, bool strict = false)
        {
            var op = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Difference) { SafeRounding = safe, StrictRounding = strict };
            op.Compute();
            return op;
        }

        // closed, manifold, finite, clean: what a kept result must be
        static void AssertSolid(DMesh3 m)
        {
            Assert.True(m.TriangleCount > 0, "empty");
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary>
        /// the three rows of the issue's table near the origin: the sheet is in the result, with the exact result's volume up to what
        /// the edits may change (their drift times the area), nothing is removed, the result is clean. Before: empty, Success
        /// </summary>
        [Theory]
        [InlineData(1e-3, 10)]
        [InlineData(1e-4, 1)]
        [InlineData(1e-5, 0.271)]
        public void SheetOfTheIssue_NearTheOrigin_IsKept(double t, double volume)
        {
            var (a, b) = Sheet(100, t, "issue");
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            AssertSolid(op.Result);
            Assert.True(op.RoundingClean);
            Assert.Equal(0, op.Rounding.RemovedComponents);
            Assert.Empty(op.Rounding.RemovedComponentInfos);
            // (the repair did edit the sheet: that is what removed it before)
            Assert.True(op.Rounding.Collapses > 0);
            double exact = Invariance.Volume(plain.Result);
            Assert.InRange(exact, 0.99 * volume, 1.01 * volume);
            Assert.True(Math.Abs(Invariance.Volume(op.Result) - exact) <= op.Rounding.MaxDrift * Invariance.Area(plain.Result) + 1e-12 * exact);
        }

        /// <summary> the same sheet under StrictRounding (as SolidBooleanPipeline runs it): Success, not RoundingNotClean </summary>
        [Fact]
        public void SheetOfTheIssue_Strict_Succeeds()
        {
            var (a, b) = Sheet(100, 1e-3, "issue");
            ExactMeshBoolean op = Difference(a, b, strict: true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            AssertSolid(op.Result);
        }

        /// <summary>
        /// one threshold in every frame: sheets of 2^-16 to 2^-34 of the size are kept axis-aligned, turned about z, tilted, under a
        /// generic rotation, and without any coincidence of the operands. Before, the frames issue and gen lost all of them and gena
        /// those from 2^-22 (the thinnest sheet kept moved by seven orders of magnitude with the frame)
        /// </summary>
        [Theory]
        [InlineData("axis")]
        [InlineData("rz")]
        [InlineData("issue")]
        [InlineData("gen")]
        [InlineData("gena")]
        public void Sheet_IsKept_InEveryFrame(string frame)
        {
            foreach (int k in new[] { 16, 22, 28, 34 }) {
                var (a, b) = Sheet(1, Math.Pow(2, -k), frame);
                ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
                AssertSolid(op.Result);
                Assert.True(op.RoundingClean, $"{frame} k {k}");
                Assert.Equal(0, op.Rounding.RemovedComponents);
                // (within 1 %: where the sheet's corner triangles are below the degenerate area the repair contracts them)
                double exact = Invariance.Volume(plain.Result);
                Assert.InRange(Invariance.Volume(op.Result), 0.99 * exact, 1.01 * exact);
            }
        }

        // the audit's case: the box [0,1024]^3 united with a cube of edge e beside it
        static (DMesh3 a, DMesh3 b) BoxAndCube(double e) => (Box(0, 0, 0, 1024, 1024, 1024), Box(1100, 0, 0, 1100 + e, e, e));

        static ExactMeshBoolean Union(DMesh3 a, DMesh3 b, bool strict = false)
        {
            var op = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Union) { StrictRounding = strict };
            op.Compute();
            return op;
        }

        /// <summary>
        /// a solid that matters at the scale of the mesh and does not survive the repair: the cube of edge 2^-11 beside the box of
        /// 1024 (its triangles are below the degenerate area of the result, the repair collapses it to 245 times its volume). It
        /// is removed, as before, but now listed with the volume and thickness of the exact result's cube, and the result is not
        /// clean: the cube is 7e8 ulps and 9e-8 of the diagonal thick. Strict: refused
        /// </summary>
        [Fact]
        public void MangledSolid_IsRemoved_Reported_AndNotClean()
        {
            double e = Math.Pow(2, -11);
            var (a, b) = BoxAndCube(e);
            ExactMeshBoolean op = Union(a, b);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal(12, op.Result.TriangleCount);
            Assert.Equal(1, op.Rounding.RemovedComponents);
            Assert.Equal(1, op.Rounding.RemovedResolvable);
            Assert.False(op.RoundingClean);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(op.Rounding.RemovedComponentInfos);
            Assert.Equal(Reasons.VolumeChanged, info.Reason);
            Assert.True(info.Resolvable);
            Assert.Equal(op.Rounding.RemovedTriangles, info.Triangles);
            // (the sums are taken in doubles relative to a corner of the cube)
            Assert.InRange(info.Volume, e * e * e * (1 - 1e-9), e * e * e * (1 + 1e-9));
            Assert.InRange(info.Area, 6 * e * e * (1 - 1e-9), 6 * e * e * (1 + 1e-9));
            Assert.InRange(info.Thickness, e / 3 * (1 - 1e-9), e / 3 * (1 + 1e-9));
            Assert.True(info.ThicknessUlps > 1e8);
            Assert.True(info.Thickness > MeshRoundingRepair.ReportBoundFraction * op.Rounding.MaxLength);
            Assert.True(info.Bounds.Min.x >= 1100 - 1 && info.Bounds.Max.x <= 1101);
            Assert.Equal(1, op.Counts["rounding_removed_resolvable"]);

            ExactMeshBoolean strict = Union(a, b, strict: true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, strict.Status);
            Assert.Null(strict.Result);
            Assert.Contains("1 components removed that their coordinates resolve", strict.Error);
            Assert.DoesNotContain("effort limit", strict.Error);
            // (nothing follows the counts where no flag of the repair is set)
            Assert.EndsWith("1 components removed that their coordinates resolve (still defective, or changed by the repair)", strict.Error);
        }

        /// <summary>
        /// the verdict has its own thresholds (owner 2026-11-22): a sheet whose own side triangles are below the degenerate area
        /// (2^-43 and 2^-46 of the size, turned about z) is mangled by the repair (22 and 175 times the volume) and removed, as
        /// before; it is listed as VolumeChanged with the exact result's volume and thickness. At 512 and 64 ulps and 1e-13 of the
        /// diagonal it is below what makes a result not clean (256 ulps and 1e-3 of the bound): clean; strict: Success, empty
        /// </summary>
        [Theory]
        [InlineData(43, 256, 1024)]
        [InlineData(46, 32, 128)]
        public void MangledSheet_BelowTheReportThresholds_IsRemoved_Listed_AndClean(int k, double ulpsLow, double ulpsHigh)
        {
            double t = Math.Pow(2, -k);
            var (a, b) = Sheet(1, t, "rz");
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            Assert.Equal(0, op.Result.TriangleCount);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(op.Rounding.RemovedComponentInfos);
            Assert.Equal(Reasons.VolumeChanged, info.Reason);
            Assert.InRange(info.Volume, 0.999 * Invariance.Volume(plain.Result), 1.001 * Invariance.Volume(plain.Result));
            Assert.InRange(info.Area, 0.999 * Invariance.Area(plain.Result), 1.001 * Invariance.Area(plain.Result));
            Assert.InRange(info.Thickness, 0.99 * t, 1.01 * t);
            Assert.InRange(info.ThicknessUlps, ulpsLow, ulpsHigh);
            Assert.True(info.Thickness < MeshRoundingRepair.ReportBoundFraction * op.Rounding.MaxLength);
            Assert.False(info.Resolvable);
            Assert.Equal(0, op.Rounding.RemovedResolvable);
            Assert.True(op.RoundingClean);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, Difference(a, b, strict: true).Status);
        }

        // the measurement of a component as the repair takes it (area, volume, largest coordinate, the sum of the norms)
        static bool Below(DMesh3 m, double factor) => Measured(m, (comp, area, volume, largest, norms) => MeshRoundingRepair.numerically_zero_volume(m, comp, volume, area, largest, norms, factor));
        static bool Thinner(DMesh3 m, double length) => Measured(m, (comp, area, volume, largest, norms) => MeshRoundingRepair.thinner_than(m, comp, volume, area, norms, length));

        static bool Measured(DMesh3 m, Func<List<int>, double, double, double, double, bool> test)
        {
            var comp = m.TriangleIndices().ToList();
            double area = 0, volume = 0, largest = 0, norms = 0;
            Vector3d o = m.GetVertex(m.GetTriangle(comp[0]).a);
            foreach (int t in comp) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                area += 0.5 * (b - a).Cross(c - a).Length;
                volume += (a - o).Dot((b - o).Cross(c - o)) / 6;
                norms += (a - o).Length * (b - o).Length * (c - o).Length;
                largest = Math.Max(largest, Math.Max(a.MaxAbs, Math.Max(b.MaxAbs, c.MaxAbs)));
            }
            return test(comp, area, volume, largest, norms);
        }

        /// <summary>
        /// both thresholds, exactly, from both sides: the box [0,1]^2 x [0,h] has the area 2 + 4 h and the volume h without a
        /// rounding, and its largest coordinate 1 has the ulp u = 2^-52, so "thinner than K ulps" is h &lt; K u (1 + 2 h). h = K u is
        /// below; h = K u (1 + 2^-40), one part in 1e12 thicker, is not (the difference to the threshold is 2 K u h, far smaller)
        /// </summary>
        [Theory]
        [InlineData(4)]
        [InlineData(256)]
        public void Thresholds_AreExact_FromBothSides(double k)
        {
            double u = Math.Pow(2, -52);
            Assert.True(Below(Box(0, 0, 0, 1, 1, k * u), k));
            Assert.False(Below(Box(0, 0, 0, 1, 1, k * u * (1 + Math.Pow(2, -40))), k));
            // (and each is told from its neighbours: twice the factor takes the thicker one too, half of it neither)
            Assert.True(Below(Box(0, 0, 0, 1, 1, k * u * (1 + Math.Pow(2, -40))), 2 * k));
            Assert.False(Below(Box(0, 0, 0, 1, 1, k * u), k / 2));
            Assert.Equal(4, MeshRoundingRepair.ResolutionUlps);
            Assert.Equal(256, MeshRoundingRepair.ReportUlps);
        }

        /// <summary>
        /// the third threshold, a length (the fraction of the bound), exactly, from both sides: the same box is thinner than a
        /// length l where h &lt; l (1 + 2 h). h = l is thinner; h = l (1 + 2^-20) is not (2^-20 is more than 2 h). With a power of
        /// two, where every product is exact, and with a length that is not one (the test is exact for the computed l x area)
        /// </summary>
        [Theory]
        [InlineData(7.450580596923828125e-9)]      // 2^-27
        [InlineData(1.7320508e-8)]
        public void BoundFraction_IsExact_FromBothSides(double l)
        {
            Assert.True(Thinner(Box(0, 0, 0, 1, 1, l), l));
            Assert.False(Thinner(Box(0, 0, 0, 1, 1, l * (1 + Math.Pow(2, -20))), l));
            Assert.True(Thinner(Box(0, 0, 0, 1, 1, l * (1 + Math.Pow(2, -20))), 2 * l));
            Assert.False(Thinner(Box(0, 0, 0, 1, 1, l), l / 2));
            // (2^k scales: the same answers, the length scaled with the box)
            foreach (double f in new[] { 1.0 / 1024, 1024 }) {
                Assert.True(Thinner(Box(0, 0, 0, f, f, f * l), f * l));
                Assert.False(Thinner(Box(0, 0, 0, f, f, f * l * (1 + Math.Pow(2, -20))), f * l));
            }
            Assert.Equal(1e-3, MeshRoundingRepair.ReportBoundFraction);
        }

        // a sphere of radius 1 (the largest component: it sets the bound), a slab of 0.5 x 0.5 x h beside it with a needle (edited)
        // and a cube through the slab (an intersection no edit separates): the slab goes as still defective. All under s
        static DMesh3 SlabScene(double h, Similarity s)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.25, 0.25, 0.5 * h)));
            new MeshEditor(m).AppendMesh(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3.01, 0.02, 0.03), 0.1 * Vector3d.One) }.Generate().MakeDMesh());
            return Invariance.Transformed(new DMesh3(m, false, MeshComponents.None), s);
        }

        /// <summary>
        /// invariance of the verdict at its second threshold: a removed slab whose thickness is one part in 1e6 above the fraction
        /// of the bound makes the repair not clean, one part in 1e6 below leaves it clean, under every similarity (the bound is
        /// taken in the frame: it follows the diagonal of the largest component's box); the 2^k scales give the same report bit
        /// for bit. Far from the origin, where 256 ulps are more than the fraction of the bound, the ulps decide: both are clean
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Verdict_AtTheFractionOfTheBound_UnderEverySimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            // the bound of the scene in this frame (the slab does not set it)
            DMesh3 probe = SlabScene(1e-3, s);
            var rp = new MeshRoundingRepair(probe);
            rp.Apply();
            double length = MeshRoundingRepair.ReportBoundFraction * rp.MaxLength / s.Scale;
            MeshRoundingRepair.RemovedComponentInfo[] infos = new MeshRoundingRepair.RemovedComponentInfo[2];
            for (int side = 0; side < 2; ++side) {
                // (the slab's thickness 2 V / A is h / (1 + 4 h))
                double h = length * (1 + (side == 0 ? -1e-6 : 1e-6));
                h *= 1 + 4 * h;
                DMesh3 m = SlabScene(h, s);
                var r = new MeshRoundingRepair(m);
                r.Apply();
                MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
                Assert.Equal(rp.MaxLength, r.MaxLength);
                // (far from the origin the slab is a few ulps thick: there the ulps decide, also what it is removed as)
                bool ulpsDecide = info.ThicknessUlps < 1024;
                Assert.True(info.Reason == Reasons.StillDefective || ulpsDecide, info.Reason.ToString());
                Assert.Equal(side == 1 && ulpsDecide == false, info.Resolvable);
                Assert.Equal(info.Resolvable ? 1 : 0, r.RemovedResolvable);
                Assert.Equal(info.Resolvable == false, r.IsClean);
                Assert.True(m.IsClosed() && Invariance.AllFinite(m));
                infos[side] = info;
            }
            if (s == Similarity.Identity || s == Similarity.Pow2Small || s == Similarity.Pow2Large) {
                Assert.False(infos[0].Resolvable);
                Assert.True(infos[1].Resolvable);
            }
            if (s == Similarity.Pow2Small || s == Similarity.Pow2Large) {
                // the same scene scaled: the same report, bit for bit
                double l0 = MeshRoundingRepair.ReportBoundFraction * (rp.MaxLength / s.Scale);
                double h0 = l0 * (1 + 1e-6);
                h0 *= 1 + 4 * h0;
                var r0 = new MeshRoundingRepair(SlabScene(h0, Similarity.Identity));
                r0.Apply();
                Assert.Equal(r0.RemovedComponentInfos[0].Thickness * s.Scale, infos[1].Thickness);
                Assert.Equal(r0.RemovedComponentInfos[0].ThicknessUlps, infos[1].ThicknessUlps);
                Assert.Equal(r0.RemovedComponentInfos[0].Resolvable, infos[1].Resolvable);
            }
        }

        /// <summary>
        /// where the coordinates are coarse the ulps decide the verdict: far from the origin at a small scale the bound is its ulp
        /// term (256 ulps), so its fraction is below one ulp, and a component that is thin by that bound is below 256 ulps. A
        /// removed slab of 32 or 128 ulps leaves the repair clean (the fraction of the bound alone would call it resolvable)
        /// </summary>
        [Theory]
        [InlineData(32, false)]
        [InlineData(128, false)]
        public void Verdict_WhereTheCoordinatesAreCoarse_IsDecidedByTheUlps(double ulps, bool resolvable)
        {
            Similarity s = Similarity.RotatedUtmMillimetre;
            DMesh3 probe = SlabScene(1e-3, s);
            double largest = probe.Vertices().Max(v => v.MaxAbs);
            double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(largest) + 1) - largest;
            DMesh3 m = SlabScene(ulps * ulp / s.Scale, s);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.True(MeshRoundingRepair.ReportBoundFraction * r.MaxLength < ulp);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.InRange(info.ThicknessUlps, ulps / 4, ulps * 4);
            Assert.Equal(resolvable, info.Resolvable);
            Assert.Equal(resolvable ? 1 : 0, r.RemovedResolvable);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
        }

        // a closed cube of half edge h at c with a needle (an edge split, the new vertex 1e-12 beside an end): one contraction repairs it
        static DMesh3 CubeWithNeedle(Vector3d c, Vector3d h)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(c, h) }.Generate().MakeDMesh();
            int eid = cube.EdgeIndices().First();
            Index2i ev = cube.GetEdgeV(eid);
            Vector3d a = cube.GetVertex(ev.a), b = cube.GetVertex(ev.b);
            Assert.Equal(MeshResult.Ok, cube.SplitEdge(eid, out DMesh3.EdgeSplitInfo split));
            cube.SetVertex(split.vNew, a + 1e-12 * (b - a).Normalized);
            return cube;
        }

        /// <summary>
        /// a component of exactly zero volume and zero area (a closed tetrahedron with its four corners on one line) is below every
        /// resolution: it goes as BelowResolution and the repair stays clean. (The K-ulp test cannot decide it, its threshold
        /// K ulp x area is zero; without the exact volume it went as still defective and made the result not clean: two results
        /// of the Thingi10K contact cohort)
        /// </summary>
        [Fact]
        public void ZeroAreaComponent_IsBelowTheResolution()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            int n = m.TriangleCount;
            new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(-3, 0, 0), 0.1 * Vector3d.One));
            var flat = new DMesh3();
            for (int i = 0; i < 4; i++) flat.AppendVertex(new Vector3d(3 + 0.125 * i, 0.5, 0.25));
            foreach (int[] t in new[] { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } }) flat.AppendTriangle(t[0], t[1], t[2]);
            new MeshEditor(m).AppendMesh(flat);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.BelowResolution, info.Reason);
            Assert.Equal(0.0, info.Volume);
            // (no area: the thickness is reported as 0, not as 0 / 0)
            Assert.Equal(0.0, info.Area);
            Assert.Equal(0.0, info.Thickness);
            Assert.Equal(0.0, info.ThicknessUlps);
            Assert.False(info.Resolvable);
            Assert.Equal(0, r.RemovedResolvable);
            Assert.True(r.IsClean);
            Assert.Equal(n + 12, m.TriangleCount);
            AssertSolid(m);
        }

        /// <summary>
        /// a defect left in a component can be an intersection alone: a thin slab that the repair edits (one needle) and that a cube
        /// passes through, which no edit within the bound separates. The slab goes as still defective (it is 1e11 ulps: not clean);
        /// the cube, neither thin nor tiny, stays, and no intersection is left
        /// </summary>
        [Fact]
        public void ThinComponent_WithAnIntersectionLeft_IsRemovedAsStillDefective()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            int n = m.TriangleCount;
            new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 1e-5)));
            new MeshEditor(m).AppendMesh(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3.01, 0.02, 0.03), 0.1 * Vector3d.One) }.Generate().MakeDMesh());
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.True(r.InitialIntersections > 0);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.StillDefective, info.Reason);
            Assert.True(info.Resolvable);
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.False(r.IsClean);
            Assert.Equal(n + 12, m.TriangleCount);
            AssertSolid(m);
        }

        /// <summary>
        /// the factor is 2: the sheet of 2^-40 ends at 3.1 times its volume and goes (a factor 4 would keep it); the cube of
        /// 2^-9 beside the box of 1024 ends as a tetrahedron of 1.85 times its volume and stays (the borderline of the study)
        /// </summary>
        [Fact]
        public void VolumeFactor_IsTwo()
        {
            var (a, b) = Sheet(1, Math.Pow(2, -40), "rz");
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(op.Rounding.RemovedComponentInfos);
            Assert.Equal(Reasons.VolumeChanged, info.Reason);
            Assert.Equal(0, op.Result.TriangleCount);

            double e = Math.Pow(2, -9);
            var union = new ExactMeshBoolean(Box(0, 0, 0, 1024, 1024, 1024), Box(1100, 0, 0, 1100 + e, e, e), Ops.Union);
            Assert.True(union.Compute());
            Assert.Equal(0, union.Rounding.RemovedComponents);
            DMesh3[] parts = MeshConnectedComponents.Separate(union.Result);
            Assert.Equal(2, parts.Length);
            double small = parts.Min(m => Math.Abs(Invariance.Volume(m)));
            Assert.InRange(small / (e * e * e), 1.5, 2);
        }

        // a box under a generic rotation, and its copy moved by d along a generic direction
        static (DMesh3 a, DMesh3 b, double ulp) Copies(double ulps)
        {
            DMesh3 a = Box(-0.5, -0.5, -0.5, 0.5, 0.5, 0.5);
            Turn(a, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
            double largest = a.Vertices().Max(v => v.MaxAbs);
            double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(largest) + 1) - largest;
            var b = new DMesh3(a);
            Vector3d d = ulps * ulp * new Vector3d(0.61, -0.37, 0.7).Normalized;
            foreach (int vid in b.VertexIndices().ToArray()) b.SetVertex(vid, b.GetVertex(vid) + d);
            return (a, b, ulp);
        }

        /// <summary>
        /// the slivers of A minus a copy 1, 2 or 4 ulps away (what the removal is for, owner 2026-11-16): all go, as before; every one
        /// is listed, none counts as a resolvable solid (all are far below 256 ulps), and the result stays clean
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void SliversOfNearlyCoincidentCopies_Go_AndTheResultIsClean(double ulps)
        {
            var (a, b, ulp) = Copies(ulps);
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            Assert.True(plain.Result.TriangleCount > 0);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal(0, op.Result.TriangleCount);
            Assert.True(op.Rounding.RemovedComponents > 0);
            Assert.Equal(op.Rounding.RemovedComponents, op.Rounding.RemovedComponentInfos.Count);
            Assert.Equal(op.Rounding.RemovedTriangles, op.Rounding.RemovedComponentInfos.Sum(i => i.Triangles));
            Assert.Contains(op.Rounding.RemovedComponentInfos, i => i.Reason == Reasons.BelowResolution);
            Assert.All(op.Rounding.RemovedComponentInfos, i => Assert.True(i.ThicknessUlps < 256 && i.Resolvable == false, i.Reason + " " + i.ThicknessUlps));
            Assert.Equal(0, op.Rounding.RemovedResolvable);
            Assert.True(op.RoundingClean);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, Difference(a, b, strict: true).Status);
        }

        /// <summary>
        /// the shell between copies 2^30 ulps apart (1e-7 of the size) is a solid its coordinates resolve: it is in the result,
        /// closed and clean. Before, it was removed
        /// </summary>
        [Fact]
        public void ShellOfCopiesFarApart_IsKept()
        {
            var (a, b, ulp) = Copies(Math.Pow(2, 30));
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            AssertSolid(op.Result);
            Assert.True(op.RoundingClean);
            Assert.Equal(0, op.Rounding.RemovedComponents);
            double exact = Invariance.Volume(plain.Result);
            Assert.InRange(Invariance.Volume(op.Result), 0.9 * exact, 1.1 * exact);
        }

        static DMesh3 FromBits(ulong[] bits)
        {
            var m = new DMesh3();
            for (int i = 0; i < bits.Length; i += 3)
                m.AppendVertex(new Vector3d(BitConverter.Int64BitsToDouble((long)bits[i]), BitConverter.Int64BitsToDouble((long)bits[i + 1]), BitConverter.Int64BitsToDouble((long)bits[i + 2])));
            int[] f = { 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6 };
            for (int i = 0; i < f.Length; i += 3) m.AppendTriangle(f[i], f[i + 1], f[i + 2]);
            return m;
        }

        // the two cases the Boolean fuzz harness reported (#1382, family slab, seeds 0 and 1): a box minus a thin slab at its top
        static readonly ulong[] Fuzz0A = { 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4000000000000000, 0x0000000000000000, 0x4000000000000000, 0x4000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x4000000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x4000000000000000, 0x4008000000000000, 0x4000000000000000, 0x4000000000000000, 0x4008000000000000 };
        static readonly ulong[] Fuzz0B = { 0xBFF0000000000000, 0xBFF0000000000000, 0x4007BFFFFF400000, 0x4014000000000000, 0xBFF0000000000000, 0x4007C00002400000, 0xBFF0000000000000, 0x4014000000000000, 0x40093FFFFF400000, 0x4014000000000000, 0x4014000000000000, 0x4009400002400000, 0xBFF0000000000000, 0xBFF0000000000000, 0x4007BFFFFF600000, 0x4014000000000000, 0xBFF0000000000000, 0x4007C00002600000, 0xBFF0000000000000, 0x4014000000000000, 0x40093FFFFF600000, 0x4014000000000000, 0x4014000000000000, 0x4009400002600000 };
        static readonly ulong[] Fuzz1A = { 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x3FF0000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x3FF0000000000000, 0x4008000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x3FF0000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x4008000000000000, 0x4008000000000000, 0x3FF0000000000000, 0x4008000000000000, 0x4008000000000000 };
        static readonly ulong[] Fuzz1B = { 0xBFF0000000002667, 0xBFEFFFFFFFFF6666, 0x4007FFFEA88A3ABB, 0x4013FFFFFFFFD000, 0xBFEFFFFFFFFD0000, 0x4007FFF94AB325A7, 0xBFEFFFFFFFFDE668, 0x4013FFFFFFFF799A, 0x40080009643864E3, 0x4014000000001CCD, 0x4013FFFFFFFFC667, 0x4008000406614FCF, 0xBFF0000000002667, 0xBFEFFFFFFFFF6666, 0x4007FFFEA88A3BBB, 0x4013FFFFFFFFD000, 0xBFEFFFFFFFFD0000, 0x4007FFF94AB326A7, 0xBFEFFFFFFFFDE668, 0x4013FFFFFFFF799A, 0x40080009643865E3, 0x4014000000001CCD, 0x4013FFFFFFFFC667, 0x40080004066150CF };

        /// <summary> fuzz case 1: a 12-triangle sheet of 2e9 ulps that two contractions of 2e-12 had removed is kept, volume unchanged </summary>
        [Fact]
        public void FuzzSlab_CleanSheet_IsKept()
        {
            DMesh3 a = FromBits(Fuzz1A), b = FromBits(Fuzz1B);
            ExactMeshBoolean plain = Difference(a, b, safe: false), op = Difference(a, b);
            AssertSolid(op.Result);
            Assert.True(op.RoundingClean);
            Assert.Equal(0, op.Rounding.RemovedComponents);
            Assert.True(op.Rounding.Collapses > 0);
            Assert.InRange(Invariance.Volume(op.Result), Invariance.Volume(plain.Result) - 1e-9, Invariance.Volume(plain.Result) + 1e-9);
            Assert.Equal(2, MeshConnectedComponents.Separate(op.Result).Length);
        }

        /// <summary>
        /// fuzz case 0: a 4-triangle piece with a degenerate triangle of its own that no edit repairs goes, as before; it is listed as
        /// still defective, with its 7e5 ulps. At 3e-10 thick (7e-11 of the diagonal) it is below the fraction of the bound: clean
        /// </summary>
        [Fact]
        public void FuzzSlab_DefectivePiece_IsRemoved_AndListed()
        {
            ExactMeshBoolean op = Difference(FromBits(Fuzz0A), FromBits(Fuzz0B));
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.True(op.Result.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(op.Rounding.RemovedComponentInfos);
            Assert.Equal(Reasons.StillDefective, info.Reason);
            Assert.Equal(4, info.Triangles);
            Assert.InRange(info.ThicknessUlps, 1e5, 1e6);
            Assert.InRange(info.Thickness, 2e-10, 4e-10);
            Assert.False(info.Resolvable);
            Assert.Equal(0, op.Rounding.RemovedResolvable);
            Assert.True(op.RoundingClean);
            Assert.Equal(0, op.Rounding.RemainingDegenerate);
        }

        /// <summary>
        /// #1067 (owner 2026-11-19): the untouched tiny inside-out residue goes and the result stays clean, as before: it is listed as
        /// a residue and does not count as a resolvable solid
        /// </summary>
        [Fact]
        public void InsideOutResidue_Goes_AndTheResultStaysClean()
        {
            DMesh3 m = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(SafeRoundingInsideOutResidueTests.Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 0), true));
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.IsClean);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.InsideOutResidue, info.Reason);
            Assert.Equal(34, info.Triangles);
            Assert.True(info.Volume < 0);
            Assert.Equal(0, r.RemovedResolvable);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> an untouched sheet of numerically zero volume (#1067) goes as before; it is listed as ZeroVolume and the result stays clean </summary>
        [Fact]
        public void ZeroVolumeSheet_IsListed_AndTheResultStaysClean()
        {
            DMesh3 m = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(SafeRoundingInsideOutResidueTests.Bipyramid(17, 5e-4, 1e-20, new Vector3d(3, 0, 0), false));
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.ZeroVolume, info.Reason);
            Assert.True(info.ThicknessUlps < 1);
            Assert.False(info.Resolvable);
            Assert.Equal(0, r.RemovedResolvable);
            AssertSolid(m);
        }

        /// <summary>
        /// the mangled sheet under every similarity: it is gone, what was removed is listed, none of it counts as resolvable (it is
        /// 1e-13 of the diagonal), and the verdict follows the list
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void MangledSheet_UnderEverySimilarity_GoesAndTheVerdictFollowsTheList(string name)
        {
            Similarity s = Similarity.ByName(name);
            var (a, b) = Sheet(1, Math.Pow(2, -43), "rz");
            ExactMeshBoolean op = Difference(Invariance.Transformed(a, s), Invariance.Transformed(b, s));
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal(0, op.Result.TriangleCount);
            Assert.Equal(op.Rounding.RemovedComponents, op.Rounding.RemovedComponentInfos.Count);
            Assert.Equal(op.Rounding.RemovedComponentInfos.Count(i => i.Resolvable), op.Rounding.RemovedResolvable);
            Assert.All(op.Rounding.RemovedComponentInfos, i => Assert.False(i.Resolvable));
            Assert.Equal(op.Rounding.RemovedResolvable == 0, op.RoundingClean);
        }

        /// <summary>
        /// the exact threshold of the resolution: the test of the numerically zero volume with the factor 4 (thickness against 4 ulps
        /// of the largest coordinate, exact for the computed area), from both sides, and the factor 1 unchanged
        /// </summary>
        [Theory]
        [InlineData(3.5, 4, true)]
        [InlineData(4.5, 4, false)]
        [InlineData(0.9, 1, true)]
        [InlineData(1.1, 1, false)]
        [InlineData(3.5, 1, false)]
        public void Resolution_IsFourUlps_FromBothSides(double thicknessUlps, double factor, bool below)
        {
            // a double cone over a 17-gon whose thickness is thicknessUlps units in the last place of its largest coordinate
            const int n = 17;
            double r = 0.0005, cx = 3;
            double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(cx + r) + 1) - (cx + r);
            Func<double, double> thickness = (h) => 2 * (2.0 / 3 * (0.5 * n * r * r * Math.Sin(2 * Math.PI / n)) * h)
                / (2 * n * 0.5 * (2 * r * Math.Sin(Math.PI / n)) * Math.Sqrt(h * h + Math.Pow(r * Math.Cos(Math.PI / n), 2)));
            double h0 = 1e-15;
            DMesh3 m = SafeRoundingInsideOutResidueTests.Bipyramid(n, r, h0 * thicknessUlps * ulp / thickness(h0), new Vector3d(cx, 0, 0), false);
            var comp = m.TriangleIndices().ToList();
            double area = 0, volume = 0, largest = 0, norms = 0;
            Vector3d o = m.GetVertex(m.GetTriangle(comp[0]).a);
            foreach (int t in comp) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                area += 0.5 * (b - a).Cross(c - a).Length;
                volume += (a - o).Dot((b - o).Cross(c - o)) / 6;
                norms += (a - o).Length * (b - o).Length * (c - o).Length;
                largest = Math.Max(largest, Math.Max(a.MaxAbs, Math.Max(b.MaxAbs, c.MaxAbs)));
            }
            Assert.Equal(below, MeshRoundingRepair.numerically_zero_volume(m, comp, volume, area, largest, norms, factor));
            Assert.Equal(4, MeshRoundingRepair.ResolutionUlps);
        }

        /// <summary>
        /// invariance: under every similarity the sheet of the issue is kept, clean, with the exact result's volume; the 2^k scales
        /// give the same result bit for bit
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SheetOfTheIssue_IsKept_UnderEverySimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            var (a, b) = Sheet(100, 1e-3, "issue");
            ExactMeshBoolean reference = Difference(a, b);
            DMesh3 sa = Invariance.Transformed(a, s), sb = Invariance.Transformed(b, s);
            ExactMeshBoolean plain = Difference(sa, sb, safe: false), op = Difference(sa, sb);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            AssertSolid(op.Result);
            Assert.True(op.RoundingClean);
            Assert.Equal(0, op.Rounding.RemovedComponents);
            double exact = Invariance.Volume(plain.Result);
            Assert.InRange(Invariance.Volume(op.Result), exact - 1e-6 * Math.Abs(exact), exact + 1e-6 * Math.Abs(exact));
            if (s == Similarity.Pow2Small || s == Similarity.Pow2Large)
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(op.Result, s)));
        }

        /// <summary> the mangled sheet and the mangled cube under the 2^k scales: the same removal, the same report (scaled), bit for bit </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Mangled_Pow2Scales_GiveTheSameRemoval(bool cube)
        {
            var (a, b) = cube ? BoxAndCube(Math.Pow(2, -11)) : Sheet(1, Math.Pow(2, -43), "rz");
            Func<DMesh3, DMesh3, ExactMeshBoolean> run = (x, y) => cube ? Union(x, y) : Difference(x, y);
            MeshRoundingRepair.RemovedComponentInfo reference = Assert.Single(run(a, b).Rounding.RemovedComponentInfos);
            Assert.Equal(cube, reference.Resolvable);
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                ExactMeshBoolean op = run(Invariance.Transformed(a, s), Invariance.Transformed(b, s));
                MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(op.Rounding.RemovedComponentInfos);
                Assert.Equal(reference.Reason, info.Reason);
                Assert.Equal(reference.Resolvable, info.Resolvable);
                Assert.Equal(reference.Triangles, info.Triangles);
                Assert.Equal(reference.ThicknessUlps, info.ThicknessUlps);
                Assert.Equal(reference.Thickness * s.Scale, info.Thickness);
                Assert.Equal(reference.Volume * s.Scale * s.Scale * s.Scale, info.Volume);
            }
        }

        static string Print(ExactMeshBoolean op)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in op.Result.VertexIndices()) {
                Vector3d p = op.Result.GetVertex(v);
                sb.Append(v).Append(' ').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(' ').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(' ').Append(BitConverter.DoubleToInt64Bits(p.z)).Append('\n');
            }
            foreach (int t in op.Result.TriangleIndices()) sb.Append(op.Result.GetTriangle(t)).Append('\n');
            foreach (MeshRoundingRepair.RemovedComponentInfo i in op.Rounding.RemovedComponentInfos)
                sb.Append(i.Reason).Append(' ').Append(i.Resolvable).Append(' ').Append(i.Triangles).Append(' ').Append(BitConverter.DoubleToInt64Bits(i.Volume)).Append(' ').Append(BitConverter.DoubleToInt64Bits(i.Area)).Append('\n');
            return sb.Append(op.Rounding.RemovedResolvable).ToString();
        }

        public static IEnumerable<object[]> DeterminismCases() => new[] { "kept", "mangled", "cube", "slivers", "fuzz0" }.Select(n => new object[] { n });

        static (DMesh3 a, DMesh3 b, Ops op) Case(string name)
        {
            switch (name) {
                case "kept": { var (a, b) = Sheet(100, 1e-4, "issue"); return (a, b, Ops.Difference); }
                case "mangled": { var (a, b) = Sheet(1, Math.Pow(2, -43), "rz"); return (a, b, Ops.Difference); }
                case "cube": { var (a, b) = BoxAndCube(Math.Pow(2, -11)); return (a, b, Ops.Union); }
                case "slivers": { var (a, b, _) = Copies(2); return (a, b, Ops.Difference); }
                default: return (FromBits(Fuzz0A), FromBits(Fuzz0B), Ops.Difference);
            }
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op)
        {
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean;
        }

        /// <summary> determinism: serial equals parallel, and repeated runs give the same result and the same report, bit for bit </summary>
        [Theory]
        [MemberData(nameof(DeterminismCases))]
        public void SerialEqualsParallel_AndRunsRepeat(string name)
        {
            var (a, b, op) = Case(name);
            string serial;
            bool was = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                serial = Print(Run(a, b, op));
            } finally {
                gParallel.SerialOnThisThread = was;
            }
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Print(Run(a, b, op)), $"run {run} differs from the serial run");
        }

        /// <summary> reuse: Compute twice on one boolean gives the same result and the same report (the list does not grow) </summary>
        [Theory]
        [MemberData(nameof(DeterminismCases))]
        public void ComputeTwice_OnOneInstance_GivesTheSame(string name)
        {
            var (a, b, operation) = Case(name);
            var op = new ExactMeshBoolean(a, b, operation);
            Assert.True(op.Compute());
            string first = Print(op);
            Assert.True(op.Compute());
            Assert.Equal(first, Print(op));
        }

        /// <summary> reuse of the repair: a second Apply on the repaired mesh removes nothing more, and its report starts empty </summary>
        [Fact]
        public void ApplyTwice_TheReportStartsEmpty()
        {
            // (the exact result of the box and the cube of 2^-11, rounded: the repair collapses the cube and removes it)
            var (a, b) = BoxAndCube(Math.Pow(2, -11));
            var plain = new ExactMeshBoolean(a, b, Ops.Union) { SafeRounding = false };
            Assert.True(plain.Compute());
            DMesh3 m = plain.Result;
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(1, r.RemovedComponents);
            Assert.Equal(1, r.RemovedResolvable);
            Assert.False(r.IsClean);
            r.Apply();
            Assert.Equal(0, r.RemovedComponents);
            Assert.Empty(r.RemovedComponentInfos);
            Assert.Equal(0, r.RemovedResolvable);
            Assert.True(r.IsClean);
            AssertSolid(m);
        }

        /// <summary>
        /// SolidBooleanPipeline (StrictRounding): the sheet of the issue comes back (before: an empty result, Success); the union
        /// that loses the cube of 2^-11 is refused with RoundingNotClean (before: Success, 12 triangles, nothing said), and the
        /// report lists what the rounding removed
        /// </summary>
        [Fact]
        public void Pipeline_KeepsTheSheet_AndRefusesAndReportsALostSolid()
        {
            var (a, b) = Sheet(100, 1e-3, "issue");
            var kept = new SolidBooleanPipeline(new List<DMesh3>() { a, b }, Ops.Difference);
            Assert.True(kept.Compute(), kept.Error);
            AssertSolid(kept.Result);
            Assert.InRange(Invariance.Volume(kept.Result), 9.9, 10.1);
            Assert.Empty(kept.Report.RoundingRemoved);

            (a, b) = BoxAndCube(Math.Pow(2, -11));
            var lost = new SolidBooleanPipeline(new List<DMesh3>() { a, b }, Ops.Union);
            Assert.False(lost.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, lost.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, lost.BooleanStatus);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(lost.Report.RoundingRemoved);
            Assert.Equal(Reasons.VolumeChanged, info.Reason);
        }
        // the scene of SlabScene far from the origin (x + 2^24, where a unit in the last place is 2^-28): a sphere of radius 1 and
        // a slab of 0.5 x 0.5 x h with a needle (one contraction repairs it); pierce: a cube through the slab (an intersection
        // no edit separates). h is a multiple of the ulp, so the slab is exact
        const double FarUlp = 3.7252902984619140625e-9;
        static DMesh3 FarSlab(double ulps, bool pierce)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.25, 0.25, 0.5)));
            if (pierce)
                new MeshEditor(m).AppendMesh(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3.01, 0.02, 0.03), 0.1 * Vector3d.One) }.Generate().MakeDMesh());
            m = new DMesh3(m, false, MeshComponents.None);
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(v);
                if (p.x > 2.5 && Math.Abs(p.z) == 0.5)
                    p.z = p.z > 0 ? ulps * FarUlp : 0;
                p.x += 16777216;
                m.SetVertex(v, p);
            }
            return m;
        }

        /// <summary>
        /// the verdict's 256 ulps in the repair itself: a slab that goes as still defective, in a frame where the fraction of the
        /// bound is far below (3e-8 against an ulp of 4e-9 x 256), leaves the repair clean at 250 ulps and makes it not clean at 262
        /// </summary>
        [Theory]
        [InlineData(250, false)]
        [InlineData(262, true)]
        public void Verdict_Is256Ulps_InTheRepair(double ulps, bool resolvable)
        {
            DMesh3 m = FarSlab(ulps, true);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.True(MeshRoundingRepair.ReportBoundFraction * r.MaxLength < 16 * FarUlp);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.StillDefective, info.Reason);
            Assert.InRange(info.ThicknessUlps, ulps - 0.01, ulps);
            Assert.Equal(resolvable, info.Resolvable);
            Assert.Equal(resolvable ? 1 : 0, r.RemovedResolvable);
            Assert.Equal(resolvable == false, r.IsClean);
            Assert.Equal(312, m.TriangleCount);
            AssertSolid(m);
        }

        /// <summary>
        /// the resolution of 4 ulps in the repair itself: an edited slab that the repair leaves without a defect and with its
        /// volume goes at 3 ulps of its largest coordinate (below the resolution: listed, clean) and stays at 5 and 6
        /// </summary>
        [Theory]
        [InlineData(3, false)]
        [InlineData(5, true)]
        [InlineData(6, true)]
        public void Resolution_IsFourUlps_InTheRepair(double ulps, bool stays)
        {
            DMesh3 m = FarSlab(ulps, false);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.True(r.Collapses >= 1);
            Assert.True(r.IsClean);
            Assert.Equal(0, r.RemovedResolvable);
            Assert.Equal(stays ? 0 : 1, r.RemovedComponents);
            Assert.Equal(stays ? 312 : 300, m.TriangleCount);
            AssertSolid(m);
            if (stays) {
                // (the slab as it was: 0.5 x 0.5 x ulps)
                DMesh3 slab = MeshConnectedComponents.Separate(m).Single(c => c.TriangleCount == 12);
                Assert.Equal(0.25 * ulps * FarUlp, Invariance.Volume(slab), 12);
            }
            else {
                MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
                Assert.Equal(Reasons.BelowResolution, info.Reason);
                Assert.False(info.Resolvable);
                Assert.InRange(info.ThicknessUlps, ulps - 0.01, ulps);
            }
        }

        /// <summary>
        /// a component that was not measured before the edits goes as before #1216, is listed as NotMeasured with what it is after
        /// the edits, and makes the repair not clean. (No edit is known to bring a component from outside twice the thresholds to
        /// inside them, so the test switches the measurement off: MeasureWindow 0. The same slab, measured, stays.)
        /// </summary>
        [Fact]
        public void NotMeasuredComponent_IsRemoved_AndNotClean()
        {
            Func<DMesh3> scene = () => {
                DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
                new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 1e-5)));
                return m;
            };
            DMesh3 kept = scene();
            var r = new MeshRoundingRepair(kept);
            r.Apply();
            Assert.Equal(0, r.RemovedComponents);
            Assert.True(r.IsClean);
            Assert.Equal(312, kept.TriangleCount);

            DMesh3 m2 = scene();
            r = new MeshRoundingRepair(m2) { MeasureWindow = 0 };
            r.Apply();
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(r.RemovedComponentInfos);
            Assert.Equal(Reasons.NotMeasured, info.Reason);
            Assert.True(info.Resolvable);
            Assert.Equal(12, info.Triangles);
            Assert.InRange(info.Volume, 1.99e-5, 2.01e-5);
            Assert.InRange(info.Thickness, 1.99e-5, 2.01e-5);
            Assert.Equal(1, r.RemovedResolvable);
            Assert.False(r.IsClean);
            Assert.Equal(300, m2.TriangleCount);
            AssertSolid(m2);
        }

        /// <summary>
        /// SolidBooleanPipeline accepts a result whose only fault is a thin solid the bound kept (ScaleDisparity), but not one that
        /// also lost a solid its coordinates resolve: the box of 1024 with a rod of 0.03 x 0.03 x 1 (thin by the bound, real
        /// volume for its area, edited: kept and counted) succeeds; with the cube of 2^-11 as well (removed, resolvable) it is refused
        /// </summary>
        [Fact]
        public void Pipeline_WithAKeptThinSolid_StillRefusesALostSolid()
        {
            var (a, b) = BoxAndCube(Math.Pow(2, -11));
            DMesh3 rod = CubeWithNeedle(new Vector3d(-50, 0, 0), new Vector3d(0.015, 0.015, 0.5));
            var thin = new SolidBooleanPipeline(new List<DMesh3>() { a, rod }, Ops.Union);
            Assert.True(thin.Compute(), thin.Error);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, thin.BooleanStatus);
            Assert.Empty(thin.Report.RoundingRemoved);
            Assert.Equal(2, MeshConnectedComponents.Separate(thin.Result).Length);
            AssertSolid(thin.Result);

            var lost = new SolidBooleanPipeline(new List<DMesh3>() { a, b, rod }, Ops.Union);
            Assert.False(lost.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, lost.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, lost.BooleanStatus);
            Assert.Contains("1 components kept that the bound took as thin", lost.Error);
            MeshRoundingRepair.RemovedComponentInfo info = Assert.Single(lost.Report.RoundingRemoved);
            Assert.Equal(Reasons.VolumeChanged, info.Reason);
            Assert.True(info.Resolvable);
        }
        /// <summary>
        /// the measurement before the edits runs only where an edit can follow: not on a mesh with nothing to repair (the pass over
        /// its components is saved), and also where the only defects are contacts the rounding made
        /// </summary>
        [Fact]
        public void Measurement_RunsOnlyWhereAnEditCanFollow()
        {
            DMesh3 clean = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            new MeshEditor(clean).AppendMesh(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 1e-5)) }.Generate().MakeDMesh());
            var r = new MeshRoundingRepair(clean);
            r.Apply();
            Assert.Equal(0, r.InitialIntersections + r.InitialDegenerate + r.InitialFalseContacts);
            Assert.Equal(-1, r.MeasuredBefore);

            // (and the removal does not run either: an inside-out residue beside a mesh with nothing to repair stays, as before)
            DMesh3 residue = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            new MeshEditor(residue).AppendMesh(SafeRoundingInsideOutResidueTests.Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 0), true));
            int before = residue.TriangleCount;
            r = new MeshRoundingRepair(residue);
            r.Apply();
            Assert.Equal(0, r.InitialIntersections + r.InitialDegenerate + r.InitialFalseContacts);
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal(before, residue.TriangleCount);

            // (one thin slab with a needle: measured, one component within the window)
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            new MeshEditor(m).AppendMesh(CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 1e-5)));
            r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(1, r.MeasuredBefore);
            r.Apply();
            Assert.Equal(-1, r.MeasuredBefore);

            // (contacts made by the rounding as the only defect: measured)
            var b = new ExactMeshBoolean(ExactBooleanRoundingContactsTests.A(), ExactBooleanRoundingContactsTests.SkewB(), Ops.Difference);
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Rounding.InitialFalseContacts > 0);
            Assert.Equal(0, b.Rounding.InitialIntersections + b.Rounding.InitialDegenerate);
            Assert.True(b.Rounding.MeasuredBefore >= 0);
        }
        /// <summary>
        /// two thin slabs that share one vertex are two components, each measured before the edits and each judged against its
        /// own measurement (the shared vertex names neither): both are edited, left as they were, and stay
        /// </summary>
        [Fact]
        public void TwoComponentsSharingAVertex_AreEachJudgedByTheirOwnMeasurement()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            m = new DMesh3(m, false, MeshComponents.None);
            int n = m.TriangleCount, sphereVertices = m.VertexCount;
            // (the second slab starts at the corner (3.5, 0.5, 1e-5) of the first)
            DMesh3 first = CubeWithNeedle(new Vector3d(3, 0, 0), new Vector3d(0.5, 0.5, 1e-5));
            DMesh3 second = CubeWithNeedle(new Vector3d(4, 1, 3e-5), new Vector3d(0.5, 0.5, 2e-5));
            var corner = new Vector3d(3.5, 0.5, 1e-5);
            // (the corners as the generator computes them, made the same point exactly)
            foreach (DMesh3 part in new[] { first, second })
                foreach (int v in part.VertexIndices().ToArray())
                    if (part.GetVertex(v).Distance(corner) < 1e-15)
                        part.SetVertex(v, corner);
            int shared = -1;
            foreach (DMesh3 part in new[] { first, second }) {
                var map = new Dictionary<int, int>();
                foreach (int v in part.VertexIndices()) {
                    bool isCorner = part.GetVertex(v) == corner;
                    if (isCorner && shared >= 0) { map[v] = shared; continue; }
                    map[v] = m.AppendVertex(part.GetVertex(v));
                    if (isCorner) shared = map[v];
                }
                foreach (Index3i t in part.Triangles())
                    Assert.True(m.AppendTriangle(map[t.a], map[t.b], map[t.c]) >= 0);
            }
            Assert.True(shared >= 0);
            Assert.Equal(sphereVertices + first.VertexCount + second.VertexCount - 1, m.VertexCount);
            var components = new MeshConnectedComponents(m);
            components.FindConnectedT();
            Assert.Equal(3, components.Count);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            // (one needle is contracted, the other, at the shared corner, is repaired by moves)
            Assert.True(r.Collapses >= 1 && r.Collapses + r.Moves >= 2);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.Equal(2, r.MeasuredBefore);
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal(n + 26, m.TriangleCount);
            Assert.True(r.IsClean);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
        }

        /// <summary>
        /// an inside-out residue enclosed by a tiny solid that the removal takes goes with it: its nesting is judged on the mesh
        /// without the components that go (as a cavity of the solid it would stay, and be left alone inside out). The solid is
        /// made to go by switching the measurement off; measured, it stays with the residue as its cavity
        /// (SafeRoundingInsideOutResidueTests.ResidueInsideATinyEditedSolid_StaysAsItsCavity)
        /// </summary>
        [Fact]
        public void ResidueInsideARemovedSolid_GoesWithIt()
        {
            DMesh3 outer = SafeRoundingInsideOutResidueTests.Bipyramid(17, 5e-5, 5e-5, new Vector3d(3, 0, 0), false);
            int eid = outer.EdgeIndices().First();
            Index2i ev = outer.GetEdgeV(eid);
            Vector3d p = outer.GetVertex(ev.a), q = outer.GetVertex(ev.b);
            Assert.Equal(MeshResult.Ok, outer.SplitEdge(eid, out DMesh3.EdgeSplitInfo split));
            outer.SetVertex(split.vNew, p + 3e-10 * (q - p).Normalized);
            new MeshEditor(outer).AppendMesh(SafeRoundingInsideOutResidueTests.Bipyramid(17, 3e-5, 1e-9, new Vector3d(3, 0, 0), true));
            DMesh3 m = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(outer);
            int parts = MeshConnectedComponents.Separate(m).Length;
            var r = new MeshRoundingRepair(m) { MeasureWindow = 0 };
            r.Apply();
            Assert.Equal(2, r.RemovedComponents);
            Assert.Equal(new[] { Reasons.NotMeasured, Reasons.InsideOutResidue }, r.RemovedComponentInfos.Select(i => i.Reason).ToArray());
            Assert.Equal(new[] { true, false }, r.RemovedComponentInfos.Select(i => i.Resolvable).ToArray());
            Assert.Equal(parts - 2, MeshConnectedComponents.Separate(m).Length);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            AssertSolid(m);
        }
    }
}
