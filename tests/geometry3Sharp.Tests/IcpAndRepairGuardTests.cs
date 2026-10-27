using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshICP (docs/ROBUSTNESS_SWEEP.md R8, R12): the transformation update was composed wrongly (the translation missed
    /// the rotation update), which diverged at UTM coordinates; the convergence tolerance was an absolute 1e-5; a NaN or
    /// infinite source vertex gave NaN sums, and the SVD of the NaN matrix looped forever. MeshAutoRepair (R11) threw from deep inside for an infinite vertex.
    /// </summary>
    public class IcpAndRepairGuardTests
    {
        static readonly Vector3d Offset = new Vector3d(0.03, -0.02, 0.01);

        static (MeshICP icp, DMesh3 source, DMesh3 target) Align(Similarity s, Action<DMesh3> editSource = null, bool twice = false)
        {
            DMesh3 target = Invariance.Transformed(Defects.Sphere(Vector3d.Zero, 1, 6), s);
            DMesh3 source = new DMesh3(target);
            // a small rotation about the model and a translation
            Vector3d c = s.Apply(Vector3d.Zero);
            var r = new Quaterniond(s.ApplyDirection(new Vector3d(1, 2, 3).Normalized), 2.0);
            foreach (int v in source.VertexIndices())
                source.SetVertex(v, c + r * (source.GetVertex(v) - c) + s.ApplyDirection(Offset) * s.Scale);
            editSource?.Invoke(source);
            var icp = new MeshICP(source, new DMeshAABBTree3(target, true)) { MaxIterations = 300 };
            icp.Solve();
            if (twice) icp.Solve();
            return (icp, source, target);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Icp_ConvergesAndAligns_UnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            var (icp, source, target) = Align(s);
            Assert.True(icp.Converged, $"{name}: not converged, error {icp.Error / s.Scale}");
            Assert.True(icp.Error / s.Scale < 1e-3, $"{name}: mean distance {icp.Error / s.Scale}");
            // the aligned source vertices are on the target
            var aligned = new DMesh3(source);
            icp.UpdateVertices(aligned);
            double worst = Invariance.VertexDistance(aligned, target) / s.Scale;
            Assert.True(worst < 5e-3, $"{name}: aligned source off the target by {worst}");
            double expected = icp.RelativeConvergeTolerance * target.CachedBounds.DiagonalLength;
            Assert.True(Math.Abs(icp.EffectiveConvergeTolerance - expected) < 1e-3 * expected, $"{name}: tolerance {icp.EffectiveConvergeTolerance} vs {expected}");
        }

        [Fact]
        public void Icp_AtUtm_SameAsAtTheOrigin()
        {
            // upstream: the mean distance grew to 1e8 at UTM coordinates
            var a = Align(Similarity.Identity).icp;
            var b = Align(Similarity.Utm).icp;
            Assert.True(Math.Abs(a.Error - b.Error) < 1e-6, $"{a.Error} vs {b.Error}");
        }

        [Fact]
        public void Icp_ReusedWithUpdate_StaysConverged()
        {
            var (icp, _, _) = Align(Similarity.Utm, twice: true);
            Assert.True(icp.Converged);
            Assert.True(icp.Error < 1e-3);
        }

        [Fact]
        public void Icp_ExplicitTolerance_IsUsedAsGiven()
        {
            DMesh3 target = Defects.Sphere(Vector3d.Zero, 1, 6);
            var icp = new MeshICP(new DMesh3(target), new DMeshAABBTree3(target, true)) { ConvergeTolerance = 1e-3 };
            icp.Solve();
            Assert.Equal(1e-3, icp.EffectiveConvergeTolerance);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Icp_NonFiniteSourceVertex_IgnoredAndFinishes(double bad)
        {
            (MeshICP icp, DMesh3 source, DMesh3 target) r = default;
            Assert.True(Timed.Finishes(() => r = Align(Similarity.Identity, m => m.SetVertex(m.VertexIndices().Skip(7).First(), new Vector3d(bad, 0, 0))),
                TimeSpan.FromSeconds(20)), "did not finish");
            Assert.True(r.icp.Converged);
            Assert.True(r.icp.Translation.IsFinite);
            Assert.True(r.icp.Error < 1e-3, $"mean distance {r.icp.Error}");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void AutoRepair_NonFiniteVertex_StillOrientsTheMesh(double bad)
        {
            // an inside-out sphere with a hole and one non-finite vertex: no exception from inside the orientation
            // repair, the bad vertex does not spread, and the mesh is still turned outward (the bounds of the finite
            // triangles decide; with the bad vertex in them every triangle was skipped and nothing was decided)
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            int top = m.VertexIndices().OrderByDescending(x => m.GetVertex(x).z).ThenBy(x => x).First();
            foreach (int t in m.VtxTrianglesItr(top).ToList()) m.RemoveTriangle(t, true, false);
            foreach (int t in m.TriangleIndices().ToList()) m.ReverseTriOrientation(t);
            int v = m.VertexIndices().Skip(m.VertexCount / 3).First();
            m.SetVertex(v, new Vector3d(bad, 0.5, 0.5));
            var repair = new MeshAutoRepair(m);
            repair.Apply();
            Assert.True(m.Vertices().Count(p => p.IsFinite == false) <= 1);
            // signed volume of the finite triangles (about the origin): positive once oriented outward
            double vol = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in m.TriangleIndices()) {
                m.GetTriVertices(t, ref a, ref b, ref c);
                if (a.IsFinite && b.IsFinite && c.IsFinite) vol += a.Dot(b.Cross(c)) / 6;
            }
            Assert.True(vol > 3, $"signed volume {vol}: not oriented outward");
        }
    }
}
