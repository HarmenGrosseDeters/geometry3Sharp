using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The metamorphic relations A1 (anisotropic scale) and P1 (tiny perturbation) of docs/research/METAMORPHIC_TESTING.md,
    /// sections 5 to 7. Pure helpers over the base API (no xUnit): benchmarks/MetamorphicFalsification compiles this file
    /// against the parent commits of the fixes, which is the first validation of the relations (A1 fires on the parent of #712,
    /// P1 on the parent of #719, both quiet on master; see its README).
    /// </summary>
    public static class MetamorphicRelations
    {
        /// <summary>
        /// A1 flattens or stretches one axis by 2^e, e from -46 to +46 (0 is the unflattened reference). The limit is the one
        /// of the #712 repro (a flattening by 2^-46 of the other extents).
        /// </summary>
        public const int AnisotropicLimit = 46;

        /// <summary> All exponents of A1 (not 0): the nightly sweep. </summary>
        public static IEnumerable<int> AnisotropicAll() =>
            Enumerable.Range(-AnisotropicLimit, 2 * AnisotropicLimit + 1).Where(e => e != 0);

        /// <summary>
        /// A cost bound on a count (triangles, vertices, Steiner points): the transformed run may not exceed the reference by more
        /// than CountFactor, unless it stays under the floor (a count of a few hundred is no cost, however it compares with a
        /// reference of zero: the unflattened input is clean and needs no wrap). One-sided, like the cost bound of
        /// CostInvarianceTests: a flattened input may legitimately be smaller (it collapses), the defect class is the growth.
        /// </summary>
        public const double CountFactor = 2;

        /// <summary> "" if transformed is within the bound of reference (see CountBound), else the violation. </summary>
        public static string CountBound(string what, long reference, long transformed, long floor)
        {
            double limit = Math.Max(Math.Ceiling(reference * CountFactor), floor);
            return transformed <= limit ? "" : $"{what}: {reference} vs {transformed} (more than {CountFactor} times the reference and above the floor {floor})";
        }

        /// <summary>
        /// A copy of the mesh with the coordinate along one axis (0, 1 or 2) multiplied by 2^e: exact in floating point, so the
        /// shape is flattened (e below 0) or stretched (e above 0) without any rounding of the other coordinates. Ids kept.
        /// </summary>
        public static DMesh3 Stretch(DMesh3 mesh, int axis, int e)
        {
            var copy = new DMesh3(mesh);
            double f = Math.Pow(2, e);
            foreach (int v in copy.VertexIndices()) {
                Vector3d p = copy.GetVertex(v);
                p[axis] *= f;
                copy.SetVertex(v, p);
            }
            return copy;
        }

        /// <summary>
        /// P1: a copy with the coordinate of smallest magnitude (of all vertices; a zero first) replaced by 2^e, the tiny
        /// perturbation of #719 (the span of the grid changes, the shape does not). Ids kept. The first of equal magnitudes is taken.
        /// </summary>
        public static DMesh3 SmallestCoordinate(DMesh3 mesh, int e)
        {
            var copy = new DMesh3(mesh);
            int best = -1, axis = 0;
            double smallest = double.MaxValue;
            foreach (int v in copy.VertexIndices()) {
                Vector3d p = copy.GetVertex(v);
                for (int k = 0; k < 3; ++k)
                    if (Math.Abs(p[k]) < smallest) {
                        smallest = Math.Abs(p[k]);
                        best = v;
                        axis = k;
                    }
            }
            if (best < 0) return copy;
            Vector3d q = copy.GetVertex(best);
            q[axis] = Math.Pow(2.0, e);
            copy.SetVertex(best, q);
            return copy;
        }

        /// <summary>
        /// P1 bound on a value: "" if the perturbed value is within tolerance of the unperturbed one (the result moves by at most
        /// the shift, far below the features of an input of size about one), else the violation.
        /// </summary>
        public static string ValueBound(string what, double before, double after, double tolerance)
        {
            if (double.IsNaN(before) || double.IsNaN(after)) return $"{what}: NaN ({before} vs {after})";
            return Math.Abs(before - after) <= tolerance ? "" : $"{what}: {before:R} vs {after:R} (more than {tolerance:R} apart)";
        }

        /// <summary>
        /// The exponents of P1: 2^-500 .. 2^-1000, the range of the #719 span (a coordinate that is 0 or tiny next to coordinates of 16).
        /// The PR tier runs the two ends (literal InlineData); the nightly sweep every 25th exponent.
        /// </summary>
        public static IEnumerable<int> TinyAll() => Enumerable.Range(0, 21).Select(i => -500 - 25 * i);
    }
}
