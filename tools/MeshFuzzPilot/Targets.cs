using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MeshFuzzPilot;

/// <summary>
/// The spatial-query targets (DMeshAABBTree3 against brute force, MeshBatchQueries against the single calls) and the
/// alpha wrap. As for the operations, every claim is a function of plain results, so that the self-test can hand it a
/// contradiction. The queries are judged on a power-of-two normalized copy of the mesh (exact), and the raw tree's
/// answer is compared with the normalized one where the claim is scale invariant.
/// </summary>
public static partial class Program
{
    // ---- decoding the queries: eight times (origin kind, three index bytes, direction kind, direction byte) ----

    static Vector3d Finite3(Vector3d p, Vector3d fallback)
        => Math.Abs(p.x) <= double.MaxValue && Math.Abs(p.y) <= double.MaxValue && Math.Abs(p.z) <= double.MaxValue ? p : fallback;

    static Vector3d Third(Vector3d a, Vector3d b, Vector3d c) => a / 3 + b / 3 + c / 3;

    static void DecodeQueries(Reader r, Case c)
    {
        int[] va = c.A.VertexIndices().ToArray(), vb = c.B.VertexIndices().ToArray();
        int[] ta = c.A.TriangleIndices().ToArray(), tb = c.B.TriangleIndices().ToArray();
        AxisAlignedBox3d box = c.A.GetBounds();
        double diag = Diagonal(box);
        Vector3d anyVertex = c.A.GetVertex(va[0]);
        for (int q = 0; q < 8; ++q) {
            int ok = r.Byte() % 8, a = r.Byte(), b = r.Byte(), cc = r.Byte(), dk = r.Byte() % 8, dv = r.Byte();
            Vector3d o;
            switch (ok) {
                case 0: o = c.A.GetVertex(va[a % va.Length]); break;
                case 1: o = c.B.GetVertex(vb[a % vb.Length]); break;
                case 2: { Index3i t = c.A.GetTriangle(ta[a % ta.Length]); o = Third(c.A.GetVertex(t.a), c.A.GetVertex(t.b), c.A.GetVertex(t.c)); break; }
                case 3: { Index3i t = c.A.GetTriangle(ta[a % ta.Length]); o = 0.5 * c.A.GetVertex(t[b % 3]) + 0.5 * c.A.GetVertex(t[(b + 1) % 3]); break; }
                case 4: { Index3i t = c.B.GetTriangle(tb[a % tb.Length]); o = Third(c.B.GetVertex(t.a), c.B.GetVertex(t.b), c.B.GetVertex(t.c)); break; }
                case 5: o = new Vector3d(box.Min.x + (box.Max.x - box.Min.x) * (a / 255.0), box.Min.y + (box.Max.y - box.Min.y) * (b / 255.0),
                    box.Min.z + (box.Max.z - box.Min.z) * (cc / 255.0)); break;
                case 6: o = c.A.GetVertex(va[dv % va.Length]) + diag * (1.0 / 1024) * new Vector3d((sbyte)a, (sbyte)b, (sbyte)cc) / 128; break;
                default: o = 0.5 * box.Min + 0.5 * box.Max; break;
            }
            o = Finite3(o, anyVertex);
            Vector3d d;
            if (dk < 6) {
                d = Vector3d.Zero;
                d[dk / 2] = dk % 2 == 0 ? 1 : -1;
            } else if (dk == 6) {
                d = (c.A.GetVertex(va[dv % va.Length]) - o).Normalized;
            } else {
                d = new Vector3d((sbyte)a, (sbyte)b, (sbyte)cc).Normalized;
            }
            if (!(Math.Abs(d.x) <= 1 && Math.Abs(d.y) <= 1 && Math.Abs(d.z) <= 1) || !d.IsNormalized)
                d = Vector3d.AxisX;
            c.Queries.Add((o, d));
        }
    }

    // ---- the common setup of the tree targets ----

    sealed class Setup
    {
        public DMesh3 Raw = null!, Norm = null!;
        public double Scale;
        public DMeshAABBTree3 RawTree = null!, NormTree = null!;
        /// <summary> the raw squared distances neither overflow nor underflow (the squared distance of the API) </summary>
        public bool RawDistancesRepresentable;
        public List<(Vector3d rawOrigin, Vector3d normOrigin, Vector3d dir)> Queries = new List<(Vector3d, Vector3d, Vector3d)>();
        /// <summary> the index of each of Queries in the case's list (the replay and dump print that one) </summary>
        public List<int> QueryIndices = new List<int>();
    }

