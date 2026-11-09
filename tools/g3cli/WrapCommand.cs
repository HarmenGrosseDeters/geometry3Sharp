// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    public static partial class Commands
    {
        // ---- wrap ----

        static double Positive(Args args, string name)
        {
            double v = args.Number(name);
            if (!(v > 0) || double.IsInfinity(v))
                throw new CommandException(ResultCodes.InvalidArguments, $"'{name}' must be a finite number > 0");
            return v;
        }

        static Command Wrap()
        {
            var c = new Command() {
                Name = "wrap",
                Description = "Alpha wrap (MeshAlphaWrap, Portaneri et al. 2022): a closed, 2-manifold surface that encloses the input " +
                    "(any mesh: a soup, open, self-intersecting, non-manifold) at a distance of about 'offset', following its detail down " +
                    "to about 'alpha' (the radius of the carving ball). Without alpha / offset the library defaults apply (bounding box diagonal / 20 " +
                    "and / 600). Result: wrap_status (success, or point_limit_reached: a valid wrap with less detail than requested, " +
                    "with a warning), used_alpha and used_offset (absolute, mesh units), the Steiner point counts and the metrics of the wrap. " +
                    "Other statuses fail with operation_failed and wrap_status; nothing is written then.",
            };
            c.Params.Add(Input());
            c.Params.Add(Output());
            c.Params.Add(new Param("alpha", ParamType.Number, "ball radius in mesh units (detail; smaller = more detail and triangles). Default: bounding box diagonal / relative_alpha"));
            c.Params.Add(new Param("relative_alpha", ParamType.Number, "bounding box diagonal divided by this gives alpha (library default 20)"));
            c.Params.Add(new Param("offset", ParamType.Number, "distance of the wrap from the input in mesh units. Default: bounding box diagonal / relative_offset"));
            c.Params.Add(new Param("relative_offset", ParamType.Number, "bounding box diagonal divided by this gives the offset (library default 600)"));
            c.Params.Add(new Param("max_steiner_points", ParamType.Integer, "stop refining after this many Steiner points (status point_limit_reached; library default 2000000)"));
            c.Params.Add(new Param("refine_non_manifold", ParamType.Boolean, "refine at the non-manifold spots before the manifold step (library default true)"));
            c.Params.Add(new Param("refine_min_angle", ParamType.Number, "refine result facets with a smaller minimum angle (degrees, 0 = off; library default 15)"));
            c.Params.Add(Deviation());
            c.Params.Add(SelfIntersections());
            c.Params.Add(Binary());
            c.Run = args => {
                MeshFiles.CheckWritable(args.String("output"));
                if (args.Has("alpha") && args.Has("relative_alpha"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give alpha or relative_alpha, not both");
                if (args.Has("offset") && args.Has("relative_offset"))
                    throw new CommandException(ResultCodes.InvalidArguments, "give offset or relative_offset, not both");
                var warnings = new List<string>();
                var wrapper = new MeshAlphaWrap((DMesh3)null);
                if (args.Has("alpha")) wrapper.Alpha = Positive(args, "alpha");
                if (args.Has("relative_alpha")) wrapper.RelativeAlpha = Positive(args, "relative_alpha");
                if (args.Has("offset")) wrapper.Offset = Positive(args, "offset");
                if (args.Has("relative_offset")) wrapper.RelativeOffset = Positive(args, "relative_offset");
                if (args.Has("max_steiner_points")) {
                    int n = args.Integer("max_steiner_points");
                    if (n < 1)
                        throw new CommandException(ResultCodes.InvalidArguments, "'max_steiner_points' must be >= 1");
                    wrapper.MaxSteinerPoints = n;
                }
                if (args.Has("refine_non_manifold")) wrapper.RefineNonManifold = args.Bool("refine_non_manifold");
                if (args.Has("refine_min_angle")) {
                    double a = args.Number("refine_min_angle");
                    if (!(a >= 0 && a < 60))
                        throw new CommandException(ResultCodes.InvalidArguments, "'refine_min_angle' must be in [0, 60) degrees");
                    wrapper.RefineMinAngleDegrees = a;
                }
                DMesh3 input = MeshFiles.Read(args.String("input"), warnings);
                wrapper.Mesh = input;
                bool ok = wrapper.Compute();
                string status = ToSnake(wrapper.Status.ToString());
                if (wrapper.Status == MeshAlphaWrap.WrapStatus.InvalidParameters)
                    throw new CommandException(ResultCodes.InvalidArguments, "alpha wrap: invalid parameters" + (wrapper.Error != null ? ": " + wrapper.Error : ""),
                        new JsonObject() { ["wrap_status"] = status });
                bool usable = wrapper.Status == MeshAlphaWrap.WrapStatus.Success || wrapper.Status == MeshAlphaWrap.WrapStatus.PointLimitReached;
                if (usable == false || wrapper.Result == null || wrapper.Result.TriangleCount == 0)
                    throw new CommandException(ResultCodes.OperationFailed, "alpha wrap failed: " + status + (wrapper.Error != null ? ": " + wrapper.Error : ""),
                        new JsonObject() { ["wrap_status"] = status });
                if (wrapper.Status == MeshAlphaWrap.WrapStatus.PointLimitReached)
                    warnings.Add("max_steiner_points reached: the wrap is closed, manifold and encloses the input, but has less detail than requested");
                MeshFiles.Write(args.String("output"), wrapper.Result, args.Bool("binary"));

                var o = new JsonObject() { ["input"] = args.String("input"), ["output"] = args.String("output") };
                o["wrap_status"] = status;
                o["used_alpha"] = Val(wrapper.UsedAlpha);
                o["used_offset"] = Val(wrapper.UsedOffset);
                var counts = new JsonObject();
                foreach (var kv in wrapper.Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) counts[kv.Key] = kv.Value;
                o["counts"] = counts;
                var phases = new JsonObject();
                foreach (var kv in wrapper.PhaseMilliseconds.OrderBy(k => k.Key, StringComparer.Ordinal)) phases[kv.Key] = kv.Value;
                o["phase_ms"] = phases;
                o["input_mesh"] = MeshReport.Metrics(input, false);
                o["mesh"] = MeshReport.Metrics(wrapper.Result, args.Bool("self_intersections"));
                if (args.Bool("deviation"))
                    o["hausdorff"] = Hausdorff(input, wrapper.Result);
                o["warnings"] = Warnings(warnings);
                return o;
            };
            return c;
        }
    }
}
