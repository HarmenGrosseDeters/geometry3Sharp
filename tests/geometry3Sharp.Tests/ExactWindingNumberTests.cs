using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Public-API tests for the exact ray-plus-boundary generalized winding number in
    /// <see cref="DMeshAABBTree3.WindingNumber"/> and <see cref="MeshWindingNumberGrid"/>.
    /// Kept free of internal hooks so <c>tools/new_tests_on_base.py</c> can run every test against the base library.
    /// </summary>
    public class ExactWindingNumberTests
    {
        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static int FloatBits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0);

        static double BruteForce(DMesh3 mesh, Vector3d p)
        {
            double sum = 0.0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                sum += MathUtil.TriSolidAngle(a, b, c, ref p);
            }
            return sum / MathUtil.FourPI;
        }

        static DMesh3 Box(double size, Vector3d center)
        {
            return new TrivialBox3Generator() { Box = new Box3d(center, 0.5 * size * Vector3d.One) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(double radius = 1.0, int edgeVertices = 14)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        static DMesh3 OpenSphere(double radius = 1.0, int edgeVertices = 14, double cutZ = 0.4)
        {
            DMesh3 m = Sphere(radius, edgeVertices);
            foreach (int tid in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(tid).z > cutZ * radius)
                    m.RemoveTriangle(tid, true, false);
            return m;
        }

        static DMesh3 OpenBox(double size = 2.0)
        {
            DMesh3 m = Box(size, Vector3d.Zero);
            foreach (int tid in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(tid).x > 0.49 * size)
                    m.RemoveTriangle(tid, true, false);
            return m;
        }

        static DMesh3 UnweldedCopy(DMesh3 src)
        {
            DMesh3 soup = new DMesh3();
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in src.TriangleIndices()) {
                src.GetTriVertices(tid, ref a, ref b, ref c);
                int va = soup.AppendVertex(a);
                int vb = soup.AppendVertex(b);
                int vc = soup.AppendVertex(c);
                soup.AppendTriangle(va, vb, vc);
            }
            return soup;
        }

        static DMesh3 DisconnectedSoup(int triCount, int seed)
        {
            var rnd = new Random(seed);
            DMesh3 soup = new DMesh3();
            for (int i = 0; i < triCount; ++i) {
                Vector3d o = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                Vector3d d1 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.25;
                Vector3d d2 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.25;
                int va = soup.AppendVertex(o);
                int vb = soup.AppendVertex(o + d1);
                int vc = soup.AppendVertex(o + d2);
                soup.AppendTriangle(va, vb, vc);
            }
            return soup;
        }

        static Vector3d[] RandomQueries(int count, int seed, double halfExtent = 1.6)
        {
            var rnd = new Random(seed);
            var pts = new Vector3d[count];
            for (int i = 0; i < count; ++i)
                pts[i] = new Vector3d((rnd.NextDouble() * 2 - 1) * halfExtent, (rnd.NextDouble() * 2 - 1) * halfExtent, (rnd.NextDouble() * 2 - 1) * halfExtent);
            return pts;
        }

        [Fact]
        public void ClosedMeshes_ExactIntegerAndMatchesBruteForce()
        {
            DMesh3 hollow = Sphere(2.0, 10);
            DMesh3 cavity = Sphere(1.0, 10);
            cavity.ReverseOrientation();
            MeshEditor.Append(hollow, cavity);

            DMesh3 doubleShell = Sphere(2.0, 10);
            MeshEditor.Append(doubleShell, Sphere(1.0, 10));

            DMesh3 inverted = Sphere(1.0, 10);
            inverted.ReverseOrientation();

            foreach (DMesh3 m in new[] { Box(2.0, Vector3d.Zero), Sphere(1.0, 10), hollow, doubleShell, inverted }) {
                var tree = new DMeshAABBTree3(m, true);
                foreach (Vector3d p in RandomQueries(100, 11, 2.5)) {
                    double exact = tree.WindingNumber(p);
                    double expected = BruteForce(m, p);
                    Assert.Equal(Math.Round(exact), exact); // exact integer off-surface
                    Assert.True(Math.Abs(exact - expected) < 1e-10, $"at {p}: exact={exact:R} vs brute={expected:R}");
                }
            }

            var hollowTree = new DMeshAABBTree3(hollow, true);
            Assert.Equal(0.0, hollowTree.WindingNumber(new Vector3d(0.05, 0.03, -0.02)));
            Assert.Equal(1.0, hollowTree.WindingNumber(new Vector3d(1.5, 0.0, 0.0)));
            Assert.Equal(0.0, hollowTree.WindingNumber(new Vector3d(3.0, 0.0, 0.0)));

            var doubleTree = new DMeshAABBTree3(doubleShell, true);
            Assert.Equal(2.0, doubleTree.WindingNumber(new Vector3d(0.05, 0.03, -0.02)));
            Assert.Equal(1.0, doubleTree.WindingNumber(new Vector3d(1.5, 0.0, 0.0)));
            Assert.Equal(0.0, doubleTree.WindingNumber(new Vector3d(3.0, 0.0, 0.0)));
        }

        [Fact]
        public void OpenNonManifoldAndInconsistentMeshes_MatchBruteForce()
        {
            DMesh3 openBox = OpenBox(2.0);

            DMesh3 twoHoles = Sphere(1.0, 10);
            foreach (int tid in twoHoles.TriangleIndices().ToArray()) {
                double z = twoHoles.GetTriCentroid(tid).z;
                if (z > 0.5 || z < -0.5)
                    twoHoles.RemoveTriangle(tid, true, false);
            }

            DMesh3 flippedCap = Sphere(1.0, 10);
            foreach (int tid in flippedCap.TriangleIndices().ToArray())
                if (flippedCap.GetTriCentroid(tid).z > 0.2)
                    flippedCap.ReverseTriOrientation(tid);

            DMesh3 tJunction = Box(2.0, Vector3d.Zero);
            int w0 = tJunction.AppendVertex(new Vector3d(0, -1, -1));
            int w1 = tJunction.AppendVertex(new Vector3d(0, 1, -1));
            int w2 = tJunction.AppendVertex(new Vector3d(0, 1, 1));
            int w3 = tJunction.AppendVertex(new Vector3d(0, -1, 1));
            tJunction.AppendTriangle(w0, w1, w2);
            tJunction.AppendTriangle(w0, w2, w3);

            DMesh3 dupSame = OpenBox(2.0);
            MeshEditor.Append(dupSame, OpenBox(2.0));

            DMesh3 dupOpp = OpenBox(2.0);
            DMesh3 revOpenBox = OpenBox(2.0);
            revOpenBox.ReverseOrientation();
            MeshEditor.Append(dupOpp, revOpenBox);

            foreach (DMesh3 m in new[] { openBox, twoHoles, flippedCap, tJunction, dupSame, dupOpp }) {
                var tree = new DMeshAABBTree3(m, true);
                foreach (Vector3d p in RandomQueries(100, 23, 2.0)) {
                    double exact = tree.WindingNumber(p);
                    double brute = BruteForce(m, p);
                    Assert.True(Math.Abs(exact - brute) < 1e-11, $"at {p}: exact={exact:R} vs brute={brute:R}");
                }
            }

            var oppTree = new DMeshAABBTree3(dupOpp, true);
            foreach (Vector3d p in RandomQueries(50, 29, 2.0))
                Assert.Equal(0.0, oppTree.WindingNumber(p));
        }

        [Fact]
        public void UnweldedClosedSoupAndDisconnectedSoup_MatchBruteForce()
        {
            DMesh3 closed = Sphere(1.0, 12);
            DMesh3 unwelded = UnweldedCopy(closed);
            Assert.False(unwelded.IsClosed());
            var unweldedTree = new DMeshAABBTree3(unwelded, true);
            var closedTree = new DMeshAABBTree3(closed, true);
            foreach (Vector3d p in RandomQueries(150, 31, 1.8))
                Assert.Equal(closedTree.WindingNumber(p), unweldedTree.WindingNumber(p));

            DMesh3 soup = DisconnectedSoup(120, 37);
            var soupTree = new DMeshAABBTree3(soup, true);
            foreach (Vector3d p in RandomQueries(80, 41, 1.8)) {
                double exact = soupTree.WindingNumber(p);
                double brute = BruteForce(soup, p);
                Assert.True(Math.Abs(exact - brute) < 1e-13, $"at {p}: exact={exact:R} vs brute={brute:R}");
            }
        }

        [Fact]
        public void ClosedSphere_NearEdgeAndCentroidPoints_ExactOrient3dWithoutTriSolidAngleCancellation()
        {
            // Points 1 ULP off a closed convex sphere (rounded edge midpoints and centroids) suffer O(eps)/O(eps)
            // cancellation in floating-point MathUtil.TriSolidAngle (the legacy WindingCache returns non-integer
            // values like -0.12 or +1.23 and flips exterior points to +0.96). ExactWindingCache decides side via
            // ExactPredicates.Orient3d and returns exact 0 or 1. (Fails on base.)
            DMesh3 sphere = Sphere(1.0, 20);
            var tree = new DMeshAABBTree3(sphere, true);
            int checkedOutside = 0, checkedInsideCentroid = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in sphere.TriangleIndices()) {
                sphere.GetTriVertices(tid, ref a, ref b, ref c);

                // Edge midpoint strictly outside the supporting plane of triangle tid -> strictly outside convex sphere
                Vector3d mid = 0.5 * (a + b);
                int oMid = ExactPredicates.Orient3d(a, b, c, mid);
                if (oMid > 0) {
                    Assert.Equal(0.0, tree.WindingNumber(mid));
                    checkedOutside++;
                }

                // Face centroid strictly off-plane -> strictly outside (o > 0 -> 0) or strictly inside (o < 0 -> 1)
                Vector3d cent = (a + b + c) / 3.0;
                int oCent = ExactPredicates.Orient3d(a, b, c, cent);
                if (oCent > 0) {
                    Assert.Equal(0.0, tree.WindingNumber(cent));
                    checkedOutside++;
                } else if (oCent < 0) {
                    Assert.Equal(1.0, tree.WindingNumber(cent));
                    checkedInsideCentroid++;
                }
            }
            Assert.True(checkedOutside > 500);
            Assert.True(checkedInsideCentroid > 500);
        }

        [Fact]
        public void AdversarialRayDegeneracies_ClosedAndOpenMeshes_NoBranchCutJumps()
        {
            // 1. Closed octahedron: +x ray through interior vertices, interior edges, and tangent vertices
            var oct = new DMesh3();
            var ov = new[] {
                new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0),
                new Vector3d(0, 1, 0), new Vector3d(0, -1, 0),
                new Vector3d(0, 0, 1), new Vector3d(0, 0, -1)
            };
            foreach (var v in ov) oct.AppendVertex(v);
            int[] ot = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (int i = 0; i < ot.Length; i += 3) oct.AppendTriangle(ot[i], ot[i + 1], ot[i + 2]);
            var octTree = new DMeshAABBTree3(oct, true);

            foreach (Vector3d p in new[] {
                new Vector3d(0.0, 0.0, 0.0),
                new Vector3d(-0.5, 0.0, 0.0),
                new Vector3d(0.0, 0.5, 0.0),
                new Vector3d(0.0, 0.0, 0.5),
                new Vector3d(0.0, -0.5, 0.0),
                new Vector3d(0.0, 0.0, -0.5),
                new Vector3d(-0.25, 0.25, 0.25)
            }) {
                Assert.Equal(1.0, octTree.WindingNumber(p));
            }
            foreach (Vector3d p in new[] {
                new Vector3d(-2.0, 0.0, 0.0),
                new Vector3d(-2.0, 0.5, 0.0),
                new Vector3d(-2.0, 0.0, 0.5),
                new Vector3d(-2.0, 1.0, 0.0),
                new Vector3d(-2.0, 0.0, 1.0),
                new Vector3d(-2.0, -1.0, 0.0),
                new Vector3d(-2.0, 0.0, -1.0)
            }) {
                Assert.Equal(0.0, octTree.WindingNumber(p));
            }

            // 2. Open box missing +x face: +x rays through boundary edge midpoints and corners at 0 and +-1e-15..1e-9.
            DMesh3 openBox = OpenBox(2.0);
            var openTree = new DMeshAABBTree3(openBox, true);
            double[] offsets = { -1e-9, -1e-12, -1e-15, 0.0, 1e-15, 1e-12, 1e-9 };
            Vector3d[] cutTargets = {
                new Vector3d(-2.0, 1.0, 0.0),
                new Vector3d(-2.0, -1.0, 0.0),
                new Vector3d(-2.0, 0.0, 1.0),
                new Vector3d(-2.0, 0.0, -1.0),
                new Vector3d(-2.0, 1.0, 1.0),
                new Vector3d(-2.0, -1.0, 1.0),
                new Vector3d(-2.0, 1.0, -1.0),
                new Vector3d(-2.0, -1.0, -1.0)
            };
            foreach (Vector3d basePt in cutTargets) {
                double wCenter = openTree.WindingNumber(basePt);
                double bruteCenter = BruteForce(openBox, basePt);
                Assert.True(Math.Abs(wCenter - bruteCenter) < 1e-10, $"base {basePt:R}: exact={wCenter:R} vs brute={bruteCenter:R}");
                foreach (double dy in offsets) {
                    foreach (double dz in offsets) {
                        Vector3d p = new Vector3d(basePt.x, basePt.y + dy, basePt.z + dz);
                        double w = openTree.WindingNumber(p);
                        double brute = BruteForce(openBox, p);
                        Assert.True(Math.Abs(w - brute) < 1e-10, $"at {p:R}: exact={w:R} vs brute={brute:R}");
                        Assert.True(Math.Abs(w - wCenter) < 1e-6, $"branch-cut jump at {p:R}: {w:R} vs {wCenter:R}");
                    }
                }
            }

            // 3. Single tilted triangle in x = 1 plane: sweep across every boundary edge and vertex at +-1e-15..1e-9
            DMesh3 single = new DMesh3();
            int s0 = single.AppendVertex(new Vector3d(1, 0, 0));
            int s1 = single.AppendVertex(new Vector3d(1, 2, 0.5));
            int s2 = single.AppendVertex(new Vector3d(1, 0.5, 2));
            single.AppendTriangle(s0, s1, s2);
            var singleTree = new DMeshAABBTree3(single, true);
            foreach (Vector3d target in new[] {
                new Vector3d(0, 1.0, 0.25),
                new Vector3d(0, 1.25, 1.25),
                new Vector3d(0, 0.25, 1.0),
                new Vector3d(0, 0, 0),
                new Vector3d(0, 2, 0.5),
                new Vector3d(0, 0.5, 2)
            }) {
                double w0 = singleTree.WindingNumber(target);
                Assert.True(Math.Abs(w0 - BruteForce(single, target)) < 1e-12, $"target={target:R}");
                foreach (double d in offsets) {
                    Vector3d p1 = new Vector3d(target.x, target.y + d, target.z);
                    Vector3d p2 = new Vector3d(target.x, target.y, target.z + d);
                    Assert.True(Math.Abs(singleTree.WindingNumber(p1) - BruteForce(single, p1)) < 1e-11, $"p1={p1:R}");
                    Assert.True(Math.Abs(singleTree.WindingNumber(p2) - BruteForce(single, p2)) < 1e-11, $"p2={p2:R}");
                    Assert.True(Math.Abs(singleTree.WindingNumber(p1) - w0) < 1e-6, $"jump p1={p1:R}");
                    Assert.True(Math.Abs(singleTree.WindingNumber(p2) - w0) < 1e-6, $"jump p2={p2:R}");
                }
            }
        }

        [Fact]
        public void TiltedNearOpenEdge_MixedExponentsAndCollinearVertex_AccurateAndFinite()
        {
            // 1. Tilted boundary edges queried ~1e-13 from open segment with oyz != 0:
            //    2D x-projection ox can disagree with -den; must not flip or clamp den (which would err by 0.165-0.25).
            //    Reference values computed at 60-digit precision; at ~1e-13 from an open edge, double cancellation is O(1e-4).
            var tiltedCases = new[] {
                (a: new Vector3d(-1, -1, -1), b: new Vector3d(1, 1, 1), c: new Vector3d(0, -1, 2), p: new Vector3d(9.9e-14, 1e-13, -1e-13), expected: -0.05238301975195226),
                (a: new Vector3d(-100, -1, -1), b: new Vector3d(100, 1, 1), c: new Vector3d(0, -1, 2), p: new Vector3d(9.9e-12, 1e-13, -1e-13), expected: -0.0729888594079798),
                (a: new Vector3d(-100, -1, -1), b: new Vector3d(100, 1, 1), c: new Vector3d(0, -1, 2), p: new Vector3d(-9.9e-12, -1e-13, 1e-13), expected: 0.4270111405920074)
            };
            foreach (var tc in tiltedCases) {
                DMesh3 m = new DMesh3();
                int v0 = m.AppendVertex(tc.a), v1 = m.AppendVertex(tc.b), v2 = m.AppendVertex(tc.c);
                m.AppendTriangle(v0, v1, v2);
                var tree = new DMeshAABBTree3(m, true);
                double w = tree.WindingNumber(tc.p);
                Assert.True(Math.Abs(w - tc.expected) < 5e-4, $"tilted edge at {tc.p:R}: exact={w:R} vs ref={tc.expected:R}");
            }

            // 2. Degenerate collinear triangles near +y queried at middle vertex b (exact winding = 0):
            //    tests cancellation-free vertex limit denominator den0 across relExp = -60 and relExp = -61.
            foreach (double dx in new[] { 1e-18, 5e-19 }) {
                DMesh3 col = new DMesh3();
                int c0 = col.AppendVertex(new Vector3d(0, 0, 0));
                int c1 = col.AppendVertex(new Vector3d(dx, 1.0, 1e-22));
                int c2 = col.AppendVertex(new Vector3d(2.0 * dx, 2.0, 2e-22));
                col.AppendTriangle(c0, c1, c2);
                var colTree = new DMeshAABBTree3(col, true);
                double wCol = colTree.WindingNumber(new Vector3d(dx, 1.0, 1e-22));
                Assert.True(Math.Abs(wCol) < 1e-12, $"collinear vertex (dx={dx:R}): {wCol:R}");
            }

            // 3. Mixed-exponent open triangle: |x| = 2^500 vs |y,z| = 2^-600 (must not underflow y/z to 0)
            double big = Math.Pow(2.0, 500), tiny = Math.Pow(2.0, -600);
            DMesh3 mixed = new DMesh3();
            int m0 = mixed.AppendVertex(new Vector3d(big, tiny, 0));
            int m1 = mixed.AppendVertex(new Vector3d(big, -tiny, tiny));
            int m2 = mixed.AppendVertex(new Vector3d(big, -tiny, -tiny));
            mixed.AppendTriangle(m0, m1, m2);
            var mixedTree = new DMeshAABBTree3(mixed, true);
            // Viewed from the origin, the triangle spans an infinitesimal solid angle (~2^-2200) -> winding ~ 0.
            double wMixed = mixedTree.WindingNumber(Vector3d.Zero);
            Assert.True(IsFinite(wMixed) && Math.Abs(wMixed) < 1e-12, $"mixed exponent: {wMixed:R}");

            // 4. Huge 1e308-span triangle queried on-surface: must remain finite (no subtraction overflow to NaN)
            DMesh3 huge = new DMesh3();
            int h0 = huge.AppendVertex(new Vector3d(1e308, 0, 0));
            int h1 = huge.AppendVertex(new Vector3d(-1e308, 0, 0));
            int h2 = huge.AppendVertex(new Vector3d(0, 1e308, 0));
            huge.AppendTriangle(h0, h1, h2);
            var hugeTree = new DMeshAABBTree3(huge, true);
            Assert.True(IsFinite(hugeTree.WindingNumber(new Vector3d(-0.9e308, 0, 0))));
            Assert.True(IsFinite(hugeTree.WindingNumber(new Vector3d(0, 0.25e308, 0.25e308))));
        }

        [Fact]
        public void OnSurfacePoints_FacesEdgesVerticesAndOpenBoundaryEdges_MatchBruteForce()
        {
            DMesh3 cube = Box(2.0, Vector3d.Zero);
            var cubeTree = new DMeshAABBTree3(cube, true);
            foreach (int vid in cube.VertexIndices())
                Assert.Equal(0.125, cubeTree.WindingNumber(cube.GetVertex(vid)), 12);

            foreach (Vector3d p in new[] {
                new Vector3d(1, 1, 0), new Vector3d(-1, 1, 0), new Vector3d(1, -1, 0),
                new Vector3d(0, 1, 1), new Vector3d(1, 0, 1), new Vector3d(0, -1, -1)
            }) {
                double w = cubeTree.WindingNumber(p);
                double brute = BruteForce(cube, p);
                Assert.True(Math.Abs(w - brute) < 1e-12, $"cube edge {p}: exact={w:R} vs brute={brute:R}");
            }

            // Strictly interior face points (cls == 2) on both closed cube and open box
            foreach (Vector3d p in new[] {
                new Vector3d(1, 0.25, -0.35), new Vector3d(-1, -0.4, 0.2),
                new Vector3d(0.3, 1, -0.2), new Vector3d(-0.3, -1, 0.4),
                new Vector3d(0.2, -0.3, 1), new Vector3d(-0.2, 0.3, -1)
            }) {
                double w = cubeTree.WindingNumber(p);
                double brute = BruteForce(cube, p);
                Assert.True(Math.Abs(w - brute) < 1e-12, $"cube face {p}: exact={w:R} vs brute={brute:R}");
            }

            DMesh3 openBox = OpenBox(2.0);
            var openTree = new DMeshAABBTree3(openBox, true);
            foreach (int vid in openBox.VertexIndices()) {
                Vector3d v = openBox.GetVertex(vid);
                double w = openTree.WindingNumber(v);
                double brute = BruteForce(openBox, v);
                Assert.True(Math.Abs(w - brute) < 1e-12, $"openBox vertex {v}: exact={w:R} vs brute={brute:R}");
            }
            foreach (Vector3d p in new[] { new Vector3d(-1, 0.25, -0.35), new Vector3d(0.2, 1, -0.3), new Vector3d(-0.2, -0.3, 1) }) {
                double w = openTree.WindingNumber(p);
                double brute = BruteForce(openBox, p);
                Assert.True(Math.Abs(w - brute) < 1e-12, $"openBox face {p}: exact={w:R} vs brute={brute:R}");
            }

            Vector3d[][] tris = {
                new[] { new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0) },
                new[] { new Vector3d(0, 0, 0), new Vector3d(0, 2, 0), new Vector3d(0, 0, 2) },
                new[] { new Vector3d(0, 0, 0), new Vector3d(2, 2, 0), new Vector3d(0, 0, 2) },
                new[] { new Vector3d(0, 0, 0), new Vector3d(2, 2, 2), new Vector3d(0, 2, -2) }
            };
            foreach (Vector3d[] tv in tris) {
                DMesh3 m = new DMesh3();
                int i0 = m.AppendVertex(tv[0]), i1 = m.AppendVertex(tv[1]), i2 = m.AppendVertex(tv[2]);
                m.AppendTriangle(i0, i1, i2);
                var t = new DMeshAABBTree3(m, true);
                foreach (Vector3d p in new[] { tv[0], tv[1], tv[2], 0.5 * (tv[0] + tv[1]), 0.5 * (tv[1] + tv[2]), 0.5 * (tv[2] + tv[0]) }) {
                    double w = t.WindingNumber(p);
                    double brute = BruteForce(m, p);
                    Assert.True(IsFinite(w));
                    Assert.True(Math.Abs(w - brute) < 1e-12, $"tri point {p}: exact={w:R} vs brute={brute:R}");
                }
            }
        }

        [Fact]
        public void BowtieVerticesBottomUpTreeNonFiniteTrianglesAndDegenerateOnlyMesh_HandledSafely()
        {
            // 1. Interior bowtie vertex (two closed boxes sharing vertex (0,0,0)) & boundary bowtie (two open triangles)
            DMesh3 bowtieClosed = Box(2.0, new Vector3d(1, 1, 1));
            MeshEditor.Append(bowtieClosed, Box(2.0, new Vector3d(-1, -1, -1)));
            var btClosedTree = new DMeshAABBTree3(bowtieClosed, true);
            Assert.Equal(1.0, btClosedTree.WindingNumber(new Vector3d(0.5, 0.5, 0.5)));
            Assert.Equal(1.0, btClosedTree.WindingNumber(new Vector3d(-0.5, -0.5, -0.5)));
            Assert.Equal(0.0, btClosedTree.WindingNumber(new Vector3d(0.5, -0.5, 0.5)));
            Assert.Equal(0.25, btClosedTree.WindingNumber(Vector3d.Zero), 12);

            DMesh3 bowtieOpen = new DMesh3();
            int bv0 = bowtieOpen.AppendVertex(Vector3d.Zero);
            int bv1 = bowtieOpen.AppendVertex(new Vector3d(1, 1, 0));
            int bv2 = bowtieOpen.AppendVertex(new Vector3d(1, 0, 1));
            int bv3 = bowtieOpen.AppendVertex(new Vector3d(-1, -1, 0));
            int bv4 = bowtieOpen.AppendVertex(new Vector3d(-1, 0, -1));
            bowtieOpen.AppendTriangle(bv0, bv1, bv2);
            bowtieOpen.AppendTriangle(bv0, bv3, bv4);
            var btOpenTree = new DMeshAABBTree3(bowtieOpen, true);
            foreach (Vector3d p in RandomQueries(50, 83, 1.5)) {
                double w = btOpenTree.WindingNumber(p);
                Assert.True(Math.Abs(w - BruteForce(bowtieOpen, p)) < 1e-12);
            }

            // 2. BottomUpFromOneRings BVH strategy (produces single-child internal nodes iChild1 < 0)
            foreach (DMesh3 m in new[] { Sphere(1.0, 8), OpenSphere(1.0, 8, 0.4) }) {
                var buTree = new DMeshAABBTree3(m, false);
                buTree.Build(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings);
                var tdTree = new DMeshAABBTree3(m, true);
                foreach (Vector3d p in RandomQueries(60, 89, 1.5))
                    Assert.True(Math.Abs(buTree.WindingNumber(p) - tdTree.WindingNumber(p)) < 1e-12);
            }

            // 3. Mesh containing a non-finite triangle alongside a valid closed box: non-finite tri skipped cleanly
            DMesh3 withNaN = Box(2.0, Vector3d.Zero);
            int nv0 = withNaN.AppendVertex(new Vector3d(double.NaN, 0, 0));
            int nv1 = withNaN.AppendVertex(new Vector3d(0, 1, 0));
            int nv2 = withNaN.AppendVertex(new Vector3d(0, 0, 1));
            withNaN.AppendTriangle(nv0, nv1, nv2);
            var nanTree = new DMeshAABBTree3(withNaN, true);
            Assert.Equal(1.0, nanTree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)));
            Assert.Equal(0.0, nanTree.WindingNumber(new Vector3d(3.0, 0.0, 0.0)));

            // 4. Degenerate-only mesh (collinear + coincident-vertex triangles only)
            DMesh3 degenOnly = new DMesh3();
            int dg0 = degenOnly.AppendVertex(new Vector3d(0, 0, 0));
            int dg1 = degenOnly.AppendVertex(new Vector3d(1, 0, 0));
            int dg2 = degenOnly.AppendVertex(new Vector3d(2, 0, 0));
            int dg3 = degenOnly.AppendVertex(new Vector3d(0, 0, 0));
            degenOnly.AppendTriangle(dg0, dg1, dg2);
            degenOnly.AppendTriangle(dg0, dg3, dg1);
            var dgTree = new DMeshAABBTree3(degenOnly, true);
            foreach (Vector3d p in RandomQueries(30, 97, 2.0)) {
                double w = dgTree.WindingNumber(p);
                Assert.True(IsFinite(w) && Math.Abs(w) < 1e-12, $"degenOnly at {p}: {w:R}");
            }
        }

        [Fact]
        public void DegenerateEmptyNonFiniteAndMutationGuard_HandledSafely()
        {
            var emptyTree = new DMeshAABBTree3(new DMesh3(), true);
            Assert.Equal(0.0, emptyTree.WindingNumber(Vector3d.Zero));

            DMesh3 cube = Box(2.0, Vector3d.Zero);
            var tree = new DMeshAABBTree3(cube, true);
            Assert.Equal(0.0, tree.WindingNumber(new Vector3d(double.NaN, 0, 0)));
            Assert.Equal(0.0, tree.WindingNumber(new Vector3d(double.PositiveInfinity, 0, 0)));
            Assert.Equal(0.0, tree.WindingNumber(new Vector3d(0, double.NegativeInfinity, 0)));

            DMesh3 withDegen = Box(2.0, Vector3d.Zero);
            int d0 = withDegen.AppendVertex(new Vector3d(0, 0, 0));
            int d1 = withDegen.AppendVertex(new Vector3d(1, 0, 0));
            int d2 = withDegen.AppendVertex(new Vector3d(2, 0, 0));
            withDegen.AppendTriangle(d0, d1, d2);
            var degenTree = new DMeshAABBTree3(withDegen, true);
            Assert.Equal(1.0, degenTree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)));
            Assert.Equal(0.0, degenTree.WindingNumber(new Vector3d(3.0, 0.0, 0.0)));

            DMesh3 mutable = Box(2.0, Vector3d.Zero);
            var mTree = new DMeshAABBTree3(mutable, true);
            Assert.Equal(1.0, mTree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)));
            mutable.ReverseOrientation();
            Assert.Throws<Exception>(() => mTree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)));
            mTree.Build();
            Assert.Equal(-1.0, mTree.WindingNumber(new Vector3d(0.1, 0.2, 0.3)));
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void OpenMesh_InvariantUnderAllSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 open = OpenSphere(1.0, 10, 0.35);
            DMesh3 openT = Invariance.Transformed(open, sim);
            var tree = new DMeshAABBTree3(open, true);
            var treeT = new DMeshAABBTree3(openT, true);

            foreach (Vector3d p in RandomQueries(60, 53, 1.5)) {
                Vector3d q = sim.Apply(p);
                double a = tree.WindingNumber(p);
                double b = treeT.WindingNumber(q);
                if (sim.IsExact) {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b));
                } else {
                    // Exact against the transformed mesh itself (even under 1e-3 + UTM coordinate rounding)
                    double bruteT = BruteForce(openT, q);
                    Assert.True(Math.Abs(b - bruteT) < 1e-11, $"{name} vs brute at {p}: {b:R} vs {bruteT:R}");
                    Assert.True(Math.Abs(a - b) < 1e-4, $"{name} invariance at {p}: {a:R} vs {b:R}");
                }
            }
        }

        [Theory]
        [InlineData(-1000)]
        [InlineData(-500)]
        [InlineData(-200)]
        [InlineData(200)]
        [InlineData(500)]
        [InlineData(1000)]
        public void OpenMesh_BitIdenticalAcrossExtremePowerOfTwoScales(int exponent)
        {
            double s = Math.Pow(2.0, exponent);
            DMesh3 open = OpenBox(2.0);
            DMesh3 scaled = new DMesh3(open);
            MeshTransforms.Scale(scaled, s);
            var tree = new DMeshAABBTree3(open, true);
            var scaledTree = new DMeshAABBTree3(scaled, true);

            foreach (Vector3d p in RandomQueries(40, 59, 1.8)) {
                double expected = tree.WindingNumber(p);
                double actual = scaledTree.WindingNumber(s * p);
                Assert.True(IsFinite(actual));
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
            }
        }

        [Fact]
        public void ConcurrentFirstCallInitialization_ClosedAndOpen_MatchesSerial()
        {
            foreach (DMesh3 m in new[] { Sphere(1.0, 12), OpenSphere(1.0, 12, 0.35) }) {
                Vector3d[] pts = RandomQueries(100, 67, 1.6);
                var refTree = new DMeshAABBTree3(m, true);
                double[] expected = pts.Select(p => refTree.WindingNumber(p)).ToArray();

                var concurrentTree = new DMeshAABBTree3(m, true);
                Task<double[]>[] tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
                    var res = new double[pts.Length];
                    for (int i = 0; i < pts.Length; ++i)
                        res[i] = concurrentTree.WindingNumber(pts[i]);
                    return res;
                })).ToArray();
                // Block on the pool tasks instead of awaiting them (issue #698): the continuation of an await goes back to
                // xUnit's MaxConcurrencySyncContext, whose worker threads run the other test classes, so it resumed only at
                // the end of the full run (410-544 s on CI, 0.01 s alone). The tasks themselves run on the thread pool.
                Task.WaitAll(tasks);
                double[][] all = tasks.Select(t => t.Result).ToArray();

                foreach (double[] res in all)
                    for (int i = 0; i < pts.Length; ++i)
                        Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(res[i]));
            }
        }

        [Theory]
        [InlineData(MeshWindingNumberGrid.ComputeModes.FullGrid)]
        [InlineData(MeshWindingNumberGrid.ComputeModes.NarrowBand)]
        public void MeshWindingNumberGrid_RowRayMatchesSinglePoint(MeshWindingNumberGrid.ComputeModes mode)
        {
            DMesh3[] meshes = {
                Sphere(1.0, 10),
                OpenSphere(1.0, 10, 0.35),
                Box(2.0, Vector3d.Zero),
                DisconnectedSoup(80, 71)
            };
            foreach (DMesh3 m in meshes) {
                double cellSize = m.CachedBounds.MaxDim / 16.0;
                var tree = new DMeshAABBTree3(m, true);
                var grid = new MeshWindingNumberGrid(m, tree, cellSize) {
                    ComputeMode = mode,
                    UseFastWindingNumber = false,
                    WantMeshSDFGrid = mode == MeshWindingNumberGrid.ComputeModes.NarrowBand
                };
                grid.Compute();
                Assert.True(grid.WindingEvaluations > 0);

                Vector3i dims = grid.Dimensions;
                Vector3d origin = grid.GridOriginD;
                double dx = (float)grid.CellSize;
                DenseGrid3f sdf = mode == MeshWindingNumberGrid.ComputeModes.NarrowBand ? grid.SDFGrid.Grid : null;
                int evaluated = 0;
                for (int k = 0; k < dims.z; ++k) {
                    double z = (double)k * dx + origin.z;
                    for (int j = 0; j < dims.y; ++j) {
                        double y = (double)j * dx + origin.y;
                        for (int i = 0; i < dims.x; ++i) {
                            float val = grid[i, j, k];
                            Assert.True(IsFinite(val));
                            if (sdf != null && !(sdf[i, j, k] < grid.CellSize))
                                continue;
                            double x = (double)i * dx + origin.x;
                            float expected = (float)tree.WindingNumber(new Vector3d(x, y, z));
                            Assert.Equal(FloatBits(expected), FloatBits(val));
                            evaluated++;
                        }
                    }
                }
                Assert.True(evaluated > 0);
            }
        }

        [Theory]
        [InlineData(MeshWindingNumberGrid.ComputeModes.FullGrid)]
        [InlineData(MeshWindingNumberGrid.ComputeModes.NarrowBand)]
        public void MeshWindingNumberGrid_ReuseAndDeterminism_BitIdentical(MeshWindingNumberGrid.ComputeModes mode)
        {
            DMesh3 open = OpenSphere(1.0, 10, 0.35);
            double cellSize = open.CachedBounds.MaxDim / 16.0;
            var tree = new DMeshAABBTree3(open, true);

            var grid = new MeshWindingNumberGrid(open, tree, cellSize) {
                ComputeMode = mode,
                UseFastWindingNumber = false
            };
            grid.Compute();
            float[] first = (float[])grid.Grid.Buffer.Clone();
            grid.Compute();
            float[] second = (float[])grid.Grid.Buffer.Clone();

            Assert.Equal(first.Length, second.Length);
            for (int i = 0; i < first.Length; ++i)
                Assert.Equal(FloatBits(first[i]), FloatBits(second[i]));

            var serialGrid = new MeshWindingNumberGrid(open, tree, cellSize) {
                ComputeMode = mode,
                UseFastWindingNumber = false
            };
            bool prevSerial = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                serialGrid.Compute();
            } finally {
                gParallel.SerialOnThisThread = prevSerial;
            }
            float[] serial = serialGrid.Grid.Buffer;
            Assert.Equal(first.Length, serial.Length);
            for (int i = 0; i < first.Length; ++i)
                Assert.Equal(FloatBits(first[i]), FloatBits(serial[i]));
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void MeshWindingNumberGrid_FullGrid_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = OpenSphere(1.0, 8, 0.4);
            DMesh3 mt = Invariance.Transformed(m, sim);
            double cellSize = m.CachedBounds.MaxDim / 12.0;

            var g = new MeshWindingNumberGrid(m, new DMeshAABBTree3(m, true), cellSize) {
                ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid,
                UseFastWindingNumber = false
            };
            g.Compute();

            var gt = new MeshWindingNumberGrid(mt, new DMeshAABBTree3(mt, true), cellSize * sim.Scale) {
                ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid,
                UseFastWindingNumber = false
            };
            gt.Compute();

            if (sim.IsExact) {
                Assert.Equal(g.Dimensions, gt.Dimensions);
                float[] a = g.Grid.Buffer, b = gt.Grid.Buffer;
                for (int i = 0; i < a.Length; ++i)
                    Assert.Equal(FloatBits(a[i]), FloatBits(b[i]));
            } else {
                // Cross-compare transformed grid against both BruteForce(mt) and the untransformed tree
                var origTree = new DMeshAABBTree3(m, true);
                Vector3i dims = gt.Dimensions;
                Vector3d origin = gt.GridOriginD;
                double dx = (float)gt.CellSize;
                for (int k = 0; k < dims.z; k += 3) {
                    for (int j = 0; j < dims.y; j += 3) {
                        for (int i = 0; i < dims.x; i += 3) {
                            Vector3d q = new Vector3d((double)i * dx + origin.x, (double)j * dx + origin.y, (double)k * dx + origin.z);
                            float v = gt[i, j, k];
                            Assert.True(IsFinite(v));
                            double bruteT = BruteForce(mt, q);
                            Assert.True(Math.Abs(v - bruteT) < 1e-5, $"{name} vs brute at {q}: {v} vs {bruteT}");
                            Vector3d pOrig = sim.Inverse(q);
                            double wOrig = origTree.WindingNumber(pOrig);
                            Assert.True(Math.Abs(v - wOrig) < 1e-3, $"{name} vs orig at {pOrig}: {v} vs {wOrig}");
                        }
                    }
                }
            }
        }

        [Theory]
        [InlineData(MeshWindingNumberGrid.ComputeModes.FullGrid)]
        [InlineData(MeshWindingNumberGrid.ComputeModes.NarrowBand)]
        public void MeshWindingNumberGrid_CancelF_AbortsEarlyOnRowRayPath(MeshWindingNumberGrid.ComputeModes mode)
        {
            DMesh3 sphere = Sphere(1.0, 10);
            var tree = new DMeshAABBTree3(sphere, true);
            var grid = new MeshWindingNumberGrid(sphere, tree, sphere.CachedBounds.MaxDim / 16.0) {
                ComputeMode = mode,
                UseFastWindingNumber = false,
                CancelF = () => true
            };
            grid.Compute();
            Assert.True(grid.WindingEvaluations <= 1);
        }

        sealed class CustomWindingTree : DMeshAABBTree3
        {
            public int OverrideCalls;
            public CustomWindingTree(DMesh3 mesh) : base(mesh, true) { }
            public override double WindingNumber(Vector3d p)
            {
                System.Threading.Interlocked.Increment(ref OverrideCalls);
                return base.WindingNumber(p) + 10.0;
            }
        }

        [Theory]
        [InlineData(MeshWindingNumberGrid.ComputeModes.FullGrid)]
        [InlineData(MeshWindingNumberGrid.ComputeModes.NarrowBand)]
        public void MeshWindingNumberGrid_RespectsSubclassWindingNumberOverride(MeshWindingNumberGrid.ComputeModes mode)
        {
            DMesh3 cube = Box(2.0, Vector3d.Zero);
            var customTree = new CustomWindingTree(cube);
            var grid = new MeshWindingNumberGrid(cube, customTree, 0.5) {
                ComputeMode = mode,
                UseFastWindingNumber = false,
                WantMeshSDFGrid = mode == MeshWindingNumberGrid.ComputeModes.NarrowBand
            };
            grid.Compute();
            Assert.True(customTree.OverrideCalls > 0);
            if (mode == MeshWindingNumberGrid.ComputeModes.FullGrid) {
                Assert.All(grid.Grid.Buffer, v => Assert.True(v >= 9.5f));
            } else {
                DenseGrid3f sdf = grid.SDFGrid.Grid;
                Vector3i dims = grid.Dimensions;
                int checkedSeeded = 0;
                for (int k = 0; k < dims.z; ++k)
                    for (int j = 0; j < dims.y; ++j)
                        for (int i = 0; i < dims.x; ++i)
                            if (sdf[i, j, k] < grid.CellSize) {
                                Assert.True(grid[i, j, k] >= 9.5f);
                                checkedSeeded++;
                            }
                Assert.True(checkedSeeded > 0);
            }
        }
    }
}
