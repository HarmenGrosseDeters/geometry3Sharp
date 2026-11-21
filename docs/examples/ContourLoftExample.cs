using System;
using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Optimal triangulated lofting between parallel closed contours (ContourLoft)
    /// and multi-slice branching stacks (ContourLoftStack).
    /// </summary>
    public static class ContourLoftExample
    {
        static List<Vector3d> Circle(double cx, double cy, double z, double r, int n)
        {
            var pts = new List<Vector3d>(n);
            for (int i = 0; i < n; ++i) {
                double a = 2.0 * Math.PI * i / n;
                pts.Add(new Vector3d(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z));
            }
            return pts;
        }

        /// <summary> Lofts a capped frustum between two parallel circular contours using minimum-area DP. </summary>
        public static ContourLoftResult LoftFrustum()
        {
            var lower = Circle(0, 0, 0, 2.0, 24);
            var upper = Circle(0, 0, 2, 1.0, 24);
            var loft = new ContourLoft {
                Cost = ContourLoftCost.MinimumArea,
                Caps = true,
                CheckSelfIntersections = true
            };
            return loft.Compute(lower, upper);
        }

        /// <summary> Lofts a 3-slice stack where one lower oval branches into two upper circles. </summary>
        public static ContourLoftStackResult LoftBranchingStack()
        {
            var slice0 = new List<IList<Vector3d>> { Circle(0, 0, 0, 3.0, 24) };
            var slice1 = new List<IList<Vector3d>> {
                Circle(-1.2, 0, 2, 0.9, 16),
                Circle(1.2, 0, 2, 0.9, 16)
            };
            var slice2 = new List<IList<Vector3d>> {
                Circle(-1.2, 0, 4, 0.7, 16),
                Circle(1.2, 0, 4, 0.7, 16)
            };

            var stack = new ContourLoftStack {
                CapEnds = true,
                LoweredSaddle = true,
                CheckSelfIntersections = true
            };
            return stack.Compute(new List<IList<IList<Vector3d>>> { slice0, slice1, slice2 });
        }
    }
}
