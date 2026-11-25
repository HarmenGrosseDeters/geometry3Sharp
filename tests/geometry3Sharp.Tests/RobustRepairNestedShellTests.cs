using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1566: a closed shell oriented outward inside another outward shell was read as a cavity (parity of nesting), the volume reference got the same
    /// flip, so the result was hollow with Apply() true and both IoUs 1. Now the flip is reported (ComponentsFlippedByNesting) and Apply() is false with
    /// ResultReason NestingAmbiguous where the plain union of the shells differs by more than 1 - MinVolumeIoU. A real cavity (inner shell inward) is
    /// unchanged. The new public members are read by reflection, so that this file compiles against the base branch's library (the tests fail there).
    /// </summary>
    public class RobustRepairNestedShellTests
    {
        readonly ITestOutputHelper output;
        public RobustRepairNestedShellTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Vector3d c, double h)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, new Vector3d(h, h, h)), NoSharedVertices = false };
            g.Generate();
            return g.MakeDMesh();
        }

        // the outer cube (half size 1, volume 8) with an inner cube of the given half size; inward: the inner cube is reversed (a real cavity)
        static DMesh3 Nested(double inner, bool inward)
        {
            DMesh3 m = Box(new Vector3d(1, 1, 1), 1.0);
            DMesh3 n = Box(new Vector3d(1, 1, 1), inner);
            if (inward) n.ReverseOrientation(false);
            MeshEditor.Append(m, n);
            return m;
        }

        static MeshRobustRepair Run(DMesh3 input)
        {
            var r = new MeshRobustRepair(new DMesh3(input)) { MeasureResultVolumeIoU = true };
            r.Apply();
            return r;
        }

        static int Flipped(MeshRobustRepair r)
        {
            PropertyInfo p = typeof(MeshRobustRepair).GetProperty("ComponentsFlippedByNesting");
            Assert.True(p != null, "MeshRobustRepair.ComponentsFlippedByNesting does not exist");
            return (int)p.GetValue(r);
        }

        static string AmbiguousText()
        {
            FieldInfo f = typeof(MeshRobustRepair).GetField("NestingAmbiguous");
            Assert.True(f != null, "MeshRobustRepair.NestingAmbiguous does not exist");
            return (string)f.GetRawConstantValue();
        }

        public static IEnumerable<object[]> Sizes() { yield return new object[] { 0.2 }; yield return new object[] { 0.5 }; yield return new object[] { 0.8 }; }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void OutwardInnerShell_IsReported_AndNotAcceptedAsACavity(double inner)
        {
            MeshRobustRepair r = Run(Nested(inner, false));
            double innerVolume = Math.Pow(2 * inner, 3), v = Invariance.Volume(r.Mesh);
            output.WriteLine(inner + ": reason='" + r.ResultReason + "' volume " + v + " flipped " + Flipped(r));
            Assert.Equal(1, Flipped(r));
            if (innerVolume / 8 > 0.01) {
                // the cavity reading differs from the union of the shells by more than 1 - MinVolumeIoU: not accepted silently
                Assert.StartsWith(AmbiguousText(), r.ResultReason);
            } else {
                // below the tolerance of the volume check (0.2: 0.1 %): reported by the count only, the result is the hollow one
                Assert.Equal("", r.ResultReason);
                Assert.Equal(8 - innerVolume, v, 6);
            }
        }

        [Theory]
        [MemberData(nameof(Sizes))]
        public void RealCavity_StaysAsItWas_NothingReported(double inner)
        {
            MeshRobustRepair r = Run(Nested(inner, true));
            Assert.Equal("", r.ResultReason);
            Assert.Equal(0, Flipped(r));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.Equal(8 - Math.Pow(2 * inner, 3), Invariance.Volume(r.Mesh), 6);
            Assert.True(r.ResultVolumeIoU >= 0.99, "" + r.ResultVolumeIoU);
        }

        [Fact]
        public void OutwardInnerShell_Reuse_SameResultTwice()
        {
            DMesh3 input = Nested(0.5, false);
            var r = new MeshRobustRepair(new DMesh3(input));
            bool a = r.Apply();
            string reason = r.ResultReason;
            int n = Flipped(r);
            r.Mesh = new DMesh3(input);
            bool b = r.Apply();
            Assert.Equal(a, b);
            Assert.Equal(reason, r.ResultReason);
            Assert.Equal(n, Flipped(r));
            Assert.Equal(1, n);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void OutwardInnerShell_Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Nested(0.5, false);
            MeshRobustRepair id = Run(input);
            MeshRobustRepair r = Run(Invariance.Transformed(input, s));
            Assert.Equal(Flipped(id), Flipped(r));
            Assert.Equal(id.ResultReason, r.ResultReason);
            if (s.IsExact) {
                Assert.Equal(id.Mesh.VertexCount, r.Mesh.VertexCount);
                Assert.Equal(id.Mesh.TriangleCount, r.Mesh.TriangleCount);
                var a = id.Mesh.VertexIndices().ToArray();
                var b = r.Mesh.VertexIndices().ToArray();
                for (int i = 0; i < a.Length; ++i) {
                    Vector3d p = id.Mesh.GetVertex(a[i]), q = r.Mesh.GetVertex(b[i]) / s.Scale;
                    Assert.True(p.x == q.x && p.y == q.y && p.z == q.z, s.Name + ": vertex " + i);
                }
                Assert.Equal(BitConverter.DoubleToInt64Bits(id.ResultVolumeIoU), BitConverter.DoubleToInt64Bits(r.ResultVolumeIoU));
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RealCavity_Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Nested(0.8, true);
            MeshRobustRepair id = Run(input);
            MeshRobustRepair r = Run(Invariance.Transformed(input, s));
            Assert.Equal(0, Flipped(r));
            Assert.Equal("", r.ResultReason);
            Assert.Equal(id.Mesh.TriangleCount, r.Mesh.TriangleCount);
            Assert.Equal(1.0, Invariance.Volume(r.Mesh) / (s.Scale * s.Scale * s.Scale) / Invariance.Volume(id.Mesh), 6);
        }

        // each triangle with its own vertices, every 3rd reversed (#1548) on the nested cubes (#1566): the orientation of each shell is lost, its
        // per component consistent orientation is the larger patch of its triangles
        static DMesh3 FlippedSoup(DMesh3 m)
        {
            var soup = new DMesh3();
            int i = 0;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                int a = soup.AppendVertex(m.GetVertex(t.a)), b = soup.AppendVertex(m.GetVertex(t.b)), c = soup.AppendVertex(m.GetVertex(t.c));
                if (i++ % 3 == 2) soup.AppendTriangle(a, c, b);
                else soup.AppendTriangle(a, b, c);
            }
            return soup;
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.8)]
        public void FlippedSoupOfNestedCubes_OutwardInner_IsAmbiguous(double inner)
        {
            // the weld and the orientation of #1548 recover the outward shells, and #1566 sees their nesting
            MeshRobustRepair r = Run(FlippedSoup(Nested(inner, false)));
            Assert.Equal(1, Flipped(r));
            Assert.StartsWith(AmbiguousText(), r.ResultReason);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.8)]
        public void FlippedSoupOfNestedCubes_InwardInner_IsTheCavity(double inner)
        {
            MeshRobustRepair r = Run(FlippedSoup(Nested(inner, true)));
            Assert.Equal(0, Flipped(r));
            Assert.Equal("", r.ResultReason);
            Assert.Equal(8 - Math.Pow(2 * inner, 3), Invariance.Volume(r.Mesh), 6);
        }
    }
}
