using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T11 (MarchingCubes; the terrain relations follow in their own PR): CsCheck campaign for MarchingCubes and MarchingCubesPro on random compositions of exact signed distance
    /// primitives (sphere, box, torus, capsule; union and subtraction), all 1-Lipschitz.
    ///   - LinearEdges at the normalized recipe (docs/design/CONSERVATIVE_MC_AUDIT.md: dyadic grid h = 2^-4 on [-1/2, 1/2]^3, level
    ///     sqrt(3) h / 2 + 2^-26, shapes away from the boundary): no exception, closed, valid, finite, and every sampled point of
    ///     {f &lt;= 0} has winding number above 0.99 (the enclosure);
    ///   - every root mode (MarchingCubes): parallel equals serial and a repeated run equals the first bit for bit; MarchingCubesPro is
    ///     compared with MarchingCubes in LinearEdges only (the same vertices and triangles by coordinates); and the field, bounds, cell size and level scaled by 2^k
    ///     give the same mesh scaled by 2^k, bit for bit (the arithmetic of the fields is +, -, *, sqrt, min, max, abs, and
    ///     rotation by a constant matrix: exact under 2^k).
    /// 20 samples in CI; G3_PROPERTY_SAMPLES=n, G3_PROPERTY_CAMPAIGN=marchingcubes with G3_PROPERTY_REPLAY=seed to replay.
    /// </summary>
    [Trait("Category", CampaignRunner.Category)]
    public class MarchingCubesCampaignTests
    {
        readonly ITestOutputHelper log;
        public MarchingCubesCampaignTests(ITestOutputHelper log) { this.log = log; }

        public sealed class Primitive
        {
            public int Kind;                // 0 sphere, 1 box, 2 torus, 3 capsule
            public int Cx, Cy, Cz;          // center in 1/64
            public int A, B;                // sphere radius A; box half sizes A, B, A + B / 2 ; torus R = A, r = B ; capsule radius B, half length A
            public int Angle;               // rotation about x in 1/8 radians (torus, capsule, box)
            public bool Subtract;           // removed from the union so far (not the first)
            public override string ToString() => $"{new[] { "sphere", "box", "torus", "capsule" }[Kind]}({Cx},{Cy},{Cz}; {A},{B}; angle {Angle}{(Subtract ? "; subtract" : "")})";
        }

        public sealed class Scene
        {
            public Primitive[] Parts;
            public int Mode;                // RootfindingModes 0 .. 3
            public int Cells;               // other modes: cells per axis, 8 .. 20
            public override string ToString() => $"{{{string.Join(" | ", Parts.Select(p => p.ToString()))}}}, mode {(MarchingCubes.RootfindingModes)Mode}, cells {Cells}";
        }

        /// <summary> the field at scale 2^shift: every length is multiplied exactly, so field(2^k p) = 2^k field(p) bit for bit </summary>
        sealed class Field : ImplicitFunction3d
        {
            readonly Scene scene; readonly double s;
            readonly double[] c, sn;
            public Field(Scene scene, int shift)
            {
                this.scene = scene; s = Math.ScaleB(1.0, shift);
                c = scene.Parts.Select(p => Math.Cos(p.Angle / 8.0)).ToArray();
                sn = scene.Parts.Select(p => Math.Sin(p.Angle / 8.0)).ToArray();
            }

            public double Value(ref Vector3d p)
            {
                double f = double.MaxValue;
                for (int i = 0; i < scene.Parts.Length; ++i) {
                    Primitive pr = scene.Parts[i];
                    Vector3d q = p - s * new Vector3d(pr.Cx / 64.0, pr.Cy / 64.0, pr.Cz / 64.0);
                    q = new Vector3d(q.x, c[i] * q.y - sn[i] * q.z, sn[i] * q.y + c[i] * q.z);
                    double a = s * (pr.A / 64.0), b = s * (pr.B / 64.0), d;
                    switch (pr.Kind) {
                        case 0: d = q.Length - a; break;
                        case 1: {
                            Vector3d e = new Vector3d(Math.Abs(q.x) - a, Math.Abs(q.y) - b, Math.Abs(q.z) - (a + b / 2));
                            d = new Vector3d(Math.Max(e.x, 0), Math.Max(e.y, 0), Math.Max(e.z, 0)).Length + Math.Min(Math.Max(e.x, Math.Max(e.y, e.z)), 0);
                            break;
                        }
                        case 2: {
                            double radial = Math.Sqrt(q.x * q.x + q.z * q.z) - a;
                            d = Math.Sqrt(radial * radial + q.y * q.y) - b;
                            break;
                        }
                        default: {
                            double y = Math.Max(-a, Math.Min(a, q.y));
                            Vector3d r = new Vector3d(q.x, q.y - y, q.z);
                            d = r.Length - b;
                            break;
                        }
                    }
                    f = i == 0 ? d : (pr.Subtract ? Math.Max(f, -d) : Math.Min(f, d));
                }
                return f;
            }
        }

        const double H = 1.0 / 16;
        static readonly double Sigma = Math.Sqrt(3) * H / 2 + Math.Pow(2, -26);

        static object Make(Scene scene, int shift, bool pro, bool parallel, MarchingCubes.RootfindingModes mode)
        {
            double s = Math.ScaleB(1.0, shift);
            int cells = mode == MarchingCubes.RootfindingModes.LinearEdges ? 16 : scene.Cells;
            double h = mode == MarchingCubes.RootfindingModes.LinearEdges ? H : 1.0 / cells;
            // (the recipe's grid for LinearEdges; another cell count for the legacy modes, where the grid is not dyadic: Bounds is the half size)
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, 0.5 * s);
            double level = mode == MarchingCubes.RootfindingModes.LinearEdges ? Sigma * s : 0.01 * s;
            if (pro) return new MarchingCubesPro { Implicit = new Field(scene, shift), Bounds = bounds, CubeSize = h * s, IsoValue = level, RootMode = (MarchingCubesPro.RootfindingModes)(int)mode, ParallelCompute = parallel };
            return new MarchingCubes { Implicit = new Field(scene, shift), Bounds = bounds, CubeSize = h * s, IsoValue = level, RootMode = mode, ParallelCompute = parallel };
        }

        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        /// <summary> "" if the meshes are equal bit for bit (b is a scaled by `scale`), ids in order </summary>
        static string SameMesh(DMesh3 a, DMesh3 b, double scale = 1, bool sorted = false)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"{a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount} vertices/triangles";
            var va = a.Vertices().ToList(); var vb = b.Vertices().ToList();
            if (sorted) { va = va.OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList(); vb = vb.OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToList(); }
            for (int i = 0; i < va.Count; ++i) {
                Vector3d p = va[i], q = vb[i];
                if (Bits(p.x * scale) != Bits(q.x) || Bits(p.y * scale) != Bits(q.y) || Bits(p.z * scale) != Bits(q.z)) return $"vertex {i} differs: {p} * {scale} vs {q}";
            }
            if (!sorted) {
                var ta = a.TriangleIndices().ToList(); var tb = b.TriangleIndices().ToList();
                for (int i = 0; i < ta.Count; ++i) if (a.GetTriangle(ta[i]) != b.GetTriangle(tb[i])) return $"triangle {i} differs";
            } else {
                // (other vertex ids: the triangles as sets of coordinate triples, each starting at its smallest corner, in order)
                string[] Triples(DMesh3 m) => m.TriangleIndices().Select(t => {
                    var c = new[] { m.GetVertex(m.GetTriangle(t).a), m.GetVertex(m.GetTriangle(t).b), m.GetVertex(m.GetTriangle(t).c) }.Select(v => $"{Bits(v.x):x}:{Bits(v.y):x}:{Bits(v.z):x}").ToArray();
                    int k = Array.IndexOf(c, c.OrderBy(x => x, StringComparer.Ordinal).First());
                    return c[k] + "|" + c[(k + 1) % 3] + "|" + c[(k + 2) % 3];
                }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string[] x1 = Triples(a), x2 = Triples(b);
                for (int i = 0; i < x1.Length; ++i) if (x1[i] != x2[i]) return $"triangle {i} (by coordinates) differs";
            }
            return "";
        }

        static string Generate(object mc, out DMesh3 mesh)
        {
            mesh = null;
            try {
                if (mc is MarchingCubesPro pro) { pro.Generate(); mesh = pro.Mesh; }
                else { var plain = (MarchingCubes)mc; plain.Generate(); mesh = plain.Mesh; }
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
            return "";
        }

        static string Property(Scene s, int[] shifts)
        {
            var mode = (MarchingCubes.RootfindingModes)s.Mode;
            bool linear = mode == MarchingCubes.RootfindingModes.LinearEdges;
            string err = Generate(Make(s, 0, false, false, mode), out DMesh3 serial);
            if (err != "") return "serial Generate threw " + err;
            // (the recipe's precondition: every sample on the extraction boundary is high. A torus can reach the box; such scenes
            // keep the determinism and scale checks)
            bool recipe = linear && BoundaryHigh(s);
            if (linear && !recipe) System.Threading.Interlocked.Increment(ref outsideRecipe);
            if (recipe) {
                if (serial.TriangleCount > 0) {
                    if (!serial.IsClosed()) return "LinearEdges: mesh not closed";
                    if (!serial.CheckValidity(false, FailMode.ReturnOnly)) return "LinearEdges: CheckValidity fails";
                }
                if (!Invariance.AllFinite(serial)) return "non-finite vertex";
                string d = Enclosure(s, serial);
                if (d != "") return d;
            }
            err = Generate(Make(s, 0, false, true, mode), out DMesh3 parallel);
            if (err != "") return "parallel Generate threw " + err;
            string diff = SameMesh(serial, parallel);
            if (diff != "") return "parallel differs from serial: " + diff;
            var again = Make(s, 0, false, true, mode);
            Generate(again, out _); err = Generate(again, out DMesh3 second);
            if (err != "") return "second Generate on the same instance threw " + err;
            diff = SameMesh(serial, second);
            if (diff != "") return "second Generate on the same instance differs: " + diff;
            if (linear) {
                err = Generate(Make(s, 0, true, false, mode), out DMesh3 pro);
                if (err != "") return "MarchingCubesPro threw " + err;
                diff = SameMesh(serial, pro, 1, true);
                if (diff != "") return "MarchingCubesPro differs: " + diff;
            }
            foreach (int shift in shifts) {
                err = Generate(Make(s, shift, false, false, mode), out DMesh3 scaled);
                if (err != "") return "scaled by 2^" + shift + " threw " + err;
                diff = SameMesh(serial, scaled, Math.ScaleB(1.0, shift));
                if (diff != "") return "scaled by 2^" + shift + " differs: " + diff;
            }
            return "";
        }

        static int outsideRecipe, enclosureChecks, nonEmptyChecks;

        static bool BoundaryHigh(Scene s)
        {
            var f = new Field(s, 0);
            // (Generate() samples one cell layer more than the box: indices 0 .. 17)
            for (int i = 0; i <= 17; ++i) for (int j = 0; j <= 17; ++j) for (int l = 0; l <= 17; ++l) {
                if (i != 0 && i != 17 && j != 0 && j != 17 && l != 0 && l != 17) continue;
                Vector3d p = new Vector3d(i * H - 0.5, j * H - 0.5, l * H - 0.5);
                if (!(f.Value(ref p) > Sigma)) return false;
            }
            return true;
        }

        /// <summary> the points of {f &lt;= 0} on a lattice (and a shifted one) have winding number above 0.99 </summary>
        static string Enclosure(Scene s, DMesh3 mesh)
        {
            var f = new Field(s, 0);
            var inside = new List<Vector3d>();
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i <= 32; ++i) for (int j = 0; j <= 32; ++j) for (int l = 0; l <= 32; ++l) {
                    double o = k == 0 ? 0 : 1.0 / 128;
                    Vector3d p = new Vector3d(i / 32.0 - 0.5 + o, j / 32.0 - 0.5 + o, l / 32.0 - 0.5 + o);
                    if (f.Value(ref p) <= 0) inside.Add(p);
                }
            if (inside.Count == 0) return "";
            System.Threading.Interlocked.Increment(ref enclosureChecks);
            if (mesh.TriangleCount > 0) System.Threading.Interlocked.Increment(ref nonEmptyChecks);
            if (mesh.TriangleCount == 0) return $"no triangles although {inside.Count} lattice points have f <= 0";
            var tree = new DMeshAABBTree3(mesh, true);
            int step = Math.Max(1, inside.Count / 400);
            for (int i = 0; i < inside.Count; i += step)
            {
                Vector3d p = inside[i];
                double w = tree.WindingNumber(p);
                if (!(w > 0.99)) return $"point {p} (f {f.Value(ref p)}) is not enclosed, winding {w}";
            }
            return "";
        }

        static readonly Gen<Primitive> GenPart =
            Gen.Select(Gen.Select(Gen.Int[0, 3], Gen.Int[-8, 8], Gen.Int[-8, 8], Gen.Int[-8, 8], (k, x, y, z) => (k, x, y, z)),
                       Gen.Select(Gen.Int[6, 14], Gen.Int[3, 8], Gen.Int[0, 12], Gen.Bool, (a, b, an, sub) => (a, b, an, sub)),
                (u, v) => new Primitive { Kind = u.k, Cx = u.x, Cy = u.y, Cz = u.z, A = v.a, B = v.b, Angle = v.an, Subtract = v.sub });

        static readonly Gen<Scene> GenScene =
            Gen.Select(GenPart.Array[1, 3], Gen.Int[0, 3], Gen.Int[8, 20],
                (parts, mode, cells) => {
                    parts[0].Subtract = false;
                    return new Scene { Parts = parts, Mode = mode, Cells = cells };
                });

        static readonly int[] Shifts = { -9, 11 };

        [Fact]
        public void Campaign()
        {
            outsideRecipe = enclosureChecks = nonEmptyChecks = 0;
            CampaignRunner.Run(log, nameof(MarchingCubesCampaignTests), "marchingcubes", GenScene, s => Property(s, Shifts), CampaignRunner.Samples(20), 401u);
            // (not vacuous: with the CI sample count some LinearEdges scene has a point of {f <= 0} and a mesh to enclose it)
            if (CampaignRunner.Samples(20) >= 20) Assert.True(nonEmptyChecks > 0, $"{enclosureChecks} enclosure checks, none with a mesh");
            log.WriteLine($"marchingcubes: {enclosureChecks} enclosure checks ({nonEmptyChecks} with a mesh), {outsideRecipe} LinearEdges scenes reach the extraction boundary (outside the recipe, closed and enclosure checks skipped)");
        }
    }
}
