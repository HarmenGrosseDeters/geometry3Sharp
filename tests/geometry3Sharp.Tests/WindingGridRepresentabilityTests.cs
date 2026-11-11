using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The grid of the winding-number contouring needs nodes origin + i * cell that are separate doubles: below 2^-40 of the largest
    /// coordinate they coincide, the edge between two nodes has no direction (0 / 0 = NaN) and the hidden-edge test threw "ray direction
    /// is not normalized" (found by the MeshFuzzPilot's libFuzzer run of MeshRobustRepair on a triangle soup with a part at x = -1.7e69;
    /// a variant took 146 s to get there).
    /// </summary>
    public class WindingGridRepresentabilityTests
    {
        [Fact]
        public void RequiredDepth_IsNotAvailable_WhenTheCellIsBelowTheCoordinateSpacing()
        {
            var far = new AxisAlignedBox3d(new Vector3d(-1.725436586697641E+69, -0.0625, 0), new Vector3d(-1.725436586697641E+69, 1, 1));
            Assert.Equal(int.MaxValue, WindingNumberOctree.RequiredDepth(far, 0.011399040556571047));
            // the same shape at the origin, and a UTM-like mesh with a millimetre cell, have a depth as before
            var near = new AxisAlignedBox3d(new Vector3d(0, -0.0625, 0), new Vector3d(0, 1, 1));
            Assert.Equal(7, WindingNumberOctree.RequiredDepth(near, 0.011399040556571047));
            var utm = new AxisAlignedBox3d(new Vector3d(632117, 5712043, 100), new Vector3d(632217, 5712143, 150));
            Assert.True(WindingNumberOctree.RequiredDepth(utm, 1e-3) < 20);
            // a mesh at 1e11 with the default grid (3.5 / 128 = 0.027, 1800 spacings of doubles) worked before and still does
            var offset = new AxisAlignedBox3d(new Vector3d(1e11 - 1, 1e11 - 1, 1e11 - 1), new Vector3d(1e11 + 1.5, 1e11 + 1, 1e11 + 1));
            Assert.NotEqual(int.MaxValue, WindingNumberOctree.RequiredDepth(offset, 0.027));
            // not a positive cell
            Assert.Equal(int.MaxValue, WindingNumberOctree.RequiredDepth(near, 0));
            Assert.Equal(int.MaxValue, WindingNumberOctree.RequiredDepth(near, -1));
            Assert.Equal(int.MaxValue, WindingNumberOctree.RequiredDepth(near, double.NaN));
        }

        /// <summary> the limit is 2^-46 of the largest coordinate, whichever side and axis it is on </summary>
        [Theory]
        [InlineData(0, 1)] [InlineData(0, -1)] [InlineData(1, 1)] [InlineData(1, -1)] [InlineData(2, 1)] [InlineData(2, -1)]
        public void RequiredDepth_LimitIsTheLargestCoordinate_OnEveryAxisAndSide(int axis, int sign)
        {
            const double largest = 1e6;
            Vector3d lo = new Vector3d(-3, -3, -3), hi = new Vector3d(3, 3, 3);
            if (sign > 0) { lo[axis] = largest - 2; hi[axis] = largest; } else { lo[axis] = -largest; hi[axis] = -largest + 2; }
            var box = new AxisAlignedBox3d(lo, hi);
            double limit = largest * Math.Pow(2, -46);
            Assert.NotEqual(int.MaxValue, WindingNumberOctree.RequiredDepth(box, limit));             // at the limit: usable
            Assert.Equal(int.MaxValue, WindingNumberOctree.RequiredDepth(box, limit * 0.999));         // just below: not
            Assert.NotEqual(int.MaxValue, WindingNumberOctree.RequiredDepth(box, limit * 1.001));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RequiredDepth_InvariantUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            // the box and cell scale by s, the depth does not (a translation moves the largest coordinate: only the scales are compared)
            if (!s.IsExact) return;
            var box = new AxisAlignedBox3d(new Vector3d(-2, -1, 0), new Vector3d(5, 3, 4));
            var scaled = new AxisAlignedBox3d(s.Scale * box.Min, s.Scale * box.Max);
            Assert.Equal(WindingNumberOctree.RequiredDepth(box, 0.05), WindingNumberOctree.RequiredDepth(scaled, 0.05 * s.Scale));
        }

        /// <summary>
        /// A union of two octahedra of radius 1 at 1e11: the grid cell (0.027) is 1800 spacings of doubles there. It worked before the
        /// representability rule and must still; the rule was first 2^-40 and refused it
        /// </summary>
        [Fact]
        public void Boolean_AtOffsetOf1e11_StillWorks()
        {
            var a = Octahedron(new Vector3d(1e11, 1e11, 1e11), 1);
            var b = Octahedron(new Vector3d(1e11 + 0.5, 1e11, 1e11), 1);
            var boolean = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.NotNull(boolean.Result);
            Assert.True(boolean.Result.TriangleCount > 100);
        }

        /// <summary>
        /// Octahedra of radius 1e154 and more: a.Distance(b) of a grid edge squares its components and overflows, the ray direction was
        /// 0 and the hidden-edge test threw (found by the review of the representability change; also on master)
        /// </summary>
        [Theory]
        [InlineData(1e154)]
        [InlineData(1e200)]
        public void Boolean_OfHugeMeshes_DoesNotThrow(double radius)
        {
            var a = Octahedron(Vector3d.Zero, radius);
            var b = Octahedron(new Vector3d(0.5 * radius, 0, 0), radius);
            var boolean = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union);
            boolean.Compute();      // true or false, but no exception
        }

        static DMesh3 Octahedron(Vector3d c, double r)
        {
            var m = new DMesh3();
            Vector3d[] v = { c + new Vector3d(r, 0, 0), c + new Vector3d(-r, 0, 0), c + new Vector3d(0, r, 0), c + new Vector3d(0, -r, 0), c + new Vector3d(0, 0, r), c + new Vector3d(0, 0, -r) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[][] t = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            foreach (int[] f in t) m.AppendTriangle(f[0], f[1], f[2]);
            return m;
        }

        /// <summary>
        /// The 79-byte pilot input: an octahedron of radius 2.4e-35 and a sliver tetrahedron 2.5e27 away on the x axis, repaired as one
        /// soup: no exception (the result may be any status)
        /// </summary>
        [Fact]
        public void RobustRepair_OfTinyMeshBesideADistantSliver_DoesNotThrow()
        {
            DMesh3 soup = Octahedron(new Vector3d(0, 6.018531076210112E-36, 4.513898307157584E-36), 2.407412430484045E-35);
            double x = -2.4758800785707605E+27;
            var tet = new DMesh3();
            tet.AppendVertex(new Vector3d(x, 0, 0)); tet.AppendVertex(new Vector3d(x, -8.96831017167883E-44, 0));
            tet.AppendVertex(new Vector3d(x, 1.4349296274686127E-42, 0)); tet.AppendVertex(new Vector3d(x, 0, 1.4349296274686127E-42));
            tet.AppendTriangle(0, 2, 1); tet.AppendTriangle(0, 1, 3); tet.AppendTriangle(0, 3, 2); tet.AppendTriangle(1, 2, 3);
            new MeshEditor(soup).AppendMesh(tet);
            var repair = new MeshRobustRepair(soup);
            bool ok = repair.Apply();
            // a defined outcome: either clean, or a named failure reason (today: "not repaired")
            if (ok) Assert.Equal("", MeshRobustRepair.NotCleanReason(repair.Mesh, false));
            else Assert.False(string.IsNullOrEmpty(repair.ResultReason));
        }
    }
}
