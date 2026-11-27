using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> TpmsLattice.TwoLevelSheet: the sheet as two levels of the surface function (TwoLevelMarchingTetrahedra). </summary>
    public class TpmsLatticeTwoLevelTests
    {
        const double L = 1.0;

        static TpmsLattice Make(bool twoLevel, double wallInCubes, int divisions = 10, double size = 1.5, TpmsType type = TpmsType.Gyroid)
        {
            return new TpmsLattice {
                Type = type, Style = TpmsStyle.Sheet, CellSize = L, Divisions = divisions, WallThickness = wallInCubes * L / divisions,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(size, size, size)), TwoLevelSheet = twoLevel
            };
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, 0.5)]
        [InlineData(TpmsType.Gyroid, 0.2)]
        [InlineData(TpmsType.SchwarzP, 0.3)]
        [InlineData(TpmsType.SchwarzD, 0.3)]
        public void ThinWall_IsCleanAndConnected_WhereMarchingCubesBreaksUp(TpmsType type, double wallInCubes)
        {
            TpmsLattice two = Make(true, wallInCubes, 10, 1.5, type);
            two.Compute();
            Assert.True(two.Status == TpmsStatus.Ok, two.Status + ": " + two.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(two.Mesh));
            Assert.True(Invariance.AllFinite(two.Mesh));
            Assert.Equal(1, MeshTopologySummary.Compute(two.Mesh).Components);
            // the wall is about as thick as asked for (2 V / A; the first-order map of WallThickness and the faceting cost some)
            Assert.InRange(two.MeanWallThickness / (wallInCubes * L / 10), 0.7, 1.4);

            // marching cubes on |f| - t at the same grid: not one connected wall (fragments, or no valid result at all)
            TpmsLattice one = Make(false, wallInCubes, 10, 1.5, type);
            one.Compute();
            Assert.True(one.Status != TpmsStatus.Ok || MeshTopologySummary.Compute(one.Mesh).Components > 1);
        }

        [Fact]
        public void ThickWall_AgreesWithMarchingCubes_AndWithTheField()
        {
            // where the grid resolves the wall both contour the same solid: the volume fraction of {|f| <= level}, sampled here
            // by the midpoint rule from the closed form, within the faceting error (measured: marching cubes -2.5 %, two levels +2.5 %)
            TpmsLattice two = Make(true, 3, 16), one = Make(false, 3, 16);
            two.Compute(); one.Compute();
            Assert.True(two.Status == TpmsStatus.Ok && one.Status == TpmsStatus.Ok, two.Message + one.Message);
            Assert.Equal(one.Level, two.Level);
            const int n = 120; long inside = 0;
            for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) for (int k = 0; k < n; ++k) {
                double x = 2 * Math.PI * 1.5 * (i + 0.5) / n, y = 2 * Math.PI * 1.5 * (j + 0.5) / n, z = 2 * Math.PI * 1.5 * (k + 0.5) / n;
                if (Math.Abs(Math.Sin(x) * Math.Cos(y) + Math.Sin(y) * Math.Cos(z) + Math.Sin(z) * Math.Cos(x)) <= two.Level) ++inside;
            }
            double truth = (double)inside / ((double)n * n * n);
            Assert.InRange(two.RealizedVolumeFraction / truth, 0.96, 1.04);
            Assert.InRange(one.RealizedVolumeFraction / truth, 0.96, 1.04);
            Assert.Equal(MeshTopologySummary.Compute(one.Mesh).Components, MeshTopologySummary.Compute(two.Mesh).Components);
            Assert.Equal(one.CellsEvaluated, two.CellsEvaluated);
        }

        [Fact]
        public void DefaultOff_AndTheOptionChangesOnlyItsOwnRuns()
        {
            Assert.False(new TpmsLattice().TwoLevelSheet);
            TpmsLattice a = Make(false, 3, 12), b = Make(false, 3, 12);
            a.Compute();
            b.TwoLevelSheet = true; b.Compute();
            b.TwoLevelSheet = false; b.Compute();          // reuse: back to marching cubes, the same mesh as a fresh instance
            Assert.Equal("", Invariance.Difference(a.Mesh, b.Mesh));
        }

        [Fact]
        public void InvalidCombinations_AreRejected()
        {
            // each configuration is valid without the option and rejected, by the option's own check, with it
            Action<Action<TpmsLattice>> expect = set => {
                var t = new TpmsLattice { AllowUnresolved = true, Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = L, Divisions = 16, VolumeFraction = 0.3,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)), VerifySelfIntersections = false };
                set(t);
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
                t.TwoLevelSheet = true;
                t.Compute();
                Assert.Equal(TpmsStatus.InvalidOptions, t.Status);
                Assert.Contains("TwoLevelSheet", t.Message);
                Assert.Null(t.Mesh);
            };
            expect(t => t.Style = TpmsStyle.SkeletalLow);
            expect(t => t.Enclosure = true);
            expect(t => { t.SecondType = TpmsType.IWP; t.Blend = TpmsLattice.SigmoidBlend(0, 1, 0.5); });
        }

        [Fact]
        public void GradedFraction_AndDomainMesh_Work()
        {
            TpmsLattice t = Make(true, 1, 10, 1.5);
            t.WallThickness = 0;
            t.VolumeFractionAt = p => 0.03 + 0.1 * p.z / 1.5;         // a wall from well below a cube to about a cube
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(t.Mesh));
            Assert.Equal(1, MeshTopologySummary.Compute(t.Mesh).Components);

            // a graded cell size (the period 1 to 1.5 along z) with a thin wall
            t = Make(true, 0.4, 10, 1.5);
            t.WallThickness = 0; t.VolumeFraction = 0.06;
            t.Box = new AxisAlignedBox3d(new Vector3d(-0.75, -0.75, 0), new Vector3d(0.75, 0.75, 1.5));
            t.Grade = TpmsLattice.LinearGrade(2, 0, 1, 1.5, 1.5, 1.75);
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(t.Mesh));
            Assert.Equal(1, MeshTopologySummary.Compute(t.Mesh).Components);

            var sphere = new Sphere3Generator_NormalizedCube { Radius = 0.7, EdgeVertices = 12 };
            DMesh3 domain = sphere.Generate().MakeDMesh();
            MeshTransforms.Translate(domain, new Vector3d(0.75, 0.75, 0.75));
            t = Make(true, 0.4, 10, 1.5);
            t.DomainMesh = domain;
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(t.Mesh));
            // cut to the ball, within a cube: outside the bounds of the domain mesh the lattice uses a lower bound of the distance
            // (the box distance), so next to the six poles the cut lies up to a cube outside, as with marching cubes
            double r = t.Mesh.Vertices().Max(v => (v - new Vector3d(0.75, 0.75, 0.75)).Length);
            Assert.InRange(r, 0.6, 0.7 + 0.1);
        }

        [Fact]
        public void Compute_IsInvariantUnderSimilarities_Reusable_AndDeterministic()
        {
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                TpmsLattice lattice = Make(true, 0.4, 8, 1);
                lattice.CellSize = s.Scale * L;
                lattice.WallThickness = 0.4 * s.Scale * L / 8;
                lattice.Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale);
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, s.Name + " " + lattice.Status + ": " + lattice.Message);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.GridShiftsUsed, lattice.GridShiftsUsed);
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                DMesh3 back = Invariance.MappedBack(lattice.Mesh, s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, back));
                else
                    foreach (int vid in back.VertexIndices())
                        Assert.True((back.GetVertex(vid) - reference.Mesh.GetVertex(vid)).Length < 1e-6 * L, s.Name);
            }
            // reuse and parallel = serial
            var first = new DMesh3(reference.Mesh);
            reference.Compute();
            Assert.Equal("", Invariance.Difference(first, reference.Mesh));
            reference.ParallelCompute = false;
            reference.Compute();
            Assert.Equal("", Invariance.Difference(first, reference.Mesh));
        }
    }
}
