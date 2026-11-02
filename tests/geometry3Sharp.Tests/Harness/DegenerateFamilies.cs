using System;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The generated families of degenerate input of docs/design/SAFE_ROUNDING.md section 2 (design question 1b),
    /// shared by SafeRoundingDegenerateInputTests and benchmarks/SnapRoundingExperiment (compiled into both; no xunit
    /// here).
    /// </summary>
    public static class DegenerateFamilies
    {
        public enum Family { Collinear, Sliver, ShortEdge, Coincident, Unwelded, VertexOnFace, SharedFace, Cluster, FoldedCluster }

        public static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

        public static DMesh3 Box(Vector3d min, Vector3d max) => new TrivialBox3Generator() {
            Box = new Box3d(new AxisAlignedBox3d(min, max))
        }.Generate().MakeDMesh();

        static void Ok(MeshResult r)
        {
            if (r != MeshResult.Ok) throw new InvalidOperationException(r.ToString());
        }

        // every count-th triangle (from seed on) gets the defect of the family; k is the exponent of the size 2^-k
        public static DMesh3 Make(Family f, int k, int seed, int count = 9)
        {
            double h = Math.Pow(2, -k);
            switch (f) {
                case Family.Collinear:
                case Family.Sliver: {
                    // a vertex poked into a triangle and moved onto (Collinear) or 2^-k inside (Sliver) its edge ab:
                    // the triangle (a, b, p) has zero area or a height of 2^-k
                    DMesh3 m = Sphere(6);
                    foreach (int tid in m.TriangleIndices().Where(t => (t + seed) % count == 0).ToArray()) {
                        Index3i t = m.GetTriangle(tid);
                        Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                        double s = 0.25 + 0.5 * ((tid * 7 + seed) % 11) / 10.0;
                        Vector3d onEdge = a + s * (b - a);
                        Vector3d p = f == Family.Collinear ? onEdge : onEdge + h * (c - onEdge).Normalized;
                        Ok(m.PokeTriangle(tid, out DMesh3.PokeTriangleInfo info));
                        m.SetVertex(info.new_vid, p);
                    }
                    return m;
                }
                case Family.ShortEdge:
                case Family.Coincident: {
                    // an edge split at 2^-k from its end (ShortEdge) or at its end (Coincident: two welded
                    // vertices at one position, two triangles of zero area)
                    DMesh3 m = Sphere(6);
                    foreach (int eid in m.EdgeIndices().Where(e => (e + seed) % count == 0).ToArray()) {
                        Index2i ev = m.GetEdgeV(eid);
                        Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                        Ok(m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                        m.SetVertex(info.vNew, f == Family.Coincident ? a : a + h * (b - a).Normalized);
                    }
                    return m;
                }
                case Family.Unwelded: {
                    // a vertex duplicated: some triangles of its fan use a copy at the same position (closed by
                    // position, open by ids)
                    DMesh3 m = Sphere(6);
                    foreach (int vid in m.VertexIndices().Where(v => (v + seed) % count == 0).ToArray()) {
                        int[] fan = m.VtxTrianglesItr(vid).ToArray();
                        int copy = m.AppendVertex(m.GetVertex(vid));
                        for (int i = 0; i < fan.Length / 2; i++) {
                            Index3i t = m.GetTriangle(fan[i]);
                            m.RemoveTriangle(fan[i], false, false);
                            m.AppendTriangle(t.a == vid ? copy : t.a, t.b == vid ? copy : t.b, t.c == vid ? copy : t.c);
                        }
                    }
                    return m;
                }
                case Family.VertexOnFace: {
                    // a box, and an octahedron whose lowest vertex lies on the box's top face (or 2^-k above it)
                    DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
                    double x = 0.3 + 0.05 * (seed % 7), y = 0.35 + 0.04 * (seed % 5), r = 0.2;
                    var o = new Vector3d(x, y, 1 + r + (k > 0 ? h : 0));
                    var oct = new DMesh3();
                    Vector3d[] p = { o + r * Vector3d.AxisX, o + r * Vector3d.AxisY, o - r * Vector3d.AxisX,
                                     o - r * Vector3d.AxisY, o + r * Vector3d.AxisZ, o - r * Vector3d.AxisZ };
                    foreach (Vector3d q in p) oct.AppendVertex(q);
                    for (int i = 0; i < 4; i++) {
                        oct.AppendTriangle(i, (i + 1) % 4, 4);
                        oct.AppendTriangle((i + 1) % 4, i, 5);
                    }
                    MeshEditor.Append(m, oct);
                    return m;
                }
                case Family.Cluster:
                case Family.FoldedCluster: {
                    // dense clusters: a triangle poked count times in a row, each new vertex near an edge of the
                    // triangle it pokes, so fans of slivers and zero-area triangles touch each other. Cluster: 2^-k of
                    // the way from the edge to the opposite corner (inside, the surface does not fold); FoldedCluster:
                    // 2^-k from the edge (outside slivers thinner than that: the surface folds over itself)
                    DMesh3 m = Sphere(6);
                    var rnd = new Random(seed);
                    foreach (int t0 in m.TriangleIndices().Where(t => (t + seed) % 17 == 0).ToArray()) {
                        int tid = t0;
                        for (int i = 0; i < count; i++) {
                            Index3i t = m.GetTriangle(tid);
                            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                            Vector3d onEdge = a + (0.2 + 0.6 * rnd.NextDouble()) * (b - a);
                            Vector3d p = k == 0 ? onEdge : f == Family.Cluster ? onEdge + h * (c - onEdge) : onEdge + h * (c - onEdge).Normalized;
                            if (m.PokeTriangle(tid, out DMesh3.PokeTriangleInfo info) != MeshResult.Ok) break;
                            m.SetVertex(info.new_vid, p);
                            tid = rnd.Next(3) == 0 ? info.new_t1 : rnd.Next(2) == 0 ? info.new_t2 : tid;
                        }
                    }
                    return m;
                }
                case Family.SharedFace: {
                    // two boxes in one mesh that share a region of a face (or are 2^-k apart)
                    DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
                    double x = 0.1 * (seed % 5), y = 0.15 * (seed % 3);
                    MeshEditor.Append(m, Box(new Vector3d(x, y, 1 + (k > 0 ? h : 0)), new Vector3d(x + 0.6, y + 0.5, 1.7)));
                    return m;
                }
            }
            throw new ArgumentException(f.ToString());
        }

    }
}
