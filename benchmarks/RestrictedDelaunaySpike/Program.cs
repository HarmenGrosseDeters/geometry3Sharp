// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Stage 1a spike runner (docs/design/CERTIFIED_REMESHING.md, section 11). Procedural inputs only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using gs;

namespace RestrictedDelaunaySpike
{
    static class Program
    {
        static DMesh3 Sphere(int n, Func<Vector3d, Vector3d> f)
        {
            var m = new Sphere3Generator_NormalizedCube() { EdgeVertices = n, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, f(m.GetVertex(v).Normalized));
            return m;
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        // a symmetric lens: two spherical caps (unit sphere radius, polar half-angle theta) glued at their rim circle;
        // the interior angle of the crease at the rim is psi = 2 theta. nu around, nphi rings per cap
        static DMesh3 Lens(double psiDeg, int nu, int nphi)
        {
            double theta = psiDeg * Math.PI / 360;
            var m = new DMesh3();
            int top = m.AppendVertex(new Vector3d(0, 0, 1 - Math.Cos(theta)));
            var rings = new List<int[]>();
            for (int k = 1; k <= 2 * nphi - 1; ++k) {
                // k < nphi: upper cap ring at phi = theta k / nphi; k == nphi: rim; k > nphi: lower cap
                double phi = theta * (k <= nphi ? k : 2 * nphi - k) / nphi;
                double z = (Math.Cos(phi) - Math.Cos(theta)) * (k <= nphi ? 1 : -1);
                var ring = new int[nu];
                for (int i = 0; i < nu; ++i) {
                    double u = 2 * Math.PI * (i + 0.5 * (k % 2)) / nu;
                    ring[i] = m.AppendVertex(new Vector3d(Math.Sin(phi) * Math.Cos(u), Math.Sin(phi) * Math.Sin(u), z));
                }
                rings.Add(ring);
            }
            int bottom = m.AppendVertex(new Vector3d(0, 0, -(1 - Math.Cos(theta))));
            for (int i = 0; i < nu; ++i) m.AppendTriangle(top, rings[0][i], rings[0][(i + 1) % nu]);
            for (int k = 0; k + 1 < rings.Count; ++k) {
                int[] r0 = rings[k], r1 = rings[k + 1];
                bool odd = (k + 1) % 2 == 1;   // ring k+1 (1-based k+2) is shifted by half a step against ring k
                for (int i = 0; i < nu; ++i) {
                    int i1 = (i + 1) % nu;
                    if (!odd) { m.AppendTriangle(r0[i], r1[i], r0[i1]); m.AppendTriangle(r0[i1], r1[i], r1[i1]); }
                    else { m.AppendTriangle(r0[i], r1[(i + nu - 1) % nu], r1[i]); m.AppendTriangle(r0[i], r1[i], r0[i1]); }
                }
            }
            int[] last = rings[rings.Count - 1];
            for (int i = 0; i < nu; ++i) m.AppendTriangle(bottom, last[(i + 1) % nu], last[i]);
            return m;
        }

        static void Rotate(DMesh3 m)
        {
            var q = new Quaterniond(new Vector3d(0.3, 0.7, 0.2).Normalized, 23.7);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, q * m.GetVertex(v));
        }

        // the input vertices, greedily at least d apart (in vertex order)
        static List<Vector3d> InitialSample(DMesh3 m, double d)
        {
            var grid = new Dictionary<Vector3i, List<Vector3d>>();
            var res = new List<Vector3d>();
            Vector3i cell(Vector3d p) => new Vector3i((int)Math.Floor(p.x / d), (int)Math.Floor(p.y / d), (int)Math.Floor(p.z / d));
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Vector3i c = cell(p);
                bool near = false;
                for (int x = -1; x <= 1 && !near; ++x) for (int y = -1; y <= 1 && !near; ++y) for (int z = -1; z <= 1 && !near; ++z)
                    if (grid.TryGetValue(new Vector3i(c.x + x, c.y + y, c.z + z), out var l) && l.Any(q => q.Distance(p) < d)) near = true;
                if (near) continue;
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<Vector3d>();
                list.Add(p); res.Add(p);
            }
            return res;
        }

