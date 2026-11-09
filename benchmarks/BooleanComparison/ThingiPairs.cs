// The thingi-pairs class (see README.md, "Thingi10K pairs after the EMBER protocol"): pair preparation and the
// check that decides which models may be drawn.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class ThingiPairs
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // thingi-pair <meshA> <meshB> <dir> qx qy qz qw tx ty tz
    // A and B are each translated so that the centre of the bounding box is the origin and scaled by 1 / (longest
    // side), so that the longest side is 1 (the unit box); then B is rotated about the origin by the quaternion
    // (normalized here) and translated by t. Both meshes are written with round-trip precision, so every tool
    // reads the same doubles. The list was filtered with NotCleanReason on the models as read; normalizing and rotating
    // rounds the coordinates, so the transformed inputs are checked again (a contact or self-intersection made by the
    // rounding would make their volumes and the identities wrong): if one is not clean, WriteCase resolves and measures
    // them as for any other class.
    public static int Pair(string[] args)
    {
        DMesh3 a = Program.Load(args[0]), b = Program.Load(args[1]);
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        double[] p = args.Skip(3).Take(7).Select(x => double.Parse(x, ci)).ToArray();
        Quaterniond q = new Quaterniond(p[0], p[1], p[2], p[3]);
        q.Normalize();
        Vector3d t = new Vector3d(p[4], p[5], p[6]);
        Normalize(a, Quaterniond.Identity, Vector3d.Zero);
        Normalize(b, q, t);
        var extra = new Dictionary<string, object> {
            ["notclean"] = true, ["trisA"] = a.TriangleCount, ["trisB"] = b.TriangleCount,
            ["a"] = Path.GetFileNameWithoutExtension(args[0]), ["b"] = Path.GetFileNameWithoutExtension(args[1]),
        };
        Program.WriteCase(a, b, args[2], Path.GetFileNameWithoutExtension(args[0]) + "+" + Path.GetFileNameWithoutExtension(args[1]),
            extra, inputsClean: MeshRobustRepair.NotCleanReason(a) == "" && MeshRobustRepair.NotCleanReason(b) == "");
        return 0;
    }

    static void Normalize(DMesh3 m, Quaterniond rotation, Vector3d translation)
    {
        AxisAlignedBox3d bounds = m.CachedBounds;
        Vector3d c = bounds.Center;
        double s = 1.0 / bounds.MaxDim;
        foreach (int vid in m.VertexIndices())
            m.SetVertex(vid, rotation * ((m.GetVertex(vid) - c) * s) + translation);
    }

    // thingi-filter <out.csv> <mesh>...: "id,triangles,reason" per mesh; reason is NotCleanReason ("" = clean)
    // or "load failed". Appends; ids that are in the file already are skipped, so that an interrupted run resumes.
    public static int Filter(string[] args)
    {
        string outPath = args[0];
        var done = new HashSet<string>();
        if (File.Exists(outPath))
            foreach (string line in File.ReadLines(outPath))
                done.Add(line.Split(',')[0]);
        else
            File.WriteAllText(outPath, "");
        foreach (string path in args.Skip(1)) {
            string id = Path.GetFileNameWithoutExtension(path);
            if (done.Contains(id))
                continue;
            DMesh3 m = Program.Load(path);
            string reason = m == null ? "load failed" : MeshRobustRepair.NotCleanReason(m);
            // a clean mesh must also enclose a positive volume (not inside out as a whole)
            if (reason == "" && Program.Volume(m, m.CachedBounds.Center) <= 0)
                reason = "inside out";
            File.AppendAllText(outPath, $"{id},{m?.TriangleCount ?? 0},{reason}\n");
        }
        return 0;
    }
}
