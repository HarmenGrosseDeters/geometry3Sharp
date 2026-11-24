// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using g3;

namespace g3cli
{
    /// <summary>
    /// boolean --volume-gradient: the gradient of the volume of the exact result with respect to every input vertex
    /// (BooleanVolumeGradient), written as .csv (one row per vertex) or .json (everything), with the volume, its bound and the
    /// contact regions (kinks of the volume) in the command's result.
    /// </summary>
    public static partial class Commands
    {
        static Param VolumeGradientParam() =>
            new Param("volume_gradient", ParamType.String, "also write the gradient of the volume of the exact result (before rounding) with respect to " +
                "every input vertex (BooleanVolumeGradient; turns on ExactMeshBoolean.RetainClassification) to this .csv (mesh, vertex, x, y, z, gx, gy, gz, " +
                "bx, by, bz: the entry and its error bound per component; vertex ids 0-based in the order of the mesh as read) or .json file (the same " +
                "per vertex, plus volume, volume_bound and the contacts); the result gets volume_gradient with the volume, its bound and the contacts " +
                "(flush faces where the volume has a kink: the gradient is the realized branch, each alternative the change to another one). " +
                "Every input must be closed (fails with operation_failed otherwise)");

        /// <summary> the format of the volume_gradient path (csv or json), checked before the boolean runs </summary>
        static string VolumeGradientFormat(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".csv" && ext != ".json")
                throw new CommandException(ResultCodes.UnknownFormat, $"cannot write '{path}': the volume gradient is written as .csv or .json");
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (Directory.Exists(dir) == false)
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': directory does not exist");
            return ext.Substring(1);
        }

        /// <summary>
        /// computes the gradient of op (computed with RetainClassification) and writes it to path + ".g3tmp" (the caller moves it
        /// into place with CommitVolumeGradient after the mesh is written, or removes it); returns the result's summary
        /// </summary>
        static JsonObject VolumeGradient(ExactMeshBoolean op, List<DMesh3> meshes, string path, string format)
        {
            var g = new BooleanVolumeGradient(op);
            if (g.Compute() == false)
                throw new CommandException(ResultCodes.OperationFailed, "volume gradient failed: " + g.Error,
                    new JsonObject() { ["volume_gradient_error"] = g.Error, ["boolean"] = BooleanInfo(op) });
            JsonArray contacts = ContactsJson(g);
            int rows = 0;
            string temp = path + ".g3tmp";
            string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
            try {
                using (var w = new StreamWriter(temp, false, new System.Text.UTF8Encoding(false))) {
                    if (format == "csv")
                        w.Write("mesh,vertex,x,y,z,gx,gy,gz,bx,by,bz\n");
                    else
                        w.Write("{\"volume\":" + R(g.Volume) + ",\"volume_bound\":" + R(g.VolumeBound) + ",\"contacts\":" + contacts.ToJsonString() + ",\"vertices\":[");
                    for (int m = 0; m < meshes.Count; ++m)
                        foreach (int v in meshes[m].VertexIndices()) {
                            Vector3d p = meshes[m].GetVertex(v);
                            Vector3d d = v < g.Gradient[m].Length ? g.Gradient[m][v] : Vector3d.Zero;
                            Vector3d b = v < g.Gradient[m].Length ? g.GradientBound[m][v] : Vector3d.Zero;
                            if (format == "csv")
                                w.Write($"{m},{v},{R(p.x)},{R(p.y)},{R(p.z)},{R(d.x)},{R(d.y)},{R(d.z)},{R(b.x)},{R(b.y)},{R(b.z)}\n");
                            else {
                                var e = new JsonObject() { ["mesh"] = m, ["vertex"] = v, ["position"] = MeshReport.Vec(p),
                                    ["gradient"] = MeshReport.Vec(d), ["bound"] = MeshReport.Vec(b) };
                                w.Write(rows == 0 ? "\n" : ",\n");
                                w.Write(e.ToJsonString());
                            }
                            rows++;
                        }
                    if (format == "json")
                        w.Write("\n]}\n");
                }
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                try { File.Delete(temp); } catch (Exception) { }
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
            return new JsonObject() {
                ["output"] = path, ["format"] = format, ["rows"] = rows,
                ["volume"] = MeshReport.Num(g.Volume), ["volume_bound"] = MeshReport.Num(g.VolumeBound),
                ["contacts"] = contacts,
            };
        }

        /// <summary> moves the written gradient into place (commit) or removes it </summary>
        static void CommitVolumeGradient(string path, bool commit)
        {
            string temp = path + ".g3tmp";
            try {
                if (commit)
                    File.Move(temp, path, true);
                else
                    File.Delete(temp);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                try { File.Delete(temp); } catch (Exception) { }
                if (commit)
                    throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
        }

        /// <summary> the contact regions: meshes, sheets, area and per other branch its sparse change of the gradient </summary>
        static JsonArray ContactsJson(BooleanVolumeGradient g)
        {
            var a = new JsonArray();
            foreach (BooleanVolumeGradient.ContactRegion region in g.Contacts) {
                var alternatives = new JsonArray();
                foreach (BooleanVolumeGradient.GradientTerm[] branch in region.Alternatives)
                    alternatives.Add(new JsonArray(branch.Select(t => (JsonNode)new JsonObject() {
                        ["mesh"] = t.Mesh, ["vertex"] = t.Vertex, ["value"] = MeshReport.Vec(t.Value), ["bound"] = MeshReport.Vec(t.Bound),
                    }).ToArray()));
                a.Add(new JsonObject() {
                    ["meshes"] = new JsonArray(region.Meshes.Select(m => (JsonNode)m).ToArray()),
                    ["sheets"] = region.Sheets, ["area"] = MeshReport.Num(region.Area), ["alternatives"] = alternatives,
                });
            }
            return a;
        }
    }
}
