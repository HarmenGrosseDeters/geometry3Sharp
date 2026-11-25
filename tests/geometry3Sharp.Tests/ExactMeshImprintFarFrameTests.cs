using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshImprint on ulp-shifted near-duplicates far from the origin (#1489, docs/design/IMPRINT_SLIVER_CLEANUP.md section 5), through the public API only
    /// (the shapes, the shift and the checks of ExactMeshImprintSliverTests), so that these tests compile against the base library and fail there
    /// </summary>
    public class ExactMeshImprintFarFrameTests
    {
        static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter) => ExactMeshImprintSliverTests.Run(target, cutter);
        static DMesh3 Shape(string kind) => ExactMeshImprintSliverTests.Shape(kind);
        static DMesh3 Shifted(DMesh3 m, int axis, int ulps) => ExactMeshImprintSliverTests.Shifted(m, axis, ulps);
        static void AssertCleanAndCarried(DMesh3 target, ExactMeshImprint imprint) => ExactMeshImprintSliverTests.AssertCleanAndCarried(target, imprint);

        // the translated frame of the harness (far from the origin), the same frame with the translation negated, and the one with mixed signs:
        // the rounding of the exact vertices to the nearest double puts the curve vertices on either side of the target's edges, which
        // the sliver cleanup must handle in every frame (#1489). Each of these left slivers before the final stage accepted neutral contractions
        public static IEnumerable<object[]> FarCases()
        {
            foreach (string frame in new[] { "utm", "utm-negated", "utm-mixed" }) {
                yield return new object[] { frame, "box", 2, 1 };
                yield return new object[] { frame, "box", 2, -3 };
                yield return new object[] { frame, "cylinder", 2, 1 };
                yield return new object[] { frame, "cylinder", 2, -4 };
                yield return new object[] { frame, "cylinder", 2, 12 };
                yield return new object[] { frame, "cylinder", 0, 3 };
            }
            yield return new object[] { "z-only", "cylinder", 0, 2 };
            yield return new object[] { "z-only", "cylinder", 0, -5 };
        }

        internal static Similarity FarFrame(string name)
        {
            Vector3d t = Similarity.Utm.Translation;
            switch (name) {
                case "utm": return Similarity.Utm;
                case "utm-negated": return new Similarity(name, 1, Matrix3d.Identity, -t);
                case "origin": return Similarity.Identity;
                case "utm-mixed": return new Similarity(name, 1, Matrix3d.Identity, new Vector3d(t.x, -t.y, t.z));
                default: return new Similarity(name, 1, Matrix3d.Identity, new Vector3d(0, 0, t.z));
            }
        }

        [Theory]
        [MemberData(nameof(FarCases))]
        public void FarFromTheOrigin_UlpShiftedCopy_BothDirections_AreCleanClosedAndCarryTheCurve(string frame, string kind, int axis, int ulps)
        {
            DMesh3 a = Invariance.Transformed(Shape(kind), FarFrame(frame)), b = Shifted(a, axis, ulps);
            AssertCleanAndCarried(a, Run(a, b));
            AssertCleanAndCarried(b, Run(b, a));
        }

        // the same pair with its vertices and triangles in another order (#1489): clean, closed, carries the curve, the same volume. The positions of the
        // repaired vertices are not compared: the repair (not exact code) takes its candidates in id order where they tie, also without the stage
        // (measured: the box at the origin differs as well)
        [Theory]
        [InlineData("utm", "box", 2, 1)]
        [InlineData("utm-negated", "box", 2, -1)]
        [InlineData("utm", "cylinder", 2, 1)]
        public void FarFromTheOrigin_PermutedInput_IsCleanAndKeepsTheVolume(string frame, string kind, int axis, int ulps)
        {
            DMesh3 a = Invariance.Transformed(Shape(kind), FarFrame(frame)), b = Shifted(a, axis, ulps);
            double volume = ResultChecks.Volume(Run(a, b).Result);
            for (int seed = 1; seed <= 3; ++seed) {
                DMesh3 pa = Invariance.Permuted(a, seed), pb = Invariance.Permuted(b, 100 + seed);
                ExactMeshImprint imprint = Run(pa, pb);
                AssertCleanAndCarried(pa, imprint);
                Assert.Equal(volume, ResultChecks.Volume(imprint.Result), 6);       // (the volume sum itself carries the rounding of the coordinates far from the origin)
            }
        }
    }
}
