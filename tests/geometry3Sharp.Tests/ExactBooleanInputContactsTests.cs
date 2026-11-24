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
    /// #1431: RoundingClean and MeshRobustRepair.NotCleanReason answer two different questions on a result whose exact solid touches itself.
    /// RoundingClean says: no crossing, no degenerate triangle, and no contact the rounding made (vertices of different exact points at one
    /// position); a position where the exact result touches itself (the vertices of one exact point, duplicated so that the mesh is manifold)
    /// is a contact of the solid, not an intersection. NotCleanReason counts every touching pair of triangles, so it says
    /// "self-intersections" there, and ExactMeshBoolean.Contacts counts those positions.
    /// Two cases. The exact one (TouchingBoxes_*) has its self-touch by construction (boxes with small integer corners meeting along an
    /// edge or at a vertex): it pins the contract on every platform. The plate case (PlateDifference_*) is the case of #1431, which the issue
    /// calls plate 8 (19 contacts, 215 pairs, measured on Windows); the tests use plate 4 (6 contacts, 60 pairs on Windows) for the time limit,
    /// the same picture. Its inputs come from Math.Cos / Math.Sin and a rotation by 20 degrees, which are not bit-identical on every platform,
    /// and the contacts need an exact incidence of those doubles, so its assertions on the contacts are conditional on what the platform gives.
    /// The plate case is the procedural plate of benchmarks/SnapRoundingExperiment (plate:n, the case "plate" of benchmarks/BooleanComparison; here
    /// n = 4: the difference of a box and a grid of 4 x 4 prisms with 24 sides) minus a copy of it, turned by 20 degrees about the axis
    /// (1, 1, 0) through its centre and shifted by a quarter of its extent: the copy touches the plate at 6 positions (the exact result has
    /// them, vertices of one exact point), and 60 pairs of triangles meet there.
    /// </summary>
    public class ExactBooleanInputContactsTests
    {
        static DMesh3 Prism(double[] r, double h)
        {
            int n = r.Length;
            var m = new DMesh3();
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < n; ++i) {
                    double t = 2 * Math.PI * i / n;
                    m.AppendVertex(new Vector3d(r[i] * Math.Cos(t), r[i] * Math.Sin(t), k * h));
                }
            int c0 = m.AppendVertex(new Vector3d(0, 0, 0)), c1 = m.AppendVertex(new Vector3d(0, 0, h));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(c0, j, i);
                m.AppendTriangle(c1, n + i, n + j);
                m.AppendTriangle(i, j, n + j);
                m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        // the plate of benchmarks/SnapRoundingExperiment (plate:n): a box 2 x 2 x 0.2 minus n x n prisms
        static DMesh3 Plate(int n)
        {
            var parts = new List<DMesh3> { Box(0, 0, 0, 2, 2, 0.2) };
            double pitch = 2.0 / n;
            var r = new double[24];
            for (int i = 0; i < 24; ++i)
                r[i] = 0.3 * pitch;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    DMesh3 hole = Prism(r, 1);
                    MeshTransforms.Translate(hole, new Vector3d((i + 0.5) * pitch, (j + 0.5) * pitch, -0.4));
                    parts.Add(hole);
                }
            var boolean = new ExactMeshBoolean(parts, Ops.Difference) { AllowOpenMeshes = true };
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean.Result;
        }

        static DMesh3 PlateCopy(DMesh3 plate)
        {
            var copy = new DMesh3(plate);
            AxisAlignedBox3d bounds = plate.CachedBounds;
            MeshTransforms.Rotate(copy, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(copy, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
            return copy;
        }

        static readonly Lazy<DMesh3> PlateA = new Lazy<DMesh3>(() => Plate(4));

        static DMesh3 A() => new DMesh3(PlateA.Value);
        static DMesh3 B() => PlateCopy(PlateA.Value);

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, bool serial = false, bool exactVertices = false)
        {
            gParallel.SerialOnThisThread = serial;
            try {
                var boolean = new ExactMeshBoolean(a, b, Ops.Difference) { ComputeExactVertices = exactVertices };
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // the vertices of the mesh by position (-0.0 and 0.0 are one), only the positions with several vertices
        static List<List<int>> PositionsWithSeveralVertices(DMesh3 m)
        {
            return m.VertexIndices().GroupBy(v => { Vector3d p = m.GetVertex(v); return new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0); })
                .Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
        }

        static DMesh3 UnitBox(double x, double y, double z)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new Vector3d(x + 0.5, y + 0.5, z + 0.5), new Vector3d(0.5, 0.5, 0.5)), NoSharedVertices = false }.Generate().MakeDMesh();
        }

        // contacts: the number of positions where the exact solid touches itself (an edge: its two ends; a vertex: one)
        [Theory]
        [InlineData(1, 1, 0, 2)]   // two unit boxes meeting along the edge x = 1, y = 1
        [InlineData(1, 1, 1, 1)]   // meeting at the corner (1, 1, 1)
        public void TouchingBoxes_Union_ExactContacts_RoundingCleanAndNotCleanReasonAnswerDifferentQuestions(double dx, double dy, double dz, int contacts)
        {
            var b = new ExactMeshBoolean(UnitBox(0, 0, 0), UnitBox(dx, dy, dz), Ops.Union) { ComputeExactVertices = true };
            Assert.True(b.Compute(), b.Error);
            DMesh3 m = b.Result;
            Assert.True(b.RoundingClean);
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.Equal(0, b.Rounding.RemainingFalseContacts);
            Assert.Equal(contacts, b.Contacts);
            Assert.True(m.IsClosed());
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, checkSelfIntersections: false));
            Assert.True(MeshRepairMetrics.SelfIntersectionPairs(m).Count > 0);
            List<List<int>> positions = PositionsWithSeveralVertices(m);
            Assert.Equal(contacts, positions.Count);
            foreach (List<int> at in positions)
                foreach (int v in at)
                    Assert.True(b.ResultExactVertices[v].Equals(b.ResultExactVertices[at[0]]), $"vertices {at[0]} and {v} are at one position, of different exact points");
            Assert.Empty(SolidBooleanPipeline.CrossingTrianglesOfAll(m));
        }

        [Fact]
        public void PlateDifference_ContactsOfTheExactSolid_RoundingCleanAndNotCleanReasonAnswerDifferentQuestions()
        {
            ExactMeshBoolean b = Run(A(), B(), exactVertices: true);
            DMesh3 m = b.Result;

            // RoundingClean: no crossing, no degenerate triangle, no contact made by the rounding
            Assert.True(b.RoundingClean);
            Assert.Equal(0, b.Rounding.RemainingIntersections);
            Assert.Equal(0, b.Rounding.RemainingDegenerate);
            Assert.Equal(0, b.Rounding.RemainingFalseContacts);

            // in every case: no crossing, and nothing else wrong with the mesh as stored
            Assert.True(m.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, checkSelfIntersections: false));
            Assert.Empty(SolidBooleanPipeline.CrossingTrianglesOfAll(m));
            List<List<int>> positions = PositionsWithSeveralVertices(m);
            Assert.Equal(b.Contacts, positions.Count);
            // every vertex at such a position belongs to one exact point (a contact the exact result has), none to different ones
            // (which would be a contact the rounding made)
            foreach (List<int> at in positions)
                foreach (int v in at)
                    Assert.True(b.ResultExactVertices[v].Equals(b.ResultExactVertices[at[0]]), $"vertices {at[0]} and {v} are at one position, of different exact points");

            // The contacts need an exact incidence of doubles that come from Math.Cos / Math.Sin and a rotation by 20 degrees, which need not be
            // bit-identical on every platform (6 contacts, 60 pairs on Windows): where the platform has none, the case shows nothing more
            // (TouchingBoxes_* pins the contract on every platform); where it has them, NotCleanReason counts them as self-intersections
            if (b.Contacts > 0) {
                Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(m));
                Assert.True(MeshRepairMetrics.SelfIntersectionPairs(m).Count > 0);
            } else
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        [Fact]
        public void PlateDifference_Similarities_Pow2BitIdentical_RoundingCleanMeansNoCrossing()
        {
            // the 2^k scales: the same result bit for bit, with the same answers of both checks. The other similarities round the inputs, which
            // can move the contacts (they need not stay exact) or make others: there RoundingClean still means "no crossing"
            ExactMeshBoolean b0 = Run(A(), B());
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(A(), sim), Invariance.Transformed(B(), sim));
                if (sim.IsExact) {
                    Assert.True(bs.RoundingClean, sim.ToString());
                    Assert.Equal(b0.Contacts, bs.Contacts);
                    Assert.Equal(b0.Contacts > 0 ? "self-intersections" : "", MeshRobustRepair.NotCleanReason(bs.Result));
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
                }
                if (bs.RoundingClean) {
                    Assert.Empty(SolidBooleanPipeline.CrossingTrianglesOfAll(bs.Result));
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(bs.Result, checkSelfIntersections: false));
                    if (bs.Contacts == 0)
                        Assert.Equal("", MeshRobustRepair.NotCleanReason(bs.Result));
                }
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

        [Fact]
        public void PlateDifference_SerialEqualsParallel_ComputeTwice()
        {
            ExactMeshBoolean serial = Run(A(), B(), serial: true);
            string expected = Print(serial.Result);
            for (int run = 0; run < 2; ++run) {
                ExactMeshBoolean parallel = Run(A(), B());
                Assert.True(expected == Print(parallel.Result), $"parallel run {run} differs from the serial run");
                Assert.Equal(serial.Contacts, parallel.Contacts);
                Assert.Equal(serial.RoundingClean, parallel.RoundingClean);
            }
            // one instance, computed twice
            var boolean = new ExactMeshBoolean(A(), B(), Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(expected == Print(boolean.Result));
            Assert.Equal(serial.Contacts, boolean.Contacts);
            Assert.True(boolean.RoundingClean);
        }
    }
}
