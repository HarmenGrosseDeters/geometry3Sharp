using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The default output of QuadRemesher (stage 2) is pinned bit for bit: options added later (SizeAdaptation, QUAD_REMESHING.md 4.5)
    /// default to off and must not change a bit of it. The hashes were taken on the code before the size field existed, on Linux with .NET 10, and taken again on 2026-11-19 for the new default
    /// CollapseLength 0.6 (owner approved; the size field and the other options did not change a bit, checked before that); the trigonometry of the cross field differs
    /// in the last bits on .NET Framework and on Windows (the CI job of net48 gave other hashes for all five shapes), so there the test only checks that two
    /// runs agree and that the result is valid.
    /// </summary>
    public class QuadRemesherDefaultOutputTests
    {
        static ulong Hash(DPolyMesh r)
        {
            ulong h = 14695981039346656037UL;
            Action<ulong> add = x => { h ^= x; h *= 1099511628211UL; };
            add((ulong)r.VertexCount); add((ulong)r.FaceCount);
            foreach (int o in r.FaceOffsets) add((ulong)o);
            foreach (int i in r.FaceIndices) add((ulong)i);
            for (int v = 0; v < r.VertexCount; ++v) {
                Vector3d p = r.GetVertex(v);
                add((ulong)BitConverter.DoubleToInt64Bits(p.x)); add((ulong)BitConverter.DoubleToInt64Bits(p.y)); add((ulong)BitConverter.DoubleToInt64Bits(p.z));
            }
            return h;
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "sphere", 16793278504409630334UL };
            // torus: re-pinned for #1674 (the default path repairs crossing edges of the lattice graph): the old output had a hole (a dropped face, 6 boundary vertices, Euler characteristic -1), the new one is the closed torus (Euler 0)
            yield return new object[] { "torus", 10803936435748404927UL };
            yield return new object[] { "box", 7670787724669005053UL };
            yield return new object[] { "cap", 13153883559173992252UL };
            yield return new object[] { "patch", 2834938008614960167UL };
        }

        [Theory, MemberData(nameof(Cases))]
        public void DefaultOutput_IsPinnedBitForBit(string name, ulong expected)
        {
            DMesh3 m;
            switch (name) {
                case "sphere": m = CrossFieldShapes.UnitSphere(); break;
                case "torus": m = CrossFieldShapes.TorusMesh(); break;
                case "box": m = CrossFieldShapes.FilletedBoxMesh(); break;
                case "cap": m = CrossFieldShapes.SphereCap(); break;
                default: m = CrossFieldShapes.OpenPatch(24); break;
            }
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = name == "patch" ? 0.08 : 0.2 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            ulong h = Hash(qr.Result);
            bool pinnedPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework");
            if (pinnedPlatform) Assert.True(expected == h, name + " hash " + h + "UL");
            else {
                var qr2 = new QuadRemesher(m) { Field = cf, TargetEdgeLength = name == "patch" ? 0.08 : 0.2 };
                Assert.Equal(QuadRemeshStatus.Success, qr2.Compute());
                Assert.Equal(h, Hash(qr2.Result));
                Assert.True(PolyMeshMetrics.Compute(qr.Result).IsManifold);
            }
        }
    }
}
