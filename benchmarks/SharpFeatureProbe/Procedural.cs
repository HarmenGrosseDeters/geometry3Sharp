// Procedural CAD-like inputs (no mesh files are committed): polycubes (box, L-shape with a re-entrant edge, cube minus an
// octant with a re-entrant corner, slot) and prisms of a polygon (V-notch, chamfered block). All closed, manifold, outward.
using System;
using System.Collections.Generic;
using g3;

static class Procedural
{
    public static readonly string[] Names = { "box", "lshape", "octant", "slot", "vnotch", "chamfer" };

    public static DMesh3 Make(string name)
    {
        switch (name) {
            case "box": return PolyCube(10, 6, 4, (x, y, z) => true);
            case "lshape": return PolyCube(10, 10, 4, (x, y, z) => !(x >= 5 && y >= 5));
            case "octant": return PolyCube(8, 8, 8, (x, y, z) => !(x >= 4 && y >= 4 && z >= 4));
            // a slot 2 wide and 2 deep along x in the top face (two re-entrant 90 degree edges at its bottom, convex rim)
            case "slot": return PolyCube(12, 8, 4, (x, y, z) => !(y >= 3 && y < 5 && z >= 2));
            case "plates": {
                // the thin plates of MeshRobustRepairTests.ThinPlates(6)
                var rnd = new Random(6);
                DMesh3 m = new DMesh3();
                for (int i = 0; i < 12; ++i) {
                    var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    var e = new Vector3d(0.3 + 0.2 * rnd.NextDouble(), 0.02 + 0.03 * rnd.NextDouble(), 0.3);
                    var b = new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh();
                    var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                    MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(axis, 360 * rnd.NextDouble()));
                    MeshEditor.Append(m, b);
                }
                return m;
            }
            case "vnotch": return Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(6, 6),
                new Vector2d(5, 3), new Vector2d(4, 6), new Vector2d(0, 6) }, 4);
            case "chamfer": return Prism(new List<Vector2d> { new Vector2d(1.5, 0), new Vector2d(8.5, 0), new Vector2d(10, 1.5), new Vector2d(10, 4.5),
                new Vector2d(8.5, 6), new Vector2d(1.5, 6), new Vector2d(0, 4.5), new Vector2d(0, 1.5) }, 4);
        }
        throw new ArgumentException(name);
    }

    // solid cells of an nx x ny x nz grid of unit cells; every boundary cell face becomes two triangles, outward
    static DMesh3 PolyCube(int nx, int ny, int nz, Func<int, int, int, bool> solid)
    {
        Func<int, int, int, bool> s = (x, y, z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && solid(x, y, z);
        var mesh = new DMesh3();
        var ids = new Dictionary<(int, int, int), int>();
        Func<int, int, int, int> vid = (x, y, z) => {
            if (!ids.TryGetValue((x, y, z), out int id)) { id = mesh.AppendVertex(new Vector3d(x, y, z)); ids[(x, y, z)] = id; }
            return id;
        };
        int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        for (int x = 0; x < nx; ++x) for (int y = 0; y < ny; ++y) for (int z = 0; z < nz; ++z) {
            if (!s(x, y, z)) continue;
            foreach (int[] d in dirs) {
                if (s(x + d[0], y + d[1], z + d[2])) continue;
                // the face's corners, counter-clockwise seen from outside
                int ax = d[0] != 0 ? 0 : (d[1] != 0 ? 1 : 2);
                int u = (ax + 1) % 3, v = (ax + 2) % 3;
                int[] o = { x, y, z };
                int[] c = { o[0], o[1], o[2] };
                if (d[ax] > 0) c[ax] += 1;
                int[][] q = new int[4][];
                for (int k = 0; k < 4; ++k) {
                    q[k] = new[] { c[0], c[1], c[2] };
                    if (k == 1 || k == 2) q[k][u] += 1;
                    if (k == 2 || k == 3) q[k][v] += 1;
                }
                // (u, v, ax) is right-handed: u x v = +ax; for the negative side reverse
                int i0 = vid(q[0][0], q[0][1], q[0][2]), i1 = vid(q[1][0], q[1][1], q[1][2]), i2 = vid(q[2][0], q[2][1], q[2][2]), i3 = vid(q[3][0], q[3][1], q[3][2]);
                if (d[ax] > 0) { mesh.AppendTriangle(i0, i1, i2); mesh.AppendTriangle(i0, i2, i3); }
                else { mesh.AppendTriangle(i0, i2, i1); mesh.AppendTriangle(i0, i3, i2); }
            }
        }
        return mesh;
    }

    // counter-clockwise polygon extruded by h along z
    static DMesh3 Prism(List<Vector2d> poly, double h)
    {
        var mesh = new DMesh3();
        int n = poly.Count;
        for (int i = 0; i < n; ++i) mesh.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 0));
        for (int i = 0; i < n; ++i) mesh.AppendVertex(new Vector3d(poly[i].x, poly[i].y, h));
        foreach (var t in EarClip(poly)) {
            mesh.AppendTriangle(t.a, t.c, t.b);                 // bottom, facing down
            mesh.AppendTriangle(n + t.a, n + t.b, n + t.c);     // top
        }
        for (int i = 0; i < n; ++i) {
            int j = (i + 1) % n;
            mesh.AppendTriangle(i, j, n + j);
            mesh.AppendTriangle(i, n + j, n + i);
        }
        return mesh;
    }

    static List<Index3i> EarClip(List<Vector2d> p)
    {
        var idx = new List<int>();
        for (int i = 0; i < p.Count; ++i) idx.Add(i);
        var res = new List<Index3i>();
        Func<Vector2d, Vector2d, Vector2d, double> cross = (a, b, c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        while (idx.Count > 3) {
            bool clipped = false;
            for (int k = 0; k < idx.Count && !clipped; ++k) {
                int a = idx[(k + idx.Count - 1) % idx.Count], b = idx[k], c = idx[(k + 1) % idx.Count];
                if (cross(p[a], p[b], p[c]) <= 1e-12) continue;
                bool ear = true;
                foreach (int o in idx) {
                    if (o == a || o == b || o == c) continue;
                    if (cross(p[a], p[b], p[o]) >= 0 && cross(p[b], p[c], p[o]) >= 0 && cross(p[c], p[a], p[o]) >= 0) { ear = false; break; }
                }
                if (!ear) continue;
                res.Add(new Index3i(a, b, c));
                idx.RemoveAt(k);
                clipped = true;
            }
            if (!clipped) throw new InvalidOperationException("ear clipping failed");
        }
        res.Add(new Index3i(idx[0], idx[1], idx[2]));
        return res;
    }
}
