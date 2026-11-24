using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.PairCoincidentVertices: the vertices of one position (touching solids: the exact boolean splits a contact line into
    /// manifold sheets with duplicated vertices) are collapsed together with their twin edge, so a contact line can be reduced
    /// and the sheets stay exactly coincident. Off (the default), the reduction is bit for bit the old one (the hash tests).
    /// </summary>
    public class ReducerPairedContactsTests
    {
        readonly ITestOutputHelper output;
        public ReducerPairedContactsTests(ITestOutputHelper output) { this.output = output; }

        // ---- inputs ----
        internal static DMesh3 Soup(DMesh3 m)
        {
            var s = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                s.AppendTriangle(s.AppendVertex(m.GetVertex(tv.a)), s.AppendVertex(m.GetVertex(tv.b)), s.AppendVertex(m.GetVertex(tv.c)));
            }
            return s;
        }

        internal static DMesh3 Box(Vector3d c, int n)
        {
            return new GridBox3Generator() { Box = new Box3d(c, new Vector3d(1, 1, 1)), EdgeVertices = n }.Generate().MakeDMesh();
        }

        /// <summary> the exact union of two subdivided boxes (n x n vertices per edge) whose second is at (2, 2, 0) (edge touch) or (2, 2, 2) (vertex touch): contacts are duplicated vertices </summary>
        internal static DMesh3 TouchingBoxes(int n, Vector3d offset)
        {
            var op = new ExactMeshBoolean(new List<DMesh3> { Soup(Box(Vector3d.Zero, n)), Soup(Box(offset, n)) }, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = false, StrictRounding = true };
            Assert.True(op.Compute());
            return op.Result;
        }

        // ---- a hash of a mesh: every id, position bit and triangle ----
        internal static string Hash(DMesh3 m)
        {
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append(',').Append(m.MaxTriangleID).Append(',').Append(m.MaxEdgeID).Append(';');
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(tv.a).Append(',').Append(tv.b).Append(',').Append(tv.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 24);
        }

        static DMesh3 Bumpy(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.1 * PortableMath.Sin(5 * p.x + 1) * PortableMath.Cos(4 * p.y - 0.5) + 0.05 * PortableMath.Sin(9 * p.z)));      // (#1151: the same bits on every platform)
            }
            return m;
        }

        // ---- off: the reduction is the old one (hashes taken on the code before the option) ----
        static IEnumerable<(string name, string hash, Func<DMesh3> run)> OffCases()
        {
            yield return ("bumpy sphere, guard + quality", "624D02D48CB8CB8F94761421", () => { var m = Bumpy(14); new Reducer(m) { PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 }.ReduceToTriangleCount(m.TriangleCount / 4); return m; });
            yield return ("bumpy sphere, plain", "F4A5507516184FBC90C26E7F", () => { var m = Bumpy(14); new Reducer(m).ReduceToTriangleCount(m.TriangleCount / 3); return m; });
            yield return ("bumpy sphere, MaxDeviation", "6B0425A59E5C432DF67299C2", () => { var m = Bumpy(10); new Reducer(m) { PreventSelfIntersections = true, MaxDeviation = 0.05 }.ReduceToTriangleCount(m.TriangleCount / 3); return m; });
            yield return ("edge-touching boxes, guard + quality", "9598CDA277B94F9AA84A9F11", () => { var m = TouchingBoxes(6, new Vector3d(2, 2, 0)); new Reducer(m) { PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 }.ReduceToTriangleCount(10); return m; });
            yield return ("vertex-touching boxes, guard + quality", "F284AB1B05779E18DA5505C2", () => { var m = TouchingBoxes(6, new Vector3d(2, 2, 2)); new Reducer(m) { PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 }.ReduceToTriangleCount(10); return m; });
            yield return ("edge-touching boxes, no guard", "8DAC807426085223D47A9A09", () => { var m = TouchingBoxes(6, new Vector3d(2, 2, 0)); new Reducer(m).ReduceToTriangleCount(40); return m; });
        }

        // ---- helpers ----
        static Dictionary<int, List<int>> PositionGroups(DMesh3 m)
        {
            var first = new Dictionary<Vector3d, List<int>>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                if (p.x == 0) p.x = 0; if (p.y == 0) p.y = 0; if (p.z == 0) p.z = 0;
                if (!first.TryGetValue(p, out var l)) first[p] = l = new List<int>();
                l.Add(v);
            }
            var groups = new Dictionary<int, List<int>>();
            foreach (var l in first.Values) if (l.Count > 1) groups[l[0]] = l;
            return groups;
        }

        internal static int ContactPositions(DMesh3 m) { return PositionGroups(m).Count; }

        // the vertex ids of a triangle with the vertices of one position as one id
        static Index3i ByPosition(DMesh3 m, Index3i t, Dictionary<Vector3d, int> ids)
        {
            Index3i r = t;
            for (int k = 0; k < 3; ++k) {
                Vector3d p = m.GetVertex(t[k]);
                if (p.x == 0) p.x = 0; if (p.y == 0) p.y = 0; if (p.z == 0) p.z = 0;
                if (!ids.TryGetValue(p, out int id)) ids[p] = id = ids.Count;
                r[k] = id;
            }
            return r;
        }

        /// <summary> the first pair of triangles that meet beyond the vertices of one position they share (a crossing, or a touch away from a shared vertex or edge), or null: the sheets of a contact may share their positions and nothing else </summary>
        internal static string FirstCrossing(DMesh3 m)
        {
            var ids = new Dictionary<Vector3d, int>();
            var tris = m.TriangleIndices().ToList();
            var tid = new List<Index3i>(); var geo = new List<Triangle3d>();
            foreach (int t in tris) {
                tid.Add(ByPosition(m, m.GetTriangle(t), ids));
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                geo.Add(new Triangle3d(a, b, c));
            }
            for (int i = 0; i < tris.Count; ++i)
                for (int j = i + 1; j < tris.Count; ++j) {
                    Triangle3d A = geo[i], B = geo[j];
                    if (ExactTriangleIntersection.IntersectsBeyondShared(tid[i], ref A, tid[j], ref B))
                        return "triangles " + tris[i] + " and " + tris[j] + " meet beyond their shared vertices";
                }
            return null;
        }

        static Reducer Pairing(DMesh3 m, bool pair = true, bool guard = true, double quality = 0.02)
        {
            return new Reducer(m) { PairCoincidentVertices = pair, PreventSelfIntersections = guard, MinCollapseTriangleQuality = quality };
        }

        static void AssertSolid(DMesh3 m)
        {
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
            Assert.Null(FirstCrossing(m));
        }

        // ---- the option's cases ----
        [Fact]
        public void EdgeTouch_ContactLineIsCollapsed_SheetsStayCoincident()
        {
            DMesh3 start = TouchingBoxes(8, new Vector3d(2, 2, 0));
            var groupsBefore = PositionGroups(start);
            Assert.Equal(8, groupsBefore.Count);
            // without the option the contact line pins about 12 triangles per position
            var off = new DMesh3(start);
            Pairing(off, pair: false).ReduceToTriangleCount(4);
            Assert.True(off.TriangleCount >= 60, "off: " + off.TriangleCount);

            var m = new DMesh3(start);
            var r = Pairing(m);
            r.ReduceToTriangleCount(4);
            Assert.Equal(8, m.TriangleCount);          // two tetrahedra, one per solid
            Assert.True(r.PairedCollapses >= 6);
            AssertSolid(m);
            // the contact is still there: the two ends of the line, each position with its twin
            Assert.Equal(2, ContactPositions(m));
            // no gap: the twins that were at one position still are
            foreach (var g in groupsBefore.Values) {
                var alive = g.Where(m.IsVertex).ToList();
                for (int i = 1; i < alive.Count; ++i)
                    Assert.Equal(m.GetVertex(alive[0]), m.GetVertex(alive[i]));
            }
            // the contact vertices are the ends of the line: the box's edge from (1, 1, -1) to (1, 1, 1)
            foreach (var g in PositionGroups(m).Values) {
                Vector3d p = m.GetVertex(g[0]);
                Assert.Equal(1.0, p.x, 6); Assert.Equal(1.0, p.y, 6);
            }
        }

        [Fact]
        public void EdgeTouch_WithoutTheGuard_AlsoWorks()
        {
            var m = TouchingBoxes(6, new Vector3d(2, 2, 0));
            var r = Pairing(m, guard: false, quality: 0);
            r.ReduceToTriangleCount(40);
            Assert.True(m.TriangleCount <= 40);
            Assert.True(r.PairedCollapses > 0);
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void VertexTouch_ContactVertexStaysWhereItIs()
        {
            DMesh3 start = TouchingBoxes(6, new Vector3d(2, 2, 2));
            var g = PositionGroups(start);
            Assert.Single(g);
            Vector3d at = start.GetVertex(g.Values.First()[0]);
            var m = new DMesh3(start);
            var r = Pairing(m);
            r.ReduceToTriangleCount(4);
            Assert.True(m.TriangleCount <= 18);
            AssertSolid(m);
            var after = PositionGroups(m);
            Assert.Single(after);
            Assert.Equal(at, m.GetVertex(after.Values.First()[0]));
            Assert.Equal(0, r.PairedCollapses);       // (no twin edge at a point)
        }

        [Fact]
        public void NoContacts_IsTheOldReduction()
        {
            // a bumpy sphere has no two vertices at one position: option on equals option off, bit for bit
            foreach (bool guard in new[] { false, true }) {
                var a = Bumpy(12); Pairing(a, pair: false, guard: guard).ReduceToTriangleCount(a.TriangleCount / 4);
                var b = Bumpy(12); var r = Pairing(b, pair: true, guard: guard); r.ReduceToTriangleCount(b.TriangleCount / 4);
                Assert.Equal(Hash(a), Hash(b));
                Assert.Equal(0, r.PairedCollapses);
            }
        }

        [Fact]
        public void MaxDeviation_IsKeptForBothSheets()
        {
            DMesh3 start = TouchingBoxes(8, new Vector3d(2, 2, 0));
            var m = new DMesh3(start);
            var r = Pairing(m);
            r.MaxDeviation = 0.02;
            r.ReduceToTriangleCount(4);
            AssertSolid(m);
            Assert.True(r.PairedCollapses > 0);
            // every vertex of the result lies within the bound of the start's surface (the boxes' faces are planes: the distance of a vertex to the start)
            var tree = new DMeshAABBTree3(start, true);
            foreach (int v in m.VertexIndices()) {
                double d2;
                tree.FindNearestTriangle(m.GetVertex(v), out d2);
                Assert.True(d2 <= 0.02 * 0.02 + 1e-18);
            }
            // and the bound refuses collapses the free reduction does
            Assert.True(m.TriangleCount > 8);
            // the parallel certification gives the same mesh as the serial one
            var m2 = new DMesh3(start);
            var r2 = Pairing(m2);
            r2.MaxDeviation = 0.02; r2.ParallelDeviationCheck = false;
            r2.ReduceToTriangleCount(4);
            Assert.Equal(Hash(m), Hash(m2));
        }

        [Fact]
        public void Reduction_IsDeterministic()
        {
            DMesh3 start = TouchingBoxes(8, new Vector3d(2, 2, 0));
            string h = null;
            for (int i = 0; i < 4; ++i) {
                var m = new DMesh3(start);
                var r = Pairing(m);
                r.MaxDeviation = 0.05;
                r.ReduceToTriangleCount(20);
                string hi = Hash(m);
                if (h == null) h = hi; else Assert.Equal(h, hi);
            }
        }

        [Fact]
        public void ScaleInvariance_PowerOfTwoScales_BitIdentical()
        {
            DMesh3 start = TouchingBoxes(6, new Vector3d(2, 2, 0));
            Func<DMesh3, DMesh3> run = (mesh) => { var r = Pairing(mesh); r.ReduceToTriangleCount(10); return mesh; };
            DMesh3 reference = run(new DMesh3(start));
            Assert.True(reference.TriangleCount <= 10);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                DMesh3 result = run(Invariance.Transformed(start, s));
                string d = Invariance.Difference(reference, Invariance.MappedBack(result, s));
                Assert.True(d == "", s.Name + ": " + d);
            }
        }

        // a box of the contact line's other side, its sheet bumped away from the planes: the pair's new position then deviates from this sheet
        // (the contact line stays: its vertices are not moved), while the flat sheet of the first box does not
        static DMesh3 TouchingWithBumpySecondSolid(int n, double bump)
        {
            DMesh3 second = Box(new Vector3d(2, 2, 0), n);
            foreach (int v in second.VertexIndices()) {
                Vector3d p = second.GetVertex(v);
                bool onLine = Math.Abs(p.x - 1) < 1e-12 && Math.Abs(p.y - 1) < 1e-12;
                if (!onLine && p.x > 1 + 1e-12 || !onLine && p.y > 1 + 1e-12)
                    second.SetVertex(v, p + bump * new Vector3d(Math.Sin(7 * p.z + 1) * (p.x - 2), Math.Cos(5 * p.z) * (p.y - 2), Math.Sin(3 * p.x + p.y)) * (Math.Abs(p.z) < 1 - 1e-12 ? 1 : 0));
            }
            var op = new ExactMeshBoolean(new List<DMesh3> { Soup(Box(Vector3d.Zero, n)), Soup(second) }, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = false, StrictRounding = true };
            Assert.True(op.Compute());
            return op.Result;
        }

        // the vertices of each solid (connected piece) of the result lie within the bound of the same piece of the start (ids are kept by the reduction)
        static void AssertEachPieceWithin(DMesh3 start, DMesh3 result, double bound)
        {
            var comps = new MeshConnectedComponents(start);
            comps.FindConnectedT();
            Assert.True(comps.Count >= 2);
            for (int k = 0; k < comps.Count; ++k) {
                var piece = new DMesh3();
                var map = new Dictionary<int, int>();
                var verts = new HashSet<int>();
                foreach (int t in comps[k].Indices) {
                    Index3i tv = start.GetTriangle(t);
                    for (int c = 0; c < 3; ++c) {
                        verts.Add(tv[c]);
                        if (!map.ContainsKey(tv[c])) map[tv[c]] = piece.AppendVertex(start.GetVertex(tv[c]));
                    }
                    piece.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
                }
                var tree = new DMeshAABBTree3(piece, true);
                foreach (int t in result.TriangleIndices()) {
                    Index3i tv = result.GetTriangle(t);
                    if (verts.Contains(tv.a) == false) continue;
                    for (int c = 0; c < 3; ++c) {
                        double d2;
                        tree.FindNearestTriangle(result.GetVertex(tv[c]), out d2);
                        Assert.True(d2 <= bound * bound * (1 + 1e-9), "piece " + k + ": a vertex of the result is " + Math.Sqrt(d2) + " from the start's piece");
                    }
                }
            }
        }

        [Fact]
        public void MaxDeviation_BothSheetsAreCheckedSeparately()
        {
            DMesh3 start = TouchingWithBumpySecondSolid(8, 0.03);
            Assert.Equal(8, ContactPositions(start));
            foreach (double bound in new[] { 0.01, 0.03 }) {
                var m = new DMesh3(start);
                var r = Pairing(m, quality: 0);
                r.MaxDeviation = bound;
                r.ReduceToTriangleCount(4);
                Assert.True(r.PairedCollapses > 0, "bound " + bound);
                AssertSolid(m);
                AssertEachPieceWithin(start, m, bound);
            }
        }

        // the deviation reference has the second solid shifted away: its sheet breaks the bound at every collapse, so no pair may collapse (the
        // first solid's sheet alone would pass); with the true reference they do
        [Fact]
        public void MaxDeviation_ASheetThatBreaksTheBound_KeepsTheWholePair()
        {
            DMesh3 start = TouchingBoxes(6, new Vector3d(2, 2, 0));
            var shifted = new DMesh3(start);
            var comps = new MeshConnectedComponents(start);
            comps.FindConnectedT();
            Assert.Equal(2, comps.Count);
            foreach (int k in Enumerable.Range(0, 2)) {
                bool second = comps[k].Indices.All(t => { var tv = start.GetTriangle(t); return start.GetVertex(tv.a).x >= 1 - 1e-12 && start.GetVertex(tv.a).y >= 1 - 1e-12; });
                if (!second) continue;
                foreach (int t in comps[k].Indices) {
                    var tv = start.GetTriangle(t);
                    for (int c = 0; c < 3; ++c) shifted.SetVertex(tv[c], start.GetVertex(tv[c]) + new Vector3d(0.4, 0.4, 0));
                }
            }
            foreach (var (reference, expectPaired) in new[] { (start, true), (shifted, false) }) {
                var m = new DMesh3(start);
                var r = Pairing(m, quality: 0);
                r.MaxDeviation = 0.05;
                r.SetDeviationReference(reference);
                r.ReduceToTriangleCount(4);
                Assert.True((r.PairedCollapses > 0) == expectPaired, "paired collapses: " + r.PairedCollapses);
            }
        }

        // a box and a tetrahedron that touch along an edge (the contact: the tetrahedron's edge is an edge of the box): the pair's twin edge sits in a
        // tetrahedron, which DMesh3 does not collapse, so the edge in the box must not be collapsed either, whatever the target (nothing is left half done)
        [Fact]
        public void TwinEdgeInATetrahedron_IsRefusedWithTheEdge()
        {
            int refusedOnce = 0;
            foreach (bool tetFirst in new[] { false, true })      // (which of the two edges is first in the queue depends on the ids)
                for (int target = 4; target <= 40; target += 2) {
                    DMesh3 m = new DMesh3();
                    DMesh3 box = Box(Vector3d.Zero, 3);
                    int p = -1, q = -1, u = -1, w = -1;
                    Action addTet = () => {
                        // (the box's contact edge: (1, 1, -1) to (1, 1, 0); the tetrahedron has these ends and two points beyond)
                        p = m.AppendVertex(new Vector3d(1, 1, -1)); q = m.AppendVertex(new Vector3d(1, 1, 0));
                        u = m.AppendVertex(new Vector3d(2, 2, -0.5)); w = m.AppendVertex(new Vector3d(2, 2.5, -0.5));
                        m.AppendTriangle(p, u, q); m.AppendTriangle(p, q, w); m.AppendTriangle(q, u, w); m.AppendTriangle(p, w, u);
                    };
                    if (tetFirst) addTet();
                    MeshEditor.Append(m, box);
                    if (!tetFirst) addTet();
                    Assert.True(m.IsClosed());
                    Assert.Equal(2, ContactPositions(m));
                    var r = Pairing(m, guard: false, quality: 0);
                    r.ReduceToTriangleCount(target);
                    Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                    Assert.True(m.IsClosed());
                    Assert.Equal(4, m.TriangleIndices().Count(t => { var tv = m.GetTriangle(t); return new[] { tv.a, tv.b, tv.c }.All(v => v == p || v == q || v == u || v == w); }));
                    if (m.TriangleCount > target) refusedOnce++;
                }
            Assert.True(refusedOnce > 0);
        }

        [Fact]
        public void PairStructure_AllowsAContactEdge_RefusesWhatTheMeshOrTheRingsDoNotAllow()
        {
            // a contact edge of the touching boxes: both ends have one twin, the twin edge exists, the rings are apart
            DMesh3 boxes = TouchingBoxes(4, new Vector3d(2, 2, 0));
            var inGroup = new HashSet<int>(PositionGroups(boxes).Values.SelectMany(g => g));
            int contactEdge = boxes.EdgeIndices().First(e => inGroup.Contains(boxes.GetEdgeV(e).a) && inGroup.Contains(boxes.GetEdgeV(e).b));
            Assert.True(new Reducer(boxes) { PairCoincidentVertices = true }.PairStructureAllows(contactEdge));
            // an edge with no twin ends
            Assert.False(new Reducer(boxes) { PairCoincidentVertices = true }.PairStructureAllows(boxes.EdgeIndices().First(e => !inGroup.Contains(boxes.GetEdgeV(e).a) && !inGroup.Contains(boxes.GetEdgeV(e).b))));
            // the twin edge is in a tetrahedron, which the mesh does not collapse (DMesh3.CollapseEdge: Failed_CollapseTetrahedron)
            DMesh3 m = Box(Vector3d.Zero, 3);
            int p = m.AppendVertex(new Vector3d(1, 1, -1)), q = m.AppendVertex(new Vector3d(1, 1, 0));
            int u = m.AppendVertex(new Vector3d(2, 2, -0.5)), w = m.AppendVertex(new Vector3d(2, 2.5, -0.5));
            m.AppendTriangle(p, u, q); m.AppendTriangle(p, q, w); m.AppendTriangle(q, u, w); m.AppendTriangle(p, w, u);
            var rm = new Reducer(m) { PairCoincidentVertices = true };
            int boxEdge = m.EdgeIndices().First(e => { var ev = m.GetEdgeV(e); return ev.a < 26 && ev.b < 26 && m.GetVertex(ev.a) == m.GetVertex(p) && m.GetVertex(ev.b) == m.GetVertex(q) || ev.a < 26 && ev.b < 26 && m.GetVertex(ev.a) == m.GetVertex(q) && m.GetVertex(ev.b) == m.GetVertex(p); });
            Assert.False(rm.PairStructureAllows(boxEdge));
            Assert.False(rm.PairStructureAllows(m.FindEdge(p, q)));
            // a closed mesh whose vertex a is next to the twin of its neighbour b (a crushed octahedron: B at the place of T, E2 at that of E0; T and E2 are adjacent)
            DMesh3 o = new DMesh3();
            int T = o.AppendVertex(new Vector3d(0, 0, 1)), B = o.AppendVertex(new Vector3d(0, 0, 1));
            int E0 = o.AppendVertex(new Vector3d(1, 0, 0)), E1 = o.AppendVertex(new Vector3d(0, 1, 0)), E2 = o.AppendVertex(new Vector3d(1, 0, 0)), E3 = o.AppendVertex(new Vector3d(0, -1, 0));
            o.AppendTriangle(T, E0, E1); o.AppendTriangle(T, E1, E2); o.AppendTriangle(T, E2, E3); o.AppendTriangle(T, E3, E0);
            o.AppendTriangle(B, E1, E0); o.AppendTriangle(B, E2, E1); o.AppendTriangle(B, E3, E2); o.AppendTriangle(B, E0, E3);
            Assert.True(o.IsClosed());
            var ro = new Reducer(o) { PairCoincidentVertices = true };
            Assert.False(ro.PairStructureAllows(o.FindEdge(T, E0)));
        }

        // a third body next to the contact: many positions of it, every reduction ends without a crossing and with a consistent grid
        [Fact]
        public void ThirdBodyNextToTheContact_NoCrossing_GridConsistent()
        {
            int pairedSomewhere = 0, cases = 0;
            foreach (double z in new[] { -0.6, 0.0, 0.5 })
                foreach (double off in new[] { 0.08, 0.2 }) {
                    var third = new GridBox3Generator() { Box = new Box3d(new Vector3d(1 + off, 1 + off, z), new Vector3d(0.15, 0.15, 0.3)), EdgeVertices = 3 }.Generate().MakeDMesh();
                    var op = new ExactMeshBoolean(new List<DMesh3> { Soup(Box(Vector3d.Zero, 6)), Soup(Box(new Vector3d(2, 2, 0), 6)), Soup(third) }, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = false, StrictRounding = true };
                    if (op.Compute() == false) continue;
                    DMesh3 start = op.Result;
                    var m = new DMesh3(start);
                    var r = Pairing(m);
                    r.ReduceToTriangleCount(4);
                    AssertSolid(m);
                    pairedSomewhere += r.PairedCollapses > 0 ? 1 : 0;
                    cases++;
                }
            Assert.True(cases >= 4 && pairedSomewhere >= 1, cases + " cases, " + pairedSomewhere + " with paired collapses");
        }

        // the self-intersection grid is checked after every collapse
        sealed class GridWatcher : Reducer
        {
            public string FirstDifference = "";
            public int Checked;
            public GridWatcher(DMesh3 m) : base(m) { }
            protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo collapseInfo)
            {
                Checked++;
                if (FirstDifference.Length == 0)
                    FirstDifference = TriangleGridDifference();
            }
        }

        [Fact]
        public void Grid_IsConsistentAfterEveryCollapse()
        {
            var m = TouchingBoxes(6, new Vector3d(2, 2, 0));
            var r = new GridWatcher(m) { PairCoincidentVertices = true, PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 };
            r.ReduceToTriangleCount(12);
            Assert.True(r.PairedCollapses > 0);
            Assert.True(r.Checked > r.PairedCollapses);
            Assert.Equal("", r.FirstDifference);
            Assert.Equal("", r.TriangleGridDifference());
        }

        // the queue's quadrics are those of a fresh computation before every collapse (an edge's quadric includes its twin edge's)
        sealed class QuadricWatcher : Reducer
        {
            public string FirstDifference = "";
            public QuadricWatcher(DMesh3 m) : base(m) { }
            protected override ProcessResult CollapseEdge(int edgeID, Vector3d vNewPos, out int collapseToV)
            {
                if (FirstDifference.Length == 0)
                    FirstDifference = QueueQuadricDifference();
                return base.CollapseEdge(edgeID, vNewPos, out collapseToV);
            }
        }

        [Fact]
        public void QueueQuadrics_StayFresh_AlsoAfterACollapseOntoAContactVertex()
        {
            foreach (var offset in new[] { new Vector3d(2, 2, 0), new Vector3d(2, 2, 2) }) {
                var m = TouchingBoxes(6, offset);
                var r = new QuadricWatcher(m) { PairCoincidentVertices = true, PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02 };
                r.ReduceToTriangleCount(10);
                Assert.Equal("", r.FirstDifference);
            }
        }

        [Fact]
        public void OtherEntryPoints_ReduceContactsToo()
        {
            DMesh3 start = TouchingBoxes(6, new Vector3d(2, 2, 0));
            var a = new DMesh3(start); var ra = Pairing(a); ra.ReduceToVertexCount(8);
            AssertSolid(a); Assert.True(a.VertexCount <= 8 && ra.PairedCollapses > 0);
            var b = new DMesh3(start); var rb = Pairing(b); rb.ReduceToMaxQuadricError(1e-3);
            AssertSolid(b); Assert.True(b.TriangleCount < start.TriangleCount / 2 && rb.PairedCollapses > 0, b.TriangleCount + " " + rb.PairedCollapses);
            var c = new DMesh3(start); var rc = Pairing(c); rc.ReduceToEdgeLength(1.0);
            AssertSolid(c); Assert.True(c.TriangleCount < start.TriangleCount);
            var d = new DMesh3(start); var rd = Pairing(d); rd.FastCollapsePass(0.8, 3);
            AssertSolid(d); Assert.True(d.TriangleCount < start.TriangleCount);
        }

        [Fact]
        public void SameReducer_ReducesTwice()
        {
            var m = TouchingBoxes(8, new Vector3d(2, 2, 0));
            var r = Pairing(m);
            r.ReduceToTriangleCount(60);
            AssertSolid(m);
            r.ReduceToTriangleCount(4);
            AssertSolid(m);
            Assert.Equal(8, m.TriangleCount);
            Assert.Equal(2, ContactPositions(m));
        }

        // golden: the reduction with the option on (the code of this commit), so a change of the quadrics, the order or the positions is seen
        [Fact]
        public void On_ByHash()
        {
            var a = TouchingBoxes(6, new Vector3d(2, 2, 0)); Pairing(a).ReduceToTriangleCount(10);
            var b = TouchingBoxes(6, new Vector3d(2, 2, 0)); Pairing(b, guard: false, quality: 0).ReduceToTriangleCount(30);
            Assert.Equal("3599AADDE54BDD9A2E4A269A", Hash(a));
            Assert.Equal("C201D292886A31EF54B84203", Hash(b));
        }

        [Fact]
        public void Unsupported_Throws()
        {
            DMesh3 start = TouchingBoxes(4, new Vector3d(2, 2, 0));
            var r = Pairing(new DMesh3(start)); r.MaxDeviation = 0.1; r.MaxDeviationTwoSided = true;
            Assert.Throws<InvalidOperationException>(() => r.ReduceToTriangleCount(10));
            var m = new DMesh3(start);
            var c = Pairing(m);
            c.SetExternalConstraints(new MeshConstraints());
            Assert.Throws<InvalidOperationException>(() => c.ReduceToTriangleCount(10));
        }

        // ---- the neighbourhood test ----
        static IEnumerable<DMesh3> NeighbourhoodMeshes()
        {
            yield return Bumpy(6);
            yield return TouchingBoxes(3, new Vector3d(2, 2, 0));
            yield return TouchingBoxes(2, new Vector3d(2, 2, 2));
            yield return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 2 }.Generate().MakeDMesh();   // small: tetrahedron-like fans
            var open = Bumpy(5);
            foreach (int t in open.TriangleIndices().Take(7).ToArray()) open.RemoveTriangle(t);
            yield return open;
            var tet = new DMesh3();
            tet.AppendVertex(new Vector3d(0, 0, 0)); tet.AppendVertex(new Vector3d(1, 0, 0)); tet.AppendVertex(new Vector3d(0, 1, 0)); tet.AppendVertex(new Vector3d(0, 0, 1));
            tet.AppendTriangle(0, 2, 1); tet.AppendTriangle(0, 1, 3); tet.AppendTriangle(1, 2, 3); tet.AppendTriangle(0, 3, 2);
            yield return tet;
            var one = new DMesh3(); one.AppendVertex(Vector3d.Zero); one.AppendVertex(Vector3d.AxisX); one.AppendVertex(Vector3d.AxisY); one.AppendTriangle(0, 1, 2);
            yield return one;
        }

        [Fact]
        public void MeshAcceptsCollapse_IsWhatDMesh3Does_OnEveryEdge()
        {
            int accepted = 0, refused = 0;
            foreach (DMesh3 m in NeighbourhoodMeshes())
                foreach (int e in m.EdgeIndices()) {
                    Index2i ev = m.GetEdgeV(e);
                    foreach (var (keep, remove) in new[] { (ev.a, ev.b), (ev.b, ev.a) }) {
                        bool predicted = Reducer.MeshAcceptsCollapse(m, keep, remove);
                        var copy = new DMesh3(m);
                        DMesh3.EdgeCollapseInfo info;
                        bool actual = copy.CollapseEdge(keep, remove, out info) == MeshResult.Ok;
                        Assert.True(predicted == actual, "edge " + e + " keep " + keep + " remove " + remove + ": predicted " + predicted + ", DMesh3 " + actual);
                        if (actual) accepted++; else refused++;
                    }
                }
            Assert.True(accepted > 100 && refused > 10, accepted + " accepted, " + refused + " refused");
            Assert.False(Reducer.MeshAcceptsCollapse(new DMesh3(), 0, 1));
        }

        [Fact]
        public void Off_IsTheOldReduction_ByHash()
        {
            var wrong = new List<string>();
            foreach (var c in OffCases())
                if (c.hash != Hash(c.run())) wrong.Add(c.name + ": " + Hash(c.run()));
            Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        }

        [Fact]
        public void Off_IsTheDefault()
        {
            Assert.False(new Reducer(new DMesh3()).PairCoincidentVertices);
        }
    }
}
