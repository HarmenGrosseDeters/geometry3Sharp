using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean gives bit-identical results (vertex and triangle ids, coordinates) in repeated runs. The
    /// arrangement assigns the ids of its implicit points in parallel, so anything after it that orders by point
    /// id depends on the thread schedule: the Lawson flips of ExactPlanarSimplify (edges keyed by the smaller
    /// point id), and the splitting of edges where the result touches itself (edges and their pairs found in the
    /// order of point ids). Each case is run often enough that the old code gave several different results.
    /// </summary>
    public class ExactBooleanDeterminismTests
    {
        const int Runs = 30;

        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // every vertex and triangle id with its coordinates (bits) and vertices
        static string Fingerprint(DMesh3 m)
        {
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append('/').Append(m.MaxTriangleID).Append('|');
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            return sb.ToString();
        }

        static void AssertRepeatable(Func<ExactMeshBoolean> make, Action<ExactMeshBoolean> check = null)
        {
            string first = null;
            for (int run = 0; run < Runs; ++run) {
                ExactMeshBoolean b = make();
                Assert.True(b.Compute(), b.Error);
                check?.Invoke(b);
                // closed inputs: a closed, manifold result (the runs are identical, so checking the first is enough)
                if (run == 0)
                    ResultChecks.AssertClosed(b.Result);
                string print = Fingerprint(b.Result);
                if (b.ResultExactVertices != null)
                    print += "|" + string.Join(";", b.ResultExactVertices.Select(p => $"{p.X},{p.Y},{p.Z},{p.W}"));
                if (first == null)
                    first = print;
                else
                    Assert.True(first == print, $"run {run} differs from run 0");
            }
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void BoxAndSphere_RepeatedRuns_BitIdentical(ExactMeshBoolean.Operations op)
        {
            // flat regions of the box are simplified, with Lawson flips
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            AssertRepeatable(() => new ExactMeshBoolean(box, sphere, op), (b) => Assert.True(b.RemovedVertices > 0));
        }

        [Fact]
        public void SafeRoundingAndExactVertices_RepeatedRuns_BitIdentical()
        {
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            AssertRepeatable(() => new ExactMeshBoolean(box, sphere, ExactMeshBoolean.Operations.Intersection) { SafeRounding = true, ComputeExactVertices = true },
                (b) => {
                    Assert.NotNull(b.ResultExactVertices);
                    // SafeRounding: verified free of self-intersections and degenerate triangles (RoundingClean, the
                    // documented flag), and an independent check of the same
                    Assert.True(b.RoundingClean);
                    ResultChecks.AssertClean(b.Result);
                });
        }

        [Fact]
        public void CoplanarBoxes_RepeatedRuns_BitIdentical()
        {
            // overlapping boxes with coplanar faces: many points inside flat regions to remove
            var meshes = new List<DMesh3>();
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    for (int k = 0; k < 2; ++k)
                        meshes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0.9 * k), new Vector3d(0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1)));
            AssertRepeatable(() => new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union), (b) => {
                Assert.True(b.RemovedVertices > 0);
                // the union is the cube [0, 1.9]^3, exactly
                Assert.Equal(1.9 * 1.9 * 1.9, ResultChecks.Volume(b.Result), 12);
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TouchingAlongEdge_RepeatedRuns_BitIdentical(bool simplify)
        {
            // a box touched along two edges by two other boxes, spheres crossing the edges: each edge (split at
            // implicit points) has pieces with four triangles of the result, split per pair at new vertices
            var meshes = new List<DMesh3>() { Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), Box(new Vector3d(1, 1, -0.2), new Vector3d(2, 2, 1.3)),
                Box(new Vector3d(-1, -1, -0.3), new Vector3d(0, 0, 1.2)) };
            for (int k = 0; k < 3; ++k) {
                meshes.Add(Sphere(new Vector3d(1.05, 0.93, 0.2 + 0.3 * k), 0.15, 5));
                meshes.Add(Sphere(new Vector3d(-0.04, 0.06, 0.25 + 0.3 * k), 0.13, 5));
            }
            AssertRepeatable(() => new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) { SimplifyFlatRegions = simplify },
                (b) => Assert.True(b.SplitNonManifoldEdges > 2));
        }
    }
}
