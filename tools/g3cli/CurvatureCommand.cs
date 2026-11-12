// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary> curvature: MeshCurvature (Meyer et al. discrete mean and Gaussian curvature per vertex) as statistics and, with output, per vertex. </summary>
    public static partial class Commands
    {
        static Command Curvature()
        {
            var c = new Command() {
                Name = "curvature",
                Description = "Discrete mean (H) and Gaussian (K) curvature per vertex (MeshCurvature: the mixed Voronoi area, mean curvature normal and angle deficit of " +
                    "Meyer, Desbrun, Schroeder and Barr), as statistics over the valid vertices (interior, manifold, with area, not at a degenerate triangle), " +
                    "and with 'output' for every vertex. H is positive on a sphere oriented outward (1 / r), K is 1 / r^2. Values are in mesh units; " +
                    "'size' is the size MeshFidelityMetrics uses, and the *_scaled statistics are H * size and K * size^2 (comparable between meshes of different scale). " +
                    "Boundary and bowtie vertices get one-sided values that are written but not valid (not in the statistics). 'angle_deficit_total' is the sum over all vertices " +
                    "(2 pi times the Euler characteristic of a closed manifold mesh).",
            };
            c.Params.Add(Input());
            c.Params.Add(new Param("output", ParamType.String, "write every vertex to this .csv (vertex, x, y, z, mean, gaussian, mixed_area, angle_deficit, valid) or .json file; vertex ids are 0-based in the order of the mesh as read"));
            c.Run = args => {
                string outputFormat = null;
                if (args.Has("output")) {
                    string ext = Path.GetExtension(args.String("output")).ToLowerInvariant();
                    if (ext != ".csv" && ext != ".json")
                        throw new CommandException(ResultCodes.UnknownFormat, $"cannot write '{args.String("output")}': the curvatures are written as .csv or .json");
                    outputFormat = ext.Substring(1);
                    string dir = Path.GetDirectoryName(Path.GetFullPath(args.String("output")));
                    if (Directory.Exists(dir) == false)
                        throw new CommandException(ResultCodes.WriteError, $"cannot write '{args.String("output")}': directory does not exist");
                }
                var warnings = new List<string>();
                DMesh3 mesh = MeshFiles.Read(args.String("input"), warnings);
                MeshCurvature mc = MeshCurvature.Compute(mesh);
                double size = MeshFidelityMetrics.SizeOf(mesh, out _);
                var valid = mesh.VertexIndices().Where(v => mc.Valid[v]).ToList();
                var o = new JsonObject() { ["input"] = args.String("input"), ["vertices"] = mesh.VertexCount, ["valid_vertices"] = mc.ValidVertices,
                    ["boundary_vertices"] = mc.BoundaryVertices, ["bowtie_vertices"] = mc.BowtieVertices, ["degenerate_triangles"] = mc.DegenerateTriangles,
                    ["size"] = MeshReport.Num(size) };
                var h = valid.Select(v => mc.Mean[v]).ToList();
                var k = valid.Select(v => mc.Gaussian[v]).ToList();
                o["mean_curvature"] = StatsReport(DeviationStatistics.Compute(h));
                o["gaussian_curvature"] = StatsReport(DeviationStatistics.Compute(k));
                if (double.IsFinite(size)) {
                    o["mean_curvature_scaled"] = StatsReport(DeviationStatistics.Compute(h.Select(x => x * size).ToList()));
                    o["gaussian_curvature_scaled"] = StatsReport(DeviationStatistics.Compute(k.Select(x => x * size * size).ToList()));
                }
                o["angle_deficit_total"] = Val(mesh.VertexIndices().Sum(v => mc.AngleDeficit[v]));
                o["mixed_area_total"] = Val(mesh.VertexIndices().Sum(v => mc.MixedArea[v]));
                if (valid.Count == 0)
                    warnings.Add("no vertex is valid (no interior manifold vertex with area): the statistics are null");
                else if (mc.ValidVertices < mesh.VertexCount)
                    warnings.Add($"{mesh.VertexCount - mc.ValidVertices} of {mesh.VertexCount} vertices are not valid (boundary, bowtie or at a degenerate triangle) and are not in the statistics");
                if (outputFormat != null) {
                    WriteCurvature(args.String("output"), outputFormat, mesh, mc);
                    o["output"] = args.String("output"); o["output_format"] = outputFormat; o["output_rows"] = mesh.VertexCount;
                }
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }

        static void WriteCurvature(string path, string format, DMesh3 mesh, MeshCurvature mc)
        {
            string temp = path + ".g3tmp";
            string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
            try {
                using (var w = new StreamWriter(temp, false, new System.Text.UTF8Encoding(false))) {
                    if (format == "csv") w.Write("vertex,x,y,z,mean,gaussian,mixed_area,angle_deficit,valid\n");
                    else w.Write("[");
                    bool first = true;
                    foreach (int v in mesh.VertexIndices()) {
                        Vector3d p = mesh.GetVertex(v);
                        if (format == "csv")
                            w.Write($"{v},{R(p.x)},{R(p.y)},{R(p.z)},{R(mc.Mean[v])},{R(mc.Gaussian[v])},{R(mc.MixedArea[v])},{R(mc.AngleDeficit[v])},{(mc.Valid[v] ? "true" : "false")}\n");
                        else {
                            var e = new JsonObject() { ["vertex"] = v, ["position"] = MeshReport.Vec(p), ["mean"] = Val(mc.Mean[v]), ["gaussian"] = Val(mc.Gaussian[v]),
                                ["mixed_area"] = Val(mc.MixedArea[v]), ["angle_deficit"] = Val(mc.AngleDeficit[v]), ["valid"] = mc.Valid[v] };
                            w.Write(first ? "\n" : ",\n");
                            w.Write(e.ToJsonString());
                            first = false;
                        }
                    }
                    if (format == "json") w.Write("\n]\n");
                }
                File.Move(temp, path, true);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                try { File.Delete(temp); } catch (Exception) { }
                throw new CommandException(ResultCodes.WriteError, $"cannot write '{path}': {e.Message}");
            }
        }
    }
}
