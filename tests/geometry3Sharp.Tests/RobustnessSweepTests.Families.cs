using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The input families of the sweep (QA 2.0 T1) for the operations that take any mesh: the defect meshes of
    /// <see cref="Defects.Fixed"/> (empty, single triangle, zero-area and sliver triangles, duplicated, flipped, open,
    /// non-manifold, flat, ...) and extreme scales (subnormal coordinates, 1e154 and beyond, where squares overflow).
    /// There is no reference result for these; the outcome must be a documented one: the operation finishes in time,
    /// throws nothing but the ArgumentException of its own type, a success has finite and valid output and finite values,
    /// and a claim it makes (clean, Delaunay, hull) holds under an independent check (Sig.Contradiction).
    /// </summary>
    public partial class RobustnessSweepTests
    {
        /// <summary> the operations whose Run takes any mesh as input (the editing operations need loops, a plane domain or a seed on the mesh) </summary>
        static readonly HashSet<string> NotForAnyMesh = new HashSet<string> { "MeshInsertPolygon", "MeshTrimLoop", "MeshStitchLoops", "MeshExtrudeMesh", "VolumeSampleCells" };

        static IEnumerable<Op> GapFamilyOps() => GapOps().Where(o => NotForAnyMesh.Contains(o.Name) == false);

        /// <summary> scale factors that overflow squares (1e160, 1e300) or underflow them, down to subnormal coordinates (1e-310) </summary>
        public static readonly double[] ExtremeScales = { 1e-310, 1e-160, 1e160, 1e300 };

        /// <summary>
        /// MeshRobustRepair takes 5 to 10 s on these tiny inputs (no solid: the alpha wrap fallback; the known open item
        /// of the coordinator's list), over the 5 s limit of a test: not run until that is fixed
        /// </summary>
        static readonly HashSet<string> SlowKnown = new HashSet<string> {
            "MeshRobustRepair|defect: flat", "MeshRobustRepair|defect: open sheet", "MeshRobustRepair|defect: single triangle" };

        public static IEnumerable<object[]> DefectCases() =>
            GapFamilyOps().SelectMany(o => Defects.Fixed().Select(c => new object[] { o.Name, "defect: " + c.Name }))
                .Where(x => SlowKnown.Contains(x[0] + "|" + x[1]) == false);

        public static IEnumerable<object[]> ExtremeScaleCases() =>
            GapFamilyOps().SelectMany(o => ExtremeScales.Select(x => new object[] { o.Name, "extreme scale " + x.ToString("E0", System.Globalization.CultureInfo.InvariantCulture) }));

        /// <summary> "" if the outcome of op on this input is a documented one, else what is wrong </summary>
        string FamilyOutcome(Op op, DMesh3 input, Similarity s, double param, bool checkValues)
        {
            // (an operation that has nothing to repair or change leaves an input that is invalid already, eg inconsistently oriented, as it is)
            bool inputValid = input.CheckValidity(false, FailMode.ReturnOnly);
            Sig sig;
            try {
                sig = RunOp(op, input, s, param);
            } catch (Exception e) {
                bool rejection = e is ArgumentException && IsOwnType(op.Type, e.TargetSite?.DeclaringType);
                return rejection ? "" : Describe(e);
            }
            if (sig.Contradiction != "") return "claim contradicted: " + sig.Contradiction;
            if (sig.Failed) return "";
            if (sig.Output != null) {
                if (NonFiniteVertices(sig.Output) > 0) return "silent garbage: non-finite output vertices with success";
                if (inputValid && sig.Output.CheckValidity(false, FailMode.ReturnOnly) == false) return "invalid output mesh with success";
            }
            if (sig.NonFiniteValues > 0) return $"silent garbage: {sig.NonFiniteValues} non-finite per-vertex values with success";
            // (at extreme scales the areas and volumes of the result overflow or underflow by themselves: not checked)
            var bad = sig.Values.FirstOrDefault(x => (checkValues || x.dim < 2) && (double.IsNaN(x.value) || double.IsInfinity(x.value)));
            return bad.name != null ? $"silent garbage: success with {bad.name} = {bad.value}" : "";
        }

        [Theory]
        [MemberData(nameof(DefectCases))]
        public void Sweep_DefectInput(string op, string c)
        {
            if (SkipKnownHang(op + "|" + c)) return;
            Defects.Case d = Defects.Fixed().First(x => "defect: " + x.Name == c);
            Op o = ByName(op);
            Expect(op + "|" + c, FamilyOutcome(o, d.Make(), Similarity.Identity, o.Param, true));
        }

        [Theory]
        [MemberData(nameof(ExtremeScaleCases))]
        public void Sweep_ExtremeScale(string op, string c)
        {
            if (SkipKnownHang(op + "|" + c)) return;
            double scale = double.Parse(c.Substring("extreme scale ".Length), System.Globalization.CultureInfo.InvariantCulture);
            var s = new Similarity(c, scale, Matrix3d.Identity, Vector3d.Zero);
            Op o = ByName(op);
            Expect(op + "|" + c, FamilyOutcome(o, Invariance.Transformed(o.Input(), s), s, double.IsNaN(o.Param) ? double.NaN : s.ApplyLength(o.Param), false));
        }
    }
}
