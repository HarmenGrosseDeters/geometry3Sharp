using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The intrinsic refinement on open meshes under similarities (docs/QA.md 2.1). The boundary splits at small input
    /// angles placed their concentric shells at absolute powers of two, so a mesh in millimetres was refined
    /// differently from the same mesh in metres: geodesic distances differed by 4 % at scale 1e-3.
    /// </summary>
    public class IntrinsicScaleInvarianceTests
    {
        /// <summary>A bumpy sphere (no exactly cocircular quads) with a cap removed: an open mesh with sharp boundary corners.</summary>
        static DMesh3 OpenBumpySphere()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.15 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5) + 0.05 * p.z));
            }
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(t).z > 0.6) m.RemoveTriangle(t);
            return m;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void GeodesicDistance_OpenMesh_ScalesWithTheInput(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 mesh = OpenBumpySphere();
            int src = mesh.VertexIndices().First(v => mesh.IsBoundaryVertex(v) == false);
            foreach (bool refine in new[] { false, true }) {
                var g0 = new MeshGeodesicDistance(mesh) { IntrinsicRefine = refine };
                Assert.True(g0.Compute(new[] { src }));
                Assert.True(g0.Refinement != null && g0.Refinement.BoundarySplits > 0, "the boundary splits are exercised");
                var g = new MeshGeodesicDistance(Invariance.Transformed(mesh, s)) { IntrinsicRefine = refine };
                Assert.True(g.Compute(new[] { src }));
                Assert.Equal(g0.Refinement.InsertedVertices, g.Refinement.InsertedVertices);
                double tol = s.IsExact ? 1e-12 : 1e-9 + 64 * s.Resolution(2);
                foreach (int vid in mesh.VertexIndices())
                    Assert.True(Math.Abs(g0.Distance[vid] - g.Distance[vid] / s.Scale) < tol,
                        $"{s.Name}, refine {refine}, vertex {vid}: {g0.Distance[vid]} vs {g.Distance[vid] / s.Scale}");
            }
        }

        /// <summary>A 10 degree wedge at the origin with legs of different lengths and edge lengths, scaled by s.</summary>
        static DMesh3 Wedge(double s)
        {
            double angle = 10 * Math.PI / 180;
            double[] da = { 0.3, 0.7, 1.2, 2.0 }, db = { 0.5, 1.1, 1.6 };
            var m = new DMesh3();
            int o = m.AppendVertex(Vector3d.Zero);
            int[] A = da.Select(d => m.AppendVertex(s * new Vector3d(d, 0, 0))).ToArray();
            int[] B = db.Select(d => m.AppendVertex(s * new Vector3d(d * Math.Cos(angle), d * Math.Sin(angle), 0))).ToArray();
            m.AppendTriangle(o, A[0], B[0]);
            int ia = 1, ib = 1, pa = A[0], pb = B[0];
            while (ia < A.Length || ib < B.Length) {
                if (ib >= B.Length || (ia < A.Length && da[ia] <= db[ib])) { m.AppendTriangle(pa, A[ia], pb); pa = A[ia++]; }
                else { m.AppendTriangle(pa, B[ib], pb); pb = B[ib++]; }
            }
            return m;
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-3)]
        [InlineData(1e3)]
        public void Refine_SmallAngle_SplitsBothLegsOnTheSameShell(double scale)
        {
            // Shewchuk's concentric shells: the splits next to a small angle lie at the same distances (unit times a
            // power of two) on both legs, so that they do not encroach each other forever. One unit for the whole
            // mesh, not per edge (each leg has its own edge lengths here).
            var T = new IntrinsicTriangulation(Wedge(scale));
            IntrinsicRefinementResult r = T.DelaunayRefine(25);
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(r.BoundarySplits > 0);
            double nearest0 = double.MaxValue, nearest1 = double.MaxValue;
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i) {
                if (T.IsBoundaryVertex(i) == false) continue;
                Vector3d p = T.VertexPosition(i);
                if (Math.Abs(p.y) < 1e-9 * scale) nearest0 = Math.Min(nearest0, p.Length);
                else nearest1 = Math.Min(nearest1, p.Length);
            }
            Assert.True(nearest0 < double.MaxValue && nearest1 < double.MaxValue, "splits on both legs");
            Assert.Equal(nearest0, nearest1, 9);
            double k = Math.Log(nearest0 / T.MeanEdgeLength, 2);
            Assert.True(Math.Abs(k - Math.Round(k)) < 1e-9, $"{nearest0} is not the unit {T.MeanEdgeLength} times a power of two");
        }
    }
}
