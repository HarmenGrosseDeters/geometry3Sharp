// Probe of the scale-free repair study (docs/design/ROBUST_REPAIR_SCALE_FREE.md, hard problem 9). Lives on the data branch
// runs/scale-free-study only; not merged. Three pieces:
//   - SceneStats: the part structure of the input (components, closed ones, the parts of the box rules, the size ratios);
//   - RepairByParts: the upper bound of option B, every part repaired by its own MeshRobustRepair (same settings), results appended;
//   - PartFidelity: deviations relative to each input component's own diagonal (the strict criterion of criteria.py is
//     relative to the scene's diagonal, so it cannot see a small part that is lost, eroded or blobbed).
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static class ScaleFreeProbe
{
    const int MaxComponents = 4096;     // above: a soup, no part statistics
    const int MaxParts = 512;           // above: no split (one part)

    sealed class Comp
    {
        public int[] Tris;
        public AxisAlignedBox3d Box;
        public bool Closed;
        public int MinTri;
    }

    static List<Comp> components(DMesh3 m)
    {
        var cc = new MeshConnectedComponents(m);
        cc.FindConnectedT();
        var list = new List<Comp>();
        foreach (var c in cc.Components) {
            var box = AxisAlignedBox3d.Empty;
            bool closed = true;
            int minTri = int.MaxValue;
            foreach (int t in c.Indices) {
                Index3i tv = m.GetTriangle(t);
                box.Contain(m.GetVertex(tv.a)); box.Contain(m.GetVertex(tv.b)); box.Contain(m.GetVertex(tv.c));
                Index3i te = m.GetTriEdges(t);
                if (m.IsBoundaryEdge(te.a) || m.IsBoundaryEdge(te.b) || m.IsBoundaryEdge(te.c))
                    closed = false;
                minTri = Math.Min(minTri, t);
            }
            list.Add(new Comp { Tris = c.Indices, Box = box, Closed = closed, MinTri = minTri });
        }
        list.Sort((a, b) => a.MinTri.CompareTo(b.MinTri));
        return list;
    }

    static int find(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }

    static bool meet(AxisAlignedBox3d a, AxisAlignedBox3d b)
    {
        return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y && a.Min.z <= b.Max.z && b.Min.z <= a.Max.z;
    }

    static AxisAlignedBox3d padded(AxisAlignedBox3d b, double f)
    {
        double d = f * b.DiagonalLength;
        return new AxisAlignedBox3d(b.Min - new Vector3d(d, d, d), b.Max + new Vector3d(d, d, d));
    }

    // the parts: "box" (stage 1 of option B): closed components linked when their boxes meet (closed intervals, exact); an open
    // component links everything (one part). "all" (stage 2 upper bound): every component linked by its box padded by 1e-3 of its
    // own diagonal. Parts in the order of their smallest triangle id; null if there are more than MaxComponents components
    static List<List<Comp>> parts(List<Comp> comps, string rule)
    {
        if (comps.Count > MaxComponents)
            return null;
        int n = comps.Count;
        var p = Enumerable.Range(0, n).ToArray();
        if (rule == "box" && comps.Any(c => c.Closed == false))
            return new List<List<Comp>> { comps };
        var boxes = comps.Select(c => rule == "all" ? padded(c.Box, 1e-3) : c.Box).ToArray();
        for (int i = 0; i < n; ++i)
            for (int j = i + 1; j < n; ++j)
                if (meet(boxes[i], boxes[j])) {
                    int a = find(p, i), b = find(p, j);
                    if (a != b) p[Math.Max(a, b)] = Math.Min(a, b);
                }
        var byRoot = new Dictionary<int, List<Comp>>();
        var order = new List<int>();
        for (int i = 0; i < n; ++i) {
            int r = find(p, i);
            if (byRoot.TryGetValue(r, out var l) == false) { byRoot[r] = l = new List<Comp>(); order.Add(r); }
            l.Add(comps[i]);
        }
        return order.Select(r => byRoot[r]).ToList();
    }

    static DMesh3 submesh(DMesh3 m, IEnumerable<Comp> comps)
    {
        var res = new DMesh3();
        var map = new Dictionary<int, int>();
        var tris = comps.SelectMany(c => c.Tris).OrderBy(t => t).ToList();
        foreach (int t in tris) {
            Index3i tv = m.GetTriangle(t);
            for (int k = 0; k < 3; ++k)
                if (map.ContainsKey(tv[k]) == false)
                    map[tv[k]] = res.AppendVertex(m.GetVertex(tv[k]));
            res.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
        }
        return res;
    }

    public static void SceneStats(DMesh3 m, Dictionary<string, object> r)
    {
        var comps = components(m);
        double diag = m.TriangleCount > 0 ? MeshRepairMetrics.TriangleBounds(m).DiagonalLength : 0;
        r["sc_components"] = comps.Count;
        r["sc_closed_components"] = comps.Count(c => c.Closed);
        r["sc_components_4"] = comps.Count(c => c.Tris.Length >= 4);
        var pb = parts(comps, "box");
        var pa = parts(comps, "all");
        r["sc_parts_box"] = pb == null ? -1 : pb.Count;
        r["sc_parts_all"] = pa == null ? -1 : pa.Count;
        if (diag > 0 && comps.Count > 0) {
            var d = comps.Select(c => c.Box.DiagonalLength / diag).ToList();
            r["sc_largest_rel"] = d.Max();
            r["sc_smallest_rel"] = d.Min();
            var dc = comps.Where(c => c.Closed && c.Tris.Length >= 4).Select(c => c.Box.DiagonalLength / diag).ToList();
            r["sc_smallest_closed_rel"] = dc.Count > 0 ? dc.Min() : double.NaN;
            // the far-part switch of MeshRobustRepair (global_scale_share > 0): two or more components and a triangle below 1e-12 diag^2
            double eps = 1e-12 * diag * diag;
            r["sc_disparity_switch"] = comps.Count >= 2 && m.TriangleIndices().Any(t => m.GetTriArea(t) < eps);
            // ... and whether that triangle is degenerate by its own component's diagonal too (a real degenerate triangle, not scale)
            int scaleOnly = 0;
            foreach (var c in comps) {
                double cd = c.Box.DiagonalLength, ceps = 1e-12 * cd * cd;
                foreach (int t in c.Tris) {
                    double a = m.GetTriArea(t);
                    if (a < eps && a >= ceps) scaleOnly++;
                }
            }
            r["sc_degenerate_by_scene_only"] = scaleOnly;
        }
        if (pb != null && pb.Count > 1 && diag > 0) {
            var sizes = pb.Select(pt => { var b = AxisAlignedBox3d.Empty; foreach (var c in pt) b.Contain(c.Box); return b.DiagonalLength / diag; }).ToList();
            r["sc_parts_box_smallest_rel"] = sizes.Min();
        }
    }

    static string path(MeshRobustRepair rr, bool ok)
    {
        string p = rr.UsedAlphaWrap ? "wrap" : rr.UsedSolidify ? "solidify" : rr.UsedExactResolve ? "exact" : "kept";
        return ok ? p : p + ":false:" + rr.ResultReason;
    }

    // option B's upper bound: each part of the rule repaired alone by a MeshRobustRepair with the settings of template, the results
    // appended in part order. null if there is one part (the caller repairs the whole as usual)
    public static DMesh3 RepairByParts(DMesh3 mesh, string rule, MeshRobustRepair template, Dictionary<string, object> r, out bool ok)
    {
        ok = false;
        var ps = parts(components(mesh), rule);
        r["parts_rule"] = rule;
        r["parts_n"] = ps == null ? -1 : ps.Count;
        if (ps == null || ps.Count <= 1 || ps.Count > MaxParts)
            return null;
        var result = new DMesh3();
        var paths = new List<string>();
        ok = true;
        int falseParts = 0;
        foreach (var pt in ps) {
            var rr = new MeshRobustRepair(submesh(mesh, pt));
            copy_settings(template, rr);
            bool partOk = rr.Apply();
            if (partOk == false) { ok = false; falseParts++; }
            paths.Add(path(rr, partOk));
            MeshEditor.Append(result, rr.Mesh);
        }
        r["parts_paths"] = paths;
        r["parts_false"] = falseParts;
        return result;
    }

    static void copy_settings(MeshRobustRepair a, MeshRobustRepair b)
    {
        b.ExactResolve = a.ExactResolve; b.ConfigureRepair = a.ConfigureRepair; b.SolidifyResolution = a.SolidifyResolution;
        b.PassThroughCleanParts = a.PassThroughCleanParts; b.ExactResolveRounds = a.ExactResolveRounds; b.AlphaWrapFallback = a.AlphaWrapFallback;
        b.SimplifyAlphaWrap = a.SimplifyAlphaWrap; b.AlphaWrapQualityPass = a.AlphaWrapQualityPass; b.ConfigureWrapQuality = a.ConfigureWrapQuality;
        b.WrapRelativeAlpha = a.WrapRelativeAlpha; b.WrapRelativeOffset = a.WrapRelativeOffset; b.WrapMaxEstimatedTriangles = a.WrapMaxEstimatedTriangles;
        b.WrapMaxAlphaCoarsening = a.WrapMaxAlphaCoarsening; b.SafeRounding = a.SafeRounding; b.CheckLostComponents = a.CheckLostComponents;
        b.RejectTopologyChange = a.RejectTopologyChange; b.SampleCellsInVolumeCheck = a.SampleCellsInVolumeCheck;
        b.SampleCellsChildBudget = a.SampleCellsChildBudget;
    }

    // deviations relative to each component's own diagonal: forward (the component's vertices to the result) and backward (the result's
    // vertices to the nearest input triangle, charged to that triangle's component). Components with at least 4 triangles; closed ones
    // separately (real parts)
    public static void PartFidelity(DMesh3 input, DMesh3 result, Dictionary<string, object> r)
    {
        var comps = components(input);
        if (comps.Count > MaxComponents || input.TriangleCount == 0) {
            r["pf_skipped"] = true;
            return;
        }
        var compOfTri = new Dictionary<int, int>();
        for (int i = 0; i < comps.Count; ++i)
            foreach (int t in comps[i].Tris) compOfTri[t] = i;
        var fwd = new double[comps.Count];
        var back = new double[comps.Count];
        DMeshAABBTree3 rt = result.TriangleCount > 0 ? new DMeshAABBTree3(result, true) : null;
        for (int i = 0; i < comps.Count; ++i) {
            var c = comps[i];
            double cd = c.Box.DiagonalLength;
            if (c.Tris.Length < 4 || !(cd > 0)) { fwd[i] = double.NaN; continue; }
            if (rt == null) { fwd[i] = double.PositiveInfinity; continue; }
            var vs = new SortedSet<int>();
            foreach (int t in c.Tris) { Index3i tv = input.GetTriangle(t); vs.Add(tv.a); vs.Add(tv.b); vs.Add(tv.c); }
            var arr = vs.ToArray();
            int stride = Math.Max(1, arr.Length / 64);
            double dmax = 0;
            for (int k = 0; k < arr.Length; k += stride) {
                Vector3d p = input.GetVertex(arr[k]);
                double dsq;
                int tid = rt.FindNearestTriangle(p, out dsq);
                dmax = Math.Max(dmax, tid < 0 ? double.PositiveInfinity : Math.Sqrt(dsq));
            }
            fwd[i] = dmax / cd;
        }
        if (rt != null) {
            var it = new DMeshAABBTree3(input, true);
            var rv = result.VertexIndices().ToArray();
            int stride = Math.Max(1, rv.Length / 20000);
            for (int k = 0; k < rv.Length; k += stride) {
                Vector3d p = result.GetVertex(rv[k]);
                double dsq;
                int tid = it.FindNearestTriangle(p, out dsq);
                if (tid < 0) continue;
                int ci = compOfTri[tid];
                double cd = comps[ci].Box.DiagonalLength;
                if (cd > 0) back[ci] = Math.Max(back[ci], Math.Sqrt(dsq) / cd);
            }
        }
        Func<int, bool> real = i => comps[i].Tris.Length >= 4 && comps[i].Box.DiagonalLength > 0;
        var closed = Enumerable.Range(0, comps.Count).Where(i => real(i) && comps[i].Closed).ToList();
        var all = Enumerable.Range(0, comps.Count).Where(real).ToList();
        r["pf_comps"] = all.Count;
        r["pf_closed"] = closed.Count;
        r["pf_fwd_max_closed"] = closed.Count > 0 ? closed.Max(i => fwd[i]) : 0;
        r["pf_back_max_closed"] = closed.Count > 0 ? closed.Max(i => back[i]) : 0;
        r["pf_fwd_off_closed"] = closed.Count(i => fwd[i] > 0.01);
        r["pf_back_off_closed"] = closed.Count(i => back[i] > 0.01);
        r["pf_lost_closed"] = closed.Count(i => fwd[i] > 0.25);
        r["pf_fwd_off_all"] = all.Count(i => fwd[i] > 0.01);
        r["pf_back_off_all"] = all.Count(i => back[i] > 0.01);
        r["pf_lost_all"] = all.Count(i => fwd[i] > 0.25);
        // the result's triangles below the degenerate area of their own component (option D's view: a scale-free degeneracy)
        if (result.TriangleCount > 0) {
            var rc = components(result);
            int degOwn = 0;
            foreach (var c in rc) {
                double cd = c.Box.DiagonalLength, eps = 1e-12 * cd * cd;
                degOwn += c.Tris.Count(t => result.GetTriArea(t) < eps);
            }
            r["after_degenerate_tris_own"] = degOwn;
        }
    }
}
