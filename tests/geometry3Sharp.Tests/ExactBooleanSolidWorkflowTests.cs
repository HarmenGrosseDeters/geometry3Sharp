using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1104: what a downstream solid workflow (ExactMeshBoolean, then MeshRobustRepair on an open result) sees,
    /// rebuilt procedurally per class of the report (no input of the reporter is used). Each test pins the documented
    /// behaviour (docs/OPERATIONS.md, "Validating boolean results"):
    ///   A  a difference leaves voids: inward shells with negative signed volume, closed, clean;
    ///   B  a real thin piece of the exact solid is kept (removed only where its triangles are degenerate);
    ///   C  solids that touch give Contacts / NonManifoldEdges: closed in memory, non-manifold when welded by position;
    ///   D  open input (AllowOpenMeshes, default true) gives an open result; AllowOpenMeshes = false refuses it;
    ///   E  MeshRobustRepair on an open boolean result: Apply() is true and the result is clean, but the solidify and the
    ///      wrap are not volume-checked against the open input's solid (documented), so a thin sheet becomes a slab;
    ///   F  unreferenced and unwelded input vertices change nothing.
    /// Closed results are checked per Similarity.All(); the 2^k scales give the same result bit for bit.
    /// </summary>
    public class ExactBooleanSolidWorkflowTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        // the triangles whose centroid has z above the limit removed: an open mesh
        static DMesh3 WithoutTop(DMesh3 m, double zLimit)
        {
            DMesh3 r = new DMesh3(m);
            foreach (int t in r.TriangleIndices().ToArray())
                if (r.GetTriCentroid(t).z > zLimit)
                    r.RemoveTriangle(t, true, false);
            return r;
        }

        // signed volumes of the connected components (a component with an inward orientation has a negative volume)
        static List<double> ComponentVolumes(DMesh3 m)
        {
            var cc = new MeshConnectedComponents(m);
            cc.FindConnectedT();
            var result = new List<double>();
            foreach (var comp in cc.Components) {
                var sub = new DMesh3();
                var map = new Dictionary<int, int>();
                foreach (int t in comp.Indices) {
                    Index3i tri = m.GetTriangle(t), n = new Index3i();
                    for (int k = 0; k < 3; ++k) {
                        if (!map.ContainsKey(tri[k]))
                            map[tri[k]] = sub.AppendVertex(m.GetVertex(tri[k]));
                        n[k] = map[tri[k]];
                    }
                    sub.AppendTriangle(n);
                }
                result.Add(Invariance.Volume(sub));
            }
            result.Sort();
            return result;
        }

        // edges with more than two triangles after welding the vertices by position (what a converter that welds sees)
        static int WeldedNonManifoldEdges(DMesh3 m)
        {
            var id = new Dictionary<Vector3d, int>();
            int Weld(Vector3d p) { if (!id.TryGetValue(p, out int i)) id[p] = i = id.Count; return i; }
            var count = new Dictionary<(int, int), int>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                int[] w = { Weld(m.GetVertex(tri.a)), Weld(m.GetVertex(tri.b)), Weld(m.GetVertex(tri.c)) };
                for (int k = 0; k < 3; ++k) {
                    int p = w[k], q = w[(k + 1) % 3];
                    var key = p < q ? (p, q) : (q, p);
                    count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }
            return count.Values.Count(c => c > 2);
        }

        // the Similarity.All() check: the image of the inputs gives the same structure, and for a 2^k scale (exact) the
        // same result bit for bit
        static void ForEachSimilarity(string name, Func<DMesh3[]> inputs, Ops op, Action<ExactMeshBoolean> check)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3[] ins = inputs();
            var reference = new ExactMeshBoolean(ins, op);
            Assert.True(reference.Compute(), reference.Error);
            DMesh3[] moved = ins.Select(m => Invariance.Transformed(m, s)).ToArray();
            var boolean = new ExactMeshBoolean(moved, op);
            Assert.True(boolean.Compute(), boolean.Error);
            check(boolean);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(Invariance.Transformed(reference.Result, s), boolean.Result));
        }

        static DMesh3[] VoidInputs() => new[] { Box(0, 0, 0, 10, 10, 10), Box(2, 2, 2, 4, 4, 4), Box(6, 6, 6, 6.001, 6.001, 6.001) };

        // ---- A: a difference with voids ----

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void A_DifferenceWithVoids_InnerShellsHaveNegativeVolume(string name)
        {
            ForEachSimilarity(name, VoidInputs, Ops.Difference, boolean => {
                double scale = Similarity.ByName(name).Scale;
                DMesh3 r = boolean.Result;
                ResultChecks.AssertClosed(r);
                Assert.True(boolean.RoundingClean);
                Assert.Equal(0, boolean.Contacts);
                Assert.Equal(0, boolean.NonManifoldEdges);
                // outer shell (positive), the 2-cube void (-8) and the tiny void (about zero, still negative): three components
                List<double> v = ComponentVolumes(r).Select(x => x / (scale * scale * scale)).ToList();
                Assert.Equal(3, v.Count);
                Assert.Equal(-8.0, v[0], 3);   // (3 places: the rotated, translated frame rounds its inputs)
                Assert.True(v[1] < 0 && v[1] > -1e-8, "the tiny void: " + v[1]);
                Assert.Equal(1000.0, v[2], 3);
                // the signed total is the volume of the solid: 1000 - 8 - 1e-9
                Assert.Equal(992.0, Invariance.Volume(r) / (scale * scale * scale), 3);
                // clean for the repair's checks too: a void is not an "inside-out component"
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            });
        }

        // ---- B: a thin piece of the exact solid ----

        // A = unit cube minus a plate that cuts all but a slab of the given thickness off the bottom and the top 30 %
        static DMesh3[] SlabInputs(double thickness) => new[] { Box(0, 0, 0, 1, 1, 1), Box(-1, -1, thickness, 2, 2, 0.7) };

        [Theory]
        [InlineData(1e-2)]
        [InlineData(1e-4)]
        [InlineData(1e-8)]
        public void B_ThinSlabOfTheSolid_IsKept(double thickness)
        {
            var boolean = new ExactMeshBoolean(SlabInputs(thickness)[0], SlabInputs(thickness)[1], Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            ResultChecks.AssertClosed(boolean.Result);
            Assert.True(boolean.RoundingClean);
            Assert.Equal(0, boolean.Rounding.RemovedComponents);
            List<double> v = ComponentVolumes(boolean.Result);
            Assert.Equal(2, v.Count);
            Assert.True(v[0] > 0, "thin slab positive");
            Assert.Equal(thickness, v[0], 12);
            Assert.Equal(0.3, v[1], 12);
        }

        [Fact]
        public void B_SlabBelowTheDegenerateArea_IsRemovedAndCounted()
        {
            // the side faces of a 1e-13 slab (1e-13 area) are below 1e-12 of the squared diagonal: degenerate; the closed component
            // that keeps them and is thinner than 1e-5 of the diagonal is removed by SafeRounding, counted in RemovedComponents
            var boolean = new ExactMeshBoolean(SlabInputs(1e-13)[0], SlabInputs(1e-13)[1], Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            ResultChecks.AssertClosed(boolean.Result);
            Assert.Equal(1, boolean.Rounding.RemovedComponents);
            Assert.Equal(new[] { 0.3 }.Select(x => Math.Round(x, 9)), ComponentVolumes(boolean.Result).Select(x => Math.Round(x, 9)));
        }

        // ---- C: solids that touch ----

        static DMesh3[] EdgeTouchInputs() => new[] { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1) };

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void C_BoxesTouchingAtAnEdge_ClosedInMemoryNonManifoldWhenWelded(string name)
        {
            ForEachSimilarity(name, EdgeTouchInputs, Ops.Union, boolean => {
                double scale = Similarity.ByName(name).Scale;
                DMesh3 r = boolean.Result;
                ResultChecks.AssertClosed(r);
                Assert.True(r.IsClosed());
                Assert.Equal(1, boolean.NonManifoldEdges);
                Assert.Equal(2, boolean.Contacts);              // the two ends of the edge, two vertices at each
                Assert.True(boolean.RoundingClean);
                Assert.Equal(1, WeldedNonManifoldEdges(r));     // what a tool that welds by position sees: four triangles at the edge
                List<double> v = ComponentVolumes(r).Select(x => x / (scale * scale * scale)).ToList();
                Assert.Equal(2, v.Count);
                Assert.All(v, x => Assert.Equal(1.0, x, 4));    // two positive solids
                // MeshRobustRepair.NotCleanReason treats the touching sheets as self-intersecting
                Assert.NotEqual("", MeshRobustRepair.NotCleanReason(r));
            });
        }

        [Fact]
        public void C_SeparateTouchingVertices_RemovesTheWeldedNonManifoldEdge()
        {
            var boolean = new ExactMeshBoolean(EdgeTouchInputs()[0], EdgeTouchInputs()[1], Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = new DMesh3(boolean.Result);
            Assert.Equal(1, WeldedNonManifoldEdges(r));
            Assert.True(MeshRobustRepair.SeparateTouchingVertices(r, 1e-4) > 0);
            Assert.Equal(0, WeldedNonManifoldEdges(r));
            ResultChecks.AssertClosed(r);
        }

        [Fact]
        public void C_BoxesSharingAFace_UnionHasNoContacts()
        {
            var boolean = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(1, 0, 0, 2, 1, 1), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            ResultChecks.AssertClosed(boolean.Result);
            Assert.Equal(0, boolean.Contacts);
            Assert.Equal(0, boolean.NonManifoldEdges);
            Assert.Equal(0, WeldedNonManifoldEdges(boolean.Result));
            Assert.Single(ComponentVolumes(boolean.Result));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result));
        }

        // ---- D: an open input ----

        static DMesh3[] OpenDifferenceInputs() => new[] { WithoutTop(Sphere(Vector3d.Zero, 10, 8), 7), Sphere(new Vector3d(5, 0, 0), 6, 8) };

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void D_OpenInput_GivesAnOpenResult_AndIsRefusedWhenNotAllowed(string name)
        {
            ForEachSimilarity(name, OpenDifferenceInputs, Ops.Difference, boolean => {
                DMesh3 r = boolean.Result;
                ResultChecks.AssertValid(r);
                Assert.False(boolean.ExactWindingA);            // generalized winding number
                Assert.True(boolean.ExactWindingB);
                Assert.False(r.IsClosed());
                Assert.False(ExactMeshBoolean.IsWindingClosed(r));
                Assert.True(r.BoundaryEdgeIndices().Any());
                Assert.Equal("not closed", MeshRobustRepair.NotCleanReason(r));
            });
            DMesh3[] ins = OpenDifferenceInputs();
            var strict = new ExactMeshBoolean(ins[0], ins[1], Ops.Difference) { AllowOpenMeshes = false };
            Assert.False(strict.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, strict.Status);
            Assert.Null(strict.Result);
        }

        // ---- E: MeshRobustRepair on an open boolean result ----

        static DMesh3 OpenResult(DMesh3 a, DMesh3 b)
        {
            var boolean = new ExactMeshBoolean(a, b, Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.False(boolean.Result.IsClosed());
            return boolean.Result;
        }

        [Fact]
        public void E_OpenResultOfASolid_IsSolidifiedClosedAndClose()
        {
            DMesh3 open = OpenResult(OpenDifferenceInputs()[0], OpenDifferenceInputs()[1]);
            var repair = new MeshRobustRepair(new DMesh3(open));
            Assert.True(repair.Apply());
            // the repair result of an open input is unclean (self-intersections): its volume differs from the input's solid, so
            // the fallbacks start from the input; the solidify gives the result
            Assert.True(repair.UsedInput);
            Assert.True(repair.UsedSolidify);
            Assert.False(repair.UsedAlphaWrap);
            Assert.NotEqual("", repair.FallbackReason);
            Assert.Equal("", repair.ResultReason);
            ResultChecks.AssertClean(repair.Mesh);
            // the solid of the open input (generalized winding number above 1/2) against the solidified result
            double iou = MeshRepairMetrics.VolumeIoU(open, repair.Mesh, out double missing, out double extra, 20000);
            Assert.True(iou > 0.95, "volume IoU " + iou);
        }

        [Theory]
        [InlineData(0.05, 1.0)]
        [InlineData(0.001, 100.0)]
        public void E_OpenThinSheet_IsWrappedIntoASlabFarAboveItsSolid(double thickness, double minVolumeRatio)
        {
            // an open sheet (a plate without its top face, minus a corner cut) has a thin solid; MeshRobustRepair returns true with
            // the alpha wrap, whose offset sets a thickness far above it. The wrap is not checked against the input's solid
            // (MeshRobustRepair class comment: "The solidify and the wrap themselves are not checked"); RepairVolumeIoU is
            // the IoU of the rejected repair result. The result is clean (closed, no self-intersections), not a copy of the solid.
            DMesh3 plate = WithoutTop(Box(0, 0, 0, 10, 10, thickness), thickness * 0.9);
            DMesh3 open = OpenResult(plate, Box(-1, -1, -1, 3, 3, 1));
            double solidVolume = Invariance.Volume(open);
            var repair = new MeshRobustRepair(new DMesh3(open));
            Assert.True(repair.Apply());
            Assert.True(repair.UsedAlphaWrap);
            Assert.Equal("solidify failed", repair.SolidifyReason);
            Assert.Equal("", repair.ResultReason);
            ResultChecks.AssertClean(repair.Mesh);
            Assert.True(repair.RepairVolumeIoU < 0.99);
            double ratio = Invariance.Volume(repair.Mesh) / solidVolume;
            Assert.True(ratio > minVolumeRatio, "volume ratio " + ratio);
        }

        [Fact]
        public void E_SignedVolumeOfAnOpenMesh_DependsOnTheOrigin()
        {
            // a signed volume (divergence theorem) is no measure of an open mesh: it changes with the origin, so far from it
            // (large coordinates) it is far from the solid's, while a closed mesh gives the same volume everywhere
            DMesh3 open = OpenResult(OpenDifferenceInputs()[0], OpenDifferenceInputs()[1]);
            Vector3d utm = new Vector3d(632117.25, 5712043.5, 1e6);     // (the cap's open boundary faces z)
            DMesh3 moved = new DMesh3(open);
            MeshTransforms.Translate(moved, utm);
            Func<DMesh3, double> aboutOrigin = m => m.TriangleIndices().Sum(t => {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                return a.Dot(b.Cross(c)) / 6;
            });
            Assert.True(Math.Abs(aboutOrigin(moved)) > 100 * Math.Abs(Invariance.Volume(open)), aboutOrigin(moved) + " vs " + Invariance.Volume(open));
            Assert.Equal(1.0, Invariance.Volume(moved) / Invariance.Volume(open), 6);     // (about its own center: the volume of the solid is the same everywhere)
        }

        // ---- F: unreferenced and unwelded input vertices ----

        static DMesh3 Octahedron(bool unusedVertices, bool unwelded)
        {
            var m = new DMesh3();
            Vector3d[] p = { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) };
            int[][] f = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            if (!unwelded) {
                foreach (Vector3d q in p) m.AppendVertex(q);
                foreach (int[] t in f) m.AppendTriangle(t[0], t[1], t[2]);
            } else {
                foreach (int[] t in f) m.AppendTriangle(m.AppendVertex(p[t[0]]), m.AppendVertex(p[t[1]]), m.AppendVertex(p[t[2]]));
            }
            if (unusedVertices)
                for (int k = 0; k < 6; ++k)
                    m.AppendVertex(new Vector3d(100 + k, 100, 100));
            return m;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void F_UnreferencedAndUnweldedVertices_ChangeNothing(string name)
        {
            Func<bool, bool, DMesh3[]> inputs = (unused, unwelded) => new[] { Octahedron(unused, unwelded), Box(0.2, -3, -3, 3, 3, 3) };
            Similarity s = Similarity.ByName(name);
            DMesh3 clean = null;
            foreach (var kv in new[] { (false, false), (true, false), (false, true) }) {
                DMesh3[] ins = inputs(kv.Item1, kv.Item2).Select(m => Invariance.Transformed(m, s)).ToArray();
                Assert.Equal(kv.Item1 ? 12 : kv.Item2 ? 24 : 6, ins[0].VertexCount);
                Assert.Equal(8, ins[0].TriangleCount);
                var boolean = new ExactMeshBoolean(ins, Ops.Difference);
                Assert.True(boolean.Compute(), boolean.Error);
                Assert.True(boolean.ExactWindingA);             // closed in the winding number sense, also unwelded
                ResultChecks.AssertClosed(boolean.Result);
                Assert.Equal(0, boolean.ReorientedTriangles);
                if (clean == null)
                    clean = boolean.Result;
                else
                    Assert.Equal("", kv.Item2 ? Invariance.GeometricDifference(clean, boolean.Result) : Invariance.Difference(clean, boolean.Result));
            }
        }

        [Fact]
        public void F_RepairDropsUnreferencedVertices()
        {
            DMesh3 input = Octahedron(true, false);
            var repair = new MeshRobustRepair(new DMesh3(input));
            Assert.True(repair.Apply());
            Assert.Equal(6, repair.Mesh.VertexCount);
            Assert.Equal(8, repair.Mesh.TriangleCount);
            Assert.Equal("", repair.FallbackReason);
        }
    }
}
