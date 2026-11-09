using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The oracle must prove itself before it judges anything: its arithmetic against hand values, valid results accepted, and
    /// every kind of deliberately broken result rejected with the kind of violation that fits. Every entry point runs this first.
    /// </summary>
    public static class SelfCheck
    {
        public sealed class Item { public string Name; public bool Passed; public string Detail; }

        static Mesh2Input Points(params double[] xy)
        {
            var input = new Mesh2Input { X = new double[xy.Length / 2], Y = new double[xy.Length / 2] };
            for (int i = 0; i < input.Count; ++i) { input.X[i] = xy[2 * i]; input.Y[i] = xy[2 * i + 1]; }
            return input;
        }

        /// <summary>
        /// The Delaunay triangulation by its definition: every triple with an empty circumcircle (O(n^4)). Only for small inputs in
        /// general position, for which it is the unique answer; shares nothing with any mesher.
        /// </summary>
        public static int[][] DelaunayByDefinition(Mesh2Input input)
        {
            var p = Enumerable.Range(0, input.Count).Select(i => Pt.FromDoubles(input.X[i], input.Y[i])).ToArray();
            var result = new List<int[]>();
            for (int a = 0; a < p.Length; ++a)
                for (int b = a + 1; b < p.Length; ++b)
                    for (int c = b + 1; c < p.Length; ++c) {
                        int side = Exact.Orient(p[a], p[b], p[c]);
                        if (side == 0) continue;
                        int[] t = side > 0 ? new[] { a, b, c } : new[] { a, c, b };
                        bool empty = true;
                        for (int d = 0; d < p.Length && empty; ++d)
                            if (d != a && d != b && d != c && Exact.InCircle(p[t[0]], p[t[1]], p[t[2]], p[d]) > 0) empty = false;
                        if (empty) result.Add(t);
                    }
            return result.ToArray();
        }

        static Mesh2Output M1Output(Mesh2Input input)
        {
            var o = new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(),
                InputToVertex = Enumerable.Range(0, input.Count).ToArray(), Triangles = DelaunayByDefinition(input)
            };
            o.Neighbours = NeighboursOf(o.Triangles);
            return o;
        }

        public static int[][] NeighboursOf(int[][] triangles)
        {
            var at = new Dictionary<(int, int), int>();
            for (int i = 0; i < triangles.Length; ++i)
                for (int k = 0; k < 3; ++k) at[(triangles[i][(k + 1) % 3], triangles[i][(k + 2) % 3])] = i;
            return triangles.Select(t => Enumerable.Range(0, 3).Select(k =>
                at.TryGetValue((t[(k + 2) % 3], t[(k + 1) % 3]), out int n) ? n : -1).ToArray()).ToArray();
        }

        static Mesh2Output Clone(Mesh2Output o) => new Mesh2Output {
            Status = o.Status, FailureKind = o.FailureKind, VX = (double[])o.VX.Clone(), VY = (double[])o.VY.Clone(),
            ExactVertices = o.ExactVertices == null ? null : (Pt[])o.ExactVertices.Clone(),
            Triangles = o.Triangles.Select(t => (int[])t.Clone()).ToArray(),
            Neighbours = o.Neighbours?.Select(t => (int[])t.Clone()).ToArray(),
            InputToVertex = (int[])o.InputToVertex?.Clone(),
            Subsegments = o.Subsegments?.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList(),
            TriangleLabels = (int[])o.TriangleLabels?.Clone(), BadTriangles = (int[])o.BadTriangles?.Clone(),
            SteinerLimitReached = o.SteinerLimitReached, IdentityRules = o.IdentityRules, Locate = o.Locate
        };

        /// <summary>Replaces the first interior edge of a convex quad by the other diagonal; the result is a valid triangulation.</summary>
        static (int[][] triangles, int a, int b) FlipSomeEdge(Mesh2Input input, int[][] triangles)
        {
            var p = Enumerable.Range(0, input.Count).Select(i => Pt.FromDoubles(input.X[i], input.Y[i])).ToArray();
            for (int i = 0; i < triangles.Length; ++i)
                for (int k = 0; k < 3; ++k) {
                    int a = triangles[i][(k + 1) % 3], b = triangles[i][(k + 2) % 3], c = triangles[i][k];
                    for (int j = i + 1; j < triangles.Length; ++j)
                        for (int m = 0; m < 3; ++m) {
                            if (triangles[j][(m + 1) % 3] != b || triangles[j][(m + 2) % 3] != a) continue;
                            int d = triangles[j][m];
                            if (Exact.Orient(p[c], p[d], p[b]) <= 0 || Exact.Orient(p[d], p[c], p[a]) <= 0) continue;
                            var copy = triangles.Select(t => (int[])t.Clone()).ToArray();
                            copy[i] = new[] { c, d, b };
                            copy[j] = new[] { d, c, a };
                            return (copy, c, d);
                        }
                }
            throw new InvalidOperationException("no flippable edge");
        }

        static OracleExpect M1() => new OracleExpect { Milestone = Milestone.M1 };
        static OracleExpect M2(Q? tube = null) => new OracleExpect { Milestone = Milestone.M2, Tube = tube ?? Q.Zero };
        static OracleExpect M3(Q? tube = null) => new OracleExpect { Milestone = Milestone.M3, Tube = tube ?? Q.Zero };

        // the grid baseline of M2 and M3: [0,8]^2 of unit cells, each cut by one diagonal, with a hole [3,5]^2, segments on y = 1
        // and y = 6, a ring region [0,2]x[6,8] and a seed region [2,8]x[6,8]. All angles are 45 or 90 degrees, all free edges ties.
        static (Mesh2Input input, Mesh2Output output) Grid(bool steiner)
        {
            var points = new List<(int x, int y)>();
            int Add(int x, int y) { points.Add((x, y)); return points.Count - 1; }
            int c00 = Add(0, 0), c80 = Add(8, 0), c88 = Add(8, 8), c08 = Add(0, 8);
            int h33 = Add(3, 3), h53 = Add(5, 3), h55 = Add(5, 5), h35 = Add(3, 5);
            int s1 = Add(1, 1), s2 = Add(7, 1), s3 = Add(0, 6), s4 = Add(8, 6), r1 = Add(2, 6), r2 = Add(2, 8);
            int inputCount = points.Count;
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { c00, c80, c88, c08, c00 }, false, 1));
            input.Rings.Add(new RingInput(new[] { h33, h53, h55, h35 }, true, 2));
            input.Segments.Add(new SegmentInput(s1, s2, 5));
            input.Segments.Add(new SegmentInput(s3, s4, 6));
            input.Regions.Add(new RegionInput { Ring = new[] { s3, r1, r2, c08 }, Label = 3 });
            input.Regions.Add(new RegionInput { Seed = new[] { 5.25, 7.25 }, Label = 4 });
            var id = new Dictionary<(int, int), int>();
            for (int i = 0; i < points.Count; ++i) id[points[i]] = i;
            for (int x = 0; x <= 8; ++x)
                for (int y = 0; y <= 8; ++y) {
                    if (id.ContainsKey((x, y))) continue;
                    if (x > 3 && x < 5 && y > 3 && y < 5) continue;
                    id[(x, y)] = points.Count; points.Add((x, y));
                }
            if (!steiner) {
                // every grid point is an input point, in the same slots
                inputCount = points.Count;
            }
            input.X = points.Take(inputCount).Select(p => (double)p.x).ToArray();
            input.Y = points.Take(inputCount).Select(p => (double)p.y).ToArray();
            var triangles = new List<int[]>(); var labels = new List<int>();
            for (int x = 0; x < 8; ++x)
                for (int y = 0; y < 8; ++y) {
                    if (x >= 3 && x < 5 && y >= 3 && y < 5) continue;
                    int a = id[(x, y)], b = id[(x + 1, y)], c = id[(x + 1, y + 1)], d = id[(x, y + 1)];
                    triangles.Add(new[] { a, b, c }); triangles.Add(new[] { a, c, d });
                    int label = y < 6 ? 0 : x < 2 ? 3 : 4;
                    labels.Add(label); labels.Add(label);
                }
            var subs = new List<SubsegmentOutput>();
            void Chain(int x0, int y0, int x1, int y1, int marker)
            {
                int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
                for (int i = 0; i < n; ++i) subs.Add(new SubsegmentOutput(id[(x0 + i * dx, y0 + i * dy)], id[(x0 + (i + 1) * dx, y0 + (i + 1) * dy)], marker));
            }
            Chain(0, 0, 8, 0, 1); Chain(8, 0, 8, 6, 1); Chain(8, 6, 8, 8, 1); Chain(8, 8, 2, 8, 1); Chain(2, 8, 0, 8, 1); Chain(0, 8, 0, 6, 1); Chain(0, 6, 0, 0, 1);
            Chain(3, 3, 5, 3, 2); Chain(5, 3, 5, 5, 2); Chain(5, 5, 3, 5, 2); Chain(3, 5, 3, 3, 2);
            Chain(1, 1, 7, 1, 5);
            Chain(0, 6, 2, 6, 6); Chain(2, 6, 8, 6, 6);
            Chain(2, 6, 2, 8, 0);
            var output = new Mesh2Output {
                Status = Mesh2Status.Success,
                VX = points.Select(p => (double)p.x).ToArray(), VY = points.Select(p => (double)p.y).ToArray(),
                InputToVertex = Enumerable.Range(0, inputCount).ToArray(),
                Triangles = triangles.ToArray(), TriangleLabels = labels.ToArray(),
                Neighbours = NeighboursOf(triangles.ToArray()), Subsegments = subs, BadTriangles = new int[0]
            };
            return (input, output);
        }

        public static List<Item> Run()
        {
            var items = new List<Item>();
            void Must(string name, bool ok, string detail = null) => items.Add(new Item { Name = name, Passed = ok, Detail = ok ? null : detail ?? "failed" });
            foreach (string index in new[] { "strtree", "strtree-hulls" }) {
                Must("conformity index " + index + ": default indexed", Oracle.UseConformityIndex(true, null, index));
                Must("conformity index " + index + ": explicit indexed", Oracle.UseConformityIndex(true, index, index));
                Must("conformity index " + index + ": explicit sweep", !Oracle.UseConformityIndex(true, "sweep", index));
                Must("conformity index " + index + ": reference default sweep", !Oracle.UseConformityIndex(false, null, index));
                Must("conformity index " + index + ": reference ignores index", !Oracle.UseConformityIndex(false, index, index));
                // set but empty or blank is unset; case and blanks around the value do not matter
                Must("conformity index " + index + ": empty is default", Oracle.UseConformityIndex(true, "", index) && Oracle.UseConformityIndex(true, "  ", index));
                Must("conformity index " + index + ": case-insensitive", Oracle.UseConformityIndex(true, index.ToUpperInvariant(), index) && !Oracle.UseConformityIndex(true, " Sweep ", index));
                bool rejected = false;
                try { Oracle.UseConformityIndex(true, "unknown", index); }
                catch (InvalidOperationException ex) { rejected = ex.Message.Contains("'unknown'") && ex.Message.Contains(index) && ex.Message.Contains("sweep"); }
                Must("conformity index " + index + ": reject unknown", rejected);
            }
            void Accept(string name, Mesh2Input input, Mesh2Output o, OracleExpect e)
            {
                var report = Oracle.Check(input, o, e);
                Must("accept: " + name, report.Ok, report.ToString());
            }
            void Reject(string name, Violation kind, Mesh2Input input, Mesh2Output o, OracleExpect e)
            {
                var report = Oracle.Check(input, o, e);
                Must("reject: " + name + " as " + kind, report.Has(kind), "report: " + report);
            }

            Arithmetic(Must);

            // ---- the double filters of "vertex within the tube of a source" (InventedVertices, Marks) decide only what is certain. In
            // M2 a vertex that is no input point must be a crossing: within the tube (2^-36 here) of two sources that are not collinear.
            // The sources are diagonal, so that a vertex near them lies inside their boxes and the filter, not the box test, decides:
            // s1 from (2, 2) to (14, 14), s2 from (2, 14) to (14, 2), and X = (8 + d, 8 - d) on s2 (up to rounding), 1.41 d off s1: d = 0.7e-11 within the
            // tube, 1.4e-11 outside (the filter's two bounds). And X on s1's extension, 1.3e-11 beyond (14, 14) per axis: inside s1's
            // box widened by the tube and on its line, but 1.84e-11 from its end (the filter's foot condition), with s2 through X
            // across. Each on the fast path and on the exact reference path
            {
                Mesh2Input TubeIn(double[] x, double[] y) => new Mesh2Input {
                    X = new double[] { 0, 16, 16, 0 }.Concat(x.Take(4)).ToArray(), Y = new double[] { 0, 0, 16, 16 }.Concat(y.Take(4)).ToArray(),
                    Segments = new List<SegmentInput> { new SegmentInput(4, 5), new SegmentInput(6, 7) } };
                Mesh2Output TubeOut(double[] x, double[] y, int[][] t) => new Mesh2Output {
                    Status = Mesh2Status.Success, VX = new double[] { 0, 16, 16, 0 }.Concat(x).ToArray(), VY = new double[] { 0, 0, 16, 16 }.Concat(y).ToArray(),
                    InputToVertex = new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Triangles = t, Neighbours = NeighboursOf(t), TriangleLabels = new int[t.Length],
                    // the piece from s1's end (14, 14) to X: along s1 for the crossing, along its extension for the vertex beyond it
                    Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(5, 8) }, BadTriangles = new int[0] };
                // triangulations of the nine points (positive, not constrained: only the checks below are looked at)
                var crossT = new[] { new[] { 4, 0, 1 }, new[] { 0, 4, 3 }, new[] { 2, 5, 1 }, new[] { 5, 2, 3 }, new[] { 5, 3, 6 }, new[] { 3, 4, 6 }, new[] { 4, 1, 7 }, new[] { 1, 5, 7 }, new[] { 4, 8, 6 }, new[] { 8, 5, 6 }, new[] { 5, 8, 7 }, new[] { 8, 4, 7 } };
                var beyondT = new[] { new[] { 4, 0, 1 }, new[] { 0, 4, 3 }, new[] { 5, 4, 1 }, new[] { 4, 5, 3 }, new[] { 2, 3, 6 }, new[] { 3, 5, 6 }, new[] { 5, 1, 7 }, new[] { 1, 2, 7 }, new[] { 5, 8, 6 }, new[] { 8, 2, 6 }, new[] { 2, 8, 7 }, new[] { 8, 5, 7 } };
                (Mesh2Input, Mesh2Output) Cross(double d)
                {
                    double[] x = { 2, 14, 2, 14, 8 + d }, y = { 2, 14, 14, 2, 8 - d };
                    return (TubeIn(x, y), TubeOut(x, y, crossT));
                }
                const double px = 14 + 1.3e-11;
                double[] bx = { 2, 14, px - 1, px + 1, px }, by = { 2, 14, px + 1, px - 1, px };
                var tube = new OracleExpect { Milestone = Milestone.M2, Tube = new Q(1, BigInteger.One << 36) };
                foreach (var (path, e) in new[] { ("fast", tube), ("reference", tube.WithReference()) }) {
                    var (ci, co) = Cross(0);
                    var crossing = Oracle.Check(ci, co, e);
                    Must("accept (" + path + "): a crossing vertex on both sources", !crossing.Has(Violation.SteinerNotAllowed) && !crossing.Has(Violation.SubsegmentUnsourced), "report: " + crossing);
                    (ci, co) = Cross(0.7e-11);
                    var inside = Oracle.Check(ci, co, e);
                    Must("accept (" + path + "): a crossing 0.99e-11 off one source, within the tube 2^-36", !inside.Has(Violation.SteinerNotAllowed), "report: " + inside);
                    (ci, co) = Cross(1.4e-11);
                    var outside = Oracle.Check(ci, co, e);
                    Must("reject (" + path + "): a vertex 1.98e-11 off one source, outside the tube 2^-36, as SteinerNotAllowed", outside.Has(Violation.SteinerNotAllowed), "report: " + outside);
                    var beyond = Oracle.Check(TubeIn(bx, by), TubeOut(bx, by, beyondT), e);
                    Must("reject (" + path + "): a vertex on a source's extension, 1.84e-11 beyond its end, as SteinerNotAllowed", beyond.Has(Violation.SteinerNotAllowed), "report: " + beyond);
                    Must("reject (" + path + "): a subsegment along a source's extension as SubsegmentUnsourced", beyond.Has(Violation.SubsegmentUnsourced), "report: " + beyond);
                }
            }

            // ---- an angle forced by a small input angle excuses its triangle only when the triangle's other angles meet Shewchuk's bound
            // near that angle (2002, section 6.3): arctan(sin phi / (2 - cos phi)), 10.27 degrees for phi = arctan(3/16) = 10.62. A hull
            // domain (D13: the hull corners force their angles) with a point on its bottom edge, at a bound of 11 degrees: the triangle at
            // the apex has its other small angle at the corner (16, 3), 10.92 degrees with the point at 8.4 and 9.94 at 8
            {
                Mesh2Output Fan(double sx) => new Mesh2Output {
                    Status = Mesh2Status.Success, VX = new[] { 0, 16, 16, sx }, VY = new double[] { 0, 0, 3, 0 }, InputToVertex = new[] { 0, 1, 2, 3 },
                    Triangles = new[] { new[] { 0, 3, 2 }, new[] { 3, 1, 2 } }, Neighbours = NeighboursOf(new[] { new[] { 0, 3, 2 }, new[] { 3, 1, 2 } }),
                    TriangleLabels = new[] { 0, 0 }, Subsegments = new List<SubsegmentOutput>(), BadTriangles = new[] { 0 } };
                Mesh2Input FanIn(double sx) => new Mesh2Input { X = new[] { 0, 16, 16, sx }, Y = new double[] { 0, 0, 3, 0 }, MinAngleDeg = 11 };
                var above = Oracle.Check(FanIn(8.4), Fan(8.4), new OracleExpect { Milestone = Milestone.M3 });
                Must("accept: a second angle above Shewchuk's bound near a small input angle is excused", !above.Has(Violation.AngleBelowBound), "report: " + above);
                var below = Oracle.Check(FanIn(8), Fan(8), new OracleExpect { Milestone = Milestone.M3 });
                Must("reject: a second angle below Shewchuk's bound near a small input angle as AngleBelowBound", below.Has(Violation.AngleBelowBound), "report: " + below);
                // mirrored in the x axis (the triangles' corners in the other order: the small second angle at the other corner)
                Mesh2Output Mirror(Mesh2Output o) { var m = Fan(o.VX[3]); m.VY = o.VY.Select(y => -y).ToArray(); m.Triangles = new[] { new[] { 0, 2, 3 }, new[] { 3, 2, 1 } }; m.Neighbours = NeighboursOf(m.Triangles); return m; }
                Mesh2Input MirrorIn(double sx) { var i = FanIn(sx); i.Y = i.Y.Select(y => -y).ToArray(); return i; }
                var aboveM = Oracle.Check(MirrorIn(8.4), Mirror(Fan(8.4)), new OracleExpect { Milestone = Milestone.M3 });
                Must("accept: the same mirrored", !aboveM.Has(Violation.AngleBelowBound), "report: " + aboveM);
                var belowM = Oracle.Check(MirrorIn(8), Mirror(Fan(8)), new OracleExpect { Milestone = Milestone.M3 });
                Must("reject: the same mirrored as AngleBelowBound", belowM.Has(Violation.AngleBelowBound), "report: " + belowM);
                // a needle at the apex: a point on the far edge, 0.28 above the corner (16, 0), splits the apex into 1.0 and 9.62 degrees,
                // both below the bound for the 10.62 degree input angle; the triangle of the apex with both far corners is excused
                Mesh2Output Needle(double ny) => new Mesh2Output {
                    Status = Mesh2Status.Success, VX = new double[] { 0, 16, 16, 16 }, VY = new[] { 0, 0, 3, ny }, InputToVertex = new[] { 0, 1, 2, 3 },
                    Triangles = new[] { new[] { 0, 1, 3 }, new[] { 0, 3, 2 } }, Neighbours = NeighboursOf(new[] { new[] { 0, 1, 3 }, new[] { 0, 3, 2 } }),
                    TriangleLabels = new[] { 0, 0 }, Subsegments = new List<SubsegmentOutput>(), BadTriangles = new[] { 0, 1 } };
                Mesh2Input NeedleIn(double ny) => new Mesh2Input { X = new double[] { 0, 16, 16, 16 }, Y = new[] { 0, 0, 3, ny }, MinAngleDeg = 11 };
                var needleReport = Oracle.Check(NeedleIn(0.28), Needle(0.28), new OracleExpect { Milestone = Milestone.M3 });
                Must("reject: a needle at the apex of a small input angle as AngleBelowBound", needleReport.Has(Violation.AngleBelowBound), "report: " + needleReport);
                var whole = Oracle.Check(new Mesh2Input { X = new double[] { 0, 16, 16 }, Y = new double[] { 0, 0, 3 }, MinAngleDeg = 11 },
                    new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 16, 16 }, VY = new double[] { 0, 0, 3 }, InputToVertex = new[] { 0, 1, 2 },
                        Triangles = new[] { new[] { 0, 1, 2 } }, Neighbours = NeighboursOf(new[] { new[] { 0, 1, 2 } }), TriangleLabels = new[] { 0 },
                        Subsegments = new List<SubsegmentOutput>(), BadTriangles = new[] { 0 } }, new OracleExpect { Milestone = Milestone.M3 });
                Must("accept: the apex triangle with the whole input angle", !whole.Has(Violation.AngleBelowBound), "report: " + whole);
            }

            // ---- the shortest edge of a triangle spans an input angle below 60 degrees (owner decision 2026-11-09, Shewchuk 2002 section
            // 6.2: refinement towards the apex of such an angle may not terminate). A hull domain of A = (0, 0), E1 = (30, 0) and E2, a bound
            // of 20 degrees, and the triangle p E1 q with p = (1, 0) and q on A-E2 at a distance within 1.5 of p's: its shortest edge p-q
            // spans the hull angle at A, and its angle at E1 is 1.5 degrees. Excused at the input angle of 36.87 degrees (E2 = (24, 18)),
            // reported at 63.43 degrees (E2 = (12, 24)), where the old threshold (the bound, 20 degrees) excused neither
            {
                Mesh2Input SpanIn(double ex, double ey) => new Mesh2Input { X = new[] { 0, 30, ex }, Y = new[] { 0, 0, ey }, MinAngleDeg = 20 };
                Mesh2Output SpanOut(double ex, double ey, double qx, double qy) {
                    var tris = new[] { new[] { 0, 3, 4 }, new[] { 3, 1, 4 }, new[] { 4, 1, 2 } };
                    return new Mesh2Output { Status = Mesh2Status.Success, VX = new[] { 0, 30, ex, 1, qx }, VY = new[] { 0, 0, ey, 0, qy },
                        InputToVertex = new[] { 0, 1, 2 }, Triangles = tris, Neighbours = NeighboursOf(tris), TriangleLabels = new[] { 0, 0, 0 },
                        Subsegments = new List<SubsegmentOutput>(), BadTriangles = new[] { 1 } };
                }
                var small = Oracle.Check(SpanIn(24, 18), SpanOut(24, 18, 1, 0.75), new OracleExpect { Milestone = Milestone.M3 });
                Must("accept: the shortest edge spans an input angle of 36.87 degrees, above the bound of 20 and below 60", !small.Has(Violation.AngleBelowBound), "report: " + small);
                var wide = Oracle.Check(SpanIn(12, 24), SpanOut(12, 24, 0.5, 1), new OracleExpect { Milestone = Milestone.M3 });
                Must("reject: the same at an input angle of 63.43 degrees, above 60, as AngleBelowBound", wide.Has(Violation.AngleBelowBound), "report: " + wide);
            }

            // ---- O-H5, parts collapsed by snapping (Q17): a 20 square with a corridor from x 20 to 50 around y 10, of width w, and a
            // segment along its middle, on a grid of pixel 1. A corridor narrower than a pixel snaps onto one row of pixel centres and
            // has no area: the mesh is the square alone, and the corridor's segment, its edges and its far points are on a collapsed
            // part. A corridor of three pixels has area: the same mesh misses its content
            {
                Mesh2Input CorridorIn(double w, double tolerance, bool extra)
                {
                    var x = new List<double> { 0, 20, 20, 50, 50, 20, 20, 0, 20, 50 };
                    var y = new List<double> { 0, 0, 10 - w / 2, 10 - w / 2, 10 + w / 2, 10 + w / 2, 20, 20, 10, 10 };
                    var input = new Mesh2Input { SnapTolerance = tolerance };
                    input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, false));
                    input.Segments.Add(new SegmentInput(8, 9));
                    // a point in the corridor with a segment into the square, where the domain has area
                    // and a segment along the corridor from it, collapsed: one of its constraints has area, the other none
                    if (extra) { x.Add(35); y.Add(10); x.Add(10); y.Add(10); input.Segments.Add(new SegmentInput(10, 11)); input.Segments.Add(new SegmentInput(10, 9)); }
                    input.X = x.ToArray(); input.Y = y.ToArray();
                    return input;
                }
                // the mesh of the snapped square: input points at their pixel centres, the representative of coinciding points the first
                Mesh2Output CorridorOut(Mesh2Input input, bool dropCorner, bool snapped = true)
                {
                    int n = input.Count;
                    var vx = input.X.Select(v => snapped ? Math.Floor(v + 0.5) : v).ToArray(); var vy = input.Y.Select(v => snapped ? Math.Floor(v + 0.5) : v).ToArray();
                    var map = new int[n];
                    for (int i = 0; i < n; ++i) { map[i] = i; for (int j = 0; j < i; ++j) if (vx[j] == vx[i] && vy[j] == vy[i]) { map[i] = j; break; } }
                    int lo = map[2], hi = map[5];
                    var t = new List<int[]>();
                    if (!dropCorner) t.Add(new[] { 0, 1, lo });
                    t.Add(new[] { 0, lo, 7 });
                    if (lo != hi) t.Add(new[] { lo, hi, 7 });
                    t.Add(new[] { hi, 6, 7 });
                    var subs = new List<SubsegmentOutput> { new SubsegmentOutput(1, lo), new SubsegmentOutput(hi, 6), new SubsegmentOutput(6, 7), new SubsegmentOutput(7, 0) };
                    if (!dropCorner) subs.Add(new SubsegmentOutput(0, 1));
                    var tri = t.ToArray();
                    return new Mesh2Output { Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = map, Triangles = tri, Neighbours = NeighboursOf(tri),
                        TriangleLabels = new int[tri.Length], Subsegments = subs, BadTriangles = new int[0] };
                }
                var m3 = new OracleExpect { Milestone = Milestone.M3 };
                var thin = CorridorIn(0.25, 1, false);
                var collapsed = Oracle.Check(thin, CorridorOut(thin, false), m3);
                Must("accept: a corridor narrower than a pixel collapsed with its segment and its far points (O-H5)", collapsed.Ok && collapsed.CollapsedSegments > 0 && collapsed.CollapsedPoints > 0, "report: " + collapsed + " / " + collapsed.ExcusedSummary());
                var wide = CorridorIn(3, 1, false);
                var area = Oracle.Check(wide, CorridorOut(wide, false), m3);
                // the segment and the corridor's three edges (an edge never covers itself: the rule asks for another edge)
                Must("reject: a corridor of three pixels whose content is missing as SegmentMissing", area.Violations.Count(v => v.Kind == Violation.SegmentMissing) >= 4, "report: " + area);
                // the segment along the middle, 1.5 pixels from the corridor's edges, in particular
                Must("reject: the segment along a corridor of three pixels as SegmentMissing", area.Violations.Any(v => v.Kind == Violation.SegmentMissing && v.Detail.StartsWith("input constraint (20, 10) - (50, 10)")), "report: " + area);
                var unsnapped = CorridorIn(0.25, 0, false);
                var untoleranced = Oracle.Check(unsnapped, CorridorOut(unsnapped, false, snapped: false), m3);
                Must("reject: the collapsed corridor without a tolerance as SegmentMissing", untoleranced.Has(Violation.SegmentMissing), "report: " + untoleranced);
                var mixed = CorridorIn(0.25, 1, true);
                var mixedReport = Oracle.Check(mixed, CorridorOut(mixed, false), m3);
                // its pixel lies on the collapsed corridor: no triangle on the grid can hold it (review 3, S1). Its segment into the square
                // keeps its chain there, which this output lacks
                Must("accept: a point on the collapsed corridor with a segment into the square, unused", !mixedReport.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 10 ")), "report: " + mixedReport);
                Must("reject: that segment's chain missing in the square", mixedReport.Violations.Any(v => v.Kind == Violation.SegmentMissing && v.Detail.StartsWith("input constraint (35, 10) - (10, 10)")), "report: " + mixedReport);
                // a dumbbell: two 20 squares joined by a corridor 0.25 wide from x 20 to 50, a segment from the middle of one to the
                // middle of the other, either way. Its chain must be there in both squares, up to the corridor's mouths at (20, 10) and
                // (50, 10), whose hot pixels the chain reaches before the collapsed stretch's first pixel boundary; it may be missing in
                // the corridor only
                foreach (bool forward in new[] { true, false }) {
                    var bell = new Mesh2Input { SnapTolerance = 1,
                        X = new double[] { 0, 20, 20, 50, 50, 70, 70, 50, 50, 20, 20, 0, 10, 60 },
                        Y = new double[] { 0, 0, 9.875, 9.875, 0, 0, 20, 20, 10.125, 10.125, 20, 20, 10, 10 } };
                    bell.Rings.Add(new RingInput(Enumerable.Range(0, 12).ToArray(), false));
                    bell.Segments.Add(forward ? new SegmentInput(12, 13) : new SegmentInput(13, 12));
                    var map = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 3, 2, 10, 11, 12, 13 };
                    var fans = new[] { new[] { 0, 1, 12 }, new[] { 1, 2, 12 }, new[] { 2, 10, 12 }, new[] { 10, 11, 12 }, new[] { 11, 0, 12 },
                        new[] { 3, 4, 13 }, new[] { 4, 5, 13 }, new[] { 5, 6, 13 }, new[] { 6, 7, 13 }, new[] { 7, 3, 13 } };
                    var ring = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 10), new SubsegmentOutput(10, 11),
                        new SubsegmentOutput(11, 0), new SubsegmentOutput(3, 4), new SubsegmentOutput(4, 5), new SubsegmentOutput(5, 6), new SubsegmentOutput(6, 7), new SubsegmentOutput(7, 3) };
                    Mesh2Output Bell(bool chainInA) => new Mesh2Output { Status = Mesh2Status.Success,
                        VX = bell.X.Select(x => Math.Floor(x + 0.5)).ToArray(), VY = bell.Y.Select(y => Math.Floor(y + 0.5)).ToArray(), InputToVertex = map,
                        Triangles = fans, Neighbours = NeighboursOf(fans), TriangleLabels = new int[fans.Length], BadTriangles = new int[0],
                        Subsegments = ring.Concat(chainInA ? new[] { new SubsegmentOutput(12, 2), new SubsegmentOutput(3, 13) } : new[] { new SubsegmentOutput(3, 13) }).ToList() };
                    if (forward)
                    foreach (var (g0, g1) in new[] { (14.8, 15.2), (19.6, 19.8) }) {
                        // reviews 4 and 5, S2: a foreign output whose chain in square A has a gap (a mesh edge, no subsegment) within one
                        // pixel of the image, (15, 10) or (20, 10), the latter the end of the collapsed link into the corridor: only vertices
                        // at the centre of such an end are joined, so it stays missing
                        var gapIn = new Mesh2Input { SnapTolerance = 1, X = bell.X.Concat(new[] { g0, g1 }).ToArray(), Y = bell.Y.Concat(new[] { 10.0, 10.0 }).ToArray() };
                        gapIn.Rings.AddRange(bell.Rings); gapIn.Segments.AddRange(bell.Segments);
                        var gx = new double[] { 0, 20, 20, 50, 50, 70, 70, 50, 50, 20, 20, 0, 10, 60, g0, g1 };
                        var gy = new double[] { 0, 0, 10, 10, 0, 0, 20, 20, 10, 10, 20, 20, 10, 10, 10, 10 };
                        var gt = new[] { new[] { 0, 1, 14 }, new[] { 14, 1, 15 }, new[] { 15, 1, 2 }, new[] { 0, 14, 12 }, new[] { 11, 0, 12 }, new[] { 12, 14, 10 }, new[] { 14, 15, 10 }, new[] { 15, 2, 10 }, new[] { 10, 11, 12 },
                            new[] { 3, 4, 13 }, new[] { 4, 5, 13 }, new[] { 5, 6, 13 }, new[] { 6, 7, 13 }, new[] { 7, 3, 13 } };
                        var gapOut = new Mesh2Output { Status = Mesh2Status.Success, VX = gx, VY = gy, InputToVertex = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 3, 2, 10, 11, 12, 13, 14, 15 },
                            Triangles = gt, Neighbours = NeighboursOf(gt), TriangleLabels = new int[gt.Length], BadTriangles = new int[0], IdentityRules = false,
                            Subsegments = ring.Concat(new[] { new SubsegmentOutput(12, 14), new SubsegmentOutput(15, 2), new SubsegmentOutput(3, 13) }).ToList() };
                        var gapReport = Oracle.Check(gapIn, gapOut, m3);
                        Must("reject: a gap in a chain where the domain has area, bridged by a shared pixel of the image (review 4, S2, at " + g0 + ")",
                            gapReport.Violations.Any(x => x.Kind == Violation.SegmentMissing && x.Detail.StartsWith("input constraint (10, 10)")), "report: " + gapReport);
                    }
                    string way = forward ? "forward" : "backward";
                    var withChain = Oracle.Check(bell, Bell(true), m3);
                    Must("accept: a segment through a collapsed corridor with its chain in both squares (" + way + ")", withChain.Ok && withChain.CollapsedSegments > 0, "report: " + withChain);
                    var noChain = Oracle.Check(bell, Bell(false), m3);
                    Must("reject: the same segment without its chain in one square (" + way + ")",
                        noChain.Violations.Any(v => v.Kind == Violation.SegmentMissing && v.Detail.StartsWith("input constraint")), "report: " + noChain);
                }
                // the corridor's segment the other way, from its collapsed tip (no vertex there: the search starts at the tip's pixel)
                {
                    var back = CorridorIn(0.25, 1, false);
                    back.Segments.Clear(); back.Segments.Add(new SegmentInput(9, 8));
                    var backReport = Oracle.Check(back, CorridorOut(back, false), m3);
                    Must("accept: the corridor's segment from its collapsed tip into the square", backReport.Ok && backReport.CollapsedSegments > 0, "report: " + backReport + " / " + backReport.ExcusedSummary());
                }
                // the corridor's mouth (20, 10) in no triangle: one link there collapsed (into the corridor), the others with area (the
                // square's right side): its input points stay unused
                {
                    var mouthless = CorridorOut(thin, false);
                    var tm = new[] { new[] { 0, 1, 6 }, new[] { 0, 6, 7 } };
                    mouthless.Triangles = tm; mouthless.Neighbours = NeighboursOf(tm); mouthless.TriangleLabels = new int[tm.Length];
                    mouthless.Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 6), new SubsegmentOutput(6, 7), new SubsegmentOutput(7, 0) };
                    var mouthReport = Oracle.Check(thin, mouthless, m3);
                    Must("reject: the corridor's mouth unused (a link there with area)", mouthReport.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 2 ")), "report: " + mouthReport);
                }
                // a free point inside the square, unused: no link at its pixel, and its centre inside the snapped domain
                {
                    var freeIn = CorridorIn(0.25, 1, false);
                    freeIn.X = freeIn.X.Concat(new[] { 5.0 }).ToArray(); freeIn.Y = freeIn.Y.Concat(new[] { 5.0 }).ToArray();
                    var freeOut = CorridorOut(freeIn, false);
                    var freeReport = Oracle.Check(freeIn, freeOut, m3);
                    Must("reject: a free point inside the square, unused", freeReport.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 10 ")), "report: " + freeReport);
                }
                var corner = Oracle.Check(thin, CorridorOut(thin, true), m3);
                Must("reject: a corner whose edges have area, unused, as VertexUnused", corner.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 1 ")), "report: " + corner);
                // the images (iterated snap rounding, pixel 1): a link is rerouted through every hot pixel it passes, again on the new
                // links until none is left
                {
                    (BigInteger x, BigInteger y) N(int x, int y) => (x, y);
                    string Show(List<(BigInteger x, BigInteger y)> img) => string.Join(" ", img.Select(n => "(" + n.x + ", " + n.y + ")"));
                    Pt O = Pt.FromDoubles(0, 0);
                    var bare = Oracle.SnapImage(O, Pt.FromDoubles(20, 1.2), new Pt[0], Q.One);
                    Must("image: a segment past no hot pixel is its end pixels", bare.SequenceEqual(new[] { N(0, 0), N(20, 1) }), Show(bare));
                    var once = Oracle.SnapImage(O, Pt.FromDoubles(20, 1.2), new[] { Pt.FromDoubles(10, 1) }, Q.One);
                    Must("image: a segment through a hot pixel is rerouted through its centre", once.SequenceEqual(new[] { N(0, 0), N(10, 1), N(20, 1) }), Show(once));
                    // (6, 1) is missed by the segment (y 0.33 to 0.39 there) but passed by its rerouted link to (10, 1) (y 0.55 to 0.65)
                    var iterated = Oracle.SnapImage(O, Pt.FromDoubles(20, 1.2), new[] { Pt.FromDoubles(10, 1), Pt.FromDoubles(6, 1) }, Q.One);
                    Must("image: a rerouted link is rerouted again (iterated snap rounding)", iterated.SequenceEqual(new[] { N(0, 0), N(6, 1), N(10, 1), N(20, 1) }), Show(iterated));
                    // two passes over the links after the first: (0, 0) - (24, -1.4) is rerouted through (11, 0) only by a link, and
                    // (14, 0) only by a link of that
                    var twice = new[] { Pt.FromDoubles(11, 0), Pt.FromDoubles(14, 0), Pt.FromDoubles(19, 1) };
                    var deep = Oracle.SnapImage(O, Pt.FromDoubles(24, -1.4), twice, Q.One);
                    Must("image: iterated until no link passes a hot pixel (two passes over the links)", deep.SequenceEqual(new[] { N(0, 0), N(11, 0), N(14, 0), N(24, -1) }), Show(deep));
                    // a hot pixel only touched at its corner does not reroute (O-H4's rule): the link (0, 0) - (1, 1) touches pixel (1, 0)
                    // at (0.5, 0.5)
                    var touched = Oracle.SnapImage(O, Pt.FromDoubles(1, 1), new[] { Pt.FromDoubles(1, 0) }, Q.One);
                    Must("image: a segment only touching a hot pixel's corner is not rerouted", touched.SequenceEqual(new[] { N(0, 0), N(1, 1) }), Show(touched));
                    // the first pass on the input segment contacts as the mesher's snap rounding does (Hobby's half-open pixels): through
                    // a pixel's bottom-left corner it is rerouted, through any other corner not (review 5)
                    // ((0, 1) - (1, 0) runs through (0.5, 0.5): the bottom-left corner of pixel (1, 1), the top-right one of (0, 0))
                    var bottomLeft = Oracle.SnapImage(Pt.FromDoubles(0, 1), Pt.FromDoubles(1, 0), new[] { Pt.FromDoubles(1, 1) }, Q.One);
                    Must("image: a segment through a hot pixel's bottom-left corner is rerouted", bottomLeft.Contains(N(1, 1)), Show(bottomLeft));
                    var topRight = Oracle.SnapImage(Pt.FromDoubles(0, 1), Pt.FromDoubles(1, 0), new[] { Pt.FromDoubles(0, 0) }, Q.One);
                    Must("image: a segment through a hot pixel's top-right corner is not", !topRight.Contains(N(0, 0)), Show(topRight));
                    // ... but a pixel made hot by refinement (an output vertex) is met by its interior only, as the mesher's grid splits
                    // avoid only pixels a constraint crosses inside: through its bottom-left corner the segment is not rerouted
                    var refinedCorner = Oracle.SnapImage(Pt.FromDoubles(0, 1), Pt.FromDoubles(1, 0), new Pt[0], Q.One, new[] { Pt.FromDoubles(1, 1) });
                    Must("image: a segment through the bottom-left corner of a refined vertex's pixel is not rerouted", !refinedCorner.Contains(N(1, 1)), Show(refinedCorner));
                    // several hot pixels on one link in order along it
                    var order = Oracle.SnapImage(O, Pt.FromDoubles(40, 0), new[] { Pt.FromDoubles(30, 0), Pt.FromDoubles(10, 0) }, Q.One);
                    Must("image: hot pixels in order along a link", order.SequenceEqual(new[] { N(0, 0), N(10, 0), N(30, 0), N(40, 0) }), Show(order));
                    // the same with more hot pixels than the links cross: the pixels along a link are walked instead of the hot pixels tested
                    var far = Enumerable.Range(0, 60).Select(i => Pt.FromDoubles(1000 + i, 1000)).ToList();
                    var walkedIterated = Oracle.SnapImage(O, Pt.FromDoubles(20, 1.2), new[] { Pt.FromDoubles(10, 1), Pt.FromDoubles(6, 1) }.Concat(far), Q.One);
                    Must("image: iterated, walking the pixels", walkedIterated.SequenceEqual(new[] { N(0, 0), N(6, 1), N(10, 1), N(20, 1) }), Show(walkedIterated));
                    var walkedOrder = Oracle.SnapImage(O, Pt.FromDoubles(40, 0), new[] { Pt.FromDoubles(30, 0), Pt.FromDoubles(10, 0) }.Concat(far), Q.One);
                    Must("image: in order, walking the pixels", walkedOrder.SequenceEqual(new[] { N(0, 0), N(10, 0), N(30, 0), N(40, 0) }), Show(walkedOrder));
                    var walkedTouched = Oracle.SnapImage(O, Pt.FromDoubles(1, 1), new[] { Pt.FromDoubles(1, 0) }.Concat(far), Q.One);
                    Must("image: a corner touch, walking the pixels", walkedTouched.SequenceEqual(new[] { N(0, 0), N(1, 1) }), Show(walkedTouched));
                }
                // the review's cases (where the rule could excuse a gap that snap rounding does not create): parts of a wide domain
                // closer than a pixel to one edge only. Output: the input snapped to pixel 1, without what the case drops; nothing may be
                // excused as collapsed
                {
                    Mesh2Output Snapped(Mesh2Input input, int[][] tri, int[][] subs)
                    {
                        var vx = input.X.Select(v => Math.Floor(v + 0.5)).ToArray(); var vy = input.Y.Select(v => Math.Floor(v + 0.5)).ToArray();
                        return new Mesh2Output { Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                            Triangles = tri, Neighbours = NeighboursOf(tri), TriangleLabels = new int[tri.Length], BadTriangles = new int[0],
                            Subsegments = subs.Select(s => new SubsegmentOutput(s[0], s[1])).ToList() };
                    }
                    int[][] Square = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 } };
                    bool NothingExcused(OracleReport rep) => rep.CollapsedSegments == 0 && rep.CollapsedPoints == 0;
                    // E1: a constraint 0.7 above the bottom of a 20 square, dropped with its ends
                    var e1 = new Mesh2Input { X = new double[] { 0, 20, 20, 0, 5, 15 }, Y = new double[] { 0, 0, 20, 20, 0.7, 0.7 }, SnapTolerance = 1 };
                    e1.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    e1.Segments.Add(new SegmentInput(4, 5));
                    var e1r = Oracle.Check(e1, Snapped(e1, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, Square), m3);
                    Must("reject: a constraint 0.7 from one edge of a wide square, dropped (review E1)", e1r.Has(Violation.SegmentMissing) && e1r.Has(Violation.VertexUnused) && NothingExcused(e1r), "report: " + e1r);
                    // E1b: the same along the hypotenuse of a right triangle, 0.9 away
                    double d = 0.9 / Math.Sqrt(2);
                    var e1b = new Mesh2Input { X = new double[] { 0, 20, 0, 15 - d, 5 - d }, Y = new double[] { 0, 0, 20, 5 - d, 15 - d }, SnapTolerance = 1 };
                    e1b.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                    e1b.Segments.Add(new SegmentInput(3, 4));
                    var e1br = Oracle.Check(e1b, Snapped(e1b, new[] { new[] { 0, 1, 2 } }, new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 0 } }), m3);
                    Must("reject: a constraint 0.9 from a diagonal edge, dropped (review E1b)", e1br.Has(Violation.SegmentMissing) && NothingExcused(e1br), "report: " + e1br);
                    // E2: a hole on a stretch of the outer bottom edge; the outer bottom's pieces beside the hole dropped (the source edge,
                    // not the piece, is excluded: the outer bottom does not collapse onto itself)
                    var e2 = new Mesh2Input { X = new double[] { 0, 20, 20, 0, 8, 12, 10 }, Y = new double[] { 0, 0, 20, 20, 0, 0, 5 }, SnapTolerance = 1 };
                    e2.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    e2.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
                    var e2t = new[] { new[] { 0, 4, 6 }, new[] { 5, 1, 6 }, new[] { 1, 2, 6 }, new[] { 6, 2, 3 }, new[] { 0, 6, 3 } };
                    var e2r = Oracle.Check(e2, Snapped(e2, e2t, new[] { new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 5, 6 }, new[] { 6, 4 } }), m3);
                    Must("reject: a hole on the outer edge with the outer edge's pieces missing (review E2)", e2r.Has(Violation.SegmentMissing) && NothingExcused(e2r), "report: " + e2r);
                    var e2c = Oracle.Check(e2, Snapped(e2, e2t, new[] { new[] { 0, 4 }, new[] { 5, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 5, 6 }, new[] { 6, 4 } }), m3);
                    Must("accept: the hole on the outer edge, complete (review E2)", e2c.Ok, "report: " + e2c);
                    // E6: a constraint 0.8 long into a wide square from its bottom, its inner end dropped (a T-junction)
                    var e6 = new Mesh2Input { X = new double[] { 0, 20, 20, 0, 10, 10 }, Y = new double[] { 0, 0, 20, 20, 0, 0.8 }, SnapTolerance = 1 };
                    e6.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    e6.Segments.Add(new SegmentInput(4, 5));
                    var e6r = Oracle.Check(e6, Snapped(e6, new[] { new[] { 0, 4, 3 }, new[] { 4, 1, 2 }, new[] { 4, 2, 3 } },
                        new[] { new[] { 0, 4 }, new[] { 4, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 } }), m3);
                    Must("reject: a short constraint into a wide square, inner end dropped (review E6)", e6r.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 5 ")) && NothingExcused(e6r), "report: " + e6r);
                    // review 3, B1: a segment on the bottom edge of a wide square, dropped with its ends (the bottom edge is on its line:
                    // it serves neither side)
                    var b1 = new Mesh2Input { X = new double[] { 0, 20, 20, 0, 5, 15 }, Y = new double[] { 0, 0, 20, 20, 0, 0 }, SnapTolerance = 1 };
                    b1.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    b1.Segments.Add(new SegmentInput(4, 5));
                    var b1r = Oracle.Check(b1, Snapped(b1, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, Square), m3);
                    Must("reject: a segment on the edge of a wide square, dropped (review 3, B1)", b1r.Has(Violation.SegmentMissing) && b1r.Has(Violation.VertexUnused) && NothingExcused(b1r), "report: " + b1r);
                    // review 3, B2: a stub 0.4 long into a wide square from its bottom at y 0.3, across the pixel line 0.5: it snaps to a
                    // whole pixel's edge; its inner end dropped (the bottom edge passes the stub's base: it lies ahead, not beside)
                    var stub = new Mesh2Input { X = new double[] { 0, 20, 20, 0, 10, 10 }, Y = new double[] { 0.3, 0.3, 20, 20, 0.3, 0.7 }, SnapTolerance = 1 };
                    stub.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    stub.Segments.Add(new SegmentInput(4, 5));
                    var stubr = Oracle.Check(stub, Snapped(stub, new[] { new[] { 0, 4, 3 }, new[] { 4, 1, 2 }, new[] { 4, 2, 3 } },
                        new[] { new[] { 0, 4 }, new[] { 4, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 } }), m3);
                    Must("reject: a stub across a pixel line into a wide square, inner end dropped (review 3, B2)", stubr.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 5 ")) && NothingExcused(stubr), "report: " + stubr);
                    // review 3, B2: a corridor 0.9 wide whose walls (y 10.05 and 10.95) snap to rows 10 and 11: it keeps its area. Its
                    // content (the walls, the segment along its middle and the far points) missing, the mouth closed instead
                    {
                        var wide9 = new Mesh2Input { SnapTolerance = 1,
                            X = new double[] { 0, 20, 20, 50, 50, 20, 20, 0, 20, 50 }, Y = new double[] { 0, 0, 10.05, 10.05, 10.95, 10.95, 20, 20, 10.5, 10.5 } };
                        wide9.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, false));
                        wide9.Segments.Add(new SegmentInput(8, 9));
                        var map9 = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 5, 4 };
                        var t9 = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 7 }, new[] { 2, 5, 7 }, new[] { 5, 6, 7 } };
                        var o9 = new Mesh2Output { Status = Mesh2Status.Success, VX = wide9.X.Select(x => Math.Floor(x + 0.5)).ToArray(), VY = wide9.Y.Select(y => Math.Floor(y + 0.5)).ToArray(),
                            InputToVertex = map9, Triangles = t9, Neighbours = NeighboursOf(t9), TriangleLabels = new int[t9.Length], BadTriangles = new int[0],
                            Subsegments = new[] { (0, 1), (1, 2), (2, 5), (5, 6), (6, 7), (7, 0) }.Select(x => new SubsegmentOutput(x.Item1, x.Item2)).ToList() };
                        var r9 = Oracle.Check(wide9, o9, m3);
                        Must("reject: a corridor 0.9 wide across a row boundary, its content missing (review 3, B2)", r9.Has(Violation.SegmentMissing) && r9.Has(Violation.VertexUnused) && NothingExcused(r9), "report: " + r9);
                    }
                    // review 3, B2: an oblique corridor 0.85 wide (walls y = x - 0.6 and y = x + 0.6 from a square's corner (20, 20) to x 40)
                    // snaps onto two diagonals and keeps its area; dropped, the corner closed by the mouth (20, 19) - (19, 20)
                    {
                        var obl = new Mesh2Input { SnapTolerance = 1,
                            X = new double[] { 0, 20, 20, 40, 40, 19.4, 0 }, Y = new double[] { 0, 0, 19.4, 39.4, 40.6, 20, 20 } };
                        obl.Rings.Add(new RingInput(Enumerable.Range(0, 7).ToArray(), false));
                        var tObl = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 5 }, new[] { 0, 5, 6 } };
                        var rObl = Oracle.Check(obl, Snapped(obl, tObl, new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 5 }, new[] { 5, 6 }, new[] { 6, 0 } }), m3);
                        Must("reject: an oblique corridor 0.85 wide, dropped (review 3, B2)", rObl.Has(Violation.SegmentMissing) && rObl.Has(Violation.VertexUnused) && NothingExcused(rObl), "report: " + rObl);
                    }
                    // snap rounding's invariant (ChainPassesHotPixel): a Steiner point (10, 11) in the pixel of a chain (2, 10) - (17, 12)
                    // that passes it 0.07 above its centre without running through it is reported, so an extra vertex cannot buy an O-H5
                    // excuse (the mesher's vertex 309 of the zigzag, turned around); routed through it, the chain is accepted
                    {
                        var hp = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 20, 0, 2, 17 }, Y = new double[] { 0, 0, 20, 20, 10, 12 } };
                        hp.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                        hp.Segments.Add(new SegmentInput(4, 5));
                        var tHp = new[] { new[] { 4, 0, 6 }, new[] { 0, 1, 6 }, new[] { 1, 5, 6 }, new[] { 5, 4, 6 }, new[] { 5, 1, 2 }, new[] { 4, 5, 2 }, new[] { 4, 2, 3 }, new[] { 4, 3, 0 } };
                        Mesh2Output Hp(params (int a, int b)[] chain) => new Mesh2Output { Status = Mesh2Status.Success,
                            VX = new double[] { 0, 20, 20, 0, 2, 17, 10 }, VY = new double[] { 0, 0, 20, 20, 10, 12, 11 }, InputToVertex = Enumerable.Range(0, 6).ToArray(),
                            Triangles = tHp, Neighbours = NeighboursOf(tHp), TriangleLabels = new int[tHp.Length], BadTriangles = new int[0],
                            Subsegments = new[] { (0, 1), (1, 2), (2, 3), (3, 0) }.Concat(chain).Select(x => new SubsegmentOutput(x.Item1, x.Item2)).ToList() };
                        var past = Oracle.Check(hp, Hp((4, 5)), m3);
                        Must("reject: a chain past a Steiner point's pixel without running through it as ChainPassesHotPixel",
                            past.Violations.Any(x => x.Kind == Violation.ChainPassesHotPixel && x.Detail.Contains("(10, 11)")), "report: " + past);
                        var through = Oracle.Check(hp, Hp((4, 6), (6, 5)), m3);
                        Must("accept: the chain routed through the Steiner point's pixel (no ChainPassesHotPixel)", !through.Has(Violation.ChainPassesHotPixel), "report: " + through);
                        var foreign = Hp((4, 5)); foreign.IdentityRules = false;
                        var foreignReport = Oracle.Check(hp, foreign, m3);
                        Must("accept: a foreign output (no snap rounder) is not held to snap rounding's invariant (review 4)", !foreignReport.Has(Violation.ChainPassesHotPixel), "report: " + foreignReport);
                        var hpPlain = new Mesh2Input { X = hp.X, Y = hp.Y, Rings = hp.Rings, Segments = hp.Segments };
                        var free = Oracle.Check(hpPlain, Hp((4, 5)), m3);
                        Must("accept: without a tolerance there are no hot pixels (no ChainPassesHotPixel)", !free.Has(Violation.ChainPassesHotPixel), "report: " + free);
                    }
                    // the crossing of two segments makes its pixel hot: two chains straight through the crossing's pixel (10, 10) without a
                    // vertex there pass it (ChainPassesHotPixel; also EdgesCross)
                    {
                        var crossIn = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 20, 0, 2, 18, 10.3, 10.3 }, Y = new double[] { 0, 0, 20, 20, 10, 10, 2, 18 } };
                        crossIn.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                        crossIn.Segments.Add(new SegmentInput(4, 5)); crossIn.Segments.Add(new SegmentInput(6, 7));
                        var tc = new[] { new[] { 0, 1, 6 }, new[] { 0, 6, 4 }, new[] { 4, 6, 5 }, new[] { 6, 1, 5 }, new[] { 1, 2, 5 }, new[] { 5, 2, 7 }, new[] { 4, 5, 7 }, new[] { 7, 2, 3 }, new[] { 4, 7, 3 }, new[] { 0, 4, 3 } };
                        var oc = Snapped(crossIn, tc, new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 4, 5 }, new[] { 6, 7 } });
                        var rc = Oracle.Check(crossIn, oc, m3);
                        Must("reject: two chains straight through their crossing's pixel without a vertex (ChainPassesHotPixel)", rc.Violations.Any(x => x.Kind == Violation.ChainPassesHotPixel && x.Detail.Contains("(10, 10)")), "report: " + rc);
                    }
                    // review 4, B1: images round the input segment itself first, not the line between its end pixels' centres. A
                    // diagonal strip 1.27 wide: its wall y = x + 0.9 passes pixel (3, 4), which a free point makes hot, so snap rounding
                    // keeps a triangle of area there; dropped with the point, rejected
                    {
                        var stripIn = new Mesh2Input { SnapTolerance = 1,
                            X = new double[] { -0.45, 0.45, 10.45, 9.55, 20, 30, 30, 20, 3 }, Y = new double[] { 0.45, -0.45, 9.55, 10.45, 0, 0, 10, 10, 3.85 } };
                        stripIn.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                        stripIn.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, false));
                        var ts = new[] { new[] { 4, 5, 6 }, new[] { 4, 6, 7 } };
                        var rs = Oracle.Check(stripIn, Snapped(stripIn, ts, new[] { new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 } }), m3);
                        Must("reject: a strip whose wall snap rounding routes through a hot pixel off the end pixels' line, dropped (review 4, B1)",
                            rs.Has(Violation.SegmentMissing) && rs.Violations.Any(x => x.Kind == Violation.VertexUnused && x.Detail.StartsWith("input 8 ")) && NothingExcused(rs), "report: " + rs);
                        // review 5: the edge (0, 5.25) - (20, -4.75) runs through (11.5, -0.5), the bottom-left corner of the pixel of the
                        // point (12, 0.2); the mesher's snap rounding routes it there and the triangle collapses: the square alone is accepted
                        var corner45 = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 12, 100, 120, 120, 100 }, Y = new double[] { 5.25, -4.75, 0.2, 0, 0, 20, 20 } };
                        corner45.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                        corner45.Rings.Add(new RingInput(new[] { 3, 4, 5, 6 }, false));
                        var t45 = new[] { new[] { 3, 4, 5 }, new[] { 3, 5, 6 } };
                        var r45 = Oracle.Check(corner45, Snapped(corner45, t45, new[] { new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 3 } }), new OracleExpect { Milestone = Milestone.M2 });
                        Must("accept: a triangle whose edge runs through a hot pixel's bottom-left corner, collapsed (review 5)", r45.Ok && r45.CollapsedPoints == 3, "report: " + r45 + " / " + r45.ExcusedSummary());
                        // review 5, S-C, rule (ii'): without the point (12, 0.1) the hypotenuse passes pixel (12, 0) only if an output vertex
                        // makes it hot; a Steiner vertex there must not buy the collapse of (0, 0) - (12, 0): the input's own rounding keeps
                        // the whole triangle, so the dropped 6 px² are reported
                        var bought = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 20 }, Y = new double[] { -0.45, -0.45, 0.55 } };
                        bought.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                        var tb = new[] { new[] { 3, 1, 2 } };
                        var ob = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 20, 20, 12 }, VY = new double[] { 0, 0, 1, 0 }, InputToVertex = new[] { 0, 1, 2 },
                            Triangles = tb, Neighbours = NeighboursOf(tb), TriangleLabels = new int[1], BadTriangles = new int[0],
                            Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(3, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 3) } };
                        var rb = Oracle.Check(bought, ob, m3);
                        Must("reject: a Steiner vertex that would make the input collapse where its own rounding keeps area (review 5, S-C, rule (ii'))",
                            rb.Has(Violation.SegmentMissing) && rb.CollapsedSegments == 0 && rb.CollapsedPoints == 0, "report: " + rb + " / " + rb.ExcusedSummary());
                        // ... and the converse: the hypotenuse (0, -0.45) - (20, 0.55) passes the pixel of the point (12, 0.1), so snap rounding
                        // collapses (0, 0) - (12, 0) onto the bottom; the mesh of the triangle (12, 0), (20, 0), (20, 1) is accepted
                        var hyp = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 20, 12 }, Y = new double[] { -0.45, -0.45, 0.55, 0.1 } };
                        hyp.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                        var th = new[] { new[] { 3, 1, 2 } };
                        var rh = Oracle.Check(hyp, Snapped(hyp, th, new[] { new[] { 3, 1 }, new[] { 1, 2 }, new[] { 2, 3 } }), m3);
                        Must("accept: a hypotenuse that snap rounding routes through a point's pixel off the end pixels' line (review 4, B1)",
                            rh.Ok && rh.CollapsedSegments > 0 && rh.CollapsedPoints > 0, "report: " + rh + " / " + rh.ExcusedSummary());
                        Must("... with no excuse counted as rerouted by output vertices ((iii))", rh.CollapsedViaRefinement == 0, "report: " + rh.ExcusedSummary());
                        // post-review B1, rule (ii'): a Steiner vertex at (10, 1) reroutes both edges of the wedge (0, 0), (40, 4.6),
                        // (40, 5.6) onto one link (0, 0) - (10, 1), collapsed there; that link's middle lies outside the input's own band,
                        // which keeps 1.25 px² beside it for x < 10: rejected (a second ring makes (0, 0) a used vertex)
                        var wedge = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 40, 40, -10, -10 }, Y = new double[] { 0, 4.6, 5.6, -5, 5 } };
                        wedge.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                        wedge.Rings.Add(new RingInput(new[] { 0, 3, 4 }, false));
                        var tw = new[] { new[] { 5, 1, 2 }, new[] { 0, 4, 3 } };
                        var ow = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 40, 40, -10, -10, 10 }, VY = new double[] { 0, 5, 6, -5, 5, 1 }, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                            Triangles = tw, Neighbours = NeighboursOf(tw), TriangleLabels = new int[2], BadTriangles = new int[0],
                            Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(5, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 5),
                                new SubsegmentOutput(0, 4), new SubsegmentOutput(4, 3), new SubsegmentOutput(3, 0) } };
                        var rw = Oracle.Check(wedge, ow, m3);
                        Must("reject: a Steiner vertex that reroutes a chain off the input's own band, which keeps area there (post-review B1, rule (ii'))",
                            rw.Violations.Count(x => x.Kind == Violation.SegmentMissing) == 2 && rw.CollapsedSegments == 0, "report: " + rw + " / " + rw.ExcusedSummary());
                        // the same output judged at M2 (no refinement): the input-only state holds the input's points, crossings and the
                        // restore pixels the oracle computes itself (none here), not the output vertex (10, 1), so it is rejected as well
                        var rw2 = Oracle.Check(wedge, ow, new OracleExpect { Milestone = Milestone.M2 });
                        Must("reject: the wedge judged at M2, where the vertex (10, 1) is no input point, crossing or restore pixel",
                            rw2.Violations.Count(x => x.Kind == Violation.SegmentMissing) == 2 && rw2.CollapsedSegments == 0, "report: " + rw2 + " / " + rw2.ExcusedSummary());
                        // ... and the converse: the sliver (0, 0), (20, 4.55), (20, 5.45) collapses in the input's own rounding (both edges
                        // the link (0, 0) - (20, 5)); a Steiner vertex at (15, 3), in the pixel of the lower edge only (that link passes
                        // row 4 there), opens 7.5 px², and
                        // the mesh of that triangle without the upper edge's chain is rejected: a link must be collapsed in both roundings
                        var sliver = new Mesh2Input { SnapTolerance = 1, X = new double[] { 0, 20, 20 }, Y = new double[] { 0, 4.55, 5.45 } };
                        sliver.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                        var tsl = new[] { new[] { 0, 3, 1 } };
                        var osl = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 20, 20, 15 }, VY = new double[] { 0, 5, 5, 3 }, InputToVertex = new[] { 0, 1, 1 },
                            Triangles = tsl, Neighbours = NeighboursOf(tsl), TriangleLabels = new int[1], BadTriangles = new int[0],
                            Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 3), new SubsegmentOutput(3, 1) } };
                        var rsl = Oracle.Check(sliver, osl, m3);
                        Must("reject: a chain missing where an output vertex opens area the input's rounding collapses (rule (ii'), both roundings)",
                            rsl.Has(Violation.SegmentMissing), "report: " + rsl + " / " + rsl.ExcusedSummary());
                        // SRR's known discrepancy (Belussi et al. 2016): Listing 2 against Proposition 4.14. The segment (0, 0) - (10, 1.4)
                        // with the point (5, 0.4) gets the restore pixels (4, 1) and (3, 0); the segment (5.4, 3.4) - (3.4, 1.2) crosses the
                        // interior of (4, 1), but its link (3, 1) - (5, 3) only touches its corner. Listing 2 restores critical edges only,
                        // so that chain stays (3, 1) - (5, 3); SR on the grown set of hot pixels (Proposition 4.14, D14, the library) routes
                        // it through (4, 1). The oracle computes the restore pixels as the library does; it accepts the routed chain and
                        // rejects Listing 2's (ChainSkipsHotPixel: a hot pixel of the input's rounding its segment contacts is on its chain)
                        var srr = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 10, 5, 5.4, 3.4 }, Y = new[] { 0.0, 1.4, 0.4, 3.4, 1.2 } };
                        srr.Segments.Add(new SegmentInput(0, 1));
                        srr.Segments.Add(new SegmentInput(3, 4));
                        var tsr = new[] { new[] { 4, 5, 6 }, new[] { 3, 4, 6 }, new[] { 0, 5, 4 }, new[] { 2, 6, 5 }, new[] { 0, 4, 3 }, new[] { 1, 6, 2 }, new[] { 1, 3, 6 } };
                        Mesh2Output SrrOutput(List<SubsegmentOutput> chains) => new Mesh2Output { Status = Mesh2Status.Success,
                            VX = new double[] { 0, 10, 5, 5, 3, 3, 4 }, VY = new double[] { 0, 1, 0, 3, 1, 0, 1 }, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                            Triangles = tsr, Neighbours = NeighboursOf(tsr), TriangleLabels = new int[7], BadTriangles = new int[0], Subsegments = chains };
                        var s1 = new List<SubsegmentOutput> { new SubsegmentOutput(0, 5), new SubsegmentOutput(5, 6), new SubsegmentOutput(6, 1) };
                        var r414 = Oracle.Check(srr, SrrOutput(s1.Concat(new[] { new SubsegmentOutput(4, 6), new SubsegmentOutput(6, 3) }).ToList()), new OracleExpect { Milestone = Milestone.M2 });
                        Must("accept: SRR's restore pixels (4, 1) and (3, 0), a chain routed through a restore pixel its segment crosses (Proposition 4.14)",
                            r414.Ok && r414.RestorePixels.Count == 2 && r414.RestorePixels.Contains(((BigInteger)4, (BigInteger)1))
                                && r414.RestorePixels.Contains(((BigInteger)3, (BigInteger)0)),
                            "report: " + r414 + " restore " + string.Join(" ", r414.RestorePixels ?? new List<(BigInteger, BigInteger)>()));
                        var rl2 = Oracle.Check(srr, SrrOutput(s1.Concat(new[] { new SubsegmentOutput(4, 3) }).ToList()), new OracleExpect { Milestone = Milestone.M2 });
                        Must("reject: the same chain past that restore pixel's corner, as SRR's Listing 2 leaves it (its segment contacts the pixel) as ChainSkipsHotPixel",
                            rl2.Violations.Count == 2 && rl2.Violations.All(x => x.Kind == Violation.ChainSkipsHotPixel), "report: " + rl2 + " / " + rl2.ExcusedSummary());
                        // chains meet only at hot pixels of the input's rounding: two crossing segments, snapped, share the crossing's
                        // vertex (2, 2), four subsegments there; no coincidence, no pinch (they are judged at vertices of refinement only)
                        var xcross = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 4, 0, 4 }, Y = new[] { 0.0, 4, 4, 0 } };
                        xcross.Segments.Add(new SegmentInput(0, 1));
                        xcross.Segments.Add(new SegmentInput(2, 3));
                        var tcr = new[] { new[] { 0, 3, 4 }, new[] { 3, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 0, 4 } };
                        var ocr = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 4, 0, 4, 2 }, VY = new double[] { 0, 4, 4, 0, 2 }, InputToVertex = new[] { 0, 1, 2, 3 },
                            Triangles = tcr, Neighbours = NeighboursOf(tcr), TriangleLabels = new int[4], BadTriangles = new int[0],
                            Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 4), new SubsegmentOutput(4, 1), new SubsegmentOutput(2, 4), new SubsegmentOutput(4, 3) } };
                        var rcr = Oracle.Check(xcross, ocr, new OracleExpect { Milestone = Milestone.M2 });
                        Must("accept: two chains meeting at the hot pixel of their crossing (four subsegments there), no pinch listed",
                            rcr.Ok && rcr.KnownViolations.Count == 0, "report: " + rcr + " / " + rcr.ExcusedSummary());
                        // "on the chain" means on the piece's own chain: with a segment T = (3.4, 1.2) - (4.2, 1.0) added, (4, 1) is an input
                        // point's pixel that S = (5.4, 3.4) - (3.4, 1.2) contacts; T's subsegment from (4, 1) to S's end (3, 1) must not stand
                        // in for S's chain, which Listing 2 leaves at (3, 1) - (5, 3) (review of #36, S2)
                        var srr2 = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 10, 5, 5.4, 3.4, 4.2 }, Y = new[] { 0.0, 1.4, 0.4, 3.4, 1.2, 1.0 } };
                        srr2.Segments.Add(new SegmentInput(0, 1));
                        srr2.Segments.Add(new SegmentInput(3, 4));
                        srr2.Segments.Add(new SegmentInput(4, 5));
                        var tsr2 = new[] { new[] { 4, 6, 5 }, new[] { 3, 4, 5 }, new[] { 0, 6, 4 }, new[] { 2, 5, 6 }, new[] { 0, 4, 3 }, new[] { 1, 5, 2 }, new[] { 1, 3, 5 } };
                        var osr2 = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 10, 5, 5, 3, 4, 3 }, VY = new double[] { 0, 1, 0, 3, 1, 1, 0 }, InputToVertex = new[] { 0, 1, 2, 3, 4, 5 },
                            Triangles = tsr2, Neighbours = NeighboursOf(tsr2), TriangleLabels = new int[7], BadTriangles = new int[0],
                            Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 6), new SubsegmentOutput(6, 5), new SubsegmentOutput(5, 1), new SubsegmentOutput(4, 5), new SubsegmentOutput(4, 3) } };
                        var rs2 = Oracle.Check(srr2, osr2, new OracleExpect { Milestone = Milestone.M2 });
                        Must("reject: a chain past a contacted hot pixel whose vertex another chain shares with the piece's end as ChainSkipsHotPixel",
                            rs2.Has(Violation.ChainSkipsHotPixel), "report: " + rs2 + " / " + rs2.ExcusedSummary());
                        // the same with a second T (4.2, 1) - (5.4, 3.4) from the shared end: the subsegments of the two T chains at (4, 1)
                        // have neighbours on both sides of the pixel along the edge, but neither is the edge's chain (review of #36, S2)
                        srr2.Segments.Add(new SegmentInput(5, 3));
                        osr2.Subsegments.Add(new SubsegmentOutput(5, 3));
                        var rs2b = Oracle.Check(srr2, osr2, new OracleExpect { Milestone = Milestone.M2 });
                        Must("reject: a chain past a contacted hot pixel whose vertex two other chains share as ChainSkipsHotPixel",
                            rs2b.Has(Violation.ChainSkipsHotPixel), "report: " + rs2b + " / " + rs2b.ExcusedSummary());
                        // a vertex of refinement (5, 0) on the chain of (0, 0) - (8, 0): the chain of (0, 3) - (8, 1) rerouted onto its link
                        // (5, 0) - (8, 0) shares it from there (three subsegments at (5, 0)); and the two chains meeting in (5, 0) (four)
                        var two = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 8, 0, 8 }, Y = new[] { 0.0, 0, 3, 1 } };
                        two.Segments.Add(new SegmentInput(0, 1));
                        two.Segments.Add(new SegmentInput(2, 3));
                        var ttw = new[] { new[] { 0, 4, 2 }, new[] { 4, 1, 3 }, new[] { 4, 3, 2 } };
                        Mesh2Output Two(params int[] chain2) => new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 8, 0, 8, 5 }, VY = new double[] { 0, 0, 3, 1, 0 },
                            InputToVertex = new[] { 0, 1, 2, 3 }, Triangles = ttw, Neighbours = NeighboursOf(ttw), TriangleLabels = new int[3], BadTriangles = new int[0],
                            Subsegments = new[] { new SubsegmentOutput(0, 4), new SubsegmentOutput(4, 1) }.Concat(chain2.Zip(chain2.Skip(1), (x, y) => new SubsegmentOutput(x, y))).ToList() };
                        var m3plain = new OracleExpect { Milestone = Milestone.M3 };
                        var rco = Oracle.Check(two, Two(2, 4, 1, 3), m3plain);
                        Must("reject: two chains sharing a link from a vertex of refinement as RefinementCoincidence", rco.Has(Violation.RefinementCoincidence), "report: " + rco);
                        var rpi = Oracle.Check(two, Two(2, 4, 3), m3plain);
                        Must("reject: two chains meeting in a vertex of refinement as RefinementPinch",
                            rpi.Has(Violation.RefinementPinch) && !rpi.Has(Violation.RefinementCoincidence) && rpi.KnownViolations.Count == 0, "report: " + rpi + " / " + rpi.ExcusedSummary());
                        // the oracle's mirror of the mesher's pinch rule, no broader: a candidate split pixel is excluded only when it is no
                        // hot pixel of the input's rounding and a subsegment of another chain ends there
                        Must("grid split: a candidate where another chain's subsegment ends is excluded (pinch rule)", Oracle.PinchRuleExcludes(false, new[] { false }), "");
                        Must("grid split: a candidate on a hot pixel of the input's rounding stays demanded", !Oracle.PinchRuleExcludes(true, new[] { false }), "");
                        Must("grid split: a candidate where only the own chain's subsegments end stays demanded", !Oracle.PinchRuleExcludes(false, new[] { true, true }), "");
                        Must("grid split: a candidate where no subsegment ends stays demanded", !Oracle.PinchRuleExcludes(false, new bool[0]), "");
                        Must("grid split: own and another chain's subsegments at the candidate: excluded", Oracle.PinchRuleExcludes(false, new[] { true, false }), "");
                        // the M2 hull model keeps a snapped input point on the hull's boundary (a vertex of a boundary edge): the square with
                        // (5, 0) on its bottom edge, meshed without it, is rejected (review of #36, S1)
                        var sq = new Mesh2Input { SnapTolerance = 1, X = new[] { 0.0, 10, 10, 0, 5 }, Y = new[] { 0.0, 0, 10, 10, 0 } };
                        var tsq = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } };
                        var osq = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 0, 10, 10, 0, 5 }, VY = new double[] { 0, 0, 10, 10, 0 }, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                            Triangles = tsq, Neighbours = NeighboursOf(tsq), TriangleLabels = new int[2], BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
                        var rsq = Oracle.Check(sq, osq, new OracleExpect { Milestone = Milestone.M2 });
                        Must("reject: a snapped input point on the hull's boundary left out at M2 as VertexUnused", rsq.Has(Violation.VertexUnused), "report: " + rsq + " / " + rsq.ExcusedSummary());
                        // a hull domain's hull pieces are judged on the grid as a segment's (option C), within their own hull edge's
                        // envelope: the bottom hull edge (0, 0.5) - (6, 0.5) runs on the pixel boundary, and the oracle's tie rule for
                        // decimal pixels (blocking pixels a hair larger, the own envelope a hair smaller) puts no pixel between its ends in
                        // its envelope, so its piece (0, 1) - (6, 1) counts as having no split point (the mesher, with snap rounding's
                        // half-open pixels, does split it at (3, 1)); the triangle (0, 1), (6, 1), (3, 2) of the free point (3, 1.6), at
                        // 18.4 degrees at the hull's right-angled corners, is excused, its circumcentre (3, -3) behind that piece. Without
                        // the hull edge as the piece's owner the oracle took every hull piece as splittable (found with the zigzag of Q17:
                        // hull pieces the mesher may not split without collapsing the band). The near miss: the edge at 0.6 has the
                        // pixels of row 1 in its envelope, a split point, and the triangle is rejected
                        var hullFloor = new Mesh2Input { SnapTolerance = 1, MinAngleDeg = 20, X = new double[] { 0, 6, 6, 0, 3 }, Y = new double[] { 0.5, 0.5, 4, 4, 1.6 } };
                        var ohf = Snapped(hullFloor, new[] { new[] { 0, 1, 4 }, new[] { 0, 4, 3 }, new[] { 4, 1, 2 }, new[] { 4, 2, 3 } }, new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 } });
                        ohf.BadTriangles = new[] { 0 };
                        var rhf = Oracle.Check(hullFloor, ohf, m3);
                        Must("accept: a triangle below the angle whose circumcentre lies behind a hull piece with no split point on the grid", rhf.Ok, "report: " + rhf);
                        var hullSplittable = new Mesh2Input { SnapTolerance = 1, MinAngleDeg = 20, X = new double[] { 0, 6, 6, 0, 3 }, Y = new double[] { 0.6, 0.6, 4, 4, 1.6 } };
                        var ohs = Snapped(hullSplittable, new[] { new[] { 0, 1, 4 }, new[] { 0, 4, 3 }, new[] { 4, 1, 2 }, new[] { 4, 2, 3 } }, new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 } });
                        ohs.BadTriangles = new[] { 0 };
                        var rhs = Oracle.Check(hullSplittable, ohs, m3);
                        Must("reject: the same triangle when the hull piece has a split point (edge at 0.6)", rhs.Has(Violation.AngleBelowBound), "report: " + rhs);
                    }
                    // E8: a slit of the outside 0.5 wide and 10 deep from the bottom, its walls snapped to columns 10 and 11 (kept apart):
                    // filled, its tips dropped
                    var e8 = new Mesh2Input { X = new double[] { 0, 10, 10, 10.5, 10.5, 20, 20, 0 }, Y = new double[] { 0, 0, 10, 10, 0, 0, 20, 20 }, SnapTolerance = 1 };
                    e8.Rings.Add(new RingInput(Enumerable.Range(0, 8).ToArray(), false));
                    var keep = new List<int> { 0, 1, 4, 5, 6, 7 };
                    var kept = new Mesh2Input { X = keep.Select(i => Math.Floor(e8.X[i] + 0.5)).ToArray(), Y = keep.Select(i => Math.Floor(e8.Y[i] + 0.5)).ToArray() };
                    var e8t = DelaunayByDefinition(kept).Select(t => t.Select(j => keep[j]).ToArray()).ToArray();
                    var e8r = Oracle.Check(e8, Snapped(e8, e8t, new[] { new[] { 0, 1 }, new[] { 1, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 0 } }), m3);
                    Must("reject: a slit of the outside filled, its tips dropped (review E8)", e8r.Violations.Any(v => v.Kind == Violation.VertexUnused && v.Detail.StartsWith("input 2 ")) && NothingExcused(e8r), "report: " + e8r);
                }
                // "passes": crossing the interior or the centre; touching the boundary does not count
                Pt P(double a, double b) => Pt.FromDoubles(a, b);
                Q one = Q.One;
                Must("pixel: an edge through the interior passes", Oracle.PassesPixel(P(0, 0.4), P(3, 0.4), P(1, 0), one), null);
                Must("pixel: an edge along the boundary does not pass", !Oracle.PassesPixel(P(0, 0.5), P(3, 0.5), P(1, 0), one), null);
                Must("pixel: an edge through the centre passes", Oracle.PassesPixel(P(1, -3), P(1, 0), P(1, 0), one), null);
                Must("pixel: an edge ending at the boundary does not pass", !Oracle.PassesPixel(P(1.5, 0), P(3, 0), P(1, 0), one), null);
            }
            DomainAreas(Must);

            // ---- M1: a set in general position, solved by the definition itself
            var pts = Points(0, 0, 16, 1, 19, 9, 13, 17, 2, 15, -3, 7, 5, 4, 11, 6, 8, 11, 3, 10, 14, 12);
            var good = M1Output(pts);
            var goodReport = Oracle.Check(pts, good, M1());
            Must("accept: M1 in general position", goodReport.Ok, goodReport.ToString());
            Must("the M1 baseline is reported as unique and checked globally", goodReport.Unique && goodReport.GlobalDelaunayChecked);

            Mesh2Output B(Action<Mesh2Output> edit) { var o = Clone(good); edit(o); return o; }

            Reject("flipped triangle", Violation.NotPositive, pts, B(o => { var t = o.Triangles[0]; int s = t[0]; t[0] = t[1]; t[1] = s; }), M1());
            Reject("repeated vertex", Violation.NotPositive, pts, B(o => o.Triangles[0][2] = o.Triangles[0][0]), M1());
            Reject("index out of range", Violation.BadIndex, pts, B(o => o.Triangles[0][0] = 999), M1());
            Reject("negative index", Violation.BadIndex, pts, B(o => o.Triangles[0][0] = -1), M1());
            Reject("gap", Violation.AreaMismatch, pts, B(o => o.Triangles = o.Triangles.Skip(1).ToArray()), M1());
            Reject("gap, no neighbours", Violation.HullBoundary, pts, B(o => { o.Neighbours = null; o.Triangles = RemoveHullTriangle(o.Triangles); }), M1());
            Reject("repeated triangle", Violation.EdgeRepeated, pts, B(o => { o.Neighbours = null; o.Triangles = o.Triangles.Concat(new[] { o.Triangles[0] }).ToArray(); }), M1());
            Reject("wrong neighbour", Violation.NeighbourWrong, pts, B(o => o.Neighbours[0][0] = o.Neighbours[0][0] == 1 ? 2 : 1), M1());
            Reject("moved vertex", Violation.InputMoved, pts, B(o => o.VX[6] += 1.0 / 1024), M1());
            Reject("moved by one ulp", Violation.InputMoved, pts, B(o => o.VX[6] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(o.VX[6]) + 1)), M1());
            Reject("NaN vertex", Violation.NonFinite, pts, B(o => o.VX[0] = double.NaN), M1());
            Reject("infinite vertex", Violation.NonFinite, pts, B(o => o.VY[0] = double.PositiveInfinity), M1());
            Reject("mapping into nothing", Violation.MappingWrong, pts, B(o => o.InputToVertex[3] = 99), M1());
            Reject("mapping missing", Violation.MappingWrong, pts, B(o => o.InputToVertex = null), M1());
            Reject("merged distinct points", Violation.SnapTooFar, pts, B(o => o.InputToVertex[3] = 2), M1());
            Reject("Steiner vertex in M1", Violation.SteinerNotAllowed, pts, B(o => { o.VX = o.VX.Concat(new[] { 8.5 }).ToArray(); o.VY = o.VY.Concat(new[] { 3.5 }).ToArray(); }), M1());
            Reject("status Empty with triangles", Violation.StatusInconsistent, pts, B(o => o.Status = Mesh2Status.Empty), M1());
            Reject("status Success without triangles", Violation.StatusInconsistent, pts, B(o => { o.Triangles = new int[0][]; o.Neighbours = null; }), M1());
            Reject("Empty on an input with area", Violation.EmptyOnArea, pts, B(o => { o.Status = Mesh2Status.Empty; o.Triangles = new int[0][]; o.Neighbours = null; }), M1());
            Reject("Failed on a valid input", Violation.StatusInconsistent, pts, B(o => { o.Status = Mesh2Status.Failed; o.Triangles = new int[0][]; }), M1());
            Reject("a mesh where failure is required", Violation.StatusInconsistent, pts, B(o => { }), new OracleExpect { ExpectFailure = true });

            var flip = FlipSomeEdge(pts, good.Triangles);
            var flipped = B(o => { o.Triangles = flip.triangles; o.Neighbours = NeighboursOf(flip.triangles); });
            Reject("non-Delaunay edge, local check", Violation.NotDelaunayLocal, pts, flipped, new OracleExpect { GlobalDelaunayLimit = 0 });
            Reject("non-Delaunay edge, global check", Violation.NotDelaunayGlobal, pts, flipped, M1());

            // a vertex on an edge that only one side is split at
            var tj = Points(0, 0, 8, 0, 8, 8, 0, 8, 4, 0);
            var tj2 = Points(0, 0, 8, 0, 8, 8, 0, 8, 4, 4);
            Reject("T-junction, right area", Violation.VertexInTriangle, tj2, new Mesh2Output {
                Status = Mesh2Status.Success, VX = tj2.X, VY = tj2.Y, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 4, 3 }, new[] { 4, 2, 3 } } }, M1());
            Reject("unused input point", Violation.VertexUnused, tj, new Mesh2Output {
                Status = Mesh2Status.Success, VX = tj.X, VY = tj.Y, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } } }, M1());

            // two triangles of the right total area over a square, which overlap and leave a gap
            var square = Points(0, 0, 8, 0, 8, 8, 0, 8);
            var overlap = new Mesh2Output {
                Status = Mesh2Status.Success, VX = square.X, VY = square.Y, InputToVertex = new[] { 0, 1, 2, 3 },
                Triangles = new[] { new[] { 0, 2, 3 }, new[] { 1, 2, 3 } } };
            Reject("overlap with the right area", Violation.EdgesCross, square, overlap, M1());
            Must("that overlap really has the right area", Oracle.Check(square, overlap, M1()).Area2 == new Q(128, 1));
            var mirrored = Points(0, 0, 8, 0, 8, 8, 0, 8, 4, 4);
            Reject("interior vertex under a triangle", Violation.VertexInTriangle, mirrored, new Mesh2Output {
                Status = Mesh2Status.Success, VX = mirrored.X, VY = mirrored.Y, InputToVertex = new[] { 0, 1, 2, 3, 4 },
                Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 1, 4 } } }, M1());

            // duplicates and degenerate inputs
            var duplicates = Points(0, 0, 8, 0, 0, 8, 8, 0, -0.0, 0);
            var dupOutput = new Mesh2Output { Status = Mesh2Status.Success, VX = duplicates.X, VY = duplicates.Y, InputToVertex = new[] { 0, 1, 2, 1, 0 }, Triangles = new[] { new[] { 0, 1, 2 } } };
            Accept("duplicates, signed zero", duplicates, dupOutput, M1());
            var d2 = Clone(dupOutput); d2.Triangles = new[] { new[] { 3, 4, 2 } };
            Reject("coincident inputs map differently", Violation.MappingWrong, duplicates, new Mesh2Output { Status = Mesh2Status.Success, VX = duplicates.X, VY = duplicates.Y, InputToVertex = new[] { 0, 1, 2, 3, 0 }, Triangles = new[] { new[] { 0, 1, 2 } } }, M1());
            Reject("merged slot used by a triangle", Violation.AliasUsed, duplicates, d2, M1());
            var collinear = Points(0, 0, 1, 1, 2, 2, 3, 3);
            Accept("collinear input, Empty", collinear, new Mesh2Output { Status = Mesh2Status.Empty, VX = collinear.X, VY = collinear.Y, InputToVertex = new[] { 0, 1, 2, 3 } }, M1());
            Reject("triangles on a collinear input", Violation.TrianglesOnDegenerate, collinear, new Mesh2Output {
                Status = Mesh2Status.Empty, VX = collinear.X.Concat(new[] { 5.0 }).ToArray(), VY = collinear.Y.Concat(new[] { 0.0 }).ToArray(),
                InputToVertex = new[] { 0, 1, 2, 3 }, Triangles = new[] { new[] { 0, 4, 3 } } }, M1());

            // point location
            Reject("location outside the triangle", Violation.LocateWrong, pts, B(o => o.Locate = (x, y) => 0), M1());
            Reject("location misses", Violation.LocateWrong, pts, B(o => o.Locate = (x, y) => -1), M1());
            Accept("location by search", pts, B(o => o.Locate = (x, y) => {
                var q = Pt.FromDoubles(x, y);
                for (int i = 0; i < o.Triangles.Length; ++i) {
                    var t = o.Triangles[i];
                    Pt a = Pt.FromDoubles(o.VX[t[0]], o.VY[t[0]]), b = Pt.FromDoubles(o.VX[t[1]], o.VY[t[1]]), c = Pt.FromDoubles(o.VX[t[2]], o.VY[t[2]]);
                    if (Exact.Orient(a, b, q) >= 0 && Exact.Orient(b, c, q) >= 0 && Exact.Orient(c, a, q) >= 0) return i;
                }
                return -1;
            }), M1());

            // ---- M2: the grid baseline, and a mesh with an edge kept against the Delaunay rule by a constraint
            var (gridIn, gridOut) = Grid(false);
            Accept("M2 grid: rings, hole, segments, regions", gridIn, gridOut, M2());
            Must("the grid has ties, so it is not unique", !Oracle.Check(gridIn, gridOut, M2()).Unique);
            Mesh2Output G(Action<Mesh2Output> edit) { var o = Clone(gridOut); edit(o); return o; }

            Reject("no subsegments reported", Violation.MissingReport, gridIn, G(o => o.Subsegments = null), M2());
            Reject("no neighbours reported", Violation.MissingReport, gridIn, G(o => o.Neighbours = null), M2());
            Reject("no labels reported", Violation.MissingReport, gridIn, G(o => o.TriangleLabels = null), M2());
            Reject("segment lost", Violation.SegmentMissing, gridIn, G(o => o.Subsegments.RemoveAll(s => s.Marker == 5)), M2());
            Reject("one piece of a segment lost", Violation.SegmentMissing, gridIn, G(o => { var piece = o.Subsegments.First(s => s.Marker == 6); o.Subsegments.Remove(piece); }), M2());
            Reject("subsegment that is no edge", Violation.SubsegmentNotEdge, gridIn, G(o => o.Subsegments.Add(new SubsegmentOutput(0, 2, 0))), M2());
            Reject("subsegment on no constraint", Violation.SubsegmentUnsourced, gridIn, G(o => o.Subsegments.Add(FreeEdge(o, 14))), M2());
            Reject("wrong marker", Violation.SubsegmentMarker, gridIn, G(o => o.Subsegments[0].Marker = 9), M2());
            Reject("hole boundary unconstrained", Violation.BoundaryNotConstrained, gridIn, G(o => o.Subsegments.RemoveAll(s => s.Marker == 2)), M2());
            // ---- a hull corner merged within the tube (O11, D13): a point set whose hull corner c lies just below the bottom edge and is
            // merged into its twin just above it; the boundary runs straight past c, cutting the corner by the tube at most (found with
            // the corpus run, delaunator's robustness1). With c farther than the tube it is a real corner, and the boundary may not skip it.
            {
                double d = 1.0 / (1 << 24);
                Q mergeTube = new Q(1, 1 << 20);
                (Mesh2Input, Mesh2Output) CutCorner(double below)
                {
                    var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 5, 5, 5 }, Y = new double[] { 0, 0, 10, 10, -below, d, 5 } };
                    int[][] tris = { new[] { 0, 1, 5 }, new[] { 1, 6, 5 }, new[] { 1, 2, 6 }, new[] { 2, 3, 6 }, new[] { 3, 0, 6 }, new[] { 0, 5, 6 } };
                    var output = new Mesh2Output {
                        Status = Mesh2Status.Success, VX = input.X.ToArray(), VY = input.Y.ToArray(),
                        InputToVertex = new[] { 0, 1, 2, 3, below <= d ? 5 : 4, 5, 6 },   // (the corner merged into its twin only in the accept case)
                        Triangles = tris, TriangleLabels = tris.Select(_ => 0).ToArray(), Neighbours = NeighboursOf(tris),
                        Subsegments = new List<SubsegmentOutput>(), BadTriangles = new int[0]
                    };
                    return (input, output);
                }
                var (mergedIn, mergedOut) = CutCorner(d);
                Accept("a hull corner merged within the tube, the boundary past it", mergedIn, mergedOut, M2(mergeTube));
                // (just outside the window: the corner twice the tube from the edge)
                var (cornerIn, cornerOut) = CutCorner(1.0 / (1 << 19));
                Reject("a real hull corner skipped by the boundary", Violation.BoundaryNotConstrained, cornerIn, cornerOut, M2(mergeTube));
            }

            Reject("hole boundary lost as a chain", Violation.SegmentMissing, gridIn, G(o => o.Subsegments.RemoveAll(s => s.Marker == 2)), M2());
            Reject("wrong region label", Violation.RegionLabelWrong, gridIn, G(o => o.TriangleLabels[0] = 7), M2());
            Reject("seed region not flooded", Violation.RegionLabelWrong, gridIn, G(o => { for (int i = 0; i < o.TriangleLabels.Length; ++i) if (o.TriangleLabels[i] == 4) o.TriangleLabels[i] = 0; }), M2());
            Reject("triangle lost", Violation.AreaMismatch, gridIn, G(o => { o.Neighbours = null; o.TriangleLabels = null; o.Triangles = o.Triangles.Skip(1).ToArray(); }), M2());

            // a triangle moved from the domain into the hole: the same area, the same counts
            int h33 = 4, h53 = 5, h55 = 6;
            var filled = G(o => {
                o.Neighbours = NeighboursOf(o.Triangles);
                int last = o.Triangles.Length - 1;
                o.Triangles[last] = new[] { h33, h53, h55 };
                o.Neighbours = NeighboursOf(o.Triangles);
            });
            var gridStated = new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, Removed = { new[] { 4.5, 3.5 } }, Kept = { new[] { 1.25, 1.75 } } };
            Reject("triangle in the hole, right area", Violation.TriangleOutsideDomain, gridIn, filled, gridStated);
            Reject("point of the hole covered", Violation.RemovedPointCovered, gridIn, filled, gridStated);
            Reject("kept point not covered", Violation.KeptPointMissing, gridIn, G(o => { o.TriangleLabels = null; o.Neighbours = null; o.Triangles = o.Triangles.Where(t => !t.Contains(0)).ToArray(); }),
                new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, Kept = { new[] { 0.25, 0.125 } } });
            Reject("area against the specification", Violation.AreaMismatch, gridIn, gridOut, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, StatedArea = new Q(61, 1) });

            // CDT: an illegal edge is correct exactly when it is a constraint, unless conforming is asked for
            var cdt = Points(pts.X.SelectMany((x, i) => new[] { x, pts.Y[i] }).ToArray());
            cdt.Segments.Add(new SegmentInput(flip.a, flip.b, 3));
            var cdtOut = Clone(flipped);
            cdtOut.Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(flip.a, flip.b, 3) };
            Accept("M2: an edge that is illegal but constrained", cdt, cdtOut, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            var plain = Clone(flipped); plain.Subsegments = new List<SubsegmentOutput>();
            Reject("M2: an illegal free edge", Violation.NotDelaunayLocal, pts, plain, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            var cdtConforming = Points(cdt.X.SelectMany((x, i) => new[] { x, cdt.Y[i] }).ToArray());
            cdtConforming.Segments = cdt.Segments; cdtConforming.Conforming = true;
            Reject("M3: conforming asked, illegal constrained edge", Violation.NotDelaunayLocal, cdtConforming, cdtOut, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            // the constrained quality mode (owner decision 2026-11-02): quality without O2, judged as constrained Delaunay. The same mesh
            // with an illegal but constrained edge passes there and fails with quality in the default mode, which demands O2
            var cdtQuality = Points(cdt.X.SelectMany((x, i) => new[] { x, cdt.Y[i] }).ToArray());
            cdtQuality.Segments = cdt.Segments; cdtQuality.MinAngleDeg = 0;
            var cdtQualityOut = Clone(cdtOut); cdtQualityOut.BadTriangles = new int[0];
            Reject("M3: quality, default mode (O2), illegal constrained edge", Violation.NotDelaunayLocal, cdtQuality, cdtQualityOut, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            cdtQuality.ConstrainedQuality = true;
            Accept("M3: quality, constrained mode, an edge that is illegal but constrained", cdtQuality, cdtQualityOut, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });

            // a segment crossing another one: rational crossing vertex; judged on exact vertices, and with doubles only inside a tube
            var cross = Points(0, 0, 6, 0, 6, 6, 0, 6, 1, 1, 4, 5, 1, 4, 5, 1);
            cross.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            cross.Segments.Add(new SegmentInput(4, 5, 1));
            cross.Segments.Add(new SegmentInput(6, 7, 2));
            CrossingAcceptance(Must, cross);

            // locked segment, split at a free point, on the Steiner grid baseline
            var (steinerIn, steinerOut) = Grid(true);
            Accept("M3 grid: constructed vertices at or above the input count", steinerIn, steinerOut, M3());
            Must("M3 grid has constructed vertices", Oracle.Check(steinerIn, steinerOut, M3()).ConstructedVertices > 0);
            Reject("M1 rules on that grid", Violation.SteinerNotAllowed, steinerIn, steinerOut, new OracleExpect { Milestone = Milestone.M1, Tube = Q.Zero });
            Mesh2Input With(Mesh2Input input, Action<Mesh2Input> edit)
            {
                var copy = new Mesh2Input {
                    X = input.X, Y = input.Y, Segments = input.Segments, Rings = input.Rings, Regions = input.Regions, HoleSeeds = input.HoleSeeds,
                    MinAngleDeg = input.MinAngleDeg, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit, SnapTolerance = input.SnapTolerance, Conforming = input.Conforming };
                edit(copy);
                return copy;
            }
            var lockedInput = With(steinerIn, i => i.Segments = i.Segments.Select(s => new SegmentInput(s.A, s.B, s.Marker, s.Marker == 5)).ToList());
            Reject("locked segment split at a free point", Violation.LockedSplit, lockedInput, steinerOut, M3());
            Accept("locked segment split at an input point", With(gridIn, i => i.Segments = i.Segments.Select(s => new SegmentInput(s.A, s.B, s.Marker, s.Marker == 5)).ToList()), gridOut, M3());

            // M3 quality bounds
            Mesh2Output S(Action<Mesh2Output> edit) { var o = Clone(steinerOut); edit(o); return o; }
            int all = steinerOut.Triangles.Length;
            var allBad = Enumerable.Range(0, all).ToArray();
            Accept("angle bound 30 degrees holds", With(steinerIn, i => i.MinAngleDeg = 30), steinerOut, M3());
            Accept("angle bound 44 degrees holds", With(steinerIn, i => i.MinAngleDeg = 44), steinerOut, M3());
            Reject("angle bound 46 degrees broken", Violation.AngleBelowBound, With(steinerIn, i => i.MinAngleDeg = 46), S(o => o.BadTriangles = allBad), M3());
            Reject("bad list incomplete", Violation.BadListWrong, With(steinerIn, i => i.MinAngleDeg = 46), steinerOut, M3());
            Reject("bad list with a good triangle", Violation.BadListWrong, With(steinerIn, i => i.MinAngleDeg = 30), S(o => o.BadTriangles = new[] { 0 }), M3());
            Reject("bad list not reported", Violation.MissingReport, With(steinerIn, i => i.MinAngleDeg = 30), S(o => o.BadTriangles = null), M3());
            Accept("Steiner limit reached lifts the bound", With(steinerIn, i => { i.MinAngleDeg = 46; i.SteinerLimit = 100; }), S(o => { o.BadTriangles = allBad; o.SteinerLimitReached = true; }), M3());
            Reject("Steiner limit exceeded", Violation.SteinerLimitExceeded, With(steinerIn, i => i.SteinerLimit = 3), steinerOut, M3());
            Reject("Steiner flag without refinement", Violation.SteinerFlagWrong, steinerIn, S(o => o.SteinerLimitReached = true), M2());
            Accept("area bound 1/2 holds", With(steinerIn, i => i.MaxArea = 0.5), steinerOut, M3());
            Reject("area bound 1/4 broken", Violation.AreaAboveBound, With(steinerIn, i => i.MaxArea = 0.25), steinerOut, M3());
            Reject("area bound per region broken", Violation.AreaAboveBound, With(steinerIn, i => i.Regions = new List<RegionInput> { i.Regions[0], new RegionInput { Seed = i.Regions[1].Seed, Label = 4, MaxArea = 0.25 } }), steinerOut, M3());
            Accept("size bound 3/2 holds", With(steinerIn, i => i.SizeFunction = (x, y) => new Q(3, 2)), steinerOut, M3());
            Reject("size bound 5/4 broken", Violation.SizeAboveBound, With(steinerIn, i => i.SizeFunction = (x, y) => new Q(5, 4)), steinerOut, M3());
            // O5: a triangle that cannot shrink because its boundary segments must not be split
            var big = Points(0, 0, 64, 0, 32, 8);
            big.MaxArea = 1;
            for (int i = 0; i < 3; ++i) big.Segments.Add(new SegmentInput(i, (i + 1) % 3, 0, locked: true));
            var bigOut = new Mesh2Output {
                Status = Mesh2Status.Success, VX = big.X, VY = big.Y, InputToVertex = new[] { 0, 1, 2 }, Triangles = new[] { new[] { 0, 1, 2 } },
                Neighbours = new[] { new[] { -1, -1, -1 } }, BadTriangles = new int[0],
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 0) } };
            Accept("locked segments exempt the area bound", big, bigOut, M3());
            var bigFree = Points(0, 0, 64, 0, 32, 8);
            bigFree.MaxArea = 1;
            for (int i = 0; i < 3; ++i) bigFree.Segments.Add(new SegmentInput(i, (i + 1) % 3));
            Reject("splittable segments do not", Violation.AreaAboveBound, bigFree, bigOut, M3());

            // a small angle forced by two segments is no failure; the same triangle without them is
            var needle = Points(0, 0, 64, 0, 64, 1);
            var needleOut = new Mesh2Output {
                Status = Mesh2Status.Success, VX = needle.X, VY = needle.Y, InputToVertex = new[] { 0, 1, 2 }, Triangles = new[] { new[] { 0, 1, 2 } },
                Neighbours = new[] { new[] { -1, -1, -1 } }, BadTriangles = new[] { 0 },
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1, 0), new SubsegmentOutput(1, 2, 0), new SubsegmentOutput(2, 0, 0) } };
            needle.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            needle.MinAngleDeg = 20;
            Accept("forced small input angle", needle, needleOut, M3());
            // not forced: a 64 x 1 rectangle cut by its diagonal; the wedge of the constraints at each corner is 90 degrees. (Until the second
            // review this item used the needle without a ring, whose 0.9 degree corner is a hull corner and so forced by the input: D13.)
            var strip = Points(0, 0, 64, 0, 64, 1, 0, 1); strip.MinAngleDeg = 20;
            strip.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            var stripTriangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } };
            var stripOut = new Mesh2Output {
                Status = Mesh2Status.Success, VX = strip.X, VY = strip.Y, InputToVertex = new[] { 0, 1, 2, 3 }, Triangles = stripTriangles,
                Neighbours = NeighboursOf(stripTriangles), BadTriangles = new[] { 0, 1 },
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 3), new SubsegmentOutput(3, 0) } };
            Reject("a small angle, not forced", Violation.AngleBelowBound, strip, stripOut, M3());

            ReviewFindings(Must, Accept, Reject);
            SecondReviewFindings(Must, Accept, Reject);
            M3ReviewFindings(Must, Accept, Reject);
            SnapGateFindings(Must, Accept, Reject);
            return items;
        }

        static int[][] StrictDelaunay(Mesh2Input input, int[] ids)
        {
            var p = ids.Select(i => Pt.FromDoubles(input.X[i], input.Y[i])).ToArray();
            var list = new List<int[]>();
            for (int a = 0; a < p.Length; ++a) for (int b = 0; b < p.Length; ++b) for (int c = 0; c < p.Length; ++c) {
                if (!(a < b && a < c) || b == c || Exact.Orient(p[a], p[b], p[c]) <= 0) continue;
                bool empty = true;
                for (int d = 0; d < p.Length && empty; ++d) if (d != a && d != b && d != c && Exact.InCircle(p[a], p[b], p[c], p[d]) >= 0) empty = false;
                if (empty) list.Add(new[] { ids[a], ids[b], ids[c] });
            }
            return list.ToArray();
        }

        // Known-wrong results for the oracle relaxations made with M3, which the review of M3 found without a negative test
        // (checklist 10a). They stay as regressions.
        /// <summary>
        /// The m2.snap gates (D14, owner 2026-11-01; docs/design/MESHING2D_SNAP.md section 3c): with an explicit tolerance an input point
        /// may move by at most tol/2 per axis, and merged input slots (aliases) do not count against a vertex bound.
        /// </summary>
        static void SnapGateFindings(Action<string, bool, string> must, Action<string, Mesh2Input, Mesh2Output, OracleExpect> accept, Action<string, Violation, Mesh2Input, Mesh2Output, OracleExpect> reject)
        {
            Mesh2Output Out(Mesh2Input input, int[][] tris) => new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                Triangles = tris, Neighbours = NeighboursOf(tris), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            Mesh2Output OutV(Mesh2Input input, double[] vx, double[] vy, int[] map, int[][] tris) => new Mesh2Output {
                Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = map, Triangles = tris, Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 3), new SubsegmentOutput(3, 0) } };
            // 0. a hull tip narrower than a pixel may collapse (owner decision 2026-11-02, Q-H4): the corner (0, 0) of a hull triangle of
            //    7.1 degrees (pixel 1/4) is in no triangle; accepted when an output vertex lies in a pixel both hull edges at the corner
            //    pass ((3/4, 0): the edge to (10, 5/4) passes it at height 3/32), not when the next vertex is beyond that ((2, 0): the edge
            //    passes 1/4 above its pixel's centre), nor without a tolerance
            foreach (var (qx, snapped, ok, what) in new[] { (0.75, true, true, "a hull tip collapsed through a pixel both hull edges pass"),
                                                          (2.0, true, false, "a hull corner in no triangle, the next vertex in a pixel only one hull edge passes"),
                                                          (0.75, false, false, "a hull corner in no triangle without a tolerance") }) {
                var input = Points(0, 0, 10, 0, 10, 1.25, 10, 0.5);
                input.MinAngleDeg = 20;
                if (snapped) input.SnapTolerance = 0.25;
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = new[] { 0, 10, 10, 10, qx }, VY = new[] { 0, 0, 1.25, 0.5, 0 }, InputToVertex = new[] { 0, 1, 2, 3 },
                    Triangles = new[] { new[] { 4, 1, 3 }, new[] { 4, 3, 2 } }, BadTriangles = new[] { 0, 1 }, Subsegments = new List<SubsegmentOutput>() };
                o.Neighbours = NeighboursOf(o.Triangles);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                bool unused = report.Violations.Any(x => x.Kind == Violation.VertexUnused && x.Detail.StartsWith("input 0 "));
                must((ok ? "accept: " : "reject: ") + what, unused != ok, "report: " + report);
            }

            // 0b. ... but not a 90 degree corner whose neighbour's pixel a hull edge only touches at its boundary (the square
            //     (0.5, 0.5) - (10.5, 10.5) with points (10.4, 10.5) and (10.5, 10.4), pixel 1: the corner (11, 11) in no triangle; the
            //     edge y = 10.5 only touches the pixel of (10, 11); review of the rule)
            {
                var input = Points(0.5, 0.5, 10.5, 0.5, 10.5, 10.5, 0.5, 10.5, 10.4, 10.5, 10.5, 10.4);
                input.MinAngleDeg = 20;
                input.SnapTolerance = 1;
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = new double[] { 1, 11, 11, 1, 10, 11 }, VY = new double[] { 1, 1, 11, 11, 11, 10 },
                    InputToVertex = new[] { 0, 1, 2, 3, 4, 5 }, Triangles = new[] { new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 0, 4, 3 } }, BadTriangles = new int[0],
                    Subsegments = new List<SubsegmentOutput>() };
                o.Neighbours = NeighboursOf(o.Triangles);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must("reject: a 90 degree hull corner in no triangle, a hull edge touching the next pixel at its boundary",
                    report.Violations.Any(x => x.Kind == Violation.VertexUnused && x.Detail.StartsWith("input 2 ")), "report: " + report);
            }

            // 1. an input point lands on the centre of its pixel (grid of pixel tol, anchored at the origin, half-open pixels): (dx, dy) is
            //    the input's offset from (5, 5), (ox, oy) the output's, both in units of tol
            foreach (var (dx, dy, ox, oy, snap, ok, what) in new[] {
                (0.4, -0.4, 0.0, 0.0, true, true, "an input point 0.4 tol off the grid, on its pixel centre (D14)"),
                (0.6, -0.6, 0.0, 0.0, true, false, "an input point 0.6 tol off the grid, on the neighbouring pixel's centre"),
                (0.5, 0.0, 1.0, 0.0, true, true, "an input point exactly between two pixels, on the upper one's centre (half-open pixels)"),
                (0.5, 0.0, 0.0, 0.0, true, false, "an input point exactly between two pixels, on the lower one's centre"),
                (0.0, 0.0, 0.5, 0.5, true, false, "an input point on the grid moved by half a pixel (a grid anchored off the origin)"),
                (0.4, -0.4, 0.0, 0.0, false, false, "an input point moved without an explicit tolerance") }) {
                double tol = 1.0 / 16;
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 5 + dx * tol, 5 + dy * tol);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                if (snap) input.SnapTolerance = tol;
                var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } };
                var o = OutV(input, new double[] { 0, 10, 10, 0, 5 + ox * tol }, new double[] { 0, 0, 10, 10, 5 + oy * tol }, new[] { 0, 1, 2, 3, 4 }, tris);
                if (ok) accept(what, input, o, new OracleExpect { Milestone = Milestone.M2 });
                else reject(what, snap ? Violation.SnapTooFar : Violation.InputMoved, input, o, new OracleExpect { Milestone = Milestone.M2, Tube = new Q(1, 4) });
            }
            // 2. a hole below the tolerance merged into one point: its three aliases do not count against the bound of 5 vertices, a bound
            //    of 4 still rejects it
            foreach (int bound in new[] { 5, 4 }) {
                double h = Math.Pow(2, -20);
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 5, 5, 5 + h, 5, 5 + h, 5 + h, 5, 5 + h);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
                input.SnapTolerance = Math.Pow(2, -10);
                var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } };
                var o = OutV(input, (double[])input.X.Clone(), (double[])input.Y.Clone(), new[] { 0, 1, 2, 3, 4, 4, 4, 4 }, tris);
                var e = new OracleExpect { Milestone = Milestone.M2, StatedArea = new Q(100, 1), StatedAreaTolerance = new Q(1, 16), MaxVertices = bound };
                if (bound == 5) accept("a collapsed hole: merged slots are no vertices for the bound", input, o, e);
                else reject("a collapsed hole: the bound still counts the survivors", Violation.CountOutOfRange, input, o, e);
            }
            // 4. M2 with an explicit tolerance: a vertex at a pixel centre on a chain in the envelope of its constraint (a restore pixel of
            //    D14) is allowed; one off the grid is refused, and one on the grid but outside the envelope is an invented vertex
            foreach (var (vx, vy, ok) in new[] { (8.0, 3.5, true), (8.0, 3.25, false), (8.0, 4.5, false) }) {
                double tol = 0.5;
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16, 2, 3, 14, 4);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5));
                input.SnapTolerance = tol;
                // the chain 4 - 6 - 5 bends through the extra vertex 6, within half a pixel of the segment (y = 3.5 there)
                var vxs = input.X.Concat(new[] { vx }).ToArray(); var vys = input.Y.Concat(new[] { vy }).ToArray();
                var all = Enumerable.Range(0, 7).ToArray();
                var pts = new Mesh2Input { X = vxs, Y = vys };
                var tris = StrictDelaunay(pts, all);
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vxs, VY = vys, InputToVertex = Enumerable.Range(0, 6).ToArray(), Triangles = tris,
                    Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                    Subsegments = new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 6), (6, 5) }.Select(q => new SubsegmentOutput(q.Item1, q.Item2)).ToList() };
                var e = new OracleExpect { Milestone = Milestone.M2 };
                if (ok) accept("M2 with a tolerance: a restore vertex at a pixel centre near its constraint (D14)", input, o, e);
                else if (vy == 3.25) reject("M2 with a tolerance: a vertex off the grid is refused", Violation.OffGrid, input, o, e);
                else reject("M2 with a tolerance: a grid vertex outside the envelope is invented", Violation.SteinerNotAllowed, input, o, e);
            }
            // 5. refinement on the grid (section 3g): a constructed vertex off the grid is refused; a skinny triangle whose shortest edge is
            //    two pixels is exempt (the floor), one of sqrt(5) pixels is judged, unless its circumcentre's pixel holds a vertex
            //    (grid-blocked)
            foreach (var (vx, vy, ok) in new[] { (5.25, 5.25, true), (5.1, 5.25, false) }) {
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.SnapTolerance = 0.25; input.MinAngleDeg = 0;
                var vxs = input.X.Concat(new[] { vx }).ToArray(); var vys = input.Y.Concat(new[] { vy }).ToArray();
                var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } };
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vxs, VY = vys, InputToVertex = Enumerable.Range(0, 4).ToArray(), Triangles = tris,
                    Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                    Subsegments = new[] { (0, 1), (1, 2), (2, 3), (3, 0) }.Select(q => new SubsegmentOutput(q.Item1, q.Item2)).ToList() };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must(ok ? "grid: a constructed vertex on the grid" : "grid: a constructed vertex off the grid is refused", report.Has(Violation.OffGrid) != ok, "report: " + report);
            }
            foreach (var (pts, blocker, ok, what) in new[] {
                (new[] { 5.0, 5, 5.5, 5, 8, 5.25 }, false, true, "grid: a skinny triangle with a shortest edge of two pixels is exempt (floor)"),
                (new[] { 5.0, 5, 9, 5.5, 5.5, 5.25 }, false, false, "grid: a skinny triangle with a shortest edge of sqrt(5) pixels is judged"),
                (new[] { 5.0, 5, 9, 5.5, 5.5, 5.25 }, true, true, "grid: the same with a vertex at its circumcentre's pixel is exempt (grid-blocked)") }) {
                double w = 0.25;
                var xy = new List<double> { 0, 0, 16, 0, 16, 16, 0, 16 };
                xy.AddRange(pts);
                if (blocker) {
                    // the exact circumcentre of the triangle, rounded to its pixel centre
                    Pt a = Pt.FromDoubles(pts[0], pts[1]), b = Pt.FromDoubles(pts[2], pts[3]), c = Pt.FromDoubles(pts[4], pts[5]);
                    Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                    double ux = double.Parse((a.Qx + (cy * b2 - by * c2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
                    double uy = double.Parse((a.Qy + (bx * c2 - cx * b2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
                    xy.Add(Math.Floor(ux / w + 0.5) * w); xy.Add(Math.Floor(uy / w + 0.5) * w);
                }
                var input = Points(xy.ToArray());
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.SnapTolerance = w; input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 4, 5, 6 } }); o.BadTriangles = new[] { 0 };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must(what, report.Has(Violation.AngleBelowBound) != ok, "report: " + report);
            }
            // 6. the exemptions for a subsegment with no split point on the grid do not reach a piece that has one: the segment (5, 5)-(9, 5)
            //    at pixel 1/4 has twelve pixel centres between its ends. A skinny triangle on it whose circumcentre lies behind it is judged,
            //    and so is a vertex behind it inside the circumcircle of the triangle on the other side (conforming at M3)
            {
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16, 5, 5, 9, 5, 7, 5.25, 7, 4.5);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5));
                input.SnapTolerance = 0.25; input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 4, 5, 6 } }); o.BadTriangles = new[] { 0 };
                o.Subsegments.Add(new SubsegmentOutput(4, 5));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must("grid: a skinny triangle whose circumcentre lies behind a splittable subsegment is judged", report.Has(Violation.AngleBelowBound), "report: " + report);
                o = Out(input, new[] { new[] { 4, 5, 6 }, new[] { 5, 4, 7 } }); o.BadTriangles = new[] { 0, 1 };
                o.Subsegments.Add(new SubsegmentOutput(4, 5));
                report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must("grid: a vertex behind a splittable subsegment inside a circumcircle is not Delaunay", report.Has(Violation.NotDelaunayGlobal), "report: " + report);
            }
            // 5b. grid-blocked by a constraint: the same skinny triangle with a constraint through its circumcentre's pixel (a vertex there
            //     would sit within half a pixel of it, D14) is exempt; with the constraint a pixel away it is judged (review of #277)
            foreach (var (offset, ok, what) in new[] { (0.0, true, "grid: a skinny triangle whose circumcentre's pixel a constraint crosses is exempt (grid-blocked)"),
                                                       (0.25, false, "grid: the same with the constraint a pixel away is judged") }) {
                double w = 0.25;
                double[] pts = { 5.0, 5, 9, 5.5, 5.5, 5.25 };
                Pt a = Pt.FromDoubles(pts[0], pts[1]), b = Pt.FromDoubles(pts[2], pts[3]), c = Pt.FromDoubles(pts[4], pts[5]);
                Q bx = b.Qx - a.Qx, by = b.Qy - a.Qy, cx = c.Qx - a.Qx, cy = c.Qy - a.Qy, d = (bx * cy - by * cx) * 2, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                double ux = double.Parse((a.Qx + (cy * b2 - by * c2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
                double uy = double.Parse((a.Qy + (bx * c2 - cx * b2) / d).ToDecimal(20), System.Globalization.CultureInfo.InvariantCulture);
                double px = Math.Floor(ux / w + 0.5) * w + offset, py = Math.Floor(uy / w + 0.5) * w;
                // a diagonal of three pixels through the pixel centre (px, py), its ends on the grid
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16, pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], px - 3 * w, py - 3 * w, px + 3 * w, py + 3 * w);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(7, 8));
                input.SnapTolerance = w; input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 4, 5, 6 } }); o.BadTriangles = new[] { 0 };
                o.Subsegments.Add(new SubsegmentOutput(7, 8));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must(what, report.Has(Violation.AngleBelowBound) != ok, "report: " + report);
            }
            // 5c. the pinch rule in GridUnsplittable, at its call site (review of the pinch fix: the static rule alone did not catch a call
            //     that passes the wrong arguments). The subsegment (3, 13)-(9, 13) at pixel 1 has one candidate split pixel, (6, 13): a vertex
            //     there makes the new edges of every other candidate cross its pixel. A skinny triangle on the piece has its circumcentre
            //     behind it. The candidate is excluded, and the angle excused, only when a subsegment of another chain ends there and it is
            //     no hot pixel of the input's rounding; it stays demanded with a free vertex there, on an input point (a hot pixel), and
            //     where only the piece's own chain ends
            foreach (var (kind, excused, what) in new[] {
                ("other", true, "grid split: a candidate where another chain's subsegment ends is excluded (pinch rule), the angle excused"),
                ("free", false, "grid split: a candidate on a free vertex of refinement stays demanded"),
                ("hot", false, "grid split: a candidate on a hot pixel of the input's rounding stays demanded, another chain's subsegment there or not"),
                ("near", false, "grid split: a candidate on a free vertex stays demanded when another chain ends in the next pixel, not in its own") }) {
                // 0-3 the square, 4-5 the piece's input segment, 6 the apex, 7-8 another input segment (6, 18)-(6, 22); 9 the vertex at the
                // candidate (an input point for "hot")
                var xy = new List<double> { 0, 0, 24, 0, 24, 24, 0, 24, 3, 13, 9, 13, 6, 14, 6, 18, 6, 22 };
                if (kind == "hot") xy.AddRange(new[] { 6.0, 13 });
                var input = Points(xy.ToArray());
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5));
                input.Segments.Add(new SegmentInput(7, 8));
                input.SnapTolerance = 1; input.MinAngleDeg = 20;
                var vx = xy.Where((_, i) => i % 2 == 0).ToList(); var vy = xy.Where((_, i) => i % 2 == 1).ToList();
                if (kind != "hot") { vx.Add(6); vy.Add(13); }
                if (kind == "near") { vx.Add(7); vy.Add(14); }   // 10: another chain's end, diagonally next to the candidate
                var tri = new[] { new[] { 4, 5, 6 } };
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vx.ToArray(), VY = vy.ToArray(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                    Triangles = tri, Neighbours = NeighboursOf(tri), BadTriangles = new[] { 0 }, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(4, 5) } };
                if (kind == "other" || kind == "hot") o.Subsegments.Add(new SubsegmentOutput(9, 7));
                if (kind == "near") o.Subsegments.Add(new SubsegmentOutput(10, 7));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must(what, report.Has(Violation.AngleBelowBound) != excused, "report: " + report);
            }
            // 5d. the piece's own chain is the stretch of the output's subsegment graph through non-hot degree-2 vertices (OwnStretch),
            //     not the subsegments in the input segment's envelope. The piece (3, 13)-(9, 13) of an input segment along y = 13 has one
            //     candidate, (6, 13) (a vertex there blocks the others, as in 5c); the skinny triangle (3, 13), (9, 13), (5, 14) is judged
            //     when the candidate stays demanded. Chain layouts (hot ends (0, 13) / (15, 13) or a second input segment):
            //       own-b     the chain runs on from the piece's end (9, 13) through (10, 13) and back through (6, 13): the subsegment at
            //                 the candidate is the piece's own, the candidate stays demanded
            //       other     the chain ends at the hot pixel (12, 13); a second input segment (12, 13)-(0, 13.3), also within a pixel
            //                 along the piece, runs on through a vertex at (6, 13): another chain's (the envelope approximation counted it
            //                 as the piece's own), the candidate is excluded, the angle excused
            //       branch    as own-b with a third subsegment at (10, 13): the stretch ends at the branching vertex, so what lies beyond
            //                 is another chain's, excused
            //     Each of the dropped pieces of OwnStretch (the walk from either end, the hot-pixel stop, the degree test) flips one
            foreach (var (kind, excused, what) in new[] {
                ("own-b", false, "grid split: a candidate where only the piece's own chain (its stretch of the graph) ends stays demanded"),
                ("other", true, "grid split: a candidate where a chain beyond a hot pixel ends, within the envelope of the piece, is another chain's: excused"),
                ("branch", true, "grid split: a candidate beyond a branching vertex of the graph is another chain's: excused") }) {
                bool second = kind == "other";
                var xy = new List<double> { 0, 0, 24, 0, 24, 24, 0, 24, 3, 13, 12, 13, 5, 14 };
                if (second) xy.AddRange(new[] { 0.0, 13.3 });
                var input = Points(xy.ToArray());
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5));
                if (second) input.Segments.Add(new SegmentInput(5, 7));
                input.SnapTolerance = 1; input.MinAngleDeg = 20;
                var vx = xy.Where((_, i) => i % 2 == 0).ToList(); var vy = xy.Where((_, i) => i % 2 == 1).ToList();
                if (second) vy[7] = 13;
                // 4 and 5: the input segment's ends, 6: the apex, then the vertices of refinement on y = 13
                int Add(double x, double y) { vx.Add(x); vy.Add(y); return vx.Count - 1; }
                int a = 4, m = Add(9, 13), c = Add(6, 13), x2 = Add(10, 13);
                var subs = new List<SubsegmentOutput>();
                switch (kind) {
                    case "own-b": case "branch": subs.AddRange(new[] { new SubsegmentOutput(a, m), new SubsegmentOutput(m, x2), new SubsegmentOutput(x2, c), new SubsegmentOutput(c, 5) }); break;
                    default: subs.AddRange(new[] { new SubsegmentOutput(a, m), new SubsegmentOutput(m, 5), new SubsegmentOutput(5, c), new SubsegmentOutput(c, 7) }); break;
                }
                if (kind == "branch") { int y2 = Add(10, 20); subs.Add(new SubsegmentOutput(x2, y2)); }
                var tri = new[] { new[] { a, m, 6 } };
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vx.ToArray(), VY = vy.ToArray(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                    Triangles = tri, Neighbours = NeighboursOf(tri), BadTriangles = new[] { 0 }, Subsegments = subs };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must(what, report.Has(Violation.AngleBelowBound) != excused, kind + " report: " + report);
            }
            // 5d'. OwnStretch on a graph, from either end of the piece (the orientation in which the call site passes it is not under the
            //      check's control): a path 10 - 11 - 12 - 13 - 14 - 15 with the piece 12-13; 10 and 15 hot: the stretch is the whole path;
            //      a hot 11 cuts it at 11 (11-12 stays: the stretch reaches the hot vertex, not beyond); a branch at 14 (14-20) cuts at 14;
            //      the same with the roles of the piece's ends swapped
            {
                Dictionary<int, List<int>> Graph(params (int, int)[] edges)
                {
                    var g = new Dictionary<int, List<int>>();
                    foreach (var (a, b) in edges) {
                        if (!g.TryGetValue(a, out var la)) g[a] = la = new List<int>(); la.Add(b);
                        if (!g.TryGetValue(b, out var lb)) g[b] = lb = new List<int>(); lb.Add(a);
                    }
                    return g;
                }
                long K(int a, int b) => Oracle.SubsegmentKey(a, b);
                var path = Graph((10, 11), (11, 12), (12, 13), (13, 14), (14, 15));
                var branch = Graph((10, 11), (11, 12), (12, 13), (13, 14), (14, 15), (14, 20));
                foreach (var (ia, ib) in new[] { (12, 13), (13, 12) }) {
                    var all = Oracle.StretchOfForSelfCheck(ia, ib, path, x => x == 10 || x == 15);
                    must("stretch: the whole path between two hot vertices, from either end, piece " + ia + "-" + ib, all.SetEquals(new[] { K(10, 11), K(11, 12), K(12, 13), K(13, 14), K(14, 15) }), "stretch " + all.Count);
                    var cut = Oracle.StretchOfForSelfCheck(ia, ib, path, x => x == 11);
                    must("stretch: a hot vertex ends the walk (its subsegment stays in), from either end, piece " + ia + "-" + ib, cut.SetEquals(new[] { K(11, 12), K(12, 13), K(13, 14), K(14, 15) }), "stretch " + cut.Count);
                    var br = Oracle.StretchOfForSelfCheck(ia, ib, branch, x => x == 10 || x == 15);
                    must("stretch: a branching vertex ends the walk (its two path subsegments stay in), from either end, piece " + ia + "-" + ib, br.SetEquals(new[] { K(10, 11), K(11, 12), K(12, 13), K(13, 14) }), "stretch " + br.Count);
                }
                var ring = Graph((1, 2), (2, 3), (3, 4), (4, 1));
                must("stretch: a closed cycle of degree-2 vertices ends (no endless walk)", Oracle.StretchOfForSelfCheck(1, 2, ring, x => false).Count == 4, "ring");
            }
            // 7. a subsegment with no split point on the grid excuses only itself and what it hides, never an unconstrained edge of a
            //    triangle next to it (review of #277: the exemption had covered every edge of such a triangle). Here (5, 5)-(6, 6) at pixel
            //    1 has no pixel centre between its ends; the triangle on it shares the free edge (6, 6)-(4, 5) with a triangle whose apex
            //    (5, 6) lies in its circumcircle, in plain sight. (The excused side needs an unsplittable piece longer than a pixel, which
            //    on the grid only a constraint at a small angle makes, inside its merge zone: the grid fuzz regressions cover it.)
            foreach (var (kind, what) in new[] {
                (Violation.NotDelaunayLocal, "grid: an unconstrained edge next to an unsplittable subsegment is judged"),
                (Violation.NotDelaunayGlobal, "grid: a visible vertex in the circumcircle of a triangle on an unsplittable subsegment is judged") }) {
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16, 5, 5, 6, 6, 4, 5, 5, 6);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5));
                input.SnapTolerance = 1; input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 4, 5, 6 }, new[] { 6, 5, 7 } }); o.BadTriangles = new[] { 0, 1 };
                o.Subsegments.Add(new SubsegmentOutput(4, 5));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                // the triangle on the unsplittable subsegment itself, not only its neighbour (whose circumcircle holds (5, 5) too)
                must(what, kind == Violation.NotDelaunayLocal ? report.Has(kind) : report.ToString().Contains("vertex 7 inside the circumcircle of 4 5 6"), "report: " + report);
            }
            // 7b. O11-unsplittable pieces: without an explicit tolerance, a subsegment that lies within the tube of another input constraint
            //     has no split point (O11 merges a point there into that constraint), so conforming Delaunay (O2) and the angle bound cannot
            //     be reached across it, as across a piece with no split point on the grid (found with the corpus at 25 degrees, earcut's
            //     water-huge). Legs A (4, 8)-(12, 8) and B (4, 8)-(12, 8.25) meet at 1.8 degrees; B's piece (4, 8)-(5, 8.03125) ends 1/32
            //     from A. Global: the triangle above B, its vertices farther than the tube from both legs (no merge zone), holds the vertex
            //     (4.5, 8) of another triangle in its circumcircle, hidden by the piece. Judged: a tube of 1/64 (the split points outside half of it),
            //     B alone (its own constraint does not count), both triangles moved right by 1.5 (the vertex then sees the triangle past the
            //     piece's end, across B's next piece, which reaches 1/4 from A), and an explicit tolerance (the grid decides there)
            //     Counted only for a triangle with no violation: with a visible vertex in the same circumcircle (5, 8.4 or 4, 8.2, before or
            //     after the hidden one in the order of the vertices), the hidden one is not counted, whichever the oracle reaches first
            foreach (var (tubeDenominator, legA, dx, snap, visible, excused, what) in new[] {
                (16, true, 0.0, 0.0, 0, true, "O11-unsplittable: a vertex hidden behind the piece is excused (O11, O2)"),
                (64, true, 0.0, 0.0, 0, false, "O11-unsplittable: the same with the piece's midpoint outside half the tube is judged"),
                (16, false, 0.0, 0.0, 0, false, "O11-unsplittable: the same without the other constraint is judged (a piece's own constraint does not count)"),
                (16, true, 1.5, 0.0, 0, false, "O11-unsplittable: the same with the vertex not behind the piece is judged"),
                (16, true, 0.0, 1.0 / 1024, 0, false, "O11-unsplittable: the same with an explicit tolerance is judged (the grid decides there)"),
                (16, true, 0.0, 0.0, 1, false, "O11-unsplittable: a triangle with a visible vertex in its circumcircle counts no hidden one"),
                (16, true, 0.0, 0.0, 2, false, "O11-unsplittable: the same with the visible vertex first") }) {
                double dy = dx / 6;   // (raised with the move: its vertices stay farther than the tube from B, its circumcircle holds (6, 8))
                var xy = new List<double> { 0, 0, 16, 0, 16, 16, 0, 16, 4, 8, 12, 8, 12, 8.375, 5, 8.046875, 4.25 + dx, 8.125 + dy, 4.75 + dx, 8.125 + dy, 4.5 + dx, 8.25 + dy,
                    4.5 + dx, 8, 4 + dx, 7.5, 5 + dx, 7.5 };
                var input = Points(xy.ToArray());
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                if (legA) input.Segments.Add(new SegmentInput(4, 5));
                input.Segments.Add(new SegmentInput(4, 6));
                input.MinAngleDeg = 20; input.SnapTolerance = snap;
                var tris = new List<int[]> { new[] { 8, 9, 10 }, new[] { 11, 12, 13 } };   // (the second puts the vertex into the mesh: only used vertices are judged)
                if (visible > 0) {
                    // a vertex in plain sight inside the circumcircle of 8 9 10 (centre (4.5, 7.9375), radius 0.3125), above B, in a triangle of
                    // its own; with visible 2 it takes the slot of a ring corner, before the hidden vertex 11
                    double vx = visible == 1 ? 4.6 : 4.4, vy = 8.2;
                    var input2 = Points(xy.Concat(new[] { vx, vy, vx + 0.5, 9.5, vx - 0.5, 9.5 }).ToArray());
                    input2.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                    input2.Segments.AddRange(input.Segments);
                    input2.MinAngleDeg = 20;
                    input = input2;
                    tris.Add(new[] { 14, 15, 16 });
                }
                var o = Out(input, tris.ToArray());
                o.Subsegments.Add(new SubsegmentOutput(4, 7)); o.Subsegments.Add(new SubsegmentOutput(7, 6));
                if (legA) { o.Subsegments.Add(new SubsegmentOutput(4, 11)); o.Subsegments.Add(new SubsegmentOutput(11, 5)); }
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3, Tube = new Q(1, tubeDenominator) });
                bool hiddenReported = report.ToString().Contains("vertex 11 inside the circumcircle of 8 9 10");
                if (visible > 0)
                    must(what, report.ToString().Contains("vertex 14 inside the circumcircle of 8 9 10") && report.O11GlobalExcused == 0, "report: " + report + ", counted " + report.O11GlobalExcused);
                else
                    must(what, report.GlobalDelaunayChecked && hiddenReported != excused && (report.O11GlobalExcused > 0) == excused, "report: " + report);
            }
            // 7c. the angle bound at the same piece: a flat triangle above B (base angles 3.6 degrees) whose circumcentre lies behind the
            //     piece is excused; judged with the piece's end outside the tube, with B alone, moved right by 1.5 (its circumcentre then lies
            //     behind B's next piece), and with an explicit tolerance
            foreach (var (tubeDenominator, legA, dx, snap, excused, what) in new[] {
                (16, true, 0.0, 0.0, true, "O11-unsplittable: a triangle below the angle whose circumcentre lies behind the piece is excused"),
                (64, true, 0.0, 0.0, false, "O11-unsplittable: the same with the piece's midpoint outside half the tube is judged (angle)"),
                (16, false, 0.0, 0.0, false, "O11-unsplittable: the same without the other constraint is judged (angle)"),
                (16, true, 1.5, 0.0, false, "O11-unsplittable: the same with the circumcentre not behind the piece is judged"),
                (16, true, 0.0, 1.0 / 1024, false, "O11-unsplittable: the same with an explicit tolerance is judged (angle)") }) {
                double dy = dx / 6;
                var input = Points(0, 0, 16, 0, 16, 16, 0, 16, 4, 8, 12, 8, 12, 8.375, 5, 8.046875, 4.25 + dx, 8.203125 + dy, 4.75 + dx, 8.203125 + dy, 4.5 + dx, 8.21875 + dy);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                if (legA) input.Segments.Add(new SegmentInput(4, 5));
                input.Segments.Add(new SegmentInput(4, 6));
                input.MinAngleDeg = 20; input.SnapTolerance = snap;
                var o = Out(input, new[] { new[] { 8, 9, 10 } }); o.BadTriangles = new[] { 0 };
                o.Subsegments.Add(new SubsegmentOutput(4, 7)); o.Subsegments.Add(new SubsegmentOutput(7, 6));
                if (legA) o.Subsegments.Add(new SubsegmentOutput(4, 5));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3, Tube = new Q(1, tubeDenominator) });
                must(what, report.ToString().Contains("triangle 8 9 10 is below 20 degrees") != excused && (report.O11AngleExcused > 0) == excused, "report: " + report);
            }
            // 7d. the local test on the piece itself, and the predicate's conditions. B runs from (4, 8) at slope m (5/128 unless stated), A' from (4.25, 8) to (12, 8),
            //     ending before it meets B (no apex, so no merge zone); the piece of B from x0 to x1 is a subsegment, a vertex below it lies
            //     in the circumcircle of the triangle above it. Excused only when every split point the mesher may choose (the midpoint and
            //     the concentric-shell points from either end) lies within half the tube (the mesher's tolerance) of A', the piece has an own
            //     constraint, and A' meets it in at most one point. Judged:
            //     - a tube of 1/64 (the midpoint outside half of it); B alone; the edge not a subsegment; an explicit tolerance;
            //     - A' only from 4.4 to 5.1, 0.04 and 0.02 below B: wholly within B's tube, nearly coincident with it (the mesher merges);
            //     - A' from 3 to 12 crossing B under the piece at 3.6 degrees: both piece ends lie within the tolerance of A', so the mesher
            //       makes the piece one of A' too and splits it (second review);
            //     - slope 1/16: the split points lie within the tube but not within half of it;
            //     - B horizontal (the piece of length 1/2: its shell points are its midpoint) and A' rising at 0.01 with the midpoint at
            //       (1 - 2^-9) of the tolerance from it: between the margin and the tolerance; and A' with the piece's ends at 0.9 and
            //       (1 + 2^-9) of the tolerance from it: within the margin of ownership, so A' counts as the piece's own;
            //     - a piece of 1.4 at slope 0.035: the midpoint lies within half the tube, the shell point nearer B's far end does not;
            //     - A' alone: the piece lies on no constraint;
            //     - B only from 4.4 to 5.1, wholly within the tube of A' (merged into it);
            //     - the long piece again with the shared edge met from the other side first (the oracle takes the piece's ends in the
            //       other order: the shell point outside half the tube is then the one from the first end).
            //     Excused also: B reaching 2^23 away with the piece's end 2^-30 off it, more than 16 ulps of the end, less than of B's far
            //     end (a split point is computed from the constraint's end; review of the first version)
            const double M = 5.0 / 128;   // (B's slope: the piece's far end 5/128 from A', beyond the tolerance 1/32, its split points within it)
            double B(double x) => 8 + M * (x - 4);
            foreach (var (tubeDenominator, legA, legB, constrained, snap, ax0, ay0, ax1, ay1, m, x0, x1, farB, excused, what, bx0, bLen, flip) in new (int, bool, bool, bool, double, double, double, double, double, double, double, double, bool, bool, string, double, double, bool)[] {
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, true, "O11-unsplittable: the piece itself is excused from the local test", 4.0, 0.0, false),
                (64, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: the same with the piece's midpoint outside half the tube is judged (local)", 4.0, 0.0, false),
                (16, false, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: the same without the other constraint is judged (local)", 4.0, 0.0, false),
                (16, true, true, false, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: an unconstrained edge there is judged (local)", 4.0, 0.0, false),
                (16, true, true, true, 1.0 / 1024, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: the same with an explicit tolerance is judged (local)", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.4, B(4.4) - 0.04, 5.1, B(5.1) - 0.02, M, 4.5, 5.0, false, false, "O11-unsplittable: a short constraint wholly within the tube of the piece's own is judged (merged, O11)", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 3.0, B(3) - 0.109375, 12.0, B(12) + 0.453125, M, 4.5, 5.0, false, false, "O11-unsplittable: a constraint crossing the piece's own at a tiny angle, both ends within the tolerance, is its own (judged)", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, 1.0 / 16, 4.5, 5.0, false, false, "O11-unsplittable: split points within the tube but not within half of it are judged", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.026190524253007, 12.0, 8.103690524253008, 0.0, 4.5, 5.0, false, false, "O11-unsplittable: a split point a hair inside the tolerance is judged (the mesher decides in doubles)", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.026532521059163, 12.0, 8.075917068537814, 0.0, 4.5, 5.0, false, false, "O11-unsplittable: a piece with an end a hair beyond the tolerance of the other constraint counts it as its own (judged)", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, 0.035, 4.125, 5.525, false, false, "O11-unsplittable: a shell point outside half the tube is judged, the midpoint inside it", 4.0, 0.0, false),
                (16, true, false, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: a piece on no input constraint is judged", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, true, true, "O11-unsplittable: a piece off its own long constraint by its rounding is excused", 4.0, 0.0, false),
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, M, 4.5, 5.0, false, false, "O11-unsplittable: a short own constraint wholly within the tube of another is judged (merged, O11)", 4.4, 0.7, false),
                (16, true, true, true, 0.0, 4.25, 8.0, 12.0, 8.0, 0.035, 4.125, 5.525, false, false, "O11-unsplittable: the same shell case with the piece's ends in the other order is judged", 4.0, 0.0, true) }) {
                double big = farB ? 1 << 24 : 16;
                double bl = bLen == 0 ? (farB ? 1 << 23 : 8) : bLen;
                double y0 = 8 + m * (x0 - 4), y1 = 8 + m * (x1 - 4) + (farB ? 1.0 / (1 << 30) : 0), xm = (x0 + x1) / 2, ym = 8 + m * (xm - 4);
                var input = Points(0, 0, big, 0, big, big, 0, big, bx0, 8 + m * (bx0 - 4), bx0 + bl, 8 + m * (bx0 + bl - 4), ax0, ay0, ax1, ay1, x0, y0, x1, y1, xm, ym + 0.2734375, xm, 7.90625);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                if (legB) input.Segments.Add(new SegmentInput(4, 5));
                if (legA) input.Segments.Add(new SegmentInput(6, 7));
                input.MinAngleDeg = 20; input.SnapTolerance = snap;
                var o = Out(input, flip ? new[] { new[] { 9, 8, 11 }, new[] { 8, 9, 10 } } : new[] { new[] { 8, 9, 10 }, new[] { 9, 8, 11 } });
                if (constrained) { o.Subsegments.Add(new SubsegmentOutput(4, 8)); o.Subsegments.Add(new SubsegmentOutput(8, 9)); o.Subsegments.Add(new SubsegmentOutput(9, 5)); }
                if (legA) o.Subsegments.Add(new SubsegmentOutput(6, 7));
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3, Tube = new Q(1, tubeDenominator) });
                must(what, report.ToString().Contains(flip ? "edge 9-8 is not locally Delaunay against 10" : "edge 8-9 is not locally Delaunay against 11") != excused
                    && (report.O11LocalExcused > 0) == excused, "report: " + report);
            }
            // 3. a hole marker that snapping swept, here inside a contour that collapsed, is dropped, not obeyed (clean-room answer 6).
            //    Still obeyed: a marker in a contour that did not move, also next to a near-duplicate point merged into its corner, and a
            //    marker outside the collapsed contour, within a pixel of it or farther (second review of #277: only a swept marker)
            foreach (var (seedX, seedY, ok, what) in new[] {
                (5 + 1.0 / 256, 5 + 1.0 / 512, true, "a hole marker inside a collapsed contour is dropped (D14, clean-room answer 6)"),
                (3.0, 3.0, false, "a hole marker inside a contour that stays still removes its cell"),
                (2 + 1.0 / 64, 2 + 1.0 / 64, false, "a hole marker next to a merged near-duplicate corner of a contour that stays still removes its cell"),
                (5 + 0.75 / 16, 5.0, false, "a hole marker within a pixel of the collapsed contour, outside it, is still obeyed"),
                (5 + 1.0 / 128, 5 + 1.0 / 256, true, "a hole marker on the collapsed contour is dropped"),
                (5 + 1.25 / 16, 5.0, false, "a hole marker more than a pixel from the collapsed contour is still obeyed") }) {
                double h = 1.0 / 128, tol = 1.0 / 16;
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 5, 5, 5 + h, 5, 5 + h, 5 + h, 5, 5 + h, 2, 2, 4, 2.25, 4, 4, 1.75, 4, 2 + tol / 4, 2);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                for (int k = 0; k < 4; ++k) { input.Segments.Add(new SegmentInput(4 + k, 4 + (k + 1) % 4)); input.Segments.Add(new SegmentInput(8 + k, 8 + (k + 1) % 4)); }
                input.HoleSeeds.Add(new[] { seedX, seedY });
                input.SnapTolerance = tol;
                // the quad near (3, 3) stays as a contour; its corners are cocircular, so its inside is added with either diagonal
                var tris = StrictDelaunay(input, new[] { 0, 1, 2, 3, 4, 8, 9, 10, 11 }).Concat(new[] { new[] { 8, 9, 10 }, new[] { 8, 10, 11 } }).ToArray();
                var vx = (double[])input.X.Clone(); var vy = (double[])input.Y.Clone();
                var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = new[] { 0, 1, 2, 3, 4, 4, 4, 4, 8, 9, 10, 11, 8 },
                    Triangles = tris, Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                    Subsegments = new[] { (0, 1), (1, 2), (2, 3), (3, 0), (8, 9), (9, 10), (10, 11), (11, 8) }.Select(p => new SubsegmentOutput(p.Item1, p.Item2)).ToList() };
                var e = new OracleExpect { Milestone = Milestone.M2, StatedArea = new Q(100, 1), StatedAreaTolerance = new Q(1, 16) };
                if (ok) accept(what, input, o, e);
                else reject(what, Violation.RemovedPointCovered, input, o, e);
            }
        }

        static void M3ReviewFindings(Action<string, bool, string> must, Action<string, Mesh2Input, Mesh2Output, OracleExpect> accept, Action<string, Violation, Mesh2Input, Mesh2Output, OracleExpect> reject)
        {
            Mesh2Output Out(Mesh2Input input, int[][] tris, params (int, int, int)[] subs) => new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(),
                InputToVertex = Enumerable.Range(0, input.Count).ToArray(), Triangles = tris, Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                Subsegments = subs.Select(s => new SubsegmentOutput(s.Item1, s.Item2, s.Item3)).ToList() };

            // 1. D13: a boundary vertex within the tube of a hull edge counts as moved; one outside the tube does not, so a missing
            //    triangle at the hull is not hidden by its distance
            {
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 5, 1.0 / 1024, 5, 5);
                var tris = StrictDelaunay(input, Enumerable.Range(0, input.Count).ToArray());
                accept("a point set, all its triangles (control of the next item)", input, Out(input, tris), new OracleExpect { Milestone = Milestone.M3 });
                var sliver = tris.Where(t => !(t.Contains(0) && t.Contains(1) && t.Contains(4))).ToArray();
                reject("a missing hull triangle, its vertex outside the tube", Violation.AreaMismatch, input, Out(input, sliver), new OracleExpect { Milestone = Milestone.M3 });
            }

            // 3b. D13: on a hull domain at M3 the hull edges are boundary, so a segment from a hull vertex at a small angle to a hull edge is a
            //     small input angle: a needle whose shortest edge spans it is exempt; with the segment at 45 degrees the same needle is
            //     judged (found with CDT's issue-211). The needle's long edges are shared with neighbours, so that its small corner is no
            //     boundary corner (which a hull domain forces anyway)
            foreach (var (bx, by, ok, what) in new[] { (8.0, 0.015625, true, "a needle across a segment at 0.11 degrees to a hull edge is exempt (D13)"),
                                                       (8.0, 8.0, false, "the same needle with the segment at 45 degrees is judged") }) {
                // (0, 0) is the apex; p = (4, 0) on the hull edge, q = (4, 1/128) on the first segment, r = (1, 3); neighbours across r-p
                // and q-r with a = (1, 1/2) and b = (4, 3), a off the hull: the mesher's hull source from (0, 0) then ends at p, as the
                // oracle's (the mesher splits hull sources at collinear input points; review of the fix)
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, bx, by, 4, 0, 4, 1.0 / 128, 1, 3, 1, 0.5, 4, 3);
                input.Segments.Add(new SegmentInput(0, 4));
                input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 5, 6, 7 }, new[] { 8, 5, 7 }, new[] { 6, 9, 7 } });
                o.BadTriangles = new[] { 0 };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must((ok ? "accept: " : "reject: ") + what, !report.Has(Violation.NotPositive) && report.Has(Violation.AngleBelowBound) != ok, "report: " + report);
            }

            // 3c. the same exempt needle with an input point at (2, 0) on the hull edge between the apex and p: the mesher's hull source
            //     at p is then (2, 0)-(4, 0), which does not reach the apex, so the needle is judged (the oracle splits its hull edges at
            //     collinear input points too; second review of the fix). With the point at (2, 1/2), off the hull, it stays exempt
            foreach (var (cy, ok, what) in new[] { (0.5, true, "the exempt needle with an input point off the hull stays exempt"),
                                                   (0.0, false, "the needle with an input point on the hull edge between apex and needle is judged") }) {
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 8, 0.015625, 4, 0, 4, 1.0 / 128, 1, 3, 1, 0.5, 4, 3, 2, cy);
                input.Segments.Add(new SegmentInput(0, 4));
                input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 5, 6, 7 }, new[] { 8, 5, 7 }, new[] { 6, 9, 7 } });
                o.BadTriangles = new[] { 0 };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must((ok ? "accept: " : "reject: ") + what, !report.Has(Violation.NotPositive) && report.Has(Violation.AngleBelowBound) != ok, "report: " + report);
            }

            // 3. the small-input-angle exemption covers endpoints at about the same distance from the apex (Shewchuk's shells), not
            //    endpoints a factor of 1.5 or more apart: two segments at about 10.6 degrees, vertices at 1, 4, 16 on one and at
            //    1.53, 6.1, 24.4 on the other (exact: multiples of (32, 6) by 3/64, 3/16, 3/4); every adjacent pair is 1.53 apart
            {
                var input = Points(0, 0, 32, 0, 32, 6, 1, 0, 4, 0, 16, 0, 1.5, 0.28125, 6, 1.125, 24, 4.5);
                input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2));
                input.MinAngleDeg = 20;
                var tris = StrictDelaunay(input, Enumerable.Range(0, input.Count).ToArray());
                var o = Out(input, tris, (0, 3, 0), (3, 4, 0), (4, 5, 0), (5, 1, 0), (0, 6, 0), (6, 7, 0), (7, 8, 0), (8, 2, 0));
                o.BadTriangles = Enumerable.Range(0, tris.Length).ToArray();
                reject("shells 1.53 apart, exempted as a small input angle", Violation.AngleBelowBound, input, o, new OracleExpect { Milestone = Milestone.M3 });
            }

            // 4. the floating-point filters of the segment check (ring edges far from a source are left out, with a lower bound of
            //    their distance): in a thin strip the step beside a boundary piece must stay below the opposite edge, which is one of
            //    the left-out edges; a missing boundary chain is still found
            {
                var input = Points(0, 0, 32, 0, 64, 0, 64, 1, 0, 1);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
                var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 3, 4 }, new[] { 1, 2, 3 } };
                accept("a thin strip, all boundary chains (control of the next item)", input, Out(input, tris, (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0), (4, 0, 0)), new OracleExpect { Milestone = Milestone.M2 });
                reject("a thin strip without one boundary piece", Violation.SegmentMissing, input, Out(input, tris, (1, 2, 0), (2, 3, 0), (3, 4, 0), (4, 0, 0)), new OracleExpect { Milestone = Milestone.M2 });
            }

            // 5. a domain thinner than the automatic tolerance may vanish when its points merge (O11): empty is fine for a sliver
            //    1e-28 wide, not for one 1e-6 wide (corpus run, earcut's infinite-loop-jhl)
            {
                foreach (var (width, ok) in new[] { (1e-28, true), (1e-6, false) }) {
                    var input = Points(-1, 2, 0, 0, 2, -1, 2, -1, 0, width, -1, 2);
                    input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false)); input.Rings.Add(new RingInput(new[] { 3, 4, 5 }, true));
                    var o = new Mesh2Output { Status = Mesh2Status.Empty, VX = input.X, VY = input.Y, InputToVertex = new[] { 0, 1, 2, 2, 1, 0 },
                        Triangles = new int[0][], Neighbours = new int[0][], Subsegments = new List<SubsegmentOutput>(), BadTriangles = new int[0] };
                    if (ok) accept("a sliver below the tolerance, empty", input, o, new OracleExpect { Milestone = Milestone.M2 });
                    else reject("a sliver above the tolerance, empty", Violation.EmptyOnArea, input, o, new OracleExpect { Milestone = Milestone.M2 });
                }
            }

            // 6. the merge zone at a small input angle: a skinny triangle at the apex of two segments at 1 degree is exempt within
            //    3 tube / sin(angle) of it (1.88e-7 here: tube 1200 / 2^40), not beyond; the farthest vertex at 2.88 resp. 3.36 of
            //    tube / sin, so that the zone factor 2 and the former 8 both fail; one vertex on a segment, the others between them, so
            //    that no other exemption applies (corpus run, earcut's water-huge)
            {
                double c = Math.Cos(Math.PI / 180), sn = Math.Sin(Math.PI / 180);
                foreach (var (r, ok) in new[] { (1.2e-7, true), (1.4e-7, false) }) {
                    var input = Points(1000, 0, 1100, 0, 1000 + 100 * c, 100 * sn, 1000 + r, 0, 1000 + 1.5 * r, 0.008 * r, 1000 + 1.25 * r, 0.0095 * r,
                        900, -100, 1200, -100, 1200, 100, 900, 100);
                    input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2));
                    // (an outer ring, so that the triangle's edges are not taken for hull corners, D13)
                    input.Rings.Add(new RingInput(new[] { 6, 7, 8, 9 }, false));
                    input.MinAngleDeg = 20;
                    var o = Out(input, new[] { new[] { 3, 4, 5 } }); o.BadTriangles = new[] { 0 };
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                    bool angleFlagged = report.Has(Violation.AngleBelowBound);
                    must(ok ? "merge zone: a skinny triangle 1.8e-7 from a 1 degree apex is exempt" : "merge zone: a skinny triangle 2.1e-7 from the apex is still judged",
                        angleFlagged != ok, "report: " + report);
                }
            }

            // 6a. the same needle where the second segment leaves the apex to the left, at 179 degrees to the first: no merge zone on
            //     that side (the segments are not within the tube of each other there; review of the hull pairing)
            {
                double c = Math.Cos(Math.PI / 180), sn = Math.Sin(Math.PI / 180), r = 1.2e-7;
                var input = Points(1000, 0, 1100, 0, 1000 - 100 * c, 100 * sn, 1000 + r, 0, 1000 + 1.5 * r, 0.008 * r, 1000 + 1.25 * r, 0.0095 * r,
                    900, -100, 1200, -100, 1200, 100, 900, 100);
                input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2));
                input.Rings.Add(new RingInput(new[] { 6, 7, 8, 9 }, false));
                input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 3, 4, 5 } }); o.BadTriangles = new[] { 0 };
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                must("merge zone: a skinny triangle at an apex of 179 degrees is judged", report.Has(Violation.AngleBelowBound), "report: " + report);
            }

            // 6b. the merge zone where a segment meets a hull edge (hull domain, D13): the mesher refuses a split point of the hull edge
            //     within the tolerance of the segment (O11), so the zone counts the pair; the same radii as in 6 (tube 1200 / 2^40, the
            //     apex a hull corner, the segment at 1 degree to the hull edge). The needle 3-4-5's two small corners are no boundary
            //     corners: its edges are shared with three neighbours (6 on the hull, 7 and 10 across the segment), which lie in the zone
            //     too (Herne tiles). Judged: beyond the zone, with an explicit tolerance (the grid refuses nothing this way), and on the
            //     obtuse side of the apex (review of the pairing: there the sources are not within the tube of each other). Exempt
            //     too with the roles swapped: the segment along the hull's inside, the hull edge at 1 degree to it
            {
                double c = Math.Cos(Math.PI / 180), sn = Math.Sin(Math.PI / 180);
                var needle = new[] { new[] { 3, 4, 5 }, new[] { 3, 6, 4 }, new[] { 3, 5, 7 }, new[] { 5, 4, 10 } };
                double[] Triangle(double r) => new[] { 1000 + r, 0.0005 * r, 1000 + 1.5 * r, 0.008 * r, 1000 + 1.25 * r, 0.0095 * r, 1000 + 1.3 * r, 0, 1000 + 0.9 * r, 0.3 * r };
                void Case(string what, bool exempt, double[] xy, int[][] triangles, double snap = 0, (int, int) segment = default)
                {
                    // (with a tolerance the input lies on its grid, 2^-44 here, so that the oracle reaches the angle bound)
                    if (snap > 0) xy = xy.Select(x => Math.Round(x / snap) * snap).ToArray();
                    var input = Points(xy);
                    if (segment.Item1 >= 0) input.Segments.Add(segment == default ? new SegmentInput(0, 2) : new SegmentInput(segment.Item1, segment.Item2));
                    input.MinAngleDeg = 20;
                    input.SnapTolerance = snap;
                    var o = Out(input, triangles); o.BadTriangles = Enumerable.Range(0, triangles.Length).ToArray();
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                    string text = report.ToString();
                    bool judged = text.Contains("triangle " + string.Join(" ", triangles[0]) + " is below");
                    must((exempt ? "hull merge zone, exempt: " : "hull merge zone, judged: ") + what,
                        !report.Has(Violation.NotPositive) && (exempt ? !report.Has(Violation.AngleBelowBound) : judged), "report: " + text);
                }
                double[] Join(params double[][] parts) => parts.SelectMany(x => x).ToArray();
                var corners = new double[] { 1200, 100, 1000, 100 };
                Case("a needle 1.8e-7 from a 1 degree apex of a segment and a hull edge", true,
                    Join(new double[] { 1000, 0, 1200, 0, 1000 + 100 * c, 100 * sn }, Triangle(1.2e-7), corners, new[] { 1000 + 1.4 * 1.2e-7, 0.2 * 1.2e-7 }), needle);
                Case("the needle 2.1e-7 from the apex", false,
                    Join(new double[] { 1000, 0, 1200, 0, 1000 + 100 * c, 100 * sn }, Triangle(1.4e-7), corners, new[] { 1000 + 1.4 * 1.4e-7, 0.2 * 1.4e-7 }), needle);
                Case("the exempt needle with an explicit tolerance (2^-44)", false,
                    Join(new double[] { 1000, 0, 1200, 0, 1000 + 100 * c, 100 * sn }, Triangle(1.2e-7), corners, new[] { 1000 + 1.4 * 1.2e-7, 0.2 * 1.2e-7 }), needle, Math.Pow(2, -44));
                // mirrored at x = 1000: the apex (1000, 0) a collinear point of the hull edge from (800, 0), the segment to the right, the
                // needle on the left, at 179 degrees to the segment
                var left = Triangle(1.2e-7).Select((x, i) => i % 2 == 0 ? 2000 - x : x).ToArray();
                Case("a needle on the obtuse side of a collinear hull point", false,
                    Join(new double[] { 1000, 0, 1200, 0, 1000 + 100 * c, 100 * sn }, left, new double[] { 1200, 100, 800, 100, 2000 - (1000 + 1.4 * 1.2e-7), 0.2 * 1.2e-7, 800, 0 }),
                    new[] { new[] { 3, 5, 4 }, new[] { 3, 4, 6 }, new[] { 3, 7, 5 }, new[] { 5, 10, 4 } });
                // the roles swapped: the segment (1000, 0) - (1100, 0) inside, the hull edge from the apex to (1000 + 100 cos 1,
                // 100 sin 1), the hull's other corners below; the needle's vertices lie in the segment's tube only, its neighbours 7 and
                // 10 between the needle and the hull edge (wedges at its small corners of 173 and 162 degrees, not forced)
                double r0 = 1.2e-7;
                var swapped = Triangle(r0);
                swapped[8] = 1000 + 0.9 * r0; swapped[9] = 0.012 * r0;
                Case("the needle near the segment, the hull edge at 1 degree to it", true,
                    Join(new double[] { 1000, 0, 1100, 0, 1000 + 100 * c, 100 * sn }, swapped, new double[] { 1200, -100, 1000, -100, 1000 + 1.55 * r0, 0.026 * r0 }),
                    needle, 0, (0, 1));
                // no segment: the hull's corner at the apex is 1 degree (edges to (1200, 0) and to (1000 + 100 cos 1, 100 sin 1)); the
                // mesher never refuses a hull split point near another hull edge, so two hull edges make no zone
                Case("a needle at a hull corner of 1 degree (two hull edges)", false,
                    Join(new double[] { 1000, 0, 1200, 0, 1000 + 100 * c, 100 * sn }, swapped, new double[] { 1100, 0.5, 1050, 0.3, 1000 + 1.55 * r0, 0.026 * r0 }),
                    needle, 0, (-1, -1));
            }

            // 7. conforming in the merge zone: a vertex inside the circumcircle of a triangle 1.8e-7 from a 1 degree apex is excused,
            //    2.1e-7 from it not (2.88 resp. 3.36 of tube / sin, as in 6; indexed global pass on water-huge)
            {
                double c = Math.Cos(Math.PI / 180), sn = Math.Sin(Math.PI / 180);
                foreach (var (r, ok) in new[] { (9e-8, true), (1.05e-7, false) }) {
                    var input = Points(1000, 0, 1100, 0, 1000 + 100 * c, 100 * sn, 1000 + r, 0, 1000 + 2 * r, 0, 1000 + 1.5 * r, 0.004 * r, 1000 + 1.2 * r, 0.002 * r,
                        900, -100, 1200, -100, 1200, 100, 900, 100);
                    input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2));
                    input.Rings.Add(new RingInput(new[] { 7, 8, 9, 10 }, false));
                    input.Conforming = true;
                    var o = Out(input, new[] { new[] { 3, 4, 5 }, new[] { 3, 5, 6 } });
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                    bool flagged = report.Has(Violation.NotDelaunayLocal) || report.Has(Violation.NotDelaunayGlobal);
                    must(ok ? "merge zone: conforming excused 1.8e-7 from a 1 degree apex" : "merge zone: conforming judged 2.1e-7 from the apex", flagged != ok, "report: " + report);
                }
            }

            // 2. a region seed on an input point is ambiguous and ignored (D6); only a vertex the refinement placed does not count
            {
                var input = Points(0, 0, 4, 0, 4, 4, 0, 4, 1, 1);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Regions.Add(new RegionInput { Seed = new double[] { 1, 1 }, Label = 9 });
                var tris = StrictDelaunay(input, Enumerable.Range(0, input.Count).ToArray());
                var subs = new[] { (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0) };
                var plain = Out(input, tris, subs); plain.TriangleLabels = new int[tris.Length];
                accept("a seed on an input point, ignored (control of the next item)", input, plain, new OracleExpect { Milestone = Milestone.M2 });
                var labelled = Out(input, tris, subs); labelled.TriangleLabels = Enumerable.Repeat(9, tris.Length).ToArray();
                reject("a seed on an input point, applied", Violation.RegionLabelWrong, input, labelled, new OracleExpect { Milestone = Milestone.M2 });
            }
        }

        // The wrong verdicts and tool defects the second independent review (2026-11-01) demonstrated. They stay as regressions.
        static void SecondReviewFindings(Action<string, bool, string> must, Action<string, Mesh2Input, Mesh2Output, OracleExpect> accept, Action<string, Violation, Mesh2Input, Mesh2Output, OracleExpect> reject)
        {
            Mesh2Output Out(Mesh2Input input, int[][] tris, int[] map = null, params (int, int, int)[] subs) => new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(),
                InputToVertex = map ?? Enumerable.Range(0, input.Count).ToArray(), Triangles = tris, Neighbours = NeighboursOf(tris), BadTriangles = new int[0],
                Subsegments = subs.Select(s => new SubsegmentOutput(s.Item1, s.Item2, s.Item3)).ToList() };

            // 1. a gap bounded by constraints hidden in the area slack of an unrelated merge
            {
                double s = 1.0 / 16;
                var input = Points(0, 0, 10, 0, 10, 10, 0, 10, 5, 5, 5 + s, 5, 5, 5 + s, 1, 1, 1 + 1.0 / 256, 1);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Segments.Add(new SegmentInput(4, 5)); input.Segments.Add(new SegmentInput(5, 6)); input.Segments.Add(new SegmentInput(6, 4));
                input.SnapTolerance = 1.0 / 64;
                var tris = StrictDelaunay(input, Enumerable.Range(0, 8).ToArray());
                var subs = new[] { (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0), (4, 5, 0), (5, 6, 0), (6, 4, 0) };
                var map = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 7 };
                accept("the full mesh with a merge (control of the next item)", input, Out(input, tris, map, subs), new OracleExpect { Milestone = Milestone.M2 });
                var gap = tris.Where(t => !(t.Contains(4) && t.Contains(5) && t.Contains(6))).ToArray();
                reject("a gap bounded by constraints, hidden by an unrelated merge", Violation.AreaMismatch, input, Out(input, gap, map, subs), new OracleExpect { Milestone = Milestone.M2 });
            }

            // 2. without a ring, a corner of the hull forces its angle like a corner of a ring (D13)
            {
                var input = Points(0, 0, 16, 0, 16, 1); input.MinAngleDeg = 20;
                var o = Out(input, new[] { new[] { 0, 1, 2 } }); o.BadTriangles = new[] { 0 };
                accept("a small angle at a corner of the hull, no subsegments", input, o, new OracleExpect { Milestone = Milestone.M3 });
                var foreign = new Mesh2Output { Status = Mesh2Status.Success, VX = input.X, VY = input.Y, Triangles = new[] { new[] { 0, 1, 2 } }, IdentityRules = false, BadTriangles = new[] { 0 } };
                accept("the same from a foreign producer", input, foreign, new OracleExpect { Milestone = Milestone.M3 });
            }

            // 3. the standalone oracle reads results without neighbours, which the text format does not carry
            {
                var input = Points(0, 0, 4, 0, 4, 4, 0, 4); input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                var o = Out(input, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, null, (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0));
                var back = TextFormat.ReadOutput(TextFormat.WriteOutput(o, "ours"), out _);
                accept("an M2 result through the text format", TextFormat.ReadInput(TextFormat.WriteInput(input)), back, new OracleExpect { Milestone = Milestone.M2 });
            }

            // 4. hashes and texts do not depend on the culture (sv-SE writes U+2212 as the minus sign)
            {
                var input = Points(0, 0, 4, 0, 4, 4); input.Segments.Add(new SegmentInput(0, 1, -3)); input.Regions.Add(new RegionInput { Seed = new double[] { 1, 1 }, Label = -2 });
                var o = Out(input, new[] { new[] { 0, 1, 2 } }, null, (0, 1, -3));
                var saved = System.Globalization.CultureInfo.CurrentCulture;
                string h1, i1, h2 = null, i2 = null, q2 = null; bool reread = false;
                try {
                    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                    h1 = TextFormat.InputSha256(input); i1 = Fingerprint.Identity(o);
                    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
                    h2 = TextFormat.InputSha256(input); i2 = Fingerprint.Identity(o); q2 = new Q(-3, 4).ToString();
                    try { TextFormat.ReadInput(TextFormat.WriteInput(input)); reread = true; } catch (FormatException) { }
                } finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
                must("input hash independent of the culture", h1 == h2, h1 + " / " + h2);
                must("identity fingerprint independent of the culture", i1 == i2, i1 + " / " + i2);
                must("exact values written with an ASCII minus in every culture", q2 == "-3/4", q2);
                must("the canonical text reads back in every culture", reread, "FormatException");
            }

            // 5. malformed text is a FormatException (exit code 2 on the command line), never accepted, never another exception
            {
                string head = "mesh2-input 1\npoints 3\n0 0\n4 0\n0 4\nsegments 0\n";
                string tail = "holeseeds 0\nregions 0\nmin_angle_deg -\nmax_area -\nsteiner_limit -\nsnap_tolerance 0\nsize_constant -\nresolve_crossings 1\nconforming 0\nend\n";
                string output = "mesh2-output 1\nproducer x\nstatus Success\nvertices 0\ntriangles 0\nsubsegments -\nmap -\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n";
                var bad = new Dictionary<string, Action> {
                    { "a ring whose count does not match", () => TextFormat.ReadInput(head + "rings 1\nouter 0 4 0 1 2\n" + tail) },
                    { "a short point line", () => TextFormat.ReadInput("mesh2-input 1\npoints 1\n0\nsegments 0\nrings 0\n" + tail) },
                    { "an integer out of range", () => TextFormat.ReadInput("mesh2-input 1\npoints 0\nsegments 99999999999\n") },
                    { "a status that is a flag combination", () => TextFormat.ReadOutput(output.Replace("Success", "Success,Empty"), out _) },
                    { "a status that is a number", () => TextFormat.ReadOutput(output.Replace("Success", "7"), out _) },
                    { "truncated JSON", () => MiniJson.Parse("{\"a\": \"unterminated") },
                    { "a JSON integer out of range", () => MiniJson.Parse("{\"a\": 123456789012345678901234567890}") },
                };
                foreach (var item in bad) {
                    string got = "accepted";
                    try { item.Value(); } catch (FormatException) { got = null; } catch (Exception ex) { got = ex.GetType().Name; }
                    must("malformed text refused: " + item.Key, got == null, got);
                }
            }

            // 6. a row removed before, re-added and removed again needs a new reason
            {
                var row = new GoldenRow { Case = "x", Milestone = "M1", Status = "Success", Triangles = 1, Reason = "first" };
                var before = new GoldenFile(); before.Removed.Add(new RemovedRow { Case = "x", Reason = "removed once" }); before.Rows.Add(row);
                var after = new GoldenFile(); after.Removed.AddRange(before.Removed);
                must("a repeated removal without a new reason is refused", GoldenProcess.VerifyChanges(before, after).Count == 1, "accepted");
                after.Removed.Add(new RemovedRow { Case = "x", Reason = "removed again, because" });
                must("a repeated removal with a new reason is accepted", GoldenProcess.VerifyChanges(before, after).Count == 0, "refused");
            }

            // 8. the chain end of a crossing at a tiny angle (a vertex in the tubes of the source and the cutting ring edge) must not let a
            //    ring edge along the source cut it: two parcels without their shared border (review of M2)
            {
                var input = Points(0, 0, 5, 0, 10, 0, 10, 10, 5, 10, 0, 10, 4, 5, 6, 5.1);
                input.Rings.Add(new RingInput(new[] { 0, 1, 4, 5 }, false, 1)); input.Rings.Add(new RingInput(new[] { 1, 2, 3, 4 }, false, 1));
                var tris = StrictDelaunay(input, Enumerable.Range(0, 8).ToArray());
                must("the strict Delaunay triangulation of the parcels has no edge 1-4", !tris.Any(t => t.Contains(1) && t.Contains(4)), "it has");
                var o = Out(input, tris, null, (0, 1, 1), (1, 2, 1), (2, 3, 1), (3, 4, 1), (4, 5, 1), (5, 0, 1));
                reject("two parcels without their shared border, automatic tube", Violation.SegmentMissing, input, o, new OracleExpect { Milestone = Milestone.M2 });
            }

            // 9. at the Steiner limit the mesh is judged as constrained Delaunay, not conforming: still a constrained Delaunay
            //    triangulation, so a flipped free edge must fail
            {
                var input = Points(0, 0, 8, 0, 8, 8, 0, 8, 3, 2, 5, 3, 4, 6);
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); input.MinAngleDeg = 20; input.SteinerLimit = 0;
                var tris = StrictDelaunay(input, Enumerable.Range(0, 7).ToArray()).ToList();
                // flip the first interior edge whose quadrilateral is convex
                bool flipped = false;
                for (int i = 0; i < tris.Count && !flipped; ++i)
                    for (int j = i + 1; j < tris.Count && !flipped; ++j) {
                        var shared = tris[i].Intersect(tris[j]).ToArray();
                        if (shared.Length != 2) continue;
                        int c = tris[i].Except(shared).First(), d = tris[j].Except(shared).First();
                        Pt P(int q) => Pt.FromDoubles(input.X[q], input.Y[q]);
                        int[] t1 = { c, d, shared[0] }, t2 = { d, c, shared[1] };
                        if (Exact.Orient(P(t1[0]), P(t1[1]), P(t1[2])) < 0) t1 = new[] { d, c, shared[0] };
                        if (Exact.Orient(P(t2[0]), P(t2[1]), P(t2[2])) < 0) t2 = new[] { c, d, shared[1] };
                        if (Exact.Orient(P(t1[0]), P(t1[1]), P(t1[2])) <= 0 || Exact.Orient(P(t2[0]), P(t2[1]), P(t2[2])) <= 0) continue;
                        tris[i] = t1; tris[j] = t2; flipped = true;
                    }
                must("a convex free edge to flip exists", flipped, "none");
                var o = Out(input, tris.ToArray(), null, (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0));
                o.SteinerLimitReached = true;
                o.BadTriangles = Enumerable.Range(0, tris.Count).Where(i => Exact.MinAngleSin2(Pt.FromDoubles(input.X[tris[i][0]], input.Y[tris[i][0]]), Pt.FromDoubles(input.X[tris[i][1]], input.Y[tris[i][1]]), Pt.FromDoubles(input.X[tris[i][2]], input.Y[tris[i][2]])) < Q.FromDouble(Math.Sin(20 * Math.PI / 180) * Math.Sin(20 * Math.PI / 180))).ToArray();
                reject("a flipped free edge at the Steiner limit", Violation.NotDelaunayLocal, input, o, new OracleExpect { Milestone = Milestone.M3 });
            }

            // 10. a skinny triangle whose shortest edge spans a small input angle (two segments from a common apex at 10 degrees, the
            //     ends at about the same distance) is exempt from the angle bound; the same triangle without the segments is not
            {
                double c = Math.Cos(10 * Math.PI / 180), sn = Math.Sin(10 * Math.PI / 180);
                var input = Points(0, 0, 16, 0, 16 * c, 16 * sn, 8, 0, 8 * c, 8 * sn);
                input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2)); input.MinAngleDeg = 20;
                var tris = new[] { new[] { 0, 3, 4 }, new[] { 3, 1, 2 }, new[] { 3, 2, 4 } };
                var o = Out(input, tris, null, (0, 3, 0), (3, 1, 0), (0, 4, 0), (4, 2, 0), (1, 2, 0));
                o.BadTriangles = Enumerable.Range(0, 3).ToArray();
                accept("skinny triangles spanning a small input angle", input, o, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
                var plain = Points(0, 0, 16, 0, 16 * c, 16 * sn, 8, 0, 8 * c, 8 * sn); plain.MinAngleDeg = 20;
                plain.Rings.Add(new RingInput(new[] { 0, 3, 1, 2, 4 }, false));
                var o2 = Out(plain, tris, null, (0, 3, 0), (3, 1, 0), (1, 2, 0), (2, 4, 0), (4, 0, 0));
                o2.BadTriangles = Enumerable.Range(0, 3).ToArray();
                reject("the same skinny triangle with only a ring", Violation.AngleBelowBound, plain, o2, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            }

            // 11. spec 1.4: a needle a few ulps wide stays; one 2^-30 wide does not
            {
                foreach (var (width, ok) in new[] { (Math.Pow(2, -50), true), (Math.Pow(2, -30), false) }) {
                    var input = Points(0, 0, 4, 0, 4, 4, 0, 4, 2, 2, 2 + width, 2, 2, 2 + width);
                    input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); input.MinAngleDeg = 20; input.MaxArea = 100;
                    var tris = StrictDelaunay(input, Enumerable.Range(0, 7).ToArray());
                    var o = Out(input, tris, null, (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0));
                    o.BadTriangles = Enumerable.Range(0, tris.Length).Where(i => Exact.MinAngleSin2(Pt.FromDoubles(input.X[tris[i][0]], input.Y[tris[i][0]]), Pt.FromDoubles(input.X[tris[i][1]], input.Y[tris[i][1]]), Pt.FromDoubles(input.X[tris[i][2]], input.Y[tris[i][2]])) < Q.FromDouble(Math.Sin(20 * Math.PI / 180) * Math.Sin(20 * Math.PI / 180))).ToArray();
                    var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
                    bool angleOk = !report.Has(Violation.AngleBelowBound);
                    must("a triangle " + (ok ? "a few ulps" : "2^-30") + " wide " + (ok ? "is exempt" : "is not exempt") + " from the angle bound", angleOk == ok, report.ToString());
                }
            }

            // 7. the list of bad triangles is a set, and empty without a minimum angle
            {
                var input = Points(0, 0, 8, 0, 0, 8);
                var o = Out(input, new[] { new[] { 0, 1, 2 } }); o.BadTriangles = new[] { 0, 0, 0 };
                reject("bad triangles listed without a minimum angle", Violation.BadListWrong, input, o, new OracleExpect { Milestone = Milestone.M1 });
                var angled = Points(0, 0, 64, 0, 64, 1); angled.MinAngleDeg = 20; angled.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
                var twice = Out(angled, new[] { new[] { 0, 1, 2 } }, null, (0, 1, 0), (1, 2, 0), (2, 0, 0)); twice.BadTriangles = new[] { 0, 0 };
                reject("a bad triangle listed twice", Violation.BadListWrong, angled, twice, new OracleExpect { Milestone = Milestone.M3 });
            }
        }

        // Each of these is a wrong verdict an independent review of the oracle demonstrated. They stay as regressions.
        static void ReviewFindings(Action<string, bool, string> must, Action<string, Mesh2Input, Mesh2Output, OracleExpect> accept, Action<string, Violation, Mesh2Input, Mesh2Output, OracleExpect> reject)
        {
            var auto = new Func<OracleExpect>(() => new OracleExpect { Milestone = Milestone.M2 });
            Mesh2Output Mesh(Mesh2Input input, double[] extra, int[][] triangles, params (int, int, int)[] subs)
            {
                extra = extra ?? new double[0];
                int n = input.Count;
                return new Mesh2Output {
                    Status = Mesh2Status.Success,
                    VX = input.X.Concat(Enumerable.Range(0, extra.Length / 2).Select(i => extra[2 * i])).ToArray(),
                    VY = input.Y.Concat(Enumerable.Range(0, extra.Length / 2).Select(i => extra[2 * i + 1])).ToArray(),
                    InputToVertex = Enumerable.Range(0, n).ToArray(), Triangles = triangles, Neighbours = NeighboursOf(triangles), BadTriangles = new int[0],
                    Subsegments = subs.Select(s => new SubsegmentOutput(s.Item1, s.Item2, s.Item3)).ToList()
                };
            }
            var ring0123 = new[] { (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0) };

            // a tube permits movement, it grants no area: a filled hole with a large tolerance
            var filled = Points(0, 0, 1000, 0, 1000, 1000, 0, 1000, 500, 500, 501, 500, 501, 501, 500, 501);
            filled.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); filled.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true)); filled.SnapTolerance = 0.01;
            var annulus = new[] { new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };
            var holeSubs = ring0123.Concat(new[] { (4, 5, 0), (5, 6, 0), (6, 7, 0), (7, 4, 0) }).ToArray();
            reject("hole filled, large tolerance, nothing moved", Violation.AreaMismatch, filled, Mesh(filled, null, annulus.Concat(new[] { new[] { 4, 5, 6 }, new[] { 4, 6, 7 } }).ToArray(), holeSubs), auto());

            // the Steiner flag is not free to claim
            var rect = Points(0, 0, 100, 0, 100, 1, 0, 1);
            rect.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); rect.MinAngleDeg = 30; rect.MaxArea = 0.5;
            var rectOut = Mesh(rect, null, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, ring0123);
            rectOut.BadTriangles = new[] { 0, 1 }; rectOut.SteinerLimitReached = true;
            reject("Steiner flag without Steiner points", Violation.SteinerFlagWrong, rect, rectOut, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });

            // a hull with a vertex outside and a notch that cancels its area
            var hull = Points(0, 0, 2, 0, 2, 2, 0, 2);
            var notch = Mesh(hull, new double[] { 1, -0.5, 1, 1.5 }, new[] { new[] { 0, 4, 5 }, new[] { 4, 1, 5 }, new[] { 1, 2, 5 }, new[] { 0, 5, 3 } }, (0, 4, 0), (4, 1, 0), (1, 2, 0), (2, 5, 0), (5, 3, 0), (3, 0, 0));
            reject("vertex outside the hull, area right", Violation.TriangleOutsideDomain, hull, notch, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            reject("the notch of that hull", Violation.BoundaryNotConstrained, hull, notch, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });

            // "a few ulps off a segment" excuses only what those ulps cause
            var kite = Points(0, 0, 30, 10, 31, 12, 15, 100, -1, 12);
            kite.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false)); kite.Conforming = true;
            reject("gross in-circle violation at an off-segment vertex", Violation.NotDelaunayLocal, kite,
                Mesh(kite, new double[] { 10, 10.0 / 3 }, new[] { new[] { 5, 1, 2 }, new[] { 5, 2, 3 }, new[] { 5, 3, 4 }, new[] { 5, 4, 0 } }, (0, 5, 0), (5, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0), (4, 0, 0)),
                new OracleExpect { Milestone = Milestone.M3 });

            // a locked diagonal does not excuse triangles that can shrink
            var diagonal = Points(0, 0, 10, 0, 10, 10, 0, 10);
            diagonal.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); diagonal.Segments.Add(new SegmentInput(0, 2, 0, true)); diagonal.MaxArea = 1;
            reject("area above the bound beside a locked segment", Violation.AreaAboveBound, diagonal, Mesh(diagonal, null, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 0, 0), (0, 2, 0)), new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });

            // a small angle elsewhere at a vertex with a wedge is not forced by it
            var wedge = Points(0, 0, 100, 0, 100, 5, 1, 100, 0, 100);
            wedge.Segments.Add(new SegmentInput(0, 1)); wedge.Segments.Add(new SegmentInput(0, 2)); wedge.MinAngleDeg = 20;
            var wedgeOut = Mesh(wedge, null, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 4 } }, (0, 1, 0), (0, 2, 0));
            wedgeOut.BadTriangles = new[] { 0, 2 };
            reject("small angle outside the input wedge", Violation.AngleBelowBound, wedge, wedgeOut, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero, GlobalDelaunayLimit = 0 });

            // M2 has no refinement
            var square = Points(0, 0, 4, 0, 4, 4, 0, 4);
            square.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            reject("M2 with a free constructed vertex", Violation.SteinerNotAllowed, square, Mesh(square, new double[] { 2, 1 }, new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } }, ring0123), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });

            // hole seeds: what the oracle cannot compute, it says so
            var seeded = Points(0, 0, 10, 0, 10, 10, 0, 10, 2, 2, 4, 2, 4, 4, 2, 4);
            seeded.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            for (int i = 0; i < 4; ++i) seeded.Segments.Add(new SegmentInput(4 + i, 4 + (i + 1) % 4));
            seeded.HoleSeeds.Add(new double[] { 3, 3 });
            var seededSubs = holeSubs;
            var seededMesh = Mesh(seeded, null, annulus, seededSubs);
            reject("hole seed, no stated area", Violation.CannotJudge, seeded, seededMesh, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            reject("hole seed covered", Violation.RemovedPointCovered, seeded, Mesh(seeded, null, annulus.Concat(new[] { new[] { 4, 5, 6 }, new[] { 4, 6, 7 } }).ToArray(), seededSubs), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, StatedArea = new Q(96, 1) });
            reject("hole seed, wrong area", Violation.AreaMismatch, seeded, Mesh(seeded, null, annulus.Skip(1).ToArray(), seededSubs), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, StatedArea = new Q(96, 1) });
            must("hole seed, the removal as stated is judged by the stated area", !Oracle.Check(seeded, seededMesh, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero, StatedArea = new Q(96, 1) }).Has(Violation.AreaMismatch), null);

            // small lies
            var triangle = Points(0, 0, 8, 0, 0, 8);
            var exact = new Mesh2Output {
                Status = Mesh2Status.Success, VX = new double[3], VY = new double[3], InputToVertex = new[] { 0, 1, 2 }, Triangles = new[] { new[] { 0, 1, 2 } },
                ExactVertices = new[] { Pt.FromDoubles(0, 0), Pt.FromDoubles(8, 0), Pt.FromDoubles(0, 8) } };
            reject("doubles that are not the exact vertices", Violation.DoublesNotRounded, triangle, exact, M1());
            var angled = Points(0, 0, 8, 0, 0, 8); angled.MinAngleDeg = 20;
            var listed = Mesh(angled, null, new[] { new[] { 0, 1, 2 } }); listed.BadTriangles = new[] { 999 };
            reject("bad list with a triangle that does not exist", Violation.BadListWrong, angled, listed, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            var twice = Points(0, 0, 8, 0, 4, 4, 4, -4);
            twice.Segments.Add(new SegmentInput(0, 1, 0, true)); twice.Segments.Add(new SegmentInput(1, 0));
            reject("locked segment split at a free point, with an unlocked duplicate", Violation.LockedSplit, twice,
                Mesh(twice, new double[] { 4, 0 }, new[] { new[] { 0, 4, 2 }, new[] { 4, 1, 2 }, new[] { 0, 3, 4 }, new[] { 3, 1, 4 } }, (0, 4, 0), (4, 1, 0)), new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            var off = Points(0, 0, 8, 0, 8, 8, 0, 8, 1, 1, 7, 7, 1, 7, 7, 1);
            off.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); off.Segments.Add(new SegmentInput(4, 5)); off.Segments.Add(new SegmentInput(6, 7)); off.ResolveCrossings = false;
            reject("a mesh although segments cross with noding off", Violation.StatusInconsistent, off, Mesh(off, null, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }), new OracleExpect { Milestone = Milestone.M2 });
            var badRegion = Points(0, 0, 8, 0, 0, 8); badRegion.Regions.Add(new RegionInput { Ring = new[] { 0, 1, 99 } });
            reject("region ring out of range", Violation.InputInvalid, badRegion, Mesh(Points(0, 0, 8, 0, 0, 8), null, new[] { new[] { 0, 1, 2 } }), M1());
            reject("subsegment out of range", Violation.BadIndex, triangle, Mesh(triangle, null, new[] { new[] { 0, 1, 2 } }, (0, 99, 0)), new OracleExpect { Milestone = Milestone.M2 });

            // correct results it used to reject: the removed stretch of a ring wants no edges
            var onBorder = Points(0, 0, 10, 0, 10, 10, 0, 10, 0, 4, 3, 4, 3, 6, 0, 6);
            onBorder.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); onBorder.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
            var borderMesh = new[] { new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 2, 7, 6 }, new[] { 2, 3, 7 } };
            var borderSubs = new[] { (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 7, 0), (4, 0, 0), (4, 5, 0), (5, 6, 0), (6, 7, 0) };
            var borderReport = Oracle.Check(onBorder, Mesh(onBorder, null, borderMesh, borderSubs), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            must("accept: a hole on the outer boundary (F-43): no edges on its removed stretch", !borderReport.Has(Violation.SegmentMissing) && !borderReport.Has(Violation.AreaMismatch) && !borderReport.Has(Violation.BoundaryNotConstrained), borderReport.ToString());
            var onBorderPoint = Points(onBorder.X.SelectMany((x, i) => new[] { x, onBorder.Y[i] }).Concat(new double[] { 0, 5 }).ToArray());
            onBorderPoint.Rings = onBorder.Rings;
            var pointReport = Oracle.Check(onBorderPoint, Mesh(onBorderPoint, null, borderMesh, borderSubs), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            must("accept: an input point on that removed stretch needs no triangle", !pointReport.Has(Violation.VertexUnused), pointReport.ToString());

            // a foreign list that repeats an input point and uses its second copy
            var repeated = Points(0, 0, 8, 0, 0, 8);
            accept("foreign vertex list with a repeated point", repeated, new Mesh2Output {
                Status = Mesh2Status.Success, VX = new double[] { 0, 0, 8, 0 }, VY = new double[] { 0, 0, 0, 8 }, Triangles = new[] { new[] { 1, 2, 3 } }, IdentityRules = false }, M1());

            // survivors of the mutation check of the oracle: kinds that other broken results hid
            var flat = Points(0, 0, 8, 0, 4, 0, 4, 4);
            reject("a triangle without area", Violation.NotPositive, flat, new Mesh2Output {
                Status = Mesh2Status.Success, VX = flat.X, VY = flat.Y, InputToVertex = new[] { 0, 1, 2, 3 }, Triangles = new[] { new[] { 0, 1, 2 } } }, M1());
            var lone = Points(0, 0, 8, 0, 0, 8);
            reject("one triangle twice", Violation.EdgeRepeated, lone, Mesh(lone, null, new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 2 } }), M1());
            var book = Points(0, 0, 8, 0, 4, 4, 4, -4, 4, 8);
            reject("three triangles on one edge", Violation.EdgeRepeated, book, Mesh(book, null, new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 0, 1, 4 } }), M1());
            var lockedNeedle = Points(0, 0, 64, 0, 32, 1);
            lockedNeedle.Segments.Add(new SegmentInput(0, 1, 0, true)); lockedNeedle.MinAngleDeg = 20;
            var needleOutput = Mesh(lockedNeedle, null, new[] { new[] { 0, 1, 2 } }, (0, 1, 0));
            needleOutput.BadTriangles = new[] { 0 };
            accept("the angle bound is lifted at a locked segment", lockedNeedle, needleOutput, new OracleExpect { Milestone = Milestone.M3, Tube = Q.Zero });
            var overlapped = Points(0, 0, 8, 0, 4, 4, 4, -4);
            overlapped.Segments.Add(new SegmentInput(0, 1, 3)); overlapped.Segments.Add(new SegmentInput(1, 0, 4));
            var tris = new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 } };
            accept("two markers on one piece give 0", overlapped, Mesh(overlapped, null, tris, (0, 1, 0)), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            reject("two markers on one piece, one of them reported", Violation.SubsegmentMarker, overlapped, Mesh(overlapped, null, tris, (0, 1, 3)), new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });

            // a seed on a constraint is ignored
            var (gridIn, gridOut) = Grid(false);
            var withSeed = new Mesh2Input { X = gridIn.X, Y = gridIn.Y, Segments = gridIn.Segments, Rings = gridIn.Rings, Regions = gridIn.Regions.Concat(new[] { new RegionInput { Seed = new double[] { 3, 1 }, Label = 9 } }).ToList() };
            accept("a region seed on a segment is ignored", withSeed, gridOut, new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero });
            accept("the grid with the automatic tube", gridIn, gridOut, auto());
        }

        static SubsegmentOutput FreeEdge(Mesh2Output o, int minVertex)
        {
            var taken = new HashSet<(int, int)>(o.Subsegments.Select(s => (Math.Min(s.A, s.B), Math.Max(s.A, s.B))));
            foreach (var t in o.Triangles)
                for (int k = 0; k < 3; ++k) {
                    int a = t[k], b = t[(k + 1) % 3];
                    // a diagonal never lies on a constraint of the grid, and has no chance of being inside its tube
                    if (o.VX[a] != o.VX[b] && o.VY[a] != o.VY[b] && !taken.Contains((Math.Min(a, b), Math.Max(a, b))))
                        return new SubsegmentOutput(a, b, 0);
                }
            throw new InvalidOperationException("no free edge");
        }

        static int[][] RemoveHullTriangle(int[][] triangles)
        {
            var uses = new Dictionary<(int, int), int>();
            foreach (var t in triangles)
                for (int k = 0; k < 3; ++k) {
                    var key = (Math.Min(t[k], t[(k + 1) % 3]), Math.Max(t[k], t[(k + 1) % 3]));
                    uses.TryGetValue(key, out int n); uses[key] = n + 1;
                }
            int drop = Array.FindIndex(triangles, t => Enumerable.Range(0, 3).Count(k => uses[(Math.Min(t[k], t[(k + 1) % 3]), Math.Max(t[k], t[(k + 1) % 3]))] == 1) == 1);
            return triangles.Where((t, i) => i != drop).ToArray();
        }

        static void Arithmetic(Action<string, bool, string> must)
        {
            Pt P(double x, double y) => Pt.FromDoubles(x, y);
            must("orient: counter-clockwise", Exact.Orient(P(0, 0), P(1, 0), P(0, 1)) == 1, null);
            // Shewchuk 2002 section 6.3: near an input angle phi every angle of a triangle is at least arctan(sin phi / (2 - cos phi)).
            // phi = arctan(3/16) = 10.62 degrees gives 10.27 degrees; corners just above and just below it, and an obtuse one
            Pt apex = P(0, 0), wa = P(16, 0), wb = P(16, 3);
            must("Shewchuk bound: 18.7 degrees near a 10.62 degree input angle", Oracle.AtLeastShewchukBound(P(4.5, 0.84375), P(0, 0), P(3, 0), apex, wa, wb), null);
            must("Shewchuk bound: 10.40 degrees (tan 47/256) is above 10.27", Oracle.AtLeastShewchukBound(P(0, 0), P(1, 0), P(1, 47.0 / 256), apex, wa, wb), null);
            must("Shewchuk bound: 10.18 degrees (tan 23/128) is below 10.27", !Oracle.AtLeastShewchukBound(P(0, 0), P(1, 0), P(1, 23.0 / 128), apex, wa, wb), null);
            must("Shewchuk bound: an obtuse corner", Oracle.AtLeastShewchukBound(P(0, 0), P(1, 0), P(-1, 0.01), apex, wa, wb), null);
            must("orient: clockwise", Exact.Orient(P(0, 0), P(0, 1), P(1, 0)) == -1, null);
            must("orient: collinear", Exact.Orient(P(0, 0), P(1, 1), P(2, 2)) == 0, null);
            must("in-circle: inside", Exact.InCircle(P(0, 0), P(4, 0), P(0, 4), P(1, 1)) == 1, null);
            must("in-circle: outside", Exact.InCircle(P(0, 0), P(4, 0), P(0, 4), P(5, 5)) == -1, null);
            must("in-circle: on the circle", Exact.InCircle(P(0, 0), P(4, 0), P(4, 4), P(0, 4)) == 0, null);
            // the double filter of "vertex within the tube of a source", tube 1: within (0.42 off the segment), farther (1.41 off it),
            // undecided beyond the end on the line (1.41 from the end: the line alone does not decide), and undecided in its error
            // band: a source of length 1.4e15 with the point 1.2 off it, 4.95e14 along it, where the rounding bound is 0.5
            must("tube filter: within", Oracle.TubeFilter(0, 0, 4, 4, 2.3, 1.7, 1, 1) == 1, null);
            must("tube filter: farther", Oracle.TubeFilter(0, 0, 4, 4, 3, 1, 1, 1) == -1, null);
            must("tube filter: beyond the end on the line is not within", Oracle.TubeFilter(0, 0, 4, 4, 5, 5, 1, 1) == 0, null);
            must("tube filter: before the start on the line is not within", Oracle.TubeFilter(0, 0, 4, 4, -1, -1, 1, 1) == 0, null);
            // one product exactly zero is not both: (2, 1 + 2^-52) off the axis-parallel source, its distance squared just above 1
            must("tube filter: one zero product does not put a point on the line", Oracle.TubeFilter(0, 0, 4, 0, 2, 1 + Math.Pow(2, -52), 1, 1) != 1, null);
            // (found by the review's fuzzing) a point whose distance squared is 1.23: the cross product's rounding error decides it
            must("tube filter: the upper bound includes the cross product's error",
                Oracle.TubeFilter(0, 0, 8826902979211216.0, 2829010415293337.0, 3625859905102232.5, 1162083174595555.5, 1, 1) != 1, null);
            must("tube filter: inside its error band it does not decide", Oracle.TubeFilter(0, 0, 1e15, 1e15, 3.5e14, 3.5e14 + 1.7, 1, 1) == 0, null);
            must("tube filter: exactly on an axis-parallel source", Oracle.TubeFilter(0, 0, 4, 0, 2, 0, 1, 1) == 1, null);
            must("tube filter: on an axis-parallel source's line beyond its end is not within", Oracle.TubeFilter(0, 0, 4, 0, 5.5, 0, 1, 1) == 0, null);
            // one unit in the last place decides, where every double evaluation rounds to zero
            double up = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1e15 + 1) + 1);
            must("orient: one ulp at 1e15", Exact.Orient(P(0, 0), P(1e15, 1e15), P(1e15 + 1, up)) == 1, null);
            must("in-circle: one ulp at 1", Exact.InCircle(P(0, 0), P(1, 0), P(1, 1), P(0, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.0) + 1))) == -1, null);
            must("in-circle: scale 2^-1000 keeps the sign", Exact.InCircle(P(0, 0), P(Math.Pow(2, -1000), 0), P(0, Math.Pow(2, -1000)), P(Math.Pow(2, -1002), Math.Pow(2, -1002))) == 1, null);
            must("in-circle: homogeneous form equals the plain form", Exact.InCircle(new Pt(0, 0, 3), new Pt(12, 0, 3), new Pt(0, 12, 3), new Pt(2, 2, 2)) == 1, null);
            must("subnormal value is exact", Q.FromDouble(double.Epsilon) == new Q(1, BigInteger.One << 1074), null);
            must("0.1 is the double, not the decimal", Q.FromDouble(0.1) == new Q(3602879701896397, BigInteger.One << 55), null);
            must("negative value", Q.FromDouble(-0.75) == new Q(-3, 4), null);
            must("signed zero is zero", Q.FromDouble(-0.0) == Q.Zero, null);
            must("decimal text truncates", new Q(2, 3).ToDecimal(4) == "0.6666", new Q(2, 3).ToDecimal(4));
            must("sin^2 of the smallest angle of a right isosceles triangle", Exact.MinAngleSin2(P(0, 0), P(1, 0), P(1, 1)) == new Q(1, 2), null);
            must("sin^2 of a 3-4-5 triangle", Exact.MinAngleSin2(P(0, 0), P(4, 0), P(4, 3)) == new Q(9, 25), null);
            must("distance to a segment, interior", Exact.Dist2ToSegment(P(0, 0), P(4, 0), P(1, 3)) == new Q(9, 1), null);
            must("distance to a segment, beyond an end", Exact.Dist2ToSegment(P(0, 0), P(4, 0), P(7, 4)) == new Q(25, 1), null);
            must("hull drops collinear corners", Exact.ConvexHull(new[] { P(0, 0), P(2, 0), P(4, 0), P(4, 4), P(0, 4), P(2, 2) }).Count == 4, null);
        }

        static void DomainAreas(Action<string, bool, string> must)
        {
            Mesh2Input Rings(double[] xy, params (int[] ring, bool hole)[] rings)
            {
                var input = Points(xy);
                foreach (var r in rings) input.Rings.Add(new RingInput(r.ring, r.hole));
                return input;
            }
            int[] R(params int[] v) => v;
            // (both paths: the filtered one, with the shoelace for a single simple ring and the slabs over box-filtered pairs otherwise,
            // and the reference path, the slabs over all pairs)
            void Area(string name, Q expected, Mesh2Input input)
            {
                var domain = new Domain(input);
                must("domain area: " + name, domain.Area == expected, "got " + domain.Area);
                var reference = new Domain(input, false);
                must("domain area, reference path: " + name, reference.Area == expected, "got " + reference.Area);
            }
            double[] S(params double[] extra) => new double[] { 0, 0, 10, 0, 10, 10, 0, 10 }.Concat(extra).ToArray();
            Area("square, either orientation, closing point", 100, Rings(S(), (R(3, 2, 1, 0, 3), false)));
            Area("hull if no outer ring", 100, Points(S(5, 5)));
            Area("a collinear hull has no area", 0, Points(0, 0, 1, 1, 2, 2));
            Area("hole", 96, Rings(S(2, 2, 4, 2, 4, 4, 2, 4), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true)));
            Area("F-7 bowtie", new Q(1, 2), Rings(new double[] { 0, 0, 1, 1, 1, 0, 0, 1 }, (R(0, 1, 2, 3), false)));
            // a single ring that is not simple must not take the shoelace: a figure eight touching itself at (5, 5) (lobes turning the
            // same way: shoelace and parity agree, both paths must still get it), a lobe inside the square touching it at a vertex only
            // (parity: a hole of 3/2, the shoelace would add it: 203/2; review of the speed-up), a lobe inside the other with the same
            // turning (parity: a hole, the shoelace would add it), a spike back along an edge (zero width: area unchanged)
            Area("a ring touching itself at a vertex", 50, Rings(new double[] { 0, 0, 5, 5, 10, 0, 10, 10, 5, 5, 0, 10 }, (R(0, 1, 2, 3, 1, 5), false)));
            Area("a lobe touching the ring at a vertex only is a hole", new Q(197, 2), Rings(new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 2, 1, 1, 2 }, (R(0, 1, 2, 3, 0, 4, 5), false)));
            // a single hole ring (the outer ring has two points, no area): nothing is inside, also on the shoelace path (review)
            Area("a lone hole ring", 0, Rings(new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 20, 20, 30, 30 }, (R(0, 1, 2, 3), true), (R(4, 5), false)));
            Area("a lobe inside the ring, turning the same way, is a hole", 96, Rings(new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 2, 2, 4, 2, 4, 4, 2, 4 },
                (R(0, 1, 2, 3, 0, 4, 5, 6, 7, 4), false)));
            Area("a spike back along an edge", 100, Rings(new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 5, 10, 5, 15 }, (R(0, 1, 2, 4, 5, 4, 3), false)));
            // a star of 40 spikes, simple: the shoelace and the slabs agree (the area is the shoelace's, by construction)
            {
                var xy = new List<double>();
                for (int i = 0; i < 80; ++i) { double rad = i % 2 == 0 ? 8 : 1 + (i % 7) * 0.25, a = 2 * Math.PI * i / 80; xy.Add(rad * Math.Cos(a)); xy.Add(rad * Math.Sin(a)); }
                var star = Rings(xy.ToArray(), (Enumerable.Range(0, 80).ToArray(), false));
                Q twice = Q.Zero;
                for (int i = 0; i < 80; ++i) { int j = (i + 1) % 80; twice += Q.FromDouble(xy[2 * i]) * Q.FromDouble(xy[2 * j + 1]) - Q.FromDouble(xy[2 * j]) * Q.FromDouble(xy[2 * i + 1]); }
                Area("a simple star of 40 spikes", Q.Abs(twice) * new Q(1, 2), star);
            }
            Area("F-43 touching holes", 92, Rings(S(2, 2, 4, 2, 4, 4, 2, 4, 6, 2, 6, 4), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true), (R(5, 8, 9, 6), true)));
            Area("F-43 hole on the boundary", 94, Rings(S(0, 4, 3, 4, 3, 6, 0, 6), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true)));
            Area("F-44 a square in a hole does not fill back", 64, Rings(S(2, 2, 8, 2, 8, 8, 2, 8, 4, 4, 6, 4, 6, 6, 4, 6), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true), (R(8, 9, 10, 11), true)));
            Area("F-48 folded hole ring", 100, Rings(S(2, 2, 8, 8, 5, 5), (R(0, 1, 2, 3), false), (R(4, 5, 6), true)));
            Area("F-48 back and forth", 100, Rings(S(2, 2, 8, 8), (R(0, 1, 2, 3), false), (R(4, 5, 4), true)));
            Area("overlapping outer rings unite", 150, Rings(S(5, 0, 15, 0, 15, 10, 5, 10), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), false)));
            Area("a hole partly outside", 92, Rings(S(8, 4, 12, 4, 12, 8, 8, 8), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true)));
            var classify = new Domain(Rings(S(2, 2, 4, 2, 4, 4, 2, 4), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true)));
            must("classify: inside", classify.Classify(Pt.FromDoubles(1, 1)) == Where.Inside, null);
            must("classify: in the hole", classify.Classify(Pt.FromDoubles(3, 3)) == Where.Outside, null);
            must("classify: on the hole's ring", classify.Classify(Pt.FromDoubles(2, 3)) == Where.Boundary, null);
            must("classify: on the outer ring", classify.Classify(Pt.FromDoubles(10, 3)) == Where.Boundary, null);
            must("classify: outside", classify.Classify(Pt.FromDoubles(11, 3)) == Where.Outside, null);
        }

        static void CrossingAcceptance(Action<string, bool, string> must, Mesh2Input cross)
        {
            // segments (1,1)-(4,5) and (1,4)-(5,1) cross at a rational point: 4t = ..., solved exactly here
            Pt a = Pt.FromDoubles(1, 1), b = Pt.FromDoubles(4, 5), c = Pt.FromDoubles(1, 4), d = Pt.FromDoubles(5, 1);
            Q ux = b.Qx - a.Qx, uy = b.Qy - a.Qy, wx = d.Qx - c.Qx, wy = d.Qy - c.Qy;
            Q t = ((c.Qx - a.Qx) * wy - (c.Qy - a.Qy) * wx) / (ux * wy - uy * wx);
            Pt x = new Pt(a.Qx + ux * t, a.Qy + uy * t);
            must("that crossing is not dyadic", x.W > 1 && !x.W.IsPowerOfTwo, x.ToString());

            var triangles = new[] {
                new[] {0,1,7}, new[] {1,2,7}, new[]{2,5,7}, new[]{7,5,8}, new[]{7,8,4}, new[]{0,7,4},
                new[]{0,4,6}, new[]{0,6,3}, new[]{3,6,5}, new[]{3,5,2}, new[]{6,8,5}, new[]{4,8,6} };
            var exactV = Enumerable.Range(0, 8).Select(i => Pt.FromDoubles(cross.X[i], cross.Y[i])).Concat(new[] { x }).ToArray();
            var subs = new List<SubsegmentOutput> {
                new SubsegmentOutput(0,1), new SubsegmentOutput(1,2), new SubsegmentOutput(2,3), new SubsegmentOutput(3,0),
                new SubsegmentOutput(4,8,1), new SubsegmentOutput(8,5,1), new SubsegmentOutput(6,8,2), new SubsegmentOutput(8,7,2) };
            double xd = (double)x.X / (double)x.W, yd = (double)x.Y / (double)x.W;
            var output = new Mesh2Output {
                Status = Mesh2Status.Success, ExactVertices = exactV,
                VX = cross.X.Concat(new[] { xd }).ToArray(), VY = cross.Y.Concat(new[] { yd }).ToArray(),
                InputToVertex = Enumerable.Range(0, 8).ToArray(), Triangles = triangles, Neighbours = NeighboursOf(triangles), Subsegments = subs };
            var strict = new OracleExpect { Milestone = Milestone.M2, Tube = Q.Zero };
            var report = Oracle.Check(cross, output, strict);
            // the free edges of this hand-made mesh need not be Delaunay: only the geometric kinds matter here
            must("accept: a rational crossing vertex lies exactly on both segments", !report.Has(Violation.SegmentMissing) && !report.Has(Violation.NotPositive) && !report.Has(Violation.AreaMismatch), report.ToString());

            var doubles = new Mesh2Output {
                Status = Mesh2Status.Success, VX = output.VX, VY = output.VY, InputToVertex = output.InputToVertex,
                Triangles = triangles, Neighbours = output.Neighbours, Subsegments = subs };
            var rounded = Oracle.Check(cross, doubles, strict);
            must("reject: the same crossing rounded to doubles is not on the segment, tube 0", rounded.Has(Violation.SegmentMissing), rounded.ToString());
            var tube = new OracleExpect { Milestone = Milestone.M2, Tube = new Q(1, BigInteger.One << 40) };
            var tubeReport = Oracle.Check(cross, doubles, tube);
            must("accept: the rounded crossing inside a tube", !tubeReport.Has(Violation.SegmentMissing), tubeReport.ToString());
        }
    }
}
