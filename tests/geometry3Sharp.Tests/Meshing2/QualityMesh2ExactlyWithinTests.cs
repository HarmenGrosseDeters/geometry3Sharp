using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> QualityMesh2.Refiner.ExactlyWithin, the test a split point of a piece shorter than 2^-1026 must pass (exactly on its
    /// source, strictly between the piece's ends), at subnormal magnitudes where products underflow </summary>
    public class QualityMesh2ExactlyWithinTests
    {
        [Fact]
        public void TinyPieces_AreSplitOnlyExactly()
        {
            // a segment whose midpoint is exact (even integer coordinates) is still split; the refiner's check accepts exactly the
            // points on the piece, strictly between its ends
            double t = double.Epsilon;
            var a = new Vector2d(0, 0); var b = new Vector2d(4 * t, 2 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(2 * t, t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(2 * t, 2 * t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, a));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(6 * t, 3 * t)));
            var v = new Vector2d(0, 0); var w = new Vector2d(0, 4 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(v, w, v, w, new Vector2d(0, 3 * t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(v, w, v, w, new Vector2d(0, -t)));
            // ends rounded next to ab (a crossing): p on ab between them counts, p on their line but off ab does not
            var u2 = new Vector2d(t, 0); var w2 = new Vector2d(3 * t, 2 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(a, b, u2, w2, new Vector2d(2 * t, t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, u2, w2, new Vector2d(2 * t, 1 * t + t)));
            // a steep piece: compared along y, in which its ends differ most
            var sa = new Vector2d(0, 0); var sb = new Vector2d(t, 8 * t);
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(sa, sb, sa, sb, new Vector2d(t, 8 * t)));
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(new Vector2d(0, 0), new Vector2d(2 * t, 8 * t), new Vector2d(0, 0), new Vector2d(2 * t, 8 * t), new Vector2d(t, 4 * t)));
            // a steep piece with ends next to ab: p is between them along y, not along x (where it shares an end's x)
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(new Vector2d(0, 0), new Vector2d(2 * t, 16 * t), new Vector2d(t, 2 * t), new Vector2d(2 * t, 14 * t), new Vector2d(t, 8 * t)));
        }
    }
}
