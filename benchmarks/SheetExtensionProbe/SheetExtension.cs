using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

// The construction of docs/design/SHEET_EXTENSION.md section 4, with public members only: a sheet that ends short of the
// workpiece's surface is continued to it, by a strip built in doubles and verified exactly. Nothing in this file is specific to
// the probe's cases (a harness outside the repository compiles it as it is). See README.md for the choices the design leaves open.
namespace SheetExtensionProbe
{
    /// <summary> why the extension was refused (None: extended, or nothing to extend) </summary>
    public enum ExtensionRefusal
    {
        None = 0,
        InvalidInput,
        /// <summary> a continued rim vertex is shared by more than two rim edges (a bowtie): refused, no per-wedge points </summary>
        BowtieVertex,
        /// <summary> the two outward normals of a rim vertex cancel, or a rim triangle is degenerate </summary>
        NoDirection,
        /// <summary> the ray of a rim vertex leaves the workpiece nowhere </summary>
        NoExit,
        /// <summary> the first exit is farther than MaxExtension times the sheet's diagonal </summary>
        DepthAboveLimit,
        /// <summary> the exit angle alpha is not above delta plus the margin (the tilted alternative may not exit) </summary>
        GrazingExit,
        /// <summary> a new point is not strictly outside the workpiece (exact winding number, nonzero distance) </summary>
        NewPointNotOutside,
        /// <summary> an outer edge of a quad passes through the workpiece and the subdivision does not get it out </summary>
        OuterEdgeInside,
        /// <summary> (B' - A') . (B - A) is not positive for an outer segment </summary>
        InvertedQuad,
        /// <summary> the strip against the workpiece's surface is not exactly one connected curve per run </summary>
        CurveRule,
        /// <summary> a strip triangle lies in the plane of a workpiece face and overlaps it </summary>
        CoplanarContact,
        /// <summary> the strip meets the sheet beyond the shared rim </summary>
        MeetsSheet,
        /// <summary> the strip meets itself </summary>
        MeetsItself,
        /// <summary> the strip meets another tool or sheet </summary>
        MeetsOtherTool,
        ImprintFailed,
        /// <summary> the strip could not be appended to a manifold mesh </summary>
        ConstructionFailed,
        /// <summary> the sheet role refuses the extended sheet (Report.SheetReason) </summary>
        SheetRoleRefused,
        /// <summary> the cut failed for another reason (budget, rounding) </summary>
        CutFailed,
        /// <summary> U relative to the smaller part is above MaxUOverSmallerPart </summary>
        UncertaintyAboveLimit
    }

    /// <summary> the verifications that can be switched off (for the mutation runs of the probe; all on by default) </summary>
    [Flags]
    public enum Rules
    {
        None = 0,
        Depth = 1,
        Grazing = 2,
        NewPointOutside = 4,
        InvertedQuad = 8,
        OuterEdge = 16,
        CurveRule = 32,
        CoplanarContact = 64,
        MeetsSheet = 128,
        MeetsOtherTool = 256,
        All = 511
    }

    /// <summary> how the continued rim edges are chosen (ByEdge is the design; the others are mutants) </summary>
    public enum RimChoice { ByEdge, ByVertex }

    /// <summary> which exit of the ray is used (First is the design; Last is a mutant) </summary>
    public enum ExitChoice { First, Last }

    /// <summary> the overshoot beyond the exit (Adaptive is the design; Fixed uses MaxOvershoot always: a mutant) </summary>
    public enum OvershootRule { Adaptive, Fixed }

    public sealed class ExtensionOptions
    {
        /// <summary> the limit of the depth g, as a share of the sheet's diagonal (decided by the owner: 5 %) </summary>
        public double MaxExtension = 0.05;
        /// <summary> o_max: the overshoot cap, as a share of the diagonal (default 1 %) </summary>
        public double MaxOvershoot = 0.01;
        /// <summary> the least overshoot, in rounding units of the largest coordinate (default 1024) </summary>
        public double MinOvershootUnits = 1024;
        /// <summary> delta_min: the least tilt of an alternative continuation, in radians (default 0.05, about 3 degrees) </summary>
        public double DeltaMin = 0.05;
        /// <summary> the margin on the exit angle, in radians: alpha must exceed delta + margin (default 0.05) </summary>
        public double AngleMargin = 0.05;
        /// <summary> the limit of U relative to the smaller part (default: none; the design leaves it open) </summary>
        public double MaxUOverSmallerPart = double.PositiveInfinity;
        /// <summary> the depth of the subdivision of an outer edge (2^depth pieces of a rim edge at most; default 14) </summary>
        public int MaxSubdivisionDepth = 14;

        public Rules Verify = Rules.All;
        public RimChoice Rim = RimChoice.ByEdge;
        public ExitChoice Exit = ExitChoice.First;
        public OvershootRule Overshoot = OvershootRule.Adaptive;
        /// <summary> false: no subdivision of an outer edge (a mutant) </summary>
        public bool Subdivide = true;
        /// <summary> true: the strip triangles are oriented against the sheet (a mutant) </summary>
        public bool FlipQuads = false;

        /// <summary> the other tools of the Difference (their roles in OtherRoles, Solid where missing); the strip may meet none of them </summary>
        public IList<DMesh3> OtherTools;
        public IList<ExactMeshBoolean.InputRole> OtherRoles;
        public ExactMeshBoolean.SheetUncutSides UncutSide = ExactMeshBoolean.SheetUncutSides.Refuse;
        /// <summary> run the cut of the sheet role on the extended sheet (the last check; its result is Report.Cut) </summary>
        public bool RunCut = true;
    }

