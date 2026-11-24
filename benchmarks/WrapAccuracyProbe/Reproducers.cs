// Mode "repro" (issue #1479): procedural classes of input on which MeshRobustRepair loses area, each run through the same analysis as
// the Thingi10K meshes (LostArea.Analyse). No mesh files. repro <out.jsonl> [name...]
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using g3;
using gs;

static class Reproducers
{
    public static readonly string[] Names = {
        "touching_boxes", "box_in_box", "box_fin_sheet", "box_fin_solid", "box_thin_plate", "flipped_closed", "flipped_hole", "box_flipped_third",
        "open_box_3faces", "open_tube" };

    static DMesh3 Box(int nx, int ny, int nz, double x, double y, double z)
    {
        DMesh3 m = Procedural.PolyCube(nx, ny, nz, (a, b, c) => true);
        Inputs.Translate(m, new Vector3d(x, y, z));
        return m;
    }

    static DMesh3 Join(params DMesh3[] ms)
    {
        var r = new DMesh3(ms[0], true);
        for (int i = 1; i < ms.Length; ++i) MeshEditor.Append(r, ms[i]);
        return r;
    }

    // an axis-aligned sheet of nu x nv quads (unwelded to anything), the normal along +axis
    static DMesh3 Sheet(int axis, double pos, double u0, double u1, double v0, double v1, int nu, int nv)
    {
        var m = new DMesh3();
        int[,] id = new int[nu + 1, nv + 1];
        int ua = (axis + 1) % 3, va = (axis + 2) % 3;
        for (int i = 0; i <= nu; ++i) for (int j = 0; j <= nv; ++j) {
            var p = new Vector3d(); p[axis] = pos; p[ua] = u0 + (u1 - u0) * i / nu; p[va] = v0 + (v1 - v0) * j / nv;
            id[i, j] = m.AppendVertex(p);
        }
        for (int i = 0; i < nu; ++i) for (int j = 0; j < nv; ++j) {
            m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
            m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
        }
        return m;
    }

    static void RemoveFaces(DMesh3 m, params Vector3d[] normals)
    {
        var kill = new List<int>();
        foreach (int t in m.TriangleIndices()) { Vector3d n = m.GetTriNormal(t); if (normals.Any(f => f.Dot(n) > 0.99)) kill.Add(t); }
        foreach (int t in kill) m.RemoveTriangle(t);
    }

    static void Flip(DMesh3 m, double share, int seed)
    {
        var rnd = new Random(seed);
        foreach (int t in m.TriangleIndices().ToList()) if (rnd.NextDouble() < share) m.ReverseTriOrientation(t);
    }

    public static DMesh3 Build(string name)
    {
        // "flipped_closed:<share>" a closed box with that share of its triangles flipped (the default 0.33)
        if (name.StartsWith("flipped_closed:")) { DMesh3 b = Box(16, 10, 6, 0, 0, 0); Flip(b, double.Parse(name.Substring(15), System.Globalization.CultureInfo.InvariantCulture), 3); return b; }
        switch (name) {
            // two closed boxes that share a 6 x 6 patch of one face (the contact face is doubled; a solid has no wall there)
            case "touching_boxes": return Join(Box(12, 6, 6, 0, 0, 0), Box(12, 6, 6, 6, 0, 6));
            // a closed box inside a closed box (the inner shell is an interior wall of the solid)
            case "box_in_box": return Join(Box(16, 10, 10, 0, 0, 0), Box(4, 4, 4, 6, 3, 3));
            // a closed box with an open sheet (zero thickness) standing on its top face, the sheet's base row on the box's vertices (not welded)
            case "box_fin_sheet": return Join(Box(16, 10, 6, 0, 0, 0), Sheet(1, 5, 2, 14, 6, 12, 12, 6));
            // the same fin as a thin closed plate (0.02 thick, a fiftieth of the unit cell), overlapping the box by 1
            case "box_fin_solid": { DMesh3 plate = Box(12, 1, 6, 2, 0, 5); ScaleY(plate, 0.02, 5); return Join(Box(16, 10, 6, 0, 0, 0), plate); }
            // the thin plate alone far from a coarse feature: a 16 x 10 x 6 box with a plate 0.002 thick, slightly overlapping it
            case "box_thin_plate": { DMesh3 plate = Box(12, 1, 6, 2, 0, 5); ScaleY(plate, 0.002, 5); return Join(Box(16, 10, 6, 0, 0, 0), plate); }
            // a closed box with 1/3 of its triangles flipped (orientation inconsistent, geometry closed and fine)
            case "flipped_closed": { DMesh3 b = Box(16, 10, 6, 0, 0, 0); Flip(b, 0.33, 3); return b; }
            // the same with a small hole (one face's centre: 6 quads)
            case "flipped_hole": { DMesh3 b = Box(16, 10, 6, 0, 0, 0); Inputs.RemovePatch(b, new Vector3d(8, 5, 6), 0.01); Flip(b, 0.33, 3); return b; }
            // the two x faces (19 % of the triangles, not a third; the name is kept for the result files) reversed as a block: two oppositely oriented parts of one surface
            case "box_flipped_third": { DMesh3 b = Box(16, 10, 6, 0, 0, 0); foreach (int t in b.TriangleIndices().ToList()) if (Math.Abs(b.GetTriNormal(t).x) > 0.99) b.ReverseTriOrientation(t); return b; }
            // a box missing three faces (top, +x, +y): the winding number of the solid's centre is 1/2
            case "open_box_3faces": { DMesh3 b = Box(16, 10, 6, 0, 0, 0); RemoveFaces(b, new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)); return b; }
            // an open tube (no caps): not a solid
            case "open_tube": { DMesh3 b = Box(16, 10, 6, 0, 0, 0); RemoveFaces(b, new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0)); return b; }
        }
        throw new ArgumentException(name);
    }

    static void ScaleY(DMesh3 m, double factor, double about)
    {
        foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); p.y = about + (p.y - about) * factor; m.SetVertex(v, p); }
    }

    public static int Run(string[] args)
    {
        string outFile = args[1];
        var names = args.Skip(2).ToList();
        if (names.Count == 0) names = Names.ToList();
        foreach (string n in names) {
            DMesh3 m = Build(n);
            var row = LostArea.Analyse("repro_" + n, m, new[] { 1e-4, 4e-4, 1e-3, 4e-3 }, true, false, -1);
            File.AppendAllText(outFile, JsonSerializer.Serialize(row, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }) + "\n");
        }
        return 0;
    }
}
