using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TwoLevelSheetCertificate: the per-level topology certificate of a wall made by TwoLevelMarchingTetrahedra
    /// (docs/design/CONTOURING_BLOCK_CERTIFICATE.md, Theorem 8 and Lemma 9).
    /// </summary>
    public class TwoLevelSheetCertificateTests
    {
        abstract class TestField : IntervalImplicit3d, ImplicitFunction3d
        {
            public abstract IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z);
            public abstract double Value(ref Vector3d p);
            protected static ExactInterval C(double v) { return ExactInterval.Of(v); }
        }

        /// <summary> |p - c| - r </summary>
        sealed class Sphere : TestField
        {
            readonly Vector3d c; readonly double r;
            public Sphere(Vector3d center, double radius) { c = center; r = radius; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval dx = x - C(c.x), dy = y - C(c.y), dz = z - C(c.z);
                ExactInterval rho = ExactInterval.Sqrt(ExactInterval.Square(dx) + ExactInterval.Square(dy) + ExactInterval.Square(dz));
                bool smooth = rho.Lo > 0;
                var whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
                return new IntervalEnclosure { F = rho - C(r), Gx = smooth ? dx / rho : whole, Gy = smooth ? dy / rho : whole, Gz = smooth ? dz / rho : whole, Smooth = smooth };
            }
            public override double Value(ref Vector3d p) { return (p - c).Length - r; }
        }

        /// <summary> (g(K x) + cap (|x|^2 / R^2)^3) / K: a TPMS closed by a smooth cap, scaled to slope about 1 </summary>
        sealed class CappedTpms : TestField
        {
            readonly IntervalTpms tpms; readonly double R, cap;
            public CappedTpms(TpmsType type, double k, double radius, double cap) { tpms = new IntervalTpms(type, k, 0); R = radius; this.cap = cap; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                IntervalEnclosure e = tpms.Enclose(x, y, z);
                ExactInterval r2 = ExactInterval.Square(C(R));
                ExactInterval u = (ExactInterval.Square(x) + ExactInterval.Square(y) + ExactInterval.Square(z)) / r2;
                ExactInterval u2 = ExactInterval.Square(u);
                ExactInterval d = C(6) * C(cap) * u2 / r2;
                ExactInterval k = C(tpms.K);
                e.F = (e.F + C(cap) * (u2 * u)) / k; e.Gx = (e.Gx + d * x) / k; e.Gy = (e.Gy + d * y) / k; e.Gz = (e.Gz + d * z) / k;
                return e;
            }
            public override double Value(ref Vector3d p)
            {
                double u = p.LengthSquared / (R * R);
                return (TpmsLattice.Evaluate(tpms.Type, tpms.K * p.x, tpms.K * p.y, tpms.K * p.z) + cap * u * u * u) / tpms.K;
            }
        }

        static readonly Vector3d Shift = new Vector3d(-1.113, -1.107, -1.103);

        static TwoLevelMarchingTetrahedra Mesh(TestField f, double halfWidth, Vector3d origin, double size, int n, double guard = 1.0 / 256)
        {
            var gen = new TwoLevelMarchingTetrahedra { Field = f, HalfWidth = halfWidth, CellSize = size / n, Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One),
                SliverGuard = guard, RecordCertificateData = true };
            gen.Generate();
            return gen;
        }

        static TwoLevelSheetCertificate Certify(TestField f, TwoLevelMarchingTetrahedra gen)
        {
            var cert = new TwoLevelSheetCertificate { Field = f, Mesher = gen };
            cert.Compute();
            return cert;
        }

        static string Summary(TwoLevelSheetCertificate c)
        {
            return c.Certified + "|" + c.RunFailure + "|surface " + string.Join(",", c.SurfaceCells) + "|alone " + string.Join(",", c.CellsCertifiedAlone) + "|in blocks " + string.Join(",", c.CellsInBlocks)
                + "|blocks " + string.Join(",", c.Blocks) + "|left " + c.UncertifiedCells.Count + ":" + string.Join(" ", c.UncertifiedCells.GroupBy(u => u.Level + "" + u.Reason).Select(g => g.Key + "x" + g.Count()));
        }

        // the blocks are boxes with disjoint interiors, and a cube is "in a block" exactly if it lies in one
        static void CheckBlocks(TwoLevelSheetCertificate c, int nx, int ny, int nz)
        {
            for (int level = 0; level < 2; ++level) {
                Assert.Equal(c.Blocks[level], c.BlockBoxes[level].Count);
                var covered = new int[nx * ny * nz];
                foreach (int[] b in c.BlockBoxes[level]) {
                    Assert.True(b[0] >= 1 && b[1] >= 1 && b[2] >= 1 && b[3] <= nx - 1 && b[4] <= ny - 1 && b[5] <= nz - 1 && b[3] > b[0] && b[4] > b[1] && b[5] > b[2]);
                    for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) ++covered[i + nx * (j + ny * k)];
                }
                for (int cell = 0; cell < covered.Length; ++cell) Assert.Equal(c.CellStates[level][cell] == 3 ? 1 : 0, covered[cell]);
                Assert.Equal(c.CellsInBlocks[level], covered.Sum());
            }
        }

        static string List(TwoLevelSheetCertificate c)
        {
            return Summary(c) + "|" + string.Join(";", c.UncertifiedCells.Select(u => u.Level + ":" + u.Cell.x + "," + u.Cell.y + "," + u.Cell.z + ":" + u.Reason))
                + "|" + string.Join("/", c.CellStates.Select(a => a == null ? "-" : string.Join("", a)))
                + "|" + string.Join("/", c.BlockBoxes.Select(l => string.Join(";", l.Select(b => string.Join(",", b)))));
        }

        static string Signature(DMesh3 mesh)
        {
            var t = gs.MeshTopologySummary.Compute(mesh);
            return "c=" + t.Components + ";g=" + string.Join("+", t.ComponentList.Select(x => x.Genus).OrderBy(g => g));
        }

        // The oracle of the level-set side, by sampling: in every cube that is zero-free the level keeps its sign at 27 points;
        // on every edge of a cube certified on its own the level changes sign at most once along 17 points, and exactly if the
        // mesher's signs at its ends differ.
        static int OracleViolations(TestField f, TwoLevelMarchingTetrahedra gen, TwoLevelSheetCertificate c)
        {
            var r = gen.Certificate;
            int nx = r.Nodes.x - 1, ny = r.Nodes.y - 1, nz = r.Nodes.z - 1, bad = 0;
            Func<int, int, double> at = (ax, i) => r.Origin[ax] + r.CellSize * i;
            for (int level = 0; level < 2; ++level) {
                Func<Vector3d, double> value = p => level == 0 ? f.Value(ref p) - r.HalfWidth : -f.Value(ref p) - r.HalfWidth;
                for (int k = 0; k < nz; ++k) for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) {
                    byte state = c.CellStates[level][i + nx * (j + ny * k)];
                    if (state == 0) {
                        int sign = 0;
                        for (int a = 0; a <= 2; ++a) for (int b = 0; b <= 2; ++b) for (int d = 0; d <= 2; ++d) {
                            double v = value(new Vector3d(at(0, i) + r.CellSize * a / 2, at(1, j) + r.CellSize * b / 2, at(2, k) + r.CellSize * d / 2));
                            int s = Math.Sign(v);
                            if (s == 0 || (sign != 0 && s != sign)) ++bad;
                            sign = s;
                        }
                    } else if (state == 1) {
                        for (int axis = 0; axis < 3; ++axis) for (int e = 0; e < 4; ++e) {
                            int[] n = { i, j, k };
                            n[(axis + 1) % 3] += e & 1; n[(axis + 2) % 3] += e >> 1;
                            int changes = 0, last = 0;
                            for (int q = 0; q <= 16; ++q) {
                                Vector3d p = new Vector3d(at(0, n[0]), at(1, n[1]), at(2, n[2]));
                                p[axis] += r.CellSize * q / 16;
                                int s = Math.Sign(value(p));
                                if (last != 0 && s != last) ++changes;
                                last = s;
                            }
                            int[] m = (int[])n.Clone(); m[axis]++;
                            int f0 = r.NodeFlags[n[0] + r.Nodes.x * (n[1] + r.Nodes.y * n[2])] >> level & 1, f1 = r.NodeFlags[m[0] + r.Nodes.x * (m[1] + r.Nodes.y * m[2])] >> level & 1;
                            if (changes > 1 || (changes == 1) != (f0 != f1)) ++bad;
                        }
                    }
                }
            }
            return bad;
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void ASphereWall_IsCertified_OnBothLevels(double cells)
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            TwoLevelMarchingTetrahedra gen = Mesh(f, cells * 2.2 / 24 / 2, Shift, 2.2, 24);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.True(c.Certified, Summary(c));
            Assert.Equal(SheetCertificateRunFailure.None, c.RunFailure);
            Assert.Empty(c.UncertifiedCells);
            Assert.True(c.SurfaceCells[0] > 500 && c.SurfaceCells[1] > 500);
            for (int level = 0; level < 2; ++level) {
                Assert.Equal(c.CellsCertifiedAlone[level], c.CellStates[level].Count(s => s == 1));
                Assert.Equal(c.CellsInBlocks[level], c.CellStates[level].Count(s => s == 3));
                Assert.True(c.SurfaceCells[level] >= c.CellsCertifiedAlone[level] && c.SurfaceCells[level] <= c.CellsCertifiedAlone[level] + c.CellsInBlocks[level]);
            }
            Assert.Equal("c=2;g=0+0", Signature(gen.Mesh));
            Assert.Equal(0, OracleViolations(f, gen, c));
            CheckBlocks(c, 24, 24, 24);
        }

        // cubes in blocks that were zero-free or certified before the block took them in are counted as in the block: the
        // count of surface cubes is those certified alone plus the surface cubes in blocks (here: at least the alone ones)

        [Fact]
        public void AGyroidWall_IsCertified_AndNeedsBlocks()
        {
            var f = new CappedTpms(TpmsType.Gyroid, 2 * Math.PI / 2.2, 0.9, 4);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * 2.2 / 32 / 2, Shift, 2.2, 32);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.True(c.Certified, Summary(c));
            Assert.True(c.Blocks[0] > 3 && c.Blocks[1] > 3, Summary(c));
            Assert.True(c.CellsInBlocks[0] > 0 && c.CellsCertifiedAlone[0] > 1000);
            Assert.Equal(0, OracleViolations(f, gen, c));
            CheckBlocks(c, 32, 32, 32);
            // blocks that met were merged: some block is wider than the two cubes a single seed needs
            Assert.Contains(c.BlockBoxes[0].Concat(c.BlockBoxes[1]), b => (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]) > 8);
            // without blocks the same run is not certified: the cubes of the blocks are left, for an edge or a face
            var alone = new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxBlockSide = 1 };
            alone.Compute();
            Assert.False(alone.Certified);
            Assert.Equal(SheetCertificateRunFailure.None, alone.RunFailure);
            Assert.NotEmpty(alone.UncertifiedCells);
            Assert.All(alone.UncertifiedCells, u => Assert.True(u.Reason == SheetCertificateReason.Edge || u.Reason == SheetCertificateReason.Face || u.Reason == SheetCertificateReason.BlockLimit
                || u.Reason == SheetCertificateReason.Direction || u.Reason == SheetCertificateReason.Circles, u.Reason.ToString()));
        }

        // The mesh of another field: the signs differ where the two level sets differ, and nothing there is certified.
        [Fact]
        public void TheMeshOfAnotherField_IsNotCertified()
        {
            var meshed = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            var claimed = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7 + 2.5 * 2.2 / 24);
            TwoLevelMarchingTetrahedra gen = Mesh(meshed, 0.5 * 2.2 / 24 / 2, Shift, 2.2, 24);
            TwoLevelSheetCertificate c = Certify(claimed, gen);
            Assert.False(c.Certified);
            Assert.True(c.UncertifiedCells.Count > 500, Summary(c));
            // every cube that the claimed level sets may meet has a node with another sign in the mesh or lies beside one, and
            // no block is free of them
            Assert.Equal(0, c.CellsCertifiedAlone[0] + c.CellsCertifiedAlone[1] + c.CellsInBlocks[0] + c.CellsInBlocks[1]);
            Assert.Contains(c.UncertifiedCells, u => u.Level == 0);
            Assert.Contains(c.UncertifiedCells, u => u.Level == 1);
        }

        // A level set that is closed inside one cube (a sphere of a third of a cell): the mesh does not see it, and the cube is
        // refused for the direction (a closed surface is no graph), whatever the block.
        [Fact]
        public void ALevelSetClosedInsideACube_IsNotCertified()
        {
            const double h = 2.2 / 16;
            Vector3d centre = Shift + h * new Vector3d(7.5, 7.5, 7.5);
            var f = new Sphere(centre, 0.3 * h);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.1 * h, Shift, 2.2, 16);
            Assert.Equal(0, gen.Mesh.TriangleCount);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.False(c.Certified);
            Assert.Contains(c.UncertifiedCells, u => u.Cell == new Vector3i(7, 7, 7) && u.Reason == SheetCertificateReason.Direction);
            Assert.All(c.UncertifiedCells, u => Assert.Equal(new Vector3i(7, 7, 7), u.Cell));
            Assert.Equal(2, c.UncertifiedCells.Count);
        }

        /// <summary> (|p - c| - R)^2 - t^2: two concentric spheres at R - t and R + t </summary>
        sealed class Shell : TestField
        {
            readonly Vector3d c; readonly double R, t;
            public Shell(Vector3d centre, double radius, double halfGap) { c = centre; R = radius; t = halfGap; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval dx = x - C(c.x), dy = y - C(c.y), dz = z - C(c.z);
                ExactInterval rho = ExactInterval.Sqrt(ExactInterval.Square(dx) + ExactInterval.Square(dy) + ExactInterval.Square(dz));
                ExactInterval s = rho - C(R);
                var whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
                bool smooth = rho.Lo > 0;
                ExactInterval two = C(2) * s;
                return new IntervalEnclosure { F = ExactInterval.Square(s) - ExactInterval.Square(C(t)), Gx = smooth ? two * dx / rho : whole, Gy = smooth ? two * dy / rho : whole, Gz = smooth ? two * dz / rho : whole, Smooth = smooth };
            }
            public override double Value(ref Vector3d p) { double s = (p - c).Length - R; return s * s - t * t; }
        }

        // Two sheets a fifth of a cell apart: the level g = +w is two spheres that most grid edges cross twice or not at all.
        // The mesh has almost nothing of them, and no block of the allowed size separates the sheets.
        [Fact]
        public void TwoSheetsThroughACube_AreNotCertified()
        {
            const double h = 2.2 / 16;
            var f = new Shell(new Vector3d(0.03, -0.02, 0.01), 0.6, 0.1 * h);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.002 * h * h, Shift, 2.2, 16);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.False(c.Certified);
            Assert.True(c.UncertifiedCells.Count(u => u.Level == 0) > 200, Summary(c));
            Assert.Contains(c.UncertifiedCells, u => u.Reason == SheetCertificateReason.Edge || u.Reason == SheetCertificateReason.BlockLimit);
        }

        [Fact]
        public void RunLevelRefusals()
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            const double w = 0.5 * 2.2 / 16 / 2;
            Func<TwoLevelMarchingTetrahedra> fresh = () => Mesh(f, w, Shift, 2.2, 16);
            Assert.True(Certify(f, fresh()).Certified);

            // no record: not asked for, a domain, a cancelled run, a mesh that is not the recorded one
            TwoLevelMarchingTetrahedra gen = fresh();
            gen.RecordCertificateData = false; gen.Generate();
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh();
            gen.Domain = new Sphere(Vector3d.Zero, 0.9); gen.Generate();
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh();
            gen.Mesh.AppendVertex(Vector3d.Zero);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh();
            gen.Mesh.RemoveTriangle(0, false, false);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            // the record's lists against the mesh, one by one
            gen = fresh(); gen.Certificate.TriangleLevel.Add(0);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh(); gen.Certificate.VertexA.Add(0);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh(); gen.Certificate.VertexB.Add(1);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh(); gen.Certificate.VertexLevel.Add(0);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            // a vertex removed from the mesh, with lists that still have the mesh's highest id
            gen = fresh();
            int extra = gen.Mesh.AppendVertex(Vector3d.Zero);
            gen.Certificate.VertexA.Add(0); gen.Certificate.VertexB.Add(1); gen.Certificate.VertexLevel.Add(0);
            gen.Mesh.RemoveVertex(extra, false, false);
            Assert.Equal(gen.Mesh.MaxVertexID, gen.Certificate.VertexA.Count);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, Certify(f, gen).RunFailure);
            gen = fresh();
            gen.Mesh = null;
            TwoLevelSheetCertificate none = Certify(f, gen);
            Assert.False(none.Certified);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, none.RunFailure);
            Assert.Empty(none.UncertifiedCells);

            // a half width that varies
            gen = new TwoLevelMarchingTetrahedra { Field = f, HalfWidthF = p => w * (1 + 0.1 * Math.Sin(p.x)), CellSize = 2.2 / 16, Bounds = new AxisAlignedBox3d(Shift, Shift + 2.2 * Vector3d.One), RecordCertificateData = true };
            gen.Generate();
            Assert.Equal(SheetCertificateRunFailure.VariableHalfWidth, Certify(f, gen).RunFailure);

            // the mesh side: a vertex moved onto a node of its edge, a vertex moved off its edge, a node value of exactly 0
            gen = fresh();
            var r = gen.Certificate;
            Func<int, Vector3d> node = id => new Vector3d(r.Origin.x + r.CellSize * (id % r.Nodes.x), r.Origin.y + r.CellSize * (id / r.Nodes.x % r.Nodes.y), r.Origin.z + r.CellSize * (id / (r.Nodes.x * r.Nodes.y)));
            Vector3d kept = gen.Mesh.GetVertex(5);
            gen.Mesh.SetVertex(5, node(r.VertexA[5]));
            TwoLevelSheetCertificate moved = Certify(f, gen);
            Assert.False(moved.Certified);
            Assert.True((moved.RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            gen.Mesh.SetVertex(5, node(r.VertexB[5]));
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            gen.Mesh.SetVertex(5, kept + new Vector3d(1e-9, 1e-9, 1e-9));
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            gen.Mesh.SetVertex(5, kept);
            Assert.True(Certify(f, gen).Certified);
            // a vertex without an edge, with its nodes in the wrong order, without a level
            int a5 = r.VertexA[5], b5 = r.VertexB[5];
            r.VertexA[5] = -1; r.VertexB[5] = -1;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.VertexA[5] = b5; r.VertexB[5] = a5;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.VertexA[5] = a5; r.VertexB[5] = a5;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.VertexB[5] = b5; r.VertexLevel[5] = 255;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.VertexLevel[5] = 0;
            Assert.True(Certify(f, gen).Certified);
            r.NodeFlags[0] |= 16;
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            r.NodeFlags[0] &= 0xEF;
            r.NodeFlags[r.NodeFlags.Length - 1] |= 32;
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            r.NodeFlags[r.NodeFlags.Length - 1] &= 0xDF;
            // a node where neither level is negative (no sampled field has one): the mesher skips its tetrahedra unseen
            byte flags0 = r.NodeFlags[0];
            r.NodeFlags[0] = 0;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.NodeFlags[0] = flags0;
            Assert.True(Certify(f, gen).Certified);
            // the two counters of the record
            r.ClippedPolygonVertices = 1;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.ClippedPolygonVertices = 0; r.NonManifoldTriangles = 1;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.NonManifoldTriangles = 0;
            r.TriangleLevel[3] = 255;
            Assert.Equal(SheetCertificateRunFailure.MeshSide, Certify(f, gen).RunFailure);
            r.TriangleLevel[3] = 0;
            Assert.True(Certify(f, gen).Certified);

            // arguments
            Assert.Throws<ArgumentNullException>(() => new TwoLevelSheetCertificate { Mesher = gen }.Compute());
            Assert.Throws<ArgumentNullException>(() => new TwoLevelSheetCertificate { Field = f }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxTightening = -1 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxTightening = 5 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxBlockSide = 0 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxBlockSide = 65 }.Compute());
        }

        // A mesh with a self-intersection that keeps every vertex strictly on its edge: a vertex of a face diagonal moved
        // within its face, across the triangles around it.
        [Fact]
        public void ASelfIntersectingMesh_IsNotCertified()
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * 2.2 / 16 / 2, Shift, 2.2, 16);
            var r = gen.Certificate;
            int found = 0;
            for (int vid = 0; vid < gen.Mesh.MaxVertexID && found == 0; ++vid) {
                int a = r.VertexA[vid], b = r.VertexB[vid];
                Vector3i na = new Vector3i(a % r.Nodes.x, a / r.Nodes.x % r.Nodes.y, a / (r.Nodes.x * r.Nodes.y)), nb = new Vector3i(b % r.Nodes.x, b / r.Nodes.x % r.Nodes.y, b / (r.Nodes.x * r.Nodes.y));
                Vector3i d = nb - na;
                if (Math.Abs(d.x) + Math.Abs(d.y) + Math.Abs(d.z) != 2) continue;      // a face diagonal
                Vector3d kept = gen.Mesh.GetVertex(vid), v = kept;
                // to the far corner region of its face: still strictly inside the face's rectangle
                for (int ax = 0; ax < 3; ++ax) if (d[ax] != 0) {
                    double lo = r.Origin[ax] + r.CellSize * Math.Min(na[ax], nb[ax]);
                    v[ax] = lo + r.CellSize * (v[ax] - lo < r.CellSize / 2 ? 0.98 : 0.02);
                }
                gen.Mesh.SetVertex(vid, v);
                if (gs.MeshRepairMetrics.HasSelfIntersections(gen.Mesh)) {
                    ++found;
                    TwoLevelSheetCertificate c = Certify(f, gen);
                    Assert.False(c.Certified);
                    Assert.Equal(SheetCertificateRunFailure.SelfIntersections, c.RunFailure);
                }
                gen.Mesh.SetVertex(vid, kept);
            }
            Assert.Equal(1, found);
            Assert.True(Certify(f, gen).Certified);
        }

        // A wall that reaches the outermost layer of cubes: the level set may leave the grid.
        [Fact]
        public void ALevelSetInTheOutermostLayer_IsNotCertified()
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 1.05);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * 2.2 / 16 / 2, Shift, 2.2, 16);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.False(c.Certified);
            Assert.True((c.RunFailure & SheetCertificateRunFailure.OuterLayer) != 0);
            Assert.Contains(c.UncertifiedCells, u => u.Reason == SheetCertificateReason.OuterLayer);
            Assert.All(c.UncertifiedCells.Where(u => u.Reason == SheetCertificateReason.OuterLayer), u =>
                Assert.True(u.Cell.x == 0 || u.Cell.y == 0 || u.Cell.z == 0 || u.Cell.x == 15 || u.Cell.y == 15 || u.Cell.z == 15));
            // no cube of the outermost layer is certified or taken into a block
            for (int k = 0; k < 16; ++k) for (int j = 0; j < 16; ++j) for (int i = 0; i < 16; ++i)
                if (i == 0 || j == 0 || k == 0 || i == 15 || j == 15 || k == 15)
                    for (int level = 0; level < 2; ++level) Assert.True(c.CellStates[level][i + 16 * (j + 16 * k)] == 0 || c.CellStates[level][i + 16 * (j + 16 * k)] == 2);
            CheckBlocks(c, 16, 16, 16);
            // a sphere that reaches one side only, the high one of each axis in turn
            for (int axis = 0; axis < 3; ++axis) {
                Vector3d centre = new Vector3d(0.03, -0.02, 0.01);
                centre[axis] = 0.3;
                var g = new Sphere(centre, 0.74);
                TwoLevelSheetCertificate one = Certify(g, Mesh(g, 0.5 * 2.2 / 16 / 2, Shift, 2.2, 16));
                Assert.True((one.RunFailure & SheetCertificateRunFailure.OuterLayer) != 0, "axis " + axis);
                Assert.NotEmpty(one.UncertifiedCells);
                Assert.All(one.UncertifiedCells.Where(u => u.Reason == SheetCertificateReason.OuterLayer), u => Assert.Equal(15, u.Cell[axis]));
                Assert.Contains(one.UncertifiedCells, u => u.Reason == SheetCertificateReason.OuterLayer);
            }
        }

        // One node far from the wall with the wrong sign in the mesher (through its seam): the mesh has a closed piece around
        // it that the level set does not have. The cubes at the node are refused, and no block takes them in.
        [Fact]
        public void ANodeWithTheWrongSign_IsNotCertified_AlsoNotInABlock()
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            var gen = new TwoLevelMarchingTetrahedra { Field = f, HalfWidth = 0.5 * 2.2 / 16 / 2, CellSize = 2.2 / 16, Bounds = new AxisAlignedBox3d(Shift, Shift + 2.2 * Vector3d.One), RecordCertificateData = true };
            int node = 2 + 17 * (2 + 17 * 2);           // near a corner of the grid, outside the sphere: g - w > 0 and -g - w < 0 there
            gen.NodeValuesForTest = v => { Assert.True(v[0][node] > 0 && v[1][node] < 0); v[0][node] = -v[0][node]; };
            gen.Generate();
            Assert.Equal("c=3;g=0+0+0", Signature(gen.Mesh));
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.False(c.Certified);
            Assert.Equal(SheetCertificateRunFailure.None, c.RunFailure);
            Assert.Equal(8, c.UncertifiedCells.Count);
            Assert.All(c.UncertifiedCells, u => {
                Assert.Equal(0, u.Level);
                Assert.True(u.Cell.x >= 1 && u.Cell.x <= 2 && u.Cell.y >= 1 && u.Cell.y <= 2 && u.Cell.z >= 1 && u.Cell.z <= 2);
                Assert.True(u.Reason == SheetCertificateReason.NodeSign || u.Reason == SheetCertificateReason.MeshInBlock, u.Reason.ToString());
            });
            Assert.Contains(c.UncertifiedCells, u => u.Reason == SheetCertificateReason.MeshInBlock);
        }

        // ---- (S1) and (S2) on their own ----

        const double H = 2.2 / 16;
        static double Node(int axis, int i) { return Shift[axis] + H * i; }

        // An x-edge that a sphere cuts twice, with a short chord (inside one piece of the edge, where the derivative has no
        // sign) and a long one (in different pieces, with certified opposite signs): refused. An edge it crosses once, and an
        // edge it does not meet: accepted.
        [Theory]
        [InlineData(0.05)]
        [InlineData(0.3)]
        public void AnEdgeCrossedTwice_Fails_S1(double halfChordInCells)
        {
            const double w = 0.05 * H;
            double d = 3 * H, c = halfChordInCells * H;
            var centre = new Vector3d(Node(0, 8) + H / 2, Node(1, 6) + d, Node(2, 7));
            var f = new Sphere(centre, Math.Sqrt(d * d + c * c) - w);          // the level g = w is the sphere of radius sqrt(d^2 + c^2)
            TwoLevelMarchingTetrahedra gen = Mesh(f, w, Shift, 2.2, 16);
            TwoLevelSheetCertificate cert = Certify(f, gen);
            // the edge from node (8, 6, 7) along x: both ends outside, the chord around its middle
            Vector3d a = new Vector3d(Node(0, 8), Node(1, 6), Node(2, 7)), mid = a + new Vector3d(H / 2, 0, 0);
            Assert.True(f.Value(ref a) - w > 0 && f.Value(ref mid) - w < 0);
            Assert.False(cert.EdgePasses(0, 0, 8, 6, 7));
            // the edge from node (8, 6, 7) along y is crossed once
            Vector3d b = a + new Vector3d(0, H, 0);
            Assert.True(f.Value(ref a) - w > 0 && f.Value(ref b) - w < 0);
            Assert.True(cert.EdgePasses(0, 1, 8, 6, 7));
            // far away: no zero
            Assert.True(cert.EdgePasses(0, 0, 2, 2, 2));
            Assert.True(cert.EdgePasses(0, 2, 2, 2, 2));
            // the other level (the sphere of radius below d) does not reach the first edge
            Assert.True(cert.EdgePasses(1, 0, 8, 6, 7));
            // the four cubes at the edge are certified in a block, not on their own, and the run is certified
            Assert.True(cert.Certified, Summary(cert));
            foreach (int j in new[] { 5, 6 }) foreach (int k in new[] { 6, 7 }) Assert.Equal(3, cert.CellStates[0][8 + 16 * (j + 16 * k)]);
            Assert.Equal(0, OracleViolations(f, gen, cert));
        }

        // The top of a sphere a hundredth of a cell above a grid plane: the level set meets one face in a small closed curve.
        // The face is refused (no direction in its plane; the direction along its normal does not count), and the two cubes
        // at it are certified in a block.
        [Fact]
        public void AClosedCurveInAFace_Fails_S2()
        {
            const double w = 0.05 * H, radius = 5 * H;
            var centre = new Vector3d(Node(0, 8) + H / 2, Node(1, 8) + H / 2, Node(2, 12) + 0.01 * H - radius);
            var f = new Sphere(centre, radius - w);
            TwoLevelMarchingTetrahedra gen = Mesh(f, w, Shift, 2.2, 16);
            TwoLevelSheetCertificate cert = Certify(f, gen);
            Assert.False(cert.FacePasses(0, 2, 8, 8, 12));
            // a face of the same plane without a zero, and a face two layers below that the sphere crosses once
            Assert.True(cert.FacePasses(0, 2, 2, 2, 12));
            Assert.True(cert.FacePasses(0, 2, 12, 8, 10));
            Assert.True(cert.FacePasses(0, 0, 13, 8, 9) || cert.FacePasses(0, 0, 12, 8, 9));
            Assert.True(cert.Certified, Summary(cert));
            Assert.Equal(3, cert.CellStates[0][8 + 16 * (8 + 16 * 11)]);
            Assert.Equal(3, cert.CellStates[0][8 + 16 * (8 + 16 * 12)]);
            Assert.Equal("c=2;g=0+0", Signature(gen.Mesh));
        }

        /// <summary>
        /// h (eps (u + v) + 0.5 - (u - v)^2) + kappa (z - z0) with u = (x - x0) / h, v = (y - y0) / h: on the unit face at
        /// (x0, y0, z0) the corner signs alternate (positive at u = v), and the function grows strictly along (1, 1, 0).
        /// </summary>
        sealed class Saddle : TestField
        {
            readonly Vector3d o; readonly double h, eps, kappa;
            public Saddle(Vector3d corner, double h, double eps, double kappa) { o = corner; this.h = h; this.eps = eps; this.kappa = kappa; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval u = (x - C(o.x)) / C(h), v = (y - C(o.y)) / C(h), s = u - v;
                return new IntervalEnclosure { F = C(h) * (C(eps) * (u + v) + C(0.5) - ExactInterval.Square(s)) + C(kappa) * (z - C(o.z)),
                    Gx = C(eps) - C(2) * s, Gy = C(eps) + C(2) * s, Gz = C(kappa), Smooth = true };
            }
            public override double Value(ref Vector3d p)
            {
                double u = (p.x - o.x) / h, v = (p.y - o.y) / h;
                return h * (eps * (u + v) + 0.5 - (u - v) * (u - v)) + kappa * (p.z - o.z);
            }
        }

        // A face with alternating signs whose level set runs beside the diagonal between its two outside corners. Where that
        // is the diagonal of the mesher's split (the face's lowest node has an even index sum) the mesh pairs the crossings
        // as the level set does and the face passes; one node further the split has the other diagonal, the level set
        // crosses it, and the face is refused.
        [Theory]
        [InlineData(4, true)]
        [InlineData(5, false)]
        public void AFaceWithAlternatingSigns_PassesOnlyWithTheSplitsDiagonal(int i, bool passes)
        {
            const double h = 2.2 / 8, w = 0.01 * h;
            var corner = new Vector3d(Shift.x + h * i, Shift.y + h * 4, Shift.z + h * 4);
            var f = new Saddle(corner, h, 0.4, 0.3);
            TwoLevelMarchingTetrahedra gen = Mesh(f, w, Shift, 2.2, 8);
            var r = gen.Certificate;
            Func<int, int, int> inside = (di, dj) => r.NodeFlags[(i + di) + 9 * ((4 + dj) + 9 * 4)] & 1;
            Assert.True(inside(0, 0) == 0 && inside(1, 1) == 0 && inside(1, 0) == 1 && inside(0, 1) == 1);
            var cert = new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxTightening = 4, MaxBlockSide = 1 };
            cert.Compute();
            Assert.Equal(passes, cert.FacePasses(0, 2, i, 4, 4));
            // with less tightening the direction along the diagonal is not certified, and the face fails in both positions
            var coarse = new TwoLevelSheetCertificate { Field = f, Mesher = gen, MaxTightening = 1, MaxBlockSide = 1 };
            coarse.Compute();
            Assert.False(coarse.FacePasses(0, 2, i, 4, 4));
            // where the face passes, the cube above it has everything but one closed curve: the level set cuts off its two inside
            // corners separately, as the mesh does
            if (passes) {
                Assert.Equal(2, TwoLevelSheetCertificate.Circles(r, 0, new[] { i, 4, 4, i + 1, 5, 5 }));
                Assert.Contains(cert.UncertifiedCells, u => u.Level == 0 && u.Cell == new Vector3i(i, 4, 4) && u.Reason == SheetCertificateReason.Circles);
            }
            // neither cube at a refused face is certified on its own
            if (!passes) {
                Assert.NotEqual(1, cert.CellStates[0][i + 8 * (4 + 8 * 4)]);
                Assert.NotEqual(1, cert.CellStates[0][i + 8 * (4 + 8 * 3)]);
            }
        }

        /// <summary> x - c, with a gradient enclosure of the whole line on boxes that lie beyond x = c + margin (a valid, useless enclosure where the level has no zero) </summary>
        sealed class PlaneWithUselessGradientsBeyond : TestField
        {
            readonly double c, margin;
            public PlaneWithUselessGradientsBeyond(double c, double margin) { this.c = c; this.margin = margin; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                var whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
                bool beyond = x.Lo > c + margin;
                return new IntervalEnclosure { F = x - C(c), Gx = beyond ? whole : C(1), Gy = beyond ? whole : C(0), Gz = beyond ? whole : C(0), Smooth = !beyond };
            }
            public override double Value(ref Vector3d p) { return p.x - c; }
        }

        // The direction is asked only of the sub-boxes that may hold a zero (remark 9 of the note): a cube that the level set
        // crosses passes although the gradient is not known on its far part.
        [Fact]
        public void TheDirection_IsAskedOnlyWhereTheLevelMayBeZero()
        {
            const double w = 0.02 * H;
            var f = new PlaneWithUselessGradientsBeyond(Node(0, 8) + 0.2 * H, 0.3 * H);
            TwoLevelMarchingTetrahedra gen = Mesh(f, w, Shift, 2.2, 16);
            TwoLevelSheetCertificate cert = Certify(f, gen);
            // the planes x = c + w and x = c - w cross the cubes of column 8; they leave the grid, so the run is not certified,
            // but every inner cube of the column is certified on its own, on both levels
            Assert.False(cert.Certified);
            Assert.True((cert.RunFailure & SheetCertificateRunFailure.OuterLayer) != 0);
            for (int level = 0; level < 2; ++level)
                for (int k = 1; k < 15; ++k) for (int j = 1; j < 15; ++j) Assert.Equal(1, cert.CellStates[level][8 + 16 * (j + 16 * k)]);
            Assert.All(cert.UncertifiedCells, u => Assert.Equal(SheetCertificateReason.OuterLayer, u.Reason));
        }

        [Fact]
        public void MayBeZero_IsTrueUnlessASignIsCertified()
        {
            Func<double, double, ExactInterval> I = (lo, hi) => new ExactInterval(lo, hi);
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(-1, 1)));
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(0, 1)));
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(-1, 0)));
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(0, 0)));
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(double.NaN, double.NaN)));
            Assert.True(TwoLevelSheetCertificate.MayBeZero(I(double.NegativeInfinity, double.PositiveInfinity)));
            Assert.False(TwoLevelSheetCertificate.MayBeZero(I(double.Epsilon, 1)));
            Assert.False(TwoLevelSheetCertificate.MayBeZero(I(-1, -double.Epsilon)));
        }

        // A grid whose coordinates do not separate the nodes (cubes below the rounding unit of the origin): refused as a run.
        [Fact]
        public void AGridWithoutIncreasingCoordinates_IsRefused()
        {
            var f = new Sphere(new Vector3d(1e17, 1e17, 1e17), 8);
            var gen = new TwoLevelMarchingTetrahedra { Field = f, HalfWidth = 1, CellSize = 3, Bounds = new AxisAlignedBox3d(new Vector3d(1e17 - 30, 1e17 - 30, 1e17 - 30), new Vector3d(1e17 + 30, 1e17 + 30, 1e17 + 30)),
                RecordCertificateData = true };
            gen.Generate();
            Assert.NotNull(gen.Certificate);
            TwoLevelSheetCertificate cert = Certify(f, gen);
            Assert.False(cert.Certified);
            Assert.Equal(SheetCertificateRunFailure.NoRecord, cert.RunFailure);
        }

        /// <summary> a field with an enclosure of its value that cannot decide the sign on every box that contains one point (a valid, weak enclosure) </summary>
        sealed class UncertainAt : TestField
        {
            readonly TestField inner; readonly Vector3d[] points;
            public UncertainAt(TestField inner, params Vector3d[] points) { this.inner = inner; this.points = points; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                IntervalEnclosure e = inner.Enclose(x, y, z);
                foreach (Vector3d p in points)
                    if (x.Lo <= p.x && p.x <= x.Hi && y.Lo <= p.y && p.y <= y.Hi && z.Lo <= p.z && p.z <= z.Hi) e.F = new ExactInterval(-1e30, 1e30);
                return e;
            }
            public override double Value(ref Vector3d q) { return inner.Value(ref q); }
        }

        // A node whose sign the enclosure cannot certify, far outside the wall: the eight cubes at it are not certified on
        // their own (the mesher's sign there is not confirmed), but in a block that has the node inside.
        [Fact]
        public void ANodeWithoutACertifiedSign_IsOnlyCertifiedInsideABlock()
        {
            var sphere = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.6);
            var f = new UncertainAt(sphere, new Vector3d(Node(0, 3), Node(1, 3), Node(2, 3)));
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * H / 2, Shift, 2.2, 16);
            TwoLevelSheetCertificate cert = Certify(f, gen);
            Assert.True(cert.Certified, Summary(cert));
            for (int level = 0; level < 2; ++level) {
                for (int c = 0; c < 8; ++c) Assert.Equal(3, cert.CellStates[level][(2 + (c & 1)) + 16 * ((2 + (c >> 1 & 1)) + 16 * (2 + (c >> 2 & 1)))]);
                Assert.Contains(cert.BlockBoxes[level], b => b[0] == 2 && b[1] == 2 && b[2] == 2 && b[3] == 4 && b[4] == 4 && b[5] == 4);
            }
            CheckBlocks(cert, 16, 16, 16);
        }

        static Vector3d NodePoint(int i, int j, int k) { return new Vector3d(Node(0, i), Node(1, j), Node(2, k)); }

        // Two blocks that come to overlap are one block: the block around the nodes (3,3,3) and (4,4,3) grows into the
        // block around (5,2,2), which was made before it.
        [Fact]
        public void BlocksThatOverlap_AreMerged()
        {
            var sphere = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.6);
            var f = new UncertainAt(sphere, NodePoint(5, 2, 2), NodePoint(3, 3, 3), NodePoint(4, 4, 3));
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * H / 2, Shift, 2.2, 16);
            var cert = new TwoLevelSheetCertificate { Field = f, Mesher = gen };
            cert.Compute();
            Assert.True(cert.Certified, Summary(cert));
            CheckBlocks(cert, 16, 16, 16);
            for (int level = 0; level < 2; ++level)
                Assert.Contains(cert.BlockBoxes[level], b => string.Join(",", b) == "2,1,1,6,5,4");
            // the same again on the instance, and nothing of it after a refused run
            string first = List(cert);
            cert.Compute();
            Assert.Equal(first, List(cert));
            cert.Mesher = new TwoLevelMarchingTetrahedra();
            cert.Compute();
            Assert.Equal(SheetCertificateRunFailure.NoRecord, cert.RunFailure);
            Assert.Empty(cert.BlockBoxes[0]);
            Assert.Empty(cert.BlockBoxes[1]);
        }

        // A block does not grow into the outermost layer: a node without a certified sign on the inner side of that layer
        // leaves the cube inside of it uncertified.
        [Fact]
        public void ABlock_DoesNotGrowIntoTheOutermostLayer()
        {
            var sphere = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.6);
            var f = new UncertainAt(sphere, NodePoint(1, 1, 1));
            TwoLevelSheetCertificate cert = Certify(f, Mesh(f, 0.5 * H / 2, Shift, 2.2, 16));
            Assert.False(cert.Certified);
            CheckBlocks(cert, 16, 16, 16);
            for (int level = 0; level < 2; ++level) {
                Assert.Equal(2, cert.CellStates[level][1 + 16 * (1 + 16 * 1)]);
                Assert.Contains(cert.UncertifiedCells, u => u.Level == level && u.Cell == new Vector3i(1, 1, 1) && u.Reason == SheetCertificateReason.BlockLimit);
            }
            // at the high end of an axis
            f = new UncertainAt(sphere, NodePoint(15, 8, 8));
            cert = Certify(f, Mesh(f, 0.5 * H / 2, Shift, 2.2, 16));
            Assert.False(cert.Certified);
            CheckBlocks(cert, 16, 16, 16);
            Assert.Contains(cert.UncertifiedCells, u => u.Cell == new Vector3i(14, 7, 7) && u.Reason == SheetCertificateReason.BlockLimit);
        }

        // the ids of edges and faces are distinct on a grid with more than 65536 nodes along an axis
        [Fact]
        public void EdgeAndFaceIds_AreDistinct_OnALongGrid()
        {
            var r = new TwoLevelMarchingTetrahedra.CertificateRecord { Nodes = new Vector3i(70000, 5, 4) };
            Assert.NotEqual(TwoLevelSheetCertificate.Key(r, 0, 65536 + 5, 1, 1), TwoLevelSheetCertificate.Key(r, 0, 5, 2, 1));
            Assert.NotEqual(TwoLevelSheetCertificate.Key(r, 0, 5, 1, 2), TwoLevelSheetCertificate.Key(r, 0, 5, 1 + 65536 % 5, 1));
            var seen = new HashSet<long>();
            foreach (int i in new[] { 0, 1, 4464, 65535, 65536, 65537, 69999 })
                for (int j = 0; j < 5; ++j) for (int k = 0; k < 4; ++k) for (int axis = 0; axis < 3; ++axis)
                    Assert.True(seen.Add(TwoLevelSheetCertificate.Key(r, axis, i, j, k)));
        }

        [Fact]
        public void HasDirection_TriesTheMeanGradient_AndTheAxes()
        {
            Func<double, double, ExactInterval> I = (lo, hi) => new ExactInterval(lo, hi);
            Func<ExactInterval, ExactInterval, ExactInterval, List<IntervalEnclosure>> one = (x, y, z) => new List<IntervalEnclosure> { new IntervalEnclosure { Gx = x, Gy = y, Gz = z } };
            // the mean (1.5, 4, 0) fails (1.5 - 4 < 0), the x axis does it
            Assert.True(TwoLevelSheetCertificate.HasDirection(one(I(1, 2), I(-1, 9), I(0, 0)), -1));
            Assert.True(TwoLevelSheetCertificate.HasDirection(one(I(-1, 9), I(-2, -1), I(0, 0)), -1));          // the y axis, negative
            Assert.True(TwoLevelSheetCertificate.HasDirection(one(I(-1, 9), I(-1, 9), I(3, 4)), -1));           // the z axis
            Assert.False(TwoLevelSheetCertificate.HasDirection(one(I(-1, 9), I(-1, 9), I(-1, 9)), -1));
            // no axis, the mean does it: two enclosures that share (1, 1, 0) only
            var two = new List<IntervalEnclosure> { new IntervalEnclosure { Gx = I(2, 3), Gy = I(-1, 1), Gz = I(0, 0) }, new IntervalEnclosure { Gx = I(-1, 1), Gy = I(2, 3), Gz = I(0, 0) } };
            Assert.True(TwoLevelSheetCertificate.HasDirection(two, -1));
            Assert.True(TwoLevelSheetCertificate.HasDirection(two, 2));
            // in a plane: the axis along its normal does not count, and the mean loses that part
            Assert.False(TwoLevelSheetCertificate.HasDirection(one(I(-1, 1), I(-1, 1), I(3, 4)), 2));
            Assert.True(TwoLevelSheetCertificate.HasDirection(one(I(-1, 1), I(-1, 1), I(3, 4)), 0));
            Assert.False(TwoLevelSheetCertificate.HasDirection(one(I(3, 4), I(-1, 1), I(-1, 1)), 0));
            Assert.True(TwoLevelSheetCertificate.HasDirection(one(I(3, 4), I(0.5, 1), I(-9, 1)), 0));           // the y axis in the plane x = const
        }

        // ---- the counts on the record ----

        static TwoLevelMarchingTetrahedra.CertificateRecord Flags(int nodesPerAxis, Func<int, int, int, bool> inside)
        {
            var r = new TwoLevelMarchingTetrahedra.CertificateRecord { Nodes = new Vector3i(nodesPerAxis, nodesPerAxis, nodesPerAxis), Origin = Vector3d.Zero, CellSize = 1,
                NodeFlags = new byte[nodesPerAxis * nodesPerAxis * nodesPerAxis] };
            for (int k = 0; k < nodesPerAxis; ++k) for (int j = 0; j < nodesPerAxis; ++j) for (int i = 0; i < nodesPerAxis; ++i)
                r.NodeFlags[i + nodesPerAxis * (j + nodesPerAxis * k)] = (byte)(inside(i, j, k) ? 2 : 0);       // level 1 only: level 0 must not be read
            return r;
        }

        // every sign pattern of one cube, at both parities of its position: the closed curves are as many as the components of
        // the marching-tetrahedra surface (0, 1, 2, 3, 4 in 2, 168, 76, 8, 2 patterns), and the mesh is disks for 0 and 1
        [Fact]
        public void Circles_AndMeshIsDisks_OnEveryPatternOfACube()
        {
            foreach (int parity in new[] { 0, 1 }) {
                int[] histogram = new int[6];
                for (int mask = 0; mask < 256; ++mask) {
                    var r = Flags(3, (i, j, k) => i >= parity && i <= parity + 1 && j <= 1 && k <= 1 && (mask >> ((i - parity) + 2 * j + 4 * k) & 1) != 0);
                    int[] box = { parity, 0, 0, parity + 1, 1, 1 };
                    int m = TwoLevelSheetCertificate.Circles(r, 1, box);
                    ++histogram[m];
                    Assert.Equal(0, TwoLevelSheetCertificate.Circles(r, 0, box));
                    Assert.Equal(m == 0, mask == 0 || mask == 255);
                    Assert.Equal(m == 0, TwoLevelSheetCertificate.MeshIsDisks(r, 1, box, 0));
                    Assert.Equal(m == 1, TwoLevelSheetCertificate.MeshIsDisks(r, 1, box, 1));
                    Assert.True(TwoLevelSheetCertificate.MeshIsDisks(r, 0, box, 0));
                }
                Assert.Equal(new[] { 2, 168, 76, 8, 2, 0 }, histogram);
            }
            // the two inside nodes of a face on a diagonal: joined by the mesh where that is the split's diagonal (both nodes
            // have an even index sum), apart where it is the other one
            var even = Flags(3, (i, j, k) => k == 0 && ((i == 0 && j == 0) || (i == 1 && j == 1)));
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(even, 1, new[] { 0, 0, 0, 1, 1, 1 }));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(even, 1, new[] { 0, 0, 0, 1, 1, 1 }, 1));
            var odd = Flags(3, (i, j, k) => k == 0 && ((i == 1 && j == 0) || (i == 2 && j == 1)));
            Assert.Equal(2, TwoLevelSheetCertificate.Circles(odd, 1, new[] { 1, 0, 0, 2, 1, 1 }));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(odd, 1, new[] { 1, 0, 0, 2, 1, 1 }, 1));
        }

        [Fact]
        public void Circles_AndMeshIsDisks_OnBlocks()
        {
            // one node inside, in the middle of 2 x 2 x 2 cubes: a closed piece of mesh around it
            var one = Flags(3, (i, j, k) => i == 1 && j == 1 && k == 1);
            int[] all = { 0, 0, 0, 2, 2, 2 }, cube = { 0, 0, 0, 1, 1, 1 }, pair = { 0, 0, 0, 2, 1, 1 };
            Assert.Equal(0, TwoLevelSheetCertificate.Circles(one, 1, all));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(one, 1, all, 0));         // a sphere inside: not "nothing"
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(one, 1, all, 1));         // and not a disk: Euler characteristic 2
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(one, 1, cube));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(one, 1, cube, 1));
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(one, 1, pair));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(one, 1, pair, 1));
            // a plane through 3 x 3 x 3 cubes: one curve and one disk in every box it crosses, none in a box beside it
            var plane = Flags(4, (i, j, k) => k <= 1);
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(plane, 1, new[] { 0, 0, 0, 3, 3, 3 }));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(plane, 1, new[] { 0, 0, 0, 3, 3, 3 }, 1));
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(plane, 1, new[] { 1, 0, 1, 3, 2, 2 }));
            Assert.Equal(0, TwoLevelSheetCertificate.Circles(plane, 1, new[] { 0, 0, 2, 3, 3, 3 }));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(plane, 1, new[] { 0, 0, 2, 3, 3, 3 }, 0));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(plane, 1, new[] { 0, 0, 2, 3, 3, 3 }, 1));
            // two planes: two curves on a box that both cross; the mesh in it is two disks, not one
            var two = Flags(5, (i, j, k) => k == 2);
            int[] slab = { 0, 0, 0, 4, 4, 4 };
            Assert.Equal(2, TwoLevelSheetCertificate.Circles(two, 1, slab));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(two, 1, slab, 1));
            // a closed ring of mesh (around a ring of inside nodes) beside a plane: one curve on the box, Euler characteristic
            // 1 + 0, but two components
            var ring = Flags(7, (i, j, k) => (k == 2 && i >= 1 && i <= 3 && j >= 1 && j <= 3 && !(i == 2 && j == 2)) || k >= 5);
            int[] whole = { 0, 0, 0, 6, 6, 6 };
            Assert.Equal(1, TwoLevelSheetCertificate.Circles(ring, 1, whole));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(ring, 1, whole, 1));
            Assert.True(TwoLevelSheetCertificate.MeshIsDisks(ring, 1, new[] { 0, 0, 4, 6, 6, 6 }, 1));
            // a tube through a box (a line of inside nodes):
            // two curves on the box, one component, not a disk
            var tube = Flags(4, (i, j, k) => i == 1 && j == 1);
            int[] across = { 0, 0, 1, 3, 3, 2 };
            Assert.Equal(2, TwoLevelSheetCertificate.Circles(tube, 1, across));
            Assert.False(TwoLevelSheetCertificate.MeshIsDisks(tube, 1, across, 1));
        }

        [Fact]
        public void OneSign_CertifiedSign_AndTheVertexTest()
        {
            Func<double, double, ExactInterval> I = (lo, hi) => new ExactInterval(lo, hi);
            Assert.Equal(1, TwoLevelSheetCertificate.CertifiedSign(I(1e-300, 2)));
            Assert.Equal(-1, TwoLevelSheetCertificate.CertifiedSign(I(-2, -1e-300)));
            Assert.Equal(0, TwoLevelSheetCertificate.CertifiedSign(I(0, 2)));
            Assert.Equal(0, TwoLevelSheetCertificate.CertifiedSign(I(-2, 0)));
            Assert.Equal(0, TwoLevelSheetCertificate.CertifiedSign(I(-1, 1)));
            Assert.Equal(0, TwoLevelSheetCertificate.CertifiedSign(I(double.NaN, double.NaN)));

            var e = new IntervalEnclosure { Gx = I(1, 2), Gy = I(-0.5, 0.5), Gz = I(-3, -2) };
            var list = new List<IntervalEnclosure> { e };
            Assert.True(TwoLevelSheetCertificate.OneSign(list, new Vector3d(1, 0, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(-1, 0, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(0, 1, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(0, -1, 0)));
            Assert.True(TwoLevelSheetCertificate.OneSign(list, new Vector3d(0, 0, -1)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(0, 0, 1)));
            Assert.True(TwoLevelSheetCertificate.OneSign(list, new Vector3d(1, 1, -1)));        // 1 - 0.5 + 2 > 0
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(1, 2, 0)));        // 1 - 1 = 0: not > 0
            Assert.False(TwoLevelSheetCertificate.OneSign(list, Vector3d.Zero));
            // a lower bound of exactly 0 is not a sign: the smallest gradients whose rounded bound reaches 0, and the next
            int atZero = -1;
            for (int a = 1; a <= 16 && atZero < 0; ++a) {
                ExactInterval gx = new ExactInterval(a * double.Epsilon, 1), zero = new ExactInterval(0, 0);
                if ((ExactInterval.Of(1.0) * gx + ExactInterval.Of(0.0) * zero + ExactInterval.Of(0.0) * zero).Lo == 0) atZero = a;
            }
            Assert.True(atZero > 0);
            Func<int, List<IntervalEnclosure>> tiny = a => new List<IntervalEnclosure> { new IntervalEnclosure { Gx = new ExactInterval(a * double.Epsilon, 1), Gy = new ExactInterval(0, 0), Gz = new ExactInterval(0, 0) } };
            Assert.False(TwoLevelSheetCertificate.OneSign(tiny(atZero), new Vector3d(1, 0, 0)));
            Assert.True(TwoLevelSheetCertificate.OneSign(tiny(atZero + 1), new Vector3d(1, 0, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(double.PositiveInfinity, 0, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(double.NaN, 0, 0)));
            // every enclosure of the list counts
            list.Add(new IntervalEnclosure { Gx = I(-1, 2), Gy = I(0, 0), Gz = I(-3, -2) });
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(1, 0, 0)));
            Assert.True(TwoLevelSheetCertificate.OneSign(list, new Vector3d(0, 0, -1)));
            Assert.True(TwoLevelSheetCertificate.OneSign(new List<IntervalEnclosure>(), new Vector3d(1, 0, 0)));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_UnderSimilarities(string name)
        {
            Similarity sim = Similarity.All().First(x => x.Name == name);
            Vector3d centre = new Vector3d(0.03, -0.02, 0.01);
            const int n = 16;
            var f0 = new Sphere(centre, 0.7);
            TwoLevelMarchingTetrahedra gen0 = Mesh(f0, 0.75 * 2.2 / n / 2, Shift, 2.2, n);
            TwoLevelSheetCertificate c0 = Certify(f0, gen0);
            Assert.True(c0.Certified, Summary(c0));

            // the same wall in the moved frame (a sphere is a sphere again), on the axis-aligned grid at the image of the corner
            var f = new Sphere(sim.IsExact ? sim.Scale * centre : sim.Apply(centre), sim.Scale * 0.7);
            Vector3d origin = sim.IsExact ? sim.Scale * Shift : sim.Apply(Vector3d.Zero) + sim.Scale * Shift;
            TwoLevelMarchingTetrahedra gen = Mesh(f, sim.Scale * 0.75 * 2.2 / n / 2, origin, sim.Scale * 2.2, n);
            TwoLevelSheetCertificate c = Certify(f, gen);
            Assert.Equal("c=2;g=0+0", Signature(gen.Mesh));
            if (sim.IsExact) Assert.Equal(List(c0), List(c));           // a power of two: the same result bit for bit
            else Assert.True(c.Certified, name + ": " + Summary(c));
            Assert.Equal(0, OracleViolations(f, gen, c));
        }

        [Fact]
        public void Reuse_AndRepeatedRuns_GiveTheSameResult()
        {
            var f = new CappedTpms(TpmsType.SchwarzP, 2 * Math.PI / 2.2, 0.9, 4);
            TwoLevelMarchingTetrahedra gen = Mesh(f, 0.5 * 2.2 / 16 / 2, Shift, 2.2, 16);
            var cert = new TwoLevelSheetCertificate { Field = f, Mesher = gen };
            cert.Compute();
            string first = List(cert);
            cert.Compute();
            Assert.Equal(first, List(cert));
            Assert.Equal(first, List(Certify(f, gen)));
            // another mesher on the same instance, then a refused one: nothing is left over
            var s = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            cert.Field = s; cert.Mesher = Mesh(s, 0.5 * 2.2 / 16 / 2, Shift, 2.2, 16);
            cert.Compute();
            Assert.True(cert.Certified);
            Assert.Equal(List(Certify(s, cert.Mesher)), List(cert));
            cert.Mesher.Mesh = null;
            cert.Compute();
            Assert.False(cert.Certified);
            Assert.Empty(cert.UncertifiedCells);
            Assert.Equal(0, cert.SurfaceCells[0] + cert.CellsCertifiedAlone[0] + cert.CellsInBlocks[0] + cert.Blocks[0]);
            Assert.Null(cert.CellStates[0]);
        }
    }
}
