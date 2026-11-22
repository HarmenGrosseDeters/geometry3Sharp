// The upper-bound measurement of docs/research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md: what a rounding with a proven
// guarantee (Devillers, Lazard, Lenhart, "3D snap rounding", SoCG 2018) could gain over SafeRounding, and what it costs,
// before anything of it is built. One JSON line per case (`bound <case>`):
//   - today: SafeRounding clean or not, removed components, time with and without the rounding;
//   - the gain side: the components SafeRounding removed, each with its thickness in units of the grid a guaranteed
//     scheme would use (a solid thinner than one cell collapses in step 1 of the paper as well);
//   - the cost side: a lower bound of the output size of the paper's algorithm, counted and not built. Its step 2 cuts
//     every face by the side walls x = c +- 1/2 of every "thin slab" (a grid column c that holds a vertex), so a face
//     ends as at least one triangle per slab of positive width between two thin slabs that it crosses with a chord of
//     two cells or more (such a piece cannot collapse to an edge in step 4). Only the mesh's own vertices define slabs
//     here; the paper adds those of the floor and back-wall arrangements, so the count is a lower bound;
//   - the same count on the patch around the defects (three rings), the size a local variant would start from.
// Nothing here is exact arithmetic: columns and chords are computed in doubles, which is enough for a count.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

