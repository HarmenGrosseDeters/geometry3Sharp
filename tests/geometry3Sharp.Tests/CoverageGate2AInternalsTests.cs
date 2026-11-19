using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Lines the nightly changed-line coverage gate found uncovered (group A): the non-finite fallback of FastPointWinding.ExactEval,
    /// the planar-span polygon of AutoHoleFill and the private parts of QuadFlowRepair (the lattice offset struct, the fan walk,
    /// the extraction of an unpaired triangle). The private members are reached by reflection, with a fabricated state where the
    /// public path cannot produce it.
    /// </summary>
    public class CoverageGate2AInternalsTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // ------------------------------------------------------------------ FastPointWinding.ExactEval

        [Fact]
        public void ExactEval_NonFiniteArea_AtAnUnderflowingDistance_FollowsTheDirectFormula()
        {
            // the cube of the distance underflows to 0 (the direct term is not usable) and the area is infinite: the scaled
            // evaluation cannot split it into a mantissa and an exponent, so the direct formula is the answer (here +infinity)
            Vector3d x = new Vector3d(1e-200, 1e-200, 1e-200), q = Vector3d.Zero, n = new Vector3d(1, 1, 1);
            double len = (x - q).Length;
            Assert.Equal(0.0, len * len * len);
            double value = FastPointWinding.ExactEval(ref x, ref n, double.PositiveInfinity, ref q);
            Assert.True(double.IsPositiveInfinity(value));
            // the opposite normal: the same branch gives -infinity (the sign of the direct formula)
            Vector3d m = -n;
            Assert.True(double.IsNegativeInfinity(FastPointWinding.ExactEval(ref x, ref m, double.PositiveInfinity, ref q)));
            // a finite area at a distance of 1e-110 (the cube still underflows) does not take the fallback: the scaled evaluation
            // gives the finite value of the dipole, here along the axis (1/(4 pi d^2))
            Vector3d xf = new Vector3d(1e-110, 0, 0), nf = Vector3d.AxisX;
            double finite = FastPointWinding.ExactEval(ref xf, ref nf, 1.0, ref q);
            Assert.True(MathUtil.IsFinite(finite));
            Assert.Equal(1e220 / MathUtil.FourPI, finite, 1e205);
        }

        // ------------------------------------------------------------------ QuadFlowRepair

        static readonly Type RepairType = typeof(QuadFlowRepair);
        static readonly Type OffType = RepairType.GetNestedType("Off", Any);
        static readonly Type TriType = RepairType.GetNestedType("Tri", Any);

        static object NewOff(int x, int y) => Activator.CreateInstance(OffType, x, y);

        static object NewTri(int[] v, (int x, int y)[] d)
        {
            object t = Activator.CreateInstance(TriType);
            TriType.GetField("V").SetValue(t, v);
            Array offs = Array.CreateInstance(OffType, 3);
            for (int i = 0; i < 3; ++i) offs.SetValue(NewOff(d[i].x, d[i].y), i);
            TriType.GetField("D").SetValue(t, offs);
            return t;
        }

        static void SetField(object o, string name, object value) => RepairType.GetField(name, Any).SetValue(o, value);
        static IList Tris(QuadFlowRepair r) => (IList)RepairType.GetField("tris", Any).GetValue(r);

        [Fact]
        public void Off_EqualsAndHashCode_FollowTheComponents()
        {
            Assert.NotNull(OffType);
            object a = NewOff(3, -2), same = NewOff(3, -2), other = NewOff(3, 2), swapped = NewOff(-2, 3);
            Assert.True(a.Equals(same));
            Assert.False(a.Equals(other));
            Assert.False(a.Equals(swapped));
            Assert.False(a.Equals("not an offset"));
            Assert.False(a.Equals(null));
            Assert.Equal(same.GetHashCode(), a.GetHashCode());
            Assert.Equal(3 * 31 - 2, a.GetHashCode());
            Assert.Equal(0, NewOff(0, 0).GetHashCode());
            Assert.Equal(31, NewOff(1, 0).GetHashCode());
            Assert.Equal(1, NewOff(0, 1).GetHashCode());
        }

        // a closed fan of n triangles around vertex 0: triangle k continues into triangle k+1 across its slot 2 (the edge into
        // vertex 0), whose slot 0 is the reversed edge; rot is the quarter turn of the transition out of triangle 0
        static (bool closed, List<int> ft, List<int> cum) WalkFan(int n, int rot)
        {
            var r = new QuadFlowRepair(new DMesh3(), null, null, 1);
            IList tris = Tris(r);
            for (int k = 0; k < n; ++k) {
                object t = NewTri(new[] { 0, k + 1, k + 2 }, new[] { (1, 0), (0, 1), (-1, -1) });
                TriType.GetField("Nb").SetValue(t, new[] { -1, -1, (k + 1) % n });
                TriType.GetField("NbS").SetValue(t, new[] { 0, 0, 0 });
                TriType.GetField("Rot").SetValue(t, new[] { 0, 0, k == 0 ? rot : 0 });
                tris.Add(t);
            }
            var cornerTri = new int[n + 2]; var cornerIdx = new int[n + 2];
            for (int i = 0; i < cornerTri.Length; ++i) cornerTri[i] = -1;
            cornerTri[0] = 0; cornerIdx[0] = 0;
            SetField(r, "cornerTri", cornerTri); SetField(r, "cornerIdx", cornerIdx);
            var ft = new List<int>(); var fi = new List<int>(); var cum = new List<int>();
            bool closed = (bool)RepairType.GetMethod("Fan", Any).Invoke(r, new object[] { 0, ft, fi, cum });
            return (closed, ft, cum);
        }

        [Fact]
        public void Fan_ClosedFanWithoutHolonomy_IsWalkedAround()
        {
            var (closed, ft, cum) = WalkFan(150, 0);
            Assert.True(closed);
            Assert.Equal(150, ft.Count);
            Assert.Equal(0, ft[0]); Assert.Equal(149, ft[149]);
            Assert.All(cum, c => Assert.Equal(0, c));
            // a full turn of four quarter turns is no holonomy either
            var (closed4, ft4, cum4) = WalkFan(5, 4);
            Assert.True(closed4);
            Assert.Equal(5, ft4.Count);
            Assert.All(cum4, c => Assert.Equal(0, c));
        }

        [Fact]
        public void Fan_HolonomyOfAQuarterTurn_IsNotAClosedFan()
        {
            var (closed, ft, _) = WalkFan(5, 1);
            Assert.False(closed);
            Assert.Equal(5, ft.Count);
        }

        [Fact]
        public void Fan_LongerThanTheGuard_IsGivenUp()
        {
            // a cycle of 250 triangles is closed, but the walk stops after 200 steps: no fan
            var (closed, ft, _) = WalkFan(250, 0);
            Assert.False(closed);
            Assert.Equal(200, ft.Count);
            // the longest fan that is still walked: 200 triangles
            var (closedMax, ftMax, _) = WalkFan(200, 0);
            Assert.True(closedMax);
            Assert.Equal(200, ftMax.Count);
        }

        [Fact]
        public void Extract_ATriangleWithoutItsPartner_IsCountedAsUnpaired_AndNoMeshIsReturned()
        {
            DMesh3 surface = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            var r = new QuadFlowRepair(surface, null, null, 1);
            // one triangle with the offsets (1, 0), (0, 1), (-1, -1): a hypotenuse, two unit edges, but no triangle on the other side
            Tris(r).Add(NewTri(new[] { 0, 1, 2 }, new[] { (1, 0), (0, 1), (-1, -1) }));
            var vp = (List<Vector3d>)RepairType.GetField("VP", Any).GetValue(r);
            var vn = (List<Vector3d>)RepairType.GetField("VN", Any).GetValue(r);
            vp.Add(new Vector3d(-1, -1, 1)); vp.Add(new Vector3d(1, -1, 1)); vp.Add(new Vector3d(1, 1, 1));
            for (int i = 0; i < 3; ++i) vn.Add(Vector3d.AxisZ);
            RepairType.GetMethod("Extract", Any).Invoke(r, new object[] { vp.ToArray() });
            Assert.Equal(1, r.UnpairedTriangles);
            Assert.Equal(0, r.BadTriangles);
            Assert.Equal(0, r.Quads);
            Assert.Equal(PureQuadStatus.NotManifold, r.Status);
            Assert.Null(r.Result);

            // two lone triangles over different hypotenuses count twice
            var r2 = new QuadFlowRepair(surface, null, null, 1);
            Tris(r2).Add(NewTri(new[] { 0, 1, 2 }, new[] { (1, 0), (0, 1), (-1, -1) }));
            Tris(r2).Add(NewTri(new[] { 0, 2, 3 }, new[] { (1, 0), (0, 1), (-1, -1) }));
            var vp2 = (List<Vector3d>)RepairType.GetField("VP", Any).GetValue(r2);
            var vn2 = (List<Vector3d>)RepairType.GetField("VN", Any).GetValue(r2);
            for (int i = 0; i < 4; ++i) { vp2.Add(new Vector3d(i, 0, 1)); vn2.Add(Vector3d.AxisZ); }
            RepairType.GetMethod("Extract", Any).Invoke(r2, new object[] { vp2.ToArray() });
            Assert.Equal(2, r2.UnpairedTriangles);
            Assert.Equal(PureQuadStatus.NotManifold, r2.Status);
            Assert.Null(r2.Result);
        }
    }
}
