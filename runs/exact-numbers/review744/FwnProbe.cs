using System;
using System.Collections.Generic;
using System.Linq;
using g3;

static class P
{
    static DMesh3 Sphere(double radius, Vector3d at, int ev = 20)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = ev }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, at);
        return m;
    }
    static DMesh3 Join(params DMesh3[] parts)
    {
        DMesh3 m = new DMesh3(parts[0]);
        for (int i = 1; i < parts.Length; ++i) new MeshEditor(m).AppendMesh(parts[i]);
        return m;
    }
    static DMesh3 Tet(Vector3d p, double s)
    {
        DMesh3 m = new DMesh3();
        int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + s * Vector3d.AxisX), v2 = m.AppendVertex(p + s * Vector3d.AxisY), v3 = m.AppendVertex(p + s * Vector3d.AxisZ);
        m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
        return m;
    }

    // query points on (the double image of) every triangle in [t0, t1)
    static void Run(string label, DMesh3 m, Func<int, bool> pick, double beta)
    {
        var tree = new DMeshAABBTree3(m, true) { FWNBeta = beta };
        tree.FastWindingNumber(Vector3d.Zero);
        var rnd = new Random(5);
        int n = 0, bad = 0, nan = 0; double worst = 0; string worstAt = "";
        foreach (int tid in m.TriangleIndices()) {
            if (pick(tid) == false) continue;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(tid, ref a, ref b, ref c);
            var qs = new List<Vector3d> { (1.0 / 3.0) * (a + b + c), (a + b + c) / 3.0, a, b, c, 0.5 * (a + b), 0.5 * (b + c), 0.5 * (c + a) };
            for (int k = 0; k < 4; ++k) {
                double u = rnd.NextDouble(), v = rnd.NextDouble();
                if (u + v > 1) { u = 1 - u; v = 1 - v; }
                qs.Add(a + u * (b - a) + v * (c - a));
            }
            // nearly at a corner
            qs.Add(a + 1e-9 * (b - a) + 1e-9 * (c - a));
            foreach (Vector3d q in qs) {
                double fast = tree.FastWindingNumber(q), exact = tree.WindingNumber(q);
                double d = Math.Abs(fast - exact);
                n++;
                if (double.IsNaN(d)) { nan++; continue; }
                if (d > 0.1) bad++;
                if (d > worst) { worst = d; worstAt = $"tri {tid} q {q} fast {fast:R} exact {exact:R}"; }
            }
        }
        Console.WriteLine($"{label,-58} beta {beta:0.###}: {n} queries, |fast-exact| > 0.1: {bad}, NaN: {nan}, worst {worst:E2}  {worstAt}");
    }

    static void Main(string[] args)
    {
        double far = 1e12;
        foreach (double beta in new[] { 2.0, Math.Sqrt(3), 1.7320508075688772 }) {
            // A: unit spheres at the origin, a tetrahedron at 1e12 (padding of master 1.8)
            DMesh3 near = Join(Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(0.5, 0.3, 0.2)));
            int nearT = near.TriangleCount;
            DMesh3 A = Join(near, Tet(new Vector3d(far, far, far), 1));
            Run("A unit spheres at 0, tet at 1e12", A, t => t < nearT, beta);
            // B: A in the unit frame (2^-40)
            DMesh3 B = new DMesh3(A); MeshTransforms.Scale(B, Math.Pow(2, -40));
            Run("B = A * 2^-40", B, t => t < nearT, beta);
            // C: unit sphere at 1e12 (edges of about 800 ulps), tet at the origin
            DMesh3 C = Join(Sphere(1, new Vector3d(far, far, far)), Tet(Vector3d.Zero, 1));
            int cT = C.TriangleCount - 4;
            Run("C unit sphere at 1e12, tet at 0", C, t => t < cT, beta);
            // D: spheres of radius r at 1e12: edges of a few ulps (ulp 1.2e-4)
            foreach (double r in new[] { 1e-1, 1e-2, 3e-3, 1e-3, 3e-4 }) {
                DMesh3 D = Join(Sphere(r, new Vector3d(far, far, far)), Tet(Vector3d.Zero, 1));
                int dT = D.TriangleCount - 4;
                Run($"D sphere r={r} at 1e12 (edge {r * 0.08 / 1.2e-4:0.#} ulp), tet at 0", D, t => t < dT, beta);
            }
            // E: a flat axis-aligned grid at z = 1e12 + small, tiny triangles (axis-flat boxes)
            {
                DMesh3 E = new DMesh3();
                int N = 24; double h = 4 * 1.220703125e-4;
                int[,] id = new int[N + 1, N + 1];
                for (int i = 0; i <= N; ++i) for (int j = 0; j <= N; ++j) id[i, j] = E.AppendVertex(new Vector3d(far + i * h, far + j * h, far));
                for (int i = 0; i < N; ++i) for (int j = 0; j < N; ++j) { E.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]); E.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]); }
                int eT = E.TriangleCount;
                E = Join(E, Tet(Vector3d.Zero, 1));
                Run("E flat grid at 1e12, edges 4 ulp", E, t => t < eT, beta);
            }
        }
    }
}
