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
    /// Since the vendored sources carry fixes (Clipper2Lib/README.md: FINDINGS item 5, #756 and, since the port of the
    /// PolyClipping/Clipper2 rounding of intersection points, f774daf, most outputs), the contract is (owner 2026-11-15):
    /// a boolean may differ only within the rounding (as below) or where the vendored area stays within the grid rounding of
    /// the exact area (ExactRegionArea; AssertVendoredExact); offsets, Minkowski sums and the open paths of a clip, which have no exact oracle here, may
    /// differ only within the rounding, ie by at most two grid units (Hausdorff distance of the paths) with an area within
    /// the perimeter; everything without intersection points (SimplifyPaths, Area, bounds, point in polygon) stays identical.
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
                result.Add(PackageBridge.ToVendored(p));
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

        [Theory]
        [InlineData(0)]
        [InlineData(120)]
        public void Booleans_DifferOnlyWhereTheVendoredStaysExact(int from)
        {
            // the 240 pairs of seed 11 in two halves (each half within the test time)
            int compared = 0, differ = 0;
            foreach (var (a, b, _, name) in Pairs(11, 240).Skip(from).Take(120))
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
                            bool vOk = vc.Execute(PackageBridge.ToVendored(op), PackageBridge.ToVendored(fill), vTree, vOpen);
                            string where = name + ", " + op + " " + fill + (collinear ? " PreserveCollinear" : "");
                            Assert.True(pOk == vOk, where);
                            // the flat output, from a fresh run (Clipper64 can be reused: run the same once more)
                            var pFlat = new Pkg.Paths64(); var vFlat = new V.Paths64();
                            pc.Execute(op, fill, pFlat);
                            vc.Execute(PackageBridge.ToVendored(op), PackageBridge.ToVendored(fill), vFlat);
                            ++compared;
                            if (Text(pFlat) != Text(vFlat)) {
                                AssertVendoredExact(a, b, op, fill, pFlat, vFlat, where);
                                ++differ;
                                // the tree: within the rounding of the package's or exact, its area that of the flat output (within the
                                // rounding: the two outputs take different cleanup paths)
                                AssertVendoredExact(a, b, op, fill, Flatten(pTree), FlattenV(vTree), where + " (tree)");
                                var (flatArea, flatPerimeter) = Measure(PackageBridge.ToPackage(vFlat));
                                Assert.True(Math.Abs(vTree.Area() - flatArea) <= flatPerimeter + 1, where + " (tree area " + vTree.Area() + " against " + flatArea + ")");
                                // the nesting, as the adapter reads it: ToGeneralPolygons of the vendored tree (on a frame that
                                // maps the grid to itself) has the area of the flat output, within the rounding, and covers the
                                // same points, each by one polygon (a tree can nest a touching sliver differently from the
                                // package's; the adapter's rules sort that out, and a hole under the wrong outer ring shows here;
                                // a tree that nests nothing at all passes, since the adapter re-nests its negative paths)
                                var polygons = new g3.Clipper2.ClipperFrame(new g3.Vector2d(0, 0), 1).ToGeneralPolygons(vTree);
                                double converted = polygons.Sum(g => g.Outer.SignedArea + g.Holes.Sum(h => h.SignedArea));
                                Assert.True(Math.Abs(converted - flatArea) <= flatPerimeter + 1, where + " (converted tree " + converted + " against " + flatArea + ")");
                                AssertSameRegion(vFlat, polygons, new Random(compared), where + " (converted tree)");
                                if (Text(pOpen) != Text(vOpen))
                                    AssertWithinRounding(pOpen, PackageBridge.ToPackage(vOpen), false, where + " (open)");
                                continue;
                            }
                            Assert.True(Text(pTree) == Text(vTree), where);
                            Assert.True(Text(pOpen) == Text(vOpen), where);
                        }
            Assert.Equal(120 * 4 * 4 * 2, compared);
            Assert.True(differ > 0, "the data does not exercise the vendored fixes");
        }

        /// <summary>
        /// the vendored result differs from the package's: its area must be within the grid rounding of the exact area;
        /// returns whether the package's was not
        /// </summary>
        static bool AssertVendoredExact(Pkg.Paths64 a, Pkg.Paths64 b, Pkg.ClipType op, Pkg.FillRule fill, Pkg.Paths64 pOut, V.Paths64 vOut, string where)
        {
            // a difference within the rounding (paths at most two grid units apart, areas within the perimeter) needs no
            // oracle: it changes no result by more than the rounding; only a larger one must be exact
            {
                var (pa, pp) = Measure(pOut);
                var (va, _) = Measure(PackageBridge.ToPackage(vOut));
                if (Math.Abs(pa - va) <= pp + 1 && Hausdorff(pOut, PackageBridge.ToPackage(vOut), true) <= 2) return false;
            }
            double exact = ExactRegionArea.Area(new[] { Rings(a), Rings(b) }, Clipper2DegenerateUnionTests.Predicate(PackageBridge.ToAddon(op), PackageBridge.ToAddon(fill)));
            var (pArea, pPerimeter) = Measure(pOut);
            var (vArea, vPerimeter) = Measure(PackageBridge.ToPackage(vOut));
            where += ": exact " + exact + ", package " + pArea + ", vendored " + vArea;
            Assert.True(Math.Abs(vArea - exact) <= vPerimeter + 1, where + " (vendored off)");
            return Math.Abs(pArea - exact) > pPerimeter + 1;
        }

        /// <summary>
        /// the largest distance from a vertex of one path set to the other set's edges, both ways (paths closed or open), in
        /// doubles: at the 2^50 grid the projection loses up to about half a grid unit, so the bound of two units used below
        /// is about 2.5 there; it sees vertices only, the area check covers the inside
        /// </summary>
        static double Hausdorff(Pkg.Paths64 p, Pkg.Paths64 q, bool closed)
        {
            double OneWay(Pkg.Paths64 from, Pkg.Paths64 to)
            {
                double worst = 0;
                foreach (Pkg.Path64 path in from)
                    foreach (Pkg.Point64 v in path) {
                        double best = double.PositiveInfinity;
                        foreach (Pkg.Path64 other in to) {
                            int n = other.Count;
                            if (n == 1) best = Math.Min(best, Math.Sqrt((double)(v.X - other[0].X) * (v.X - other[0].X) + (double)(v.Y - other[0].Y) * (v.Y - other[0].Y)));
                            for (int i = 0; i + 1 < n || (closed && n > 2 && i < n); ++i) {
                                Pkg.Point64 s = other[i], t = other[(i + 1) % n];
                                double dx = (double)t.X - s.X, dy = (double)t.Y - s.Y, px = (double)v.X - s.X, py = (double)v.Y - s.Y;
                                double l2 = dx * dx + dy * dy, u = l2 == 0 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / l2));
                                best = Math.Min(best, Math.Sqrt((px - u * dx) * (px - u * dx) + (py - u * dy) * (py - u * dy)));
                            }
                        }
                        worst = Math.Max(worst, best);
                    }
                return worst;
            }
            if (p.Count == 0 || q.Count == 0) return p.Count == q.Count ? 0 : double.PositiveInfinity;
            return Math.Max(OneWay(p, q), OneWay(q, p));
        }

        /// <summary> the vendored paths differ from the package's only within the rounding: two grid units and the area within the perimeter </summary>
        static void AssertWithinRounding(Pkg.Paths64 pOut, Pkg.Paths64 vOut, bool closed, string where)
        {
            double h = Hausdorff(pOut, vOut, closed);
            Assert.True(h <= 2, where + ": paths " + h + " grid units apart");
            if (!closed) return;
            var (pArea, pPerimeter) = Measure(pOut);
            var (vArea, _) = Measure(vOut);
            Assert.True(Math.Abs(pArea - vArea) <= pPerimeter + 1, where + ": area " + vArea + " against " + pArea);
        }

        /// <summary>
        /// At random points of the flat output's bounds: a point is in the region of the flat paths (non-zero winding) exactly
        /// if one polygon contains it (its outer ring and none of its holes); points within two grid units of an edge are
        /// skipped
        /// </summary>
        static void AssertSameRegion(V.Paths64 flat, List<g3.GeneralPolygon2d> polygons, Random rand, string where)
        {
            var rings = flat.Where(p => p.Count > 0).Select(p => (IReadOnlyList<g3.Vector2d>)p.Select(q => new g3.Vector2d(q.X, q.Y)).ToList()).ToList();
            if (rings.Count == 0) { Assert.Empty(polygons); return; }
            double x0 = rings.Min(r => r.Min(v => v.x)), x1 = rings.Max(r => r.Max(v => v.x));
            double y0 = rings.Min(r => r.Min(v => v.y)), y1 = rings.Max(r => r.Max(v => v.y));
            for (int i = 0; i < 24; ++i) {
                var p = new g3.Vector2d(x0 + rand.NextDouble() * (x1 - x0), y0 + rand.NextDouble() * (y1 - y0));
                if (rings.Any(r => Oracle.RingDistance(r, p) <= 2) || Oracle.BoundaryDistance(polygons, p) <= 2) continue;
                bool inFlat = rings.Sum(r => Oracle.Winding(r, p)) != 0;
                int count = Oracle.ResultCount(polygons, p);
                Assert.True(count == (inFlat ? 1 : 0), where + ": point " + p + " in the flat region " + inFlat + ", in " + count + " polygons");
            }
        }

        static V.Paths64 FlattenV(V.PolyPath64 node)
        {
            var result = new V.Paths64();
            void Walk(V.PolyPath64 n) { for (int i = 0; i < n.Count; ++i) { result.Add(n.Child(i).Polygon); Walk(n.Child(i)); } }
            Walk(node);
            return result;
        }

        static string PackageText(Pkg.Paths64 paths) => Text(paths);

        static Pkg.Paths64 Flatten(Pkg.PolyPath64 node)
        {
            var result = new Pkg.Paths64();
            void Walk(Pkg.PolyPath64 n) { for (int i = 0; i < n.Count; ++i) { result.Add(n.Child(i).Polygon); Walk(n.Child(i)); } }
            Walk(node);
            return result;
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
        public void DegenerateBooleans_DifferOnlyWhereTheVendoredStaysExact()
        {
            // on inputs full of exact degeneracies (16-gons on the lattice -4..4) the vendored sources may differ from the
            // package only where their area stays within the grid rounding of the exact area (ExactRegionArea); some of these
            // results are wrong in the package (FINDINGS item 5)
            var rand = new Random(18);
            int differ = 0, packageWrong = 0;
            for (int i = 0; i < 200; ++i) {
                var a = new Pkg.Paths64 { RandomRing(rand, 16, 4) };
                var b = new Pkg.Paths64 { RandomRing(rand, 16, 4) };
                foreach (Pkg.ClipType op in ClipTypes)
                    foreach (Pkg.FillRule fill in FillRules) {
                        var pc = new Pkg.Clipper64(); pc.AddSubject(a); pc.AddClip(b);
                        var vc = new V.Clipper64(); vc.AddSubject(ToVendored(a)); vc.AddClip(ToVendored(b));
                        var pOut = new Pkg.Paths64(); var vOut = new V.Paths64();
                        pc.Execute(op, fill, pOut);
                        vc.Execute(PackageBridge.ToVendored(op), PackageBridge.ToVendored(fill), vOut);
                        if (Text(pOut) == Text(vOut)) continue;
                        ++differ;
                        if (AssertVendoredExact(a, b, op, fill, pOut, vOut, "pair " + i + ", " + op + " " + fill)) ++packageWrong;
                    }
            }
            Assert.True(packageWrong > 0, "the data does not exercise the fixes");   // differ counts the rounding too
        }

        [Fact]
        public void OpenPaths_DifferOnlyWithinTheRounding()
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
                        Assert.Equal(pc.Execute(op, fill, pTree, pOpen), vc.Execute(PackageBridge.ToVendored(op), PackageBridge.ToVendored(fill), vTree, vOpen));
                        string where = name + ", " + op + " " + fill;
                        if (Text(pTree) != Text(vTree))
                            AssertVendoredExact(a, b, op, fill, Flatten(pTree), FlattenV(vTree), where);
                        if (Text(pOpen) != Text(vOpen))
                            AssertWithinRounding(pOpen, PackageBridge.ToPackage(vOpen), false, where + " (open)");
                    }
            }
        }

        [Fact]
        public void Offsets_DifferOnlyWithinTheRounding()
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
                            vo.AddPaths(ToVendored(a), PackageBridge.ToVendored(join), PackageBridge.ToVendored(end));
                            // degenerate paths can make both throw (an empty open path): the same exception is the same outcome
                            string where = name + ", " + join + " " + end + " " + delta + " miter " + miter + " arc " + arc;
                            Pkg.Paths64 pFlat = null, vFlat = null, pTree = null, vTree = null;
                            string pOutcome = Outcome(() => { pFlat = new Pkg.Paths64(); po.Execute(delta, pFlat); return ""; });
                            string vOutcome = Outcome(() => { var o = new V.Paths64(); vo.Execute(delta, o); vFlat = PackageBridge.ToPackage(o); return ""; });
                            Assert.True(pOutcome == vOutcome, where + ": " + pOutcome + " against " + vOutcome);
                            if (pFlat != null && vFlat != null && Text(pFlat) != PackageText(vFlat))
                                AssertWithinRounding(pFlat, vFlat, true, where);
                            pOutcome = Outcome(() => { var t = new Pkg.PolyTree64(); po.Execute(delta, t); pTree = Flatten(t); return ""; });
                            vOutcome = Outcome(() => { var t = new V.PolyTree64(); vo.Execute(delta, t); vTree = PackageBridge.ToPackage(FlattenV(t)); return ""; });
                            Assert.True(pOutcome == vOutcome, where + " (tree): " + pOutcome + " against " + vOutcome);
                            if (pTree != null && vTree != null && Text(pTree) != PackageText(vTree))
                                AssertWithinRounding(pTree, vTree, true, where + " (tree)");
                        }
        }

        [Fact]
        public void MinkowskiWithinTheRounding_SimplifyAndQueriesIdentical()
        {
            foreach (var (a, b, _, name) in Pairs(15, 50)) {
                Pkg.Path64 pa = a[0], pb = b[0];
                V.Path64 va = PackageBridge.ToVendored(pa), vb = PackageBridge.ToVendored(pb);
                foreach (bool closed in new[] { false, true }) {
                    AssertWithinRounding(Pkg.Minkowski.Sum(pa, pb, closed), PackageBridge.ToPackage(V.Minkowski.Sum(va, vb, closed)), true, name + " sum " + closed);
                    AssertWithinRounding(Pkg.Minkowski.Diff(pa, pb, closed), PackageBridge.ToPackage(V.Minkowski.Diff(va, vb, closed)), true, name + " diff " + closed);
                    foreach (double eps in new[] { 0.0, 3e4, 4e5 })
                        Assert.True(Text(Pkg.Clipper.SimplifyPaths(a, eps, closed)) == Text(V.Clipper.SimplifyPaths(ToVendored(a), eps, closed)), name + " simplify " + eps + " " + closed);
                }
                Assert.Equal(Pkg.Clipper.Area(pa), V.Clipper.Area(va));
                Pkg.Rect64 pr = Pkg.Clipper.GetBounds(pb);
                V.Rect64 vr = V.Clipper.GetBounds(vb);
                Assert.Equal((pr.left, pr.top, pr.right, pr.bottom), (vr.left, vr.top, vr.right, vr.bottom));
                foreach (Pkg.Point64 q in pb)
                    Assert.Equal((int)Pkg.Clipper.PointInPolygon(q, pa), (int)V.Clipper.PointInPolygon(PackageBridge.ToVendored(q), va));
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
                        Assert.Equal((int)Pkg.Clipper.PointInPolygon(q, path), (int)V.Clipper.PointInPolygon(PackageBridge.ToVendored(q), PackageBridge.ToVendored(path)));
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
                Assert.Equal(Text(tree), Text(PackageBridge.ToVendored(tree)));
                Assert.Equal(Text(a), Text(PackageBridge.ToPackage(ToVendored(a))));
            }
            foreach (Pkg.ClipType op in Enum.GetValues(typeof(Pkg.ClipType)))
                Assert.Equal(op.ToString(), PackageBridge.ToVendored(op).ToString());
            foreach (Pkg.FillRule f in Enum.GetValues(typeof(Pkg.FillRule)))
                Assert.Equal(f.ToString(), PackageBridge.ToVendored(f).ToString());
            foreach (Pkg.JoinType j in Enum.GetValues(typeof(Pkg.JoinType)))
                Assert.Equal(j.ToString(), PackageBridge.ToVendored(j).ToString());
            foreach (Pkg.EndType e in Enum.GetValues(typeof(Pkg.EndType)))
                Assert.Equal(e.ToString(), PackageBridge.ToVendored(e).ToString());
            // the addon's public enums (its own since 2.0): the same names and values as the package's and the vendored ones
            foreach ((Type addon, Type package, Type vendored) in new[] {
                    (typeof(g3.Clipper2.ClipType), typeof(Pkg.ClipType), typeof(V.ClipType)),
                    (typeof(g3.Clipper2.FillRule), typeof(Pkg.FillRule), typeof(V.FillRule)),
                    (typeof(g3.Clipper2.JoinType), typeof(Pkg.JoinType), typeof(V.JoinType)),
                    (typeof(g3.Clipper2.EndType), typeof(Pkg.EndType), typeof(V.EndType)) }) {
                Assert.Equal(Enum.GetNames(package), Enum.GetNames(addon));
                Assert.Equal(Enum.GetNames(vendored), Enum.GetNames(addon));
                foreach (string member in Enum.GetNames(addon)) {
                    Assert.Equal(Convert.ToInt32(Enum.Parse(package, member)), Convert.ToInt32(Enum.Parse(addon, member)));
                    Assert.Equal(Convert.ToInt32(Enum.Parse(vendored, member)), Convert.ToInt32(Enum.Parse(addon, member)));
                }
            }
            foreach (g3.Clipper2.JoinType j in Enum.GetValues(typeof(g3.Clipper2.JoinType)))
                Assert.Equal(j.ToString(), g3.Clipper2.Bridge.ToVendored(j).ToString());
            Assert.Equal(Enum.GetValues(typeof(Pkg.ClipType)).Length, Enum.GetValues(typeof(V.ClipType)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.FillRule)).Length, Enum.GetValues(typeof(V.FillRule)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.JoinType)).Length, Enum.GetValues(typeof(V.JoinType)).Length);
            Assert.Equal(Enum.GetValues(typeof(Pkg.EndType)).Length, Enum.GetValues(typeof(V.EndType)).Length);
        }
    }
}
