// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact geometry of the slope envelope (docs/design/TERRAIN_1A_PLAN.md): planes through three double points,
    /// lines in XY where two planes have the same height or through two double points, and vertices where two lines
    /// cross or at a double point. Every decision (side of a line at a vertex, orientation of three vertices, order
    /// along a line) is the sign of a polynomial in the input doubles: interval arithmetic first (ExactInterval), then
    /// exact (ExactDyadic). Constructed values (the rounded vertex position, a plane's height there) are rounded once,
    /// to nearest, from the exact rational value.
    /// </summary>
    internal sealed class ExactSlopeKernel
    {
        // ---- planes: Nx x + Ny y + Nz z = D, Nz > 0 ----
        sealed class Plane
        {
            public ExactInterval Nx, Ny, Nz, D;
            public ExactDyadic eNx, eNy, eNz, eD;
            public bool exact;
            public Vector3d P, Q, R;
        }

        // ---- lines: A x + B y + C = 0 ----
        sealed class Line
        {
            public ExactInterval A, B, C;
            public ExactDyadic eA, eB, eC;
            public bool exact;
            public int Pa = -1, Pb = -1;          // plane pair (sign = sign(P_a - P_b)), or
            public Vector2d U, W;                  // point pair (sign = orient(U, W, x): + left of U -> W)
        }

        // ---- vertices: homogeneous (X, Y, W), W != 0 ----
        sealed class Vertex
        {
            public ExactInterval X, Y, W;
            public ExactDyadic eX, eY, eW;
            public bool exact;
            public int L1 = -1, L2 = -1;            // crossing of two lines, or
            public double x, y;                     // a double point (L1 = -1)
            public bool rounded;
            public double rx, ry;                   // rounded position
        }

        readonly List<Plane> planes = new List<Plane>();
        readonly List<Line> lines = new List<Line>();
        readonly List<Vertex> vertices = new List<Vertex>();
        readonly Dictionary<long, int> planeLines = new Dictionary<long, int>();
        readonly Dictionary<long, int> crossings = new Dictionary<long, int>();

        public int PlaneCount { get { return planes.Count; } }
        public int LineCount { get { return lines.Count; } }
        public int VertexCount { get { return vertices.Count; } }
        /// <summary> predicate calls, those that needed the exact stage, and constructions (roundings) done exactly </summary>
        public long PredicateCalls, ExactPredicates, ExactConstructions;

        static ExactInterval I(double x) { return ExactInterval.Of(x); }
        static ExactDyadic E(double x) { return ExactDyadic.Of(x); }
        static long key(int a, int b) { return ((long)a << 32) | (uint)b; }

        /// <summary> the plane through three points, not vertical (their XY not collinear); returns its id </summary>
        public int AddPlane(Vector3d p, Vector3d q, Vector3d r)
        {
            var pl = new Plane() { P = p, Q = q, R = r };
            ExactInterval ux = I(q.x) - I(p.x), uy = I(q.y) - I(p.y), uz = I(q.z) - I(p.z);
            ExactInterval vx = I(r.x) - I(p.x), vy = I(r.y) - I(p.y), vz = I(r.z) - I(p.z);
            pl.Nx = uy * vz - uz * vy; pl.Ny = uz * vx - ux * vz; pl.Nz = ux * vy - uy * vx;
            int s = pl.Nz.Sign();
            if (s == 2) {
                exact_plane(pl);
                s = pl.eNz.Sign;
            }
            if (s == 0)
                throw new ArgumentException("ExactSlopeKernel: vertical plane");
            if (s < 0) {
                // orient so that Nz > 0
                pl.P = p; pl.Q = r; pl.R = q;
                pl.Nx = -pl.Nx; pl.Ny = -pl.Ny; pl.Nz = -pl.Nz;
                if (pl.exact) { pl.eNx = -pl.eNx; pl.eNy = -pl.eNy; pl.eNz = -pl.eNz; pl.exact = false; }
            }
            pl.D = pl.Nx * I(pl.P.x) + pl.Ny * I(pl.P.y) + pl.Nz * I(pl.P.z);
            pl.exact = false;
            planes.Add(pl);
            return planes.Count - 1;
        }

        /// <summary> the horizontal plane z = h </summary>
        public int AddHorizontalPlane(double h)
        {
            return AddPlane(new Vector3d(0, 0, h), new Vector3d(1, 0, h), new Vector3d(0, 1, h));
        }

        void exact_plane(Plane pl)
        {
            if (pl.exact) return;
            ExactDyadic ux = E(pl.Q.x) - E(pl.P.x), uy = E(pl.Q.y) - E(pl.P.y), uz = E(pl.Q.z) - E(pl.P.z);
            ExactDyadic vx = E(pl.R.x) - E(pl.P.x), vy = E(pl.R.y) - E(pl.P.y), vz = E(pl.R.z) - E(pl.P.z);
            pl.eNx = uy * vz - uz * vy; pl.eNy = uz * vx - ux * vz; pl.eNz = ux * vy - uy * vx;
            pl.eD = pl.eNx * E(pl.P.x) + pl.eNy * E(pl.P.y) + pl.eNz * E(pl.P.z);
            pl.exact = true;
        }

        /// <summary> the line where planes a and b have the same height; side &gt; 0 where a is higher (cached) </summary>
        public int PlaneLine(int a, int b)
        {
            int id;
            if (planeLines.TryGetValue(key(a, b), out id))
                return id;
            Plane pa = planes[a], pb = planes[b];
            var l = new Line() { Pa = a, Pb = b };
            l.A = pa.Nz * pb.Nx - pb.Nz * pa.Nx;
            l.B = pa.Nz * pb.Ny - pb.Nz * pa.Ny;
            l.C = pb.Nz * pa.D - pa.Nz * pb.D;
            lines.Add(l);
            id = lines.Count - 1;
            planeLines[key(a, b)] = id;
            return id;
        }

        /// <summary> the line through two distinct double points; side &gt; 0 left of u -> w </summary>
        public int PointLine(Vector2d u, Vector2d w)
        {
            var l = new Line() { U = u, W = w };
            ExactInterval dx = I(w.x) - I(u.x), dy = I(w.y) - I(u.y);
            l.A = -dy; l.B = dx; l.C = dy * I(u.x) - dx * I(u.y);
            lines.Add(l);
            return lines.Count - 1;
        }

        void exact_line(Line l)
        {
            if (l.exact) return;
            if (l.Pa >= 0) {
                Plane pa = planes[l.Pa], pb = planes[l.Pb];
                exact_plane(pa); exact_plane(pb);
                l.eA = pa.eNz * pb.eNx - pb.eNz * pa.eNx;
                l.eB = pa.eNz * pb.eNy - pb.eNz * pa.eNy;
                l.eC = pb.eNz * pa.eD - pa.eNz * pb.eD;
            } else {
                ExactDyadic dx = E(l.W.x) - E(l.U.x), dy = E(l.W.y) - E(l.U.y);
                l.eA = -dy; l.eB = dx; l.eC = dy * E(l.U.x) - dx * E(l.U.y);
            }
            l.exact = true;
        }

        /// <summary> the double point p as a vertex </summary>
        public int PointVertex(Vector2d p)
        {
            var v = new Vertex() { x = p.x, y = p.y, X = I(p.x), Y = I(p.y), W = I(1), rounded = true, rx = p.x, ry = p.y };
            vertices.Add(v);
            return vertices.Count - 1;
        }

        /// <summary>
        /// the crossing of lines l1 and l2 (cached by the pair); -1 if they are parallel (or the same line)
        /// </summary>
        public int Crossing(int l1, int l2)
        {
            if (l1 > l2) { int t = l1; l1 = l2; l2 = t; }
            int id;
            if (crossings.TryGetValue(key(l1, l2), out id))
                return id;
            Line a = lines[l1], b = lines[l2];
            var v = new Vertex() { L1 = l1, L2 = l2 };
            v.X = a.B * b.C - b.B * a.C;
            v.Y = a.C * b.A - b.C * a.A;
            v.W = a.A * b.B - b.A * a.B;
            if (v.W.Sign() == 0 || (v.W.Sign() == 2 && exact_vertex_w_zero(v))) {
                crossings[key(l1, l2)] = -1;
                return -1;
            }
            vertices.Add(v);
            id = vertices.Count - 1;
            crossings[key(l1, l2)] = id;
            return id;
        }

        bool exact_vertex_w_zero(Vertex v)
        {
            exact_vertex(v);
            return v.eW.Sign == 0;
        }

        void exact_vertex(Vertex v)
        {
            if (v.exact) return;
            if (v.L1 < 0) {
                v.eX = E(v.x); v.eY = E(v.y); v.eW = E(1);
            } else {
                Line a = lines[v.L1], b = lines[v.L2];
                exact_line(a); exact_line(b);
                v.eX = a.eB * b.eC - b.eB * a.eC;
                v.eY = a.eC * b.eA - b.eC * a.eA;
                v.eW = a.eA * b.eB - b.eA * a.eB;
            }
            v.exact = true;
        }

        /// <summary> sign of the line's function at the vertex: +1, -1 or 0 (on the line) </summary>
        [MethodImpl(JitHints.Optimize)]
        public int Side(int vertex, int line)
        {
            Vertex v = vertices[vertex];
            Line l = lines[line];
            if (v.L1 == line || v.L2 == line)
                return 0;
            // a double point that both planes of the line pass through (eg a toe apex of cone tangents): on the line
            if (v.L1 < 0 && l.Pa >= 0 && passes_through(planes[l.Pa], v.x, v.y) && passes_through(planes[l.Pb], v.x, v.y)
                && same_height(planes[l.Pa], planes[l.Pb], v.x, v.y))
                return 0;
            PredicateCalls++;
            int sw = v.W.Sign();
            int s = (l.A * v.X + l.B * v.Y + l.C * v.W).Sign();
            if (s != 2 && sw != 2)
                return s * sw;
            ExactPredicates++;
            exact_vertex(v); exact_line(l);
            return (l.eA * v.eX + l.eB * v.eY + l.eC * v.eW).Sign * v.eW.Sign;
        }

        // the plane's defining point at (x, y), if any (its height there is that point's z, exactly)
        static bool passes_through(Plane pl, double x, double y)
        {
            return (pl.P.x == x && pl.P.y == y) || (pl.Q.x == x && pl.Q.y == y) || (pl.R.x == x && pl.R.y == y);
        }

        static double defining_z(Plane pl, double x, double y)
        {
            if (pl.P.x == x && pl.P.y == y) return pl.P.z;
            if (pl.Q.x == x && pl.Q.y == y) return pl.Q.z;
            return pl.R.z;
        }

        static bool same_height(Plane a, Plane b, double x, double y)
        {
            return defining_z(a, x, y) == defining_z(b, x, y);
        }

        /// <summary> orientation of three vertices: +1 counter-clockwise, -1 clockwise, 0 collinear </summary>
        public int Orient(int a, int b, int c)
        {
            Vertex p = vertices[a], q = vertices[b], r = vertices[c];
            ExactInterval det = p.X * (q.Y * r.W - r.Y * q.W) - q.X * (p.Y * r.W - r.Y * p.W) + r.X * (p.Y * q.W - q.Y * p.W);
            ExactInterval ws = p.W * q.W * r.W;
            PredicateCalls++;
            int sd = det.Sign(), sw = ws.Sign();
            if (sd != 2 && sw != 2)
                return sd * sw;
            ExactPredicates++;
            exact_vertex(p); exact_vertex(q); exact_vertex(r);
            ExactDyadic e = p.eX * (q.eY * r.eW - r.eY * q.eW) - q.eX * (p.eY * r.eW - r.eY * p.eW) + r.eX * (p.eY * q.eW - q.eY * p.eW);
            return e.Sign * (p.eW * q.eW * r.eW).Sign;
        }

        /// <summary>
        /// the vertex as doubles, each coordinate within 3 units in the last place of the exact value: from the
        /// intervals when they are tight enough to prove that, else rounded to nearest from the exact value. A
        /// function of the input doubles only (deterministic, and exact under scaling by powers of two).
        /// </summary>
        public Vector2d Rounded(int vertex)
        {
            Vertex v = vertices[vertex];
            if (v.rounded == false) {
                double x, y;
                if (fast_quotient(v.X, v.W, out x) && fast_quotient(v.Y, v.W, out y)) {
                    v.rx = x; v.ry = y;
                } else {
                    ExactConstructions++;
                    exact_vertex(v);
                    v.rx = ExactDyadic.Quotient(v.eX, v.eW);
                    v.ry = ExactDyadic.Quotient(v.eY, v.eW);
                }
                v.rounded = true;
            }
            return new Vector2d(v.rx, v.ry);
        }

        // q = mid(num) / mid(den), if it is provably within 3 ulp(q) of every quotient of values in the intervals:
        // |n/d - q| <= (|n - n0| + |q| |d - d0|) / |d| + |n0/d0 - q|, with |n - n0| <= width(num), |d - d0| <= width(den),
        // |d| >= min |den|, and the division rounded to nearest (half an ulp; one ulp is counted)
        static bool fast_quotient(ExactInterval num, ExactInterval den, out double q)
        {
            q = 0;
            double dmin = den.Lo > 0 ? den.Lo : (den.Hi < 0 ? -den.Hi : 0);
            if (!(dmin > 1e-290) || double.IsInfinity(den.Lo) || double.IsInfinity(den.Hi) || double.IsInfinity(num.Lo) || double.IsInfinity(num.Hi))
                return false;
            double n0 = 0.5 * num.Lo + 0.5 * num.Hi, d0 = 0.5 * den.Lo + 0.5 * den.Hi;
            q = n0 / d0;
            if (q == 0 || MathUtil.IsFinite(q) == false || Math.Abs(q) < 1e-280)
                return false;
            double ulpq = ExactInterval.Up(Math.Abs(q)) - Math.Abs(q);
            double err = ((num.Hi - num.Lo) + Math.Abs(q) * (den.Hi - den.Lo)) / dmin;
            // (err is computed in doubles; the factor 1.01 covers its own rounding)
            return 1.01 * err + ulpq <= 3 * ulpq;
        }

        /// <summary> an approximation of the vertex position (for bounding boxes and heuristics only) </summary>
        public Vector2d Approx(int vertex)
        {
            Vertex v = vertices[vertex];
            if (v.rounded) return new Vector2d(v.rx, v.ry);
            double w = 0.5 * (v.W.Lo + v.W.Hi);
            double x = 0.5 * (v.X.Lo + v.X.Hi) / w, y = 0.5 * (v.Y.Lo + v.Y.Hi) / w;
            // tight intervals only (nearly parallel lines have a wide W): else the rounded exact position
            double err = ((v.X.Hi - v.X.Lo) + (v.Y.Hi - v.Y.Lo)) / Math.Abs(w) + (Math.Abs(x) + Math.Abs(y)) * (v.W.Hi - v.W.Lo) / Math.Abs(w);
            if (MathUtil.IsFinite(x) && MathUtil.IsFinite(y) && err <= 1e-12 * (Math.Abs(x) + Math.Abs(y)))
                return new Vector2d(x, y);
            return Rounded(vertex);
        }

        /// <summary> the plane's height at the double point (x, y), within 3 ulp (as Rounded) </summary>
        public double Height(int plane, double x, double y)
        {
            Plane pl = planes[plane];
            double z;
            if (fast_quotient(pl.D - pl.Nx * I(x) - pl.Ny * I(y), pl.Nz, out z))
                return z;
            ExactConstructions++;
            exact_plane(pl);
            ExactDyadic num = pl.eD - pl.eNx * E(x) - pl.eNy * E(y);
            return ExactDyadic.Quotient(num, pl.eNz);
        }

        /// <summary> an approximation of the plane's height at (x, y), for heuristics only </summary>
        public double ApproxHeight(int plane, double x, double y)
        {
            Plane pl = planes[plane];
            double nx = 0.5 * (pl.Nx.Lo + pl.Nx.Hi), ny = 0.5 * (pl.Ny.Lo + pl.Ny.Hi), nz = 0.5 * (pl.Nz.Lo + pl.Nz.Hi);
            return pl.P.z - (nx * (x - pl.P.x) + ny * (y - pl.P.y)) / nz;
        }
    }
}
