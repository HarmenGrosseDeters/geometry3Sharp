using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The size of a field's values is not part of its level set, in sparse mode and with local refinement as in dense mode (#1376, item 3):
    /// the contour of c f for c = 2^k has the bits of the contour of f. Sparse node values were kept as floats (a value below 1.4e-45 was 0,
    /// above 1.7e38 clamped) and an imposed sign was a value of 1e-3 from the iso value, larger than every value of c f for small c.
    /// Only the API of the library before the change is used (this file compiles against it, so that its tests count as regressions there).
    /// </summary>
    public class DualContouringSparseScaleTests
    {
        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        static double Pow2(int e) => ExactPoint3.ScaleB(1.0, e);

        // a sphere (off the grid planes) and a creased box, unit geometry
        static double Sphere(Vector3d p) => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 0.8;
        static double Box(Vector3d p)
        {
            double dx = Math.Abs(p.x - 0.03) - 0.55, dy = Math.Abs(p.y + 0.02) - 0.36, dz = Math.Abs(p.z - 0.05) - 0.27;
            return new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
        }

        // a ball of radius 0.4 and a small ball of 0.05 between two coarse nodes of a grid of 0.25 (refined by 4 on one side of the edge: the fine node
        // at its center is inside, both coarse ends of the edge are outside): the refinement imposes signs on that interface edge
        static readonly Vector3d SmallBall = new Vector3d(0.6875, 0, 0);
        static double Balls(Vector3d p) => Math.Min(p.Length - 0.4, (p - SmallBall).Length - 0.05);

        // the same edge from coarse node (6, 4, 4) along x with fine nodes 0..4 at x = 0.5, 0.5625, 0.625, 0.6875, 0.75: the end 0 is outside, the end 4
        // inside, and the fine values run out, in, out, in, in (a ball of 0.03 around node 1, a ball of 0.15 around (0.8, 0, 0)): three sign changes where
        // the coarse edge has one, so the refinement imposes node 2 (outside by its value) to be inside, and the fine edges from node 2 to its outside
        // neighbours in the refined cell are crossings that end at the imposed node: their points are interpolated between its value and the neighbour's
        static double ThreeChanges(Vector3d p) => Math.Min((p - new Vector3d(0.5625, 0, 0)).Length - 0.03, (p - new Vector3d(0.8, 0, 0)).Length - 0.15);

        static string Digest(DMesh3 m)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { for (int i = 0; i < 8; ++i) { h ^= (byte)(x >> (8 * i)); h *= 1099511628211UL; } } }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return m.TriangleCount + " " + h.ToString("X16");
        }

        // geometry scaled by 2^geo (the cells with it), values by 2^k: sparse mode over all cells of the box, no refinement
        static DualContouring Sparse(Func<Vector3d, double> unit, double half, int cells, int k, int geo = 0, bool exactHermite = true, bool parallel = false)
        {
            double s = Pow2(geo), c = Pow2(k), h = 2 * half / cells * s;
            Vector3d origin = new Vector3d(-half, -half, -half) * s;
            Func<Vector3d, double> f = p => c * unit(p / s);
            var all = new List<Vector3i>();
            for (int z = 0; z < cells; ++z) for (int y = 0; y < cells; ++y) for (int x = 0; x < cells; ++x) all.Add(new Vector3i(x, y, z));
            var dc = new DualContouring {
                Implicit = new Field { F = f, Box = new AxisAlignedBox3d(origin, origin + cells * h * Vector3d.One) }, InsideIsLess = true, CellSize = h, ParallelCompute = parallel,
                Bounds = new AxisAlignedBox3d(origin, origin + cells * h * Vector3d.One), ExactHermite = exactHermite,
                CandidateCells = all, NodeValueF = v => f(origin + h * new Vector3d(v.x, v.y, v.z))
            };
            dc.Generate();
            return dc;
        }

        // the same with the cells around the small ball refined by 4
        static DualContouring Refined(int k, int geo = 0, Func<Vector3d, double> unit = null, bool linear = false, bool parallel = false)
        {
            if (unit == null) unit = Balls;
            double s = Pow2(geo), c = Pow2(k), h = 0.25 * s;
            const int cells = 8, factor = 4;
            Vector3d origin = new Vector3d(-1, -1, -1) * s;
            Func<Vector3d, double> f = p => c * unit(p / s);
            var all = new List<Vector3i>();
            for (int z = 0; z < cells; ++z) for (int y = 0; y < cells; ++y) for (int x = 0; x < cells; ++x) all.Add(new Vector3i(x, y, z));
            // one of the four coarse cells around the edge from node (6, 4, 4) along x (so the edge is between a refined and an unrefined cell)
            var refined = new List<Vector3i>();
            var fine = new List<Vector3i>();
            {
                var cc = new Vector3i(6, 3, 3);
                refined.Add(cc);
                for (int a = 0; a < factor; ++a) for (int b = 0; b < factor; ++b) for (int d = 0; d < factor; ++d) fine.Add(factor * cc + new Vector3i(a, b, d));
            }
            var dc = new DualContouring {
                Implicit = new Field { F = f, Box = new AxisAlignedBox3d(origin, origin + cells * h * Vector3d.One) }, InsideIsLess = true, CellSize = h, ParallelCompute = parallel,
                Bounds = new AxisAlignedBox3d(origin, origin + cells * h * Vector3d.One), ExactHermite = true,
                CandidateCells = all, NodeValueF = v => f(origin + h * new Vector3d(v.x, v.y, v.z))
            };
            // linear: the Hermite point is the interpolated crossing itself (HermiteF gets it as `crossing`), the normal the analytic one of the two balls
            if (linear)
                dc.HermiteF = (DualContouring.HermiteFunc)((Vector3d a, Vector3d b, Vector3d crossing, out Vector3d point, out Vector3d normal) => {
                    Vector3d q = crossing / s, c1 = new Vector3d(0.5625, 0, 0), c2 = new Vector3d(0.8, 0, 0);
                    normal = ((q - c1).Length - 0.03 < (q - c2).Length - 0.15 ? q - c1 : q - c2).Normalized;
                    point = crossing;
                    return true;
                });
            dc.SetRefinement(factor, refined, fine, v => f(origin + h / factor * new Vector3d(v.x, v.y, v.z)));
            dc.Generate();
            return dc;
        }

        static void AssertSolid(DualContouring dc, string what)
        {
            DMesh3 m = dc.Mesh;
            Assert.True(m.TriangleCount > 0, what + ": empty");
            Assert.True(m.IsClosed(), what + ": not closed");
            Assert.True(Invariance.AllFinite(m), what + ": not finite");
        }

        // the values times 2^k, small and large: below 2^-126 a float value is subnormal or 0 (the contour was empty or partial, no status), above
        // 2^127 it was clamped
        [Theory]
        [InlineData(-60)]
        [InlineData(-140)]
        [InlineData(-300)]
        [InlineData(100)]
        [InlineData(140)]
        [InlineData(300)]
        public void Sparse_ValuesTimesAPowerOfTwo_GiveTheBitsOfTheUnitField(int k)
        {
            DualContouring unit = Sparse(Sphere, 1.2, 12, 0);
            AssertSolid(unit, "unit");
            DualContouring scaled = Sparse(Sphere, 1.2, 12, k);
            AssertSolid(scaled, "2^" + k);
            Assert.Equal(Digest(unit.Mesh), Digest(scaled.Mesh));
        }

        [Theory]
        [InlineData(-60)]
        [InlineData(-140)]
        [InlineData(-300)]
        [InlineData(100)]
        [InlineData(140)]
        [InlineData(300)]
        public void Refined_ValuesTimesAPowerOfTwo_GiveTheBitsOfTheUnitField(int k)
        {
            DualContouring unit = Refined(0);
            Assert.True(unit.ImposedEdges + unit.ImposedFaces > 0, "the case must impose signs");
            AssertSolid(unit, "unit");
            DualContouring scaled = Refined(k);
            AssertSolid(scaled, "2^" + k);
            Assert.Equal(unit.ImposedEdges, scaled.ImposedEdges);
            Assert.Equal(unit.ImposedFaces, scaled.ImposedFaces);
            Assert.Equal(Digest(unit.Mesh), Digest(scaled.Mesh));
        }

        // the linear interpolation path (HermiteF returns the interpolated crossing it is given as the Hermite point; ExactHermite = false is absolute
        // by design): crossings that end at an imposed node go through its value iso -+ step, so the step is in the output. The unit field has imposed signs (asserted); a field times 2^k gives the
        // bits of the unit one only if the step follows the values (an absolute 1e-3 puts the crossing at the end of the edge or at its middle)
        [Theory]
        [InlineData(-60)]
        [InlineData(-300)]
        [InlineData(20)]
        [InlineData(140)]
        public void Refined_LinearInterpolation_ImposedValuesFollowTheScale(int k)
        {
            DualContouring unit = Refined(0, 0, ThreeChanges, true);
            Assert.True(unit.ImposedEdges > 0, "the case must impose an edge");
            AssertSolid(unit, "unit");
            DualContouring scaled = Refined(k, 0, ThreeChanges, true);
            AssertSolid(scaled, "2^" + k);
            Assert.Equal(unit.ImposedEdges, scaled.ImposedEdges);
            Assert.Equal(Digest(unit.Mesh), Digest(scaled.Mesh));
        }

        // sparse mode computes the Hermite data and the node values in parallel: the same mesh as serially, repeated
        [Fact]
        public void Sparse_ParallelEqualsSerial_AndRepeats()
        {
            string serial = Digest(Sparse(Sphere, 1.2, 12, -140).Mesh);
            for (int i = 0; i < 3; ++i) {
                DualContouring p = Sparse(Sphere, 1.2, 12, -140, 0, true, true);
                Assert.Equal(serial, Digest(p.Mesh));
            }
            string refinedSerial = Digest(Refined(-140).Mesh);
            Assert.Equal(refinedSerial, Digest(Refined(-140, 0, null, false, true).Mesh));
            Assert.Equal(refinedSerial, Digest(Refined(-140, 0, null, false, true).Mesh));
        }

        // a creased shape in sparse mode at both ends of the geometry's range: the degenerate-triangle rules take the fourth power of an edge
        [Theory]
        [InlineData(300, 0)]
        [InlineData(-300, 0)]
        [InlineData(300, -200)]
        [InlineData(-300, 200)]
        public void SparseBox_GeometryAndValuesTimesPowersOfTwo_GiveTheBitsOfTheUnitBox(int geo, int k)
        {
            DualContouring unit = Sparse(Box, 0.9, 16, 0);
            AssertSolid(unit, "unit");
            DualContouring scaled = Sparse(Box, 0.9, 16, k, geo);
            AssertSolid(scaled, $"2^{geo}, values 2^{k}");
            Assert.Equal(Digest(unit.Mesh), Digest(Invariance.MappedBack(scaled.Mesh, new Similarity("scale", Pow2(geo), Matrix3d.Identity, Vector3d.Zero))));
        }

        // ordinary fields keep their output: sparse mode gives the contour of the dense grid (same nodes: the float rounding of the values changes
        // the interpolated points, not the signs), closed and clean
        [Fact]
        public void Sparse_OrdinaryField_IsAClosedCleanSphereOfTheRightVolume()
        {
            DualContouring dc = Sparse(Sphere, 1.2, 12, 0);
            AssertSolid(dc, "sparse");
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(dc.Mesh));
            Assert.InRange(Invariance.Volume(dc.Mesh), 4.0 / 3 * Math.PI * 0.512 * 0.97, 4.0 / 3 * Math.PI * 0.512 * 1.03);
        }

        // reuse: Generate() twice on one instance gives the same mesh (the sparse cache of node values is rebuilt)
        [Fact]
        public void Sparse_Reuse_SameMesh()
        {
            DualContouring dc = Sparse(Sphere, 1.2, 12, -140);
            string first = Digest(dc.Mesh);
            dc.FindCrossings();
            dc.Generate();
            Assert.Equal(first, Digest(dc.Mesh));
        }

        // invariance over Similarity.All(), but only the frames of scale and translation are compared: a rotated field on the world-axis grid of a
        // sparse run is another sampling and is skipped (the dense tests cover it). The field c f(S^-1 p) on the grid of the frame (cells of the
        // frame's size, the box transformed): a closed sphere of the right volume in every compared frame, the same bits scaled back in the 2^k frames
        [Fact]
        public void Sparse_Invariance_OverSimilarities()
        {
            string unit = null;
            foreach (Similarity s in Similarity.All()) {
                double cells = 12, half = 1.2, h = 2 * half / cells * s.Scale;
                Func<Vector3d, double> f = p => s.Scale * Sphere(s.Inverse(p));
                // (a rotated frame: the grid of the frame covers the rotated box)
                Vector3d origin = s.Apply(new Vector3d(-half, -half, -half));
                if (!s.IsExact) origin = s.Apply(new Vector3d(-2 * half, -2 * half, -2 * half));
                int n = s.IsExact ? 12 : 24;
                if (!s.IsExact) h = 2 * half / 12 * s.Scale;
                var all = new List<Vector3i>();
                for (int z = 0; z < n; ++z) for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x) all.Add(new Vector3i(x, y, z));
                Vector3d ex = s.Rotation * Vector3d.AxisX, ey = s.Rotation * Vector3d.AxisY, ez = s.Rotation * Vector3d.AxisZ;
                var dc = new DualContouring {
                    Implicit = new Field { F = f, Box = new AxisAlignedBox3d(origin, origin + n * h * Vector3d.One) }, InsideIsLess = true, CellSize = h, ParallelCompute = false,
                    Bounds = new AxisAlignedBox3d(origin, origin + n * h * Vector3d.One), CandidateCells = all,
                    NodeValueF = v => f(origin + h * new Vector3d(v.x, v.y, v.z))
                };
                if (!s.IsExact && s.Rotation.EpsilonEqual(Matrix3d.Identity, 0) == false)
                    continue;       // (the grid axes of the field are the world axes: a rotated field is a different sampling, covered by the dense tests)
                dc.Generate();
                AssertSolid(dc, s.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReasons(dc.Mesh));
                double vol = Invariance.Volume(dc.Mesh) / (s.Scale * s.Scale * s.Scale);
                Assert.InRange(vol, 4.0 / 3 * Math.PI * 0.512 * 0.96, 4.0 / 3 * Math.PI * 0.512 * 1.04);
                if (s.IsExact) {
                    string d = Digest(Invariance.MappedBack(dc.Mesh, s));
                    if (s.Name == Similarity.Identity.Name) unit = d;
                    Assert.Equal(unit, d);
                }
            }
            Assert.NotNull(unit);
        }
    }
}
