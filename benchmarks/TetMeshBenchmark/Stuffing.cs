// Spike prototype (docs/research/TET_DIHEDRAL_FLOOR_SPIKE_2026-11.md), NOT library code: uniform isosurface stuffing after
// F. Labelle, J. R. Shewchuk, "Isosurface Stuffing: Fast Tetrahedral Meshes with Good Dihedral Angles", SIGGRAPH 2007,
// implemented from the paper's text only (sections 3.1-3.3: BCC lattice, cut points, warping with alpha_long / alpha_short,
// the two diagonal rules; the stencils are derived from the rules, not copied from a table). It measures what a lattice
// mesher would give on the procedural inputs: worst dihedral angle against the paper's bound, tet count, deviation, time,
// next to MeshTetrahedralization on the same input.
//   dotnet run -c Release --project benchmarks/TetMeshBenchmark -- floor [relativeCell ...]        (default 20 40 80)
// The cut function is the signed distance to the input (sign from the fast winding number) shifted outward by
// diagonal / relativeOffset (G3_STUFF_OFFSET, default 600, as the wrap's offset; 0 = the input surface itself).
// G3_STUFF_ALPHA = "long,short" overrides the warp parameters (default 0.28511,0.39882: the paper's 10.78 degree row, "unsafe";
// 0.24999,0.41189 is the "safe" 9.31 degree row).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class Stuffing
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // the 7 positive edge directions in doubled lattice coordinates: 3 black (axis, length 1 cell), 4 red (diagonal, sqrt(3)/2 cell)
    static readonly int[][] Dirs = { new[] { 2, 0, 0 }, new[] { 0, 2, 0 }, new[] { 0, 0, 2 }, new[] { 1, 1, 1 }, new[] { 1, 1, -1 }, new[] { 1, -1, 1 }, new[] { 1, -1, -1 } };

    public sealed class Result
    {
        public TetMesh3 Mesh = new TetMesh3();
        public long LatticePoints, CutPoints, Warped, QuadZeroDiscarded, InvertedBackground, RuleConflicts;
        public double FieldSeconds, MeshSeconds;
    }

    sealed class Lattice
    {
        public int Dx, Dy, Dz;
        public Vector3d Origin; public double Half;       // position = Origin + Half * (i, j, k)
        public double[] F; public sbyte[] Sign; public Vector3d[] Pos; public double[] Cut;   // Cut[index * 7 + dir] = t from the lower point, NaN if none
        public int Index(int i, int j, int k) => i + Dx * (j + Dy * k);
        public bool Inside(int i, int j, int k) => i >= 0 && j >= 0 && k >= 0 && i < Dx && j < Dy && k < Dz;
        public Vector3d At(int i, int j, int k) => Origin + Half * new Vector3d(i, j, k);
    }

    public static Result Run(DMesh3 input, double cell, double offset, double alphaLong, double alphaShort, Func<Vector3d, double> cutFunction = null)
    {
        var res = new Result();
        var sw = Stopwatch.StartNew();
        var tree = new DMeshAABBTree3(input, true);
        tree.FastWindingNumber(Vector3d.Zero);
        AxisAlignedBox3d b = input.CachedBounds;
        double margin = offset + 2.5 * cell;
        var L = new Lattice { Half = cell / 2, Origin = b.Min - new Vector3d(margin, margin, margin) };
        L.Dx = 2 * (int)Math.Ceiling((b.Width + 2 * margin) / cell) + 1;
        L.Dy = 2 * (int)Math.Ceiling((b.Height + 2 * margin) / cell) + 1;
        L.Dz = 2 * (int)Math.Ceiling((b.Depth + 2 * margin) / cell) + 1;
        int n = L.Dx * L.Dy * L.Dz;
        L.F = new double[n]; L.Sign = new sbyte[n]; L.Pos = new Vector3d[n]; L.Cut = new double[n * 7];
        for (int i = 0; i < L.Cut.Length; ++i) L.Cut[i] = double.NaN;
        Func<Vector3d, double> f = cutFunction ?? (p => {
            double d = Math.Sqrt(MeshQueries.TriDistanceSqr(input, tree.FindNearestTriangle(p), p));
            return tree.FastWindingNumber(p) > 0.5 ? offset + d : offset - d;
        });
        // step 1: the cut function at every lattice point of the box (as the paper's prototype does)
        gParallel.BlockStartEnd(0, L.Dz - 1, (k0, k1) => {
            for (int k = k0; k <= k1; ++k)
                for (int j = 0; j < L.Dy; ++j)
                    for (int i = 0; i < L.Dx; ++i) {
                        if ((i & 1) != (j & 1) || (i & 1) != (k & 1)) continue;
                        int id = L.Index(i, j, k);
                        L.Pos[id] = L.At(i, j, k);
                        L.F[id] = f(L.Pos[id]);
                        L.Sign[id] = (sbyte)Math.Sign(L.F[id]);
                    }
        });
        res.FieldSeconds = sw.Elapsed.TotalSeconds;
        sw.Restart();
        // step 2: a cut point on every edge with a positive and a negative end, by bisection
        for (int k = 0; k < L.Dz; ++k)
            for (int j = 0; j < L.Dy; ++j)
                for (int i = 0; i < L.Dx; ++i) {
                    if ((i & 1) != (j & 1) || (i & 1) != (k & 1)) continue;
                    int id = L.Index(i, j, k);
                    res.LatticePoints++;
                    for (int d = 0; d < 7; ++d) {
                        int i2 = i + Dirs[d][0], j2 = j + Dirs[d][1], k2 = k + Dirs[d][2];
                        if (!L.Inside(i2, j2, k2)) continue;
                        int id2 = L.Index(i2, j2, k2);
                        if (L.Sign[id] * L.Sign[id2] >= 0) continue;
                        double lo = 0, hi = 1; bool loPos = L.Sign[id] > 0;
                        for (int it = 0; it < 40; ++it) {
                            double mid = 0.5 * (lo + hi);
                            if ((f(L.Pos[id] + mid * (L.Pos[id2] - L.Pos[id])) > 0) == loPos) lo = mid; else hi = mid;
                        }
                        L.Cut[id * 7 + d] = 0.5 * (lo + hi);
                        res.CutPoints++;
                    }
                }
        // step 3: warp every violated lattice point to its nearest violating cut point (lattice order; the 10.78 and 9.31 degree
        // rows of the paper's table 1 hold for any order)
        var unwarped = (Vector3d[])L.Pos.Clone();
        for (int k = 0; k < L.Dz; ++k)
            for (int j = 0; j < L.Dy; ++j)
                for (int i = 0; i < L.Dx; ++i) {
                    if ((i & 1) != (j & 1) || (i & 1) != (k & 1)) continue;
                    int id = L.Index(i, j, k);
                    if (L.Sign[id] == 0) continue;
                    double best = double.MaxValue; Vector3d bestP = Vector3d.Zero;
                    for (int d = 0; d < 14; ++d) {
                        int[] dir = Dirs[d % 7]; int s = d < 7 ? 1 : -1;
                        int i2 = i + s * dir[0], j2 = j + s * dir[1], k2 = k + s * dir[2];
                        if (!L.Inside(i2, j2, k2)) continue;
                        int id2 = L.Index(i2, j2, k2);
                        double t = s > 0 ? L.Cut[id * 7 + d] : L.Cut[id2 * 7 + d - 7];
                        if (double.IsNaN(t)) continue;
                        if (s < 0) t = 1 - t;                                   // the fraction of the edge from this point
                        double len = (d % 7) < 3 ? cell : cell * Math.Sqrt(3) / 2;
                        if (t < ((d % 7) < 3 ? alphaLong : alphaShort) && t * len < best) { best = t * len; bestP = unwarped[id] + t * (unwarped[id2] - unwarped[id]); }
                    }
                    if (best == double.MaxValue) continue;
                    L.Pos[id] = bestP; L.Sign[id] = 0; res.Warped++;
                    for (int d = 0; d < 14; ++d) {
                        int[] dir = Dirs[d % 7]; int s = d < 7 ? 1 : -1;
                        int i2 = i + s * dir[0], j2 = j + s * dir[1], k2 = k + s * dir[2];
                        if (!L.Inside(i2, j2, k2)) continue;
                        if (s > 0) L.Cut[id * 7 + d] = double.NaN; else L.Cut[L.Index(i2, j2, k2) * 7 + d - 7] = double.NaN;
                    }
                }
        // step 4: the stencils
        var vertexOf = new Dictionary<long, int>();
        TetMesh3 m = res.Mesh;
        Func<int[], int> latticeVertex = p => {
            int id = L.Index(p[0], p[1], p[2]);
            if (!vertexOf.TryGetValue(id, out int v)) { v = m.Vertices.Count; m.Vertices.Add(L.Pos[id]); vertexOf[id] = v; }
            return v;
        };
        Func<int[], int[], int> cutVertex = (p, q) => {
            int d = Array.FindIndex(Dirs, x => x[0] == q[0] - p[0] && x[1] == q[1] - p[1] && x[2] == q[2] - p[2]);
            if (d < 0) { int[] t0 = p; p = q; q = t0; d = Array.FindIndex(Dirs, x => x[0] == q[0] - p[0] && x[1] == q[1] - p[1] && x[2] == q[2] - p[2]); }
            int id = L.Index(p[0], p[1], p[2]), id2 = L.Index(q[0], q[1], q[2]);
            long key = -1 - ((long)id * 7 + d);
            if (!vertexOf.TryGetValue(key, out int v)) {
                v = m.Vertices.Count; m.Vertices.Add(unwarped[id] + L.Cut[id * 7 + d] * (unwarped[id2] - unwarped[id])); vertexOf[key] = v;
            }
            return v;
        };
        Action<int, int, int, int> emit = (a, b2, c, d) => {
            double o = ExactPredicates.Orient3d(m.Vertices[a], m.Vertices[b2], m.Vertices[c], m.Vertices[d]);
            m.Tets.Add(o >= 0 ? new Index4i(a, b2, c, d) : new Index4i(b2, a, c, d));
        };
        // the quadrilateral on the lattice triangle (a, b, n) with a, b kept and n negative: the kept vertex its diagonal starts at
        // (the diagonal ends at the cut point of the other kept vertex's edge to n). Section 3.3: a truncated black edge's cut
        // point gets the diagonal; with a whole black edge a b, the Parity Rule.
        Func<int[], int[], int[], int[]> diagStart = (a, b2, nn) => {
            bool aBlack = Math.Abs(a[0] - nn[0]) + Math.Abs(a[1] - nn[1]) + Math.Abs(a[2] - nn[2]) == 2;
            bool bBlack = Math.Abs(b2[0] - nn[0]) + Math.Abs(b2[1] - nn[1]) + Math.Abs(b2[2] - nn[2]) == 2;
            if (aBlack) return b2;
            if (bBlack) return a;
            int count = (a[0] > nn[0] ? 1 : 0) + (a[1] > nn[1] ? 1 : 0) + (a[2] > nn[2] ? 1 : 0);
            bool even = (a[0] & 1) == 0;                                         // a and b on the cubical lattice Z^3
            return ((count & 1) == 1) == even ? a : b2;
        };
        var pts = new int[4][];
        for (int k = 0; k < L.Dz; k += 2)
            for (int j = 0; j < L.Dy; j += 2)
                for (int i = 0; i < L.Dx; i += 2)
                    for (int axis = 0; axis < 3; ++axis) {
                        int u = (axis + 1) % 3, v = (axis + 2) % 3;
                        var a = new[] { i, j, k }; var bb = new[] { i, j, k }; bb[axis] += 2;
                        if (!L.Inside(bb[0], bb[1], bb[2])) continue;
                        for (int q = 0; q < 4; ++q) {
                            int[] su = { 1, -1, -1, 1 }, sv = { 1, 1, -1, -1 };
                            var c0 = new[] { i, j, k }; c0[axis] += 1; c0[u] += su[q]; c0[v] += sv[q];
                            var c1 = new[] { i, j, k }; c1[axis] += 1; c1[u] += su[(q + 1) % 4]; c1[v] += sv[(q + 1) % 4];
                            if (!L.Inside(c0[0], c0[1], c0[2]) || !L.Inside(c1[0], c1[1], c1[2])) continue;
                            pts[0] = a; pts[1] = bb; pts[2] = c0; pts[3] = c1;
                            var P = new List<int[]>(); var Z = new List<int[]>(); var N = new List<int[]>();
                            foreach (int[] p in pts) { int s = L.Sign[L.Index(p[0], p[1], p[2])]; (s > 0 ? P : s < 0 ? N : Z).Add(p); }
                            if (P.Count == 0) { if (Z.Count == 4) res.QuadZeroDiscarded++; continue; }
                            // a warped background tet that is not positive any more ("unsafe" parameters): counted, exact
                            if (Z.Count > 0) {
                                double o = ExactPredicates.Orient3d(L.Pos[L.Index(a[0], a[1], a[2])], L.Pos[L.Index(bb[0], bb[1], bb[2])], L.Pos[L.Index(c0[0], c0[1], c0[2])], L.Pos[L.Index(c1[0], c1[1], c1[2])]);
                                double o0 = ExactPredicates.Orient3d(unwarped[L.Index(a[0], a[1], a[2])], unwarped[L.Index(bb[0], bb[1], bb[2])], unwarped[L.Index(c0[0], c0[1], c0[2])], unwarped[L.Index(c1[0], c1[1], c1[2])]);
                                if (Math.Sign(o) != Math.Sign(o0)) res.InvertedBackground++;
                            }
                            if (N.Count == 0) { emit(latticeVertex(pts[0]), latticeVertex(pts[1]), latticeVertex(pts[2]), latticeVertex(pts[3])); continue; }
                            if (P.Count == 1) {
                                var w = new List<int> { latticeVertex(P[0]) };
                                foreach (int[] z in Z) w.Add(latticeVertex(z));
                                foreach (int[] nn in N) w.Add(cutVertex(P[0], nn));
                                emit(w[0], w[1], w[2], w[3]);
                            } else if (P.Count == 2 && N.Count == 1) {
                                int p1 = latticeVertex(P[0]), p2 = latticeVertex(P[1]), z = latticeVertex(Z[0]), c1v = cutVertex(P[0], N[0]), c2v = cutVertex(P[1], N[0]);
                                if (diagStart(P[0], P[1], N[0]) == P[0]) { emit(z, p1, p2, c2v); emit(z, p1, c2v, c1v); }
                                else { emit(z, p1, p2, c1v); emit(z, p2, c2v, c1v); }
                            } else if (P.Count == 2) {
                                int p1 = latticeVertex(P[0]), p2 = latticeVertex(P[1]);
                                int c11 = cutVertex(P[0], N[0]), c12 = cutVertex(P[0], N[1]), c21 = cutVertex(P[1], N[0]), c22 = cutVertex(P[1], N[1]);
                                int[] x1 = diagStart(P[0], P[1], N[0]), x2 = diagStart(P[0], P[1], N[1]);
                                if (x1 == x2) {
                                    // not expected (the paper: the two diagonals do not share an endpoint); counted, and filled validly
                                    res.RuleConflicts++;
                                    if (x1 == P[0]) { emit(p1, p2, c21, c22); emit(p1, c11, c12, c22); emit(p1, c11, c22, c21); }
                                    else { emit(p2, p1, c11, c12); emit(p2, c21, c22, c12); emit(p2, c21, c12, c11); }
                                } else if (x1 == P[0]) { emit(p1, c11, c12, c21); emit(p2, c21, c22, c12); emit(p1, p2, c12, c21); }
                                else { emit(p1, c12, c11, c22); emit(p2, c22, c21, c11); emit(p1, p2, c11, c22); }
                            } else {
                                int[] lv = { latticeVertex(P[0]), latticeVertex(P[1]), latticeVertex(P[2]) };
                                int[] cv = { cutVertex(P[0], N[0]), cutVertex(P[1], N[0]), cutVertex(P[2], N[0]) };
                                var starts = new int[3]; var startOf = new int[3];        // startOf[e]: the start of the face opposite P[e]
                                for (int e = 0; e < 3; ++e) {
                                    int x = (e + 1) % 3, y = (e + 2) % 3;
                                    startOf[e] = diagStart(P[x], P[y], N[0]) == P[x] ? x : y;
                                    starts[startOf[e]]++;
                                }
                                int apex = Array.IndexOf(starts, 2);
                                if (apex < 0) { res.RuleConflicts++; continue; }           // a cyclic choice has no triangulation; not expected
                                int o1 = (apex + 1) % 3, o2 = (apex + 2) % 3;
                                emit(lv[apex], cv[0], cv[1], cv[2]);
                                if (startOf[apex] == o1) { emit(lv[apex], lv[o1], lv[o2], cv[o2]); emit(lv[apex], lv[o1], cv[o2], cv[o1]); }
                                else { emit(lv[apex], lv[o1], lv[o2], cv[o1]); emit(lv[apex], lv[o2], cv[o2], cv[o1]); }
                            }
                        }
                    }
        res.MeshSeconds = sw.Elapsed.TotalSeconds;
        return res;
    }

    // the smallest interior dihedral angle at an input edge: no mesh that conforms to the input can have a larger smallest angle
    static double MinInputDihedral(DMesh3 m)
    {
        double min = 360;
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Index4i ev = m.GetEdge(eid);
            Vector3d n1 = m.GetTriNormal(et.a), n2 = m.GetTriNormal(et.b);
            double theta = Math.Atan2(n1.Cross(n2).Length, n1.Dot(n2)) * 180 / Math.PI;
            Index3i t2 = m.GetTriangle(et.b);
            int opp = t2.a != ev.a && t2.a != ev.b ? t2.a : t2.b != ev.a && t2.b != ev.b ? t2.b : t2.c;
            bool convex = n1.Dot(m.GetVertex(opp) - m.GetVertex(ev.a)) < 0;
            min = Math.Min(min, convex ? 180 - theta : 180 + theta);
        }
        return min;
    }

    static DMesh3 Cone(double apexDegrees, int slices)
    {
        var m = new DMesh3();
        double r = Math.Tan(apexDegrees * Math.PI / 360);
        int apex = m.AppendVertex(new Vector3d(0, 0, 1)), centre = m.AppendVertex(Vector3d.Zero);
        for (int i = 0; i < slices; ++i) m.AppendVertex(new Vector3d(r * Math.Cos(2 * Math.PI * i / slices), r * Math.Sin(2 * Math.PI * i / slices), 0));
        for (int i = 0; i < slices; ++i) { int a = 2 + i, b = 2 + (i + 1) % slices; m.AppendTriangle(apex, a, b); m.AppendTriangle(centre, b, a); }
        return m;
    }

    static DMesh3 Box(Vector3d centre, Vector3d half) => new TrivialBox3Generator() { Box = new Box3d(centre, half) }.Generate().MakeDMesh();

    // inputs chosen by what can make a tet mesher miss a dihedral floor: acute creases, small solid angles, thin parts and near contacts
    public static IEnumerable<KeyValuePair<string, DMesh3>> HardInputs()
    {
        foreach (double a in new[] { 60.0, 30, 20, 12, 5 }) yield return new KeyValuePair<string, DMesh3>("wedge " + a.ToString(Inv), FeatureProbe.Wedge(a));
        yield return new KeyValuePair<string, DMesh3>("cone 10", Cone(10, 24));
        yield return new KeyValuePair<string, DMesh3>("plate 1x1x0.02", Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.01)));
        var two = Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5));
        new MeshEditor(two).AppendMesh(Box(new Vector3d(1.01, 0.1, 0), new Vector3d(0.5, 0.5, 0.5)));
        yield return new KeyValuePair<string, DMesh3>("two cubes gap 0.01", two);
        foreach (string nm in new[] { "lshape", "octant", "slot", "vnotch", "chamfer" }) yield return new KeyValuePair<string, DMesh3>(nm, Procedural.Make(nm));
    }

    // The angle bound on labels and cut points no surface would give: the cut function is a hash of the position (white noise
    // on a grid finer than the lattice, not continuous), so signs are independent per lattice point and the bisection ends at
    // an arbitrary point of each edge. The paper's bound is proven over all cut positions, so it must hold here too.
    public static int Noise(string[] args)
    {
        int seeds = args.Length > 1 ? int.Parse(args[1], Inv) : 20;
        DMesh3 box = Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5));
        Console.WriteLine("alpha_long,alpha_short,seeds,tets,min_dihedral,max_dihedral,inverted_tets,inverted_background,rule_conflicts,quadzero_discarded");
        foreach (var (aLong, aShort) in new[] { (0.28511, 0.39882), (0.24999, 0.41189) }) {
            long tets = 0, invertedBg = 0, conflicts = 0, quadZero = 0, inverted = 0; double min = 180, max = 0;
            for (int seed = 0; seed < seeds; ++seed) {
                // smaller density of inside points for some seeds, so that all sign patterns occur
                double bias = (seed % 4) * 0.3 - 0.45;
                Func<Vector3d, double> noise = p => {
                    long h = (long)Math.Floor(p.x * 4099) * 73856093L ^ (long)Math.Floor(p.y * 4099) * 19349663L ^ (long)Math.Floor(p.z * 4099) * 83492791L ^ seed * 2654435761L;
                    h ^= h >> 13; h *= 1274126177L; h ^= h >> 16;
                    return ((h & 0xFFFF) / 65535.0 - 0.5) + bias * 0.5;
                };
                Result r = Run(box, 1.0 / 14, 0, aLong, aShort, noise);
                tets += r.Mesh.Tets.Count; invertedBg += r.InvertedBackground; conflicts += r.RuleConflicts; quadZero += r.QuadZeroDiscarded;
                foreach (Index4i q in r.Mesh.Tets) {
                    if (ExactPredicates.Orient3d(r.Mesh.Vertices[q.a], r.Mesh.Vertices[q.b], r.Mesh.Vertices[q.c], r.Mesh.Vertices[q.d]) <= 0) { inverted++; continue; }
                    TetMeshQuality.Measure(r.Mesh.Vertices[q.a], r.Mesh.Vertices[q.b], r.Mesh.Vertices[q.c], r.Mesh.Vertices[q.d], out _, out double lo, out double hi);
                    min = Math.Min(min, lo); max = Math.Max(max, hi);
                }
            }
            Console.WriteLine(string.Join(",", aLong.ToString(Inv), aShort.ToString(Inv), seeds, tets, min.ToString("F4", Inv), max.ToString("F4", Inv), inverted, invertedBg, conflicts, quadZero));
        }
        return 0;
    }

    public static int Main(string[] args, IEnumerable<KeyValuePair<string, DMesh3>> procedural)
    {
        double[] cells = args.Length > 1 ? args.Skip(1).Select(x => double.Parse(x, Inv)).ToArray() : new[] { 20.0, 40, 80 };
        double relOffset = Environment.GetEnvironmentVariable("G3_STUFF_OFFSET") is string os ? double.Parse(os, Inv) : 600;
        double aLong = 0.28511, aShort = 0.39882;
        if (Environment.GetEnvironmentVariable("G3_STUFF_ALPHA") is string al) { aLong = double.Parse(al.Split(',')[0], Inv); aShort = double.Parse(al.Split(',')[1], Inv); }
        string only = Environment.GetEnvironmentVariable("G3_STUFF_ONLY");
        Console.WriteLine("method,param," + TetFiles.Header + ",max_dihedral,input_min_dihedral,extra");
        // G3_STUFF_FILE=<mesh>: that mesh instead of the procedural inputs (fetched by hand, never committed)
        if (Environment.GetEnvironmentVariable("G3_STUFF_FILE") is string file)
            procedural = new[] { new KeyValuePair<string, DMesh3>(System.IO.Path.GetFileName(file), StandardMeshReader.ReadMesh(file)) };
        else procedural = procedural.Concat(HardInputs());
        foreach (var kv in procedural) {
            if (only != null && !kv.Key.Contains(only)) continue;
            DMesh3 input = kv.Value;
            double diag = input.CachedBounds.DiagonalLength, inDih = MinInputDihedral(input);
            Action<string, string, TetMesh3, double, string> row = (method, param, t, secs, extra) => {
                double maxDih = TetMeshQuality.Measure(t).MaxDihedral.Worst;
                Console.WriteLine(method + "," + param + "," + TetFiles.Evaluate(kv.Key, t, input, secs.ToString("G4", Inv), "1") + "," +
                    maxDih.ToString("G6", Inv) + "," + inDih.ToString("G5", Inv) + "," + extra);
            };
            if (Environment.GetEnvironmentVariable("G3_STUFF_OURS") != "0")
                foreach (double crease in new[] { 0.0, 30 }) {
                    var sw = Stopwatch.StartNew();
                    var tm = new MeshTetrahedralization(input) { CreaseAngleDegrees = crease };
                    tm.Compute();
                    if (tm.Result == null) { Console.WriteLine("ours,crease=" + crease.ToString(Inv) + "," + kv.Key + "," + tm.Status); continue; }
                    row("ours", "crease=" + crease.ToString(Inv), tm.Result, sw.Elapsed.TotalSeconds,
                        "status=" + tm.Status + " " + string.Join(" ", tm.Counts.Where(c => c.Key.StartsWith("crease") || c.Key == "unrefined_bad_tets").Select(c => c.Key + "=" + c.Value)));
                }
            foreach (double rc in cells) {
                Result r = Run(input, diag / rc, relOffset > 0 ? diag / relOffset : 0, aLong, aShort);
                if (r.Mesh.Tets.Count == 0) { Console.WriteLine("stuffing,cell=" + rc.ToString(Inv) + "," + kv.Key + ",empty"); continue; }
                row("stuffing", "cell=" + rc.ToString(Inv), r.Mesh, r.FieldSeconds + r.MeshSeconds,
                    "field_s=" + r.FieldSeconds.ToString("G3", Inv) + " mesh_s=" + r.MeshSeconds.ToString("G3", Inv) + " lattice=" + r.LatticePoints + " cuts=" + r.CutPoints +
                    " warped=" + r.Warped + " quadzero_discarded=" + r.QuadZeroDiscarded + " inverted_background=" + r.InvertedBackground + " rule_conflicts=" + r.RuleConflicts);
            }
        }
        return 0;
    }
}
