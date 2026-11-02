using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRoundingRepair's threshold-free unit vector (its own file: it uses an internal method that the base library
    /// does not have, and SafeRoundingBoundTests must still compile there for the new-tests-on-base check; the class name
    /// falls under the rounding mutation shard's filter)
    /// </summary>
    public class MeshRoundingRepairTestsUnitVector
    {
        // the unit vectors of the repair's moves: as Normalized above its threshold, and the same for 2^k v at any magnitude
        // (squares that would underflow or overflow are avoided by an exact power-of-two scaling; independent review)
        [Fact]
        public void UnitVector_SameForEveryPowerOfTwo()
        {
            var v = new Vector3d(3, 4, 0.1);
            Assert.Equal(v.Normalized, MeshRoundingRepair.unit(v));
            // (down to 2^-1015: below, 0.1 * 2^k is subnormal and the input itself is no longer 2^k v)
            foreach (int k in new[] { -1015, -600, -530, -200, 0, 200, 530, 600, 1000 }) {
                Vector3d w = v * Math.Pow(2, k);
                Assert.Equal(MeshRoundingRepair.unit(v), MeshRoundingRepair.unit(w));
            }
            Assert.Equal(new Vector3d(1, 0, 0), MeshRoundingRepair.unit(new Vector3d(3e-170, 0, 0)));
            Assert.Equal(Vector3d.Zero, MeshRoundingRepair.unit(Vector3d.Zero));
            Assert.Equal(Vector3d.Zero, MeshRoundingRepair.unit(new Vector3d(double.NaN, 0, 0)));
        }
    }
}
