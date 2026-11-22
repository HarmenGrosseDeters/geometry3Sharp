using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The InsideOutInput advice of SolidBooleanPipeline (InsideOutAdviceTests) through its internal parts: the per-input
    /// counts of ExactMeshBoolean it reads, and the naming of a repaired copy (in a file of its own: it uses members the base
    /// library does not have, so that the tests of InsideOutAdviceTests still compile and run against the base).
    /// </summary>
    public class InsideOutAdviceInternalTests
    {
        static readonly ExactMeshBoolean.Operations D = InsideOutAdviceTests.D;
        const ExactMeshBoolean.OrientationModes Nesting = ExactMeshBoolean.OrientationModes.ByNesting;
        static List<DMesh3> Parts(DMesh3 tool) { return InsideOutAdviceTests.Parts(tool); }
        static DMesh3 DentedThroughBox() { return InsideOutAdviceTests.DentedThroughBox(); }

        [Fact]
        public void ARepairedCopy_IsNamedAsWhatTheRepairProduced()
        {
            var op = new ExactMeshBoolean(Parts(DentedThroughBox()), D) { CountInsideOutCells = true, Orientation = Nesting };
            Assert.True(op.Compute(), op.Error);
            var repaired = new List<SolidBooleanInputRepair> { new SolidBooleanInputRepair { Index = 1, Method = "MeshAutoRepair" } };
            string advice = SolidBooleanPipeline.InsideOutAdvice(op, new List<int> { 0, 1 }, repaired);
            Assert.Contains("input 1 (the closed copy that the input repair MeshAutoRepair produced from the open input) self-intersects", advice);
            Assert.Contains("close the input before the boolean", advice);
            Assert.DoesNotContain("ByNesting derives", advice);
            // (the pipeline's index of the boolean's input: active maps it)
            advice = SolidBooleanPipeline.InsideOutAdvice(op, new List<int> { 0, 2 }, new List<SolidBooleanInputRepair>());
            Assert.Contains("input 2 self-intersects", advice);
        }

        [Fact]
        public void Reuse_ThePerInputCountsOfAByNestingRun_DoNotSurviveIntoARunInAnotherMode()
        {
            var op = new ExactMeshBoolean(Parts(DentedThroughBox()), D) { CountInsideOutCells = true, Orientation = Nesting };
            Assert.True(op.Compute(), op.Error);
            Assert.NotNull(op.NestingSelfIntersectingPairsOfInput);
            op.Orientation = ExactMeshBoolean.OrientationModes.Refuse;
            Assert.True(op.Compute(), op.Error);
            Assert.Null(op.NestingSelfIntersectingPairsOfInput);
            Assert.Equal(new[] { 0, 1 }, op.InsideOutCellsOfInput);
        }
    }
}
