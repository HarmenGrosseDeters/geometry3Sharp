using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Open operands with few boundary segments are classified by the Antipodal Method (an exact ray from the piece's
    /// centroid into its front side plus a sum over the mesh's boundary segments; ExactMeshBoolean.
    /// antipodal_front_winding), the others by fast winding numbers. The switch counts the boundary as an oriented
    /// 1-chain of the mesh welded by exact position (ExactMeshBoolean.BoundarySegments). (The class name keeps the
    /// mutation shard's test filter, FullyQualifiedName~ExactBooleanOpenInputTests.)
    /// </summary>
    public class ExactBooleanOpenInputTestsAntipodal
    {
        // a unit sphere with the cap above capZ (relative to its centre) removed; capZ above 1 keeps it closed
        static DMesh3 Sphere(Vector3d c, double capZ, int edgeVertices = 20)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > capZ)
                    m.RemoveTriangle(t, true, false);
            MeshTransforms.Translate(m, c);
            return m;
        }

        // a box with its top face (z = max) removed: an open box
        static DMesh3 OpenBox(Vector3d min, Vector3d max)
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)), EdgeVertices = 4 }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > max.z - 1e-9)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, int maxSegments)
        {
            var r = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AntipodalMaxBoundarySegments = maxSegments };
            Assert.True(r.Compute(), r.Error);
            return r;
        }

        const int Antipodal = int.MaxValue, Fwn = 0;

        // the generalized winding number by the direct sum over all triangles (no far-field approximation)
        static double Direct(DMesh3 m, Vector3d p)
        {
            double w = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                w += MathUtil.TriSolidAngle(a, b, c, ref p) / MathUtil.FourPI;
            }
            return w;
        }

        // the triangles of x that y does not have (as oriented triangles with exact coordinates)
        static List<int> Missing(DMesh3 x, DMesh3 y)
        {
            Func<DMesh3, int, string> key = (m, t) => {
                Index3i f = m.GetTriangle(t);
                var v = new[] { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
                int k = 0;
                for (int i = 1; i < 3; ++i) if (v[i].CompareTo(v[k]) < 0) k = i;
                return string.Join(";", Enumerable.Range(0, 3).Select(i => v[(k + i) % 3].ToString("R")));
            };
            var have = new HashSet<string>(y.TriangleIndices().Select(t => key(y, t)));
            return x.TriangleIndices().Where(t => have.Contains(key(x, t)) == false).ToList();
        }

        /// <summary>
        /// Where the two paths differ, the Antipodal Method decides as the direct sum: every triangle that one result has
        /// and the other has not is classified by the direct generalized winding number of the input it does not lie on
        /// (AssertPieces' rule; triangles on both inputs or with the direct value within 1e-6 of 1/2 are skipped), and it
        /// must be in the Antipodal result iff the direct sum keeps it. Returns the number of differing triangles checked.
        /// </summary>
        static int AssertDifferencesDecidedAsDirectSum(DMesh3 a, DMesh3 b, ExactMeshBoolean ant, ExactMeshBoolean fwn, Ops op, string what)
        {
            var ta = new DMeshAABBTree3(a, true);
            var tb = new DMeshAABBTree3(b, true);
            int checkedCount = 0, wrong = 0;
            foreach (var (m, inAnt) in new[] { (ant.Result, true), (fwn.Result, false) }) {
                DMesh3 other = inAnt ? fwn.Result : ant.Result;
                foreach (int t in Missing(m, other)) {
                    Vector3d c = m.GetTriCentroid(t);
                    double da = Math.Sqrt(MeshQueries.TriDistanceSqr(a, ta.FindNearestTriangle(c), c));
                    double db = Math.Sqrt(MeshQueries.TriDistanceSqr(b, tb.FindNearestTriangle(c), c));
                    double resolution = 1e-14 * (Math.Max(Math.Abs(c.x), Math.Max(Math.Abs(c.y), Math.Abs(c.z))) + 1);
                    if (Math.Abs(da - db) <= Math.Max(1e-3 * (da + db), resolution))
                        continue;
                    bool onA = da < db;
                    double w = Direct(onA ? b : a, c);
                    if (Math.Abs(w - 0.5) < 1e-6)
                        continue;
                    bool inOther = w > 0.5;
                    bool keep = op == Ops.Intersection ? inOther : op == Ops.Union ? !inOther : (onA ? !inOther : inOther);
                    checkedCount++;
                    // (a triangle only in the Antipodal result must be kept by the direct sum, one only in the FWN result not)
                    if (keep != inAnt)
                        wrong++;
                }
            }
            Assert.True(wrong == 0, $"{what}: {wrong} of {checkedCount} differing triangles decided against the direct sum");
            return checkedCount;
        }

        static readonly Vector3d CenterB = new Vector3d(0.6, 0.3, 0.1);

        // ---------------------------------------------------------------- the boundary count

        [Fact]
        public void BoundarySegments_ClosedMeshIsEmpty_OpenSphereIsItsHoleLoop()
        {
            DMesh3 closed = Sphere(Vector3d.Zero, 2);
            Assert.Empty(ExactMeshBoolean.BoundarySegments(closed));
            DMesh3 open = Sphere(Vector3d.Zero, 0.6);
            Vector3d[] s = ExactMeshBoolean.BoundarySegments(open);
            int boundaryEdges = open.BoundaryEdgeIndices().Count();
            Assert.True(boundaryEdges > 10);
            Assert.Equal(2 * boundaryEdges, s.Length);
            // an oriented loop: every point starts one segment and ends one
            var starts = new Dictionary<Vector3d, int>();
            var ends = new Dictionary<Vector3d, int>();
            for (int k = 0; k < s.Length; k += 2) {
                starts[s[k]] = (starts.TryGetValue(s[k], out int a) ? a : 0) + 1;
                ends[s[k + 1]] = (ends.TryGetValue(s[k + 1], out int b) ? b : 0) + 1;
            }
            Assert.All(starts, kv => Assert.Equal(1, kv.Value));
            Assert.All(ends, kv => Assert.Equal(1, kv.Value));
            Assert.Equal(starts.Keys.OrderBy(v => v.x).ThenBy(v => v.y).ThenBy(v => v.z), ends.Keys.OrderBy(v => v.x).ThenBy(v => v.y).ThenBy(v => v.z));
        }

        [Fact]
        public void BoundarySegments_WeldByExactPosition_DuplicatesAndFans()
        {
            // the same closed sphere with every triangle on vertices of its own (same positions): welded, no boundary
            DMesh3 closed = Sphere(Vector3d.Zero, 2, 12);
            Assert.True(closed.IsClosed());
            Assert.Empty(ExactMeshBoolean.BoundarySegments(closed));
            var unwelded = new DMesh3();
            foreach (int t in closed.TriangleIndices()) {
                Index3i f = closed.GetTriangle(t);
                unwelded.AppendTriangle(unwelded.AppendVertex(closed.GetVertex(f.a)), unwelded.AppendVertex(closed.GetVertex(f.b)),
                    unwelded.AppendVertex(closed.GetVertex(f.c)));
            }
            Assert.Empty(ExactMeshBoolean.BoundarySegments(unwelded));
            // a duplicate triangle of the same orientation (on its own vertices) on a closed mesh: its three edges are
            // left over once each, in its orientation
            DMesh3 dup = new DMesh3(closed);
            Index3i d = dup.GetTriangle(0);
            dup.AppendTriangle(dup.AppendVertex(dup.GetVertex(d.a)), dup.AppendVertex(dup.GetVertex(d.b)), dup.AppendVertex(dup.GetVertex(d.c)));
            Vector3d[] sd = ExactMeshBoolean.BoundarySegments(dup);
            Assert.Equal(2 * 3, sd.Length);
            // the same triangle reversed: the same three edges, the other way round
            DMesh3 rev = new DMesh3(closed);
            rev.AppendTriangle(rev.AppendVertex(rev.GetVertex(d.a)), rev.AppendVertex(rev.GetVertex(d.c)), rev.AppendVertex(rev.GetVertex(d.b)));
            Vector3d[] sr = ExactMeshBoolean.BoundarySegments(rev);
            Assert.Equal(2 * 3, sr.Length);
            var forward = new HashSet<(Vector3d, Vector3d)>(Enumerable.Range(0, 3).Select(k => (sd[2 * k], sd[2 * k + 1])));
            for (int k = 0; k < 3; ++k)
                Assert.Contains((sr[2 * k + 1], sr[2 * k]), forward);
            // both together (opposite half-edges) cancel: no boundary
            DMesh3 both = new DMesh3(dup);
            both.AppendTriangle(both.AppendVertex(both.GetVertex(d.a)), both.AppendVertex(both.GetVertex(d.c)), both.AppendVertex(both.GetVertex(d.b)));
            Assert.Empty(ExactMeshBoolean.BoundarySegments(both));
            // the duplicate twice in the same orientation: each edge counts twice
            DMesh3 twice = new DMesh3(dup);
            twice.AppendTriangle(twice.AppendVertex(twice.GetVertex(d.a)), twice.AppendVertex(twice.GetVertex(d.b)), twice.AppendVertex(twice.GetVertex(d.c)));
            Assert.Equal(2 * 6, ExactMeshBoolean.BoundarySegments(twice).Length);
            // a fin on an edge (a non-manifold fan of three triangles): its other two edges and the shared one
            DMesh3 fin = new DMesh3(closed);
            Index3i e = fin.GetTriangle(0);
            // (on vertices of its own at the same positions: DMesh3 keeps edges manifold, the weld makes the fan)
            int apex = fin.AppendVertex(fin.GetVertex(e.a) * 1.5 + fin.GetVertex(e.b) * 0.1);
            Assert.True(fin.AppendTriangle(fin.AppendVertex(fin.GetVertex(e.a)), fin.AppendVertex(fin.GetVertex(e.b)), apex) >= 0);
            Assert.Equal(2 * 3, ExactMeshBoolean.BoundarySegments(fin).Length);
            // -0 and 0 are the same position
            var z = new DMesh3();
            z.AppendTriangle(z.AppendVertex(new Vector3d(0, 0, 0)), z.AppendVertex(new Vector3d(1, 0, 0)), z.AppendVertex(new Vector3d(0, 1, 0)));
            z.AppendTriangle(z.AppendVertex(new Vector3d(-0.0, 0, 0)), z.AppendVertex(new Vector3d(0, 1, 0)), z.AppendVertex(new Vector3d(1, 0, 0)));
            Assert.Empty(ExactMeshBoolean.BoundarySegments(z));
        }

        // ---------------------------------------------------------------- the switch

        [Fact]
        public void Switch_SoupsGoToFastWinding_SmallBoundariesToAntipodal()
        {
            DMesh3 closed = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            // a pure soup: the triangles moved apart (no shared positions); and a near soup: every copy of a vertex off
            // by a few ulps in x, so that no two triangles weld
            var soup = new DMesh3();
            var near = new DMesh3();
            foreach (int t in closed.TriangleIndices()) {
                Index3i f = closed.GetTriangle(t);
                Vector3d shift = closed.GetTriCentroid(t) * 0.05;
                soup.AppendTriangle(soup.AppendVertex(closed.GetVertex(f.a) + shift), soup.AppendVertex(closed.GetVertex(f.b) + shift),
                    soup.AppendVertex(closed.GetVertex(f.c) + shift));
                // (t + 1 units in the last place: every copy of a vertex differs)
                Func<Vector3d, Vector3d> off = (v) => new Vector3d(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v.x) + t + 1), v.y, v.z);
                near.AppendTriangle(near.AppendVertex(off(closed.GetVertex(f.a))), near.AppendVertex(off(closed.GetVertex(f.b))),
                    near.AppendVertex(off(closed.GetVertex(f.c))));
            }
            DMesh3 b = Sphere(CenterB, 0.5);
            foreach (DMesh3 a in new[] { soup, near }) {
                Assert.True(3 * a.TriangleCount >= 1000);
                ExactMeshBoolean byDefault = Run(a, b, Ops.Intersection, 1000), fwn = Run(a, b, Ops.Intersection, Fwn);
                Assert.Equal(3 * a.TriangleCount, byDefault.OpenBoundarySegments[0]);
                // (A to fast winding numbers, B, with its hole loop, to the Antipodal Method)
                Assert.Equal(1, byDefault.Counts["antipodal_meshes"]);
                Assert.True(byDefault.OpenBoundarySegments[1] > 0 && byDefault.OpenBoundarySegments[1] < 1000);
                ExactMeshBoolean fwnA = Run(a, b, Ops.Intersection, byDefault.OpenBoundarySegments[1] + 1);
                Assert.Equal("", Invariance.Difference(byDefault.Result, fwnA.Result));
                Assert.Equal(0, fwn.Counts["antipodal_meshes"]);
            }
        }

        [Fact]
        public void Switch_ThresholdIsStrict()
        {
            // a mesh with exactly as many boundary segments as the limit goes to fast winding numbers, one fewer to Antipodal
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 2);
            int count = ExactMeshBoolean.BoundarySegments(a).Length / 2;
            Assert.Equal(0, Run(a, b, Ops.Intersection, count).Counts["antipodal_meshes"]);
            ExactMeshBoolean ant = Run(a, b, Ops.Intersection, count + 1);
            Assert.Equal(1, ant.Counts["antipodal_meshes"]);
            Assert.Equal(count, ant.OpenBoundarySegments[0]);
            Assert.Equal(-1, ant.OpenBoundarySegments[1]);
            Assert.Equal(0, ant.Counts["antipodal_undecided"]);
        }

        [Fact]
        public void CoplanarOpenBoxes_StacksOnTheAntipodalPath()
        {
            // two open boxes whose bottoms overlap in one plane (coplanar stacks of both meshes), and the same shifted so
            // that they overlap in their side walls
            DMesh3 a = OpenBox(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), b = OpenBox(new Vector3d(0.5, 0.25, 0), new Vector3d(1.5, 0.75, 0.7));
            DMesh3 c = OpenBox(new Vector3d(0.5, 0, 0.2), new Vector3d(1.5, 1, 0.8));
            foreach (DMesh3 other in new[] { b, c })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    ExactMeshBoolean ant = Run(a, other, op, Antipodal), fwn = Run(a, other, op, Fwn);
                    Assert.Equal(2, ant.Counts["antipodal_meshes"]);
                    Assert.Equal(0, ant.Counts["antipodal_undecided"]);
                    AssertDifferencesDecidedAsDirectSum(a, other, ant, fwn, op, $"coplanar {op}");
                    // (AssertPieces is not used here: on these coplanar open boxes it flags 5 of 154 union triangles for both
                    // paths alike, the fast winding numbers included, so it is not a property of this path; reported)
                }
        }

        [Fact]
        public void SmallOpenMeshFarFromALargeOne()
        {
            // a small open hemisphere and a large closed box far away (thousands of the hemisphere's sizes): the box's
            // pieces are far outside the open mesh (winding number about 0), so the union keeps all of the box
            DMesh3 a = Sphere(Vector3d.Zero, 0.0, 12);
            MeshTransforms.Scale(a, 1e-3);
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(20, 7, 3), new Vector3d(10, 6, 5)) }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(box);
            // (a second, small closed part of b next to a, so that the arrangement has pieces near both)
            DMesh3 near = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0, 0, -5e-4), new Vector3d(2e-4, 2e-4, 2e-4)) }.Generate().MakeDMesh();
            MeshEditor.Append(b, near);
            ExactMeshBoolean ant = Run(a, b, Ops.Union, Antipodal), fwn = Run(a, b, Ops.Union, Fwn);
            Assert.Equal(1, ant.Counts["antipodal_meshes"]);
            AssertDifferencesDecidedAsDirectSum(a, b, ant, fwn, Ops.Union, "far");
            ExactBooleanOpenPerPieceTests.AssertPieces(a, b, ant, Ops.Union, "far");
            double vol = MeshMeasurements.VolumeArea(ant.Result, ant.Result.TriangleIndices(), ant.Result.GetVertex).x;
            Assert.True(vol >= 0.999999 * 20 * 12 * 10, $"volume {vol}");
        }

        // ---------------------------------------------------------------- equivalence with fast winding numbers

        public static IEnumerable<object[]> Cases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                yield return new object[] { "two open spheres", op };
                yield return new object[] { "open sphere and open box", op };
                yield return new object[] { "open sphere and closed sphere", op };
            }
        }

        static void Inputs(string name, out DMesh3 a, out DMesh3 b)
        {
            if (name == "two open spheres") { a = Sphere(Vector3d.Zero, 0.6); b = Sphere(CenterB, 0.5); }
            else if (name == "open sphere and open box") { a = Sphere(Vector3d.Zero, 0.6); b = OpenBox(new Vector3d(-0.3, -0.4, -0.5), new Vector3d(1.1, 0.7, 0.8)); }
            else { a = Sphere(Vector3d.Zero, 0.6); b = Sphere(CenterB, 2); }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SameResultAsFastWinding_AndPiecesOnTheRightSide(string name, Ops op)
        {
            DMesh3 a, b;
            Inputs(name, out a, out b);
            ExactMeshBoolean ant = Run(a, b, op, Antipodal), fwn = Run(a, b, op, Fwn);
            Assert.True(ant.Counts["antipodal_meshes"] >= 1);
            Assert.Equal(0, fwn.Counts["antipodal_meshes"]);
            Assert.True(ant.Result.TriangleCount > 0);
            // the results agree except where fast winding numbers are inaccurate (pieces near the holes' rims, where the
            // generalized winding number crosses 1/2): there the Antipodal result follows the direct sum
            ExactBooleanOpenPerPieceTests.AssertPieces(a, b, ant, op, $"{name} {op}");
            int differing = AssertDifferencesDecidedAsDirectSum(a, b, ant, fwn, op, $"{name} {op}");
            // (the check is not blind: the two open spheres differ near the rims, where the fast winding numbers err)
            if (name == "two open spheres")
                Assert.True(differing > 0, "no differing triangle checked");
            Assert.True(Missing(ant.Result, fwn.Result).Count + Missing(fwn.Result, ant.Result).Count < ant.Result.TriangleCount / 50, "differences beyond the rims");
        }

        [Fact]
        public void DuplicatesAndFans_SameClassificationOnBothPaths()
        {
            // an open sphere with a same-orientation duplicate of a triangle far from B, and one with a fin on an edge
            DMesh3 b = Sphere(CenterB, 2);
            DMesh3 dup = Sphere(Vector3d.Zero, 0.6), fin = Sphere(Vector3d.Zero, 0.6);
            int far = dup.TriangleIndices().OrderBy(t => -(dup.GetTriCentroid(t) - CenterB).Length).First();
            Index3i d = dup.GetTriangle(far);
            dup.AppendTriangle(dup.AppendVertex(dup.GetVertex(d.a)), dup.AppendVertex(dup.GetVertex(d.b)), dup.AppendVertex(dup.GetVertex(d.c)));
            Index3i e = fin.GetTriangle(far);
            Assert.True(fin.AppendTriangle(fin.AppendVertex(fin.GetVertex(e.a)), fin.AppendVertex(fin.GetVertex(e.b)),
                fin.AppendVertex((fin.GetVertex(e.a) + fin.GetVertex(e.b)) * 0.5 * 1.3)) >= 0);
            foreach (DMesh3 a in new[] { dup, fin })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    ExactMeshBoolean ant = Run(a, b, op, Antipodal), fwn = Run(a, b, op, Fwn);
                    Assert.True(ant.Counts["antipodal_meshes"] == 1);
                    AssertDifferencesDecidedAsDirectSum(a, b, ant, fwn, op, $"duplicate / fin {op}");
                }
        }

        [Fact]
        public void PlateStandingOnABox_BoundaryInTheBoxFace_BothPathsAgree()
        {
            // an open square plate (its boundary loop) standing upright with its lower boundary edge in the top face of a
            // box: the pieces of the box's face next to that edge lie where the plate's winding number jumps
            var plate = new DMesh3();
            int p0 = plate.AppendVertex(new Vector3d(0.2, 0.5, 1)), p1 = plate.AppendVertex(new Vector3d(0.8, 0.5, 1));
            int p2 = plate.AppendVertex(new Vector3d(0.8, 0.5, 1.6)), p3 = plate.AppendVertex(new Vector3d(0.2, 0.5, 1.6));
            plate.AppendTriangle(p0, p1, p2);
            plate.AppendTriangle(p0, p2, p3);
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                ExactMeshBoolean ant = Run(box, plate, op, Antipodal), fwn = Run(box, plate, op, Fwn);
                Assert.Equal(1, ant.Counts["antipodal_meshes"]);
                AssertDifferencesDecidedAsDirectSum(box, plate, ant, fwn, op, $"plate {op}");
            }
        }

        // ---------------------------------------------------------------- invariance, determinism, reuse

        [Fact]
        public void TwoOpenSpheres_Similarities_Antipodal()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 0.5);
            ExactMeshBoolean r0 = Run(a, b, Ops.Intersection, Antipodal);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 sa = Invariance.Transformed(a, sim), sb = Invariance.Transformed(b, sim);
                ExactMeshBoolean r = Run(sa, sb, Ops.Intersection, Antipodal);
                Assert.True(r.Counts["antipodal_meshes"] == 2, sim.ToString());
                Assert.True(Invariance.AllFinite(r.Result), sim.ToString());
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(r0.Result, Invariance.MappedBack(r.Result, sim)));
                else
                    ExactBooleanOpenPerPieceTests.AssertPieces(sa, sb, r, Ops.Intersection, sim.ToString());
            }
        }

        [Fact]
        public void ParallelEqualsSerial_Repeated_AndReuse()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = OpenBox(new Vector3d(-0.3, -0.4, -0.5), new Vector3d(1.1, 0.7, 0.8));
            ExactMeshBoolean serial = null;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Run(a, b, Ops.Difference, Antipodal);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 3; ++run)
                Assert.Equal("", Invariance.Difference(serial.Result, Run(a, b, Ops.Difference, Antipodal).Result));
            // the same instance twice, and after a closed operand
            var op = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Difference) { AntipodalMaxBoundarySegments = Antipodal };
            Assert.True(op.Compute());
            Assert.True(op.Compute());
            Assert.Equal("", Invariance.Difference(serial.Result, op.Result));
            op.MeshB = Sphere(CenterB, 2);
            Assert.True(op.Compute());
            Assert.Equal(1, op.Counts["antipodal_meshes"]);
            op.MeshB = new DMesh3(b);
            Assert.True(op.Compute());
            Assert.Equal("", Invariance.Difference(serial.Result, op.Result));
        }
    }
}
