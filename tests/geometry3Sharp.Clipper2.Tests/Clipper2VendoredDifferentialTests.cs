using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Pkg = Clipper2Lib;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// The vendored Clipper2 2.0.0 sources (g3.Clipper2.Vendored) against the Clipper2 2.0.0 NuGet package they replace
    /// as the engine: every operation the adapter uses, on the same procedural inputs, must give the same output paths,
    /// point for point and in the same order, and the same PolyTree nesting. Inputs: self-intersecting rings on a coarse
    /// lattice (vertices on other edges, collinear overlaps: the degenerate cases) and on a fine one (generic positions),
    /// and open polylines, at the magnitudes of the adapter's grid (±2^20 to ±2^50), with degenerate paths mixed in.
    /// Since the fix of FINDINGS item 5 the booleans may differ, but only where the package's area is off the exact area
    /// (ExactRegionArea) by more than the grid rounding and the vendored one is not (AssertFixes).
    /// On net48 (Windows CI) this compares the netstandard2.0 builds; the net10.0 build of the vendored sources binds
    /// List.EnsureCapacity to the BCL method instead of Clipper2's extension, which changes only list capacities.
    /// </summary>
    public class Clipper2VendoredDifferentialTests
    {
        static readonly Pkg.ClipType[] ClipTypes = { Pkg.ClipType.Union, Pkg.ClipType.Intersection, Pkg.ClipType.Difference, Pkg.ClipType.Xor };
        static readonly Pkg.FillRule[] FillRules = { Pkg.FillRule.EvenOdd, Pkg.FillRule.NonZero, Pkg.FillRule.Positive, Pkg.FillRule.Negative };

        static Pkg.Path64 RandomRing(Random rand, int n, long range, int bits = 20)
        {
            long step = (1L << bits) / range;
            var path = new Pkg.Path64(n);
            for (int i = 0; i < n; ++i)
                path.Add(new Pkg.Point64(rand.Next(-(int)range, (int)range + 1) * step, rand.Next(-(int)range, (int)range + 1) * step));
            return path;
        }

        /// <summary>
        /// pairs of random rings: n-gons with coordinates on a lattice of 2 range + 1 values per axis, spanning ±2^bits
        /// (bits 20, 40 and 50: the adapter's grid is ±2^40 by default and up to ±2^50, where Clipper2 takes its 128-bit
        /// paths); every 7th case adds the degenerate paths the adapter can pass on (empty, one and two points,
        /// repeated points, zero area)
        /// </summary>
        static IEnumerable<(Pkg.Paths64 a, Pkg.Paths64 b, int bits, string name)> Pairs(int seed, int count)
        {
            var rand = new Random(seed);
            for (int i = 0; i < count; ++i) {
                int n = i % 3 == 0 ? 4 : i % 3 == 1 ? 8 : 16;
                int bits = (i / 3) % 3 == 0 ? 20 : (i / 3) % 3 == 1 ? 40 : 50;
                long range = i % 2 == 0 ? 4 : 64;       // coarse: exact degeneracies; finer: fewer of them
                if (i % 5 == 4) range = 1 << 15;         // generic positions
                var a = new Pkg.Paths64 { RandomRing(rand, n, range, bits) };
                var b = new Pkg.Paths64 { RandomRing(rand, n, range, bits) };
                if (i % 4 == 3) a.Add(RandomRing(rand, 5, range, bits));   // more than one ring per operand
                if (i % 7 == 6) {
                    Pkg.Path64 r = RandomRing(rand, 4, range, bits);
                    a.Add(new Pkg.Path64());
                    a.Add(new Pkg.Path64 { r[0] });
                    b.Add(new Pkg.Path64 { r[1], r[2] });
                    b.Add(new Pkg.Path64 { r[0], r[0], r[1], r[1], r[2], r[3], r[3] });
                    b.Add(new Pkg.Path64 { r[0], new Pkg.Point64(2 * r[0].X - r[1].X, 2 * r[0].Y - r[1].Y), r[1] });   // collinear
                }
                yield return (a, b, bits, "seed " + seed + " case " + i + " (" + n + "-gons, range " + range + ", 2^" + bits + ")");
            }
        }

        static V.Paths64 ToVendored(Pkg.Paths64 paths)
        {
            var result = new V.Paths64(paths.Count);
            foreach (Pkg.Path64 p in paths)
                result.Add(g3.Clipper2.Bridge.ToVendored(p));
            return result;
        }

        /// <summary> the output, or the exception type and message </summary>
        static string Outcome(Func<string> run)
        {
            try {
                return run();
            } catch (Exception e) {
                return "throws " + e.GetType().Name + ": " + e.Message;
            }
        }

        static string Text(Pkg.Paths64 paths)
        {
            var sb = new StringBuilder();
            foreach (Pkg.Path64 p in paths) {
                foreach (Pkg.Point64 q in p) sb.Append(q.X).Append(',').Append(q.Y).Append(' ');
                sb.Append('|');
            }
            return sb.ToString();
        }

        static string Text(V.Paths64 paths)
        {
            var sb = new StringBuilder();
            foreach (V.Path64 p in paths) {
                foreach (V.Point64 q in p) sb.Append(q.X).Append(',').Append(q.Y).Append(' ');
                sb.Append('|');
            }
            return sb.ToString();
        }

        static string Text(Pkg.PolyPath64 node)
        {
            var sb = new StringBuilder("(");
            for (int i = 0; i < node.Count; ++i) {
                Pkg.PolyPath64 child = node.Child(i);
                sb.Append(Text(new Pkg.Paths64 { child.Polygon })).Append(Text(child));
            }
            return sb.Append(')').ToString();
        }

        static string Text(V.PolyPath64 node)
        {
            var sb = new StringBuilder("(");
            for (int i = 0; i < node.Count; ++i) {
                V.PolyPath64 child = node.Child(i);
                sb.Append(Text(new V.Paths64 { child.Polygon })).Append(Text(child));
            }
            return sb.Append(')').ToString();
        }

        [Fact]
        public void Booleans_AreIdentical()
        {
            int compared = 0, fixes = 0;
            foreach (var (a, b, _, name) in Pairs(11, 240))
                foreach (Pkg.ClipType op in ClipTypes)
                    foreach (Pkg.FillRule fill in FillRules)
                        foreach (bool collinear in new[] { false, true }) {
                            var pc = new Pkg.Clipper64 { PreserveCollinear = collinear };
                            pc.AddSubject(a); pc.AddClip(b);
                            var vc = new V.Clipper64 { PreserveCollinear = collinear };
                            vc.AddSubject(ToVendored(a)); vc.AddClip(ToVendored(b));
                            var pTree = new Pkg.PolyTree64(); var vTree = new V.PolyTree64();
                            var pOpen = new Pkg.Paths64(); var vOpen = new V.Paths64();
                            bool pOk = pc.Execute(op, fill, pTree, pOpen);
                            bool vOk = vc.Execute(g3.Clipper2.Bridge.ToVendored(op), g3.Clipper2.Bridge.ToVendored(fill), vTree, vOpen);
                            string where = name + ", " + op + " " + fill + (collinear ? " PreserveCollinear" : "");
                            Assert.True(pOk == vOk, where);
                            // the flat output, from a fresh run (Clipper64 can be reused: run the same once more)
                            var pFlat = new Pkg.Paths64(); var vFlat = new V.Paths64();
                            pc.Execute(op, fill, pFlat);
                            vc.Execute(g3.Clipper2.Bridge.ToVendored(op), g3.Clipper2.Bridge.ToVendored(fill), vFlat);
                            ++compared;
                            if (Text(pFlat) != Text(vFlat)) {
                                // the item 5 fix: allowed only where it corrects the package
                                AssertFixes(a, b, op, fill, pFlat, vFlat, where);
                                ++fixes;
                                continue;
                            }
                            Assert.True(Text(pTree) == Text(vTree), where);
                            Assert.True(Text(pOpen) == Text(vOpen), where);
                        }
            Assert.Equal(240 * 4 * 4 * 2, compared);
            Assert.True(fixes < compared / 100, fixes + " differences");
        }

        /// <summary> the vendored result differs from the package's: the package's area must be off the exact area and the vendored one not </summary>
        static void AssertFixes(Pkg.Paths64 a, Pkg.Paths64 b, Pkg.ClipType op, Pkg.FillRule fill, Pkg.Paths64 pOut, V.Paths64 vOut, string where)
        {
            double exact = ExactRegionArea.Area(new[] { Rings(a), Rings(b) }, Clipper2DegenerateUnionTests.Predicate(op, fill));
            var (pArea, pPerimeter) = Measure(pOut);
            var (vArea, vPerimeter) = Measure(g3.Clipper2.Bridge.ToPackage(vOut));
            where += ": exact " + exact + ", package " + pArea + ", vendored " + vArea;
            Assert.True(Math.Abs(vArea - exact) <= vPerimeter + 1, where + " (vendored off)");
            Assert.True(Math.Abs(pArea - exact) > pPerimeter + 1, where + " (differs where the package is right)");
        }

        static (long x, long y)[][] Rings(Pkg.Paths64 paths) => paths.Select(p => p.Select(q => (q.X, q.Y)).ToArray()).ToArray();

        static (double area, double perimeter) Measure(Pkg.Paths64 paths)
        {
            double area = 0, perimeter = 0;
            foreach (Pkg.Path64 p in paths) {
                area += Pkg.Clipper.Area(p);
                for (int i = 0; i < p.Count; ++i) {
                    Pkg.Point64 s = p[i], t = p[(i + 1) % p.Count];
                    perimeter += Math.Sqrt((double)(t.X - s.X) * (t.X - s.X) + (double)(t.Y - s.Y) * (t.Y - s.Y));
                }
            }
            return (area, perimeter);
        }

        [Fact]
        public void DegenerateBooleans_DifferOnlyWhereThePackageIsWrong()
        {
            // the vendored sources carry the fix of FINDINGS item 5 (Clipper2Lib/README.md): on inputs full of exact
            // degeneracies (16-gons on the lattice -4..4) they may differ from the package only where the package's area
            // is off the exact area (ExactRegionArea) by more than the grid rounding and theirs is not
            var rand = new Random(18);
            int differ = 0;
            for (int i = 0; i < 200; ++i) {
                var a = new Pkg.Paths64 { RandomRing(rand, 16, 4) };
                var b = new Pkg.Paths64 { RandomRing(rand, 16, 4) };
                foreach (Pkg.ClipType op in ClipTypes)
                    foreach (Pkg.FillRule fill in FillRules) {
                        var pc = new Pkg.Clipper64(); pc.AddSubject(a); pc.AddClip(b);
                        var vc = new V.Clipper64(); vc.AddSubject(ToVendored(a)); vc.AddClip(ToVendored(b));
                        var pOut = new Pkg.Paths64(); var vOut = new V.Paths64();
                        pc.Execute(op, fill, pOut);
                        vc.Execute(g3.Clipper2.Bridge.ToVendored(op), g3.Clipper2.Bridge.ToVendored(fill), vOut);
                        if (Text(pOut) == Text(vOut)) continue;
                        ++differ;
                        AssertFixes(a, b, op, fill, pOut, vOut, "pair " + i + ", " + op + " " + fill);
                    }
            }
            Assert.InRange(differ, 1, 200 * 16);   // the data does exercise the fix
        }

        [Fact]
        public void OpenPaths_AreIdentical()
        {
            var rand = new Random(12);
            foreach (var (a, b, bits, name) in Pairs(13, 120)) {
                var lines = new Pkg.Paths64 { RandomRing(rand, 6, 16, bits), RandomRing(rand, 3, 64, bits) };
                foreach (Pkg.ClipType op in ClipTypes)
                    foreach (Pkg.FillRule fill in FillRules) {
                        var pc = new Pkg.Clipper64();
                        pc.AddSubject(a); pc.AddOpenSubject(lines); pc.AddClip(b);
                        var vc = new V.Clipper64();
                        vc.AddSubject(ToVendored(a)); vc.AddOpenSubject(ToVendored(lines)); vc.AddClip(ToVendored(b));
                        var pTree = new Pkg.PolyTree64(); var vTree = new V.PolyTree64();
                        var pOpen = new Pkg.Paths64(); var vOpen = new V.Paths64();
                        Assert.Equal(pc.Execute(op, fill, pTree, pOpen), vc.Execute(g3.Clipper2.Bridge.ToVendored(op), g3.Clipper2.Bridge.ToVendored(fill), vTree, vOpen));
                        string where = name + ", " + op + " " + fill;
                        Assert.True(Text(pTree) == Text(vTree), where);
                        Assert.True(Text(pOpen) == Text(vOpen), where);
                    }
            }
        }

        [Fact]
        public void Offsets_AreIdentical()
        {
            var joins = new[] { Pkg.JoinType.Square, Pkg.JoinType.Bevel, Pkg.JoinType.Round, Pkg.JoinType.Miter };
            var ends = new[] { Pkg.EndType.Polygon, Pkg.EndType.Joined, Pkg.EndType.Butt, Pkg.EndType.Square, Pkg.EndType.Round };
            var rand = new Random(17);
            int c = 0;
            foreach (var (rings, _, bits, name) in Pairs(14, 42))
                foreach (Pkg.JoinType join in joins)
                    foreach (Pkg.EndType end in ends)
                        foreach (double f in new[] { -0.3, 0, 0.07, 1.4 }) {
                            if (f < 0 && end != Pkg.EndType.Polygon) continue;
                            double unit = Math.Pow(2, bits), delta = f * unit;
                            // the adapter's ranges: MiterLimit up to MaxMiterLimit, arc tolerance up to |delta| / 4
                            double miter = ++c % 2 == 0 ? 2.5 : g3.Clipper2.Clipper2Options.MaxMiterLimit;
                            double arc = c % 3 == 0 ? Math.Abs(delta) / 4 : c % 3 == 1 ? unit * 1e-3 : 0;
                            var a = new Pkg.Paths64(rings);
                            if (end == Pkg.EndType.Round && c % 5 == 0)
                                a.Add(new Pkg.Path64 { rings[0][0] });   // the single-point polyline of OffsetPolyLines
                            var po = new Pkg.ClipperOffset(miter, arc);
                            po.AddPaths(a, join, end);
                            var vo = new V.ClipperOffset(miter, arc);
                            vo.AddPaths(ToVendored(a), g3.Clipper2.Bridge.ToVendored(join), g3.Clipper2.Bridge.ToVendored(end));
                            // degenerate paths can make both throw (an empty open path): the same exception is the same outcome
                            string where = name + ", " + join + " " + end + " " + delta + " miter " + miter + " arc " + arc;
                            Assert.True(Outcome(() => { var o = new Pkg.Paths64(); po.Execute(delta, o); return Text(o); })
                                        == Outcome(() => { var o = new V.Paths64(); vo.Execute(delta, o); return Text(o); }), where);
                            Assert.True(Outcome(() => { var t = new Pkg.PolyTree64(); po.Execute(delta, t); return Text(t); })
                                        == Outcome(() => { var t = new V.PolyTree64(); vo.Execute(delta, t); return Text(t); }), where + " (tree)");
                        }
        }

        [Fact]
        public void MinkowskiSimplifyAndQueries_AreIdentical()
        {
            foreach (var (a, b, _, name) in Pairs(15, 50)) {
                Pkg.Path64 pa = a[0], pb = b[0];
                V.Path64 va = g3.Clipper2.Bridge.ToVendored(pa), vb = g3.Clipper2.Bridge.ToVendored(pb);
                foreach (bool closed in new[] { false, true }) {
                    Assert.True(Text(Pkg.Minkowski.Sum(pa, pb, closed)) == Text(V.Minkowski.Sum(va, vb, closed)), name + " sum " + closed);
                    Assert.True(Text(Pkg.Minkowski.Diff(pa, pb, closed)) == Text(V.Minkowski.Diff(va, vb, closed)), name + " diff " + closed);
                    foreach (double eps in new[] { 0.0, 3e4, 4e5 })
                        Assert.True(Text(Pkg.Clipper.SimplifyPaths(a, eps, closed)) == Text(V.Clipper.SimplifyPaths(ToVendored(a), eps, closed)), name + " simplify " + eps + " " + closed);
                }
                Assert.Equal(Pkg.Clipper.Area(pa), V.Clipper.Area(va));
                Pkg.Rect64 pr = Pkg.Clipper.GetBounds(pb);
                V.Rect64 vr = V.Clipper.GetBounds(vb);
                Assert.Equal((pr.left, pr.top, pr.right, pr.bottom), (vr.left, vr.top, vr.right, vr.bottom));
                foreach (Pkg.Point64 q in pb)
                    Assert.Equal((int)Pkg.Clipper.PointInPolygon(q, pa), (int)V.Clipper.PointInPolygon(g3.Clipper2.Bridge.ToVendored(q), va));
            }
            // point in polygon on a triangle and on 1- and 2-point paths (the length guard), at a vertex, on an edge,
            // inside and outside
            var rand = new Random(19);
            for (int i = 0; i < 40; ++i) {
                Pkg.Path64 ring = RandomRing(rand, 3, 8);
                if (Pkg.Clipper.Area(ring) == 0) continue;
                var probes = new List<Pkg.Point64> { ring[0], ring[1], ring[2],
                    new Pkg.Point64((ring[0].X + ring[1].X) / 2, (ring[0].Y + ring[1].Y) / 2),   // on an edge (lattice step 2^17: exact)
                    new Pkg.Point64((ring[0].X + ring[1].X + ring[2].X) / 3, (ring[0].Y + ring[1].Y + ring[2].Y) / 3),
                    new Pkg.Point64(1L << 22, 1L << 22) };
                foreach (Pkg.Path64 path in new[] { ring, new Pkg.Path64 { ring[0], ring[1] }, new Pkg.Path64 { ring[0] } })
                    foreach (Pkg.Point64 q in probes)
                        Assert.Equal((int)Pkg.Clipper.PointInPolygon(q, path), (int)V.Clipper.PointInPolygon(g3.Clipper2.Bridge.ToVendored(q), g3.Clipper2.Bridge.ToVendored(path)));
                // a 3-vertex path is a polygon (not cut off by the length guard): a vertex is on it
                Assert.Equal(Pkg.PointInPolygonResult.IsOn, Pkg.Clipper.PointInPolygon(ring[0], ring));
            }
        }

        [Fact]
        public void Bridge_CopiesTreesAndEnums()
        {
            // the conversions the adapter's public ClipperFrame methods use
            foreach (var (a, b, _, _) in Pairs(16, 30)) {
                var pc = new Pkg.Clipper64();
                pc.AddSubject(a); pc.AddClip(b);
                var tree = new Pkg.PolyTree64();
                pc.Execute(Pkg.ClipType.Xor, Pkg.FillRule.EvenOdd, tree);
                Assert.Equal(Text(tree), Text(g3.Clipper2.Bridge.ToVendored(tree)));
                Assert.Equal(Text(a), Text(g3.Clipper2.Bridge.ToPackage(ToVendored(a))));
            }
            foreach (Pkg.ClipType op in Enum.GetValues(typeof(Pkg.ClipType)))
                Assert.Equal(op.ToString(), g3.Clipper2.Bridge.ToVendored(op).ToString());
            foreach (Pkg.FillRule f in Enum.GetValues(typeof(Pkg.FillRule)))
                Assert.Equal(f.ToString(), g3.Clipper2.Bridge.ToVendored(f).ToString());
            foreach (Pkg.JoinType j in Enum.GetValues(typeof(Pkg.JoinType)))
                Assert.Equal(j.ToString(), g3.Clipper2.Bridge.ToVendored(j).ToString());
            foreach (Pkg.EndType e in Enum.GetValues(typeof(Pkg.EndType)))
                Assert.Equal(e.ToString(), g3.Clipper2.Bridge.ToVendored(e).ToString());
            Assert.Equal(Enum.GetValues(typeof(Pkg.ClipType)).Length, Enum.GetValues(typeof(V.ClipType)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.FillRule)).Length, Enum.GetValues(typeof(V.FillRule)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.JoinType)).Length, Enum.GetValues(typeof(V.JoinType)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.EndType)).Length, Enum.GetValues(typeof(V.EndType)).Length);
        }
    }
}
