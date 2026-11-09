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
public static class Program
{
    /// <summary> what an input decodes to </summary>
    public sealed class Case
    {
        public int Op;
        public DMesh3 A = null!, B = null!;
        public override string ToString() => $"op {Op} ({Ops[Op]}): A {A.VertexCount} v / {A.TriangleCount} t, B {B.VertexCount} v / {B.TriangleCount} t";
    }

    public static readonly string[] Ops = { "union", "intersection", "difference", "robust-repair", "robust-repair-soup", "rounding-repair" };

    // base shapes, closed and consistently oriented: tetrahedron, octahedron, cube
    static readonly double[][] ShapeVertices = {
        new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 },
        new double[] { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 },
        new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1 },
    };
    static readonly int[][] ShapeTriangles = {
        new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 },
        new[] { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 },
        new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 },
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

    static DMesh3 DecodeMesh(Reader r)
    {
        int shape = r.Byte() % ShapeVertices.Length;
        // scale 2^e, e in [-1022, 1023], and an offset of k * 2^f
        int e = ((r.Byte() << 8) | r.Byte()) % 2046 - 1022;
        double scale = Pow2(e);
        int f = ((r.Byte() << 8) | r.Byte()) % 2046 - 1022;
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
        var c = new Case { Op = r.Byte() % Ops.Length };
        c.A = DecodeMesh(r);
        c.B = DecodeMesh(r);
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
            largest = Math.Max(largest, box.DiagonalLength);
        }
        return largest;
    }

    /// <summary>
    /// a copy scaled by a power of two so that the largest component's diagonal is in [1, 2), where the checks'
    /// thresholds (1e-12 times a squared diagonal) neither underflow nor overflow; exact unless coordinates become
    /// subnormal; the mesh itself if the scale is not finite or the scaled coordinates would overflow
    /// </summary>
    public static DMesh3 Normalized(DMesh3 m)
    {
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
        int contacts, int nonManifoldTriangles, int conflictEdges)
    {
        if (ok != (status == ExactMeshBoolean.BooleanStatus.Success))
            throw new Finding($"{what}: Compute() {ok} but Status {status}");
        if (!ok)
            return;
        if (result == null)
            throw new Finding($"{what}: Success without a Result");
        if (!AllFinite(result))
            throw new Finding($"{what}: a result vertex is not finite");
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
    public static void CheckRounding(string what, bool clean, bool isClean, DMesh3 mesh, double maxDrift, double maxLength)
    {
        if (clean != isClean)
            throw new Finding($"{what}: Apply() {clean} but IsClean {isClean}");
        if (!AllFinite(mesh))
            throw new Finding($"{what}: a vertex is not finite after the rounding repair");
        if (!mesh.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the mesh is not valid after the rounding repair");
        if (mesh.TriangleCount > 0 && !mesh.IsClosed())
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

    /// <summary> runs the case and checks the result's claims; throws on a finding (or lets an exception through) </summary>
    public static void Run(Case c)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string what = c.ToString();
        if (c.Op <= 2) {
            var op = c.Op == 0 ? ExactMeshBoolean.Operations.Union : c.Op == 1 ? ExactMeshBoolean.Operations.Intersection : ExactMeshBoolean.Operations.Difference;
            var b = new ExactMeshBoolean(c.A, c.B, op);
            bool ok = b.Compute();
            CheckBoolean(what, ok, b.Status, b.Result, b.RoundingClean, b.Contacts, b.NonManifoldTriangles, b.ConflictEdges);
        } else if (c.Op <= 4) {
            DMesh3 input = new DMesh3(c.A);
            if (c.Op == 4)
                new MeshEditor(input).AppendMesh(c.B);
            var repair = new MeshRobustRepair(input);
            bool ok = repair.Apply();
            CheckRepair(what, ok, repair.ResultReason, repair.Mesh);
        } else {
            DMesh3 m = new DMesh3(c.A);
            new MeshEditor(m).AppendMesh(c.B);
            var repair = new MeshRoundingRepair(m);
            bool clean = repair.Apply();
            CheckRounding(what, clean, repair.IsClean, m, repair.MaxDrift, repair.MaxLength);
        }
    }

    // seeds: each operation on the plain shapes, at unit size and with an offset (no perturbation)
    public static IEnumerable<(string name, byte[] data)> Seeds()
    {
        for (int op = 0; op < Ops.Length; ++op)
            for (int shape = 0; shape < 3; ++shape) {
                var bytes = new List<byte> { (byte)op };
                foreach (int s in new[] { shape, (shape + 1) % 3 }) {
                    bytes.Add((byte)s);
                    // e = 0 and f = 0: (1022 + e) as two bytes
                    bytes.AddRange(new byte[] { 0x03, 0xFE, 0x03, 0xFE });
                    bytes.AddRange(s == shape ? new byte[] { 0, 0, 0 } : new byte[] { 4, 3, 2 });
                    for (int i = 0; i < ShapeVertices[s].Length / 3; ++i)
                        bytes.AddRange(new byte[] { 0, 0, 0, 0 });
                }
                yield return ($"{Ops[op]}-{shape}.bin", bytes.ToArray());
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
        if (args.Length == 2 && args[0] == "replay") {
            Case c = Decode(File.ReadAllBytes(args[1]));
            Console.WriteLine(c);
            Run(c);
            return 0;
        }
        if (args.Length != 0) {
            Console.Error.WriteLine("usage: MeshFuzzPilot [write-seeds <empty dir> | replay <input> | self-test]");
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
        if (Seeds().Select(s => Decode(s.data).Op).Distinct().Count() != Ops.Length)
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
        // and pass on a clean result
        CheckBoolean("t", true, S, cube, true, 0, 0, 0);
        CheckRepair("t", true, "", cube);
        CheckRounding("t", true, true, cube, 0, 1);
        return 0;
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

