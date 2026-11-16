// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

/// <summary>
/// UVAtlasGenerator on one mesh (Thingi10K, see fetch_data.py), one JSON line: status, time, components flattened
/// with cones against chart segmentation, charts per method, coverage, seam length, overlaps, distortion.
/// Usage: dotnet UVAtlasBenchmark.dll mesh [auto|charts|cones|single]
///        dotnet UVAtlasBenchmark.dll to-obj mesh out.obj      input for an external atlas tool
///        dotnet UVAtlasBenchmark.dll atlas mesh out.obj       UVAtlasGenerator (Auto, defaults), atlas as OBJ + time
///        dotnet UVAtlasBenchmark.dll evaluate mesh uv.obj|- --tool name --ms ms --status s   (same metrics for every tool)
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "to-obj") return ToObj(args);
        if (args.Length > 0 && args[0] == "atlas") return Atlas(args);
        if (args.Length > 0 && args[0] == "evaluate") return Evaluate(args);
        var r = new Dictionary<string, object>();
        r["file"] = Path.GetFileName(args[0]);
        string method = args.Length > 1 ? args[1] : "auto";
        r["method"] = method;
        try {
            DMesh3 mesh = LoadMesh(args[0], out string loadError);
            if (mesh == null) {
                r["status"] = "load_failed"; r["error"] = loadError;
                Emit(r); return 0;
            }
            r["faces"] = mesh.TriangleCount;
            r["verts"] = mesh.VertexCount;
            double area = 0;
            foreach (int tid in mesh.TriangleIndices())
                area += mesh.GetTriArea(tid);
            r["area"] = area;

            var gen = new UVAtlasGenerator(mesh);
            gen.Method = method == "charts" ? UVAtlasGenerator.Methods.Charts : method == "cones" ? UVAtlasGenerator.Methods.Cones
                : method == "single" ? UVAtlasGenerator.Methods.SingleChart : UVAtlasGenerator.Methods.Auto;
            var sw = Stopwatch.StartNew();
            gen.Compute();
            r["ms"] = sw.Elapsed.TotalMilliseconds;
            r["status"] = gen.Status.ToString();
            // Error can keep the message of a path that a fallback then replaced: only reported on failure
            if (gen.Status != UVAtlasStatus.Success)
                r["error"] = gen.Error;
            r["components"] = gen.ComponentCount;
            r["cone_components"] = gen.ConeComponents;
            r["cones"] = gen.ConeCount;
            r["charts"] = gen.ChartCount;
            r["charts_over_limit"] = gen.ChartsOverLimit;
            var byMethod = new Dictionary<string, int>();
            var trisByMethod = new Dictionary<string, int>();
            foreach (var c in gen.Charts) {
                string m = c.Method ?? "none";
                byMethod[m] = byMethod.TryGetValue(m, out int n) ? n + 1 : 1;
                trisByMethod[m] = trisByMethod.TryGetValue(m, out int t) ? t + c.Triangles : c.Triangles;
            }
            r["chart_methods"] = byMethod;
            r["chart_method_triangles"] = trisByMethod;
            r["coverage"] = gen.Coverage;
            r["seam_rel"] = area > 0 ? gen.SeamLength / Math.Sqrt(area) : 0;
            if (gen.Overlap != null) {
                r["overlap_pairs"] = gen.Overlap.OverlappingPairs;
                r["inverted"] = gen.Overlap.InvertedTriangles;
                r["degenerate_uv"] = gen.Overlap.DegenerateTriangles;
            }
            r["collapsed"] = gen.CollapsedTriangleCount;
            r["float_defects"] = gen.FloatRoundingDefects;
            var d = gen.Distortion;
            if (d != null) {
                r["mean_conformal"] = d.MeanConformal;
                r["max_conformal"] = d.MaxConformal;
                r["mean_area"] = d.MeanAreaDistortion;
                r["max_area"] = d.MaxAreaDistortion;
                r["mean_angle_deg"] = d.MeanAngleError * 180 / Math.PI;
            }
            if (gen.AtlasMesh != null && gen.UV != null) {
                // triangle quality (Verdict metrics) in 3D and of the UV triangles, and the topology of the atlas
                // mesh: every chart should be a disc (genus 0, one boundary loop)
                gen.AtlasMesh.Let(m => r["q3d"] = FidelityFields.Quality(gs.TriangleQualityStatistics.Compute(m)));
                var flat = new DMesh3(gen.AtlasMesh, false, MeshComponents.None);
                foreach (int vid in flat.VertexIndices())
                    flat.SetVertex(vid, new Vector3d(gen.UV[vid].x, gen.UV[vid].y, 0));
                r["quv"] = FidelityFields.Quality(gs.TriangleQualityStatistics.Compute(flat));
                r["atlas_topo"] = FidelityFields.Topology(gs.MeshTopologySummary.Compute(gen.AtlasMesh));
            }
        } catch (Exception e) {
            r["status"] = "exception";
            r["error"] = e.GetType().Name + ": " + e.Message;
        }
        Emit(r);
        return 0;
    }

    static void Let<T>(this T t, Action<T> f) => f(t);

    /// <summary> the mesh of a file (all its meshes appended), or null with the reader's message </summary>
    static DMesh3 LoadMesh(string path, out string error)
    {
        error = null;
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(path, new ReadOptions(), builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) {
            error = rr.message;
            return null;
        }
        DMesh3 mesh = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i)
            MeshEditor.Append(mesh, builder.Meshes[i]);
        return mesh;
    }

    /// <summary>
    /// to-obj mesh out.obj: the input as OBJ (vertices in id order, faces in triangle id order), the input of xatlas
    /// and the reference of evaluate.
    /// </summary>
    static int ToObj(string[] args)
    {
        DMesh3 mesh = LoadMesh(args[1], out string error);
        if (mesh == null) {
            Console.Error.WriteLine("load failed: " + error);
            return 1;
        }
        UvEvaluation.WriteInput(args[2], mesh);
        return 0;
    }

    /// <summary>
    /// atlas mesh out.obj: UVAtlasGenerator (Method = Auto, defaults) on the mesh, the atlas as OBJ in the layout of
    /// UvEvaluation (face i = input triangle i), one JSON line with the status and the time of Compute.
    /// </summary>
    static int Atlas(string[] args)
    {
        var r = new Dictionary<string, object>();
        r["file"] = Path.GetFileName(args[1]);
        r["tool"] = "UVAtlasGenerator";
        try {
            DMesh3 mesh = LoadMesh(args[1], out string error);
            if (mesh == null) {
                r["status"] = "load_failed"; r["error"] = error;
                Emit(r); return 0;
            }
            var gen = new UVAtlasGenerator(mesh);
            var sw = Stopwatch.StartNew();
            gen.Compute();
            r["ms"] = sw.Elapsed.TotalMilliseconds;
            r["status"] = gen.Status.ToString();
            if (gen.Status != UVAtlasStatus.Success)
                r["error"] = gen.Error;
            if (gen.AtlasMesh != null && gen.UV != null && gen.AtlasToInputTriangle != null) {
                UvEvaluation.WriteAtlas(args[2], UvEvaluation.FromUVAtlas(mesh, gen));
                r["written"] = gen.AtlasToInputTriangle.Length;
            }
        } catch (Exception e) {
            r["status"] = "exception";
            r["error"] = e.GetType().Name + ": " + e.Message;
        }
        Emit(r);
        return 0;
    }

    /// <summary>
    /// evaluate mesh uv.obj|- --tool name --ms ms --status producer_status: the evaluation of an atlas (UvEvaluation),
    /// the same code for every tool. "-" (no atlas written) gives status no_output. One JSON line.
    /// </summary>
    static int Evaluate(string[] args)
    {
        var r = new Dictionary<string, object>();
        r["file"] = Path.GetFileName(args[1]);
        string uvPath = args[2];
        for (int i = 3; i + 1 < args.Length; i += 2) {
            switch (args[i]) {
                case "--tool": r["tool"] = args[i + 1]; break;
                case "--ms": r["ms"] = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--status": r["producer_status"] = args[i + 1]; break;
            }
        }
        try {
            if (uvPath == "-" || !File.Exists(uvPath)) {
                r["status"] = "no_output";
            } else {
                DMesh3 mesh = LoadMesh(args[1], out string error);
                if (mesh == null) {
                    r["status"] = "load_failed"; r["error"] = error;
                } else {
                    UvEvaluation.Atlas atlas;
                    try {
                        atlas = UvEvaluation.ReadObj(uvPath);
                    } catch (FormatException e) {
                        r["status"] = "bad_output"; r["error"] = e.Message;
                        Emit(r); return 0;
                    }
                    foreach (var kv in UvEvaluation.Evaluate(mesh, atlas))
                        r[kv.Key] = kv.Value;
                }
            }
        } catch (Exception e) {
            r["status"] = "exception";
            r["error"] = e.GetType().Name + ": " + e.Message;
        }
        Emit(r);
        return 0;
    }

    static void Emit(object o) => Console.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}
