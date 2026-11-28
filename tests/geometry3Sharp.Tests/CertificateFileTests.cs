using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The certificate as bytes and the stand-alone checker (Harness/CertificateFile.cs, step B of
    /// docs/research/CERTIFYING_BOOLEANS_2026-11.md): writing is deterministic and reading it back gives the same bytes and
    /// the same verdict as the in-process checker on the fuzzer's closed families; every tampering of the certificate, of the
    /// output and of the inputs that the checker can see is rejected; the exact frames (2^k scales) give the same certificate.
    /// </summary>
    public class CertificateFileTests
    {
        readonly ITestOutputHelper log;
        public CertificateFileTests(ITestOutputHelper log) { this.log = log; }

        // (RegionClipping = false: the file format has no regions; a certificate of a region-clipped result is checked in process only, so the
        // stand-alone verdict would differ from the in-process one)
        static BooleanCertificate Run(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean eb) =>
            BooleanCertificate.Run(a, b, op, out eb, x => { x.RegionClipping = false; if (op == Ops.Intersection) x.CullOutsideA = false; });

        // the in-process verdict, as CertificateFile.Check words it
        static string InProcess(BooleanCertificate c)
        {
            string r = c.Check();
            if (r != "") return r;
            return c.Uncovered == 0 ? "" : $"{c.Uncovered} triangles not covered within Delta";
        }

        static DMesh3 Sphere(Vector3d center, double r = 1, int n = 4)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        static readonly Ops[] AllOps = { Ops.Union, Ops.Intersection, Ops.Difference };

        public static IEnumerable<object[]> Families() => BooleanFuzzCases.ClosedFamilies.Select(f => new object[] { f });

        // ---------------------------------------------------------------- round trip, and the same verdicts as in process

        /// <summary>
        /// The 84 runs of the study (the closed families, seeds 0-3, three operations): the bytes are deterministic (twice the
        /// same), reading and writing again gives the same bytes, and the stand-alone checker returns the in-process verdict
        /// word for word (certified, or the same reason, as for the sliver families it documents).
        /// </summary>
        [Fact]
        public void RegionClippedCertificate_RoundTrips_AndTamperingFlagIsRejected()
        {
            // two grid boxes with a flush coplanar face: the default clips regions, and CertificateFile v2 preserves RegionClipping
            DMesh3 a = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 4 }.Generate().MakeDMesh();
            DMesh3 b = new GridBox3Generator() { Box = new Box3d(new Vector3d(0.8, 0.7, 0.5), new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = 4 }.Generate().MakeDMesh();
            foreach (Ops op in new[] { Ops.Union, Ops.Difference }) {
                BooleanCertificate c = BooleanCertificate.Run(a, b, op, out ExactMeshBoolean eb, null);
                Assert.True(eb.RegionClipping && c.RegionClipping);
                byte[] bytes = CertificateFile.Write(c);
                Assert.Equal(bytes, CertificateFile.Write(c));
                var inputs = new List<DMesh3> { a, b };
                BooleanCertificate back = CertificateFile.Read(bytes, inputs, eb.Result);
                Assert.True(back.RegionClipping);
                Assert.Equal(bytes, CertificateFile.Write(back));
                Assert.Equal("", CertificateFile.Check(bytes, inputs, eb.Result));
                Assert.Equal(InProcess(BooleanCertificate.From(eb)), CertificateFile.Check(bytes, inputs, eb.Result));
                // tampering the RegionClipping flag off on a region-clipped certificate must be rejected (C2 straddle)
                back.RegionClipping = false;
                Assert.NotEqual("", CertificateFile.Check(CertificateFile.Write(back), inputs, eb.Result));
            }
            // RegionClipping = false still round-trips cleanly with RegionClipping == false
            BooleanCertificate old = BooleanCertificate.Run(a, b, Ops.Union, out ExactMeshBoolean ebOld, x => x.RegionClipping = false);
            Assert.False(old.RegionClipping);
            byte[] oldBytes = CertificateFile.Write(old);
            BooleanCertificate oldBack = CertificateFile.Read(oldBytes, new List<DMesh3> { a, b }, ebOld.Result);
            Assert.False(oldBack.RegionClipping);
            Assert.Equal("", CertificateFile.Check(oldBytes, new List<DMesh3> { a, b }, ebOld.Result));
        }

[Theory, MemberData(nameof(Families))]
        public void RoundTrip_GivesTheSameBytes_AndTheInProcessVerdict(string family)
        {
            int runs = 0, certified = 0; long certBytes = 0, outBytes = 0;
            for (int seed = 0; seed < 4; ++seed) {
                FuzzCase fc = BooleanFuzzCases.Make(family, seed);
                foreach (Ops op in AllOps) {
                    BooleanCertificate c = Run(fc.A, fc.B, op, out ExactMeshBoolean eb);
                    string expected = InProcess(BooleanCertificate.From(eb));
                    byte[] bytes = CertificateFile.Write(c);
                    Assert.Equal(bytes, CertificateFile.Write(c));
                    var inputs = new List<DMesh3> { fc.A, fc.B };
                    BooleanCertificate back = CertificateFile.Read(bytes, inputs, eb.Result);
                    Assert.Equal(bytes, CertificateFile.Write(back));
                    // (the arrangement's meshes are rebuilt from the caller's)
                    for (int m = 0; m < 2; ++m)
                        foreach (int t in c.Meshes[m].TriangleIndices()) Assert.Equal(c.Meshes[m].GetTriangle(t), back.Meshes[m].GetTriangle(t));
                    string got = CertificateFile.Check(bytes, inputs, eb.Result);
                    Assert.True(expected == got, $"{family} #{seed} {op}: in process '{expected}', stand-alone '{got}'");
                    runs++; if (got == "") certified++;
                    certBytes += bytes.Length; outBytes += 24L * eb.Result.VertexCount + 12L * eb.Result.TriangleCount;
                }
            }
            log.WriteLine($"{family}: {runs} runs, {certified} certified, certificate {certBytes} bytes, output {outBytes} bytes");
            Assert.True(certified >= runs - 3, $"{certified} of {runs}");
        }

        /// <summary>
        /// An operand with a few triangles turned around, which the boolean turns back (AreaMajority): the reversed triangles are in the file, the reader
        /// rebuilds the arrangement's meshes from the caller's, and the verdict is the in-process one, for each operation.
        /// </summary>
        [Fact]
        public void ReorientedInputs_RoundTrip()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.25, 0.125));
            foreach (int t in b.TriangleIndices().Take(3)) b.ReverseTriOrientation(t);       // three triangles turned around
            foreach (Ops op in AllOps) {
                BooleanCertificate c = BooleanCertificate.Run(a, b, op, out ExactMeshBoolean eb, x => {
                    x.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority;
                    x.RegionClipping = false;       // (the file format has no regions)
                    if (op == Ops.Intersection) x.CullOutsideA = false;
                });
                Assert.True(c.ReorientedTriangles > 0, "nothing reoriented");
                byte[] bytes = CertificateFile.Write(c);
                var inputs = new List<DMesh3> { a, b };
                BooleanCertificate back = CertificateFile.Read(bytes, inputs, eb.Result);
                Assert.Equal(c.ReorientedTriangles, back.ReorientedTriangles);
                foreach (int t in c.Meshes[1].TriangleIndices()) Assert.Equal(c.Meshes[1].GetTriangle(t), back.Meshes[1].GetTriangle(t));
                Assert.Equal(InProcess(BooleanCertificate.From(eb)), CertificateFile.Check(bytes, inputs, eb.Result));
                Assert.Equal("", CertificateFile.Check(bytes, inputs, eb.Result));
            }
        }

        // ---------------------------------------------------------------- tampering with the certificate

        sealed class Setup
        {
            public DMesh3 A, B; public ExactMeshBoolean Boolean; public BooleanCertificate Cert; public byte[] Bytes; public List<DMesh3> Inputs;
        }

        static Setup Spheres(Ops op = Ops.Union, int edgeVertices = 4)
        {
            var s = new Setup { A = Sphere(Vector3d.Zero, 1, edgeVertices), B = Sphere(new Vector3d(0.5, 0.25, 0.125), 1, edgeVertices) };
            s.Cert = Run(s.A, s.B, op, out s.Boolean);
            s.Bytes = CertificateFile.Write(s.Cert);
            s.Inputs = new List<DMesh3> { s.A, s.B };
            Assert.Equal("", CertificateFile.Check(s.Bytes, s.Inputs, s.Boolean.Result));
            return s;
        }

        // a fresh copy of the certificate of s, mutated, written and checked: the verdict (must be a rejection)
        static string Tampered(Setup s, Action<BooleanCertificate> mutate)
        {
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, s.Boolean.Result);
            mutate(c);
            return CertificateFile.Check(CertificateFile.Write(c), s.Inputs, s.Boolean.Result);
        }

        static ExactPoint3 Shift(ExactPoint3 p, int k) =>
            ExactPoint3.Create(p.X + (k == 0 ? p.W : 0), p.Y + (k == 1 ? p.W : 0), p.Z + (k == 2 ? p.W : 0), p.W);

        /// <summary>
        /// A corner of a piece moved by one grid unit, a rational changed in every piece that has it, an entry dropped, entries
        /// swapped, a label, a flag or the grid changed: each is rejected, by the part of the checker that owns it.
        /// </summary>
        [Fact]
        public void TamperedCertificates_AreRejected()
        {
            Setup s = Spheres();
            int n = s.Cert.PieceCorners.Length;
            // a piece corner that is a constructed point (W > 1) and its first piece
            int ci = Enumerable.Range(0, n).First(i => s.Cert.PieceCorners[i].Any(p => p.W > 1));
            int ck = Array.FindIndex(s.Cert.PieceCorners[ci], p => p.W > 1);
            ExactPoint3 victim = s.Cert.PieceCorners[ci][ck];
            var rejected = new List<string>();
            void Reject(string what, Action<BooleanCertificate> mutate)
            {
                string r = Tampered(s, mutate);
                Assert.True(r != "", what + ": accepted");
                rejected.Add($"{what}: {r.Substring(0, Math.Min(90, r.Length))}");
            }
            Reject("one rational of one piece changed", c => c.PieceCorners[ci][ck] = Shift(victim, 0));
            Reject("one rational changed in every piece that has it", c => {
                foreach (ExactPoint3[] piece in c.PieceCorners)
                    for (int k = 0; k < 3; ++k) if (piece[k].Equals(victim)) piece[k] = Shift(victim, 1);
            });
            Reject("a denominator changed", c => c.PieceCorners[ci][ck] = ExactPoint3.Create(victim.X, victim.Y, victim.Z, victim.W + 1));
            Reject("a piece dropped", c => Drop(c, ci));
            Reject("the last piece dropped", c => Drop(c, n - 1));
            Reject("two pieces swap their sources", c => {
                int j = Enumerable.Range(0, n).First(i => c.PieceSource[i] != c.PieceSource[ci] && c.PieceMesh[i] == c.PieceMesh[ci]);
                (c.PieceSource[ci], c.PieceSource[j]) = (c.PieceSource[j], c.PieceSource[ci]);
            });
            Reject("two pieces swap their labels", c => {
                int j = Enumerable.Range(0, n).First(i => c.Front[i] != null && c.Front[ci] != null && c.Front[i].SequenceEqual(c.Front[ci]) == false);
                (c.Front[ci], c.Front[j]) = (c.Front[j], c.Front[ci]);
            });
            Reject("a winding number changed", c => c.Front[Array.FindIndex(c.Front, f => f != null)][0] += 1);
            Reject("a keep flag flipped", c => c.Keep[Array.IndexOf(c.Keep, true)] = false);
            Reject("a dropped piece's keep flag set", c => c.Keep[Array.IndexOf(c.Keep, false)] = true);
            Reject("a flip flag flipped", c => c.Flip[Array.IndexOf(c.Keep, true)] ^= true);
            Reject("a corner order reversed (a flipped piece)", c => { var p = c.PieceCorners[ci]; (p[1], p[2]) = (p[2], p[1]); });
            Reject("the grid scale changed", c => c.ScaleExp += 1);
            Reject("the grid centre changed", c => c.Center[0] += 1);
            Reject("an input triangle's culled flag set", c => c.TriCulled[0] = true);
            Reject("an input triangle dropped from the list", c => {
                c.TriMesh = c.TriMesh.Skip(1).ToArray(); c.TriTid = c.TriTid.Skip(1).ToArray(); c.TriCulled = c.TriCulled.Skip(1).ToArray();
                for (int i = 0; i < c.PieceSource.Length; ++i) c.PieceSource[i] = Math.Max(0, c.PieceSource[i] - 1);
            });
            Reject("the operation changed", c => { c.Operation = Ops.Difference; c.Predicate = BooleanCertificate.OperationPredicate(Ops.Difference); });
            Reject("a reported rounding length above the bound", c => c.SetRounding(true, 1.0, 0));
            Reject("a result-before-rounding vertex moved", c => c.AssembledExact[0] = Shift(c.AssembledExact[0], 2));
            Reject("a result-before-rounding triangle reversed", c => c.Assembled.ReverseTriOrientation(c.Assembled.TriangleIndices().First()));
            foreach (string line in rejected) log.WriteLine(line);
        }

        static void Drop(BooleanCertificate c, int i)
        {
            int[] keep = Enumerable.Range(0, c.PieceCorners.Length).Where(k => k != i).ToArray();
            c.PieceMesh = keep.Select(k => c.PieceMesh[k]).ToArray(); c.PieceSource = keep.Select(k => c.PieceSource[k]).ToArray();
            c.PieceCorners = keep.Select(k => c.PieceCorners[k]).ToArray(); c.Front = keep.Select(k => c.Front[k]).ToArray();
            c.Keep = keep.Select(k => c.Keep[k]).ToArray(); c.Flip = keep.Select(k => c.Flip[k]).ToArray();
        }

        /// <summary>
        /// One flipped bit anywhere after the header's rounding settings (which the user reads: a looser bound is a claim, not
        /// an error) is rejected, for every position on a stride through the file; and the same on a difference.
        /// </summary>
        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        public void OneFlippedBit_IsRejected(Ops op)
        {
            Setup s = Spheres(op, 3);
            int start = 24;                                     // after the magic, version, format, operation, culling mesh, flags and settings
            int stride = Math.Max(1, (s.Bytes.Length - start) / 80);
            var accepted = new List<string>();
            int tried = 0;
            for (int pos = start; pos < s.Bytes.Length; pos += stride, ++tried) {
                byte[] b = (byte[])s.Bytes.Clone();
                b[pos] ^= (byte)(1 << (pos % 8));
                if (CertificateFile.Check(b, s.Inputs, s.Boolean.Result) != "") continue;
                // accepted: allowed only where the flip changed nothing the checker reads, a label of a piece outside the culling
                // mesh (a Difference discards what lies outside A, so the winding number of B there is a don't-care)
                BooleanCertificate o = CertificateFile.Read(s.Bytes, s.Inputs, s.Boolean.Result), f = CertificateFile.Read(b, s.Inputs, s.Boolean.Result);
                bool same = o.PieceMesh.SequenceEqual(f.PieceMesh) && o.PieceSource.SequenceEqual(f.PieceSource) && o.Keep.SequenceEqual(f.Keep) && o.Flip.SequenceEqual(f.Flip)
                    && o.TriCulled.SequenceEqual(f.TriCulled) && o.AssembledExact.SequenceEqual(f.AssembledExact);
                for (int i = 0; same && i < o.PieceCorners.Length; ++i) same = o.PieceCorners[i].SequenceEqual(f.PieceCorners[i]);
                for (int i = 0; same && i < o.Front.Length; ++i) {
                    if ((o.Front[i] == null) != (f.Front[i] == null)) same = false;
                    else if (o.Front[i] != null && o.Front[i].SequenceEqual(f.Front[i]) == false)
                        same = o.CullMesh >= 0 && o.Front[i][o.CullMesh] <= 0 && f.Front[i][o.CullMesh] == o.Front[i][o.CullMesh];
                }
                if (same == false) accepted.Add(pos.ToString());
            }
            log.WriteLine($"{tried} flips of {s.Bytes.Length} bytes");
            string secs = string.Join(", ", CertificateFile.LastSections.Select(x => x.section + "@" + x.offset));
            Assert.True(accepted.Count == 0, secs + "; accepted flips at " + string.Join(", ", accepted.Take(20)));
        }

        // ---------------------------------------------------------------- tampering with the output and the inputs

        /// <summary>
        /// The output: a triangle reversed, one removed, a vertex moved by ten times the coverage distance, and the output of
        /// another operation are rejected. A vertex moved by one ulp is within the coverage distance (the result is a rounding
        /// of the exact one, so Delta is the rounding bound plus four ulps): it is accepted exactly when the in-process
        /// checker accepts it, which is the documented meaning of the certificate (Hausdorff distance at most Delta).
        /// </summary>
        [Fact]
        public void TamperedOutputs_AreRejected_AndOneUlpIsWithinTheCoverageDistance()
        {
            Setup s = Spheres();
            DMesh3 Copy() => new DMesh3(s.Boolean.Result, false, MeshComponents.None);
            string Verdict(DMesh3 o) => CertificateFile.Check(s.Bytes, s.Inputs, o);
            int t0 = s.Boolean.Result.TriangleIndices().First();
            { DMesh3 o = Copy(); o.ReverseTriOrientation(t0); Assert.NotEqual("", Verdict(o)); }
            { DMesh3 o = Copy(); o.RemoveTriangle(t0, false, false); Assert.NotEqual("", Verdict(o)); }
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, s.Boolean.Result);
            int v0 = s.Boolean.Result.VertexIndices().First();
            { DMesh3 o = Copy(); o.SetVertex(v0, o.GetVertex(v0) + new Vector3d(10 * c.Delta, 0, 0)); Assert.NotEqual("", Verdict(o)); }
            { DMesh3 o = Copy(); o.SetVertex(v0, o.GetVertex(v0) + new Vector3d(0, 0, 3e-3)); Assert.NotEqual("", Verdict(o)); }
            {
                var other = Run(s.A, s.B, Ops.Intersection, out ExactMeshBoolean eb2);
                Assert.NotEqual("", Verdict(eb2.Result));
            }
            {
                // one ulp: the same answer as the in-process checker on the same output
                DMesh3 o = Copy();
                Vector3d p = o.GetVertex(v0);
                o.SetVertex(v0, new Vector3d(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p.x) + (p.x >= 0 ? 1 : -1)), p.y, p.z));
                BooleanCertificate inProc = BooleanCertificate.From(s.Boolean);
                inProc.Result = o;
                Assert.Equal(InProcess(inProc), Verdict(o));
                log.WriteLine("one ulp: '" + Verdict(o) + "'");
            }
        }

        /// <summary> the caller's inputs are part of the claim: other meshes than the boolean's are rejected </summary>
        [Fact]
        public void OtherInputs_AreRejected()
        {
            Setup s = Spheres();
            DMesh3 moved = new DMesh3(s.A, false, MeshComponents.None);
            moved.SetVertex(moved.VertexIndices().First(), moved.GetVertex(moved.VertexIndices().First()) + new Vector3d(0.01, 0, 0));
            Assert.NotEqual("", CertificateFile.Check(s.Bytes, new List<DMesh3> { moved, s.B }, s.Boolean.Result));
            DMesh3 fewer = new DMesh3(s.A, false, MeshComponents.None);
            fewer.RemoveTriangle(fewer.TriangleIndices().First(), false, false);
            Assert.NotEqual("", CertificateFile.Check(s.Bytes, new List<DMesh3> { fewer, s.B }, s.Boolean.Result));
            Assert.StartsWith("malformed", CertificateFile.Check(s.Bytes, new List<DMesh3> { s.A }, s.Boolean.Result));
        }


        // ---------------------------------------------------------------- forged bounds (review of #1527)

        static DMesh3 WrongOutput(Setup s)
        {
            DMesh3 o = new DMesh3(s.Boolean.Result, false, MeshComponents.None);
            int v = o.VertexIndices().First();
            o.SetVertex(v, o.GetVertex(v) + new Vector3d(0, 0, 3e-3));           // far above the coverage distance
            return o;
        }

        /// <summary>
        /// E1: a vertex of the result before rounding that no triangle uses, at 1e300, would stretch the box the bound is derived
        /// from (the bound is a fraction of its diagonal) and with it the coverage distance: the reader refuses such a file, and the
        /// bound is taken over the vertices that triangles use only.
        /// </summary>
        [Fact]
        public void E1_AnUnreferencedVertexAtOneE300_DoesNotLoosenTheBound()
        {
            Setup s = Spheres();
            DMesh3 wrong = WrongOutput(s);
            Assert.NotEqual("", CertificateFile.Check(s.Bytes, s.Inputs, wrong));
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, wrong);
            int v = c.Assembled.AppendVertex(new Vector3d(1e300, 0, 0));
            var ex = c.AssembledExact.ToList();
            while (ex.Count <= v) ex.Add(default(ExactPoint3));
            ex[v] = ExactPoint3.FromInteger(new System.Numerics.BigInteger(1e300), 0, 0);
            c.AssembledExact = ex.ToArray();
            c.SetRounding(true, 0.9, 0);                                         // the claim the loose bound would admit
            string r = CertificateFile.Check(CertificateFile.Write(c, forge: true), s.Inputs, wrong);
            Assert.StartsWith("malformed: a vertex of the result before rounding that no triangle uses", r);
            // the in-memory checker, which has no file to refuse: the unreferenced vertex does not count either
            Assert.True(c.MaxLength > c.MaxLengthBound, $"{c.MaxLength} {c.MaxLengthBound}");
        }

        /// <summary>
        /// E1b: the output has a vertex that no triangle uses, at 1e5; the file claims a rounding length of 0.9. Over all vertices
        /// the bound would be about 1 and admit it, the wrong output (a vertex moved by 3e-3) would be covered. Over the vertices
        /// that triangles use, the bound stays at about 2.5e-5 and the claim is refused.
        /// </summary>
        [Fact]
        public void E1b_AnUnreferencedVertexOfTheOutput_DoesNotLoosenTheBound()
        {
            Setup s = Spheres();
            DMesh3 wrong = WrongOutput(s);
            wrong.AppendVertex(new Vector3d(1e5, 0, 0));
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, wrong);
            c.SetRounding(true, 0.9, 0);
            string r = CertificateFile.Check(CertificateFile.Write(c), s.Inputs, wrong);
            Assert.Contains("above the documented", r);
            // and the honest certificate still holds for an output with such a vertex
            DMesh3 honest = new DMesh3(s.Boolean.Result, false, MeshComponents.None);
            honest.AppendVertex(new Vector3d(1e5, 0, 0));
            Assert.Equal("", CertificateFile.Check(s.Bytes, s.Inputs, honest));
        }

        /// <summary>
        /// E2: the rounding settings that the bound depends on are the caller's (library defaults), not the file's: a certificate
        /// whose in-memory settings are loose writes the same bytes, and is judged with the defaults; settings that are not finite
        /// numbers of at least 0 are refused.
        /// </summary>
        [Fact]
        public void E2_TheFileCannotLoosenItsOwnSettings()
        {
            Setup s = Spheres();
            DMesh3 wrong = WrongOutput(s);
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, wrong);
            c.SettingAbsoluteTolerance = 1; c.SettingMaxDisplacement = 1; c.SettingUlpFactor = 1e12;
            c.SetRounding(true, 0.9, 0);
            byte[] forged = CertificateFile.Write(c);
            string r = CertificateFile.Check(forged, s.Inputs, wrong);
            Assert.Contains("above the documented", r);
            Assert.Equal(s.Bytes.Length, forged.Length);
            // what the caller states is used (and shown through Delta)
            string loose = CertificateFile.Check(forged, s.Inputs, wrong, new CertificateFile.RoundingSettings { AbsoluteTolerance = 1 }, out double delta);
            Assert.True(delta >= 0.9, delta.ToString());
            foreach (var bad in new[] { new CertificateFile.RoundingSettings { AbsoluteTolerance = double.NaN }, new CertificateFile.RoundingSettings { MaxDisplacement = -1 },
                new CertificateFile.RoundingSettings { UlpFactor = double.PositiveInfinity } })
                Assert.StartsWith("malformed: rounding settings", CertificateFile.Check(s.Bytes, s.Inputs, s.Boolean.Result, bad));
            Assert.Equal("", CertificateFile.Check(s.Bytes, s.Inputs, s.Boolean.Result, new CertificateFile.RoundingSettings()));
            log.WriteLine("with the caller's loose settings: '" + loose + "'");
        }

        // ---------------------------------------------------------------- a reader that cannot be made to allocate

        static byte[] Splice(byte[] b, int at, int removeBytes, byte[] insert) => b.Take(at).Concat(insert).Concat(b.Skip(at + removeBytes)).ToArray();

        static byte[] Varint(ulong v) { var l = new List<byte>(); while (v >= 0x80) { l.Add((byte)(v | 0x80)); v >>= 7; } l.Add((byte)v); return l.ToArray(); }

        // the length of the varint at b[at]
        static int VarintLength(byte[] b, int at) { int n = 1; while ((b[at + n - 1] & 0x80) != 0) n++; return n; }

        static long Allocated() =>