        static double MinAngleDeg(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
            return Math.Min(A, Math.Min(B, 180 - A - B));
        }

        static int Genus(DMesh3 m) => (2 - (m.VertexCount - m.EdgeCount + m.TriangleCount)) / 2;

        static string F(double x) => x.ToString("G4", CultureInfo.InvariantCulture);

        static void RunCase(string group, string name, DMesh3 input, double rmaxFrac)
        {
            Rotate(input);
            double diag = input.CachedBounds.Diagonal.Length;
            double rmax = rmaxFrac * diag, hmin = rmax / 16;
            var rdr = new Rdr(input) { RMax = rmax, HMin = hmin };
            bool done = rdr.Run(InitialSample(input, 4 * rmax));
            var rep = rdr.Check();
            DMesh3 D = rdr.Output(out int appendFailed);
            string notClean = MeshRobustRepair.NotCleanReason(D) ?? "";
            int comps = new MeshConnectedComponents(D).Let(c => { c.FindConnectedT(); return c.Count; });
            int compsIn = new MeshConnectedComponents(input).Let(c => { c.FindConnectedT(); return c.Count; });
            bool closed = D.IsClosed();
            bool homeo = closed && appendFailed == 0 && comps == compsIn && Genus(D) == Genus(input);
            var hd = new MeshHausdorffDistance(input, D) { Tolerance = 0.01 * rmax };
            hd.Compute();
            var angles = D.TriangleIndices().Select(t => MinAngleDeg(D, t)).OrderBy(a => a).ToList();
            double p01 = angles.Count > 0 ? angles[angles.Count / 100] : double.NaN, amin = angles.Count > 0 ? angles[0] : double.NaN;
            int uncert = rep.Corner + rep.MultiHit + rep.TooLarge + rep.BadUmbrella;
            Console.WriteLine($"{{\"group\":\"{group}\",\"case\":\"{name}\",\"input_tris\":{input.TriangleCount},\"rmax_over_diag\":{F(rmaxFrac)}," +
                $"\"done\":{(done ? "true" : "false")},\"inserted\":{rdr.Inserted},\"refused\":{rdr.Refused},\"failed\":{rdr.Failed}," +
                $"\"seconds\":{F(rdr.Seconds)},\"out_vertices\":{D.VertexCount},\"out_tris\":{D.TriangleCount},\"us_per_vertex\":{F(1e6 * (rdr.Seconds - rdr.RegionSeconds) / Math.Max(1, D.VertexCount))},\"region_check_seconds\":{F(rdr.RegionSeconds)}," +
                $"\"restricted\":{rep.Restricted},\"corner\":{rep.Corner},\"multi_hit\":{rep.MultiHit},\"too_large\":{rep.TooLarge},\"bad_ratio\":{rep.BadRatio},\"bad_umbrella\":{rep.BadUmbrella}," +
                $"\"uncertified_share\":{F((double)uncert / Math.Max(1, rep.Restricted))},\"append_failed\":{appendFailed},\"closed\":{(closed ? "true" : "false")}," +
                $"\"homeomorphic\":{(homeo ? "true" : "false")},\"not_clean\":\"{notClean}\",\"hausdorff_over_rmax\":{F(hd.UpperBound / rmax)}," +
                $"\"region_violations\":{rdr.RegionViolations},\"rounds\":{rdr.Rounds},\"far_violations\":{rdr.FarViolations},\"min_angle\":{F(amin)},\"p01_angle\":{F(p01)},\"bad_ratio_share\":{F((double)rep.BadRatio / Math.Max(1, rep.Restricted))}}}");
        }

