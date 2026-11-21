using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 4 sharp feature / crease preservation (docs/design/QUAD_REMESHING.md §4):
    /// PositionField.PreserveCreases and QuadRemesher.PreserveCreases (opt-in, default false).
    /// Verifies sharp CAD gates (> 95 % feature length coverage, angle deviation p95 <= 10 deg,
    /// feature Hausdorff <= 0.1 s, manifoldness), boundary-crease junctions, threshold/validation,
    /// Similarity.All() invariance (exact 2^k bit-identity), determinism, and reuse.
    /// </summary>
    public class QuadCreasePreservationTests
    {
        static readonly Dictionary<string, DMesh3> shapes = new Dictionary<string, DMesh3>();

        static DMesh3 Shape(string name)
        {
            lock (shapes) {
                DMesh3 m;
                if (!shapes.TryGetValue(name, out m)) {
                    m = BuildShape(name);
                    shapes[name] = m;
                }
                return m;
            }
        }

        static double TargetOf(string name)
        {
            switch (name) {
                case "sharp-box": return 0.2;
                case "sharp-cylinder": return 0.15;
                case "l-profile": return 0.12;
                case "gear": return 0.08;
                case "open-bent": return 0.1;
                default: throw new ArgumentException(name);
            }
        }

        static DMesh3 BuildShape(string name)
        {
            DMesh3 m;
            double h = 0.5 * TargetOf(name);
            switch (name) {
                case "sharp-box": {
                    var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 0.7, 0.5)), EdgeVertices = 5 };
                    g.Generate();
                    m = g.MakeDMesh();
                    break;
                }
                case "sharp-cylinder": {
                    var g = new CappedCylinderGenerator { BaseRadius = 0.6f, TopRadius = 0.6f, Height = 1.6f, Slices = 40, NoSharedVertices = false };
                    g.Generate();
                    m = g.MakeDMesh();
                    break;
                }
                case "l-profile": {
                    var poly = new Polygon2d(new[] {
                        new Vector2d(0, 0), new Vector2d(1.2, 0), new Vector2d(1.2, 0.4),
                        new Vector2d(0.4, 0.4), new Vector2d(0.4, 1.2), new Vector2d(0, 1.2)
                    });
                    m = ExtrudePolygon(poly, 0.6);
                    break;
                }
                case "gear": {
                    int teeth = 8;
                    double r0 = 0.6, r1 = 0.85;
                    var pts = new List<Vector2d>();
                    for (int k = 0; k < teeth; ++k) {
                        double a0 = 2 * Math.PI * k / teeth;
                        double step = 2 * Math.PI / (teeth * 4);
                        pts.Add(new Vector2d(r0 * Math.Cos(a0), r0 * Math.Sin(a0)));
                        pts.Add(new Vector2d(r0 * Math.Cos(a0 + step), r0 * Math.Sin(a0 + step)));
                        pts.Add(new Vector2d(r1 * Math.Cos(a0 + 1.4 * step), r1 * Math.Sin(a0 + 1.4 * step)));
                        pts.Add(new Vector2d(r1 * Math.Cos(a0 + 2.6 * step), r1 * Math.Sin(a0 + 2.6 * step)));
                    }
                    m = ExtrudePolygon(new Polygon2d(pts.ToArray()), 0.4);
                    break;
                }
                case "open-bent": {
                    // Open L-bent sheet: flat half x in [0, 0.5], z = 0; vertical half x = 0.5, z in [0, 0.5], y in [0, 0.8]
                    int nu = 10, nv = 16;
                    m = new DMesh3();
                    int[,] ids = new int[2 * nu + 1, nv + 1];
                    for (int j = 0; j <= nv; ++j) {
                        double y = 0.8 * j / nv;
                        for (int i = 0; i <= 2 * nu; ++i) {
                            Vector3d p = i <= nu
                                ? new Vector3d(0.5 * i / nu, y, 0)
                                : new Vector3d(0.5, y, 0.5 * (i - nu) / nu);
                            ids[i, j] = m.AppendVertex(p);
                        }
                    }
                    for (int j = 0; j < nv; ++j)
                        for (int i = 0; i < 2 * nu; ++i) {
                            m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                            m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                        }
                    return m;
                }
                default:
                    throw new ArgumentException(name);
            }
            DMesh3 src = new DMesh3(m);
            var r = new Remesher(m) { SmoothSpeedT = 0.5, PreventNormalFlips = true, MinTriangleQuality = 0.2 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            r.SetTargetEdgeLength(h);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            for (int k = 0; k < 15; ++k) r.BasicRemeshPass();
            return new DMesh3(m, true);
        }

        static DMesh3 ExtrudePolygon(Polygon2d poly, double height)
        {
            var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(poly), Subdivisions = 2 };
            gen.Generate();
            DMesh3 m = gen.MakeDMesh();
            var ext = new MeshExtrudeMesh(m) {
                ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, height),
                IsPositiveOffset = true
            };
            ext.Extrude();
            return new DMesh3(m, true);
        }

        static PolyMeshMetrics CheckResult(DPolyMesh r, string what)
        {
            Assert.True(r.FaceCount > 0, what);
            Assert.True(r.Validate().IsValid, what + " " + r.Validate());
            for (int v = 0; v < r.VertexCount; ++v) Assert.True(r.GetVertex(v).IsFinite, what);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.MaxSides <= 6, what + ": a face with " + m.MaxSides + " sides");
            Assert.True(m.IsManifold, what + ": not manifold (" + m.NonManifoldEdges + "e " + m.InconsistentEdges + "i " + m.NonManifoldVertices + "v)");
            return m;
        }

        static void AssertSame(DPolyMesh a, DPolyMesh b, double scale = 1)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.FaceCount, b.FaceCount);
            Assert.Equal(a.FaceOffsets.ToArray(), b.FaceOffsets.ToArray());
            Assert.Equal(a.FaceIndices.ToArray(), b.FaceIndices.ToArray());
            for (int v = 0; v < a.VertexCount; ++v) {
                Vector3d p = a.GetVertex(v), q = b.GetVertex(v);
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * scale), BitConverter.DoubleToInt64Bits(q.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * scale), BitConverter.DoubleToInt64Bits(q.y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * scale), BitConverter.DoubleToInt64Bits(q.z));
            }
        }

        static double PointSegmentDist(Vector3d p, Vector3d a, Vector3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.LengthSquared;
            if (!(l2 > 0)) return p.Distance(a);
            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
            return p.Distance(a + t * ab);
        }

        static void MeasureFeatures(DMesh3 input, DPolyMesh result, double s, double creaseAngle, out double coverage, out double angleP95, out double featHausOverS)
        {
            var topo = new MeshTopology(input) { CreaseAngle = creaseAngle };
            topo.Compute();
            Assert.True(topo.CreaseEdges.Count > 0, "expected sharp crease edges");
            var outEdges = new List<(Vector3d a, Vector3d b, Vector3d dir)>();
            var seen = new HashSet<long>();
            int nv = result.VertexCount;
            for (int f = 0; f < result.FaceCount; ++f) {
                int[] fv = result.GetFace(f);
                for (int k = 0; k < fv.Length; ++k) {
                    int a = fv[k], b = fv[(k + 1) % fv.Length];
                    long key = a < b ? (long)a * nv + b : (long)b * nv + a;
                    if (!seen.Add(key)) continue;
                    Vector3d pa = result.GetVertex(a), pb = result.GetVertex(b);
                    Vector3d d = pb - pa; double l = d.Length;
                    if (l > 0) outEdges.Add((pa, pb, d / l));
                }
            }
            double totalLen = 0, coveredLen = 0, maxDist = 0;
            var angles = new List<double>();
            foreach (int eid in topo.CreaseEdges) {
                Index2i ev = input.GetEdgeV(eid);
                Vector3d u = input.GetVertex(ev.a), v = input.GetVertex(ev.b);
                Vector3d d = v - u; double len = d.Length;
                if (!(len > 0)) continue;
                Vector3d dir = d / len;
                totalLen += len;
                double dEndMax = 0, ang = 90;
                foreach (double t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) {
                    Vector3d q = (1 - t) * u + t * v;
                    double best = double.MaxValue;
                    double bestAng = 90;
                    for (int i = 0; i < outEdges.Count; ++i) {
                        double dist = PointSegmentDist(q, outEdges[i].a, outEdges[i].b);
                        if (dist < best - 1e-9 * s) {
                            best = dist;
                            if (t == 0.5) {
                                double dot = Math.Max(0, Math.Min(1, Math.Abs(dir.Dot(outEdges[i].dir))));
                                bestAng = Math.Acos(dot) * 180.0 / Math.PI;
                            }
                        } else if (t == 0.5 && dist <= best + 1e-9 * s) {
                            double dot = Math.Max(0, Math.Min(1, Math.Abs(dir.Dot(outEdges[i].dir))));
                            double candAng = Math.Acos(dot) * 180.0 / Math.PI;
                            if (candAng < bestAng) bestAng = candAng;
                        }
                    }
                    if (best > maxDist) maxDist = best;
                    if (best > dEndMax) dEndMax = best;
                    if (t == 0.5) ang = bestAng;
                }
                angles.Add(ang);
                if (dEndMax <= 0.1 * s && ang <= 10.0) coveredLen += len;
            }
            coverage = totalLen > 0 ? coveredLen / totalLen : 1.0;
            angleP95 = angles.Count > 0 ? PolyMeshMetrics.Percentile(angles.ToArray(), 0.95) : 0;
            featHausOverS = maxDist / s;
        }

        static double OneSidedHausdorff(DPolyMesh r, DMesh3 reference)
        {
            int[] map;
            DMesh3 tri = r.ToDMesh3(out map);
            var hd = new MeshHausdorffDistance(tri, reference) { Symmetric = false, RelativeTolerance = 1e-3 };
            hd.Compute();
            return hd.UpperBound;
        }

        [Theory]
        [InlineData("sharp-box")]
        [InlineData("sharp-cylinder")]
        [InlineData("l-profile")]
        [InlineData("gear")]
        public void SharpCadShapes_MeetStage4FeatureGates(string name)
        {
            DMesh3 m = Shape(name);
            double s = TargetOf(name);

            // Baseline without PreserveCreases bevels sharp features (fails the <= 0.1 s feature Hausdorff gate)
            var off = new QuadRemesher(m) { TargetEdgeLength = s };
            Assert.False(off.PreserveCreases);
            Assert.Equal(QuadRemeshStatus.Success, off.Compute());
            MeasureFeatures(m, off.Result, s, 30, out double covOff, out _, out double hausOff);
            Assert.True(hausOff > 0.25 || covOff < 0.85, name + " baseline unexpectedly sharp: cov=" + covOff + " haus=" + hausOff);

            // With PreserveCreases = true: meets all Stage 4 gates
            var on = new QuadRemesher(m) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, on.Compute());
            PolyMeshMetrics metrics = CheckResult(on.Result, name + " PreserveCreases");
            Assert.True(metrics.QuadFraction >= 0.85, name + " quads " + metrics.QuadFraction);
            Assert.True(OneSidedHausdorff(on.Result, m) <= 0.5 * s, name + " surface Hausdorff");

            MeasureFeatures(m, on.Result, s, 30, out double covOn, out double ang95On, out double hausOn);
            Assert.True(covOn > 0.95, name + ": feature coverage " + (100 * covOn).ToString("F1") + "% <= 95%");
            Assert.True(ang95On <= 10.0, name + ": feature angle p95 " + ang95On.ToString("F1") + " deg > 10 deg");
            Assert.True(hausOn <= 0.1, name + ": feature Hausdorff " + hausOn.ToString("F3") + " s > 0.1 s");
        }

        [Fact]
        public void SharpBox_AllEightCornersAreExactOutputVertices()
        {
            DMesh3 box = Shape("sharp-box");
            double s = TargetOf("sharp-box");
            var qr = new QuadRemesher(box) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            CheckResult(qr.Result, "sharp-box corners");

            // Box3d(Zero, (1.0, 0.7, 0.5)) has 8 corners at (+-1.0, +-0.7, +-0.5)
            foreach (double sx in new[] { -1.0, 1.0 })
                foreach (double sy in new[] { -0.7, 0.7 })
                    foreach (double sz in new[] { -0.5, 0.5 }) {
                        var corner = new Vector3d(sx, sy, sz);
                        double best = double.MaxValue;
                        for (int v = 0; v < qr.Result.VertexCount; ++v)
                            best = Math.Min(best, qr.Result.GetVertex(v).Distance(corner));
                        Assert.True(best < 1e-9, "corner " + corner + " missing, nearest " + best);
                    }
        }

        [Fact]
        public void SharpRidgeAbove120Degrees_PreservesCreaseAndStaysManifold()
        {
            // Triangular prism with 120-degree dihedral edges (equilateral triangle cross-section extruded along z)
            double r = 0.8;
            var triPoly = new Polygon2d(new[] {
                new Vector2d(r, 0),
                new Vector2d(r * Math.Cos(2 * Math.PI / 3), r * Math.Sin(2 * Math.PI / 3)),
                new Vector2d(r * Math.Cos(4 * Math.PI / 3), r * Math.Sin(4 * Math.PI / 3))
            });
            DMesh3 prism = ExtrudePolygon(triPoly, 1.2);
            double s = 0.16;
            DMesh3 src = new DMesh3(prism);
            var rem = new Remesher(prism) { SmoothSpeedT = 0.5, PreventNormalFlips = true, MinTriangleQuality = 0.2 };
            rem.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            rem.SetTargetEdgeLength(0.5 * s);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(rem, 30);
            for (int k = 0; k < 15; ++k) rem.BasicRemeshPass();
            prism = new DMesh3(prism, true);

            var qr = new QuadRemesher(prism) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            PolyMeshMetrics metrics = CheckResult(qr.Result, "120-deg triangular prism");
            Assert.True(metrics.QuadFraction >= 0.85, "prism quads " + metrics.QuadFraction);
            MeasureFeatures(prism, qr.Result, s, 30, out double cov, out double ang95, out double haus);
            Assert.True(cov > 0.95, "prism coverage " + cov);
            Assert.True(ang95 <= 10.0, "prism angle p95 " + ang95);
            Assert.True(haus <= 0.1, "prism feature Hausdorff " + haus);
        }

        [Fact]
        public void OpenMeshWithInteriorCrease_PinsBoundaryCreaseJunctionsAndFollowsCrease()
        {
            DMesh3 m = Shape("open-bent");
            double s = TargetOf("open-bent");
            var qr = new QuadRemesher(m) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            PolyMeshMetrics metrics = CheckResult(qr.Result, "open-bent");
            Assert.True(metrics.QuadFraction >= 0.90, "quads " + metrics.QuadFraction);
            MeasureFeatures(m, qr.Result, s, 30, out double cov, out double ang95, out double haus);
            Assert.True(cov > 0.95, "open-bent coverage " + cov);
            Assert.True(ang95 <= 10.0, "open-bent angle p95 " + ang95);
            Assert.True(haus <= 0.1, "open-bent feature Hausdorff " + haus);

            // Both junctions where the 90-degree crease meets the open boundary are exact output vertices
            foreach (var junction in new[] { new Vector3d(0.5, 0.0, 0.0), new Vector3d(0.5, 0.8, 0.0) }) {
                double best = double.MaxValue;
                for (int v = 0; v < qr.Result.VertexCount; ++v)
                    best = Math.Min(best, qr.Result.GetVertex(v).Distance(junction));
                Assert.True(best < 1e-9, "boundary-crease junction " + junction + " missing, nearest " + best);
            }
        }

        [Fact]
        public void PositionField_ClassifiesDegree1Endpoint_Degree2SharpTurn_AndDegree4JunctionAsCorners()
        {
            // 1) Open ridge ending smoothly in the interior -> degree-1 crease endpoint (Kind == 2)
            //    Built on a 12x12 grid where z = 0.25 * max(0, 1 - |y - 0.5| / 0.2) for x <= 0.5, ramping to 0 for x > 0.5
            int n = 12;
            var ridge = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    double along = x <= 0.5 ? 1.0 : Math.Max(0, 1.0 - (x - 0.5) / 0.25);
                    double z = 0.25 * along * Math.Max(0, 1.0 - Math.Abs(y - 0.5) / 0.25);
                    ids[i, j] = ridge.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    ridge.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    ridge.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            var cf1 = new CrossField(ridge) { AlignCreases = true, CreaseAngleDegrees = 35 };
            Assert.Equal(CrossFieldStatus.Success, cf1.Compute());
            var pf1 = new PositionField(ridge, cf1) { TargetEdgeLength = 0.15, PreserveCreases = true, CreaseAngleDegrees = 35 };
            Assert.Equal(PositionFieldStatus.Success, pf1.Compute());
            var topo1 = new MeshTopology(ridge) { CreaseAngle = 35 };
            topo1.Compute();
            var deg1 = new Dictionary<int, int>();
            foreach (int eid in topo1.CreaseEdges) {
                Index2i ev = ridge.GetEdgeV(eid);
                deg1[ev.a] = deg1.TryGetValue(ev.a, out int ca) ? ca + 1 : 1;
                deg1[ev.b] = deg1.TryGetValue(ev.b, out int cb) ? cb + 1 : 1;
            }
            int deg1Count = 0;
            foreach (var kv in deg1)
                if (kv.Value == 1 && !ridge.IsBoundaryVertex(kv.Key)) {
                    deg1Count++;
                    int ci = pf1.ToCompact[kv.Key];
                    Assert.Equal((byte)2, pf1.L0.Kind[ci]);
                    Assert.True(ridge.GetVertex(kv.Key).Distance(pf1.Origin(kv.Key)) < 1e-12);
                }
            Assert.True(deg1Count >= 1, "expected at least one interior degree-1 crease endpoint");

            // 2) Degree-2 sharp 90-degree crease turn (L-shaped raised roof on a flat sheet)
            //    z(x, y) = 0.35 * max(0, 1 - distToL(x, y) / 0.25) -> sharp L-ridge with one 90-deg turn vertex of crease degree 2
            var lRidge = new DMesh3();
            int[,] idsL = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    double d1 = PointSegmentDist(new Vector3d(x, y, 0), new Vector3d(0, 0.5, 0), new Vector3d(0.5, 0.5, 0));
                    double d2 = PointSegmentDist(new Vector3d(x, y, 0), new Vector3d(0.5, 0.5, 0), new Vector3d(0.5, 1.0, 0));
                    double z = 0.35 * Math.Max(0, 1.0 - Math.Min(d1, d2) / 0.25);
                    idsL[i, j] = lRidge.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    lRidge.AppendTriangle(idsL[i, j], idsL[i + 1, j], idsL[i + 1, j + 1]);
                    lRidge.AppendTriangle(idsL[i, j], idsL[i + 1, j + 1], idsL[i, j + 1]);
                }
            int turnVid = idsL[n / 2, n / 2];
            var topoL = new MeshTopology(lRidge) { CreaseAngle = 45 };
            topoL.Compute();
            int turnDeg = 0;
            foreach (int eid in lRidge.VtxEdgesItr(turnVid)) if (topoL.CreaseEdges.Contains(eid)) turnDeg++;
            Assert.Equal(2, turnDeg);
            var cfL = new CrossField(lRidge) { AlignCreases = true, CreaseAngleDegrees = 45 };
            Assert.Equal(CrossFieldStatus.Success, cfL.Compute());
            var pfSharp = new PositionField(lRidge, cfL) { TargetEdgeLength = 0.15, PreserveCreases = true, CreaseAngleDegrees = 45, CornerAngleDegrees = 30 };
            Assert.Equal(PositionFieldStatus.Success, pfSharp.Compute());
            Assert.Equal((byte)2, pfSharp.L0.Kind[pfSharp.ToCompact[turnVid]]);
            Assert.True(lRidge.GetVertex(turnVid).Distance(pfSharp.Origin(turnVid)) < 1e-12);
            // Raising CornerAngleDegrees above 90 deg turns the degree-2 bend into a smooth Kind == 1 crease vertex
            var pfSmooth = new PositionField(lRidge, cfL) { TargetEdgeLength = 0.15, PreserveCreases = true, CreaseAngleDegrees = 45, CornerAngleDegrees = 100 };
            Assert.Equal(PositionFieldStatus.Success, pfSmooth.Compute());
            Assert.Equal((byte)1, pfSmooth.L0.Kind[pfSmooth.ToCompact[turnVid]]);

            // 3) High-degree (>= 4) crease X-junction (crossing ridges at x = 0.5 and y = 0.5)
            var xRidge = new DMesh3();
            int[,] idsX = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    double d = Math.Min(Math.Abs(x - 0.5), Math.Abs(y - 0.5));
                    double z = 0.35 * Math.Max(0, 1.0 - d / 0.25);
                    idsX[i, j] = xRidge.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    xRidge.AppendTriangle(idsX[i, j], idsX[i + 1, j], idsX[i + 1, j + 1]);
                    xRidge.AppendTriangle(idsX[i, j], idsX[i + 1, j + 1], idsX[i, j + 1]);
                }
            int centerVid = idsX[n / 2, n / 2];
            var topoX = new MeshTopology(xRidge) { CreaseAngle = 45 };
            topoX.Compute();
            int centerDeg = 0;
            foreach (int eid in xRidge.VtxEdgesItr(centerVid)) if (topoX.CreaseEdges.Contains(eid)) centerDeg++;
            Assert.True(centerDeg >= 4, "expected high-degree crease junction (>= 4), got " + centerDeg);
            var cfX = new CrossField(xRidge) { AlignCreases = true, CreaseAngleDegrees = 45 };
            Assert.Equal(CrossFieldStatus.Success, cfX.Compute());
            var pfX = new PositionField(xRidge, cfX) { TargetEdgeLength = 0.15, PreserveCreases = true, CreaseAngleDegrees = 45 };
            Assert.Equal(PositionFieldStatus.Success, pfX.Compute());
            Assert.Equal((byte)2, pfX.L0.Kind[pfX.ToCompact[centerVid]]);
            Assert.True(xRidge.GetVertex(centerVid).Distance(pfX.Origin(centerVid)) < 1e-12);
        }

        [Fact]
        public void PositionField_CreaseOriginsStayOnCreaseLine_AndHoldWithZeroSweeps()
        {
            DMesh3 box = Shape("sharp-box");
            double s = TargetOf("sharp-box");
            var cf = new CrossField(box) { AlignCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());

            foreach (int sweeps in new[] { 0, 20 }) {
                var pf = new PositionField(box, cf) { TargetEdgeLength = s, Sweeps = sweeps, PreserveCreases = true, CreaseAngleDegrees = 30 };
                Assert.Equal(PositionFieldStatus.Success, pf.Compute());
                Assert.True(pf.Levels >= 2, "expected multi-level hierarchy to exercise prolongation, got " + pf.Levels);

                var topo = new MeshTopology(box) { CreaseAngle = 30 };
                topo.Compute();
                var creaseDeg = new Dictionary<int, int>();
                foreach (int eid in topo.CreaseEdges) {
                    Index2i ev = box.GetEdgeV(eid);
                    creaseDeg[ev.a] = creaseDeg.TryGetValue(ev.a, out int ca) ? ca + 1 : 1;
                    creaseDeg[ev.b] = creaseDeg.TryGetValue(ev.b, out int cb) ? cb + 1 : 1;
                }
                int corners = 0, smoothCrease = 0;
                foreach (var kv in creaseDeg) {
                    int v = kv.Key;
                    Vector3d p = box.GetVertex(v), o = pf.Origin(v);
                    if (kv.Value != 2) {
                        corners++;
                        Assert.True(p.Distance(o) < 1e-12, "corner " + v + " moved by " + p.Distance(o));
                    } else {
                        smoothCrease++;
                        // Origin must lie on the crease line: zero offset along N and along Cb (perpendicular to crease)
                        int ci = pf.ToCompact[v];
                        Assert.Equal((byte)1, pf.L0.Kind[ci]);
                        Assert.True(Math.Abs(pf.L0.Nrm[ci].Dot(o - p)) < 1e-12);
                        Assert.True(Math.Abs(pf.L0.Cb[ci].Dot(o - p)) < 1e-12);
                    }
                }
                Assert.Equal(8, corners);
                Assert.True(smoothCrease > 20);
            }
        }

        [Fact]
        public void QuadRemesher_WithExplicitField_PreservesCreasesWithoutMutatingField()
        {
            DMesh3 box = Shape("sharp-box");
            double s = TargetOf("sharp-box");
            var cf = new CrossField(box) { AlignCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var dirBefore = new Vector3d[box.MaxVertexID];
            foreach (int v in box.VertexIndices()) dirBefore[v] = cf.VertexDirection(v, 0);

            var qr = new QuadRemesher(box) { Field = cf, TargetEdgeLength = s, PreserveCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            CheckResult(qr.Result, "explicit Field + PreserveCreases");
            MeasureFeatures(box, qr.Result, s, 30, out double cov, out double ang95, out double haus);
            Assert.True(cov > 0.95, "cov " + cov);
            Assert.True(ang95 <= 10.0, "ang95 " + ang95);
            Assert.True(haus <= 0.1, "haus " + haus);
            foreach (int v in box.VertexIndices())
                Assert.Equal(dirBefore[v], cf.VertexDirection(v, 0));
        }

        [Fact]
        public void CreaseAngleThreshold_AndSmoothMesh_MatchDefaultBitForBit()
        {
            // On a smooth closed sphere with no creases > 30 deg, PreserveCreases = true is bit-identical to false
            DMesh3 sphere = CrossFieldShapes.UnitSphere();
            var offSphere = new QuadRemesher(sphere) { TargetEdgeLength = 0.2, PreserveCreases = false };
            var onSphere = new QuadRemesher(sphere) { TargetEdgeLength = 0.2, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, offSphere.Compute());
            Assert.Equal(QuadRemeshStatus.Success, onSphere.Compute());
            AssertSame(offSphere.Result, onSphere.Result);

            // On a 90-deg sharp box with CreaseAngleDegrees = 100 (above every dihedral), no creases are constrained
            DMesh3 box = CrossFieldShapes.SharpBox();
            var offBox = new QuadRemesher(box) { TargetEdgeLength = 0.25, PreserveCreases = false };
            var highAngleBox = new QuadRemesher(box) { TargetEdgeLength = 0.25, PreserveCreases = true, CreaseAngleDegrees = 100 };
            Assert.Equal(QuadRemeshStatus.Success, offBox.Compute());
            Assert.Equal(QuadRemeshStatus.Success, highAngleBox.Compute());
            AssertSame(offBox.Result, highAngleBox.Result);
        }

        [Fact]
        public void InvalidCreaseAngleDegrees_ReturnsInvalidInput()
        {
            DMesh3 box = CrossFieldShapes.SharpBox();
            var cf = new CrossField(box);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            foreach (double bad in new[] { -1.0, 181.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var pf = new PositionField(box, cf) { TargetEdgeLength = 0.25, PreserveCreases = true, CreaseAngleDegrees = bad };
                Assert.Equal(PositionFieldStatus.InvalidInput, pf.Compute());
                var qr = new QuadRemesher(box) { Field = cf, TargetEdgeLength = 0.25, PreserveCreases = true, CreaseAngleDegrees = bad };
                Assert.Equal(QuadRemeshStatus.InvalidInput, qr.Compute());
                Assert.Null(qr.Result);
                // Also validated even when PreserveCreases is false
                var qrOff = new QuadRemesher(box) { Field = cf, TargetEdgeLength = 0.25, PreserveCreases = false, CreaseAngleDegrees = bad };
                Assert.Equal(QuadRemeshStatus.InvalidInput, qrOff.Compute());
            }
        }

        [Theory]
        [InlineData(SizeAdaptationMode.Scale)]
        [InlineData(SizeAdaptationMode.ScaleAndRotation)]
        public void PreserveCreases_CombinesCleanlyWithSizeAdaptation(SizeAdaptationMode mode)
        {
            // Capped dome (closed hemisphere z >= 0 with flat bottom cap at z = 0): curved dome ensures
            // IntegrableFrames lowers curl by > 20% (SizeAdaptationApplied == true) while the equator rim is a 90-deg crease.
            DMesh3 dome = CrossFieldShapes.UnitSphere();
            var cut = new MeshPlaneCut(dome, Vector3d.Zero, -Vector3d.AxisZ);
            Assert.True(cut.Cut());
            Assert.True(cut.FillHoles());
            double s = 0.2;
            DMesh3 src = new DMesh3(dome);
            var rem = new Remesher(dome) { SmoothSpeedT = 0.5, PreventNormalFlips = true, MinTriangleQuality = 0.2 };
            rem.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            rem.SetTargetEdgeLength(0.5 * s);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(rem, 30);
            for (int k = 0; k < 12; ++k) rem.BasicRemeshPass();
            dome = new DMesh3(dome, true);

            var qr = new QuadRemesher(dome) { TargetEdgeLength = s, PreserveCreases = true, SizeAdaptation = mode };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.True(qr.SizeAdaptationApplied, mode + ": expected SizeAdaptationApplied on curved capped dome");
            PolyMeshMetrics metrics = CheckResult(qr.Result, "capped-dome " + mode + " + PreserveCreases");
            Assert.True(metrics.QuadFraction >= 0.85, "quads " + metrics.QuadFraction);
            MeasureFeatures(dome, qr.Result, s, 30, out double cov, out double ang95, out double haus);
            Assert.True(cov > 0.90, mode + " coverage " + cov);
            Assert.True(ang95 <= 10.0, mode + " angle p95 " + ang95);
            Assert.True(haus <= 0.1, mode + " feature Hausdorff " + haus);
        }

        [Fact]
        public void PreserveCreases_IsDeterministicAndReusable()
        {
            DMesh3 box = Shape("sharp-box");
            double s = TargetOf("sharp-box");
            var serial = new QuadRemesher(box) { TargetEdgeLength = s, PreserveCreases = true, Parallel = false, ChunkSize = 8 };
            var parallel = new QuadRemesher(box) { TargetEdgeLength = s, PreserveCreases = true, Parallel = true, ChunkSize = 8 };
            Assert.Equal(QuadRemeshStatus.Success, serial.Compute());
            Assert.Equal(QuadRemeshStatus.Success, parallel.Compute());
            AssertSame(serial.Result, parallel.Result);

            // Reuse one instance across PreserveCreases = true -> true -> false -> true
            var qr = new QuadRemesher(box) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            DPolyMesh first = qr.Result;
            AssertSame(serial.Result, first);

            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            AssertSame(first, qr.Result);

            qr.PreserveCreases = false;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            var freshOff = new QuadRemesher(box) { TargetEdgeLength = s, PreserveCreases = false };
            Assert.Equal(QuadRemeshStatus.Success, freshOff.Compute());
            AssertSame(freshOff.Result, qr.Result);

            qr.PreserveCreases = true;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            AssertSame(first, qr.Result);
        }

        public static IEnumerable<object[]> Names() => Similarity.Names();

        [Theory, MemberData(nameof(Names))]
        public void PreserveCreases_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 baseBox = CrossFieldShapes.SharpBox();
            double s = 0.25;
            var baseQr = new QuadRemesher(baseBox) { TargetEdgeLength = s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, baseQr.Compute());

            DMesh3 t = Invariance.Transformed(baseBox, sim);
            var qr = new QuadRemesher(t) { TargetEdgeLength = sim.Scale * s, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            if (sim.IsExact) {
                AssertSame(baseQr.Result, qr.Result, sim.Scale);
            } else {
                PolyMeshMetrics a = CheckResult(baseQr.Result, "base"), b = CheckResult(qr.Result, name);
                Assert.InRange(b.Faces, 0.9 * a.Faces, 1.1 * a.Faces);
                MeasureFeatures(t, qr.Result, sim.Scale * s, 30, out double cov, out double ang95, out double haus);
                Assert.True(cov > 0.95, name + ": cov " + cov);
                Assert.True(ang95 <= 10.0, name + ": ang95 " + ang95);
                Assert.True(haus <= 0.1, name + ": haus " + haus);
            }
        }
    }
}
