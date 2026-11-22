// The measurement of docs/research/ROUNDING_THIN_SOLIDS_2026-11.md (#1216): which closed components SafeRounding removes
// as tiny or thin, which of them are solids the inputs define, and what candidate rules would remove instead. Nothing
// in the library is changed: the boolean runs once with the default (what happens today) and once without SafeRounding;
// on that plainly rounded exact result the tool runs the same repair as the boolean does (the same options, the fixed
// vertices and point ids read from the boolean by reflection) with RemoveTinyComponents off, and then applies each
// rule to the components of the repaired mesh itself. The rule "cur" is today's rule written out again; `replica`
// says whether it gives the triangles of the boolean's own result (the check that the replay is faithful).
//   thin list <set>     the case names of a set (sheet, issue, copies, plate, and the sets of `list`)
//   thin <case>...      one JSON line per case
//   thin set <set>      one JSON line per case of the set, in one process (no timings are taken)
//   thin dump <case>    the operands and the plainly rounded exact result, as text
//   thin cost <case>    the time of the one pass a candidate rule adds, against the boolean and its rounding
// Families (all generated in code):
//   sheet:<k>:<L>:<off>:<frame>   the repro of #1216, scaled: A = [0,L]^2 x [0,L/10] minus B = [-L/100, 1.01 L]^2 x
//       [t, L/5], t = L 2^-k: a sheet of thickness t. frame: axis | rz (30 degrees about z) | issue (B tilted by 1e-4
//       degrees about x through (L/2, L/2, t), then rz: the issue's case) | gen (one generic rotation) | rza, gena (as
//       rz and gen with B's sides shifted, so that no edge of B passes over a corner of A). off: the
//       translation (0.25275, 0.56342, 1e-5) 10 L off (off = 1e5 at L = 100 is the issue's (2527500, 5634200, 100))
//   issue:<t>:<near|far>          the three rows of the issue's table, t as a decimal
//   fuzz:<0|1>                    the two cases of the Boolean fuzz harness (PR #1382), see FuzzBits
//   plate:<k>:<L>:<off>:<frame>   the cube [0,L]^3 united with the plate [1.5 L, 2.5 L]^2 x [0, t] beside it (an
//       untouched thin input solid)
//   copies:<shape>:<mode>:<e>:<L>:<off>:<seed>   A minus a nearly coincident copy A': shape box (generic rotation) or
//       sphere (cube-sphere, 6 vertices per edge); mode shift (A' = A moved by d along a generic direction), jitter
//       (every coordinate of A' moved by a random amount in [-d, d]) or turn (A' = A turned by d / L about a generic
//       axis); d = 2^e units in the last place of the largest coordinate of A at its place
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

