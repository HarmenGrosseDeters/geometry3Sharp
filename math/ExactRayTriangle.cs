using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact decisions whether a line or a ray crosses a triangle, shared by IntrRay3Triangle3 (every ray query of
    /// DMeshAABBTree3), DMeshAABBTree3.IsInside and the exact winding number of VolumeSampleCells.
    ///
    /// The line is given by two double points o and p (for a ray: its origin and SecondPoint(origin, direction)). The
    /// line meets the triangle (a, b, c) iff the three edge signs Orient3d(o, p, a, b), (o, p, b, c), (o, p, c, a)
    /// do not have opposite signs (Plücker coordinates); all three are 0 iff the line lies in the triangle's plane.
    /// Two rules for an exact zero (the line meets the line of an edge: through an edge or a vertex):
    ///   - closed (ClosedCrossing, RayHitClosed; the hit queries): the triangle is closed, a line through an edge or
    ///     a vertex meets every triangle there. Watertight: a ray onto a shared edge or vertex, or onto the boundary of
    ///     an open mesh, never misses all of them, at any scale; a line in the plane of a triangle does not meet it.
    ///   - perturbed (EdgeSign, LineCrossing, RayHitPerturbed; counting): Simulation of Simplicity (Edelsbrunner and
    ///     Mücke, ACM TOG 9(1), 1990): p is moved by (ε, ε², ε³), o by (ε⁴, ε⁸, ε¹²), the same for every triangle, so the
    ///     decision is that of a line in general position that no edge meets. EdgeSign(o, p, a, b) = -EdgeSign(o, p, b,
    ///     a) exactly, so a line through an edge or a vertex crosses exactly one triangle of a closed fan: no crossing
    ///     is lost or counted twice, the parity and the signed crossing count are right.
    /// Every decision is an exact sign (ExactPredicates, a floating-point filter in the common case) or a comparison
    /// of coordinates, so it does not change under scaling by 2^k.
    /// </summary>
    internal static class ExactRayTriangle
    {
        /// <summary> RayHit*: no crossing at t &gt;= 0 </summary>
        public const int Miss = 0;
        /// <summary> RayHit*: the ray crosses the triangle at t &gt; 0 </summary>
        public const int Ahead = 1;
        /// <summary> RayHit*: the origin lies on the triangle (t = 0) </summary>
        public const int AtOrigin = 2;

        /// <summary>
        /// The second point of the ray's line: origin + 2^m direction, with 2^m chosen from the exponents of the largest
        /// coordinates of origin and direction so that the scaled direction is at least twice the origin's largest
        /// coordinate (so p != origin, and the rounding of the sum changes the line's direction by at most about
        /// 2^-51 relative). 2^m depends only on the exponent difference, so scaling origin and direction by 2^k (or the
        /// origin alone, with the 2^k scaled into 2^m) scales p by 2^k exactly. Returns origin if the direction is 0
        /// or a value is not finite (no hits then).
        /// </summary>
        public static Vector3d SecondPoint(Vector3d origin, Vector3d direction)
        {
            double mo = Math.Max(Math.Abs(origin.x), Math.Max(Math.Abs(origin.y), Math.Abs(origin.z)));
            double md = Math.Max(Math.Abs(direction.x), Math.Max(Math.Abs(direction.y), Math.Abs(direction.z)));
            // (NaN fails both comparisons)
            if (!(md > 0 && md <= double.MaxValue && mo <= double.MaxValue))
                return origin;
            int m = 0;
            if (mo > 0) {
                // largest coordinate of 2^m direction in [2^(e(mo)+1), 2^(e(mo)+2)): at least 2 mo
                m = exponent(mo) - exponent(md) + 1;
                m = Math.Max(-2000, Math.Min(2000, m));
            }
            Vector3d p;
            if (m >= -1022 && m <= 1023) {
                // 2^m is a normal double: x * 2^m is x scaled by 2^m with one rounding (only where the result is
                // subnormal or overflows), the same double as ScaleB(x, m)
                double s = BitConverter.Int64BitsToDouble((long)(m + 1023) << 52);
                p = origin + new Vector3d(direction.x * s, direction.y * s, direction.z * s);
            } else {
                p = origin + new Vector3d(ExactPoint3.ScaleB(direction.x, m), ExactPoint3.ScaleB(direction.y, m),
                    ExactPoint3.ScaleB(direction.z, m));
            }
            if (is_finite(p) == false)
                return origin;
            return p;
        }

        // floor(log2 |x|) for finite x != 0
        static int exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            if (e != 0)
                return e - 1023;
            // subnormal: normalize (exact)
            bits = BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0);    // 2^64
            return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool is_finite(Vector3d v)
        {
            // x - x is 0 for a finite x and NaN for an infinite or NaN one
            return (v.x - v.x) == 0 && (v.y - v.y) == 0 && (v.z - v.z) == 0;
        }

        /// <summary>
        /// The sign of Orient3d(o', p', a, b) for o' = o + (ε⁴, ε⁸, ε¹²), p' = p + (ε, ε², ε³), ε → 0+: never 0 unless
        /// a == b. With f(o, p) = det[p - o, a - o, b - o] (the value whose sign Orient3d(o, p, a, b) is), affine in o
        /// and in p, the expansion is f + Σ ε^(1,2,3) ∂f/∂p_(x,y,z) + Σ ε^(4,8,12) ∂f/∂o_(x,y,z) + Σ ε^(i+j) ∂²f/∂p∂o
        /// (no other terms: f is linear in p, affine in o, and the exponents 1..15 are all distinct), with
        ///   ∂f/∂p_x = ((a - o) x (b - o))_x = orient2d of o, a, b in the (y, z) plane, likewise (z, x) and (x, y);
        ///   ∂f/∂o_x = -orient2d of p, a, b in the (y, z) plane, likewise (z, x) and (x, y);
        ///   ∂²f/∂p_i∂o_j = (e_j x (a - b))_i: for (p_y, o_x) -(a - b)_z, (p_z, o_x) (a - b)_y, (p_x, o_y) (a - b)_z,
        ///   (p_z, o_y) -(a - b)_x, ..., (p_i, o_i) 0.
        /// The first nonzero coefficient in the order of the exponents is the sign. It is reached by exponent 11 at the
        /// latest if a != b ((a - b)_z at 6, (a - b)_y at 7, (a - b)_x at 11).
        /// </summary>
        public static int EdgeSign(Vector3d o, Vector3d p, Vector3d a, Vector3d b)
        {
            int s = ExactPredicates.Orient3d(o, p, a, b);
            if (s != 0) return s;
            return edge_sign_perturbed(o, p, a, b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int edge_sign_perturbed(Vector3d o, Vector3d p, Vector3d a, Vector3d b)
        {
            int s;
            // ε¹, ε², ε³: p moved along x, y, z
            if ((s = ExactPredicates.Orient2d(yz(o), yz(a), yz(b))) != 0) return s;
            if ((s = ExactPredicates.Orient2d(zx(o), zx(a), zx(b))) != 0) return s;
            if ((s = ExactPredicates.Orient2d(xy(o), xy(a), xy(b))) != 0) return s;
            // (o, a, b collinear) ε⁴: o moved along x
            if ((s = ExactPredicates.Orient2d(yz(p), yz(a), yz(b))) != 0) return -s;
            // ε⁵ = (p_x, o_x): 0; ε⁶ = (p_y, o_x): -(a - b)_z; ε⁷ = (p_z, o_x): (a - b)_y
            if (a.z != b.z) return a.z > b.z ? -1 : 1;
            if (a.y != b.y) return a.y > b.y ? 1 : -1;
            // ε⁸: o moved along y
            if ((s = ExactPredicates.Orient2d(zx(p), zx(a), zx(b))) != 0) return -s;
            // ε⁹ = (p_x, o_y): (a - b)_z = 0 here; ε¹⁰ = (p_y, o_y): 0; ε¹¹ = (p_z, o_y): -(a - b)_x
            if (a.x != b.x) return a.x > b.x ? -1 : 1;
            return 0;   // a == b
        }

        // The differences Orient3d(o, p, x, y) computes for the three edges (u = p - o; a - o, b - o, c - o), each
        // rounded once and shared, so that ExactPredicates.Orient3dFilter gives Orient3d's own decision bit for bit
        // (the same operations on the same doubles); where it does not decide, Orient3d(o, p, x, y) itself does.
        struct Diffs
        {
            public double ux, uy, uz, ax, ay, az, bx, by, bz, cx, cy, cz;
            public Diffs(Vector3d o, Vector3d p, Vector3d a, Vector3d b, Vector3d c)
            {
                ux = p.x - o.x; uy = p.y - o.y; uz = p.z - o.z;
                ax = a.x - o.x; ay = a.y - o.y; az = a.z - o.z;
                bx = b.x - o.x; by = b.y - o.y; bz = b.z - o.z;
                cx = c.x - o.x; cy = c.y - o.y; cz = c.z - o.z;
            }
        }

        // Orient3d(o, p, x, y) for edge 0 (x, y) = (a, b), 1 (b, c), 2 (c, a)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int orient3d(ref Diffs d, int edge, Vector3d o, Vector3d p, Vector3d x, Vector3d y)
        {
            int s;
            if (edge == 0) s = ExactPredicates.Orient3dFilter(d.ux, d.uy, d.uz, d.ax, d.ay, d.az, d.bx, d.by, d.bz);
            else if (edge == 1) s = ExactPredicates.Orient3dFilter(d.ux, d.uy, d.uz, d.bx, d.by, d.bz, d.cx, d.cy, d.cz);
            else s = ExactPredicates.Orient3dFilter(d.ux, d.uy, d.uz, d.cx, d.cy, d.cz, d.ax, d.ay, d.az);
            if (s != 0) {
                PredicateStats.Call(PredicateStats.Kind.Orient3d);
                return s;
            }
            return ExactPredicates.Orient3d(o, p, x, y);
        }

        // EdgeSign(o, p, x, y) with the shared differences
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int edge_sign(ref Diffs d, int edge, Vector3d o, Vector3d p, Vector3d x, Vector3d y)
        {
            int s = orient3d(ref d, edge, o, p, x, y);
            if (s != 0) return s;
            return edge_sign_perturbed(o, p, x, y);
        }

        static Vector2d yz(Vector3d v) { return new Vector2d(v.y, v.z); }
        static Vector2d zx(Vector3d v) { return new Vector2d(v.z, v.x); }
        static Vector2d xy(Vector3d v) { return new Vector2d(v.x, v.y); }

        /// <summary>
        /// The crossing of the perturbed line (o', p') with the triangle (a, b, c): +1 if it crosses the triangle's
        /// interior with (p - o) . N &gt; 0, N = (b - a) x (c - a) (the normal Orient3d(a, b, c, .) is positive along), -1
        /// if it crosses with (p - o) . N &lt; 0, 0 if it does not cross (also for a triangle of no area). The edge signs
        /// sum to (p' - o') . N (Σ det[d, a - o, b - o] over the edges is d . N), so when they agree that is their sign.
        /// </summary>
        public static int LineCrossing(Vector3d o, Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            var d = new Diffs(o, p, a, b, c);
            int s = edge_sign(ref d, 0, o, p, a, b);
            if (s == 0 || edge_sign(ref d, 1, o, p, b, c) != s || edge_sign(ref d, 2, o, p, c, a) != s)
                return 0;
            return s;
        }

        /// <summary>
        /// The crossing of the line (o, p) with the closed triangle (a, b, c): +1 if it meets the triangle (interior,
        /// edge or vertex) with (p - o) . N &gt; 0, -1 with (p - o) . N &lt; 0, 0 if it does not meet it or lies in its
        /// plane (all three signs 0; also every line for a triangle of no area). The edge signs sum to (p - o) . N, so
        /// when none is opposite to another and not all are 0, the nonzero ones have the sign of (p - o) . N.
        /// </summary>
        public static int ClosedCrossing(Vector3d o, Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            var d = new Diffs(o, p, a, b, c);
            int s0 = orient3d(ref d, 0, o, p, a, b);
            int s1 = orient3d(ref d, 1, o, p, b, c);
            if (s0 * s1 < 0) return 0;
            int s2 = orient3d(ref d, 2, o, p, c, a);
            if (s0 * s2 < 0 || s1 * s2 < 0) return 0;
            return s0 != 0 ? s0 : (s1 != 0 ? s1 : s2);
        }

        /// <summary>
        /// Does the ray from o through p hit the closed triangle (a, b, c): Miss, Ahead (at t &gt; 0) or AtOrigin (o lies
        /// on the triangle: a ray that starts on the surface hits it at t = 0, as upstream). The side of o is exact:
        /// the crossing is ahead iff Orient3d(a, b, c, o) is opposite to the crossing sign; a crossing implies
        /// (p - o) . N != 0, so with o in the plane the crossing is o itself.
        /// </summary>
        public static int RayHitClosed(Vector3d o, Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            if (is_finite(o) == false || is_finite(a) == false || is_finite(b) == false || is_finite(c) == false || o == p)
                return Miss;
            int s = ClosedCrossing(o, p, a, b, c);
            if (s == 0)
                return Miss;
            int so = ExactPredicates.Orient3d(a, b, c, o);
            if (so != 0)
                return (so == -s) ? Ahead : Miss;
            return AtOrigin;
        }

        /// <summary>
        /// Does the perturbed ray from o' through p' cross the triangle (a, b, c): Miss, Ahead (at t &gt; 0) or AtOrigin
        /// (o lies on the triangle: either answer is consistent for a point on the surface). The side of o is exact: the
        /// crossing is ahead iff Orient3d(a, b, c, o) is opposite to the crossing sign. With o exactly in the
        /// triangle's plane: if p is in it too (the line lies in the plane), the perturbed line crosses the plane
        /// behind o' (both (o' - a) . N and (p' - o') . N lead with the first nonzero coordinate of N, so t' &lt; 0): Miss,
        /// as the perturbation decides, and as upstream ("parallel: no intersection"). Otherwise the crossing is at o
        /// itself, on the closed triangle: AtOrigin.
        /// </summary>
        public static int RayHitPerturbed(Vector3d o, Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            if (is_finite(o) == false || is_finite(a) == false || is_finite(b) == false || is_finite(c) == false || o == p)
                return Miss;
            int s = LineCrossing(o, p, a, b, c);
            if (s == 0)
                return Miss;
            int so = ExactPredicates.Orient3d(a, b, c, o);
            if (so != 0)
                return (so == -s) ? Ahead : Miss;
            return ExactPredicates.Orient3d(a, b, c, p) == 0 ? Miss : AtOrigin;
        }
    }
}
