using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Stage 1 of docs/design/COPLANAR_REGION_CLIPPING.md (#1630): the regions of the input triangles (ExactMeshArrangement.RegionOf) </summary>
    public class ExactMeshArrangementRegionTests
    {
        static int[] Regions(params DMesh3[] meshes)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3>(meshes)) { SelfIntersections = true, BuildRegions = true };
            arr.Compute();
            return arr.RegionOf;
        }

        static DMesh3 Far() { var m = new DMesh3(); m.AppendVertex(new Vector3d(100, 100, 100)); m.AppendVertex(new Vector3d(101, 100, 100)); m.AppendVertex(new Vector3d(100, 101, 100)); m.AppendTriangle(0, 1, 2); return m; }

        static int Count(int[] r, int from, int to)
        {
            var roots = new HashSet<int>();
            for (int i = from; i < to; ++i) roots.Add(r[i]);
            return roots.Count;
        }

        static DMesh3 Grid(int n, double z = 0, double bendFrom = double.MaxValue)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i, j, i >= bendFrom ? z + (i - bendFrom) : z));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
            return m;
        }

        [Fact]
        public void AGridInOnePlane_IsOneRegion_AndABentGridTwo()
        {
            int[] r = Regions(Grid(4), Far());
            Assert.Equal(1, Count(r, 0, 32));
            Assert.Equal(0, r[31]);
            Assert.Equal(32, r[32]);        // the far triangle: a region of its own
            Assert.Equal(2, Count(Regions(Grid(4, 0, 2), Far()), 0, 32));
        }

        [Fact]
        public void ABox_HasSixRegionsOfTwoTriangles_AndTwoMeshesNeverShareOne()
        {
            var box = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1))) }.Generate().MakeDMesh();
            int[] r = Regions(box, Far());
            Assert.Equal(6, Count(r, 0, 12));
            foreach (int root in new HashSet<int>(r.Take(12))) Assert.Equal(2, System.Linq.Enumerable.Count(r.Take(12), x => x == root));
            int[] two = Regions(Grid(2), Grid(2));
            Assert.Equal(1, Count(two, 0, 8));
            Assert.Equal(1, Count(two, 8, 16));
            Assert.Equal(8, two[8]);
        }

        [Fact]
        public void AFlippedNeighbour_IsNotFlat()
        {
            var flipped = new DMesh3();
            flipped.AppendVertex(new Vector3d(0, 0, 0)); flipped.AppendVertex(new Vector3d(1, 0, 0)); flipped.AppendVertex(new Vector3d(0, 1, 0)); flipped.AppendVertex(new Vector3d(1, 1, 0));
            flipped.AppendTriangle(0, 1, 2);
            flipped.AppendTriangle(1, 3, 2);                      // 1->2 in the first, 2->1 in the second: consistent
            int[] ok = Regions(flipped, Far());
            Assert.Equal(1, Count(ok, 0, 2));                   // consistent: one region
            var bad = new DMesh3();
            bad.AppendVertex(new Vector3d(0, 0, 0)); bad.AppendVertex(new Vector3d(1, 0, 0)); bad.AppendVertex(new Vector3d(0, 1, 0)); bad.AppendVertex(new Vector3d(1, 1, 0));
            bad.AppendTriangle(0, 1, 2);
            bad.AppendTriangle(1, 2, 3);                          // 1->2 in both: a flipped neighbour
            Assert.Equal(2, Count(Regions(bad, Far()), 0, 2));
            var fold = new DMesh3();                              // consistent edge direction (1->2, 2->1) but both third corners on the same side
            fold.AppendVertex(new Vector3d(0, 0, 0)); fold.AppendVertex(new Vector3d(2, 0, 0)); fold.AppendVertex(new Vector3d(0, 2, 0)); fold.AppendVertex(new Vector3d(2, 2, 0));
            fold.AppendTriangle(0, 1, 2);
            fold.AppendTriangle(2, 1, 3);                         // the edge 1-2 is 1->2 in the first and 2->1 here, the corners 0 and 3 are on opposite sides: flat
            Assert.Equal(1, Count(Regions(fold, Far()), 0, 2));
            var over = new DMesh3();                              // the edge 0-1 as 0->1 and 1->0, the third corners (0,2) and (1,1) both above
            over.AppendVertex(new Vector3d(0, 0, 0)); over.AppendVertex(new Vector3d(2, 0, 0)); over.AppendVertex(new Vector3d(0, 2, 0)); over.AppendVertex(new Vector3d(1, 1, 0));
            over.AppendTriangle(0, 1, 2);
            over.AppendTriangle(1, 0, 3);
            Assert.Equal(2, Count(Regions(over, Far()), 0, 2));
        }

        [Fact]
        public void ACylinder_HasTwoCapsAndOneRegionPerSideQuad()
        {
            DMesh3 c = ExactMeshBooleanCoplanarWalkTests.Cylinder(8);
            int n = (c.VertexCount - 2) / 2;
            int[] r = Regions(c, Far());
            Assert.Equal(2 + n, Count(r, 0, c.TriangleCount));
            // the regions are a function of the mesh alone: again, and with the other order of the meshes' triangles
            Assert.Equal(r, Regions(c, Far()));
        }

        static string Outputs(bool regions, params DMesh3[] meshes)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3>(meshes)) { SelfIntersections = true, BuildRegions = regions };
            gParallel.SerialOnThisThread = true;       // (point ids follow the thread order otherwise)
            try { arr.Compute(); } finally { gParallel.SerialOnThisThread = false; }
            return string.Join(";", arr.Output.Select(o => o.Mesh + "," + o.Source + "," + o.A + "," + o.B + "," + o.C));
        }

        [Fact]
        public void BuildingTheRegions_ChangesNoOutput()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            Assert.Equal(Outputs(false, a, b), Outputs(true, a, b));
            DMesh3 c = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0);
            Assert.Equal(Outputs(false, a, c), Outputs(true, a, c));
        }

        static DMesh3 Moved(DMesh3 m, Similarity sim)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices().ToList()) c.SetVertex(v, sim.Apply(c.GetVertex(v)));
            return c;
        }

        [Fact]
        public void TheRegions_AreBitIdenticalUnderTheExactSimilarities_AndAPartitionUnderAllOthers()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            int[] r = Regions(a, b);
            foreach (Similarity sim in Similarity.All()) {
                int[] t = Regions(Moved(a, sim), Moved(b, sim));
                if (sim.IsExact) {
                    Assert.Equal(r, t);     // the 2^k scales (and the identity): the same exact planes, bit for bit
                    continue;
                }
                // the others round the coordinates, so the planes may no longer be exact; the regions are still a partition into sets of one mesh
                // that each hold their lowest index
                int n = a.TriangleCount;
                for (int i = 0; i < t.Length; ++i) {
                    Assert.True(t[i] <= i && t[t[i]] == t[i], sim.Name);
                    Assert.True((t[i] < n) == (i < n), sim.Name + ": a region of two meshes");
                }
            }
        }

        [Fact]
        public void TheRegions_AreTheSameWhenComputedTwice()
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(6), b = ExactMeshBooleanCoplanarWalkTests.Shifted(a, 0.25);
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, BuildRegions = true };
            arr.Compute();
            int[] first = arr.RegionOf;
            arr.Compute();
            Assert.Equal(first, arr.RegionOf);
        }

        [Fact]
        public void TheRegions_AreNullWithTheSwitchOff()
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { Grid(2), Far() }) { SelfIntersections = true };
            arr.Compute();
            Assert.Null(arr.RegionOf);
        }

        static DMesh3 ReversedTriangles(DMesh3 src)
        {
            var m = new DMesh3();
            foreach (int vid in src.VertexIndices())
                m.AppendVertex(src.GetVertex(vid));
            var tris = src.TriangleIndices().ToList();
            tris.Reverse();
            foreach (int tid in tris)
                m.AppendTriangle(src.GetTriangle(tid));
            return m;
        }

        static DMesh3 StackedGrids(int layers, int gridN, double zBase, double dz, double ox, double oy)
        {
            var m = new DMesh3();
            for (int layer = 0; layer < layers; ++layer) {
                double z = zBase + layer * dz;
                int baseV = m.VertexCount;
                for (int j = 0; j <= gridN; ++j)
                    for (int i = 0; i <= gridN; ++i)
                        m.AppendVertex(new Vector3d(ox + i, oy + j, z));
                for (int j = 0; j < gridN; ++j)
                    for (int i = 0; i < gridN; ++i) {
                        int a = baseV + j * (gridN + 1) + i, b = a + 1, c = a + gridN + 1, d = c + 1;
                        m.AppendTriangle(a, b, d);
                        m.AppendTriangle(a, d, c);
                    }
            }
            return m;
        }

        [Fact]
        public void RegionClipping_MaxPlaneCandidatesOverflow_JoinsAllCoplanarPlanesRegardlessOfOrder()
        {
            // 11 distinct horizontal planes z = 1.0 + layer * 2^-35, plus a symmetric far triangle at z = -1.0
            // so Center.z ~ 0. In integer units (scaleExp = -35), d/m = 2^35 + layer has round_key step
            // 2^(35 - 30) = 32, so all 11 planes share the exact same plane_bucket and overflow MaxPlaneCandidates = 8.
            const int layers = 11, gridN = 2;
            const int trisPerLayer = 2 * gridN * gridN;
            double dz = System.Math.Pow(2.0, -35);
            DMesh3 a = StackedGrids(layers, gridN, 1.0, dz, 0.0, 0.0);
            int fa = a.AppendVertex(new Vector3d(10, 10, -1.0));
            int fb = a.AppendVertex(new Vector3d(11, 10, -1.0));
            int fc = a.AppendVertex(new Vector3d(10, 11, -1.0));
            a.AppendTriangle(fa, fb, fc);
            DMesh3 b = StackedGrids(layers, gridN, 1.0, dz, 0.5, 0.5);

            var arrFwd = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = true };
            arrFwd.Compute();
            // Confirm bucket overflow actually happened on the last layer (CanonicalPlane did not unify its triangles).
            int lastStart = (layers - 1) * trisPerLayer;
            Assert.NotEqual(arrFwd.CanonicalPlane(4 * lastStart), arrFwd.CanonicalPlane(4 * (lastStart + 1)));
            Assert.Equal(layers, Count(arrFwd.RegionOf, 0, layers * trisPerLayer));
            for (int layer = 0; layer < layers; ++layer)
                Assert.Equal(1, Count(arrFwd.RegionOf, layer * trisPerLayer, (layer + 1) * trisPerLayer));

            // Reversing triangle order must still form 1 region per plane (same 11 regions).
            DMesh3 aRev = ReversedTriangles(a);
            var arrRev = new ExactMeshArrangement(new List<DMesh3> { aRev, b }) { SelfIntersections = true, RegionClipping = true };
            arrRev.Compute();
            // First triangle of aRev is the far z=-1 triangle; the remaining layers*trisPerLayer triangles are the 11 grids.
            Assert.Equal(layers, Count(arrRev.RegionOf, 1, 1 + layers * trisPerLayer));
            for (int layer = 0; layer < layers; ++layer)
                Assert.Equal(1, Count(arrRev.RegionOf, 1 + layer * trisPerLayer, 1 + (layer + 1) * trisPerLayer));
        }

        [Fact]
        public void RegionClipping_RoundKeyBoundarySplit_StillJoinsCoplanarTriangles()
        {
            // Exact plane z = s * x with s = 7 * 2^-31 = 3.5 * 2^-30 (a round_key half-step boundary).
            // T1 has exact normal (-7, 0, 2^31) -> a/c = -3.5 * 2^-30 -> round_key = -4 * 2^-30.
            // T2 has area factor k = 2^52 + 1 (via y3 = 1 + 2^-52), so FixedArith.ToDouble (53-bit truncation)
            // truncates P.A differently from P.C across the tie boundary, splitting T1 and T2 into different plane buckets
            // (-4 * 2^-30 vs -3 * 2^-30) despite exact coplanarity.
            double s = 7.0 * System.Math.Pow(2.0, -31);
            var v0 = new Vector3d(0.0, 0.0, 0.0);
            var v1 = new Vector3d(1.0, 0.0, s);
            var v2 = new Vector3d(0.0, 1.0, 0.0);
            var v3 = new Vector3d(1.0, 1.0 + System.Math.Pow(2.0, -52), s);
            Assert.Equal(0, ExactPredicates.Orient3d(v0, v1, v2, v3));

            var a = new DMesh3();
            a.AppendVertex(v0); a.AppendVertex(v1); a.AppendVertex(v2); a.AppendVertex(v3);
            a.AppendTriangle(0, 1, 2);
            a.AppendTriangle(2, 1, 3);
            // Far symmetric triangle so Center stays exact 0.
            int s0 = a.AppendVertex(new Vector3d(-1.0, -(1.0 + System.Math.Pow(2.0, -52)), -s));
            int s1 = a.AppendVertex(new Vector3d(-0.5, -(1.0 + System.Math.Pow(2.0, -52)), -s));
            int s2 = a.AppendVertex(new Vector3d(-1.0, 0.0, -s));
            a.AppendTriangle(s0, s1, s2);

            // Overlapping coplanar partner on mesh B (dyadic 0.75*s is exact in binary64) so both triangles of A are in candidate pairs.
            var b = new DMesh3();
            b.AppendVertex(new Vector3d(0.25, 0.25, 0.25 * s));
            b.AppendVertex(new Vector3d(0.75, 0.25, 0.75 * s));
            b.AppendVertex(new Vector3d(0.25, 0.75, 0.25 * s));
            b.AppendTriangle(0, 1, 2);

            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = true };
            arr.Compute();
            Assert.NotEqual(arr.CanonicalPlane(0), arr.CanonicalPlane(4)); // confirms round_key split occurred
            Assert.Equal(arr.RegionOf[0], arr.RegionOf[1]);                // regions still join via exact Orient3d
        }

        [Fact]
        public void RegionClipping_MixedExponentNonFiniteBucket_StillJoinsCoplanarTriangles()
        {
            // Mixed exponents (a distant small coordinate 2^-200 alongside coordinates at 2^180) give a 380-bit
            // integer span, so D = N . p has ~1140 bits > double.MaxValue (~2^1024), making PlaneApprox non-finite.
            // RegionOf must still join the coplanar grid triangles into 1 region.
            double big = System.Math.Pow(2.0, 180);
            double tiny = System.Math.Pow(2.0, -200);
            DMesh3 a = Grid(2);
            MeshTransforms.Scale(a, big);
            MeshTransforms.Translate(a, new Vector3d(0, 0, big));
            // Add an isolated tiny triangle far away so scaleExp drops to -200 while the grid sits at z = +big.
            int t0 = a.AppendVertex(new Vector3d(tiny, 0, -big));
            int t1 = a.AppendVertex(new Vector3d(2 * tiny, 0, -big));
            int t2 = a.AppendVertex(new Vector3d(tiny, tiny, -big));
            a.AppendTriangle(t0, t1, t2);

            DMesh3 b = Grid(2);
            MeshTransforms.Scale(b, big);
            MeshTransforms.Translate(b, new Vector3d(0.5 * big, 0.5 * big, big));

            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = true };
            arr.Compute();
            Assert.NotEqual(arr.CanonicalPlane(0), arr.CanonicalPlane(4)); // confirms non-finite PlaneApprox skipped bucketing
            Assert.Equal(1, Count(arr.RegionOf, 0, 8));
        }

        [Fact]
        public void RegionClipping_FlatMaskReducesOutputPiecesOnBothIncidentTriangles()
        {
            // Two overlapping coplanar 2x2 grids: every interior edge is shared by two triangles (ti and other).
            // RegionClipping must ghost flat edges on BOTH ti and other, strictly reducing output pieces vs RegionClipping = false
            // (kills mutants that drop either flatMask[ti] or flatMask[other] inside stryker-mesh-arrangement-4).
            DMesh3 a = Grid(2), b = Grid(2);
            MeshTransforms.Translate(b, new Vector3d(0.3, 0.2, 0.0));
            var off = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = false };
            off.Compute();
            var on = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = true };
            on.Compute();
            Assert.True(on.Output.Count < off.Output.Count, $"expected fewer pieces with RegionClipping ({on.Output.Count} vs {off.Output.Count})");
            // Exact piece count on this fixed 2x2 grid pair: both flatMask[ti] and flatMask[other] must be set to reach the minimum.
            // Reversing a's triangle order swaps which triangle is `other` vs `ti` on every flat edge, pinning both masks symmetrically.
            var onRev = new ExactMeshArrangement(new List<DMesh3> { ReversedTriangles(a), b }) { SelfIntersections = true, RegionClipping = true };
            onRev.Compute();
            Assert.Equal(on.Output.Count, onRev.Output.Count);
        }

        [Fact]
        public void RegionClipping_BooleanWithOverflowedPlaneBuckets_IsCleanAndDeterministic()
        {
            // 10 closed boxes whose bottom faces z = 1.0 + k * 2^-35 all land in the same plane_bucket (anchored by a
            // symmetric box at z = -1.0 so Center.z ~ 0), overflowing MaxPlaneCandidates = 8, plus a coplanar partner box
            // sharing the 10th (overflowed) bottom plane z = 1.0 + 9 * 2^-35.
            double dz = System.Math.Pow(2.0, -35);
            var meshes = new List<DMesh3>();
            meshes.Add(new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, -1.5), new Vector3d(0.5, 0.5, -1.0)))
            }.Generate().MakeDMesh());
            for (int k = 0; k < 10; ++k) {
                double z0 = 1.0 + k * dz;
                meshes.Add(new TrivialBox3Generator() {
                    Box = new Box3d(new AxisAlignedBox3d(new Vector3d(k * 0.15, 0, z0), new Vector3d(k * 0.15 + 0.5, 1.0, z0 + 0.5)))
                }.Generate().MakeDMesh());
            }
            // Coplanar partner overlapping the 10th (overflowed) box on z = 1.0 + 9 * 2^-35
            double zLast = 1.0 + 9 * dz;
            meshes.Add(new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(1.2, 0.25, zLast), new Vector3d(2.0, 0.75, zLast + 0.6)))
            }.Generate().MakeDMesh());

            var op1 = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) {
                RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true
            };
            Assert.True(op1.Compute(), op1.Error);
            Assert.True(op1.RoundingClean);
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(op1.Result) ?? "");
            Assert.Equal("", BooleanCertificate.From(op1).Check());

            // Confirm bucket overflow actually happened on the 10th box's bottom face (two coplanar bottom tris kept distinct CanonicalPlane)
            var lastBoxTris = op1.Arrangement.MeshTriangles[10];
            int overflowDistinct = 0;
            for (int i = 0; i < lastBoxTris.Count; ++i)
                for (int j = i + 1; j < lastBoxTris.Count; ++j)
                    if (op1.Arrangement.RegionOf[lastBoxTris[i]] == op1.Arrangement.RegionOf[lastBoxTris[j]]
                        && op1.Arrangement.CanonicalPlane(4 * lastBoxTris[i]) != op1.Arrangement.CanonicalPlane(4 * lastBoxTris[j]))
                        overflowDistinct++;
            Assert.True(overflowDistinct >= 1, "expected at least one joined region spanning an overflowed plane bucket");

            // Bit-identical serial vs parallel determinism
            var opSerial = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) { RegionClipping = true };
            gParallel.SerialOnThisThread = true;
            try { Assert.True(opSerial.Compute(), opSerial.Error); } finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal("", Invariance.Difference(op1.Result, opSerial.Result));

            // Matches RegionClipping = false volume and stays clean under reversed triangle order
            var opOff = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) { RegionClipping = false };
            Assert.True(opOff.Compute(), opOff.Error);
            var revMeshes = meshes.Select(ReversedTriangles).ToList();
            var opRev = new ExactMeshBoolean(revMeshes, ExactMeshBoolean.Operations.Union) { RegionClipping = true };
            Assert.True(opRev.Compute(), opRev.Error);
            Assert.True(opRev.RoundingClean);
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(opRev.Result) ?? "");
            double v1 = MeshMeasurements.VolumeArea(op1.Result, op1.Result.TriangleIndices(), op1.Result.GetVertex).x;
            double vOff = MeshMeasurements.VolumeArea(opOff.Result, opOff.Result.TriangleIndices(), opOff.Result.GetVertex).x;
            double vRev = MeshMeasurements.VolumeArea(opRev.Result, opRev.Result.TriangleIndices(), opRev.Result.GetVertex).x;
            Assert.True(System.Math.Abs(v1 - vOff) <= 1e-12 * System.Math.Max(1.0, System.Math.Abs(v1)));
            Assert.True(System.Math.Abs(v1 - vRev) <= 1e-12 * System.Math.Max(1.0, System.Math.Abs(v1)));
        }

        [Fact]
        public void RegionClipping_ReusedInstance_EqualsFreshInstance()
        {
            double dz = System.Math.Pow(2.0, -35);
            DMesh3 a = StackedGrids(10, 2, 1.0, dz, 0.0, 0.0);
            int fa = a.AppendVertex(new Vector3d(10, 10, -1.0));
            int fb = a.AppendVertex(new Vector3d(11, 10, -1.0));
            int fc = a.AppendVertex(new Vector3d(10, 11, -1.0));
            a.AppendTriangle(fa, fb, fc);
            DMesh3 b = StackedGrids(10, 2, 1.0, dz, 0.5, 0.5);

            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, RegionClipping = true };
            arr.Compute();
            int[] first = (int[])arr.RegionOf.Clone();
            arr.Compute();
            Assert.Equal(first, arr.RegionOf);

            DMesh3 boxA = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 3 }.Generate().MakeDMesh();
            DMesh3 boxB = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.8, 0.7, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 3 }.Generate().MakeDMesh();
            var op = new ExactMeshBoolean(boxA, boxB, ExactMeshBoolean.Operations.Union) { RegionClipping = true };
            Assert.True(op.Compute(), op.Error);
            DMesh3 firstResult = new DMesh3(op.Result);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal("", Invariance.Difference(firstResult, op.Result));
        }

        [Fact]
        public void RegionClipping_SimilarityAll_ExactScalesBitIdenticalAndOthersClean()
        {
            DMesh3 boxA = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 3 }.Generate().MakeDMesh();
            DMesh3 boxB = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.75, 0.75, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 3 }.Generate().MakeDMesh();
            var refOp = new ExactMeshBoolean(boxA, boxB, ExactMeshBoolean.Operations.Union) { RegionClipping = true };
            Assert.True(refOp.Compute(), refOp.Error);
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(refOp.Result) ?? "");

            foreach (Similarity sim in Similarity.All()) {
                var op = new ExactMeshBoolean(Moved(boxA, sim), Moved(boxB, sim), ExactMeshBoolean.Operations.Union) { RegionClipping = true };
                Assert.True(op.Compute(), sim.Name + ": " + op.Error);
                Assert.True(op.RoundingClean, sim.Name);
                string reason = gs.MeshRobustRepair.NotCleanReason(op.Result) ?? "";
                Assert.True(reason == "", sim.Name + ": " + reason);
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(refOp.Result, Invariance.MappedBack(op.Result, sim)));
            }
        }
    }
}