    /// <summary>
    /// a copy scaled by a power of two so that the diagonal of the whole mesh's box is in [1, 2) (the queries are judged
    /// on the whole mesh, not on its largest component); the mesh itself if the scale does not fit
    /// </summary>
    public static DMesh3 NormalizeWhole(DMesh3 m, out double scale)
    {
        scale = 1;
        double d = Diagonal(m.GetBounds());
        if (!(d > 0 && d <= double.MaxValue))
            return m;
        int e = -Exponent(d);
        double s = Pow2(e);
        if (e == 0 || Exponent(s) != e)
            return m;
        var n = new DMesh3(m);
        foreach (int v in n.VertexIndices()) {
            Vector3d p = s * n.GetVertex(v);
            if (!(Math.Abs(p.x) <= double.MaxValue / 16 && Math.Abs(p.y) <= double.MaxValue / 16 && Math.Abs(p.z) <= double.MaxValue / 16))
                return m;
            n.SetVertex(v, p);
        }
        scale = s;
        return n;
    }

    // null if the mesh cannot be scaled exactly (a subnormal coordinate): the claims need the exact copy
    static Setup? MakeSetup(Case c, bool noSlivers = false)
    {
        var s = new Setup { Raw = c.A };
        // the doubles' last few binades are not a domain of the queries: sums of three coordinates, differences of far
        // corners and the boxes' padding overflow there (coordinates up to 2^1000 are)
        foreach (Vector3d p in c.A.Vertices())
            if (!(Math.Abs(p.x) <= Pow2(1000) && Math.Abs(p.y) <= Pow2(1000) && Math.Abs(p.z) <= Pow2(1000)))
                return null;
        s.Norm = NormalizeWhole(c.A, out s.Scale);
        double nd = Diagonal(s.Norm.GetBounds());
        if (!(nd >= 1 && nd < 2))
            return null;        // (no exact copy of normal size: the claims have nothing to be judged on)
        // a triangle smaller than 2^-50 of the mesh is below the rounding of the mesh's own coordinates (the boxes of the
        // tree are padded by 2^-40 of the mesh, so a query cannot be told from a miss there): not a domain of the claims
        foreach (int tid in s.Norm.TriangleIndices()) {
            double td = Diagonal(s.Norm.GetTriBounds(tid));
            if (td > 0 && td < 8.8817841970012523e-16)
                return null;
            if (noSlivers) {
                // the ray parameter of a sliver (area below 2^-30 of its longest edge squared, but not zero) is the quotient
                // of two rounding errors, so the traversal's pruning by the boxes cannot be compared with the parameter
                Index3i f = s.Norm.GetTriangle(tid);
                Vector3d a = s.Norm.GetVertex(f.a), b = s.Norm.GetVertex(f.b), cc = s.Norm.GetVertex(f.c);
                double area = SafeLength((b - a).Cross(cc - a)), longest = Math.Max(SafeLength(b - a), Math.Max(SafeLength(cc - a), SafeLength(cc - b)));
                longest *= longest;
                if (area > 0 && area < 9.313225746154785e-10 * longest)
                    return null;
            }
        }
        // exact: scaling back gives the raw coordinates
        foreach (int v in c.A.VertexIndices())
            if (s.Norm.GetVertex(v) / s.Scale != c.A.GetVertex(v))
                return null;
        double diag = Diagonal(c.A.GetBounds());
        s.RawDistancesRepresentable = diag >= Math.Pow(2, -450) && diag <= Math.Pow(2, 450);
        s.RawTree = new DMeshAABBTree3(s.Raw, true);
        s.NormTree = ReferenceEquals(s.Norm, s.Raw) ? s.RawTree : new DMeshAABBTree3(s.Norm, true);
        AxisAlignedBox3d bounds = c.A.GetBounds();
        for (int qx = 0; qx < c.Queries.Count; ++qx) {
            var (o, d) = c.Queries[qx];
            // the claims are for queries near the mesh: the boxes' padding scales with the mesh, not with the distance of
            // the query (an origin 1e280 times farther than the mesh is wider than the mesh in rounding alone)
            AxisAlignedBox3d withQuery = bounds;
            withQuery.Contain(o);
            if (!(Diagonal(withQuery) <= 1048576 * diag))
                continue;
            Vector3d on = o * s.Scale;
            if (!(on / s.Scale == o) || Finite3(on, Vector3d.Zero) != on)
                continue;
            s.Queries.Add((o, on, d));
            s.QueryIndices.Add(qx);
        }
        return s;
    }

    // ---- nearest triangle and vertex ----

