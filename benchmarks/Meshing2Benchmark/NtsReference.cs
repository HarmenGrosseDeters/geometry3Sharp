using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Meshing2Acceptance
{
    /// <summary>
    /// NetTopologySuite (BSD-3-Clause) as a black box: the reference of the M1 differential. A reference is not truth. Its output
    /// goes through the same oracle, and a difference needs a reason; either side may be at fault. Only this benchmark project
    /// depends on it, never the library.
    /// </summary>
    public sealed class NtsReference : IMeshing2Candidate
    {
        public string Name => "nts-" + typeof(DelaunayTriangulationBuilder).Assembly.GetName().Version;

        public Mesh2Output Run(Mesh2Input input)
        {
            var sites = new List<Coordinate>(input.Count);
            for (int i = 0; i < input.Count; ++i) sites.Add(new Coordinate(input.X[i] == 0 ? 0.0 : input.X[i], input.Y[i] == 0 ? 0.0 : input.Y[i]));
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(sites);
            var factory = new GeometryFactory(new PrecisionModel(PrecisionModels.Floating));
            var triangles = builder.GetTriangles(factory);

            var vx = new List<double>(); var vy = new List<double>();
            var index = new Dictionary<(double, double), int>();
            int Id(Coordinate c)
            {
                var key = (c.X == 0 ? 0.0 : c.X, c.Y == 0 ? 0.0 : c.Y);
                if (!index.TryGetValue(key, out int id)) { id = vx.Count; index[key] = id; vx.Add(key.Item1); vy.Add(key.Item2); }
                return id;
            }

            var result = new List<int[]>();
            for (int i = 0; i < triangles.NumGeometries; ++i) {
                var ring = ((Polygon)triangles.GetGeometryN(i)).ExteriorRing.Coordinates;
                result.Add(new[] { Id(ring[0]), Id(ring[1]), Id(ring[2]) });
            }
            // NTS gives no orientation promise: make each triangle counter-clockwise by the oracle's exact sign; a flat one stays and is judged
            foreach (var t in result) {
                Pt a = Pt.FromDoubles(vx[t[0]], vy[t[0]]), b = Pt.FromDoubles(vx[t[1]], vy[t[1]]), c = Pt.FromDoubles(vx[t[2]], vy[t[2]]);
                if (Exact.Orient(a, b, c) < 0) { int s = t[1]; t[1] = t[2]; t[2] = s; }
            }
            return new Mesh2Output {
                Status = result.Count == 0 ? Mesh2Status.Empty : Mesh2Status.Success,
                VX = vx.ToArray(), VY = vy.ToArray(), Triangles = result.ToArray(), IdentityRules = false
            };
        }
    }
}
