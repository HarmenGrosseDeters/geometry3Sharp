// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

namespace FilletDetection
{
    /// <summary> what the library has today: the dihedral crease test of the quad and tet code, and MeshPrincipalCurvature </summary>
    public static class Baselines
    {
        /// <summary> largest absolute principal curvature per triangle (mean of its vertices), from MeshPrincipalCurvature </summary>
        public static double[] TriangleCurvature(DMesh3 mesh)
        {
            MeshPrincipalCurvature pc = MeshPrincipalCurvature.Compute(mesh);
            var k = new double[mesh.MaxTriangleID];
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                double sum = 0;
                for (int j = 0; j < 3; ++j) sum += Math.Max(Math.Abs(pc.K1[t[j]]), Math.Abs(pc.K2[t[j]]));
                k[tid] = sum / 3;
            }
            return k;
        }

        /// <summary> curvature threshold a user could set: a triangle is a fillet if its curvature radius is at most maxRadius </summary>
        public static Detection Curvature(DMesh3 mesh, double maxRadius)
        {
            var sw = Stopwatch.StartNew();
            double[] k = TriangleCurvature(mesh);
            var d = new Detection(mesh.MaxTriangleID);
            foreach (int tid in mesh.TriangleIndices())
                if (k[tid] >= 1 / maxRadius) { d.Fillet[tid] = true; d.Radius[tid] = 1 / k[tid]; }
            d.Seconds = sw.Elapsed.TotalSeconds;
            return d;
        }

        /// <summary>
        /// The upper bound of any curvature band: the band [lo, hi] of the triangle curvature with the best F1 against the
        /// known answer (40 x 40 log-spaced bounds). No user can pick this band without the answer.
        /// </summary>
        public static Detection CurvatureOracle(Solid solid)
        {
            DMesh3 mesh = solid.Mesh;
            double[] k = TriangleCurvature(mesh);
            double min = double.MaxValue, max = 0;
            foreach (int tid in mesh.TriangleIndices()) if (k[tid] > 1e-9) { min = Math.Min(min, k[tid]); max = Math.Max(max, k[tid]); }
            const int steps = 40;
            var bounds = new double[steps + 1];
            for (int i = 0; i <= steps; ++i) bounds[i] = min * 0.999 * Math.Pow(max * 1.002 / min, (double)i / steps);
            // area per (bin, truth), bin = the last bound <= k
            var area = new double[steps + 1, 2];
            double truthArea = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                double a = mesh.GetTriArea(tid); bool truth = solid.IsFillet(tid);
                if (truth) truthArea += a;
                if (!(k[tid] > 1e-9)) continue;
                int bin = Math.Min(steps, (int)Math.Floor(Math.Log(k[tid] / bounds[0]) / Math.Log(bounds[steps] / bounds[0]) * steps));
                while (bin > 0 && bounds[bin] > k[tid]) --bin;
                while (bin < steps && bounds[bin + 1] <= k[tid]) ++bin;
                area[bin, truth ? 1 : 0] += a;
            }
            double bestF = -1; int bestLo = 0, bestHi = steps;
            for (int lo = 0; lo <= steps; ++lo) {
                double tp = 0, fp = 0;
                for (int hi = lo; hi <= steps; ++hi) {
                    tp += area[hi, 1]; fp += area[hi, 0];
                    double f = (tp > 0) ? 2 * tp / (2 * tp + fp + (truthArea - tp)) : 0;
                    if (f > bestF) { bestF = f; bestLo = lo; bestHi = hi; }
                }
            }
            var d = new Detection(mesh.MaxTriangleID);
            double hiBound = bestHi < steps ? bounds[bestHi + 1] : double.MaxValue;
            foreach (int tid in mesh.TriangleIndices())
                if (k[tid] >= bounds[bestLo] && k[tid] < hiBound) { d.Fillet[tid] = true; d.Radius[tid] = 1 / k[tid]; }
            d.Note = "band 1/k in [" + (1 / hiBound).ToString("0.###") + ", " + (1 / bounds[bestLo]).ToString("0.###") + "]";
            return d;
        }

        /// <summary>
        /// Length of the edges the crease test of the quad and tet code marks (dihedral angle above creaseDegrees, as
        /// MeshTopology does): on fillets (an edge with a fillet triangle) and elsewhere.
        /// </summary>
        public static void CreaseLengths(Solid solid, double creaseDegrees, out double onFillets, out double elsewhere)
        {
            DMesh3 mesh = solid.Mesh;
            onFillets = 0; elsewhere = 0;
            double cos = Math.Cos(creaseDegrees * MathUtil.Deg2Rad);
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                double dot = mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b));
                if (dot >= cos) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                double len = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                // a fold inside a fillet or to its tangent face; the rim between a fillet and a cap (90 degrees) is a design crease
                bool inside = (solid.IsFillet(et.a) && solid.IsFillet(et.b)) || ((solid.IsFillet(et.a) || solid.IsFillet(et.b)) && dot > Math.Cos(50 * MathUtil.Deg2Rad));
                if (inside) onFillets += len; else elsewhere += len;
            }
        }

        public static double CreaseLength(DMesh3 mesh, double creaseDegrees)
        {
            double total = 0, cos = Math.Cos(creaseDegrees * MathUtil.Deg2Rad);
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b)) >= cos) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                total += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
            }
            return total;
        }
    }
}
