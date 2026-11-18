using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> The parameter MaxTrialInsertionsPerWrapVertex of the refinement's work bound (docs/design/TET_MESHING.md section 17.10, #1138). </summary>
    public class TetRefinementWorkBoundParameterTests
    {
        static MeshTetrahedralization Make(string shape, double alpha, double rho) => TetRefinementWorkBoundTests.Make(shape, alpha, rho);
        static bool Go(MeshTetrahedralization tm) => TetRefinementWorkBoundTests.Go(tm);
        static int V(MeshTetrahedralization tm) => TetRefinementWorkBoundTests.WrapVertices(tm);

        [Fact]
        public void TerminatingRuns_AreNotCut()
        {
            // 1.1 on the sphere ends with 7 trial insertions per wrap vertex: below the bound, so the same result as without it
            MeshTetrahedralization a = Make("sphere", 12, 1.1);
            Assert.True(Go(a));
            MeshTetrahedralization off = Make("sphere", 12, 1.1);
            off.MaxTrialInsertionsPerWrapVertex = 0;
            Assert.True(Go(off));
            Assert.False(a.Counts.ContainsKey("work_limit_reached"));
            Assert.Equal(off.Result.Vertices, a.Result.Vertices);
            Assert.Equal(off.Result.Tets, a.Result.Tets);
            Assert.Equal(off.Counts, a.Counts);
            Assert.True(a.Counts["trial_insertions"] > 5 * V(a));                        // and it did real work
        }

        [Fact]
        public void TheParameter_TurnsTheBoundOffOrTightensIt()
        {
            MeshTetrahedralization one = Make("sphere", 12, 1.0);
            one.MaxTrialInsertionsPerWrapVertex = 1;
            Go(one);
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, one.Status);
            Assert.Equal(1, one.Counts["work_limit_reached"]);
            Assert.InRange(one.Counts["trial_insertions"], V(one) + 2000, V(one) + 2000 + 30);
            // off: only MaxSteinerPoints stops it, and that is the user's own limit, not reported as the work bound
            MeshTetrahedralization off = Make("sphere", 12, 1.0);
            off.MaxTrialInsertionsPerWrapVertex = 0; off.MaxSteinerPoints = 3000;
            Go(off);
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, off.Status);
            Assert.Equal(3000, off.Counts["tet_splits"]);
            Assert.False(off.Counts.ContainsKey("work_limit_reached"));
            // negative and NaN also turn it off (the documented "0 or less")
            foreach (double v in new[] { -1.0, double.NaN }) {
                MeshTetrahedralization x = Make("sphere", 12, 1.0);
                x.MaxTrialInsertionsPerWrapVertex = v; x.MaxSteinerPoints = 500;
                Go(x);
                Assert.Equal(500, x.Counts["tet_splits"]);
                Assert.False(x.Counts.ContainsKey("work_limit_reached"));
            }
            // the user's lower limit wins over the bound, also unreported
            MeshTetrahedralization user = Make("sphere", 12, 1.0);
            user.MaxSteinerPoints = 500;
            Go(user);
            Assert.Equal(500, user.Counts["tet_splits"]);
            Assert.False(user.Counts.ContainsKey("work_limit_reached"));
        }
    }
}
