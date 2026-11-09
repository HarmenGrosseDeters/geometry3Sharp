using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Rep = geometry3Sharp.Tests.BooleanOracle.Representation;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Metamorphic and oracle tests of ExactMeshBoolean on a fixed corpus of procedural solids (docs/QA.md 4a items
    /// 9 and 10, docs/design/BOOLEAN_PROPERTY_TESTS.md): an analytic reference for boxes, inclusion-exclusion,
    /// (A-B) u (A n B) = A, commutativity, and invariance under triangle order, vertex ids, corner rotation and
    /// conforming subdivision. They run on net48 too; ExactBooleanPropertyTests.CsCheck.cs samples the same
    /// properties with shrinking (net10.0).
    /// </summary>
    public class ExactBooleanMetamorphicTests
    {
        static IntBox Bx(int x0, int y0, int z0, int x1, int y1, int z1) => new IntBox(x0, y0, z0, x1, y1, z1);

        /// <summary> one deterministic case per family, in the integer frame </summary>
        public static readonly Dictionary<string, (IntBox a, IntBox b)> Families = new Dictionary<string, (IntBox, IntBox)> {
            ["overlapping"] = (Bx(0, 0, 0, 2, 2, 2), Bx(1, 1, 1, 3, 3, 3)),
            ["coplanar overlap"] = (Bx(0, 0, 0, 2, 2, 2), Bx(1, 1, 0, 3, 3, 2)),
            ["cross"] = (Bx(0, 1, 1, 4, 2, 2), Bx(1, 0, 1, 2, 4, 2)),
            ["face touching, partial"] = (Bx(0, 0, 0, 2, 2, 2), Bx(2, 0, 0, 4, 1, 1)),
            ["face touching, equal faces"] = (Bx(0, 0, 0, 1, 1, 1), Bx(1, 0, 0, 2, 1, 1)),
            ["edge touching"] = (Bx(0, 0, 0, 2, 2, 2), Bx(2, 2, 0, 3, 3, 2)),
            ["vertex touching"] = (Bx(0, 0, 0, 2, 2, 2), Bx(2, 2, 2, 3, 3, 3)),
            ["nested"] = (Bx(0, 0, 0, 3, 3, 3), Bx(1, 1, 1, 2, 2, 2)),
            ["nested, on the wall"] = (Bx(0, 0, 0, 3, 3, 3), Bx(0, 1, 1, 1, 2, 2)),
            ["nested, in a corner"] = (Bx(0, 0, 0, 3, 3, 3), Bx(0, 0, 0, 1, 1, 1)),
            ["identical"] = (Bx(0, 0, 0, 2, 1, 3), Bx(0, 0, 0, 2, 1, 3)),
            ["disjoint"] = (Bx(0, 0, 0, 1, 1, 1), Bx(3, 0, 0, 4, 1, 1)),
        };

        public static IEnumerable<object[]> FamilyNames() => Families.Keys.Select(k => new object[] { k });

        // the identity, a small and a large power-of-two scale, and a translation on a 1/16 grid (all exact)
        static IEnumerable<BoxCase> Frames(string family)
        {
            var (a, b) = Families[family];
            yield return new BoxCase(a, b);
            yield return new BoxCase(a, b, -6, new Vector3d(1024.5, -3.25, 0.0625));
            yield return new BoxCase(a, b, 7, new Vector3d(-2048, 96, 4095.9375));
        }

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, bool safeRounding = true) => Run(a, b, op, out _, safeRounding);

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, out double maxDrift, bool safeRounding = true)
        {
            DMesh3 r = BooleanOracle.Run(a, b, op, out string error, out maxDrift, safeRounding);
            Assert.True(error == "", error);
            return r;
        }

        static void AssertOk(string reason, string what) => Assert.True(reason == "", what + ": " + reason);

        [Theory, MemberData(nameof(FamilyNames))]
        public void Boxes_MatchAnalyticReference(string family)
        {
            foreach (BoxCase c in Frames(family))
                foreach (Ops op in BooleanOracle.AllOps)
                    AssertOk(BooleanOracle.CheckBoxResult(Run(c.MeshA(), c.MeshB(), op), c, op), $"{family} {op} {c}");
        }

        [Theory, MemberData(nameof(FamilyNames))]
        public void Boxes_InclusionExclusion_PartitionAndCommutativity(string family)
        {
            foreach (BoxCase c in Frames(family)) {
                DMesh3 a = c.MeshA(), b = c.MeshB();
                DMesh3 union = Run(a, b, Ops.Union), inter = Run(a, b, Ops.Intersection), diff = Run(a, b, Ops.Difference);
                // vol(A u B) + vol(A n B) = vol A + vol B, exactly (dyadic coordinates, exact volumes)
                Assert.Equal(BooleanOracle.ExactSixVolume(a) + BooleanOracle.ExactSixVolume(b),
                    BooleanOracle.ExactSixVolume(union) + BooleanOracle.ExactSixVolume(inter));

                // (A - B) u (A n B) = A: computed, not only summed; it is checked against box A itself
                DMesh3 rebuilt = Run(diff, inter, Ops.Union);
                AssertOk(BooleanOracle.CheckBoxResult(rebuilt, new BoxCase(c.A, c.A, c.Exponent, c.Translation), Ops.Union), $"{family} {c}: (A-B) u (AnB)");
                AssertOk(BooleanOracle.SameSolid(rebuilt, a, c.Scale * 4), $"{family} {c}: (A-B) u (AnB) vs A");

                // commutativity of u and n
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                    DMesh3 ba = Run(b, a, op);
                    AssertOk(BooleanOracle.CheckBoxResult(ba, new BoxCase(c.B, c.A, c.Exponent, c.Translation), op), $"{family} {op} B,A");
                    AssertOk(BooleanOracle.SameSolid(op == Ops.Union ? union : inter, ba, c.Scale * 4), $"{family} {op}: A,B vs B,A");
                }
            }
        }

        [Theory, MemberData(nameof(FamilyNames))]
        public void Boxes_RepresentationInvariance(string family)
        {
            BoxCase c = Frames(family).ElementAt(1);
            double size = c.Scale * 4;
            foreach (Ops op in BooleanOracle.AllOps) {
                DMesh3 baseline = Run(c.MeshA(), c.MeshB(), op);
                foreach (Rep r in Enum.GetValues(typeof(Rep)).Cast<Rep>())
                    for (int operand = 0; operand < 3; ++operand) {   // A, B or both
                        DMesh3 a = c.MeshA(), b = c.MeshB();
                        if (operand != 1) { DMesh3 x = BooleanOracle.Change(a, r, 17); AssertOk(BooleanOracle.CheckRepresentation(a, x, size), $"{r} of A"); a = x; }
                        if (operand != 0) { DMesh3 x = BooleanOracle.Change(b, r, 29); AssertOk(BooleanOracle.CheckRepresentation(b, x, size), $"{r} of B"); b = x; }
                        DMesh3 result = Run(a, b, op, out double drift);
                        string what = $"{family} {op} {r} operand {"ABX"[operand]}";
                        if (r == Rep.Slivers) {
                            // SafeRounding: clean, and the same solid up to its documented displacement bound; the exact
                            // geometry without it (see Tetrahedra_MetamorphicRelations)
                            AssertOk(BooleanOracle.CleanReason(result), what + " SafeRounding");
                            AssertOk(BooleanOracle.WithinSafeRoundingBound(result, baseline, size, drift), what + $" SafeRounding vs baseline (MaxDrift {drift:G3})");
                            result = Run(a, b, op, safeRounding: false);
                        }
                        AssertOk(BooleanOracle.CheckBoxResult(result, c, op, r == Rep.Slivers), what);
                        AssertOk(BooleanOracle.SameSolid(result, baseline, size), what + " vs baseline");
                    }
            }
        }

        // ---------------------------------------------------------------- non-axis-aligned solids

        static Vector3i V(int x, int y, int z) => new Vector3i(x, y, z);

        public static readonly Dictionary<string, (IntTet a, IntTet b)> TetFamilies = new Dictionary<string, (IntTet, IntTet)> {
            ["overlapping"] = (new IntTet(V(0, 0, 0), V(4, 0, 0), V(0, 4, 0), V(0, 0, 4)), new IntTet(V(1, 1, 1), V(5, 1, 0), V(1, 5, 1), V(2, 2, 5))),
            ["shared vertex"] = (new IntTet(V(0, 0, 0), V(4, 0, 0), V(0, 4, 0), V(0, 0, 4)), new IntTet(V(0, 0, 0), V(3, 3, -1), V(-1, 3, 3), V(3, -1, 3))),
            ["shared edge"] = (new IntTet(V(0, 0, 0), V(4, 0, 0), V(0, 4, 0), V(0, 0, 4)), new IntTet(V(0, 0, 0), V(4, 0, 0), V(1, 3, 3), V(2, -2, 2))),
            ["coplanar faces"] = (new IntTet(V(0, 0, 0), V(4, 0, 0), V(0, 4, 0), V(0, 0, 4)), new IntTet(V(1, 1, 0), V(5, 1, 0), V(1, 5, 0), V(2, 2, 3))),
            ["sliver"] = (new IntTet(V(0, 0, 0), V(6, 0, 0), V(0, 6, 0), V(0, 0, 6)), new IntTet(V(-1, -1, 1), V(7, -1, 2), V(7, 7, 1), V(-1, 7, 2))),
            ["nested"] = (new IntTet(V(0, 0, 0), V(8, 0, 0), V(0, 8, 0), V(0, 0, 8)), new IntTet(V(1, 1, 1), V(3, 1, 1), V(1, 3, 1), V(1, 1, 3))),
        };

        public static IEnumerable<object[]> TetFamilyNames() => TetFamilies.Keys.Select(k => new object[] { k });

        [Theory, MemberData(nameof(TetFamilyNames))]
        public void Tetrahedra_MetamorphicRelations(string family)
        {
            var (ta, tb) = TetFamilies[family];
            foreach (Vector3d shift in new[] { Vector3d.Zero, new Vector3d(632117.25, 5712043.5, 113.75) })
                CheckTetRelations(ta, tb, shift, family);
        }

        internal static void CheckTetRelations(IntTet ta, IntTet tb, Vector3d shift, string what)
        {
            DMesh3 a = ta.Mesh(shift), b = tb.Mesh(shift);
            double size = 8;
            var results = new Dictionary<Ops, DMesh3>();
            foreach (Ops op in BooleanOracle.AllOps) {
                results[op] = Run(a, b, op);
                AssertOk(BooleanOracle.CleanReason(results[op]), $"{what} {op}");
            }
            // inclusion-exclusion within the rounding tolerance of the results (the inputs are exact)
            double lhs = BooleanOracle.Volume(results[Ops.Union]) + BooleanOracle.Volume(results[Ops.Intersection]);
            double rhs = (ta.SixVolume + tb.SixVolume) / 6.0;
            double tol = BooleanOracle.VolumeTolerance(results[Ops.Union], results[Ops.Intersection]) + BooleanOracle.VolumeTolerance(a, b);
            Assert.True(Math.Abs(lhs - rhs) <= tol, $"{what}: vol(AuB) + vol(AnB) = {lhs:R}, vol A + vol B = {rhs:R}, tolerance {tol:G3}");
            // difference and intersection partition A
            double split = BooleanOracle.Volume(results[Ops.Difference]) + BooleanOracle.Volume(results[Ops.Intersection]);
            Assert.True(Math.Abs(split - ta.SixVolume / 6.0) <= tol, $"{what}: vol(A-B) + vol(AnB) = {split:R}, vol A = {ta.SixVolume / 6.0:R}");

            DMesh3 rebuilt = Run(results[Ops.Difference], results[Ops.Intersection], Ops.Union);
            AssertOk(BooleanOracle.CleanReason(rebuilt), what + ": (A-B) u (AnB)");
            // (cracks of rounding thickness between the separately rounded A-B and AnB are allowed: SameRegularizedSolid)
            AssertOk(BooleanOracle.SameRegularizedSolid(rebuilt, a, size), what + ": (A-B) u (AnB) vs A");
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection })
                AssertOk(BooleanOracle.SameSolid(results[op], Run(b, a, op), size), $"{what} {op}: A,B vs B,A");
            foreach (Rep r in Enum.GetValues(typeof(Rep)).Cast<Rep>()) {
                DMesh3 ra = BooleanOracle.Change(a, r, 5), rb = BooleanOracle.Change(b, r, 6);
                AssertOk(BooleanOracle.CheckRepresentation(a, ra, size), $"{r} of A");
                AssertOk(BooleanOracle.CheckRepresentation(b, rb, size), $"{r} of B");
                foreach (Ops op in BooleanOracle.AllOps) {
                    DMesh3 result = Run(ra, rb, op, out double drift);
                    AssertOk(BooleanOracle.CleanReason(result), $"{what} {op} {r}");
                    if (r == Rep.Slivers) {
                        AssertOk(BooleanOracle.WithinSafeRoundingBound(result, results[op], size, drift), $"{what} {op} {r} vs baseline, SafeRounding (MaxDrift {drift:G3})");
                        // where the other solid cuts a sliver, the pieces can fall below the degenerate threshold of
                        // SafeRounding (area 1e-12 diagonal^2; rounded or even exact), which contracts them within its
                        // displacement bound (ExactBooleanPropertyFindings): exactly, the solid is compared without
                        // that repair, as the arrangement gives it
                        DMesh3 plain = Run(ra, rb, op, safeRounding: false);
                        AssertOk(BooleanOracle.SameSolid(plain, Run(a, b, op, safeRounding: false), size), $"{what} {op} {r} vs baseline, plain rounding");
                    }
                    else
                        AssertOk(BooleanOracle.SameSolid(result, results[op], size), $"{what} {op} {r} vs baseline");
                }
            }
        }

        // ---------------------------------------------------------------- similarities, reuse, repeated runs

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Partition_Similarities(string name)
        {
            // coplanar overlap: B's faces inside A are shared by A-B and AnB
            Similarity s = Similarity.ByName(name);
            var (ia, ib) = Families["coplanar overlap"];
            var c = new BoxCase(ia, ib);
            DMesh3 a = c.MeshA(), b = c.MeshB();
            DMesh3 d0 = Run(a, b, Ops.Difference), i0 = Run(a, b, Ops.Intersection);
            DMesh3 reference = Run(d0, i0, Ops.Union);
            AssertOk(BooleanOracle.CheckBoxResult(reference, new BoxCase(ia, ia), Ops.Union), "identity");
            DMesh3 ta = Invariance.Transformed(a, s), tb = Invariance.Transformed(b, s);
            DMesh3 d = Run(ta, tb, Ops.Difference), i = Run(ta, tb, Ops.Intersection);
            DMesh3 back = Invariance.MappedBack(Run(d, i, Ops.Union), s);
            AssertOk(BooleanOracle.CleanReason(back), name);
            if (s.IsExact) {
                // 2^k scales: the same result bit for bit
                Assert.Equal("", Invariance.GeometricDifference(reference, back));
                return;
            }
            if (!s.IsRotated && s.Scale == 1) {
                // a dyadic translation of the integer boxes (UTM) is exact too: the analytic reference in that frame
                var moved = new BoxCase(ia, ib, 0, s.Translation);
                AssertOk(BooleanOracle.CheckBoxResult(d, moved, Ops.Difference), name + ": A-B");
                AssertOk(BooleanOracle.CheckBoxResult(i, moved, Ops.Intersection), name + ": AnB");
                AssertOk(BooleanOracle.CheckBoxResult(Run(d, i, Ops.Union), new BoxCase(ia, ia, 0, s.Translation), Ops.Union), name + ": (A-B) u (AnB)");
                return;
            }
            // rotations and translations round the input coordinates: B's faces on A's planes z = 0 and z = 2 are no
            // longer exactly coplanar with them, and A-B can keep slabs of rounding thickness (rotated: area 24
            // instead of 22). A-B and AnB, rounded separately, can also leave cracks of that thickness where they
            // meet, which their union keeps. Both are true for the rounded inputs, so the regularized solids are
            // compared (volume and winding numbers away from the surface), not the boundaries.
            // (a coordinate is off by up to 3.5 rounding units of the transformed coordinates per transform: three
            // products summed in the rotation and the translation; once into the frame and once back, plus half a
            // unit for the rounding of the exact points: 7.5, at most 7.5 sqrt(3) < 16 per vertex; moving the
            // vertices by e changes the volume by at most e times the area)
            double tol = 16 * s.Resolution(3) * Invariance.Area(a);
            AssertOk(BooleanOracle.CheckBoxSolid(Invariance.MappedBack(d, s), c, Ops.Difference, tol), name + ": A-B");
            AssertOk(BooleanOracle.CheckBoxSolid(Invariance.MappedBack(i, s), c, Ops.Intersection, tol), name + ": AnB");
            AssertOk(BooleanOracle.CheckBoxSolid(back, new BoxCase(ia, ia), Ops.Union, tol), name + ": (A-B) u (AnB)");
        }

        [Fact]
        public void ReusedInstance_AndRepeatedRuns_GiveTheSameResult()
        {
            var (ia, ib) = Families["coplanar overlap"];
            var c = new BoxCase(ia, ib, -3, new Vector3d(17.5, 0, -1));
            var boolean = new ExactMeshBoolean(c.MeshA(), c.MeshB(), Ops.Union);
            var first = new Dictionary<Ops, DMesh3>();
            foreach (Ops op in BooleanOracle.AllOps) {
                boolean.Operation = op;
                Assert.True(boolean.Compute(), boolean.Error);
                first[op] = boolean.Result;
            }
            foreach (Ops op in BooleanOracle.AllOps) {
                boolean.Operation = op;
                Assert.True(boolean.Compute(), boolean.Error);
                Assert.Equal("", Invariance.Difference(first[op], boolean.Result));
                for (int run = 0; run < 3; ++run)
                    Assert.Equal("", Invariance.Difference(first[op], Run(c.MeshA(), c.MeshB(), op)));
                AssertOk(BooleanOracle.CheckBoxResult(first[op], c, op), op.ToString());
            }
        }

        // ---------------------------------------------------------------- the oracles themselves

        [Fact]
        public void BoxOracle_AcceptsHandBuiltResults_AndRejectsWrongOnes()
        {
            var c = new BoxCase(Bx(0, 0, 0, 1, 1, 1), Bx(1, 0, 0, 2, 1, 1));
            DMesh3 union = BooleanOracle.BoxMesh(Bx(0, 0, 0, 2, 1, 1), c);
            AssertOk(BooleanOracle.CheckBoxResult(union, c, Ops.Union), "hand-built union");
            AssertOk(BooleanOracle.CheckBoxResult(BooleanOracle.Subdivided(union, 2), c, Ops.Union), "subdivided union");
            AssertOk(BooleanOracle.CheckBoxResult(new DMesh3(), c, Ops.Intersection), "empty intersection");
            AssertOk(BooleanOracle.CheckBoxResult(BooleanOracle.BoxMesh(c.A, c), c, Ops.Difference), "difference");

            // the wrong operation, a non-empty result for an empty one
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(union, c, Ops.Difference));
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(union, c, Ops.Intersection));
            // the same volume and area, but another solid
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(BooleanOracle.BoxMesh(Bx(0, 0, 0, 1, 2, 1), c), c, Ops.Union));
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(BooleanOracle.BoxMesh(Bx(1, 0, 0, 3, 1, 1), c), c, Ops.Union));
            // inside out
            DMesh3 reversed = new DMesh3(union);
            reversed.ReverseOrientation(false);
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(reversed, c, Ops.Union));
            // a missing triangle
            DMesh3 open = new DMesh3(union);
            open.RemoveTriangle(open.TriangleIndices().First(), true, false);
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(open, c, Ops.Union));
            // an extra shell: a cavity (reversed inner box) keeps the area test but not the volume, a second box both
            var big = new BoxCase(Bx(0, 0, 0, 4, 4, 4), Bx(0, 0, 0, 4, 4, 4));
            DMesh3 withCavity = BooleanOracle.BoxMesh(big.A, big);
            DMesh3 cavity = BooleanOracle.BoxMesh(Bx(1, 1, 1, 2, 2, 2), big);
            cavity.ReverseOrientation(false);
            new MeshEditor(withCavity).AppendMesh(cavity);
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(withCavity, big, Ops.Union));
            // two boxes stacked as separate shells with a shared face (equal volume and area; the inner faces are extra)
            DMesh3 twoShells = BooleanOracle.BoxMesh(c.A, c);
            new MeshEditor(twoShells).AppendMesh(BooleanOracle.BoxMesh(c.B, c));
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(twoShells, c, Ops.Union));
            // a shifted result in a scaled frame
            var scaled = new BoxCase(c.A, c.B, -4, new Vector3d(3, 0, 0));
            DMesh3 shifted = BooleanOracle.BoxMesh(Bx(0, 0, 0, 2, 1, 1), new BoxCase(c.A, c.B, -4, new Vector3d(3.0625, 0, 0)));
            Assert.NotEqual("", BooleanOracle.CheckBoxResult(shifted, scaled, Ops.Union));
            AssertOk(BooleanOracle.CheckBoxResult(BooleanOracle.BoxMesh(Bx(0, 0, 0, 2, 1, 1), scaled), scaled, Ops.Union), "scaled union");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void EmptyResult_StillRequiresFiniteCoordinates(double invalid)
        {
            var pair = Families["disjoint"];
            var c = new BoxCase(pair.a, pair.b);
            Assert.Equal("", BooleanOracle.CleanReason(new DMesh3()));
            Assert.Equal("", BooleanOracle.CheckBoxResult(new DMesh3(), c, Ops.Intersection));
            var malformed = new DMesh3();
            malformed.AppendVertex(new Vector3d(invalid, 0, 0));
            Assert.Equal(0, malformed.TriangleCount);
            Assert.Equal("non-finite coordinates", BooleanOracle.CleanReason(malformed));
            Assert.Equal("non-finite coordinates", BooleanOracle.CheckBoxResult(malformed, c, Ops.Intersection));
        }

        [Fact]
        public void Dyadic_IsExact()
        {
            // 0.1 + 0.2 is 0.30000000000000004 in double; exactly it is neither that nor 0.3
            Dyadic sum = Dyadic.Of(0.1) + Dyadic.Of(0.2);
            Assert.NotEqual(Dyadic.Of(0.1 + 0.2), sum);
            Assert.NotEqual(Dyadic.Of(0.3), sum);
            Assert.Equal(Dyadic.Of(0.2), sum - Dyadic.Of(0.1));
            // a product with more than 53 bits, and subnormal and negative values
            double x = 1 + Math.Pow(2, -30);
            Assert.NotEqual(Dyadic.Of(x * x), Dyadic.Of(x) * Dyadic.Of(x));
            Assert.Equal(Dyadic.Of(1) + Dyadic.Of(Math.Pow(2, -29)) + Dyadic.Of(Math.Pow(2, -60)), Dyadic.Of(x) * Dyadic.Of(x));
            Assert.Equal(Dyadic.Of(-double.Epsilon), Dyadic.Of(0) - Dyadic.Of(double.Epsilon));
            Assert.Equal(-3.5, Dyadic.Of(-3.5).ToDouble());
            // the exact volume of a box with a sliver vertex of 2^-30, where the double sum is not exact
            DMesh3 box = BooleanOracle.SliverSplit(new BoxCase(Bx(0, 0, 0, 3, 5, 7), Bx(0, 0, 0, 1, 1, 1), 0, new Vector3d(1000.25, 0, 0)).MeshA(), 6, 30, 3);
            Assert.Equal(Dyadic.Of(6 * 105), BooleanOracle.ExactSixVolume(box));
        }

        [Fact]
        public void SameSolid_IgnoresTriangulation_AndRejectsOtherSolids()
        {
            var c = new BoxCase(Bx(0, 0, 0, 2, 1, 1), Bx(0, 0, 0, 2, 1, 1));
            DMesh3 box = c.MeshA();
            AssertOk(BooleanOracle.SameSolid(box, BooleanOracle.Subdivided(BooleanOracle.CornersRotated(box, 3), 2), 2), "subdivided");
            // equal volume and area, rotated by 90 degrees
            Assert.NotEqual("", BooleanOracle.SameSolid(box, BooleanOracle.BoxMesh(Bx(0, 0, 0, 1, 2, 1), c), 2));
            // equal volume and area, shifted by a little (2^-10 of the size)
            Assert.NotEqual("", BooleanOracle.SameSolid(box, new BoxCase(c.A, c.A, 0, new Vector3d(1.0 / 512, 0, 0)).MeshA(), 2));
            DMesh3 reversed = new DMesh3(box);
            reversed.ReverseOrientation(false);
            Assert.NotEqual("", BooleanOracle.SameSolid(box, reversed, 2));
            Assert.NotEqual("", BooleanOracle.SameSolid(box, new DMesh3(), 2));
            // a tetrahedron vs one with an extra sliver split: the same solid
            var tet = TetFamilies["overlapping"].a;
            AssertOk(BooleanOracle.SameSolid(tet.Mesh(Vector3d.Zero), BooleanOracle.SliverSplit(tet.Mesh(Vector3d.Zero), 3, 20, 1), 4), "sliver split");
        }

        [Fact]
        public void RepresentationChanges_KeepTheSurface_AndChangeTheRepresentation()
        {
            var c = new BoxCase(Bx(0, 0, 0, 2, 1, 3), Bx(0, 0, 0, 1, 1, 1), -5, new Vector3d(100.5, 0, 0));
            DMesh3 box = c.MeshA(), tet = TetFamilies["sliver"].a.Mesh(Vector3d.Zero);
            foreach (Rep r in Enum.GetValues(typeof(Rep)).Cast<Rep>())
                foreach (DMesh3 m in new[] { box, tet }) {
                    DMesh3 x = BooleanOracle.Change(m, r, 11);
                    AssertOk(BooleanOracle.CheckRepresentation(m, x, 4), r.ToString());
                    // dyadic inputs: the signed volume is kept bit for bit
                    Assert.Equal(BooleanOracle.ExactSixVolume(m), BooleanOracle.ExactSixVolume(x));
                }
            // subdivision against the unsplit procedural box: the analytic oracle of the box itself
            AssertOk(BooleanOracle.CheckBoxResult(BooleanOracle.Subdivided(box, 2), new BoxCase(c.A, c.A, c.Exponent, c.Translation), Ops.Union), "subdivided box");
            Assert.Equal(12 * 16, BooleanOracle.Subdivided(box, 2).TriangleCount);
            Assert.True(BooleanOracle.Subdivided(box, 2).IsClosed());
            // slivers are near-degenerate: the smallest angle is tiny
            DMesh3 slivers = BooleanOracle.SliverSplit(box, 3, 24, 2);
            Assert.True(slivers.TriangleIndices().Min(t => slivers.GetTriArea(t)) < 1e-6 * Invariance.Area(box));
        }
    }
}
