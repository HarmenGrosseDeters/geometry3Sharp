using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshMeshCut refuses contact between the cut mesh and the target (issue 856): a pair of triangles whose closed triangles intersect
    /// with a vertex on the other's plane gives Status ContactDetected and leaves Target unchanged; a transversal cut still cuts.
    /// The decision is exact, so it is the same at every scaling by a power of two.
    /// </summary>
    public class MeshMeshCutContactTests
    {
        static DMesh3 Cube(double x, double y, double z, double s)
        {
            var m = new DMesh3();
            var c = new[] { (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1) };
            foreach (var p in c) m.AppendVertex(new Vector3d(x + s * p.Item1, y + s * p.Item2, z + s * p.Item3));
            foreach (var t in new[] { (0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) })
                m.AppendTriangle(t.Item1, t.Item2, t.Item3);
            return m;
        }

        static DMesh3 Mesh(Vector3d[] v, (int, int, int)[] t)
        {
            var m = new DMesh3();
            foreach (var p in v) m.AppendVertex(p);
            foreach (var x in t) m.AppendTriangle(x.Item1, x.Item2, x.Item3);
            return m;
        }

        static DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c) => Mesh(new[] { a, b, c }, new[] { (0, 1, 2) });

        /// <summary> a tetrahedron over the triangle (a, b, c) with apex d </summary>
        static DMesh3 Tet(Vector3d a, Vector3d b, Vector3d c, Vector3d d) =>
            Mesh(new[] { a, b, c, d }, new[] { (0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3) });

        static DMesh3 Flat() => Tri(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0));

        /// <summary> a triangle above the plane z = 0 that touches it in (1, 1, 0), inside Flat(), with that vertex at index i </summary>
        static DMesh3 Pointed(int i)
        {
            Vector3d[] v = new Vector3d[3];
            v[i] = new Vector3d(1, 1, 0);
            v[(i + 1) % 3] = new Vector3d(5, 5, 1);
            v[(i + 2) % 3] = new Vector3d(6, 0.5, 1);
            return Tri(v[0], v[1], v[2]);
        }

        static bool SameVertices(DMesh3 a, DMesh3 b) =>
            a.VertexCount == b.VertexCount && a.TriangleCount == b.TriangleCount
            && a.VertexIndices().All(v => a.GetVertex(v) == b.GetVertex(v));

        static MeshMeshCut.CutStatus Status(DMesh3 target, DMesh3 cut, out DMesh3 before)
        {
            before = new DMesh3(target);
            var op = new MeshMeshCut() { Target = target, CutMesh = cut };
            op.Compute();
            return op.Status;
        }

        // ---- the contact cases: each is refused and Target is unchanged ----

        public static IEnumerable<object[]> ContactCases()
        {
            // a vertex of the cut mesh on a face (inside a triangle of the top face of the cube)
            yield return new object[] { "vertex on face", Cube(0, 0, 0, 1),
                Tet(new Vector3d(0.2, 0.7, 1.4), new Vector3d(0.5, 0.7, 1.4), new Vector3d(0.35, 0.9, 1.5), new Vector3d(0.3, 0.6, 1)) };
            // an edge of the cut mesh on a face
            yield return new object[] { "edge on face", Cube(0, 0, 0, 1),
                Tet(new Vector3d(0.2, 0.9, 1), new Vector3d(0.3, 0.6, 1), new Vector3d(0.6, 0.8, 1.6), new Vector3d(0.1, 0.8, 1.7)) };
            // two coplanar triangles as a star of David: no vertex of one is in the other
            yield return new object[] { "coplanar star", Tri(new Vector3d(0, 0, 0), new Vector3d(6, 0, 0), new Vector3d(3, 6, 0)),
                Tri(new Vector3d(0, 4, 0), new Vector3d(6, 4, 0), new Vector3d(3, -2, 0)) };
            // two coplanar triangles that share only a vertex
            yield return new object[] { "coplanar touch at a vertex", Tri(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)),
                Tri(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, 1, 0)) };
            // coplanar, one triangle inside the other (no edges meet): the endpoints are inside
            yield return new object[] { "coplanar, cut inside target", Tri(new Vector3d(0, 0, 0), new Vector3d(8, 0, 0), new Vector3d(0, 8, 0)),
                Tri(new Vector3d(1, 1, 0), new Vector3d(2, 1, 0), new Vector3d(1, 2, 0)) };
            yield return new object[] { "coplanar, target inside cut", Tri(new Vector3d(1, 1, 0), new Vector3d(2, 1, 0), new Vector3d(1, 2, 0)),
                Tri(new Vector3d(0, 0, 0), new Vector3d(8, 0, 0), new Vector3d(0, 8, 0)) };
            // an edge of the cut triangle in the plane of the target triangle, crossing its hypotenuse at an interior point of both edges
            yield return new object[] { "edge crosses edge in the plane", Tri(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0)),
                Tri(new Vector3d(1, 1, 0), new Vector3d(3, 3, 0), new Vector3d(2, 2, 1)) };
            // a vertex on the plane outside the triangle while another edge pierces the triangle properly
            yield return new object[] { "vertex on the plane, proper crossing", Tri(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0)),
                Tri(new Vector3d(1, 1, 1), new Vector3d(1, 1, -1), new Vector3d(10, 10, 0)) };
            // exactly one of the six orientations is zero: a vertex at index i of the one triangle touches the plane (inside the other triangle), the others
            // are strictly on one side; for the cut triangle (indices 0..2) and for the target triangle (indices 3..5)
            for (int i = 0; i < 3; ++i) {
                yield return new object[] { "one zero orientation, cut vertex " + i, Flat(), Pointed(i) };
                yield return new object[] { "one zero orientation, target vertex " + i, Pointed(i), Flat() };
            }
            // two cubes that share a face plane region (the box of one is against the face of the other)
            yield return new object[] { "shared face", Cube(0, 0, 0, 1), Cube(1, 0, 0, 1) };
            // the cut mesh inside with faces on the faces of the target
            yield return new object[] { "inner cube on the faces", Cube(0, 0, 0, 1), Cube(0.5, 0.5, 0.5, 0.5) };
        }

        [Theory]
        [MemberData(nameof(ContactCases))]
        public void Contact_IsRefused_AndTargetIsUnchanged(string name, DMesh3 target, DMesh3 cut)
        {
            var status = Status(target, cut, out DMesh3 before);
            Assert.True(status == MeshMeshCut.CutStatus.ContactDetected, name + ": " + status);
            Assert.True(SameVertices(before, target), name + ": Target was modified");
        }

        // ---- the case of the fuzz campaign (issue 856) at 13 powers of two: no exception, the same refusal at every scale ----

        static DMesh3 Scaled(DMesh3 m, int e)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) { Vector3d p = c.GetVertex(v); c.SetVertex(v, new Vector3d(Math.ScaleB(p.x, e), Math.ScaleB(p.y, e), Math.ScaleB(p.z, e))); }
            return c;
        }

        static (DMesh3 a, DMesh3 b) Issue856()
        {
            var a = Mesh(new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),
                new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0.125, 1.4375, 7.625) },
                new[] { (0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) });
            var b = Mesh(new[] { new Vector3d(0.01806640625, 0, 0), new Vector3d(0.01810455322265625, 2.47955322265625E-05, 1.1444091796875E-05),
                new Vector3d(0.018096923828125, 3.0517578125E-05, 0), new Vector3d(0.01806640625, 3.0517578125E-05, 0),
                new Vector3d(0.018133163452148438, 1.33514404296875E-05, 0.0001239776611328125), new Vector3d(0.018096923828125, 0, 3.0517578125E-05),
                new Vector3d(0.018096923828125, 3.0517578125E-05, 3.0517578125E-05), new Vector3d(0.01806640625, 3.0517578125E-05, 3.0517578125E-05) },
                new[] { (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) });
            return (a, b);
        }

        [Fact]
        public void FuzzCase_IsRefusedAtEveryPowerOfTwo_WithoutException()
        {
            var (a, b) = Issue856();
            for (int e = -8; e <= 5; ++e) {     // (the artifact's 2^113 .. : the case at 2^108 .. 2^120 of the issue is 2^-7 .. 2^5 here)
                DMesh3 target = Scaled(a, e), cut = Scaled(b, e);
                var status = Status(target, cut, out DMesh3 before);        // before: master threw "update_from_split: unsortable vertex?" at one of these
                Assert.Equal(MeshMeshCut.CutStatus.ContactDetected, status);
                Assert.True(SameVertices(before, target), "2^" + e);
            }
        }

        // ---- a transversal cut still cuts ----

        [Fact]
        public void TransversalCut_StillCuts_AndEqualsTheUnrefusedRun()
        {
            DMesh3 target = Cube(0, 0, 0, 1), cut = Cube(0.3, 0.4, 0.45, 1);
            var op = new MeshMeshCut() { Target = target, CutMesh = cut };
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.Ok, op.Status);
            Assert.True(target.TriangleCount > 12, "the cut inserted vertices: " + target.TriangleCount);
            Assert.True(target.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(0, op.FailedSegmentInsertions);
            DMesh3 plain = Cube(0, 0, 0, 1);
            var op2 = new MeshMeshCut() { Target = plain, CutMesh = Cube(0.3, 0.4, 0.45, 1), RefuseContact = false };
            op2.Compute();
            Assert.True(SameVertices(plain, target));
            op.RemoveContained();
            Assert.True(target.CheckValidity(false, FailMode.ReturnOnly));
        }

        // ---- the same result at every similarity (the exact ones for the contact cases) ----

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void TransversalCut_IsAcceptedUnderEverySimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 target = Invariance.Transformed(Cube(0, 0, 0, 1), s), cut = Invariance.Transformed(Cube(0.3, 0.4, 0.45, 1), s);
            var op = new MeshMeshCut() { Target = target, CutMesh = cut, VertexSnapTol = 1e-5 * s.Scale };
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.Ok, op.Status);
            Assert.True(target.TriangleCount > 12);
            Assert.True(target.CheckValidity(true, FailMode.ReturnOnly));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ContactCases_AreRefusedUnderTheExactSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (!s.IsExact)
                return;     // a rotation or a translation rounds the coordinates, and exact contact is then not contact any more
            foreach (object[] c in ContactCases()) {
                DMesh3 target = Invariance.Transformed((DMesh3)c[1], s), cut = Invariance.Transformed((DMesh3)c[2], s);
                var status = Status(target, cut, out DMesh3 before);
                Assert.True(status == MeshMeshCut.CutStatus.ContactDetected, c[0] + " at " + name + ": " + status);
                Assert.True(SameVertices(before, target), c[0] + " at " + name);
            }
        }

        // ---- reuse: the status is reset by every Compute() ----

        [Fact]
        public void Reuse_StatusFollowsTheInputOfEachCompute()
        {
            var op = new MeshMeshCut() { Target = Cube(0, 0, 0, 1), CutMesh = Cube(1, 0, 0, 1) };
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.ContactDetected, op.Status);
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.ContactDetected, op.Status);
            op.Target = Cube(0, 0, 0, 1);
            op.CutMesh = Cube(0.3, 0.4, 0.45, 1);
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.Ok, op.Status);
            Assert.True(op.Target.TriangleCount > 12);
            op.Target = Cube(0, 0, 0, 1);
            op.CutMesh = Cube(1, 0, 0, 1);
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.ContactDetected, op.Status);
        }

        // ---- RefuseContact = false and MeshBoolean keep the behaviour of before (numbers measured on master) ----

        [Theory]
        [InlineData("shared face", 0, 0, 0, 1, 1, 0, 0, 1, true, 16, 24)]
        [InlineData("overlap", 0, 0, 0, 1, 0.5, 0.5, 0.5, 1, true, 23, 42)]
        [InlineData("coplanar faces overlap", 0, 0, 0, 1, 0.5, 0.25, 0, 1, false, 31, 48)]
        [InlineData("inner cube on the faces", 0, 0, 0, 1, 0.5, 0.5, 0.5, 0.5, false, 15, 20)]
        public void MeshBoolean_OnTouchingCubes_KeepsItsBehaviour(string name, double ax, double ay, double az, double asz, double bx, double by, double bz, double bsz,
            bool closed, int vertices, int triangles)
        {
            var op = new MeshBoolean() { Target = Cube(ax, ay, az, asz), Tool = Cube(bx, by, bz, bsz) };
            bool ok = op.Compute();
            Assert.True(ok == closed, name);
            Assert.Equal(vertices, op.Result.VertexCount);
            Assert.Equal(triangles, op.Result.TriangleCount);
        }

        // ---- cases that must NOT be refused ----

        public static IEnumerable<object[]> AcceptedCases()
        {
            // a vertex on the plane of the target triangle but outside it, the cut triangle above the plane
            yield return new object[] { "vertex on the plane, outside", Flat(), Tri(new Vector3d(9, 9, 0), new Vector3d(10, 9, 1), new Vector3d(9, 10, 1)) };
            // coplanar triangles that are apart
            yield return new object[] { "coplanar, apart", Flat(), Tri(new Vector3d(5, 5, 0), new Vector3d(6, 5, 0), new Vector3d(5, 6, 0)) };
            // a triangle crossing the plane properly (all orientations nonzero) beside the target triangle, not meeting it
            yield return new object[] { "crossing the plane beside", Flat(), Tri(new Vector3d(7, 7, -1), new Vector3d(8, 7, 1), new Vector3d(7, 8, 1)) };
            // a triangle above the plane, apart
            yield return new object[] { "apart above", Flat(), Tri(new Vector3d(1, 1, 3), new Vector3d(2, 1, 3), new Vector3d(1, 2, 3)) };
            // a triangle without an area (collinear corners) far from the other mesh: not contact
            yield return new object[] { "degenerate triangle apart", Flat(), Tri(new Vector3d(20, 0, 5), new Vector3d(21, 0, 5), new Vector3d(22, 0, 5)) };
            yield return new object[] { "degenerate target apart", Tri(new Vector3d(0, 0, 5), new Vector3d(0, 0, 5), new Vector3d(0, 0, 5)), Flat() };
            // degenerate triangles whose boxes overlap the target's but that do not meet it: coplanar and outside, a point outside
            yield return new object[] { "degenerate coplanar segment outside", Flat(), Tri(new Vector3d(3, 3, 0), new Vector3d(4.5, 4.5, 0), new Vector3d(6, 6, 0)) };
            yield return new object[] { "degenerate coplanar point outside", Flat(), Tri(new Vector3d(3, 3, 0), new Vector3d(3, 3, 0), new Vector3d(3, 3, 0)) };
            // two segments in a vertical plane whose xy projections overlap: they do not meet
            yield return new object[] { "degenerate segments in a vertical plane, apart", Tri(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0), new Vector3d(2, 2, 0)),
                Tri(new Vector3d(1, 1, 1), new Vector3d(2, 2, 1), new Vector3d(3, 3, 1)) };
            // two segments on the x axis, apart (their yz projections are the same point)
            yield return new object[] { "degenerate segments on a line, apart", Tri(new Vector3d(0, 0, 0), new Vector3d(0.5, 0, 0), new Vector3d(1, 0, 0)),
                Tri(new Vector3d(2, 0, 0), new Vector3d(2.5, 0, 0), new Vector3d(3, 0, 0)) };
            // one ulp above the plane inside the target triangle: no contact (the cut mesh is above, nothing is zero)
            yield return new object[] { "one ulp above", Flat(), Tri(new Vector3d(1, 1, Math.BitIncrement(0.0)), new Vector3d(5, 5, 1), new Vector3d(6, 0.5, 1)) };
        }

        [Theory]
        [MemberData(nameof(AcceptedCases))]
        public void NoContact_IsNotRefused(string name, DMesh3 target, DMesh3 cut)
        {
            var status = Status(target, cut, out DMesh3 _);
            Assert.True(status == MeshMeshCut.CutStatus.Ok, name + ": " + status);
        }

        public static IEnumerable<object[]> DegenerateContactCases()
        {
            // a triangle without an area that touches: a point on a face, a segment crossing a face, a segment on a face, two segments that cross
            yield return new object[] { "point on a face", Flat(), Tri(new Vector3d(1, 1, 0), new Vector3d(1, 1, 0), new Vector3d(1, 1, 0)) };
            yield return new object[] { "segment through a face", Flat(), Tri(new Vector3d(1, 1, -1), new Vector3d(1, 1, 0.5), new Vector3d(1, 1, 1)) };
            yield return new object[] { "segment in a face", Flat(), Tri(new Vector3d(0.5, 0.5, 0), new Vector3d(1, 1, 0), new Vector3d(2, 1, 0)) };
            yield return new object[] { "segments cross", Tri(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0), new Vector3d(2, 2, 0)),
                Tri(new Vector3d(0, 2, 0), new Vector3d(1, 1, 0), new Vector3d(2, 0, 0)) };
            yield return new object[] { "point on a segment", Tri(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1), new Vector3d(2, 2, 2)),
                Tri(new Vector3d(1.5, 1.5, 1.5), new Vector3d(1.5, 1.5, 1.5), new Vector3d(1.5, 1.5, 1.5)) };
        }

        [Theory]
        [MemberData(nameof(DegenerateContactCases))]
        public void DegenerateContact_IsRefused(string name, DMesh3 target, DMesh3 cut)
        {
            var status = Status(target, cut, out DMesh3 before);
            Assert.True(status == MeshMeshCut.CutStatus.ContactDetected, name + ": " + status);
            Assert.True(SameVertices(before, target), name);
        }

        // ---- after a refusal ----

        [Fact]
        public void AfterARefusal_RemoveContainedAndAppendSegmentsDoNothing()
        {
            // the target inside a larger cut cube whose faces lie on three faces of it: contact, and most of the target is inside the cut mesh
            DMesh3 target = Cube(0, 0, 0, 1);
            var op = new MeshMeshCut() { Target = target, CutMesh = Cube(-1, -1, -1, 2) };
            op.Compute();
            Assert.Equal(MeshMeshCut.CutStatus.ContactDetected, op.Status);
            DMesh3 before = new DMesh3(target);
            op.RemoveContained();
            op.AppendSegments(0.01);
            Assert.True(SameVertices(before, target));
            Assert.Empty(op.CutVertices);
            // a cut that happened before: the next, refused Compute() does not keep its segments
            DMesh3 t2 = Cube(0, 0, 0, 1);
            var op2 = new MeshMeshCut() { Target = t2, CutMesh = Cube(0.3, 0.4, 0.45, 1) };
            op2.Compute();
            op2.Target = Cube(0, 0, 0, 1);
            op2.CutMesh = Cube(1, 0, 0, 1);
            op2.Compute();
            DMesh3 t3 = new DMesh3(op2.Target);
            op2.AppendSegments(0.01);
            Assert.True(SameVertices(t3, op2.Target));
        }

        // ---- ContactTest against an exact integer oracle (separating axes) on random small triangles ----

        static long[] Sub(long[] a, long[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static long[] Cross(long[] a, long[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static long Dot(long[] a, long[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        static bool OracleIntersect(long[][] t, long[][] u)
        {
            long[][] te = { Sub(t[1], t[0]), Sub(t[2], t[1]), Sub(t[0], t[2]) }, ue = { Sub(u[1], u[0]), Sub(u[2], u[1]), Sub(u[0], u[2]) };
            long[] nt = Cross(te[0], te[1]), nu = Cross(ue[0], ue[1]);
            var axes = new List<long[]> { nt, nu };
            foreach (var a in te) foreach (var b in ue) axes.Add(Cross(a, b));
            if (Cross(nt, nu).All(c => c == 0)) {       // coplanar: the in-plane edge normals
                foreach (var a in te) axes.Add(Cross(nt, a));
                foreach (var a in ue) axes.Add(Cross(nu, a));
            }
            foreach (var ax in axes) {
                if (ax.All(c => c == 0)) continue;
                long tmin = t.Min(v => Dot(ax, v)), tmax = t.Max(v => Dot(ax, v)), umin = u.Min(v => Dot(ax, v)), umax = u.Max(v => Dot(ax, v));
                if (tmax < umin || umax < tmin) return false;
            }
            return true;
        }

        static int OracleOrient(long[][] tri, long[] p)
        {
            long d = Dot(Cross(Sub(tri[1], tri[0]), Sub(tri[2], tri[0])), Sub(p, tri[0]));
            return Math.Sign(d);
        }

        [Fact]
        public void TrianglesTouch_AgreesWithAnExactOracle_OnRandomSmallTriangles()
        {
            var rnd = new Random(856);
            int touching = 0, pairs = 0, transversal = 0;
            for (int n = 0; n < 60000; ++n) {
                long[][] t = new long[3][], u = new long[3][];
                for (int i = 0; i < 3; ++i) { t[i] = new long[] { rnd.Next(4), rnd.Next(4), rnd.Next(4) }; u[i] = new long[] { rnd.Next(4), rnd.Next(4), rnd.Next(4) }; }
                if (Cross(Sub(t[1], t[0]), Sub(t[2], t[0])).All(c => c == 0) || Cross(Sub(u[1], u[0]), Sub(u[2], u[0])).All(c => c == 0))
                    continue;       // (degenerate triangles have their own cases)
                bool anyZero = Enumerable.Range(0, 3).Any(i => OracleOrient(t, u[i]) == 0 || OracleOrient(u, t[i]) == 0);
                bool expected = anyZero && OracleIntersect(t, u);
                Triangle3d tt = new Triangle3d(new Vector3d(t[0][0], t[0][1], t[0][2]), new Vector3d(t[1][0], t[1][1], t[1][2]), new Vector3d(t[2][0], t[2][1], t[2][2]));
                Triangle3d uu = new Triangle3d(new Vector3d(u[0][0], u[0][1], u[0][2]), new Vector3d(u[1][0], u[1][1], u[1][2]), new Vector3d(u[2][0], u[2][1], u[2][2]));
                ++pairs;
                if (expected) ++touching; else if (!anyZero && OracleIntersect(t, u)) ++transversal;
                Assert.True(ContactTest.TrianglesTouch(tt, uu) == expected, $"pair {n}: expected {expected}: t={string.Join(";", t.Select(v => string.Join(",", v)))} u={string.Join(";", u.Select(v => string.Join(",", v)))}");
            }
            Assert.True(touching > 5000 && transversal > 1000 && pairs - touching > 5000, $"the sample covers both outcomes: {touching} touching, {transversal} transversal, {pairs} pairs");
        }
    }
}
