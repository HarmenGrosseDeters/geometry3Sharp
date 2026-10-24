using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Volumetric booleans: combine the winding numbers of the inputs and extract the result with dual
    /// contouring. Expected volumes are analytic.
    /// </summary>
    public class MeshBooleanTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            var box = new Box3d(0.5 * (min + max), 0.5 * (max - min));
            return new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
        }
        static DMesh3 Box(double min, double max) { return Box(min * Vector3d.One, max * Vector3d.One); }

        // cylinder along +y through (cx, *, cz), from y0 to y1
        static DMesh3 CylinderY(double cx, double cz, double r, double y0, double y1)
        {
            DMesh3 m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)(y1 - y0), Slices = 64 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(cx, y0, cz));
            return m;
        }

        static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        static double MinDistance(DMesh3 m, Vector3d p) { return m.VertexIndices().Min(v => m.GetVertex(v).Distance(p)); }

        static DMesh3 Run(DMesh3 a, DMesh3 b, MeshWindingNumberBoolean.Operations op, int resolution = 96)
        {
            var boolean = new MeshWindingNumberBoolean(a, b, op) { GridResolution = resolution };
            Assert.True(boolean.Compute());
            DMesh3 result = boolean.Result;
            Assert.True(result.IsClosed(), "result is not closed");
            Assert.Empty(MeshIterators.BowtieVertices(result));
            Assert.True(result.CheckValidity(false, FailMode.ReturnOnly));
            return result;
        }

        static void AssertClean(DMesh3 m)
        {
            double diag2 = m.CachedBounds.DiagonalLength * m.CachedBounds.DiagonalLength;
            Assert.Equal(0, m.TriangleIndices().Count(t => m.GetTriArea(t) < 1e-12 * diag2));
            double foldCos = Math.Cos(175 * MathUtil.Deg2Rad);
            Assert.Equal(0, m.EdgeIndices().Count(e => {
                Index2i et = m.GetEdgeT(e);
                return et.b >= 0 && m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) < foldCos;
            }));
        }


        [Fact]
        public void Union_OverlappingBoxes_ExactVolumeAndSharpConcaveCorners()
        {
            DMesh3 result = Run(Box(0, 10), Box(5, 15), MeshWindingNumberBoolean.Operations.Union);
            Assert.InRange(Volume(result), 1875 - 0.5, 1875 + 0.5);
            Assert.Equal(1, Components(result));
            foreach (Vector3d c in new[] { new Vector3d(0, 0, 0), new Vector3d(15, 15, 15),
                                           new Vector3d(10, 10, 5), new Vector3d(10, 5, 10), new Vector3d(5, 10, 10) })
                Assert.True(MinDistance(result, c) < 1e-3, $"corner {c} lost");
            AssertClean(result);
        }

        [Fact]
        public void Intersection_OverlappingBoxes_IsTheOverlapBox()
        {
            DMesh3 result = Run(Box(0, 10), Box(5, 15), MeshWindingNumberBoolean.Operations.Intersection);
            Assert.InRange(Volume(result), 125 - 0.1, 125 + 0.1);
            foreach (Vector3d c in new Box3d(7.5 * Vector3d.One, 2.5 * Vector3d.One).ComputeVertices())
                Assert.True(MinDistance(result, c) < 1e-3, $"corner {c} lost");
            AssertClean(result);
        }

        [Fact]
        public void Difference_BoxMinusBox_CutsTheCorner()
        {
            DMesh3 result = Run(Box(0, 10), Box(5, 15), MeshWindingNumberBoolean.Operations.Difference);
            Assert.InRange(Volume(result), 875 - 0.5, 875 + 0.5);
            Assert.True(MinDistance(result, new Vector3d(5, 5, 5)) < 1e-3, "concave corner lost");
            AssertClean(result);
        }

        [Fact]
        public void Difference_DrillHole_ThroughHoleWithRightVolume()
        {
            DMesh3 result = Run(Box(0, 10), CylinderY(5, 5, 2, -1, 11), MeshWindingNumberBoolean.Operations.Difference, 128);
            double expected = 1000 - Math.PI * 4 * 10;
            Assert.InRange(Volume(result), expected * 0.99, expected * 1.01);
            // a through hole: genus 1, Euler characteristic 0
            Assert.Equal(0, result.VertexCount - result.EdgeCount + result.TriangleCount);
            AssertClean(result);
        }

        [Fact]
        public void Union_CoplanarFaces_MergesFlat()
        {
            // classic failure case for mesh booleans: the boxes share their top, bottom, front and back planes
            DMesh3 result = Run(Box(Vector3d.Zero, 10 * Vector3d.One), Box(new Vector3d(5, 0, 0), new Vector3d(15, 10, 10)),
                MeshWindingNumberBoolean.Operations.Union);
            Assert.InRange(Volume(result), 1500 - 0.5, 1500 + 0.5);
            Assert.Equal(1, Components(result));
            // all vertices on the outer box
            var outer = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(15, 10, 10));
            foreach (int vid in result.VertexIndices()) {
                Vector3d p = result.GetVertex(vid);
                double d = Math.Min(Math.Min(Math.Min(p.x, 15 - p.x), Math.Min(p.y, 10 - p.y)), Math.Min(p.z, 10 - p.z));
                Assert.True(Math.Abs(d) < 1e-3, $"vertex {p} not on the outer box");
            }
            AssertClean(result);
        }

        [Fact]
        public void Union_DisjointBoxes_TwoParts()
        {
            DMesh3 result = Run(Box(0, 10), Box(20, 30), MeshWindingNumberBoolean.Operations.Union);
            Assert.Equal(2, Components(result));
            Assert.InRange(Volume(result), 2000 - 0.5, 2000 + 0.5);
        }

        [Fact]
        public void Union_BrokenInputs_StillClosedAndRight()
        {
            // A: box without its top face, as an unwelded triangle soup; B: overlapping box
            DMesh3 a = Box(0, 10);
            foreach (int tid in a.TriangleIndices().ToArray())
                if (a.GetTriCentroid(tid).y > 9.99)
                    a.RemoveTriangle(tid, true, false);
            var soup = new DMesh3();
            foreach (int tid in a.TriangleIndices()) {
                Index3i t = a.GetTriangle(tid);
                soup.AppendTriangle(soup.AppendVertex(a.GetVertex(t.a)), soup.AppendVertex(a.GetVertex(t.b)), soup.AppendVertex(a.GetVertex(t.c)));
            }
            Assert.False(soup.IsClosed());

            DMesh3 result = Run(soup, Box(5, 15), MeshWindingNumberBoolean.Operations.Union);
            Assert.InRange(Volume(result), 1875 * 0.99, 1875 * 1.01);
            Assert.Equal(1, Components(result));
        }

        [Fact]
        public void Union_SelfOverlappingInput_CountsOverlapOnce()
        {
            // A is two overlapping boxes in one mesh (not unioned), B a third box
            DMesh3 a = Box(0, 10);
            MeshEditor.Append(a, Box(5, 15));
            DMesh3 result = Run(a, Box(new Vector3d(12, 0, 0), new Vector3d(20, 4, 4)), MeshWindingNumberBoolean.Operations.Union);
            // |A| = 1875, B = 8x4x4 = 128, overlap of B with A: x in [12,15], y in [5,..] -> none (B is y<4, z<4; A's second box starts at 5)
            Assert.InRange(Volume(result), 1875 + 128 - 0.5, 1875 + 128 + 0.5);
        }
    }
}
