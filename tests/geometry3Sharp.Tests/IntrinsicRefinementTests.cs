using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;
using static geometry3Sharp.Tests.IntrinsicGeodesicsTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Intrinsic Delaunay refinement (vertex insertion, Sharp, Soliman and Crane 2019) and the common
    /// subdivision of the intrinsic and the input triangulation.
    /// </summary>
    public class IntrinsicRefinementTests
    {
        readonly ITestOutputHelper output;
        public IntrinsicRefinementTests(ITestOutputHelper output) { this.output = output; }

        static void AssertConsistent(IntrinsicTriangulation T)
        {
            string err;
            Assert.True(T.CheckConsistency(out err), err);
        }

        static double TotalArea(IntrinsicTriangulation T)
        {
            return Enumerable.Range(0, T.TriangleCount).Sum(t => T.TriangleArea(t));
        }

        static int NegativeWeights(IntrinsicTriangulation T, bool boundaryOnly)
        {
            int n = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                if (T.Twin(h) >= 0 && (boundaryOnly || T.Twin(h) < h)) continue;
                if (T.CotanWeight(h) < -1e-9) n++;
            }
            return n;
        }

        // in the plane every intrinsic edge is the straight segment between the positions of its vertices
        static double PlanarLengthError(IntrinsicTriangulation T)
        {
            double e = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h)
                e = Math.Max(e, Math.Abs(T.Length(h) - T.VertexPosition(T.Tail(h)).Distance(T.VertexPosition(T.Head(h)))));
            return e;
        }

        static double TraceEndError(IntrinsicTriangulation T)
        {
            double e = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                var path = T.TraceEdge(h);
                e = Math.Max(e, path[path.Count - 1].Distance(T.VertexPosition(T.Head(h))));
            }
            return e;
        }

        static double MinAngleDeg(IntrinsicTriangulation T, int t)
        {
            return Math.Min(T.CornerAngle(3 * t), Math.Min(T.CornerAngle(3 * t + 1), T.CornerAngle(3 * t + 2))) * 180 / Math.PI;
        }

        // ---------------------------------------------------------------- refinement

        [Fact]
        public void Refine_ObtuseBoundary_AllCotanWeightsNonNegative()
        {
            // the bottom and top rows have boundary edges opposite 169 degree angles: flips cannot fix them
            DMesh3 mesh = ObtusePlane(10, 100, 1.0, 0.05);
            var flipped = new IntrinsicTriangulation(mesh);
            flipped.FlipToDelaunay();
            int negBefore = NegativeWeights(flipped, true);
            Assert.True(negBefore >= 10, $"{negBefore} negative boundary weights after flips");

            var T = new IntrinsicTriangulation(mesh);
            double area0 = TotalArea(T);
            var r = T.DelaunayRefine(0);
            output.WriteLine(r.ToString());
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(r.BoundarySplits > 0 && r.CircumcenterInsertions == 0);
            Assert.Equal(r.InsertedVertices, T.VertexCount - T.InputVertexCount);
            Assert.Equal(0, r.NegativeWeights);
            Assert.Equal(0, NegativeWeights(T, false));
            Assert.True(r.MinCotanWeight > -1e-9);
            Assert.True(T.IsDelaunay());
            AssertConsistent(T);
            Assert.Equal(area0, TotalArea(T), 9);
            Assert.True(PlanarLengthError(T) < 1e-9, $"{PlanarLengthError(T)}");
            // the inserted vertices lie on the boundary of the plane
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i) {
                Assert.True(T.IsBoundaryVertex(i));
                Assert.Equal(-1, T.VertexID(i));
                Vector3d p = T.VertexPosition(i);
                Assert.True(Math.Abs(p.y) < 1e-12 || Math.Abs(p.y - 5) < 1e-12, $"{p}");
            }
            // the cotan Laplacian has no positive off-diagonal entries (a maximum principle)
            foreach (var kv in T.CotanLaplacian().NonZeros())
                if (kv.Key.a != kv.Key.b) Assert.True(kv.Value <= 1e-9, $"positive off-diagonal {kv.Value}");
        }

        [Fact]
        public void Refine_Sphere_MinimumAngleBound()
        {
            DMesh3 mesh = RingSphere(1, 60, 12);
            var flipped = new IntrinsicTriangulation(mesh);
            flipped.FlipToDelaunay();
            Assert.True(new IntrinsicTriangulation(mesh).MinAngle() * 180 / Math.PI < 12);
            Assert.True(flipped.MinAngle() * 180 / Math.PI < 28, $"{flipped.MinAngle() * 180 / Math.PI}");

            var T = new IntrinsicTriangulation(mesh);
            double area0 = TotalArea(T);
            var r = T.DelaunayRefine(30);
            output.WriteLine(r.ToString());
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(r.CircumcenterInsertions > 0);
            Assert.Equal(0, r.SkippedTriangles);
            Assert.True(T.MinAngle() * 180 / Math.PI >= 30 - 1e-9, $"min angle {T.MinAngle() * 180 / Math.PI}");
            Assert.True(r.MinAngle >= 30 - 1e-9);
            Assert.True(T.IsDelaunay());
            AssertConsistent(T);
            Assert.Equal(area0, TotalArea(T), 9);
            // the inserted vertices lie on the input surface, and the traced edges end at their heads
            var spatial = new DMeshAABBTree3(mesh, true);
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i) {
                Vector3d p = T.VertexPosition(i);
                int tid = spatial.FindNearestTriangle(p);
                double dist = Math.Sqrt(MeshQueries.TriangleDistance(mesh, tid, p).DistanceSquared);
                Assert.True(dist < 1e-8, $"{dist}");
            }
            Assert.True(TraceEndError(T) < 1e-9, $"{TraceEndError(T)}");
        }

        [Fact]
        public void Refine_ObtusePlane_AngleBoundExceptAtSmallInputAngles()
        {
            // the left and right sides zigzag with 11 degree corners: those triangles cannot be improved
            DMesh3 mesh = ObtusePlane(10, 100, 1.0, 0.05);
            var T = new IntrinsicTriangulation(mesh);
            var r = T.DelaunayRefine(25);
            output.WriteLine(r.ToString());
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(r.SkippedTriangles > 0);
            Assert.Equal(0, r.RemainingBadTriangles);
            Assert.Equal(0, NegativeWeights(T, false));
            AssertConsistent(T);
            Assert.True(PlanarLengthError(T) < 1e-9, $"{PlanarLengthError(T)}");
            int bad = 0;
            for (int t = 0; t < T.TriangleCount; ++t) {
                if (MinAngleDeg(T, t) >= 25 - 1e-9) continue;
                bad++;
                Index3i tv = T.GetTriangleIndices(t);
                double xmin = Math.Min(T.VertexPosition(tv.a).x, Math.Min(T.VertexPosition(tv.b).x, T.VertexPosition(tv.c).x));
                double xmax = Math.Max(T.VertexPosition(tv.a).x, Math.Max(T.VertexPosition(tv.b).x, T.VertexPosition(tv.c).x));
                Assert.True(xmin <= 0.5 + 1e-9 || xmax >= 9.5 - 1e-9, $"bad triangle away from the zigzag sides: {xmin}..{xmax}");
            }
            Assert.Equal(r.SkippedTriangles, bad);
        }

        [Fact]
        public void Refine_InsertionLimit_ReportsStatus()
        {
            DMesh3 mesh = RingSphere(1, 60, 12);
            var T = new IntrinsicTriangulation(mesh);
            var r = T.DelaunayRefine(28, 5);
            Assert.Equal(IntrinsicRefinementStatus.InsertionLimitReached, r.Status);
            Assert.Equal(5, r.InsertedVertices);
            Assert.True(r.RemainingBadTriangles > 0);
            Assert.True(T.IsDelaunay());
            AssertConsistent(T);
            // continuing reaches the bound
            r = T.DelaunayRefine(28);
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(T.MinAngle() * 180 / Math.PI >= 28 - 1e-9);
        }

        [Fact]
        public void InsertVertex_SplitEdge_KeepGeometryAndSignposts()
        {
            DMesh3 mesh = Grid(4, 5, (x, y) => 0);
            var T = new IntrinsicTriangulation(mesh);
            double area0 = TotalArea(T);
            Vector2d q0, q1, q2;
            T.GetTriangleLayout(3, out q0, out q1, out q2);
            Assert.Equal(-1, T.InsertVertex(3, 2 * q1));                  // outside
            int v = T.InsertVertex(3, (q0 + q1 + q2) / 3);
            Assert.Equal(T.InputVertexCount, v);
            Assert.True(T.IsInserted(v) && T.IsBoundaryVertex(v) == false);
            AssertConsistent(T);
            // an interior and a boundary edge
            int hi = Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) >= 0 && T.IsInserted(T.Tail(h)) == false && T.IsInserted(T.Head(h)) == false);
            int hb = Enumerable.Range(0, T.HalfedgeCount).First(h => T.Twin(h) < 0);
            Vector3d a = T.VertexPosition(T.Tail(hi)), b = T.VertexPosition(T.Head(hi));
            int m = T.SplitEdge(hi, 0.3);
            Assert.True(m > v);
            Assert.True(T.VertexPosition(m).Distance(a + 0.3 * (b - a)) < 1e-12);
            Vector3d c = T.VertexPosition(T.Tail(hb)), d = T.VertexPosition(T.Head(hb));
            int mb = T.SplitEdge(hb, 0.5);
            Assert.True(T.IsBoundaryVertex(mb));
            Assert.True(T.VertexPosition(mb).Distance(0.5 * (c + d)) < 1e-12);
            Assert.Equal(-1, T.SplitEdge(hb, 1.0));
            AssertConsistent(T);
            Assert.Equal(area0, TotalArea(T), 12);
            Assert.True(PlanarLengthError(T) < 1e-12);
            // insertions keep working after flips
            T.FlipToDelaunay();
            T.DelaunayRefine(30);
            AssertConsistent(T);
            Assert.True(PlanarLengthError(T) < 1e-9);
            Assert.True(TraceEndError(T) < 1e-9);
        }

        [Fact]
        public void BuildDelaunay_RefinedLaplacianHasNoNegativeWeights()
        {
            DMesh3 mesh = ObtusePlane(6, 20, 1.0, 0.05);
            IntrinsicRefinementResult r;
            var T0 = IntrinsicTriangulation.BuildDelaunay(mesh, false, out r);
            Assert.Null(r);
            Assert.True(T0.CotanLaplacian().NonZeros().Any(kv => kv.Key.a != kv.Key.b && kv.Value > 1e-9));
            var T = IntrinsicTriangulation.BuildDelaunay(mesh, true, out r, 0);
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            var L = T.CotanLaplacian();
            Assert.Equal(T.VertexCount, L.Size.a);
            Assert.False(L.NonZeros().Any(kv => kv.Key.a != kv.Key.b && kv.Value > 1e-9));
            // linear precision: L x = 0 at interior vertices for the coordinate functions (x, y)
            double[] x = new double[T.VertexCount], y = new double[T.VertexCount];
            for (int i = 0; i < T.VertexCount; ++i) { x[i] = T.VertexPosition(i).x; y[i] = T.VertexPosition(i).y; }
            double[] Lx = new double[T.VertexCount], Ly = new double[T.VertexCount];
            L.Multiply(x, Lx); L.Multiply(y, Ly);
            for (int i = 0; i < T.VertexCount; ++i)
                if (T.IsBoundaryVertex(i) == false) Assert.True(Math.Abs(Lx[i]) + Math.Abs(Ly[i]) < 1e-8, $"{Lx[i]} {Ly[i]}");
            Assert.Equal(TotalArea(T), T.VertexAreas().Sum(), 9);
        }

        /// <summary>
        /// Planar wedge with a corner of the given angle at the origin: rays of length L, a polar grid of
        /// rings and spokes.
        /// </summary>
        static DMesh3 Wedge(double angleDeg, double L, int rings, int spokes)
        {
            DMesh3 mesh = new DMesh3();
            double a = angleDeg * Math.PI / 180;
            int apex = mesh.AppendVertex(Vector3d.Zero);
            var ring = new List<int[]>();
            for (int r = 1; r <= rings; ++r) {
                int[] row = new int[spokes + 1];
                double rad = L * r / rings;
                for (int s = 0; s <= spokes; ++s) {
                    double t = a * s / spokes;
                    row[s] = mesh.AppendVertex(new Vector3d(rad * Math.Cos(t), rad * Math.Sin(t), 0));
                }
                ring.Add(row);
            }
            for (int s = 0; s < spokes; ++s) mesh.AppendTriangle(apex, ring[0][s], ring[0][s + 1]);
            for (int r = 0; r + 1 < rings; ++r)
                for (int s = 0; s < spokes; ++s) {
                    mesh.AppendTriangle(ring[r][s], ring[r + 1][s], ring[r + 1][s + 1]);
                    mesh.AppendTriangle(ring[r][s], ring[r + 1][s + 1], ring[r][s + 1]);
                }
            return mesh;
        }

        [Fact]
        public void Refine_Wedges_AllSucceed()
        {
            // corners from 5 to 55 degrees (all small input angles); 45 degrees, 3 rings, 1 spoke needs a bad
            // triangle to be looked at again after its circumcenter split a boundary edge instead
            foreach (double ang in new[] { 5.0, 10, 20, 30, 45, 55 })
                foreach (int rings in new[] { 3, 10 })
                    foreach (int sp in new[] { 1, 3 }) {
                        var T = new IntrinsicTriangulation(Wedge(ang, 10, rings, sp));
                        var r = T.DelaunayRefine(30, 3000);
                        Assert.True(r.Status == IntrinsicRefinementStatus.Success, $"{ang} {rings} {sp}: {r}");
                        Assert.Equal(0, r.RemainingBadTriangles);
                        Assert.Equal(0, r.NegativeWeights);
                        AssertConsistent(T);
                        Assert.True(PlanarLengthError(T) < 1e-9);
                    }
        }

        [Fact]
        public void Refine_SmallInputAngle_Terminates()
        {
            // a 15 degree corner: near it no triangulation meets a 25 degree bound; the triangles there are
            // skipped (their shortest edge joins the two sides of the corner) and the refinement terminates
            DMesh3 mesh = Wedge(15, 10, 10, 3);
            var T = new IntrinsicTriangulation(mesh);
            var r = T.DelaunayRefine(25, 5000);
            output.WriteLine(r.ToString());
            Assert.Equal(IntrinsicRefinementStatus.Success, r.Status);
            Assert.True(r.SkippedTriangles > 0);
            Assert.True(r.InsertedVertices < 1000, $"{r.InsertedVertices}");
            Assert.Equal(0, r.NegativeWeights);
            AssertConsistent(T);
            Assert.True(PlanarLengthError(T) < 1e-9);
            for (int t = 0; t < T.TriangleCount; ++t) {
                if (MinAngleDeg(T, t) >= 25 - 1e-9) continue;
                Index3i tv = T.GetTriangleIndices(t);
                double rmin = Math.Min(T.VertexPosition(tv.a).Length, Math.Min(T.VertexPosition(tv.b).Length, T.VertexPosition(tv.c).Length));
                Assert.True(rmin < 2.6, $"bad triangle away from the corner at distance {rmin}");
            }
        }

        [Fact]
        public void InsertVertex_SplitEdge_CurvedSurface()
        {
            // at curved vertices signpost angles are scaled (angle sums below 2 pi): the new signposts must be too
            DMesh3 mesh = RingSphere(1, 20, 8);
            var T = new IntrinsicTriangulation(mesh);
            T.FlipToDelaunay();
            double area0 = TotalArea(T);
            var rnd = new Random(3);
            for (int k = 0; k < 40; ++k) {
                int t = rnd.Next(T.TriangleCount);
                Vector2d q0, q1, q2;
                T.GetTriangleLayout(t, out q0, out q1, out q2);
                if (k % 2 == 0) Assert.True(T.InsertVertex(t, (q0 + q1 + q2) / 3) >= 0);
                else Assert.True(T.SplitEdge(3 * t + rnd.Next(3), 0.25 + 0.5 * rnd.NextDouble()) >= 0);
                AssertConsistent(T);
            }
            Assert.Equal(area0, TotalArea(T), 9);
            Assert.True(TraceEndError(T) < 1e-9, $"{TraceEndError(T)}");
            var spatial = new DMeshAABBTree3(mesh, true);
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i) {
                Vector3d p = T.VertexPosition(i);
                Assert.True(Math.Sqrt(MeshQueries.TriangleDistance(mesh, spatial.FindNearestTriangle(p), p).DistanceSquared) < 1e-8);
            }
            T.FlipToDelaunay();
            AssertConsistent(T);
            CheckSubdivision(mesh, T, output);
        }

        [Fact]
        public void Flips_AtBoundaryVertexWithLargeAngleSum_KeepSignposts()
        {
            // a pleated fan around a boundary vertex: angle sum above 2 pi, so its signposts must not wrap
            DMesh3 mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            int n = 24;
            var ring = new int[n + 1];
            for (int k = 0; k <= n; ++k) {
                double a = Math.PI * k / n;
                double rad = (k % 2 == 0) ? 1 : 0.3;
                ring[k] = mesh.AppendVertex(new Vector3d(rad * Math.Cos(a), rad * Math.Sin(a), (k % 2 == 0) ? 0.6 : -0.2));
            }
            for (int k = 0; k < n; ++k) mesh.AppendTriangle(c, ring[k], ring[k + 1]);
            var T = new IntrinsicTriangulation(mesh);
            Assert.True(T.AngleSum(T.VertexIndex(c)) > 2.5 * Math.PI, $"{T.AngleSum(T.VertexIndex(c))}");
            Assert.True(T.FlipToDelaunay() > 0);
            AssertConsistent(T);
            var r = T.DelaunayRefine(25);
            output.WriteLine(r.ToString());
            AssertConsistent(T);
            Assert.True(TraceEndError(T) < 1e-9, $"{TraceEndError(T)}");
            CheckSubdivision(mesh, T, output);
        }

        // ---------------------------------------------------------------- common subdivision

        static void CheckSubdivision(DMesh3 mesh, IntrinsicTriangulation T, ITestOutputHelper output)
        {
            var cs = T.ExtractCommonSubdivision();
            output.WriteLine($"{cs.Status} {cs.Error}: {cs.Faces.Count} faces, input area error {cs.MaxInputAreaError}, intrinsic {cs.MaxIntrinsicAreaError}");
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.True(cs.Faces.Count >= Math.Max(mesh.TriangleCount, T.TriangleCount));
            // the faces tile every input triangle and every intrinsic triangle
            var inputSum = new Dictionary<int, double>();
            double[] intrinsicSum = new double[T.TriangleCount];
            foreach (var f in cs.Faces) {
                Assert.True(f.Area > 0);
                double cur;
                inputSum.TryGetValue(f.InputTriangle, out cur);
                inputSum[f.InputTriangle] = cur + f.Area;
                intrinsicSum[f.IntrinsicTriangle] += f.Area;
                foreach (Vector3d b in f.Barycentric)
                    Assert.True(b.x > -1e-9 && b.y > -1e-9 && b.z > -1e-9 && Math.Abs(b.x + b.y + b.z - 1) < 1e-9);
            }
            Assert.Equal(mesh.TriangleCount, inputSum.Count);
            foreach (int tid in mesh.TriangleIndices()) {
                double a = mesh.GetTriArea(tid);
                Assert.True(Math.Abs(inputSum[tid] - a) < 1e-9 * a, $"input triangle {tid}: {inputSum[tid]} vs {a}");
            }
            for (int t = 0; t < T.TriangleCount; ++t)
                Assert.True(Math.Abs(intrinsicSum[t] - T.TriangleArea(t)) < 1e-6 * T.TriangleArea(t), $"intrinsic triangle {t}");
            // as a mesh: same area, points on the input
            DMesh3 sub = cs.ToMesh();
            Assert.Equal(MeshMeasurements.AreaT(mesh, mesh.TriangleIndices()), MeshMeasurements.AreaT(sub, sub.TriangleIndices()), 8);
        }

        [Fact]
        public void CommonSubdivision_TilesInputAndIntrinsicTriangles()
        {
            // input itself: one face per triangle
            DMesh3 grid = Grid(4, 5, (x, y) => 0.2 * x * y);
            var T = new IntrinsicTriangulation(grid);
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.Equal(grid.TriangleCount, cs.Faces.Count);

            // flipped (obtuse sphere), refined (sphere, plane with inserted vertices on input edges), curved grid
            DMesh3 sphere = RingSphere(1, 60, 12);
            T = new IntrinsicTriangulation(sphere);
            T.FlipToDelaunay();
            CheckSubdivision(sphere, T, output);
            Assert.True(T.DelaunayRefine(30).InsertedVertices > 50);
            CheckSubdivision(sphere, T, output);

            DMesh3 plane = ObtusePlane(6, 40, 1.0, 0.05);
            T = new IntrinsicTriangulation(plane);
            T.DelaunayRefine(25);
            Assert.True(T.VertexCount > T.InputVertexCount + 100);
            CheckSubdivision(plane, T, output);

            DMesh3 bump = Grid(4, 9, (x, y) => 0.5 * Math.Exp(-(x * x + y * y)));
            T = new IntrinsicTriangulation(bump);
            T.DelaunayRefine(30);
            CheckSubdivision(bump, T, output);
        }

        // ---------------------------------------------------------------- integration

        [Fact]
        public void HeatDistance_ObtusePlane_RefinementMoreAccurate()
        {
            DMesh3 mesh = ObtusePlane(10, 200, 1.0, 0.05);
            int src = NearestVertex(mesh, new Vector3d(5, 5, 0));
            Vector3d ps = mesh.GetVertex(src);
            Func<int, double> exact = v => mesh.GetVertex(v).Distance(ps);
            Func<int, bool> inner = v => mesh.GetVertex(v).x >= 1 && mesh.GetVertex(v).x <= 9;

            var flip = new MeshGeodesicDistance(mesh);
            Assert.True(flip.Compute(new[] { src }));
            Assert.Null(flip.Refinement);
            double fmean, fmax;
            Errors(mesh, flip.Distance, exact, inner, out fmean, out fmax);

            var refined = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
            Assert.True(refined.Compute(new[] { src }));
            output.WriteLine(refined.Refinement.ToString());
            Assert.Equal(IntrinsicRefinementStatus.Success, refined.Refinement.Status);
            double mean, max;
            Errors(mesh, refined.Distance, exact, inner, out mean, out max);
            output.WriteLine($"flips: mean {fmean} max {fmax}; refined: mean {mean} max {max}");
            Assert.True(mean < 0.5 * fmean, $"refined {mean} vs flips {fmean}");
            Assert.True(max < fmax, $"refined max {max} vs flips {fmax}");
            // distances only at mesh vertices
            Assert.Equal(mesh.MaxVertexID, refined.Distance.Length);
            Assert.Equal(0, refined.Distance[src]);
        }

        [Fact]
        public void VectorHeat_Refined_TransportAndLogMap()
        {
            DMesh3 mesh = ObtusePlane(10, 200, 1.0, 0.05);
            int src = NearestVertex(mesh, new Vector3d(5, 5, 0));
            Vector3d ps = mesh.GetVertex(src);
            var flip = new VectorHeatMethod(mesh);
            var refined = new VectorHeatMethod(mesh) { IntrinsicRefine = true };
            Assert.Null(flip.Refinement);
            Assert.Equal(IntrinsicRefinementStatus.Success, refined.Refinement.Status);
            Assert.True(refined.Triangulation.VertexCount > refined.Triangulation.InputVertexCount);

            // transport of a constant vector in the plane stays constant
            Vector2d v0 = refined.Triangulation.FromWorld(src, new Vector3d(1, 0, 0));
            Vector2d[] tr = refined.TransportVector(src, v0);
            double maxAng = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d w = refined.Triangulation.ToWorld(v, tr[v]);
                maxAng = Math.Max(maxAng, Math.Atan2(Math.Abs(w.y), w.x));
            }
            output.WriteLine($"transport max angle {maxAng}");
            Assert.True(maxAng < 0.05, $"{maxAng}");

            // log map against the true offset
            Func<VectorHeatMethod, double> logError = (vhm) => {
                Vector2d[] log = vhm.ComputeLogMap(src);
                double sum = 0; int n = 0;
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d p = mesh.GetVertex(v);
                    if (p.x < 1 || p.x > 9 || p.y < 1 || p.y > 9) continue;
                    Vector3d w = vhm.Triangulation.ToWorld(src, log[v]);
                    sum += (w - (p - ps)).Length; n++;
                }
                return sum / n;
            };
            double ef = logError(flip), er = logError(refined);
            output.WriteLine($"log map mean error: flips {ef}, refined {er}");
            Assert.True(er < ef, $"refined {er} vs flips {ef}");
        }

        [Fact]
        public void Refinement_NonManifoldInput_Reported()
        {
            // two triangles sharing only a vertex (bowtie)
            DMesh3 mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
            int d = mesh.AppendVertex(new Vector3d(-1, 0, 0));
            int e = mesh.AppendVertex(new Vector3d(0, -1, 0));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, d, e);
            var geo = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
            Assert.True(geo.Compute(new[] { b }));
            Assert.Equal(IntrinsicRefinementStatus.NonManifoldInput, geo.Refinement.Status);
            Assert.True(geo.Distance[c] > 1 && geo.Distance[c] < 2);
        }
    }
}
