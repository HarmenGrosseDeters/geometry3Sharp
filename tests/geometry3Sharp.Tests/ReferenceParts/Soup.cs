using System;
using System.Collections.Generic;
using g3;

namespace ReferenceParts
{
    /// <summary>
    /// A rigid motion plus uniform scale of a part (canonical coordinates to the coordinates handed to the operations). Not the
    /// test harness's Similarity, so that the benchmark can use these files without the harness.
    /// </summary>
    public sealed class PartFrame
    {
        public readonly double Scale;
        public readonly Matrix3d Rotation;
        public readonly Vector3d Translation;

        public PartFrame(double scale, Matrix3d rotation, Vector3d translation)
        {
            Scale = scale; Rotation = rotation; Translation = translation;
        }

        public static readonly PartFrame Identity = new PartFrame(1, Matrix3d.Identity, Vector3d.Zero);

        public Vector3d Apply(Vector3d p) { return Scale * (Rotation * p) + Translation; }
        public Vector3d Inverse(Vector3d p) { return Rotation.Transpose() * ((p - Translation) / Scale); }
        public Vector3d ApplyDirection(Vector3d d) { return Rotation * d; }
    }

    /// <summary> the defects put into a soup after it is built (all deterministic) </summary>
    public sealed class DefectSet
    {
        /// <summary> every triangle owns its three vertices (an STL-like soup); false: corners at one position share a vertex </summary>
        public bool Unwelded = true;
        /// <summary> every k-th triangle is reversed (0: none) </summary>
        public int FlipEvery = 0;
        /// <summary> every k-th triangle appears twice (0: none) </summary>
        public int DuplicateEvery = 0;
        /// <summary> the triangles of the group "top" move by this fraction of the part size along their normal: a small gap along their edges (0: none) </summary>
        public double GapFraction = 0;

        public static DefectSet None() { return new DefectSet() { Unwelded = false }; }
        public static DefectSet Soup() { return new DefectSet(); }
        public static DefectSet Faulty() { return new DefectSet() { FlipEvery = 13, DuplicateEvery = 17 }; }
        public static DefectSet FaultyWithGap(double gapFraction) { return new DefectSet() { FlipEvery = 13, DuplicateEvery = 17, GapFraction = gapFraction }; }

        public override string ToString()
        {
            var s = new List<string>();
            s.Add(Unwelded ? "unwelded" : "welded");
            if (FlipEvery > 0) s.Add("flipped/" + FlipEvery);
            if (DuplicateEvery > 0) s.Add("dup/" + DuplicateEvery);
            if (GapFraction > 0) s.Add("gap " + GapFraction.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("+", s);
        }
    }

    /// <summary>
    /// A triangle soup built analytically (no boolean): triangles with three corner positions each, in groups. Every
    /// face is built from the construction's own vertices, so the soup, welded, is a closed manifold with a known volume.
    /// </summary>
    public sealed class Soup
    {
        public readonly List<Vector3d> Corners = new List<Vector3d>();     // 3 per triangle
        public readonly List<string> Groups = new List<string>();          // 1 per triangle

        public int TriangleCount { get { return Groups.Count; } }

        public void Tri(string group, Vector3d a, Vector3d b, Vector3d c)
        {
            Corners.Add(a); Corners.Add(b); Corners.Add(c); Groups.Add(group);
        }

        /// <summary> the points of a circle polygon: centre + radius (cos u + sin v), n of them </summary>
        public static Vector3d[] Ring(Vector3d centre, Vector3d u, Vector3d v, double radius, int n)
        {
            var r = new Vector3d[n];
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                r[i] = centre + radius * (Math.Cos(a) * u + Math.Sin(a) * v);
            }
            return r;
        }

        /// <summary> the area of the regular n-gon inscribed in a circle of this radius (what the construction's rings enclose) </summary>
        public static double PolygonArea(double radius, int n) { return 0.5 * n * radius * radius * Math.Sin(2 * Math.PI / n); }

