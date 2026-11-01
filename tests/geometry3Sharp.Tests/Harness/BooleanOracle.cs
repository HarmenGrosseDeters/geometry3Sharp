using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// An axis-aligned box with integer corners [X0,X1] x [Y0,Y1] x [Z0,Z1], X0 &lt; X1 etc. An immutable, printable
    /// description of a Boolean operand (docs/design/BOOLEAN_PROPERTY_TESTS.md): meshes are built fresh from it.
    /// </summary>
    public sealed class IntBox
    {
        public readonly int X0, Y0, Z0, X1, Y1, Z1;

        public IntBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            if (x0 >= x1 || y0 >= y1 || z0 >= z1) throw new ArgumentException("empty box");
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }

        public int Lo(int axis) => axis == 0 ? X0 : axis == 1 ? Y0 : Z0;
        public int Hi(int axis) => axis == 0 ? X1 : axis == 1 ? Y1 : Z1;

        /// <summary> strictly inside (the oracle only asks at points off every box plane) </summary>
        public bool Contains(Vector3d p) => X0 < p.x && p.x < X1 && Y0 < p.y && p.y < Y1 && Z0 < p.z && p.z < Z1;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "[{0},{1}]x[{2},{3}]x[{4},{5}]", X0, X1, Y0, Y1, Z0, Z1);
    }

    /// <summary>
    /// Two boxes and the exact map p -> 2^Exponent * p + Translation into world coordinates. With small integer
    /// corners, |Exponent| &lt;= 8 and a translation on a 1/16 grid below 2^12, every world coordinate, every
    /// difference of two and every axis-aligned cross product is exact in double, so the box results can be
    /// checked bit for bit (BoxOracle).
    /// </summary>
    public sealed class BoxCase
    {
        public readonly IntBox A, B;
        public readonly int Exponent;
        public readonly Vector3d Translation;

        public BoxCase(IntBox a, IntBox b, int exponent = 0, Vector3d translation = default(Vector3d))
        {
            A = a; B = b; Exponent = exponent; Translation = translation;
        }

        public double Scale => Math.Pow(2, Exponent);
        public Vector3d ToWorld(Vector3d p) => Scale * p + Translation;
        public Vector3d FromWorld(Vector3d p) => (p - Translation) / Scale;

        public DMesh3 MeshA() => BooleanOracle.BoxMesh(A, this);
        public DMesh3 MeshB() => BooleanOracle.BoxMesh(B, this);

        /// <summary> a rough measure for shrinking checks: the sum of all extents and offsets </summary>
        public int Size => new[] { A, B }.Sum(x => Math.Abs(x.X0) + Math.Abs(x.Y0) + Math.Abs(x.Z0) + x.X1 + x.Y1 + x.Z1)
            + Math.Abs(Exponent) + (Translation == Vector3d.Zero ? 0 : 1);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "A={0} B={1} scale=2^{2} t=({3:R},{4:R},{5:R})",
                A, B, Exponent, Translation.x, Translation.y, Translation.z);
    }

    /// <summary> A tetrahedron with integer corners, positively oriented (the corners are swapped if needed). </summary>
    public sealed class IntTet
    {
        public readonly Vector3i P0, P1, P2, P3;

        public IntTet(Vector3i p0, Vector3i p1, Vector3i p2, Vector3i p3)
        {
            long det = Det(p0, p1, p2, p3);
            if (det == 0) throw new ArgumentException("flat tetrahedron");
            P0 = p0; P1 = det > 0 ? p1 : p2; P2 = det > 0 ? p2 : p1; P3 = p3;
        }

        public static long Det(Vector3i a, Vector3i b, Vector3i c, Vector3i d)
        {
            long bx = b.x - a.x, by = b.y - a.y, bz = b.z - a.z;
            long cx = c.x - a.x, cy = c.y - a.y, cz = c.z - a.z;
            long dx = d.x - a.x, dy = d.y - a.y, dz = d.z - a.z;
            return bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx);
        }

        /// <summary> six times the volume (exact) </summary>
        public long SixVolume => Det(P0, P1, P2, P3);

        public DMesh3 Mesh(Vector3d translation)
        {
            var m = new DMesh3();
            foreach (Vector3i p in new[] { P0, P1, P2, P3 })
                m.AppendVertex(new Vector3d(p.x, p.y, p.z) + translation);
            // outward for det(P0,P1,P2,P3) > 0: each face seen from outside is counter-clockwise
            m.AppendTriangle(0, 2, 1);
            m.AppendTriangle(0, 1, 3);
            m.AppendTriangle(1, 2, 3);
            m.AppendTriangle(0, 3, 2);
            return m;
        }

        static string P(Vector3i p) => string.Format(CultureInfo.InvariantCulture, "({0},{1},{2})", p.x, p.y, p.z);
        public override string ToString() => P(P0) + P(P1) + P(P2) + P(P3);
    }

    /// <summary>
    /// An exact dyadic rational M * 2^E: every finite double is one, and sums, differences and products of them
    /// stay exact. For the oracle's volumes and areas, which have more than 53 bits once a coordinate has many
    /// (eg a sliver vertex at a + 2^-25 (b - a)).
    /// </summary>
    public struct Dyadic : IEquatable<Dyadic>
    {
        public readonly BigInteger M;
        public readonly int E;

        public Dyadic(BigInteger m, int e) { M = m; E = e; }

        public static Dyadic Of(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentException("not finite");
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & ((1L << 52) - 1);
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            return new Dyadic(bits < 0 ? -mant : mant, exp - 1075);
        }

        static BigInteger Align(Dyadic x, int e) => x.M << (x.E - e);
        public static Dyadic operator +(Dyadic x, Dyadic y) { int e = Math.Min(x.E, y.E); return new Dyadic(Align(x, e) + Align(y, e), e); }
        public static Dyadic operator -(Dyadic x, Dyadic y) { int e = Math.Min(x.E, y.E); return new Dyadic(Align(x, e) - Align(y, e), e); }
        public static Dyadic operator *(Dyadic x, Dyadic y) => new Dyadic(x.M * y.M, x.E + y.E);
        public Dyadic Abs() => new Dyadic(BigInteger.Abs(M), E);
        public int Sign => M.Sign;

        public bool Equals(Dyadic other) => (this - other).Sign == 0;
        public override bool Equals(object obj) => obj is Dyadic d && Equals(d);
        public override int GetHashCode() => 0;
        /// <summary> the nearest double, up to an ulp (for messages; compare with Equals) </summary>
        public double ToDouble()
        {
            if (M.IsZero) return 0;
            // (keep about 64 bits of M: a huge M with a very negative E would give infinity times 0)
            int k = Math.Max(0, (int)Math.Ceiling(BigInteger.Log(BigInteger.Abs(M), 2)) - 64);
            return (double)(M >> k) * Math.Pow(2, E + k);
        }
        public override string ToString() => ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Oracles for ExactMeshBoolean results that share no code with it: an analytic reference for two boxes, a
    /// solid-angle winding number, and a resolution-aware comparison of two solids that does not depend on their
    /// triangulation. Every check returns "" when it holds, otherwise the reason.
    /// </summary>
    public static class BooleanOracle
    {
        public static readonly Ops[] AllOps = { Ops.Union, Ops.Intersection, Ops.Difference };

        public static bool Apply(Ops op, bool a, bool b) =>
            op == Ops.Union ? a || b : op == Ops.Intersection ? a && b : a && !b;

        /// <summary> the Boolean under test, with a fresh instance; null with the error if Compute() fails </summary>
        public static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, out string error, bool safeRounding = true)
        {
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safeRounding };
            bool ok = boolean.Compute();
            error = ok && boolean.Status == ExactMeshBoolean.BooleanStatus.Success && boolean.Result != null ? ""
                : "Compute() failed: " + boolean.Status + " " + boolean.Error;
            return error == "" ? boolean.Result : null;
        }

        // ---------------------------------------------------------------- meshes

        public static DMesh3 BoxMesh(IntBox box, BoxCase frame)
        {
            var m = new DMesh3();
            for (int k = 0; k < 8; ++k) {
                var p = new Vector3d((k & 1) == 0 ? box.X0 : box.X1, (k & 2) == 0 ? box.Y0 : box.Y1, (k & 4) == 0 ? box.Z0 : box.Z1);
                m.AppendVertex(frame.ToWorld(p));
            }
            // each quad counter-clockwise seen from outside (corner bit 1 = x, 2 = y, 4 = z)
            int[][] quads = {
                new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 },   // -x, +x
                new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 },   // -y, +y
                new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 } }; // -z, +z
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        /// <summary> six times the signed volume, exactly: the sum of a . (b x c) over the triangles </summary>
        public static Dyadic ExactSixVolume(DMesh3 mesh)
        {
            Dyadic v = Dyadic.Of(0);
            foreach (int t in mesh.TriangleIndices()) {
                Index3i f = mesh.GetTriangle(t);
                Vector3d a = mesh.GetVertex(f.a), b = mesh.GetVertex(f.b), c = mesh.GetVertex(f.c);
                Dyadic ax = Dyadic.Of(a.x), ay = Dyadic.Of(a.y), az = Dyadic.Of(a.z), bx = Dyadic.Of(b.x), by = Dyadic.Of(b.y), bz = Dyadic.Of(b.z);
                Dyadic cx = Dyadic.Of(c.x), cy = Dyadic.Of(c.y), cz = Dyadic.Of(c.z);
                v = v + ax * (by * cz - bz * cy) + ay * (bz * cx - bx * cz) + az * (bx * cy - by * cx);
            }
            return v;
        }

        /// <summary> six times the signed volume in double, relative to the bounds center; absolute: the sum of the absolute terms </summary>
        public static double SixVolume(DMesh3 mesh, out double absolute)
        {
            absolute = 0;
            if (mesh.TriangleCount == 0) return 0;
            Vector3d c = MeshRepairMetrics.TriangleBounds(mesh).Center;
            double v = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, d = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref d);
                double term = (a - c).Dot((b - c).Cross(d - c));
                v += term;
                absolute += Math.Abs(term);
            }
            return v;
        }

        public static double Volume(DMesh3 mesh) => SixVolume(mesh, out _) / 6;

        public static double EdgeLength(DMesh3 mesh) =>
            mesh.EdgeIndices().Sum(e => { Index2i ev = mesh.GetEdgeV(e); return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); });

        /// <summary>
        /// Generalized winding number at p: the sum of the solid angles of all triangles (Van Oosterom and Strackee
        /// 1983) over 4 pi. About 1 inside and 0 outside a closed, outward-oriented mesh; accurate away from it.
        /// </summary>
        public static double Winding(DMesh3 mesh, Vector3d p)
        {
            double sum = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d u = a - p, v = b - p, w = c - p;
                double lu = u.Length, lv = v.Length, lw = w.Length;
                double num = u.Dot(v.Cross(w));
                double den = lu * lv * lw + u.Dot(v) * lw + u.Dot(w) * lv + v.Dot(w) * lu;
                sum += 2 * Math.Atan2(num, den);
            }
            return sum / (4 * Math.PI);
        }

        /// <summary>
        /// Valid DMesh3, finite, closed, manifold, consistently oriented, no inside-out components, no degenerate
        /// triangles (MeshRobustRepair.FindDefects), and no self-intersections other than contact at duplicated
        /// positions: solids that touch along an edge or at a vertex give manifold sheets with copies of the contact
        /// vertices (ExactBooleanByDesignTests), and MeshRepairMetrics counts two triangles that meet at copies with
        /// different ids as intersecting. So each pair it reports is tested again with the vertex ids replaced by
        /// one id per position. allowDegenerate: for inputs that already have sliver triangles, which the result
        /// may keep.
        /// </summary>
        public static string CleanReason(DMesh3 mesh, bool allowDegenerate = false)
        {
            if (mesh == null) return "null mesh";
            if (mesh.TriangleCount == 0) return "";
            if (!mesh.CheckValidity(false, FailMode.ReturnOnly)) return "invalid DMesh3";
            if (!mesh.Vertices().All(v => v.IsFinite)) return "non-finite coordinates";
            var defects = MeshRobustRepair.FindDefects(mesh, false);
            if (allowDegenerate) defects &= ~MeshRobustRepair.Defects.DegenerateTriangles;
            if (defects != MeshRobustRepair.Defects.None) return MeshRobustRepair.DescribeDefects(defects);
            var byPosition = new Dictionary<Vector3d, int>();
            foreach (int v in mesh.VertexIndices())
                if (!byPosition.ContainsKey(mesh.GetVertex(v))) byPosition[mesh.GetVertex(v)] = v;
            Index3i Welded(int t) { Index3i f = mesh.GetTriangle(t); return new Index3i(byPosition[mesh.GetVertex(f.a)], byPosition[mesh.GetVertex(f.b)], byPosition[mesh.GetVertex(f.c)]); }
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(mesh)) {
                Triangle3d x = Triangle(mesh, pair.a), y = Triangle(mesh, pair.b);
                if (ExactTriangleIntersection.IntersectsBeyondShared(Welded(pair.a), ref x, Welded(pair.b), ref y))
                    return $"self-intersections (triangles {pair.a} and {pair.b})";
            }
            return "";
        }

        static Triangle3d Triangle(DMesh3 mesh, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(t, ref a, ref b, ref c);
            return new Triangle3d(a, b, c);
        }

        // ---------------------------------------------------------------- analytic box reference

        static double[][] Grid(BoxCase c)
        {
            var g = new double[3][];
            for (int axis = 0; axis < 3; ++axis)
                g[axis] = new double[] { c.A.Lo(axis), c.A.Hi(axis), c.B.Lo(axis), c.B.Hi(axis) }.Distinct().OrderBy(x => x).ToArray();
            return g;
        }

        public static bool Inside(BoxCase c, Ops op, Vector3d integerPoint) =>
            Apply(op, c.A.Contains(integerPoint), c.B.Contains(integerPoint));

        /// <summary>
        /// The cells of the grid of all box planes with their centers (integer frame) and volumes: each cell is
        /// entirely inside or outside A and B, so the result's solid is the union of the cells the truth table selects.
        /// </summary>
        static IEnumerable<(Vector3d center, double volume)> Cells(BoxCase c)
        {
            double[][] g = Grid(c);
            for (int i = 0; i + 1 < g[0].Length; ++i)
                for (int j = 0; j + 1 < g[1].Length; ++j)
                    for (int k = 0; k + 1 < g[2].Length; ++k)
                        yield return (new Vector3d((g[0][i] + g[0][i + 1]) / 2, (g[1][j] + g[1][j + 1]) / 2, (g[2][k] + g[2][k + 1]) / 2),
                            (g[0][i + 1] - g[0][i]) * (g[1][j + 1] - g[1][j]) * (g[2][k + 1] - g[2][k]));
        }

        /// <summary> the exact volume of the result, in the integer frame </summary>
        public static double ExpectedVolume(BoxCase c, Ops op) => Cells(c).Where(x => Inside(c, op, x.center)).Sum(x => x.volume);

        /// <summary> the exact boundary area of the result in the integer frame: the cell faces between inside and outside </summary>
        public static double ExpectedArea(BoxCase c, Ops op)
        {
            double area = 0;
            foreach (var cell in Cells(c)) {
                if (!Inside(c, op, cell.center)) continue;
                double[][] g = Grid(c);
                for (int axis = 0; axis < 3; ++axis) {
                    int i = Array.IndexOf(g[axis], g[axis].First(x => x > cell.center[axis])) - 1;
                    double face = cell.volume / (g[axis][i + 1] - g[axis][i]);
                    for (int side = 0; side < 2; ++side) {
                        // the center of the neighbour cell (or a point outside the grid)
                        Vector3d n = cell.center;
                        n[axis] = side == 0 ? (i == 0 ? g[axis][0] - 1 : (g[axis][i - 1] + g[axis][i]) / 2)
                                            : (i + 2 == g[axis].Length ? g[axis][i + 1] + 1 : (g[axis][i + 1] + g[axis][i + 2]) / 2);
                        if (!Inside(c, op, n)) area += face;
                    }
                }
            }
            return area;
        }

        // in-plane sample weights of a triangle; at least one avoids the grid lines of a triangle with grid corners
        static readonly double[][] SampleWeights = {
            new[] { 0.29, 0.33, 0.38 }, new[] { 0.61, 0.23, 0.16 }, new[] { 0.17, 0.62, 0.21 }, new[] { 0.13, 0.19, 0.68 },
            new[] { 0.45, 0.41, 0.14 }, new[] { 0.11, 0.47, 0.42 } };

        /// <summary>
        /// The analytic box oracle: the result must be empty if the expected volume is 0; otherwise clean (valid,
        /// closed, manifold, no self-intersections), with exactly the expected volume and area, every triangle on an
        /// axis plane with the expected solid behind and none in front of it (orientation, no misplaced or extra
        /// patch), and a winding number of 1 at the center of every selected cell and 0 at every other one.
        /// </summary>
        public static string CheckBoxResult(DMesh3 result, BoxCase c, Ops op, bool allowDegenerate = false)
        {
            if (result == null) return "null result";
            double s = c.Scale;
            Dyadic s3 = Dyadic.Of(s) * Dyadic.Of(s) * Dyadic.Of(s);
            Dyadic expectedSix = Dyadic.Of(6 * ExpectedVolume(c, op)) * s3;
            if (expectedSix.Sign == 0)
                return result.TriangleCount == 0 ? "" : $"expected an empty result, got {result.TriangleCount} triangles";
            string clean = CleanReason(result, allowDegenerate);
            if (clean != "") return clean;
            if (result.TriangleCount == 0) return "empty result, expected volume " + expectedSix.ToDouble() / 6;
            Dyadic six = ExactSixVolume(result);
            if (!six.Equals(expectedSix)) return $"volume {six.ToDouble() / 6:R}, expected {expectedSix.ToDouble() / 6:R}";

            Dyadic area2 = Dyadic.Of(0), expectedArea2 = Dyadic.Of(2 * ExpectedArea(c, op)) * Dyadic.Of(s) * Dyadic.Of(s);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, d = Vector3d.Zero;
            double[][] g = Grid(c);
            foreach (int t in result.TriangleIndices()) {
                result.GetTriVertices(t, ref a, ref b, ref d);
                // (on an axis plane, the other two components are products with an exact 0)
                Vector3d n = (b - a).Cross(d - a);
                int axis = n.x != 0 ? 0 : n.y != 0 ? 1 : 2;
                if (n[(axis + 1) % 3] != 0 || n[(axis + 2) % 3] != 0 || n[axis] == 0 || a[axis] != b[axis] || a[axis] != d[axis])
                    return $"triangle {t} is not axis-aligned: normal {n}";
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                Dyadic exactN = (Dyadic.Of(b[u]) - Dyadic.Of(a[u])) * (Dyadic.Of(d[v]) - Dyadic.Of(a[v]))
                              - (Dyadic.Of(b[v]) - Dyadic.Of(a[v])) * (Dyadic.Of(d[u]) - Dyadic.Of(a[u]));
                area2 = area2 + exactN.Abs();
                var dir = Vector3d.Zero;
                dir[axis] = Math.Sign(n[axis]);
                bool probed = false;
                foreach (double[] w in SampleWeights) {
                    Vector3d p = c.FromWorld(w[0] * a + w[1] * b + w[2] * d);
                    bool onLine = false;
                    for (int k = 1; k < 3; ++k) {
                        int other = (axis + k) % 3;
                        onLine |= g[other].Any(x => Math.Abs(x - p[other]) < 1e-6);
                    }
                    if (onLine) continue;
                    probed = true;
                    // grid planes are at least 1 apart in the integer frame
                    if (!Inside(c, op, p - 0.25 * dir) || Inside(c, op, p + 0.25 * dir))
                        return $"triangle {t} (normal {n}) is not on the boundary of the expected solid, or reversed";
                }
                // (every sample of a sliver along a grid line can be on it: its area is still counted above)
                double longest = Math.Max((b - a).Length, Math.Max((d - b).Length, (a - d).Length));
                if (!probed && n.Length / longest >= 1e-5 * s)
                    return $"triangle {t} could not be probed";
            }
            if (!area2.Equals(expectedArea2)) return $"area {area2.ToDouble() / 2:R}, expected {expectedArea2.ToDouble() / 2:R}";
            foreach (var cell in Cells(c)) {
                double w = Winding(result, c.ToWorld(cell.center));
                double want = Inside(c, op, cell.center) ? 1 : 0;
                if (Math.Abs(w - want) > 0.25) return $"winding number {w:G4} at cell {cell.center}, expected {want}";
            }
            return "";
        }

        /// <summary>
        /// The regularized solid of a box result whose coordinates are not exact (eg rotated and mapped back): clean,
        /// the expected volume within volumeTolerance, and the expected winding number at the center of every cell
        /// and at the eight points a quarter of its extents off the center (at least 1/4 from every box plane, so far
        /// from the rounding noise). Faces need not be axis-aligned, and the area is not compared: rounded input
        /// coordinates are no longer exactly coplanar, so a coplanar contact can leave slabs or cracks of rounding
        /// thickness, which have area but no volume.
        /// </summary>
        public static string CheckBoxSolid(DMesh3 result, BoxCase c, Ops op, double volumeTolerance)
        {
            string clean = CleanReason(result);
            if (clean != "") return clean;
            double s = c.Scale, expected = ExpectedVolume(c, op) * s * s * s, v = Volume(result);
            if (Math.Abs(v - expected) > volumeTolerance) return $"volume {v:R}, expected {expected:R} (tolerance {volumeTolerance:G3})";
            double[][] g = Grid(c);
            foreach (var cell in Cells(c))
                for (int k = 0; k < 9; ++k) {
                    Vector3d p = cell.center;
                    if (k > 0)
                        for (int axis = 0; axis < 3; ++axis) {
                            double width = g[axis].First(x => x > cell.center[axis]) - g[axis].Last(x => x < cell.center[axis]);
                            p[axis] += ((((k - 1) >> axis) & 1) == 0 ? -0.25 : 0.25) * width;
                        }
                    double want = Inside(c, op, p) ? 1 : 0, w = result.TriangleCount == 0 ? 0 : Winding(result, c.ToWorld(p));
                    if (Math.Abs(w - want) > 0.25) return $"winding number {w:G4} at {p}, expected {want}";
                }
            return "";
        }

        // ---------------------------------------------------------------- comparison of two solids

        /// <summary>
        /// The rounding unit of the coordinates of the meshes: the largest coordinate magnitude times 2^-52.
        /// A result vertex is an exact point rounded to double, so it is off by at most half of it.
        /// </summary>
        public static double Resolution(params DMesh3[] meshes)
        {
            double m = 0;
            foreach (DMesh3 mesh in meshes)
                foreach (Vector3d v in mesh.Vertices())
                    m = Math.Max(m, Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))));
            return m * MathUtil.Epsilon;
        }

        /// <summary>
        /// Volume tolerance for two results of the same exact solid: moving each vertex by up to e (4 rounding
        /// units: the rounding of the exact points, and of the input's coordinates of a transformed copy) changes
        /// the volume by at most e times the area; the signed volume sum itself is off by about n eps times the sum
        /// of its absolute terms. Units: volume.
        /// </summary>
        public static double VolumeTolerance(DMesh3 x, DMesh3 y)
        {
            double e = 4 * Resolution(x, y);
            SixVolume(x, out double ax); SixVolume(y, out double ay);
            int n = x.TriangleCount + y.TriangleCount;
            return e * (Invariance.Area(x) + Invariance.Area(y)) + 4 * n * MathUtil.Epsilon * (ax + ay) / 6;
        }

        /// <summary> Area tolerance: a vertex moved by e changes each incident triangle's area by at most e times its perimeter. </summary>
        public static double AreaTolerance(DMesh3 x, DMesh3 y)
        {
            double e = 4 * Resolution(x, y);
            return 4 * e * (EdgeLength(x) + EdgeLength(y)) + 4 * (x.TriangleCount + y.TriangleCount) * MathUtil.Epsilon * (Invariance.Area(x) + Invariance.Area(y));
        }

        /// <summary>
        /// Whether x and y bound the same solid, independent of their triangulations: volumes and areas agree within
        /// the rounding tolerances, and for every well-shaped triangle of either mesh the other mesh has winding
        /// number 1 just behind and 0 just in front of it (both directions, so a missing or extra patch of either is
        /// found). This is a bounded numerical check, not a proof of equality. size: the extent of the inputs;
        /// </summary>
        public static string SameSolid(DMesh3 x, DMesh3 y, double size)
        {
            if (x == null || y == null) return "null mesh";
            if (x.TriangleCount == 0 || y.TriangleCount == 0)
                return x.TriangleCount == y.TriangleCount ? "" : $"triangle count {x.TriangleCount} vs {y.TriangleCount}";
            double vx = Volume(x), vy = Volume(y), tolV = VolumeTolerance(x, y);
            if (Math.Abs(vx - vy) > tolV) return $"volume {vx:R} vs {vy:R} (tolerance {tolV:G3})";
            double ax = Invariance.Area(x), ay = Invariance.Area(y), tolA = AreaTolerance(x, y);
            if (Math.Abs(ax - ay) > tolA) return $"area {ax:R} vs {ay:R} (tolerance {tolA:G3})";
            string p = Probe(x, y, size, true);
            return p != "" ? p : Probe(y, x, size, true);
        }

        /// <summary>
        /// Whether x bounds the regularized solid of reference, for results of chained Booleans on rounded
        /// intermediate results: A-B and AnB, rounded separately, triangulate their shared faces differently, and with
        /// inexact coordinates the two triangulations leave cracks of rounding thickness, which the union keeps (true
        /// for its rounded inputs). A crack has area and no volume, and the solid on both of its sides. So: the volumes
        /// agree within rounding, every well-shaped triangle of reference has x's solid just behind and none just in
        /// front of it, and every triangle of x has reference's solid just behind it (an extra solid outside the
        /// reference is found, a crack inside it is not).
        /// </summary>
        public static string SameRegularizedSolid(DMesh3 x, DMesh3 reference, double size)
        {
            if (x == null || reference == null) return "null mesh";
            if (x.TriangleCount == 0 || reference.TriangleCount == 0)
                return x.TriangleCount == reference.TriangleCount ? "" : $"triangle count {x.TriangleCount} vs {reference.TriangleCount}";
            double vx = Volume(x), vr = Volume(reference), tolV = VolumeTolerance(x, reference);
            if (Math.Abs(vx - vr) > tolV) return $"volume {vx:R} vs {vr:R} (tolerance {tolV:G3})";
            string p = Probe(reference, x, size, true);
            return p != "" ? p : Probe(x, reference, size, false);
        }

        // a solid of no well-shaped triangle (all of them thinner than the probe distance) is compared by its
        // volume and area only
        static string Probe(DMesh3 from, DMesh3 to, double size, bool front)
        {
            // the probes are delta off the triangle; triangles with an inradius below 4 delta are skipped, so a probe
            // cannot reach through another part of the surface that meets the triangle at an edge
            double delta = 1e-5 * size;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in from.TriangleIndices()) {
                from.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d cross = (b - a).Cross(c - a);
                double perimeter = a.Distance(b) + b.Distance(c) + c.Distance(a);
                if (cross.Length / perimeter < 4 * delta) continue;
                Vector3d n = cross.Normalized, center = (a + b + c) / 3;
                double inside = Winding(to, center - delta * n), outside = Winding(to, center + delta * n);
                if (Math.Abs(inside - 1) > 0.25 || (front && Math.Abs(outside) > 0.25))
                    return $"triangle {t} at {center}: winding {inside:G4} behind and {outside:G4} in front in the other mesh";
            }
            return "";
        }

        // ---------------------------------------------------------------- representation changes

        /// <summary> the same mesh with its triangles in a random order (vertex ids and corners kept) </summary>
        public static DMesh3 TrianglesPermuted(DMesh3 mesh, int seed)
        {
            var rand = new Random(seed);
            var result = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices()) map[v] = result.AppendVertex(mesh.GetVertex(v));
            foreach (int t in mesh.TriangleIndices().OrderBy(t => rand.Next()).ToArray()) {
                Index3i tri = mesh.GetTriangle(t);
                result.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
            }
            return result;
        }

        /// <summary> the same mesh with its vertex ids permuted (triangle order and corners kept) </summary>
        public static DMesh3 VerticesPermuted(DMesh3 mesh, int seed)
        {
            var rand = new Random(seed);
            var result = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices().OrderBy(v => rand.Next()).ToArray()) map[v] = result.AppendVertex(mesh.GetVertex(v));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                result.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
            }
            return result;
        }

        /// <summary> each triangle's corners rotated cyclically by 1 or 2 (orientation kept; never a swap) </summary>
        public static DMesh3 CornersRotated(DMesh3 mesh, int seed)
        {
            var rand = new Random(seed);
            var result = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices()) map[v] = result.AppendVertex(mesh.GetVertex(v));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                int k = 1 + rand.Next(2);
                result.AppendTriangle(map[tri[k]], map[tri[(k + 1) % 3]], map[tri[(k + 2) % 3]]);
            }
            return result;
        }

        /// <summary>
        /// Each triangle split into four at its edge midpoints, one midpoint per undirected edge, so the mesh stays
        /// conforming (no T-junctions). The mesh is rebuilt per level. Midpoints of dyadic coordinates are exact.
        /// </summary>
        public static DMesh3 Subdivided(DMesh3 mesh, int levels)
        {
            for (int level = 0; level < levels; ++level) {
                var result = new DMesh3();
                var map = new Dictionary<int, int>();
                foreach (int v in mesh.VertexIndices()) map[v] = result.AppendVertex(mesh.GetVertex(v));
                var mid = new Dictionary<int, int>();
                int Mid(int u, int w)
                {
                    int e = mesh.FindEdge(u, w);
                    if (!mid.TryGetValue(e, out int m))
                        mid[e] = m = result.AppendVertex(0.5 * (mesh.GetVertex(u) + mesh.GetVertex(w)));
                    return m;
                }
                foreach (int t in mesh.TriangleIndices()) {
                    Index3i f = mesh.GetTriangle(t);
                    int ab = Mid(f.a, f.b), bc = Mid(f.b, f.c), ca = Mid(f.c, f.a);
                    result.AppendTriangle(map[f.a], ab, ca);
                    result.AppendTriangle(ab, map[f.b], bc);
                    result.AppendTriangle(ca, bc, map[f.c]);
                    result.AppendTriangle(ab, bc, ca);
                }
                mesh = result;
            }
            return mesh;
        }

        /// <summary>
        /// Near-degenerate triangles: count distinct edges of the input (chosen by seed) split at a + t (b - a),
        /// t = 2^-tExponent, so the new vertex lies exactly on the edge for dyadic coordinates and the two triangles
        /// at the edge's end a become slivers. Only edges of the input are split: a split of a new edge would give
        /// triangles of area t^2, below the 1e-12 diagonal^2 that MeshRobustRepair counts as degenerate (that case is
        /// ExactBooleanPropertyFindings.SafeRounding_KeepsTheSurfaceOfDegenerateInputTriangles).
        /// </summary>
        public static DMesh3 SliverSplit(DMesh3 mesh, int count, int tExponent, int seed)
        {
            var result = new DMesh3(mesh);
            var rand = new Random(seed);
            double t = Math.Pow(2, -tExponent);
            var edges = mesh.EdgeIndices().Select(e => mesh.GetEdgeV(e)).OrderBy(e => rand.Next()).Take(count).ToArray();
            foreach (Index2i ev in edges) {
                int e = result.FindEdge(ev.a, ev.b);
                Vector3d a = result.GetVertex(ev.a), b = result.GetVertex(ev.b);
                if (result.SplitEdge(e, out DMesh3.EdgeSplitInfo info, 0.5) != MeshResult.Ok) throw new InvalidOperationException("split failed");
                result.SetVertex(info.vNew, a + t * (b - a));
            }
            return new DMesh3(result, true);
        }

        /// <summary>
        /// A representation change must keep the oriented surface: valid, closed, finite, the same signed volume and
        /// area within rounding, the same solid (SameSolid), and an actual change of the triangle list.
        /// </summary>
        public static string CheckRepresentation(DMesh3 original, DMesh3 changed, double size)
        {
            string clean = CleanReason(changed, allowDegenerate: true);
            if (clean != "") return "changed input: " + clean;
            if (Invariance.Difference(original, changed) == "") return "the representation did not change";
            return SameSolid(original, changed, size);
        }

        public enum Representation { TriangleOrder, VertexIds, CornerRotation, Subdivision, Slivers }

        public static DMesh3 Change(DMesh3 mesh, Representation r, int seed) =>
            r == Representation.TriangleOrder ? TrianglesPermuted(mesh, seed)
            : r == Representation.VertexIds ? VerticesPermuted(mesh, seed)
            : r == Representation.CornerRotation ? CornersRotated(mesh, seed)
            : r == Representation.Subdivision ? Subdivided(mesh, 1 + (seed & 1))
            : SliverSplit(mesh, 3, 20 + (seed & 7), seed);
    }
}
