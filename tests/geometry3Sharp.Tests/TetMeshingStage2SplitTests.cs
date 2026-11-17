using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The edge splits and edge contractions of stage 2 (docs/design/TET_MESHING.md section 16): splits of interior edges and in-place
    /// splits of boundary edges with roll-back, contractions of an interior vertex into a neighbour under the link condition. They keep
    /// the mesh exactly valid, the boundary closed and around the input, and the boundary as a point set (splits) or unchanged
    /// (contractions).
    /// </summary>
    public class TetMeshingStage2SplitTests
    {
        // the stage-1 result (no improvement) and an improver on it, as MeshTetrahedralization sets it up
        static (MeshTetrahedralization, TetMeshImprover) Plain(DMesh3 input, double alpha = 20, double offset = 600)
        {
            var tm = new MeshTetrahedralization(input) { Improve = false, RelativeAlpha = alpha, RelativeOffset = offset };
            Assert.True(tm.Compute());
            var imp = new TetMeshImprover(tm.Result) { InputTree = new DMeshAABBTree3(input, true), BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset };
            return (tm, imp);
        }

        static DMesh3 Torus48() => TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);

        static void AssertValid(TetMesh3 r)
        {
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Null(TetOracle.Check(r, null));
            Assert.False(MeshRepairMetrics.HasSelfIntersections(r.ToSurfaceMesh()));
        }

        static double Area(TetMesh3 m) => m.BoundaryTriangles.Sum(f => new Triangle3d(m.Vertices[f.a], m.Vertices[f.b], m.Vertices[f.c]).Area);

        [Fact]
        public void Splits_LiftTheThinTorusToTenDegrees_AndKeepTheInvariants()
        {
            var off = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) { SplitEdges = false, ContractEdges = false };
            Assert.True(off.Compute());
            var on = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin"));
            Assert.True(on.Compute(), on.Status.ToString());
            TetMeshingStage2Tests.Invariants(on);
            double before = TetMeshQuality.Measure(off.Result).MinDihedral.Worst, after = TetMeshQuality.Measure(on.Result).MinDihedral.Worst;
            Assert.True(before < 10, before.ToString());                          // (9.7 measured: flips and smoothing alone miss it)
            Assert.True(after >= 10, after.ToString());
            Assert.True(on.Counts["improve_insertions"] > 0);
            Assert.Equal(0, off.Counts["improve_insertions"] + off.Counts["improve_contractions"]);
        }

        [Fact]
        public void ForcedSplits_KeepTheMeshValid_AndTheBoundaryAsAPointSet()
        {
            // every split that passes the exact checks is kept (no quality judgement, no smoothing): the mesh stays valid, boundary
            // splits put the new vertex at the edge's midpoint, and the boundary's area does not change (its triangles are split in place)
            var (tm, imp) = Plain(Torus48(), 12, 200);
            imp.IgnoreQualityForTest = true; imp.SmoothIterations = 0;
            TetMesh3 before = imp.Result();
            double area0 = Area(before);
            var boundary0 = new DMeshAABBTree3(before.ToSurfaceMesh(), true);
            int nb = 0, ni = 0;
            var edges = new List<(int, int)>();
            foreach (Index4i t in imp.AliveTets().Take(400))
                for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) edges.Add((t[i], t[j]));
            foreach (var (a, b) in edges.Distinct().Take(600)) {
                if (!imp.Neighbours(a).Contains(b)) continue;                         // split away already
                bool be = imp.IsBoundaryEdge(a, b);
                if (!imp.TrySplit(a, b)) continue;
                if (be) nb++; else ni++;
            }
            Assert.True(nb > 20 && ni > 20, nb + " / " + ni);
            Assert.Equal(nb + ni, imp.Counts["insertions"]);
            TetMesh3 r = imp.Result();
            AssertValid(r);
            Assert.Equal(before.Vertices.Count + nb + ni, r.Vertices.Count);
            Assert.Equal(before.BoundaryTriangles.Count + 2 * nb, r.BoundaryTriangles.Count);
            Assert.True(Math.Abs(Area(r) - area0) <= 1e-9 * area0, Area(r) + " vs " + area0);
            DMesh3 surf = boundary0.Mesh;
            foreach (Index3i f in r.BoundaryTriangles)
                foreach (Vector3d p in new[] { r.Vertices[f.a], r.Vertices[f.b], r.Vertices[f.c] }) {
                    int tid = boundary0.FindNearestTriangle(p);
                    double dd = Math.Sqrt(MeshQueries.TriDistanceSqr(surf, tid, p));
                    // (the squared-distance query loses about half the digits: 1e-7 offsets measured)
                    Assert.True(dd <= 1e-5 * tm.UsedOffset, dd / tm.UsedOffset + " offsets");
                }
        }

        [Fact]
        public void BoundarySplit_IsRefused_WhereTheSplitRegionTouchesTheInput()
        {
            // an input triangle through the midpoint of a boundary edge (across it): the in-place split's region (the flat tet of the
            // old triangle and the midpoint) touches it, so the split is refused; without that triangle the same split is allowed
            var (tm, imp) = Plain(Torus48(), 12, 200);
            imp.IgnoreQualityForTest = true; imp.SmoothIterations = 0;
            // the improver's vertex ids are those of the stage-1 result
            TetMesh3 src = tm.Result;
            Index3i g = src.BoundaryTriangles[0];
            int a = g.a, b = g.b;
            Vector3d pa = src.Vertices[a], pb = src.Vertices[b], m = 0.5 * (pa + pb);
            Vector3d d = (pb - pa).Normalized, n = Vector3d.AxisZ.Cross(d);
            if (n.Length < 0.1) n = Vector3d.AxisX.Cross(d);
            n.Normalize();
            Vector3d k = d.Cross(n);
            double h = 1e-3 * pa.Distance(pb);
            DMesh3 input = new DMesh3(tm.Mesh);
            int v0 = input.AppendVertex(m + h * n), v1 = input.AppendVertex(m - h * n + h * k), v2 = input.AppendVertex(m - h * n - h * k);
            input.AppendTriangle(v0, v1, v2);
            var imp2 = new TetMeshImprover(src) { InputTree = new DMeshAABBTree3(input, true), BoundarySlideLimit = imp.BoundarySlideLimit, IgnoreQualityForTest = true, SmoothIterations = 0 };
            Assert.True(imp2.IsBoundaryEdge(a, b));
            Assert.False(imp2.TrySplit(a, b));
            Assert.Equal(1, imp2.Counts["split_refused_input"]);
            Assert.True(imp.TrySplit(a, b));
            Assert.Equal(0, imp.Counts["split_refused_input"]);
        }

        [Fact]
        public void Split_IsRefused_ForANegativeTet_OrABoundaryTriangleThatCutsAnother()
        {
            // two separate tets 0.1 apart (two boundary components), a far input. The split point is moved off the edge (a test hook;
            // the midpoint is off only by rounding): beyond the far face, a split tet is negative; into the second tet, the new
            // boundary triangles cut its boundary. Both refused, whatever the quality; on the edge itself the split is kept
            var v = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),
                new Vector3d(1.1, 0, 0), new Vector3d(2.1, 0, 0), new Vector3d(1.1, 1, 0), new Vector3d(1.1, 0, 1) };
            TetMesh3 m = TetMeshingStage2Tests.FromTets(v, new List<Index4i> { new Index4i(0, 1, 2, 3), new Index4i(4, 5, 6, 7) });
            var far = new DMesh3(); far.AppendVertex(new Vector3d(10, 10, 10)); far.AppendVertex(new Vector3d(11, 10, 10)); far.AppendVertex(new Vector3d(10, 11, 10));
            far.AppendTriangle(0, 1, 2);
            Func<Func<Vector3d, Vector3d, Vector3d>, TetMeshImprover> make = (f) => new TetMeshImprover(m) {
                InputTree = new DMeshAABBTree3(far, true), BoundarySlideLimit = 10, IgnoreQualityForTest = true, SmoothIterations = 0, SplitPointForTest = f };
            // edge (1, 2): the half (0, m, 2, 3) is positive only for m.x > 0
            var neg = make((a, b) => new Vector3d(-0.2, 0.6, 0.3));
            Assert.False(neg.TrySplit(1, 2));
            // edge (1, 3), split point pushed in x into the second tet (x > 1.1): both halves positive (m.x, m.z > 0), but the new
            // boundary triangles cut the second tet's faces
            var cut = make((a, b) => new Vector3d(1.3, 0.05, 0.45));
            Assert.False(cut.TrySplit(1, 3));
            var ok = make(null);
            Assert.True(ok.TrySplit(1, 3));
            Assert.Null(TetMeshQuality.NotValidReason(ok.Result()));
        }

        [Fact]
        public void RefusedSplits_LeaveTheMeshBitForBit()
        {
            // with the quality judgement: a split that does not raise its neighbourhood's worst angle is undone completely
            var (tm, imp) = Plain(Torus48(), 12, 200);
            int refused = 0, kept = 0;
            var tets = imp.AliveTets();
            foreach (Index4i t in tets.Take(300)) {
                int a = t.a, b = t.b;
                if (!imp.Neighbours(a).Contains(b)) continue;
                TetMesh3 snap = imp.Result();
                string state = imp.StateForTest();
                if (imp.TrySplit(a, b)) { kept++; continue; }
                refused++;
                Assert.Equal(state, imp.StateForTest());          // the lists in their order, the free ids: later sums run over them
                TetMesh3 now = imp.Result();
                Assert.Equal(snap.Vertices, now.Vertices); Assert.Equal(snap.Tets, now.Tets); Assert.Equal(snap.BoundaryTriangles, now.BoundaryTriangles);
            }
            Assert.True(refused > 50 && kept > 0, refused + " / " + kept);
            AssertValid(imp.Result());
        }

        // the merge of u into w, combinatorially: valid iff no tet appears twice and every triangle is in at most two tets, with the
        // boundary triangles (in one tet) the same as before
        static bool MergeIsAComplex(List<Index4i> tets, int u, int w)
        {
            Func<Index4i, List<Index3i>> faces = (t) => Enumerable.Range(0, 4).Select(i => new Index3i(t[Delaunay3.FacetIndices[i, 0]], t[Delaunay3.FacetIndices[i, 1]], t[Delaunay3.FacetIndices[i, 2]])).ToList();
            Func<Index3i, (int, int, int)> key = (f) => { var s = new[] { f.a, f.b, f.c }; Array.Sort(s); return (s[0], s[1], s[2]); };
            Func<IEnumerable<Index4i>, (HashSet<(int, int, int, int)>, Dictionary<(int, int, int), int>, bool)> census = (ts) => {
                var tk = new HashSet<(int, int, int, int)>(); var fc = new Dictionary<(int, int, int), int>(); bool dup = false;
                foreach (Index4i t in ts) {
                    var s = new[] { t.a, t.b, t.c, t.d }; Array.Sort(s);
                    if (!tk.Add((s[0], s[1], s[2], s[3]))) dup = true;
                    foreach (var f in faces(t)) { var k = key(f); fc[k] = (fc.TryGetValue(k, out int c) ? c : 0) + 1; }
                }
                return (tk, fc, dup);
            };
            // only the tets at u, w and their neighbours matter (the merge changes nothing else; the face counts and vertex links
            // near the merge are complete over these)
            var region = new HashSet<int> { u, w };
            foreach (Index4i t in tets) if (t.a == u || t.b == u || t.c == u || t.d == u || t.a == w || t.b == w || t.c == w || t.d == w) for (int i = 0; i < 4; ++i) region.Add(t[i]);
            tets = tets.Where(t => region.Contains(t.a) || region.Contains(t.b) || region.Contains(t.c) || region.Contains(t.d)).ToList();
            var merged = new List<Index4i>();
            foreach (Index4i t in tets) {
                bool hu = t.a == u || t.b == u || t.c == u || t.d == u, hw = t.a == w || t.b == w || t.c == w || t.d == w;
                if (hu && hw) continue;
                Index4i n = t; for (int i = 0; i < 4; ++i) if (n[i] == u) n[i] = w;
                merged.Add(n);
            }
            var (_, f0, _) = census(tets);
            var (_, f1, dup1) = census(merged);
            if (dup1 || f1.Values.Any(c => c > 2)) return false;
            // boundary triangles: those in one tet, among the faces of the tets at u or w (the region's own rim is not boundary)
            Func<Index4i, bool> atUW = (t) => t.a == u || t.b == u || t.c == u || t.d == u || t.a == w || t.b == w || t.c == w || t.d == w;
            var starFaces = new HashSet<(int, int, int)>(tets.Where(atUW).SelectMany(t => faces(t)).Select(key));
            var b0 = new HashSet<(int, int, int)>(f0.Where(kv => kv.Value == 1 && starFaces.Contains(kv.Key)).Select(kv => kv.Key));
            var b1 = new HashSet<(int, int, int)>(f1.Where(kv => kv.Value == 1 && starFaces.Contains(kv.Key)).Select(kv => kv.Key));
            if (!b0.SetEquals(b1)) return false;
            // a 3-manifold with boundary: the link of every vertex near the merge is a connected 2-sphere or disk (Euler 2 or 1,
            // every link edge in one or two link triangles)
            var near = new HashSet<int> { w };
            foreach (Index4i t in merged) if (t.a == w || t.b == w || t.c == w || t.d == w) for (int i = 0; i < 4; ++i) near.Add(t[i]);
            foreach (int x in near) if (!LinkIsSphereOrDisk(merged, x)) return false;
            return true;
        }

        static bool LinkIsSphereOrDisk(List<Index4i> tets, int x)
        {
            var tris = new List<int[]>();
            foreach (Index4i t in tets) {
                if (!(t.a == x || t.b == x || t.c == x || t.d == x)) continue;
                tris.Add(new[] { t.a, t.b, t.c, t.d }.Where(y => y != x).OrderBy(y => y).ToArray());
            }
            if (tris.Count == 0) return true;
            var edgeCount = new Dictionary<(int, int), int>();
            var verts = new HashSet<int>();
            foreach (var tr in tris) {
                foreach (int y in tr) verts.Add(y);
                foreach (var e in new[] { (tr[0], tr[1]), (tr[1], tr[2]), (tr[0], tr[2]) }) edgeCount[e] = (edgeCount.TryGetValue(e, out int c) ? c : 0) + 1;
            }
            if (edgeCount.Values.Any(c => c > 2)) return false;
            int chi = verts.Count - edgeCount.Count + tris.Count;
            bool hasBoundary = edgeCount.Values.Any(c => c == 1);
            if (chi != (hasBoundary ? 1 : 2)) return false;
            var seen = new HashSet<int> { 0 };
            var stack = new Stack<int>(); stack.Push(0);
            while (stack.Count > 0) {
                int i = stack.Pop();
                for (int j = 0; j < tris.Count; ++j)
                    if (!seen.Contains(j) && tris[i].Intersect(tris[j]).Count() == 2) { seen.Add(j); stack.Push(j); }
            }
            return seen.Count == tris.Count;
        }

        [Fact]
        public void LinkCondition_AgreesWithABruteForceMerge()
        {
            // for interior vertices u and each neighbour w: the link condition holds exactly when merging u into w gives a complex
            // (no doubled tet or triangle, the same boundary). Edges of the stage-1 result, plus a mesh made hostile by forced splits
            var (tm, imp) = Plain(Torus48(), 12, 200);
            int yes = 0, no = 0;
            var noPairs = new List<(int, int)>();
            for (int round = 0; round < 2; ++round) {
                List<Index4i> tets = imp.AliveTets();
                for (int u = 0; u < imp.VertexCount && yes + no < 1500 * (round + 1); ++u) {
                    if (imp.IsBoundaryVertex(u)) continue;
                    foreach (int w in imp.Neighbours(u)) {
                        bool link = imp.LinkCondition(u, w);
                        Assert.Equal(MergeIsAComplex(tets, u, w), link);
                        if (link) yes++; else { no++; noPairs.Add((u, w)); }
                    }
                }
                imp.IgnoreQualityForTest = true; imp.SmoothIterations = 0;
                foreach (Index4i t in tets.Take(200)) if (imp.Neighbours(t.a).Contains(t.c)) imp.TrySplit(t.a, t.c);
                imp.IgnoreQualityForTest = false;
            }
            Assert.True(yes > 100 && no > 100, yes + " / " + no);
            // and the contraction refuses those, whatever the quality (the last round's pairs: the mesh has not changed since)
            imp.IgnoreQualityForTest = true;
            int checkedPairs = 0;
            foreach (var (u, w) in noPairs.Skip(noPairs.Count / 2)) { Assert.False(imp.TryContract(u, w)); checkedPairs++; }
            Assert.True(checkedPairs > 50);
        }

        [Fact]
        public void ForcedContractions_KeepTheMeshValid_AndTheBoundaryUnchanged()
        {
            var (tm, imp) = Plain(Torus48(), 12, 200);
            imp.IgnoreQualityForTest = true;
            TetMesh3 before = imp.Result();
            var bset = new HashSet<(Vector3d, Vector3d, Vector3d)>(before.BoundaryTriangles.Select(f => Key(before.Vertices, f)));
            int n = 0, refusedBoundary = 0;
            for (int u = 0; u < imp.VertexCount && n < 150; ++u) {
                foreach (int w in imp.Neighbours(u)) {
                    if (imp.IsBoundaryVertex(u)) { if (!imp.TryContract(u, w)) refusedBoundary++; break; }
                    if (imp.TryContract(u, w)) { n++; break; }
                }
            }
            Assert.True(n > 20 && refusedBoundary > 50, n + " / " + refusedBoundary);
            Assert.Equal(n, imp.Counts["contractions"]);
            TetMesh3 r = imp.Result();
            AssertValid(r);
            Assert.True(bset.SetEquals(r.BoundaryTriangles.Select(f => Key(r.Vertices, f))));
            Assert.Equal(before.Vertices.Count - n, r.Vertices.Count);
        }

        [Fact]
        public void Contraction_WithTheQualityJudgement_RaisesTheWorstAngleOfTheChangedTets()
        {
            // the stage-1 result has interior vertices close to others: some contractions improve, and every kept one leaves the
            // mesh's worst angle no lower (it raises the worst angle of the tets it changes)
            var (tm, imp) = Plain(TetMeshingStage2Tests.Input("torus thin"));
            double worst = TetMeshQuality.Measure(imp.Result()).MinDihedral.Worst;
            int n = 0;
            for (int u = 0; u < imp.VertexCount; ++u) {
                if (imp.IsBoundaryVertex(u)) continue;
                foreach (int w in imp.Neighbours(u))
                    if (imp.TryContract(u, w)) {
                        n++;
                        double now = TetMeshQuality.Measure(imp.Result()).MinDihedral.Worst;
                        Assert.True(now >= worst, now + " < " + worst);
                        worst = now;
                        break;
                    }
            }
            Assert.True(n > 0);
            AssertValid(imp.Result());
        }

        static (Vector3d, Vector3d, Vector3d) Key(List<Vector3d> vs, Index3i f)
        {
            Vector3d a = vs[f.a], b = vs[f.b], c = vs[f.c];
            // the rotation that starts at the smallest corner (lexicographic)
            Func<Vector3d, Vector3d, int> cmp = (x, y) => x.x != y.x ? x.x.CompareTo(y.x) : x.y != y.y ? x.y.CompareTo(y.y) : x.z.CompareTo(y.z);
            if (cmp(b, a) < 0 && cmp(b, c) < 0) return (b, c, a);
            if (cmp(c, a) < 0 && cmp(c, b) < 0) return (c, a, b);
            return (a, b, c);
        }

        [Fact]
        public void Diagnosis_ChangesNothing_AndListsEveryTetLeft()
        {
            var a = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) { SplitEdges = false, ContractEdges = false };
            Assert.True(a.Compute());
            var b = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) { SplitEdges = false, ContractEdges = false, DiagnoseImprovement = true };
            Assert.True(b.Compute());
            Assert.Equal(a.Result.Vertices, b.Result.Vertices); Assert.Equal(a.Result.Tets, b.Result.Tets);
            int left = a.Result.Tets.Count(t => TetMeshImprover.Quality(a.Result.Vertices[t.a], a.Result.Vertices[t.b], a.Result.Vertices[t.c], a.Result.Vertices[t.d]) < 10);
            Assert.True(left > 0);
            Assert.Equal(left, b.DiagnosticLines.Count);
            Assert.Equal(left, b.Counts.Where(kv => kv.Key.StartsWith("improve_diag_touch_")).Sum(kv => kv.Value));
            Assert.Equal(left, b.Counts.Where(kv => kv.Key.StartsWith("improve_diag_shape_")).Sum(kv => kv.Value));
            // the counts of the improvement itself are those of the run without the diagnosis
            foreach (var kv in a.Counts) Assert.Equal(kv.Value, b.Counts[kv.Key]);
        }

        [Fact]
        public void Diagnosis_WithContraction_ChangesNothing()
        {
            Func<bool, MeshTetrahedralization> run = (d) => {
                // one pass, so that needles and wedges are left (their short edges get the dry contraction)
                var m = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")) { SplitEdges = false, ImprovePasses = 1, DiagnoseImprovement = d };
                Assert.True(m.Compute()); return m;
            };
            MeshTetrahedralization a = run(false), b = run(true);
            Assert.Equal(a.Result.Vertices, b.Result.Vertices); Assert.Equal(a.Result.Tets, b.Result.Tets);
            foreach (var kv in a.Counts) Assert.Equal(kv.Value, b.Counts[kv.Key]);
            Assert.True(b.Counts.Keys.Any(k => k.StartsWith("improve_diag_contract_")), string.Join(" ", b.Counts.Keys.Where(k => k.Contains("diag"))));
            Assert.Null(a.DiagnosticLines);
            Assert.True(b.Compute() && b.DiagnosticLines != null);
            b.DiagnoseImprovement = false;
            Assert.True(b.Compute());
            Assert.Null(b.DiagnosticLines);                                       // not left over from the run before
        }

        [Fact]
        public void Shape_ClassifiesTheFourKinds()
        {
            // Cheng et al. 2000, fig. 1: sliver (a flattened regular tet), cap (a vertex just above the opposite face's centre),
            // wedge (a short edge, two small angles), needle (a short edge, otherwise fine-ish)
            Assert.Equal("sliver", TetMeshImprover.Shape(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0.02), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0.02)));
            Assert.Equal("cap", TetMeshImprover.Shape(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, 0.9, 0), new Vector3d(0.5, 0.3, 0.02)));
            Assert.Equal("wedge", TetMeshImprover.Shape(new Vector3d(0, 0, 0), new Vector3d(0.1, 0, 0), new Vector3d(0.05, 1, 0), new Vector3d(0.05, 1, 0.05)));
            Assert.Equal("needle", TetMeshImprover.Shape(new Vector3d(0, 0, 0), new Vector3d(0.1, 0, 0), new Vector3d(0, 0.1, 0), new Vector3d(0.03, 0.03, 1)));
        }
    }
}
