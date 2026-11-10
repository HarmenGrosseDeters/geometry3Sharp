using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using g3;
using gs;
using SharpFuzz;

namespace MeshFuzzPilot;

/// <summary>
/// Coverage-guided fuzzing of the mesh operations (not of a reader): the input bytes describe one or two small closed
/// meshes, a base shape whose corners are perturbed, and the operation to run on them. Coordinates are small grid
/// values times a power of two plus an offset (coplanar, coincident and collinear cases, and every binary scale from
/// subnormal to near 1e308), or raw doubles. A finding is an exception, a timeout (libFuzzer's), or a result that
/// contradicts what the operation claims about it.
/// </summary>
public static partial class Program
{
    /// <summary> what an input decodes to </summary>
    public sealed class Case
    {
        public int Op;
        public DMesh3 A = null!, B = null!;
        /// <summary> the queries of the tree, batch and alpha-wrap targets: an origin and a unit direction each </summary>
        public List<(Vector3d origin, Vector3d dir)> Queries = new List<(Vector3d, Vector3d)>();
        public override string ToString() => $"op {Op} ({Ops[Op]}): A {A.VertexCount} v / {A.TriangleCount} t, B {B.VertexCount} v / {B.TriangleCount} t";
    }

    public static readonly string[] Ops = { "union", "intersection", "difference", "robust-repair", "robust-repair-soup", "rounding-repair",
        "tree-nearest", "tree-ray", "tree-inside", "batch-queries", "alpha-wrap", "tree-winding", "partitioned-reducer",
        "remesher", "reducer", "quality-mesh2", "clipper2" };

    /// <summary>
    /// the operations a campaign runs: MESH_FUZZ_OPS, a comma separated list of names or numbers (default: all). The
    /// first input byte selects among them, so a replay needs the same value
    /// </summary>
    static readonly int[] OpFilter = ParseOps(Environment.GetEnvironmentVariable("MESH_FUZZ_OPS"));

