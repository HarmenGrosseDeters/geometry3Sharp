using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Status = gs.MeshSheetExtension.ExtensionStatus;

namespace geometry3Sharp.Tests.SheetExt
{
    /// <summary> cases of the tests that the spike's list does not have: several runs on one sheet, a sheet that already cuts through </summary>
    internal static class SheetExtraCases
    {
        static readonly double[] YsWide = { -2.3, -0.5, 1.3, 3.1, 4.9, 6.7, 8.5, 10.3, 12.1 };

        public static List<SheetCase> All()
        {
            var list = new List<SheetCase>();
            list.Add(new SheetCase {
                Group = "extra", Name = "two runs on one sheet", Expect = SheetKind.Extended, Frames = true,
                Description = "cube, sheet z = 5 (up) 0.1 short of the cube at both ends x = 0.1 and x = 9.9, and wider than it in y: two separate runs, each continued; kept 500",
                Build = () => new SheetCaseData {
                    W = SheetGeometry.Box(0, 0, 0, 10, 10, 10),
                    Sheets = { SheetGeometry.Grid(new[] { 0.1, 1.1, 5.0, 8.0, 9.9 }, YsWide, (x, y) => 5, true) },
                    Analytic = 500
                }
            });
            list.Add(new SheetCase {
                Group = "extra", Name = "a sheet that already cuts through", Expect = SheetKind.Unchanged, Frames = true,
                Description = "cube, sheet z = 5 (up) larger than the cube on every side: no rim edge meets the interior, the sheet is returned unchanged; kept 500",
                Build = () => new SheetCaseData {
                    W = SheetGeometry.Box(0, 0, 0, 10, 10, 10),
                    Sheets = { SheetGeometry.Grid(new[] { -2.3, 5.0, 12.3 }, new[] { -2.3, 5.0, 12.1 }, (x, y) => 5, true) },
                    Analytic = 500
                }
            });
            return list;
        }
    }
}
