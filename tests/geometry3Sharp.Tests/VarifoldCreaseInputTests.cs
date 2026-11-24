using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// AlphaWrapFeaturePass.VarifoldCreases (docs/design/VARIFOLD_CREASE_INPUT.md): the input's creases from its unoriented
    /// varifold, so an unwelded, flipped, duplicated triangle soup, where the dihedral of its edges finds no crease, gets its
    /// creases back; the pass's guarantees are kept; off by default; deterministic, reusable, 2^k invariant; and the crease
    /// test itself (VarifoldCreaseQuery) rejects faces, fillets wider than its ball, narrow lips and sheets that do not meet.
    /// </summary>
    public class VarifoldCreaseInputTests
    {
        const double RelAlpha = 60;

        static MeshAlphaWrap Wrap(DMesh3 input, double relAlpha = RelAlpha)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = 10 * relAlpha };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        /// <summary> every triangle with its own three vertices (no edge has two triangles) </summary>
        internal static DMesh3 Unweld(DMesh3 m)
        {
            var r = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i v = m.GetTriangle(t);
                r.AppendTriangle(r.AppendVertex(m.GetVertex(v.a)), r.AppendVertex(m.GetVertex(v.b)), r.AppendVertex(m.GetVertex(v.c)));
            }
            return r;
        }

        /// <summary> unwelded, every 5th triangle reversed, every 7th duplicated reversed (a broken CAD export) </summary>
        static DMesh3 Broken(DMesh3 m)
        {
            var r = new DMesh3();
            int k = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i v = m.GetTriangle(t);
                Vector3d a = m.GetVertex(v.a), b = m.GetVertex(v.b), c = m.GetVertex(v.c);
                if (k % 5 == 0) { var s = b; b = c; c = s; }
                r.AppendTriangle(r.AppendVertex(a), r.AppendVertex(b), r.AppendVertex(c));
                if (k % 7 == 0) r.AppendTriangle(r.AppendVertex(a), r.AppendVertex(c), r.AppendVertex(b));
                k++;
            }
            return r;
        }

        static AlphaWrapFeaturePass Pass(MeshAlphaWrap w, DMesh3 input, bool varifold)
        {
            var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset) { VarifoldCreases = varifold };
            Assert.True(p.Compute());
            return p;
        }

        [Fact]
        public void VarifoldCreases_OffByDefault()
        {
            DMesh3 input = AlphaWrapFeaturePassTests.LShape();
            var p = new AlphaWrapFeaturePass(new DMesh3(), input, 1, 0.1);
            Assert.False(p.VarifoldCreases);
            Assert.False(new MeshRobustRepair(new DMesh3()).AlphaWrapSharpFeaturesOnSoups);
        }

        // a broken soup (unwelded, flipped, duplicated): the concave crease comes back as on the clean mesh, the guarantees hold
        // against the soup itself
        [Fact]
        public void VarifoldCreases_BrokenSoup_RecoversTheCrease_KeepsTheGuarantees()
        {
            DMesh3 soup = Broken(AlphaWrapFeaturePassTests.LShape());
            MeshAlphaWrap w = Wrap(soup);
            var before = Pass(w, soup, false);
            Assert.Equal(0, before.InputCreases);
            Assert.Equal(0, before.CreaseSnaps + before.Splits);
            var after = Pass(w, soup, true);
            Assert.True(after.InputCreases > 0 && after.CreaseSnaps > 0, $"creases {after.InputCreases}, snaps {after.CreaseSnaps}");
            double cov = AlphaWrapFeaturePassTests.ConcaveCoverage(after.Mesh, w.UsedOffset, Similarity.Identity);
            Assert.True(cov > 0.8, $"concave crease coverage {cov}");
            AlphaWrapFeaturePassTests.AssertGuarantees(after.Mesh, soup, w.UsedOffset, "varifold creases on a broken soup");
        }

        // on a welded input the varifold source finds the same crease as the dihedral source
        [Fact]
        public void VarifoldCreases_WeldedInput_AsGoodAsTheDihedral()
        {
            DMesh3 input = AlphaWrapFeaturePassTests.LShape();
            MeshAlphaWrap w = Wrap(input);
            var dih = Pass(w, input, false);
            var vf = Pass(w, input, true);
            double cd = AlphaWrapFeaturePassTests.ConcaveCoverage(dih.Mesh, w.UsedOffset, Similarity.Identity);
            double cv = AlphaWrapFeaturePassTests.ConcaveCoverage(vf.Mesh, w.UsedOffset, Similarity.Identity);
            Assert.True(cv >= cd - 0.05 && cv > 0.8, $"coverage dihedral {cd}, varifold {cv}");
            AlphaWrapFeaturePassTests.AssertGuarantees(vf.Mesh, input, w.UsedOffset, "varifold creases on a welded input");
        }

        // the switch off is the pass before, bit for bit (the default path does not reach the new code)
        [Fact]
        public void VarifoldCreases_Off_IsTheDihedralPass()
        {
            DMesh3 input = AlphaWrapFeaturePassTests.LShape();
            MeshAlphaWrap w = Wrap(input);
            var a = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(a.Compute());
            var b = Pass(w, input, false);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(a.Mesh), AlphaWrapManifoldStepTests.Hash(b.Mesh));
            Assert.Equal(0, b.VarifoldQueries);
        }

        // invariance over Similarity.All() on the broken soup: clean, the crease recovered; 2^k scales the result bit for bit
        [Fact]
        public void VarifoldCreases_Invariance()
        {
            DMesh3 soup = Broken(AlphaWrapFeaturePassTests.LShape());
            MeshAlphaWrap w0 = Wrap(soup, 30);
            var p0 = Pass(w0, soup, true);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = new DMesh3(soup);
                MeshTransforms.PerVertexTransform(m, s.Apply);
                MeshAlphaWrap w = Wrap(m, 30);
                var p = Pass(w, m, true);
                Assert.True(MeshRobustRepair.NotCleanReason(p.Mesh) == "", s.Name + ": " + MeshRobustRepair.NotCleanReason(p.Mesh));
                double cov = AlphaWrapFeaturePassTests.ConcaveCoverage(p.Mesh, w.UsedOffset, s);
                Assert.True(cov > 0.8, s.Name + " " + cov);
                if (s.IsExact) {
                    Assert.Equal(p0.InputCreases, p.InputCreases);
                    Assert.Equal(p0.Mesh.TriangleCount, p.Mesh.TriangleCount);
                    Assert.True(p0.Mesh.VertexIndices().All(v => p.Mesh.GetVertex(v) == s.Apply(p0.Mesh.GetVertex(v))), s.Name);
                    Assert.True(p0.Mesh.TriangleIndices().All(t => p.Mesh.GetTriangle(t) == p0.Mesh.GetTriangle(t)), s.Name);
                }
            }
        }

        // deterministic (a run with every parallel loop of this thread serial gives the same bits) and reusable (Compute
        // again on the same instance gives what a new instance on the same mesh gives; the query asked twice answers the same)
        [Fact]
        public void VarifoldCreases_Deterministic_AndReusable()
        {
            DMesh3 soup = Broken(AlphaWrapFeaturePassTests.LShape());
            MeshAlphaWrap w = Wrap(soup);
            var a = Pass(w, soup, true);
            string hash1;
            bool saved = gParallel.SerialOnThisThread;
            try {
                gParallel.SerialOnThisThread = true;
                MeshAlphaWrap ws = Wrap(soup);
                Assert.Equal(AlphaWrapManifoldStepTests.Hash(w.Result), AlphaWrapManifoldStepTests.Hash(ws.Result));
                hash1 = AlphaWrapManifoldStepTests.Hash(Pass(ws, soup, true).Mesh);
            } finally { gParallel.SerialOnThisThread = saved; }
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(a.Mesh), hash1);
            var again = new AlphaWrapFeaturePass(new DMesh3(a.Mesh, false), soup, w.UsedAlpha, w.UsedOffset) { VarifoldCreases = true };
            Assert.True(again.Compute());
            Assert.True(a.Compute());
            Assert.Equal(again.InputCreases, a.InputCreases);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(new DMesh3(again.Mesh, true)), AlphaWrapManifoldStepTests.Hash(new DMesh3(a.Mesh, true)));
            Assert.True(MeshRobustRepair.NotCleanReason(a.Mesh) == "");

            var q = new VarifoldCreaseQuery(soup, 0.8);
            var x = new Vector3d(5, 5, 2);
            bool y1 = q.IsCrease(x, Vector3d.AxisX, Vector3d.AxisY, 30, 0.05, out double d1);
            bool y2 = q.IsCrease(x, Vector3d.AxisX, Vector3d.AxisY, 30, 0.05, out double d2);
            Assert.True(y1 && y2);
            Assert.Equal(BitConverter.DoubleToInt64Bits(d1), BitConverter.DoubleToInt64Bits(d2));
        }

        // the repair's public switch runs the pass with the varifold source on a soup
        [Fact]
        public void RobustRepair_SharpFeaturesOnSoups_RunsTheVarifoldSource()
        {
            DMesh3 soup = Unweld(MeshRobustRepairTests.ThinPlates(6));
            Func<bool, MeshRobustRepair> run = (on) => {
                var r = new MeshRobustRepair(soup) {
                    RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 32, WrapRelativeAlpha = 40, WrapRelativeOffset = 400,
                    AlphaWrapSharpFeatures = true, AlphaWrapSharpFeaturesOnSoups = on
                };
                Assert.True(MeshRobustRepairTests.WithUnrepairedContour(() => r.Apply()));
                Assert.True(r.UsedAlphaWrap);
                return r;
            };
            var off = run(false);
            var on = run(true);
            Assert.True(on.AlphaWrapSharpFeaturesApplied);
            Assert.Equal("", on.ResultReason);
            Assert.True(MeshRobustRepair.NotCleanReason(on.Mesh) == "");
            Assert.NotEqual(AlphaWrapManifoldStepTests.Hash(off.Mesh), AlphaWrapManifoldStepTests.Hash(on.Mesh));
        }

        // smooth parts (a thin cylinder, radius below the test's ball, and a sphere) next to a box, all unwelded: the varifold
        // source adds no sharp edge on the smooth parts (the box's creases are found)
        [Fact]
        public void VarifoldCreases_SmoothSoup_NoCreasesOnSmoothParts()
        {
            DMesh3 input = AlphaWrapFeaturePassTests.PolyCube(10, 6, 4, (x, y, z) => true);
            DMesh3 cyl = new CappedCylinderGenerator() { BaseRadius = 0.25f, TopRadius = 0.25f, Height = 4, Slices = 64 }.Generate().MakeDMesh();
            MeshTransforms.Translate(cyl, new Vector3d(14, 0, 3));
            AxisAlignedBox3d cb = cyl.CachedBounds;
            DMesh3 sph = new Sphere3Generator_NormalizedCube() { Radius = 1.5, EdgeVertices = 16 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sph, new Vector3d(5, 10, 2));
            AxisAlignedBox3d sb = sph.CachedBounds;
            MeshEditor.Append(input, cyl);
            MeshEditor.Append(input, sph);
            DMesh3 soup = Unweld(input);
            MeshAlphaWrap w = Wrap(soup);
            var p = Pass(w, soup, true);
            Assert.True(p.CreaseSnaps > 0);
            Func<DMesh3, int> sharpOnSmooth = (m) => {
                int n = 0;
                foreach (int eid in m.EdgeIndices()) {
                    Index2i et = m.GetEdgeT(eid);
                    if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) > Math.Cos(45 * MathUtil.Deg2Rad)) continue;
                    Index2i ev = m.GetEdgeV(eid);
                    Vector3d mid = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
                    // (the middle of the cylinder, away from its caps, and the sphere)
                    if (mid.x > 12 && mid.y > cb.Min.y + 1 && mid.y < cb.Max.y - 1) n++;
                    if (mid.y > sb.Min.y - 1) n++;
                }
                return n;
            };
            Assert.Equal(sharpOnSmooth(w.Result), sharpOnSmooth(p.Mesh));
        }

        // ---- the crease test itself ----

        static DMesh3 Grid(Func<double, double, Vector3d> f, int nu, int nv)
        {
            var m = new DMesh3();
            for (int j = 0; j < nv; ++j)
                for (int i = 0; i < nu; ++i) {
                    Vector3d a = f((double)i / nu, (double)j / nv), b = f((double)(i + 1) / nu, (double)j / nv);
                    Vector3d c = f((double)(i + 1) / nu, (double)(j + 1) / nv), d = f((double)i / nu, (double)(j + 1) / nv);
                    m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
                    m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(c), m.AppendVertex(d));
                }
            return m;
        }

        static DMesh3 Join(params DMesh3[] ms)
        {
            var r = new DMesh3();
            foreach (var m in ms) new MeshEditor(r).AppendMesh(m);
            return r;
        }

        // floor z = 0 over x in [-2, 0], and a wall x = 0 rising to z = height (two sheets meeting at the line x = z = 0)
        static DMesh3 FloorAndWall(double height, double wallX = 0, double floorEnd = 0)
        {
            DMesh3 floor = Grid((u, v) => new Vector3d(-2 + (2 + floorEnd) * u, -2 + 4 * v, 0), 20, 40);
            DMesh3 wall = Grid((u, v) => new Vector3d(wallX, -2 + 4 * v, height * u), Math.Max(1, (int)(10 * height)), 40);
            return Join(floor, wall);
        }

        [Fact]
        public void VarifoldCreaseQuery_Crease_Accepted_WithItsDihedral()
        {
            var q = new VarifoldCreaseQuery(FloorAndWall(2), 0.5);
            Assert.True(q.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out double dih));
            Assert.InRange(dih, 89.9, 90.1);
            // a higher threshold than the crease's dihedral: refused
            Assert.False(q.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 95, 0.01, out _));
            // the signs of the normals do not matter
            Assert.True(q.IsCrease(Vector3d.Zero, -Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }

        // inside a face: the second plane has no mass
        [Fact]
        public void VarifoldCreaseQuery_Face_Refused()
        {
            var q = new VarifoldCreaseQuery(FloorAndWall(2), 0.5);
            Assert.False(q.IsCrease(new Vector3d(-1, 0, 0), Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }

        // a lip of the wall 0.04 radius high: a real meeting of two planes, but with about 9 % of the ball's mass (the kernel
        // integrals: the lip 0.406 h, the floor pi r^2 / 10); below the 20 % share a plane needs, above 5 %; a lip of 0.6 radius passes
        [Fact]
        public void VarifoldCreaseQuery_NarrowLip_Refused_WideLip_Accepted()
        {
            var narrow = new VarifoldCreaseQuery(FloorAndWall(0.02), 0.5);
            Assert.Equal(0.2, narrow.MinFraction);
            Assert.False(narrow.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
            // (either plane: the lip as the first normal)
            Assert.False(narrow.IsCrease(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisZ, 30, 0.01, out _));
            narrow.MinFraction = 0.05;
            Assert.True(narrow.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
            Assert.True(narrow.IsCrease(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisZ, 30, 0.01, out _));
            var wide = new VarifoldCreaseQuery(FloorAndWall(0.3), 0.5);
            Assert.True(wide.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }

        // a wall standing 0.2 away from where the floor ends (two sheets that do not meet): both planes have their mass, but
        // the wall's plane is 0.2 from the point; refused, while the same sheets meeting there are accepted
        [Fact]
        public void VarifoldCreaseQuery_SheetsThatDoNotMeet_Refused()
        {
            var apart = new VarifoldCreaseQuery(FloorAndWall(2, 0.2, 0.2), 0.5);
            Assert.False(apart.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
            var meet = new VarifoldCreaseQuery(FloorAndWall(2, 0.2, 0.2), 0.5);
            Assert.True(meet.IsCrease(new Vector3d(0.2, 0, 0), Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }

        // a real crease 0.15 away from the asked point (the wall stands at x = 0.15 on the floor): both planes have their mass,
        // are planar and meet at 90 degrees, but the wall's plane misses the point by 0.15 (tolerance 0.01); refused, and
        // accepted at the crease itself; the same with the normals in the other order
        [Fact]
        public void VarifoldCreaseQuery_CreaseAwayFromThePoint_Refused()
        {
            DMesh3 m = Join(Grid((u, v) => new Vector3d(-2 + 4 * u, -2 + 4 * v, 0), 40, 40), Grid((u, v) => new Vector3d(0.15, -2 + 4 * v, 2 * u), 20, 40));
            var q = new VarifoldCreaseQuery(m, 0.5);
            Assert.False(q.IsCrease(Vector3d.Zero, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
            Assert.False(q.IsCrease(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisZ, 30, 0.01, out _));
            // (the floor's plane misses a point 0.15 above it)
            Assert.False(q.IsCrease(new Vector3d(0.15, 0, 0.15), Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
            Assert.True(q.IsCrease(new Vector3d(0.15, 0, 0), Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }

        // a crease of 45 degrees (a floor and a slope rising at 45 degrees): the dihedral is the turn between the half-planes,
        // 45, not the 135 of the angle between them
        [Fact]
        public void VarifoldCreaseQuery_ObtuseCrease_DihedralIsTheTurn()
        {
            double c = Math.Sqrt(0.5);
            DMesh3 m = Join(Grid((u, v) => new Vector3d(-2 + 2 * u, -2 + 4 * v, 0), 20, 40), Grid((u, v) => new Vector3d(2 * u * c, -2 + 4 * v, 2 * u * c), 20, 40));
            var q = new VarifoldCreaseQuery(m, 0.5);
            Assert.True(q.IsCrease(Vector3d.Zero, Vector3d.AxisZ, new Vector3d(-c, 0, c), 30, 0.01, out double dih));
            Assert.InRange(dih, 44.9, 45.1);
        }

        // a cylinder of radius 0.4 seen at +-30 degrees (the asked planes tangent there): the normals between and beyond the two
        // planes' hold more than MaxBetweenFraction of the ball (a curved surface, not two planes); refused for that reason
        // alone (the tolerance is wide and the rms guard off, so that they do not decide), accepted with the guard off
        [Fact]
        public void VarifoldCreaseQuery_CurvedBetweenThePlanes_Refused()
        {
            const double R = 0.4;
            DMesh3 cyl = Grid((u, v) => { double t = 2 * Math.PI * u; return new Vector3d(R * Math.Cos(t), R * Math.Sin(t), -2 + 4 * v); }, 200, 40);
            var q = new VarifoldCreaseQuery(cyl, 0.5) { MaxRmsFraction = 1 };
            double c = Math.Cos(Math.PI / 6), sn = 0.5;
            Vector3d nA = new Vector3d(c, sn, 0), nB = new Vector3d(c, -sn, 0), x0 = new Vector3d(R / c, 0, 0);
            Assert.False(q.IsCrease(x0, nA, nB, 30, 0.3, out _));
            q.MaxBetweenFraction = 1;
            Assert.True(q.IsCrease(x0, nA, nB, 30, 0.3, out _));
        }

        // a thin cylinder (radius 0.15) inside the ball: each plane's cluster holds two opposite strips 0.3 apart, not a plane
        // (rms 0.15, above MaxRmsFraction of the radius); refused for that reason alone (the between guard off), accepted with
        // the rms guard off
        [Fact]
        public void VarifoldCreaseQuery_ClusterNotPlanar_Refused()
        {
            const double R = 0.15;
            DMesh3 cyl = Grid((u, v) => { double t = 2 * Math.PI * u; return new Vector3d(R * Math.Cos(t), R * Math.Sin(t), -2 + 4 * v); }, 120, 60);
            var q = new VarifoldCreaseQuery(cyl, 0.5) { MaxBetweenFraction = 1 };
            Assert.False(q.IsCrease(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, 30, 0.01, out _));
            q.MaxRmsFraction = 1;
            Assert.True(q.IsCrease(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, 30, 0.01, out _));
        }

        // a fillet of radius 2 (four times the ball) between the floor and the wall: the ball at its middle sees only the
        // cylinder's normals, 45 degrees from both planes; refused
        [Fact]
        public void VarifoldCreaseQuery_WideFillet_Refused()
        {
            const double R = 2;
            DMesh3 fillet = Grid((u, v) => {
                double t = u * Math.PI / 2;
                return new Vector3d(R - R * Math.Cos(t), -2 + 4 * v, R - R * Math.Sin(t));
            }, 60, 40);
            var q = new VarifoldCreaseQuery(fillet, 0.5);
            var mid = new Vector3d(R - R * Math.Cos(Math.PI / 4), 0, R - R * Math.Sin(Math.PI / 4));
            Assert.False(q.IsCrease(mid, Vector3d.AxisZ, Vector3d.AxisX, 30, 0.01, out _));
        }
    }
}