static class ThinSolids
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "list") {
            foreach (string c in Cases(args[1])) Console.WriteLine(c);
            return 0;
        }
        if (args.Length == 2 && args[0] == "dump") {
            // the operands and the plainly rounded exact result of a case, as text (for a look at a single case)
            var (a, b, op) = Input(args[1]);
            var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = false };
            plain.Compute();
            foreach (var (label, m) in new[] { ("A", a), ("B", b), ("result", plain.Result) }) {
                Console.WriteLine(label + ": " + m.VertexCount + " vertices, " + m.TriangleCount + " triangles");
                foreach (int vid in m.VertexIndices()) Console.WriteLine("  v" + vid + " " + m.GetVertex(vid).x.ToString("R", ci) + " " + m.GetVertex(vid).y.ToString("R", ci) + " " + m.GetVertex(vid).z.ToString("R", ci));
                foreach (int t in m.TriangleIndices()) Console.WriteLine("  t" + t + " " + m.GetTriangle(t) + " area " + m.GetTriArea(t).ToString("R", ci));
            }
            return 0;
        }
        if (args.Length == 2 && args[0] == "cost") {
            Console.WriteLine(Cost(args[1]));
            return 0;
        }
        IEnumerable<string> cases = args.Length == 2 && args[0] == "set" ? Cases(args[1]) : args;
        foreach (string c in cases) {
            string line;
            try { line = Measure(c); }
            catch (Exception ex) { line = "{\"case\":" + Json(c) + ",\"ok\":false,\"error\":" + Json(ex.GetType().Name + ": " + ex.Message) + "}"; }
            Console.WriteLine(line);
        }
        return 0;
    }

    static readonly string[] Offsets = { "0", "1e1", "1e3", "1e5", "1e7" };

    static IEnumerable<string> Cases(string set)
    {
        switch (set) {
            case "issue":
                foreach (string t in new[] { "1e-3", "1e-4", "1e-5" })
                    foreach (string where in new[] { "near", "far" })
                        yield return $"issue:{t}:{where}";
                break;
            case "fuzz":
                yield return "fuzz:0";
                yield return "fuzz:1";
                break;
            case "sheet":
                foreach (string frame in new[] { "axis", "rz", "issue", "gen", "rza", "gena" })
                    foreach (string L in new[] { "1", "100", "1e4", "1e5" })
                        foreach (string off in Offsets)
                            for (int k = 7; k <= 52; k += 3)
                                yield return $"sheet:{k}:{L}:{off}:{frame}";
                break;
            case "plate":
                foreach (string frame in new[] { "axis", "gen" })
                    foreach (string L in new[] { "1", "1e4" })
                        foreach (string off in new[] { "0", "1e5" })
                            for (int k = 7; k <= 52; k += 3)
                                yield return $"plate:{k}:{L}:{off}:{frame}";
                break;
            case "copies":
                foreach (string shape in new[] { "box", "sphere" })
                    foreach (string mode in new[] { "shift", "jitter", "turn" })
                        foreach (string L in new[] { "1", "1e4" })
                            foreach (string off in new[] { "0", "1e5" })
                                foreach (int e in new[] { 0, 1, 2, 3, 4, 6, 8, 10, 14, 18, 24, 30, 36 })
                                    for (int seed = 0; seed < 3; seed++)
                                        yield return $"copies:{shape}:{mode}:{e}:{L}:{off}:{seed}";
                break;
            default:
                foreach (string c in Program.Cases(set)) yield return c;
                break;
        }
    }

    static (DMesh3 a, DMesh3 b, Ops op) Input(string name)
    {
        string[] p = name.Split(':');
        switch (p[0]) {
            case "fuzz": {
                int n = int.Parse(p[1]);
                return (FromBits(FuzzBits[2 * n]), FromBits(FuzzBits[2 * n + 1]), Ops.Difference);
            }
            case "issue": {
                double t = double.Parse(p[1], ci);
                var (a, b) = Sheet(100, t, "issue");
                if (p[2] == "far") { Move(a, new Vector3d(2527500, 5634200, 100)); Move(b, new Vector3d(2527500, 5634200, 100)); }
                return (a, b, Ops.Difference);
            }
            case "sheet": {
                double L = double.Parse(p[2], ci), off = double.Parse(p[3], ci);
                var (a, b) = Sheet(L, L * Math.Pow(2, -int.Parse(p[1])), p[4]);
                Vector3d d = Offset(L, off);
                Move(a, d); Move(b, d);
                return (a, b, Ops.Difference);
            }
            case "plate": {
                double L = double.Parse(p[2], ci), off = double.Parse(p[3], ci), t = L * Math.Pow(2, -int.Parse(p[1]));
                DMesh3 a = Box(0, 0, 0, L, L, L), b = Box(1.5 * L, 1.5 * L, 0, 2.5 * L, 2.5 * L, t);
                if (p[4] == "gen") { Turn(a, Generic); Turn(b, Generic); }
                Vector3d d = Offset(L, off);
                Move(a, d); Move(b, d);
                return (a, b, Ops.Union);
            }
            case "copies": {
                double L = double.Parse(p[4], ci), off = double.Parse(p[5], ci);
                int e = int.Parse(p[3]), seed = int.Parse(p[6]);
                var rng = new Random(1000 * seed + e);
                DMesh3 a;
                if (p[1] == "box") {
                    a = Box(-L / 2, -L / 2, -L / 2, L / 2, L / 2, L / 2);
                    Turn(a, Quaterniond.AxisAngleD(new Vector3d(0.3 + 0.1 * seed, 0.5, 0.81).Normalized, 23 + 7 * seed));
                } else {
                    a = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = L / 2 }.Generate().MakeDMesh();
                    Turn(a, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5 + 0.1 * seed, 0.81).Normalized, 23));
                }
                Move(a, Offset(L, off));
                double d = Math.Pow(2, e) * Program.Ulp(Program.Largest(a));
                var b = new DMesh3(a);
                Vector3d dir = new Vector3d(0.61, -0.37, 0.7).Normalized, centre = a.CachedBounds.Center;
                foreach (int vid in b.VertexIndices().ToArray()) {
                    Vector3d v = b.GetVertex(vid);
                    if (p[2] == "shift") v += d * dir;
                    else if (p[2] == "jitter") v += d * new Vector3d(2 * rng.NextDouble() - 1, 2 * rng.NextDouble() - 1, 2 * rng.NextDouble() - 1);
                    else v = centre + Quaterniond.AxisAngleR(dir, d / L) * (v - centre);
                    b.SetVertex(vid, v);
                }
                return (a, b, Ops.Difference);
            }
        }
        return Program.Input(name);
    }

    // the family "fuzz": the two cases the Boolean fuzz harness of PR #1382 reported on net48 (its CI log of 6f055e0a,
    // ExactBooleanFuzzTests.Smoke "slab", seeds 0 and 1): a box minus a thin slab at its top, the coordinates as bits.
    //   fuzz:0   slab of thickness 2^-30 at z = 3 - 2^-29, sheared;  fuzz:1   slab of thickness 2^-43 at z = 3, turned by 3.8e-6 rad
    static readonly ulong[][] FuzzBits = {
        new ulong[] { 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4000000000000000, 0x0000000000000000, 0x4000000000000000, 0x4000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x4000000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x4000000000000000, 0x4008000000000000, 0x4000000000000000, 0x4000000000000000, 0x4008000000000000 },
        new ulong[] { 0xBFF0000000000000, 0xBFF0000000000000, 0x4007BFFFFF400000, 0x4014000000000000, 0xBFF0000000000000, 0x4007C00002400000, 0xBFF0000000000000, 0x4014000000000000, 0x40093FFFFF400000, 0x4014000000000000, 0x4014000000000000, 0x4009400002400000, 0xBFF0000000000000, 0xBFF0000000000000, 0x4007BFFFFF600000, 0x4014000000000000, 0xBFF0000000000000, 0x4007C00002600000, 0xBFF0000000000000, 0x4014000000000000, 0x40093FFFFF600000, 0x4014000000000000, 0x4014000000000000, 0x4009400002600000 },
        new ulong[] { 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x3FF0000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x3FF0000000000000, 0x4008000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x4008000000000000, 0x3FF0000000000000, 0x0000000000000000, 0x4008000000000000, 0x0000000000000000, 0x4008000000000000, 0x4008000000000000, 0x3FF0000000000000, 0x4008000000000000, 0x4008000000000000 },
        new ulong[] { 0xBFF0000000002667, 0xBFEFFFFFFFFF6666, 0x4007FFFEA88A3ABB, 0x4013FFFFFFFFD000, 0xBFEFFFFFFFFD0000, 0x4007FFF94AB325A7, 0xBFEFFFFFFFFDE668, 0x4013FFFFFFFF799A, 0x40080009643864E3, 0x4014000000001CCD, 0x4013FFFFFFFFC667, 0x4008000406614FCF, 0xBFF0000000002667, 0xBFEFFFFFFFFF6666, 0x4007FFFEA88A3BBB, 0x4013FFFFFFFFD000, 0xBFEFFFFFFFFD0000, 0x4007FFF94AB326A7, 0xBFEFFFFFFFFDE668, 0x4013FFFFFFFF799A, 0x40080009643865E3, 0x4014000000001CCD, 0x4013FFFFFFFFC667, 0x40080004066150CF },
    };
    static readonly int[] FuzzTriangles = { 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6 };

    static DMesh3 FromBits(ulong[] bits)
    {
        var m = new DMesh3();
        for (int i = 0; i < bits.Length; i += 3)
            m.AppendVertex(new Vector3d(BitConverter.Int64BitsToDouble((long)bits[i]), BitConverter.Int64BitsToDouble((long)bits[i + 1]), BitConverter.Int64BitsToDouble((long)bits[i + 2])));
        for (int i = 0; i < FuzzTriangles.Length; i += 3) m.AppendTriangle(FuzzTriangles[i], FuzzTriangles[i + 1], FuzzTriangles[i + 2]);
        return m;
    }

    static readonly Quaterniond Generic = Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23);

    static Vector3d Offset(double L, double off) => new Vector3d(0.25275, 0.56342, 1e-5) * (10 * L * off);

    static (DMesh3 a, DMesh3 b) Sheet(double L, double t, string frame)
    {
        // (B's bottom diagonal runs over two corners of A: a coincidence, as faceted CAD models have them. The frames
        // rza and gena shift B's sides so that no edge of one passes over a vertex of the other)
        bool apart = frame == "rza" || frame == "gena";
        DMesh3 a = Box(0, 0, 0, L, L, L / 10), b = Box(-L / 100, apart ? -L / 50 : -L / 100, t, apart ? 1.03 * L : 1.01 * L, 1.01 * L, L / 5);
        if (frame == "issue") {
            var pivot = new Vector3d(L / 2, L / 2, t);
            Quaterniond tilt = Quaterniond.AxisAngleD(Vector3d.AxisX, 1e-4);
            foreach (int vid in b.VertexIndices().ToArray())
                b.SetVertex(vid, pivot + tilt * (b.GetVertex(vid) - pivot));
        }
        if (frame == "issue" || frame == "rz" || frame == "rza") {
            Quaterniond rz = Quaterniond.AxisAngleD(Vector3d.AxisZ, 30);
            Turn(a, rz); Turn(b, rz);
        } else if (frame == "gen" || frame == "gena") {
            Turn(a, Generic); Turn(b, Generic);
        }
        return (a, b);
    }

    static void Turn(DMesh3 m, Quaterniond q)
    {
        foreach (int vid in m.VertexIndices().ToArray()) m.SetVertex(vid, q * m.GetVertex(vid));
    }

    static void Move(DMesh3 m, Vector3d d)
    {
        if (d == Vector3d.Zero) return;
        foreach (int vid in m.VertexIndices().ToArray()) m.SetVertex(vid, m.GetVertex(vid) + d);
    }

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        var x = new[] { x0, x1 }; var y = new[] { y0, y1 }; var z = new[] { z0, z1 };
        for (int i = 0; i < 8; i++) m.AppendVertex(new Vector3d(x[i & 1], y[(i >> 1) & 1], z[(i >> 2) & 1]));
        int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
        for (int i = 0; i < f.Length; i += 3) m.AppendTriangle(f[i], f[i + 1], f[i + 2]);
        return m;
    }

    // one closed or open component of the repaired mesh (repair without the removal), with the component of the plainly
    // rounded exact result it came from (the edits add no vertex and stay within a component)
    sealed class Comp
    {
        public int[] Tris;
        public bool Closed, Touched, ZeroVolume, Tiny, Thin, InvertedResidue;
        public int Bad, Pairs, FalseContacts, Tris0;
        // as the library computes them (doubles, relative to a vertex of the component), after the edits
        public double Area1, Volume1, Largest1, MaxLength;
        // before the edits: the exact volume of the rounded exact result's component, its area, its largest coordinate
        public double Area0, Volume0, Largest0, Diagonal0;
        // the exact volume after the edits
        public double ExactVolume1;
        // the largest drift of an edit of the repair (MaxDrift: of the whole mesh, an upper bound for the component)
        public double Drift;
        public bool HasBad => Bad > 0 || Touched;
        public bool Defective => Bad > 0 || Pairs > 0 || FalseContacts > 0;
        public double Thickness0 => Area0 > 0 ? 2 * Math.Abs(Volume0) / Area0 : 0;
        public double Thickness1 => Area1 > 0 ? 2 * Math.Abs(ExactVolume1) / Area1 : 0;
        public double Ulp0 => Program.Ulp(Largest0);
        public bool Inflated => Math.Abs(ExactVolume1) > 2 * Math.Abs(Volume0) || 2 * Math.Abs(ExactVolume1) < Math.Abs(Volume0);
    }

    // the rules: whether a closed component that the repair edited or that keeps a degenerate triangle goes. All of them
    // keep the rest of today's rule: the untouched components (numerically zero volume, tiny inside-out residues), the
    // volume guard (a thin component with real volume for its area stays and counts as ScaleDisparity) and KeepWhenEmptied
    static readonly (string name, Func<Comp, bool> remove)[] Rules = {
        ("cur", c => c.Tiny || c.Thin),
        ("none", c => false),
        ("a_post_4", c => c.Tiny || c.Thickness1 < 4 * c.Ulp0),
        ("a_post_256", c => c.Tiny || c.Thickness1 < 256 * c.Ulp0),
        ("a_pre_4", c => c.Tiny || c.Thickness0 < 4 * c.Ulp0),
        ("a_pre_256", c => c.Tiny || c.Thickness0 < 256 * c.Ulp0),
        ("a_pre_65536", c => c.Tiny || c.Thickness0 < 65536 * c.Ulp0),
        ("a_pre_256_notiny", c => c.Thickness0 < 256 * c.Ulp0),
        ("b", c => (c.Tiny || c.Thin) && c.Defective),
        ("c_4", c => c.Thickness0 < 4 * c.Ulp0 || ((c.Tiny || c.Thin) && c.Defective)),
        ("c_256", c => c.Thickness0 < 256 * c.Ulp0 || ((c.Tiny || c.Thin) && c.Defective)),
        ("d_4", c => c.Thickness0 < 4 * c.Ulp0 || ((c.Tiny || c.Thin) && (c.Defective || c.Inflated))),
        ("d_256", c => c.Thickness0 < 256 * c.Ulp0 || ((c.Tiny || c.Thin) && (c.Defective || c.Inflated))),
        ("e_4", c => c.Thickness0 < 4 * c.Ulp0 || ((c.Tiny || c.Thin) && (c.Defective || c.Thickness0 < 2 * c.Drift))),
        ("e_256", c => c.Thickness0 < 256 * c.Ulp0 || ((c.Tiny || c.Thin) && (c.Defective || c.Thickness0 < 2 * c.Drift))),
    };

    static string Measure(string name)
    {
        var (a, b, op) = Input(name);
        var sb = new StringBuilder();
        sb.Append("{\"case\":").Append(Json(name));
        var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = false };
        var safe = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        if (!plain.Compute() || !safe.Compute())
            return sb.Append(",\"ok\":false,\"error\":").Append(Json(plain.Error ?? safe.Error ?? "")).Append('}').ToString();
        DMesh3 raw = plain.Result;
        MeshRoundingRepair r = safe.Rounding;
        sb.Append(",\"ok\":true,\"status\":").Append(Json(safe.Status.ToString()));
        sb.Append(",\"input_largest\":").Append(D(Math.Max(Program.Largest(a), b.TriangleCount > 0 ? Program.Largest(b) : 0)));
        sb.Append(",\"raw\":{\"tris\":").Append(raw.TriangleCount).Append(",\"volume\":").Append(D(ExactVolume(raw, raw.TriangleIndices())));
        sb.Append(",\"reasons\":").Append(Json(gs.MeshRobustRepair.NotCleanReasons(raw))).Append('}');
        sb.Append(",\"safe\":{\"tris\":").Append(safe.Result.TriangleCount).Append(",\"volume\":").Append(D(ExactVolume(safe.Result, safe.Result.TriangleIndices())));
        sb.Append(",\"reasons\":").Append(Json(gs.MeshRobustRepair.NotCleanReasons(safe.Result)));
        sb.Append(",\"rounding_clean\":").Append(B(r.IsClean)).Append(",\"removed_components\":").Append(r.RemovedComponents).Append(",\"removed_tris\":").Append(r.RemovedTriangles);
        sb.Append(",\"scale_disparity\":").Append(r.ScaleDisparity).Append(",\"limit\":").Append(B(r.EvaluationLimitReached));
        sb.Append(",\"init_intersections\":").Append(r.InitialIntersections).Append(",\"init_degenerate\":").Append(r.InitialDegenerate).Append(",\"init_false_contacts\":").Append(r.InitialFalseContacts);
        sb.Append(",\"collapses\":").Append(r.Collapses).Append(",\"flips\":").Append(r.Flips).Append(",\"moves\":").Append(r.Moves).Append(",\"zips\":").Append(r.Zips);
        sb.Append(",\"vertex_removals\":").Append(r.VertexRemovals).Append(",\"max_length\":").Append(D(r.MaxLength)).Append(",\"max_drift\":").Append(D(r.MaxDrift)).Append('}');
        if (raw.TriangleCount == 0)
            return sb.Append('}').ToString();

        // the repair as the boolean runs it (ExactMeshBoolean.Compute), on a copy of the plain result with the same ids
        bool[] fixedV = (bool[])typeof(ExactMeshBoolean).GetField("resultFixed", Private).GetValue(plain);
        int[] pointId = (int[])typeof(ExactMeshBoolean).GetField("resultPointId", Private).GetValue(plain);
        bool keepWhenEmptied = MeshRoundingRepair.IsThinOrTiny(a, 1e-5, 1e-12) || MeshRoundingRepair.IsThinOrTiny(b, 1e-5, 1e-12);
        MeshRoundingRepair Repair(DMesh3 mesh, bool removeTiny) => new MeshRoundingRepair(mesh) {
            SharedPositionsTouch = true, FixedVertexF = (v) => v < fixedV.Length && fixedV[v], FixedWhereCoarse = false,
            PointIdF = (v) => v < pointId.Length ? pointId[v] : -1, ExactVertexF = (v) => v < fixedV.Length && fixedV[v],
            KeepWhenEmptied = keepWhenEmptied, RemoveTinyComponents = removeTiny
        };
        // the scales the removal uses, taken at the start of the repair: the largest component's diagonal
        double size = gs.MeshRepairMetrics.LargestDiagonal(gs.MeshRepairMetrics.ComponentBoxes(raw, out int[] rawComponentOf));
        var mesh = new DMesh3(raw);
        MeshRoundingRepair probe = Repair(mesh, false);
        probe.Apply();
        var touched = (HashSet<int>)typeof(MeshRoundingRepair).GetField("touched", Private).GetValue(probe);
        var bad = (List<int>)typeof(MeshRoundingRepair).GetMethod("degenerate_triangles", Private).Invoke(probe, null);
        var pairs = (List<Index2i>)typeof(MeshRoundingRepair).GetMethod("intersecting_pairs", Private).Invoke(probe, null);
        var contacts = probe.RemainingFalseContacts > 0 ? (List<List<int>>)typeof(MeshRoundingRepair).GetMethod("false_contacts", Private).Invoke(probe, null) : new List<List<int>>();
        sb.Append(",\"keep_when_emptied\":").Append(B(keepWhenEmptied)).Append(",\"size\":").Append(S(size)).Append(",\"box\":").Append(S(gs.MeshRepairMetrics.TriangleBounds(raw).DiagonalLength));
        sb.Append(",\"probe\":{\"tris\":").Append(mesh.TriangleCount).Append(",\"degenerate\":").Append(bad.Count).Append(",\"pairs\":").Append(pairs.Count);
        sb.Append(",\"false_contacts\":").Append(contacts.Count).Append(",\"max_drift\":").Append(D(probe.MaxDrift)).Append(",\"limit\":").Append(B(probe.EvaluationLimitReached)).Append('}');

        // the components before the edits, by vertex
        var raw0 = new Dictionary<int, (List<int> tris, AxisAlignedBox3d box)>();
        foreach (int t in raw.TriangleIndices()) {
            int c = rawComponentOf[raw.GetTriangle(t).a];
            if (!raw0.TryGetValue(c, out var e)) e = (new List<int>(), AxisAlignedBox3d.Empty);
            e.tris.Add(t);
            Index3i tv = raw.GetTriangle(t);
            e.box.Contain(raw.GetVertex(tv.a)); e.box.Contain(raw.GetVertex(tv.b)); e.box.Contain(raw.GetVertex(tv.c));
            raw0[c] = e;
        }
        var isBad = new bool[mesh.MaxTriangleID];
        foreach (int t in bad) isBad[t] = true;
        var comps = new List<Comp>();
        var compOfTri = new int[mesh.MaxTriangleID];
        foreach (List<int> list in MeshRoundingRepair.connected_components(mesh)) {
            var c = new Comp() { Tris = list.ToArray(), Closed = true, Drift = probe.MaxDrift };
            Vector3d o = mesh.GetVertex(mesh.GetTriangle(list[0]).a);
            double tripleNorms = 0;
            foreach (int t in list) {
                compOfTri[t] = comps.Count;
                Index3i tv = mesh.GetTriangle(t), te = mesh.GetTriEdges(t);
                Vector3d pa = mesh.GetVertex(tv.a), pb = mesh.GetVertex(tv.b), pc = mesh.GetVertex(tv.c);
                c.Touched |= touched.Contains(tv.a) || touched.Contains(tv.b) || touched.Contains(tv.c);
                if (isBad[t]) c.Bad++;
                c.Area1 += 0.5 * (pb - pa).Cross(pc - pa).Length;
                c.Volume1 += (pa - o).Dot((pb - o).Cross(pc - o)) / 6;
                tripleNorms += (pa - o).Length * (pb - o).Length * (pc - o).Length;
                c.Largest1 = Math.Max(c.Largest1, Math.Max(pa.MaxAbs, Math.Max(pb.MaxAbs, pc.MaxAbs)));
                c.Closed &= !mesh.IsBoundaryEdge(te.a) && !mesh.IsBoundaryEdge(te.b) && !mesh.IsBoundaryEdge(te.c);
            }
            var (tris0, box0) = raw0[rawComponentOf[mesh.GetTriangle(list[0]).a]];
            c.Tris0 = tris0.Count;
            c.Largest0 = Math.Max(box0.Min.MaxAbs, box0.Max.MaxAbs);
            c.Diagonal0 = box0.DiagonalLength;
            foreach (int t in tris0) c.Area0 += raw.GetTriArea(t);
            c.Volume0 = ExactVolume(raw, tris0);
            c.ExactVolume1 = ExactVolume(mesh, list);
            // the bound of the component, as component_scales sets it
            c.MaxLength = Math.Max(1e-5 * size, 256 * Program.Ulp(c.Largest0));
            c.Tiny = c.Area1 < 1e4 * 1e-12 * size * size;
            c.Thin = 2 * Math.Abs(c.Volume1) < c.MaxLength * c.Area1;
            c.ZeroVolume = c.Closed && MeshRoundingRepair.numerically_zero_volume(mesh, list, c.Volume1, c.Area1, c.Largest1, tripleNorms);
            comps.Add(c);
        }
        foreach (Index2i pr in pairs) {
            comps[compOfTri[pr.a]].Pairs++;
            if (compOfTri[pr.b] != compOfTri[pr.a]) comps[compOfTri[pr.b]].Pairs++;
        }
        foreach (List<int> group in contacts)
            foreach (int c in group.Select(v => compOfTri[mesh.VtxTrianglesItr(v).First()]).Distinct())
                comps[c].FalseContacts++;

        // the components a rule can remove (closed, and tiny, thin or of numerically zero volume by today's tests); the
        // others are counted
        sb.Append(",\"other_components\":").Append(comps.Count(c => !Candidate(c))).Append(",\"comps\":[");
        bool firstComp = true;
        for (int i = 0; i < comps.Count; i++) {
            Comp c = comps[i];
            if (!Candidate(c)) continue;
            sb.Append(firstComp ? "" : ",").Append("{\"i\":").Append(i).Append(",\"tris\":").Append(c.Tris.Length).Append(",\"tris0\":").Append(c.Tris0);
            sb.Append(",\"area0\":").Append(D(c.Area0)).Append(",\"volume0\":").Append(D(c.Volume0)).Append(",\"area1\":").Append(S(c.Area1)).Append(",\"volume1\":").Append(D(c.ExactVolume1));
            sb.Append(",\"largest0\":").Append(D(c.Largest0)).Append(",\"diagonal0\":").Append(S(c.Diagonal0)).Append(",\"max_length\":").Append(S(c.MaxLength));
            sb.Append(",\"touched\":").Append(B(c.Touched)).Append(",\"bad\":").Append(c.Bad).Append(",\"pairs\":").Append(c.Pairs).Append(",\"false_contacts\":").Append(c.FalseContacts);
            sb.Append(",\"tiny\":").Append(B(c.Tiny)).Append(",\"thin\":").Append(B(c.Thin)).Append(",\"zero_volume\":").Append(B(c.ZeroVolume)).Append('}');
            firstComp = false;
        }
        sb.Append("]");

        // every rule; the outcomes are verified once per distinct set of removed components
        var outcomes = new Dictionary<string, int>();
        var outcomeList = new StringBuilder();
        var ruleList = new StringBuilder();
        bool replica = false;
        foreach (var (rule, remove) in Rules) {
            var go = new List<int>();
            int disparity = 0;
            var invertedCandidates = new List<int>();
            for (int i = 0; i < comps.Count; i++) {
                Comp c = comps[i];
                // (the removal runs only where the repair edited or found something, as in Apply)
                if (!c.Closed || rule == "none" || (bad.Count == 0 && touched.Count == 0)) continue;
                if (!c.HasBad) {
                    if (!c.Tiny && !c.Thin && !c.ZeroVolume) continue;
                    if (c.ZeroVolume) go.Add(i);
                    else if (c.Tiny && gs.MeshPostRepairCleanup.volume_sign(mesh, c.Tris) < 0) invertedCandidates.Add(i);
                    continue;
                }
                if (!remove(c) && !c.ZeroVolume) continue;
                if (!c.Tiny && Math.Abs(c.Volume1) >= 1e-3 * c.Area1 * Math.Sqrt(c.Area1)) { disparity++; continue; }
                go.Add(i);
            }
            if (invertedCandidates.Count > 0) {
                var rest = new DMesh3(mesh);
                foreach (int i in go) foreach (int t in comps[i].Tris) rest.RemoveTriangle(t, true);
                var flagged = new HashSet<int>();
                foreach (int[] fc in gs.MeshPostRepairCleanup.FindInvertedComponents(rest)) flagged.Add(fc[0]);
                foreach (int i in invertedCandidates) if (flagged.Contains(comps[i].Tris[0])) go.Add(i);
            }
            if (keepWhenEmptied && go.Count > 0 && go.Sum(i => comps[i].Tris.Length) == mesh.TriangleCount) { disparity += go.Count; go.Clear(); }
            go.Sort();
            string key = string.Join(",", go);
            if (!outcomes.TryGetValue(key, out int outcome)) {
                var final = new DMesh3(mesh);
                foreach (int i in go) foreach (int t in comps[i].Tris) final.RemoveTriangle(t, true);
                var gone = new HashSet<int>(go);
                int defects = 0;
                for (int i = 0; i < comps.Count; i++) if (!gone.Contains(i) && comps[i].Defective) defects++;
                outcomes[key] = outcome = outcomes.Count;
                outcomeList.Append(outcome > 0 ? "," : "").Append("{\"removed\":[").Append(key).Append("],\"tris\":").Append(final.TriangleCount).Append(",\"volume\":").Append(S(ExactVolume(final, final.TriangleIndices())));
                outcomeList.Append(",\"defective_components\":").Append(defects).Append(",\"reasons\":").Append(Json(final.TriangleCount > 0 ? gs.MeshRobustRepair.NotCleanReasons(final) : "empty")).Append('}');
                if (rule == "cur") {
                    // the check of the replay: today's rule, written out here, against the boolean's own result
                    var mine = new HashSet<(Vector3d, Vector3d, Vector3d)>();
                    foreach (int t in final.TriangleIndices()) mine.Add(GuaranteeBound.Key(final, t));
                    replica = final.TriangleCount == safe.Result.TriangleCount && go.Count == r.RemovedComponents;
                    foreach (int t in safe.Result.TriangleIndices()) replica &= mine.Contains(GuaranteeBound.Key(safe.Result, t));
                }
            }
            ruleList.Append(ruleList.Length > 0 ? "," : "").Append(Json(rule)).Append(":[").Append(outcome).Append(',').Append(disparity).Append(']');
        }
        // rules: per rule, the index of its outcome and the components it keeps as ScaleDisparity
        sb.Append(",\"replica\":").Append(B(replica)).Append(",\"outcomes\":[").Append(outcomeList).Append("],\"rules\":{").Append(ruleList).Append('}');
        return sb.Append('}').ToString();
    }

    // what a candidate rule adds to the repair: one pass over the triangles before the edits (area, volume and largest
    // coordinate per component, in doubles), against the time of the rounding today. Median of 9 passes
    static string Cost(string name)
    {
        var (a, b, op) = Input(name);
        new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op).Compute();
        var safe = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        safe.Compute();
        double msBoolean = clock.Elapsed.TotalMilliseconds;
        var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = false };
        plain.Compute();
        DMesh3 raw = plain.Result;
        var times = new List<double>();
        double check = 0;
        int components = 0;
        for (int round = 0; round < 9; round++) {
            clock.Restart();
            components = 0;
            foreach (List<int> comp in MeshRoundingRepair.connected_components(raw)) {
                double area = 0, volume = 0, largest = 0;
                Vector3d o = raw.GetVertex(raw.GetTriangle(comp[0]).a);
                foreach (int t in comp) {
                    Index3i tv = raw.GetTriangle(t);
                    Vector3d pa = raw.GetVertex(tv.a), pb = raw.GetVertex(tv.b), pc = raw.GetVertex(tv.c);
                    area += 0.5 * (pb - pa).Cross(pc - pa).Length;
                    volume += (pa - o).Dot((pb - o).Cross(pc - o)) / 6;
                    largest = Math.Max(largest, Math.Max(pa.MaxAbs, Math.Max(pb.MaxAbs, pc.MaxAbs)));
                }
                check += area + volume + largest;
                components++;
            }
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        safe.PhaseMilliseconds.TryGetValue("rounding", out long msRounding);
        return "{\"case\":" + Json(name) + ",\"tris\":" + raw.TriangleCount + ",\"components\":" + components + ",\"ms_boolean\":" + msBoolean.ToString("F1", ci)
            + ",\"ms_rounding\":" + msRounding + ",\"ms_pass\":" + times[4].ToString("F3", ci) + ",\"check\":" + S(check) + "}";
    }

    static bool Candidate(Comp c) => c.Closed && (c.Tiny || c.Thin || c.ZeroVolume);

    // the volume of the triangles, exact: doubles are integers times a power of two, so the sum of the determinants
    // relative to the first corner is an integer times a power of two; rounded once at the end
    static double ExactVolume(DMesh3 mesh, IEnumerable<int> tris)
    {
        int minExp = int.MaxValue;
        var list = tris as IList<int> ?? tris.ToList();
        if (list.Count == 0) return 0;
        foreach (int t in list) {
            Index3i tv = mesh.GetTriangle(t);
            for (int k = 0; k < 3; k++) {
                Vector3d v = mesh.GetVertex(tv[k]);
                for (int j = 0; j < 3; j++) if (v[j] != 0) minExp = Math.Min(minExp, Exponent(v[j]));
            }
        }
        if (minExp == int.MaxValue) return 0;
        BigInteger[] Int(Vector3d v) => new[] { Scaled(v.x, minExp), Scaled(v.y, minExp), Scaled(v.z, minExp) };
        BigInteger[] o = Int(mesh.GetVertex(mesh.GetTriangle(list[0]).a));
        BigInteger sum = BigInteger.Zero;
        var cache = new Dictionary<int, BigInteger[]>();
        BigInteger[] Rel(int vid)
        {
            if (!cache.TryGetValue(vid, out BigInteger[] q)) {
                q = Int(mesh.GetVertex(vid));
                for (int j = 0; j < 3; j++) q[j] -= o[j];
                cache[vid] = q;
            }
            return q;
        }
        foreach (int t in list) {
            Index3i tv = mesh.GetTriangle(t);
            BigInteger[] p = Rel(tv.a), q = Rel(tv.b), s = Rel(tv.c);
            sum += p[0] * (q[1] * s[2] - q[2] * s[1]) - p[1] * (q[0] * s[2] - q[2] * s[0]) + p[2] * (q[0] * s[1] - q[1] * s[0]);
        }
        // (double)sum and the division by 6 round once each; the scaling by a power of two is exact unless it leaves the double range
        return (double)sum / 6 * Math.Pow(2, minExp) * Math.Pow(2, minExp) * Math.Pow(2, minExp);
    }

    // x = m 2^e with an integer m: the e of a finite nonzero double
    static int Exponent(double x)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        int e = (int)((bits >> 52) & 0x7FF);
        return (e == 0 ? 1 : e) - 1075;
    }

    static BigInteger Scaled(double x, int minExp)
    {
        if (x == 0) return BigInteger.Zero;
        long bits = BitConverter.DoubleToInt64Bits(x);
        int e = (int)((bits >> 52) & 0x7FF);
        long m = bits & 0xFFFFFFFFFFFFFL;
        if (e != 0) m |= 1L << 52;
        BigInteger v = new BigInteger(m) << ((e == 0 ? 1 : e) - 1075 - minExp);
        return bits < 0 ? -v : v;
    }

    static string D(double x) => double.IsNaN(x) || double.IsInfinity(x) ? "null" : x.ToString("R", ci);
    // (six digits, where a value is only shown or compared loosely; what decides a band keeps all digits)
    static string S(double x) => double.IsNaN(x) || double.IsInfinity(x) ? "null" : x.ToString("G6", ci);
    static string B(bool x) => x ? "true" : "false";
    static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
