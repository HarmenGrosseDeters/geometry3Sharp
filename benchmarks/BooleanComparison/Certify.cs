// Research spike (docs/research/BOOLEAN_CERTIFIED_MODE_2026-11.md): what a certifying checker of ExactMeshBoolean costs
// per run. A prototype of stage 3 (C2a: an independent exact recheck that the arrangement's pieces are embedded), timed
// against the boolean and against the stages 1-2 checker (tests/.../Harness/BooleanCertificate.cs, linked into this
// project), on the procedural inputs of benchmarks/SolidBooleanBenchmark and a size series.
//
// The C2a prototype shares no code with the arrangement: its own box tree over the pieces, and one predicate (the sign of
// an orientation determinant of four exact points) in three variants: BigInteger only (as the stages 1-2 checker decides
// every sign), a floating-point filter with the BigInteger fallback, and floating point only (the idealised floor: no
// certified answer, only the time). The filter's error bound is a prototype bound, not a reviewed one.
//
// Usage: BooleanComparison certify [selftest | cost [reps] | scale [reps] | scale-c2a [reps] | removed]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using geometry3Sharp.Tests;
using Ops = g3.ExactMeshBoolean.Operations;

static class Certify
{
    // ------------------------------------------------------------------ procedural inputs (as SolidBooleanBenchmark)

