using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshWindingNumberGrid in NarrowBand mode against FullGrid as the oracle, where nodes lie exactly at the iso value:
    /// the winding number of an open box is exactly 0.5 on the whole plane of its opening, so the 0.5 contour passes through
    /// a plane of grid nodes in free space. The front of NarrowBand stopped at such nodes (strict crossing test) and
    /// fill_spans filled the rows beyond them with values on the wrong side of the iso value.
    /// </summary>
    public class MeshWindingNumberGridNarrowBandTests
    {
        const double Cell = 0.25;

        // the six faces of the box [0,2]^3: face f is on axis f / 2, at 0 for f even and at 2 for f odd
        const int MinusX = 1 << 0, PlusX = 1 << 1, MinusY = 1 << 2, PlusY = 1 << 3, MinusZ = 1 << 4, PlusZ = 1 << 5;

        /// <summary>
        /// The box [0,2]^3 (welded, 2 x 2 quads per face, outward normals) without the faces of the mask. The grid starts at the bounds of
        /// all vertices: with stray != 0 an unused vertex at (-stray, -stray, -stray) moves the node planes by that much against the box
        /// (as MeshWindingNumberSolidify does with its shift), so that no node lies on a face or on the plane of an opening.
        /// </summary>
        static DMesh3 Box(int removed, double stray = 0)
        {
            var m = new DMesh3();
            var ids = new Dictionary<(int, int, int), int>();
            int Id(int[] c)
            {
                var key = (c[0], c[1], c[2]);
                if (!ids.TryGetValue(key, out int id)) {
                    id = m.AppendVertex(new Vector3d(c[0], c[1], c[2]));
                    ids[key] = id;
                }
                return id;
            }
            for (int f = 0; f < 6; ++f) {
                if ((removed & (1 << f)) != 0)
                    continue;
                int axis = f / 2, side = f % 2, u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int b = 0; b < 2; ++b)
                    for (int a = 0; a < 2; ++a) {
                        int P(int da, int db) { var c = new int[3]; c[axis] = 2 * side; c[u] = a + da; c[v] = b + db; return Id(c); }
                        int p00 = P(0, 0), p10 = P(1, 0), p11 = P(1, 1), p01 = P(0, 1);
                        if (side == 1) {
                            m.AppendTriangle(p00, p10, p11); m.AppendTriangle(p00, p11, p01);
                        } else {
                            m.AppendTriangle(p00, p11, p10); m.AppendTriangle(p00, p01, p11);
                        }
                    }
            }
            if (stray != 0)
                m.AppendVertex(new Vector3d(-stray, -stray, -stray));
            return m;
        }

        /// <summary> The unit sphere from a normalized cube (square roots only), welded, without the part above 0 on cutAxis (a hemisphere, the opening on the plane through the origin normal to cutAxis). </summary>
        static DMesh3 Hemisphere(int n, int cutAxis)
        {
            var m = new DMesh3();
            var ids = new Dictionary<(int, int, int), int>();
            int Id(int[] c)
            {
                var key = (c[0], c[1], c[2]);
                if (!ids.TryGetValue(key, out int id)) {
                    Vector3d p = new Vector3d(c[0] - n, c[1] - n, c[2] - n);
                    id = m.AppendVertex(p / Math.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z));
                    ids[key] = id;
                }
                return id;
            }
            for (int f = 0; f < 6; ++f) {
                int axis = f / 2, side = f % 2, u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int b = 0; b < 2 * n; ++b)
                    for (int a = 0; a < 2 * n; ++a) {
                        int P(int da, int db) { var c = new int[3]; c[axis] = 2 * n * side; c[u] = a + da; c[v] = b + db; return Id(c); }
                        int p00 = P(0, 0), p10 = P(1, 0), p11 = P(1, 1), p01 = P(0, 1);
                        if (side == 1) {
                            m.AppendTriangle(p00, p10, p11); m.AppendTriangle(p00, p11, p01);
                        } else {
                            m.AppendTriangle(p00, p11, p10); m.AppendTriangle(p00, p01, p11);
                        }
                    }
            }
            foreach (int tid in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(tid)[cutAxis] > 0)
                    m.RemoveTriangle(tid, true, false);
            MeshEditor.RemoveUnusedVertices(m);
            return m;
        }

        static DMesh3 Tilted(DMesh3 m)
        {
            Matrix3d r = Matrix3d.AxisAngleD(new Vector3d(0.3, -0.5, 0.8).Normalized, 17.0);
            DMesh3 t = new DMesh3(m);
            foreach (int vid in t.VertexIndices().ToArray())
                t.SetVertex(vid, r * (t.GetVertex(vid) - new Vector3d(1, 1, 1)));
            return t;
        }

        // name -> mesh, the cell size of the grid
        static readonly (string Name, Func<DMesh3> Mesh, double Cell)[] Shapes = {
            ("closed box", () => Box(0), Cell),
            ("closed box, stray vertex half a cell", () => Box(0, 0.5 * Cell), Cell),
            ("open -x", () => Box(MinusX), Cell), ("open +x", () => Box(PlusX), Cell),
            ("open -y", () => Box(MinusY), Cell), ("open +y", () => Box(PlusY), Cell),
            ("open -z", () => Box(MinusZ), Cell), ("open +z", () => Box(PlusZ), Cell),
            ("open -x, stray vertex half a cell", () => Box(MinusX, 0.5 * Cell), Cell), ("open +x, stray vertex half a cell", () => Box(PlusX, 0.5 * Cell), Cell),
            ("open -y, stray vertex half a cell", () => Box(MinusY, 0.5 * Cell), Cell), ("open +y, stray vertex half a cell", () => Box(PlusY, 0.5 * Cell), Cell),
            ("open -z, stray vertex half a cell", () => Box(MinusZ, 0.5 * Cell), Cell), ("open +z, stray vertex half a cell", () => Box(PlusZ, 0.5 * Cell), Cell),
            ("tube x", () => Box(MinusX | PlusX), Cell), ("tube y", () => Box(MinusY | PlusY), Cell), ("tube z", () => Box(MinusZ | PlusZ), Cell),
            ("tube x, stray vertex half a cell", () => Box(MinusX | PlusX, 0.5 * Cell), Cell),
            ("open +x, stray vertex three quarters of a cell", () => Box(PlusX, 0.75 * Cell), Cell),
            ("open +x, tilted", () => Tilted(Box(PlusX)), Cell),
            ("tube z, tilted", () => Tilted(Box(MinusZ | PlusZ)), Cell),
            ("hemisphere, opening normal to z", () => Hemisphere(6, 2), 0.125),
            ("hemisphere, opening normal to x", () => Hemisphere(6, 0), 0.125),
        };

        public static IEnumerable<object[]> ShapesAndIso()
        {
            for (int s = 0; s < Shapes.Length; ++s)
                foreach (float iso in new[] { 0.5f, 0.25f, 0.75f, 1f })
                    yield return new object[] { s, iso };
        }

        public static IEnumerable<object[]> ShapeNames() => Shapes.Select((s, i) => new object[] { i });

        sealed class PlainSubclassTree : DMeshAABBTree3
        {
            public PlainSubclassTree(DMesh3 mesh) : base(mesh, true) { }
        }

        /// <summary> A tree that records the points whose winding number is asked (the general path of the grid: no row cache). </summary>
        sealed class RecordingTree : DMeshAABBTree3
        {
            public readonly ConcurrentDictionary<Vector3d, byte> Points = new ConcurrentDictionary<Vector3d, byte>();
            public RecordingTree(DMesh3 mesh) : base(mesh, true) { }
            public override double WindingNumber(Vector3d p)
            {
                Points[p] = 0;
                return base.WindingNumber(p);
            }
        }

        public enum Path { RowCache, Subclass, Fast, Legacy }

        static MeshWindingNumberGrid Make(DMesh3 mesh, double cell, MeshWindingNumberGrid.ComputeModes mode, float iso, Path path, Func<DMesh3, DMeshAABBTree3> treeFactory = null)
        {
            DMeshAABBTree3 tree = treeFactory != null ? treeFactory(mesh) : path == Path.Subclass ? new PlainSubclassTree(mesh) : new DMeshAABBTree3(mesh, true);
            if (path == Path.Legacy)
                tree.ForceLegacyWindingCache = true;
            var g = new MeshWindingNumberGrid(mesh, tree, cell) {
                ComputeMode = mode, WindingIsoValue = iso, UseFastWindingNumber = path == Path.Fast
            };
            g.Compute();
            return g;
        }

        static int Side(float v, float iso) => v < iso ? -1 : v > iso ? 1 : 0;
        static int Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0);

        sealed class Verdict
        {
            public int Nodes, AtIso, SideDiffs, RowViolations, Evaluations, FullEvaluations;
            public string First = "";
            public override string ToString() => $"{Nodes} nodes, {AtIso} exactly at the iso value in the full grid, {SideDiffs} on another side than FullGrid ({First}), {RowViolations} row violations, evaluations {Evaluations} / {FullEvaluations}";
        }

        /// <summary>
        /// The two properties NarrowBand has to have against FullGrid: every node is on the same side of the iso value (strictly below,
        /// exactly at, strictly above), and along an x row (which fill_spans fills) both nodes of every consecutive pair that is not
        /// strictly on one side of the iso value are computed, ie bit-equal to FullGrid.
        /// </summary>
        static Verdict Compare(DMesh3 mesh, double cell, float iso, Path path, Func<DMesh3, DMeshAABBTree3> treeFactory = null)
        {
            MeshWindingNumberGrid full = Make(mesh, cell, MeshWindingNumberGrid.ComputeModes.FullGrid, iso, path, treeFactory);
            MeshWindingNumberGrid narrow = Make(mesh, cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, iso, path, treeFactory);
            Assert.Equal(full.Dimensions, narrow.Dimensions);
            var v = new Verdict { Evaluations = narrow.WindingEvaluations, FullEvaluations = full.WindingEvaluations };
            DenseGrid3f f = full.Grid, n = narrow.Grid;
            v.Nodes = f.ni * f.nj * f.nk;
            for (int k = 0; k < f.nk; ++k)
                for (int j = 0; j < f.nj; ++j)
                    for (int i = 0; i < f.ni; ++i) {
                        float a = f[i, j, k], b = n[i, j, k];
                        if (a == iso)
                            v.AtIso++;
                        if (Side(a, iso) != Side(b, iso)) {
                            if (v.SideDiffs++ == 0)
                                v.First = $"node ({i},{j},{k}): full {a:R}, narrow {b:R}";
                        }
                        if (i + 1 < f.ni) {
                            float a2 = f[i + 1, j, k];
                            bool strictlyOneSide = (a < iso && a2 < iso) || (a > iso && a2 > iso);
                            if (!strictlyOneSide && (Bits(a) != Bits(b) || Bits(a2) != Bits(n[i + 1, j, k])))
                                v.RowViolations++;
                        }
                    }
            return v;
        }



        // ---- the defect ----

        [Fact]
        public void OpenBox_OpeningPlaneIsExactlyAtTheIsoValue_NarrowBandKeepsTheSides()
        {
            // the reproducer: master gives 59 nodes on the other side of 0.5 than FullGrid (first: node (11,4,4): full 0.3522371, narrow 0.5)
            DMesh3 box = Box(PlusX);
            Verdict v = Compare(box, Cell, 0.5f, Path.RowCache);
            Assert.True(v.AtIso == 49, "the opening plane is exactly 0.5: " + v);
            Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, v.ToString());

            MeshWindingNumberGrid full = Make(box, Cell, MeshWindingNumberGrid.ComputeModes.FullGrid, 0.5f, Path.RowCache);
            MeshWindingNumberGrid narrow = Make(box, Cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
            Assert.Equal(Bits(0.3522371f), Bits(full.Grid[11, 4, 4]));
            Assert.Equal(Bits(full.Grid[11, 4, 4]), Bits(narrow.Grid[11, 4, 4]));
        }

        [Theory]
        [MemberData(nameof(ShapesAndIso))]
        public void NarrowBand_SameSideAsFullGrid_AndRowPairsAcrossTheIsoValueComputed(int shape, float iso)
        {
            Verdict v = Compare(Shapes[shape].Mesh(), Shapes[shape].Cell, iso, Path.RowCache);
            Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, Shapes[shape].Name + ", iso " + iso + ": " + v);
        }

        [Theory]
        [MemberData(nameof(ShapesAndIso))]
        public void NarrowBand_GeneralPath_SameSideAsFullGrid(int shape, float iso)
        {
            // a subclassed spatial: no row cache, the general winding_number path for the band and the front
            Verdict v = Compare(Shapes[shape].Mesh(), Shapes[shape].Cell, iso, Path.Subclass);
            Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, Shapes[shape].Name + ", iso " + iso + ": " + v);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(14)]
        [InlineData(20)]
        public void NarrowBand_FastAndLegacyWinding_SameSideAsFullGrid(int shape)
        {
            foreach (Path path in new[] { Path.Fast, Path.Legacy }) {
                Verdict v = Compare(Shapes[shape].Mesh(), Shapes[shape].Cell, 0.5f, path);
                Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, path + ", " + Shapes[shape].Name + ": " + v);
            }
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(0.5f)]
        public void ClosedBox_FieldExactlyAtTheIsoValueOverARegion_FrontDoesNotFlood(float iso)
        {
            // iso 1: the field of a closed mesh is exactly 1 over the whole inside. A plateau of exact values over a volume is not followed
            // (a fractional exact value occurs only on surfaces); the front must stay near the surface, as the strict one did
            Verdict v = Compare(Box(0), Cell / 4, iso, Path.RowCache);
            Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, v.ToString());
            Assert.True(v.AtIso > 100, v.ToString());
            // a flood would evaluate about every node. Measured: iso 1: 23,237 of 50,653 (ratio 0.459); iso 0.5: 29,181 before the on-surface value was
            // the principal value (606 nodes exactly at 0.5), 30,596 after it, ratio 0.604 (#1546: every face node reads exactly 0.5, 5,766 nodes; the
            // plateau on the faces is a surface and is followed, by design). The limit (0.65) is well below a flood and above both.
            Assert.True(v.Evaluations < v.FullEvaluations * 0.65, v.ToString());
        }

        [Fact]
        public void HalfCellShift_NoNodeExactlyAtTheIsoValue_SoTheFrontIsTheStrictOne()
        {
            // with no node exactly at the iso value the non-strict and the strict test are the same: the grids are the ones of the old code
            foreach (int shape in new[] { 1, 8, 9, 10, 11, 12, 13, 17, 18 }) {
                Verdict v = Compare(Shapes[shape].Mesh(), Shapes[shape].Cell, 0.5f, Path.RowCache);
                Assert.True(v.AtIso == 0, Shapes[shape].Name + ": " + v);
                Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, Shapes[shape].Name + ": " + v);
            }
        }

        // ---- synthetic fields: the crossing test pair by pair ----

        /// <summary> A spatial whose winding number is a given function of the position (the general path of the grid). </summary>
        sealed class FieldTree : DMeshAABBTree3
        {
            readonly Func<Vector3d, double> field;
            public readonly ConcurrentDictionary<Vector3d, byte> Points = new ConcurrentDictionary<Vector3d, byte>();
            public FieldTree(DMesh3 mesh, Func<Vector3d, double> field) : base(mesh, true) { this.field = field; }
            public override double WindingNumber(Vector3d p)
            {
                Points[p] = 0;
                return field(p);
            }
        }

        /// <summary>
        /// Two tiny triangles, at x = 0 and x = 12, on a grid of cell 1 (the nodes are at integers, the band is the nodes at x = 0 and 1 around
        /// the first one), and a field that depends on the node column x only: the value of table[x], deep (strictly below any iso value) elsewhere.
        /// </summary>
        static DMesh3 TwoTriangles()
        {
            var m = new DMesh3();
            foreach (double x0 in new[] { 0.0, 12.0 }) {
                int a = m.AppendVertex(new Vector3d(x0, 0, 0)), b = m.AppendVertex(new Vector3d(x0 + 0.001, 0, 0)), c = m.AppendVertex(new Vector3d(x0, 0.001, 0));
                m.AppendTriangle(a, b, c);
            }
            return m;
        }

        static Func<DMesh3, DMeshAABBTree3> Field(Dictionary<int, double> table, double deep, out FieldTree[] last)
        {
            var box = new FieldTree[1];
            last = box;
            return mesh => box[0] = new FieldTree(mesh, p => table.TryGetValue((int)Math.Round(p.x), out double v) ? v : deep);
        }

        // below, exactly at the iso value, above, in columns x = 0, 1, ...: every chain needs each of its pairs as a crossing to reach the next column
        public static IEnumerable<object[]> Chains()
        {
            foreach (float iso in new[] { 0.5f, 0.25f, 0.75f, 1f })
                for (int chain = 0; chain < 5; ++chain)
                    if (chain < 4 || iso != 1f)    // (a plateau of exact values is not followed for an integer iso value)
                        yield return new object[] { chain, iso };
        }

        static Dictionary<int, double> ChainTable(int chain, float iso, out double deep)
        {
            double I = iso, B = (float)(0.6 * iso), A = (float)(1.4 * iso);
            deep = (float)(0.2 * iso);
            switch (chain) {
                case 0: return new Dictionary<int, double> { { 0, B }, { 1, I }, { 2, A }, { 3, B } };                 // at iso, above, below
                case 1: return new Dictionary<int, double> { { 0, B }, { 1, B }, { 2, I }, { 3, A }, { 4, B } };       // below, at iso, above, below
                case 2: return new Dictionary<int, double> { { 0, B }, { 1, I }, { 2, B }, { 3, A }, { 4, B } };       // at iso, below, above, below
                case 3: return new Dictionary<int, double> { { 0, A }, { 1, A }, { 2, I }, { 3, B } };                 // above, at iso, below
                default: return new Dictionary<int, double> { { 0, B }, { 1, I }, { 2, I }, { 3, I }, { 4, I }, { 5, B } };  // a plateau along the row
            }
        }

        [Theory]
        [MemberData(nameof(Chains))]
        public void NarrowBand_SyntheticField_FollowsEveryPairThatIsNotStrictlyOnOneSide(int chain, float iso)
        {
            var table = ChainTable(chain, iso, out double deep);
            Func<DMesh3, DMeshAABBTree3> factory = Field(table, deep, out FieldTree[] _);
            Verdict v = Compare(TwoTriangles(), 1.0, iso, Path.Subclass, factory);
            Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, "chain " + chain + ", iso " + iso + ": " + v);
            Assert.True(v.AtIso > 0, v.ToString());
        }

        [Fact]
        public void NarrowBand_SyntheticField_ANaNIsNoCrossing()
        {
            // a node whose value is NaN is not a crossing with anything (not even with another NaN): the front does not flood the grid from the band
            Func<DMesh3, DMeshAABBTree3> factory = Field(new Dictionary<int, double>(), double.NaN, out FieldTree[] tree);
            MeshWindingNumberGrid g = Make(TwoTriangles(), 1.0, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.Subclass, factory);
            int nodes = g.Grid.Buffer.Length;
            Assert.True(tree[0].Points.Count < nodes / 2, tree[0].Points.Count + " of " + nodes + " nodes evaluated");
        }

        // ---- end to end: marching cubes ----

        [Fact]
        public void OpenBox_MarchingCubesOnNarrowBand_DoesNotLeakBeyondTheOpening()
        {
            // master: the 0.5 contour leaks along the rows beyond the opening plane x = 2 (bounding box up to the grid border)
            DMesh3 box = Box(PlusX);
            MeshWindingNumberGrid narrow = Make(box, Cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
            MeshWindingNumberGrid full = Make(box, Cell, MeshWindingNumberGrid.ComputeModes.FullGrid, 0.5f, Path.RowCache);
            AxisAlignedBox3d bn = Contour(narrow), bf = Contour(full);
            Assert.True(bn.Max.x <= 2.0 + Cell + 1e-9, "narrow band contour reaches x = " + bn.Max.x);
            Assert.True(bf.Max.x <= 2.0 + Cell + 1e-9, "full grid contour reaches x = " + bf.Max.x);
            Assert.Equal(bf.Max.x, bn.Max.x);
        }

        static AxisAlignedBox3d Contour(MeshWindingNumberGrid g)
        {
            var iso = new DenseGridTrilinearImplicit(g.Grid, g.GridOriginD, g.CellSize) { Outside = 0 };
            AxisAlignedBox3d bounds = iso.Bounds();
            bounds.Expand(3 * Cell);
            var mc = new MarchingCubes() { Implicit = iso, IsoValue = 0.5, Bounds = bounds, CubeSize = Cell, RootMode = MarchingCubes.RootfindingModes.LerpSteps };
            mc.Generate();
            Assert.True(mc.Mesh.TriangleCount > 0);
            return mc.Mesh.CachedBounds;
        }

        // ---- invariance, reuse, determinism ----

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void NarrowBand_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Box(PlusX);
            DMesh3 mt = Invariance.Transformed(m, sim);
            MeshWindingNumberGrid g = Make(m, Cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
            if (sim.IsExact) {
                // (the cell size is scaled with the mesh: a power of two is exact, the grid is the same bit for bit)
                MeshWindingNumberGrid gt = Make(mt, Cell * sim.Scale, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
                Assert.Equal(g.Dimensions, gt.Dimensions);
                float[] a = g.Grid.Buffer, b = gt.Grid.Buffer;
                for (int i = 0; i < a.Length; ++i)
                    Assert.Equal(Bits(a[i]), Bits(b[i]));
            } else {
                // not exact: the nodes are no longer exactly at the iso value, but the oracle holds in the moved frame
                Verdict v = Compare(mt, Cell * sim.Scale, 0.5f, Path.RowCache);
                Assert.True(v.SideDiffs == 0 && v.RowViolations == 0, name + ": " + v);
            }
        }

        [Fact]
        public void NarrowBand_ReuseOfOneInstance_SameGrid()
        {
            DMesh3 box = Box(PlusX);
            var g = new MeshWindingNumberGrid(box, new DMeshAABBTree3(box, true), Cell);
            g.Compute();
            float[] first = (float[])g.Grid.Buffer.Clone();
            g.Compute();
            Assert.Equal(first.Length, g.Grid.Buffer.Length);
            for (int i = 0; i < first.Length; ++i)
                Assert.Equal(Bits(first[i]), Bits(g.Grid.Buffer[i]));
        }

        [Fact]
        public void NarrowBand_ParallelEqualsSerial_InEveryOrder_AndTheSetOfComputedNodesIsTheSame()
        {
            foreach (int shape in new[] { 3, 20, 22 }) {
                DMesh3 mesh = Shapes[shape].Mesh();
                double cell = Shapes[shape].Cell;

                Run(mesh, cell, serial: false, order: 0, out float[] reference, out HashSet<Vector3d> referenceSet);
                Assert.True(reference.Length > 0);
                for (int rep = 0; rep < 2; ++rep) {
                    Run(mesh, cell, false, 0, out float[] again, out HashSet<Vector3d> againSet);
                    Assert.True(SameBits(reference, again) && againSet.SetEquals(referenceSet), Shapes[shape].Name + ": parallel repeat " + rep);
                }
                foreach (int order in new[] { 0, -1, 7, 12345 }) {
                    Run(mesh, cell, true, order, out float[] serial, out HashSet<Vector3d> serialSet);
                    Assert.True(SameBits(reference, serial), Shapes[shape].Name + ": serial order " + order + " grid");
                    Assert.True(serialSet.SetEquals(referenceSet), Shapes[shape].Name + ": serial order " + order + " computed nodes");
                }
            }
        }

        static bool SameBits(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; ++i)
                if (Bits(a[i]) != Bits(b[i]))
                    return false;
            return true;
        }

        // the grid and the set of points whose winding number was asked (the general path: a recording subclass)
        static void Run(DMesh3 mesh, double cell, bool serial, int order, out float[] grid, out HashSet<Vector3d> points)
        {
            var tree = new RecordingTree(mesh);
            var g = new MeshWindingNumberGrid(mesh, tree, cell);
            bool prevSerial = gParallel.SerialOnThisThread;
            int prevOrder = gParallel.SerialOrderOnThisThread;
            try {
                gParallel.SerialOnThisThread = serial;
                gParallel.SerialOrderOnThisThread = order;
                g.Compute();
            } finally {
                gParallel.SerialOnThisThread = prevSerial;
                gParallel.SerialOrderOnThisThread = prevOrder;
            }
            grid = (float[])g.Grid.Buffer.Clone();
            points = new HashSet<Vector3d>(tree.Points.Keys);
        }

        [Fact]
        public void NarrowBand_RowBatchedBFS_BitIdenticalToSubclassAndSerialAndRespectsCancelF()
        {
            DMesh3 openSphere = new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = 8 }.Generate().MakeDMesh();
            foreach (int tid in openSphere.TriangleIndices().ToArray())
                if (openSphere.GetTriCentroid(tid).z > 0.35)
                    openSphere.RemoveTriangle(tid, true, false);

            DMesh3 flippedSphere = new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = 8 }.Generate().MakeDMesh();
            foreach (int tid in flippedSphere.TriangleIndices().ToArray()) {
                if (flippedSphere.GetTriCentroid(tid).z > 0.35) {
                    Index3i tv = flippedSphere.GetTriangle(tid);
                    flippedSphere.RemoveTriangle(tid, false, false);
                    flippedSphere.AppendTriangle(tv.a, tv.c, tv.b);
                }
            }

            foreach (DMesh3 mesh in new[] { openSphere, flippedSphere }) {
                const double cell = 0.25;
                MeshWindingNumberGrid rowParallel = Make(mesh, cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
                MeshWindingNumberGrid subParallel = Make(mesh, cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.Subclass);

                Assert.Equal(subParallel.Dimensions, rowParallel.Dimensions);
                Assert.True(SameBits(subParallel.Grid.Buffer, rowParallel.Grid.Buffer));
                Assert.True(rowParallel.WindingEvaluations > 0 && rowParallel.WindingEvaluations <= subParallel.WindingEvaluations);

                // Row-batched BFS must be bit-identical between parallel and serial schedules, with identical WindingEvaluations.
                bool prevSerial = gParallel.SerialOnThisThread;
                int prevOrder = gParallel.SerialOrderOnThisThread;
                try {
                    gParallel.SerialOnThisThread = true;
                    gParallel.SerialOrderOnThisThread = -1;
                    MeshWindingNumberGrid rowSerial = Make(mesh, cell, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
                    Assert.True(SameBits(rowParallel.Grid.Buffer, rowSerial.Grid.Buffer));
                    Assert.Equal(rowParallel.WindingEvaluations, rowSerial.WindingEvaluations);
                } finally {
                    gParallel.SerialOnThisThread = prevSerial;
                    gParallel.SerialOrderOnThisThread = prevOrder;
                }
            }

            // CancelF during row-batched BFS exits cleanly without throwing and evaluates fewer nodes than full completion.
            MeshWindingNumberGrid fullRun = Make(openSphere, 0.2, MeshWindingNumberGrid.ComputeModes.NarrowBand, 0.5f, Path.RowCache);
            int cancelCalls = 0;
            var cancelled = new MeshWindingNumberGrid(openSphere, new DMeshAABBTree3(openSphere, true), 0.2) {
                ComputeMode = MeshWindingNumberGrid.ComputeModes.NarrowBand,
                CancelF = () => System.Threading.Interlocked.Increment(ref cancelCalls) > 60
            };
            cancelled.Compute();
            Assert.True(cancelled.WindingEvaluations < fullRun.WindingEvaluations);
        }
    }
}