static class GuaranteeBound
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // the family "scale": two cube-spheres, the second rotated, shifted and smaller, so that the result has no
    // axis-parallel structure; the size is set by the resolution
    internal static (DMesh3 a, DMesh3 b, Ops op) ScaleCase(int resolution, Ops op)
    {
        DMesh3 a = new Sphere3Generator_NormalizedCube() { EdgeVertices = resolution, Radius = 1 }.Generate().MakeDMesh();
        DMesh3 b = new Sphere3Generator_NormalizedCube() { EdgeVertices = resolution, Radius = 0.8 }.Generate().MakeDMesh();
        MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23));
        MeshTransforms.Translate(b, new Vector3d(0.6, 0.3, 0.2));
        return (a, b, op);
    }

    // the family "drop" (cases of the SolidBooleanPipeline audit, 2026-11-22): valid small or thin solids.
    //   drop:cube:<e>          the union of the box [0,1024]^3 and a cube of edge 2^-e one unit beside it
    //   drop:slab:<k>:<frame>  [0,2]^3 intersected with [2-h,4] x [0,2]^2, h = 2^-k: a slab of thickness h;
    //                          frame "axis", or "rot": both operands under one generic rotation
    internal static (DMesh3 a, DMesh3 b, Ops op) DropCase(string[] p)
    {
        if (p[1] == "cube") {
            double e = Math.Pow(2, -int.Parse(p[2]));
            return (Box(0, 0, 0, 1024, 1024, 1024), Box(1025, 0, 0, 1025 + e, e, e), Ops.Union);
        }
        double h = Math.Pow(2, -int.Parse(p[2]));
        DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(2 - h, 0, 0, 4, 2, 2);
        if (p[3] == "rot") {
            Quaterniond q = Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.81).Normalized, 23);
            MeshTransforms.Rotate(a, Vector3d.Zero, q);
            MeshTransforms.Rotate(b, Vector3d.Zero, q);
        }
        return (a, b, Ops.Intersection);
    }

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        var x = new[] { x0, x1 }; var y = new[] { y0, y1 }; var z = new[] { z0, z1 };
        for (int i = 0; i < 8; i++) m.AppendVertex(new Vector3d(x[i & 1], y[(i >> 1) & 1], z[(i >> 2) & 1]));
        int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
        for (int i = 0; i < f.Length; i += 3) m.AppendTriangle(f[i], f[i + 1], f[i + 2]);
        return m;
    }

    internal static string Run(string name, DMesh3 a, DMesh3 b, Ops op)
    {
        var sb = new StringBuilder();
        sb.Append("{\"case\":").Append(System.Text.Json.JsonSerializer.Serialize(name));
        // a first run takes the JIT, then one timed run without and one with the rounding
        var warm = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        if (!warm.Compute())
            return sb.Append(",\"ok\":false,\"error\":").Append(System.Text.Json.JsonSerializer.Serialize(warm.Error ?? "")).Append('}').ToString();
        var clock = Stopwatch.StartNew();
        var plain = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { SafeRounding = false };
        bool plainOk = plain.Compute();
        double msPlain = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var safe = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        if (!safe.Compute() || !plainOk)
            return sb.Append(",\"ok\":false,\"error\":\"second run failed\"}").ToString();
        double msSafe = clock.Elapsed.TotalMilliseconds;
        DMesh3 result = safe.Result, raw = plain.Result;
        MeshRoundingRepair r = safe.Rounding;
        string reasons = gs.MeshRobustRepair.NotCleanReasons(result);
        sb.Append(",\"ok\":true,\"tris\":").Append(result.TriangleCount).Append(",\"plain_tris\":").Append(raw.TriangleCount);
        sb.Append(",\"ms_plain\":").Append(msPlain.ToString("F1", ci)).Append(",\"ms_safe\":").Append(msSafe.ToString("F1", ci));
        sb.Append(",\"ms_rounding\":").Append((safe.PhaseMilliseconds.TryGetValue("rounding", out long msRounding) ? msRounding : 0));
        sb.Append(",\"plain_clean\":").Append(gs.MeshRobustRepair.NotCleanReasons(raw) == "" ? "true" : "false");
        sb.Append(",\"rounding_clean\":").Append(r.IsClean ? "true" : "false");
        sb.Append(",\"clean\":").Append(r.IsClean && reasons == "" ? "true" : "false");
        sb.Append(",\"limit\":").Append(r.EvaluationLimitReached ? "true" : "false");
        sb.Append(",\"removed_components\":").Append(r.RemovedComponents).Append(",\"removed_tris\":").Append(r.RemovedTriangles);
        sb.Append(",\"scale_disparity\":").Append(r.ScaleDisparity);
        sb.Append(",\"edits\":").Append(r.Collapses + r.Flips + r.Moves + r.Zips + r.VertexRemovals);
        sb.Append(",\"max_drift\":").Append(r.MaxDrift.ToString("R", ci)).Append(",\"max_length\":").Append(r.MaxLength.ToString("R", ci));
        if (raw.TriangleCount == 0)
            return sb.Append('}').ToString();

        // the grid of BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md section 2.3: the smallest power of two g with
        // g^2 / 2 >= DegenerateArea * D_a^2 (a grid triangle that is not collinear passes the area test); it fits the
        // drift bound if the paper's 3/2 cells in the maximum norm, (3/2) sqrt(3) g, stay within MaxLength
        AxisAlignedBox3d box = gs.MeshRepairMetrics.TriangleBounds(raw);
        double da = box.DiagonalLength, largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
        double g = Math.Pow(2, Math.Ceiling(Math.Log(Math.Sqrt(2e-12) * da, 2)));
        sb.Append(",\"diag\":").Append(da.ToString("R", ci)).Append(",\"grid\":").Append(g.ToString("R", ci));
        sb.Append(",\"grid_fits_drift\":").Append(1.5 * Math.Sqrt(3) * g <= r.MaxLength ? "true" : "false");
        sb.Append(",\"grid_representable\":").Append(g >= Program.Ulp(largest) ? "true" : "false");

        // the gain side: what SafeRounding removed. A component of the plainly rounded result none of whose triangles
        // (as three positions) is in the repaired result counts as removed (a heuristic: a small component whose every
        // triangle was edited counts too), with its thickness in cells of g
        var kept = new HashSet<(Vector3d, Vector3d, Vector3d)>();
        foreach (int t in result.TriangleIndices()) kept.Add(Key(result, t));
        var comps = new MeshConnectedComponents(raw);
        comps.FindConnectedT();
        var removed = new List<string>();
        var removedAt = new List<(Vector3d centre, double volume)>();
        int thinnerThanCell = 0, components = comps.Count;
        foreach (var comp in comps) {
            bool gone = true;
            var cb = AxisAlignedBox3d.Empty;
            foreach (int t in comp.Indices) {
                Index3i tv = raw.GetTriangle(t);
                for (int k = 0; k < 3; k++) cb.Contain(raw.GetVertex(tv[k]));
                if (kept.Contains(Key(raw, t))) gone = false;
            }
            // (the volume relative to a point of the component: relative to the origin a sliver's volume is cancellation)
            Vector3d o = raw.GetVertex(raw.GetTriangle(comp.Indices[0]).a);
            double volume = 0, area = 0;
            foreach (int t in comp.Indices) {
                Index3i tv = raw.GetTriangle(t);
                Vector3d pa = raw.GetVertex(tv.a) - o, pb = raw.GetVertex(tv.b) - o, pc = raw.GetVertex(tv.c) - o;
                volume += pa.Dot(pb.Cross(pc)) / 6;
                area += 0.5 * (pb - pa).Cross(pc - pa).Length;
            }
            // the thickness of a plate (2 volume / area) or the width of a needle (area / twice its length), the smaller
            double thickness = area > 0 ? Math.Min(2 * Math.Abs(volume) / area, area / (2 * cb.MaxDim)) : 0;
            if (thickness < g) thinnerThanCell++;
            if (gone) removedAt.Add((cb.Center, Math.Abs(volume)));
            if (gone)
                removed.Add("{\"tris\":" + comp.Indices.Length + ",\"thickness_cells\":" + (thickness / g).ToString("G4", ci)
                    + ",\"box_max_cells\":" + (cb.MaxDim / g).ToString("G4", ci) + "}");
        }
        sb.Append(",\"components\":").Append(components).Append(",\"components_thinner_than_cell\":").Append(thinnerThanCell);
        sb.Append(",\"removed\":[").Append(string.Join(",", removed)).Append(']');

        if (r.RemovedComponents > 0) {
            // the cheap alternative to a guarantee: the same repair on the plainly rounded result with the removal of
            // tiny and thin components switched off (outside the boolean, so without its fixed input vertices: a probe).
            // Clean then means the removed components could have stayed as valid solids
            var keepMesh = new DMesh3(raw);
            var keep = new MeshRoundingRepair(keepMesh) { SharedPositionsTouch = true, FixedWhereCoarse = false, RemoveTinyComponents = false };
            keep.Apply();
            var kc = new MeshConnectedComponents(keepMesh);
            kc.FindConnectedT();
            sb.Append(",\"no_removal_clean\":").Append(keep.IsClean && gs.MeshRobustRepair.NotCleanReasons(keepMesh) == "" ? "true" : "false");
            sb.Append(",\"no_removal_components\":").Append(kc.Count);
            // what became of each removed component: the volume of the probe's component nearest to it, over its own
            // (1: kept as it was; another value: the edits changed the solid, eg a cube inflated to a tetrahedron)
            var ratios = new List<string>();
            foreach (var (centre, volume) in removedAt) {
                double best = double.MaxValue, bestVolume = 0;
                foreach (var comp in kc) {
                    var cb = AxisAlignedBox3d.Empty;
                    Vector3d o = keepMesh.GetVertex(keepMesh.GetTriangle(comp.Indices[0]).a);
                    double v = 0;
                    foreach (int t in comp.Indices) {
                        Index3i tv = keepMesh.GetTriangle(t);
                        Vector3d pa = keepMesh.GetVertex(tv.a), pb = keepMesh.GetVertex(tv.b), pc = keepMesh.GetVertex(tv.c);
                        cb.Contain(pa); cb.Contain(pb); cb.Contain(pc);
                        v += (pa - o).Dot((pb - o).Cross(pc - o)) / 6;
                    }
                    double d = (cb.Center - centre).Length;
                    if (d < best) { best = d; bestVolume = Math.Abs(v); }
                }
                ratios.Add(volume > 0 ? (bestVolume / volume).ToString("G6", ci) : "null");
            }
            sb.Append(",\"no_removal_volume_ratio\":[").Append(string.Join(",", ratios)).Append(']');
        }
        if (result.TriangleCount == 0)
            return sb.Append('}').ToString();
        // the cost side, on the whole result and on the patch around the defects
        AppendSlabs(sb, "", result, result.TriangleIndices().ToArray(), g);
        var defects = new HashSet<int>(SnapFallback.DefectTriangles(result));
        sb.Append(",\"defect_tris\":").Append(defects.Count);
        if (defects.Count > 0) {
            for (int ring = 0; ring < 3; ring++)
                foreach (int t in defects.ToArray()) {
                    Index3i tv = result.GetTriangle(t);
                    for (int k = 0; k < 3; k++)
                        foreach (int u in result.VtxTrianglesItr(tv[k])) defects.Add(u);
                }
            int[] patch = defects.ToArray();
            AppendSlabs(sb, "patch_", result, patch, g);
            // step 1 of the paper merges what lies closer than one cell (along one axis): the pairs of patch triangles
            // with no common position and a distance below g end as coincident or connected sheets, which the clean
            // test does not accept. (Distances in doubles; a count, not a decision.)
            if (patch.Length <= 4000) {
                var tri = new Triangle3d[patch.Length];
                var tb = new AxisAlignedBox3d[patch.Length];
                for (int i = 0; i < patch.Length; i++) {
                    Index3i tv = result.GetTriangle(patch[i]);
                    tri[i] = new Triangle3d(result.GetVertex(tv.a), result.GetVertex(tv.b), result.GetVertex(tv.c));
                    tb[i] = new AxisAlignedBox3d(tri[i].V0, tri[i].V0);
                    tb[i].Contain(tri[i].V1); tb[i].Contain(tri[i].V2);
                    tb[i].Expand(g);
                }
                int close = 0;
                var inClose = new bool[patch.Length];
                for (int i = 0; i < patch.Length; i++)
                    for (int j = i + 1; j < patch.Length; j++) {
                        if (!tb[i].Intersects(tb[j]) || SharePosition(tri[i], tri[j])) continue;
                        if (new DistTriangle3Triangle3(tri[i], tri[j]).GetSquared() < g * g) { close++; inClose[i] = inClose[j] = true; }
                    }
                sb.Append(",\"patch_close_pairs\":").Append(close).Append(",\"patch_close_tris\":").Append(inClose.Count(x => x));
            }
        }
        return sb.Append('}').ToString();
    }

    // per axis taken as the paper's x: the thin slabs (grid columns with a vertex of the triangles given) and the lower
    // bound of the output triangles; "fine" is the limit of a grid as fine as the coordinates (every distinct
    // coordinate its own thin slab, every piece counted), the size the paper's remark 11 calls independent of the grid
    static void AppendSlabs(StringBuilder sb, string prefix, DMesh3 m, int[] tris, double g)
    {
        long best = long.MaxValue, bestFine = long.MaxValue, bestSlabs = 0;
        for (int axis = 0; axis < 3; axis++) {
            var (slabs, bound) = SlabBound(m, tris, axis, g);
            var (_, fine) = SlabBound(m, tris, axis, 0);
            if (bound < best) { best = bound; bestSlabs = slabs; }
            bestFine = Math.Min(bestFine, fine);
        }
        sb.Append(",\"").Append(prefix).Append("in_tris\":").Append(tris.Length);
        sb.Append(",\"").Append(prefix).Append("thin_slabs\":").Append(bestSlabs);
        sb.Append(",\"").Append(prefix).Append("out_tris_min\":").Append(best);
        sb.Append(",\"").Append(prefix).Append("out_tris_min_fine\":").Append(bestFine);
    }

    // The slabs of the triangles' vertices along an axis, and the number of (triangle, thick slab) pieces that cannot
    // collapse: the slab lies between two thin slabs at least two columns apart inside the triangle's range (positive
    // width), and the triangle's chord in the slab's middle plane is two cells or longer in the maximum norm of the
    // other two coordinates (one of its two wall edges is then two cells long, so its ends snap to two voxel centres,
    // and the other wall lies in another column: a triangle that is not collinear). g = 0: every distinct coordinate is
    // a thin slab and every piece counts.
    internal static (long slabs, long bound) SlabBound(DMesh3 m, int[] tris, int axis, double g)
    {
        var set = new HashSet<double>();
        foreach (int t in tris) {
            Index3i tv = m.GetTriangle(t);
            for (int k = 0; k < 3; k++) set.Add(Column(m.GetVertex(tv[k])[axis], g));
        }
        double[] cols = set.ToArray();
        Array.Sort(cols);
        double minGap = g > 0 ? 1.5 * g : 0, minChord = 2 * g;
        int u = (axis + 1) % 3, w = (axis + 2) % 3;
        long bound = 0;
        var p = new Vector3d[3];
        foreach (int t in tris) {
            Index3i tv = m.GetTriangle(t);
            double lo = double.MaxValue, hi = double.MinValue;
            for (int k = 0; k < 3; k++) {
                p[k] = m.GetVertex(tv[k]);
                double c = Column(p[k][axis], g);
                lo = Math.Min(lo, c); hi = Math.Max(hi, c);
            }
            int i0 = Array.BinarySearch(cols, lo), i1 = Array.BinarySearch(cols, hi);
            for (int i = i0; i < i1; i++) {
                if (cols[i + 1] - cols[i] <= minGap) continue;
                if (g == 0) { bound++; continue; }
                double x = 0.5 * (cols[i] + cols[i + 1]);
                // the chord of the triangle in the plane axis = x
                double u0 = 0, w0 = 0, u1 = 0, w1 = 0; int found = 0;
                for (int k = 0; k < 3 && found < 2; k++) {
                    Vector3d a = p[k], b = p[(k + 1) % 3];
                    if ((a[axis] < x) == (b[axis] < x)) continue;
                    double s = (x - a[axis]) / (b[axis] - a[axis]);
                    double cu = a[u] + s * (b[u] - a[u]), cw = a[w] + s * (b[w] - a[w]);
                    if (found++ == 0) { u0 = cu; w0 = cw; } else { u1 = cu; w1 = cw; }
                }
                if (found == 2 && Math.Max(Math.Abs(u1 - u0), Math.Abs(w1 - w0)) >= minChord) bound++;
            }
        }
        return (cols.Length, bound);
    }

    static bool SharePosition(Triangle3d a, Triangle3d b)
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (a[i] == b[j]) return true;
        return false;
    }

    // a triangle as its three positions, rotated so that the smallest comes first (the orientation is kept)
    static (Vector3d, Vector3d, Vector3d) Key(DMesh3 m, int t)
    {
        Index3i tv = m.GetTriangle(t);
        Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
        if (Less(b, a) && Less(b, c)) return (b, c, a);
        if (Less(c, a) && Less(c, b)) return (c, a, b);
        return (a, b, c);
    }

    static bool Less(Vector3d p, Vector3d q) => p.x != q.x ? p.x < q.x : p.y != q.y ? p.y < q.y : p.z < q.z;

    // A hand-counted case for the slab count (`bound selfcheck`): with g = 1, a triangle over the columns 0 to 8 and a
    // second one over 3 to 5 far away in y. Thin slabs 0, 3, 5, 8. The first crosses the thick slabs (0,3), (3,5) and
    // (5,8) with chords 6.5, 4 and 1.5 (two count), the second (3,5) with a chord of 2 (counts): 3 on the grid, and 4
    // with every piece counted. Then a second configuration for the slabs without width.
    internal static int SelfCheck()
    {
        var m = new DMesh3();
        int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(8, 0, 0)), c = m.AppendVertex(new Vector3d(0, 8, 0));
        int d = m.AppendVertex(new Vector3d(3, 100, 0)), e = m.AppendVertex(new Vector3d(3, 104, 0)), f = m.AppendVertex(new Vector3d(5, 100, 0));
        m.AppendTriangle(a, b, c);
        m.AppendTriangle(d, f, e);
        int[] tris = m.TriangleIndices().ToArray();
        var grid = SlabBound(m, tris, 0, 1);
        var fine = SlabBound(m, tris, 0, 0);
        // a third triangle inside column 4: the thick slabs (3,4) and (4,5) have no width and are skipped on the grid
        // (1 piece is left, the first triangle's in (0,3)); with every piece counted they are 4 + 2
        m.AppendTriangle(m.AppendVertex(new Vector3d(4, 200, 0)), m.AppendVertex(new Vector3d(4, 204, 0)), m.AppendVertex(new Vector3d(4, 202, 3)));
        tris = m.TriangleIndices().ToArray();
        var grid2 = SlabBound(m, tris, 0, 1);
        var fine2 = SlabBound(m, tris, 0, 0);
        bool ok = grid == (4, 3) && fine == (4, 4) && grid2 == (5, 1) && fine2 == (5, 6);
        Console.WriteLine($"selfcheck: slabs {grid.slabs}, pieces {grid.bound} (expected 4, 3); fine {fine.bound} (expected 4); "
            + $"with a column between: slabs {grid2.slabs}, pieces {grid2.bound} (expected 5, 1); fine {fine2.bound} (expected 6): {(ok ? "ok" : "FAILED")}");
        return ok ? 0 : 1;
    }

    // the centre of the grid column of a coordinate (the coordinate itself for g = 0)
    static double Column(double x, double g) => g > 0 ? Math.Round(x / g) * g : x;
}
