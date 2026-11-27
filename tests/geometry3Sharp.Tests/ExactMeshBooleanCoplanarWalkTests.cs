using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1630: the walk of the coplanar stacks (ExactMeshBoolean.CoplanarWalk) against the scan of every piece: the same pieces, the same stacks in the same
    /// order, the same decisions and the same result, bit for bit, on overlays of coplanar fans (a capped cylinder and a shifted copy) and of coplanar boxes,
    /// and the count of the exact tests per piece that it bounds (docs/design/COPLANAR_FAN_DIAGNOSIS.md).
    /// </summary>
    public class ExactMeshBooleanCoplanarWalkTests
    {
        /// <summary>
        /// a capped cylinder (radius r, height h) whose ring points are on the rational parametrisation of the circle, (x, z) = r (1 - t^2, 2 t) / (1 + t^2),
        /// t = k / K on the right half, mirrored on the left: 4 K - 2 ring points (no sine, the same on every platform), the caps are fans from the cap centres
        /// </summary>
        internal static DMesh3 Cylinder(int K, double r = 0.5, double h = 1.0)
        {
            var points = new List<Vector2d>();
            for (int k = -K; k <= K; ++k) { double t = (double)k / K; points.Add(new Vector2d(r * (1 - t * t) / (1 + t * t), r * 2 * t / (1 + t * t))); }
            for (int k = -K + 1; k < K; ++k) { double t = (double)k / K; points.Add(new Vector2d(-r * (1 - t * t) / (1 + t * t), -r * 2 * t / (1 + t * t))); }
            points.Sort((a, b) => Math.Atan2(a.y, a.x).CompareTo(Math.Atan2(b.y, b.x)));
            var ring = new List<Vector2d>();
            foreach (Vector2d p in points)
                if (ring.Count == 0 || ring[ring.Count - 1] != p)
                    ring.Add(p);
            if (ring[0] == ring[ring.Count - 1])
                ring.RemoveAt(ring.Count - 1);
            int n = ring.Count;
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(ring[i].x, 0, ring[i].y));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(ring[i].x, h, ring[i].y));
            int c0 = m.AppendVertex(new Vector3d(0, 0, 0)), c1 = m.AppendVertex(new Vector3d(0, h, 0));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(c0, i, j); m.AppendTriangle(c1, n + j, n + i);
                m.AppendTriangle(i, n + j, j); m.AppendTriangle(i, n + i, n + j);
            }
            return m;
        }

        /// <summary> the mesh shifted along x by dx (the 2-ulp shift of #1630 if dx is 0: 2 ulps of the x extent, every shifted coordinate exact) </summary>
        internal static DMesh3 Shifted(DMesh3 a, double dx)
        {
            if (dx == 0) {
                double maxAbs = a.VertexIndices().Max(v => Math.Abs(a.GetVertex(v).x));
                dx = 2 * (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(maxAbs) + 1) - maxAbs);       // (the next double up; maxAbs is positive and finite)
            }
            var b = new DMesh3(a);
            foreach (int vid in b.VertexIndices().ToList()) { Vector3d v = b.GetVertex(vid); v.x += dx; b.SetVertex(vid, v); }
            return b;
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        /// <summary> a plate and boxes whose tops lie in the plane of its top and overlap each other: coplanar triangles in the grid pattern of the box faces </summary>
        static List<DMesh3> PlateAndBoxes()
        {
            var meshes = new List<DMesh3>() { Box(0, 0, 0, 4, 4, 1) };
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    meshes.Add(Box(0.7 * i + 0.25, 0.6 * j + 0.3, 0.5, 0.7 * i + 1.6, 0.6 * j + 1.4, 1));
            return meshes;
        }

        /// <summary> everything a Compute decides, as text: the pieces, the stacks, keep and flip, and the result's vertices (bits) and triangles </summary>
        internal static string Snapshot(ExactMeshBoolean b)
        {
            var sb = new StringBuilder();
            foreach (var ot in b.Arrangement.Output)
                sb.Append(ot.Mesh).Append(',').Append(ot.Source).Append(',').Append(ot.A).Append(',').Append(ot.B).Append(',').Append(ot.C).Append(';');
            sb.Append('|');
            foreach (List<int> st in b.StacksPerPiece)
                sb.Append(st == null ? "-" : string.Join(",", st)).Append(';');
            sb.Append('|');
            for (int i = 0; i < b.CertificateKeep.Length; ++i)
                sb.Append(b.CertificateKeep[i] ? 'k' : '_').Append(b.CertificateFlip[i] ? 'f' : '_');
            sb.Append('|');
            if (b.Result != null) {
                foreach (int v in b.Result.VertexIndices()) {
                    Vector3d p = b.Result.GetVertex(v);
                    sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
                }
                foreach (int t in b.Result.TriangleIndices()) {
                    Index3i f = b.Result.GetTriangle(t);
                    sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
                }
            }
            return sb.ToString();
        }

        enum Mode { Scan, Forced, Default }

        static ExactMeshBoolean Run(IEnumerable<DMesh3> meshes, ExactMeshBoolean.Operations op, Mode mode, bool serial = true)
        {
            var b = new ExactMeshBoolean(meshes, op) { RecordStacks = true, RecordCertificate = true };
            if (mode == Mode.Scan) b.CoplanarWalk = false;
            if (mode == Mode.Forced) { b.CoplanarWalkMinCoplanar = 0; b.CoplanarWalkMinPieces = 0; }
            gParallel.SerialOnThisThread = serial;
            try { Assert.True(b.Compute(), b.Error); }
            finally { gParallel.SerialOnThisThread = false; }
            return b;
        }

        static void AssertWalkEqualsScan(IList<DMesh3> meshes, ExactMeshBoolean.Operations op, string what)
        {
            string scan = Snapshot(Run(meshes, op, Mode.Scan));
            Assert.True(scan == Snapshot(Run(meshes, op, Mode.Forced)), what + ": the forced walk differs from the scan");
            Assert.True(scan == Snapshot(Run(meshes, op, Mode.Default)), what + ": the walk at its threshold differs from the scan");
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (ExactMeshBoolean.Operations op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
                yield return new object[] { "cylinder 2 ulps", 8, 0.0, op };
                yield return new object[] { "cylinder 0.25", 8, 0.25, op };
                yield return new object[] { "cylinder 1e-9", 6, 1e-9, op };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TheWalk_GivesTheStacksAndTheResultOfTheScan_OnFans(string what, int K, double dx, ExactMeshBoolean.Operations op)
        {
            DMesh3 a = Cylinder(K);
            AssertWalkEqualsScan(new[] { a, Shifted(a, dx) }, op, what + " " + op);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void TheWalk_GivesTheStacksAndTheResultOfTheScan_OnCoplanarBoxes(ExactMeshBoolean.Operations op)
        {
            AssertWalkEqualsScan(PlateAndBoxes(), op, "plate and boxes " + op);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void TheWalk_GivesTheScansStacks_ForIdenticalMeshes(ExactMeshBoolean.Operations op)
        {
            // every triangle of one mesh has an identical triangle in the other (three shared corners: meets_only_at_shared decides the pair, nothing is recorded)
            DMesh3 a = Cylinder(6);
            AssertWalkEqualsScan(new[] { a, new DMesh3(a) }, op, "identical cylinders " + op);
            DMesh3 plate = Box(0, 0, 0, 4, 4, 1);
            AssertWalkEqualsScan(new[] { plate, new DMesh3(plate) }, op, "identical boxes " + op);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void TheWalk_GivesTheScansStacks_WhereABoxCrossesThePlateTop(ExactMeshBoolean.Operations op)
        {
            // the plate's top has coplanar boxes on it (their tops and edges give coplanar edge planes) and a box that crosses it: its side faces meet the top in segments
            // that lie on the edge lines of the coplanar triangles
            var meshes = PlateAndBoxes();
            meshes.Add(Box(0.7, 0.9, 0.4, 1.9, 2.2, 1.5));
            meshes.Add(Box(2.0, 0.6, 0.8, 3.2, 1.7, 1.3));
            AssertWalkEqualsScan(meshes, op, "plate, boxes and crossing boxes " + op);
            // a crossing box alone with the plate: the crossing segments on the plate top without coplanar neighbours
            AssertWalkEqualsScan(new[] { Box(0, 0, 0, 4, 4, 1), Box(1, 1, 0.5, 2.5, 2.5, 1.5) }, op, "plate and one crossing box " + op);
        }

        [Fact]
        public void TheWalk_ChangesNothing_ForTheStacksOfTheFansAtTheRingsOfTheIssue()
        {
            // the cap centre of a fan has the longest stacks: the first run of the rings of the growth test, the walk against the scan on every piece
            foreach (int K in new[] { 5, 9 }) {
                DMesh3 a = Cylinder(K);
                AssertWalkEqualsScan(new[] { a, Shifted(a, 0) }, ExactMeshBoolean.Operations.Union, "ring of " + K);
            }
        }

        // the exact containment tests per piece (Counts["coplanar_tests"] / Counts["pieces"]) of the cylinder and its 2-ulp shift at rings of 30, 62 and 118 points: the scan's
        // grow with the ring (about a third of it: 10.8, 20, 40 at these rings), the walk's are 7.7, 3.2 and 2.0: the seed scans (one per source, the length of its list: they
        // add up to a constant times n^2 and the pieces are 4 times as many per n^2) and a few per boundary edge
        public static IEnumerable<object[]> Rings() { yield return new object[] { 8 }; yield return new object[] { 16 }; yield return new object[] { 30 }; }

        [Theory]
        [MemberData(nameof(Rings))]
        public void TheExactTestsPerPiece_AreBoundedByAConstant_OnTheCylinderAndItsShift(int K)
        {
            DMesh3 a = Cylinder(K);
            ExactMeshBoolean b = Run(new[] { a, Shifted(a, 0) }, ExactMeshBoolean.Operations.Union, Mode.Default, serial: false);
            double perPiece = (double)b.Counts["coplanar_tests"] / b.Counts["pieces"];
            Assert.True(perPiece <= 8.5, "tests per piece " + perPiece + " pieces " + b.Counts["pieces"] + " tests " + b.Counts["coplanar_tests"]);
            Assert.True(b.Counts["pieces"] > 1000);
        }

        [Fact]
        public void TheScan_NeedsTheTestsThatTheWalkSaves()
        {
            DMesh3 a = Cylinder(16);
            long scan = Run(new[] { a, Shifted(a, 0) }, ExactMeshBoolean.Operations.Union, Mode.Scan).Counts["coplanar_tests"];
            long walk = Run(new[] { a, Shifted(a, 0) }, ExactMeshBoolean.Operations.Union, Mode.Default).Counts["coplanar_tests"];
            Assert.True(walk * 5 < scan, scan + " scan tests, " + walk + " walk tests");
        }

        static string ResultText(ExactMeshBoolean b)
        {
            string all = Snapshot(b);
            return all.Substring(all.LastIndexOf('|') + 1);
        }

        [Fact]
        public void UnderSimilarities_TheWalkGivesTheScansResult_AndThePowersOfTwoScaleTheResultExactly()
        {
            DMesh3 a = Cylinder(5), b = Shifted(a, 0);
            string reference = null;
            foreach (Similarity sim in Similarity.All()) {
                var pair = new[] { Invariance.Transformed(a, sim), Invariance.Transformed(b, sim) };
                string scan = Snapshot(Run(pair, ExactMeshBoolean.Operations.Union, Mode.Scan));
                Assert.True(scan == Snapshot(Run(pair, ExactMeshBoolean.Operations.Union, Mode.Forced)), sim.Name);
                if (sim.IsExact) {
                    // (the 2^k scalings: the same mesh scaled, bit for bit, and the same pieces and stacks)
                    DMesh3 r = Run(pair, ExactMeshBoolean.Operations.Union, Mode.Forced).Result;
                    DMesh3 expected = Run(new[] { a, b }, ExactMeshBoolean.Operations.Union, Mode.Forced).Result;
                    Assert.Equal(expected.VertexCount, r.VertexCount);
                    foreach (int v in expected.VertexIndices()) {
                        Vector3d p = expected.GetVertex(v), q = r.GetVertex(v);
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * sim.Scale), BitConverter.DoubleToInt64Bits(q.x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * sim.Scale), BitConverter.DoubleToInt64Bits(q.y));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * sim.Scale), BitConverter.DoubleToInt64Bits(q.z));
                    }
                    Assert.Equal(expected.TriangleCount, r.TriangleCount);
                    if (reference == null) reference = "set";
                }
            }
            Assert.NotNull(reference);
        }

        [Fact]
        public void ComputingTwiceOnOneInstance_GivesTheSameResult()
        {
            DMesh3 a = Cylinder(6);
            var b = new ExactMeshBoolean(new[] { a, Shifted(a, 0) }, ExactMeshBoolean.Operations.Union) { RecordStacks = true, RecordCertificate = true, CoplanarWalkMinCoplanar = 0, CoplanarWalkMinPieces = 0 };
            gParallel.SerialOnThisThread = true;
            try {
                Assert.True(b.Compute(), b.Error);
                string first = Snapshot(b);
                Assert.True(b.Compute(), b.Error);
                Assert.True(first == Snapshot(b));
            }
            finally { gParallel.SerialOnThisThread = false; }
        }

        [Fact]
        public void TheWalkOnSeveralThreads_GivesTheResultOfOne_AndRepeatedRunsAgree()
        {
            DMesh3 a = Cylinder(10);
            var pair = new[] { a, Shifted(a, 0) };
            string serial = ResultText(Run(pair, ExactMeshBoolean.Operations.Union, Mode.Forced, serial: true));
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == ResultText(Run(pair, ExactMeshBoolean.Operations.Union, Mode.Forced, serial: false)), "run " + run);
        }
    }
}
