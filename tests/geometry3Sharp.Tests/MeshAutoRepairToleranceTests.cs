using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAutoRepair tolerances: relative to the size of the mesh by default (the same model in mm, um, km or far
    /// from the origin is repaired the same way), absolute when set explicitly or with ToleranceModes.Absolute.
    /// </summary>
    public class MeshAutoRepairToleranceTests
    {
        /// <summary>
        /// A unit sphere cut into two halves (x &gt; 0 and the rest) whose seam vertices are moved apart by gap: a
        /// crack that the crack merging closes if gap is below the repair tolerance.
        /// </summary>
        static DMesh3 CrackedSphere(double gap)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            var s = new Defects.Soup();
            var map = new Dictionary<int, int>();
            var moved = new Dictionary<int, int>();
            foreach (int v in m.VertexIndices()) map[v] = s.AddVertex(m.GetVertex(v));
            Vector3d dir = new Vector3d(1, 1, 1).Normalized;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                bool right = m.GetTriCentroid(t).x > 0;
                int vid(int v)
                {
                    if (right == false) return map[v];
                    // a seam vertex: also used by a triangle of the other half
                    bool seam = m.VtxTrianglesItr(v).Any(o => m.GetTriCentroid(o).x <= 0);
                    if (seam == false) return map[v];
                    if (moved.TryGetValue(v, out int c) == false)
                        moved[v] = c = s.AddVertex(m.GetVertex(v) + gap * dir);
                    return c;
                }
                s.T.Add(new Index3i(vid(tv.a), vid(tv.b), vid(tv.c)));
            }
            return s.ToMesh();
        }

        /// <summary> a unit sphere and a closed speck 1e-4 across next to it (MinEdgeLengthTol of 1.0.324 collapses it) </summary>
        static DMesh3 SphereAndSpeck()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            MeshEditor.Append(m, Defects.Sphere(new Vector3d(3, 0, 0), 5e-5, 3));
            return m;
        }

        static IEnumerable<(string name, Func<DMesh3> make)> Cases()
        {
            // a crack of 1e-8 (relative tolerance 1e-8 * diagonal 3.5): merged at every scale; with the absolute 1e-6
            // of 1.0.324 not in km
            yield return ("crack", () => CrackedSphere(1e-8));
            // global orientation: in um the triangles of 1.0.324 were all below the absolute area threshold
            yield return ("inside-out", () => Defects.ByName("inside-out").Make());
            yield return ("sphere and speck", SphereAndSpeck);
            yield return ("T-junction", () => Defects.ByName("T-junction").Make());
            yield return ("triangle soup", () => Defects.ByName("triangle soup").Make());
        }

        public static IEnumerable<object[]> CaseNames() =>
            from s in Similarity.All() from c in Cases() select new object[] { s.Name, c.name };

        static MeshAutoRepair Repair(DMesh3 mesh)
        {
            var r = new MeshAutoRepair(mesh);
            Assert.True(r.Apply());
            return r;
        }

        static void AssertClosedSolid(DMesh3 mesh, string what)
        {
            Assert.True(mesh.TriangleCount > 0, what + ": empty");
            Assert.True(mesh.IsClosed(), what + ": not closed");
            Assert.True(Invariance.AllFinite(mesh), what + ": non-finite coordinates");
            Assert.False(mesh.VertexIndices().Any(v => mesh.IsBowtieVertex(v)), what + ": bowtie");
            Assert.True(Invariance.Volume(mesh) > 0, what + ": not outward oriented");
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void SameResultUnderSimilarity(string sname, string cname)
        {
            Similarity s = Similarity.ByName(sname);
            Func<DMesh3> make = Cases().First(c => c.name == cname).make;
            MeshAutoRepair r0 = Repair(make());
            AssertClosedSolid(r0.Mesh, cname);

            MeshAutoRepair r = Repair(Invariance.Transformed(make(), s));
            AssertClosedSolid(r.Mesh, $"{cname}, {s.Name}");
            Assert.Equal(r0.Mesh.TriangleCount, r.Mesh.TriangleCount);
            Assert.Equal(r0.Mesh.VertexCount, r.Mesh.VertexCount);
            DMesh3 back = Invariance.MappedBack(r.Mesh, s);
            if (s.IsExact) {
                // a power of two: the tolerances scale exactly, so the result is the same bit for bit
                Assert.Equal(s.ApplyLength(r0.EffectiveRepairTolerance), r.EffectiveRepairTolerance);
                Assert.Equal(s.ApplyLength(r0.EffectiveMinEdgeLength), r.EffectiveMinEdgeLength);
                Assert.Equal("", Invariance.GeometricDifference(r0.Mesh, back));
            }
            double dist = Invariance.VertexDistance(r0.Mesh, back);
            Assert.True(dist <= s.DistanceResolution(4), $"{cname}, {s.Name}: results {dist} apart");
        }

        [Fact]
        public void RelativeTolerances_AreFractionsOfTheTriangleDiagonal()
        {
            DMesh3 m = CrackedSphere(1e-8);
            // an unused vertex far away does not count
            m.AppendVertex(new Vector3d(1e6, 0, 0));
            double diag = MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            MeshAutoRepair r = Repair(m);
            Assert.Equal(1e-8 * diag, r.EffectiveRepairTolerance);
            Assert.Equal(1e-6 * diag, r.EffectiveMinEdgeLength);

            var r2 = new MeshAutoRepair(CrackedSphere(1e-8)) { RelativeRepairTolerance = 1e-9, RelativeMinEdgeLength = 0 };
            Assert.True(r2.Apply());
            Assert.Equal(1e-9 * diag, r2.EffectiveRepairTolerance);
            Assert.Equal(0, r2.EffectiveMinEdgeLength);
        }

        [Fact]
        public void RelativeTolerance_FarFromTheOrigin_AtLeastTheCoordinateResolution()
        {
            // a tiny model in UTM coordinates: 1e-8 of its diagonal is below the rounding unit of its coordinates
            DMesh3 m = CrackedSphere(1e-8);
            MeshTransforms.Scale(m, 1e-3);
            MeshTransforms.Translate(m, new Vector3d(632117.25, 5712043.5, 113.75));
            MeshAutoRepair r = Repair(m);
            Assert.Equal(16 * MathUtil.Epsilon * 5712043.5, r.EffectiveRepairTolerance, 12);
            AssertClosedSolid(r.Mesh, "utm");
        }

        [Fact]
        public void ExplicitAbsoluteTolerances_AreUsedAsGiven()
        {
            // in km the crack is 1e-5 wide: the relative default closes it, the absolute 1e-6 of 1.0.324 does not
            DMesh3 Km() { DMesh3 m = CrackedSphere(1e-8); MeshTransforms.Scale(m, 1e3); return m; }
            MeshAutoRepair rel = Repair(Km());
            Assert.Equal(1e-8 * MeshRepairMetrics.TriangleBounds(Km()).DiagonalLength, rel.EffectiveRepairTolerance);

            // set to the value of the old default: still explicit, so absolute
            var abs = new MeshAutoRepair(Km()) { RepairTolerance = MathUtil.ZeroTolerancef };
            Assert.True(abs.Apply());
            Assert.Equal(MathUtil.ZeroTolerancef, abs.EffectiveRepairTolerance);
            Assert.Equal(1e-6 * MeshRepairMetrics.TriangleBounds(Km()).DiagonalLength, abs.EffectiveMinEdgeLength);

            var mode = new MeshAutoRepair(Km()) { ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute };
            Assert.True(mode.Apply());
            Assert.Equal(MathUtil.ZeroTolerancef, mode.EffectiveRepairTolerance);
            Assert.Equal(1e-4, mode.EffectiveMinEdgeLength);

            // the crack merge welds the seam; without it the crack is filled as holes (more vertices)
            int welded = rel.Mesh.VertexCount, unwelded = abs.Mesh.VertexCount;
            Assert.Equal(Defects.Sphere(Vector3d.Zero, 1).VertexCount, welded);
            Assert.True(unwelded > welded, $"absolute 1e-6: {unwelded} vertices, relative: {welded}");
            Assert.Equal(unwelded, mode.Mesh.VertexCount);

            // MinEdgeLengthTol set explicitly: the speck (edges below 1e-4) is collapsed as in 1.0.324, the relative
            // default keeps it
            int sphereTris = Defects.Sphere(Vector3d.Zero, 1).TriangleCount;
            MeshAutoRepair keep = Repair(SphereAndSpeck());
            Assert.True(keep.Mesh.TriangleCount > sphereTris);
            var collapse = new MeshAutoRepair(SphereAndSpeck()) { MinEdgeLengthTol = 1e-4 };
            Assert.True(collapse.Apply());
            Assert.Equal(1e-4, collapse.EffectiveMinEdgeLength);
            Assert.Equal(1e-8 * MeshRepairMetrics.TriangleBounds(SphereAndSpeck()).DiagonalLength, collapse.EffectiveRepairTolerance);
            Assert.True(collapse.Mesh.TriangleCount < keep.Mesh.TriangleCount,
                $"MinEdgeLengthTol 1e-4: {collapse.Mesh.TriangleCount} triangles, relative: {keep.Mesh.TriangleCount}");
        }

        [Fact]
        public void ReusedInstance_RecomputesTheTolerances()
        {
            // one instance on a mesh, then on the same mesh in km: equals a fresh instance on the km mesh
            DMesh3 Km() { DMesh3 m = CrackedSphere(1e-8); MeshTransforms.Scale(m, 1e3); return m; }
            var r = new MeshAutoRepair(CrackedSphere(1e-8));
            Assert.True(r.Apply());
            r.Mesh = Km();
            Assert.True(r.Apply());
            MeshAutoRepair fresh = Repair(Km());
            Assert.Equal(fresh.EffectiveRepairTolerance, r.EffectiveRepairTolerance);
            Assert.Equal("", Invariance.Difference(fresh.Mesh, r.Mesh));
        }

        [Fact]
        public void Orientation_DegenerateAreaIsRelative()
        {
            // an inside-out sphere in um: 1.0.324 skipped every triangle (area below 1e-6) and left it inside out
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            m.ReverseOrientation();
            MeshTransforms.Scale(m, 1e-3);
            var orient = new MeshRepairOrientation(m);
            orient.OrientComponents();
            orient.SolveGlobalOrientation();
            Assert.True(Invariance.Volume(m) > 0);
        }
    }
}
