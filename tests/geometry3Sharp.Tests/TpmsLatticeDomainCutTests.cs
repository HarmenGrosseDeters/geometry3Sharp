using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TpmsLattice (#1372): with a DomainMesh the cut lies at the mesh up to the rounding of the contour. The distance to the
    /// mesh used to be the distance to its bounding box outside the box, a lower bound: a cube edge from a node inside the
    /// bounds to one outside them put the cut up to 0.7 cube outside the domain.
    /// </summary>
    public class TpmsLatticeDomainCutTests
    {
        const int Divisions = 10;
        const double Cube = 1.0 / Divisions;
        // the cut is within this many cubes of the domain mesh (measured 0.01 with the fix, 0.72 before)
        const double BoundInCubes = 0.1;

        static DMesh3 Sphere(double radius, Vector3d center)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = 16 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + center);
            return m;
        }

        public static IEnumerable<object[]> Domains() =>
            from mode in new[] { "Skeletal", "Sheet", "TwoLevelSheet" }
            from d in new[] { (0.838, 0.1, 0.2, 0.3), (1.464, 0.7, 0.4, 0.9), (0.690, 0.5, 0.1, 0.6), (0.570, 0.9, 0.8, 0.2) }
            select new object[] { mode, d.Item1, d.Item2, d.Item3, d.Item4 };

        static TpmsLattice Make(string mode, DMesh3 domain)
        {
            return new TpmsLattice {
                Type = TpmsType.Gyroid, Style = mode == "Skeletal" ? TpmsStyle.SkeletalLow : TpmsStyle.Sheet, CellSize = 1, Divisions = Divisions,
                VolumeFraction = 0.5, DomainMesh = domain, TwoLevelSheet = mode == "TwoLevelSheet"
            };
        }

        // the largest distance of a vertex of the result that is outside the domain mesh
        static double Overshoot(DMesh3 result, DMesh3 domain)
        {
            var tree = new DMeshAABBTree3(domain, true);
            double worst = 0;
            foreach (Vector3d v in result.Vertices()) {
                if (tree.FastWindingNumber(v) > 0.5) continue;
                tree.FindNearestTriangle(v, out double d2);
                worst = Math.Max(worst, Math.Sqrt(d2));
            }
            return worst;
        }

        [Theory]
        [MemberData(nameof(Domains))]
        public void TheCutLiesAtTheDomainMesh(string mode, double radius, double cx, double cy, double cz)
        {
            DMesh3 domain = Sphere(radius, new Vector3d(cx, cy, cz));
            TpmsLattice t = Make(mode, domain);
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(t.Mesh));
            Assert.True(Invariance.AllFinite(t.Mesh));
            Assert.True(t.Mesh.IsClosed());
            double over = Overshoot(t.Mesh, domain);
            Assert.True(over <= BoundInCubes * Cube, mode + ": the cut is " + over / Cube + " cubes outside the domain");
            // the lattice still fills the domain: a vertex near the rim
            Assert.True(t.Mesh.Vertices().Max(v => (v - new Vector3d(cx, cy, cz)).Length) > radius - 2 * Cube);
        }

        [Theory]
        [InlineData("Skeletal")]
        [InlineData("TwoLevelSheet")]
        public void InvariantUnderSimilarities_ReusableAndParallelEqualsSerial(string mode)
        {
            DMesh3 domain = Sphere(0.838, new Vector3d(0.1, 0.2, 0.3));
            DMesh3 reference = null;
            foreach (Similarity s in Similarity.All()) {
                TpmsLattice t = Make(mode, Invariance.Transformed(domain, s));
                t.CellSize = s.Scale;
                t.Origin = s.Apply(Vector3d.Zero);
                t.Rotation = s.Rotation;
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, s.Name + " " + t.Status + ": " + t.Message);
                DMesh3 back = Invariance.MappedBack(t.Mesh, s);
                Assert.True(Overshoot(back, domain) <= BoundInCubes * Cube, s.Name);
                if (reference == null) { reference = back; continue; }
                Assert.Equal(reference.TriangleCount, back.TriangleCount);
                if (s.IsExact) Assert.Equal("", Invariance.Difference(reference, back));
            }
            TpmsLattice serial = Make(mode, domain); serial.ParallelCompute = false; serial.Compute();
            TpmsLattice par = Make(mode, domain); par.ParallelCompute = true;
            for (int i = 0; i < 3; ++i) {
                par.Compute();
                Assert.Equal("", Invariance.Difference(serial.Mesh, par.Mesh));    // repeated runs on one instance
            }
        }
    }
}
