using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

// Twisting a closed bar (length 4, radius 0.35, 24 x 49 rings and two cap centres, 1178 vertices): the bottom 4 rings and
// the bottom cap centre are fixed, the top 4 rings and the top cap centre rotate about the bar axis.
//   usage: ArapIncrementalTwist [stepDegrees = 10] [maxDegrees = 180] [budget = 1000]
// Prints, per angle, an incremental solve (one Prepare, each step started from the previous result) and a cold solve of the
// same angle from the rest pose. midR is the smallest radius of the middle ring about its own centre (0.35 is the round bar;
// a smaller value means the bar necks).
class Program
{
    static int Main(string[] args)
    {
        double step = args.Length > 0 ? double.Parse(args[0]) : 10, max = args.Length > 1 ? double.Parse(args[1]) : 180;
        int budget = args.Length > 2 ? int.Parse(args[2]) : 1000;
        const int around = 24, rings = 49; const double length = 4, radius = 0.35;
        var mesh = new DMesh3(MeshComponents.None);
        for (int k = 0; k < rings; ++k)
            for (int i = 0; i < around; ++i) {
                double t = 2 * Math.PI * i / around;
                mesh.AppendVertex(new Vector3d(radius * Math.Cos(t), radius * Math.Sin(t), length * k / (rings - 1)));
            }
        int bottomCap = mesh.AppendVertex(new Vector3d(0, 0, 0)), topCap = mesh.AppendVertex(new Vector3d(0, 0, length));
        for (int k = 0; k + 1 < rings; ++k)
            for (int i = 0; i < around; ++i) {
                int i2 = (i + 1) % around, p = k * around + i, q = k * around + i2, u = (k + 1) * around + i, v = (k + 1) * around + i2;
                mesh.AppendTriangle(p, q, v); mesh.AppendTriangle(p, v, u);
            }
        for (int i = 0; i < around; ++i) {
            int i2 = (i + 1) % around, top = (rings - 1) * around;
            mesh.AppendTriangle(bottomCap, i2, i); mesh.AppendTriangle(topCap, top + i, top + i2);
        }
        Dictionary<int, Vector3d> Handles(double degrees)
        {
            var rotation = Matrix3d.AxisAngleD(Vector3d.AxisZ, degrees);
            var h = new Dictionary<int, Vector3d>();
            for (int k = 0; k < 4; ++k)
                for (int i = 0; i < around; ++i) {
                    h[k * around + i] = mesh.GetVertex(k * around + i);
                    int t = (rings - 1 - k) * around + i; h[t] = rotation * mesh.GetVertex(t);
                }
            h[bottomCap] = mesh.GetVertex(bottomCap); h[topCap] = rotation * mesh.GetVertex(topCap);
            return h;
        }
        double MidRadius(DMesh3 m)
        {
            int mid = (rings / 2) * around; double cx = 0, cy = 0;
            for (int i = 0; i < around; ++i) { var v = m.GetVertex(mid + i); cx += v.x; cy += v.y; }
            cx /= around; cy /= around; double min = double.MaxValue;
            for (int i = 0; i < around; ++i) { var v = m.GetVertex(mid + i); min = Math.Min(min, Math.Sqrt((v.x - cx) * (v.x - cx) + (v.y - cy) * (v.y - cy))); }
            return min;
        }
        var solver = new ArapMeshDeformer(mesh); solver.Options.MaxIterations = budget;
        var preparation = solver.Prepare(Handles(step).Keys);
        Console.WriteLine($"vertices={mesh.VertexCount} step={step} budget={budget} prepare_status={preparation.Status}");
        Console.WriteLine("degrees | incremental: status iterations energy midR | cold: status iterations energy midR");
        Dictionary<int, Vector3d> previous = null; long incrementalIterations = 0; int bad = 0;
        for (double degrees = step; degrees <= max + 1e-9; degrees += step) {
            var handles = Handles(degrees);
            var inc = solver.Solve(preparation, handles, previous);
            var cold = new ArapMeshDeformer(mesh) { Options = { MaxIterations = budget } }.Compute(handles);
            if (inc.Mesh == null || cold.Mesh == null) { ++bad; Console.WriteLine($"{degrees,5} failed {inc.Status} {cold.Status}"); continue; }
            incrementalIterations += inc.Iterations;
            previous = new Dictionary<int, Vector3d>();
            foreach (int v in inc.Mesh.VertexIndices()) previous[v] = inc.Mesh.GetVertex(v);
            Console.WriteLine($"{degrees,5} | {inc.Status,-13} {inc.Iterations,5} {inc.Energy:G6} {MidRadius(inc.Mesh):F3} | {cold.Status,-13} {cold.Iterations,5} {cold.Energy:G6} {MidRadius(cold.Mesh):F3}");
        }
        Console.WriteLine($"total incremental iterations={incrementalIterations}");
        return bad == 0 ? 0 : 1;
    }
}
