using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Step B of docs/research/CERTIFYING_BOOLEANS_2026-11.md: the certificate of an ExactMeshBoolean as bytes, and a checker
    /// that reads (input meshes, output mesh, bytes) and decides C0-C5 with the checker of BooleanCertificate, without the
    /// boolean or its arrangement (this file and BooleanCertificate.cs do not name them; BooleanCertificate.Recording.cs is the
    /// only part that does, and CertificateFileTests checks that by reading the sources).
    ///
    /// Format R (the rational corners the checker reads), version 1. Little-endian, deterministic, no floating-point text: every
    /// number is an integer (varints, zigzag for signed ones, BigInteger as a length and its two's complement bytes) or the 64
    /// raw bits of a double. Layout: magic "G3BC", version, format; the operation (built-in only: a custom winding predicate has
    /// no file format); the culling mesh; whether the result was rounded, MaxLength and the removed components (claims: MaxLength
    /// is checked against a bound the reader derives from the output and the caller's rounding settings, which are not in the file); the grid (ScaleExp, Center); the triangles reversed by the boolean (the
    /// reader rebuilds the arrangement's meshes from the caller's); per input triangle its mesh, id and culled flag; the points
    /// (X, Y, Z, W, each used once or more); the pieces (mesh, source, three point ids); the labels (front winding numbers, keep and flip);
    /// the result before rounding (vertices as exact points, triangles; the double positions are derived). The output mesh is not in the file: the
    /// caller gives it.
    /// </summary>
    internal static class CertificateFile
    {
        /// <summary>
        /// The rounding settings the caller used (ExactMeshBoolean.RoundingMaxDisplacement, RoundingUlpFactor,
        /// RoundingAbsoluteTolerance, StrictRounding), with the library's defaults. They decide the bound that MaxLength is
        /// checked against, so they come from the caller and not from the file: a certificate cannot loosen its own bound.
        /// </summary>
        internal sealed class RoundingSettings
        {
            public double MaxDisplacement = 1e-5, UlpFactor = 256, AbsoluteTolerance = 0;
            public bool Strict;
            /// <summary> "" or why the settings are refused (not finite, or negative) </summary>
            public string Validate()
            {
                foreach (double v in new[] { MaxDisplacement, UlpFactor, AbsoluteTolerance })
                    if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) return "rounding settings that are not finite numbers of at least 0";
                return "";
            }
        }

        public const int Version = 1;
        static readonly byte[] Magic = { (byte)'G', (byte)'3', (byte)'B', (byte)'C' };

        // ------------------------------------------------------------------ writer

        sealed class W
        {
            readonly MemoryStream ms = new MemoryStream();
            public void Byte(int b) { ms.WriteByte((byte)b); }
            public void U(ulong v) { while (v >= 0x80) { ms.WriteByte((byte)(v | 0x80)); v >>= 7; } ms.WriteByte((byte)v); }
            public void I(long v) { U((ulong)((v << 1) ^ (v >> 63))); }
            public void Bits(double d) { long b = BitConverter.DoubleToInt64Bits(d); for (int i = 0; i < 8; ++i) ms.WriteByte((byte)(b >> (8 * i))); }
            public void Big(BigInteger v) { byte[] b = v.ToByteArray(); U((ulong)b.Length); ms.Write(b, 0, b.Length); }
            public void Point(ExactPoint3 p) { Big(p.X); Big(p.Y); Big(p.Z); Big(p.W); }
            public byte[] ToArray() => ms.ToArray();
            public int Position => (int)ms.Position;
        }

        /// <summary> for tests and reports: where each section of the last Write started </summary>
        [ThreadStatic] public static List<(string section, int offset)> LastSections;

        /// <summary> the certificate of c in format R; c must come from a built-in operation and not be not-applicable </summary>

        /// <summary>
        /// forge (tests only): also writes the vertices of the result before rounding that no triangle uses, and the extra points,
        /// as a hand-made file would; the reader must refuse both
        /// </summary>
        public static byte[] Write(BooleanCertificate c, bool forge = false, IList<ExactPoint3> extraPoints = null)
        {
            var sections = LastSections = new List<(string, int)>();
            if (c.NotApplicableReason != null) throw new NotSupportedException("no certificate: " + c.NotApplicableReason);
            if (c.Operation == null) throw new NotSupportedException("a custom winding predicate has no file format");
            var w = new W();
            foreach (byte b in Magic) w.Byte(b);
            w.U(Version); w.U(1);                                           // format R
            w.U((ulong)(int)c.Operation.Value);
            w.I(c.CullMesh);
            w.Byte(c.Rounded ? 1 : 0);
            w.Bits(c.MaxLength); w.U((ulong)c.RemovedComponents);
            w.I(c.ScaleExp); foreach (BigInteger x in c.Center) w.Big(x);
            // the triangles the boolean reversed: by comparing corner positions with the caller's meshes
            var reversed = new List<(int m, int t)>();
            for (int m = 0; m < c.Meshes.Count; ++m)
                foreach (int t in c.Inputs[m].TriangleIndices()) {
                    Index3i a = c.Inputs[m].GetTriangle(t), b = c.Meshes[m].GetTriangle(t);
                    Vector3d[] pa = { c.Inputs[m].GetVertex(a.a), c.Inputs[m].GetVertex(a.b), c.Inputs[m].GetVertex(a.c) };
                    Vector3d[] pb = { c.Meshes[m].GetVertex(b.a), c.Meshes[m].GetVertex(b.b), c.Meshes[m].GetVertex(b.c) };
                    if (pa[0] == pb[0] && pa[1] == pb[1] && pa[2] == pb[2]) continue;
                    if (pa[0] == pb[1] && pa[1] == pb[0] && pa[2] == pb[2]) reversed.Add((m, t));       // DMesh3.ReverseTriOrientation: corners 0 and 1 swapped
                    else throw new NotSupportedException("a triangle that is neither kept nor reversed");
                }
            sections.Add(("reversed", w.Position));
            w.U((ulong)c.Meshes.Count);
            w.U((ulong)reversed.Count); foreach (var (m, t) in reversed) { w.U((ulong)m); w.U((ulong)t); }
            w.U((ulong)c.ReorientedTriangles);
            sections.Add(("triangles", w.Position));
            w.U((ulong)c.TriMesh.Length);
            for (int t = 0; t < c.TriMesh.Length; ++t) { w.U((ulong)c.TriMesh[t]); w.U((ulong)c.TriTid[t]); w.Byte(c.TriCulled[t] ? 1 : 0); }
            var ids = new Dictionary<ExactPoint3, int>(); var points = new List<ExactPoint3>();
            foreach (ExactPoint3[] piece in c.PieceCorners)
                foreach (ExactPoint3 p in piece)
                    if (ids.ContainsKey(p) == false) { ids[p] = points.Count; points.Add(p); }
            sections.Add(("points", w.Position));
            if (extraPoints != null) points.AddRange(extraPoints);
            w.U((ulong)points.Count); foreach (ExactPoint3 p in points) w.Point(p);
            int n = c.PieceCorners.Length;
            sections.Add(("pieces", w.Position));
            w.U((ulong)n);
            for (int i = 0; i < n; ++i) {
                w.U((ulong)c.PieceMesh[i]); w.U((ulong)c.PieceSource[i]);
                foreach (ExactPoint3 p in c.PieceCorners[i]) w.U((ulong)ids[p]);
                w.Byte((c.Keep[i] ? 1 : 0) | (c.Flip[i] ? 2 : 0) | (c.Front[i] != null ? 4 : 0));
                if (c.Front[i] != null) foreach (int f in c.Front[i]) w.I(f);
            }
            sections.Add(("result before rounding", w.Position));
            var vid = new Dictionary<int, int>();
            var referenced = new SortedSet<int>();
            foreach (int t in c.Assembled.TriangleIndices()) { Index3i tri = c.Assembled.GetTriangle(t); referenced.Add(tri.a); referenced.Add(tri.b); referenced.Add(tri.c); }
            if (forge) foreach (int v in c.Assembled.VertexIndices()) referenced.Add(v);
            w.U((ulong)referenced.Count);
            foreach (int v in referenced) {
                vid[v] = vid.Count;
                w.Point(c.AssembledExact[v]);
            }
            w.U((ulong)c.Assembled.TriangleCount);
            foreach (int t in c.Assembled.TriangleIndices()) {
                Index3i tri = c.Assembled.GetTriangle(t);
                w.U((ulong)vid[tri.a]); w.U((ulong)vid[tri.b]); w.U((ulong)vid[tri.c]);
            }
            return w.ToArray();
        }

        // ------------------------------------------------------------------ reader

        sealed class FormatException2 : Exception { public FormatException2(string m) : base(m) { } }

        sealed class R
        {
            readonly byte[] b; int pos;
            public R(byte[] bytes) { b = bytes; }
            public bool AtEnd => pos == b.Length;
            public int Byte() { if (pos >= b.Length) throw new FormatException2("truncated"); return b[pos++]; }
            public ulong U()
            {
                ulong v = 0;
                for (int shift = 0; ; shift += 7) {
                    if (shift > 63) throw new FormatException2("varint too long");
                    int x = Byte();
                    v |= (ulong)(x & 0x7F) << shift;
                    if ((x & 0x80) == 0) { if (x == 0 && shift > 0) throw new FormatException2("non-minimal varint"); return v; }
                }
            }
            // a count or an id that must fit the bytes left (every element takes at least one byte) or the given limit
            public int Count(long limit)
            {
                ulong v = U();
                if (v > (ulong)limit) throw new FormatException2("a count above its limit");
                return (int)v;
            }
            // a count of elements that take at least minBytes each: it cannot exceed what is left of the bytes
            public int Counted(int minBytes) => Count((b.Length - pos) / Math.Max(1, minBytes));
            public long I() { ulong v = U(); return (long)(v >> 1) ^ -(long)(v & 1); }
            public double Bits()
            {
                if (pos + 8 > b.Length) throw new FormatException2("truncated");
                long v = 0; for (int i = 0; i < 8; ++i) v |= (long)b[pos++] << (8 * i);
                return BitConverter.Int64BitsToDouble(v);
            }
            public BigInteger Big()
            {
                int len = Counted(1);
                if (len > 4096) throw new FormatException2("an integer of more than 32768 bits");
                if (len == 0) throw new FormatException2("an empty integer");
                var bytes = new byte[len]; Array.Copy(b, pos, bytes, 0, len); pos += len;
                var v = new BigInteger(bytes);
                if (v.ToByteArray().Length != len) throw new FormatException2("a non-minimal integer");
                return v;
            }
            public ExactPoint3 Point()
            {
                BigInteger x = Big(), y = Big(), z = Big(), w = Big();
                if (w.Sign <= 0) throw new FormatException2("a point with W <= 0");
                ExactPoint3 p = ExactPoint3.Create(x, y, z, w);
                if (p.X != x || p.Y != y || p.Z != z || p.W != w) throw new FormatException2("a point that is not reduced");
                return p;
            }
        }

        /// <summary>
        /// The certificate of bytes for the caller's meshes and the output mesh (not copied: the checker reads it). Throws
        /// FormatException on bytes that are not a certificate of this version.
        /// </summary>
        public static BooleanCertificate Read(byte[] bytes, IList<DMesh3> inputs, DMesh3 output, RoundingSettings settings = null)
        {
            try { return Parse(bytes, inputs, output, settings ?? new RoundingSettings()); }
            catch (FormatException2 e) { throw new FormatException(e.Message); }
            catch (Exception e) when (e is ArgumentException || e is IndexOutOfRangeException || e is OverflowException || e is InvalidOperationException || e is DivideByZeroException)
            { throw new FormatException("malformed certificate: " + e.Message); }
        }

        static BooleanCertificate Parse(byte[] bytes, IList<DMesh3> inputs, DMesh3 output, RoundingSettings settings)
        {
            string bad = settings.Validate();
            if (bad != "") throw new FormatException2(bad);
            var r = new R(bytes);
            foreach (byte m in Magic) if (r.Byte() != m) throw new FormatException2("not a certificate (magic)");
            if (r.U() != Version) throw new FormatException2("an unknown version");
            if (r.U() != 1) throw new FormatException2("an unknown format");
            ulong op = r.U();
            if (op > 2 || Enum.IsDefined(typeof(Ops), (int)op) == false) throw new FormatException2("an unknown operation");
            var c = new BooleanCertificate { Operation = (Ops)(int)op };
            c.Predicate = BooleanCertificate.OperationPredicate(c.Operation.Value);
            long cull = r.I();
            if (cull < -1 || cull >= inputs.Count) throw new FormatException2("a culling mesh out of range");
            c.CullMesh = (int)cull;
            int rounded = r.Byte();
            if (rounded > 1) throw new FormatException2("a flag out of range");
            c.SettingStrict = settings.Strict; c.SettingMaxDisplacement = settings.MaxDisplacement;
            c.SettingUlpFactor = settings.UlpFactor; c.SettingAbsoluteTolerance = settings.AbsoluteTolerance;
            double maxLength = r.Bits();
            int removed = r.Count(int.MaxValue);
            if (double.IsNaN(maxLength) || double.IsInfinity(maxLength) || maxLength < 0) throw new FormatException2("a rounding length that is not a finite number of at least 0");
            long scaleExp = r.I();
            if (scaleExp < -2200 || scaleExp > 2200) throw new FormatException2("a grid scale out of range");
            c.ScaleExp = (int)scaleExp;
            c.Center = new[] { r.Big(), r.Big(), r.Big() };
            int meshCount = r.Count(inputs.Count);
            if (meshCount != inputs.Count) throw new FormatException2("a different number of meshes");
            // the meshes of the arrangement: the caller's, with the listed triangles reversed
            c.Inputs = inputs.ToList();
            c.Meshes = inputs.Select(m => new DMesh3(m, false, MeshComponents.None)).ToList();
            int nrev = r.Counted(2);
            var seenRev = new HashSet<(int, int)>();
            for (int i = 0; i < nrev; ++i) {
                int m = r.Count(inputs.Count - 1), t = r.Count(int.MaxValue);
                if (c.Meshes[m].IsTriangle(t) == false || seenRev.Add((m, t)) == false) throw new FormatException2("a reversed triangle that does not exist or is listed twice");
                c.Meshes[m].ReverseTriOrientation(t);
            }
            c.ReorientedTriangles = r.Count(int.MaxValue);
            int nt = r.Counted(3);
            c.TriMesh = new int[nt]; c.TriTid = new int[nt]; c.TriCulled = new bool[nt];
            for (int t = 0; t < nt; ++t) {
                c.TriMesh[t] = r.Count(inputs.Count - 1); c.TriTid[t] = r.Count(int.MaxValue);
                if (c.Meshes[c.TriMesh[t]].IsTriangle(c.TriTid[t]) == false) throw new FormatException2("an input triangle that does not exist");
                int cu = r.Byte(); if (cu > 1) throw new FormatException2("a flag out of range");
                c.TriCulled[t] = cu == 1;
            }
            int np = r.Counted(8);
            var pointUsed = new bool[np];
            var points = new ExactPoint3[np];
            for (int i = 0; i < np; ++i) points[i] = r.Point();
            int n = r.Counted(6);
            c.PieceMesh = new int[n]; c.PieceSource = new int[n]; c.PieceCorners = new ExactPoint3[n][];
            c.Front = new int[n][]; c.Keep = new bool[n]; c.Flip = new bool[n];
            for (int i = 0; i < n; ++i) {
                c.PieceMesh[i] = r.Count(inputs.Count - 1); c.PieceSource[i] = r.Count(Math.Max(0, nt - 1));
                int[] pid = { r.Count(np - 1), r.Count(np - 1), r.Count(np - 1) };
                foreach (int k in pid) pointUsed[k] = true;
                c.PieceCorners[i] = new[] { points[pid[0]], points[pid[1]], points[pid[2]] };
                int flags = r.Byte();
                if (flags > 7) throw new FormatException2("flags out of range");
                c.Keep[i] = (flags & 1) != 0; c.Flip[i] = (flags & 2) != 0;
                if ((flags & 4) != 0) {
                    c.Front[i] = new int[meshCount];
                    for (int k = 0; k < meshCount; ++k) {
                        long f = r.I();
                        if (f < int.MinValue || f > int.MaxValue) throw new FormatException2("a winding number out of range");
                        c.Front[i][k] = (int)f;
                    }
                }
            }
            if (pointUsed.Contains(false)) throw new FormatException2("a point that no piece uses");
            var assembled = new DMesh3();
            int nv = r.Counted(8);
            c.AssembledExact = new ExactPoint3[nv];
            for (int i = 0; i < nv; ++i) {
                // (the double position is derived from the exact point: within an ulp of it, and what the checker's boxes and Delta need)
                c.AssembledExact[i] = r.Point();
                assembled.AppendVertex(c.AssembledExact[i].ToVector3d(0));
            }
            int ntri = r.Counted(3);
            var vertexUsed = new bool[nv];
            for (int i = 0; i < ntri; ++i) {
                int va = r.Count(nv - 1), vb = r.Count(nv - 1), vc = r.Count(nv - 1);
                vertexUsed[va] = vertexUsed[vb] = vertexUsed[vc] = true;
                if (assembled.AppendTriangle(va, vb, vc) < 0) throw new FormatException2("a result-before-rounding triangle the mesh cannot hold");
            }
            if (vertexUsed.Contains(false)) throw new FormatException2("a vertex of the result before rounding that no triangle uses");
            if (r.AtEnd == false) throw new FormatException2("bytes after the certificate");
            c.Assembled = assembled;
            c.Result = output;
            c.SetRounding(rounded == 1, maxLength, removed);
            return c;
        }

        /// <summary> the verdict of the stand-alone checker: "" if the bytes certify output as the result for inputs (C0-C5, nothing removed, everything covered), else the reason </summary>
        public static string Check(byte[] bytes, IList<DMesh3> inputs, DMesh3 output, RoundingSettings settings = null) => Check(bytes, inputs, output, settings, out _);

        /// <summary> as Check, and the coverage distance Delta the verdict was decided with (NaN where the bytes were not read) </summary>
        public static string Check(byte[] bytes, IList<DMesh3> inputs, DMesh3 output, RoundingSettings settings, out double delta)
        {
            BooleanCertificate c;
            delta = double.NaN;
            try { c = Read(bytes, inputs, output, settings); }
            catch (FormatException e) { return "malformed: " + e.Message; }
            string r;
            delta = c.Delta;
            try { r = c.Check(); }
            catch (Exception e) when (e is OutOfMemoryException == false && e is StackOverflowException == false)
            { return "rejected: the checker could not evaluate the certificate (" + e.GetType().Name + ": " + e.Message + ")"; }
            if (r != "") return r;
            return c.Uncovered == 0 ? "" : $"{c.Uncovered} triangles not covered within Delta";
        }
    }
}
