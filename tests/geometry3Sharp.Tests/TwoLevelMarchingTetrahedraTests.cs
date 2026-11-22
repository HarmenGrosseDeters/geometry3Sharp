using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class TwoLevelMarchingTetrahedraTests
    {
        sealed class Fn : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Fn(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
        }

        const double H = 1.0 / 16;       // cell size of the unit inputs
        static readonly Vector3d Shift = new Vector3d(0.37, 0.11, 0.73);     // the grid is not aligned with the shapes

        // Inputs in a unit frame around the origin, mapped by a similarity: the fields are distances, so they scale with it.
        static TwoLevelMarchingTetrahedra Make(string shape, double thicknessInCells, Similarity s, bool parallel = true)
        {
            double w = thicknessInCells * H / 2;
            Matrix3d tilt = Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33);
            Func<Vector3d, double> g, d = null;
            double extent = 1;
            switch (shape) {
                case "shell": g = p => p.Length - 0.7; break;
                case "plate": g = p => (tilt * p).z; d = p => p.Length - 0.8; break;
                case "gyroid": {
                    double k = 2 * Math.PI / (16 * H);
                    g = p => { Vector3d q = tilt * p; return (Math.Sin(k * q.x) * Math.Cos(k * q.y) + Math.Sin(k * q.y) * Math.Cos(k * q.z) + Math.Sin(k * q.z) * Math.Cos(k * q.x)) / k; };
                    d = p => p.Length - 0.8;
                    break; }
                default: throw new ArgumentException(shape);
            }
            // bounds of the mapped cube [-extent, extent]^3, with a margin, on a grid that is not aligned with the shape
            var box = AxisAlignedBox3d.Empty;
            for (int c = 0; c < 8; ++c) box.Contain(s.Apply(extent * new Vector3d((c & 1) * 2 - 1, ((c >> 1) & 1) * 2 - 1, ((c >> 2) & 1) * 2 - 1)));
            double h = s.Scale * H;
            var gen = new TwoLevelMarchingTetrahedra {
                Field = new Fn(p => s.Scale * g(s.Inverse(p))), HalfWidth = s.Scale * w,
                Domain = d == null ? null : new Fn(p => s.Scale * d(s.Inverse(p))),
                Bounds = new AxisAlignedBox3d(box.Min - h * (2 * Vector3d.One + Shift), box.Max + 2 * h * Vector3d.One), CellSize = h, ParallelCompute = parallel
            };
            return gen;
        }

        static DMesh3 Contour(string shape, double thicknessInCells, Similarity s, bool parallel = true)
        {
            TwoLevelMarchingTetrahedra gen = Make(shape, thicknessInCells, s, parallel);
            gen.Generate();
            Assert.Equal(0, gen.NonManifoldTriangles);
            return gen.Mesh;
        }

        static string Signature(DMesh3 m)
        {
            MeshTopologySummary t = MeshTopologySummary.Compute(m);
            return "c=" + t.Components + ";g=" + string.Join(",", t.ComponentList.Select(c => c.Genus).OrderBy(x => x));
        }

        [Theory]
        [InlineData(1.5)]
        [InlineData(0.5)]
        [InlineData(0.25)]
        [InlineData(0.02)]
        public void Shell_KeepsBothSides_AtAnyThickness(double cells)
        {
            // a wall far below the cell size: closed, manifold, oriented, no degenerate triangles, no self-intersections, and two spheres
            DMesh3 m = Contour("shell", cells, Similarity.Identity);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(Invariance.AllFinite(m));
            Assert.Equal("c=2;g=0,0", Signature(m));
            double w = cells * H / 2, exact = 4.0 / 3 * Math.PI * (Math.Pow(0.7 + w, 3) - Math.Pow(0.7 - w, 3));
            // the faceted sphere loses area, and the sliver guard thickens a very thin wall: within 3 % down to a quarter cell
            if (cells >= 0.25) Assert.InRange(Invariance.Volume(m) / exact, 0.97, 1.03);
            else Assert.InRange(Invariance.Volume(m) / exact, 0.9, 1.6);
        }

        [Fact]
        public void MarchingCubes_LosesTheSameShell()
        {
            // the reason for this class: the single field |g| - w on the same grid gives fragments, not two spheres
            double w = 0.25 * H / 2;
            TwoLevelMarchingTetrahedra gen = Make("shell", 0.25, Similarity.Identity);
            var mc = new MarchingCubes { Implicit = new Fn(p => Math.Abs(p.Length - 0.7) - w), Bounds = gen.Bounds, CubeSize = H, RootMode = MarchingCubes.RootfindingModes.LinearEdges };
            mc.Generate();
            Assert.NotEqual("c=2;g=0,0", Signature(mc.Mesh));
        }

        [Theory]
        [InlineData("plate", 1.5)]
        [InlineData("plate", 0.25)]
        [InlineData("plate", 0.02)]
        [InlineData("gyroid", 1.5)]
        [InlineData("gyroid", 0.25)]
        public void ClippedWall_IsCleanAndClosed(string shape, double cells)
        {
            DMesh3 m = Contour(shape, cells, Similarity.Identity);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(Invariance.AllFinite(m));
            if (shape == "plate") {
                // a disk: one sphere, volume of the slab of the ball (thickness 2 w around a great circle)
                Assert.Equal("c=1;g=0", Signature(m));
                double w = cells * H / 2, exact = 2 * Math.PI * (0.64 * w - w * w * w / 3);
                if (cells >= 0.25) Assert.InRange(Invariance.Volume(m) / exact, 0.97, 1.03);
            } else
                Assert.Equal(1, MeshTopologySummary.Compute(m).Components);
        }

        [Fact]
        public void Gyroid_TopologyDoesNotDependOnTheThickness()
        {
            // both sides are levels of the same interpolant, so a thinner wall does not change how the sheets connect
            string reference = Signature(Contour("gyroid", 1.0, Similarity.Identity));
            foreach (double cells in new[] { 0.5, 0.25, 0.1 })
                Assert.Equal(reference, Signature(Contour("gyroid", cells, Similarity.Identity)));
        }

        [Fact]
        public void HalfWidthF_VariesTheWall()
        {
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity);
            gen.HalfWidthF = p => H * (0.15 + 0.5 * (p.z + 1) / 2);      // 0.15 to 0.65 cells half width, bottom to top
            gen.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(gen.Mesh));
            Assert.Equal("c=2;g=0,0", Signature(gen.Mesh));
            // the wall is thicker at the top: outer radius from the vertices near both poles
            double top = gen.Mesh.Vertices().Where(v => v.z > 0.6).Max(v => v.Length), bottom = gen.Mesh.Vertices().Where(v => v.z < -0.6).Max(v => v.Length);
            Assert.InRange(top - 0.7, 0.55 * H, 0.7 * H);
            Assert.InRange(bottom - 0.7, 0.1 * H, 0.25 * H);
        }

        [Fact]
        public void Similarities_Pow2BitIdentical_AllClean()
        {
            foreach (string shape in new[] { "shell", "gyroid" }) {
                DMesh3 r0 = Contour(shape, 0.3, Similarity.Identity);
                foreach (Similarity sim in Similarity.All()) {
                    DMesh3 r = Contour(shape, 0.3, sim);
                    Assert.True(r.IsClosed(), sim.Name);
                    Assert.True(Invariance.AllFinite(r), sim.Name);
                    DMesh3 back = Invariance.MappedBack(r, sim);
                    if (sim.IsExact)
                        Assert.Equal("", Invariance.Difference(r0, back));
                    else {
                        // another grid position: the same solid up to the cell size, still clean
                        Assert.Equal("", MeshRobustRepair.NotCleanReason(back));
                        Assert.InRange(Invariance.Volume(back) / Invariance.Volume(r0), 0.9, 1.1);
                    }
                }
            }
        }

        [Fact]
        public void Reuse_GenerateTwice_GivesTheSameMesh()
        {
            TwoLevelMarchingTetrahedra gen = Make("gyroid", 0.3, Similarity.Identity);
            gen.Generate();
            var first = new DMesh3(gen.Mesh);
            gen.Generate();
            Assert.Equal("", Invariance.Difference(first, gen.Mesh));
            // and after other options in between
            gen.HalfWidth *= 2; gen.Generate();
            Assert.NotEqual("", Invariance.Difference(first, gen.Mesh));
            gen.HalfWidth /= 2; gen.Generate();
            Assert.Equal("", Invariance.Difference(first, gen.Mesh));
        }

        [Fact]
        public void ParallelEqualsSerial_AndRepeats()
        {
            DMesh3 serial = Contour("gyroid", 0.3, Similarity.Identity, false);
            for (int rep = 0; rep < 3; ++rep)
                Assert.Equal("", Invariance.Difference(serial, Contour("gyroid", 0.3, Similarity.Identity, true)));
        }

        [Fact]
        public void SliverGuard_KeepsPlanesOffTheGridNodes()
        {
            // a wall whose two sides lie exactly in node planes of the grid: every crossing would be at a node
            Func<bool, DMesh3> run = guard => {
                var gen = new TwoLevelMarchingTetrahedra {
                    Field = new Fn(p => p.z), HalfWidth = 0.25, Domain = new Fn(p => Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 0.6),
                    Bounds = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), CellSize = 0.25, SliverGuard = guard ? 1.0 / 256 : 0
                };
                gen.Generate();
                return gen.Mesh;
            };
            DMesh3 guarded = run(true);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(guarded));
            Assert.Equal("c=1;g=0", Signature(guarded));
            // the guard only grows the solid, by less than its fraction of a cell per side
            AxisAlignedBox3d b = guarded.CachedBounds;
            Assert.InRange(b.Max.z, 0.25, 0.25 + 0.25 / 128);
            Assert.InRange(b.Min.z, -0.25 - 0.25 / 128, -0.25);
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(run(false)));
        }

        [Fact]
        public void RimGuard_RemovesTheSliversOfARimNextToAGridEdge()
        {
            // without the guard this clipped gyroid has triangles of almost no area where the rim passes a grid edge
            TwoLevelMarchingTetrahedra off = Make("gyroid", 0.3, Similarity.Identity);
            off.RimGuard = 0;
            off.Generate();
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(off.Mesh));
            Assert.Equal(0, off.RimNodesMoved);
            TwoLevelMarchingTetrahedra on = Make("gyroid", 0.3, Similarity.Identity);
            on.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(on.Mesh));
            Assert.InRange(on.RimNodesMoved, 1, 2000);
            // the guard moves the cut of the domain by a small fraction of a cell only: nearly the same solid
            Assert.InRange(Invariance.Volume(on.Mesh) / Invariance.Volume(off.Mesh), 0.999, 1.001);
            Assert.Equal(off.Mesh.CachedBounds.Max.x, on.Mesh.CachedBounds.Max.x, 3);
        }

        [Fact]
        public void CoincidentCrossings_WithoutTheRimGuard_StillGiveOneArrangement()
        {
            // the domain is exactly the outer side of the wall, so both cross every grid edge at the same parameter: the tie rule
            // must give every tetrahedron the same order, or the pieces do not fit together (the triangles there have no area,
            // which is what RimGuard is for; closed and manifold is the claim here)
            const double w = 0.4 * H;
            var gen = new TwoLevelMarchingTetrahedra {
                Field = new Fn(p => p.Length - 0.7), HalfWidth = w, Domain = new Fn(p => (p.Length - 0.7) - w), RimGuard = 0,
                Bounds = new AxisAlignedBox3d(-Vector3d.One - H * Shift, Vector3d.One), CellSize = H
            };
            gen.Generate();
            Assert.Equal(0, gen.NonManifoldTriangles);
            Assert.True(gen.Mesh.IsClosed());
            Assert.Equal(2, MeshTopologySummary.Compute(gen.Mesh).Components);
        }

        [Fact]
        public void InvalidInput_Throws_AndCancelLeavesNoMesh()
        {
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity);
            gen.HalfWidth = 0;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity); gen.Field = null;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity); gen.CellSize = 0;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity); gen.MaxGridNodes = 1000;
            Assert.Throws<ArgumentException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity); gen.Field = new Fn(p => p.x > 0.5 ? double.NaN : p.Length - 0.7);
            Assert.Throws<InvalidOperationException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity); gen.HalfWidthF = p => p.x > 0.5 ? -1 : 0.01;
            Assert.Throws<InvalidOperationException>(() => gen.Generate());
            gen = Make("shell", 1, Similarity.Identity);
            gen.Generate();
            Assert.NotNull(gen.Mesh);
            gen.CancelF = () => true;
            gen.Generate();
            Assert.Null(gen.Mesh);
        }

        [Fact]
        public void Empty_WhereTheWallIsOutsideTheDomain()
        {
            TwoLevelMarchingTetrahedra gen = Make("shell", 1, Similarity.Identity);
            gen.Domain = new Fn(p => p.Length - 0.3);       // the ball of the domain is inside the shell's cavity
            gen.Generate();
            Assert.Equal(0, gen.Mesh.TriangleCount);
        }
    }
}
