// The transforms of the thingi-contact class (README.md, "Thingi10K pairs with exact contact"): B is placed against A
// so that the two solids touch or overlap exactly (coplanar faces, a one-ulp offset, a mirror image). Everything is a
// translation, a power-of-two scale or a reflection; nothing rounds. Every sum is checked with TwoSum (Knuth): if a
// coordinate of the result is not the exact sum, the placement is refused, never rounded. No dependency on the
// benchmark tool: tests/geometry3Sharp.Tests links this file.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace BoolCmp
{
    public enum ContactVariant { FaceContact, PartialOverlap, UlpShift, UlpShiftFew, Mirror }

    public sealed class ContactResult
    {
        public DMesh3 A, B;
        public Dictionary<string, object> Info = new Dictionary<string, object>();
        public string Error;                 // null if the placement is exact
    }

    public static class ContactTransforms
    {
        public static readonly string[] VariantNames = { "face-contact", "partial-overlap", "ulp-shift", "ulp-shift-few", "mirror" };

        public static ContactVariant ParseVariant(string name)
        {
            int i = Array.IndexOf(VariantNames, name);
            if (i < 0)
                throw new ArgumentException("unknown variant " + name);
            return (ContactVariant)i;
        }

        // ---- exact arithmetic helpers

        /// <summary> True if a + b is exactly representable (TwoSum: the rounding error is zero). Not for infinities / NaN. </summary>
        public static bool ExactSum(double a, double b, out double sum)
        {
            sum = a + b;
            double bb = sum - a;
            double err = (a - (sum - bb)) + (b - bb);
            return err == 0 && !double.IsNaN(sum) && !double.IsInfinity(sum);
        }

        /// <summary> 2^k as a double; exact for the normal range. </summary>
        public static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);     // k in [-1022, 1023]

        /// <summary> floor(log2(|x|)) of a normal, non-zero double. (Math.ILogB, Math.ScaleB and Math.BitIncrement do not exist on net48.) </summary>
        public static int ILog2(double x) => (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023;

        /// <summary> The next double above a finite positive x. </summary>
        public static double NextUp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);

        /// <summary> The power of two s = 2^-k so that maxDim * s is in [0.5, 1). </summary>
        public static double UnitScale(double maxDim)
        {
            int e = ILog2(maxDim) + 1;       // maxDim in [2^(e-1), 2^e)
            return Pow2(-e);
        }

        static bool InNormalRange(double v) => v == 0 || (Math.Abs(v) >= 1e-290 && Math.Abs(v) <= 1e290);

        /// <summary> Scale all vertices by the power of two s; false (mesh unchanged) if a coordinate would leave the
        /// normal range, so that the scaling is exact. </summary>
        public static bool ScaleExact(DMesh3 m, double s)
        {
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                if (!InNormalRange(v.x * s) || !InNormalRange(v.y * s) || !InNormalRange(v.z * s))
                    return false;
            }
            foreach (int vid in m.VertexIndices())
                m.SetVertex(vid, m.GetVertex(vid) * s);
            return true;
        }

        /// <summary> t added to every vertex if every coordinate sum is exact; false (mesh unchanged) otherwise. </summary>
        public static bool TranslateExact(DMesh3 m, Vector3d t)
        {
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                if (!ExactSum(v.x, t.x, out _) || !ExactSum(v.y, t.y, out _) || !ExactSum(v.z, t.z, out _))
                    return false;
            }
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                m.SetVertex(vid, new Vector3d(v.x + t.x, v.y + t.y, v.z + t.z));
            }
            return true;
        }

        /// <summary> x rounded to a multiple of the power of two q (exact). </summary>
        public static double RoundToGrid(double x, double q) => Math.Round(x / q) * q;

        /// <summary> Scale to a longest side in [0.5, 1) and move the box centre to the origin, up to a grid of 1/32 of the
        /// longest side (the move is skipped, not rounded, if it is not exact). Both steps are exact. </summary>
        public static bool NormalizeExact(DMesh3 m)
        {
            AxisAlignedBox3d bounds = m.GetBounds();
            if (!(bounds.MaxDim > 0) || !InNormalRange(bounds.MaxDim))
                return false;
            if (!ScaleExact(m, UnitScale(bounds.MaxDim)))
                return false;
            bounds = m.GetBounds();
            double q = Pow2(ILog2(bounds.MaxDim) - 5);
            Vector3d c = bounds.Center;
            TranslateExact(m, new Vector3d(-RoundToGrid(c.x, q), -RoundToGrid(c.y, q), -RoundToGrid(c.z, q)));   // inexact: stays
            return true;
        }

        // ---- planar extreme faces

        public struct PlanarFace
        {
            public double Plane;        // the extreme coordinate
            public double Area;         // area of the triangles with all three vertices exactly (bit-equal) in the plane
            public double Lo0, Hi0, Lo1, Hi1;    // box of those triangles in the other two axes (ascending axis order)
        }

        static double Get(Vector3d v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        /// <summary> The planar face of the mesh at its extreme (max or min) coordinate along axis: the triangles whose
        /// three vertices all have exactly that coordinate. Area 0 if the extreme is reached by vertices or edges only. </summary>
        public static PlanarFace ExtremeFace(DMesh3 m, int axis, bool atMax)
        {
            double ext = atMax ? double.NegativeInfinity : double.PositiveInfinity;
            foreach (int vid in m.VertexIndices()) {
                double c = Get(m.GetVertex(vid), axis);
                ext = atMax ? Math.Max(ext, c) : Math.Min(ext, c);
            }
            var f = new PlanarFace { Plane = ext, Lo0 = double.PositiveInfinity, Lo1 = double.PositiveInfinity, Hi0 = double.NegativeInfinity, Hi1 = double.NegativeInfinity };
            int o0 = (axis + 1) % 3, o1 = (axis + 2) % 3;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                if (Get(a, axis) != ext || Get(b, axis) != ext || Get(c, axis) != ext)
                    continue;
                f.Area += 0.5 * (b - a).Cross(c - a).Length;
                foreach (Vector3d p in new[] { a, b, c }) {
                    f.Lo0 = Math.Min(f.Lo0, Get(p, o0)); f.Hi0 = Math.Max(f.Hi0, Get(p, o0));
                    f.Lo1 = Math.Min(f.Lo1, Get(p, o1)); f.Hi1 = Math.Max(f.Hi1, Get(p, o1));
                }
            }
            return f;
        }

        // ---- the placements

        /// <summary>
        /// Builds the pair of a variant from two clean meshes (not modified). axis 0..2 and sign (+1 / -1) pick the direction
        /// of the contact or the shift; param is a small integer of the pair list (the embedding depth 2^-param of
        /// partial-overlap; the number of ulps of ulp-shift-few; the rank of the mirror plane).
        /// Both meshes are first normalized exactly (power-of-two scale, grid translation). face-contact and partial-overlap
        /// try the six directions starting at the given one and take the first with a planar face on both sides and an exact placement, else
        /// the first exact one (kind "bbox": contact through vertices or edges only).
        /// </summary>
        public static ContactResult Build(DMesh3 a0, DMesh3 b0, ContactVariant variant, int axis, int sign, int param)
        {
            var r = new ContactResult { A = new DMesh3(a0, false, true, false, false) };
            if (!NormalizeExact(r.A)) {
                r.Error = "A cannot be normalized exactly";
                return r;
            }
            r.Info["variant"] = VariantNames[(int)variant];
            r.Info["axis"] = axis; r.Info["sign"] = sign; r.Info["param"] = param;
            switch (variant) {
                case ContactVariant.FaceContact:
                case ContactVariant.PartialOverlap:
                    return Contact(r, b0, variant == ContactVariant.PartialOverlap, axis, sign, param);
                case ContactVariant.UlpShift:
                    return Ulps(r, axis, sign, 1);
                case ContactVariant.UlpShiftFew:
                    return Ulps(r, axis, sign, 2 + Math.Abs(param) % 4);    // 2..5
                default:
                    return Mirror(r, axis, param);
            }
        }

        static ContactResult Contact(ContactResult r, DMesh3 b0, bool embed, int axis, int sign, int param)
        {
            DMesh3 b = new DMesh3(b0, false, true, false, false);
            if (!NormalizeExact(b)) {
                r.Error = "B cannot be normalized exactly";
                return r;
            }
            DMesh3 a = r.A;
            AxisAlignedBox3d boxA = a.GetBounds(), boxB = b.GetBounds();
            int depth = embed ? Math.Abs(param) % 3 : -1;           // B is embedded by 2^-depth of its extent along the axis (depth 0: flush)
            double q = Pow2(ILog2(boxA.MaxDim) - 5);
            // the candidates: the preferred direction first, then the others in a fixed order
            var cands = new List<(int axis, int sign)>();
            int first = ((axis % 3 + 3) % 3) * 2 + (sign >= 0 ? 0 : 1);
            for (int k = 0; k < 6; ++k)
                cands.Add((((first + k) % 6) / 2, ((first + k) % 2) == 0 ? 1 : -1));
            DMesh3 best = null; Dictionary<string, object> bestInfo = null;
            string firstError = null;
            foreach (var (ax, sg) in cands) {
                // B lies on the sg side of A: A's face at the extreme along ax in direction sg touches B's face at the other extreme
                PlanarFace fa = ExtremeFace(a, ax, sg > 0);
                PlanarFace fb = ExtremeFace(b, ax, sg < 0);                               // B's near face
                PlanarFace fbFar = ExtremeFace(b, ax, sg > 0);                            // B's far face (flush case)
                double pA = fa.Plane, pB = fb.Plane;
                double h = Math.Abs(fbFar.Plane - fb.Plane);
                double target = pA;
                if (embed) {
                    double depthShift = Pow2(-depth) * h;                                 // exact: power of two times a double
                    if (!ExactSum(pA, -sg * depthShift, out target)) { firstError ??= "embedding depth not exact"; continue; }
                }
                if (!ExactSum(target, -pB, out double delta)) { firstError ??= "contact shift not exact"; continue; }
                Vector3d t = Vector3d.Zero;
                int o0 = (ax + 1) % 3, o1 = (ax + 2) % 3;
                double s0 = RoundToGrid(a.GetBounds().Center[o0] - b.GetBounds().Center[o0], q);
                double s1 = RoundToGrid(a.GetBounds().Center[o1] - b.GetBounds().Center[o1], q);
                t[ax] = delta; t[o0] = s0; t[o1] = s1;
                DMesh3 moved = new DMesh3(b, false, true, false, false);
                if (!TranslateExact(moved, t)) { firstError ??= "translation not exact"; continue; }
                PlanarFace nearMoved = ExtremeFace(moved, ax, sg < 0);
                if (nearMoved.Plane != target) { firstError ??= "contact plane not exact"; continue; }
                // planar faces on both sides that overlap laterally: face contact (or flush)
                PlanarFace farMoved = ExtremeFace(moved, ax, sg > 0);
                PlanarFace bFace = embed && depth == 0 ? farMoved : nearMoved;
                bool lateral = Math.Min(fa.Hi0, bFace.Hi0) > Math.Max(fa.Lo0, bFace.Lo0) && Math.Min(fa.Hi1, bFace.Hi1) > Math.Max(fa.Lo1, bFace.Lo1);
                bool planar = fa.Area > 0 && bFace.Area > 0 && lateral;
                var info = new Dictionary<string, object> {
                    ["kind"] = planar ? "face" : "bbox", ["contact_axis"] = ax, ["contact_sign"] = sg,
                    ["plane"] = target, ["plane_a"] = pA, ["areaA"] = fa.Area, ["areaB"] = bFace.Area,
                    ["depth"] = embed ? depth : -1, ["shift"] = new[] { t.x, t.y, t.z },
                };
                if (best == null) { best = moved; bestInfo = info; }
                if (planar) { best = moved; bestInfo = info; break; }
            }
            if (best == null) {
                r.Error = firstError ?? "no exact placement";
                return r;
            }
            r.B = best;
            foreach (var kv in bestInfo)
                r.Info[kv.Key] = kv.Value;
            return r;
        }

        static ContactResult Ulps(ContactResult r, int axis, int sign, int ulps)
        {
            DMesh3 a = r.A;
            // try the given axis, then the others
            for (int k = 0; k < 3; ++k) {
                int ax = ((axis % 3 + 3) % 3 + k) % 3;
                double maxAbs = 0;
                foreach (int vid in a.VertexIndices())
                    maxAbs = Math.Max(maxAbs, Math.Abs(Get(a.GetVertex(vid), ax)));
                if (maxAbs == 0)
                    continue;
                double ulp = NextUp(maxAbs) - maxAbs;    // one ulp of the largest coordinate
                double step = (sign >= 0 ? 1 : -1) * ulps * ulp;
                DMesh3 b = new DMesh3(a, false, true, false, false);
                Vector3d t = Vector3d.Zero; t[ax] = step;
                if (!TranslateExact(b, t))
                    continue;
                r.B = b;
                r.Info["shift_axis"] = ax; r.Info["ulp"] = ulp; r.Info["ulps"] = ulps; r.Info["kind"] = "ulp";
                return r;
            }
            r.Error = "ulp shift not exact on any axis";
            return r;
        }

        static ContactResult Mirror(ContactResult r, int axis, int rank)
        {
            DMesh3 a = r.A;
            for (int k = 0; k < 3; ++k) {
                int ax = ((axis % 3 + 3) % 3 + k) % 3;
                double center = a.GetBounds().Center[ax];
                // the distinct vertex coordinates, nearest to the box centre first: the plane goes through one of them
                var coords = a.VertexIndices().Select(v => Get(a.GetVertex(v), ax)).Distinct()
                    .OrderBy(c => Math.Abs(c - center)).ThenBy(c => c).ToList();
                for (int j = 0; j < Math.Min(coords.Count, 32); ++j) {
                    double c = coords[(Math.Abs(rank) + j) % coords.Count];
                    DMesh3 b = Reflect(a, ax, c);
                    if (b == null)
                        continue;
                    r.B = b;
                    r.Info["mirror_axis"] = ax; r.Info["plane"] = c; r.Info["kind"] = "mirror";
                    return r;
                }
            }
            r.Error = "mirror not exact";
            return r;
        }

        /// <summary> The mirror image across the plane axis = c (x' = 2c - x), orientation kept outward; null if a coordinate
        /// is not exact. 2c is exact (power of two); the difference is checked. </summary>
        public static DMesh3 Reflect(DMesh3 m, int axis, double c)
        {
            DMesh3 b = new DMesh3(m, false, true, false, false);
            double c2 = 2 * c;
            foreach (int vid in b.VertexIndices()) {
                Vector3d v = b.GetVertex(vid);
                if (!ExactSum(c2, -Get(v, axis), out double x))
                    return null;
                if (axis == 0) v.x = x; else if (axis == 1) v.y = x; else v.z = x;
                b.SetVertex(vid, v);
            }
            b.ReverseOrientation(false);
            return b;
        }

        /// <summary> Signed volume of a closed mesh (divergence theorem, relative to its first vertex). </summary>
        public static double Volume(DMesh3 m)
        {
            Vector3d o = m.GetVertex(m.VertexIndices().First());
            double v = 0;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                v += (m.GetVertex(t.a) - o).Dot((m.GetVertex(t.b) - o).Cross(m.GetVertex(t.c) - o)) / 6.0;
            }
            return v;
        }
    }
}
