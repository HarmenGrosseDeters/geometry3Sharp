using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// IntrinsicTriangulation, DelaunayRefine and ExtractCommonSubdivision. The geometry is not changed by flips or insertions,
    /// so the total area, the Euler characteristic, the angle sum of every input vertex and the edge-length triangle inequalities
    /// are invariants; FlipToDelaunay ends Delaunay with non-negative interior cotan weights; a traced edge has the edge's length
    /// and lies on the surface; inserted vertices lie on the surface; a Success of the refinement meets its angle bound; a Success
    /// of the common subdivision is a mesh of the same area on the surface. Relations: 2^k (the same flips, the lengths scaled
    /// exactly), a permuted, a flipped and a subdivided input (the same Delaunay triangulation: the sorted edge lengths), reuse.
    /// </summary>
    public static class IntrinsicStage
    {
        const string S = "intrinsic";

        static DMesh3 Jitter(DMesh3 m, double amount, int seed)
        {
            var rng = new Random(seed);
            return Shapes.Map(m, p => p + amount * new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5));
        }

        static DMesh3 OpenGrid(int nx, int ny, double sy, double amp, int seed)
        {
            var rng = new Random(seed);
            var m = new DMesh3();
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) {
                bool inner = i > 0 && j > 0 && i < nx - 1 && j < ny - 1;
                double jx = inner ? 0.3 * (rng.NextDouble() - 0.5) : 0, jy = inner ? 0.3 * (rng.NextDouble() - 0.5) : 0;
                double x = (i + jx) / (nx - 1), y = sy * (j + jy) / (ny - 1);
                m.AppendVertex(new Vector3d(x, y, amp * Math.Sin(4 * x + 3 * y)));
            }
            for (int j = 0; j + 1 < ny; ++j) for (int i = 0; i + 1 < nx; ++i) {
                int a = j * nx + i, b = a + 1, c = a + nx + 1, d = a + nx;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            return m;
        }

        static (string, DMesh3)[] Meshes() => new[] {
            ("sphere jittered", Jitter(Shapes.Sphere(Vector3d.Zero, 1, 6), 0.03, 1)),
            ("torus jittered", Jitter(Shapes.Torus(Vector3d.Zero, 1, 0.35, 14, 9), 0.02, 2)),
            ("box 12", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5))),
            ("needle box", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.01, 0.5))),
            ("star prism", Shapes.Rotate(Shapes.Prism(Shapes.Star(7, 1.0, 0.3), -0.2, 0.2), new Vector3d(1, 2, 3), 37)),
            ("open grid", OpenGrid(9, 8, 1, 0.2, 3)),
            ("open sliver grid", OpenGrid(12, 5, 0.05, 0.0, 4)),
            ("open stretched terrain", OpenGrid(7, 9, 6, 0.4, 5)),
            ("cylinder", Jitter(Shapes.Cylinder(Vector3d.Zero, 1, 3, 11), 0.01, 6)),
        };

        static double MeshArea(DMesh3 m) => Checks.Area(m);

        static double IntrinsicArea(IntrinsicTriangulation T) { double a = 0; for (int t = 0; t < T.TriangleCount; ++t) a += T.TriangleArea(t); return a; }

        static int Edges(IntrinsicTriangulation T) { int interior = 0, boundary = 0; for (int h = 0; h < T.HalfedgeCount; ++h) if (T.Twin(h) >= 0) ++interior; else ++boundary; return interior / 2 + boundary; }

        static double[] SortedLengths(IntrinsicTriangulation T, double scale = 1)
        {
            var l = new List<double>();
            for (int h = 0; h < T.HalfedgeCount; ++h) if (T.Twin(h) < h) l.Add(T.Length(h) * scale);
            l.Sort();
            return l.ToArray();
        }

        static string Invariants(IntrinsicTriangulation T, DMesh3 mesh, double extent, double[] angleSums, bool checkTrace)
        {
            if (!T.CheckConsistency(out string err)) return "CheckConsistency: " + err;
            double ma = MeshArea(mesh), ia = IntrinsicArea(T);
            if (T.MollificationDelta == 0 && Math.Abs(ma - ia) > 1e-9 * ma) return $"area {ia:R} vs the mesh {ma:R}";
            int euler = T.VertexCount - Edges(T) + T.TriangleCount;
            int meshEuler = Checks.Euler(mesh);
            if (euler != meshEuler) return $"Euler {euler} vs the mesh {meshEuler}";
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                double a = T.Length(h), b = T.Length(IntrinsicTriangulation.Next(h)), c = T.Length(IntrinsicTriangulation.Prev(h));
                if (!(a > 0) || double.IsNaN(a) || double.IsInfinity(a)) return $"length of halfedge {h}: {a}";
                if (!(a < b + c)) return $"triangle inequality at halfedge {h}: {a:R} vs {b:R} + {c:R}";
            }
            if (angleSums != null && T.MollificationDelta == 0) for (int i = 0; i < T.InputVertexCount; ++i) {
                double s = 0; int h0 = T.VertexHalfedge(i), h = h0, guard = 0;
                // sum of the corner angles around the vertex, through all its triangles
                for (int hh = 0; hh < T.HalfedgeCount; ++hh) if (T.Tail(hh) == i) s += T.CornerAngle(hh);
                if (Math.Abs(s - angleSums[T.VertexID(i)]) > 1e-8) return $"angle sum of vertex {T.VertexID(i)}: {s:R} vs the mesh {angleSums[T.VertexID(i)]:R}";
                if (Math.Abs(T.AngleSum(i) - s) > 1e-8) return $"AngleSum({i}) {T.AngleSum(i):R} vs the corners {s:R}";
            }
            if (checkTrace) {
                for (int h = 0; h < T.HalfedgeCount; h += Math.Max(1, T.HalfedgeCount / 60)) {
                    List<Vector3d> tr = T.TraceEdge(h);
                    if (tr == null || tr.Count < 2) return $"TraceEdge({h}) has {(tr == null ? 0 : tr.Count)} points";
                    double len = 0; for (int k = 1; k < tr.Count; ++k) len += (tr[k] - tr[k - 1]).Length;
                    if (T.MollificationDelta == 0 && Math.Abs(len - T.Length(h)) > 1e-7 * Math.Max(T.Length(h), 1e-3 * extent)) return $"TraceEdge({h}) has the length {len:R}, the edge {T.Length(h):R}";
                    if ((tr[0] - T.VertexPosition(T.Tail(h))).Length > 1e-9 * extent || (tr[tr.Count - 1] - T.VertexPosition(T.Head(h))).Length > 1e-7 * extent) return $"TraceEdge({h}) does not connect its vertices: ends {(tr[0] - T.VertexPosition(T.Tail(h))).Length:E2} and {(tr[tr.Count - 1] - T.VertexPosition(T.Head(h))).Length:E2} away";
                    foreach (Vector3d q in tr) if (Checks.DistanceTo(mesh, q) > 1e-7 * extent) return $"TraceEdge({h}): a point {Checks.DistanceTo(mesh, q):E2} off the surface";
                }
                for (int i = T.InputVertexCount; i < T.VertexCount; i += Math.Max(1, (T.VertexCount - T.InputVertexCount) / 40)) {
                    Vector3d p = T.VertexPosition(i);
                    if (Checks.DistanceTo(mesh, p) > 1e-7 * extent) return $"inserted vertex {i} is {Checks.DistanceTo(mesh, p):E2} off the surface";
                    T.GetInputLocation(i, out int tid, out Vector3d bary);
                    if (!mesh.IsTriangle(tid)) return $"inserted vertex {i}: triangle {tid}";
                    Index3i tv = mesh.GetTriangle(tid);
                    Vector3d q = bary.x * mesh.GetVertex(tv.a) + bary.y * mesh.GetVertex(tv.b) + bary.z * mesh.GetVertex(tv.c);
                    if ((q - p).Length > 1e-9 * extent) return $"inserted vertex {i}: GetInputLocation is {(q - p).Length:E2} from VertexPosition";
                    if (bary.x < -1e-9 || bary.y < -1e-9 || bary.z < -1e-9) return $"inserted vertex {i}: barycentric {bary}";
                }
            }
            return "";
        }

        static double[] AngleSums(DMesh3 m)
        {
            var s = new double[m.MaxVertexID];
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    Vector3d a = m.GetVertex(tv[k]), b = m.GetVertex(tv[(k + 1) % 3]), c = m.GetVertex(tv[(k + 2) % 3]);
                    s[tv[k]] += Vector3d.AngleR((b - a).Normalized, (c - a).Normalized);
                }
            }
            return s;
        }

        static string SameLengths(double[] a, double[] b, double rel)
        {
            if (a.Length != b.Length) return $"edges {a.Length} vs {b.Length}";
            for (int i = 0; i < a.Length; ++i) if (Math.Abs(a[i] - b[i]) > rel * Math.Max(a[i], b[i])) return $"sorted length {i}: {a[i]:R} vs {b[i]:R}";
            return "";
        }

        public static void Run()
        {
            foreach (var (name, mesh) in Meshes()) {
                try { One(name, mesh); }
                catch (Exception e) { Program.Report(S, name, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        static void One(string name, DMesh3 mesh)
        {
            double extent = Checks.Bounds(mesh).Diagonal.Length;
            double[] sums = AngleSums(mesh);
            var T = new IntrinsicTriangulation(mesh);
            if (T.MollificationDelta != 0) Program.Info(S, name, "mollified by " + T.MollificationDelta.ToString("R"));
            Program.Report(S, name + " | initial invariants", Invariants(T, mesh, extent, sums, true));
            int flips = Program.Timed(() => T.FlipToDelaunay(), out long ms);
            Program.Report(S, name + " | FlipToDelaunay is Delaunay", T.IsDelaunay() ? "" : "not Delaunay after " + flips + " flips", ms);
            Program.Report(S, name + " | invariants after the flips", Invariants(T, mesh, extent, sums, true));
            string neg = ""; for (int h = 0; h < T.HalfedgeCount && neg == ""; ++h) if (T.Twin(h) >= 0 && T.CotanWeight(h) < -1e-9) neg = $"interior halfedge {h}: cotan weight {T.CotanWeight(h):R}";
            Program.Report(S, name + " | non-negative interior cotan weights", neg);
            Program.Report(S, name + " | a second FlipToDelaunay flips nothing", T.FlipToDelaunay() == 0 ? "" : "flipped again");
            double[] lengths = SortedLengths(T);
            // 2^k: the same flips, the lengths scaled exactly
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                var Ts = new IntrinsicTriangulation(Shapes.Map(mesh, p => p * s));
                int fs = Ts.FlipToDelaunay();
                Program.Report(S, name + " | scale " + s + " flips", fs == flips ? "" : $"{fs} vs {flips}");
                string same = ""; if (Ts.HalfedgeCount != T.HalfedgeCount) same = "halfedges"; else for (int h = 0; h < T.HalfedgeCount && same == ""; ++h) if (Ts.Length(h) != T.Length(h) * s || Ts.Tail(h) != T.Tail(h)) same = $"halfedge {h}: {Ts.Length(h):R} vs {T.Length(h) * s:R}";
                Program.Report(S, name + " | scale " + s + " bit for bit", same);
            }
            // the Delaunay triangulation is unique (no cocircular quadruples on the jittered inputs): the same edge lengths for an
            // equivalent input
            bool generic = name.Contains("jittered") || name.StartsWith("open");
            {
                var Tp = new IntrinsicTriangulation(Shapes.Permute(mesh, 11)); Tp.FlipToDelaunay();
                string same = SameLengths(lengths, SortedLengths(Tp), 1e-9);
                if (generic) Program.Report(S, name + " | permuted: the same Delaunay edges", same); else if (same != "") Program.Info(S, name + " | permuted", "cocircular input: " + same);
                var fl = new DMesh3(mesh); fl.ReverseOrientation();
                var Tf = new IntrinsicTriangulation(fl); Tf.FlipToDelaunay();
                same = SameLengths(lengths, SortedLengths(Tf), 1e-9);
                if (generic) Program.Report(S, name + " | flipped: the same Delaunay edges", same); else if (same != "") Program.Info(S, name + " | flipped", "cocircular input: " + same);
                foreach (var (sn, f, sc) in LoftStage.Sims()) {
                    var Tr = new IntrinsicTriangulation(Shapes.Map(mesh, f)); Tr.FlipToDelaunay();
                    same = SameLengths(lengths.Select(x => x * sc).ToArray(), SortedLengths(Tr), sn.Contains("utm") ? 1e-4 : 1e-9);
                    if (generic) Program.Report(S, name + " | " + sn + ": the same Delaunay edges", same); else if (same != "") Program.Info(S, name + " | " + sn, "cocircular input: " + same);
                }
                // subdivided in the planes of the triangles: the same surface; its Delaunay triangulation has the same area
                DMesh3 sub = GeodesicStage.Subdivide(mesh);
                var Tsub = new IntrinsicTriangulation(sub); Tsub.FlipToDelaunay();
                Program.Report(S, name + " | subdivided input: invariants", Invariants(Tsub, sub, extent, AngleSums(sub), false));
                Program.Report(S, name + " | subdivided input: Delaunay", Tsub.IsDelaunay() ? "" : "not Delaunay");
            }
            // refinement
            foreach (double angle in new[] { 0.0, 25.0, 30.0 }) {
                string c = name + " | refine " + angle;
                var R = new IntrinsicTriangulation(mesh);
                IntrinsicRefinementResult res = Program.Timed(() => R.DelaunayRefine(angle), out long rms);
                Program.Report(S, c + " | time", "", rms);
                Program.Report(S, c + " | invariants", Invariants(R, mesh, extent, sums, true));
                if (res.Status == IntrinsicRefinementStatus.Success) {
                    Program.Report(S, c + " | Delaunay", R.IsDelaunay(1e-8) ? "" : "Success, but not Delaunay");
                    double min = R.MinAngle() * 180 / Math.PI;
                    Program.Report(S, c + " | reported MinAngle", Math.Abs(min - res.MinAngle) < 1e-6 ? "" : $"{res.MinAngle:R} vs {min:R}");
                    if (res.SkippedTriangles == 0) Program.Report(S, c + " | the angle bound", min >= angle - 1e-6 ? "" : $"Success with a minimum angle of {min:R} and no skipped triangles");
                    string w = ""; for (int h = 0; h < R.HalfedgeCount && w == ""; ++h) if (R.CotanWeight(h) < -1e-9) w = $"halfedge {h} (twin {R.Twin(h)}): cotan weight {R.CotanWeight(h):R}";
                    Program.Report(S, c + " | non-negative cotan weights, boundary too", w);
                    Program.Report(S, c + " | counters", res.NegativeWeights == 0 && res.RemainingBadTriangles == 0 ? "" : $"Success with NegativeWeights {res.NegativeWeights}, RemainingBadTriangles {res.RemainingBadTriangles}");
                } else Program.Info(S, c, res.ToString());
                Program.Report(S, c + " | inserted count", R.VertexCount - R.InputVertexCount == res.InsertedVertices ? "" : $"{R.VertexCount - R.InputVertexCount} vs {res.InsertedVertices}");
                // repeat and 2^k
                var R2 = new IntrinsicTriangulation(mesh); IntrinsicRefinementResult res2 = R2.DelaunayRefine(angle);
                Program.Report(S, c + " | repeat", res2.InsertedVertices == res.InsertedVertices && res2.Flips == res.Flips && res2.Status == res.Status ? "" : $"{res2} vs {res}");
                var Rs = new IntrinsicTriangulation(Shapes.Map(mesh, p => p * 1024)); IntrinsicRefinementResult ress = Rs.DelaunayRefine(angle);
                Program.Report(S, c + " | scale 1024", ress.InsertedVertices == res.InsertedVertices && ress.Flips == res.Flips && ress.Status == res.Status ? "" : $"{ress} vs {res}");
                // a second refinement of a successful one inserts nothing
                if (res.Status == IntrinsicRefinementStatus.Success) { IntrinsicRefinementResult again = R.DelaunayRefine(angle); Program.Report(S, c + " | refining again inserts nothing", again.InsertedVertices == 0 ? "" : $"{again.InsertedVertices} more vertices ({again.Status})"); }
                // the common subdivision of the refined triangulation
                var Rc = new IntrinsicTriangulation(mesh); Rc.DelaunayRefine(angle);
                IntrinsicCommonSubdivision cs = Program.Timed(() => Rc.ExtractCommonSubdivision(), out long cms);
                Program.Report(S, c + " | subdivision time", "", cms);
                if (cs.Status != CommonSubdivisionStatus.Success) { Program.Info(S, c + " | common subdivision", cs.Status + " " + cs.Error); continue; }
                double fa = cs.Faces.Sum(f => f.Area), ma = MeshArea(mesh);
                Program.Report(S, c + " | subdivision area", Rc.MollificationDelta != 0 || Math.Abs(fa - ma) <= 1e-9 * ma ? "" : $"{fa:R} vs the mesh {ma:R}");
                DMesh3 cm = cs.ToMesh();
                Program.Report(S, c + " | subdivision mesh area", Rc.MollificationDelta != 0 || Math.Abs(MeshArea(cm) - ma) <= 1e-8 * ma ? "" : $"{MeshArea(cm):R} vs the mesh {ma:R}");
                string off = ""; foreach (Vector3d p in cs.Points) if (Checks.DistanceTo(mesh, p) > 1e-7 * extent) { off = $"a point {Checks.DistanceTo(mesh, p):E2} off the surface"; break; }
                Program.Report(S, c + " | subdivision points on the surface", off);
                if (Checks.ClosedManifold(mesh) == "") {
                    string cl = Checks.ClosedManifold(cm);
                    Program.Report(S, c + " | subdivision of a closed mesh is closed", cl);
                    if (cl == "") Program.Report(S, c + " | subdivision volume", Program.Rel("volume", Checks.Volume(cm), Checks.Volume(mesh), 1e-9));
                }
                string faces = ""; foreach (var f in cs.Faces) { if (f.Corners.Length < 3) { faces = "a face with " + f.Corners.Length + " corners"; break; } if (!(f.Area >= 0)) { faces = "a face of area " + f.Area; break; } if (!mesh.IsTriangle(f.InputTriangle) || f.IntrinsicTriangle < 0 || f.IntrinsicTriangle >= Rc.TriangleCount) { faces = "a face with invalid triangle ids"; break; } }
                Program.Report(S, c + " | subdivision faces", faces);
            }
        }
    }
}
