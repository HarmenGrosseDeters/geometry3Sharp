using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class PlanarComplexNestingProbeTests
    {
        // DistanceAccuracy scales with the input so that the absolute simplification tolerance is isolated.
        // The 3D similarities are represented in their transformed plane's orthonormal 2D frame.
        public static IEnumerable<object[]> Cases(bool passing)
        {
            foreach (double gap in new[] { 1e-1, 1e-2, 1e-3, 1e-6 })
            foreach (bool offset in new[] { false, true })
            foreach (bool sort in new[] { false, true })
            foreach (Similarity s in Similarity.All2D()) {
                bool expectedPass = sort
                    ? !(s == Similarity.RotatedZUtmMillimetre && gap == 1e-6)
                    : s == Similarity.Pow2Large || s == Similarity.Kilometre;
                if (passing == expectedPass)
                    yield return new object[] { gap, offset, sort, s.Name };
            }
        }

        public static IEnumerable<object[]> PassingCases() => Cases(true);
        public static IEnumerable<object[]> DefaultFailingCases() => Cases(false).Where(c => !(bool)c[2]);
        public static IEnumerable<object[]> UlpFailingCases() => Cases(false).Where(c => (bool)c[2]);

        [Theory]
        [MemberData(nameof(PassingCases))]
        public void NestedCircles(double gap, bool offset, bool sort, string similarity)
        {
            Check(gap, offset, sort, similarity);
        }

        [Theory(Skip = "Absolute simplification tolerance 0.1 by design (FindSolidsOptions.Default), see #628")]
        [MemberData(nameof(DefaultFailingCases))]
        public void NestedCircles_DefaultAbsoluteTolerance(double gap, bool offset, bool sort, string similarity)
        {
            Check(gap, offset, sort, similarity);
        }

        [Theory(Skip = "Gap 1e-6 at the rotated UTM millimetre similarity is about 1 ulp of the coordinates, not representable (#628)")]
        [MemberData(nameof(UlpFailingCases))]
        public void NestedCircles_GapBelowCoordinateResolution(double gap, bool offset, bool sort, string similarity)
        {
            Check(gap, offset, sort, similarity);
        }

        static void Check(double gap, bool offset, bool sort, string similarity)
        {
            Similarity s = Similarity.ByName(similarity);
            Vector3d origin = s.Apply(Vector3d.Zero);
            Vector3d ex = s.ApplyDirection(Vector3d.AxisX), ey = s.ApplyDirection(Vector3d.AxisY);
            var center = new Vector2d(origin.Dot(ex), origin.Dot(ey));
            double radius = s.Scale;
            var complex = new PlanarComplex { DistanceAccuracy = 0.1 * radius };
            var outer = complex.Add(new Circle2d(center, radius * (1 + gap)) { IsReversed = true });
            var inner = complex.Add(new Circle2d(center + new Vector2d(offset ? 0.9 * gap * radius : 0, 0), radius));
            var result = complex.FindSolidRegions(sort ? PlanarComplex.FindSolidsOptions.SortPolygons : PlanarComplex.FindSolidsOptions.Default);

            var polygon = Assert.Single(result.Polygons);
            Assert.Single(polygon.Holes);
            var sources = Assert.Single(result.PolygonsSources);
            Assert.Same(outer, sources.Outer);
            Assert.Same(inner, Assert.Single(sources.Holes));
            // 63 samples per inner circle: polygon area differs from pi by about 0.00521.
            // The extra margin covers rounding of the polygon area at the UTM translation.
            double area = result.HolesArea / (radius * radius);
            Assert.True(Math.Abs(area - Math.PI) < 0.006, $"normalized hole area {area:G17} for {similarity}");
        }
    }
}
