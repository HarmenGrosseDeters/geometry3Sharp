using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1674, only the API of master (the new-tests gate): QuadRemesher with PreserveCreases of a closed input returns a closed surface, also at
    /// the remesh pass counts of the sharp-box, sharp-cylinder and gear fixtures of QuadCreasePreservationTests where it dropped a face and
    /// left 5 to 7 boundary vertices (the lattice graph had two edges that cross each other). The fixtures are built here (the same triangulations).
    /// </summary>
    public class QuadRemesherClosedOutputTests
    {
        static readonly Dictionary<string, DMesh3> shapes = new Dictionary<string, DMesh3>();

        internal static double TargetOf(string name) { return name == "sharp-box" ? 0.2 : name == "sharp-cylinder" ? 0.15 : name == "l-profile" ? 0.12 : 0.08; }

        internal static DMesh3 Shape(string name, int passes)
        {
            lock (shapes) {
                DMesh3 m;
                string key = name + "/" + passes;
                if (!shapes.TryGetValue(key, out m)) { m = BuildShape(name, passes); shapes[key] = m; }
                return new DMesh3(m);
            }
        }

        static DMesh3 BuildShape(string name, int passes)
        {
            DMesh3 m;
            double h = 0.5 * TargetOf(name);
            if (name == "sharp-box") {
                var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 0.7, 0.5)), EdgeVertices = 5 };
                g.Generate();
                m = g.MakeDMesh();
            } else if (name == "sharp-cylinder") {
                var g = new CappedCylinderGenerator { BaseRadius = 0.6f, TopRadius = 0.6f, Height = 1.6f, Slices = 40, NoSharedVertices = false };
                g.Generate();
                m = g.MakeDMesh();
            } else if (name == "l-profile") {
                var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(1.2, 0), new Vector2d(1.2, 0.4), new Vector2d(0.4, 0.4), new Vector2d(0.4, 1.2), new Vector2d(0, 1.2) })), Subdivisions = 2 };
                gen.Generate();
                m = gen.MakeDMesh();
                var ext = new MeshExtrudeMesh(m) { ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, 0.6), IsPositiveOffset = true };
                ext.Extrude();
                m = new DMesh3(m, true);
            } else {
                int teeth = 8;
                double r0 = 0.6, r1 = 0.85;
                var pts = new List<Vector2d>();
                for (int k = 0; k < teeth; ++k) {
                    double a0 = 2 * Math.PI * k / teeth, step = 2 * Math.PI / (teeth * 4);
                    pts.Add(new Vector2d(r0 * Math.Cos(a0), r0 * Math.Sin(a0)));
                    pts.Add(new Vector2d(r0 * Math.Cos(a0 + step), r0 * Math.Sin(a0 + step)));
                    pts.Add(new Vector2d(r1 * Math.Cos(a0 + 1.4 * step), r1 * Math.Sin(a0 + 1.4 * step)));
                    pts.Add(new Vector2d(r1 * Math.Cos(a0 + 2.6 * step), r1 * Math.Sin(a0 + 2.6 * step)));
                }
                var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(new Polygon2d(pts.ToArray())), Subdivisions = 2 };
                gen.Generate();
                m = gen.MakeDMesh();
                var ext = new MeshExtrudeMesh(m) { ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, 0.4), IsPositiveOffset = true };
                ext.Extrude();
                m = new DMesh3(m, true);
            }
            DMesh3 src = new DMesh3(m);
            var r = new Remesher(m) { SmoothSpeedT = 0.5 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            r.SetTargetEdgeLength(h);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            for (int k = 0; k < passes; ++k) r.BasicRemeshPass();
            return new DMesh3(m, true);
        }

        internal static QuadRemesher Run(DMesh3 mesh, double s, bool parallel = true)
        {
            var qr = new QuadRemesher(mesh) { TargetEdgeLength = s, PreserveCreases = true, Parallel = parallel };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        // the output of a closed input is closed (no boundary vertex, Euler characteristic 2: all three shapes are of genus 0), manifold, finite
        static void AssertClosed(QuadRemesher qr, string what)
        {
            Assert.Equal(0, qr.DroppedFaces);
            DPolyMesh r = qr.Result;
            Assert.True(r.FaceCount > 0, what);
            Assert.True(r.Validate().IsValid, what + " " + r.Validate());
            for (int v = 0; v < r.VertexCount; ++v) Assert.True(r.GetVertex(v).IsFinite, what);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold, what + ": not manifold");
            Assert.Equal(0, m.BoundaryVertices);
            Assert.Equal(2, m.EulerCharacteristic);
            Assert.True(m.MaxSides <= 6, what + ": " + m.MaxSides + " sides");
        }

        public static IEnumerable<object[]> FailingCases()
        {
            yield return new object[] { "sharp-box", 26 };
            yield return new object[] { "sharp-box", 50 };
            yield return new object[] { "sharp-cylinder", 10 };
            yield return new object[] { "sharp-cylinder", 12 };
            yield return new object[] { "gear", 18 };
            yield return new object[] { "gear", 22 };
            yield return new object[] { "gear", 30 };
        }

        [Theory]
        [MemberData(nameof(FailingCases))]
        public void ClosedInput_GivesAClosedOutput(string name, int passes)
        {
            DMesh3 m = Shape(name, passes);
            Assert.True(m.IsClosed());
            QuadRemesher qr = Run(m, TargetOf(name));
            AssertClosed(qr, name + "/" + passes);
        }

        static ulong Hash(DPolyMesh r)
        {
            ulong h = 14695981039346656037UL;
            Action<ulong> add = x => { h ^= x; h *= 1099511628211UL; };
            add((ulong)r.VertexCount); add((ulong)r.FaceCount);
            foreach (int o in r.FaceOffsets) add((ulong)o);
            foreach (int i in r.FaceIndices) add((ulong)i);
            for (int v = 0; v < r.VertexCount; ++v) {
                Vector3d p = r.GetVertex(v);
                add((ulong)BitConverter.DoubleToInt64Bits(p.x)); add((ulong)BitConverter.DoubleToInt64Bits(p.y)); add((ulong)BitConverter.DoubleToInt64Bits(p.z));
            }
            return h;
        }

        // a PreserveCreases output that drops no face is what it was before the repair of the crossing edges, bit for bit (gear/50: the graph has
        // a crossing that the repair would remove, and nothing is dropped; the others have none)
        [Theory]
        [InlineData("sharp-box", 20, 8977766501779236071UL)]
        [InlineData("sharp-cylinder", 20, 16018759166025473961UL)]
        [InlineData("gear", 20, 15266646667066191006UL)]
        [InlineData("gear", 50, 11341578008197146238UL)]
        public void PreserveCreasesOutputWithoutADroppedFace_IsPinnedBitForBit(string name, int passes, ulong expected)
        {
            QuadRemesher qr = Run(Shape(name, passes), TargetOf(name));
            AssertClosed(qr, name + "/" + passes);
            ulong h = Hash(qr.Result);
            bool pinnedPlatform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)
                && !System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework");
            if (pinnedPlatform) Assert.True(expected == h, name + "/" + passes + " hash " + h + "UL");
            else Assert.Equal(h, Hash(Run(Shape(name, passes), TargetOf(name)).Result));
        }

        // the default path (no PreserveCreases) had the same crossing edges: the torus of QuadRemesherDefaultOutputTests had a hole (a dropped face, 6 boundary vertices)
        [Fact]
        public void DefaultPath_Torus_IsClosed()
        {
            DMesh3 m = CrossFieldShapes.TorusMesh();
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = 0.2 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(0, qr.DroppedFaces);
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(pm.IsManifold);
            Assert.Equal(0, pm.BoundaryVertices);
            Assert.Equal(0, pm.EulerCharacteristic);        // genus 1
        }

        [Fact]
        public void DefaultPath_OpenPatchWithTheOldCollapseLength_HasNoExtraHole()
        {
            DMesh3 m = CrossFieldShapes.OpenPatch(50);
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = 0.03, CollapseLength = 0.4 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(0, qr.DroppedFaces);               // an open mesh drops its outer loop only when it is traced as a face: here it is not
            Assert.Equal(135, PolyMeshMetrics.Compute(qr.Result).BoundaryVertices);
        }

        [Fact]
        public void ClosedInput_IsReusableAndSerialEqualsParallel()
        {
            DMesh3 m = Shape("sharp-box", 26);
            double s = TargetOf("sharp-box");
            QuadRemesher qr = Run(m, s);
            AssertClosed(qr, "first");
            DPolyMesh first = qr.Result;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());          // reuse: a second Compute on the instance
            AssertClosed(qr, "second");
            AssertSame(first, qr.Result);
            QuadRemesher serial = Run(Shape("sharp-box", 26), s, parallel: false);
            AssertClosed(serial, "serial");
            AssertSame(first, serial.Result);
        }

        internal static void AssertSame(DPolyMesh a, DPolyMesh b, double scale = 1)
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

        [Fact]
        public void ClosedOutput_UnderSimilarities()
        {
            // the 2^k scalings are exact: the same polygon mesh, scaled, bit for bit; the other similarities give a closed output again
            DMesh3 m = Shape("sharp-box", 26);
            double s = TargetOf("sharp-box");
            DPolyMesh reference = Run(m, s).Result;
            foreach (Similarity sim in Similarity.All()) {
                QuadRemesher qr = Run(Invariance.Transformed(m, sim), s * sim.Scale);
                AssertClosed(qr, sim.Name);
                if (sim.IsExact) AssertSame(reference, qr.Result, sim.Scale);
            }
        }

        // the sweep of the issue: every pass count of the fixtures (about 10 s alone, more under the load of CI: nightly; the single cases above are the quick tier)
        [Trait("Category", "Nightly")]
        [Fact]
        public void ClosedInput_GivesAClosedOutput_AtEveryFixturePassCount()
        {
            var failed = new List<string>();
            foreach (string name in new[] { "sharp-box", "sharp-cylinder", "gear" })
                foreach (int passes in new[] { 10, 12, 14, 16, 18, 20, 22, 24, 26, 27, 28, 29, 30, 32, 35, 40, 50, 60 }) {
                    var qr = new QuadRemesher(Shape(name, passes)) { TargetEdgeLength = TargetOf(name), PreserveCreases = true };
                    if (qr.Compute() != QuadRemeshStatus.Success) { failed.Add(name + "/" + passes + " status"); continue; }
                    PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
                    if (qr.DroppedFaces != 0 || pm.BoundaryVertices != 0 || pm.EulerCharacteristic != 2) failed.Add(name + "/" + passes + " dropped " + qr.DroppedFaces + " boundary " + pm.BoundaryVertices);
                }
            Assert.True(failed.Count == 0, string.Join("; ", failed));
        }
    }
}
