using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Local refinement for hidden gaps and walls: only the cells around them are refined, the rest keeps
    /// the cell size. The transition between refined and unrefined cells must keep the contour closed and
    /// manifold, and the cost must follow the size of the refined regions, not of the whole model.
    /// </summary>
    public class LocalRefinementTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        static double Volume(DMesh3 m)
        {
            Vector3d c = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), (v) => m.GetVertex(v) - c).x;
        }

        // every edge has two triangles that traverse it in opposite directions, no bowtie vertices
        static void AssertClosedOrientedManifold(DMesh3 m)
        {
            Assert.True(m.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(m));
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid);
                Assert.NotEqual(DMesh3.InvalidID, et.b);
                bool a = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.a)) != DMesh3.InvalidID;
                bool b = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.b)) != DMesh3.InvalidID;
                Assert.NotEqual(a, b);
            }
        }

        static readonly Quaterniond OffAxis = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27);

        // a large plate and, beside it, two small boxes with a gap of 0.45 cells: only the gap needs refining
        static DMesh3 PlateAndGap(double cell)
        {
            DMesh3 m = Box(new Vector3d(-10, -5, -2), new Vector3d(10, 5, 2));
            MeshEditor.Append(m, Box(new Vector3d(12, -1, -1), new Vector3d(14, 0, 1)));
            MeshEditor.Append(m, Box(new Vector3d(12, 0.45 * cell, -1), new Vector3d(14, 1 + 0.45 * cell, 1)));
            MeshTransforms.Rotate(m, Vector3d.Zero, OffAxis);
            return m;
        }

        [Fact]
        public void Solidify_LocalGap_CostsMuchLessThanGlobalRefinement()
        {
            double cell = 0.5;
            DMesh3 mesh = PlateAndGap(cell);
            double volume = 20 * 10 * 4 + 2 * (2 * 1 * 2);

            var global = new MeshWindingNumberSolidify(mesh) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MinRefineEdges = 1, LocalRefinement = false };
            Assert.True(global.Compute());
            var local = new MeshWindingNumberSolidify(mesh) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MinRefineEdges = 1 };
            Assert.True(local.Compute());

            // both refine to the same cell size and resolve the gap
            Assert.Equal(cell / 4, global.UsedCellSize);
            Assert.Equal(cell / 4, local.UsedCellSize);
            Assert.Equal(4, local.RefineFactor);
            foreach (var s in new[] { global, local }) {
                Assert.Equal(0, s.HiddenGapEdges);
                AssertClosedOrientedManifold(s.Result);
                Assert.Equal(3, Components(s.Result));         // the plate and the two boxes, the gap is kept
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(s.Result));
                Assert.InRange(Volume(s.Result), 0.99 * volume, 1.01 * volume);
            }
            // only the cells around the gap are refined: a fraction of the work (the plate alone has ~7000
            // surface cells, refining it costs 16x)
            Assert.InRange(local.RefinedCells, 1, 1500);
            Assert.True(local.WindingEvaluations < 0.25 * global.WindingEvaluations,
                $"local {local.WindingEvaluations} vs global {global.WindingEvaluations} evaluations");
            Assert.True(local.Result.TriangleCount < 0.3 * global.Result.TriangleCount);
        }

        // union of a large plate and a small box above it with a narrow gap over a small area
        [Fact]
        public void Boolean_LocalGap_CostsMuchLessThanGlobalRefinement()
        {
            double cell = 0.5, gap = 0.3 * cell;
            DMesh3 a = Box(new Vector3d(-10, -5, -2), new Vector3d(10, 5, 2));
            DMesh3 b = Box(new Vector3d(2, 5 + gap, -1), new Vector3d(5, 7, 1.5));
            MeshTransforms.Rotate(a, Vector3d.Zero, OffAxis);
            MeshTransforms.Rotate(b, Vector3d.Zero, OffAxis);
            double volume = 20 * 10 * 4 + 3 * (2 - gap) * 2.5;

            var global = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = cell, ReduceTolerance = 0, MinRefineEdges = 1, LocalRefinement = false };
            Assert.True(global.Compute());
            var local = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = cell, ReduceTolerance = 0, MinRefineEdges = 1 };
            Assert.True(local.Compute());

            Assert.True(local.UsedCellSize < cell);
            Assert.Equal(global.UsedCellSize, local.UsedCellSize);
            foreach (var bo in new[] { global, local }) {
                Assert.Equal(0, bo.HiddenGapEdges);
                AssertClosedOrientedManifold(bo.Result);
                Assert.Equal(2, Components(bo.Result));
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(bo.Result));
                Assert.InRange(Volume(bo.Result), 0.99 * volume, 1.01 * volume);
            }
            Assert.True(local.WindingEvaluations < 0.25 * global.WindingEvaluations,
                $"local {local.WindingEvaluations} vs global {global.WindingEvaluations} evaluations");
        }

        // no hidden gaps: nothing is refined, the result is the same as without local refinement
        [Fact]
        public void NoHiddenGaps_SameAsUnrefined()
        {
            DMesh3 mesh = Box(new Vector3d(-3, -2, -1), new Vector3d(3, 2, 1));
            MeshTransforms.Rotate(mesh, Vector3d.Zero, OffAxis);
            var local = new MeshWindingNumberSolidify(mesh) { CellSize = 0.25, Adaptive = true, ReduceTolerance = 0 };
            var plain = new MeshWindingNumberSolidify(mesh) { CellSize = 0.25, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 0 };
            Assert.True(local.Compute());
            Assert.True(plain.Compute());
            Assert.Equal(0, local.RefinedCells);
            Assert.Equal(1, local.RefineFactor);
            Assert.Equal(0.25, local.UsedCellSize);
            Assert.InRange(local.WindingEvaluations, 0.99 * plain.WindingEvaluations, 1.01 * plain.WindingEvaluations);
            Assert.InRange(local.Result.TriangleCount, 0.98 * plain.Result.TriangleCount, 1.02 * plain.Result.TriangleCount);
            Assert.True(local.Result.IsClosed());
        }


        // --- the transition itself, on analytic fields with random refined regions ---

        class LambdaImplicit : ImplicitFunction3d
        {
            public Func<Vector3d, double> F;
            public double Value(ref Vector3d pt) { return F(pt); }
        }

        // sparse dual contouring of the SDF f (negative inside) with the coarse cells for which refine() is
        // true refined by factor
        static DualContouring ContourMixed(Func<Vector3d, double> f, double cell, double extent, int factor, Func<Vector3i, bool> refine)
        {
            var bounds = new AxisAlignedBox3d(-extent * Vector3d.One, extent * Vector3d.One);
            int n = (int)Math.Ceiling(2 * extent / cell);
            double hf = cell / factor;
            var candidates = new List<Vector3i>();
            var refined = new List<Vector3i>();
            var fineCandidates = new List<Vector3i>();
            for (int k = 0; k < n; ++k) {
                for (int j = 0; j < n; ++j) {
                    for (int i = 0; i < n; ++i) {
                        var c = new Vector3i(i, j, k);
                        Vector3d center = bounds.Min + cell * new Vector3d(i + 0.5, j + 0.5, k + 0.5);
                        if (Math.Abs(f(center)) < cell)
                            candidates.Add(c);
                        if (refine(c) == false)
                            continue;
                        refined.Add(c);
                        for (int fk = 0; fk < factor; ++fk)
                            for (int fj = 0; fj < factor; ++fj)
                                for (int fi = 0; fi < factor; ++fi) {
                                    Vector3i fc = factor * c + new Vector3i(fi, fj, fk);
                                    if (Math.Abs(f(bounds.Min + hf * new Vector3d(fc.x + 0.5, fc.y + 0.5, fc.z + 0.5))) < hf)
                                        fineCandidates.Add(fc);
                                }
                    }
                }
            }
            var dc = new DualContouring() {
                Implicit = new LambdaImplicit() { F = f }, IsoValue = 0, InsideIsLess = true, CellSize = cell, Bounds = bounds,
                CandidateCells = candidates,
                NodeValueF = (v) => f(bounds.Min + cell * new Vector3d(v.x, v.y, v.z))
            };
            dc.SetRefinement(factor, refined, fineCandidates, (v) => f(bounds.Min + hf * new Vector3d(v.x, v.y, v.z)));
            dc.Generate();
            return dc;
        }

        static int EulerCharacteristic(DMesh3 m) { return m.VertexCount - m.EdgeCount + m.TriangleCount; }

        static Func<Vector3i, bool> RandomRegion(int seed, double p)
        {
            return (c) => {
                // deterministic per cell
                int hsh = unchecked(c.x * 73856093 ^ c.y * 19349663 ^ c.z * 83492791 ^ seed * 2654435761u.GetHashCode());
                return new Random(hsh).NextDouble() < p;
            };
        }

        [Theory]
        [InlineData(2, 1, 0.3)]
        [InlineData(2, 2, 0.6)]
        [InlineData(4, 3, 0.3)]
        [InlineData(4, 4, 0.1)]
        [InlineData(8, 5, 0.5)]
        public void Transition_RandomRegions_Sphere_ClosedManifoldGenusZero(int factor, int seed, double p)
        {
            Func<Vector3d, double> sphere = (q) => q.Length - 1;
            DualContouring dc = ContourMixed(sphere, 0.15, 1.3, factor, RandomRegion(seed, p));
            DMesh3 m = dc.Mesh;
            Assert.Equal(0, dc.TransitionErrors);
            Assert.Equal(0, dc.NonManifoldTriangles);
            AssertClosedOrientedManifold(m);
            Assert.Equal(1, Components(m));
            Assert.Equal(2, EulerCharacteristic(m));
            foreach (int vid in m.VertexIndices())
                Assert.InRange(m.GetVertex(vid).Length, 0.97, 1.03);
            Assert.InRange(Volume(m), 0.98 * 4.0 / 3 * Math.PI, 1.02 * 4.0 / 3 * Math.PI);
        }

        [Theory]
        [InlineData(2, 7, 0.4)]
        [InlineData(4, 8, 0.4)]
        public void Transition_RandomRegions_Torus_GenusOne(int factor, int seed, double p)
        {
            Func<Vector3d, double> torus = (q) => {
                double r = Math.Sqrt(q.x * q.x + q.y * q.y) - 0.8;
                return Math.Sqrt(r * r + q.z * q.z) - 0.35;
            };
            DualContouring dc = ContourMixed(torus, 0.12, 1.3, factor, RandomRegion(seed, p));
            Assert.Equal(0, dc.TransitionErrors);
            Assert.Equal(0, dc.NonManifoldTriangles);
            AssertClosedOrientedManifold(dc.Mesh);
            Assert.Equal(0, EulerCharacteristic(dc.Mesh));
        }

        // a sphere with many small balls (narrower than a coarse cell) on and near its surface: the fine cells
        // see them, the coarse ones only partly, so the fine signs on many faces between refined and unrefined
        // cells are not equivalent to the coarse ones and get imposed. The result must stay closed and manifold.
        [Theory]
        [InlineData(2, 11, 0.5)]
        [InlineData(4, 12, 0.5)]
        [InlineData(4, 13, 0.2)]
        public void Transition_SubCellFeatures_ImposedSigns_StaysClosedManifold(int factor, int seed, double p)
        {
            var rnd = new Random(seed);
            var balls = new List<Vector3d>();
            for (int i = 0; i < 150; ++i) {
                Vector3d d = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                balls.Add((1.0 + 0.12 * rnd.NextDouble()) * d);
            }
            Func<Vector3d, double> f = (q) => {
                double v = q.Length - 1;
                foreach (Vector3d b in balls)
                    v = Math.Min(v, q.Distance(b) - 0.06);
                return v;
            };
            DualContouring dc = ContourMixed(f, 0.15, 1.4, factor, RandomRegion(seed, p));
            Assert.True(dc.ImposedEdges + dc.ImposedFaces > 0);
            Assert.Equal(0, dc.TransitionErrors);
            // such small features make dual contouring leave out a few quads that would be non-manifold, on
            // a uniform fine grid too (the callers fill these holes); the transition must not add any
            DualContouring uniform = ContourMixed(f, 0.15, 1.4, factor, (c) => true);
            Assert.InRange(dc.NonManifoldTriangles, 0, uniform.NonManifoldTriangles);
            DMesh3 m = dc.Mesh;
            if (dc.NonManifoldTriangles == 0)
                Assert.Empty(MeshIterators.BowtieVertices(m));      // (the borders of left-out quads can meet at a vertex)
            int boundary = 0;
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) { boundary++; continue; }
                bool a = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.a)) != DMesh3.InvalidID;
                bool b = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.b)) != DMesh3.InvalidID;
                Assert.NotEqual(a, b);
            }
            Assert.InRange(boundary, 0, 2 * dc.NonManifoldTriangles);     // only the borders of the left-out quads
        }
    }
}
