using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meshing2HardProblems
{
    /// <summary>
    /// A throwaway interval bisection for the angle floor of HP 6 (research note, section 1.6). The configuration: an apex at the
    /// origin, a = (1, 0) and b = (k cos t, k sin t) on two segments that meet at the angle t, the next vertex of the first
    /// segment beyond a at (2, 0), and a third vertex c on the far side of a-b. The claim: if (2, 0) is not strictly inside the
    /// circumcircle of a, b, c (the triangle is Delaunay), the angle at c is at least arctan(sin t / (2 - cos t)) for k = 1.
    /// Intervals are plain doubles widened by a relative 1e-12 per operation: a check of the statement, not a formal proof.
    /// </summary>
    public static class FloorLemma
    {
        readonly struct I
        {
            public readonly double Lo, Hi;
            public I(double lo, double hi) { Lo = lo; Hi = hi; }
            static I Pad(double lo, double hi) { double e = 1e-12 * Math.Max(Math.Abs(lo), Math.Abs(hi)) + 1e-300; return new I(lo - e, hi + e); }
            public static implicit operator I(double v) => new I(v, v);
            public static I operator +(I a, I b) => Pad(a.Lo + b.Lo, a.Hi + b.Hi);
            public static I operator -(I a, I b) => Pad(a.Lo - b.Hi, a.Hi - b.Lo);
            public static I operator *(I a, I b)
            {
                double p = a.Lo * b.Lo, q = a.Lo * b.Hi, r = a.Hi * b.Lo, s = a.Hi * b.Hi;
                return Pad(Math.Min(Math.Min(p, q), Math.Min(r, s)), Math.Max(Math.Max(p, q), Math.Max(r, s)));
            }
            /// <summary> cosine and sine on [lo, hi] within [0, pi/2]: monotone there </summary>
            public static I Cos(I t) => Pad(Math.Cos(t.Hi), Math.Cos(t.Lo));
            public static I Sin(I t) => Pad(Math.Sin(t.Lo), Math.Sin(t.Hi));
        }

        /// <summary> 1: the claim holds on the whole box, or the box has no configuration; 0: undecided </summary>
        static int Decide(I t, I k, I cx, I cy, double slack, double holes)
        {
            I cos = I.Cos(t), sin = I.Sin(t), bx = k * cos, by = k * sin;
            // c within `holes` of a or of b is no configuration (the claim is about a triangle)
            I da = (cx - 1) * (cx - 1) + cy * cy, db = (cx - bx) * (cx - bx) + (cy - by) * (cy - by);
            if (da.Hi < holes * holes || db.Hi < holes * holes) return 1;
            // the far side of a-b: cross(b - a, c - a) < 0 (the apex has it positive)
            I side = (bx - 1) * cy - by * (cx - 1);
            if (side.Lo >= 0) return 1;
            // (2, 0) strictly inside the circumcircle of b, a, c (counter-clockwise on the far side): no Delaunay triangle
            I ax = -1.0, ay = 0.0, ux = bx - 2, uy = by, vx = cx - 2, vy = cy;   // b, a, c relative to (2, 0); a - (2,0) = (-1, 0)
            I al = ax * ax + ay * ay, ul = ux * ux + uy * uy, vl = vx * vx + vy * vy;
            I inCircle = ul * (ax * vy - ay * vx) + al * (vx * uy - vy * ux) + vl * (ux * ay - uy * ax);
            if (inCircle.Lo > 0) return 1;
            // the claim: cot(angle at c) <= (1 + slack) * cot(floor) = (1 + slack) * (2 - cos t) / sin t, as dot * sin t <= (1 + slack) * (2 - cos t) * |cross|
            I px = 1 - cx, py = -1.0 * cy, qx = bx - cx, qy = by - cy;
            // (the cross product of the two edges at c is minus `side`; a configuration has it positive, so the box is clipped there)
            I dot = px * qx + py * qy, cross = new I(Math.Max(0, -side.Hi), -side.Lo);
            I lhs = dot * sin, rhs = (2 - cos) * cross * (1 + slack);
            return lhs.Hi <= rhs.Lo ? 1 : 0;
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            double slack = double.Parse(arg(args, "--slack") ?? "0.05", CultureInfo.InvariantCulture), kMax = double.Parse(arg(args, "--k") ?? "1", CultureInfo.InvariantCulture);
            double tMin = double.Parse(arg(args, "--phi-min") ?? "0.5", CultureInfo.InvariantCulture) * Math.PI / 180, holes = 0.01;
            double tMax = double.Parse(arg(args, "--phi-max") ?? "60", CultureInfo.InvariantCulture) * Math.PI / 180;
            int maxDepth = int.Parse(arg(args, "--depth") ?? "60", CultureInfo.InvariantCulture);
            var stack = new Stack<(I t, I k, I cx, I cy, int depth)>();
            stack.Push((new I(tMin, tMax), new I(1, kMax), new I(-4, 6), new I(-5, 5), 0));
            long proved = 0, undecided = 0, boxes = 0; int deepest = 0; (I t, I k, I cx, I cy) first = default;
            while (stack.Count > 0) {
                var (t, k, cx, cy, depth) = stack.Pop();
                ++boxes; deepest = Math.Max(deepest, depth);
                if (Decide(t, k, cx, cy, slack, holes) == 1) { ++proved; continue; }
                if (depth >= maxDepth || boxes > 200_000_000) { if (undecided++ == 0) first = (t, k, cx, cy); continue; }
                // bisect the widest side, relative to its start width
                double wt = (t.Hi - t.Lo) / (Math.PI / 3), wk = kMax > 1 ? (k.Hi - k.Lo) / (kMax - 1) : 0, wx = (cx.Hi - cx.Lo) / 10, wy = (cy.Hi - cy.Lo) / 10, w = Math.Max(Math.Max(wt, wk), Math.Max(wx, wy));
                I L(I v) => new I(v.Lo, (v.Lo + v.Hi) / 2); I H(I v) => new I((v.Lo + v.Hi) / 2, v.Hi);
                if (w == wt) { stack.Push((L(t), k, cx, cy, depth + 1)); stack.Push((H(t), k, cx, cy, depth + 1)); }
                else if (w == wk) { stack.Push((t, L(k), cx, cy, depth + 1)); stack.Push((t, H(k), cx, cy, depth + 1)); }
                else if (w == wx) { stack.Push((t, k, L(cx), cy, depth + 1)); stack.Push((t, k, H(cx), cy, depth + 1)); }
                else { stack.Push((t, k, cx, L(cy), depth + 1)); stack.Push((t, k, cx, H(cy), depth + 1)); }
            }
            string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
            Console.WriteLine("floor lemma: phi in [" + F(tMin * 180 / Math.PI) + ", " + F(tMax * 180 / Math.PI) + "] degrees, k in [1, " + F(kMax) + "], c in [-4, 6] x [-5, 5] without discs of " + F(holes) + " around a and b, slack " + F(slack) + " on the cotangent");
            Console.WriteLine("boxes " + boxes + ", proved " + proved + ", undecided at depth " + maxDepth + ": " + undecided + ", deepest " + deepest);
            if (undecided > 0) Console.WriteLine("first undecided box: phi [" + F(first.t.Lo * 180 / Math.PI) + ", " + F(first.t.Hi * 180 / Math.PI) + "] degrees, k [" + F(first.k.Lo) + ", " + F(first.k.Hi) + "], c [" + F(first.cx.Lo) + ", " + F(first.cx.Hi) + "] x [" + F(first.cy.Lo) + ", " + F(first.cy.Hi) + "]");
            Console.WriteLine(undecided == 0 ? "PROVED on the box (to the slack)" : "NOT PROVED: undecided boxes remain");
            return undecided == 0 ? 0 : 1;
        }
    }
}
