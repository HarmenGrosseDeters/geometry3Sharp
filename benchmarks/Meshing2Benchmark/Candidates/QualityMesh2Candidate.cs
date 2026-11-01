using System;
using System.Globalization;
using g3;

namespace Meshing2Acceptance
{
    /// <summary> The production M3 mesher, <see cref="QualityMesh2"/>, as seen by the gates. </summary>
    public sealed class QualityMesh2Candidate : IMeshing2Candidate
    {
        public string Name => "g3-quality";

        public Mesh2Output Run(Mesh2Input input)
        {
            var mesher = new QualityMesh2 {
                MinAngle = input.MinAngleDeg ?? 0, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit,
                // a minimum angle given as 0 still asks for quality, and quality is conforming (O2)
                ResolveCrossings = input.ResolveCrossings, Conforming = input.Conforming || input.MinAngleDeg != null || input.MaxArea != null || input.SizeFunction != null
            };
            if (input.SizeConstant != null) { double h = input.SizeConstant.Value; mesher.SizeFunction = (x, y) => h; }
            else if (input.SizeFunction != null) {
                var f = input.SizeFunction;
                // the rational size truncated toward zero: a little stricter than asked
                mesher.SizeFunction = (x, y) => double.Parse(f(Q.FromDouble(x), Q.FromDouble(y)).ToDecimal(17), CultureInfo.InvariantCulture);
            }
            var result = mesher.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            var output = ConstrainedDelaunayMesh2Candidate.Output(result);
            if (result.Mesh != null) {
                output.BadTriangles = new int[result.Mesh.BadTriangles.Count];
                for (int i = 0; i < output.BadTriangles.Length; ++i) output.BadTriangles[i] = result.Mesh.BadTriangles[i];
                output.SteinerLimitReached = result.Mesh.SteinerLimitReached;
            }
            return output;
        }
    }
}
