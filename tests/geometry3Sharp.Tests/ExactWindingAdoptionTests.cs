using System;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Public-API tests for opt-in exact winding adoption in WindingNumberOctree,
    /// MeshWindingNumberSolidify, MeshWindingNumberBoolean, MeshBatchQueries, and MeshDeviation,
    /// plus large-boundary open-mesh ExactWindingNumber threshold parity.
    /// </summary>
    public class ExactWindingAdoptionTests
    {
        static DMesh3 Sphere(int edgeVertices = 10, double radius = 1.0, Vector3d center = default(Vector3d))
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            if (center != Vector3d.Zero)
                MeshTransforms.Translate(m, center);
            return m;
        }

        static DMesh3 Box(double size = 2.0, Vector3d center = default(Vector3d))
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(center, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, 0.5 * size * Vector3d.One) }.Generate().MakeDMesh();
            return m;
        }

        static DMesh3 Unweld(DMesh3 src)
        {
            DMesh3 dst = new DMesh3();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in src.TriangleIndices()) {
                src.GetTriVertices(tid, ref a, ref b, ref c);
                dst.AppendTriangle(dst.AppendVertex(a), dst.AppendVertex(b), dst.AppendVertex(c));
            }
            return dst;
        }

        // Open subdivided wavy patch / hemisphere with >= 600 exterior boundary edges to exercise BoundaryTree.
        static DMesh3 LargeBoundaryOpenGrid(int n = 160)
        {
            DMesh3 m = new DMesh3();
            int[,] vid = new int[n + 1, n + 1];
            for (int iy = 0; iy <= n; ++iy) {
                double y = -1.0 + 2.0 * iy / n;
                for (int ix = 0; ix <= n; ++ix) {
                    double x = -1.0 + 2.0 * ix / n;
                    double r2 = x * x + y * y;
                    double z = 0.25 * Math.Cos(1.5 * r2) - 0.1 * x * y;
                    vid[ix, iy] = m.AppendVertex(new Vector3d(x, y, z));
                }
            }
            for (int iy = 0; iy < n; ++iy) {
                for (int ix = 0; ix < n; ++ix) {
                    int v00 = vid[ix, iy], v10 = vid[ix + 1, iy], v01 = vid[ix, iy + 1], v11 = vid[ix + 1, iy + 1];
                    m.AppendTriangle(v00, v10, v11);
                    m.AppendTriangle(v00, v11, v01);
                }
            }
            return m;
        }

        static bool AllVerticesFinite(DMesh3 m)
        {
            foreach (int vid in m.VertexIndices())
                if (!m.GetVertex(vid).IsFinite)
                    return false;
            return true;
        }

        static bool BitEqualMeshes(DMesh3 a, DMesh3 b)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount)
                return false;
            if (a.MaxVertexID != b.MaxVertexID || a.MaxTriangleID != b.MaxTriangleID)
                return false;
            for (int vid = 0; vid < a.MaxVertexID; ++vid) {
                if (a.IsVertex(vid) != b.IsVertex(vid)) return false;
                if (!a.IsVertex(vid)) continue;
                Vector3d va = a.GetVertex(vid), vb = b.GetVertex(vid);
                if (BitConverter.DoubleToInt64Bits(va.x) != BitConverter.DoubleToInt64Bits(vb.x) ||
                    BitConverter.DoubleToInt64Bits(va.y) != BitConverter.DoubleToInt64Bits(vb.y) ||
                    BitConverter.DoubleToInt64Bits(va.z) != BitConverter.DoubleToInt64Bits(vb.z))
                    return false;
            }
            for (int tid = 0; tid < a.MaxTriangleID; ++tid) {
                if (a.IsTriangle(tid) != b.IsTriangle(tid)) return false;
                if (!a.IsTriangle(tid)) continue;
                if (a.GetTriangle(tid) != b.GetTriangle(tid)) return false;
            }
            return true;
        }

        sealed class ConstantOffsetTree : DMeshAABBTree3
        {
            public ConstantOffsetTree(DMesh3 m) : base(m, true) { }
            public override double WindingNumber(Vector3d p) { return 0.75; }
        }

        [Theory]
        [InlineData(false, MeshWindingNumberSolidify.Methods.DualContouring)]
        [InlineData(true, MeshWindingNumberSolidify.Methods.DualContouring)]
        [InlineData(false, MeshWindingNumberSolidify.Methods.MarchingCubes)]
        public void MeshWindingNumberSolidify_UseExactWinding_ProducesCleanClosedManifoldSolid(
            bool adaptive, MeshWindingNumberSolidify.Methods method)
        {
            DMesh3 sphere = Sphere(8, 1.0);
            double expectedVol = MeshMeasurements.VolumeArea(sphere, sphere.TriangleIndices(), sphere.GetVertex).x;

            foreach (DMesh3 input in new[] { sphere, Unweld(sphere) }) {
                var solid = new MeshWindingNumberSolidify(input) {
                    Method = method,
                    Adaptive = adaptive,
                    GridResolution = 24,
                    UseExactWinding = true
                };
                Assert.True(solid.Compute());
                Assert.NotNull(solid.Result);
                Assert.True(solid.Result.TriangleCount > 0);
                Assert.True(solid.Result.IsClosed());
                Assert.True(AllVerticesFinite(solid.Result));
                Assert.Equal(0, solid.NonManifoldTriangles);

                double vol = MeshMeasurements.VolumeArea(solid.Result, solid.Result.TriangleIndices(), solid.Result.GetVertex).x;
                Assert.True(Math.Abs(vol - expectedVol) / expectedVol < 0.08, $"vol={vol} vs expected={expectedVol}");
            }
        }

        [Fact]
        public void MeshWindingNumberSolidify_UseExactWinding_ScaleInvarianceReuseAndDeterminism()
        {
            DMesh3 box = Box(2.0);
            var s1 = new MeshWindingNumberSolidify(box) {
                Adaptive = true,
                GridResolution = 20,
                UseExactWinding = true,
                CleanupResult = false,
                ReduceTolerance = 0
            };
            Assert.True(s1.Compute());
            DMesh3 first = new DMesh3(s1.Result);

            // Reuse: toggle UseExactWinding false -> true and verify bit-identical determinism
            s1.UseExactWinding = false;
            Assert.True(s1.Compute());
            Assert.True(s1.Result.IsClosed());

            s1.UseExactWinding = true;
            Assert.True(s1.Compute());
            Assert.True(BitEqualMeshes(first, s1.Result), "Repeated UseExactWinding runs must be bit-identical");

            // Power-of-two scale invariance (2^-20 and 2^20 both pass through the scaled inner copy)
            foreach (int exp in new[] { -20, 20 }) {
                double sc = Math.Pow(2.0, exp);
                DMesh3 scaled = new DMesh3(box);
                MeshTransforms.Scale(scaled, sc);
                var ss = new MeshWindingNumberSolidify(scaled) {
                    Adaptive = true,
                    GridResolution = 20,
                    UseExactWinding = true,
                    CleanupResult = false,
                    ReduceTolerance = 0
                };
                Assert.True(ss.Compute());
                Assert.True(ss.Result.IsClosed());
                Assert.True(AllVerticesFinite(ss.Result));
                double vol = MeshMeasurements.VolumeArea(ss.Result, ss.Result.TriangleIndices(), ss.Result.GetVertex).x;
                double expectedVol = 8.0 * sc * sc * sc;
                Assert.True(Math.Abs(vol - expectedVol) / expectedVol < 0.05, $"exp={exp}: vol={vol} vs {expectedVol}");
            }

            // Cancellation check
            var cancelled = new MeshWindingNumberSolidify(box) {
                Adaptive = false,
                GridResolution = 20,
                UseExactWinding = true,
                CancelF = () => true
            };
            Assert.False(cancelled.Compute());
            Assert.Null(cancelled.Result);
        }

        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union, 12.0)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection, 4.0)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference, 4.0)]
        public void MeshWindingNumberBoolean_UseExactWinding_ProducesAccurateCleanResult(
            MeshWindingNumberBoolean.Operations op, double expectedVol)
        {
            DMesh3 a = Box(2.0, new Vector3d(-0.5, 0, 0));
            DMesh3 b = Box(2.0, new Vector3d(0.5, 0, 0));

            var opExact = new MeshWindingNumberBoolean(a, b, op) {
                GridResolution = 24,
                UseExactWinding = true
            };
            Assert.True(opExact.Compute());
            Assert.NotNull(opExact.Result);
            Assert.True(opExact.Result.IsClosed());
            Assert.True(AllVerticesFinite(opExact.Result));

            double vol = MeshMeasurements.VolumeArea(opExact.Result, opExact.Result.TriangleIndices(), opExact.Result.GetVertex).x;
            Assert.True(Math.Abs(vol - expectedVol) / expectedVol < 0.05, $"{op}: vol={vol} vs expected={expectedVol}");

            // Determinism & reuse on the same instance
            DMesh3 first = new DMesh3(opExact.Result);
            Assert.True(opExact.Compute());
            Assert.True(BitEqualMeshes(first, opExact.Result));
        }

        [Fact]
        public void WindingNumberOctree_UseExactWinding_MatchesExactAndInvalidatesCacheOnToggle()
        {
            DMesh3 sphere = Sphere(8, 1.0);
            DMesh3 unwelded = Unweld(sphere);
            var tree = new DMeshAABBTree3(unwelded, true);

            var octree = new WindingNumberOctree(unwelded, tree) {
                MaxDepth = 4,
                UseExactWinding = true
            };
            octree.Build();
            Assert.True(octree.SurfaceCells.Count > 0);
            Assert.Equal(tree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)), octree.Evaluate(new Vector3d(0.1, 0.2, 0.3)));

            // BuildOnGrid with toggled UseExactWinding must invalidate the previous node cache
            int exactEvals = octree.WindingEvaluations;
            octree.UseExactWinding = false;
            octree.BuildOnGrid(octree.Origin, octree.LeafCellSize, octree.MaxDepth);
            Assert.True(octree.WindingEvaluations > 0);
            Assert.Equal(tree.FastWindingNumber(new Vector3d(0.1, 0.2, 0.3)), octree.Evaluate(new Vector3d(0.1, 0.2, 0.3)));
            Assert.True(exactEvals > 0);
        }

        [Fact]
        public void MeshBatchQueries_ExactWindingNumber_MatchesSinglePointAcrossClosedOpenUnweldedAndLargeBoundary()
        {
            DMesh3 closed = Sphere(10, 1.0);
            DMesh3 unwelded = Unweld(closed);
            DMesh3 openLarge = LargeBoundaryOpenGrid(160); // 640 exterior boundary edges (>= 500 -> BoundaryTree)

            var rnd = new Random(71);
            var pts = new Vector3d[600];
            for (int i = 0; i < pts.Length - 2; ++i)
                pts[i] = new Vector3d(rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5);
            pts[pts.Length - 2] = new Vector3d(double.NaN, 0.0, 0.0);
            pts[pts.Length - 1] = new Vector3d(0.0, double.PositiveInfinity, 0.0);

            foreach (DMesh3 m in new[] { closed, unwelded, openLarge }) {
                var tree = new DMeshAABBTree3(m, true);
                var par = new InsideStatus[pts.Length];
                var ser = new InsideStatus[pts.Length];

                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, par, InsideMethod.ExactWindingNumber,
                    new BatchOptions { Parallel = true, SortForCoherence = true }));
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, ser, InsideMethod.ExactWindingNumber,
                    new BatchOptions { Parallel = false, SortForCoherence = false }));

                for (int i = 0; i < pts.Length; ++i) {
                    Assert.Equal(ser[i], par[i]);
                    if (!pts[i].IsFinite) {
                        Assert.Equal(InsideStatus.Invalid, par[i]);
                    } else {
                        InsideStatus expected = tree.WindingNumber(pts[i]) > 0.5 ? InsideStatus.Inside : InsideStatus.Outside;
                        Assert.Equal(expected, par[i]);
                    }
                }
            }

            // Empty mesh -> Outside for finite, Invalid for non-finite
            var emptyTree = new DMeshAABBTree3(new DMesh3(), true);
            var emptyRes = new InsideStatus[pts.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(emptyTree, pts, emptyRes, InsideMethod.ExactWindingNumber));
            Assert.Equal(InsideStatus.Outside, emptyRes[0]);
            Assert.Equal(InsideStatus.Invalid, emptyRes[pts.Length - 2]);

            // Subclass overriding WindingNumber must be respected
            var subTree = new ConstantOffsetTree(closed);
            var subRes = new InsideStatus[4];
            var farPts = new[] { new Vector3d(10, 0, 0), new Vector3d(20, 0, 0), new Vector3d(30, 0, 0), new Vector3d(double.NaN, 0, 0) };
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(subTree, farPts, subRes, InsideMethod.ExactWindingNumber));
            Assert.Equal(InsideStatus.Inside, subRes[0]);
            Assert.Equal(InsideStatus.Invalid, subRes[3]);

            // Cancellation check
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Equal(BatchStatus.Cancelled, MeshBatchQueries.IsInsideBatch(
                new DMeshAABBTree3(closed, true), pts, new InsideStatus[pts.Length],
                InsideMethod.ExactWindingNumber, new BatchOptions { Cancel = cts.Token }));
        }

        [Fact]
        public void MeshDeviation_ExactWindingNumber_MatchesExactAcrossClosedInvertedLargeBoundaryAndScales()
        {
            DMesh3 refSphere = Sphere(10, 1.0);
            DMesh3 querySphere = Sphere(8, 0.85, new Vector3d(0.25, 0.0, 0.0));

            // 1. Closed reference: ExactWindingNumber agrees with PseudoNormal on every vertex off the surface
            var devPseudo = new MeshDeviation(querySphere, refSphere) { Sign = MeshDeviation.Signs.PseudoNormal };
            var devExact = new MeshDeviation(querySphere, refSphere) { Sign = MeshDeviation.Signs.ExactWindingNumber };
            Assert.True(devPseudo.Compute());
            Assert.True(devExact.Compute());
            foreach (int vid in querySphere.VertexIndices()) {
                Assert.Equal(devPseudo.VertexDeviation[vid], devExact.VertexDeviation[vid], 12);
            }

            // 2. Inverted closed reference: |w| > 0.5 still marks interior as negative
            DMesh3 invertedRef = new DMesh3(refSphere);
            invertedRef.ReverseOrientation(false);
            var devInv = new MeshDeviation(querySphere, invertedRef) { Sign = MeshDeviation.Signs.ExactWindingNumber };
            Assert.True(devInv.Compute());
            foreach (int vid in querySphere.VertexIndices()) {
                Assert.Equal(devExact.VertexDeviation[vid], devInv.VertexDeviation[vid], 12);
            }

            // 3. Large-boundary open reference (exercises BoundaryTree via EvaluateAbsAbove)
            DMesh3 openLarge = LargeBoundaryOpenGrid(160);
            var openTree = new DMeshAABBTree3(openLarge, true);
            var devOpen = new MeshDeviation(querySphere, openLarge) { Sign = MeshDeviation.Signs.ExactWindingNumber, ReferenceSpatial = openTree };
            Assert.True(devOpen.Compute());
            foreach (int vid in querySphere.VertexIndices()) {
                Vector3d p = querySphere.GetVertex(vid);
                double d = Math.Abs(devOpen.VertexDeviation[vid]);
                double expectedSign = Math.Abs(openTree.WindingNumber(p)) > 0.5 ? -d : d;
                Assert.Equal(expectedSign, devOpen.VertexDeviation[vid], 12);
            }

            // 4. Scale invariance (2^40) and UTM translation
            double sc = Math.Pow(2.0, 40);
            DMesh3 qScaled = new DMesh3(querySphere), rScaled = new DMesh3(refSphere);
            MeshTransforms.Scale(qScaled, sc);
            MeshTransforms.Scale(rScaled, sc);
            var devScaled = new MeshDeviation(qScaled, rScaled) { Sign = MeshDeviation.Signs.ExactWindingNumber };
            Assert.True(devScaled.Compute());
            foreach (int vid in querySphere.VertexIndices()) {
                Assert.True(Math.Abs(devScaled.VertexDeviation[vid] / sc - devExact.VertexDeviation[vid]) < 1e-12);
            }

            Vector3d utm = new Vector3d(500000.0, 5700000.0, 200.0);
            DMesh3 qUtm = new DMesh3(querySphere), rUtm = new DMesh3(refSphere);
            MeshTransforms.Translate(qUtm, utm);
            MeshTransforms.Translate(rUtm, utm);
            var devUtm = new MeshDeviation(qUtm, rUtm) { Sign = MeshDeviation.Signs.ExactWindingNumber };
            Assert.True(devUtm.Compute());
            foreach (int vid in querySphere.VertexIndices()) {
                Assert.True(Math.Abs(devUtm.VertexDeviation[vid] - devExact.VertexDeviation[vid]) < 1e-7);
            }

            // 5. Subclass ReferenceSpatial overriding WindingNumber must be respected
            var devSub = new MeshDeviation(querySphere, refSphere) {
                Sign = MeshDeviation.Signs.ExactWindingNumber,
                ReferenceSpatial = new ConstantOffsetTree(refSphere)
            };
            Assert.True(devSub.Compute());
            foreach (int vid in querySphere.VertexIndices())
                Assert.True(devSub.VertexDeviation[vid] <= 0.0, "ConstantOffsetTree returns 0.75 (> 0.5), so every vertex must be signed negative");

            // 6. Reuse: toggling Sign on the same instance works cleanly
            devExact.Sign = MeshDeviation.Signs.Unsigned;
            Assert.True(devExact.Compute());
            foreach (int vid in querySphere.VertexIndices())
                Assert.True(devExact.VertexDeviation[vid] >= 0.0);
            devExact.Sign = MeshDeviation.Signs.ExactWindingNumber;
            Assert.True(devExact.Compute());
            foreach (int vid in querySphere.VertexIndices())
                Assert.Equal(devPseudo.VertexDeviation[vid], devExact.VertexDeviation[vid], 12);
        }

        [Fact]
        public void OpenMeshSolidifyAndUtmAdoption_UseExactWinding_CleanAndConsistent()
        {
            // Open sphere cap with UseExactWinding = true exercises NarrowBand + ExactWindingCache in compute_grid
            DMesh3 openSphere = Sphere(10, 1.0);
            foreach (int tid in openSphere.TriangleIndices().ToArray())
                if (openSphere.GetTriCentroid(tid).z > 0.65)
                    openSphere.RemoveTriangle(tid, true, false);
            var openSolid = new MeshWindingNumberSolidify(openSphere) {
                Adaptive = false,
                GridResolution = 24,
                UseExactWinding = true
            };
            Assert.True(openSolid.Compute());
            Assert.NotNull(openSolid.Result);
            Assert.True(openSolid.Result.IsClosed());
            Assert.True(AllVerticesFinite(openSolid.Result));

            // UTM translation for Solidify, Boolean, and large-boundary MeshBatchQueries (BoundaryTree at UTM)
            Vector3d utm = new Vector3d(500000.0, 5700000.0, 200.0);
            DMesh3 boxA = Box(2.0, utm + new Vector3d(-0.5, 0, 0));
            DMesh3 boxB = Box(2.0, utm + new Vector3d(0.5, 0, 0));

            var solidUtm = new MeshWindingNumberSolidify(boxA) { Adaptive = false, GridResolution = 20, UseExactWinding = true };
            Assert.True(solidUtm.Compute());
            Assert.True(solidUtm.Result.IsClosed());
            double sVol = MeshMeasurements.VolumeArea(solidUtm.Result, solidUtm.Result.TriangleIndices(), solidUtm.Result.GetVertex).x;
            Assert.True(Math.Abs(sVol - 8.0) / 8.0 < 0.08, $"UTM solidify vol={sVol}");

            var boolUtm = new MeshWindingNumberBoolean(boxA, boxB, MeshWindingNumberBoolean.Operations.Union) {
                GridResolution = 20,
                UseExactWinding = true
            };
            Assert.True(boolUtm.Compute());
            Assert.True(boolUtm.Result.IsClosed());
            double bVol = MeshMeasurements.VolumeArea(boolUtm.Result, boolUtm.Result.TriangleIndices(), boolUtm.Result.GetVertex).x;
            Assert.True(Math.Abs(bVol - 12.0) / 12.0 < 0.08, $"UTM boolean vol={bVol}");

            DMesh3 openUtm = LargeBoundaryOpenGrid(160);
            MeshTransforms.Translate(openUtm, utm);
            var utmTree = new DMeshAABBTree3(openUtm, true);
            var rnd = new Random(199);
            var pts = new Vector3d[200];
            for (int i = 0; i < pts.Length; ++i)
                pts[i] = utm + new Vector3d(rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5);
            var res = new InsideStatus[pts.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(utmTree, pts, res, InsideMethod.ExactWindingNumber));
            for (int i = 0; i < pts.Length; ++i) {
                InsideStatus exp = utmTree.WindingNumber(pts[i]) > 0.5 ? InsideStatus.Inside : InsideStatus.Outside;
                Assert.Equal(exp, res[i]);
            }
        }
    }
}
