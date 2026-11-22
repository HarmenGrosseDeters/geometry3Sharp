using System;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// A wall thinner than a grid cell with TwoLevelMarchingTetrahedra: both sides are kept, the mesh is closed and has no
    /// self-intersections by construction.
    /// </summary>
    public static class TwoLevelMarchingTetrahedraExample
    {
        sealed class Distance : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Distance(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
        }

        /// <summary>
        /// A spherical shell of radius 1 whose wall is a quarter of a grid cell thick (cell 0.1, wall 0.025), cut to the half
        /// space z &lt;= 0.5: a bowl with a rim. Marching cubes on the single field |r - 1| - w would return fragments.
        /// </summary>
        public static DMesh3 ThinBowl()
        {
            var gen = new TwoLevelMarchingTetrahedra {
                Field = new Distance(p => p.Length - 1.0),          // g: the wall is around its zero level set
                HalfWidth = 0.0125,                                  // w: the solid is |g| <= w
                Domain = new Distance(p => p.z - 0.5),               // d: cut to d <= 0
                Bounds = new AxisAlignedBox3d(new Vector3d(-1.23, -1.27, -1.21), new Vector3d(1.2, 1.2, 0.7)),
                CellSize = 0.1
            };
            gen.Generate();
            return gen.Mesh;
        }
    }
}
