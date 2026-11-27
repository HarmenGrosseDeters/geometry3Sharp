using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Delaunay3 without SymbolicPerturbation (the default) is unchanged: the triangulations of a cospherical lattice and
    /// of random points, an alpha wrap and a tetrahedralization are pinned bit for bit to the values of master before the
    /// perturbation was added (docs/design/BISECTOR_PREDICATES.md section 3; the hashes were taken on master 20116116).
    /// Only public API, so the test also runs against the base library.
    /// </summary>
    public class Delaunay3PinnedTests
    {
        // FNV-1a over 64-bit words
        sealed class Hash
        {
            public ulong H = 14695981039346656037UL;
            public void Add(long v) { for (int i = 0; i < 8; ++i) { H ^= (ulong)((v >> (8 * i)) & 0xFF); H *= 1099511628211UL; } }
            public void Add(double d) => Add(BitConverter.DoubleToInt64Bits(d));
            public void Add(Vector3d p) { Add(p.x); Add(p.y); Add(p.z); }
        }

        // the cells in id order with their vertices and neighbours as stored (ids, order and adjacency all count)
        static ulong Cells(Delaunay3 dt)
        {
            var h = new Hash();
            h.Add(dt.VertexCount);
            h.Add(dt.CellCount);
            for (int c = 0; c < dt.MaxCellID; ++c) {
                if (!dt.IsCell(c)) { h.Add(-1); continue; }
                for (int i = 0; i < 4; ++i) { h.Add(dt.CellVertex(c, i)); h.Add(dt.Neighbour(c, i)); }
            }
            return h.H;
        }

        static ulong Mesh(DMesh3 m)
        {
            var h = new Hash();
            foreach (int v in m.VertexIndices()) h.Add(m.GetVertex(v));
            foreach (int t in m.TriangleIndices()) { var tr = m.GetTriangle(t); h.Add(tr.a); h.Add(tr.b); h.Add(tr.c); }
            return h.H;
        }

        static ulong Tets(TetMesh3 t)
        {
            var h = new Hash();
            foreach (var p in t.Vertices) h.Add(p);
            foreach (var q in t.Tets) { h.Add(q.a); h.Add(q.b); h.Add(q.c); h.Add(q.d); }
            return h.H;
        }

        internal static ulong[] Compute()
        {
            var lattice = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(5, 5, 5)));
            var rand = new Random(7);
            var pts = new List<Vector3d>();
            for (int i = 1; i <= 4; ++i) for (int j = 1; j <= 4; ++j) for (int k = 1; k <= 4; ++k) pts.Add(new Vector3d(i, j, k));
            foreach (var p in pts.OrderBy(_ => rand.Next())) lattice.Insert(p, -1, 0, out _);
            var random = new Delaunay3(new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)));
            for (int i = 0; i < 2000; ++i) random.Insert(new Vector3d(rand.NextDouble() * 1.8 - 0.9, rand.NextDouble() * 1.8 - 0.9, rand.NextDouble() * 1.8 - 0.9), -1, 1e-3, out _);
            var sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            var wrap = new MeshAlphaWrap(sphere) { RelativeAlpha = 20, RelativeOffset = 300 };
            Assert.True(wrap.Compute());
            var tm = new MeshTetrahedralization(sphere) { RelativeAlpha = 12, RelativeOffset = 200, Improve = false };
            Assert.True(tm.Compute());
            return new[] { Cells(lattice), Cells(random), Mesh(wrap.Result), Tets(tm.Result) };
        }

        [Fact]
        public void WithoutPerturbation_EqualToMaster()
        {
            var got = Compute();
            Assert.Equal(new ulong[] { 14469820202339814898UL, 17741016177304067256UL, 16177413057851189453UL, 6322502901359635903UL }, got);
        }
    }
}
