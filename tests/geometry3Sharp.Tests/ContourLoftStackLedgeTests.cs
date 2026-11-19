using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> ContourLoftStack.LoweredSaddle: the roof of a contained branching or merging group (docs/design/CONTOUR_LOFT.md section 10.11). </summary>
    public class ContourLoftStackLedgeTests
    {
        static List<Vector3d> Disc(int n, double r, double z, double cx = 0, double cy = 0) => ContourLoftStackHolesTests.Disc(n, r, z, cx, cy);
        static IList<IList<IList<Vector3d>>> Slices(params IList<Vector3d>[][] s) => ContourLoftStackHolesTests.Slices(s);
        static IList<Vector3d>[] Sl(params IList<Vector3d>[] c) => c;
        static IList<IList<IList<Vector3d>>> Pad(IList<IList<IList<Vector3d>>> s) => ContourLoftStackHolesTests.Pad(s);

        static ContourLoftStackResult Loft(IList<IList<IList<Vector3d>>> s, bool ledge = true, bool caps = true, QualityMesh2 mesher = null) =>
            new ContourLoftStack { CapEnds = caps, LoweredSaddle = ledge, SaddleMesher = mesher }.Compute(s);

        // contained cases: one contour holding several of the other slice
        static Dictionary<string, IList<IList<IList<Vector3d>>>> Contained() => new Dictionary<string, IList<IList<IList<Vector3d>>>> {
            ["1 to 2"] = Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2))),
            ["1 to 3"] = Slices(Sl(ContourLoftStackHolesTests.Ellipse(48, 7, 3, 0)), Sl(Disc(24, 1.5, 1, -4), Disc(24, 1.5, 1, 0), Disc(24, 1.5, 1, 4))),
            ["dissimilar"] = Slices(Sl(Disc(48, 10, 0)), Sl(Disc(24, 2, 1, -6), Disc(24, 5, 1, 4))),
            ["2 to 1"] = Slices(Sl(Disc(24, 1.6, 0, -2.2), Disc(24, 1.6, 0, 2.2)), Sl(Disc(48, 4, 1))),
            ["1 to 2, stacked"] = Pad(Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2)))),
            ["2 to 1, stacked"] = Pad(Slices(Sl(Disc(24, 1.6, 0, -2.2), Disc(24, 1.6, 0, 2.2)), Sl(Disc(48, 4, 1)))),
            // a merge followed by a branch: two roofs share the contour of the middle slice (found by the review)
            ["2 to 1 to 2"] = Slices(Sl(Disc(24, 1.6, 0, -2.2), Disc(24, 1.6, 0, 2.2)), Sl(Disc(48, 4, 1)), Sl(Disc(24, 1.6, 2, -2.2), Disc(24, 1.6, 2, 2.2))),
        };
        // inputs where a chord of the mesher across a gap can be the same edge in the roofs of two neighbouring slabs (found by the review):
        // the stack is built again without the roofs when they make it fail, so it succeeds whenever it does without the option
        static Dictionary<string, IList<IList<IList<Vector3d>>>> Chords() => new Dictionary<string, IList<IList<IList<Vector3d>>>> {
            ["1 to 2 to 1"] = Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2)), Sl(Disc(48, 4, 2))),
            // close inner contours: the mesher would put a chord between them unless it refines the gap
            ["1 to 2 to 1, close"] = Slices(Sl(Disc(12, 10, 0)), Sl(Disc(4, 0.5, 1, -2), Disc(4, 0.5, 1, 2)), Sl(Disc(12, 10, 2))),
            ["squares"] = Slices(Sl(ContourLoftStackHolesTests.Rect(-6, 6, -3, 3, 0, 6)), Sl(ContourLoftStackHolesTests.Rect(-5, -1, -2, 2, 1, 4), ContourLoftStackHolesTests.Rect(1, 5, -2, 2, 1, 4)), Sl(ContourLoftStackHolesTests.Rect(-6, 6, -3, 3, 2, 6))),
        };
        public static IEnumerable<object[]> ChordNames() => Chords().Keys.Select(k => new object[] { k });

        public static IEnumerable<object[]> Names() => Contained().Keys.Select(k => new object[] { k });

        static double MaxDihedralAtNonContourEdges(DMesh3 m, IList<IList<IList<Vector3d>>> slices)
        {
            var contourEdges = new HashSet<long>();
            int id = 0;
            foreach (var sl in slices) foreach (var c in sl) {
                for (int i = 0; i < c.Count; ++i) { int a = id + i, b = id + (i + 1) % c.Count; contourEdges.Add(((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b)); }
                id += c.Count;
            }
            double max = 0;
            foreach (int e in m.EdgeIndices()) {
                var et = m.GetEdgeT(e); var ev = m.GetEdgeV(e);
                if (et.b < 0 || contourEdges.Contains(((long)Math.Min(ev.a, ev.b) << 32) | (uint)Math.Max(ev.a, ev.b))) continue;
                max = Math.Max(max, Math.Acos(Math.Max(-1, Math.Min(1, m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b))))) * 180 / Math.PI);
            }
            return max;
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void ARoof_IsClosedManifoldAndKeepsTheContours(string name)
        {
            var slices = Contained()[name];
            var old = Loft(slices, ledge: false);
            var r = Loft(slices);
            ContourLoftStackHolesTests.AssertClean(r, 2);
            ContourLoftStackHolesTests.AssertBoundaryIsTheInput(slices, r.Mesh);
            Assert.Equal(old.Groups.Select(g => g.Kind), r.Groups.Select(g => g.Kind));
            // the roof has vertices of its own (the mesher separates the inner contours), so the roof was built
            Assert.True(r.Mesh.VertexCount > old.Mesh.VertexCount);
            // the roof is not rougher than the bridged band
            Assert.True(MaxDihedralAtNonContourEdges(r.Mesh, slices) <= MaxDihedralAtNonContourEdges(old.Mesh, slices) + 1e-9,
                name + ": the roof is not smoother than the bridged band");
        }

        static List<Vector3d> Rev(List<Vector3d> c) { var l = new List<Vector3d>(c); l.Reverse(); return l; }

        [Theory]
        [MemberData(nameof(ChordNames))]
        public void WhenTheRoofsMakeTheStackFail_ItIsBuiltWithoutThem(string name)
        {
            var slices = Chords()[name];
            foreach (bool caps in new[] { false, true }) {
                var off = Loft(slices, ledge: false, caps: caps);
                var on = Loft(slices, caps: caps);
                Assert.Equal(ContourLoftStackStatus.Success, off.Status);
                Assert.True(on.Status == ContourLoftStackStatus.Success, name + ": " + on.Status + " " + on.Reason);
                Assert.False(MeshRepairMetrics.HasSelfIntersections(on.Mesh));
                ContourLoftStackHolesTests.AssertBoundaryIsTheInput(slices, on.Mesh);
                if (caps) Assert.Equal("", MeshRobustRepair.NotCleanReason(on.Mesh));
            }
        }

        [Fact]
        public void HoleGroups_GetARoofToo_WithTheOrientationOfAnAirPocket()
        {
            // a block whose hole splits into two and the reverse: the hole contours are the container and the inner contours (odd depth),
            // the roof faces the other way. Compared with the bridged composite: same topology, clean, the hole volume close
            foreach (bool merge in new[] { false, true }) {
                var wide = new[] { Rev(Disc(48, 4, 0)) }; var two = new[] { Rev(Disc(24, 1.6, 0, -2.2)), Rev(Disc(24, 1.6, 0, 2.2)) };
                IList<IList<IList<Vector3d>>> Lift(IEnumerable<List<Vector3d>> hs, double z) =>
                    new List<IList<IList<Vector3d>>> { new List<IList<Vector3d>> { Disc(48, 10, z) }.Concat(hs.Select(h => (IList<Vector3d>)h.Select(p => new Vector3d(p.x, p.y, z)).ToList())).ToList() };
                var s0 = Lift(merge ? two : wide, 0)[0]; var s1 = Lift(merge ? wide : two, 1)[0];
                var slices = Pad(new List<IList<IList<Vector3d>>> { s0, s1 });
                var old = Loft(slices, ledge: false); var r = Loft(slices);
                Assert.Equal(ContourLoftStackStatus.Success, old.Status);
                ContourLoftStackHolesTests.AssertClean(r, old.Mesh.VertexCount - old.Mesh.EdgeCount + old.Mesh.TriangleCount);
                ContourLoftStackHolesTests.AssertBoundaryIsTheInput(slices, r.Mesh);
                Assert.True(r.Mesh.VertexCount > old.Mesh.VertexCount);
                Assert.InRange(ContourLoftStackHolesTests.Volume(r.Mesh), 0.97 * ContourLoftStackHolesTests.Volume(old.Mesh), 1.03 * ContourLoftStackHolesTests.Volume(old.Mesh));
            }
        }

        [Fact]
        public void TheCrotchBetweenTwoBranches_IsLowered()
        {
            var slices = Contained()["1 to 2"];
            var r = Loft(slices);
            // the vertices of the roof near the middle between the two discs: below the upper plane, above the lower, well inside the slab
            var gap = r.Mesh.VertexIndices().Select(v => r.Mesh.GetVertex(v)).Where(p => Math.Abs(p.x) < 0.35 && Math.Abs(p.y) < 0.35 && p.z > 1e-9 && p.z < 1 - 1e-9).ToList();
            Assert.NotEmpty(gap);
            Assert.All(gap, p => Assert.InRange(p.z, 0.5, 0.95));
            // the merge is its mirror image: the crotch is raised from the upper plane
            var m = Loft(Contained()["2 to 1"]);
            var gap2 = m.Mesh.VertexIndices().Select(v => m.Mesh.GetVertex(v)).Where(p => Math.Abs(p.x) < 0.35 && Math.Abs(p.y) < 0.35 && p.z > 1e-9 && p.z < 1 - 1e-9).ToList();
            Assert.NotEmpty(gap2);
            Assert.All(gap2, p => Assert.InRange(p.z, 0.05, 0.5));
            // no band vertex leaves the slab
            foreach (int v in r.Mesh.VertexIndices()) Assert.InRange(r.Mesh.GetVertex(v).z, 0, 1);
        }

        [Fact]
        public void WithoutTheOption_TheResultsAreBitForBitUnchanged()
        {
            foreach (var kv in ContourLoftStackHolesTests.Existing()) {
                var plain = new ContourLoftStack { CapEnds = true }.Compute(kv.Value);
                var off = new ContourLoftStack { CapEnds = true, LoweredSaddle = false, SaddleMesher = new QualityMesh2 { MinAngle = 30 } }.Compute(kv.Value);
                Assert.Equal(ContourLoftStackHolesTests.Digest(plain.Mesh), ContourLoftStackHolesTests.Digest(off.Mesh));
                Assert.Equal(ContourLoftStackHolesTests.Golden()[kv.Key], ContourLoftStackHolesTests.Digest(off.Mesh));
            }
        }

        [Fact]
        public void GroupsThatDoNotQualify_KeepTheBridgedComposite()
        {
            // cut pieces (intermediate plane), a 1:1 band, nested tunnels, a torus by slices, a zig-zag: the option changes nothing
            foreach (var name in new[] { "branchCut", "cut1to2", "cut1to3", "torus", "washer", "zigzag", "holeIn1to1" }) {
                var slices = ContourLoftStackHolesTests.Existing()[name];
                var r = Loft(slices);
                Assert.Equal(ContourLoftStackHolesTests.Golden()[name], ContourLoftStackHolesTests.Digest(r.Mesh));
            }
        }

        [Fact]
        public void AContainedGroupWithHoles_KeepsTheBridgedComposite()
        {
            // the lower contour has a hole that continues through one leg: not a roof, the groups with holes are built as without the option
            var slices = ContourLoftStackHolesTests.HoleCases()["hole through one leg"].slices;
            var on = Loft(slices); var off = Loft(slices, ledge: false);
            Assert.Equal(ContourLoftStackStatus.Success, on.Status);
            Assert.Equal(ContourLoftStackHolesTests.Digest(off.Mesh), ContourLoftStackHolesTests.Digest(on.Mesh));
        }

        [Fact]
        public void ContoursThatTouch_KeepTheBridgedComposite()
        {
            var rect = ContourLoftStackHolesTests.Rect;
            var cases = new[] {
                // a vertex of a leg is a vertex of the lower contour
                Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, 2.4), Disc(24, 1.6, 1, -2.2))),
                // a side of a leg runs along a side of the lower contour (its vertices lie on that side)
                Slices(Sl(rect(-4, 4, -2, 2, 0, 4)), Sl(rect(-3, 0, -2, 1, 1, 3), rect(1, 3, -1, 1, 1, 3))),
                // a corner of a leg on a side of the lower contour, not at a vertex
                Slices(Sl(rect(-4, 4, -2, 2, 0, 2)), Sl(rect(-3, 0, -2, 1, 1, 2), rect(1, 3, -1, 1, 1, 2))),
            };
            foreach (var slices in cases) {
                var off = Loft(slices, ledge: false);
                Assert.Equal(ContourLoftStackStatus.Success, off.Status);
                var on = Loft(slices);
                Assert.Equal(ContourLoftStackHolesTests.Digest(off.Mesh), ContourLoftStackHolesTests.Digest(on.Mesh));
            }
        }

        [Fact]
        public void TheSaddleMesher_IsUsed()
        {
            var slices = Contained()["1 to 2"];
            int inputCount = slices.Sum(sl => sl.Sum(c => c.Count));
            // without refinement the roof has no vertex of its own (the crotch is then flat at the upper plane, but valid)
            var plain = Loft(slices, mesher: new QualityMesh2 { MinAngle = 0 });
            ContourLoftStackHolesTests.AssertClean(plain, 2);
            // CapEnds adds cap vertices of its own only with a refining cap mesher; the default caps do not
            Assert.Equal(inputCount, plain.Mesh.VertexCount);
            var fine = Loft(slices, mesher: new QualityMesh2 { MinAngle = 28 });
            ContourLoftStackHolesTests.AssertClean(fine, 2);
            Assert.True(fine.Mesh.VertexCount > Loft(slices).Mesh.VertexCount);
        }

        // ---- invariance, reuse, determinism ----

        static IEnumerable<(string, IList<IList<IList<Vector3d>>>)> InvarianceCases()
        {
            yield return ("1 to 2", ContourLoftStackHolesTests.Jitter(Contained()["1 to 2"], 31, 0.05));
            yield return ("2 to 1", ContourLoftStackHolesTests.Jitter(Contained()["2 to 1"], 32, 0.05));
            yield return ("1 to 3", ContourLoftStackHolesTests.Jitter(Contained()["1 to 3"], 33, 0.05));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarity_GivesTheSameSurface(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (var (cname, slices) in InvarianceCases()) {
                var expected = Loft(slices);
                Assert.True(expected.Status == ContourLoftStackStatus.Success, cname + ": " + expected.Status + " " + expected.Reason);
                var mapped = slices.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(sim.Apply).ToList()).ToList()).ToList();
                var got = Loft(mapped);
                Assert.True(got.Status == ContourLoftStackStatus.Success, cname + " " + sim.Name + ": " + got.Status + " " + got.Reason);
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(expected.Mesh, Invariance.MappedBack(got.Mesh, sim)));
                else {
                    // the mesher's Steiner points follow the geometry; compare what the similarity must keep
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(got.Mesh));
                    Assert.InRange(got.Mesh.VertexCount, expected.Mesh.VertexCount * 0.9, expected.Mesh.VertexCount * 1.1);
                    double ev = Invariance.Volume(expected.Mesh) * sim.Scale * sim.Scale * sim.Scale, gv = Invariance.Volume(got.Mesh);
                    Assert.True(Math.Abs(ev - gv) <= 0.02 * Math.Abs(ev), cname + " / " + sim.Name + ": volume " + ev + " vs " + gv);
                }
            }
        }

        [Fact]
        public void Reuse_OneInstanceTwice_EqualsFreshInstances()
        {
            var loft = new ContourLoftStack { CapEnds = true, LoweredSaddle = true };
            var a = Contained()["1 to 2"]; var b = Contained()["2 to 1"];
            string fa = ContourLoftStackHolesTests.Fingerprint(loft.Compute(a)), fb = ContourLoftStackHolesTests.Fingerprint(loft.Compute(b)), fa2 = ContourLoftStackHolesTests.Fingerprint(loft.Compute(a));
            Assert.Equal(ContourLoftStackHolesTests.Fingerprint(Loft(a)), fa);
            Assert.Equal(ContourLoftStackHolesTests.Fingerprint(Loft(b)), fb);
            Assert.Equal(fa, fa2);
        }

        [Fact]
        public void Deterministic_RepeatedAndConcurrentRunsAreIdentical()
        {
            foreach (var (name, slices) in InvarianceCases()) {
                string first = ContourLoftStackHolesTests.Fingerprint(Loft(slices));
                for (int i = 0; i < 3; ++i) Assert.Equal(first, ContourLoftStackHolesTests.Fingerprint(Loft(slices)));
                var results = new string[4];
                System.Threading.Tasks.Parallel.For(0, 4, i => results[i] = ContourLoftStackHolesTests.Fingerprint(Loft(slices)));
                Assert.All(results, x => Assert.Equal(first, x));
            }
        }
    }
}
