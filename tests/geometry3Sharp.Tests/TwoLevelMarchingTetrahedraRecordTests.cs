using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The internal certificate record of TwoLevelMarchingTetrahedra (docs/design/CONTOURING_BLOCK_CERTIFICATE.md, Lemma 9,
    /// remark 6): the signs of the node values used, the tetrahedron edge and level of every vertex, the level of every
    /// triangle, and the count of cut polygons. Checked against the mesh and against counts made from the signs alone.
    /// </summary>
    public class TwoLevelMarchingTetrahedraRecordTests
    {
        sealed class Fn : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Fn(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
        }

        const double H = 1.0 / 16;
        static readonly Vector3d Shift = new Vector3d(0.37, 0.11, 0.73);

        // a wall around a sphere or a gyroid in a unit frame, mapped by a similarity; no domain
        static TwoLevelMarchingTetrahedra Make(string shape, double thicknessInCells, Similarity s, double sliverGuard = 1.0 / 256, bool parallel = true)
        {
            double w = thicknessInCells * H / 2;
            Matrix3d tilt = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33);
            Func<Vector3d, double> g;
            double extent = shape == "shell" ? 1 : 0.5;
            if (shape == "shell") g = p => p.Length - 0.7;
            else {
                double k = 2 * Math.PI / (16 * H);
                g = p => { Vector3d q = tilt * p; return (Math.Sin(k * q.x) * Math.Cos(k * q.y) + Math.Sin(k * q.y) * Math.Cos(k * q.z) + Math.Sin(k * q.z) * Math.Cos(k * q.x)) / k + 0.3 * Math.Pow(p.LengthSquared / 0.16, 3); };
            }
            var box = AxisAlignedBox3d.Empty;
            for (int c = 0; c < 8; ++c) box.Contain(s.Apply(extent * new Vector3d((c & 1) * 2 - 1, ((c >> 1) & 1) * 2 - 1, ((c >> 2) & 1) * 2 - 1)));
            double h = s.Scale * H;
            return new TwoLevelMarchingTetrahedra {
                Field = new Fn(p => s.Scale * g(s.Inverse(p))), HalfWidth = s.Scale * w,
                Bounds = new AxisAlignedBox3d(box.Min - h * (2 * Vector3d.One + Shift), box.Max + 2 * h * Vector3d.One), CellSize = h,
                SliverGuard = sliverGuard, ParallelCompute = parallel, RecordCertificateData = true
            };
        }

        static readonly int[][] EvenTets = { new[] { 0, 3, 5, 6 }, new[] { 1, 0, 3, 5 }, new[] { 2, 0, 3, 6 }, new[] { 4, 0, 5, 6 }, new[] { 7, 3, 5, 6 } };

        // The table above is the mesher's; it is a split of the cube: five tetrahedra of volume 1/3 (the middle one) and 1/6
        // that add up to 1, all eight corners, 18 edges (the 12 of the cube and one diagonal per face), no diagonal through the cube.
        [Fact]
        public void TheFiveTetrahedra_PartitionTheCube()
        {
            Func<int, Vector3d> corner = c => new Vector3d(c & 1, (c >> 1) & 1, (c >> 2) & 1);
            double volume = 0;
            var edges = new HashSet<int>(); var corners = new HashSet<int>();
            foreach (int[] t in EvenTets) {
                Vector3d a = corner(t[0]);
                volume += Math.Abs((corner(t[1]) - a).Dot((corner(t[2]) - a).Cross(corner(t[3]) - a))) / 6;
                for (int x = 0; x < 4; ++x) { corners.Add(t[x]); for (int y = x + 1; y < 4; ++y) edges.Add(Math.Min(t[x], t[y]) * 8 + Math.Max(t[x], t[y])); }
            }
            Assert.Equal(1.0, volume, 12);
            Assert.Equal(8, corners.Count);
            Assert.Equal(18, edges.Count);
            Assert.DoesNotContain(edges, e => ((e / 8) ^ (e % 8)) == 7);
            Assert.Equal(6, edges.Count(e => { int d = (e / 8) ^ (e % 8); return d == 3 || d == 5 || d == 6; }));
        }

        // The record against the mesh, and against the numbers of crossed tetrahedron edges and of polygon triangles that
        // follow from the node signs alone (a walk over the five tetrahedra of every cube; it counts every tetrahedron with
        // both signs of a level, which is what the mesher makes for sampled values: Lemma 9, remark 6).
        static void CheckRecord(TwoLevelMarchingTetrahedra gen)
        {
            TwoLevelMarchingTetrahedra.CertificateRecord r = gen.Certificate;
            DMesh3 mesh = gen.Mesh;
            Assert.NotNull(r);
            Vector3i n = r.Nodes;
            Assert.Equal(gen.CellDimensions + Vector3i.One, n);
            Assert.Equal(n.x * n.y * n.z, r.NodeFlags.Length);
            Assert.Equal(0, r.ClippedPolygonVertices);
            Assert.Equal(0, gen.NonManifoldTriangles);
            Assert.Equal(0, r.NonManifoldTriangles);
            Assert.Equal(gen.HalfWidthF != null, r.VariableHalfWidth);
            Assert.DoesNotContain((byte)255, r.VertexLevel);
            Assert.DoesNotContain((byte)255, r.TriangleLevel);
            Assert.Equal(mesh.MaxVertexID, r.VertexA.Count);
            Assert.Equal(mesh.MaxVertexID, r.VertexB.Count);
            Assert.Equal(mesh.MaxVertexID, r.VertexLevel.Count);
            Assert.Equal(mesh.MaxTriangleID, r.TriangleLevel.Count);
            Func<int, Vector3i> at = id => new Vector3i(id % n.x, id / n.x % n.y, id / (n.x * n.y));
            Func<Vector3i, Vector3d> pos = q => new Vector3d(r.Origin.x + r.CellSize * q.x, r.Origin.y + r.CellSize * q.y, r.Origin.z + r.CellSize * q.z);

            // the crossed tetrahedron edges and the triangles, from the signs
            var crossed = new HashSet<ValueTuple<int, int, int>>();
            int[] triangles = new int[2];
            for (int k = 0; k + 1 < n.z; ++k) for (int j = 0; j + 1 < n.y; ++j) for (int i = 0; i + 1 < n.x; ++i) {
                bool odd = ((i + j + k) & 1) != 0;
                foreach (int[] tet in EvenTets) {
                    int[] id = new int[4];
                    for (int q = 0; q < 4; ++q) {
                        int c = odd ? tet[q] ^ 1 : tet[q];     // the odd split is the even one mirrored in x
                        id[q] = (i + (c & 1)) + n.x * ((j + ((c >> 1) & 1)) + n.y * (k + ((c >> 2) & 1)));
                    }
                    for (int level = 0; level < 2; ++level) {
                        int inside = id.Count(q => (r.NodeFlags[q] >> level & 1) != 0);
                        if (inside == 0 || inside == 4) continue;
                        triangles[level] += inside == 2 ? 2 : 1;
                        for (int a = 0; a < 4; ++a) for (int b = a + 1; b < 4; ++b)
                            if ((r.NodeFlags[id[a]] >> level & 1) != (r.NodeFlags[id[b]] >> level & 1))
                                crossed.Add(new ValueTuple<int, int, int>(Math.Min(id[a], id[b]), Math.Max(id[a], id[b]), level));
                    }
                }
            }

            var seen = new HashSet<ValueTuple<int, int, int>>();
            foreach (int vid in mesh.VertexIndices()) {
                int a = r.VertexA[vid], b = r.VertexB[vid], level = r.VertexLevel[vid];
                Assert.True(a >= 0 && b > a && level < 2, "vertex " + vid);
                Assert.True(seen.Add(new ValueTuple<int, int, int>(a, b, level)), "one vertex per crossed edge");
                Assert.Contains(new ValueTuple<int, int, int>(a, b, level), crossed);
                // (T2) of Lemma 9: the coordinates the two nodes share are exact, the others strictly between
                Vector3d pa = pos(at(a)), pb = pos(at(b)), v = mesh.GetVertex(vid);
                for (int ax = 0; ax < 3; ++ax) {
                    if (pa[ax] == pb[ax]) Assert.Equal(pa[ax], v[ax]);
                    else Assert.True(v[ax] > Math.Min(pa[ax], pb[ax]) && v[ax] < Math.Max(pa[ax], pb[ax]), "vertex " + vid + " on its edge");
                }
            }
            Assert.Equal(crossed.Count, seen.Count);

            int[] counted = new int[2];
            foreach (int tid in mesh.TriangleIndices()) {
                int level = r.TriangleLevel[tid];
                Assert.True(level < 2);
                Index3i t = mesh.GetTriangle(tid);
                Assert.True(r.VertexLevel[t.a] == level && r.VertexLevel[t.b] == level && r.VertexLevel[t.c] == level, "triangle " + tid);
                ++counted[level];
            }
            Assert.Equal(triangles[0], counted[0]);
            Assert.Equal(triangles[1], counted[1]);
            Assert.True(counted[0] > 0 && counted[1] > 0);
        }

        static string Fingerprint(TwoLevelMarchingTetrahedra.CertificateRecord r)
        {
            return r.Nodes + "|" + string.Join("", r.NodeFlags.Select(b => (char)('0' + (b & 3) + (b >> 4) * 4))) + "|" + string.Join(",", r.VertexA) + "|" + string.Join(",", r.VertexB)
                + "|" + string.Join("", r.VertexLevel) + "|" + string.Join("", r.TriangleLevel) + "|" + r.ClippedPolygonVertices;
        }

        static bool SameMesh(DMesh3 a, DMesh3 b)
        {
            if (a.MaxVertexID != b.MaxVertexID || a.MaxTriangleID != b.MaxTriangleID || a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return false;
            foreach (int vid in a.VertexIndices()) if (!(a.GetVertex(vid) == b.GetVertex(vid))) return false;
            foreach (int tid in a.TriangleIndices()) if (!(a.GetTriangle(tid) == b.GetTriangle(tid))) return false;
            return true;
        }

        [Theory]
        [InlineData("shell", 0.5, 1.0 / 256)]
        [InlineData("shell", 3.0, 0.0)]
        [InlineData("gyroid", 0.5, 1.0 / 256)]
        [InlineData("gyroid", 1.5, 0.0)]
        public void TheRecord_DescribesTheMesh(string shape, double cells, double guard)
        {
            TwoLevelMarchingTetrahedra gen = Make(shape, cells, Similarity.Identity, guard);
            gen.Generate();
            CheckRecord(gen);
        }

        [Fact]
        public void TheRecord_IsOffByDefault_AndTheMeshIsTheSameWithIt()
        {
            TwoLevelMarchingTetrahedra on = Make("gyroid", 0.5, Similarity.Identity), off = Make("gyroid", 0.5, Similarity.Identity);
            Assert.False(new TwoLevelMarchingTetrahedra().RecordCertificateData);
            off.RecordCertificateData = false;
            on.Generate(); off.Generate();
            Assert.Null(off.Certificate);
            Assert.NotNull(on.Certificate);
            Assert.True(SameMesh(on.Mesh, off.Mesh));
        }

        [Fact]
        public void NodeFlags_AreTheSignsOfTheValuesUsed()
        {
            // without the guard: the signs of g - w and -g - w as the mesher computes them at the nodes
            Func<Vector3d, double> g = p => p.x + 0.25 * p.y - 0.5 * p.z * p.z;
            const double w = 0.0625;
            var gen = new TwoLevelMarchingTetrahedra { Field = new Fn(g), HalfWidth = w, Bounds = new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(0.5, 0.5, 0.5)),
                CellSize = 0.125, SliverGuard = 0, RecordCertificateData = true };
            gen.Generate();
            TwoLevelMarchingTetrahedra.CertificateRecord r = gen.Certificate;
            Assert.Equal(new Vector3i(9, 9, 9), r.Nodes);
            int zeros = 0, inside0 = 0, inside1 = 0;
            for (int k = 0; k < 9; ++k) for (int j = 0; j < 9; ++j) for (int i = 0; i < 9; ++i) {
                double v = g(new Vector3d(-0.5 + 0.125 * i, -0.5 + 0.125 * j, -0.5 + 0.125 * k));
                double f0 = v - w, f1 = -v - w;
                int expected = (f0 < 0 ? 1 : 0) | (f1 < 0 ? 2 : 0) | (f0 == 0 ? 16 : 0) | (f1 == 0 ? 32 : 0);
                Assert.Equal(expected, r.NodeFlags[i + 9 * (j + 9 * k)]);
                if ((expected & 48) != 0) ++zeros;
                if ((expected & 1) != 0) ++inside0;
                if ((expected & 2) != 0) ++inside1;
            }
            // the case has all of them: zeros of both levels at nodes (the values are sums of multiples of 1 / 128), both signs
            Assert.True(zeros > 4 && inside0 > 100 && inside1 > 100 && inside0 < 700 && inside1 < 700, zeros + " " + inside0 + " " + inside1);
            Assert.Contains(r.NodeFlags, b => (b & 16) != 0);
            Assert.Contains(r.NodeFlags, b => (b & 32) != 0);

            // with the guard: no zero is left, and a node that was zero counts as inside
            gen.SliverGuard = 1.0 / 256;
            gen.Generate();
            TwoLevelMarchingTetrahedra.CertificateRecord guarded = gen.Certificate;
            Assert.DoesNotContain(guarded.NodeFlags, b => (b & 48) != 0);
            for (int id = 0; id < r.NodeFlags.Length; ++id) {
                if ((r.NodeFlags[id] & 16) != 0) Assert.True((guarded.NodeFlags[id] & 1) != 0);
                if ((r.NodeFlags[id] & 32) != 0) Assert.True((guarded.NodeFlags[id] & 2) != 0);
                Assert.Equal(r.NodeFlags[id] & 3, guarded.NodeFlags[id] & 3 & ~((r.NodeFlags[id] >> 4) & 3));
            }
        }

        [Fact]
        public void ADomain_ACancelledRun_OrNoRequest_LeavesNoRecord_AndARunReplacesIt()
        {
            // serial, so that the calls of CancelF below are counted in order
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity, parallel: false);
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            string first = Fingerprint(gen.Certificate);
            // again on the same instance: the same record, a new object
            TwoLevelMarchingTetrahedra.CertificateRecord before = gen.Certificate;
            gen.Generate();
            Assert.NotSame(before, gen.Certificate);
            Assert.Equal(first, Fingerprint(gen.Certificate));
            CheckRecord(gen);
            // another wall on the same instance: another record
            gen.HalfWidth *= 3;
            gen.Generate();
            Assert.NotEqual(first, Fingerprint(gen.Certificate));
            CheckRecord(gen);
            // a domain: no record (the lemma is for a level without one)
            gen.Domain = new Fn(p => p.Length - 0.8);
            gen.Generate();
            Assert.NotNull(gen.Mesh);
            Assert.Null(gen.Certificate);
            gen.Domain = null;
            // cancelled: none, and the one before is gone
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            int calls = 0;
            gen.CancelF = () => ++calls > 3;
            gen.Generate();
            Assert.Null(gen.Mesh);
            Assert.Null(gen.Certificate);
            // cancelled while the mesh is made, when the record is already begun: none, and nothing of it reaches a later
            // run that does not ask for one
            calls = 0;
            int slabs = gen.CellDimensions.z + 1;
            gen.CancelF = () => ++calls > slabs + 2;
            gen.Generate();
            Assert.True(calls > slabs + 2);
            Assert.Null(gen.Mesh);
            Assert.Null(gen.Certificate);
            gen.CancelF = () => false;
            gen.RecordCertificateData = false;
            gen.Generate();
            Assert.NotNull(gen.Mesh);
            Assert.Null(gen.Certificate);
            gen.RecordCertificateData = true;
            // CancelF throws while the mesh is made: none, and nothing of the begun record reaches a later run without one
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            calls = 0;
            gen.CancelF = () => { if (++calls > slabs + 2) throw new InvalidOperationException("cancel"); return false; };
            Assert.Throws<InvalidOperationException>(() => gen.Generate());
            Assert.Null(gen.Certificate);
            gen.CancelF = () => false;
            gen.RecordCertificateData = false;
            gen.Generate();
            Assert.Null(gen.Certificate);
            gen.RecordCertificateData = true;
            // an argument that is refused: none
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            double cell = gen.CellSize;
            gen.CellSize = -1;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            Assert.Null(gen.Certificate);
            gen.CellSize = cell;
            // an exception of the field: none
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            ImplicitFunction3d good = gen.Field;
            gen.Field = new Fn(p => { throw new InvalidOperationException("field"); });
            Assert.Throws<InvalidOperationException>(() => gen.Generate());
            Assert.Null(gen.Certificate);
            gen.Field = good;
            // not requested: none
            gen.RecordCertificateData = false;
            gen.Generate();
            Assert.NotNull(gen.Mesh);
            Assert.Null(gen.Certificate);
        }

        // Node values of one grid edge on which both levels cross and the two crossing parameters, each a rounded quotient,
        // come out in the wrong order (exactly, the level -g - w is crossed first; found by search, the sampling itself did
        // not produce such values in 12 million trials). The polygon of a level is then cut by the other level, which Lemma 9
        // excludes: the record counts it. With values a little apart the order is right and the count is 0.
        [Theory]
        [InlineData(-1.857005103938893, 0.5723265060210355, 1.8570051039388928, -0.5723265060210356, true)]
        [InlineData(-1.9892730205385019, 0.12205123551133952, 1.9892730205385016, -0.12205123551133956, true)]
        [InlineData(-1.857005103938893, 0.5723265060210355, 1.8570051039388, -0.5723265060211, false)]
        public void AReversedOrderOfTwoCrossings_IsCounted(double a0, double b0, double a1, double b1, bool reversed)
        {
            // the exact order of the two crossings on an edge from a to b, and the order of the rounded parameters
            // t0 = a0 / (a0 - b0) and t1 = a1 / (a1 - b1) with a0 - b0 < 0 < a1 - b1: t1 < t0 exactly if a1 (a0 - b0) > a0 (a1 - b1)
            ExactDyadic A0 = ExactDyadic.Of(a0), B0 = ExactDyadic.Of(b0), A1 = ExactDyadic.Of(a1), B1 = ExactDyadic.Of(b1);
            Assert.True(a0 < 0 && b0 > 0 && a1 > 0 && b1 < 0 && a0 + a1 < 0 && b0 + b1 < 0);
            bool level1FirstExactly = ExactDyadic.Compare(A1 * (A0 - B0), A0 * (A1 - B1)) > 0;
            Assert.True(level1FirstExactly);        // follows from the signs above: a check of the algebra in the comment, not of the mesher
            // the two quotients as doubles, as the mesher computes them (strict double arithmetic, as on every 64-bit runtime)
            Assert.Equal(reversed, a1 / (a1 - b1) > a0 / (a0 - b0));

            // a grid of 2 x 2 x 2 cubes; the middle node has the values a, all others the values b
            var gen = new TwoLevelMarchingTetrahedra { Field = new Fn(p => 1), HalfWidth = 0.5, Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)), CellSize = 1,
                SliverGuard = 0, RecordCertificateData = true };
            Action<double[][]> values = f => {
                Assert.Equal(2, f.Length);
                Assert.Equal(27, f[0].Length);
                for (int id = 0; id < 27; ++id) { f[0][id] = id == 13 ? a0 : b0; f[1][id] = id == 13 ? a1 : b1; }
            };
            gen.NodeValuesForTest = values;
            gen.Generate();
            Assert.Null(gen.NodeValuesForTest);         // the seam is for one run
            TwoLevelMarchingTetrahedra.CertificateRecord r = gen.Certificate;
            Assert.NotNull(r);
            Assert.Equal(1, r.NodeFlags[13] & 3);
            Assert.Equal(2, r.NodeFlags[0] & 3);
            // the record does not change the mesh, in this case either
            gen.RecordCertificateData = false;
            DMesh3 recorded = gen.Mesh;
            gen.NodeValuesForTest = values;
            gen.Generate();
            Assert.True(SameMesh(recorded, gen.Mesh));
            // and without the seam the next run is the sampled field again: no crossing at all here
            gen.Generate();
            Assert.Equal(0, gen.Mesh.TriangleCount);
            Assert.Equal(recorded.MaxVertexID, r.VertexA.Count);
            Assert.Equal(recorded.MaxVertexID, r.VertexLevel.Count);
            // the triangle list stays aligned when polygons are cut, and no triangle was refused in these cases
            Assert.Equal(recorded.MaxTriangleID, r.TriangleLevel.Count);
            Assert.All(r.TriangleLevel, level => Assert.True(level < 2));
            Assert.Equal(0, gen.NonManifoldTriangles);
            Assert.Equal(0, r.NonManifoldTriangles);
            if (reversed) {
                Assert.True(r.ClippedPolygonVertices > 0);
                // the vertices where a polygon was cut are not on an edge: recorded as such, so that the lists stay aligned
                Assert.Contains(-1, r.VertexA);
                foreach (int vid in recorded.VertexIndices()) {
                    if (r.VertexA[vid] < 0) { Assert.Equal(-1, r.VertexB[vid]); Assert.Equal(255, r.VertexLevel[vid]); }
                    else Assert.True(r.VertexLevel[vid] < 2 && r.VertexB[vid] > r.VertexA[vid]);
                }
                Assert.Contains(recorded.VertexIndices(), vid => r.VertexA[vid] >= 0);
            }
            else {
                Assert.Equal(0, r.ClippedPolygonVertices);
                gen.RecordCertificateData = true;
                gen.NodeValuesForTest = values;
                gen.Generate();
                CheckRecord(gen);
            }
        }

        // The record says whether the half width was a function, and carries the generator's count of refused triangles.
        // A refused triangle was found only for node values the sampling cannot produce (random values with zeros on a
        // grid of 2 x 2 x 2 cubes, where both levels are outside at some nodes and polygons are cut: one case in 4000).
        [Fact]
        public void TheRecord_CarriesTheRefusedTriangles_AndTheKindOfHalfWidth()
        {
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity);
            double w = gen.HalfWidth;
            gen.HalfWidthF = p => w * (1 + 0.2 * Math.Sin(3 * p.x));
            gen.Generate();
            Assert.True(gen.Certificate.VariableHalfWidth);
            Assert.True(double.IsNaN(gen.Certificate.HalfWidth));
            CheckRecord(gen);
            gen.HalfWidthF = null;
            gen.Generate();
            Assert.False(gen.Certificate.VariableHalfWidth);
            Assert.Equal(w, gen.Certificate.HalfWidth);

            var rng = new Random(2704);
            double[] choices = { -1, -1, 1, 1, 0, -0.5, 0.25 };
            var v0 = new double[27]; var v1 = new double[27];
            for (int i = 0; i < 27; ++i) { v0[i] = choices[rng.Next(choices.Length)]; v1[i] = choices[rng.Next(choices.Length)]; }
            var odd = new TwoLevelMarchingTetrahedra { Field = new Fn(p => 1), HalfWidth = 0.5, Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)), CellSize = 1,
                SliverGuard = 0, RecordCertificateData = true };
            odd.NodeValuesForTest = f => { for (int id = 0; id < 27; ++id) { f[0][id] = v0[id]; f[1][id] = v1[id]; } };
            odd.Generate();
            Assert.True(odd.NonManifoldTriangles > 0);
            Assert.Equal(odd.NonManifoldTriangles, odd.Certificate.NonManifoldTriangles);
            Assert.True(odd.Certificate.ClippedPolygonVertices > 0);
            Assert.Equal(odd.Mesh.MaxTriangleID, odd.Certificate.TriangleLevel.Count);
            Assert.Equal(odd.Mesh.MaxVertexID, odd.Certificate.VertexA.Count);
        }

        // Cases at the edge of the grid: a wall that reaches Bounds (crossed edges on the grid's boundary, an open mesh), and
        // a grid of one cube. The record is aligned with the mesh all the same.
        [Fact]
        public void AWallThatReachesTheBounds_AndAGridOfOneCube_AreRecorded()
        {
            var open = new TwoLevelMarchingTetrahedra { Field = new Fn(p => p.z + 0.3 * p.x - 0.07), HalfWidth = 0.04, Bounds = new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(0.5, 0.5, 0.5)),
                CellSize = 0.125, RecordCertificateData = true };
            open.Generate();
            Assert.False(open.Mesh.IsClosed());
            CheckRecord(open);
            var one = new TwoLevelMarchingTetrahedra { Field = new Fn(p => p.x + 0.5 * p.y + 0.25 * p.z - 0.6), HalfWidth = 0.2, Bounds = new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One),
                CellSize = 1, RecordCertificateData = true };
            one.Generate();
            Assert.Equal(new Vector3i(2, 2, 2), one.Certificate.Nodes);
            CheckRecord(one);
        }

        // the seam is called with a domain too (three functions), and a run with a domain keeps no record
        [Fact]
        public void TheSeam_SeesTheDomain_AndARunWithADomainKeepsNoRecord()
        {
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity);
            gen.Domain = new Fn(p => p.z - 0.2);
            int functions = 0;
            gen.NodeValuesForTest = f => functions = f.Length;
            gen.Generate();
            Assert.Equal(3, functions);
            Assert.Null(gen.NodeValuesForTest);
            // taken also by a run that ends before the values exist
            gen.NodeValuesForTest = f => functions = -1;
            double cell = gen.CellSize;
            gen.CellSize = -1;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            Assert.Null(gen.NodeValuesForTest);
            gen.CellSize = cell;
            gen.Generate();
            Assert.Equal(3, functions);
            Assert.NotNull(gen.Mesh);
            Assert.Null(gen.Certificate);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_UnderSimilarities(string name)
        {
            Similarity s = Similarity.All().First(x => x.Name == name);
            TwoLevelMarchingTetrahedra gen = Make("shell", 0.75, s);
            gen.Generate();
            CheckRecord(gen);
            if (s.IsExact) {
                // a power of two: the same record bit for bit
                TwoLevelMarchingTetrahedra unit = Make("shell", 0.75, Similarity.Identity);
                unit.Generate();
                Assert.Equal(Fingerprint(unit.Certificate), Fingerprint(gen.Certificate));
            }
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_AndRepeatedRuns()
        {
            TwoLevelMarchingTetrahedra par = Make("gyroid", 0.5, Similarity.Identity, parallel: true), ser = Make("gyroid", 0.5, Similarity.Identity, parallel: false);
            par.Generate(); ser.Generate();
            string a = Fingerprint(par.Certificate);
            Assert.Equal(a, Fingerprint(ser.Certificate));
            Assert.True(SameMesh(par.Mesh, ser.Mesh));
            for (int run = 0; run < 3; ++run) { par.Generate(); Assert.Equal(a, Fingerprint(par.Certificate)); }
        }
    }
}
