using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Wrong Success results of IntrinsicTriangulation.ExtractCommonSubdivision that the area checks do not show.
    /// #1313, false crossings: a trace that crosses an input edge of its own end vertex, just outside the snap
    /// tolerance, was taken as running along that edge from the crossing on (a subdivision vertex of three edges).
    /// #1326, lost faces: where crossings coincide up to rounding, the outer cycle of an input triangle took sliver
    /// faces with it (edges of the result with a face on one side only). Both found with
    /// benchmarks/IntrinsicIntegerCoordinates and the reviews; the cases here are its sheared stretched grid, small,
    /// with vertices inserted through the public API. Mesh and points use only +, * and a division by 8, so the
    /// inputs are the same bits on every platform.
    /// </summary>
    public class IntrinsicCommonSubdivisionConsistencyTests
    {
        const int GridCells = 3;

        // (GridCells + 1)^2 vertices at (a * (i + shear * j), j): cells stretched by a and sheared, so the Delaunay
        // flips make long thin intrinsic triangles
        static DMesh3 ShearedGrid(double a, double shear, Similarity sim, bool reverseTriangles = false)
        {
            var mesh = new DMesh3();
            int n = GridCells;
            int[,] vid = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    vid[i, j] = mesh.AppendVertex(sim.Apply(new Vector3d(a * (i + shear * j), j, 0)));
            var tris = new List<Index3i>();
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    tris.Add(new Index3i(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]));
                    tris.Add(new Index3i(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]));
                }
            if (reverseTriangles) tris.Reverse();
            foreach (Index3i t in tris) mesh.AppendTriangle(t);
            return mesh;
        }

        // One vertex inserted into triangle t of the unflipped grid, at the barycentric distance d from its edge
        // from corner 2 to corner 0 (parameter s along it), then flipped to Delaunay: the vertex counts as on the
        // input edge (snap 1e-9) or lies just beside it
        static IntrinsicTriangulation BuildNearEdge(double a, double shear, bool reverseTriangles, int t, double d, double s,
            Similarity sim, out DMesh3 mesh)
        {
            mesh = ShearedGrid(a, shear, sim, reverseTriangles);
            var T = new IntrinsicTriangulation(mesh);
            T.GetTriangleLayout(t, out Vector2d q0, out Vector2d q1, out Vector2d q2);
            Assert.True(T.InsertVertex(t, (1 - d) * ((1 - s) * q2 + s * q0) + d * q1) >= 0);
            Assert.True(T.FlipToDelaunay() > 0);
            Assert.True(T.CheckConsistency(out string err), err);
            return T;
        }

        // #1326: the inserted vertex lies 1.4e-9 beside an input edge, and two traces cross that edge 2e-16 apart
        // next to it. The order of the arrangement edges around these coincident points joined the outer cycle of
        // the input triangle with sliver faces: Success without them (four edges of the result with a face on one
        // side only, 16 one-sided edges where the input has 12), the areas agreeing to 8e-10. Found by the review
        // of #1313.
        static IntrinsicTriangulation BuildUnmatched(Similarity sim, out DMesh3 mesh) =>
            BuildNearEdge(2.0, -1.25, true, 6, 1.4229630544726813e-09, 0.566618744836477, sim, out mesh);

        // (a, shear, reversed triangle order, triangle, d, s): Success with one-sided edges inside before the fix
        public static IEnumerable<object[]> MissingFaceCases() => new[] {
            new object[] { 2.0, -1.25, true, 6, 1.4229630544726813e-09, 0.566618744836477 },
            new object[] { 1.0, 1.625, true, 6, 1.173030251315181E-09, 0.6282472738033802 },
            new object[] { 0.0625, 1.375, false, 17, 3.739849251355159E-09, 0.8715104458657218 } };

        [Theory]
        [MemberData(nameof(MissingFaceCases))]
        public void FacesMissingAtCoincidentCrossings_AreReportedAsInconsistent(double a, double shear, bool rev, int t, double d, double s)
        {
            var T = BuildNearEdge(a, shear, rev, t, d, s, Similarity.Identity, out DMesh3 mesh);
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("not a cycle of the arrangement", cs.Error);
            Assert.Empty(cs.Faces);
        }

        // an inside cycle of the arrangement with area exactly 0 (two coincident crossings and a vertex): not a face.
        // Inconsistent before the fix too (as a second outer cycle); pinned for the rule that tells the outer cycle
        // by its darts and no longer by its area
        [Theory]
        [InlineData(0.125, -1.25, false, 2, 3.453532734510681E-09, 0.13303328999507147)]
        [InlineData(16.0, 0.25, true, 7, 2.340538376399827E-09, 0.7759003422941299)]
        [InlineData(1.0, 1.25, false, 10, 1.044115342241211E-09, 0.8114711726090286)]
        public void FaceWithoutArea_IsReportedAsInconsistent(double a, double shear, bool rev, int t, double d, double s)
        {
            var T = BuildNearEdge(a, shear, rev, t, d, s, Similarity.Identity, out DMesh3 mesh);
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("face without area", cs.Error);
        }

        // The squared lengths and areas overflow from about 2^256 on; before the unit scale of #1377 the area errors were NaN there, and a NaN
        // passed both area checks (`err > 1e-6` is false for NaN): Success with wrong faces (#1352). Now the triangulation computes in
        // unit scale: Success up to 2^512, and from 2^513 on the face areas in the units of the mesh overflow: Inconsistent.
        [Theory]
        [InlineData(260, true)]
        [InlineData(300, true)]
        [InlineData(400, true)]
        [InlineData(500, true)]
        [InlineData(512, true)]
        [InlineData(700, false)]
        [InlineData(1000, false)]
        public void AreaChecks_AtOverflowingScales_AreSuccessOrInconsistent_NeverWrong(int exponent, bool success)
        {
            DMesh3 mesh = ShearedGrid(1.0, 0.25, Similarity.Identity);
            double scale = Math.Pow(2, exponent);
            foreach (int vid in mesh.VertexIndices()) mesh.SetVertex(vid, mesh.GetVertex(vid) * scale);
            var T = new IntrinsicTriangulation(mesh);
            T.FlipToDelaunay();
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(success ? CommonSubdivisionStatus.Success : CommonSubdivisionStatus.Inconsistent, cs.Status);
            if (success) Assert.All(cs.Faces, f => Assert.True(f.Area > 0 && !double.IsInfinity(f.Area)));
            else Assert.False(string.IsNullOrEmpty(cs.Error));
        }

        [Fact]
        public void AreaChecks_StillSucceed_OnTheSameMeshAtUnitScale()
        {
            var T = new IntrinsicTriangulation(ShearedGrid(1.0, 0.25, Similarity.Identity));
            T.FlipToDelaunay();
            Assert.Equal(CommonSubdivisionStatus.Success, T.ExtractCommonSubdivision().Status);
        }

        [Fact]
        public void FacesMissingAtCoincidentCrossings_Invariance_OverSimilarities()
        {
            var cs0 = BuildUnmatched(Similarity.Identity, out DMesh3 mesh0).ExtractCommonSubdivision();
            foreach (Similarity sim in Similarity.All()) {
                var T = BuildUnmatched(sim, out DMesh3 mesh);
                var cs = T.ExtractCommonSubdivision();
                // reported, or a correct complex (rotations and translations change the rounding)
                if (cs.Status == CommonSubdivisionStatus.Success) {
                    string defect = ComplexDefect(mesh, T, cs);
                    Assert.True(defect == null, $"{sim}: Success, but {defect}");
                } else
                    Assert.False(string.IsNullOrEmpty(cs.Error), sim.Name);
                if (sim.IsExact == false) continue;
                Assert.Equal(cs0.Status, cs.Status);
                Assert.Equal(cs0.Error, cs.Error);
                Assert.Equal(cs0.Points.Count, cs.Points.Count);
                for (int i = 0; i < cs.Points.Count; ++i)
                    Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
            }
        }

        [Fact]
        public void FacesMissingAtCoincidentCrossings_Reuse()
        {
            var T = BuildUnmatched(Similarity.Identity, out DMesh3 mesh);
            var a = T.ExtractCommonSubdivision();
            var b = T.ExtractCommonSubdivision();
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.Error, b.Error);
            Assert.Equal(a.Points, b.Points);
        }

        [Fact]
        public void NearEdgeInsertions_SuccessIsACellComplex()
        {
            // vertices inserted 1e-12 to 6e-9 beside the input edge from corner 2 to corner 0 of a triangle (the kind
            // of input on which the tail side of #1313 and #1326 were found; these 600 contain no #1326 case, so this
            // is the contract on near-edge input, not a regression test): every result is reported as Inconsistent or
            // passes ComplexDefect; both occur. (A vertex within the snap of an input boundary edge is not covered:
            // none of these succeeds, and the boundary count of ComplexDefect assumes none.)
            ulong x = 20261122UL;
            Func<double> rnd = () => {
                x = unchecked(x * 6364136223846793005UL + 1442695040888963407UL);
                return (x >> 11) * (1.0 / 9007199254740992.0);
            };
            double[] deltas = { 1e-12, 1e-11, 1e-10, 3e-10, 9e-10, 1.5e-9, 3e-9 };
            double[] stretches = { 0.0625, 0.125, 0.25, 0.5, 1, 2, 4, 8, 16 };
            int success = 0, reported = 0;
            for (int trial = 0; trial < 600; ++trial) {
                double a = stretches[(int)(rnd() * stretches.Length)];
                double shear = ((int)(rnd() * 33) - 16) / 8.0;              // -2 .. 2 in eighths
                double d = deltas[(int)(rnd() * deltas.Length)] * (1 + rnd());
                double s = 0.0625 + 0.875 * rnd();
                int t = (int)(rnd() * 18);
                bool rev = rnd() < 0.5;
                DMesh3 mesh = ShearedGrid(a, shear, Similarity.Identity, rev);
                var T = new IntrinsicTriangulation(mesh);
                T.GetTriangleLayout(t, out Vector2d q0, out Vector2d q1, out Vector2d q2);
                if (T.InsertVertex(t, (1 - d) * ((1 - s) * q2 + s * q0) + d * q1) < 0) continue;
                T.FlipToDelaunay();
                var cs = T.ExtractCommonSubdivision();
                if (cs.Status == CommonSubdivisionStatus.Success) {
                    success++;
                    string defect = ComplexDefect(mesh, T, cs);
                    Assert.True(defect == null, $"trial {trial} (a {a}, shear {shear}, d {d:R}, s {s:R}, t {t}, rev {rev}): Success, but {defect}");
                } else {
                    reported++;
                    Assert.False(string.IsNullOrEmpty(cs.Error));
                }
            }
            Assert.True(success >= 100, $"only {success} of 600 inputs succeed");
            Assert.True(reported >= 300, $"only {reported} of 600 inputs are reported");
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

        // #1335. One vertex inserted into triangle t of the unflipped grid at the barycentric distance d from its
        // edge from corner e to corner e + 1 (parameter s along it); not flipped yet
        static IntrinsicTriangulation BuildNearEdgeOf(double a, double shear, int t, int e, double d, double s, Similarity sim,
            out DMesh3 mesh)
        {
            mesh = ShearedGrid(a, shear, sim);
            var T = new IntrinsicTriangulation(mesh);
            var q = new Vector2d[3];
            T.GetTriangleLayout(t, out q[0], out q[1], out q[2]);
            Assert.True(T.InsertVertex(t, (1 - d) * ((1 - s) * q[e] + s * q[(e + 1) % 3]) + d * q[(e + 2) % 3]) >= 0);
            return T;
        }

        // #1335, item 1: the vertex lies 5e-10 beside the input boundary edge of triangle 0 and counts as on it
        // (snap 1e-9). The intrinsic triangle between it and that edge has the area 0 from its lengths (it passes
        // the area check with no face) and its three sides run along the input edge: Success without a face of
        // that triangle before the fix. (CheckConsistency names the triangle as degenerate; the extraction has to
        // report it on its own.)
        static IntrinsicTriangulation BuildBoundarySliver(Similarity sim, out DMesh3 mesh)
        {
            var T = BuildNearEdgeOf(1.0, -1.0, 0, 0, 5e-10, 0.4, sim, out mesh);
            Assert.True(T.FlipToDelaunay() > 0);
            return T;
        }

        static int TrianglesWithoutFace(IntrinsicTriangulation T, IntrinsicCommonSubdivision cs)
        {
            var has = new bool[T.TriangleCount];
            foreach (var f in cs.Faces) has[f.IntrinsicTriangle] = true;
            return has.Count(x => x == false);
        }

        [Fact]
        public void SliverAtInputBoundary_WithoutFace_IsReportedAsInconsistent()
        {
            var T = BuildBoundarySliver(Similarity.Identity, out DMesh3 mesh);
            Assert.Equal(1, Enumerable.Range(0, T.TriangleCount).Count(t => T.TriangleArea(t) == 0));
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("has no face", cs.Error);
            Assert.Empty(cs.Faces);
        }

        [Fact]
        public void SliverAtInputBoundary_Invariance_OverSimilarities()
        {
            var cs0 = BuildBoundarySliver(Similarity.Identity, out DMesh3 mesh0).ExtractCommonSubdivision();
            foreach (Similarity sim in Similarity.All()) {
                var T = BuildBoundarySliver(sim, out DMesh3 mesh);
                var cs = T.ExtractCommonSubdivision();
                // reported without faces, or every intrinsic triangle has a face (rotations and translations change
                // the rounding: the vertex may then lie outside the snap, and the sliver have an area)
                if (cs.Status == CommonSubdivisionStatus.Success) {
                    Assert.True(TrianglesWithoutFace(T, cs) == 0, $"{sim}: Success, but an intrinsic triangle has no face");
                    string defect = ComplexDefect(mesh, T, cs);
                    Assert.True(defect == null, $"{sim}: Success, but {defect}");
                } else {
                    Assert.False(string.IsNullOrEmpty(cs.Error), sim.Name);
                    Assert.Empty(cs.Faces);
                }
                if (sim.IsExact == false) continue;
                Assert.Equal(cs0.Status, cs.Status);
                Assert.Equal(cs0.Error, cs.Error);
                Assert.Equal(cs0.Points.Count, cs.Points.Count);
                for (int i = 0; i < cs.Points.Count; ++i)
                    Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
            }
        }

        [Fact]
        public void NearBoundaryInsertions_SuccessHasAFaceInEveryIntrinsicTriangle()
        {
            // vertices inserted 1e-12 to 6e-9 beside an input boundary edge of the grid (the bottom row, the edge
            // from corner 0 to corner 1 of its even triangles): every result is reported as Inconsistent without
            // faces, or every intrinsic triangle has a face; both occur (14 and 286 of the 300, 151 of them for a
            // triangle without a face: those were Success without the face of the sliver before the fix).
            ulong x = 1335UL;
            Func<double> rnd = () => {
                x = unchecked(x * 6364136223846793005UL + 1442695040888963407UL);
                return (x >> 11) * (1.0 / 9007199254740992.0);
            };
            double[] deltas = { 1e-12, 1e-11, 1e-10, 3e-10, 9e-10, 1.5e-9, 3e-9 };
            double[] stretches = { 0.0625, 0.125, 0.25, 0.5, 1, 2, 4, 8, 16 };
            int success = 0, noFace = 0;
            for (int trial = 0; trial < 300; ++trial) {
                double a = stretches[(int)(rnd() * stretches.Length)];
                double shear = ((int)(rnd() * 33) - 16) / 8.0;
                double d = deltas[(int)(rnd() * deltas.Length)] * (1 + rnd());
                double s = 0.0625 + 0.875 * rnd();
                int t = 2 * (int)(rnd() * GridCells);
                var T = BuildNearEdgeOf(a, shear, t, 0, d, s, Similarity.Identity, out DMesh3 mesh);
                T.FlipToDelaunay();
                var cs = T.ExtractCommonSubdivision();
                if (cs.Status == CommonSubdivisionStatus.Success) {
                    success++;
                    Assert.True(TrianglesWithoutFace(T, cs) == 0, $"trial {trial} (a {a}, shear {shear}, d {d:R}, s {s:R}, t {t}): Success, but an intrinsic triangle has no face");
                    // (a Success has the vertex off the boundary edge, so the boundary count of ComplexDefect holds)
                    string defect = ComplexDefect(mesh, T, cs);
                    Assert.True(defect == null, $"trial {trial}: Success, but {defect}");
                } else {
                    Assert.Empty(cs.Faces);
                    if (cs.Error.Contains("has no face")) noFace++;
                }
            }
            Assert.True(success >= 8, $"only {success} of 300 inputs succeed");
            Assert.True(noFace >= 100, $"only {noFace} of 300 inputs have a triangle without a face");
        }

        // #1335, item 2: (a, shear, triangle, corner e, d, s) on which the last check, the intrinsic areas, fails:
        // Inconsistent with all the faces in Faces before the fix
        public static IEnumerable<object[]> IntrinsicAreaCases() => new[] {
            new object[] { 0.0625, -0.75, 4, 0, 5.914687293044002E-09, 0.8195240137495864 },
            new object[] { 2.0, -1.5, 0, 0, 1.4082562921870012E-07, 0.7015703587535858 },
            new object[] { 1024.0, -0.5, 3, 1, 1.1559745329350519E-09, 0.6787513866511119 } };

        [Theory]
        [MemberData(nameof(IntrinsicAreaCases))]
        public void Inconsistent_AtTheLastCheck_HasNoFaces(double a, double shear, int t, int e, double d, double s)
        {
            var T = BuildNearEdgeOf(a, shear, t, e, d, s, Similarity.Identity, out DMesh3 mesh);
            T.FlipToDelaunay();
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("do not sum to the intrinsic triangle areas", cs.Error);
            Assert.Empty(cs.Faces);
            Assert.True(cs.MaxIntrinsicAreaError > 1e-6);
        }

        [Theory]
        [MemberData(nameof(IntrinsicAreaCases))]
        public void Reuse_FailingExtractionAfterASuccessfulOne_HasNoFaces(double a, double shear, int t, int e, double d, double s)
        {
            // one instance: Success before the insertion, Inconsistent (at the last check) after it, twice the same
            DMesh3 mesh = ShearedGrid(a, shear, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            var first = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, first.Status);
            Assert.Null(ComplexDefect(mesh, T, first));
            Assert.Equal(T.TriangleCount, first.Faces.Count);
            var q = new Vector2d[3];
            T.GetTriangleLayout(t, out q[0], out q[1], out q[2]);
            Assert.True(T.InsertVertex(t, (1 - d) * ((1 - s) * q[e] + s * q[(e + 1) % 3]) + d * q[(e + 2) % 3]) >= 0);
            T.FlipToDelaunay();
            var second = T.ExtractCommonSubdivision();
            var third = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, second.Status);
            Assert.Empty(second.Faces);
            Assert.Equal(second.Status, third.Status);
            Assert.Equal(second.Error, third.Error);
            Assert.Equal(second.Points, third.Points);
            Assert.Empty(third.Faces);
        }
    }
}
