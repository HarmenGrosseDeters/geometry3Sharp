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
    /// MeshRoundingRepair tests its triangle pairs with the projection filter TriangleTriangleFilter in front of the exact
    /// test. With MeshRoundingRepair.CheckOnThisThread every decision of the filter is compared with the exact test (it
    /// throws if they differ) and counted. The cases are CAD-like: axis-aligned boxes whose faces lie in common axis
    /// planes and touch at rims, where the filter decides most pairs, and a box turned about z, whose crossings with the
    /// other box are rounded onto the shared top and bottom planes.
    /// </summary>
    public class MeshRoundingRepairFilterTests
    {
        static DMesh3 Box(Vector3d center, Vector3d half)
        {
            return new TrivialBox3Generator() { Box = new Box3d(center, half), NoSharedVertices = false }.Generate().MakeDMesh();
        }

        // A and B per case: "steps" (B shifted by a non-dyadic amount, top faces and one side in common planes),
        // "flush" (B through A's top, its side flush with A's side x = 1), "turned" (B turned about z by 17 degrees,
        // the same top and bottom planes as A)
        static (DMesh3, DMesh3) Inputs(string name)
        {
            DMesh3 a = Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.4));
            switch (name) {
                case "steps":
                    return (a, Box(new Vector3d(0.9, 0.3, 0), new Vector3d(0.6, 0.7, 0.4)));
                case "flush":
                    return (a, Box(new Vector3d(0.5, 0.1, 0.5), new Vector3d(0.5, 0.45, 0.3)));
                case "turned": {
                    DMesh3 b = Box(new Vector3d(0.7, 0.2, 0), new Vector3d(0.8, 0.5, 0.4));
                    MeshTransforms.Rotate(b, new Vector3d(0.7, 0.2, 0), Quaterniond.AxisAngleD(Vector3d.AxisZ, 17));
                    // (the rotation rounds z: back onto the planes of A)
                    foreach (int v in b.VertexIndices()) {
                        Vector3d p = b.GetVertex(v);
                        b.SetVertex(v, new Vector3d(p.x, p.y, p.z > 0 ? 0.4 : -0.4));
                    }
                    return (a, b);
                }
            }
            throw new ArgumentException(name);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string name in new[] { "steps", "flush", "turned" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    yield return new object[] { name, op };
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
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

        static void AssertClean(ExactMeshBoolean b, string what)
        {
            Assert.True(b.RoundingClean, what);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
            Assert.True(Invariance.AllFinite(b.Result), what);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FilterDecisions_AgreeWithExactTest(string name, Ops op)
        {
            // the check throws on a decision that differs from the exact test; on these inputs the filter decides most
            // pairs (all the coplanar neighbours of the faces), and the result is the one without the check, clean
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean plain = Run(a, b, op);
            ExactMeshBoolean checkedRun = Run(a, b, op, check: true);
            Assert.Equal(Print(plain.Result), Print(checkedRun.Result));
            AssertClean(checkedRun, name + " " + op);
            MeshRoundingRepair r = checkedRun.Rounding;
            Assert.True(r.PairTestsChecked > 0, "no pair tests");
            Assert.True(2 * r.FilterDecisions > r.PairTestsChecked,
                $"{name} {op}: the filter decided {r.FilterDecisions} of {r.PairTestsChecked} pair tests");
        }

        [Fact]
        public void FilterDecisions_InDegenerateFamilies()
        {
            // the families of SafeRoundingDegenerateInputTests (general position, rotated): the filter's projection along
            // the dominant axis decides some pairs there too, each compared with the exact test
            long decided = 0, tests = 0;
            foreach (SafeRoundingDegenerateInputTests.Family f in new[] { SafeRoundingDegenerateInputTests.Family.Sliver,
                SafeRoundingDegenerateInputTests.Family.Coincident, SafeRoundingDegenerateInputTests.Family.Cluster }) {
                DMesh3 m = SafeRoundingDegenerateInputTests.Make(f, 30, 1);
                ExactMeshBoolean b = Run(m, new DMesh3(), Ops.Union, check: true);
                decided += b.Rounding.FilterDecisions;
                tests += b.Rounding.PairTestsChecked;
            }
            Assert.True(tests > 0);
            Assert.True(decided > 0, $"the filter decided none of {tests} pair tests");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Similarities_SameResult(string name, Ops op)
        {
            // 2^k scales give the same result bit for bit (and the same filter decisions), and every similarity gives a
            // clean result but one: rotated, the planes are no longer axis planes, and on "steps" difference the
            // rounding of the crossings leaves a cap the repair keeps (the same with and without the filter, whose
            // decisions are checked here: a SafeRounding limit, not a filter case; Exact Boolean 2.0 finding D1), so
            // there the result is checked for being closed and finite
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean b0 = Run(a, b, op, check: true);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), op, check: true);
                if (sim.Name.Contains("rotated") && name == "steps" && op == Ops.Difference)
                    Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), $"{name} {op} {sim}");
                else
                    AssertClean(bs, $"{name} {op} {sim}");
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
                    Assert.Equal(b0.Rounding.FilterDecisions, bs.Rounding.FilterDecisions);
                }
            }
        }

        // (the boxes are small, so most of these runs stay on one thread; MeshRoundingRepairIncrementalTests'
        // SerialEqualsParallel_BitIdentical runs the larger degenerate families, through the filter as well)
        [Theory]
        [MemberData(nameof(Cases))]
        public void SerialEqualsParallel_Repeated(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            string serial = Print(Run(a, b, op, serial: true).Result);
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Print(Run(a, b, op).Result), $"parallel run {run} differs from the serial run");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ComputeTwice_SameResult(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(boolean.Compute(), boolean.Error);
            string first = Print(boolean.Result);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(first, Print(boolean.Result));
            AssertClean(boolean, name + " " + op);
        }
    }
}