    static int[] ParseOps(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
            return Enumerable.Range(0, Ops.Length).ToArray();
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out int i) && i >= 0 && i < Ops.Length ? i : Array.IndexOf(Ops, x))
            .Select(i => i >= 0 ? i : throw new ArgumentException("unknown operation in MESH_FUZZ_OPS")).ToArray();
    }

    // base shapes: tetrahedron, octahedron, cube (closed and consistently oriented); two disjoint tetrahedra (two components);
    // a cube without its bottom face (open); two cubes, the second shifted by half an edge (closed, self-intersecting)
    static readonly double[][] ShapeVertices = {
        new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 },
        new double[] { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 },
        new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1 },
        new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 3, 0, 0, 4, 0, 0, 3, 1, 0, 3, 0, 1 },
        new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1 },
        new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1,
                       0.5, 0.5, 0.5, 1.5, 0.5, 0.5, 1.5, 1.5, 0.5, 0.5, 1.5, 0.5, 0.5, 0.5, 1.5, 1.5, 0.5, 1.5, 1.5, 1.5, 1.5, 0.5, 1.5, 1.5 },
    };
    static readonly int[][] ShapeTriangles = {
        new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 },
        new[] { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 },
        new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 },
        new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3, 4, 6, 5, 4, 5, 7, 4, 7, 6, 5, 6, 7 },
        new[] { 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 },
        new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
                8, 10, 9, 8, 11, 10, 12, 13, 14, 12, 14, 15, 8, 9, 13, 8, 13, 12, 9, 10, 14, 9, 14, 13, 10, 11, 15, 10, 15, 14, 11, 8, 12, 11, 12, 15 },
    };

    sealed class Reader
    {
        readonly byte[] data;
        int pos;
        public Reader(byte[] data) { this.data = data; }
        public byte Byte() => pos < data.Length ? data[pos++] : (byte)0;
        public double Raw()
        {
            long bits = 0;
            for (int i = 0; i < 8; ++i)
                bits |= (long)Byte() << (8 * i);
            return BitConverter.Int64BitsToDouble(bits);
        }
    }

    // a power of two 2^e (e clamped to the normal range)
    static double Pow2(int e)
    {
        e = Math.Max(-1022, Math.Min(1023, e));
        return BitConverter.Int64BitsToDouble((long)(e + 1023) << 52);
    }

    // two bytes to an exponent (see DecodeMesh)
    static int Exponent16(byte hi, byte lo)
    {
        int x = (hi << 8) | lo;
        if ((x & 3) == 0)
            return (x >> 2) % 2046 - 1022;
        x >>= 2;
        if (x % 8 == 0) {
            int sign = ((x >> 3) & 1) == 0 ? -1 : 1;
            return sign * (100 + (x >> 4) % 51);
        }
        return (x >> 3) % 301 - 150;
    }

    static DMesh3 DecodeMesh(Reader r)
    {
        int shape = r.Byte() % ShapeVertices.Length;
        // scale 2^e and an offset of k * 2^f: three cases in four inside the supported range (QA.md 2.5a: exponents
        // within +-150, one in eight of them at its edges, +-100 .. +-150), one in four anywhere in [-1022, 1023] (the small
        // out-of-range sample, judged only for no crash, no hang, no false success)
        int e = Exponent16(r.Byte(), r.Byte());
        double scale = Pow2(e);
        int f = Exponent16(r.Byte(), r.Byte());
        var offset = new Vector3d((sbyte)r.Byte(), (sbyte)r.Byte(), (sbyte)r.Byte()) * Pow2(f);
        double[] v = ShapeVertices[shape];
        var m = new DMesh3();
        for (int i = 0; i < v.Length / 3; ++i) {
            Vector3d p;
            byte mode = r.Byte();
            if ((mode & 0xE0) == 0xE0) {
                // raw doubles (a non-finite one becomes 0: the operations reject non-finite input by contract)
                p = new Vector3d(r.Raw(), r.Raw(), r.Raw());
                for (int k = 0; k < 3; ++k)
                    if (!(Math.Abs(p[k]) <= double.MaxValue)) p[k] = 0;
            } else {
                // the corner times 16 plus a small grid perturbation, times the scale, plus the offset
                var d = new Vector3d((sbyte)r.Byte(), (sbyte)r.Byte(), (sbyte)r.Byte());
                if ((mode & 1) == 0) d = Vector3d.Zero;
                p = (16 * new Vector3d(v[3 * i], v[3 * i + 1], v[3 * i + 2]) + d) * scale + offset;
                for (int k = 0; k < 3; ++k)
                    if (!(Math.Abs(p[k]) <= double.MaxValue)) p[k] = 0;
            }
            m.AppendVertex(p);
        }
        int[] t = ShapeTriangles[shape];
        for (int i = 0; i < t.Length; i += 3)
            m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
        return m;
    }

    public static Case Decode(byte[] data)
    {
        var r = new Reader(data);
        var c = new Case { Op = OpFilter[r.Byte() % OpFilter.Length] };
        c.A = DecodeMesh(r);
        c.B = DecodeMesh(r);
        DecodeQueries(r, c);
        return c;
    }

    /// <summary> a contradiction found in a result (thrown, so libFuzzer records the input as a crash) </summary>
    public sealed class Finding : Exception
    {
        public Finding(string message) : base(message) { }
    }

    static bool AllFinite(DMesh3 m)
    {
        foreach (Vector3d p in m.Vertices())
            if (!(Math.Abs(p.x) <= double.MaxValue && Math.Abs(p.y) <= double.MaxValue && Math.Abs(p.z) <= double.MaxValue))
                return false;
        return true;
    }

    // exponent of x > 0, finite: x in [2^e, 2^(e+1))
    static int Exponent(double x)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        int e = (int)((bits >> 52) & 0x7FF);
        if (e != 0)
            return e - 1023;
        bits = BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0);    // 2^64, exact for a subnormal
        return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
    }

    /// <summary> the diagonal of a box without overflow in the extents or the squares (infinite only if it exceeds the doubles) </summary>
    public static double Diagonal(AxisAlignedBox3d box)
    {
        // (halves: max - min of two values near 1.7e308 overflows)
        double dx = 0.5 * box.Max.x - 0.5 * box.Min.x, dy = 0.5 * box.Max.y - 0.5 * box.Min.y, dz = 0.5 * box.Max.z - 0.5 * box.Min.z;
        double m = Math.Max(dx, Math.Max(dy, dz));
        if (!(m > 0))
            return 0;
        double ex = dx / m, ey = dy / m, ez = dz / m;
        return 2 * m * Math.Sqrt(ex * ex + ey * ey + ez * ez);
    }

    // the diagonals of the connected components' boxes, largest first
    static double LargestComponentDiagonal(DMesh3 m)
    {
        var comps = new MeshConnectedComponents(m);
        comps.FindConnectedT();
        double largest = 0;
        foreach (var c in comps.Components) {
            var box = AxisAlignedBox3d.Empty;
            foreach (int t in c.Indices)
                box.Contain(m.GetTriBounds(t));
            largest = Math.Max(largest, Diagonal(box));
        }
        return largest;
    }

    /// <summary>
    /// a copy scaled by a power of two so that the largest component's diagonal is in [1, 2), where the checks'
    /// thresholds (1e-12 times a squared diagonal) neither underflow nor overflow; exact unless coordinates become
    /// subnormal; the mesh itself if the scale is not finite or the scaled coordinates would overflow
    /// </summary>
    public static DMesh3 Normalized(DMesh3 m) => Normalize(m, out _);

    /// <summary> Normalized, and the scale (a power of two, 1 if the mesh itself is returned) </summary>
    public static DMesh3 Normalize(DMesh3 m, out double scale)
    {
        scale = 1;
        double d = LargestComponentDiagonal(m);
        if (!(d > 0 && d <= double.MaxValue))
            return m;
        int e = -Exponent(d);
        if (e == 0)
            return m;
        double s = Pow2(e);
        if (Exponent(s) != e)
            return m;
        double largest = 0;
        foreach (Vector3d p in m.Vertices())
            largest = Math.Max(largest, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
        if (!(largest * s <= double.MaxValue / 16))
            return m;
        var n = new DMesh3(m);
        foreach (int v in n.VertexIndices())
            n.SetVertex(v, s * n.GetVertex(v));
        scale = s;
        return n;
    }

    /// <summary>
    /// the triangles that are degenerate as the rounding judges them (MeshRoundingRepair: area below 1e-12 times the
    /// squared diagonal of the largest component, or exactly collinear corners), on the normalized mesh
    /// </summary>
    public static int RoundingDegenerate(DMesh3 m)
    {
        DMesh3 n = Normalized(m);
        double d = LargestComponentDiagonal(n), eps = 1e-12 * d * d;
        int count = 0;
        foreach (int t in n.TriangleIndices()) {
            Index3i f = n.GetTriangle(t);
            var tri = new Triangle3d(n.GetVertex(f.a), n.GetVertex(f.b), n.GetVertex(f.c));
            if (n.GetTriArea(t) < eps || ExactTriangleIntersection.IsDegenerate(ref tri))
                ++count;
        }
        return count;
    }

    // ---- the claims, each a function of the results, so that the self-test can hand them a contradiction ----

    /// <summary>
    /// ExactMeshBoolean on closed inputs: Compute() is Status == Success; Success has a Result (closed and manifold,
    /// documented for closed inputs, also when the rounding is not clean; empty is allowed) with finite vertices,
    /// NonManifoldTriangles and ConflictEdges 0 (documented for closed inputs); RoundingClean: the result is clean in
    /// memory (orientation, inside-out components, self-intersections unless it has contacts, which the check would
    /// count; degenerate triangles as the rounding judges them)
    /// </summary>
    public static void CheckBoolean(string what, bool ok, ExactMeshBoolean.BooleanStatus status, DMesh3? result, bool roundingClean,
        int contacts, int nonManifoldTriangles, int conflictEdges, bool inputsClosed = true)
    {
        if (ok != (status == ExactMeshBoolean.BooleanStatus.Success))
            throw new Finding($"{what}: Compute() {ok} but Status {status}");
        if (!ok)
            return;
        if (result == null)
            throw new Finding($"{what}: Success without a Result");
        if (!AllFinite(result))
            throw new Finding($"{what}: a result vertex is not finite");
        if (!inputsClosed)
            return;     // (open inputs are allowed by default; nothing is promised about the result's shape)
        if (nonManifoldTriangles != 0 || conflictEdges != 0)
            throw new Finding($"{what}: closed inputs, but NonManifoldTriangles {nonManifoldTriangles}, ConflictEdges {conflictEdges}");
        if (result.TriangleCount == 0)
            return;
        DMesh3 n = Normalized(result);
        var shape = MeshRobustRepair.FindDefects(n, false) & (MeshRobustRepair.Defects.NotClosed | MeshRobustRepair.Defects.Bowties);
        if (shape != MeshRobustRepair.Defects.None)
            throw new Finding($"{what}: closed inputs, but the result has {MeshRobustRepair.DescribeDefects(shape)}");
        if (roundingClean) {
            var d = MeshRobustRepair.FindDefects(n, checkSelfIntersections: contacts == 0) & ~MeshRobustRepair.Defects.DegenerateTriangles;
            if (d != MeshRobustRepair.Defects.None)
                throw new Finding($"{what}: RoundingClean but {MeshRobustRepair.DescribeDefects(d)}");
            int deg = RoundingDegenerate(result);
            if (deg > 0)
                throw new Finding($"{what}: RoundingClean but {deg} degenerate triangles (at the rounding's scale)");
        }
    }

    // every directed edge, by the positions of its corners (coincident vertices are one), is matched by its reverse equally often
    static bool WindingClosed(DMesh3 m)
    {
        var count = new Dictionary<(Vector3d, Vector3d), int>();
        void Add(Vector3d a, Vector3d b, int d)
        {
            if (a == b) return;
            count[(a, b)] = count.GetValueOrDefault((a, b)) + d;
        }
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            Vector3d a = m.GetVertex(f.a), b = m.GetVertex(f.b), c = m.GetVertex(f.c);
            Add(a, b, 1); Add(b, c, 1); Add(c, a, 1);
        }
        foreach (var kv in count)
            if (kv.Value != count.GetValueOrDefault((kv.Key.Item2, kv.Key.Item1)))
                return false;
        return true;
    }

    /// <summary>
    /// AllowOpenMeshes = false: an input with every edge matched is not OpenInput (a mesh with no boundary edge at all), and an
    /// input with an unmatched edge, by the positions of its corners, is OpenInput
    /// </summary>
    public static void CheckStrictOpen(string what, bool noBoundaryEdge, bool unmatchedEdge, ExactMeshBoolean.BooleanStatus status)
    {
        if (noBoundaryEdge && status == ExactMeshBoolean.BooleanStatus.OpenInput)
            throw new Finding($"{what}: closed inputs, but OpenInput with AllowOpenMeshes = false");
        if (unmatchedEdge && status != ExactMeshBoolean.BooleanStatus.OpenInput)
            throw new Finding($"{what}: an input with an unmatched edge, but {status} with AllowOpenMeshes = false (documented: OpenInput)");
    }

    /// <summary>
    /// MeshRobustRepair: Apply() true with ResultReason "" is a clean result (MeshRobustRepair.NotCleanReason, judged
    /// on the normalized mesh, so that a tiny result's degenerate area does not underflow) with finite vertices
    /// </summary>
    public static void CheckRepair(string what, bool ok, string resultReason, DMesh3 mesh)
    {
        if (!(ok && resultReason == ""))
            return;
        if (!AllFinite(mesh))
            throw new Finding($"{what}: repaired mesh has a vertex that is not finite");
        string reason = MeshRobustRepair.NotCleanReason(Normalized(mesh));
        if (reason != "")
            throw new Finding($"{what}: repaired (ResultReason \"\") but NotCleanReason \"{reason}\"");
    }

    /// <summary>
    /// MeshRoundingRepair on a closed mesh: Apply() is IsClean; the vertices stay finite and the mesh closed and valid
    /// (edits keep it manifold, removals take whole components); IsClean: no exact self-intersection, no degenerate
    /// triangle as the rounding judges it, and the drift within the bound
    /// </summary>
    public static void CheckRounding(string what, bool clean, bool isClean, DMesh3 mesh, double maxDrift, double maxLength, bool inputClosed = true)
    {
        if (clean != isClean)
            throw new Finding($"{what}: Apply() {clean} but IsClean {isClean}");
        if (!AllFinite(mesh))
            throw new Finding($"{what}: a vertex is not finite after the rounding repair");
        if (!mesh.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the mesh is not valid after the rounding repair");
        if (inputClosed && mesh.TriangleCount > 0 && !mesh.IsClosed())
            throw new Finding($"{what}: a closed mesh is not closed after the rounding repair");
        if (!clean)
            return;
        int si = MeshRepairMetrics.SelfIntersections(mesh);
        if (si != 0)
            throw new Finding($"{what}: IsClean but {si} self-intersections");
        int deg = RoundingDegenerate(mesh);
        if (deg > 0)
            throw new Finding($"{what}: IsClean but {deg} degenerate triangles (at the rounding's scale)");
        if (maxLength <= double.MaxValue && !(maxDrift <= maxLength))
            throw new Finding($"{what}: IsClean but MaxDrift {maxDrift} above the bound {maxLength}");
    }

    /// <summary> every nonzero coordinate of the used vertices has a magnitude in [min, max]; with extentRule also the extent is zero or at least 2^-40 of the largest magnitude </summary>
    static bool CoordinatesInRange(DMesh3 m, double min, double max, bool extentRule = false)
    {
        double biggest = 0;
        Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            for (int k = 0; k < 3; ++k) {
                Vector3d p = m.GetVertex(t[k]);
                for (int c = 0; c < 3; ++c) {
                    double a = Math.Abs(p[c]);
                    if (!(a == 0 || (a >= min && a <= max))) return false;
                    biggest = Math.Max(biggest, a);
                    lo[c] = Math.Min(lo[c], p[c]); hi[c] = Math.Max(hi[c], p[c]);
                }
            }
        }
        if (!extentRule) return true;
        double extent = Math.Max(hi.x - lo.x, Math.Max(hi.y - lo.y, hi.z - lo.z));
        return extent == 0 || extent >= biggest * Pow2(-40);
    }

    static bool CombinesMeshes(int op) => op <= 2 || op == 4 || op == 5 || op == 10;

    /// <summary> runs the case and checks the result's claims; throws on a finding (or lets an exception through) </summary>
    public static void Run(Case c)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string what = c.ToString();
        // two meshes of sizes more than 2^60 apart are a separate class (the tolerances of the rounding are relative to the
        // largest component: the smaller one is degenerate by definition); reported, not a domain of these claims
        if (CombinesMeshes(c.Op)) {
            double da = Diagonal(c.A.GetBounds()), db = Diagonal(c.B.GetBounds());
            if (da > 0 && db > 0 && !(da / db <= Pow2(60) && db / da <= Pow2(60)))
                return;
        }
        // outside the supported ranges (QA.md 2.5a) only the second property is judged: no crash, no hang, no false success
        // (the Boolean operations are judged as always: the exact arrangement has no range, and the rounding says RoundingClean false where its range ends)
        if (c.Op == 3 || c.Op == 4) {
            DMesh3 all = new DMesh3(c.A);
            if (c.Op == 4)
                new MeshEditor(all).AppendMesh(c.B);
            if (!CoordinatesInRange(all, Pow2(-200), Pow2(400), extentRule: true)) {
                var outside = new MeshRobustRepair(all);
                if (outside.Apply())
                    throw new Finding($"{what}: outside the supported range, Apply() is true");
                return;
            }
        }
        if (c.Op <= 2) {
            var op = c.Op == 0 ? ExactMeshBoolean.Operations.Union : c.Op == 1 ? ExactMeshBoolean.Operations.Intersection : ExactMeshBoolean.Operations.Difference;
            var b = new ExactMeshBoolean(c.A, c.B, op);
            bool ok = b.Compute();
            bool closed = c.A.IsClosed() && c.B.IsClosed();
            CheckBoolean(what, ok, b.Status, b.Result, b.RoundingClean, b.Contacts, b.NonManifoldTriangles, b.ConflictEdges, inputsClosed: closed);
            // AllowOpenMeshes = false: an input with a boundary edge is OpenInput, a closed one is not
            var strict = new ExactMeshBoolean(c.A, c.B, op) { AllowOpenMeshes = false };
            strict.Compute();
            CheckStrictOpen(what, closed, !(WindingClosed(c.A) && WindingClosed(c.B)), strict.Status);
        } else if (c.Op <= 4) {
            DMesh3 input = new DMesh3(c.A);
            if (c.Op == 4)
                new MeshEditor(input).AppendMesh(c.B);
            var repair = new MeshRobustRepair(input);
            bool ok = repair.Apply();
            CheckRepair(what, ok, repair.ResultReason, repair.Mesh);
        } else if (c.Op == 5) {
            DMesh3 m = new DMesh3(c.A);
            new MeshEditor(m).AppendMesh(c.B);
            bool inputClosed = m.IsClosed();
            var repair = new MeshRoundingRepair(m);
            bool clean = repair.Apply();
            CheckRounding(what, clean, repair.IsClean, m, repair.MaxDrift, repair.MaxLength, inputClosed);
        } else if (c.Op == 6) {
            RunTreeNearest(c, what);
        } else if (c.Op == 7) {
            RunTreeRay(c, what);
        } else if (c.Op == 8) {
            RunTreeInside(c, what);
        } else if (c.Op == 9) {
            RunBatch(c, what);
        } else if (c.Op == 10) {
            RunAlphaWrap(c, what);
        } else if (c.Op == 11) {
            RunTreeWinding(c, what);
        } else if (c.Op == 12) {
            RunPartitionedReducer(c, what);
        } else if (c.Op == 13) {
            RunRemesher(c, what);
        } else if (c.Op == 14) {
            RunReducer(c, what);
        } else if (c.Op == 15) {
            RunQualityMesh2(c, what);
        } else {
            RunClipper(c, what);
        }
    }

    // seeds: each operation on the plain shapes, at unit size and with an offset (no perturbation)
    public static IEnumerable<(string name, byte[] data)> Seeds()
    {
        for (int op = 0; op < Ops.Length; ++op) {
            int selector = Array.IndexOf(OpFilter, op);
            if (selector < 0)
                continue;
            for (int shape = 0; shape < ShapeVertices.Length; ++shape) {
                var bytes = new List<byte> { (byte)selector };
                foreach (int s in new[] { shape, (shape + 1) % ShapeVertices.Length }) {
                    bytes.Add((byte)s);
                    // e = 0 and f = 0: (1022 + e) as two bytes
                    bytes.AddRange(new byte[] { 0x03, 0xFE, 0x03, 0xFE });
                    bytes.AddRange(s == shape ? new byte[] { 0, 0, 0 } : new byte[] { 4, 3, 2 });
                    for (int i = 0; i < ShapeVertices[s].Length / 3; ++i)
                        bytes.AddRange(new byte[] { 0, 0, 0, 0 });
                }
                for (int q = 0; q < 8; ++q)
                    bytes.AddRange(new byte[] { (byte)q, (byte)(3 * q + 1), (byte)(q + 2), (byte)(5 * q), (byte)q, (byte)(7 * q) });
                yield return ($"{Ops[op]}-{shape}.bin", bytes.ToArray());
            }
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "write-seeds") {
            if (Directory.Exists(args[1]) && Directory.EnumerateFileSystemEntries(args[1]).Any()) {
                Console.Error.WriteLine("seed directory is not empty: " + args[1]);
                return 2;
            }
            Directory.CreateDirectory(args[1]);
            foreach (var (name, data) in Seeds())
                File.WriteAllBytes(Path.Combine(args[1], name), data);
            return 0;
        }
        if (args.Length == 1 && args[0] == "self-test")
            return SelfTest();
        if (args.Length == 2 && args[0] == "dump") {
            // the decoded case with exact coordinates (round-trip format), for a procedural regression test
            Case d = Decode(File.ReadAllBytes(args[1]));
            Console.WriteLine(d);
            foreach (var (name, m) in new[] { ("A", d.A), ("B", d.B) }) {
                Console.WriteLine(name + " vertices:");
                foreach (int v in m.VertexIndices())
                    Console.WriteLine($"  {v}: {m.GetVertex(v).x:R} {m.GetVertex(v).y:R} {m.GetVertex(v).z:R}");
                Console.WriteLine(name + " triangles: " + string.Join(" ", m.TriangleIndices().Select(t => $"({m.GetTriangle(t).a},{m.GetTriangle(t).b},{m.GetTriangle(t).c})")));
            }
            for (int i = 0; i < d.Queries.Count; ++i)
                Console.WriteLine($"query {i}: origin {d.Queries[i].origin.x:R} {d.Queries[i].origin.y:R} {d.Queries[i].origin.z:R} direction {d.Queries[i].dir.x:R} {d.Queries[i].dir.y:R} {d.Queries[i].dir.z:R}");
            return 0;
        }
        if (args.Length == 3 && args[0] == "random") {
            // a smoke test without coverage guidance: n pseudo-random inputs (seed s, 40 to 300 bytes) of the selected operations, each run
            // with a 60 s watchdog; prints the hex of every input that gives a finding or an exception (no libFuzzer needed)
            int n = int.Parse(args[1], CultureInfo.InvariantCulture);
            var rnd = new Random(int.Parse(args[2], CultureInfo.InvariantCulture));
            int bad = 0;
            for (int i = 0; i < n; ++i) {
                var data = new byte[40 + rnd.Next(260)];
                rnd.NextBytes(data);
                string text = Convert.ToHexString(data);
                Exception? error = null;
                var thread = new Thread(() => { try { Run(Decode(data)); } catch (Exception e) { error = e; } }, 64 * 1024 * 1024) { IsBackground = true };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                thread.Start();
                if (!thread.Join(60000)) { Console.WriteLine($"HANG {text}"); ++bad; continue; }
                if (error != null) { Console.WriteLine($"{(error is Finding ? "FINDING" : "EXCEPTION " + error.GetType().Name)} {error.Message.Split('\n')[0]} {text}"); ++bad; }
                else if (watch.ElapsedMilliseconds > 5000) Console.WriteLine($"SLOW {watch.ElapsedMilliseconds} ms {text}");
            }
            Console.WriteLine($"{n} inputs, {bad} findings");
            return bad == 0 ? 0 : 1;
        }
        if (args.Length == 2 && args[0] == "replay") {
            Case c = Decode(File.ReadAllBytes(args[1]));
            Console.WriteLine(c);
            Run(c);
            return 0;
        }
        if (args.Length != 0) {
            Console.Error.WriteLine("usage: MeshFuzzPilot [write-seeds <empty dir> | replay <input> | dump <input> | random <n> <seed> | self-test]");
            return 2;
        }
        Fuzzer.LibFuzzer.Run(data => Run(Decode(data.ToArray())));
        return 0;
    }

    // the decoder is deterministic and gives finite closed shapes, also for extreme bytes; the seeds cover every
    // operation and run without a finding; each check throws a Finding when handed a contradiction
    static int SelfTest()
    {
        foreach (var (name, data) in Seeds()) {
            Case a = Decode(data), b = Decode(data);
            if (a.ToString() != b.ToString() || !a.A.Vertices().SequenceEqual(b.A.Vertices()) || !a.B.Vertices().SequenceEqual(b.B.Vertices()))
                throw new InvalidOperationException("decoder not deterministic: " + name);
            Run(a);
        }
        if (Seeds().Select(s => Decode(s.data).Op).Distinct().Count() != OpFilter.Length)
            throw new InvalidOperationException("the seeds do not cover every operation");
        // extreme bytes: all 0xFF (raw corners of NaN), the largest exponents, raw infinity / NaN / subnormal
        var extremes = new List<byte[]> { Enumerable.Repeat((byte)0xFF, 512).ToArray(), new byte[] { 3, 2, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3 },
            new byte[] { 4, 1, 0, 0, 0, 0 } };
        var raw = new List<byte> { 5, 0, 0x03, 0xFE, 0x03, 0xFE, 0, 0, 0, 0xE0 };
        raw.AddRange(BitConverter.GetBytes(double.PositiveInfinity));
        raw.AddRange(BitConverter.GetBytes(double.NaN));
        raw.AddRange(BitConverter.GetBytes(double.Epsilon));
        extremes.Add(raw.ToArray());
        foreach (byte[] data in extremes) {
            Case c = Decode(data);
            if (!AllFinite(c.A) || !AllFinite(c.B))
                throw new InvalidOperationException("decoded a corner that is not finite: " + c);
            if (!c.A.IsClosed() || !c.B.IsClosed() || !c.A.CheckValidity(true, FailMode.ReturnOnly) || !c.B.CheckValidity(true, FailMode.ReturnOnly))
                throw new InvalidOperationException("decoded shape not closed or not valid: " + c);
        }
        // the checks fire on contradictions
        var open = new DMesh3();
        open.AppendTriangle(open.AppendVertex(Vector3d.Zero), open.AppendVertex(Vector3d.AxisX), open.AppendVertex(Vector3d.AxisY));
        DMesh3 tinyFlat = FlatTetrahedron(Math.Pow(2, -600));
        DMesh3 cube = new TrivialBox3Generator() { NoSharedVertices = false }.Generate().MakeDMesh();
        var S = ExactMeshBoolean.BooleanStatus.Success;
        ExpectFinding("Compute() true with another status", () => CheckBoolean("t", true, ExactMeshBoolean.BooleanStatus.OpenInput, cube, false, 0, 0, 0));
        ExpectFinding("OpenInput for closed inputs", () => CheckStrictOpen("t", true, false, ExactMeshBoolean.BooleanStatus.OpenInput));
        ExpectFinding("an open input accepted with AllowOpenMeshes = false", () => CheckStrictOpen("t", false, true, S));
        ExpectFinding("Success without a Result", () => CheckBoolean("t", true, S, null, false, 0, 0, 0));
        ExpectFinding("an open Boolean result", () => CheckBoolean("t", true, S, open, false, 0, 0, 0));
        ExpectFinding("NonManifoldTriangles", () => CheckBoolean("t", true, S, cube, false, 0, 1, 0));
        ExpectFinding("RoundingClean with a degenerate triangle at a tiny scale", () => CheckBoolean("t", true, S, tinyFlat, true, 0, 0, 0));
        ExpectFinding("a repaired mesh that is open", () => CheckRepair("t", true, "", open));
        ExpectFinding("a repaired tiny mesh with a degenerate triangle", () => CheckRepair("t", true, "", tinyFlat));
        ExpectFinding("Apply() and IsClean disagree", () => CheckRounding("t", true, false, cube, 0, 1));
        ExpectFinding("IsClean with a degenerate triangle at a tiny scale", () => CheckRounding("t", true, true, tinyFlat, 0, 1));
        ExpectFinding("IsClean with the drift over the bound", () => CheckRounding("t", true, true, cube, 2, 1));
        ExpectFinding("an open mesh after the rounding repair", () => CheckRounding("t", false, false, open, 0, 1));
        // the tree, batch and alpha wrap claims
        ExpectFinding("no nearest triangle for a mesh with triangles", () => CheckNearestTriangle("t", DMesh3.InvalidID, double.NaN, double.NaN, 1, true));
        ExpectFinding("a nearest triangle of an empty mesh", () => CheckNearestTriangle("t", 0, 1, 1, 1, false));
        ExpectFinding("a nearest triangle that is not the nearest", () => CheckNearestTriangle("t", 0, 2, 2, 1, true));
        ExpectFinding("a reported distance that is not the minimum", () => CheckNearestTriangle("t", 0, 2, 1, 1, true));
        ExpectFinding("no nearest vertex", () => CheckNearestVertex("t", DMesh3.InvalidID, double.NaN, 1));
        ExpectFinding("a nearest vertex that is not the nearest", () => CheckNearestVertex("t", 0, 2, 1));
        ExpectFinding("a nearest hit that is not the brute force one", () => CheckRay("t", 3, 4, new[] { 3, 4 }, new[] { 3, 4 }));
        ExpectFinding("a missing hit", () => CheckRay("t", 3, 3, new[] { 3, 4 }, new[] { 3 }));
        ExpectFinding("an extra hit", () => CheckRay("t", DMesh3.InvalidID, DMesh3.InvalidID, new int[0], new[] { 1 }));
        ExpectFinding("a crossing count that is not the brute force one", () => CheckInside("t", 1, 2, false, null));
        ExpectFinding("IsInside that is not the parity", () => CheckInside("t", 2, 2, true, null));
        ExpectFinding("IsInside against the winding number", () => CheckInside("t", 1, 1, true, false));
        ExpectFinding("a winding number that is NaN", () => CheckWinding("t", 1, 1, double.NaN));
        ExpectFinding("a winding number that is not the brute force one", () => CheckWinding("t", 1, 0, 0));
        ExpectFinding("a winding number that depends on the scale", () => CheckWinding("t", 1, 1, 0));
        ExpectFinding("a batch result that differs", () => CheckBatchEqual("t", "x", 1, 2));
        var okWrap = MeshAlphaWrap.WrapStatus.Success;
        ExpectFinding("an alpha wrap whose Compute() and Status disagree", () => CheckAlphaWrap("t", true, MeshAlphaWrap.WrapStatus.Failed, "", cube, true, true));
        ExpectFinding("EmptyInput for a mesh", () => CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.EmptyInput, "", null, true, true));
        ExpectFinding("InvalidInput for finite input", () => CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.InvalidInput, "", null, true, true));
        ExpectFinding("InvalidParameters for an input of positive size", () => CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.InvalidParameters, "", null, true, true));
        ExpectFinding("Failed", () => CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.Failed, "", cube, true, true));
        ExpectFinding("Success without a result", () => CheckAlphaWrap("t", true, okWrap, "", null, true, true));
        ExpectFinding("an open alpha wrap", () => CheckAlphaWrap("t", true, okWrap, "", open, true, true));
        ExpectFinding("an alpha wrap that is not finite", () => CheckAlphaWrap("t", true, okWrap, "", NotFinite(cube), true, true));
        ExpectFinding("a nearest hit that is not a hit (near-parallel query)", () => CheckRay("t", 3, 5, new[] { 3 }, new[] { 3 }, false));
        ExpectFinding("a missing nearest hit (near-parallel query)", () => CheckRay("t", 3, DMesh3.InvalidID, new[] { 3 }, new[] { 3 }, false));
        ExpectFinding("an alpha wrap that was cancelled", () => CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.Cancelled, "", null, true, true));
        ExpectFinding("an empty alpha wrap", () => CheckAlphaWrap("t", true, okWrap, "", new DMesh3(), true, true));
        ExpectFinding("a Boolean on an open input claiming nothing is checked", () => CheckBoolean("t", true, S, NotFinite(cube), false, 0, 0, 0, inputsClosed: false));
        ExpectFinding("a reduced mesh with more triangles than the input", () => CheckPartitionedReduction("t", true, open, cube, cube, cube, null, 1));
        ExpectFinding("a thread count that changes the result", () => CheckPartitionedReduction("t", true, cube, cube, ShiftedBy(cube, 1), cube, null, 1));
        ExpectFinding("a repeated run that differs", () => CheckPartitionedReduction("t", true, cube, cube, cube, ShiftedBy(cube, 1), null, 1));
        ExpectFinding("a scaled result that is not the scaled result", () => CheckPartitionedReduction("t", true, cube, cube, cube, cube, ShiftedBy(cube, 1), 1));
        ExpectFinding("a result that is open for a closed input", () => CheckPartitionedReduction("t", true, cube, open, open, open, null, 1));
        ExpectFinding("a result with a vertex that is not finite", () => CheckPartitionedReduction("t", false, cube, NotFinite(cube), NotFinite(cube), NotFinite(cube), null, 1));
        // the second wave: remesher, reducer, 2D mesher, Clipper2
        ExpectFinding("a remeshed mesh that is open", () => CheckRemesh("t", true, cube, open, open, open));
        ExpectFinding("a remeshed mesh that is not finite", () => CheckRemesh("t", true, cube, NotFinite(cube), NotFinite(cube), NotFinite(cube)));
        ExpectFinding("a remeshed mesh with another Euler characteristic", () => CheckRemesh("t", true, open, cube, cube, cube));
        ExpectFinding("parallel smoothing that differs", () => CheckRemesh("t", true, cube, cube, ShiftedBy(cube, 1), cube));
        ExpectFinding("a second remesh that differs", () => CheckRemesh("t", true, cube, cube, cube, ShiftedBy(cube, 1)));
        ExpectFinding("a reduced mesh that is open", () => CheckReduce("t", true, cube, open, open, null, 1, 0));
        ExpectFinding("a reduced mesh with more triangles", () => CheckReduce("t", true, open, cube, cube, null, 1, 0));
        ExpectFinding("a reduced mesh farther than MaxDeviation", () => CheckReduce("t", true, cube, ScaledBy(cube, 3), ScaledBy(cube, 3), null, 1, 0.1));
        ExpectFinding("a reduced mesh that is not the scaled one", () => CheckReduce("t", true, cube, cube, cube, ShiftedBy(cube, 1), 1, 0));
        ExpectFinding("a 2D mesher that Failed", () => CheckMesher2("t", "Failed", "x", false, ""));
        ExpectFinding("a 2D mesher violation", () => CheckMesher2("t", "Success", "", false, "AngleBelowBound"));
        ExpectFinding("a Clipper2 status that is not Success", () => CheckClipper("t", "Failed", "x", true, true, 0, ""));
        ExpectFinding("a Clipper2 vertex that is not finite", () => CheckClipper("t", "Success", "", false, true, 0, ""));
        ExpectFinding("a Clipper2 ring with the wrong orientation", () => CheckClipper("t", "Success", "", true, false, 0, ""));
        ExpectFinding("a Clipper2 membership that disagrees", () => CheckClipper("t", "Success", "", true, true, 1, "p"));
        CheckPartitionedReduction("t", true, cube, cube, cube, cube, ShiftedBy(cube, 0), 1);
        // and pass on a clean result
        CheckNearestTriangle("t", 0, 1, 1, 1, true);
        CheckNearestVertex("t", 0, 1, 1);
        CheckRay("t", 3, 3, new[] { 3, 4 }, new[] { 4, 3 });
        CheckInside("t", 1, 1, true, true);
        CheckWinding("t", 1, 1, 1);
        CheckBatchEqual("t", "x", 1, 1);
        CheckAlphaWrap("t", true, okWrap, "", cube, true, true);
        CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.InvalidInput, "", null, true, false);
        CheckAlphaWrap("t", false, MeshAlphaWrap.WrapStatus.InvalidParameters, "", null, false, true);
        CheckBoolean("t", true, S, cube, true, 0, 0, 0);
        CheckRepair("t", true, "", cube);
        CheckRounding("t", true, true, cube, 0, 1);
        CheckRemesh("t", true, cube, cube, cube, cube);
        CheckReduce("t", true, cube, cube, cube, cube, 1, 0.1);
        CheckMesher2("t", "Success", "", true, "");
        CheckClipper("t", "Success", "", true, true, 0, "");
        return 0;
    }

    static DMesh3 ScaledBy(DMesh3 m, double k)
    {
        var n = new DMesh3(m);
        foreach (int v in n.VertexIndices().ToArray()) n.SetVertex(v, k * n.GetVertex(v));
        return n;
    }

    static DMesh3 ShiftedBy(DMesh3 m, double dx)
    {
        var n = new DMesh3(m);
        n.SetVertex(0, n.GetVertex(0) + new Vector3d(dx, 0, 0));
        return n;
    }

    static DMesh3 NotFinite(DMesh3 m)
    {
        var n = new DMesh3(m);
        n.SetVertex(0, new Vector3d(double.NaN, 0, 0));
        return n;
    }

    // a closed tetrahedron with corner c on the edge ab (the face abc exactly degenerate), times scale
    static DMesh3 FlatTetrahedron(double scale)
    {
        var m = new DMesh3();
        int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(scale * new Vector3d(2, 0, 0));
        int c = m.AppendVertex(scale * new Vector3d(1, 0, 0)), d = m.AppendVertex(scale * new Vector3d(0, 1, 1));
        m.AppendTriangle(a, c, b);
        m.AppendTriangle(a, b, d);
        m.AppendTriangle(b, c, d);
        m.AppendTriangle(c, a, d);
        return m;
    }

    static void ExpectFinding(string what, Action check)
    {
        try {
            check();
        } catch (Finding) {
            return;
        }
        throw new InvalidOperationException("the check did not fire: " + what);
    }
}

