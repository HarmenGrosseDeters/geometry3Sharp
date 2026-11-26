using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Procedural closed solids with curved or rotated surfaces for the metamorphic Boolean tests (spheres, tori, boxes,
    /// moved and rotated copies). Coordinates are not dyadic, so every result is rounded: the relations are checked within
    /// the documented rounding bound, not exactly.
    /// </summary>
    public static class CurvedSolids
    {
        public static DMesh3 Sphere(double radius, Vector3d center, int edgeVertices = 7)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            return Moved(m, center);
        }

        public static DMesh3 Box(Vector3d lo, Vector3d hi) =>
            new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();

        /// <summary> outward oriented torus about the z axis: major radius R, minor radius r, nu steps around, nv around the tube </summary>
        public static DMesh3 Torus(double R, double r, Vector3d center, int nu = 16, int nv = 8)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    double rho = R + r * Math.Cos(v);
                    m.AppendVertex(new Vector3d(rho * Math.Cos(u), rho * Math.Sin(u), r * Math.Sin(v)) + center);
                }
            int V(int i, int j) => (i % nu) * nv + (j % nv);
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    m.AppendTriangle(V(i, j), V(i + 1, j), V(i + 1, j + 1));
                    m.AppendTriangle(V(i, j), V(i + 1, j + 1), V(i, j + 1));
                }
            if (SignedVolume(m) < 0) m.ReverseOrientation();
            return m;
        }

        public static double SignedVolume(DMesh3 m) => BooleanOracle.Volume(m);

        public static DMesh3 Moved(DMesh3 m, Vector3d d)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, c.GetVertex(v) + d);
            return c;
        }

        public static DMesh3 Rotated(DMesh3 m, Vector3d axis, double degrees, Vector3d about)
        {
            Matrix3d r = Matrix3d.AxisAngleD(axis.Normalized, degrees);
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, r * (c.GetVertex(v) - about) + about);
            return c;
        }

        /// <summary> every triangle reversed: a consistently inside-out copy </summary>
        public static DMesh3 Inverted(DMesh3 m)
        {
            var c = new DMesh3(m);
            c.ReverseOrientation();
            return c;
        }

        /// <summary>
        /// Midpoint splits of about every third edge (chosen by seed): each split keeps the surface (the new vertex is the
        /// edge midpoint, the two triangles at the edge become four). The mesh stays closed and conforming.
        /// </summary>
        public static DMesh3 EdgeMidpointsSplit(DMesh3 m, int seed)
        {
            var c = new DMesh3(m);
            var rand = new Random(seed);
            foreach (int e in c.EdgeIndices().ToArray())
                if (rand.Next(3) == 0) c.SplitEdge(e, out _, 0.5);
            return c;
        }

        public sealed class Pair
        {
            public readonly string Name;
            public readonly DMesh3 A, B;
            /// <summary> general position: the result must be clean. Hard pairs (touching, near-coincident, identical): only the relations. </summary>
            public readonly bool General;
            public Pair(string name, DMesh3 a, DMesh3 b, bool general) { Name = name; A = a; B = b; General = general; }
            public override string ToString() => Name;
        }

        /// <summary> the corpus: overlapping, nested, disjoint, touching, near-coincident and rotated pairs </summary>
        public static readonly Dictionary<string, Pair> Pairs = Build();

        public static IEnumerable<object[]> PairNames() => Pairs.Keys.Select(k => new object[] { k });

        static Dictionary<string, Pair> Build()
        {
            var list = new List<Pair>();
            DMesh3 s1 = Sphere(1, Vector3d.Zero);
            list.Add(new Pair("sphere overlap", s1, Sphere(0.8, new Vector3d(0.9, 0.1, 0.05)), true));
            list.Add(new Pair("sphere nested", s1, Sphere(0.5, new Vector3d(0.1, 0.05, 0.02)), true));
            list.Add(new Pair("sphere disjoint", s1, Sphere(1, new Vector3d(3, 0.2, 0.1)), true));
            AxisAlignedBox3d bb = s1.CachedBounds;
            list.Add(new Pair("sphere touching", s1, Moved(s1, new Vector3d(bb.Width, 0, 0)), false));
            list.Add(new Pair("sphere near coincident", s1, Moved(s1, new Vector3d(1e-6, 0, 0)), false));
            list.Add(new Pair("sphere asymmetric", s1, Sphere(0.6, new Vector3d(0.9, 0.05, 0.02)), true));
            list.Add(new Pair("sphere same radius shifted", s1, Moved(s1, new Vector3d(0.3, 0.1, 0.0)), true));
            DMesh3 box = Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            DMesh3 box2 = Rotated(Moved(Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), new Vector3d(0.5, 0.2, 0.1)), new Vector3d(1, 2, 3), 30, Vector3d.Zero);
            list.Add(new Pair("box rotated box", box, box2, true));
            list.Add(new Pair("box sphere", box, Sphere(1.2, new Vector3d(0.7, 0.6, 0.4)), true));
            DMesh3 t1 = Torus(1, 0.35, Vector3d.Zero);
            list.Add(new Pair("torus sphere in hole", t1, Sphere(0.9, new Vector3d(0.05, 0.03, 0.02)), true));
            list.Add(new Pair("torus linked torus", t1, Moved(Rotated(t1, Vector3d.AxisX, 90, Vector3d.Zero), new Vector3d(1, 0.01, 0.02)), true));
            list.Add(new Pair("torus box", t1, Box(new Vector3d(0.5, -0.5, -0.2), new Vector3d(1.7, 0.5, 0.3)), true));
            DMesh3 f1 = Rotated(Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), new Vector3d(1, 1, 0.5), 17, Vector3d.Zero);
            DMesh3 f2 = Rotated(Box(new Vector3d(1, 0, 0), new Vector3d(2, 1, 1)), new Vector3d(1, 1, 0.5), 17, Vector3d.Zero);
            list.Add(new Pair("rotated boxes face touching", f1, f2, false));
            list.Add(new Pair("identical sphere", s1, new DMesh3(s1), false));
            return list.ToDictionary(p => p.Name);
        }
    }
}
