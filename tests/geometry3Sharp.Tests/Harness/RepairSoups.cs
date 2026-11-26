using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The broken-soup generator of the browser demo (RepairDemo.MakeSoup, PR #1574), as a procedural test input (#1575): a clean solid (box,
    /// sphere or torus), optionally a second shifted copy so that two shells cross, unwelded to a triangle soup, with every k-th triangle
    /// reversed, random triangles removed (holes of about one triangle), vertex jitter, duplicated triangles and fins. Same parameters and
    /// seed as the demo, so the numbers match the issue.
    /// </summary>
    public sealed class RepairSoupParams
    {
        public string Shape = "sphere";          // sphere | box | torus
        public int Resolution = 10;
        public int FlipEvery;                    // 0 = none, else every k-th triangle reversed
        public int Holes;                        // triangles removed
        public int JitterExp;                    // 0 = none, else each soup vertex moved by up to 10^-JitterExp of the size
        public int DuplicatePercent;             // share of triangles appended a second time
        public bool OverlappingShell;            // a second copy of the solid, shifted so that the two shells cross
        public int Fins;                         // loose triangles that cross the surface
        public int Seed = 1;

        /// <summary> the repro of #1575 </summary>
        public static RepairSoupParams Issue(string shape, int resolution)
        {
            return new RepairSoupParams { Shape = shape, Resolution = resolution, FlipEvery = 4, Holes = 8, JitterExp = 7, DuplicatePercent = 10,
                OverlappingShell = true, Fins = 3, Seed = 1 };
        }
    }

    public static class RepairSoups
    {
        static DMesh3 BaseShape(RepairSoupParams p)
        {
            switch (p.Shape) {
                case "box": return GridBox(Vector3d.Zero, new Vector3d(0.5, 0.4, 0.3), p.Resolution);
                case "torus": return Torus(0.6, 0.25, 4 * p.Resolution, 2 * p.Resolution);
                default: return Sphere(Vector3d.Zero, 0.5, p.Resolution);
            }
        }

        static DMesh3 GridBox(Vector3d center, Vector3d halfExtents, int edgeVertices)
        {
            if (edgeVertices <= 2)
                return new TrivialBox3Generator() { Box = new Box3d(center, halfExtents) }.Generate().MakeDMesh();
            return new GridBox3Generator() { Box = new Box3d(center, halfExtents), EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        static DMesh3 Torus(double major, double minor, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    double r = major + minor * Math.Cos(v);
                    m.AppendVertex(new Vector3d(r * Math.Cos(u), minor * Math.Sin(v), r * Math.Sin(u)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, c, b);
                    m.AppendTriangle(a, d, c);
                }
            if (MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x < 0)
                m.ReverseOrientation();
            return m;
        }

        public static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }
        public static double Area(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).y; }

        /// <summary> the clean solid(s) and the broken soup made from them; knownVolume is the volume of the clean solid (of the exact union with OverlappingShell) </summary>
        public static DMesh3 MakeSoup(RepairSoupParams p, out DMesh3 clean, out double knownVolume)
        {
            var rnd = new Random(p.Seed);
            clean = BaseShape(p);
            knownVolume = Volume(clean);
            if (p.OverlappingShell) {
                DMesh3 second = BaseShape(p);
                MeshTransforms.Translate(second, new Vector3d(0.31, 0.17, 0.11));
                var union = new ExactMeshBoolean(clean, second, ExactMeshBoolean.Operations.Union);
                union.Compute();
                var both = new DMesh3(clean);
                new MeshEditor(both).AppendMesh(second);
                knownVolume = union.Result != null ? Volume(union.Result) : double.NaN;
                clean = both;
            }
            double size = clean.GetBounds().DiagonalLength;
            var soup = new DMesh3();
            var tris = clean.TriangleIndices().ToList();
            var holes = new HashSet<int>();
            while (holes.Count < Math.Min(p.Holes, tris.Count / 2))
                holes.Add(tris[rnd.Next(tris.Count)]);
            int k = 0;
            Func<Vector3d, Vector3d> jitter = v => p.JitterExp == 0 ? v
                : v + Math.Pow(10, -p.JitterExp) * size * new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            var appended = new List<Index3i>();
            foreach (int tid in tris) {
                if (holes.Contains(tid))
                    continue;
                Index3i t = clean.GetTriangle(tid);
                int[] nv = new int[3];
                for (int j = 0; j < 3; ++j)
                    nv[j] = soup.AppendVertex(jitter(clean.GetVertex(t[j])));
                ++k;
                var tri = (p.FlipEvery > 0 && k % p.FlipEvery == 0) ? new Index3i(nv[0], nv[2], nv[1]) : new Index3i(nv[0], nv[1], nv[2]);
                if (soup.AppendTriangle(tri) >= 0)
                    appended.Add(tri);
            }
            int dups = appended.Count * p.DuplicatePercent / 100;
            for (int i = 0; i < dups; ++i) {
                Index3i t = appended[rnd.Next(appended.Count)];
                int a = soup.AppendVertex(soup.GetVertex(t.a)), b = soup.AppendVertex(soup.GetVertex(t.b)), c = soup.AppendVertex(soup.GetVertex(t.c));
                soup.AppendTriangle(a, b, c);
            }
            for (int i = 0; i < p.Fins; ++i) {
                Vector3d c = clean.GetVertex(clean.VertexIndices().ElementAt(rnd.Next(clean.VertexCount)));
                Vector3d n = c.Normalized;
                Vector3d u = n.Cross(Math.Abs(n.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY).Normalized;
                double s = 0.08 * size;
                int a = soup.AppendVertex(c - n * s), b = soup.AppendVertex(c + n * s + u * s), cc = soup.AppendVertex(c + n * s - u * s);
                soup.AppendTriangle(a, b, cc);
            }
            return soup;
        }
    }
}
