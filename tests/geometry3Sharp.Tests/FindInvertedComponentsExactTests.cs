using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BoolCmp;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RC3 of docs/research/BOOLEAN_TRIAGE_2026-11.md, through the public API only (so that the tests also run against the
    /// library before the fix, where they fail): MeshPostRepairCleanup.FindInvertedComponents, and so NotCleanReason's
    /// "inside-out components", took the orientation of a closed component from a rounded volume sign. The exact signs here
    /// are a test oracle of their own (BigInteger), independent of the library's ExactPredicates.VolumeSign
    /// </summary>
    public class FindInvertedComponentsExactTests
    {
        // the exact sign of the sum of det(a - o, b - o, c - o) over the triangles, o the first corner of the first one
        internal static int Exact(DMesh3 m, int[] tris)
        {
            var corners = tris.Select(t => m.GetTriangle(t)).SelectMany(tv => new[] { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) }).ToArray();
            return ExactSign(corners);
        }

        static int ExactSign(Vector3d[] p, int[][] faces) => ExactSign(faces.SelectMany(f => f.Select(k => p[k])).ToArray());

        // triangles as consecutive corner triples
        static int ExactSign(Vector3d[] corners)
        {
            const int Shift = 1200;     // every finite double times 2^1200 is an integer (the smallest is 2^-1074)
            BigInteger Big(double d)
            {
                long bits = BitConverter.DoubleToInt64Bits(d);
                int ex = (int)((bits >> 52) & 0x7FF);
                long man = bits & 0xFFFFFFFFFFFFFL;
                if (ex == 0) ex = 1; else man |= 1L << 52;
                BigInteger b = new BigInteger(man) << (ex - 1075 + Shift);
                return d < 0 ? -b : b;
            }
            Vector3d o = corners[0];
            BigInteger ox = Big(o.x), oy = Big(o.y), oz = Big(o.z), sum = 0;
            for (int i = 0; i + 2 < corners.Length; i += 3) {
                BigInteger ux = Big(corners[i].x) - ox, uy = Big(corners[i].y) - oy, uz = Big(corners[i].z) - oz;
                BigInteger vx = Big(corners[i + 1].x) - ox, vy = Big(corners[i + 1].y) - oy, vz = Big(corners[i + 1].z) - oz;
                BigInteger wx = Big(corners[i + 2].x) - ox, wy = Big(corners[i + 2].y) - oy, wz = Big(corners[i + 2].z) - oz;
                sum += wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
            }
            return sum.Sign;
        }

        // the volume as FindInvertedComponents took its sign before: floating point, relative to the centre of the whole mesh
        internal static double OldVolume(DMesh3 m, int[] tris)
        {
            Vector3d center = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, tris, v => m.GetVertex(v) - center).x;
        }

        // a nearly flat tetrahedron (its fourth corner rounded onto the plane of the others), outward by its exact sign
        // (corners b and c swapped where the first order is inside out)
        internal static int[] AppendSliverTet(DMesh3 m, Random rnd, Vector3d at)
        {
            Vector3d R(double s) => new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * s;
            Vector3d p0 = at + R(0.4), e1 = R(0.1), e2 = R(0.1);
            Vector3d[] p = { p0, p0 + e1, p0 + e2, p0 + 0.3 * e1 + 0.4 * e2 };
            int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 1, 2, 3 }, new[] { 2, 0, 3 } };
            if (ExactSign(p, faces) < 0) {
                Vector3d tmp = p[1]; p[1] = p[2]; p[2] = tmp;
            }
            int[] v = p.Select(q => m.AppendVertex(q)).ToArray();
            return faces.Select(f => m.AppendTriangle(v[f[0]], v[f[1]], v[f[2]])).ToArray();
        }

        // a unit sphere far below the slivers (they are outside it: no nesting), and a sliver whose old floating-point
        // volume is negative although its exact volume is positive; found by a fixed-seed search
        internal static (DMesh3 mesh, int[] tet) SliverWithAWrongFloatSign()
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
            Assert.Equal(-1, Exact(m, tet));
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
            // (EdgeVertices 12 until #1410: the conversion of an exact vertex rounds to nearest now, and that sphere leaves one component only)
            DMesh3 s = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 14 }.Generate().MakeDMesh();
            foreach (int v in s.VertexIndices()) { Vector3d p = s.GetVertex(v); s.SetVertex(v, new Vector3d((float)p.x, (float)p.y, (float)p.z)); }
            var c = ContactTransforms.BuildChain(new DMesh3(s), new DMesh3(s), new DMesh3(s),
                ContactVariant.PartialOverlap, 2, 1, 1, ContactVariant.UlpShift, 1, -1, 0);
            Assert.Null(c.Error);
            DMesh3 Run(DMesh3 x, DMesh3 y, Ops op) { var b = new ExactMeshBoolean(x, y, op); Assert.True(b.Compute()); return b.Result; }
            DMesh3 r2 = Run(Run(c.A, c.B, Ops.Union), c.C, Ops.Difference);
            // the exact result (no rounding): the slivers are there, each of exact volume +1, none inside out
            var exact = new ExactMeshBoolean(r2, c.A, Ops.Difference) { SafeRounding = false };
            Assert.True(exact.Compute());
            var comps = new MeshConnectedComponents(exact.Result);
            comps.FindConnectedT();
            Assert.True(comps.Count >= 2);
            foreach (var comp in comps.Components)
                Assert.Equal(1, Exact(exact.Result, comp.Indices));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(exact.Result));
            // the rounded result: clean and not inside out; the slivers of numerically zero volume are removed by the rounding
            // (#1067: untouched closed components thinner than the ulp of their coordinates), so there are fewer components
            var d = new ExactMeshBoolean(r2, c.A, Ops.Difference) { ComputeExactVertices = true };
            Assert.True(d.Compute());
            Assert.True(d.RoundingClean);
            // (#1216: what the rounding removed is listed. One sliver is 5.6e5 ulps thick, a residue of the rounding of the step
            // before, as thick as that step's drift; the repair mangles it and it goes as VolumeChanged. At 3.1e-11 of the diagonal
            // it is below the fraction of the bound that makes a result not clean; it was 2.4e4 ulps and 1e-12 until #1410 re-set the fixture,
            // so the ranges below were widened; since #1489 they are within a factor 2 of the measured values again (the upper bound of the volume is master's 1e-15). Guarded path, checked with hand mutants:
            // ReportBoundFraction 1e-3 -> 1e-7 (the sliver would become resolvable), the volume factor of volume_changed 2 -> 2^40 (the sliver would be kept) and
            // ResolutionUlps 4 -> 1e7 (it would go as BelowResolution) each fail this test)
            Assert.Equal(d.Rounding.RemovedComponents, d.Rounding.RemovedComponentInfos.Count);
            MeshRoundingRepair.RemovedComponentInfo sliver = Assert.Single(d.Rounding.RemovedComponentInfos, i => i.Reason == MeshRoundingRepair.RemovalReasons.VolumeChanged);
            Assert.InRange(sliver.ThicknessUlps, 3e5, 1.2e6);      // (measured 5.63e5; 1e4 to 1e5 until #1410)
            Assert.InRange(sliver.Thickness, 1.5e-11, 6e-11);      // (measured 3.13e-11; 1e-12 to 2e-12 until #1410)
            Assert.InRange(sliver.Volume, 2e-16, 1e-15);      // (measured 5.29e-16; below 1e-21 until #1410; 1e-16 of the sphere's volume, numerically zero)
            Assert.False(sliver.Resolvable);
            Assert.Equal(0, d.Rounding.RemovedResolvable);
            var rounded = new MeshConnectedComponents(d.Result);
            rounded.FindConnectedT();
            Assert.True(rounded.Count < comps.Count, $"{rounded.Count} components against {comps.Count} of the exact result");
            foreach (var comp in rounded.Components)
                Assert.Equal(1, Exact(d.Result, comp.Indices));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(d.Result));
            // (clean up to contacts: since #1410 the conversion rounds to nearest, and the result holds duplicated vertices of one exact point)
            ResultChecks.AssertCleanExceptContacts(d.Result, "rounded result");
            // the contacts are of the exact solid (#1489, measured with the exact vertices: 4 positions, each one exact point; the 24 contacts the rounding
            // made of different exact points were separated by the repair)
            Assert.Equal(4, ResultChecks.AssertContactsAreOfTheExactSolid(d, "rounded result"));
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
            Assert.Equal(0, Exact(m, m.TriangleIndices().ToArray()));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(m));
        }

    }
}
