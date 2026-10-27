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
    /// TerrainSafetyLines.AlongSurface (band remeshed in parallel passes, heat method geodesic, iso line): the result
    /// must not depend on the frame, the run or the thread timing, and the line must keep the exact distance along
    /// the surface. A pit edge with a plateau (z = 0 for y &lt;= 40) and a 35 degree slope down, crest along y = 40,
    /// where the distance along the surface is known. Under 2^k scales the lines are the same bits only since the
    /// heat source of MeshGeodesicDistance scales with the mesh.
    /// </summary>
    public class TerrainSafetyAlongSurfaceInvarianceTests
    {
        const double D = 5;
        static readonly double Slope = 35 * MathUtil.Deg2Rad;
        const double H = D / 8;     // TerrainSafetyLines.EdgeLength default

        static double Height(double x, double y) => (y > 40) ? -(y - 40) * Math.Tan(Slope) : 0;

        // 80 x 80 m on a 1 m grid
        static DMesh3 Pit()
        {
            var m = new DMesh3();
            int[,] vid = new int[81, 81];
            for (int j = 0; j <= 80; ++j)
                for (int i = 0; i <= 80; ++i)
                    vid[i, j] = m.AppendVertex(new Vector3d(i, j, Height(i, j)));
            for (int j = 0; j < 80; ++j)
                for (int i = 0; i < 80; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return m;
        }

        static List<Vector3d> Crest() => new List<Vector3d>() { new Vector3d(0, 40, 0), new Vector3d(80, 40, 0) };

        // exact distance along the surface to the crest (the slope unfolds onto the plateau)
        static double Exact(Vector3d p) => (p.y <= 40) ? 40 - p.y : (p.y - 40) / Math.Cos(Slope);

        static TerrainSafetyLines Compute(Similarity s)
        {
            DMesh3 terrain = Invariance.Transformed(Pit(), s);
            var crest = Crest().Select(s.Apply).ToList();
            var sl = new TerrainSafetyLines(terrain, new[] { crest }, s.ApplyLength(D)) { Mode = TerrainSafetyLines.Modes.AlongSurface };
            Assert.True(sl.Compute());
            return sl;
        }

        // terrain is z-up (the band is cut by horizontal distance): a rotated frame is rotated about z only
        static Similarity ZUp(Similarity s)
        {
            return s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;
        }

        static List<List<Vector3d>> LinesBack(TerrainSafetyLines sl, Similarity s) =>
            sl.Lines.Select(l => l.Select(s.Inverse).ToList()).ToList();

        static string LinesDifference(List<List<Vector3d>> a, List<List<Vector3d>> b)
        {
            if (a.Count != b.Count) return $"{a.Count} vs {b.Count} lines";
            for (int i = 0; i < a.Count; ++i) {
                if (a[i].Count != b[i].Count) return $"line {i}: {a[i].Count} vs {b[i].Count} points";
                for (int k = 0; k < a[i].Count; ++k)
                    if (a[i][k] != b[i][k]) return $"line {i} point {k}: {a[i][k]} vs {b[i][k]}";
            }
            return "";
        }

        // the largest distance from a point of a to the polylines b
        static double MaxDistance(List<List<Vector3d>> a, List<List<Vector3d>> b)
        {
            double max = 0;
            foreach (var line in a)
                foreach (Vector3d p in line) {
                    double best = double.MaxValue;
                    foreach (var other in b)
                        for (int k = 0; k + 1 < other.Count; ++k)
                            best = Math.Min(best, Math.Sqrt(new Segment3d(other[k], other[k + 1]).DistanceSquared(p)));
                    max = Math.Max(max, best);
                }
            return max;
        }

        static double Length(List<Vector3d> l) { double s = 0; for (int i = 1; i < l.Count; ++i) s += l[i].Distance(l[i - 1]); return s; }

        // the remeshed band is one clean piece and the line (in the original frame) is safe and close to the distance
        static void AssertResult(TerrainSafetyLines sl, Similarity s)
        {
            DMesh3 band = sl.RemeshedTerrain;
            Assert.True(band.TriangleCount > 0);
            var comps = new MeshConnectedComponents(band);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
            Assert.Empty(MeshIterators.BowtieVertices(band));
            Assert.True(band.Vertices().All(v => v.IsFinite));
            double h = s.ApplyLength(H);
            int degenerate = band.TriangleIndices().Count(tid => band.GetTriArea(tid) < 1e-6 * h * h);
            Assert.True(degenerate == 0, $"{s.Name}: {degenerate} degenerate triangles in the band");
            foreach (int vid in band.VertexIndices())
                Assert.False(double.IsNaN(sl.Geodesic.Distance[vid]));
            var lines = LinesBack(sl, s);
            Assert.Equal(2, lines.Count);       // both sides of the crest
            int n = 0;
            foreach (var line in lines)
                foreach (Vector3d p in line) {
                    if (p.x < 2 * D || p.x > 80 - 2 * D) continue;      // away from the crest ends
                    double e = Exact(p);
                    Assert.True(e >= D, $"{s.Name}: {p} only {e:F3} m from the crest along the surface");
                    Assert.True(e <= D + 0.35 * H + 0.3, $"{s.Name}: {p} {e:F3} m from the crest, too far");
                    n++;
                }
            Assert.True(n > 100, $"{s.Name}: {n} line points checked");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void AlongSurface_SameResultUnderSimilarity(string name)
        {
            var reference = Compute(Similarity.Identity);
            AssertResult(reference, Similarity.Identity);
            Similarity s = ZUp(Similarity.ByName(name));
            var sl = Compute(s);
            AssertResult(sl, s);
            if (s.IsExact) {
                // scaling by 2^k is exact: the same band and lines, scaled, bit for bit
                Assert.Equal("", Invariance.Difference(reference.RemeshedTerrain, Invariance.MappedBack(sl.RemeshedTerrain, s)));
                Assert.Equal("", LinesDifference(LinesBack(reference, Similarity.Identity), LinesBack(sl, s)));
            } else {
                // rounding differs: the same band up to rounding, but a crest point equally far from two band
                // vertices may be mapped to the other one (eg at scale 1e3: 141 instead of 140 source vertices),
                // which moves the line there by up to about 0.3 h
                var back = LinesBack(sl, s);
                double l0 = reference.Lines.Sum(Length), l1 = back.Sum(Length);
                Assert.True(Math.Abs(l1 - l0) < 0.01 * l0, $"{s.Name}: line length {l1} vs {l0}");
                double dev = Math.Max(MaxDistance(back, reference.Lines), MaxDistance(reference.Lines, back));
                Assert.True(dev < 0.5 * H, $"{s.Name}: lines {dev} m apart");
            }
        }

        [Fact]
        public void AlongSurface_RepeatedAndConcurrentRuns_BitIdentical()
        {
            // the band is smoothed in parallel: the result must not depend on the thread timing (runs one after
            // another, and several at once)
            var reference = Compute(Similarity.Identity);
            var runs = new List<TerrainSafetyLines>();
            for (int k = 0; k < 2; ++k)
                runs.Add(Compute(Similarity.Identity));
            var concurrent = new TerrainSafetyLines[4];
            Parallel.For(0, concurrent.Length, k => concurrent[k] = Compute(Similarity.Identity));
            runs.AddRange(concurrent);
            foreach (var sl in runs) {
                Assert.Equal("", Invariance.Difference(reference.RemeshedTerrain, sl.RemeshedTerrain));
                Assert.Equal("", LinesDifference(reference.Lines, sl.Lines));
            }
        }

        [Fact]
        public void AlongSurface_ComputeTwiceOnOneInstance_SameResult()
        {
            var sl = Compute(Similarity.Identity);
            DMesh3 band = sl.RemeshedTerrain;
            var lines = sl.Lines.Select(l => l.ToList()).ToList();
            Assert.True(sl.Compute());
            Assert.Equal("", Invariance.Difference(band, sl.RemeshedTerrain));
            Assert.Equal("", LinesDifference(lines, sl.Lines));
            AssertResult(sl, Similarity.Identity);
        }
    }
}
