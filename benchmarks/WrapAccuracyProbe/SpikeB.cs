// Spike B: inputs that are solids with holes (and an open sheet for contrast). Which part of the wrap is made-up surface, how it relates to
// the input's boundary loops, and the added / lost surface share of the wrap, the wrap after snap back, MeshAutoRepair, MeshRobustRepair.
//   added share = area share of the result farther than tau from the input   (result surface on no input surface)
//   lost share  = area share of the input farther than tau from the result   (input surface on no result surface)
//   tau = 4 offsets (the same for all four, so they compare); both from n = 60000 area-stratified samples (deterministic).
// Inputs: sheet | hole:<solid>:<kind>[:<param>]   solid = box|cylinder|sphere
//   kinds: face (box: the whole top face), p:<share> (patch grown around the top center / the pole), e:<share> (patch around a vertical box
//   edge: non-planar rim), slit (a strip one cell wide and 3 long in the top face)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class SpikeB
{
    public static void Build(string spec, out DMesh3 open, out DMesh3 solid, out double removedArea)
    {
        removedArea = 0; solid = null;
        if (spec == "sheet") { open = Inputs.BentSheet(); return; }
        if (!spec.StartsWith("hole:")) { open = Inputs.Load(spec); return; }          // a mesh file (open): no solid, no removed area
        string[] p = spec.Split(':');                       // hole, solid, kind, param
        string s = p[1], kind = p[2];
        solid = Inputs.FineSolid(s);
        open = new DMesh3(solid, true);
        AxisAlignedBox3d bb = solid.GetBounds();
        Vector3d top = new Vector3d(bb.Center.x, bb.Center.y, bb.Max.z);
        if (kind == "face") removedArea = Inputs.RemoveSlit(open, top, 2, 1e-9 * bb.DiagonalLength, 1e9);
        else if (kind == "p") removedArea = Inputs.RemovePatch(open, top, double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
        else if (kind == "e") removedArea = Inputs.RemovePatch(open, new Vector3d(bb.Min.x, bb.Min.y, bb.Center.z), double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
        else if (kind == "slit") removedArea = Inputs.RemoveSlit(open, new Vector3d(Math.Floor(bb.Center.x) + 0.5, bb.Center.y, bb.Max.z), 0, 0.4, 3);
        else throw new ArgumentException(spec);
    }

    public static List<Dictionary<string, object>> Run(string spec, double alphaDen, double offsetRel, double limit)
    {
        string name = spec.StartsWith("hole:") || spec == "sheet" ? spec.Replace(':', '_') : Inputs.NameOf(spec);
        Build(spec, out DMesh3 open, out DMesh3 solid, out double removed);
        double diag = Inputs.Diag(open), alpha = diag / alphaDen, offset = diag * offsetRel, tau = 4 * offset;
        var inTree = new DMeshAABBTree3(open, true);
        Vector3d origin = open.GetBounds().Center;
        double inArea = Metrics.Area(open);
        double solidVol = solid != null ? Metrics.SignedVolume(solid, origin) : double.NaN;
        var common = new Dictionary<string, object> { ["input"] = name, ["alpha_den"] = alphaDen, ["offset_rel"] = offsetRel, ["diag"] = diag };
        var rows = new List<Dictionary<string, object>>();
        Func<string, DMesh3, Dictionary<string, object>> row = (variant, res) => {
            var r = new Dictionary<string, object>(common) { ["variant"] = variant };
            r["input_tris"] = open.TriangleCount; r["input_boundary_loops"] = Metrics.BoundaryLoopCount(open); r["hole_area_share"] = removed / (inArea + removed);
            if (res == null) return r;
            var resTree = new DMeshAABBTree3(res, true);
            r["tris"] = res.TriangleCount;
            r["added_share"] = Metrics.FarShare(res, inTree, tau, 60000, out double mx1);
            r["added_max_dist"] = mx1;
            r["lost_share"] = Metrics.FarShare(open, resTree, tau, 60000, out double mx2);
            r["lost_max_dist"] = mx2;
            r["verdict"] = Metrics.Verdict(res);
            r["closed"] = res.IsClosed();
            if (res.IsClosed()) r["vol_ratio"] = Metrics.SignedVolume(res, origin) / solidVol;
            Patches(res, inTree, open, tau, 2 * alpha + tau, removed, r);
            return r;
        };

        SpikeA.WrapRun wr = SpikeA.Wrap(open, alpha, offset, limit);
        if (wr.Result == null) {
            var r = new Dictionary<string, object>(common) { ["variant"] = "wrap", ["status"] = wr.Status, ["ms"] = wr.Ms };
            rows.Add(r);
            return rows;
        }
        var w = row("wrap", wr.Result); w["status"] = wr.Status; w["ms"] = wr.Ms;
        // deep interior share: of the wrap's volume, the part farther than tau from the input (a sheet's wrap is a thin shell: 0)
        w["deep_volume_share"] = DeepShare(wr.Result, inTree, tau);
        rows.Add(w);
        var st = new BackStats();
        DMesh3 safe = SnapBack.Safe(wr.Result, open, inTree, diag, new SnapBack.Options(), st);
        var s = row("snap", safe); s["snap_ms"] = st.Ms; s["moved_full"] = (double)st.Full / st.Vertices; s["moved_none"] = (double)st.None / st.Vertices;
        s["selfint"] = SpikeA.SafeSelfInt(safe);
        rows.Add(s);

        try {
            var sw = Stopwatch.StartNew();
            var ar = new MeshAutoRepair(new DMesh3(open, true));
            bool ok = ar.Apply();
            var a = row("auto", ar.Mesh); a["ms"] = sw.Elapsed.TotalMilliseconds; a["ok"] = ok; rows.Add(a);
        } catch (Exception e) { var a = row("auto", null); a["status"] = "exception " + e.GetType().Name; rows.Add(a); }
        try {
            var sw = Stopwatch.StartNew();
            var rr = new MeshRobustRepair(new DMesh3(open, true));
            bool ok = rr.Apply();
            var a = row("robust", rr.Mesh); a["ms"] = sw.Elapsed.TotalMilliseconds; a["ok"] = ok; a["result_path"] = rr.ResultPath.ToString(); a["status"] = rr.ResultReason; rows.Add(a);
        } catch (Exception e) { var a = row("robust", null); a["status"] = "exception " + e.GetType().Name; rows.Add(a); }
        return rows;
    }

    // share of the wrap's interior (uniform samples in its box) that is farther than tau from the input
    static double DeepShare(DMesh3 wrap, DMeshAABBTree3 inTree, double tau)
    {
        var tree = new DMeshAABBTree3(wrap, true);
        AxisAlignedBox3d bb = wrap.GetBounds();
        var rnd = new Random(7);
        int inside = 0, deep = 0;
        for (int k = 0; k < 20000; ++k) {
            var p = new Vector3d(bb.Min.x + rnd.NextDouble() * bb.Width, bb.Min.y + rnd.NextDouble() * bb.Height, bb.Min.z + rnd.NextDouble() * bb.Depth);
            if (!tree.IsInside(p)) continue;
            inside++;
            inTree.FindNearestTriangle(p, out double d2);
            if (Math.Sqrt(d2) > tau) deep++;
        }
        return inside == 0 ? 0 : (double)deep / inside;
    }

    // far triangles (centroid farther than tau from the input) grouped by edge adjacency: patches. A patch is matched to a hole if one of its
    // vertices is within `radius` of a vertex of an input boundary edge.
    static void Patches(DMesh3 res, DMeshAABBTree3 inTree, DMesh3 input, double tau, double radius, double holeArea, Dictionary<string, object> r)
    {
        var far = new HashSet<int>();
        foreach (int tid in res.TriangleIndices()) {
            // far if at least 2 of 4 points (the centroid and three points near the corners) are farther than tau (a centroid alone misses big cap triangles)
            Index3i tv = res.GetTriangle(tid);
            Vector3d a = res.GetVertex(tv.a), b = res.GetVertex(tv.b), c = res.GetVertex(tv.c);
            int nfar = 0;
            foreach (Vector3d q in new[] { (a + b + c) / 3, (4 * a + b + c) / 6, (a + 4 * b + c) / 6, (a + b + 4 * c) / 6 }) {
                inTree.FindNearestTriangle(q, out double d2);
                if (Math.Sqrt(d2) > tau) nfar++;
            }
            if (nfar >= 2) far.Add(tid);
        }
        // boundary vertices of the input, hashed on cells of `radius`
        var cells = new Dictionary<(long, long, long), List<Vector3d>>();
        Func<Vector3d, (long, long, long)> key = p => ((long)Math.Floor(p.x / radius), (long)Math.Floor(p.y / radius), (long)Math.Floor(p.z / radius));
        foreach (int eid in input.BoundaryEdgeIndices()) {
            Index2i ev = input.GetEdgeV(eid);
            foreach (int vid in new[] { ev.a, ev.b }) {
                Vector3d p = input.GetVertex(vid);
                var k = key(p);
                if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<Vector3d>();
                l.Add(p);
            }
        }
        var seen = new HashSet<int>();
        int comps = 0, matched = 0; double areaAll = 0, areaUnmatched = 0, biggest = 0;
        foreach (int start in far) {
            if (seen.Contains(start)) continue;
            comps++;
            var stack = new Stack<int>(); stack.Push(start); seen.Add(start);
            double area = 0; bool match = false;
            while (stack.Count > 0) {
                int t = stack.Pop();
                area += res.GetTriArea(t);
                Index3i tv = res.GetTriangle(t);
                if (!match && cells.Count > 0)
                    foreach (int vid in new[] { tv.a, tv.b, tv.c }) {
                        Vector3d p = res.GetVertex(vid); var k = key(p);
                        for (int dx = -1; dx <= 1 && !match; ++dx) for (int dy = -1; dy <= 1 && !match; ++dy) for (int dz = -1; dz <= 1 && !match; ++dz)
                            if (cells.TryGetValue((k.Item1 + dx, k.Item2 + dy, k.Item3 + dz), out var l))
                                foreach (var q in l) if (q.Distance(p) <= radius) { match = true; break; }
                        if (match) break;
                    }
                Index3i nb = res.GetTriNeighbourTris(t);
                for (int j = 0; j < 3; ++j) { int n = nb[j]; if (n >= 0 && far.Contains(n) && seen.Add(n)) stack.Push(n); }
            }
            areaAll += area; if (area > biggest) biggest = area;
            if (match) matched++; else areaUnmatched += area;
        }
        r["patches"] = comps; r["patches_matched"] = matched; r["patch_area"] = areaAll; r["patch_area_unmatched"] = areaUnmatched;
        r["patch_over_hole_area"] = holeArea > 0 ? areaAll / holeArea : double.NaN;
        r["largest_patch_over_hole_area"] = holeArea > 0 ? biggest / holeArea : double.NaN;
    }
}