    static DMesh3 Box(Vector3d c, Vector3d e) { return new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh(); }
    static DMesh3 FineBox(Vector3d c, Vector3d e, int n) { return new GridBox3Generator() { Box = new Box3d(c, e), EdgeVertices = n }.Generate().MakeDMesh(); }
    static DMesh3 Sphere(Vector3d c, double r, int n)
    {
        var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }
    static DMesh3 Cylinder(Vector3d c, double r, double h, int slices)
    {
        var m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }
    static DMesh3 Torus(Vector3d c, double R, double r, int nu, int nv)
    {
        var m = new DMesh3();
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                m.AppendVertex(c + new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), r * Math.Sin(v), (R + r * Math.Cos(v)) * Math.Sin(u)));
            }
        Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                int a = id(i, j), b = id(i + 1, j), cc = id(i + 1, j + 1), d = id(i, j + 1);
                m.AppendTriangle(a, d, cc); m.AppendTriangle(a, cc, b);
            }
        return m;
    }

    sealed class Case { public string Name; public List<DMesh3> Inputs; public Ops Op; }

    static List<Case> Cases()
    {
        var L = new List<Case>();
        Action<string, Ops, Func<DMesh3[]>> add = (n, op, f) => L.Add(new Case { Name = n, Op = op, Inputs = f().ToList() });
        Ops U = Ops.Union, I = Ops.Intersection, D = Ops.Difference;
        add("box+box overlap U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(0.7, 0.4, 0.2), new Vector3d(1, 1, 1)) });
        add("box+box face-touch U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(2, 0, 0), new Vector3d(1, 1, 1)) });
        add("box+box edge-touch U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Box(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1)) });
        add("sphere+sphere overlap U", U, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 24) });
        add("sphere&sphere overlap I", I, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 24) });
        add("sphere-sphere D", D, () => new[] { Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(1.0, 0.1, 0.05), 0.6, 24) });
        add("sphere+sphere tangent-ish U", U, () => new[] { Sphere(Vector3d.Zero, 1, 16), Sphere(new Vector3d(2.0, 0, 0), 1, 16) });
        add("box-cylinder D", D, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 48) });
        add("torus+cylinder U", U, () => new[] { Torus(Vector3d.Zero, 1, 0.3, 64, 24), Cylinder(new Vector3d(1, -0.8, 0), 0.25, 1.6, 48) });
        add("torus+torus linked U", U, () => new[] { Torus(Vector3d.Zero, 1, 0.3, 64, 24), Torus(new Vector3d(1, 0, 0), 1, 0.3, 64, 24) });
        add("fine box edge-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), FineBox(new Vector3d(2, 2, 0), new Vector3d(1, 1, 1), 16) });
        add("fine box vertex-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), FineBox(new Vector3d(2, 2, 2), new Vector3d(1, 1, 1), 16) });
        add("sphere on box face (pole touch) U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), Sphere(new Vector3d(0, 2, 0), 1, 25) });
        add("two cylinders line-touch U", U, () => new[] { Cylinder(new Vector3d(-0.5, 0, 0), 0.5, 2, 64), Cylinder(new Vector3d(0.5, 0, 0), 0.5, 2, 64) });
        add("cylinder on box, partial face-touch U", U, () => new[] { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), 16), Cylinder(new Vector3d(0.5, 1, 0), 0.5, 1, 64) });
        add("3 spheres U", U, () => new[] { Sphere(Vector3d.Zero, 1, 20), Sphere(new Vector3d(1, 0.2, 0), 1, 20), Sphere(new Vector3d(0.5, 1, 0.1), 1, 20) });
        add("box+sphere+cyl U", U, () => new[] { Box(Vector3d.Zero, new Vector3d(1, 1, 1)), Sphere(new Vector3d(1, 1, 1), 0.8, 24), Cylinder(new Vector3d(-0.5, -1.5, 0), 0.3, 3, 32) });
        return L;
    }

    // a size series: the same two operations at growing resolution, and a face-on-face pair (coplanar stacks)
    static List<Case> ScaleCases()
    {
        var L = new List<Case>();
        foreach (int n in new[] { 16, 32, 64, 128 })
            L.Add(new Case { Name = $"sphere+sphere U n={n}", Op = Ops.Union, Inputs = new List<DMesh3> { Sphere(Vector3d.Zero, 1, n), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, n) } });
        foreach (int n in new[] { 32, 64, 128, 256 })
            L.Add(new Case { Name = $"torus-torus D nu={n}", Op = Ops.Difference, Inputs = new List<DMesh3> { Torus(Vector3d.Zero, 1, 0.3, n, n / 2), Torus(new Vector3d(1, 0, 0), 1, 0.3, n, n / 2) } });
        foreach (int n in new[] { 8, 16, 32 })
            L.Add(new Case { Name = $"fine box face-on-face U n={n}", Op = Ops.Union, Inputs = new List<DMesh3> { FineBox(Vector3d.Zero, new Vector3d(1, 1, 1), n), FineBox(new Vector3d(1.5, 0.25, 0.125), new Vector3d(1, 1, 1), n + 1) } });
        return L;
    }

    // ------------------------------------------------------------------ the orientation predicate, three variants

    enum Mode { Exact, Filtered, FloatOnly }

    sealed class Kernel
    {
        public ExactPoint3[] X;     // exact points by id (homogeneous, W > 0)
        public Vector3d[] D;        // their approximations (relative error per coordinate below Rel)
        public Mode Mode;
        /// <summary>
        /// per point: it is an input vertex, and In holds its input coordinates (doubles, exact). The grid is the input
        /// scaled by a power of two and shifted, so orientations of input vertices are the same in both; null: none known
        /// </summary>
        public bool[] IsDouble;
        public Vector3d[] In;
        /// <summary> per point: the source triangles of the pieces it is a corner of: the point lies in their planes (C1 checks it per piece) </summary>
        public int[][] Sources;
        public long Calls, ExactCalls, AdaptiveCalls, KnownZeros;

        public bool AllDouble(int a, int b, int c, int d) => IsDouble != null && IsDouble[a] && IsDouble[b] && IsDouble[c] && IsDouble[d];
        public bool KnownOnPlaneOf(int point, int source)
        {
            if (Mode == Mode.Exact || Sources == null || source < 0) return false;
            int[] s = Sources[point];
            for (int i = 0; i < s.Length; ++i) if (s[i] == source) { KnownZeros++; return true; }
            return false;
        }
        const double Rel = 1.0 / (1L << 50);        // ExactPoint3.ApproxVector3d: below 4 * 2^-52
        const double U = 1.0 / (1L << 53);

        static BigInteger[] Sub(ExactPoint3 a, ExactPoint3 b)
        {
            if (a.W.IsOne && b.W.IsOne) return new[] { a.X - b.X, a.Y - b.Y, a.Z - b.Z };
            return new[] { a.X * b.W - b.X * a.W, a.Y * b.W - b.Y * a.W, a.Z * b.W - b.Z * a.W };
        }
        public static BigInteger[] Cross(BigInteger[] u, BigInteger[] v) =>
            new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
        public static BigInteger Dot(BigInteger[] u, BigInteger[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
        public BigInteger[] Diff(int a, int b) => Sub(X[a], X[b]);

        public int ExactO3(int a, int b, int c, int d)
        {
            ExactCalls++;
            return Dot(Sub(X[b], X[a]), Cross(Sub(X[c], X[a]), Sub(X[d], X[a]))).Sign;
        }

        /// <summary> the sign of det[b - a, c - a, d - a]; equal ids give 0 without arithmetic </summary>
        public int O3(int a, int b, int c, int d)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d) return 0;
            Calls++;
            if (Mode == Mode.Exact) return ExactO3(a, b, c, d);
            // input vertices: the library's adaptive predicate on the doubles is exact there (ExactPredicates, a basic
            // predicate tested on its own, with the same sign convention)
            if (AllDouble(a, b, c, d)) { AdaptiveCalls++; return ExactPredicates.Orient3d(In[a], In[b], In[c], In[d]); }
            Vector3d pa = D[a], pb = D[b], pc = D[c], pd = D[d];
            double bx = pb.x - pa.x, by = pb.y - pa.y, bz = pb.z - pa.z;
            double cx = pc.x - pa.x, cy = pc.y - pa.y, cz = pc.z - pa.z;
            double dx = pd.x - pa.x, dy = pd.y - pa.y, dz = pd.z - pa.z;
            double det = bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx);
            if (Mode == Mode.FloatOnly) return Math.Sign(det);
            // the error of each difference: the errors of its two ends and its own rounding
            double ax = Math.Abs(pa.x), ay = Math.Abs(pa.y), az = Math.Abs(pa.z);
            double abx = Math.Abs(bx), aby = Math.Abs(by), abz = Math.Abs(bz);
            double acx = Math.Abs(cx), acy = Math.Abs(cy), acz = Math.Abs(cz);
            double adx = Math.Abs(dx), ady = Math.Abs(dy), adz = Math.Abs(dz);
            double p = abx * (acy * adz + acz * ady) + aby * (acx * adz + acz * adx) + abz * (acx * ady + acy * adx);
            abx += Rel * (ax + Math.Abs(pb.x)) + U * abx; aby += Rel * (ay + Math.Abs(pb.y)) + U * aby; abz += Rel * (az + Math.Abs(pb.z)) + U * abz;
            acx += Rel * (ax + Math.Abs(pc.x)) + U * acx; acy += Rel * (ay + Math.Abs(pc.y)) + U * acy; acz += Rel * (az + Math.Abs(pc.z)) + U * acz;
            adx += Rel * (ax + Math.Abs(pd.x)) + U * adx; ady += Rel * (ay + Math.Abs(pd.y)) + U * ady; adz += Rel * (az + Math.Abs(pd.z)) + U * adz;
            double pp = abx * (acy * adz + acz * ady) + aby * (acx * adz + acz * adx) + abz * (acx * ady + acy * adx);
            // the permanent of the widened entries bounds the determinant of any entries within the widths; the rest is
            // the rounding of the determinant and of the two permanents (prototype bound, generous, not reviewed)
            double err = (pp - p) + 32 * U * pp;
            // (no underflow term in the bound: tiny or non-finite magnitudes go to the exact stage)
            if (!(pp > 1e-200 && pp < 1e200)) return ExactO3(a, b, c, d);
            if (det > err) return 1;
            if (-det > err) return -1;
            return ExactO3(a, b, c, d);
        }
    }

    // ------------------------------------------------------------------ the pair test (C2a)

    /// <summary>
    /// Do the triangles p and q (point ids) meet anywhere beyond the corners they share by id: not in a shared corner,
    /// not along a shared edge? Three shared corners (the same triangle twice) are reported as a duplicate, not a
    /// violation. By the segments: two triangles in different planes meet in a segment whose ends lie on an edge of one
    /// inside the other, so they meet only where they share iff every edge of each that is not shared meets the other at
    /// most in an end that is a shared corner; coplanar ones likewise, plus the fold of two triangles on one side of a
    /// shared edge.
    /// </summary>
    sealed class PairTest
    {
        public Kernel K;
        public long Coplanar, CoplanarExact, Duplicates, EarlyOut;
        /// <summary> points to try as the apex of a plane (input vertices spread over the meshes); null: coplanar pairs by BigInteger </summary>
        public int[] ApexCandidates;
        readonly Dictionary<int, int> apexOf = new Dictionary<int, int>();

        public bool Violation(int[] p, int[] q, int sourceP = -1, int sourceQ = -1)
        {
            int shared = 0;
            for (int i = 0; i < 3; ++i) if (p[i] == q[0] || p[i] == q[1] || p[i] == q[2]) shared++;
            if (shared == 3) { Duplicates++; return false; }
            int[] sp = new int[3], sq = new int[3];     // sides of p's corners against q's plane, of q's against p's
            int pos = 0, neg = 0;
            for (int i = 0; i < 3; ++i) { sp[i] = K.KnownOnPlaneOf(p[i], sourceQ) ? 0 : K.O3(q[0], q[1], q[2], p[i]); if (sp[i] > 0) pos++; else if (sp[i] < 0) neg++; }
            if (pos == 3 || neg == 3) { EarlyOut++; return false; }
            if (pos == 0 && neg == 0) { Coplanar++; return CoplanarViolation(p, q, sourceP); }
            pos = neg = 0;
            for (int i = 0; i < 3; ++i) { sq[i] = K.KnownOnPlaneOf(q[i], sourceP) ? 0 : K.O3(p[0], p[1], p[2], q[i]); if (sq[i] > 0) pos++; else if (sq[i] < 0) neg++; }
            if (pos == 3 || neg == 3) { EarlyOut++; return false; }
            return EdgesViolate(p, sp, q) || EdgesViolate(q, sq, p);
        }

        static bool Has(int[] t, int v) => t[0] == v || t[1] == v || t[2] == v;
        static bool Mixed(int a, int b, int c) => (a > 0 || b > 0 || c > 0) && (a < 0 || b < 0 || c < 0);

        // the edges of p against q (not coplanar; s: the sides of p's corners against q's plane)
        bool EdgesViolate(int[] p, int[] s, int[] q)
        {
            for (int i = 0; i < 3; ++i) {
                int j = (i + 1) % 3, k = (i + 2) % 3, a = p[i], b = p[j];
                bool sa = Has(q, a), sb = Has(q, b);
                if (sa && sb) continue;                                 // a shared edge
                if (s[i] * s[j] > 0) continue;
                if (s[i] * s[j] < 0) {
                    // crosses q's plane between its ends: in the closed triangle iff the three edges of q are not on
                    // both sides of the line a b
                    if (Mixed(K.O3(a, b, q[0], q[1]), K.O3(a, b, q[1], q[2]), K.O3(a, b, q[2], q[0])) == false) return true;
                } else if (s[i] == 0 && s[j] != 0) {
                    if (sa == false && InTriangle(a, q, b)) return true;
                } else if (s[j] == 0 && s[i] != 0) {
                    if (sb == false && InTriangle(b, q, a)) return true;
                } else {
                    // the edge lies in q's plane: in that plane, seen from p's third corner (off the plane)
                    if (SegmentViolates(a, b, sa, sb, q, p[k])) return true;
                }
            }
            return false;
        }

        // x (in q's plane) in the closed triangle q, seen from the apex (off the plane)
        bool InTriangle(int x, int[] q, int apex) =>
            Mixed(K.O3(q[0], q[1], x, apex), K.O3(q[1], q[2], x, apex), K.O3(q[2], q[0], x, apex)) == false;

        // the segment a b in q's plane: does it meet q beyond its ends that are corners of q (sa, sb)? Orientations in
        // the plane by the apex.
        bool SegmentViolates(int a, int b, bool sa, bool sb, int[] q, int apex)
        {
            if (sa == false && InTriangle(a, q, apex)) return true;
            if (sb == false && InTriangle(b, q, apex)) return true;
            int[] o = new int[3];
            for (int j = 0; j < 3; ++j) o[j] = K.O3(a, b, q[j], apex);
            for (int j = 0; j < 3; ++j) {
                int c = q[j], d = q[(j + 1) % 3];
                // a corner of q strictly inside the segment: on its line, and a, b strictly on both sides of a line
                // through the corner (the edge to the next corner, or to the one before if that edge is on the line too)
                if (o[j] == 0 && c != a && c != b) {
                    int e = o[(j + 1) % 3] != 0 ? d : q[(j + 2) % 3];
                    if (K.O3(c, e, a, apex) * K.O3(c, e, b, apex) < 0) return true;
                }
                // a proper crossing with the edge c d
                if (o[j] * o[(j + 1) % 3] < 0 && K.O3(c, d, a, apex) * K.O3(c, d, b, apex) < 0) return true;
            }
            return false;
        }

        // coplanar triangles: exact, orientations in the plane by p's normal (no filter in the prototype)
        bool CoplanarViolation(int[] p, int[] q, int sourceP)
        {
            Func<int, int, int, int> o2 = null;
            if (K.Mode != Mode.Exact && ApexCandidates != null) {
                // a point off the plane (kept per source triangle): the orientation in the plane is the one seen from it
                if (apexOf.TryGetValue(sourceP, out int apex) == false) {
                    apex = -1;
                    foreach (int c in ApexCandidates) if (K.O3(p[0], p[1], p[2], c) != 0) { apex = c; break; }
                    if (sourceP >= 0) apexOf[sourceP] = apex;
                }
                if (apex >= 0) {
                    int s = K.O3(p[0], p[1], p[2], apex);
                    o2 = (x, y, z) => s * K.O3(x, y, z, apex);
                }
            }
            if (o2 == null) {
                CoplanarExact++;
                BigInteger[] n = Kernel.Cross(K.Diff(p[1], p[0]), K.Diff(p[2], p[0]));
                o2 = (x, y, z) => (x == y || x == z || y == z) ? 0 : Kernel.Dot(Kernel.Cross(K.Diff(y, x), K.Diff(z, x)), n).Sign;
            }
            for (int i = 0; i < 3; ++i) {
                int a = p[i], b = p[(i + 1) % 3];
                if (Has(q, a) && Has(q, b)) {
                    // a shared edge: the third corners on the same side fold onto each other
                    int qc = q[0] != a && q[0] != b ? q[0] : q[1] != a && q[1] != b ? q[1] : q[2];
                    if (o2(a, b, p[(i + 2) % 3]) * o2(a, b, qc) > 0) return true;
                }
            }
            return SegmentsViolate2(p, q, o2) || SegmentsViolate2(q, p, o2);
        }

        static bool SegmentsViolate2(int[] p, int[] q, Func<int, int, int, int> o2)
        {
            Func<int, bool> inQ = x => Mixed(o2(q[0], q[1], x), o2(q[1], q[2], x), o2(q[2], q[0], x)) == false;
            for (int i = 0; i < 3; ++i) {
                int a = p[i], b = p[(i + 1) % 3];
                bool sa = Has(q, a), sb = Has(q, b);
                if (sa && sb) continue;
                if (sa == false && inQ(a)) return true;
                if (sb == false && inQ(b)) return true;
                int[] o = { o2(a, b, q[0]), o2(a, b, q[1]), o2(a, b, q[2]) };
                for (int j = 0; j < 3; ++j) {
                    int c = q[j], d = q[(j + 1) % 3];
                    if (o[j] == 0 && c != a && c != b) {
                        int e = o[(j + 1) % 3] != 0 ? d : q[(j + 2) % 3];
                        if (o2(c, e, a) * o2(c, e, b) < 0) return true;
                    }
                    if (o[j] * o[(j + 1) % 3] < 0 && o2(c, d, a) * o2(c, d, b) < 0) return true;
                }
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ the broad phase: a box tree of its own

    sealed class BoxTree
    {
        readonly double[] lo, hi;       // per item: 3 + 3
        readonly int[] items;
        int[] nodeStart, nodeEnd, nodeLeft, nodeRight;
        double[] nodeBox;
        int nodes;
        const int Leaf = 4;

        public BoxTree(double[] lo, double[] hi, int n)
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
                // (by the box centres on the axis; any split is a valid tree, and the pairs are sorted afterwards)
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
            if (lb || (la == false && nodeEnd[a] - nodeStart[a] >= nodeEnd[b] - nodeStart[b])) { Join(nodeLeft[a], b, group, pairs); Join(nodeRight[a], b, group, pairs); } else { Join(a, nodeLeft[b], group, pairs); Join(a, nodeRight[b], group, pairs); }
        }
    }

    // ------------------------------------------------------------------ C2a over the pieces of an arrangement

    sealed class Embedding
    {
        public int[][] Tri;         // per piece: point ids
        public int[] Source;
        public ExactPoint3[] X;
        public Vector3d[] D;
        public bool[] IsDouble;
        public Vector3d[] In;
        public int[][] Sources;
        public int[] ApexCandidates;
        public List<(int, int)> Pairs;
        public double PointsMs, ApproxMs, BroadMs;

        public static Embedding From(ExactMeshArrangement arr)
        {
            var e = new Embedding();
            int n = arr.Output.Count, maxId = 0;
            e.Tri = new int[n][]; e.Source = new int[n];
            for (int i = 0; i < n; ++i) {
                var t = arr.Output[i];
                e.Tri[i] = new[] { t.A, t.B, t.C }; e.Source[i] = t.Source;
                maxId = Math.Max(maxId, Math.Max(t.A, Math.Max(t.B, t.C)));
            }
            // the witness: the exact points of the pieces' corners (the arrangement computes most of them only here)
            var sw = Stopwatch.StartNew();
            e.X = new ExactPoint3[maxId + 1];
            var used = new bool[maxId + 1];
            foreach (int[] t in e.Tri) foreach (int v in t) if (used[v] == false) { used[v] = true; e.X[v] = arr.Exact(v); }
            e.PointsMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            e.D = new Vector3d[maxId + 1];
            e.IsDouble = new bool[maxId + 1]; e.In = new Vector3d[maxId + 1];
            var apex = new List<int>();
            for (int v = 0; v <= maxId; ++v)
                if (used[v]) {
                    ExactPoint3 x = e.X[v];
                    e.D[v] = x.ApproxVector3d();
                    if (x.W.IsOne == false) continue;
                    // an integer grid point: an input vertex iff (x + Center) 2^ScaleExp is a double (checked by the way back)
                    bool isDouble = true;
                    for (int k = 0; k < 3 && isDouble; ++k) {
                        BigInteger g = x[k] + arr.Center[k];
                        double d = ExactPoint3.ScaleB((double)g, arr.ScaleExp);
                        isDouble = (BigInteger)(double)g == g && (d != 0 || g.IsZero) && double.IsInfinity(d) == false && ExactPoint3.ScaleB(d, -arr.ScaleExp) == (double)g;
                        e.In[v][k] = d;
                    }
                    e.IsDouble[v] = isDouble;
                    if (isDouble && (apex.Count < 16 && v % Math.Max(1, maxId / 16) == 0)) apex.Add(v);
                }
            e.ApexCandidates = apex.ToArray();
            var src = new List<int>[maxId + 1];
            for (int i = 0; i < n; ++i)
                foreach (int v in e.Tri[i]) {
                    if (src[v] == null) src[v] = new List<int>(4);
                    if (src[v].Contains(e.Source[i]) == false) src[v].Add(e.Source[i]);
                }
            e.Sources = new int[maxId + 1][];
            for (int v = 0; v <= maxId; ++v) if (src[v] != null) e.Sources[v] = src[v].ToArray();
            e.ApproxMs = sw.Elapsed.TotalMilliseconds;
            return e;
        }

        /// <summary> the pairs of pieces of different source triangles whose boxes (widened beyond the approximation's error) meet </summary>
        public void BroadPhase()
        {
            var sw = Stopwatch.StartNew();
            int n = Tri.Length;
            double[] lo = new double[3 * n], hi = new double[3 * n];
            for (int i = 0; i < n; ++i)
                for (int k = 0; k < 3; ++k) {
                    double a = D[Tri[i][0]][k], b = D[Tri[i][1]][k], c = D[Tri[i][2]][k];
                    double l = Math.Min(a, Math.Min(b, c)), h = Math.Max(a, Math.Max(b, c));
                    lo[3 * i + k] = l - Math.Abs(l) * (1.0 / (1L << 49)) - double.Epsilon;
                    hi[3 * i + k] = h + Math.Abs(h) * (1.0 / (1L << 49)) + double.Epsilon;
                }
            var pairs = new List<(int, int)>();
            new BoxTree(lo, hi, n).SelfJoin(Source, pairs);
            pairs.Sort();
            Pairs = pairs;
            BroadMs = sw.Elapsed.TotalMilliseconds;
        }

        public (int violations, int first, PairTest test, double ms) Check(Mode mode)
        {
            var test = new PairTest { K = new Kernel { X = X, D = D, Mode = mode, IsDouble = IsDouble, In = In, Sources = Sources }, ApexCandidates = ApexCandidates };
            var sw = Stopwatch.StartNew();
            int bad = 0, first = -1;
            for (int i = 0; i < Pairs.Count; ++i) {
                int x = Pairs[i].Item1, y = Pairs[i].Item2;
                if (test.Violation(Tri[x], Tri[y], Source[x], Source[y])) { bad++; if (first < 0) first = i; }
            }
            return (bad, first, test, sw.Elapsed.TotalMilliseconds);
        }
    }

    // ------------------------------------------------------------------ measurements

    static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

    static double TimeMs(Action a, int reps)
    {
        var t = new List<double>();
        for (int i = 0; i < reps; ++i) { var sw = Stopwatch.StartNew(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        return Median(t);
    }

    static ExactMeshBoolean Boolean(Case c, bool record)
    {
        var b = new ExactMeshBoolean(c.Inputs, c.Op) { AllowOpenMeshes = true };
        if (record) {
            b.RecordCertificate = true; b.ComputeExactVertices = true;
            if (c.Op == Ops.Intersection) b.CullOutsideA = false;       // the stages 1-2 checker's class (see its comment)
        }
        if (b.Compute() == false) throw new InvalidOperationException(c.Name + ": " + b.Error);
        return b;
    }

    static string F(double ms) => ms.ToString(ms < 10 ? "0.00" : ms < 100 ? "0.0" : "0", CultureInfo.InvariantCulture);

    static int Cost(List<Case> cases, int reps, bool stages12)
    {
        Console.WriteLine($"# {Environment.ProcessorCount} logical cores, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; times in ms, medians of {reps} warm runs (the BigInteger columns: one run)");
        Console.WriteLine("case | in tris | pieces | boolean | boolean serial | recording | points | approx | broad | pairs | coplanar (BigInteger) | dup | C2a BigInteger | C2a filtered | BigInteger / adaptive / filter calls, known zeros | C2a float floor | NotClean(R) | "
            + (stages12 ? "C0 | C1 | C3+C4 | C5 | " : "") + "verdict");
        Boolean(cases[0], true); Boolean(cases[0], false);
        int failed = 0;
        foreach (Case c in cases) {
            for (int w = 0; w < 3; ++w) { Boolean(c, false); Boolean(c, true); }
            double tBool = TimeMs(() => Boolean(c, false), reps);
            bool was = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            double tSerial;
            try { tSerial = TimeMs(() => Boolean(c, false), reps); } finally { gParallel.SerialOnThisThread = was; }
            double tRec = TimeMs(() => Boolean(c, true), reps);
            ExactMeshBoolean b = Boolean(c, true);
            // (a fresh boolean per repetition for the points: Exact(id) caches)
            var pts = new List<double>(); var apx = new List<double>(); Embedding e = null;
            for (int r = 0; r < reps; ++r) { e = Embedding.From(Boolean(c, true).Arrangement); pts.Add(e.PointsMs); apx.Add(e.ApproxMs); }
            double tBroad = TimeMs(() => e.BroadPhase(), reps);
            var exact = e.Check(Mode.Exact);
            (int violations, int first, PairTest test, double ms) filt = default;
            double tFilt = TimeMs(() => filt = e.Check(Mode.Filtered), reps);
            double tFloat = TimeMs(() => e.Check(Mode.FloatOnly), reps);
            string verdict = exact.violations == 0 && filt.violations == 0 ? "embedded" : $"VIOLATIONS exact {exact.violations} filtered {filt.violations}";
            if (exact.violations != filt.violations || exact.first != filt.first) verdict += " MISMATCH";
            string notClean = "";
            double tClean = TimeMs(() => notClean = MeshRobustRepair.NotCleanReason(b.Result), reps);
            if (notClean != "") verdict += $", result not clean: {notClean} (Contacts {b.Contacts})";
            string s12 = "";
            if (stages12) {
                BooleanCertificate cert = BooleanCertificate.From(b);
                if (cert.NotApplicableReason != null) s12 = "n/a | n/a | n/a | n/a | ";
                else {
                    var sw = Stopwatch.StartNew();
                    string r0 = cert.CheckInputs(); double t0 = sw.Elapsed.TotalMilliseconds; sw.Restart();
                    string r1 = cert.CheckPieces(); double t1 = sw.Elapsed.TotalMilliseconds; sw.Restart();
                    string r3 = cert.CheckClassification(); double t3 = sw.Elapsed.TotalMilliseconds; sw.Restart();
                    string r5 = cert.CheckResult(); double t5 = sw.Elapsed.TotalMilliseconds;
                    s12 = $"{F(t0)} | {F(t1)} | {F(t3)} | {F(t5)} | ";
                    if (r0 + r1 + r3 + r5 != "") verdict += $", stages 1-2: '{r0}' '{r1}' '{r3}' '{r5}'";
                    else if (cert.Uncovered != 0) verdict += $", {cert.Uncovered} uncovered";
                }
            }
            Console.WriteLine($"{c.Name} | {c.Inputs.Sum(m => m.TriangleCount)} | {e.Tri.Length} | {F(tBool)} | {F(tSerial)} | {F(tRec - tBool)} | {F(Median(pts))} | {F(Median(apx))} | {F(tBroad)} | {e.Pairs.Count} | {filt.test.Coplanar} ({filt.test.CoplanarExact}) | {exact.test.Duplicates} | "
                + $"{F(exact.ms)} | {F(tFilt)} | {filt.test.K.ExactCalls} / {filt.test.K.AdaptiveCalls} / {filt.test.K.Calls - filt.test.K.AdaptiveCalls}, {filt.test.K.KnownZeros} | {F(tFloat)} | {F(tClean)} | {s12}{verdict}");
            if (verdict.Contains("VIOLATIONS") || verdict.Contains("MISMATCH") || verdict.Contains("stages 1-2") || verdict.Contains("uncovered")) failed++;
        }
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ self test of the prototype

    // (1) the pair test against the library's ExactTriangleIntersection.IntersectsBeyondShared on random triangles of a
    // small integer lattice (shared corners, coplanar and touching pairs are frequent there); (2) the classic failure,
    // a missed intersection: every split source triangle is put back unsplit as one more piece, and C2a must report it.
    static int SelfTest()
    {
        var rng = new Random(20261122);
        int mismatches = 0, tested = 0, violating = 0;
        foreach (int range in new[] { 3, 5, 9 })
            for (int it = 0; it < 200000; ++it) {
                bool flat = rng.Next(4) == 0;
                var pts = new Vector3d[6];
                for (int i = 0; i < 6; ++i) pts[i] = new Vector3d(rng.Next(range), rng.Next(range), flat ? 0 : rng.Next(range));
                int share = rng.Next(3), mask = rng.Next(64);
                int[] p = { 0, 1, 2 }, q = { 3, 4, 5 };
                for (int s = 0; s < share; ++s) q[s] = p[(s + rng.Next(3)) % 3];
                if (q[0] == q[1]) continue;
                // equal positions are equal points (the arrangement merges them): one id per position
                var idOf = new Dictionary<Vector3d, int>();
                int[] canon = new int[6];
                for (int i = 0; i < 6; ++i) { if (idOf.TryGetValue(pts[i], out int id) == false) idOf[pts[i]] = id = i; canon[i] = id; }
                p = p.Select(v => canon[v]).ToArray(); q = q.Select(v => canon[v]).ToArray();
                var A = new Triangle3d(pts[p[0]], pts[p[1]], pts[p[2]]); var B = new Triangle3d(pts[q[0]], pts[q[1]], pts[q[2]]);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                if (p.Count(v => q.Contains(v)) == 3) continue;
                bool expected = ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(p[0], p[1], p[2]), ref A, new Index3i(q[0], q[1], q[2]), ref B);
                var X = pts.Select(v => ExactPoint3.FromInteger((BigInteger)v.x, (BigInteger)v.y, (BigInteger)v.z)).ToArray();
                foreach (Mode mode in new[] { Mode.Exact, Mode.Filtered })
                    foreach (int adaptive in new[] { 0, 1, 2 }) {
                        // (no points, all points, a random subset of the points on the adaptive path: the paths must agree in sign)
                        bool[] isDouble = adaptive == 0 ? null : Enumerable.Range(0, 6).Select(i => adaptive == 1 || (mask >> i & 1) != 0).ToArray();
                        var test = new PairTest { K = new Kernel { X = X, D = pts, In = pts, Mode = mode, IsDouble = isDouble }, ApexCandidates = adaptive == 2 ? new[] { 0, 1, 2, 3, 4, 5 } : null };
                        if (test.Violation(p, q) != expected || test.Violation(q, p) != expected) mismatches++;
                    }
                tested++; if (expected) violating++;
            }
        Console.WriteLine($"pair test against ExactTriangleIntersection.IntersectsBeyondShared: {tested} pairs ({violating} meeting beyond shared), {mismatches} mismatches");

        int unsplit = 0, caught = 0, baseline = 0;
        foreach (Case c in Cases()) {
            ExactMeshArrangement arr = Boolean(c, true).Arrangement;
            Embedding e = Embedding.From(arr);
            e.BroadPhase();
            baseline += e.Check(Mode.Filtered).violations;
            var count = new Dictionary<int, int>();
            foreach (int s in e.Source) count[s] = count.TryGetValue(s, out int k) ? k + 1 : 1;
            foreach (var kv in count.Where(kv => kv.Value > 1).OrderBy(kv => kv.Key).Take(40)) {
                // the unsplit source triangle as one more piece of a source of its own
                Index3i corners = arr.Triangles[kv.Key].Corners;
                var m = new Embedding { X = e.X, D = e.D, IsDouble = e.IsDouble, In = e.In, Sources = e.Sources, ApexCandidates = e.ApexCandidates, Tri = e.Tri.Append(new[] { corners.a, corners.b, corners.c }).ToArray(), Source = e.Source.Append(-1).ToArray() };
                // (its own pieces overlap it by construction: only the pieces of other sources count)
                m.BroadPhase();
                m.Pairs = m.Pairs.Where(pr => pr.Item2 == m.Tri.Length - 1 && m.Source[pr.Item1] != kv.Key).ToList();
                unsplit++;
                if (m.Check(Mode.Filtered).violations > 0 && m.Check(Mode.Exact).violations > 0) caught++;
            }
        }
        Console.WriteLine($"missed intersections (a split source triangle put back unsplit): {caught} of {unsplit} caught; violations without the mutation: {baseline}");
        return mismatches == 0 && caught == unsplit && baseline == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ components the rounding removes

    // Two cases of the SolidBooleanPipeline audit (docs/research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md, F1): the boolean
    // succeeds while SafeRounding's removal of tiny and thin components drops a solid of the exact result. What the
    // stages 1-2 checker says: as built (the removed components exempt from the coverage, counted in ExemptComponents),
    // and strict (no exemption: every component of the exact result must be covered by the result).
    static int Removed()
    {
        Func<Vector3d, Vector3d, DMesh3> box = (lo, hi) => Box(0.5 * (lo + hi), 0.5 * (hi - lo));
        Func<DMesh3, DMesh3> turn = m => { var r = new DMesh3(m); MeshTransforms.Rotate(r, Vector3d.Zero, new Quaterniond(new Vector3d(1, 2, 3).Normalized, 40)); return r; };
        var cases = new List<Case>();
        foreach (int k in new[] { 8, 9 })
            cases.Add(new Case { Name = $"box [0,1024]^3 + cube of edge 2^-{k} at x = 1100, U", Op = Ops.Union,
                Inputs = new List<DMesh3> { box(Vector3d.Zero, new Vector3d(1024, 1024, 1024)), box(new Vector3d(1100, 0, 0), new Vector3d(1100, 0, 0) + Math.Pow(2, -k) * Vector3d.One) } });
        foreach (int k in new[] { 10, 15 })
            cases.Add(new Case { Name = $"[0,2]^3 & [2-h,4]x[0,2]^2, h = 2^-{k}, turned, I", Op = Ops.Intersection,
                Inputs = new List<DMesh3> { turn(box(Vector3d.Zero, new Vector3d(2, 2, 2))), turn(box(new Vector3d(2 - Math.Pow(2, -k), 0, 0), new Vector3d(4, 2, 2))) } });
        Console.WriteLine("case | status | result tris | exact result tris | RemovedComponents | checker as built | exempt components | checker strict (no exemption)");
        foreach (Case c in cases) {
            ExemptOrStrict(c, false, out ExactMeshBoolean b, out BooleanCertificate cert, out string asBuilt);
            ExemptOrStrict(c, true, out _, out _, out string strict);
            Console.WriteLine($"{c.Name} | {b.Status} | {b.Result.TriangleCount} | {b.CertificateAssembled.TriangleCount} | {b.Rounding?.RemovedComponents ?? 0} | {asBuilt} | {cert.ExemptComponents} | {strict}");
        }
        return 0;
    }

    static void ExemptOrStrict(Case c, bool strict, out ExactMeshBoolean b, out BooleanCertificate cert, out string verdict)
    {
        b = Boolean(c, true);
        cert = BooleanCertificate.From(b);
        if (strict) cert.RemovedComponents = 0;
        verdict = cert.Check();
        if (verdict == "") verdict = cert.Uncovered == 0 ? "certified" : $"{cert.Uncovered} triangles not covered";
    }

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string what = args.Length > 0 ? args[0] : "cost";
        int reps = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
        switch (what) {
            case "selftest": return SelfTest();
            case "cost": return Cost(Cases(), reps, true);
            case "scale": return Cost(ScaleCases(), reps, true);
            case "scale-c2a": return Cost(ScaleCases(), reps, false);
            case "removed": return Removed();
            case "size": {
                // (docs/research/CERTIFYING_BOOLEANS_2026-11.md: certificate size and checker time; CertifySize.cs)
                int seeds = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 4;
                var all = Cases().Concat(ScaleCases().Where(c => c.Name.EndsWith("n=64") || c.Name.EndsWith("nu=128") || c.Name.EndsWith("n=16")))
                    .Select(c => (c.Name, c.Inputs, c.Op)).ToList();
                return CertifySize.Run(CertifySize.FuzzCases(seeds).Concat(all).ToList(), reps);
            }
        }
        Console.Error.WriteLine("usage: certify [selftest | cost [reps] | scale [reps] | scale-c2a [reps] | removed | size [reps] [seeds]]");
        return 1;
    }
}
