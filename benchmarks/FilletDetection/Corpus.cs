// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

namespace FilletDetection
{
    /// <summary>
    /// A CAD mesh with the patch structure of its B-rep, as the ABC dataset gives it (Koch et al., CVPR 2019, supplement
    /// section 2.3: per patch a surface type and its triangles, per curve a sharp flag and its mesh vertices), read from the
    /// small JSON that abc_feat_to_json.py writes, or made from a procedural solid.
    /// </summary>
    public class PatchModel
    {
        public string Id;
        public DMesh3 Mesh;
        public string[] PatchType;
        public int[] PatchOf;                       // per triangle id, -1: in no patch
        public Vector3d[] PatchAxis, PatchLocation; // from the file where given (torus), else zero
        public List<int[]> SharpCurves = new List<int[]>();

        /// <summary> the model of dir/id.obj and dir/id.json; null with a reason if the mesh cannot carry the indices </summary>
        public static PatchModel Read(string objPath, string jsonPath, out string reason)
        {
            reason = null;
            var mesh = new DMesh3();
            var inv = CultureInfo.InvariantCulture;
            int faces = 0;
            foreach (string line in File.ReadLines(objPath)) {
                if (line.Length < 2) continue;
                if (line[0] == 'v' && line[1] == ' ') {
                    string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    mesh.AppendVertex(new Vector3d(double.Parse(p[1], inv), double.Parse(p[2], inv), double.Parse(p[3], inv)));
                } else if (line[0] == 'f' && line[1] == ' ') {
                    string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length != 4) { reason = "a face that is no triangle"; return null; }
                    var t = new Index3i();
                    for (int k = 0; k < 3; ++k) { string s = p[k + 1]; int slash = s.IndexOf('/'); t[k] = int.Parse(slash < 0 ? s : s.Substring(0, slash), inv) - 1; }
                    // the triangle id must stay the face index of the file
                    if (mesh.AppendTriangle(t) != faces) { reason = "non-manifold or degenerate triangle " + faces; return null; }
                    ++faces;
                }
            }
            var model = new PatchModel { Id = Path.GetFileNameWithoutExtension(objPath), Mesh = mesh };
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath))) {
                JsonElement patches = doc.RootElement.GetProperty("patches");
                int n = patches.GetArrayLength();
                model.PatchType = new string[n]; model.PatchAxis = new Vector3d[n]; model.PatchLocation = new Vector3d[n];
                model.PatchOf = Enumerable.Repeat(-1, faces).ToArray();
                int pi = 0;
                foreach (JsonElement p in patches.EnumerateArray()) {
                    model.PatchType[pi] = p.GetProperty("type").GetString();
                    if (p.TryGetProperty("z_axis", out JsonElement ax) && ax.GetArrayLength() == 3) model.PatchAxis[pi] = new Vector3d(ax[0].GetDouble(), ax[1].GetDouble(), ax[2].GetDouble());
                    if (p.TryGetProperty("location", out JsonElement lo) && lo.GetArrayLength() == 3) model.PatchLocation[pi] = new Vector3d(lo[0].GetDouble(), lo[1].GetDouble(), lo[2].GetDouble());
                    foreach (JsonElement f in p.GetProperty("faces").EnumerateArray()) {
                        int fi = f.GetInt32();
                        if (fi < 0 || fi >= faces) { reason = "face index " + fi + " outside the mesh"; return null; }
                        model.PatchOf[fi] = pi;
                    }
                    ++pi;
                }
                foreach (JsonElement c in doc.RootElement.GetProperty("sharp_curves").EnumerateArray())
                    model.SharpCurves.Add(c.EnumerateArray().Select(v => v.GetInt32()).ToArray());
            }
            return model;
        }

        /// <summary>
        /// A procedural solid in the same form: patches are the connected regions of one class without a fold above 40 degrees
        /// (Plane, Cylinder, Sphere), sharp curves are the patch borders that fold by more than 40 degrees (the tangent
        /// border of a fillet with 2 chords folds by 22.5).
        /// </summary>
        public static PatchModel FromSolid(Solid s)
        {
            DMesh3 mesh = s.Mesh;
            int n = mesh.MaxTriangleID;
            var patchOf = Enumerable.Repeat(-1, n).ToArray();
            var types = new List<string>();
            double cos40 = Math.Cos(40 * MathUtil.Deg2Rad);
            foreach (int seed in mesh.TriangleIndices()) {
                if (patchOf[seed] >= 0) continue;
                int id = types.Count;
                int cls = s.Class(seed);
                types.Add(cls == Solid.Flat ? "Plane" : cls == Solid.Corner ? "Sphere" : "Cylinder");
                var stack = new Stack<int>(); stack.Push(seed); patchOf[seed] = id;
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i nb = mesh.GetTriNeighbourTris(t);
                    for (int j = 0; j < 3; ++j) {
                        int o = nb[j];
                        if (o < 0 || patchOf[o] >= 0 || mesh.GetTriangleGroup(o) != mesh.GetTriangleGroup(t)) continue;
                        // a planar face ends at any fold, a curved one at a sharp fold
                        double dot = mesh.GetTriNormal(o).Dot(mesh.GetTriNormal(t));
                        if (cls == Solid.Flat ? dot < 1 - 1e-9 : dot < cos40) continue;
                        patchOf[o] = id; stack.Push(o);
                    }
                }
            }
            var model = new PatchModel {
                Id = s.Name, Mesh = mesh, PatchOf = patchOf, PatchType = types.ToArray(),
                PatchAxis = new Vector3d[types.Count], PatchLocation = new Vector3d[types.Count]
            };
            var curves = new Dictionary<(int, int), HashSet<int>>();
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || patchOf[et.a] == patchOf[et.b]) continue;
                if (mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b)) >= cos40) continue;
                var key = (Math.Min(patchOf[et.a], patchOf[et.b]), Math.Max(patchOf[et.a], patchOf[et.b]));
                if (!curves.TryGetValue(key, out var set)) curves[key] = set = new HashSet<int>();
                Index2i ev = mesh.GetEdgeV(eid);
                set.Add(ev.a); set.Add(ev.b);
            }
            foreach (var kv in curves.OrderBy(k => k.Key)) model.SharpCurves.Add(kv.Value.OrderBy(v => v).ToArray());
            return model;
        }

        /// <summary> writes id.obj and id_features.yml in the layout of the ABC files (for testing the conversion without the data) </summary>
        public void WriteAbcFiles(string dir)
        {
            var inv = CultureInfo.InvariantCulture;
            using (var w = new StreamWriter(Path.Combine(dir, Id + "_trimesh_000.obj"))) {
                foreach (int vid in Mesh.VertexIndices()) { Vector3d v = Mesh.GetVertex(vid); w.WriteLine(string.Format(inv, "v {0:R} {1:R} {2:R}", v.x, v.y, v.z)); }
                foreach (int tid in Mesh.TriangleIndices()) { Index3i t = Mesh.GetTriangle(tid); w.WriteLine($"f {t.a + 1}//{t.a + 1} {t.b + 1}//{t.b + 1} {t.c + 1}//{t.c + 1}"); }
            }
            using (var w = new StreamWriter(Path.Combine(dir, Id + "_features_000.yml"))) {
                w.WriteLine("curves:");
                foreach (int[] c in SharpCurves) {
                    w.WriteLine("- sharp: true\n  type: Line\n  vert_indices:");
                    foreach (int v in c) w.WriteLine("  - " + v);
                }
                w.WriteLine("surfaces:");
                for (int p = 0; p < PatchType.Length; ++p) {
                    w.WriteLine("- face_indices:");
                    foreach (int tid in Mesh.TriangleIndices()) if (PatchOf[tid] == p) w.WriteLine("  - " + tid);
                    w.WriteLine("  type: " + PatchType[p]);
                }
            }
        }
    }

    /// <summary>
    /// The answer for a CAD mesh without fillet labels, FROZEN before the first run on the data (2026-11-22): a heuristic on the
    /// B-rep patches, not a label of the CAD system.
    ///   - A border edge between two patches is sharp if both its vertices lie on one curve marked sharp, else smooth.
    ///   - A patch is a FILLET if its type is Cylinder or Torus, it has smooth border edges to at least two other patches, and
    ///     its sectional angle is at most 150 degrees. The sectional angle is the extent of the triangle normals around the
    ///     cylinder's axis (the direction the normals vary least in), for a torus the extent of the angle between the normal
    ///     and the plane perpendicular to its axis (axis and location from the file).
    ///   - A patch is IGNORED (scored neither way) if it could be a fillet this rule cannot decide: Cylinder or Torus with two
    ///     smooth neighbours and a sectional angle above 150 and up to 200 degrees (a full round), a torus without axis, and
    ///     every patch of another curved type (Sphere, Cone, BSpline, Revolution, Extrusion, Other) with smooth border edges to
    ///     at least two other patches (vertex fillets, variable-radius fillets, blends).
    ///   - Everything else is NO FILLET: planes, holes and shafts (sharp rims, or more than 200 degrees), triangles in no patch.
    /// </summary>
    public static class AbcHeuristic
    {
        public const int No = 0, Fillet = 1, Ignored = 2;
        public const double FilletMaxDegrees = 150, IgnoredMaxDegrees = 200;

        public static int[] Label(PatchModel m)
        {
            DMesh3 mesh = m.Mesh;
            int np = m.PatchType.Length;
            var onCurve = new Dictionary<int, List<int>>();
            for (int c = 0; c < m.SharpCurves.Count; ++c)
                foreach (int v in m.SharpCurves[c]) {
                    if (!onCurve.TryGetValue(v, out var list)) onCurve[v] = list = new List<int>();
                    list.Add(c);
                }
            var smooth = new HashSet<int>[np];
            for (int p = 0; p < np; ++p) smooth[p] = new HashSet<int>();
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                int pa = m.PatchOf[et.a], pb = m.PatchOf[et.b];
                if (pa == pb || pa < 0 || pb < 0) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                bool sharp = onCurve.TryGetValue(ev.a, out var la) && onCurve.TryGetValue(ev.b, out var lb) && la.Intersect(lb).Any();
                if (!sharp) { smooth[pa].Add(pb); smooth[pb].Add(pa); }
            }
            var normals = new List<Vector3d>[np]; var centroids = new List<Vector3d>[np];
            for (int p = 0; p < np; ++p) { normals[p] = new List<Vector3d>(); centroids[p] = new List<Vector3d>(); }
            foreach (int tid in mesh.TriangleIndices()) {
                int p = m.PatchOf[tid];
                if (p < 0 || mesh.GetTriArea(tid) <= 0) continue;
                normals[p].Add(mesh.GetTriNormal(tid)); centroids[p].Add(mesh.GetTriCentroid(tid));
            }
            var patchLabel = new int[np];
            for (int p = 0; p < np; ++p) {
                string type = m.PatchType[p];
                if (type == "Plane" || smooth[p].Count < 2 || normals[p].Count == 0) continue;
                if (type != "Cylinder" && type != "Torus") { patchLabel[p] = Ignored; continue; }
                double extent;
                if (type == "Cylinder") {
                    Vector3d axis = LeastVariation(normals[p]);
                    Vector3d e1 = Vector3d.Zero, e2 = Vector3d.Zero;
                    Vector3d.MakePerpVectors(ref axis, out e1, out e2);
                    extent = Extent(normals[p].Select(nrm => Math.Atan2(nrm.Dot(e2), nrm.Dot(e1))));
                } else {
                    Vector3d axis = m.PatchAxis[p];
                    if (axis.Length < 0.5) { patchLabel[p] = Ignored; continue; }
                    axis.Normalize();
                    Vector3d loc = m.PatchLocation[p];
                    extent = Extent(normals[p].Select((nrm, i) => {
                        Vector3d d = centroids[p][i] - loc; d -= d.Dot(axis) * axis;
                        return Math.Atan2(nrm.Dot(axis), nrm.Dot(d.Normalized));
                    }));
                }
                patchLabel[p] = extent <= FilletMaxDegrees ? Fillet : extent <= IgnoredMaxDegrees ? Ignored : No;
            }
            var label = new int[mesh.MaxTriangleID];
            foreach (int tid in mesh.TriangleIndices()) if (m.PatchOf[tid] >= 0) label[tid] = patchLabel[m.PatchOf[tid]];
            return label;
        }

        // unit direction along which the vectors vary least (smallest eigenvector of their covariance, by power iteration on the complement)
        static Vector3d LeastVariation(List<Vector3d> v)
        {
            double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            foreach (Vector3d a in v) { xx += a.x * a.x; yy += a.y * a.y; zz += a.z * a.z; xy += a.x * a.y; xz += a.x * a.z; yz += a.y * a.z; }
            double tr = xx + yy + zz;
            var m = new Matrix3d(tr - xx, -xy, -xz, -xy, tr - yy, -yz, -xz, -yz, tr - zz);
            Vector3d best = Vector3d.AxisX; double bestLen = -1;
            foreach (Vector3d start in new[] { Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ }) {
                Vector3d x = start;
                for (int i = 0; i < 200; ++i) { Vector3d y = m * x; double len = y.Length; if (len < 1e-300) break; x = y / len; }
                double value = (m * x).Dot(x);
                if (value > bestLen) { bestLen = value; best = x; }
            }
            return best;
        }

        // angular extent in degrees of a set of angles on the circle: 360 minus the largest gap
        static double Extent(IEnumerable<double> angles)
        {
            var a = angles.OrderBy(x => x).ToList();
            if (a.Count < 2) return 0;
            double gap = a[0] + 2 * Math.PI - a[a.Count - 1];
            for (int i = 1; i < a.Count; ++i) gap = Math.Max(gap, a[i] - a[i - 1]);
            return (2 * Math.PI - gap) * MathUtil.Rad2Deg;
        }
    }

    /// <summary> the second measurement: the medial-ball detector, thresholds as merged in #1386, against AbcHeuristic on a sample of CAD meshes </summary>
    public static class Corpus
    {
        /// <summary> radius bound candidates, frozen: shares of the bounding box diagonal and multiples of the median wall thickness </summary>
        public static readonly (string, bool, double)[] Bounds = {
            ("0.05 diagonal", false, 0.05), ("0.1 diagonal", false, 0.1), ("0.2 diagonal", false, 0.2),
            ("0.5 thickness", true, 0.5), ("1 thickness", true, 1), ("2 thickness", true, 2)
        };
        public const int MaxTriangles = 100000;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary> area weighted median of the inscribed-ball wall thickness over the triangles (MeshWallThickness) </summary>
        public static double MedianWallThickness(DMesh3 mesh)
        {
            var wt = new MeshWallThickness(mesh) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false, ComputeTriangles = true };
            if (!wt.Compute()) return double.NaN;
            var values = new List<(double, double)>(); double total = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                double t = wt.TriangleThickness[tid];
                if (double.IsNaN(t)) continue;
                double a = mesh.GetTriArea(tid); values.Add((t, a)); total += a;
            }
            values.Sort();
            double run = 0;
            foreach (var (t, a) in values) { run += a; if (run >= 0.5 * total) return t; }
            return double.NaN;
        }

        /// <summary>
        /// One detector run with the largest bound serves every bound: a facet is a fillet for a bound if it is one for the
        /// largest and its radius is at most the bound (the ball found does not depend on the start radius above it; `boundcheck`
        /// compares with separate runs on the procedural solids).
        /// </summary>
        public static bool[][] DetectAll(DMesh3 mesh, out double[] boundValues, out double seconds)
        {
            var sw = Stopwatch.StartNew();
            double diagonal = mesh.CachedBounds.DiagonalLength, thickness = MedianWallThickness(mesh);
            boundValues = Bounds.Select(b => b.Item2 ? b.Item3 * thickness : b.Item3 * diagonal).ToArray();
            double largest = boundValues.Where(b => !double.IsNaN(b)).Max();
            Detection d = new MedialDetector { MaxRadius = largest }.Run(mesh);
            var result = new bool[Bounds.Length][];
            for (int k = 0; k < Bounds.Length; ++k) {
                result[k] = new bool[mesh.MaxTriangleID];
                if (double.IsNaN(boundValues[k])) continue;
                foreach (int tid in mesh.TriangleIndices()) result[k][tid] = d.Fillet[tid] && d.Radius[tid] <= boundValues[k];
            }
            seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        static void Areas(DMesh3 mesh, int[] label, bool[] hit, out double tp, out double fp, out double fn)
        {
            tp = fp = fn = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                if (label[tid] == AbcHeuristic.Ignored) continue;
                double a = mesh.GetTriArea(tid);
                if (label[tid] == AbcHeuristic.Fillet) { if (hit[tid]) tp += a; else fn += a; }
                else if (hit[tid]) fp += a;
            }
        }

        /// <summary> the heuristic against the known answer of the procedural solids (vertex fillets are ignored by the heuristic and reported) </summary>
        public static int SpotCheck(string[] filters)
        {
            Console.WriteLine("| solid | heuristic precision | recall | true fillet area the heuristic ignores | non-fillet area it ignores |");
            Console.WriteLine("|---|---|---|---|---|");
            double sp = 0, sr = 0; int np = 0, nr = 0;
            foreach (Solid s in Program.Cases(filters.Append("cad").ToArray())) {
                int[] label = AbcHeuristic.Label(PatchModel.FromSolid(s));
                double tp = 0, fp = 0, fn = 0, ignTrue = 0, ignFalse = 0, truth = 0, rest = 0;
                foreach (int tid in s.Mesh.TriangleIndices()) {
                    double a = s.Mesh.GetTriArea(tid); bool t = s.IsFillet(tid);
                    if (t) truth += a; else rest += a;
                    if (label[tid] == AbcHeuristic.Ignored) { if (t) ignTrue += a; else ignFalse += a; }
                    else if (label[tid] == AbcHeuristic.Fillet) { if (t) tp += a; else fp += a; }
                    else if (t) fn += a;
                }
                string P = tp + fp > 0 ? (tp / (tp + fp)).ToString("0.00", Inv) : "-", R = tp + fn > 0 ? (tp / (tp + fn)).ToString("0.00", Inv) : "-";
                if (tp + fp > 0) { sp += tp / (tp + fp); ++np; }
                if (tp + fn > 0) { sr += tp / (tp + fn); ++nr; }
                Console.WriteLine($"| {s.Name} | {P} | {R} | {(truth > 0 ? (ignTrue / truth).ToString("0.00", Inv) : "-")} | {(ignFalse / rest).ToString("0.00", Inv)} |");
            }
            Console.WriteLine($"\nmean precision {(sp / Math.Max(1, np)).ToString("0.000", Inv)} over {np} solids, mean recall {(sr / Math.Max(1, nr)).ToString("0.000", Inv)} over {nr} (on the area the heuristic does not ignore)");
            return 0;
        }

        /// <summary> one shared run against separate runs per bound, on the procedural solids: triangles that differ </summary>
        public static int BoundCheck(string[] filters)
        {
            int differ = 0, total = 0;
            foreach (Solid s in Program.Cases(filters.Append("cad").ToArray())) {
                bool[][] all = DetectAll(s.Mesh, out double[] values, out double sec);
                for (int k = 0; k < values.Length; ++k) {
                    if (double.IsNaN(values[k])) continue;
                    Detection d = new MedialDetector { MaxRadius = values[k] }.Run(s.Mesh);
                    int before = differ;
                    foreach (int tid in s.Mesh.TriangleIndices()) { ++total; if (d.Fillet[tid] != all[k][tid]) ++differ; }
                    if (differ > before) Console.WriteLine($"{s.Name} {Bounds[k].Item1} = {values[k]:0.0000} (largest {values.Max():0.0000}): {differ - before} differ, separate marks {s.Mesh.TriangleIndices().Count(t => d.Fillet[t])}, shared {s.Mesh.TriangleIndices().Count(t => all[k][t])}");
                }
            }
            Console.WriteLine($"triangle verdicts that differ between the shared run and separate runs: {differ} of {total}");
            return differ == 0 ? 0 : 1;
        }

        /// <summary> writes the procedural solids in the ABC file layout (to test the conversion and the corpus run without the data) </summary>
        public static int Export(string dir, string[] filters)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (Solid s in Program.Cases(filters.Append("cad").ToArray())) {
                PatchModel m = PatchModel.FromSolid(s);
                m.Id = (n++).ToString("00000000", Inv) + "_" + new string(s.Name.Where(char.IsLetterOrDigit).ToArray());
                m.WriteAbcFiles(dir);
            }
            return 0;
        }

        /// <summary>
        /// Labels of every model in dir (id.json from the conversion): prints "id fillet|plain triangles" in the order given,
        /// for the sample rule (the first 200 with a fillet triangle and the first 50 without, at most MaxTriangles).
        /// Needs the meshes for the labels, so it reads id.obj too.
        /// </summary>
        public static int Run(string dir, string listPath, string csvPath, double budgetSeconds, int wantFillet, int wantPlain)
        {
            var ids = File.ReadAllLines(listPath).Where(l => l.Trim().Length > 0).Select(l => l.Trim()).ToList();
            var rows = new List<string> { "id,triangles,total_area,fillet_area,ignored_area,bound,bound_value,tp,fp,fn,seconds" };
            var perBound = Bounds.Select(_ => new List<(double, double, double, bool)>()).ToArray();
            int doneFillet = 0, donePlain = 0, skipped = 0, tooLarge = 0, missing = 0;
            var skipReasons = new Dictionary<string, int>();
            var clock = Stopwatch.StartNew();
            double detectSeconds = 0; long detectTriangles = 0;
            foreach (string id in ids) {
                if (doneFillet >= wantFillet && donePlain >= wantPlain) break;
                if (clock.Elapsed.TotalSeconds > budgetSeconds) { Console.WriteLine($"time budget of {budgetSeconds} s used: stopped before {id}"); break; }
                string obj = Path.Combine(dir, id + ".obj"), json = Path.Combine(dir, id + ".json");
                if (!File.Exists(obj) || !File.Exists(json)) { ++missing; continue; }
                PatchModel m;
                string reason;
                try { m = PatchModel.Read(obj, json, out reason); }
                catch (Exception e) { m = null; reason = e.GetType().Name; }
                if (m == null) { ++skipped; skipReasons[reason.Split(' ')[0]] = skipReasons.GetValueOrDefault(reason.Split(' ')[0]) + 1; continue; }
                if (m.Mesh.TriangleCount > MaxTriangles || m.Mesh.TriangleCount == 0) { ++tooLarge; continue; }
                int[] label = AbcHeuristic.Label(m);
                double total = 0, filletArea = 0, ignoredArea = 0;
                foreach (int tid in m.Mesh.TriangleIndices()) {
                    double a = m.Mesh.GetTriArea(tid); total += a;
                    if (label[tid] == AbcHeuristic.Fillet) filletArea += a; else if (label[tid] == AbcHeuristic.Ignored) ignoredArea += a;
                }
                bool hasFillet = filletArea > 0;
                if (hasFillet ? doneFillet >= wantFillet : donePlain >= wantPlain) continue;
                bool[][] hits = DetectAll(m.Mesh, out double[] values, out double seconds);
                detectSeconds += seconds; detectTriangles += m.Mesh.TriangleCount;
                for (int k = 0; k < Bounds.Length; ++k) {
                    Areas(m.Mesh, label, hits[k], out double tp, out double fp, out double fn);
                    perBound[k].Add((tp, fp, fn, hasFillet));
                    rows.Add(string.Join(",", id, m.Mesh.TriangleCount, total.ToString("R", Inv), filletArea.ToString("R", Inv), ignoredArea.ToString("R", Inv),
                        Bounds[k].Item1, values[k].ToString("R", Inv), tp.ToString("R", Inv), fp.ToString("R", Inv), fn.ToString("R", Inv), seconds.ToString("0.0", Inv)));
                }
                if (hasFillet) ++doneFillet; else ++donePlain;
            }
            File.WriteAllLines(csvPath, rows);
            Console.WriteLine($"\nmodels scored: {doneFillet} with fillets (heuristic), {donePlain} without; not scored: {missing} files missing, {skipped} unreadable ({string.Join(", ", skipReasons.Select(kv => kv.Key + " " + kv.Value))}), {tooLarge} above {MaxTriangles} triangles or empty");
            Console.WriteLine($"detection: {detectSeconds.ToString("0", Inv)} s for {detectTriangles} triangles\n");
            Console.WriteLine("| radius bound | precision mean | median | worst decile | recall mean | median | worst decile | models with recall < 0.9 | F1 worst decile | models without fillets that got marks |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            for (int k = 0; k < Bounds.Length; ++k) {
                var withF = perBound[k].Where(x => x.Item4).ToList();
                var prec = withF.Where(x => x.Item1 + x.Item2 > 0).Select(x => x.Item1 / (x.Item1 + x.Item2)).OrderBy(x => x).ToList();
                var rec = withF.Select(x => x.Item1 / (x.Item1 + x.Item3)).OrderBy(x => x).ToList();
                var f1 = withF.Select(x => 2 * x.Item1 / (2 * x.Item1 + x.Item2 + x.Item3)).OrderBy(x => x).ToList();
                int plainMarked = perBound[k].Count(x => !x.Item4 && x.Item2 > 0);
                Console.WriteLine($"| {Bounds[k].Item1} | {Stat(prec, -1)} | {Stat(prec, 0.5)} | {Stat(prec, 0.1)} | {Stat(rec, -1)} | {Stat(rec, 0.5)} | {Stat(rec, 0.1)} | {rec.Count(x => x < 0.9)} of {rec.Count} | {Stat(f1, 0.1)} | {plainMarked} of {perBound[k].Count(x => !x.Item4)} |");
            }
            return 0;
        }

        // mean (q < 0) or the q quantile of a sorted list
        static string Stat(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return "-";
            double v = q < 0 ? sorted.Average() : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
            return v.ToString("0.000", Inv);
        }
    }
}
