using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The relation checker of the Boolean fuzzer (docs/research/EXACT_BOOLEAN_FUZZ_2026-11.md). One instance checks one
    /// FuzzCase; Failures lists what does not hold ("relation: detail"), empty if all relations hold.
    ///
    /// The exact relations run ExactMeshBoolean with ComputeExactVertices and without SafeRounding and compare
    /// SurfaceFingerprints of the exact results: equalities of rational numbers, no tolerance, valid for any input
    /// coordinates. The default mode (SafeRounding) is checked against its documented contract: closed and manifold, clean
    /// where it says so, the volume within the rounding bound, power-of-two scales bit for bit, a second Compute() the same.
    /// Differential checks use code that shares nothing with the classification: BooleanCertificate, solid-angle winding
    /// numbers at sample points, and SolidBooleanPipeline as a consumer.
    /// </summary>
    public sealed class BooleanFuzzCheck
    {
        public static readonly Ops[] AllOps = { Ops.Union, Ops.Intersection, Ops.Difference };

        /// <summary> which groups of relations run (all by default; the tests of the checker switch groups off) </summary>
        [Flags]
        public enum Groups
        {
            None = 0, Algebra = 1, Representation = 2, Complement = 4, DefaultMode = 8, Similarities = 16, Differential = 32,
            All = Algebra | Representation | Complement | DefaultMode | Similarities | Differential
        }

        public readonly FuzzCase Case;
        public readonly List<string> Failures = new List<string>();
        public Groups Run = Groups.All;
        /// <summary> Boolean runs and relations checked, and how often a documented exception applied </summary>
        public int Booleans, Relations, ReportedNotClean, ComplementInexact, CertificateUncovered, CertificateRemovedParts, PipelineRefused, SamplesUsed, ThinInsideOut;
        /// <summary> of ReportedNotClean, by what MeshRoundingRepair reports (a result can count in several) </summary>
        public int NotCleanIntersections, NotCleanDegenerate, NotCleanFalseContacts, NotCleanScaleDisparity;
        /// <summary> for the tests of the checker: replaces the result of a run (name of the run, the Boolean) before it is checked </summary>
        public Action<string, ExactMeshBoolean> Tamper;
        /// <summary> the same for the pipeline's result ("pipeline Union", ...) </summary>
        public Action<string, SolidBooleanPipeline> TamperPipeline;

        /// <summary> how BooleanCertificate.Check() starts when all its checks hold but the rounding removed components </summary>
        public const string RemovedPartsVerdict = "not clean: the rounding removed ";

        readonly Random rand;

        public BooleanFuzzCheck(FuzzCase c)
        {
            Case = c;
            rand = new Random(c.Seed * 31 + 5);
        }

        void Fail(string relation, string detail) => Failures.Add(relation + ": " + detail);

        void Expect(string relation, string reason)
        {
            ++Relations;
            if (reason != "") Fail(relation, reason);
        }

        // ------------------------------------------------------------------ runs

        public sealed class ExactRun
        {
            public ExactMeshBoolean Boolean;
            public DMesh3 Result;
            public SurfaceFingerprint Print;
            /// <summary> every exact vertex is a double, and the result has it: the result mesh is the exact result </summary>
            public bool Representable;
        }

        static ExactMeshBoolean Make(IList<DMesh3> meshes, Ops op, Func<int[], bool> predicate)
        {
            List<DMesh3> copies = meshes.Select(m => new DMesh3(m)).ToList();
            if (predicate != null) return new ExactMeshBoolean(copies, predicate);
            return copies.Count == 2 ? new ExactMeshBoolean(copies[0], copies[1], op) : new ExactMeshBoolean(copies, op);
        }

        /// <summary> an exact run (no SafeRounding, exact vertices); null with a failure if it does not succeed or its result is not a closed surface </summary>
        public ExactRun Exact(string name, IList<DMesh3> meshes, Ops op, Func<int[], bool> predicate = null, Action<ExactMeshBoolean> configure = null)
        {
            ExactMeshBoolean b = Make(meshes, op, predicate);
            b.SafeRounding = false; b.ComputeExactVertices = true;
            configure?.Invoke(b);
            if (!Compute(name, b)) return null;
            Tamper?.Invoke(name, b);
            if (b.ResultExactVertices == null || b.ResultExactVertices.Length < b.Result.MaxVertexID) { Fail(name, "no ResultExactVertices for every vertex"); return null; }
            // closed inputs have exact winding numbers: an input taken as open would switch the classification to the generalized path
            ++Relations;
            if (b.ExactWinding == null || Array.IndexOf(b.ExactWinding, false) >= 0) { Fail(name, "a closed input is not reported with exact winding numbers"); return null; }
            ++Relations;
            string closed = ClosedOriented(b.Result);
            if (closed != "") { Fail(name + " exact result", closed); return null; }
            var run = new ExactRun { Boolean = b, Result = b.Result, Representable = true };
            ++Relations;
            // (the unit of the largest coordinate: a vertex that splits an edge where solids touch is the midpoint of the rounded
            // ends, which is documented to differ from its rounded exact point by units of the ends, not of the midpoint)
            double unit = 0;
            foreach (Vector3d p in b.Result.Vertices()) unit = Math.Max(unit, UlpOf(p.MaxAbs));
            foreach (int v in b.Result.VertexIndices()) {
                Vector3d p = b.Result.GetVertex(v), q = b.ResultExactVertices[v].ToVector3d();
                Rat[] e = SurfaceFingerprint.Exact(b.ResultExactVertices[v]), d = SurfaceFingerprint.Exact(p);
                if (!(e[0] == d[0] && e[1] == d[1] && e[2] == d[2])) run.Representable = false;
                // documented: the coordinates are the exact ones rounded (the conversion is within one unit in the last place)
                for (int k = 0; k < 3; ++k)
                    if (Math.Abs(p[k] - q[k]) > 2 * unit) { Fail(name + " exact vertices", $"vertex {v} is {p}, its exact point rounds to {q}"); return null; }
            }
            run.Print = SurfaceFingerprint.Of(b.Result, b.ResultExactVertices);
            return run;
        }

        bool Compute(string name, ExactMeshBoolean b)
        {
            ++Booleans;
            bool ok;
            try { ok = b.Compute(); }
            catch (Exception e) { Fail(name, "Compute() threw " + e.GetType().Name + ": " + e.Message); return false; }
            if (!ok || b.Status != ExactMeshBoolean.BooleanStatus.Success || b.Result == null) {
                Fail(name, $"Compute() {ok}, status {b.Status}, error {b.Error}");
                return false;
            }
            return true;
        }

        static double UlpOf(double x)
        {
            x = Math.Abs(x);
            if (x == 0) return double.Epsilon;
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;
        }

        // ------------------------------------------------------------------ independent checks of one mesh

        /// <summary> "" if every directed edge (by vertex id) occurs once and its reverse once: a closed, oriented, edge-manifold surface </summary>
        public static string ClosedOriented(DMesh3 mesh)
        {
            var edges = new HashSet<long>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i f = mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    int a = f[k], b = f[(k + 1) % 3];
                    if (a == b) return $"triangle {t} repeats vertex {a}";
                    if (!edges.Add(((long)a << 32) | (uint)b)) return $"the directed edge {a}-{b} occurs twice";
                }
            }
            foreach (long e in edges)
                if (!edges.Contains(((e & 0xFFFFFFFFL) << 32) | (uint)(e >> 32))) return $"the edge {e >> 32}-{e & 0xFFFFFFFFL} has no opposite triangle";
            foreach (int v in mesh.VertexIndices())
                if (!mesh.GetVertex(v).IsFinite) return $"vertex {v} is not finite";
            return "";
        }

        /// <summary> "" if the meshes have the same vertices (those of y are factor times those of x, bit for bit) and triangles, id by id </summary>
        public static string SameBits(DMesh3 x, DMesh3 y, double factor = 1)
        {
            if (x.VertexCount != y.VertexCount || x.TriangleCount != y.TriangleCount || x.MaxVertexID != y.MaxVertexID || x.MaxTriangleID != y.MaxTriangleID)
                return $"{x.VertexCount} vertices and {x.TriangleCount} triangles vs {y.VertexCount} and {y.TriangleCount}";
            foreach (int v in x.VertexIndices()) {
                if (!y.IsVertex(v)) return $"vertex {v} is missing";
                Vector3d p = x.GetVertex(v) * factor, q = y.GetVertex(v);
                if (p.x != q.x || p.y != q.y || p.z != q.z) return FormattableString.Invariant($"vertex {v}: ({p.x:R}, {p.y:R}, {p.z:R}) vs ({q.x:R}, {q.y:R}, {q.z:R})");
            }
            foreach (int t in x.TriangleIndices())
                if (!y.IsTriangle(t) || x.GetTriangle(t) != y.GetTriangle(t)) return $"triangle {t} differs";
            return "";
        }

        /// <summary> a default-mode result with RoundingClean false: documented (Status Success with the defects counted), so counted here </summary>
        void CountNotClean(ExactMeshBoolean x)
        {
            ++ReportedNotClean;
            if (x.Rounding == null) return;
            if (x.Rounding.RemainingIntersections > 0) ++NotCleanIntersections;
            if (x.Rounding.RemainingDegenerate > 0) ++NotCleanDegenerate;
            if (x.Rounding.RemainingFalseContacts > 0) ++NotCleanFalseContacts;
            if (x.Rounding.ScaleDisparity > 0) ++NotCleanScaleDisparity;
        }

        /// <summary>
        /// The check of a result with RoundingClean: BooleanOracle.CleanReason (valid, closed, manifold, oriented, no
        /// self-intersections other than contacts; slivers of the inputs allowed), with the one documented exception
        /// (MeshRoundingRepair.remove_tiny_components, #1067): a closed component that the rounding turned inside out and
        /// that is thin by the rounding's bound (2 |V| / area below it) is kept and reported by NotCleanReason, not by
        /// RoundingClean. Such components are counted (ThinInsideOut) and the rest is checked without them.
        /// </summary>
        string CleanWhereClaimed(DMesh3 r, ExactMeshBoolean x)
        {
            string reason = CleanExceptThinInsideOut(r, x.RoundingMaxDisplacement, x.RoundingUlpFactor, DegenerateAllowed, out int thin);
            ThinInsideOut += thin;
            return reason;
        }

        /// <summary>
        /// slivers in a result are allowed for the families whose inputs are made to give them (offsets of units in the last
        /// place, thin slabs, and their rotated copies); every other family must give a result without degenerate triangles
        /// </summary>
        bool DegenerateAllowed => Case.Family == "ulp" || Case.Family == "slab" || Case.Family == "rotated";

        /// <summary> the bound of a default-mode run on how far the rounding moves the surface: the one it applied, at least the relative one </summary>
        static double RoundingBound(ExactMeshBoolean x, double diag) => Math.Max(x.RoundingMaxDisplacement * diag, x.Rounding != null ? x.Rounding.MaxLength : 0);

        /// <summary> CleanWhereClaimed as a function: the reason, and the thin inside-out components it let through </summary>
        public static string CleanExceptThinInsideOut(DMesh3 r, double maxDisplacement, double ulpFactor, bool allowDegenerate, out int thin)
        {
            thin = 0;
            string reason = BooleanOracle.CleanReason(r, allowDegenerate);
            if (reason == "" || !reason.Contains("inside-out")) return reason;
            double bound = Math.Max(maxDisplacement * Bounds(r).DiagonalLength, ulpFactor * UlpOf(Math.Max(Bounds(r).Min.MaxAbs, Bounds(r).Max.MaxAbs)));
            var rest = new DMesh3(r);
            var components = new MeshConnectedComponents(r);
            components.FindConnectedT();
            foreach (MeshConnectedComponents.Component c in components) {
                DMesh3 sub = new DSubmesh3(r, c.Indices).SubMesh;
                double volume = SurfaceFingerprint.Of(sub).Volume();
                if (volume >= 0) continue;
                if (2 * Math.Abs(volume) >= bound * Area(sub))
                    return FormattableString.Invariant($"an inside-out component of volume {volume:R} and area {Area(sub):R} that is not thin by the bound {bound:R}");
                ++thin;
                foreach (int t in c.Indices) rest.RemoveTriangle(t, true, false);
            }
            if (thin == 0) return reason;
            return BooleanOracle.CleanReason(new DMesh3(rest, true), allowDegenerate);
        }

        static double Area(DMesh3 m) => m.TriangleIndices().Sum(t => m.GetTriArea(t));

        static AxisAlignedBox3d Bounds(params DMesh3[] meshes)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (DMesh3 m in meshes) foreach (Vector3d v in m.Vertices()) box.Contain(v);
            return box;
        }

        // ------------------------------------------------------------------ the closed case

        /// <summary> all relations of a case with closed inputs; returns Failures </summary>
        public List<string> CheckClosed()
        {
            DMesh3 a = Case.A, b = Case.B;
            var ab = new[] { a, b };
            var exact = new Dictionary<Ops, ExactRun>();
            foreach (Ops op in AllOps) exact[op] = Exact("A " + op + " B", ab, op);
            if (exact.Values.Any(x => x == null)) return Failures;
            SurfaceFingerprint u = exact[Ops.Union].Print, i = exact[Ops.Intersection].Print, d = exact[Ops.Difference].Print;

            if ((Run & Groups.Algebra) != 0) {
                ExactRun aa = Exact("A Union A", new[] { a, a }, Ops.Union), bb = Exact("B Union B", new[] { b, b }, Ops.Union);
                ExactRun ba = Exact("B Union A", new[] { b, a }, Ops.Union), bia = Exact("B Intersection A", new[] { b, a }, Ops.Intersection);
                ExactRun e = Exact("B Difference A", new[] { b, a }, Ops.Difference);
                ExactRun ana = Exact("A Intersection A", new[] { a, a }, Ops.Intersection), ama = Exact("A Difference A", new[] { a, a }, Ops.Difference);
                ExactRun bnb = Exact("B Intersection B", new[] { b, b }, Ops.Intersection);
                if (aa == null || bb == null || ba == null || bia == null || e == null || ana == null || ama == null || bnb == null) return Failures;
                SurfaceFingerprint sa = aa.Print, sb = bb.Print;
                Expect("A u B = B u A", SurfaceFingerprint.Difference(u, ba.Print));
                Expect("A n B = B n A", SurfaceFingerprint.Difference(i, bia.Print));
                Expect("A n A = A u A", SurfaceFingerprint.Difference(ana.Print, sa));
                Expect("B n B = B u B", SurfaceFingerprint.Difference(bnb.Print, sb));
                Expect("A - A is empty", ama.Result.TriangleCount == 0 ? "" : ama.Result.TriangleCount + " triangles, volume " + ama.Print.Volume());
                if (Case.SimpleA) Expect("A u A = A", SurfaceFingerprint.Difference(sa, SurfaceFingerprint.Of(a)));
                if (Case.SimpleB) Expect("B u B = B", SurfaceFingerprint.Difference(sb, SurfaceFingerprint.Of(b)));
                Expect("boundary(A u B) + boundary(A n B) = boundary(A) + boundary(B)", SurfaceFingerprint.Difference(u.Plus(i), sa.Plus(sb)));
                Expect("boundary(A - B) = boundary(A) - boundary(A n B)", SurfaceFingerprint.Difference(d, sa.Minus(i)));
                Expect("boundary(B - A) = boundary(B) - boundary(A n B)", SurfaceFingerprint.Difference(e.Print, sb.Minus(i)));
                Rat vu = u.SixVolume(), vi = i.SixVolume(), va = sa.SixVolume(), vb = sb.SixVolume();
                Expect("volumes are ordered", vi.Sign >= 0 && (va - vi).Sign >= 0 && (vb - vi).Sign >= 0 && (vu - va).Sign >= 0 && (vu - vb).Sign >= 0 ? ""
                    : $"vol(A n B) {vi.ToDouble() / 6}, vol A {va.ToDouble() / 6}, vol B {vb.ToDouble() / 6}, vol(A u B) {vu.ToDouble() / 6}");
            }

            if ((Run & Groups.Representation) != 0)
                foreach (Ops op in AllOps) {
                    SurfaceFingerprint f = exact[op].Print;
                    int s1 = rand.Next(), s2 = rand.Next(), seed = rand.Next(), salt = rand.Next();
                    Same(op + " with vertices and triangles reordered", f, Exact(op + " reordered", new[] { Invariance.Permuted(a, s1), Invariance.Permuted(b, s2) }, op));
                    Same(op + " with Seed " + seed, f, Exact(op + " seed", ab, op, null, x => x.Seed = seed));
                    Same(op + " with another hash salt, one shard", f, Exact(op + " salt", ab, op, null, x => { x.ArrangementHashSalt = salt; x.ArrangementSingleShard = true; }));
                    Same(op + " without the coplanar pre-decision and the early-out", f,
                        Exact(op + " no shortcuts", ab, op, null, x => { x.ArrangementPreDecideCoplanar = false; x.CullOutsideA = false; }));
                    Same(op + " without SimplifyFlatRegions", f, Exact(op + " not simplified", ab, op, null, x => x.SimplifyFlatRegions = false));
                    Same(op + " as a winding predicate", f, Exact(op + " predicate", ab, op, w => BooleanOracle.Apply(op, w[0] > 0, w[1] > 0)));
                    // a repeated operand changes nothing: A u B u A, A n B n A, A - B - B
                    Same(op + " with a repeated operand", f, Exact(op + " repeated", op == Ops.Difference ? new[] { a, b, b } : new[] { a, b, a }, op));
                }

            if ((Run & Groups.Complement) != 0) CheckComplement(a, b, u, i, d);
            if ((Run & (Groups.DefaultMode | Groups.Differential)) != 0) CheckDefaultMode(a, b, exact);
            if ((Run & Groups.Similarities) != 0) CheckSimilarities(a, b, exact);
            return Failures;
        }

        void Same(string relation, SurfaceFingerprint expected, ExactRun run)
        {
            if (run != null) Expect(relation, SurfaceFingerprint.Difference(run.Print, expected));
        }

        /// <summary>
        /// Complements in a universe box U around both: A - B = A n (U - B), (U - A) n (U - B) = U - (A u B) and
        /// U - ((U - A) n (U - B)) = A u B. U - X has only vertices of U and X where X does not self-intersect, so the chain
        /// stays exact; where an intermediate result is not exactly representable in doubles the step is counted, not checked.
        /// </summary>
        void CheckComplement(DMesh3 a, DMesh3 b, SurfaceFingerprint u, SurfaceFingerprint i, SurfaceFingerprint d)
        {
            AxisAlignedBox3d box = Bounds(a, b);
            // margins that are not a multiple of the inputs' grid; any doubles are exact inputs
            Vector3d m = box.Diagonal * 0.37 + new Vector3d(box.MaxDim, box.MaxDim, box.MaxDim) * 0.11;
            DMesh3 universe = BooleanFuzzShapes.Box(box.Min - m, box.Max + 1.3 * m);
            SurfaceFingerprint fu = SurfaceFingerprint.Of(universe);
            Same("U as a third operand: U n (A u B)", u, Exact("universe predicate", new[] { universe, a, b }, Ops.Union, w => w[0] > 0 && (w[1] > 0 || w[2] > 0)));
            ExactRun ca = Exact("U - A", new[] { universe, a }, Ops.Difference), cb = Exact("U - B", new[] { universe, b }, Ops.Difference);
            if (ca == null || cb == null) return;
            if (cb.Representable) Same("A n (U - B) = A - B", d, Exact("A n (U - B)", new[] { a, cb.Result }, Ops.Intersection));
            // (one count per case whose chain stops at a result that doubles cannot hold)
            if (!ca.Representable || !cb.Representable) { ++ComplementInexact; return; }
            ExactRun both = Exact("(U - A) n (U - B)", new[] { ca.Result, cb.Result }, Ops.Intersection);
            if (both == null) return;
            Expect("(U - A) n (U - B) = U - (A u B)", SurfaceFingerprint.Difference(both.Print, fu.Minus(u)));
            if (!both.Representable) { ++ComplementInexact; return; }
            Same("U - ((U - A) n (U - B)) = A u B", u, Exact("U - ((U - A) n (U - B))", new[] { universe, both.Result }, Ops.Difference));
        }

        /// <summary> the default mode (SafeRounding) against its documented contract, and the differential checks on its results </summary>
        void CheckDefaultMode(DMesh3 a, DMesh3 b, Dictionary<Ops, ExactRun> exact)
        {
            AxisAlignedBox3d box = Bounds(a, b);
            double diag = box.DiagonalLength, areas = Area(a) + Area(b);
            foreach (Ops op in AllOps) {
                string n = "default " + op;
                ExactMeshBoolean x = Make(new[] { a, b }, op, null);
                if (!Compute(n, x)) continue;
                Tamper?.Invoke(n, x);
                DMesh3 r = x.Result;
                Expect(n + " is a closed surface", ClosedOriented(r));
                Expect(n + " is a valid, closed DMesh3", r.CheckValidity(true, FailMode.ReturnOnly) && (r.TriangleCount == 0 || r.IsClosed()) ? "" : "CheckValidity or IsClosed fails");
                if (x.RoundingClean) Expect(n + " is clean where RoundingClean says so", CleanWhereClaimed(r, x));
                else CountNotClean(x);
                if ((Run & Groups.DefaultMode) != 0) {
                    // SafeRounding moves the surface by at most 1e-5 of the diagonal (and removes components thinner than that)
                    double ve = exact[op].Print.Volume(), vr = BooleanOracle.Volume(r), tol = 4 * RoundingBound(x, diag) * areas + 1e-9 * diag * diag * diag;
                    Expect(n + " has the exact volume within the rounding bound", Math.Abs(ve - vr) <= tol ? "" : FormattableString.Invariant($"exact {ve:R}, rounded {vr:R}, bound {tol:R}"));
                    DMesh3 first = new DMesh3(r);
                    if (Compute(n + " again", x)) { Tamper?.Invoke(n + " again", x); Expect(n + ": a second Compute() gives the same mesh", SameBits(first, x.Result)); }
                    foreach (int k in new[] { -10, 10 }) {
                        double f = Math.Pow(2, k);
                        ExactMeshBoolean s = Make(new[] { BooleanFuzzShapes.Map(a, p => p * f), BooleanFuzzShapes.Map(b, p => p * f) }, op, null);
                        if (Compute(n + " scaled 2^" + k, s)) { Tamper?.Invoke(n + " scaled 2^" + k, s); Expect(n + " scaled by 2^" + k + " is the same bit for bit", SameBits(first, s.Result, f)); }
                    }
                    r = first;
                }
                if ((Run & Groups.Differential) != 0) {
                    Expect(n + " has the inputs' winding numbers", WindingSamples(r, a, b, op, box));
                    // the certificate's violations are failures; triangles it cannot cover within Delta after its splits are
                    // counted only, as its class comment says (slivers thinner than Delta, which the near-coincident families are full of)
                    string cert; int uncovered = 0;
                    try {
                        BooleanCertificate c = BooleanCertificate.Run(new DMesh3(a), new DMesh3(b), op, out _, y => { if (op == Ops.Intersection) y.CullOutsideA = false; });
                        ++Booleans;
                        cert = c.Check();
                        CertificateUncovered += uncovered = c.Uncovered;
                    } catch (Exception e) { cert = "the certificate threw " + e.GetType().Name + ": " + e.Message; }
                    // (closed inputs, Intersection without the early-out: the certificate always applies; "n/a" would hide the check)
                    // "not clean: the rounding removed N part(s) ... (all else holds)" is the certificate's report of the documented
                    // removal of tiny and thin components: counted in the families built to give them, a failure elsewhere
                    if (DegenerateAllowed && cert.StartsWith(BooleanFuzzCheck.RemovedPartsVerdict, StringComparison.Ordinal)) { ++CertificateRemovedParts; ++Relations; }
                    else Expect(n + " has a valid certificate", cert);
                    // outside the families built to give slivers, every triangle must be covered, as in BooleanCertificate.CheckBoolean
                    if (!DegenerateAllowed) Expect(n + " is covered by its certificate", uncovered == 0 ? "" : uncovered + " triangles not covered within Delta");
                    var pipeline = new SolidBooleanPipeline(new DMesh3(a), new DMesh3(b), op);
                    bool ok;
                    try { ok = pipeline.Compute(); ++Booleans; }
                    catch (Exception e) { Fail("pipeline " + op, "threw " + e.GetType().Name + ": " + e.Message); continue; }
                    if (!ok) {
                        // a documented refusal (thin, inside-out or open input) is not a failure; a failed Boolean is
                        // (the pipeline runs the Boolean with StrictRounding, within the relative bound only: RoundingNotClean is its
                        // documented answer, also where the default mode, with its ulp term, reports a clean result)
                        bool documented = PipelineRefusalIsDocumented(pipeline.Status, pipeline.BooleanStatus);
                        // (a refusal needs a reason in the inputs: outside the sliver families there is none)
                        if (documented && DegenerateAllowed) ++PipelineRefused;
                        else Fail("pipeline " + op, pipeline.Status + ", Boolean " + pipeline.BooleanStatus + ": " + pipeline.Error);
                        continue;
                    }
                    TamperPipeline?.Invoke("pipeline " + op, pipeline);
                    Expect("pipeline " + op + " is a closed surface", ClosedOriented(pipeline.Result));
                    double vp = BooleanOracle.Volume(pipeline.Result), vr2 = BooleanOracle.Volume(r), tol2 = 8 * RoundingBound(x, diag) * areas + 1e-9 * diag * diag * diag;
                    Expect("pipeline " + op + " has the volume of the Boolean", Math.Abs(vp - vr2) <= tol2 ? "" : FormattableString.Invariant($"pipeline {vp:R}, Boolean {vr2:R}, bound {tol2:R}"));
                }
            }
        }

        /// <summary>
        /// A SolidBooleanPipeline that does not succeed on closed inputs is within its contract if it refuses an input (thin,
        /// inside-out, inconsistent) or if its Boolean, run with StrictRounding, answers RoundingNotClean; any other failed
        /// Boolean (and InvalidInput or Cancelled, which these inputs cannot cause) is a failure.
        /// </summary>
        public static bool PipelineRefusalIsDocumented(SolidBooleanPipeline.PipelineStatus status, ExactMeshBoolean.BooleanStatus booleanStatus)
        {
            switch (status) {
                case SolidBooleanPipeline.PipelineStatus.ThinInput:
                case SolidBooleanPipeline.PipelineStatus.InsideOutInput:
                case SolidBooleanPipeline.PipelineStatus.InconsistentOrientation:
                    return true;
                case SolidBooleanPipeline.PipelineStatus.BooleanFailed:
                    return booleanStatus == ExactMeshBoolean.BooleanStatus.RoundingNotClean;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Solid-angle winding numbers of the result and of the inputs at random points of the bounds: where all three are
        /// within 0.02 of an integer (so the point is away from every surface), the result's is 1 where the operation of the
        /// inputs' (positive is inside) is true and 0 elsewhere.
        /// </summary>
        string WindingSamples(DMesh3 result, DMesh3 a, DMesh3 b, Ops op, AxisAlignedBox3d box)
        {
            for (int s = 0; s < 48; ++s) {
                var p = new Vector3d(box.Min.x + rand.NextDouble() * box.Width, box.Min.y + rand.NextDouble() * box.Height, box.Min.z + rand.NextDouble() * box.Depth);
                double wa = BooleanOracle.Winding(a, p), wb = BooleanOracle.Winding(b, p), wr = BooleanOracle.Winding(result, p);
                if (Math.Abs(wa - Math.Round(wa)) > 0.02 || Math.Abs(wb - Math.Round(wb)) > 0.02 || Math.Abs(wr - Math.Round(wr)) > 0.02) continue;
                ++SamplesUsed;
                int expected = BooleanOracle.Apply(op, Math.Round(wa) > 0, Math.Round(wb) > 0) ? 1 : 0;
                if ((int)Math.Round(wr) != expected)
                    return FormattableString.Invariant($"at ({p.x:R}, {p.y:R}, {p.z:R}) the result's winding number is {wr:G4}, the inputs' are {wa:G4} and {wb:G4}");
            }
            return "";
        }

        /// <summary>
        /// Similarity.All(): under a similarity that rounds (translation, rotation, a scale that is no power of two) the
        /// transformed inputs are other inputs, so the exact identity is checked on them again (inclusion-exclusion of the
        /// exact volumes), the default mode's result is a closed surface, and the volume agrees with the untransformed one
        /// within the resolution of the transformed coordinates. The power-of-two members are checked bit for bit in
        /// CheckDefaultMode.
        /// </summary>
        void CheckSimilarities(DMesh3 a, DMesh3 b, Dictionary<Ops, ExactRun> exact)
        {
            double size = Bounds(a, b).DiagonalLength, maxAbs = Math.Max(Bounds(a, b).Min.Abs.MaxAbs, Bounds(a, b).Max.Abs.MaxAbs);
            double areas = Area(a) + Area(b);
            foreach (Similarity s in Similarity.All()) {
                if (s == Similarity.Identity || s.IsExact) continue;
                DMesh3 ta = Invariance.Transformed(a, s), tb = Invariance.Transformed(b, s);
                var tab = new[] { ta, tb };
                ExactRun tu = Exact("A Union B, " + s, tab, Ops.Union), ti = Exact("A Intersection B, " + s, tab, Ops.Intersection);
                ExactRun taa = Exact("A Union A, " + s, new[] { ta, ta }, Ops.Union), tbb = Exact("B Union B, " + s, new[] { tb, tb }, Ops.Union);
                if (tu == null || ti == null || taa == null || tbb == null) continue;
                Expect("inclusion-exclusion, " + s, SurfaceFingerprint.Difference(tu.Print.Plus(ti.Print), taa.Print.Plus(tbb.Print)));
                // the transformed vertices are rounded: each moves by at most the resolution, the volume by that times the area
                double s3 = s.Scale * s.Scale * s.Scale;
                double resolution = (Math.Max(Math.Abs(s.Translation.x), Math.Max(Math.Abs(s.Translation.y), Math.Abs(s.Translation.z))) + s.Scale * (size + maxAbs)) * MathUtil.Epsilon / s.Scale;
                double tol = 64 * resolution * areas + 1e-13 * size * size * size;
                foreach (var (op, t) in new[] { (Ops.Union, tu), (Ops.Intersection, ti) }) {
                    double v = exact[op].Print.Volume(), tv = t.Print.Volume() / s3;
                    Expect(op + " volume, " + s, Math.Abs(v - tv) <= tol ? "" : FormattableString.Invariant($"{v:R} vs {tv:R} mapped back, bound {tol:R}"));
                }
                Ops dop = AllOps[rand.Next(3)];
                ExactMeshBoolean x = Make(tab, dop, null);
                if (Compute("default " + dop + ", " + s, x)) {
                    Tamper?.Invoke("default, " + s, x);
                    Expect("default " + dop + ", " + s + " is a closed surface", ClosedOriented(x.Result));
                    if (x.RoundingClean) Expect("default " + dop + ", " + s + " is clean where RoundingClean says so", CleanWhereClaimed(x.Result, x));
                    else CountNotClean(x);
                }
            }
        }

        // ------------------------------------------------------------------ the open case

        /// <summary>
        /// A case with an open input: the generalized-winding contract only. Every operation in both orders succeeds with a
        /// finite, valid mesh; the open input is reported (ExactWinding false), the closed one exact; a second Compute() and a
        /// power-of-two scale give the same mesh bit for bit, another Seed the same surface (the fingerprint of the doubles).
        /// </summary>
        public List<string> CheckOpen()
        {
            foreach (bool swap in new[] { false, true }) {
                DMesh3 a = swap ? Case.B : Case.A, b = swap ? Case.A : Case.B;
                foreach (Ops op in AllOps) {
                    string n = (swap ? "B " : "A ") + op + (swap ? " A" : " B");
                    ExactMeshBoolean x = Make(new[] { a, b }, op, null);
                    if (!Compute(n, x)) continue;
                    Tamper?.Invoke(n, x);
                    DMesh3 r = new DMesh3(x.Result);
                    // (a non-finite result ends the case here: the comparisons below need finite coordinates)
                    bool finite = r.Vertices().All(v => v.IsFinite);
                    Expect(n + " is finite and valid", finite && r.CheckValidity(true, FailMode.ReturnOnly) ? "" : "non-finite or invalid");
                    if (!finite) continue;
                    Expect(n + " reports the open input", x.ExactWinding != null && x.ExactWinding[swap ? 0 : 1] == false && x.ExactWinding[swap ? 1 : 0] ? ""
                        : "ExactWinding " + (x.ExactWinding == null ? "null" : string.Join(",", x.ExactWinding)));
                    if (Compute(n + " again", x)) { Tamper?.Invoke(n + " again", x); Expect(n + ": a second Compute() gives the same mesh", SameBits(r, x.Result)); }
                    ExactMeshBoolean s = Make(new[] { BooleanFuzzShapes.Map(a, p => p * 1024), BooleanFuzzShapes.Map(b, p => p * 1024) }, op, null);
                    if (Compute(n + " scaled", s)) Tamper?.Invoke(n + " scaled", s);
                    if (s.Result != null && s.Status == ExactMeshBoolean.BooleanStatus.Success) Expect(n + " scaled by 2^10 is the same bit for bit", SameBits(r, s.Result, 1024));
                    int seed = rand.Next();
                    ExactMeshBoolean other = Make(new[] { a, b }, op, null);
                    other.Seed = seed;
                    if (Compute(n + " seed", other)) Tamper?.Invoke(n + " seed", other);
                    if (other.Result != null && other.Status == ExactMeshBoolean.BooleanStatus.Success) Expect(n + " with Seed " + seed + " is the same surface", SurfaceFingerprint.Difference(SurfaceFingerprint.Of(other.Result), SurfaceFingerprint.Of(r)));
                }
            }
            return Failures;
        }

        public List<string> Check() => Case.Open ? CheckOpen() : CheckClosed();
    }
}
