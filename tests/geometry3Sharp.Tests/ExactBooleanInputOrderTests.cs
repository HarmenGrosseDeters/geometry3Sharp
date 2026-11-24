using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact boolean with its default SafeRounding and SolidBooleanPipeline under a reordering of the input (the same
    /// triangles in another order, with their corners rotated, the same vertices with other ids): the result is the same
    /// mesh bit for bit, ids included, and so is the status (#1464, docs/research/BOOLEAN_INPUT_ORDER_2026-11.md). The
    /// inputs are two nearly coincident curved plates, generated here. Before #1464 the kept pieces were the same but the
    /// rounding repair worked through them in the order of the input, and the flat regions were triangulated by a filter
    /// that depends on how a point was computed.
    ///
    /// MeshRoundingRepair itself still depends on the ids of the mesh it is given: the boolean hands it a mesh numbered
    /// by its geometry (Repair_OfOneMesh_DependsOnItsNumbering pins that limit).
    /// </summary>
    public class ExactBooleanInputOrderTests
    {
        const ExactMeshBoolean.Operations Difference = ExactMeshBoolean.Operations.Difference;
        const ExactMeshBoolean.Operations Intersection = ExactMeshBoolean.Operations.Intersection;

        // ---- inputs ----

        static double Bowl(double x, double y) { return 0.3 * ((x - 0.5) * (x - 0.5) + (y - 0.5) * (y - 0.5)); }
        static double Wave(double x, double y) { return 0.05 * Math.Sin(3 * x + 1) * Math.Cos(2 * y); }
        static double Dome(double x, double y) { return Math.Sqrt(4 - (x - 0.5) * (x - 0.5) - (y - 0.5) * (y - 0.5)); }

        static readonly Vector3d Center = new Vector3d(0.5, 0.5, 0);

        /// <summary>
        /// A curved plate: the surface z = h(x, y) over the unit square on an n by n grid, the thickness t along z, closed
        /// by side walls; map moves its vertices.
        /// </summary>
        static DMesh3 Plate(int n, double t, Func<double, double, double> h, Func<Vector3d, Vector3d> map = null)
        {
            var m = new DMesh3();
            int N = n + 1;
            var top = new int[N, N];
            var bot = new int[N, N];
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j) {
                    double x = (double)i / n, y = (double)j / n, z = h(x, y);
                    Vector3d a = new Vector3d(x, y, z + t / 2), b = new Vector3d(x, y, z - t / 2);
                    top[i, j] = m.AppendVertex(map != null ? map(a) : a);
                    bot[i, j] = m.AppendVertex(map != null ? map(b) : b);
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]);
                    m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                    m.AppendTriangle(bot[i, j], bot[i + 1, j + 1], bot[i + 1, j]);
                    m.AppendTriangle(bot[i, j], bot[i, j + 1], bot[i + 1, j + 1]);
                }
            Action<int, int, int, int> wall = (a0, a1, b0, b1) => { m.AppendTriangle(a0, b0, b1); m.AppendTriangle(a0, b1, a1); };
            for (int i = 0; i < n; ++i) {
                wall(top[i, 0], top[i + 1, 0], bot[i, 0], bot[i + 1, 0]);
                wall(top[i + 1, n], top[i, n], bot[i + 1, n], bot[i, n]);
                wall(top[0, i + 1], top[0, i], bot[0, i + 1], bot[0, i]);
                wall(top[n, i], top[n, i + 1], bot[n, i], bot[n, i + 1]);
            }
            return m;
        }

        // a rotation by ax about the x axis, then by ay about the y axis, about Center
        static Vector3d Rotated(Vector3d p, double ax, double ay)
        {
            Vector3d q = p - Center;
            double co = Math.Cos(ax), si = Math.Sin(ax);
            q = new Vector3d(q.x, co * q.y - si * q.z, si * q.y + co * q.z);
            co = Math.Cos(ay);
            si = Math.Sin(ay);
            q = new Vector3d(co * q.x + si * q.z, q.y, -si * q.x + co * q.z);
            return Center + q;
        }

        sealed class Case
        {
            public string Name;
            public DMesh3 A, B;
            public ExactMeshBoolean.Operations Op;
        }

        // a plate minus its copy shrunk by 1e-6 about Center: a skin of 1e-6 of the plate's size on its outer side
        static Case ShrunkCopy()
        {
            return new Case { Name = "bowl minus its copy shrunk by 1e-6", Op = Difference,
                A = Plate(6, 0.02, Bowl), B = Plate(6, 0.02, Bowl, p => Center + (1 - 1e-6) * (p - Center)) };
        }

        // a plate and its copy turned by 1e-6 and lifted by nearly its thickness: the intersection is a curved strip
        static Case ThinStrip()
        {
            const double e = 1e-6, t = 0.02;
            return new Case { Name = "thin curved strip", Op = Intersection,
                A = Plate(6, t, Dome), B = Plate(6, t, Dome, p => Rotated(p, e, 0.7 * e) + new Vector3d(0, 0, t * (1 - 16 * e))) };
        }

        // two tessellations of one plate, one turned by 1e-7.75: skins between the two tessellations
        static Case TwoTessellations()
        {
            double e = Math.Pow(10, -7.75);
            return new Case { Name = "two tessellations, one turned", Op = Difference,
                A = Plate(8, 0.02, Dome, p => Rotated(p, e, 0.7 * e)), B = Plate(6, 0.02, Dome) };
        }

        // a plate minus its copy turned by 1e-6: flat regions of the result have points on a circle
        static Case TurnedCopy()
        {
            const double e = 1e-6;
            return new Case { Name = "wave minus its copy turned by 1e-6", Op = Difference,
                A = Plate(10, 0.02, Wave), B = Plate(10, 0.02, Wave, p => Rotated(p, e, 0.7 * e)) };
        }

        // ---- reorderings ----

        /// <summary>
        /// The orders of the triangles and of the vertex ids (0 as given, -1 reversed, any other value the seed of a
        /// shuffle), and the seed of a rotation of each triangle's corners (0: as given).
        /// </summary>
        static readonly int[][] Orders = {
            new[] { 0, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, -1, 0 }, new[] { -1, -1, 0 }, new[] { 0, 0, 5 },
            new[] { 249, 111, 0 }, new[] { 467, 771, 3 }, new[] { 12, 34, 56 }
        };

        static ulong Next(ref ulong s)
        {
            s = s * 6364136223846793005UL + 1442695040888963407UL;
            return s >> 33;
        }

        static int[] Ordered(IEnumerable<int> ids, int order)
        {
            int[] a = ids.ToArray();
            if (order == -1)
                Array.Reverse(a);
            else if (order != 0) {
                // (a Fisher-Yates shuffle with a generator of its own: the same on every runtime)
                ulong s = (ulong)order * 0x9E3779B97F4A7C15UL + 1;
                for (int i = a.Length - 1; i > 0; --i) {
                    int j = (int)(Next(ref s) % (ulong)(i + 1));
                    int tmp = a[i]; a[i] = a[j]; a[j] = tmp;
                }
            }
            return a;
        }

        /// <summary> the same mesh with its triangles and its vertex ids in another order and the corners of each triangle rotated </summary>
        static DMesh3 Reordered(DMesh3 mesh, int[] order)
        {
            var map = new Dictionary<int, int>();
            var r = new DMesh3();
            foreach (int v in Ordered(mesh.VertexIndices(), order[1]))
                map[v] = r.AppendVertex(mesh.GetVertex(v));
            ulong s = (ulong)order[2] * 0x9E3779B97F4A7C15UL + 1;
            foreach (int t in Ordered(mesh.TriangleIndices(), order[0])) {
                Index3i f = mesh.GetTriangle(t);
                int k = order[2] == 0 ? 0 : (int)(Next(ref s) % 3);
                Assert.True(r.AppendTriangle(map[f[k]], map[f[(k + 1) % 3]], map[f[(k + 2) % 3]]) >= 0);
            }
            return r;
        }

        // ---- what is compared ----

        static int Compare(Vector3d a, Vector3d b)
        {
            int c = a.x.CompareTo(b.x);
            if (c != 0) return c;
            c = a.y.CompareTo(b.y);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        static string Bits(Vector3d p)
        {
            return BitConverter.DoubleToInt64Bits(p.x).ToString("x16") + BitConverter.DoubleToInt64Bits(p.y).ToString("x16") + BitConverter.DoubleToInt64Bits(p.z).ToString("x16");
        }

        /// <summary>
        /// The triangles of a mesh as a set of positions (bits): each with its corners rotated to start at the smallest
        /// position, the triangles sorted. Equal for two meshes exactly if they have the same oriented triangles, whatever
        /// their ids.
        /// </summary>
        static string TriangleSet(DMesh3 m)
        {
            if (m == null)
                return "(no result)";
            var keys = new List<string>();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                Vector3d[] p = { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
                int k = 0;
                for (int i = 1; i < 3; ++i)
                    if (Compare(p[i], p[k]) < 0) k = i;
                keys.Add(Bits(p[k]) + Bits(p[(k + 1) % 3]) + Bits(p[(k + 2) % 3]));
            }
            keys.Sort(string.CompareOrdinal);
            return string.Join("\n", keys);
        }

        static ExactMeshBoolean Exact(Case c, int[] order, bool simplify = true)
        {
            var b = new ExactMeshBoolean(Reordered(c.A, order), Reordered(c.B, order), c.Op) { SafeRounding = false, SimplifyFlatRegions = simplify };
            Assert.True(b.Compute(), c.Name + ": " + b.Error);
            return b;
        }

        static ExactMeshBoolean Rounded(Case c, int[] order)
        {
            var b = new ExactMeshBoolean(Reordered(c.A, order), Reordered(c.B, order), c.Op);
            Assert.True(b.Compute(), c.Name + ": " + b.Error);
            return b;
        }

        static string Counts(MeshRoundingRepair r)
        {
            return r.InitialIntersections + " " + r.InitialDegenerate + " " + r.InitialFalseContacts + " -> " + r.RemainingIntersections + " " + r.RemainingDegenerate + " "
                + r.RemainingFalseContacts + " | " + r.Collapses + " " + r.Flips + " " + r.Moves + " " + r.Zips + " " + r.StripZips + " " + r.PositionSearchMoves + " " + r.RemovedComponents;
        }

        /// <summary> the mesh with its ids: every vertex id with its coordinates (bits), every triangle id with its corners in order, every edge id with its vertices </summary>
        static string Mesh(DMesh3 m)
        {
            if (m == null)
                return "(no result)";
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append('/').Append(m.MaxTriangleID).Append('\n');
            foreach (int v in m.VertexIndices())
                sb.Append(v).Append(':').Append(Bits(m.GetVertex(v))).Append('\n');
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append('\n');
            }
            // (the repair also works by edge ids)
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                sb.Append(e).Append('=').Append(ev.a).Append(',').Append(ev.b).Append('\n');
            }
            return sb.ToString();
        }

        static string Order(int[] order) { return "order " + order[0] + ", " + order[1] + ", " + order[2]; }

        public static IEnumerable<object[]> CaseNames()
        {
            return new[] { new object[] { "shrunk" }, new object[] { "strip" }, new object[] { "tessellations" }, new object[] { "turned" } };
        }

        static Case Named(string name)
        {
            return name == "shrunk" ? ShrunkCopy() : name == "strip" ? ThinStrip() : name == "tessellations" ? TwoTessellations() : TurnedCopy();
        }

        // ---- the exact stages ----

        /// <summary>
        /// The kept pieces of the arrangement (no simplification of flat regions, no repair) and the exact result are the
        /// same triangles for every order of the input.
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ExactResult_HasTheSameTriangles_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            string pieces = TriangleSet(Exact(c, Orders[0], simplify: false).Result), exact = TriangleSet(Exact(c, Orders[0]).Result);
            Assert.NotEqual(pieces, exact);     // (the simplification removes vertices in these cases)
            foreach (int[] order in Orders.Skip(1)) {
                Assert.True(pieces == TriangleSet(Exact(c, order, simplify: false).Result), name + ": the pieces differ for " + Order(order));
                Assert.True(exact == TriangleSet(Exact(c, order).Result), name + ": the exact result differs for " + Order(order));
            }
        }

        /// <summary>
        /// The exact result without the repair is the same mesh, ids included, for every order: the pieces are ordered by
        /// their geometry before the vertices are numbered (ExactMeshBoolean.order_pieces). "turned" has flat regions whose
        /// points lie on a circle: before, their flips towards Delaunay were decided by the floating-point filter alone,
        /// whose verdict depends on how a point was computed, and reversed vertex ids gave other diagonals.
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ExactResult_IsBitIdentical_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            ExactMeshBoolean first = Exact(c, Orders[0]);
            Assert.True(first.RemovedVertices > 0);
            string mesh = Mesh(first.Result);
            foreach (int[] order in Orders.Skip(1)) {
                ExactMeshBoolean b = Exact(c, order);
                Assert.Equal(first.RemovedVertices, b.RemovedVertices);
                Assert.True(mesh == Mesh(b.Result), name + ": another exact result for " + Order(order));
            }
        }

        // ---- the default mode and the pipeline ----

        /// <summary>
        /// The default result (SafeRounding) is the same mesh bit for bit, ids included, for every order of the input, with
        /// the same verdict and the same edits of the repair; where it is clean it is a closed, clean surface. In all four
        /// cases the repair edits the result (before #1464: several results per case, volumes that differ in the fourth
        /// digit, and for "tessellations" a verdict that depends on the order).
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void DefaultResult_IsBitIdentical_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            ExactMeshBoolean first = Rounded(c, Orders[0]);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, first.Status);
            Assert.True(first.Rounding.InitialDegenerate > 0 && first.Rounding.Collapses > 0, name + ": the repair has nothing to do (the test would show nothing)");
            ResultChecks.AssertValid(first.Result);
            ResultChecks.AssertClosed(first.Result);
            if (first.RoundingClean)
                Assert.Equal("", BooleanOracle.CleanReason(first.Result));
            string mesh = Mesh(first.Result), counts = Counts(first.Rounding);
            foreach (int[] order in Orders.Skip(1)) {
                ExactMeshBoolean b = Rounded(c, order);
                Assert.Equal(first.Status, b.Status);
                Assert.Equal(first.RoundingClean, b.RoundingClean);
                Assert.Equal(counts, Counts(b.Rounding));
                Assert.Equal(first.Contacts, b.Contacts);
                Assert.True(mesh == Mesh(b.Result), name + ": another result for " + Order(order));
            }
        }

        /// <summary>
        /// SolidBooleanPipeline gives the same status and the same mesh for every order of the input: here a closed, clean
        /// result in all four cases. Before #1464 "tessellations" succeeded in some orders and failed in others (a degenerate
        /// triangle that the repair cannot remove from some states, #1465).
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Pipeline_IsTheSame_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            SolidBooleanPipeline first = null;
            foreach (int[] order in Orders) {
                var pipe = new SolidBooleanPipeline(Reordered(c.A, order), Reordered(c.B, order), c.Op);
                bool ok = pipe.Compute();
                if (first == null) {
                    first = pipe;
                    Assert.True(ok, name + ": " + pipe.Error);
                    Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipe.Status);
                    ResultChecks.AssertClosed(pipe.Result);
                    Assert.Equal("", BooleanOracle.CleanReason(pipe.Result));
                    continue;
                }
                Assert.Equal(first.Status, pipe.Status);
                Assert.Equal(first.BooleanStatus, pipe.BooleanStatus);
                Assert.Equal(first.Report.NotCleanReason, pipe.Report.NotCleanReason);
                Assert.True(Mesh(first.Result) == Mesh(pipe.Result), name + ": another result for " + Order(order));
            }
        }

        /// <summary> a second Compute() on the same instance gives the same mesh </summary>
        [Fact]
        public void DefaultResult_IsTheSame_ForASecondCompute()
        {
            Case c = ThinStrip();
            var b = new ExactMeshBoolean(c.A, c.B, c.Op);
            Assert.True(b.Compute());
            string mesh = Mesh(b.Result);
            Assert.True(b.Compute());
            Assert.Equal(mesh, Mesh(b.Result));
        }

        /// <summary>
        /// Under Similarity.All(): the transformed inputs in two orders give the same mesh bit for bit (the order of the
        /// pieces is an exact comparison of points, at every scale and place), valid and, unless empty, closed. The 2^k scales give the
        /// mesh of the untransformed inputs, scaled, bit for bit and with the same ids.
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void DefaultResult_IsBitIdentical_ForReorderedInputs_UnderSimilarities(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            foreach (Case c in new[] { ShrunkCopy(), ThinStrip() }) {
                var moved = new Case { Name = c.Name, Op = c.Op, A = Invariance.Transformed(c.A, s), B = Invariance.Transformed(c.B, s) };
                ExactMeshBoolean first = Rounded(moved, Orders[0]);
                ResultChecks.AssertValid(first.Result);
                // (far from the origin the coordinates do not resolve the skin: the result may be empty)
                if (first.Result.TriangleCount > 0)
                    ResultChecks.AssertClosed(first.Result);
                foreach (int[] order in new[] { Orders[3], Orders[7] }) {
                    ExactMeshBoolean b = Rounded(moved, order);
                    Assert.Equal(first.RoundingClean, b.RoundingClean);
                    Assert.True(Mesh(first.Result) == Mesh(b.Result), c.Name + ", " + s.Name + ": another result for " + Order(order));
                }
                if (s.IsExact) {
                    ExactMeshBoolean plain = Rounded(c, Orders[0]);
                    Assert.Equal(plain.RoundingClean, first.RoundingClean);
                    Assert.True(Mesh(plain.Result) == Mesh(Invariance.MappedBack(first.Result, s)), c.Name + ", " + s.Name + ": not the scaled result");
                }
            }
        }

        // ---- the simplification of flat regions on its own ----

        // a fan around the origin in the plane z = 0 whose rim has 8 points on one circle (x^2 + y^2 = 25); ExactPlanarSimplify
        // on its triangles, the list starting at triangle `first` and each triangle's corners rotated by `turn`: the
        // triangles left after the hub is removed, as sorted point triples
        static string SimplifiedFan(int first, int turn)
        {
            int[][] rim = { new[] { 5, 0 }, new[] { 4, 3 }, new[] { 3, 4 }, new[] { 0, 5 }, new[] { -3, 4 }, new[] { -4, 3 }, new[] { -5, 0 }, new[] { 0, -5 } };
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero);
            foreach (int[] p in rim)
                m.AppendVertex(new Vector3d(p[0], p[1], 0));
            int n = rim.Length;
            for (int i = 0; i < n; ++i)
                m.AppendTriangle(0, 1 + i, 1 + (i + 1) % n);
            var arr = new ExactMeshArrangement(new List<DMesh3> { m });
            arr.Compute();
            var tris = new List<Index3i>();
            var source = new List<int>();
            var flipped = new List<bool>();
            for (int j = 0; j < n; ++j) {
                int t = (first + j) % n;
                Index3i f = m.GetTriangle(t);
                int k = (turn + j) % 3;
                tris.Add(new Index3i(arr.Canonical(arr.VertexPoints[0][f[k]]), arr.Canonical(arr.VertexPoints[0][f[(k + 1) % 3]]), arr.Canonical(arr.VertexPoints[0][f[(k + 2) % 3]])));
                source.Add(arr.MeshTriangles[0][t]);
                flipped.Add(false);
            }
            var simplify = new ExactPlanarSimplify(arr, tris, source, flipped);
            var keep = new bool[arr.PointCount];
            int hub = arr.Canonical(arr.VertexPoints[0][0]);
            for (int i = 0; i < keep.Length; ++i)
                keep[i] = i != hub;
            simplify.Run(keep);
            Assert.Equal(1, simplify.Removed);
            var keys = new List<string>();
            foreach (Index3i f in simplify.Result()) {
                int k = f.a < f.b && f.a < f.c ? 0 : f.b < f.c ? 1 : 2;
                keys.Add(f[k] + " " + f[(k + 1) % 3] + " " + f[(k + 2) % 3]);
            }
            Assert.Equal(n - 2, keys.Count);
            keys.Sort(string.CompareOrdinal);
            return string.Join(", ", keys);
        }

        /// <summary>
        /// ExactPlanarSimplify triangulates the hole of a removed point the same way whatever the order of the triangles
        /// it is given and of their corners: the hole starts at its smallest point. With all points of the hole on one
        /// circle no flip follows, so the ear clipping's start decides (before: a corner of the point's first triangle).
        /// </summary>
        [Fact]
        public void FlatRegion_IsTriangulatedTheSameWay_WhateverTheOrderOfItsTriangles()
        {
            string expected = SimplifiedFan(0, 0);
            for (int first = 0; first < 8; ++first)
                for (int turn = 0; turn < 3; ++turn)
                    Assert.True(expected == SimplifiedFan(first, turn), "first triangle " + first + ", corners turned by " + turn + ": " + SimplifiedFan(first, turn) + " instead of " + expected);
        }

        // ---- coplanar faces, contacts, several and open inputs ----

        // a box [0, w]^2 x [0, h] at `at` with every face on an n by n grid: flat regions with many points on common circles
        static DMesh3 GridBox(int n, double w, double h, Vector3d at)
        {
            var m = new DMesh3();
            var ids = new Dictionary<long, int>();
            Func<int, int, int, int> vertex = (i, j, k) => {
                long key = ((long)i << 40) | ((long)j << 20) | (long)k;
                int id;
                if (ids.TryGetValue(key, out id) == false)
                    ids[key] = id = m.AppendVertex(at + new Vector3d(i * w / n, j * w / n, k * h / n));
                return id;
            };
            Action<Func<int, int, int>, bool> face = (corner, turn) => {
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j) {
                        int a = corner(i, j), b = corner(i + 1, j), c = corner(i + 1, j + 1), d = corner(i, j + 1);
                        if (turn) { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                        else { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    }
            };
            face((u, v) => vertex(u, v, 0), true);
            face((u, v) => vertex(u, v, n), false);
            face((u, v) => vertex(u, 0, v), false);
            face((u, v) => vertex(u, n, v), true);
            face((u, v) => vertex(0, u, v), true);
            face((u, v) => vertex(n, u, v), false);
            return m;
        }

        public static IEnumerable<object[]> FlatCaseNames()
        {
            foreach (string name in new[] { "coplanar union", "coplanar difference", "coplanar intersection", "common diagonal, intersection", "common diagonal, union",
                "edge contact", "vertex contact", "three boxes", "open input" })
                foreach (bool simplify in new[] { true, false })
                    yield return new object[] { name, simplify };
        }

        static void FlatCase(string name, out List<DMesh3> meshes, out ExactMeshBoolean.Operations op)
        {
            op = ExactMeshBoolean.Operations.Union;
            switch (name) {
                case "coplanar union": meshes = new List<DMesh3> { GridBox(6, 1, 1, Vector3d.Zero), GridBox(6, 1, 1, new Vector3d(0.5, 0.25, 0)) }; break;
                case "coplanar difference": meshes = new List<DMesh3> { GridBox(6, 1, 1, Vector3d.Zero), GridBox(5, 1, 0.5, new Vector3d(0.3, 0.3, 0.5)) }; op = Difference; break;
                case "coplanar intersection": meshes = new List<DMesh3> { GridBox(6, 1, 1, Vector3d.Zero), GridBox(4, 1, 1, new Vector3d(0.25, 0, 0.5)) }; op = Intersection; break;
                // (a diagonal of the second box's face crosses a triangle of the first whose points lie on one circle: the
                // constraint along it was inserted in the direction of the lower of the two triangles at the diagonal, and
                // reversing the triangles gave other pieces; found by the review of #1464)
                case "common diagonal, intersection": meshes = new List<DMesh3> { GridBox(2, 1, 1, Vector3d.Zero), GridBox(5, 1, 1, new Vector3d(0.125, 0, 0.5)) }; op = Intersection; break;
                case "common diagonal, union": meshes = new List<DMesh3> { GridBox(2, 1, 1, Vector3d.Zero), GridBox(4, 1, 1, new Vector3d(0, 0.625, 0)) }; break;
                case "edge contact": meshes = new List<DMesh3> { GridBox(3, 1, 1, Vector3d.Zero), GridBox(3, 1, 1, new Vector3d(1, 1, 0)) }; break;
                case "vertex contact": meshes = new List<DMesh3> { GridBox(3, 1, 1, Vector3d.Zero), GridBox(3, 1, 1, new Vector3d(1, 1, 1)) }; break;
                case "three boxes": meshes = new List<DMesh3> { GridBox(4, 1, 1, Vector3d.Zero), GridBox(4, 1, 1, new Vector3d(0.5, 0.5, 0)), GridBox(3, 1, 1, new Vector3d(0.25, 0.75, 0.5)) }; break;
                default: {
                    DMesh3 open = GridBox(4, 1, 1, new Vector3d(0.5, 0.25, 0.25));
                    foreach (int t in open.TriangleIndices().Take(6).ToArray()) open.RemoveTriangle(t);
                    meshes = new List<DMesh3> { GridBox(5, 1, 1, Vector3d.Zero), new DMesh3(open, true) };
                    break;
                }
            }
        }

        /// <summary>
        /// Boxes with gridded faces: coplanar overlapping faces (the arrangement triangulates points on common circles, with
        /// constraints along the other mesh's edges and diagonals), solids that touch along an edge or at a vertex (edges
        /// with more than two pieces, split and copied vertices), three inputs and an open input. The result is the same
        /// mesh, ids included, for every order of the inputs' triangles, corners and vertex ids, with and without the
        /// simplification of flat regions.
        /// </summary>
        [Theory]
        [MemberData(nameof(FlatCaseNames))]
        public void FlatAndTouchingInputs_AreBitIdentical_WhateverTheInputOrder(string name, bool simplify)
        {
            List<DMesh3> meshes;
            ExactMeshBoolean.Operations op;
            FlatCase(name, out meshes, out op);
            string first = null, status = null;
            foreach (int[] order in Orders) {
                var b = new ExactMeshBoolean(meshes.Select(m => Reordered(m, order)).ToList(), op) { SimplifyFlatRegions = simplify, AllowOpenMeshes = true };
                b.Compute();
                string mesh = Mesh(b.Result), st = b.Status + " " + b.RoundingClean + " " + b.NonManifoldEdges + " " + b.SplitNonManifoldEdges + " " + b.Contacts;
                if (first == null) {
                    first = mesh;
                    status = st;
                    Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, b.Status);
                    Assert.True(b.Result.TriangleCount > 0);
                    if (name.EndsWith("contact"))
                        Assert.True(b.Contacts > 0, name + ": no contact");
                }
                Assert.True(status == st, name + ": " + st + " instead of " + status + " for " + Order(order));
                Assert.True(first == mesh, name + ", simplify " + simplify + ": another result for " + Order(order));
            }
        }

        // ---- nearly coincident spheres, unions of balls and of tubes ----

        static DMesh3 Ball(Vector3d centre, double radius, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, centre);
            return m;
        }

        // a tapered tube from a to b, as benchmarks/NeuronSkeletonProbe builds its segments
        static DMesh3 Tube(Vector3d a, double ra, Vector3d b, double rb, int slices)
        {
            Vector3d axis = b - a;
            DMesh3 cyl = new CappedCylinderGenerator() { BaseRadius = (float)ra, TopRadius = (float)rb, Height = (float)axis.Length, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
            MeshTransforms.Rotate(cyl, Vector3d.Zero, new Quaterniond(Vector3d.AxisY, axis.Normalized));
            MeshTransforms.Translate(cyl, a);
            return cyl;
        }

        // a skeleton as a union of balls at its nodes and tubes along its edges (node: position, radius, parent or -1)
        static List<DMesh3> Skeleton(Vector3d[] at, double[] radius, int[] parent)
        {
            var parts = new List<DMesh3>();
            for (int i = 0; i < at.Length; ++i)
                parts.Add(Ball(at[i], radius[i], i == 0 ? 6 : 4));
            for (int i = 0; i < at.Length; ++i)
                if (parent[i] >= 0)
                    parts.Add(Tube(at[parent[i]], radius[parent[i]], at[i], radius[i], 8));
            return parts;
        }

        public static IEnumerable<object[]> StressCaseNames()
        {
            foreach (string name in new[] { "spheres turned", "spheres two tessellations", "balls tangent", "balls gap 1e-12", "balls overlap 1e-12", "balls overlap 1e-6",
                "balls cavity", "fork 4 degrees", "tubes 1e-6 apart" })
                yield return new object[] { name };
        }

        static void StressCase(string name, out List<DMesh3> meshes, out ExactMeshBoolean.Operations op)
        {
            op = ExactMeshBoolean.Operations.Union;
            switch (name) {
                // (a procedural reproducer of the dependence: two nearly coincident spheres that cross, a Difference)
                case "spheres turned": {
                    DMesh3 b = Ball(Vector3d.Zero, 1, 8);
                    MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 1e-4));
                    meshes = new List<DMesh3> { Ball(Vector3d.Zero, 1, 8), b };
                    op = Difference;
                    break;
                }
                case "spheres two tessellations": meshes = new List<DMesh3> { Ball(Vector3d.Zero, 1, 8), Ball(Vector3d.Zero, 1 - 1e-6, 9) }; op = Difference; break;
                // (from benchmarks/ProceduralSphereUnionProbe, #1455: two unit balls that touch, miss or overlap by a hair, and
                // the four balls around a cavity)
                case "balls tangent": meshes = new List<DMesh3> { Ball(new Vector3d(-1, 0, 0), 1, 7), Ball(new Vector3d(1, 0, 0), 1, 7) }; break;
                case "balls gap 1e-12": meshes = new List<DMesh3> { Ball(new Vector3d(-1 - 0.5e-12, 0, 0), 1, 7), Ball(new Vector3d(1 + 0.5e-12, 0, 0), 1, 7) }; break;
                case "balls overlap 1e-12": meshes = new List<DMesh3> { Ball(new Vector3d(-1 + 0.5e-12, 0, 0), 1, 7), Ball(new Vector3d(1 - 0.5e-12, 0, 0), 1, 7) }; break;
                case "balls overlap 1e-6": meshes = new List<DMesh3> { Ball(new Vector3d(-1 + 0.5e-6, 0, 0), 1, 7), Ball(new Vector3d(1 - 0.5e-6, 0, 0), 1, 7) }; break;
                case "balls cavity": {
                    double d = 1.68 / (2 * Math.Sqrt(2.0));
                    meshes = new List<DMesh3> { Ball(new Vector3d(d, d, d), 1, 6), Ball(new Vector3d(d, -d, -d), 1, 6), Ball(new Vector3d(-d, d, -d), 1, 6), Ball(new Vector3d(-d, -d, d), 1, 6) };
                    break;
                }
                // (from benchmarks/NeuronSkeletonProbe, #1457: a fork whose branches part at 4 degrees, and two branches whose
                // tips pass 1e-6 from each other)
                case "fork 4 degrees": {
                    double h = 2 * Math.PI / 180, len = 2;
                    meshes = Skeleton(new[] { Vector3d.Zero, new Vector3d(0, 0, 1.4), new Vector3d(len * Math.Sin(h), 0, 1.4 + len * Math.Cos(h)), new Vector3d(-len * Math.Sin(h), 0, 1.4 + len * Math.Cos(h)) },
                        new[] { 0.45, 0.25, 0.12, 0.12 }, new[] { -1, 0, 1, 1 });
                    break;
                }
                default: {
                    double r = 0.18, tip = r + 0.5e-6;
                    meshes = Skeleton(new[] { Vector3d.Zero, new Vector3d(-0.9, 0, 0.7), new Vector3d(0.9, 0, 0.7), new Vector3d(-tip, 0, 2), new Vector3d(tip, 0, 2) },
                        new[] { 0.45, 0.22, 0.22, r, r }, new[] { -1, 0, 0, 1, 2 });
                    break;
                }
            }
        }

        /// <summary>
        /// Inputs where the repair has many ties: two nearly coincident spheres that cross (a procedural reproducer of the
        /// dependence: reversing the triangles changed most configurations, one of them in its status), and unions of balls
        /// and tubes from the probes of #1455 and #1457 (tangent, 1e-12 and 1e-6 apart or overlapping, a cavity, a grazing
        /// fork). ExactMeshBoolean and SolidBooleanPipeline give the same status and the same mesh, ids included, for every
        /// order of the triangles, corners and vertex ids.
        /// </summary>
        [Theory]
        [MemberData(nameof(StressCaseNames))]
        public void NearCoincidentAndTangentInputs_AreBitIdentical_WhateverTheInputOrder(string name)
        {
            List<DMesh3> meshes;
            ExactMeshBoolean.Operations op;
            StressCase(name, out meshes, out op);
            string first = null, firstPipe = null;
            foreach (int[] order in Orders) {
                List<DMesh3> inputs = meshes.Select(m => Reordered(m, order)).ToList();
                var b = new ExactMeshBoolean(inputs, op);
                b.Compute();
                string mesh = b.Status + " " + b.RoundingClean + " " + (b.Rounding != null ? Counts(b.Rounding) : "") + "\n" + Mesh(b.Result);
                var pipe = new SolidBooleanPipeline(inputs, op);
                pipe.Compute();
                string piped = pipe.Status + " " + pipe.BooleanStatus + " " + pipe.Report.NotCleanReason + "\n" + Mesh(pipe.Result);
                if (first == null) {
                    first = mesh;
                    firstPipe = piped;
                    Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, b.Status);
                    Assert.True(b.Result.TriangleCount > 0);
                }
                Assert.True(first == mesh, name + ": another result of the boolean for " + Order(order));
                Assert.True(firstPipe == piped, name + ": another result of the pipeline for " + Order(order) + ": " + piped.Split('\n')[0] + " instead of " + firstPipe.Split('\n')[0]);
            }
        }

        // ---- the inside-out check ----

        // a prism over a figure of eight, (0,0) (2,2) (2,0) (0,2): closed, every edge used once in each direction, and
        // self-intersecting. One lobe is oriented outward and the other inward, so the winding number in front of a triangle
        // is 0 on the one and -1 on the other
        static DMesh3 FigureEightPrism()
        {
            var m = new DMesh3();
            double[][] o = { new[] { 0.0, 0 }, new[] { 2.0, 2 }, new[] { 2.0, 0 }, new[] { 0.0, 2 } };
            for (int s = 0; s < 2; ++s)
                foreach (double[] p in o)
                    m.AppendVertex(new Vector3d(p[0], p[1], s));
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 3, 2);
            m.AppendTriangle(4, 5, 6); m.AppendTriangle(4, 6, 7);
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(i, j, 4 + j);
                m.AppendTriangle(i, 4 + j, 4 + i);
            }
            return m;
        }

        /// <summary>
        /// The inside-out check reads one triangle per component. In front of the triangles of a component that intersects
        /// itself the winding number differs (here 0 on one lobe and -1 on the other), so with the first triangle by id the
        /// count, and the status of SolidBooleanPipeline (InsideOutInput or not), followed the order of the triangles. The
        /// triangle is now chosen by its geometry: the same count and status for every order.
        /// </summary>
        [Fact]
        public void InsideOutCheck_OfASelfIntersectingInput_IsTheSame_WhateverTheInputOrder()
        {
            DMesh3 eight = FigureEightPrism(), box = GridBox(1, 1, 1, new Vector3d(5, 0, 0));
            Assert.True(ExactMeshBoolean.IsWindingClosed(eight));
            // (that which triangle is read matters is shown by the mutant of the old rule, the first triangle by id: this test fails on it)
            var counts = new HashSet<int>();
            var statuses = new HashSet<string>();
            foreach (int[] order in Orders) {
                DMesh3 a = Reordered(eight, order), b = Reordered(box, order);
                var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { CountInsideOutCells = true };
                var read = new List<int>();
                op.CountRayObserverForTests = ti => read.Add(ti);
                op.Compute();
                Assert.Equal(0, op.InsideOutUndecided);
                Assert.Equal(2, read.Count);
                counts.Add(op.InsideOutCells);
                var pipe = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Union);
                pipe.Compute();
                statuses.Add(pipe.Status + "/" + pipe.BooleanStatus);
            }
            Assert.True(counts.Count == 1, "inside-out counts " + string.Join(", ", counts));
            Assert.True(statuses.Count == 1, "statuses " + string.Join(", ", statuses));
        }

        // A prism (height 1) over a square with a triangular hole whose corner touches the corner of the square: one closed component
        // whose vertical edge at the touching point has four triangles, two of them in the same direction (a DMesh3 has no such edge:
        // the square's corner and the hole's corner are two vertices of one position, which the boolean welds)
        static DMesh3 PrismOverTouchingAnnulus()
        {
            var m = new DMesh3();
            // the boundary of the region, the region on the left: the square counter-clockwise, then the hole clockwise
            var outer = new[] { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(4, 4, 0), new Vector3d(0, 4, 0) };
            var hole = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 3, 0), new Vector3d(3, 1, 0) };
            var bottom = new List<int>(); var top = new List<int>();
            foreach (Vector3d[] loop in new[] { outer, hole })
                foreach (Vector3d q in loop) { bottom.Add(m.AppendVertex(q)); top.Add(m.AppendVertex(q + new Vector3d(0, 0, 1))); }
            // (ids: square corner k at 0..3, hole corner k at 4..6)
            int A = 0, B = 1, C = 2, D = 3, H0 = 4, H2 = 5, H1 = 6;
            Action<int, int> wall = (u, v) => {
                Assert.True(m.AppendTriangle(bottom[u], bottom[v], top[v]) >= 0);
                Assert.True(m.AppendTriangle(bottom[u], top[v], top[u]) >= 0);
            };
            wall(A, B); wall(B, C); wall(C, D); wall(D, A); wall(H0, H2); wall(H2, H1); wall(H1, H0);
            // the region, counter-clockwise: the diagonals A-H2 and H1-A are the hole's edges (welded with H0)
            foreach (int[] tri in new[] { new[] { A, B, H1 }, new[] { B, C, H1 }, new[] { C, H2, H1 }, new[] { C, D, H2 }, new[] { D, A, H2 } }) {
                Assert.True(m.AppendTriangle(top[tri[0]], top[tri[1]], top[tri[2]]) >= 0);
                Assert.True(m.AppendTriangle(bottom[tri[0]], bottom[tri[2]], bottom[tri[1]]) >= 0);
            }
            return m;
        }

        /// <summary>
        /// The triangle that the inside-out check reads is the one with the smallest corner, then the smallest next corner, then the
        /// smallest third. Two triangles of one component that leave the smallest point towards the same next point (here the two
        /// walls that meet at the touching point, going up) are told apart by their third corner.
        /// </summary>
        [Fact]
        public void InsideOutCheck_TellsTwoTrianglesOfOneComponentApart_ByTheThirdCorner()
        {
            DMesh3 pinched = PrismOverTouchingAnnulus(), box = GridBox(1, 1, 1, new Vector3d(5, 0, 0));
            Assert.True(ExactMeshBoolean.IsWindingClosed(pinched));
            var chosen = new HashSet<string>();
            foreach (int[] order in Orders) {
                DMesh3 a = Reordered(pinched, order), b = Reordered(box, order);
                var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { CountInsideOutCells = true };
                var read = new List<int>();
                op.CountRayObserverForTests = ti => read.Add(ti);
                op.Compute();
                Assert.Equal(0, op.InsideOutUndecided);
                Assert.Equal(0, op.InsideOutCells);          // both inputs point outwards
                Assert.Equal(2, read.Count);                 // one component each: the walls at the touching point are one component
                // the triangle of the pinched input: from the smallest point (0, 0, 0), its next corner (0, 0, 1) and the smaller third corner
                Index3i t = a.GetTriangle(read[0]);
                var corners = new[] { a.GetVertex(t.a), a.GetVertex(t.b), a.GetVertex(t.c) };
                int k = Array.FindIndex(corners, x => x == Vector3d.Zero);
                Assert.True(k >= 0, "the chosen triangle has the smallest point");
                Assert.Equal(new Vector3d(0, 0, 1), corners[(k + 1) % 3]);
                chosen.Add(corners[(k + 2) % 3].ToString());
            }
            // the same triangle for every order of the input
            Assert.Single(chosen);
        }

        // ---- the repair alone ----

        // the repair as ExactMeshBoolean configures it, without the exact point ids (no vertices of different exact points
        // share a position in this case); fixedAt: the positions of the input vertices
        static MeshRoundingRepair Repair(DMesh3 mesh, HashSet<Vector3d> fixedAt)
        {
            var r = new MeshRoundingRepair(mesh) { SharedPositionsTouch = true, FixedWhereCoarse = false };
            // (read at the start: a vertex keeps its role when the repair moves it)
            var isFixed = new bool[mesh.MaxVertexID];
            foreach (int v in mesh.VertexIndices())
                isFixed[v] = fixedAt.Contains(mesh.GetVertex(v));
            r.FixedVertexF = v => v < isFixed.Length && isFixed[v];
            r.ExactVertexF = r.FixedVertexF;
            r.Apply();
            return r;
        }

        /// <summary>
        /// The documented limit: MeshRoundingRepair on one and the same mesh gives another mesh when only the ids of that mesh
        /// are reversed (the order of its work lists and ties, the end of an edge it keeps). The boolean is independent of the
        /// input order because it hands the repair a mesh numbered by its geometry, not because the repair is.
        /// </summary>
        [Fact]
        public void Repair_OfOneMesh_DependsOnItsNumbering()
        {
            Case c = ShrunkCopy();
            var inputs = new HashSet<Vector3d>();
            foreach (DMesh3 m in new[] { c.A, c.B })
                foreach (int v in m.VertexIndices())
                    inputs.Add(m.GetVertex(v));
            DMesh3 exact = Exact(c, Orders[0]).Result;
            DMesh3 asGiven = new DMesh3(exact), renumbered = Reordered(exact, new[] { -1, -1, 0 });
            Assert.Equal(TriangleSet(asGiven), TriangleSet(renumbered));
            MeshRoundingRepair r0 = Repair(asGiven, inputs), r1 = Repair(renumbered, inputs);
            Assert.True(r0.IsClean && r1.IsClean);
            Assert.True(r0.InitialDegenerate > 0 && r0.InitialDegenerate == r1.InitialDegenerate && r0.Collapses > 0);
            Assert.NotEqual(TriangleSet(asGiven), TriangleSet(renumbered));
        }
    }
}