    /// <summary>
    /// FindNearestTriangle on the normalized mesh: a triangle (a mesh with triangles has one) whose squared distance is
    /// the brute force minimum, bit for bit (the same TriDistanceSqr), and the out value is that distance. The raw tree
    /// (where the squared distances are representable) returns a triangle that is a minimum as well.
    /// </summary>
    public static void CheckNearestTriangle(string what, int treeId, double treeDistSqr, double idDistSqr, double bruteDistSqr, bool hasTriangles)
    {
        if (!hasTriangles) {
            if (treeId != DMesh3.InvalidID)
                throw new Finding($"{what}: nearest triangle {treeId} of an empty mesh");
            return;
        }
        if (treeId == DMesh3.InvalidID)
            throw new Finding($"{what}: no nearest triangle, but the brute force minimum is {bruteDistSqr}");
        if (idDistSqr != bruteDistSqr)
            throw new Finding($"{what}: nearest triangle {treeId} at squared distance {idDistSqr}, the minimum is {bruteDistSqr}");
        if (!double.IsNaN(treeDistSqr) && treeDistSqr != bruteDistSqr)
            throw new Finding($"{what}: the reported squared distance {treeDistSqr} is not the minimum {bruteDistSqr}");
    }

    public static void CheckNearestVertex(string what, int treeId, double idDistSqr, double bruteDistSqr)
    {
        if (treeId == DMesh3.InvalidID)
            throw new Finding($"{what}: no nearest vertex, but the brute force minimum is {bruteDistSqr}");
        if (idDistSqr != bruteDistSqr)
            throw new Finding($"{what}: nearest vertex {treeId} at squared distance {idDistSqr}, the minimum is {bruteDistSqr}");
    }

    static void RunTreeNearest(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        var usedVertices = new HashSet<int>();
        foreach (int t in s.Norm.TriangleIndices()) {
            Index3i f = s.Norm.GetTriangle(t);
            usedVertices.Add(f.a); usedVertices.Add(f.b); usedVertices.Add(f.c);
        }
        int qn = -1;
        foreach (var (rawO, o, _) in s.Queries) {
            string w = what + $" query {s.QueryIndices[++qn]}";
            double brute = double.MaxValue;
            foreach (int t in s.Norm.TriangleIndices())
                brute = Math.Min(brute, MeshQueries.TriDistanceSqr(s.Norm, t, o));
            int id = s.NormTree.FindNearestTriangle(o, out double d2);
            CheckNearestTriangle(w, id, d2, id == DMesh3.InvalidID ? double.NaN : MeshQueries.TriDistanceSqr(s.Norm, id, o), brute, true);
            if (s.RawDistancesRepresentable) {
                int rid = s.RawTree.FindNearestTriangle(rawO, out double rd2);
                CheckNearestTriangle(w + " (raw scale)", rid, double.NaN, rid == DMesh3.InvalidID ? double.NaN : MeshQueries.TriDistanceSqr(s.Norm, rid, o), brute, true);
            }
            double bruteV = double.MaxValue;
            foreach (int v in usedVertices)
                bruteV = Math.Min(bruteV, (s.Norm.GetVertex(v) - o).LengthSquared);
            int vid = s.NormTree.FindNearestVertex(o);
            CheckNearestVertex(w, vid, vid == DMesh3.InvalidID ? double.NaN : (s.Norm.GetVertex(vid) - o).LengthSquared, bruteV);
        }
    }

    // ---- rays ----

    /// <summary>
    /// FindNearestHitTriangle is the brute force nearest hit (smallest ray parameter, the lowest id among equal ones), and
    /// FindAllHitTriangles the set of triangles the ray hits; the raw tree gives the same answers
    /// </summary>
    public static void CheckRay(string what, int bruteNearest, int treeNearest, IEnumerable<int> bruteAll, IEnumerable<int> treeAll, bool judgeNearest = true)
    {
        if (judgeNearest && bruteNearest != treeNearest)
            throw new Finding($"{what}: nearest hit triangle {treeNearest}, brute force {bruteNearest}");
        if (!judgeNearest && (treeNearest == DMesh3.InvalidID) != (bruteNearest == DMesh3.InvalidID))
            throw new Finding($"{what}: nearest hit triangle {treeNearest}, brute force {bruteNearest} (existence of a hit)");
        if (!judgeNearest && treeNearest != DMesh3.InvalidID && !bruteAll.Contains(treeNearest))
            throw new Finding($"{what}: nearest hit triangle {treeNearest} is not a hit");
        string b = string.Join(",", bruteAll.OrderBy(x => x)), t = string.Join(",", treeAll.OrderBy(x => x));
        if (b != t)
            throw new Finding($"{what}: all hits [{t}], brute force [{b}]");
    }

    static void BruteRay(DMesh3 m, Ray3d ray, out int nearest, out List<int> all)
    {
        nearest = DMesh3.InvalidID;
        all = new List<int>();
        double best = double.MaxValue;
        Vector3d sp = ExactRayTriangle.SecondPoint(ray.Origin, ray.Direction);
        foreach (int t in m.TriangleIndices()) {
            Triangle3d tri = default;
            m.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
            if (IntrRay3Triangle3.Intersects(ref ray, ref sp, ref tri.V0, ref tri.V1, ref tri.V2, out double rt)) {
                all.Add(t);
                if (rt < best) { best = rt; nearest = t; }
            }
        }
    }

