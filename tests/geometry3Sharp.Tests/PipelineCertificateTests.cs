using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The harness of the pipeline certificate spike (Harness/PipelineCertificate.cs, docs/research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md):
    /// verdicts on direct runs of the exact stage, and the seeded defects: each known defect injected into a certified run is
    /// rejected, by the clause the note's table names. This is also the mutation check of the checker as a whole.
    /// </summary>
    public class PipelineCertificateTests
    {
        readonly ITestOutputHelper log;
        public PipelineCertificateTests(ITestOutputHelper log) { this.log = log; }

        static DMesh3 Sphere(double r, Vector3d c, int n = 5)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }
        static DMesh3 Box(Vector3d c, Vector3d e) => new TrivialBox3Generator { Box = new Box3d(c, e) }.Generate().MakeDMesh();
        static DMesh3 Copy(DMesh3 m) => new DMesh3(m, false, MeshComponents.None);

        static DirectRun Spheres(Ops op = Ops.Union)
        {
            DirectRun run = DirectRun.Compute(new[] { Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(1.2, 0.1, 0.05)) }, op);
            Assert.True(run.Succeeded, run.Failure);
            return run;
        }

        // ---------------------------------------------------------------- verdicts

        [Fact]
        public void DirectRuns_AreCertified_AndAgreeWithBooleanCertificateCheck()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                DirectRun run = Spheres(op);
                PipelineVerdict v = PipelineCertificate.Certify(run);
                log.WriteLine($"{op}: {v} (certificate {v.CertificateMs:F1} ms, boolean {run.BooleanMs:F1} ms)");
                Assert.Equal(PipelineVerdictKind.Certified, v.Kind);
                // (the clause chain of the harness is BooleanCertificate.Check, split to be timed)
                Assert.Equal("", BooleanCertificate.From(run.Boolean).Check());
                Assert.True(v.CertificateMs > 0);
            }
        }

        [Fact]
        public void IntersectionWithTheEarlyOut_IsNotCertifiable_AndOpenInputsToo()
        {
            DirectRun cull = DirectRun.Compute(new[] { Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(1.2, 0.1, 0.05)) }, Ops.Intersection, certifiableIntersection: false);
            PipelineVerdict v = PipelineCertificate.Certify(cull);
            log.WriteLine(v.ToString());
            Assert.Equal(PipelineVerdictKind.NotCertifiable, v.Kind);
            Assert.Equal("n/a", v.Clause);

            // an open tool: the sheet role (generalized winding numbers)
            DMesh3 open = Sphere(0.5, new Vector3d(0.2, 0, 0));
            open.RemoveTriangle(open.TriangleIndices().First());
            DirectRun sheet = DirectRun.Compute(new[] { Sphere(1, Vector3d.Zero), open }, Ops.Difference, configure: b => b.AllowOpenMeshes = true);
            Assert.True(sheet.Succeeded, sheet.Failure);
            sheet.Route = PipelineRoute.SheetRole;
            v = PipelineCertificate.Certify(sheet);
            log.WriteLine(v.ToString());
            Assert.Equal(PipelineVerdictKind.NotCertifiable, v.Kind);
            Assert.Equal(PipelineRoute.SheetRole, v.Route);
            Assert.Equal("n/a", v.Clause);   // (not "no-result": the sheet run has a result, the certificate does not apply to it)

            // a route without an exact arrangement, and a run that failed
            var wrap = new DirectRun();
            wrap.Route = PipelineRoute.Wrap;
            Assert.Equal("route", PipelineCertificate.Certify(wrap).Clause);
            DirectRun refused = DirectRun.Compute(new[] { Sphere(1, Vector3d.Zero), open }, Ops.Difference);
            Assert.False(refused.Succeeded);
            Assert.Equal("no-result", PipelineCertificate.Certify(refused).Clause);
        }

        // ---------------------------------------------------------------- seeded defects: each is rejected by the clause named

        DirectRun sharedRun;

        PipelineVerdict Seeded(string what, Action<PipelineEvidence> mutate, string clause, DirectRun run = null)
        {
            run = run ?? sharedRun ?? (sharedRun = Spheres());
            PipelineEvidence e = PipelineEvidence.From(run);
            mutate(e);
            PipelineVerdict v = PipelineCertificate.Judge(e);
            log.WriteLine($"{what}: {v}");
            Assert.True(v.Kind == PipelineVerdictKind.Rejected && v.Clause == clause, $"{what}: expected Rejected({clause}), got {v}");
            return v;
        }

        static int SplitPiece(BooleanCertificate c) =>
            Enumerable.Range(0, c.PieceCorners.Length).First(i => c.IsClassified(i) && Enumerable.Range(0, c.PieceCorners.Length).Count(j => c.PieceSource[j] == c.PieceSource[i]) >= 3);

        static T[] RemoveAt<T>(T[] a, int i) => a.Where((_, k) => k != i).ToArray();
        static T[] Append<T>(T[] a, T x) => a.Concat(new[] { x }).ToArray();

        [Fact]
        public void SeededDefects_AreRejected_ByTheRightClause()
        {
            // (the unmutated run is certified: the baseline of every row)
            Assert.Equal(PipelineVerdictKind.Certified, PipelineCertificate.Judge(PipelineEvidence.From(sharedRun = Spheres())).Kind);
            // a piece's label flipped: kept / not kept, and the winding numbers in front
            Seeded("label: keep of a classified piece toggled", e => {
                int i = Enumerable.Range(0, e.Certificate.Keep.Length).First(k => e.Certificate.IsClassified(k) && e.Certificate.Keep[k]);
                e.Certificate.Keep[i] = false;
            }, "C3/C4");
            Seeded("label: winding number in front of a piece changed", e => {
                BooleanCertificate c = e.Certificate;
                int i = Enumerable.Range(0, c.Front.Length).First(k => c.Front[k] != null && c.IsClassified(k));
                c.Front[i][0] += 1;
            }, "C3/C4");

            // a piece dropped (of a source triangle that is split) / duplicated
            Seeded("piece dropped", e => {
                BooleanCertificate c = e.Certificate;
                int j = SplitPiece(c);
                c.PieceMesh = RemoveAt(c.PieceMesh, j); c.PieceSource = RemoveAt(c.PieceSource, j); c.PieceCorners = RemoveAt(c.PieceCorners, j);
                c.Front = RemoveAt(c.Front, j); c.Keep = RemoveAt(c.Keep, j); c.Flip = RemoveAt(c.Flip, j);
            }, "C1");
            Seeded("piece duplicated", e => {
                BooleanCertificate c = e.Certificate;
                int j = SplitPiece(c);
                c.PieceMesh = Append(c.PieceMesh, c.PieceMesh[j]); c.PieceSource = Append(c.PieceSource, c.PieceSource[j]);
                c.PieceCorners = Append(c.PieceCorners, (ExactPoint3[])c.PieceCorners[j].Clone());
                c.Front = Append(c.Front, c.Front[j] == null ? null : (int[])c.Front[j].Clone()); c.Keep = Append(c.Keep, c.Keep[j]); c.Flip = Append(c.Flip, c.Flip[j]);
            }, "C1");

            // the rounded output: a vertex moved beyond the rounding bound, a triangle with swapped orientation, a patch of invented surface
            Seeded("vertex moved by 10 Delta (beyond the rounding bound)", e => {
                e.Certificate.Result = Copy(e.Certificate.Result);
                int v = e.Certificate.Result.VertexIndices().First();
                e.Certificate.Result.SetVertex(v, e.Certificate.Result.GetVertex(v) + new Vector3d(10 * e.Certificate.Delta, 0, 0));
            }, "C5");
            Seeded("one output triangle with swapped orientation", e => {
                e.Certificate.Result = Copy(e.Certificate.Result);
                e.Certificate.Result.ReverseTriOrientation(e.Certificate.Result.TriangleIndices().First());
            }, "C5");
            Seeded("a closed patch of invented surface (a tetrahedron) next to the result", e => {
                e.Certificate.Result = Copy(e.Certificate.Result);
                AppendTetrahedron(e.Certificate.Result, new Vector3d(0.1, 0.1, 0.1));
            }, "C5");

            // C0 through Judge: on the repair route (on Direct, InputsFed would see it first) the copy the exact stage got is the passed one, but the
            // boolean's own inputs are reversed: the orientation the exact stage took is not the inputs' (Certificate.Inputs and OriginalInputs mutated together)
            Seeded("an input of the certificate reversed (C0, repair route)", e => {
                var reversed = e.Certificate.Inputs.Select(m => { DMesh3 r = Copy(m); r.ReverseOrientation(false); return r; }).ToList();
                e.Route = PipelineRoute.InputRepair;
                e.Certificate.Inputs = reversed;
                e.FedInputs = reversed;
                e.OriginalInputs = reversed;
            }, "C0");
            // C2 through Judge: a split source triangle left unsplit (the missed intersection); some source of the run shows it
            {
                bool seen = false;
                BooleanCertificate probe = BooleanCertificate.From((sharedRun ?? (sharedRun = Spheres())).Boolean);
                var split = probe.PieceSource.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(x => x).ToList();
                for (int k = 0; k < Math.Min(12, split.Count) && !seen; ++k) {
                    int source = split[k * split.Count / Math.Min(12, split.Count)];
                    PipelineEvidence e = PipelineEvidence.From(sharedRun);
                    BooleanCertificate c = e.Certificate;
                    int[] keep = Enumerable.Range(0, c.PieceSource.Length).Where(i => c.PieceSource[i] != source).ToArray();
                    int mesh = c.PieceMesh[Array.IndexOf(c.PieceSource, source)];
                    c.PieceMesh = keep.Select(i => c.PieceMesh[i]).Append(mesh).ToArray();
                    c.PieceCorners = keep.Select(i => c.PieceCorners[i]).Append(c.SourceCornersOnGrid(source)).ToArray();
                    c.PieceSource = keep.Select(i => c.PieceSource[i]).Append(source).ToArray();
                    if (!c.IsClassified(c.PieceSource.Length - 1)) continue;
                    PipelineVerdict v = PipelineCertificate.Judge(e);
                    log.WriteLine("a split source left unsplit: " + v);
                    Assert.True(v.Kind == PipelineVerdictKind.Rejected && v.Clause == "C2", "a split source left unsplit: " + v);
                    seen = true;
                }
                Assert.True(seen, "no split source gave a classified unsplit piece");
            }

            // the inputs the exact stage got are not the inputs passed: a plate dropped from B (issue #1567's mechanism)
            {
                DMesh3 b = Box(new Vector3d(5, 0, 0), new Vector3d(0.5, 0.5, 0.5));
                DMesh3 plate = Box(new Vector3d(5, 20, 0), new Vector3d(5, 5, 0.005));
                DMesh3 passed = Copy(b); MeshEditor.Append(passed, plate);
                DirectRun run = DirectRun.Compute(new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), b }, Ops.Union);
                Assert.True(run.Succeeded, run.Failure);
                Seeded("a part of input B dropped before the exact stage", e => e.OriginalInputs = new[] { run.OriginalInputs[0], passed }, "InputsFed", run);
            }
        }

        [Fact]
        public void SeededRoundingDefects_AreRejected_ByTheRoundingChecks()
        {
            // the rounding repair's volume: a change above the limit and the floor
            Seeded("the rounding changed the volume by 1 %", e => { e.RoundingRan = true; e.RoundingVolumeBefore = 4.0; e.RoundingVolumeAfter = 3.96; }, "RoundingVolume");
            // a patch of surface on no input, in a result the exact-stage clauses have not seen (what an input repair adds): C5 is bypassed by
            // handing the checker the patch in the result before rounding too, so that only the rounding checks can see it
            DirectRun run = Spheres();
            PipelineEvidence ev = PipelineEvidence.From(run);
            DMesh3 withPatch = Copy(ev.Certificate.Result);
            AppendTetrahedron(withPatch, new Vector3d(0.1, 0.1, 0.1));
            PipelineCertificate.InventedSurface inv = PipelineCertificate.NoInventedSurface(withPatch, ev.OriginalInputs, ev.RoundingBound, 0);
            log.WriteLine($"invented: share {inv.Share:G4}, {inv.Triangles} triangles, max distance {inv.MaxDistance:G4}, tolerance {inv.Tolerance:G4}");
            Assert.Equal(4, inv.Triangles);
            Assert.True(inv.Share > 0);
            PipelineCertificate.InventedSurface none = PipelineCertificate.NoInventedSurface(ev.Certificate.Result, ev.OriginalInputs, ev.RoundingBound, 0);
            Assert.Equal(0, none.Share);
            // a cube lid: a hole filled by an input repair on a copy, the original input is open
            DMesh3 open = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            open.RemoveTriangle(open.TriangleIndices().First());
            DMesh3 lidded = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            PipelineCertificate.InventedSurface lid = PipelineCertificate.NoInventedSurface(lidded, new[] { open }, 0, 0);
            Assert.Equal(1, lid.Triangles);
        }

        [Fact]
        public void BelowTheRoundingBound_ADefectIsNotSeen_AndThatIsTheStatement()
        {
            // the negative control: moving a vertex by half of Delta stays Certified; the certificate is about the point set up to the bound
            DirectRun run = Spheres();
            PipelineEvidence e = PipelineEvidence.From(run);
            e.Certificate.Result = Copy(e.Certificate.Result);
            int v = e.Certificate.Result.VertexIndices().First();
            e.Certificate.Result.SetVertex(v, e.Certificate.Result.GetVertex(v) + new Vector3d(0.5 * e.Certificate.Delta, 0, 0));
            PipelineVerdict verdict = PipelineCertificate.Judge(e);
            log.WriteLine($"Delta {e.Certificate.Delta:G4}: {verdict}");
            Assert.Equal(PipelineVerdictKind.Certified, verdict.Kind);
        }

        static DMesh3 Moved(DMesh3 m, Similarity sim)
        {
            DMesh3 c = Copy(m);
            foreach (int vid in c.VertexIndices()) c.SetVertex(vid, sim.Apply(c.GetVertex(vid)));
            return c;
        }

        // (small spheres: nine similarities, each a boolean and its certificate; Slow-free)
        [Fact]
        public void Verdict_IsInvariantUnderSimilarities_AndRepeatable()
        {
            var shapes = new[] { Sphere(1, Vector3d.Zero, 3), Sphere(1, new Vector3d(1.2, 0.1, 0.05), 3) };
            PipelineVerdict reference = null;
            foreach (Similarity sim in Similarity.All()) {
                DirectRun run = DirectRun.Compute(shapes.Select(m => Moved(m, sim)).ToArray(), Ops.Difference);
                Assert.True(run.Succeeded, sim + ": " + run.Failure);
                PipelineVerdict v1 = PipelineCertificate.Judge(PipelineEvidence.From(run)), v2 = PipelineCertificate.Judge(PipelineEvidence.From(run));
                Assert.Equal(PipelineVerdictKind.Certified, v1.Kind);
                Assert.Equal(v1.ToString(), v2.ToString());   // repeatable on one run
                if (reference == null) reference = v1;
                // (the verdict, the clause and the caveats do not depend on the frame; the numbers of a Rejected verdict are not compared across frames)
                Assert.True(v1.Kind == reference.Kind && v1.Clause == reference.Clause && v1.Caveats.SequenceEqual(reference.Caveats), sim + ": " + v1);
                if (sim.IsExact) Assert.Equal(reference.ToString(), v1.ToString());   // the 2^k scales: the same text, bit for bit
            }
        }

        [Fact]
        public void Caveats_ContactsInsideOutInputsRemovedParts_AndALegitimateEmptyResult()
        {
            // two boxes that touch along an edge: the union has a contact, NotCleanReason says "self-intersections", the verdict is Certified with a caveat
            DirectRun touch = DirectRun.Compute(new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1)) }, Ops.Union);
            Assert.True(touch.Succeeded, touch.Failure);
            PipelineVerdict v = PipelineCertificate.Certify(touch);
            log.WriteLine(v.ToString() + ": " + string.Join(" | ", v.Caveats));
            Assert.Equal(PipelineVerdictKind.Certified, v.Kind);
            Assert.Contains(v.Caveats, c => c.StartsWith("contacts"));

            // the intersection of two disjoint solids is empty, and that is clean (the exact result is empty too)
            DirectRun empty = DirectRun.Compute(new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(5, 0, 0), new Vector3d(1, 1, 1)) }, Ops.Intersection);
            v = PipelineCertificate.Certify(empty);
            Assert.Equal(PipelineVerdictKind.Certified, v.Kind);
            Assert.Equal(0, empty.Boolean.Result.TriangleCount);

            // an outward cavity wall (both shells outward: winding number 2 in the cavity): certified as what its winding numbers say, with the caveat
            DMesh3 wall = Box(Vector3d.Zero, new Vector3d(2, 2, 2));
            MeshEditor.Append(wall, Box(Vector3d.Zero, new Vector3d(1, 1, 1)));
            DirectRun cavity = DirectRun.Compute(new[] { wall, Box(new Vector3d(6, 0, 0), new Vector3d(0.5, 0.5, 0.5)) }, Ops.Union);
            Assert.True(cavity.Succeeded, cavity.Failure);
            v = PipelineCertificate.Certify(cavity);
            log.WriteLine(v.ToString() + ": " + string.Join(" | ", v.Caveats));
            Assert.Equal(PipelineVerdictKind.Certified, v.Kind);
            Assert.Contains(v.Caveats, c => c.Contains("winding number"));

            // a clean run has no caveat
            Assert.Empty(PipelineCertificate.Certify(Spheres()).Caveats);

            // the rounding removed a thin solid (the audit's "U plate 2^-35" in the frame rotation + UTM offset: the cube [0,2]^3, a plate 2 x 2 x 2^-35 beside it)
            double h = Math.Pow(2, -35);
            Func<DMesh3, DMesh3> frame = m => {
                var r = Copy(m);
                foreach (int vid in r.VertexIndices()) {
                    Vector3d p = r.GetVertex(vid);
                    double ax = 1 / Math.Sqrt(14), ay = 2 / Math.Sqrt(14), az = 3 / Math.Sqrt(14), c = Math.Cos(0.7), sn = Math.Sin(0.7), k = 1 - c;
                    r.SetVertex(vid, new Vector3d((c + ax * ax * k) * p.x + (ax * ay * k - az * sn) * p.y + (ax * az * k + ay * sn) * p.z,
                        (ay * ax * k + az * sn) * p.x + (c + ay * ay * k) * p.y + (ay * az * k - ax * sn) * p.z,
                        (az * ax * k - ay * sn) * p.x + (az * ay * k + ax * sn) * p.y + (c + az * az * k) * p.z) + new Vector3d(500000, 5500000, 100));
                }
                return r;
            };
            DirectRun thin = DirectRun.Compute(new[] { frame(Box(new Vector3d(1, 1, 1), new Vector3d(1, 1, 1))), frame(Box(new Vector3d(4, 1, h / 2), new Vector3d(1, 1, h / 2))) }, Ops.Union);
            Assert.True(thin.Succeeded, thin.Failure);
            Assert.True(thin.Boolean.Rounding.RemovedComponents > 0, "the rounding removed nothing: the case no longer shows the caveat");
            v = PipelineCertificate.Certify(thin);
            log.WriteLine(v.ToString() + ": " + string.Join(" | ", v.Caveats));
            Assert.Equal(PipelineVerdictKind.CertifiedWithRemovals, v.Kind);
            Assert.False(v.Kind == PipelineVerdictKind.Certified);
            Assert.Contains(v.Caveats, c => c.StartsWith("rounding removed"));

            // two thin plates, both removed: CertifiedWithRemovals; the checker's exemption (every thin component of the exact result) is
            // matched against the removals, so a defect that loses one more thin part than the rounding removed cannot hide behind it
            DirectRun two = DirectRun.Compute(new[] { frame(Box(new Vector3d(1, 1, 1), new Vector3d(1, 1, 1))), frame(Box(new Vector3d(4, 1, h / 2), new Vector3d(1, 1, h / 2))), frame(Box(new Vector3d(7, 1, h / 2), new Vector3d(1, 1, h / 2))) }, Ops.Union);
            Assert.True(two.Succeeded, two.Failure);
            Assert.True(two.Boolean.Rounding.RemovedComponents == 2, "the rounding removed " + two.Boolean.Rounding.RemovedComponents + " parts, 2 expected");
            Assert.Equal(PipelineVerdictKind.CertifiedWithRemovals, PipelineCertificate.Certify(two).Kind);
            PipelineEvidence ev = PipelineEvidence.From(two);
            ev.Certificate.RemovedComponents = 1; ev.RemovedComponentInfoCount = 1;   // (one removal reported, two thin components exempt)
            v = PipelineCertificate.Judge(ev);
            log.WriteLine(v.ToString());
            Assert.True(v.Kind == PipelineVerdictKind.NotCertifiable && v.Clause == "removals-unmatched", v.ToString());
            ev = PipelineEvidence.From(two);
            ev.RemovedComponentInfoCount = 0;   // (the listed removals do not match the count)
            Assert.Equal("removals-unmatched", PipelineCertificate.Judge(ev).Clause);
        }

        [Fact]
        public void InputsFed_LostSurfaceOnARouteThatAltersInputs_IsNotCertifiable()
        {
            DMesh3 a = Box(Vector3d.Zero, new Vector3d(1, 1, 1)), other = Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 1, 1));
            DirectRun run = DirectRun.Compute(new[] { a, other }, Ops.Union);
            Assert.True(run.Succeeded, run.Failure);
            // (input B passed with a 2 x 2 plate more, which the route dropped: issue #1567's mechanism, on the repair route)
            DMesh3 passed = Copy(other); MeshEditor.Append(passed, Box(new Vector3d(9, 0, 0), new Vector3d(1, 1, 0.001)));
            PipelineEvidence e = PipelineEvidence.From(run);
            e.Route = PipelineRoute.InputRepair;
            e.OriginalInputs = new[] { a, passed };
            PipelineVerdict v = PipelineCertificate.Judge(e);
            log.WriteLine(v.ToString());
            Assert.True(v.Kind == PipelineVerdictKind.NotCertifiable && v.Clause == "inputs-altered", v.ToString());
            // the drop is explained by the route (the 12 triangles of the plate): then the rest decides
            e.ExplainedDroppedTriangles = 12;
            Assert.Equal(PipelineVerdictKind.Certified, PipelineCertificate.Judge(e).Kind);
            // added triangles alone stay a caveat (NoInventedSurface covers them): the exact stage got more than was passed
            e = PipelineEvidence.From(run);
            e.Route = PipelineRoute.InputRepair;
            DMesh3 fedMore = Copy(a); MeshEditor.Append(fedMore, Box(new Vector3d(0, 0, 9), new Vector3d(1, 1, 1)));
            PipelineCertificate.InputsFedReason(new[] { a }, new[] { fedMore }, out bool identical, out int missing, out int extra, 0);
            Assert.False(identical);
            Assert.Equal(0, missing);
            Assert.Equal(12, extra);
            // GridRetry: the fed inputs moved by at most the displacement are the inputs passed; moved by more they are not
            DMesh3 snapped = Copy(a);
            foreach (int vid in snapped.VertexIndices()) snapped.SetVertex(vid, snapped.GetVertex(vid) + new Vector3d(0.001, -0.001, 0));
            Assert.Equal("", PipelineCertificate.InputsFedReason(new[] { a }, new[] { snapped }, out identical, out missing, out extra, 0.0011));
            Assert.Equal(0, missing + extra);
            string far = PipelineCertificate.InputsFedReason(new[] { a }, new[] { snapped }, out identical, out missing, out extra, 0.0005);
            Assert.NotEqual("", far);
            Assert.Equal(12, missing);
            // no record of the inputs is a reason, not "identical"
            Assert.NotEqual("", PipelineCertificate.InputsFedReason(null, new[] { a }, out identical));
            Assert.False(identical);
            Assert.NotEqual("", PipelineCertificate.InputsFedReason(new[] { a }, null, out identical));
        }

        // the audit's generic rotation (axis (1, 2, 3), angle 0.7), with the UTM offset if asked
        static DMesh3 Turn(DMesh3 m, bool utm)
        {
            DMesh3 r = Copy(m);
            double ax = 1 / Math.Sqrt(14), ay = 2 / Math.Sqrt(14), az = 3 / Math.Sqrt(14), c = Math.Cos(0.7), sn = Math.Sin(0.7), k = 1 - c;
            foreach (int vid in r.VertexIndices()) {
                Vector3d p = r.GetVertex(vid);
                Vector3d q = new Vector3d((c + ax * ax * k) * p.x + (ax * ay * k - az * sn) * p.y + (ax * az * k + ay * sn) * p.z,
                    (ay * ax * k + az * sn) * p.x + (c + ay * ay * k) * p.y + (ay * az * k - ax * sn) * p.z,
                    (az * ax * k - ay * sn) * p.x + (az * ay * k + ax * sn) * p.y + (c + az * az * k) * p.z);
                r.SetVertex(vid, utm ? q + new Vector3d(500000, 5500000, 100) : q);
            }
            return r;
        }

        [Fact]
        public void TheOtherClauses_ReachTheVerdict_ThroughJudge()
        {
            // NoInventedSurface on the InputRepair route: the exact stage got the closed copy, the input as passed is open (one face missing)
            DMesh3 open = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            open.RemoveTriangle(open.TriangleIndices().First());
            DMesh3 repaired = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            DMesh3 other = Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 1, 1));
            DirectRun run = DirectRun.Compute(new[] { repaired, other }, Ops.Union);
            Assert.True(run.Succeeded, run.Failure);
            PipelineEvidence e = PipelineEvidence.From(run);
            e.Route = PipelineRoute.InputRepair;
            e.OriginalInputs = new[] { open, other };
            PipelineVerdict v = PipelineCertificate.Judge(e);
            log.WriteLine(v.ToString());
            Assert.True(v.Kind == PipelineVerdictKind.Rejected && v.Clause == "NoInventedSurface", v.ToString());
            Assert.Equal(1, (int)v.Numbers[2]);
            // (the same run on the direct route: the inputs fed are not the inputs passed)
            e = PipelineEvidence.From(run);
            e.OriginalInputs = new[] { open, other };
            Assert.Equal("InputsFed", PipelineCertificate.Judge(e).Clause);

            // NotClean: the slab 2 x 2 x 2^-35 of an intersection in the rotated frame with the UTM offset is removed by the rounding, the output is empty
            double h = Math.Pow(2, -35);
            DirectRun slab = DirectRun.Compute(new[] { Turn(Box(new Vector3d(1, 1, 1), new Vector3d(1, 1, 1)), true), Turn(Box(new Vector3d(3 - h / 2, 1, 1), new Vector3d(1 + h / 2, 1, 1)), true) }, Ops.Intersection);
            Assert.True(slab.Succeeded, slab.Failure);
            v = PipelineCertificate.Certify(slab);
            log.WriteLine(v.ToString());
            Assert.True(v.Kind == PipelineVerdictKind.Rejected && v.Clause == "NotClean", v.ToString());
            Assert.Equal(0, slab.Boolean.Result.TriangleCount);
            Assert.True(slab.Boolean.Rounding.RemovedComponents > 0);
        }

        // the audit's box builder (its triangulation matters for the case below)
        static DMesh3 AuditBox(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[] t = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 7, 3, 2, 6, 7, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            for (int i = 0; i < 36; i += 3) m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        [Fact]
        public void CoverageUndecided_IsNotCertifiable_NotRejected_AndNotCertified()
        {
            // the audit's "flush cut D (coplanar faces)" in the rotated frame: the output merges coplanar triangles of the exact result, which no single
            // triangle covers even after the splits (the checker's limit, section 4.7 of the note); the sampled distance between the two is tiny
            DirectRun run = DirectRun.Compute(new[] { Turn(AuditBox(0, 0, 0, 2, 2, 2), false), Turn(AuditBox(1, 0, 0, 2, 2, 2), false) }, Ops.Difference);
            Assert.True(run.Succeeded, run.Failure);
            PipelineVerdict v = PipelineCertificate.Certify(run);
            log.WriteLine(v.ToString());
            Assert.True(v.Kind == PipelineVerdictKind.NotCertifiable && v.Clause == "coverage-undecided", v.ToString());
        }

        static void AppendTetrahedron(DMesh3 m, Vector3d at)
        {
            int a = m.AppendVertex(at), b = m.AppendVertex(at + new Vector3d(0.01, 0, 0)), c = m.AppendVertex(at + new Vector3d(0, 0.01, 0)), d = m.AppendVertex(at + new Vector3d(0, 0, 0.01));
            m.AppendTriangle(a, c, b); m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); m.AppendTriangle(c, a, d);
        }
    }
}
