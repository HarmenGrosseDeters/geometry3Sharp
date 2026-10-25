// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Break lines of a terrain mesh: edges where the surface bends by more than MinAngle, split into
    /// crests (convex, eg the top edge of a bench face, Boeschungsoberkante) and toes (concave, the foot
    /// of a face, Boeschungsunterkante), chained into polylines. Best effort: where a break line does not
    /// run along mesh edges (on TerrainDualContouring output where it only clips a cell), its bend is
    /// spread over several edges, so the lines are about a cell off and can have gaps (open pit test, 1 m
    /// cells: 85% of the crest length found). For safety lines use complete, surveyed break lines.
    /// </summary>
    public class TerrainBreakLines
    {
        public DMesh3 Terrain;
        /// <summary> minimum bend angle between the two triangles of an edge (degrees) </summary>
        public double MinAngle = 20;
        /// <summary>
        /// polylines shorter than this are dropped (small kinks along the other kind of break line, eg a
        /// slightly convex edge on a toe); 0 = 3 x the mean edge length
        /// </summary>
        public double MinLineLength = 0;
        /// <summary>
        /// end points of open polylines closer than this are joined (the break line runs through a triangle
        /// there, eg where it only clips a cell of TerrainDualContouring); 0 = 3 x the mean edge length.
        /// Joined parts are straight segments, so the position lists may contain points between vertices;
        /// CrestVertices / ToeVertices are not joined.
        /// </summary>
        public double GapClosingDistance = 0;

        // outputs: polylines as positions and as vertex ids (same order)
        public List<List<Vector3d>> Crests = new List<List<Vector3d>>(), Toes = new List<List<Vector3d>>();
        public List<List<int>> CrestVertices = new List<List<int>>(), ToeVertices = new List<List<int>>();

        public TerrainBreakLines(DMesh3 terrain)
        {
            Terrain = terrain;
        }

        public bool Compute()
        {
            Crests.Clear(); Toes.Clear(); CrestVertices.Clear(); ToeVertices.Clear();
            double cosMin = Math.Cos(MinAngle * MathUtil.Deg2Rad);
            var crestEdges = new List<int>();
            var toeEdges = new List<int>();
            foreach (int eid in Terrain.EdgeIndices()) {
                Index2i et = Terrain.GetEdgeT(eid);
                if (et.b < 0) continue;
                Vector3d n0 = up(Terrain.GetTriNormal(et.a)), n1 = up(Terrain.GetTriNormal(et.b));
                if (n0.Dot(n1) > cosMin) continue;
                // convex if the far corner of the second triangle is below the plane of the first
                Index2i ev = Terrain.GetEdgeV(eid);
                Index3i t1 = Terrain.GetTriangle(et.b);
                int opp = (t1.a != ev.a && t1.a != ev.b) ? t1.a : (t1.b != ev.a && t1.b != ev.b) ? t1.b : t1.c;
                double side = n0.Dot(Terrain.GetVertex(opp) - Terrain.GetVertex(ev.a));
                (side < 0 ? crestEdges : toeEdges).Add(eid);
            }
            double minLen = MinLineLength;
            if (minLen <= 0) {
                double sum = 0; int n = 0;
                foreach (int eid in Terrain.EdgeIndices()) { Index2i ev = Terrain.GetEdgeV(eid); sum += Terrain.GetVertex(ev.a).Distance(Terrain.GetVertex(ev.b)); n++; }
                minLen = 3 * sum / Math.Max(1, n);
            }
            prune(crestEdges, minLen);
            prune(toeEdges, minLen);
            chain(crestEdges, Crests, CrestVertices);
            chain(toeEdges, Toes, ToeVertices);
            double gap = (GapClosingDistance > 0) ? GapClosingDistance : minLen;
            close_gaps(Crests, gap);
            close_gaps(Toes, gap);
            return true;
        }

        static Vector3d up(Vector3d n) { return (n.z < 0) ? -n : n; }

        // join the closest pair of open-polyline end points while it is closer than gap
        static void close_gaps(List<List<Vector3d>> lines, double gap)
        {
            while (true) {
                double best = gap; int bi = -1, bj = -1; bool biEnd = false, bjEnd = false;
                for (int i = 0; i < lines.Count; ++i) {
                    var li = lines[i];
                    if (li.Count < 2 || li[0].Distance(li[li.Count - 1]) < 1e-9) continue;     // closed
                    for (int j = i; j < lines.Count; ++j) {
                        var lj = lines[j];
                        if (lj.Count < 2 || lj[0].Distance(lj[lj.Count - 1]) < 1e-9) continue;
                        foreach (bool ei in new[] { false, true })
                            foreach (bool ej in new[] { false, true }) {
                                if (i == j && ei == ej) continue;
                                Vector3d a = ei ? li[li.Count - 1] : li[0], b = ej ? lj[lj.Count - 1] : lj[0];
                                double d = a.Distance(b);
                                if (d < best) { best = d; bi = i; bj = j; biEnd = ei; bjEnd = ej; }
                            }
                    }
                }
                if (bi < 0)
                    return;
                if (bi == bj) {
                    lines[bi].Add(lines[bi][0]);        // close the loop
                    continue;
                }
                var A = lines[bi]; var B = lines[bj];
                if (biEnd == false) A.Reverse();         // A now ends at the joined end
                if (bjEnd) B.Reverse();                  // B now starts at the joined end
                A.AddRange(B);
                lines.RemoveAt(bj);
            }
        }
        static long edge_key(int a, int b) { return (long)Math.Min(a, b) * int.MaxValue + Math.Max(a, b); }

        // remove short spurs (dead-end pieces shorter than minLen hanging off a line, eg small kinks next to
        // a break line) and short isolated pieces; repeated until nothing changes
        void prune(List<int> edges, double minLen)
        {
            var set = new HashSet<long>();
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (int eid in edges) {
                Index2i ev = Terrain.GetEdgeV(eid);
                set.Add(edge_key(ev.a, ev.b));
                if (!adj.ContainsKey(ev.a)) adj[ev.a] = new HashSet<int>();
                if (!adj.ContainsKey(ev.b)) adj[ev.b] = new HashSet<int>();
                adj[ev.a].Add(ev.b); adj[ev.b].Add(ev.a);
            }
            bool changed = true;
            while (changed) {
                changed = false;
                foreach (int start in new List<int>(adj.Keys)) {
                    if (adj.ContainsKey(start) == false || adj[start].Count != 1) continue;
                    // walk from the dead end to the next junction or end
                    var chainV = new List<int>() { start };
                    int prev = -1, cur = start;
                    double len = 0;
                    while (true) {
                        int next = -1;
                        foreach (int nb in adj[cur]) if (nb != prev) { next = nb; break; }
                        if (next < 0) break;
                        len += Terrain.GetVertex(cur).Distance(Terrain.GetVertex(next));
                        chainV.Add(next);
                        prev = cur; cur = next;
                        if (adj[cur].Count != 2) break;
                    }
                    bool spur = adj[cur].Count >= 3, isolated = adj[cur].Count == 1;
                    if ((spur || isolated) && len < minLen) {
                        for (int k = 1; k < chainV.Count; ++k) {
                            int a = chainV[k - 1], b = chainV[k];
                            adj[a].Remove(b); adj[b].Remove(a);
                            set.Remove(edge_key(a, b));
                        }
                        foreach (int v in chainV)
                            if (adj.ContainsKey(v) && adj[v].Count == 0) adj.Remove(v);
                        changed = true;
                    }
                }
            }
            edges.RemoveAll(eid => { Index2i ev = Terrain.GetEdgeV(eid); return set.Contains(edge_key(ev.a, ev.b)) == false; });
        }

        // polylines through the edges: open chains between vertices of degree != 2, then closed loops
        void chain(List<int> edges, List<List<Vector3d>> lines, List<List<int>> vertexLines)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (int eid in edges) {
                Index2i ev = Terrain.GetEdgeV(eid);
                if (!adj.ContainsKey(ev.a)) adj[ev.a] = new List<int>();
                if (!adj.ContainsKey(ev.b)) adj[ev.b] = new List<int>();
                adj[ev.a].Add(ev.b); adj[ev.b].Add(ev.a);
            }
            var used = new HashSet<long>();
            Func<int, int, long> key = (a, b) => (long)Math.Min(a, b) * int.MaxValue + Math.Max(a, b);
            Action<int, int> walk = (start, first) => {
                var line = new List<int>() { start };
                int prev = start, cur = first;
                used.Add(key(start, first));
                while (true) {
                    line.Add(cur);
                    if (adj[cur].Count != 2 || cur == start) break;
                    int next = (adj[cur][0] == prev) ? adj[cur][1] : adj[cur][0];
                    if (used.Contains(key(cur, next))) break;
                    used.Add(key(cur, next));
                    prev = cur; cur = next;
                }
                vertexLines.Add(line);
                var pts = new List<Vector3d>(line.Count);
                foreach (int v in line) pts.Add(Terrain.GetVertex(v));
                lines.Add(pts);
            };
            foreach (var kv in adj)
                if (kv.Value.Count != 2)
                    foreach (int nb in kv.Value)
                        if (!used.Contains(key(kv.Key, nb))) walk(kv.Key, nb);
            foreach (var kv in adj)
                foreach (int nb in kv.Value)
                    if (!used.Contains(key(kv.Key, nb))) walk(kv.Key, nb);
        }
    }


    /// <summary>
    /// Safety lines at a distance from source lines, eg crests (surveyed break lines or TerrainBreakLines),
    /// on the terrain; lines on both sides of the sources are returned.
    ///
    ///   - Horizontal (default): the distance measured horizontally, as safety distances from a crest
    ///     usually are. The line points are found by bisection on the terrain edges, so they lie exactly at
    ///     the distance; fast, no remeshing.
    ///   - AlongSurface: the geodesic distance along the terrain (heat method). That is accurate to about
    ///     2% plus half an edge length on regular meshes, so the band within 3 x SafetyDistance of the
    ///     sources is cut out and remeshed to EdgeLength (default SafetyDistance / 5) with its boundary and
    ///     break lines kept; RemeshedTerrain and Geodesic.Distance give the distance field there.
    /// </summary>
    public class TerrainSafetyLines
    {
        public enum Modes { Horizontal, AlongSurface }
        public Modes Mode = Modes.Horizontal;

        public DMesh3 Terrain;
        /// <summary> source points (on the terrain); polylines are densified </summary>
        public List<Vector3d> SourcePoints = new List<Vector3d>();
        public double SafetyDistance;
        /// <summary> AlongSurface: edge length for the distance computation; 0 = SafetyDistance / 5 </summary>
        public double EdgeLength = 0;
        /// <summary> AlongSurface: break lines bending more than this (degrees) are kept by the remeshing </summary>
        public double FeatureAngle = 20;

        // outputs
        public List<List<Vector3d>> Lines = new List<List<Vector3d>>();
        public DMesh3 RemeshedTerrain;
        public MeshGeodesicDistance Geodesic;

        public TerrainSafetyLines(DMesh3 terrain, IEnumerable<int> sourceVertices, double distance)
        {
            Terrain = terrain;
            SafetyDistance = distance;
            foreach (int v in sourceVertices)
                SourcePoints.Add(terrain.GetVertex(v));
        }

        /// <summary> sources given as polylines (eg surveyed break lines) </summary>
        public TerrainSafetyLines(DMesh3 terrain, IEnumerable<List<Vector3d>> polylines, double distance)
        {
            Terrain = terrain;
            SafetyDistance = distance;
            polyLines = new List<List<Vector3d>>(polylines);
        }
        List<List<Vector3d>> polyLines;

        public bool Compute()
        {
            return (Mode == Modes.Horizontal) ? compute_horizontal() : compute_along_surface();
        }


        // horizontal distance to the source segments (single points are degenerate segments)
        List<Segment2d> segments;
        PointHashGrid2d<int> segmentGrid;
        double gridCell;

        double horizontal_distance(Vector3d p)
        {
            double best = double.MaxValue;
            Vector2d q = p.xy;
            // segments are binned by their midpoint; search the neighbourhood the band can reach
            var near = segmentGrid.FindNearestInRadius(q, 2 * gridCell, (k) => Math.Sqrt(segments[k].DistanceSquared(q)));
            if (near.Key >= 0) best = near.Value;
            return best;
        }

        bool compute_horizontal()
        {
            Lines.Clear();
            segments = new List<Segment2d>();
            var lines = new List<List<Vector3d>>();
            if (polyLines != null) lines.AddRange(polyLines);
            foreach (Vector3d p in SourcePoints) lines.Add(new List<Vector3d>() { p });
            double maxLen = 0;
            foreach (var line in lines) {
                if (line.Count == 1) segments.Add(new Segment2d(line[0].xy, line[0].xy));
                for (int i = 0; i + 1 < line.Count; ++i) {
                    segments.Add(new Segment2d(line[i].xy, line[i + 1].xy));
                    maxLen = Math.Max(maxLen, line[i].xy.Distance(line[i + 1].xy));
                }
            }
            if (segments.Count == 0)
                return false;
            // a segment's points are within half its length of its midpoint: search radius covers that
            gridCell = SafetyDistance + 0.5 * maxLen + MathUtil.Epsilon;
            segmentGrid = new PointHashGrid2d<int>(gridCell, -1);
            for (int k = 0; k < segments.Count; ++k)
                segmentGrid.InsertPointUnsafe(k, segments[k].Center);
            var iso = new MeshIsoCurves(Terrain, (p) => horizontal_distance(p) - SafetyDistance) {
                RootMode = MeshIsoCurves.RootfindingModes.Bisection, RootModeSteps = 40
            };
            iso.Compute();
            chain_graph(iso.Graph);
            return true;
        }

        bool compute_along_surface()
        {
            Lines.Clear();
            double h = (EdgeLength > 0) ? EdgeLength : SafetyDistance / 5;
            if (h <= 0)
                return false;

            // source points: densified polylines and the given points
            var pts = new List<Vector3d>(SourcePoints);
            if (polyLines != null)
                foreach (var line in polyLines)
                    for (int i = 0; i + 1 < line.Count || (line.Count == 1 && i == 0); ++i) {
                        Vector3d a = line[i], b = (i + 1 < line.Count) ? line[i + 1] : line[i];
                        int steps = Math.Max(1, (int)Math.Ceiling(a.Distance(b) / (0.5 * h)));
                        for (int k = 0; k <= steps; ++k)
                            pts.Add(Vector3d.Lerp(a, b, (double)k / steps));
                    }
            if (pts.Count == 0)
                return false;

            // only the band within 3 x SafetyDistance of the sources matters (the distance along the
            // surface is at least the straight distance; the margin keeps the band boundary, where the
            // heat method is less accurate, away from the line): cut it out, then remesh it uniformly
            // with the break lines and its boundary kept
            double band = 3 * SafetyDistance + 2 * h;
            var grid = new PointHashGrid3d<int>(band, -1);
            for (int i = 0; i < pts.Count; ++i)
                grid.InsertPointUnsafe(i, pts[i]);
            var keep = new List<int>();
            foreach (int tid in Terrain.TriangleIndices()) {
                Vector3d c = Terrain.GetTriCentroid(tid);
                var near = grid.FindNearestInRadius(c, band, (i) => pts[i].Distance(c));
                if (near.Key >= 0) keep.Add(tid);
            }
            if (keep.Count == 0)
                return false;
            RemeshedTerrain = new DSubmesh3(Terrain, keep).SubMesh;
            var remesher = new Remesher(RemeshedTerrain);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, FeatureAngle);
            remesher.SetTargetEdgeLength(h);
            remesher.SmoothSpeedT = 0.5;
            for (int k = 0; k < 8; ++k)
                remesher.BasicRemeshPass();
            DMesh3 mesh = RemeshedTerrain;

            // sources mapped to the nearest vertices
            var spatial = new DMeshAABBTree3(mesh, true);
            var sources = new HashSet<int>();
            foreach (Vector3d p in pts) {
                int tid = spatial.FindNearestTriangle(p);
                if (tid < 0) continue;
                Index3i t = mesh.GetTriangle(tid);
                int best = t.a;
                foreach (int v in new[] { t.b, t.c })
                    if (mesh.GetVertex(v).DistanceSquared(p) < mesh.GetVertex(best).DistanceSquared(p)) best = v;
                sources.Add(best);
            }

            Geodesic = new MeshGeodesicDistance(mesh) { TimeFactor = 0.25 };
            if (Geodesic.Compute(sources) == false)
                return false;
            double[] dist = Geodesic.Distance;
            var iso = new MeshIsoCurves(mesh, (v) => 0) {
                VertexValueF = (vid) => dist[vid] - SafetyDistance,
                RootMode = MeshIsoCurves.RootfindingModes.SingleLerp
            };
            iso.Compute();
            chain_graph(iso.Graph);
            return true;
        }

        // chain the iso segments into polylines
        void chain_graph(DGraph3 g)
        {
            var visited = new HashSet<int>();
            foreach (int start in g.VertexIndices()) {
                if (visited.Contains(start) || g.GetVtxEdgeCount(start) == 2) continue;
                foreach (int nb in g.VtxVerticesItr(start)) {
                    var line = walk(g, start, nb, visited);
                    if (line.Count > 1) Lines.Add(line);
                }
            }
            foreach (int start in g.VertexIndices()) {
                if (visited.Contains(start)) continue;
                int nb = -1;
                foreach (int n in g.VtxVerticesItr(start)) { nb = n; break; }
                if (nb < 0) continue;
                var line = walk(g, start, nb, visited);
                if (line.Count > 1) Lines.Add(line);
            }
        }

        static List<Vector3d> walk(DGraph3 g, int start, int next, HashSet<int> visited)
        {
            var line = new List<Vector3d>() { g.GetVertex(start) };
            visited.Add(start);
            int prev = start, cur = next;
            while (true) {
                if (visited.Contains(cur)) {
                    if (cur == start) line.Add(g.GetVertex(cur));
                    break;
                }
                visited.Add(cur);
                line.Add(g.GetVertex(cur));
                if (g.GetVtxEdgeCount(cur) != 2) break;
                int nn = -1;
                foreach (int n in g.VtxVerticesItr(cur))
                    if (n != prev) { nn = n; break; }
                if (nn < 0) break;
                prev = cur; cur = nn;
            }
            return line;
        }
    }
}
