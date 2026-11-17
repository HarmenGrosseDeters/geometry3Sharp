using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using g3;
using geometry3Sharp.Tests;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Property tests of ExactMeshBoolean on solid families beyond boxes and tetrahedra (Exact Boolean 3.0 testing plan, #1038,
    /// phase 1): procedural solids with exact rotations (G4), near-degenerate slivers (G5), multi-component shells that touch
    /// at a face, an edge or a vertex (G6), open inputs (G7) and a range of 2^k magnitudes (G8). Properties: algebraic laws,
    /// determinism serial against parallel, the n-ary entry point against the pairwise chain, reuse, and the status contract.
    /// Oracles are BooleanOracle's (winding numbers, resolution-aware solid comparison). CsCheck campaigns through
    /// CampaignRunner, so G3_PROPERTY_SAMPLES and G3_PROPERTY_REPLAY work as for ExactBooleanPropertyTests (Apache-2.0, net10.0).
    /// </summary>
    public class ExactBooleanFamiliesPropertyTests
    {
        readonly ITestOutputHelper log;
        public ExactBooleanFamiliesPropertyTests(ITestOutputHelper log) { this.log = log; }

        // ---------------------------------------------------------------- helpers

        /// <summary> one boolean; the first error of the sequence is kept in error </summary>
        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, ref string error)
        {
            DMesh3 r = BooleanOracle.Run(a, b, op, out string err);
            if (error == "" && err != "") error = err;
            return r;
        }

        /// <summary> exact rotation by 90 degrees about z: (x, y, z) -> (-y, x, z), determinant +1, integer coordinates stay exact </summary>
        static DMesh3 Rot90(DMesh3 m)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) { Vector3d p = r.GetVertex(v); r.SetVertex(v, new Vector3d(-p.y, p.x, p.z)); }
            return r;
        }

        /// <summary> the same vertices and triangles in the same order: a bit-for-bit comparison of two results </summary>
        static string SameBits(DMesh3 x, DMesh3 y, string what)
        {
            if (x.VertexCount != y.VertexCount || x.TriangleCount != y.TriangleCount)
                return $"{what}: {x.VertexCount}/{x.TriangleCount} against {y.VertexCount}/{y.TriangleCount}";
            var xv = x.VertexIndices().ToList(); var yv = y.VertexIndices().ToList();
            for (int i = 0; i < xv.Count; i++)
                if (x.GetVertex(xv[i]) != y.GetVertex(yv[i])) return $"{what}: vertex {i} differs";
            var xt = x.TriangleIndices().ToList(); var yt = y.TriangleIndices().ToList();
            for (int i = 0; i < xt.Count; i++)
                if (x.GetTriangle(xt[i]) != y.GetTriangle(yt[i])) return $"{what}: triangle {i} differs";
            return "";
        }

        static string Prefix(string what, string reason) => reason == "" ? "" : $"{what}: {reason}";

        static DMesh3 Append(DMesh3 a, DMesh3 b) { var r = new DMesh3(a); MeshEditor.Append(r, b); return r; }

        // ---------------------------------------------------------------- generators

        /// <summary> a box with integer corners in [0, range]^3, extents at least 1 </summary>
        static Gen<IntBox> GenBox(int range) =>
            Gen.Select(GenInterval(range), GenInterval(range), GenInterval(range),
                (x, y, z) => new IntBox(x.lo, y.lo, z.lo, x.hi, y.hi, z.hi));

        static Gen<(int lo, int hi)> GenInterval(int range) =>
            Gen.Int[0, range - 1].SelectMany(lo => Gen.Int[1, range - lo].Select(ext => (lo, lo + ext)));

        /// <summary> G4: a box, optionally subdivided once, optionally rotated by a multiple of 90 degrees (exact) </summary>
        static Gen<DMesh3> GenProcedural =>
            Gen.Select(GenBox(4), Gen.Int[0, 1], Gen.Int[0, 3], (box, levels, quarter) => {
                DMesh3 m = BooleanOracle.Subdivided(BooleanOracle.BoxMesh(box, new BoxCase(box, box, 0, Vector3d.Zero)), levels);
                for (int i = 0; i < quarter; i++) m = Rot90(m);
                return m;
            });

        /// <summary> G5: a box whose corner slivers are split by 2^-tExponent offsets (BooleanOracle.SliverSplit) </summary>
        static Gen<DMesh3> GenSliver =>
            Gen.Select(GenBox(3), Gen.Int[1, 4], Gen.Int[10, 30], Gen.Int[0, 1000], (box, count, tExp, seed) =>
                BooleanOracle.SliverSplit(BooleanOracle.BoxMesh(box, new BoxCase(box, box, 0, Vector3d.Zero)), count, tExp, seed));

        /// <summary> G6: two boxes that touch at a face, an edge or a vertex (one shell each side), as one mesh with two components </summary>
        /// mode 0: the second box starts at the first one's +x face (shared face); 1: at its +x +y edge (shared edge);
        /// 2: at its +x +y +z corner (shared vertex). Extents are the second box's own, 1 to 3 units.
        static Gen<(DMesh3 a, DMesh3 b)> GenTouching =>
            Gen.Select(GenBox(3), Gen.Int[0, 2], Gen.Int[1, 3], Gen.Int[1, 3], Gen.Int[1, 3], (box, mode, ex, ey, ez) => {
                int x0 = box.X1, y0 = mode >= 1 ? box.Y1 : box.Y0, z0 = mode == 2 ? box.Z1 : box.Z0;
                var second = new IntBox(x0, y0, z0, x0 + ex, y0 + ey, z0 + ez);
                DMesh3 a = BooleanOracle.BoxMesh(box, new BoxCase(box, box, 0, Vector3d.Zero));
                DMesh3 b = BooleanOracle.BoxMesh(second, new BoxCase(second, second, 0, Vector3d.Zero));
                return (a, b);
            });

        /// <summary> G7: a box with one triangle removed: an open input (boundary edges) </summary>
        static Gen<DMesh3> GenOpen =>
            GenBox(3).Select(box => {
                DMesh3 m = BooleanOracle.BoxMesh(box, new BoxCase(box, box, 0, Vector3d.Zero));
                m.RemoveTriangle(m.TriangleIndices().First());
                return m;
            });

        /// <summary> G8: a box scaled by 2^k with k in the supported range of the boolean's rounding stage (QA.md 2.5a) </summary>
        static Gen<(IntBox box, int k)> GenMagnitude =>
            Gen.Select(GenBox(3), Gen.Int[-150, 150], (box, k) => (box, k));

        // ---------------------------------------------------------------- properties

        /// <summary> algebraic laws: A|A and A&amp;A are A, A-A is empty; each checked on the solid, not on the triangles </summary>
        static string AlgebraicLaws(DMesh3 a)
        {
            string e = "";
            DMesh3 uu = Run(a, a, Ops.Union, ref e), ii = Run(a, a, Ops.Intersection, ref e), dd = Run(a, a, Ops.Difference, ref e);
            if (e != "") return e;
            double size = 4 * BooleanOracle.EdgeLength(a);   // a length scale, as the metamorphic tests use (c.Scale * 4); not a resolution
            return First(
                () => Prefix("union with itself", BooleanOracle.SameRegularizedSolid(uu, a, size)),
                () => Prefix("intersection with itself", BooleanOracle.SameRegularizedSolid(ii, a, size)),
                () => Prefix("difference with itself", BooleanOracle.Volume(dd) > BooleanOracle.VolumeTolerance(a, a) ? $"volume {BooleanOracle.Volume(dd)} is not empty" : ""));
        }

        /// <summary> two operands: V(A|B) + V(A&amp;B) = V(A) + V(B), V(A-B) + V(A&amp;B) = V(A) and V(B-A) + V(A&amp;B) = V(B) (the laws of one operand
        /// cannot tell an intersection from a union: A&amp;A = A|A); each result also closed and manifold-valid through BooleanOracle.Run </summary>
        static string InclusionExclusion((DMesh3 a, DMesh3 b) x)
        {
            DMesh3 a = x.a, b = x.b;
            string e = "";
            DMesh3 u = Run(a, b, Ops.Union, ref e), i = Run(a, b, Ops.Intersection, ref e);
            DMesh3 d = Run(a, b, Ops.Difference, ref e), d2 = Run(b, a, Ops.Difference, ref e);
            if (e != "") return e;
            double tol = 4 * BooleanOracle.VolumeTolerance(a, b);
            double va = BooleanOracle.Volume(a), vb = BooleanOracle.Volume(b), vi = BooleanOracle.Volume(i);
            return First(
                () => Math.Abs(BooleanOracle.Volume(u) + vi - va - vb) > tol ? $"inclusion-exclusion: {BooleanOracle.Volume(u)} + {vi} against {va} + {vb} (tolerance {tol})" : "",
                () => Math.Abs(BooleanOracle.Volume(d) + vi - va) > tol ? $"A-B: {BooleanOracle.Volume(d)} + {vi} against {va} (tolerance {tol})" : "",
                () => Math.Abs(BooleanOracle.Volume(d2) + vi - vb) > tol ? $"B-A: {BooleanOracle.Volume(d2)} + {vi} against {vb} (tolerance {tol})" : "");
        }

        /// <summary> determinism: one computation serial and parallel gives the same bits (serial equals parallel, #774) </summary>
        static string SerialEqualsParallel(DMesh3 a, DMesh3 b)
        {
            string e = "";
            bool serial = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                DMesh3 s = Run(a, b, Ops.Union, ref e);
                gParallel.SerialOnThisThread = false;
                DMesh3 p = Run(a, b, Ops.Union, ref e);
                if (e != "") return e;
                return SameBits(s, p, "union serial against parallel");
            } finally { gParallel.SerialOnThisThread = serial; }
        }

        /// <summary> reuse: the same instance computed twice gives the same bits </summary>
        static string ReuseIsIdentical(DMesh3 a, DMesh3 b)
        {
            var op = new ExactMeshBoolean(a, b, Ops.Union);
            if (!op.Compute()) return "first Compute() failed: " + op.Error;
            DMesh3 first = new DMesh3(op.Result);
            if (!op.Compute()) return "second Compute() failed: " + op.Error;
            return SameBits(first, op.Result, "second Compute() of the same instance");
        }

        /// <summary> n-ary entry point against the chain of pairwise unions, on the solid </summary>
        static string NaryAgainstChain(DMesh3 a, DMesh3 b, DMesh3 c)
        {
            string e = "";
            DMesh3 nary = ExactMeshBoolean.Union(new[] { a, b, c });
            DMesh3 chain = Run(Run(a, b, Ops.Union, ref e), c, Ops.Union, ref e);
            if (e != "") return e;
            double size = 4 * Math.Max(BooleanOracle.EdgeLength(a), Math.Max(BooleanOracle.EdgeLength(b), BooleanOracle.EdgeLength(c)));
            return BooleanOracle.SameRegularizedSolid(nary, chain, size);
        }

        /// <summary> status contract: a success on closed inputs gives a closed, manifold and clean result </summary>
        static string StatusContract(DMesh3 a, DMesh3 b, Ops op)
        {
            var o = new ExactMeshBoolean(a, b, op);
            bool ok = o.Compute();
            if (!ok) return $"{op}: Compute() false on closed inputs: {o.Error}";
            return Prefix(op.ToString(), BooleanOracle.CleanReason(o.Result));
        }

        /// <summary> magnitudes: the result of a box scaled by 2^k is the result of the unscaled box scaled by 2^k, bit for bit </summary>
        static string MagnitudeScaling((IntBox box, int k) x)
        {
            var unit = new BoxCase(x.box, x.box, 0, Vector3d.Zero);
            var scaled = new BoxCase(x.box, x.box, x.k, Vector3d.Zero);
            DMesh3 a = BooleanOracle.BoxMesh(x.box, unit), b = BooleanOracle.BoxMesh(new IntBox(x.box.X0 + 1, x.box.Y0, x.box.Z0, x.box.X1 + 1, x.box.Y1, x.box.Z1), unit);
            DMesh3 sa = BooleanOracle.BoxMesh(x.box, scaled), sb = BooleanOracle.BoxMesh(new IntBox(x.box.X0 + 1, x.box.Y0, x.box.Z0, x.box.X1 + 1, x.box.Y1, x.box.Z1), scaled);
            string e = "";
            DMesh3 r = Run(a, b, Ops.Union, ref e), sr = Run(sa, sb, Ops.Union, ref e);
            if (e != "") return e;
            double s = Math.Pow(2, x.k);
            DMesh3 back = new DMesh3(sr);
            foreach (int v in back.VertexIndices()) back.SetVertex(v, back.GetVertex(v) / s);
            return SameBits(back, r, $"union at 2^{x.k} against the unscaled union");
        }

        static string First(params Func<string>[] checks)
        {
            foreach (var c in checks) { string r = c(); if (r != "") return r; }
            return "";
        }

        // ---------------------------------------------------------------- campaigns (opt-in sample counts, default small)

        [Fact]
        public void ProceduralAlgebraicLaws() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "ProceduralAlgebraicLaws", GenProcedural, AlgebraicLaws, samples: 40, campaign: 1101);

        [Fact]
        public void SliverAlgebraicLaws() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "SliverAlgebraicLaws", GenSliver, AlgebraicLaws, samples: 30, campaign: 1102);

        [Fact]
        public void ProceduralInclusionExclusion() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "ProceduralInclusionExclusion", Gen.Select(GenProcedural, GenProcedural, (a, b) => (a, b)), InclusionExclusion, samples: 40, campaign: 1111);

        [Fact]
        public void SliverInclusionExclusion() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "SliverInclusionExclusion", Gen.Select(GenSliver, GenProcedural, (a, b) => (a, b)), InclusionExclusion, samples: 30, campaign: 1112);

        [Fact]
        public void TouchingComponentsAreDeterministic() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "TouchingComponentsAreDeterministic", GenTouching,
                x => SerialEqualsParallel(x.a, x.b), samples: 25, campaign: 1103);

        [Fact]
        public void ProceduralReuseIsIdentical() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "ProceduralReuseIsIdentical", GenProcedural.Select(a => (a, Rot90(a))),
                x => ReuseIsIdentical(x.Item1, x.Item2), samples: 25, campaign: 1104);

        [Fact]
        public void NaryMatchesChain() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "NaryMatchesChain",
                GenProcedural.Select(a => (a, BooleanOracle.Subdivided(a, 0), Rot90(a))),
                x => NaryAgainstChain(x.Item1, x.Item2, x.Item3), samples: 20, campaign: 1105);

        [Fact]
        public void TouchingStatusContract() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "TouchingStatusContract", GenTouching,
                x => First(() => StatusContract(x.a, x.b, Ops.Union), () => StatusContract(x.a, x.b, Ops.Difference)), samples: 25, campaign: 1106);

        [Fact]
        public void MagnitudeScalingIsBitExact() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "MagnitudeScalingIsBitExact", GenMagnitude, MagnitudeScaling, samples: 30, campaign: 1107);

        /// negative control: a result moved by 1e-3 of the edge length must be rejected by the same oracle, so the
        /// properties above can fail; a comparison that accepts everything would make them vacuous
        [Fact]
        public void OracleRejectsAShiftedResult()
        {
            DMesh3 a = BooleanOracle.Subdivided(BooleanOracle.BoxMesh(new IntBox(0, 0, 0, 2, 1, 1), new BoxCase(new IntBox(0, 0, 0, 2, 1, 1), new IntBox(0, 0, 0, 2, 1, 1), 0, Vector3d.Zero)), 1);
            DMesh3 shifted = new DMesh3(a);
            double e = BooleanOracle.EdgeLength(a);
            foreach (int v in shifted.VertexIndices()) shifted.SetVertex(v, shifted.GetVertex(v) + new Vector3d(1e-3 * e, 0, 0));
            Assert.Equal("", BooleanOracle.SameRegularizedSolid(a, a, 4 * e));
            Assert.NotEqual("", BooleanOracle.SameRegularizedSolid(shifted, a, 4 * e));
        }

        [Fact]
        public void OpenInputsDoNotThrow() =>
            CampaignRunner.Run(log, nameof(ExactBooleanFamiliesPropertyTests), "OpenInputsDoNotThrow", GenOpen,
                a => { string e = ""; Run(a, BooleanOracle.Subdivided(a, 0), Ops.Union, ref e); return ""; }, samples: 25, campaign: 1108);
    }
}
