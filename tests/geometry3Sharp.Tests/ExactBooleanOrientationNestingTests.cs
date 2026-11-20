using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.Orientation = ByNesting (#1210): a closed mod 2, embedded input is oriented so that the front of every
    /// triangle faces the even side of the nesting (the parity of the crossings of one exact ray per component), and
    /// ExactMeshBoolean.InsideOutCells: the components of closed inputs whose front is inside the solid (reported in every mode).
    /// </summary>
    public class ExactBooleanOrientationNestingTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        // the unit cube with the triangles of k of its six faces reversed (the faces in a fixed order: by normal)
        static DMesh3 FlippedCube(int k) { return FlipFaces(Box(0, 0, 0, 1, 1, 1), k); }

        // the cube with the triangles of its first k faces (by normal) reversed
        static DMesh3 FlipFaces(DMesh3 cube, int k)
        {
            var faces = cube.TriangleIndices().GroupBy(t => cube.GetTriNormal(t)).OrderBy(g => g.Key.x).ThenBy(g => g.Key.y).ThenBy(g => g.Key.z).ToList();
            Assert.Equal(6, faces.Count);
            foreach (int t in faces.Take(k).SelectMany(g => g).ToList())
                cube.ReverseTriOrientation(t);
            return cube;
        }

        static DMesh3 Inverted(DMesh3 m)
        {
            var copy = new DMesh3(m);
            foreach (int t in copy.TriangleIndices().ToList())
                copy.ReverseTriOrientation(t);
            return copy;
        }

        // the meshes as one mesh (their vertices are not welded: shells that share positions stay separate components)
        static DMesh3 Together(params DMesh3[] parts)
        {
            var all = new DMesh3(parts[0]);
            var editor = new MeshEditor(all);
            for (int i = 1; i < parts.Length; ++i)
                editor.AppendMesh(parts[i]);
            return all;
        }

        static readonly ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        static readonly ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union;
        const ExactMeshBoolean.OrientationModes Nesting = ExactMeshBoolean.OrientationModes.ByNesting;

        // the boundary of the solid of one mesh, on the winding rule
        static ExactMeshBoolean Solid(DMesh3 m, ExactMeshBoolean.OrientationModes mode)
        {
            return new ExactMeshBoolean(m, new DMesh3(), U) { Orientation = mode };
        }

        static void AssertGoodResult(DMesh3 result, double volume)
        {
            Assert.True(Invariance.AllFinite(result));
            Assert.True(ExactMeshBoolean.IsWindingClosed(result));
            Assert.Equal(volume, Invariance.Volume(result), 9);
        }

        // ---------------------------------------------------------------------------------------------- the flipped-cube table

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void FlippedCubeTool_ByNesting_IsTheCubeInEveryRow(int k)
        {
            // [-1,2]^3 minus the unit cube with k faces flipped: 26 for all k (also k = 6, a consistently inside-out tool),
            // the flips reported: the triangles of the faces that point inward, 2 per flipped face
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(k), D) { Orientation = Nesting };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2 * k, b.ReorientedTriangles);
            Assert.Equal(0, b.InsideOutCells);
            AssertGoodResult(b.Result, 26);
            Assert.True(b.Result.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
        }

        [Fact]
        public void TheInputsAreNotChanged_AndTheFlippedTriangleCountIsReported()
        {
            DMesh3 tool = FlippedCube(4);
            DMesh3 before = new DMesh3(tool);
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), tool, D) { Orientation = Nesting };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(8, b.ReorientedTriangles);
            Assert.Equal("", Invariance.Difference(before, tool));
        }

        // ---------------------------------------------------------------------------------------------- nested shells

        // an outer cube [0,6]^3, a cavity [1,5]^3 and an island [2,4]^3, each given outward or inward
        static DMesh3 ThreeShells(bool outerOut, bool cavityOut, bool islandOut)
        {
            DMesh3 outer = Box(0, 0, 0, 6, 6, 6), cavity = Box(1, 1, 1, 5, 5, 5), island = Box(2, 2, 2, 4, 4, 4);
            return Together(outerOut ? outer : Inverted(outer), cavityOut ? cavity : Inverted(cavity), islandOut ? island : Inverted(island));
        }

        public static IEnumerable<object[]> ShellOrientations()
        {
            for (int bits = 0; bits < 8; ++bits)
                yield return new object[] { (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0 };
        }

        [Theory]
        [MemberData(nameof(ShellOrientations))]
        public void NestedShells_AllEightOrientationsGiveTheSameSolid(bool outerOut, bool cavityOut, bool islandOut)
        {
            // outer minus cavity plus island: 216 - 64 + 8; the outer shell outward, the cavity wall inward (facing the cavity),
            // the island outward, whatever was given; the flips are the triangles of the shells given the other way
            var b = Solid(ThreeShells(outerOut, cavityOut, islandOut), Nesting);
            Assert.True(b.Compute(), b.Error);
            AssertGoodResult(b.Result, 160);
            Assert.Equal(12 * ((outerOut ? 0 : 1) + (cavityOut ? 1 : 0) + (islandOut ? 0 : 1)), b.ReorientedTriangles);
            Assert.Equal(0, b.InsideOutCells);
            Assert.Equal(3, Components(b.Result));
            var reference = Solid(ThreeShells(true, false, true), Nesting);
            Assert.True(reference.Compute(), reference.Error);
            Assert.Equal("", Invariance.GeometricDifference(reference.Result, b.Result));
        }

        static int Components(DMesh3 m)
        {
            var cc = new MeshConnectedComponents(m);
            cc.FindConnectedT();
            return cc.Count;
        }

        [Fact]
        public void ACavityWallGivenOutward_ByNestingFlipsIt_TheWindingRuleReadsAUnion()
        {
            // the outer cube with a cavity whose wall is given outward: a union for the winding number (a solid cube, 216, not
            // counted as inside-out), a cavity for the nesting: flipped, volume outer minus cavity
            DMesh3 wrong = Together(Box(0, 0, 0, 6, 6, 6), Box(1, 1, 1, 5, 5, 5));
            var winding = Solid(wrong, ExactMeshBoolean.OrientationModes.Refuse);
            Assert.True(winding.Compute(), winding.Error);
            Assert.Equal(216, Invariance.Volume(winding.Result), 9);
            Assert.Equal(0, winding.InsideOutCells);
            var nesting = Solid(wrong, Nesting);
            Assert.True(nesting.Compute(), nesting.Error);
            AssertGoodResult(nesting.Result, 152);
            Assert.Equal(12, nesting.ReorientedTriangles);
            Assert.Equal(0, nesting.InsideOutCells);
        }

        [Fact]
        public void ASolidInsideTheCavity_StaysSolid()
        {
            // depth 3: the island, given outward, is solid again; inward it is flipped to outward
            foreach (bool islandOut in new[] { true, false }) {
                var b = Solid(Together(Box(0, 0, 0, 6, 6, 6), Inverted(Box(1, 1, 1, 5, 5, 5)), islandOut ? Box(2, 2, 2, 4, 4, 4) : Inverted(Box(2, 2, 2, 4, 4, 4))), Nesting);
                Assert.True(b.Compute(), b.Error);
                AssertGoodResult(b.Result, 160);
                Assert.Equal(islandOut ? 0 : 12, b.ReorientedTriangles);
            }
        }

        // ---------------------------------------------------------------------------------------------- touching shells

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwoCubesSharingAnEdge_OneFlipped(bool flipFirst)
        {
            // the cubes meet in the edge x = y = 1: it has four triangles; each cube is its own component
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), c = Box(1, 1, 0, 2, 2, 1);
            DMesh3 mesh = flipFirst ? Together(Inverted(a), c) : Together(a, Inverted(c));
            var b = Solid(mesh, Nesting);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(12, b.ReorientedTriangles);
            Assert.Equal(0, b.InsideOutCells);
            AssertGoodResult(b.Result, 2);
            // the winding rule: the inverted cube is an empty solid, reported
            var winding = Solid(mesh, ExactMeshBoolean.OrientationModes.Refuse);
            Assert.True(winding.Compute(), winding.Error);
            Assert.Equal(1, winding.InsideOutCells);
            Assert.Equal(1, Invariance.Volume(winding.Result), 9);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwoCubesSharingAVertex_OneFlipped(bool flipFirst)
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), c = Box(1, 1, 1, 2, 2, 2);
            var b = Solid(flipFirst ? Together(Inverted(a), c) : Together(a, Inverted(c)), Nesting);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(12, b.ReorientedTriangles);
            AssertGoodResult(b.Result, 2);
        }

        // ---------------------------------------------------------------------------------------------- not embedded

        [Fact]
        public void TwoCrossingCubesWithAFlippedFace_AreRefusedByName()
        {
            DMesh3 mesh = Together(Box(0, 0, 0, 2, 2, 2), FlipFaces(Box(1, 1, 1, 3, 3, 3), 1));
            var b = Solid(mesh, Nesting);
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SelfIntersectingInconsistent, b.Status);
            Assert.Null(b.Result);
            Assert.Contains("input mesh 0", b.Error);
            Assert.Contains("not embedded", b.Error);
            Assert.Contains("2 of its triangles", b.Error);
            // the same mesh, flipped face repaired: used as given (a union by the winding number: 8 + 8 - 1)
            var consistent = Solid(Together(Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1, 3, 3, 3)), Nesting);
            Assert.True(consistent.Compute(), consistent.Error);
            Assert.Equal(0, consistent.ReorientedTriangles);
            Assert.True(consistent.SelfIntersectingPairs > 0);
            AssertGoodResult(consistent.Result, 15);
        }

        [Fact]
        public void TwoStackedCubesWithAFlippedTriangle_AreRefusedByName_CoincidentFaces()
        {
            // the cubes share the face z = 1 (coincident faces: not embedded); with a flipped triangle the orientation is
            // inconsistent too, and nothing derives it
            DMesh3 top = Box(0, 0, 1, 1, 1, 2);
            top.ReverseTriOrientation(top.TriangleIndices().First());
            var b = Solid(Together(Box(0, 0, 0, 1, 1, 1), top), Nesting);
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SelfIntersectingInconsistent, b.Status);
            // consistent: the winding rule decides (the stack is one box)
            var ok = Solid(Together(Box(0, 0, 0, 1, 1, 1), Box(0, 0, 1, 1, 1, 2)), Nesting);
            Assert.True(ok.Compute(), ok.Error);
            Assert.Equal(0, ok.ReorientedTriangles);
            AssertGoodResult(ok.Result, 2);
        }

        [Fact]
        public void ACubeWithADoubledFace_IsRefusedByName()
        {
            // a closed cube and a doubled triangle (two coincident triangles running the same way: every edge has two triangles,
            // closed modulo 2, and the two cannot be oriented consistently across their shared edges... as one component)
            DMesh3 mesh = Box(0, 0, 0, 1, 1, 1);
            int a = mesh.AppendVertex(new Vector3d(5, 5, 5)), b = mesh.AppendVertex(new Vector3d(6, 5, 5)), c = mesh.AppendVertex(new Vector3d(5, 6, 5));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, b, c);
            var op = Solid(mesh, Nesting);
            Assert.False(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.SelfIntersectingInconsistent, op.Status);
            Assert.Contains("input mesh 0", op.Error);
        }

        // ---------------------------------------------------------------------------------------------- inside-out cells

        [Fact]
        public void AConsistentlyInvertedCube_IsAnEmptyTool_WithInsideOutCellsReported()
        {
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D);
            Assert.Equal(ExactMeshBoolean.OrientationModes.Refuse, b.Orientation);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1, b.InsideOutCells);
            Assert.Equal(0, b.ReorientedTriangles);
            AssertGoodResult(b.Result, 27);
            // reported in every mode, and by a boolean that fails nowhere else
            var legacy = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(legacy.Compute(), legacy.Error);
            Assert.Equal(1, legacy.InsideOutCells);
            // a correctly oriented one: none
            var fine = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(0), D);
            Assert.True(fine.Compute(), fine.Error);
            Assert.Equal(0, fine.InsideOutCells);
        }

        [Fact]
        public void InsideOutCells_CountsEveryInvertedComponent()
        {
            // three inverted cubes and two outward ones, in two meshes: 3
            DMesh3 a = Together(Inverted(Box(0, 0, 0, 1, 1, 1)), Box(3, 0, 0, 4, 1, 1), Inverted(Box(6, 0, 0, 7, 1, 1)));
            DMesh3 c = Together(Box(10, 0, 0, 11, 1, 1), Inverted(Box(13, 0, 0, 14, 1, 1)));
            var b = new ExactMeshBoolean(a, c, U);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(3, b.InsideOutCells);
            var n = new ExactMeshBoolean(a, c, U) { Orientation = Nesting };
            Assert.True(n.Compute(), n.Error);
            Assert.Equal(0, n.InsideOutCells);
            Assert.Equal(36, n.ReorientedTriangles);
            AssertGoodResult(n.Result, 5);
        }

        [Theory]
        [MemberData(nameof(ShellOrientations))]
        public void InsideOutCells_IsTheWindingNumberInFrontOfEachShell_NotTheNesting(bool outerOut, bool cavityOut, bool islandOut)
        {
            // the winding number in the three regions (between the outer wall and the cavity, in the cavity, in the island) from
            // the orientations; the front of a shell is the region outside it if it is given outward, inside it otherwise;
            // counted: an odd negative winding number there
            int s1 = outerOut ? 1 : -1, s2 = cavityOut ? 1 : -1, s3 = islandOut ? 1 : -1;
            int between = s1, inCavity = s1 + s2, inIsland = s1 + s2 + s3;
            int[] fronts = { outerOut ? 0 : between, cavityOut ? between : inCavity, islandOut ? inCavity : inIsland };
            int expected = fronts.Count(w => w < 0 && (w & 1) != 0);
            var b = Solid(ThreeShells(outerOut, cavityOut, islandOut), ExactMeshBoolean.OrientationModes.AreaMajority);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(expected, b.InsideOutCells);
        }

        [Fact]
        public void OpenInputs_AreNotCounted_AndKeepTheirOpenStatus()
        {
            DMesh3 open = Inverted(Box(0, 0, 0, 1, 1, 1));
            open.RemoveTriangle(open.TriangleIndices().First());
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { AllowOpenMeshes = false, Orientation = Nesting };
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, b.Status);
            var allowed = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { Orientation = Nesting };
            Assert.True(allowed.Compute(), allowed.Error);
            Assert.Equal(0, allowed.InsideOutCells);
            // (an open input is oriented by area, not by nesting: this consistent one is not changed)
            Assert.Equal(0, allowed.ReorientedTriangles);
        }

        // ---------------------------------------------------------------------------------------------- the pipeline

        [Fact]
        public void Pipeline_RefusesAnInsideOutInput_ByDefault_AndDerivesTheSolidByNesting()
        {
            var parts = new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(6) };
            var p = new SolidBooleanPipeline(parts, D);
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Null(p.Result);
            Assert.Equal(1, p.Report.InsideOutCells);
            Assert.Contains("inside-out", p.Error);
            // taken as given on request
            var given = new SolidBooleanPipeline(parts, D) { AllowInsideOutInputs = true };
            Assert.True(given.Compute(), given.Error);
            Assert.Equal(27, Invariance.Volume(given.Result), 9);
            Assert.Equal(1, given.Report.InsideOutCells);
            // the enclosed solid by nesting, an inconsistent tool as well
            foreach (int k in new[] { 3, 6 }) {
                var nested = new SolidBooleanPipeline(new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), FlippedCube(k) }, D) { Orientation = Nesting };
                Assert.True(nested.Compute(), nested.Error);
                Assert.Equal(26, Invariance.Volume(nested.Result), 9);
                Assert.Equal(0, nested.Report.InsideOutCells);
            }
        }

        [Fact]
        public void Pipeline_SelfIntersectingInconsistent_IsAnOrientationRefusal()
        {
            DMesh3 top = Box(0, 0, 1, 1, 1, 2);
            top.ReverseTriOrientation(top.TriangleIndices().First());
            var p = new SolidBooleanPipeline(new List<DMesh3> { Box(-5, -5, -5, 5, 5, 5), Together(Box(0, 0, 0, 1, 1, 1), top) }, D) { Orientation = Nesting };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InconsistentOrientation, p.Status);
            Assert.Contains("not embedded", p.Error);
        }

        [Fact]
        public void DifferenceEach_AppliesByNestingToTheSharedFirstMesh()
        {
            var statuses = new ExactMeshBoolean.BooleanStatus[2];
            DMesh3[] results = ExactMeshBoolean.DifferenceEach(ScaledBy(FlippedCube(6), 10), new List<DMesh3> { Box(1, 1, 1, 2, 2, 2), Box(20, 20, 20, 21, 21, 21) },
                op => op.Orientation = Nesting, statuses);
            Assert.All(statuses, s => Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, s));
            Assert.Equal(999, Invariance.Volume(results[0]), 9);
            Assert.Equal(1000, Invariance.Volume(results[1]), 9);
        }

        static DMesh3 ScaledBy(DMesh3 m, double f)
        {
            foreach (int v in m.VertexIndices().ToList())
                m.SetVertex(v, m.GetVertex(v) * f);
            return m;
        }

        // ---------------------------------------------------------------------------------------------- invariance, reuse, determinism

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void ByNesting_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var inputs = new List<(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)> {
                (Box(-1, -1, -1, 2, 2, 2), FlippedCube(3), D),
                (Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D),
                (ThreeShells(true, true, false), new DMesh3(), U),
                (Together(Inverted(Box(0, 0, 0, 1, 1, 1)), Box(1, 1, 0, 2, 2, 1)), new DMesh3(), U) };
            foreach (var (a, c, op) in inputs) {
                var reference = new ExactMeshBoolean(a, c, op) { Orientation = Nesting };
                Assert.True(reference.Compute(), reference.Error);
                var b = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(c, s), op) { Orientation = Nesting };
                Assert.True(b.Compute(), b.Error);
                Assert.Equal(reference.ReorientedTriangles, b.ReorientedTriangles);
                Assert.Equal(reference.InsideOutCells, b.InsideOutCells);
                Assert.Equal(reference.Result.TriangleCount, b.Result.TriangleCount);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(b.Result, s)));
                else
                    {
                    // (the frames that move the vertices by rounding: the volume to the rounding of their coordinates)
                    double v = Invariance.Volume(reference.Result);
                    Assert.InRange(Invariance.Volume(b.Result) / (s.Scale * s.Scale * s.Scale), v - 1e-3, v + 1e-3);
                }
            }
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void InsideOutCells_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var a = Together(Inverted(Box(0, 0, 0, 1, 1, 1)), Box(3, 0, 0, 4, 1, 1), Inverted(Box(6, 0, 0, 7, 1, 1)));
            var reference = new ExactMeshBoolean(a, Box(10, 10, 10, 11, 11, 11), U);
            Assert.True(reference.Compute(), reference.Error);
            var b = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(Box(10, 10, 10, 11, 11, 11), s), U);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2, reference.InsideOutCells);
            Assert.Equal(reference.InsideOutCells, b.InsideOutCells);
        }

        [Fact]
        public void Reuse_ComputeTwice_AndAcrossModes_OnOneInstance()
        {
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(3), D) { Orientation = Nesting };
            Assert.True(b.Compute(), b.Error);
            DMesh3 first = b.Result;
            int flips = b.ReorientedTriangles;
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(flips, b.ReorientedTriangles);
            Assert.Equal("", Invariance.Difference(first, b.Result));
            b.Orientation = ExactMeshBoolean.OrientationModes.Refuse;
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
            Assert.Equal(0, b.InsideOutCells);
            b.Orientation = Nesting;
            Assert.True(b.Compute(), b.Error);
            Assert.Equal("", Invariance.Difference(first, b.Result));
            // a refusal, then a success, resets the report
            var inverted = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D);
            Assert.True(inverted.Compute(), inverted.Error);
            Assert.Equal(1, inverted.InsideOutCells);
            inverted.Orientation = Nesting;
            Assert.True(inverted.Compute(), inverted.Error);
            Assert.Equal(0, inverted.InsideOutCells);
            Assert.Equal(12, inverted.ReorientedTriangles);
        }

        [Fact]
        public void Determinism_RepeatedAndSerialRunsGiveTheSameBits()
        {
            DMesh3 mesh = ThreeShells(false, true, false);
            var first = Solid(mesh, Nesting);
            Assert.True(first.Compute(), first.Error);
            for (int run = 0; run < 3; ++run) {
                var again = Solid(mesh, Nesting);
                Assert.True(again.Compute(), again.Error);
                Assert.Equal("", Invariance.Difference(first.Result, again.Result));
                Assert.Equal(first.ReorientedTriangles, again.ReorientedTriangles);
            }
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            try {
                var one = Solid(mesh, Nesting);
                Assert.True(one.Compute(), one.Error);
                Assert.Equal("", Invariance.Difference(first.Result, one.Result));
            } finally {
                gParallel.SerialOnThisThread = serial;
            }
            // another seed of the rays: the same result (the rays are exact)
            var seeded = Solid(mesh, Nesting);
            seeded.Seed = 12345;
            Assert.True(seeded.Compute(), seeded.Error);
            Assert.Equal("", Invariance.GeometricDifference(first.Result, seeded.Result));
        }

        [Fact]
        public void ADegenerateComponent_HasNoRayAndNoSide()
        {
            // a flat pillow of two zero-area triangles (closed by itself) next to an inverted cube: it has no triangle to start a ray
            // from, so it is left as it is, and the cube is flipped
            DMesh3 mesh = Inverted(Box(0, 0, 0, 1, 1, 1));
            int a = mesh.AppendVertex(new Vector3d(5, 5, 5)), b = mesh.AppendVertex(new Vector3d(6, 5, 5)), c = mesh.AppendVertex(new Vector3d(7, 5, 5));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, b);
            var op = Solid(mesh, Nesting);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(12, op.ReorientedTriangles);
            AssertGoodResult(op.Result, 1);
            var winding = Solid(mesh, ExactMeshBoolean.OrientationModes.Refuse);
            Assert.True(winding.Compute(), winding.Error);
            Assert.Equal(1, winding.InsideOutCells);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }}
