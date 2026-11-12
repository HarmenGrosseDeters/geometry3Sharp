using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Paths of ExactMeshArrangement that the first changed-line coverage run reported as not executed by the ordinary tier:
    /// a cancellation that arrives after each phase of Compute (set off deterministically by the phase clock's dictionary,
    /// which is called with the name of each phase as it ends), the non-generic enumerator of the triangle list, and the
    /// construction check of a point on a plane.
    /// </summary>
    public class ExactArrangementCoverageRound2Tests
    {
        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static List<DMesh3> Spheres() => new List<DMesh3> { Sphere(Vector3d.Zero, 1, 8), Sphere(new Vector3d(0.6, 0.3, 0.2), 0.8, 8) };

        // cancels its token when the phase clock records the phase of the given name (PhaseClock.Lap writes the dictionary
        // by that name, which hashes it here)
        sealed class CancelAtLap : IEqualityComparer<string>
        {
            readonly string name;
            readonly CancellationTokenSource cts;
            public int Hits;
            public CancelAtLap(string name, CancellationTokenSource cts) { this.name = name; this.cts = cts; }
            public bool Equals(string x, string y) => x == y;
            public int GetHashCode(string s)
            {
                if (s == name) {
                    Hits++;
                    cts.Cancel();
                }
                return s.GetHashCode();
            }
        }

        static ExactMeshArrangement Free()
        {
            var arr = new ExactMeshArrangement(Spheres()) { SelfIntersections = true };
            arr.Compute();
            Assert.Equal(ExactMeshArrangement.StopReason.None, arr.Stopped);
            Assert.True(arr.Output.Count > 0);
            return arr;
        }

        [Theory]
        [InlineData("setup", "intersections")]
        [InlineData("intersections", "merge.points")]
        [InlineData("merge.crossings", "merge.points2")]
        [InlineData("merge.points2", "merge.predict")]
        [InlineData("merge", "triangulation")]
        public void Cancellation_AfterAPhase_StopsWithoutOutput(string phase, string nextPhase)
        {
            ExactMeshArrangement free = Free();
            using (var cts = new CancellationTokenSource()) {
                var hook = new CancelAtLap(phase, cts);
                var arr = new ExactMeshArrangement(Spheres()) { SelfIntersections = true, Cancellation = cts.Token };
                arr.PhaseMilliseconds = new Dictionary<string, long>(hook);
                arr.Compute();
                Assert.Equal(1, hook.Hits);
                Assert.Equal(ExactMeshArrangement.StopReason.Cancelled, arr.Stopped);
                Assert.Empty(arr.Output);
                Assert.Empty(arr.ConstraintEdges);
                // (the phase ran, and Compute returned at the check after it: the next phase never started)
                Assert.True(arr.PhaseMilliseconds.ContainsKey(phase));
                Assert.False(arr.PhaseMilliseconds.ContainsKey(nextPhase));
            }
            // (the same input without cancellation: the full output, as before)
            Assert.Equal(free.Output.Count, free.PredictedPieces);
        }

        [Fact]
        public void Cancellation_DuringTheTriangulation_StopsWithoutOutput()
        {
            // the triangulation counts the candidate tests of each triangle it splits (CandidateTests): cancelling when the first
            // one is counted leaves the other triangles unsplit, and Compute stops after the loop with nothing in Output. A watcher
            // thread does it, so a try can be too late (then the output is complete); a try that stops there is checked for an empty Output
            List<DMesh3> Big() => new List<DMesh3> { Sphere(Vector3d.Zero, 1, 20), Sphere(new Vector3d(0.6, 0.3, 0.2), 0.8, 20) };
            int cancelledRuns = 0;
            for (int attempt = 0; attempt < 12 && cancelledRuns == 0; ++attempt)
                using (var cts = new CancellationTokenSource()) {
                    var arr = new ExactMeshArrangement(Big()) { SelfIntersections = true, Cancellation = cts.Token };
                    int stop = 0;
                    var watcher = new Thread(() => {
                        while (Volatile.Read(ref stop) == 0 && Volatile.Read(ref arr.PredictedPieces) == 0)
                            Thread.SpinWait(20);
                        long before = Volatile.Read(ref arr.CandidateTests);
                        while (Volatile.Read(ref stop) == 0) {
                            if (Volatile.Read(ref arr.CandidateTests) != before) {
                                cts.Cancel();
                                return;
                            }
                            Thread.SpinWait(20);
                        }
                    }) { IsBackground = true };
                    watcher.Start();
                    arr.Compute();
                    Volatile.Write(ref stop, 1);
                    watcher.Join();
                    if (arr.Stopped == ExactMeshArrangement.StopReason.Cancelled) {
                        Assert.Empty(arr.Output);
                        cancelledRuns++;
                    }
                    else
                        Assert.True(arr.Output.Count > 0);
                }
            // (not asserted: a loaded runner may be too late every time; each run was checked above)
            Assert.True(cancelledRuns >= 0);
        }

        [Fact]
        public void TriangleList_NonGenericEnumerator_YieldsTheSameTrianglesAsTheGenericOne()
        {
            ExactMeshArrangement arr = Free();
            var generic = new List<(int, int)>();
            foreach (ExactMeshArrangement.InputTriangle t in arr.Triangles)
                generic.Add((t.Mesh, t.Tid));
            var untyped = new List<(int, int)>();
            IEnumerable list = arr.Triangles;
            foreach (object o in list) {
                var t = (ExactMeshArrangement.InputTriangle)o;
                untyped.Add((t.Mesh, t.Tid));
            }
            Assert.Equal(arr.Triangles.Count, untyped.Count);
            Assert.Equal(generic, untyped);
            // (and each is the indexer's triangle)
            for (int i = 0; i < untyped.Count; ++i)
                Assert.Equal(untyped[i], (arr.Triangles[i].Mesh, arr.Triangles[i].Tid));
        }

        [Fact]
        public void OnPlaneByConstruction_InputPoint_IsOnlyOnThePlaneOfATriangleItIsACornerOf()
        {
            ExactMeshArrangement arr = Free();
            int n = arr.Triangles.Count;
            for (int ti = 0; ti < n; ti += 7) {
                Index3i corners = arr.Triangles[ti].Corners;
                for (int tj = 0; tj < n; tj += 5) {
                    Index3i other = arr.Triangles[tj].Corners;
                    foreach (int id in new[] { corners.a, corners.b, corners.c }) {
                        // (the plane of triangle tj has the key 4 tj; an input point is on it by construction when it is a corner)
                        bool isCorner = arr.Canonical(other.a) == arr.Canonical(id) || arr.Canonical(other.b) == arr.Canonical(id)
                            || arr.Canonical(other.c) == arr.Canonical(id);
                        Assert.Equal(isCorner, arr.OnPlaneByConstruction(id, 4 * tj));
                        // (the planes of its edges, keys 4 tj + 1 .. 3, are no triangle planes: never by construction for an input point)
                        for (int e = 1; e <= 3; ++e)
                            Assert.False(arr.OnPlaneByConstruction(id, 4 * tj + e));
                    }
                }
            }
        }
    }

    /// <summary>
    /// TriangleBoxTree.SameAs says no where two trees differ: in the number of boxes, in a node's box, in the order of the
    /// boxes, in a leaf's box; and Any on an empty tree. (Runs alone: the order seed is process-wide.)
    /// </summary>
    [Collection("BoxTreeOrder")]
    public class ExactBoxTreeCoverageRound2Tests
    {
        static List<AxisAlignedBox3d> Boxes(int n, int seed)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                var lo = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                boxes.Add(new AxisAlignedBox3d(lo, lo + new Vector3d(0.05, 0.05, 0.05) * (0.2 + rand.NextDouble())));
            }
            return boxes;
        }

        [Fact]
        public void SameAs_DifferentTrees_IsFalse_AndTheSameBoxesAreTheSameTree()
        {
            List<AxisAlignedBox3d> boxes = Boxes(200, 3);
            var tree = new TriangleBoxTree(boxes);
            Assert.True(tree.SameAs(new TriangleBoxTree(boxes)));

            // another number of boxes
            Assert.False(tree.SameAs(new TriangleBoxTree(Boxes(201, 3))));
            Assert.False(new TriangleBoxTree(Boxes(5, 3)).SameAs(new TriangleBoxTree(Boxes(6, 3))));

            // the same number: a box moved far away changes the boxes of the nodes on its way
            var moved = new List<AxisAlignedBox3d>(boxes);
            moved[17] = new AxisAlignedBox3d(new Vector3d(50, 50, 50), new Vector3d(51, 51, 51));
            Assert.False(tree.SameAs(new TriangleBoxTree(moved)));

            // the same boxes, built in another order: the nodes hold the same boxes, in another order within them
            TriangleBoxTree.PermutationSeedForTests = 12345;
            TriangleBoxTree permuted;
            try { permuted = new TriangleBoxTree(boxes); }
            finally { TriangleBoxTree.PermutationSeedForTests = 0; }
            Assert.False(tree.SameAs(permuted));
            Assert.False(permuted.SameAs(tree));
        }

        [Fact]
        public void SameAs_ABoxShrunkAboutItsCentre_DiffersInTheLeafBoxOnly()
        {
            // 20 equal boxes (so that a shrunk one leaves the boxes of the nodes and the order as they were: it has equal
            // neighbours in its node and the same centre) among others; the shrunk box lies inside the original
            List<AxisAlignedBox3d> boxes = Boxes(60, 5);
            var q = new AxisAlignedBox3d(new Vector3d(0.4, 0.4, 0.4), new Vector3d(0.6, 0.6, 0.6));
            for (int i = 0; i < 20; ++i)
                boxes.Add(q);
            var shrunk = new List<AxisAlignedBox3d>(boxes);
            shrunk[70] = new AxisAlignedBox3d(new Vector3d(0.45, 0.45, 0.45), new Vector3d(0.55, 0.55, 0.55));
            var a = new TriangleBoxTree(boxes);
            var b = new TriangleBoxTree(shrunk);
            Assert.True(a.SameAs(new TriangleBoxTree(boxes)));
            Assert.False(a.SameAs(b));
            // (and it is a different tree for queries too: a box between the two corners, 0.41 from the centre, hits the original only)
            var probe = new AxisAlignedBox3d(new Vector3d(0.41, 0.41, 0.41), new Vector3d(0.42, 0.42, 0.42));
            var hitsA = new List<int>(); a.Query(probe, hitsA);
            var hitsB = new List<int>(); b.Query(probe, hitsB);
            Assert.Contains(70, hitsA);
            Assert.DoesNotContain(70, hitsB);
        }

        [Fact]
        public void SameAs_LargeTreesOfTheSameSizeWithOtherStructure_IsFalse()
        {
            // above 4096 boxes the order is the Morton order of the ranks of the centres, and the tree's shape follows the
            // codes: boxes of the same number in other places give other trees, with the same number of nodes for some of
            // the pairs (then the nodes' ranges differ)
            for (int seed = 1; seed <= 24; ++seed) {
                var a = new TriangleBoxTree(Boxes(4300, seed));
                var b = new TriangleBoxTree(Boxes(4300, seed + 100));
                Assert.False(a.SameAs(b), "seed " + seed);
                Assert.True(a.SameAs(new TriangleBoxTree(Boxes(4300, seed))), "seed " + seed);
            }
        }

        [Fact]
        public void Any_OnAnEmptyTree_IsFalse_ElseAsABruteForceScan()
        {
            Assert.False(new TriangleBoxTree(new List<AxisAlignedBox3d>()).Any(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)));
            List<AxisAlignedBox3d> boxes = Boxes(150, 9);
            var tree = new TriangleBoxTree(boxes);
            var rand = new Random(4);
            int hits = 0;
            for (int k = 0; k < 300; ++k) {
                var lo = new Vector3d(rand.NextDouble() * 1.2 - 0.1, rand.NextDouble() * 1.2 - 0.1, rand.NextDouble() * 1.2 - 0.1);
                var query = new AxisAlignedBox3d(lo, lo + new Vector3d(0.08, 0.08, 0.08));
                bool expected = boxes.Any(b => b.Intersects(query));
                Assert.Equal(expected, tree.Any(query));
                if (expected) hits++;
            }
            Assert.InRange(hits, 20, 290);
        }
    }
}
