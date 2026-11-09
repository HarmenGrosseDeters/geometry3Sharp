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
    /// The arrangement's candidate pairs in the proper case (every vertex strictly on one side of the other triangle's
    /// plane) are decided by the Devillers-Guigue test from orientation signs alone; only pairs that meet construct the
    /// points (docs/design/TRITRI_PREFILTER.md). The test against an independent exact test on integer triangles
    /// (many touching configurations), and the Booleans in check mode, where every proper pair is decided both ways and a
    /// difference throws.
    /// </summary>
    public class TriTriPrefilterTests
    {
        static int[] Sides(Vector3d a, Vector3d b, Vector3d c, Vector3d p, Vector3d q, Vector3d r)
        {
            return new[] { ExactPredicates.Orient3d(a, b, c, p), ExactPredicates.Orient3d(a, b, c, q), ExactPredicates.Orient3d(a, b, c, r) };
        }

        static bool straddles(int[] s) => s.All(x => x != 0) && s.Any(x => x > 0) && s.Any(x => x < 0);

        [Theory]
        [InlineData(3, 1)]
        [InlineData(5, 2)]
        [InlineData(1000, 3)]
        public void ProperPairs_SameAsAnExactTest(int range, int seed)
        {
            // integer coordinates in [0, range]: small ranges give many pairs whose segments on the line touch at an
            // end (both predicates zero or one of them), larger ones generic pairs; both orders of the triangles, and
            // the triangles' vertices in every cyclic order and both orientations
            var rand = new Random(seed);
            int proper = 0, apart = 0, touching = 0;
            Func<Vector3d> v = () => new Vector3d(rand.Next(0, range + 1), rand.Next(0, range + 1), rand.Next(0, range + 1));
            for (int n = 0; n < 200000 && proper < 20000; ++n) {
                Vector3d t0 = v(), t1 = v(), t2 = v(), u0 = v(), u1 = v(), u2 = v();
                int[] sT = Sides(u0, u1, u2, t0, t1, t2), sU = Sides(t0, t1, t2, u0, u1, u2);
                if (straddles(sT) == false || straddles(sU) == false)
                    continue;
                proper++;
                var T = new Triangle3d(t0, t1, t2);
                var U = new Triangle3d(u0, u1, u2);
                bool meet = ExactTriangleIntersection.Intersects(ref T, ref U);
                bool got = ExactMeshArrangement.apart_on_the_line(t0, t1, t2, sT, u0, u1, u2, sU);
                Assert.True(got == !meet, $"T ({t0}) ({t1}) ({t2}) U ({u0}) ({u1}) ({u2}): prefilter apart {got}, exact meet {meet}");
                Assert.Equal(got, ExactMeshArrangement.apart_on_the_line(u0, u1, u2, sU, t0, t1, t2, sT));
                // cyclic order and orientation of t (the signs follow the vertices; reversing t flips sU)
                Assert.Equal(got, ExactMeshArrangement.apart_on_the_line(t1, t2, t0, new[] { sT[1], sT[2], sT[0] }, u0, u1, u2, sU));
                Assert.Equal(got, ExactMeshArrangement.apart_on_the_line(t0, t2, t1, new[] { sT[0], sT[2], sT[1] }, u0, u1, u2, sU.Select(x => -x).ToArray()));
                if (got) apart++;
                else if (ExactTriangleIntersection.Intersects(ref T, ref U) && range <= 5) touching++;
            }
            Assert.True(proper >= 1000, $"{proper} proper pairs");
            Assert.True(apart > 0 && apart < proper, $"{apart} apart of {proper}");
            if (range <= 5)
                Assert.True(touching > 0, "no touching pair");
        }

        static DMesh3 OneTriangle(Vector3d a, Vector3d b, Vector3d c)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
            return m;
        }

        static int MeetingPairs(Vector3d[] t, Vector3d[] u, bool check)
        {
            ExactMeshArrangement.CheckPrefilterOnThisThread = check;
            try {
                var arr = new ExactMeshArrangement(new List<DMesh3> { OneTriangle(t[0], t[1], t[2]), OneTriangle(u[0], u[1], u[2]) });
                arr.Compute();
                return arr.IntersectingPairs;
            } finally {
                ExactMeshArrangement.CheckPrefilterOnThisThread = false;
            }
        }

        static Vector3d[] Cyclic(Vector3d[] v, int k) => new[] { v[k % 3], v[(k + 1) % 3], v[(k + 2) % 3] };

        [Fact]
        public void OneVertexInTheOtherPlane_NotPrefiltered()
        {
            // U touches T's plane only at one vertex, which lies in T: sT = [+, -, -], sU = [+, +, 0]. Not a proper pair:
            // the construction finds the common point. In every cyclic order of both, with the roles swapped, with and
            // without the check (a prefilter that ignored one of the six non-zero conditions called it apart)
            var T = new[] { new Vector3d(0, 2, 2), new Vector3d(3, 0, 0), new Vector3d(2, 4, 0) };
            var U = new[] { new Vector3d(2, 2, 1), new Vector3d(3, 4, 1), new Vector3d(1, 3, 1) };
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    foreach (bool check in new[] { false, true }) {
                        Assert.True(MeetingPairs(Cyclic(T, i), Cyclic(U, j), check) == 1, $"T {i} U {j} check {check}");
                        Assert.True(MeetingPairs(Cyclic(U, j), Cyclic(T, i), check) == 1, $"U {j} T {i} check {check}");
                    }
        }

        [Fact]
        public void OneZeroSign_SameAsAnExactTest()
        {
            // integer triangles with exactly one of the six signs zero and both straddling otherwise: the arrangement
            // finds a meeting pair iff an exact test does
            var rand = new Random(4);
            int found = 0, meeting = 0;
            for (int n = 0; n < 400000 && found < 400; ++n) {
                Func<Vector3d> v = () => new Vector3d(rand.Next(0, 5), rand.Next(0, 5), rand.Next(0, 5));
                var t = new[] { v(), v(), v() };
                var u = new[] { v(), v(), v() };
                int[] sT = Sides(u[0], u[1], u[2], t[0], t[1], t[2]), sU = Sides(t[0], t[1], t[2], u[0], u[1], u[2]);
                if (sT.Concat(sU).Count(x => x == 0) != 1 || sT.All(x => x == 0) || sU.All(x => x == 0))
                    continue;
                // (both triangles must reach both sides, or touch the plane, else the plane separates them)
                if (sT.All(x => x >= 0) && sT.Any(x => x > 0) && sT.Count(x => x == 0) == 0) continue;
                var A = new Triangle3d(t[0], t[1], t[2]);
                var B = new Triangle3d(u[0], u[1], u[2]);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                    continue;
                found++;
                bool meet = ExactTriangleIntersection.Intersects(ref A, ref B);
                if (meet) meeting++;
                Assert.True(MeetingPairs(t, u, false) == (meet ? 1 : 0), $"T ({t[0]}) ({t[1]}) ({t[2]}) U ({u[0]}) ({u[1]}) ({u[2]}): exact meet {meet}");
            }
            Assert.True(found >= 200 && meeting > 0, $"{found} cases, {meeting} meeting");
        }

        static DMesh3 Grid(Vector3d center, Vector3d half, int n)
        {
            return new GridBox3Generator() { Box = new Box3d(center, half), EdgeVertices = n }.Generate().MakeDMesh();
        }

        static DMesh3 Turned(DMesh3 m, Vector3d axis, double degrees)
        {
            MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(axis.Normalized, degrees));
            return m;
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool check)
        {
            ExactMeshArrangement.CheckPrefilterOnThisThread = check;
            try {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
            } finally {
                ExactMeshArrangement.CheckPrefilterOnThisThread = false;
            }
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string c in new[] { "turned", "sphere", "grid", "touching" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    yield return new object[] { c, op };
        }

        static (DMesh3, DMesh3) Inputs(string name)
        {
            switch (name) {
                case "turned":
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4), 12), Turned(Grid(new Vector3d(0.6, 0.2, 0.1), new Vector3d(0.6, 0.5, 0.35), 12), new Vector3d(1, 1, 0), 20));
                case "sphere": {
                        var s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 14 }.Generate().MakeDMesh();
                        var t = new DMesh3(s);
                        MeshTransforms.Translate(t, new Vector3d(0.7, 0.3, 0.2));
                        return (s, Turned(t, new Vector3d(1, 2, 3), 17));
                    }
                case "grid":
                    // axis-aligned grids: crossing edges on the lines of the other grid (zero signs, not proper)
                    return (Grid(Vector3d.Zero, new Vector3d(1, 0.7, 0.4), 9), Grid(new Vector3d(0.5, 0.25, 0.125), new Vector3d(1, 0.7, 0.4), 9));
                default:
                    // a box turned about a vertical axis through a corner of the other: segments that touch at ends
                    return (Grid(Vector3d.Zero, new Vector3d(1, 1, 1), 6), Turned(Grid(new Vector3d(1, 1, 0), new Vector3d(0.5, 0.5, 0.5), 6), new Vector3d(0, 0, 1), 45));
            }
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Booleans_CheckMode_SameResult(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean plain = Run(a, b, op, false);
            ExactMeshBoolean checkedRun = Run(a, b, op, true);
            Assert.Equal(Print(plain.Result), Print(checkedRun.Result));
            Assert.Equal(plain.Arrangement.IntersectingPairs, checkedRun.Arrangement.IntersectingPairs);
            if (name != "grid")
                Assert.True(checkedRun.Arrangement.PrefilterRejected > 0, $"{name}: nothing rejected");
            // (the check ran: proper pairs were decided both ways)
            if (name != "grid")
                Assert.True(checkedRun.Arrangement.PrefilterChecked > 0, $"{name}: nothing checked");
            Assert.True(checkedRun.Result.IsClosed() && Invariance.AllFinite(checkedRun.Result));
            if (checkedRun.RoundingClean)
                Assert.Equal("", MeshRobustRepair.NotCleanReason(checkedRun.Result, checkSelfIntersections: false));
        }

        [Fact]
        public void Turned_Similarities_Pow2BitIdentical()
        {
            (DMesh3 a, DMesh3 b) = Inputs("turned");
            ExactMeshBoolean b0 = Run(a, b, Ops.Difference, true);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Ops.Difference, true);
                Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), sim.ToString());
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
                    Assert.Equal(b0.Arrangement.PrefilterRejected, bs.Arrangement.PrefilterRejected);
                }
            }
        }

        [Fact]
        public void SerialEqualsParallel_ComputeTwice()
        {
            (DMesh3 a, DMesh3 b) = Inputs("sphere");
            gParallel.SerialOnThisThread = true;
            string serial;
            try {
                serial = Print(Run(a, b, Ops.Union, false).Result);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 2; ++run)
                Assert.True(serial == Print(Run(a, b, Ops.Union, false).Result), $"parallel run {run}");
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(serial, Print(boolean.Result));
        }
    }
}
