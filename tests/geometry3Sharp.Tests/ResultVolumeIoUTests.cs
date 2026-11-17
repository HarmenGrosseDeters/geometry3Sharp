using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.ResultVolumeIoU / ResultPath (#1104 class E option (a), #1096): the volume IoU of the final result against the
    /// input, on every path, as an opt-in diagnostic that changes no other output. The second half reproduces the three patterns of #1096
    /// (open, not winding-closed, self-intersecting inputs that end in the alpha wrap) procedurally.
    /// </summary>
    public class ResultVolumeIoUTests
    {
        static DMesh3 Sphere(double r, Vector3d c, int n = 12)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        // the triangles whose centroid direction is within cosLimit of dir are removed: an open surface
        static DMesh3 Holed(DMesh3 m, Vector3d dir, double cosLimit)
        {
            var r = new DMesh3(m);
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).Normalized.Dot(dir) > cosLimit) r.RemoveTriangle(t);
            return r;
        }

        // every triangle with its own vertices (an unwelded soup)
        static DMesh3 Soup(DMesh3 m)
        {
            var r = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                r.AppendTriangle(r.AppendVertex(m.GetVertex(tri.a)), r.AppendVertex(m.GetVertex(tri.b)), r.AppendVertex(m.GetVertex(tri.c)));
            }
            return r;
        }

        // a flat ellipsoid of the given thickness (radius 1 in x and y) with a large hole: a thin open wall
        static DMesh3 Lens(double thickness, Vector3d c, int n = 6)
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, n);
            foreach (int v in m.VertexIndices().ToList()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(p.x, p.y, p.z * thickness / 2));
            }
            m = Holed(m, Vector3d.AxisX, 0.9);
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 CrossingLenses(double thickness)
        {
            DMesh3 m = Lens(thickness, Vector3d.Zero);
            MeshEditor.Append(m, Lens(thickness, new Vector3d(0.7, 0.2, thickness / 3)));
            return m;
        }

        // two clean spheres far apart: every part passes through
        static DMesh3 FarSpheres()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, 6);
            MeshEditor.Append(m, Sphere(1, new Vector3d(5, 0, 0), 6));
            return m;
        }

        // every output of the last Apply() that exists without the diagnostic, as a hash (the mesh bit for bit)
        static string Fingerprint(MeshRobustRepair r, bool ok)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ok).Append('|').Append(r.ResultReason).Append('|').Append(r.FallbackReason).Append('|').Append(r.ExactResolveReason).Append('|')
              .Append(r.SolidifyReason).Append('|').Append(r.WrapReason).Append('|').Append(r.UsedExactResolve).Append(r.UsedSolidify).Append(r.UsedAlphaWrap)
              .Append(r.UsedInput).Append('|').Append(r.PassedThroughTriangles).Append('|').Append(r.AlphaWrapTriangles).Append('|')
              .Append(BitConverter.DoubleToInt64Bits(r.RepairVolumeIoU)).Append('|').Append(r.VolumeChecksRun).Append('|')
              .Append(MeshRobustRepair.NotCleanReason(r.Mesh)).Append('|').Append(r.ResultPath).Append('|');
            foreach (int v in r.Mesh.VertexIndices()) {
                Vector3d p = r.Mesh.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in r.Mesh.TriangleIndices()) {
                Index3i tri = r.Mesh.GetTriangle(t);
                sb.Append(tri.a).Append(',').Append(tri.b).Append(',').Append(tri.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
        }

        static MeshRobustRepair Run(DMesh3 mesh, bool measure, Action<MeshRobustRepair> configure = null)
        {
            var r = new MeshRobustRepair(mesh) { MeasureResultVolumeIoU = measure };
            configure?.Invoke(r);
            Assert.True(r.Apply(), r.ResultReason);
            return r;
        }

        static readonly Action<MeshRobustRepair> NoExact = r => r.ExactResolve = false;
        // a wrap of diagonal / 100 and an offset of diagonal / 1000 (library defaults 300 and 3000): a tenth of the triangles, and a coarse solidify grid that is rejected on the way, and no simplification (the soup test below simplifies), for the test time
        static readonly Action<MeshRobustRepair> CoarseWrap = r => { r.WrapRelativeAlpha = 100; r.WrapRelativeOffset = 1000; r.SolidifyResolution = 32; r.SimplifyAlphaWrap = false; };
        static readonly Action<MeshRobustRepair> SolidifyOnly = r => { r.ExactResolve = false; r.AlphaWrapFallback = false; };

        // (name, input, configuration, path, IoU range)
        public static IEnumerable<object[]> PathCases()
        {
            yield return new object[] { "pass-through", 0, null, MeshRobustRepair.ResultPaths.PassThrough, 0.9999, 1.0001 };
            yield return new object[] { "repair", 1, null, MeshRobustRepair.ResultPaths.Repair, 0.9999, 1.0001 };
            yield return new object[] { "exact resolve", 2, null, MeshRobustRepair.ResultPaths.ExactResolve, 0.99, 1.0001 };
            yield return new object[] { "solidify", 2, SolidifyOnly, MeshRobustRepair.ResultPaths.Solidify, 0.99, 1.0001 };
            yield return new object[] { "alpha wrap", 3, CoarseWrap, MeshRobustRepair.ResultPaths.AlphaWrap, 0.3, 0.8 };
        }

        static DMesh3 Overlapping()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, 8);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0), 8));
            return m;
        }

        static DMesh3 Input(int kind)
        {
            switch (kind) {
                case 0: return FarSpheres();
                case 1: return Sphere(1, Vector3d.Zero);
                case 2: return Overlapping();
                default: return CrossingLenses(0.004);
            }
        }

        [Theory]
        [MemberData(nameof(PathCases))]
        public void EveryPath_ReportsItsPathAndTheIoUOfTheFinalResult(string name, int kind, Action<MeshRobustRepair> configure, MeshRobustRepair.ResultPaths path, double lo, double hi)
        {
            var r = Run(Input(kind), true, configure);
            Assert.Equal(path, r.ResultPath);
            Assert.InRange(r.ResultVolumeIoU, lo, hi);
            // (the fidelity metric samples differently, and an open mesh has more variance: within 0.1)
            var f = new MeshFidelityMetrics(Input(kind), r.Mesh) { Curvature = false, SurfaceSamples = 5000, EdgeSamples = 1000 };
            Assert.True(f.Compute());
            Assert.InRange(r.ResultVolumeIoU, f.VolumeIoU - 0.1, f.VolumeIoU + 0.1);
        }

        [Fact]
        public void Off_NothingIsMeasured_AndThePathIsStillReported()
        {
            var r = Run(Overlapping(), false);
            Assert.True(double.IsNaN(r.ResultVolumeIoU));
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
        }

        [Fact]
        public void RepairPath_EqualsRepairVolumeIoU()
        {
            // the repair result is the final one: the diagnostic is the volume check's own value (same reference, sampling and estimate)
            foreach (Similarity s in Similarity.All()) {
                var r = Run(Invariance.Transformed(Sphere(1, Vector3d.Zero), s), true);
                Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
                Assert.Equal(r.RepairVolumeIoU, r.ResultVolumeIoU);
            }
            var e = Run(Overlapping(), true);
            Assert.Equal(e.RepairVolumeIoU, e.ResultVolumeIoU);
        }

        [Fact]
        public void NoSolidInTheInput_IsNaN_AndNothingElseChanges()
        {
            // one open triangle pair: no inside (the IoU is not defined); the wrap result is returned as without the diagnostic
            var sheet = new DMesh3();
            int a = sheet.AppendVertex(new Vector3d(0, 0, 0)), b = sheet.AppendVertex(new Vector3d(10, 0, 0)), c = sheet.AppendVertex(new Vector3d(10, 10, 0.001)), d = sheet.AppendVertex(new Vector3d(0, 10, 0.001));
            sheet.AppendTriangle(a, b, c);
            sheet.AppendTriangle(a, c, d);
            var r = Run(new DMesh3(sheet), true);
            Assert.True(double.IsNaN(r.ResultVolumeIoU));
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, r.ResultPath);
            var off = Run(new DMesh3(sheet), false);
            Assert.Equal(Fingerprint(off, true), Fingerprint(r, true));
        }

        [Fact]
        public void TheDiagnostic_ChangesNoOtherOutput()
        {
            // a hash of every output of the default path (mesh bit for bit, reasons, flags, counters) is the same with and without it
            int k = 0;
            foreach (object[] c in PathCases()) {
                if ((int)c[1] == 3)
                    continue;       // (the wrap: in the thin wall test below, for the test time)
                var cfg = (Action<MeshRobustRepair>)c[2];
                var off = new MeshRobustRepair(Input((int)c[1]));
                cfg?.Invoke(off);
                bool okOff = off.Apply();
                var on = new MeshRobustRepair(Input((int)c[1])) { MeasureResultVolumeIoU = true };
                cfg?.Invoke(on);
                bool okOn = on.Apply();
                Assert.Equal(Fingerprint(off, okOff), Fingerprint(on, okOn));
                Assert.True(double.IsNaN(off.ResultVolumeIoU), (string)c[0]);
                ++k;
            }
            Assert.Equal(4, k);
        }

        [Fact]
        public void PassedThroughParts_ReportTheStageThatRepairedTheRest()
        {
            // a clean sphere passes through, the sphere with a hole next to it is repaired (here by the plain repair): the path is that stage, not PassThrough
            DMesh3 input = Holed(Sphere(1, Vector3d.Zero, 16), Vector3d.AxisX, 0.7);
            MeshEditor.Append(input, Sphere(1, new Vector3d(5, 0, 0), 6));
            var r = Run(input, true);
            Assert.True(r.PassedThroughTriangles > 0);
            Assert.False(r.UsedSolidify || r.UsedExactResolve || r.UsedAlphaWrap);
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.InRange(r.ResultVolumeIoU, 0.95, 1.0001);
        }

        [Fact]
        public void Reuse_OneInstanceTwice_AndTheValueIsResetWhenOffOrNotRepaired()
        {
            var r = new MeshRobustRepair(Overlapping()) { MeasureResultVolumeIoU = true };
            Assert.True(r.Apply());
            double first = r.ResultVolumeIoU;
            Assert.InRange(first, 0.99, 1.0001);
            r.Mesh = Overlapping();
            Assert.True(r.Apply());
            Assert.Equal(first, r.ResultVolumeIoU);
            // another input on the same instance (a sphere with a hole, repaired by closing it): its own path and value
            r.Mesh = Holed(Sphere(1, Vector3d.Zero, 16), Vector3d.AxisX, 0.7);
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.InRange(r.ResultVolumeIoU, 0.95, 0.9999);
            // switched off: NaN again, not the value of the run before
            r.MeasureResultVolumeIoU = false;
            r.Mesh = Sphere(1, Vector3d.Zero);
            Assert.True(r.Apply());
            Assert.True(double.IsNaN(r.ResultVolumeIoU));
            // not repaired (an empty input): no value, no path
            r.MeasureResultVolumeIoU = true;
            r.Mesh = new DMesh3();
            Assert.False(r.Apply());
            Assert.True(double.IsNaN(r.ResultVolumeIoU));
            Assert.Equal(MeshRobustRepair.ResultPaths.None, r.ResultPath);
            // stopped before the stages (a coordinate that is not finite): the values of the run before are gone as well
            r.Mesh = Sphere(1, Vector3d.Zero);
            Assert.True(r.Apply());
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.False(double.IsNaN(r.ResultVolumeIoU));
            DMesh3 bad = Sphere(1, Vector3d.Zero);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            r.Mesh = bad;
            Assert.False(r.Apply());
            Assert.True(double.IsNaN(r.ResultVolumeIoU));
            Assert.Equal(MeshRobustRepair.ResultPaths.None, r.ResultPath);
        }

        [Fact]
        public void InvariantUnderSimilarities_TwoToTheKScalesBitForBit()
        {
            // the pass-through, the repair and the exact resolve (the paths that are cheap): the same path everywhere, the same value
            // bit for bit for the 2^k scales, within sampling noise for the others (another tessellation of the sampled solid)
            foreach (int kind in new[] { 0, 1, 2 }) {
                var reference = Run(Input(kind), true);
                foreach (Similarity s in Similarity.All()) {
                    var r = Run(Invariance.Transformed(Input(kind), s), true);
                    Assert.Equal(reference.ResultPath, r.ResultPath);
                    if (s.IsExact)
                        Assert.Equal(reference.ResultVolumeIoU, r.ResultVolumeIoU);
                    else
                        Assert.InRange(r.ResultVolumeIoU, reference.ResultVolumeIoU - 0.02, reference.ResultVolumeIoU + 0.02);
                }
            }
        }

        // ---- #1096: the wrap fallback on open, not winding-closed, self-intersecting inputs ----

        // the wrap's offset is a fraction of the diagonal, and it wraps both sides of a surface: its solid is a layer around the input
        // surface, not the inside. The reference (winding number solid) and the reference of the outer faces agree where the input has
        // no interior faces, so the low IoU is a difference of the solids, not an artefact of the reference
        static void AssertWrapIsNearTheInput_ButItsSolidDiffers(DMesh3 input, MeshRobustRepair r, out MeshFidelityMetrics f)
        {
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, r.ResultPath);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.True(r.Mesh.IsClosed());
            f = new MeshFidelityMetrics(input, r.Mesh) { Curvature = false, SurfaceSamples = 5000, EdgeSamples = 1000 };
            Assert.True(f.Compute());
            // the wrap contract: no part of the result farther from the input than alpha + offset (relative to the size), the input
            // within the same distance of the result
            double bound = (r.AlphaWrap.UsedAlpha + r.AlphaWrap.UsedOffset) / f.Size;
            Assert.True(f.MaxDistanceReferenceToMesh <= bound, $"input to result {f.MaxDistanceReferenceToMesh} against {bound}");
            Assert.True(f.MaxDistanceMeshToReference <= bound, $"result to input {f.MaxDistanceMeshToReference} against {bound}");
        }

        // Slow: two wraps of the thin walls (with the diagnostic and without, for the hash of the wrap path) and the fidelity metrics, about 5 s
        [Trait("Category", "Slow")]
        [Fact]
        public void WrapOfThinOpenWalls_InflatesThem_TheReferenceIsNotWrong()
        {
            // a wall of 0.004 against an offset of diagonal / 1000 and an alpha of diagonal / 100 on both sides: the wrap is a layer
            // thicker than the wall: IoU about 0.5, more extra than missing, the same IoU against the outer faces
            var input = CrossingLenses(0.004);
            var r = Run(new DMesh3(input), true, CoarseWrap);
            MeshFidelityMetrics f;
            AssertWrapIsNearTheInput_ButItsSolidDiffers(input, r, out f);
            Assert.InRange(r.ResultVolumeIoU, 0.3, 0.8);
            Assert.True(f.VolumeExtra > f.VolumeMissing);
            Assert.InRange(f.OuterVolumeIoU, r.ResultVolumeIoU - 0.15, r.ResultVolumeIoU + 0.15);
            // the diagnostic changed nothing of the wrap result (the same hash without it)
            var off = new MeshRobustRepair(new DMesh3(input));
            CoarseWrap(off);
            bool ok = off.Apply();
            Assert.Equal(Fingerprint(off, ok), Fingerprint(r, true));
            Assert.True(double.IsNaN(off.ResultVolumeIoU));
        }

        [Fact]
        public void WrapOfAnOpenSoupWithLargeHoles_IsALayerAroundTheSurface_NotTheInside()
        {
            // two overlapping unwelded spheres with holes of 60 degrees: the winding number solid is the inside; the wrap goes through the
            // holes (alpha far below them) and is a thin layer around the surface: most of the reference solid is missing, little extra
            // (a coarser wrap, alpha = diagonal / 30, for the test time; the pattern does not depend on it)
            DMesh3 input = Soup(Holed(Sphere(1, Vector3d.Zero, 8), Vector3d.AxisX, 0.5));
            MeshEditor.Append(input, Soup(Holed(Sphere(1, new Vector3d(1.2, 0, 0.3), 8), -Vector3d.AxisY, 0.5)));
            var r = Run(new DMesh3(input), true, c => { c.WrapRelativeAlpha = 30; c.WrapRelativeOffset = 300; });
            MeshFidelityMetrics f;
            AssertWrapIsNearTheInput_ButItsSolidDiffers(input, r, out f);
            Assert.InRange(r.ResultVolumeIoU, 0.0, 0.2);
            Assert.True(f.VolumeMissing > 0.5);
            Assert.True(f.VolumeExtra < 0.2);
            // the repair result (self-intersecting, not used) was close to the input's solid: the wrap is what differs
            Assert.True(r.RepairVolumeIoU > 0.9, "repair " + r.RepairVolumeIoU);
            Assert.InRange(f.OuterVolumeIoU, 0, 0.2);
        }
    }
}
