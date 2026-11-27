using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Metamorphic relations of ExactMeshBoolean and SolidBooleanPipeline on curved and rotated solids (spheres, tori,
    /// rotated boxes; overlapping, nested, touching, near-coincident and identical pairs). Complements
    /// ExactBooleanMetamorphicTests (dyadic boxes and tetrahedra, exact volumes): here the coordinates are not dyadic, the
    /// volumes are compared within the rounding bound that was measured (MeshRoundingRepair.MaxDrift, checked against the
    /// documented 1e-5 times the diagonal), and the relations hold without knowing the right answer:
    /// commutativity (same solid, same clean state), inclusion-exclusion, A u A = A, A - A empty, refinement of an input by midpoint
    /// splits, and the documented reaction to an inverted input. A relation is not asserted where the documentation does not
    /// promise it (the same mesh for the swapped operands).
    /// </summary>
    public class BooleanMetamorphicCurvedTests
    {
        sealed class Outcome
        {
            public string Status = "";
            public DMesh3 Mesh;
            public string Unclean = "";
            public double Drift;
            public double Volume;
            public bool Ok => Status == "Success";
        }

        static Outcome Exact(DMesh3 a, DMesh3 b, Ops op, ExactMeshBoolean.OrientationModes orientation = ExactMeshBoolean.OrientationModes.Refuse)
        {
            var boolean = new ExactMeshBoolean(a, b, op) { Orientation = orientation };
            bool ok = boolean.Compute();
            return Make(ok ? boolean.Status.ToString() : "failed " + boolean.Status, boolean.Result, boolean);
        }

        static Outcome Pipeline(DMesh3 a, DMesh3 b, Ops op, bool allowInsideOut = false)
        {
            ExactMeshBoolean last = null;
            var pipeline = new SolidBooleanPipeline(a, b, op) { AllowInsideOutInputs = allowInsideOut, ConfigureBoolean = x => last = x };
            pipeline.Compute();
            return Make(pipeline.Status.ToString(), pipeline.Result, last);
        }

        static Outcome Make(string status, DMesh3 mesh, ExactMeshBoolean boolean)
        {
            var o = new Outcome { Status = status, Mesh = mesh, Drift = boolean?.Rounding?.MaxDrift ?? 0 };
            if (o.Ok && mesh != null) {
                o.Unclean = BooleanOracle.CleanReason(mesh);
                o.Volume = mesh.TriangleCount == 0 ? 0 : BooleanOracle.Volume(mesh);
            }
            return o;
        }

        static readonly Func<DMesh3, DMesh3, Ops, Outcome>[] Engines = { (a, b, op) => Exact(a, b, op), (a, b, op) => Pipeline(a, b, op) };
        public static IEnumerable<object[]> EngineNames() { yield return new object[] { 0 }; yield return new object[] { 1 }; }
        static string EngineName(int e) => e == 0 ? "ExactMeshBoolean" : "SolidBooleanPipeline";

        /// <summary> pairs x engines </summary>
        public static IEnumerable<object[]> PairsAndEngines() =>
            CurvedSolids.Pairs.Keys.SelectMany(k => new[] { 0, 1 }.Select(e => new object[] { k, e }));

        public static IEnumerable<object[]> PairNamesOnly() => CurvedSolids.PairNames();

        static double Area(DMesh3 m) => m == null ? 0 : Invariance.Area(m);

        /// <summary>
        /// Tolerance of a volume identity over results: every vertex of a result may be off by the rounding of its exact
        /// point plus the measured drift of the rounding repair (itself at most 1e-5 times the diagonal, asserted), so a
        /// volume is off by at most that times its area.
        /// </summary>
        static double Tol(DMesh3 inputs0, DMesh3 inputs1, params Outcome[] results)
        {
            double e = 8 * BooleanOracle.Resolution(inputs0, inputs1) + results.Sum(r => r.Drift);
            double area = Area(inputs0) + Area(inputs1) + results.Sum(r => Area(r.Mesh));
            return e * area + 1e-12 * (Math.Abs(BooleanOracle.Volume(inputs0)) + Math.Abs(BooleanOracle.Volume(inputs1)));
        }

        static void AssertSuccess(Outcome o, string what, bool general)
        {
            Assert.True(o.Ok, $"{what}: status {o.Status}");
            if (general) Assert.True(o.Unclean == "", $"{what}: not clean: {o.Unclean}");
            Assert.True(o.Drift <= BooleanOracle.SafeRoundingBound(o.Mesh) * 1.0000001 + 1e-300 || o.Mesh.TriangleCount == 0, $"{what}: drift {o.Drift:G3} above the documented bound");
        }

        // ---------------------------------------------------------------- commutativity

        [Theory, MemberData(nameof(PairsAndEngines))]
        public void Commutativity_SameSolid_SameState(string name, int engine)
        {
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                Outcome ab = Engines[engine](p.A, p.B, op), ba = Engines[engine](p.B, p.A, op);
                string what = $"{EngineName(engine)} {name} {op}";
                Assert.True(ab.Status == ba.Status, $"{what}: status {ab.Status} vs {ba.Status}");
                if (p.General) Assert.True(ab.Ok, $"{what}: status {ab.Status}");
                if (!ab.Ok) continue;
                Assert.True((ab.Unclean == "") == (ba.Unclean == ""), $"{what}: clean state differs: '{ab.Unclean}' vs '{ba.Unclean}'");
                if (p.General) { AssertSuccess(ab, what + " A,B", true); AssertSuccess(ba, what + " B,A", true); }
                double tol = Tol(p.A, p.B, ab, ba);
                Assert.True(Math.Abs(ab.Volume - ba.Volume) <= tol, $"{what}: volume {ab.Volume:R} vs {ba.Volume:R} (tolerance {tol:G3})");
            }
        }

        // ---------------------------------------------------------------- inclusion-exclusion

        [Theory, MemberData(nameof(PairsAndEngines))]
        public void InclusionExclusion_VolumesAdd(string name, int engine)
        {
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            string what = $"{EngineName(engine)} {name}";
            Outcome u = Engines[engine](p.A, p.B, Ops.Union), i = Engines[engine](p.A, p.B, Ops.Intersection);
            Outcome dab = Engines[engine](p.A, p.B, Ops.Difference), dba = Engines[engine](p.B, p.A, Ops.Difference);
            foreach (Outcome o in new[] { u, i, dab, dba }) Assert.True(o.Ok, $"{what}: status {o.Status}");
            if (p.General) foreach (Outcome o in new[] { u, i, dab, dba }) Assert.True(o.Unclean == "", $"{what}: not clean: {o.Unclean}");
            double va = BooleanOracle.Volume(p.A), vb = BooleanOracle.Volume(p.B);
            double tol = Tol(p.A, p.B, u, i, dab, dba);
            Assert.True(Math.Abs(u.Volume - (va + vb - i.Volume)) <= tol, $"{what}: vol(AuB) {u.Volume:R} vs vol A + vol B - vol(AnB) {va + vb - i.Volume:R} (tolerance {tol:G3})");
            Assert.True(Math.Abs(dab.Volume + i.Volume - va) <= tol, $"{what}: vol(A-B) + vol(AnB) {dab.Volume + i.Volume:R} vs vol A {va:R} (tolerance {tol:G3})");
            Assert.True(Math.Abs(dba.Volume + i.Volume - vb) <= tol, $"{what}: vol(B-A) + vol(AnB) {dba.Volume + i.Volume:R} vs vol B {vb:R} (tolerance {tol:G3})");
        }

        // ---------------------------------------------------------------- idempotence

        [Theory, MemberData(nameof(EngineNames))]
        public void Idempotence_AuA_IsA_AminusA_IsEmpty(int engine)
        {
            foreach (string name in new[] { "sphere overlap", "torus sphere in hole", "box rotated box" }) {
                DMesh3 a = CurvedSolids.Pairs[name].A;
                string what = $"{EngineName(engine)} {name}";
                double va = BooleanOracle.Volume(a);
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                    Outcome o = Engines[engine](a, new DMesh3(a), op);
                    Assert.True(o.Ok, $"{what} {op}: status {o.Status}");
                    Assert.True(Math.Abs(o.Volume - va) <= Tol(a, a, o), $"{what} {op}: volume {o.Volume:R} vs {va:R}");
                    Assert.True(o.Unclean == "", $"{what} {op}: not clean: {o.Unclean}");
                }
                Outcome d = Engines[engine](a, new DMesh3(a), Ops.Difference);
                Assert.True(d.Ok, $"{what} A-A: status {d.Status}");
                Assert.True(d.Mesh.TriangleCount == 0 || Math.Abs(d.Volume) <= Tol(a, a, d), $"{what} A-A: not empty (volume {d.Volume:R}, {d.Mesh.TriangleCount} triangles)");
            }
        }

        // ---------------------------------------------------------------- refinement

        static IEnumerable<(string name, Func<DMesh3, DMesh3> refine)> Refinements()
        {
            yield return ("midpoint 1-to-4", m => BooleanOracle.Subdivided(m, 1));
            yield return ("edge midpoints seed 7", m => CurvedSolids.EdgeMidpointsSplit(m, 7));
        }

        [Theory, MemberData(nameof(PairsAndEngines))]
        public void Refinement_MidpointSplitsOfAnInput_KeepVolumeAndState(string name, int engine)
        {
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            foreach (Ops op in BooleanOracle.AllOps) {
                Outcome baseline = Engines[engine](p.A, p.B, op);
                foreach (var (rname, refine) in Refinements())
                    for (int operand = 0; operand < 3; ++operand) {
                        DMesh3 a = operand != 1 ? refine(p.A) : p.A, b = operand != 0 ? refine(p.B) : p.B;
                        Outcome r = Engines[engine](a, b, op);
                        string what = $"{EngineName(engine)} {name} {op} {rname} operand {"ABX"[operand]}";
                        Assert.True(baseline.Status == r.Status, $"{what}: status {baseline.Status} vs {r.Status}");
                        if (p.General) Assert.True(baseline.Ok, $"{what}: baseline status {baseline.Status}");
                        if (!baseline.Ok) continue;
                        Assert.True((baseline.Unclean == "") == (r.Unclean == ""), $"{what}: clean state '{baseline.Unclean}' vs '{r.Unclean}'");
                        double tol = Tol(a, b, baseline, r);
                        Assert.True(Math.Abs(baseline.Volume - r.Volume) <= tol, $"{what}: volume {baseline.Volume:R} vs {r.Volume:R} (tolerance {tol:G3})");
                    }
            }
        }

        // ---------------------------------------------------------------- orientation

        [Theory, MemberData(nameof(EngineNames))]
        public void InvertedInput_IsRefusedOrDocumented_NeverASilentWrongVolume(int engine)
        {
            foreach (string name in new[] { "sphere overlap", "torus linked torus", "box sphere" }) {
                CurvedSolids.Pair p = CurvedSolids.Pairs[name];
                foreach (Ops op in BooleanOracle.AllOps)
                    foreach (bool invertA in new[] { true, false }) {
                        DMesh3 a = invertA ? CurvedSolids.Inverted(p.A) : p.A, b = invertA ? p.B : CurvedSolids.Inverted(p.B);
                        Outcome o = Engines[engine](a, b, op);
                        string what = $"{EngineName(engine)} {name} {op} inverted {(invertA ? "A" : "B")}";
                        if (!o.Ok) {   // refused: only the documented refusals (InsideOutInput for the pipeline, InconsistentOrientation)
                            Assert.True(o.Status == "InsideOutInput" || o.Status.EndsWith("InconsistentOrientation"), $"{what}: refused with {o.Status}, not a documented refusal");
                            continue;
                        }
                        // not refused: the inside-out input is an empty solid ("used as given"): the result is the Boolean with that input empty
                        double va = invertA ? 0 : BooleanOracle.Volume(p.A), vb = invertA ? BooleanOracle.Volume(p.B) : 0;
                        double expected = op == Ops.Union ? va + vb : op == Ops.Intersection ? 0 : va;
                        Assert.True(Math.Abs(o.Volume - expected) <= Tol(p.A, p.B, o), $"{what}: Success with volume {o.Volume:R}; an inside-out input is an empty solid, expected {expected:R}");
                    }
            }
        }

        // exact engine only: Orientation is a property of ExactMeshBoolean
        [Theory, MemberData(nameof(PairNamesOnly))]
        public void InvertedInput_ByNesting_GivesTheResultOfTheOutwardInput(string name)
        {
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            foreach (Ops op in BooleanOracle.AllOps) {
                Outcome baseline = Exact(p.A, p.B, op);
                Outcome r = Exact(CurvedSolids.Inverted(p.A), CurvedSolids.Inverted(p.B), op, ExactMeshBoolean.OrientationModes.ByNesting);
                string what = $"{name} {op} both inverted, ByNesting";
                Assert.True(baseline.Status == r.Status, $"{what}: status {baseline.Status} vs {r.Status}");
                if (p.General) Assert.True(baseline.Ok, $"{what}: baseline status {baseline.Status}");
                if (!baseline.Ok) continue;
                Assert.True(Math.Abs(baseline.Volume - r.Volume) <= Tol(p.A, p.B, baseline, r), $"{what}: volume {baseline.Volume:R} vs {r.Volume:R}");
            }
        }
    }
}
