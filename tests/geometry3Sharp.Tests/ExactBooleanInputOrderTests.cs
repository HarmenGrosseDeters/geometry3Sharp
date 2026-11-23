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

        // ---- the order of the points ----

        /// <summary>
        /// ExactMeshArrangement.SortPoints gives the list sorted by ComparePoints, whatever order it is handed and whichever
        /// way it takes: the sort by the filter's key with the insertion sort after it, or (budget below 0, or used up at
        /// the first move with 0) ComparePoints alone. The points are those of two nearly coincident plates: many lie closer
        /// along the key's direction than the errors of their keys.
        /// </summary>
        [Fact]
        public void SortPoints_IsTheOrderOfComparePoints_OnEveryPath()
        {
            // (the rotated plates: there the keys alone give another order than ComparePoints, so the insertion sort moves points)
            Case tess = TwoTessellations(), shrunk = ShrunkCopy();
            Similarity rotated = Similarity.ByName("rotated");
            foreach (DMesh3[] meshes in new[] { new[] { tess.A, tess.B }, new[] { Invariance.Transformed(shrunk.A, rotated), Invariance.Transformed(shrunk.B, rotated) } }) {
                var arr = new ExactMeshArrangement(new List<DMesh3>(meshes));
                arr.Compute();
                var seen = new HashSet<int>();
                foreach (var t in arr.Output) {
                    seen.Add(t.A);
                    seen.Add(t.B);
                    seen.Add(t.C);
                }
                var expected = new List<int>(seen);
                expected.Sort(arr.ComparePoints);
                Assert.True(expected.Count > 100);
                for (int i = 1; i < expected.Count; ++i)
                    Assert.True(arr.ComparePoints(expected[i - 1], expected[i]) < 0);
                foreach (int budget in new[] { 8, 0, -1 })
                    foreach (int order in new[] { 0, -1, 5, 77 }) {
                        arr.SortPointsMoveBudget = budget;
                        var ids = new List<int>(Ordered(expected, order));
                        arr.SortPoints(ids);
                        Assert.True(expected.SequenceEqual(ids), "budget " + budget + ", order " + order);
                    }
                var none = new List<int>();
                arr.SortPoints(none);
                Assert.Empty(none);
            }
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
