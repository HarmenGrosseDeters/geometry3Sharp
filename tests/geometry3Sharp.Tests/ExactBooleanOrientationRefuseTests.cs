using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.Orientation (#1210): a closed mesh whose triangles are inconsistently oriented is refused by default
    /// (Status InconsistentOrientation) instead of being reoriented by the larger area, which turned a cube tool with three
    /// or more of its six faces flipped into an empty tool, silently. AreaMajority keeps the earlier behaviour.
    /// </summary>
    public class ExactBooleanOrientationRefuseTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // the unit cube with the triangles of k of its six faces reversed (the faces in a fixed order: by normal)
        static DMesh3 FlippedCube(int k)
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            var faces = cube.TriangleIndices().GroupBy(t => cube.GetTriNormal(t)).OrderBy(g => g.Key.x).ThenBy(g => g.Key.y).ThenBy(g => g.Key.z).ToList();
            Assert.Equal(6, faces.Count);
            foreach (int t in faces.Take(k).SelectMany(g => g).ToList())
                cube.ReverseTriOrientation(t);
            return cube;
        }

        static double Volume(DMesh3 m)
        {
            double v6 = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                v6 += m.GetVertex(tv.a).Dot(m.GetVertex(tv.b).Cross(m.GetVertex(tv.c)));
            }
            return v6 / 6;
        }

        static readonly ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;

        [Theory]
        [InlineData(0, 26.0)]
        [InlineData(1, double.NaN)]
        [InlineData(2, double.NaN)]
        [InlineData(3, double.NaN)]
        [InlineData(4, double.NaN)]
        [InlineData(5, double.NaN)]
        [InlineData(6, 27.0)]
        public void FlippedCubeTool_IsRefusedByDefault_UnlessConsistentlyOriented(int k, double expected)
        {
            // [-1,2]^3 minus the unit cube with k faces flipped: 0 flipped gives 26; 1 to 5 flipped are inconsistent and
            // refused, naming the tool; 6 flipped is a consistently inside-out tool, used as given (an empty solid: 27)
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(k), D);
            Assert.Equal(ExactMeshBoolean.OrientationModes.Refuse, b.Orientation);
            if (double.IsNaN(expected)) {
                Assert.False(b.Compute());
                Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
                Assert.Null(b.Result);
                Assert.Contains("input mesh 1", b.Error);
                Assert.Contains((2 * Math.Min(k, 6 - k)) + " of its 12 triangles", b.Error);
            } else {
                Assert.True(b.Compute(), b.Error);
                Assert.Equal(0, b.ReorientedTriangles);
                Assert.Equal(expected, Volume(b.Result), 9);
                Assert.True(b.Result.IsClosed());
            }
        }

        [Theory]
        [InlineData(0, 26.0)]
        [InlineData(1, 26.0)]
        [InlineData(2, 26.0)]
        [InlineData(3, 27.0)]
        [InlineData(4, 27.0)]
        [InlineData(5, 27.0)]
        [InlineData(6, 27.0)]
        public void FlippedCubeTool_WithAreaMajority_IsReorientedAsBefore(int k, double expected)
        {
            // the earlier behaviour, pinned: the larger part by area keeps its orientation; three flipped faces of six is a
            // tie (the first triangle keeps its orientation) and from there the tool is inside-out, an empty solid
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(k), D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(expected, Volume(b.Result), 9);
            Assert.Equal(k == 0 || k == 6 ? 0 : 2 * Math.Min(k, 6 - k), b.ReorientedTriangles);
        }

        [Fact]
        public void Pipeline_RefusesAnInconsistentlyOrientedInput_ByDefault()
        {
            var parts = new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(3) };
            var p = new SolidBooleanPipeline(parts, D);
            Assert.Equal(ExactMeshBoolean.OrientationModes.Refuse, p.Orientation);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InconsistentOrientation, p.Status);
            Assert.Null(p.Result);
            Assert.Contains("inconsistently oriented", p.Error);
            Assert.Empty(p.Report.RepairedInputs);
            // the earlier behaviour on request
            // (the area majority makes this tool inside-out, an empty solid: the pipeline refuses that unless it is allowed)
            var legacy = new SolidBooleanPipeline(parts, D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.False(legacy.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, legacy.Status);
            legacy = new SolidBooleanPipeline(parts, D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority, AllowInsideOutInputs = true };
            Assert.True(legacy.Compute(), legacy.Error);
            Assert.Equal(27, Volume(legacy.Result), 9);
        }

        [Fact]
        public void AnOpenInputWithAFlippedTriangle_StaysOpenInput()
        {
            // a cube with one triangle removed and one flipped: open also after orienting, so OpenInput (not
            // InconsistentOrientation) without AllowOpenMeshes, and used with its open-mesh classification with it
            DMesh3 open = Box(0, 0, 0, 1, 1, 1);
            int[] tris = open.TriangleIndices().ToArray();
            open.RemoveTriangle(tris[0]);
            open.ReverseTriOrientation(tris[5]);
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { AllowOpenMeshes = false };
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, b.Status);
            var withOpen = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { AllowOpenMeshes = true };
            Assert.True(withOpen.Compute(), withOpen.Error);
            Assert.True(withOpen.ReorientedTriangles > 0);
            Assert.True(Invariance.AllFinite(withOpen.Result));
            Assert.InRange(Volume(withOpen.Result), 25.5, 27);      // (the open cube, classified by generalized winding numbers)
        }

        [Fact]
        public void NaryInputs_TheRefusalNamesTheInconsistentMesh()
        {
            var b = new ExactMeshBoolean(new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), Box(5, 5, 5, 6, 6, 6), FlippedCube(2), Box(8, 8, 8, 9, 9, 9) }, ExactMeshBoolean.Operations.Union);
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
            Assert.Contains("input mesh 2", b.Error);
        }

        [Fact]
        public void Reuse_RefusedThenAreaMajority_OnTheSameInstance()
        {
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(1), D);
            Assert.False(b.Compute());
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
            b.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority;
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(26, Volume(b.Result), 9);
            Assert.Equal(2, b.ReorientedTriangles);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Refusal_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            for (int k = 0; k <= 6; ++k) {
                var reference = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(k), D);
                bool refOk = reference.Compute();
                var b = new ExactMeshBoolean(Invariance.Transformed(Box(-1, -1, -1, 2, 2, 2), s), Invariance.Transformed(FlippedCube(k), s), D);
                bool ok = b.Compute();
                Assert.Equal(k == 0 || k == 6, ok);
                Assert.Equal(refOk, ok);
                if (!ok) {
                    Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
                    Assert.Equal(reference.Error, b.Error);
                } else if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(b.Result, s)));
            }
        }

        [Fact]
        public void Resolve_StillOrientsARepairInput()
        {
            // Resolve is a repair of one mesh: it keeps AreaMajority
            DMesh3 resolved = ExactMeshBoolean.Resolve(FlippedCube(1));
            Assert.NotNull(resolved);
            Assert.Equal(1, Volume(resolved), 9);
            Assert.True(resolved.IsClosed());
        }

        [Fact]
        public void AStrayOpenTriangle_DoesNotSwitchTheRefusalOff()
        {
            // the decision is per component: a closed, inconsistently oriented cube next to a stray open triangle in the same
            // mesh is refused (with open meshes allowed, the default), not reoriented by area into an empty tool
            DMesh3 tool = FlippedCube(3);
            int a = tool.AppendVertex(new Vector3d(10, 10, 10)), b = tool.AppendVertex(new Vector3d(11, 10, 10)), c = tool.AppendVertex(new Vector3d(10, 11, 10));
            tool.AppendTriangle(a, b, c);
            var op = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), tool, D) { AllowOpenMeshes = true };
            Assert.True(op.AllowOpenMeshes);
            Assert.False(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, op.Status);
            Assert.Contains("6 of its 13 triangles", op.Error);
        }

        [Fact]
        public void TheFirstMeshIsRefusedToo_AlsoInDifferenceEach()
        {
            var b = new ExactMeshBoolean(FlippedCube(2), Box(0.5, 0.5, 0.5, 2, 2, 2), D);
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
            Assert.Contains("input mesh 0", b.Error);
            // the shared preparation of the first mesh (PreparedFirst)
            var ops = new List<ExactMeshBoolean>();
            DMesh3[] results = ExactMeshBoolean.DifferenceEach(FlippedCube(2), new List<DMesh3> { Box(0.5, 0.5, 0.5, 2, 2, 2), Box(-1, -1, -1, 0.5, 0.5, 0.5) }, x => { lock (ops) ops.Add(x); });
            Assert.All(results, Assert.Null);
            Assert.Equal(2, ops.Count);
            Assert.All(ops, x => Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, x.Status));
        }

        [Fact]
        public void TwoInconsistentMeshes_TheFirstIsNamedWithItsOwnCount()
        {
            var b = new ExactMeshBoolean(new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(1), FlippedCube(2) }, ExactMeshBoolean.Operations.Union);
            Assert.False(b.Compute());
            Assert.Contains("input mesh 1", b.Error);
            Assert.Contains("2 of its 12 triangles", b.Error);
            Assert.Equal(6, b.ReorientedTriangles);     // (both meshes counted: 2 + 4)
        }

        [Fact]
        public void InconsistentOrientation_GoesBeforeOpenInput()
        {
            // an inconsistently oriented closed mesh and an open one without AllowOpenMeshes: the orientation is reported
            DMesh3 open = Box(5, 5, 5, 6, 6, 6);
            open.RemoveTriangle(open.TriangleIndices().First());
            var b = new ExactMeshBoolean(new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(1), open }, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = false };
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
        }

        [Fact]
        public void Pipeline_ConfigureBoolean_CanOverrideTheOrientation()
        {
            // the pipeline sets Orientation before ConfigureBoolean runs, so ConfigureBoolean has the last word
            var parts = new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(1) };
            var p = new SolidBooleanPipeline(parts, D) { ConfigureBoolean = op => op.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(26, Volume(p.Result), 9);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
