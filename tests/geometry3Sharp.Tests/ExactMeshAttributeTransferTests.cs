using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for <see cref="ExactMeshAttributeTransfer"/>: local-frame barycentric accuracy at UTM 1e7 and extreme
    /// 2^k scales, linear UV/normal/color transfer across booleans, normal flipping on Difference cavities, and
    /// <see cref="AttributeTransferOptions.SplitAttributeSeams"/> preventing attribute bleeding across seams.
    /// </summary>
    public class ExactMeshAttributeTransferTests
    {
        static DMesh3 BoxWithLinearAttributes(double x0, double y0, double z0, double x1, double y1, double z1, int groupId,
            Func<Vector3d, Vector2f> uvF, Func<Vector3d, Vector3f> colF)
        {
            var g = new TrivialBox3Generator {
                Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)),
                    new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))),
                NoSharedVertices = true // 24 vertices: crisp per-face normals
            };
            DMesh3 m = g.Generate().MakeDMesh();
            m.EnableTriangleGroups(groupId);
            m.EnableVertexUVs(Vector2f.Zero);
            m.EnableVertexColors(Vector3f.One);
            m.EnableVertexNormals(Vector3f.AxisY);
            foreach (int tid in m.TriangleIndices()) {
                Vector3d fn = m.GetTriNormal(tid);
                Index3i tv = m.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    int vid = tv[k];
                    Vector3d p = m.GetVertex(vid);
                    m.SetVertexNormal(vid, new Vector3f((float)fn.x, (float)fn.y, (float)fn.z));
                    m.SetVertexUV(vid, uvF(p));
                    m.SetVertexColor(vid, colF(p));
                }
            }
            return m;
        }

        [Fact]
        public void LocalBarycentric_LinearFields_AccurateTo1e12_AtOriginUtm1e7AndExtremeScales()
        {
            // Dyadic offsets so p = a + w1*(b-a) + w2*(c-a) is represented with zero quantization error even at UTM 5.7e6.
            Vector3d a0 = new Vector3d(0.25, 0.5, 0.25);
            Vector3d b0 = new Vector3d(2.25, 0.75, 0.5);
            Vector3d c0 = new Vector3d(0.75, 2.5, 1.25);
            Vector2d uvA = new Vector2d(0.25, 0.75);
            Vector2d uvB = new Vector2d(1.75, 0.50);
            Vector2d uvC = new Vector2d(0.50, 2.25);
            Vector3d colA = new Vector3d(0.1, 0.2, 0.3);
            Vector3d colB = new Vector3d(0.8, 0.4, 0.2);
            Vector3d colC = new Vector3d(0.3, 0.9, 0.7);
            Vector3d nA = new Vector3d(0.2, 0.3, 0.9).Normalized;
            Vector3d nB = new Vector3d(0.1, 0.5, 0.85).Normalized;
            Vector3d nC = new Vector3d(-0.2, 0.2, 0.95).Normalized;

            double[] scales = { Math.Pow(2, -20), 1.0, Math.Pow(2, 20) };
            Vector3d[] shifts = { Vector3d.Zero, new Vector3d(600000.0, 5700000.0, 100.0) };
            double[,] testWeights = {
                { 0.25, 0.25, 0.5 },
                { 0.0, 0.375, 0.625 },
                { 1.0, 0.0, 0.0 },
                { 0.125, 0.625, 0.25 }
            };

            foreach (double s in scales) {
                foreach (Vector3d shift in shifts) {
                    if (s < 1e-3 && shift.Length > 1.0)
                        continue;
                    Vector3d a = a0 * s + shift;
                    Vector3d b = b0 * s + shift;
                    Vector3d c = c0 * s + shift;
                    for (int i = 0; i < testWeights.GetLength(0); ++i) {
                        double w0 = testWeights[i, 0], w1 = testWeights[i, 1], w2 = testWeights[i, 2];
                        Vector3d p = a + w1 * (b - a) + w2 * (c - a);

                        Vector3d gotW = ExactMeshAttributeTransfer.LocalBarycentric(p, a, b, c, clamp: false);
                        Assert.True(Math.Abs(gotW.x - w0) < 1e-12 && Math.Abs(gotW.y - w1) < 1e-12 && Math.Abs(gotW.z - w2) < 1e-12,
                            $"s={s}, shift={shift}: got {gotW} vs ({w0},{w1},{w2})");

                        Vector2d expectedUV = w0 * uvA + w1 * uvB + w2 * uvC;
                        Vector2d gotUV = ExactMeshAttributeTransfer.InterpolateCornerUV(p, a, b, c, uvA, uvB, uvC);
                        Assert.True((gotUV - expectedUV).Length < 1e-12);

                        Vector3d expectedCol = w0 * colA + w1 * colB + w2 * colC;
                        Vector3d gotCol = ExactMeshAttributeTransfer.InterpolateCornerColor(p, a, b, c, colA, colB, colC);
                        Assert.True((gotCol - expectedCol).Length < 1e-12);

                        Vector3d expectedN = (w0 * nA + w1 * nB + w2 * nC).Normalized;
                        Vector3d gotN = ExactMeshAttributeTransfer.InterpolateCornerNormal(p, a, b, c, nA, nB, nC, reversed: false);
                        Vector3d gotNRev = ExactMeshAttributeTransfer.InterpolateCornerNormal(p, a, b, c, nA, nB, nC, reversed: true);
                        Assert.True((gotN - expectedN).Length < 1e-12);
                        Assert.True((gotNRev + expectedN).Length < 1e-12);
                    }
                }
            }
        }

        [Fact]
        public void EvaluateCorners_LinearFieldsAtUtm1e7_AndDifferenceFlipsToolNormals()
        {
            Vector3d utm = new Vector3d(600000.0, 5700000.0, 100.0);
            // Linear UV field in local coordinates around utm: u = (x - utm.x)/4, v = (y - utm.y + z - utm.z)/8
            Func<Vector3d, Vector2f> uvF = p => new Vector2f((float)((p.x - utm.x) * 0.25), (float)(((p.y - utm.y) + (p.z - utm.z)) * 0.125));
            Func<Vector3d, Vector3f> colF = p => new Vector3f((float)((p.x - utm.x) * 0.25), (float)((p.y - utm.y) * 0.25), (float)((p.z - utm.z) * 0.25));

            DMesh3 a = BoxWithLinearAttributes(utm.x, utm.y, utm.z, utm.x + 2, utm.y + 2, utm.z + 2, 1, uvF, colF);
            DMesh3 b = BoxWithLinearAttributes(utm.x + 1, utm.y + 1, utm.z + 1, utm.x + 3, utm.y + 3, utm.z + 3, 2, uvF, colF);

            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(eb.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));

            CornerAttributes corners = ExactMeshAttributeTransfer.EvaluateCorners(eb.Result, eb.ResultProvenance, new[] { a, b });
            Assert.NotNull(corners.UVs);
            Assert.NotNull(corners.Normals);
            Assert.NotNull(corners.Colors);

            int checkedCavity = 0;
            foreach (int tid in eb.Result.TriangleIndices()) {
                TriangleProvenance tp = eb.ResultProvenance[tid];
                Vector3d faceNormal = eb.Result.GetTriNormal(tid);
                Index3i tv = eb.Result.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    int slot = 3 * tid + k;
                    Assert.True(corners.HasUV[slot]);
                    Assert.True(corners.HasNormal[slot]);
                    Assert.True(corners.HasColor[slot]);
                    Vector3d p = eb.Result.GetVertex(tv[k]);

                    Vector2f expUv = uvF(p);
                    Vector2d gotUv = corners.UVs[slot];
                    // DMesh3 stores vertex UVs in float (~1e-7); local-frame double barycentric adds < 1e-12.
                    Assert.True(Math.Abs(gotUv.x - expUv.x) < 1e-6 && Math.Abs(gotUv.y - expUv.y) < 1e-6,
                        $"UTM UV mismatch at {p}: got {gotUv} vs {expUv}");

                    Vector3f expCol = colF(p);
                    Vector3d gotCol = corners.Colors[slot];
                    Assert.True(Math.Abs(gotCol.x - expCol.x) < 1e-6 && Math.Abs(gotCol.y - expCol.y) < 1e-6 && Math.Abs(gotCol.z - expCol.z) < 1e-6);

                    // Box normals are exact per-face normals; for Difference tool (MeshIndex == 1, Reversed == true),
                    // negated tool normal MUST align with the outward face normal of the Difference result!
                    Assert.True(corners.Normals[slot].Dot(faceNormal) > 0.999999,
                        $"Normal misaligned on tri {tid} (mesh {tp.MeshIndex}, rev {tp.Reversed}): {corners.Normals[slot]} vs {faceNormal}");
                    if (tp.MeshIndex == 1)
                        checkedCavity++;
                }
            }
            Assert.True(checkedCavity > 0);
        }

        [Fact]
        public void Transfer_SplitAttributeSeams_PreventsBleedingAcrossMaterialUVAndCreaseSeams()
        {
            // Cube A: constant UV (0.1, 0.1), red (1, 0, 0), group 1
            // Cube B: constant UV (0.9, 0.9), blue (0, 0, 1), group 2
            DMesh3 a = BoxWithLinearAttributes(0, 0, 0, 2, 2, 2, 1, _ => new Vector2f(0.1f, 0.1f), _ => new Vector3f(1, 0, 0));
            DMesh3 b = BoxWithLinearAttributes(1, 1, 1, 3, 3, 3, 2, _ => new Vector2f(0.9f, 0.9f), _ => new Vector3f(0, 0, 1));

            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(eb.Compute());
            DMesh3 beforePositions = new DMesh3(eb.Result);
            int origVertices = eb.Result.VertexCount;

            var opts = new AttributeTransferOptions { SplitAttributeSeams = true };
            int splits = ExactMeshAttributeTransfer.Transfer(eb, opts);
            Assert.True(splits > 0, "Expected vertices along sharp cube creases and A/B material seam to split.");
            Assert.Equal(origVertices + splits, eb.Result.VertexCount);
            Assert.True(eb.Result.HasVertexUVs);
            Assert.True(eb.Result.HasVertexNormals);
            Assert.True(eb.Result.HasVertexColors);

            // Every triangle corner must match its source mesh's exact UV, color, and face normal with ZERO bleeding,
            // and every triangle's 3D vertex positions must remain bit-identical to before splitting!
            foreach (int tid in eb.Result.TriangleIndices()) {
                TriangleProvenance tp = eb.ResultProvenance[tid];
                Index3i tvAfter = eb.Result.GetTriangle(tid);
                Index3i tvBefore = beforePositions.GetTriangle(tid);
                Vector3d faceNormal = eb.Result.GetTriNormal(tid);
                Vector2f expUV = tp.MeshIndex == 0 ? new Vector2f(0.1f, 0.1f) : new Vector2f(0.9f, 0.9f);
                Vector3f expCol = tp.MeshIndex == 0 ? new Vector3f(1, 0, 0) : new Vector3f(0, 0, 1);
                for (int k = 0; k < 3; ++k) {
                    Assert.Equal(beforePositions.GetVertex(tvBefore[k]), eb.Result.GetVertex(tvAfter[k]));
                    Vector2f uv = eb.Result.GetVertexUV(tvAfter[k]);
                    Vector3f col = eb.Result.GetVertexColor(tvAfter[k]);
                    Vector3f n = eb.Result.GetVertexNormal(tvAfter[k]);
                    Assert.True(Math.Abs(uv.x - expUV.x) < 1e-5 && Math.Abs(uv.y - expUV.y) < 1e-5,
                        $"UV bled on tri {tid}: got {uv} vs {expUV}");
                    Assert.True(Math.Abs(col.x - expCol.x) < 1e-5 && Math.Abs(col.y - expCol.y) < 1e-5 && Math.Abs(col.z - expCol.z) < 1e-5,
                        $"Color bled on tri {tid}: got {col} vs {expCol}");
                    Assert.True(new Vector3d(n.x, n.y, n.z).Dot(faceNormal) > 0.9999,
                        $"Normal bled across hard crease on tri {tid}: got {n} vs {faceNormal}");
                }
            }
        }

        [Fact]
        public void Transfer_UnsplitMode_PreservesWatertightTopology_AndGuardsInvalidCall()
        {
            DMesh3 a = BoxWithLinearAttributes(0, 0, 0, 2, 2, 2, 1, p => new Vector2f((float)p.x, (float)p.y), _ => new Vector3f(0.5f, 0.5f, 0.5f));
            DMesh3 b = BoxWithLinearAttributes(1, 1, 1, 3, 3, 3, 2, p => new Vector2f((float)p.x, (float)p.y), _ => new Vector3f(0.5f, 0.5f, 0.5f));

            // Guard: Transfer(eb) throws clearly when TrackFaceProvenance was false
            var ebNoProv = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(ebNoProv.Compute());
            Assert.Throws<InvalidOperationException>(() => ExactMeshAttributeTransfer.Transfer(ebNoProv));

            // Unsplit mode keeps mesh closed and manifold (0 splits)
            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { TrackFaceProvenance = true };
            Assert.True(eb.Compute());
            int vBefore = eb.Result.VertexCount;
            int splits = ExactMeshAttributeTransfer.Transfer(eb, new AttributeTransferOptions { SplitAttributeSeams = false });
            Assert.Equal(0, splits);
            Assert.Equal(vBefore, eb.Result.VertexCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
            Assert.True(eb.Result.HasVertexUVs);
            Assert.True(eb.Result.HasVertexNormals);
            Assert.True(eb.Result.HasVertexColors);
        }

        [Fact]
        public void LocalBarycentric_SliverSubnormalAndExtremeNormals_ExactAndFinite()
        {
            // High-aspect right-angled sliver (1 x 1e-16): must NOT falsely fall back to collinear edge
            Vector3d a = Vector3d.Zero, b = new Vector3d(1.0, 0, 0), c = new Vector3d(0, 1e-16, 0);
            Vector3d wc = ExactMeshAttributeTransfer.LocalBarycentric(c, a, b, c, clamp: false);
            Assert.InRange(wc.x, -1e-12, 1e-12);
            Assert.InRange(wc.y, -1e-12, 1e-12);
            Assert.InRange(wc.z, 1.0 - 1e-12, 1.0 + 1e-12);

            // Subnormal coordinates (~1e-315): power-of-two scaling prevents 1/scale = Inf => NaN
            double sub = 1e-315;
            Vector3d sa = new Vector3d(0, 0, 0), sb = new Vector3d(4 * sub, 0, 0), sc = new Vector3d(0, 4 * sub, 0);
            Vector3d sp = new Vector3d(sub, 2 * sub, 0);
            Vector3d ws = ExactMeshAttributeTransfer.LocalBarycentric(sp, sa, sb, sc, clamp: true);
            Assert.False(double.IsNaN(ws.x) || double.IsNaN(ws.y) || double.IsNaN(ws.z));
            Assert.InRange(ws.x, 0.25 - 1e-6, 0.25 + 1e-6);
            Assert.InRange(ws.y, 0.25 - 1e-6, 0.25 + 1e-6);
            Assert.InRange(ws.z, 0.50 - 1e-6, 0.50 + 1e-6);

            // Degenerate coincident/collinear triangles: finite weights, never NaN
            Vector3d wd = ExactMeshAttributeTransfer.LocalBarycentric(new Vector3d(0.5, 0, 0), a, a, b, clamp: true);
            Assert.False(double.IsNaN(wd.x) || double.IsNaN(wd.y) || double.IsNaN(wd.z));
            Vector3d wCol = ExactMeshAttributeTransfer.LocalBarycentric(new Vector3d(0.5, 0, 0), a, b, new Vector3d(2.0, 0, 0), clamp: true);
            Assert.False(double.IsNaN(wCol.x) || double.IsNaN(wCol.y) || double.IsNaN(wCol.z));

            // Extreme-scale normals (1e-200 and 1e+200) normalize to unit length without underflow/overflow
            Vector3d nTiny = ExactMeshAttributeTransfer.InterpolateCornerNormal(
                new Vector3d(0.25, 0.25, 0), a, b, new Vector3d(0, 1, 0),
                new Vector3d(0, 0, 1e-200), new Vector3d(0, 0, 1e-200), new Vector3d(0, 0, 1e-200), reversed: false);
            Assert.InRange(nTiny.z, 1.0 - 1e-12, 1.0 + 1e-12);
            Vector3d nHuge = ExactMeshAttributeTransfer.InterpolateCornerNormal(
                new Vector3d(0.25, 0.25, 0), a, b, new Vector3d(0, 1, 0),
                new Vector3d(0, 0, 1e200), new Vector3d(0, 0, 1e200), new Vector3d(0, 0, 1e200), reversed: true);
            Assert.InRange(nHuge.z, -1.0 - 1e-12, -1.0 + 1e-12);
        }

        [Fact]
        public void Transfer_MixedInputs_OptionValidation_AndCornerBoundsCheck()
        {
            // Input A has NO vertex attributes; input B has UVs, normals, and colors.
            var gen = new TrivialBox3Generator { Box = new Box3d(new Vector3d(1, 1, 1), Vector3d.One), NoSharedVertices = false };
            DMesh3 a = gen.Generate().MakeDMesh();
            a.DiscardVertexNormals();
            a.DiscardVertexUVs();
            a.DiscardVertexColors();
            DMesh3 b = BoxWithLinearAttributes(1, 1, 1, 3, 3, 3, 2, _ => new Vector2f(0.4f, 0.6f), _ => new Vector3f(0.2f, 0.8f, 0.5f));

            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { TrackFaceProvenance = true };
            Assert.True(eb.Compute());

            // Option validation guards
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExactMeshAttributeTransfer.Transfer(eb, new AttributeTransferOptions { UVSeamTolerance = -1e-3 }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExactMeshAttributeTransfer.Transfer(eb, new AttributeTransferOptions { NormalSeamAngleDeg = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ExactMeshAttributeTransfer.Transfer(eb, new AttributeTransferOptions { ColorSeamTolerance = -1 }));
            Assert.Throws<ArgumentException>(() =>
                ExactMeshAttributeTransfer.ApplyCornerAttributes(eb.Result, new CornerAttributes { UVs = new Vector2d[1], HasUV = new bool[1] }));

            // NormalSeamAngleDeg = 360 (>= 180) must NOT wrap Math.Cos and split normal seams
            DMesh3 resCopy = new DMesh3(eb.Result);
            int splits360 = ExactMeshAttributeTransfer.Transfer(resCopy, eb.ResultProvenance, new[] { a, b },
                new AttributeTransferOptions { SplitAttributeSeams = true, SplitGroupSeams = false, TransferUVs = false, TransferColors = false, NormalSeamAngleDeg = 360.0 });
            // Only vertices on the A/B boundary (HasNormal false vs true) may split, never interior B crease vertices
            Assert.True(splits360 < resCopy.VertexCount);

            // Unsplit mixed-input transfer writes finite unit normals on all B vertices (never zero vector)
            int splits = ExactMeshAttributeTransfer.Transfer(eb, new AttributeTransferOptions { SplitAttributeSeams = false });
            Assert.Equal(0, splits);
            foreach (int tid in eb.Result.TriangleIndices()) {
                if (eb.ResultProvenance[tid].MeshIndex != 1) continue;
                Index3i tv = eb.Result.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    Vector3f n = eb.Result.GetVertexNormal(tv[k]);
                    Assert.True(n.Length > 0.5f, $"Expected non-zero normal on mesh B vertex {tv[k]}, got {n}");
                }
            }
        }

        [Fact]
        public void EvaluateCorners_SimilarityAll_PreservesLinearUVAndOrientation()
        {
            Func<Vector3d, Vector2f> uv0 = p => new Vector2f((float)(p.x * 0.25), (float)((p.y + p.z) * 0.125));
            Func<Vector3d, Vector3f> col0 = p => new Vector3f(0.2f, 0.5f, 0.8f);
            DMesh3 a0 = BoxWithLinearAttributes(0, 0, 0, 2, 2, 2, 1, uv0, col0);
            DMesh3 b0 = BoxWithLinearAttributes(1, 1, 1, 3, 3, 3, 2, uv0, col0);

            foreach (Similarity sim in Similarity.All()) {
                DMesh3 a = new DMesh3(a0);
                DMesh3 b = new DMesh3(b0);
                foreach (int v in a.VertexIndices()) a.SetVertex(v, sim.Apply(a.GetVertex(v)));
                foreach (int v in b.VertexIndices()) b.SetVertex(v, sim.Apply(b.GetVertex(v)));
                // Re-orient stored box normals under similarity rotation
                foreach (DMesh3 m in new[] { a, b }) {
                    foreach (int tid in m.TriangleIndices()) {
                        Vector3d fn = m.GetTriNormal(tid);
                        Index3i tv = m.GetTriangle(tid);
                        for (int k = 0; k < 3; ++k)
                            m.SetVertexNormal(tv[k], new Vector3f((float)fn.x, (float)fn.y, (float)fn.z));
                    }
                }
                var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) { TrackFaceProvenance = true };
                Assert.True(eb.Compute(), sim.Name);
                CornerAttributes corners = ExactMeshAttributeTransfer.EvaluateCorners(eb.Result, eb.ResultProvenance, new[] { a, b });
                double sliverArea = 1e-4 * sim.Scale * sim.Scale;
                foreach (int tid in eb.Result.TriangleIndices()) {
                    TriangleProvenance tp = eb.ResultProvenance[tid];
                    DMesh3 origSrc = tp.MeshIndex == 0 ? a0 : b0;
                    Vector3d expectedNormal = sim.Rotation * origSrc.GetTriNormal(tp.SourceTriangle);
                    if (tp.Reversed) expectedNormal = -expectedNormal;
                    Vector3d fn = eb.Result.GetTriNormal(tid);
                    double area = eb.Result.GetTriArea(tid);
                    Index3i tv = eb.Result.GetTriangle(tid);
                    for (int k = 0; k < 3; ++k) {
                        int slot = 3 * tid + k;
                        Assert.True(corners.HasUV[slot] && corners.HasNormal[slot]);
                        Vector3d pOrig = sim.Inverse(eb.Result.GetVertex(tv[k]));
                        Vector2f exp = uv0(pOrig);
                        Assert.True(Math.Abs(corners.UVs[slot].x - exp.x) < 1e-4 && Math.Abs(corners.UVs[slot].y - exp.y) < 1e-4,
                            $"{sim.Name}: UV {corners.UVs[slot]} vs {exp}");
                        Assert.True(corners.Normals[slot].Dot(expectedNormal) > 0.9999,
                            $"{sim.Name}: source normal mismatch {corners.Normals[slot]} vs {expectedNormal}");
                        if (area > sliverArea)
                            Assert.True(corners.Normals[slot].Dot(fn) > 0.999, $"{sim.Name}: face normal mismatch");
                    }
                }
            }
        }
    }
}
