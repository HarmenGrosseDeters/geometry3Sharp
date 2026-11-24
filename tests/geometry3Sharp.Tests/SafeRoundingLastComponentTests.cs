using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #985 (docs/research/BOOLEAN_TRIAGE_2026-11.md, RC1): SafeRounding's cleanup removed the only component of a non-empty
    /// exact result as tiny or thin, both relative to that component's own diagonal, and reported an empty result with
    /// Success and RoundingClean. Owner decision 2026-11-17 (option a): where an exact input is itself thin or tiny by the same
    /// tests and the cleanup would empty the result, nothing is removed; every component is kept and counted in
    /// ScaleDisparity (RoundingClean false). A fat input with a thin result (the slivers of A - A') is dropped as before.
    /// </summary>
    public class SafeRoundingLastComponentTests
    {
        static DMesh3 Flattened(IntTet t, int e) => MetamorphicRelations.Stretch(t.Mesh(Vector3d.Zero), 0, e);

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool safeRounding = true)
        {
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safeRounding };
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, boolean.Status);
            return boolean;
        }

        public static IEnumerable<object[]> FamiliesAtTheEnds() =>
            ExactBooleanMetamorphicTests.TetFamilies.Keys.SelectMany(f => new[] { -46, 46 }.Select(e => new object[] { f, e }));

        /// <summary>
        /// The six families of #985, flattened along x by 2^-46 and 2^+46, all three operations: where the exact result
        /// (SafeRounding off) is not empty, the rounded one is not empty and is closed; whatever NotCleanReason finds in it is
        /// reported by the rounding too (RoundingClean is false then). Before the fix the unions were empty, Success, clean
        /// </summary>
        [Theory, MemberData(nameof(FamiliesAtTheEnds))]
        public void FlattenedTetFamilies_ResultIsNotEmptied_AndDefectsAreReported(string family, int e)
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            DMesh3 a = Flattened(ta, e), b = Flattened(tb, e);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                string what = $"{family} {op} 2^{e}";
                ExactMeshBoolean exact = Run(a, b, op, safeRounding: false);
                Assert.True(exact.Result.TriangleCount > 0, what + ": the exact result is empty (the case is not one of #985)");
                ExactMeshBoolean rounded = Run(a, b, op);
                Assert.True(rounded.Result.TriangleCount > 0, what + ": SafeRounding emptied a non-empty exact result");
                Assert.True(rounded.Result.IsClosed(), what + ": not closed");
                Assert.True(Invariance.AllFinite(rounded.Result), what);
                // every component of the exact result is still there (none dropped, no cavity lost), and a kept result is reported
                Assert.Equal(Components(exact.Result), Components(rounded.Result));
                if (rounded.Rounding.KeptWhenEmptied > 0) {
                    Assert.False(rounded.RoundingClean, what);
                    Assert.Equal(rounded.Rounding.KeptWhenEmptied, rounded.Rounding.ScaleDisparity);
                    Assert.Equal(0, rounded.Rounding.RemovedComponents);
                }
            }
        }

        /// <summary>
        /// The issue's case: the union of the nested pair is the flattened tetrahedron A (every vertex an input point). It was
        /// removed as thin (2^-46) and as tiny (2^+46); now it is kept and counted, and the repair is not clean
        /// </summary>
        [Theory]
        [InlineData(-46)]
        [InlineData(46)]
        public void NestedUnion_TheOnlyComponentIsKept_AndCounted(int e)
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            ExactMeshBoolean u = Run(Flattened(ta, e), Flattened(tb, e), Ops.Union);
            Assert.Equal(4, u.Result.TriangleCount);
            Assert.True(u.Result.IsClosed());
            Assert.Equal(0, u.Rounding.RemovedComponents);
            Assert.Equal(1, u.Rounding.ScaleDisparity);
            Assert.False(u.RoundingClean);
            Assert.True(u.Result.TriangleIndices().Sum(t => u.Result.GetTriArea(t)) > 0);
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        /// <summary>
        /// MeshRoundingRepair on its own: without KeepWhenEmptied (the default; ExactMeshBoolean sets it) a closed, flattened
        /// tetrahedron is removed as before; with it the tetrahedron is kept and counted
        /// </summary>
        [Theory]
        [InlineData(-46)]
        [InlineData(46)]
        public void MeshRoundingRepair_KeepWhenEmptied(int e)
        {
            var (ta, _) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            var plain = new MeshRoundingRepair(Flattened(ta, e));
            plain.Apply();
            Assert.Equal(0, plain.Mesh.TriangleCount);
            Assert.Equal(1, plain.RemovedComponents);
            DMesh3 m = Flattened(ta, e);
            var keep = new MeshRoundingRepair(m) { KeepWhenEmptied = true };
            keep.Apply();
            Assert.Equal(4, m.TriangleCount);
            Assert.Equal(0, keep.RemovedComponents);
            Assert.Equal(1, keep.KeptWhenEmptied);
            Assert.Equal(1, keep.ScaleDisparity);
            Assert.False(keep.IsClean);
        }

        /// <summary>
        /// the nested difference at 2^-46 is A with the cavity B: both shells are kept (keeping only the largest dropped the cavity)
        /// </summary>
        [Fact]
        public void NestedDifference_TheCavityIsKept()
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            DMesh3 a = Flattened(ta, -46), b = Flattened(tb, -46);
            ExactMeshBoolean d = Run(a, b, Ops.Difference);
            Assert.Equal(8, d.Result.TriangleCount);
            Assert.Equal(2, Components(d.Result));
            Assert.Equal(2, d.Rounding.KeptWhenEmptied);
            Assert.Equal(0, d.Rounding.RemovedComponents);
            // one shell is the inner one, reversed: its volume is negative
            int negative = 0;
            var comps = new MeshConnectedComponents(d.Result);
            comps.FindConnectedT();
            foreach (var c in comps.Components) {
                var sub = new DSubmesh3(d.Result, c.Indices).SubMesh;
                if (Invariance.Volume(sub) < 0) ++negative;
            }
            Assert.Equal(1, negative);
            Assert.False(d.RoundingClean);
        }

        /// <summary>
        /// What the kept results report where the repair's edits (within the isotropic bound, 1e-5 of the diagonal: far more
        /// than the slab's thickness) turned components inside out: not clean (RoundingClean false), and NotCleanReason names
        /// the defect it finds first. Measured 2026-11-17; the volume sign is exact (handed over to RC3: a true inversion, not
        /// a rounding of the sign)
        /// </summary>
        [Theory]
        [InlineData("shared vertex", -46, "inside-out components")]
        [InlineData("shared vertex", 46, "degenerate triangles")]
        // (sliver at -46 until #1464: with the result's ids given by its geometry the repair takes its edits in another order
        // and leaves that one with a positive volume; at 45 its edits turn it over)
        [InlineData("sliver", 45, "degenerate triangles")]
        public void KeptDifferences_TurnedInsideOutByTheEdits_AreReported(string family, int e, string reason)
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            ExactMeshBoolean d = Run(Flattened(ta, e), Flattened(tb, e), Ops.Difference);
            Assert.True(d.Rounding.KeptWhenEmptied > 0);
            Assert.False(d.RoundingClean);
            Assert.Equal(reason, MeshRobustRepair.NotCleanReason(d.Result));
            Assert.True(ExactVolumeSign(d.Result) < 0, "the result's exact volume is negative");
        }

        // the sign of the signed volume, exactly (BigInteger sum of the determinants; the coordinates scaled to integers)
        static int ExactVolumeSign(DMesh3 m)
        {
            System.Numerics.BigInteger Big(double d)
            {
                long bits = BitConverter.DoubleToInt64Bits(d);
                int ex = (int)((bits >> 52) & 0x7FF);
                long man = bits & 0xFFFFFFFFFFFFFL;
                if (ex == 0) ex = 1; else man |= 1L << 52;
                var b = new System.Numerics.BigInteger(man) << (ex - 1075 + 1200);
                return d < 0 ? -b : b;
            }
            System.Numerics.BigInteger sum = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                Vector3d p = m.GetVertex(f.a), q = m.GetVertex(f.b), r = m.GetVertex(f.c);
                System.Numerics.BigInteger px = Big(p.x), py = Big(p.y), pz = Big(p.z), qx = Big(q.x), qy = Big(q.y), qz = Big(q.z),
                    rx = Big(r.x), ry = Big(r.y), rz = Big(r.z);
                sum += px * (qy * rz - qz * ry) - py * (qx * rz - qz * rx) + pz * (qx * ry - qy * rx);
            }
            return sum.Sign;
        }

        /// <summary>
        /// IsThinOrTiny, the input test of ExactMeshBoolean: flattened (2^-46) and needle-like (2^+46) tetrahedra are, a unit
        /// tetrahedron and a sphere are not
        /// </summary>
        [Fact]
        public void IsThinOrTiny_FlattenedAndNeedleInputs()
        {
            var (ta, _) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            Assert.True(MeshRoundingRepair.IsThinOrTiny(Flattened(ta, -46), 1e-5, 1e-12));
            Assert.True(MeshRoundingRepair.IsThinOrTiny(Flattened(ta, 46), 1e-5, 1e-12));
            Assert.False(MeshRoundingRepair.IsThinOrTiny(ta.Mesh(Vector3d.Zero), 1e-5, 1e-12));
            Assert.False(MeshRoundingRepair.IsThinOrTiny(new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh(), 1e-5, 1e-12));
            Assert.False(MeshRoundingRepair.IsThinOrTiny(new DMesh3(), 1e-5, 1e-12));
            // judged by its largest component, as the removal judges: two unit tetrahedra 1e6 apart are not tiny
            Assert.False(MeshRoundingRepair.IsThinOrTiny(TwoFar(ta, Vector3d.Zero), 1e-5, 1e-12));
            // the largest component decides: a flattened one with a small fat one next to it is thin
            DMesh3 mixed = Flattened(ta, -46);
            DMesh3 small = ta.Mesh(new Vector3d(0, 20, 0));
            MeshTransforms.Scale(small, 1e-3);
            new MeshEditor(mixed).AppendMesh(small);
            Assert.True(MeshRoundingRepair.IsThinOrTiny(mixed, 1e-5, 1e-12));
        }

        // a tetrahedron and a copy of it 1e6 away (along x), shifted by d
        static DMesh3 TwoFar(IntTet t, Vector3d d)
        {
            DMesh3 m = t.Mesh(d);
            new MeshEditor(m).AppendMesh(t.Mesh(d + new Vector3d(1e6, 0, 0)));
            return m;
        }

        /// <summary>
        /// the other side of the rule: fat inputs whose result is only thin pieces (the slivers of nearly coincident tetrahedra,
        /// A - A') still give an empty result, reported clean (owner 2026-11-16). Since #1216 (owner 2026-11-22) the removal is
        /// listed: the shell between tetrahedra 1e-12 apart is 844 ulps thick and mangled by the repair (its own triangles are
        /// degenerate), so it goes as VolumeChanged. At 1e-13 of the diagonal it is far below what makes a result not clean.
        /// </summary>
        [Fact]
        public void FatInputs_ThinResult_IsStillDropped()
        {
            var (ta, _) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            DMesh3 a = ta.Mesh(Vector3d.Zero), b = ta.Mesh(new Vector3d(1e-12, 2e-12, -1e-12));
            Assert.Equal(12, Run(a, b, Ops.Difference, safeRounding: false).Result.TriangleCount);   // the exact sliver shell
            ExactMeshBoolean d = Run(a, b, Ops.Difference);
            Assert.Equal(0, d.Rounding.KeptWhenEmptied);
            Assert.Equal(1, d.Rounding.RemovedComponents);
            Assert.Equal(0, d.Result.TriangleCount);
            Assert.Equal(MeshRoundingRepair.RemovalReasons.VolumeChanged, d.Rounding.RemovedComponentInfos[0].Reason);
            Assert.InRange(d.Rounding.RemovedComponentInfos[0].ThicknessUlps, 100, 10000);
            Assert.False(d.Rounding.RemovedComponentInfos[0].Resolvable);
            Assert.Equal(0, d.Rounding.RemovedResolvable);
            Assert.True(d.RoundingClean);
            // also where the input has components far apart (its box is not the scale, its largest component is)
            DMesh3 a2 = TwoFar(ta, Vector3d.Zero), b2 = TwoFar(ta, new Vector3d(1e-12, 2e-12, -1e-12));
            Assert.True(Run(a2, b2, Ops.Difference, safeRounding: false).Result.TriangleCount > 0);
            ExactMeshBoolean d2 = Run(a2, b2, Ops.Difference);
            Assert.Equal(0, d2.Rounding.KeptWhenEmptied);
            Assert.Equal(0, d2.Result.TriangleCount);
            Assert.Equal(d2.Rounding.RemovedComponents, d2.Rounding.RemovedComponentInfos.Count);
            Assert.Equal(0, d2.Rounding.RemovedResolvable);
            Assert.True(d2.RoundingClean);
        }

        /// <summary>
        /// StrictRounding: a kept result is not clean, so Compute() fails with RoundingNotClean where it returned an empty,
        /// clean result before (Result null, the counts kept for the diagnosis)
        /// </summary>
        [Fact]
        public void StrictRounding_AKeptResultFails()
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            var u = new ExactMeshBoolean(Flattened(ta, -46), Flattened(tb, -46), Ops.Union) { StrictRounding = true };
            Assert.False(u.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, u.Status);
            Assert.Null(u.Result);
            Assert.Equal(1, u.Rounding.KeptWhenEmptied);
        }

        /// <summary>
        /// MeshRobustRepair takes a resolve that kept components this way as failed, as the empty result before (#712: the
        /// flattened tetrahedron goes to the wrap fallback)
        /// </summary>
        [Fact]
        public void MeshRobustRepair_TreatsAKeptResolveAsFailed()
        {
            var (ta, _) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            var r = new MeshRobustRepair(Flattened(ta, -46));
            Assert.True(r.Apply(), r.ResultReason);
            Assert.Equal("resolve failed", r.ExactResolveReason);
            Assert.Equal(1, r.ExactResolveRounding.KeptWhenEmptied);
            Assert.True(r.UsedAlphaWrap);
            Assert.True(r.Mesh.IsClosed());
        }

        /// <summary>
        /// Invariance (Similarity.All()): the 2^k scales give the same result bit for bit, mapped back; every similarity whose
        /// exact union is not empty keeps a non-empty, closed result of the nested union flattened by 2^-46
        /// </summary>
        [Fact]
        public void NestedUnion_InvariantUnderSimilarities()
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            DMesh3 a = Flattened(ta, -46), b = Flattened(tb, -46);
            ExactMeshBoolean b0 = Run(a, b, Ops.Union);
            int exact = 0;
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 sa = Invariance.Transformed(a, sim), sb = Invariance.Transformed(b, sim);
                ExactMeshBoolean r = Run(sa, sb, Ops.Union);
                // (a translation or rotation that is not exact can round the 2^-46 extent away: then the input is flat
                // and its exact union may be empty, which is no case of the rule)
                if (Run(sa, sb, Ops.Union, safeRounding: false).Result.TriangleCount > 0)
                    Assert.True(r.Result.TriangleCount > 0 && r.Result.IsClosed(), sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(b0.Result, Invariance.MappedBack(r.Result, sim)));
                    Assert.Equal(b0.Rounding.ScaleDisparity, r.Rounding.ScaleDisparity);
                    ++exact;
                }
            }
            Assert.True(exact >= 3);
        }

        /// <summary> reuse (Compute twice on one instance) and repeated runs give the same result, bit for bit </summary>
        [Fact]
        public void NestedUnion_ReuseAndRepeatedRuns_AreIdentical()
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies["nested"];
            DMesh3 a = Flattened(ta, 46), b = Flattened(tb, 46);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(boolean.Compute());
            DMesh3 first = boolean.Result;
            Assert.True(boolean.Compute());
            Assert.Equal("", Invariance.Difference(first, boolean.Result));
            Assert.Equal(1, boolean.Rounding.ScaleDisparity);
            for (int k = 0; k < 3; ++k)
                Assert.Equal("", Invariance.Difference(first, Run(a, b, Ops.Union).Result));
        }
    }
}
