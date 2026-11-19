// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Sharp creases in the tet boundary (docs/design/TET_MESHING.md section 17.14, P4(b), S1-F): MeshTetrahedralization.CreaseAngleDegrees and
    /// TetFeatureSurface. Off by default (the output as before bit for bit); with it the wrap's surface gets the feature pass, is made Delaunay
    /// conforming and meshed; a recovery that does not finish falls back to the rounded wrap.
    /// </summary>
    public class TetMeshCreaseTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)), new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))) }.Generate().MakeDMesh();

        // an L-shaped prism: one re-entrant (concave) edge, which the alpha wrap bridges
        static readonly Lazy<DMesh3> LShared = new Lazy<DMesh3>(() => ExactMeshBoolean.Union(new List<DMesh3> { Box(0, 0, 0, 1, 0.5, 0.4), Box(0, 0, 0, 0.5, 1, 0.4) }));
        internal static DMesh3 LShape() => LShared.Value;

        // a slot (0.2 wide, 0.2 deep) in the top of a plate: two re-entrant edges at its bottom; after the flips edges are still missing (they need Steiner points on edges)
        static readonly Lazy<DMesh3> SlotShared = new Lazy<DMesh3>(() => ExactMeshBoolean.Union(new List<DMesh3> { Box(0, 0, 0, 1.2, 0.8, 0.2), Box(0, 0, 0, 1.2, 0.3, 0.4), Box(0, 0.5, 0, 1.2, 0.8, 0.4) }));

        static MeshTetrahedralization Make(DMesh3 m, double crease = 30, double alpha = 40) => new MeshTetrahedralization(m) { RelativeAlpha = alpha, CreaseAngleDegrees = crease };
        static void Run(MeshTetrahedralization tm) { Assert.True(tm.Compute(), tm.Status.ToString()); Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status); }

        // the reference runs are deterministic and read only: computed once
        static readonly Lazy<MeshTetrahedralization> RoundedRef = new Lazy<MeshTetrahedralization>(() => { var tm = Make(LShape(), 0); Run(tm); return tm; });
        static readonly Lazy<MeshTetrahedralization> FeaturedRef = new Lazy<MeshTetrahedralization>(() => { var tm = Make(LShape()); Run(tm); return tm; });

        // the concave edge of the L (x = y = 0.5, z in 0..0.4): the mean distance of 9 points on it to the boundary, in offsets
        static double ConcaveCreaseDistance(MeshTetrahedralization tm)
        {
            var b = tm.Result.ToSurfaceMesh();
            var tree = new DMeshAABBTree3(b, true);
            double sum = 0;
            for (int i = 0; i <= 8; ++i) {
                var p = new Vector3d(0.5, 0.5, 0.4 * i / 8.0);
                tree.FindNearestTriangle(p, out double d2); sum += Math.Sqrt(d2) / tm.UsedOffset;
            }
            return sum / 9;
        }

        // ------------------------------------------------------------------------------------------------ off by default

        [Fact]
        public void Default_IsOff_AndSwitchedOffValuesGiveTheDefaultResult()
        {
            Assert.Equal(0.0, new MeshTetrahedralization(new DMesh3()).CreaseAngleDegrees);
            var def = RoundedRef.Value;
            Assert.DoesNotContain(def.Counts.Keys, k => k.StartsWith("crease"));
            foreach (double off in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var tm = Make(LShape(), off); Run(tm);
                Assert.Equal(def.Result.Tets, tm.Result.Tets);
                Assert.Equal(def.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(def.Counts, tm.Counts);
            }
        }

        [Fact]
        public void CreaseAngleAboveEveryCrease_GivesTheDefaultResult()
        {
            // the L-shape's creases turn by 90 degrees: with 100 none is a crease, and the pass changes nothing
            var tm = Make(LShape(), 100); Run(tm);
            var def = RoundedRef.Value;
            Assert.Equal(def.Result.Tets, tm.Result.Tets);
            Assert.Equal(def.Result.Vertices, tm.Result.Vertices);
            Assert.DoesNotContain(tm.Counts.Keys, k => k.StartsWith("crease"));
        }

        [Fact]
        public void InputWithoutCreases_GivesTheDefaultResult()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { EdgeVertices = 8 }.Generate().MakeDMesh();
            var a = new MeshTetrahedralization(sphere) { RelativeAlpha = 20 }; Run(a);
            var b = new MeshTetrahedralization(sphere) { RelativeAlpha = 20, CreaseAngleDegrees = 30 }; Run(b);
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.Equal(a.Result.Vertices, b.Result.Vertices);
            Assert.False(b.Counts.ContainsKey("crease_recovery_failed"));
        }

        // ------------------------------------------------------------------------------------------------ the success path

        [Fact]
        public void LShape_KeepsTheConcaveCrease_AndEveryInvariant()
        {
            var tm = FeaturedRef.Value;
            Assert.Equal(1, tm.Counts["crease_recovered"]);
            Assert.False(tm.Counts.ContainsKey("crease_recovery_failed"));
            Assert.True(tm.Counts["crease_missing_triangles"] > 0, "the featured surface needed a recovery");
            TetMeshingStage2Tests.Invariants(tm);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(tm.Result.ToSurfaceMesh()));
            Assert.True(tm.Targets.MinDihedralMet);
            double rounded = ConcaveCreaseDistance(RoundedRef.Value), featured = ConcaveCreaseDistance(tm);
            Assert.True(featured < 0.5 * rounded, "concave crease " + featured + " offsets (featured) against " + rounded + " (rounded)");
            Assert.True(featured < 3.5, featured.ToString());
        }

        // ------------------------------------------------------------------------------------------------ reuse, threads, scale

        [Fact]
        public void Reuse_ComputeTwiceOnOneInstance_GivesTheSameResult()
        {
            var tm = Make(LShape());
            Run(tm);
            var tets = tm.Result.Tets.ToList(); var verts = tm.Result.Vertices.ToList(); var counts = new Dictionary<string, long>(tm.Counts);
            Run(tm);
            Assert.Equal(tets, tm.Result.Tets);
            Assert.Equal(verts, tm.Result.Vertices);
            Assert.Equal(counts, tm.Counts);
        }

        [Fact]
        public void EveryMaxThreads_AndRepeatedRuns_GiveTheSameResult()
        {
            var a = FeaturedRef.Value;
            foreach (int threads in new[] { 1, 3 }) {
                var tm = Make(LShape()); tm.MaxThreads = threads; Run(tm);
                Assert.Equal(a.Result.Tets, tm.Result.Tets);
                Assert.Equal(a.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(a.Counts, tm.Counts);
            }
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = LShape();
            var a = FeaturedRef.Value;
            DMesh3 moved = Invariance.Transformed(input, s);
            var b = Make(moved);
            if (!s.IsExact) {
                // the relative alpha and offset are fractions of the bounding box diagonal, which a rotation changes: give the moved input the same absolute alpha and offset
                double ratio = moved.CachedBounds.DiagonalLength / (s.Scale * input.CachedBounds.DiagonalLength);
                b.RelativeAlpha = a.RelativeAlpha * ratio; b.RelativeOffset = a.RelativeOffset * ratio;
            }
            Run(b);
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                // not bit for bit (rounding of the moved coordinates), but the same method: recovered, the tets within 25 %, the crease as close
                Assert.Equal(1, b.Counts["crease_recovered"]);
                Assert.InRange((double)b.Result.Tets.Count, 0.75 * a.Result.Tets.Count, 1.25 * a.Result.Tets.Count);
                Assert.True(b.Targets.MinDihedralMet);
                var tree = new DMeshAABBTree3(b.Result.ToSurfaceMesh(), true);
                double sum = 0;
                for (int i = 0; i <= 8; ++i) {
                    tree.FindNearestTriangle(s.Apply(new Vector3d(0.5, 0.5, 0.4 * i / 8.0)), out double d2); sum += Math.Sqrt(d2) / b.UsedOffset;
                }
                Assert.True(sum / 9 < 3.5, (sum / 9).ToString());
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // ------------------------------------------------------------------------------------------------ the fallback

        [Fact]
        public void RecoveryBudgetTooSmall_FallsBackToTheRoundedWrap()
        {
            var tm = Make(LShape()); tm.CreaseSteinerLimit = 0;
            Run(tm);
            Assert.Equal(1, tm.Counts["crease_recovery_failed"]);
            Assert.False(tm.Counts.ContainsKey("crease_recovered"));
            var def = RoundedRef.Value;
            Assert.Equal(def.Result.Tets, tm.Result.Tets);
            Assert.Equal(def.Result.Vertices, tm.Result.Vertices);
            TetMeshingStage2Tests.Invariants(tm);
        }

        [Fact]
        public void Cancelled_AfterTheWrap_IsReportedAsCancelled()
        {
            MeshTetrahedralization tm = null;
            tm = Make(LShape());
            tm.CancelF = () => tm.Wrap != null;
            Assert.False(tm.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, tm.Status);
            Assert.Null(tm.Result);
        }

        // ------------------------------------------------------------------------------------------------ TetFeatureSurface

        // the featured surface of the L-shape (the wrap + the feature pass), a copy
        static DMesh3 FeaturedSurface(out MeshAlphaWrap wrap)
        {
            wrap = new MeshAlphaWrap(LShape()) { RelativeAlpha = 40, RelativeOffset = 600 };
            Assert.True(wrap.Compute());
            var fp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), LShape(), wrap.UsedAlpha, wrap.UsedOffset);
            Assert.True(fp.Compute());
            return fp.Mesh;
        }

        [Fact]
        public void Recover_MakesEverySurfaceTriangleAFacet_AndTheInsideCellsFillTheSurface()
        {
            DMesh3 s = FeaturedSurface(out MeshAlphaWrap wrap);
            double floor = 0.125 * Math.Min(wrap.UsedOffset, wrap.UsedAlpha - wrap.UsedOffset);
            TetFeatureSurface r = TetFeatureSurface.Recover(s, floor, 1000, null);
            Assert.NotNull(r);
            Assert.True(r.MissingTrianglesBefore > 0 && r.Flips > 0);
            foreach (int t in r.Surface.TriangleIndices()) {
                Index3i tv = r.Surface.GetTriangle(t);
                Assert.True(r.Dt.IsFacet(r.Ids[tv.a], r.Ids[tv.b], r.Ids[tv.c], out _, out _));
            }
            // the cells labelled inside are a tet mesh of exactly the surface: their volume is the volume of the closed surface
            double vol = 0;
            foreach (int c in r.Dt.CellIndices()) {
                if (r.Dt.CellLabel(c) != 1) continue;
                Index4i q = r.Dt.GetCell(c);
                Vector3d a = r.Dt.GetVertex(q.a);
                vol += (r.Dt.GetVertex(q.b) - a).Dot((r.Dt.GetVertex(q.c) - a).Cross(r.Dt.GetVertex(q.d) - a)) / 6;
            }
            double surfaceVol = MeshMeasurements.VolumeArea(r.Surface, r.Surface.TriangleIndices(), r.Surface.GetVertex).x;
            Assert.True(vol > 0);
            Assert.Equal(surfaceVol, vol, 9);
        }

        [Fact]
        public void Recover_StopsAtTheFloorAndAtTheBudget_WithoutAResult()
        {
            // the L-shape needs Steiner points (the test above has Steiner > 0 or flips only: use the budget and floor on a surface that does)
            DMesh3 s = FeaturedSurface(out MeshAlphaWrap wrap);
            double floor = 0.125 * Math.Min(wrap.UsedOffset, wrap.UsedAlpha - wrap.UsedOffset);
            var ok = TetFeatureSurface.Recover(new DMesh3(s, true), floor, 1000, null);
            Assert.NotNull(ok);
            Assert.True(ok.Steiner > 0, "this input needs Steiner points");
            Assert.Null(TetFeatureSurface.Recover(new DMesh3(s, true), floor, 0, null));                          // the budget
            Assert.Null(TetFeatureSurface.Recover(new DMesh3(s, true), 1e3 * wrap.UsedAlpha, 1000, null));        // the floor: no edge may be split
            Assert.Null(TetFeatureSurface.Recover(new DMesh3(s, true), floor, 1000, () => true));                 // cancelled
        }

        [Fact]
        public void Slot_NeedsEdgeSplits_AndStaysValidAndCloser()
        {
            DMesh3 slot = SlotShared.Value;
            var wrap = new MeshAlphaWrap(slot) { RelativeAlpha = 40, RelativeOffset = 600 };
            Assert.True(wrap.Compute());
            var fp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), slot, wrap.UsedAlpha, wrap.UsedOffset);
            Assert.True(fp.Compute());
            double floor = 0.125 * Math.Min(wrap.UsedOffset, wrap.UsedAlpha - wrap.UsedOffset);
            var r = TetFeatureSurface.Recover(fp.Mesh, floor, 1000, null);
            Assert.NotNull(r);
            Assert.True(r.MissingEdgesBefore > 0 && r.Steiner > 0);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Surface));
            var tm = Make(slot); Run(tm);
            Assert.Equal(1, tm.Counts["crease_recovered"]);
            TetMeshingStage2Tests.Invariants(tm);
            // the slot's bottom edges (y = 0.3 and 0.5, z = 0.2) are closer to the boundary than with the rounded wrap
            var rounded = new MeshTetrahedralization(slot) { RelativeAlpha = 40 }; Run(rounded);
            Func<MeshTetrahedralization, double> dist = m => {
                var tree = new DMeshAABBTree3(m.Result.ToSurfaceMesh(), true);
                double sum = 0; int n = 0;
                for (int i = 1; i < 12; ++i) foreach (double y in new[] { 0.3, 0.5 }) { tree.FindNearestTriangle(new Vector3d(0.1 * i, y, 0.2), out double d2); sum += Math.Sqrt(d2); n++; }
                return sum / n / m.UsedOffset;
            };
            Assert.True(dist(tm) < 0.5 * dist(rounded), dist(tm) + " against " + dist(rounded));
        }

        [Fact]
        public void Recover_OfARecoveredSurface_NeedsNothing()
        {
            DMesh3 s = FeaturedSurface(out MeshAlphaWrap wrap);
            double floor = 0.125 * Math.Min(wrap.UsedOffset, wrap.UsedAlpha - wrap.UsedOffset);
            var first = TetFeatureSurface.Recover(s, floor, 1000, null);
            Assert.NotNull(first);
            var again = TetFeatureSurface.Recover(first.Surface, floor, 0, null);
            Assert.NotNull(again);
            Assert.Equal(0, again.Steiner);
            Assert.Equal(0, again.MissingTrianglesBefore);
            Assert.Equal(0, again.MissingEdgesBefore);
        }
    }
}
