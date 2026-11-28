// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Stage 1b time gate: the library's internal RestrictedDelaunayRefinement (exact clipping, symbolic perturbation) on the
// spike's inputs. `exact [only=<regex>] [check]`; one JSON line per case. check: compare the incremental cells with a full
// exact recompute after every insertion (slow).
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

namespace RestrictedDelaunaySpike
{
    static class ExactMode
    {
        static string F(double x) => x.ToString("G4", CultureInfo.InvariantCulture);

        public static void Run(DMesh3 input, string group, string name, double rmaxFrac, Action<DMesh3> rotate, bool check)
        {
            rotate(input);
            double rmax = rmaxFrac * input.CachedBounds.Diagonal.Length;
            var rdr = new RestrictedDelaunayRefinement { RMax = rmax, CheckEachInsertion = check };
            var sw = Stopwatch.StartNew();
            rdr.Compute(input);
            double sec = sw.Elapsed.TotalSeconds;
            DMesh3 D = rdr.Output;
            // the Delaunay share: the same vertices inserted again in the same order, perturbed and plain
            double dtPert = 0, dtPlain = 0;
            if (rdr.Diagram != null) {
                var tri = rdr.Diagram.Triangulation;
                foreach (bool pert in new[] { true, false }) {
                    var sw2 = Stopwatch.StartNew();
                    var d2 = new Delaunay3(tri.Box) { SymbolicPerturbation = pert };
                    for (int v = 8; v < tri.VertexCount; ++v) d2.Insert(tri.GetVertex(v), -1, 0, out _);
                    if (pert) dtPert = sw2.Elapsed.TotalSeconds; else dtPlain = sw2.Elapsed.TotalSeconds;
                }
            }
            string notClean = D.TriangleCount > 0 ? MeshRobustRepair.NotCleanReason(D) ?? "" : "empty";
            int comps(DMesh3 m) { var c = new MeshConnectedComponents(m); c.FindConnectedT(); return c.Count; }
            int genus(DMesh3 m) => (2 - (m.VertexCount - m.EdgeCount + m.TriangleCount)) / 2;
            bool closed = D.TriangleCount > 0 && D.IsClosed();
            bool homeo = closed && comps(D) == comps(input) && genus(D) == genus(input);
            double hd = double.NaN;
            if (D.TriangleCount > 0) { var h = new MeshHausdorffDistance(input, D) { Tolerance = 0.01 * rmax }; h.Compute(); hd = h.UpperBound / rmax; }
            double amin = D.TriangleCount == 0 ? double.NaN : D.TriangleIndices().Min(t => {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; D.GetTriVertices(t, ref a, ref b, ref c);
                double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
                return Math.Min(A, Math.Min(B, 180 - A - B)); });
            Console.WriteLine($"{{\"group\":\"{group}\",\"case\":\"{name}\",\"status\":\"{rdr.Status}\",\"seconds\":{F(sec)},\"out_vertices\":{D.VertexCount},\"us_per_vertex\":{F(1e6 * sec / Math.Max(1, D.VertexCount))},\"delaunay_perturbed_us\":{F(1e6 * dtPert / Math.Max(1, D.VertexCount))},\"delaunay_plain_us\":{F(1e6 * dtPlain / Math.Max(1, D.VertexCount))}," +
                $"\"inserted\":{rdr.Inserted},\"refused\":{rdr.Refused},\"restricted\":{rdr.RestrictedFacets},\"bad_edges\":{rdr.BadEdges},\"bad_arcs\":{rdr.BadArcs},\"bad_cells\":{rdr.BadCells},\"corner_cells\":{rdr.CornerCells}," +
                $"\"far_balls\":{rdr.FarBalls},\"far_cells\":{rdr.FarCells},\"exempt\":{rdr.ExemptFacets},\"closed\":{(closed ? "true" : "false")},\"homeomorphic\":{(homeo ? "true" : "false")},\"not_clean\":\"{notClean}\"," +
                $"\"hausdorff_over_rmax\":{F(hd)},\"min_angle\":{F(amin)},\"side2_calls\":{rdr.Diagram?.Side2Calls ?? 0},\"side3_calls\":{rdr.Diagram?.Side3Calls ?? 0},\"checked\":{rdr.CheckedInsertions},\"check_mismatches\":{rdr.CheckMismatches}}}");
        }
    }
}
