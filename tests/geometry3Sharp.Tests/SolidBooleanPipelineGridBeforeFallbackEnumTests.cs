using System.Collections.Generic;
using g3;
using gs;
using Xunit;
using Auto = gs.SolidBooleanPipeline.AutomaticStepModes;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The tests of SolidBooleanPipelineGridBeforeFallbackTests that name a member that is new (AutomaticStepModes.GridBeforeFallback, DestroyedByRetry), in a file of their own
    /// so that the behaviour tests compile against a library without it (and fail there)
    /// </summary>
    public class SolidBooleanPipelineGridBeforeFallbackEnumTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;

        static List<DMesh3> Inputs()
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(4, 4, 4))) }.Generate().MakeDMesh();
            var sheet = new DMesh3();
            int a = sheet.AppendVertex(new Vector3d(-1, -1, 2)), b = sheet.AppendVertex(new Vector3d(5, -1, 2)), c = sheet.AppendVertex(new Vector3d(5, 5, 2)), d = sheet.AppendVertex(new Vector3d(-1, 5, 2));
            sheet.AppendTriangle(a, b, c); sheet.AppendTriangle(a, c, d);
            return new List<DMesh3> { box, sheet };
        }

        [Fact]
        public void TheDefault_IsTheSheetRetryAndTheRoundingRetry()
        {
            Assert.Equal(Auto.GridBeforeFallback | Auto.GridAfterRoundingNotClean, new SolidBooleanPipeline(Inputs(), D).AutomaticSteps);
            Assert.Equal((Auto)6, new SolidBooleanPipeline(Inputs(), D).AutomaticSteps);
            Assert.Equal((Auto)4, Auto.GridAfterRoundingNotClean);
            Assert.Equal((Auto)2, Auto.GridBeforeFallback);
            Assert.Equal((Auto)1, Auto.Grid);
        }

        [Fact]
        public void TheRejectionOfAnEmptyRetry_ChecksTheFirstInputAndTheResult()
        {
            DMesh3 box = Inputs()[0];
            Assert.True(SolidBooleanPipeline.DestroyedByRetry(box, new DMesh3()));
            Assert.True(SolidBooleanPipeline.DestroyedByRetry(box, null));
            Assert.False(SolidBooleanPipeline.DestroyedByRetry(box, box));
            Assert.False(SolidBooleanPipeline.DestroyedByRetry(new DMesh3(), new DMesh3()));
        }

        [Fact]
        public void EveryCombinationOfTheThreeBits_IsDefined_AndOtherBitsAreRefused()
        {
            for (int v = 0; v < 8; ++v) {
                var ok = new SolidBooleanPipeline(Inputs(), D) { AutomaticSteps = (Auto)v };
                Assert.True(ok.Compute(), v + ": " + ok.Error);
            }
            var all = new SolidBooleanPipeline(Inputs(), D) { AutomaticSteps = Auto.Grid | Auto.GridBeforeFallback | Auto.GridAfterRoundingNotClean };
            Assert.True(all.Compute(), all.Error);
            foreach (int v in new[] { 8, 9, 16, 12, -1 }) {
                var p = new SolidBooleanPipeline(Inputs(), D) { AutomaticSteps = (Auto)v };
                Assert.False(p.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
                Assert.Contains("AutomaticSteps", p.Error);
            }
        }

    }
}
