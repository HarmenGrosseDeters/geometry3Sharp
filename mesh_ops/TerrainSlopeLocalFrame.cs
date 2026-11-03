// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace gs
{
    // The opt-in local frame (docs/design/TERRAIN.md 5.8.1): the inputs translated in XY to a local origin, the design
    // computed there, and the outputs returned in that local frame with the origin
    public partial class TerrainSlopeDesign
    {
        /// <summary>
        /// Compute in a local frame (docs/design/TERRAIN.md 5.8.1). The toes and the terrain are translated in XY to an
        /// origin near the toes, and the design is computed there. The toes are copied; the terrain's positions are
        /// translated in place and restored when Compute returns (also if it throws), bit for bit, as every translation is
        /// checked exact. The terrain's timestamps do not change, so a spatial index the caller holds on it stays valid
        /// afterwards. While Compute runs, though, the caller's Terrain is in the local frame: it must not be read, or
        /// shared with another design or other work running concurrently (a concurrent change would also be translated
        /// back, and could leave the terrain corrupted). A copy instead cost 10-20 % of the run on a 2-million-triangle
        /// terrain (docs/design/TERRAIN.md 5.8.1). Every output
        /// (SlopeSurface, Pieces, CatchLines, SlopeRegion, Composite, Replacement, TargetHeightLines) is in that local
        /// frame: its XY plus UsedOrigin is the input's frame. The derived sizes
        /// (UsedMinFeatureSize, the margins, λ) follow from the local coordinates, not from the magnitude of the input's
        /// (MinFeatureSize is about 1.5 cm at UTM with the default SlopeMargin, and some micrometres in a local frame of
        /// a few hundred metres), so features that the input's frame cannot resolve can be designed.
        /// The outputs are not translated back: fl(local + UsedOrigin) rounds each coordinate by up to half an ulp of the
        /// input's magnitude, more than the local margins, so the exact guarantees (never steeper, support, no overlaps)
        /// hold for the local outputs only. Translating them back is the caller's choice, without that guarantee.
        /// With Replacement (CompositeKind.Replacement) the kept triangles are the caller's Terrain, in the input's
        /// frame: translate one or the other. If an input coordinate cannot be translated to the origin exactly, the
        /// design is computed in the input's frame (LocalFrameFallback, UsedOrigin (0, 0)). That is common near the
        /// origin, where the input's frame is local already; at UTM it takes a coordinate of a much smaller magnitude
        /// with fine bits (eg a stray terrain vertex near (0, 0)). While Compute runs, Toes and Terrain are in the local
        /// frame (a CancelF callback that reads them sees it). Default false: the input's frame.
        /// </summary>
        public bool LocalFrame = false;
        /// <summary>
        /// output: the origin of the local frame (the outputs' XY plus UsedOrigin is the input's frame); (0, 0) without
        /// LocalFrame or after a fallback
        /// </summary>
        public Vector2d UsedOrigin;
        /// <summary>
        /// output: LocalFrame was asked for, but an input coordinate cannot be translated to the local origin exactly
        /// (TwoSum), so the design was computed in the input's frame (UsedOrigin (0, 0))
        /// </summary>
        public bool LocalFrameFallback;

        public bool Compute()
        {
            UsedOrigin = Vector2d.Zero; LocalFrameFallback = false;
            if (LocalFrame == false)
                return compute_here();
            var sw = Stopwatch.StartNew();
            Vector2d o;
            int verdict = local_origin(out o);
            if (verdict != 1) {
                // (non-finite or empty inputs: the run in the input's frame reports them; an inexact translation is a
                // fallback, said in the result, also if the run throws)
                LocalFrameFallback = verdict == 0;
                return compute_here();
            }
            // (this instance computes on translated toes and the terrain translated in place; both are put back)
            List<SlopeToe> toes = Toes;
            DMesh3 terrain = Terrain;
            bool moved = false, ok;
            double pre = 0;
            try {
                Toes = translated(toes, o);
                // (allocation-free, so it does not stop half way)
                translate(terrain, -o);
                moved = true;
                // (set before the run: outputs it leaves, also on a throw, are local)
                UsedOrigin = o;
                pre = sw.Elapsed.TotalSeconds;
                ok = compute_here();
            } finally {
                sw.Restart();
                Toes = toes;
                // (exact: x - o was checked exact, so (x - o) + o is x)
                if (moved) translate(terrain, o);
            }
            PhaseSeconds["local-frame"] = pre + sw.Elapsed.TotalSeconds;
            return ok;
        }

        // the local origin: the centre of the toes' XY box (it scales bitwise with 2^k, short of overflow). A coordinate
        // within a factor 2 of it has an exact difference to it (Sterbenz), as at UTM; others are checked. 1: every input
        // XY minus it is exact; 0: one is not (a fallback); -1: no toe point, or a non-finite coordinate (the run in the
        // input's frame reports it). A terrain coordinate -0.0 is a fallback too: -0 - o + o is +0, not -0
        int local_origin(out Vector2d o)
        {
            o = Vector2d.Zero;
            var box = AxisAlignedBox2d.Empty;
            bool any = false;
            if (Toes == null) return -1;
            foreach (SlopeToe t in Toes) {
                if (t == null || t.Points == null) return -1;
                foreach (Vector3d p in t.Points) {
                    if (MathUtil.IsFinite(p.x) == false || MathUtil.IsFinite(p.y) == false) return -1;
                    box.Contain(p.xy); any = true;
                }
            }
            if (any == false) return -1;
            if (Terrain != null)
                foreach (int v in Terrain.VertexIndices()) {
                    Vector3d p = Terrain.GetVertex(v);
                    if (MathUtil.IsFinite(p.x) == false || MathUtil.IsFinite(p.y) == false) return -1;
                }
            o = box.Center;
            foreach (SlopeToe t in Toes)
                foreach (Vector3d p in t.Points)
                    if (exact_difference(p.x, o.x) == false || exact_difference(p.y, o.y) == false) return 0;
            if (Terrain != null)
                foreach (int v in Terrain.VertexIndices()) {
                    Vector3d p = Terrain.GetVertex(v);
                    if (exact_difference(p.x, o.x) == false || exact_difference(p.y, o.y) == false) return 0;
                    if (negative_zero(p.x) || negative_zero(p.y)) return 0;
                }
            return 1;
        }

        // x - o is exact: its rounding error (TwoSum, Knuth: s + e = a + b exactly, with s = fl(a + b)) is 0
        static bool exact_difference(double x, double o)
        {
            double a = x, b = -o, s = a + b;
            double bb = s - a;
            return (a - (s - bb)) + (b - bb) == 0;
        }

        static bool negative_zero(double x)
        {
            return x == 0 && BitConverter.DoubleToInt64Bits(x) < 0;
        }

        // copies of the toes translated by -o (the other fields shared: they are not changed)
        static List<SlopeToe> translated(List<SlopeToe> toes, Vector2d o)
        {
            return toes.Select(t => new SlopeToe() {
                Points = t.Points.Select(p => new Vector3d(p.x - o.x, p.y - o.y, p.z)).ToList(), Closed = t.Closed, Side = t.Side,
                CutStart = t.CutStart, CutEnd = t.CutEnd, FillStart = t.FillStart, FillEnd = t.FillEnd
            }).ToList();
        }

        // the terrain's positions translated by d in XY, in place: the same vertex and triangle ids (so
        // ReplacedTerrainTriangles and the vertex maps refer to the caller's terrain), and in its vertex buffer, so its
        // timestamps stay (restored bit for bit, the terrain is the caller's as it was)
        static void translate(DMesh3 terrain, Vector2d d)
        {
            if (terrain == null) return;
            DVector<double> buf = terrain.VerticesBuffer;
            int n = terrain.MaxVertexID;
            for (int v = 0; v < n; ++v) {
                if (terrain.IsVertex(v) == false) continue;
                buf[3 * v] = buf[3 * v] + d.x;
                buf[3 * v + 1] = buf[3 * v + 1] + d.y;
            }
        }
    }
}
