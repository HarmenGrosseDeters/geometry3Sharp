using System;
using System.Collections.Generic;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary> Procedural closed solids and transformations of them (no mesh files). </summary>
    public static class Shapes
    {
        public static DMesh3 Box(Vector3d center, Vector3d halfExtent) =>
            new TrivialBox3Generator() { Box = new Box3d(center, halfExtent) }.Generate().MakeDMesh();

        public static DMesh3 GridBox(Vector3d center, Vector3d halfExtent, int n) =>
            new GridBox3Generator() { Box = new Box3d(center, halfExtent), EdgeVertices = n }.Generate().MakeDMesh();

        public static DMesh3 Sphere(Vector3d center, double radius, int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        /// <summary> a torus around the z axis, rings x sides quads split into triangles, outward oriented </summary>
        public static DMesh3 Torus(Vector3d center, double major, double minor, int rings, int sides)
        {
            var m = new DMesh3();
            for (int i = 0; i < rings; ++i) {
                double u = 2 * Math.PI * i / rings;
                for (int j = 0; j < sides; ++j) {
                    double v = 2 * Math.PI * j / sides;
                    double r = major + minor * Math.Cos(v);
                    m.AppendVertex(center + new Vector3d(r * Math.Cos(u), r * Math.Sin(u), minor * Math.Sin(v)));
                }
            }
            for (int i = 0; i < rings; ++i) {
                int i1 = (i + 1) % rings;
                for (int j = 0; j < sides; ++j) {
                    int j1 = (j + 1) % sides;
                    int a = i * sides + j, b = i1 * sides + j, c = i1 * sides + j1, d = i * sides + j1;
                    m.AppendTriangle(a, b, c);
                    m.AppendTriangle(a, c, d);
                }
            }
            return m;
        }

        /// <summary> a prism over a simple polygon (counter-clockwise, star-shaped about its centroid), z in [z0, z1] </summary>
        public static DMesh3 Prism(IList<Vector2d> poly, double z0, double z1)
        {
            var m = new DMesh3();
            int n = poly.Count;
            Vector2d c = Vector2d.Zero;
            foreach (Vector2d p in poly) c += p;
            c /= n;
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, z0));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, z1));
            int cb = m.AppendVertex(new Vector3d(c.x, c.y, z0)), ct = m.AppendVertex(new Vector3d(c.x, c.y, z1));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(cb, j, i);
                m.AppendTriangle(ct, n + i, n + j);
                m.AppendTriangle(i, j, n + j);
                m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        public static List<Vector2d> Star(int points, double outer, double inner, double phase = 0)
        {
            var l = new List<Vector2d>();
            for (int i = 0; i < 2 * points; ++i) {
                double a = phase + Math.PI * i / points, r = (i % 2 == 0) ? outer : inner;
                l.Add(new Vector2d(r * Math.Cos(a), r * Math.Sin(a)));
            }
            return l;
        }

        public static List<Vector2d> Ngon(int n, double r, double phase = 0)
        {
            var l = new List<Vector2d>();
            for (int i = 0; i < n; ++i) l.Add(new Vector2d(r * Math.Cos(phase + 2 * Math.PI * i / n), r * Math.Sin(phase + 2 * Math.PI * i / n)));
            return l;
        }

        public static DMesh3 Cylinder(Vector3d center, double radius, double height, int n)
        {
            DMesh3 m = Prism(Ngon(n, radius), -height / 2, height / 2);
            MeshTransforms.Translate(m, center);
            return m;
        }

        public static DMesh3 Tet(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var m = new DMesh3();
            if ((b - a).Cross(c - a).Dot(d - a) < 0) { Vector3d t = b; b = c; c = t; }
            m.AppendVertex(a); m.AppendVertex(b); m.AppendVertex(c); m.AppendVertex(d);
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(2, 0, 3);
            return m;
        }

        public static DMesh3 Map(DMesh3 mesh, Func<Vector3d, Vector3d> f)
        {
            var c = new DMesh3(mesh);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, f(c.GetVertex(v)));
            return c;
        }

        public static DMesh3 Rotate(DMesh3 mesh, Vector3d axis, double degrees)
        {
            Matrix3d r = Matrix3d.AxisAngleD(axis.Normalized, degrees);
            return Map(mesh, p => r * p);
        }

        public static DMesh3 Combine(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts) {
                var map = new Dictionary<int, int>();
                foreach (int v in p.VertexIndices()) map[v] = m.AppendVertex(p.GetVertex(v));
                foreach (int t in p.TriangleIndices()) { Index3i tv = p.GetTriangle(t); m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            }
            return m;
        }

        /// <summary> a copy with shuffled vertex ids, triangle order and start corners </summary>
        public static DMesh3 Permute(DMesh3 mesh, int seed)
        {
            var rng = new Random(seed);
            var vs = new List<int>(mesh.VertexIndices());
            var ts = new List<int>(mesh.TriangleIndices());
            Shuffle(vs, rng); Shuffle(ts, rng);
            var m = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in vs) map[v] = m.AppendVertex(mesh.GetVertex(v));
            foreach (int t in ts) {
                Index3i tv = mesh.GetTriangle(t);
                int k = rng.Next(3);
                m.AppendTriangle(map[tv[k]], map[tv[(k + 1) % 3]], map[tv[(k + 2) % 3]]);
            }
            return m;
        }

        static void Shuffle<T>(List<T> l, Random rng)
        {
            for (int i = l.Count - 1; i > 0; --i) { int j = rng.Next(i + 1); T t = l[i]; l[i] = l[j]; l[j] = t; }
        }
    }
}
