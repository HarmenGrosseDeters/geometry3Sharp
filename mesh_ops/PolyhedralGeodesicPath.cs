// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> The outcome of <see cref="PolyhedralGeodesicDistance.TracePath(int)"/>. </summary>
    public enum PolyhedralGeodesicPathStatus
    {
        /// <summary> Points is the shortest path from Source to the target; Length is its length </summary>
        Ok,
        /// <summary> the last Compute did not end with Ok or PartiallyReached (it was not called, or ended with WindowLimit, Cancelled, NoSource, ...): no path, empty Points </summary>
        NotComputed,
        /// <summary> a vertex id that is not a vertex, a triangle id that is not a triangle, or barycentric coordinates that are negative, not finite or do not sum to 1: no path </summary>
        InvalidTarget,
        /// <summary> the target has no distance (cut off by MaxDistance, in another component than every source, or one of the UnreachedVertices): no path </summary>
        Unreached,
        /// <summary> the unfolding of the path did not end at a source (the walk left the mesh at a boundary, or ran out of its step budget). <b>Points is not a path</b>:
        /// it holds the points found from the target up to where the walk stopped (in target-to-source order and without a first point at a source); only Distance
        /// and Source are meaningful. Not seen on any tested family: it is a guard, not an expected outcome </summary>
        TraceFailed
    }

    /// <summary> A point of a surface path: where it is on the mesh. </summary>
    public struct SurfacePathPoint
    {
        /// <summary> the 3D position (for a point on an edge: the edge's first vertex plus T times the edge vector) </summary>
        public Vector3d Position;
        /// <summary> the mesh vertex the point is at, or -1 </summary>
        public int Vertex;
        /// <summary> the edge the point is in the interior of, or -1 (for a vertex, and for the target point of a face-point query) </summary>
        public int Edge;
        /// <summary> for a point on an edge: the fraction of the way from the edge's first vertex (DMesh3.GetEdgeV(Edge).a) to its second, in (0, 1); else 0 </summary>
        public double T;
        /// <summary> -1, except for the target of a face-point query: the triangle that contains it </summary>
        public int Face;
    }

    /// <summary>
    /// The shortest path on the surface from the nearest source to a target (<see cref="PolyhedralGeodesicDistance.TracePath(int)"/>).
    /// Points go from the source to the target. Between two consecutive points the path is a straight segment inside one triangle:
    /// Faces[i] is that triangle for the segment from Points[i] to Points[i + 1] (for a segment along an edge, one of the two triangles of the edge).
    /// A point of the path is a vertex, a point in the interior of an edge, or (the last point of a face-point query) a point in a triangle; the path bends only at
    /// vertices with an angle of at least 2 pi (saddles, boundary vertices).
    /// </summary>
    public sealed class PolyhedralGeodesicPath
    {
        public PolyhedralGeodesicPathStatus Status { get; internal set; }
        /// <summary> the source the path starts at (the lowest id among sources at an exact tie), -1 unless the path was found or TraceFailed </summary>
        public int Source { get; internal set; } = -1;
        /// <summary> the distance of the target from the sources: Distance[v] of the run for a vertex, for a point in a triangle the smallest length of a straight segment
        /// from the point to a window of the triangle's edges or to a corner plus the corner's distance; NaN if there is no distance </summary>
        public double Distance { get; internal set; } = double.NaN;
        /// <summary> the sum of the lengths of the segments between consecutive Points (in 3D); equals Distance up to the tolerance of the class (see the docs of the class and GEODESICS.md A.8); 0 unless Status is Ok </summary>
        public double Length { get; internal set; }
        public List<SurfacePathPoint> Points { get; internal set; } = new List<SurfacePathPoint>();
        public List<int> Faces { get; internal set; } = new List<int>();
    }

    public sealed partial class PolyhedralGeodesicDistance
    {
        /// <summary> test hook: a cap on the steps of one trace (a step is one triangle crossed or one edge walked); 0 means 2 x triangles + 16 </summary>
        internal int TraceStepCap;

        /// <summary>
        /// The shortest path from the nearest source to the vertex <paramref name="targetVertex"/> (the vertex of the last <see cref="Compute"/>): backtracing through the
        /// windows (Mitchell, Mount and Papadimitriou 1987; Surazhsky et al. 2005): the run records at every vertex how its distance arose (the window and the unfolded
        /// image of its origin, or the edge from a pseudo-source), and the path is the straight line to that image, unfolded back across the triangles it crosses until it reaches
        /// a vertex (a pseudo-source or a source), where it continues with that vertex's record. Valid until the next Compute; the mesh must not change in between. Only reads the instance: calls may run concurrently with each other, not with Compute. Assumes no edge much shorter than 1e-7 of the extent (GEODESICS.md A.8).
        /// Costs memory: 32 bytes per vertex are kept during Compute for this, and the run's windows stay in memory after it.
        /// </summary>
        public PolyhedralGeodesicPath TracePath(int targetVertex)
        {
            var res = new PolyhedralGeodesicPath();
            if (!CanTrace()) { res.Status = PolyhedralGeodesicPathStatus.NotComputed; return res; }
            if (targetVertex < 0 || targetVertex >= Mesh.MaxVertexID || !Mesh.IsVertex(targetVertex)) { res.Status = PolyhedralGeodesicPathStatus.InvalidTarget; return res; }
            if (!(Distance[targetVertex] < double.PositiveInfinity)) { res.Status = PolyhedralGeodesicPathStatus.Unreached; return res; }
            res.Distance = Distance[targetVertex]; res.Source = NearestSource[targetVertex];
            var pts = new List<SurfacePathPoint> { VertexPoint(targetVertex) }; var fcs = new List<int>();
            int steps = 0;
            bool ok = Walk(targetVertex, pts, fcs, ref steps);
            return Finish(res, pts, fcs, ok);
        }

        /// <summary>
        /// The shortest path from the nearest source to the point with the barycentric coordinates <paramref name="barycentric"/> in the triangle <paramref name="triangle"/>
        /// (in the order of <c>DMesh3.GetTriangle</c>). The distance of the point is the smallest of: a corner's distance plus the straight segment to the point, and
        /// sigma + the distance to the unfolded origin image over every window of the triangle's three edges that the segment to the image crosses.
        /// A point that is a corner (one coordinate 1, the others 0) gives exactly the result of the vertex query. See <see cref="TracePath(int)"/>.
        /// </summary>
        public PolyhedralGeodesicPath TracePath(int triangle, Vector3d barycentric)
        {
            var res = new PolyhedralGeodesicPath();
            if (!CanTrace()) { res.Status = PolyhedralGeodesicPathStatus.NotComputed; return res; }
            double b0 = barycentric.x, b1 = barycentric.y, b2 = barycentric.z;
            if (triangle < 0 || triangle >= Mesh.MaxTriangleID || !Mesh.IsTriangle(triangle) || !(b0 >= -1e-12 && b1 >= -1e-12 && b2 >= -1e-12) || !(Math.Abs(b0 + b1 + b2 - 1) <= 1e-9) || double.IsNaN(b0 + b1 + b2))
            { res.Status = PolyhedralGeodesicPathStatus.InvalidTarget; return res; }
            Index3i tri = Mesh.GetTriangle(triangle);
            if (b0 == 1 && b1 == 0 && b2 == 0) return TracePath(tri.a);
            if (b1 == 1 && b0 == 0 && b2 == 0) return TracePath(tri.b);
            if (b2 == 1 && b0 == 0 && b1 == 0) return TracePath(tri.c);
            Vector3d pa = Mesh.GetVertex(tri.a), pb = Mesh.GetVertex(tri.b), pc = Mesh.GetVertex(tri.c);
            double wsum = Math.Max(b0, 0) + Math.Max(b1, 0) + Math.Max(b2, 0);   // normalised: a sum within 1e-9 of 1 is accepted
            Vector3d p = (Math.Max(b0, 0) * pa + Math.Max(b1, 0) * pb + Math.Max(b2, 0) * pc) / wsum;
            // candidates: the corners (straight segment inside the triangle) and the windows on the three edge sides that enter the triangle
            double best = double.PositiveInfinity; int bestCorner = -1, bestE = -1, bestSide = -1, bestOrigin = -1; double bestSx = 0, bestSy = 0, bestPx = 0, bestPy = 0;
            for (int k = 0; k < 3; ++k) {
                int v = tri[k];
                double dv = Distance[v];                        // NaN beyond MaxDistance (dist[] holds tentative values there)
                if (!(dv < double.PositiveInfinity)) continue;
                double c = dv + p.Distance(Mesh.GetVertex(v));
                if (c < best) { best = c; bestCorner = v; bestE = -1; }
            }
            Index3i te = Mesh.GetTriEdges(triangle);
            double tol = 64 * cEps;
            for (int k = 0; k < 3; ++k) {
                int e = te[k]; int side = SideOf(e, triangle);
                Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
                PlacePoint(ev.a, ev.b, p, L, out double px, out double py);
                for (int c = head[side]; c >= 0; c = Next(c)) {
                    ref Window w = ref W(c);
                    if (!(w.B1 > w.B0)) continue;
                    double sy = Math.Min(w.Sy, 0);
                    double x = py > 0 ? px + (w.Sx - px) * (py / (py - sy)) : px;
                    if (x < w.B0 - tol || x > w.B1 + tol) continue;
                    double cand = w.Sigma + Hyp(px - w.Sx, py - w.Sy);
                    if (cand < best) { best = cand; bestCorner = -1; bestE = e; bestSide = side; bestOrigin = w.Origin; bestSx = w.Sx; bestSy = w.Sy; bestPx = px; bestPy = py; }
                }
            }
            if (!(best <= MaxDistance)) { res.Status = PolyhedralGeodesicPathStatus.Unreached; return res; }
            res.Distance = best;
            var target = new SurfacePathPoint { Position = p, Vertex = -1, Edge = -1, T = 0, Face = triangle };
            var pts = new List<SurfacePathPoint> { target }; var fcs = new List<int>();
            int steps = 0; bool ok; int via;
            if (bestE < 0) {
                res.Source = root[bestCorner];
                pts.Add(VertexPoint(bestCorner)); fcs.Add(triangle);
                ok = Walk(bestCorner, pts, fcs, ref steps);
            } else {
                res.Source = root[bestOrigin];
                via = TraceLeg(bestE, bestSide, bestSx, bestSy, bestPx, bestPy, triangle, pts, fcs, ref steps);
                ok = via >= 0 && Walk(via, pts, fcs, ref steps);
            }
            return Finish(res, pts, fcs, ok);
        }

        bool CanTrace() => (Status == PolyhedralGeodesicStatus.Ok || Status == PolyhedralGeodesicStatus.PartiallyReached) && vrec != null && Distance != null;

        PolyhedralGeodesicPath Finish(PolyhedralGeodesicPath res, List<SurfacePathPoint> pts, List<int> fcs, bool ok)
        {
            if (!ok) { res.Status = PolyhedralGeodesicPathStatus.TraceFailed; res.Points = pts; res.Faces = fcs; return res; }
            pts.Reverse(); fcs.Reverse();
            res.Source = pts[0].Vertex;                      // the vertex the walk ended at (a source); equals the root of the run except at near ties
            double len = 0;
            for (int i = 0; i + 1 < pts.Count; ++i) len += pts[i].Position.Distance(pts[i + 1].Position);
            res.Status = PolyhedralGeodesicPathStatus.Ok; res.Points = pts; res.Faces = fcs; res.Length = len;
            return res;
        }

        SurfacePathPoint VertexPoint(int v) => new SurfacePathPoint { Position = Mesh.GetVertex(v), Vertex = v, Edge = -1, T = 0, Face = -1 };

        SurfacePathPoint EdgePoint(int e, double x)
        {
            Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
            if (x <= 0) return VertexPoint(ev.a);
            if (x >= L) return VertexPoint(ev.b);
            double t = x / L; Vector3d a = Mesh.GetVertex(ev.a), b = Mesh.GetVertex(ev.b);
            return new SurfacePathPoint { Position = a + (b - a) * t, Vertex = -1, Edge = e, T = t, Face = -1 };
        }

        // the position of the point p in the frame of the edge (a at the origin, b on +x), on the side y >= 0
        void PlacePoint(int a, int b, Vector3d p, double L, out double x, out double y)
        {
            double ux = vx[b] - vx[a], uy = vy[b] - vy[a], uz = vz[b] - vz[a], wx = p.x - vx[a], wy = p.y - vy[a], wz = p.z - vz[a];
            double dot = ux * wx + uy * wy + uz * wz;
            double cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
            x = dot / L; y = Math.Sqrt(cx * cx + cy * cy + cz * cz) / L;
        }

        // from the vertex v back to a source, following the records of the run; the points are appended (target to source order)
        bool Walk(int v, List<SurfacePathPoint> pts, List<int> fcs, ref int steps)
        {
            int cap = TraceStepCap > 0 ? TraceStepCap : 2 * Mesh.TriangleCount + 16;
            while (true) {
                if (++steps > cap) return false;
                switch (vrec[v].Kind) {
                    case 1: return true;
                    case 2: {
                        int u = vrec[v].Org; int e = Mesh.FindEdge(v, u);
                        if (e < 0) return false;
                        Index2i et = Mesh.GetEdgeT(e); fcs.Add(et.a >= 0 ? et.a : et.b); pts.Add(VertexPoint(u)); v = u; break;
                    }
                    case 3: {
                        int e = vrec[v].E, side = vrec[v].Side; Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
                        double px, py; int cf = -1;
                        if (v == ev.a) { px = 0; py = 0; } else if (v == ev.b) { px = L; py = 0; }
                        else { Place(ev.a, ev.b, v, L, out px, out py); cf = side >= 0 ? FaceOfSide(side) : -1; }
                        int u = TraceLeg(e, side, vrec[v].X, vrec[v].Y, px, py, cf, pts, fcs, ref steps);
                        if (u < 0) return false;
                        v = u; break;
                    }
                    default: return false;
                }
            }
        }

        // One straight leg: from the point Q = (qx, qy) in the frame of the edge e (qy >= 0, in the triangle cf when qy > 0, else on the edge) to the unfolded image S = (sx, sy)
        // (sy <= 0) of a vertex, back across the triangles. The vertex the leg ends at (a corner of the triangle whose unfolded position is S) is returned and its point
        // appended, -1 on failure. side: the edge side the window entered (-1: a terminal window on a boundary edge).
        int TraceLeg(int e, int side, double sx, double sy, double qx, double qy, int cf, List<SurfacePathPoint> pts, List<int> fcs, ref int steps)
        {
            int cap = TraceStepCap > 0 ? TraceStepCap : 2 * Mesh.TriangleCount + 16;
            double posTolGlobal = 1e-9 * extent, posTol = posTolGlobal;
            int pf;
            { Index2i et = Mesh.GetEdgeT(e); if (side >= 0) pf = Other(e, FaceOfSide(side)); else pf = et.a >= 0 ? et.a : et.b; }
            sy = Math.Min(sy, 0);
            while (true) {
                if (pf < 0) return -1;
                if (++steps > cap) return -1;
                Index2i ev = Mesh.GetEdgeV(e); double L = elen[e];
                if (qy > 0) {
                    double t = qy / (qy - sy);
                    double xx = Math.Min(Math.Max(qx + (sx - qx) * t, 0), L);
                    // the line crosses the edge at one of its end vertices: the leg ends there (a subpath of a shortest path is shortest: the vertex continues with its own record)
                    double tolX = Math.Min(posTolGlobal, 1e-6 * L);
                    int endV = xx <= tolX ? ev.a : (xx >= L - tolX ? ev.b : -1);
                    if (endV >= 0) { fcs.Add(cf); pts.Add(VertexPoint(endV)); return endV; }
                    fcs.Add(cf); pts.Add(EdgePoint(e, xx)); qx = xx; qy = 0;
                }
                int cp = Third(pf, ev.a, ev.b);
                Place(ev.a, ev.b, cp, L, out double cx, out double cy); double Cy = -cy;
                // the tolerance for 'at a vertex' is 1e-9 extent but at most a millionth of the smallest edge of the triangle: a feature near the tolerance would be snapped away
                posTol = Math.Min(posTolGlobal, 1e-6 * Math.Min(L, Math.Min(Hyp(cx, cy), Hyp(cx - L, cy))));
                // S at a corner: the leg ends there
                int corner = -1;
                if (Hyp(sx, sy) <= posTol) corner = ev.a; else if (Hyp(sx - L, sy) <= posTol) corner = ev.b; else if (Hyp(sx - cx, sy - Cy) <= posTol) corner = cp;
                if (corner >= 0) {
                    if (pts[pts.Count - 1].Vertex != corner) { fcs.Add(pf); pts.Add(VertexPoint(corner)); }
                    return corner;
                }
                // the segment from Q to S passes through the third corner (the path runs along an edge of the triangle, or through a flat vertex): the leg ends at it
                {
                    double ddx = sx - qx, ddy = sy, dd = Hyp(ddx, ddy);
                    double tproj = ((cx - qx) * ddx + (Cy) * ddy) / dd;
                    double off = Math.Abs((cx - qx) * ddy - Cy * ddx) / dd;
                    if (off <= posTol && tproj > 0 && tproj < dd) { fcs.Add(pf); pts.Add(VertexPoint(cp)); return cp; }
                }
                // the ray from Q to S leaves the triangle through the edge (a, cp) or (b, cp): cross(C - Q, S - Q) < 0 means on the side of a
                double cr = (cx - qx) * sy + cy * (sx - qx);
                bool viaA = cr < 0;
                int third = viaA ? ev.b : ev.a;     // the corner not on the exit edge
                int u1 = viaA ? ev.a : ev.b;
                int e2 = Mesh.FindEdge(u1, cp);
                if (e2 < 0) return -1;
                Index2i ev2 = Mesh.GetEdgeV(e2); double L2 = elen[e2];
                // the positions of the corners in the current frame
                double ax = 0, ay = 0, bx = L, by = 0, ccx = cx, ccy = Cy;
                double p2ax, p2ay, p2bx, p2by, ptx, pty;
                { // ev2.a, ev2.b, third
                    Pos(ev2.a, ev.a, ev.b, cp, ax, ay, bx, by, ccx, ccy, out p2ax, out p2ay);
                    Pos(ev2.b, ev.a, ev.b, cp, ax, ay, bx, by, ccx, ccy, out p2bx, out p2by);
                    Pos(third, ev.a, ev.b, cp, ax, ay, bx, by, ccx, ccy, out ptx, out pty);
                }
                double dx = p2bx - p2ax, dy = p2by - p2ay, dl = Hyp(dx, dy); double ux = dx / dl, uy = dy / dl;
                double sg = (ux * (pty - p2ay) - uy * (ptx - p2ax)) >= 0 ? 1 : -1;
                Map(ux, uy, sg, p2ax, p2ay, qx, 0, out double nqx, out double nqy);
                Map(ux, uy, sg, p2ax, p2ay, sx, sy, out double nsx, out double nsy);
                // rounding: the exit edge is in the triangle's frame at y = 0, S beyond it
                // Q is the last point; when that is the vertex shared with the exit edge, Q stays on the exit edge and the ray turns around the vertex (no snapping by a distance:
                // in a sliver triangle a point far along the edge is close to the line of the next edge)
                if (pts[pts.Count - 1].Vertex == u1) nqy = 0; else if (nqy < 0) nqy = 0;
                if (nsy > 0) nsy = 0;
                cf = pf; pf = Other(e2, pf); e = e2; qx = nqx; qy = nqy; sx = nsx; sy = nsy;
            }
        }

        static void Pos(int v, int a, int b, int c, double ax, double ay, double bx, double by, double cx, double cy, out double x, out double y)
        {
            if (v == a) { x = ax; y = ay; } else if (v == b) { x = bx; y = by; } else { x = cx; y = cy; }
        }

        // the point (x, y) in the frame with the origin (ox, oy), the x axis (ux, uy) and the y axis rotated by +-90 degrees (sg)
        static void Map(double ux, double uy, double sg, double ox, double oy, double x, double y, out double nx, out double ny)
        {
            double rx = x - ox, ry = y - oy;
            nx = rx * ux + ry * uy;
            ny = sg * (ux * ry - uy * rx);
        }
    }
}
