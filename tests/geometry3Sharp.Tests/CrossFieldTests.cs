using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.CrossField (docs/design/QUAD_REMESHING.md stage 1; Knöppel et al. 2013): the singularity indices against the theory
    /// (Poincare-Hopf), alignment with curvature, boundary and crease edges, status of bad input, invariance, reuse, determinism.
    /// </summary>
    public class CrossFieldTests
    {
        // the smallest angle between the line of b and the four directions of the cross of a (radians, 0 .. pi/4)
        static double CrossAngle(Vector3d a, Vector3d b)
        {
            double c = Math.Min(1, Math.Abs(a.Normalized.Dot(b.Normalized)));
            double th = Math.Acos(c);
            return Math.Min(th, Math.PI / 2 - th);
        }

        static CrossField Run(DMesh3 m, double s = 0, Action<CrossField> configure = null)
        {
            var cf = new CrossField(m) { CurvatureAlignment = s };
            configure?.Invoke(cf);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            return cf;
        }

        // the checks that hold for every successful result: finite unit directions in the tangent plane, a consistent histogram
        static void CheckResult(CrossField cf)
        {
            DMesh3 m = cf.Mesh;
            foreach (int v in m.VertexIndices()) {
                Assert.True(cf.HasField(v));
                Vector3d d = cf.VertexDirection(v);
                Assert.True(d.IsFinite);
                Assert.Equal(1, d.Length, 9);
                Assert.Equal(0, d.Dot(cf.VertexNormal(v)), 9);
                Assert.Equal(1, cf.VertexDirection(v, 1).Dot(cf.VertexNormal(v).Cross(d)), 9);
            }
            double sum = 0; int count = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d fd = cf.FaceDirection(t);
                Assert.Equal(1, fd.Length, 9);
                Assert.Equal(0, fd.Dot(m.GetTriNormal(t)), 9);
                sum += cf.FaceIndex(t) / 4.0; if (cf.FaceIndex(t) != 0) count++;
            }
            Assert.Equal(count, cf.SingularFaces.Count);
            Assert.Equal(count, cf.IndexHistogram.Values.Sum());
            Assert.Equal(sum, cf.IndexSum, 12);
        }

        static int EulerCharacteristic(DMesh3 m) => m.VertexCount - m.EdgeCount + m.TriangleCount;

        [Fact]
        public void Sphere_SmoothestField_HasEightQuarterConesAndPoincareHopf()
        {
            var cf = Run(CrossFieldShapes.UnitSphere());
            CheckResult(cf);
            Assert.True(cf.Converged);
            Assert.Equal(EulerCharacteristic(cf.Mesh), cf.IndexSum, 9);                 // Poincare-Hopf: chi = 2
            Assert.Equal(new[] { 1 }, cf.IndexHistogram.Keys);                         // only +1/4
            Assert.Equal(8, cf.IndexHistogram[1]);                                     // the minimum on genus 0
        }

        [Fact]
        public void FilletedBox_SmoothestField_HasEightQuarterCones()
        {
            var cf = Run(CrossFieldShapes.FilletedBoxMesh());
            CheckResult(cf);
            Assert.Equal(2, cf.IndexSum, 9);
            Assert.Equal(8, cf.SingularFaces.Count);
            Assert.Equal(new[] { 1 }, cf.IndexHistogram.Keys);
        }

        [Fact]
        public void Torus_CurvatureAligned_HasNoSingularity_SmoothestHasSome()
        {
            DMesh3 m = CrossFieldShapes.TorusMesh();
            var smooth = Run(m);
            var aligned = Run(m, 10);
            CheckResult(aligned);
            Assert.Equal(0, smooth.IndexSum, 9);                                         // chi = 0 for both
            Assert.Equal(0, aligned.IndexSum, 9);
            Assert.True(smooth.SingularFaces.Count > 0, "the smoothest field of a torus has singularities (pairs of +1/4 and -1/4)");
            Assert.Empty(aligned.SingularFaces);
            // and the field follows the principal directions: the cross has a direction along the big circle
            double worst = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d c = m.GetTriCentroid(t);
                Vector3d around = new Vector3d(-c.y, c.x, 0);
                worst = Math.Max(worst, CrossAngle(aligned.FaceDirection(t), around));
            }
            Assert.True(worst < 12 * MathUtil.Deg2Rad, "largest deviation from the principal directions " + worst / MathUtil.Deg2Rad + " degrees");
        }

        [Fact]
        public void NoisyTorus_CurvatureAligned_HasNoSingularity()
        {
            // vertices moved by up to 0.2 edge lengths: the raw principal directions are nearly random, the smoothed ones carry the field
            var cf = Run(CrossFieldShapes.NoisyTorus(), 10);
            CheckResult(cf);
            Assert.Equal(0, cf.IndexSum, 9);
            Assert.Empty(cf.SingularFaces);
        }

        [Fact]
        public void CurvatureAlignment_DoesNotAddConesToRoundShapes()
        {
            // a sphere (and a noisy one) is nearly umbilic everywhere: its curvature directions are noise and the aligned field must stay the smoothest one
            foreach (DMesh3 m in new[] { CrossFieldShapes.UnitSphere(), CrossFieldShapes.NoisySphere(), CrossFieldShapes.FilletedBoxMesh() }) {
                var cf = Run(m, 10);
                CheckResult(cf);
                Assert.Equal(2, cf.IndexSum, 9);
                Assert.Equal(8, cf.SingularFaces.Count);
                Assert.Equal(new[] { 1 }, cf.IndexHistogram.Keys);
            }
        }

        [Fact]
        public void CurvatureAlignment_StrongWeightOnNoisyRoundShape_StaysNearTheIdeal()
        {
            // without the anisotropy factor in the weight the noise of the curvature directions makes over a hundred cones here
            var cf = Run(CrossFieldShapes.NoisySphere(), 1000);
            Assert.Equal(2, cf.IndexSum, 9);
            Assert.True(cf.SingularFaces.Count <= 8 + 12, "singular faces " + cf.SingularFaces.Count);
        }

        [Fact]
        public void SharpBox_CreaseAlignment_PutsAConeOnEachCornerAndAlignsTheEdges()
        {
            DMesh3 m = CrossFieldShapes.SharpBox();
            var cf = Run(m, 0, c => { c.AlignCreases = true; c.CreaseAngleDegrees = 30; });
            CheckResult(cf);
            Assert.Equal(2, cf.IndexSum, 9);
            Assert.True(cf.SingularFaces.Count <= 8 + 4, "singular faces " + cf.SingularFaces.Count);
            // every crease edge (dihedral angle 90) lies along a cross direction at both of its vertices (not at the corners,
            // where three creases meet at 120 degrees in the tangent plane of the vertex and cannot all be along the cross)
            var creaseEdges = new List<int>(); var creaseCount = new Dictionary<int, int>();
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e);
                if (et.b == DMesh3.InvalidID) continue;
                if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) > 0.5) continue;
                creaseEdges.Add(e);
                Index2i ev = m.GetEdgeV(e);
                int ca, cb; creaseCount.TryGetValue(ev.a, out ca); creaseCount.TryGetValue(ev.b, out cb);
                creaseCount[ev.a] = ca + 1; creaseCount[ev.b] = cb + 1;
            }
            Assert.Equal(12 * 8, creaseEdges.Count);
            foreach (int e in creaseEdges) {
                Index2i ev = m.GetEdgeV(e);
                Vector3d d = m.GetVertex(ev.b) - m.GetVertex(ev.a);
                foreach (int v in new[] { ev.a, ev.b })
                    if (creaseCount[v] == 2)
                        Assert.True(CrossAngle(cf.VertexDirection(v), d) < 5 * MathUtil.Deg2Rad, "edge " + e + " at vertex " + v);
            }
        }

        [Fact]
        public void OpenMesh_BoundaryAlignment_AlignsTheBoundaryEdges()
        {
            DMesh3 m = CrossFieldShapes.OpenPatch();
            var cf = Run(m);
            CheckResult(cf);
            int checkedVertices = 0;
            foreach (int e in m.BoundaryEdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                Vector3d d = m.GetVertex(ev.b) - m.GetVertex(ev.a);
                foreach (int v in new[] { ev.a, ev.b }) {
                    Assert.True(CrossAngle(cf.VertexDirection(v), d) < 5 * MathUtil.Deg2Rad, "boundary edge " + e);
                    checkedVertices++;
                }
            }
            Assert.Equal(2 * 4 * 24, checkedVertices);
            // the same field with AlignBoundary off is not along the boundary, so the test above checks the constraint
            var free = Run(m, 0, c => c.AlignBoundary = false);
            double worst = m.BoundaryEdgeIndices().Max(e => { Index2i ev = m.GetEdgeV(e); return CrossAngle(free.VertexDirection(ev.a), m.GetVertex(ev.b) - m.GetVertex(ev.a)); });
            Assert.True(worst > 10 * MathUtil.Deg2Rad, "the unaligned field is not along the boundary by chance: " + worst / MathUtil.Deg2Rad);
        }

        [Fact]
        public void ZigzagBoundary_IsASuccessfulCompute()
        {
            var cf = Run(CrossFieldShapes.SphereCap());
            CheckResult(cf);
            Assert.True(cf.Converged);
        }

        [Fact]
        public void BadInput_IsReportedNotThrown()
        {
            Assert.Equal(CrossFieldStatus.InvalidMesh, new CrossField(new DMesh3()).Compute());

            // two triangles that share only a vertex (a bowtie)
            var bow = new DMesh3();
            int a = bow.AppendVertex(new Vector3d(0, 0, 0)), b = bow.AppendVertex(new Vector3d(1, 0, 0)), c = bow.AppendVertex(new Vector3d(0, 1, 0));
            int d = bow.AppendVertex(new Vector3d(-1, 0, 0)), e = bow.AppendVertex(new Vector3d(0, -1, 0));
            bow.AppendTriangle(a, b, c); bow.AppendTriangle(a, d, e);
            var cf = new CrossField(bow);
            Assert.Equal(CrossFieldStatus.NotManifold, cf.Compute());
            Assert.False(cf.Success);
            Assert.NotNull(cf.Error);
            Assert.False(cf.HasField(a));

            DMesh3 nan = CrossFieldShapes.SphereCap();
            nan.SetVertex(nan.VertexIndices().First(), new Vector3d(double.NaN, 0, 0));
            Assert.Equal(CrossFieldStatus.InvalidMesh, new CrossField(nan).Compute());

            var flat = new DMesh3();
            int p = flat.AppendVertex(new Vector3d(0, 0, 0)), q = flat.AppendVertex(new Vector3d(1, 0, 0)), r = flat.AppendVertex(new Vector3d(2, 0, 0));
            flat.AppendTriangle(p, q, r);
            Assert.Equal(CrossFieldStatus.InvalidMesh, new CrossField(flat).Compute());
        }

        [Fact]
        public void SeveralComponents_GetIndependentFields()
        {
            DMesh3 m = CrossFieldShapes.SphereCap();
            DMesh3 box = CrossFieldShapes.SharpBox();
            MeshEditor.Append(m, box);
            var cf = Run(m, 0, c => c.AlignCreases = true);
            CheckResult(cf);
            Assert.Equal(2, cf.Components);
            Assert.True(cf.Converged);
        }

        [Fact]
        public void Compute_CanBeRepeatedOnOneInstance()
        {
            DMesh3 m = CrossFieldShapes.FilletedBoxMesh();
            var fresh = Run(m, 3);
            var cf = new CrossField(m) { CurvatureAlignment = 3 };
            cf.Compute();
            cf.Compute();
            AssertSame(fresh, cf);
            // changed settings are used by the next run
            cf.CurvatureAlignment = 0;
            cf.Compute();
            AssertSame(Run(m, 0), cf);
        }

        [Fact]
        public void Compute_IsDeterministic()
        {
            DMesh3 m = CrossFieldShapes.TorusMesh();
            AssertSame(Run(m, 10), Run(m, 10));
            AssertSame(Run(m), Run(m));
        }

        [Fact]
        public void ParallelLoops_EqualSerialOnes_BitForBit()
        {
            DMesh3 m = CrossFieldShapes.TorusMesh();
            var parallel = Run(m, 10);
            foreach (int order in new[] { 0, -1, 7 }) {
                gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
                CrossField serial;
                try { serial = Run(m, 10); }
                finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
                AssertSame(parallel, serial);
                foreach (int t in m.TriangleIndices())
                    for (int k = 0; k < 4; ++k) {
                        Vector3d a = parallel.FaceDirection(t, k), b = serial.FaceDirection(t, k);
                        Assert.Equal(BitConverter.DoubleToInt64Bits(a.x), BitConverter.DoubleToInt64Bits(b.x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(a.y), BitConverter.DoubleToInt64Bits(b.y));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(a.z), BitConverter.DoubleToInt64Bits(b.z));
                    }
            }
        }

        static void AssertSame(CrossField x, CrossField y)
        {
            Assert.Equal(x.Iterations, y.Iterations);
            Assert.Equal(BitConverter.DoubleToInt64Bits(x.Eigenvalue), BitConverter.DoubleToInt64Bits(y.Eigenvalue));
            foreach (int v in x.Mesh.VertexIndices()) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(x.VertexAngle(v)), BitConverter.DoubleToInt64Bits(y.VertexAngle(v)));
                Assert.Equal(BitConverter.DoubleToInt64Bits(x.VertexMagnitude(v)), BitConverter.DoubleToInt64Bits(y.VertexMagnitude(v)));
            }
            foreach (int t in x.Mesh.TriangleIndices())
                Assert.Equal(x.FaceIndex(t), y.FaceIndex(t));
            Assert.Equal(x.SingularFaces, y.SingularFaces);
        }

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory, MemberData(nameof(Frames))]
        public void Field_IsInvariantUnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = CrossFieldShapes.TorusMesh();
            // curvature aligned: a well conditioned eigenproblem (the plain smoothest field of a torus has near-degenerate eigenvalues)
            var cfBase = Run(m, 10);
            var cf = Run(Invariance.Transformed(m, s), 10);
            Assert.Equal(cfBase.SingularFaces, cf.SingularFaces);
            foreach (int t in m.TriangleIndices()) Assert.Equal(cfBase.FaceIndex(t), cf.FaceIndex(t));
            if (s.IsExact) {
                // scaling by a power of two: the same angles bit for bit, the eigenvalue (1 / area) scaled
                Assert.Equal(cfBase.Iterations, cf.Iterations);
                foreach (int v in m.VertexIndices())
                    Assert.Equal(BitConverter.DoubleToInt64Bits(cfBase.VertexAngle(v)), BitConverter.DoubleToInt64Bits(cf.VertexAngle(v)));
                Assert.Equal(cfBase.Eigenvalue / (s.Scale * s.Scale), cf.Eigenvalue);
            } else {
                double worst = 0;
                foreach (int v in m.VertexIndices())
                    worst = Math.Max(worst, CrossAngle(s.ApplyDirection(cfBase.VertexDirection(v)), cf.VertexDirection(v)));
                Assert.True(worst < 1e-4, name + ": largest direction difference " + worst);
            }
        }

        [Fact]
        public void Field_DoesNotDependOnTheRandomStart()
        {
            // the gauge (phase of the smoothest field, sign of an aligned one) is fixed by the data, not by the start vector
            foreach (var (m, s, creases) in new[] { (CrossFieldShapes.FilletedBoxMesh(), 0.0, false), (CrossFieldShapes.UnitSphere(), 0.0, false), (CrossFieldShapes.TorusMesh(), 10.0, false), (CrossFieldShapes.SharpBox(), 0.0, true) }) {
                var a = Run(m, s, c => { c.Seed = 0; c.Tolerance = 1e-8; c.AlignCreases = creases; });
                var b = Run(m, s, c => { c.Seed = 5; c.Tolerance = 1e-8; c.AlignCreases = creases; });
                Assert.Equal(a.SingularFaces, b.SingularFaces);
                double worst = 0;
                foreach (int v in m.VertexIndices()) worst = Math.Max(worst, CrossAngle(a.VertexDirection(v), b.VertexDirection(v)));
                Assert.True(worst < 1e-3, "largest direction difference between two starts " + worst);
                // not only the lines: the same direction 0 (a flip by 45 degrees is a different cross; one by 90 degrees the same)
                foreach (int v in m.VertexIndices()) Assert.True(Math.Abs(Math.Cos(4 * (a.VertexAngle(v) - b.VertexAngle(v))) - 1) < 1e-4);
            }
        }

        [Fact]
        public void FaceDirections_AreInvariantUnderATinyPowerOfTwoScale()
        {
            // Vector3d.Normalized has an absolute length threshold that a cross product of two tiny edges falls under
            DMesh3 m = CrossFieldShapes.FilletedBoxMesh();
            var a = Run(m);
            var b = Run(Invariance.Transformed(m, new Similarity("scale 2^-40", Math.Pow(2, -40), Matrix3d.Identity, Vector3d.Zero)));
            foreach (int t in m.TriangleIndices())
                for (int k = 0; k < 4; ++k) {
                    Vector3d x = a.FaceDirection(t, k), y = b.FaceDirection(t, k);
                    Assert.Equal(1, y.Length, 9);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(x.x), BitConverter.DoubleToInt64Bits(y.x));
                }
        }

        [Fact]
        public void ComponentsOfVeryDifferentSize_AreAllSolved()
        {
            DMesh3 m = CrossFieldShapes.UnitSphere();
            var tiny = CrossFieldShapes.OpenPatch(2);
            foreach (int v in tiny.VertexIndices()) tiny.SetVertex(v, tiny.GetVertex(v) * 1e-3 + new Vector3d(5, 0, 0));
            MeshEditor.Append(m, tiny);
            var cf = Run(m, 0, c => c.AlignBoundary = false);
            Assert.Equal(2, cf.Components);
            CheckResult(cf);
        }

        [Fact]
        public void NoIterationBudget_IsNotASilentSuccess()
        {
            var cf = new CrossField(CrossFieldShapes.FilletedBoxMesh()) { MaxIterations = 0 };
            CrossFieldStatus st = cf.Compute();
            Assert.True(st != CrossFieldStatus.Success || !cf.Converged);
        }

        [Fact]
        public void SmoothestField_IsInvariantUnderPowersOfTwo()
        {
            // no alignment: the phase of the field is fixed by the gauge, which must not depend on the scale either
            DMesh3 m = CrossFieldShapes.FilletedBoxMesh();
            var cfBase = Run(m);
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                var cf = Run(Invariance.Transformed(m, s));
                foreach (int v in m.VertexIndices())
                    Assert.Equal(BitConverter.DoubleToInt64Bits(cfBase.VertexAngle(v)), BitConverter.DoubleToInt64Bits(cf.VertexAngle(v)));
                Assert.Equal(cfBase.SingularFaces, cf.SingularFaces);
            }
        }
    }
}
