using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The relations of the second metamorphic bug hunt that hold (docs/research/METAMORPHIC_BUG_HUNT_2_2026-11.md): one test per
    /// area, each on small procedural inputs, each checking the result with IndependentChecks (not only the status). The harness
    /// with the full sweep is benchmarks/MetamorphicBugHunt (stages io, loft, contour2, geodesic, intrinsic, quad, wrap2, mesh2d2,
    /// tet); these tests pin the relations a regression would break first.
    /// </summary>
    public class MetamorphicBugHunt2Tests
    {
        // ---------------------------------------------------------------- procedural inputs

        static DMesh3 Sphere(double r, int n, Vector3d c = default(Vector3d))
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Box(Vector3d half) => new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, half) }.Generate().MakeDMesh();

        static List<Vector2d> Ngon(int n, double r, double phase = 0) =>
            Enumerable.Range(0, n).Select(i => new Vector2d(r * Math.Cos(phase + 2 * Math.PI * i / n), r * Math.Sin(phase + 2 * Math.PI * i / n))).ToList();

        static List<Vector2d> Star(int points, double outer, double inner, double phase = 0) =>
            Enumerable.Range(0, 2 * points).Select(i => { double a = phase + Math.PI * i / points, r = i % 2 == 0 ? outer : inner; return new Vector2d(r * Math.Cos(a), r * Math.Sin(a)); }).ToList();

        /// <summary> a tube along z: outer and inner n-gon, z in [-h, h], outward oriented (a through hole of radius ri) </summary>
        static DMesh3 Tube(int n, double ro, double ri, double h)
        {
            var m = new DMesh3();
            for (int k = 0; k < 4; ++k) for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n, r = k % 2 == 0 ? ro : ri, z = k < 2 ? -h : h;
                m.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), z));
            }
            int O0 = 0, I0 = n, O1 = 2 * n, I1 = 3 * n;
            void Quad(int a, int b, int c, int d) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                Quad(O0 + i, O0 + j, O1 + j, O1 + i);
                Quad(I0 + j, I0 + i, I1 + i, I1 + j);
                Quad(O1 + i, O1 + j, I1 + j, I1 + i);
                Quad(O0 + j, O0 + i, I0 + i, I0 + j);
            }
            return m;
        }

        static DMesh3 Torus(double major, double minor, int rings, int sides)
        {
            var m = new DMesh3();
            for (int i = 0; i < rings; ++i) for (int j = 0; j < sides; ++j) {
                double u = 2 * Math.PI * i / rings, v = 2 * Math.PI * j / sides, r = major + minor * Math.Cos(v);
                m.AppendVertex(new Vector3d(r * Math.Cos(u), r * Math.Sin(u), minor * Math.Sin(v)));
            }
            for (int i = 0; i < rings; ++i) for (int j = 0; j < sides; ++j) {
                int i1 = (i + 1) % rings, j1 = (j + 1) % sides;
                int a = i * sides + j, b = i1 * sides + j, c = i1 * sides + j1, d = i * sides + j1;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            return m;
        }

        static DMesh3 Scaled(DMesh3 m, double s) { var c = new DMesh3(m); foreach (int v in c.VertexIndices()) c.SetVertex(v, c.GetVertex(v) * s); return c; }
        static DMesh3 Moved(DMesh3 m, Vector3d d) { var c = new DMesh3(m); foreach (int v in c.VertexIndices()) c.SetVertex(v, c.GetVertex(v) + d); return c; }
        static DMesh3 Flipped(DMesh3 m) { var c = new DMesh3(m); c.ReverseOrientation(); return c; }

        static DMesh3 Combined(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts) {
                var map = new Dictionary<int, int>();
                foreach (int v in p.VertexIndices()) map[v] = m.AppendVertex(p.GetVertex(v));
                foreach (int t in p.TriangleIndices()) { Index3i tv = p.GetTriangle(t); m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            }
            return m;
        }

        /// <summary> every triangle split into four at its edge midpoints: the same piecewise flat surface, the old vertex ids kept </summary>
        static DMesh3 Subdivided(DMesh3 mesh)
        {
            var m = new DMesh3();
            foreach (int v in mesh.VertexIndices()) Assert.Equal(v, m.AppendVertex(mesh.GetVertex(v)));
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b) { var k = a < b ? (a, b) : (b, a); if (!mid.TryGetValue(k, out int id)) mid[k] = id = m.AppendVertex(0.5 * (mesh.GetVertex(a) + mesh.GetVertex(b))); return id; }
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                int ab = Mid(tv.a, tv.b), bc = Mid(tv.b, tv.c), ca = Mid(tv.c, tv.a);
                m.AppendTriangle(tv.a, ab, ca); m.AppendTriangle(ab, tv.b, bc); m.AppendTriangle(ca, bc, tv.c); m.AppendTriangle(ab, bc, ca);
            }
            return m;
        }

        static void Holds(string problem, string what) => Assert.True(problem == "", what + ": " + problem);

        static void Close(double expected, double actual, double relative, string what) =>
            Assert.True(Math.Abs(expected - actual) <= relative * Math.Max(Math.Abs(expected), Math.Abs(actual)), $"{what}: {actual:R} vs {expected:R}");

        // ---------------------------------------------------------------- readers and writers

        static byte[] Write(string ext, DMesh3 mesh, bool binary)
        {
            var ms = new MemoryStream();
            var w = new StandardMeshWriter { OpenStreamF = name => ms, CloseStreamF = s => { } };
            WriteOptions o = WriteOptions.Defaults; o.bWriteBinary = binary;
            IOWriteResult r = w.Write("mesh." + ext, new List<WriteMesh> { new WriteMesh(mesh) }, o);
            Assert.True(r.code == IOCode.Ok, ext + " write: " + r.message);
            return ms.ToArray();
        }

        static DMesh3 Read(string ext, byte[] bytes)
        {
            var b = new DMesh3Builder();
            IOReadResult r = StandardMeshReader.ReadFile(new MemoryStream(bytes), ext, ReadOptions.Defaults, b);
            Assert.True(r.code == IOCode.Ok, ext + " read: " + r.message);
            Assert.Single(b.Meshes);
            return b.Meshes[0];
        }

        /// <summary>
        /// The formats that store doubles (or shortest round-trip decimals) give back the mesh bit for bit, for the input scaled by 2^k
        /// too; a second round trip writes the same bytes; a mesh with gaps in its ids writes the same file as its compact copy; a
        /// flipped mesh stays flipped.
        /// </summary>
        [Theory]
        [InlineData("obj", false)]
        [InlineData("ply", false)]
        [InlineData("ply", true)]
        [InlineData("g3mesh", false)]
        [InlineData("3mf", false)]
        public void ExactFormats_RoundTrip_BitForBit_Idempotent_GapsAndScale(string ext, bool binary)
        {
            DMesh3 mesh = Sphere(1, 5, new Vector3d(Math.PI, -Math.E, 1.0 / 3));
            foreach (double s in new[] { 1.0, 1.0 / 1024, 1024.0, Math.Pow(2, -100), Math.Pow(2, 100) }) {
                DMesh3 src = Scaled(mesh, s);
                byte[] bytes = Write(ext, src, binary);
                DMesh3 back = Read(ext, bytes);
                Holds(IndependentChecks.SameBits(src, back), $"{ext} round trip at scale {s}");
                Holds(IndependentChecks.ClosedManifold(back), $"{ext} round trip at scale {s}");
                Assert.True(bytes.SequenceEqual(Write(ext, back, binary)), $"{ext}: the second write differs at scale {s}");
            }
            DMesh3 gaps = IndependentChecks.WithIdGaps(mesh, out _);
            Assert.False(gaps.IsCompact);
            DMesh3 fromGaps = Read(ext, Write(ext, gaps, binary));
            // (g3mesh stores the DMesh3 with its ids, every other format the compact vertex and triangle lists)
            Holds(IndependentChecks.SameBits(ext == "g3mesh" ? gaps : Read(ext, Write(ext, mesh, binary)), fromGaps), ext + ": a mesh with id gaps reads back as its compact copy (g3mesh: with its ids)");
            Holds(IndependentChecks.ClosedManifold(fromGaps), ext + ": a mesh with id gaps");
            Assert.Equal(IndependentChecks.Volume(mesh), IndependentChecks.Volume(fromGaps));
            DMesh3 flipped = Read(ext, Write(ext, Flipped(mesh), binary));
            Close(-IndependentChecks.Volume(mesh), IndependentChecks.Volume(flipped), 1e-13, ext + ": the flipped mesh");
        }

        /// <summary> The float and fixed-decimal formats keep the solid (counts, closed, volume) and the positions within their precision. </summary>
        [Theory]
        [InlineData("stl", false)]
        [InlineData("stl", true)]
        [InlineData("off", false)]
        [InlineData("glb", false)]
        [InlineData("gltf", false)]
        public void LossyFormats_RoundTrip_KeepsTheSolid(string ext, bool binary)
        {
            DMesh3 mesh = Sphere(1, 5, new Vector3d(0.25, -0.5, 0.125));
            foreach (DMesh3 src in new[] { mesh, IndependentChecks.WithIdGaps(mesh, out _) }) {
                DMesh3 back = Read(ext, Write(ext, src, binary));
                Assert.Equal(mesh.TriangleCount, back.TriangleCount);
                Assert.Equal(mesh.VertexCount, back.VertexCount);       // (STL: the reader welds the soup again)
                Holds(IndependentChecks.ClosedManifold(back), ext + " round trip");
                Close(IndependentChecks.Volume(mesh), IndependentChecks.Volume(back), 1e-5, ext + " volume");
                DMesh3 again = Read(ext, Write(ext, back, binary));
                Close(IndependentChecks.Volume(back), IndependentChecks.Volume(again), 1e-5, ext + " second round trip");
                Holds(IndependentChecks.ClosedManifold(again), ext + " second round trip");
            }
        }

        // ---------------------------------------------------------------- ContourLoft

        static List<Vector3d> At(IEnumerable<Vector2d> poly, double z, double dx = 0, double dy = 0) => poly.Select(p => new Vector3d(p.x + dx, p.y + dy, z)).ToList();
        static List<Vector3d> Rotated(List<Vector3d> c, int k) => Enumerable.Range(0, c.Count).Select(i => c[(i + k) % c.Count]).ToList();
        static List<Vector3d> Reversed(List<Vector3d> c) { var r = new List<Vector3d>(c); r.Reverse(); return r; }

        /// <summary>
        /// The optimal band does not depend on how the contours are given: another start vertex, the contours swapped, a contour
        /// reversed and a mirror give the same cost and, with caps, a solid of the same volume; a second Compute() gives the same mesh.
        /// </summary>
        [Theory]
        [InlineData(ContourLoftCost.MinimumArea, false)]
        [InlineData(ContourLoftCost.MinimumArea, true)]
        [InlineData(ContourLoftCost.MaximumVolume, false)]
        public void ContourLoft_SameBand_HoweverTheContoursAreGiven(ContourLoftCost cost, bool normalize)
        {
            List<Vector3d> a = At(Star(5, 1.0, 0.55, 0.2), 0), b = At(Ngon(16, 0.8, 0.1), 1.0);
            var loft = new ContourLoft { Cost = cost, NormalizeForCost = normalize, Caps = true };
            ContourLoftResult r = loft.Compute(a, b);
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            Holds(IndependentChecks.ClosedManifold(r.Mesh), "loft");
            ResultChecks.AssertClean(r.Mesh, "loft");
            double volume = IndependentChecks.Volume(r.Mesh);
            Assert.True(volume > 0, "the loft faces outward");
            for (int i = 0; i < a.Count; ++i) Assert.Equal(a[i], r.Mesh.GetVertex(i));
            for (int j = 0; j < b.Count; ++j) Assert.Equal(b[j], r.Mesh.GetVertex(a.Count + j));
            Holds(IndependentChecks.SameBits(r.Mesh, loft.Compute(a, b).Mesh), "a second Compute()");

            var variants = new (string, List<Vector3d>, List<Vector3d>)[] {
                ("start vertex of the first", Rotated(a, 3), b), ("start vertex of the second", a, Rotated(b, 5)), ("swapped", b, a),
                ("first reversed", Reversed(a), b), ("second reversed", a, Reversed(b)), ("both reversed", Reversed(a), Reversed(b)),
                ("mirrored", a.Select(p => new Vector3d(-p.x, p.y, p.z)).ToList(), b.Select(p => new Vector3d(-p.x, p.y, p.z)).ToList()),
            };
            foreach (var (name, x, y) in variants) {
                ContourLoftResult v = new ContourLoft { Cost = cost, NormalizeForCost = normalize, Caps = true }.Compute(x, y);
                Assert.True(v.Status == ContourLoftStatus.Success, name + ": " + v.Status + " " + v.Reason);
                Close(r.TotalCost, v.TotalCost, 1e-12, name + ": cost");
                Holds(IndependentChecks.ClosedManifold(v.Mesh), name);
                Close(volume, IndependentChecks.Volume(v.Mesh), 1e-9, name + ": volume");
            }
        }

        /// <summary> Similarity.All(): the status and the solid are kept, the volume scales, and 2^k gives the same mesh bit for bit. </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ContourLoft_AndStack_Invariance(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            List<Vector3d> a = At(Ngon(24, 2, 0.1), 0), b1 = At(Ngon(12, 0.7, 0.1), 1, -1), b2 = At(Ngon(12, 0.7, 0.2), 1, 1);
            IList<IList<IList<Vector3d>>> Slices(Func<Vector3d, Vector3d> f) => new List<IList<IList<Vector3d>>> {
                new List<IList<Vector3d>> { a.Select(f).ToList() }, new List<IList<Vector3d>> { b1.Select(f).ToList(), b2.Select(f).ToList() } };
            ContourLoftStackResult r0 = new ContourLoftStack { CapEnds = true }.Compute(Slices(p => p));
            ContourLoftStackResult r = new ContourLoftStack { CapEnds = true }.Compute(Slices(s.Apply));
            Assert.Equal(ContourLoftStackStatus.Success, r0.Status);
            Assert.True(r.Status == ContourLoftStackStatus.Success, r.Status + " " + r.Reason);
            Holds(IndependentChecks.ClosedManifold(r.Mesh), "stack");
            ResultChecks.AssertClean(r.Mesh, "stack");
            Assert.Equal(2, IndependentChecks.Euler(r.Mesh));
            double scale3 = s.Scale * s.Scale * s.Scale;
            Close(IndependentChecks.Volume(r0.Mesh) * scale3, IndependentChecks.Volume(r.Mesh), 1e-6, "stack volume");
            if (s.IsExact) Holds(IndependentChecks.SameBits(r0.Mesh, r.Mesh, s.Scale), "the scaled stack");

            ContourLoftResult l0 = new ContourLoft { Caps = true }.Compute(a, b1), l = new ContourLoft { Caps = true }.Compute(a.Select(s.Apply).ToList(), b1.Select(s.Apply).ToList());
            Assert.Equal(l0.Status, l.Status);
            if (l0.Status == ContourLoftStatus.Success) {
                ResultChecks.AssertClean(l.Mesh, "loft");
                Close(IndependentChecks.Volume(l0.Mesh) * scale3, IndependentChecks.Volume(l.Mesh), 1e-6, "loft volume");
                if (s.IsExact) Holds(IndependentChecks.SameBits(l0.Mesh, l.Mesh, s.Scale), "the scaled loft");
            }
        }

        /// <summary>
        /// A stack is the same solid when the contours of a slice are listed in another order or direction, when it is given upside
        /// down, and next to a far copy of itself (twice the volume and the triangles); a second Compute() gives the same mesh.
        /// </summary>
        [Fact]
        public void ContourLoftStack_SameSolid_ForEquivalentInputs_AndAFarCopy()
        {
            var slices = new[] {
                new[] { At(Ngon(16, 2, 0.1), 0), At(Ngon(10, 1, 0.3), 0) },
                new[] { At(Ngon(16, 2.1, 0.2), 1), At(Ngon(9, 0.9, 0.1), 1) },
                new[] { At(Ngon(18, 1.9, 0.0), 2), At(Ngon(11, 1.0, 0.2), 2) } };
            IList<IList<IList<Vector3d>>> L(IEnumerable<IEnumerable<List<Vector3d>>> s) => s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c).ToList()).ToList();
            var stack = new ContourLoftStack { CapEnds = true };
            ContourLoftStackResult r = stack.Compute(L(slices));
            Assert.Equal(ContourLoftStackStatus.Success, r.Status);
            ResultChecks.AssertClean(r.Mesh, "tube");
            Holds(IndependentChecks.ClosedManifold(r.Mesh), "tube");
            Assert.Equal(0, IndependentChecks.Euler(r.Mesh));       // a solid with a tunnel
            double volume = IndependentChecks.Volume(r.Mesh);
            Assert.True(volume > 0);
            Holds(IndependentChecks.SameBits(r.Mesh, stack.Compute(L(slices)).Mesh), "a second Compute()");

            var variants = new (string, IEnumerable<IEnumerable<List<Vector3d>>>, double)[] {
                ("contours in reverse order", slices.Select(s => s.Reverse()), 1),
                ("other start vertices", slices.Select(s => s.Select((c, i) => Rotated(c, 1 + i))), 1),
                ("other directions", slices.Select(s => s.Select(Reversed)), 1),
                ("upside down", slices.Reverse().Select(s => s.Select(c => c.Select(p => new Vector3d(p.x, p.y, -p.z)).ToList())), 1),
                ("a far copy", slices.Select(s => s.Concat(s.Select(c => c.Select(p => p + new Vector3d(0, 64, 0)).ToList()))), 2),
            };
            foreach (var (name, v, factor) in variants) {
                ContourLoftStackResult q = new ContourLoftStack { CapEnds = true }.Compute(L(v));
                Assert.True(q.Status == ContourLoftStackStatus.Success, name + ": " + q.Status + " " + q.Reason);
                Holds(IndependentChecks.ClosedManifold(q.Mesh), name);
                ResultChecks.AssertClean(q.Mesh, name);
                Close(factor * volume, IndependentChecks.Volume(q.Mesh), 1e-9, name + ": volume");
                Assert.Equal((int)factor * r.Mesh.TriangleCount, q.Mesh.TriangleCount);
            }
        }

        // ---------------------------------------------------------------- contouring

        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        static double TorusField(Vector3d p) => Math.Sqrt(Math.Pow(Math.Sqrt(p.x * p.x + p.y * p.y) - 0.9, 2) + p.z * p.z) - 0.35;

        static DMesh3 Contour(bool dual, Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, bool parallel = true, object reuse = null)
        {
            if (dual) {
                var dc = (DualContouring)reuse ?? new DualContouring();
                dc.Implicit = new Field { F = f, Box = box }; dc.Bounds = box; dc.CellSize = cell; dc.InsideIsLess = true; dc.ParallelCompute = parallel;
                dc.Generate();
                return new DMesh3(dc.Mesh);
            }
            var mc = (MarchingCubes)reuse ?? new MarchingCubes();
            mc.Implicit = new Field { F = f, Box = box }; mc.Bounds = box; mc.CubeSize = cell; mc.ParallelCompute = parallel;
            mc.Generate();
            return new DMesh3(mc.Mesh);
        }

        /// <summary>
        /// MarchingCubes and DualContouring: closed and manifold; serial, a repeated run and a second Generate() on the instance give
        /// the same ids; 2^k gives the scaled mesh bit for bit; a far copy of the field in the same grid doubles triangles and volume.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Contouring_Determinism_Reuse_Scale_FarCopy(bool dual)
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
            const double cell = 0.125;      // dyadic: the grid nodes are exact
            DMesh3 m = Contour(dual, TorusField, box, cell);
            Holds(IndependentChecks.ClosedManifold(m), "contour");
            Assert.Equal(0, IndependentChecks.Euler(m));
            double volume = IndependentChecks.Volume(m);
            Close(2 * Math.PI * Math.PI * 0.9 * 0.35 * 0.35, volume, 0.05, "the torus volume");
            Holds(IndependentChecks.SameBits(m, Contour(dual, TorusField, box, cell, parallel: false)), "serial = parallel");
            object instance = dual ? (object)new DualContouring() : new MarchingCubes();
            Holds(IndependentChecks.SameBits(m, Contour(dual, TorusField, box, cell, reuse: instance)), "the first Generate() of an instance");
            Holds(IndependentChecks.SameBits(m, Contour(dual, TorusField, box, cell, reuse: instance)), "the second Generate() of the instance");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 })
                Holds(IndependentChecks.SameBits(m, Contour(dual, p => s * TorusField(p / s), new AxisAlignedBox3d(box.Min * s, box.Max * s), cell * s), s), "scale " + s);
            var wide = new AxisAlignedBox3d(box.Min, box.Max + new Vector3d(4, 0, 0));      // 32 cells
            DMesh3 two = Contour(dual, p => Math.Min(TorusField(p), TorusField(p - new Vector3d(4, 0, 0))), wide, cell);
            Holds(IndependentChecks.ClosedManifold(two), "two tori");
            Assert.Equal(2 * m.TriangleCount, two.TriangleCount);
            Close(2 * volume, IndependentChecks.Volume(two), 1e-9, "two tori");
        }

        /// <summary>
        /// TpmsLattice: Ok is a solid inside the box; the frame (Origin, Rotation) moves the mesh rigidly; 2^k is bit for bit; serial
        /// and a second Compute() give the same ids; the inside-out domain mesh is refused without a mesh.
        /// </summary>
        [Theory]
        [InlineData(TpmsType.Gyroid, TpmsStyle.Sheet)]
        [InlineData(TpmsType.SchwarzP, TpmsStyle.SkeletalLow)]
        [InlineData(TpmsType.IWP, TpmsStyle.SkeletalHigh)]
        public void TpmsLattice_Solid_FrameEquivariance_Scale_Reuse(TpmsType type, TpmsStyle style)
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1, -0.75, -0.5), new Vector3d(1, 0.75, 1));
            TpmsLattice Make(double s) => new TpmsLattice { Type = type, Style = style, CellSize = s, Divisions = 8, Box = new AxisAlignedBox3d(box.Min * s, box.Max * s) };
            TpmsLattice l = Make(1);
            l.Compute();
            Assert.True(l.Status == TpmsStatus.Ok, l.Status + " " + l.Message);
            DMesh3 m = new DMesh3(l.Mesh);
            Holds(IndependentChecks.ClosedManifold(m), "lattice");
            ResultChecks.AssertClean(m, "lattice");
            double volume = IndependentChecks.Volume(m);
            Close(volume / box.Volume, l.RealizedVolumeFraction, 1e-9, "RealizedVolumeFraction");
            AxisAlignedBox3d b = m.GetBounds();
            Assert.True(box.Contains(b.Min) && box.Contains(b.Max), "the lattice leaves its box");
            l.Compute();
            Holds(IndependentChecks.SameBits(m, l.Mesh), "a second Compute()");
            TpmsLattice serial = Make(1); serial.ParallelCompute = false; serial.Compute();
            Holds(IndependentChecks.SameBits(m, serial.Mesh), "serial");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                TpmsLattice q = Make(s); q.Compute();
                Assert.Equal(TpmsStatus.Ok, q.Status);
                Holds(IndependentChecks.SameBits(m, q.Mesh, s), "scale " + s);
            }
            foreach (Similarity s in Similarity.All().Where(x => x.Scale == 1)) {
                TpmsLattice q = Make(1); q.Rotation = s.Rotation; q.Origin = s.Translation; q.Compute();
                Assert.True(q.Status == TpmsStatus.Ok, s.Name + ": " + q.Status);
                Assert.Equal(m.TriangleCount, q.Mesh.TriangleCount);
                double worst = m.VertexIndices().Max(v => (q.Mesh.GetVertex(v) - s.Apply(m.GetVertex(v))).Length);
                Assert.True(worst <= 64 * s.Resolution(2), $"{s.Name}: a vertex {worst:R} from the moved lattice");
                Holds(IndependentChecks.ClosedManifold(q.Mesh), s.Name);
            }
            TpmsLattice insideOut = Make(1); insideOut.DomainMesh = Flipped(Moved(Box(box.Extents), box.Center)); insideOut.Compute();
            Assert.Equal(TpmsStatus.InvalidDomain, insideOut.Status);
            Assert.Null(insideOut.Mesh);
        }

        // ---------------------------------------------------------------- geodesics

        static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                double x = i / (double)(n - 1), y = j / (double)(n - 1);
                m.AppendVertex(new Vector3d(x, y, 0.15 * (Math.Sin(5 * x) * Math.Cos(4 * y) + 0.3 * Math.Sin(13 * x * y))));
            }
            for (int j = 0; j + 1 < n; ++j) for (int i = 0; i + 1 < n; ++i) {
                int a = j * n + i, b = a + 1, c = a + n + 1, d = a + n;
                if (((i + j) & 1) == 1) { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
            }
            return m;
        }

        static double[] Polyhedral(DMesh3 m, params int[] sources)
        {
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(sources));
            return (double[])g.Distance.Clone();
        }

        public static IEnumerable<object[]> GeodesicMeshes() => new[] { new object[] { "torus" }, new object[] { "terrain" }, new object[] { "box" } };

        static DMesh3 GeodesicMesh(string name) =>
            name == "torus" ? Torus(1, 0.35, 16, 10) : name == "terrain" ? Terrain(12) : new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), EdgeVertices = 5 }.Generate().MakeDMesh();

        /// <summary>
        /// The polyhedral distance is a metric of the surface: symmetric, with the triangle inequality, not below the straight
        /// distance, the minimum over the sources; and it does not depend on the triangulation of the surface (every triangle split
        /// in its plane), on the orientation, or on a far second component (which stays unreached).
        /// </summary>
        [Theory]
        [MemberData(nameof(GeodesicMeshes))]
        public void PolyhedralGeodesicDistance_IsAMetricOfTheSurface(string name)
        {
            DMesh3 mesh = GeodesicMesh(name);
            int n = mesh.MaxVertexID, a = 1, b = n / 3, c = n - 2;
            double extent = mesh.GetBounds().DiagonalLength;
            double[] da = Polyhedral(mesh, a), db = Polyhedral(mesh, b);
            Assert.Equal(0, da[a]);
            Close(da[b], db[a], 1e-12, "symmetry");
            for (int v = 0; v < n; ++v) {
                Assert.True(da[v] >= (mesh.GetVertex(v) - mesh.GetVertex(a)).Length * (1 - 1e-12), $"vertex {v} is closer than the straight distance");
                Assert.True(da[v] <= da[b] + db[v] + 1e-12 * extent, $"the triangle inequality fails at vertex {v}");
            }
            double[] both = Polyhedral(mesh, a, b);
            for (int v = 0; v < n; ++v) Close(Math.Min(da[v], db[v]), both[v], 1e-12, "two sources at vertex " + v);

            double[] subdivided = Polyhedral(Subdivided(mesh), a), flipped = Polyhedral(Flipped(mesh), a);
            double[] two = Polyhedral(Combined(mesh, Moved(mesh, new Vector3d(100, 0, 0))), a);
            for (int v = 0; v < n; ++v) {
                Assert.True(Math.Abs(subdivided[v] - da[v]) <= 1e-11 * da[v] + 1e-13 * extent, $"subdivided, vertex {v}: {subdivided[v]:R} vs {da[v]:R}");
                Assert.True(Math.Abs(flipped[v] - da[v]) <= 1e-12 * da[v] + 1e-14 * extent, $"flipped, vertex {v}: {flipped[v]:R} vs {da[v]:R}");
                Assert.Equal(da[v], two[v]);
                Assert.True(double.IsNaN(two[n + v]) || double.IsInfinity(two[n + v]), $"vertex {n + v} of the part without a source has the distance {two[n + v]:R}");
            }
            Assert.True(da[c] > 0);
        }

        /// <summary>
        /// Similarity.All(): the distances scale (2^k: bit for bit). A traced path has the length of the distance; the FlipOut path
        /// is not shorter than the polyhedral distance and not longer than its initial edge path; the heat distance of a far part
        /// without a source is NaN.
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Geodesics_Invariance_Paths_AndBounds(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            DMesh3 mesh = Torus(1, 0.35, 16, 10), moved = Invariance.Transformed(mesh, s);
            int n = mesh.MaxVertexID, a = 1, t = n / 2 + 3;
            double[] d0 = Polyhedral(mesh, a);
            var g = new PolyhedralGeodesicDistance(moved);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { a }));
            // (the transformed coordinates are rounded: the mesh itself moves by Resolution)
            double tolerance = s.IsExact ? 0 : 8 * s.Resolution(3) * s.Scale / (0.1 * s.Scale) + 1e-12;
            for (int v = 0; v < n; ++v)
                Assert.True(Math.Abs(g.Distance[v] - d0[v] * s.Scale) <= tolerance * d0[v] * s.Scale, $"vertex {v}: {g.Distance[v]:R} vs {d0[v] * s.Scale:R}");

            PolyhedralGeodesicPath path = g.TracePath(t);
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, path.Status);
            double polyline = 0;
            for (int i = 1; i < path.Points.Count; ++i) polyline += (path.Points[i].Position - path.Points[i - 1].Position).Length;
            // (the path's points are world positions: far from the origin they carry the rounding of the coordinates)
            double positions = 64 * s.Resolution(3) / 0.1;
            Close(g.Distance[t], polyline, 1e-9 + positions, "the traced polyline");
            Close(g.Distance[t], path.Length, 1e-12 + positions, "PolyhedralGeodesicPath.Length");

            var flipOut = new MeshGeodesicPath(moved);
            Assert.Equal(GeodesicPathStatus.Success, flipOut.Compute(a, t));
            Assert.True(flipOut.Length >= g.Distance[t] * (1 - 1e-9), $"FlipOut {flipOut.Length:R} is below the polyhedral distance {g.Distance[t]:R}");
            Assert.True(flipOut.Length <= flipOut.InitialLength * (1 + 1e-12), "FlipOut is longer than its initial path");
            double traced = 0;
            for (int i = 1; i < flipOut.Path.Count; ++i) traced += (flipOut.Path[i] - flipOut.Path[i - 1]).Length;
            Close(flipOut.Length, traced, 1e-6 + positions, "the FlipOut polyline");

            var heat = new MeshGeodesicDistance(Combined(moved, Moved(moved, s.ApplyLength(100) * Vector3d.AxisX)));
            Assert.True(heat.Compute(new[] { a }));
            Assert.Equal(0, heat.Distance[a]);
            for (int v = 0; v < n; ++v) {
                Assert.True(heat.Distance[v] >= 0, "heat distance of vertex " + v);
                Assert.True(double.IsNaN(heat.Distance[n + v]), "heat: a vertex of the part without a source has a distance");
            }
        }

        // ---------------------------------------------------------------- intrinsic triangulation

        static double IntrinsicArea(IntrinsicTriangulation T) => Enumerable.Range(0, T.TriangleCount).Sum(t => T.TriangleArea(t));

        static int IntrinsicEuler(IntrinsicTriangulation T)
        {
            int interior = 0, boundary = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h) if (T.Twin(h) >= 0) ++interior; else ++boundary;
            return T.VertexCount - (interior / 2 + boundary) + T.TriangleCount;
        }

        static double[] SortedLengths(IntrinsicTriangulation T) => Enumerable.Range(0, T.HalfedgeCount).Where(h => T.Twin(h) < h).Select(h => T.Length(h)).OrderBy(x => x).ToArray();

        /// <summary>
        /// Flips and insertions do not change the surface: the area, the Euler characteristic and the angle sums of the input
        /// vertices are kept; the flipped triangulation is Delaunay and the same for a permuted or flipped input (the sorted edge
        /// lengths); a successful refinement meets its bound; the common subdivision is a closed mesh of the same area and volume.
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(25.0)]
        public void IntrinsicTriangulation_KeepsTheSurface(double refineAngle)
        {
            var rng = new Random(7);
            DMesh3 mesh = Torus(1, 0.35, 14, 9);
            foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, mesh.GetVertex(v) + 0.02 * new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5));
            double area = IndependentChecks.Area(mesh);
            var sums = new double[mesh.MaxVertexID];
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) sums[tv[k]] += Vector3d.AngleR((mesh.GetVertex(tv[(k + 1) % 3]) - mesh.GetVertex(tv[k])).Normalized, (mesh.GetVertex(tv[(k + 2) % 3]) - mesh.GetVertex(tv[k])).Normalized);
            }
            void Invariants(IntrinsicTriangulation T, string what)
            {
                Assert.True(T.CheckConsistency(out string error), what + ": " + error);
                Close(area, IntrinsicArea(T), 1e-9, what + ": area");
                Assert.Equal(0, IntrinsicEuler(T));
                for (int i = 0; i < T.InputVertexCount; ++i) {
                    double sum = Enumerable.Range(0, T.HalfedgeCount).Where(h => T.Tail(h) == i).Sum(h => T.CornerAngle(h));
                    Assert.True(Math.Abs(sum - sums[T.VertexID(i)]) < 1e-8, $"{what}: the angle sum of vertex {T.VertexID(i)} changed");
                }
            }
            var flippedOnly = new IntrinsicTriangulation(mesh);
            int flips = flippedOnly.FlipToDelaunay();
            Assert.True(flips > 0, "the jittered torus needs flips");
            Assert.True(flippedOnly.IsDelaunay());
            Assert.Equal(0, flippedOnly.FlipToDelaunay());
            Invariants(flippedOnly, "flipped");
            for (int h = 0; h < flippedOnly.HalfedgeCount; ++h) Assert.True(flippedOnly.CotanWeight(h) >= -1e-9, "a negative cotan weight after FlipToDelaunay");
            double[] lengths = SortedLengths(flippedOnly);
            foreach (DMesh3 variant in new[] { Invariance.Permuted(mesh, 11), Flipped(mesh) }) {
                var Tv = new IntrinsicTriangulation(variant); Tv.FlipToDelaunay();
                double[] lv = SortedLengths(Tv);
                Assert.Equal(lengths.Length, lv.Length);
                for (int i = 0; i < lengths.Length; ++i) Close(lengths[i], lv[i], 1e-9, "the Delaunay edge lengths of an equivalent input");
            }

            var T = new IntrinsicTriangulation(mesh);
            IntrinsicRefinementResult res = T.DelaunayRefine(refineAngle);
            Assert.Equal(IntrinsicRefinementStatus.Success, res.Status);
            Invariants(T, "refined");
            Assert.True(T.IsDelaunay(1e-8));
            Assert.Equal(res.InsertedVertices, T.VertexCount - T.InputVertexCount);
            if (res.SkippedTriangles == 0) Assert.True(T.MinAngle() * MathUtil.Rad2Deg >= refineAngle - 1e-6, "Success below the angle bound");
            Assert.Equal(0, T.DelaunayRefine(refineAngle).InsertedVertices);
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i)
                Assert.True(IndependentChecks.Distance(mesh, T.VertexPosition(i)) <= 1e-7, "an inserted vertex is off the surface");

            IntrinsicCommonSubdivision cs = T.ExtractCommonSubdivision();
            Assert.True(cs.Status == CommonSubdivisionStatus.Success, cs.Error);
            Close(area, cs.Faces.Sum(f => f.Area), 1e-9, "the common subdivision's area");
            DMesh3 cm = cs.ToMesh();
            Holds(IndependentChecks.ClosedManifold(cm), "the common subdivision");
            Close(IndependentChecks.Volume(mesh), IndependentChecks.Volume(cm), 1e-9, "the common subdivision's volume");
        }

        // ---------------------------------------------------------------- quad remesher

        static string SamePoly(DPolyMesh a, DPolyMesh b, double scale = 1)
        {
            if (a.VertexCount != b.VertexCount || a.FaceCount != b.FaceCount) return $"{a.VertexCount} vertices / {a.FaceCount} faces vs {b.VertexCount} / {b.FaceCount}";
            for (int v = 0; v < a.VertexCount; ++v) { Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v); if (p.x != q.x || p.y != q.y || p.z != q.z) return "vertex " + v; }
            for (int f = 0; f < a.FaceCount; ++f) if (!a.GetFace(f).SequenceEqual(b.GetFace(f))) return "face " + f;
            return "";
        }

        static string PolyManifold(DPolyMesh m, out bool closed)
        {
            closed = false;
            var dir = new HashSet<(int, int)>();
            for (int f = 0; f < m.FaceCount; ++f) {
                int[] face = m.GetFace(f);
                if (face.Length < 3) return $"face {f} has {face.Length} vertices";
                for (int i = 0; i < face.Length; ++i) {
                    if (!m.GetVertex(face[i]).IsFinite) return "non-finite vertex " + face[i];
                    if (!dir.Add((face[i], face[(i + 1) % face.Length]))) return $"directed edge ({face[i]}, {face[(i + 1) % face.Length]}) in two faces";
                }
            }
            closed = dir.All(e => dir.Contains((e.Item2, e.Item1)));
            return "";
        }

        /// <summary>
        /// QuadRemesher with its options: a second Compute(), the serial run and 2^k give the same polygon mesh; the result is an
        /// oriented manifold polygon mesh near the input; a successful pure-quad attempt is a closed mesh of quads only with the
        /// input's Euler characteristic, a failed one leaves the stage 2 mesh (identical to PureQuads = false).
        /// </summary>
        [Theory]
        [InlineData("sphere", false, SizeAdaptationMode.Off)]
        [InlineData("sphere", true, SizeAdaptationMode.Off)]
        [InlineData("torus", true, SizeAdaptationMode.Off)]
        [InlineData("torus", false, SizeAdaptationMode.Scale)]
        [InlineData("box", true, SizeAdaptationMode.ScaleAndRotation)]
        public void QuadRemesher_Options_Deterministic_ScaleExact_PureQuadContract(string shape, bool pure, SizeAdaptationMode size)
        {
            DMesh3 mesh = shape == "sphere" ? Sphere(1, 12) : shape == "torus" ? Torus(1, 0.4, 32, 16)
                : new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.75, 0.5)), EdgeVertices = 9 }.Generate().MakeDMesh();
            double edge = shape == "box" ? 0.25 : 0.2;
            QuadRemesher Make(DMesh3 m, double s, bool parallel = true, bool pureQuads = true) =>
                new QuadRemesher(m) { TargetEdgeLength = edge * s, PureQuads = pure && pureQuads, SizeAdaptation = size, Parallel = parallel };
            var input = new DMesh3(mesh);
            QuadRemesher q = Make(input, 1);
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Holds(IndependentChecks.SameBits(mesh, input), "the input is not modified");
            DPolyMesh r = q.Result;
            Holds(PolyManifold(r, out bool closed), "the polygon mesh");
            DMesh3 tri = r.ToDMesh3(out int[] _);
            foreach (int v in tri.VertexIndices()) Assert.True(IndependentChecks.Distance(mesh, tri.GetVertex(v)) <= edge, "a vertex farther than the target edge from the input");
            if (!pure) Assert.Equal(PureQuadStatus.NotRequested, q.PureQuadStatus);
            else if (q.PureQuadStatus == PureQuadStatus.Success) {
                Assert.True(Enumerable.Range(0, r.FaceCount).All(f => r.FaceVertexCount(f) == 4), "a pure-quad Success with a face that is not a quad");
                Assert.True(closed, "a pure-quad Success that is not closed");
                Assert.Equal(IndependentChecks.Euler(mesh), IndependentChecks.Euler(tri));
                Close(IndependentChecks.Volume(mesh), IndependentChecks.Volume(tri), 0.1, "the volume of the quad mesh");
                Assert.Equal(0, q.InvertedTriangles + q.FlippedQuads);
            } else {
                QuadRemesher stage2 = Make(new DMesh3(mesh), 1, pureQuads: false);
                stage2.Compute();
                Holds(SamePoly(stage2.Result, r), "a failed pure-quad attempt leaves the stage 2 mesh");
            }
            q.Compute();
            Holds(SamePoly(r, q.Result), "a second Compute()");
            QuadRemesher serial = Make(new DMesh3(mesh), 1, parallel: false);
            serial.Compute();
            Holds(SamePoly(r, serial.Result), "serial");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                QuadRemesher qs = Make(Scaled(mesh, s), s);
                Assert.Equal(QuadRemeshStatus.Success, qs.Compute());
                Assert.Equal(q.PureQuadStatus, qs.PureQuadStatus);
                Holds(SamePoly(r, qs.Result, s), "scale " + s);
            }
        }

        // ---------------------------------------------------------------- the wrap family

        static string PinsClear(DMesh3 wrap, IEnumerable<PassagePin> pins)
        {
            foreach (PassagePin pin in pins)
                for (int i = 0; i + 1 < pin.Axis.Count; ++i)
                    for (int k = 0; k <= 40; ++k) {
                        Vector3d q = pin.Axis[i] + (pin.Axis[i + 1] - pin.Axis[i]) * (k / 40.0);
                        if (Math.Abs(IndependentChecks.Winding(wrap, q)) > 0.5) return "an axis point is inside the wrap";
                        double d = IndependentChecks.Distance(wrap, q);
                        if (d < pin.Radius * (1 - 1e-9)) return $"an axis point is {d:R} from the wrap, the radius is {pin.Radius:R}";
                    }
            return "";
        }

        /// <summary>
        /// PassageWrap: a Success is a solid around the input whose pins are clear by an independent check (winding number and
        /// sampled distance); the same wrap for reversed axes, a pin given twice, the pins in another order, one thread and a second
        /// Compute(); Similarity.All(): Success again with clear pins, 2^k bit for bit; a pin the input blocks is never passed.
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PassageWrap_PinsClear_Independently_AndInvariant(string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            DMesh3 two = Combined(Tube(12, 1.0, 0.4, 0.5), Moved(Tube(12, 0.8, 0.3, 0.4), new Vector3d(3, 0, 0)));
            PassagePin[] pins = {
                new PassagePin(new[] { new Vector3d(0, 0, -0.9), new Vector3d(0, 0, 0.9) }, 0.2),
                new PassagePin(new[] { new Vector3d(3, 0, -0.8), new Vector3d(3, 0, 0.8) }, 0.15) };
            PassageWrap Run(DMesh3 m, IEnumerable<PassagePin> p, int threads = 0)
            {
                var w = new PassageWrap(m) { MaxThreads = threads };
                w.Pins.AddRange(p);
                w.Compute();
                return w;
            }
            PassageWrap reference = Run(new DMesh3(two), pins);
            Assert.Equal(PassageWrap.PassageStatus.Success, reference.Status);
            DMesh3 first = new DMesh3(reference.Result);

            DMesh3 moved = Invariance.Transformed(two, s);
            PassagePin[] movedPins = pins.Select(p => new PassagePin(p.Axis.Select(s.Apply), s.ApplyLength(p.Radius))).ToArray();
            PassageWrap w = Run(moved, movedPins);
            Assert.True(w.Status == PassageWrap.PassageStatus.Success, w.Status + " " + w.Error);
            Assert.True(w.AllPassed);
            Holds(IndependentChecks.ClosedManifold(w.Result), "the wrap");
            ResultChecks.AssertClean(w.Result, "the wrap");
            Holds(IndependentChecks.Encloses(w.Result, moved), "the wrap");
            Holds(PinsClear(w.Result, movedPins), "the pins");
            foreach (PassagePinResult pr in w.PinResults) Assert.True(pr.AxisOutside && pr.ClearanceLow <= pr.ClearanceHigh && pr.ClearanceLow > 0, "a pin result");
            if (s.IsExact) Holds(IndependentChecks.SameBits(first, w.Result, s.Scale), "the scaled wrap");
            if (s != Similarity.Identity) return;

            reference.Compute();
            Holds(IndependentChecks.SameBits(first, reference.Result), "a second Compute()");
            Holds(IndependentChecks.SameBits(first, Run(new DMesh3(two), pins, threads: 1).Result), "one thread");
            Holds(IndependentChecks.SameBits(first, Run(new DMesh3(two), pins.Select(p => new PassagePin(p.Axis.AsEnumerable().Reverse(), p.Radius))).Result), "reversed axes");
            Holds(IndependentChecks.SameBits(first, Run(new DMesh3(two), pins.Concat(pins)).Result), "every pin twice");
            Holds(IndependentChecks.SameBits(first, Run(new DMesh3(two), pins.Reverse()).Result), "the pins in reverse order");
            PassageWrap blocked = Run(new DMesh3(two), new[] { new PassagePin(pins[0].Axis, 0.45) });
            Assert.Equal(PassageWrap.PassageStatus.PinBlockedByInput, blocked.Status);
            Assert.False(blocked.AllPassed);
        }

        /// <summary>
        /// The sharp-feature pass (AlphaWrapFeaturePass) keeps the wrap's guarantees by an independent check: closed, manifold, clean,
        /// the input strictly inside, every vertex at least 0.9 offsets from the input, the Euler characteristic of the wrap; it is
        /// deterministic, 2^k is bit for bit, and a second pass on its result keeps the guarantees again.
        /// </summary>
        [Theory]
        [InlineData("tube", 20.0, 600.0)]
        [InlineData("star", 30.0, 300.0)]
        [InlineData("box", 12.0, 120.0)]
        public void AlphaWrapFeaturePass_KeepsTheGuarantees_Independently(string shape, double relativeAlpha, double relativeOffset)
        {
            DMesh3 input = shape == "tube" ? Tube(8, 1.0, 0.5, 0.5) : shape == "box" ? Box(new Vector3d(1, 0.7, 0.5)) : StarPrism();
            (DMesh3 wrap, DMesh3 result, int operations, double offset) Run(DMesh3 m)
            {
                var w = new MeshAlphaWrap(new DMesh3(m)) { RelativeAlpha = relativeAlpha, RelativeOffset = relativeOffset };
                Assert.True(w.Compute());
                var pass = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), m, w.UsedAlpha, w.UsedOffset);
                Assert.True(pass.Compute());
                return (w.Result, pass.Mesh, pass.PlaneMoves + pass.CreaseSnaps + pass.Splits + pass.Flips, w.UsedOffset);
            }
            void Guarantees(DMesh3 r, DMesh3 wrap, double offset, string what)
            {
                Holds(IndependentChecks.ClosedManifold(r), what);
                ResultChecks.AssertClean(r, what);
                Holds(IndependentChecks.Encloses(r, input), what);
                Assert.Equal(IndependentChecks.Euler(wrap), IndependentChecks.Euler(r));
                double nearest = r.VertexIndices().Min(v => IndependentChecks.Distance(input, r.GetVertex(v)));
                Assert.True(nearest >= 0.9 * offset * (1 - 1e-6), $"{what}: a vertex {nearest / offset:R} offsets from the input");
                Assert.True(IndependentChecks.Volume(r) > IndependentChecks.Volume(input), what + ": the volume is below the input's");
            }
            var (wrap0, result, operations, offset0) = Run(input);
            Assert.True(operations > 0, "the pass did nothing on an input with sharp edges");
            Guarantees(result, wrap0, offset0, "the pass");
            Holds(IndependentChecks.SameBits(result, Run(input).result), "a repeated run");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 })
                Holds(IndependentChecks.SameBits(result, Run(Scaled(input, s)).result, s), "scale " + s);
            var second = new AlphaWrapFeaturePass(new DMesh3(result, true), input, wrap0.GetBounds().DiagonalLength / relativeAlpha, offset0);
            Assert.True(second.Compute());
            Guarantees(second.Mesh, wrap0, offset0, "a second pass");
        }

        static DMesh3 StarPrism()
        {
            List<Vector2d> poly = Star(5, 1.2, 0.6);
            var m = new DMesh3();
            int n = poly.Count;
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, -0.4));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 0.4));
            int cb = m.AppendVertex(new Vector3d(0, 0, -0.4)), ct = m.AppendVertex(new Vector3d(0, 0, 0.4));
            for (int i = 0; i < n; ++i) { int j = (i + 1) % n; m.AppendTriangle(cb, j, i); m.AppendTriangle(ct, n + i, n + j); m.AppendTriangle(i, j, n + j); m.AppendTriangle(i, n + j, n + i); }
            return m;
        }

        public static IEnumerable<object[]> WrapOptions() => new[] { "PullPinchPoints", "no RefineNonManifold", "angle 25", "budget", "alpha field" }.Select(o => new object[] { o });

        /// <summary>
        /// The options of MeshAlphaWrap keep the guarantees (a clean solid, the input inside, the vertices at the offset) and the
        /// reproducibility (a second Compute(), one thread, 2^k bit for bit).
        /// </summary>
        [Theory]
        [MemberData(nameof(WrapOptions))]
        public void MeshAlphaWrap_Options_KeepGuarantees_AndReproducibility(string option)
        {
            DMesh3 input = Tube(12, 1.0, 0.5, 0.5);
            MeshAlphaWrap Make(DMesh3 m, double s, int threads = 0)
            {
                var w = new MeshAlphaWrap(new DMesh3(m)) { RelativeAlpha = 20, RelativeOffset = 300, MaxThreads = threads };
                switch (option) {
                    case "PullPinchPoints": w.PullPinchPoints = true; break;
                    case "no RefineNonManifold": w.RefineNonManifold = false; break;
                    case "angle 25": w.RefineMinAngleDegrees = 25; break;
                    case "budget": w.MaxEstimatedTriangles = 2000; w.MaxAlphaCoarsening = 1.5; break;
                    case "alpha field": w.AlphaField = p => s * (0.08 + 0.1 * Math.Abs(p.x / s)); break;
                }
                return w;
            }
            MeshAlphaWrap wrap = Make(input, 1);
            Assert.True(wrap.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, wrap.Status);
            DMesh3 r = new DMesh3(wrap.Result);
            Holds(IndependentChecks.ClosedManifold(r), option);
            ResultChecks.AssertClean(r, option);
            Holds(IndependentChecks.Encloses(r, input), option);
            double nearest = r.VertexIndices().Min(v => IndependentChecks.Distance(input, r.GetVertex(v)));
            Assert.True(nearest >= wrap.UsedOffset * (1 - 1e-6), $"a vertex {nearest / wrap.UsedOffset:R} offsets from the input");
            Assert.True(wrap.AlphaCoarsening >= 1 && (option != "budget" || wrap.AlphaCoarsening <= 1.5 * (1 + 1e-12)), "AlphaCoarsening " + wrap.AlphaCoarsening);
            wrap.Compute();
            Holds(IndependentChecks.SameBits(r, wrap.Result), "a second Compute()");
            MeshAlphaWrap one = Make(input, 1, threads: 1);
            one.Compute();
            Holds(IndependentChecks.SameBits(r, one.Result), "one thread");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                MeshAlphaWrap ws = Make(Scaled(input, s), s);
                ws.Compute();
                Holds(IndependentChecks.SameBits(r, ws.Result, s), "scale " + s);
            }
        }

        // ---------------------------------------------------------------- tet mesher

        static string TetValid(TetMesh3 t, out double volume, out DMesh3 boundary)
        {
            volume = 0; boundary = null;
            var facets = new Dictionary<(int, int, int), int>();
            int[][] F = { new[] { 1, 2, 3 }, new[] { 0, 3, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 1 } };
            (int, int, int) Canon(int a, int b, int c) => a < b && a < c ? (a, b, c) : b < a && b < c ? (b, c, a) : (c, a, b);
            for (int i = 0; i < t.Tets.Count; ++i) {
                Index4i q = t.Tets[i];
                Vector3d a = t.Vertices[q.a], b = t.Vertices[q.b], c = t.Vertices[q.c], d = t.Vertices[q.d];
                double vol = (b - a).Cross(c - a).Dot(d - a) / 6;
                if (!(vol > 0)) return $"tet {i} has the volume {vol:R}";
                volume += vol;
                foreach (int[] f in F) { var key = Canon(q[f[0]], q[f[1]], q[f[2]]); facets.TryGetValue(key, out int n); facets[key] = n + 1; }
            }
            var b3 = new DMesh3();
            foreach (Vector3d v in t.Vertices) b3.AppendVertex(v);
            int count = 0;
            foreach (var kv in facets) {
                if (kv.Value != 1) return $"facet {kv.Key} is used {kv.Value} times with one orientation";
                if (facets.ContainsKey(Canon(kv.Key.Item1, kv.Key.Item3, kv.Key.Item2))) continue;
                ++count;
                if (b3.AppendTriangle(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3) < 0) return $"boundary facet {kv.Key} is non-manifold";
            }
            if (count != t.BoundaryTriangles.Count) return $"{count} boundary facets, BoundaryTriangles has {t.BoundaryTriangles.Count}";
            var used = new HashSet<int>();
            foreach (int tid in b3.TriangleIndices()) { Index3i tv = b3.GetTriangle(tid); used.Add(tv.a); used.Add(tv.b); used.Add(tv.c); }
            foreach (int v in b3.VertexIndices().ToArray()) if (!used.Contains(v)) b3.RemoveVertex(v, false, false);
            boundary = b3;
            return "";
        }

        static string SameTets(TetMesh3 a, TetMesh3 b, double scale = 1)
        {
            if (a.Vertices.Count != b.Vertices.Count || a.Tets.Count != b.Tets.Count) return $"{a.Vertices.Count} vertices / {a.Tets.Count} tets vs {b.Vertices.Count} / {b.Tets.Count}";
            for (int i = 0; i < a.Vertices.Count; ++i) { Vector3d p = a.Vertices[i] * scale, q = b.Vertices[i]; if (p.x != q.x || p.y != q.y || p.z != q.z) return "vertex " + i; }
            for (int i = 0; i < a.Tets.Count; ++i) { Index4i x = a.Tets[i], y = b.Tets[i]; if (x.a != y.a || x.b != y.b || x.c != y.c || x.d != y.d) return "tet " + i; }
            return "";
        }

        public static IEnumerable<object[]> TetOptions() => new[] { "target edge", "size function", "grading", "surface deviation", "creases", "no improve" }.Select(o => new object[] { o });

        /// <summary>
        /// MeshTetrahedralization with its options: positive tets with matched facets, the boundary a clean solid around the input,
        /// the volume in the wrap's band; a second Compute(), one thread and 2^k give the same tets; a size function that returns
        /// only invalid sizes counts them and changes nothing.
        /// </summary>
        [Theory]
        [MemberData(nameof(TetOptions))]
        public void MeshTetrahedralization_Options_Valid_AndReproducible(string option)
        {
            DMesh3 input = Tube(10, 1.0, 0.5, 0.4);
            MeshTetrahedralization Make(DMesh3 m, double s, int threads = 0)
            {
                var t = new MeshTetrahedralization(new DMesh3(m)) { RelativeAlpha = 20, RelativeOffset = 300, MaxThreads = threads };
                switch (option) {
                    case "target edge": t.TargetEdgeLength = 0.25 * s; break;
                    case "size function": t.SizeFunction = p => s * (0.15 + 0.2 * Math.Abs(p.x / s)); break;
                    case "grading": t.GradingFactor = 0.5; break;
                    case "surface deviation": t.MaxSurfaceDeviation = 0.02 * s; break;
                    case "creases": t.CreaseAngleDegrees = 30; break;
                    case "no improve": t.Improve = false; break;
                }
                return t;
            }
            MeshTetrahedralization tet = Make(input, 1);
            Assert.True(tet.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tet.Status);
            TetMesh3 first = tet.Result;
            Holds(TetValid(first, out double volume, out DMesh3 boundary), option);
            Assert.True(string.IsNullOrEmpty(TetMeshQuality.NotValidReason(first)), "TetMeshQuality.NotValidReason: " + TetMeshQuality.NotValidReason(first));
            Holds(IndependentChecks.ClosedManifold(boundary), option + ": the boundary");
            ResultChecks.AssertClean(boundary, option + ": the boundary");
            Holds(IndependentChecks.Encloses(boundary, input), option + ": the boundary");
            double inputVolume = IndependentChecks.Volume(input), slack = IndependentChecks.Area(input) * (tet.UsedOffset + tet.UsedAlpha);
            Assert.True(volume >= inputVolume && volume <= inputVolume + slack, $"the tet volume {volume:R} is outside [{inputVolume:R}, {inputVolume + slack:R}]");
            Close(volume, IndependentChecks.Volume(boundary), 1e-9, "the tet volume against its boundary's");
            Assert.NotNull(tet.Targets);
            tet.Compute();
            Holds(SameTets(first, tet.Result), "a second Compute()");
            MeshTetrahedralization one = Make(input, 1, threads: 1);
            one.Compute();
            Holds(SameTets(first, one.Result), "one thread");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                MeshTetrahedralization ts = Make(Scaled(input, s), s);
                Assert.True(ts.Compute());
                Holds(SameTets(first, ts.Result, s), "scale " + s);
            }
            if (option != "size function") return;
            MeshTetrahedralization plain = new MeshTetrahedralization(new DMesh3(input)) { RelativeAlpha = 20, RelativeOffset = 300 };
            plain.Compute();
            MeshTetrahedralization invalid = new MeshTetrahedralization(new DMesh3(input)) { RelativeAlpha = 20, RelativeOffset = 300, SizeFunction = p => p.x > 0 ? double.NaN : -1 };
            invalid.Compute();
            Assert.True(invalid.InvalidSizeSamples > 0);
            Holds(SameTets(plain.Result, invalid.Result), "a size function without a valid size");
        }

        // ---------------------------------------------------------------- 2D mesher

        static double InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double ax = a.x - d.x, ay = a.y - d.y, bx = b.x - d.x, by = b.y - d.y, cx = c.x - d.x, cy = c.y - d.y;
            return (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
        }

        /// <summary> positive triangles of the domain's area, mutual neighbours, every input point a vertex; the Delaunay property across the edges that are not subsegments (conforming: across all) </summary>
        static string Mesh2Valid(ConstrainedMesh2 m, ConstrainedMesh2Input input, double area, bool conforming)
        {
            var dir = new Dictionary<(int, int), int>();
            double sum = 0;
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                double ar = ((b - a).x * (c - a).y - (b - a).y * (c - a).x) / 2;
                if (!(ar > 0)) return $"triangle {t} has the area {ar:R}";
                sum += ar;
                for (int k = 0; k < 3; ++k) { var e = (tv[k], tv[(k + 1) % 3]); if (dir.ContainsKey(e)) return $"directed edge {e} in two triangles"; dir[e] = t; }
            }
            if (Math.Abs(sum - area) > 1e-9 * area) return $"area {sum:R} vs the domain's {area:R}";
            for (int i = 0; i < input.Points.Count; ++i) if (m.Vertices[m.InputToVertex[i]] != input.Points[i]) return $"input point {i} is not a vertex at its position";
            var sub = new HashSet<(int, int)>();
            foreach (Mesh2Subsegment s in m.Subsegments) { sub.Add((s.A, s.B)); sub.Add((s.B, s.A)); }
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                double scale = Math.Max((b - a).LengthSquared, (c - a).LengthSquared), eps = 1e-9 * scale * scale;
                for (int k = 0; k < 3; ++k) {
                    int u = tv[(k + 1) % 3], v = tv[(k + 2) % 3], nb = m.Neighbours[t][k];
                    int across = dir.TryGetValue((v, u), out int o) ? o : -1;
                    if (nb != across) return $"Neighbours[{t}][{k}] is {nb}, the triangle across ({u}, {v}) is {across}";
                    if (nb < 0) { if (!sub.Contains((u, v))) return $"boundary edge ({u}, {v}) is not a subsegment"; continue; }
                    if (!conforming && sub.Contains((u, v))) continue;
                    Index3i nv = m.Triangles[nb];
                    int opp = nv.a != u && nv.a != v ? nv.a : nv.b != u && nv.b != v ? nv.b : nv.c;
                    if (InCircle(a, b, c, m.Vertices[opp]) > eps) return $"vertex {opp} is inside the circumcircle of triangle {t}";
                }
            }
            return "";
        }

        public static IEnumerable<object[]> Mesh2Options() => new[] { "default", "conforming", "conforming 25", "size function", "steiner limit" }.Select(o => new object[] { o });

        /// <summary>
        /// QualityMesh2 with its options: a valid triangulation of the domain that is Delaunay across unconstrained edges (Conforming:
        /// across every edge), by a brute-force in-circle test; the size function bounds the longest edge; a small Steiner limit is
        /// kept and reported; a second Compute() and 2^k give the same triangles; a far copy of the domain in the same input is valid
        /// with twice the area; the points in another order give a valid mesh again.
        /// </summary>
        [Theory]
        [MemberData(nameof(Mesh2Options))]
        public void QualityMesh2_Options_Delaunay_Bounds_Reuse_FarCopy(string option)
        {
            List<Vector2d> outer = Ngon(17, 2, 0.05), hole = Ngon(5, 0.7, 0.3);
            hole.Reverse();
            var free = new[] { new Vector2d(1.3, 0.2), new Vector2d(-1.2, -0.7), new Vector2d(0.1, 1.5) };
            double Ring(List<Vector2d> r) => Math.Abs(Enumerable.Range(0, r.Count).Sum(i => r[i].x * r[(i + 1) % r.Count].y - r[i].y * r[(i + 1) % r.Count].x) / 2);
            double area = Ring(outer) - Ring(hole);
            Func<double, double, double> sizeF = (x, y) => 0.15 + 0.1 * Math.Abs(x);
            ConstrainedMesh2Input Input(double s, bool farCopy = false, int shuffle = 0)
            {
                var points = new List<Vector2d>(); var rings = new List<(int[], bool)>();
                foreach (double dx in farCopy ? new[] { 0.0, 64.0 } : new[] { 0.0 }) {
                    rings.Add((Enumerable.Range(points.Count, outer.Count).ToArray(), false)); points.AddRange(outer.Select(p => (p + new Vector2d(dx, 0)) * s));
                    rings.Add((Enumerable.Range(points.Count, hole.Count).ToArray(), true)); points.AddRange(hole.Select(p => (p + new Vector2d(dx, 0)) * s));
                    points.AddRange(free.Select(p => (p + new Vector2d(dx, 0)) * s));
                }
                int[] perm = Enumerable.Range(0, points.Count).ToArray();
                if (shuffle != 0) { var rng = new Random(shuffle); perm = perm.OrderBy(i => rng.Next()).ToArray(); }
                var input = new ConstrainedMesh2Input();
                var placed = new Vector2d[points.Count];
                for (int i = 0; i < points.Count; ++i) placed[perm[i]] = points[i];
                input.Points.AddRange(placed);
                foreach (var (ids, isHole) in rings) input.Rings.Add(new Mesh2Ring(ids.Select(i => perm[i]).ToArray(), isHole));
                return input;
            }
            QualityMesh2 Make(double s, bool farCopy = false)
            {
                switch (option) {
                    case "conforming": return new QualityMesh2 { Conforming = true, MinAngle = 0 };
                    case "conforming 25": return new QualityMesh2 { Conforming = true, MinAngle = 25, MaxArea = area * s * s / 100 };
                    case "size function": return new QualityMesh2 { SizeFunction = (x, y) => s * sizeF((farCopy && x > 32 * s ? x - 64 * s : x) / s, y / s) };
                    case "steiner limit": return new QualityMesh2 { MinAngle = 30, MaxArea = area * s * s / 500, SteinerLimit = 5 };
                    default: return new QualityMesh2();
                }
            }
            bool conforming = option.StartsWith("conforming");
            void Bounds(ConstrainedMesh2 m, QualityMesh2 q, double shiftBack)
            {
                if (m.SteinerLimitReached) return;
                var bad = new HashSet<int>(m.BadTriangles);
                for (int t = 0; t < m.Triangles.Count; ++t) {
                    Vector2d a = m.Vertices[m.Triangles[t].a], b = m.Vertices[m.Triangles[t].b], c = m.Vertices[m.Triangles[t].c];
                    double Angle(Vector2d p, Vector2d u, Vector2d v) => Math.Atan2(Math.Abs((u - p).x * (v - p).y - (u - p).y * (v - p).x), (u - p).Dot(v - p)) * MathUtil.Rad2Deg;
                    double min = Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b)));
                    Assert.True(bad.Contains(t) || min >= q.MinAngle - 1e-9, $"triangle {t} outside BadTriangles has a minimum angle of {min:R}");
                    if (q.MaxArea.HasValue) Assert.True(((b - a).x * (c - a).y - (b - a).y * (c - a).x) / 2 <= q.MaxArea.Value * (1 + 1e-12), "a triangle above MaxArea");
                    if (option != "size function") continue;
                    Vector2d g = (a + b + c) / 3;
                    double longest = Math.Max((a - b).Length, Math.Max((b - c).Length, (c - a).Length));
                    Assert.True(longest <= sizeF(g.x > 32 ? g.x - shiftBack : g.x, g.y) * (1 + 1e-12), "a triangle longer than the size function allows");
                }
            }
            ConstrainedMesh2Input input = Input(1);
            QualityMesh2 mesher = Make(1);
            ConstrainedTriangulation2Result r = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, r.Status);
            Holds(Mesh2Valid(r.Mesh, input, area, conforming), option);
            Bounds(r.Mesh, mesher, 0);
            if (option == "steiner limit") {
                Assert.True(r.Mesh.SteinerLimitReached, "a limit of 5 Steiner points is not reported");
                Assert.True(r.Mesh.Vertices.Count - r.Mesh.InputCount <= 5, "more Steiner points than the limit");
            } else Assert.False(r.Mesh.SteinerLimitReached);
            ConstrainedTriangulation2Result again = mesher.Compute(input);
            Assert.True(again.Mesh.Triangles.SequenceEqual(r.Mesh.Triangles) && again.Mesh.Vertices.SequenceEqual(r.Mesh.Vertices), "a second Compute() gives another mesh");
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                ConstrainedTriangulation2Result rs = Make(s).Compute(Input(s));
                Assert.Equal(Triangulation2Status.Success, rs.Status);
                Assert.True(rs.Mesh.Triangles.SequenceEqual(r.Mesh.Triangles) && rs.Mesh.Vertices.Select(p => p / s).SequenceEqual(r.Mesh.Vertices), "another mesh at scale " + s);
            }
            ConstrainedMesh2Input two = Input(1, farCopy: true);
            QualityMesh2 mesher2 = Make(1, farCopy: true);
            ConstrainedTriangulation2Result r2 = mesher2.Compute(two);
            Assert.Equal(Triangulation2Status.Success, r2.Status);
            Holds(Mesh2Valid(r2.Mesh, two, 2 * area, conforming), option + ", a far copy");
            Bounds(r2.Mesh, mesher2, 64);
            ConstrainedMesh2Input shuffled = Input(1, shuffle: 7);
            ConstrainedTriangulation2Result rp = Make(1).Compute(shuffled);
            Assert.Equal(Triangulation2Status.Success, rp.Status);
            Holds(Mesh2Valid(rp.Mesh, shuffled, area, conforming), option + ", shuffled points");
            Bounds(rp.Mesh, mesher, 0);
        }

        // ---------------------------------------------------------------- relations across the operations

        /// <summary>
        /// A mesh with gaps in its vertex and triangle ids (as any mesh after an edit that removes vertices) is the same input as
        /// its compact copy: the wrap, the tet mesher and PassageWrap give the same result bit for bit, the geodesic classes the
        /// same distances and lengths at the mapped vertices. (TpmsLattice.DomainMesh does not: finding H2-F1 of the hunt.)
        /// </summary>
        [Fact]
        public void IdGaps_AreTheSameInputAsTheCompactCopy()
        {
            DMesh3 tube = Tube(10, 1, 0.5, 0.4);
            DMesh3 gaps = IndependentChecks.WithIdGaps(tube, out Dictionary<int, int> map);
            Assert.False(gaps.IsCompact);
            Holds(IndependentChecks.ClosedManifold(gaps), "the mesh with gaps");
            Assert.Equal(IndependentChecks.Volume(tube), IndependentChecks.Volume(gaps));

            var wrapA = new MeshAlphaWrap(new DMesh3(tube)); var wrapB = new MeshAlphaWrap(gaps);
            Assert.True(wrapA.Compute() && wrapB.Compute());
            Holds(IndependentChecks.SameBits(wrapA.Result, wrapB.Result), "MeshAlphaWrap");
            var tetA = new MeshTetrahedralization(new DMesh3(tube)) { RelativeAlpha = 20 }; var tetB = new MeshTetrahedralization(gaps) { RelativeAlpha = 20 };
            Assert.True(tetA.Compute() && tetB.Compute());
            Holds(SameTets(tetA.Result, tetB.Result), "MeshTetrahedralization");
            var pin = new PassagePin(new[] { new Vector3d(0, 0, -0.8), new Vector3d(0, 0, 0.8) }, 0.2);
            var passA = new PassageWrap(new DMesh3(tube)); passA.Pins.Add(pin); var passB = new PassageWrap(gaps); passB.Pins.Add(pin);
            Assert.True(passA.Compute() && passB.Compute());
            Holds(IndependentChecks.SameBits(passA.Result, passB.Result), "PassageWrap");

            int s = 0, t = tube.VertexCount / 2 + 1;
            var polyA = new PolyhedralGeodesicDistance(tube); var polyB = new PolyhedralGeodesicDistance(gaps);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, polyA.Compute(new[] { s }));
            Assert.Equal(PolyhedralGeodesicStatus.Ok, polyB.Compute(new[] { map[s] }));
            var heatA = new MeshGeodesicDistance(tube); var heatB = new MeshGeodesicDistance(gaps);
            Assert.True(heatA.Compute(new[] { s }) && heatB.Compute(new[] { map[s] }));
            foreach (int v in tube.VertexIndices()) {
                Assert.Equal(polyA.Distance[v], polyB.Distance[map[v]]);
                Assert.True(Math.Abs(heatA.Distance[v] - heatB.Distance[map[v]]) <= 1e-12, "heat distance of vertex " + v);
            }
            Assert.Equal(polyA.TracePath(t).Length, polyB.TracePath(map[t]).Length);
            var pathA = new MeshGeodesicPath(tube); var pathB = new MeshGeodesicPath(gaps);
            Assert.Equal(GeodesicPathStatus.Success, pathA.Compute(s, t));
            Assert.Equal(GeodesicPathStatus.Success, pathB.Compute(map[s], map[t]));
            Assert.Equal(pathA.Length, pathB.Length);
            var intrinsicA = new IntrinsicTriangulation(tube); var intrinsicB = new IntrinsicTriangulation(gaps);
            Assert.Equal(intrinsicA.VertexCount, intrinsicB.VertexCount);
            Assert.Equal(intrinsicA.DelaunayRefine(25).InsertedVertices, intrinsicB.DelaunayRefine(25).InsertedVertices);
        }

        /// <summary>
        /// A field multiplied by a positive power of two has the same level set: MarchingCubes gives the same mesh bit for bit for
        /// every factor, DualContouring while the gradient of the field stays far above 2.2e-16 (below it: finding H2-F2).
        /// </summary>
        [Theory]
        [InlineData(-300, false)]
        [InlineData(-60, false)]
        [InlineData(-40, true)]
        [InlineData(-10, true)]
        [InlineData(20, true)]
        [InlineData(100, true)]
        [InlineData(300, false)]
        public void Contouring_FieldTimesAPowerOfTwo_SameLevelSet(int exponent, bool dualContouringToo)
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
            double c = Math.Pow(2, exponent);
            Func<Vector3d, double> sphere = p => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 1;
            DMesh3 mc = Contour(false, sphere, box, 0.125);
            Holds(IndependentChecks.ClosedManifold(mc), "marching cubes");
            Holds(IndependentChecks.SameBits(mc, Contour(false, p => c * sphere(p), box, 0.125)), "MarchingCubes of the scaled field");
            if (!dualContouringToo) return;
            DMesh3 dc = Contour(true, sphere, box, 0.125);
            Holds(IndependentChecks.ClosedManifold(dc), "dual contouring");
            Holds(IndependentChecks.SameBits(dc, Contour(true, p => c * sphere(p), box, 0.125)), "DualContouring of the scaled field");
        }

        /// <summary>
        /// Far beyond the 2^10 of Similarity.All(): from 2^-100 to 2^100 the geodesic classes, the intrinsic refinement, the contour
        /// loft and marching cubes give the scaled result bit for bit (outside about 2^-240 .. 2^250 some of them report success
        /// with another result: finding H2-F3).
        /// </summary>
        [Theory]
        [InlineData(-100)]
        [InlineData(-60)]
        [InlineData(60)]
        [InlineData(100)]
        public void LargePowerOfTwoScales_GiveTheScaledResult(int exponent)
        {
            double s = Math.Pow(2, exponent);
            DMesh3 mesh = Sphere(1, 6, new Vector3d(0.1, 0.2, 0.3)), scaled = Scaled(mesh, s);
            double[] d0 = Polyhedral(mesh, 0), d = Polyhedral(scaled, 0);
            var heat0 = new MeshGeodesicDistance(mesh); var heat = new MeshGeodesicDistance(scaled);
            Assert.True(heat0.Compute(new[] { 0 }) && heat.Compute(new[] { 0 }));
            for (int v = 0; v < mesh.MaxVertexID; ++v) {
                Assert.Equal(d0[v] * s, d[v]);
                Assert.Equal(heat0.Distance[v] * s, heat.Distance[v]);
            }
            var path0 = new MeshGeodesicPath(mesh); var path = new MeshGeodesicPath(scaled);
            Assert.Equal(GeodesicPathStatus.Success, path0.Compute(0, 77));
            Assert.Equal(GeodesicPathStatus.Success, path.Compute(0, 77));
            Assert.Equal(path0.Length * s, path.Length);
            IntrinsicRefinementResult r0 = new IntrinsicTriangulation(mesh).DelaunayRefine(25), r = new IntrinsicTriangulation(scaled).DelaunayRefine(25);
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.Equal(r0.InsertedVertices, r.InsertedVertices);
            Assert.Equal(r0.MinAngle, r.MinAngle);

            List<Vector3d> a = At(Ngon(8, 1, 0.1), 0), b = At(Ngon(6, 0.8, 0.3), 1);
            ContourLoftResult l0 = new ContourLoft { Caps = true }.Compute(a, b), l = new ContourLoft { Caps = true }.Compute(a.Select(p => p * s).ToList(), b.Select(p => p * s).ToList());
            Assert.Equal(ContourLoftStatus.Success, l.Status);
            Holds(IndependentChecks.SameBits(l0.Mesh, l.Mesh, s), "the scaled loft");
            var box = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
            DMesh3 mc0 = Contour(false, p => p.Length - 1, box, 0.25), mc = Contour(false, p => s * ((p / s).Length - 1), new AxisAlignedBox3d(box.Min * s, box.Max * s), 0.25 * s);
            Holds(IndependentChecks.SameBits(mc0, mc, s), "the scaled marching cubes mesh");
        }
    }
}
