using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact sign stages of ExactMeshBoolean's classification (plane side, normal dot, the 4 x 4 orientation) run on
    /// stack limbs (ExactLimbSigns). Through the library's existing entry points only, so that the tests also build
    /// against the base library: with the filters switched off every such decision takes the exact stage, the result
    /// must be the filtered one, and the operation must allocate far less than with BigInteger temporaries.
    /// </summary>
    public class ExactBooleanLimbSignsTests
    {
        // open inputs, whose pieces are classified one by one (the Antipodal path): tilted grids through the unit cube
        // that cross each other, and a sphere with holes
        internal static DMesh3 Shape(string kind)
        {
            DMesh3 m;
            if (kind.EndsWith(" away")) {
                // away from the origin: the planes' offsets D are large against the normals, so the side of a ray's far
                // point depends on D (near the origin n . R alone has its sign)
                m = Shape(kind.Substring(0, kind.Length - 5));
                MeshTransforms.Translate(m, new Vector3d(1000, 2000, -1500));
                return m;
            }
            if (kind.StartsWith("sheets")) {
                int n = int.Parse(kind.Substring(6));
                var r = new Random(9);
                m = new DMesh3();
                for (int k = 0; k < n; ++k) {
                    Vector3d o = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), u = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5).Normalized;
                    Vector3d v = u.Cross(new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5)).Normalized;
                    const int G = 6;
                    int[,] id = new int[G + 1, G + 1];
                    for (int i = 0; i <= G; ++i) for (int j = 0; j <= G; ++j) id[i, j] = m.AppendVertex(o + (i / (double)G - 0.5) * 1.5 * u + (j / (double)G - 0.5) * 1.5 * v);
                    for (int i = 0; i < G; ++i) for (int j = 0; j < G; ++j) { m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]); m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]); }
                }
            } else {
                m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 14 }.Generate().MakeDMesh();
                foreach (int t in new List<int>(m.TriangleIndices())) if (t % 37 == 0) m.RemoveTriangle(t);
            }
            return m;
        }

        // the second operand: the first one, 20 degrees about (1, 1, 0) through its centre and a quarter of its box away
        internal static DMesh3 Copy(DMesh3 a)
        {
            DMesh3 b = new DMesh3(a);
            AxisAlignedBox3d bounds = a.CachedBounds;
            MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
            return b;
        }

        static long AllocatedBy(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, bool disableFilters, out ExactMeshBoolean result)
        {
            // (the parallel loops on this thread, so that the thread's allocation counter sees the whole operation and
            // nothing of the tests that run beside this one)
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            try {
                long before = GC.GetAllocatedBytesForCurrentThread();
                result = new ExactMeshBoolean(a, b, op) { DisableFilters = disableFilters };
                result.Compute();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            } finally {
                gParallel.SerialOnThisThread = serial;
            }
        }

        // the triangles of a mesh as sorted coordinate strings (the same mesh: the same list)
        static List<string> Triangles(DMesh3 m)
        {
            var list = new List<string>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                var c = new[] { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                int first = 0;
                for (int k = 1; k < 3; ++k)
                    if (c[k].x < c[first].x || (c[k].x == c[first].x && (c[k].y < c[first].y || (c[k].y == c[first].y && c[k].z < c[first].z)))) first = k;
                list.Add(FormattableString.Invariant($"{c[first].x:R} {c[first].y:R} {c[first].z:R} | {c[(first + 1) % 3].x:R} {c[(first + 1) % 3].y:R} {c[(first + 1) % 3].z:R} | {c[(first + 2) % 3].x:R} {c[(first + 2) % 3].y:R} {c[(first + 2) % 3].z:R}"));
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // Measured (one thread, .NET 10): 17 / 34 / 55 MB with the limb stages, 73 / 160 / 158 MB with the BigInteger
        // expressions. The bound is on the allocation of the operation, which does not depend on the machine's speed.
        [Theory]
        [InlineData("sheets3", 40)]
        [InlineData("sheets4", 80)]
        [InlineData("open", 100)]
        [InlineData("sheets3 away", 40)]
        public void FiltersOff_SameResult_WithoutBigIntegerSigns(string kind, int megabytes)
        {
            DMesh3 a = Shape(kind), b = Copy(a);
            ExactMeshBoolean filtered, exact;
            AllocatedBy(a, b, ExactMeshBoolean.Operations.Union, false, out filtered);
            AllocatedBy(a, b, ExactMeshBoolean.Operations.Union, true, out exact);      // (compiles the path)
            long bytes = AllocatedBy(a, b, ExactMeshBoolean.Operations.Union, true, out exact);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, filtered.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, exact.Status);
            Assert.True(filtered.Result.TriangleCount > 300, $"{filtered.Result.TriangleCount} triangles");
            // every decision by the exact stages: the mesh of the filtered run, triangle for triangle
            Assert.Equal(Triangles(filtered.Result), Triangles(exact.Result));
            Assert.True(bytes < megabytes * 1048576L, $"{kind}: {bytes / 1048576.0:0.0} MB allocated with the filters off, bound {megabytes} MB");
        }

        // The three operations with the filters off give the filtered results (the stages decide intersections and
        // differences as well as unions).
        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void FiltersOff_SameResult_OtherOperations(ExactMeshBoolean.Operations op)
        {
            DMesh3 a = Shape("sheets3"), b = Copy(a);
            ExactMeshBoolean filtered, exact;
            AllocatedBy(a, b, op, false, out filtered);
            AllocatedBy(a, b, op, true, out exact);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, filtered.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, exact.Status);
            Assert.Equal(Triangles(filtered.Result), Triangles(exact.Result));
        }
    }
}
