using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The volume that SafeRounding (the default of ExactMeshBoolean and SolidBooleanPipeline) moves on thin results
    /// (docs/research/ROUNDING_THIN_VOLUME_2026-11.md). These tests document the behaviour as it is: the rounding repair contracts
    /// an edge between two features of the exact result that nearly coincide, within its bound, and where the vertex that moves
    /// carries a fan over a large flat face, the volume under the fan changes by a third of the move times the fan's area, whatever
    /// the thickness of the solid. The run reports Success, clean, nothing removed. The fixtures are shapes described by the owner,
    /// built procedurally; the oracle is the exact result with its vertices rounded to the nearest double (SafeRounding = false).
    /// </summary>
    public class RoundingThinVolumeTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection;

        static double Pow2(int k) { return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); }

        internal static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
        static double Area(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).y;

        // ---- the meshes ---------------------------------------------------------------------------------------------------------------

        // a closed prism over a convex outline (counter-clockwise) from z0 to z1; the caps are fans from the first vertex
        static DMesh3 Prism(IList<Vector2d> o, double z0, double z1)
        {
            var m = new DMesh3();
            int n = o.Count;
            for (int k = 0; k < 2; ++k)
                foreach (Vector2d p in o)
                    m.AppendVertex(new Vector3d(p.x, p.y, k == 0 ? z0 : z1));
            for (int i = 1; i + 1 < n; ++i) {
                m.AppendTriangle(0, i + 1, i);
                m.AppendTriangle(n, n + i, n + i + 1);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(i, j, n + j);
                m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        static DMesh3 Joined(IEnumerable<DMesh3> parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 b in parts) {
                var map = new int[b.MaxVertexID];
                foreach (int v in b.VertexIndices())
                    map[v] = m.AppendVertex(b.GetVertex(v));
                foreach (int t in b.TriangleIndices()) {
                    Index3i q = b.GetTriangle(t);
                    m.AppendTriangle(map[q.a], map[q.b], map[q.c]);
                }
            }
            return m;
        }

        // the rectangle [x0, x1] x [y0, y1] with its corners cut into c facets of radius r: 4 (c + 1) vertices
        static List<Vector2d> RoundedRectangle(double x0, double x1, double y0, double y1, double r, int c)
        {
            var o = new List<Vector2d>();
            Vector2d[] centers = { new Vector2d(x1 - r, y0 + r), new Vector2d(x1 - r, y1 - r), new Vector2d(x0 + r, y1 - r), new Vector2d(x0 + r, y0 + r) };
            for (int q = 0; q < 4; ++q)
                for (int i = 0; i <= c; ++i) {
                    double a = -Math.PI / 2 + q * Math.PI / 2 + i * (Math.PI / 2) / c;
                    o.Add(centers[q] + r * new Vector2d(Math.Cos(a), Math.Sin(a)));
                }
            return o;
        }

        // Shape 1a. A: one thin plate, the unit square with faceted corners (22 face planes), z in [0, tA]. B: four separate, thicker
        // plates beside it (24 face planes), whose corners towards A lie e inside A's side face x = 1 and whose bottoms lie d (to
        // 1.15 d) below A's bottom face: no face of one is coplanar with a face of the other, and the 8 lower corner vertices of B lie
        // within e and d of A's surface without being on it
        internal static List<DMesh3> ThinPlates(double tA, double e, double d, double tB = 7.5e-5)
        {
            DMesh3 a = Prism(RoundedRectangle(0, 1, 0, 1, 0.05, 4), 0, tA);
            var pieces = new List<DMesh3>();
            for (int k = 0; k < 4; ++k) {
                double y0 = -0.25 + k * 0.375 + 0.01, y1 = y0 + 0.355;
                var o = new List<Vector2d> { new Vector2d(1 - e, y0), new Vector2d(1.3, y0 + 0.01), new Vector2d(1.3 + 0.01 * k, y1 - 0.01), new Vector2d(1 - e * (1 + 0.1 * k), y1) };
                double zb = -d * (1 + 0.05 * k);
                pieces.Add(Prism(o, zb, zb + tB));
            }
            return new List<DMesh3> { a, Joined(pieces) };
        }

        // the standard case: A 1 / 50 000 of its side thick, the near miss 3.5e-6 and 1.5e-6 (about 1e-6 of the diagonal 2)
        internal const double TA = 2e-5, E = 3.5e-6, Dz = 1.5e-6;

        // a thin shell over [0, 1] x [0, w], nx by ny quads: the bottom surface at bottom(x), the top t above it
        static DMesh3 Shell(double w, double t, Func<double, double> bottom, int nx, int ny)
        {
            var m = new DMesh3();
            Func<int, int, int, int> id = (i, j, k) => (k * (ny + 1) + j) * (nx + 1) + i;
            for (int k = 0; k < 2; ++k)
                for (int j = 0; j <= ny; ++j)
                    for (int i = 0; i <= nx; ++i) {
                        double x = (double)i / nx;
                        m.AppendVertex(new Vector3d(x, w * j / ny, bottom(x) + (k == 0 ? 0 : t)));
                    }
            Action<int, int, int, int> quad = (a, b, c, d) => { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); };
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    quad(id(i, j, 0), id(i, j + 1, 0), id(i + 1, j + 1, 0), id(i + 1, j, 0));
                    quad(id(i, j, 1), id(i + 1, j, 1), id(i + 1, j + 1, 1), id(i, j + 1, 1));
                }
            for (int i = 0; i < nx; ++i) {
                quad(id(i, 0, 0), id(i + 1, 0, 0), id(i + 1, 0, 1), id(i, 0, 1));
                quad(id(i, ny, 0), id(i, ny, 1), id(i + 1, ny, 1), id(i + 1, ny, 0));
            }
            for (int j = 0; j < ny; ++j) {
                quad(id(0, j, 0), id(0, j, 1), id(0, j + 1, 1), id(0, j + 1, 0));
                quad(id(nx, j, 0), id(nx, j + 1, 0), id(nx, j + 1, 1), id(nx, j, 1));
            }
            return m;
        }

        internal static DMesh3 Box(Vector3d lo, Vector3d hi, Func<Vector3d, Vector3d> f = null)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
            if (f != null)
                foreach (int v in m.VertexIndices())
                    m.SetVertex(v, f(m.GetVertex(v)));
            return m;
        }

        // ---- the measurement ----------------------------------------------------------------------------------------------------------

        sealed class Measured
        {
            public ExactMeshBoolean Exact, Safe;
            public double ExactVolume, Change;      // Change: the volume of the default result minus the exact one
            public double Relative { get { return Change / ExactVolume; } }
        }

        static Measured Measure(List<DMesh3> inputs, ExactMeshBoolean.Operations op)
        {
            var r = new Measured { Exact = new ExactMeshBoolean(inputs, op) { SafeRounding = false }, Safe = new ExactMeshBoolean(inputs, op) };
            Assert.True(r.Exact.Compute(), r.Exact.Error);
            Assert.True(r.Safe.Compute(), r.Safe.Error);
            r.ExactVolume = Volume(r.Exact.Result);
            r.Change = Volume(r.Safe.Result) - r.ExactVolume;
            return r;
        }

        static void AssertSolid(DMesh3 r)
        {
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
        }

        // ---- shape 1a: the union of two thin plates -----------------------------------------------------------------------------------

        [Fact]
        public void ThinPlates_TheRoundingMovesAlmostAPercentOfTheVolume_AndTheRunReportsNothing()
        {
            Measured m = Measure(ThinPlates(TA, E, Dz), U);
            // the exact union is the two plates (they overlap in a strip 3.5e-6 wide): A 2e-5 of volume, B 3.2e-5
            Assert.Equal(5.178e-5, m.ExactVolume, 8);
            // the default result has 0.7 % more volume, 1.85 % of the thin plate A
            Assert.InRange(m.Relative, 0.0070, 0.0073);
            Assert.InRange(m.Change / 2e-5, 0.018, 0.019);
            // and says nothing: Success, clean, no component removed; the six edits are contractions within the bound
            MeshRoundingRepair r = m.Safe.Rounding;
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, m.Safe.Status);
            Assert.True(m.Safe.RoundingClean);
            AssertSolid(m.Safe.Result);
            Assert.Equal(0, r.RemovedComponents);
            Assert.Empty(r.RemovedComponentInfos);
            Assert.Equal(6, r.InitialDegenerate);
            Assert.Equal(0, r.InitialIntersections);
            Assert.Equal(6, r.Collapses);
            Assert.Equal(0, r.Flips + r.Moves + r.Zips + r.StripZips + r.VertexRemovals);
            // the pipeline: the same result, Success, clean, an empty route
            var p = new SolidBooleanPipeline(ThinPlates(TA, E, Dz), U);
            Assert.True(p.Compute(), p.Error);
            Assert.Equal("", Invariance.GeometricDifference(m.Safe.Result, p.Result));
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Empty(p.Report.RoundingRemoved);
            Assert.Empty(p.Report.Route);
            Assert.Equal(0, p.Report.ContactsResult);
        }

        [Fact]
        public void ThinPlates_TheCause_IsAContractionAcrossTheNearMiss_ThatPullsAFanOverTheBottomFace()
        {
            Measured m = Measure(ThinPlates(TA, E, Dz), U);
            MeshRoundingRepair r = m.Safe.Rounding;
            // the largest move of the surface is the largest near miss (the lowest piece of B: 1.15 d), 1 / 12 of A's thickness and
            // far inside the bound of 1e-5 of the diagonal
            Assert.Equal(1.15 * Dz, r.MaxDrift, 9);
            Assert.InRange(r.MaxLength, 1.9e-5, 2.1e-5);
            Assert.True(r.MaxDrift < 0.1 * r.MaxLength);
            // the vertices that went: where the vertical corner edges of B pierce A's bottom face z = 0, 3.5e-6 from its edge. Each is
            // contracted into B's corner below it, and its fan over A's bottom face goes down with it
            var kept = new HashSet<Vector3d>(m.Safe.Result.Vertices());
            var gone = m.Exact.Result.VertexIndices().Where(v => !kept.Contains(m.Exact.Result.GetVertex(v))).ToList();
            Assert.Equal(6, gone.Count);
            double pulled = 0;
            foreach (int v in gone) {
                Vector3d q = m.Exact.Result.GetVertex(v);
                Assert.Equal(0.0, q.z);
                Assert.InRange(1 - q.x, 0.9 * E, 1.4 * E);
                Vector3d to = m.Safe.Result.Vertices().OrderBy(w => w.DistanceSquared(q)).First();
                Assert.Equal(q.x, to.x);
                Assert.Equal(q.y, to.y);
                Assert.InRange(-to.z, Dz, 1.15 * Dz);
                // (a third of the move times the area of the triangles of the bottom face at the vertex: the volume of the cone under it)
                double fan = m.Exact.Result.VtxTrianglesItr(v).Where(t => Math.Abs(m.Exact.Result.GetTriNormal(t).z) > 0.5).Sum(t => m.Exact.Result.GetTriArea(t));
                pulled += -to.z * fan / 3;
            }
            // the cones overlap where two of the vertices share a triangle, so their sum is an upper estimate of the change
            Assert.InRange(m.Change, 0.5 * pulled, pulled * (1 + 1e-12));   // (equal up to 8.3e-15 relative since the exact InCircle)
            // the proven bound of the repair, the drift times the area, holds; it is 13 times the change
            Assert.True(Math.Abs(m.Change) <= r.MaxDrift * Area(m.Safe.Result));
            Assert.InRange(r.MaxDrift * Area(m.Safe.Result) / m.Change, 12, 14);
        }

        [Theory]
        [InlineData(1.6e-4)]
        [InlineData(8e-5)]
        [InlineData(4e-5)]
        [InlineData(2e-5)]
        [InlineData(1e-5)]
        [InlineData(5e-6)]
        public void ThinPlates_TheChangeIsTheSameVolumeAtEveryThickness_SoItsShareGrowsAsThePlateGetsThinner(double tA)
        {
            Measured m = Measure(ThinPlates(tA, E, Dz), U);
            Assert.Equal(6, m.Safe.Rounding.Collapses);
            // 3.7e-7 of volume: 0.23 % of a plate 1.6e-4 thick, 7.4 % of one 5e-6 thick
            Assert.InRange(m.Change, 3.65e-7, 3.75e-7);
            Assert.InRange(m.Change / tA, 0.98 * 3.7e-7 / tA, 1.02 * 3.7e-7 / tA);
            Assert.True(m.Safe.RoundingClean);
        }

        [Theory]
        [InlineData(1e-8, 6)]
        [InlineData(1e-7, 6)]
        [InlineData(5e-7, 6)]
        [InlineData(1e-6, 6)]
        [InlineData(1.5e-6, 6)]
        [InlineData(2e-6, 3)]
        [InlineData(2.5e-6, 0)]
        [InlineData(5e-6, 0)]
        public void ThinPlates_TheChangeGrowsWithTheNearMiss_UntilTheTrianglesBetweenAreNoLongerDegenerateByArea(double d, int edits)
        {
            Measured m = Measure(ThinPlates(TA, E, d), U);
            Assert.Equal(edits, m.Safe.Rounding.Collapses);
            Assert.Equal(edits, m.Safe.Rounding.InitialDegenerate);
            if (edits == 0) {
                // the triangles between the two features, about e by d, are larger than the degenerate area (1e-12 of the squared
                // diagonal, 4e-12 here): nothing is edited, and the result is the exact one rounded
                Assert.Equal("", Invariance.Difference(m.Exact.Result, m.Safe.Result));
                Assert.Equal(0.0, m.Change);
            } else if (edits == 6)
                // linear in the near miss: 0.247 of volume per unit of d
                Assert.InRange(m.Change / d, 0.24, 0.255);
            else
                // in between, the triangles at three of the six corners are degenerate by area: 0.27 % of the union
                Assert.InRange(m.Relative, 0.0025, 0.0029);
        }

        [Fact]
        public void ThinPlates_AContractionAlongTheFace_MovesNoVolume()
        {
            // the near miss in the plane of the plate smaller than the one across it: the repair contracts the shorter, horizontal edge
            // (six times as well), and the fans stay in their plane
            Measured m = Measure(ThinPlates(TA, 1e-6, Dz), U);
            Assert.Equal(6, m.Safe.Rounding.Collapses);
            Assert.InRange(m.Safe.Rounding.MaxDrift, 0.9e-6, 1.3e-6);
            Assert.True(Math.Abs(m.Relative) < 1e-7, m.Relative.ToString());
        }

        [Fact]
        public void ThinPlates_AGridCuresTheNearMissesBelowItsCell_AndNotTheOnesAboveIt()
        {
            double grid = Pow2(-20);        // 9.5e-7
            Func<double, double> onGrid = (d) => {
                // the change of the pipeline's result with InputGrid against the exact union of the snapped inputs
                int moved;
                var snapped = ThinPlates(TA, E, d).Select(x => SolidBooleanPipeline.Snapped(x, grid, out moved)).ToList();
                var exact = new ExactMeshBoolean(snapped, U) { SafeRounding = false };
                Assert.True(exact.Compute(), exact.Error);
                var p = new SolidBooleanPipeline(ThinPlates(TA, E, d), U) { InputGrid = grid };
                Assert.True(p.Compute(), p.Error);
                Assert.True(p.Report.Clean, p.Report.NotCleanReason);
                return (Volume(p.Result) - Volume(exact.Result)) / Volume(exact.Result);
            };
            // a near miss of 1e-7 snaps to 0: the bottoms are coplanar, the exact boolean resolves that without a sliver
            Assert.True(Math.Abs(onGrid(1e-7)) < 1e-9);
            Assert.InRange(Measure(ThinPlates(TA, E, 1e-7), U).Relative, 4e-4, 6e-4);
            // a near miss of 1.5e-6 is still one on the grid (it snaps to two cells): the same contraction, the same order of change
            Assert.InRange(onGrid(Dz), 0.005, 0.012);
            // (and the snap itself moves the faces of a plate 21 cells thick: against the exact union of the inputs as given)
            var given = new SolidBooleanPipeline(ThinPlates(TA, E, Dz), U) { InputGrid = grid };
            Assert.True(given.Compute(), given.Error);
            Assert.InRange((Volume(given.Result) - 5.178e-5) / 5.178e-5, 0.005, 0.02);
        }

        [Fact]
        public void ThinPlates_TheGridStepDoesNotRun_TheRunIsASuccess()
        {
            var plain = new SolidBooleanPipeline(ThinPlates(TA, E, Dz), U);
            Assert.True(plain.Compute(), plain.Error);
            var auto = new SolidBooleanPipeline(ThinPlates(TA, E, Dz), U) { AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.Grid };
            Assert.True(auto.Compute(), auto.Error);
            Assert.Empty(auto.Report.Route);
            Assert.Equal("", Invariance.Difference(plain.Result, auto.Result));
        }

        // ---- shape 1b: a thin curved shell cut by a box -------------------------------------------------------------------------------

        // A: a shell that sags in the middle (364 triangles, 1 / 4000 of its length thick). B: a box 40 long whose top face lies h above
        // the lowest part of the shell's bottom surface and whose side faces pass within off of vertex columns of the shell: the
        // intersection is a lens strip h thick (1e-5 of its diagonal and less)
        static List<DMesh3> ShellAndBox(double h, double off, double tiltX, double tiltY, double skew)
        {
            const double t = 2.5e-4, sag = 1e-3;
            DMesh3 shell = Shell(0.3, t, x => -sag * x * (1 - x), 45, 1);
            double x0 = 21.0 / 45 + off, x1 = 24.0 / 45 - 0.7 * off, y0 = 0.4 * off, zb = -sag * 0.25;
            DMesh3 box = Box(new Vector3d(x0, y0, -40), new Vector3d(x1, y0 + 40, zb + h),
                q => new Vector3d(q.x + skew * (q.y - y0), q.y, q.z + (q.z > -1 ? tiltX * (q.x - 0.5) + tiltY * (q.y - 0.15) : 0)));
            return new List<DMesh3> { shell, box };
        }

        [Theory]
        [InlineData(2e-6, 1e-8)]
        [InlineData(2e-6, 1e-7)]
        [InlineData(4e-6, 1e-8)]
        [InlineData(4e-6, 3e-7)]
        [InlineData(8e-6, 1e-7)]
        public void ShellCutByABox_TheThinStripKeepsItsVolume(double h, double off)
        {
            // not reproduced here: the strip's faces are small (a fan covers a column, not a face of the size of the result), and the
            // edges the repair contracts run along the faces. A change of the order of the one of the plates would need a contraction
            // across the strip at a vertex whose fan covers much of it
            Measured m = Measure(ShellAndBox(h, off, 2e-5, -1e-5, 5e-3), I);
            Assert.True(m.ExactVolume > 0);
            Assert.True(m.Exact.Result.CachedBounds.DiagonalLength > 0.3);      // (h is 6e-6 to 2.6e-5 of it)
            Assert.True(Math.Abs(m.Relative) < 1e-6, m.Relative.ToString());
            Assert.True(m.Safe.RoundingClean);
            Assert.Equal(0, m.Safe.Rounding.RemovedComponents);
            AssertSolid(m.Safe.Result);
        }

        // The second description of shape 1b: a clearly curved panel (a cylindrical arc of half angle 0.7 over a chord of 1, 416
        // triangles, 1 / 3400 of its length thick, 1.08 wide) clamped between the box's side faces y = 0 and y = 1.08, its rim vertices
        // within 3e-12 of them and not on them, and the box's top face through it
        static DMesh3 ClampedPanel(double sign, int seed)
        {
            const double w = 1.08, t = 1.0 / 3400, theta = 0.7;
            const int nx = 14, ny = 6;
            var m = new DMesh3();
            double R = 0.5 / Math.Sin(theta);
            var noise = new Random(seed);
            Func<int, int, int, int> id = (i, j, k) => (k * (ny + 1) + j) * (nx + 1) + i;
            for (int k = 0; k < 2; ++k)
                for (int j = 0; j <= ny; ++j)
                    for (int i = 0; i <= nx; ++i) {
                        double a = -theta + 2 * theta * i / nx, r = R + (k == 0 ? 0 : t), y = w * j / ny;
                        if (j == 0 || j == ny)
                            y += 3e-12 * (2 * noise.NextDouble() - 1);
                        m.AppendVertex(new Vector3d(0.5 + r * Math.Sin(a), y, sign * (r * Math.Cos(a) - R * Math.Cos(theta))));
                    }
            Action<int, int, int, int> quad = (a, b, c, d) => { if (sign > 0) { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); } };
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    quad(id(i, j, 0), id(i + 1, j, 0), id(i + 1, j + 1, 0), id(i, j + 1, 0));
                    quad(id(i, j, 1), id(i, j + 1, 1), id(i + 1, j + 1, 1), id(i + 1, j, 1));
                }
            for (int i = 0; i < nx; ++i) {
                quad(id(i, 0, 0), id(i, 0, 1), id(i + 1, 0, 1), id(i + 1, 0, 0));
                quad(id(i, ny, 0), id(i + 1, ny, 0), id(i + 1, ny, 1), id(i, ny, 1));
            }
            for (int j = 0; j < ny; ++j) {
                quad(id(0, j, 0), id(0, j + 1, 0), id(0, j + 1, 1), id(0, j, 1));
                quad(id(nx, j, 0), id(nx, j, 1), id(nx, j + 1, 1), id(nx, j + 1, 0));
            }
            return m;
        }

        // the box 2.4 long between y = 0 and y = 1.08 whose top face passes through (xm, zm) with the slope sl along x
        static DMesh3 ClampBox(double xm, double zm, double sl)
        {
            return Box(new Vector3d(-0.7, 0, -0.6), new Vector3d(1.7, 1.08, 0), q => q.z == 0 ? new Vector3d(q.x, q.y, zm + sl * (q.x - xm)) : q);
        }

        // The exact InCircle of the delaunay code (this PR) makes the flat regions Delaunay, so the caps along the rims that used to
        // round to degenerate triangles are gone: with the default settings there is nothing left to repair at seed 7. The cases below
        // keep the shape: the default run (nothing to repair, the volume identical), the run without SimplifyFlatRegions (which keeps the
        // near-flush rim triangles: the repair contracts them), and one seed (8) of the sagging panel where a flip is still needed
        [Theory]
        [InlineData(1.0, 0.0630075575145928, 0.19)]         // a vault: the part below the plane, 19 % of the panel
        [InlineData(1.0, 0.08170061422591124, 0.256)]       // 26 %
        [InlineData(-1.0, -0.05836649455985357, 0.826)]     // a sagging panel: 83 %
        [InlineData(-1.0, -0.1356371257154444, 0.51)]       // 51 %
        public void ClampedPanelCutAtFiveDegrees_NothingIsLeftToRepair_AndTheVolumeIsTheExactOne(double sign, double z0, double share)
        {
            DMesh3 panel = ClampedPanel(sign, 7);
            Assert.Equal(416, panel.TriangleCount);
            Assert.True(panel.IsClosed());
            var op = new ExactMeshBoolean(new List<DMesh3> { panel, ClampBox(0.5, z0, Math.Tan(5 * Math.PI / 180)) }, I);
            Assert.True(op.Compute(), op.Error);
            MeshRoundingRepair r = op.Rounding;
            Assert.Equal(0, r.InitialDegenerate);
            Assert.Equal(0, r.Flips + r.Collapses + r.Zips + r.StripZips + r.Moves);
            Assert.Equal(0.0, r.MaxDrift);
            Assert.True(op.RoundingClean);
            Assert.InRange(r.VolumeBefore / Volume(panel), share - 0.01, share + 0.01);
            Assert.Equal(r.VolumeBefore, r.VolumeAfter);
            AssertSolid(op.Result);
        }

        [Theory]
        [InlineData(1.0, 0.0630075575145928, 0.19)]
        [InlineData(1.0, 0.08170061422591124, 0.256)]
        [InlineData(-1.0, -0.05836649455985357, 0.826)]
        [InlineData(-1.0, -0.1356371257154444, 0.51)]
        public void ClampedPanelCutAtFiveDegrees_WithoutSimplify_TheRepairContractsTheRimTriangles_AndTheVolumeStays(double sign, double z0, double share)
        {
            DMesh3 panel = ClampedPanel(sign, 7);
            var op = new ExactMeshBoolean(new List<DMesh3> { panel, ClampBox(0.5, z0, Math.Tan(5 * Math.PI / 180)) }, I) { SimplifyFlatRegions = false };
            Assert.True(op.Compute(), op.Error);
            MeshRoundingRepair r = op.Rounding;
            // 18 to 48 degenerate triangles (measured: 18, 18, 48, 26), each pair contracted (9, 9, 24, 13), the rest moves nothing
            // that matters. (The counts are not pinned: a last bit of the sines of the fixture can move one across the degenerate area)
            Assert.InRange(r.InitialDegenerate, 10, 60);
            Assert.InRange(r.Collapses, 5, 30);
            Assert.True(r.MaxDrift > 0 && r.MaxDrift <= 2e-12, r.MaxDrift.ToString());
            Assert.True(op.RoundingClean);
            Assert.InRange(r.VolumeBefore / Volume(panel), share - 0.01, share + 0.01);
            Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) <= 5e-12 * r.VolumeBefore, ((r.VolumeAfter - r.VolumeBefore) / r.VolumeBefore).ToString());
            AssertSolid(op.Result);
        }

        [Fact]
        public void ClampedPanelCutAtFiveDegrees_ASaggingPanelAtSeed8_NeedsOneFlip_WhichMovesAlmostNothing()
        {
            // the rim noise of seed 8 leaves one degenerate triangle in the exact result, flipped; the flip moves a vertex by 1.5e-7
            // (1.1e-7 of the diagonal) and the volume by 3.5e-10 of itself
            DMesh3 panel = ClampedPanel(-1, 8);
            var op = new ExactMeshBoolean(new List<DMesh3> { panel, ClampBox(0.5, -0.05836649455985357, Math.Tan(5 * Math.PI / 180)) }, I);
            Assert.True(op.Compute(), op.Error);
            MeshRoundingRepair r = op.Rounding;
            Assert.Equal(1, r.InitialDegenerate);
            Assert.Equal(1, r.Flips);
            Assert.Equal(0, r.Collapses + r.Zips + r.StripZips + r.Moves);
            Assert.InRange(r.MaxDrift / op.Result.CachedBounds.DiagonalLength, 5e-8, 2e-7);
            Assert.InRange(r.VolumeBefore / Volume(panel), 0.82, 0.83);
            Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) <= 1e-9 * r.VolumeBefore, ((r.VolumeAfter - r.VolumeBefore) / r.VolumeBefore).ToString());
            Assert.True(op.RoundingClean);
            AssertSolid(op.Result);
        }

        // A lens: the box's top face a fraction f of the thickness above the lower surface of the facet next to the lowest point of the
        // sagging panel, nearly parallel to it (tilt in slope), 0.14 to 0.29 % of the panel's volume across the full width. With the exact
        // InCircle most of the (tilt, f) pairs need no repair (measured on seeds 8 and 9: 20 of the 24 combinations of tilt -3e-4, -1e-4,
        // 0, 1e-4 and f 0.02, 0.03, 0.04 contract nothing); two pairs (on both seeds) make the repair contract an edge, and they are the cases of the shape
        static ExactMeshBoolean Lens(double tilt, double f, int seed, out DMesh3 panel)
        {
            const double t = 1.0 / 3400, theta = 0.7;
            double R = 0.5 / Math.Sin(theta);
            Func<int, Vector3d> lower = (i) => { double a = -theta + 2 * theta * i / 14; return new Vector3d(0.5 + (R + t) * Math.Sin(a), 0, -((R + t) * Math.Cos(a) - R * Math.Cos(theta))); };
            Vector3d p0 = lower(7), p1 = lower(8);
            panel = ClampedPanel(-1, seed);
            var op = new ExactMeshBoolean(new List<DMesh3> { panel, ClampBox((p0.x + p1.x) / 2, (p0.z + p1.z) / 2 + f * t, (p1.z - p0.z) / (p1.x - p0.x) + tilt) }, I);
            Assert.True(op.Compute(), op.Error);
            return op;
        }

        [Fact]
        public void ClampedPanelShavedInsideOneFacet_TheRepairContractsAnEdge_AndTheVolumeStays()
        {
            // the top face 4 % of the thickness above the facet, tilted by -3e-4: two contractions, a drift of 1.3e-6 of the diagonal, as
            // large as the one described for the shape; but the lens is flat (inside one facet), and the contraction runs along it
            ExactMeshBoolean op = Lens(-3e-4, 0.04, 8, out DMesh3 panel);
            MeshRoundingRepair r = op.Rounding;
            Assert.InRange(r.VolumeBefore / Volume(panel), 0.0028, 0.0030);
            Assert.Equal(2, r.Collapses);
            Assert.Equal(2, r.InitialDegenerate);
            Assert.InRange(r.MaxDrift / op.Result.CachedBounds.DiagonalLength, 1e-6, 1.5e-6);
            Assert.True(Math.Abs(r.VolumeAfter - r.VolumeBefore) <= 1e-12 * r.VolumeBefore, ((r.VolumeAfter - r.VolumeBefore) / r.VolumeBefore).ToString());
            Assert.True(op.RoundingClean);
            AssertSolid(op.Result);
        }

        [Fact]
        public void ClampedPanelShavedInsideOneFacet_ATiltedLens_ACollapseChangesTheVolumeByAMillionth()
        {
            // the top face 2 % of the thickness above the facet, tilted by +1e-4: two contractions with a drift of 6.8e-7 of the diagonal
            // and a change of the volume of -1.6e-6 of the lens's volume (the lens is 0.14 % of the panel): a case of its own, the
            // contraction goes across the lens
            ExactMeshBoolean op = Lens(1e-4, 0.02, 8, out DMesh3 panel);
            MeshRoundingRepair r = op.Rounding;
            Assert.InRange(r.VolumeBefore / Volume(panel), 0.0014, 0.00146);
            Assert.Equal(2, r.Collapses);
            Assert.InRange(r.MaxDrift / op.Result.CachedBounds.DiagonalLength, 6e-7, 7.5e-7);
            double change = (r.VolumeAfter - r.VolumeBefore) / r.VolumeBefore;
            Assert.InRange(change, -2e-6, -1.2e-6);
            Assert.True(op.RoundingClean);
            AssertSolid(op.Result);
        }

        [Fact]
        public void ClampedPanelShavedInsideOneFacet_AFlatLens_NeedsNoRepair()
        {
            ExactMeshBoolean op = Lens(0, 0.03, 8, out DMesh3 panel);
            MeshRoundingRepair r = op.Rounding;
            Assert.InRange(r.VolumeBefore / Volume(panel), 0.0021, 0.0022);
            Assert.Equal(0, r.InitialDegenerate);
            Assert.Equal(0.0, r.MaxDrift);
            Assert.Equal(r.VolumeBefore, r.VolumeAfter);
            Assert.True(op.RoundingClean);
            AssertSolid(op.Result);
        }

        // ---- shape 2: a curved plate partly flush with a block (the guard) ------------------------------------------------------------

        // A: a plate 1 / 250 of its length thick (152 triangles), flat over the first quarter and curving down after it. B: a block with
        // faceted corners (76 triangles, 22 face planes) whose top face z = 1/4 carries the flat part of A: 8 vertices of A lie exactly on
        // it and 12 triangles of A's bottom are coplanar with it; the curved part goes down into the block at a clear angle
        internal static List<DMesh3> PlateOnBlock()
        {
            DMesh3 plate = Shell(0.3, 0.004, x => 0.25 - (x > 0.25 ? 0.6 * (x - 0.25) * (x - 0.25) : 0), 12, 2);
            DMesh3 block = Prism(RoundedRectangle(-0.0625, 0.9375, -0.0625, 0.25, 0.03125, 4), -0.25, 0.25);
            return new List<DMesh3> { plate, block };
        }

        [Fact]
        public void PlateFlushOnABlock_TheRoundedVolumeIsTheExactOne_AndTheResultIsClean()
        {
            List<DMesh3> inputs = PlateOnBlock();
            // the fixture is what it says: the flush vertices and the coplanar triangles
            DMesh3 plate = inputs[0];
            Assert.Equal(152, plate.TriangleCount);
            Assert.Equal(76, inputs[1].TriangleCount);
            Assert.Equal(8, plate.Vertices().Count(v => v.z == 0.25 && v.x <= 0.25 && v.y <= 0.25));
            Assert.Equal(12, plate.TriangleIndices().Count(t => { Index3i q = plate.GetTriangle(t); return plate.GetVertex(q.a).z == 0.25 && plate.GetVertex(q.b).z == 0.25 && plate.GetVertex(q.c).z == 0.25; }));

            Measured m = Measure(inputs, I);
            Assert.True(m.ExactVolume > 1e-4, m.ExactVolume.ToString());
            Assert.True(Math.Abs(m.Relative) < 1e-9, m.Relative.ToString());
            Assert.True(m.Safe.RoundingClean);
            Assert.Equal(0, m.Safe.Rounding.RemovedComponents);
            AssertSolid(m.Safe.Result);
            var p = new SolidBooleanPipeline(inputs, I);
            Assert.True(p.Compute(), p.Status + " " + p.Error);
            Assert.True(p.Report.Clean, p.Report.NotCleanReason);
            Assert.Empty(p.Report.RoundingRemoved);
            Assert.Equal(m.ExactVolume, Volume(p.Result), 12);
            // (the part of the plate inside the block: below z = 1/4, so none of the flat part)
            Assert.True(p.Result.Vertices().All(v => v.z <= 0.25 && v.x >= 0.25));
        }
    }
}
