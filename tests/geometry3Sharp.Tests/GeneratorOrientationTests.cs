using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The cylinder/cone generators produced inside-out meshes (docs/Analyse-2026-10.md):
    /// with the default Clockwise = false every triangle faced inwards, unlike the box, sphere,
    /// revolve and tube generators. These tests check every triangle against the solid it bounds.
    /// </summary>
    public class GeneratorOrientationTests
    {
        static double TriangleArea(DMesh3 m, int tid) { return m.GetTriArea(tid); }

        /// <summary> exact winding number of the (closed) surface, also for meshes with duplicated seam vertices </summary>
        static double WindingNumber(DMesh3 m, Vector3d p)
        {
            double sum = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in m.TriangleIndices()) {
                m.GetTriVertices(tid, ref a, ref b, ref c);
                sum += MathUtil.TriSolidAngle(a, b, c, ref p);
            }
            return sum / (4 * Math.PI);
        }

        /// <summary> signed volume, measured from a point that lies in no face plane </summary>
        static double SignedVolume(DMesh3 m)
        {
            Vector3d p = new Vector3d(0.0123, 0.0377, -0.0211);
            double vol = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in m.TriangleIndices()) {
                m.GetTriVertices(tid, ref a, ref b, ref c);
                vol += (a - p).Dot((b - p).Cross(c - p)) / 6;
            }
            return vol;
        }

        /// <summary> area of the polygon with vertices at radius r, n edges of angle step d (a fan from the axis) </summary>
        static double FanArea(double r, int n, double d) { return 0.5 * r * r * n * Math.Sin(d); }

        static double Frustum(double h, double a0, double a1) { return h / 3 * (a0 + a1 + Math.Sqrt(a0 * a1)); }

        /// <summary>
        /// Every non-degenerate triangle must face out of the solid: just in front of it the winding
        /// number is 0, just behind it 1. Returns the problems found.
        /// </summary>
        static List<string> CheckClosedSolid(string name, DMesh3 m, double expectedVolume)
        {
            var problems = new List<string>();
            double size = m.CachedBounds.DiagonalLength;
            int inward = 0;
            foreach (int tid in m.TriangleIndices()) {
                if (TriangleArea(m, tid) < 1e-10 * size * size)
                    continue;
                Vector3d c = m.GetTriCentroid(tid), n = m.GetTriNormal(tid);
                double front = WindingNumber(m, c + 1e-4 * size * n);
                double back = WindingNumber(m, c - 1e-4 * size * n);
                if (Math.Abs(front) > 0.1 || Math.Abs(back - 1) > 0.1)
                    inward++;
            }
            if (inward > 0)
                problems.Add($"{name}: {inward} of {m.TriangleCount} triangles face into the solid");
            double vol = SignedVolume(m);
            if (Math.Abs(vol - expectedVolume) > 1e-4 * expectedVolume)
                problems.Add($"{name}: signed volume {vol:G6}, expected {expectedVolume:G6}");
            return problems;
        }

        public static IEnumerable<object[]> SharedVertices() { return new[] { new object[] { false }, new object[] { true } }; }

        [Theory]
        [MemberData(nameof(SharedVertices))]
        public void CappedCylinder_FacesOutward(bool noSharedVertices)
        {
            var problems = new List<string>();
            int n = 16;
            // full cylinder and a frustum
            foreach (var (r0, r1) in new[] { (1.0f, 1.0f), (1.5f, 0.5f) }) {
                var gen = new CappedCylinderGenerator() { BaseRadius = r0, TopRadius = r1, Height = 2, Slices = n, NoSharedVertices = noSharedVertices };
                DMesh3 m = gen.Generate().MakeDMesh();
                double d = 2 * Math.PI / n;
                problems.AddRange(CheckClosedSolid($"cylinder r={r0}/{r1}", m, Frustum(2, FanArea(r0, n, d), FanArea(r1, n, d))));
            }
            // wedges, convex and not
            foreach (float end in new[] { 90.0f, 270.0f }) {
                var gen = new CappedCylinderGenerator() { Height = 2, Slices = n, EndAngleDeg = end, NoSharedVertices = noSharedVertices };
                DMesh3 m = gen.Generate().MakeDMesh();
                double d = end * MathUtil.Deg2Rad / (n - 1);
                problems.AddRange(CheckClosedSolid($"wedge 0-{end}", m, 2 * FanArea(1, n - 1, d)));
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Theory]
        [MemberData(nameof(SharedVertices))]
        public void Cone_FacesOutward(bool noSharedVertices)
        {
            var problems = new List<string>();
            int n = 16;
            var gen = new ConeGenerator() { BaseRadius = 1.5f, Height = 2, Slices = n, NoSharedVertices = noSharedVertices };
            problems.AddRange(CheckClosedSolid("cone", gen.Generate().MakeDMesh(), 2.0 / 3 * FanArea(1.5, n, 2 * Math.PI / n)));
            foreach (float end in new[] { 90.0f, 270.0f }) {
                var wedge = new ConeGenerator() { BaseRadius = 1.5f, Height = 2, Slices = n, EndAngleDeg = end, NoSharedVertices = noSharedVertices };
                double d = end * MathUtil.Deg2Rad / (n - 1);
                problems.AddRange(CheckClosedSolid($"cone wedge 0-{end}", wedge.Generate().MakeDMesh(), 2.0 / 3 * FanArea(1.5, n - 1, d)));
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Theory]
        [MemberData(nameof(SharedVertices))]
        public void VerticalGeneralizedCylinder_FacesOutward(bool noSharedVertices)
        {
            int n = 16;
            var sections = new[] {
                new MeshGenerator.CircularSection(1.0f, 0.0f),
                new MeshGenerator.CircularSection(1.5f, 1.0f),
                new MeshGenerator.CircularSection(0.5f, 2.5f) };
            var gen = new VerticalGeneralizedCylinderGenerator() { Sections = sections, Slices = n, NoSharedVertices = noSharedVertices };
            double d = 2 * Math.PI / n;
            double vol = 0;
            for (int i = 0; i + 1 < sections.Length; ++i)
                vol += Frustum(sections[i + 1].SectionY - sections[i].SectionY, FanArea(sections[i].Radius, n, d), FanArea(sections[i + 1].Radius, n, d));
            var problems = CheckClosedSolid("generalized cylinder", gen.Generate().MakeDMesh(), vol);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void Radial3DArrow_FacesOutward()
        {
            var gen = new Radial3DArrowGenerator() { StickRadius = 0.5f, StickLength = 2, HeadBaseRadius = 1, TipRadius = 0, HeadLength = 1, Slices = 16 };
            DMesh3 m = gen.Generate().MakeDMesh();
            Assert.True(SignedVolume(m) > 0, "arrow has negative volume");
            var problems = CheckClosedSolid("arrow", m, SignedVolume(m));
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void OpenCylinder_FacesOutward()
        {
            var gen = new OpenCylinderGenerator() { Height = 2, Slices = 16 };
            DMesh3 m = gen.Generate().MakeDMesh();
            foreach (int tid in m.TriangleIndices()) {
                Vector3d c = m.GetTriCentroid(tid), n = m.GetTriNormal(tid);
                Assert.True(n.Dot(new Vector3d(c.x, 0, c.z)) > 0, $"triangle {tid} faces the axis");
            }
        }

        [Theory]
        [MemberData(nameof(SharedVertices))]
        public void Clockwise_ReversesEveryTriangle(bool noSharedVertices)
        {
            var ccw = new CappedCylinderGenerator() { NoSharedVertices = noSharedVertices }.Generate();
            var cw = new CappedCylinderGenerator() { NoSharedVertices = noSharedVertices, Clockwise = true }.Generate();
            DMesh3 a = ccw.MakeDMesh(), b = cw.MakeDMesh();
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.True(SignedVolume(a) > 0);
            Assert.Equal(-SignedVolume(a), SignedVolume(b), 10);
        }
    }
}
