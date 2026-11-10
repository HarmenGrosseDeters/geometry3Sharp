using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // The wrap's promise is "the input is strictly inside" (MeshAlphaWrap). The simplification of the wrap
    // (MeshRobustRepair.SimplifyAlphaWrap) is certified within half the offset of the wrap, not of the input, so before
    // it knew the input a collapse could put a triangle across a convex crease of the input. The oracle here is not the
    // pipeline's certification: exact triangle-triangle tests of every result triangle against every input triangle.
    // (public API only: the "New tests on base" job compiles this file against the base branch's library)
    public class AlphaWrapNoContactTests
    {
        static DMesh3 Plates() { return MeshRobustRepairTests.ThinPlates(6); }

        static DMesh3 Wrapped(DMesh3 input, out MeshRobustRepair r)
        {
            r = new MeshRobustRepair(input) { RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 24, WrapRelativeAlpha = 70, WrapRelativeOffset = 700 };
            Assert.True(r.Apply());
            Assert.True(r.UsedAlphaWrap && r.AlphaWrapSimplified);
            return r.Mesh;
        }

        /// <summary> result triangles that touch or cross an input triangle (exact predicates), candidates from the input's tree </summary>
        internal static int Crossings(DMesh3 result, DMesh3 input)
        {
            var tree = new DMeshAABBTree3(input, true);
            int n = 0;
            foreach (int tid in result.TriangleIndices()) {
                Triangle3d A = new Triangle3d(result.GetVertex(result.GetTriangle(tid).a), result.GetVertex(result.GetTriangle(tid).b), result.GetVertex(result.GetTriangle(tid).c));
                var ab = new AxisAlignedBox3d(A.V0); ab.Contain(A.V1); ab.Contain(A.V2);
                bool hit = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (bx, d) => !hit && bx.Min.x <= ab.Max.x && bx.Max.x >= ab.Min.x && bx.Min.y <= ab.Max.y && bx.Max.y >= ab.Min.y && bx.Min.z <= ab.Max.z && bx.Max.z >= ab.Min.z,
                    NextTriangleF = (it) => {
                        if (hit) return;
                        Triangle3d B = new Triangle3d(input.GetVertex(input.GetTriangle(it).a), input.GetVertex(input.GetTriangle(it).b), input.GetVertex(input.GetTriangle(it).c));
                        if (!ExactTriangleIntersection.IsDegenerate(ref B) && ExactTriangleIntersection.Intersects(ref A, ref B)) hit = true;
                    }
                });
                if (hit) n++;
            }
            return n;
        }

        // The wrap (alpha 1/100) of the plates and its simplification, as MeshRobustRepair runs them, without the solidify stage that the
        // end-to-end test below needs. clearOf null = the simplification of Alpha Wrap 2.0, which did not know the input.
        static DMesh3 Simplified(DMesh3 raw, double offset, DMesh3 clearOf)
        {
            DMesh3 s = MeshRobustRepair.SimplifyWithin(raw, 0.5 * offset, 0.5 * offset, true, () => false, out int sub,
                MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, 0, null, clearOf);
            Assert.NotNull(s);
            return s;
        }

        static DMesh3 Raw(DMesh3 input, out double offset)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 70, RelativeOffset = 700 };
            Assert.True(w.Compute(), w.Error);
            offset = w.UsedOffset;
            return w.Result;
        }

        // (shared by the tests of the class: one wrap, its simplification with and without the input)
        sealed class Shared
        {
            public DMesh3 Input = Plates(), Raw, WithInput;
            public readonly Lazy<DMesh3> WithoutInput;
            public double Offset;
            public Shared()
            {
                Raw = AlphaWrapNoContactTests.Raw(Input, out Offset);
                WithInput = Simplified(Raw, Offset, Input);
                WithoutInput = new Lazy<DMesh3>(() => Simplified(Raw, Offset, null));
            }
        }
        static readonly Lazy<Shared> Base = new Lazy<Shared>(() => new Shared());

        // Slow: the class's one wrap (alpha 1/70, the coarsest that still cuts the creases without the fix) is made by the first test to run, with the JIT
        [Fact, Trait("Category", "Slow")]
        public void Simplification_KeepsTheInputInside_WhichItDidNotBefore()
        {
            Shared b = Base.Value;
            // the power of the test: without the input the simplification cuts convex creases of it
            Assert.True(Crossings(b.WithoutInput.Value, b.Input) > 0);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.WithInput, true));
            Assert.Equal(0, Crossings(b.WithInput, b.Input));
        }

        [Fact]
        public void QualityPass_RejectsAnOperationThatTouchesTheInput()
        {
            // accept() of the pass, with a proposed triangle that a wrap triangle of this shape could become (no cheap wrap input makes the
            // pass cut a crease by itself; on the Thingi10K T1 meshes at alpha 1/300 it did, 30 triangles on 6 of 18 meshes)
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(0, 0, 0)), mesh.AppendVertex(new Vector3d(1, 0, 0)), mesh.AppendVertex(new Vector3d(0, 1, 0)));
            var pass = new AlphaWrapQualityPass(new DMesh3(mesh), mesh, 10) { CheckSelfIntersections = false };
            pass.Prepare();
            var ids = new List<Index3i> { new Index3i(0, 1, 2) };
            var tris = new List<Triangle3d> { new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)) };
            var removed = new HashSet<int>();
            Assert.True(pass.accept(ids, tris, removed));
            // an input triangle that stands through the proposed one (a crease of the input)
            var input = new DMesh3();
            input.AppendTriangle(input.AppendVertex(new Vector3d(0.2, 0.2, -1)), input.AppendVertex(new Vector3d(0.2, 0.2, 1)), input.AppendVertex(new Vector3d(0.7, 0.2, 0)));
            pass.KeepClearOf = new DMeshAABBTree3(input, true);
            Assert.False(pass.accept(ids, tris, removed));
            Assert.Equal(1, pass.RejectedByIntersection);
            // one that stays clear of it is accepted
            tris[0] = new Triangle3d(new Vector3d(0, 0, 5), new Vector3d(1, 0, 5), new Vector3d(0, 1, 5));
            Assert.True(pass.accept(ids, tris, removed));
        }

        [Theory, Trait("Category", "Slow")]     // (Slow: may make the class's shared wrap, see above)
        [InlineData("scale 2^10")]
        [InlineData("scale 1e-3")]
        public void Simplification_KeepsTheInputInside_UnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Invariance.Transformed(Plates(), s);
            DMesh3 raw = Raw(input, out double offset);
            DMesh3 simplified = Simplified(raw, offset, input);
            Assert.Equal(0, Crossings(simplified, input));
            // 2^k scales give the same result bit for bit
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(Base.Value.WithInput, Invariance.MappedBack(simplified, s)));
        }

        // Slow: end to end through MeshRobustRepair.Apply (the wiring of the input into the simplification: the solidify stage costs):
        // the result is the direct simplification with the input, bit for bit
        [Fact, Trait("Category", "Slow")]
        public void Apply_PassesTheInputToTheSimplification()
        {
            Shared b = Base.Value;
            DMesh3 simplified = Wrapped(b.Input, out MeshRobustRepair r);
            Assert.Equal("", r.ResultReason);
            Assert.Equal("", Invariance.Difference(b.WithInput, simplified));
        }
    }
}
