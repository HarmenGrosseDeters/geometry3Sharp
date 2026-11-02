using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact d-separation certificate of the snap-rounding experiment (Harness/SeparationCertificate.cs, used by
    /// benchmarks/SnapRoundingExperiment): known distances right at d and one ulp beyond, for each kind of pair, and
    /// meshes with a gap below and above d.
    /// </summary>
    public class SeparationCertificateTests
    {
        static CertDyadic Sq(double d) => CertDyadic.Of(d) * CertDyadic.Of(d);
        static CertVector V(double x, double y, double z) => SeparationCertificate.V(new Vector3d(x, y, z));
        // the next double above a positive x (Math.BitIncrement is not in net48)
        static double Up(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);

        [Fact]
        public void PointSegment_AtDIsNotFarther_OneUlpBeyondIs()
        {
            double d = 3e-15;
            // (the nearest point inside the segment, and beyond either end)
            Assert.False(SeparationCertificate.point_segment_farther(V(0.5, d, 0), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            Assert.True(SeparationCertificate.point_segment_farther(V(0.5, Up(d), 0), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            Assert.False(SeparationCertificate.point_segment_farther(V(-d, 0, 0), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            Assert.True(SeparationCertificate.point_segment_farther(V(-Up(d), 0, 0), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            Assert.False(SeparationCertificate.point_segment_farther(V(1, 0, d), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            Assert.True(SeparationCertificate.point_segment_farther(V(1, 0, Up(d)), V(0, 0, 0), V(1, 0, 0), Sq(d)));
            // (beyond the end on the line: the distance to the end, not to the line)
            Assert.True(SeparationCertificate.point_segment_farther(V(1 + 1e-14, 0, 0), V(0, 0, 0), V(1, 0, 0), Sq(d)));
        }

        [Fact]
        public void PointTriangle_AboveTheInteriorAndBesideAnEdge()
        {
            double d = 3e-15;
            CertVector a = V(0, 0, 0), b = V(1, 0, 0), c = V(0, 1, 0);
            Assert.False(SeparationCertificate.point_triangle_farther(V(0.25, 0.25, d), a, b, c, Sq(d)));
            Assert.True(SeparationCertificate.point_triangle_farther(V(0.25, 0.25, Up(d)), a, b, c, Sq(d)));
            Assert.True(SeparationCertificate.point_triangle_farther(V(0.25, 0.25, -Up(d)), a, b, c, Sq(d)));
            // (beside the edge b-c in the plane: the distance to that edge, not to the plane)
            Assert.True(SeparationCertificate.point_triangle_farther(V(0.5 + 1e-14, 0.5 + 1e-14, 0), a, b, c, Sq(d)));
            Assert.False(SeparationCertificate.point_triangle_farther(V(0.5 + 1e-16, 0.5 + 1e-16, 0), a, b, c, Sq(d)));
            // (a degenerate triangle: its edges)
            Assert.False(SeparationCertificate.point_triangle_farther(V(0.5, d, 0), a, b, V(2, 0, 0), Sq(d)));
        }

        [Fact]
        public void Segments_SkewParallelAndEndpoints()
        {
            double d = 3e-15;
            // skew, nearest points inside both
            Assert.False(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(0.5, -1, d), V(0.5, 1, d), Sq(d)));
            Assert.True(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(0.5, -1, Up(d)), V(0.5, 1, Up(d)), Sq(d)));
            // parallel and overlapping
            Assert.False(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(0.5, 0, d), V(2, 0, d), Sq(d)));
            Assert.True(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(0.5, 0, Up(d)), V(2, 0, Up(d)), Sq(d)));
            // crossing lines, but the nearest points of the segments at an end
            Assert.True(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(1 + 1e-14, -1, 0), V(1 + 1e-14, 1, 0), Sq(d)));
            Assert.False(SeparationCertificate.segments_farther(V(0, 0, 0), V(1, 0, 0), V(1 + 1e-16, -1, 0), V(1 + 1e-16, 1, 0), Sq(d)));
        }

        [Fact]
        public void Mesh_PairExactlyAtD_NotCertified()
        {
            // a vertex at distance exactly d = √3 u (u = ulp(1.5) = 2^-52) from another triangle's corner: not farther
            // than d (the double √3 is below the true one, so d² must not come from it; independent review)
            double u = Math.Pow(2, -52);
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(-1, 0, 0)); m.AppendVertex(new Vector3d(0, -1, 0));
            m.AppendVertex(new Vector3d(u, u, u)); m.AppendVertex(new Vector3d(1, 1, 1.5)); m.AppendVertex(new Vector3d(1, 1.25, 1));
            m.AppendTriangle(0, 1, 2);
            m.AppendTriangle(3, 4, 5);
            Assert.True(SeparationCertificate.Check(m).Uncertified > 0);
            // one ulp of the coordinate farther: certified
            m.SetVertex(3, new Vector3d(2 * u, u, u));
            Assert.Equal(0, SeparationCertificate.Check(m).Uncertified);
        }

        static DMesh3 Boxes(double gap)
        {
            DMesh3 m = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(m, DegenerateFamilies.Box(new Vector3d(0.2, 0.2, 1 + gap), new Vector3d(0.7, 0.7, 2)));
            return m;
        }

        [Fact]
        public void Meshes_GapBelowAndAboveD()
        {
            // d = √3 ulp(2) = 7.7e-16 here
            SeparationCertificate sphere = SeparationCertificate.Check(DegenerateFamilies.Sphere(6));
            Assert.True(sphere.Pairs > 0);
            Assert.Equal(0, sphere.Uncertified);
            SeparationCertificate near = SeparationCertificate.Check(Boxes(Math.Pow(2, -52)));
            Assert.Equal(Math.Sqrt(3) * Math.Pow(2, -51), near.D);
            Assert.True(near.Uncertified > 0);
            Assert.Equal(0, SeparationCertificate.Check(Boxes(Math.Pow(2, -48))).Uncertified);
            // (vertices at one position count as one: a sphere with unwelded duplicate vertices is certified)
            Assert.Equal(0, SeparationCertificate.Check(DegenerateFamilies.Make(DegenerateFamilies.Family.Unwelded, 0, 0, 3)).Uncertified);
            // (touching boxes: vertices at one position count as one, so only the pairs of other features fail)
            Assert.True(SeparationCertificate.Check(Boxes(0)).Uncertified > 0);
        }
    }
}
