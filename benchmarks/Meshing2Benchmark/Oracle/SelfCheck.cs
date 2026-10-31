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
            var needlePoints = Points(0, 0, 64, 0, 64, 1); needlePoints.MinAngleDeg = 20;
            var free = Clone(needleOut); free.Subsegments = new List<SubsegmentOutput>();
            Reject("the same small angle, not forced", Violation.AngleBelowBound, needlePoints, free, M3());

            ReviewFindings(Must, Accept, Reject);
            return items;
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
            must("orient: clockwise", Exact.Orient(P(0, 0), P(0, 1), P(1, 0)) == -1, null);
            must("orient: collinear", Exact.Orient(P(0, 0), P(1, 1), P(2, 2)) == 0, null);
            must("in-circle: inside", Exact.InCircle(P(0, 0), P(4, 0), P(0, 4), P(1, 1)) == 1, null);
            must("in-circle: outside", Exact.InCircle(P(0, 0), P(4, 0), P(0, 4), P(5, 5)) == -1, null);
            must("in-circle: on the circle", Exact.InCircle(P(0, 0), P(4, 0), P(4, 4), P(0, 4)) == 0, null);
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
            void Area(string name, Q expected, Mesh2Input input)
            {
                var domain = new Domain(input);
                must("domain area: " + name, domain.Area == expected, "got " + domain.Area);
            }
            double[] S(params double[] extra) => new double[] { 0, 0, 10, 0, 10, 10, 0, 10 }.Concat(extra).ToArray();
            Area("square, either orientation, closing point", 100, Rings(S(), (R(3, 2, 1, 0, 3), false)));
            Area("hull if no outer ring", 100, Points(S(5, 5)));
            Area("a collinear hull has no area", 0, Points(0, 0, 1, 1, 2, 2));
            Area("hole", 96, Rings(S(2, 2, 4, 2, 4, 4, 2, 4), (R(0, 1, 2, 3), false), (R(4, 5, 6, 7), true)));
            Area("F-7 bowtie", new Q(1, 2), Rings(new double[] { 0, 0, 1, 1, 1, 0, 0, 1 }, (R(0, 1, 2, 3), false)));
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
