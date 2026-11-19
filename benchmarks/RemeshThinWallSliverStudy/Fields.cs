// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Benchmark-only sizing fields: L(p) = min(target, c * sqrt(r(p) * t(p))) (chord sag L^2 / (8 r) <= t / 2 gives L <= 2 sqrt(r t)),
// with t(p) the local wall thickness and r(p) the local radius of curvature of the wall surface, plus the predicted triangle count
// of any field (integral of 1 / L^2 over the input surface).
using System;
using g3;
using gs;

static class Fields
{
    // thickness per input vertex as RemeshSizing.FromWallThickness (smallest of the incident triangles), interpolated over the
    // nearest triangle; curvature radius: analytic (rFunc) or estimated per vertex (MeshPrincipalCurvature, smoothingRounds)
    public class Probe
    {
        readonly DMesh3 copy; readonly DMeshAABBTree3 tree; readonly double[] vt, vk;
        readonly Func<Vector3d, double> rFunc;
        public double MinEdge;
        public Probe(DMesh3 input, Func<Vector3d, double> rAnalytic, int smoothingRounds)
        {
            copy = new DMesh3(input);
            rFunc = rAnalytic;
            MinEdge = 1e-3 * copy.CachedBounds.DiagonalLength;
            var wt = new MeshWallThickness(copy) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false };
            if (!wt.Compute()) throw new Exception("wall thickness failed");
            vt = new double[copy.MaxVertexID];
            for (int v = 0; v < vt.Length; ++v) vt[v] = double.NaN;
            foreach (int tid in copy.TriangleIndices()) {
                double t = wt.TriangleThickness[tid];
                if (!(t > 0) || double.IsInfinity(t)) continue;
                Index3i tv = copy.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) if (!(vt[tv[k]] <= t)) vt[tv[k]] = t;
            }
            vk = new double[copy.MaxVertexID];
            if (rAnalytic == null) {
                var pc = MeshPrincipalCurvature.Compute(copy, smoothingRounds);
                for (int v = 0; v < vk.Length; ++v)
                    vk[v] = copy.IsVertex(v) && pc.Valid[v] ? Math.Max(Math.Abs(pc.K1[v]), Math.Abs(pc.K2[v])) : double.NaN;
            }
            tree = new DMeshAABBTree3(copy, true);
        }

        // t and r at p (r = +inf where the wall is flat)
        public bool At(Vector3d p, out double t, out double r)
        {
            t = r = double.NaN;
            int tid = tree.FindNearestTriangle(p);
            if (tid == DMesh3.InvalidID) return false;
            Index3i tv = copy.GetTriangle(tid);
            var d = new DistPoint3Triangle3(p, new Triangle3d(copy.GetVertex(tv.a), copy.GetVertex(tv.b), copy.GetVertex(tv.c))).Compute();
            double st = 0, sk = 0, wt = 0, wk = 0;
            for (int k = 0; k < 3; ++k) {
                double w = d.TriangleBaryCoords[k];
                if (!(w > 0)) continue;
                if (!double.IsNaN(vt[tv[k]])) { st += w * vt[tv[k]]; wt += w; }
                if (rFunc == null && !double.IsNaN(vk[tv[k]])) { sk += w * vk[tv[k]]; wk += w; }
            }
            if (!(wt > 0)) {
                double m = double.NaN;
                for (int k = 0; k < 3; ++k) if (!double.IsNaN(vt[tv[k]]) && !(m <= vt[tv[k]])) m = vt[tv[k]];
                if (double.IsNaN(m)) return false;
                st = m; wt = 1;
            }
            t = st / wt;
            if (rFunc != null) r = rFunc(p);
            else if (wk > 0) { double kap = sk / wk; r = kap > 0 ? 1.0 / kap : double.PositiveInfinity; }
            else r = double.PositiveInfinity;
            return true;
        }

        // the field of the rule: min(target, c sqrt(max(r, rMinFactor t) t)), at least MinEdge
        public Func<Vector3d, double> Rule(double c, double target, double rMinFactor = 1.0)
        {
            return p => {
                if (!At(p, out double t, out double r)) return double.NaN;
                double rr = Math.Max(r, rMinFactor * t);
                double L = double.IsInfinity(rr) ? target : Math.Min(target, c * Math.Sqrt(rr * t));
                return Math.Max(L, MinEdge);
            };
        }
    }

    // predicted triangle count: sum over input triangles of area * 4 / (sqrt 3 L^2) (equilateral triangles of edge L), L = min(target, field),
    // sampled at 4 points per triangle (the centroid and three points towards the corners)
    public static double Predict(DMesh3 input, Func<Vector3d, double> field, double target)
    {
        double sum = 0;
        foreach (int tid in input.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            input.GetTriVertices(tid, ref a, ref b, ref c);
            double area = 0.5 * (b - a).Cross(c - a).Length;
            if (!(area > 0)) continue;
            Vector3d[] pts = { (a + b + c) / 3, 0.6 * a + 0.2 * b + 0.2 * c, 0.2 * a + 0.6 * b + 0.2 * c, 0.2 * a + 0.2 * b + 0.6 * c };
            double inv = 0;
            foreach (var p in pts) {
                double L = field == null ? target : field(p);
                if (double.IsNaN(L)) L = target;
                L = Math.Min(L, target);
                inv += 1.0 / (L * L);
            }
            sum += area * inv / pts.Length;
        }
        return sum * 4.0 / Math.Sqrt(3.0);
    }
}
