// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Options for <see cref="ExactMeshAttributeTransfer"/>.
    /// </summary>
    public class AttributeTransferOptions
    {
        /// <summary> interpolate vertex UVs (DMesh3.HasVertexUVs) from inputs that carry them (default true) </summary>
        public bool TransferUVs = true;
        /// <summary> interpolate vertex normals (DMesh3.HasVertexNormals) from inputs that carry them, negating when TriangleProvenance.Reversed is true (default true) </summary>
        public bool TransferNormals = true;
        /// <summary> interpolate vertex colors (DMesh3.HasVertexColors) from inputs that carry them (default true) </summary>
        public bool TransferColors = true;
        /// <summary>
        /// When true, split shared vertices of the result mesh wherever incident triangle corners disagree on an
        /// interpolated attribute (beyond the tolerances below) or on TriangleGroup (when <see cref="SplitGroupSeams"/>
        /// is true). Duplicate vertices are created at the exact same 3D coordinates, preventing attribute bleeding across
        /// UV islands, hard creases, or material seams without altering geometry. Note: splitting vertices opens edges
        /// along seams (and may produce bowtie vertices at checkerboard-like multi-seam corners), so use this as a
        /// rendering/export post-pass rather than before further solid booleans (default false).
        /// </summary>
        public bool SplitAttributeSeams = false;
        /// <summary> when <see cref="SplitAttributeSeams"/> is true and result.HasTriangleGroups, split vertices shared across different triangle groups (default true) </summary>
        public bool SplitGroupSeams = true;
        /// <summary> non-negative Euclidean UV distance threshold above which corners at a vertex are split (default 1e-5) </summary>
        public double UVSeamTolerance = 1e-5;
        /// <summary> non-negative normal angle threshold in degrees (values >= 180 disable normal seam splitting; default 1.0) </summary>
        public double NormalSeamAngleDeg = 1.0;
        /// <summary> non-negative Euclidean RGB color distance threshold above which corners at a vertex are split (default 1e-4) </summary>
        public double ColorSeamTolerance = 1e-4;
        /// <summary> clamp barycentric weights to [0, 1] and renormalize to guard against tiny rounding drift outside the source triangle (default true) </summary>
        public bool ClampBarycentric = true;
    }

    /// <summary>
    /// Double-precision per-triangle-corner attributes evaluated by <see cref="ExactMeshAttributeTransfer.EvaluateCorners"/>.
    /// Corner k (0, 1, 2) of triangle tid is stored at slot <c>3 * tid + k</c>.
    /// </summary>
    public sealed class CornerAttributes
    {
        /// <summary> length MaxTriangleID * 3: interpolated double-precision UVs, or null if no input had UVs </summary>
        public Vector2d[] UVs;
        /// <summary> length MaxTriangleID * 3: true where UVs[slot] came from an input mesh with VertexUVs </summary>
        public bool[] HasUV;
        /// <summary> length MaxTriangleID * 3: interpolated unit double-precision normals (flipped when Reversed), or null if no input had normals </summary>
        public Vector3d[] Normals;
        /// <summary> length MaxTriangleID * 3: true where Normals[slot] came from an input mesh with VertexNormals </summary>
        public bool[] HasNormal;
        /// <summary> length MaxTriangleID * 3: interpolated double-precision RGB colors, or null if no input had colors </summary>
        public Vector3d[] Colors;
        /// <summary> length MaxTriangleID * 3: true where Colors[slot] came from an input mesh with VertexColors </summary>
        public bool[] HasColor;
    }

    /// <summary>
    /// Transfers vertex attributes (UVs, normals, colors) from boolean input meshes onto an <see cref="ExactMeshBoolean"/>
    /// result using <see cref="TriangleProvenance"/> and local-frame barycentric coordinates shifted to each source triangle's
    /// first corner (avoiding catastrophic cancellation at large coordinates such as UTM 1e7 and extreme 2^k scales).
    /// Optionally splits shared vertices along attribute or triangle-group seams so rendering attributes never bleed across
    /// creases, UV island boundaries, or material interfaces.
    /// </summary>
    public static class ExactMeshAttributeTransfer
    {
        static bool is_finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        static bool is_finite(Vector3d v) { return is_finite(v.x) && is_finite(v.y) && is_finite(v.z); }

        static double max_abs(Vector3d v)
        {
            double ax = Math.Abs(v.x), ay = Math.Abs(v.y), az = Math.Abs(v.z);
            return Math.Max(ax, Math.Max(ay, az));
        }

        // Exact unbiased binary exponent e so that 2^e <= m < 2^(e+1) for positive finite m (including subnormals).
        static int ilogb_pos(double m)
        {
            long bits = BitConverter.DoubleToInt64Bits(m);
            int exp = (int)((bits >> 52) & 0x7FFL);
            if (exp == 0) {
                bits = BitConverter.DoubleToInt64Bits(m * 18014398509481984.0); // 2^54
                exp = (int)((bits >> 52) & 0x7FFL);
                return exp - 1023 - 54;
            }
            return exp - 1023;
        }

        // Exact multiplication by 2^shift using at most two power-of-two multiplies (no overflow/underflow on subnormals).
        static double ldexp_exact(double x, int shift)
        {
            if (x == 0 || shift == 0)
                return x;
            while (shift > 512) {
                x *= BitConverter.Int64BitsToDouble((long)(1023 + 512) << 52);
                shift -= 512;
            }
            while (shift < -512) {
                x *= BitConverter.Int64BitsToDouble((long)(1023 - 512) << 52);
                shift += 512;
            }
            return x * BitConverter.Int64BitsToDouble((long)(shift + 1023) << 52);
        }

        static Vector3d scale_pow2(Vector3d v, int shift)
        {
            return new Vector3d(ldexp_exact(v.x, shift), ldexp_exact(v.y, shift), ldexp_exact(v.z, shift));
        }

        static Vector3d normalize_safe(Vector3d n)
        {
            if (!is_finite(n))
                return Vector3d.Zero;
            double m = max_abs(n);
            if (m <= 0)
                return Vector3d.Zero;
            Vector3d s = scale_pow2(n, -ilogb_pos(m));
            double len = s.Length;
            return (len > 0 && is_finite(len)) ? s / len : Vector3d.Zero;
        }

        /// <summary>
        /// Compute barycentric coordinates (w0, w1, w2) of point <paramref name="p"/> with respect to triangle
        /// (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>) in the triangle's local frame shifted to
        /// <paramref name="a"/> and scaled by an exact power of two (immune to cancellation at UTM 1e7 offsets and
        /// overflow/underflow across the full normal and subnormal IEEE-754 range).
        /// </summary>
        public static Vector3d LocalBarycentric(Vector3d p, Vector3d a, Vector3d b, Vector3d c, bool clamp = true)
        {
            if (!is_finite(p) || !is_finite(a) || !is_finite(b) || !is_finite(c))
                return new Vector3d(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);

            Vector3d v0 = b - a;
            Vector3d v1 = c - a;
            Vector3d v2 = p - a;
            if (!is_finite(v0) || !is_finite(v1) || !is_finite(v2))
                return new Vector3d(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);

            double edgeScale = Math.Max(max_abs(v0), max_abs(v1));
            if (edgeScale <= 0)
                return new Vector3d(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);

            // Power-of-two scaling of edge vectors into [0, 2); if p is vastly farther than the triangle, scale v2
            // by an extra power-of-two factor so dot products never overflow, then restore the factor on w1, w2.
            int eShift = -ilogb_pos(edgeScale);
            v0 = scale_pow2(v0, eShift);
            v1 = scale_pow2(v1, eShift);
            int pExtraShift = 0;
            double v2Max = max_abs(v2);
            if (v2Max > 0) {
                int pExp = ilogb_pos(v2Max);
                if (pExp + eShift > 200)
                    pExtraShift = -(pExp + eShift);
            }
            v2 = scale_pow2(v2, eShift + pExtraShift);

            double d00 = v0.Dot(v0);
            double d01 = v0.Dot(v1);
            double d11 = v1.Dot(v1);
            double d20 = v2.Dot(v0);
            double d21 = v2.Dot(v1);
            double prod = d00 * d11;
            double denom = prod - d01 * d01;

            double w1, w2;
            if (prod <= 0 || !is_finite(denom) || Math.Abs(denom) <= 1e-30 * prod) {
                // Degenerate or near-collinear source triangle: project onto the longest edge
                Vector3d vbc = v1 - v0;
                double d22 = vbc.Dot(vbc);
                if (d00 >= d11 && d00 >= d22 && d00 > 0) {
                    w1 = d20 / d00;
                    w2 = 0;
                } else if (d11 >= d22 && d11 > 0) {
                    w1 = 0;
                    w2 = d21 / d11;
                } else if (d22 > 0) {
                    Vector3d v0Scaled = pExtraShift == 0 ? v0 : scale_pow2(v0, pExtraShift);
                    double t = (v2 - v0Scaled).Dot(vbc) / d22;
                    w1 = (pExtraShift == 0 ? 1.0 : ldexp_exact(1.0, pExtraShift)) - t;
                    w2 = t;
                } else {
                    return new Vector3d(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);
                }
            } else {
                w1 = (d11 * d20 - d01 * d21) / denom;
                w2 = (d00 * d21 - d01 * d20) / denom;
            }
            if (pExtraShift != 0) {
                w1 = ldexp_exact(w1, -pExtraShift);
                w2 = ldexp_exact(w2, -pExtraShift);
            }
            double w0 = 1.0 - w1 - w2;
            if (!is_finite(w0) || !is_finite(w1) || !is_finite(w2))
                return new Vector3d(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);

            if (clamp) {
                if (w0 < 0 || w1 < 0 || w2 < 0) {
                    w0 = Math.Max(0.0, w0);
                    w1 = Math.Max(0.0, w1);
                    w2 = Math.Max(0.0, w2);
                    double sum = w0 + w1 + w2;
                    if (sum > 0 && is_finite(sum)) {
                        double invSum = 1.0 / sum;
                        w0 *= invSum;
                        w1 *= invSum;
                        w2 *= invSum;
                    } else {
                        w0 = w1 = w2 = 1.0 / 3.0;
                    }
                }
            }
            return new Vector3d(w0, w1, w2);
        }

        /// <summary> Double-precision local-frame barycentric UV interpolation. </summary>
        public static Vector2d InterpolateCornerUV(Vector3d p, Vector3d a, Vector3d b, Vector3d c, Vector2d uvA, Vector2d uvB, Vector2d uvC, bool clamp = true)
        {
            Vector3d w = LocalBarycentric(p, a, b, c, clamp);
            return new Vector2d(w.x * uvA.x + w.y * uvB.x + w.z * uvC.x, w.x * uvA.y + w.y * uvB.y + w.z * uvC.y);
        }

        /// <summary> Double-precision local-frame barycentric normal interpolation (power-of-two scaled unit vector, negated when <paramref name="reversed"/> is true). </summary>
        public static Vector3d InterpolateCornerNormal(Vector3d p, Vector3d a, Vector3d b, Vector3d c, Vector3d nA, Vector3d nB, Vector3d nC, bool reversed, bool clamp = true)
        {
            Vector3d w = LocalBarycentric(p, a, b, c, clamp);
            Vector3d n = new Vector3d(
                w.x * nA.x + w.y * nB.x + w.z * nC.x,
                w.x * nA.y + w.y * nB.y + w.z * nC.y,
                w.x * nA.z + w.y * nB.z + w.z * nC.z);
            if (reversed)
                n = -n;
            return normalize_safe(n);
        }

        /// <summary> Double-precision local-frame barycentric RGB color interpolation. </summary>
        public static Vector3d InterpolateCornerColor(Vector3d p, Vector3d a, Vector3d b, Vector3d c, Vector3d cA, Vector3d cB, Vector3d cC, bool clamp = true)
        {
            Vector3d w = LocalBarycentric(p, a, b, c, clamp);
            return new Vector3d(
                w.x * cA.x + w.y * cB.x + w.z * cC.x,
                w.x * cA.y + w.y * cB.y + w.z * cC.y,
                w.x * cA.z + w.y * cB.z + w.z * cC.z);
        }

        /// <summary>
        /// Evaluate double-precision per-triangle-corner attributes for every live triangle in <paramref name="result"/>
        /// without modifying <paramref name="result"/>.
        /// </summary>
        public static CornerAttributes EvaluateCorners(DMesh3 result, TriangleProvenance[] provenance, IReadOnlyList<DMesh3> inputs, AttributeTransferOptions options = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            options = options ?? new AttributeTransferOptions();

            bool anyUV = false, anyNorm = false, anyCol = false;
            for (int i = 0; i < inputs.Count; ++i) {
                DMesh3 m = inputs[i];
                if (m == null) continue;
                if (options.TransferUVs && m.HasVertexUVs) anyUV = true;
                if (options.TransferNormals && m.HasVertexNormals) anyNorm = true;
                if (options.TransferColors && m.HasVertexColors) anyCol = true;
            }

            int slots = result.MaxTriangleID * 3;
            var corners = new CornerAttributes();
            if (anyUV) {
                corners.UVs = new Vector2d[slots];
                corners.HasUV = new bool[slots];
            }
            if (anyNorm) {
                corners.Normals = new Vector3d[slots];
                corners.HasNormal = new bool[slots];
            }
            if (anyCol) {
                corners.Colors = new Vector3d[slots];
                corners.HasColor = new bool[slots];
            }
            if (!anyUV && !anyNorm && !anyCol)
                return corners;

            foreach (int tid in result.TriangleIndices()) {
                if (tid >= provenance.Length)
                    continue;
                TriangleProvenance tp = provenance[tid];
                if (!tp.IsValid || tp.MeshIndex >= inputs.Count)
                    continue;
                DMesh3 src = inputs[tp.MeshIndex];
                if (src == null || !src.IsTriangle(tp.SourceTriangle))
                    continue;

                Index3i st = src.GetTriangle(tp.SourceTriangle);
                Vector3d sa = src.GetVertex(st.a);
                Vector3d sb = src.GetVertex(st.b);
                Vector3d sc = src.GetVertex(st.c);

                bool srcUV = anyUV && src.HasVertexUVs;
                bool srcNorm = anyNorm && src.HasVertexNormals;
                bool srcCol = anyCol && src.HasVertexColors;
                if (!srcUV && !srcNorm && !srcCol)
                    continue;

                Vector2d uvA = default, uvB = default, uvC = default;
                if (srcUV) {
                    Vector2f fa = src.GetVertexUV(st.a), fb = src.GetVertexUV(st.b), fc = src.GetVertexUV(st.c);
                    uvA = new Vector2d(fa.x, fa.y);
                    uvB = new Vector2d(fb.x, fb.y);
                    uvC = new Vector2d(fc.x, fc.y);
                }
                Vector3d nA = default, nB = default, nC = default;
                if (srcNorm) {
                    Vector3f na = src.GetVertexNormal(st.a), nb = src.GetVertexNormal(st.b), nc = src.GetVertexNormal(st.c);
                    nA = new Vector3d(na.x, na.y, na.z);
                    nB = new Vector3d(nb.x, nb.y, nb.z);
                    nC = new Vector3d(nc.x, nc.y, nc.z);
                }
                Vector3d cA = default, cB = default, cC = default;
                if (srcCol) {
                    Vector3f ca = src.GetVertexColor(st.a), cb = src.GetVertexColor(st.b), cc = src.GetVertexColor(st.c);
                    cA = new Vector3d(ca.x, ca.y, ca.z);
                    cB = new Vector3d(cb.x, cb.y, cb.z);
                    cC = new Vector3d(cc.x, cc.y, cc.z);
                }

                Index3i rt = result.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    int vid = k == 0 ? rt.a : (k == 1 ? rt.b : rt.c);
                    Vector3d p = result.GetVertex(vid);
                    Vector3d w = LocalBarycentric(p, sa, sb, sc, options.ClampBarycentric);
                    int slot = 3 * tid + k;
                    if (srcUV) {
                        corners.UVs[slot] = new Vector2d(w.x * uvA.x + w.y * uvB.x + w.z * uvC.x, w.x * uvA.y + w.y * uvB.y + w.z * uvC.y);
                        corners.HasUV[slot] = true;
                    }
                    if (srcNorm) {
                        Vector3d n = new Vector3d(
                            w.x * nA.x + w.y * nB.x + w.z * nC.x,
                            w.x * nA.y + w.y * nB.y + w.z * nC.y,
                            w.x * nA.z + w.y * nB.z + w.z * nC.z);
                        if (tp.Reversed)
                            n = -n;
                        corners.Normals[slot] = normalize_safe(n);
                        corners.HasNormal[slot] = true;
                    }
                    if (srcCol) {
                        corners.Colors[slot] = new Vector3d(
                            w.x * cA.x + w.y * cB.x + w.z * cC.x,
                            w.x * cA.y + w.y * cB.y + w.z * cC.y,
                            w.x * cA.z + w.y * cB.z + w.z * cC.z);
                        corners.HasColor[slot] = true;
                    }
                }
            }
            return corners;
        }

        /// <summary>
        /// Transfer vertex UVs, normals, and colors onto <paramref name="result"/> from <paramref name="inputs"/> using
        /// <paramref name="provenance"/>. When <see cref="AttributeTransferOptions.SplitAttributeSeams"/> is true,
        /// vertices whose incident corners disagree on attributes (or triangle groups) are split into duplicate vertices
        /// at identical 3D coordinates. Returns the number of newly created split vertices (0 when seam splitting is off).
        /// </summary>
        public static int Transfer(DMesh3 result, TriangleProvenance[] provenance, IReadOnlyList<DMesh3> inputs, AttributeTransferOptions options = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            options = options ?? new AttributeTransferOptions();
            validate_options(options);

            CornerAttributes corners = EvaluateCorners(result, provenance, inputs, options);
            return ApplyCornerAttributes(result, corners, options);
        }

        /// <summary>
        /// Convenience overload reading Result, ResultProvenance, and input meshes directly from a computed
        /// <see cref="ExactMeshBoolean"/> instance (requires <see cref="ExactMeshBoolean.TrackFaceProvenance"/> to be true).
        /// </summary>
        public static int Transfer(ExactMeshBoolean boolean, AttributeTransferOptions options = null)
        {
            if (boolean == null) throw new ArgumentNullException(nameof(boolean));
            if (boolean.Result == null) throw new InvalidOperationException("ExactMeshBoolean has no Result (call Compute() first).");
            if (boolean.ResultProvenance == null) throw new InvalidOperationException("ExactMeshBoolean.ResultProvenance is null (set TrackFaceProvenance = true before Compute()).");
            IReadOnlyList<DMesh3> inputs = boolean.Meshes != null
                ? (IReadOnlyList<DMesh3>)boolean.Meshes
                : new DMesh3[] { boolean.MeshA, boolean.MeshB };
            return Transfer(boolean.Result, boolean.ResultProvenance, inputs, options);
        }

        static void validate_options(AttributeTransferOptions options)
        {
            if (double.IsNaN(options.UVSeamTolerance) || options.UVSeamTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "UVSeamTolerance must be non-negative and not NaN.");
            if (double.IsNaN(options.NormalSeamAngleDeg) || options.NormalSeamAngleDeg < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "NormalSeamAngleDeg must be non-negative and not NaN.");
            if (double.IsNaN(options.ColorSeamTolerance) || options.ColorSeamTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "ColorSeamTolerance must be non-negative and not NaN.");
        }

        /// <summary>
        /// Apply pre-evaluated <see cref="CornerAttributes"/> onto <paramref name="result"/>, optionally splitting
        /// shared vertices along attribute or group discontinuities. Returns the number of split vertices created.
        /// </summary>
        public static int ApplyCornerAttributes(DMesh3 result, CornerAttributes corners, AttributeTransferOptions options = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            options = options ?? new AttributeTransferOptions();
            validate_options(options);

            int requiredSlots = result.MaxTriangleID * 3;
            bool doUV = corners.UVs != null;
            bool doNorm = corners.Normals != null;
            bool doCol = corners.Colors != null;
            if (doUV && (corners.HasUV == null || corners.UVs.Length < requiredSlots || corners.HasUV.Length < requiredSlots))
                throw new ArgumentException("CornerAttributes.UVs / HasUV must be non-null and have length at least result.MaxTriangleID * 3.", nameof(corners));
            if (doNorm && (corners.HasNormal == null || corners.Normals.Length < requiredSlots || corners.HasNormal.Length < requiredSlots))
                throw new ArgumentException("CornerAttributes.Normals / HasNormal must be non-null and have length at least result.MaxTriangleID * 3.", nameof(corners));
            if (doCol && (corners.HasColor == null || corners.Colors.Length < requiredSlots || corners.HasColor.Length < requiredSlots))
                throw new ArgumentException("CornerAttributes.Colors / HasColor must be non-null and have length at least result.MaxTriangleID * 3.", nameof(corners));

            bool doGroupSplit = options.SplitAttributeSeams && options.SplitGroupSeams && result.HasTriangleGroups;
            if (!doUV && !doNorm && !doCol && !doGroupSplit)
                return 0;

            if (doUV && !result.HasVertexUVs)
                result.EnableVertexUVs(Vector2f.Zero);
            if (doNorm && !result.HasVertexNormals)
                result.EnableVertexNormals(Vector3f.AxisY);
            if (doCol && !result.HasVertexColors)
                result.EnableVertexColors(Vector3f.One);

            int origMaxV = result.MaxVertexID;
            var incident = new List<Index2i>[origMaxV];
            foreach (int tid in result.TriangleIndices()) {
                Index3i tv = result.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    int vid = k == 0 ? tv.a : (k == 1 ? tv.b : tv.c);
                    var list = incident[vid];
                    if (list == null)
                        incident[vid] = list = new List<Index2i>(6);
                    list.Add(new Index2i(tid, k));
                }
            }

            if (!options.SplitAttributeSeams) {
                for (int vid = 0; vid < origMaxV; ++vid) {
                    List<Index2i> list = incident[vid];
                    if (list == null || list.Count == 0)
                        continue;
                    write_vertex_attributes(result, vid, list, corners, doUV, doNorm, doCol);
                }
                return 0;
            }

            double uvTolSq = double.IsInfinity(options.UVSeamTolerance) ? double.PositiveInfinity : options.UVSeamTolerance * options.UVSeamTolerance;
            double colTolSq = double.IsInfinity(options.ColorSeamTolerance) ? double.PositiveInfinity : options.ColorSeamTolerance * options.ColorSeamTolerance;
            double clampedAngle = Math.Min(180.0, options.NormalSeamAngleDeg);
            double normCosMin = clampedAngle >= 180.0 ? -1.0 : Math.Cos(clampedAngle * (Math.PI / 180.0));
            int splitCount = 0;

            for (int vid = 0; vid < origMaxV; ++vid) {
                List<Index2i> list = incident[vid];
                if (list == null || list.Count == 0)
                    continue;
                if (list.Count == 1) {
                    write_vertex_attributes(result, vid, list, corners, doUV, doNorm, doCol);
                    continue;
                }

                // Deterministic clustering in ascending (tid, corner) order: a corner joins the first cluster
                // whose members are all compatible with it (prevents transitive drift across a smooth fan).
                var clusters = new List<List<Index2i>>();
                for (int i = 0; i < list.Count; ++i) {
                    Index2i c = list[i];
                    int matched = -1;
                    for (int ci = 0; ci < clusters.Count; ++ci) {
                        var cl = clusters[ci];
                        bool compat = true;
                        for (int j = 0; j < cl.Count; ++j) {
                            if (!corners_compatible(result, corners, c, cl[j], doUV, doNorm, doCol, doGroupSplit, uvTolSq, normCosMin, colTolSq)) {
                                compat = false;
                                break;
                            }
                        }
                        if (compat) {
                            matched = ci;
                            break;
                        }
                    }
                    if (matched >= 0) {
                        clusters[matched].Add(c);
                    } else {
                        clusters.Add(new List<Index2i> { c });
                    }
                }

                write_vertex_attributes(result, vid, clusters[0], corners, doUV, doNorm, doCol);
                if (clusters.Count <= 1)
                    continue;

                Vector3d pos = result.GetVertex(vid);
                for (int ci = 1; ci < clusters.Count; ++ci) {
                    int newVid = result.AppendVertex(pos);
                    List<Index2i> cl = clusters[ci];
                    int moved = 0;
                    for (int j = 0; j < cl.Count; ++j) {
                        int tid = cl[j].a;
                        Index3i tv = result.GetTriangle(tid);
                        tv[cl[j].b] = newVid;
                        if (result.SetTriangle(tid, tv, false) == MeshResult.Ok)
                            moved++;
                    }
                    if (moved > 0) {
                        splitCount++;
                        write_vertex_attributes(result, newVid, cl, corners, doUV, doNorm, doCol);
                    } else {
                        result.RemoveVertex(newVid, true, false);
                    }
                }
            }

            return splitCount;
        }

        static bool corners_compatible(
            DMesh3 result, CornerAttributes corners, Index2i a, Index2i b,
            bool doUV, bool doNorm, bool doCol, bool doGroupSplit,
            double uvTolSq, double normCosMin, double colTolSq)
        {
            if (doGroupSplit && result.GetTriangleGroup(a.a) != result.GetTriangleGroup(b.a))
                return false;
            int sa = 3 * a.a + a.b;
            int sb = 3 * b.a + b.b;
            if (doUV) {
                bool ha = corners.HasUV[sa], hb = corners.HasUV[sb];
                if (ha != hb)
                    return false;
                if (ha) {
                    Vector2d d = corners.UVs[sa] - corners.UVs[sb];
                    if (d.x * d.x + d.y * d.y > uvTolSq)
                        return false;
                }
            }
            if (doNorm) {
                bool ha = corners.HasNormal[sa], hb = corners.HasNormal[sb];
                if (ha != hb)
                    return false;
                if (ha && normCosMin > -1.0) {
                    double dot = corners.Normals[sa].Dot(corners.Normals[sb]);
                    if (dot < normCosMin)
                        return false;
                }
            }
            if (doCol) {
                bool ha = corners.HasColor[sa], hb = corners.HasColor[sb];
                if (ha != hb)
                    return false;
                if (ha) {
                    Vector3d d = corners.Colors[sa] - corners.Colors[sb];
                    if (d.x * d.x + d.y * d.y + d.z * d.z > colTolSq)
                        return false;
                }
            }
            return true;
        }

        static void write_vertex_attributes(DMesh3 result, int vid, List<Index2i> list, CornerAttributes corners, bool doUV, bool doNorm, bool doCol)
        {
            if (doUV) {
                Vector2d sum = Vector2d.Zero;
                int count = 0;
                for (int i = 0; i < list.Count; ++i) {
                    int s = 3 * list[i].a + list[i].b;
                    if (corners.HasUV[s]) {
                        sum += corners.UVs[s];
                        count++;
                    }
                }
                if (count > 0) {
                    sum /= count;
                    result.SetVertexUV(vid, new Vector2f((float)sum.x, (float)sum.y));
                }
            }
            if (doNorm) {
                Vector3d sum = Vector3d.Zero;
                Vector3d firstValid = Vector3d.Zero;
                int count = 0;
                for (int i = 0; i < list.Count; ++i) {
                    int s = 3 * list[i].a + list[i].b;
                    if (corners.HasNormal[s]) {
                        Vector3d n = corners.Normals[s];
                        if (count == 0)
                            firstValid = n;
                        sum += n;
                        count++;
                    }
                }
                if (count > 0) {
                    Vector3d avg = normalize_safe(sum);
                    if (avg == Vector3d.Zero)
                        avg = firstValid;
                    result.SetVertexNormal(vid, new Vector3f((float)avg.x, (float)avg.y, (float)avg.z));
                }
            }
            if (doCol) {
                Vector3d sum = Vector3d.Zero;
                int count = 0;
                for (int i = 0; i < list.Count; ++i) {
                    int s = 3 * list[i].a + list[i].b;
                    if (corners.HasColor[s]) {
                        sum += corners.Colors[s];
                        count++;
                    }
                }
                if (count > 0) {
                    sum /= count;
                    result.SetVertexColor(vid, new Vector3f((float)sum.x, (float)sum.y, (float)sum.z));
                }
            }
        }
    }
}
