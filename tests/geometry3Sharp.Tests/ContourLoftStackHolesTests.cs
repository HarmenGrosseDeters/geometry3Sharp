using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary> ContourLoftStack, holes inside branching and merging groups (docs/design/CONTOUR_LOFT.md section 10.10). </summary>
    public class ContourLoftStackHolesTests
    {
        readonly ITestOutputHelper log;
        public ContourLoftStackHolesTests(ITestOutputHelper log) { this.log = log; }

        // coordinates on a 2^-20 grid: the inputs (and so the golden digests) do not depend on the platform's cos / sin
        static double Q(double v) => Math.Round(v * 1048576.0) / 1048576.0;

        static List<Vector3d> Disc(int n, double r, double z, double cx = 0, double cy = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; c.Add(new Vector3d(Q(cx + r * Math.Cos(a)), Q(cy + r * Math.Sin(a)), z)); }
            return c;
        }

        static List<Vector3d> Ellipse(int n, double rx, double ry, double z, double cx = 0, double cy = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; c.Add(new Vector3d(Q(cx + rx * Math.Cos(a)), Q(cy + ry * Math.Sin(a)), z)); }
            return c;
        }

        static List<Vector3d> Rect(double x0, double x1, double y0, double y1, double z, int k = 3)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0 + (x1 - x0) * i / k, y0, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1, y0 + (y1 - y0) * i / k, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1 - (x1 - x0) * i / k, y1, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0, y1 - (y1 - y0) * i / k, z));
            return c;
        }

        static IList<IList<IList<Vector3d>>> Slices(params IList<Vector3d>[][] s) =>
            s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c).ToList()).ToList();
        static IList<Vector3d>[] Sl(params IList<Vector3d>[] contours) => contours;

        // a digest of a mesh: the exact bits of all vertices and the triangles in order
        static string Digest(DMesh3 m)
        {
            unchecked {
                ulong h = 1469598103934665603UL;
                void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
                Mix((ulong)m.VertexCount); Mix((ulong)m.TriangleCount);
                foreach (int v in m.VertexIndices()) {
                    var p = m.GetVertex(v);
                    Mix((ulong)BitConverter.DoubleToInt64Bits(p.x)); Mix((ulong)BitConverter.DoubleToInt64Bits(p.y)); Mix((ulong)BitConverter.DoubleToInt64Bits(p.z));
                }
                foreach (int t in m.TriangleIndices()) { var tr = m.GetTriangle(t); Mix((ulong)tr.a); Mix((ulong)tr.b); Mix((ulong)tr.c); }
                return h.ToString("x16");
            }
        }

        // the inputs of the stage 2 success cases that existed before holes in branching groups
        static Dictionary<string, IList<IList<IList<Vector3d>>>> Existing() => new Dictionary<string, IList<IList<IList<Vector3d>>>> {
            ["merge"] = Slices(Sl(Disc(16, 1, 0, -2), Disc(16, 1, 0, 2)), Sl(Disc(16, 1, 1, -2), Disc(16, 1, 1, 2)), Sl(Rect(-3.5, 3.5, -1.2, 1.2, 2, 6))),
            ["branch3"] = Slices(Sl(Rect(-5, 5, -1.5, 1.5, 0, 8)), Sl(Disc(12, 1, 1, -3.5), Disc(12, 1, 1, 0), Disc(12, 1, 1, 3.5)), Sl(Disc(12, 1, 2, -3.5), Disc(12, 1, 2, 0), Disc(12, 1, 2, 3.5))),
            ["branchCut"] = Slices(Sl(Rect(-3, 3, -1, 1, 0, 6)), Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3))),
            ["torus"] = Slices(Sl(Rect(-4, 4, -1, 1, 0, 6)), Sl(Rect(-4, -2, -1, 1, 1, 3), Rect(2, 4, -1, 1, 1, 3)), Sl(Rect(-4, 4, -1, 1, 2, 6))),
            ["washer"] = Slices(Sl(Disc(20, 3, 0), Disc(20, 1, 0)), Sl(Disc(20, 3.2, 1), Disc(20, 1.2, 1)), Sl(Disc(20, 3, 2), Disc(20, 1, 2))),
            ["zigzag"] = Slices(Sl(Rect(-4, -0.5, -1, 1, 0, 4), Rect(0.5, 4, -1, 1, 0, 4)), Sl(Rect(-3, 1, -1.2, 1.2, 1, 4), Rect(2, 5, -1.2, 1.2, 1, 3))),
            ["cut1to2"] = Slices(Sl(Ellipse(48, 5, 2, 0)), Sl(Disc(24, 2.2, 1, -2.5), Disc(24, 2.2, 1, 2.5))),
            ["cut1to3"] = Slices(Sl(Ellipse(48, 6, 2.5, 0)), Sl(Disc(24, 1.8, 1, -4), Disc(24, 1.8, 1, 0), Disc(24, 1.8, 1, 4))),
            ["contained1to2"] = Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2))),
            ["dissimilar"] = Slices(Sl(Ellipse(48, 9, 3, 0)), Sl(Disc(24, 1, 1, -6), Disc(24, 1, 1, 3))),
            ["holeIn1to1"] = Slices(Sl(Disc(24, 4, 0), Disc(12, 1, 0)), Sl(Disc(24, 4.2, 1), Disc(12, 1.1, 1))),
        };

        [Fact]
        public void ExistingResults_AreBitForBitUnchanged()
        {
            var expected = new Dictionary<string, string> {
                ["merge"] = "c10640d5f4d9cbb1",
                ["branch3"] = "342952bf9b7f9dba",
                ["branchCut"] = "36b87a1a77ff484d",
                ["torus"] = "afcbf5b9deb16d59",
                ["washer"] = "a25ab2571b32289d",
                ["zigzag"] = "e18667c94a72b6e2",
                ["cut1to2"] = "90f363e2946bd80b",
                ["cut1to3"] = "67587b0ce8071407",
                ["contained1to2"] = "a041e717a3027efd",
                ["dissimilar"] = "3b69340fefa85301",
                ["holeIn1to1"] = "8782888705827f5b",
            };
            foreach (var kv in Existing()) {
                var r = new ContourLoftStack { CapEnds = true }.Compute(kv.Value);
                string d = r.Status == ContourLoftStackStatus.Success ? Digest(r.Mesh) : r.Status.ToString();
                log.WriteLine("[\"" + kv.Key + "\"] = \"" + d + "\",");
                Assert.Equal(expected[kv.Key], d);
            }
        }
    }
}
