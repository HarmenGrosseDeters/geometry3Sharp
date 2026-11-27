using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Property-based tests of SolidBooleanPipeline with CsCheck (net10.0 only, like ExactBooleanPropertyTests, whose
    /// deterministic shrinking it uses). The cases are faceted-CAD-like: prisms over convex polygons (centre and radius on a
    /// 1/64 grid), a second solid that overlaps the first, shares one of its side faces (with or without vertex noise of 1e-8,
    /// the noise of a faceting), or is a thin layer through it, an optional third prism, all rotated about z, optionally
    /// at UTM-like coordinates and scaled by 2^k. Each case is a printable description; the meshes are built per run.
    ///
    /// Campaigns as in ExactBooleanPropertyTests: case i of a campaign from seed (campaign, i), a failure shrunk and
    /// reported with a replay command. Opt-in:
    ///   G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~SolidBooleanPipelinePropertyTests
    ///   G3_PROPERTY_CAMPAIGN="Valid" G3_PROPERTY_REPLAY=&lt;seed&gt; dotnet test ... --filter "FullyQualifiedName~SolidBooleanPipelinePropertyTests.Valid"
    /// </summary>
    public class SolidBooleanPipelinePropertyTests
    {
        readonly ITestOutputHelper log;
        public SolidBooleanPipelinePropertyTests(ITestOutputHelper log) { this.log = log; }

        const int ShrinkIterations = 200;

        // ---------------------------------------------------------------- cases

        /// <summary> a prism over a convex polygon: centre and radius on a 1/64 grid, n corners from a start angle, z range </summary>
        public sealed class Prism
        {
            public int Cx, Cy, R, N, Start, Z0, H;
            public override string ToString() { return $"Prism(c=({Cx},{Cy})/64 r={R}/64 n={N} start={Start}deg z={Z0}/64+{H}/64)"; }

            public List<Vector3d> Corners()
            {
                var c = new List<Vector3d>(N);
                for (int i = 0; i < N; ++i) {
                    double a = (Start + 360.0 * i / N) * Math.PI / 180;
                    c.Add(new Vector3d((Cx + R * Math.Cos(a)) / 64, (Cy + R * Math.Sin(a)) / 64, 0));
                }
                return c;
            }
        }

        public enum Second { Overlapping, SharedFace, SharedFaceNoisy, ThinLayer, Pinch }

        public sealed class Case
        {
            public Prism A, B, C;           // B: the overlapping prism (Second.Overlapping); C: an optional third prism
            public Second Kind;
            public int Face, Depth;         // SharedFace*: the side of A (corner Face to Face + 1) and the depth of the neighbour in 1/64
            public int LayerE4;             // ThinLayer: thickness in units of 1e-4 (1 .. 100, so 1e-4 .. 1e-2)
            public int Rotation, ScaleK;
            public bool Utm;
            public uint NoiseSeed;
            public Ops Op;

            public override string ToString()
            {
                string second = Kind == Second.Overlapping ? "B=" + B : Kind == Second.ThinLayer ? $"layer {LayerE4}e-4 thick"
                    : Kind == Second.Pinch ? $"two through-holes of {Depth}/64 touching at an edge" : $"{Kind} side {Face} depth {Depth}/64";
                return $"Case({Op}: A={A}, {second}{(C != null ? ", C=" + C : "")}, rotation {Rotation}deg, 2^{ScaleK}{(Utm ? ", UTM" : "")}, noise seed {NoiseSeed})";
            }

            /// <summary> the inputs, in world coordinates </summary>
            public List<DMesh3> Build()
            {
                var rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, Rotation);
                double s = Math.Pow(2, ScaleK);
                Vector3d at = Utm ? new Vector3d(2527504.25, 5634208.5, 96.125) : Vector3d.Zero;
                Func<Vector3d, Vector3d> place = p => (rot * p) * s + at;
                var list = new List<DMesh3>();
                List<Vector3d> a = A.Corners();
                double az0 = A.Z0 / 64.0, az1 = (A.Z0 + A.H) / 64.0;
                list.Add(PrismMesh(a, az0, az1, place, null));
                switch (Kind) {
                    case Second.Overlapping:
                        list.Add(PrismMesh(B.Corners(), B.Z0 / 64.0, (B.Z0 + B.H) / 64.0, place, null));
                        break;
                    case Second.SharedFace:
                    case Second.SharedFaceNoisy: {
                        // a quadrilateral prism on the outer side of A's side face (Face, Face + 1), as tall as A
                        Vector3d p = a[Face % a.Count], q = a[(Face + 1) % a.Count];
                        Vector3d edge = q - p;
                        Vector3d outward = new Vector3d(edge.y, -edge.x, 0).Normalized * (Depth / 64.0);
                        var quad = new List<Vector3d> { q, p, p + outward, q + outward };
                        var rnd = Kind == Second.SharedFaceNoisy ? new Random((int)NoiseSeed) : null;
                        list.Add(PrismMesh(quad, az0, az1, place, rnd, 1e-8 * s));
                        break;
                    }
                    case Second.Pinch: {
                        // two square through-holes in A that touch each other along a vertical edge at A's centre: a
                        // difference leaves A touching itself there (the corner is the same expression for both, so it coincides)
                        double w = Math.Min(Depth, A.R / 2) / 64.0, cx = A.Cx / 64.0, cy = A.Cy / 64.0, lo = az0 - 1, hi = az1 + 1;
                        var q1 = new List<Vector3d> { new Vector3d(cx - w, cy - w, 0), new Vector3d(cx, cy - w, 0), new Vector3d(cx, cy, 0), new Vector3d(cx - w, cy, 0) };
                        var q2 = new List<Vector3d> { new Vector3d(cx, cy, 0), new Vector3d(cx + w, cy, 0), new Vector3d(cx + w, cy + w, 0), new Vector3d(cx, cy + w, 0) };
                        list.Add(PrismMesh(q1, lo, hi, place, null));
                        list.Add(PrismMesh(q2, lo, hi, place, null));
                        break;
                    }
                    case Second.ThinLayer: {
                        // a slab through the middle of A, wider than A
                        double t = LayerE4 * 1e-4, zc = (az0 + az1) / 2, w = A.R / 64.0 * 1.5;
                        var sq = new List<Vector3d> { new Vector3d(A.Cx / 64.0 - w, A.Cy / 64.0 - w, 0), new Vector3d(A.Cx / 64.0 + w, A.Cy / 64.0 - w, 0),
                            new Vector3d(A.Cx / 64.0 + w, A.Cy / 64.0 + w, 0), new Vector3d(A.Cx / 64.0 - w, A.Cy / 64.0 + w, 0) };
                        list.Add(PrismMesh(sq, zc, zc + t, place, null));
                        break;
                    }
                }
                if (C != null)
                    list.Add(PrismMesh(C.Corners(), C.Z0 / 64.0, (C.Z0 + C.H) / 64.0, place, null));
                return list;
            }
        }

        // a closed prism over the counter-clockwise polygon, from z0 to z1, placed; rnd: noise up to `noise` on every vertex
        // (1e-8 of the case's unit: far below the grid of InputGrid, 2^-20 of the unit, and above one ulp at UTM coordinates)
        static DMesh3 PrismMesh(List<Vector3d> poly, double z0, double z1, Func<Vector3d, Vector3d> place, Random rnd, double noise = 0)
        {
            var m = new DMesh3();
            int n = poly.Count;
            Func<Vector3d, Vector3d> v = p => {
                Vector3d w = place(p);
                if (rnd != null) {
                    w += new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * (2 * noise);
                }
                return w;
            };
            var bottom = poly.Select(p => m.AppendVertex(v(new Vector3d(p.x, p.y, z0)))).ToList();
            var top = poly.Select(p => m.AppendVertex(v(new Vector3d(p.x, p.y, z1)))).ToList();
            for (int i = 1; i + 1 < n; ++i) {
                m.AppendTriangle(bottom[0], bottom[i + 1], bottom[i]);
                m.AppendTriangle(top[0], top[i], top[i + 1]);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(bottom[i], bottom[j], top[j]);
                m.AppendTriangle(bottom[i], top[j], top[i]);
            }
            return m;
        }

        // ---------------------------------------------------------------- generators

        static readonly Gen<Prism> GenPrism =
            Gen.Select(Gen.Int[-128, 128], Gen.Int[-128, 128], Gen.Int[32, 256], Gen.Int[3, 6], Gen.Int[0, 119], Gen.Int[-64, 64], Gen.Int[16, 256],
                (cx, cy, r, n, start, z0, h) => new Prism { Cx = cx, Cy = cy, R = r, N = n, Start = start, Z0 = z0, H = h });

        // (the kinds are drawn from 0..maxKind: GenCase keeps its draws, so the fixed seeds of the campaigns that use it replay as before;
        // the Pinch kind, the last one, is only in GenCaseWithPinch)
        static Gen<Case> GenCaseOf(int maxKind) =>
            Gen.Select(GenPrism, GenPrism, Gen.Int[0, maxKind], Gen.Int[0, 5], Gen.Int[8, 128], Gen.Int[1, 100], Gen.Int[0, 359])
                .SelectMany(t => Gen.Select(Gen.Int[-3, 3], Gen.Bool, Gen.UInt, Gen.Int[0, 2], Gen.Bool, GenPrism, (k, utm, seed, op, third, c) => new Case {
                    A = t.Item1, B = t.Item2, Kind = (Second)t.Item3, Face = t.Item4, Depth = t.Item5, LayerE4 = t.Item6, Rotation = t.Item7,
                    ScaleK = k, Utm = utm, NoiseSeed = seed, Op = (Ops)op, C = third ? c : null
                }));

        static readonly Gen<Case> GenCase = GenCaseOf(3);
        static readonly Gen<Case> GenCaseWithPinch = GenCaseOf(4);

        // two inputs only (for the inclusion-exclusion of the volumes)
        static readonly Gen<Case> GenPair = GenCase.Select(c => { c.C = null; return c; });

        // ---------------------------------------------------------------- campaign

        static int Samples(int normal)
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : normal;
        }

        void Campaign<T>(string name, Gen<T> gen, Func<T, string> property, int samples, uint campaign)
        {
            void Check(T t) { string r = property(t); if (r != "") throw new Exception(r); }
            var watch = Stopwatch.StartNew();
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            string[] seeds = string.IsNullOrEmpty(replay)
                ? Enumerable.Range(0, Samples(samples)).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray()
                : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            foreach (string seed in seeds) {
                string failure = null;
                T original = default(T);
                try { gen.Sample(t => { original = t; Check(t); }, seed: seed, iter: 1, threads: 1, print: t => t.ToString()); }
                catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                string shrunk = ExactBooleanPropertyTests.Shrink(gen, property, seed, ShrinkIterations).Report;
                throw new Xunit.Sdk.XunitException($"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Shrunk ({ShrinkIterations} tries, not necessarily minimal): {shrunk}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY=<seed> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~SolidBooleanPipelinePropertyTests.{name}\"");
            }
            log.WriteLine($"{name}: passed in {watch.ElapsedMilliseconds} ms");
        }

        // ---------------------------------------------------------------- checks

        static double Volume(DMesh3 m)
        {
            if (m.TriangleCount == 0)
                return 0;
            Vector3d o = m.GetBounds().Center;
            double v6 = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                v6 += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o));
            }
            return v6 / 6;
        }

        static double Area(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).y; }

        // a fingerprint of a mesh: every vertex and triangle, bit for bit
        static string Fingerprint(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(m.GetTriangle(t).ToString()).Append(';');
            return sb.ToString();
        }

        /// <summary> the reason the run violates the pipeline's contract, or "" </summary>
        static string Contract(SolidBooleanPipeline p, List<DMesh3> inputs, List<string> before)
        {
            for (int i = 0; i < inputs.Count; ++i)
                if (Fingerprint(inputs[i]) != before[i])
                    return "input " + i + " was modified";
            if (p.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed && p.BooleanStatus == ExactMeshBoolean.BooleanStatus.RoundingNotClean)
                return p.Result == null ? "" : "a failed run has a result";     // (allowed: the strict rounding may refuse)
            if (p.Status != SolidBooleanPipeline.PipelineStatus.Success)
                return "status " + p.Status + " / " + p.BooleanStatus + ": " + p.Error;
            DMesh3 r = p.Result;
            if (r == null)
                return "Success without a result";
            if (r.TriangleCount != p.Report.ResultTriangles)
                return "ResultTriangles " + p.Report.ResultTriangles + " but the result has " + r.TriangleCount;
            if (r.TriangleCount == 0)
                return p.Report.NotCleanReason == "empty" ? "" : "an empty result reports '" + p.Report.NotCleanReason + "'";
            if (!r.IsCompact)
                return "the result is not compact";
            if (!r.Vertices().All(v => v.IsFinite))
                return "a coordinate is not finite";
            if (!r.IsClosed())
                return "the result is not closed";
            if (!r.CheckValidity(false, FailMode.ReturnOnly))
                return "the result is not a valid mesh";
            string reason = p.Report.NotCleanReason;
            if (reason != "" && !(reason == "self-intersections" && p.Report.ContactsResult > 0))
                return "the result is not clean: " + reason + " (contacts " + p.Report.ContactsResult + ")";
            if (Volume(r) <= 0)
                return "the result has volume " + Volume(r).ToString("R");
            return "";
        }

        static SolidBooleanPipeline Run(List<DMesh3> inputs, Ops op)
        {
            var p = new SolidBooleanPipeline(inputs, op);
            p.Compute();
            return p;
        }

        // ---------------------------------------------------------------- properties

        [Fact]
        public void Valid()
        {
            // every run either refuses with RoundingNotClean or returns a closed, valid, clean (or touching) solid with
            // positive volume, and leaves its inputs as they are
            var seen = new Dictionary<string, int>();
            Campaign("Valid", GenCase, c => {
                List<DMesh3> inputs = c.Build();
                var before = inputs.Select(Fingerprint).ToList();
                var p = Run(inputs, c.Op);
                string outcome = p.Status != SolidBooleanPipeline.PipelineStatus.Success ? p.BooleanStatus.ToString()
                    : p.Result.TriangleCount == 0 ? "empty" : p.Report.ContactsResult > 0 ? "contacts" : "solid";
                string key = c.Kind + (c.Utm ? " UTM" : "") + " -> " + outcome;
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                return Contract(p, inputs, before);
            }, 40, 9101);
            // (what the campaign covered: the kinds of case and their outcomes)
            foreach (var kv in seen.OrderBy(k => k.Key))
                log.WriteLine("  " + kv.Value + "  " + kv.Key);
            // every kind of case reaches a solid result at least once (the campaign is not vacuous for any of them)
            if (Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") == null)
                foreach (Second kind in Enum.GetValues(typeof(Second)).Cast<Second>().Where(k => k != Second.Pinch))       // (Pinch: only in GenCaseWithPinch)
                    Assert.True(seen.Any(kv => kv.Key.StartsWith(kind + " ") && (kv.Key.EndsWith("-> solid") || kv.Key.EndsWith("-> contacts"))),
                        kind + " never gave a solid: " + string.Join(", ", seen.Keys));
        }

        [Fact]
        public void WithInputGrid_NoRoundingFailure()
        {
            // InputGrid 2^-20 (scaled with the case): the noise of nearly coincident faces is snapped away, so the strict
            // rounding never refuses, and the result keeps the contract of Valid
            Campaign("WithInputGrid", GenCase, c => {
                List<DMesh3> inputs = c.Build();
                var before = inputs.Select(Fingerprint).ToList();
                var p = new SolidBooleanPipeline(inputs, c.Op) { InputGrid = Math.Pow(2, -20 + c.ScaleK) };
                p.Compute();
                if (p.Status != SolidBooleanPipeline.PipelineStatus.Success)
                    return "status " + p.Status + " / " + p.BooleanStatus + " with InputGrid: " + p.Error;
                return Contract(p, inputs, before);
            }, 40, 9105);
        }

        [Fact]
        public void Volumes_AddUpAcrossTheOperations()
        {
            // V(A u B) + V(A n B) = V(A) + V(B), V(A - B) = V(A) - V(A n B), and for a thin layer through A, V(A n B) = A's polygon
            // times the thickness: within the rounding of the results (the displacements the rounding made, times the area)
            Campaign("Volumes", GenPair, c => {
                List<DMesh3> inputs = c.Build();
                double drift = 0;
                Func<Ops, SolidBooleanPipeline> run = op => {
                    ExactMeshBoolean b = null;
                    var p = new SolidBooleanPipeline(inputs, op) { ConfigureBoolean = x => b = x };
                    p.Compute();
                    if (b != null && b.Rounding != null)
                        drift += b.Rounding.MaxDrift;
                    return p;
                };
                var u = run(Ops.Union);
                var i = run(Ops.Intersection);
                var d = run(Ops.Difference);
                if (new[] { u, i, d }.Any(p => p.Status != SolidBooleanPipeline.PipelineStatus.Success))
                    return "";      // (Valid judges the refusals)
                double va = Volume(inputs[0]), vb = Volume(inputs[1]);
                double vu = Volume(u.Result), vi = Volume(i.Result), vd = Volume(d.Result);
                var box = inputs[0].GetBounds();
                box.Contain(inputs[1].GetBounds());
                // (the displacements the rounding made in the three runs, and the rounding of new vertices to the double grid:
                // a few units in the last place of the largest coordinate; times the area that can move)
                double ulp = Math.Max(Math.Abs(box.Min.MaxAbs), Math.Abs(box.Max.MaxAbs)) * 2.3e-16;
                double area = Area(inputs[0]) + Area(inputs[1]);
                double tol = 2 * (drift + 8 * ulp) * area + 1e-12 * (Math.Abs(va) + Math.Abs(vb));
                if (Math.Abs(vu + vi - va - vb) > tol)
                    return $"V(u) + V(n) = {vu + vi:R}, V(A) + V(B) = {va + vb:R} (tolerance {tol:G3})";
                if (Math.Abs(vd - (va - vi)) > tol)
                    return $"V(A - B) = {vd:R}, V(A) - V(A n B) = {va - vi:R} (tolerance {tol:G3})";
                if (c.Kind == Second.ThinLayer) {
                    // the slab crosses A completely: A n B is A's polygon times the thickness
                    var poly = c.A.Corners();
                    double pa = 0;
                    for (int k = 0; k < poly.Count; ++k)
                        pa += poly[k].x * poly[(k + 1) % poly.Count].y - poly[(k + 1) % poly.Count].x * poly[k].y;
                    double s3 = Math.Pow(2, 3 * c.ScaleK), want = pa / 2 * c.LayerE4 * 1e-4 * s3;
                    if (Math.Abs(vi - want) > tol + 1e-6 * want)
                        return $"V(A n layer) = {vi:R}, the polygon times the thickness = {want:R} (tolerance {tol:G3})";
                }
                return "";
            }, 25, 9102);
        }

        [Fact]
        public void Repeated_AndReused_AreBitIdentical()
        {
            Campaign("Repeated", GenCase, c => {
                List<DMesh3> inputs = c.Build();
                var p = new SolidBooleanPipeline(inputs, c.Op);
                bool ok1 = p.Compute();
                string first = ok1 ? Fingerprint(p.Result) : p.Status + "/" + p.BooleanStatus;
                bool ok2 = p.Compute();
                string second = ok2 ? Fingerprint(p.Result) : p.Status + "/" + p.BooleanStatus;
                var q = Run(c.Build(), c.Op);
                string third = q.Status == SolidBooleanPipeline.PipelineStatus.Success ? Fingerprint(q.Result) : q.Status + "/" + q.BooleanStatus;
                if (first != second)
                    return "Compute() twice on one instance gives different results";
                if (first != third)
                    return "a second instance gives a different result";
                return "";
            }, 25, 9103);
        }

        [Fact]
        public void SeparateSelfContacts_KeepsTheContract_AndTheVolume()
        {
            // with InputGrid (which makes nearly touching faces touch) and SeparateSelfContacts 2^-20 of the case's unit: the
            // contract of Valid holds, the counts of the report agree with the results (the self-contacts only go down), the
            // moves stay within the distance, the triangle count stays, the volume moves at most the distance times the area,
            // no triangle crosses that did not before, and the cleanliness is no worse (MeshRobustRepair, independent)
            int withSelfContacts = 0, separated = 0;
            Campaign("SeparateSelfContacts", GenCaseWithPinch, c => {
                List<DMesh3> inputs = c.Build();
                var before = inputs.Select(Fingerprint).ToList();
                double grid = Math.Pow(2, -20 + c.ScaleK);
                var plain = new SolidBooleanPipeline(inputs, c.Op) { InputGrid = grid };
                plain.Compute();
                var p = new SolidBooleanPipeline(inputs, c.Op) { InputGrid = grid, SeparateSelfContacts = grid };
                p.Compute();
                if (p.Status != plain.Status)
                    return "status " + p.Status + " with the option, " + plain.Status + " without";
                string contract = Contract(p, inputs, before);
                if (contract != "")
                    return contract;
                if (p.Status != SolidBooleanPipeline.PipelineStatus.Success || p.Result.TriangleCount == 0)
                    return "";
                int found = SolidBooleanPipeline.SelfContacts(plain.Result).Count;
                if (found > 0) withSelfContacts++;
                if (found > 0 && p.Report.SelfContactsLeft == 0) separated++;
                if (found == 0) {
                    // nothing to separate: the result and the report are the plain ones, bit for bit
                    string same = Invariance.Difference(plain.Result, p.Result);
                    if (same != "" || p.Report.NotCleanReason != plain.Report.NotCleanReason || p.Report.ResultKind != plain.Report.ResultKind)
                        return "no self-contact, but the result differs from the plain run: " + same;
                }
                if (p.Report.SelfContactsFound != found)
                    return $"SelfContactsFound {p.Report.SelfContactsFound}, the result without the option has {found}";
                if (p.Report.SelfContactsLeft > found || p.Report.SelfContactsLeft != SolidBooleanPipeline.SelfContacts(p.Result).Count)
                    return $"SelfContactsLeft {p.Report.SelfContactsLeft} of {found}";
                // (up to the distance plus the rounding of the new coordinates: a few units in the last place of the largest one)
                var bounds = p.Result.GetBounds();
                double ulp = Math.Max(bounds.Min.Abs.MaxAbs, bounds.Max.Abs.MaxAbs) * 2.220446049250313e-16;
                if (p.Report.SelfContactMaxMove > grid * (1 + 1e-12) + 4 * ulp)
                    return $"a move of {p.Report.SelfContactMaxMove:R} beyond the distance {grid:R} (and 4 ulps, {4 * ulp:G3})";
                if (p.Result.TriangleCount != plain.Result.TriangleCount)
                    return "the separation changed the triangle count";
                double tol = grid * Area(plain.Result) + 1e-12 * Math.Abs(Volume(plain.Result));
                if (Math.Abs(Volume(p.Result) - Volume(plain.Result)) > tol)
                    return $"volume {Volume(p.Result):R} against {Volume(plain.Result):R} (tolerance {tol:G3})";
                // no new crossing: a move that would cross is put back (vertices at one position count as one; the moves keep
                // the triangle ids, so the sets compare triangle by triangle)
                var crossingBefore = SolidBooleanPipeline.CrossingTriangles(plain.Result, plain.Result.VertexIndices());
                var crossingAfter = SolidBooleanPipeline.CrossingTriangles(p.Result, p.Result.VertexIndices());
                if (!crossingAfter.IsSubsetOf(crossingBefore))
                    return $"{crossingAfter.Count(t => !crossingBefore.Contains(t))} triangles cross after the separation that did not before";
                // an oracle independent of the code under test: the report says what MeshRobustRepair.NotCleanReason says of
                // the result, and a clean result stays clean
                string reasonAfter = MeshRobustRepair.NotCleanReason(p.Result);
                if (reasonAfter != p.Report.NotCleanReason)
                    return $"Report.NotCleanReason '{p.Report.NotCleanReason}', the result's '{reasonAfter}'";
                if (p.Report.SelfContactsLeft == 0 && reasonAfter == "" && p.Report.ResultKind != SolidBooleanResultKind.Solid)
                    return $"all separated and clean, but ResultKind {p.Report.ResultKind}";
                if (plain.Report.Clean && reasonAfter != "")
                    return $"clean without the option, '{reasonAfter}' with it";
                return "";
            }, 40, 9106);
            log.WriteLine($"  results with self-contacts: {withSelfContacts}, all separated: {separated}");
            if (Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") == null)
            {
                Assert.True(withSelfContacts > 0, "no case of the campaign had a self-contact (the property would be vacuous)");
                Assert.True(separated > 0, "no self-contact of the campaign was separated (the property would not check a move)");
            }
        }

        [Fact]
        public void PowerOfTwoScales_GiveTheSameResult_BitForBit()
        {
            Campaign("PowerOfTwoScales", GenCase, c => {
                List<DMesh3> inputs = c.Build();
                var p = Run(inputs, c.Op);
                foreach (int k in new[] { -7, 9 }) {
                    double f = Math.Pow(2, k);
                    var scaled = inputs.Select(m => { var s = new DMesh3(m); MeshTransforms.Scale(s, f); return s; }).ToList();
                    var q = Run(scaled, c.Op);
                    if (q.Status != p.Status || q.BooleanStatus != p.BooleanStatus)
                        return $"2^{k}: status {q.Status}/{q.BooleanStatus} instead of {p.Status}/{p.BooleanStatus}";
                    if (p.Status != SolidBooleanPipeline.PipelineStatus.Success)
                        continue;
                    var back = new DMesh3(q.Result);
                    MeshTransforms.Scale(back, 1 / f);
                    if (Fingerprint(back) != Fingerprint(p.Result))
                        return $"2^{k}: the result scaled back differs";
                }
                return "";
            }, 20, 9104);
        }
    }
}
