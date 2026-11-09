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
        // scale 2^e, e in [-1022, 1023] (centred on 0), and an offset of k * 2^f
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

    static int Components(DMesh3 m)
    {
        var comps = new MeshConnectedComponents(m);
        comps.FindConnectedT();
        return comps.Count;
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
            if (ok != (b.Status == ExactMeshBoolean.BooleanStatus.Success))
                throw new Finding($"{what}: Compute() {ok} but Status {b.Status}");
            if (!ok)
                return;
            if (b.Result == null)
                throw new Finding($"{what}: Success without a Result");
            if (!AllFinite(b.Result))
                throw new Finding($"{what}: a result vertex is not finite");
            // the claim: a clean rounding of closed inputs is clean in memory (contacts aside: they are reported, and
            // the self-intersection check sees them; degenerate triangles by the diagonal of all components can differ
            // from the rounding's per-component scale when components lie far apart, documented)
            if (b.RoundingClean && b.Result.TriangleCount > 0) {
                string reason = MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: b.Contacts == 0);
                if (reason != "" && !(reason == "degenerate triangles" && Components(b.Result) > 1))
                    throw new Finding($"{what}: RoundingClean but NotCleanReason \"{reason}\"");
            }
        } else if (c.Op <= 4) {
            DMesh3 input = new DMesh3(c.A);
            if (c.Op == 4)
                new MeshEditor(input).AppendMesh(c.B);
            var repair = new MeshRobustRepair(input);
            bool ok = repair.Apply();
            if (ok && repair.ResultReason == "") {
                if (!AllFinite(repair.Mesh))
                    throw new Finding($"{what}: repaired mesh has a vertex that is not finite");
                string reason = MeshRobustRepair.NotCleanReason(repair.Mesh);
                if (reason != "" && !(reason == "degenerate triangles" && Components(repair.Mesh) > 1))
                    throw new Finding($"{what}: repaired (ResultReason \"\") but NotCleanReason \"{reason}\"");
            }
        } else {
            DMesh3 m = new DMesh3(c.A);
            new MeshEditor(m).AppendMesh(c.B);
            var repair = new MeshRoundingRepair(m);
            bool clean = repair.Apply();
            if (clean != repair.IsClean)
                throw new Finding($"{what}: Apply() {clean} but IsClean {repair.IsClean}");
            if (!AllFinite(m))
                throw new Finding($"{what}: a vertex is not finite after the rounding repair");
            if (clean && MeshRepairMetrics.SelfIntersections(m) != 0)
                throw new Finding($"{what}: IsClean but {MeshRepairMetrics.SelfIntersections(m)} self-intersections");
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

    // the decoder is deterministic and covers every operation, the seeds run without a finding, and the checks fire
    static int SelfTest()
    {
        foreach (var (name, data) in Seeds()) {
            Case a = Decode(data), b = Decode(data);
            if (a.ToString() != b.ToString() || !a.A.Vertices().SequenceEqual(b.A.Vertices()) || !a.B.Vertices().SequenceEqual(b.B.Vertices()))
                throw new InvalidOperationException("decoder not deterministic: " + name);
            if (!a.A.IsClosed() || !a.B.IsClosed())
                throw new InvalidOperationException("seed shape not closed: " + name);
            Run(a);
        }
        if (Seeds().Select(s => Decode(s.data).Op).Distinct().Count() != Ops.Length)
            throw new InvalidOperationException("the seeds do not cover every operation");
        // a short or empty input decodes too (all zeros)
        Run(Decode(Array.Empty<byte>()));
        // the check of a clean claim fires on a mesh that is not clean
        var open = new DMesh3();
        open.AppendTriangle(open.AppendVertex(Vector3d.Zero), open.AppendVertex(Vector3d.AxisX), open.AppendVertex(Vector3d.AxisY));
        if (MeshRobustRepair.NotCleanReason(open) == "")
            throw new InvalidOperationException("NotCleanReason accepts an open triangle");
        return 0;
    }
}
