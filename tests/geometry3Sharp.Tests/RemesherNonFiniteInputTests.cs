using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A NaN or infinite vertex in the input: smoothing, splits and projection spread it to the neighbours (100 of
    /// 300 vertices after 5 passes), and the projection onto such a mesh threw. Remesher and RemesherPro skip the
    /// pass and report the vertices (NonFiniteVertices); MeshProjectionTarget leaves a point it cannot project.
    /// </summary>
    public class RemesherNonFiniteInputTests
    {
        static DMesh3 Sphere(double bad)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            mesh.SetVertex(5, new Vector3d(bad, 0, 0));
            return mesh;
        }

        static int NonFinite(DMesh3 mesh) => mesh.VertexIndices().Count(v => mesh.GetVertex(v).IsFinite == false);

        [Theory]
        [InlineData(false, false, double.NaN)]
        [InlineData(false, true, double.NaN)]
        [InlineData(true, false, double.NaN)]
        [InlineData(true, true, double.PositiveInfinity)]
        public void NonFiniteVertex_PassSkipped(bool pro, bool projection, double bad)
        {
            DMesh3 input = Sphere(bad), mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            if (projection)
                r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            r.SetTargetEdgeLength(0.2);
            for (int k = 0; k < 5; ++k) {
                if (pro)
                    ((RemesherPro)r).RemeshIteration();
                else
                    r.BasicRemeshPass();
            }
            Assert.Equal(1, r.NonFiniteVertices);
            Assert.Equal(1, NonFinite(mesh));
            Assert.Equal(input.TriangleCount, mesh.TriangleCount);
            foreach (int v in input.VertexIndices())
                if (v != 5)
                    Assert.Equal(input.GetVertex(v), mesh.GetVertex(v));

            if (pro) {
                ((RemesherPro)r).FastestRemesh(5);
                Assert.Equal(1, NonFinite(mesh));
                Assert.Equal(input.TriangleCount, mesh.TriangleCount);
            }

            // once the vertex is finite the passes run again
            mesh.SetVertex(5, input.GetVertex(4));
            if (pro)
                ((RemesherPro)r).RemeshIteration();
            else
                r.BasicRemeshPass();
            Assert.Equal(0, r.NonFiniteVertices);
            Assert.NotEqual(input.TriangleCount, mesh.TriangleCount);
        }

        [Fact]
        public void ProjectionTarget_UnprojectablePoint_Stays()
        {
            var target = new MeshProjectionTarget(Sphere(double.NaN));
            Vector3d p = new Vector3d(double.NaN, 1, 2);
            Vector3d q0 = target.Project(p);
            Assert.True(double.IsNaN(q0.x) && q0.y == 1 && q0.z == 2);
            Vector3d q = target.Project(p, out Vector3d n);
            Assert.True(double.IsNaN(q.x) && q.y == 1 && q.z == 2);
            Assert.Equal(Vector3d.Zero, n);
        }
    }
}
