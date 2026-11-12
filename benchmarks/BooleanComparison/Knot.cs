// A procedural stand-in for the knot of Thingi10K 55928 (docs/design/EXACT_BOOLEAN_TAIL_55928.md): an open tube wound
// tightly inside a small ball like a ball of yarn, so that it cuts through itself everywhere. It is one component with
// two boundary loops (the open ends of the tube), and its triangles are small and point in every direction, with many
// self-intersecting pairs per triangle. B is the export's pairing: A rotated 20 degrees about (1, 1, 0) about its
// centre and shifted by a quarter of its extent. Deterministic for a seed; no mesh file is committed.
//   knot <case dir> [--segments s] [--ring m] [--turns t] [--tube r] [--seed k]
//     segments: rings along the tube (default 600); ring: vertices per ring (default 8); turns: how often the path
//     winds around the ball (default 12); tube: tube radius relative to the ball's (default 0.12)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using g3;

static class Knot
{
    public static int Run(string[] args)
    {
        string dir = args[0];
        int segments = 600, ring = 8, seed = 1;
        double turns = 12, tube = 0.12;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--segments") segments = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--ring") ring = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--turns") turns = double.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--tube") tube = double.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--seed") seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
        }
        DMesh3 a = Generate(segments, ring, turns, tube, seed);
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        Directory.CreateDirectory(dir);
        Program.WriteOFF(a, Path.Combine(dir, "A.off"));
        Program.WriteOFF(b, Path.Combine(dir, "B.off"));
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> {
            ["triangles"] = a.TriangleCount, ["vertices"] = a.VertexCount, ["segments"] = segments, ["ring"] = ring,
            ["turns"] = turns, ["tube"] = tube, ["seed"] = seed }));
        return 0;
    }

    /// <summary>
    /// the tube: rings of `ring` vertices around a path on the unit sphere (a spherical spiral with seeded wobble, `turns`
    /// windings from pole to pole and back), tube radius `tube`, frames by parallel transport; the two end rings stay open
    /// </summary>
    public static DMesh3 Generate(int segments, int ring, double turns, double tube, int seed)
    {
        var rand = new Random(seed);
        // wobble: a few sines of random frequency and phase on the two angles
        const int K = 4;
        var fa = new double[K]; var pa = new double[K]; var fb = new double[K]; var pb = new double[K];
        for (int k = 0; k < K; ++k) {
            fa[k] = 1 + 6 * rand.NextDouble(); pa[k] = 2 * Math.PI * rand.NextDouble();
            fb[k] = 1 + 6 * rand.NextDouble(); pb[k] = 2 * Math.PI * rand.NextDouble();
        }
        Func<double, Vector3d> path = (t) => {
            // t in [0, 1]: the polar angle goes pole to pole and back, the azimuth turns `turns` times
            double theta = Math.PI * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t)), phi = 2 * Math.PI * turns * t;
            for (int k = 0; k < K; ++k) {
                theta += 0.15 * Math.Sin(2 * Math.PI * fa[k] * t + pa[k]);
                phi += 0.4 * Math.Sin(2 * Math.PI * fb[k] * t + pb[k]);
            }
            double r = 1 + 0.1 * Math.Sin(2 * Math.PI * 5 * t);
            return r * new Vector3d(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta));
        };
        var centres = new Vector3d[segments + 1];
        for (int s = 0; s <= segments; ++s)
            centres[s] = path((double)s / segments);
        var m = new DMesh3();
        // frames by parallel transport of a normal along the tangents
        Vector3d tan0 = (centres[1] - centres[0]).Normalized;
        Vector3d nrm = Math.Abs(tan0.z) < 0.9 ? tan0.Cross(Vector3d.AxisZ).Normalized : tan0.Cross(Vector3d.AxisX).Normalized;
        for (int s = 0; s <= segments; ++s) {
            Vector3d tan = (centres[Math.Min(s + 1, segments)] - centres[Math.Max(s - 1, 0)]).Normalized;
            nrm = (nrm - nrm.Dot(tan) * tan).Normalized;
            Vector3d bin = tan.Cross(nrm);
            for (int j = 0; j < ring; ++j) {
                double ang = 2 * Math.PI * j / ring;
                m.AppendVertex(centres[s] + tube * (Math.Cos(ang) * nrm + Math.Sin(ang) * bin));
            }
        }
        for (int s = 0; s < segments; ++s)
            for (int j = 0; j < ring; ++j) {
                int a = s * ring + j, b = s * ring + (j + 1) % ring, c = a + ring, d = b + ring;
                m.AppendTriangle(a, b, d);
                m.AppendTriangle(a, d, c);
            }
        return m;
    }
}
