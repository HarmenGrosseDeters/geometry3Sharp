using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public partial class RobustnessSweepTests
    {
        [Fact]
        public void InsertPolygon_PowerOfTwoScalesPreserveTheCut()
        {
            Polygon2d polygon = Polygon2d.MakeCircle(0.45, 12);
            polygon.Translate(new Vector2d(0.13, 0.07));
            var baseline = Insert(Similarity.Identity);
            AssertSameCut(Insert(Similarity.Identity), Similarity.Identity);
            bool wasSerial = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                AssertSameCut(Insert(Similarity.Pow2Small), Similarity.Pow2Small);
            } finally {
                gParallel.SerialOnThisThread = wasSerial;
            }

            // Transform within the xy plane, as the operation requires z = 0.
            var scales = new[] { -100, -24, 20, 100 }.Select(k =>
                new Similarity($"scale 2^{k}", Math.Pow(2, k), Matrix3d.Identity, Vector3d.Zero));
            foreach (Similarity s in Similarity.All2D().Concat(scales)) {
                var result = Insert(s);
                if (s.IsExact)
                    AssertSameCut(result, s);
            }

            void AssertSameCut(MeshInsertPolygon result, Similarity s)
            {
                Assert.Equal(baseline.InteriorTriangles.ToArray(), result.InteriorTriangles.ToArray());
                Assert.Equal(baseline.Mesh.TriangleIndices().Select(baseline.Mesh.GetTriangle).ToArray(),
                    result.Mesh.TriangleIndices().Select(result.Mesh.GetTriangle).ToArray());
                Assert.Equal(baseline.Mesh.VertexIndices().SelectMany(v => Bits(baseline.Mesh.GetVertex(v))).ToArray(),
                    result.Mesh.VertexIndices().SelectMany(v => Bits(s.Inverse(result.Mesh.GetVertex(v)))).ToArray());
            }

            Insert(Similarity.Identity, addDetachedTriangle: true);

            MeshInsertPolygon Insert(Similarity s, bool addDetachedTriangle = false)
            {
                DMesh3 mesh = FlatGrid();
                foreach (int vid in mesh.VertexIndices()) {
                    Vector2d uv = s.Apply2d(mesh.GetVertex(vid).xy);
                    mesh.SetVertex(vid, new Vector3d(uv.x, uv.y, 0));
                }
                // An unused vertex is outside the UV domain of the triangles.
                mesh.AppendVertex(new Vector3d(s.Scale * 1e12, 0, 0));
                if (addDetachedTriangle) {
                    int a = mesh.AppendVertex(new Vector3d(1e5, 0, 0));
                    int b = mesh.AppendVertex(new Vector3d(1e5 + 1, 0, 0));
                    int c = mesh.AppendVertex(new Vector3d(1e5, 1, 0));
                    mesh.AppendTriangle(a, b, c);
                }
                var transformed = new Polygon2d(polygon.Vertices.Select(s.Apply2d));
                var insertion = new MeshInsertPolygon { Mesh = mesh, Polygon = new GeneralPolygon2d(transformed) };
                var curve = new MeshInsertUVPolyCurve(mesh, transformed);
                Assert.Equal(ValidationStatus.Ok, curve.Validate());
                Assert.True(insertion.Insert(), s.Name);
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
                Assert.Equal(30, insertion.InteriorTriangles.Count);
                return insertion;
            }

            long[] Bits(Vector3d v) => new[] {
                BitConverter.DoubleToInt64Bits(v.x), BitConverter.DoubleToInt64Bits(v.y), BitConverter.DoubleToInt64Bits(v.z)
            };
        }
    }
}
