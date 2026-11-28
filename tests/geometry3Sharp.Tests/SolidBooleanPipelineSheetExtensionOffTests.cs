using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using gs;
using geometry3Sharp.Tests.SheetExt;
using Xunit;
using Roles = System.Collections.Generic.List<g3.ExactMeshBoolean.InputRole>;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline.SheetExtension = Off (the default) is the run without the option, bit for bit: the status, the error text, the route, the report's numbers
    /// and the result mesh of several fixtures (a cut, a flush rim cured by the grid retry, short sheets that are refused, stated and routed, with and without the
    /// repair fallback, two short sheets, a sheet that cuts) are hashed, and the hashes are those of the library before the option existed (taken with this file on
    /// the base library: the file does not use the option, so it compiles there). A mode that is ignored (always on) changes the fixtures with a short sheet.
    /// </summary>
    public class SolidBooleanPipelineSheetExtensionOffTests
    {
        const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference;
        static readonly double[] Ys = { -2.3, -0.5, 1.3, 3.1, 4.9, 6.7, 8.5, 10.3, 12.1 };
        static readonly ExactMeshBoolean.InputRole S = ExactMeshBoolean.InputRole.Sheet, Sol = ExactMeshBoolean.InputRole.Solid;

        static DMesh3 Cube() => SheetGeometry.Box(0, 0, 0, 10, 10, 10);

        static DMesh3 Bent(double xe, double z0, bool up) =>
            SheetGeometry.Grid(new[] { -2.3, 1.1, 5.0, (5.0 + xe) / 2, xe }, Ys, (x, y) => z0 + 0.3 * Math.Max(0, x - 5), up);

        static DMesh3 Short(double share, double z0 = 5, bool up = true)
        {
            double xe = 10 - share * 18;
            for (int i = 0; i < 8; ++i)
                xe = 10 - share * Bent(xe, z0, up).GetBounds().Diagonal.Length;
            return Bent(xe, z0, up);
        }

        const double T = 1.2e6;

        static DMesh3 FlushCutter()
        {
            var m = new DMesh3();
            Vector2d[] poly = { new Vector2d(3, 3), new Vector2d(7, 3), new Vector2d(7, 7), new Vector2d(3, 7) };
            var low = new int[4]; var rim = new int[4];
            for (int i = 0; i < 4; ++i) {
                low[i] = m.AppendVertex(new Vector3d(T + poly[i].x, T + poly[i].y, T + 2));
                double top = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(T + 5) - (1 + i % 3));
                rim[i] = m.AppendVertex(new Vector3d(T + poly[i].x, T + poly[i].y, top));
            }
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(low[i], low[j], rim[j]);
                m.AppendTriangle(low[i], rim[j], rim[i]);
            }
            m.AppendTriangle(low[0], low[2], low[1]);
            m.AppendTriangle(low[0], low[3], low[2]);
            return m;
        }

        public static readonly string[] Names = {
            "closed tool", "flush rim", "stated short sheet", "routed short sheet", "routed short sheet with repair", "two stated short sheets", "sheet that cuts", "stated deep sheet"
        };

        public static IEnumerable<object[]> Fixtures() => Names.Select(n => new object[] { n });

        static SolidBooleanPipeline Make(string name)
        {
            Func<List<DMesh3>, SolidBooleanPipeline> p = m => new SolidBooleanPipeline(m, D);
            switch (name) {
                case "closed tool": return p(new List<DMesh3> { Cube(), SheetGeometry.Box(3, 3, 3, 12, 7, 7) });
                case "flush rim": return p(new List<DMesh3> { SheetGeometry.Box(T, T, T, T + 10, T + 10, T + 5), FlushCutter() });
                case "stated short sheet": { var q = p(new List<DMesh3> { Cube(), Short(0.005) }); q.InputRoles = new Roles { Sol, S }; return q; }
                case "routed short sheet": return p(new List<DMesh3> { Cube(), Short(0.005) });
                case "routed short sheet with repair": { var q = p(new List<DMesh3> { Cube(), Short(0.005) }); q.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair; return q; }
                case "two stated short sheets": { var q = p(new List<DMesh3> { Cube(), Short(0.005, 3, true), Short(0.005, 7, false) }); q.InputRoles = new Roles { Sol, S, S }; return q; }
                case "sheet that cuts": return p(new List<DMesh3> { Cube(), SheetGeometry.Grid(new[] { -2.3, 5.0, 12.1 }, Ys, (x, y) => 3 + 0.3 * Math.Max(0, x - 5), true) });
                default: { var q = p(new List<DMesh3> { Cube(), Short(0.08) }); q.InputRoles = new Roles { Sol, S }; return q; }
            }
        }

        // FNV-1a over what a run returns that does not depend on the clock or on the formatting of a number in a text (the texts of the route and of the error
        // carry witness points: they are compared against a run of the same library in the other tests, not hashed here)
        static string Hash(SolidBooleanPipeline p)
        {
            ulong h = 14695981039346656037UL;
            Action<byte[]> add = bytes => { foreach (byte b in bytes) { h ^= b; h *= 1099511628211UL; } };
            Action<string> str = s => add(Encoding.UTF8.GetBytes(s ?? "<null>"));
            Action<double> dbl = d => add(BitConverter.GetBytes(d));
            Action<long> lng = l => add(BitConverter.GetBytes(l));
            str(p.Status.ToString()); str(p.BooleanStatus.ToString()); str(p.SheetReason.ToString()); lng(p.Error == null ? 0 : 1);
            foreach (SolidBooleanRouteStep s in p.Report.Route) {
                str(s.Name); lng(s.Input); str(s.SheetReason.ToString()); dbl(s.Grid);
            }
            str(p.Report.ResultKind.ToString()); lng(p.Report.BooleanTriangles); lng(p.Report.ResultTriangles); lng(p.Report.InputSnappedVertices);
            str(p.Report.NotCleanReason); lng(p.Report.InputTriangles.Length);
            if (p.Result != null) {
                lng(p.Result.VertexCount); lng(p.Result.TriangleCount);
                foreach (int v in p.Result.VertexIndices()) { Vector3d x = p.Result.GetVertex(v); dbl(x.x); dbl(x.y); dbl(x.z); }
                foreach (int t in p.Result.TriangleIndices()) { Index3i x = p.Result.GetTriangle(t); lng(x.a); lng(x.b); lng(x.c); }
            }
            return h.ToString("x16");
        }

        // taken with this file on the library of master before the option (677e2ce32). Outcomes: a cut (closed tool, flush rim by the grid retry, sheet that cuts), the
        // refusal BooleanFailed / SheetRefused (stated short sheet, two of them, stated deep sheet: the hash of the first and the last is the same, a refusal without
        // a result), OpenInput after the fallback (routed short sheet, with and without the repair)
        static readonly Dictionary<string, string> Expected = new Dictionary<string, string> {
            { "closed tool", "e4d39eaadc9b9fda" },
            { "flush rim", "ba7802237549e75d" },
            { "stated short sheet", "9367678c8ef1165b" },
            { "routed short sheet", "184726ea8f96b03f" },
            { "routed short sheet with repair", "926076e3df3aad4a" },
            { "two stated short sheets", "746ca0838401cc3a" },
            { "sheet that cuts", "a6a7a0722272f904" },
            { "stated deep sheet", "9367678c8ef1165b" },
        };

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void TheDefault_IsTheRunOfTheLibraryBeforeTheOption_BitForBit(string name)
        {
            SolidBooleanPipeline p = Make(name);
            p.Compute();
            string h = Hash(p);
            Assert.True(Expected[name] == h, name + " => " + h);
        }
    }
}
