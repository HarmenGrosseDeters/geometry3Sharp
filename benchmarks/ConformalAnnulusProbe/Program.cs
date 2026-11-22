using System;
using gs;
using System.Collections.Generic;
using g3;

foreach (var (sides, bands, dz, jitter) in new[] { (8, 3, 0.6, 0.0), (8, 3, 0.6, 1e-5), (48, 12, 0.3, 0.0), (48, 12, 0.3, 1e-5) }) {
    var mesh = new DMesh3();
    var uv = new List<Vector2d>();
    var radius = new List<double>();
    double q = Math.Exp(-2 * Math.Asinh(dz / 2));
    for (int j = 0; j <= bands; j++) {
        double r = Math.Pow(q, j);
        for (int i = 0; i < sides; i++) {
            double angle = 2 * Math.PI * i / sides;
            if (i == 0) angle += jitter;
            var p = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            mesh.AppendVertex(new Vector3d(p.x, p.y, j * dz));
            uv.Add(r * p);
            radius.Add(r);
        }
    }
    for (int j = 0; j < bands; j++)
        for (int i = 0; i < sides; i++) {
            int a = j * sides + i, b = j * sides + (i + 1) % sides;
            mesh.AppendTriangle(a, b, b + sides);
            mesh.AppendTriangle(a, b + sides, a + sides);
        }
    var intrinsic = new IntrinsicTriangulation(mesh, 0);
    int strictFailures = 0;
    double residual = 0;
    for (int h = 0; h < intrinsic.HalfedgeCount; h++) {
        int a = intrinsic.VertexID(intrinsic.Tail(h)), b = intrinsic.VertexID(intrinsic.Head(h));
        if (a >= b && intrinsic.Twin(h) >= 0) continue;
        if (!intrinsic.IsDelaunay(h, 0)) strictFailures++;
        double expected = intrinsic.Length(h) * Math.Sqrt(radius[a] * radius[b]);
        residual = Math.Max(residual, Math.Abs(uv[a].Distance(uv[b]) / expected - 1));
    }
    int flips = intrinsic.FlipToDelaunay();
    var baseline = new LSCMParameterization(mesh).Compute();
    var d = ParameterizationDistortion.Compute(mesh, uv);
    Console.WriteLine($"{sides}x{bands} jitter={jitter} LSCM={baseline.Status} bad={baseline.Distortion.FlippedTriangles + baseline.Distortion.DegenerateUVTriangles} scaledBad={d.FlippedTriangles + d.DegenerateUVTriangles} maxRel={residual:G4} strictFailures={strictFailures} flips={flips}");
}
