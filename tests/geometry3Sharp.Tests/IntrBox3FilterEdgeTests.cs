using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The pieces of the certified filter of IntrExactBox3 at the edge of their ranges, one function at a time with the values at which the
    /// comparison or the constant matters (a random geometry test reaches such a value rarely; the first local mutation run of the shard
    /// left the survivors that shrink a bound or a margin because nothing tests the edge itself). Each case names the mutant it kills.
    /// </summary>
    public class IntrBox3FilterEdgeTests
    {
        const double U = IntrExactBox3.U, Huge = IntrExactBox3.Huge, AbsErr = IntrExactBox3.AbsErr;

        // ---- Kind: the rule of the infinite bounds ----
        [Fact]
        public void Kind_NumberUpToAndIncludingHuge()
        {
            Assert.Equal(0, IntrExactBox3.Kind(5e289, 5e289, 1, 0, 1, 0));            // |v| + ev == Huge exactly: a number (not <)
            Assert.Equal(2, IntrExactBox3.Kind(5e289, 5.000000000000001e289, 1, 0, 1, 0));
        }

        [Fact]
        public void Kind_InfinityNeedsTheNumeratorBeyondTwiceHugeTimesTheLargestQ()
        {
            double n = 2 * Huge * (1 + U);              // just above 2 Huge, but n (1 - 2u) is below it: undecided (n / (1 - 2u) or n (1 + 2u) would certify)
            Assert.Equal(2, IntrExactBox3.Kind(n, 0, n, 0, 1, 0));
            Assert.Equal(1, IntrExactBox3.Kind(2.0000001 * Huge, 0, 2.0000001 * Huge, 0, 1, 0));
            // the denominator is aq + eQ, not aq - eQ
            Assert.Equal(2, IntrExactBox3.Kind(2.5e290, 0, 2.5e290, 0, 1, 0.5));      // 2.5e290 < 2e290 * 1.5
            Assert.Equal(1, IntrExactBox3.Kind(3.5e290, 0, 3.5e290, 0, 1, 0.5));
            // eP is subtracted from the numerator
            Assert.Equal(2, IntrExactBox3.Kind(3e290, 0, 3e290, 2e290, 1, 0));
            Assert.Equal(1, IntrExactBox3.Kind(3e290, 0, 3e290, 0.5e290, 1, 0));
        }

        [Fact]
        public void Kind_OverflowedNumerator_IsBeyondAbout1p7e308_NotInfinitelyFar()
        {
            // n overflowed: the exact numerator is at least 1.7e308; with aq about 8.8e17 the bound is not certainly beyond 2 Huge
            Assert.Equal(2, IntrExactBox3.Kind(double.PositiveInfinity, 0, double.PositiveInfinity, 0, 8.8e17, 0));
            Assert.Equal(1, IntrExactBox3.Kind(double.PositiveInfinity, 0, double.PositiveInfinity, 0, 1e17, 0));
            // a finite numerator is its own magnitude, not 1.7e308
            Assert.Equal(2, IntrExactBox3.Kind(3e290, 0, 1e100, 0, 1, 0));
        }

        // ---- TErr: the bound of a quotient, term by term ----
        [Fact]
        public void TErr_IsTheDerivedFormula()
        {
            double en = 3, eq = 0.5, ri = 1.5, t = 7;
            Assert.Equal((en + Math.Abs(t) * eq) * ri + U * Math.Abs(t) * 2 + AbsErr, IntrExactBox3.TErr(en, eq, ri, t));
            Assert.Equal((0 + 7 * 0.5) * 1.5 + U * 7 * 2 + AbsErr, IntrExactBox3.TErr(0, 0.5, 1.5, -7));      // sign of t is not used
            Assert.True(IntrExactBox3.TErr(0, 0, 1, 1e-300) >= AbsErr);
        }

        // ---- Away: a ray that starts beyond a face and moves away ----
        [Fact]
        public void Away_TheEdgesOfEveryComparison()
        {
            Assert.True(IntrExactBox3.Away(10, 1, 0.5, 0.5, 5));       // Q == eQ still counts as moving away (Q >= eQ)
            Assert.False(IntrExactBox3.Away(10, 1, 0.4, 0.5, 5));      // Q below eQ: the sign is not certified
            Assert.True(IntrExactBox3.Away(10, 1, 0, 0, 5));           // Q == 0 exactly: not moving toward the face (Q >= 0)
            Assert.False(IntrExactBox3.Away(6, 1, 1, 0, 5));           // P - eP == e: the origin may be on the face (P - eP > e)
            Assert.True(IntrExactBox3.Away(6.5, 1, 1, 0, 5));
            Assert.True(IntrExactBox3.Away(-10, 1, -0.5, 0.5, 5));
            Assert.False(IntrExactBox3.Away(-10, 1, -0.4, 0.5, 5));
            Assert.True(IntrExactBox3.Away(-10, 1, 0, 0, 5));
            Assert.False(IntrExactBox3.Away(-6, 1, -1, 0, 5));         // P + eP == -e
            Assert.True(IntrExactBox3.Away(10, 1, 1, 0, 5));           // one of the two beyond-faces is enough (||)
            Assert.True(IntrExactBox3.Away(-10, 1, -1, 0, 5));
            Assert.False(IntrExactBox3.Away(10, 1, -1, 0, 5));         // beyond the face but moving toward it
            Assert.False(IntrExactBox3.Away(-10, 1, 1, 0, 5));
        }

        // ---- Step: the slab parallel to the direction ----
        static int Run(double P, double eP, double Q, double eQ, double L, double H, bool ray, ref IntrExactBox3.Acc c) { return IntrExactBox3.Step(0, P, eP, Q, eQ, L, H, ray, ref c); }
        static IntrExactBox3.Acc Fresh() { var c = default(IntrExactBox3.Acc); c.Init(false, 0, false, 0); return c; }

        [Fact]
        public void Step_ParallelSlab_TheEdgesOfEveryComparison()
        {
            var c = Fresh();
            Assert.Equal(1, Run(0, 0.5, 0, 0, 1, 2, false, ref c));          // P + eP < L: a miss
            Assert.Equal(3, Run(0.5, 0.5, 0, 0, 1, 2, false, ref c));        // P + eP == L: the point may touch the face
            Assert.Equal(1, Run(3, 0.5, 0, 0, 1, 2, false, ref c));          // P - eP > H
            Assert.Equal(3, Run(2.5, 0.5, 0, 0, 1, 2, false, ref c));        // P - eP == H
            Assert.Equal(0, Run(1.5, 0.25, 0, 0, 1, 2, false, ref c));       // strictly inside with its error: no constraint
            Assert.Equal(3, Run(1.5, 0.5, 0, 0, 1, 3, false, ref c));        // L == P - eP (and P + eP < H)
            Assert.Equal(3, Run(1.5, 0.5, 0, 0, 0, 2, false, ref c));        // L < P - eP, but P + eP == H
            Assert.Equal(3, Run(2.5, 0.5, 0, 0, 0, 3, false, ref c));        // P + eP == H
            Assert.Equal(0, Run(1, 0, 0, 0, 1, 2, false, ref c));           // exact P on the face L: the point touches the face (P >= L)
            Assert.Equal(0, Run(2, 0, 0, 0, 1, 2, false, ref c));           // exact P on the face H (P <= H)
            Assert.Equal(1, Run(2.0000001, 0, 0, 0, 1, 2, false, ref c));
            // eQ != 0 is not a parallel slab even for Q == 0: undecided (aq <= 2 eQ)
            Assert.Equal(3, Run(1.5, 0, 0, 1e-300, 1, 2, false, ref c));
        }

        [Fact]
        public void Step_QAgainstItsError_AndTheRangeOfQ()
        {
            var c = Fresh();
            Assert.Equal(3, Run(0, 0, 2, 1, 1, 2, false, ref c));            // aq == 2 eQ: the sign is not certified (aq <= 2 eQ)
            // aq == Tiny: the quotient is a number (undecided only below Tiny): a = 1e-300 / 1e-290
            c = Fresh();
            Assert.Equal(0, Run(0, 0, 1e-290, 0, 1e-300, 2e-300, false, ref c));
            Assert.Equal(1e-300 / 1e-290, c.LoBest);
            // aq == Huge: also a number
            c = Fresh();
            Assert.Equal(0, Run(0, 0, 1e290, 0, 1e290, 2e290, false, ref c));
            Assert.Equal(1.0, c.LoBest); Assert.Equal(2.0, c.UpBest);
            // below Tiny and above Huge: undecided
            c = Fresh();
            Assert.Equal(3, Run(0, 0, 9e-291, 0, 1e-300, 2e-300, false, ref c));
            Assert.Equal(3, Run(0, 0, 1.1e290, 0, 1e290, 2e290, false, ref c));
        }

        [Fact]
        public void Step_GeneralSlab_BoundsErrorsAndTheWinners()
        {
            // exact P and Q (eP = eQ = 0): a = (L - P) / Q = 1, b = 3, each with 2.5 u |t| + AbsErr
            var c = Fresh();
            Assert.Equal(0, Run(0, 0, 2, 0, 2, 6, false, ref c));
            double ea = 2.5 * U * 1 + AbsErr, eb = 2.5 * U * 3 + AbsErr;
            Assert.Equal(1 - ea, c.LoMinus); Assert.Equal(1 + ea, c.LoPlus); Assert.Equal(1.0, c.LoBest); Assert.Equal(ea, c.LoBestErr);
            Assert.Equal(3 + eb, c.UpPlus); Assert.Equal(3 - eb, c.UpMinus); Assert.Equal(3.0, c.UpBest); Assert.Equal(eb, c.UpBestErr);
            Assert.Equal(0, c.Act0); Assert.Equal(0, c.Act1);
            // a negative Q swaps the bounds
            c = Fresh();
            Assert.Equal(0, Run(0, 0, -2, 0, 2, 6, false, ref c));
            Assert.Equal(-3.0, c.LoBest); Assert.Equal(-1.0, c.UpBest);
            // ties keep the first slab as the winner (strict comparisons)
            c = Fresh();
            Assert.Equal(0, IntrExactBox3.Step(0, 0, 0, 2, 0, 2, 6, false, ref c));
            Assert.Equal(0, IntrExactBox3.Step(1, 0, 0, 2, 0, 2, 6, false, ref c));
            Assert.Equal(0, c.Act0); Assert.Equal(0, c.Act1);
            Assert.Equal(1 - ea, c.LoMinus); Assert.Equal(1 + ea, c.LoPlus); Assert.Equal(3 - eb, c.UpMinus); Assert.Equal(3 + eb, c.UpPlus);
            // a later, larger lower bound and smaller upper bound win
            Assert.Equal(0, IntrExactBox3.Step(2, 0, 0, 2, 0, 4, 5, false, ref c));      // a = 2, b = 2.5
            Assert.Equal(2, c.Act0); Assert.Equal(2, c.Act1); Assert.Equal(2.0, c.LoBest); Assert.Equal(2.5, c.UpBest);
            Assert.True(c.LoMinus > 1.5 && c.LoPlus > 2 && c.UpMinus < 2.5 + 1e-15 && c.UpPlus < 2.6);
            // a bound with error: P and Q carry errors
            c = Fresh();
            Assert.Equal(0, Run(0, 0.25, 2, 0.5, 2, 6, false, ref c));
            double ri = 1.0000001 / (2 - 0.5), en1 = 0.25 + U * 2 + AbsErr, en2 = 0.25 + U * 6 + AbsErr;
            Assert.Equal(1 - IntrExactBox3.TErr(en1, 0.5, ri, 1), c.LoMinus);
            Assert.Equal(3 - IntrExactBox3.TErr(en2, 0.5, ri, 3), c.UpMinus);
        }

        [Fact]
        public void Step_RayInsideTheEnteringSide_NeedsOnlyTheUpperBound()
        {
            // the origin certainly inside the slab on the entering side: no lower bound is set, the upper one is (H - P) / Q
            var c = Fresh();
            Assert.Equal(0, Run(0, 0, 2, 0, -2, 6, true, ref c));
            Assert.Equal(double.NegativeInfinity, c.LoBest); Assert.Equal(3.0, c.UpBest); Assert.Equal(0, c.Act1);
            double e3 = 2.5 * U * 3 + AbsErr;
            Assert.Equal(3 + e3, c.UpPlus); Assert.Equal(3 - e3, c.UpMinus); Assert.Equal(e3, c.UpBestErr);
            // the same for a negative Q: the upper bound is (L - P) / Q
            c = Fresh();
            Assert.Equal(0, Run(0, 0, -2, 0, -6, 2, true, ref c));
            Assert.Equal(double.NegativeInfinity, c.LoBest); Assert.Equal(3.0, c.UpBest);
            // not a ray: both bounds
            c = Fresh();
            Assert.Equal(0, Run(0, 0, 2, 0, -2, 6, false, ref c));
            Assert.Equal(-1.0, c.LoBest);
            // the origin on the entering face (n1 == 0 with its error > 0): not certainly inside: both bounds
            c = Fresh();
            Assert.Equal(0, Run(0, 0, 2, 0, 0, 6, true, ref c));
            Assert.Equal(0.0, c.LoBest); Assert.Equal(0, c.Act0);
        }

        [Fact]
        public void Step_InfiniteBounds_AreCertifiedOnlyBeyondTheRule()
        {
            var c = Fresh();
            // L = -1e300, H = 1e300 with Q = 1: both bounds beyond 2 Huge on opposite sides: no constraint
            Assert.Equal(0, Run(0, 0, 1, 0, -1e300, 1e300, false, ref c));
            Assert.Equal(double.NegativeInfinity, c.LoBest); Assert.Equal(double.PositiveInfinity, c.UpBest);
            // both beyond on the same side: undecided
            c = Fresh();
            Assert.Equal(3, Run(0, 0, 1, 0, 1e300, 2e300, false, ref c));
            c = Fresh();
            Assert.Equal(3, Run(0, 0, 1, 0, -2e300, -1e300, false, ref c));
            // one infinite lower bound and a finite upper bound: the lower end is -infinity
            c = Fresh();
            Assert.Equal(0, Run(0, 0, 1, 0, -1e300, 5, false, ref c));
            Assert.Equal(double.NegativeInfinity, c.LoBest); Assert.Equal(5.0, c.UpBest);
            // the sign of the infinity follows n and Q: Q < 0 swaps
            c = Fresh();
            Assert.Equal(0, Run(0, 0, -1, 0, -5, 1e300, false, ref c));
            Assert.Equal(double.NegativeInfinity, c.LoBest); Assert.Equal(5.0, c.UpBest);
            c = Fresh();
            Assert.Equal(0, Run(0, 0, -1, 0, -1e300, 5, false, ref c));
            Assert.Equal(-5.0, c.LoBest); Assert.Equal(double.PositiveInfinity, c.UpBest);
            // subnormal-sized bounds are undecided: a = (L - P) / Q below Tiny
            c = Fresh();
            Assert.Equal(3, Run(0, 0, 1, 0, 1e-300, 5, false, ref c));
            c = Fresh();
            Assert.Equal(3, Run(0, 0, 1, 0, -5, 1e-300, false, ref c));
            // an error that reaches inside the rule: eP large, the computed bound beyond Huge but not certainly infinite
            c = Fresh();
            Assert.Equal(3, Run(0, 1e300, 1, 0, -1e300, 5, false, ref c));
        }

        [Fact]
        public void Step_EachErrorTermAloneUsesTheGeneralBound()
        {
            // exactPQ is eP == 0 AND eQ == 0: with one of them alone the bound is TErr, not the 2.5 u of exact P and Q
            foreach (var (eP, eQ) in new[] { (0.0, 0.25), (0.25, 0.0) }) {
                var c = Fresh();
                Assert.Equal(0, Run(0, eP, 2, eQ, 2, 6, false, ref c));
                double ri = 1.0000001 / (2 - eQ), en1 = eP + U * 2 + AbsErr, en2 = eP + U * 6 + AbsErr;
                Assert.Equal(1 - IntrExactBox3.TErr(en1, eQ, ri, 1), c.LoMinus);
                Assert.Equal(3 + IntrExactBox3.TErr(en2, eQ, ri, 3), c.UpPlus);
            }
        }

        [Fact]
        public void Step_RayBranch_UsesTheErrorOfTheUpperNumerator_AndTheTinyEdge()
        {
            // Q < 0: the upper bound is (L - P) / Q with the error of n1; Q > 0: (H - P) / Q with the error of n2
            foreach (double q in new[] { 2.0, -2.0 }) {
                var c = Fresh();
                double L = q > 0 ? -2 : -6, H = q > 0 ? 6 : 2;       // the origin certainly inside on the entering side
                Assert.Equal(0, Run(0, 0.125, q, 0.25, L, H, true, ref c));
                double nu = q > 0 ? H : L, enu = 0.125 + U * Math.Abs(nu) + AbsErr, uv = nu / q;
                double ri = 1.0000001 / (Math.Abs(q) - 0.25);
                Assert.Equal(IntrExactBox3.TErr(enu, 0.25, ri, uv), c.UpBestErr);
                Assert.Equal(uv + c.UpBestErr, c.UpPlus); Assert.Equal(uv - c.UpBestErr, c.UpMinus);
            }
            // the upper bound is below Tiny: undecided; at Tiny exactly it is a number
            var d = Fresh();
            Assert.Equal(3, Run(0, 0, 1, 0, -1, 1e-300, true, ref d));
            d = Fresh();
            Assert.Equal(0, Run(0, 0, 1, 0, -1, 1e-290, true, ref d));
            Assert.Equal(1e-290, d.UpBest);
            // a tie in the ray branch keeps the first slab as the winner
            d = Fresh();
            Assert.Equal(0, IntrExactBox3.Step(0, 0, 0, 2, 0, -2, 6, true, ref d));
            Assert.Equal(0, IntrExactBox3.Step(1, 0, 0, 2, 0, -2, 6, true, ref d));
            Assert.Equal(0, d.Act1);
            // a bound of exactly Tiny in the general branch is a number
            d = Fresh();
            Assert.Equal(0, Run(0, 0, 1, 0, 1e-290, 5, false, ref d));
            Assert.Equal(1e-290, d.LoBest);
            d = Fresh();
            Assert.Equal(0, Run(0, 0, 1, 0, -5, 1e-290, false, ref d));
            Assert.Equal(1e-290, d.UpBest);
        }

        [Fact]
        public void Prep_ErrorOfQ_IsZeroOnlyWhenEveryProductIsAnExactZero()
        {
            Vector3d o = new Vector3d(1.1, 2.2, 3.3), c = Vector3d.Zero;
            double P, eP, Q, eQ;
            // every product zero (the direction component or the axis component): exact
            Vector3d dd = new Vector3d(0, 0.3, 0.7);
            IntrExactBox3.Prep(1, 0, 0, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.Equal(0.0, eQ); Assert.Equal(0.0, Q);
            dd = new Vector3d(0.5, 0.3, 0.7);
            IntrExactBox3.Prep(0, 0, 0, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.Equal(0.0, eQ);
            // one product not zero: an error
            IntrExactBox3.Prep(1, 0, 0, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.True(eQ > 0);
            // each single non-zero product on its own
            dd = new Vector3d(0, 0.3, 0.7);
            IntrExactBox3.Prep(0.6, 0.8, 0, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.True(eQ > 0);
            IntrExactBox3.Prep(0, 0, 1, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.True(eQ > 0);
            dd = new Vector3d(0.5, 0, 0.7);
            IntrExactBox3.Prep(0.6, 0.8, 0.1, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.True(eQ > 0);
            dd = new Vector3d(0.5, 0.3, 0);
            IntrExactBox3.Prep(0.6, 0.8, 0.1, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.True(eQ > 0);
            // dd.x == 0 and the others' axis components zero: exact; dd.x != 0 with ux == 0 and others dd == 0: exact
            dd = new Vector3d(0, 0.3, 0.7);
            IntrExactBox3.Prep(0.6, 0, 0, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.Equal(0.0, eQ);
            dd = new Vector3d(0.5, 0, 0);
            IntrExactBox3.Prep(0, 0.8, 0.1, 0, 0, 0, ref o, ref dd, out P, out eP, out Q, out eQ);
            Assert.Equal(0.0, eQ);
            // the error of P is the same sum
            Assert.True(eP > 0);
        }

        [Fact]
        public void NonSolidSegment_WithAnEndOnAFace_IsAHit_ForEachFaceAndEnd()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            // the start or the end on the lower or the upper face, the rest inside the open box: a hit when only the surface counts
            var cases = new[] {
                (new Vector3d(-1, 0, 0), new Vector3d(0.5, 0, 0)),     // start on the lower face
                (new Vector3d(1, 0, 0), new Vector3d(-0.5, 0, 0)),     // start on the upper face
                (new Vector3d(0.5, 0, 0), new Vector3d(-1, 0, 0)),     // end on the lower face
                (new Vector3d(-0.5, 0, 0), new Vector3d(1, 0, 0)),     // end on the upper face
            };
            foreach (var (a, b) in cases) {
                var s = new IntrSegment3Box3(new Segment3d(a, b), box, false);
                Assert.True(s.Find());
                var inside = new IntrSegment3Box3(new Segment3d(a * 0.5, b * 0.5), box, false);
                Assert.False(inside.Find());                            // strictly inside: no surface contact
                Assert.True(new IntrSegment3Box3(new Segment3d(a * 0.5, b * 0.5), box, true).Find());
            }
        }

        [Fact]
        public void ZeroDirection_NoComponent_IsNoContact_OnTheExactRoute()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            double t0 = -double.MaxValue, t1 = double.MaxValue; int q = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Segment;
            Assert.False(IntrLine3Box3.DoClipping(ref t0, ref t1, Vector3d.Zero, Vector3d.Zero, box, true, ref q, ref p0, ref p1, ref type));
            Assert.Equal(IntersectionType.Empty, type); Assert.Equal(0, q);
            var aab = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            Vector3d o = Vector3d.Zero, z = Vector3d.Zero;
            t0 = -double.MaxValue; t1 = double.MaxValue;
            Assert.False(IntrLine3AxisAlignedBox3.DoClipping(ref t0, ref t1, ref o, ref z, ref aab, true, ref q, ref p0, ref p1, ref type));
            // with a limit on one side the zero direction is still no direction
            t0 = 0; t1 = double.MaxValue;
            Assert.False(IntrLine3Box3.DoClipping(ref t0, ref t1, Vector3d.Zero, Vector3d.Zero, box, true, ref q, ref p0, ref p1, ref type));
        }

        // ---- the points: an end of a segment that is an end of the contact is that end exactly ----
        [Fact]
        public void SegmentEnd_InsideTheBox_IsReturnedExactly_NotRecomputedFromT()
        {
            // 1.88 + (0.2 - 1.88) is 0.19999999999999996, not 0.2: the end point P1 must be returned as given
            var box = new Box3d(new Vector3d(0, 0, 0), new Vector3d(10, 10, 10));
            Vector3d p0 = new Vector3d(1.88, 1.88, 1.88), p1 = new Vector3d(0.2, 0.2, 0.2);
            var spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
            IntrExactBox3.Hit hit;
            Assert.True(IntrExactBox3.Find(ref spec, ref p0, ref p0, ref p1, true, 0, true, 1, true, out hit));
            Assert.Equal(2, hit.Kind);
            Assert.Equal(1.0, hit.T1); Assert.Equal(0.0, hit.T0);
            Assert.Equal(p1, hit.P1); Assert.Equal(p0, hit.P0);
            // the same through the public segment class
            var s = new IntrSegment3Box3(new Segment3d(p0, p1), box, true);
            Assert.True(s.Find());
            Assert.Equal(s.Segment.P1, s.Point1);
            Assert.Equal(s.Segment.P0, s.Point0);
        }
    }
}