        // item 3 of docs/research/GEOGRAM_MEASUREMENTS_2026-11.md: the true restricted Voronoi cells against the region proxy, on the
        // state before the first region round and on the final state
        static string Compare(Rdr rdr, DMesh3 input, double rmax, string when)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rdr.ProxyCheck();
            double proxySeconds = sw.Elapsed.TotalSeconds;
            var proxy = new Dictionary<int, (bool disk, bool farOk)>(rdr.ProxyVerdict);
            var rvc = new RvcCheck(input, rdr.Dt);
            rvc.Compute();
            int cells = 0, unseen = 0, trueNotDisk = 0, trueFar = 0, proxyNotDisk = 0, proxyFar = 0, missedDisk = 0, missedFar = 0, falseDisk = 0, falseFar = 0;
            int unseenNotDisk = 0, unseenFar = 0, pieces = 0;
            double area = 0, maxFar = 0;
            foreach (var kv in rvc.Cells) {
                var c = kv.Value;
                cells++; area += c.Area; pieces += c.Pieces; maxFar = Math.Max(maxFar, c.Far / rmax);
                bool tDisk = c.Disk, tFar = c.Far <= rmax;
                if (!tDisk) trueNotDisk++;
                if (!tFar) trueFar++;
                if (!proxy.TryGetValue(kv.Key, out var pv)) { unseen++; if (!tDisk) unseenNotDisk++; if (!tFar) unseenFar++; continue; }
                if (!pv.disk) proxyNotDisk++;
                if (!pv.farOk) proxyFar++;
                if (pv.disk && !tDisk) missedDisk++;
                if (!pv.disk && tDisk) falseDisk++;
                if (pv.farOk && !tFar) missedFar++;
                if (!pv.farOk && tFar) falseFar++;
            }
            int proxyOnly = proxy.Keys.Count(k => !rvc.Cells.ContainsKey(k));
            double sArea = input.TriangleIndices().Sum(t => input.GetTriArea(t));
            return $"\"{when}\":{{\"samples_with_cell\":{cells},\"pieces\":{pieces},\"area_ratio\":{F(area / sArea)},\"true_not_disk\":{trueNotDisk},\"true_far\":{trueFar}," +
                $"\"proxy_not_disk\":{proxyNotDisk},\"proxy_far\":{proxyFar},\"missed_disk\":{missedDisk},\"missed_far\":{missedFar},\"false_disk\":{falseDisk},\"false_far\":{falseFar}," +
                $"\"unseen\":{unseen},\"unseen_not_disk\":{unseenNotDisk},\"unseen_far\":{unseenFar},\"proxy_only\":{proxyOnly},\"max_far_over_rmax\":{F(maxFar)}," +
                $"\"proxy_seconds\":{F(proxySeconds)},\"rvc_seconds\":{F(rvc.Seconds)},\"clips\":{rvc.Clips},\"skipped_by_security_radius\":{rvc.ClipsSkippedBySecurityRadius}}}";
        }

        static void RunRvcCase(string group, string name, DMesh3 input, double rmaxFrac)
        {
            Rotate(input);
            double diag = input.CachedBounds.Diagonal.Length;
            double rmax = rmaxFrac * diag, hmin = rmax / 16;
            var rdr = new Rdr(input) { RMax = rmax, HMin = hmin };
            string before = null;
            rdr.BeforeRegionRound = round => { if (round == 0) before = Compare(rdr, input, rmax, "before_regions"); };
            bool done = rdr.Run(InitialSample(input, 4 * rmax));
            string after = Compare(rdr, input, rmax, "final");
            double edge = input.EdgeIndices().Average(e => { var ev = input.GetEdgeV(e); return input.GetVertex(ev.a).Distance(input.GetVertex(ev.b)); });
            Console.WriteLine($"{{\"group\":\"{group}\",\"case\":\"{name}\",\"input_tris\":{input.TriangleCount},\"mean_edge_over_rmax\":{F(edge / rmax)},\"done\":{(done ? "true" : "false")}," +
                $"\"samples\":{rdr.Dt.VertexCount - 8},\"rounds\":{rdr.Rounds},\"region_seconds\":{F(rdr.RegionSeconds)},\"seconds\":{F(rdr.Seconds)},{before ?? "\"before_regions\":null"},{after}}}");
        }

        static T2 Let<T1, T2>(this T1 x, Func<T1, T2> f) => f(x);

        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            string only = args.FirstOrDefault(a => a.StartsWith("only="))?.Substring(5);
            var cases = new List<(string group, string name, Func<DMesh3> make, double rmax)>();
            foreach (double fr in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 120 }) {
                string s = fr > 0.02 ? "d30" : fr > 0.01 ? "d60" : "d120";
                cases.Add(("smooth", "sphere_" + s, () => Sphere(64, p => p), fr));
                cases.Add(("smooth", "ellipsoid_" + s, () => Sphere(64, p => new Vector3d(p.x, 0.6 * p.y, 0.4 * p.z)), fr));
                cases.Add(("smooth", "bumpy_" + s, () => Sphere(80, p => p * (1 + 0.12 * Math.Sin(6 * p.x) * Math.Sin(6 * p.y) * Math.Sin(6 * p.z + 0.3))), fr));
                cases.Add(("smooth", "torus_" + s, () => Torus(1, 0.3, 240, 72), fr));
            }
            foreach (double psi in new[] { 15.0, 30, 45, 60, 90, 120, 135, 150, 160, 170, 175, 200, 240, 270, 300, 330 })
                cases.Add(("lens", "lens_psi" + psi, () => Lens(psi, 400, 60), 1.0 / 30));
            if (args.Contains("rvc")) {
                // self-test of the RVC topology: sphere with samples at the poles (two hemispheres: disks, chi 1); torus with three
                // samples on its centre circle (three tube sectors: annuli, chi 0, one component each)
                foreach (var (what, m, pts) in new (string, DMesh3, Vector3d[])[] {
                    ("sphere, 2 poles", Sphere(32, p => p), new[] { new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) }),
                    ("torus, 3 on the centre circle", Torus(1, 0.3, 120, 36), Enumerable.Range(0, 3).Select(k => new Vector3d(Math.Cos(2 * Math.PI * k / 3 + 0.1), Math.Sin(2 * Math.PI * k / 3 + 0.1), 0)).ToArray()) }) {
                    var box = m.CachedBounds; box.Expand(box.Diagonal.Length);
                    var dt = new Delaunay3(box);
                    foreach (var p in pts) dt.Insert(p, -1, 0, out _);
                    var rc = new RvcCheck(m, dt); rc.Compute();
                    Console.WriteLine("{\"selftest\":\"" + what + "\",\"cells\":[" + string.Join(",", rc.Cells.OrderBy(k => k.Key).Select(k => $"{{\"chi\":{k.Value.Chi},\"components\":{k.Value.Components},\"disk\":{(k.Value.Disk ? "true" : "false")}}}")) + "]}");
                }
                // the proxy's blind spot is features smaller than the input's edges: coarser spheres too (input edge up to r_max)
                foreach (double fr in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 120 }) {
                    string sfx = fr > 0.02 ? "d30" : fr > 0.01 ? "d60" : "d120";
                    foreach (int n in new[] { 8, 16, 32 }) cases.Add(("coarse", "sphere" + n + "_" + sfx, () => Sphere(n, p => p), fr));
                    cases.Add(("coarse", "bumpy24_" + sfx, () => Sphere(24, p => p * (1 + 0.12 * Math.Sin(6 * p.x) * Math.Sin(6 * p.y) * Math.Sin(6 * p.z + 0.3))), fr));
                    cases.Add(("coarse", "torus60x18_" + sfx, () => Torus(1, 0.3, 60, 18), fr));
                }
                foreach (var c in cases) {
                    if (c.group == "lens" && !new[] { "lens_psi45", "lens_psi60", "lens_psi90", "lens_psi300" }.Contains(c.name)) continue;
                    if (only != null && !System.Text.RegularExpressions.Regex.IsMatch(c.name, only)) continue;
                    RunRvcCase(c.group, c.name, c.make(), c.rmax);
                }
                return;
            }
            foreach (var c in cases) {
                if (only != null && !System.Text.RegularExpressions.Regex.IsMatch(c.name, only)) continue;
                RunCase(c.group, c.name, c.make(), c.rmax);
            }
        }
    }
}
