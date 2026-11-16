// The thingi-contact class (see README.md, "Thingi10K pairs with exact contact"): the case writer of one pair. The
// transforms are in ContactTransforms.cs (exact, tested without data).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BoolCmp;
using g3;
using gs;

static class ThingiContact
{
    // thingi-contact <meshA> <meshB> <dir> <variant> <axis> <sign> <param>
    // The meshes are the Thingi10K models as read. An exact placement that does not exist (a coordinate sum would round)
    // is an error row, not a rounded case.
    public static int Pair(string[] args)
    {
        DMesh3 a = Program.Load(args[0]), b = Program.Load(args[1]);
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        ContactVariant variant = ContactTransforms.ParseVariant(args[3]);
        ContactResult r = ContactTransforms.Build(a, b, variant, int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6]));
        if (r.Error != null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "inexact transform: " + r.Error }));
            return 0;
        }
        string name = Path.GetFileNameWithoutExtension(args[0]) + "+" + Path.GetFileNameWithoutExtension(args[1]);
        var extra = new Dictionary<string, object>(r.Info) {
            ["notclean"] = true, ["trisA"] = r.A.TriangleCount, ["trisB"] = r.B.TriangleCount,
            ["a"] = Path.GetFileNameWithoutExtension(args[0]), ["b"] = Path.GetFileNameWithoutExtension(args[1]),
        };
        // inputs are checked again after the transform (the models were filtered as read)
        bool clean = MeshRobustRepair.NotCleanReason(r.A) == "" && MeshRobustRepair.NotCleanReason(r.B) == "";
        extra["inputs_clean"] = clean;
        if (variant == ContactVariant.FaceContact && (string)r.Info["kind"] == "face") {
            // touching only along a face: the volumes of the results are known exactly
            Vector3d center = r.A.CachedBounds.Center;
            double vA = Program.Volume(r.A, center), vB = Program.Volume(r.B, center);
            extra["expected"] = new Dictionary<string, double> { ["union"] = vA + vB, ["intersection"] = 0, ["difference"] = vA };
        }
        Program.WriteCase(r.A, r.B, args[2], name, extra, inputsClean: clean);
        return 0;
    }
}
