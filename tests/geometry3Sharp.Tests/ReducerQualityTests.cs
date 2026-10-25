using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducing must not create degenerate triangles or folds. Solidify output (dual contouring of
    /// shapes with sharp creases at arbitrary angles) followed by reduction triggered both.
    /// </summary>
    public class ReducerQualityTests
    {
        static DMesh3 Box(double size, Vector3d center)
        {
            return new TrivialBox3Generator() { Box = new Box3d(center, size * 0.5 * Vector3d.One) }.Generate().MakeDMesh();
        }
        static DMesh3 Sphere(double r, Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { EdgeVertices = 24, Radius = r }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }
        static DMesh3 Cylinder(double r, double h)
        {
            DMesh3 m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = 40 }.Generate().MakeDMesh();
            return m;
        }
        static DMesh3 Rotated(DMesh3 m)
        {
            MeshTransforms.Rotate(m, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33));
            return m;
        }
        static DMesh3 Union(DMesh3 a, DMesh3 b)
        {
            MeshEditor.Append(a, b);
            return a;
        }

        static DMesh3 Shape(string name)
        {
            switch (name) {
                case "rotated box": return Rotated(Box(10, Vector3d.Zero));
                case "box + sphere": return Union(Box(10, Vector3d.Zero), Sphere(4, new Vector3d(5, 3, 2)));
                case "cylinder + box": return Union(Cylinder(3, 10), Rotated(Box(6, new Vector3d(0, 5, 0))));
                default: return Union(Rotated(Box(8, Vector3d.Zero)), Box(8, new Vector3d(3, 3, 3)));
            }
        }

        [Theory]
        [InlineData("rotated box")]
        [InlineData("box + sphere")]
        [InlineData("cylinder + box")]
        [InlineData("two boxes")]
        public void SolidifyAndReduce_NoDegenerateTrianglesOrFolds(string shape)
        {
            DMesh3 input = Shape(shape);
            var solid = new MeshWindingNumberSolidify(input) { GridResolution = 96, Adaptive = true };
            Assert.True(solid.Compute());
            DMesh3 mesh = solid.Result;

            double diag2 = mesh.CachedBounds.DiagonalLength * mesh.CachedBounds.DiagonalLength;
            double foldCos = Math.Cos(175 * MathUtil.Deg2Rad);
            int degenerate = mesh.TriangleIndices().Count(t => mesh.GetTriArea(t) < 1e-12 * diag2);
            int folds = mesh.EdgeIndices().Count(e => {
                Index2i et = mesh.GetEdgeT(e);
                return et.b >= 0 && mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b)) < foldCos;
            });
            Assert.Equal(0, degenerate);
            Assert.Equal(0, folds);
            Assert.True(mesh.IsClosed());
            // and the reduction still does its job
            Assert.True(mesh.TriangleCount < 6000, $"{mesh.TriangleCount} triangles");
        }
    }
}
