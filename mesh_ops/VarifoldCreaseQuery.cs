// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Is there a crease of the input at a point, asked without the input's connectivity (docs/design/VARIFOLD_CREASE_INPUT.md):
    /// the input as an unoriented varifold, a measure on position x unoriented plane (Buet, Leonardi, Masnou, ARMA 2017 and
    /// Nonlinear Analysis 2022), so an unwelded, duplicated, flipped or T-junctioned soup is the same measure as its clean mesh.
    /// The measure: every triangle cut into congruent pieces no longer than Radius / 2, the edge-midpoint rule on each (exact
    /// for degree 2; slivers halved at the longest edge first), each point with its mass and its triangle's unit normal (its
    /// sign carries no meaning). A query at x0 with two plane normals nA, nB takes the kernel (1 - r^2 / Radius^2)^4 over the
    /// points within Radius, splits the mass into the planes within 20 degrees of nA and of nB, and accepts when each holds
    /// at least MinFraction of the mass and at most MaxBetweenFraction lies between them (no curved surface), each is
    /// planar (rms at most MaxRmsFraction of the radius) and its mass-weighted plane passes within tol plus three times its
    /// rms of x0 (both sheets meet there), and the dihedral of the two half-planes (from their line towards each centroid, so no
    /// orientation is needed) is at least the given angle. Built once, read only after: queries are serial or parallel at will.
    /// Serial, in triangle order; the points sorted by cell, then index; a query sums in a fixed order. Relative lengths only:
    /// a scale by 2^k gives the same answers.
    /// </summary>
    internal sealed class VarifoldCreaseQuery
    {
        internal readonly double Radius;
        /// <summary> each plane needs at least this share of the ball's mass </summary>
        internal double MinFraction = 0.2;
        /// <summary> a point's plane belongs to a cluster when its normal is within this angle of the cluster's (unoriented) </summary>
        internal double ClusterAngleDegrees = 20;
        /// <summary>
        /// at most this share of the ball's mass has a normal between the two planes' (outside both clusters, and within 30
        /// degrees of the great circle through nA and nB): a crease is two planes, a curved surface spreads its normals between
        /// them (a sphere, or a cylinder that the ball sees from more than one side); a third plane at a corner has its normal
        /// along the crease line and does not count
        /// </summary>
        internal double MaxBetweenFraction = 0.3;
        /// <summary> each cluster is planar: its rms distance from its plane is at most this share of the radius </summary>
        internal double MaxRmsFraction = 0.1;

        readonly double[] pts;      // x, y, z, nx, ny, nz, mass per point, in cell order
        readonly long[] keys;       // the cell key per point, ascending
        readonly int count;
        readonly Vector3d origin;
        readonly long nx, ny, nz;
        readonly double cell;

        /// <summary> the measure of mesh (not kept) at radius (the quadrature spacing radius / 2) </summary>
        internal VarifoldCreaseQuery(DMesh3 mesh, double radius)
        {
            if (!(radius > 0)) throw new ArgumentException("VarifoldCreaseQuery: the radius must be positive");
            Radius = radius; cell = radius;
            double h = 0.5 * radius;
            var raw = new List<double>();
            var stack = new Stack<(Vector3d, Vector3d, Vector3d)>();
            foreach (int tid in mesh.TriangleIndices()) {
                Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
                mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                Vector3d cr = (v1 - v0).Cross(v2 - v0);
                double len = cr.Length;
                if (!(len > 0) || double.IsInfinity(len)) continue;
                Vector3d n = cr / len;
                stack.Push((v0, v1, v2));
                while (stack.Count > 0) {
                    var (a, b, c) = stack.Pop();
                    double ab = a.DistanceSquared(b), bc = b.DistanceSquared(c), ca = c.DistanceSquared(a);
                    double longest = Math.Max(ab, Math.Max(bc, ca)), twice = (b - a).Cross(c - a).Length;
                    // a sliver (longest^2 > 8 area) longer than h: halved at its longest edge first (the lattice would fill it with
                    // (longest / h)^2 points whatever its area)
                    if (longest > h * h && longest > 4 * twice) {
                        if (ab == longest) { Vector3d m = 0.5 * (a + b); stack.Push((a, m, c)); stack.Push((m, b, c)); }
                        else if (bc == longest) { Vector3d m = 0.5 * (b + c); stack.Push((a, b, m)); stack.Push((a, m, c)); }
                        else { Vector3d m = 0.5 * (c + a); stack.Push((a, b, m)); stack.Push((m, b, c)); }
                        continue;
                    }
                    double kk = Math.Ceiling(Math.Sqrt(longest) / h);
                    if (!(kk >= 1 && kk < 1e6)) continue;
                    int k = (int)kk;
                    double w = 0.5 * twice / ((double)k * k) / 3;
                    Vector3d e1 = (b - a) / k, e2 = (c - a) / k;
                    for (int i = 0; i < k; ++i)
                        for (int j = 0; i + j < k; ++j) {
                            // the three edge midpoints of the upright piece at (i, j); an edge inside the triangle is shared with
                            // the inverted piece on its other side and carries both thirds
                            add(raw, a + (i + 0.5) * e1 + j * e2, n, j > 0 ? 2 * w : w);
                            add(raw, a + i * e1 + (j + 0.5) * e2, n, i > 0 ? 2 * w : w);
                            add(raw, a + (i + 0.5) * e1 + (j + 0.5) * e2, n, i + j < k - 1 ? 2 * w : w);
                        }
                }
            }
            count = raw.Count / 7;
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < count; ++i) {
                x0 = Math.Min(x0, raw[7 * i]); y0 = Math.Min(y0, raw[7 * i + 1]); z0 = Math.Min(z0, raw[7 * i + 2]);
                x1 = Math.Max(x1, raw[7 * i]); y1 = Math.Max(y1, raw[7 * i + 1]); z1 = Math.Max(z1, raw[7 * i + 2]);
            }
            if (count == 0) { x0 = y0 = z0 = x1 = y1 = z1 = 0; }
            origin = new Vector3d(x0, y0, z0);
            nx = (long)Math.Floor((x1 - x0) / cell) + 1; ny = (long)Math.Floor((y1 - y0) / cell) + 1; nz = (long)Math.Floor((z1 - z0) / cell) + 1;
            var k0 = new long[count]; var order = new int[count];
            for (int i = 0; i < count; ++i) {
                k0[i] = key(coord(raw[7 * i], origin.x, nx), coord(raw[7 * i + 1], origin.y, ny), coord(raw[7 * i + 2], origin.z, nz));
                order[i] = i;
            }
            // ascending by cell, equal cells by index (Array.Sort is not stable)
            Array.Sort(k0, order);
            for (int j = 0; j < count;) { int e = j; while (e < count && k0[e] == k0[j]) ++e; Array.Sort(order, j, e - j); j = e; }
            keys = k0;
            pts = new double[7 * count];
            for (int j = 0; j < count; ++j) for (int q = 0; q < 7; ++q) pts[7 * j + q] = raw[7 * order[j] + q];
        }

        /// <summary> the number of quadrature points </summary>
        internal int PointCount { get { return count; } }

        static void add(List<double> raw, Vector3d p, Vector3d n, double m)
        {
            raw.Add(p.x); raw.Add(p.y); raw.Add(p.z); raw.Add(n.x); raw.Add(n.y); raw.Add(n.z); raw.Add(m);
        }

        long coord(double v, double o, long n)
        {
            long c = (long)Math.Floor((v - o) / cell);
            return c < 0 ? 0 : c >= n ? n - 1 : c;
        }

        long key(long x, long y, long z) { return (x * ny + y) * nz + z; }

        int lower_bound(long k)
        {
            int lo = 0, hi = count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (keys[mid] < k) lo = mid + 1; else hi = mid; }
            return lo;
        }

        // the kernel-weighted sums of one cluster, relative to the query point
        struct Sums
        {
            public double W;
            public double Mxx, Mxy, Mxz, Myy, Myz, Mzz;     // sum w n n^T
            public Vector3d C;                              // sum w y
            public double Qxx, Qxy, Qxz, Qyy, Qyz, Qzz;     // sum w y y^T
            public void Add(double w, Vector3d n, Vector3d y)
            {
                W += w;
                Mxx += w * n.x * n.x; Mxy += w * n.x * n.y; Mxz += w * n.x * n.z; Myy += w * n.y * n.y; Myz += w * n.y * n.z; Mzz += w * n.z * n.z;
                C += w * y;
                Qxx += w * y.x * y.x; Qxy += w * y.x * y.y; Qxz += w * y.x * y.z; Qyy += w * y.y * y.y; Qyz += w * y.y * y.z; Qzz += w * y.z * y.z;
            }
        }

        /// <summary>
        /// true if the input has a crease at x0 between the planes of unit normals nA and nB (signs ignored): each plane holds at
        /// least MinFraction of the ball's mass, passes within tol + 3 rms of x0, and the dihedral (degrees, 0 for a flat
        /// sheet, 90 for a box edge) is at least minDihedralDeg
        /// </summary>
        internal bool IsCrease(Vector3d x0, Vector3d nA, Vector3d nB, double minDihedralDeg, double tol, out double dihedral)
        {
            dihedral = 0;
            if (count == 0) return false;
            double cosC = Math.Cos(ClusterAngleDegrees * MathUtil.Deg2Rad), r2 = Radius * Radius;
            var A = new Sums(); var B = new Sums();
            double W = 0, between = 0;
            Vector3d axis = nA.Cross(nB);
            double al = axis.Length;
            if (!(al > 1e-6)) return false;
            axis /= al;
            long cx = (long)Math.Floor((x0.x - origin.x) / cell), cy = (long)Math.Floor((x0.y - origin.y) / cell), cz = (long)Math.Floor((x0.z - origin.z) / cell);
            for (long x = cx - 1; x <= cx + 1; ++x) {
                if (x < 0 || x >= nx) continue;
                for (long y = cy - 1; y <= cy + 1; ++y) {
                    if (y < 0 || y >= ny) continue;
                    for (long z = cz - 1; z <= cz + 1; ++z) {
                        if (z < 0 || z >= nz) continue;
                        long k = key(x, y, z);
                        for (int j = lower_bound(k); j < count && keys[j] == k; ++j) {
                            int o = 7 * j;
                            var d = new Vector3d(pts[o] - x0.x, pts[o + 1] - x0.y, pts[o + 2] - x0.z);
                            double t = d.LengthSquared;
                            if (!(t < r2)) continue;
                            double s = 1 - t / r2, s2 = s * s, w = pts[o + 6] * s2 * s2;
                            var n = new Vector3d(pts[o + 3], pts[o + 4], pts[o + 5]);
                            W += w;
                            double ca = Math.Abs(n.Dot(nA)), cb = Math.Abs(n.Dot(nB));
                            // (a plane near both normals goes to the nearer one)
                            if (ca >= cosC && ca >= cb) A.Add(w, n, d);
                            else if (cb >= cosC) B.Add(w, n, d);
                            else if (Math.Abs(n.Dot(axis)) <= 0.5) between += w;
                        }
                    }
                }
            }
            if (!(W > 0) || !(A.W >= MinFraction * W) || !(B.W >= MinFraction * W) || !(between <= MaxBetweenFraction * W)) return false;
            Vector3d pa, pb, ca2, cb2;
            if (!plane(ref A, out pa, out ca2, out double rmsA) || !plane(ref B, out pb, out cb2, out double rmsB)) return false;
            if (!(rmsA <= MaxRmsFraction * Radius) || !(rmsB <= MaxRmsFraction * Radius)) return false;
            // both planes meet at x0 (the origin here)
            if (!(Math.Abs(pa.Dot(ca2)) <= tol + 3 * rmsA) || !(Math.Abs(pb.Dot(cb2)) <= tol + 3 * rmsB)) return false;
            Vector3d dir = pa.Cross(pb);
            double sl = dir.Length;
            if (!(sl > 1e-6)) return false;
            dir /= sl;
            // the half-planes: in each plane, perpendicular to the line, towards the cluster's centroid
            Vector3d ua = dir.Cross(pa), ub = dir.Cross(pb);
            if (ua.Dot(ca2) < 0) ua = -ua;
            if (ub.Dot(cb2) < 0) ub = -ub;
            double c = Math.Max(-1, Math.Min(1, ua.Dot(ub)));
            dihedral = 180 - Math.Acos(c) * MathUtil.Rad2Deg;
            return dihedral >= minDihedralDeg;
        }

        // a cluster's plane: the main eigenvector of sum w n n^T, the centroid, the rms distance of the mass from the plane
        static bool plane(ref Sums s, out Vector3d n, out Vector3d centroid, out double rms)
        {
            centroid = s.C / s.W;
            n = main_eigenvector(s.Mxx, s.Mxy, s.Mxz, s.Myy, s.Myz, s.Mzz);
            rms = 0;
            if (!(n.LengthSquared > 0.5)) return false;
            double q = s.Qxx * n.x * n.x + s.Qyy * n.y * n.y + s.Qzz * n.z * n.z + 2 * (s.Qxy * n.x * n.y + s.Qxz * n.x * n.z + s.Qyz * n.y * n.z);
            double m = n.Dot(centroid);
            rms = Math.Sqrt(Math.Max(0, q / s.W - m * m));
            return MathUtil.IsFinite(rms);
        }

        // the unit eigenvector of the largest eigenvalue of a symmetric positive semi-definite 3 x 3 (cyclic Jacobi, fixed order)
        static Vector3d main_eigenvector(double xx, double xy, double xz, double yy, double yz, double zz)
        {
            double[,] a = { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
            double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 50; ++sweep) {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                double sc = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
                if (off == 0 || off <= 1e-15 * sc) break;
                for (int p = 0; p < 2; ++p)
                    for (int q = p + 1; q < 3; ++q) {
                        if (a[p, q] == 0) continue;
                        double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = th == 0 ? 1 : Math.Sign(th) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < 3; ++k) { double akp = a[k, p], akq = a[k, q]; a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq; }
                        for (int k = 0; k < 3; ++k) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk; }
                        for (int k = 0; k < 3; ++k) { double vkp = v[k, p], vkq = v[k, q]; v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq; }
                    }
            }
            int best = 0;
            for (int i = 1; i < 3; ++i) if (a[i, i] > a[best, best]) best = i;
            var r = new Vector3d(v[0, best], v[1, best], v[2, best]);
            double l = r.Length;
            return l > 0 ? r / l : Vector3d.Zero;
        }
    }
}
