using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The canonical, order-independent fingerprints, exactly as specified in docs/design/MESHING2D_GOLDEN_FORMAT.md, section 3,
    /// so that any producer in any language can compute them. They hash coordinates, never vertex numbers.
    /// </summary>
    public static class Fingerprint
    {
        /// <summary>16 lowercase hex digits of the IEEE 754 bits, most significant first; -0 is written as +0.</summary>
        public static string Coordinate(double v)
        {
            if (v == 0) v = 0.0; // both zeros compare equal; this keeps +0 and turns -0 into +0
            return BitConverter.DoubleToInt64Bits(v).ToString("x16", CultureInfo.InvariantCulture);
        }

        public static string Vertex(double x, double y) => Coordinate(x) + "," + Coordinate(y);

        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create()) {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string Lines(IEnumerable<string> lines)
        {
            var sorted = lines.ToList();
            sorted.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            foreach (var line in sorted) sb.Append(line).Append('\n');
            return sb.ToString();
        }

        public static string TriangleLines(Mesh2Output o) => Lines(o.Triangles.Select(t => {
            string a = Vertex(o.VX[t[0]], o.VY[t[0]]), b = Vertex(o.VX[t[1]], o.VY[t[1]]), c = Vertex(o.VX[t[2]], o.VY[t[2]]);
            // keep the counter-clockwise order; start at the smallest token
            if (string.CompareOrdinal(b, a) < 0 && string.CompareOrdinal(b, c) < 0) return "T " + b + " " + c + " " + a;
            if (string.CompareOrdinal(c, a) < 0 && string.CompareOrdinal(c, b) < 0) return "T " + c + " " + a + " " + b;
            return "T " + a + " " + b + " " + c;
        }));

        public static string SubsegmentLines(Mesh2Output o)
        {
            if (o.Subsegments == null) return "";
            return Lines(o.Subsegments.Select(s => {
                string a = Vertex(o.VX[s.A], o.VY[s.A]), b = Vertex(o.VX[s.B], o.VY[s.B]);
                return string.CompareOrdinal(a, b) < 0 ? "S " + a + " " + b : "S " + b + " " + a;
            }).Distinct());
        }

        public static string VertexLines(Mesh2Output o) => Lines(o.Triangles.SelectMany(t => t).Distinct().Select(i => "V " + Vertex(o.VX[i], o.VY[i])));

        public static string Triangles(Mesh2Output o) => Sha256(TriangleLines(o));
        public static string Subsegments(Mesh2Output o) => Sha256(SubsegmentLines(o));
        public static string Vertices(Mesh2Output o) => Sha256(VertexLines(o));

        /// <summary>
        /// Our own producer only: the numbering and metadata, which pin Q8 (input i is vertex i), the mapping of merged inputs,
        /// the markers and the labels. Foreign producers leave it out.
        /// </summary>
        public static string Identity(Mesh2Output o)
        {
            var lines = new List<string>();
            if (o.InputToVertex != null)
                for (int i = 0; i < o.InputToVertex.Length; ++i) lines.Add("M " + i.ToString(CultureInfo.InvariantCulture) + " " + o.InputToVertex[i].ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < o.Triangles.Length; ++i) {
                var t = o.Triangles[i];
                int k = t[0] < t[1] && t[0] < t[2] ? 0 : t[1] < t[2] ? 1 : 2;
                string label = o.TriangleLabels == null ? "" : " " + o.TriangleLabels[i].ToString(CultureInfo.InvariantCulture);
                lines.Add("T " + t[k] + " " + t[(k + 1) % 3] + " " + t[(k + 2) % 3] + label);
            }
            if (o.Subsegments != null)
                foreach (var s in o.Subsegments) lines.Add("S " + Math.Min(s.A, s.B) + " " + Math.Max(s.A, s.B) + " " + s.Marker);
            return Sha256(Lines(lines));
        }
    }
}
