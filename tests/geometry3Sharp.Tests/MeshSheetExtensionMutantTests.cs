using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests.SheetExt;
using Xunit;
using Status = gs.MeshSheetExtension.ExtensionStatus;
using Rules = gs.MeshSheetExtension.Rules;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The hand mutants of the design's section 8 (docs/design/SHEET_EXTENSION.md): each refusal rule switched off, the first exit replaced by the last, the overshoot rule, the
    /// choice by edge replaced by the choice by vertex, the subdivision of the outer edge, the orientation of the quads. The library has the internal switches of these
    /// mutants (for tests, like ExactMeshImprint.SliverCleanup); a mutant is dead when a case that ends as the design says without it ends otherwise with it. Every case has the exact
    /// status the library gives (not just one of the reasons the design allows), so that a rule that is redundant with a later one is still seen by the reason.
    /// </summary>
    public class MeshSheetExtensionMutantTests
    {
        sealed class Probe
        {
            public string Case;
            public Status Expected;
            public Action<MeshSheetExtension> Options;
            public Probe(string c, Status expected, Action<MeshSheetExtension> options = null) { Case = c; Expected = expected; Options = options; }
        }

        sealed class Mutant
        {
            public string Name;
            public Action<MeshSheetExtension> Apply;
            public Probe[] Probes;
        }

        static readonly Probe SlotAndFin = new Probe("slot and fin", Status.Success);
        static readonly Probe FreePin = new Probe("free pin in the slot", Status.CurveRule);

        static readonly Mutant[] Mutants = {
            new Mutant { Name = "depth rule off", Apply = o => o.Verify &= ~Rules.Depth,
                Probes = new[] { new Probe("planar 8 %", Status.DepthAboveLimit), new Probe("bent 8 %", Status.DepthAboveLimit), new Probe("slit ends in the middle", Status.DepthAboveLimit) } },
            new Mutant { Name = "grazing rule off", Apply = o => o.Verify &= ~Rules.Grazing, Probes = new[] { new Probe("exit at 3 degrees", Status.GrazingExit) } },
            new Mutant { Name = "new-point test off", Apply = o => o.Verify &= ~Rules.NewPointOutside,
                Probes = new[] { new Probe("ray grazes a reflex edge", Status.NewPointNotOutside), new Probe("sheet in the plane of a step face", Status.NewPointNotOutside) } },
            new Mutant { Name = "inverted-quad test off", Apply = o => o.Verify &= ~Rules.InvertedQuad,
                Probes = new[] { new Probe("notch narrower than the depth", Status.InvertedQuad), new Probe("rib on the slot wall (no way round)", Status.InvertedQuad) } },
            // the outer-edge test is redundant with the curve rule and the sheet role at the depth the cases have; a strip whose outer edge cannot be got out (no subdivision allowed) shows it
            new Mutant { Name = "outer-edge test off", Apply = o => o.Verify &= ~Rules.OuterEdge,
                Probes = new[] { new Probe("chord across a sphere", Status.OuterEdgeInside, o => o.MaxSubdivisionDepth = 0), new Probe("planar 0.5 %", Status.OuterEdgeInside, o => o.MaxSubdivisionDepth = 0) } },
            new Mutant { Name = "curve rule off", Apply = o => o.Verify &= ~Rules.CurveRule, Probes = new[] { FreePin } },
            new Mutant { Name = "coplanar test off", Apply = o => o.Verify &= ~Rules.CoplanarContact, Probes = new[] { new Probe("free pin whose top lies in the strip's plane", Status.CoplanarContact) } },
            new Mutant { Name = "strip against the sheet and itself off", Apply = o => o.Verify &= ~Rules.MeetsSheet, Probes = new[] { new Probe("folded sheet, strips converge", Status.MeetsItself) } },
            new Mutant { Name = "strip against other tools off", Apply = o => o.Verify &= ~Rules.MeetsOtherTool, Probes = new[] { new Probe("strip crosses another sheet", Status.MeetsOtherTool) } },
            new Mutant { Name = "last exit instead of first", Apply = o => o.UseLastExit = true,
                Probes = new[] { SlotAndFin, new Probe("wide pocket (concave, re-entry)", Status.Success), new Probe("thick pin touching the outer edge (routed round)", Status.Success), new Probe("ray grazes a reflex edge", Status.NewPointNotOutside) } },
            new Mutant { Name = "fixed overshoot instead of the adaptive rule", Apply = o => o.FixedOvershoot = true,
                Probes = new[] { SlotAndFin, FreePin, new Probe("thick pin touching the outer edge (routed round)", Status.Success) } },
            new Mutant { Name = "choice by vertex instead of by edge", Apply = o => o.ChooseByVertex = true, Probes = new[] { new Probe("chord across a sphere", Status.Success) } },
            new Mutant { Name = "no subdivision of the outer edge", Apply = o => o.NoSubdivision = true,
                Probes = new[] { new Probe("planar 0.5 %", Status.Success), SlotAndFin, new Probe("chord across a sphere", Status.Success) } },
            new Mutant { Name = "quads oriented against the sheet", Apply = o => o.FlipQuads = true,
                Probes = new[] { new Probe("planar 0.1 %", Status.Success), new Probe("chord across a sphere", Status.Success), SlotAndFin } },
            new Mutant { Name = "last exit and curve rule off", Apply = o => { o.UseLastExit = true; o.Verify &= ~Rules.CurveRule; }, Probes = new[] { SlotAndFin, FreePin } },
            new Mutant { Name = "new-point test and curve rule off", Apply = o => o.Verify &= ~(Rules.NewPointOutside | Rules.CurveRule), Probes = new[] { FreePin } },
        };

        public static IEnumerable<object[]> MutantNames() => Mutants.Select(m => new object[] { m.Name });

        static bool Matches(SheetRun r, Probe p)
        {
            if (r.Status != p.Expected) return false;
            return p.Expected != Status.Success || SheetRunner.Verdict(r) == null;
        }

        [Theory]
        [MemberData(nameof(MutantNames))]
        public void EveryMutant_IsKilled_ByTheCasesThatEndAsDesignedWithoutIt(string name)
        {
            Mutant m = Mutants.First(x => x.Name == name);
            foreach (Probe p in m.Probes) {
                SheetCase c = MeshSheetExtensionTests.Get(p.Case);
                SheetRun good = SheetRunner.Run(c, null, p.Options);
                Assert.True(Matches(good, p), name + ": the case '" + p.Case + "' does not end as expected without the mutant: " + good.Actual + " " + good.Report.Witness + " " + SheetRunner.Verdict(good));
                SheetRun bad = SheetRunner.Run(c, null, o => { p.Options?.Invoke(o); m.Apply(o); });
                Assert.False(Matches(bad, p), name + ": the mutant is alive on '" + p.Case + "': " + bad.Actual);
            }
        }

        [Fact]
        public void LastExit_WithoutTheCurveRule_ReproducesTheFinTopCutOff_OfTheDesignsSectionEleven()
        {
            // the first design cut a strip through the fin and kept 491 instead of 506; the first exit with the curve rule is what prevents it, and only both together
            SheetCase c = MeshSheetExtensionTests.Get("slot and fin");
            SheetRun r = SheetRunner.Run(c, null, o => { o.UseLastExit = true; o.Verify &= ~Rules.CurveRule; });
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(491, r.Kept, 6);
            Assert.NotNull(SheetRunner.Verdict(r));
        }

        [Fact]
        public void LastExit_AndTheMissingCurveRule_AreEachNotEnoughToCutWrong()
        {
            // the last exit alone is refused by the curve rule, and the first exit needs no curve rule on this case (the curve rule is what the free pin needs)
            SheetCase c = MeshSheetExtensionTests.Get("slot and fin");
            Assert.Equal(Status.CurveRule, SheetRunner.Run(c, null, o => o.UseLastExit = true).Status);
            Assert.Equal(506, SheetRunner.Run(c, null, o => o.Verify &= ~Rules.CurveRule).Kept, 6);
        }

        [Fact]
        public void WithoutTheCurveRule_TheStripPiercesTheFreePin_AndTheCutIsAccepted()
        {
            SheetRun r = SheetRunner.Run(MeshSheetExtensionTests.Get("free pin in the slot"), null, o => o.Verify &= ~Rules.CurveRule);
            Assert.Equal(Status.Success, r.Status);
            Assert.Equal(506.015, r.Kept, 6);
            Assert.NotNull(SheetRunner.Verdict(r));
        }

        [Fact]
        public void TheDefaultSwitches_AreTheDesign()
        {
            var op = new MeshSheetExtension();
            Assert.Equal(Rules.All, op.Verify);
            Assert.False(op.ChooseByVertex || op.UseLastExit || op.FixedOvershoot || op.NoSubdivision || op.FlipQuads);
            Assert.Equal(0.05, op.MaxExtension);
            Assert.True(op.VerifyWithSheetRole);
        }
    }
}
