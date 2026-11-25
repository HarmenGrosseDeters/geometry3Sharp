// Procedural inputs of the probe (no mesh files) and the loader for mesh files given on the command line.
// All procedural solids are closed, manifold, outward; sizes are in units where the box is about 10 long.
using System;
using System.Collections.Generic;
using System.IO;
using g3;
using gs;

static class Inputs
{
    // Spike A (closed solids)
    public static readonly string[] SolidNames = {
        "box", "lshape", "slot0.20", "slot0.05", "slot0.01", "plate1e-2", "plate1e-3", "plate1e-4", "cylinder", "sphere", "box_far" };

    public static double Diag(DMesh3 m) { return m.GetBounds().DiagonalLength; }

    // block 10 x 6 (xy), height 4 (z); a slot of the given width (relative to the block's diagonal) cut from the top edge, 3 deep
    static DMesh3 SlotBlock(double relWidth)
    {
        double diag = Math.Sqrt(10 * 10 + 6 * 6 + 4 * 4);
        double w = relWidth * diag, x0 = 5 - 0.5 * w, x1 = 5 + 0.5 * w;
        var poly = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(x1, 6),
            new Vector2d(x1, 3), new Vector2d(x0, 3), new Vector2d(x0, 6), new Vector2d(0, 6) };
        return Procedural.Prism(poly, 4);
    }

    static DMesh3 Plate(double relThickness)
    {
        double diag0 = Math.Sqrt(10 * 10 + 10 * 10);                     // the plate's diagonal is about 14.14
        double t = relThickness * diag0;
        var poly = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) };
        return Procedural.Prism(poly, t);
    }

    static DMesh3 Cylinder(int n)
    {
        var poly = new List<Vector2d>();
        for (int i = 0; i < n; ++i) poly.Add(new Vector2d(3 * Math.Cos(2 * Math.PI * i / n), 3 * Math.Sin(2 * Math.PI * i / n)));
        return Procedural.Prism(poly, 8);
    }

    static DMesh3 Sphere(int edgeVertices)
    {
        return new Sphere3Generator_NormalizedCube() { Radius = 5, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
    }

    public static void Translate(DMesh3 m, Vector3d d)
    {
        foreach (int vid in m.VertexIndices()) m.SetVertex(vid, m.GetVertex(vid) + d);
    }

    public static DMesh3 Solid(string name)
    {
        switch (name) {
            case "box": return Procedural.Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(0, 6) }, 4);
            case "lshape": return Procedural.Make("lshape");
            case "slot0.20": return SlotBlock(0.20);
            case "slot0.05": return SlotBlock(0.05);
            case "slot0.01": return SlotBlock(0.01);
            case "plate1e-2": return Plate(1e-2);
            case "plate1e-3": return Plate(1e-3);
            case "plate1e-4": return Plate(1e-4);
            case "cylinder": return Cylinder(64);
            case "sphere": return Sphere(14);
            // 2^22 = 4194304 in each coordinate: exactly representable, ulp there is 9.3e-10
            case "box_far": { var m = Solid("box"); Translate(m, new Vector3d(4194304, 4194304, 4194304)); return m; }
        }
        throw new ArgumentException(name);
    }

    // the base solids of Spike B, finer than the Spike A ones (a hole of 1 % needs many triangles)
    public static DMesh3 FineSolid(string name)
    {
        switch (name) {
            case "box": return Procedural.PolyCube(16, 10, 6, (x, y, z) => true);
            case "cylinder": {
                // a polycube cannot make a cylinder; use the faceted prism with its caps subdivided by the ear clip (long triangles are fine)
                return Cylinder(48);
            }
            case "sphere": return Sphere(12);
        }
        throw new ArgumentException(name);
    }

    // an open sheet: a quarter cylinder shell (one bent surface) of 24 x 24 quads, radius 6, 90 degrees
    public static DMesh3 BentSheet()
    {
        int n = 24;
        var m = new DMesh3();
        for (int i = 0; i <= n; ++i) for (int j = 0; j <= n; ++j) {
            double a = 0.5 * Math.PI * i / n;
            m.AppendVertex(new Vector3d(6 * Math.Cos(a), 6 * Math.Sin(a), 10.0 * j / n));
        }
        Func<int, int, int> id = (i, j) => i * (n + 1) + j;
        for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) {
            m.AppendTriangle(id(i, j), id(i + 1, j), id(i + 1, j + 1));
            m.AppendTriangle(id(i, j), id(i + 1, j + 1), id(i, j + 1));
        }
        return m;
    }

    public static DMesh3 LoadFile(string path)
    {
        var builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(path, new ReadOptions(), builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) return null;
        DMesh3 mesh = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i) MeshEditor.Append(mesh, builder.Meshes[i]);
        return mesh;
    }

    // "proc:<name>" or a path
    public static DMesh3 Load(string spec)
    {
        if (spec.StartsWith("proc:")) return Solid(spec.Substring(5));
        return LoadFile(spec);
    }

    public static string NameOf(string spec) { return spec.StartsWith("proc:") ? spec.Substring(5) : Path.GetFileNameWithoutExtension(spec); }

    // ---- holes (Spike B) ----

    // Removes triangles, growing a region from the triangle nearest to `seed` by increasing distance of the centroid from the seed,
    // until `share` of the area is removed. Returns the removed area. The mesh is edited in place (closed solid -> open).
    public static double RemovePatch(DMesh3 m, Vector3d seed, double share)
    {
        double total = 0;
        var tris = new List<(double d, int tid, double area)>();
        foreach (int tid in m.TriangleIndices()) {
            double a = m.GetTriArea(tid); total += a;
            tris.Add(((m.GetTriCentroid(tid) - seed).LengthSquared, tid, a));
        }
        tris.Sort((p, q) => p.d.CompareTo(q.d));
        double removed = 0;
        foreach (var t in tris) {
            if (removed >= share * total) break;
            m.RemoveTriangle(t.tid);
            removed += t.area;
        }
        return removed;
    }

    // a slit: all triangles whose centroid is within `halfWidth` of the plane `axis = pos` and within `length` of `center` along the other axes
    public static double RemoveSlit(DMesh3 m, Vector3d center, int axis, double halfWidth, double length)
    {
        double removed = 0;
        var kill = new List<int>();
        foreach (int tid in m.TriangleIndices()) {
            Vector3d c = m.GetTriCentroid(tid);
            if (Math.Abs(c[axis] - center[axis]) <= halfWidth && (c - center).Length <= length) { kill.Add(tid); removed += m.GetTriArea(tid); }
        }
        foreach (int tid in kill) m.RemoveTriangle(tid);
        return removed;
    }
}
