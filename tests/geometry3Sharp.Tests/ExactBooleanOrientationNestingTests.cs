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
            return new ExactMeshBoolean(m, new DMesh3(), U) { CountInsideOutCells = true, Orientation = mode };
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
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(k), D) { CountInsideOutCells = true, Orientation = Nesting };
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
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), tool, D) { CountInsideOutCells = true, Orientation = Nesting };
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
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { CountInsideOutCells = true };
            Assert.Equal(ExactMeshBoolean.OrientationModes.Refuse, b.Orientation);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1, b.InsideOutCells);
            Assert.Equal(0, b.ReorientedTriangles);
            AssertGoodResult(b.Result, 27);
            // reported in every mode, and by a boolean that fails nowhere else
            var legacy = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { CountInsideOutCells = true, Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(legacy.Compute(), legacy.Error);
            Assert.Equal(1, legacy.InsideOutCells);
            // a correctly oriented one: none
            var fine = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(0), D) { CountInsideOutCells = true };
            Assert.True(fine.Compute(), fine.Error);
            Assert.Equal(0, fine.InsideOutCells);
        }

        [Fact]
        public void TheCountIsOptIn_OffItIsZeroAndNoComponentsAreBuilt()
        {
            // the default: nothing is counted and the preparation builds no components
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D);
            Assert.False(b.CountInsideOutCells);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.InsideOutCells);
            Assert.Null(ExactMeshBoolean.prepare_input(FlippedCube(6)).ComponentStart);
            Assert.NotNull(ExactMeshBoolean.prepare_input(FlippedCube(6), null, true).ComponentStart);
            // ByNesting alone does not count either (the value would be 0 for an embedded input)
            var n = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { Orientation = Nesting };
            Assert.True(n.Compute(), n.Error);
            Assert.Equal(0, n.InsideOutCells);
            // a first mesh prepared without components (DifferenceEach) is prepared again when the count is on
            var statuses = new ExactMeshBoolean.BooleanStatus[1];
            var seen = new List<ExactMeshBoolean>();
            DMesh3[] r = ExactMeshBoolean.DifferenceEach(FlippedCube(6), new List<DMesh3> { Box(5, 5, 5, 6, 6, 6) }, op => { op.CountInsideOutCells = true; lock (seen) seen.Add(op); }, statuses);
            Assert.NotNull(r[0]);
            Assert.Equal(1, Assert.Single(seen).InsideOutCells);
        }

        [Fact]
        public void InsideOutCells_CountsEveryInvertedComponent()
        {
            // three inverted cubes and two outward ones, in two meshes: 3
            DMesh3 a = Together(Inverted(Box(0, 0, 0, 1, 1, 1)), Box(3, 0, 0, 4, 1, 1), Inverted(Box(6, 0, 0, 7, 1, 1)));
            DMesh3 c = Together(Box(10, 0, 0, 11, 1, 1), Inverted(Box(13, 0, 0, 14, 1, 1)));
            var b = new ExactMeshBoolean(a, c, U) { CountInsideOutCells = true };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(3, b.InsideOutCells);
            var n = new ExactMeshBoolean(a, c, U) { CountInsideOutCells = true, Orientation = Nesting };
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
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { CountInsideOutCells = true, AllowOpenMeshes = false, Orientation = Nesting };
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, b.Status);
            var allowed = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), open, D) { CountInsideOutCells = true, Orientation = Nesting };
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
                var reference = new ExactMeshBoolean(a, c, op) { CountInsideOutCells = true, Orientation = Nesting };
                Assert.True(reference.Compute(), reference.Error);
                var b = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(c, s), op) { CountInsideOutCells = true, Orientation = Nesting };
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
            var reference = new ExactMeshBoolean(a, Box(10, 10, 10, 11, 11, 11), U) { CountInsideOutCells = true };
            Assert.True(reference.Compute(), reference.Error);
            var b = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(Box(10, 10, 10, 11, 11, 11), s), U) { CountInsideOutCells = true };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2, reference.InsideOutCells);
            Assert.Equal(reference.InsideOutCells, b.InsideOutCells);
        }

        [Fact]
        public void Reuse_ComputeTwice_AndAcrossModes_OnOneInstance()
        {
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(3), D) { CountInsideOutCells = true, Orientation = Nesting };
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
            var inverted = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { CountInsideOutCells = true };
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Invert_TakesTheFlipOfTheDecidingTriangleIntoAccount(bool given_inverted)
        {
            // Compute always decides at the first triangle of a component, whose flip is 0: a deciding triangle with a flip needs a
            // component that starts with a degenerate triangle, which an embedded mesh cannot have (the long edge of a collinear
            // triangle is matched by a triangle that then overlaps the short edges' triangles). So the rule is checked on its own:
            // parity of the front as given, xor the triangle's flip relative to the component's start
            DMesh3 cube = given_inverted ? Inverted(Box(0, 0, 0, 1, 1, 1)) : Box(0, 0, 0, 1, 1, 1);
            var nest = new ExactMeshBoolean.NestingContext(new ExactMeshBoolean(cube, new DMesh3(), U), cube);
            nest.Begin();
            Assert.True(nest.Embedded);
            List<int> component = cube.TriangleIndices().ToList();
            var flipT = new bool[cube.MaxTriangleID];
            // the front of a triangle of the given cube is outside (even) if outward: nothing to flip; inverted: flip the component
            Assert.Equal(given_inverted, nest.Invert(component, flipT));
            // the same triangle as propagated flipped: the other answer
            flipT[component[0]] = true;
            Assert.Equal(!given_inverted, nest.Invert(component, flipT));
        }

        [Fact]
        public void ByNesting_StillRefusesNonFiniteInputs_WithoutPreparingTwice()
        {
            DMesh3 bad = Box(0, 0, 0, 1, 1, 1);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), bad, D) { Orientation = Nesting };
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InvalidInput, b.Status);
            int prepared = 0;
            var counted = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(3), D) { Orientation = Nesting, PrepareObserverForTests = m => System.Threading.Interlocked.Increment(ref prepared) };
            Assert.True(counted.Compute(), counted.Error);
            Assert.Equal(2, prepared);      // (the observer sees each input once: the finiteness pass; the nesting preparation is not parallel)
        }

        [Fact]
        public void ARayThatFindsNoDirection_IsCountedAsUndecided_AndTheLibraryDoesNotFail()
        {
            var b = new ExactMeshBoolean(Box(-1, -1, -1, 2, 2, 2), FlippedCube(6), D) { CountInsideOutCells = true,
                CountRayObserverForTests = ti => throw new InvalidOperationException("no ray") };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.InsideOutCells);
            Assert.Equal(2, b.InsideOutUndecided);          // (both inputs have one component)
            // reset by the next Compute
            b.CountRayObserverForTests = null;
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.InsideOutUndecided);
            Assert.Equal(1, b.InsideOutCells);
        }

        [Fact]
        public void Pipeline_RefusesAnUndecidedComponent_LikeAnInsideOutOne()
        {
            var parts = new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), Box(0, 0, 0, 1, 1, 1) };
            Action<ExactMeshBoolean> noRay = op => op.CountRayObserverForTests = ti => throw new InvalidOperationException("no ray");
            var p = new SolidBooleanPipeline(parts, D) { ConfigureBoolean = noRay };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, p.Status);
            Assert.Equal(2, p.Report.InsideOutUndecided);
            Assert.Contains("could not be tested", p.Error);
            var allowed = new SolidBooleanPipeline(parts, D) { ConfigureBoolean = noRay, AllowInsideOutInputs = true };
            Assert.True(allowed.Compute(), allowed.Error);
        }

        [Fact]
        public void Pipeline_DefaultsRefuseAnInsideOutInput_AllowRestoresTheEmptyTool()
        {
            // pinned behaviour change: a consistently inverted closed tool was Success with an empty solid (27), now InsideOutInput
            var parts = new List<DMesh3> { Box(-1, -1, -1, 2, 2, 2), Inverted(Box(0, 0, 0, 1, 1, 1)) };
            var defaults = new SolidBooleanPipeline(parts, D);
            Assert.False(defaults.AllowInsideOutInputs);
            Assert.False(defaults.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InsideOutInput, defaults.Status);
            Assert.Null(defaults.Result);
            var allowed = new SolidBooleanPipeline(parts, D) { AllowInsideOutInputs = true };
            Assert.True(allowed.Compute(), allowed.Error);
            Assert.Equal(27, Invariance.Volume(allowed.Result), 9);
        }

        // ---------------------------------------------------------------------------------------------- review follow-ups

        [Fact]
        public void NotEmbedded_AConsistentlyInvertedCubeBesideCrossingCubes_IsRefused_NotTakenAsEmpty()
        {
            // one pair of crossing cubes makes the whole mesh "not embedded": the inverted cube far away cannot be decided by the
            // parity of crossings, and used as given it would be an empty tool, silently
            DMesh3 mesh = Together(Inverted(Box(10, 10, 10, 11, 11, 11)), Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1, 3, 3, 3));
            var b = Solid(mesh, Nesting);
            Assert.False(b.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, b.Status);
            Assert.Null(b.Result);
            Assert.Contains("input mesh 0", b.Error);
            Assert.Contains("not embedded", b.Error);
            Assert.Contains("1 of its components enclose a negative volume", b.Error);
            // the same mesh with that cube outward: used as given (the crossing cubes read by the winding number: 15, and 1)
            var fine = Solid(Together(Box(10, 10, 10, 11, 11, 11), Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1, 3, 3, 3)), Nesting);
            Assert.True(fine.Compute(), fine.Error);
            AssertGoodResult(fine.Result, 16);
            // the inverted cube alone (an embedded mesh) is still derived: nesting makes it a cube
            var alone = Solid(Inverted(Box(10, 10, 10, 11, 11, 11)), Nesting);
            Assert.True(alone.Compute(), alone.Error);
            AssertGoodResult(alone.Result, 1);
            // the second input is named as well
            var second = new ExactMeshBoolean(Box(5, 5, 5, 6, 6, 6), mesh, U) { Orientation = Nesting };
            Assert.False(second.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InconsistentOrientation, second.Status);
            Assert.Contains("input mesh 1", second.Error);
        }

        // the six-vertex projective plane: closed (every edge has two triangles) and not orientable
        static DMesh3 ProjectivePlane()
        {
            var m = new DMesh3();
            foreach (var v in new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0.1, 0.2), new Vector3d(0.3, 1, 0.1), new Vector3d(-0.8, 0.4, 0.9), new Vector3d(-0.5, -0.9, 0.3), new Vector3d(0.6, -0.7, -0.5) })
                m.AppendVertex(v);
            int[][] faces = { new[] { 1, 2, 3 }, new[] { 1, 3, 4 }, new[] { 1, 4, 5 }, new[] { 1, 5, 6 }, new[] { 1, 6, 2 },
                              new[] { 2, 3, 5 }, new[] { 2, 5, 4 }, new[] { 3, 4, 6 }, new[] { 3, 6, 5 }, new[] { 4, 2, 6 } };
            foreach (var f in faces)
                m.AppendTriangle(f[0] - 1, f[1] - 1, f[2] - 1);
            return m;
        }

        [Fact]
        public void AnUnorientableComponentOfAnEmbeddedMesh_IsCounted_AndOnlyThere()
        {
            // an embedded, closed modulo 2 mesh with a component that cannot be oriented cannot be built from a real mesh in 3D that is
            // also embedded (a Moebius strip's rim would need edges with four triangles); the projective plane is closed modulo 2 and
            // not orientable, and "embedded" is set by hand after the arrangement (Begin does nothing a second time)
            DMesh3 mesh = ProjectivePlane();
            var op = new ExactMeshBoolean(mesh, new DMesh3(), U);
            var nest = new ExactMeshBoolean.NestingContext(op, mesh);
            nest.Begin();
            nest.Begin();
            Assert.False(nest.Embedded);
            Assert.True(nest.SelfIntersectingPairs > 0);
            nest.Embedded = true;
            ExactMeshBoolean.prepare_input(mesh, nest);
            Assert.Equal(1, nest.Unorientable);
            Assert.Equal(0, nest.Inconsistent);
            // not embedded: no count (the winding rule keeps it as it is)
            var other = new ExactMeshBoolean.NestingContext(op, mesh);
            other.Begin();
            ExactMeshBoolean.prepare_input(mesh, other);
            Assert.Equal(0, other.Unorientable);
        }

        [Fact]
        public void CancellingWhileTheInputsAreNested_StopsWithoutResult()
        {
            var cts = new System.Threading.CancellationTokenSource();
            // (the first input has flipped faces: nesting it without the arrangement would refuse it, not stop)
            var op = new ExactMeshBoolean(FlipFaces(Box(0, 0, 0, 2, 2, 2), 2), Box(1, 1, 1, 3, 3, 3), U) { Orientation = Nesting, Cancellation = cts.Token };
            // (cancelled as the inputs' preparation starts: the arrangement of the first input's nesting finds it)
            op.PrepareObserverForTests = m => cts.Cancel();
            Assert.False(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Cancelled, op.Status);
            Assert.Null(op.Result);
            Assert.Equal(0, op.ReorientedTriangles);
        }

        [Fact]
        public void ATriangleWithAZeroLengthEdge_IsItsOwnComponent_AndTheCubeIsStillCounted()
        {
            // the triangle (b, a, b') with b' at b: its edges b-a run both ways (closed by itself), the zero-length one is no edge
            DMesh3 mesh = Box(0, 0, 0, 1, 1, 1);
            int a = mesh.AppendVertex(new Vector3d(5, 5, 5)), b = mesh.AppendVertex(new Vector3d(6, 5, 5)), b2 = mesh.AppendVertex(new Vector3d(6, 5, 5));
            int sliver = mesh.AppendTriangle(b, a, b2);
            var p = ExactMeshBoolean.prepare_input(mesh, null, true);
            Assert.True(p.Closed);
            Assert.Equal(3, p.ComponentStart.Length);
            Assert.Equal(12, p.ComponentStart[1] - p.ComponentStart[0]);
            Assert.Equal(1, p.ComponentStart[2] - p.ComponentStart[1]);
            Assert.Equal(sliver, p.ComponentTriangles[p.ComponentStart[1]]);
            Assert.Equal(13, p.ComponentTriangles.Length);
            Assert.Equal(13, new HashSet<int>(p.ComponentTriangles).Count);
            // an inverted cube beside it is still found, the sliver has no side
            DMesh3 inverted = Inverted(Box(0, 0, 0, 1, 1, 1));
            int a2 = inverted.AppendVertex(new Vector3d(5, 5, 5)), b3 = inverted.AppendVertex(new Vector3d(6, 5, 5)), b4 = inverted.AppendVertex(new Vector3d(6, 5, 5));
            inverted.AppendTriangle(b3, a2, b4);
            var op = new ExactMeshBoolean(inverted, new DMesh3(), U) { CountInsideOutCells = true };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1, op.InsideOutCells);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }}
