using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// False crossings in IntrinsicTriangulation.ExtractCommonSubdivision (#1313): a trace that crosses an input edge of
    /// its own end vertex, just outside the snap tolerance, was taken as running along that edge from the crossing
    /// on. The result was Success with a subdivision vertex of three edges (the areas still sum up). Found by
    /// benchmarks/IntrinsicIntegerCoordinates; the cases here are its sheared stretched grid, small, with random
    /// vertices inserted through the public API. Mesh and points use only + and *, so the inputs are the same
    /// bits on every platform.
    /// </summary>
    public class IntrinsicCommonSubdivisionFalseCrossingTests
    {
        const int GridCells = 3;

        // (GridCells + 1)^2 vertices at (a * (i + shear * j), j): cells stretched by a and sheared, so the Delaunay
        // flips make long thin intrinsic triangles
        static DMesh3 ShearedGrid(double a, double shear, Similarity sim)
        {
            var mesh = new DMesh3();
            int n = GridCells;
            int[,] vid = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    vid[i, j] = mesh.AppendVertex(sim.Apply(new Vector3d(a * (i + shear * j), j, 0)));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        // flips, V / 2 pseudo-random vertices (a linear congruential generator) inside pseudo-random triangles, flips
        static IntrinsicTriangulation Build(double a, double shear, ulong seed, Similarity sim, out DMesh3 mesh)
        {
            mesh = ShearedGrid(a, shear, sim);
            var T = new IntrinsicTriangulation(mesh);
            T.FlipToDelaunay();
            ulong x = seed;
            Func<double> rnd = () => {
                x = unchecked(x * 6364136223846793005UL + 1442695040888963407UL);
                return (x >> 11) * (1.0 / 9007199254740992.0);
            };
            int K = T.InputVertexCount / 2;
            for (int k = 0; k < K; ++k) {
                int t = (int)(rnd() * T.TriangleCount);
                double u = 0.0625 + 0.875 * rnd(), v = 0.0625 + 0.875 * rnd();
                if (u + v > 1) { u = 1 - u; v = 1 - v; }
                Vector2d q0, q1, q2;
                T.GetTriangleLayout(t, out q0, out q1, out q2);
                T.InsertVertex(t, u * q0 + v * q1 + (1 - u - v) * q2);
            }
            T.FlipToDelaunay();
            return T;
        }

        /// <summary>
        /// null if the faces pass the necessary conditions of the cell complex of a common subdivision, else the
        /// first one they fail: consistently oriented, every edge on two faces or on one at the input boundary, the
        /// Euler characteristic of the input, every crossing (a vertex that is not a triangulation vertex) with four
        /// edges, every point used. (The connectivity only: not the triangles of the faces, not the geometry.)
        /// </summary>
        static string ComplexDefect(DMesh3 mesh, IntrinsicTriangulation T, IntrinsicCommonSubdivision cs)
        {
            var directed = new HashSet<(int, int)>();
            var edges = new Dictionary<(int, int), int>();
            var valence = new Dictionary<int, int>();
            foreach (var f in cs.Faces)
                for (int k = 0; k < f.Corners.Length; ++k) {
                    int a = f.Corners[k], b = f.Corners[(k + 1) % f.Corners.Length];
                    if (directed.Add((a, b)) == false) return $"two faces run along edge {a} -> {b}";
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    edges.TryGetValue(key, out int c);
                    edges[key] = c + 1;
                    if (c == 0) {
                        valence.TryGetValue(a, out int va); valence[a] = va + 1;
                        valence.TryGetValue(b, out int vb); valence[b] = vb + 1;
                    }
                }
            int boundary = edges.Values.Count(c => c == 1);
            if (edges.Values.Any(c => c > 2)) return "an edge on more than two faces";
            // (no vertex was inserted on the input boundary: its edges are not cut)
            int inputBoundary = mesh.EdgeIndices().Count(e => mesh.IsBoundaryEdge(e));
            if (boundary != inputBoundary) return $"{boundary} boundary edges, the input has {inputBoundary}";
            int chi = valence.Count - edges.Count + cs.Faces.Count;
            int chiInput = mesh.VertexCount - mesh.EdgeCount + mesh.TriangleCount;
            if (chi != chiInput) return $"Euler characteristic {chi}, the input has {chiInput}";
            foreach (var kv in valence)
                if (kv.Key >= T.VertexCount && kv.Value != 4) return $"crossing {kv.Key} has {kv.Value} edges";
            if (valence.Count != cs.Points.Count) return "points that no face uses";
            return null;
        }

        // inputs on which the trace of an intrinsic edge crosses an input edge of its head vertex. Before the fix:
        // Success with a three-edged crossing, except the two marked rows, which were Inconsistent for another
        // reason found later in the function (two outer cycles)
        public static IEnumerable<object[]> FalseCrossingCases() => new[] {
            new object[] { 1024.0, 0.375, 8UL }, new object[] { 1024.0, 0.375, 10UL },
            new object[] { 1024.0, 0.25, 5UL } /* was Inconsistent */, new object[] { 16384.0, 0.375, 5UL },
            // the false crossing is the first crossing of the first traced edge (the first id after the vertices)
            new object[] { 1024.0, 0.25, 895UL },
            // the piece along the input edge runs between two crossings (was Inconsistent)
            new object[] { 16384.0, 0.375, 418UL } };

        [Theory]
        [MemberData(nameof(FalseCrossingCases))]
        public void FalseCrossing_IsReportedAsInconsistent(double a, double shear, ulong seed)
        {
            var T = Build(a, shear, seed, Similarity.Identity, out DMesh3 mesh);
            Assert.True(T.CheckConsistency(out string err), err);
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("false crossing", cs.Error);
        }

        [Fact]
        public void FalseCrossingAtTheTail_IsReportedAsInconsistent()
        {
            // A vertex inserted 9e-10 (barycentric) from an input edge counts as on that edge (snap 1e-9), but its
            // walks start off the edge: the trace of an intrinsic edge from it crosses the edge 4e-9 away, and the
            // piece from the vertex to that crossing runs along the edge. Success with a three-edged crossing
            // before the fix; found by the review of #1313.
            DMesh3 mesh = ShearedGrid(16.0, 1.625, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            T.GetTriangleLayout(9, out Vector2d q0, out Vector2d q1, out Vector2d q2);
            double d = 9.017260132893283e-10, s = 0.7905587757241721;
            int v = T.InsertVertex(9, (1 - d) * ((1 - s) * q2 + s * q0) + d * q1);
            Assert.True(v >= 0);
            Assert.True(T.FlipToDelaunay() > 0);
            Assert.True(T.CheckConsistency(out string err), err);
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("false crossing", cs.Error);
        }

        [Fact]
        public void Success_IsACellComplexWithFourEdgedCrossings()
        {
            // on these 80 inputs every result is either reported as Inconsistent or passes ComplexDefect; both occur
            int success = 0, falseCrossings = 0;
            foreach (double shear in new[] { 0.375, 0.25 })
                for (ulong seed = 1; seed <= 40; ++seed) {
                    var T = Build(1024.0, shear, seed, Similarity.Identity, out DMesh3 mesh);
                    var cs = T.ExtractCommonSubdivision();
                    if (cs.Status == CommonSubdivisionStatus.Success) {
                        success++;
                        string defect = ComplexDefect(mesh, T, cs);
                        Assert.True(defect == null, $"shear {shear}, seed {seed}: Success, but {defect}");
                        Assert.True(cs.MaxInputAreaError <= 1e-6 && cs.MaxIntrinsicAreaError <= 1e-6);
                    } else {
                        Assert.False(string.IsNullOrEmpty(cs.Error));
                        if (cs.Error.Contains("false crossing")) falseCrossings++;
                    }
                }
            Assert.True(success >= 20, $"only {success} of 80 inputs succeed");
            Assert.True(falseCrossings >= 5, $"only {falseCrossings} of 80 inputs have a false crossing");
        }

        [Theory]
        [InlineData(8UL)]       // a false crossing for the identity
        [InlineData(1UL)]       // Success for the identity
        public void Invariance_OverSimilarities(ulong seed)
        {
            var T0 = Build(1024.0, 0.375, seed, Similarity.Identity, out DMesh3 mesh0);
            var cs0 = T0.ExtractCommonSubdivision();
            Assert.Equal(seed == 1UL, cs0.Status == CommonSubdivisionStatus.Success);
            foreach (Similarity sim in Similarity.All()) {
                var T = Build(1024.0, 0.375, seed, sim, out DMesh3 mesh);
                var cs = T.ExtractCommonSubdivision();
                // rotations and translations change the rounding of the traces, so the status may differ; the
                // contract holds for each: reported, or a correct complex
                if (cs.Status == CommonSubdivisionStatus.Success) {
                    string defect = ComplexDefect(mesh, T, cs);
                    Assert.True(defect == null, $"{sim}: Success, but {defect}");
                } else
                    Assert.False(string.IsNullOrEmpty(cs.Error), sim.Name);
                if (sim.IsExact == false) continue;
                // scaling by a power of two: the same result bit for bit
                Assert.Equal(cs0.Status, cs.Status);
                Assert.Equal(cs0.Error, cs.Error);
                Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
                Assert.Equal(cs0.Points.Count, cs.Points.Count);
                for (int i = 0; i < cs.Points.Count; ++i)
                    Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
                for (int f = 0; f < cs.Faces.Count; ++f) {
                    Assert.Equal(cs0.Faces[f].Corners, cs.Faces[f].Corners);
                    Assert.Equal(cs0.Faces[f].IntrinsicTriangle, cs.Faces[f].IntrinsicTriangle);
                    Assert.Equal(cs0.Faces[f].InputTriangle, cs.Faces[f].InputTriangle);
                    Assert.Equal(cs0.Faces[f].Barycentric, cs.Faces[f].Barycentric);
                    Assert.Equal(cs0.Faces[f].Area * sim.Scale * sim.Scale, cs.Faces[f].Area);
                }
            }
        }

        [Theory]
        [InlineData(8UL)]
        [InlineData(1UL)]
        public void Reuse_SecondExtractionGivesTheSameResult(ulong seed)
        {
            var T = Build(1024.0, 0.375, seed, Similarity.Identity, out DMesh3 mesh);
            var a = T.ExtractCommonSubdivision();
            var b = T.ExtractCommonSubdivision();
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.Error, b.Error);
            Assert.Equal(a.Points, b.Points);
            Assert.Equal(a.Faces.Count, b.Faces.Count);
            for (int f = 0; f < a.Faces.Count; ++f) {
                Assert.Equal(a.Faces[f].Corners, b.Faces[f].Corners);
                Assert.Equal(a.Faces[f].IntrinsicTriangle, b.Faces[f].IntrinsicTriangle);
                Assert.Equal(a.Faces[f].InputTriangle, b.Faces[f].InputTriangle);
            }
        }
    }
}
