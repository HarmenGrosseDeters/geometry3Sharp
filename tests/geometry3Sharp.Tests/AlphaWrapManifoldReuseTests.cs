using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap keeps the labels of the last trial run of the manifold step (refinement at non-manifold spots) when
    /// it moved nothing, instead of running the step again: the same result and counts, bit for bit.
    /// </summary>
    public class AlphaWrapManifoldReuseTests
    {
        public static IEnumerable<object[]> Inputs()
        {
            yield return new object[] { "soup42", 20.0, 200.0 };
            yield return new object[] { "soup173", 20.0, 200.0 };
            yield return new object[] { "soup42", 60.0, 600.0 };
            yield return new object[] { "ellipsoid", 150.0, 1500.0 };
            yield return new object[] { "boxes", 20.0, 200.0 };
        }

        static DMesh3 Input(string name)
        {
            switch (name) {
                case "soup42": return AlphaWrapTests.RandomSoup(42);
                case "soup173": return AlphaWrapTests.RandomSoup(173);
                case "ellipsoid": return AlphaWrapTests.Ellipsoid(5, 0.3, 0.45);
                default:
                    // two boxes nearly touching at an edge: a pinch
                    DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)), NoSharedVertices = false }.Generate().MakeDMesh();
                    MeshEditor.Append(m, new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1.51, 1.51, 0.5), new Vector3d(0.5, 0.5, 0.5)), NoSharedVertices = false }.Generate().MakeDMesh());
                    return m;
            }
        }

        // the wrap without the Φ gates and the quality refinement of #408 (old: true): the inputs above were chosen on it,
        // and there the last trial of the pinch refinement is reused on most of them. With the defaults the quality
        // refinement inserts after that trial (the reuse must be dropped) and the second pinch pass decides
        static MeshAlphaWrap Wrap(DMesh3 input, double relAlpha, double relOffset, bool old, bool reuse = true)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, ReuseTrialLabels = reuse };
            if (old) { w.TraverseByEmptyBall = false; w.RefineMinAngleDegrees = 0; }
            return w;
        }

        public static IEnumerable<object[]> InputsBoth()
        {
            foreach (object[] c in Inputs())
                foreach (bool old in new[] { true, false })
                    yield return new object[] { c[0], c[1], c[2], old };
        }

        [Theory]
        [MemberData(nameof(InputsBoth))]
        public void ReusedTrialLabels_SameResultAndCounts(string name, double relAlpha, double relOffset, bool old)
        {
            DMesh3 input = Input(name);
            var reuse = Wrap(input, relAlpha, relOffset, old);
            var again = Wrap(input, relAlpha, relOffset, old, false);
            Assert.Equal(reuse.Compute(), again.Compute());
            Assert.Equal(again.Status, reuse.Status);
            Assert.Equal("", Invariance.Difference(again.Result, reuse.Result));
            var a = new Dictionary<string, long>(reuse.Counts);
            a.Remove("manifold_reused");
            Assert.Equal(again.Counts, a);
            Assert.False(again.Counts.ContainsKey("manifold_reused"));
        }

        // the same with the point limit reached during the refinement, and with the other start box (the retry)
        [Theory]
        [InlineData("soup42", 20.0, 200.0, 30, false)]
        [InlineData("soup173", 20.0, 200.0, 200, false)]
        [InlineData("boxes", 20.0, 200.0, 60, false)]
        [InlineData("soup42", 20.0, 200.0, 2000000, true)]
        [InlineData("ellipsoid", 150.0, 1500.0, 2000000, true)]
        public void ReusedTrialLabels_SameResult_PointLimitAndOtherBox(string name, double relAlpha, double relOffset, int maxPoints, bool otherBox)
        {
            DMesh3 input = Input(name);
            var reuse = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, MaxSteinerPoints = maxPoints, StartWithOtherBox = otherBox };
            var again = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, MaxSteinerPoints = maxPoints, StartWithOtherBox = otherBox,
                ReuseTrialLabels = false };
            Assert.Equal(reuse.Compute(), again.Compute());
            Assert.Equal(again.Status, reuse.Status);
            if (maxPoints < 1000)
                Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, reuse.Status);
            if (again.Result != null)
                Assert.Equal("", Invariance.Difference(again.Result, reuse.Result));
        }

        // under every similarity: the same result with and without the reuse, and the reuse happens (without rotation)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ReusedTrialLabels_SameResult_UnderSimilarity(string sim)
        {
            DMesh3 input = Invariance.Transformed(Input("soup42"), Similarity.ByName(sim));
            var reuse = Wrap(input, 60, 600, true);
            var again = Wrap(input, 60, 600, true, false);
            Assert.True(reuse.Compute(), reuse.Error);
            Assert.True(again.Compute(), again.Error);
            Assert.Equal("", Invariance.Difference(again.Result, reuse.Result));
            // (a rotation changes the start box, so the refinement can differ; the reuse is asserted without one)
            if (!sim.Contains("rotated"))
                Assert.True(reuse.Counts.ContainsKey("manifold_reused"), "no reuse under " + sim);
        }

        // the reuse happens on these inputs (otherwise the test above compares nothing new), and a reused instance gives
        // the same result again
        [Fact]
        public void ReusedTrialLabels_HappensAndReuse()
        {
            int reused = 0;
            foreach (object[] c in Inputs()) {
                var w = Wrap(Input((string)c[0]), (double)c[1], (double)c[2], true);
                Assert.True(w.Compute(), w.Error);
                if (w.Counts.ContainsKey("manifold_reused")) reused++;
                DMesh3 first = new DMesh3(w.Result);
                Assert.True(w.Compute(), w.Error);
                Assert.Equal("", Invariance.Difference(first, w.Result));
            }
            Assert.True(reused >= 3, $"reused on {reused} inputs");
        }
    }
}
