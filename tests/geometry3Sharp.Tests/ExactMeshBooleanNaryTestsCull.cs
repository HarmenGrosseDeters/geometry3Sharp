using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The T3a early-out of Difference and Intersection (ExactMeshBoolean.CullOutsideA, ExactMeshArrangement.CullOutsideOf):
    /// triangles of the other meshes wholly outside the closed first mesh are culled from the arrangement, and the result is
    /// the same, bit for bit, as without. (The class name keeps the mutation shard's test filter,
    /// FullyQualifiedName~ExactMeshBooleanNaryTests.)
    /// </summary>
    public class ExactMeshBooleanNaryTestsCull
    {
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

        static DMesh3 Join(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts)
                MeshEditor.Append(m, p);
            return m;
        }

        // A: a slab 4 x 4 x 1 (its triangles' boxes are whole faces); the Bi: pits through its top face (their caps above
        // it are culled, their walls kept), a body wholly above it, one far away (all culled), one wholly inside it (its
        // box meets no box of A, but it is inside: kept), and a self-intersecting one (two boxes in one mesh) whose upper
        // box lies above A and cuts the walls of the lower one there (culled triangles that intersect kept ones of their
        // own mesh)
        static List<DMesh3> Slab(out DMesh3 a)
        {
            a = Box(0, 0, 0, 4, 4, 1);
            return new List<DMesh3> {
                Box(0.3, 0.3, 0.5, 0.9, 0.9, 2),
                Box(2.2, 0.4, 0.25, 3.1, 1.3, 1.6),
                Box(1.2, 2.5, 1.4, 2.4, 3.3, 2.2),
                Box(10, 10, 10, 11, 11, 11),
                Box(3.1, 3.1, 0.3, 3.6, 3.6, 0.6),
                Join(Box(1, 1, 0.5, 2, 2, 3), Box(1.5, 1.5, 1.5, 2.5, 2.5, 3.5)),
            };
        }

        static ExactMeshBoolean Run(IList<DMesh3> meshes, Ops op, bool cull, int seed = 31337)
        {
            var b = new ExactMeshBoolean(meshes.Select(m => new DMesh3(m)), op) { CullOutsideA = cull, Seed = seed };
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(Status.Success, b.Status);
            return b;
        }

        // A first, then the Bi
        static List<DMesh3> SlabMeshes()
        {
            DMesh3 a;
            List<DMesh3> bs = Slab(out a);
            var list = new List<DMesh3> { a };
            list.AddRange(bs);
            return list;
        }

        [Theory]
        [InlineData(Ops.Difference)]
        [InlineData(Ops.Intersection)]
        public void Culled_SameResultAsWithout_EverySeed(Ops op)
        {
            List<DMesh3> meshes = SlabMeshes();
            ExactMeshBoolean reference = Run(meshes, op, false);
            Assert.Equal(0, reference.Counts["t3a_culled"]);
            // (the exact rays of the closed Bi still cross their culled caps: other seeds, other rays)
            foreach (int seed in new[] { 31337, 1, 2, 3, 4 }) {
                ExactMeshBoolean culled = Run(meshes, op, true, seed);
                Assert.True(culled.Counts["t3a_culled"] > 30, $"culled {culled.Counts["t3a_culled"]}");
                Assert.Equal("", Invariance.Difference(reference.Result, culled.Result));
                // (a culled triangle gives no piece: the classification never sees it, the work the early-out saves)
                var arr = culled.Arrangement;
                Assert.DoesNotContain(arr.Output, o => arr.Culled[o.Source]);
                Assert.True(arr.Output.Count < reference.Arrangement.Output.Count);
            }
            if (op == Ops.Difference) {
                Assert.True(reference.Result.TriangleCount > 0);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(reference.Result));
            }
        }

        [Theory]
        [InlineData(Ops.Difference)]
        [InlineData(Ops.Intersection)]
        public void SelfIntersectingBi_CulledTrianglesCutKeptOnesOutsideA(Ops op)
        {
            // the lower box crosses A's top face, the upper one lies above A and cuts the lower one's walls above A:
            // without the early-out these are self-intersecting pairs, with it the upper box is culled
            DMesh3 a = Box(0, 0, 0, 4, 4, 1);
            DMesh3 b = Join(Box(1, 1, 0.5, 2, 2, 3), Box(1.5, 1.5, 1.5, 2.5, 2.5, 3.5));
            var meshes = new List<DMesh3> { a, b };
            ExactMeshBoolean reference = Run(meshes, op, false), culled = Run(meshes, op, true);
            // (the upper box's 12 triangles and the lower box's cap above A)
            Assert.Equal(14, culled.Counts["t3a_culled"]);
            Assert.True(reference.SelfIntersectingPairs > 0);
            Assert.Equal(0, culled.SelfIntersectingPairs);
            Assert.Equal("", Invariance.Difference(reference.Result, culled.Result));
            Assert.True(culled.Result.TriangleCount > 0);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(culled.Result));
            // the volume: A minus (or within) the lower box's part inside A
            double inside = 1 * 1 * 0.5;
            double vol = MeshMeasurements.VolumeArea(culled.Result, culled.Result.TriangleIndices(), culled.Result.GetVertex).x;
            Assert.Equal(op == Ops.Difference ? 16 - inside : inside, vol, 9);
        }

        [Fact]
        public void NoCulling_OpenA_Union_OrPredicate()
        {
            List<DMesh3> meshes = SlabMeshes();
            DMesh3 a = meshes[0];
            // an open A: its winding number is not exact, so nothing is culled, and the result is today's
            DMesh3 open = new DMesh3(a);
            open.RemoveTriangle(open.TriangleIndices().First(), true, false);
            Assert.False(open.IsClosed());
            meshes[0] = open;
            foreach (Ops op in new[] { Ops.Difference, Ops.Intersection }) {
                ExactMeshBoolean culled = Run(meshes, op, true), reference = Run(meshes, op, false);
                Assert.Equal(0, culled.Counts["t3a_culled"]);
                Assert.Equal("", Invariance.Difference(reference.Result, culled.Result));
            }
            // a closed A, but a union (its result is not inside A) or a predicate of one's own
            meshes[0] = a;
            Assert.Equal(0, Run(meshes, Ops.Union, true).Counts["t3a_culled"]);
            var own = new ExactMeshBoolean(meshes.Select(m => new DMesh3(m)), (w) => w[0] > 0 && w.Skip(1).All(x => x <= 0));
            Assert.True(own.Compute(), own.Error);
            Assert.Equal(0, own.Counts["t3a_culled"]);
            // and the same predicate as the built-in Difference gives the same result
            Assert.Equal("", Invariance.Difference(Run(meshes, Ops.Difference, true).Result, own.Result));
        }

        [Fact]
        public void OpenBi_CulledTrianglesStillCountInTheirWindingNumber()
        {
            // an open hemisphere (its cap removed) through A's top face, mostly above A: its triangles above A are
            // culled, its winding number inside A (Antipodal Method or fast winding numbers) is that of the whole mesh
            DMesh3 a = Box(0, 0, 0, 4, 4, 1);
            DMesh3 h = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            foreach (int t in h.TriangleIndices().ToArray())
                if (h.GetTriCentroid(t).z > 0.5)
                    h.RemoveTriangle(t, true, false);
            MeshTransforms.Translate(h, new Vector3d(2, 2, 1.6));
            Assert.False(h.IsClosed());
            var meshes = new List<DMesh3> { a, h };
            foreach (int maxSegments in new[] { 1000, 0 }) {
                Func<bool, ExactMeshBoolean> run = (cull) => {
                    var b = new ExactMeshBoolean(meshes.Select(m => new DMesh3(m)), Ops.Difference) { CullOutsideA = cull, AntipodalMaxBoundarySegments = maxSegments };
                    Assert.True(b.Compute(), b.Error);
                    return b;
                };
                ExactMeshBoolean reference = run(false), culled = run(true);
                Assert.True(culled.Counts["t3a_culled"] > 50, $"culled {culled.Counts["t3a_culled"]}");
                Assert.Equal(maxSegments > 0 ? 1 : 0, culled.Counts["antipodal_meshes"]);
                Assert.Equal("", Invariance.Difference(reference.Result, culled.Result));
                // the hemisphere's lower part is inside A: the result lacks it
                double vol = MeshMeasurements.VolumeArea(culled.Result, culled.Result.TriangleIndices(), culled.Result.GetVertex).x;
                Assert.True(vol < 16 - 0.1, $"volume {vol}");
            }
        }

        [Fact]
        public void Similarities_CulledEqualsUnculled()
        {
            List<DMesh3> meshes = SlabMeshes();
            ExactMeshBoolean r0 = Run(meshes, Ops.Difference, true);
            foreach (Similarity sim in Similarity.All()) {
                List<DMesh3> t = meshes.Select(m => Invariance.Transformed(m, sim)).ToList();
                ExactMeshBoolean culled = Run(t, Ops.Difference, true), reference = Run(t, Ops.Difference, false);
                // (fewer under a rotation: the boxes of A's rotated faces are fat; the far Bi is culled under all)
                Assert.True(culled.Counts["t3a_culled"] >= 12, $"{sim}: culled {culled.Counts["t3a_culled"]}");
                Assert.Equal("", Invariance.Difference(reference.Result, culled.Result));
                if (sim.IsExact) {
                    Assert.Equal(r0.Counts["t3a_culled"], culled.Counts["t3a_culled"]);
                    Assert.Equal("", Invariance.Difference(r0.Result, Invariance.MappedBack(culled.Result, sim)));
                }
            }
        }

        [Fact]
        public void ParallelEqualsSerial_Repeated_AndReuse()
        {
            List<DMesh3> meshes = SlabMeshes();
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Run(meshes, Ops.Difference, true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 3; ++run) {
                ExactMeshBoolean p = Run(meshes, Ops.Difference, true);
                Assert.Equal(serial.Counts["t3a_culled"], p.Counts["t3a_culled"]);
                Assert.Equal("", Invariance.Difference(serial.Result, p.Result));
            }
            var op = new ExactMeshBoolean(meshes.Select(m => new DMesh3(m)), Ops.Difference);
            Assert.True(op.Compute());
            long first = op.Counts["t3a_culled"];
            Assert.True(op.Compute());
            Assert.Equal(first, op.Counts["t3a_culled"]);
            Assert.Equal("", Invariance.Difference(serial.Result, op.Result));
        }
    }
}
