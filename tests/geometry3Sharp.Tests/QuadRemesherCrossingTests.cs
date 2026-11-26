using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1674: QuadRemesher.RemoveCrossings unlinks the longer edge of two graph edges that cross each other (the lattice extraction can link two
    /// vertices over a valence-3 vertex whose third link crosses the chord), on procedural graphs.
    /// </summary>
    public class QuadRemesherCrossingTests
    {
        static QuadRemesher.PolyGraph Graph(Vector3d[] pts, int[][] edges, Vector3d? normal = null)
        {
            var g = new QuadRemesher.PolyGraph();
            foreach (var p in pts) g.Add(p, normal ?? Vector3d.AxisZ, 0);
            foreach (var e in edges) g.Link(e[0], e[1]);
            return g;
        }

        static HashSet<string> Edges(QuadRemesher.PolyGraph g)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < g.Pos.Count; ++i) foreach (int j in g.Adj[i]) if (i < j) set.Add(i + "-" + j);
            return set;
        }

        // the configuration of the issue: a (0,0), b (0,2) linked over v (0.05, 1) which is linked to both, and v's third link to c (1.5, 1.05)... crossing the chord a-b
        // from the left: v lies on the right of the chord, c on the left
        static readonly Vector3d[] Chord = { new Vector3d(0, 0, 0), new Vector3d(0, 2, 0), new Vector3d(0.05, 1, 0), new Vector3d(-1.5, 1.05, 0) };
        static readonly int[][] ChordEdges = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 2, 1 }, new[] { 2, 3 } };

        [Fact]
        public void TheChordOfADegenerateTriangle_IsRemoved()
        {
            var g = Graph(Chord, ChordEdges);
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g));
            Assert.Equal(new[] { "0-2", "1-2", "2-3" }.OrderBy(x => x), Edges(g).OrderBy(x => x));
        }

        [Fact]
        public void TwoCrossingDiagonals_LoseTheLongerOne_AndATieTheLowerEdge()
        {
            // a square (corners 0..3) with both diagonals, the first a little longer
            var pts = new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(2, 1, 0), new Vector3d(0, 1, 0) };
            var g = Graph(pts, new[] { new[] { 0, 2 }, new[] { 1, 3 } });
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g));                 // both have the length of the diagonal: the lower edge (0, 2) goes
            Assert.Equal(new[] { "1-3" }, Edges(g).ToArray());
            var pts2 = new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(2.5, 1, 0), new Vector3d(0, 1, 0) };
            var g2 = Graph(pts2, new[] { new[] { 0, 2 }, new[] { 1, 3 } });    // 0-2 is 2.69, 1-3 is 2.24: the longer goes
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g2));
            Assert.Equal(new[] { "1-3" }, Edges(g2).ToArray());
        }

        [Fact]
        public void AGridAndEdgesThatOnlyShareVertices_AreUnchanged()
        {
            var pts = new List<Vector3d>(); var edges = new List<int[]>();
            for (int j = 0; j < 5; ++j) for (int i = 0; i < 5; ++i) pts.Add(new Vector3d(i, j, 0));
            for (int j = 0; j < 5; ++j) for (int i = 0; i < 5; ++i) {
                if (i + 1 < 5) edges.Add(new[] { j * 5 + i, j * 5 + i + 1 });
                if (j + 1 < 5) edges.Add(new[] { j * 5 + i, (j + 1) * 5 + i });
            }
            var g = Graph(pts.ToArray(), edges.ToArray());
            var before = Edges(g);
            Assert.Equal(0, QuadRemesher.RemoveCrossings(g));
            Assert.Equal(before.OrderBy(x => x), Edges(g).OrderBy(x => x));
            // a fan from one vertex: the edges touch, they do not cross
            var fan = Graph(new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0) }, new[] { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 3 } });
            Assert.Equal(0, QuadRemesher.RemoveCrossings(fan));
        }

        [Fact]
        public void EdgesOnDifferentSheetsOrWithDisagreeingNormals_AreNotCrossings()
        {
            // the same crossing 1.5 apart in height (two sheets one above the other, a long edge each): not a crossing
            var pts = new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, -1, 1.5), new Vector3d(1, 1, 1.5) };
            var g = Graph(pts, new[] { new[] { 0, 1 }, new[] { 2, 3 } });
            Assert.Equal(0, QuadRemesher.RemoveCrossings(g));
            // normals that cancel (the two edges on opposite sides of a thin solid): no plane to judge in
            var h = Graph(new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, -1, 0.01), new Vector3d(1, 1, 0.01) }, new[] { new[] { 0, 1 }, new[] { 2, 3 } });
            h.Nrm[0] = h.Nrm[1] = Vector3d.AxisZ; h.Nrm[2] = h.Nrm[3] = -Vector3d.AxisZ;
            Assert.Equal(0, QuadRemesher.RemoveCrossings(h));
            // normals that nearly cancel (the sum is shorter than one): the same
            var k = Graph(new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, -1, 0.01), new Vector3d(1, 1, 0.01) }, new[] { new[] { 0, 1 }, new[] { 2, 3 } });
            Vector3d tilted = new Vector3d(Math.Sin(170 * Math.PI / 180), 0, Math.Cos(170 * Math.PI / 180));
            k.Nrm[0] = k.Nrm[1] = Vector3d.AxisZ; k.Nrm[2] = k.Nrm[3] = tilted;
            Assert.Equal(0, QuadRemesher.RemoveCrossings(k));
            // agreeing normals: the same two edges cross
            var agree = Graph(new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, -1, 0.01), new Vector3d(1, 1, 0.01) }, new[] { new[] { 0, 1 }, new[] { 2, 3 } });
            Assert.Equal(1, QuadRemesher.RemoveCrossings(agree));
        }

        [Fact]
        public void TheResult_DoesNotDependOnTheNumberingAndIsInvariantUnderSimilarities()
        {
            var reference = Graph(Chord, ChordEdges);
            QuadRemesher.RemoveCrossings(reference);
            var kept = Edges(reference);
            // a reversed numbering: the same geometric edges are removed
            int n = Chord.Length;
            var perm = Enumerable.Range(0, n).Reverse().ToArray();         // new index of old vertex i: perm[i]
            var pts = new Vector3d[n];
            for (int i = 0; i < n; ++i) pts[perm[i]] = Chord[i];
            var edges = ChordEdges.Select(e => new[] { perm[e[0]], perm[e[1]] }).ToArray();
            var g = Graph(pts, edges);
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g));
            var keptPerm = Edges(g);
            var back = new HashSet<string>(kept.Select(k => { var p = k.Split('-'); int a = perm[int.Parse(p[0])], b = perm[int.Parse(p[1])]; return Math.Min(a, b) + "-" + Math.Max(a, b); }));
            Assert.True(back.SetEquals(keptPerm));
            // similarities: the graph moved with its normals gives the same edges
            foreach (Similarity s in Similarity.All()) {
                var moved = new QuadRemesher.PolyGraph();
                foreach (var p in Chord) moved.Add(s.Apply(p), s.ApplyDirection(Vector3d.AxisZ), 0);
                foreach (var e in ChordEdges) moved.Link(e[0], e[1]);
                Assert.Equal(1, QuadRemesher.RemoveCrossings(moved));
                Assert.True(kept.SetEquals(Edges(moved)), s.Name);
            }
        }

        [Fact]
        public void ALinkOfACreaseChain_IsKept_AndTheOtherEdgeGoes()
        {
            // the chord a-b joins two crease vertices (a link of a chain): the crossing edge v-c goes instead
            var g = Graph(Chord, ChordEdges);
            g.Kind[0] = 1; g.Kind[1] = 1;
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g));
            Assert.Equal(new[] { "0-1", "0-2", "1-2" }.OrderBy(x => x), Edges(g).OrderBy(x => x));
            // both are links of chains (v and c are crease vertices too): neither goes
            var both = Graph(Chord, ChordEdges);
            both.Kind[0] = 1; both.Kind[1] = 1; both.Kind[2] = 1; both.Kind[3] = 1;
            Assert.Equal(0, QuadRemesher.RemoveCrossings(both));
            Assert.Equal(4, Edges(both).Count);
            // only one end of the chord is a crease vertex: no link, the chord goes
            var one = Graph(Chord, ChordEdges);
            one.Kind[0] = 1;
            Assert.Equal(1, QuadRemesher.RemoveCrossings(one));
            Assert.False(Edges(one).Contains("0-1"));
            // the shorter one is the link, the longer is not: the longer goes as before
            var other = Graph(Chord, ChordEdges);
            other.Kind[2] = 1; other.Kind[3] = 1;
            Assert.Equal(1, QuadRemesher.RemoveCrossings(other));
            Assert.False(Edges(other).Contains("0-1"));
        }

        // no defect, no change: where the extraction drops no face, the repair of the crossing edges is never tried and the output is the same bit for bit
        [Theory]
        [InlineData("sharp-box", 20)]
        [InlineData("sharp-box", 40)]
        [InlineData("sharp-cylinder", 16)]
        [InlineData("sharp-cylinder", 30)]
        [InlineData("sharp-cylinder", 40)]
        [InlineData("gear", 16)]
        [InlineData("gear", 20)]
        [InlineData("gear", 32)]
        [InlineData("sharp-box", 26)]       // the repair acts: the output differs from the one without it, and has no dropped face
        [InlineData("gear", 18)]
        public void RepairOfCrossings_ChangesOnlyOutputsThatDropAFace(string name, int passes)
        {
            bool defect = (name == "sharp-box" && passes == 26) || (name == "gear" && passes == 18);
            DMesh3 m = QuadRemesherClosedOutputTests.Shape(name, passes);
            double s = QuadRemesherClosedOutputTests.TargetOf(name);
            var without = new QuadRemesher(m) { TargetEdgeLength = s, PreserveCreases = true, SkipCrossingRepair = true };
            Assert.Equal(QuadRemeshStatus.Success, without.Compute());
            QuadRemesher with = QuadRemesherClosedOutputTests.Run(QuadRemesherClosedOutputTests.Shape(name, passes), s);
            Assert.Equal(defect, without.DroppedFaces > 0);       // the switch of the repair works
            if (without.DroppedFaces == 0) QuadRemesherClosedOutputTests.AssertSame(without.Result, with.Result);
            else {
                Assert.Equal(0, with.DroppedFaces);
                Assert.True(without.Result.FaceCount != with.Result.FaceCount || PolyMeshMetrics.Compute(without.Result).BoundaryVertices > 0);
                Assert.Equal(0, PolyMeshMetrics.Compute(with.Result).BoundaryVertices);
            }
        }

        [Fact]
        public void RemovingTwiceChangesNothing()
        {
            var g = Graph(Chord, ChordEdges);
            Assert.Equal(1, QuadRemesher.RemoveCrossings(g));
            Assert.Equal(0, QuadRemesher.RemoveCrossings(g));
        }
    }
}
