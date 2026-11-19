using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace ReferenceParts
{
    /// <summary> the functional checks of the reference parts; each one measures a value and says pass or fail </summary>
    public static class Checks
    {
        static double Cube(double x) { return x * x * x; }

        /// <summary> the signed volume about the bounds centre (accurate far from the origin) </summary>
        public static double MeshVolume(DMesh3 mesh)
        {
            if (mesh.TriangleCount == 0) return 0;
            Vector3d c = MeshRepairMetrics.TriangleBounds(mesh).Center;
            double v = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, d = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref d);
                v += (a - c).Dot((b - c).Cross(d - c));
            }
            return v / 6;
        }

        // ---------------- volume ----------------

        /// <summary> the material volume is the construction's (after the cut-out once it is made), within the deviation bound's area times its distance </summary>
        public static PartCheck Volume() { return new PartCheck() { Name = "volume", Functional = true, Run = volumeCheck }; }

        static CheckResult volumeCheck(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            double expected = afterCut ? part.VolumeAfter : part.VolumeBefore;
            double measured = MeshVolume(mesh) / Cube(part.Scale);
            double tol = part.Area * part.Deviation + 1e-4 * expected;
            double rel = (measured - expected) / expected;
            return new CheckResult() {
                Name = "volume", Functional = true, Value = rel, Pass = Math.Abs(measured - expected) <= tol,
                Requirement = "|dV/V| <= " + (tol / expected).ToString("0.0e+0", System.Globalization.CultureInfo.InvariantCulture),
                Detail = "V=" + measured.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        // ---------------- clean (descriptive) ----------------

        public static PartCheck Clean() { return new PartCheck() { Name = "clean", Functional = false, Run = cleanCheck }; }

        static CheckResult cleanCheck(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            string reason = mesh.TriangleCount == 0 ? "empty" : MeshRobustRepair.NotCleanReason(mesh, true);
            return new CheckResult() { Name = "clean", Functional = false, Pass = reason.Length == 0, Value = reason.Length == 0 ? 1 : 0, Requirement = "NotCleanReason empty", Detail = reason };
        }

        // ---------------- pins through the bores ----------------

        /// <summary> a pin of fixed radius passes: the certified distance from the bore's axis segment to the mesh is at least the radius </summary>
        public static PartCheck Pin(string name) { return new PartCheck() { Name = name, Functional = true, Run = (p, m, a) => pinCheck(p, m, name) }; }

        static CheckResult pinCheck(ReferencePart part, DMesh3 mesh, string name)
        {
            ReferencePart.Pin pin = part.Pins.First(x => x.Name == name);
            double clearance = SegmentClearance(mesh, part.Frame.Apply(pin.A), part.Frame.Apply(pin.B), pin.Radius * part.Scale, 0.01 * pin.Radius * part.Scale) / part.Scale;
            return new CheckResult() {
                Name = name, Functional = true, Value = clearance, Pass = clearance >= pin.Radius,
                Requirement = ">= " + pin.Radius.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// A certified lower bound of the distance from the segment ab to the triangles of the mesh: CertifiedPointMeshDistance.LowerBound at the
        /// ends of intervals, minus half the interval's length (the distance is 1-Lipschitz). An interval is split until it proves the target or is
        /// shorter than minLength, so a segment that clears the target by a margin costs few queries and the value is at most the true distance.
        /// </summary>
        public static double SegmentClearance(DMesh3 mesh, Vector3d a, Vector3d b, double target, double minLength)
        {
            double length = (b - a).Length;
            var cache = new Dictionary<double, double>();
            Func<double, double> lb = u => {
                double v;
                if (!cache.TryGetValue(u, out v)) { v = CertifiedPointMeshDistance_LowerBound(mesh, a + u * (b - a)); cache[u] = v; }
                return v;
            };
            double worst = double.PositiveInfinity;
            var stack = new Stack<double[]>();
            const int initial = 8;
            for (int i = 0; i < initial; ++i) stack.Push(new[] { (double)i / initial, (double)(i + 1) / initial });
            while (stack.Count > 0) {
                double[] iv = stack.Pop();
                double len = (iv[1] - iv[0]) * length;
                double bound = Math.Min(lb(iv[0]), lb(iv[1])) - 0.5 * len;
                if (bound >= target || len <= minLength) { worst = Math.Min(worst, bound); continue; }
                double mid = 0.5 * (iv[0] + iv[1]);
                stack.Push(new[] { iv[0], mid }); stack.Push(new[] { mid, iv[1] });
            }
            return worst;
        }

        static double CertifiedPointMeshDistance_LowerBound(DMesh3 mesh, Vector3d p) { return CertifiedPointMeshDistance.LowerBound(mesh, p); }

        // ---------------- components ----------------

        public sealed class Component
        {
            public int Triangles;
            public AxisAlignedBox3d Box = AxisAlignedBox3d.Empty;
            public List<int> Vertices = new List<int>();
            public double Diagonal { get { return Box.Diagonal.Length; } }
        }

        /// <summary>
        /// The connected components of the triangles, with vertices closer than tolerance counted as one (so an unwelded soup of two
        /// solids has two components, and a slit wider than the tolerance separates). Largest bounding box diagonal first.
        /// </summary>
        public static List<Component> Components(DMesh3 mesh, double tolerance)
        {
            int nv = mesh.MaxVertexID;
            var parent = new int[nv];
            for (int i = 0; i < nv; ++i) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            var used = new bool[nv];
            foreach (int t in mesh.TriangleIndices()) { Index3i tri = mesh.GetTriangle(t); used[tri.a] = used[tri.b] = used[tri.c] = true; }
            var cells = new Dictionary<(long, long, long), List<int>>();
            for (int v = 0; v < nv; ++v) {
                if (!used[v]) continue;
                Vector3d p = mesh.GetVertex(v);
                var key = ((long)Math.Floor(p.x / tolerance), (long)Math.Floor(p.y / tolerance), (long)Math.Floor(p.z / tolerance));
                for (int dx = -1; dx <= 1; ++dx)
                    for (int dy = -1; dy <= 1; ++dy)
                        for (int dz = -1; dz <= 1; ++dz) {
                            List<int> list;
                            if (!cells.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out list)) continue;
                            foreach (int u in list)
                                if ((mesh.GetVertex(u) - p).Length <= tolerance) parent[find(u)] = find(v);
                        }
                List<int> own;
                if (!cells.TryGetValue(key, out own)) { own = new List<int>(); cells[key] = own; }
                own.Add(v);
            }
            foreach (int t in mesh.TriangleIndices()) { Index3i tri = mesh.GetTriangle(t); parent[find(tri.b)] = find(tri.a); parent[find(tri.c)] = find(tri.a); }
            var byRoot = new Dictionary<int, Component>();
            foreach (int t in mesh.TriangleIndices()) {
                int r = find(mesh.GetTriangle(t).a);
                Component c;
                if (!byRoot.TryGetValue(r, out c)) { c = new Component(); byRoot[r] = c; }
                c.Triangles++;
            }
            for (int v = 0; v < nv; ++v) {
                if (!used[v]) continue;
                Component c;
                if (!byRoot.TryGetValue(find(v), out c)) continue;
                c.Box.Contain(mesh.GetVertex(v));
                c.Vertices.Add(v);
            }
            return byRoot.Values.OrderByDescending(c => c.Diagonal).ToList();
        }

        static double WeldTolerance(DMesh3 mesh) { return 1e-7 * MeshRepairMetrics.TriangleBounds(mesh).Diagonal.Length; }

        // ---------------- rib connected ----------------

        /// <summary> the rib is still part of the housing: one component, and the highest vertex (the rib's top) and the lowest (the housing's base) are in it </summary>
        public static PartCheck RibConnected() { return new PartCheck() { Name = "ribConnected", Functional = true, Run = ribConnected }; }

        static CheckResult ribConnected(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            var r = new CheckResult() { Name = "ribConnected", Functional = true, Requirement = "1 component, rib top and base in it" };
            if (mesh.TriangleCount == 0) { r.Detail = "empty"; r.Value = 0; return r; }
            List<Component> comps = Components(mesh, WeldTolerance(mesh));
            double zTop = double.NegativeInfinity;
            // the rib's top: the highest vertex in the frame's z direction (canonical z)
            Component top = null, bottom = null; double lo = double.PositiveInfinity;
            foreach (Component c in comps)
                foreach (int v in c.Vertices) {
                    double z = part.Frame.Inverse(mesh.GetVertex(v)).z;
                    if (z > zTop) { zTop = z; top = c; }
                    if (z < lo) { lo = z; bottom = c; }
                }
            double topExpected = part.Targets["ribTopZ"], bottomExpected = part.Targets["baseBottomZ"];
            bool topOk = Math.Abs(zTop - topExpected) <= 4 * part.Deviation + 1e-3;
            bool bottomOk = Math.Abs(lo - bottomExpected) <= 4 * part.Deviation + 1e-3;
            r.Value = comps.Count;
            r.Pass = comps.Count == 1 && top == bottom && topOk && bottomOk;
            r.Detail = comps.Count + " components, top z " + (zTop - topExpected).ToString("+0.####;-0.####;0", System.Globalization.CultureInfo.InvariantCulture)
                + (top == bottom ? ", rib top and base joined" : ", rib top and base apart");
            return r;
        }

        // ---------------- rib thickness ----------------

        /// <summary>
        /// local thickness sampling across the rib: along lines through the rib's mid plane the length of the material interval that contains the
        /// mid plane (0 if there is none); the minimum over the lines, within 20 % of the construction's thickness (and no thicker than 120 %)
        /// </summary>
        public static PartCheck RibThickness() { return new PartCheck() { Name = "ribThickness", Functional = true, Run = ribThickness }; }

        static CheckResult ribThickness(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            double t = part.Targets["ribThickness"];
            double min = double.PositiveInfinity, max = 0;
            foreach (Ray3d line in part.ThicknessLines) {
                double th = IntervalThickness(mesh, part.Frame.Apply(line.Origin), part.Frame.ApplyDirection(line.Direction), 5 * part.Scale) / part.Scale;
                min = Math.Min(min, th); max = Math.Max(max, th);
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new CheckResult() {
                Name = "ribThickness", Functional = true, Value = min, Pass = min >= 0.8 * t && max <= 1.2 * t,
                Requirement = "in [" + (0.8 * t).ToString("0.###", inv) + ", " + (1.2 * t).ToString("0.###", inv) + "]",
                Detail = "max " + max.ToString("0.#####", inv)
            };
        }

        /// <summary>
        /// the length of the material interval of the line (origin, unit direction) that contains the parameter tc (0 if tc is outside the material):
        /// the crossings sorted, equal ones (a duplicated triangle) merged, the intervals between an odd and the next crossing are inside
        /// </summary>
        public static double IntervalThickness(DMesh3 mesh, Vector3d origin, Vector3d dir, double tc)
        {
            var ts = new List<double>();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d e1 = b - a, e2 = c - a, p = dir.Cross(e2);
                double det = e1.Dot(p);
                if (Math.Abs(det) < 1e-300) continue;
                Vector3d s = origin - a;
                double u = s.Dot(p) / det;
                if (u < 0 || u > 1) continue;
                Vector3d q = s.Cross(e1);
                double v = dir.Dot(q) / det;
                if (v < 0 || u + v > 1) continue;
                ts.Add(e2.Dot(q) / det);
            }
            ts.Sort();
            var merged = new List<double>();
            foreach (double x in ts)
                if (merged.Count == 0 || x - merged[merged.Count - 1] > 1e-9) merged.Add(x);
            for (int k = 0; k + 1 < merged.Count; ++k)
                if (merged[k] <= tc && tc < merged[k + 1])
                    return (k % 2 == 0) ? merged[k + 1] - merged[k] : 0;
            return 0;
        }

        // ---------------- screw ----------------

        /// <summary> the screw is still a body of its own: exactly two components </summary>
        public static PartCheck ScrewSeparate() { return new PartCheck() { Name = "screwSeparate", Functional = true, Run = screwSeparate }; }

        static CheckResult screwSeparate(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            var r = new CheckResult() { Name = "screwSeparate", Functional = true, Requirement = "2 components" };
            if (mesh.TriangleCount == 0) { r.Detail = "empty"; return r; }
            List<Component> comps = Components(mesh, WeldTolerance(mesh));
            r.Value = comps.Count; r.Pass = comps.Count == 2;
            r.Detail = string.Join(" / ", comps.Select(c => c.Triangles + " tri"));
            return r;
        }

        /// <summary> the smaller body has the screw's box: each extent within 2 deviations plus 1 % of the screw's size of the construction </summary>
        public static PartCheck ScrewBox() { return new PartCheck() { Name = "screwBox", Functional = true, Run = screwBox }; }

        static CheckResult screwBox(ReferencePart part, DMesh3 mesh, bool afterCut)
        {
            double tol = 2 * part.Deviation + 0.01 * part.Targets["screwSize"];
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var r = new CheckResult() { Name = "screwBox", Functional = true, Value = double.NaN, Requirement = "extent error <= " + tol.ToString("0.#####", inv) };
            if (mesh.TriangleCount == 0) { r.Detail = "empty"; return r; }
            List<Component> comps = Components(mesh, WeldTolerance(mesh));
            if (comps.Count < 2) { r.Detail = comps.Count + " component(s): no screw"; return r; }
            Component screw = comps[comps.Count - 1];
            var box = AxisAlignedBox3d.Empty;
            foreach (int v in screw.Vertices) box.Contain(part.Frame.Inverse(mesh.GetVertex(v)));
            Vector3d ext = box.Max - box.Min;
            double err = Math.Max(Math.Abs(ext.x - part.ScrewExtent.x), Math.Max(Math.Abs(ext.y - part.ScrewExtent.y), Math.Abs(ext.z - part.ScrewExtent.z)));
            r.Value = err; r.Pass = err <= tol;
            r.Detail = "extent " + ext.x.ToString("0.#####", inv) + " x " + ext.y.ToString("0.#####", inv) + " x " + ext.z.ToString("0.#####", inv);
            return r;
        }
    }
}
