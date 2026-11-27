// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// The size estimate of the alpha wrap on open input and the budget WrapMaxEstimatedTriangles (issue #1113, README.md): the reproducer
// (two overlapping unwelded spheres, 12 edge vertices per cube side, 1.2 apart in x, the second shifted 0.3 in z, the axis caps whose
// centroid direction is within 0.5 rad of an axis removed), the same two spheres closed, and a flat open sheet. First the wrap alone, then through
// MeshRobustRepair at the budgets of the table. Columns: seconds of Apply(), AlphaWrap.EstimatedTriangles (before the refinement),
// raw wrap triangles, the ratio raw / budget (raw / estimate without a budget), the alpha used relative to the diagonal, the result triangles.
//     dotnet run --project benchmarks/OpenSoupWrapBenchmark -c Release [-- --quick]   (--quick: the first two budgets of each input only)
static class Program
{
    static DMesh3 Sphere(Vector3d center)
    {
        var g = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 12 };
        g.Generate();
        DMesh3 m = g.MakeDMesh();
        foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) + center);
        return m;
    }

    // a soup (own vertices per triangle) of the triangles of the meshes, without the caps if cut
    static DMesh3 Soup(IEnumerable<DMesh3> meshes, bool cut)
    {
        var soup = new DMesh3();
        double lim = Math.Cos(0.5);
        foreach (DMesh3 m in meshes)
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                if (cut) {
                    Vector3d d = ((a + b + c) / 3 - m.GetBounds().Center).Normalized;
                    if (Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))) > lim) continue;
                }
                soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            }
        return soup;
    }

    static DMesh3 Sheet(int n, double side)
    {
        var m = new DMesh3();
        for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i * side / n, j * side / n, 0));
        for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
            int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
            m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
        }
        return m;
    }

    // W04-gap-2 from TargetedMeshRegression. The two solids have an exact 1/32 gap.
    const double Gap = 1.0 / 32;
    static readonly Vector3d UtmShift = new Vector3d(632117.25, 5712043.5, 113.75);

    static DMesh3 GapInput(double scale)
    {
        var input = new DMesh3();
        var corners = new[] {
            new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
            new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0, 1, 1),
        };
        var quads = new[] { (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                            (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7) };
        foreach (double x in new[] { 0.0, 1 + Gap }) {
            int first = input.VertexCount;
            foreach (var corner in corners) input.AppendVertex(scale * (corner + new Vector3d(x, 0, 0)));
            foreach (var (a, b, c, d) in quads) {
                input.AppendTriangle(first + a, first + b, first + c);
                input.AppendTriangle(first + a, first + c, first + d);
            }
        }
        return input;
    }

    static void GapBudget()
    {
        var input = GapInput(1);
        var gapCenter = new Vector3d(1 + Gap / 2, 0.5, 0.5);
        var cases = new[] { (Budget: 0, Cap: 0.0), (Budget: 50000, Cap: 0.0), (Budget: 50000, Cap: 1.5) };
        Console.WriteLine("budget,cap,estimate_at_request,alpha,offset,coarsening,triangles,seconds,status,clean,components,gap_center_inside");
        foreach (var settings in cases) {
            var wrap = new MeshAlphaWrap(input) {
                Alpha = Gap / 4,
                Offset = Gap / 40,
                MaxThreads = 1,
                MaxEstimatedTriangles = settings.Budget,
                MaxAlphaCoarsening = settings.Cap,
            };
            var stopwatch = Stopwatch.StartNew();
            wrap.Compute();
            stopwatch.Stop();
            var result = wrap.Result;
            bool clean = result != null && string.IsNullOrEmpty(MeshRobustRepair.NotCleanReason(result));
            var components = clean ? new MeshConnectedComponents(result) : null;
            components?.FindConnectedT();
            bool? gapCenterInside = clean ? new DMeshAABBTree3(result, true).IsInside(gapCenter) : (bool?)null;
            Console.WriteLine(FormattableString.Invariant(
                $"{settings.Budget},{settings.Cap},{wrap.EstimatedTriangles:R},{wrap.UsedAlpha:R},{wrap.UsedOffset:R},{wrap.AlphaCoarsening:R},{result?.TriangleCount},{stopwatch.Elapsed.TotalSeconds:F6},{wrap.Status},{clean},{components?.Count},{gapCenterInside}"));
        }
    }

    // The public API accepts explicit alpha and offset independently of the coordinate range.
    static void BudgetRange()
    {
        double tiny = Math.Pow(2, -550);
        var cases = new[] {
            (Name: "unit", Scale: 1.0, Alpha: Gap / 4, Offset: Gap / 32),
            (Name: "small_mesh", Scale: Math.Pow(2, -126), Alpha: Gap / 4, Offset: Gap / 32),
            (Name: "large_mesh", Scale: Math.Pow(2, 126), Alpha: Gap / 4, Offset: Gap / 32),
            (Name: "square_underflows", Scale: 1.0, Alpha: tiny, Offset: tiny / 8),
            (Name: "factor_overflows", Scale: 1.0, Alpha: double.Epsilon, Offset: double.Epsilon),
        };
        Console.WriteLine("case,scale,requested_alpha,requested_offset,budget,estimate_at_request,coarsening,used_alpha,used_offset,triangles,status,clean,error");
        foreach (var c in cases) {
            var result = RunRangeCase(c.Name, c.Scale, c.Scale * c.Alpha, c.Scale * c.Offset, 50000);
            if (c.Name == "factor_overflows" && MathUtil.IsFinite(result.UsedAlpha) && result.UsedAlpha > 0) {
                // The requested offset/alpha is exactly one. Replay the intended coarsened values
                // without constructing the unrepresentable factor. This exercises the remaining wrap.
                RunRangeCase("factor_overflow_replay_ratio_one", c.Scale, result.UsedAlpha, result.UsedAlpha, 0);
            }
        }
    }

    static void WeldRange()
    {
        Console.WriteLine("case,scale,requested_alpha,requested_offset,budget,estimate_at_request,coarsening,used_alpha,used_offset,triangles,status,clean,error");
        foreach (var (name, alpha) in new[] { ("zero_cell", double.Epsilon), ("subnormal_cell", Math.Pow(2, -1058)) }) {
            RunRangeCase(name + "_origin", 1, alpha, alpha, 50000);
            RunRangeCase(name + "_utm", 1, alpha, alpha, 50000, UtmShift);
        }
    }

    static void FrameAudit()
    {
        // The explicit parameters match --weld-range and bypass budget coarsening.
        const double alpha = 0.026868839879520764;
        AxisAlignedBox3d bounds = GapInput(1).CachedBounds;
        // MeshAlphaWrap.setup uses 4 * alpha + the diagonal term when offset == alpha.
        double padding = 1e-3 * bounds.DiagonalLength;
        double dyadicAlpha = (0.125 - padding) / 4;
        Console.WriteLine("seed,phases,frame,alpha,initial_box_roundtrip_max_axis,first_steiner_x,first_steiner_y,first_steiner_z,rule1,rule2,carved_cells,pinch_points,quality_points,box_retries,triangles,status,clean,components,cube_centers_inside,gap_center_inside,vertex_sample_distance,identical_local_mesh,centered_output_roundtrip_max_axis,restored_output_clean");
        RunFrameSeries("ordinary", alpha, padding, true, true);
        RunFrameSeries("ordinary", alpha, padding, false, true);
        RunFrameSeries("dyadic_margin", dyadicAlpha, padding, false, false);
    }

    static void RunFrameSeries(string seed, double alpha, double padding, bool refine, bool includeCentered)
    {
        DMesh3 reference = null;
        var frames = includeCentered ? new[] { "origin", "utm", "utm_centered" } : new[] { "origin", "utm" };
        foreach (string frame in frames) {
            DMesh3 input = GapInput(1);
            if (frame != "origin")
                MeshTransforms.Translate(input, UtmShift);
            if (frame == "utm_centered")
                MeshTransforms.Translate(input, -UtmShift);
            Vector3d shift = frame == "utm" ? UtmShift : Vector3d.Zero;
            double margin = 4 * alpha + padding;
            AxisAlignedBox3d box = input.CachedBounds;
            box.Expand(margin);
            AxisAlignedBox3d localBox = GapInput(1).CachedBounds;
            localBox.Expand(margin);
            double boxDrift = Math.Max((box.Min - shift - localBox.Min).MaxAbs, (box.Max - shift - localBox.Max).MaxAbs);
            if (seed == "dyadic_margin" && boxDrift != 0)
                throw new InvalidOperationException("The dyadic control requires exactly translated seed corners.");
            Vector3d first = FirstSteiner(input, alpha, box) - shift;

            var wrap = new MeshAlphaWrap(input) {
                Alpha = alpha, Offset = alpha, MaxThreads = 1,
                RefineNonManifold = refine, RefineMinAngleDegrees = refine ? 15 : 0
            };
            if (!wrap.Compute() || !string.IsNullOrEmpty(MeshRobustRepair.NotCleanReason(wrap.Result)))
                throw new InvalidOperationException($"{seed}, {frame}: {wrap.Status} {wrap.Error}");
            DMesh3 result = wrap.Result;
            if (shift != Vector3d.Zero) {
                MeshTransforms.Translate(result, -shift);
                if (!string.IsNullOrEmpty(MeshRobustRepair.NotCleanReason(result)))
                    throw new InvalidOperationException("The inverse translation introduced a mesh defect.");
            }
            reference ??= result;

            var tree = new DMeshAABBTree3(result, true);
            var components = new MeshConnectedComponents(result);
            components.FindConnectedT();
            bool cubesInside = tree.IsInside(new Vector3d(0.5, 0.5, 0.5)) && tree.IsInside(new Vector3d(1.5 + Gap, 0.5, 0.5));
            bool gapInside = tree.IsInside(new Vector3d(1 + Gap / 2, 0.5, 0.5));
            bool identical = SameMesh(reference, result);
            double distance = identical ? 0 : VertexSampleDistance(reference, result, tree);
            double? centeredOutputDrift = null;
            bool? restoredClean = null;
            if (frame == "utm_centered") {
                var restored = new DMesh3(result);
                MeshTransforms.Translate(restored, UtmShift);
                restoredClean = string.IsNullOrEmpty(MeshRobustRepair.NotCleanReason(restored));
                if (!identical || restoredClean != true)
                    throw new InvalidOperationException("The centered control did not reproduce a clean origin mesh.");
                centeredOutputDrift = result.VertexIndices().Max(v => (restored.GetVertex(v) - UtmShift - result.GetVertex(v)).MaxAbs);
            }
            Console.WriteLine(FormattableString.Invariant(
                $"{seed},{(refine ? "full" : "carve")},{frame},{alpha:R},{boxDrift:R},{first.x:R},{first.y:R},{first.z:R},{Count(wrap, "steiner_intersection")},{Count(wrap, "steiner_projection")},{Count(wrap, "carved_cells")},{Count(wrap, "pinch_points")},{Count(wrap, "quality_points")},{Count(wrap, "box_retries")},{result.TriangleCount},{wrap.Status},True,{components.Count},{cubesInside},{gapInside},{distance:R},{identical},{centeredOutputDrift:R},{restoredClean}"));
        }
    }

    static Vector3d FirstSteiner(DMesh3 input, double alpha, AxisAlignedBox3d initialBox)
    {
        var wrap = new MeshAlphaWrap(input) {
            Alpha = alpha, Offset = alpha, MaxThreads = 1, MaxSteinerPoints = 1,
            RefineNonManifold = false, RefineMinAngleDegrees = 0
        };
        wrap.Compute();
        if (wrap.Status != MeshAlphaWrap.WrapStatus.PointLimitReached || wrap.Result?.VertexCount != 9 ||
            Count(wrap, "steiner_intersection") + Count(wrap, "steiner_projection") != 1)
            throw new InvalidOperationException("Expected a capped result with the eight box corners and one Steiner point.");

        var corners = Enumerable.Range(0, 8)
            .Select(i => initialBox.Point((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))
            .ToArray();
        Vector3d[] vertices = wrap.Result.Vertices().ToArray();
        if (!corners.All(c => vertices.Any(v => SameCoordinateBits(c, v))))
            throw new InvalidOperationException("The capped result did not recover all eight expected box corners.");
        return vertices.Single(v => !corners.Any(c => SameCoordinateBits(c, v)));
    }

    static long Count(MeshAlphaWrap wrap, string name) => wrap.Counts.TryGetValue(name, out long value) ? value : 0;

    static bool SameMesh(DMesh3 a, DMesh3 b) =>
        a.VertexIndices().SequenceEqual(b.VertexIndices()) && a.TriangleIndices().SequenceEqual(b.TriangleIndices()) &&
        a.VertexIndices().All(v => SameCoordinateBits(a.GetVertex(v), b.GetVertex(v))) &&
        a.TriangleIndices().All(t => a.GetTriangle(t) == b.GetTriangle(t));

    static bool SameCoordinateBits(Vector3d a, Vector3d b) =>
        BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) &&
        BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y) &&
        BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z);

    static double VertexSampleDistance(DMesh3 reference, DMesh3 result, DMeshAABBTree3 resultTree)
    {
        double MaxDistanceSquared(DMesh3 source, DMeshAABBTree3 target)
        {
            double maximum = 0;
            foreach (Vector3d vertex in source.Vertices()) {
                target.FindNearestTriangle(vertex, out double distance);
                maximum = Math.Max(maximum, distance);
            }
            return maximum;
        }

        return Math.Sqrt(Math.Max(MaxDistanceSquared(reference, resultTree),
            MaxDistanceSquared(result, new DMeshAABBTree3(reference, true))));
    }

    static MeshAlphaWrap RunRangeCase(string name, double scale, double alpha, double offset, int budget, Vector3d shift = default)
    {
        DMesh3 input = GapInput(scale);
        if (shift != Vector3d.Zero)
            MeshTransforms.Translate(input, shift);
        var wrap = new MeshAlphaWrap(input) {
            Alpha = alpha, Offset = offset, MaxEstimatedTriangles = budget, MaxThreads = 1,
        };
        wrap.Compute();
        bool clean = wrap.Result != null && string.IsNullOrEmpty(MeshRobustRepair.NotCleanReason(wrap.Result));
        Console.WriteLine(FormattableString.Invariant(
            $"{name},{scale:R},{alpha:R},{offset:R},{budget},{wrap.EstimatedTriangles:R},{wrap.AlphaCoarsening:R},{wrap.UsedAlpha:R},{wrap.UsedOffset:R},{wrap.Result?.TriangleCount},{wrap.Status},{clean},{wrap.Error}"));
        return wrap;
    }

    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--gap-budget") {
            GapBudget();
            return;
        }
        if (args.Length == 1 && args[0] == "--budget-range") {
            BudgetRange();
            return;
        }
        if (args.Length == 1 && args[0] == "--weld-range") {
            WeldRange();
            return;
        }
        if (args.Length == 1 && args[0] == "--frame-audit") {
            FrameAudit();
            return;
        }
        bool quick = args.Contains("--quick");
        Vector3d shift = new Vector3d(1.2, 0, 0.3);
        var inputs = new List<(string, DMesh3)> {
            ("two open spheres (#1113)", Soup(new[] { Sphere(Vector3d.Zero), Sphere(shift) }, true)),
            ("two closed spheres", Soup(new[] { Sphere(Vector3d.Zero), Sphere(shift) }, false)),
            ("open sheet 4 x 4", Sheet(60, 4)),
        };
        // 1. the wrap alone (MeshAlphaWrap, relative alpha 100, offset 1/1000), no budget: the estimate against the raw wrap
        Console.WriteLine("input,estimate,raw_triangles,raw_over_estimate,seconds");
        foreach (var (name, input) in inputs) {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 100, RelativeOffset = 1000 };
            var sw = Stopwatch.StartNew();
            bool ok = w.Compute();
            Console.WriteLine($"{name},{w.EstimatedTriangles:F0},{w.Result?.TriangleCount},{(w.Result?.TriangleCount / w.EstimatedTriangles):F2},{sw.Elapsed.TotalSeconds:F1}");
        }
        // 3. the cost of the open-area estimate on a large input (Compute() with a coarse alpha, so that the wrap itself is small): no budget
        // (after: the estimate is not computed) against a budget that never binds (which runs it, as every Compute() did before this change)
        Console.WriteLine("input_triangles,budget,seconds,estimate");
        foreach (int n in quick ? new[] { 300 } : new[] { 300, 500, 700 })
            foreach (int budget in new[] { 0, int.MaxValue }) {
                DMesh3 big = Sheet(n, 4);
                var w = new MeshAlphaWrap(big) { RelativeAlpha = 8, RelativeOffset = 80, MaxEstimatedTriangles = budget };
                var sw = Stopwatch.StartNew();
                bool ok = w.Compute();
                if (!ok) throw new InvalidOperationException(w.Error);
                Console.WriteLine($"{big.TriangleCount},{budget},{sw.Elapsed.TotalSeconds:F2},{w.EstimatedTriangles:F0}");
            }
        // 2. MeshRobustRepair at library defaults (alpha diagonal / 300) with WrapMaxEstimatedTriangles: only the open inputs reach the wrap
        Console.WriteLine("input,budget,seconds,estimate,raw_triangles,raw_over_budget_or_estimate,alpha_rel,result_triangles");
        foreach (var (name, input) in inputs.Where(i => i.Item1 != "two closed spheres"))
            foreach (int budget in new[] { 0, 200000, 100000, 50000 }.Take(quick ? 2 : 4)) {
                var r = new MeshRobustRepair(new DMesh3(input)) { WrapMaxEstimatedTriangles = budget };
                var sw = Stopwatch.StartNew();
                bool ok = r.Apply();
                double s = sw.Elapsed.TotalSeconds;
                double diag = input.GetBounds().DiagonalLength;
                double est = r.AlphaWrap != null ? r.AlphaWrap.EstimatedTriangles : double.NaN;
                Console.WriteLine($"{name},{budget},{s:F1},{est:F0},{r.AlphaWrapTriangles},{(r.AlphaWrapTriangles / (budget > 0 ? budget : est)):F2},{(r.AlphaWrap != null ? r.AlphaWrap.UsedAlpha / diag : double.NaN):G3},{r.Mesh?.TriangleCount}");
            }
    }
}
