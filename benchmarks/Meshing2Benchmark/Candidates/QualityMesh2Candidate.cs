using System;
using System.Globalization;
using g3;

namespace Meshing2Acceptance
{
    /// <summary> The production M3 mesher, <see cref="QualityMesh2"/>, as seen by the gates. </summary>
    public sealed class QualityMesh2Candidate : IMeshing2Candidate
    {
        public string Name => "g3-quality";

        /// <summary> benchmarks only (per thread): conforming on or off regardless of the input, to measure what O2 costs; null: as the spec says </summary>
        [System.ThreadStatic] public static bool? ConformingForBenchmarks;

        public Mesh2Output Run(Mesh2Input input)
        {
            var mesher = new QualityMesh2 {
                MinAngle = input.MinAngleDeg ?? 0, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit,
                // a minimum angle given as 0 still asks for quality, and quality is conforming (O2)
                ResolveCrossings = input.ResolveCrossings, SnapTolerance = input.SnapTolerance,
                Conforming = ConformingForBenchmarks ?? (!input.ConstrainedQuality && (input.Conforming || input.MinAngleDeg != null || input.MaxArea != null || input.SizeFunction != null || input.TriangleAreas.Count > 0))
            };
            if (input.SizeConstant != null) { double h = input.SizeConstant.Value; mesher.SizeFunction = (x, y) => h; }
            else if (input.SizeFunction != null) {
                var f = input.SizeFunction;
                // the rational size truncated toward zero: a little stricter than asked
                mesher.SizeFunction = (x, y) => double.Parse(f(Q.FromDouble(x), Q.FromDouble(y)).ToDecimal(17), CultureInfo.InvariantCulture);
            }
            ConstrainedTriangulation2Result result;
            if (input.TriangleAreas.Count > 0) {
                var areaByTriple = new System.Collections.Generic.Dictionary<(int, int, int), double>();
                foreach (var ta in input.TriangleAreas) {
                    var triple = SortTriple(ta.A, ta.B, ta.C);
                    if (!areaByTriple.TryGetValue(triple, out double cur) || ta.MaxArea < cur) areaByTriple[triple] = ta.MaxArea;
                }
                if (input.Segments.Count == 0 && input.Rings.Count == 0 && input.HoleSeeds.Count == 0 && input.Regions.Count == 0) {
                    var pts = new Vector2d[input.Count];
                    for (int i = 0; i < input.Count; ++i) pts[i] = new Vector2d(input.X[i], input.Y[i]);
                    var initial = new DelaunayMesh2().Compute(pts);
                    var perTri = new double?[initial.Mesh.TriangleCount];
                    for (int t = 0; t < perTri.Length; ++t) {
                        var tri = initial.Mesh.Triangles[t];
                        if (areaByTriple.TryGetValue(SortTriple(tri.a, tri.b, tri.c), out double maxA)) perTri[t] = maxA;
                    }
                    result = mesher.Refine(initial.Mesh, perTri);
                } else {
                    var initial = new ConstrainedDelaunayMesh2 { ResolveCrossings = input.ResolveCrossings }.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
                    if (initial.Mesh == null) {
                        result = initial;
                    } else {
                        var perTri = new double?[initial.Mesh.TriangleCount];
                        for (int t = 0; t < perTri.Length; ++t) {
                            var tri = initial.Mesh.Triangles[t];
                            if (areaByTriple.TryGetValue(SortTriple(tri.a, tri.b, tri.c), out double maxA)) perTri[t] = maxA;
                        }
                        result = mesher.Refine(initial.Mesh, perTri);
                    }
                }
            } else {
                result = mesher.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            }
            var output = ConstrainedDelaunayMesh2Candidate.Output(result);
            if (result.Mesh != null) {
                output.BadTriangles = new int[result.Mesh.BadTriangles.Count];
                for (int i = 0; i < output.BadTriangles.Length; ++i) output.BadTriangles[i] = result.Mesh.BadTriangles[i];
                output.SteinerLimitReached = result.Mesh.SteinerLimitReached;
            }
            return output;
        }

        static (int, int, int) SortTriple(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return (a, b, c);
        }
    }
}
