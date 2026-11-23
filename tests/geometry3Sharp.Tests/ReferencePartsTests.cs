using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using ReferenceParts;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The reference suite of functional technical parts (benchmarks/ReferenceParts, docs/QA.md): analytic soups with known targets and
    /// deliberate defects, a chain of operations (repair, subtract a cut-out, reduce, STL and OBJ export) and functional checks after every
    /// step. These tests check the parts and the checks (a mutated mesh must fail its check), pin today's results of the chains (a known
    /// failing step is pinned with its measured value, not skipped) and check the invariance of the checks.
    /// </summary>
    public class ReferencePartsTests : IClassFixture<ReferencePartsTests.IdentityChains>
    {
        /// <summary> the chains of the invariance test in the identity frame, run once for all frames (the fixture's time is not a test's) </summary>
        public sealed class IdentityChains
        {
            public readonly ChainResult[] Results = Jobs.Select(j => j.Item2(j.Item1(PartFrame.Identity))).ToArray();
        }

        readonly IdentityChains identity;
        public ReferencePartsTests(IdentityChains identityChains) { identity = identityChains; }

        static PartFrame FrameOf(Similarity s) { return new PartFrame(s.Scale, s.Rotation, s.Translation); }

        static CheckResult Last(ChainResult c, string check) { return c.Steps.Last(s => s.Ran).Check(check); }

        static string Steps(ChainResult c) { return string.Join(",", c.Steps.Select(s => s.Name)); }

        // ---------------- the construction ----------------

        [Fact]
        public void Plate_WithHole_CoversOuterAreaMinusHole()
        {
            var soup = new Soup();
            Vector3d[] hole = Soup.Ring(new Vector3d(1, 2, 3), Vector3d.AxisX, Vector3d.AxisY, 2, 12);
            soup.Plate("p", new[] { new Vector3d(-5, -4, 3), new Vector3d(7, -4, 3), new Vector3d(7, 8, 3), new Vector3d(-5, 8, 3) }, new[] { hole }, Vector3d.AxisZ);
            Assert.Equal(12 * 12 - Soup.PolygonArea(2, 12), soup.Area(), 9);
            for (int t = 0; t < soup.TriangleCount; ++t)
                Assert.True((soup.Corners[3 * t + 1] - soup.Corners[3 * t]).Cross(soup.Corners[3 * t + 2] - soup.Corners[3 * t]).z > 0, "triangle " + t + " is not counter-clockwise from +z");
        }

        public static IEnumerable<object[]> Constructions()
        {
            yield return new object[] { "flange" };
            yield return new object[] { "rib" };
            yield return new object[] { "screw1e-1" };
            yield return new object[] { "screw1e-4" };
        }

        static ReferencePart Build(string name, DefectSet defects, PartFrame frame)
        {
            switch (name) {
                case "flange": return Parts.Flange(24, defects, frame);
                case "rib": return Parts.HousingRib(defects, frame);
                case "screw1e-1": return Parts.ScrewAssembly(1e-1, defects, frame);
                default: return Parts.ScrewAssembly(1e-4, defects, frame);
            }
        }

        [Theory, MemberData(nameof(Constructions))]
        public void WeldedConstruction_IsClosedManifold_WithTheConstructionsVolume(string name)
        {
            ReferencePart part = Build(name, DefectSet.None(), PartFrame.Identity);
            DMesh3 mesh = part.Input;
            Assert.True(mesh.IsClosed(), "not closed");
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly), "not a valid mesh");
            Assert.True(ExactMeshBoolean.IsWindingClosed(mesh));
            Assert.Equal(part.VolumeBefore, Checks.MeshVolume(mesh), 6);
            Assert.Equal(part.Area, Enumerable.Range(0, mesh.TriangleCount).Sum(t => mesh.GetTriArea(t)), 6);
            Assert.True(part.Cutter.IsClosed());
            Assert.True(part.VolumeAfter < part.VolumeBefore);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, true));
        }

        [Fact]
        public void Defects_AreApplied()
        {
            ReferencePart welded = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            ReferencePart soup = Parts.Flange(24, DefectSet.Soup(), PartFrame.Identity);
            Assert.Equal(3 * soup.Input.TriangleCount, soup.Input.VertexCount);
            Assert.True(welded.Input.VertexCount < soup.Input.VertexCount);
            Assert.Equal(welded.Input.TriangleCount, soup.Input.TriangleCount);

            ReferencePart dup = Parts.Flange(24, new DefectSet() { DuplicateEvery = 17 }, PartFrame.Identity);
            Assert.Equal(welded.Input.TriangleCount + welded.Input.TriangleCount / 17, dup.Input.TriangleCount);

            ReferencePart flip = Parts.Flange(24, new DefectSet() { FlipEvery = 13 }, PartFrame.Identity);
            Assert.Equal(welded.Input.TriangleCount, flip.Input.TriangleCount);
            Assert.True(Math.Abs(Checks.MeshVolume(flip.Input) - welded.VolumeBefore) > 0.01 * welded.VolumeBefore, "flipped triangles must change the signed volume");

            // the top plate moves along its normal by the gap fraction of the part size: the bounding box grows by that much
            ReferencePart gap = Parts.Flange(24, new DefectSet() { GapFraction = 1e-3 }, PartFrame.Identity);
            double dz = MeshRepairMetrics.TriangleBounds(gap.Input).Max.z - MeshRepairMetrics.TriangleBounds(welded.Input).Max.z;
            Assert.Equal(1e-3 * welded.Size, dz, 9);
        }

        // ---------------- the checks fail where they must ----------------

        static DMesh3 Scaled(DMesh3 mesh, double factor)
        {
            var copy = new DMesh3(mesh);
            Vector3d c = MeshRepairMetrics.TriangleBounds(copy).Center;
            foreach (int v in copy.VertexIndices()) copy.SetVertex(v, c + factor * (copy.GetVertex(v) - c));
            return copy;
        }

        static CheckResult Run(ReferencePart part, string check, DMesh3 mesh, bool afterCut = false) { return part.Checks.First(c => c.Name == check).Run(part, mesh, afterCut); }

        [Fact]
        public void PinCheck_MeasuresTheInradius_AndFailsAPinThatDoesNotFit()
        {
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            CheckResult r = Run(part, "pinMain", part.Input);
            Assert.True(r.Pass);
            double inradius = part.Targets["boreInradius"];
            // a certified lower bound: never above the construction's clearance, and within the interval resolution of it
            Assert.InRange(r.Value, 0.97 * part.Targets["boreRadius"], inradius + 1e-9);
            ReferencePart.Pin pin = part.Pins.First(p => p.Name == "pinMain");
            double tight = pin.Radius; pin.Radius = part.Targets["boreRadius"];       // the pin as big as the bore: it cannot pass the n-gon
            Assert.False(Run(part, "pinMain", part.Input).Pass);
            pin.Radius = tight;
            // everything 3 % smaller: the bore is too narrow for the same pin
            Assert.False(Run(part, "pinMain", Scaled(part.Input, 0.97)).Pass);
            CheckResult cross = Run(part, "pinCross", part.Input);
            Assert.True(cross.Pass);
            Assert.InRange(cross.Value, 0.97 * part.Targets["crossRadius"], part.Targets["crossInradius"] + 1e-9);
        }

        [Fact]
        public void SegmentClearance_IsALowerBoundBetweenTheSamples()
        {
            // a cube of edge 0.1 at x = 5.3, its nearest face 1 from the line of the segment: the samples at multiples of 1.25 miss the point of
            // the true distance 1 (the sampled distances are 1.03 and more), so only the half-interval shave keeps the value a lower bound
            var cube = new Soup();
            cube.Box("c", new Vector3d(5.25, 1, -0.05), new Vector3d(5.35, 1.1, 0.05));
            DMesh3 mesh = cube.ToMesh(DefectSet.None(), PartFrame.Identity, 1);
            double coarse = Checks.SegmentClearance(mesh, new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), 0.5, 100);
            Assert.True(coarse <= 1 + 1e-12, "not a lower bound: " + coarse);
            Assert.True(coarse > 0.3, "far too low: " + coarse);
            // refined until the target is proved or the interval is small: the bound approaches the true distance from below
            double fine = Checks.SegmentClearance(mesh, new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), 2, 0.01);
            Assert.InRange(fine, 0.99, 1 + 1e-12);
        }

        [Fact]
        public void PinCheck_FailsWhenAPinBlocksTheBore()
        {
            // a block pushed into the bore: a thin box across the axis at mid height
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            var obstacle = new Soup();
            Vector3d c = new Vector3d(Parts.Offset.x - 20, Parts.Offset.y, Parts.Offset.z + 10);
            obstacle.Box("o", c - new Vector3d(1, 1, 1), c + new Vector3d(1, 1, 1));
            var mesh = new DMesh3(part.Input);
            new MeshEditor(mesh).AppendMesh(obstacle.ToMesh(DefectSet.None(), PartFrame.Identity, 1));
            CheckResult r = Run(part, "pinMain", mesh);
            Assert.False(r.Pass);
            Assert.True(r.Value < 0.5, "the box is on the axis: the clearance is at most 0 (inside the box), got " + r.Value);
        }

        [Fact]
        public void VolumeCheck_FailsWhenTheMaterialIsOff()
        {
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            Assert.True(Run(part, "volume", part.Input).Pass);
            Assert.True(Run(part, "volume", Scaled(part.Input, 1.001)).Pass);        // 0.3 % more volume is within the tolerance of a reduction's deviation bound
            Assert.False(Run(part, "volume", Scaled(part.Input, 1.01)).Pass);        // 3 % is not
            Assert.False(Run(part, "volume", part.Input, afterCut: true).Pass);       // the cut-out was not made
            Assert.False(Run(part, "volume", new DMesh3()).Pass);                      // nothing left
            Assert.False(Run(part, "pinMain", new DMesh3()).Pass);
            Assert.False(Run(part, "pinCross", new DMesh3()).Pass);
        }

        [Fact]
        public void RibThickness_IsTheConstructions_AndFailsAThinnedRib()
        {
            ReferencePart part = Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.4);
            CheckResult ok = Run(part, "ribThickness", part.Input);
            Assert.True(ok.Pass);
            Assert.Equal(0.4, ok.Value, 9);
            // the rib of a thinner part (0.3 of 0.4 required: below 80 %) fails; 0.35 passes
            Assert.False(Run(part, "ribThickness", Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.3).Input).Pass);
            Assert.True(Run(part, "ribThickness", Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.35).Input).Pass);
            Assert.False(Run(part, "ribThickness", Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.5).Input).Pass);
        }

        [Fact]
        public void RibConnected_FailsWhenTheRibIsCutOffByAGap()
        {
            ReferencePart connected = Parts.HousingRib(DefectSet.None(), PartFrame.Identity);
            CheckResult ok = Run(connected, "ribConnected", connected.Input);
            Assert.True(ok.Pass, ok.Detail);
            Assert.Equal(1, ok.Value);
            // the top plate 0.1 above the walls it joins (a gap far above the welding tolerance): the plate and the rib are apart
            ReferencePart gap = Parts.HousingRib(new DefectSet() { Unwelded = false, GapFraction = 1e-3 }, PartFrame.Identity);
            CheckResult broken = Run(gap, "ribConnected", gap.Input);
            Assert.False(broken.Pass);
            Assert.True(broken.Value > 1);
            // a gap below the welding tolerance does not separate
            ReferencePart tiny = Parts.HousingRib(new DefectSet() { Unwelded = false, GapFraction = 1e-12 }, PartFrame.Identity);
            Assert.True(Run(tiny, "ribConnected", tiny.Input).Pass);
        }

        [Fact]
        public void ScrewChecks_FailWhenTheScrewIsLostOrWrong()
        {
            ReferencePart part = Parts.ScrewAssembly(1e-2, DefectSet.None(), PartFrame.Identity);
            Assert.True(Run(part, "screwSeparate", part.Input).Pass);
            CheckResult box = Run(part, "screwBox", part.Input);
            Assert.True(box.Pass);
            Assert.True(box.Value < 1e-9, "the construction's box: " + box.Value);

            List<Checks.Component> comps = Checks.Components(part.Input, 1e-5);
            Assert.Equal(2, comps.Count);
            Checks.Component screw = comps[1];
            var lost = new DMesh3(part.Input);
            foreach (int t in lost.TriangleIndices().ToArray())
                if (screw.Vertices.Contains(lost.GetTriangle(t).a)) lost.RemoveTriangle(t);
            Assert.False(Run(part, "screwSeparate", lost).Pass);
            Assert.False(Run(part, "screwBox", lost).Pass);

            // the screw 30 % too big (its vertices scaled about the screw's centre)
            var big = new DMesh3(part.Input);
            Vector3d centre = screw.Box.Center;
            foreach (int v in screw.Vertices) big.SetVertex(v, centre + 1.3 * (big.GetVertex(v) - centre));
            Assert.True(Run(part, "screwSeparate", big).Pass);
            Assert.False(Run(part, "screwBox", big).Pass);
        }

        // ---------------- the chains: today's results ----------------

        [Fact]
        public void Flange_Welded_LibraryChain_KeepsThePinThroughEveryStep()
        {
            ChainResult chain = Chains.RunLibraryChain(Parts.Flange(24, DefectSet.None(), PartFrame.Identity));
            Assert.Equal("construct,repair,subtract,reduce,stl,obj", Steps(chain));
            Assert.Null(chain.FirstFailingStep());
            foreach (StepResult s in chain.Steps) Assert.True(s.Passes, s.Name);
            double reduced = chain.Steps.First(s => s.Name == "reduce").Check("pinMain").Value;
            double stl = chain.Steps.First(s => s.Name == "stl").Check("pinMain").Value;
            // the float32 export at coordinates around 1000 (spacing 6e-5) moves the measured clearance: visible, but far below the pin's margin
            Assert.NotEqual(reduced, stl);
            Assert.True(Math.Abs(reduced - stl) < 1e-3, "float32 changed the clearance by " + (reduced - stl));
            Assert.True(chain.Steps.First(s => s.Name == "stl").Triangles == chain.Steps.First(s => s.Name == "reduce").Triangles);
            // the OBJ text keeps what the STL step returned
            Assert.Equal(stl, chain.Steps.First(s => s.Name == "obj").Check("pinMain").Value, 9);
        }

        [Fact]
        public void Flange_Welded_PipelineChain_KeepsThePinThroughEveryStep()
        {
            ChainResult chain = Chains.RunPipelineChain(Parts.Flange(24, DefectSet.None(), PartFrame.Identity));
            Assert.Equal("construct,pipeline,stl,obj", Steps(chain));
            Assert.Null(chain.FirstFailingStep());
            Assert.True(chain.Steps[1].Passes);
        }

        [Fact]
        public void Flange_WithDuplicatedFaces_PinnedFailure_TheRepairNarrowsTheBore()
        {
            // Today's result (not a wanted one): a soup with every 17th triangle twice (the other defects off) goes through MeshRobustRepair on the
            // ExactResolve path and the bore comes out about 0.45 mm too narrow for the pin (clearance 7.45 for 7.76 needed; the construction has
            // 7.93), and every later step keeps it. SolidBooleanPipeline with AutoRepairThenRobust shows the same at its first step. The cross
            // bore and the volume pass. If the repair or the pipeline is fixed, this test fails: pin the new result then.
            ReferencePart part = Parts.Flange(24, new DefectSet() { DuplicateEvery = 17 }, PartFrame.Identity);
            ChainResult lib = Chains.RunLibraryChain(part);
            Assert.Equal("repair", lib.FirstFailingName());
            Assert.Equal("repair", lib.FirstFailingName("pinMain"));
            Assert.Equal("-", lib.FirstFailingName("pinCross"));
            Assert.Equal("-", lib.FirstFailingName("volume"));
            Assert.InRange(lib.FirstFailingStep("pinMain").Check("pinMain").Value, 7.40, 7.50);
            Assert.Equal("repair", lib.FirstFailingName("pinMain"));
            Assert.Equal("obj", lib.Steps.Last().Name);
            Assert.False(Last(lib, "pinMain").Pass);

            ChainResult pipe = Chains.RunPipelineChain(part);
            Assert.Equal("pipeline", pipe.FirstFailingName());
            Assert.Equal("pipeline", pipe.FirstFailingName("pinMain"));
            Assert.InRange(pipe.FirstFailingStep("pinMain").Check("pinMain").Value, 7.40, 7.50);
            Assert.True(pipe.Steps[1].Check("pinCross").Pass);
        }

        [Fact]
        public void Flange_WithTheFaultySoup_Pipeline_PinnedFailure_VolumeAndBoreAreOff()
        {
            // Today's result: flipped and duplicated faces together (unwelded): the pipeline's result misses the volume (about 0.9 % less) and
            // the main bore's clearance (7.70 for 7.76 needed). The cross bore passes.
            ReferencePart part = Parts.Flange(24, DefectSet.Faulty(), PartFrame.Identity);
            ChainResult pipe = Chains.RunPipelineChain(part);
            StepResult first = pipe.FirstFailingStep();
            Assert.Equal("pipeline", first.Name);
            Assert.False(first.Check("volume").Pass);
            Assert.InRange(first.Check("volume").Value, -0.02, -0.005);
            Assert.False(first.Check("pinMain").Pass);
            Assert.InRange(first.Check("pinMain").Value, 7.6, 7.74);
            Assert.True(first.Check("pinCross").Pass);
        }

        [Fact]
        public void HousingRib_Thick_BothChains_KeepTheRib()
        {
            ReferencePart part = Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.4);
            ChainResult lib = Chains.RunLibraryChain(part);
            Assert.Null(lib.FirstFailingStep());
            Assert.Null(Chains.RunPipelineChain(part).FirstFailingStep());
            // the float32 export moves the thickness by less than its spacing
            double thickness = lib.Steps.First(s => s.Name == "stl").Check("ribThickness").Value;
            Assert.True(Math.Abs(thickness - 0.4) < 1e-4);
        }

        [Fact]
        public void HousingRib_Thin_CleanInput_TheRepairPassesItThrough()
        {
            // A rib of 0.1 (1e-3 of the housing, a clean welded soup) was thinned to about 0.025 by MeshRobustRepair's Repair path (it moved the rib's
            // faces together). A clean input of one component is now returned as it is (#1337), so the library chain keeps the rib as the pipeline does.
            ReferencePart part = Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.1);
            ChainResult lib = Chains.RunLibraryChain(part);
            Assert.Null(lib.FirstFailingStep());
            Assert.True(Last(lib, "ribThickness").Pass);
            Assert.Null(Chains.RunPipelineChain(part).FirstFailingStep());
        }

        [Theory]
        [InlineData(1e-1)]
        [InlineData(1e-2)]
        [InlineData(1e-3)]
        [InlineData(1e-4)]
        public void Screw_AtRatios_StaysASeparateBodyWithItsBox(double ratio)
        {
            ReferencePart part = Parts.ScrewAssembly(ratio, DefectSet.None(), PartFrame.Identity);
            foreach (ChainResult chain in new[] { Chains.RunLibraryChain(part), Chains.RunPipelineChain(part) }) {
                Assert.True(chain.FirstFailingStep() == null, chain.Chain + " fails at " + chain.FirstFailingName());
                foreach (StepResult s in chain.Steps.Skip(1)) {
                    Assert.Equal(2, s.Check("screwSeparate").Value);
                    Assert.True(s.Check("screwBox").Value <= 2 * part.Deviation + 0.01 * part.Targets["screwSize"], chain.Chain + " " + s.Name);
                }
            }
        }

        [Fact]
        public void Screw_AtRatio1e4_Float32ExportMovesTheBoxByItsSpacing()
        {
            // the screw is 0.01 big at coordinates of 1000: the float32 spacing there is 6.1e-5, so the STL step changes the measured extent error
            // (visible), still within the tolerance
            ReferencePart part = Parts.ScrewAssembly(1e-4, DefectSet.None(), PartFrame.Identity);
            ChainResult chain = Chains.RunLibraryChain(part);
            double before = chain.Steps.First(s => s.Name == "reduce").Check("screwBox").Value;
            double after = chain.Steps.First(s => s.Name == "stl").Check("screwBox").Value;
            Assert.NotEqual(before, after);
            Assert.True(Math.Abs(after - before) < 2e-4, "change " + (after - before));
            Assert.True(chain.Steps.First(s => s.Name == "stl").Passes);
        }

        [Fact]
        public void Screw_AtRatio1e5_PinnedFailure_TheBooleanDropsTheScrew_AndReportsIt()
        {
            // Today's result (not a wanted one): a screw of 1e-3 (1e-5 of the housing) is a closed, welded body 1e-4 above the housing. The
            // exact boolean itself removes it: its triangles are below the degenerate area of the result, the repair contracts them and
            // what is left is not the screw (its volume changed by more than a factor 2), so SafeRounding's component removal takes it;
            // with SafeRounding off the screw stays. Since #1216 the removal is listed (VolumeChanged, with the screw's thickness) and the
            // result is not clean, and the limit is lower: the screw stays down to a ratio of 2e-5 (before: 7e-5), because a component the
            // edits left as it was is no longer removed. MeshRobustRepair
            // used to lose it too (ExactResolve, degenerate triangles; fixed by #1363, see below), the pipeline loses it at its boolean (first failing step: pipeline).
            ReferencePart part = Parts.ScrewAssembly(1e-5, DefectSet.None(), PartFrame.Identity);
            var kept = new ExactMeshBoolean(part.Input, part.Cutter, ExactMeshBoolean.Operations.Difference) { SafeRounding = false };
            Assert.True(kept.Compute());
            Assert.Equal(2, Checks.Components(kept.Result, 1e-5).Count);
            var dropped = new ExactMeshBoolean(part.Input, part.Cutter, ExactMeshBoolean.Operations.Difference);
            Assert.True(dropped.Compute());
            Assert.Equal(1, Checks.Components(dropped.Result, 1e-5).Count);
            Assert.Equal(1, dropped.Rounding.RemovedComponents);
            MeshRoundingRepair.RemovedComponentInfo screw = Assert.Single(dropped.Rounding.RemovedComponentInfos);
            Assert.Equal(MeshRoundingRepair.RemovalReasons.VolumeChanged, screw.Reason);
            Assert.InRange(screw.Thickness, 1e-4, 2e-4);
            Assert.Equal(1, dropped.Rounding.RemovedResolvable);
            Assert.False(dropped.RoundingClean);
            var narrow = new ExactMeshBoolean(part.Input, part.Cutter, ExactMeshBoolean.Operations.Difference) { RoundingMaxDisplacement = 1e-9 };
            Assert.True(narrow.Compute());
            Assert.Equal(1, Checks.Components(narrow.Result, 1e-5).Count);       // the threshold is not RoundingMaxDisplacement
            Assert.Equal(MeshRoundingRepair.RemovalReasons.StillDefective, narrow.Rounding.RemovedComponentInfos[0].Reason);
            // the limit lies between the ratios 2e-5 (kept) and 1e-5 (dropped); before #1216 between 7e-5 and 5e-5
            foreach (var pair in new[] { Tuple.Create(7e-5, 2), Tuple.Create(5e-5, 2), Tuple.Create(2e-5, 2) }) {
                ReferencePart p = Parts.ScrewAssembly(pair.Item1, DefectSet.None(), PartFrame.Identity);
                var b = new ExactMeshBoolean(p.Input, p.Cutter, ExactMeshBoolean.Operations.Difference);
                Assert.True(b.Compute());
                Assert.Equal(pair.Item2, Checks.Components(b.Result, 1e-5).Count);
            }

            // The library chain no longer loses it (#1363): the needle cleanup of the repair collapsed the screw's degenerate triangles to their midpoint and
            // moved it; it now collapses onto the corner or the end that keeps the volume, bounded, so the screw stays a separate part of its size.
            ChainResult lib = Chains.RunLibraryChain(part);
            Assert.Null(lib.FirstFailingStep());
            Assert.Equal("-", lib.FirstFailingName("screwSeparate"));
            Assert.Equal("-", lib.FirstFailingName("screwBox"));
            Assert.Equal("-", lib.FirstFailingName("volume"));
            foreach (StepResult st in lib.Steps.Skip(1))
                Assert.Equal(2, st.Check("screwSeparate").Value);
            ChainResult pipe = Chains.RunPipelineChain(part);
            Assert.Equal("pipeline", pipe.FirstFailingName());
            Assert.Equal("pipeline", pipe.FirstFailingName("screwSeparate"));
        }

        // ---------------- invariance and determinism ----------------

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Constructions_Checks_AreInvariantUnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (string part in new[] { "flange", "rib", "screw1e-1" }) {
                ReferencePart reference = Build(part, DefectSet.None(), PartFrame.Identity);
                ReferencePart moved = Build(part, DefectSet.None(), FrameOf(s));
                foreach (PartCheck c in reference.Checks.Where(x => x.Functional)) {
                    CheckResult a = c.Run(reference, reference.Input, false), b = moved.Checks.First(x => x.Name == c.Name).Run(moved, moved.Input, false);
                    Assert.True(a.Pass, part + " " + c.Name + " reference");
                    Assert.True(b.Pass, part + " " + c.Name + " under " + name + ": " + b.Value + " " + b.Detail);
                    if (s.IsExact) Assert.True(a.Value == b.Value, part + " " + c.Name + " must be bit for bit under " + name + ": " + a.Value + " vs " + b.Value);
                    else Assert.True(Math.Abs(a.Value - b.Value) <= 1e-5 * Math.Max(1, Math.Abs(a.Value)), part + " " + c.Name + " under " + name + ": " + a.Value + " vs " + b.Value);
                }
            }
        }

        public static IEnumerable<object[]> SimilarityNames() { return Similarity.Names(); }

        static readonly List<Tuple<Func<PartFrame, ReferencePart>, Func<ReferencePart, ChainResult>>> Jobs = new List<Tuple<Func<PartFrame, ReferencePart>, Func<ReferencePart, ChainResult>>>() {
            Tuple.Create<Func<PartFrame, ReferencePart>, Func<ReferencePart, ChainResult>>(f => Parts.Flange(24, DefectSet.None(), f), p => Chains.RunLibraryChain(p)),
            Tuple.Create<Func<PartFrame, ReferencePart>, Func<ReferencePart, ChainResult>>(f => Parts.HousingRib(DefectSet.None(), f, 0.4), p => Chains.RunLibraryChain(p)),
            Tuple.Create<Func<PartFrame, ReferencePart>, Func<ReferencePart, ChainResult>>(f => Parts.ScrewAssembly(1e-2, DefectSet.None(), f), p => Chains.RunPipelineChain(p)),
        };

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Chains_AreInvariantUnderSimilarities(string name)
        {
            // a welded flange (library chain), the thick rib (library) and a screw at 1e-2 (pipeline): every step passes in every frame, the
            // measured values agree with the identity frame, and for a power of two scale they are the same bit for bit (the float32 export of
            // a power of two scaled part rounds the same way). The exports are left out where the frame moves the part by UTM coordinates:
            // float32 has a spacing of 0.5 at 5.7e6 (Chain_InUtmCoordinates_...)
            Similarity s = Similarity.ByName(name);
            bool translated = s.Translation != Vector3d.Zero;
            var jobs = Jobs;
            foreach (var job in jobs) {
                ChainResult reference = identity.Results[jobs.IndexOf(job)];
                ChainResult moved = job.Item2(job.Item1(FrameOf(s)));
                Assert.Equal(Steps(reference), Steps(moved));
                for (int i = 0; i < reference.Steps.Count; ++i) {
                    StepResult a = reference.Steps[i], b = moved.Steps[i];
                    if (translated && (a.Name == "stl" || a.Name == "obj")) continue;
                    Assert.Equal(a.Passes, b.Passes);
                    Assert.True(b.Passes || i == 0, reference.Part.Name + " " + b.Name + " under " + name);
                    if (s.IsExact)
                        Assert.Equal(a.Triangles, b.Triangles);
                    foreach (CheckResult c in a.Checks.Where(x => x.Functional)) {
                        CheckResult d = b.Check(c.Name);
                        double tolerance = s.IsExact ? 0 : 2e-4 * Math.Max(1, Math.Abs(c.Value)) + 3 * reference.Part.Deviation;
                        Assert.True(Math.Abs(c.Value - d.Value) <= tolerance, reference.Part.Name + " " + a.Name + " " + c.Name + " under " + name + ": " + c.Value + " vs " + d.Value);
                    }
                }
            }
        }

        [Fact]
        public void Flange_InUtmCoordinates_PinnedFailure_Float32ExportBreaksThePin()
        {
            // Today's result and physics: at UTM coordinates (y about 5.7e6) float32 has a spacing of 0.5, so the binary STL export of an 80 mm flange
            // moves its vertices by up to 0.25 and the bore no longer takes the pin. Every step before the export passes.
            ChainResult chain = Chains.RunLibraryChain(Parts.Flange(24, DefectSet.None(), FrameOf(Similarity.Utm)));
            Assert.Equal("stl", chain.FirstFailingName());
            foreach (StepResult s in chain.Steps.TakeWhile(x => x.Name != "stl")) Assert.True(s.Passes, s.Name);
            Assert.False(chain.Steps.First(s => s.Name == "stl").Check("pinMain").Pass);
        }

        [Fact]
        public void Chain_IsDeterministic_RepeatedRunsGiveTheSameMeshesAndValues()
        {
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            ReferencePart rib = Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.4);
            ChainResult a = Chains.RunLibraryChain(rib), b = Chains.RunLibraryChain(rib), p1 = Chains.RunPipelineChain(part), p2 = Chains.RunPipelineChain(part);
            foreach (var pair in new[] { Tuple.Create(a, b), Tuple.Create(p1, p2) })
                for (int i = 0; i < pair.Item1.Steps.Count; ++i) {
                    Assert.Equal(pair.Item1.Steps[i].Triangles, pair.Item2.Steps[i].Triangles);
                    foreach (CheckResult c in pair.Item1.Steps[i].Checks)
                        Assert.True(c.Value == pair.Item2.Steps[i].Check(c.Name).Value, pair.Item1.Chain + " " + pair.Item1.Steps[i].Name + " " + c.Name);
                }
        }

        [Fact]
        public void ExportSteps_StlIsFloat32_ObjKeepsTheDoubles()
        {
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            DMesh3 stl = Chains.StlRoundTrip(part.Input), obj = Chains.ObjRoundTrip(part.Input);
            Assert.Equal(part.Input.TriangleCount, stl.TriangleCount);
            Assert.Equal(part.Input.VertexCount, stl.VertexCount);
            double stlError = 0, objError = 0;
            for (int v = 0; v < part.Input.VertexCount; ++v) {
                stlError = Math.Max(stlError, (stl.GetVertex(v) - part.Input.GetVertex(v)).Length);
                objError = Math.Max(objError, (obj.GetVertex(v) - part.Input.GetVertex(v)).Length);
            }
            Assert.True(stlError > 1e-5 && stlError < 1e-4, "float32 spacing at 1000 is 6.1e-5, error " + stlError);
            Assert.True(objError < 1e-9, "OBJ error " + objError);
        }
    }
}
