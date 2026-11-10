using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using gs;
using Meshing2Acceptance;

namespace MeshFuzzPilot;

/// <summary>
/// The second wave of targets (QA 2.0 T13): Remesher, Reducer, QualityMesh2 (judged by the acceptance oracle of the mesher) and the
/// Clipper2 adapter. As for the first wave, every claim is a function of plain results, so that the self-test can hand it a
/// contradiction. The operations run on the power-of-two normalized copy of the case's mesh A (its box diagonal in [1, 2)): the
/// Remesher and the Reducer have been judged at the scales 2^-30 .. 2^6 by the campaigns; this target looks for the topology and
/// the corners of the shapes. The options (a few flags, the target size) are four raw bytes after the queries.
/// </summary>
public static partial class Program
{
    /// <summary> the k-th option byte of a case (four raw bytes after the queries) </summary>
    static int ParamByte(Case c, int k) => c.Options[k & 3];

    static double MeanEdge(DMesh3 m)
    {
        double sum = 0; int n = 0;
        foreach (int e in m.EdgeIndices()) {
            Index2i ev = m.GetEdgeV(e);
            sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    static int EulerCharacteristic(DMesh3 m) => m.VertexCount - m.EdgeCount + m.TriangleCount;

    static int ComponentCount(DMesh3 m)
    {
        var cc = new MeshConnectedComponents(m);
        cc.FindConnectedT();
        return cc.Count;
    }

    // ---- the Remesher ----

    /// <summary>
    /// Remesher (a few passes on A subdivided once; flips, collapses, splits, smoothing, parallel smoothing and PreventNormalFlips
    /// from the bytes): the result is valid and finite; a closed input stays closed with the same Euler characteristic and number of
    /// components; the parallel and the serial smoothing, and a second run, give the same mesh bit for bit
    /// </summary>
    public static void CheckRemesh(string what, bool inputClosed, DMesh3 input, DMesh3 result, DMesh3 other, DMesh3 again)
    {
        if (!AllFinite(result))
            throw new Finding($"{what}: a vertex of the remeshed mesh is not finite");
        if (!result.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the remeshed mesh is not valid");
        if (result.TriangleCount == 0)
            throw new Finding($"{what}: the remeshed mesh is empty");
        if (inputClosed) {
            if (!result.IsClosed())
                throw new Finding($"{what}: a closed input, but the remeshed mesh is not closed");
            if (ComponentCount(result) != ComponentCount(input))
                throw new Finding($"{what}: {ComponentCount(input)} components became {ComponentCount(result)}");
            if (EulerCharacteristic(result) != EulerCharacteristic(input))
                throw new Finding($"{what}: the Euler characteristic changed from {EulerCharacteristic(input)} to {EulerCharacteristic(result)}");
        }
        if (!SameMesh(result, other, 1))
            throw new Finding($"{what}: parallel and serial smoothing give different meshes");
        if (!SameMesh(result, again, 1))
            throw new Finding($"{what}: two runs give different meshes");
    }

    static void RunRemesher(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 input = Subdivide(s.Norm);
        if (!input.CheckValidity(true, FailMode.ReturnOnly))
            return;     // (a degenerate input, eg coincident corners: its subdivision is not a valid mesh, the claims have nothing to be judged on)
        double mean = MeanEdge(input);
        if (!(mean > 0))
            return;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1);
        double[] factors = { 0.45, 0.7, 1.0, 1.6 };
        DMesh3 Run(bool parallel)
        {
            var m = new DMesh3(input);
            var r = new Remesher(m) {
                EnableParallelSmooth = parallel, SmoothSpeedT = 0.5,
                EnableFlips = (p1 & 1) == 0, EnableCollapses = (p1 & 2) == 0, EnableSplits = (p1 & 4) == 0, EnableSmoothing = (p1 & 8) == 0,
                PreventNormalFlips = (p1 & 16) != 0
            };
            r.SetTargetEdgeLength(factors[p0 & 3] * mean);
            for (int k = 0; k < 3; ++k)
                r.BasicRemeshPass();
            return m;
        }
        bool parallelFirst = (p0 & 4) != 0;
        DMesh3 a = Run(parallelFirst), b = Run(!parallelFirst), again = Run(parallelFirst);
        CheckRemesh(what, input.IsClosed(), input, a, b, again);
    }

    // ---- the Reducer ----

    /// <summary>
    /// Reducer (ReduceToTriangleCount on A subdivided twice; quadric position, area weights, PreventFoldOver and a MaxDeviation from
    /// the bytes): the result is valid and finite, no larger than the input; a closed input stays closed with the same Euler
    /// characteristic and components; with MaxDeviation every vertex is within it of the input surface; a second run and the copy
    /// scaled by 2^40 give the same mesh (scaled) bit for bit
    /// </summary>
    public static void CheckReduce(string what, bool inputClosed, DMesh3 input, DMesh3 result, DMesh3 again, DMesh3? scaled, double scale, double deviation)
    {
        if (!AllFinite(result))
            throw new Finding($"{what}: a vertex of the reduced mesh is not finite");
        if (!result.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the reduced mesh is not valid");
        if (result.TriangleCount > input.TriangleCount)
            throw new Finding($"{what}: {result.TriangleCount} triangles from {input.TriangleCount}");
        if (inputClosed && result.TriangleCount > 0) {
            if (!result.IsClosed())
                throw new Finding($"{what}: a closed input, but the reduced mesh is not closed");
            if (ComponentCount(result) != ComponentCount(input))
                throw new Finding($"{what}: {ComponentCount(input)} components became {ComponentCount(result)}");
            if (EulerCharacteristic(result) != EulerCharacteristic(input))
                throw new Finding($"{what}: the Euler characteristic changed from {EulerCharacteristic(input)} to {EulerCharacteristic(result)}");
        }
        if (deviation > 0 && result.TriangleCount > 0) {
            var tree = new DMeshAABBTree3(input, true);
            foreach (int v in result.VertexIndices()) {
                tree.FindNearestTriangle(result.GetVertex(v), out double d2);
                if (Math.Sqrt(d2) > deviation * (1 + 1e-9))
                    throw new Finding($"{what}: vertex {v} is {Math.Sqrt(d2)} from the input, MaxDeviation {deviation}");
            }
        }
        if (!SameMesh(result, again, 1))
            throw new Finding($"{what}: two runs give different meshes");
        if (scaled != null && !SameMesh(result, scaled, 1 / scale))
            throw new Finding($"{what}: the result for the copy scaled by {scale} is not the scaled result ({result.VertexCount} vertices / {result.TriangleCount} triangles against {scaled.VertexCount} / {scaled.TriangleCount})");
    }

    static void RunReducer(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 input = Subdivide(Subdivide(s.Norm));
        if (!input.CheckValidity(true, FailMode.ReturnOnly))
            return;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1);
        double diag = Diagonal(input.GetBounds());
        double deviation = (p1 & 8) != 0 ? 0.02 * diag : 0;
        double[] fractions = { 0.25, 0.5, 0.75 };
        int target = Math.Max(4, (int)(fractions[p0 % 3] * input.TriangleCount));
        DMesh3 Run(DMesh3 src, double dev)
        {
            var m = new DMesh3(src);
            var r = new Reducer(m) {
                MinimizeQuadricPositionError = (p1 & 1) == 0, AreaWeightedQuadrics = (p1 & 2) == 0, PreventFoldOver = (p1 & 4) == 0, MaxDeviation = dev
            };
            r.ReduceToTriangleCount(target);
            return m;
        }
        DMesh3 a = Run(input, deviation), again = Run(input, deviation);
        // the scale claim holds inside the Reducer's range (QA.md 2.5a: nonzero coordinates 2^-200 .. 2^200; the copy is 2^40 larger): a mesh
        // with a coordinate of a tiny magnitude beside large ones (the case's offset against its extent) is judged without it
        double k = Pow2(40);
        bool inRange = input.Vertices().All(p => new[] { p.x, p.y, p.z }.All(v => v == 0 || (Math.Abs(v) >= Pow2(-200) && Math.Abs(v) <= Pow2(150))));
        DMesh3? scaled = null;
        if (inRange) {
            DMesh3 big = new DMesh3(input);
            foreach (int v in big.VertexIndices()) big.SetVertex(v, k * big.GetVertex(v));
            scaled = Run(big, deviation * k);
        }
        CheckReduce(what, input.IsClosed(), input, a, again, scaled, k, deviation);
    }

    // ---- QualityMesh2 ----

    /// <summary>
    /// QualityMesh2 on the projection of A to the xy plane (its corners as points, the edges of its triangles as segments, crossings
    /// resolved; a minimum angle, conforming or constrained quality mode and a Steiner limit from the bytes), judged by the
    /// acceptance oracle of the mesher (milestone M3): the status is not Failed and the oracle reports no violation
    /// </summary>
    public static void CheckMesher2(string what, string status, string failureKind, bool oracleOk, string violations)
    {
        if (status == "Failed")
            throw new Finding($"{what}: status Failed ({failureKind})");
        if (!oracleOk)
            throw new Finding($"{what}: the oracle reports {violations}");
    }

    /// <summary>
    /// The open findings of the 2D mesher that this target meets (FINDINGS.md, second wave), so that a campaign goes on to other findings;
    /// MESH_FUZZ_STRICT=1 turns the gate off. 1: AngleBelowBound only, with a minimum angle, in any quality mode (about 1 in 700
    /// random inputs, a projected polyhedron with 17 to 36 crossing segments, the Steiner limit not reached). 2: VertexUnused / SegmentMissing only,
    /// for coordinates more than 2^16 extents from the origin (the automatic snap grid).
    /// </summary>
    static string? KnownMesher2Class(Mesh2Input input, OracleReport report)
    {
        if (Environment.GetEnvironmentVariable("MESH_FUZZ_STRICT") == "1")
            return null;
        var kinds = new HashSet<Violation>(report.Violations.Select(v => v.Kind));
        if (kinds.Count > 0 && kinds.IsSubsetOf(new[] { Violation.AngleBelowBound }) && input.MinAngleDeg != null)
            return "AngleBelowBound";
        double far = Math.Max(input.X.Max(v => Math.Abs(v)), input.Y.Max(v => Math.Abs(v)));
        if (kinds.Count > 0 && kinds.IsSubsetOf(new[] { Violation.VertexUnused, Violation.SegmentMissing, Violation.ChainSkipsHotPixel }))
            return far > Pow2(16) ? "VertexUnused / SegmentMissing far from the origin" : "VertexUnused / SegmentMissing";
        return null;
    }

    static void RunQualityMesh2(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        // (the mesher's range, QA.md 2.5a: coordinates up to 1e70: the normalized copy is far inside it)
        // the mesher's contract: coordinates finite and at most 1e70 in absolute value, else ArgumentException (the normalized copy keeps the
        // case's offset, so a large offset reaches this)
        bool beyondRange = s.Norm.Vertices().Any(p => Math.Abs(p.x) > 1e70 || Math.Abs(p.y) > 1e70);
        var input = new Mesh2Input();
        var index = new Dictionary<int, int>();
        var x = new List<double>(); var y = new List<double>();
        foreach (int v in s.Norm.VertexIndices()) {
            index[v] = x.Count;
            Vector3d p = s.Norm.GetVertex(v);
            x.Add(p.x); y.Add(p.y);
        }
        input.X = x.ToArray(); input.Y = y.ToArray();
        var seen = new HashSet<(int, int)>();
        foreach (int e in s.Norm.EdgeIndices()) {
            Index2i ev = s.Norm.GetEdgeV(e);
            int a = index[ev.a], b = index[ev.b];
            if (x[a] == x[b] && y[a] == y[b])
                continue;       // (a segment of no extent in the projection)
            if (seen.Add(a < b ? (a, b) : (b, a)))
                input.Segments.Add(new SegmentInput(a, b));
        }
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1);
        double?[] angles = { null, 20, 25, 25 };
        input.MinAngleDeg = angles[p0 & 3];
        input.ConstrainedQuality = (p1 & 1) != 0;
        input.Conforming = (p1 & 2) != 0;
        input.SteinerLimit = 1500;
        if (beyondRange) {
            try { new QualityMesh2Candidate().Run(input); }
            catch (ArgumentException) { return; }
            throw new Finding($"{what}: coordinates beyond 1e70 were accepted (the documented contract is an ArgumentException)");
        }
        Mesh2Output o = new QualityMesh2Candidate().Run(input);
        if (o.Status == Mesh2Status.Failed) {
            CheckMesher2(what, "Failed", o.FailureKind?.ToString() ?? "", false, "");
            return;
        }
        if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1") Console.Error.WriteLine($"mesher2: limit reached {o.SteinerLimitReached}, {o.VX.Length} vertices for {input.X.Length} input points, angle {input.MinAngleDeg}, constrained {input.ConstrainedQuality}, conforming {input.Conforming}, x0 {input.X[0]}, {input.Segments.Count} segments, x range {input.X.Min()}..{input.X.Max()}, y range {input.Y.Min()}..{input.Y.Max()}, duplicates {input.X.Length - input.X.Zip(input.Y, (a, b) => (a, b)).Distinct().Count()}, snap {input.SnapTolerance}");
        OracleReport report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
        if (!report.Ok && KnownMesher2Class(input, report) != null)
            return;     // (an open class, pinned in FINDINGS.md; MESH_FUZZ_STRICT=1 judges it)
        CheckMesher2(what, o.Status.ToString(), "", report.Ok, string.Join("; ", report.Violations.Take(4)));
    }

    // ---- the Clipper2 adapter ----

    static int WindingOf(IReadOnlyList<Vector2d> ring, Vector2d p)
    {
        int w = 0, n = ring.Count;
        for (int i = 0; i < n; ++i) {
            Vector2d a = ring[i], b = ring[(i + 1) % n];
            double side = (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y);
            if (a.y <= p.y) { if (b.y > p.y && side > 0) ++w; }
            else if (b.y <= p.y && side < 0) --w;
        }
        return w;
    }

    static double RingDistance(IReadOnlyList<Vector2d> ring, Vector2d p)
    {
        double d = double.MaxValue;
        for (int i = 0; i < ring.Count; ++i) {
            Vector2d a = ring[i], b = ring[(i + 1) % ring.Count], e = b - a;
            double len2 = e.x * e.x + e.y * e.y;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.x - a.x) * e.x + (p.y - a.y) * e.y) / len2)) : 0;
            d = Math.Min(d, (p - (a + t * e)).Length);
        }
        return d;
    }

    static double SignedArea2D(IReadOnlyList<Vector2d> ring)
    {
        double a = 0;
        Vector2d c = ring[0];
        for (int i = 1; i + 1 < ring.Count; ++i)
            a += (ring[i] - c).x * (ring[i + 1] - c).y - (ring[i] - c).y * (ring[i + 1] - c).x;
        return 0.5 * a;
    }

    /// <summary>
    /// Clipper2Polygons.BooleanPaths of two raw rings (the projections of the corners of A and of B, in index order: simple,
    /// self-intersecting or degenerate; FillRule NonZero), and Offset and Simplify of the union: the status is Success; every
    /// vertex is finite, outer rings are counterclockwise and holes clockwise; at the sample points farther than a few grid
    /// resolutions from every input ring, the result contains the point exactly if the Boolean operation on the windings does
    /// </summary>
    public static void CheckClipper(string what, string status, string error, bool allFinite, bool orientationsOk, int membershipMismatches, string firstMismatch)
    {
        if (status != "Success")
            throw new Finding($"{what}: status {status} ({error})");
        if (!allFinite)
            throw new Finding($"{what}: a result vertex is not finite");
        if (!orientationsOk)
            throw new Finding($"{what}: an outer ring is not counterclockwise or a hole is not clockwise");
        if (membershipMismatches > 0)
            throw new Finding($"{what}: {membershipMismatches} sample points disagree with the winding numbers, first {firstMismatch}");
    }

    /// <summary> all vertices (outer rings and holes) finite; outer rings counterclockwise and holes clockwise </summary>
    static void Inspect(Clipper2Result r, out bool finite, out bool oriented)
    {
        finite = true; oriented = true;
        foreach (GeneralPolygon2d g in r.Polygons) {
            finite &= g.Outer.Vertices.All(v => v.IsFinite) && g.Holes.All(h => h.Vertices.All(v => v.IsFinite));
            if (g.Outer.VertexCount >= 3 && SignedArea2D(g.Outer.Vertices) <= 0) oriented = false;
            foreach (Polygon2d h in g.Holes)
                if (h.VertexCount >= 3 && SignedArea2D(h.Vertices) >= 0) oriented = false;
        }
    }

    static void RunClipper(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 nb = NormalizeWhole(c.B, out _);
        // B moved to A's centre plus a shift of up to a quarter of the diagonal, so the rings overlap
        Vector3d ca = s.Norm.GetBounds().Center, cb = nb.GetBounds().Center;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1), p2 = ParamByte(c, 2);
        Vector2d shift = 0.25 * new Vector2d((p1 - 127.5) / 127.5, (p2 - 127.5) / 127.5);
        var ringA = s.Norm.VertexIndices().Select(v => s.Norm.GetVertex(v).xy).ToList();
        var ringB = nb.VertexIndices().Select(v => nb.GetVertex(v).xy - cb.xy + ca.xy + shift).ToList();
        if (ringA.Count < 3 || ringB.Count < 3 || ringA.Any(p => !p.IsFinite) || ringB.Any(p => !p.IsFinite))
            return;
        // the grid of the adapter (2^-40 of the extent) must be representable at the coordinates' magnitude: far from the origin
        // compared with the extent (more than 2^16 extents), the results are rounded to doubles coarser than the grid and rings collapse
        // (a limit of the representation, not of the adapter), so there only no crash, a Success status and finite vertices are judged
        double extent = Math.Max(Diagonal(s.Norm.GetBounds()), 1.0), far = ringA.Concat(ringB).Max(p => Math.Max(Math.Abs(p.x), Math.Abs(p.y)));
        bool farInput = far > Pow2(16) * extent;
        var pa = new Polygon2d(ringA); var pb = new Polygon2d(ringB);
        ClipType[] ops = { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor };
        ClipType op = ops[p0 & 3];
        Clipper2Result r = Clipper2Polygons.BooleanPaths(op, new[] { pa }, new[] { pb });
        if (!r.Ok) {
            CheckClipper(what, r.Status.ToString(), r.Error ?? "", true, true, 0, "");
            return;
        }
        if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1") {
            Console.Error.WriteLine("ring A: " + string.Join(" ", ringA.Select(p => $"({p.x:R},{p.y:R})")));
            Console.Error.WriteLine("ring B: " + string.Join(" ", ringB.Select(p => $"({p.x:R},{p.y:R})")));
            Console.Error.WriteLine($"op {op}, frame {r.Frame}");
            foreach (GeneralPolygon2d g in r.Polygons) Console.Error.WriteLine("result outer: " + string.Join(" ", g.Outer.Vertices.Select(p => $"({p.x:R},{p.y:R})")) + $" holes {g.Holes.Count}");
        }
        bool finite = true, orientations = true;
        if (farInput) {
            Inspect(r, out finite, out _);
            CheckClipper(what + " (far)", r.Status.ToString(), "", finite, true, 0, "");
            return;
        }
        foreach (GeneralPolygon2d g in r.Polygons) {
            finite &= g.Outer.Vertices.All(v => v.IsFinite) && g.Holes.All(h => h.Vertices.All(v => v.IsFinite));
            if (g.Outer.VertexCount >= 3 && SignedArea2D(g.Outer.Vertices) <= 0) { orientations = false; if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1") Console.Error.WriteLine($"outer area {SignedArea2D(g.Outer.Vertices):R}, {g.Outer.VertexCount} vertices, resolution {r.Frame.Resolution:R}"); }
            foreach (Polygon2d h in g.Holes)
                if (h.VertexCount >= 3 && SignedArea2D(h.Vertices) >= 0) { orientations = false; if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1") Console.Error.WriteLine($"hole area {SignedArea2D(h.Vertices):R}, {h.VertexCount} vertices, resolution {r.Frame.Resolution:R}"); }
        }
        double margin = 4 * r.Frame.Resolution;
        var box = new AxisAlignedBox2d(ringA[0]);
        foreach (Vector2d v in ringA.Concat(ringB)) box.Contain(v);
        int mismatches = 0; string first = "";
        for (int i = 0; i < 12; ++i)
            for (int j = 0; j < 12; ++j) {
                Vector2d p = new Vector2d(box.Min.x + box.Width * (i + 0.37) / 12, box.Min.y + box.Height * (j + 0.61) / 12);
                if (RingDistance(ringA, p) <= margin || RingDistance(ringB, p) <= margin)
                    continue;
                bool ia = WindingOf(ringA, p) != 0, ib = WindingOf(ringB, p) != 0;
                bool expect = op == ClipType.Union ? ia || ib : op == ClipType.Intersection ? ia && ib : op == ClipType.Difference ? ia && !ib : ia != ib;
                int count = 0;
                foreach (GeneralPolygon2d g in r.Polygons)
                    if (WindingOf(g.Outer.Vertices, p) != 0 && !g.Holes.Any(h => WindingOf(h.Vertices, p) != 0))
                        ++count;
                if ((count > 0) != expect) {
                    if (mismatches++ == 0) first = $"{p} (in A {ia}, in B {ib}, result {count > 0}, {op})";
                }
            }
        CheckClipper(what, r.Status.ToString(), "", finite, orientations, mismatches, first);
        // Offset and Simplify of the union
        Clipper2Result u = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { pa }, new[] { pb });
        if (!u.Ok || u.Polygons.Count == 0)
            return;
        double diag = Math.Max(box.Width, box.Height);
        Clipper2Result off = Clipper2Polygons.Offset(u.Polygons, (p0 & 4) == 0 ? 0.05 * diag : -0.03 * diag, new Clipper2Options { JoinType = (p0 & 8) == 0 ? JoinType.Round : JoinType.Miter });
        Inspect(off, out bool offFinite, out bool offOriented);
        CheckClipper(what + " (offset)", off.Status.ToString(), off.Error ?? "", offFinite, offOriented, 0, "");
        Clipper2Result simp = Clipper2Polygons.Simplify(u.Polygons, 0.01 * diag);
        Inspect(simp, out bool simpFinite, out bool simpOriented);
        CheckClipper(what + " (simplify)", simp.Status.ToString(), simp.Error ?? "", simpFinite, simpOriented, 0, "");
    }
}
