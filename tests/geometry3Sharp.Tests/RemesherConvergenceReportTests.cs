using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1578 (findings 1 and 2): a remesh that is stopped after a fixed number of passes can still be refining. With
    /// PreventNormalFlips it then holds triangles that face against the input surface (the splits of long input edges
    /// next to pinned corners, the projection and smoothing moves are not guarded) and self-intersections; they go away
    /// while the passes converge. Remesher.ModifiedEdgesLastPass (edges split, collapsed or flipped in the last pass)
    /// divided by the edge count is the convergence signal, and these tests pin that use of it, the loop of the remeshing
    /// guide: the pass loop of a box (12 triangles) refined to 4 % of its diagonal, the setup of the issue.
    /// Inverted = the normal of a triangle points against the normal of the nearest input triangle at its centroid.
    /// Measured (this box, 4 % target, PreventNormalFlips, net10.0): passes 3 to 7 hold 1 to 11 inverted triangles and
    /// self-intersections (modified edges 32 to 42 % of the edges), from pass 8 on none (34 % at pass 8, 14 % at pass 13).
    /// With ApplySafePreset the loop holds no inverted triangle at any pass and ends clean, but 8 to 10 triangles
    /// stay below the preset's MinTriangleQuality of 0.2 (it is not a floor: splits and projection moves are not guarded).
    /// </summary>
    public class RemesherConvergenceReportTests
    {
        readonly ITestOutputHelper output;
        public RemesherConvergenceReportTests(ITestOutputHelper output) { this.output = output; }

        const double TargetFraction = 0.04;
        const double ConvergedFraction = 0.15;      // modified edges / edges below this: converged (ish)
        const int MaxPasses = 30;

        static DMesh3 Box(Similarity s)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
            return Invariance.Transformed(box, s);
        }

        static Remesher Setup(DMesh3 mesh, DMesh3 input, bool safe)
        {
            var r = new Remesher(mesh);
            r.SetTargetEdgeLength(TargetFraction * input.GetBounds().DiagonalLength);
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 40);
            MeshConstraintUtil.PinSharpVertices(r, 75);
            r.PreventNormalFlips = true;
            if (safe)
                r.ApplySafePreset();
            return r;
        }

        static int InvertedTriangles(DMesh3 mesh, DMesh3 input)
        {
            var tree = new DMeshAABBTree3(input, true);
            int n = 0;
            foreach (int t in mesh.TriangleIndices()) {
                int nearest = tree.FindNearestTriangle(mesh.GetTriCentroid(t));
                if (mesh.GetTriNormal(t).Dot(input.GetTriNormal(nearest)) < 0)
                    n++;
            }
            return n;
        }

        static int TrianglesBelowQuality(DMesh3 mesh, double q)
        {
            int n = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                double e = (a - b).LengthSquared + (b - c).LengthSquared + (c - a).LengthSquared;
                if (4 * Math.Sqrt(3) * mesh.GetTriArea(t) / e < q)
                    n++;
            }
            return n;
        }

        struct PassRecord { public int Pass, Inverted, Edges, Modified; public string NotClean; public double Fraction => (double)Modified / Math.Max(1, Edges); }

        // passes until the modified fraction falls below ConvergedFraction (at most MaxPasses); the record of every pass
        List<PassRecord> RunUntilConverged(DMesh3 mesh, DMesh3 input, Remesher r)
        {
            var records = new List<PassRecord>();
            for (int i = 0; i < MaxPasses; ++i) {
                r.BasicRemeshPass();
                var rec = new PassRecord { Pass = i, Inverted = InvertedTriangles(mesh, input), Edges = mesh.EdgeCount, Modified = r.ModifiedEdgesLastPass,
                                           NotClean = MeshRobustRepair.NotCleanReason(mesh) };
                records.Add(rec);
                output.WriteLine($"pass {i}: {mesh.TriangleCount} triangles, modified {rec.Modified}/{rec.Edges} = {rec.Fraction:F2}, inverted {rec.Inverted}, not clean: '{rec.NotClean}'");
                if (rec.Fraction < ConvergedFraction)
                    break;
            }
            return records;
        }

        [Fact]
        public void PreventNormalFlips_UnconvergedPassesAreVisibleInModifiedEdgesLastPass_ConvergedHasNoInverted()
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, false);
            List<PassRecord> rec = RunUntilConverged(mesh, input, r);

            // the refinement phase: a box of 12 triangles takes several passes to reach the target, with a large share of
            // the edges modified in every pass, and the mesh holds inverted triangles on the way (the issue's 807 at 10 passes
            // of a 1 % target)
            Assert.Contains(rec, p => p.Inverted > 0);
            foreach (PassRecord p in rec.Take(5))
                Assert.True(p.Fraction > 0.25, "pass " + p.Pass + " modified " + p.Fraction);
            Assert.True(rec.Max(p => p.Inverted) > 0 && rec.First(p => p.Inverted > 0).Pass < rec.Last().Pass);

            // converged: the signal is below the limit within the pass cap, and nothing faces against the input
            PassRecord last = rec.Last();
            Assert.True(last.Fraction < ConvergedFraction, "not converged after " + rec.Count + " passes: " + last.Fraction);
            Assert.True(rec.Count < MaxPasses);
            Assert.Equal(0, last.Inverted);
            Assert.Equal("", last.NotClean);
            Assert.True(Invariance.AllFinite(mesh));
            Assert.Equal(0, r.InvertingPassesRejected);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SafePreset_ConvergedLoop_HasNoInvertedAndIsClean_UnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Box(s);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, true);
            List<PassRecord> rec = RunUntilConverged(mesh, input, r);

            PassRecord last = rec.Last();
            Assert.True(last.Fraction < ConvergedFraction, "not converged after " + rec.Count + " passes");
            Assert.Equal(0, rec.Max(p => p.Inverted));      // the safe preset (self-intersections prevented): none on the way either
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
            Assert.True(Invariance.AllFinite(mesh));
            Assert.True(mesh.IsClosed());
            int below = TrianglesBelowQuality(mesh, Remesher.SafePresetMinTriangleQuality);
            output.WriteLine($"{name}: {rec.Count} passes, {mesh.TriangleCount} triangles, below {Remesher.SafePresetMinTriangleQuality}: {below}");
        }

        [Fact]
        public void SafePreset_Pow2Scales_GiveTheSameResultBitForBit()
        {
            DMesh3 reference = null;
            foreach (Similarity s in new[] { Similarity.Identity, Similarity.Pow2Small, Similarity.Pow2Large }) {
                DMesh3 input = Box(s);
                DMesh3 mesh = new DMesh3(input);
                Remesher r = Setup(mesh, input, true);
                RunUntilConverged(mesh, input, r);
                DMesh3 back = Invariance.MappedBack(mesh, s);
                if (reference == null) { reference = back; continue; }
                Assert.Equal("", Invariance.Difference(reference, back));
            }
        }

        [Fact]
        public void ConvergedLoop_CanBeRunAgainOnTheSameInstance()
        {
            // reuse: more passes on a converged remesher stay converged and clean (the signal stays below the limit)
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, false);
            RunUntilConverged(mesh, input, r);
            List<PassRecord> again = RunUntilConverged(mesh, input, r);
            Assert.True(again.Last().Fraction < ConvergedFraction);
            Assert.Equal(0, again.Last().Inverted);
            Assert.Equal("", again.Last().NotClean);
        }
    }
}
