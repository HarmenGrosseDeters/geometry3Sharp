// The abc-chained class (see README.md, "Chained booleans on ABC models"): the case writers of one chain. The placements
// are in ContactTransforms.cs (exact, tested without data); the chain itself is run by data/abc_chained.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BoolCmp;
using g3;
using gs;

static class AbcChained
{
    // abc-chain-inputs <meshA> <meshB> <meshC> <dir> <variantB> <axisB> <signB> <paramB> <variantC> <axisC> <signC> <paramC>
    // Writes A.off, B.off, C.off (round-trip precision) and info.json: B and C placed against A by exact placements.
    // An exact placement that does not exist is an error line, not a rounded case.
    public static int Inputs(string[] args)
    {
        DMesh3 a = Program.Load(args[0]), b = Program.Load(args[1]), c = Program.Load(args[2]);
        if (a == null || b == null || c == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        ChainResultOrError(a, b, c, args, out var r);
        if (r.Error != null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "inexact transform: " + r.Error }));
            return 0;
        }
        Directory.CreateDirectory(args[3]);
        Program.WriteOFF(r.A, Path.Combine(args[3], "A.off"));
        Program.WriteOFF(r.B, Path.Combine(args[3], "B.off"));
        Program.WriteOFF(r.C, Path.Combine(args[3], "C.off"));
        var info = new Dictionary<string, object>(r.Info) {
            ["trisA"] = r.A.TriangleCount, ["trisB"] = r.B.TriangleCount, ["trisC"] = r.C.TriangleCount,
            ["a"] = Path.GetFileNameWithoutExtension(args[0]), ["b"] = Path.GetFileNameWithoutExtension(args[1]), ["c"] = Path.GetFileNameWithoutExtension(args[2]),
            // the inputs are checked again after the transform (the models were filtered as read)
            ["inputs_clean"] = MeshRobustRepair.NotCleanReason(r.A) == "" && MeshRobustRepair.NotCleanReason(r.B) == "" && MeshRobustRepair.NotCleanReason(r.C) == "",
        };
        File.WriteAllText(Path.Combine(args[3], "chain.json"), JsonSerializer.Serialize(info));
        Console.WriteLine(JsonSerializer.Serialize(info));
        return 0;
    }

    static void ChainResultOrError(DMesh3 a, DMesh3 b, DMesh3 c, string[] args, out ContactTransforms.ChainResult r)
    {
        r = ContactTransforms.BuildChain(a, b, c,
            ContactTransforms.ParseVariant(args[4]), int.Parse(args[5]), int.Parse(args[6]), int.Parse(args[7]),
            ContactTransforms.ParseVariant(args[8]), int.Parse(args[9]), int.Parse(args[10]), int.Parse(args[11]));
    }

    // chain-step <A.off> <B.off> <dir> <name>
    // One step of a chain: the inputs are the results of the previous steps (of one method), written as they are into dir
    // (A.off, B.off, info.json) with the properties that eval and the identities need. The inputs are not necessarily clean
    // (a rounded result of a boolean): the volumes are then those of the resolved meshes, as for any other class.
    public static int Step(string[] args)
    {
        DMesh3 a = Program.Load(args[0]), b = Program.Load(args[1]);
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["error"] = "load failed" }));
            return 0;
        }
        string ra = MeshRobustRepair.NotCleanReason(a), rb = MeshRobustRepair.NotCleanReason(b);
        var extra = new Dictionary<string, object> {
            ["notclean"] = true, ["trisA"] = a.TriangleCount, ["trisB"] = b.TriangleCount,
            ["reasonA"] = ra, ["reasonB"] = rb, ["inputs_clean"] = ra == "" && rb == "",
        };
        Program.WriteCase(a, b, args[2], args[3], extra, inputsClean: ra == "" && rb == "");
        return 0;
    }
}