    public sealed class RunReport
    {
        public int Id, Edges;
        /// <summary> the length of the rim of the run (the chosen rim edges) </summary>
        public double Length;
        /// <summary> the largest and the mean depth g (the mean over the run's rim length) </summary>
        public double MaxDepth, MeanDepth;
        public double StripAreaInside = double.NaN;
        /// <summary> the smallest exit angle alpha and the largest delta of the run (radians) </summary>
        public double MinAlpha = double.NaN, MaxDelta;
        /// <summary> the direction estimate U of the run (absolute, in volume units) and relative to the smaller part of the cut </summary>
        public double U, URelative = double.NaN;
        public int CurveComponents = -1;
        /// <summary> the number of distinct ExactMeshImprint.CurveId values of the run's strip (the design's count; differs from CurveComponents where curves branch) </summary>
        public int CurveIds = -1;
        public ExtensionRefusal Refusal;
        public string Witness = "";
    }

    public sealed class ExtensionReport
    {
        public ExtensionRefusal Refusal;
        public string Witness = "";
        public Vector3d WitnessPoint;
        public bool HasWitnessPoint;
        /// <summary> true: strip triangles were added (false with Refusal None: no rim edge meets the workpiece, the sheet is unchanged) </summary>
        public bool Extended;
        public double Diagonal;
        public List<RunReport> Runs = new List<RunReport>();
        public int ChosenEdges, NewVertices, NewTriangles, SubdivisionPoints;
        /// <summary> the largest depth g of any run, absolute and relative to the diagonal </summary>
        public double MaxDepth, MaxDepthRelative;
        public double U, SmallerPart = double.NaN, URelative = double.NaN;
        public int CurveCount, CurveIdCount;
        /// <summary> the extended sheet as built, also where a verification refused it (null before the strip exists) </summary>
        public DMesh3 Candidate;
        public ExactMeshBoolean.SheetRefusal SheetReason;
        public string CutStatus = "";
        public DMesh3 Cut;
        public double ConstructMs, VerifyMs, ImprintMs, CutMs;
    }

    public static class SheetExtension
    {
        /// <summary>
        /// Continues the rim of the sheet to the surface of the closed workpiece. Returns the extended sheet (the original triangles
        /// unchanged, ids kept; the strip added), or the sheet itself when no rim edge meets the workpiece's interior (Refusal None,
        /// Extended false), or null with a refusal and its witness in the report.
        /// </summary>
        public static DMesh3 TryExtend(DMesh3 sheet, DMesh3 workpiece, ExtensionOptions options, out ExtensionReport report)
        {
            var b = new Builder(sheet, workpiece, options ?? new ExtensionOptions());
            DMesh3 r = b.Run();
            report = b.Rep;
            return r;
        }

        /// <summary> the volume of a closed (or nearly closed) mesh, summed against its own first vertex so that a far frame costs no digits </summary>
        public static double Volume(DMesh3 mesh)
        {
            if (mesh == null || mesh.TriangleCount == 0)
                return 0;
            Vector3d o = mesh.GetVertex(mesh.VertexIndices().First());
            double v = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                a -= o; b -= o; c -= o;
                v += a.Dot(b.Cross(c));
            }
            return v / 6;
        }

        sealed class VData
        {
            public int V;
            public Vector3d Pos;
            public bool Inside, OnSurface, HasDir;
            public Vector3d Dir;
            public double G, Alpha, Delta, Kappa;
            public Vector3d P;
            public int Pid = -1;
        }

        sealed class OPoint
        {
            public double S, G, Alpha, Delta;
            public Vector3d P;
            public int Vid = -1;
            public bool OnSurface;
        }

        sealed class REdge
        {
            public int Eid, A, B, Tri, Run;
            public Vector3d PA, PB;
            public double Len;
            public List<double[]> Inside = new List<double[]>();
            public List<OPoint> Outer;
        }

        sealed class Builder
        {
            readonly DMesh3 S, W;
            readonly ExtensionOptions O;
            public readonly ExtensionReport Rep = new ExtensionReport();
            DMeshAABBTree3 tree;
            double D, unit, tolOn, oMin, oMax, maxDepthAbs, tolT;
            readonly Dictionary<int, VData> vdata = new Dictionary<int, VData>();
            readonly Stopwatch clock = new Stopwatch();
            double lap;
            bool failed, quiet;
            ExtensionRefusal pendRefusal; string pendWitness; Vector3d? pendPoint;

            public Builder(DMesh3 sheet, DMesh3 workpiece, ExtensionOptions o) { S = sheet; W = workpiece; O = o; }

            bool Fail(ExtensionRefusal r, string witness, Vector3d? p = null)
            {
                if (failed)
                    return false;
                if (quiet) { pendRefusal = r; pendWitness = witness; pendPoint = p; return false; }
                failed = true;
                Rep.Refusal = r;
                Rep.Witness = witness;
                if (p.HasValue) { Rep.WitnessPoint = p.Value; Rep.HasWitnessPoint = true; }
                return false;
            }

            double Lap() { double now = clock.Elapsed.TotalMilliseconds, d = now - lap; lap = now; return d; }

            // ---------------------------------------------------------------- queries on the workpiece

