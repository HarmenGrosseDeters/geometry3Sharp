using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using g3;
using g3.CoreMathPort;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1713: the quad pipeline (CrossField, PositionField, QuadRemesher, IntegrableFrames) calls the correctly rounded CoreMath instead of the platform
    /// libm (G3E0004 makes a platform call there a build error), so the same input gives the same bits on every platform. On the gear fixture at 18
    /// remesh passes a 1-ulp change of an input decides whether the extraction drops a face (QuadRemesherCrossingTests): the cross field, the position
    /// field and both extractions of that input are pinned here, on an input built with correctly rounded sines and cosines.
    /// </summary>
    public class QuadPipelineCoreMathTests
    {
        const double Target = 0.08;

        // the gear of QuadRemesherClosedOutputTests at 18 passes, its outline with CoreMath sin / cos
        static readonly Lazy<DMesh3> gear18 = new Lazy<DMesh3>(() => Gear(18));

        static DMesh3 Gear(int passes)
        {
            int teeth = 8;
            double r0 = 0.6, r1 = 0.85;
            var pts = new List<Vector2d>();
            for (int k = 0; k < teeth; ++k) {
                double a0 = 2 * Math.PI * k / teeth, step = 2 * Math.PI / (teeth * 4);
                foreach (var (r, a) in new[] { (r0, a0), (r0, a0 + step), (r1, a0 + 1.4 * step), (r1, a0 + 2.6 * step) })
                    pts.Add(new Vector2d(r * CoreMath.Cos(a), r * CoreMath.Sin(a)));
            }
            var gen = new TriangulatedPolygonGenerator { Polygon = new GeneralPolygon2d(new Polygon2d(pts.ToArray())), Subdivisions = 2 };
            gen.Generate();
            DMesh3 m = gen.MakeDMesh();
            var ext = new MeshExtrudeMesh(m) { ExtrudedPositionF = (v, n, id) => v + new Vector3d(0, 0, 0.4), IsPositiveOffset = true };
            ext.Extrude();
            m = new DMesh3(m, true);
            DMesh3 src = new DMesh3(m);
            var rem = new Remesher(m) { SmoothSpeedT = 0.5 };
            rem.SetProjectionTarget(MeshProjectionTarget.Auto(src));
            rem.SetTargetEdgeLength(0.5 * Target);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(rem, 30);
            for (int k = 0; k < passes; ++k) rem.BasicRemeshPass();
            return new DMesh3(m, true);
        }

        sealed class Hasher
        {
            readonly List<byte> bytes = new List<byte>();
            public void Add(double x) { bytes.AddRange(BitConverter.GetBytes(x)); }
            public void Add(int x) { bytes.AddRange(BitConverter.GetBytes(x)); }
            public void Add(Vector3d v) { Add(v.x); Add(v.y); Add(v.z); }
            public override string ToString()
            {
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
            }
        }

        static string MeshHash(DMesh3 m)
        {
            var h = new Hasher();
            foreach (int v in m.VertexIndices()) { h.Add(v); h.Add(m.GetVertex(v)); }
            foreach (int t in m.TriangleIndices()) { Index3i tri = m.GetTriangle(t); h.Add(tri.a); h.Add(tri.b); h.Add(tri.c); }
            return h.ToString();
        }

        static string FieldHash(CrossField cf, DMesh3 m)
        {
            var h = new Hasher();
            foreach (int v in m.VertexIndices())
                if (cf.HasField(v)) { h.Add(v); h.Add(cf.VertexAngle(v)); h.Add(cf.VertexDirection(v)); }
            // the face directions (two of the four: k = 1 is the rotated one), the singular faces and the smoothness energy
            foreach (int t in m.TriangleIndices()) { h.Add(cf.FaceDirection(t)); h.Add(cf.FaceDirection(t, 1)); }
            foreach (int t in cf.SingularFaces) h.Add(t);
            h.Add(cf.Eigenvalue);
            return h.ToString();
        }

        static string PositionHash(PositionField pf, DMesh3 m)
        {
            var h = new Hasher();
            foreach (int v in m.VertexIndices())
                if (pf.HasPosition(v)) { h.Add(v); h.Add(pf.Origin(v)); h.Add(pf.Orientation(v)); }
            return h.ToString();
        }

        static string PolyHash(DPolyMesh r)
        {
            var h = new Hasher();
            for (int v = 0; v < r.VertexCount; ++v) h.Add(r.GetVertex(v));
            foreach (int i in r.FaceIndices) h.Add(i);
            foreach (int i in r.FaceOffsets) h.Add(i);
            return h.ToString();
        }

        static CrossField Field(DMesh3 m)
        {
            // the field QuadRemesher computes with PreserveCreases (AlignCreases)
            var cf = new CrossField(m) { AlignCreases = true };
            cf.Compute();
            Assert.True(cf.Success, cf.Error);
            return cf;
        }

        static PositionField Positions(DMesh3 m, CrossField cf, bool parallel)
        {
            var pf = new PositionField(m, cf) { TargetEdgeLength = Target, PreserveCreases = true, Parallel = parallel };
            Assert.Equal(PositionFieldStatus.Success, pf.Compute());
            return pf;
        }

        [Fact]
        public void Gear18_FieldPositionsAndExtraction_ArePinned()
        {
            // a platform libm call anywhere in the pipeline changes these bits for some of the thousands of angles (glibc differs from the correctly
            // rounded value in about 0.1 % of arguments, the Windows CRT in a few %)
            DMesh3 m = gear18.Value;
            Assert.Equal("8F12E88272F2CC9F", MeshHash(m));          // the input (QuadFixtureInputTests pins the same)
            CrossField cf = Field(m);
            Assert.Equal("7E9B1EDA00A4165E", FieldHash(cf, m));
            Assert.Equal("071EA1332B78B320", PositionHash(Positions(m, cf, true), m));
            // the extraction without the repair of crossing edges drops one face (the defect of QuadRemesherCrossingTests gear/18), the repair closes it
            var without = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = Target, PreserveCreases = true, SkipCrossingRepair = true };
            Assert.Equal(QuadRemeshStatus.Success, without.Compute());
            Assert.Equal(1, without.DroppedFaces);
            Assert.Equal("A888E4408D38CE34", PolyHash(without.Result));
            var with = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = Target, PreserveCreases = true };
            Assert.Equal(QuadRemeshStatus.Success, with.Compute());
            Assert.Equal(0, with.DroppedFaces);
            Assert.Equal(0, PolyMeshMetrics.Compute(with.Result).BoundaryVertices);
            Assert.Equal("3C7259DD02BD4D3E", PolyHash(with.Result));
        }

        [Fact]
        public void Torus_CurvatureAlignedAndSizeAdapted_IsPinned()
        {
            // the paths the gear does not take: the curvature alignment of the field (FaceDirectionFrom), the integrable frames of the size
            // adaptation (IntegrableFrames: exp, log, sin, cos) and the rotated orientations of the position field
            DMesh3 m = CrossFieldShapes.TorusMesh();
            var q = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = 0.1, CurvatureAlignment = 1, SizeAdaptation = SizeAdaptationMode.ScaleAndRotation };
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Assert.True(q.SizeAdaptationApplied);
            Assert.Equal("4B69ED601A8295F0", PositionHash(q.Positions, m));
            Assert.Equal("D5AEF8EA7B7BE7E6", PolyHash(q.Result));
        }

        [Fact]
        public void Sphere_WithASizeFunction_IsPinned()
        {
            // the size target of IntegrableFrames (the log of the asked size over the target)
            DMesh3 m = CrossFieldShapes.UnitSphere();
            var q = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = 0.15, SizeAdaptation = SizeAdaptationMode.Scale, SizeFunction = p => 0.15 + 0.05 * p.z };
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Assert.True(q.SizeAdaptationApplied);
            Assert.Equal("C51C2AD111C3D75E", PositionHash(q.Positions, m));
        }

        [Fact]
        public void Gear18_ParallelEqualsSerial_AndRepeatedRuns()
        {
            DMesh3 m = gear18.Value;
            CrossField a = Field(new DMesh3(m)), b = Field(new DMesh3(m));
            Assert.Equal(FieldHash(a, m), FieldHash(b, m));
            string serial = PositionHash(Positions(m, a, false), m);
            Assert.Equal(serial, PositionHash(Positions(m, a, true), m));
            Assert.Equal(serial, PositionHash(Positions(m, b, true), m));
            // reuse: Compute twice on one QuadRemesher
            var q = new QuadRemesher(new DMesh3(m)) { TargetEdgeLength = Target, PreserveCreases = true };
            q.Compute();
            string first = PolyHash(q.Result);
            q.Compute();
            Assert.Equal(first, PolyHash(q.Result));
        }

        [Fact]
        public void CrossField_IsInvariantUnderSimilarities()
        {
            // the cross field of the moved mesh is the moved field: the same bits under the 2^k scales (atan2 of scaled arguments is the same
            // correctly rounded angle), the same directions modulo 90 degrees under rotations and translations
            DMesh3 m = gear18.Value;
            CrossField reference = Field(new DMesh3(m));
            int exact = 0;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 moved = Invariance.Transformed(m, s);
                CrossField cf = Field(moved);
                foreach (int v in m.VertexIndices()) {
                    if (!reference.HasField(v)) continue;
                    Vector3d d0 = s.ApplyDirection(reference.VertexDirection(v)), d = cf.VertexDirection(v);
                    if (s.IsExact) {
                        Assert.Equal(reference.VertexDirection(v), d);
                        continue;
                    }
                    Vector3d n = cf.VertexNormal(v);
                    double angle = Math.Atan2(d0.Cross(d).Dot(n), d0.Dot(d));
                    // the deviation from a multiple of 90 degrees; the extreme frame (UTM translation at mm scale) keeps about 7 digits of the coordinates
                    double dev = Math.Abs(Math.Atan2(Math.Sin(4 * angle), Math.Cos(4 * angle))) / 4;
                    Assert.True(dev < (s == Similarity.RotatedUtmMillimetre ? 2e-3 : 1e-6), s.Name + ": vertex " + v + " turned by " + angle);
                }
                if (s.IsExact) ++exact;
            }
            Assert.True(exact >= 3);
        }
    }
}
