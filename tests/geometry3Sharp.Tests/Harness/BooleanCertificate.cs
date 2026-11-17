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
    ///   intervals), and a bounded plane chain with zero boundary is zero. The rounded result R is closed and oriented
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
    /// meshes; a large flat region of many triangles would want a 2D grid), and C5 near-linear by a uniform grid. All
    /// signs are decided with BigInteger arithmetic on the exact points (no floating-point predicate decides anything).
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
        public bool CoverageOfExactSkipped;

        /// <summary>
        /// Runs a boolean with the recording on (and exact vertices) and returns it with its certificate data. configure
        /// may set more fields (SafeRounding, CullOutsideA, ...).
        /// </summary>
        public static BooleanCertificate Run(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean boolean, Action<ExactMeshBoolean> configure = null)
        {
            boolean = new ExactMeshBoolean(a, b, op) { RecordCertificate = true, ComputeExactVertices = true };
            configure?.Invoke(boolean);
            if (boolean.Compute() == false)
                throw new InvalidOperationException("boolean failed: " + boolean.Error);
            BooleanCertificate c = From(boolean);
            c.Inputs = new List<DMesh3> { a, b };
            return c;
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

        /// <summary> all checks: "" if the certificate holds, "n/a: ..." if it does not apply, else the first failure </summary>
        public string Check()
        {
            if (NotApplicableReason != null)
                return "n/a: " + NotApplicableReason;
            string r = CheckInputs();
            if (r != "") return "C0: " + r;
            r = CheckPieces();
            if (r != "") return "C1: " + r;
            r = CheckClassification();
            if (r != "") return "C3/C4: " + r;
            r = CheckResult();
            if (r != "") return "C5: " + r;
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
                        if (same == false) reversed++;
                    }
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
                foreach (int u in planeGroups[triPlane[src]]) {
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
            PrepareStacks();
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
            // (the rounding removes tiny and thin components (MeshRoundingRepair.RemovedComponents, a documented decision):
            // then the exact result is not within the bound of the result, and that direction is skipped)
            CoverageOfExactSkipped = RemovedComponents > 0;
            if (CoverageOfExactSkipped) return "";
            return Cover(eTris, eBoxes, rTris, rBoxes, "result before rounding", "result");
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
            var grid = new BoxGrid(all, to.Count);
            for (int j = 0; j < to.Count; ++j) {
                var b = toBoxes[j]; b.Expand(pad); grid.Add(j, b);
            }
            Q delta2 = DeltaSquared();
            for (int i = 0; i < from.Count; ++i) {
                var box = fromBoxes[i]; box.Expand(pad);
                List<int> cand = grid.Query(box).OrderBy(x => x == i ? -1 : x).ToList();
                if (Covered(from[i], cand, to, delta2, SplitDepth)) continue;
                Uncovered++;
                // not certified: is a corner farther than Delta from every triangle (a definite violation)?
                foreach (Q[] corner in from[i]) {
                    bool near = false;
                    foreach (int j in cand) {
                        CoverageTests++;
                        if (Near(corner, to[j], delta2)) { near = true; break; }
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

        bool Covered(Q[][] t, List<int> cand, List<Q[][]> to, Q delta2, int depth)
        {
            foreach (int j in cand) {
                bool all = true;
                foreach (Q[] corner in t) {
                    CoverageTests++;
                    if (Near(corner, to[j], delta2) == false) { all = false; break; }
                }
                if (all) return true;
            }
            if (depth == 0) return false;
            Q[] Mid(Q[] a, Q[] b) => new[] { ((a[0] + b[0]) * new Q(1, 2)).Reduce(), ((a[1] + b[1]) * new Q(1, 2)).Reduce(), ((a[2] + b[2]) * new Q(1, 2)).Reduce() };
            Q[] ab = Mid(t[0], t[1]), bc = Mid(t[1], t[2]), ca = Mid(t[2], t[0]);
            return Covered(new[] { t[0], ab, ca }, cand, to, delta2, depth - 1) && Covered(new[] { ab, t[1], bc }, cand, to, delta2, depth - 1)
                && Covered(new[] { ca, bc, t[2] }, cand, to, delta2, depth - 1) && Covered(new[] { ab, bc, ca }, cand, to, delta2, depth - 1);
        }

        Q DeltaSquared()
        {
            ExactPoint3 d = FromDouble(new Vector3d(Delta, 0, 0));
            Q q = new Q(d.X, d.W);
            return (q * q).Reduce();
        }
    }
}