        /// <summary> the distance from the centre to the edges of the regular n-gon (the clearance of a bore built from it) </summary>
        public static double PolygonInradius(double radius, int n) { return radius * Math.Cos(Math.PI / n); }

        /// <summary> an axis-aligned box as 12 triangles, outward normals </summary>
        public void Box(string group, Vector3d min, Vector3d max, string topGroup = null)
        {
            var p = new Vector3d[8];
            for (int i = 0; i < 8; ++i)
                p[i] = new Vector3d((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            Plate(group, new[] { p[0], p[2], p[3], p[1] }, null, -Vector3d.AxisZ);
            Plate(topGroup ?? group, new[] { p[4], p[5], p[7], p[6] }, null, Vector3d.AxisZ);
            Plate(group, new[] { p[0], p[1], p[5], p[4] }, null, -Vector3d.AxisY);
            Plate(group, new[] { p[2], p[6], p[7], p[3] }, null, Vector3d.AxisY);
            Plate(group, new[] { p[0], p[4], p[6], p[2] }, null, -Vector3d.AxisX);
            Plate(group, new[] { p[1], p[3], p[7], p[5] }, null, Vector3d.AxisX);
        }

        /// <summary>
        /// A planar face with the given outward normal: a convex outer polygon and convex holes (all inside it; any winding), by ear
        /// clipping after bridging each hole to the outer boundary. The face uses only the given points as corners.
        /// </summary>
        public void Plate(string group, IList<Vector3d> outer, IList<Vector3d[]> holes, Vector3d normal)
        {
            normal = normal.Normalized;
            Vector3d u = Math.Abs(normal.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY;
            u = (u - u.Dot(normal) * normal).Normalized;
            Vector3d v = normal.Cross(u);
            var pts = new List<Vector3d>(); var xy = new List<Vector2d>();
            Func<Vector3d, Vector2d> to2 = p => new Vector2d(p.Dot(u), p.Dot(v));
            // outer polygon counter-clockwise in (u, v)
            var ring = new List<int>();
            var o = new List<Vector3d>(outer);
            if (area2(o, to2) < 0) o.Reverse();
            foreach (Vector3d p in o) { ring.Add(pts.Count); pts.Add(p); xy.Add(to2(p)); }
            if (holes != null) {
                // the holes, clockwise; bridged one after the other, the one with the largest u coordinate first
                var hs = new List<List<Vector3d>>();
                foreach (Vector3d[] h in holes) { var l = new List<Vector3d>(h); if (area2(l, to2) > 0) l.Reverse(); hs.Add(l); }
                hs.Sort((a, b) => maxU(b, u).CompareTo(maxU(a, u)));
                foreach (List<Vector3d> h in hs) {
                    var hid = new List<int>();
                    foreach (Vector3d p in h) { hid.Add(pts.Count); pts.Add(p); xy.Add(to2(p)); }
                    bridge(ring, hid, xy);
                }
            }
            earClip(ring, pts, xy, group);
        }

        static double maxU(List<Vector3d> l, Vector3d u) { double m = double.NegativeInfinity; foreach (Vector3d p in l) m = Math.Max(m, p.Dot(u)); return m; }

        static double area2(List<Vector3d> l, Func<Vector3d, Vector2d> to2)
        {
            double a = 0;
            for (int i = 0; i < l.Count; ++i) { Vector2d p = to2(l[i]), q = to2(l[(i + 1) % l.Count]); a += p.x * q.y - q.x * p.y; }
            return a;
        }

        static double cross(Vector2d o, Vector2d a, Vector2d b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        static bool segmentsCross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double d1 = cross(a, b, c), d2 = cross(a, b, d), d3 = cross(c, d, a), d4 = cross(c, d, b);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        // splice the clockwise hole into the ring through a bridge between a hole vertex and a ring vertex that see each other
        static void bridge(List<int> ring, List<int> hole, List<Vector2d> xy)
        {
            var best = new List<KeyValuePair<double, int[]>>();
            for (int i = 0; i < ring.Count; ++i)
                for (int j = 0; j < hole.Count; ++j)
                    best.Add(new KeyValuePair<double, int[]>((xy[ring[i]] - xy[hole[j]]).LengthSquared, new[] { i, j }));
            best.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var cand in best) {
                int i = cand.Value[0], j = cand.Value[1];
                Vector2d a = xy[ring[i]], b = xy[hole[j]];
                bool ok = true;
                for (int k = 0; k < ring.Count && ok; ++k)
                    if (segmentsCross(a, b, xy[ring[k]], xy[ring[(k + 1) % ring.Count]])) ok = false;
                for (int k = 0; k < hole.Count && ok; ++k)
                    if (segmentsCross(a, b, xy[hole[k]], xy[hole[(k + 1) % hole.Count]])) ok = false;
                if (ok && hole.Count > 0) {
                    // the bridge must leave the hole outward: its midpoint is not inside the hole polygon (convex, clockwise)
                    Vector2d mid = 0.5 * (a + b);
                    bool inside = true;
                    for (int k = 0; k < hole.Count; ++k)
                        if (cross(xy[hole[k]], xy[hole[(k + 1) % hole.Count]], mid) > 0) { inside = false; break; }
                    if (inside) ok = false;
                }
                if (!ok) continue;
                var merged = new List<int>();
                for (int k = 0; k <= i; ++k) merged.Add(ring[k]);
                for (int k = 0; k <= hole.Count; ++k) merged.Add(hole[(j + k) % hole.Count]);
                merged.Add(ring[i]);
                for (int k = i + 1; k < ring.Count; ++k) merged.Add(ring[k]);
                ring.Clear(); ring.AddRange(merged);
                return;
            }
            throw new InvalidOperationException("no bridge between a hole and the outer polygon");
        }

        void earClip(List<int> ring, List<Vector3d> pts, List<Vector2d> xy, string group)
        {
            var r = new List<int>(ring);
            int guard = 4 * r.Count * r.Count + 16;
            while (r.Count > 3 && guard-- > 0) {
                bool clipped = false;
                for (int i = 0; i < r.Count && !clipped; ++i) {
                    int ia = r[(i + r.Count - 1) % r.Count], ib = r[i], ic = r[(i + 1) % r.Count];
                    Vector2d a = xy[ia], b = xy[ib], c = xy[ic];
                    if (cross(a, b, c) <= 1e-12 * ((b - a).Length * (c - a).Length)) continue;      // reflex or degenerate corner
                    bool empty = true;
                    for (int k = 0; k < r.Count && empty; ++k) {
                        int ik = r[k];
                        if (ik == ia || ik == ib || ik == ic) continue;
                        Vector2d p = xy[ik];
                        if ((p - a).LengthSquared == 0 || (p - b).LengthSquared == 0 || (p - c).LengthSquared == 0) continue;   // a bridge's duplicate
                        if (cross(a, b, p) >= 0 && cross(b, c, p) >= 0 && cross(c, a, p) >= 0) empty = false;
                    }
                    if (!empty) continue;
                    Tri(group, pts[ia], pts[ib], pts[ic]);
                    r.RemoveAt(i);
                    clipped = true;
                }
                if (!clipped) break;
            }
            if (r.Count == 3) Tri(group, pts[r[0]], pts[r[1]], pts[r[2]]);
            else throw new InvalidOperationException("ear clipping stopped with " + r.Count + " corners left");
        }

        /// <summary>
        /// The wall between two rings of equal length (quads as two triangles), oriented away from the axis (outward) or towards it
        /// (a bore). The axis runs from axisA to axisB.
        /// </summary>
        public void Wall(string group, Vector3d[] ringA, Vector3d[] ringB, Vector3d axisA, Vector3d axisB, bool outward)
        {
            int n = ringA.Length;
            Vector3d axis = (axisB - axisA).Normalized;
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                Vector3d a = ringA[i], b = ringA[j], c = ringB[j], d = ringB[i];
                Vector3d mid = 0.25 * (a + b + c + d) - axisA;
                Vector3d radial = mid - mid.Dot(axis) * axis;
                Vector3d nrm = (b - a).Cross(c - a);
                bool flip = (nrm.Dot(radial) > 0) != outward;
                if (!flip) { Tri(group, a, b, c); Tri(group, a, c, d); }
                else { Tri(group, a, c, b); Tri(group, a, d, c); }
            }
        }

        /// <summary> a copy with every corner mapped </summary>
        public Soup Mapped(Func<Vector3d, Vector3d> f)
        {
            var s = new Soup();
            for (int t = 0; t < TriangleCount; ++t)
                s.Tri(Groups[t], f(Corners[3 * t]), f(Corners[3 * t + 1]), f(Corners[3 * t + 2]));
            return s;
        }

        /// <summary> the signed volume of the soup (divergence theorem about the first corner), the sum over all triangles </summary>
        public double Volume()
        {
            if (TriangleCount == 0) return 0;
            Vector3d c = Corners[0];
            double v = 0;
            for (int t = 0; t < TriangleCount; ++t)
                v += (Corners[3 * t] - c).Dot((Corners[3 * t + 1] - c).Cross(Corners[3 * t + 2] - c));
            return v / 6;
        }

        public double Area()
        {
            double a = 0;
            for (int t = 0; t < TriangleCount; ++t)
                a += 0.5 * (Corners[3 * t + 1] - Corners[3 * t]).Cross(Corners[3 * t + 2] - Corners[3 * t]).Length;
            return a;
        }

        /// <summary>
        /// The mesh handed to the operations: the defects applied, in the frame. Unwelded: three vertices per triangle (an STL-like soup);
        /// else corners at one position share a vertex. The normal of the gap shift is the triangle's, before the frame.
        /// </summary>
        public DMesh3 ToMesh(DefectSet defects, PartFrame frame, double partSize)
        {
            if (!defects.Unwelded && defects.FlipEvery > 0)
                throw new ArgumentException("a welded soup cannot hold flipped triangles (they share directed edges with their neighbours): use Unwelded");
            var mesh = new DMesh3();
            var weld = new Dictionary<Vector3d, int>();
            Func<Vector3d, int> vertex = p => {
                if (defects.Unwelded) return mesh.AppendVertex(p);
                int id;
                if (!weld.TryGetValue(p, out id)) { id = mesh.AppendVertex(p); weld[p] = id; }
                return id;
            };
            for (int t = 0; t < TriangleCount; ++t) {
                Vector3d a = Corners[3 * t], b = Corners[3 * t + 1], c = Corners[3 * t + 2];
                if (defects.GapFraction > 0 && Groups[t] == "top") {
                    Vector3d n = (b - a).Cross(c - a).Normalized;
                    Vector3d shift = defects.GapFraction * partSize * n;
                    a += shift; b += shift; c += shift;
                }
                a = frame.Apply(a); b = frame.Apply(b); c = frame.Apply(c);
                bool flip = defects.FlipEvery > 0 && t % defects.FlipEvery == defects.FlipEvery - 1;
                int copies = (defects.DuplicateEvery > 0 && t % defects.DuplicateEvery == defects.DuplicateEvery - 1) ? 2 : 1;
                for (int k = 0; k < copies; ++k) {
                    int ia = vertex(a), ib = vertex(b), ic = vertex(c);
                    int tid = flip ? mesh.AppendTriangle(ia, ic, ib) : mesh.AppendTriangle(ia, ib, ic);
                    if (tid < 0) {
                        // a welded soup with a repeated triangle cannot hold it as a second copy: the duplicate is dropped there
                        if (defects.Unwelded) throw new InvalidOperationException("triangle not appended");
                    }
                }
            }
            return mesh;
        }
    }
}
