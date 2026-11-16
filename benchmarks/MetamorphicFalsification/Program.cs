// Falsification run of docs/research/METAMORPHIC_TESTING.md: apply metamorphic relations to the procedural repro of a known
// bug on the fixing commit's parent and report whether the relation exposes it. Uses only the base public API, so it
// compiles against old commits (copy this folder into a worktree of the parent). No mesh files: every input is procedural.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Program
{
    // The relations. A case maps a Xf (what to do to the input) to a signature string of its result mapped back to the
    // input frame, or throws. A relation is "caught" when the signature differs from the identity run, the run throws
    // where the identity run does not, or the cost exceeds the limit.
    public sealed class Xf
    {
        public string Name; public double Scale = 1; public int Sym = 0; public bool Perm; public bool Refine;
        public Vector3d Map(Vector3d p) => Back(Sym, p, true) * Scale;
        public Vector3d Unmap(Vector3d q) => Back(Sym, q / Scale, false);
        static readonly int[][] P = { new[] {0,1,2}, new[] {1,0,2}, new[] {2,1,0}, new[] {0,2,1}, new[] {1,2,0}, new[] {2,0,1} };
        // symmetry index s: perm P[s % 6], signs from bits of s / 6 (0..7); 48 in all
        public static Vector3d Back(int s, Vector3d p, bool fwd)
        {
            int[] pm = P[s % 6]; int sg = s / 6;
            double[] a = { p.x, p.y, p.z }; double[] q = new double[3];
            if (fwd) for (int i = 0; i < 3; ++i) q[i] = ((sg >> i & 1) != 0 ? -1 : 1) * a[pm[i]];
            else for (int i = 0; i < 3; ++i) q[pm[i]] = ((sg >> i & 1) != 0 ? -1 : 1) * a[i];
            return new Vector3d(q[0], q[1], q[2]);
        }
        public bool Reflects { get { int[] pm = P[Sym % 6]; int par = (pm[0] > pm[1] ? 1 : 0) + (pm[0] > pm[2] ? 1 : 0) + (pm[1] > pm[2] ? 1 : 0); int sg = Sym / 6;
            int neg = (sg & 1) + (sg >> 1 & 1) + (sg >> 2 & 1); return ((par % 2 == 0 ? 1 : -1) * (neg % 2 == 0 ? 1 : -1)) < 0; } }
        public DMesh3 Apply(DMesh3 m) {
            var r = new DMesh3(); var vs = m.VertexIndices().ToList(); var ts = m.TriangleIndices().ToList();
            if (Perm) { vs.Reverse(); ts.Reverse(); }
            var id = new Dictionary<int, int>(); foreach (int v in vs) id[v] = r.AppendVertex(Map(m.GetVertex(v)));
            foreach (int t in ts) { Index3i tr = m.GetTriangle(t); int a = id[tr.a], b = id[tr.b], c = id[tr.c]; if (Reflects) { int z = b; b = c; c = z; }
                if (Perm) r.AppendTriangle(b, c, a); else r.AppendTriangle(a, b, c); }
            return r; }
    }
    public static string Sig(IEnumerable<Vector3d[]> tris, Xf x) // set of triangles, mapped back, order independent
        => string.Join(";", tris.Select(t => { var u = t.Select(x.Unmap).ToArray(); int k = 0; for (int i = 1; i < 3; ++i) if (Cmp(u[i], u[k]) < 0) k = i;
            return string.Join(",", Enumerable.Range(0, 3).Select(i => u[(k + i) % 3]).Select(p => $"{p.x + 0.0:R}|{p.y + 0.0:R}|{p.z + 0.0:R}")); }).OrderBy(s => s, StringComparer.Ordinal));
    static int Cmp(Vector3d a, Vector3d b) { int c = a.x.CompareTo(b.x); if (c != 0) return c; c = a.y.CompareTo(b.y); return c != 0 ? c : a.z.CompareTo(b.z); }
    public static string Sig(DMesh3 m, Xf x) => Sig(m.TriangleIndices().Select(t => { var tr = m.GetTriangle(t); var v = new[] { m.GetVertex(tr.a), m.GetVertex(tr.b), m.GetVertex(tr.c) };
            if (x.Reflects) { var tmp = v[1]; v[1] = v[2]; v[2] = tmp; } return v; }), x);

    public sealed class Case
    {
        public string Issue, Name; public Func<Xf, string> Run;
        public Xf[] Scales, Syms; public bool NoPerm;
        // raw numbers printed for the note (not a relation): name -> text
        public List<(string name, Func<string> info)> Info = new List<(string, Func<string>)>();
        // extra relations of the case: name -> (xf-less) function returning "" when fine, else the violation
        public List<(string name, Func<string> check)> Extra = new List<(string, Func<string>)>();
    }

    static int Main(string[] args)
    {
        var cases = Cases.All();
        string only = args.Length > 0 ? args[0] : null;
        foreach (var c in cases) {
            if (only != null && !c.Issue.Contains(only)) continue;
            Console.WriteLine($"== {c.Issue} {c.Name}");
            string baseSig; var sw = Stopwatch.StartNew();
            try { try { c.Run(new Xf { Name = "identity" }); } catch { } sw.Restart(); baseSig = c.Run(new Xf { Name = "identity" }); } catch (Exception e) { Console.WriteLine($"  identity run THROWS {e.GetType().Name}: {Trim(e.Message)}  (crash oracle, no relation needed)"); baseSig = null; }
            long identityMs = sw.ElapsedMilliseconds; Console.WriteLine($"  identity {identityMs} ms");
            foreach (var grp in new[] { ("scale 2^k", c.Scales), ("symmetry", c.Syms), ("permutation", c.NoPerm ? null : new[] { new Xf { Name = "perm", Perm = true } }) }) {
                if (grp.Item2 == null) { Console.WriteLine($"  {grp.Item1,-12} n/a"); continue; }
                int diff = 0, thr = 0, slow = 0; string first = null; long maxMs = 0, minMs = identityMs;
                foreach (var x in grp.Item2) {
                    sw.Restart();
                    try { string s = c.Run(x); if (baseSig != null && s != baseSig) { diff++; first ??= x.Name + " differs"; } }
                    catch (Exception e) { thr++; first ??= x.Name + " throws " + e.GetType().Name + ": " + Trim(e.Message); }
                    long ms = sw.ElapsedMilliseconds; maxMs = Math.Max(maxMs, ms); minMs = Math.Min(minMs, ms);
                }
                maxMs = Math.Max(maxMs, identityMs);
                // cost relation: the same work, so the times of the group agree within a factor of 5 (absolute times only above 300 ms)
                if (maxMs > 300 && maxMs > 5 * Math.Max(1, minMs)) { slow = 1; first ??= $"cost spread {minMs}..{maxMs} ms"; }
                Console.WriteLine($"  {grp.Item1,-12} {(diff + thr + slow > 0 ? "CAUGHT" : "not caught")}  differs {diff}, throws {thr}, cost-spread {slow} of {grp.Item2.Length}; time {minMs}..{maxMs} ms; {first}");
            }
            foreach (var (n, f) in c.Info) { string r; try { r = f(); } catch (Exception e) { r = "throws " + Trim(e.Message); } Console.WriteLine($"  info {n}: {r}"); }
            foreach (var (n, f) in c.Extra) { string r; try { r = f(); } catch (Exception e) { r = "throws " + Trim(e.Message); } Console.WriteLine($"  {n,-12} {(r == "" ? "not caught" : "CAUGHT")} {r}"); }
        }
        return 0;
    }
    static string Trim(string s) => s.Length > 80 ? s.Substring(0, 80) : s;
}
