using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The 64-bit pair keys of the 2D mesher (source pairs of the small-angle cache, unsplittable pieces, grid cells) used the default hash
    /// of a long, lo ^ hi, which for (a &lt;&lt; 32) | c is a ^ c: ids that lie close to each other (neighbours in a mesh) fall on a few dozen
    /// values and 200 000 pairs make one chain per lookup. A snap-grid parcels mesh of 56 000 triangles spent 20 of 22 seconds there
    /// (QA 2.0 T5, item 2 of the Meshing2D open items); with the shared <see cref="EdgeKeyComparer"/> (<see cref="PairKeys"/>) the run is
    /// linear, the meshes are the same.
    /// </summary>
    public class QualityMesh2PairKeyHashTests
    {
        static long Pair(int a, int c) => ((long)a << 32) | (uint)c;

        /// <summary> counts the hashes taken and the key comparisons made by the sets and dictionaries that use it </summary>
        sealed class Counting : IEqualityComparer<long>
        {
            readonly IEqualityComparer<long> inner;
            public long Hashes, Comparisons;
            public Counting(IEqualityComparer<long> inner) { this.inner = inner; }
            public bool Equals(long a, long b) { ++Comparisons; return inner.Equals(a, b); }
            public int GetHashCode(long key) { ++Hashes; return inner.GetHashCode(key); }
        }

        [Fact]
        public void PairKeyComparer_SpreadsPairsOfNearbyIds()
        {
            // 2 000 ids, each paired with the 50 ids after it: the pairs a mesh makes of neighbouring sources
            var keys = new List<long>();
            for (int a = 0; a < 2000; ++a) for (int d = 1; d <= 50; ++d) keys.Add(Pair(a, a + d));
            int mixed = keys.Select(k => PairKeys.Comparer.GetHashCode(k)).Distinct().Count();
            int plain = keys.Select(k => k.GetHashCode()).Distinct().Count();
            Assert.True(mixed >= keys.Count * 0.99, "the pair key comparer: " + mixed + " distinct hashes of " + keys.Count);
            // (what the default hash did: 100 000 keys on about a hundred values)
            Assert.True(plain < keys.Count / 100, "the default hash was expected to collide: " + plain + " distinct of " + keys.Count);
        }

        /// <summary> a snap-grid parcels mesh: the keys the refinement actually uses stay on short chains (comparisons per hash taken) </summary>
        [Fact]
        public void SnapGridParcels_UseShortChains()
        {
            var input = Parcels(24);
            var counting = new Counting(PairKeys.Comparer);   // (the comparer the mesher uses in production)
            PairKeys.ForTests = counting;
            ConstrainedTriangulation2Result result;
            try { result = new QualityMesh2 { MinAngle = 30, SnapTolerance = 1.0 / 64, SteinerLimit = 400000 }.Compute(input); }
            finally { PairKeys.ForTests = null; }
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.True(counting.Hashes > 5000, "the mesher must have used the comparer: " + counting.Hashes + " hashes");
            double chain = (double)counting.Comparisons / counting.Hashes;
            Assert.True(chain < 3, "comparisons per hash: " + chain.ToString("F1") + " (hashes " + counting.Hashes + ", comparisons " + counting.Comparisons + ")");
        }

        static ConstrainedMesh2Input Parcels(int side)
        {
            var rnd = new Random(7);
            var input = new ConstrainedMesh2Input();
            for (int i = 0; i <= side; ++i)
                for (int j = 0; j <= side; ++j) {
                    bool edge = i == 0 || j == 0 || i == side || j == side;
                    input.Points.Add(new Vector2d(i + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5), j + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5)));
                }
            for (int i = 0; i < side; ++i)
                for (int j = 0; j < side; ++j) {
                    int a = i * (side + 1) + j;
                    input.Rings.Add(new Mesh2Ring(new[] { a, a + side + 1, a + side + 2, a + 1 }, false, i * side + j));
                }
            return input;
        }
    }
}
