using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The final lost-part check of MeshRobustRepair (F1, docs/design/ROBUST_REPAIR_SCALE_FREE_STUDY.md section 6): a small separable part
    /// next to a unit sphere must not be lost with Apply() true (the owner's rule of 2026-11-10: the loss of a part is reported). Each scene
    /// is one of the three paths traced in section 3.1, all of which returned true without the part on master 86e94029:
    /// - a clean box removed by the safe rounding's tiny test (the area relative to the largest component);
    /// - an open small sphere collapsed by MeshAutoRepair's cleanup, relative to the scene (the far-part mode is off for open inputs);
    /// - two overlapping small boxes where the far-part switch does not fire, so no lost-part check ran.
    /// The scenes are those of the study's sweep (an icosphere of subdivision 3 on a 2^-12 grid; the small part of half size 2^-r at 2^f on x).
    /// </summary>
    public class RobustRepairLostPartTests
    {
        // the icosphere of the study's generator (gen.py): subdivision n of an icosahedron, radius r, centre c
        internal static (List<Vector3d> v, List<Index3i> f) icosphere(int n, double r, Vector3d c)
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            var V = new List<Vector3d> { new Vector3d(-1, t, 0), new Vector3d(1, t, 0), new Vector3d(-1, -t, 0), new Vector3d(1, -t, 0),
                new Vector3d(0, -1, t), new Vector3d(0, 1, t), new Vector3d(0, -1, -t), new Vector3d(0, 1, -t),
                new Vector3d(t, 0, -1), new Vector3d(t, 0, 1), new Vector3d(-t, 0, -1), new Vector3d(-t, 0, 1) };
            var F = new List<Index3i> { new Index3i(0, 11, 5), new Index3i(0, 5, 1), new Index3i(0, 1, 7), new Index3i(0, 7, 10), new Index3i(0, 10, 11),
                new Index3i(1, 5, 9), new Index3i(5, 11, 4), new Index3i(11, 10, 2), new Index3i(10, 7, 6), new Index3i(7, 1, 8),
                new Index3i(3, 9, 4), new Index3i(3, 4, 2), new Index3i(3, 2, 6), new Index3i(3, 6, 8), new Index3i(3, 8, 9),
                new Index3i(4, 9, 5), new Index3i(2, 4, 11), new Index3i(6, 2, 10), new Index3i(8, 6, 7), new Index3i(9, 8, 1) };
            for (int i = 0; i < V.Count; ++i) V[i] = V[i].Normalized;
            for (int k = 0; k < n; ++k) {
                var mid = new Dictionary<(int, int), int>();
                Func<int, int, int> m = (a, b) => {
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    int id;
                    if (mid.TryGetValue(key, out id) == false) {
                        V.Add((0.5 * (V[a] + V[b])).Normalized);
                        mid[key] = id = V.Count - 1;
                    }
                    return id;
                };
                var NF = new List<Index3i>();
                foreach (Index3i f in F) {
                    int ab = m(f.a, f.b), bc = m(f.b, f.c), ca = m(f.c, f.a);
                    NF.Add(new Index3i(f.a, ab, ca)); NF.Add(new Index3i(f.b, bc, ab)); NF.Add(new Index3i(f.c, ca, bc)); NF.Add(new Index3i(ab, bc, ca));
                }
                F = NF;
            }
            return (V.Select(p => c + r * p).ToList(), F);
        }

        internal static (List<Vector3d> v, List<Index3i> f) cube(double h, Vector3d c)
        {
            var V = new List<Vector3d>();
            foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                V.Add(c + h * new Vector3d(sx, sy, sz));
            var F = new List<Index3i> { new Index3i(0, 1, 3), new Index3i(0, 3, 2), new Index3i(4, 6, 7), new Index3i(4, 7, 5), new Index3i(0, 4, 5),
                new Index3i(0, 5, 1), new Index3i(2, 3, 7), new Index3i(2, 7, 6), new Index3i(0, 2, 6), new Index3i(0, 6, 4), new Index3i(1, 5, 7), new Index3i(1, 7, 3) };
            return (V, F);
        }

        internal static void append(DMesh3 m, (List<Vector3d> v, List<Index3i> f) part, Func<Vector3d, Vector3d> map, int dropLast = 0)
        {
            int[] ids = part.v.Select(p => m.AppendVertex(map(p))).ToArray();
            for (int i = 0; i < part.f.Count - dropLast; ++i)
                m.AppendTriangle(ids[part.f[i].a], ids[part.f[i].b], ids[part.f[i].c]);
        }

        static double grid(double x) => Math.Round(x * 4096) / 4096;

        /// <summary> the scene of the study's sweep: "box" (v0), "boxes" (v1, two overlapping boxes), "open" (v2, a sphere with 6 triangles removed) </summary>
        internal static DMesh3 Scene(string kind, int r, int f, Func<Vector3d, Vector3d> map = null)
        {
            map = map ?? (p => p);
            var big = icosphere(3, 1, Vector3d.Zero);
            big.v = big.v.Select(p => new Vector3d(grid(p.x), grid(p.y), grid(p.z))).ToList();
            var m = new DMesh3();
            append(m, big, map);
            double h = Math.Pow(2, -r);
            var c = new Vector3d(Math.Pow(2, f), 0, 0);
            if (kind == "box")
                append(m, cube(h, c), map);
            else if (kind == "boxes") {
                append(m, cube(h, c), map);
                append(m, cube(h, c + new Vector3d(h, 0.5 * h, 0.25 * h)), map);
            } else
                append(m, icosphere(2, h, c), map, 6);
            return m;
        }

        // the small part (centroids beyond half the distance) has triangles in the mesh
        static bool small_part_kept(DMesh3 m, int f, Func<Vector3d, Vector3d> inverse = null)
        {
            inverse = inverse ?? (p => p);
            double cut = 0.5 * Math.Pow(2, f);
            return m.TriangleIndices().Any(t => inverse(m.GetTriCentroid(t)).x > cut);
        }

        [Theory]
        // the rounding's tiny test (box), the repair stage's cleanup for an open input (open), no far-part switch (boxes)
        [InlineData("box", 20, 2)]
        [InlineData("box", 16, 8)]
        [InlineData("open", 4, 16)]
        [InlineData("open", 20, 8)]
        [InlineData("boxes", 16, 2)]
        [InlineData("boxes", 20, 16)]
        public void SmallPartFarAway_IsKept_OrApplyIsFalseWithLostFarPart(string kind, int r, int f)
        {
            var rr = new MeshRobustRepair(Scene(kind, r, f));
            bool ok = rr.Apply();
            bool kept = small_part_kept(rr.Mesh, f);
            Assert.True(kept || (ok == false && rr.ResultReason == MeshRobustRepair.LostFarPart),
                $"part kept {kept}, Apply() {ok}, ResultReason '{rr.ResultReason}'");
            // (on these scenes the part is lost: the check is what decides)
            Assert.False(ok);
            Assert.Equal(MeshRobustRepair.LostFarPart, rr.ResultReason);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void LostPartCheck_UnderSimilarity_SameDecision(string name)
        {
            Similarity sim = Similarity.ByName(name);
            var reference = new MeshRobustRepair(Scene("box", 20, 2));
            bool refOk = reference.Apply();
            // (the part is lost on this scene: the decision compared is the check's)
            Assert.False(refOk);
            Assert.Equal(MeshRobustRepair.LostFarPart, reference.ResultReason);
            var rr = new MeshRobustRepair(Scene("box", 20, 2, sim.Apply));
            bool ok = rr.Apply();
            Assert.Equal(refOk, ok);
            Assert.Equal(reference.ResultReason, rr.ResultReason);
            if (sim.IsExact) {
                // a 2^k scale: the same mesh, scaled, bit for bit (the check runs in the working frame)
                Assert.Equal(reference.Mesh.TriangleCount, rr.Mesh.TriangleCount);
                foreach (int v in reference.Mesh.VertexIndices())
                    Assert.Equal(sim.Apply(reference.Mesh.GetVertex(v)), rr.Mesh.GetVertex(v));
            }
        }

        [Fact]
        public void Reuse_TwoApplies_SameResult()
        {
            var rr = new MeshRobustRepair(Scene("open", 4, 16));
            bool a = rr.Apply();
            string ra = rr.ResultReason;
            int ta = rr.Mesh.TriangleCount;
            Assert.False(a);
            Assert.Equal(MeshRobustRepair.LostFarPart, ra);
            rr.Mesh = Scene("open", 4, 16);
            bool b = rr.Apply();
            Assert.Equal(a, b);
            Assert.Equal(ra, rr.ResultReason);
            Assert.Equal(ta, rr.Mesh.TriangleCount);
            // and an input without a lost part after it: the check of the earlier Apply() leaves nothing behind
            rr.Mesh = Scene("box", 4, 2);
            Assert.True(rr.Apply(), rr.ResultReason);
            Assert.True(small_part_kept(rr.Mesh, 2));
        }

        [Theory]
        // parts the repair keeps: no false report (Apply() true as on master)
        [InlineData("box", 4, 2)]
        [InlineData("open", 4, 2)]
        public void SmallPartKept_ApplyStaysTrue(string kind, int r, int f)
        {
            var rr = new MeshRobustRepair(Scene(kind, r, f));
            Assert.True(rr.Apply(), rr.ResultReason);
            Assert.True(small_part_kept(rr.Mesh, f));
        }

        [Fact]
        public void StrayTriangleFarAway_IsNotAPart_ApplyStaysTrue()
        {
            // a single tiny triangle far away (fewer than MinTriangles, degenerate by the scene): the repair removes it on purpose; not
            // reported as a lost part
            DMesh3 m = Scene("box", 4, 2);
            int a = m.AppendVertex(new Vector3d(64, 0, 0)), b = m.AppendVertex(new Vector3d(64 + 1e-5, 0, 0)), c = m.AppendVertex(new Vector3d(64, 1e-5, 0));
            m.AppendTriangle(a, b, c);
            var rr = new MeshRobustRepair(m);
            Assert.True(rr.Apply(), rr.ResultReason);
            // (the triangle is gone: the check is what keeps Apply() true)
            Assert.False(small_part_kept(rr.Mesh, 6));
        }
    }
}