#if NETCOREAPP
            GC.GetAllocatedBytesForCurrentThread();
#else
            0;
#endif

        /// <summary>
        /// A count that claims 2^31 elements, followed by the end of the bytes: every list of the format is refused by the bytes
        /// left (at least one byte per element, more for the larger ones), with FormatException and no allocation worth the
        /// name. Read, not Check: Check would hide any other exception.
        /// </summary>
        [Fact]
        public void HugeCounts_ThenTruncation_AreRefusedWithoutAllocating()
        {
            Setup s = Spheres(Ops.Union, 3);
            CertificateFile.Write(s.Cert);
            var sections = CertificateFile.LastSections;
            int refused = 0;
            foreach (var (name, offset) in sections) {
                int len = VarintLength(s.Bytes, offset);
                byte[] huge = Varint((1UL << 31) - 1);
                byte[] data = Splice(s.Bytes, offset, len, huge).Take(offset + huge.Length + 3).ToArray();
                long before = Allocated();
                Assert.Throws<FormatException>(() => CertificateFile.Read(data, s.Inputs, s.Boolean.Result));
                long used = Allocated() - before;
#if NETCOREAPP
                Assert.True(used < 2_000_000, $"{name}: {used} bytes allocated");
#endif
                refused++;
            }
            Assert.True(refused >= 4, refused.ToString());
        }

        /// <summary>
        /// The piece count claims half of the bytes that follow it (a piece takes at least six): the reader refuses it before it
        /// allocates the arrays of the pieces (about 26 bytes per piece), so its allocation does not grow with the claim. With a
        /// bound of one byte per element the arrays would take about 13 times the rest of the file.
        /// </summary>
        [Fact]
        public void APieceCountOfHalfTheRemainingBytes_IsRefusedBeforeTheArraysAreAllocated()
        {
            Setup s = Spheres();
            CertificateFile.Write(s.Cert);
            int at = CertificateFile.LastSections.First(x => x.section == "pieces").offset;
            int len = VarintLength(s.Bytes, at);
            int remaining = s.Bytes.Length - at - len;
            byte[] claim = Splice(s.Bytes, at, len, Varint((ulong)(remaining / 2)));
            byte[] baseline = s.Bytes.Take(at).ToArray();                        // the same read up to the piece count
            long b0 = Allocated();
            Assert.Throws<FormatException>(() => CertificateFile.Read(baseline, s.Inputs, s.Boolean.Result));
            long baseAlloc = Allocated() - b0;
            long c0 = Allocated();
            Assert.Throws<FormatException>(() => CertificateFile.Read(claim, s.Inputs, s.Boolean.Result));
            long claimAlloc = Allocated() - c0;
            log.WriteLine($"{remaining} bytes after the count: baseline {baseAlloc}, with the claim {claimAlloc}");
#if NETCOREAPP
            Assert.True(claimAlloc - baseAlloc < remaining, $"{claimAlloc - baseAlloc} bytes for a claim over {remaining} bytes");
#endif
        }

        [Fact]
        public void PaddedVarints_AndOutOfRangeNumbers_AreRefused()
        {
            Setup s = Spheres();
            // the version as a padded varint (0x81 0x00 for 1)
            Assert.StartsWith("malformed: non-minimal varint", CertificateFile.Check(Splice(s.Bytes, 4, 1, new byte[] { 0x81, 0x00 }), s.Inputs, s.Boolean.Result));
            // a count as a padded varint: the triangle list's
            CertificateFile.Write(s.Cert);
            int at = CertificateFile.LastSections.First(x => x.section == "triangles").offset;
            byte[] padded = Varint((ulong)s.Cert.TriMesh.Length);
            padded[padded.Length - 1] |= 0x80;
            padded = padded.Concat(new byte[] { 0x00 }).ToArray();
            Assert.StartsWith("malformed: non-minimal varint", CertificateFile.Check(Splice(s.Bytes, at, VarintLength(s.Bytes, at), padded), s.Inputs, s.Boolean.Result));
            // a grid scale and a winding number out of range
            BooleanCertificate c = CertificateFile.Read(s.Bytes, s.Inputs, s.Boolean.Result);
            c.ScaleExp = 5000;
            Assert.StartsWith("malformed: a grid scale out of range", CertificateFile.Check(CertificateFile.Write(c), s.Inputs, s.Boolean.Result));
            // a point table entry that no piece uses
            byte[] extra = CertificateFile.Write(s.Cert, extraPoints: new[] { ExactPoint3.FromInteger(7, 7, 7) });
            Assert.StartsWith("malformed: a point that no piece uses", CertificateFile.Check(extra, s.Inputs, s.Boolean.Result));
            Assert.True(extra.Length > s.Bytes.Length);
        }

        // ---------------------------------------------------------------- the file as a format

        [Fact]
        public void Format_IsVersionedAndStrict()
        {
            Setup s = Spheres();
            Assert.Equal(new byte[] { (byte)'G', (byte)'3', (byte)'B', (byte)'C', 2, 1 }, s.Bytes.Take(6).ToArray());
            string V(byte[] b) => CertificateFile.Check(b, s.Inputs, s.Boolean.Result);
            Assert.StartsWith("malformed", V(new byte[0]));
            Assert.StartsWith("malformed", V(s.Bytes.Take(s.Bytes.Length - 1).ToArray()));
            Assert.StartsWith("malformed", V(s.Bytes.Concat(new byte[] { 0 }).ToArray()));
            // version 1 (RegionClipping = false, flags <= 1) remains readable for backward compatibility; flags = 2 is rejected in v1
            var v1 = (byte[])s.Bytes.Clone(); v1[4] = 1;
            Assert.Equal("", V(v1));
            Assert.False(CertificateFile.Read(v1, s.Inputs, s.Boolean.Result).RegionClipping);
            var v1BadFlag = (byte[])v1.Clone(); v1BadFlag[8] |= 2;
            Assert.StartsWith("malformed: a flag out of range", V(v1BadFlag));
            var v2BadFlag = (byte[])s.Bytes.Clone(); v2BadFlag[8] = 4;
            Assert.StartsWith("malformed: a flag out of range", V(v2BadFlag));
            var v3 = (byte[])s.Bytes.Clone(); v3[4] = 3;
            Assert.StartsWith("malformed: an unknown version", V(v3));
            var f2 = (byte[])s.Bytes.Clone(); f2[5] = 2;
            Assert.StartsWith("malformed: an unknown format", V(f2));
            var m = (byte[])s.Bytes.Clone(); m[0] = (byte)'X';
            Assert.StartsWith("malformed: not a certificate", V(m));
            // no floating-point text: the file has no digit strings of a number format (all integers or raw bits), so it holds no '.'-separated numbers; checked by its size not growing with the locale
            foreach (string culture in new[] { "de-DE", "en-US" }) {
                var old = System.Globalization.CultureInfo.CurrentCulture;
                try {
                    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                    Assert.Equal(s.Bytes, CertificateFile.Write(s.Cert));
                } finally { System.Globalization.CultureInfo.CurrentCulture = old; }
            }
            // a point that is not reduced, a point with W <= 0 and a repeated integer are not canonical: the reader refuses them
            Assert.StartsWith("malformed: a point that is not reduced", Tampered(s, c => {
                ExactPoint3 p = c.PieceCorners[0][0];
                c.PieceCorners[0][0] = ExactPoint3.CreateUnreduced(p.X * 2, p.Y * 2, p.Z * 2, p.W * 2);
            }));
            // a custom predicate and an open input have no certificate
            var custom = BooleanCertificate.From(s.Boolean); custom.Operation = null;
            Assert.Throws<NotSupportedException>(() => CertificateFile.Write(custom));
        }

        // ---------------------------------------------------------------- the exact frames

        /// <summary>
        /// Over Similarity.All(): every frame certifies through the file, and the frames that are a power of two scale give the
        /// same certificate: the same pieces on the grid (exact points), labels, selection and check counters, and the same
        /// number of bytes in the pieces' part (the grid's scale is the only header difference).
        /// </summary>
        [Theory, MemberData(nameof(Similarity.NamesExceptExtremeFrame), MemberType = typeof(Similarity))]
        public void Frames_GiveTheSameCertificate_ForPowerOfTwoScales(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 a0 = Sphere(Vector3d.Zero), b0 = Sphere(new Vector3d(0.5, 0.25, 0.125));
            DMesh3 Map(DMesh3 m) { var r = new DMesh3(m); foreach (int v in r.VertexIndices()) r.SetVertex(v, sim.Apply(r.GetVertex(v))); return r; }
            DMesh3 a = Map(a0), b = Map(b0);
            foreach (Ops op in AllOps) {
                BooleanCertificate c = Run(a, b, op, out ExactMeshBoolean eb);
                byte[] bytes = CertificateFile.Write(c);
                string r = CertificateFile.Check(bytes, new List<DMesh3> { a, b }, eb.Result);
                Assert.True(r == "", $"{name} {op}: {r}");
                if (sim.IsExact && sim != Similarity.Identity) {
                    BooleanCertificate c0 = Run(a0, b0, op, out ExactMeshBoolean eb0);
                    byte[] bytes0 = CertificateFile.Write(c0);
                    BooleanCertificate r0 = CertificateFile.Read(bytes0, new List<DMesh3> { a0, b0 }, eb0.Result);
                    BooleanCertificate r1 = CertificateFile.Read(bytes, new List<DMesh3> { a, b }, eb.Result);
                    Assert.Equal(r0.PieceCorners.Length, r1.PieceCorners.Length);
                    for (int i = 0; i < r0.PieceCorners.Length; ++i) Assert.Equal(r0.PieceCorners[i], r1.PieceCorners[i]);
                    Assert.Equal(r0.PieceSource, r1.PieceSource); Assert.Equal(r0.PieceMesh, r1.PieceMesh);
                    Assert.Equal(r0.Keep, r1.Keep); Assert.Equal(r0.Flip, r1.Flip);
                    for (int i = 0; i < r0.Front.Length; ++i) Assert.Equal(r0.Front[i], r1.Front[i]);
                    Assert.Equal(r0.TriCulled, r1.TriCulled);
                }
            }
        }

        // ---------------------------------------------------------------- the checker does not use the boolean

        /// <summary>
        /// The stand-alone checker is built from files that never name the boolean's arrangement or its Compute: the code lines
        /// (comments and string literals aside) of BooleanCertificate.cs and CertificateFile.cs; the one use of ExactMeshBoolean
        /// is the alias of its operations enum. Only BooleanCertificate.Recording.cs may.
        /// </summary>
        [Fact]
        public void StandAloneChecker_DoesNotReferenceTheBooleanOrItsArrangement()
        {
            string dir = Path.Combine(Path.GetDirectoryName(SourcePath()), "Harness");
            foreach (string file in new[] { "BooleanCertificate.cs", "CertificateFile.cs" }) {
                string[] lines = File.ReadAllLines(Path.Combine(dir, file));
                for (int i = 0; i < lines.Length; ++i) {
                    string t = lines[i].Trim();
                    if (t.StartsWith("//")) continue;
                    int comment = t.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) t = t.Substring(0, comment);
                    Assert.False(t.Contains("ExactMeshArrangement") || t.Contains("Arrangement.") || t.Contains(".Compute(") || t.Contains("new ExactMeshBoolean"),
                        $"{file}:{i + 1}: {t}");
                    if (t.Contains("ExactMeshBoolean")) Assert.True(t == "using Ops = g3.ExactMeshBoolean.Operations;", $"{file}:{i + 1}: {t}");
                }
            }
            Assert.Contains("ExactMeshArrangement", File.ReadAllText(Path.Combine(dir, "BooleanCertificate.Recording.cs")));
        }

        static string SourcePath([CallerFilePath] string path = "") => path;
    }
}
