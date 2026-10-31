using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Meshing2Acceptance
{
    /// <summary>The plain text input and output formats of docs/design/MESHING2D_GOLDEN_FORMAT.md, sections 2 and 4.</summary>
    public static class TextFormat
    {
        static string Token(double v) => "0x" + BitConverter.DoubleToInt64Bits(v).ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>A coordinate: 0x plus 16 hex digits of the IEEE 754 bits, or a decimal number.</summary>
        public static double ParseCoordinate(string token)
        {
            if (token.StartsWith("0x", StringComparison.Ordinal)) {
                if (token.Length != 18) throw new FormatException("a bit pattern has 16 hex digits: " + token);
                return BitConverter.Int64BitsToDouble(long.Parse(token.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            }
            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>The canonical input text. Its SHA-256 is the row's input_sha256; no comments, no blank lines, signed zero kept.</summary>
        public static string WriteInput(Mesh2Input input)
        {
            var sb = new StringBuilder();
            sb.Append("mesh2-input 1\n");
            sb.Append("points ").Append(input.Count).Append('\n');
            for (int i = 0; i < input.Count; ++i) sb.Append(Token(input.X[i])).Append(' ').Append(Token(input.Y[i])).Append('\n');
            sb.Append("segments ").Append(input.Segments.Count).Append('\n');
            foreach (var s in input.Segments) sb.Append(s.A).Append(' ').Append(s.B).Append(' ').Append(s.Marker).Append(' ').Append(s.Locked ? 1 : 0).Append('\n');
            sb.Append("rings ").Append(input.Rings.Count).Append('\n');
            foreach (var r in input.Rings) sb.Append(r.Hole ? "hole " : "outer ").Append(r.Marker).Append(' ').Append(r.Vertices.Length).Append(' ').Append(string.Join(" ", r.Vertices)).Append('\n');
            sb.Append("holeseeds ").Append(input.HoleSeeds.Count).Append('\n');
            foreach (var h in input.HoleSeeds) sb.Append(Token(h[0])).Append(' ').Append(Token(h[1])).Append('\n');
            sb.Append("regions ").Append(input.Regions.Count).Append('\n');
            foreach (var r in input.Regions) {
                string area = r.MaxArea == null ? "-" : Token(r.MaxArea.Value);
                if (r.Ring != null) sb.Append("ring ").Append(r.Label).Append(' ').Append(area).Append(' ').Append(r.Ring.Length).Append(' ').Append(string.Join(" ", r.Ring)).Append('\n');
                else sb.Append("seed ").Append(r.Label).Append(' ').Append(area).Append(' ').Append(Token(r.Seed[0])).Append(' ').Append(Token(r.Seed[1])).Append('\n');
            }
            sb.Append("min_angle_deg ").Append(input.MinAngleDeg == null ? "-" : Token(input.MinAngleDeg.Value)).Append('\n');
            sb.Append("max_area ").Append(input.MaxArea == null ? "-" : Token(input.MaxArea.Value)).Append('\n');
            sb.Append("steiner_limit ").Append(input.SteinerLimit == null ? "-" : input.SteinerLimit.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("snap_tolerance ").Append(Token(input.SnapTolerance)).Append('\n');
            if (input.SizeFunction != null && input.SizeConstant == null) throw new InvalidOperationException("a size function that is no constant has no text form");
            sb.Append("size_constant ").Append(input.SizeConstant == null ? "-" : Token(input.SizeConstant.Value)).Append('\n');
            sb.Append("resolve_crossings ").Append(input.ResolveCrossings ? 1 : 0).Append('\n');
            sb.Append("conforming ").Append(input.Conforming ? 1 : 0).Append('\n');
            sb.Append("end\n");
            return sb.ToString();
        }

        public static string InputSha256(Mesh2Input input) => Fingerprint.Sha256(WriteInput(input));

        sealed class Reader
        {
            readonly string[] lines; int at;
            public Reader(string text)
            {
                lines = text.Split('\n').Select(l => { int h = l.IndexOf('#'); return (h >= 0 ? l.Substring(0, h) : l).Trim(); }).Where(l => l.Length > 0).ToArray();
            }
            public string[] Next()
            {
                if (at >= lines.Length) throw new FormatException("unexpected end of the file");
                return lines[at++].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            }
            public string[] Named(string name, int fields)
            {
                var f = Next();
                if (f[0] != name || f.Length != fields) throw new FormatException("expected \"" + name + "\" with " + (fields - 1) + " values, got: " + string.Join(" ", f));
                return f;
            }
            public int Count(string name)
            {
                var value = Int(Named(name, 2)[1]);
                if (value < 0) throw new FormatException(name + " is negative");
                return value;
            }
            public bool TryOptional(string name, out string[] fields)
            {
                fields = null;
                if (at >= lines.Length) return false;
                var f = lines[at].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f[0] != name) return false;
                ++at; fields = f; return true;
            }
        }

        static int Int(string token) => int.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        public static Mesh2Input ReadInput(string text)
        {
            var r = new Reader(text);
            var head = r.Named("mesh2-input", 2);
            if (head[1] != "1") throw new FormatException("unknown input format version " + head[1]);
            var input = new Mesh2Input();
            int n = r.Count("points");
            input.X = new double[n]; input.Y = new double[n];
            for (int i = 0; i < n; ++i) { var f = r.Next(); input.X[i] = ParseCoordinate(f[0]); input.Y[i] = ParseCoordinate(f[1]); }
            for (int i = 0, c = r.Count("segments"); i < c; ++i) {
                var f = r.Next();
                input.Segments.Add(new SegmentInput(Int(f[0]), Int(f[1]), Int(f[2]), f[3] == "1"));
            }
            for (int i = 0, c = r.Count("rings"); i < c; ++i) {
                var f = r.Next();
                int k = Int(f[2]);
                input.Rings.Add(new RingInput(f.Skip(3).Take(k).Select(Int).ToArray(), f[0] == "hole", Int(f[1])));
            }
            for (int i = 0, c = r.Count("holeseeds"); i < c; ++i) { var f = r.Next(); input.HoleSeeds.Add(new[] { ParseCoordinate(f[0]), ParseCoordinate(f[1]) }); }
            for (int i = 0, c = r.Count("regions"); i < c; ++i) {
                var f = r.Next();
                var region = new RegionInput { Label = Int(f[1]), MaxArea = f[2] == "-" ? (double?)null : ParseCoordinate(f[2]) };
                if (f[0] == "ring") region.Ring = f.Skip(4).Take(Int(f[3])).Select(Int).ToArray();
                else region.Seed = new[] { ParseCoordinate(f[3]), ParseCoordinate(f[4]) };
                input.Regions.Add(region);
            }
            var angle = r.Named("min_angle_deg", 2)[1]; input.MinAngleDeg = angle == "-" ? (double?)null : ParseCoordinate(angle);
            var area = r.Named("max_area", 2)[1]; input.MaxArea = area == "-" ? (double?)null : ParseCoordinate(area);
            var limit = r.Named("steiner_limit", 2)[1]; input.SteinerLimit = limit == "-" ? (int?)null : Int(limit);
            input.SnapTolerance = ParseCoordinate(r.Named("snap_tolerance", 2)[1]);
            var size = r.Named("size_constant", 2)[1];
            if (size != "-") {
                double h = ParseCoordinate(size);
                input.SizeConstant = h;
                input.SizeFunction = (x, y) => Q.FromDouble(h);
            }
            input.ResolveCrossings = r.Named("resolve_crossings", 2)[1] == "1";
            input.Conforming = r.Named("conforming", 2)[1] == "1";
            r.Named("end", 1);
            return input;
        }

        public static string WriteOutput(Mesh2Output o, string producer)
        {
            var sb = new StringBuilder();
            sb.Append("mesh2-output 1\n");
            sb.Append("producer ").Append(producer.Replace(' ', '_')).Append('\n');
            sb.Append("status ").Append(o.Status).Append('\n');
            sb.Append("vertices ").Append(o.VX.Length).Append('\n');
            for (int i = 0; i < o.VX.Length; ++i) sb.Append(Token(o.VX[i])).Append(' ').Append(Token(o.VY[i])).Append('\n');
            sb.Append("triangles ").Append(o.Triangles.Length).Append('\n');
            foreach (var t in o.Triangles) sb.Append(t[0]).Append(' ').Append(t[1]).Append(' ').Append(t[2]).Append('\n');
            if (o.Subsegments == null) sb.Append("subsegments -\n");
            else {
                sb.Append("subsegments ").Append(o.Subsegments.Count).Append('\n');
                foreach (var s in o.Subsegments) sb.Append(s.A).Append(' ').Append(s.B).Append(' ').Append(s.Marker).Append('\n');
            }
            sb.Append("map ").Append(o.InputToVertex == null ? "-" : string.Join(" ", new[] { o.InputToVertex.Length.ToString(CultureInfo.InvariantCulture) }.Concat(o.InputToVertex.Select(i => i.ToString(CultureInfo.InvariantCulture))))).Append('\n');
            sb.Append("labels ").Append(o.TriangleLabels == null ? "-" : string.Join(" ", new[] { o.TriangleLabels.Length.ToString(CultureInfo.InvariantCulture) }.Concat(o.TriangleLabels.Select(i => i.ToString(CultureInfo.InvariantCulture))))).Append('\n');
            sb.Append("bad ").Append(o.BadTriangles == null ? "-" : string.Join(" ", new[] { o.BadTriangles.Length.ToString(CultureInfo.InvariantCulture) }.Concat(o.BadTriangles.Select(i => i.ToString(CultureInfo.InvariantCulture))))).Append('\n');
            sb.Append("steiner_limit_reached ").Append(o.SteinerLimitReached ? 1 : 0).Append('\n');
            sb.Append("end\n");
            return sb.ToString();
        }

        /// <summary>A result in the text format. Without a map the numbering rules are not judged (a foreign producer).</summary>
        public static Mesh2Output ReadOutput(string text, out string producer)
        {
            var r = new Reader(text);
            var head = r.Named("mesh2-output", 2);
            if (head[1] != "1") throw new FormatException("unknown output format version " + head[1]);
            producer = r.Named("producer", 2)[1];
            var o = new Mesh2Output();
            if (!Enum.TryParse(r.Named("status", 2)[1], out o.Status)) throw new FormatException("unknown status");
            int n = r.Count("vertices");
            o.VX = new double[n]; o.VY = new double[n];
            for (int i = 0; i < n; ++i) { var f = r.Next(); o.VX[i] = ParseCoordinate(f[0]); o.VY[i] = ParseCoordinate(f[1]); }
            int t = r.Count("triangles");
            o.Triangles = new int[t][];
            for (int i = 0; i < t; ++i) { var f = r.Next(); o.Triangles[i] = new[] { Int(f[0]), Int(f[1]), Int(f[2]) }; }
            var sub = r.Named("subsegments", 2)[1];
            if (sub != "-") {
                o.Subsegments = new List<SubsegmentOutput>();
                for (int i = 0, c = Int(sub); i < c; ++i) { var f = r.Next(); o.Subsegments.Add(new SubsegmentOutput(Int(f[0]), Int(f[1]), f.Length > 2 ? Int(f[2]) : 0)); }
            }
            o.InputToVertex = List(r.Next(), "map");
            o.TriangleLabels = List(r.Next(), "labels");
            o.BadTriangles = List(r.Next(), "bad");
            o.SteinerLimitReached = r.Named("steiner_limit_reached", 2)[1] == "1";
            r.Named("end", 1);
            o.IdentityRules = o.InputToVertex != null;
            return o;
        }

        static int[] List(string[] fields, string name)
        {
            if (fields[0] != name) throw new FormatException("expected \"" + name + "\", got " + fields[0]);
            if (fields.Length == 2 && fields[1] == "-") return null;
            int count = Int(fields[1]);
            if (fields.Length != count + 2) throw new FormatException(name + ": the count does not match");
            return fields.Skip(2).Select(Int).ToArray();
        }
    }
}
