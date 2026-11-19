using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary> ContourLoftStack, holes inside branching and merging groups (docs/design/CONTOUR_LOFT.md section 10.10). </summary>
    public class ContourLoftStackHolesTests
    {
        readonly ITestOutputHelper log;
        public ContourLoftStackHolesTests(ITestOutputHelper log) { this.log = log; }

        // coordinates on a 2^-20 grid: the inputs (and so the golden digests) do not depend on the platform's cos / sin
        internal static double Q(double v) => Math.Round(v * 1048576.0) / 1048576.0;

        internal static List<Vector3d> Disc(int n, double r, double z, double cx = 0, double cy = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; c.Add(new Vector3d(Q(cx + r * Math.Cos(a)), Q(cy + r * Math.Sin(a)), z)); }
            return c;
        }

        internal static List<Vector3d> Ellipse(int n, double rx, double ry, double z, double cx = 0, double cy = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; c.Add(new Vector3d(Q(cx + rx * Math.Cos(a)), Q(cy + ry * Math.Sin(a)), z)); }
            return c;
        }

        internal static List<Vector3d> Rect(double x0, double x1, double y0, double y1, double z, int k = 3)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0 + (x1 - x0) * i / k, y0, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1, y0 + (y1 - y0) * i / k, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x1 - (x1 - x0) * i / k, y1, z));
            for (int i = 0; i < k; ++i) c.Add(new Vector3d(x0, y1 - (y1 - y0) * i / k, z));
            return c;
        }

        internal static IList<IList<IList<Vector3d>>> Slices(params IList<Vector3d>[][] s) =>
            s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c).ToList()).ToList();
        internal static IList<Vector3d>[] Sl(params IList<Vector3d>[] contours) => contours;

        // a digest of a mesh: the exact bits of all vertices and the triangles in order
        internal static string Digest(DMesh3 m)
        {
            unchecked {
                ulong h = 1469598103934665603UL;
                void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
                Mix((ulong)m.VertexCount); Mix((ulong)m.TriangleCount);
                foreach (int v in m.VertexIndices()) {
                    var p = m.GetVertex(v);
                    Mix((ulong)BitConverter.DoubleToInt64Bits(p.x)); Mix((ulong)BitConverter.DoubleToInt64Bits(p.y)); Mix((ulong)BitConverter.DoubleToInt64Bits(p.z));
                }
                foreach (int t in m.TriangleIndices()) { var tr = m.GetTriangle(t); Mix((ulong)tr.a); Mix((ulong)tr.b); Mix((ulong)tr.c); }
                return h.ToString("x16");
            }
        }

        // the inputs of the stage 2 success cases that existed before holes in branching groups
        internal static Dictionary<string, IList<IList<IList<Vector3d>>>> Existing() => new Dictionary<string, IList<IList<IList<Vector3d>>>> {
            ["merge"] = Slices(Sl(Disc(16, 1, 0, -2), Disc(16, 1, 0, 2)), Sl(Disc(16, 1, 1, -2), Disc(16, 1, 1, 2)), Sl(Rect(-3.5, 3.5, -1.2, 1.2, 2, 6))),
            ["branch3"] = Slices(Sl(Rect(-5, 5, -1.5, 1.5, 0, 8)), Sl(Disc(12, 1, 1, -3.5), Disc(12, 1, 1, 0), Disc(12, 1, 1, 3.5)), Sl(Disc(12, 1, 2, -3.5), Disc(12, 1, 2, 0), Disc(12, 1, 2, 3.5))),
            ["branchCut"] = Slices(Sl(Rect(-3, 3, -1, 1, 0, 6)), Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3))),
            ["torus"] = Slices(Sl(Rect(-4, 4, -1, 1, 0, 6)), Sl(Rect(-4, -2, -1, 1, 1, 3), Rect(2, 4, -1, 1, 1, 3)), Sl(Rect(-4, 4, -1, 1, 2, 6))),
            ["washer"] = Slices(Sl(Disc(20, 3, 0), Disc(20, 1, 0)), Sl(Disc(20, 3.2, 1), Disc(20, 1.2, 1)), Sl(Disc(20, 3, 2), Disc(20, 1, 2))),
            ["zigzag"] = Slices(Sl(Rect(-4, -0.5, -1, 1, 0, 4), Rect(0.5, 4, -1, 1, 0, 4)), Sl(Rect(-3, 1, -1.2, 1.2, 1, 4), Rect(2, 5, -1.2, 1.2, 1, 3))),
            ["cut1to2"] = Slices(Sl(Ellipse(48, 5, 2, 0)), Sl(Disc(24, 2.2, 1, -2.5), Disc(24, 2.2, 1, 2.5))),
            ["cut1to3"] = Slices(Sl(Ellipse(48, 6, 2.5, 0)), Sl(Disc(24, 1.8, 1, -4), Disc(24, 1.8, 1, 0), Disc(24, 1.8, 1, 4))),
            ["contained1to2"] = Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2))),
            ["dissimilar"] = Slices(Sl(Ellipse(48, 9, 3, 0)), Sl(Disc(24, 1, 1, -6), Disc(24, 1, 1, 3))),
            ["holeIn1to1"] = Slices(Sl(Disc(24, 4, 0), Disc(12, 1, 0)), Sl(Disc(24, 4.2, 1), Disc(12, 1.1, 1))),
        };

        // digests taken on master before holes in branching groups
        internal static Dictionary<string, string> Golden() => new Dictionary<string, string> {
                ["merge"] = "c10640d5f4d9cbb1",
                ["branch3"] = "342952bf9b7f9dba",
                ["branchCut"] = "36b87a1a77ff484d",
                ["torus"] = "afcbf5b9deb16d59",
                ["washer"] = "a25ab2571b32289d",
                ["zigzag"] = "e18667c94a72b6e2",
                ["cut1to2"] = "90f363e2946bd80b",
                ["cut1to3"] = "67587b0ce8071407",
                ["contained1to2"] = "a041e717a3027efd",
                ["dissimilar"] = "3b69340fefa85301",
                ["holeIn1to1"] = "8782888705827f5b",
            };

        [Fact]
        public void ExistingResults_AreBitForBitUnchanged()
        {
            var expected = Golden();
            foreach (var kv in Existing()) {
                var r = new ContourLoftStack { CapEnds = true }.Compute(kv.Value);
                string d = r.Status == ContourLoftStackStatus.Success ? Digest(r.Mesh) : r.Status.ToString();
                log.WriteLine("[\"" + kv.Key + "\"] = \"" + d + "\",");
                Assert.Equal(expected[kv.Key], d);
            }
        }

        // ---- inputs with holes in branching and merging groups. Three slices at least: a hole that ends needs a slice below it ----

        // the stack with a copy of its first slice one unit below and a copy of its last slice one unit above
        internal static IList<IList<IList<Vector3d>>> Pad(IList<IList<IList<Vector3d>>> s)
        {
            IList<IList<Vector3d>> Shift(IList<IList<Vector3d>> sl, double dz) =>
                sl.Select(c => (IList<Vector3d>)c.Select(p => new Vector3d(p.x, p.y, p.z + dz)).ToList()).ToList();
            var list = new List<IList<IList<Vector3d>>> { Shift(s[0], -1) };
            list.AddRange(s);
            list.Add(Shift(s[s.Count - 1], 1));
            return list;
        }

        static IList<IList<IList<Vector3d>>> Lower(IList<IList<IList<Vector3d>>> s, double dz) =>
            s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(p => new Vector3d(p.x, p.y, p.z + dz)).ToList()).ToList()).ToList();

        // name -> (stack, Euler characteristic of the closed solid)
        internal static Dictionary<string, (IList<IList<IList<Vector3d>>> slices, int euler)> HoleCases() => new Dictionary<string, (IList<IList<IList<Vector3d>>>, int)> {
            // a hole ends under the saddle of a branch (contained pieces: bridge at the upper plane)
            ["hole ends under a branch"] = (Pad(Slices(Sl(Disc(48, 6, 0), Disc(24, 1, 0)), Sl(Disc(24, 2.5, 1, -3), Disc(24, 2.5, 1, 3)))), 2),
            // a hole continues through one of the branches: a tunnel through one leg (genus 1)
            ["hole through one leg"] = (Pad(Slices(Sl(Disc(48, 6, 0), Disc(24, 0.8, 0, -3)), Sl(Disc(24, 2.5, 1, -3), Disc(24, 2.5, 1, 3), Disc(12, 0.9, 1, -3)))), 0),
            // the same with cut pieces (intermediate plane, the hole group passes it too)
            ["hole through one leg, cut"] = (Pad(Slices(Sl(Ellipse(48, 5, 2, 0), Disc(24, 0.8, 0, -2.5)), Sl(Disc(24, 2.2, 1, -2.5), Disc(24, 2.2, 1, 2.5), Disc(12, 0.9, 1, -2.5)))), 0),
            ["merge, hole through one leg, cut"] = (Pad(Slices(Sl(Disc(24, 2.2, 0, -2.5), Disc(24, 2.2, 0, 2.5), Disc(12, 0.9, 0, -2.5)), Sl(Ellipse(48, 5, 2, 1), Disc(24, 0.8, 1, -2.5)))), 0),
            // a hole that appears in a leg
            ["hole appears in a leg"] = (Pad(Slices(Sl(Disc(48, 6, 0)), Sl(Disc(24, 2.5, 1, -3), Disc(24, 2.5, 1, 3), Disc(12, 0.9, 1, -3)))), 2),
            // a hole that splits into two inside one leg: a Y-shaped tunnel with three mouths (genus 2)
            ["hole splits in a leg"] = (Pad(Slices(Sl(Disc(48, 6, 0), Disc(24, 2, 0, -3)), Sl(Disc(24, 3.2, 1, -3), Disc(24, 2.5, 1, 3), Disc(12, 0.7, 1, -3, 1), Disc(12, 0.7, 1, -3, -1)))), -2),
            // a cut branch into three with a hole through the middle one
            ["1 to 3 cut, hole through the middle"] = (Pad(Slices(Sl(Ellipse(48, 6, 2.5, 0), Disc(12, 0.6, 0)), Sl(Disc(24, 1.8, 1, -4), Disc(24, 1.8, 1, 0), Disc(24, 1.8, 1, 4), Disc(12, 0.6, 1)))), 0),
        };

        public static IEnumerable<object[]> HoleCaseNames() => HoleCases().Keys.Select(k => new object[] { k });

        internal static ContourLoftStackResult Loft(IList<IList<IList<Vector3d>>> slices, bool caps = true) => new ContourLoftStack { CapEnds = caps }.Compute(slices);

        internal static double Volume(DMesh3 mesh)
        {
            double vol = 0;
            foreach (int t in mesh.TriangleIndices()) { var tr = mesh.GetTriangle(t); vol += mesh.GetVertex(tr.a).Dot(mesh.GetVertex(tr.b).Cross(mesh.GetVertex(tr.c))) / 6; }
            return vol;
        }

        internal static int Euler(DMesh3 m) => m.VertexCount - m.EdgeCount + m.TriangleCount;

        // the contours are in the mesh as given: vertex i is input vertex i and every contour edge is a mesh edge
        internal static void AssertBoundaryIsTheInput(IList<IList<IList<Vector3d>>> slices, DMesh3 m)
        {
            int id = 0;
            foreach (var sl in slices)
                foreach (var c in sl) {
                    for (int i = 0; i < c.Count; ++i) Assert.Equal(c[i], m.GetVertex(id + i));
                    for (int i = 0; i < c.Count; ++i) Assert.True(m.FindEdge(id + i, id + (i + 1) % c.Count) >= 0);
                    id += c.Count;
                }
        }

        internal static void AssertClean(ContourLoftStackResult r, int euler)
        {
            Assert.True(r.Status == ContourLoftStackStatus.Success, r.Status + ": " + r.Reason);
            DMesh3 m = r.Mesh;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.False(MeshRepairMetrics.HasSelfIntersections(m));
            foreach (int v in m.VertexIndices()) Assert.False(m.IsBowtieVertex(v));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(m));
            foreach (int e in m.EdgeIndices()) Assert.False(m.IsBoundaryEdge(e));
            Assert.Equal(euler, Euler(m));
            Assert.True(Volume(m) > 0);
        }

        [Theory]
        [MemberData(nameof(HoleCaseNames))]
        public void HolesInBranchingGroups_AreClosedManifoldAndKeepTheContours(string name)
        {
            var (slices, euler) = HoleCases()[name];
            var r = Loft(slices);
            AssertClean(r, euler);
            AssertBoundaryIsTheInput(slices, r.Mesh);
        }

        [Fact]
        public void TheGroupsOfAHoleThroughALeg_AreABranchAndABand()
        {
            var r = Loft(HoleCases()["hole through one leg, cut"].slices);
            Assert.Contains(r.Groups, g => g.Slab == 1 && g.Kind == ContourLoftGroupKind.Branch && g.Lower.Count == 1 && g.Upper.Count == 2);
            Assert.Contains(r.Groups, g => g.Slab == 1 && g.Kind == ContourLoftGroupKind.Band && g.Lower.SequenceEqual(new[] { 1 }) && g.Upper.SequenceEqual(new[] { 2 }));
        }

        [Fact]
        public void AHoleThatEnds_RemovesExactlyItsPrism()
        {
            // the hole exists in the two lowest slices (a prism of height 1 between identical polygons); above, the stack is the same
            var withHole = HoleCases()["hole ends under a branch"].slices;
            var without = withHole.Select(sl => (IList<IList<Vector3d>>)sl.Take(1).ToList()).ToList();
            // the upper slices of the stack do not hold the hole; the lowest two do
            for (int i = 2; i < withHole.Count; ++i) without[i] = withHole[i];
            double v1 = Volume(Loft(withHole).Mesh), v0 = Volume(Loft(without).Mesh);
            var h = withHole[0][1];
            double hole = 0;
            for (int i = 0; i < h.Count; ++i) { var a = h[i]; var b = h[(i + 1) % h.Count]; hole += 0.5 * (a.x * b.y - b.x * a.y); }
            Assert.Equal(hole, v0 - v1, 6);
        }

        [Theory]
        [InlineData(-2.3, -1.0)]
        [InlineData(-2.0, -1.0)]
        [InlineData(-2.0, -0.5)]
        public void AHoleWallThroughTheRegionOfTheBridge_PassesTheIntermediatePlane(double bx, double ax)
        {
            // the hole of the lower contour reaches into the gap between the legs and the hole of the leg overlaps it only partly: a direct
            // band from one to the other runs through the fins of the bridge (SelfIntersecting). The hole group passes the intermediate
            // plane of its parent group instead, and its wall stays clear
            var slices = Pad(Slices(Sl(Rect(-3, 3, -1.2, 1.2, 0, 6), Ellipse(24, 1.2, 0.6, 0, ax, 0)), Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3), Disc(12, 0.4, 1, bx, 0))));
            var r = Loft(slices);
            AssertClean(r, 0);
            AssertBoundaryIsTheInput(slices, r.Mesh);
        }

        [Fact]
        public void ASlotAcrossBothLegs_HasNoBridgeBetweenItsPieces_AndIsReported()
        {
            // the hole of the lower contour is a slot over both legs; its two pieces are in different solid pieces with air between them
            var branch = Pad(Slices(Sl(Ellipse(48, 5, 2, 0), Ellipse(24, 4, 0.4, 0)), Sl(Disc(24, 2.2, 1, -2.5), Disc(24, 2.2, 1, 2.5), Disc(12, 0.3, 1, -2.5), Disc(12, 0.3, 1, 2.5))));
            var r = Loft(branch);
            Assert.Equal(ContourLoftStackStatus.BridgeNotFound, r.Status);
            Assert.Null(r.Mesh);
            var merge = Pad(Slices(Sl(Disc(24, 2.2, 0, -2.5), Disc(24, 2.2, 0, 2.5), Disc(12, 0.3, 0, -2.5), Disc(12, 0.3, 0, 2.5)), Sl(Ellipse(48, 5, 2, 1), Ellipse(24, 4, 0.4, 1))));
            Assert.Equal(ContourLoftStackStatus.BridgeNotFound, Loft(merge).Status);
        }

        [Fact]
        public void HoleBandAcrossABridge_NeverGivesAnUncleanMesh()
        {
            // a hole of the lower contour overlaps a hole of one leg more or less; its wall slants through the region of the bridge of the
            // cut pieces. Whatever happens (a clean mesh, or a status), no mesh that is not clean is returned
            int success = 0;
            foreach (double bx in new[] { -2.6, -2.3, -2.0 })
                foreach (double ax in new[] { -1.0, -0.5, 0.0 })
                    foreach (double ay in new[] { 0.0, 0.5 }) {
                        var slices = Pad(Slices(Sl(Rect(-3, 3, -1.2, 1.2, 0, 6), Ellipse(24, 1.2, 0.6, 0, ax, ay)), Sl(Disc(24, 1.5, 1, -3), Disc(24, 1.5, 1, 3), Disc(12, 0.4, 1, bx, 0))));
                        var r = Loft(slices);
                        if (r.Status == ContourLoftStackStatus.Success) {
                            success++;
                            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
                            Assert.False(MeshRepairMetrics.HasSelfIntersections(r.Mesh));
                            AssertBoundaryIsTheInput(slices, r.Mesh);
                        } else {
                            Assert.Null(r.Mesh);
                            Assert.Contains(r.Status, new[] { ContourLoftStackStatus.SelfIntersecting, ContourLoftStackStatus.Unsupported, ContourLoftStackStatus.BridgeNotFound, ContourLoftStackStatus.BandFailed, ContourLoftStackStatus.CrossingContours });
                        }
                    }
            Assert.True(success > 0);
        }

        [Fact]
        public void RandomHoles_NeverGiveAnUncleanMesh()
        {
            var rnd = new Random(11);
            int success = 0;
            for (int it = 0; it < 24; ++it) {
                bool cut = it % 2 == 0, merge = (it / 2) % 2 == 1;
                double ra = 0.3 + rnd.NextDouble() * 0.8, rb = 0.3 + rnd.NextDouble() * 0.8;
                double ax = (rnd.NextDouble() * 2 - 1) * 2.5, ay = (rnd.NextDouble() - 0.5) * 0.6;
                double bx = (rnd.NextDouble() < 0.5 ? -1 : 1) * 2.5 + (rnd.NextDouble() - 0.5) * 1.5, by = (rnd.NextDouble() - 0.5) * 0.6;
                var legs = cut ? new[] { Disc(24, 2.2, 1, -2.5), Disc(24, 2.2, 1, 2.5) } : new[] { Disc(24, 2.0, 1, -3), Disc(24, 2.0, 1, 3) };
                var slices = merge
                    ? Pad(Slices(Sl(legs[0], legs[1], Disc(12, rb, 1, bx, by)), Sl(cut ? Ellipse(48, 5, 2, 2) : Disc(48, 5.5, 2), Disc(12, ra, 2, ax, ay))))
                    : Pad(Slices(Sl(cut ? Ellipse(48, 5, 2, 0) : Disc(48, 5.5, 0), Disc(12, ra, 0, ax, ay)), Sl(legs[0], legs[1], Disc(12, rb, 1, bx, by))));
                var r = Loft(slices);
                if (r.Status == ContourLoftStackStatus.Success) {
                    success++;
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
                    Assert.False(MeshRepairMetrics.HasSelfIntersections(r.Mesh));
                    AssertBoundaryIsTheInput(slices, r.Mesh);
                } else
                    Assert.Null(r.Mesh);
            }
            Assert.True(success >= 18, "only " + success + " of 24 random inputs succeeded");
        }

        [Fact]
        public void IslandsInHolesOfABranchingGroup_AreReported()
        {
            // one contour with a hole that holds an island, splitting into two: not built, never a wrong mesh
            var s = Slices(Sl(Rect(-3, 3, -1.5, 1.5, 0, 6), Disc(12, 1.0, 0), Disc(8, 0.3, 0)), Sl(Disc(12, 1.2, 1, -2), Disc(12, 1.2, 1, 2)));
            var r = Loft(s);
            Assert.Equal(ContourLoftStackStatus.Unsupported, r.Status);
            Assert.Null(r.Mesh);
            Assert.Contains("islands", r.Reason);
        }

        [Fact]
        public void AnIslandInAHoleThatBranchesByItself_IsBuilt()
        {
            // a ring-shaped solid whose hole splits into two holes, one of them holding an island that continues: the hole group branches,
            // its parent group is 1:1, so the island is built (found by the review of this change)
            var slices = Pad(Slices(
                Sl(Disc(48, 8, 0), Disc(24, 3, 0), Disc(8, 0.3, 0, -1.8)),
                Sl(Disc(48, 8, 1), Disc(16, 1.3, 1, -1.8), Disc(16, 1.3, 1, 1.8), Disc(8, 0.3, 1, -1.8))));
            var r = Loft(slices);
            Assert.True(r.Status == ContourLoftStackStatus.Success, r.Status + ": " + r.Reason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.False(MeshRepairMetrics.HasSelfIntersections(r.Mesh));
            AssertBoundaryIsTheInput(slices, r.Mesh);
        }

        // ---- invariance, reuse, determinism ----

        internal static IList<IList<IList<Vector3d>>> Jitter(IList<IList<IList<Vector3d>>> s, int seed, double amount)
        {
            var rand = new Random(seed);
            return s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c.Select(p =>
                new Vector3d(p.x + amount * (rand.NextDouble() - 0.5), p.y + amount * (rand.NextDouble() - 0.5), p.z)).ToList()).ToList()).ToList();
        }

        static IEnumerable<(string name, IList<IList<IList<Vector3d>>> slices)> InvarianceCases()
        {
            yield return ("hole through one leg, cut", Jitter(HoleCases()["hole through one leg, cut"].slices, 21, 0.08));
            yield return ("merge, hole through one leg, cut", Jitter(HoleCases()["merge, hole through one leg, cut"].slices, 22, 0.08));
            yield return ("hole splits in a leg", Jitter(HoleCases()["hole splits in a leg"].slices, 23, 0.08));
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
                    // same pieces and numbering; a planar quad of a band has a diagonal that is a tie of the search, so compare what it cannot change
                    Assert.Equal(expected.Mesh.VertexCount, got.Mesh.VertexCount);
                    Assert.Equal(expected.Mesh.TriangleCount, got.Mesh.TriangleCount);
                    Assert.Equal(Euler(expected.Mesh), Euler(got.Mesh));
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(got.Mesh));
                    double tol = 100 * sim.Resolution(10) + 1e-9;
                    foreach (int v in expected.Mesh.VertexIndices())
                        Assert.True((sim.Inverse(got.Mesh.GetVertex(v)) - expected.Mesh.GetVertex(v)).Length <= tol, cname + " / " + sim.Name + ": vertex " + v + " moved");
                    double ev = Invariance.Volume(expected.Mesh) * sim.Scale * sim.Scale * sim.Scale, gv = Invariance.Volume(got.Mesh);
                    Assert.True(Math.Abs(ev - gv) <= 1e-6 * Math.Abs(ev), cname + " / " + sim.Name + ": volume " + ev + " vs " + gv);
                }
                Assert.Equal(expected.Groups.Select(g => g.Kind), got.Groups.Select(g => g.Kind));
            }
        }

        internal static string Fingerprint(ContourLoftStackResult r) =>
            r.Status + "|" + string.Join(";", r.Groups.Select(g => g.Slab + ":" + g.Kind + ":" + string.Join(",", g.Lower) + ">" + string.Join(",", g.Upper))) + "|" +
            (r.Mesh == null ? "" : string.Join(";", r.Mesh.VertexIndices().Select(v => r.Mesh.GetVertex(v).ToString())) + "|" +
             string.Join(";", r.Mesh.TriangleIndices().Select(t => r.Mesh.GetTriangle(t).ToString())));

        [Fact]
        public void Reuse_OneInstanceTwice_EqualsFreshInstances()
        {
            var loft = new ContourLoftStack { CapEnds = true };
            var a = HoleCases()["hole through one leg, cut"].slices; var b = HoleCases()["hole splits in a leg"].slices;
            string fa = Fingerprint(loft.Compute(a)), fb = Fingerprint(loft.Compute(b)), fa2 = Fingerprint(loft.Compute(a));
            Assert.Equal(Fingerprint(new ContourLoftStack { CapEnds = true }.Compute(a)), fa);
            Assert.Equal(Fingerprint(new ContourLoftStack { CapEnds = true }.Compute(b)), fb);
            Assert.Equal(fa, fa2);
        }

        [Fact]
        public void Deterministic_RepeatedAndConcurrentRunsAreIdentical()
        {
            foreach (var (name, slices) in InvarianceCases()) {
                string first = Fingerprint(Loft(slices));
                for (int i = 0; i < 3; ++i) Assert.Equal(first, Fingerprint(Loft(slices)));
                var results = new string[4];
                System.Threading.Tasks.Parallel.For(0, 4, i => results[i] = Fingerprint(Loft(slices)));
                Assert.All(results, x => Assert.Equal(first, x));
            }
        }
    }
}