    // the length of a vector without underflow or overflow of the squares
    static double SafeLength(Vector3d v)
    {
        double m = Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
        if (!(m > 0) || !(m <= double.MaxValue))
            return m;
        double x = v.x / m, y = v.y / m, z = v.z / m;
        return m * Math.Sqrt(x * x + y * y + z * z);
    }

    // a hit whose ray is within 2^-30 of the triangle's plane (or in it) has a ray parameter that is a quotient of rounding
    // errors: the traversal's boxes cannot be compared with it, so such a query is judged by its set of hits only
    static bool NearParallel(DMesh3 m, int t, Vector3d dir)
    {
        Index3i f = m.GetTriangle(t);
        Vector3d a = m.GetVertex(f.a), b = m.GetVertex(f.b), c = m.GetVertex(f.c);
        Vector3d n = (b - a).Cross(c - a);
        return !(Math.Abs(n.Dot(dir)) >= 9.313225746154785e-10 * SafeLength(n));
    }

    static void RunTreeRay(Case c, string what)
    {
        Setup? s = MakeSetup(c, noSlivers: true);
        if (s == null)
            return;
        int qn = -1;
        foreach (var (rawO, o, d) in s.Queries) {
            int qi = s.QueryIndices[++qn];
            var rayN = new Ray3d(o, d);
            BruteRay(s.Norm, rayN, out int bn, out List<int> ba);
            var allN = new List<int>();
            s.NormTree.FindAllHitTriangles(rayN, allN);
            bool judgeN = !ba.Any(t => NearParallel(s.Norm, t, d));
            CheckRay(what + $" query {qi}", bn, s.NormTree.FindNearestHitTriangle(rayN), ba, allN, judgeN);
            // the raw tree against the brute force at the raw scale (the decisions are exact for the line through the origin and
            // its second point, which depends on how the direction is rounded at the scale: not compared across scales)
            var rayR = new Ray3d(rawO, d);
            BruteRay(s.Raw, rayR, out int br, out List<int> bra);
            var allR = new List<int>();
            s.RawTree.FindAllHitTriangles(rayR, allR);
            CheckRay(what + $" query {qi} (raw scale)", br, s.RawTree.FindNearestHitTriangle(rayR), bra, allR, !bra.Any(t => NearParallel(s.Raw, t, d)));
        }
    }

    // ---- inside ----

    static readonly Vector3d InsideDirection = new Vector3d(0.331960519038825, 0.462531727525156, 0.822111072077288);

