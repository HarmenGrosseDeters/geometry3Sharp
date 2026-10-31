using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Meshing2Acceptance
{
    public enum CaseState
    {
        /// <summary>Runs now against a candidate through the oracle, or through the named test.</summary>
        Covered,
        /// <summary>The fixture exists and its input is checked; the feature does not exist yet. Fails loudly once it does.</summary>
        Pending,
        /// <summary>Out of the first version by a decision of the owner (MESHING2D_SPEC section 5).</summary>
        Deferred,
        /// <summary>Cannot run here, for the stated reason.</summary>
        Blocked
    }

    public sealed class CatalogueRow
    {
        public string F, Part, Title, Feature, Note, Test;
        public CaseState State;
        public Mesh2Case Case;
        public string Id => Part == null ? F : F + "/" + Part;
        public override string ToString() => Id;
    }

    /// <summary>The failure catalogue of MESHING2D_SPEC section 2, one state per row. Inputs are integer or dyadic, without trigonometry.</summary>
    public static class Catalogue
    {
        static readonly double E20 = Math.Pow(2, -20), E27 = Math.Pow(2, -27), E33 = Math.Pow(2, -33), E40 = Math.Pow(2, -40), E43 = Math.Pow(2, -43), E46 = Math.Pow(2, -46), E50 = Math.Pow(2, -50);

        public static List<CatalogueRow> Rows()
        {
            var rows = new List<CatalogueRow>();

            void Mesh(string f, string part, string title, string feature, Milestone m, B input, Q? area = null, Q? tolerance = null, Action<OracleExpect> tweak = null, string note = null, bool ties = false)
            {
                var e = new OracleExpect { Milestone = m, StatedArea = area, StatedAreaTolerance = tolerance ?? Q.Zero };
                tweak?.Invoke(e);
                var c = new Mesh2Case { Id = f.ToLowerInvariant() + (part == null ? "" : "/" + part), Generator = input.Text, Milestone = m, Input = input.Build(), Expect = e, Ties = ties };
                rows.Add(new CatalogueRow { F = f, Part = part, Title = title, Feature = feature, State = CaseState.Pending, Case = c, Note = note });
            }
            void Test(string f, string part, string title, string test, string note = null) =>
                rows.Add(new CatalogueRow { F = f, Part = part, Title = title, State = CaseState.Covered, Feature = Feature.Kernel, Test = test, Note = note });
            void M1(string f, string part, string title, string caseId, string note = null)
            {
                var c = M1Cases.Ci().First(x => x.Id == caseId);
                rows.Add(new CatalogueRow { F = f, Part = part, Title = title, State = CaseState.Covered, Feature = Feature.Kernel, Case = c, Note = note });
            }
            void Deferred(string f, string part, string title, string reason) =>
                rows.Add(new CatalogueRow { F = f, Part = part, Title = title, State = CaseState.Deferred, Note = reason });
            void Blocked(string f, string part, string title, string reason) =>
                rows.Add(new CatalogueRow { F = f, Part = part, Title = title, State = CaseState.Blocked, Note = reason });

            const Milestone M2 = Milestone.M2, M3 = Milestone.M3;
            Q Hundred = 100;
            Func<B> S = () => new B().Square();

            // 2.1 crossing and touching segments
            Mesh("F-1", "a", "Three crossing segments", Feature.Noding, M2, S().Segs(2, 4, 4, 8, 1, 5, 6, 6, 1, 7, 5, 3), Hundred);
            Mesh("F-1", "b", "Three crossing segments", Feature.Noding, M2, S().Segs(2, 5, 9, 5, 6, 3, 10, 10, 8, 7, 5, 0), Hundred);
            Mesh("F-1", "c", "Three crossing segments", Feature.Noding, M2, S().Segs(5, 0, 1, 10, 7, 7, 0, 7, 3, 3, 8, 8), Hundred);
            Mesh("F-1", "random", "Random crossing segments, one seed of many", Feature.Noding, M2, S().RandomSegments(10, 15, 1), Hundred);
            Mesh("F-2", null, "Five crossing segments, formerly a stack overflow", Feature.Noding, M2, S().Segs(9, 3, 2, 0, 7, 2, 1, 8, 7, 3, 3, 6, 10, 6, 9, 1, 10, 10, 8, 2), Hundred);
            Mesh("F-3", "a", "Crossing segments, exception", Feature.Noding, M2, S().Segs(9, 5, 5, 7, 7, 7, 4, 1, 6, 5, 3, 10), Hundred);
            double nearX = 4096.25, nearY = 1024.5;
            Mesh("F-3", "b", "Near-duplicate point (14th digit) on a segment, hull domain", Feature.Noding, M2,
                new B().Seg(nearX, nearY, 8192, 2048).Point(nearX + 2 * Math.Pow(2, -40), nearY).Seg(0, 512, 8192, 2048 + 512), note: "dyadic analog of the spec's coordinates");
            Mesh("F-3", "c", "The same at 20 degrees", Feature.Refine, M3,
                new B().Seg(nearX, nearY, 8192, 2048).Point(nearX + 2 * Math.Pow(2, -40), nearY).Seg(0, 512, 8192, 2048 + 512).Min(20).Limit(20000));
            Mesh("F-4", "a", "40 random segments at 25 degrees", Feature.Refine, M3, S().RandomSegments(10, 40, 4).Min(25), Hundred, note: "the spec runs 20 seeds; the benchmark sweeps them");
            Mesh("F-4", "b", "100 segments in a 20 x 20 square at 20 degrees", Feature.Refine, M3, new B().Square(20).RandomSegments(20, 100, 5).Min(20), 400);
            Mesh("F-4", "c", "200 segments on a 50-grid at 20 degrees", Feature.Refine, M3, new B().Square(50).RandomSegments(50, 200, 6).Min(20), 2500);
            Mesh("F-5", null, "40 random segments at 30 degrees", Feature.Refine, M3, S().RandomSegments(10, 40, 172).Min(30), Hundred, note: "the spec's seeds belong to another generator: this seed does not reproduce them");
            Mesh("F-6", "a", "Segment through input points", Feature.Cdt, M2, new B().Square(4).Point(1, 1).Point(2, 2).Seg(0.5, 0.5, 3.5, 3.5), 16);
            foreach (var (name, eps) in new[] { ("b", E27), ("c", E40), ("d", E50) })
                Mesh("F-6", name, "Two diagonals and a point just off them, 20 degrees", Feature.Refine, M3, new B().Square(4).Segs(0, 0, 4, 4, 0, 4, 4, 0).Point(2, 2 + eps).Min(20), 16, note: "epsilon is a power of two here");
            Mesh("F-7", "a", "Overlapping collinear segments", Feature.Noding, M2, S().Segs(1, 5, 6, 5, 4, 5, 9, 5), Hundred);
            Mesh("F-7", "b", "A contour running back along itself", Feature.Noding, M2, S().Polyline(10, 10, 5, 10, 8, 10, 0, 10), Hundred);
            Mesh("F-7", "c", "A spike", Feature.Noding, M2, new B().Polyline(10, 5, 15, 5, 10, 5).Rect(0, 0, 20, 10), 200);
            Mesh("F-7", "d", "Duplicate and reversed duplicate segments", Feature.Cdt, M2, S().Segs(2, 2, 8, 8, 2, 2, 8, 8, 8, 8, 2, 2), Hundred);
            Mesh("F-7", "e", "Bowtie contour", Feature.Noding, M2, new B().Outer(0, 0, 1, 1, 1, 0, 0, 1), new Q(1, 2));
            Mesh("F-7", "f", "All of F-7 at 20 degrees", Feature.Refine, M3, S().Segs(1, 5, 6, 5, 4, 5, 9, 5).Polyline(10, 10, 5, 10, 8, 10, 0, 10).Min(20), Hundred);
            Mesh("F-8", null, "Zero-length segments mixed with crossings", Feature.Noding, M2, S().Segs(3, 3, 3, 3, 5, 5, 5, 5, 0, 0, 10, 10, 0, 10, 10, 0, 1, 1, 1 + Math.Pow(2, -46), 1), Hundred);
            Mesh("F-9", null, "Noding off must fail with a status", Feature.Noding, M2, S().Segs(2, 4, 4, 8, 1, 5, 6, 6, 1, 7, 5, 3).NoNoding(), tweak: e => e.ExpectFailure = true);

            // 2.2 near-coincident geometry
            foreach (var (name, eps) in new[] { ("a", E20), ("b", E33), ("c", E46) })
                Mesh("F-10", name, "Vertex nearly on a segment, 20 degrees", Feature.Snap, M3, S().Seg(1, 5, 9, 5).Point(5, 5 + eps).Min(20), Hundred, new Q(1, 1000000));
            foreach (var (name, eps) in new[] { ("a", E20), ("b", E33), ("c", E43), ("d", E50) })
                Mesh("F-11", name, "Nearly duplicate vertices, 20 degrees", Feature.Snap, M3, new B().Square(1).Point(0.5, 0.5).Point(0.5 + eps, 0.5).Point(0.5, 0.5 + eps).Min(20), 1);
            Mesh("F-12", "a", "Double-digitised border, 2^-40", Feature.Snap, M3, Border(E40).Min(20).Limit(20000), 200, new Q(1, 1000), note: "the wavy border is a fixed table, not sin(i)");
            Mesh("F-12", "b", "Double-digitised border, 2^-20: the limit stops it", Feature.Snap, M3, Border(E20).Min(20).Limit(5000), 200, new Q(1, 1000));
            Mesh("F-13", null, "Micro-rings with coordinate noise", Feature.Snap, M3, MicroRings().Min(20).Limit(20000), new Q(1, 1 << 10), new Q(1, 1 << 20));
            foreach (var (name, d) in new[] { ("a", Math.Pow(2, -33)), ("b", Math.Pow(2, -27)), ("c", E20), ("d", Math.Pow(2, -17)), ("e", Math.Pow(2, -13)) })
                Mesh("F-14", name, "40 stacked nearly identical squares", Feature.Snap, M2, Stack(d), Hundred, new Q(1, 1000000000));
            Mesh("F-15", null, "Snap cycle, tolerance 11/1024", Feature.Snap, M2, new B().Square(2).Seg(1, 1, 1 + 10.0 / 1024, 1 + 19.0 / 1024).Seg(1, 1, 1, 1 + 20.0 / 1024).Snap(11.0 / 1024), 4, new Q(1, 16), note: "shifted from the origin into a frame of area 4; dyadic analog");
            Mesh("F-16", null, "Near-collinear pair with a third segment close to the shared end", Feature.Snap, M3, F16().Snap(1.0 / 128).Min(20).Limit(100000), tweak: e => e.MaxVertices = 2000, note: "dyadic analog at UTM scale");
            Mesh("F-17", null, "Snap tolerance larger than a hole", Feature.Snap, M2, S().Hole(5, 5, 5 + E20, 5, 5 + E20, 5 + E20, 5, 5 + E20).Snap(Math.Pow(2, -10)), Hundred, new Q(1, 16));
            double ox = 370000, oy = 5710000;
            Mesh("F-18", "a", "Building wall 4 mm along a parcel border, tolerance merges it", Feature.Snap, M3, new B().Rect(ox, oy, ox + 32, oy + 20).Hole(ox + 8, oy + 1.0 / 256, ox + 18, oy + 1.0 / 256, ox + 18, oy + 8, ox + 8, oy + 8).Snap(1.0 / 128).Min(20).Limit(50000), note: "dyadic analog: 2^-8 instead of 5 mm");
            Mesh("F-18", "b", "Wall 62 mm away is kept", Feature.Snap, M3, new B().Rect(ox, oy, ox + 32, oy + 20).Hole(ox + 8, oy + 1.0 / 16, ox + 18, oy + 1.0 / 16, ox + 18, oy + 8, ox + 8, oy + 8).Snap(1.0 / 128).Min(20).Limit(50000));
            Mesh("F-19", null, "Thin wedges, wedge tolerance", Feature.Wedge, M3, new B().Rect(-1, -1, 21, 6).Seg(0, 0, 20, 0).Polyline(5, 0, 15, 1.0 / 32, 15, 5).Snap(1.0 / 128).Min(30), note: "O13: beyond this brief; the fixture is the first of the spec's five");
            Mesh("F-20", null, "Long edges cross a dense polyline at tiny angles, procedural analog", Feature.Snap, M2, Zigzag(), tweak: e => e.MaxVertices = 20000, note: "the public input CDT issue-211 is not fetched here; this is an analog built from its description");
            Mesh("F-21", null, "Long parallel stripes: scaling check", Feature.Noding, M2, Stripes(64), note: "the time target needs 20 000 stripes, run by the benchmark, not in the oracle's quadratic segment check");
            Mesh("F-22", "a", "Back-and-forth border, no snapping: limit stops it", Feature.Refine, M3, Backforth().Min(20).Limit(20000));
            Mesh("F-22", "b", "The same with tolerance 2^-29: sliver gone", Feature.Snap, M3, Backforth().Snap(Math.Pow(2, -29)).Min(20).Limit(20000));

            // 2.3 quality refinement
            foreach (var deg in new[] { 0.0, 20, 30 })
                Mesh("F-23", "angle-" + Text(deg), "Max area 1/2, min angle " + Text(deg), Feature.Refine, M3, S().Area(0.5).Min(deg), Hundred);
            Mesh("F-24", "a", "Region area limits on two sides of a diagonal", Feature.Regions, M3, S().Seg(0, 0, 10, 10).RegionSeed(7, 2, 1, 0.125).RegionSeed(2, 7, 2, 2).Min(25).Limit(100000), Hundred, tweak: e => e.MaxVertices = 5000);
            Mesh("F-24", "b", "Inner square as a second region", Feature.Regions, M3, S().RegionRing(2, 0.5, 2, 2, 5, 2, 5, 5, 2, 5).RegionSeed(1, 1, 1, 0.0625).Min(25).Limit(100000), Hundred);
            Mesh("F-25", "a", "No segment splitting: rectangle 10 x 20", Feature.Locks, M3, Locked(10, 20).Area(1.75), 200, tweak: e => e.MinTriangles = 115);
            Mesh("F-25", "b", "No segment splitting: rectangle 10 x 30", Feature.Locks, M3, Locked(10, 30).Area(1.75), 300, tweak: e => e.MinTriangles = 172);
            Mesh("F-26", null, "Precision limit at needle regions", Feature.Refine, M3, Needles().Min(20).Limit(10000), tweak: e => e.AngleGuaranteed = false);
            Mesh("F-27", null, "Needle refinement scaling (performance row)", Feature.Refine, M3, Needles().Min(20).Limit(60000), tweak: e => e.AngleGuaranteed = false, note: "time target: over 50 000 vertices in about 2 s; the time is measured by the benchmark");
            Deferred("F-28", null, "A max angle alone is ignored", "Q3: no maximum angle bound in scope");
            Deferred("F-29", null, "Refining again with a max angle crashes", "Q3: no maximum angle bound in scope");
            Mesh("F-30", "a", "Invalid minimum angle next to a max area", Feature.Refine, M3, S().Area(0.5).Min(70), Hundred, tweak: e => e.InvalidAngle = true, note: "O3: the invalid value must not disable the area limit. Reject or ignore it: decision D12");
            Deferred("F-30", "b", "Invalid maximum angle", "Q3: no maximum angle bound in scope");
            Mesh("F-31", "a", "Min angle and area with locked outer boundary", Feature.Locks, M3, new B().Outer(0, 0, 16, 1, 19, 9, 13, 17, 2, 15).RandomPoints(10, 8, 31).Area(4).Min(20));
            Deferred("F-31", "b", "The same with a max angle", "Q3: no maximum angle bound in scope");
            Mesh("F-32", null, "Extreme edge lengths in refinement", Feature.Refine, M3, new B().Outer(0, 0, Math.Pow(2, 200), 0, Math.Pow(2, 200), Math.Pow(2, 200), 0, Math.Pow(2, 200)).Point(1, 1).Point(1 + Math.Pow(2, -500), 1).Min(20).Limit(10000), tweak: e => e.AngleGuaranteed = false);
            Mesh("F-33", null, "Per-triangle area limits on a point-set mesh", Feature.RefineAgain, M3, new B().RandomPoints(16, 12, 33), note: "I8. The input to refine again is the mesh of this point set; session 2 adds the area map to the fixture when the API exists");
            Mesh("F-34", null, "Area-only refinement (performance row)", Feature.Refine, M3, new B().Square(1).Area(1.0 / 1024), 1, note: "time target about 14 ms; the time is measured by the benchmark");
            Mesh("F-35", null, "Refining a point-set mesh keeps the hull", Feature.Refine, M3, new B().RandomPoints(100, 40, 35).Min(20).Limit(20000));
            Mesh("F-36", null, "Crossing locked breaklines: no vertex outside", Feature.Locks, M3, new B().Square(96).RandomPoints(96, 30, 36).RandomSegments(96, 5, 37, locked: true, marker: 7).Min(20), 9216);
            Mesh("F-37", null, "Parallel breaklines 2^-13 apart must terminate", Feature.Refine, M3, new B().Square(32).Segs(4, 8, 28, 8, 4, 8 + Math.Pow(2, -13), 28, 8 + Math.Pow(2, -13)).Min(20).Limit(50000), 1024);
            foreach (var (name, t) in new[] { ("a", 1.0 / 64), ("b", 1.0 / 512), ("c", 1.0 / 4096) })
                Mesh("F-38", name, "Triangle with a small input angle, 30 degrees", Feature.Refine, M3, new B().Outer(0, 0, 1, 0, 1, t).Min(30), tweak: e => e.AngleGuaranteed = false, note: "the spec asks for termination and validity here");
            Mesh("F-38", "d", "Fan of 20 segments at 1/1024 rad", Feature.Refine, M3, Fan().Min(25), tweak: e => e.AngleGuaranteed = false);
            Mesh("F-38", "e", "Star spikes down to 2^-50 rad", Feature.Refine, M3, Star().Min(20).Limit(20000), tweak: e => e.AngleGuaranteed = false);
            Mesh("F-39", null, "High-degree input vertex", Feature.Refine, M3, Hub(1105).Min(25).Area(1), note: "1105 gives 108 lattice points; 32045 gives 324 for the spec's size");
            foreach (var deg in new[] { 33.75, 34.5, 40 })
                Mesh("F-40", "angle-" + Text(deg), "High minimum angle", Feature.Refine, M3, new B().Square(16).RandomPoints(16, 30, 40).Min(deg).Limit(200000), 256, tweak: e => e.AngleGuaranteed = deg < 38);
            Mesh("F-41", "a", "Thin rectangle, 20 degrees", Feature.Refine, M3, new B().Rect(0, 0, 128, 1.0 / 128).Min(20), 1);
            Deferred("F-41", "b", "The same with a max angle", "Q3: no maximum angle bound in scope");
            Mesh("F-42", null, "Bounded memory in refinement", Feature.Refine, M3, new B().Square(1024).RandomSegments(1024, 64, 42).Min(30).Limit(100000), note: "bounded by the limit; memory is measured by the benchmark");

            // 2.4 holes and regions
            Mesh("F-43", "a", "Touching holes, 25 degrees", Feature.Domain, M3, S().Hole(2, 2, 4, 2, 4, 4, 2, 4).Hole(4, 2, 6, 2, 6, 4, 4, 4).Min(25), 92);
            Mesh("F-43", "b", "A hole on the outer boundary with a seed", Feature.Domain, M2, S().Hole(0, 4, 3, 4, 3, 6, 0, 6).HoleSeed(1, 5), 94);
            Mesh("F-43", "c", "A hole with its seed on its own boundary", Feature.Domain, M2, S().Hole(2, 2, 4, 2, 4, 4, 2, 4).HoleSeed(2, 3), 96, note: "ring semantics (Q7): the ring removes it, the seed changes nothing");
            Mesh("F-43", "d", "A thin hole", Feature.Domain, M2, S().Hole(2, 5, 8, 5, 8, 5 + Math.Pow(2, -17), 2, 5 + Math.Pow(2, -17)), 100 - 6 * new Q(1, 1 << 17));
            Mesh("F-44", null, "Hole in a hole, seeds outside and inside holes", Feature.Regions, M3, S().Hole(2, 2, 8, 2, 8, 8, 2, 8).HoleSeed(3, 3).Hole(4, 4, 6, 4, 6, 6, 4, 6).HoleSeed(5, 5).HoleSeed(20, 20).RegionSeed(5, 5, 9).RegionSeed(1, 1, 5).Min(20), 64);
            Mesh("F-45", null, "Two seeds in one region: the last wins", Feature.Regions, M2, S().RegionSeed(1, 1, 3).RegionSeed(2, 2, 4), Hundred);
            Mesh("F-46", "a", "A segment inside a hole does not stop the removal (Q7)", Feature.Domain, M2, S().Hole(2, 2, 8, 2, 8, 8, 2, 8).Seg(2, 5, 8, 5).Seg(5, 3, 5, 7), 64, tweak: e => { e.Removed.Add(new[] { 3.0, 3.0 }); e.Removed.Add(new[] { 7.0, 7.0 }); });
            Mesh("F-46", "b", "A crossing breakline opens no leak", Feature.Domain, M2, S().Hole(2, 2, 8, 2, 8, 8, 2, 8).Seg(0, 5, 10, 5), 64, tweak: e => { e.Kept.Add(new[] { 1.0, 1.0 }); e.Removed.Add(new[] { 3.0, 3.0 }); });
            Mesh("F-47", "a", "Concentric circles, inner a hole", Feature.Domain, M2, Circles(true));
            Mesh("F-47", "b", "Concentric circles, inner a constraint", Feature.Cdt, M2, Circles(false));
            Mesh("F-48", "a", "Degenerate hole ring, three collinear points", Feature.Domain, M3, S().Hole(2, 2, 5, 5, 8, 8).Min(25), Hundred, tweak: e => e.Kept.Add(new[] { 4.0, 4.5 }));
            Mesh("F-48", "b", "Degenerate hole ring, back and forth", Feature.Domain, M3, S().Hole(2, 2, 8, 8, 2, 2).Min(25), Hundred);
            Mesh("F-48", "c", "Degenerate hole ring, folded", Feature.Domain, M3, S().Hole(2, 2, 8, 8, 5, 5, 2, 2).Min(25), Hundred);
            Mesh("F-49", null, "Triangular hole at UTM scale", Feature.Domain, M2, new B().Rect(500000, 5700000, 500064, 5700064).Hole(500010.25, 5700011.5, 500020.5, 5700011.75, 500015.125, 5700030.5), note: "with ring semantics there is no seed to misplace; the helper of I4 is F-50");
            Blocked("F-50", null, "Interior point of thin or small contours", "needs the API of the helper of I4 (decision D10); session 2 writes the fixture with it. Feature " + Feature.RingInterior);

            // 2.5 exact arithmetic
            M1("F-51", "a", "Tiny coordinates", "m1/tiny-64");
            M1("F-51", "b", "Huge coordinates", "m1/huge-64");
            M1("F-51", "c", "Mixed magnitudes", "m1/mixed-48");
            M1("F-51", "d", "Subnormal coordinates", "m1/subnormal-32");
            Test("F-51", "e", "Signs of the production predicates against the oracle, 1e-300 to 1e60", "Meshing2PredicateTests.PredicateSigns_MatchTheOracle");
            Test("F-52", "a", "Non-finite and out-of-range coordinates are argument errors", "Meshing2CandidateTests.InvalidCoordinates_AreArgumentErrors");
            M1("F-52", "b", "Nearly collinear points", "m1/near-collinear-64");
            Blocked("F-53", null, "1 000 published near-degenerate cases each", "public fixtures are not committed and not fetched here (CLAUDE.md, licence check open); PredicateDifferentialTests and F-51/e and F-54 cover the same kind of input");
            Test("F-54", null, "Near-cocircular signs at UTM scale and near zero", "Meshing2PredicateTests.NearCocircular_MatchesTheOracle", "20 000 cases, not the spec's 200 000, to keep the suite short");
            Mesh("F-55", "a", "Millimetre features at 2^17", Feature.Refine, M3, Offset(1 << 17), Hundred);
            Mesh("F-55", "b", "Millimetre features at 2^23", Feature.Refine, M3, Offset(1 << 23), Hundred);
            Mesh("F-55", "c", "Millimetre features at 2^30", Feature.Refine, M3, Offset(1 << 30), Hundred);
            M1("F-56", "a", "Exact Delaunay on a circle", "m1/lattice-circle-1105", "lattice points instead of random points: exactly cocircular");
            M1("F-56", "b", "The same, one ulp off", "m1/near-cocircular");
            M1("F-57", null, "Identical points", "m1/identical-5");

            // 2.6 concurrency, identity, reuse
            Test("F-58", "a", "Parallel runs equal sequential runs (point sets)", "Meshing2CandidateTests.ParallelRuns_EqualSerial");
            Test("F-58", "b", "4 threads of near-cocircular predicates", "Meshing2PredicateTests.Predicates_InParallel_MatchTheOracle");
            Mesh("F-58", "c", "200 parallel quality runs", Feature.Refine, M3, new B().RandomPoints(40, 300, 58).Rect(0, 0, 40, 40).Min(25).Limit(100000), note: "the test harness runs it in parallel when the feature exists");
            Mesh("F-59", "a", "Crossings create vertices, input ids stay", Feature.Noding, M2, S().Segs(0, 0, 10, 10, 0, 10, 10, 0, 2, 0, 2, 10), Hundred, note: "Q8: input i stays vertex i, new vertices at n or above. Caller ids: decision D2");
            M1("F-59", "b", "Duplicate inputs map to one vertex", "m1/duplicates-signed-zero");
            Test("F-60", null, "Results are independent of each other and of the input array", "Meshing2CandidateTests.Results_AreIndependentOfTheInput", "the API has no vertex objects to share, so only this remains of the case");
            M1("F-61", null, "+0 and -0 are one point", "m1/duplicates-signed-zero");
            Mesh("F-62", null, "Point location on interior edges", Feature.Api, Milestone.M1, new B().RandomPoints(32, 40, 62), note: "the oracle probes vertices, edge midpoints and centroids through the location of the result");
            Deferred("F-63", null, "Terrain simplification bound", "Q5: it belongs to TerrainGreedyTIN (roadmap C2(D))");
            Deferred("F-64", "a", "Smoothing a finished mesh", "Q4: O16 is out of the first version");
            Deferred("F-64", "b", "Bounded Voronoi after smoothing", "Q1: Voronoi output is out of the first version");
            Mesh("F-64", "c", "Steiner point smoothing stays valid at 2^20", Feature.Steiner, M3, new B().Rect(1 << 20, 1 << 20, (1 << 20) + 10, (1 << 20) + 10).Seg((1 << 20) + 1, (1 << 20) + 1, (1 << 20) + 9, (1 << 20) + 1 + Math.Pow(2, -20)).Min(25), note: "O15: beyond this brief");

            // 2.7, 2.8
            Mesh("F-65", null, "Grid: rounding flips nothing", Feature.Grid, M3, Pairs().Min(30), Hundred, new Q(1, 16), tweak: e => e.MayMoveVertices = true, note: "O12: beyond this brief");
            Blocked("F-66", null, "Public polygon fixtures (earcut, delaunator, poly2tri)", "no mesh files in the repository; needs a fetch script pinned by URL and hash and a licence check per fixture. GOLDEN_FORMAT section 2 has the slot for them");

            return rows;
        }

        static string Text(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The cases that docs/design/MESHING2D_GOLDEN_FORMAT.md lists, so that another producer can rebuild them without any of our code.</summary>
        public static IEnumerable<Mesh2Case> ReferenceCases()
        {
            string[] ids = {
                "f-1/a", "f-1/b", "f-1/c", "f-1/random", "f-2", "f-3/a", "f-6/a", "f-7/a", "f-7/d", "f-7/e", "f-43/b", "f-43/c", "f-44", "f-46/a", "f-46/b", "f-48/a", "f-48/b", "f-48/c",
                "f-23/angle-0", "f-23/angle-20", "f-23/angle-30", "f-34", "f-38/a", "f-38/b", "f-40/angle-33.75", "f-41/a"
            };
            var rows = Rows();
            foreach (var c in M1Cases.Ci()) yield return c;
            foreach (var id in ids) yield return rows.First(r => r.Case != null && r.Case.Id == id).Case;
        }

        // ---- procedural inputs too long to write inline

        static readonly double[] Wave = { 0, 0.875, 0.875, 0.125, -0.75, -1, -0.25, 0.625, 1, 0.375, -0.5 }; // the table replaces sin(i), i = 0..10

        static B Border(double eps)
        {
            var b = new B().Rect(0, 0, 20, 10);
            var random = new SplitMix64(12);
            for (int i = 0; i < 10; ++i) {
                b.Seg(10 + Wave[i], i, 10 + Wave[i + 1], i + 1);
                double a = (random.Bits(8) - 128) * eps / 256, c = (random.Bits(8) - 128) * eps / 256;
                b.Seg(10 + Wave[i] + a, i, 10 + Wave[i + 1] + c, i + 1);
            }
            return b;
        }

        static B MicroRings()
        {
            double cx = -90, cy = 40, u = Math.Pow(2, -43), frame = Math.Pow(2, -5);
            var b = new B().Rect(cx, cy, cx + frame, cy + frame);
            var random = new SplitMix64(13);
            for (int k = 1; k <= 5; ++k) {
                double px = cx + k * Math.Pow(2, -9) / 2, py = cy + k * Math.Pow(2, -10);
                var ring = new List<double>();
                for (int i = 0; i < 5; ++i) { ring.Add(px + random.Bits(4) * u); ring.Add(py + random.Bits(4) * u); }
                b.Polyline(ring.ToArray());
            }
            return b;
        }

        static B Stack(double d)
        {
            var b = new B().Rect(595, 595, 605, 605);
            double grid = Math.Pow(2, -34);
            double R(double v) => Math.Round(v / grid) * grid;
            for (int k = 0; k < 40; ++k) {
                double t = 0.4228 + 0.05 * k * d, c = (1 - t * t) / (1 + t * t), s = 2 * t / (1 + t * t);
                double cx = 600 + k * d, cy = 600 + k * d;
                var ring = new List<double>();
                foreach (var (x, y) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0) }) { ring.Add(R(cx + c * x - s * y)); ring.Add(R(cy + s * x + c * y)); }
                b.Polyline(ring.ToArray());
            }
            return b;
        }

        static B F16()
        {
            double sx = 380000, sy = 5700000;
            return new B().Rect(sx - 8, sy - 8, sx + 16, sy + 16)
                .Seg(sx, sy, sx + 4, sy + 3)
                .Seg(sx, sy, sx + 8, sy + 6 + Math.Pow(2, -15))
                .Seg(sx - 1.0 / 1024, sy + 1, sx + 1, sy - 1.0 / 1024);
        }

        static B Zigzag()
        {
            var b = new B().Rect(-1, -2, 257, 2);
            var line = new List<double>();
            for (int i = 0; i <= 256; ++i) { line.Add(i); line.Add((i % 2 == 0 ? 1 : -1) * Math.Pow(2, -8)); }
            b.Polyline(line.ToArray());
            b.Seg(0, -1.0 / 128, 256, 1.0 / 128).Seg(0, 1.0 / 128, 256, -1.0 / 128);
            return b;
        }

        static B Stripes(int n)
        {
            var b = new B().Rect(-1, -1, n + 2 * 1024, n + 1);
            for (int i = 0; i < n; ++i) b.Seg(i, i, i + 2048, i);
            return b;
        }

        static B Backforth()
        {
            double ax = 7, ay = 51, bx = 7 + 1.0 / 32, by = 51 + 1.0 / 32;
            return new B().Rect(6.75, 50.75, 7.25, 51.25).Seg(ax, ay, bx, by).Seg(bx, by, ax + 1.0 / 2048, ay + 1.0 / 2048 + Math.Pow(2, -31));
        }

        static B Locked(double w, double h)
        {
            var b = new B();
            b.Seg(0, 0, w, 0, 1, true).Seg(w, 0, w, h, 1, true).Seg(w, h, 0, h, 1, true).Seg(0, h, 0, 0, 1, true);
            return b;
        }

        static B Needles() => new B().Rect(-100000, -100000, 1100000, 1100000)
            .Polyline(101, 99, 101, 1000000, 102, 1000000, 101, 99)
            .Polyline(100, 100, 1000000, 110, 1000000, 100, 100, 100);

        static B Fan()
        {
            var b = new B().Rect(-1, -1, 2048, 256);
            for (int i = 0; i < 20; ++i) b.Seg(0, 0, 1024, i);
            return b;
        }

        static B Star()
        {
            var b = new B().Rect(-2, -2, 3, 3);
            for (int k = 10; k <= 50; k += 10) {
                double t = Math.Pow(2, -k);
                b.Polyline(0, k / 16.0, 1, k / 16.0 + t, 1, k / 16.0 - t, 0, k / 16.0);
            }
            return b;
        }

        static IEnumerable<(double x, double y)> Lattice(int r)
        {
            var c = Generators.LatticeCircle(r, false);
            // in angular order: by quadrant walk over the sorted points
            var upper = Enumerable.Range(0, c.Count).Where(i => c.Y[i] >= 0).OrderBy(i => -c.X[i]).Select(i => (c.X[i], c.Y[i]));
            var lower = Enumerable.Range(0, c.Count).Where(i => c.Y[i] < 0).OrderBy(i => c.X[i]).Select(i => (c.X[i], c.Y[i]));
            return upper.Concat(lower);
        }

        static B Hub(int r)
        {
            double scale = 10.0 / 1024; // radius about 10.8; a power of two would do, this one keeps the spec's size
            scale = Math.Pow(2, -7);
            var b = new B();
            var ring = Lattice(r).SelectMany(p => new[] { p.x * scale, p.y * scale }).ToArray();
            b.Outer(ring);
            foreach (var (x, y) in Lattice(r)) b.Seg(0, 0, x * scale, y * scale);
            return b;
        }

        static B Circles(bool hole)
        {
            var b = new B().Outer(Lattice(1105).SelectMany(p => new[] { p.x, p.y }).ToArray());
            var inner = Lattice(325).SelectMany(p => new[] { p.x, p.y }).ToArray();
            return hole ? b.Hole(inner) : b.Polyline(inner.Concat(inner.Take(2)).ToArray());
        }

        static B Offset(double offset)
        {
            var b = new B().Rect(offset, offset, offset + 10, offset + 10);
            var random = new SplitMix64((ulong)offset);
            for (int i = 0; i < 30; ++i) {
                double x = offset + 1 + random.Bits(13) / 1024.0, y = offset + 1 + random.Bits(13) / 1024.0;
                b.Seg(x, y, x + 1.0 / 1024, y + random.Bits(1) / 1024.0);
            }
            return b.Min(20);
        }

        static B Pairs()
        {
            var b = new B().Square();
            for (int i = 0; i < 8; ++i) {
                double y = 2 + 0.75 * i;
                b.Seg(0.5, y, 9.5, y + (i + 1) / 256.0);
                b.Seg(0.5, y + 3.0 / 1024, 9.5, y + 2.0 / 1024);
            }
            return b;
        }

        /// <summary>The table of docs/design/MESHING2D_CATALOGUE.md.</summary>
        public static string Markdown()
        {
            var sb = new StringBuilder();
            sb.Append("| Case | Title | State | Waits for or run by | Note |\n|---|---|---|---|---|\n");
            foreach (var r in Rows()) {
                string by = r.Test ?? (r.State == CaseState.Pending ? r.Feature : r.Case?.Id ?? "");
                sb.Append("| ").Append(r.Id).Append(" | ").Append(r.Title).Append(" | ").Append(r.State.ToString().ToLowerInvariant()).Append(" | ").Append(by).Append(" | ").Append(r.Note ?? "").Append(" |\n");
            }
            return sb.ToString();
        }
    }
}
