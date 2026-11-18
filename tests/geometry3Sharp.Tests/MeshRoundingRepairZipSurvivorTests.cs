using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests written from the survivors of the first full run of the mutation shard rounding-13 (MeshRoundingRepair.cs
    /// from try_zip: the zip of two caps and the guards of the strip search; scheduled run 44217581261 on master). Each
    /// test pins one guard or one branch of try_zip that no test of the shard reached or checked: the neighbours' ids,
    /// the positions of the apexes along the edge, edges that exist already, and the orientation reference of the
    /// added triangles. A rejected zip is checked by the ids of the triangles (an undone edit gives the restored
    /// triangles other ids) and by the counts.
    /// </summary>
    public class MeshRoundingRepairZipSurvivorTests
    {
        // Two caps on both sides of (a, b): T = (a, b, c) and U = (b, a, d), their neighbours N1 and N2 along the edge,
        // x and y their third corners. Apex order a, c, d, b (cFirst): N1 = (c, b, x), N2 = (d, a, y); else a, d, c, b:
        // N1 = (a, c, x), N2 = (b, d, y). The triangles are appended in the order `order` (indexes 0..3: T, U, N1,
        // N2), so a test can give any of them the id 0.
        sealed class Caps
        {
            public DMesh3 Mesh = new DMesh3();
            public int A, B, C, D, X, Y;
            public int[] Tri = new int[4];      // the triangle ids of T, U, N1, N2
            public Index3i[] Corners = new Index3i[4];

            public Caps(Vector3d pa, Vector3d pb, Vector3d pc, Vector3d pd, Vector3d px, Vector3d py, int[] order = null, bool sameApex = false)
            {
                A = Mesh.AppendVertex(pa); B = Mesh.AppendVertex(pb);
                C = Mesh.AppendVertex(pc); D = Mesh.AppendVertex(pd);
                X = Mesh.AppendVertex(px);
                Y = sameApex ? X : Mesh.AppendVertex(py);
                Vector3d ab = pb - pa;
                bool cFirst = (pc - pa).Dot(ab) < (pd - pa).Dot(ab);
                var tris = new[] {
                    new Index3i(A, B, C), new Index3i(B, A, D),
                    cFirst ? new Index3i(C, B, X) : new Index3i(A, C, X),
                    cFirst ? new Index3i(D, A, Y) : new Index3i(B, D, Y) };
                foreach (int k in order ?? new[] { 0, 1, 2, 3 }) {
                    Tri[k] = Mesh.AppendTriangle(tris[k]);
                    Corners[k] = tris[k];
                }
            }

            public static Caps Flat(Vector3d offset, double cx, double dx, int[] order = null)
            {
                return new Caps(offset, offset + new Vector3d(4, 0, 0), offset + new Vector3d(cx, 0, 0), offset + new Vector3d(dx, 0, 0),
                    offset + new Vector3d(2, 1, 0), offset + new Vector3d(2, -1, 0), order);
            }

            // the triangles keep their ids and corners (no zip, and no undone zip, which would give them other ids)
            public void AssertUntouchedTriangles()
            {
                for (int k = 0; k < 4; ++k) {
                    Assert.True(Mesh.IsTriangle(Tri[k]), "triangle " + k + " removed");
                    Assert.Equal(Corners[k], Mesh.GetTriangle(Tri[k]));
                }
            }
        }

        static MeshRoundingRepair Run(DMesh3 m)
        {
            var r = new MeshRoundingRepair(m);
            r.Apply();
            return r;
        }

        static void AssertZipped(Caps k, MeshRoundingRepair r)
        {
            Assert.Equal(1, r.Zips);
            Assert.Equal(0, r.StripZips);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.Equal(4, k.Mesh.TriangleCount);
            Assert.True(k.Mesh.FindEdge(k.A, k.B) < 0);
            Assert.True(k.Mesh.FindEdge(k.C, k.D) >= 0);
            Assert.True(k.Mesh.CheckValidity(true, FailMode.ReturnOnly));
        }

        // a not at the origin: the position of b and the apexes relative to a (ab = pb - pa, pc - pa, pd - pa)
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Zip_EdgeAwayFromTheOrigin_ApexParametersRelativeToA(bool cFirst)
        {
            Caps k = Caps.Flat(new Vector3d(-10, 7, -3), cFirst ? 1 : 3, cFirst ? 3 : 1);
            AssertZipped(k, Run(k.Mesh));
        }

        // an apex outside the edge (before a, beyond b), the other inside or also outside: no zip (the neighbours would
        // be split at a point that is not on their edge); the repair handles the caps another way
        [Theory]
        [InlineData(5.0, 3.0)]      // c beyond b
        [InlineData(-1.0, 3.0)]     // c before a
        [InlineData(1.0, 5.0)]      // d beyond b
        [InlineData(1.0, -1.0)]     // d before a
        [InlineData(5.0, -1.0)]
        [InlineData(-1.0, -2.0)]
        public void Zip_ApexOutsideTheEdge_Rejected(double cx, double dx)
        {
            Caps k = Caps.Flat(new Vector3d(1.5, -2, 3), cx, dx);
            var r = Run(k.Mesh);
            Assert.Equal(0, r.Zips);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.True(k.Mesh.CheckValidity(true, FailMode.ReturnOnly));
        }

        // the neighbour (or the cap) with the id 0: a valid id (the guards test < 0)
        [Theory]
        [InlineData(true, 2)]
        [InlineData(true, 3)]
        [InlineData(false, 2)]
        [InlineData(false, 3)]
        public void Zip_NeighbourWithIdZero_Zipped(bool cFirst, int first)
        {
            var order = new[] { first }.Concat(new[] { 0, 1, 2, 3 }.Where(i => i != first)).ToArray();
            Caps k = Caps.Flat(Vector3d.Zero, cFirst ? 1 : 3, cFirst ? 3 : 1, order);
            Assert.Equal(0, k.Tri[first]);
            AssertZipped(k, Run(k.Mesh));
        }

        // a cap whose edge has no neighbour on one side: no zip (nothing to split there), the other triangles stay
        [Theory]
        [InlineData(true, 2)]
        [InlineData(true, 3)]
        [InlineData(false, 2)]
        [InlineData(false, 3)]
        public void Zip_NeighbourMissing_Rejected(bool cFirst, int missing)
        {
            Caps k = Caps.Flat(Vector3d.Zero, cFirst ? 1 : 3, cFirst ? 3 : 1);
            k.Mesh.RemoveTriangle(k.Tri[missing]);
            var r = Run(k.Mesh);
            Assert.Equal(0, r.Zips);
            Assert.Equal(0, r.Flips + r.Collapses + r.StripZips);
            Assert.Equal(3, k.Mesh.TriangleCount);
            for (int i = 0; i < 4; ++i)
                if (i != missing)
                    Assert.Equal(k.Corners[i], k.Mesh.GetTriangle(k.Tri[i]));
        }

        // N1 and N2 have a common third corner: the zip would give two triangles on the same three vertices
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Zip_NeighboursShareTheThirdCorner_Rejected(bool cFirst)
        {
            var k = new Caps(Vector3d.Zero, new Vector3d(4, 0, 0), new Vector3d(cFirst ? 1 : 3, 0, 0), new Vector3d(cFirst ? 3 : 1, 0, 0),
                new Vector3d(2, 1, 0), Vector3d.Zero, null, true);
            var r = Run(k.Mesh);
            Assert.Equal(0, r.Zips);
            Assert.Equal(0, r.StripZips);
            Assert.True(k.Mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(4, k.Mesh.TriangleCount);
        }

        // (c, d) is an edge already (of a triangle on the far side): neither a flip nor a zip, the caps keep their ids
        // (a zip that is undone appends the triangles again, with other ids). The edge has the id 0 in one case.
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void Zip_ApexesAlreadyJoined_Rejected(bool cFirst, bool edgeIdZero)
        {
            var m = new DMesh3();
            Vector3d pc = new Vector3d(cFirst ? 1 : 3, 0, 0), pd = new Vector3d(cFirst ? 3 : 1, 0, 0);
            int c = 0, d = 0, z = 0;
            if (edgeIdZero) {
                // the first triangle (d, c, z): the edge (c, d) is the edge 0 (the same orientation as the zip would give)
                c = m.AppendVertex(pc); d = m.AppendVertex(pd); z = m.AppendVertex(new Vector3d(2, 0, 5));
                m.AppendTriangle(d, c, z);
            }
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(4, 0, 0));
            if (!edgeIdZero) {
                c = m.AppendVertex(pc); d = m.AppendVertex(pd); z = m.AppendVertex(new Vector3d(2, 0, 5));
            }
            int x = m.AppendVertex(new Vector3d(2, 1, 0)), y = m.AppendVertex(new Vector3d(2, -1, 0));
            var ids = new[] { m.AppendTriangle(a, b, c), m.AppendTriangle(b, a, d),
                cFirst ? m.AppendTriangle(c, b, x) : m.AppendTriangle(a, c, x), cFirst ? m.AppendTriangle(d, a, y) : m.AppendTriangle(b, d, y) };
            if (!edgeIdZero)
                m.AppendTriangle(d, c, z);
            Assert.Equal(edgeIdZero, m.FindEdge(c, d) == 0);
            var before = ids.Select(t => m.GetTriangle(t)).ToArray();
            var r = Run(m);
            Assert.Equal(0, r.Zips);
            Assert.Equal(0, r.StripZips);
            for (int i = 0; i < 4; ++i)
                Assert.Equal(before[i], m.GetTriangle(ids[i]));
        }

        // (d, x) or (c, y) exists already, on a triangle apart from the ones the zip replaces: the new triangles would
        // put a third triangle on it
        [Theory]
        [InlineData(true, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, true)]
        public void Zip_NewEdgeExistsAlready_Rejected(bool cFirst, bool atDx, bool edgeIdZero)
        {
            var m = new DMesh3();
            Vector3d pc = new Vector3d(cFirst ? 1 : 3, 0, 0), pd = new Vector3d(cFirst ? 3 : 1, 0, 0);
            Vector3d px = new Vector3d(2, 1, 0), py = new Vector3d(2, -1, 0);
            int a, b, c, d, x, y, w;
            int[] ids;
            if (edgeIdZero) {
                // the first triangle holds the edge, so that it has the id 0
                a = m.AppendVertex(Vector3d.Zero); b = m.AppendVertex(new Vector3d(4, 0, 0));
                c = m.AppendVertex(pc); d = m.AppendVertex(pd);
                x = m.AppendVertex(px); y = m.AppendVertex(py);
                w = m.AppendVertex(new Vector3d(2, 0, 6));
                if (atDx) m.AppendTriangle(d, x, w); else m.AppendTriangle(c, y, w);
            } else {
                a = m.AppendVertex(Vector3d.Zero); b = m.AppendVertex(new Vector3d(4, 0, 0));
                c = m.AppendVertex(pc); d = m.AppendVertex(pd);
                x = m.AppendVertex(px); y = m.AppendVertex(py);
                w = m.AppendVertex(new Vector3d(2, 0, 6));
            }
            ids = new[] { m.AppendTriangle(a, b, c), m.AppendTriangle(b, a, d),
                cFirst ? m.AppendTriangle(c, b, x) : m.AppendTriangle(a, c, x), cFirst ? m.AppendTriangle(d, a, y) : m.AppendTriangle(b, d, y) };
            if (!edgeIdZero) {
                if (atDx) m.AppendTriangle(d, x, w); else m.AppendTriangle(c, y, w);
            }
            Assert.Equal(edgeIdZero, m.FindEdge(atDx ? d : c, atDx ? x : y) == 0);
            var before = ids.Select(t => m.GetTriangle(t)).ToArray();
            var r = Run(m);
            Assert.Equal(0, r.Zips);
            for (int i = 0; i < 4; ++i)
                Assert.Equal(before[i], m.GetTriangle(ids[i]));
        }

        // N1 and N2 on one side of the edge, as a fold (N2 raised): each added triangle keeps the orientation of the
        // neighbour it splits, N1 for the first two and N2 for the others; with the references taken from the wrong
        // neighbour the zip is refused (the triangles would turn over)
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Zip_NeighboursFolded_AddedTrianglesKeepTheirNeighboursOrientation(bool cFirst)
        {
            var k = new Caps(Vector3d.Zero, new Vector3d(4, 0, 0), new Vector3d(cFirst ? 1 : 3, 0, 0), new Vector3d(cFirst ? 3 : 1, 0, 0),
                new Vector3d(2, 1, 0), new Vector3d(2, 1, 0.5));
            var r = Run(k.Mesh);
            AssertZipped(k, r);
            // the halves of N1 (flat, normal +z) and of N2 (raised, normal -z and +y): a half taken from the wrong
            // neighbour would have the other sign
            foreach (int t in k.Mesh.TriangleIndices()) {
                Vector3d n = k.Mesh.GetTriNormal(t);
                bool hasY = k.Mesh.GetTriangle(t).a == k.Y || k.Mesh.GetTriangle(t).b == k.Y || k.Mesh.GetTriangle(t).c == k.Y;
                Assert.True(hasY ? n.z < -0.5 : n.z > 0.999, "triangle " + t + " turned over: " + n);
            }
        }

        // the same on scalings by powers of two (the counts and the topology are the same; the bit-for-bit invariance
        // over Similarity.All() of the repair as a whole is in MeshRoundingRepairIncrementalTests)
        [Fact]
        public void Zip_ScaledByPowersOfTwo_SameEdit()
        {
            foreach (double s in new[] { 1.0, 0.25, 64.0, 1.0 / 1024 }) {
                var k = new Caps(new Vector3d(1.5, -2, 3) * s, new Vector3d(5.5, -2, 3) * s, new Vector3d(2.5, -2, 3) * s, new Vector3d(4.5, -2, 3) * s,
                    new Vector3d(3.5, -1, 3) * s, new Vector3d(3.5, -3, 3) * s);
                AssertZipped(k, Run(k.Mesh));
            }
        }

        // the strips of slivers along lines that a sphere poked four times in a row leaves (as SafeRoundingBoundTests.Strips): the
        // edits and the result are pinned, so that the order and the number of the proposals of try_strip (the line of
        // the longest edge first, then every direction; the largest part of a strip first) cannot change unnoticed.
        // The counts depend on the boolean output of the generator input: a change there needs them re-pinned.
        [Fact]
        public void Strips_EditsAndResultPinned()
        {
            var raw = new ExactMeshBoolean(SafeRoundingDegenerateInputTests.Make(SafeRoundingDegenerateInputTests.Family.Cluster, 30, 2, 4),
                new DMesh3(), ExactMeshBoolean.Operations.Union) { SafeRounding = false };
            Assert.True(raw.Compute(), raw.Error);
            DMesh3 m = raw.Result;
            Assert.Equal(436, m.TriangleCount);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
            Assert.True(r.Apply());
            Assert.Equal(69, r.InitialDegenerate);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.Equal(0, r.Collapses);
            Assert.Equal(33, r.Flips);
            Assert.Equal(0, r.Moves);
            Assert.Equal(12, r.Zips);
            Assert.Equal(12, r.StripZips);
            Assert.Equal(410, m.TriangleCount);
            Assert.Equal(207, m.VertexCount);
            ResultChecks.AssertClean(m);
        }

        // a lock on an edge of one of the two caps only (the edge (c, a) of T, the edge (d, b) of U, which have no
        // neighbour in the first topology): no zip, though the other cap is free; a lock on a neighbour's outer edge
        // (b, x), an edge of neither cap, leaves the zip alone
        [Theory]
        [InlineData("T")]
        [InlineData("U")]
        [InlineData("both")]
        public void Zip_OneCapLocked_Rejected(string which)
        {
            Caps k = Caps.Flat(Vector3d.Zero, 1, 3);
            var locks = new HashSet<long>();
            if (which != "U") locks.Add(MeshRoundingRepair.LockedKey(k.C, k.A));
            if (which != "T") locks.Add(MeshRoundingRepair.LockedKey(k.D, k.B));
            var r = new MeshRoundingRepair(k.Mesh) { LockedEdges = locks };
            r.Apply();
            // (the caps are repaired by other edits: flips and a move)
            Assert.Equal(0, r.Zips + r.StripZips + r.Collapses);
            Assert.Equal(0, r.RemainingDegenerate);
            foreach (long key in locks)
                Assert.True(k.Mesh.FindEdge((int)(key >> 32), (int)(key & 0xFFFFFFFF)) >= 0, "a lock outlived its edge");
        }

        [Fact]
        public void Zip_LockElsewhere_StillZipped()
        {
            Caps k = Caps.Flat(Vector3d.Zero, 1, 3);
            var locks = new HashSet<long> { MeshRoundingRepair.LockedKey(k.B, k.X) };
            var r = new MeshRoundingRepair(k.Mesh) { LockedEdges = locks };
            r.Apply();
            AssertZipped(k, r);
            Assert.True(k.Mesh.FindEdge(k.B, k.X) >= 0);
        }
    }
}