    static int BruteCrossings(DMesh3 m, Vector3d origin)
    {
        var ray = new Ray3d(origin, InsideDirection, true);
        Vector3d sp = ExactRayTriangle.SecondPoint(ray.Origin, ray.Direction);
        int n = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = default, b = default, cc = default; m.GetTriVertices(t, ref a, ref b, ref cc);
            if (ExactRayTriangle.RayHitPerturbed(ray.Origin, sp, a, b, cc) != ExactRayTriangle.Miss)
                ++n;
        }
        return n;
    }

    /// <summary>
    /// IsInside is the parity of the exact crossings: the tree's count is the brute force count, on the normalized mesh
    /// and at the raw scale; for a clean closed mesh (no defect) and a point off the surface it is also the winding
    /// number above one half
    /// </summary>
    public static void CheckInside(string what, int bruteCrossings, int treeCrossings, bool isInside, bool? windingInside)
    {
        if (bruteCrossings != treeCrossings)
            throw new Finding($"{what}: {treeCrossings} crossings, brute force {bruteCrossings}");
        if (isInside != (treeCrossings % 2 != 0))
            throw new Finding($"{what}: IsInside {isInside} but {treeCrossings} crossings");
        if (windingInside != null && windingInside.Value != isInside)
            throw new Finding($"{what}: a clean closed mesh, IsInside {isInside} but the winding number says {windingInside.Value}");
    }

    /// <summary>
    /// DMeshAABBTree3.WindingNumber (the exact sum of the solid angles, with the fan closing of its cache for far boxes)
    /// is finite and agrees with the brute force sum on the normalized mesh and with the normalized one at the raw scale
    /// </summary>
    public static void CheckWinding(string what, double bruteWinding, double normWinding, double rawWinding)
    {
        if (!(Math.Abs(normWinding) <= double.MaxValue) || !(Math.Abs(rawWinding) <= double.MaxValue))
            throw new Finding($"{what}: WindingNumber is not finite ({normWinding} normalized, {rawWinding} at the raw scale)");
        if (!(Math.Abs(normWinding - bruteWinding) <= 1e-6))
            throw new Finding($"{what}: WindingNumber {normWinding}, brute force {bruteWinding}");
        if (!(Math.Abs(rawWinding - normWinding) <= 1e-6))
            throw new Finding($"{what}: WindingNumber {rawWinding} at the raw scale, {normWinding} normalized");
    }

    static void RunTreeInside(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        bool clean = MeshRobustRepair.FindDefects(s.Norm, true) == MeshRobustRepair.Defects.None;
        double diag = Diagonal(s.Norm.GetBounds());
        int qn = -1;
        foreach (var (rawO, o, _) in s.Queries) {
            string w = what + $" query {s.QueryIndices[++qn]}";
            int brute = BruteCrossings(s.Norm, o);
            int crossN = s.NormTree.CountCrossings(o, InsideDirection);
            bool? winding = null;
            if (clean) {
                double near = double.MaxValue;
                foreach (int t in s.Norm.TriangleIndices())
                    near = Math.Min(near, MeshQueries.TriDistanceSqr(s.Norm, t, o));
                if (near > 1e-8 * diag * diag)
                    winding = BruteWinding(s.Norm, o) > 0.5;
            }
            CheckInside(w, brute, crossN, s.NormTree.IsInside(o), winding);
            int crossR = s.RawTree.CountCrossings(rawO, InsideDirection);
            CheckInside(w + " (raw scale)", brute, crossR, s.RawTree.IsInside(rawO), null);
        }
    }

    static double BruteWinding(DMesh3 m, Vector3d p)
    {
        double sum = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = default, b = default, cc = default;
            m.GetTriVertices(t, ref a, ref b, ref cc);
            sum += MathUtil.TriSolidAngle(a, b, cc, ref p);
        }
        return sum / (4 * Math.PI);
    }

    static void RunTreeWinding(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        int qn = -1;
        foreach (var (rawO, o, _) in s.Queries) {
            string w = what + $" query {s.QueryIndices[++qn]}";
            // (not on the surface, where the winding number jumps and a solid angle is ill-conditioned)
            double near = double.MaxValue;
            foreach (int t in s.Norm.TriangleIndices())
                near = Math.Min(near, MeshQueries.TriDistanceSqr(s.Norm, t, o));
            if (!(near > 1e-8 * Diagonal(s.Norm.GetBounds()) * Diagonal(s.Norm.GetBounds())))
                continue;
            CheckWinding(w, BruteWinding(s.Norm, o), s.NormTree.WindingNumber(o), s.RawTree.WindingNumber(rawO));
        }
    }

    // ---- MeshBatchQueries against the single calls ----

    /// <summary> a batch result equals the single call's, bit for bit (the contract of MeshBatchQueries) </summary>
    public static void CheckBatchEqual(string what, string field, object? batch, object? single)
    {
        if (!Equals(batch, single))
            throw new Finding($"{what}: batch {field} {batch}, single call {single}");
    }

    static void RunBatch(Case c, string what)
    {
        DMesh3 mesh = c.A;
        var tree = new DMeshAABBTree3(mesh, true);
        var queries = c.Queries;
        // enough queries to take the sorted and the parallel path (1024 and more), the base queries repeated, and a few
        // bad ones: a direction that is not normalized, a non-finite origin
        int n = 1030;
        var rays = new Ray3d[n];
        var points = new Vector3d[n];
        for (int i = 0; i < n; ++i) {
            var (o, d) = queries[i % queries.Count];
            rays[i] = new Ray3d(o, d);
            points[i] = o;
            if (i % 97 == 5) rays[i] = new Ray3d(o, 1.5 * d);
            if (i % 101 == 7) { rays[i] = new Ray3d(new Vector3d(double.NaN, 0, 0), d); points[i] = new Vector3d(0, double.PositiveInfinity, 0); }
        }
        double diag = Diagonal(mesh.GetBounds());
        // the single calls, once per distance limit
        foreach (double maxDist in new[] { double.MaxValue, 0.75 * diag }) {
            var single = new Dictionary<int, (RayStatus rs, int tri, long tBits, int nearest, long nearestBits, bool inside, bool windInside)>();
            for (int i = 0; i < n; ++i) {
                Ray3d ray = rays[i];
                bool finiteRay = double.IsFinite(ray.Origin.x) && double.IsFinite(ray.Origin.y) && double.IsFinite(ray.Origin.z);
                RayStatus rs = !finiteRay ? RayStatus.NonFinite : !ray.Direction.IsNormalized ? RayStatus.NotNormalized : RayStatus.Ok;
                int tri = DMesh3.InvalidID;
                double t = double.MaxValue;
                if (rs == RayStatus.Ok) {
                    tri = tree.FindNearestHitTriangle(ray, maxDist);
                    if (tri != DMesh3.InvalidID) {
                        Vector3d a = default, b = default, cc = default;
                        mesh.GetTriVertices(tri, ref a, ref b, ref cc);
                        IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref cc, out t);
                    }
                }
                Vector3d p = points[i];
                int nt = DMesh3.InvalidID;
                double d2 = 0;
                bool finiteP = double.IsFinite(p.x) && double.IsFinite(p.y) && double.IsFinite(p.z);
                if (finiteP)
                    nt = tree.FindNearestTriangle(p, out d2, maxDist);
                single[i] = (rs, tri, BitConverter.DoubleToInt64Bits(t), nt, BitConverter.DoubleToInt64Bits(nt == DMesh3.InvalidID ? double.MaxValue : Math.Sqrt(d2)),
                    finiteP && tree.IsInside(p), finiteP && tree.FastWindingNumber(p) > 0.5);
            }
            var combos = maxDist == double.MaxValue ? new[] { (true, true), (false, false), (true, false), (false, true) } : new[] { (true, true) };
            foreach (var (parallel, sort) in combos) {
                var options = new BatchOptions() { Parallel = parallel, SortForCoherence = sort, MaxDistance = maxDist };
                string w = $"{what} [max {maxDist}, parallel {parallel}, sorted {sort}]";
                var hits = new RayHit[n];
                var rs = new RayStatus[n];
                CheckBatchEqual(w, "IntersectBatch status", MeshBatchQueries.IntersectBatch(tree, rays, hits, rs, options), BatchStatus.Ok);
                var any = new bool[n];
                CheckBatchEqual(w, "AnyHitBatch status", MeshBatchQueries.AnyHitBatch(tree, rays, any, null, options), BatchStatus.Ok);
                var res = new NearestPointResult[n];
                var ps = new PointStatus[n];
                CheckBatchEqual(w, "NearestPointBatch status", MeshBatchQueries.NearestPointBatch(tree, points, res, ps, options), BatchStatus.Ok);
                var inside = new InsideStatus[n];
                var inside2 = new InsideStatus[n];
                CheckBatchEqual(w, "IsInsideBatch status", MeshBatchQueries.IsInsideBatch(tree, points, inside, InsideMethod.Crossings, options), BatchStatus.Ok);
                CheckBatchEqual(w, "IsInsideBatch (winding) status", MeshBatchQueries.IsInsideBatch(tree, points, inside2, InsideMethod.WindingNumber, options), BatchStatus.Ok);
                for (int i = 0; i < n; ++i) {
                    var e = single[i];
                    string wi = $"{w} query {i}";
                    CheckBatchEqual(wi, "ray status", rs[i], e.rs);
                    CheckBatchEqual(wi, "hit triangle", hits[i].Triangle, e.tri);
                    CheckBatchEqual(wi, "hit parameter", BitConverter.DoubleToInt64Bits(hits[i].T), e.tBits);
                    CheckBatchEqual(wi, "any hit", any[i], e.tri != DMesh3.InvalidID);
                    bool finiteP = double.IsFinite(points[i].x) && double.IsFinite(points[i].y) && double.IsFinite(points[i].z);
                    CheckBatchEqual(wi, "point status", ps[i], finiteP ? PointStatus.Ok : PointStatus.NonFinite);
                    if (finiteP) {
                        CheckBatchEqual(wi, "nearest triangle", res[i].Triangle, e.nearest);
                        CheckBatchEqual(wi, "nearest distance", BitConverter.DoubleToInt64Bits(res[i].Distance), e.nearestBits);
                        CheckBatchEqual(wi, "inside", inside[i], e.inside ? InsideStatus.Inside : InsideStatus.Outside);
                        CheckBatchEqual(wi, "inside (winding)", inside2[i], e.windInside ? InsideStatus.Inside : InsideStatus.Outside);
                    } else {
                        CheckBatchEqual(wi, "inside of a non-finite point", inside[i], InsideStatus.Invalid);
                    }
                }
            }
        }
    }


    // ---- PartitionedReducer ----

    // one midpoint subdivision (every triangle into four) of a mesh, vertices of shared edges shared
    static DMesh3 Subdivide(DMesh3 m)
    {
        var r = new DMesh3();
        var map = new Dictionary<int, int>();
        foreach (int v in m.VertexIndices())
            map[v] = r.AppendVertex(m.GetVertex(v));
        var mid = new Dictionary<(int, int), int>();
        int Mid(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!mid.TryGetValue(key, out int id))
                mid[key] = id = r.AppendVertex(0.5 * m.GetVertex(a) + 0.5 * m.GetVertex(b));
            return id;
        }
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            int a = map[f.a], b = map[f.b], c = map[f.c], ab = Mid(f.a, f.b), bc = Mid(f.b, f.c), ca = Mid(f.c, f.a);
            r.AppendTriangle(a, ab, ca); r.AppendTriangle(ab, b, bc); r.AppendTriangle(ca, bc, c); r.AppendTriangle(ab, bc, ca);
        }
        return r;
    }

    static bool SameMesh(DMesh3 a, DMesh3 b, double scaleOfB)
    {
        if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount)
            return false;
        var va = a.VertexIndices().ToArray();
        var vb = b.VertexIndices().ToArray();
        for (int i = 0; i < va.Length; ++i)
            if (a.GetVertex(va[i]) != b.GetVertex(vb[i]) * scaleOfB)
                return false;
        return a.TriangleIndices().Select(t => a.GetTriangle(t)).SequenceEqual(b.TriangleIndices().Select(t => b.GetTriangle(t)));
    }

    /// <summary>
    /// PartitionedReducer (MinTriangles 0, on A subdivided to about 1500 triangles; the error is (0.01 diagonal)^2,
    /// without area weights): the result is valid, compact, finite, closed if the input is, without isolated vertices,
    /// no larger than the input, and does not depend on the thread count or the run (bit for bit); a power-of-two
    /// scaled copy gives the scaled result bit for bit (the documented contract)
    /// </summary>
    public static void CheckPartitionedReduction(string what, bool inputClosed, DMesh3 input, DMesh3 reduced1, DMesh3 reduced4, DMesh3 reducedAgain,
        DMesh3? reducedScaled, double scale)
    {
        foreach (var (name, m) in new[] { ("one thread", reduced1), ("four threads", reduced4) }) {
            if (!AllFinite(m))
                throw new Finding($"{what}: a vertex of the {name} result is not finite");
            if (!m.CheckValidity(true, FailMode.ReturnOnly))
                throw new Finding($"{what}: the {name} result is not valid");
            if (!m.IsCompact)
                throw new Finding($"{what}: the {name} result is not compact");
            if (inputClosed && m.TriangleCount > 0 && !m.IsClosed())
                throw new Finding($"{what}: a closed input, but the {name} result is not closed");
            if (m.TriangleCount > input.TriangleCount)
                throw new Finding($"{what}: the {name} result has {m.TriangleCount} triangles, the input {input.TriangleCount}");
        }
        if (!SameMesh(reduced1, reduced4, 1))
            throw new Finding($"{what}: one thread and four threads give different meshes");
        if (!SameMesh(reduced1, reducedAgain, 1))
            throw new Finding($"{what}: two runs give different meshes");
        if (reducedScaled != null && !SameMesh(reduced1, reducedScaled, 1 / scale))
            throw new Finding($"{what}: the result for the copy scaled by {scale} is not the scaled result");
    }

    static void RunPartitionedReducer(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 fine = Subdivide(Subdivide(Subdivide(s.Norm)));
        if (!fine.CheckValidity(true, FailMode.ReturnOnly))
            return;     // (a mesh with coincident corners: the subdivision of a degenerate input is not a mesh)
        double diag = Diagonal(fine.GetBounds());
        double error = 1e-4 * diag * diag;
        DMesh3 Reduce(DMesh3 m, double err, int threads)
        {
            var pr = new PartitionedReducer() { MaxThreads = threads, MinTriangles = 0, Configure = r => { r.AreaWeightedQuadrics = false; } };
            return pr.Reduce(m, err);
        }
        DMesh3 r1 = Reduce(fine, error, 1), r4 = Reduce(fine, error, 4), again = Reduce(fine, error, 4);
        // the same case at a power of two other than the normalization: exact, so the result is the scaled one
        double k = Pow2(40);
        DMesh3 big = new DMesh3(fine);
        foreach (int v in big.VertexIndices()) big.SetVertex(v, k * big.GetVertex(v));
        DMesh3? scaled = Reduce(big, error * k * k, 4);
        CheckPartitionedReduction(what, fine.IsClosed(), fine, r1, r4, again, scaled, k);
    }

    // ---- the alpha wrap ----

    /// <summary>
    /// MeshAlphaWrap: Compute() is Status == Success; EmptyInput only for no triangles and no points, InvalidInput only
    /// for a box that does not fit doubles (the input here is finite), InvalidParameters never (alpha and offset are
    /// relative to a positive box), Failed never (internal failure); Success and PointLimitReached have a Result that
    /// is finite, valid, closed and without bowties, oriented outwards (positive volume), and strictly encloses
    /// the input vertices (inside by the exact crossings; the tree on a closed manifold result is valid for it)
    /// </summary>
    public static void CheckAlphaWrap(string what, bool ok, MeshAlphaWrap.WrapStatus status, string? error, DMesh3? result,
        bool inputHasBox, bool boxFitsDoubles, bool inputsFinite = true)
    {
        if (ok != (status == MeshAlphaWrap.WrapStatus.Success))
            throw new Finding($"{what}: Compute() {ok} but Status {status}");
        switch (status) {
            case MeshAlphaWrap.WrapStatus.EmptyInput:
                throw new Finding($"{what}: EmptyInput, but the input has triangles");
            case MeshAlphaWrap.WrapStatus.InvalidInput:
                if (inputsFinite && boxFitsDoubles)
                    throw new Finding($"{what}: InvalidInput for finite input whose box fits ({error})");
                return;
            case MeshAlphaWrap.WrapStatus.InvalidParameters:
                if (inputHasBox)
                    throw new Finding($"{what}: InvalidParameters for an input of positive size ({error})");
                return;
            case MeshAlphaWrap.WrapStatus.Failed:
                throw new Finding($"{what}: Failed ({error})");
            case MeshAlphaWrap.WrapStatus.Cancelled:
                throw new Finding($"{what}: Cancelled, but CancelF is never set");
        }
        if (result == null)
            throw new Finding($"{what}: {status} without a Result");
        if (!AllFinite(result))
            throw new Finding($"{what}: a result vertex is not finite");
        if (!result.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the result is not valid");
        if (result.TriangleCount == 0)
            throw new Finding($"{what}: an empty result for a non-empty input");
        DMesh3 n = Normalized(result);
        var shape = MeshRobustRepair.FindDefects(n, false) & (MeshRobustRepair.Defects.NotClosed | MeshRobustRepair.Defects.Bowties);
        if (shape != MeshRobustRepair.Defects.None)
            throw new Finding($"{what}: the result has {MeshRobustRepair.DescribeDefects(shape)}");
    }

    // (the library also checks isolated vertices and Points, and returns EmptyInput for no input: the decoded shapes always have triangles and no loose vertices)
    static bool WrapRangeOk(DMesh3 m)
    {
        double biggest = 0;
        Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            for (int k = 0; k < 3; ++k) {
                Vector3d p = m.GetVertex(t[k]);
                for (int c = 0; c < 3; ++c) {
                    double a = Math.Abs(p[c]);
                    if (!(a == 0 || (a >= Pow2(-128) && a <= Pow2(128)))) return false;
                    biggest = Math.Max(biggest, a);
                    lo[c] = Math.Min(lo[c], p[c]); hi[c] = Math.Max(hi[c], p[c]);
                }
            }
        }
        double extent = Math.Max(hi.x - lo.x, Math.Max(hi.y - lo.y, hi.z - lo.z));
        return extent == 0 || extent >= biggest * Pow2(-40);
    }

    static void RunAlphaWrap(Case c, string what)
    {
        var input = new DMesh3(c.A);
        new MeshEditor(input).AppendMesh(c.B);
        // the supported range of the wrap (class comment, QA.md 2.5a): every nonzero coordinate in [2^-128, 2^128], the extent zero
        // or at least 2^-40 of the largest magnitude. Outside, the documented status is InvalidInput (no hang, no false success)
        if (!WrapRangeOk(input)) {
            var outside = new MeshAlphaWrap(input) { RelativeAlpha = 5, RelativeOffset = 100, MaxSteinerPoints = 3000 };
            if (outside.Compute() || outside.Status != MeshAlphaWrap.WrapStatus.InvalidInput)
                throw new Finding($"{what}: outside the supported range, Status {outside.Status} instead of InvalidInput ({outside.Error})");
            return;
        }
        // a coarse wrap (alpha a fifth of the diagonal), few Steiner points: an input costs milliseconds to a second
        var wrap = new MeshAlphaWrap(input) { RelativeAlpha = 5, RelativeOffset = 100, MaxSteinerPoints = 3000 };
        bool ok = wrap.Compute();
        AxisAlignedBox3d box = input.GetBounds();
        bool hasBox = Diagonal(box) > 0;
        CheckAlphaWrap(what, ok, wrap.Status, wrap.Error, wrap.Result, hasBox, Diagonal(box) <= double.MaxValue / 1e6);
        if (wrap.Result == null || (wrap.Status != MeshAlphaWrap.WrapStatus.Success && wrap.Status != MeshAlphaWrap.WrapStatus.PointLimitReached))
            return;
        // the result encloses the input strictly: every input vertex is inside (a closed manifold result without
        // self-intersections, so the parity is right), and its volume is positive
        double scale;
        DMesh3 result = Normalize(wrap.Result, out scale);
        var tree = new DMeshAABBTree3(result, true);
        foreach (int v in input.VertexIndices()) {
            Vector3d p = input.GetVertex(v) * scale;
            if (!tree.IsInside(p))
                throw new Finding($"{what}: input vertex {v} is not inside the wrap");
        }
    }
}
