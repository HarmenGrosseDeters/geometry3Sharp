using System;
using System.Collections.Generic;
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
    /// and open polylines.
    /// </summary>
    public class Clipper2VendoredDifferentialTests
    {
        static readonly Pkg.ClipType[] ClipTypes = { Pkg.ClipType.Union, Pkg.ClipType.Intersection, Pkg.ClipType.Difference, Pkg.ClipType.Xor };
        static readonly Pkg.FillRule[] FillRules = { Pkg.FillRule.EvenOdd, Pkg.FillRule.NonZero, Pkg.FillRule.Positive, Pkg.FillRule.Negative };

        static Pkg.Path64 RandomRing(Random rand, int n, long range)
        {
            long step = (1L << 20) / range;
            var path = new Pkg.Path64(n);
            for (int i = 0; i < n; ++i)
                path.Add(new Pkg.Point64(rand.Next(-(int)range, (int)range + 1) * step, rand.Next(-(int)range, (int)range + 1) * step));
            return path;
        }

        /// <summary> pairs of random rings: n-gons with coordinates on a lattice of 2 range + 1 values per axis </summary>
        static IEnumerable<(Pkg.Paths64 a, Pkg.Paths64 b, string name)> Pairs(int seed, int count)
        {
            var rand = new Random(seed);
            for (int i = 0; i < count; ++i) {
                int n = i % 3 == 0 ? 4 : i % 3 == 1 ? 8 : 16;
                long range = i % 2 == 0 ? 4 : 64;       // coarse: exact degeneracies; finer: fewer of them
                if (i % 5 == 4) range = 1 << 15;         // generic positions
                var a = new Pkg.Paths64 { RandomRing(rand, n, range) };
                var b = new Pkg.Paths64 { RandomRing(rand, n, range) };
                if (i % 4 == 3) a.Add(RandomRing(rand, 5, range));   // more than one ring per operand
                yield return (a, b, "seed " + seed + " case " + i + " (" + n + "-gons, range " + range + ")");
            }
        }

        static V.Paths64 ToVendored(Pkg.Paths64 paths)
        {
            var result = new V.Paths64(paths.Count);
            foreach (Pkg.Path64 p in paths)
                result.Add(g3.Clipper2.Bridge.ToVendored(p));
            return result;
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
            int compared = 0;
            foreach (var (a, b, name) in Pairs(11, 240))
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
                            Assert.True(Text(pTree) == Text(vTree), where);
                            Assert.True(Text(pOpen) == Text(vOpen), where);
                            // the flat output, from a fresh run (Clipper64 can be reused: run the same once more)
                            var pFlat = new Pkg.Paths64(); var vFlat = new V.Paths64();
                            pc.Execute(op, fill, pFlat);
                            vc.Execute(g3.Clipper2.Bridge.ToVendored(op), g3.Clipper2.Bridge.ToVendored(fill), vFlat);
                            Assert.True(Text(pFlat) == Text(vFlat), where + " (flat)");
                            ++compared;
                        }
            Assert.Equal(240 * 4 * 4 * 2, compared);
        }

        [Fact]
        public void OpenPaths_AreIdentical()
        {
            var rand = new Random(12);
            foreach (var (a, b, name) in Pairs(13, 120)) {
                var lines = new Pkg.Paths64 { RandomRing(rand, 6, 16), RandomRing(rand, 3, 64) };
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
            foreach (var (a, _, name) in Pairs(14, 40))
                foreach (Pkg.JoinType join in joins)
                    foreach (Pkg.EndType end in ends)
                        foreach (double delta in new[] { -3e5, 7e4, 1.5e6 }) {
                            if (delta < 0 && end != Pkg.EndType.Polygon) continue;
                            var po = new Pkg.ClipperOffset(2.5, 1e3);
                            po.AddPaths(a, join, end);
                            var vo = new V.ClipperOffset(2.5, 1e3);
                            vo.AddPaths(ToVendored(a), g3.Clipper2.Bridge.ToVendored(join), g3.Clipper2.Bridge.ToVendored(end));
                            var pOut = new Pkg.Paths64(); var vOut = new V.Paths64();
                            po.Execute(delta, pOut);
                            vo.Execute(delta, vOut);
                            Assert.True(Text(pOut) == Text(vOut), name + ", " + join + " " + end + " " + delta);
                            var pTree = new Pkg.PolyTree64(); var vTree = new V.PolyTree64();
                            po.Execute(delta, pTree);
                            vo.Execute(delta, vTree);
                            Assert.True(Text(pTree) == Text(vTree), name + ", " + join + " " + end + " " + delta + " (tree)");
                        }
        }

        [Fact]
        public void MinkowskiSimplifyAndQueries_AreIdentical()
        {
            foreach (var (a, b, name) in Pairs(15, 50)) {
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
        }

        [Fact]
        public void Bridge_CopiesTreesAndEnums()
        {
            // the conversions the adapter's public ClipperFrame methods use
            foreach (var (a, b, _) in Pairs(16, 30)) {
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