            double Dist(Vector3d p) { tree.FindNearestTriangle(p, out double d2); return Math.Sqrt(d2); }
            bool Winding(Vector3d p) { return Math.Abs(tree.WindingNumber(p)) > 0.5; }
            bool IsOnSurface(Vector3d p) { return Dist(p) <= tolOn; }
            bool IsInside(Vector3d p) { return Dist(p) > tolOn && Winding(p); }
            // strictly outside: the exact winding number is 0 and no triangle of the workpiece contains the point (exact: the same predicate as the
            // segment test, applied to a degenerate segment); the winding number alone cannot tell a point on the surface from one next to it
            bool StrictlyOutside(Vector3d p)
            {
                if (Winding(p))
                    return false;
                var box = new AxisAlignedBox3d(p);
                bool on = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (bx, depth) => !on && bx.Contains(p),
                    NextTriangleF = tid => {
                        if (on) return;
                        Triangle3d tri = Tri(W, tid);
                        if (ExactTriangleIntersection.SegmentIntersectsTriangle(p, p, ref tri)) on = true;
                    }
                });
                return !on;
            }

            static AxisAlignedBox3d TriBox(Triangle3d t)
            {
                var b = new AxisAlignedBox3d(t.V0);
                b.Contain(t.V1); b.Contain(t.V2);
                return b;
            }

            Triangle3d Tri(DMesh3 m, int t)
            {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                return new Triangle3d(a, b, c);
            }

            // the hits of the ray with the workpiece as groups (equal parameters within tolT: a ray through an edge or vertex), sorted
            List<KeyValuePair<double, List<int>>> RayGroups(Vector3d o, Vector3d d)
            {
                var ray = new Ray3d(o, d);
                var ids = new List<int>();
                tree.FindAllHitTriangles(ray, ids);
                var hits = new List<KeyValuePair<double, int>>();
                foreach (int t in ids) {
                    var intr = new IntrRay3Triangle3(ray, Tri(W, t));
                    double par = intr.Find() ? intr.RayParameter : d.Dot(W.GetTriCentroid(t) - o);
                    hits.Add(new KeyValuePair<double, int>(par, t));
                }
                hits.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
                var groups = new List<KeyValuePair<double, List<int>>>();
                foreach (var h in hits) {
                    if (groups.Count > 0 && h.Key - groups[groups.Count - 1].Key <= tolT)
                        groups[groups.Count - 1].Value.Add(h.Value);
                    else
                        groups.Add(new KeyValuePair<double, List<int>>(h.Key, new List<int> { h.Value }));
                }
                return groups;
            }

            bool SegmentTouchesW(Vector3d p, Vector3d q)
            {
                var box = new AxisAlignedBox3d(p);
                box.Contain(q);
                box.Expand(tolT);
                bool hit = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (bx, depth) => !hit && bx.Intersects(box),
                    NextTriangleF = tid => {
                        if (hit) return;
                        Triangle3d tri = Tri(W, tid);
                        if (ExactTriangleIntersection.SegmentIntersectsTriangle(p, q, ref tri)) hit = true;
                    }
                });
                return hit;
            }

            // the parts of the segment A-B (as fractions) that lie inside the workpiece (not on its surface)
            List<double[]> InsideIntervals(Vector3d A, Vector3d B)
            {
                var res = new List<double[]>();
                double len = (B - A).Length;
                if (len <= tolOn)
                    return res;
                Vector3d dir = (B - A) / len;
                var ray = new Ray3d(A, dir);
                var ids = new List<int>();
                tree.FindAllHitTriangles(ray, ids, len + tolT);
                var ts = new List<double> { 0, len };
                foreach (int t in ids) {
                    var intr = new IntrRay3Triangle3(ray, Tri(W, t));
                    if (intr.Find() && intr.RayParameter > 0 && intr.RayParameter < len)
                        ts.Add(intr.RayParameter);
                }
                ts.Sort();
                for (int i = 0; i + 1 < ts.Count; ++i) {
                    double a = ts[i], b = ts[i + 1];
                    if (b - a <= tolOn)
                        continue;
                    if (!IsInside(A + dir * ((a + b) / 2)))
                        continue;
                    if (res.Count > 0 && res[res.Count - 1][1] >= a / len - 1e-15)
                        res[res.Count - 1][1] = b / len;
                    else
                        res.Add(new[] { a / len, b / len });
                }
                return res;
            }

            // ---------------------------------------------------------------- the rim

            double KappaAt(int v)
            {
                double k = 0;
                foreach (int e in S.VtxEdgesItr(v)) {
                    if (!S.IsBoundaryEdge(e))
                        continue;
                    int t = S.GetEdgeT(e).a;
                    Vector3d n = S.GetTriNormal(t), c = S.GetTriCentroid(t);
                    Index3i nb = S.GetTriNeighbourTris(t);
                    for (int i = 0; i < 3; ++i) {
                        if (nb[i] < 0) continue;
                        double ang = Math.Acos(Math.Max(-1, Math.Min(1, n.Dot(S.GetTriNormal(nb[i])))));
                        double dist = (S.GetTriCentroid(nb[i]) - c).Length;
                        if (dist > 0) k = Math.Max(k, ang / dist);
                    }
                }
                return k;
            }

            Vector3d OutwardNormal(int e, out bool ok)
            {
                Index2i ab = S.GetOrientedBoundaryEdgeV(e);
                Vector3d A = S.GetVertex(ab.a), B = S.GetVertex(ab.b);
                Vector3d N = S.GetTriNormal(S.GetEdgeT(e).a);
                Vector3d n = (B - A).Cross(N);
                double l = n.Length;
                ok = l > 0 && N.LengthSquared > 0.5;
                return ok ? n / l : Vector3d.Zero;
            }

            VData GetV(int v)
            {
                if (vdata.TryGetValue(v, out VData vd))
                    return vd;
                vd = new VData { V = v, Pos = S.GetVertex(v) };
                vdata[v] = vd;
                vd.Inside = IsInside(vd.Pos);
                vd.OnSurface = !vd.Inside && IsOnSurface(vd.Pos);
                return vd;
            }

            // the unit direction of a rim vertex; false (and a refusal) for a bowtie or cancelling normals
            bool Direction(VData vd)
            {
                if (vd.HasDir)
                    return true;
                var bnd = new List<int>();
                foreach (int e in S.VtxEdgesItr(vd.V))
                    if (S.IsBoundaryEdge(e)) bnd.Add(e);
                if (bnd.Count != 2)
                    return Fail(ExtensionRefusal.BowtieVertex, "rim vertex " + vd.V + " has " + bnd.Count + " rim edges", vd.Pos);
                Vector3d n1 = OutwardNormal(bnd[0], out bool ok1), n2 = OutwardNormal(bnd[1], out bool ok2);
                Vector3d s = n1 + n2;
                if (!ok1 || !ok2 || s.Length < 1e-9)
                    return Fail(ExtensionRefusal.NoDirection, "rim vertex " + vd.V + ": the outward normals cancel", vd.Pos);
                vd.Dir = s.Normalized;
                vd.HasDir = true;
                vd.Kappa = KappaAt(vd.V);
                return true;
            }

            double Overshoot(double gap)
            {
                if (O.Overshoot == OvershootRule.Fixed)
                    return oMax;
                return Math.Max(oMin, Math.Min(oMax, gap / 2));
            }

            // the ray from p along d to its exit; fills g, alpha, delta, the new point P; false with a refusal
            bool Ray(Vector3d p, Vector3d d, double kappa, out double g, out double alpha, out double delta, out Vector3d P)
            {
                g = alpha = delta = 0; P = p;
                var groups = RayGroups(p, d);
                if (groups.Count == 0)
                    return Fail(ExtensionRefusal.NoExit, "the ray leaves the workpiece nowhere", p);
                int gi = O.Exit == ExitChoice.First ? 0 : groups.Count - 1;
                g = groups[gi].Key;
                double gap = gi + 1 < groups.Count ? groups[gi + 1].Key - g : double.PositiveInfinity;
                if ((O.Verify & Rules.Depth) != 0 && g > maxDepthAbs)
                    return Fail(ExtensionRefusal.DepthAboveLimit, "depth " + g.ToString("R") + " = " + (g / D).ToString("0.####") + " of the diagonal", p);
                alpha = double.PositiveInfinity;
                foreach (int t in groups[gi].Value) {
                    double s = Math.Abs(d.Dot(W.GetTriNormal(t)));
                    alpha = Math.Min(alpha, Math.Asin(Math.Min(1, s)));
                }
                delta = Math.Max(O.DeltaMin, kappa * g);
                if ((O.Verify & Rules.Grazing) != 0 && alpha <= delta + O.AngleMargin)
                    return Fail(ExtensionRefusal.GrazingExit, "exit angle " + (alpha * 180 / Math.PI).ToString("0.##") + " deg, delta " + (delta * 180 / Math.PI).ToString("0.##") + " deg", p + d * g);
                P = p + d * (g + Overshoot(gap));
                if ((O.Verify & Rules.NewPointOutside) != 0 && !StrictlyOutside(P))
                    return Fail(ExtensionRefusal.NewPointNotOutside, "a new point is not strictly outside the workpiece (exit at depth " + g.ToString("R") + ", next surface " + gap.ToString("R") + " beyond; winding " + tree.WindingNumber(P).ToString("R") + ", distance " + Dist(P).ToString("R") + ")", P);
                return true;
            }

            bool Prepare(VData vd)
            {
                if (!Direction(vd))
                    return false;
                if (vd.Inside && vd.G == 0 && vd.Pid == -1) {
                    if (!Ray(vd.Pos, vd.Dir, vd.Kappa, out vd.G, out vd.Alpha, out vd.Delta, out vd.P))
                        return false;
                }
                return true;
            }

            OPoint EndPoint(VData vd)
            {
                if (vd.G > 0)
                    return new OPoint { P = vd.P, G = vd.G, Alpha = vd.Alpha, Delta = vd.Delta };
                return new OPoint { P = vd.Pos, OnSurface = vd.OnSurface };
            }

            bool OuterTouches(OPoint p, OPoint q)
            {
                Vector3d a = p.P, b = q.P;
                // an end on the workpiece's surface (a rim vertex exactly on it, g = 0) is not a contact of the segment
                if (p.OnSurface) a = a + (b - a) * 1e-7;
                if (q.OnSurface) b = b + (a - b) * 1e-7;
                return SegmentTouchesW(a, b);
            }

            bool Refine(REdge e, VData va, VData vb, int i, int depth)
            {
                if (!OuterTouches(e.Outer[i], e.Outer[i + 1]))
                    return true;
                bool strict = (O.Verify & Rules.OuterEdge) != 0;
                if (depth >= O.MaxSubdivisionDepth)
                    return !strict || Fail(ExtensionRefusal.OuterEdgeInside, "the outer edge of rim edge " + e.Eid + " passes through the workpiece after " + depth + " subdivisions", e.Outer[i].P);
                double s0 = e.Outer[i].S, s1 = e.Outer[i + 1].S;
                // the longest stretch of the rim edge inside the workpiece between the two points; tried at its middle, then at two off-centre
                // places (a new point that lands exactly on a face of the workpiece is no exit that can be used, and a nearby one often is)
                double best = 0, lo0 = 0, hi0 = 0;
                foreach (double[] iv in e.Inside) {
                    double lo = Math.Max(iv[0], s0), hi = Math.Min(iv[1], s1);
                    if (hi - lo > best) { best = hi - lo; lo0 = lo; hi0 = hi; }
                }
                if (best <= 0)
                    return !strict || Fail(ExtensionRefusal.OuterEdgeInside, "no inside part of rim edge " + e.Eid + " between the points of a failing outer segment", e.Outer[i].P);
                OPoint pt = null;
                pendRefusal = ExtensionRefusal.None;
                foreach (double f in new[] { 0.5, 0.4142135623730951, 0.5857864376269049 }) {
                    double s = lo0 + (hi0 - lo0) * f;
                    Vector3d M = e.PA + (e.PB - e.PA) * s;
                    Vector3d d = (va.Dir * (1 - s) + vb.Dir * s);
                    d = d.Length > 1e-9 ? d.Normalized : va.Dir;
                    if (!IsInside(M))
                        continue;
                    var cand = new OPoint { S = s };
                    quiet = true;
                    bool ok = Ray(M, d, Math.Max(va.Kappa, vb.Kappa), out cand.G, out cand.Alpha, out cand.Delta, out cand.P);
                    quiet = false;
                    if (ok) { pt = cand; break; }
                }
                if (pt == null) {
                    if (!strict) return true;
                    return Fail(pendRefusal != ExtensionRefusal.None ? pendRefusal : ExtensionRefusal.OuterEdgeInside, pendRefusal != ExtensionRefusal.None ? pendWitness : "no usable point of subdivision on rim edge " + e.Eid, pendPoint ?? e.Outer[i].P);
                }
                e.Outer.Insert(i + 1, pt);
                Rep.SubdivisionPoints++;
                if (!Refine(e, va, vb, i, depth + 1))
                    return false;
                int idx = e.Outer.IndexOf(pt);
                return Refine(e, va, vb, idx, depth + 1);
            }

            // ---------------------------------------------------------------- the whole construction

            public DMesh3 Run()
            {
                clock.Start();
                if (S == null || W == null || S.TriangleCount == 0 || W.TriangleCount == 0) {
                    Fail(ExtensionRefusal.InvalidInput, "empty input");
                    return null;
                }
                if (!ExactMeshBoolean.IsWindingClosed(W)) {
                    Fail(ExtensionRefusal.InvalidInput, "the workpiece is not closed");
                    return null;
                }
                AxisAlignedBox3d sb = S.GetBounds(), wb = W.GetBounds();
                D = sb.Diagonal.Length;
                Rep.Diagonal = D;
                double scale = 0;
                foreach (AxisAlignedBox3d bx in new[] { sb, wb })
                    scale = Math.Max(scale, Math.Max(Math.Max(Math.Abs(bx.Min.x), Math.Abs(bx.Max.x)), Math.Max(Math.Max(Math.Abs(bx.Min.y), Math.Abs(bx.Max.y)), Math.Max(Math.Abs(bx.Min.z), Math.Abs(bx.Max.z)))));
                if (scale == 0) scale = 1;
                unit = Math.Pow(2, -52) * scale;
                tolOn = 64 * unit;
                tolT = 16 * unit;
                oMin = O.MinOvershootUnits * unit;
                oMax = Math.Max(O.MaxOvershoot * D, oMin);
                maxDepthAbs = O.MaxExtension * D;
                tree = new DMeshAABBTree3(W, true);

                // 1. where: the rim edges that meet the interior of the workpiece
                var edges = new List<REdge>();
                foreach (int eid in S.BoundaryEdgeIndices()) {
                    Index2i ab = S.GetOrientedBoundaryEdgeV(eid);
                    var e = new REdge { Eid = eid, A = ab.a, B = ab.b, Tri = S.GetEdgeT(eid).a };
                    e.PA = S.GetVertex(e.A); e.PB = S.GetVertex(e.B);
                    e.Len = (e.PB - e.PA).Length;
                    e.Inside = InsideIntervals(e.PA, e.PB);
                    bool chosen = O.Rim == RimChoice.ByEdge ? e.Inside.Count > 0 : (GetV(e.A).Inside || GetV(e.B).Inside);
                    if (chosen) edges.Add(e);
                }
                Rep.ChosenEdges = edges.Count;
                if (edges.Count == 0) {
                    Rep.ConstructMs = Lap();
                    return RunCutOnly();
                }

                // 2./3. direction, ray, depth per rim vertex of a chosen edge
                foreach (REdge e in edges) {
                    if (!Prepare(GetV(e.A)) || !Prepare(GetV(e.B)))
                        return Finish(null);
                }
                // runs: chosen edges joined at a vertex with g > 0
                var uf = new int[edges.Count];
                for (int i = 0; i < uf.Length; ++i) uf[i] = i;
                Func<int, int> find = null;
                find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
                var atVertex = new Dictionary<int, int>();
                for (int i = 0; i < edges.Count; ++i)
                    foreach (int v in new[] { edges[i].A, edges[i].B }) {
                        if (vdata[v].G <= 0) continue;
                        if (atVertex.TryGetValue(v, out int j)) uf[find(i)] = find(j); else atVertex[v] = i;
                    }
                var runIds = new Dictionary<int, int>();
                var runs = new List<RunReport>();
                for (int i = 0; i < edges.Count; ++i) {
                    int r = find(i);
                    if (!runIds.TryGetValue(r, out int id)) { runIds[r] = id = runs.Count; runs.Add(new RunReport { Id = id }); }
                    edges[i].Run = id;
                }
                Rep.Runs = runs;

                // 4. the outer polyline of each edge, subdivided where it passes through the workpiece
                foreach (REdge e in edges) {
                    VData va = vdata[e.A], vb = vdata[e.B];
                    OPoint pa = EndPoint(va), pb = EndPoint(vb);
                    pa.S = 0; pb.S = 1;
                    e.Outer = new List<OPoint> { pa, pb };
                    if (O.Subdivide) {
                        if (!Refine(e, va, vb, 0, 0))
                            return Finish(null);
                    }
                    else if ((O.Verify & Rules.OuterEdge) != 0 && OuterTouches(pa, pb)) {
                        Fail(ExtensionRefusal.OuterEdgeInside, "the outer edge of rim edge " + e.Eid + " passes through the workpiece (no subdivision)", pa.P);
                        return Finish(null);
                    }
                }
                Rep.ConstructMs += Lap();

                // 5a. verification before the mesh exists: inverted quads (the new points were verified where they were made)
                if ((O.Verify & Rules.InvertedQuad) != 0) {
                    // no strip triangle is turned against the sheet triangle it continues (a quad whose outer edge runs backwards folds over)
                    foreach (REdge e in edges) {
                        Vector3d nSheet = S.GetTriNormal(e.Tri);
                        foreach (Index3i lt in StripLocal(e.Outer.Count - 1)) {
                            Vector3d a = LocalPos(e, lt.a), b = LocalPos(e, lt.b), c = LocalPos(e, lt.c);
                            if (a == b || b == c || a == c) continue;
                            Vector3d n = (b - a).Cross(c - a);
                            double l = n.Length;
                            if (l > 0 && n.Dot(nSheet) / l > 1e-9) continue;
                            Fail(ExtensionRefusal.InvertedQuad, "a strip triangle of rim edge " + e.Eid + " is turned against the sheet (or degenerate): " + a + " " + b + " " + c, a);
                            return Finish(null);
                        }
                    }
                }
                Rep.VerifyMs += Lap();

                // the strip
                DMesh3 ext = new DMesh3(S);
                int tid0 = S.MaxTriangleID;
                if (ext.MaxVertexID != S.MaxVertexID || ext.MaxTriangleID != S.MaxTriangleID) {
                    Fail(ExtensionRefusal.ConstructionFailed, "the copy of the sheet changed its ids");
                    return Finish(null);
                }
                int nv0 = ext.VertexCount;
                var stripTris = new List<List<Index3i>>();
                for (int i = 0; i < runs.Count; ++i) stripTris.Add(new List<Index3i>());
                foreach (REdge e in edges) {
                    VData va = vdata[e.A], vb = vdata[e.B];
                    foreach (OPoint p in e.Outer) {
                        if (p.S == 0) p.Vid = va.G > 0 ? Pid(ext, va) : e.A;
                        else if (p.S == 1) p.Vid = vb.G > 0 ? Pid(ext, vb) : e.B;
                        else p.Vid = ext.AppendVertex(p.P);
                    }
                    var tris = new List<Index3i>();
                    int nseg = e.Outer.Count - 1;
                    foreach (Index3i lt in StripLocal(nseg))
                        tris.Add(new Index3i(LocalVid(e, lt.a), LocalVid(e, lt.b), LocalVid(e, lt.c)));
                    foreach (Index3i t in tris) {
                        if (t.a == t.b || t.b == t.c || t.a == t.c)
                            continue;
                        Index3i tt = O.FlipQuads ? new Index3i(t.a, t.c, t.b) : t;
                        if (ext.AppendTriangle(tt) < 0) {
                            Fail(ExtensionRefusal.ConstructionFailed, "a strip triangle cannot be appended to a manifold mesh", ext.GetVertex(t.a));
                            Rep.Candidate = null;
                            return Finish(null);
                        }
                        stripTris[e.Run].Add(tt);
                    }
                }
                Rep.NewVertices = ext.VertexCount - nv0;
                Rep.NewTriangles = ext.TriangleCount - S.TriangleCount;
                Rep.Candidate = ext;
                Rep.Extended = true;
                RunStats(edges, runs);
                Rep.ConstructMs += Lap();

                // 5b. the curve rule per run (and the coplanar contact)
                int totalCurves = 0;
                for (int r = 0; r < runs.Count; ++r) {
                    var strip = new DMesh3();
                    var map = new Dictionary<int, int>();
                    foreach (Index3i t in stripTris[r]) {
                        var l = new int[3];
                        for (int k = 0; k < 3; ++k) {
                            if (!map.TryGetValue(t[k], out l[k])) map[t[k]] = l[k] = strip.AppendVertex(ext.GetVertex(t[k]));
                        }
                        strip.AppendTriangle(l[0], l[1], l[2]);
                    }
                    if ((O.Verify & Rules.CoplanarContact) != 0 && Coplanar(strip, out Vector3d at)) {
                        runs[r].Refusal = ExtensionRefusal.CoplanarContact;
                        Fail(ExtensionRefusal.CoplanarContact, "run " + r + ": a strip triangle lies in the plane of a face of the workpiece and overlaps it", at);
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                    Rep.VerifyMs += Lap();
                    var imp = new ExactMeshImprint { Target = strip, Cutter = W, LabelCurves = true };
                    imp.Compute();
                    Rep.ImprintMs += Lap();
                    if (imp.Status != ExactMeshImprint.ImprintStatus.Success) {
                        Fail(ExtensionRefusal.ImprintFailed, "run " + r + ": the imprint of the strip failed: " + imp.Status + " " + imp.Error);
                        return Finish(null);
                    }
                    int comps = Components(imp, out Vector3d second);
                    runs[r].CurveComponents = comps;
                    runs[r].CurveIds = imp.CurveId.Values.Distinct().Count();
                    Rep.CurveIdCount += runs[r].CurveIds;
                    totalCurves += comps;
                    double area = 0;
                    foreach (int t in imp.Result.TriangleIndices()) {
                        Vector3d c = imp.Result.GetTriCentroid(t);
                        if (IsInside(c)) area += imp.Result.GetTriArea(t);
                    }
                    runs[r].StripAreaInside = area;
                    if ((O.Verify & Rules.CurveRule) != 0 && comps != 1) {
                        runs[r].Refusal = ExtensionRefusal.CurveRule;
                        Fail(ExtensionRefusal.CurveRule, "run " + r + ": the strip meets the surface in " + comps + " curves, not 1", comps > 1 ? second : (Vector3d?)null);
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                    Rep.VerifyMs += Lap();
                }
                Rep.CurveCount = totalCurves;

                // 5c. the strip meets neither the sheet, nor itself, nor another tool
                if ((O.Verify & Rules.MeetsSheet) != 0) {
                    foreach (Index2i pr in MeshRepairMetrics.SelfIntersectionPairs(ext)) {
                        bool n0 = pr.a >= tid0, n1 = pr.b >= tid0;
                        if (!n0 && !n1) continue;
                        Fail(n0 && n1 ? ExtensionRefusal.MeetsItself : ExtensionRefusal.MeetsSheet, "triangles " + pr.a + " and " + pr.b + " of the extended sheet intersect", ext.GetTriCentroid(n0 ? pr.a : pr.b));
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                }
                if ((O.Verify & Rules.MeetsOtherTool) != 0 && O.OtherTools != null) {
                    foreach (DMesh3 tool in O.OtherTools) {
                        var tt = new DMeshAABBTree3(tool, true);
                        bool hit = false; Vector3d at = Vector3d.Zero;
                        foreach (int t in ext.TriangleIndices()) {
                            if (t < tid0 || hit) continue;
                            Triangle3d a = Tri(ext, t);
                            var box = TriBox(a);
                            tt.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                                NextBoxDF = (bx, depth) => !hit && bx.Intersects(box),
                                NextTriangleF = tid => {
                                    if (hit) return;
                                    Triangle3d b = Tri(tool, tid);
                                    if (ExactTriangleIntersection.Intersects(ref a, ref b)) { hit = true; at = ext.GetTriCentroid(t); }
                                }
                            });
                        }
                        if (hit) {
                            Fail(ExtensionRefusal.MeetsOtherTool, "the strip meets another tool", at);
                            Rep.VerifyMs += Lap();
                            return Finish(null);
                        }
                    }
                }
                Rep.VerifyMs += Lap();

                // 5d. the sheet role accepts the extended sheet
                if (O.RunCut) {
                    if (!Cut(ext, runs))
                        return Finish(null);
                }
                return Finish(ext);
            }

            // the triangles of the strip of one rim edge with n outer segments, as local corner numbers: 0 = A, k + 1 = outer point k, n + 2 = B;
            // the polygon A, P0 .. Pn, B (the closing edge B-A is the rim edge) is split by a fan from A up to the middle point and from B after it
            static List<Index3i> StripLocal(int n)
            {
                int m = n / 2;
                var tris = new List<Index3i>();
                for (int k = 0; k < m; ++k) tris.Add(new Index3i(0, k + 1, k + 2));
                tris.Add(new Index3i(0, m + 1, n + 2));
                for (int k = m; k < n; ++k) tris.Add(new Index3i(k + 1, k + 2, n + 2));
                return tris;
            }

            static Vector3d LocalPos(REdge e, int i) { return i == 0 ? e.PA : (i == e.Outer.Count + 1 ? e.PB : e.Outer[i - 1].P); }
            static int LocalVid(REdge e, int i) { return i == 0 ? e.A : (i == e.Outer.Count + 1 ? e.B : e.Outer[i - 1].Vid); }

            DMesh3 RunCutOnly()
            {
                // no rim edge meets the interior: nothing to extend; the cut of the sheet as it is (the role decides)
                if (O.RunCut)
                    Cut(S, Rep.Runs);
                return Finish(S);
            }

            DMesh3 Finish(DMesh3 result)
            {
                if (failed)
                    return null;
                return result;
            }

            int Pid(DMesh3 ext, VData vd)
            {
                if (vd.Pid < 0) vd.Pid = ext.AppendVertex(vd.P);
                return vd.Pid;
            }

            bool Coplanar(DMesh3 strip, out Vector3d at)
            {
                bool found = false; Vector3d where = Vector3d.Zero;
                foreach (int t in strip.TriangleIndices()) {
                    Triangle3d a = Tri(strip, t);
                    var box = TriBox(a);
                    tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                        NextBoxDF = (bx, depth) => !found && bx.Intersects(box),
                        NextTriangleF = tid => {
                            if (found) return;
                            Triangle3d b = Tri(W, tid);
                            if (ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V0) != 0 || ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V1) != 0 || ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V2) != 0)
                                return;
                            if (ExactTriangleIntersection.Intersects(ref a, ref b)) { found = true; where = (a.V0 + a.V1 + a.V2) / 3; }
                        }
                    });
                    if (found) break;
                }
                at = where;
                return found;
            }

            // connected components of the curve edges of an imprint (edges that share a vertex are one component)
            static int Components(ExactMeshImprint imp, out Vector3d second)
            {
                var parent = new Dictionary<int, int>();
                Func<int, int> find = null;
                find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                foreach (long k in imp.CurveEdges) {
                    int a = (int)(k >> 32), b = (int)(k & 0xFFFFFFFF);
                    if (!parent.ContainsKey(a)) parent[a] = a;
                    if (!parent.ContainsKey(b)) parent[b] = b;
                    parent[find(a)] = find(b);
                }
                var roots = new Dictionary<int, int>();
                second = Vector3d.Zero;
                foreach (int v in parent.Keys.OrderBy(x => x)) {
                    int r = find(v);
                    if (!roots.ContainsKey(r)) {
                        roots[r] = v;
                        if (roots.Count == 2) second = imp.Result.GetVertex(v);
                    }
                }
                return roots.Count;
            }

            // the per-run depth, direction estimate and length (trapezoids over the polyline points)
            void RunStats(List<REdge> edges, List<RunReport> runs)
            {
                var gInt = new double[runs.Count];
                foreach (REdge e in edges) {
                    RunReport r = runs[e.Run];
                    r.Edges++;
                    r.Length += e.Len;
                    for (int i = 0; i + 1 < e.Outer.Count; ++i) {
                        OPoint p = e.Outer[i], q = e.Outer[i + 1];
                        double w = (q.S - p.S) * e.Len;
                        gInt[e.Run] += w * (p.G + q.G) / 2;
                        r.U += w * (Unit(p) + Unit(q)) / 2;
                    }
                    foreach (OPoint p in e.Outer) {
                        r.MaxDepth = Math.Max(r.MaxDepth, p.G);
                        if (p.G > 0) {
                            r.MinAlpha = double.IsNaN(r.MinAlpha) ? p.Alpha : Math.Min(r.MinAlpha, p.Alpha);
                            r.MaxDelta = Math.Max(r.MaxDelta, p.Delta);
                        }
                    }
                }
                for (int i = 0; i < runs.Count; ++i) {
                    runs[i].MeanDepth = runs[i].Length > 0 ? gInt[i] / runs[i].Length : 0;
                    Rep.U += runs[i].U;
                    Rep.MaxDepth = Math.Max(Rep.MaxDepth, runs[i].MaxDepth);
                }
                Rep.MaxDepthRelative = Rep.MaxDepth / D;
            }

            // the first-order estimate of the cut's change per unit of rim for a continuation tilted by delta (design section 5)
            static double Unit(OPoint p)
            {
                if (p.G <= 0 || p.Alpha <= p.Delta)
                    return 0;
                return 0.5 * p.G * p.G * Math.Sin(p.Delta) * Math.Sin(p.Alpha) / Math.Sin(p.Alpha - p.Delta);
            }

            bool Cut(DMesh3 sheet, List<RunReport> runs)
            {
                var meshes = new List<DMesh3> { W, sheet };
                var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
                if (O.OtherTools != null)
                    for (int i = 0; i < O.OtherTools.Count; ++i) {
                        meshes.Add(O.OtherTools[i]);
                        roles.Add(O.OtherRoles != null && i < O.OtherRoles.Count ? O.OtherRoles[i] : ExactMeshBoolean.InputRole.Solid);
                    }
                Lap();
                var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) {
                    AllowOpenMeshes = true, Roles = roles, SheetUncutSide = O.UncutSide
                };
                op.Compute();
                Rep.CutMs += Lap();
                Rep.CutStatus = op.Status.ToString();
                if (op.Status == ExactMeshBoolean.BooleanStatus.SheetRefused) {
                    Rep.SheetReason = op.SheetReason;
                    Rep.CutStatus += " " + op.SheetReason;
                    return Fail(ExtensionRefusal.SheetRoleRefused, "the sheet role refuses the sheet: " + op.SheetReason + " (" + op.Error + ")", op.SheetWitnessPoint);
                }
                if (op.Status != ExactMeshBoolean.BooleanStatus.Success || op.Result == null)
                    return Fail(ExtensionRefusal.CutFailed, "the cut failed: " + op.Status + " " + op.Error);
                Rep.Cut = op.Result;
                double vw = Volume(W), vk = Volume(op.Result);
                Rep.SmallerPart = Math.Min(vk, vw - vk);
                if (Rep.SmallerPart > 0) {
                    Rep.URelative = Rep.U / Rep.SmallerPart;
                    foreach (RunReport r in runs)
                        r.URelative = r.U / Rep.SmallerPart;
                    if (Rep.URelative > O.MaxUOverSmallerPart)
                        return Fail(ExtensionRefusal.UncertaintyAboveLimit, "U is " + Rep.URelative.ToString("0.####") + " of the smaller part");
                }
                return true;
            }
        }
    }
}
