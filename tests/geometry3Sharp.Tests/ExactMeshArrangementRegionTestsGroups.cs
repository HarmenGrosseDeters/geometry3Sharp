using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Region-building tests for <see cref="ExactMeshArrangement.PreserveTriangleGroups"/>: coplanar triangles of one
    /// mesh merge across flat edges only when their TriangleGroup ids match (when PreserveTriangleGroups is enabled).
    /// </summary>
    public class ExactMeshArrangementRegionTestsGroups
    {
        // 2x2 quad grid in z = 0 (8 triangles); left column gets groupLeft, right column gets groupRight.
        static DMesh3 SubdividedPlate(int groupLeft, int groupRight, bool enableGroups = true)
        {
            var m = new DMesh3();
            if (enableGroups)
                m.EnableTriangleGroups(0);
            int[,] v = new int[3, 3];
            for (int iy = 0; iy < 3; ++iy)
                for (int ix = 0; ix < 3; ++ix)
                    v[ix, iy] = m.AppendVertex(new Vector3d(ix, iy, 0));
            for (int iy = 0; iy < 2; ++iy) {
                for (int ix = 0; ix < 2; ++ix) {
                    int g = ix == 0 ? groupLeft : groupRight;
                    int t0 = m.AppendTriangle(v[ix, iy], v[ix + 1, iy], v[ix + 1, iy + 1]);
                    int t1 = m.AppendTriangle(v[ix, iy], v[ix + 1, iy + 1], v[ix, iy + 1]);
                    if (enableGroups) {
                        m.SetTriangleGroup(t0, g);
                        m.SetTriangleGroup(t1, g);
                    }
                }
            }
            return m;
        }

        [Fact]
        public void FlatEdge_DifferentGroups_StaysSeparateRegions_WhenPreserveEnabled()
        {
            DMesh3 plate = SubdividedPlate(10, 20, enableGroups: true);

            // Default (PreserveTriangleGroups = false): all 8 coplanar triangles merge into 1 region.
            var arrDefault = new ExactMeshArrangement(new List<DMesh3> { plate }) { BuildRegions = true };
            arrDefault.Compute();
            var defaultRegions = new HashSet<int>(arrDefault.RegionOf);
            Assert.Single(defaultRegions);

            // With PreserveTriangleGroups = true: left (group 10) and right (group 20) stay 2 separate regions.
            var arrPreserved = new ExactMeshArrangement(new List<DMesh3> { plate }) { BuildRegions = true, PreserveTriangleGroups = true };
            arrPreserved.Compute();
            var preservedRegions = new HashSet<int>(arrPreserved.RegionOf);
            Assert.Equal(2, preservedRegions.Count);

            // Every pair of triangles in the same region must share the same TriangleGroup.
            var groupOfRegion = new Dictionary<int, int>();
            for (int ti = 0; ti < arrPreserved.Triangles.Count; ++ti) {
                int r = arrPreserved.RegionOf[ti];
                int g = plate.GetTriangleGroup(arrPreserved.Triangles[ti].Tid);
                if (groupOfRegion.TryGetValue(r, out int prev))
                    Assert.Equal(prev, g);
                else
                    groupOfRegion[r] = g;
            }
        }

        [Fact]
        public void FlatEdge_SameGroupOrUngrouped_JoinsIntoOneRegion_WhenPreserveEnabled()
        {
            DMesh3 sameGroup = SubdividedPlate(7, 7, enableGroups: true);
            var arrSame = new ExactMeshArrangement(new List<DMesh3> { sameGroup }) { BuildRegions = true, PreserveTriangleGroups = true };
            arrSame.Compute();
            Assert.Single(new HashSet<int>(arrSame.RegionOf));

            DMesh3 ungrouped = SubdividedPlate(0, 0, enableGroups: false);
            Assert.False(ungrouped.HasTriangleGroups);
            var arrUngrouped = new ExactMeshArrangement(new List<DMesh3> { ungrouped }) { BuildRegions = true, PreserveTriangleGroups = true };
            arrUngrouped.Compute();
            Assert.Single(new HashSet<int>(arrUngrouped.RegionOf));
        }
    }
}
