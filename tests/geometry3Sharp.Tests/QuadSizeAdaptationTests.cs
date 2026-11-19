using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.IntegrableFrames, PositionField.SpacingFactors / RotationAngles and QuadRemesher.SizeAdaptation (docs/design/QUAD_REMESHING.md 4.5):
    /// the curl of the lattice frames drops, the spacing and rotation stay in range, the option changes the result on smooth closed shapes
    /// (fewer irregular vertices, more quads, still a faithful manifold mesh) and nothing where it does not help, invariance, reuse, determinism.
    /// The pinned default output is in QuadRemesherDefaultOutputTests.
    /// </summary>
    public class QuadSizeAdaptationTests
    {
        const double S = 0.2;
        static readonly Dictionary<string, DMesh3> meshes = new Dictionary<string, DMesh3>();
        static readonly Dictionary<string, CrossField> fields = new Dictionary<string, CrossField>();

        static DMesh3 Shape(string name)
        {
            lock (meshes) {
                DMesh3 m;
                if (!meshes.TryGetValue(name, out m)) {
                    switch (name) {
                        case "sphere": m = CrossFieldShapes.UnitSphere(); break;
                        case "torus": m = CrossFieldShapes.TorusMesh(); break;
                        case "box": m = CrossFieldShapes.FilletedBoxMesh(); break;
                        case "cap": m = CrossFieldShapes.SphereCap(); break;
                        case "patch": m = CrossFieldShapes.OpenPatch(24); break;
                        default: throw new ArgumentException(name);
                    }
                    meshes[name] = m;
                }
                return m;
            }
        }

        static CrossField FieldOf(string name)
        {
            lock (fields) {
                CrossField cf;
                if (!fields.TryGetValue(name, out cf)) {
                    cf = new CrossField(Shape(name));
                    Assert.Equal(CrossFieldStatus.Success, cf.Compute());
                    fields[name] = cf;
                }
                return cf;
            }
        }

        static double SizeOf(string name) { return name == "patch" ? 0.08 : S; }

        static QuadRemesher Run(string name, SizeAdaptationMode mode, Action<QuadRemesher> configure = null)
        {
            var qr = new QuadRemesher(Shape(name)) { Field = FieldOf(name), TargetEdgeLength = SizeOf(name), SizeAdaptation = mode };
            configure?.Invoke(qr);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        static IntegrableFrames Frames(string name, Action<IntegrableFrames> configure = null)
        {
            var fr = new IntegrableFrames(Shape(name), FieldOf(name)) { TargetEdgeLength = SizeOf(name) };
            configure?.Invoke(fr);
            Assert.Equal(IntegrableFramesStatus.Success, fr.Compute());
            return fr;
        }

        static void AssertSame(DPolyMesh a, DPolyMesh b, double scale = 1)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.FaceCount, b.FaceCount);
            Assert.Equal(a.FaceOffsets.ToArray(), b.FaceOffsets.ToArray());
            Assert.Equal(a.FaceIndices.ToArray(), b.FaceIndices.ToArray());
            for (int v = 0; v < a.VertexCount; ++v) {
                Vector3d p = a.GetVertex(v), q = b.GetVertex(v);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * scale), BitConverter.DoubleToInt64Bits(q.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * scale), BitConverter.DoubleToInt64Bits(q.y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * scale), BitConverter.DoubleToInt64Bits(q.z));
            }
        }

        static void AssertSameBits(double[] a, double[] b, double scale = 1)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i) Assert.Equal(BitConverter.DoubleToInt64Bits(a[i] * scale), BitConverter.DoubleToInt64Bits(b[i]));
        }

        static PolyMeshMetrics CheckResult(DPolyMesh r, string what)
        {
            Assert.True(r.FaceCount > 0, what);
            Assert.True(r.Validate().IsValid, what + " " + r.Validate());
            for (int v = 0; v < r.VertexCount; ++v) Assert.True(r.GetVertex(v).IsFinite, what);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.MaxSides <= 6, what + ": a face with " + m.MaxSides + " sides");
            Assert.True(m.IsManifold, what + ": not manifold");
            return m;
        }

        static double OneSidedHausdorff(DPolyMesh r, DMesh3 reference)
        {
            int[] map;
            DMesh3 tri = r.ToDMesh3(out map);
            var hd = new MeshHausdorffDistance(tri, reference) { Symmetric = false, RelativeTolerance = 1e-3 };
            hd.Compute();
            return hd.UpperBound;
        }

        // ---------------------------------------------------------------- IntegrableFrames

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        public void Frames_LowerTheCurlWithinRanges(string name)
        {
            IntegrableFrames fr = Frames(name);
            Assert.True(fr.CurlBefore > 20, "curl before " + fr.CurlBefore);
            Assert.True(fr.CurlAfter <= 0.7 * fr.CurlBefore, "curl " + fr.CurlBefore + " -> " + fr.CurlAfter);
            Assert.True(fr.StepsTaken >= 1 && fr.TrianglesUsed > 0);
            Assert.InRange(fr.MinSpacing, 1 / fr.MaxSpacingRatio - 1e-9, 1);
            Assert.InRange(fr.MaxSpacing, 1, fr.MaxSpacingRatio + 1e-9);
            Assert.True(fr.MaxRotationUsedDegrees <= fr.MaxRotationDegrees + 1e-9);
            // the mean of the log spacing is 0 (the mean edge length stays the target)
            double mean = 0; int n = 0;
            foreach (int v in Shape(name).VertexIndices()) { mean += Math.Log(fr.Spacing[v]); n++; }
            Assert.True(Math.Abs(mean / n) < 1e-9, "mean log spacing " + mean / n);
            Assert.Equal(Shape(name).MaxVertexID, fr.Spacing.Length);
            Assert.Equal(Shape(name).MaxVertexID, fr.Rotation.Length);
        }

        [Fact]
        public void Frames_ClampedSpacingKeepsTheMeanLogSpacingAtZero()
        {
            IntegrableFrames fr = Frames("sphere", f => f.MaxSpacingRatio = 1.25);
            double mean = 0; int n = 0;
            foreach (int v in Shape("sphere").VertexIndices()) { mean += Math.Log(fr.Spacing[v]); n++; }
            Assert.True(Math.Abs(mean / n) < 1e-9, "mean log spacing " + mean / n);
            Assert.InRange(fr.MaxSpacing / fr.MinSpacing, 1.0, 1.25 * 1.25 + 1e-9);
            Assert.True(fr.MaxSpacing / fr.MinSpacing > 1.2, "the clamp is active");
        }

        [Theory]
        [InlineData("sphere", 1.0)]
        [InlineData("sphere", 16.0)]
        [InlineData("torus", 16.0)]
        public void Frames_BacktrackingKeepsTheCostFalling_EvenFromAnOvershoot(string name, double firstStep)
        {
            // the first trial step is firstStep times the Gauss-Newton step: far too long, the backtracking halves it until the cost falls
            IntegrableFrames fr = Frames(name, f => { f.InitialStepFactor = firstStep; f.Iterations = 8; f.StopTolerance = 0; });
            Assert.True(fr.CurlAfter < 0.8 * fr.CurlBefore, "curl " + fr.CurlBefore + " -> " + fr.CurlAfter);
            Assert.True(fr.StepsTaken >= 1);
        }

        [Fact]
        public void Frames_ScaleOnlyHasNoRotation_AndBoundaryVerticesDoNotRotate()
        {
            IntegrableFrames scaleOnly = Frames("sphere", f => f.UseRotation = false);
            Assert.All(scaleOnly.Rotation, r => Assert.Equal(0.0, r));
            Assert.True(scaleOnly.CurlAfter < scaleOnly.CurlBefore);
            IntegrableFrames cap = Frames("cap");
            DMesh3 m = Shape("cap");
            int boundary = 0;
            foreach (int v in m.VertexIndices())
                if (m.IsBoundaryVertex(v)) { boundary++; Assert.Equal(0.0, cap.Rotation[v]); }
            Assert.True(boundary > 10);
            Assert.True(cap.Rotation.Any(r => r != 0));
        }

        [Fact]
        public void Frames_NoCurlOnAFlatPatch_NothingToDo()
        {
            IntegrableFrames fr = Frames("patch");
            Assert.True(fr.CurlBefore < 5, "curl " + fr.CurlBefore);
            Assert.InRange(fr.MinSpacing, 0.9, 1);
            Assert.InRange(fr.MaxSpacing, 1, 1.1);
        }

        [Fact]
        public void Frames_BadInput()
        {
            var cf = FieldOf("sphere");
            Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(null, cf) { TargetEdgeLength = S }.Compute());
            Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(Shape("sphere"), null) { TargetEdgeLength = S }.Compute());
            Assert.Equal(IntegrableFramesStatus.NoSolution, new IntegrableFrames(Shape("sphere"), new CrossField(Shape("sphere"))) { TargetEdgeLength = S }.Compute());   // a field that was not computed has no vertex with a field
            foreach (double s in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
                Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(Shape("sphere"), cf) { TargetEdgeLength = s }.Compute());
            Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(Shape("sphere"), cf) { TargetEdgeLength = S, Iterations = 0 }.Compute());
            Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(Shape("sphere"), cf) { TargetEdgeLength = S, Damping = 0 }.Compute());
            Assert.Equal(IntegrableFramesStatus.InvalidInput, new IntegrableFrames(Shape("sphere"), cf) { TargetEdgeLength = S, MaxSpacingRatio = 0.5 }.Compute());
            var bad = new IntegrableFrames(Shape("sphere"), cf) { TargetEdgeLength = -1 };
            bad.Compute();
            Assert.False(bad.Success);
            Assert.NotNull(bad.Error);
        }

        [Fact]
        public void Frames_AreTheSameWhenComputedAgain_AndRepeated()
        {
            var fr = new IntegrableFrames(Shape("torus"), FieldOf("torus")) { TargetEdgeLength = S };
            Assert.Equal(IntegrableFramesStatus.Success, fr.Compute());
            double[] sp = (double[])fr.Spacing.Clone(), ro = (double[])fr.Rotation.Clone();
            Assert.Equal(IntegrableFramesStatus.Success, fr.Compute());     // reuse of the instance
            AssertSameBits(sp, fr.Spacing); AssertSameBits(ro, fr.Rotation);
            IntegrableFrames other = Frames("torus");
            AssertSameBits(sp, other.Spacing); AssertSameBits(ro, other.Rotation);
        }

        public static IEnumerable<object[]> Names() => Similarity.Names();

        [Theory, MemberData(nameof(Names))]
        public void Frames_AreInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            IntegrableFrames baseFrames = Frames("sphere");
            DMesh3 t = Invariance.Transformed(Shape("sphere"), sim);
            var cf = new CrossField(t);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var fr = new IntegrableFrames(t, cf) { TargetEdgeLength = sim.Scale * S };
            Assert.Equal(IntegrableFramesStatus.Success, fr.Compute());
            if (sim.IsExact) {
                AssertSameBits(baseFrames.Spacing, fr.Spacing);
                AssertSameBits(baseFrames.Rotation, fr.Rotation);
                Assert.Equal(BitConverter.DoubleToInt64Bits(baseFrames.CurlAfter), BitConverter.DoubleToInt64Bits(fr.CurlAfter));
            } else {
                // rounding changes the field a little: the same kind of size field
                Assert.True(fr.CurlAfter <= 0.75 * fr.CurlBefore, name + ": curl " + fr.CurlBefore + " -> " + fr.CurlAfter);
                Assert.InRange(fr.MinSpacing, 0.4, 0.8);
                Assert.InRange(fr.MaxSpacing, 1.0, 1.5);
            }
        }

        // ---------------------------------------------------------------- PositionField inputs

        static PositionField Positions(string name, Action<PositionField> configure = null)
        {
            var pf = new PositionField(Shape(name), FieldOf(name)) { TargetEdgeLength = SizeOf(name) };
            configure?.Invoke(pf);
            return pf;
        }

        static void AssertSameOrigins(PositionField a, PositionField b, DMesh3 m, double scale = 1)
        {
            foreach (int v in m.VertexIndices()) {
                Vector3d p = a.Origin(v), q = b.Origin(v);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * scale), BitConverter.DoubleToInt64Bits(q.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * scale), BitConverter.DoubleToInt64Bits(q.y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * scale), BitConverter.DoubleToInt64Bits(q.z));
            }
        }

        [Fact]
        public void PositionField_NeutralSpacingAndRotationChangeNothing()
        {
            DMesh3 m = Shape("torus");
            PositionField a = Positions("torus"); a.Compute();
            PositionField b = Positions("torus", p => { p.SpacingFactors = Enumerable.Repeat(1.0, m.MaxVertexID).ToArray(); p.RotationAngles = new double[m.MaxVertexID]; });
            Assert.Equal(PositionFieldStatus.Success, b.Compute());
            AssertSameOrigins(a, b, m);
        }

        [Fact]
        public void PositionField_ConstantSpacingFactorIsTheTargetEdgeLength()
        {
            // a spacing factor 2 everywhere with the target s is the lattice of the target 2 s (powers of two: exact)
            DMesh3 m = Shape("sphere");
            PositionField a = Positions("sphere", p => p.TargetEdgeLength = 2 * S); a.Compute();
            PositionField b = Positions("sphere", p => p.SpacingFactors = Enumerable.Repeat(2.0, m.MaxVertexID).ToArray());
            Assert.Equal(PositionFieldStatus.Success, b.Compute());
            AssertSameOrigins(a, b, m);
        }

        [Fact]
        public void PositionField_SpacingAndRotationAreValidated()
        {
            DMesh3 m = Shape("sphere");
            Assert.Equal(PositionFieldStatus.InvalidInput, Positions("sphere", p => p.SpacingFactors = new double[3]).Compute());
            var zero = new double[m.MaxVertexID];
            Assert.Equal(PositionFieldStatus.InvalidInput, Positions("sphere", p => p.SpacingFactors = zero).Compute());
            var nan = Enumerable.Repeat(1.0, m.MaxVertexID).ToArray(); nan[5] = double.NaN;
            Assert.Equal(PositionFieldStatus.InvalidInput, Positions("sphere", p => p.SpacingFactors = nan).Compute());
            Assert.Equal(PositionFieldStatus.InvalidInput, Positions("sphere", p => p.RotationAngles = new double[2]).Compute());
            var rot = new double[m.MaxVertexID]; rot[7] = double.PositiveInfinity;
            Assert.Equal(PositionFieldStatus.InvalidInput, Positions("sphere", p => p.RotationAngles = rot).Compute());
        }

        [Fact]
        public void PositionField_SpacingFieldChangesTheLattice()
        {
            DMesh3 m = Shape("sphere");
            IntegrableFrames fr = Frames("sphere");
            PositionField a = Positions("sphere"); a.Compute();
            PositionField b = Positions("sphere", p => { p.SpacingFactors = fr.Spacing; p.RotationAngles = fr.Rotation; });
            Assert.Equal(PositionFieldStatus.Success, b.Compute());
            int differ = 0;
            foreach (int v in m.VertexIndices()) if (a.Origin(v) != b.Origin(v)) differ++;
            Assert.True(differ > m.VertexCount / 2, "differ " + differ);
            // the same serial and parallel
            PositionField c = Positions("sphere", p => { p.SpacingFactors = fr.Spacing; p.RotationAngles = fr.Rotation; p.Parallel = false; });
            c.Compute();
            PositionField d = Positions("sphere", p => { p.SpacingFactors = fr.Spacing; p.RotationAngles = fr.Rotation; p.Parallel = true; p.ChunkSize = 16; });
            d.Compute();
            AssertSameOrigins(c, b, m);
            AssertSameOrigins(c, d, m);
        }

        // ---------------------------------------------------------------- QuadRemesher.SizeAdaptation

        [Fact]
        public void Off_IsTheDefault_AndEqualsAnUnusedOption()
        {
            DPolyMesh dflt = Run("sphere", SizeAdaptationMode.Off).Result;
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = S };
            Assert.Equal(SizeAdaptationMode.Off, qr.SizeAdaptation);
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.False(qr.SizeAdaptationApplied);
            AssertSame(dflt, qr.Result);
        }

        [Theory]
        [InlineData("sphere", SizeAdaptationMode.Scale)]
        [InlineData("sphere", SizeAdaptationMode.ScaleAndRotation)]
        [InlineData("torus", SizeAdaptationMode.ScaleAndRotation)]
        public void SizeAdaptation_CutsTheIrregularVerticesOfSmoothClosedShapes(string name, SizeAdaptationMode mode)
        {
            PolyMeshMetrics off = CheckResult(Run(name, SizeAdaptationMode.Off).Result, "off");
            QuadRemesher qr = Run(name, mode);
            Assert.True(qr.SizeAdaptationApplied);
            Assert.True(qr.CurlAfter <= 0.8 * qr.CurlBefore);
            Assert.InRange(qr.MinSpacing, 0.4, 0.95);
            Assert.InRange(qr.MaxSpacing, 1.0, 2.5);
            Assert.True(qr.SizeSeconds > 0);
            PolyMeshMetrics m = CheckResult(qr.Result, name + " " + mode);
            Assert.True(m.IrregularVertices <= 0.85 * off.IrregularVertices, "irregular " + off.IrregularVertices + " -> " + m.IrregularVertices);
            Assert.True(m.QuadFraction >= off.QuadFraction + 0.01, "quads " + off.QuadFraction + " -> " + m.QuadFraction);
            Assert.True(m.BoundaryVertices <= 12 && qr.DroppedFaces <= 2, "holes: boundary vertices " + m.BoundaryVertices + ", dropped faces " + qr.DroppedFaces);     // a small hole where a face had too many sides
            Assert.InRange(m.Faces, 0.9 * off.Faces, 1.1 * off.Faces);
            double h = OneSidedHausdorff(qr.Result, Shape(name));
            Assert.True(h <= 0.5 * S, "Hausdorff " + h);
            double c95 = PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), c95off = PolyMeshMetrics.Percentile(off.CornerDeviations, 0.95);
            Assert.True(c95 <= c95off + 4, "corner p95 " + c95off + " -> " + c95);
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        public void IrregularVertices_AreWithinTheRestatedGate_OfTheCurlMass(string name)
        {
            // docs/design/QUAD_REMESHING.md 4.5: irregular <= 2 S + 4 + 1.25 C (S singular faces, C the curl mass of the uniform spacing), and the curl
            // mass is what the dislocations come from: the count is not far below it (the cones are only part of it)
            QuadRemesher qr = Run(name, SizeAdaptationMode.Scale);
            int irregular = PolyMeshMetrics.Compute(Run(name, SizeAdaptationMode.Off).Result).IrregularVertices;
            int sing = FieldOf(name).SingularFaces.Count;
            Assert.True(irregular <= 2 * sing + 4 + 1.25 * qr.CurlBefore, name + ": irregular " + irregular + ", S " + sing + ", C " + qr.CurlBefore);
            Assert.True(irregular >= 0.5 * (sing + qr.CurlBefore), name + ": irregular " + irregular + ", S " + sing + ", C " + qr.CurlBefore);
        }

        [Fact]
        public void SizeAdaptation_IsNotUsedWhereTheCurlDoesNotDrop_AndThenTheResultIsTheUniformOne()
        {
            // the filleted box: the curl hardly changes, the option leaves the result as it is
            QuadRemesher box = Run("box", SizeAdaptationMode.ScaleAndRotation);
            Assert.False(box.SizeAdaptationApplied);
            Assert.True(box.CurlAfter > 0.8 * box.CurlBefore);
            Assert.Equal(1.0, box.MinSpacing); Assert.Equal(1.0, box.MaxSpacing);
            AssertSame(Run("box", SizeAdaptationMode.Off).Result, box.Result);
            // or by the threshold
            QuadRemesher strict = Run("sphere", SizeAdaptationMode.Scale, q => q.SizeAdaptationMinGain = 0.99);
            Assert.False(strict.SizeAdaptationApplied);
            AssertSame(Run("sphere", SizeAdaptationMode.Off).Result, strict.Result);
        }

        [Fact]
        public void SizeAdaptation_OpenShapesStayValid()
        {
            foreach (string name in new[] { "cap", "patch" }) {
                QuadRemesher qr = Run(name, SizeAdaptationMode.ScaleAndRotation);
                PolyMeshMetrics m = CheckResult(qr.Result, name);
                Assert.True(m.QuadFraction >= 0.85, name + " quads " + m.QuadFraction);
                double hOff = OneSidedHausdorff(Run(name, SizeAdaptationMode.Off).Result, Shape(name)), hOn = OneSidedHausdorff(qr.Result, Shape(name));
                Assert.True(hOn <= 0.8 * SizeOf(name), name + " Hausdorff " + hOff + " -> " + hOn);
            }
        }

        [Fact]
        public void SizeAdaptation_ResultIsTheSameSerialParallelRepeatedAndReused()
        {
            DPolyMesh serial = Run("sphere", SizeAdaptationMode.ScaleAndRotation, q => { q.Parallel = false; q.ChunkSize = 8; }).Result;
            DPolyMesh parallel = Run("sphere", SizeAdaptationMode.ScaleAndRotation, q => { q.Parallel = true; q.ChunkSize = 8; }).Result;
            DPolyMesh dflt = Run("sphere", SizeAdaptationMode.ScaleAndRotation).Result;
            AssertSame(serial, parallel);
            AssertSame(serial, dflt);
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = S, SizeAdaptation = SizeAdaptationMode.ScaleAndRotation };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            DPolyMesh first = qr.Result;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());          // reuse of the instance
            AssertSame(first, qr.Result);
            AssertSame(serial, qr.Result);
            // switching the option off on the same instance gives the uniform result again
            qr.SizeAdaptation = SizeAdaptationMode.Off;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.False(qr.SizeAdaptationApplied);
            AssertSame(Run("sphere", SizeAdaptationMode.Off).Result, qr.Result);
        }

        [Theory, MemberData(nameof(Names))]
        public void SizeAdaptation_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DPolyMesh baseResult = Run("sphere", SizeAdaptationMode.ScaleAndRotation).Result;
            DMesh3 t = Invariance.Transformed(Shape("sphere"), sim);
            var cf = new CrossField(t);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(t) { Field = cf, TargetEdgeLength = sim.Scale * S, SizeAdaptation = SizeAdaptationMode.ScaleAndRotation };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (sim.IsExact) {
                AssertSame(baseResult, qr.Result, sim.Scale);
            } else {
                PolyMeshMetrics a = CheckResult(baseResult, "base"), b = CheckResult(qr.Result, name);
                Assert.True(qr.SizeAdaptationApplied, name);
                Assert.InRange(b.Faces, 0.9 * a.Faces, 1.1 * a.Faces);
                Assert.True(b.QuadFraction >= 0.93, name + ": quads " + b.QuadFraction);
                Assert.True(OneSidedHausdorff(qr.Result, t) <= 0.5 * sim.Scale * S, name + ": Hausdorff");
            }
        }

        [Fact]
        public void SizeAdaptation_BadSettingsAreReported()
        {
            var qr = new QuadRemesher(Shape("sphere")) { Field = FieldOf("sphere"), TargetEdgeLength = S, SizeAdaptation = SizeAdaptationMode.Scale, Frames = new IntegrableFrames(null, null) { Iterations = 0 } };
            Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
            Assert.Null(qr.Result);
            Assert.NotNull(qr.Error);
        }

        [Fact]
        public void SizeAdaptation_UsesTheSettingsOfTheFramesObjectOfTheCaller()
        {
            var fr = new IntegrableFrames(null, null) { Iterations = 3, StopTolerance = 0 };
            QuadRemesher qr = Run("sphere", SizeAdaptationMode.Scale, q => q.Frames = fr);
            Assert.True(qr.SizeAdaptationApplied);
            Assert.Same(Shape("sphere"), fr.Mesh);
            Assert.Equal(S, fr.TargetEdgeLength);
            Assert.False(fr.UseRotation);              // the mode decides
            Assert.Equal(3, fr.StepsTaken);
            Assert.Equal(fr.CurlAfter, qr.CurlAfter);
            Assert.Equal(fr.MaxSpacing, qr.MaxSpacing);
            // fewer steps lower the curl less than the default
            Assert.True(fr.CurlAfter > Run("sphere", SizeAdaptationMode.Scale).CurlAfter);
        }
    }
}
