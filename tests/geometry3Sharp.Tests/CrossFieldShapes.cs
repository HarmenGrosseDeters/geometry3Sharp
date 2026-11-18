using System;
using g3;
using gs;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Procedural test shapes of the cross field tests (CrossFieldTests, MeshPrincipalCurvatureTests): implicit shapes through
    /// MarchingCubes and 20 isotropic Remesher passes (the inputs of benchmarks/CrossFieldBenchmark, coarser), built once. No mesh files.
    /// </summary>
    public static class CrossFieldShapes
    {
        class Fn : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d B;
            public double Value(ref Vector3d p) { return F(p); }
            public AxisAlignedBox3d Bounds() { return B; }
        }

        public static double SphereRadius = 1, TorusMajor = 1, TorusMinor = 0.35;

        static DMesh3 Build(Func<Vector3d, double> f, double h, double noise)
        {
            var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
            var mc = new MarchingCubes { Implicit = new Fn { F = f, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
            mc.Generate();
            DMesh3 m = mc.Mesh;
            DMesh3 src = new DMesh3(m);
            var r = new Remesher(m) { SmoothSpeedT = 0.5 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            r.SetTargetEdgeLength(h);
            for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
            m = new DMesh3(m, true);
            if (noise > 0) {
                var rnd = new Random(1);
                MeshNormals.QuickCompute(m);
                foreach (int v in m.VertexIndices()) {
                    Vector3d n = (Vector3d)m.GetVertexNormal(v);
                    m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h));
                }
            }
            return m;
        }

        static double Sphere(Vector3d p) => p.Length - SphereRadius;
        static double Torus(Vector3d p) { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - TorusMajor; return Math.Sqrt(q * q + p.z * p.z) - TorusMinor; }
        static double FilletedBox(Vector3d p)
        {
            Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        }

        static readonly Lazy<DMesh3> sphere = new Lazy<DMesh3>(() => Build(Sphere, 0.1, 0));
        static readonly Lazy<DMesh3> torus = new Lazy<DMesh3>(() => Build(Torus, 0.1, 0));
        static readonly Lazy<DMesh3> noisyTorus = new Lazy<DMesh3>(() => Build(Torus, 0.1, 0.2));
        static readonly Lazy<DMesh3> noisySphere = new Lazy<DMesh3>(() => Build(Sphere, 0.1, 0.2));
        static readonly Lazy<DMesh3> filletedBox = new Lazy<DMesh3>(() => Build(FilletedBox, 0.1, 0));

        /// <summary> a copy: the unit sphere (about 1.6k vertices, 8 cone singularities expected) </summary>
        public static DMesh3 UnitSphere() => new DMesh3(sphere.Value);
        /// <summary> a copy: the unit sphere with every vertex moved up to 0.2 edge lengths along its normal </summary>
        public static DMesh3 NoisySphere() => new DMesh3(noisySphere.Value);
        /// <summary> a copy: the torus R = 1, r = 0.35 (about 1.7k vertices) </summary>
        public static DMesh3 TorusMesh() => new DMesh3(torus.Value);
        /// <summary> a copy: the torus with every vertex moved up to 0.2 edge lengths along its normal </summary>
        public static DMesh3 NoisyTorus() => new DMesh3(noisyTorus.Value);
        /// <summary> a copy: a box 2 x 1.4 x 1 with fillets of radius 0.15 (smooth, 8 corners) </summary>
        public static DMesh3 FilletedBoxMesh() => new DMesh3(filletedBox.Value);

        /// <summary> a box with sharp edges: 9 x 9 grid vertices per face, shared edge vertices (closed, genus 0) </summary>
        public static DMesh3 SharpBox()
        {
            var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), EdgeVertices = 9 };
            g.Generate();
            return g.MakeDMesh();
        }

        /// <summary> an open n x n grid patch on [0, 1]^2 with a smooth height (a boundary of straight edges and four corners) </summary>
        public static DMesh3 OpenPatch(int n = 24)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        /// <summary> the unit sphere without the triangles above z = 0.5 (one boundary loop, zigzag) </summary>
        public static DMesh3 SphereCap()
        {
            DMesh3 m = UnitSphere();
            var remove = new System.Collections.Generic.List<int>();
            foreach (int t in m.TriangleIndices())
                if (m.GetTriCentroid(t).z > 0.5) remove.Add(t);
            MeshEditor.RemoveTriangles(m, remove, true);
            return m;
        }
    }
}
