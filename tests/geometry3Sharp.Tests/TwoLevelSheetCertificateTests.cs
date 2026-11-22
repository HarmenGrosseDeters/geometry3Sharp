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

        static string List(TwoLevelSheetCertificate c)
        {
            return Summary(c) + "|" + string.Join(";", c.UncertifiedCells.Select(u => u.Level + ":" + u.Cell.x + "," + u.Cell.y + "," + u.Cell.z + ":" + u.Reason))
                + "|" + string.Join("/", c.CellStates.Select(a => a == null ? "-" : string.Join("", a)));
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
            Assert.Equal(c.SurfaceCells[0], c.CellStates[0].Count(s => s == 1 || s == 3) - c.CellStates[0].Count(s => s == 3) + c.CellsInBlocks[0] - ZeroFreeInBlocks(c, 0));
            Assert.Equal("c=2;g=0+0", Signature(gen.Mesh));
            Assert.Equal(0, OracleViolations(f, gen, c));
        }

        // cubes in blocks that were zero-free or certified before the block took them in are counted as in the block: the
        // count of surface cubes is those certified alone plus the surface cubes in blocks (here: at least the alone ones)
        static int ZeroFreeInBlocks(TwoLevelSheetCertificate c, int level) { return c.CellsInBlocks[level] - (c.SurfaceCells[level] - c.CellsCertifiedAlone[level]); }

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
            r.NodeFlags[0] |= 16;
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            r.NodeFlags[0] &= 0xEF;
            r.NodeFlags[r.NodeFlags.Length - 1] |= 32;
            Assert.True((Certify(f, gen).RunFailure & SheetCertificateRunFailure.MeshSide) != 0);
            r.NodeFlags[r.NodeFlags.Length - 1] &= 0xDF;
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
            // a tube through a box (a ring of inside nodes around a line of outside ones is not needed: a line of inside nodes):
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
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(double.NaN, 0, 0)));
            Assert.False(TwoLevelSheetCertificate.OneSign(list, new Vector3d(double.PositiveInfinity, 0, 0)));
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
