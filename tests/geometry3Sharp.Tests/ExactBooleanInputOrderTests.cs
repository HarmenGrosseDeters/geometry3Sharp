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
    /// triangles in another order, the same vertices with other ids): what is the same and what is not, stage by stage
    /// (docs/research/BOOLEAN_INPUT_ORDER_2026-11.md). The inputs are two nearly coincident curved plates, generated here.
    ///
    /// The tests named ..._Today pin the dependence as it is: they fail when it is removed, and are then turned into the
    /// invariance they describe. The others hold and stay.
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

        /// <summary> the orders of the triangles and of the vertex ids: 0 as given, -1 reversed, any other value a seed </summary>
        static readonly int[][] Orders = {
            new[] { 0, 0 }, new[] { -1, 0 }, new[] { 0, -1 }, new[] { -1, -1 }, new[] { 249, 111 }, new[] { 467, 771 }
        };

        static int[] Ordered(IEnumerable<int> ids, int order)
        {
            int[] a = ids.ToArray();
            if (order == -1)
                Array.Reverse(a);
            else if (order != 0) {
                // (a Fisher-Yates shuffle with a generator of its own: the same on every runtime)
                ulong s = (ulong)order * 0x9E3779B97F4A7C15UL + 1;
                for (int i = a.Length - 1; i > 0; --i) {
                    s = s * 6364136223846793005UL + 1442695040888963407UL;
                    int j = (int)((s >> 33) % (ulong)(i + 1));
                    int tmp = a[i]; a[i] = a[j]; a[j] = tmp;
                }
            }
            return a;
        }

        /// <summary> the same mesh with its triangles and its vertex ids in another order (corners kept) </summary>
        static DMesh3 Reordered(DMesh3 mesh, int triangleOrder, int vertexOrder)
        {
            var map = new Dictionary<int, int>();
            var r = new DMesh3();
            foreach (int v in Ordered(mesh.VertexIndices(), vertexOrder))
                map[v] = r.AppendVertex(mesh.GetVertex(v));
            foreach (int t in Ordered(mesh.TriangleIndices(), triangleOrder)) {
                Index3i f = mesh.GetTriangle(t);
                Assert.True(r.AppendTriangle(map[f.a], map[f.b], map[f.c]) >= 0);
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

        /// <summary> the volume, summed in an order that does not depend on the ids </summary>
        static double Volume(DMesh3 m)
        {
            var terms = new List<double>();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                Vector3d[] p = { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
                int k = 0;
                for (int i = 1; i < 3; ++i)
                    if (Compare(p[i], p[k]) < 0) k = i;
                terms.Add(p[k].Dot(p[(k + 1) % 3].Cross(p[(k + 2) % 3])));
            }
            terms.Sort();
            double v = 0;
            foreach (double x in terms) v += x;
            return v / 6;
        }

        static ExactMeshBoolean Exact(Case c, int[] order, bool simplify = true)
        {
            var b = new ExactMeshBoolean(Reordered(c.A, order[0], order[1]), Reordered(c.B, order[0], order[1]), c.Op) { SafeRounding = false, SimplifyFlatRegions = simplify };
            Assert.True(b.Compute(), c.Name + ": " + b.Error);
            return b;
        }

        static ExactMeshBoolean Rounded(Case c, int[] order)
        {
            var b = new ExactMeshBoolean(Reordered(c.A, order[0], order[1]), Reordered(c.B, order[0], order[1]), c.Op);
            Assert.True(b.Compute(), c.Name + ": " + b.Error);
            return b;
        }

        static string Counts(MeshRoundingRepair r)
        {
            return r.InitialIntersections + " " + r.InitialDegenerate + " " + r.InitialFalseContacts;
        }

        public static IEnumerable<object[]> CaseNames()
        {
            return new[] { new object[] { "shrunk" }, new object[] { "strip" }, new object[] { "tessellations" } };
        }

        static Case Named(string name)
        {
            return name == "shrunk" ? ShrunkCopy() : name == "strip" ? ThinStrip() : name == "tessellations" ? TwoTessellations() : TurnedCopy();
        }

        // ---- stage 1 and 2: the arrangement and the exact result ----

        /// <summary>
        /// The kept pieces of the arrangement (no simplification of flat regions, no repair) are the same triangles for
        /// every order of the input, and so is the exact result of these three cases with the simplification.
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ExactResult_HasTheSameTriangles_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            string pieces = TriangleSet(Exact(c, Orders[0], simplify: false).Result), exact = TriangleSet(Exact(c, Orders[0]).Result);
            Assert.NotEqual(pieces, exact);     // (the simplification removes vertices in these cases)
            foreach (int[] order in Orders.Skip(1)) {
                Assert.True(pieces == TriangleSet(Exact(c, order, simplify: false).Result), name + ": the pieces differ for order " + order[0] + ", " + order[1]);
                Assert.True(exact == TriangleSet(Exact(c, order).Result), name + ": the exact result differs for order " + order[0] + ", " + order[1]);
            }
        }

        /// <summary>
        /// Pinned: ExactPlanarSimplify triangulates the hole of a removed point starting at a corner that depends on the order
        /// of the pieces, and its flips towards Delaunay stop where the filter does not decide (points on a circle, as on a
        /// regular grid). So the triangulation of a flat region can depend on the vertex ids of the input: here the same
        /// pieces, the same number of removed points and of triangles, the same exact surface, another set of triangles.
        /// </summary>
        [Fact]
        public void FlatRegions_AreTriangulatedDifferently_ForReversedVertexIds_Today()
        {
            Case c = TurnedCopy();
            int[] asGiven = { 0, 0 }, reversedIds = { 0, -1 }, reversedTriangles = { -1, 0 };
            Assert.Equal(TriangleSet(Exact(c, asGiven, simplify: false).Result), TriangleSet(Exact(c, reversedIds, simplify: false).Result));
            ExactMeshBoolean x = Exact(c, asGiven), y = Exact(c, reversedIds);
            x.ComputeExactVertices = y.ComputeExactVertices = true;
            Assert.True(x.Compute() && y.Compute());
            Assert.Equal(x.RemovedVertices, y.RemovedVertices);
            Assert.Equal(x.Result.TriangleCount, y.Result.TriangleCount);
            // (the same surface, exactly: only its triangulation differs)
            Assert.Equal("", SurfaceFingerprint.Difference(SurfaceFingerprint.Of(x.Result, x.ResultExactVertices), SurfaceFingerprint.Of(y.Result, y.ResultExactVertices)));
            Assert.Equal(TriangleSet(x.Result), TriangleSet(Exact(c, reversedTriangles).Result));
            Assert.True(TriangleSet(x.Result) != TriangleSet(y.Result),
                "the flat regions are triangulated the same way for reversed vertex ids: the dependence is gone, turn this test into the invariance");
        }

        // ---- stage 3: the rounding repair ----

        /// <summary>
        /// Pinned: the default result (SafeRounding) depends on the order of the input. Every order gives a clean, closed
        /// result from the same exact result with the same defects after rounding, but the repair edits them in another
        /// order and keeps other vertices: other triangles, and a volume that differs in the fourth significant digit (the
        /// result is a skin or strip about as thin as the repair's bound, so each contraction moves a visible part of it).
        /// </summary>
        [Theory]
        [InlineData("shrunk", 1e-4, 2e-2)]
        [InlineData("strip", 1e-5, 2e-3)]
        public void RoundedResult_DependsOnTheInputOrder_Today(string name, double leastSpread, double mostSpread)
        {
            Case c = Named(name);
            var results = new HashSet<string>();
            var defects = new HashSet<string>();
            double low = double.MaxValue, high = double.MinValue;
            foreach (int[] order in Orders) {
                ExactMeshBoolean b = Rounded(c, order);
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, b.Status);
                Assert.True(b.RoundingClean, name + ": not clean for order " + order[0] + ", " + order[1]);
                ResultChecks.AssertValid(b.Result);
                ResultChecks.AssertClosed(b.Result);
                Assert.Equal("", BooleanOracle.CleanReason(b.Result));
                Assert.True(b.Rounding.Collapses > 0);
                defects.Add(Counts(b.Rounding));
                results.Add(TriangleSet(b.Result));
                double v = Volume(b.Result);
                low = Math.Min(low, v);
                high = Math.Max(high, v);
            }
            Assert.Single(defects);     // (the repair starts from the same defects)
            double spread = (high - low) / high;
            Assert.True(spread < mostSpread, name + ": the volumes differ by " + spread.ToString("G3") + " relative, more than pinned");
            Assert.True(results.Count > 1 && spread > leastSpread,
                name + ": " + results.Count + " results over " + Orders.Length + " orders, volumes within " + spread.ToString("G3") + " relative: the dependence is gone or smaller, turn this test into the invariance");
        }

        /// <summary>
        /// Pinned: the status of SolidBooleanPipeline (and RoundingClean of the default mode) depends on the order of the
        /// input. In some orders the repair's sequence of edits leaves one degenerate triangle that no further edit
        /// removes, and the pipeline fails with RoundingNotClean; in the others it succeeds.
        /// </summary>
        [Fact]
        public void PipelineStatus_DependsOnTheInputOrder_Today()
        {
            Case c = TwoTessellations();
            var statuses = new List<string>();
            foreach (int[] order in Orders) {
                DMesh3 a = Reordered(c.A, order[0], order[1]), b = Reordered(c.B, order[0], order[1]);
                var pipe = new SolidBooleanPipeline(a, b, c.Op);
                bool ok = pipe.Compute();
                var plain = new ExactMeshBoolean(a, b, c.Op);
                Assert.True(plain.Compute());
                Assert.Equal(ok, plain.RoundingClean);
                if (ok) {
                    Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipe.Status);
                    ResultChecks.AssertClosed(pipe.Result);
                    Assert.Equal("", BooleanOracle.CleanReason(pipe.Result));
                } else {
                    Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, pipe.Status);
                    Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, pipe.BooleanStatus);
                    Assert.Null(pipe.Result);
                    Assert.Equal(0, plain.Rounding.RemainingIntersections);
                    Assert.Equal(1, plain.Rounding.RemainingDegenerate);
                }
                statuses.Add(order[0] + "," + order[1] + ":" + pipe.Status);
            }
            Assert.True(statuses.Any(s => s.EndsWith(":Success")) && statuses.Any(s => s.EndsWith(":BooleanFailed")),
                "one status for every order (" + string.Join(" ", statuses) + "): the dependence is gone, turn this test into the invariance");
        }

        // ---- the repair alone, and a numbering that does not depend on the input ----

        // the repair as ExactMeshBoolean configures it, without the exact point ids (no vertices of different exact points
        // share a position in these cases); fixedAt: the positions of the input vertices
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

        static HashSet<Vector3d> InputPositions(Case c)
        {
            var set = new HashSet<Vector3d>();
            foreach (DMesh3 m in new[] { c.A, c.B })
                foreach (int v in m.VertexIndices())
                    set.Add(m.GetVertex(v));
            return set;
        }

        /// <summary>
        /// The mesh numbered by its geometry alone: the vertices sorted by position (those at one position by the positions
        /// of their triangles), each triangle's corners rotated to start at its smallest vertex, the triangles sorted.
        /// </summary>
        static DMesh3 NumberedByGeometry(DMesh3 m)
        {
            var ring = new Dictionary<int, string>();
            foreach (int v in m.VertexIndices()) {
                var l = new List<string>();
                foreach (int t in m.VtxTrianglesItr(v)) {
                    Index3i f = m.GetTriangle(t);
                    int k = f.a == v ? 0 : f.b == v ? 1 : 2;
                    l.Add(Bits(m.GetVertex(f[(k + 1) % 3])) + Bits(m.GetVertex(f[(k + 2) % 3])));
                }
                l.Sort(string.CompareOrdinal);
                ring[v] = string.Join("|", l);
            }
            int[] vs = m.VertexIndices().ToArray();
            Comparison<int> cmp = (x, y) => { int c = Compare(m.GetVertex(x), m.GetVertex(y)); return c != 0 ? c : string.CompareOrdinal(ring[x], ring[y]); };
            Array.Sort(vs, cmp);
            for (int i = 1; i < vs.Length; ++i)
                Assert.True(cmp(vs[i - 1], vs[i]) != 0, "two vertices that the geometry does not tell apart");
            var map = new Dictionary<int, int>();
            var r = new DMesh3();
            foreach (int v in vs)
                map[v] = r.AppendVertex(m.GetVertex(v));
            var tris = new List<Index3i>();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                int[] c = { map[f.a], map[f.b], map[f.c] };
                int k = 0;
                for (int i = 1; i < 3; ++i)
                    if (c[i] < c[k]) k = i;
                tris.Add(new Index3i(c[k], c[(k + 1) % 3], c[(k + 2) % 3]));
            }
            tris.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b != y.b ? x.b.CompareTo(y.b) : x.c.CompareTo(y.c));
            foreach (Index3i f in tris)
                Assert.True(r.AppendTriangle(f) >= 0);
            return r;
        }

        /// <summary>
        /// Pinned, and the localisation: MeshRoundingRepair on one and the same mesh (the exact result of one order, rounded)
        /// gives another mesh when only the ids of that mesh are reversed. The dependence is in the repair's use of ids
        /// (the order of its work lists and ties, the end of an edge it keeps), not in what the boolean hands it.
        /// </summary>
        [Fact]
        public void Repair_OfOneMesh_DependsOnItsNumbering_Today()
        {
            Case c = ShrunkCopy();
            HashSet<Vector3d> inputs = InputPositions(c);
            DMesh3 exact = Exact(c, Orders[0]).Result;
            DMesh3 asGiven = new DMesh3(exact), renumbered = Reordered(exact, -1, -1);
            Assert.Equal(TriangleSet(asGiven), TriangleSet(renumbered));
            MeshRoundingRepair r0 = Repair(asGiven, inputs), r1 = Repair(renumbered, inputs);
            Assert.True(r0.IsClean && r1.IsClean);
            Assert.Equal(Counts(r0), Counts(r1));
            Assert.True(r0.InitialDegenerate > 0 && r0.Collapses > 0);
            Assert.True(TriangleSet(asGiven) != TriangleSet(renumbered),
                "the repair gives the same mesh for reversed ids: the dependence is gone, turn this test into the invariance");
        }

        /// <summary>
        /// The upper bound of the option "number the exact result by its geometry before the repair": with that numbering
        /// the repair gives one mesh for every order of the input (it is a deterministic function of the numbered mesh), here
        /// on the three cases whose default result differs between the orders.
        /// </summary>
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Repair_AfterNumberingByGeometry_IsTheSame_WhateverTheInputOrder(string name)
        {
            Case c = Named(name);
            HashSet<Vector3d> inputs = InputPositions(c);
            string first = null;
            bool firstClean = false;
            var asToday = new HashSet<string>();
            foreach (int[] order in Orders) {
                DMesh3 exact = Exact(c, order).Result;
                DMesh3 today = new DMesh3(exact);
                Repair(today, inputs);
                asToday.Add(TriangleSet(today));
                DMesh3 numbered = NumberedByGeometry(exact);
                Assert.Equal(TriangleSet(exact), TriangleSet(numbered));
                MeshRoundingRepair r = Repair(numbered, inputs);
                Assert.True(r.InitialDegenerate > 0);
                if (first == null) {
                    first = TriangleSet(numbered);
                    firstClean = r.IsClean;
                }
                Assert.True(first == TriangleSet(numbered) && firstClean == r.IsClean, name + ": another result for order " + order[0] + ", " + order[1]);
            }
            Assert.True(asToday.Count > 1, name + ": without the numbering the orders give one result (the test would show nothing)");
        }
    }
}
