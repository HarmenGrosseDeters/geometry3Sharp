using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Property: for random triangle soups on a small integer lattice (many coplanar, touching, shared-vertex and
    /// shared-edge pairs, zero-area triangles), MeshRepairMetrics.SelfIntersectionPairs is the brute force over all pairs
    /// with ExactTriangleIntersection.IntersectsBeyondShared (SelfIntersectionPairsTests.BruteForce), and the count and the
    /// early-exit flag agree. A fixed seed keeps the run deterministic.
    /// </summary>
    public class SelfIntersectionPairsProperty
    {
        static readonly Gen<(int, int, int)> Point = Gen.Select(Gen.Int[0, 3], Gen.Int[0, 3], Gen.Int[0, 2]);

        [Fact]
        public void RandomLatticeSoups_PairSetEqualsTheBruteForce()
        {
            Gen.Select(Point, Point, Point).List[2, 14].Sample(tris => {
                var soup = SelfIntersectionPairsTests.Soup(tris.Select(t => (V(t.Item1), V(t.Item2), V(t.Item3))));
                var expected = SelfIntersectionPairsTests.BruteForce(soup);
                var actual = MeshRepairMetrics.SelfIntersectionPairs(soup);
                return expected.SequenceEqual(actual) && MeshRepairMetrics.HasSelfIntersections(soup) == (expected.Count > 0);
            }, seed: "0000000000000", iter: 3000);
        }

        static Vector3d V((int x, int y, int z) p) => new Vector3d(p.x, p.y, p.z);
    }
}
