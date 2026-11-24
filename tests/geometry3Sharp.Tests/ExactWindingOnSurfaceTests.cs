using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact winding number at a point that lies exactly on a triangle of the mesh is the principal value: the mean of the
    /// two sides on a flat face (n + 1/2 on a closed mesh, bit for bit, also on the inner edges and vertices of a flat face),
    /// the solid-angle fraction on a crease or a corner (0.25 on a cube edge, 0.125 at its corner), independent of the
    /// triangle order and of the vertex order in the triangles (bit for bit on flat faces; on creases and corners within the rounding of the sum) and of the directions of the coordinate axes (design note
    /// EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md, "A point exactly on a triangle"). Fixtures are procedural with dyadic
    /// coordinates (every frame below is exact in floating point), so a point is exactly on a triangle by construction.
    /// </summary>
    public class ExactWindingOnSurfaceTests
    {
        readonly ITestOutputHelper Output;
        public ExactWindingOnSurfaceTests(ITestOutputHelper output) { Output = output; }

        // ---- fixtures ----

        // the box [lo, lo + n step]^3 with n x n quads per face, welded, outward oriented; diag shifts the quad diagonals
        static DMesh3 GridBox(Vector3d lo, double step, int n, bool reversed = false, int diag = 0)
        {
            var mesh = new DMesh3();
            var ids = new Dictionary<(double, double, double), int>();
            Func<Vector3d, int> vid = p => {
                var key = (p.x + 0.0, p.y + 0.0, p.z + 0.0);
                if (!ids.TryGetValue(key, out int id)) { id = mesh.AppendVertex(p); ids[key] = id; }
                return id;
            };
            for (int a = 0; a < 3; ++a)
                for (int s = 0; s < 2; ++s) {
                    int u = (a + 1) % 3, v = (a + 2) % 3;
                    Func<int, int, Vector3d> P = (i, j) => {
                        Vector3d q = lo;
                        q[a] = lo[a] + s * n * step; q[u] = lo[u] + i * step; q[v] = lo[v] + j * step;
                        return q;
                    };
                    for (int i = 0; i < n; ++i)
                        for (int j = 0; j < n; ++j) {
                            int q00 = vid(P(i, j)), q10 = vid(P(i + 1, j)), q11 = vid(P(i + 1, j + 1)), q01 = vid(P(i, j + 1));
                            var tris = ((i + j + diag) & 1) == 0
                                ? new[] { (q00, q10, q11), (q00, q11, q01) } : new[] { (q00, q10, q01), (q10, q11, q01) };
                            foreach (var t in tris) {
                                bool flip = (s == 0) != reversed;      // (u, v, a) is positive: the normal of (q00, q10, q11) is +a
                                if (flip) mesh.AppendTriangle(t.Item1, t.Item3, t.Item2); else mesh.AppendTriangle(t.Item1, t.Item2, t.Item3);
                            }
                        }
                }
            return mesh;
        }

        // a flat square sheet [lo, lo + n step]^2 in the plane z = lo.z, counter-clockwise seen from +z
        static DMesh3 Sheet(Vector3d lo, double step, int n, bool reversed = false)
        {
            var mesh = new DMesh3();
            var ids = new int[n + 1, n + 1];
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j)
                    ids[i, j] = mesh.AppendVertex(new Vector3d(lo.x + i * step, lo.y + j * step, lo.z));
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    int[][] t = ((i + j) & 1) == 0 ? new[] { new[] { a, b, c }, new[] { a, c, d } } : new[] { new[] { a, b, d }, new[] { b, c, d } };
                    foreach (int[] tr in t)
                        if (reversed) mesh.AppendTriangle(tr[0], tr[2], tr[1]); else mesh.AppendTriangle(tr[0], tr[1], tr[2]);
                }
            return mesh;
        }

        // the L prism: the polygon (0,0) (4,0) (4,2) (2,2) (2,4) (0,4) extruded by 2 in z; the edge x = y = 2 is reflex
        static DMesh3 LPrism()
        {
            var m = new DMesh3();
            var ids = new Dictionary<(double, double, double), int>();
            Func<Vector3d, int> vid = p => {
                var key = (p.x + 0.0, p.y + 0.0, p.z + 0.0);
                if (!ids.TryGetValue(key, out int id)) { id = m.AppendVertex(p); ids[key] = id; }
                return id;
            };
            Action<Vector3d, Vector3d, Vector3d, Vector3d> tri = (a, b, c, outward) => {
                if ((b - a).Cross(c - a).Dot(outward) >= 0) m.AppendTriangle(vid(a), vid(b), vid(c)); else m.AppendTriangle(vid(a), vid(c), vid(b));
            };
            Vector3d[] poly = { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(4, 2, 0), new Vector3d(2, 2, 0), new Vector3d(2, 4, 0), new Vector3d(0, 4, 0) };
            Vector3d up = new Vector3d(0, 0, 2);
            for (int k = 1; k < 5; ++k) {
                tri(poly[0], poly[k], poly[k + 1], -Vector3d.AxisZ);
                tri(poly[0] + up, poly[k] + up, poly[k + 1] + up, Vector3d.AxisZ);
            }
            Vector3d[] nrm = { -Vector3d.AxisY, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisX, Vector3d.AxisY, -Vector3d.AxisX };
            for (int i = 0; i < 6; ++i) {
                Vector3d a = poly[i], b = poly[(i + 1) % 6];
                tri(a, b, b + up, nrm[i]);
                tri(a, b + up, a + up, nrm[i]);
            }
            return m;
        }

        // the reproducer tetrahedron of the design note
        static DMesh3 Tetra(out Vector3d a, out Vector3d b, out Vector3d c)
        {
            a = new Vector3d(0.25, 0.75, 0.625); b = new Vector3d(-1, -0.5, -1.25); c = new Vector3d(0.75, -1.25, 0.125);
            var e = new Vector3d(0.1, -0.2, 3);
            var m = new DMesh3();
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), ie = m.AppendVertex(e);
            m.AppendTriangle(ia, ic, ib); m.AppendTriangle(ia, ib, ie); m.AppendTriangle(ib, ic, ie); m.AppendTriangle(ic, ia, ie);
            return m;
        }

        static DMesh3 Append(DMesh3 a, DMesh3 b)
        {
            var m = new DMesh3(a);
            MeshEditor.Append(m, b);
            return m;
        }

        static DMesh3 Soup(params Vector3d[] vertices)
        {
            var m = new DMesh3();
            for (int i = 0; i + 2 < vertices.Length; i += 3)
                m.AppendTriangle(m.AppendVertex(vertices[i]), m.AppendVertex(vertices[i + 1]), m.AppendVertex(vertices[i + 2]));
            return m;
        }

        static DMesh3 Map(DMesh3 mesh, Func<Vector3d, Vector3d> f)
        {
            var m = new DMesh3();
            foreach (int v in mesh.VertexIndices()) m.AppendVertex(f(mesh.GetVertex(v)));
            foreach (int t in mesh.TriangleIndices()) { Index3i tr = mesh.GetTriangle(t); m.AppendTriangle(tr.a, tr.b, tr.c); }
            return m;
        }

        // the same mesh with the triangles in another order, each with its corners rotated, vertex ids reversed
        static DMesh3 Reorder(DMesh3 mesh, int seed, bool rotateCorners)
        {
            var m = new DMesh3();
            var vs = mesh.VertexIndices().ToList();
            var map = new Dictionary<int, int>();
            var rnd = new Random(seed);
            var order = vs.OrderBy(v => rnd.Next()).ToList();
            foreach (int v in order) map[v] = m.AppendVertex(mesh.GetVertex(v));
            var ts = mesh.TriangleIndices().OrderBy(t => rnd.Next()).ToList();
            foreach (int t in ts) {
                Index3i tr = mesh.GetTriangle(t);
                if (rotateCorners && rnd.Next(2) == 1) tr = new Index3i(tr.b, tr.c, tr.a);
                else if (rotateCorners) tr = new Index3i(tr.c, tr.a, tr.b);
                m.AppendTriangle(map[tr.a], map[tr.b], map[tr.c]);
            }
            return m;
        }

        // ---- frames: exact maps (dyadic) ----

        static Vector3d Shear(Vector3d p) => new Vector3d(p.x + 0.5 * p.y + 0.25 * p.z, 0.25 * p.x + p.y + 0.5 * p.z, 0.5 * p.x + 0.25 * p.y + p.z);

        // the 24 proper rotations that permute the axes (signed permutation matrices with determinant 1)
        static List<Func<Vector3d, Vector3d>> AxisRotations()
        {
            var list = new List<Func<Vector3d, Vector3d>>();
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 0, 2, 1 }, new[] { 2, 1, 0 }, new[] { 1, 0, 2 } };
            int[] parity = { 1, 1, 1, -1, -1, -1 };
            for (int pi = 0; pi < 6; ++pi)
                for (int sg = 0; sg < 8; ++sg) {
                    int sx = (sg & 1) != 0 ? -1 : 1, sy = (sg & 2) != 0 ? -1 : 1, sz = (sg & 4) != 0 ? -1 : 1;
                    if (parity[pi] * sx * sy * sz != 1) continue;
                    int[] pm = perms[pi];
                    int[] sgn = { sx, sy, sz };
                    list.Add(p => new Vector3d(sgn[0] * p[pm[0]], sgn[1] * p[pm[1]], sgn[2] * p[pm[2]]));
                }
            return list;
        }

        static List<(string, Func<Vector3d, Vector3d>)> Frames()
        {
            var f = new List<(string, Func<Vector3d, Vector3d>)> {
                ("identity", p => p), ("shear", Shear),
                ("far", p => p + new Vector3d(1048576, -2097152, 524288)),
                ("2^-30", p => p * 9.313225746154785e-10), ("2^40", p => p * 1099511627776.0),
                ("shear 2^-30 far", p => Shear(p) * 9.313225746154785e-10 + new Vector3d(1024, -2048, 512)),
            };
            var rots = AxisRotations();
            f.Add(("rot 3", rots[3])); f.Add(("rot 11", rots[11])); f.Add(("rot 17", rots[17])); f.Add(("rot 22", rots[22]));
            return f;
        }

        // ---- evaluation ----

        static double W(DMesh3 m, Vector3d p) => new DMeshAABBTree3(m, true).WindingNumber(p);
        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);
        static double Down(double x) => x == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? -1 : 1));

        // the principal value by the solid-angle sum: a triangle whose plane contains p counts 0, decided by the exact predicate
        static double Principal(DMesh3 mesh, Vector3d p)
        {
            double sum = 0.0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                if (ExactPredicates.Orient3d(a, b, c, p) == 0) continue;
                sum += MathUtil.TriSolidAngle(a, b, c, ref p);
            }
            return sum / MathUtil.FourPI;
        }

        static bool InPlaneOfATriangleAndBox(DMesh3 mesh, Vector3d p)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                if (ExactPredicates.Orient3d(a, b, c, p) != 0) continue;
                var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c);
                if (box.Contains(p)) return true;
            }
            return false;
        }

        // the points of a box mesh [lo, lo + size]^3 on its surface: vertices, edge midpoints, points inside the triangles;
        // kind = the number of coordinates on the box's own planes: 1 flat face, 2 box edge, 3 corner
        static List<(Vector3d, int)> BoxPoints(DMesh3 box, double lo, double size)
        {
            var set = new Dictionary<(double, double, double), Vector3d>();
            Action<Vector3d> add = p => set[(p.x + 0.0, p.y + 0.0, p.z + 0.0)] = p;
            foreach (int v in box.VertexIndices()) add(box.GetVertex(v));
            foreach (int e in box.EdgeIndices()) { Index2i ev = box.GetEdgeV(e); add(0.5 * (box.GetVertex(ev.a) + box.GetVertex(ev.b))); }
            foreach (int t in box.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                box.GetTriVertices(t, ref a, ref b, ref c);
                add(0.25 * (2 * a + b + c)); add(0.25 * (a + 2 * b + c)); add(0.25 * (a + b + 2 * c));
            }
            var res = new List<(Vector3d, int)>();
            foreach (Vector3d p in set.Values) {
                int k = 0;
                for (int i = 0; i < 3; ++i) if (p[i] == lo || p[i] == lo + size) ++k;
                res.Add((p, k));
            }
            return res;
        }

        static double Expected(int kind) => kind == 1 ? 0.5 : (kind == 2 ? 0.25 : 0.125);

        // ---- closed meshes: flat faces, creases, corners ----

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ClosedBox_FlatPointsAreExactlyHalf_CreasesAndCornersAreTheFractions_InEveryFrame(int n)
        {
            double step = 2.0, lo = -n, size = n * step;
            DMesh3 box = GridBox(new Vector3d(lo, lo, lo), step, n);
            List<(Vector3d, int)> pts = BoxPoints(box, lo, size);
            Assert.True(pts.Count(p => p.Item2 == 1) >= 6 * (n * n + 1) && pts.Any(p => p.Item2 == 2) && pts.Count(p => p.Item2 == 3) == 8);
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(box, f);
                var tree = new DMeshAABBTree3(m, true);
                foreach (var (p0, kind) in pts) {
                    Vector3d p = f(p0);
                    Assert.True(InPlaneOfATriangleAndBox(m, p), $"{name}: fixture point {p0}");
                    double w = tree.WindingNumber(p);
                    if (kind == 1) Assert.True(w == 0.5, $"n {n}, {name}, {p0}: {w:R}");
                    else if (name.StartsWith("shear")) Assert.True(Math.Abs(w - Principal(m, p)) <= 1e-12, $"n {n}, {name}, {p0}: {w:R}");
                    else Assert.True(Math.Abs(w - Expected(kind)) <= 1e-12, $"n {n}, {name}, {p0}: {w:R}");
                }
            }
        }

        [Fact]
        public void InsideOutBox_AndCavity_FlatPointsAreExactlyHalfOfTheirSide()
        {
            DMesh3 outer = GridBox(new Vector3d(-4, -4, -4), 4, 2), inner = GridBox(new Vector3d(-1, -1, -1), 1, 2);
            DMesh3 innerRev = GridBox(new Vector3d(-1, -1, -1), 1, 2, true);
            DMesh3 nested = Append(outer, inner), cavity = Append(outer, innerRev);
            DMesh3 rev = GridBox(new Vector3d(-2, -2, -2), 2, 2, true);
            foreach (var (name, f) in Frames()) {
                DMesh3 nestedF = Map(nested, f), cavityF = Map(cavity, f), revF = Map(rev, f);
                var tn = new DMeshAABBTree3(nestedF, true); var tc = new DMeshAABBTree3(cavityF, true); var tr = new DMeshAABBTree3(revF, true);
                foreach (var (p0, kind) in BoxPoints(inner, -1, 2).Where(q => q.Item2 == 1)) {
                    Assert.True(tn.WindingNumber(f(p0)) == 1.5, $"{name}: nested, inner face {p0}");        // inside the outer box, on the inner one
                    Assert.True(tc.WindingNumber(f(p0)) == 0.5, $"{name}: cavity wall {p0}");
                }
                foreach (var (p0, kind) in BoxPoints(outer, -4, 8).Where(q => q.Item2 == 1))
                    Assert.True(tn.WindingNumber(f(p0)) == 0.5 && tc.WindingNumber(f(p0)) == 0.5, $"{name}: outer face {p0}");
                foreach (var (p0, kind) in BoxPoints(rev, -2, 4).Where(q => q.Item2 == 1))
                    Assert.True(tr.WindingNumber(f(p0)) == -0.5, $"{name}: inside-out box {p0}");
            }
        }

        [Fact]
        public void GluedBoxes_OnTheInterface_ValueIsExactlyOne_AndOnTheRimOfTheUnionFlatOrCrease()
        {
            // the interface x = 2 carries the faces of both boxes with opposite orientations and different diagonals
            DMesh3 a = GridBox(new Vector3d(-2, -2, -2), 2, 2), b = GridBox(new Vector3d(2, -2, -2), 2, 2, false, 1);
            DMesh3 glued = Append(a, b);
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(glued, f);
                var tree = new DMeshAABBTree3(m, true);
                int count = 0;
                foreach (var (p0, kind) in BoxPoints(a, -2, 4).Concat(BoxPoints(b, -2, 4).Select(q => q)).ToList()) {
                    if (p0.x != 2.0 || Math.Abs(p0.y) >= 2 || Math.Abs(p0.z) >= 2) continue;
                    Assert.True(tree.WindingNumber(f(p0)) == 1.0, $"{name}: interface {p0}");
                    ++count;
                }
                Assert.True(count >= 9);
                Assert.True(tree.WindingNumber(f(new Vector3d(2, 2, 0))) == 0.5, $"{name}: rim of the interface, in the flat top face");
                Vector3d crease = f(new Vector3d(2, 2, 2));
                Assert.True(Math.Abs(tree.WindingNumber(crease) - (name.StartsWith("shear") ? Principal(m, crease) : 0.25)) <= 1e-12, $"{name}: rim, on a crease");
            }
        }

        [Fact]
        public void Tetrahedron_FaceNextToAnEdge_IsExactlyHalf_AtEveryDistance()
        {
            // the reproducer of the design note: on the old code 5e-15, 1.0, 0.99999, 5e-5, 0.026 for d = 1e-3 ... 1e-16
            DMesh3 tet = Tetra(out Vector3d a, out Vector3d b, out Vector3d c);
            var tree = new DMeshAABBTree3(tet, true);
            var ds = new List<double>();
            for (int k = 3; k <= 40; ++k) ds.Add(Math.Pow(2, -k));
            ds.AddRange(new[] { 1e-3, 1e-10, 1e-12, 1e-13, 1e-16 });
            int checkedCount = 0;
            foreach (double d in ds) {
                double x = -0.21875 + d, y = 0.28125 - d;
                var p = new Vector3d(x, y, x + 0.5 * y);
                if (ExactPredicates.Orient3d(a, b, c, p) != 0) continue;        // (the rounded z of a non-dyadic d may leave the plane)
                ++checkedCount;
                Assert.True(tree.WindingNumber(p) == 0.5, $"d {d:R}: {tree.WindingNumber(p):R}");
            }
            Assert.True(checkedCount >= 38);
        }

        [Fact]
        public void ReflexPrism_FlatPointsExact_ReflexEdgeAndCornersMatchThePrincipalSum()
        {
            DMesh3 l = LPrism();
            (Vector3d, double, bool)[] pts = {
                (new Vector3d(1, 1, 2), 0.5, true), (new Vector3d(3, 0.75, 2), 0.5, true), (new Vector3d(2, 3, 0.75), 0.5, true),
                (new Vector3d(3, 2, 0.75), 0.5, true), (new Vector3d(2, 3, 1), 0.5, true), (new Vector3d(1, 1, 0), 0.5, true),
                (new Vector3d(2, 2, 1), 0.75, false), (new Vector3d(4, 2, 1), 0.25, false), (new Vector3d(0, 0, 1), 0.25, false),
                (new Vector3d(2, 2, 2), 0.375, false), (new Vector3d(2, 2, 0), 0.375, false), (new Vector3d(2, 2, 1.5), 0.75, false),
            };
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(l, f);
                var tree = new DMeshAABBTree3(m, true);
                foreach (var (p0, expected, flat) in pts) {
                    Vector3d p = f(p0);
                    Assert.True(InPlaneOfATriangleAndBox(m, p), $"{name}: fixture point {p0}");
                    double w = tree.WindingNumber(p);
                    if (flat) Assert.True(w == expected, $"{name}, {p0}: {w:R}");
                    else Assert.True((name.StartsWith("shear") || Math.Abs(w - expected) <= 1e-12) && Math.Abs(w - Principal(m, p)) <= 1e-12, $"{name}, {p0}: {w:R} vs {expected}, principal sum {Principal(m, p):R}");
                }
            }
        }

        [Fact]
        public void ShearedCreasesAndCorners_MatchThePrincipalSum()
        {
            DMesh3 box = Map(GridBox(new Vector3d(-2, -2, -2), 2, 2), Shear);
            var tree = new DMeshAABBTree3(box, true);
            int n = 0;
            foreach (var (p0, kind) in BoxPoints(GridBox(new Vector3d(-2, -2, -2), 2, 2), -2, 4).Where(q => q.Item2 >= 2)) {
                Vector3d p = Shear(p0);
                double w = tree.WindingNumber(p), r = Principal(box, p);
                Assert.True(Math.Abs(w - r) <= 1e-12, $"{p0}: {w:R} vs {r:R}");
                Assert.True(w > 0 && w < 0.5);      // convex: below one half (the sheared cube has other solid angles than 1/4, 1/8)
                ++n;
            }
            Assert.True(n >= 20);
        }

        [Fact]
        public void NonManifoldEdge_AndASharedVertex_AreTheSumOfTheBodies()
        {
            // the boxes [-1, 0]^3 and [0, 1] x [0, 1] x [-1, 0] share the edge x = y = 0, z in [-1, 0]; the boxes [-1, 0]^3 and
            // [0, 1]^3 share the vertex (0, 0, 0). The bodies are separate (unwelded): the sum of their fractions.
            DMesh3 edge = Append(GridBox(new Vector3d(-1, -1, -1), 1, 1), GridBox(new Vector3d(0, 0, -1), 1, 1));
            DMesh3 vertex = Append(GridBox(new Vector3d(-1, -1, -1), 1, 1), GridBox(new Vector3d(0, 0, 0), 1, 1));
            foreach (var (name, f) in Frames()) {
                bool shear = name.StartsWith("shear");
                foreach (var (mesh, p0, expected, what) in new[] { (edge, new Vector3d(0, 0, -0.5), 0.5, "non-manifold edge"), (edge, new Vector3d(0, 0, 0), 0.25, "its end vertex"), (vertex, new Vector3d(0, 0, 0), 0.25, "vertex shared by two bodies") }) {
                    DMesh3 m = Map(mesh, f);
                    Vector3d p = f(p0);
                    double w = W(m, p);
                    Assert.True(Math.Abs(w - Principal(m, p)) <= 1e-12, $"{name}: {what}: {w:R}");
                    if (!shear) Assert.True(Math.Abs(w - expected) <= 1e-12, $"{name}: {what}: {w:R}");
                }
            }
        }

        // ---- the flat rule and its coplanar special cases ----

        [Fact]
        public void FlatSheets_DoubledSheetIsZero_SameOrientationDoublesTheValue()
        {
            DMesh3 sheet = Sheet(new Vector3d(-2, -2, 0), 2, 2);
            DMesh3 doubled = Append(sheet, Sheet(new Vector3d(-2, -2, 0), 2, 2, true));
            DMesh3 same = Append(sheet, Sheet(new Vector3d(-2, -2, 0), 2, 2));
            Vector3d[] pts = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(1, 1, 0), new Vector3d(-1, 0.5, 0), new Vector3d(0.5, -1.25, 0), new Vector3d(2, 0, 0), new Vector3d(2, 2, 0) };
            foreach (var (name, f) in Frames()) {
                DMesh3 s1 = Map(sheet, f), s2 = Map(doubled, f), s3 = Map(same, f);
                var t1 = new DMeshAABBTree3(s1, true); var t2 = new DMeshAABBTree3(s2, true); var t3 = new DMeshAABBTree3(s3, true);
                foreach (Vector3d p0 in pts) {
                    Vector3d p = f(p0);
                    double w1 = t1.WindingNumber(p);
                    Assert.True(Math.Abs(w1) <= 1e-11, $"{name}, {p0}: single sheet {w1:R}");            // the principal value of a flat sheet: 0
                    if (Math.Abs(p0.x) < 2 && Math.Abs(p0.y) < 2)
                        Assert.True(t2.WindingNumber(p) == 0.0, $"{name}, {p0}: doubled sheet {t2.WindingNumber(p):R}");     // exactly
                    else
                        Assert.True(Math.Abs(t2.WindingNumber(p)) <= 1e-11, $"{name}, {p0}: doubled sheet at the rim");
                    Assert.True(Math.Abs(t3.WindingNumber(p) - 2 * w1) <= 1e-11, $"{name}, {p0}: same orientation twice {t3.WindingNumber(p):R} vs {w1:R}");
                }
            }
        }

        [Fact]
        public void OverlappingCoplanarTriangles_AndMixedOrientations_AndADegenerateTriangle_AndAFanThatDoesNotClose()
        {
            Vector3d p = new Vector3d(1, 0, 0);
            // p on the edge y = 0 of the first triangle and strictly inside the second
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(4, 0, 0), c = new Vector3d(0, 4, 0);
            Vector3d d = new Vector3d(-1, -1, 0), e = new Vector3d(5, -1, 0), g = new Vector3d(1, 3, 0);
            Assert.True(ExactPredicates.Orient2d(new Vector2d(d.x, d.y), new Vector2d(e.x, e.y), new Vector2d(p.x, p.y)) > 0
                && ExactPredicates.Orient2d(new Vector2d(e.x, e.y), new Vector2d(g.x, g.y), new Vector2d(p.x, p.y)) > 0
                && ExactPredicates.Orient2d(new Vector2d(g.x, g.y), new Vector2d(d.x, d.y), new Vector2d(p.x, p.y)) > 0);
            DMesh3 overlap = Soup(a, b, c, d, e, g), mixed = Soup(a, b, c, d, g, e);
            foreach (DMesh3 m in new[] { overlap, mixed })
                Assert.True(Math.Abs(W(m, p)) <= 1e-11, $"overlapping coplanar triangles: {W(m, p):R}");

            // a zero-area triangle through p is ignored
            DMesh3 withDegenerate = Append(overlap, Soup(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(3, 0, 0)));
            Assert.True(Math.Abs(W(withDegenerate, p) - W(overlap, p)) <= 1e-11);
            DMesh3 degenerateOnly = Soup(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(3, 0, 0));
            Assert.True(Math.Abs(W(degenerateOnly, p)) <= 1e-11);

            // a fan around p over 270 degrees: p is a flat open boundary vertex, the sectors do not cover every direction
            Vector3d o = new Vector3d(0, 0, 0);
            DMesh3 fan = Soup(o, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), o, new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), o, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
            Assert.True(Math.Abs(W(fan, o)) <= 1e-11, $"open fan: {W(fan, o):R}");
            // the fan closed (a hexagon of six triangles): p is an interior vertex of a flat sheet
            DMesh3 hexagon = Soup(o, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), o, new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), o, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0), o, new Vector3d(0, -1, 0), new Vector3d(1, 0, 0));
            Assert.True(Math.Abs(W(hexagon, o)) <= 1e-11, $"closed fan: {W(hexagon, o):R}");
        }

        [Fact]
        public void TJunctionInAFlatSheet_IsZero()
        {
            // U's bottom edge carries the vertex M = (0, 0, 0) of the two triangles below it: p = M is on the interior of an edge of
            // one triangle and at a vertex of two others, all in one plane. The collinear boundary edges (the long edge against
            // its two halves) cancel only in exact arithmetic, so the value is 0 within the bound of the edge angles.
            Vector3d m = new Vector3d(0, 0, 0);
            DMesh3 sheet = Soup(new Vector3d(-2, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0),
                new Vector3d(-2, 0, 0), new Vector3d(0, -2, 0), m, m, new Vector3d(0, -2, 0), new Vector3d(2, 0, 0));
            foreach (var (name, f) in Frames())
                Assert.True(Math.Abs(W(Map(sheet, f), f(m))) <= 1e-11, $"{name}: {W(Map(sheet, f), f(m)):R}");
        }

        // ---- open meshes ----

        static DMesh3 OpenBox()
        {
            // the box [-2, 2]^3 without its +x face
            DMesh3 full = GridBox(new Vector3d(-2, -2, -2), 2, 2);
            var m = new DMesh3();
            foreach (int v in full.VertexIndices()) m.AppendVertex(full.GetVertex(v));
            foreach (int t in full.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                full.GetTriVertices(t, ref a, ref b, ref c);
                if (a.x == 2 && b.x == 2 && c.x == 2) continue;
                Index3i tr = full.GetTriangle(t);
                m.AppendTriangle(tr.a, tr.b, tr.c);
            }
            return m;
        }

        [Fact]
        public void OpenBox_OnTheSurface_EqualsThePrincipalSum_BoundaryEdgeAndVertexAreTheFractions()
        {
            DMesh3 open = OpenBox();
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(open, f);
                var tree = new DMeshAABBTree3(m, true);
                int n = 0;
                foreach (var (p0, kind) in BoxPoints(GridBox(new Vector3d(-2, -2, -2), 2, 2), -2, 4)) {
                    Vector3d p = f(p0);
                    if (!InPlaneOfATriangleAndBox(m, p)) continue;
                    Assert.True(Math.Abs(tree.WindingNumber(p) - Principal(m, p)) <= 2e-12, $"{name}, {p0}: {tree.WindingNumber(p):R} vs {Principal(m, p):R}");
                    ++n;
                }
                Assert.True(n > 60);
            }
            var t0 = new DMeshAABBTree3(open, true);
            Assert.True(Math.Abs(t0.WindingNumber(new Vector3d(2, 2, 0.5)) - 0.25) <= 1e-12, "boundary edge");
            Assert.True(Math.Abs(t0.WindingNumber(new Vector3d(2, 2, 2)) - 0.125) <= 1e-12, "boundary vertex");
        }

        // ---- the soup path (direct sum) ----

        [Fact]
        public void Soup_OnATriangle_IsThePrincipalValue()
        {
            var verts = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0) };
            for (int i = 0; i < 71; ++i) {
                double ox = 10 + 2 * i;
                verts.Add(new Vector3d(ox, 0, 0)); verts.Add(new Vector3d(ox + 1, 0, 0.5)); verts.Add(new Vector3d(ox, 1, 0.25));
            }
            DMesh3 soup = Soup(verts.ToArray());
            var tree = new DMeshAABBTree3(soup, true);
            Assert.True(tree.GetExactWindingCache().UseDirectSum);
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(soup, f);
                var t = new DMeshAABBTree3(m, true);
                foreach (Vector3d p0 in new[] { new Vector3d(1, 1, 0), new Vector3d(2, 2, 0), new Vector3d(2, 0, 0), new Vector3d(0, 0, 0), new Vector3d(1, 0.5, 0) }) {
                    Vector3d p = f(p0);
                    double r = Principal(m, p);
                    Assert.True(Math.Abs(t.WindingNumber(p) - r) <= 1e-13, $"{name}, {p0}: {t.WindingNumber(p):R} vs {r:R}");     // before: off by 0.5 inside the triangle
                }
            }
        }

        // ---- all query paths agree ----

        [Fact]
        public void AllQueryPaths_AgreeOnTheSurface_ThresholdOfOneHalfIsFalseOnAFlatFace_ReflexEdgeIsInside()
        {
            DMesh3 box = GridBox(new Vector3d(-2, -2, -2), 2, 2);
            DMesh3 l = LPrism();
            foreach (var (mesh, label) in new[] { (box, "box"), (l, "prism") }) {
                var tree = new DMeshAABBTree3(mesh, true);
                ExactWindingCache cache = tree.GetExactWindingCache();
                var points = new List<Vector3d>();
                foreach (int v in mesh.VertexIndices()) points.Add(mesh.GetVertex(v));
                foreach (int e in mesh.EdgeIndices()) { Index2i ev = mesh.GetEdgeV(e); points.Add(0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b))); }
                var res = new InsideStatus[points.Count];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, points.ToArray(), res, InsideMethod.ExactWindingNumber));
                for (int i = 0; i < points.Count; ++i) {
                    Vector3d p = points[i];
                    double w = cache.Evaluate(p);
                    Assert.True(Bits(w) == Bits(tree.WindingNumber(p)), $"{label}, {p}");
                    Assert.Equal(w > 0.5, cache.EvaluateAbove(p, 0.5));
                    Assert.True(cache.EvaluateAbove(p, Down(w)) && !cache.EvaluateAbove(p, w));
                    Assert.Equal(Math.Abs(w) > 0.5, cache.EvaluateAbsAbove(p, 0.5));
                    Assert.Equal(w > 0.5 ? InsideStatus.Inside : InsideStatus.Outside, res[i]);
                    if (w == 0.5) Assert.True(!cache.EvaluateAbove(p, 0.5) && cache.EvaluateAbove(p, 0.5 - 1e-15));
                }
            }
            // on a reflex edge the value is 0.75: Inside (a convex edge 0.25 and a face 0.5 are Outside)
            var lt = new DMeshAABBTree3(l, true);
            var r = new InsideStatus[3];
            MeshBatchQueries.IsInsideBatch(lt, new[] { new Vector3d(2, 2, 1), new Vector3d(4, 2, 1), new Vector3d(1, 1, 2) }, r, InsideMethod.ExactWindingNumber);
            Assert.Equal(new[] { InsideStatus.Inside, InsideStatus.Outside, InsideStatus.Outside }, r);
        }

        [Fact]
        public void GridRows_And_MeshWindingNumberGrid_AgreeOnFacesDiagonalsAndInnerVertices()
        {
            // the box [0, 2]^3 with 2 x 2 quads per face, cell 0.5: the origin is -1, so the faces lie on grid nodes
            DMesh3 box = GridBox(new Vector3d(0, 0, 0), 1, 2);
            var tree = new DMeshAABBTree3(box, true);
            ExactWindingCache cache = tree.GetExactWindingCache();
            var g = new MeshWindingNumberGrid(box, tree, 0.5) { ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid };
            g.Compute();
            Vector3d o = g.GridOriginD;
            int onFace = 0;
            for (int i = 0; i < g.Dimensions.x; ++i)
                for (int j = 0; j < g.Dimensions.y; ++j)
                    for (int k = 0; k < g.Dimensions.z; ++k) {
                        Vector3d p = o + new Vector3d(i * 0.5, j * 0.5, k * 0.5);
                        double w = cache.Evaluate(p);
                        Assert.True(g[i, j, k] == (float)w, $"node {i} {j} {k} at {p}: {g[i, j, k]} vs {w:R}");
                        int onPlanes = (p.x == 0 || p.x == 2 ? 1 : 0) + (p.y == 0 || p.y == 2 ? 1 : 0) + (p.z == 0 || p.z == 2 ? 1 : 0);
                        bool inside = p.x >= 0 && p.x <= 2 && p.y >= 0 && p.y <= 2 && p.z >= 0 && p.z <= 2;
                        if (inside && onPlanes == 1) { Assert.True(g[i, j, k] == 0.5f, $"face node {p}"); ++onFace; }
                        if (inside && onPlanes == 2) Assert.True(g[i, j, k] == 0.25f, $"edge node {p}");
                        if (inside && onPlanes == 3) Assert.True(g[i, j, k] == 0.125f, $"corner node {p}");
                    }
            Assert.True(onFace >= 6 * 9);                  // 3 x 3 nodes per face, inner edges, diagonals and the inner vertex among them

            // the row evaluation of the cache itself, along x, in the face planes y = 0, z = 1 and through the inner vertex
            var hits = new List<ExactWindingCache.LineHit>(); var leaves = new List<int>(); var diff = new int[16]; var row = new float[9];
            foreach (var (y, z) in new[] { (0.0, 1.0), (1.0, 2.0), (1.0, 1.0), (0.5, 0.0), (0.0, 0.0) }) {
                cache.EvaluateFullRowX(-1, 0.5, 9, y, z, hits, leaves, diff, row);
                for (int i = 0; i < 9; ++i)
                    Assert.True(row[i] == (float)cache.Evaluate(new Vector3d(-1 + 0.5 * i, y, z)), $"row y {y} z {z} node {i}");
            }
        }

        // ---- independence: order, corners, frames, determinism, reuse ----

        [Fact]
        public void IndependentOfTriangleOrderVertexOrderAndOfTheDirectionsOfTheAxes()
        {
            DMesh3 box = GridBox(new Vector3d(-3, -3, -3), 2, 3);
            DMesh3 l = LPrism();
            foreach (DMesh3 baseMesh in new[] { box, l }) {
                var pts = new List<Vector3d>();
                foreach (int v in baseMesh.VertexIndices()) pts.Add(baseMesh.GetVertex(v));
                foreach (int e in baseMesh.EdgeIndices()) { Index2i ev = baseMesh.GetEdgeV(e); pts.Add(0.5 * (baseMesh.GetVertex(ev.a) + baseMesh.GetVertex(ev.b))); }
                var reference = new double[pts.Count];
                var t0 = new DMeshAABBTree3(baseMesh, true);
                for (int i = 0; i < pts.Count; ++i) reference[i] = t0.WindingNumber(pts[i]);
                for (int seed = 1; seed <= 6; ++seed) {
                    DMesh3 m = Reorder(baseMesh, seed, seed % 2 == 0);
                    var t = new DMeshAABBTree3(m, true);
                    for (int i = 0; i < pts.Count; ++i) {
                        double w = t.WindingNumber(pts[i]);
                        // a flat point (0.5 or the cap/wall values with a half) is bit for bit the same, a crease within the rounding of the sum
                        if (reference[i] == 0.5) Assert.True(w == 0.5, $"order {seed}, {pts[i]}: {w:R}");
                        else Assert.True(Math.Abs(w - reference[i]) <= 1e-13, $"order {seed}, {pts[i]}: {w:R} vs {reference[i]:R}");
                    }
                }
                // the 24 rotations by coordinate permutations and sign changes: the value on the surface is the same (it does not
                // depend on the axes; the perturbed point would pick another side on a face)
                foreach (var rot in AxisRotations()) {
                    DMesh3 m = Map(baseMesh, rot);
                    var t = new DMeshAABBTree3(m, true);
                    for (int i = 0; i < pts.Count; ++i) {
                        double w = t.WindingNumber(rot(pts[i]));
                        if (reference[i] == 0.5) Assert.True(w == 0.5, $"rotation, {pts[i]}: {w:R}");
                        else Assert.True(Math.Abs(w - reference[i]) <= 1e-13, $"rotation, {pts[i]}: {w:R} vs {reference[i]:R}");
                    }
                }
            }
        }

        [Fact]
        public void SimilarityPowersOfTwo_AreBitForBitTheSame()
        {
            DMesh3 box = GridBox(new Vector3d(-2, -2, -2), 2, 2);
            DMesh3 l = LPrism();
            foreach (DMesh3 baseMesh in new[] { box, l }) {
                var pts = new List<Vector3d>();
                foreach (int v in baseMesh.VertexIndices()) pts.Add(baseMesh.GetVertex(v));
                foreach (int e in baseMesh.EdgeIndices()) { Index2i ev = baseMesh.GetEdgeV(e); pts.Add(0.5 * (baseMesh.GetVertex(ev.a) + baseMesh.GetVertex(ev.b))); }
                var t0 = new DMeshAABBTree3(baseMesh, true);
                foreach (Similarity s in Similarity.All().Where(q => q.IsExact)) {
                    var t = new DMeshAABBTree3(Invariance.Transformed(baseMesh, s), true);
                    foreach (Vector3d p in pts)
                        Assert.True(Bits(t.WindingNumber(s.Apply(p))) == Bits(t0.WindingNumber(p)), $"{s.Name}, {p}");
                }
            }
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_RepeatedRuns_Reuse_AndNoPendingStateBetweenQueries()
        {
            DMesh3 box = GridBox(new Vector3d(-3, -3, -3), 2, 3);
            var tree = new DMeshAABBTree3(box, true);
            ExactWindingCache cache = tree.GetExactWindingCache();
            var pts = BoxPoints(box, -3, 6).Select(q => q.Item1).ToList();
            var rnd = new Random(7);
            for (int i = 0; i < 300; ++i) pts.Add(new Vector3d(rnd.NextDouble() * 8 - 4, rnd.NextDouble() * 8 - 4, rnd.NextDouble() * 8 - 4));
            var serial = pts.Select(p => cache.Evaluate(p)).ToArray();
            for (int run = 0; run < 3; ++run) {
                var par = new double[pts.Count];
                Parallel.For(0, pts.Count, i => par[i] = cache.Evaluate(pts[i]));
                for (int i = 0; i < pts.Count; ++i) Assert.True(Bits(par[i]) == Bits(serial[i]), $"run {run}, {pts[i]}");
                var res = new InsideStatus[pts.Count];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts.ToArray(), res, InsideMethod.ExactWindingNumber));
                for (int i = 0; i < pts.Count; ++i) Assert.Equal(serial[i] > 0.5 ? InsideStatus.Inside : InsideStatus.Outside, res[i]);
            }

            // on one thread: a query with pending triangles (an inner vertex of a face, and a vertex on a box edge) followed by an
            // off-surface query and by a row evaluation must not see the triangles of the first
            var fresh = new DMeshAABBTree3(box, true).GetExactWindingCache();
            Vector3d inner = new Vector3d(-1, -1, -3), crease = new Vector3d(-3, -3, -1), off = new Vector3d(0.3, 0.2, -0.1), far = new Vector3d(5, 0.3, 0.2);
            for (int round = 0; round < 3; ++round) {
                Assert.True(cache.Evaluate(inner) == 0.5);
                Assert.True(Bits(cache.Evaluate(off)) == Bits(fresh.Evaluate(off)) && cache.Evaluate(off) == 1.0);
                Assert.True(Math.Abs(cache.Evaluate(crease) - 0.25) <= 1e-12);
                Assert.True(cache.Evaluate(far) == 0.0);
                Assert.True(cache.EvaluateAbove(inner, 0.5) == false && cache.EvaluateAbove(off, 0.5));
                var hits = new List<ExactWindingCache.LineHit>(); var leaves = new List<int>(); var diff = new int[8]; var row = new float[5];
                cache.EvaluateFullRowX(-5, 2.5, 5, 0.3, 0.2, hits, leaves, diff, row);      // y, z off the surface: -5, -2.5, 0, 2.5, 5
                Assert.Equal(new float[] { 0f, 1f, 1f, 1f, 0f }, row);
                cache.EvaluateFullRowX(-3, 1.5, 5, -1, -3, hits, leaves, diff, row);         // the line through the inner vertices of the face z = -3
                Assert.Equal(new float[] { 0.25f, 0.5f, 0.5f, 0.5f, 0.25f }, row);
            }
        }

        // ---- cost at a vertex of high valence, the cap on the number of planes, the order of the rays ----

        // a flat fan of 8 triangles around p in the plane spanned by e1 and e2 (the ring of the 3 x 3 lattice), with its reversed
        // copy (own vertices) if doubled: a doubled fan has the value 0 exactly and its boundary edges cancel
        static DMesh3 Fan(Vector3d p, Vector3d e1, Vector3d e2, bool doubled)
        {
            var m = new DMesh3();
            int[] di = { 1, 1, 0, -1, -1, -1, 0, 1 }, dj = { 0, 1, 1, 1, 0, -1, -1, -1 };
            for (int rep = 0; rep < (doubled ? 2 : 1); ++rep) {
                int c = m.AppendVertex(p);
                var r = new int[8];
                for (int k = 0; k < 8; ++k) r[k] = m.AppendVertex(p + di[k] * e1 + dj[k] * e2);
                for (int k = 0; k < 8; ++k) {
                    if (rep == 0) m.AppendTriangle(c, r[k], r[(k + 1) % 8]); else m.AppendTriangle(c, r[(k + 1) % 8], r[k]);
                }
            }
            return m;
        }

        [Fact]
        public void DrumWithAVertexOfValence20000_AtTheApexAndTheFanCentre_IsFast()
        {
            // A flat fan of k triangles around (0, 0, 1) on top, a cone with the apex (0, 0, -1) below (one plane per triangle at
            // the apex), the rim in the plane z = 1 on a rational parametrisation of the circle (no libm). The resolution of the
            // triangles through the query point was quadratic in k (apex: 3.4 s for k = 20000, fan centre 158 ms); it is linear now
            // (7 and 13 ms). The limit of 2 s for both queries protects against the quadratic code, which needs more than that
            // for the apex alone on a fast machine.
            // (the apex and the fan centre are a copy of the vertex per triangle: DMesh3 finds the edge of a new triangle by a linear
            // search at its vertices, so a welded vertex of valence 20000 costs 7 s to build; the cache keys the boundary edges by
            // their coordinates, so the unwelded drum is closed for it, as an unwelded closed STL soup is, and every triangle still has
            // p on its vertex)
            const int half = 10000;
            var m = new DMesh3();
            var rim = new List<int>();
            for (int j = 0; j < half; ++j) {          // the right half circle from (0, -1) up to (0, 1), the point (0, 1) excluded
                double t = -1.0 + 2.0 * j / half, d = 1 + t * t;
                rim.Add(m.AppendVertex(new Vector3d((1 - t * t) / d, 2 * t / d, 1)));
            }
            for (int j = 0; j < half; ++j) {          // the left half circle from (0, 1) down to (0, -1), the point (0, -1) excluded
                double t = 1.0 - 2.0 * j / half, d = 1 + t * t;
                rim.Add(m.AppendVertex(new Vector3d(-(1 - t * t) / d, 2 * t / d, 1)));
            }
            int k = rim.Count;
            for (int i = 0; i < k; ++i) {
                m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 1)), rim[i], rim[(i + 1) % k]);
                m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, -1)), rim[(i + 1) % k], rim[i]);
            }
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in m.TriangleIndices()) {         // the fan is flat: every corner of a top triangle is in the plane z = 1
                m.GetTriVertices(t, ref a, ref b, ref c);
                if (a.z != 1 || b.z != 1) continue;     // (a bottom triangle has the apex at z = -1)
                Assert.True(ExactPredicates.Orient3d(new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(0, 1, 1), b) == 0 && b.z == 1 && c.z == 1);
            }
            var tree = new DMeshAABBTree3(m, true);
            Assert.True(tree.WindingNumber(new Vector3d(5, 5, 5)) == 0.0);       // (builds the cache)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double wCentre = tree.WindingNumber(new Vector3d(0, 0, 1));
            double wApex = tree.WindingNumber(new Vector3d(0, 0, -1));
            sw.Stop();
            Assert.True(sw.Elapsed.TotalSeconds < 2.0, $"two queries took {sw.Elapsed.TotalSeconds:F2} s");
            Assert.True(wCentre == 0.5, $"fan centre: {wCentre:R}");
            double reference = Principal(m, new Vector3d(0, 0, -1));
            // (the bound: 3 edge angles of 2e-13 / 2 pi for each of the 20000 triangles through the apex, 2e-9; the rounded sum is far closer)
            Assert.True(Math.Abs(wApex - reference) <= 2e-9, $"apex: {wApex:R} vs {reference:R}");
            Assert.True(wApex > 0.0 && wApex < 0.25);
        }

        // the box [-3, 3]^3 (3 x 3 quads per face) with count doubled or single flat fans through (1, 1, 3), each in another plane
        // (all contain the x axis through p: e2 = (0, 1, k)); the top face of the box is the plane number one
        static DMesh3 BoxWithFans(int count, bool doubled)
        {
            DMesh3 m = GridBox(new Vector3d(-3, -3, -3), 2, 3);
            for (int k = 1; k <= count; ++k)
                MeshEditor.Append(m, Fan(new Vector3d(1, 1, 3), new Vector3d(1, 0, 0), new Vector3d(0, 1, k), doubled));
            return m;
        }

        [Fact]
        public void PlaneCap_EightPlanesThroughThePoint_FlatFaceIsExact_NineTakeTheRoundedSum()
        {
            // p = (1, 1, 3) is an inner vertex of the flat top face of the box. The doubled fans through it have the value 0 exactly
            // (cover 0), so with the box's plane and 7 more (8 planes = MaxFlatPlanes) every group is resolved exactly and the
            // value is 0.5 bit for bit; with 8 more (9 planes) no group is formed and every triangle takes the rounded sum, which
            // leaves noise in the last bits on this point (0.4999999999999998 in the identity frame, a platform-dependent
            // rounding, so only the bound is asserted): the principal value within the bound of the edge angles.
            foreach (var (name, f) in Frames().Where(q => q.Item1 == "identity" || q.Item1 == "shear")) {
                Vector3d p = f(new Vector3d(1, 1, 3));
                DMesh3 eight = Map(BoxWithFans(7, true), f), nine = Map(BoxWithFans(8, true), f);
                Assert.True(InPlaneOfATriangleAndBox(eight, p));
                double w8 = new DMeshAABBTree3(eight, true).WindingNumber(p);
                Assert.True(w8 == 0.5, $"{name}: 8 planes {w8:R}");
                double w9 = new DMeshAABBTree3(nine, true).WindingNumber(p);
                Assert.True(Math.Abs(w9 - 0.5) <= 1e-12, $"{name}: 9 planes {w9:R}");
            }
        }

        [Fact]
        public void PlaneCap_SingleFansAndTheBoxFace_AreThePrincipalValue_NoneIsDropped()
        {
            // 8, 9 and 10 planes through p, all but one of them single (open) flat fans whose group is a hemisphere each: with more
            // than MaxFlatPlanes planes every triangle takes the rounded sum and none may be dropped (the value is the principal
            // value, 0.5 from the box's face, 0 from each fan)
            foreach (var (name, f) in Frames().Where(q => q.Item1 == "identity" || q.Item1 == "shear" || q.Item1 == "rot 11")) {
                Vector3d p = f(new Vector3d(1, 1, 3));
                foreach (int count in new[] { 7, 8, 9 }) {
                    DMesh3 m = Map(BoxWithFans(count, false), f);
                    double w = new DMeshAABBTree3(m, true).WindingNumber(p), r = Principal(m, p);
                    Assert.True(Math.Abs(w - r) <= 1e-11, $"{name}, {count} fans: {w:R} vs {r:R}");
                    Assert.True(Math.Abs(w - 0.5) <= 1e-11, $"{name}, {count} fans: {w:R}");
                }
            }
        }

        // a closed prism whose top face is a flat fan around (0, 0, 1): the rim has points on all four half axes (at different
        // distances: (2, 0), (0, 3), (-4, 0), (0, -1)), in every quadrant, and rays that are opposite to each other
        static readonly double[][] StarRim = { new[] { 2.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 3 }, new[] { -1.0, 1 }, new[] { -4.0, 0 }, new[] { -2.0, -2 }, new[] { 0.0, -1 }, new[] { 1.0, -3 } };

        static DMesh3 StarPrism()
        {
            var m = new DMesh3();
            var ids = new Dictionary<(double, double, double), int>();
            Func<Vector3d, int> vid = q => {
                var key = (q.x + 0.0, q.y + 0.0, q.z + 0.0);
                if (!ids.TryGetValue(key, out int id)) { id = m.AppendVertex(q); ids[key] = id; }
                return id;
            };
            Action<Vector3d, Vector3d, Vector3d, Vector3d> tri = (a, b, c, outward) => {
                if ((b - a).Cross(c - a).Dot(outward) >= 0) m.AppendTriangle(vid(a), vid(b), vid(c)); else m.AppendTriangle(vid(a), vid(c), vid(b));
            };
            Vector3d top = new Vector3d(0, 0, 1), bottom = new Vector3d(0, 0, 0), mid = new Vector3d(0, 0, 0.5);
            for (int i = 0; i < 8; ++i) {
                double[] u = StarRim[i], v = StarRim[(i + 1) % 8];
                Vector3d ut = new Vector3d(u[0], u[1], 1), vt = new Vector3d(v[0], v[1], 1), ub = new Vector3d(u[0], u[1], 0), vb = new Vector3d(v[0], v[1], 0);
                tri(top, ut, vt, Vector3d.AxisZ);
                tri(bottom, ub, vb, -Vector3d.AxisZ);
                Vector3d outward = (0.5 * (ut + vt)) - mid;
                tri(ub, vb, vt, outward);
                tri(ub, vt, ut, outward);
            }
            return m;
        }

        [Fact]
        public void FlatFanWithRaysInEveryQuadrantAndOnTheHalfAxes_IsExactlyHalf()
        {
            DMesh3 prism = StarPrism();
            var pts = new List<Vector3d> { new Vector3d(0, 0, 1) };
            foreach (double[] r in StarRim) pts.Add(new Vector3d(0.5 * r[0], 0.5 * r[1], 1));        // the middle of each spoke: p on an edge of the fan
            foreach (var (name, f) in Frames()) {
                DMesh3 m = Map(prism, f);
                var tree = new DMeshAABBTree3(m, true);
                foreach (Vector3d p0 in pts) {
                    Assert.True(InPlaneOfATriangleAndBox(m, f(p0)));
                    double w = tree.WindingNumber(f(p0));
                    Assert.True(w == 0.5, $"{name}, {p0}: {w:R}");
                }
            }
        }

        [Fact]
        public void StartAndEndRaysThatAreNotTheSameMultiset_AreNotConstantCover_ForAllSixOrders()
        {
            // in the plane z = 0 at p = (0, 0, 0): T1 has p on its edge (the upper half plane, 0 to 180 degrees), T3 on its edge
            // (the lower half plane, 180 to 360), T2 at its corner (0 to 63 degrees). The start rays are {0, 0, 180} degrees, the
            // end rays {180, 63, 0}: every start ray is among the end rays (a set-inclusion test would accept), but not with the same
            // multiplicity: the cover is 2 on [0, 63), 1 on [63, 360), not constant, so the group takes the rounded sum and the
            // value is the principal value, 0 for a flat sheet
            Vector3d[] t1 = { new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            Vector3d[] t2 = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 1, 0) };
            Vector3d[] t3 = { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0) };
            var all = new[] { t1, t2, t3 };
            int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (var (name, f) in Frames().Where(q => q.Item1 == "identity" || q.Item1 == "shear" || q.Item1 == "rot 3")) {
                foreach (int[] o in orders) {
                    DMesh3 m = Soup(o.SelectMany(i => all[i]).Select(f).ToArray());
                    double w = W(m, f(Vector3d.Zero));
                    Assert.True(Math.Abs(w) <= 1e-11, $"{name}, order {string.Join("", o)}: {w:R}");
                }
            }
        }

        [Fact]
        public void LayeredFansOfOppositeOrientation_SameRaysAtDifferentDistances_AreZero()
        {
            // a fan around p and a reversed fan of twice the size: the same rays with different lengths, opposite orientations
            Vector3d p = new Vector3d(0, 0, 1);
            DMesh3 m = Append(Fan(p, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), false), Fan(p, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0), true));
            foreach (var (name, f) in Frames()) {
                DMesh3 mm = Map(m, f);
                Assert.True(Math.Abs(W(mm, f(p))) <= 1e-11, $"{name}: {W(mm, f(p)):R}");
            }
        }

        // ---- NarrowBand against FullGrid with nodes exactly on the surface ----

        // Both modes on the same mesh; returns the number of nodes with the value 0.5f (on a flat face of a closed mesh), the number
        // of nodes that differ and the largest difference. NarrowBand computes the nodes within a cell of the mesh, follows crossings
        // of the iso value from node to node (strict comparisons: a node exactly at the iso value is never a crossing) and fills the
        // other nodes along their x row (0 on the outside), so a node whose value moved with the on-surface convention (a node on the
        // surface reads 0.5 or a fraction, no longer the rounded 0 or 1) can change where the front goes. What a user can rely on:
        // (1) wherever the two grids differ, both values are on the same side of the iso value and the node is at least one cell from
        // the mesh; (2) along every x row, between two consecutive nodes whose FullGrid values are on different sides of the iso
        // value (or one equals it) the NarrowBand values at both nodes are bit-equal to FullGrid: the cells that marching cubes turns
        // into surface see the same values.
        (int onHalf, int differ, double maxDiff) CompareNarrowBandAndFullGrid(string name, DMesh3 mesh, double cell)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            var full = new MeshWindingNumberGrid(mesh, tree, cell) { ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid };
            full.Compute();
            var narrow = new MeshWindingNumberGrid(mesh, new DMeshAABBTree3(mesh, true), cell) { ComputeMode = MeshWindingNumberGrid.ComputeModes.NarrowBand };
            narrow.Compute();
            Vector3i dims = full.Dimensions;
            Assert.Equal(dims, narrow.Dimensions);
            float iso = full.WindingIsoValue;
            Vector3d origin = full.GridOriginD;
            double dx = (float)cell;
            int onHalf = 0, differ = 0;
            double maxDiff = 0;
            Func<float, int> side = v => v < iso ? -1 : (v > iso ? 1 : 0);
            for (int k = 0; k < dims.z; ++k)
                for (int j = 0; j < dims.y; ++j)
                    for (int i = 0; i < dims.x; ++i) {
                        float f = full[i, j, k], n = narrow[i, j, k];
                        if (f == 0.5f) ++onHalf;
                        if (f != n) {
                            ++differ;
                            maxDiff = Math.Max(maxDiff, Math.Abs(f - n));
                            Vector3d p = origin + new Vector3d(i * dx, j * dx, k * dx);
                            Assert.True(side(f) == side(n), $"{name}: node {i} {j} {k}: full {f:R}, narrow {n:R} are on different sides of the iso value");
                            int tid = tree.FindNearestTriangle(p);
                            double dist = Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, tid, p));
                            Assert.True(dist >= dx * (1 - 1e-6), $"{name}: node {i} {j} {k} at {p}: full {f:R}, narrow {n:R}, only {dist / dx:F3} cells from the mesh");
                        }
                        if (i + 1 < dims.x) {
                            float f2 = full[i + 1, j, k], n2 = narrow[i + 1, j, k];
                            if (side(f) != side(f2))
                                Assert.True(f == n && f2 == n2, $"{name}: row y {j} z {k}, nodes {i} and {i + 1}: full {f:R} {f2:R}, narrow {n:R} {n2:R}");
                        }
                    }
            return (onHalf, differ, maxDiff);
        }

        [Fact]
        public void MeshWindingNumberGrid_NarrowBandAndFullGrid_AreBitEqualOnClosedBoxesWithNodesOnTheSurface()
        {
            // the box [0, 2]^3 with 2 x 2 quads per face and cell 0.25: the origin is -0.5, the faces lie on grid nodes. Before the
            // value on the surface was the principal value a node on a face read 0 or 1 by rounding (150 of the 294 nodes at 0.5 here).
            // Not asserted here: an open mesh whose 0.5 contour passes exactly through grid nodes (the box without a face, the plane
            // of the opening on a node plane): NarrowBand differs from FullGrid there on master as well, a defect of the front
            // propagation at nodes exactly at the iso value (strict crossing test), fixed separately.
            DMesh3 closed = GridBox(new Vector3d(0, 0, 0), 1, 2);
            var rot = AxisRotations()[11];
            var cases = new List<(string, DMesh3, int)> {
                ("closed box", closed, 294), ("closed box, axes permuted", Map(closed, rot), 294), ("closed box, sheared", Map(closed, Shear), 150),
            };
            foreach (var (name, mesh, expectedOnHalf) in cases) {
                var (onHalf, differ, maxDiff) = CompareNarrowBandAndFullGrid(name, mesh, 0.25);
                Output.WriteLine($"{name}: {onHalf} nodes with the value 0.5, {differ} nodes differ, largest difference {maxDiff:R}");
                Assert.True(onHalf == expectedOnHalf, $"{name}: {onHalf} nodes at 0.5, expected {expectedOnHalf}");
                Assert.True(differ == 0, $"{name}: {differ} nodes differ, largest difference {maxDiff:R}");
            }
        }
    }
}
