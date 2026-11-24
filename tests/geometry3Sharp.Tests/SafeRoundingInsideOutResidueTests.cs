using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1067 (abc-chained iud p0008): a thin closed shell of near-zero volume that is inside out survived the removal of
    /// tiny and thin components after the rounding, because the removal only looked at components with an edited vertex
    /// or a degenerate triangle.
    /// </summary>
    public class SafeRoundingInsideOutResidueTests
    {
        // a double cone over an n-gon of radius r, apexes at +-h (2n triangles); inverted = inside out
        internal static DMesh3 Bipyramid(int n, double r, double h, Vector3d at, bool inverted)
        {
            var m = new DMesh3();
            int top = m.AppendVertex(at + new Vector3d(0, 0, h)), bot = m.AppendVertex(at + new Vector3d(0, 0, -h));
            var ring = new int[n];
            for (int i = 0; i < n; ++i)
                ring[i] = m.AppendVertex(at + new Vector3d(r * Math.Cos(2 * Math.PI * i / n), r * Math.Sin(2 * Math.PI * i / n), 0));
            for (int i = 0; i < n; ++i) {
                int a = ring[i], b = ring[(i + 1) % n];
                if (inverted) { m.AppendTriangle(a, top, b); m.AppendTriangle(b, bot, a); }
                else { m.AppendTriangle(a, b, top); m.AppendTriangle(b, a, bot); }
            }
            return m;
        }

        // a unit sphere with needles (every 9th edge split at 3e-10: degenerate triangles, so that the repair edits and
        // looks at the components), and the residue beside it
        internal static DMesh3 SphereWithNeedlesAndResidue(DMesh3 residue)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            foreach (int eid in s.EdgeIndices().ToArray()) {
                if (eid % 9 != 0)
                    continue;
                Index2i ev = s.GetEdgeV(eid);
                Vector3d p = s.GetVertex(ev.a), q = s.GetVertex(ev.b);
                Assert.Equal(MeshResult.Ok, s.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                s.SetVertex(info.vNew, p + 3e-10 * (q - p).Normalized);
            }
            new MeshEditor(s).AppendMesh(residue);
            return s;
        }

        static DMesh3 Residue() => Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 0), true);

        // the repaired copy of m and its repair
        static (DMesh3 Mesh, MeshRoundingRepair Repair) Repaired(DMesh3 m)
        {
            var copy = new DMesh3(m);
            var r = new MeshRoundingRepair(copy);
            r.Apply();
            return (copy, r);
        }

        static string Positions(DMesh3 m, Similarity s)
        {
            DMesh3 back = Invariance.MappedBack(m, s);
            return string.Join(";", back.VertexIndices().Select(v => back.GetVertex(v)).Select(p => $"{p.x:R},{p.y:R},{p.z:R}"));
        }

        /// <summary>
        /// A thin inside-out shell (34 triangles, volume about 1e-17) beside a solid that the repair edits: before the fix it
        /// stayed (the removal only looked at components with an edit or a degenerate triangle) and the result was
        /// "inside-out components". Now it goes, the solid stays, and the result is closed, manifold and clean
        /// </summary>
        [Fact]
        public void UntouchedInsideOutResidue_IsRemoved()
        {
            DMesh3 input = SphereWithNeedlesAndResidue(Residue());
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(input, false));
            var (m, r) = Repaired(input);
            Assert.True(r.IsClean);
            Assert.Equal(1, r.RemovedComponents);
            Assert.Equal(34, r.RemovedTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
            Assert.True(Invariance.Volume(m) > 4);
        }

        /// <summary> the same residue inside the solid is a cavity, a legitimate result: it stays, and the result is clean </summary>
        [Fact]
        public void EnclosedCavityOfTheSameShape_Stays()
        {
            DMesh3 input = SphereWithNeedlesAndResidue(Bipyramid(17, 1e-4, 1e-8, new Vector3d(0.1, 0.05, 0), true));
            var (m, r) = Repaired(input);
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal(34, m.TriangleCount - Repaired(SphereWithNeedlesAndResidue(new DMesh3())).Mesh.TriangleCount);
        }

        /// <summary> the same shell with the right orientation is a solid that was not touched: it stays </summary>
        [Fact]
        public void UntouchedOutsideInThinSolid_Stays()
        {
            var (m, r) = Repaired(SphereWithNeedlesAndResidue(Bipyramid(17, 1e-4, 1e-8, new Vector3d(3, 0, 0), false)));
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> invariance over Similarity.All(): removed in every frame; in the exact ones (2^k scales) the result is bit for bit the same </summary>
        [Fact]
        public void Removal_Invariance()
        {
            DMesh3 input = SphereWithNeedlesAndResidue(Residue());
            string expected = Positions(Repaired(input).Mesh, Similarity.Identity);
            foreach (Similarity s in Similarity.All()) {
                var (m, r) = Repaired(Invariance.Transformed(input, s));
                Assert.True(r.RemovedComponents == 1 && r.RemovedTriangles == 34, s.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
                if (s.IsExact)
                    Assert.Equal(expected, Positions(m, s));
            }
        }

        // a needle on the first edge of m: its new vertex 3e-10 from the first end (a degenerate triangle: the repair edits there)
        static void Needle(DMesh3 m)
        {
            int eid = m.EdgeIndices().First();
            Index2i ev = m.GetEdgeV(eid);
            Vector3d p = m.GetVertex(ev.a), q = m.GetVertex(ev.b);
            Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
            m.SetVertex(info.vNew, p + 3e-10 * (q - p).Normalized);
        }

        /// <summary>
        /// A wrongly oriented thin plate that is not tiny (area 0.016, thickness 2e-6) may be a real foil: it is not
        /// deleted, the result is still reported as inside out. Only a tiny residue goes
        /// </summary>
        [Fact]
        public void UntouchedInsideOutPlateThatIsNotTiny_StaysAndIsReported()
        {
            var (m, r) = Repaired(SphereWithNeedlesAndResidue(Bipyramid(17, 0.05, 1e-6, new Vector3d(3, 0, 0), true)));
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal("inside-out components", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary>
        /// A tiny edited solid that encloses the residue. Since #1216 the solid stays (the repair contracts its needle and leaves
        /// it as it was), so the residue is its cavity and stays too: its nesting is judged without the components that are
        /// removed, and none is. (Before, every tiny edited component went, and the residue with it.)
        /// </summary>
        [Fact]
        public void ResidueInsideATinyEditedSolid_StaysAsItsCavity()
        {
            DMesh3 outer = Bipyramid(17, 5e-5, 5e-5, new Vector3d(3, 0, 0), false);
            Needle(outer);
            new MeshEditor(outer).AppendMesh(Bipyramid(17, 3e-5, 1e-9, new Vector3d(3, 0, 0), true));
            DMesh3 input = SphereWithNeedlesAndResidue(outer);
            var (m, r) = Repaired(input);
            Assert.Equal(0, r.RemovedComponents);
            Assert.True(r.IsClean);
            Assert.Equal(MeshConnectedComponents.Separate(input).Length, MeshConnectedComponents.Separate(m).Length);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed());
            Assert.True(Invariance.Volume(m) > 4);
        }

        /// <summary>
        /// The removal runs where the repair edits or finds something (the gate at its call): the residue next to an
        /// unedited solid stays and is reported as inside out, as before the fix. Pinned as the documented limit
        /// </summary>
        [Fact]
        public void ResidueWithNothingToRepair_StaysAndIsReported()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            new MeshEditor(m).AppendMesh(Residue());
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal("inside-out components", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> two Apply calls on one instance (the second on the result of the first) leave the result as it is </summary>
        [Fact]
        public void Reuse_SecondApplyChangesNothing()
        {
            DMesh3 m = SphereWithNeedlesAndResidue(Residue());
            var r = new MeshRoundingRepair(m);
            r.Apply();
            string once = Positions(m, Similarity.Identity);
            r.Apply();
            Assert.Equal(once, Positions(m, Similarity.Identity));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> repeated runs on equal inputs give the same result, bit for bit </summary>
        [Fact]
        public void Removal_IsDeterministic()
        {
            DMesh3 input = SphereWithNeedlesAndResidue(Residue());
            Assert.Equal(Positions(Repaired(input).Mesh, Similarity.Identity), Positions(Repaired(input).Mesh, Similarity.Identity));
        }
    }
}
