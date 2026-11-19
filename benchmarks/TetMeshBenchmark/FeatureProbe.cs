// P4(b) phase 1 probe (docs/design/TET_MESHING.md 17.14): what the featured surface (S1-F) gives against the rounded wrap (S1-R) on sharp solids.
//   ... -- feature [relativeAlpha] [relativeOffset]
// Per solid: S1-R = MeshTetrahedralization at the defaults (boundary of the result); S1-F = the raw wrap after AlphaWrapFeaturePass.
// Crease deviation = distance of 16 points per sharp input edge (> 30 degrees) to the surface, in offsets (max / mean).
// Recovery proxy = how many Steiner points the splitting of missing surface edges / facets at midpoints needs (what 14.2 would do),
// counted on a fresh Delaunay3 of the surface's vertices; it changes the surface only by splitting (no validity claim).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class FeatureProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static DMesh3 Box(double sx, double sy, double sz) =>
        new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, new Vector3d(sx / 2, sy / 2, sz / 2)) }.Generate().MakeDMesh();

    // a triangular prism (wedge): apex angle degrees at the x axis edge
    static DMesh3 Wedge(double apexDegrees)
    {
        double h = 1, w = h * Math.Tan(apexDegrees * Math.PI / 360) * 2;
        var m = new DMesh3();
        Vector3d[] p = { new Vector3d(-w / 2, 0, 0), new Vector3d(w / 2, 0, 0), new Vector3d(0, h, 0), new Vector3d(-w / 2, 0, 2), new Vector3d(w / 2, 0, 2), new Vector3d(0, h, 2) };
        foreach (var v in p) m.AppendVertex(v);
        int[][] t = { new[] { 0, 2, 1 }, new[] { 3, 4, 5 }, new[] { 0, 1, 4 }, new[] { 0, 4, 3 }, new[] { 1, 2, 5 }, new[] { 1, 5, 4 }, new[] { 2, 0, 3 }, new[] { 2, 3, 5 } };
        foreach (var f in t) m.AppendTriangle(f[0], f[1], f[2]);
        return m;
    }

    static DMesh3 Transform(DMesh3 m, Quaterniond q, Vector3d shift, double scale)
    {
        var r = new DMesh3(m, true);
        foreach (int v in r.VertexIndices()) r.SetVertex(v, q * (r.GetVertex(v) * scale) + shift);
        return r;
    }

    static IEnumerable<KeyValuePair<string, DMesh3>> Solids()
    {
        yield return new KeyValuePair<string, DMesh3>("cube", Box(1, 1, 1));
        yield return new KeyValuePair<string, DMesh3>("box 3x2x1", Box(3, 2, 1));
        yield return new KeyValuePair<string, DMesh3>("wedge 60", Wedge(60));
        yield return new KeyValuePair<string, DMesh3>("wedge 30", Wedge(30));
        yield return new KeyValuePair<string, DMesh3>("box rotated", Transform(Box(3, 2, 1), new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37), Vector3d.Zero, 1));
        yield return new KeyValuePair<string, DMesh3>("box offset", Transform(Box(3, 2, 1), Quaterniond.Identity, new Vector3d(1000.25, -300.5, 50), 1));
        yield return new KeyValuePair<string, DMesh3>("box x2^-10", Transform(Box(3, 2, 1), Quaterniond.Identity, Vector3d.Zero, 1.0 / 1024));
        yield return new KeyValuePair<string, DMesh3>("box x2^10", Transform(Box(3, 2, 1), Quaterniond.Identity, Vector3d.Zero, 1024));
        foreach (string n in new[] { "lshape", "octant", "slot", "vnotch", "chamfer" })
            yield return new KeyValuePair<string, DMesh3>(n, Procedural.Make(n));
    }

    // sample points on the input's sharp edges
    static List<bool> sampleConcave = new List<bool>();
    static List<Vector3d> CreaseSamples(DMesh3 m, double angleDegrees)
    {
        var s = new List<Vector3d>();
        sampleConcave = new List<bool>();
        double cosA = Math.Cos(angleDegrees * Math.PI / 180);
        foreach (int e in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(e);
            if (et.b == DMesh3.InvalidID) continue;
            if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) > cosA) continue;
            Index2i ev = m.GetEdgeV(e);
            Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
            Index2i op = m.GetEdgeOpposingV(e);
            // concave if the opposite vertex of one face lies behind the plane of the other (outward normals)
            bool concave = m.GetTriNormal(et.a).Dot(m.GetVertex(op.b) - a) < 0 ? false : true;
            for (int i = 0; i <= 16; ++i) { s.Add(a + (b - a) * (i / 16.0)); sampleConcave.Add(concave); }
        }
        return s;
    }

    // max / mean over the samples of convex (concave = false) or concave creases, in offsets; NaN if there are none
    static void CreaseDev(DMesh3 surface, List<Vector3d> samples, double offset, bool concave, out double max, out double mean)
    {
        var tree = new DMeshAABBTree3(surface, true);
        max = 0; mean = 0; int n = 0;
        for (int i = 0; i < samples.Count; ++i) {
            if (sampleConcave[i] != concave) continue;
            int t = tree.FindNearestTriangle(samples[i]);
            double d = Math.Sqrt(MeshQueries.TriDistanceSqr(surface, t, samples[i])) / offset;
            max = Math.Max(max, d); mean += d; n++;
        }
        if (n == 0) { max = mean = double.NaN; } else mean /= n;
    }
    static string Dev(DMesh3 s, List<Vector3d> smp, double off)
    {
        CreaseDev(s, smp, off, false, out double a, out double b); CreaseDev(s, smp, off, true, out double c, out double d);
        return a.ToString("G3", Inv) + "/" + b.ToString("G3", Inv) + " " + c.ToString("G3", Inv) + "/" + d.ToString("G3", Inv);
    }

    // surface dihedral (interior angle between the two faces, degrees) of every edge: smallest, and the number below 90
    static void SurfaceAngles(DMesh3 m, out double min, out int below90)
    {
        min = 180; below90 = 0;
        foreach (int e in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(e);
            if (et.b == DMesh3.InvalidID) continue;
            double a = 180 - Math.Acos(Math.Max(-1, Math.Min(1, m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b))))) * 180 / Math.PI;   // 180 = flat, convex or concave alike
            // interior dihedral: 180 - turn if convex, 180 + turn if concave; the sign is not needed for the missing-facet question, the turn is
            if (a < min) min = a;
            if (a < 90) below90++;
        }
    }

    // fresh Delaunay3 of the surface's vertices; ids[v] = Delaunay3 vertex id; counts surface triangles / edges missing
    static Delaunay3 Build(DMesh3 s, out Dictionary<int, int> ids, double floor)
    {
        var box = s.CachedBounds;
        double pad = box.DiagonalLength;
        var dt = new Delaunay3(new AxisAlignedBox3d(box.Min - new Vector3d(pad, pad, pad), box.Max + new Vector3d(pad, pad, pad))) { RefinementSupport = true };
        ids = new Dictionary<int, int>();
        int hint = -1;
        foreach (int v in s.VertexIndices()) {
            var r = dt.Insert(s.GetVertex(v), hint, 0, out int dv);
            ids[v] = dv;
        }
        return dt;
    }

    static void Missing(DMesh3 s, Delaunay3 dt, Dictionary<int, int> ids, out int missTris, out int missEdges, List<int> triList = null, List<int> edgeList = null)
    {
        missTris = missEdges = 0;
        foreach (int t in s.TriangleIndices()) {
            Index3i tv = s.GetTriangle(t);
            if (!dt.IsFacet(ids[tv.a], ids[tv.b], ids[tv.c], out _, out _)) { missTris++; triList?.Add(t); }
        }
        foreach (int e in s.EdgeIndices()) {
            Index2i ev = s.GetEdgeV(e);
            if (!dt.IsEdge(ids[ev.a], ids[ev.b], out _)) { missEdges++; edgeList?.Add(e); }
        }
    }

    // proxy recovery: split the longest edge of every missing facet and every missing edge at its midpoint, rebuild, until nothing is missing
    static string Recover(DMesh3 s0, double floor, int budget, out bool ok, out int steiner, out int rounds)
    {
        var s = new DMesh3(s0, true);
        int v0 = s.VertexCount;
        steiner = 0; rounds = 0; ok = false;
        double floorSqr = floor * floor;
        int blocked = 0;
        for (; rounds < 25; ++rounds) {
            var dt = Build(s, out var ids, floor);
            var tl = new List<int>(); var el = new List<int>();
            Missing(s, dt, ids, out int mt, out int me, tl, el);
            if (mt == 0 && me == 0) { ok = true; break; }
            var toSplit = new HashSet<int>(el);
            foreach (int t in tl) {
                Index3i te = s.GetTriEdges(t);
                int best = te.a; double bl = -1;
                for (int k = 0; k < 3; ++k) {
                    Index2i ev = s.GetEdgeV(te[k]);
                    double l = s.GetVertex(ev.a).DistanceSquared(s.GetVertex(ev.b));
                    if (l > bl) { bl = l; best = te[k]; }
                }
                toSplit.Add(best);
            }
            int done = 0;
            foreach (int e in toSplit.OrderBy(x => x)) {
                if (!s.IsEdge(e)) continue;
                Index2i ev = s.GetEdgeV(e);
                if (s.GetVertex(ev.a).DistanceSquared(s.GetVertex(ev.b)) < 4 * floorSqr) { blocked++; continue; }   // the floor: no point closer than floor to an endpoint
                if (s.SplitEdge(e, out _) == MeshResult.Ok) { done++; steiner++; }
            }
            if (done == 0 || s.VertexCount - v0 > budget) break;
        }
        return "blocked_by_floor=" + blocked;
    }

    static double MinAngle(DMesh3 m, int t)
    {
        m.GetTriVertices(t, ref ta, ref tb, ref tc);
        double mn = 10;
        for (int k = 0; k < 3; ++k) {
            Vector3d p = k == 0 ? ta : k == 1 ? tb : tc, q = k == 0 ? tb : k == 1 ? tc : ta, r = k == 0 ? tc : k == 1 ? ta : tb;
            Vector3d u = (q - p).Normalized, v = (r - p).Normalized;
            mn = Math.Min(mn, Math.Acos(Math.Max(-1, Math.Min(1, u.Dot(v)))));
        }
        return mn;
    }
    static Vector3d ta, tb, tc;

    // option G: flip surface edges that are missing in the Delaunay3 of the vertices when the flipped edge is present, the turn stays small
    // and the angles stay above 10 degrees; repeated until nothing changes
    static int FlipToDelaunay(DMesh3 s, out int missTris, out int missEdges)
    {
        int flips = 0;
        for (int round = 0; round < 30; ++round) {
            var dt = Build(s, out var ids, 0);
            var el = new List<int>();
            Missing(s, dt, ids, out missTris, out missEdges, null, el);
            int done = 0;
            foreach (int e in el) {
                if (!s.IsEdge(e)) continue;
                Index2i et = s.GetEdgeT(e); if (et.b == DMesh3.InvalidID) continue;
                Index2i op = s.GetEdgeOpposingV(e);
                if (!dt.IsEdge(ids[op.a], ids[op.b], out _)) continue;
                Vector3d n0 = s.GetTriNormal(et.a), n1 = s.GetTriNormal(et.b);
                if (n0.Dot(n1) < Math.Cos(20 * Math.PI / 180)) continue;           // only (nearly) flat quads: the surface does not change
                if (s.FlipEdge(e, out _) == MeshResult.Ok) {
                    bool bad = false;
                    foreach (int t in new[] { et.a, et.b }) { if (MinAngle(s, t) < 10 * Math.PI / 180) bad = true; }
                    if (bad) { s.FlipEdge(e, out _); continue; }
                    done++;
                }
            }
            flips += done;
            if (done == 0) break;
        }
        var dt2 = Build(s, out var ids2, 0);
        Missing(s, dt2, ids2, out missTris, out missEdges);
        return flips;
    }

    // option C: the wrap with a local alpha near the input's creases (alpha / 4 on them, slope 0.25), wrap surface only
    static string LocalAlphaNearCreases(DMesh3 input, List<Vector3d> samples, double ra, double ro, double offsetRef, out int tris)
    {
        var w0 = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = ro }; w0.Compute();
        double a0 = w0.UsedAlpha;
        var pts = samples.Where((v, i) => i % 4 == 0).ToArray();
        Func<Vector3d, double> f = p => { double d2 = double.MaxValue; foreach (var q in pts) d2 = Math.Min(d2, p.DistanceSquared(q)); return Math.Min(a0, a0 / 4 + 0.25 * Math.Sqrt(d2)); };
        var sw = Stopwatch.StartNew();
        var w = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = ro, AlphaField = f }; w.Compute();
        double t = sw.Elapsed.TotalSeconds;
        tris = w.Result == null ? 0 : w.Result.TriangleCount;
        if (w.Result == null) return w.Status.ToString();
        return Dev(w.Result, samples, offsetRef) + " tris=" + tris + " vs " + w0.Result.TriangleCount + " time_s=" + t.ToString("G3", Inv);
    }

    public static void Run(double ra, double ro)
    {
        bool recover = Environment.GetEnvironmentVariable("G3_PROBE_RECOVER") != "0";
        Console.WriteLine("input,mode,status,tets,time_s,crease_dev_convex_max/mean_concave_max/mean_in_offsets,min_dihedral,p01_dihedral,surf_tris,missing_tris,missing_edges,extra");
        foreach (var kv in Solids()) {
            DMesh3 input = kv.Value;
            var samples = CreaseSamples(input, 30);
            var sw = Stopwatch.StartNew();
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = ra, RelativeOffset = ro };
            tm.Compute();
            double tR = sw.Elapsed.TotalSeconds;
            double offset = tm.UsedOffset;
            if (tm.Result == null) { Console.WriteLine(kv.Key + ",S1-R," + tm.Status); continue; }
            DMesh3 bR = tm.Result.ToSurfaceMesh();
            var qs = Dihedrals(tm.Result);
            Console.WriteLine(string.Join(",", kv.Key, "S1-R", tm.Status, tm.Result.Tets.Count, tR.ToString("G4", Inv), Dev(bR, samples, offset),
                qs.min.ToString("G4", Inv), qs.p01.ToString("G4", Inv), bR.TriangleCount, "", "", "wrap_tris=" + tm.Wrap.Result.TriangleCount + " alpha/offset=" + (tm.UsedAlpha / offset).ToString("G3", Inv)));
            // the raw wrap's own surface (before the tets' boundary slide), for reference
            Console.WriteLine(string.Join(",", kv.Key, "wrap", "", "", "", Dev(tm.Wrap.Result, samples, offset), "", "", tm.Wrap.Result.TriangleCount, "", "", ""));
            // S1-F: the raw wrap + the feature pass
            sw.Restart();
            var wrap = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = ro };
            wrap.Compute();
            var fp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), input, wrap.UsedAlpha, wrap.UsedOffset);
            fp.Compute();
            DMesh3 sF = fp.Mesh;
            double tF = sw.Elapsed.TotalSeconds;
            var dt = Build(sF, out var ids, 0);
            Missing(sF, dt, ids, out int mt, out int me);
            double floor = 0.125 * Math.Min(wrap.UsedOffset, wrap.UsedAlpha - wrap.UsedOffset);
            string note = "creases=" + fp.InputCreases + " snaps=" + fp.CreaseSnaps + " splits=" + fp.Splits + " flips=" + fp.Flips;
            Console.WriteLine(string.Join(",", kv.Key, "S1-F surface", "", "", tF.ToString("G4", Inv), Dev(sF, samples, offset), "", "", sF.TriangleCount, mt, me, note));
            // option G: Delaunay flips on the featured surface
            var g = new DMesh3(sF, true);
            int nf = FlipToDelaunay(g, out int gmt, out int gme);
            Console.WriteLine(string.Join(",", kv.Key, "S1-F + flips", "", "", "", Dev(g, samples, offset), "", "", g.TriangleCount, gmt, gme, "flips=" + nf));
            // the recovery proxy: midpoint splits (the 14.2 rule without encroachment bookkeeping), budget 8 x the vertex count
            if (recover) {
                sw.Restart();
                string rn = Recover(g, floor, 8 * g.VertexCount, out bool ok, out int st, out int rounds);
                Console.WriteLine(string.Join(",", kv.Key, "recovery proxy", ok ? "converged" : "NOT converged", "", sw.Elapsed.TotalSeconds.ToString("G3", Inv), "", "", "", "", "", "", "steiner=" + st + " rounds=" + rounds + " " + rn));
            }
            // option C: local alpha near the creases
            Console.WriteLine(string.Join(",", kv.Key, "local alpha at creases", "", "", "", LocalAlphaNearCreases(input, samples, ra, ro, offset, out _), "", "", "", "", "", ""));
        }
    }

    static (double min, double p01) Dihedrals(TetMesh3 m)
    {
        var d = new List<double>();
        foreach (var t in m.Tets)
            if (TetMeshQuality.Measure(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c], m.Vertices[t.d], out _, out double mn, out _)) d.Add(mn);
        d.Sort();
        return d.Count == 0 ? (0, 0) : (d[0], d[(int)(0.01 * (d.Count - 1))]);
    }
}
