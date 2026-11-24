using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A test-only certifying checker for ExactMeshBoolean (docs/research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md, direction
    /// 2, stages 1-2; docs/QA.md "Certifying checker of the exact boolean"). It takes what the boolean recorded
    /// (ExactMeshBoolean.RecordCertificate: the winding numbers in front of each classified piece, keep and flip, the
    /// result before the rounding) as a witness and checks it against the input meshes with its own exact tests, without
    /// the classification code it checks:
    ///
    /// - C0 inputs: the arrangement's meshes are the caller's (reversed triangles as many as ReorientedTriangles), each
    ///   non-degenerate input triangle is in the arrangement once, every culled triangle meets no triangle of the
    ///   culling mesh and lies outside it (an exact ray per connected set), and the rounding's bound is at most the
    ///   documented one from the boolean's settings.
    /// - C1 pieces: every arrangement piece is a non-degenerate triangle in the plane of its source triangle, inside it,
    ///   oriented like it; the pieces of a source tile it (the directed edges pair up inside, the unpaired ones run along
    ///   the source's edges in its direction, and the projected areas add up exactly to the source's: a positive chain
    ///   whose boundary lies on the source's boundary covers it a constant number of times, the area makes that once).
    /// - C3 classification: the stack of each piece (the coplanar input triangles that contain its centroid) is found from
    ///   the input triangles grouped by exact plane; a piece is classified iff its source is the stack's first triangle.
    ///   The back of a piece has the front's winding numbers plus +1 per stack triangle oriented like it and -1 per
    ///   opposite one (of that triangle's mesh). Around every edge of the classified pieces the pieces are sorted
    ///   radially (exact orientation tests); consecutive pieces must give the wedge between them the same winding
    ///   numbers. Per connected set of pieces one anchor: the winding numbers on one side of one piece by an exact ray
    ///   against all input triangles (signed crossings), equal to the witness. Locally consistent values with one right
    ///   value per connected set are the winding numbers (the jumps and the wedges fix all others).
    /// - C4 selection: a classified piece is kept iff the operation's predicate differs on its two sides, flipped iff its
    ///   front is inside; no other piece is kept.
    /// - C5 result: the result before rounding (E_s, with its exact vertices) is the kept pieces (E) as an oriented point
    ///   set: per exact plane, the boundary of E minus E_s is zero (directed edges per supporting line, swept as signed
    ///   intervals), and a bounded plane chain with zero boundary is zero; the unsigned areas per plane are equal too (E
    ///   covers a point at most once, so a two-sided sheet in E_s, which the chain cancels, shows there). The rounded result R is closed and oriented
    ///   (every directed edge once, with its reverse once), and R and E_s lie within delta of each other (Delta: the
    ///   rounding's bound MaxLength plus a few ulps of the coordinates): a triangle is within delta of a convex triangle
    ///   iff its corners are (the distance to a convex set is convex), checked with exact squared distances, the
    ///   triangle split into four up to SplitDepth times where no single triangle covers it. A corner farther than delta
    ///   from all of the other mesh is a violation; a triangle not covered after the splits is only counted (Uncovered).
    ///
    /// Class (the note's section 3.3): closed inputs (exact winding numbers) only; open inputs are NotApplicable. The
    /// early-out of Difference and Intersection (culled triangles of the other meshes outside a closed mesh c) leaves
    /// sheets out where the predicate is false whatever the other winding numbers are: there the entries of the other
    /// meshes are compared only where w_c > 0 (on both sides of the wedge), and every connected set of pieces under those
    /// comparisons that has such a side gets its own anchor. Culling against several meshes (Intersection of closed
    /// meshes) is NotApplicable: the rule would make the conditions circular (run it with CullOutsideA = false). The
    /// embedding of the arrangement (C2: no two pieces cross) is not checked (stage 3); C3 trusts it, and an anchor ray
    /// that meets a non-coplanar triangle at the centroid reports it.
    ///
    /// Cost: linear in the pieces for C1, C3 and C4 (hash maps of edges and points, a sort per edge), plus one ray per
    /// anchor (linear in the input triangles each), the coplanar stacks quadratic per plane (fine for the procedural test
    /// meshes; a large flat region of many triangles would want a 2D grid), the culled triangles against the culling
    /// mesh's (with a box test; quadratic in the worst case), and C5 near-linear by a uniform grid. All signs are decided
    /// with BigInteger arithmetic on the exact points (the culled triangles by the library's exact ExactTriangleIntersection
    /// on the input doubles), so it costs several times the boolean: a test oracle, not a mode for users.
    /// </summary>
    internal sealed class BooleanCertificate
    {
        // ------------------------------------------------------------------ the data checked (copies: tests mutate them)

        /// <summary> per input mesh (as the boolean used it: ExactMeshArrangement.Meshes, after reorientation) </summary>
        public List<DMesh3> Meshes;
        public int ScaleExp;
        public BigInteger[] Center;
        /// <summary> per arrangement piece: mesh, source input triangle (an index into the input triangles), exact corners (grid) </summary>
        public int[] PieceMesh, PieceSource;
        public ExactPoint3[][] PieceCorners;
        /// <summary> per input triangle (arrangement order): mesh and triangle id, and whether the early-out culled it </summary>
        public int[] TriMesh, TriTid;
        public bool[] TriCulled;
        /// <summary> the witness: front winding numbers per classified piece (null otherwise), keep, flip </summary>
        public int[][] Front;
        public bool[] Keep, Flip;
        /// <summary> the predicate of the result's solid on the winding numbers </summary>
        public Func<int[], bool> Predicate;
        /// <summary> the mesh culled against (-1: none), and whether that is the only one </summary>
        public int CullMesh = -1;
        public string NotApplicableReason;
        /// <summary> the result before rounding with exact vertices (input coordinates), and the result </summary>
        public DMesh3 Assembled, Result;
        public ExactPoint3[] AssembledExact;
        /// <summary> the caller's input meshes (null: not checked against arrangement's), and how many triangles were reoriented </summary>
        public List<DMesh3> Inputs;
        public int ReorientedTriangles;
        /// <summary>
        /// the coverage distance: the rounding's bound (MeshRoundingRepair.MaxLength) plus a few ulps; checked against
        /// the documented bound from the boolean's settings (MaxLengthBound)
        /// </summary>
        public double Delta, MaxLength, MaxLengthBound;
        /// <summary> components the rounding removed (tiny or thin): the coverage of the result before rounding by the result is skipped then </summary>
        public int RemovedComponents;

        /// <summary> sub-triangle splits tried where no single triangle covers one (C5) </summary>
        public int SplitDepth = 3;

        // ------------------------------------------------------------------ results of the last Check

        public int Anchors, CoverageTests, Uncovered, CulledSets;
        /// <summary> components of the result before rounding exempt from its coverage by the result (thin or tiny, removed by the rounding) </summary>
        public int ExemptComponents;
        /// <summary>
        /// the components of the exact result left out of the coverage because the rounding removed tiny or thin ones (one
        /// line each: triangles, area, box); not empty: Check() does not report the result as clean (owner decision
        /// 2026-11-22: removed parts are listed, never silently exempt)
        /// </summary>
        public List<string> RemovedParts = new List<string>();
        /// <summary> the rounding's relative bound (for the thin and tiny components it may remove) </summary>
        public double MaxDisplacement = 1e-5;

        /// <summary>
        /// Runs a boolean with the recording on (and exact vertices) and returns it with its certificate data. configure
        /// may set more fields (SafeRounding, CullOutsideA, ...).
        /// </summary>
        public static BooleanCertificate Run(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean boolean, Action<ExactMeshBoolean> configure = null)
        {
            boolean = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, RecordCertificate = true, ComputeExactVertices = true };
            configure?.Invoke(boolean);
            if (boolean.Compute() == false)
                throw new InvalidOperationException("boolean failed: " + boolean.Error);
            return From(boolean);
        }

        /// <summary>
        /// The oracle for the property campaigns: runs a op b with the recording (Intersection without the early-out,
        /// see the class comment) and returns "" if the certificate holds and covers every triangle, else the reason.
        /// </summary>
        public static string CheckBoolean(DMesh3 a, DMesh3 b, Ops op)
        {
            BooleanCertificate c = Run(a, b, op, out _, x => { if (op == Ops.Intersection) x.CullOutsideA = false; });
            string r = c.Check();
            if (r != "") return "certificate " + op + ": " + r;
            return c.Uncovered == 0 ? "" : $"certificate {op}: {c.Uncovered} triangles not covered within Delta";
        }

        public static BooleanCertificate From(ExactMeshBoolean b)
        {
            ExactMeshArrangement arr = b.Arrangement;
            var c = new BooleanCertificate {
                Meshes = arr.Meshes, ScaleExp = arr.ScaleExp, Center = (BigInteger[])arr.Center.Clone(),
                Front = b.CertificateFront?.Select(f => f == null ? null : (int[])f.Clone()).ToArray(),
                Keep = (bool[])b.CertificateKeep?.Clone(), Flip = (bool[])b.CertificateFlip?.Clone(),
                Assembled = b.CertificateAssembled, Result = b.Result, AssembledExact = b.ResultExactVertices
            };
            int n = arr.Output.Count;
            c.PieceMesh = new int[n]; c.PieceSource = new int[n]; c.PieceCorners = new ExactPoint3[n][];
            for (int i = 0; i < n; ++i) {
                var ot = arr.Output[i];
                c.PieceMesh[i] = ot.Mesh; c.PieceSource[i] = ot.Source;
                c.PieceCorners[i] = new[] { arr.Exact(ot.A), arr.Exact(ot.B), arr.Exact(ot.C) };
            }
            int nt = arr.Triangles.Count;
            c.TriMesh = new int[nt]; c.TriTid = new int[nt]; c.TriCulled = new bool[nt];
            for (int t = 0; t < nt; ++t) {
                c.TriMesh[t] = arr.Triangles[t].Mesh; c.TriTid[t] = arr.Triangles[t].Tid;
                c.TriCulled[t] = arr.Culled != null && arr.Culled[t];
            }
            if (b.WindingPredicate != null) c.Predicate = b.WindingPredicate;
            else c.Predicate = OperationPredicate(b.Operation);
            if (b.ExactWinding == null || Array.IndexOf(b.ExactWinding, false) >= 0)
                c.NotApplicableReason = "an input is open (generalized winding numbers)";
            if (arr.CulledCount > 0) {
                if (arr.CullOutsideOf == null || arr.CullOutsideOf.Length != 1)
                    c.NotApplicableReason = c.NotApplicableReason ?? "triangles culled against several meshes (set CullOutsideA = false)";
                else if (b.WindingPredicate != null || (b.Operation != Ops.Difference && b.Operation != Ops.Intersection))
                    c.NotApplicableReason = c.NotApplicableReason ?? "culling with a predicate that is not false outside the culling mesh";
                else
                    c.CullMesh = arr.CullOutsideOf[0];
            }
            c.ReorientedTriangles = b.ReorientedTriangles;
            double maxAbs = 0;
            var box = AxisAlignedBox3d.Empty;
            foreach (DMesh3 m in new[] { b.Result, b.CertificateAssembled })
                if (m != null)
                    foreach (Vector3d v in m.Vertices()) {
                        maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))));
                        box.Contain(v);
                    }
            // (the rounding's bound, plus the rounding of the exact vertices to the nearest double: at most an ulp of maxAbs
            // per coordinate; 4 ulps leave room for the split vertices, whose coordinates are midpoints of rounded ends)
            double ulp = Ulp(maxAbs);
            c.MaxLength = b.Rounding != null ? b.Rounding.MaxLength : 0;
            c.Delta = c.MaxLength + 4 * ulp;
            // the documented bound (MeshRoundingRepair: max(MaxDisplacement times the largest component's diagonal,
            // UlpFactor ulps of its largest coordinate, AbsoluteTolerance); StrictRounding without the last two), from the
            // settings: the diagonal of all vertices is at least the largest component's, maxAbs at least its coordinates
            bool strict = b.StrictRounding;
            c.MaxLengthBound = b.Rounding == null ? 0 : Math.Max(b.RoundingMaxDisplacement * (box.DiagonalLength * (1 + 1e-12)),
                strict ? 0 : Math.Max(b.RoundingUlpFactor * ulp, b.RoundingAbsoluteTolerance));
            c.RemovedComponents = b.Rounding != null ? b.Rounding.RemovedComponents : 0;
            c.MaxDisplacement = b.RoundingMaxDisplacement;
            c.Inputs = new List<DMesh3>(b.Meshes ?? new List<DMesh3> { b.MeshA, b.MeshB });
            return c;
        }

        // the distance between x and the next double away from 0 (2^-1074 at 0)
        static double Ulp(double x)
        {
            x = Math.Abs(x);
            if (x == 0) return double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            return e == 0 ? double.Epsilon : ExactPoint3.ScaleB(1.0, e - 1075);
        }

        /// <summary> the checker's own predicate of a built-in operation (as documented on ExactMeshBoolean.Operations) </summary>
        public static Func<int[], bool> OperationPredicate(Ops op)
        {
            switch (op) {
                case Ops.Union: return w => w.Any(x => x > 0);
                case Ops.Intersection: return w => w.All(x => x > 0);
                default: return w => w[0] > 0 && w.Skip(1).All(x => x <= 0);
            }
        }

        /// <summary> all checks (the embedding C2 unless embedding is false): "" if the certificate holds, "n/a: ..." if it does not apply, else the first failure </summary>
        public string Check(bool embedding = true)
        {
            if (NotApplicableReason != null)
                return "n/a: " + NotApplicableReason;
            string r = CheckInputs();
            if (r != "") return "C0: " + r;
            r = CheckPieces();
            if (r != "") return "C1: " + r;
            // (embedding: false only for the tests that mutate the witness, which C2 does not read, many times over one set of pieces)
            r = embedding ? CheckEmbedding() : "";
            if (r != "") return "C2: " + r;
            r = CheckClassification();
            if (r != "") return "C3/C4: " + r;
            r = CheckResult();
            if (r != "") return "C5: " + r;
            // (the rounding's removal of tiny and thin components is a documented decision, but not the point set within
            // the bound: the removed parts are listed, and the result is not reported as clean)
            if (RemovedParts.Count > 0)
                return $"not clean: the rounding removed {RemovedParts.Count} part(s) of the exact result (all else holds): " + string.Join("; ", RemovedParts);
            return "";
        }

        // ------------------------------------------------------------------ exact helpers (homogeneous points, W > 0)

        // numerator of a - b (scaled by a.W b.W > 0)
        static BigInteger[] Sub(ExactPoint3 a, ExactPoint3 b) =>
            new[] { a.X * b.W - b.X * a.W, a.Y * b.W - b.Y * a.W, a.Z * b.W - b.Z * a.W };
        static BigInteger[] Cross(BigInteger[] u, BigInteger[] v) =>
            new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
        static BigInteger Dot(BigInteger[] u, BigInteger[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
        static bool IsZero(BigInteger[] u) => u[0].IsZero && u[1].IsZero && u[2].IsZero;
        // sign of det[b - a, c - a, d - a]
        static int Orient(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d) =>
            Dot(Sub(b, a), Cross(Sub(c, a), Sub(d, a))).Sign;
        // normal numerator (b - a) x (c - a), positively scaled
        static BigInteger[] Normal(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c) => Cross(Sub(b, a), Sub(c, a));
        static ExactPoint3 Centroid(ExactPoint3[] t)
        {
            ExactPoint3 a = t[0], b = t[1], c = t[2];
            BigInteger w = a.W * b.W * c.W;
            return ExactPoint3.Create(a.X * b.W * c.W + b.X * a.W * c.W + c.X * a.W * b.W, a.Y * b.W * c.W + b.Y * a.W * c.W + c.Y * a.W * b.W,
                a.Z * b.W * c.W + b.Z * a.W * c.W + c.Z * a.W * b.W, 3 * w);
        }

        static ExactPoint3 FromDouble(Vector3d v)
        {
            // x = m 2^e: a common power of two below the smallest exponent
            BigInteger[] m = new BigInteger[3]; int[] e = new int[3];
            for (int k = 0; k < 3; ++k) ExactPoint3.Decompose(v[k], out m[k], out e[k]);
            int lo = Math.Min(0, Math.Min(e[0], Math.Min(e[1], e[2])));
            BigInteger[] x = new BigInteger[3];
            for (int k = 0; k < 3; ++k) x[k] = m[k] << (e[k] - lo);
            return ExactPoint3.Create(x[0], x[1], x[2], BigInteger.One << -lo);
        }

        // grid point of an input double vertex: v 2^-ScaleExp - Center (an integer point; checked)
        ExactPoint3 ToGrid(Vector3d v)
        {
            ExactPoint3 p = FromDouble(v);
            BigInteger[] g = new BigInteger[3];
            for (int k = 0; k < 3; ++k) {
                BigInteger num = p[k], den = p.W;
                if (ScaleExp >= 0) den <<= ScaleExp; else num <<= -ScaleExp;
                BigInteger q = BigInteger.DivRem(num, den, out BigInteger rem);
                if (rem.IsZero == false)
                    throw new InvalidOperationException("an input coordinate is not on the arrangement's grid");
                g[k] = q - Center[k];
            }
            return ExactPoint3.FromInteger(g[0], g[1], g[2]);
        }

        // input coordinates of a grid point: (p + Center) 2^ScaleExp
        ExactPoint3 ToInput(ExactPoint3 p)
        {
            BigInteger x = p.X + Center[0] * p.W, y = p.Y + Center[1] * p.W, z = p.Z + Center[2] * p.W, w = p.W;
            if (ScaleExp >= 0) { x <<= ScaleExp; y <<= ScaleExp; z <<= ScaleExp; } else w <<= -ScaleExp;
            return ExactPoint3.Create(x, y, z, w);
        }

        // ------------------------------------------------------------------ input triangles (grid), planes, stacks

        ExactPoint3[][] triCorners;
        BigInteger[][] triNormal;
        Dictionary<string, List<int>> planeGroups;
        string[] triPlane;

        static string Key(params BigInteger[] v)
        {
            var sb = new StringBuilder();
            foreach (BigInteger x in v) sb.Append(x.ToString(CultureInfo.InvariantCulture)).Append(',');
            return sb.ToString();
        }

        // canonical key of the plane n . x = d (n, d numerators, d over dw > 0): reduced, first nonzero of n positive
        static string PlaneKey(BigInteger[] n, BigInteger d, BigInteger dw)
        {
            // n . x = d / dw  <=>  (n dw) . x = d
            BigInteger[] v = { n[0] * dw, n[1] * dw, n[2] * dw, d };
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(v[0], v[1]), BigInteger.GreatestCommonDivisor(v[2], v[3]));
            int s = !v[0].IsZero ? v[0].Sign : !v[1].IsZero ? v[1].Sign : v[2].Sign;
            for (int k = 0; k < 4; ++k) v[k] = v[k] * s / g;
            return Key(v);
        }

        static string PlaneKeyOf(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c)
        {
            BigInteger[] n = Normal(a, b, c);
            BigInteger[] an = { a.X, a.Y, a.Z };
            return PlaneKey(n, Dot(n, an), a.W);
        }

        void PrepareInputs()
        {
            if (triCorners != null) return;
            int nt = TriMesh.Length;
            triCorners = new ExactPoint3[nt][];
            triNormal = new BigInteger[nt][];
            triPlane = new string[nt];
            planeGroups = new Dictionary<string, List<int>>();
            for (int t = 0; t < nt; ++t) {
                DMesh3 m = Meshes[TriMesh[t]];
                Index3i tri = m.GetTriangle(TriTid[t]);
                var c = new[] { ToGrid(m.GetVertex(tri.a)), ToGrid(m.GetVertex(tri.b)), ToGrid(m.GetVertex(tri.c)) };
                triCorners[t] = c;
                triNormal[t] = Normal(c[0], c[1], c[2]);
                if (IsZero(triNormal[t])) continue;
                triPlane[t] = PlaneKeyOf(c[0], c[1], c[2]);
                if (planeGroups.TryGetValue(triPlane[t], out List<int> g) == false) planeGroups[triPlane[t]] = g = new List<int>();
                g.Add(t);
            }
        }

        // A conservative box of exact corners, from their approximations (each within 2 ulp), widened by a relative 2^-49 and the
        // smallest double: a pair of triangles whose boxes are disjoint is disjoint, so a test that only decides pairs that meet
        // may skip it. The filter of the two in-plane loops (the stacks, the straddle test of C2); never a decision by itself.
        static void ApproxBox(ExactPoint3[] c, double[] lo, double[] hi)
        {
            var a = new[] { c[0].ApproxVector3d(), c[1].ApproxVector3d(), c[2].ApproxVector3d() };
            for (int k = 0; k < 3; ++k) {
                double l = Math.Min(a[0][k], Math.Min(a[1][k], a[2][k])), h = Math.Max(a[0][k], Math.Max(a[1][k], a[2][k]));
                lo[k] = l - Math.Abs(l) * (1.0 / (1L << 49)) - double.Epsilon;
                hi[k] = h + Math.Abs(h) * (1.0 / (1L << 49)) + double.Epsilon;
            }
        }

        // the boxes of the input triangles of one plane (the coplanar ones: planeGroups), with a tree where there are many
        sealed class PlaneBoxes
        {
            public int[] Members; public double[] Lo, Hi; public EmbeddingBoxTree Tree;
        }
        Dictionary<string, PlaneBoxes> planeBoxes;
        const int PlaneTreeMin = 8;

        PlaneBoxes BoxesOfPlane(string plane)
        {
            if (planeBoxes == null) planeBoxes = new Dictionary<string, PlaneBoxes>();
            if (planeBoxes.TryGetValue(plane, out PlaneBoxes pb)) return pb;
            int[] m = planeGroups[plane].ToArray();
            pb = new PlaneBoxes { Members = m, Lo = new double[3 * m.Length], Hi = new double[3 * m.Length] };
            double[] l = new double[3], h = new double[3];
            for (int i = 0; i < m.Length; ++i) {
                ApproxBox(triCorners[m[i]], l, h);
                for (int k = 0; k < 3; ++k) { pb.Lo[3 * i + k] = l[k]; pb.Hi[3 * i + k] = h[k]; }
            }
            if (m.Length > PlaneTreeMin) pb.Tree = new EmbeddingBoxTree(pb.Lo, pb.Hi, m.Length);
            planeBoxes[plane] = pb;
            return pb;
        }

        /// <summary>
        /// the input triangles u of the plane of source src, in the order of planeGroups (ascending), whose box meets the box of the
        /// piece (and src itself); every coplanar triangle that meets the piece is among them
        /// </summary>
        List<int> CoplanarNear(int src, ExactPoint3[] piece)
        {
            PlaneBoxes pb = BoxesOfPlane(triPlane[src]);
            double[] qlo = new double[3], qhi = new double[3];
            ApproxBox(piece, qlo, qhi);
            var hit = new List<int>();
            if (pb.Tree != null) pb.Tree.Query(qlo, qhi, hit);
            else
                for (int i = 0; i < pb.Members.Length; ++i) {
                    int x = 3 * i;
                    if (pb.Lo[x] <= qhi[0] && qlo[0] <= pb.Hi[x] && pb.Lo[x + 1] <= qhi[1] && qlo[1] <= pb.Hi[x + 1] && pb.Lo[x + 2] <= qhi[2] && qlo[2] <= pb.Hi[x + 2])
                        hit.Add(i);
                }
            var r = new List<int>(hit.Count + 1);
            foreach (int i in hit) r.Add(pb.Members[i]);
            if (r.Contains(src) == false) r.Add(src);
            r.Sort();
            return r;
        }

        // sign of point p against the triangle t's edges in its plane: +1 strictly inside, 0 on the boundary, -1 outside
        int InsideInPlane(int t, ExactPoint3 p)
        {
            ExactPoint3[] c = triCorners[t];
            bool zero = false;
            for (int k = 0; k < 3; ++k) {
                ExactPoint3 a = c[k], b = c[(k + 1) % 3];
                int s = Dot(Cross(Sub(b, a), Sub(p, a)), triNormal[t]).Sign;
                if (s < 0) return -1;
                if (s == 0) zero = true;
            }
            return zero ? 0 : 1;
        }

        // ------------------------------------------------------------------ C0: the inputs, the culled triangles, the bound

        /// <summary>
        /// C0: the meshes the arrangement used are the caller's (the same corners per triangle, in the same order, or
        /// reversed for exactly ReorientedTriangles of them); every non-degenerate input triangle is an input triangle of
        /// the arrangement exactly once; every culled triangle lies outside the culling mesh (it meets none of that mesh's
        /// triangles, exact test, and an exact ray per connected set of culled triangles gives a winding number <= 0
        /// there); the rounding's bound is at most the documented one. "" or the reason
        /// </summary>
        public string CheckInputs()
        {
            try { return check_inputs(); }
            catch (CertificateException e) { return e.Message; }
        }

        string check_inputs()
        {
            PrepareInputs();
            if (Inputs != null) {
                if (Inputs.Count != Meshes.Count) return "a different number of meshes";
                int reversed = 0;
                for (int m = 0; m < Meshes.Count; ++m) {
                    DMesh3 x = Inputs[m], y = Meshes[m];
                    var flipped = new HashSet<int>();
                    if (x.TriangleCount != y.TriangleCount) return $"mesh {m}: a different number of triangles";
                    foreach (int t in x.TriangleIndices()) {
                        if (y.IsTriangle(t) == false) return $"mesh {m}: triangle {t} missing";
                        Index3i a = x.GetTriangle(t), b = y.GetTriangle(t);
                        Vector3d[] pa = { x.GetVertex(a.a), x.GetVertex(a.b), x.GetVertex(a.c) }, pb = { y.GetVertex(b.a), y.GetVertex(b.b), y.GetVertex(b.c) };
                        bool same = false, rev = false;
                        for (int k = 0; k < 3; ++k) {
                            same |= pa[0] == pb[k] && pa[1] == pb[(k + 1) % 3] && pa[2] == pb[(k + 2) % 3];
                            rev |= pa[0] == pb[k] && pa[1] == pb[(k + 2) % 3] && pa[2] == pb[(k + 1) % 3];
                        }
                        if (same == false && rev == false) return $"mesh {m}: triangle {t} has other corners";
                        if (same == false) { reversed++; flipped.Add(t); }
                    }
                    string o = CheckOrientation(x, flipped);
                    if (o != "") return $"mesh {m}: " + o;
                }
                if (reversed != ReorientedTriangles) return $"{reversed} triangles reversed, {ReorientedTriangles} reported";
            }
            var seen = new HashSet<(int, int)>();
            for (int t = 0; t < TriMesh.Length; ++t)
                if (seen.Add((TriMesh[t], TriTid[t])) == false) return $"input triangle {TriMesh[t]}:{TriTid[t]} twice in the arrangement";
            for (int m = 0; m < Meshes.Count; ++m)
                foreach (int t in Meshes[m].TriangleIndices()) {
                    if (seen.Contains((m, t))) continue;
                    Index3i tri = Meshes[m].GetTriangle(t);
                    if (IsZero(Normal(ToGrid(Meshes[m].GetVertex(tri.a)), ToGrid(Meshes[m].GetVertex(tri.b)), ToGrid(Meshes[m].GetVertex(tri.c)))) == false)
                        return $"input triangle {m}:{t} is not in the arrangement";
                }
            if (MaxLength > MaxLengthBound) return $"the rounding's bound {MaxLength:R} is above the documented {MaxLengthBound:R}";
            return CheckCulled();
        }

        // the caller's mesh x, its triangles reversed by the boolean: none of a component that was oriented consistently
        // (components: triangles joined across edges with exactly two triangles, by vertex position; consistent: the two
        // run every such edge in opposite directions), and every component consistent afterwards where one orientation of
        // it is (no edge with two triangles that cannot be made opposite)
        static string CheckOrientation(DMesh3 x, HashSet<int> flipped)
        {
            var edges = new Dictionary<(Vector3d, Vector3d), List<(int t, bool forward)>>();
            foreach (int t in x.TriangleIndices()) {
                Index3i tri = x.GetTriangle(t);
                Vector3d[] p = { x.GetVertex(tri.a), x.GetVertex(tri.b), x.GetVertex(tri.c) };
                for (int k = 0; k < 3; ++k) {
                    Vector3d a = p[k], b = p[(k + 1) % 3];
                    if (a == b) continue;
                    bool forward = a.x < b.x || (a.x == b.x && (a.y < b.y || (a.y == b.y && a.z < b.z)));
                    var key = forward ? (a, b) : (b, a);
                    if (edges.TryGetValue(key, out var l) == false) edges[key] = l = new List<(int, bool)>();
                    l.Add((t, forward));
                }
            }
            var parent = new Dictionary<int, int>();
            foreach (int t in x.TriangleIndices()) parent[t] = t;
            int Root(int v) { while (parent[v] != v) v = parent[v] = parent[parent[v]]; return v; }
            var pairs = edges.Values.Where(l => l.Count == 2 && l[0].t != l[1].t).ToList();
            foreach (var l in pairs) { int ra = Root(l[0].t), rb = Root(l[1].t); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }
            var consistentBefore = new Dictionary<int, bool>();
            var consistentAfter = new Dictionary<int, bool>();
            foreach (int t in x.TriangleIndices()) { consistentBefore[Root(t)] = true; consistentAfter[Root(t)] = true; }
            foreach (var l in pairs) {
                int r = Root(l[0].t);
                if (l[0].forward == l[1].forward) consistentBefore[r] = false;
                if ((l[0].forward ^ flipped.Contains(l[0].t)) == (l[1].forward ^ flipped.Contains(l[1].t))) consistentAfter[r] = false;
            }
            foreach (int t in flipped)
                if (consistentBefore[Root(t)]) return $"triangle {t} reversed in a consistently oriented component";
            // (a component that can be oriented: the boolean must leave it consistent; a Moebius-like one it leaves as it is)
            foreach (var kv in consistentAfter)
                if (kv.Value == false && consistentBefore[kv.Key] == false && Orientable(x, kv.Key, pairs, Root))
                    return $"a component (triangle {kv.Key}) left inconsistently oriented";
            return "";
        }

        // whether the component of root can be oriented consistently (2-colouring of its triangles by the edge pairs)
        static bool Orientable(DMesh3 x, int root, List<List<(int t, bool forward)>> pairs, Func<int, int> Root)
        {
            var flip = new Dictionary<int, bool>();
            var adj = new Dictionary<int, List<(int o, bool same)>>();
            foreach (var l in pairs) {
                if (Root(l[0].t) != root) continue;
                bool same = l[0].forward == l[1].forward;
                (adj.TryGetValue(l[0].t, out var a) ? a : adj[l[0].t] = new List<(int, bool)>()).Add((l[1].t, same));
                (adj.TryGetValue(l[1].t, out var b) ? b : adj[l[1].t] = new List<(int, bool)>()).Add((l[0].t, same));
            }
            foreach (int start in adj.Keys) {
                if (flip.ContainsKey(start)) continue;
                flip[start] = false;
                var stack = new Stack<int>(); stack.Push(start);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    foreach (var (o, same) in adj[t]) {
                        bool want = flip[t] ^ same;
                        if (flip.TryGetValue(o, out bool f)) { if (f != want) return false; }
                        else { flip[o] = want; stack.Push(o); }
                    }
                }
            }
            return true;
        }

        string CheckCulled()
        {
            CulledSets = 0;
            var culled = Enumerable.Range(0, TriMesh.Length).Where(t => TriCulled[t] && IsZero(triNormal[t]) == false).ToList();
            if (culled.Count == 0) return "";
            if (CullMesh < 0) return "triangles culled without a culling mesh";
            // the culling mesh's triangles, as doubles, with their boxes
            var cTris = Enumerable.Range(0, TriMesh.Length).Where(t => TriMesh[t] == CullMesh && IsZero(triNormal[t]) == false).Select(t => {
                DMesh3 m = Meshes[CullMesh]; Index3i i = m.GetTriangle(TriTid[t]);
                var tri = new Triangle3d(m.GetVertex(i.a), m.GetVertex(i.b), m.GetVertex(i.c));
                var bx = new AxisAlignedBox3d(tri.V0); bx.Contain(tri.V1); bx.Contain(tri.V2);
                return (tri, bx);
            }).ToList();
            // connected sets of culled triangles (shared vertices of their mesh)
            var parent = culled.ToDictionary(t => t, t => t);
            int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var byVertex = new Dictionary<(int, int), int>();
            foreach (int t in culled) {
                if (TriMesh[t] == CullMesh) return $"input triangle {t} of the culling mesh is culled";
                DMesh3 m = Meshes[TriMesh[t]]; Index3i i = m.GetTriangle(TriTid[t]);
                var tri = new Triangle3d(m.GetVertex(i.a), m.GetVertex(i.b), m.GetVertex(i.c));
                var bx = new AxisAlignedBox3d(tri.V0); bx.Contain(tri.V1); bx.Contain(tri.V2);
                foreach (var (ct, cb) in cTris) {
                    // (closed boxes of doubles: exact; a contact counts)
                    if (cb.Max.x < bx.Min.x || bx.Max.x < cb.Min.x || cb.Max.y < bx.Min.y || bx.Max.y < cb.Min.y || cb.Max.z < bx.Min.z || bx.Max.z < cb.Min.z) continue;
                    Triangle3d u = tri, v = ct;
                    if (ExactTriangleIntersection.Intersects(ref u, ref v)) return $"culled input triangle {t} meets the culling mesh";
                }
                foreach (int vid in new[] { i.a, i.b, i.c }) {
                    if (byVertex.TryGetValue((TriMesh[t], vid), out int o)) { int ra = Root(t), rb = Root(o); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }
                    else byVertex[(TriMesh[t], vid)] = t;
                }
            }
            foreach (int t in culled.Where(t => Root(t) == t)) {
                ExactPoint3[] c = triCorners[t];
                int[] w = RayWindingAt(Centroid(c), triNormal[t], triPlane[t], CullMesh, 7919 * t + 3, true, $"culled input triangle {t}");
                CulledSets++;
                if (w[CullMesh] > 0) return $"culled input triangle {t} lies inside the culling mesh (winding number {w[CullMesh]})";
            }
            return "";
        }

        // ------------------------------------------------------------------ C1: the pieces tile their sources

        Dictionary<ExactPoint3, int> pointIds;
        int[][] pieceV;

        int PointId(ExactPoint3 p)
        {
            if (pointIds.TryGetValue(p, out int id) == false) pointIds[p] = id = pointIds.Count;
            return id;
        }

        void PreparePieces()
        {
            if (pieceV != null) return;
            pointIds = new Dictionary<ExactPoint3, int>();
            pieceV = new int[PieceCorners.Length][];
            for (int i = 0; i < PieceCorners.Length; ++i)
                pieceV[i] = PieceCorners[i].Select(PointId).ToArray();
        }

        public string CheckPieces()
        {
            PrepareInputs();
            pieceV = null;
            stacks = null;
            PreparePieces();
            int n = PieceCorners.Length;
            var bySource = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; ++i) {
                int t = PieceSource[i];
                if (t < 0 || t >= TriMesh.Length || TriMesh[t] != PieceMesh[i]) return $"piece {i}: bad source";
                if (IsZero(triNormal[t])) return $"piece {i}: of a degenerate source";
                if (TriCulled[t]) return $"piece {i}: of a culled source";
                string pr = CheckPiece(triCorners[t], PieceCorners[i]);
                if (pr != "") return $"piece {i}: " + pr;
                if (bySource.TryGetValue(t, out List<int> l) == false) bySource[t] = l = new List<int>();
                l.Add(i);
            }
            for (int t = 0; t < TriMesh.Length; ++t)
                if (IsZero(triNormal[t]) == false && TriCulled[t] == false && bySource.ContainsKey(t) == false)
                    return $"input triangle {t} has no pieces";
            foreach (var kv in bySource) {
                string r = CheckTiling(triCorners[kv.Key], kv.Value.Select(i => PieceCorners[i]).ToList());
                if (r != "") return $"source {kv.Key}: " + r;
            }
            return "";
        }

        /// <summary>
        /// C1 for one piece of the (non-degenerate) source triangle c: not degenerate, every corner in c's plane and in the
        /// closed triangle c, oriented like c. "" or the reason
        /// </summary>
        public static string CheckPiece(ExactPoint3[] c, ExactPoint3[] p)
        {
            BigInteger[] nt = Normal(c[0], c[1], c[2]);
            BigInteger[] np = Normal(p[0], p[1], p[2]);
            if (IsZero(np)) return "degenerate";
            for (int k = 0; k < 3; ++k) {
                if (Orient(c[0], c[1], c[2], p[k]) != 0) return $"corner {k} off its source's plane";
                for (int e = 0; e < 3; ++e)
                    if (Dot(Cross(Sub(c[(e + 1) % 3], c[e]), Sub(p[k], c[e])), nt).Sign < 0) return $"corner {k} outside its source";
            }
            if (Dot(np, nt).Sign <= 0) return "oriented against its source";
            return "";
        }

        /// <summary>
        /// C1 for the pieces of the source triangle c (each passing CheckPiece): no directed edge twice, the projected
        /// areas add up exactly to c's, and every unpaired directed edge lies on an edge of c (in c's direction then: a
        /// piece inside c and oriented like it has c's interior on its left). "" or the reason
        /// </summary>
        public static string CheckTiling(ExactPoint3[] c, IList<ExactPoint3[]> pieces)
        {
            BigInteger[] nt = Normal(c[0], c[1], c[2]);
            var ids = new Dictionary<ExactPoint3, int>();
            int Id(ExactPoint3 x) { if (ids.TryGetValue(x, out int v) == false) ids[x] = v = ids.Count; return v; }
            int axis = Enumerable.Range(0, 3).OrderByDescending(k => BigInteger.Abs(nt[k])).First();
            // directed edges (point ids) -> the piece's corners
            var edges = new Dictionary<(int, int), (ExactPoint3, ExactPoint3)>();
            BigInteger num = 0, den = 1;
            foreach (ExactPoint3[] p in pieces) {
                for (int k = 0; k < 3; ++k) {
                    var e = (Id(p[k]), Id(p[(k + 1) % 3]));
                    if (edges.ContainsKey(e)) return "directed edge used twice";
                    edges[e] = (p[k], p[(k + 1) % 3]);
                }
                // projected area component (rational): normal numerator over (a.W b.W)(a.W c.W)
                BigInteger[] np = Normal(p[0], p[1], p[2]);
                BigInteger d = p[0].W * p[1].W * p[0].W * p[2].W;
                num = num * d + np[axis] * den; den *= d;
            }
            // sum == nt[axis] (an integer)
            if (num != nt[axis] * den) return "the pieces' areas do not add up to the source's";
            foreach (var kv in edges) {
                if (edges.ContainsKey((kv.Key.Item2, kv.Key.Item1))) continue;
                (ExactPoint3 p, ExactPoint3 q) = kv.Value;
                bool on = false;
                for (int k = 0; k < 3 && on == false; ++k) {
                    ExactPoint3 a = c[k], b = c[(k + 1) % 3];
                    BigInteger[] ab = Sub(b, a);
                    if (Dot(Cross(ab, Sub(p, a)), nt).IsZero && Dot(Cross(ab, Sub(q, a)), nt).IsZero)
                        on = true;
                }
                if (on == false) return "an unpaired edge is not on the source's boundary";
            }
            return "";
        }

        // ------------------------------------------------------------------ C2: the pieces are embedded (stage 3)

        /// <summary>
        /// results of the last CheckEmbedding: the pieces that took part (the classified ones), the coplanar copies left out,
        /// the pairs of pieces of different sources whose boxes meet, and the coplanar ones among those
        /// </summary>
        public int EmbeddingPieces, EmbeddingCopies, EmbeddingPairs, EmbeddingCoplanarPairs;

        /// <summary>
        /// C2, the embedding of the arrangement: no two pieces meet except in a corner or along an edge they share (by
        /// exact point), so that nothing passes through a piece and every piece that touches an edge has it as an edge.
        /// C3 assumes this (the winding number is constant on one side of a piece, the radial order around an edge is
        /// complete). A box tree of its own over the pieces, then a pair test by orientation signs of exact points only
        /// (BigInteger, no filter and no floating-point decision; see EmbeddingPairTest). Pieces of one source are not
        /// paired: C1 certifies that they tile it. Needs C1 to hold. "" or the first violating pair.
        /// Which pieces: the classified ones, as C3 reads them (the pieces of the first source of each coplanar stack). The
        /// other pieces of a stack are copies, which the arrangement does not make identical to the classified ones: where
        /// the faces of two meshes touch (the bottom face of A on the top face of B), their triangulations differ and the
        /// pieces overlap in the plane by design. They are not kept and take no part in C3's winding numbers, which
        /// the stacks (from the input triangles) account for; so they are left out here and counted (EmbeddingCopies).
        /// </summary>
        /// <summary> the corners of an input triangle (arrangement order) on the grid, for the tests that inject faults </summary>
        public ExactPoint3[] SourceCornersOnGrid(int source) { PrepareInputs(); return triCorners[source]; }
        /// <summary> the input triangles in the plane of the given one, itself included (arrangement order) </summary>
        public int[] CoplanarSources(int source) { PrepareInputs(); return planeGroups[triPlane[source]].ToArray(); }

        public string CheckEmbedding()
        {
            // (uses the pieces and stacks that CheckPieces prepared: call CheckPieces again after changing the pieces)
            try { return check_embedding(); }
            catch (DivideByZeroException e) { return "a degenerate configuration: " + e.Message; }
            catch (CertificateException e) { return e.Message; }
        }

        string check_embedding()
        {
            if (stacks == null) PrepareStacks();
            EmbeddingPieces = EmbeddingCopies = EmbeddingPairs = EmbeddingCoplanarPairs = 0;
            int[] part = Enumerable.Range(0, PieceCorners.Length).Where(i => classified[i]).ToArray();
            EmbeddingPieces = part.Length;
            EmbeddingCopies = PieceCorners.Length - part.Length;
            int n = part.Length;
            ExactPoint3[] x = new ExactPoint3[pointIds.Count];
            foreach (var kv in pointIds) x[kv.Value] = kv.Key;
            var approx = new Vector3d[x.Length];
            for (int i = 0; i < x.Length; ++i) approx[i] = x[i].ApproxVector3d();
            // the boxes of the pieces from the approximations, widened beyond their error (relative 2^-50) with room to spare
            double[] lo = new double[3 * n], hi = new double[3 * n];
            for (int i = 0; i < n; ++i)
                for (int k = 0; k < 3; ++k) {
                    int[] v = pieceV[part[i]];
                    double a = approx[v[0]][k], b = approx[v[1]][k], c = approx[v[2]][k];
                    double l = Math.Min(a, Math.Min(b, c)), h = Math.Max(a, Math.Max(b, c));
                    lo[3 * i + k] = l - Math.Abs(l) * (1.0 / (1L << 49)) - double.Epsilon;
                    hi[3 * i + k] = h + Math.Abs(h) * (1.0 / (1L << 49)) + double.Epsilon;
                }
            var pairs = new List<(int, int)>();
            new EmbeddingBoxTree(lo, hi, n).SelfJoin(part.Select(i => PieceSource[i]).ToArray(), pairs);
            pairs.Sort();
            var test = new EmbeddingPairTest(x);
            EmbeddingPairs = pairs.Count;
            foreach ((int k, int l) in pairs) {
                int i = part[k], j = part[l];
                if (test.MeetBeyondShared(pieceV[i], pieceV[j])) {
                    EmbeddingCoplanarPairs = (int)test.Coplanar;
                    return $"pieces {i} (source {PieceSource[i]}) and {j} (source {PieceSource[j]}) meet beyond the corners and edges they share";
                }
            }
            EmbeddingCoplanarPairs = (int)test.Coplanar;
            // the stacks of C3 take a coplanar input triangle u for a piece by the piece's centroid, which assumes that no
            // classified piece straddles u's boundary: it lies in u, or its interior is disjoint from u's
            foreach (int i in part) {
                int src = PieceSource[i];
                foreach (int u in CoplanarNear(src, PieceCorners[i]))
                    if (u != src && TriCulled[u] == false && IsZero(triNormal[u]) == false && StraddlesInPlane(PieceCorners[i], triCorners[u]))
                        return $"piece {i} (source {src}) straddles the boundary of the coplanar input triangle {u}";
            }
            return "";
        }

        /// <summary>
        /// for tests: the box filter of the two in-plane loops (the stacks of C3, the straddle test of C2) against the exact
        /// tests it stands in front of: every coplanar input triangle that contains a piece's centroid or touches it (is not
        /// "interior disjoint") must be among CoplanarNear's. "" or the first piece and triangle that the filter lost.
        /// </summary>
        public string AuditCoplanarFilter(out int tested, out int exactHits)
        {
            tested = exactHits = 0;
            PrepareStacks();
            for (int i = 0; i < PieceCorners.Length; ++i) {
                int src = PieceSource[i];
                var near = new HashSet<int>(CoplanarNear(src, PieceCorners[i]));
                ExactPoint3 cen = Centroid(PieceCorners[i]);
                foreach (int u in planeGroups[triPlane[src]]) {
                    if (u == src || TriCulled[u] || IsZero(triNormal[u])) continue;
                    tested++;
                    bool hit = InsideInPlane(u, cen) >= 0 || StraddlesInPlane(PieceCorners[i], triCorners[u]);
                    if (hit) exactHits++;
                    if (hit && near.Contains(u) == false) return $"piece {i} (source {src}) and coplanar triangle {u}: the box filter dropped a pair that meets";
                }
            }
            return "";
        }

        /// <summary>
        /// Coplanar triangles p and u (both non-degenerate): p is neither inside the closed u nor has an interior disjoint from
        /// u's. Exact: p inside u iff its corners are on the inner side of u's edge lines; the interiors are disjoint iff a
        /// line of an edge of either triangle has the other on its closed outer side (the separating axis test).
        /// </summary>
        public static bool StraddlesInPlane(ExactPoint3[] p, ExactPoint3[] u)
        {
            BigInteger[] n = Normal(p[0], p[1], p[2]);
            int O2(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c) => Dot(Cross(Sub(b, a), Sub(c, a)), n).Sign;
            if (O2(u[0], u[1], u[2]) < 0) u = new[] { u[0], u[2], u[1] };
            bool inside = true;
            for (int e = 0; e < 3 && inside; ++e)
                for (int k = 0; k < 3; ++k)
                    if (O2(u[e], u[(e + 1) % 3], p[k]) < 0) { inside = false; break; }
            if (inside) return false;
            for (int e = 0; e < 3; ++e) {
                bool pOut = true, uOut = true;
                for (int k = 0; k < 3; ++k) {
                    if (O2(u[e], u[(e + 1) % 3], p[k]) > 0) pOut = false;
                    if (O2(p[e], p[(e + 1) % 3], u[k]) > 0) uOut = false;
                }
                if (pOut || uOut) return false;
            }
            return true;
        }

        /// <summary>
        /// Do the triangles p and q (point ids into X) meet anywhere beyond the corners they share by id: not in a shared
        /// corner, not along a shared edge? (The same triangle twice meets beyond: the fold, below.) By the segments: two triangles in different planes meet in
        /// a segment whose ends lie on an edge of one inside the other, so they meet only where they share iff every edge
        /// of each that is not shared meets the other at most in an end that is a shared corner; an edge against a triangle
        /// is decided by orientation signs (the ends against the plane, then the line against the three edges; an edge in
        /// the plane is decided through the other edges, see EdgesMeet). Coplanar
        /// pairs use the same rule in the plane, plus the fold (two triangles on one side of a shared edge). The only
        /// predicate is the sign of det[b - a, c - a, d - a] on exact points, by BigInteger (the principle of Devillers and
        /// Guigue 2002: orientation signs only, no constructed point). Shares no code with the arrangement.
        /// </summary>
        internal sealed class EmbeddingPairTest
        {
            readonly ExactPoint3[] X;
            // integer points with coordinates below 2^19 in absolute value (W = 1): the determinant of four of them is exact in
            // long (each difference below 2^20, each of the six terms of the determinant below 2^60)
            readonly bool[] small;
            readonly long[] sx, sy, sz;
            public long Coplanar, EarlyOut;

            public EmbeddingPairTest(ExactPoint3[] x)
            {
                X = x;
                small = new bool[x.Length]; sx = new long[x.Length]; sy = new long[x.Length]; sz = new long[x.Length];
                BigInteger limit = BigInteger.One << 19;
                for (int i = 0; i < x.Length; ++i) {
                    ExactPoint3 p = x[i];
                    if (p.W.IsOne && BigInteger.Abs(p.X) < limit && BigInteger.Abs(p.Y) < limit && BigInteger.Abs(p.Z) < limit) {
                        small[i] = true; sx[i] = (long)p.X; sy[i] = (long)p.Y; sz[i] = (long)p.Z;
                    }
                }
            }

            BigInteger[] Diff(int a, int b)
            {
                ExactPoint3 p = X[a], q = X[b];
                if (p.W.IsOne && q.W.IsOne) return new[] { p.X - q.X, p.Y - q.Y, p.Z - q.Z };
                return Sub(p, q);
            }

            /// <summary> the sign of det[b - a, c - a, d - a]; equal ids give 0 without arithmetic </summary>
            public int O3(int a, int b, int c, int d)
            {
                if (a == b || a == c || a == d || b == c || b == d || c == d) return 0;
                if (small[a] && small[b] && small[c] && small[d]) {
                    long bx = sx[b] - sx[a], by = sy[b] - sy[a], bz = sz[b] - sz[a];
                    long cx = sx[c] - sx[a], cy = sy[c] - sy[a], cz = sz[c] - sz[a];
                    long dx = sx[d] - sx[a], dy = sy[d] - sy[a], dz = sz[d] - sz[a];
                    return Math.Sign(bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));
                }
                return Dot(Diff(b, a), Cross(Diff(c, a), Diff(d, a))).Sign;
            }

            public bool MeetBeyondShared(int[] p, int[] q)
            {
                int[] sp = new int[3], sq = new int[3];     // the sides of p's corners against q's plane, of q's against p's
                int pos = 0, neg = 0;
                for (int i = 0; i < 3; ++i) { sp[i] = O3(q[0], q[1], q[2], p[i]); if (sp[i] > 0) pos++; else if (sp[i] < 0) neg++; }
                if (pos == 3 || neg == 3) { EarlyOut++; return false; }
                if (pos == 0 && neg == 0) { Coplanar++; return CoplanarMeet(p, q); }
                pos = neg = 0;
                for (int i = 0; i < 3; ++i) { sq[i] = O3(p[0], p[1], p[2], q[i]); if (sq[i] > 0) pos++; else if (sq[i] < 0) neg++; }
                if (pos == 3 || neg == 3) { EarlyOut++; return false; }
                return EdgesMeet(p, sp, q) || EdgesMeet(q, sq, p);
            }

            static bool Has(int[] t, int v) => t[0] == v || t[1] == v || t[2] == v;
            static bool Mixed(int a, int b, int c) => (a > 0 || b > 0 || c > 0) && (a < 0 || b < 0 || c < 0);

            // the edges of p against q (not coplanar; s: the sides of p's corners against q's plane). An edge of p that lies in
            // q's plane (both ends on it) needs no test of its own: if it meets q beyond a shared corner, then an end of it
            // lies in q (found by the end's other edge, which leaves the plane: the case below), or it passes through q and
            // an edge of q crosses p's plane at a point of it (found by the same test with p and q swapped, or through a
            // corner of q on it). The lattice test of the tests checks this against the library's exact test.
            bool EdgesMeet(int[] p, int[] s, int[] q)
            {
                for (int i = 0; i < 3; ++i) {
                    int j = (i + 1) % 3, a = p[i], b = p[j];
                    if (s[i] * s[j] > 0) continue;
                    if (s[i] * s[j] < 0) {
                        // crosses q's plane between its ends: in the closed triangle q iff the three edges of q are not on
                        // both sides of the line a b
                        if (Mixed(O3(a, b, q[0], q[1]), O3(a, b, q[1], q[2]), O3(a, b, q[2], q[0])) == false) return true;
                    } else if (s[i] == 0 && s[j] != 0) {
                        if (Has(q, a) == false && InTriangle(a, q, b)) return true;
                    } else if (s[j] == 0 && s[i] != 0) {
                        if (Has(q, b) == false && InTriangle(b, q, a)) return true;
                    }
                }
                return false;
            }

            // x (in q's plane) in the closed triangle q, seen from the apex (off the plane)
            bool InTriangle(int x, int[] q, int apex) =>
                Mixed(O3(q[0], q[1], x, apex), O3(q[1], q[2], x, apex), O3(q[2], q[0], x, apex)) == false;

            // coplanar triangles: orientations in the plane by p's normal
            bool CoplanarMeet(int[] p, int[] q)
            {
                BigInteger[] n = Cross(Diff(p[1], p[0]), Diff(p[2], p[0]));
                int O2(int x, int y, int z) => (x == y || x == z || y == z) ? 0 : Dot(Cross(Diff(y, x), Diff(z, x)), n).Sign;
                for (int i = 0; i < 3; ++i) {
                    int a = p[i], b = p[(i + 1) % 3];
                    if (Has(q, a) && Has(q, b)) {
                        // a shared edge: the third corners on the same side fold onto each other
                        int qc = q[0] != a && q[0] != b ? q[0] : q[1] != a && q[1] != b ? q[1] : q[2];
                        if (O2(a, b, p[(i + 2) % 3]) * O2(a, b, qc) > 0) return true;
                    }
                }
                return EdgesMeetInPlane(p, q, O2) || EdgesMeetInPlane(q, p, O2);
            }

            // coplanar: a corner of p that is not a corner of q lies in the closed triangle q (each corner is the end of the
            // edge before it), or an edge of p crosses an edge of q properly. Both directions are run by the caller, so a
            // corner of q on an edge of p, or collinear overlaps, are found as a corner of q in the closed triangle p.
            static bool EdgesMeetInPlane(int[] p, int[] q, Func<int, int, int, int> o2)
            {
                bool InQ(int x) => Mixed(o2(q[0], q[1], x), o2(q[1], q[2], x), o2(q[2], q[0], x)) == false;
                for (int i = 0; i < 3; ++i) {
                    int a = p[i], b = p[(i + 1) % 3];
                    if (Has(q, b) == false && InQ(b)) return true;
                    int[] o = { o2(a, b, q[0]), o2(a, b, q[1]), o2(a, b, q[2]) };
                    for (int j = 0; j < 3; ++j) {
                        int c = q[j], d = q[(j + 1) % 3];
                        if (o[j] * o[(j + 1) % 3] < 0 && o2(c, d, a) * o2(c, d, b) < 0) return true;
                    }
                }
                return false;
            }
        }

        /// <summary> the broad phase of C2: a median-split tree over boxes of its own (no code of the arrangement's TriangleBoxTree) </summary>
        sealed class EmbeddingBoxTree
        {
            readonly double[] lo, hi;       // per item: 3 + 3
            readonly int[] items;
            readonly int[] nodeStart, nodeEnd, nodeLeft, nodeRight;
            readonly double[] nodeBox;
            int nodes;
            const int Leaf = 4;

            public EmbeddingBoxTree(double[] lo, double[] hi, int n)
            {
                this.lo = lo; this.hi = hi;
                items = Enumerable.Range(0, n).ToArray();
                int cap = Math.Max(1, 2 * n);
                nodeStart = new int[cap]; nodeEnd = new int[cap]; nodeLeft = new int[cap]; nodeRight = new int[cap]; nodeBox = new double[6 * cap];
                if (n > 0) Build(0, n);
            }

            int Build(int s, int e)
            {
                int node = nodes++;
                nodeStart[node] = s; nodeEnd[node] = e; nodeLeft[node] = -1; nodeRight[node] = -1;
                int o = 6 * node;
                for (int k = 0; k < 3; ++k) { nodeBox[o + k] = double.MaxValue; nodeBox[o + 3 + k] = double.MinValue; }
                for (int i = s; i < e; ++i)
                    for (int k = 0; k < 3; ++k) { nodeBox[o + k] = Math.Min(nodeBox[o + k], lo[3 * items[i] + k]); nodeBox[o + 3 + k] = Math.Max(nodeBox[o + 3 + k], hi[3 * items[i] + k]); }
                if (e - s > Leaf) {
                    double ex = nodeBox[o + 3] - nodeBox[o], ey = nodeBox[o + 4] - nodeBox[o + 1], ez = nodeBox[o + 5] - nodeBox[o + 2];
                    int axis = (ex >= ey && ex >= ez) ? 0 : (ey >= ez ? 1 : 2);
                    // (any split is a valid tree: the pairs are sorted afterwards)
                    var keys = new double[e - s]; var part = new int[e - s];
                    for (int i = s; i < e; ++i) { part[i - s] = items[i]; keys[i - s] = lo[3 * items[i] + axis] + hi[3 * items[i] + axis]; }
                    Array.Sort(keys, part);
                    Array.Copy(part, 0, items, s, e - s);
                    int mid = (s + e) / 2;
                    int l = Build(s, mid), r = Build(mid, e);
                    nodeLeft[node] = l; nodeRight[node] = r;
                }
                return node;
            }

            bool NodesMeet(int a, int b)
            {
                int x = 6 * a, y = 6 * b;
                return nodeBox[x] <= nodeBox[y + 3] && nodeBox[y] <= nodeBox[x + 3] && nodeBox[x + 1] <= nodeBox[y + 4] && nodeBox[y + 1] <= nodeBox[x + 4]
                    && nodeBox[x + 2] <= nodeBox[y + 5] && nodeBox[y + 2] <= nodeBox[x + 5];
            }

            bool ItemsMeet(int x, int y)
            {
                x *= 3; y *= 3;
                return lo[x] <= hi[y] && lo[y] <= hi[x] && lo[x + 1] <= hi[y + 1] && lo[y + 1] <= hi[x + 1] && lo[x + 2] <= hi[y + 2] && lo[y + 2] <= hi[x + 2];
            }

            /// <summary> every item whose box meets the box [qlo, qhi] (closed boxes, as in SelfJoin), appended to result </summary>
            public void Query(double[] qlo, double[] qhi, List<int> result)
            {
                if (items.Length == 0) return;
                var stack = new Stack<int>();
                stack.Push(0);
                while (stack.Count > 0) {
                    int node = stack.Pop(), o = 6 * node;
                    if (nodeBox[o] > qhi[0] || qlo[0] > nodeBox[o + 3] || nodeBox[o + 1] > qhi[1] || qlo[1] > nodeBox[o + 4]
                        || nodeBox[o + 2] > qhi[2] || qlo[2] > nodeBox[o + 5]) continue;
                    if (nodeLeft[node] >= 0) { stack.Push(nodeLeft[node]); stack.Push(nodeRight[node]); continue; }
                    for (int i = nodeStart[node]; i < nodeEnd[node]; ++i) {
                        int x = 3 * items[i];
                        if (lo[x] <= qhi[0] && qlo[0] <= hi[x] && lo[x + 1] <= qhi[1] && qlo[1] <= hi[x + 1] && lo[x + 2] <= qhi[2] && qlo[2] <= hi[x + 2])
                            result.Add(items[i]);
                    }
                }
            }

            /// <summary> every unordered pair of items of different groups whose boxes meet, once (smaller item first) </summary>
            public void SelfJoin(int[] group, List<(int, int)> pairs) { if (items.Length > 0) Join(0, 0, group, pairs); }

            void Join(int a, int b, int[] group, List<(int, int)> pairs)
            {
                if (a != b && NodesMeet(a, b) == false) return;
                bool la = nodeLeft[a] < 0, lb = nodeLeft[b] < 0;
                if (la && lb) {
                    for (int i = nodeStart[a]; i < nodeEnd[a]; ++i)
                        for (int j = (a == b ? i + 1 : nodeStart[b]); j < nodeEnd[b]; ++j) {
                            int x = items[i], y = items[j];
                            if (group[x] != group[y] && ItemsMeet(x, y)) pairs.Add(x < y ? (x, y) : (y, x));
                        }
                    return;
                }
                if (a == b) { Join(nodeLeft[a], nodeLeft[a], group, pairs); Join(nodeRight[a], nodeRight[a], group, pairs); Join(nodeLeft[a], nodeRight[a], group, pairs); return; }
                if (lb || (la == false && nodeEnd[a] - nodeStart[a] >= nodeEnd[b] - nodeStart[b])) { Join(nodeLeft[a], b, group, pairs); Join(nodeRight[a], b, group, pairs); }
                else { Join(a, nodeLeft[b], group, pairs); Join(a, nodeRight[b], group, pairs); }
            }
        }

        // ------------------------------------------------------------------ C3 + C4: classification and selection

        List<int>[] stacks;
        bool[] classified;

        void PrepareStacks()
        {
            PrepareInputs();
            PreparePieces();
            int n = PieceCorners.Length;
            stacks = new List<int>[n];
            classified = new bool[n];
            for (int i = 0; i < n; ++i) {
                int src = PieceSource[i];
                ExactPoint3 cen = Centroid(PieceCorners[i]);
                var s = new List<int>();
                foreach (int u in CoplanarNear(src, PieceCorners[i])) {
                    if (u == src) { s.Add(u); continue; }
                    if (TriCulled[u]) continue;
                    int inside = InsideInPlane(u, cen);
                    if (inside == 0) throw new CertificateException($"piece {i}: its centroid is on the edge of coplanar triangle {u}");
                    if (inside > 0) s.Add(u);
                }
                stacks[i] = s;
                classified[i] = s.Min() == src;
            }
        }

        /// <summary> the winding numbers behind classified piece i, from its front and its stack </summary>
        public int[] Back(int i)
        {
            if (stacks == null) PrepareStacks();
            int[] b = (int[])Front[i].Clone();
            BigInteger[] ns = triNormal[PieceSource[i]];
            foreach (int u in stacks[i])
                b[TriMesh[u]] += Dot(triNormal[u], ns).Sign > 0 ? 1 : -1;
            return b;
        }

        bool Care(int[] v, int m) => CullMesh < 0 || m == CullMesh || v[CullMesh] > 0;

        sealed class CertificateException : Exception { public CertificateException(string m) : base(m) { } }

        public string CheckClassification()
        {
            try { return check_classification(); }
            catch (CertificateException e) { return e.Message; }
        }

        string check_classification()
        {
            if (stacks == null) PrepareStacks();      // (CheckPieces invalidates them; C2 prepares them first)
            int n = PieceCorners.Length, nm = Meshes.Count;
            if (Front == null || Keep == null || Flip == null || Front.Length != n || Keep.Length != n || Flip.Length != n)
                return "no witness of the right size";
            var back = new int[n][];
            for (int i = 0; i < n; ++i) {
                if (classified[i] == false) continue;
                if (Front[i] == null || Front[i].Length != nm) return $"piece {i}: classified but no winding numbers";
                back[i] = Back(i);
            }
            // C4 first (cheap): the selection from the witness
            for (int i = 0; i < n; ++i) {
                if (classified[i] == false) {
                    if (Keep[i]) return $"piece {i}: a coplanar copy is kept";
                    continue;
                }
                bool inF = Predicate(Front[i]), inB = Predicate(back[i]);
                if (Keep[i] != (inF != inB)) return $"piece {i}: keep {Keep[i]} but the predicate is {inF} in front and {inB} behind";
                if (Keep[i] && Flip[i] != inF) return $"piece {i}: flip {Flip[i]} but in front {inF}";
            }
            // C3: the wedges around every edge of the classified pieces
            var uf = new int[nm][];
            for (int m = 0; m < nm; ++m) uf[m] = Enumerable.Range(0, n).ToArray();
            var atEdge = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < n; ++i) {
                if (classified[i] == false) continue;
                for (int k = 0; k < 3; ++k) {
                    int u = pieceV[i][k], v = pieceV[i][(k + 1) % 3];
                    var e = (Math.Min(u, v), Math.Max(u, v));
                    if (atEdge.TryGetValue(e, out List<int> l) == false) atEdge[e] = l = new List<int>();
                    l.Add(i);
                }
            }
            var points = new ExactPoint3[pointIds.Count];
            foreach (var kv in pointIds) points[kv.Value] = kv.Key;
            foreach (var kv in atEdge) {
                ExactPoint3 p = points[kv.Key.Item1], q = points[kv.Key.Item2];
                List<int> around = kv.Value;
                // third corner and whether the piece runs p -> q (its front faces the increasing angle)
                var r = new ExactPoint3[around.Count];
                var up = new bool[around.Count];
                for (int j = 0; j < around.Count; ++j) {
                    int i = around[j];
                    for (int k = 0; k < 3; ++k) {
                        int a = pieceV[i][k], b = pieceV[i][(k + 1) % 3];
                        if (a == kv.Key.Item1 && b == kv.Key.Item2) up[j] = true;
                        if (a != kv.Key.Item1 && a != kv.Key.Item2) r[j] = points[a];
                    }
                }
                int[] order = RadialOrder(p, q, r);
                for (int t = 0; t < order.Length; ++t) {
                    int ja = order[t], jb = order[(t + 1) % order.Length];
                    int a = around[ja], b = around[jb];
                    int[] va = up[ja] ? Front[a] : back[a];
                    int[] vb = up[jb] ? back[b] : Front[b];
                    // (the culling mesh's entry: Care is true for it, so it is always compared; the others where it is
                    // positive on both sides)
                    for (int m = 0; m < nm; ++m) {
                        if (Care(va, m) == false || Care(vb, m) == false) continue;
                        if (va[m] != vb[m]) return $"pieces {a} and {b} give the wedge at an edge different winding numbers of mesh {m}";
                        Union(uf[m], a, b);
                    }
                }
            }
            // anchors: per mesh entry, every connected set with a side where the entry matters
            var anchored = new bool[nm][];
            for (int m = 0; m < nm; ++m) anchored[m] = new bool[n];
            Anchors = 0;
            for (int i = 0; i < n; ++i) {
                if (classified[i] == false) continue;
                for (int side = 0; side < 2; ++side) {
                    int[] v = side == 0 ? Front[i] : back[i];
                    bool need = false;
                    for (int m = 0; m < nm; ++m)
                        if (Care(v, m) && anchored[m][Find(uf[m], i)] == false) need = true;
                    if (need == false) continue;
                    int[] truth = RayWinding(i, side == 0);
                    Anchors++;
                    for (int m = 0; m < nm; ++m) {
                        if ((Care(v, m) || Care(truth, m)) && v[m] != truth[m])
                            return $"piece {i}: the {(side == 0 ? "front" : "back")} winding number of mesh {m} is {v[m]}, the exact ray gives {truth[m]}";
                        if (Care(v, m)) anchored[m][Find(uf[m], i)] = true;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// for mutation tests: set Keep and Flip from the witness by the rule C4 checks, so that a changed witness
        /// reaches C3 with a consistent selection (what a wrong classification would give)
        /// </summary>
        public void SelectFromWitness()
        {
            PrepareStacks();
            for (int i = 0; i < PieceCorners.Length; ++i) {
                Keep[i] = Flip[i] = false;
                if (classified[i] == false || Front[i] == null) continue;
                bool inF = Predicate(Front[i]), inB = Predicate(Back(i));
                Keep[i] = inF != inB;
                Flip[i] = inF;
            }
        }

        /// <summary> for tests: whether the checker classifies piece i (its source is its stack's first triangle) </summary>
        public bool IsClassified(int i) { if (classified == null) { PrepareStacks(); } return classified[i]; }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
        static void Union(int[] p, int a, int b) { a = Find(p, a); b = Find(p, b); if (a != b) p[Math.Max(a, b)] = Math.Min(a, b); }

        // the third corners r sorted by angle around the axis p -> q (right-handed), from r[0]; equal angles fail
        static int[] RadialOrder(ExactPoint3 p, ExactPoint3 q, ExactPoint3[] r)
        {
            int k = r.Length;
            var half = new int[k];
            BigInteger[] d = Sub(q, p), b0 = Sub(r[0], p);
            for (int j = 0; j < k; ++j) {
                int s = Orient(p, q, r[0], r[j]);
                if (s > 0) half[j] = 1;
                else if (s < 0) half[j] = 3;
                else {
                    BigInteger[] a = Sub(r[j], p);
                    // the components perpendicular to d point the same way: (d.d)(a.b) - (a.d)(b.d) > 0
                    // (a and b0 are scaled by different positive factors, which does not change the sign)
                    half[j] = (Dot(d, d) * Dot(a, b0) - Dot(a, d) * Dot(b0, d)).Sign > 0 ? 0 : 2;
                }
            }
            int Compare(int x, int y)
            {
                if (half[x] != half[y]) return half[x].CompareTo(half[y]);
                int s = (half[x] == 1 || half[x] == 3) ? -Orient(p, q, r[x], r[y]) : 0;
                if (s == 0) throw new CertificateException("two classified pieces on one half-plane at an edge");
                return s;
            }
            // (insertion sort: few pieces per edge, and an exception of the comparison reaches the caller unwrapped)
            var order = new int[k];
            for (int j = 0; j < k; ++j) {
                int x = j, at = j;
                while (at > 0 && Compare(order[at - 1], x) > 0) { order[at] = order[at - 1]; --at; }
                order[at] = x;
            }
            return order;
        }

        // the exact winding numbers just in front of (or behind) piece i: a ray from its centroid, signed crossings with
        // every input triangle (culled ones too); a ray that meets an edge or a vertex is replaced by another
        int[] RayWinding(int i, bool front) =>
            RayWindingAt(Centroid(PieceCorners[i]), Normal(PieceCorners[i][0], PieceCorners[i][1], PieceCorners[i][2]), triPlane[PieceSource[i]],
                -1, 1000003 * i + (front ? 1 : 2), front, $"piece {i}");

        // the exact winding numbers at o + epsilon dir for directions near +-np: a ray, signed crossings with every input
        // triangle (of mesh onlyMesh only, if >= 0; culled ones too); triangles through o in plane `plane` are skipped,
        // another one through o (of a counted mesh) is reported (C2); a ray that meets an edge or a vertex is replaced
        int[] RayWindingAt(ExactPoint3 o, BigInteger[] np, string plane, int onlyMesh, int seed, bool front, string what)
        {
            // a direction close to the normal (its scale: the normal reduced, times 4096, plus at most 1024 per coordinate)
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(np[0], np[1]), np[2]);
            BigInteger[] nr = np.Select(x => x / g).ToArray();
            var rng = new Random(seed);
            for (int attempt = 0; attempt < 64; ++attempt) {
                BigInteger[] dir = new BigInteger[3];
                // (4096 |n|^2 > 1024 |n|_1 for an integer n != 0: the perturbation keeps the side; checked below anyway)
                for (int k = 0; k < 3; ++k) dir[k] = (front ? 1 : -1) * nr[k] * 4096 + rng.Next(-1024, 1025);
                if ((Dot(dir, np).Sign > 0) != front) continue;
                int[] w = new int[Meshes.Count];
                bool degenerate = false;
                for (int t = 0; t < TriMesh.Length && degenerate == false; ++t) {
                    if (IsZero(triNormal[t]) || (onlyMesh >= 0 && TriMesh[t] != onlyMesh)) continue;
                    int hit = Crossing(o, dir, t, out degenerate);
                    if (degenerate) break;
                    if (hit == 2) {
                        if (triPlane[t] != plane)
                            throw new CertificateException($"{what}: input triangle {t} passes through its centroid (C2)");
                        continue;
                    }
                    w[TriMesh[t]] += hit;
                }
                if (degenerate == false) return w;
            }
            throw new CertificateException($"{what}: no non-degenerate ray");
        }

        // ray o + s dir (s > 0) against input triangle t: +1 / -1 crossing from its back to its front / front to back, 0 none,
        // 2 the ray starts in the triangle; degenerate: it meets an edge or a vertex, or runs in the plane
        int Crossing(ExactPoint3 o, BigInteger[] dir, int t, out bool degenerate)
        {
            degenerate = false;
            ExactPoint3[] c = triCorners[t];
            // corners relative to o (numerators, all scaled by o.W)
            BigInteger[][] rel = c.Select(v => new[] { v.X * o.W - o.X, v.Y * o.W - o.Y, v.Z * o.W - o.Z }).ToArray();
            bool pos = false, neg = false, zero = false;
            for (int k = 0; k < 3; ++k) {
                int s = Dot(dir, Cross(rel[k], rel[(k + 1) % 3])).Sign;
                if (s > 0) pos = true; else if (s < 0) neg = true; else zero = true;
            }
            if (pos && neg) return 0;
            if (zero) { degenerate = true; return 0; }
            BigInteger[] n = triNormal[t];
            int side = -Dot(n, rel[0]).Sign;     // n . (o - c0)
            int nd = Dot(n, dir).Sign;
            if (side == 0) return 2;
            if (side * nd > 0) return 0;         // behind the start
            return nd > 0 ? 1 : -1;
        }

        // ------------------------------------------------------------------ C5: the result

        public string CheckResult()
        {
            try { return check_result(); }
            catch (DivideByZeroException e) { return "a degenerate configuration: " + e.Message; }
        }

        string check_result()
        {
            PreparePieces();
            if (Result == null || Assembled == null || AssembledExact == null) return "no result, result before rounding, or exact vertices";
            string r = CheckClosed(Result);
            if (r != "") return "result: " + r;
            r = CheckSameChain();
            if (r != "") return r;
            return CheckCoverage();
        }

        static string CheckClosed(DMesh3 mesh)
        {
            var count = new Dictionary<(int, int), int>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    var e = (tri[k], tri[(k + 1) % 3]);
                    count[e] = count.TryGetValue(e, out int c) ? c + 1 : 1;
                }
            }
            foreach (var kv in count) {
                if (kv.Value != 1) return $"edge {kv.Key} used {kv.Value} times the same way";
                if (count.ContainsKey((kv.Key.Item2, kv.Key.Item1)) == false) return $"edge {kv.Key} without its reverse";
            }
            return "";
        }

        // per plane, the boundary of (kept pieces) - (result before rounding) is zero
        string CheckSameChain()
        {
            // (plane, line) -> events (position along the line, weight)
            var lines = new Dictionary<string, List<(ExactPoint3 p, BigInteger[] dir, int w, bool start)>>();
            // per plane: the unsigned projected area of each side (num, den), on the axis of the normal's largest component
            var areas = new Dictionary<string, (BigInteger en, BigInteger ed, BigInteger sn, BigInteger sd)>();
            bool AddTriangle(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int w)
            {
                BigInteger[] n = Normal(a, b, c);
                if (IsZero(n)) return false;
                string plane = PlaneKeyOf(a, b, c);
                int axis = BigInteger.Abs(n[0]) >= BigInteger.Abs(n[1]) && BigInteger.Abs(n[0]) >= BigInteger.Abs(n[2]) ? 0 : BigInteger.Abs(n[1]) >= BigInteger.Abs(n[2]) ? 1 : 2;
                // (the normal numerator is scaled by (a.W b.W)(a.W c.W) > 0)
                BigInteger an = BigInteger.Abs(n[axis]), ad = a.W * b.W * a.W * c.W;
                var ar = areas.TryGetValue(plane, out var v) ? v : (BigInteger.Zero, BigInteger.One, BigInteger.Zero, BigInteger.One);
                if (w > 0) ar = (ar.Item1 * ad + an * ar.Item2, ar.Item2 * ad, ar.Item3, ar.Item4);
                else ar = (ar.Item1, ar.Item2, ar.Item3 * ad + an * ar.Item4, ar.Item4 * ad);
                areas[plane] = ar;
                var t = new[] { a, b, c };
                for (int k = 0; k < 3; ++k) {
                    ExactPoint3 p = t[k], q = t[(k + 1) % 3];
                    BigInteger[] d = Sub(q, p);
                    BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(d[0], d[1]), d[2]);
                    int s = !d[0].IsZero ? d[0].Sign : !d[1].IsZero ? d[1].Sign : d[2].Sign;
                    BigInteger[] dr = d.Select(x => x * s / g).ToArray();
                    // the line: canonical direction and the moment p x dir (p over p.W)
                    BigInteger[] mo = Cross(new[] { p.X, p.Y, p.Z }, dr);
                    BigInteger gm = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(mo[0], mo[1]), BigInteger.GreatestCommonDivisor(mo[2], p.W));
                    string key = plane + "|" + Key(dr) + "|" + Key(mo[0] / gm, mo[1] / gm, mo[2] / gm, p.W / gm);
                    if (lines.TryGetValue(key, out var l) == false) lines[key] = l = new List<(ExactPoint3, BigInteger[], int, bool)>();
                    // the edge p -> q: weight +w at p's position along dir and -w at q's (against dir: -w over [q, p])
                    l.Add((p, dr, w, true));
                    l.Add((q, dr, w, false));
                }
                return true;
            }
            for (int i = 0; i < PieceCorners.Length; ++i) {
                if (Keep[i] == false) continue;
                ExactPoint3[] c = PieceCorners[i].Select(ToInput).ToArray();
                if ((Flip[i] ? AddTriangle(c[0], c[2], c[1], 1) : AddTriangle(c[0], c[1], c[2], 1)) == false) return $"kept piece {i} is degenerate";
            }
            foreach (int t in Assembled.TriangleIndices()) {
                Index3i tri = Assembled.GetTriangle(t);
                if (AddTriangle(AssembledExact[tri.a], AssembledExact[tri.b], AssembledExact[tri.c], -1) == false)
                    return $"triangle {t} of the result before rounding is degenerate";
            }
            foreach (var kv in lines) {
                // position along dir: (p . dir) / p.W; an interval from its start to its end adds w (start < end) or -w
                var ev = new List<(BigInteger num, BigInteger den, int delta)>();
                for (int j = 0; j < kv.Value.Count; j += 2) {
                    var s = kv.Value[j]; var e = kv.Value[j + 1];
                    BigInteger[] dir = s.dir;
                    BigInteger sn = Dot(new[] { s.p.X, s.p.Y, s.p.Z }, dir), en = Dot(new[] { e.p.X, e.p.Y, e.p.Z }, dir);
                    // signed weight w over [s, e]: +w at s, -w at e (an edge against dir has s > e and gives the same as -w over [e, s])
                    ev.Add((sn, s.p.W, s.w));
                    ev.Add((en, e.p.W, -s.w));
                }
                ev.Sort((x, y) => (x.num * y.den).CompareTo(y.num * x.den));
                int acc = 0;
                for (int j = 0; j < ev.Count; ++j) {
                    acc += ev[j].delta;
                    bool last = j + 1 == ev.Count || (ev[j].num * ev[j + 1].den) != (ev[j + 1].num * ev[j].den);
                    if (last && acc != 0) return "the result before rounding differs from the kept pieces (a plane's boundary is not zero)";
                }
            }
            // (equal chains, and E of density at most 1 (copies are never kept): an unsigned area of E_s above E's is a
            // pair of opposite coincident triangles, which the chain cancels)
            foreach (var kv in areas)
                if (kv.Value.Item1 * kv.Value.Item4 != kv.Value.Item3 * kv.Value.Item2)
                    return "the result before rounding has another area than the kept pieces in a plane (coincident opposite triangles)";
            return "";
        }

        // ------------------------------------------------------------------ C5: coverage within Delta

        // exact rational
        struct Q
        {
            public BigInteger N, D;     // D > 0
            public Q(BigInteger n, BigInteger d)
            {
                if (d.IsZero) throw new DivideByZeroException("rational with denominator 0");
                if (d.Sign < 0) { n = -n; d = -d; }
                N = n; D = d;
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            public int Sign => N.Sign;
            public static int Compare(Q a, Q b) => (a.N * b.D).CompareTo(b.N * a.D);
            public Q Reduce() { BigInteger g = BigInteger.GreatestCommonDivisor(N, D); return g.IsOne || g.IsZero ? this : new Q(N / g, D / g); }
        }

        static Q[] Vec(ExactPoint3 p) => new[] { new Q(p.X, p.W), new Q(p.Y, p.W), new Q(p.Z, p.W) };
        static Q[] VSub(Q[] a, Q[] b) => new[] { (a[0] - b[0]).Reduce(), (a[1] - b[1]).Reduce(), (a[2] - b[2]).Reduce() };
        static Q VDot(Q[] a, Q[] b) => (a[0] * b[0] + a[1] * b[1] + a[2] * b[2]).Reduce();

        // exact squared distance of p to triangle (a, b, c) (Ericson, Real-Time Collision Detection, 5.1.5: the closest
        // point by the Voronoi regions of the triangle, all in rationals)
        static Q SquaredDistance(Q[] p, Q[] a, Q[] b, Q[] c)
        {
            Q[] ab = VSub(b, a), ac = VSub(c, a), ap = VSub(p, a);
            Q d1 = VDot(ab, ap), d2 = VDot(ac, ap);
            if (d1.Sign <= 0 && d2.Sign <= 0) return VDot(ap, ap);
            Q[] bp = VSub(p, b);
            Q d3 = VDot(ab, bp), d4 = VDot(ac, bp);
            if (d3.Sign >= 0 && Q.Compare(d4, d3) <= 0) return VDot(bp, bp);
            Q vc = (d1 * d4 - d3 * d2).Reduce();
            if (vc.Sign <= 0 && d1.Sign >= 0 && d3.Sign <= 0) return PointOnSegment(p, a, ab, (d1 / (d1 - d3)).Reduce());
            Q[] cp = VSub(p, c);
            Q d5 = VDot(ab, cp), d6 = VDot(ac, cp);
            if (d6.Sign >= 0 && Q.Compare(d5, d6) <= 0) return VDot(cp, cp);
            Q vb = (d5 * d2 - d1 * d6).Reduce();
            if (vb.Sign <= 0 && d2.Sign >= 0 && d6.Sign <= 0) return PointOnSegment(p, a, ac, (d2 / (d2 - d6)).Reduce());
            Q va = (d3 * d6 - d5 * d4).Reduce();
            if (va.Sign <= 0 && (d4 - d3).Sign >= 0 && (d5 - d6).Sign >= 0)
                return PointOnSegment(p, b, VSub(c, b), ((d4 - d3) / ((d4 - d3) + (d5 - d6))).Reduce());
            Q denom = (va + vb + vc).Reduce();
            Q v = (vb / denom).Reduce(), w = (vc / denom).Reduce();
            Q[] x = new Q[3];
            for (int k = 0; k < 3; ++k) x[k] = (a[k] + ab[k] * v + ac[k] * w).Reduce();
            Q[] dpx = VSub(p, x);
            return VDot(dpx, dpx);
        }

        static Q PointOnSegment(Q[] p, Q[] a, Q[] ab, Q t)
        {
            Q[] x = new Q[3];
            for (int k = 0; k < 3; ++k) x[k] = (a[k] + ab[k] * t).Reduce();
            Q[] d = VSub(p, x);
            return VDot(d, d);
        }

        // a simple uniform grid over triangle boxes (doubles, padded so that it is conservative for the exact distance test)
        sealed class BoxGrid
        {
            readonly double cell; readonly Vector3d origin;
            readonly Dictionary<(int, int, int), List<int>> cells = new Dictionary<(int, int, int), List<int>>();
            public BoxGrid(AxisAlignedBox3d all, int count)
            {
                origin = all.Min;
                double ext = Math.Max(all.MaxDim, double.Epsilon);
                cell = ext / Math.Max(1, Math.Ceiling(Math.Pow(count, 1.0 / 3)));
            }
            (int, int, int) Cell(Vector3d p) => ((int)Math.Floor((p.x - origin.x) / cell), (int)Math.Floor((p.y - origin.y) / cell), (int)Math.Floor((p.z - origin.z) / cell));
            public void Add(int id, AxisAlignedBox3d b)
            {
                var lo = Cell(b.Min); var hi = Cell(b.Max);
                for (int x = lo.Item1; x <= hi.Item1; ++x)
                    for (int y = lo.Item2; y <= hi.Item2; ++y)
                        for (int z = lo.Item3; z <= hi.Item3; ++z) {
                            if (cells.TryGetValue((x, y, z), out List<int> l) == false) cells[(x, y, z)] = l = new List<int>();
                            l.Add(id);
                        }
            }
            public HashSet<int> Query(AxisAlignedBox3d b)
            {
                var r = new HashSet<int>();
                var lo = Cell(b.Min); var hi = Cell(b.Max);
                for (int x = lo.Item1; x <= hi.Item1; ++x)
                    for (int y = lo.Item2; y <= hi.Item2; ++y)
                        for (int z = lo.Item3; z <= hi.Item3; ++z)
                            if (cells.TryGetValue((x, y, z), out List<int> l)) r.UnionWith(l);
                return r;
            }
        }

        string CheckCoverage()
        {
            CoverageTests = Uncovered = 0;
            var rTris = Result.TriangleIndices().Select(t => {
                Index3i i = Result.GetTriangle(t);
                return new[] { Vec(FromDouble(Result.GetVertex(i.a))), Vec(FromDouble(Result.GetVertex(i.b))), Vec(FromDouble(Result.GetVertex(i.c))) };
            }).ToList();
            var eTris = Assembled.TriangleIndices().Select(t => {
                Index3i i = Assembled.GetTriangle(t);
                return new[] { Vec(AssembledExact[i.a]), Vec(AssembledExact[i.b]), Vec(AssembledExact[i.c]) };
            }).ToList();
            var eBoxes = Assembled.TriangleIndices().Select(t => Assembled.GetTriBounds(t)).ToList();
            var rBoxes = Result.TriangleIndices().Select(t => Result.GetTriBounds(t)).ToList();
            string r = Cover(rTris, rBoxes, eTris, eBoxes, "result", "result before rounding");
            if (r != "") return r;
            // (the rounding removes tiny and thin components (MeshRoundingRepair.RemovedComponents, the decision of
            // docs/design/SAFE_ROUNDING.md section 1): where it removed some, the components of the result before rounding
            // that are tiny or thin by that rule are left out of its coverage by the result, the others are covered)
            ExemptComponents = 0;
            RemovedParts = new List<string>();
            if (RemovedComponents > 0) {
                bool[] exempt = ThinOrTinyTriangles(out int comps, RemovedParts);
                ExemptComponents = comps;
                var keep = Enumerable.Range(0, eTris.Count).Where(i => exempt[i] == false).ToList();
                eTris = keep.Select(i => eTris[i]).ToList();
                eBoxes = keep.Select(i => eBoxes[i]).ToList();
            }
            return Cover(eTris, eBoxes, rTris, rBoxes, "result before rounding", "result");
        }

        // per triangle of Assembled (in TriangleIndices order): whether its component (triangles joined by vertices) is tiny
        // (area below 1e-8 times the square of the largest component's diagonal) or thin (closed, and 2 |V| below
        // MaxDisplacement times that diagonal times its area, and |V| below 1e-3 area^1.5): the rule of the removal
        bool[] ThinOrTinyTriangles(out int count, List<string> listed)
        {
            List<int> tids = Assembled.TriangleIndices().ToList();
            var parent = Enumerable.Range(0, tids.Count).ToArray();
            int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var byVertex = new Dictionary<int, int>();
            for (int i = 0; i < tids.Count; ++i) {
                Index3i t = Assembled.GetTriangle(tids[i]);
                foreach (int v in new[] { t.a, t.b, t.c }) {
                    if (byVertex.TryGetValue(v, out int o)) { int ra = Root(i), rb = Root(o); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }
                    else byVertex[v] = i;
                }
            }
            var groups = Enumerable.Range(0, tids.Count).GroupBy(Root).Select(g => g.ToList()).ToList();
            var diag = new double[groups.Count];
            var area = new double[groups.Count];
            var volume = new double[groups.Count];
            var closed = new bool[groups.Count];
            var boxes = new AxisAlignedBox3d[groups.Count];
            for (int g = 0; g < groups.Count; ++g) {
                var box = AxisAlignedBox3d.Empty;
                var edges = new HashSet<(int, int)>();
                Vector3d o = Assembled.GetVertex(Assembled.GetTriangle(tids[groups[g][0]]).a);
                foreach (int i in groups[g]) {
                    Index3i t = Assembled.GetTriangle(tids[i]);
                    Vector3d a = Assembled.GetVertex(t.a), b = Assembled.GetVertex(t.b), c = Assembled.GetVertex(t.c);
                    box.Contain(a); box.Contain(b); box.Contain(c);
                    area[g] += 0.5 * (b - a).Cross(c - a).Length;
                    volume[g] += (a - o).Dot((b - o).Cross(c - o)) / 6;
                    edges.Add((t.a, t.b)); edges.Add((t.b, t.c)); edges.Add((t.c, t.a));
                }
                diag[g] = box.DiagonalLength;
                boxes[g] = box;
                closed[g] = edges.All(e => edges.Contains((e.Item2, e.Item1)));
            }
            double d = diag.Length > 0 ? diag.Max() : 0;
            var result = new bool[tids.Count];
            count = 0;
            for (int g = 0; g < groups.Count; ++g) {
                bool ex = area[g] < 1e-8 * d * d || (closed[g] && 2 * Math.Abs(volume[g]) < MaxDisplacement * d * area[g]
                    && Math.Abs(volume[g]) < 1e-3 * area[g] * Math.Sqrt(area[g]));
                if (ex == false) continue;
                count++;
                listed.Add(string.Format(CultureInfo.InvariantCulture, "{0} triangles, area {1:G4}, box [{2:G6}, {3:G6}, {4:G6}] to [{5:G6}, {6:G6}, {7:G6}]",
                    groups[g].Count, area[g], boxes[g].Min.x, boxes[g].Min.y, boxes[g].Min.z, boxes[g].Max.x, boxes[g].Max.y, boxes[g].Max.z));
                foreach (int i in groups[g]) result[i] = true;
            }
            return result;
        }

        // ---- the double filters of C5. They only decide in one direction, with a margin far above the rounding: a point whose
        // approximation is farther than Delta from the box of a triangle's approximated corners is farther than Delta from the
        // triangle (reject), and one whose distance to a point computed inside the triangle (a convex combination of the
        // approximated corners, whatever the rounding made of the weights) is below Delta by the margin is within Delta
        // (accept). Everything between is decided by the exact test (Near).

        sealed class TriApprox
        {
            public bool Ok; public Vector3d A, B, C; public double[] Lo = new double[3], Hi = new double[3];
        }

        // q as a double (relative error below 2^-50), false where it is not finite or too large for the conversion
        static bool ApproxQ(Q q, out double v)
        {
            v = 0;
            if (q.N.IsZero) return true;
            if (ExactPoint3.BitLength(q.N) > 1000 || ExactPoint3.BitLength(q.D) > 1000) return false;
            v = (double)q.N / (double)q.D;
            return double.IsInfinity(v) == false && double.IsNaN(v) == false;
        }

        static bool ApproxPoint(Q[] p, out Vector3d v)
        {
            v = Vector3d.Zero;
            if (ApproxQ(p[0], out double x) && ApproxQ(p[1], out double y) && ApproxQ(p[2], out double z)) { v = new Vector3d(x, y, z); return true; }
            return false;
        }

        static TriApprox ApproxTri(Q[][] t)
        {
            var r = new TriApprox();
            if (ApproxPoint(t[0], out r.A) && ApproxPoint(t[1], out r.B) && ApproxPoint(t[2], out r.C)) {
                r.Ok = true;
                for (int k = 0; k < 3; ++k) {
                    r.Lo[k] = Math.Min(r.A[k], Math.Min(r.B[k], r.C[k]));
                    r.Hi[k] = Math.Max(r.A[k], Math.Max(r.B[k], r.C[k]));
                }
            }
            return r;
        }

        // the distance from p to a point of the triangle abc that is inside it up to rounding (Ericson 5.1.5; the weights are
        // clamped to a convex combination, so a wrong region only gives a larger distance); NaN where it cannot be computed
        static double DistanceToTriangleApprox(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            Vector3d ab = b - a, ac = c - a, ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            double v, w;
            if (d1 <= 0 && d2 <= 0) { v = 0; w = 0; }
            else {
                Vector3d bp = p - b;
                double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
                Vector3d cp = p - c;
                double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
                double vc = d1 * d4 - d3 * d2, vb = d5 * d2 - d1 * d6, va = d3 * d6 - d5 * d4;
                if (d3 >= 0 && d4 <= d3) { v = 1; w = 0; }
                else if (vc <= 0 && d1 >= 0 && d3 <= 0) { v = d1 / (d1 - d3); w = 0; }
                else if (d6 >= 0 && d5 <= d6) { v = 0; w = 1; }
                else if (vb <= 0 && d2 >= 0 && d6 <= 0) { v = 0; w = d2 / (d2 - d6); }
                else if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) { v = 1 - (d4 - d3) / ((d4 - d3) + (d5 - d6)); w = 1 - v; }
                else { double den = 1 / (va + vb + vc); v = vb * den; w = vc * den; }
            }
            if (double.IsNaN(v) || double.IsNaN(w)) return double.NaN;
            v = Math.Min(1, Math.Max(0, v)); w = Math.Min(1 - v, Math.Max(0, w));
            Vector3d q = a + v * ab + w * ac;
            return (p - q).Length;
        }

        double covDelta, covEps;       // Delta, and the absolute slack of the double filters (set by Cover)

        // Near with the double filters first (pa, ta: the approximations of p and t; ok = they exist)
        static bool NearFiltered(Q[] p, bool pOk, Vector3d pa, Q[][] t, TriApprox ta, Q delta2, double covDelta, double covEps)
        {
            if (pOk && ta.Ok) {
                double tol = covDelta + 3 * covEps;
                if (pa.x < ta.Lo[0] - tol || pa.x > ta.Hi[0] + tol || pa.y < ta.Lo[1] - tol || pa.y > ta.Hi[1] + tol
                    || pa.z < ta.Lo[2] - tol || pa.z > ta.Hi[2] + tol) return false;
                if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) + 3 * covEps <= covDelta) return true;
            }
            return Near(p, t, delta2);
        }

        string Cover(List<Q[][]> from, List<AxisAlignedBox3d> fromBoxes, List<Q[][]> to, List<AxisAlignedBox3d> toBoxes, string fromName, string toName)
        {
            if (from.Count == 0) return "";
            if (to.Count == 0) return $"the {fromName} has triangles, the {toName} none";
            var all = AxisAlignedBox3d.Empty;
            foreach (var b in toBoxes) all.Contain(b);
            foreach (var b in fromBoxes) all.Contain(b);
            // (the boxes of the exact triangles from rounded corners: a pad of Delta plus a relative 1e-12 of the extent and
            // the coordinates is far above the rounding of the box corners)
            double pad = Delta * 1.001 + 1e-12 * (all.MaxDim + Math.Abs(all.Min.x) + Math.Abs(all.Min.y) + Math.Abs(all.Min.z) + Math.Abs(all.Max.x) + Math.Abs(all.Max.y) + Math.Abs(all.Max.z)) + 1e-300;
            // (the grid over the padded extent: with the unpadded one, a result thinner than Delta in all directions, eg a sliver
            // of a few units in the last place, gave cells far smaller than the pad, and one padded box filled (pad / cell)^3 of
            // them: out of memory after 100 s on a result of 4 triangles, found by ExactBooleanFuzzTests)
            all.Expand(pad);
            var grid = new BoxGrid(all, to.Count);
            for (int j = 0; j < to.Count; ++j) {
                var b = toBoxes[j]; b.Expand(pad); grid.Add(j, b);
            }
            Q delta2 = DeltaSquared();
            covDelta = Delta;
            covEps = (Math.Max(Math.Abs(all.Min.x), Math.Max(Math.Abs(all.Min.y), Math.Max(Math.Abs(all.Min.z),
                Math.Max(Math.Abs(all.Max.x), Math.Max(Math.Abs(all.Max.y), Math.Abs(all.Max.z)))))) + all.MaxDim) / (double)(1L << 40) + 1e-300;
            var toApprox = new TriApprox[to.Count];
            for (int j = 0; j < to.Count; ++j) toApprox[j] = ApproxTri(to[j]);
            for (int i = 0; i < from.Count; ++i) {
                var box = fromBoxes[i]; box.Expand(pad);
                List<int> cand = grid.Query(box).OrderBy(x => x == i ? -1 : x).ToList();
                if (Covered(from[i], cand, to, toApprox, delta2, SplitDepth)) continue;
                Uncovered++;
                // not certified: is a corner farther than Delta from every triangle (a definite violation)?
                foreach (Q[] corner in from[i]) {
                    bool near = false;
                    bool cOk = ApproxPoint(corner, out Vector3d ca);
                    foreach (int j in cand) {
                        CoverageTests++;
                        if (NearFiltered(corner, cOk, ca, to[j], toApprox[j], delta2, covDelta, covEps)) { near = true; break; }
                    }
                    if (near == false) return $"a corner of the {fromName} is farther than {Delta:R} from the {toName}";
                }
            }
            return "";
        }

        // p within Delta of triangle t: of one of its corners (cheap, and the case of the vertices the rounding moved by
        // ulps), else by the exact squared distance to the triangle
        static bool Near(Q[] p, Q[][] t, Q delta2)
        {
            foreach (Q[] corner in t) {
                Q[] d = VSub(p, corner);
                if (Q.Compare(VDot(d, d), delta2) <= 0) return true;
            }
            Q[] u = VSub(t[1], t[0]), v = VSub(t[2], t[0]);
            Q[] n = { (u[1] * v[2] - u[2] * v[1]).Reduce(), (u[2] * v[0] - u[0] * v[2]).Reduce(), (u[0] * v[1] - u[1] * v[0]).Reduce() };
            if (n[0].Sign == 0 && n[1].Sign == 0 && n[2].Sign == 0) {
                // a degenerate triangle (corners on a line or equal): the distance to its three edges
                for (int k = 0; k < 3; ++k)
                    if (Q.Compare(SegmentSquaredDistance(p, t[k], t[(k + 1) % 3]), delta2) <= 0) return true;
                return false;
            }
            return Q.Compare(SquaredDistance(p, t[0], t[1], t[2]), delta2) <= 0;
        }

        /// <summary> for tests: Near on exact points (p within delta of the closed triangle t) </summary>
        public static bool PointNearTriangle(ExactPoint3 p, ExactPoint3[] t, double delta)
        {
            ExactPoint3 d = FromDouble(new Vector3d(delta, 0, 0));
            Q q = new Q(d.X, d.W);
            return Near(Vec(p), t.Select(Vec).ToArray(), (q * q).Reduce());
        }

        /// <summary>
        /// for tests: PointNearTriangle through the double filters of C5 (reject by box, accept by a point inside the triangle),
        /// with the slack the checker derives from the largest coordinate it sees (scale); must give the exact answer
        /// </summary>
        public static bool PointNearTriangleFiltered(ExactPoint3 p, ExactPoint3[] t, double delta, double scale)
        {
            ExactPoint3 d = FromDouble(new Vector3d(delta, 0, 0));
            Q q = new Q(d.X, d.W);
            Q[][] tq = t.Select(Vec).ToArray();
            Q[] pq = Vec(p);
            bool pOk = ApproxPoint(pq, out Vector3d pa);
            return NearFiltered(pq, pOk, pa, tq, ApproxTri(tq), (q * q).Reduce(), delta, 2 * scale / (double)(1L << 40) + 1e-300);
        }

        static Q SegmentSquaredDistance(Q[] p, Q[] a, Q[] b)
        {
            Q[] ab = VSub(b, a), ap = VSub(p, a);
            Q l2 = VDot(ab, ab);
            if (l2.Sign == 0) return VDot(ap, ap);
            Q t = VDot(ap, ab);
            if (t.Sign <= 0) return VDot(ap, ap);
            if (Q.Compare(t, l2) >= 0) { Q[] bp = VSub(p, b); return VDot(bp, bp); }
            return PointOnSegment(p, a, ab, (t / l2).Reduce());
        }

        bool Covered(Q[][] t, List<int> cand, List<Q[][]> to, TriApprox[] toApprox, Q delta2, int depth)
        {
            var cOk = new bool[3]; var pv = new Vector3d[3];
            for (int k = 0; k < 3; ++k) cOk[k] = ApproxPoint(t[k], out pv[k]);
            foreach (int j in cand) {
                bool all = true;
                for (int k = 0; k < 3; ++k) {
                    CoverageTests++;
                    if (NearFiltered(t[k], cOk[k], pv[k], to[j], toApprox[j], delta2, covDelta, covEps) == false) { all = false; break; }
                }
                if (all) return true;
            }
            if (depth == 0) return false;
            Q[] Mid(Q[] a, Q[] b) => new[] { ((a[0] + b[0]) * new Q(1, 2)).Reduce(), ((a[1] + b[1]) * new Q(1, 2)).Reduce(), ((a[2] + b[2]) * new Q(1, 2)).Reduce() };
            Q[] ab = Mid(t[0], t[1]), bc = Mid(t[1], t[2]), ca = Mid(t[2], t[0]);
            return Covered(new[] { t[0], ab, ca }, cand, to, toApprox, delta2, depth - 1) && Covered(new[] { ab, t[1], bc }, cand, to, toApprox, delta2, depth - 1)
                && Covered(new[] { ca, bc, t[2] }, cand, to, toApprox, delta2, depth - 1) && Covered(new[] { ab, bc, ca }, cand, to, toApprox, delta2, depth - 1);
        }

        Q DeltaSquared()
        {
            ExactPoint3 d = FromDouble(new Vector3d(Delta, 0, 0));
            Q q = new Q(d.X, d.W);
            return (q * q).Reduce();
        }
    }
}
