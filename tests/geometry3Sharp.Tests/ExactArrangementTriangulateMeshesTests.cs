using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshArrangement.TriangulateMeshes (the mask of the exact imprint, docs/design/EXACT_IMPRINT.md section 1): the meshes
    /// that are not listed take part in the intersections but are not triangulated or output, and the pieces of the listed
    /// ones are exactly those of the unmasked run.
    /// </summary>
    public class ExactArrangementTriangulateMeshesTests
    {
        static DMesh3 Box(Vector3d c, Vector3d h, Matrix3d rot)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, h) }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices().ToArray())
                m.SetVertex(vid, rot * m.GetVertex(vid) + c);
            return m;
        }

        static Matrix3d Rot() => Matrix3d.AxisAngleD(Vector3d.AxisZ, 29) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);

        static ExactMeshArrangement Run(Similarity s, bool[] mask)
        {
            DMesh3 target = Box(s.Apply(Vector3d.Zero), s.ApplyLength(1) * Vector3d.One, Matrix3d.Identity);
            DMesh3 cutter = Box(s.Apply(new Vector3d(0.3, 0.2, 0.1)), s.ApplyLength(0.7) * Vector3d.One, Rot());
            var arr = new ExactMeshArrangement(new List<DMesh3> { target, cutter }) { TriangulateMeshes = mask };
            arr.Compute();
            return arr;
        }

        static string Key(ExactMeshArrangement arr, ExactMeshArrangement.OutputTriangle o)
        {
            var pts = new[] { arr.Exact(o.A).ToString(), arr.Exact(o.B).ToString(), arr.Exact(o.C).ToString() };
            return o.Source + ":" + string.Join("|", pts.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void MaskedRun_OutputsOnlyListedMesh_WithTheSamePieces()
        {
            var full = Run(Similarity.Identity, null);
            var masked = Run(Similarity.Identity, new[] { true, false });
            Assert.All(masked.Output, o => Assert.Equal(0, o.Mesh));
            Assert.True(full.Output.Any(o => o.Mesh == 1));
            var expected = full.Output.Where(o => o.Mesh == 0).Select(o => Key(full, o)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var actual = masked.Output.Select(o => Key(masked, o)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(actual.Count > 12, "the target must be split");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MaskedRun_PredictsOnlyListedPieces_AndIntersectionsAreUnchanged()
        {
            var full = Run(Similarity.Identity, null);
            var masked = Run(Similarity.Identity, new[] { true, false });
            Assert.Equal(masked.Output.Count, masked.PredictedPieces);
            Assert.True(masked.PredictedPieces < full.PredictedPieces);
            Assert.Equal(full.IntersectingPairs, masked.IntersectingPairs);
            Assert.Equal(full.PointCount >= masked.PointCount, true);
        }

        [Fact]
        public void MaskedRun_TriangulatesTheOtherMeshInstead()
        {
            var masked = Run(Similarity.Identity, new[] { false, true });
            Assert.NotEmpty(masked.Output);
            Assert.All(masked.Output, o => Assert.Equal(1, o.Mesh));
        }

        public static IEnumerable<object[]> Scalings() { foreach (var s in Similarity.All().Where(x => x.IsRotated == false)) yield return new object[] { s.Name }; }

        [Theory]
        [MemberData(nameof(Scalings))]
        public void MaskedRun_PieceCountIsInvariant(string name)
        {
            Similarity s = Similarity.All().First(x => x.Name == name);
            int reference = Run(Similarity.Identity, new[] { true, false }).Output.Count;
            Assert.Equal(reference, Run(s, new[] { true, false }).Output.Count);
        }
    }
}
