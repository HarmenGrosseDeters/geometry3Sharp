using System;
using System.Collections.Generic;
using System.Linq;
using BoolCmp;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RC3 of docs/research/BOOLEAN_TRIAGE_2026-11.md: MeshPostRepairCleanup.FindInvertedComponents (and so
    /// MeshRobustRepair.NotCleanReason, "inside-out components") took the orientation of a closed component from the sign of
    /// its floating-point volume relative to the centre of the whole mesh; for a sliver far from that centre the rounding of
    /// the sum exceeds the volume and the sign is noise. ExactPredicates.VolumeSign is a filtered exact sign
    /// (docs/design/EXACT_VOLUME_SIGN.md): it only corrects wrong answers.
    /// </summary>
    public class ExactVolumeSignTests
    {
        static (double[] xyz, int[] corners) Arrays(DMesh3 m, IEnumerable<int> tris)
        {
            var index = new Dictionary<int, int>();
            var xyz = new List<double>();
            var corners = new List<int>();
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    if (index.TryGetValue(tv[k], out int id) == false) {
                        index[tv[k]] = id = xyz.Count / 3;
                        Vector3d p = m.GetVertex(tv[k]);
                        xyz.Add(p.x); xyz.Add(p.y); xyz.Add(p.z);
                    }
                    corners.Add(id);
                }
            }
            return (xyz.ToArray(), corners.ToArray());
        }

        static int Filtered(DMesh3 m, IEnumerable<int> tris) { var (x, c) = Arrays(m, tris); return ExactPredicates.VolumeSign(x, c); }
        static int Exact(DMesh3 m, IEnumerable<int> tris) { var (x, c) = Arrays(m, tris); return ExactPredicates.VolumeSignExact(x, c); }

        // the volume as FindInvertedComponents took its sign before: floating point, relative to the centre of the whole mesh
        static double OldVolume(DMesh3 m, int[] tris)
        {
            Vector3d center = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, tris, v => m.GetVertex(v) - center).x;
        }

        // a nearly flat tetrahedron (its fourth corner rounded onto the plane of the others), outward by its exact sign
        // (corners b and c swapped where the first order is inside out)
        static int[] AppendSliverTet(DMesh3 m, Random rnd, Vector3d at)
        {
            Vector3d R(double s) => new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * s;
            Vector3d p0 = at + R(0.4), e1 = R(0.1), e2 = R(0.1);
            Vector3d[] p = { p0, p0 + e1, p0 + e2, p0 + 0.3 * e1 + 0.4 * e2 };
            int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 1, 2, 3 }, new[] { 2, 0, 3 } };
            double[] xyz = p.SelectMany(q => new[] { q.x, q.y, q.z }).ToArray();
            if (ExactPredicates.VolumeSignExact(xyz, faces.SelectMany(f => f).ToArray()) < 0) {
                Vector3d tmp = p[1]; p[1] = p[2]; p[2] = tmp;
            }
            int[] v = p.Select(q => m.AppendVertex(q)).ToArray();
            return faces.Select(f => m.AppendTriangle(v[f[0]], v[f[1]], v[f[2]])).ToArray();
        }

        // a closed prism over a regular k-gon in a random plane through at, its top offset from its bottom by `ulps` units
        // in the last place of the coordinates along the plane's normal (rounded): a slab of nearly zero volume of many
        // triangles, outward by its exact sign
        static DMesh3 ThinPrism(Random rnd, int k, int ulps, Vector3d at)
        {
            Vector3d nrm = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
            Vector3d e1 = nrm.Cross(Vector3d.AxisZ).Normalized, e2 = nrm.Cross(e1);
            double len = at.Length;      // (positive; the next double above it, also on net48)
            double h = ulps * (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(len) + 1) - len);
            var m = new DMesh3();
            for (int i = 0; i < k; ++i) {
                double a = 2 * Math.PI * i / k;
                Vector3d p = at + 0.3 * (Math.Cos(a) * e1 + Math.Sin(a) * e2);
                m.AppendVertex(p);
                m.AppendVertex(p + h * nrm);
            }
            int cb = m.AppendVertex(at), ct = m.AppendVertex(at + h * nrm);
            for (int i = 0; i < k; ++i) {
                int j = (i + 1) % k;
                m.AppendTriangle(cb, 2 * j, 2 * i);
                m.AppendTriangle(ct, 2 * i + 1, 2 * j + 1);
                m.AppendTriangle(2 * i, 2 * j, 2 * j + 1);
                m.AppendTriangle(2 * i, 2 * j + 1, 2 * i + 1);
            }
            return m;
        }

        // a unit sphere far below the slivers (they are outside it: no nesting), and a sliver whose old floating-point
        // volume is negative although its exact volume is positive; found by a fixed-seed search
        static (DMesh3 mesh, int[] tet) SliverWithAWrongFloatSign()
        {
            var rnd = new Random(7);
            for (int it = 0; it < 5000; ++it) {
                DMesh3 m = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 3 }.Generate().MakeDMesh();
                MeshTransforms.Translate(m, new Vector3d(-2.5, -2.3, -2.1));
                int[] tet = AppendSliverTet(m, rnd, new Vector3d(0.7, 0.7, 0.7));
                int exact = Exact(m, tet);
                if (exact > 0 && OldVolume(m, tet) < 0)
                    return (m, tet);
            }
            throw new Exception("no case found");
        }

        [Fact]
        public void SliverFarFromTheCentre_PositiveExactVolume_IsNotInsideOut()
        {
            var (m, tet) = SliverWithAWrongFloatSign();
            Assert.True(OldVolume(m, tet) < 0);                 // the old sign
            Assert.Equal(1, Exact(m, tet));
            Assert.Equal(1, Filtered(m, tet));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(m));
            Assert.NotEqual("inside-out components", MeshRobustRepair.NotCleanReason(m, false));
        }

        [Fact]
        public void SliverFarFromTheCentre_Reversed_IsStillInsideOut()
        {
            var (m, tet) = SliverWithAWrongFloatSign();
            foreach (int t in tet) {
                Index3i tv = m.GetTriangle(t);
                Assert.Equal(MeshResult.Ok, m.SetTriangle(t, new Index3i(tv.a, tv.c, tv.b), false));
            }
            Assert.Equal(-1, Filtered(m, tet));
            var inv = MeshPostRepairCleanup.FindInvertedComponents(m);
            Assert.Single(inv);
            Assert.Equal(tet.OrderBy(t => t), inv[0].OrderBy(t => t));
        }

        /// <summary>
        /// the triage's procedural repro: a float-snapped sphere through the abc-chained ucd chain with C one ulp off A; the
        /// step-3 difference keeps a sliver of exact volume +3e-21 that the old sign called inside out
        /// </summary>
        [Fact]
        public void ChainSliver_OfTheTriage_IsNotInsideOut()
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            foreach (int v in s.VertexIndices()) { Vector3d p = s.GetVertex(v); s.SetVertex(v, new Vector3d((float)p.x, (float)p.y, (float)p.z)); }
            var c = ContactTransforms.BuildChain(new DMesh3(s), new DMesh3(s), new DMesh3(s),
                ContactVariant.PartialOverlap, 2, 1, 1, ContactVariant.UlpShift, 1, -1, 0);
            Assert.Null(c.Error);
            DMesh3 Run(DMesh3 x, DMesh3 y, Ops op) { var b = new ExactMeshBoolean(x, y, op); Assert.True(b.Compute()); return b.Result; }
            DMesh3 r2 = Run(Run(c.A, c.B, Ops.Union), c.C, Ops.Difference);
            var d = new ExactMeshBoolean(r2, c.A, Ops.Difference);
            Assert.True(d.Compute());
            Assert.True(d.RoundingClean);
            var comps = new MeshConnectedComponents(d.Result);
            comps.FindConnectedT();
            Assert.True(comps.Count >= 2);
            foreach (var comp in comps.Components)
                Assert.Equal(1, Exact(d.Result, comp.Indices));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(d.Result));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(d.Result));
        }

        [Fact]
        public void ZeroVolume_IsNotInsideOut()
        {
            // a closed pillow of two triangles, the same three points both ways: volume exactly 0
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0.1, 0.2, 0.3)), b = m.AppendVertex(new Vector3d(1.7, 0.4, 0.2)), c = m.AppendVertex(new Vector3d(0.3, 1.9, 0.7));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(a, c, b);
            Assert.True(m.IsClosed());
            Assert.Equal(0, Filtered(m, m.TriangleIndices()));
            Assert.Equal(0, Exact(m, m.TriangleIndices()));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(m));
        }

        [Fact]
        public void NonFiniteCoordinates_HaveNoSign()
        {
            Assert.Equal(0, ExactPredicates.VolumeSign(new[] { 0.0, 0, 0, 1, 0, 0, double.NaN, 1, 0 }, new[] { 0, 1, 2 }));
            Assert.Equal(0, ExactPredicates.VolumeSignExact(new[] { 0.0, 0, 0, 1, 0, 0, double.PositiveInfinity, 1, 0 }, new[] { 0, 1, 2 }));
            Assert.Equal(0, ExactPredicates.VolumeSign(new double[0], new int[0]));
        }

        /// <summary>
        /// The filter never decides wrong: on random slivers (volumes near the rounding of the sum, far from the origin and
        /// at extreme scales) and on fat solids, the filtered sign equals the integer sum's; a good share of the slivers needs
        /// the exact stage, the fat solids none
        /// </summary>
        [Fact]
        public void Filter_AgreesWithTheExactSum()
        {
            var rnd = new Random(20261117);
            int slivers = 0;
            foreach (double scale in new[] { 1.0, Math.Pow(2, -300), Math.Pow(2, 300), 1e-7 })
                foreach (double offset in new[] { 0.0, 1e3, 6e6 })
                    for (int i = 0; i < 150; ++i) {
                        var m = new DMesh3();
                        int[] tet = AppendSliverTet(m, rnd, new Vector3d(offset, -offset, 0.5 * offset));
                        MeshTransforms.Scale(m, scale);
                        int exact = Exact(m, tet);
                        Assert.Equal(exact, Filtered(m, tet));
                        if (exact != 0) ++slivers;
                    }
            Assert.True(slivers > 1000);
            // many triangles: thin prisms over a 64-gon, a few ulps thick, tilted, far from the origin (the rounding of the
            // sum grows with the number of terms: the bound's summation term)
            for (int i = 0; i < 300; ++i) {
                DMesh3 m = ThinPrism(rnd, 64, rnd.Next(1, 6), new Vector3d(1e3 * rnd.NextDouble(), -2e3, 7e2));
                Assert.Equal(Exact(m, m.TriangleIndices()), Filtered(m, m.TriangleIndices()));
            }
            DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 3, EdgeVertices = 8 }.Generate().MakeDMesh();
            Assert.Equal(1, Filtered(sphere, sphere.TriangleIndices()));
            sphere.ReverseOrientation();
            Assert.Equal(-1, Filtered(sphere, sphere.TriangleIndices()));
        }

        /// <summary>
        /// the filter decides fat solids, also far from the origin and at large and small scales, without the exact stage
        /// (not where the products of the differences underflow, eg 2^-500: then the underflow term keeps it undecided and the
        /// integer sum decides, as in Filter_AgreesWithTheExactSum_OpenSoupsAndSubnormals)
        /// </summary>
        [Fact]
        public void Filter_DecidesFatSolids_WithoutTheExactStage()
        {
            foreach (double scale in new[] { 1.0, Math.Pow(2, -300), Math.Pow(2, 300) }) {
                DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
                MeshTransforms.Translate(sphere, new Vector3d(1e6, 2e6, -3e6));
                MeshTransforms.Scale(sphere, scale);
                var (xyz, corners) = Arrays(sphere, sphere.TriangleIndices());
                Assert.Equal(1, ExactPredicates.VolumeSignFilter(xyz, corners));
                Assert.Equal(1, ExactPredicates.VolumeSignExact(xyz, corners));
            }
        }

        /// <summary>
        /// open triangle soups (the sum depends on the origin o, the first corner: a filter or an exact sum that took another
        /// origin would disagree), with coordinates down to subnormal numbers (products that underflow) and mixed magnitudes:
        /// the filtered sign is the exact one
        /// </summary>
        [Fact]
        public void Filter_AgreesWithTheExactSum_OpenSoupsAndSubnormals()
        {
            var rnd = new Random(985);
            int undecided = 0;
            foreach (double scale in new[] { 1.0, Math.Pow(2, -1000), Math.Pow(2, -1060), 1e-320, Math.Pow(2, 500) })
                for (int i = 0; i < 400; ++i) {
                    int k = rnd.Next(1, 6), np = 3 * k + 1;
                    var xyz = new double[3 * np];
                    for (int j = 0; j < xyz.Length; ++j) {
                        double v = (rnd.NextDouble() - 0.5) * (rnd.Next(3) == 0 ? Math.Pow(2, -80) : 1) + (j % 3 == 0 ? 3 : 0);
                        // (some coordinates rounded to eighths: exact zeros and cancellations)
                        xyz[j] = (rnd.Next(2) == 0 ? Math.Round(8 * v) / 8 : v) * scale;
                    }
                    var corners = new int[3 * k];
                    for (int j = 0; j < corners.Length; ++j) corners[j] = rnd.Next(np);
                    int exact = ExactPredicates.VolumeSignExact(xyz, corners);
                    int filter = ExactPredicates.VolumeSignFilter(xyz, corners);
                    Assert.True(filter == 0 || filter == exact, $"scale {scale:R}, case {i}: filter {filter}, exact {exact}");
                    Assert.Equal(exact, ExactPredicates.VolumeSign(xyz, corners));
                    if (filter == 0) ++undecided;
                }
            Assert.True(undecided > 0);
        }

        /// <summary>
        /// the summation term of the bound: the terms det = z of triangles (o; (1,0,0), (0,1,0), (0,0,z)) are exact, but their
        /// recursive sum loses the small ones: +1, +2^-46, then 200 times -(2^-53 - 2^-106) (each just under half an ulp of
        /// the running sum, absorbed), then -1 gives +2^-46 = 128 * 2^-53 in floating point, while the exact sum is negative.
        /// The permanents alone (about 14 * 2^-53) or a quarter of the summation term (about 101 * 2^-53) would let the filter
        /// decide +1; the bound (about 418 * 2^-53, a factor of about 3 above the error, 200 * 2^-53) does not
        /// </summary>
        [Fact]
        public void Filter_SummationError_IsInTheBound()
        {
            double[] z = { 1, Math.Pow(2, -46), -(Math.Pow(2, -53) - Math.Pow(2, -106)), -1 };
            var xyz = new List<double> { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            foreach (double v in z) { xyz.Add(0); xyz.Add(0); xyz.Add(v); }
            var corners = new List<int> { 0, 1, 2 };     // (a first triangle at o, det 0: o is the origin)
            corners.AddRange(new[] { 1, 2, 3, 1, 2, 4 });
            for (int i = 0; i < 200; ++i) corners.AddRange(new[] { 1, 2, 5 });
            corners.AddRange(new[] { 1, 2, 6 });
            double sum = 0;
            for (int t = 0; t < corners.Count / 3; ++t) sum += xyz[3 * corners[3 * t + 2] + 2] * (t == 0 ? 0 : 1);
            Assert.Equal(Math.Pow(2, -46), sum);            // the floating-point sum
            Assert.Equal(-1, ExactPredicates.VolumeSignExact(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(0, ExactPredicates.VolumeSignFilter(xyz.ToArray(), corners.ToArray()));     // not decided by the filter
            Assert.Equal(-1, ExactPredicates.VolumeSign(xyz.ToArray(), corners.ToArray()));
        }

        /// <summary>
        /// the underflow term of the bound: 1000 terms of exactly 2^-1076 (2^-300 * 2^-300 * 2^-476), each lost to 0 by the
        /// subnormal rounding, and one representable term of -100 * 2^-1074 give a floating-point sum of -100 * 2^-1074,
        /// while the exact sum, (250 - 100) * 2^-1074, is positive. The relative terms of the bound are about 1e-14 of the
        /// sum and would let the filter decide -1; UnderflowUnit times the count does not
        /// </summary>
        [Fact]
        public void Filter_UnderflowTerm_IsInTheBound()
        {
            double t = Math.Pow(2, -300);
            var xyz = new List<double> { 0, 0, 0, t, 0, 0, 0, t, 0, 0, 0, Math.Pow(2, -476), 0, 0, -100 * Math.Pow(2, -474) };
            var corners = new List<int> { 0, 1, 2 };     // (o at the origin, det 0)
            for (int i = 0; i < 1000; ++i) corners.AddRange(new[] { 1, 2, 3 });
            corners.AddRange(new[] { 1, 2, 4 });
            Assert.Equal(0.0, t * t * Math.Pow(2, -476));                       // a term lost to underflow
            Assert.Equal(-100 * double.Epsilon, t * t * (-100 * Math.Pow(2, -474)));
            Assert.Equal(1, ExactPredicates.VolumeSignExact(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(0, ExactPredicates.VolumeSignFilter(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(1, ExactPredicates.VolumeSign(xyz.ToArray(), corners.ToArray()));
        }

        /// <summary> invariance (Similarity.All()): the 2^k scales give the same sign, also for the hard sliver </summary>
        [Fact]
        public void Sign_InvariantUnderSimilarities()
        {
            var (m, tet) = SliverWithAWrongFloatSign();
            int s0 = Filtered(m, tet);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 t = Invariance.Transformed(m, sim);
                if (sim.IsExact) {
                    Assert.Equal(s0, Filtered(t, tet));
                    Assert.Equal(MeshPostRepairCleanup.FindInvertedComponents(m).Count, MeshPostRepairCleanup.FindInvertedComponents(t).Count);
                }
                // every frame: the filtered sign is the exact one
                Assert.Equal(Exact(t, tet), Filtered(t, tet));
            }
        }
    }
}
