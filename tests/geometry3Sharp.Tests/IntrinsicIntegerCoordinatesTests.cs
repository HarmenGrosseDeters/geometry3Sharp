using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using g3.CoreMathPort;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The integer (normal) coordinates of IntrinsicTriangulation (EnableIntegerCoordinates; Gillespie, Sharp and
    /// Crane 2021): kept through flips and splits of boundary edges, and ExtractCommonSubdivision from them. The
    /// meshes are those of benchmarks/IntrinsicIntegerCoordinates, small, with CoreMath in place of Math.Sin / Cos
    /// and without System.Random, so the inputs are the same bits on every platform.
    /// </summary>
    public class IntrinsicIntegerCoordinatesTests
    {
        readonly ITestOutputHelper output;
        public IntrinsicIntegerCoordinatesTests(ITestOutputHelper output) { this.output = output; }

        // ------------------------------------------------------------------ meshes

        // (n + 1)^2 vertices at (a * (i + shear * j), j)
        static DMesh3 ShearedGrid(int n, double a, double shear, Similarity sim)
        {
            var mesh = new DMesh3();
            int[,] vid = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    vid[i, j] = mesh.AppendVertex(sim.Apply(new Vector3d(a * (i + shear * j), j, 0)));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        // rows shifted by half a column, dy apart: every triangle has an angle near 180 degrees, also at the boundary
        static DMesh3 ObtusePlane(int nx, int ny, double dy, Similarity sim)
        {
            var mesh = new DMesh3();
            var rows = new List<int[]>();
            for (int j = 0; j <= ny; ++j) {
                bool odd = (j % 2) == 1;
                int[] row = new int[odd ? nx : nx + 1];
                for (int i = 0; i < row.Length; ++i)
                    row[i] = mesh.AppendVertex(sim.Apply(new Vector3d(i + (odd ? 0.5 : 0), j * dy, 0)));
                rows.Add(row);
            }
            for (int j = 0; j < ny; ++j) {
                int[] A = rows[j], B = rows[j + 1];
                if (j % 2 == 0) {
                    for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i]);
                    for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i + 1], B[i + 1], B[i]);
                } else {
                    for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], B[i + 1], B[i]);
                    for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i + 1]);
                }
            }
            return mesh;
        }

        // rings of 'per' vertices; ring k is joined to ring k + 1 turned by 'shear' vertices. torus: closed in both
        // directions (major radius 3, minor 1), else a cylinder of radius 1 and height 4
        static DMesh3 Sheared(int rings, int per, int shear, bool torus, Similarity sim)
        {
            var mesh = new DMesh3();
            int[,] vid = new int[rings, per];
            for (int k = 0; k < rings; ++k)
                for (int i = 0; i < per; ++i) {
                    double ph = 2 * Math.PI * i / per;
                    Vector3d p;
                    if (torus) {
                        double th = 2 * Math.PI * k / rings;
                        double rr = 3 + CoreMath.Cos(ph);
                        p = new Vector3d(rr * CoreMath.Cos(th), rr * CoreMath.Sin(th), CoreMath.Sin(ph));
                    } else
                        p = new Vector3d(CoreMath.Cos(ph), CoreMath.Sin(ph), 4.0 * k / (rings - 1));
                    vid[k, i] = mesh.AppendVertex(sim.Apply(p));
                }
            int last = torus ? rings : rings - 1;
            for (int k = 0; k < last; ++k) {
                int k1 = (k + 1) % rings;
                for (int i = 0; i < per; ++i) {
                    int i1 = (i + 1) % per, j = (i + shear) % per, j1 = (i + shear + 1) % per;
                    if (torus) {
                        mesh.AppendTriangle(vid[k, i], vid[k1, j], vid[k, i1]);
                        mesh.AppendTriangle(vid[k, i1], vid[k1, j], vid[k1, j1]);
                    } else {
                        mesh.AppendTriangle(vid[k, i], vid[k, i1], vid[k1, j]);
                        mesh.AppendTriangle(vid[k, i1], vid[k1, j1], vid[k1, j]);
                    }
                }
            }
            return mesh;
        }

        // latitude rings, alternately shifted by half a step; every other vertex of a ring moved by jitter steps
        // towards its neighbour (needles), every other vertex pushed outwards by spike (cone vertices of both signs)
        static DMesh3 RingSphere(int rings, int perRing, double spike, double jitter, Similarity sim)
        {
            var mesh = new DMesh3();
            var pts = new List<Vector3d>();
            Func<Vector3d, int> addV = (p) => { pts.Add(p); return mesh.AppendVertex(sim.Apply(p)); };
            int north = addV(new Vector3d(0, 0, 1));
            var ring = new List<int[]>();
            for (int k = 1; k < rings; ++k) {
                double th = Math.PI * k / rings;
                int[] r = new int[perRing];
                for (int i = 0; i < perRing; ++i) {
                    double ph = 2 * Math.PI * (i + ((k % 2 == 1) ? 0.5 : 0) + ((i % 2 == 1) ? jitter : -jitter)) / perRing;
                    double rr = ((i + k) % 2 == 0) ? 1 + spike : 1;
                    r[i] = addV(new Vector3d(rr * CoreMath.Sin(th) * CoreMath.Cos(ph), rr * CoreMath.Sin(th) * CoreMath.Sin(ph), rr * CoreMath.Cos(th)));
                }
                ring.Add(r);
            }
            int south = addV(new Vector3d(0, 0, -1));
            Action<int, int, int> add = (a, b, c) => {
                Vector3d pa = pts[a], pb = pts[b], pc = pts[c];
                if ((pb - pa).Cross(pc - pa).Dot(pa + pb + pc) < 0) { int t = b; b = c; c = t; }
                mesh.AppendTriangle(a, b, c);
            };
            for (int i = 0; i < perRing; ++i) {
                add(north, ring[0][i], ring[0][(i + 1) % perRing]);
                add(south, ring[rings - 2][i], ring[rings - 2][(i + 1) % perRing]);
            }
            for (int k = 0; k + 1 < ring.Count; ++k) {
                int[] A = ring[k], B = ring[k + 1];
                for (int i = 0; i < perRing; ++i) {
                    int i1 = (i + 1) % perRing;
                    if (k % 2 == 1) { add(A[i], A[i1], B[i]); add(A[i1], B[i1], B[i]); }
                    else { add(B[i], B[i1], A[i]); add(B[i1], A[i1], A[i]); }
                }
            }
            return mesh;
        }

        // n x n unit grid; every cell has a fifth vertex at distance eps from the middle of its diagonal: caps
        static DMesh3 CapGrid(int n, double eps, Similarity sim)
        {
            var mesh = new DMesh3();
            int[,] vid = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    vid[i, j] = mesh.AppendVertex(sim.Apply(new Vector3d(i, j, 0)));
            double o = eps / Math.Sqrt(2.0);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = vid[i, j], b = vid[i + 1, j], c = vid[i + 1, j + 1], d = vid[i, j + 1];
                    int m = mesh.AppendVertex(sim.Apply(new Vector3d(i + 0.5 + o, j + 0.5 - o, 0)));
                    mesh.AppendTriangle(a, c, d);
                    mesh.AppendTriangle(a, m, c);
                    mesh.AppendTriangle(a, b, m);
                    mesh.AppendTriangle(b, c, m);
                }
            return mesh;
        }

        public static IEnumerable<object[]> MeshNames() => new[] {
            "sheared grid a=1", "sheared grid a=100", "sheared grid a=1e4", "sheared grid a=1e6", "grid",
            "obtuse plane dy=0.05", "obtuse plane dy=0.005", "obtuse plane dy=0.0005",
            "sheared cylinder s=3", "sheared cylinder s=7", "sheared torus s=3", "sheared torus s=7",
            "ring sphere", "spiky ring sphere", "clustered ring sphere j=0.3", "clustered ring sphere j=0.49",
            "clustered ring sphere j=0.499999", "cap grid eps=1e-3", "cap grid eps=1e-9" }.Select(n => new object[] { n });

        static DMesh3 Make(string name, Similarity sim)
        {
            switch (name) {
                case "sheared grid a=1": return ShearedGrid(8, 1, 0.375, sim);
                case "sheared grid a=100": return ShearedGrid(10, 100, 0.375, sim);
                case "sheared grid a=1e4": return ShearedGrid(10, 1e4, 0.375, sim);
                case "sheared grid a=1e6": return ShearedGrid(8, 1e6, 0.375, sim);
                case "grid": return ShearedGrid(6, 1, 0, sim);
                case "obtuse plane dy=0.05": return ObtusePlane(6, 40, 0.05, sim);
                case "obtuse plane dy=0.005": return ObtusePlane(6, 40, 0.005, sim);
                case "obtuse plane dy=0.0005": return ObtusePlane(6, 30, 0.0005, sim);
                case "sheared cylinder s=3": return Sheared(12, 16, 3, false, sim);
                case "sheared cylinder s=7": return Sheared(12, 16, 7, false, sim);
                case "sheared torus s=3": return Sheared(14, 16, 3, true, sim);
                case "sheared torus s=7": return Sheared(14, 16, 7, true, sim);
                case "ring sphere": return RingSphere(30, 10, 0, 0, sim);
                case "spiky ring sphere": return RingSphere(20, 10, 0.3, 0, sim);
                case "clustered ring sphere j=0.3": return RingSphere(14, 12, 0, 0.3, sim);
                case "clustered ring sphere j=0.49": return RingSphere(14, 12, 0, 0.49, sim);
                case "clustered ring sphere j=0.499999": return RingSphere(14, 12, 0, 0.499999, sim);
                case "cap grid eps=1e-3": return CapGrid(8, 1e-3, sim);
                case "cap grid eps=1e-9": return CapGrid(8, 1e-9, sim);
            }
            throw new ArgumentException(name);
        }

        // ------------------------------------------------------------------ pipelines

        // The steps on one triangulation; with integers == false the same steps without the layer (the layer does
        // not change what the flips and splits do, so both describe the same triangulation, slot for slot).
        //   0: flips to Delaunay
        //   1: flips, and splits of the boundary edges opposite obtuse angles (DelaunayRefine(0))
        //   2: splits of every third boundary edge at 1/4, 1/2 or 5/8 first, then flips, then DelaunayRefine(0)
        static IntrinsicTriangulation Build(DMesh3 mesh, int step, bool integers)
        {
            var T = new IntrinsicTriangulation(mesh);
            if (integers) Assert.True(T.EnableIntegerCoordinates());
            if (step == 2) {
                double[] at = { 0.25, 0.5, 0.625 };
                int H = T.HalfedgeCount, count = 0;
                for (int h = 0; h < H; ++h)
                    if (T.Twin(h) < 0 && (count++ % 3) == 0) Assert.True(T.SplitEdge(h, at[(count / 3) % 3]) >= 0);
            }
            if (step == 0) T.FlipToDelaunay();
            else T.DelaunayRefine(0);
            Assert.Equal(integers, T.HasIntegerCoordinates);
            return T;
        }

        // ------------------------------------------------------------------ checks

        class Report
        {
            public int Crossings, Faces;
            public double AreaError;
        }

        /// <summary>
        /// null if cs is the cell complex of a common subdivision of T and mesh with consistent geometry, else the
        /// first defect: Success; consistently oriented faces, every edge on two faces or on one (as many of those
        /// as T has boundary edges); the Euler characteristic of the input; every crossing with four edges and
        /// every triangulation vertex and crossing used; as many crossings as the normal coordinates count; every
        /// face in a triangle of the mesh, its corners in order at the points its barycentric coordinates give,
        /// convex and counter-clockwise there, with the area stated (as a part of its input triangle: with
        /// mollification the areas are those of the mollified triangles); the areas summing to every input triangle,
        /// without mollification to those of the mesh, and (to tol) to every intrinsic triangle.
        /// </summary>
        static string Defect(DMesh3 mesh, IntrinsicTriangulation T, IntrinsicCommonSubdivision cs, Report rep = null, double intrinsicTol = 1e-6)
        {
            if (cs.Status != CommonSubdivisionStatus.Success) return "status " + cs.Status + ": " + cs.Error;
            if (cs.Error != null) return "Success with an error text";
            var directed = new HashSet<(int, int)>();
            var edges = new Dictionary<(int, int), int>();
            var valence = new Dictionary<int, int>();
            double size = mesh.CachedBounds.MaxDim;
            // (far from the origin the points are rounded to the unit of their coordinates)
            double pointTol = 1e-9 * size + 64 * MathUtil.Epsilon * Math.Max(mesh.CachedBounds.Min.MaxAbs, mesh.CachedBounds.Max.MaxAbs);
            var part = new Dictionary<int, double>();
            var shares = new List<double>();
            var inArea = new Dictionary<int, double>();
            double[] itArea = new double[T.TriangleCount];
            foreach (var f in cs.Faces) {
                int n = f.Corners.Length;
                if (n < 3 || f.Barycentric.Length != n) return "face with " + n + " corners";
                if (mesh.IsTriangle(f.InputTriangle) == false) return "face in no mesh triangle";
                if (f.IntrinsicTriangle < 0 || f.IntrinsicTriangle >= T.TriangleCount) return "face in no intrinsic triangle";
                Index3i tri = mesh.GetTriangle(f.InputTriangle);
                Vector3d A = mesh.GetVertex(tri.a), B = mesh.GetVertex(tri.b), C = mesh.GetVertex(tri.c);
                var p = new Vector3d[n];
                for (int k = 0; k < n; ++k) {
                    int a = f.Corners[k], b = f.Corners[(k + 1) % n];
                    if (a < 0 || a >= cs.Points.Count) return "corner id out of range";
                    if (directed.Add((a, b)) == false) return $"two faces run along edge {a} -> {b}";
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    edges.TryGetValue(key, out int c);
                    edges[key] = c + 1;
                    if (c == 0) {
                        valence.TryGetValue(a, out int va); valence[a] = va + 1;
                        valence.TryGetValue(b, out int vb); valence[b] = vb + 1;
                    }
                    Vector3d bc = f.Barycentric[k];
                    if (!(bc.x >= 0 && bc.y >= 0 && bc.z >= 0) || Math.Abs(bc.x + bc.y + bc.z - 1) > 1e-12) return "barycentric coordinates " + bc + " outside the triangle";
                    p[k] = bc.x * A + bc.y * B + bc.z * C;
                    if (p[k].Distance(cs.Points[a]) > pointTol) return $"corner {a} of a face is {p[k].Distance(cs.Points[a]) / size:E1} of the size from its point";
                }
                // convex and counter-clockwise in its input triangle (in barycentric coordinates, where the
                // triangle has the area 1/2: independent of the shape of the mesh triangle)
                Func<int, Vector2d> q = (k) => new Vector2d(f.Barycentric[k % n].y, f.Barycentric[k % n].z);
                double share = 0;
                for (int k = 0; k < n; ++k) {
                    Vector2d e1 = q(k + 1) - q(k), e2 = q(k + 2) - q(k + 1);
                    if (e1.x * e2.y - e1.y * e2.x < -1e-12) return "face that is not convex and counter-clockwise";
                    if (k >= 1 && k + 1 < n) { Vector2d d1 = q(k) - q(0), d2 = q(k + 1) - q(0); share += d1.x * d2.y - d1.y * d2.x; }
                }
                shares.Add(share);
                if (!(f.Area >= 0)) return "face area " + f.Area;
                part.TryGetValue(f.InputTriangle, out double pa); part[f.InputTriangle] = pa + share;
                inArea.TryGetValue(f.InputTriangle, out double ia); inArea[f.InputTriangle] = ia + f.Area;
                itArea[f.IntrinsicTriangle] += f.Area;
            }
            if (edges.Values.Any(c => c > 2)) return "an edge on more than two faces";
            int boundary = edges.Values.Count(c => c == 1);
            int tBoundary = Enumerable.Range(0, T.HalfedgeCount).Count(h => T.Twin(h) < 0);
            if (boundary != tBoundary) return $"{boundary} boundary edges, the triangulation has {tBoundary}";
            int chi = valence.Count - edges.Count + cs.Faces.Count;
            int chiInput = mesh.VertexCount - mesh.EdgeCount + mesh.TriangleCount;
            if (chi != chiInput) return $"Euler characteristic {chi}, the input has {chiInput}";
            foreach (var kv in valence)
                if (kv.Key >= T.VertexCount && kv.Value != 4) return $"crossing {kv.Key} has {kv.Value} edges";
            if (valence.Count != cs.Points.Count) return "points that no face uses";
            foreach (int t in mesh.TriangleIndices()) {
                if (inArea.TryGetValue(t, out double ia) == false) return "mesh triangle " + t + " without a face";
                if (Math.Abs(part[t] - 1) > 1e-9) return $"the faces of mesh triangle {t} cover {part[t]:R} of it";
                double a = mesh.GetTriArea(t);
                if (T.MollificationDelta == 0 && Math.Abs(ia - a) > 1e-9 * a) return $"faces of mesh triangle {t} sum to {ia:R}, its area is {a:R}";
            }
            // the stated areas are the parts of the (mollified) input triangles the corners give
            for (int k = 0; k < cs.Faces.Count; ++k) {
                var f = cs.Faces[k];
                double a = shares[k] * inArea[f.InputTriangle];
                if (Math.Abs(f.Area - a) > 1e-9 * inArea[f.InputTriangle]) return $"face area {f.Area:R}, its corners give {a:R}";
            }
            if (!(cs.MaxInputAreaError <= 1e-9)) return "MaxInputAreaError " + cs.MaxInputAreaError;
            double worst = 0;
            for (int t = 0; t < itArea.Length; ++t) {
                double a = T.TriangleArea(t);
                worst = Math.Max(worst, Math.Abs(itArea[t] - a) / Math.Max(a, 1e-300));
            }
            if (worst != cs.MaxIntrinsicAreaError) return $"MaxIntrinsicAreaError {cs.MaxIntrinsicAreaError:R}, the faces give {worst:R}";
            if (!(worst <= intrinsicTol)) return $"faces of an intrinsic triangle are off its area by a relative {worst:E1}";
            if (rep != null) { rep.Crossings = cs.Points.Count - T.VertexCount; rep.Faces = cs.Faces.Count; rep.AreaError = worst; }
            return null;
        }

        // null if the two results are the same subdivision (of triangulations with the same slots): the same
        // points up to tol (the crossings in any order) and, per input and intrinsic triangle, faces of the same
        // corner counts and areas
        static string Difference(IntrinsicCommonSubdivision a, IntrinsicCommonSubdivision b, int vertices, double size)
        {
            if (a.Points.Count != b.Points.Count) return $"{a.Points.Count - vertices} and {b.Points.Count - vertices} crossings";
            if (a.Faces.Count != b.Faces.Count) return $"{a.Faces.Count} and {b.Faces.Count} faces";
            double tol = 1e-8 * size;
            for (int v = 0; v < vertices; ++v)
                if (a.Points[v].Distance(b.Points[v]) > tol) return "vertex " + v + " differs";
            Func<Vector3d, double> key = (p) => p.x + 0.618 * p.y + 0.382 * p.z;
            var pb = b.Points.Skip(vertices).OrderBy(key).ToList();
            var keys = pb.Select(key).ToList();
            foreach (Vector3d p in a.Points.Skip(vertices)) {
                int i = keys.BinarySearch(key(p) - 3 * tol);
                if (i < 0) i = ~i;
                bool found = false;
                for (; i < pb.Count && keys[i] <= key(p) + 3 * tol && found == false; ++i) found = pb[i].Distance(p) <= tol;
                if (found == false) return "crossing " + p + " of the first is not in the second";
            }
            Func<IntrinsicCommonSubdivision, List<(int, int, int, double)>> faces = (cs) =>
                cs.Faces.Select(f => (f.InputTriangle, f.IntrinsicTriangle, f.Corners.Length, f.Area)).OrderBy(x => x).ToList();
            var fa = faces(a); var fb = faces(b);
            for (int k = 0; k < fa.Count; ++k)
                if (fa[k].Item1 != fb[k].Item1 || fa[k].Item2 != fb[k].Item2 || fa[k].Item3 != fb[k].Item3 || Math.Abs(fa[k].Item4 - fb[k].Item4) > 1e-7 * size * size)
                    return $"faces differ: {fa[k]} and {fb[k]}";
            return null;
        }

        // ------------------------------------------------------------------ tests

        [Theory]
        [MemberData(nameof(MeshNames))]
        public void Extraction_IsTheCommonSubdivision_AndAgreesWithTracing(string name)
        {
            for (int step = 0; step < 3; ++step) {
                DMesh3 mesh = Make(name, Similarity.Identity);
                var T = Build(mesh, step, true);
                var cs = T.ExtractCommonSubdivision();
                var rep = new Report();
                // (cap grid 1e-9: triangles with an area of 1e-9 of their size, where the rounding of the lengths shows)
                string defect = Defect(mesh, T, cs, rep, name == "cap grid eps=1e-9" ? 1e-2 : 1e-6);
                Assert.True(defect == null, $"{name}, step {step}: {defect}");
                // the same steps without the layer give the same triangulation, bit for bit
                var S = Build(mesh, step, false);
                Assert.Equal(T.TriangleCount, S.TriangleCount);
                for (int h = 0; h < T.HalfedgeCount; ++h) {
                    Assert.Equal(S.Tail(h), T.Tail(h));
                    Assert.Equal(S.Twin(h), T.Twin(h));
                    Assert.Equal(S.Length(h), T.Length(h));
                    Assert.Equal(S.Signpost(h), T.Signpost(h));
                }
                // and where its traces succeed, the same subdivision
                var traced = S.ExtractCommonSubdivision();
                if (name == "sheared grid a=100" && step == 1) Assert.True(cs.MaxInputAreaError > 0);       // (reported, not left at 0)
                string same = traced.Status == CommonSubdivisionStatus.Success ? Difference(cs, traced, T.VertexCount, mesh.CachedBounds.MaxDim) : null;
                Assert.True(same == null, $"{name}, step {step}: {same}");
                output.WriteLine($"{name}, step {step}: {T.VertexCount - T.InputVertexCount} splits, {rep.Crossings} crossings, {rep.Faces} faces, intrinsic area error {rep.AreaError:E1}, traced {traced.Status}");
            }
        }

        [Fact]
        public void TracedExtraction_FailsWhereTheIntegersSucceed()
        {
            // the point of the layer. Neighbouring vertices of a ring 2.6e-7 apart (938 vertices): after the flips
            // the traced subdivision is Inconsistent (a false crossing), the one from the integers is the common
            // subdivision. The faces of a needle triangle are off its area by a relative 1e-5: the rounding of the
            // lengths, reported in MaxIntrinsicAreaError and not a failure here.
            DMesh3 mesh = RingSphere(40, 24, 0, 0.499999, Similarity.Identity);
            var traced = Build(mesh, 0, false).ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, traced.Status);
            Assert.Empty(traced.Faces);
            var T = Build(mesh, 0, true);
            var cs = T.ExtractCommonSubdivision();
            string defect = Defect(mesh, T, cs, null, 1e-4);
            Assert.True(defect == null, defect);
            Assert.True(cs.MaxIntrinsicAreaError > 1e-6);
        }

        [Fact]
        public void FlippedDiagonal_OfASquare_HasOneCrossing()
        {
            // two triangles; the flipped diagonal crosses the input diagonal once, at the centre: four faces
            DMesh3 mesh = ShearedGrid(1, 1, 0, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            int diag = Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) >= 0);
            Assert.True(T.FlipEdge(diag));
            var cs = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, cs));
            Assert.Equal(5, cs.Points.Count);
            Assert.True(cs.Points[4].Distance(new Vector3d(0.5, 0.5, 0)) < 1e-15);
            Assert.Equal(4, cs.Faces.Count);
            foreach (var f in cs.Faces) {
                Assert.Equal(3, f.Corners.Length);
                Assert.Equal(0.25, f.Area, 15);
                // the crossing lies at the middle of the input diagonal: two coordinates of 1/2
                Vector3d b = f.Barycentric[Array.IndexOf(f.Corners, 4)];
                Assert.Equal(new[] { 0.0, 0.5, 0.5 }, new[] { b.x, b.y, b.z }.OrderBy(x => x).ToArray());
            }
            // flipped back: the input mesh again, no crossing
            Assert.True(T.FlipEdge(diag));
            cs = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, cs));
            Assert.Equal(4, cs.Points.Count);
            Assert.Equal(2, cs.Faces.Count);
        }

        [Fact]
        public void BoundarySplit_AfterAFlip_CountsTheCrossedInputEdges()
        {
            // 2 x 1 cells (i, j) -> (i, j): the diagonal of the first cell is flipped, so the input diagonal crosses
            // the new edge and ends at the corner (0, 0) on the bottom boundary. Splitting the bottom edge of that
            // cell: the new edge from the split vertex to the apex crosses the input diagonal (paper eq. 6: the
            // larger count of the two other edges, here 0 and 1)
            var mesh = new DMesh3();
            int[,] vid = new int[3, 2];
            for (int j = 0; j < 2; ++j) for (int i = 0; i < 3; ++i) vid[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int i = 0; i < 2; ++i) {
                mesh.AppendTriangle(vid[i, 0], vid[i + 1, 0], vid[i + 1, 1]);
                mesh.AppendTriangle(vid[i, 0], vid[i + 1, 1], vid[i, 1]);
            }
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            // the diagonal of the first cell: halfedge 2 of triangle 0, (1, 1) -> (0, 0)
            Assert.True(T.FlipEdge(2));
            int bottom = Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) < 0
                && T.VertexID(T.Tail(h)) == vid[0, 0] && T.VertexID(T.Head(h)) == vid[1, 0]);
            int m = T.SplitEdge(bottom, 0.25);
            Assert.True(m >= 0);
            Assert.True(T.HasIntegerCoordinates);
            var cs = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, cs));
            // two crossings of the input diagonal y = x: with the flipped diagonal at (1/2, 1/2), and with the edge
            // from the split vertex (1/4, 0) to (0, 1) at (1/5, 1/5)
            Assert.Equal(T.VertexCount + 2, cs.Points.Count);
            var crossings = cs.Points.Skip(T.VertexCount).OrderBy(p => p.x).ToList();
            Assert.True(crossings[0].Distance(new Vector3d(0.2, 0.2, 0)) < 1e-15, crossings[0].ToString());
            Assert.True(crossings[1].Distance(new Vector3d(0.5, 0.5, 0)) < 1e-15, crossings[1].ToString());
            // the same as tracing
            var S = new IntrinsicTriangulation(mesh);
            S.FlipEdge(2); S.SplitEdge(bottom, 0.25);
            Assert.Null(Difference(cs, S.ExtractCommonSubdivision(), T.VertexCount, 2));
        }

        [Fact]
        public void TriangleWithThreeSplitBoundaryEdges_MiddleTriangleLiesInTheInputTriangle()
        {
            // one input triangle, its three edges split at the middle: after the flips the middle triangle has no
            // side on an input edge and no input edge enters it
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(0, 0, 0)), mesh.AppendVertex(new Vector3d(1, 0, 0)), mesh.AppendVertex(new Vector3d(0.5, 0.875, 0)));
            Func<bool, IntrinsicTriangulation> build = (integers) => {
                var X = new IntrinsicTriangulation(mesh);
                if (integers) Assert.True(X.EnableIntegerCoordinates());
                for (int k = 0; k < 3; ++k) {
                    int h = Enumerable.Range(0, X.HalfedgeCount).First(x => X.Twin(x) < 0 && X.Tail(x) == k && X.Head(x) == (k + 1) % 3);
                    Assert.True(X.SplitEdge(h, 0.5) >= 0);
                }
                X.FlipToDelaunay();
                return X;
            };
            var T = build(true);
            var cs = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, cs));
            Assert.Equal(6, cs.Points.Count);
            Assert.Equal(4, cs.Faces.Count);
            Assert.Contains(cs.Faces, f => f.Corners.All(c => c >= 3));
            Assert.Null(Difference(cs, build(false).ExtractCommonSubdivision(), 6, 1));
        }

        [Fact]
        public void SingleTriangle_Unflipped_IsOneFace()
        {
            // no input edge enters it, and all its sides are boundary edges
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(0, 0, 0)), mesh.AppendVertex(new Vector3d(1, 0, 0)), mesh.AppendVertex(new Vector3d(0.5, 0.875, 0)));
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            var cs = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, cs));
            Assert.Single(cs.Faces);
            Assert.Equal(new[] { 0, 1, 2 }, cs.Faces[0].Corners);
        }

        [Fact]
        public void TrianglesWithoutArea_PositionsAreClampedToTheOrder()
        {
            // caps with the middle vertex exactly on the diagonal, no mollification: triangles without area, in
            // which the unfolded position of a crossing is not a number or lies outside its edge. The integers
            // still give the order; every face has an area of at least 0 and the faces tile the input triangles.
            DMesh3 mesh = CapGrid(4, 0, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh, 0);
            Assert.True(T.EnableIntegerCoordinates());
            T.FlipToDelaunay();
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.True(cs.Points.Count > T.VertexCount);
            foreach (Vector3d p in cs.Points) Assert.True(p.x >= 0 && p.x <= 4 && p.y >= 0 && p.y <= 4 && p.z == 0, p.ToString());
            foreach (var f in cs.Faces) {
                Assert.True(f.Area >= 0 && f.Area <= 0.5, "area " + f.Area);
                foreach (Vector3d b in f.Barycentric) Assert.True(b.x >= 0 && b.y >= 0 && b.z >= 0 && Math.Abs(b.x + b.y + b.z - 1) < 1e-12, b.ToString());
            }
            Assert.Equal(16.0, cs.Faces.Sum(f => f.Area), 9);
            Assert.True(cs.MaxInputAreaError <= 1e-6);
        }

        [Fact]
        public void AreasBeyondTheDoubleRange_AreReportedAsInconsistent()
        {
            // coordinates of 2^520: the lengths are finite, the areas are not. No Success with faces of infinite area
            var big = new Similarity("scale 2^520", Math.Pow(2, 520), Matrix3d.Identity, Vector3d.Zero);
            DMesh3 mesh = ShearedGrid(3, 1, 0.375, big);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            T.FlipToDelaunay();
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("do not sum to the input triangle areas", cs.Error);
            Assert.Empty(cs.Faces);
        }

        [Theory]
        [InlineData(200)]
        [InlineData(-200)]
        [InlineData(300)]
        [InlineData(480)]
        public void ExtremeScales_GiveTheSubdivisionOrAreReported(int exponent)
        {
            // squares of the lengths overflow from 2^512 on and the product of four lengths in the area from 2^256
            // on: up to there the result is the scaled one of the unit mesh, beyond it is that or it is reported,
            // never a Success with other content. (Small scales only down to 2^-200: below about 2^-256 the areas
            // underflow in the triangulation itself, whose flips then differ; #1352.)
            var sim = new Similarity("scale 2^" + exponent, Math.Pow(2, exponent), Matrix3d.Identity, Vector3d.Zero);
            DMesh3 unit = ShearedGrid(3, 1, 0.375, Similarity.Identity), mesh = ShearedGrid(3, 1, 0.375, sim);
            var cs0 = Build(unit, 0, true).ExtractCommonSubdivision();
            var cs = Build(mesh, 0, true).ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs0.Status);
            if (cs.Status != CommonSubdivisionStatus.Success) { Assert.Empty(cs.Faces); return; }
            Assert.Equal(cs0.Points.Count, cs.Points.Count);
            for (int i = 0; i < cs.Points.Count; ++i) Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
            Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
            for (int f = 0; f < cs.Faces.Count; ++f) {
                Assert.Equal(cs0.Faces[f].Corners, cs.Faces[f].Corners);
                Assert.Equal(cs0.Faces[f].Barycentric, cs.Faces[f].Barycentric);
                Assert.Equal(cs0.Faces[f].InputTriangle, cs.Faces[f].InputTriangle);
            }
            Assert.Equal(cs0.MaxIntrinsicAreaError, cs.MaxIntrinsicAreaError);
        }

        [Fact]
        public void SelfGluedTriangles_AtANeedleApex()
        {
            // a needle: the apex of degree 3 has an angle sum of 17 degrees. Its edges are flipped away one after
            // the other, as far as FlipEdge allows: the apex gets the degree 2, then 1 (a triangle glued to itself
            // along the last apex edge). The same subdivision as tracing after every flip.
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0)), c = mesh.AppendVertex(new Vector3d(0.5, 0.875, 0));
            int apex = mesh.AppendVertex(new Vector3d(0.5, 0.3125, 6));
            mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(a, b, apex); mesh.AppendTriangle(b, c, apex); mesh.AppendTriangle(c, a, apex);
            var T = new IntrinsicTriangulation(mesh);
            var S = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            int top = T.VertexIndex(apex), flips = 0;
            for (int round = 0; round < 2; ++round) {
                int h = Enumerable.Range(0, T.HalfedgeCount).Where(x => T.Tail(x) == top && T.Head(x) != top && T.FlipEdge(x)).DefaultIfEmpty(-1).First();
                if (h < 0) break;
                Assert.True(S.FlipEdge(h));
                flips++;
                var cs = T.ExtractCommonSubdivision();
                string defect = Defect(mesh, T, cs);
                Assert.True(defect == null, $"after {flips} flips: {defect}");
                var traced = S.ExtractCommonSubdivision();
                if (traced.Status == CommonSubdivisionStatus.Success) Assert.Null(Difference(cs, traced, 4, 6));
            }
            Assert.Equal(2, flips);
            Assert.Equal(1, Enumerable.Range(0, T.HalfedgeCount).Count(x => T.Tail(x) == top));
        }

        [Fact]
        public void IntrinsicAreasThatAreNotFinite_AreReportedAsInconsistent()
        {
            // The intrinsic area error is reported and not checked against a tolerance, but it must be a number.
            // Coordinates of 2^300: the lengths, their squares and the areas of the input triangles are finite,
            // the product of four lengths in the area of an intrinsic triangle is not.
            var big = new Similarity("scale 2^300", Math.Pow(2, 300), Matrix3d.Identity, Vector3d.Zero);
            DMesh3 mesh = ShearedGrid(3, 1, 0.375, big);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            Assert.True(double.IsInfinity(T.TriangleArea(0)));
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("intrinsic triangle areas are not finite", cs.Error);
            Assert.Empty(cs.Faces);
            Assert.True(cs.MaxInputAreaError <= 1e-6);
        }

        [Fact]
        public void Roundabouts_CountTheInputEdgesAroundEveryVertex()
        {
            // after flips and boundary splits: every roundabout is an index into the fan of its vertex, and the
            // halfedges on input edges have the roundabouts 0, 1, 2, ... of those edges
            DMesh3 mesh = Make("sheared grid a=1", Similarity.Identity);
            var T = Build(mesh, 2, true);
            Assert.True(T.VertexCount > T.InputVertexCount);
            int shared = 0, crossed = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                int v = T.Tail(h);
                if (v >= T.InputVertexCount) { Assert.Equal(0, T.roundabout(h)); continue; }
                int degree = mesh.GetVtxEdgeCount(T.VertexID(v));
                Assert.InRange(T.roundabout(h), 0, degree - 1);
                Assert.Equal(T.normal_coordinate(h), T.Twin(h) >= 0 ? T.normal_coordinate(T.Twin(h)) : -1);
                if (T.normal_coordinate(h) == -1 && T.Head(h) < T.InputVertexCount) {
                    Assert.True(mesh.FindEdge(T.VertexID(v), T.VertexID(T.Head(h))) >= 0);
                    shared++;
                }
                if (T.normal_coordinate(h) > 0) crossed++;
            }
            Assert.True(shared > 0 && crossed > 0);
        }

        // (kind, expected part of the error)
        [Theory]
        [InlineData(0, null)]                        // one crossing more on an edge
        [InlineData(6, "odd corner count")]         // counts on a triangle that do not add up to whole arcs
        [InlineData(1, null)]                        // two crossings more: the curves no longer close up
        [InlineData(2, null)]                        // a crossed edge marked as lying on an input edge
        [InlineData(3, "roundabout of halfedge")]   // a roundabout one too large
        [InlineData(4, "below -1")]
        [InlineData(5, null)]                        // an edge on an input edge marked as crossed twice
        public void InconsistentCoordinates_AreReported_WithoutFaces(int kind, string expected)
        {
            DMesh3 mesh = Make("sheared grid a=1", Similarity.Identity);
            var T = Build(mesh, 0, true);
            Assert.Equal(CommonSubdivisionStatus.Success, T.ExtractCommonSubdivision().Status);
            int crossedEdge = Enumerable.Range(0, T.HalfedgeCount).First(h => T.normal_coordinate(h) > 0);
            int sharedEdge = Enumerable.Range(0, T.HalfedgeCount).First(h => T.normal_coordinate(h) == -1 && T.Twin(h) >= 0);
            int n = T.normal_coordinate(crossedEdge), r = T.roundabout(crossedEdge);
            if (kind == 6) {
                // counts 2, 2, 3 on a triangle: no input edge starts at a corner, and its corner counts are halves
                int t = Enumerable.Range(0, T.TriangleCount).First(x => T.Twin(3 * x) >= 0 && T.Twin(3 * x + 1) >= 0 && T.Twin(3 * x + 2) >= 0);
                for (int k = 0; k < 3; ++k) T.set_integer_coordinates(3 * t + k, k == 2 ? 3 : 2, T.roundabout(3 * t + k));
            } else if (kind == 0) T.set_integer_coordinates(crossedEdge, n + 1, r);
            else if (kind == 1) T.set_integer_coordinates(crossedEdge, n + 2, r);
            else if (kind == 2) T.set_integer_coordinates(crossedEdge, -1, r);
            else if (kind == 3) {
                // (at an interior vertex, and still an index into its fan: only the count around the vertex shows it)
                int g = Enumerable.Range(0, T.HalfedgeCount).First(h => T.normal_coordinate(h) > 0 && T.IsBoundaryVertex(T.Tail(h)) == false);
                T.set_integer_coordinates(g, T.normal_coordinate(g), (T.roundabout(g) + 1) % mesh.GetVtxEdgeCount(T.VertexID(T.Tail(g))));
            }
            else if (kind == 4) T.set_integer_coordinates(crossedEdge, -2, r);
            else T.set_integer_coordinates(sharedEdge, 2, T.roundabout(sharedEdge));
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.False(string.IsNullOrEmpty(cs.Error));
            if (expected != null) Assert.Contains(expected, cs.Error);
            if (kind == 3) Assert.DoesNotContain("on an input edge", cs.Error);
            Assert.Empty(cs.Faces);
            output.WriteLine(cs.Error);
        }

        [Fact]
        public void InconsistentCoordinates_NeverGiveAWrongSuccess()
        {
            // the count of one edge changed by -2, 2 or 4 (the parity of the corner counts stays), on every fifth
            // crossed edge: the extraction ends, and reports it
            DMesh3 mesh = Make("sheared grid a=1", Similarity.Identity);
            int H = Build(mesh, 0, true).HalfedgeCount, tried = 0;
            foreach (int delta in new[] { -2, 2, 4 })
                for (int h = 0; h < H; h += 5) {
                    var T = Build(mesh, 0, true);
                    int n = T.normal_coordinate(h);
                    if (n <= 0 || T.Twin(h) < h || n + delta < 0) continue;
                    T.set_integer_coordinates(h, n + delta, T.roundabout(h));
                    var cs = T.ExtractCommonSubdivision();
                    Assert.True(cs.Status == CommonSubdivisionStatus.Inconsistent, $"halfedge {h}, {n} to {n + delta}: Success");
                    Assert.Empty(cs.Faces);
                    tried++;
                }
            Assert.True(tried >= 20, tried + " cases");
        }

        [Fact]
        public void ClampToOrder()
        {
            Assert.Equal(0.5, IntrinsicTriangulation.clamp_to_order(0.5, 0.25));
            Assert.Equal(0.25, IntrinsicTriangulation.clamp_to_order(0.125, 0.25));     // before the one before it
            Assert.Equal(0.25, IntrinsicTriangulation.clamp_to_order(double.NaN, 0.25));
            Assert.Equal(1.0, IntrinsicTriangulation.clamp_to_order(1.5, 0.25));          // beyond the end vertex
            Assert.Equal(1.0, IntrinsicTriangulation.clamp_to_order(double.PositiveInfinity, 0));
            Assert.Equal(0.0, IntrinsicTriangulation.clamp_to_order(double.NegativeInfinity, 0));
        }

        [Fact]
        public void Enable_OnlyBeforeTheFirstChange()
        {
            DMesh3 mesh = ShearedGrid(3, 1, 0.375, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            Assert.False(T.HasIntegerCoordinates);
            Assert.True(T.EnableIntegerCoordinates());
            Assert.True(T.EnableIntegerCoordinates());
            Assert.True(T.HasIntegerCoordinates);
            Assert.True(T.FlipToDelaunay() > 0);
            Assert.True(T.EnableIntegerCoordinates());      // still on

            var flipped = new IntrinsicTriangulation(mesh);
            int interior = Enumerable.Range(0, flipped.HalfedgeCount).First(h => flipped.Twin(h) >= 0 && flipped.FlipEdge(h));
            Assert.False(flipped.EnableIntegerCoordinates());
            // also when the flip is undone (the slots are then those of the input again, the history is not)
            Assert.True(flipped.FlipEdge(interior));
            Assert.False(flipped.EnableIntegerCoordinates());
            Assert.False(flipped.HasIntegerCoordinates);
            Assert.Equal(CommonSubdivisionStatus.Success, flipped.ExtractCommonSubdivision().Status);

            var split = new IntrinsicTriangulation(mesh);
            Assert.True(split.SplitEdge(Enumerable.Range(0, split.HalfedgeCount).First(h => split.Twin(h) < 0), 0.5) >= 0);
            Assert.False(split.EnableIntegerCoordinates());

            var interiorSplit = new IntrinsicTriangulation(mesh);
            Assert.True(interiorSplit.SplitEdge(Enumerable.Range(0, interiorSplit.HalfedgeCount).First(h => interiorSplit.Twin(h) >= 0), 0.5) >= 0);
            Assert.False(interiorSplit.EnableIntegerCoordinates());
            Assert.False(interiorSplit.HasIntegerCoordinates);

            var inserted = new IntrinsicTriangulation(mesh);
            inserted.GetTriangleLayout(0, out Vector2d q0, out Vector2d q1, out Vector2d q2);
            Assert.True(inserted.InsertVertex(0, (q0 + q1 + q2) / 3) >= 0);
            Assert.False(inserted.EnableIntegerCoordinates());
            Assert.False(inserted.HasIntegerCoordinates);
            Assert.Equal(CommonSubdivisionStatus.Success, inserted.ExtractCommonSubdivision().Status);
        }

        // the square of two triangles; diag: the halfedge of its diagonal
        static IntrinsicTriangulation Square(out DMesh3 mesh, out int diag)
        {
            mesh = ShearedGrid(1, 1, 0, Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            diag = Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) >= 0);
            return T;
        }

        [Fact]
        public void InputEdgeThatIsMissing_IsReportedAsInconsistent()
        {
            // the diagonal marked as an edge that no input edge crosses or lies on. Both its end vertices have no
            // next halfedge around them, so the roundabouts do not show it: only the count of the input edges does
            var T = Square(out DMesh3 mesh, out int diag);
            T.set_integer_coordinates(diag, 0, T.roundabout(diag));
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("0 of 1 interior input edges found", cs.Error);
            Assert.Empty(cs.Faces);
        }

        [Fact]
        public void FlipBeyondTheIntRange_SwitchesTheLayerOff_AndTheExtractionTraces()
        {
            // 2^30 arcs around each of the two far corners of the diagonal: the flipped diagonal would be crossed
            // 2^31 times. The flip is made, the integers are dropped, and the extraction traces
            var T = Square(out DMesh3 mesh, out int diag);
            foreach (int h in new[] { IntrinsicTriangulation.Next(diag), IntrinsicTriangulation.Prev(diag),
                    IntrinsicTriangulation.Next(T.Twin(diag)), IntrinsicTriangulation.Prev(T.Twin(diag)) })
                T.set_integer_coordinates(h, 1 << 30, T.roundabout(h));
            T.set_integer_coordinates(diag, 0, T.roundabout(diag));
            Assert.True(T.FlipEdge(diag));
            Assert.False(T.HasIntegerCoordinates);
            Assert.False(T.EnableIntegerCoordinates());
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.Equal(5, cs.Points.Count);
            Assert.Equal(4, cs.Faces.Count);
            // one step below the limit the layer stays
            var U = Square(out mesh, out diag);
            foreach (int h in new[] { IntrinsicTriangulation.Next(diag), IntrinsicTriangulation.Prev(diag) })
                U.set_integer_coordinates(h, 1 << 30, U.roundabout(h));
            foreach (int h in new[] { IntrinsicTriangulation.Next(U.Twin(diag)), IntrinsicTriangulation.Prev(U.Twin(diag)) })
                U.set_integer_coordinates(h, (1 << 30) - 1, U.roundabout(h));
            U.set_integer_coordinates(diag, 0, U.roundabout(diag));
            Assert.True(U.FlipEdge(diag));
            Assert.True(U.HasIntegerCoordinates);
            Assert.Equal(int.MaxValue, U.normal_coordinate(diag));
        }

        [Fact]
        public void TooManyCrossings_AreReported_NotAllocated()
        {
            // the diagonal crossed twice as often as the limit of crossings allows (the corner counts stay whole
            // numbers): a status, before anything of that size is allocated
            var T = Square(out DMesh3 mesh, out int diag);
            T.set_integer_coordinates(diag, 2 * IntrinsicTriangulation.MaxCrossings, T.roundabout(diag));
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Inconsistent, cs.Status);
            Assert.Contains("too many crossings", cs.Error);
            Assert.Empty(cs.Faces);
            Assert.Empty(cs.Points);
        }

        [Theory]
        [InlineData(0)]     // InsertVertex
        [InlineData(1)]     // SplitEdge of an interior edge
        [InlineData(2)]     // DelaunayRefine with circumcenters
        public void OtherInsertions_SwitchTheLayerOff_AndTheExtractionTraces(int kind)
        {
            DMesh3 mesh = kind == 2 ? RingSphere(10, 8, 0, 0.45, Similarity.Identity) : ShearedGrid(4, 1, 0.375, Similarity.Identity);
            Func<bool, IntrinsicTriangulation> build = (integers) => {
                var T = new IntrinsicTriangulation(mesh);
                if (integers) Assert.True(T.EnableIntegerCoordinates());
                T.FlipToDelaunay();
                if (kind == 0) {
                    T.GetTriangleLayout(5, out Vector2d q0, out Vector2d q1, out Vector2d q2);
                    Assert.True(T.InsertVertex(5, 0.5 * q0 + 0.25 * q1 + 0.25 * q2) >= 0);
                } else if (kind == 1)
                    Assert.True(T.SplitEdge(Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) >= 0), 0.375) >= 0);
                else
                    Assert.True(T.DelaunayRefine(25).CircumcenterInsertions > 0);
                T.FlipToDelaunay();
                return T;
            };
            var withLayer = build(true);
            Assert.False(withLayer.HasIntegerCoordinates);
            Assert.False(withLayer.EnableIntegerCoordinates());
            var a = withLayer.ExtractCommonSubdivision();
            var b = build(false).ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, a.Status);
            Assert.Equal(b.Points, a.Points);
            Assert.Equal(b.Faces.Count, a.Faces.Count);
            for (int f = 0; f < a.Faces.Count; ++f) {
                Assert.Equal(b.Faces[f].Corners, a.Faces[f].Corners);
                Assert.Equal(b.Faces[f].Barycentric, a.Faces[f].Barycentric);
            }
        }

        [Theory]
        [InlineData("sheared grid a=100", 1)]
        [InlineData("sheared cylinder s=7", 2)]
        [InlineData("clustered ring sphere j=0.49", 0)]
        public void Invariance_OverSimilarities(string name, int step)
        {
            DMesh3 mesh0 = Make(name, Similarity.Identity);
            var cs0 = Build(mesh0, step, true).ExtractCommonSubdivision();
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 mesh = Make(name, sim);
                var T = Build(mesh, step, true);
                var cs = T.ExtractCommonSubdivision();
                // every placement: the common subdivision of its triangulation (the flips can differ at ties)
                string defect = Defect(mesh, T, cs);
                Assert.True(defect == null, $"{sim}: {defect}");
                if (sim.IsExact == false) continue;
                // scaling by a power of two: the same result bit for bit
                Assert.Equal(cs0.Points.Count, cs.Points.Count);
                for (int i = 0; i < cs.Points.Count; ++i)
                    Assert.Equal(sim.Apply(cs0.Points[i]), cs.Points[i]);
                Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
                for (int f = 0; f < cs.Faces.Count; ++f) {
                    Assert.Equal(cs0.Faces[f].Corners, cs.Faces[f].Corners);
                    Assert.Equal(cs0.Faces[f].IntrinsicTriangle, cs.Faces[f].IntrinsicTriangle);
                    Assert.Equal(cs0.Faces[f].InputTriangle, cs.Faces[f].InputTriangle);
                    Assert.Equal(cs0.Faces[f].Barycentric, cs.Faces[f].Barycentric);
                    Assert.Equal(cs0.Faces[f].Area * sim.Scale * sim.Scale, cs.Faces[f].Area);
                }
                Assert.Equal(cs0.MaxIntrinsicAreaError, cs.MaxIntrinsicAreaError);
                Assert.Equal(cs0.MaxInputAreaError, cs.MaxInputAreaError);
            }
        }

        [Fact]
        public void Reuse_ExtractionsOnOneInstance()
        {
            // twice the same; then more changes on the same instance, and again the subdivision of that state
            DMesh3 mesh = Make("obtuse plane dy=0.05", Similarity.Identity);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.EnableIntegerCoordinates());
            var first = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, first));
            Assert.Equal(mesh.TriangleCount, first.Faces.Count);
            T.FlipToDelaunay();
            var a = T.ExtractCommonSubdivision();
            var b = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, a));
            Assert.Equal(a.Points, b.Points);
            Assert.Equal(a.Faces.Count, b.Faces.Count);
            for (int f = 0; f < a.Faces.Count; ++f) {
                Assert.Equal(a.Faces[f].Corners, b.Faces[f].Corners);
                Assert.Equal(a.Faces[f].Barycentric, b.Faces[f].Barycentric);
                Assert.Equal(a.Faces[f].Area, b.Faces[f].Area);
                Assert.Equal(a.Faces[f].InputTriangle, b.Faces[f].InputTriangle);
                Assert.Equal(a.Faces[f].IntrinsicTriangle, b.Faces[f].IntrinsicTriangle);
            }
            Assert.True(T.DelaunayRefine(0).BoundarySplits > 0);
            var c = T.ExtractCommonSubdivision();
            Assert.Null(Defect(mesh, T, c));
            Assert.True(c.Faces.Count > a.Faces.Count);
            Assert.Equal(mesh.TriangleCount, first.Faces.Count);
        }
    }
}
