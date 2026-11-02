using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for mesh repair (MeshRobustRepair, MeshAutoRepair, the hole fillers) from the issue trackers of other
    /// libraries (docs/QA.md 2.11): only the failure mode, rebuilt here from simple primitives. No code, test code or
    /// meshes of these projects are used (CGAL is GPL and MeshLib source-available: only the prose of their issues was
    /// read). The issue URL is in the comment of each test.
    ///
    /// Every case checks the result itself (MeshRobustRepair.NotCleanReasons, the volume, case-specific checks). A case
    /// we fail is listed in KnownFailures with its finding (strict, as in RobustnessSweepTests: a listed case that passes
    /// fails, so a fix removes the entry).
    /// </summary>
    public class MeshRepairIssueCasesTests
    {
        /// <summary> case name -> finding; a listed case must still fail </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
            // MeshPostRepairCleanup.FindInvertedComponents counts a closed component as enclosed by another when most of
            // its interior samples lie inside it, also when the two surfaces cross (it assumes interiors that are
            // disjoint or nested): of two outward boxes crossing at 3 or 10 degrees, one is flipped as a cavity, and
            // the repair returns A minus B (0.84 of 7.24) as clean. The volume check accepts it (IoU 1): the input's
            // reference solid uses the same nesting rule
            [nameof(ThinFold_NearlyParallelOverlap_Resolved)] = "finding R1 (docs/ISSUE_CASE_FINDINGS.md): crossing components taken for nested ones",
        };

        static void Expect(string name, string failure)
        {
            if (KnownFailures.TryGetValue(name, out string finding)) {
                if (!IssueCases.Strict) return;
                Assert.True(failure != "", $"{name} passes now: remove its entry ({finding}) from KnownFailures and docs/QA.md 2.11");
            }
            else
                Assert.True(failure == "", name + ": " + failure);
        }

        // ---- primitives (doubles throughout, so the expected volumes are exact) ----

        /// <summary> an axis-aligned box, 12 triangles, outward </summary>
        internal static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        internal static DMesh3 Sphere(Vector3d c, double r, int n = 8)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        /// <summary> a closed cone: a fan of n triangles to the apex over a regular n-gon in z = 0, and a fan base </summary>
        static DMesh3 Cone(int n, double r, double h, bool withBase)
        {
            var m = new DMesh3();
            int apex = m.AppendVertex(new Vector3d(0, 0, h));
            int[] ring = Enumerable.Range(0, n).Select(i => m.AppendVertex(new Vector3d(r * Math.Cos(2 * Math.PI * i / n), r * Math.Sin(2 * Math.PI * i / n), 0))).ToArray();
            for (int i = 0; i < n; ++i)
                m.AppendTriangle(ring[i], ring[(i + 1) % n], apex);
            if (withBase)
                for (int i = 1; i + 1 < n; ++i)
                    m.AppendTriangle(ring[0], ring[i + 1], ring[i]);
            return m;
        }

        static DMesh3 Copy(DMesh3 m) => new DMesh3(m);

        static AxisAlignedBox3d Bounds(DMesh3 m)
        {
            var b = AxisAlignedBox3d.Empty;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                b.Contain(m.GetVertex(tv.a)); b.Contain(m.GetVertex(tv.b)); b.Contain(m.GetVertex(tv.c));
            }
            return b;
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        /// <summary>
        /// MeshRobustRepair on a copy: "" if the result is clean (closed, manifold, oriented, no degenerate triangles, no
        /// self-intersections), its volume is within relTol of the expected one, and it stays in the input's bounds
        /// grown by grow (relative to their diagonal); otherwise the reasons
        /// </summary>
        static string RobustOutcome(DMesh3 input, double volume, double relTol, out MeshRobustRepair repair, double grow = 0.01)
        {
            repair = new MeshRobustRepair(Copy(input));
            bool ok = repair.Apply();
            string o = Outcome(ok, repair.Mesh, input, volume, relTol, grow);
            return o == "" ? "" : o + " [" + Path(repair) + "]";
        }

        /// <summary>
        /// MeshAutoRepair (with the post-repair cleanup, as in MeshRobustRepair) on a copy, checked like RobustOutcome.
        /// For holes: MeshRobustRepair compares a repair with the winding number solid of the open input, and a large
        /// hole's fill differs from that by more than its MinVolumeIoU, so it would remesh the input instead
        /// </summary>
        static string AutoOutcome(DMesh3 input, double volume, double relTol, out DMesh3 result,
            MeshAutoRepair.HoleFillModes mode = MeshAutoRepair.HoleFillModes.Minimal, DMesh3 boundsOf = null, double grow = 0.01)
        {
            var repair = new MeshAutoRepair(Copy(input)) { PostRepairCleanup = true, HoleFillMode = mode };
            bool ok = repair.Apply();
            result = repair.Mesh;
            return Outcome(ok, result, boundsOf ?? input, volume, relTol, grow);
        }

        static string Path(MeshRobustRepair r) =>
            $"exact {r.UsedExactResolve}, solidify {r.UsedSolidify}, wrap {r.UsedAlphaWrap}, input {r.UsedInput}, result '{r.ResultReason}', "
            + $"fallback '{r.FallbackReason}', exact '{r.ExactResolveReason}', IoU {r.RepairVolumeIoU}";

        static string Outcome(bool ok, DMesh3 result, DMesh3 input, double volume, double relTol, double grow)
        {
            var r = new List<string>();
            if (!ok) r.Add("Apply() false");
            if (result == null) return "no result";
            if (!result.CheckValidity(false, FailMode.ReturnOnly)) r.Add("invalid DMesh3");
            if (!result.Vertices().All(v => v.IsFinite)) r.Add("non-finite coordinates");
            string reasons = MeshRobustRepair.NotCleanReasons(result);
            if (reasons != "") r.Add(reasons);
            double vol = ResultChecks.Volume(result);
            if (!(Math.Abs(vol - volume) <= relTol * Math.Abs(volume))) r.Add($"volume {vol:R}, expected {volume:R}");
            AxisAlignedBox3d bi = Bounds(input), bo = Bounds(result);
            double g = grow * bi.DiagonalLength;
            if (!(bo.Min.x >= bi.Min.x - g && bo.Min.y >= bi.Min.y - g && bo.Min.z >= bi.Min.z - g
                && bo.Max.x <= bi.Max.x + g && bo.Max.y <= bi.Max.y + g && bo.Max.z <= bi.Max.z + g))
                r.Add($"result bounds {bo} beyond the input's {bi}");
            return string.Join("; ", r);
        }

        static double SphereVolume(DMesh3 sphere) => ResultChecks.Volume(sphere);

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(MeshRepairIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                // a case name is a test method, or a test method and a suffix for one of its parts
                Assert.True(names.Any(n => kv.Key == n || kv.Key.StartsWith(n + "_")), kv.Key + ": no such case");
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // holes

        [Fact]
        public void HoleOfOneTriangle_FilledWithoutAMirroredShell()
        {
            // https://github.com/CGAL/cgal/issues/937: a hole that is exactly one triangle was filled with that triangle
            // and a mirrored copy, a closed two-triangle shell of zero volume. Correct: the hole is closed by one triangle.
            DMesh3 sphere = Sphere(Vector3d.Zero, 1), input = Copy(sphere);
            int tid = input.TriangleIndices().First();
            input.RemoveTriangle(tid, false);
            Expect(nameof(HoleOfOneTriangle_FilledWithoutAMirroredShell), RobustOutcome(input, SphereVolume(sphere), 1e-9, out var rr));
            Assert.Equal(sphere.TriangleCount, rr.Mesh.TriangleCount);
            Assert.Equal(1, Components(rr.Mesh));
        }

        [Fact]
        public void PlanarSquareHole_FilledExactly()
        {
            // https://github.com/CGAL/cgal/issues/937: a planar square hole (a box missing one face): with one choice of
            // diagonal nothing was filled, yet success was reported. Rebuilt for each of the six faces (their
            // triangulations have both diagonal directions). Correct: two triangles, the box's exact volume.
            for (int face = 0; face < 6; ++face) {
                DMesh3 input = Box(new Vector3d(-1, -2, -3), new Vector3d(4, 5, 6));
                input.RemoveTriangle(2 * face, false);
                input.RemoveTriangle(2 * face + 1, false);
                string name = nameof(PlanarSquareHole_FilledExactly);
                Expect(name, RobustOutcome(input, 5 * 7 * 9, 1e-12, out var rr));
                Assert.Equal(12, rr.Mesh.TriangleCount);
                Assert.Equal(8, rr.Mesh.VertexCount);
            }
        }

        [Fact]
        public void QuadHoleWhoseDiagonalIsAnExistingEdge()
        {
            // https://github.com/pmp-library/pmp-library/issues/181: the triangulation of a hole created an edge that
            // already existed in the mesh, so adding the face failed and the filler left a broken state. Rebuilt: a
            // tetrahedron without the two faces on edge bd. The hole a-b-c-d has the diagonal ac as an existing interior
            // edge, so only the diagonal bd closes it.
            var a = new Vector3d(0, 0, 0); var b = new Vector3d(3, 0, 0); var c = new Vector3d(1, 2, 0); var d = new Vector3d(1, 1, 2);
            var tet = new DMesh3();
            foreach (Vector3d v in new[] { a, b, c, d }) tet.AppendVertex(v);
            int abc = tet.AppendTriangle(0, 2, 1), abd = tet.AppendTriangle(0, 1, 3), bcd = tet.AppendTriangle(1, 2, 3), acd = tet.AppendTriangle(0, 3, 2);
            Assert.True(abc >= 0 && abd >= 0 && bcd >= 0 && acd >= 0);
            double volume = ResultChecks.Volume(tet);
            Assert.True(volume > 0);
            DMesh3 input = Copy(tet);
            input.RemoveTriangle(abd, false);
            input.RemoveTriangle(bcd, false);
            Expect(nameof(QuadHoleWhoseDiagonalIsAnExistingEdge), AutoOutcome(input, volume, 1e-12, out DMesh3 repaired));
            Assert.Equal(4, repaired.TriangleCount);
            Assert.Equal(4, repaired.VertexCount);

            // the minimal hole filler alone, on the same hole
            DMesh3 m = Copy(input);
            var loops = new MeshBoundaryLoops(m);
            Assert.Single(loops.Loops);
            var fill = new MinimalHoleFill(m, loops.Loops[0]);
            bool filled = fill.Apply();
            string why = filled ? "" : "MinimalHoleFill.Apply() false";
            if (filled) {
                if (!m.CheckValidity(false, FailMode.ReturnOnly)) why = "invalid after the fill";
                else if (!m.IsClosed()) why = "not closed after the fill";
                else if (Math.Abs(ResultChecks.Volume(m) - volume) > 1e-12 * volume) why = $"volume {ResultChecks.Volume(m)}";
            }
            Expect(nameof(QuadHoleWhoseDiagonalIsAnExistingEdge) + "_MinimalHoleFill", why);
        }

        // the tetrahedron of QuadHoleWhoseDiagonalIsAnExistingEdge without its faces abd and bcd, transformed
        static DMesh3 TetraWithQuadHole(Similarity s)
        {
            var m = new DMesh3();
            foreach (Vector3d v in new[] { new Vector3d(0, 0, 0), new Vector3d(3, 0, 0), new Vector3d(1, 2, 0), new Vector3d(1, 1, 2) })
                m.AppendVertex(s.Apply(v));
            m.AppendTriangle(0, 2, 1);
            m.AppendTriangle(0, 3, 2);
            return m;
        }

        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void MinimalHoleFill_ExistingDiagonal_UnderSimilarity(string similarity)
        {
            // the fix of QuadHoleWhoseDiagonalIsAnExistingEdge under Similarity.All(): the same two triangles (the new
            // diagonal bd), no new vertex; and a sphere's cap hole filled the same way, bit for bit under 2^k scales
            Similarity s = Similarity.ByName(similarity);
            DMesh3 m = TetraWithQuadHole(s);
            var fill = new MinimalHoleFill(m, new MeshBoundaryLoops(m).Loops[0]);
            Assert.True(fill.Apply());
            ResultChecks.AssertClosed(m, similarity);
            Assert.Equal(4, m.VertexCount);
            Assert.Equal(4, m.TriangleCount);
            Assert.Empty(fill.FillVertices);
            Assert.True(m.FindEdge(1, 3) >= 0, similarity + ": diagonal bd missing");
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(m));

            DMesh3 reference = null;
            foreach (Similarity t in new[] { Similarity.Identity, s }) {
                DMesh3 sphere = Invariance.Transformed(Sphere(Vector3d.Zero, 1, 8), t);
                foreach (int tid in sphere.TriangleIndices().ToArray())
                    if (t.Inverse(sphere.GetTriCentroid(tid)).Normalized.z > 0.8)
                        sphere.RemoveTriangle(tid, true);
                var f = new MinimalHoleFill(sphere, new MeshBoundaryLoops(sphere).Loops[0]);
                Assert.True(f.Apply());
                ResultChecks.AssertClosed(sphere, similarity);
                DMesh3 back = Invariance.MappedBack(sphere, t);
                if (reference == null) { reference = back; continue; }
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, back));
                else {
                    Assert.Equal(reference.TriangleCount, back.TriangleCount);
                    Assert.True(Invariance.VertexDistance(reference, back) <= 1e-6, similarity + ": " + Invariance.VertexDistance(reference, back));
                }
            }
        }

        [Fact]
        public void SeveralHoles_AllFilled()
        {
            // https://github.com/pmp-library/pmp-library/issues/98: of a mesh with two holes, only the first boundary
            // was filled. Rebuilt with more: a sphere with four separate holes of one to a few triangles.
            DMesh3 sphere = Sphere(Vector3d.Zero, 2, 10), input = Copy(sphere);
            var rand = new Random(98);
            var removed = new HashSet<int>();
            foreach (Vector3d dir in new[] { Vector3d.AxisX, -Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ }) {
                // the triangles of a small cap around dir
                foreach (int t in sphere.TriangleIndices())
                    if (sphere.GetTriCentroid(t).Normalized.Dot(dir) > 0.985)
                        removed.Add(t);
            }
            foreach (int t in removed) input.RemoveTriangle(t, false);
            Assert.Equal(4, new MeshBoundaryLoops(input).Count);
            Expect(nameof(SeveralHoles_AllFilled), RobustOutcome(input, SphereVolume(sphere), 1e-3, out var rr));
            Assert.Equal(1, Components(rr.Mesh));
        }

        [Fact]
        public void ConeWithoutItsBase_FilledFlat()
        {
            // https://github.com/pmp-library/pmp-library/issues/64: filling the open base of a cone (a large planar
            // loop) gave a non-smooth patch out of the plane. Correct: a flat patch, the cone's exact volume.
            foreach (int n in new[] { 7, 32, 101 }) {
                DMesh3 cone = Cone(n, 10, 3, true), input = Cone(n, 10, 3, false);
                Expect(nameof(ConeWithoutItsBase_FilledFlat), RobustOutcome(input, ResultChecks.Volume(cone), 1e-9, out var rr));
                // every vertex of the fill lies in the base plane, or is the apex
                foreach (Vector3d v in rr.Mesh.Vertices())
                    Assert.True(Math.Abs(v.z) <= 1e-12 || v.z == 3, $"n = {n}: vertex {v} off the base plane");
            }
        }

        [Fact]
        public void HoleNextToANearZeroAreaTriangle_PatchStaysLocal()
        {
            // https://github.com/pmp-library/pmp-library/issues/225: a near-zero-area triangle on the rim of a hole
            // made the fairing of the fill move its new vertices far away from the hole. Rebuilt: a sphere with a cap
            // removed, and one rim edge split very close to its end (a triangle of almost zero area on the rim).
            DMesh3 sphere = Sphere(Vector3d.Zero, 1, 12), input = Copy(sphere);
            foreach (int t in sphere.TriangleIndices().ToArray())
                if (sphere.GetTriCentroid(t).Normalized.z > 0.9)
                    input.RemoveTriangle(t, false);
            var loops = new MeshBoundaryLoops(input);
            Assert.Single(loops.Loops);
            int e = loops.Loops[0].Edges[0];
            Index2i ev = input.GetEdgeV(e);
            Assert.Equal(MeshResult.Ok, input.SplitEdge(e, out DMesh3.EdgeSplitInfo split, 1e-7));
            Assert.True(input.IsBoundaryVertex(split.vNew));
            foreach (var mode in new[] { MeshAutoRepair.HoleFillModes.Minimal, MeshAutoRepair.HoleFillModes.Smoothed }) {
                Expect(nameof(HoleNextToANearZeroAreaTriangle_PatchStaysLocal), AutoOutcome(input, SphereVolume(sphere), 0.02, out DMesh3 repaired, mode, sphere));
                // no vertex of the fill far from the hole: the minimal fill within the sphere, the smoothed one (which
                // bulges like a cap) within 10 % of its radius
                foreach (Vector3d v in repaired.Vertices())
                    Assert.True(v.Length <= (mode == MeshAutoRepair.HoleFillModes.Minimal ? 1 + 1e-6 : 1.1), $"{mode}: vertex {v} outside the sphere");
            }
        }

        [Fact]
        public void HoleWithASelfTouchingBoundary()
        {
            // https://github.com/CGAL/cgal/issues/1238: a hole boundary that touches itself (it runs through one vertex
            // twice), which a filler for simple polygons fails on. Rebuilt: a sphere without two triangles that share
            // only a vertex, so the two holes meet at that vertex (a boundary bowtie).
            DMesh3 sphere = Sphere(Vector3d.Zero, 1, 8), input = Copy(sphere);
            int v = sphere.VertexIndices().First(vid => sphere.GetVtxEdgeCount(vid) == 6);
            int[] ring = sphere.VtxTrianglesItr(v).ToArray();
            // two triangles of the one-ring that share no edge
            int t0 = ring[0], t1 = ring.First(t => t != t0 && sphere.GetTriNeighbourTris(t0).a != t && sphere.GetTriNeighbourTris(t0).b != t && sphere.GetTriNeighbourTris(t0).c != t);
            input.RemoveTriangle(t0, false);
            input.RemoveTriangle(t1, false);
            Expect(nameof(HoleWithASelfTouchingBoundary), RobustOutcome(input, SphereVolume(sphere), 1e-9, out var rr));
            Assert.Equal(sphere.TriangleCount, rr.Mesh.TriangleCount);
        }

        [Fact]
        public void SmallAndLargeModel_SmoothedFill_SameRefinement()
        {
            // https://github.com/pmp-library/pmp-library/issues/67: the refinement of a small hole fill on a small mesh
            // added millions of vertices. Rebuilt: the smoothed hole fill of MeshAutoRepair on a sphere with a cap
            // removed, 1e-3 and 1e3 in size, and 2^-10, 2^10 (compared with size 1 bit for bit). Correct: the fill's size
            // is bounded by the hole, not by an absolute constant, so all sizes give the same triangles.
            int count = -1;
            DMesh3 unit = null;
            foreach (double size in new[] { 1.0, 1e-3, 1e3, 1.0 / 1024, 1024 }) {
                DMesh3 sphere = Sphere(Vector3d.Zero, 1, 8);
                MeshTransforms.Scale(sphere, size);
                DMesh3 input = Copy(sphere);
                foreach (int t in sphere.TriangleIndices().ToArray())
                    if (sphere.GetTriCentroid(t).Normalized.z > 0.8)
                        input.RemoveTriangle(t, false);
                var sw = Stopwatch.StartNew();
                Expect(nameof(SmallAndLargeModel_SmoothedFill_SameRefinement), AutoOutcome(input, SphereVolume(sphere), 0.03, out DMesh3 repaired, MeshAutoRepair.HoleFillModes.Smoothed, sphere));
                Assert.True(repaired.TriangleCount < 2 * sphere.TriangleCount, $"size {size}: {repaired.TriangleCount} triangles");
                Assert.True(sw.Elapsed.TotalSeconds < 60, $"size {size}: {sw.Elapsed.TotalSeconds} s");
                if (count < 0) count = repaired.TriangleCount;
                Assert.Equal(count, repaired.TriangleCount);
                if (size == 1) unit = repaired;
                else if (size == 1.0 / 1024 || size == 1024) {
                    DMesh3 back = Copy(repaired);
                    MeshTransforms.Scale(back, 1 / size);
                    Assert.Equal("", Invariance.Difference(unit, back));
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // seams, gaps, pinches

        [Fact]
        public void UnweldedSeam_ZeroAreaHoleIsStitchedNotFilled()
        {
            // https://github.com/CGAL/cgal/issues/3902: after a soup repair, "holes" made of two matching boundary
            // halfedges of opposite direction (a loop p-q-p-q of zero area) were passed to the hole filler, which failed.
            // Correct: such seams are stitched. Rebuilt: a sphere cut open along a meridian (the seam vertices
            // duplicated). (A full triangle soup is the defect case "triangle soup" of Harness/Defects.cs.)
            DMesh3 sphere = Sphere(Vector3d.Zero, 1, 9), cut = new DMesh3();
            // duplicate the vertices of the triangles on the x > 0 side of the plane y = 0 that touch it
            var map = new Dictionary<int, int>();
            foreach (int vid in sphere.VertexIndices()) map[vid] = cut.AppendVertex(sphere.GetVertex(vid));
            var dup = new Dictionary<int, int>();
            foreach (int t in sphere.TriangleIndices()) {
                Index3i tv = sphere.GetTriangle(t);
                bool side = sphere.GetTriCentroid(t).y > 0 && sphere.GetTriCentroid(t).x > 0;
                int M(int vid)
                {
                    Vector3d p = sphere.GetVertex(vid);
                    if (!side || Math.Abs(p.y) > 1e-12 || p.x <= 0) return map[vid];
                    if (!dup.TryGetValue(vid, out int nv)) dup[vid] = nv = cut.AppendVertex(p);
                    return nv;
                }
                cut.AppendTriangle(M(tv.a), M(tv.b), M(tv.c));
            }
            Assert.False(cut.IsClosed());
            Expect(nameof(UnweldedSeam_ZeroAreaHoleIsStitchedNotFilled), RobustOutcome(cut, SphereVolume(sphere), 1e-12, out var rr));
            Assert.Equal(sphere.TriangleCount, rr.Mesh.TriangleCount);
        }

        [Fact]
        public void TwoHalvesWithANarrowGap_ClosedSolid()
        {
            // https://github.com/CGAL/cgal/issues/7747: two boundary loops close to each other that bound one gap (like a
            // cut ring), which hole filling of single loops cannot close. Rebuilt: a box cut into two open halves at
            // z = 0, the upper one lifted by 1e-7 of the size. Correct: one closed solid (either bridging the gap or
            // closing each half), with the volume of the parts within the gap's share.
            DMesh3 lower = Box(new Vector3d(0, 0, -1), new Vector3d(2, 2, 0)), upper = Box(new Vector3d(0, 0, 1e-7), new Vector3d(2, 2, 1 + 1e-7));
            // remove the faces at the cut: the top of the lower box and the bottom of the upper one
            foreach (int t in lower.TriangleIndices().ToArray()) if (lower.GetTriNormal(t).z > 0.5) lower.RemoveTriangle(t, false);
            foreach (int t in upper.TriangleIndices().ToArray()) if (upper.GetTriNormal(t).z < -0.5) upper.RemoveTriangle(t, false);
            var input = new DMesh3();
            new MeshEditor(input).AppendMesh(lower);
            new MeshEditor(input).AppendMesh(upper);
            Expect(nameof(TwoHalvesWithANarrowGap_ClosedSolid), RobustOutcome(input, 8, 1e-6, out var rr));
        }

        [Fact]
        public void TwoOpenBoxesTouchingAtAHoleCorner()
        {
            // https://github.com/libigl/libigl/issues/873 and https://github.com/libigl/libigl/issues/1070: two patches
            // that meet only at one vertex of their boundaries: the boundary walk jumped between them and returned a loop
            // over a missing edge. Rebuilt: two boxes, each missing its face at the shared corner, touching at that
            // corner (one vertex). Correct: both holes filled, two closed boxes, the shared corner not merged into a
            // bowtie.
            DMesh3 a = Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), b = Box(new Vector3d(1, 1, 1), new Vector3d(2, 3, 4));
            foreach (int t in a.TriangleIndices().ToArray()) if (a.GetTriNormal(t).z > 0.5) a.RemoveTriangle(t, false);
            foreach (int t in b.TriangleIndices().ToArray()) if (b.GetTriNormal(t).z < -0.5) b.RemoveTriangle(t, false);
            // weld into one mesh with the corner (1,1,1) shared: a soup by position, as a reader would weld it
            var input = new DMesh3();
            var at = new Dictionary<Vector3d, int>();
            foreach (DMesh3 m in new[] { a, b })
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    int V(int vid) { Vector3d p = m.GetVertex(vid); if (!at.TryGetValue(p, out int id)) at[p] = id = input.AppendVertex(p); return id; }
                    Assert.True(input.AppendTriangle(V(tv.a), V(tv.b), V(tv.c)) >= 0);
                }
            // (the exact resolve separates the touching corner by MeshRobustRepair.SeparateFraction of the size, which
            // moves the volume by about that much)
            Expect(nameof(TwoOpenBoxesTouchingAtAHoleCorner), RobustOutcome(input, 1 + 6, 1e-4, out var rr));
            Assert.Equal(2, Components(rr.Mesh));
        }

        [Fact]
        public void RingOfBoxes_LastNearlyCoincidesWithTheFirst()
        {
            // https://github.com/elalish/manifold/issues/1309: a ring of 9 boxes, each rotated about an axis by 45
            // degrees more than the last, so the 9th matches the first only up to the rounding of the rotation; the
            // near-duplicate overlapping shells made the cleanup hang or crash. Correct: a clean result of the union
            // (the 8 boxes; the 9th adds only rounding).
            var input = new DMesh3();
            double single = 0;
            for (int i = 0; i < 9; ++i) {
                DMesh3 box = Box(new Vector3d(3, -0.5, -0.5), new Vector3d(4, 0.5, 0.5));
                single = ResultChecks.Volume(box);
                var q = new Quaterniond(Vector3d.AxisZ, 45.0 * i);
                MeshTransforms.Rotate(box, Vector3d.Zero, q);
                new MeshEditor(input).AppendMesh(box);
            }
            var sw = Stopwatch.StartNew();
            Expect(nameof(RingOfBoxes_LastNearlyCoincidesWithTheFirst), RobustOutcome(input, 8 * single, 1e-6, out var rr));
            Assert.Equal(8, Components(rr.Mesh));
            Assert.True(sw.Elapsed.TotalSeconds < 120);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // orientation

        [Fact]
        public void SphereWithOneFlippedTriangle_Reoriented()
        {
            // https://github.com/isl-org/Open3D/issues/1386: a sphere with one flipped triangle passed every manifold
            // check. Correct: reported as an orientation defect, and fixed by propagation (same triangles, same volume).
            // (The defect case "partly flipped" flips a whole cap; this is the smallest case, one triangle.)
            DMesh3 sphere = Sphere(Vector3d.Zero, 1), input = Copy(sphere);
            int tid = input.TriangleIndices().Skip(17).First();
            Index3i tv = input.GetTriangle(tid);
            // flipped: its own vertices (DMesh3 cannot hold an inconsistently oriented edge pair)
            input.RemoveTriangle(tid, false);
            input.AppendTriangle(input.AppendVertex(input.GetVertex(tv.a)), input.AppendVertex(input.GetVertex(tv.c)), input.AppendVertex(input.GetVertex(tv.b)));
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(input));
            Expect(nameof(SphereWithOneFlippedTriangle_Reoriented), RobustOutcome(input, SphereVolume(sphere), 1e-9, out var rr));
            Assert.Equal(sphere.TriangleCount, rr.Mesh.TriangleCount);
        }

        [Theory]
        [InlineData(3.0)]
        [InlineData(10.0)]
        public void ThinFold_NearlyParallelOverlap_Resolved(double degrees)
        {
            // https://github.com/MeshInspector/MeshLib/issues/5567: thin folds, where intersecting faces point almost
            // the same way (within about 5 degrees), were missed by the self-intersection detection. Rebuilt: a flat box
            // and a copy rotated by 3 (and 10) degrees about an axis through its middle, so their large faces cross at a
            // small angle. Correct: the union, clean.
            DMesh3 a = Box(new Vector3d(-2, -2, -0.2), new Vector3d(2, 2, 0.2)), b = Copy(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(Vector3d.AxisY, degrees));
            var input = new DMesh3();
            new MeshEditor(input).AppendMesh(a);
            new MeshEditor(input).AppendMesh(b);
            // the volume of the union, from the exact boolean of the two closed boxes
            var union = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(union.Compute(), union.Error);
            Expect(nameof(ThinFold_NearlyParallelOverlap_Resolved), RobustOutcome(input, ResultChecks.Volume(union.Result), 1e-6, out var rr));
            // while listed: the failure of finding R1 (a clean result of the wrong solid, the difference of the boxes), not
            // some other one
            if (IssueCases.Strict && KnownFailures.ContainsKey(nameof(ThinFold_NearlyParallelOverlap_Resolved))) {
                ResultChecks.AssertClean(rr.Mesh, "the known failure's result");
                Assert.True(ResultChecks.Volume(rr.Mesh) < 0.5 * ResultChecks.Volume(union.Result), $"volume {ResultChecks.Volume(rr.Mesh)}");
            }
        }

        [Fact]
        public void ManySmallOverlaps_NoComponentsLost()
        {
            // https://github.com/MeshInspector/MeshLib/issues/5923: a mesh with dense self-intersections fell apart into
            // 12 components after the repair. Rebuilt: a row of 10 boxes, each overlapping the next. Correct: one solid
            // of the union's volume.
            var input = new DMesh3();
            for (int i = 0; i < 10; ++i)
                new MeshEditor(input).AppendMesh(Box(new Vector3d(i, 0.1 * (i % 2), 0), new Vector3d(i + 1.5, 1 + 0.1 * (i % 2), 1)));
            // union volume: the boxes overlap in x by 0.5 each and alternate in y by 0.1:
            // area in the xy plane of the union = sum of the boxes - overlaps (0.5 x 0.9 each)
            double volume = 10 * 1.5 - 9 * 0.5 * 0.9;
            Expect(nameof(ManySmallOverlaps_NoComponentsLost), RobustOutcome(input, volume, 1e-9, out var rr));
            Assert.Equal(1, Components(rr.Mesh));
        }
    }
}
