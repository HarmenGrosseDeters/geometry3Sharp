// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace FilletDetection
{
    /// <summary>
    /// A procedural CAD-like solid with a known answer: the triangle group of every triangle is its class
    /// (planar, edge fillet, vertex fillet, a cylinder that is no fillet) plus the index of its radius, and Sharp is the
    /// same solid before the fillets (the design intent). Subdividing the mesh keeps the groups.
    /// </summary>
    public class Solid
    {
        public const int Flat = 0, Edge = 1, Corner = 2, Cylinder = 3;

        public string Name;
        public DMesh3 Mesh;
        public DMesh3 Sharp;
        /// <summary> radii by index (fillets and cylinders) </summary>
        public double[] Radii;
        /// <summary> the largest fillet radius </summary>
        public double MaxFilletRadius;

        public int Class(int tid) { return Mesh.GetTriangleGroup(tid) & 3; }
        public double Radius(int tid) { return Radii[Mesh.GetTriangleGroup(tid) >> 2]; }
        public bool IsFillet(int tid) { int c = Class(tid); return c == Edge || c == Corner; }
        static int Group(int cls, int radiusIndex) { return cls + 4 * radiusIndex; }

        public static Solid Make(string name, int segments)
        {
            switch (name) {
                case "box": return RoundedBox("box", new Vector3d(1, 0.7, 0.5), 0.15, segments);
                case "plate": return RoundedBox("plate", new Vector3d(1, 0.7, 0.1), 0.04, segments);
                case "lprofile": return LProfile(segments);
                case "gear": return Gear(segments);
                case "rod": return Rod(segments);
                default: throw new ArgumentException("unknown solid " + name);
            }
        }

        /// <summary>
        /// Box of half extents h with all edges rounded by r (the Minkowski sum of the box h - r with a ball): six planar
        /// faces, twelve quarter cylinders (edge fillets) and eight sphere octants (vertex fillets). segments (even) facets per
        /// quarter circle; one quad per facet along an edge and one quad per face, as a CAD tessellation gives.
        /// </summary>
        public static Solid RoundedBox(string name, Vector3d h, double r, int segments)
        {
            if (segments < 2 || (segments & 1) != 0) throw new ArgumentException("the rounded box needs an even segment count");
            int m = segments / 2, M = 2 * m + 1;
            var c = new double[3][]; var t = new double[M + 1];
            for (int a = 0; a < 3; ++a) {
                c[a] = new double[M + 1];
                for (int i = 0; i <= M; ++i) c[a][i] = (i <= m) ? -(h[a] - r) : (h[a] - r);
            }
            for (int i = 0; i <= m; ++i) {
                double tan = (i == m) ? 1 : Math.Tan(Math.PI / 4 * i / m);
                t[m + 1 + i] = tan; t[m - i] = -tan;
            }
            var mesh = new DMesh3(); mesh.EnableTriangleGroups();
            var ids = new Dictionary<(int, int, int), int>();
            Func<int, int, int, int> vertex = (i, j, k) => {
                if (ids.TryGetValue((i, j, k), out int id)) return id;
                Vector3d d = new Vector3d(t[i], t[j], t[k]).Normalized;
                id = mesh.AppendVertex(new Vector3d(c[0][i], c[1][j], c[2][k]) + r * d);
                ids[(i, j, k)] = id;
                return id;
            };
            for (int axis = 0; axis < 3; ++axis) {
                for (int side = 0; side < 2; ++side) {
                    for (int u = 0; u < M; ++u) {
                        for (int v = 0; v < M; ++v) {
                            var q = new int[4];
                            int[][] uv = { new[] { u, v }, new[] { u + 1, v }, new[] { u + 1, v + 1 }, new[] { u, v + 1 } };
                            for (int n = 0; n < 4; ++n) {
                                var idx = new int[3];
                                idx[axis] = side * M; idx[(axis + 1) % 3] = uv[n][0]; idx[(axis + 2) % 3] = uv[n][1];
                                q[n] = vertex(idx[0], idx[1], idx[2]);
                            }
                            // (u, v, axis) is right handed, so the quad faces +axis; the other side is reversed
                            if (side == 0) { int tmp = q[1]; q[1] = q[3]; q[3] = tmp; }
                            int cls = (u != m ? 1 : 0) + (v != m ? 1 : 0);
                            mesh.AppendTriangle(new Index3i(q[0], q[1], q[2]), Group(cls, 0));
                            mesh.AppendTriangle(new Index3i(q[0], q[2], q[3]), Group(cls, 0));
                        }
                    }
                }
            }
            var rect = new[] { new Vector2d(-h.x, -h.y), new Vector2d(h.x, -h.y), new Vector2d(h.x, h.y), new Vector2d(-h.x, h.y) };
            return new Solid {
                Name = name, Mesh = mesh, Radii = new[] { r }, MaxFilletRadius = r,
                Sharp = Extrude(new List<List<(Vector2d, int)>> { RoundPolygon(rect, new double[4], new int[4], 1) }, 2 * h.z)
            };
        }

        /// <summary> L profile, extruded: five convex vertical edges rounded with 0.1, the concave one with 0.2; the rims stay sharp </summary>
        public static Solid LProfile(int segments)
        {
            var p = new[] { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2, 0.8), new Vector2d(0.8, 0.8), new Vector2d(0.8, 2), new Vector2d(0, 2) };
            for (int i = 0; i < p.Length; ++i) p[i] -= new Vector2d(0.7, 0.7);
            var rad = new[] { 0.1, 0.1, 0.1, 0.2, 0.1, 0.1 };
            var idx = new[] { 0, 0, 0, 1, 0, 0 };
            return new Solid {
                Name = "lprofile", Radii = new[] { 0.1, 0.2 }, MaxFilletRadius = 0.2,
                Mesh = Extrude(new List<List<(Vector2d, int)>> { RoundPolygon(p, rad, idx, segments) }, 0.6),
                Sharp = Extrude(new List<List<(Vector2d, int)>> { RoundPolygon(p, new double[6], new int[6], 1) }, 0.6)
            };
        }

        /// <summary>
        /// Spur gear with 12 trapezoid teeth: tip corners rounded with 0.03 (convex), root corners with 0.04 (concave), a bore of
        /// radius 0.3 (a cylinder that is not a fillet), sharp rims.
        /// </summary>
        public static Solid Gear(int segments)
        {
            const int teeth = 12; const double rootR = 0.8, tipR = 1.0, bore = 0.3, height = 0.4;
            double pitch = 2 * Math.PI / teeth;
            var p = new List<Vector2d>(); var rad = new List<double>(); var idx = new List<int>();
            for (int k = 0; k < teeth; ++k) {
                double a = k * pitch;
                double[] ang = { a - 0.30 * pitch, a - 0.17 * pitch, a + 0.17 * pitch, a + 0.30 * pitch };
                double[] rr = { rootR, tipR, tipR, rootR };
                for (int n = 0; n < 4; ++n) {
                    p.Add(new Vector2d(rr[n] * Math.Cos(ang[n]), rr[n] * Math.Sin(ang[n])));
                    bool tip = n == 1 || n == 2;
                    rad.Add(tip ? 0.03 : 0.04); idx.Add(tip ? 0 : 1);
                }
            }
            Func<bool, DMesh3> build = (round) => {
                var outer = RoundPolygon(p.ToArray(), round ? rad.ToArray() : new double[p.Count], idx.ToArray(), round ? segments : 1);
                var hole = new List<(Vector2d, int)>();
                int n = 4 * Math.Max(segments, 2);
                for (int i = 0; i < n; ++i) {   // clockwise, so that the wall faces the axis
                    double a = -2 * Math.PI * i / n;
                    hole.Add((new Vector2d(bore * Math.Cos(a), bore * Math.Sin(a)), Group(Cylinder, 2)));
                }
                return Extrude(new List<List<(Vector2d, int)>> { outer, hole }, height);
            };
            return new Solid { Name = "gear", Radii = new[] { 0.03, 0.04, bore }, MaxFilletRadius = 0.04, Mesh = build(true), Sharp = build(false) };
        }

        /// <summary> a plain rod (cylinder of radius 0.5 with sharp rims): nothing on it is a fillet </summary>
        public static Solid Rod(int segments)
        {
            int n = 4 * Math.Max(segments, 2);
            var loop = new List<(Vector2d, int)>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                loop.Add((new Vector2d(0.5 * Math.Cos(a), 0.5 * Math.Sin(a)), Group(Cylinder, 0)));
            }
            DMesh3 mesh = Extrude(new List<List<(Vector2d, int)>> { loop }, 1.5);
            return new Solid { Name = "rod", Radii = new[] { 0.5 }, MaxFilletRadius = 0.5, Mesh = mesh, Sharp = new DMesh3(mesh) };
        }

        /// <summary>
        /// The closed polygon with its corners rounded (radius 0: sharp): points in order, each with the group of the segment
        /// that starts at it. An arc of a quarter circle gets `segments` chords, other turning angles in proportion.
        /// </summary>
        public static List<(Vector2d, int)> RoundPolygon(Vector2d[] corners, double[] radius, int[] radiusIndex, int segments)
        {
            var result = new List<(Vector2d, int)>();
            int n = corners.Length;
            for (int i = 0; i < n; ++i) {
                Vector2d a = corners[(i + n - 1) % n], b = corners[i], c = corners[(i + 1) % n];
                Vector2d u = (b - a).Normalized, w = (c - b).Normalized;
                if (radius[i] <= 0) { result.Add((b, Group(Flat, 0))); continue; }
                double phi = Math.Atan2(u.x * w.y - u.y * w.x, u.Dot(w));
                double r = radius[i], tl = r * Math.Tan(Math.Abs(phi) / 2);
                if (tl > 0.45 * Math.Min((b - a).Length, (c - b).Length)) throw new ArgumentException("fillet radius too large for the profile");
                Vector2d start = b - tl * u;
                Vector2d center = start + Math.Sign(phi) * r * new Vector2d(-u.y, u.x);
                Vector2d arm = start - center;
                int steps = Math.Max(1, (int)Math.Round(segments * Math.Abs(phi) / (Math.PI / 2)));
                for (int k = 0; k <= steps; ++k) {
                    double ang = phi * k / steps, cs = Math.Cos(ang), sn = Math.Sin(ang);
                    Vector2d q = center + new Vector2d(cs * arm.x - sn * arm.y, sn * arm.x + cs * arm.y);
                    result.Add((q, k < steps ? Group(Edge, radiusIndex[i]) : Group(Flat, 0)));
                }
            }
            return result;
        }

        /// <summary>
        /// Prism over the outer loop (counter-clockwise) minus an optional hole (clockwise, star shaped around the origin like
        /// the outer loop), centred in z. One quad per profile segment (CAD strips), planar caps.
        /// </summary>
        public static DMesh3 Extrude(List<List<(Vector2d, int)>> loops, double height)
        {
            var mesh = new DMesh3(); mesh.EnableTriangleGroups();
            var bottom = new List<int[]>(); var top = new List<int[]>();
            foreach (var loop in loops) {
                int n = loop.Count;
                var lo = new int[n]; var hi = new int[n];
                for (int i = 0; i < n; ++i) {
                    lo[i] = mesh.AppendVertex(new Vector3d(loop[i].Item1.x, loop[i].Item1.y, -height / 2));
                    hi[i] = mesh.AppendVertex(new Vector3d(loop[i].Item1.x, loop[i].Item1.y, height / 2));
                }
                for (int i = 0; i < n; ++i) {
                    int j = (i + 1) % n;
                    mesh.AppendTriangle(new Index3i(lo[i], lo[j], hi[j]), loop[i].Item2);
                    mesh.AppendTriangle(new Index3i(lo[i], hi[j], hi[i]), loop[i].Item2);
                }
                bottom.Add(lo); top.Add(hi);
            }
            List<Index3i> cap;
            if (loops.Count == 1)
                cap = EarClip(loops[0].ConvertAll(x => x.Item1));
            else {
                var hole = loops[1].ConvertAll(x => x.Item1); hole.Reverse();
                cap = Zipper(loops[0].ConvertAll(x => x.Item1), hole);
                int no = loops[0].Count, nh = hole.Count;
                // zipper indices: outer 0..no-1, then the reversed hole
                for (int i = 0; i < cap.Count; ++i) {
                    Index3i tr = cap[i];
                    for (int k = 0; k < 3; ++k) if (tr[k] >= no) tr[k] = no + (nh - 1 - (tr[k] - no));
                    cap[i] = tr;
                }
            }
            Func<List<int[]>, int, int> map = (rings, i) => i < rings[0].Length ? rings[0][i] : rings[1][i - rings[0].Length];
            foreach (Index3i tr in cap) {
                mesh.AppendTriangle(new Index3i(map(top, tr.a), map(top, tr.b), map(top, tr.c)), Group(Flat, 0));
                mesh.AppendTriangle(new Index3i(map(bottom, tr.a), map(bottom, tr.c), map(bottom, tr.b)), Group(Flat, 0));
            }
            return mesh;
        }

        static double Cross(Vector2d a, Vector2d b, Vector2d c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

        static List<Index3i> EarClip(List<Vector2d> p)
        {
            var idx = new List<int>(); for (int i = 0; i < p.Count; ++i) idx.Add(i);
            var tris = new List<Index3i>();
            while (idx.Count > 3) {
                int best = -1; double bestScore = double.NegativeInfinity;
                for (int i = 0; i < idx.Count; ++i) {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    double area = Cross(p[a], p[b], p[c]);
                    if (area <= 1e-14) continue;
                    bool empty = true;
                    foreach (int k in idx) {
                        if (k == a || k == b || k == c) continue;
                        if (Cross(p[a], p[b], p[k]) >= 0 && Cross(p[b], p[c], p[k]) >= 0 && Cross(p[c], p[a], p[k]) >= 0) { empty = false; break; }
                    }
                    if (!empty) continue;
                    // the ear with the best shape (smallest longest edge relative to its area), for fewer slivers
                    double longest = Math.Max((p[a] - p[b]).LengthSquared, Math.Max((p[b] - p[c]).LengthSquared, (p[c] - p[a]).LengthSquared));
                    double score = area / longest;
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                if (best < 0) throw new InvalidOperationException("ear clipping found no ear");
                tris.Add(new Index3i(idx[(best + idx.Count - 1) % idx.Count], idx[best], idx[(best + 1) % idx.Count]));
                idx.RemoveAt(best);
            }
            tris.Add(new Index3i(idx[0], idx[1], idx[2]));
            return tris;
        }

        // triangles of the ring between two counter-clockwise loops that are star shaped around the origin, advancing by polar angle
        static List<Index3i> Zipper(List<Vector2d> outer, List<Vector2d> inner)
        {
            int n = outer.Count, m = inner.Count;
            Func<List<Vector2d>, int> first = (l) => { int b = 0; for (int i = 1; i < l.Count; ++i) if (Math.Atan2(l[i].y, l[i].x) < Math.Atan2(l[b].y, l[b].x)) b = i; return b; };
            Func<Vector2d, Vector2d, double> turn = (a, b) => Math.Atan2(a.x * b.y - a.y * b.x, a.Dot(b));
            int i0 = first(outer), j0 = first(inner), ia = 0, jb = 0;
            double angA = Math.Atan2(outer[i0].y, outer[i0].x), angB = Math.Atan2(inner[j0].y, inner[j0].x);
            var tris = new List<Index3i>();
            while (ia < n || jb < m) {
                int a = (i0 + ia) % n, a1 = (i0 + ia + 1) % n, b = (j0 + jb) % m, b1 = (j0 + jb + 1) % m;
                double nextA = angA + turn(outer[a], outer[a1]), nextB = angB + turn(inner[b], inner[b1]);
                if (ia < n && (jb >= m || nextA <= nextB)) { tris.Add(new Index3i(a, a1, n + b)); ++ia; angA = nextA; }
                else { tris.Add(new Index3i(a, n + b1, n + b)); ++jb; angB = nextB; }
            }
            return tris;
        }

        /// <summary>
        /// Splits every edge longer than maxLength at its midpoint (longest first), which keeps the surface and the groups:
        /// the uniform tessellation the paper assumes, on the same faceted geometry.
        /// </summary>
        public static DMesh3 Refine(DMesh3 source, double maxLength)
        {
            var mesh = new DMesh3(source, true);
            for (int pass = 0; pass < 64; ++pass) {
                var longEdges = new List<(double, int)>();
                foreach (int eid in mesh.EdgeIndices()) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    double len = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                    if (len > maxLength) longEdges.Add((-len, eid));
                }
                if (longEdges.Count == 0) break;
                longEdges.Sort();
                foreach (var (negLen, eid) in longEdges) {
                    if (!mesh.IsEdge(eid)) continue;
                    Index2i ev = mesh.GetEdgeV(eid);
                    if (mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)) <= maxLength) continue;
                    mesh.SplitEdge(eid, out DMesh3.EdgeSplitInfo info);
                }
            }
            return new DMesh3(mesh, true);
        }

        public Solid With(DMesh3 mesh, string suffix)
        {
            return new Solid { Name = Name + suffix, Mesh = mesh, Sharp = Sharp, Radii = Radii, MaxFilletRadius = MaxFilletRadius };
        }

        /// <summary> the solid rotated (37 degrees about (1, 2, 3)) and moved, to take the axis alignment away </summary>
        public Solid Rotated()
        {
            var q = new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37);
            var shift = new Vector3d(0.31, -0.17, 0.23);
            Func<DMesh3, DMesh3> move = (m) => {
                var copy = new DMesh3(m, true);
                foreach (int vid in copy.VertexIndices()) copy.SetVertex(vid, q * copy.GetVertex(vid) + shift);
                return copy;
            };
            return new Solid { Name = Name + "@rot", Mesh = move(Mesh), Sharp = move(Sharp), Radii = Radii, MaxFilletRadius = MaxFilletRadius };
        }
    }
}
