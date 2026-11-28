// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// The gradient of the volume of an exact Boolean result with respect to the input vertex positions, each component a
    /// double with a proven absolute error bound (docs/guides/booleans-and-imprint.md, "Volume gradient";
    /// docs/research/EXACT_BOOLEAN_GRADIENTS_2026-11.md).
    ///
    /// The result before rounding (the exact result of <see cref="ExactMeshBoolean"/>) is made of pieces of the input
    /// triangles. A kept piece P of the input triangle T moves with T when T's vertices move, so by the Hadamard formula
    /// (the volume changes by the integral of the normal velocity over the surface; the intersection curves have no area)
    ///   dV/dx_i = sum over the kept pieces P of a triangle T with corner i of  s_P lambda_i^T(centroid of P) N_P,
    /// N_P the area vector of P oriented like T, s_P = -1 where the result reverses it, lambda^T the barycentric coordinates in
    /// T. Each term is evaluated in interval arithmetic (<see cref="ExactInterval"/>, which encloses every rounding) in a local
    /// frame of its triangle (exact differences of the exact corners, rounded once, scaled to the triangle's size), and the
    /// terms are summed per vertex in the order of the pieces, so the enclosure holds for the sum and the bounds stay relative
    /// to each entry's magnitude, however far apart the inputs' magnitudes are.
    /// The result is the same bit for bit on every run (serial, no FMA) and scales exactly with a power-of-two scaling of the
    /// inputs (s^2 for the gradient, s^3 for the volume).
    ///
    /// Where V is not differentiable: only where faces of different inputs overlap in a common plane with positive area (a
    /// contact). There <see cref="Gradient"/> is the one-sided derivative of the branch the exact result realizes (a union
    /// merges touching solids, an intersection and a difference separate them; of flush faces the result keeps the first
    /// input's, by input order), and <see cref="Contacts"/> lists each such region with the other branches. Elsewhere (an edge
    /// or a vertex on a face, edges crossing) V is differentiable and the gradient is the derivative.
    /// </summary>
    public sealed class BooleanVolumeGradient
    {
        /// <summary> a sparse change of the gradient: the entry of vertex Vertex of input Mesh </summary>
        public struct GradientTerm
        {
            /// <summary> the input mesh (index in the boolean's inputs: 0 for MeshA, 1 for MeshB, or the index in Meshes) </summary>
            public int Mesh;
            /// <summary> the vertex id in that mesh </summary>
            public int Vertex;
            /// <summary> the value (a double near the exact one) </summary>
            public Vector3d Value;
            /// <summary> per component: the exact value differs from Value by at most this </summary>
            public Vector3d Bound;
        }

        /// <summary>
        /// A region where faces of several inputs lie in one plane and overlap (a contact of positive area): V has a kink here.
        /// A perturbation that moves the faces apart in one order is the derivative of one branch; each branch other than
        /// the realized one (<see cref="BooleanVolumeGradient.Gradient"/>) is given as a change of the gradient.
        /// </summary>
        public sealed class ContactRegion
        {
            /// <summary> the inputs whose faces overlap here, ascending </summary>
            public int[] Meshes;
            /// <summary> the number of coplanar faces stacked here (2 for two touching or flush faces) </summary>
            public int Sheets;
            /// <summary> the area of the region (a double near the exact value) </summary>
            public double Area;
            /// <summary>
            /// the other branches: the gradient of branch k is Gradient plus Alternatives[k] (only the vertices that change).
            /// One per order of the faces along the plane's normal that gives another result than the realized one; empty for
            /// more than 4 faces or two faces of one input with the same orientation (not enumerated)
            /// </summary>
            public GradientTerm[][] Alternatives;
        }

        readonly ExactMeshBoolean boolean;

        /// <summary> the gradient of the result of boolean, which must have been computed with RetainClassification = true </summary>
        public BooleanVolumeGradient(ExactMeshBoolean boolean)
        {
            this.boolean = boolean ?? throw new ArgumentNullException(nameof(boolean));
        }

        /// <summary> why Compute() failed, else null </summary>
        public string Error;
        /// <summary> the volume of the exact result (before rounding) </summary>
        public double Volume;
        /// <summary> the exact volume differs from Volume by at most this </summary>
        public double VolumeBound;
        /// <summary> per input mesh, by vertex id (MaxVertexID entries; zero for vertices of no kept piece): dV/dx </summary>
        public Vector3d[][] Gradient;
        /// <summary> per input mesh and vertex id, per component: the exact derivative differs from Gradient by at most this </summary>
        public Vector3d[][] GradientBound;
        /// <summary> the contact regions (kinks of V), in a fixed order </summary>
        public List<ContactRegion> Contacts;

        /// <summary> number of kept pieces evaluated via the uncut input-triangle fast path in the last Compute() </summary>
        internal int UncutFastCount;
        /// <summary> whether the last Compute() used the plane-origin double volume path </summary>
        internal bool FastVolumeUsed;

        const double MinNormal = 2.2250738585072014E-308;       // 2^-1022
        static readonly double TwoDiffMax = ExactPoint3.ScaleB(1, 1020);

        /// <summary>
        /// computes Volume, Gradient and Contacts from the boolean's last Compute(). False, with Error, if it did not succeed, was
        /// not computed with RetainClassification, has an input that is not closed (the volume of an open result is not defined),
        /// or if an entry or its bound is beyond the double range (the volume from coordinates of about 2^341, the gradient from
        /// about 2^512; the arrays are filled then; small coordinates fail nothing, the bounds become absolute subnormals)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public bool Compute()
        {
            Error = null;
            Volume = VolumeBound = 0;
            Gradient = GradientBound = null;
            Contacts = null;
            UncutFastCount = 0;
            FastVolumeUsed = false;
            ExactMeshArrangement arr = boolean.Arrangement;
            if (boolean.Status != ExactMeshBoolean.BooleanStatus.Success || arr == null)
                return fail("the boolean did not succeed");
            bool[] keep = boolean.CertificateKeep, flip = boolean.CertificateFlip;
            List<int>[] stacks = boolean.StacksPerPiece;
            if (boolean.RetainClassification == false || keep == null || flip == null || keep.Length != arr.Output.Count)
                return fail("the boolean was not computed with RetainClassification = true");
            if (boolean.ExactWinding == null || Array.IndexOf(boolean.ExactWinding, false) >= 0)
                return fail("an input is not closed: the volume of the result is not defined");

            int nm = arr.Meshes.Count;
            this.arr = arr;
            e = arr.ScaleExp;
            exact = null;
            haveExact = null;
            tris = null;
            try {
                List<ExactMeshArrangement.OutputTriangle> output = arr.Output;

                // Pass 1: choose the volume origin (an actual input vertex of a kept piece) and binary scale exponent E_vol
                // (in input units). For uncut pieces, V0 is a corner of the piece itself; for cut pieces, we use the piece's own
                // vertex ot.A (in double when ot.A is an input point, or cached exact BigInteger when constructed) so a large
                // clipping triangle never inflates E_vol or VolumeBound beyond the kept result's own extent.
                bool fastVol = true, hasOrigin = false, anyKept = false;
                int volOriginId = -1;
                Vector3d volOrigin = default(Vector3d);
                int volTop = int.MinValue;
                for (int i = 0; i < output.Count; ++i) {
                    if (keep[i] == false)
                        continue;
                    anyKept = true;
                    var ot = output[i];
                    var it = arr.Triangles[ot.Source];
                    if (is_uncut(it.Corners, ot)) {
                        volOriginId = arr.Canonical(it.Corners.a);
                        volOrigin = it.V0;
                        hasOrigin = true;
                        break;
                    }
                    int ca = arr.Canonical(ot.A);
                    if (arr.KindOf(ca) == ExactMeshArrangement.PointKind.Input) {
                        volOriginId = ca;
                        volOrigin = arr.PointToVector(ca);
                        hasOrigin = true;
                        break;
                    }
                }
                if (hasOrigin) {
                    if (!(Math.Abs(volOrigin.x) <= TwoDiffMax && Math.Abs(volOrigin.y) <= TwoDiffMax && Math.Abs(volOrigin.z) <= TwoDiffMax)) {
                        fastVol = false;
                    } else {
                        for (int i = 0; i < output.Count; ++i) {
                            if (keep[i] == false)
                                continue;
                            var ot = output[i];
                            var it = arr.Triangles[ot.Source];
                            if (is_uncut(it.Corners, ot)) {
                                Vector3d v0 = it.V0;
                                if (!try_two_diff(v0.x, volOrigin.x, out _, out int ex)
                                    || !try_two_diff(v0.y, volOrigin.y, out _, out int ey)
                                    || !try_two_diff(v0.z, volOrigin.z, out _, out int ez)) {
                                    fastVol = false;
                                    break;
                                }
                                int m = Math.Max(ex, Math.Max(ey, ez));
                                if (m > volTop)
                                    volTop = m;
                            } else {
                                int ca = arr.Canonical(ot.A);
                                if (ca == volOriginId)
                                    continue;
                                if (arr.KindOf(ca) == ExactMeshArrangement.PointKind.Input) {
                                    Vector3d va = arr.PointToVector(ca);
                                    if (!try_two_diff(va.x, volOrigin.x, out _, out int ex)
                                        || !try_two_diff(va.y, volOrigin.y, out _, out int ey)
                                        || !try_two_diff(va.z, volOrigin.z, out _, out int ez)) {
                                        fastVol = false;
                                        break;
                                    }
                                    int m = Math.Max(ex, Math.Max(ey, ez));
                                    if (m > volTop)
                                        volTop = m;
                                } else {
                                    ExactPoint3 a = P(ca), b = P(volOriginId);
                                    if (a.Equals(b) == false) {
                                        long mag = magnitude(ExactPoint3.CreateUnreduced(a.X * b.W - b.X * a.W, a.Y * b.W - b.Y * a.W, a.Z * b.W - b.Z * a.W, a.W * b.W)) + e;
                                        if (mag > volTop)
                                            volTop = (int)mag;
                                    }
                                }
                            }
                        }
                    }
                } else if (anyKept) {
                    fastVol = false;
                }
                int S_grid = 0;
                int E_vol;
                if (fastVol) {
                    E_vol = volTop == int.MinValue ? 0 : volTop;
                } else {
                    // fallback: 2^S_grid above every coordinate of a kept piece (grid units, relative to the center)
                    long top = 0;
                    for (int i = 0; i < output.Count; ++i)
                        if (keep[i]) {
                            var ot = output[i];
                            top = Math.Max(top, Math.Max(magnitude(P(ot.A)), Math.Max(magnitude(P(ot.B)), magnitude(P(ot.C)))));
                        }
                    S_grid = (int)top;
                    E_vol = S_grid + e;
                }
                FastVolumeUsed = fastVol;

                var acc = new IV3[nm][];
                for (int m = 0; m < nm; ++m)
                    acc[m] = new IV3[arr.Meshes[m].MaxVertexID];
                ExactInterval vol = ExactInterval.Of(0);
                ExactInterval third = ExactInterval.Of(1) / ExactInterval.Of(3);
                ExactInterval sixth = ExactInterval.Of(1) / ExactInterval.Of(6);
                ExactInterval negSixth = -sixth;
                for (int i = 0; i < output.Count; ++i) {
                    if (keep[i] == false)
                        continue;
                    var ot = output[i];
                    var it = arr.Triangles[ot.Source];
                    Vector3d v0 = it.V0;
                    // Uncut kept piece (P == T, same orientation): exact barycentric weights of centroid are 1/3 each, so every
                    // corner receives s * (1/6) * (V1 - V0) x (V2 - V0), evaluated directly in ExactInterval without BigInteger.
                    if (is_uncut(it.Corners, ot) && try_uncut_frame(v0, it.V1, it.V2, out int E, out IV3 d1, out IV3 d2)) {
                        UncutFastCount++;
                        IV3 n2 = d1.Cross(d2);
                        ExactInterval s6 = flip[i] ? negSixth : sixth;
                        IV3 gTerm = scale(n2 * s6, 2 * E);
                        Index3i v = arr.Meshes[it.Mesh].GetTriangle(it.Tid);
                        IV3[] mAcc = acc[it.Mesh];
                        mAcc[v.a] = mAcc[v.a] + gTerm;
                        mAcc[v.b] = mAcc[v.b] + gTerm;
                        mAcc[v.c] = mAcc[v.c] + gTerm;
                        IV3 p0 = vol_p0_uncut(v0, ot, fastVol, volOrigin, E_vol, S_grid);
                        vol = add(vol, mul(s6, scale(n2, 2 * (E - E_vol)).Dot(p0)));
                        continue;
                    }
                    Tri T = tri(ot.Source);
                    Local l = local(T, ot);
                    ExactInterval s = ExactInterval.Of(flip[i] ? -1 : 1);
                    add_terms(acc, T, l, s);
                    // V = sum of s N_P . p_0 / 3 (the divergence theorem per piece), at the scale 2^E_vol
                    IV3 p0Cut = fastVol ? diff(ot.A, volOriginId, E_vol) : vol_p0_exact(ot, S_grid);
                    vol = add(vol, mul(mul(s, third), scale(l.N, 2 * (T.E - E_vol)).Dot(p0Cut)));
                }
                Volume = scaled(vol, 3 * E_vol, out VolumeBound);
                Gradient = new Vector3d[nm][];
                GradientBound = new Vector3d[nm][];
                bool finite = !double.IsInfinity(VolumeBound);
                for (int m = 0; m < nm; ++m) {
                    Gradient[m] = new Vector3d[acc[m].Length];
                    GradientBound[m] = new Vector3d[acc[m].Length];
                    for (int v = 0; v < acc[m].Length; ++v) {
                        Gradient[m][v] = acc[m][v].Mid(0, out GradientBound[m][v]);
                        finite &= !double.IsInfinity(GradientBound[m][v].MaxAbs);
                    }
                }
                Contacts = contacts(output, keep, flip, stacks, nm);
                if (finite == false)
                    return fail("a value is beyond the double range (an entry or its bound is infinite)");
                return true;
            } finally {
                this.arr = null;
                exact = null;
                haveExact = null;
                tris = null;
            }
        }

        bool fail(string why) { Error = why; return false; }

        // ---- the terms ----

        ExactMeshArrangement arr;
        int e;
        ExactPoint3[] exact;
        bool[] haveExact;
        Tri[] tris;

        // Every term is evaluated in a local frame of its input triangle T: coordinates relative to T's first corner, scaled
        // by 2^-E_T (in input units) so that T's edges are about 1. Differences of input vertices use Knuth's exact TwoDiff in
        // IEEE 754 binary64 (falling back to BigInteger for constructed points or extreme/subnormal inputs), so the evaluation
        // neither loses the precision of a small triangle far from the center nor under- or overflows, whatever the spread of
        // the inputs' magnitudes; each term is then scaled exactly by 2^(2 E_T) into the input's units (scale).
        sealed class Tri
        {
            public int Mesh;
            public int[] Vid;           // per corner (in the arrangement's corner order)
            public int C0;              // the point id of corner 0 (the local origin)
            public int E;               // the local scale exponent (in input units)
            public IV3 D1, D2;          // corners 1 and 2 in the local frame (corner 0 is the origin)
            public IV3 N;               // D1 x D2
            public ExactInterval NN;
            public BigInteger[] Normal; // exact, for the orientation of coplanar triangles (when needed)
        }

        // a piece in the local frame of a triangle that contains it: its centroid and its area vector (oriented like the piece)
        struct Local { public IV3 C, N; }

        ExactPoint3 P(int id)
        {
            if (exact == null) {
                exact = new ExactPoint3[arr.PointCount];
                haveExact = new bool[arr.PointCount];
            }
            if (haveExact[id] == false) {
                exact[id] = arr.Exact(id);
                haveExact[id] = true;
            }
            return exact[id];
        }

        // floor(log2) + 1 of the largest coordinate's magnitude (bit lengths: at most one above)
        static long magnitude(ExactPoint3 p)
        {
            long w = ExactPoint3.BitLength(BigInteger.Abs(p.W));
            return Math.Max(ExactPoint3.BitLength(BigInteger.Abs(p.X)), Math.Max(ExactPoint3.BitLength(BigInteger.Abs(p.Y)), ExactPoint3.BitLength(BigInteger.Abs(p.Z)))) - w + 1;
        }

        // Exact enclosure of a - b via Knuth's TwoDiff (exact with IEEE 754 gradual underflow whenever |a|, |b| <= 2^1020 so
        // no intermediate overflows; Boldo & Melquiond 2005). Because a - b = d + err exactly and d = fl(a - b) is the nearest
        // double, err == 0 gives the exact point [d, d], err > 0 gives [d, Up(d)], and err < 0 gives [Down(d), d].
        // Returns false outside TwoDiffMax or when a nonzero |d| <= MinNormal (subnormal or MinNormal whose Down is subnormal).
        [MethodImpl(JitHints.Optimize)]
        internal static bool try_two_diff(double a, double b, out ExactInterval diff, out int exp)
        {
            if (!(Math.Abs(a) <= TwoDiffMax && Math.Abs(b) <= TwoDiffMax)) {
                diff = default(ExactInterval);
                exp = 0;
                return false;
            }
            if (a == b) {
                diff = ExactInterval.Of(0);
                exp = int.MinValue;
                return true;
            }
            double d = a - b;
            double ad = Math.Abs(d);
            if (!(ad > MinNormal)) {
                diff = default(ExactInterval);
                exp = 0;
                return false;
            }
            double bv = d - a;
            double err = (a - (d - bv)) - (b + bv);
            if (err == 0)
                diff = ExactInterval.Of(d);
            else if (err > 0)
                diff = new ExactInterval(d, ExactInterval.Up(d));
            else
                diff = new ExactInterval(ExactInterval.Down(d), d);
            // unbiased exponent + 1: 2^(exp - 1) <= |d| < 2^exp
            long bits = BitConverter.DoubleToInt64Bits(ad);
            exp = (int)((bits >> 52) & 0x7FF) - 1022;
            return true;
        }

        [MethodImpl(JitHints.Optimize)]
        internal static bool try_input_diff(Vector3d a, Vector3d b, int E, out IV3 res)
        {
            if (!try_two_diff(a.x, b.x, out ExactInterval dx, out _)
                || !try_two_diff(a.y, b.y, out ExactInterval dy, out _)
                || !try_two_diff(a.z, b.z, out ExactInterval dz, out _)) {
                res = default(IV3);
                return false;
            }
            res = new IV3(scale(dx, -E), scale(dy, -E), scale(dz, -E));
            return true;
        }

        [MethodImpl(JitHints.Optimize)]
        internal static bool try_uncut_frame(Vector3d v0, Vector3d v1, Vector3d v2, out int E, out IV3 d1, out IV3 d2)
        {
            if (!try_two_diff(v1.x, v0.x, out ExactInterval d1x, out int e1x)
                || !try_two_diff(v1.y, v0.y, out ExactInterval d1y, out int e1y)
                || !try_two_diff(v1.z, v0.z, out ExactInterval d1z, out int e1z)
                || !try_two_diff(v2.x, v0.x, out ExactInterval d2x, out int e2x)
                || !try_two_diff(v2.y, v0.y, out ExactInterval d2y, out int e2y)
                || !try_two_diff(v2.z, v0.z, out ExactInterval d2z, out int e2z)) {
                E = 0; d1 = default(IV3); d2 = default(IV3);
                return false;
            }
            int m = Math.Max(Math.Max(e1x, Math.Max(e1y, e1z)), Math.Max(e2x, Math.Max(e2y, e2z)));
            E = m == int.MinValue ? 0 : m;
            d1 = new IV3(scale(d1x, -E), scale(d1y, -E), scale(d1z, -E));
            d2 = new IV3(scale(d2x, -E), scale(d2y, -E), scale(d2z, -E));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool is_cyclic_match(int a, int b, int c, int t0, int t1, int t2)
            => (a == t0 && b == t1 && c == t2)
            || (a == t1 && b == t2 && c == t0)
            || (a == t2 && b == t0 && c == t1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool is_uncut(Index3i c, ExactMeshArrangement.OutputTriangle ot)
        {
            if (ot.A == c.a && ot.B == c.b && ot.C == c.c)
                return true;
            return is_cyclic_match(
                arr.Canonical(ot.A), arr.Canonical(ot.B), arr.Canonical(ot.C),
                arr.Canonical(c.a), arr.Canonical(c.b), arr.Canonical(c.c));
        }

        [MethodImpl(JitHints.Optimize)]
        IV3 vol_p0_uncut(Vector3d v0, ExactMeshArrangement.OutputTriangle ot, bool fastVol, Vector3d volOrigin, int E_vol, int S_grid)
        {
            if (fastVol) {
                if (!try_input_diff(v0, volOrigin, E_vol, out IV3 d))
                    throw new InvalidOperationException("fastVol invariant violated");
                return d;
            }
            return vol_p0_exact(ot, S_grid);
        }

        IV3 vol_p0_exact(ExactMeshArrangement.OutputTriangle ot, int S_grid)
        {
            ExactPoint3 p = P(ot.A);
            return new IV3(enclose(p.X, p.W, -S_grid), enclose(p.Y, p.W, -S_grid), enclose(p.Z, p.W, -S_grid));
        }

        // (q - p) 2^-E in input units, using exact double TwoDiff when both endpoints are input points and BigInteger otherwise
        [MethodImpl(JitHints.Optimize)]
        IV3 diff(int q, int p, int E)
        {
            if (q == p)
                return default(IV3);
            int cq = arr.Canonical(q), cp = arr.Canonical(p);
            if (cq == cp)
                return default(IV3);
            if (arr.KindOf(cq) == ExactMeshArrangement.PointKind.Input
                && arr.KindOf(cp) == ExactMeshArrangement.PointKind.Input
                && try_input_diff(arr.PointToVector(cq), arr.PointToVector(cp), E, out IV3 d))
                return d;
            return diff_exact(cq, cp, E);
        }

        // (q - p) 2^-E in input units (== (grid_q - grid_p) 2^(e - E)), each coordinate the exact difference, rounded once and enclosed
        IV3 diff_exact(int q, int p, int E)
        {
            ExactPoint3 a = P(q), b = P(p);
            BigInteger den = a.W * b.W;
            int shift = e - E;
            return new IV3(enclose(a.X * b.W - b.X * a.W, den, shift), enclose(a.Y * b.W - b.Y * a.W, den, shift), enclose(a.Z * b.W - b.Z * a.W, den, shift));
        }

        [MethodImpl(JitHints.Optimize)]
        Local local(Tri T, ExactMeshArrangement.OutputTriangle ot)
        {
            // (the piece's edges from the local corners: they are at most about 1 in the local frame, so the subtraction of the
            // enclosed corners costs a few spacings of 1, not of the far coordinates)
            IV3 q0 = diff(ot.A, T.C0, T.E), q1 = diff(ot.B, T.C0, T.E), q2 = diff(ot.C, T.C0, T.E);
            return new Local { C = (q0 + q1 + q2) / ExactInterval.Of(3), N = (q1 - q0).Cross(q2 - q0) * ExactInterval.Of(0.5) };
        }

        // x 2^k: exact unless an end is subnormal or overflows; then each end is widened by one spacing (ScaleB rounds once,
        // by less than one spacing of its result; an end that overflows to infinity is at least as far as the exact value).
        // Fast path requires strict |lo| > MinNormal (and finite): a subnormal exact product just below MinNormal can tie-to-even
        // up to MinNormal, so == MinNormal must go through scale_slow's round-trip check.
        [MethodImpl(JitHints.Optimize)]
        internal static ExactInterval scale(ExactInterval x, int k)
        {
            if (k == 0 || (x.Lo == 0 && x.Hi == 0))
                return x;
            if (k >= -1022 && k <= 1023) {
                double f = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
                double lo = x.Lo * f, hi = x.Hi * f;
                bool loOk = x.Lo == 0 || (Math.Abs(lo) > MinNormal && !double.IsInfinity(lo));
                bool hiOk = x.Hi == 0 || (Math.Abs(hi) > MinNormal && !double.IsInfinity(hi));
                if (loOk && hiOk)
                    return new ExactInterval(lo, hi);
            }
            return scale_slow(x, k);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static ExactInterval scale_slow(ExactInterval x, int k)
        {
            double lo = ExactPoint3.ScaleB(x.Lo, k), hi = ExactPoint3.ScaleB(x.Hi, k);
            if (ExactPoint3.ScaleB(lo, -k) == x.Lo && ExactPoint3.ScaleB(hi, -k) == x.Hi)
                return new ExactInterval(lo, hi);
            return new ExactInterval(ExactInterval.Down(lo), ExactInterval.Up(hi));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static IV3 scale(IV3 v, int k) => new IV3(scale(v.X, k), scale(v.Y, k), scale(v.Z, k));

        // num / den * 2^e: exact via scale((double)num, e) when den == 1 and |num| < 2^53 (every <= 53-bit integer is an exact
        // binary64 double; scale widens if subnormal or overflowing); otherwise correctly rounded by ExactPoint3.ToDouble and
        // widened by one spacing on both sides.
        internal static ExactInterval enclose(BigInteger num, BigInteger den, int e)
        {
            if (num.IsZero)
                return ExactInterval.Of(0);
            if (den.IsOne && ExactPoint3.BitLength(BigInteger.Abs(num)) <= 53)
                return scale(ExactInterval.Of((double)num), e);
            double x = ExactPoint3.ToDouble(num, den, e);
            return new ExactInterval(ExactInterval.Down(x), ExactInterval.Up(x));
        }

        Tri tri(int t)
        {
            if (tris == null)
                tris = new Tri[arr.Triangles.Count];
            if (tris[t] != null)
                return tris[t];
            var it = arr.Triangles[t];
            Index3i c = it.Corners;
            DMesh3 mesh = arr.Meshes[it.Mesh];
            Index3i v = mesh.GetTriangle(it.Tid);
            var x = new Tri { Mesh = it.Mesh, Vid = new int[3], C0 = c.a };
            int vp0 = arr.Canonical(arr.VertexPoints[it.Mesh][v.a]);
            int vp1 = arr.Canonical(arr.VertexPoints[it.Mesh][v.b]);
            int vp2 = arr.Canonical(arr.VertexPoints[it.Mesh][v.c]);
            int c0 = arr.Canonical(c.a), c1 = arr.Canonical(c.b), c2 = arr.Canonical(c.c);
            if (c0 == vp0 && c1 == vp1 && c2 == vp2) {
                x.Vid[0] = v.a; x.Vid[1] = v.b; x.Vid[2] = v.c;
            } else {
                int[] cs = { c0, c1, c2 }, vids = { v.a, v.b, v.c }, vp = { vp0, vp1, vp2 };
                for (int k = 0; k < 3; ++k) {
                    x.Vid[k] = -1;
                    for (int j = 0; j < 3; ++j)
                        if (vp[j] == cs[k]) { x.Vid[k] = vids[j]; break; }
                    if (x.Vid[k] < 0)
                        throw new InvalidOperationException("a corner of an input triangle is not one of its vertices");
                }
            }
            if (try_uncut_frame(it.V0, it.V1, it.V2, out int E, out IV3 d1, out IV3 d2)) {
                x.E = E;
                x.D1 = d1;
                x.D2 = d2;
            } else {
                // the local scale: 2^S at least the largest edge coordinate (0 for a degenerate triangle: no piece comes from it)
                int[] corners = { c.a, c.b, c.c };
                long sc = long.MinValue;
                for (int k = 1; k <= 2; ++k) {
                    ExactPoint3 a = P(corners[k]), b = P(corners[0]);
                    if (a.Equals(b) == false)
                        sc = Math.Max(sc, magnitude(ExactPoint3.CreateUnreduced(a.X * b.W - b.X * a.W, a.Y * b.W - b.Y * a.W, a.Z * b.W - b.Z * a.W, a.W * b.W)));
                }
                int sGrid = sc == long.MinValue ? 0 : (int)sc;
                x.E = sGrid + e;
                x.D1 = diff_exact(c.b, c.a, x.E);
                x.D2 = diff_exact(c.c, c.a, x.E);
            }
            x.N = x.D1.Cross(x.D2);
            x.NN = x.N.Dot(x.N);
            tris[t] = x;
            return x;
        }

        // corner k of T in its local frame
        static IV3 corner(Tri T, int k) => k == 0 ? default(IV3) : k == 1 ? T.D1 : T.D2;

        // the barycentric coordinate k of c (a point of T's plane, in T's local frame) in T
        static ExactInterval lambda(Tri T, IV3 c, int k)
            => div((corner(T, (k + 1) % 3) - c).Cross(corner(T, (k + 2) % 3) - c).Dot(T.N), T.NN);

        // adds s lambda_k(centroid) N_P to the corners of T, in the input's units
        void add_terms(IV3[][] acc, Tri T, Local l, ExactInterval s)
        {
            for (int k = 0; k < 3; ++k) {
                ExactInterval w = mul(s, lambda(T, l.C, k));
                ref IV3 a = ref acc[T.Mesh][T.Vid[k]];
                a = a + scale(l.N * w, 2 * T.E);
            }
        }

        internal static double mid(ExactInterval x, out double bound)
        {
            double m = 0.5 * x.Lo + 0.5 * x.Hi;
            if (double.IsNaN(m) || double.IsInfinity(m) || double.IsInfinity(x.Lo) || double.IsInfinity(x.Hi)) {
                bound = double.PositiveInfinity;
                return double.IsNaN(m) ? 0 : m;
            }
            if (x.Lo == x.Hi) {
                bound = 0;          // (an exact value)
                return x.Lo;
            }
            // (Hi - m and m - Lo rounded to nearest, then one spacing up: at least the exact differences)
            bound = ExactInterval.Up(Math.Max(x.Hi - m, m - x.Lo));
            return m;
        }

        // mid of x times 2^k, with its bound times 2^k: exact scalings unless a result is subnormal (each ScaleB then rounds once,
        // by at most 2^-1075: both together are covered by adding 2^-1074 and one spacing) or overflows (an infinite bound)
        internal static double scaled(ExactInterval x, int k, out double bound)
        {
            double m = mid(x, out double b);
            double ms = ExactPoint3.ScaleB(m, k), bs = ExactPoint3.ScaleB(b, k);
            if (double.IsInfinity(ms) || double.IsInfinity(bs) || double.IsNaN(ms)) {
                bound = double.PositiveInfinity;
                return double.IsNaN(ms) ? 0 : ms;
            }
            if (ExactPoint3.ScaleB(ms, -k) != m || ExactPoint3.ScaleB(bs, -k) != b)
                bs = ExactInterval.Up(bs + double.Epsilon);
            bound = bs;
            return ms;
        }

        // ---- contact regions ----

        BigInteger[] normal(Tri T, int t)
        {
            if (T.Normal != null)
                return T.Normal;
            Index3i c = arr.Triangles[t].Corners;
            ExactPoint3 a = arr.Exact(c.a), b = arr.Exact(c.b), d = arr.Exact(c.c);
            // homogeneous corners of input points (W = 1 for input points; general W kept for safety)
            BigInteger[] u = diff(b, a), v = diff(d, a);
            T.Normal = new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
            return T.Normal;
        }

        // (q - p) times the positive factor W_p W_q
        static BigInteger[] diff(ExactPoint3 q, ExactPoint3 p)
        {
            BigInteger sq = q.W.Sign, sp = p.W.Sign;
            return new[] { (q.X * p.W - p.X * q.W) * sq * sp, (q.Y * p.W - p.Y * q.W) * sq * sp, (q.Z * p.W - p.Z * q.W) * sq * sp };
        }

        static int dot_sign(BigInteger[] a, BigInteger[] b) => (a[0] * b[0] + a[1] * b[1] + a[2] * b[2]).Sign;

        // +1 if the first nonzero component is positive (the canonical side of a plane)
        static int canonical_sign(BigInteger[] n)
        {
            for (int k = 0; k < 3; ++k)
                if (n[k].Sign != 0)
                    return n[k].Sign;
            return 1;
        }

        sealed class Sheet { public int Mesh, O, Tri; }

        List<ContactRegion> contacts(List<ExactMeshArrangement.OutputTriangle> output, bool[] keep, bool[] flip, List<int>[] stacks, int nm)
        {
            var result = new List<ContactRegion>();
            int[][] front = boolean.CertificateFront;
            if (stacks == null || front == null)
                return result;
            // the representative pieces whose stack holds triangles of at least two meshes
            var pieces = new List<int>();
            var sheets = new Dictionary<int, Sheet[]>();
            var keyOf = new Dictionary<int, string>();
            for (int i = 0; i < output.Count; ++i) {
                List<int> st = stacks[i];
                // (RegionClipping: a piece whose centroid is on a flat edge of a region lists two triangles of it, which are one sheet)
                if (st != null && arr.RegionClipping)
                    st = one_per_region(st);
                if (st == null || st.Count < 2 || front[i] == null)
                    continue;
                bool several = false;
                foreach (int t in st)
                    several |= arr.Triangles[t].Mesh != arr.Triangles[st[0]].Mesh;
                if (several == false)
                    continue;
                BigInteger[] n0 = normal(tri(st[0]), st[0]);
                int cs = canonical_sign(n0);
                var sh = new Sheet[st.Count];
                for (int j = 0; j < st.Count; ++j) {
                    int t = st[j];
                    sh[j] = new Sheet { Mesh = arr.Triangles[t].Mesh, O = dot_sign(normal(tri(t), t), n0) * cs, Tri = t };
                }
                Array.Sort(sh, (x, y) => x.Mesh != y.Mesh ? x.Mesh.CompareTo(y.Mesh) : x.O.CompareTo(y.O));
                var key = new System.Text.StringBuilder();
                foreach (Sheet x in sh)
                    key.Append(x.Mesh).Append(x.O > 0 ? '+' : '-').Append(',');
                pieces.Add(i);
                sheets[i] = sh;
                keyOf[i] = key.ToString();
            }
            if (pieces.Count == 0)
                return result;
            // regions: pieces with the same sheets joined across shared edges (a shared edge of two coplanar pieces puts them in one
            // plane; pieces of one stack signature on different planes never share an edge in the plane of both)
            var parent = new Dictionary<int, int>();
            foreach (int i in pieces)
                parent[i] = i;
            Func<int, int> find = null;
            find = x => { int r = x; while (parent[r] != r) r = parent[r]; while (parent[x] != r) { int nx = parent[x]; parent[x] = r; x = nx; } return r; };
            var edgePieces = new Dictionary<(int, int), List<int>>();
            foreach (int i in pieces) {
                var ot = output[i];
                int[] c = { arr.Canonical(ot.A), arr.Canonical(ot.B), arr.Canonical(ot.C) };
                for (int k = 0; k < 3; ++k) {
                    int a = c[k], b = c[(k + 1) % 3];
                    var ek = a < b ? (a, b) : (b, a);
                    if (edgePieces.TryGetValue(ek, out List<int> others) == false)
                        edgePieces[ek] = others = new List<int>();
                    foreach (int o in others)
                        if (keyOf[o] == keyOf[i] && coplanar(sheets[o][0].Tri, sheets[i][0].Tri)) {
                            int ra = find(o), rb = find(i);
                            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                        }
                    others.Add(i);
                }
            }
            var members = new SortedDictionary<int, List<int>>();
            foreach (int i in pieces) {
                int r = find(i);
                if (members.TryGetValue(r, out List<int> l) == false)
                    members[r] = l = new List<int>();
                l.Add(i);
            }
            foreach (var kv in members)
                result.Add(region(output, keep, flip, front, sheets, kv.Value, nm));
            return result;
        }

        // the first triangle of each region (RegionClipping) in a stack, in the order of the stack
        List<int> one_per_region(List<int> stack)
        {
            List<int> kept = null;
            for (int j = 0; j < stack.Count; ++j) {
                bool listed = false;
                for (int k = 0; k < j && listed == false; ++k)
                    listed = arr.RegionKey(stack[k]) == arr.RegionKey(stack[j]);
                if (listed && kept == null)
                    kept = stack.GetRange(0, j);
                else if (listed == false && kept != null)
                    kept.Add(stack[j]);
            }
            return kept ?? stack;
        }

        bool coplanar(int t, int u)
        {
            BigInteger[] a = normal(tri(t), t), b = normal(tri(u), u);
            return (a[1] * b[2] - a[2] * b[1]).IsZero && (a[2] * b[0] - a[0] * b[2]).IsZero && (a[0] * b[1] - a[1] * b[0]).IsZero;
        }

        ContactRegion region(List<ExactMeshArrangement.OutputTriangle> output, bool[] keep, bool[] flip, int[][] front,
            Dictionary<int, Sheet[]> sheets, List<int> regionPieces, int nm)
        {
            Sheet[] sig = sheets[regionPieces[0]];
            var meshes = new SortedSet<int>();
            foreach (Sheet x in sig) meshes.Add(x.Mesh);
            var cr = new ContactRegion { Meshes = new List<int>(meshes).ToArray(), Sheets = sig.Length, Alternatives = new GradientTerm[0][] };
            ExactInterval area = ExactInterval.Of(0);
            foreach (int i in regionPieces) {
                var ot = output[i];
                Tri T = tri(ot.Source);
                IV3 np = local(T, ot).N;
                area = add(area, scale(ExactInterval.Sqrt(np.Dot(np)), 2 * T.E));
            }
            cr.Area = scaled(area, 0, out _);
            bool enumerable = sig.Length <= 4;
            for (int j = 1; j < sig.Length; ++j)
                enumerable &= !(sig[j].Mesh == sig[j - 1].Mesh && sig[j].O == sig[j - 1].O);
            if (enumerable == false)
                return cr;
            var seen = new HashSet<string>();
            var alternatives = new List<GradientTerm[]>();
            foreach (int[] order in permutations(sig.Length)) {
                // per piece: the sheets kept in this order and their outward side (+1: the canonical normal), against the realized
                var label = new System.Text.StringBuilder();
                var delta = new Dictionary<long, IV3>();
                var deltaOrder = new List<long>();
                bool differs = false;
                foreach (int i in regionPieces) {
                    Sheet[] sh = sheets[i];
                    var ot = output[i];
                    Sheet rep = null;
                    foreach (Sheet x in sh) if (x.Tri == ot.Source) rep = x;
                    // the winding numbers on the canonical side (front[i] is on the side the representative's normal points to)
                    int[] w = (int[])front[i].Clone();
                    if (rep.O < 0)
                        foreach (Sheet x in sh) w[x.Mesh] += x.O * rep.O;
                    var kept = new List<(Sheet sheet, int side)>();
                    bool before = boolean.InsideOf(w);
                    foreach (int j in order) {
                        Sheet x = sh[j];
                        w[x.Mesh] += x.O;
                        bool after = boolean.InsideOf(w);
                        if (before != after)
                            kept.Add((x, after ? 1 : -1));
                        before = after;
                    }
                    var realized = new List<(Sheet sheet, int side)>();
                    if (keep[i])
                        realized.Add((rep, flip[i] ? -rep.O : rep.O));
                    string k1 = describe(kept), k0 = describe(realized);
                    label.Append(k1).Append('|');
                    if (k1 == k0)
                        continue;
                    differs = true;
                    // the change: the kept sheets of this order minus the realized one, each moving with its own triangle
                    // (each sheet's term in its own triangle's frame; the area vector on the canonical side: the piece is
                    // oriented like the representative's triangle)
                    foreach (var (sheet, side) in kept)
                        add_delta(delta, deltaOrder, tri(sheet.Tri), ot, rep.O, side);
                    foreach (var (sheet, side) in realized)
                        add_delta(delta, deltaOrder, tri(sheet.Tri), ot, rep.O, -side);
                }
                if (differs == false || seen.Add(label.ToString()) == false)
                    continue;
                var terms = new List<GradientTerm>();
                foreach (long key in deltaOrder) {
                    Vector3d b;
                    Vector3d v = delta[key].Mid(0, out b);
                    terms.Add(new GradientTerm { Mesh = (int)(key >> 32), Vertex = (int)(uint)key, Value = v, Bound = b });
                }
                alternatives.Add(terms.ToArray());
            }
            cr.Alternatives = alternatives.ToArray();
            return cr;
        }

        static string describe(List<(Sheet sheet, int side)> kept)
        {
            var b = new System.Text.StringBuilder();
            foreach (var (s, side) in kept) b.Append(s.Mesh).Append(s.O > 0 ? '+' : '-').Append(side > 0 ? '^' : 'v');
            return b.ToString();
        }

        // side times lambda^T(centroid) times the area vector on the canonical side, to the corners of T, in the input's units
        void add_delta(Dictionary<long, IV3> delta, List<long> order, Tri T, ExactMeshArrangement.OutputTriangle ot, int repO, int side)
        {
            Local l = local(T, ot);
            IV3 npCanon = repO > 0 ? l.N : -l.N;
            ExactInterval s = ExactInterval.Of(side);
            for (int k = 0; k < 3; ++k) {
                long key = ((long)T.Mesh << 32) | (uint)T.Vid[k];
                if (delta.TryGetValue(key, out IV3 a) == false) {
                    a = default(IV3);
                    order.Add(key);
                }
                delta[key] = a + scale(npCanon * mul(s, lambda(T, l.C, k)), 2 * T.E);
            }
        }

        // the permutations of 0..n-1 in lexicographic order
        static IEnumerable<int[]> permutations(int n)
        {
            int[] p = new int[n];
            for (int i = 0; i < n; ++i) p[i] = i;
            while (true) {
                yield return (int[])p.Clone();
                int k = n - 2;
                while (k >= 0 && p[k] > p[k + 1]) k--;
                if (k < 0) yield break;
                int l = n - 1;
                while (p[l] < p[k]) l--;
                int t = p[k]; p[k] = p[l]; p[l] = t;
                Array.Reverse(p, k + 1, n - k - 1);
            }
        }

        // ---- interval vectors ----

        // exact identities kept exact: ExactInterval widens every result, also an exact zero, and a widened zero (+-2^-1074 in
        // grid units) would underflow when scaled to the input's units; x + 0 = x, x * 0 = 0 and x - x = 0 for a point x are exact
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool zero(ExactInterval a) => a.Lo == 0 && a.Hi == 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ExactInterval add(ExactInterval a, ExactInterval b) => zero(a) ? b : zero(b) ? a : a + b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ExactInterval sub(ExactInterval a, ExactInterval b)
            => zero(b) ? a : (a.Lo == a.Hi && b.Lo == b.Hi && a.Lo == b.Lo) ? ExactInterval.Of(0) : zero(a) ? -b : a - b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ExactInterval mul(ExactInterval a, ExactInterval b) => (zero(a) || zero(b)) ? ExactInterval.Of(0) : a * b;
        // (0 / b = 0 only for a b that excludes 0; else the whole line, as ExactInterval gives)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ExactInterval div(ExactInterval a, ExactInterval b) => (zero(a) && (b.Lo > 0 || b.Hi < 0)) ? ExactInterval.Of(0) : a / b;

        internal struct IV3
        {
            public ExactInterval X, Y, Z;     // default: [0, 0]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IV3(ExactInterval x, ExactInterval y, ExactInterval z) { X = x; Y = y; Z = z; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IV3 operator +(IV3 a, IV3 b) => new IV3(add(a.X, b.X), add(a.Y, b.Y), add(a.Z, b.Z));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IV3 operator -(IV3 a, IV3 b) => new IV3(sub(a.X, b.X), sub(a.Y, b.Y), sub(a.Z, b.Z));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IV3 operator -(IV3 a) => new IV3(-a.X, -a.Y, -a.Z);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IV3 operator *(IV3 a, ExactInterval s) => new IV3(mul(a.X, s), mul(a.Y, s), mul(a.Z, s));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IV3 operator /(IV3 a, ExactInterval s) => new IV3(div(a.X, s), div(a.Y, s), div(a.Z, s));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ExactInterval Dot(IV3 b) => add(add(mul(X, b.X), mul(Y, b.Y)), mul(Z, b.Z));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IV3 Cross(IV3 b) => new IV3(sub(mul(Y, b.Z), mul(Z, b.Y)), sub(mul(Z, b.X), mul(X, b.Z)), sub(mul(X, b.Y), mul(Y, b.X)));
            public Vector3d Mid(int k, out Vector3d bound)
            {
                double bx, by, bz;
                var v = new Vector3d(scaled(X, k, out bx), scaled(Y, k, out by), scaled(Z, k, out bz));
                bound = new Vector3d(bx, by, bz);
                return v;
            }
        }
    }
}
