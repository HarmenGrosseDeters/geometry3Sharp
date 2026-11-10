using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T3: seeded CsCheck property campaigns for MeshRobustRepair and MeshRoundingRepair (net10.0 only; run
    /// protocol and environment variables in CampaignRunner). Cases are printable scenes: 1 to 4 pieces (tetrahedron,
    /// box, sphere, sliver tetrahedron, plate) of sizes 2^-8 .. 2^8 and positions from touching, nearly touching
    /// (gap 2^-g), overlapping to far apart (up to 2^25 sizes), with seeded defects (flipped or duplicated faces,
    /// open boundaries, jitter of a few thousand units in the last place of the piece, where the coordinates allow). Coordinates are rounded once when built; scaling the built scene by 2^s is exact.
    /// Large campaigns (not in CI): G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "Category=Campaign".
    /// Each failing seed found becomes a fixed example test (RepairCampaignFindings).
    /// </summary>
    [Trait("Category", CampaignRunner.Category)]
    public class RepairCampaignTests
    {
        readonly ITestOutputHelper log;
        public RepairCampaignTests(ITestOutputHelper log) { this.log = log; }

        // ---------------------------------------------------------------- scenes

        public sealed class Piece
        {
            public int Kind;    // 0 tetrahedron, 1 box, 2 sphere, 3 sliver tetrahedron, 4 plate
            public int Exp;     // half size 2^Exp
            public int Rel;     // to piece 0: 0 apart (2^Far), 1 touching, 2 nearly touching (gap 2^-Gap), 3 overlapping, 4 a copy of piece 0 turned by 2^-Gap rad and moved by 2^-Gap
            public int Axis;    // direction from piece 0: 0..5 = +x, -x, +y, -y, +z, -z
            public int Gap;     // 2..30
            public int Far;     // 0..24
            public int Thin;    // sliver and plate thickness 2^-Thin of the size, 3..40
            public int Defect;  // flags: 1 flip all faces, 2 flip one face, 4 duplicate one face, 8 drop one face, 16 jitter
            public int Seed;

            public override string ToString()
            {
                string[] kinds = { "tet", "box", "sphere", "sliver", "plate" };
                string rel = Rel == 0 ? $"far 2^{Far}" : Rel == 1 ? "touch" : Rel == 2 ? $"gap 2^-{Gap}" : Rel == 3 ? "overlap" : $"copy 2^-{Gap}";
                return $"{kinds[Kind]}(2^{Exp}, {rel}, axis {Axis}" + (Kind >= 3 ? $", thin 2^-{Thin}" : "") + $", defect {Defect}, seed {Seed})";
            }
        }

        public sealed class Scene
        {
            public Piece[] Pieces;
            /// <summary> pieces one after the other along the axis of the first one that follows piece 0 (so that apart or nearly touching pieces cannot overlap) </summary>
            public bool Chain;
            public override string ToString() => (Chain ? "chain " : "") + "[" + string.Join("; ", Pieces.Select(p => p.ToString())) + "]";

            static readonly Vector3d[] Axes = {
                new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) };

            static DMesh3 Tetrahedron(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
            {
                if ((b - a).Dot((c - a).Cross(d - a)) < 0) { Vector3d t = c; c = d; d = t; }
                var m = new DMesh3();
                int v0 = m.AppendVertex(a), v1 = m.AppendVertex(b), v2 = m.AppendVertex(c), v3 = m.AppendVertex(d);
                m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
                return m;
            }

            static DMesh3 Body(Piece p, Vector3d c)
            {
                double h = Math.ScaleB(1.0, p.Exp);
                double t = Math.ScaleB(h, -p.Thin);
                switch (p.Kind) {
                    case 0:
                        return Tetrahedron(c + new Vector3d(h, h, h), c + new Vector3d(h, -h, -h), c + new Vector3d(-h, h, -h), c + new Vector3d(-h, -h, h));
                    case 1:
                        return new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(h, h, h)) }.Generate().MakeDMesh();
                    case 2: {
                        DMesh3 s = DegenerateFamilies.Sphere(4);
                        foreach (int v in s.VertexIndices()) s.SetVertex(v, c + h * s.GetVertex(v));
                        return s;
                    }
                    case 3:
                        return Tetrahedron(c + new Vector3d(-h, -h, -h), c + new Vector3d(h, -h, -h), c + new Vector3d(-h, h, -h), c + new Vector3d(h / 4, h / 4, -h + t));
                    default:
                        return new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(h, h, t)) }.Generate().MakeDMesh();
                }
            }

            static int Next(ref uint state, int n)
            {
                state = state * 1664525u + 1013904223u;
                return (int)((state >> 8) % (uint)n);
            }

            static Defects.Soup WithDefects(Piece p, DMesh3 body)
            {
                Defects.Soup s = Defects.Soup.From(body);
                uint state = (uint)p.Seed * 2654435761u + 12345u;
                if ((p.Defect & 1) != 0)
                    for (int i = 0; i < s.T.Count; ++i) s.T[i] = new Index3i(s.T[i].a, s.T[i].c, s.T[i].b);
                if ((p.Defect & 2) != 0) {
                    int i = Next(ref state, s.T.Count);
                    s.T[i] = new Index3i(s.T[i].a, s.T[i].c, s.T[i].b);
                }
                if ((p.Defect & 4) != 0) s.T.Add(s.T[Next(ref state, s.T.Count)]);
                if ((p.Defect & 8) != 0 && s.T.Count > 1) s.T.RemoveAt(Next(ref state, s.T.Count));
                if ((p.Defect & 16) != 0) {
                    double unit = Math.ScaleB(Math.ScaleB(1.0, p.Exp), -40);
                    for (int i = 0; i < s.P.Count; ++i)
                        s.P[i] += unit * new Vector3d(Next(ref state, 65) - 32, Next(ref state, 65) - 32, Next(ref state, 65) - 32);
                }
                return s;
            }

            /// <summary> the scene as a mesh, with all coordinates multiplied by 2^shift (exact) </summary>
            public DMesh3 Build(int shift = 0)
            {
                double h0 = Math.ScaleB(1.0, Pieces[0].Exp);
                var soup = new Defects.Soup();
                var centers = new Vector3d[Pieces.Length];
                for (int i = 0; i < Pieces.Length; ++i) {
                    Piece p = Pieces[i];
                    Vector3d c = Vector3d.Zero;
                    if (i > 0 && p.Rel == 4) {
                        // (the copy keeps piece 0's kind and size)
                        var copy = new Piece { Kind = Pieces[0].Kind, Exp = Pieces[0].Exp, Thin = Pieces[0].Thin, Rel = 4, Axis = p.Axis, Gap = p.Gap, Defect = p.Defect, Seed = p.Seed };
                        Defects.Soup cs = WithDefects(copy, Body(copy, Vector3d.Zero));
                        var q = new Quaterniond(Axes[p.Axis].Normalized, Math.ScaleB(1.0, -p.Gap) * MathUtil.Rad2Deg);
                        Vector3d move = Axes[(p.Axis + 2) % 6] * (h0 * Math.ScaleB(1.0, -p.Gap));
                        for (int k = 0; k < cs.P.Count; ++k) cs.P[k] = q * cs.P[k] + move;
                        soup.Append(cs);
                        centers[i] = c;
                        continue;
                    }
                    if (i > 0) {
                        double prev = Chain ? Math.ScaleB(1.0, Pieces[i - 1].Exp) : h0;
                        double r = prev + Math.ScaleB(1.0, p.Exp);
                        double m = p.Rel == 0 ? Math.ScaleB(1.0, p.Far + 1) : p.Rel == 1 ? 1 : p.Rel == 2 ? 1 + Math.ScaleB(1.0, -p.Gap) : 0.5;
                        c = (Chain ? centers[i - 1] : Vector3d.Zero) + Axes[Chain ? Pieces[1].Axis : p.Axis] * (r * m);
                    }
                    centers[i] = c;
                    soup.Append(WithDefects(p, Body(p, c)));
                }
                DMesh3 mesh = soup.ToMesh();
                if (shift != 0)
                    foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, Math.ScaleB(1.0, shift) * mesh.GetVertex(v));
                return mesh;
            }
        }

        // ---------------------------------------------------------------- generators

        static Gen<Piece> GenPiece(Gen<int> kind, Gen<int> rel, Gen<int> defect, int expRange, int maxThin) =>
            Gen.Select(Gen.Select(kind, Gen.Int[-expRange, expRange], rel, Gen.Int[0, 5], (k, e, r, a) => (k, e, r, a)),
                       Gen.Select(Gen.Int[2, 30], Gen.Int[0, 24], Gen.Int[3, maxThin], defect, Gen.Int[0, 1000], (g, f, t, d, s) => (g, f, t, d, s)),
                (x, y) => new Piece { Kind = x.k, Exp = x.e, Rel = x.r, Axis = x.a, Gap = y.g, Far = y.f, Thin = y.t, Defect = y.d, Seed = y.s });

        static Gen<Scene> GenScene(Gen<int> kind, Gen<int> rel, Gen<int> defect, int maxPieces, int expRange, int maxThin, bool chain = false) =>
            Gen.Int[1, maxPieces].SelectMany(n => GenPiece(kind, rel, defect, expRange, maxThin).List[n, n].Select(l => new Scene { Pieces = l.ToArray(), Chain = chain }));

        /// <summary> closed clean solids that do not overlap: apart (far) or nearly touching, sizes and distances far apart </summary>
        static readonly Gen<Scene> GenClean =
            GenScene(Gen.Int[0, 2], Gen.OneOfConst(0, 2), Gen.Const(0), 4, 8, 18, chain: true);

        /// <summary>
        /// everything: all kinds, touching, overlapping, flipped, duplicated, dropped faces, jitter. Slivers and plates are
        /// at most 2^-18 thick (relative): thinner than that, with the frame's scale, the thickness falls below absolute
        /// tolerances of the spatial queries (about 1e-9 and less: F3 in RepairCampaignFindings) and the solidify fallback
        /// is no longer scale invariant bit for bit
        /// </summary>
        static readonly Gen<Scene> GenDirty =
            GenScene(Gen.Int[0, 4], Gen.Int[0, 4], Gen.Int[0, 31], 3, 8, 18);

        /// <summary> the input of the rounding repair: pieces that touch, nearly touch or overlap a little, jittered by units in the last place </summary>
        static readonly Gen<Scene> GenRounding =
            GenScene(Gen.Int[0, 4], Gen.OneOfConst(0, 1, 2, 3, 4, 4, 4), Gen.OneOfConst(0, 16, 16, 2 + 16, 8 + 16), 3, 6, 40);

        /// <summary>
        /// the scale of the robust repair's frame: 2^-4 .. 2^10 times the scene (pieces of half size 2^-8 .. 2^8, so none
        /// smaller than 2^-12 = 2.4e-4). Smaller than that the spatial queries' absolute padding (DMeshAABBTree3 box_eps
        /// 6e-6, the ray window 1e-8) is no longer small against the geometry, and the solidify fallback differs in the
        /// last bits between scales (F3 in RepairCampaignFindings). The rounding repair is scale relative: 2^-10 .. 2^10.
        /// </summary>
        static readonly Gen<int> RobustShift = Gen.Int[0, 7].SelectMany(i => i == 0 ? Gen.OneOfConst(24, 32, 40) : Gen.Int[-20, 10]);

        /// <summary>
        /// the edges of the supported range (docs/QA.md 2.5a) are part of the sample: one case in eight uses a shift near its
        /// upper end (the scenes reach 2^33 before the shift, the alpha wrap fallback ends at 2^128; the robust repair's own
        /// range is wider, 2^400). Not above 2^40 (coordinates beyond about 2^72): F4 in RepairCampaignFindings. Its lower end is held by the absolute tolerances of F3 (above). The rounding repair is
        /// scale relative (2^-200 .. 2^200): its edges are +-100 (the scenes' coordinates reach 2^-52 .. 2^33 before the shift)
        /// </summary>
        static readonly Gen<int> RoundingShift = Gen.Int[0, 7].SelectMany(i => i == 0 ? Gen.OneOfConst(-100, 100, -90, 90) : Gen.Int[-10, 10]);

        /// <summary> outside the supported range (small sample, property 2 of QA.md 2.5a): below 2^-200 and above 2^400 for the robust repair, a subnormal scene included </summary>
        static readonly Gen<int> OutsideShift = Gen.OneOfConst(-1060, -700, -400, 450, 700, 1000);

        // ---------------------------------------------------------------- helpers

        // what the cases of a campaign exercised (logged at its end): a campaign that never reaches a path proves nothing about it
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Coverage = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        static void Cover(string key) => Coverage.AddOrUpdate(key, 1, (k, n) => n + 1);
        void LogCoverage(string campaign)
        {
            log.WriteLine(campaign + " coverage: " + string.Join(", ", Coverage.Where(kv => kv.Key.StartsWith(campaign + ":")).OrderBy(kv => kv.Key).Select(kv => kv.Key.Substring(campaign.Length + 1) + " " + kv.Value)));
        }

        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.ScaleB(1.0, shift) * c.GetVertex(v));
            return c;
        }

        static void WithTimeout(Action a, string what)
        {
            if (Timed.Finishes(a, TimeSpan.FromSeconds(60)) == false) throw new Exception(what + " did not finish in 60 s");
        }

        static string Prefix(string what, string reason) => reason == "" ? "" : what + ": " + reason;

        static string First(params Func<string>[] checks)
        {
            foreach (Func<string> c in checks) { string r = c(); if (r != "") return r; }
            return "";
        }

        // ---------------------------------------------------------------- MeshRobustRepair

        sealed class RobustRun
        {
            public bool Ok;
            public MeshRobustRepair Repair;
            public DMesh3 Result => Repair.Mesh;
            public string Path => $"ok {Ok}, reason '{Repair.ResultReason}', resolve {Repair.UsedExactResolve}, solidify {Repair.Solidify != null}, wrap {Repair.UsedAlphaWrap}, input {Repair.UsedInput}";
        }

        // The fallbacks (solidify, alpha wrap) cost seconds at their default resolution, even for one small piece. The
        // campaign runs them coarser (same code paths, same guarantees: closed, manifold, clean); G3_CAMPAIGN_DEFAULTS=1
        // runs the defaults (large local campaigns).
        static readonly bool DefaultResolution = Environment.GetEnvironmentVariable("G3_CAMPAIGN_DEFAULTS") == "1";

        static RobustRun Robust(DMesh3 input, Action<MeshRobustRepair> configure = null)
        {
            var run = new RobustRun { Repair = new MeshRobustRepair(new DMesh3(input)) };
            if (DefaultResolution == false) {
                run.Repair.SolidifyResolution = 24;
                run.Repair.WrapRelativeAlpha = 16;
                run.Repair.WrapRelativeOffset = 160;
            }
            configure?.Invoke(run.Repair);
            WithTimeout(() => run.Ok = run.Repair.Apply(), "MeshRobustRepair.Apply");
            return run;
        }

        static string Same(RobustRun a, RobustRun b) => First(
            () => a.Ok == b.Ok ? "" : $"ok {a.Ok} vs {b.Ok}",
            () => a.Repair.ResultReason == b.Repair.ResultReason ? "" : $"reason '{a.Repair.ResultReason}' vs '{b.Repair.ResultReason}'",
            () => a.Repair.UsedExactResolve == b.Repair.UsedExactResolve && a.Repair.UsedAlphaWrap == b.Repair.UsedAlphaWrap && a.Repair.UsedInput == b.Repair.UsedInput
                  && (a.Repair.Solidify != null) == (b.Repair.Solidify != null) ? "" : $"path {a.Path} vs {b.Path}",
            () => Invariance.Difference(a.Result, b.Result));

        /// <summary>
        /// The properties of Apply() on any scene: it finishes, the result is finite and valid; true implies clean by
        /// NotCleanReason, and ResultReason is the independent NotCleanReason (an unclean true result only where the
        /// solidify was not wrapped, ResultReason says why); the same input gives the same result bit for bit; the
        /// scene scaled by 2^s gives the result scaled by 2^s bit for bit.
        /// </summary>
        internal static string RobustProperties(Scene scene, int shift)
        {
            // (the edge shifts compare two large scales: the base scene at 2^20 has no feature near the absolute tolerances of the spatial
            // queries, which the scene at scale 1 can have, F3; the scaled run is then 2^(shift - 20) times it)
            int pre = shift > 10 ? 20 : 0;
            DMesh3 input = scene.Build(pre);
            RobustRun a = Robust(input);
            DMesh3 m = a.Result;
            string kind = scene.Chain ? "clean" : "any";
            Cover(kind + ":" + (a.Ok ? "ok" : "false") + "/" + (a.Repair.UsedAlphaWrap ? "wrap" : a.Repair.Solidify != null ? "solidify" : a.Repair.UsedExactResolve ? "resolve" : "repair") + (a.Repair.UsedInput ? "+input" : ""));
            return First(
                () => Invariance.AllFinite(m) ? "" : "result not finite",
                () => !input.CheckValidity(true, FailMode.ReturnOnly) || m.CheckValidity(true, FailMode.ReturnOnly) ? "" : "result invalid (CheckValidity) for a valid input",
                () => a.Ok == false ? "" : Prefix("Apply() true but " + a.Path, NotCleanButTrue(a)),
                // (a repeat on the expensive paths is left to the scaled run: a nondeterminism shows there as well)
                () => a.Repair.Solidify != null || a.Repair.UsedAlphaWrap ? "" : Prefix("repeat", Same(a, Robust(input))),
                () => Prefix($"scale 2^{shift}", SameScaled(a, Robust(Scaled(input, shift - pre)), shift - pre)));
        }

        static string NotCleanButTrue(RobustRun a)
        {
            string reason = MeshRobustRepair.NotCleanReason(a.Result, a.Repair.CheckSelfIntersections);
            if (reason == a.Repair.ResultReason && (reason == "" || a.Repair.WrapReason != "" || a.Repair.AlphaWrapFallback == false)) {
                // (NotCleanReason is the library's own oracle: the clean result is also checked by counts that share no code with it)
                return reason == "" ? IndependentlyClean(a.Result, a.Repair.CheckSelfIntersections) : "";
            }
            return $"NotCleanReason '{reason}', ResultReason '{a.Repair.ResultReason}', WrapReason '{a.Repair.WrapReason}'";
        }

        /// <summary> closed, no bowtie vertex (the triangles around each vertex form one fan), no self-intersections (inside-out components are the library's: a cavity is one) </summary>
        static string IndependentlyClean(DMesh3 m, bool selfIntersections)
        {
            if (m.TriangleCount == 0) return "empty result";
            if (m.BoundaryEdgeIndices().Any()) return "independent check: boundary edges";
            foreach (int v in m.VertexIndices()) {
                int[] tris = m.VtxTrianglesItr(v).ToArray();
                if (tris.Length == 0) continue;
                // the fan of triangles around v, connected across the edges that contain v
                var seen = new HashSet<int> { tris[0] };
                var stack = new Stack<int>();
                stack.Push(tris[0]);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i te = m.GetTriEdges(t);
                    for (int k = 0; k < 3; ++k) {
                        Index2i ev = m.GetEdgeV(te[k]);
                        if (ev.a != v && ev.b != v) continue;
                        Index2i et = m.GetEdgeT(te[k]);
                        int o = et.a == t ? et.b : et.a;
                        if (o >= 0 && seen.Add(o)) stack.Push(o);
                    }
                }
                if (seen.Count != tris.Length) return $"independent check: bowtie vertex {v}";
            }
            if (selfIntersections && MeshRepairMetrics.SelfIntersections(m) > 0) return "independent check: self-intersections";
            return "";
        }

        static string SameScaled(RobustRun a, RobustRun scaled, int shift)
        {
            var back = Scaled(scaled.Result, -shift);
            return First(
                () => a.Ok == scaled.Ok ? "" : $"ok {a.Ok} vs {scaled.Ok} ({a.Path} vs {scaled.Path})",
                () => a.Repair.ResultReason == scaled.Repair.ResultReason ? "" : $"reason '{a.Repair.ResultReason}' vs '{scaled.Repair.ResultReason}'",
                () => a.Repair.UsedExactResolve == scaled.Repair.UsedExactResolve && a.Repair.UsedAlphaWrap == scaled.Repair.UsedAlphaWrap && a.Repair.UsedInput == scaled.Repair.UsedInput
                      && (a.Repair.Solidify != null) == (scaled.Repair.Solidify != null) ? "" : $"path {a.Path} vs {scaled.Path}",
                () => Prefix(a.Path, Invariance.Difference(a.Result, back)));
        }

        /// <summary>
        /// Clean solids that do not overlap (apart or nearly touching, sizes and distances far apart): the repair must
        /// not lose or change a solid. Apply() true implies clean (RobustProperties); every piece's volume is in the
        /// result (matched by its center, within 1e-3 relative; a solid moves only by the rounding bound), and
        /// each part has its own component in the result. Where parts lie so far apart that a triangle of a part is below the degenerate area of all triangles and Apply() is false, the check is skipped (F1b, RepairCampaignFindings).
        /// </summary>
        internal static string CleanSceneProperties(Scene scene, int shift)
        {
            DMesh3 input = scene.Build();
            string general = RobustProperties(scene, shift);
            if (general != "") return general;
            // (CheckLostComponents is off by default: a part too small for MinVolumeIoU may be lost by design)
            RobustRun a = Robust(input, r => r.CheckLostComponents = true);
            if (PartsTooCloseForTheCoordinates(input)) return "";
            // (F1, fixed: where parts lie far apart compared to their size, a part is no longer lost with Apply() true)
            double diag = MeshRepairMetrics.TriangleBounds(input).DiagonalLength;
            bool disparity = input.TriangleIndices().Any(t => input.GetTriArea(t) < 1e-12 * diag * diag);
            // clean solids that do not overlap are repaired (true) or, only where parts lie far apart compared to their
            // size (ScaleDisparity), reported as not clean with the resolved mesh
            if (a.Ok == false && (disparity == false || !a.Repair.ResultReason.StartsWith("scale disparity")))
                return $"Apply() false for clean solids ({a.Path}: {a.Repair.ResultReason})";
            return ComponentVolumesKept(input, a, 1e-3);
        }

        // parts closer to each other than a few units in the last place of their coordinates: rounded onto each other (a gap of 2^-29 of
        // a size 2^-7 part at 1e8), the scene is no longer one of disjoint solids and the repair may weld them
        static bool PartsTooCloseForTheCoordinates(DMesh3 m)
        {
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(m, out int[] _);
            for (int i = 0; i < boxes.Count; ++i)
                for (int j = i + 1; j < boxes.Count; ++j) {
                    double ulp = Math.Max(Math.Max(boxes[i].Min.MaxAbs, boxes[i].Max.MaxAbs), Math.Max(boxes[j].Min.MaxAbs, boxes[j].Max.MaxAbs)) * MathUtil.Epsilon;
                    AxisAlignedBox3d e = boxes[i]; e.Expand(64 * ulp);
                    if (e.Intersects(boxes[j])) return true;
                }
            return false;
        }

        static string ComponentVolumesKept(DMesh3 input, RobustRun a, double tolerance)
        {
            var inComps = new MeshConnectedComponents(input);
            inComps.FindConnectedT();
            var outComps = new MeshConnectedComponents(a.Result);
            outComps.FindConnectedT();
            double Vol(DMesh3 m, MeshConnectedComponents.Component c)
            {
                Vector3d o = m.GetVertex(m.GetTriangle(c.Indices[0]).a);
                double v = 0;
                foreach (int t in c.Indices) {
                    Index3i tv = m.GetTriangle(t);
                    v += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o)) / 6;
                }
                return v;
            }
            Vector3d Center(DMesh3 m, MeshConnectedComponents.Component c)
            {
                var box = AxisAlignedBox3d.Empty;
                foreach (int t in c.Indices) { Index3i tv = m.GetTriangle(t); box.Contain(m.GetVertex(tv.a)); box.Contain(m.GetVertex(tv.b)); box.Contain(m.GetVertex(tv.c)); }
                return box.Center;
            }
            // (a part below 1e-3 of the largest part may be lost by design: MeshRepairMetrics.MinComponentDiagonal, the
            // tolerances of MeshAutoRepair)
            double Diag(DMesh3 m, MeshConnectedComponents.Component c)
            {
                var box = AxisAlignedBox3d.Empty;
                foreach (int t in c.Indices) { Index3i tv = m.GetTriangle(t); box.Contain(m.GetVertex(tv.a)); box.Contain(m.GetVertex(tv.b)); box.Contain(m.GetVertex(tv.c)); }
                return box.DiagonalLength;
            }
            double largest = inComps.Components.Max(c => Diag(input, c));
            var matched = new HashSet<int>();
            foreach (var c in inComps.Components) {
                if (Diag(input, c) < MeshRepairMetrics.MinComponentDiagonal * largest) continue;
                double expected = Vol(input, c);
                Vector3d at = Center(input, c);
                // the output component whose center is nearest
                double best = double.MaxValue, got = 0; int bestIndex = -1, index = 0;
                foreach (var o in outComps.Components) {
                    double d = Center(a.Result, o).Distance(at);
                    if (d < best) { best = d; got = Vol(a.Result, o); bestIndex = index; }
                    index++;
                }
                if (!matched.Add(bestIndex))
                    return $"two parts of the input share one component of the result (at {at}; {a.Path})";
                if (Math.Abs(got - expected) > tolerance * Math.Abs(expected))
                    return $"component at {at} of volume {expected:R} has {got:R} in the result (nearest {best:G3}; {a.Path})";
            }
            return "";
        }

        // ---------------------------------------------------------------- MeshRoundingRepair

        sealed class RoundingRun
        {
            public bool Clean;
            public MeshRoundingRepair Repair;
            public DMesh3 Result => Repair.Mesh;
        }

        static RoundingRun Rounding(DMesh3 input, bool serial = false)
        {
            var run = new RoundingRun { Repair = new MeshRoundingRepair(new DMesh3(input)) };
            WithTimeout(() => {
                gParallel.SerialOnThisThread = serial;
                try { run.Clean = run.Repair.Apply(); } finally { gParallel.SerialOnThisThread = false; }
            }, "MeshRoundingRepair.Apply");
            return run;
        }

        // the counters that Apply reports, as numbers that must agree between runs
        static string Counters(MeshRoundingRepair r) =>
            $"{r.Collapses}/{r.Flips}/{r.Moves}/{r.Zips}/{r.StripZips}/{r.VertexRemovals}/{r.RemovedComponents}/{r.RemainingIntersections}/{r.RemainingDegenerate}/{r.MaxDrift:R}";

        static int DegenerateTriangles(DMesh3 m, double degenerateArea)
        {
            if (m.TriangleCount == 0) return 0;
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(m, out int[] _);
            double diag = boxes.Max(b => b.DiagonalLength);
            // (the repair measures against the largest component's diagonal at its start; half the area keeps clear of the edge)
            return m.TriangleIndices().Count(t => m.GetTriArea(t) < 0.5 * degenerateArea * diag * diag);
        }

        /// <summary>
        /// The properties of MeshRoundingRepair.Apply(): it finishes; the result is finite and valid; the defects do
        /// not grow (self-intersections, boundary edges; closed stays closed) and a clean result has none left by
        /// independent counts; the vertices that remain are at most MaxDrift (up to rounding) from where they were and
        /// MaxDrift is within the bound; repeated, serial and 2^s scaled runs agree bit for bit; applying it again to
        /// the result changes nothing (reuse of the instance).
        /// </summary>
        internal static string RoundingProperties(Scene scene, int shift)
        {
            DMesh3 input = scene.Build();
            int before = MeshRepairMetrics.SelfIntersections(input);
            int boundaryBefore = input.BoundaryEdgeIndices().Count();
            bool closedBefore = input.IsClosed();
            RoundingRun a = Rounding(input);
            DMesh3 m = a.Result;
            Cover("rounding:" + (a.Repair.InitialIntersections > 0 ? "intersecting" : a.Repair.InitialDegenerate > 0 ? "degenerate" : "defect-free"));
            Cover("rounding:" + (a.Clean ? "clean" : "not clean") + (a.Repair.Collapses + a.Repair.Flips + a.Repair.Moves + a.Repair.Zips + a.Repair.StripZips + a.Repair.VertexRemovals > 0 ? "+edits" : ""));
            double maxCoord = input.Vertices().Select(v => Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)))).DefaultIfEmpty(0).Max();
            double slack = 16 * Math.Max(maxCoord, 1e-300) * MathUtil.Epsilon;
            return First(
                () => Invariance.AllFinite(m) ? "" : "result not finite",
                () => !input.CheckValidity(true, FailMode.ReturnOnly) || m.CheckValidity(true, FailMode.ReturnOnly) ? "" : "result invalid (CheckValidity) for a valid input",
                () => a.Clean == a.Repair.IsClean ? "" : "Apply() != IsClean",
                () => a.Repair.RemainingIntersections <= Math.Max(a.Repair.InitialIntersections, 0) ? "" : $"intersections grew {a.Repair.InitialIntersections} -> {a.Repair.RemainingIntersections}",
                () => MeshRepairMetrics.SelfIntersections(m) <= before ? "" : $"self-intersections grew {before} -> {MeshRepairMetrics.SelfIntersections(m)}",
                () => m.BoundaryEdgeIndices().Count() <= boundaryBefore ? "" : "boundary edges grew",
                () => closedBefore == false || m.TriangleCount == 0 || m.IsClosed() ? "" : "a closed input is not closed",
                () => a.Clean == false ? "" : (MeshRepairMetrics.SelfIntersections(m) == 0 ? "" : "clean but self-intersecting"),
                () => a.Clean == false ? "" : (DegenerateTriangles(m, a.Repair.DegenerateArea) == 0 ? "" : "clean but degenerate triangles"),
                () => a.Repair.MaxDrift <= a.Repair.MaxLength * (1 + 1e-9) + slack ? "" : $"MaxDrift {a.Repair.MaxDrift:G4} > MaxLength {a.Repair.MaxLength:G4}",
                () => Displacement(input, m, a.Repair.MaxDrift + slack),
                () => Prefix("repeat", Equal(a, Rounding(input))),
                () => Prefix("serial", Equal(a, Rounding(input, serial: true))),
                () => Prefix($"scale 2^{shift}", EqualScaled(a, Rounding(Scaled(input, shift)), shift)),
                () => Prefix("reuse", Reuse(a)));
        }

        static string Displacement(DMesh3 before, DMesh3 after, double bound)
        {
            foreach (int v in after.VertexIndices()) {
                if (v >= before.MaxVertexID || before.IsVertex(v) == false) continue;
                double d = (after.GetVertex(v) - before.GetVertex(v)).Length;
                if (d > bound) return $"vertex {v} moved {d:G4} > MaxDrift {bound:G4}";
            }
            return "";
        }

        static string Equal(RoundingRun a, RoundingRun b) => First(
            () => a.Clean == b.Clean ? "" : $"clean {a.Clean} vs {b.Clean}",
            () => Counters(a.Repair) == Counters(b.Repair) ? "" : $"counters {Counters(a.Repair)} vs {Counters(b.Repair)}",
            () => Invariance.Difference(a.Result, b.Result));

        static string EqualScaled(RoundingRun a, RoundingRun scaled, int shift) => First(
            () => a.Clean == scaled.Clean ? "" : $"clean {a.Clean} vs {scaled.Clean}",
            () => Counters(a.Repair).Substring(0, Counters(a.Repair).LastIndexOf('/')) == Counters(scaled.Repair).Substring(0, Counters(scaled.Repair).LastIndexOf('/'))
                  ? "" : $"counters {Counters(a.Repair)} vs {Counters(scaled.Repair)}",
            () => Math.ScaleB(scaled.Repair.MaxDrift, -shift) == a.Repair.MaxDrift ? "" : $"MaxDrift {a.Repair.MaxDrift:R} vs {Math.ScaleB(scaled.Repair.MaxDrift, -shift):R}",
            () => Invariance.Difference(a.Result, Scaled(scaled.Result, -shift)));

        static string Reuse(RoundingRun a)
        {
            if (a.Clean == false) return "";
            var before = new DMesh3(a.Result);
            bool again = a.Repair.Apply();
            return First(
                () => again ? "" : "second Apply() not clean after a clean first",
                () => Invariance.Difference(before, a.Result));
        }

        /// <summary>
        /// Property 2 of docs/QA.md 2.5a, for a scene scaled out of the supported range: the robust repair and the alpha wrap
        /// finish (no hang), the result is finite, and there is no false success (true implies clean; the wrap's Success implies a
        /// closed result). The expected answer is the documented status (false / InvalidInput), but only the three properties
        /// are the claim: a module may also repair such a scene.
        /// </summary>
        internal static string OutsideRangeProperties(Scene scene, int shift)
        {
            DMesh3 input = Scaled(scene.Build(), shift);
            RobustRun a = Robust(input);
            string wrapResult = "";
            var wrap = new MeshAlphaWrap(new DMesh3(input)) { RelativeAlpha = 16, RelativeOffset = 160 };
            bool wrapOk = false;
            WithTimeout(() => wrapOk = wrap.Compute(), "MeshAlphaWrap.Compute");
            if (wrapOk) {
                if (wrap.Result == null || !wrap.Result.IsClosed() || !Invariance.AllFinite(wrap.Result)) wrapResult = "wrap Success with a result that is not closed and finite";
            } else if (wrap.Status == MeshAlphaWrap.WrapStatus.Success) wrapResult = "wrap returned false with Status Success";
            return First(
                // (a scene scaled past the doubles has infinite coordinates itself: Apply() must report it and keep the mesh)
                () => Invariance.AllFinite(a.Result) || Invariance.AllFinite(input) == false ? "" : "result not finite",
                () => a.Ok == false ? "" : Prefix("Apply() true outside the range, " + a.Path, NotCleanButTrue(a)),
                () => wrapResult);
        }

        // ---------------------------------------------------------------- campaigns

        [Fact]
        public void Robust_CleanScenes()
        {
            CampaignRunner.Run(log, nameof(RepairCampaignTests), "Robust(clean)",
                GenClean.SelectMany(s => RobustShift.Select(k => (s, k))), c => CleanSceneProperties(c.s, c.k), 8, 101u, 40);
            LogCoverage("clean");
        }


        [Fact]
        public void Robust_AnyScenes()
        {
            CampaignRunner.Run(log, nameof(RepairCampaignTests), "Robust(any)",
                GenDirty.SelectMany(s => RobustShift.Select(k => (s, k))), c => Timing("any", c.s, () => RobustProperties(c.s, c.k)), 4, 102u, 40);
            LogCoverage("any");
        }


        // (the slow cases of a campaign, to keep its CI size within the test time limit)
        string Timing(string what, Scene s, Func<string> property)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string r = property();
            if (watch.ElapsedMilliseconds > 1500) log.WriteLine($"{what}: {watch.ElapsedMilliseconds} ms: {s}");
            return r;
        }

        [Fact]
        public void Range_OutsideTheSupportedRange()
        {
            CampaignRunner.Run(log, nameof(RepairCampaignTests), "Range(outside)",
                GenDirty.SelectMany(s => OutsideShift.Select(k => (s, k))), c => OutsideRangeProperties(c.s, c.k), 6, 104u, 40);
        }

        [Fact]
        public void Rounding_Scenes()
        {
            CampaignRunner.Run(log, nameof(RepairCampaignTests), "Rounding",
                GenRounding.SelectMany(s => RoundingShift.Select(k => (s, k))), c => RoundingProperties(c.s, c.k), 25, 103u, 40);
            LogCoverage("rounding");
        }

    }
}
