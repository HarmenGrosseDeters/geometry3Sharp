// Synthetic hard cases for the boolean comparison: coplanar faces, solids that touch exactly at a face, an
// edge or a vertex, very thin overlaps and gaps, large (UTM) coordinates, large differences in scale, and
// iterated sequences of booleans (cube grids). Where the volumes of the results are known analytically, they
// are stored in info.json ("expected") and checked by eval.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class Synthetic
{
    // closed box with outward orientation and exactly the given coordinates
    static DMesh3 Box(Vector3d min, Vector3d max)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
        int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        foreach (int[] q in quads) {
            m.AppendTriangle(q[0], q[1], q[2]);
            m.AppendTriangle(q[0], q[2], q[3]);
        }
        return m;
    }

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        return Box(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1));
    }

    static DMesh3 Sphere(Vector3d center, double r, int edgeVertices = 16)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, center);
        return m;
    }

    // box with a regular grid triangulation on its faces
    static DMesh3 MeshedBox(double size, int edgeVertices)
    {
        var box = new Box3d(new Vector3d(size / 2, size / 2, size / 2), new Vector3d(size / 2, size / 2, size / 2));
        return new GridBox3Generator() { Box = box, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
    }

    // rotation about the z axis that keeps z coordinates exact
    static DMesh3 RotateZ(DMesh3 m, Vector3d center, double degrees)
    {
        var r = new DMesh3(m);
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        foreach (int vid in r.VertexIndices()) {
            Vector3d p = r.GetVertex(vid) - center;
            r.SetVertex(vid, center + new Vector3d(c * p.x - s * p.y, s * p.x + c * p.y, p.z));
        }
        return r;
    }

    static DMesh3 Rotate(DMesh3 m, Vector3d center, Vector3d axis, double degrees)
    {
        var r = new DMesh3(m);
        MeshTransforms.Rotate(r, center, Quaterniond.AxisAngleD(axis.Normalized, degrees));
        return r;
    }

    static DMesh3 Shift(DMesh3 m, Vector3d d)
    {
        var r = new DMesh3(m);
        MeshTransforms.Translate(r, d);
        return r;
    }

    static Dictionary<string, object> Expected(double union, double intersection, double difference)
    {
        return new Dictionary<string, object> {
            ["expected"] = new Dictionary<string, double> { ["union"] = union, ["intersection"] = intersection, ["difference"] = difference } };
    }

    public static int Write(string root)
    {
        Directory.CreateDirectory(root);
        var cases = new List<string>();
        void pair(string name, DMesh3 a, DMesh3 b, Dictionary<string, object> extra = null)
        {
            string dir = Path.GetFullPath(Path.Combine(root, name));
            Program.WriteCase(a, b, dir, name, extra);
            cases.Add(JsonSerializer.Serialize(new Dictionary<string, object> { ["class"] = "synthetic", ["name"] = name, ["case"] = dir }));
        }

        DMesh3 unit = Box(0, 0, 0, 1, 1, 1);
        // coplanar faces
        pair("coplanar_shift_x", unit, Box(0.5, 0, 0, 1.5, 1, 1), Expected(1.5, 0.5, 0.5));
        pair("coplanar_shift_xy", unit, Box(0.5, 0.5, 0, 1.5, 1.5, 1), Expected(1.75, 0.25, 0.75));
        pair("coplanar_half_box", unit, Box(0, 0, 0, 1, 1, 0.5), Expected(1, 0.5, 0.5));
        pair("identical_box", unit, Box(0, 0, 0, 1, 1, 1), Expected(1, 1, 0));
        double oct = 2 * (Math.Sqrt(2) - 1);   // square ∩ square rotated by 45°
        pair("coplanar_rotated_45", unit, RotateZ(unit, new Vector3d(0.5, 0.5, 0.5), 45), Expected(2 - oct, oct, 1 - oct));
        pair("coplanar_meshed_half_cell", MeshedBox(1, 5), Shift(MeshedBox(1, 5), new Vector3d(0.125, 0.25, 0)),
            Expected(2 - 0.875 * 0.75, 0.875 * 0.75, 1 - 0.875 * 0.75));
        // exactly touching solids
        pair("touch_face", unit, Box(1, 0, 0, 2, 1, 1), Expected(2, 0, 1));
        pair("touch_face_partial", unit, Box(1, 0.25, 0.25, 2, 1.25, 1.25), Expected(2, 0, 1));
        pair("touch_edge", unit, Box(1, 1, 0, 2, 2, 1), Expected(2, 0, 1));
        pair("touch_vertex", unit, Box(1, 1, 1, 2, 2, 2), Expected(2, 0, 1));
        pair("nested_touching_face", unit, Box(0.25, 0.25, 0, 0.75, 0.75, 0.5), Expected(1, 0.125, 0.875));
        // very thin overlap and gap, nearly coplanar faces
        double vThin = (2 - (1 - 1e-9)) * 0.6 * 0.6, vGap = (2 - (1 + 1e-9)) * 0.6 * 0.6;
        pair("thin_overlap_1e-9", unit, Box(1 - 1e-9, 0.2, 0.2, 2, 0.8, 0.8), Expected(1 + vThin - 1e-9 * 0.36, 1e-9 * 0.36, 1 - 1e-9 * 0.36));
        pair("thin_gap_1e-9", unit, Box(1 + 1e-9, 0.2, 0.2, 2, 0.8, 0.8), Expected(1 + vGap, 0, 1));
        pair("nearly_coplanar_1e-6deg", unit, Rotate(Box(0.5, 0, 0, 1.5, 1, 1), new Vector3d(1, 0.5, 0.5), Vector3d.AxisX, 1e-6));
        pair("sliver_rotated_0.01deg", unit, Rotate(Box(0.5, 0, 0, 1.5, 1, 1), new Vector3d(1, 0.5, 0.5), Vector3d.AxisX, 0.01));
        // spheres: generic control case, identical and rotated copies
        DMesh3 sphere = Sphere(Vector3d.Zero, 1);
        pair("spheres_generic", sphere, Sphere(new Vector3d(0.7, 0.3, 0.2), 1));
        pair("sphere_identical", sphere, new DMesh3(sphere));
        pair("sphere_rotated_same_center", sphere, Rotate(sphere, Vector3d.Zero, new Vector3d(1, 2, 3), 37));
        // large coordinates (UTM, metres): buildings on the same ground plane, spheres
        var utm = new Vector3d(300000.123, 5600000.456, 50);
        DMesh3 house = Box(utm, utm + new Vector3d(20, 20, 15));
        pair("utm_buildings_axis_aligned", house, Box(utm + new Vector3d(10, 5, 0), utm + new Vector3d(30, 25, 15)),
            Expected(2 * 6000 - 10 * 15 * 15, 10 * 15 * 15, 6000 - 10 * 15 * 15));
        pair("utm_buildings_rotated", house, RotateZ(Shift(house, new Vector3d(7.3, 4.1, 0)), utm + new Vector3d(17.3, 14.1, 0), 15));
        pair("utm_spheres", Sphere(utm, 10), Sphere(utm + new Vector3d(6, 3, 2), 10));
        pair("far_from_origin_1e8", Sphere(new Vector3d(1e8, 1e8, 1e8), 1), Sphere(new Vector3d(1e8 + 0.7, 1e8 + 0.3, 1e8 + 0.2), 1));
        // large differences in scale
        DMesh3 big = Sphere(Vector3d.Zero, 1000, 32);
        Vector3d onSurface = big.GetVertex(big.VertexIndices().First());
        var h = new Vector3d(5e-4, 5e-4, 5e-4);
        pair("scale_1e3_vs_1e-3", big, Box(onSurface - h, onSurface + h));
        pair("scale_tiny_1e-6", Sphere(Vector3d.Zero, 1e-6), Sphere(new Vector3d(0.7e-6, 0.3e-6, 0.2e-6), 1e-6));

        cases.Add(Sequence(root, "cube_grid_5", 5));
        cases.Add(Sequence(root, "cube_grid_10", 10));
        File.WriteAllLines(Path.Combine(root, "cases.jsonl"), cases);
        Console.WriteLine($"{cases.Count} cases in {Path.Combine(root, "cases.jsonl")}");
        return 0;
    }

    // n x n x n overlapping unit cubes (spacing 0.9, so the faces of a layer are exactly coplanar) are united
    // one after the other, then a small cube (edge 0.5) is subtracted at the centre of each: n^3 - 1 unions
    // and n^3 differences (1999 booleans for n = 10). The result is the box minus n^3 disjoint cubic voids.
    // Written as an Open Boolean Benchmark request (request.json) with paths relative to the case directory.
    static string Sequence(string root, string name, int n)
    {
        string dir = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(dir);
        var ops = new List<Dictionary<string, object>>();
        var load = new List<int>();
        void add(DMesh3 m, string file)
        {
            Program.WriteOFF(m, Path.Combine(dir, file));
            load.Add(ops.Count);
            ops.Add(new Dictionary<string, object> { ["op"] = "load-mesh", ["path"] = file, ["name"] = file });
        }
        for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) for (int k = 0; k < n; ++k)
            add(Box(0.9 * i, 0.9 * j, 0.9 * k, 0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1), $"cube_{i}_{j}_{k}.off");
        for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) for (int k = 0; k < n; ++k)
            add(Box(0.9 * i + 0.25, 0.9 * j + 0.25, 0.9 * k + 0.25, 0.9 * i + 0.75, 0.9 * j + 0.75, 0.9 * k + 0.75), $"void_{i}_{j}_{k}.off");
        int cubes = n * n * n, current = load[0];
        for (int c = 1; c < cubes; ++c) {
            ops.Add(new Dictionary<string, object> { ["op"] = "boolean-union", ["args"] = new[] { current, load[c] } });
            current = ops.Count - 1;
        }
        for (int c = 0; c < cubes; ++c) {
            ops.Add(new Dictionary<string, object> { ["op"] = "boolean-difference", ["args"] = new[] { current, load[cubes + c] } });
            current = ops.Count - 1;
        }
        ops[current]["name"] = "result";
        double side = 0.9 * (n - 1) + 1, volume = side * side * side - cubes * 0.125;
        var request = new Dictionary<string, object> {
            ["kind"] = "boolean-benchmark", ["version"] = 1, ["id"] = name, ["system_snapshot"] = new Dictionary<string, object>(),
            ["runs"] = new[] { new Dictionary<string, object> {
                ["case_id"] = name, ["out_format"] = "obj",
                ["bounding_box"] = new Dictionary<string, object> { ["min"] = new[] { 0.0, 0, 0 }, ["max"] = new[] { side, side, side } },
                ["operations"] = ops } } };
        File.WriteAllText(Path.Combine(dir, "request.json"), JsonSerializer.Serialize(request));
        var info = new Dictionary<string, object> {
            ["file"] = name, ["tris"] = 2 * cubes * 12, ["closed"] = true, ["wclosed"] = true, ["selfint"] = 0,
            ["vA"] = volume, ["vB"] = 0.0, ["booleans"] = 2 * cubes - 1,
            ["ops"] = new[] { "final" }, ["expected"] = new Dictionary<string, double> { ["final"] = volume } };
        File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(info));
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["class"] = "sequence", ["name"] = name, ["sequence"] = dir });
    }
}
