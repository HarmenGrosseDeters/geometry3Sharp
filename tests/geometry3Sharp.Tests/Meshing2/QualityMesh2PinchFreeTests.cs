using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The refinement on the snap grid does not pinch two chains (owner decision 2026-11-03 on Q17, strict): a split point may not be a
    /// vertex of refinement that another chain already runs through. Every pinch measured came that way: one chain's split made the
    /// vertex, another chain's split later chose it as "a vertex already there" (the star polygon fuzz: 12 pinches in 11 inputs; Herne:
    /// 11 to 217 per run). Only the library's existing API here, so that these tests run against the library before the fix.
    /// </summary>
    public class QualityMesh2PinchFreeTests
    {
        static OracleReport Judge(Mesh2Input input, Mesh2Output o) => Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });

        static int Pinches(OracleReport r) => r.Violations.Count(v => v.Kind == Violation.RefinementPinch);

        /// <summary> the inputs of the star polygon fuzz that pinched: no pinch now, and the verdict of their class (2858 drops an input
        /// point, as it did before; QualityMesh2KeepsChainsApartTests) </summary>
        [Theory]
        [InlineData(527)]
        [InlineData(533)]
        [InlineData(555)]
        [InlineData(586)]
        [InlineData(1516)]
        [InlineData(1618)]
        [InlineData(1833)]
        [InlineData(1861)]
        [InlineData(2020)]
        [InlineData(2434)]
        [InlineData(2858)]
        public void StarFuzz_FormerPinches_NoPinch(int seed)
        {
            var input = StarPolygonFuzz.Make(seed);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var r = Judge(input, o);
            Assert.Equal(0, Pinches(r));
            Assert.True(r.Ok || seed == 2858, r.ToString());
        }

        /// <summary> the same inputs in the constrained quality mode, at 20 and 25 degrees (lenses) </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void StarFuzz_ConstrainedQuality_NoPinch()
        {
            int pinches = 0;
            for (int seed = 0; seed < 3000; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                input.ConstrainedQuality = true;
                input.MinAngleDeg = seed % 2 == 0 ? 20 : 25;
                var o = new QualityMesh2Candidate().Run(input);
                if (o.Status != Mesh2Status.Success) continue;
                pinches += Pinches(Judge(input, o));
            }
            Assert.Equal(0, pinches);
        }

        /// <summary>
        /// random segments in a square on the grid: refused split points leave more subsegments unsplittable, and the oracle, which
        /// recomputes from the specification which subsegments can be split, accepts the meshes (a subsegment whose only split pixels
        /// hold another chain's vertex of refinement is not demanded conforming Delaunay; these seeds were rejected without that)
        /// </summary>
        [Theory]
        [InlineData(111, 20.0)]
        [InlineData(429, 20.0)]
        [InlineData(1471, 20.0)]
        [InlineData(271, 30.0)]
        public void SegmentFuzz_UnsplittableByThePinchRule_Accepted(int seed, double angle)
        {
            var input = Segments(seed, angle);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var r = Judge(input, o);
            Assert.True(r.Ok, r.ToString());
            Assert.Equal(0, Pinches(r));
        }

        internal static Mesh2Input Segments(int seed, double angle)
        {
            var r0 = new Random(seed);
            int n = 6 + r0.Next(14);
            var x = new List<double> { 0, 24, 24, 0 }; var y = new List<double> { 0, 0, 24, 24 };
            var input = new Mesh2Input { SnapTolerance = new[] { 1.0, 0.5, 0.3 }[seed % 3], MinAngleDeg = angle, SteinerLimit = 20000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            for (int i = 0; i < n; ++i) {
                x.Add(1 + r0.NextDouble() * 22); y.Add(1 + r0.NextDouble() * 22); x.Add(1 + r0.NextDouble() * 22); y.Add(1 + r0.NextDouble() * 22);
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        static Mesh2Input Moved(Mesh2Input plain, Similarity s)
        {
            var moved = new Mesh2Input { X = (double[])plain.X.Clone(), Y = (double[])plain.Y.Clone(), SnapTolerance = plain.SnapTolerance * s.Scale,
                MinAngleDeg = plain.MinAngleDeg, SteinerLimit = plain.SteinerLimit };
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            moved.Rings.AddRange(plain.Rings);
            moved.Segments.AddRange(plain.Segments);
            return moved;
        }

        /// <summary> invariance: seed 527 has no pinch in any frame (its free point outside the ring meshes outside the domain in some
        /// frames, a class of its own); at the 2^k scales the mesh is the same bit for bit, and accepted </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Seed527_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var plain = StarPolygonFuzz.Make(527);
            var moved = Moved(plain, s);
            var o = new QualityMesh2Candidate().Run(moved);
            Assert.Equal(0, Pinches(Judge(moved, o)));
            if (!s.IsExact) return;
            var a = new QualityMesh2Candidate().Run(plain);
            Assert.Equal(a.VX.Select(x => x * s.Scale), o.VX);
            Assert.Equal(a.VY.Select(y => y * s.Scale), o.VY);
            Assert.Equal(a.Triangles.SelectMany(t => t), o.Triangles.SelectMany(t => t));
            Assert.True(Judge(moved, o).Ok, name);
        }

        /// <summary> reuse: one mesher instance twice on seed 527 gives the same mesh </summary>
        [Fact]
        public void Reuse_SameInstance()
        {
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 1 };
            var input = ConstrainedDelaunayMesh2Candidate.Convert(StarPolygonFuzz.Make(527));
            var a = mesher.Compute(input);
            var b = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, a.Status);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
        }
    }
}
