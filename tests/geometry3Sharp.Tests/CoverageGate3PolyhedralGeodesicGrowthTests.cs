// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Reflection;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The growth of the first window chunk in PolyhedralGeodesicDistance.NewWindow. The chunk is sized from MaxWindows plus a slack of
    /// 2 x valence + 2; a real run never overshoots that slack (a fuzz of 60k small-MaxWindows runs peaked at 9 of 14), so the guard is
    /// exercised by calling the private NewWindow on a computed instance until the chunk is full.
    /// </summary>
    public class CoverageGate3PolyhedralGeodesicGrowthTests
    {
        const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        static DMesh3 Tetrahedron()
        {
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0)); m.AppendVertex(new Vector3d(0, 0, 1));
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(0, 3, 2);
            return m;
        }

        [Fact]
        public void FirstChunk_WhenFull_DoublesAndKeepsTheStoredWindows()
        {
            var g = new PolyhedralGeodesicDistance(Tetrahedron()) { MaxWindows = 1 };
            g.Compute(new[] { 0 });
            Type t = typeof(PolyhedralGeodesicDistance);
            FieldInfo wch = t.GetField("wchunks", NonPublic), nch = t.GetField("nchunks", NonPublic), pch = t.GetField("pchunks", NonPublic), wc = t.GetField("wcount", NonPublic);
            MethodInfo nw = t.GetMethod("NewWindow", NonPublic);
            Type wt = t.GetNestedType("Window", BindingFlags.NonPublic);
            FieldInfo sigma = wt.GetField("Sigma");
            foreach (object member in new object[] { wch, nch, pch, wc, nw, wt, sigma }) Assert.NotNull(member);   // a renamed private member fails clearly
            Array Chunk0() => ((Array[])wch.GetValue(g))[0];

            int len0 = Chunk0().Length;
            Assert.True(len0 < 1 << 16);
            int first = (int)wc.GetValue(g);
            Assert.True(first < len0);
            // fill the chunk exactly: no growth yet
            int last = -1;
            for (int i = first; i < len0; ++i) {
                object w = Activator.CreateInstance(wt); sigma.SetValue(w, 100.0 + i);
                last = (int)nw.Invoke(g, new[] { w });
            }
            Assert.Equal(len0, Chunk0().Length);
            // the next window does not fit: the chunk doubles (all three parallel arrays) and the stored windows survive
            object extra = Activator.CreateInstance(wt); sigma.SetValue(extra, 5.5);
            int id = (int)nw.Invoke(g, new[] { extra });
            Assert.Equal(len0, id);
            Assert.Equal(2 * len0, Chunk0().Length);
            Assert.Equal(2 * len0, ((Array[])nch.GetValue(g))[0].Length);
            Assert.Equal(2 * len0, ((Array[])pch.GetValue(g))[0].Length);
            Assert.Equal(5.5, (double)sigma.GetValue(Chunk0().GetValue(id)));
            Assert.True(last >= 0);
            Assert.Equal(100.0 + last, (double)sigma.GetValue(Chunk0().GetValue(last)));
            Assert.Equal(len0 + 1, (int)wc.GetValue(g));
        }
    }
}
