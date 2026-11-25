using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1548: an unwelded soup whose triangles are reversed at random positions has a winding number that cancels at the flipped faces. The
    /// repair's volume reference and its fallbacks must start from an oriented, welded copy, not from the raw soup. Procedural input: the
    /// box PolyCube(4, 3, 2) of 104 triangles and volume 24, every triangle with its own three vertices. Public members only (the tests run
    /// against the base branch's library too).
    /// </summary>
    public class RobustRepairOrientedReferenceTests
    {
        readonly ITestOutputHelper output;
        public RobustRepairOrientedReferenceTests(ITestOutputHelper output) { this.output = output; }

        const double BoxVolume = 24;

        static DMesh3 Box() { return AlphaWrapFeaturePassTests.PolyCube(4, 3, 2, (x, y, z) => true); }

        // each triangle with its own vertices; flip(i): the i-th triangle (in id order) is reversed
        static DMesh3 Soup(DMesh3 m, Func<int, bool> flip)
        {
            var soup = new DMesh3();
            int i = 0;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                int a = soup.AppendVertex(m.GetVertex(t.a)), b = soup.AppendVertex(m.GetVertex(t.b)), c = soup.AppendVertex(m.GetVertex(t.c));
                if (flip(i++)) soup.AppendTriangle(a, c, b);
                else soup.AppendTriangle(a, b, c);
            }
            return soup;
        }

        static Func<int, bool> EveryKth(int k) { return i => i % k == k - 1; }
        static Func<int, bool> RandomHalf(int seed) { var r = new Random(seed); var bits = Enumerable.Range(0, 1000).Select(_ => r.Next(2) == 1).ToArray(); return i => bits[i]; }

        public static IEnumerable<object[]> Variants()
        {
            yield return new object[] { "every 5th" };
            yield return new object[] { "every 2nd" };
            yield return new object[] { "every 3rd" };
            yield return new object[] { "random half" };
        }

        static Func<int, bool> Flip(string name)
        {
            switch (name) {
                case "every 5th": return EveryKth(5);
                case "every 2nd": return EveryKth(2);
                case "every 3rd": return EveryKth(3);
                default: return RandomHalf(7);
            }
        }

        static MeshRobustRepair Run(DMesh3 input, Action<MeshRobustRepair> configure = null)
        {
            var r = new MeshRobustRepair(new DMesh3(input)) { MeasureResultVolumeIoU = true };
            configure?.Invoke(r);
            r.Apply();
            return r;
        }

        static DMesh3 Sphere(double r, Vector3d c, int n = 12)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        // a closed, finite, manifold mesh, as NotCleanReason sees it
        static void AssertClean(MeshRobustRepair r, string what)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.True(r.Mesh.IsClosed(), what + ": not closed");
            Assert.True(r.Mesh.TriangleCount > 0, what);
            foreach (int v in r.Mesh.VertexIndices()) {
                Vector3d p = r.Mesh.GetVertex(v);
                Assert.True(!double.IsNaN(p.x + p.y + p.z) && !double.IsInfinity(p.x + p.y + p.z), what + ": not finite");
            }
        }

        // the mesh bit for bit, with the outputs that say how it came about
        static string Fingerprint(MeshRobustRepair r, bool ok)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ok).Append('|').Append(r.ResultReason).Append('|').Append(r.FallbackReason).Append('|').Append(r.ExactResolveReason).Append('|')
              .Append(r.SolidifyReason).Append('|').Append(r.WrapReason).Append('|').Append(r.UsedExactResolve).Append(r.UsedSolidify).Append(r.UsedAlphaWrap)
              .Append(r.UsedInput).Append('|').Append(r.PassedThroughTriangles).Append('|').Append(r.AlphaWrapTriangles).Append('|')
              .Append(BitConverter.DoubleToInt64Bits(r.RepairVolumeIoU)).Append('|').Append(r.ResultPath).Append('|')
              .Append(BitConverter.DoubleToInt64Bits(r.ResultVolumeIoU)).Append('|');
            foreach (int v in r.Mesh.VertexIndices()) {
                Vector3d p = r.Mesh.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in r.Mesh.TriangleIndices()) {
                Index3i tri = r.Mesh.GetTriangle(t);
                sb.Append(tri.a).Append(',').Append(tri.b).Append(',').Append(tri.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        // The volume of an accepted result is within 1 % of the reference (MinVolumeIoU 0.99: an IoU of at least 0.99 of two solids, one of which
        // contains the other here, is a volume ratio of at least 0.99); the exact resolve of the oriented soup gives the box itself
        const double VolumeTolerance = 0.01 * BoxVolume;

        [Theory]
        [MemberData(nameof(Variants))]
        public void FlippedSoup_RepairsToTheBox(string name)
        {
            DMesh3 soup = Soup(Box(), Flip(name));
            MeshRobustRepair r = Run(soup);
            Assert.True(r.ResultReason == "", name + ": " + r.ResultReason);
            // the path: the repair result (self-intersecting after the weld) is sent to the oriented input, which the exact resolve takes
            Assert.Equal(MeshRobustRepair.ResultPaths.ExactResolve, r.ResultPath);
            Assert.True(r.UsedInput, name);
            AssertClean(r, name);
            Assert.InRange(Invariance.Volume(r.Mesh), BoxVolume - VolumeTolerance, BoxVolume + VolumeTolerance);
            Assert.True(r.ResultVolumeIoU >= 0.99, name + ": ResultVolumeIoU " + r.ResultVolumeIoU);
            Assert.True(r.RepairVolumeIoU >= 0.99, name + ": RepairVolumeIoU " + r.RepairVolumeIoU);
        }

        // the solidify, with neither the exact resolve nor the wrap: it starts from the oriented input as well
        [Theory]
        [MemberData(nameof(Variants))]
        public void FlippedSoup_SolidifyStartsFromTheOrientedInput(string name)
        {
            DMesh3 soup = Soup(Box(), Flip(name));
            MeshRobustRepair r = Run(soup, x => { x.ExactResolve = false; x.AlphaWrapFallback = false; x.SolidifyResolution = 48; });
            Assert.True(r.ResultReason == "", name + ": " + r.ResultReason);
            Assert.Equal(MeshRobustRepair.ResultPaths.Solidify, r.ResultPath);
            Assert.True(r.UsedInput, name);
            AssertClean(r, name);
            // (the solidify's resolution rounds the box's corners and edges)
            Assert.InRange(Invariance.Volume(r.Mesh), 0.95 * BoxVolume, 1.05 * BoxVolume);
            Assert.True(r.ResultVolumeIoU >= 0.9, name + ": ResultVolumeIoU " + r.ResultVolumeIoU);
        }

        // a clean repair result (RepairF) that is right: accepted against the oriented soup's solid (against the soup's cancelling winding
        // volume of 20 it would differ)
        [Fact]
        public void FlippedSoup_CleanRepairResult_IsAccepted()
        {
            DMesh3 soup = Soup(Box(), EveryKth(5));
            MeshRobustRepair r = Run(soup, x => x.RepairF = m => Box());
            Assert.True(r.ResultReason == "", r.ResultReason);
            Assert.Equal(MeshRobustRepair.ResultPaths.Repair, r.ResultPath);
            Assert.Equal("", r.FallbackReason);
            Assert.False(r.UsedInput);
            Assert.True(r.RepairVolumeIoU >= 0.99, "RepairVolumeIoU " + r.RepairVolumeIoU);
            Assert.InRange(Invariance.Volume(r.Mesh), BoxVolume - VolumeTolerance, BoxVolume + VolumeTolerance);
        }

        // a clean repair result that lost most of the volume, and an empty one: the fallbacks start from the oriented soup, not from the raw one
        [Theory]
        [InlineData("small box", 2)]
        [InlineData("empty", 0)]
        public void FlippedSoup_WrongRepairResult_FallsBackToTheOrientedSoup(string what, int n)
        {
            DMesh3 soup = Soup(Box(), EveryKth(5));
            MeshRobustRepair r = Run(soup, x => x.RepairF = m => n == 0 ? new DMesh3() : AlphaWrapFeaturePassTests.PolyCube(n, n, n, (a, b, c) => true));
            Assert.True(r.ResultReason == "", what + ": " + r.ResultReason);
            Assert.True(r.UsedInput, what);
            AssertClean(r, what);
            Assert.InRange(Invariance.Volume(r.Mesh), BoxVolume - VolumeTolerance, BoxVolume + VolumeTolerance);
            Assert.True(r.ResultVolumeIoU >= 0.99, what + ": " + r.ResultVolumeIoU);
        }

        // jitter on the soup (corners of one point differ by up to 1e-4, beyond the weld tolerance 1e-6 of the diagonal, and the flips as before): the repair
        // may not empty it, and the result keeps the volume (a return to the orientation of MeshAutoRepair, which fixes none of the flips, fails this)
        [Theory]
        [InlineData("every 3rd")]
        [InlineData("every 5th")]
        [InlineData("random half")]
        public void JitteredFlippedSoup_KeepsTheVolume(string name)
        {
            DMesh3 soup = Soup(Box(), Flip(name));
            var rnd = new Random(11);
            foreach (int v in soup.VertexIndices().ToArray())
                soup.SetVertex(v, soup.GetVertex(v) + 1e-4 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5));
            MeshRobustRepair r = Run(soup);
            output.WriteLine($"{name}: ok={r.ResultReason == ""} path={r.ResultPath} vol={Invariance.Volume(r.Mesh)} fb={r.FallbackReason} ex={r.ExactResolveReason} iou={r.ResultVolumeIoU}");
            Assert.True(r.ResultReason == "", name + ": " + r.ResultReason);
            AssertClean(r, name);
            Assert.InRange(Invariance.Volume(r.Mesh), 0.95 * BoxVolume, 1.05 * BoxVolume);
            Assert.True(r.ResultVolumeIoU >= 0.9, name + ": " + r.ResultVolumeIoU);
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void FlippedSoup_Reuse_SameResultOnTheSameInstance(string name)
        {
            DMesh3 soup = Soup(Box(), Flip(name));
            var r = new MeshRobustRepair(new DMesh3(soup)) { MeasureResultVolumeIoU = true };
            bool ok1 = r.Apply();
            string first = Fingerprint(r, ok1);
            r.Mesh = new DMesh3(soup);
            bool ok2 = r.Apply();
            Assert.Equal(first, Fingerprint(r, ok2));
            // (and a fresh instance: the same)
            var fresh = Run(soup);
            Assert.Equal(first, Fingerprint(fresh, fresh.ResultReason == ""));
        }

        // repeated runs: the same bits (the new stage is serial, the stages after it are the ones that were)
        [Fact]
        public void FlippedSoup_Deterministic()
        {
            DMesh3 soup = Soup(Box(), RandomHalf(7));
            string first = null;
            for (int i = 0; i < 3; ++i) {
                var r = Run(soup);
                string f = Fingerprint(r, r.ResultReason == "");
                if (first == null) first = f;
                Assert.Equal(first, f);
            }
        }

        // all similarities: the box comes back (volume scaled); the 2^k scales bit for bit
        [Fact]
        public void FlippedSoup_Invariance()
        {
            DMesh3 soup = Soup(Box(), EveryKth(5));
            MeshRobustRepair id = Run(soup);
            foreach (Similarity s in Similarity.All()) {
                MeshRobustRepair r = Run(Invariance.Transformed(soup, s));
                Assert.True(r.ResultReason == "", s.Name + ": " + r.ResultReason);
                AssertClean(r, s.Name);
                double v = Invariance.Volume(r.Mesh) / (s.Scale * s.Scale * s.Scale);
                Assert.InRange(v, BoxVolume - VolumeTolerance, BoxVolume + VolumeTolerance);
                Assert.True(r.ResultVolumeIoU >= 0.99, s.Name + ": " + r.ResultVolumeIoU);
                if (s.IsExact) {
                    Assert.Equal(id.Mesh.VertexCount, r.Mesh.VertexCount);
                    Assert.Equal(id.Mesh.TriangleCount, r.Mesh.TriangleCount);
                    var a = id.Mesh.VertexIndices().ToArray();
                    var b = r.Mesh.VertexIndices().ToArray();
                    for (int i = 0; i < a.Length; ++i) {
                        Vector3d p = id.Mesh.GetVertex(a[i]), q = r.Mesh.GetVertex(b[i]) / s.Scale;
                        Assert.True(p.x == q.x && p.y == q.y && p.z == q.z, s.Name + ": vertex " + i);
                    }
                    Assert.Equal(id.ResultPath, r.ResultPath);
                    Assert.Equal(id.FallbackReason, r.FallbackReason);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(id.RepairVolumeIoU), BitConverter.DoubleToInt64Bits(r.RepairVolumeIoU));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(id.ResultVolumeIoU), BitConverter.DoubleToInt64Bits(r.ResultVolumeIoU));
                }
            }
        }

        // Inputs that are welded and consistently oriented: the reference is the input itself, the results are those of master (SHA-256 of the
        // fingerprint, computed on origin/master dfe4d8d58 from the same procedural inputs)
        static DMesh3 TwoBoxes()
        {
            DMesh3 m = AlphaWrapFeaturePassTests.PolyCube(3, 3, 2, (x, y, z) => true);
            DMesh3 n = AlphaWrapFeaturePassTests.PolyCube(3, 3, 2, (x, y, z) => true);
            MeshTransforms.Translate(n, new Vector3d(1.5, 1.5, 0.5));
            MeshEditor.Append(m, n);
            return m;
        }

        static DMesh3 OverlappingSpheres()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero, 10);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0), 10));
            return m;
        }

        static DMesh3 OpenBox()
        {
            DMesh3 m = Box();
            m.RemoveTriangle(m.TriangleIndices().First());
            return m;
        }

        static readonly Dictionary<string, string> MasterHashes = new Dictionary<string, string> {
            { "box", "21E5FF898E321D9C8D1A8590995FEC42AAA38A7AE733DB3A75D6DBAC52E1ABA7" },
            { "two overlapping boxes", "5042964F629766C3B5EA86305963BC2FCD3C9612252C3E38E23B493C1738726D" },
            { "overlapping spheres, solidify", "98863A74EDE975EBE3168E768217582FB117FAD59110A60FAE4321CBD5754296" },
            { "box with a missing triangle", "D8080BA780D421192566C1365FB1B10F0704024E516E3BED08E522CEFE4E1186" },
        };

        public static IEnumerable<object[]> CleanCases() { return MasterHashes.Keys.Select(k => new object[] { k }); }

        static MeshRobustRepair RunClean(string name)
        {
            switch (name) {
                case "box": return Run(Box());
                case "two overlapping boxes": return Run(TwoBoxes());
                case "overlapping spheres, solidify": return Run(OverlappingSpheres(), x => { x.ExactResolve = false; x.AlphaWrapFallback = false; x.SolidifyResolution = 48; });
                default: return Run(OpenBox());
            }
        }

        [Theory]
        [MemberData(nameof(CleanCases))]
        public void WeldedConsistentInput_SameAsMaster(string name)
        {
            MeshRobustRepair r = RunClean(name);
            string f = Fingerprint(r, r.ResultReason == "");
            output.WriteLine(name + " => " + f + " path=" + r.ResultPath + " reason=" + r.ResultReason + " fb=" + r.FallbackReason);
            Assert.Equal(MasterHashes[name], f);
        }
    }
}
