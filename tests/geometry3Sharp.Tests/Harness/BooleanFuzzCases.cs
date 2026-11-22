using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary> One generated input of the Boolean fuzzer: two procedural meshes and what the generator knows about them. </summary>
    public sealed class FuzzCase
    {
        public string Family;
        public int Seed;
        /// <summary> how the generator built it (the kinds and parameters it drew), for the report of a failure </summary>
        public string Description = "";
        public DMesh3 A, B;
        /// <summary>
        /// the winding number of the mesh is 0 or 1 everywhere, so the mesh, as an oriented surface, is the boundary of its
        /// solid (false for nested shells of the same orientation and for overlapping parts: winding number 2)
        /// </summary>
        public bool SimpleA = true, SimpleB = true;
        /// <summary>
        /// SimpleA / SimpleB hold only for these exact coordinates (faces of two shells that coincide and cancel): a transform
        /// that rounds makes the shells cross by a unit in the last place, and the winding number leaves 0 and 1
        /// </summary>
        public bool SimpleOnlyExactly;
        /// <summary> an input is open: only the generalized-winding contract applies </summary>
        public bool Open;

        public override string ToString() => $"{Family} #{Seed} ({Description})";

        public FuzzCase With(DMesh3 a, DMesh3 b, string note) =>
            new FuzzCase { Family = Family, Seed = Seed, Description = Description + note, A = a, B = b, SimpleA = SimpleA, SimpleB = SimpleB, SimpleOnlyExactly = SimpleOnlyExactly, Open = Open };

        /// <summary> the two meshes as C# code (coordinates as bit patterns, exact), to start the reduction of a failure by hand </summary>
        public string ToCode()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// " + ToString());
            foreach (var (name, m) in new[] { ("a", A), ("b", B) }) {
                DMesh3 c = new DMesh3(m, true);
                sb.Append("DMesh3 " + name + " = BooleanFuzzShapes.FromBits(new long[] { ");
                sb.Append(string.Join(", ", c.VertexIndices().SelectMany(v => new[] { c.GetVertex(v).x, c.GetVertex(v).y, c.GetVertex(v).z })
                    .Select(x => "0x" + BitConverter.DoubleToInt64Bits(x).ToString("X16", CultureInfo.InvariantCulture))));
                sb.Append(" }, new[] { ");
                sb.Append(string.Join(", ", c.TriangleIndices().SelectMany(t => c.GetTriangle(t).array).Select(i => i.ToString(CultureInfo.InvariantCulture))));
                sb.AppendLine(" });");
            }
            return sb.ToString();
        }
    }

    /// <summary> Mesh builders of the Boolean fuzzer: everything is procedural, nothing is read from a file. </summary>
    public static class BooleanFuzzShapes
    {
        public static DMesh3 FromBits(long[] coordinates, int[] triangles)
        {
            var m = new DMesh3();
            for (int i = 0; i < coordinates.Length; i += 3)
                m.AppendVertex(new Vector3d(BitConverter.Int64BitsToDouble(coordinates[i]), BitConverter.Int64BitsToDouble(coordinates[i + 1]), BitConverter.Int64BitsToDouble(coordinates[i + 2])));
            for (int i = 0; i < triangles.Length; i += 3)
                m.AppendTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
            return m;
        }

        /// <summary> an outward-oriented box of 8 vertices and 12 triangles </summary>
        public static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            var m = new DMesh3();
            for (int k = 0; k < 8; ++k)
                m.AppendVertex(new Vector3d((k & 1) == 0 ? lo.x : hi.x, (k & 2) == 0 ? lo.y : hi.y, (k & 4) == 0 ? lo.z : hi.z));
            int[][] quads = {
                new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        public static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) => Box(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1));

        /// <summary> an outward-oriented tetrahedron, or null if the points are coplanar </summary>
        public static DMesh3 Tet(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            double det = (b - a).Dot((c - a).Cross(d - a));
            if (det == 0) return null;
            if (det < 0) { Vector3d t = b; b = c; c = t; }
            var m = new DMesh3();
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex(d);
            m.AppendTriangle(ia, ic, ib); m.AppendTriangle(ia, ib, id); m.AppendTriangle(ib, ic, id); m.AppendTriangle(ic, ia, id);
            return m;
        }

        public static DMesh3 Map(DMesh3 mesh, Func<Vector3d, Vector3d> f)
        {
            var m = new DMesh3(mesh);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, f(m.GetVertex(v)));
            return m;
        }

        public static DMesh3 Reversed(DMesh3 mesh)
        {
            var m = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices()) map[v] = m.AppendVertex(mesh.GetVertex(v));
            foreach (int t in mesh.TriangleIndices()) { Index3i f = mesh.GetTriangle(t); m.AppendTriangle(map[f.a], map[f.c], map[f.b]); }
            return m;
        }

        /// <summary> the meshes as one mesh (separate vertices) </summary>
        public static DMesh3 Join(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts) {
                var map = new Dictionary<int, int>();
                foreach (int v in p.VertexIndices()) map[v] = m.AppendVertex(p.GetVertex(v));
                foreach (int t in p.TriangleIndices()) { Index3i f = p.GetTriangle(t); m.AppendTriangle(map[f.a], map[f.b], map[f.c]); }
            }
            return m;
        }

        /// <summary> the mesh without some triangles (an open mesh) </summary>
        public static DMesh3 Without(DMesh3 mesh, params int[] triangles)
        {
            var m = new DMesh3(mesh);
            foreach (int t in triangles) m.RemoveTriangle(t, true, false);
            return new DMesh3(m, true);
        }

        /// <summary>
        /// x moved by k units in the last place (k of either sign); a zero moves by k * 2^-52, the unit of the coordinates
        /// around it (its own unit, 2^-1074, is outside the supported range of the rounding, 2^-200 .. 2^200)
        /// </summary>
        public static double Ulps(double x, int k)
        {
            if (k == 0) return x;
            if (x == 0) return k * MathUtil.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x);
            long moved = (bits & long.MaxValue) + k;   // the magnitude grows with k
            return moved < 0 ? -x : BitConverter.Int64BitsToDouble(moved | (bits & long.MinValue));
        }
    }

    /// <summary>
    /// The generators of the Boolean fuzzer (docs/research/EXACT_BOOLEAN_FUZZ_2026-11.md): small solids biased to the cases
    /// where the exact Boolean has to decide ties, each from a family name and a seed. The same seed gives the same case on
    /// every platform (System.Random with a seed, and arithmetic that is exact or correctly rounded; the rotations use
    /// Math.Sin / Math.Cos, whose last bit may differ between runtimes: the checks do not depend on particular coordinates).
    /// </summary>
    public static class BooleanFuzzCases
    {
        public static readonly string[] ClosedFamilies = { "coplanar", "shared", "ulp", "slab", "shells", "generic", "rotated" };
        public const string OpenFamily = "open";

        public static FuzzCase Make(string family, int seed)
        {
            // decorrelate the families and neighbouring seeds
            var r = new Random(unchecked(seed * 7919 + StableHash(family)));
            FuzzCase c = Build(family, r);
            c.Family = family; c.Seed = seed;
            return c;
        }

        static int StableHash(string s) { int h = 17; foreach (char ch in s) h = unchecked(h * 31 + ch); return h; }

        static FuzzCase Build(string family, Random r)
        {
            switch (family) {
                case "coplanar": return Coplanar(r);
                case "shared": return Shared(r);
                case "ulp": return Ulp(r);
                case "slab": return Slab(r);
                case "shells": return Shells(r);
                case "generic": return Generic(r);
                case "rotated": return Rotated(r);
                case "open": return OpenCase(r);
                default: throw new ArgumentException("unknown family " + family);
            }
        }

        static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);
        static T Pick<T>(Random r, params T[] options) => options[r.Next(options.Length)];
        static Vector3d GridPoint(Random r, int range) => new Vector3d(r.Next(range + 1), r.Next(range + 1), r.Next(range + 1));

        // ------------------------------------------------------------------ coplanar faces

        /// <summary>
        /// Two boxes on a dyadic grid; per axis B takes its bounds from a short list around A's, so shared face planes
        /// (overlapping, touching, equal faces) are the rule. Optionally one box is subdivided (other triangles in the shared
        /// planes) and both are sheared by a dyadic matrix (coplanar faces that are not axis-aligned, still exact).
        /// </summary>
        static FuzzCase Coplanar(Random r)
        {
            var lo = new Vector3d(); var hi = new Vector3d(); var blo = new Vector3d(); var bhi = new Vector3d();
            for (int k = 0; k < 3; ++k) {
                double a0 = r.Next(0, 3), a1 = a0 + r.Next(1, 4);
                double b0 = Pick(r, a0 - 0.5, a0, a0, a0 + 0.25, (a0 + a1) / 2, a1 - 0.25, a1);
                double b1 = b0 >= a1 ? a1 + Pick(r, 0.5, 1) : Pick(r, a1 - 0.125, a1, a1, a1 + 0.5);
                if (b1 <= b0) b1 = a1 + 0.5;
                lo[k] = a0; hi[k] = a1; blo[k] = b0; bhi[k] = b1;
            }
            DMesh3 a = BooleanFuzzShapes.Box(lo, hi), b = BooleanFuzzShapes.Box(blo, bhi);
            string note = $"A [{lo}]-[{hi}], B [{blo}]-[{bhi}]";
            int sub = r.Next(4);
            if (sub == 1) { a = BooleanOracle.Subdivided(a, 1); note += ", A subdivided"; }
            if (sub == 2) { b = BooleanOracle.Subdivided(b, 1); note += ", B subdivided"; }
            if (r.Next(3) == 0) {
                double sx = Pick(r, 0.25, 0.5, -0.5), sy = Pick(r, 0, 0.25, -0.75);
                Func<Vector3d, Vector3d> shear = p => new Vector3d(p.x + sx * p.z, p.y + sy * p.z + 0.5 * p.x, p.z);
                a = BooleanFuzzShapes.Map(a, shear); b = BooleanFuzzShapes.Map(b, shear);
                note += $", sheared {F(sx)} {F(sy)}";
            }
            return new FuzzCase { A = a, B = b, Description = note };
        }

        // ------------------------------------------------------------------ shared edges, vertices and faces

        /// <summary>
        /// Two tetrahedra on a coarse integer grid that share a face, an edge or a vertex (the same coordinates), or where a
        /// vertex or an edge of B lies exactly in a face or on an edge of A (dyadic combinations of A's corners).
        /// </summary>
        static FuzzCase Shared(Random r)
        {
            for (int attempt = 0; ; ++attempt) {
                int kind = r.Next(7);
                Vector3d p0 = GridPoint(r, 4), p1 = GridPoint(r, 4), p2 = GridPoint(r, 4), p3 = GridPoint(r, 4);
                Vector3d q1 = GridPoint(r, 4), q2 = GridPoint(r, 4), q3 = GridPoint(r, 4);
                Vector3d onFace = (p0 + p1 + 2 * p2) / 4, onEdge = (p0 + p1) / 2, onFace2 = (2 * p0 + p1 + p2) / 4;
                DMesh3 a = BooleanFuzzShapes.Tet(p0, p1, p2, p3), b;
                string what;
                switch (kind) {
                    case 0: b = BooleanFuzzShapes.Tet(p0, p1, p2, q3); what = "shared face"; break;
                    case 1: b = BooleanFuzzShapes.Tet(p0, p1, q2, q3); what = "shared edge"; break;
                    case 2: b = BooleanFuzzShapes.Tet(p0, q1, q2, q3); what = "shared vertex"; break;
                    case 3: b = BooleanFuzzShapes.Tet(onFace, q1, q2, q3); what = "vertex of B in a face of A"; break;
                    case 4: b = BooleanFuzzShapes.Tet(onEdge, q1, q2, q3); what = "vertex of B on an edge of A"; break;
                    case 5: b = BooleanFuzzShapes.Tet(onFace, onFace2, q2, q3); what = "edge of B in a face of A"; break;
                    default: b = BooleanFuzzShapes.Tet(p2, p3, p0, p1); what = "identical, other vertex order"; break;
                }
                if (a == null || b == null) continue;
                return new FuzzCase { A = a, B = b, Description = $"{what}: A {p0} {p1} {p2} {p3}, B {string.Join(" ", b.Vertices())}" };
            }
        }

        // ------------------------------------------------------------------ near-coincident offsets

        /// <summary>
        /// A coplanar or shared case with coordinates of B (and sometimes A) moved by a few units in the last place: every
        /// coordinate on its own, one axis of all vertices, or one coordinate of one vertex. Faces that coincided cross at
        /// angles of 1e-16, and the crossings need the exact predicates beyond every filter.
        /// </summary>
        static FuzzCase Ulp(Random r)
        {
            FuzzCase c = r.Next(2) == 0 ? Coplanar(r) : Shared(r);
            int mode = r.Next(3), reach = r.Next(1, 4);
            DMesh3 Nudge(DMesh3 m)
            {
                var n = new DMesh3(m);
                int axis = r.Next(3), shift = r.Next(2) == 0 ? 1 : -1, one = r.Next(n.VertexCount);
                foreach (int v in n.VertexIndices()) {
                    Vector3d p = n.GetVertex(v);
                    for (int k = 0; k < 3; ++k) {
                        if (mode == 0 && r.Next(2) == 0) p[k] = BooleanFuzzShapes.Ulps(p[k], r.Next(-reach, reach + 1));
                        else if (mode == 1 && k == axis) p[k] = BooleanFuzzShapes.Ulps(p[k], shift * reach);
                        else if (mode == 2 && v == one && k == axis) p[k] = BooleanFuzzShapes.Ulps(p[k], shift * reach);
                    }
                    n.SetVertex(v, p);
                }
                return n;
            }
            bool both = r.Next(3) == 0;
            DMesh3 b = Nudge(c.B), a = both ? Nudge(c.A) : c.A;
            return new FuzzCase { A = a, B = b, Description = c.Description + $"; ulp mode {mode}, reach {reach}" + (both ? ", both" : "") };
        }

        // ------------------------------------------------------------------ tilted thin slabs

        /// <summary>
        /// A box and a slab of thickness 2^-k (down to a few units in the last place) at or through one of its faces, sheared
        /// by 2^-j per unit or rotated by a tiny angle, so that the slab's faces cross the box's face at a very flat angle.
        /// </summary>
        static FuzzCase Slab(Random r)
        {
            double top = r.Next(1, 4);
            DMesh3 a = BooleanFuzzShapes.Box(0, 0, 0, r.Next(1, 4), r.Next(1, 4), top);
            int k = r.Next(1, 50);
            double t = Math.Pow(2, -k);
            double z0 = Pick(r, top, top - t, top - t / 2, top - 2 * t, top - 0.5);
            bool wide = r.Next(2) == 0;
            DMesh3 b = wide ? BooleanFuzzShapes.Box(-1, -1, z0, 5, 5, z0 + t) : BooleanFuzzShapes.Box(0.25, 0.5, z0, 0.75, 1.5, z0 + t);
            string note = $"box top {F(top)}, slab z {F(z0)} thickness 2^-{k}" + (wide ? ", wide" : ", inside the face");
            int tilt = r.Next(4);
            if (tilt == 1 || tilt == 2) {
                double sx = Math.Pow(2, -r.Next(4, 46)) * Pick(r, 1, -1), sy = tilt == 2 ? Math.Pow(2, -r.Next(4, 46)) : 0;
                b = BooleanFuzzShapes.Map(b, p => new Vector3d(p.x, p.y, p.z + sx * p.x + sy * p.y));
                note += $", sheared {F(sx)} {F(sy)}";
            } else if (tilt == 3) {
                double angle = Math.Pow(2, -r.Next(3, 40));
                Matrix3d rot = Matrix3d.AxisAngleD(new Vector3d(1, 0.5, 0).Normalized, angle * MathUtil.Rad2Deg);
                var pivot = new Vector3d(0.5, 0.5, z0);
                b = BooleanFuzzShapes.Map(b, p => rot * (p - pivot) + pivot);
                note += $", rotated {F(angle)} rad";
            }
            return new FuzzCase { A = a, B = b, Description = note };
        }

        // ------------------------------------------------------------------ nested shells

        /// <summary>
        /// A is a box with a second shell: a cavity (reversed), a cavity with an island, a cavity with a face in the outer
        /// wall, a nested box of the same orientation (winding number 2) or an overlapping box (self-intersecting). B is a
        /// box across the shells, the cavity itself, or a box inside the cavity.
        /// </summary>
        static FuzzCase Shells(Random r)
        {
            DMesh3 outer = BooleanFuzzShapes.Box(0, 0, 0, 4, 4, 4), inner = BooleanFuzzShapes.Box(1, 1, 1, 3, 3, 3);
            int kind = r.Next(5);
            DMesh3 a; bool simple = true; string what;
            switch (kind) {
                case 0: a = BooleanFuzzShapes.Join(outer, BooleanFuzzShapes.Reversed(inner)); what = "cavity"; break;
                case 1: a = BooleanFuzzShapes.Join(outer, BooleanFuzzShapes.Reversed(inner), BooleanFuzzShapes.Box(1.5, 1.5, 1.5, 2.5, 2.5, 2.5)); what = "cavity with an island"; break;
                case 2: a = BooleanFuzzShapes.Join(outer, BooleanFuzzShapes.Reversed(BooleanFuzzShapes.Box(1, 1, 1, 3, 3, 4))); what = "cavity with a face in the wall"; break;
                case 3: a = BooleanFuzzShapes.Join(outer, inner); simple = false; what = "nested, same orientation"; break;
                default: a = BooleanFuzzShapes.Join(outer, BooleanFuzzShapes.Box(2, 1, 1, 5, 3, 3)); simple = false; what = "overlapping parts"; break;
            }
            int bk = r.Next(5);
            DMesh3 b; string bwhat;
            switch (bk) {
                case 0: b = BooleanFuzzShapes.Box(1, 1, 1, 3, 3, 3); bwhat = "the inner box"; break;
                case 1: b = BooleanFuzzShapes.Box(1.5, 1.5, 1.5, 2.5, 2.5, 2.5); bwhat = "inside the inner box"; break;
                case 2: b = BooleanFuzzShapes.Box(-1, 1.5, 1.5, 5, 2.5, 2.5); bwhat = "a bar through all shells"; break;
                case 3: b = BooleanFuzzShapes.Box(2, 2, 2, 5, 5, 5); bwhat = "a corner box"; break;
                default: {
                    double x0 = r.Next(-2, 8) / 2.0, y0 = r.Next(-2, 8) / 2.0, z0 = r.Next(-2, 8) / 2.0;
                    b = BooleanFuzzShapes.Box(x0, y0, z0, x0 + r.Next(1, 8) / 2.0, y0 + r.Next(1, 8) / 2.0, z0 + r.Next(1, 8) / 2.0);
                    bwhat = "a random half-grid box at " + new Vector3d(x0, y0, z0); break;
                }
            }
            bool swap = r.Next(4) == 0;
            var c = new FuzzCase { A = a, B = b, SimpleA = simple, SimpleOnlyExactly = kind == 2, Description = $"A {what}, B {bwhat}" };
            return swap ? new FuzzCase { A = b, B = a, SimpleB = simple, SimpleOnlyExactly = kind == 2, Description = c.Description + ", swapped" } : c;
        }

        // ------------------------------------------------------------------ general position

        static Vector3d Any(Random r) => new Vector3d(4 * r.NextDouble(), 4 * r.NextDouble(), 4 * r.NextDouble());

        static FuzzCase Generic(Random r)
        {
            DMesh3 One()
            {
                if (r.Next(2) == 0) {
                    DMesh3 t = null;
                    while (t == null) t = BooleanFuzzShapes.Tet(Any(r), Any(r), Any(r), Any(r));
                    return t;
                }
                Vector3d c = Any(r);
                Matrix3d rot = Matrix3d.AxisAngleD(Any(r).Normalized, 360 * r.NextDouble());
                Vector3d h = new Vector3d(0.3 + r.NextDouble(), 0.3 + r.NextDouble(), 0.3 + r.NextDouble());
                return BooleanFuzzShapes.Map(BooleanFuzzShapes.Box(-h, h), p => rot * p + c);
            }
            return new FuzzCase { A = One(), B = One(), Description = "random tetrahedra and rotated boxes" };
        }

        // ------------------------------------------------------------------ the degenerate families, rotated

        /// <summary>
        /// A case of another family with both meshes under one random similarity (rotation, scale, translation). The rounding
        /// of the transformed coordinates turns every exact coincidence into a near one, in all directions at once.
        /// </summary>
        static FuzzCase Rotated(Random r)
        {
            string family = Pick(r, "coplanar", "shared", "slab", "shells", "ulp");
            FuzzCase c = Build(family, r);
            Matrix3d rot = Matrix3d.AxisAngleD(Any(r).Normalized, 360 * r.NextDouble());
            double scale = Pick(r, 1, 1, 0.001, 3, 1000);
            Vector3d shift = Pick(r, Vector3d.Zero, new Vector3d(0.1, -0.3, 0.7), new Vector3d(632117.25, 5712043.5, 113.75));
            Func<Vector3d, Vector3d> f = p => scale * (rot * p) + shift;
            FuzzCase t = c.With(BooleanFuzzShapes.Map(c.A, f), BooleanFuzzShapes.Map(c.B, f), $"; {family} rotated, scale {F(scale)}, shift {shift}");
            if (c.SimpleOnlyExactly) t.SimpleA = t.SimpleB = false;
            return t;
        }

        // ------------------------------------------------------------------ open meshes

        /// <summary>
        /// A closed box and an open mesh: a box without a face or without one triangle, a single triangle through the box, or
        /// a sheet of two triangles across it. For the generalized-winding contract only (the result is in general not closed).
        /// </summary>
        static FuzzCase OpenCase(Random r)
        {
            DMesh3 a = BooleanFuzzShapes.Box(0, 0, 0, 2, 2, 2);
            double x0 = r.Next(-2, 4) / 2.0, y0 = r.Next(-2, 4) / 2.0, z0 = r.Next(-2, 4) / 2.0;
            DMesh3 box = BooleanFuzzShapes.Box(x0, y0, z0, x0 + 1.5, y0 + 1.5, z0 + 1.5);
            int kind = r.Next(4);
            DMesh3 b; string what;
            switch (kind) {
                case 0: { int q = 2 * r.Next(6); b = BooleanFuzzShapes.Without(box, q, q + 1); what = $"box at {x0} {y0} {z0} without face {q / 2}"; break; }
                case 1: { int t = r.Next(12); b = BooleanFuzzShapes.Without(box, t); what = $"box at {x0} {y0} {z0} without triangle {t}"; break; }
                case 2: {
                    b = new DMesh3();
                    b.AppendTriangle(b.AppendVertex(new Vector3d(-1, 0.5 + z0 / 4, 1)), b.AppendVertex(new Vector3d(3, 0.5, 0.5)), b.AppendVertex(new Vector3d(1, 3, 1.5 + x0 / 4)));
                    what = "one triangle through the box"; break;
                }
                default: {
                    double z = 1 + z0 / 4;
                    b = new DMesh3();
                    int v0 = b.AppendVertex(new Vector3d(-1, -1, z)), v1 = b.AppendVertex(new Vector3d(3, -1, z)), v2 = b.AppendVertex(new Vector3d(3, 3, z)), v3 = b.AppendVertex(new Vector3d(-1, 3, z));
                    b.AppendTriangle(v0, v1, v2); b.AppendTriangle(v0, v2, v3);
                    what = $"a sheet at z = {F(z)} across the box"; break;
                }
            }
            return new FuzzCase { A = a, B = b, Open = true, Description = "A box, B " + what };
        }
    }
}
