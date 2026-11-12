using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The manifold step of MeshAlphaWrap (make_manifold, its trial runs in the pinch refinement, remove_pockets) after the
    /// Alpha Wrap 3.0 speedups (lever 4: no dictionaries, stars kept between trials without an insertion, component volumes
    /// only when the choice needs them, no star search for a vertex whose result facets form one disk). Bit-identical to before: the results are pinned to the hashes of the code before
    /// the change, on inputs whose pinch refinement runs several rounds of trials.
    /// </summary>
    public class AlphaWrapManifoldStepTests
    {
        // (seed, alpha, offset) -> SHA-256 prefix of the result before lever 4 (master 2947179 + #651)
        public static IEnumerable<object[]> Pinned()
        {
            yield return new object[] { 42, 60.0, 600.0, "6AF040F54B9CC48B" };
            yield return new object[] { 173, 20.0, 200.0, "261E1214AC836C70" };
            yield return new object[] { 3, 20.0, 200.0, "ED7C3843B9618E48" };
            yield return new object[] { 17, 20.0, 200.0, "614F77D53A96BA60" };
            yield return new object[] { 1, 40.0, 400.0, "0019E976E62D150F" };
        }

        internal static string Hash(DMesh3 mesh)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                sb.Append(vid).Append(':').Append(BitConverter.DoubleToInt64Bits(v.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(v.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(v.z)).Append(';');
            }
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                sb.Append(tid).Append(':').Append(t.a).Append(',').Append(t.b).Append(',').Append(t.c).Append(';');
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        [Theory]
        [MemberData(nameof(Pinned))]
        public void ManifoldStep_BitIdenticalToBefore(int seed, double relAlpha, double relOffset, string hash)
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(seed)) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            // (the inputs exercise the trials: several pinch rounds, so stars are rebuilt after insertions and reused without)
            Assert.True(w.Counts["pinch_rounds"] >= 1, $"soup {seed}: no pinch round");
            Assert.Equal(hash, Hash(w.Result));
        }

        // computed twice on one instance (and so on a new triangulation each time): the same result; the stars of the
        // first triangulation are not reused for the second
        [Fact]
        public void ManifoldStep_Reuse_SameResult()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(3)) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(w.Compute(), w.Error);
            string first = Hash(w.Result);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(first, Hash(w.Result));
            // (and on another instance after a different input: no state carried between instances either)
            var other = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(17)) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(other.Compute(), other.Error);
            Assert.Equal(first, Hash(new MeshAlphaWrap(AlphaWrapTests.RandomSoup(3)) { RelativeAlpha = 20, RelativeOffset = 200 }.ComputeAndGet()));
        }

        // invariance over Similarity.All(): the 2^k scales give the same result, scaled, bit for bit; the others are clean
        [Fact]
        public void ManifoldStep_Invariance()
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(17);
            var w0 = new MeshAlphaWrap(input) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(w0.Compute(), w0.Error);
            DMesh3 r0 = w0.Result;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = new DMesh3(input);
                MeshTransforms.PerVertexTransform(m, s.Apply);
                var w = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200 };
                Assert.True(w.Compute(), s.Name + ": " + w.Error);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                if (s.IsExact) {
                    Assert.Equal(r0.TriangleCount, w.Result.TriangleCount);
                    Assert.True(r0.VertexIndices().All(v => w.Result.GetVertex(v) == s.Apply(r0.GetVertex(v))), s.Name);
                    Assert.True(r0.TriangleIndices().All(t => w.Result.GetTriangle(t) == r0.GetTriangle(t)), s.Name);
                    Assert.Equal(w0.Counts["manifold_flips"], w.Counts["manifold_flips"]);
                }
            }
        }
    }

    static class MeshAlphaWrapTestExtensions
    {
        public static DMesh3 ComputeAndGet(this MeshAlphaWrap w)
        {
            Assert.True(w.Compute(), w.Error);
            return w.Result;
        }
    }
}
