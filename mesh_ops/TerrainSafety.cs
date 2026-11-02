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
            // which way the (consistently oriented) mesh faces: up = above the terrain. The triangle normals
            // with this sign, not each flipped up on its own, so vertical walls and overhangs are classified
            // correctly
            double nz = 0;
            foreach (int tid in Terrain.TriangleIndices())
                nz += Terrain.GetTriNormal(tid).z * Terrain.GetTriArea(tid);
            double up = (nz < 0) ? -1 : 1;
            foreach (int eid in Terrain.EdgeIndices()) {
                Index2i et = Terrain.GetEdgeT(eid);
                if (et.b < 0) continue;
                Vector3d n0 = up * Terrain.GetTriNormal(et.a), n1 = up * Terrain.GetTriNormal(et.b);
                if (n0.Dot(n1) > cosMin) continue;
                // convex if the far corner of the second triangle is below the plane of the first (exact)
                Index2i ev = Terrain.GetEdgeV(eid);
                Index3i t0 = Terrain.GetTriangle(et.a), t1 = Terrain.GetTriangle(et.b);
                int opp = (t1.a != ev.a && t1.a != ev.b) ? t1.a : (t1.b != ev.a && t1.b != ev.b) ? t1.b : t1.c;
                int side = (int)up * ExactPredicates.Orient3d(Terrain.GetVertex(t0.a), Terrain.GetVertex(t0.b), Terrain.GetVertex(t0.c), Terrain.GetVertex(opp));
                if (side != 0)
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

        // Join the end points of open polylines closer than gap, the closest pair first (a pair of ends of
        // the same polyline closes it). Joining moves no point, so the candidate pairs are found once (hash
        // grid) and taken in order of distance while both ends are still free; then the joined polylines
        // are assembled. (Was quadratic per join, cubic overall: 105 s for 1500 lines of real crests.)
        internal static void close_gaps(List<List<Vector3d>> lines, double gap)
        {
            int n = lines.Count;
            Func<int, bool> open = (i) => lines[i].Count >= 2 && lines[i][0].Distance(lines[i][lines[i].Count - 1]) >= 1e-9;
            // end e of line e / 2: the first point if e is even, the last if odd
            Func<int, Vector3d> endPoint = (e) => { var l = lines[e / 2]; return (e % 2 == 0) ? l[0] : l[l.Count - 1]; };
            double cellSize = Math.Max(gap, 1e-12);
            Func<Vector3d, Vector3i> cellOf = (p) => new Vector3i((int)Math.Floor(p.x / cellSize), (int)Math.Floor(p.y / cellSize), (int)Math.Floor(p.z / cellSize));
            var cells = new Dictionary<Vector3i, List<int>>();
            for (int e = 0; e < 2 * n; ++e) {
                if (open(e / 2) == false) continue;
                Vector3i c = cellOf(endPoint(e));
                List<int> l;
                if (cells.TryGetValue(c, out l) == false) cells[c] = l = new List<int>();
                l.Add(e);
            }
            var pairs = new List<KeyValuePair<double, Index2i>>();
            for (int e = 0; e < 2 * n; ++e) {
                if (open(e / 2) == false) continue;
                Vector3d p = endPoint(e);
                Vector3i c = cellOf(p);
                for (int dz = -1; dz <= 1; ++dz)
                    for (int dy = -1; dy <= 1; ++dy)
                        for (int dx = -1; dx <= 1; ++dx) {
                            List<int> l;
                            if (cells.TryGetValue(new Vector3i(c.x + dx, c.y + dy, c.z + dz), out l) == false) continue;
                            foreach (int f in l) {
                                if (f <= e) continue;
                                double d = p.Distance(endPoint(f));
                                if (d < gap) pairs.Add(new KeyValuePair<double, Index2i>(d, new Index2i(e, f)));
                            }
                        }
            }
            pairs.Sort((x, y) => {
                int c = x.Key.CompareTo(y.Key);
                if (c != 0) return c;
                c = x.Value.a.CompareTo(y.Value.a);
                return (c != 0) ? c : x.Value.b.CompareTo(y.Value.b);
            });
            var joined = new int[2 * n];
            for (int e = 0; e < 2 * n; ++e) joined[e] = -1;
            // union-find of the joined lines, to tell a join of two chains from closing one
            var parent = new int[n];
            for (int i = 0; i < n; ++i) parent[i] = i;
            Func<int, int> find = null;
            find = (i) => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            var closedChain = new bool[n];
            foreach (var kv in pairs) {
                int e = kv.Value.a, f = kv.Value.b;
                if (joined[e] >= 0 || joined[f] >= 0) continue;
                int re = find(e / 2), rf = find(f / 2);
                if (closedChain[re] || closedChain[rf]) continue;
                joined[e] = f; joined[f] = e;
                if (re == rf) closedChain[re] = true;       // the two free ends of one chain: a ring
                else parent[rf] = re;
            }
            // assemble: chains from a free end, then the rings
            var result = new List<List<Vector3d>>();
            var visited = new bool[n];
            Action<int, bool> walk = (startEnd, ring) => {
                var line = new List<Vector3d>();
                int e = startEnd;
                while (true) {
                    int i = e / 2;
                    visited[i] = true;
                    var l = lines[i];
                    if (e % 2 == 0) line.AddRange(l);
                    else for (int k = l.Count - 1; k >= 0; --k) line.Add(l[k]);
                    int exit = e ^ 1, next = joined[exit];
                    if (next < 0 || (ring && next == startEnd)) break;
                    e = next;
                }
                if (ring) line.Add(line[0]);
                result.Add(line);
            };
            for (int i = 0; i < n; ++i) {
                if (visited[i]) continue;
                if (open(i) == false) { visited[i] = true; result.Add(lines[i]); continue; }
                if (joined[2 * i] < 0) walk(2 * i, false);
                else if (joined[2 * i + 1] < 0) walk(2 * i + 1, false);
            }
            for (int i = 0; i < n; ++i)
                if (visited[i] == false) walk(2 * i, true);
            lines.Clear();
            lines.AddRange(result);
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
    /// on the terrain; lines on both sides of the sources are returned. Polylines are open: repeat the
    /// first point to close a ring. Sources outside the terrain give no lines there.
    ///
    ///   - Horizontal (default): the distance measured horizontally, as safety distances from a crest
    ///     usually are. The edges near the sources are split (the surface stays the same, coarse survey
    ///     TINs are fine) and the line points found by bisection on them, so they lie exactly at the
    ///     distance; between them, around convex corners and line ends, the chords of the arcs lie at
    ///     most Tolerance inside it.
    ///   - AlongSurface: the geodesic distance along the terrain (heat method), on the band within
    ///     3 x SafetyDistance of the sources, cut out and remeshed to EdgeLength (default SafetyDistance / 8)
    ///     with its boundary and break lines kept; RemeshedTerrain and Geodesic.Distance give the distance
    ///     field there. The heat method overestimates the distance next to break lines (by up to about 0.27 x
    ///     EdgeLength in tests with 75 degree faces), which would put the line too close to the crest:
    ///     the line is drawn at SafetyDistance + SafetyMargin (default 0.35 x EdgeLength). Not guaranteed:
    ///     on real crests (open pit, 1 m DGM) the heat method still overestimates by up to 1-2 m in places,
    ///     which puts the line that much too close.
    ///   - Guaranteed: the straight 3D distance to the sources, which is never more than the distance
    ///     along the terrain: the line lies on the safe side for certain (computed like Horizontal, at
    ///     SafetyDistance + Tolerance, so the chords between the line points stay outside SafetyDistance).
    ///     It is conservative where the path along the terrain bends over another break line or the
    ///     straight line crosses a gap. How much is certified: shortest paths along the refined terrain
    ///     (Dijkstra over its vertices and SteinerPoints points per edge, straight through each triangle)
    ///     are upper bounds of the distance along the terrain; Excess gives, per line point, the upper
    ///     bound minus SafetyDistance, ie at most how far beyond SafetyDistance the point lies. The whole
    ///     segments are bounded (samples every ExcessTolerance / 8, the distance is 1-Lipschitz); while not
    ///     certified, the bound is computed again with twice the points per edge (up to MaxSteinerPoints). Certified if the lines lie at least SafetyDistance (MinLineDistance, exact)
    ///     and at most ExcessTolerance (default 0.2 m) beyond it; RequireCertified makes Compute fail otherwise.
    ///     On the open pit test data: median excess 5-18 cm, 3% (5 m) to 44% (20 m) of the points above 20 cm.
    ///     An empty certificate: with sources off the terrain, or a distance beyond the terrain, Compute returns
    ///     true, also with RequireCertified, with Certified true, no lines and MinLineDistance double.MaxValue. It
    ///     means that no line exists and nothing was certified: check Lines.
    ///
    /// NoData and outliers: an edge near the sources (within the reach of the mode) with an end at NoDataValue, or
    /// steeper than MaxTerrainSlope, makes Compute fail with Status NoDataVertex or OutlierVertex and the vertices in
    /// OutlierVertices; the refinement does not start. A single vertex at z = -9999 (a nodata value) next to a source
    /// refined until memory ran out (1.8 million triangles for a spike of 1000 m on a 1 m grid, 34 times that at
    /// 10 km); remove or fix such vertices, or raise MaxTerrainSlope for real walls.
    ///
    /// Limits: Compute fails (Status TooLarge) above MaxCertifySteps, MaxSourcePoints and MaxRefinedTriangles. The last
    /// one is an estimate: the horizontal area of the band times the ratio of 3D to horizontal area of the terrain
    /// triangles near the sources, and it is no bound. On a 1 m grid the real count was 4.8 times of it on flat terrain,
    /// 4.6 times on a plane of slope 1 and 9.3 times at slope 20 (before the 3D ratio: 6.6 and 187 times; the splits at
    /// midpoints refine edges to the next power of two). Spikes make needles and far more (95 times for 1000 m): there the
    /// outlier check is the guard.
    /// It also does not know where the sources leave the terrain, and counts sources on top of each other twice.
    /// The limits do not cover slivers narrower than an edge.
    /// An exception (eg out of memory, or SteinerPoints too large for an array) leaves Certified false, but Lines,
    /// RefinedTerrain, MinLineDistance and UsedSteinerPoints may stay as they were at that point.
    /// </summary>
    public class TerrainSafetyLines
    {
        public enum Modes { Horizontal, AlongSurface, Guaranteed }
        public Modes Mode = Modes.Horizontal;

        /// <summary> why Compute returned false (Success if it returned true) </summary>
        public enum Statuses
        {
            Success,
            /// <summary> a parameter, the terrain or a source is invalid (see Compute) </summary>
            InvalidInput,
            /// <summary> a vertex of an edge near the sources has z = NoDataValue (OutlierVertices) </summary>
            NoDataVertex,
            /// <summary> an edge near the sources is steeper than MaxTerrainSlope (OutlierVertices) </summary>
            OutlierVertex,
            /// <summary> more steps or triangles than MaxCertifySteps, MaxSourcePoints or MaxRefinedTriangles </summary>
            TooLarge,
            /// <summary> Guaranteed with RequireCertified: a line point is not certified </summary>
            NotCertified,
            /// <summary> AlongSurface: the geodesic distance failed; also after an exception in Compute </summary>
            Failed
        }
        public Statuses Status;
        /// <summary> NoDataVertex, OutlierVertex: the vertices found (ids of Terrain), sorted </summary>
        public List<int> OutlierVertices = new List<int>();

        /// <summary> a vertex with exactly this z is a nodata value (eg -9999); NaN (default): none </summary>
        public double NoDataValue = double.NaN;
        /// <summary>
        /// an edge near the sources steeper than this (rise / run; default 100, 89.4 degrees) marks an outlier; its end
        /// with the larger height difference to its other neighbours is reported. +infinity: no check
        /// </summary>
        public double MaxTerrainSlope = 100;

        public DMesh3 Terrain;
        /// <summary> source points (on the terrain); polylines are densified </summary>
        public List<Vector3d> SourcePoints = new List<Vector3d>();
        public double SafetyDistance;
        /// <summary> AlongSurface: edge length for the distance computation; 0 = SafetyDistance / 8 </summary>
        public double EdgeLength = 0;
        /// <summary> AlongSurface: the line is drawn at SafetyDistance + SafetyMargin; negative = 0.35 x EdgeLength; not finite: Compute fails </summary>
        public double SafetyMargin = -1;
        /// <summary> AlongSurface: break lines bending more than this (degrees) are kept by the remeshing </summary>
        public double FeatureAngle = 20;
        /// <summary>
        /// Horizontal: the line lies at most this much inside the distance (chords of arcs; +infinity: no limit,
        /// only the edge length limit of SafetyDistance); Guaranteed: the line is drawn this much outside
        /// </summary>
        public double Tolerance = 0.01;
        /// <summary>
        /// Guaranteed: line points farther than this beyond SafetyDistance (upper bound) are not certified;
        /// +infinity: no upper limit, only the lower bound (MinLineDistance) is certified
        /// </summary>
        public double ExcessTolerance = 0.2;
        /// <summary> Guaranteed: Compute fails if a line point is not certified </summary>
        public bool RequireCertified = false;
        /// <summary>
        /// Guaranteed: points per edge for the upper bound (more = tighter bound, slower); doubled up to
        /// MaxSteinerPoints while not certified
        /// </summary>
        public int SteinerPoints = 3, MaxSteinerPoints = 12;

        // Limits for finite values that would need more steps or memory than any sensible use: above them Compute
        // fails. They are counts, so the decision does not depend on the units. Counted in double before any work.

        /// <summary>
        /// Guaranteed: most steps per pass of the upper bound, along the lines (1 per ExcessTolerance / 8) or along
        /// the sources (4 per edge length). 1e9 samples are 125 km of lines at ExcessTolerance = 1 mm, against 0.2 m
        /// by default, and hours of computing.
        /// </summary>
        public const double MaxCertifySteps = 1e9;
        /// <summary>
        /// AlongSurface: most points of the densified source lines (kept in memory, 24 bytes each, 2 per
        /// EdgeLength). 1e7 are 600 km of sources at the default EdgeLength of SafetyDistance / 8 for a distance of 1 m.
        /// </summary>
        public const double MaxSourcePoints = 1e7;
        /// <summary>
        /// Most triangles expected of refining the terrain near the sources (2 x area of the band, within the area of
        /// the terrain, / edge length^2; AlongSurface: also of the remeshing), which take in the order of 130 bytes
        /// each. 1e8 are 120 km of sparse sources at the default EdgeLength for a distance of 1 m, or 2000 km at the
        /// default Tolerance, by the estimate, and less with vertices close together (47 km and 778 km at 1 m apart):
        /// see the class comment and expected_refined_triangles.
        /// </summary>
        public const double MaxRefinedTriangles = 1e8;

        // outputs
        public List<List<Vector3d>> Lines = new List<List<Vector3d>>();
        /// <summary> Horizontal: the terrain with its edges near the sources split (same surface) </summary>
        public DMesh3 RefinedTerrain;
        public DMesh3 RemeshedTerrain;
        public MeshGeodesicDistance Geodesic;
        /// <summary>
        /// Guaranteed: per line point (same structure as Lines), an upper bound of how far beyond
        /// SafetyDistance it lies along the terrain; +infinity if more than 2 x ExcessTolerance + edge length
        /// </summary>
        public List<List<double>> Excess = new List<List<double>>();
        /// <summary>
        /// Guaranteed: upper bound of how far beyond SafetyDistance any point of the lines lies (between
        /// the line points too), and the number of line segments where it exceeds ExcessTolerance
        /// </summary>
        public double MaxExcess;
        public int UncertifiedSegments;
        /// <summary> Guaranteed: the smallest straight 3D distance of the lines to the sources (exact, segments) </summary>
        public double MinLineDistance;
        /// <summary>
        /// Guaranteed: the whole lines lie at least SafetyDistance from the sources and at most
        /// ExcessTolerance beyond it along the terrain
        /// </summary>
        public bool Certified;
        /// <summary> Guaranteed: points per edge used for the bound </summary>
        public int UsedSteinerPoints;

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

        /// <summary>
        /// false (with no lines, no certificate, no outputs of an earlier Compute) if SafetyDistance is not
        /// positive and finite, the terrain is empty or a vertex of a triangle is not finite, a source point is not finite or a source segment is too
        /// long for its length to be finite, or the parameters of the mode are invalid: Tolerance must be positive
        /// (Horizontal; may be +infinity) or positive and finite (Guaranteed), ExcessTolerance (Guaranteed) must
        /// be positive (may be +infinity), EdgeLength and SafetyMargin (AlongSurface) must be finite. Also false if
        /// finite values need too much: see MaxCertifySteps, MaxSourcePoints and MaxRefinedTriangles.
        /// </summary>
        public bool Compute()
        {
            clear_outputs();
            failStatus = Statuses.InvalidInput;
            // (an exception leaves Failed, not the status of an earlier Compute)
            Status = Statuses.Failed;
            bool ok = compute();
            Status = ok ? Statuses.Success : failStatus;
            return ok;
        }
        Statuses failStatus;

        bool compute()
        {
            if (positive_finite(SafetyDistance) == false || Terrain.TriangleCount == 0 || terrain_finite() == false || sources_finite() == false
                || !(MaxTerrainSlope > 0))
                return false;
            if (Mode == Modes.Guaranteed)
                return compute_guaranteed();
            return (Mode == Modes.Horizontal) ? compute_horizontal() : compute_along_surface();
        }

        void clear_outputs()
        {
            Lines.Clear(); Excess.Clear(); OutlierVertices.Clear();
            segments = null;
            MaxExcess = 0; UncertifiedSegments = 0; MinLineDistance = 0; Certified = false; UsedSteinerPoints = 0;
            RefinedTerrain = null; RemeshedTerrain = null; Geodesic = null;
        }

        // steps along the source segments at this spacing, as the densifying loops take them (in double: the casts
        // to int of these loops overflow, with different results on the runtimes)
        double source_steps(double spacing)
        {
            double steps = 0;
            if (polyLines != null)
                foreach (var line in polyLines)
                    for (int i = 0; i + 1 < line.Count; ++i)
                        steps += Math.Max(1, Math.Ceiling(line[i].Distance(line[i + 1]) / spacing));
            return steps;
        }

        // Triangles expected of refining the band within reach of the sources down to this edge length, in one pass
        // over the sources: a strip along the source segments, and discs around their ends and the points, one per
        // occupied cell of size reach, as points that lie together share their disc. In units of edge^2: the
        // squares can leave the range of double where the ratios do not. It still misses: the band counted twice
        // where source segments run together or across each other (too high, so a refusal of many segments on top
        // of each other is possible); the parts of the band off the terrain (too high, but never above the
        // terrain); the terrain being steeper than the horizontal band, and the grading of the splits around
        // it (too low: see the class comment); a little dependence of the cells on the frame.
        internal double expected_refined_triangles(double reach, double edge)
        {
            double perEdge2 = 0;
            foreach (int tid in Terrain.TriangleIndices()) {
                double area = Terrain.GetTriArea(tid);
                if (area > 0) perEdge2 += area / edge / edge;   // (no NaN from coordinates that overflow it)
            }
            var cells = new HashSet<Vector2d>();
            Action<Vector3d> occupy = (p) => cells.Add(new Vector2d(Math.Floor(p.x / reach), Math.Floor(p.y / reach)));
            foreach (Vector3d p in SourcePoints)
                occupy(p);
            double length = 0;
            if (polyLines != null)
                foreach (var line in polyLines)
                    for (int i = 0; i < line.Count; ++i) {
                        occupy(line[i]);
                        if (i + 1 < line.Count)
                            length += line[i].Distance(line[i + 1]);
                    }
            double r = reach / edge;
            double band = 2 * r * (length / edge) + Math.PI * r * r * cells.Count;
            return 2 * Math.Min(perEdge2, band * area_ratio_near_sources(reach));
        }

        // 3D area / horizontal area of the terrain triangles that come within reach of the sources (horizontally),
        // at least 1: the refinement splits by 3D edge length, so steep terrain needs that many more triangles
        double area_ratio_near_sources(double reach)
        {
            if (segments == null)
                build_segments();
            if (segments.Count == 0)
                return 1;
            double area3 = 0, areaXY = 0;
            foreach (int tid in Terrain.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                Terrain.GetTriVertices(tid, ref a, ref b, ref c);
                Vector2d m = (a.xy + b.xy + c.xy) / 3;
                double rad = Math.Max(m.Distance(a.xy), Math.Max(m.Distance(b.xy), m.Distance(c.xy)));
                if (horizontal_distance(m, reach + rad) > reach + rad) continue;
                double axy = 0.5 * Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                area3 += Terrain.GetTriArea(tid);
                areaXY += axy;
            }
            double ratio = area3 / areaXY;
            // (no triangle near: 0 / 0; vertical triangles only: infinity, refused)
            return double.IsNaN(ratio) ? 1 : Math.Max(1, ratio);
        }

        bool refinement_too_large(double reach, double edge)
        {
            if (expected_refined_triangles(reach, edge) <= MaxRefinedTriangles)
                return false;
            failStatus = Statuses.TooLarge;
            return true;
        }

        // NoData and outlier vertices on the edges within reach of the sources (horizontally); fails with their status
        bool has_outliers(double reach)
        {
            if (segments == null)
                build_segments();
            var noData = new SortedSet<int>();
            var outliers = new SortedSet<int>();
            foreach (int eid in Terrain.EdgeIndices()) {
                Index2i ev = Terrain.GetEdgeV(eid);
                Vector3d a = Terrain.GetVertex(ev.a), b = Terrain.GetVertex(ev.b);
                double r = reach + 0.5 * a.xy.Distance(b.xy);
                if (horizontal_distance(0.5 * (a.xy + b.xy), r) > r) continue;
                if (a.z == NoDataValue) noData.Add(ev.a);
                if (b.z == NoDataValue) noData.Add(ev.b);
                if (Math.Abs(a.z - b.z) > MaxTerrainSlope * a.xy.Distance(b.xy))
                    outliers.Add(spikier(ev.a, ev.b));
            }
            if (noData.Count > 0) {
                OutlierVertices.AddRange(noData);
                failStatus = Statuses.NoDataVertex;
                return true;
            }
            if (outliers.Count > 0) {
                OutlierVertices.AddRange(outliers);
                failStatus = Statuses.OutlierVertex;
                return true;
            }
            return false;
        }

        // of the two ends of a steep edge, the one farther in height from the median of its neighbours
        int spikier(int va, int vb)
        {
            Func<int, double> dev = (v) => {
                var zs = new List<double>();
                foreach (int nb in Terrain.VtxVerticesItr(v)) zs.Add(Terrain.GetVertex(nb).z);
                zs.Sort();
                double med = zs.Count == 0 ? 0 : zs[zs.Count / 2];
                return Math.Abs(Terrain.GetVertex(v).z - med);
            };
            double da = dev(va), db = dev(vb);
            return (da > db || (da == db && va < vb)) ? va : vb;
        }

        static bool positive_finite(double x)
        {
            return x > 0 && double.IsInfinity(x) == false;
        }

        // (the splits near a vertex that is not finite would never end: an edge to it never gets short)
        bool terrain_finite()
        {
            // (vertices without triangles do not count: they are not part of the surface)
            foreach (int tid in Terrain.TriangleIndices()) {
                Index3i t = Terrain.GetTriangle(tid);
                for (int j = 0; j < 3; ++j) {
                    Vector3d p = Terrain.GetVertex(t[j]);
                    if (MathUtil.IsFinite(p.x) == false || MathUtil.IsFinite(p.y) == false || MathUtil.IsFinite(p.z) == false) return false;
                }
            }
            return true;
        }

        // per coordinate (Vector3d.IsFinite tests their sum, which overflows). Segments: an infinite length
        // would need about 2^31 densification steps, and their positions would be NaN.
        bool sources_finite()
        {
            Func<Vector3d, bool> finite = (p) => MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z);
            foreach (Vector3d p in SourcePoints)
                if (finite(p) == false) return false;
            if (polyLines != null)
                foreach (var line in polyLines)
                    for (int i = 0; i < line.Count; ++i) {
                        if (finite(line[i]) == false) return false;
                        if (i > 0 && MathUtil.IsFinite(line[i - 1].Distance(line[i])) == false) return false;
                    }
            return true;
        }


        // horizontal distance to the source segments (single points are degenerate segments)
        List<Segment2d> segments;
        List<Segment3d> segments3;
        PointHashGrid2d<int> segmentGrid;
        double maxSegLength;

        void build_segments()
        {
            segments = new List<Segment2d>();
            segments3 = new List<Segment3d>();
            var lines = new List<List<Vector3d>>();
            if (polyLines != null) lines.AddRange(polyLines);
            foreach (Vector3d p in SourcePoints) lines.Add(new List<Vector3d>() { p });
            maxSegLength = 0;
            foreach (var line in lines) {
                if (line.Count == 1) { segments.Add(new Segment2d(line[0].xy, line[0].xy)); segments3.Add(new Segment3d(line[0], line[0])); }
                for (int i = 0; i + 1 < line.Count; ++i) {
                    segments.Add(new Segment2d(line[i].xy, line[i + 1].xy));
                    segments3.Add(new Segment3d(line[i], line[i + 1]));
                    maxSegLength = Math.Max(maxSegLength, line[i].xy.Distance(line[i + 1].xy));
                }
            }
            segmentGrid = new PointHashGrid2d<int>(SafetyDistance + 0.5 * maxSegLength + MathUtil.Epsilon, -1);
            for (int k = 0; k < segments.Count; ++k)
                segmentGrid.InsertPointUnsafe(k, segments[k].Center);
        }

        // horizontal distance to the nearest source segment if it is at most radius, else MaxValue
        double horizontal_distance(Vector2d q, double radius)
        {
            // segments are binned by their midpoint, their points are within half their length of it
            var near = segmentGrid.FindNearestInRadius(q, radius + 0.5 * maxSegLength + MathUtil.Epsilon,
                (k) => Math.Sqrt(segments[k].DistanceSquared(q)));
            return (near.Key >= 0 && near.Value <= radius) ? near.Value : double.MaxValue;
        }

        // straight 3D distance to the nearest source segment if it is at most radius, else MaxValue (at
        // least the horizontal one, so the segments are found as in horizontal_distance)
        double distance_3d(Vector3d q, double radius)
        {
            var near = segmentGrid.FindNearestInRadius(q.xy, radius + 0.5 * maxSegLength + MathUtil.Epsilon,
                (k) => Math.Sqrt(segments3[k].DistanceSquared(q)));
            return (near.Key >= 0 && near.Value <= radius) ? near.Value : double.MaxValue;
        }

        // Split the edges that come within reach (horizontally) of the sources until they are at most
        // maxLength long. Splits at edge midpoints do not change the surface.
        DMesh3 refine_near_sources(double maxLength, double reach)
        {
            DMesh3 mesh = new DMesh3(Terrain);
            var queue = new Queue<int>(mesh.EdgeIndices());
            while (queue.Count > 0) {
                int eid = queue.Dequeue();
                if (mesh.IsEdge(eid) == false) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                Vector3d a = mesh.GetVertex(ev.a), b = mesh.GetVertex(ev.b);
                double len = a.Distance(b);
                if (len <= maxLength) continue;
                // lower bound of the edge's distance: midpoint distance minus half the length
                double r = reach + 0.5 * a.xy.Distance(b.xy);
                if (horizontal_distance(0.5 * (a.xy + b.xy), r) > r) continue;
                DMesh3.EdgeSplitInfo split;
                if (mesh.SplitEdge(eid, out split) != MeshResult.Ok) continue;
                queue.Enqueue(eid); queue.Enqueue(split.eNewBN); queue.Enqueue(split.eNewCN);
                if (split.eNewDN >= 0) queue.Enqueue(split.eNewDN);
            }
            return mesh;
        }

        bool compute_horizontal()
        {
            Lines.Clear();
            build_segments();
            if (segments.Count == 0 || !(Tolerance > 0))
                return false;
            // the line points are exact on the edges; between them a chord of an arc of radius
            // SafetyDistance (around convex corners and line ends) lies at most len^2 / (8 SafetyDistance)
            // inside it: edges near the sources are split down to the length that keeps this below Tolerance
            double maxLength = Math.Min(Math.Sqrt(8 * SafetyDistance * Tolerance), SafetyDistance);
            if (has_outliers(SafetyDistance) || refinement_too_large(SafetyDistance, maxLength))
                return false;
            RefinedTerrain = refine_near_sources(maxLength, SafetyDistance);
            double D = SafetyDistance;
            var iso = new MeshIsoCurves(RefinedTerrain, (p) => Math.Min(horizontal_distance(p.xy, 2 * D), 2 * D) - D) {
                RootMode = MeshIsoCurves.RootfindingModes.Bisection, RootModeSteps = 40
            };
            iso.Compute();
            chain_graph(iso.Graph);
            return true;
        }

        bool compute_guaranteed()
        {
            build_segments();
            if (segments.Count == 0 || positive_finite(Tolerance) == false || !(ExcessTolerance > 0))
                return false;
            // as Horizontal, with the 3D distance (a lower bound of the distance along the terrain); the
            // chords between the line points lie at most Tolerance inside the level, so outside SafetyDistance
            double level = SafetyDistance + Tolerance;
            double maxLength = Math.Min(Math.Sqrt(8 * level * Tolerance), level);
            if (has_outliers(level))
                return false;
            if (refinement_too_large(level, maxLength) || !(source_steps(0.25 * maxLength) <= MaxCertifySteps)) {
                failStatus = Statuses.TooLarge;
                return false;
            }
            MinLineDistance = double.MaxValue;
            RefinedTerrain = refine_near_sources(maxLength, level);
            var iso = new MeshIsoCurves(RefinedTerrain, (p) => Math.Min(distance_3d(p, 2 * level), 2 * level) - level) {
                RootMode = MeshIsoCurves.RootfindingModes.Bisection, RootModeSteps = 40
            };
            iso.Compute();
            chain_graph(iso.Graph);
            // (only now are the lines known)
            if (!(certify_samples() <= MaxCertifySteps)) {
                clear_outputs();
                failStatus = Statuses.TooLarge;
                return false;
            }
            // safe side, for the whole line: the smallest 3D distance of its segments to the sources (exact)
            foreach (var line in Lines)
                for (int i = 0; i + 1 < line.Count; ++i)
                    MinLineDistance = Math.Min(MinLineDistance, segment_distance_3d(line[i], line[i + 1], 2 * level));
            // excess: upper bounds of the distance along the terrain, for the whole lines. The graph paths
            // zigzag (eg 0.4 m too long on flat ground with 3 points per edge, where the line lies 0.01 m
            // beyond the distance): more points per edge while not certified
            for (int S = Math.Max(1, SteinerPoints); ; S *= 2) {
                certify(RefinedTerrain, maxLength, S);
                if (Certified || 2 * S > MaxSteinerPoints) break;
            }
            Certified = Certified && MinLineDistance >= SafetyDistance;
            if (Certified == false && RequireCertified)
                failStatus = Statuses.NotCertified;
            return Certified || RequireCertified == false;
        }

        // samples of one pass of certify along the lines
        double certify_samples()
        {
            double h = ExcessTolerance / 8, samples = 0;
            foreach (var line in Lines)
                for (int i = 0; i + 1 < line.Count; ++i)
                    samples += Math.Max(1, Math.Ceiling(line[i].Distance(line[i + 1]) / h));
            return samples;
        }

        // smallest 3D distance of the segment pq to the source segments (if below radius, else MaxValue)
        double segment_distance_3d(Vector3d p, Vector3d q, double radius)
        {
            Vector3d m = 0.5 * (p + q);
            double half = 0.5 * p.Distance(q);
            var near = segmentGrid.FindNearestInRadius(m.xy, radius + half + 0.5 * maxSegLength + MathUtil.Epsilon,
                (k) => segment_segment_distance(p, q, segments3[k].P0, segments3[k].P1));
            return (near.Key >= 0) ? near.Value : double.MaxValue;
        }

        // distance between the segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection 5.1.9)
        static double segment_segment_distance(Vector3d p1, Vector3d q1, Vector3d p2, Vector3d q2)
        {
            Vector3d d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
            double s, t;
            if (a <= 1e-300 && e <= 1e-300) return p1.Distance(p2);
            if (a <= 1e-300) {
                s = 0; t = MathUtil.Clamp(f / e, 0, 1);
            } else {
                double c = d1.Dot(r);
                if (e <= 1e-300) {
                    t = 0; s = MathUtil.Clamp(-c / a, 0, 1);
                } else {
                    double b = d1.Dot(d2), denom = a * e - b * b;
                    s = (denom > 0) ? MathUtil.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = MathUtil.Clamp(-c / a, 0, 1); }
                    else if (t > 1) { t = 1; s = MathUtil.Clamp((b - c) / a, 0, 1); }
                }
            }
            return (p1 + s * d1).Distance(p2 + t * d2);
        }

        // Upper bounds of the distance along the mesh (the refined terrain, same surface): Dijkstra over the
        // vertices and SteinerPoints points on each edge, linked straight through each triangle (paths on
        // the surface), from the densified sources (straight to the nodes of the triangles they lie in).
        // Every line segment lies in one triangle; it is sampled every ExcessTolerance / 8, and between
        // samples a and b (distance h) the bound is (U(a) + U(b) + h) / 2 (the distance is 1-Lipschitz along
        // the surface), so the whole segment is certified.
        void certify(DMesh3 m, double maxLength, int S)
        {
            // (Certified is set at the end: the allocations below can throw, see S26 in ROBUSTNESS_SWEEP.md)
            bool certified = true;
            Excess.Clear(); MaxExcess = 0; UncertifiedSegments = 0;
            UsedSteinerPoints = S;
            int V = m.MaxVertexID;
            int N = V + m.MaxEdgeID * S;
            double cutoff = SafetyDistance + 2 * ExcessTolerance + maxLength;
            var U = new double[N];
            for (int k = 0; k < N; ++k) U[k] = double.PositiveInfinity;
            Func<int, Vector3d> pos = (id) => {
                if (id < V) return m.GetVertex(id);
                int e = (id - V) / S, k = (id - V) % S;
                Index2i ev = m.GetEdgeV(e);
                return Vector3d.Lerp(m.GetVertex(ev.a), m.GetVertex(ev.b), (k + 1.0) / (S + 1));
            };
            var triNodes = new List<int>(3 + 3 * S);
            Action<int> nodes_of = (tid) => {
                triNodes.Clear();
                Index3i tv = m.GetTriangle(tid), te = m.GetTriEdges(tid);
                triNodes.Add(tv.a); triNodes.Add(tv.b); triNodes.Add(tv.c);
                for (int j = 0; j < 3; ++j)
                    for (int k = 0; k < S; ++k) triNodes.Add(V + te[j] * S + k);
            };
            var spatial = new DMeshAABBTree3(m, true);
            var pq = new MinHeap();
            // sources: the densified source lines, projected onto the terrain (+ their distance to it)
            var lines = new List<List<Vector3d>>();
            if (polyLines != null) lines.AddRange(polyLines);
            foreach (Vector3d p in SourcePoints) lines.Add(new List<Vector3d>() { p });
            foreach (var line in lines)
                for (int i = 0; i < line.Count; ++i) {
                    int steps = (i + 1 < line.Count) ? Math.Max(1, (int)Math.Ceiling(line[i].Distance(line[i + 1]) / (0.25 * maxLength))) : 1;
                    for (int k = 0; k < steps; ++k) {
                        Vector3d s = (i + 1 < line.Count) ? Vector3d.Lerp(line[i], line[i + 1], (double)k / steps) : line[i];
                        int tid = spatial.FindNearestTriangle(s, maxLength);
                        if (tid < 0) continue;
                        Vector3d sp = MeshQueries.TriangleDistance(m, tid, s).TriangleClosest;
                        double off = s.Distance(sp);
                        nodes_of(tid);
                        foreach (int q in triNodes) {
                            double d = off + sp.Distance(pos(q));
                            if (d < U[q]) { U[q] = d; pq.Enqueue(q, d); }
                        }
                    }
                }
            var done = new bool[N];
            var incident = new List<int>();
            while (pq.Count > 0) {
                int id; double d;
                pq.TryDequeue(out id, out d);
                if (done[id] || d > U[id]) continue;
                done[id] = true;
                if (d > cutoff) break;
                incident.Clear();
                if (id < V) {
                    foreach (int t in m.VtxTrianglesItr(id)) incident.Add(t);
                } else {
                    Index2i et = m.GetEdgeT((id - V) / S);
                    incident.Add(et.a);
                    if (et.b >= 0) incident.Add(et.b);
                }
                Vector3d p = pos(id);
                foreach (int t in incident) {
                    nodes_of(t);
                    foreach (int q in triNodes) {
                        if (done[q]) continue;
                        double nd = d + p.Distance(pos(q));
                        if (nd < U[q]) { U[q] = nd; pq.Enqueue(q, nd); }
                    }
                }
            }
            // a point on the surface: through the triangle it lies in (and the one across the edge it lies on)
            Func<Vector3d, double> bound = (p) => {
                double best = double.PositiveInfinity;
                int tid = spatial.FindNearestTriangle(p);
                if (tid < 0) return best;
                incident.Clear();
                incident.Add(tid);
                Index3i te = m.GetTriEdges(tid);
                for (int j = 0; j < 3; ++j) {
                    Index2i ev = m.GetEdgeV(te[j]);
                    if (new Segment3d(m.GetVertex(ev.a), m.GetVertex(ev.b)).DistanceSquared(p) < 1e-18 * (1 + p.LengthSquared)) {
                        Index2i et = m.GetEdgeT(te[j]);
                        int other = (et.a == tid) ? et.b : et.a;
                        if (other >= 0) incident.Add(other);
                    }
                }
                foreach (int t in incident.ToArray()) {
                    nodes_of(t);
                    foreach (int q in triNodes)
                        if (done[q]) best = Math.Min(best, U[q] + p.Distance(pos(q)));
                }
                return (best <= cutoff) ? best : double.PositiveInfinity;
            };
            double h = ExcessTolerance / 8;
            foreach (var line in Lines) {
                var ex = new List<double>(line.Count);
                double prev = bound(line[0]);
                ex.Add(prev - SafetyDistance);
                for (int i = 0; i + 1 < line.Count; ++i) {
                    int n = Math.Max(1, (int)Math.Ceiling(line[i].Distance(line[i + 1]) / h));
                    double step = line[i].Distance(line[i + 1]) / n;
                    double segMax = 0;
                    for (int k = 1; k <= n; ++k) {
                        double next = bound(Vector3d.Lerp(line[i], line[i + 1], (double)k / n));
                        segMax = Math.Max(segMax, 0.5 * (prev + next + step) - SafetyDistance);
                        prev = next;
                    }
                    ex.Add(prev - SafetyDistance);
                    MaxExcess = Math.Max(MaxExcess, segMax);
                    if (!(segMax <= ExcessTolerance)) {
                        UncertifiedSegments++;
                        certified = false;
                    }
                }
                Excess.Add(ex);
            }
            Certified = certified;
        }

        // binary min-heap of (key, id) with lazy deletion (netstandard2.0 has no PriorityQueue)
        class MinHeap
        {
            List<KeyValuePair<double, int>> h = new List<KeyValuePair<double, int>>();
            public int Count { get { return h.Count; } }
            public void Enqueue(int id, double key)
            {
                h.Add(new KeyValuePair<double, int>(key, id));
                int i = h.Count - 1;
                while (i > 0) {
                    int p = (i - 1) / 2;
                    if (h[p].Key <= h[i].Key) break;
                    var t = h[p]; h[p] = h[i]; h[i] = t; i = p;
                }
            }
            public bool TryDequeue(out int id, out double key)
            {
                id = h[0].Value; key = h[0].Key;
                h[0] = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                int i = 0;
                while (true) {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].Key < h[m].Key) m = l;
                    if (r < h.Count && h[r].Key < h[m].Key) m = r;
                    if (m == i) break;
                    var t = h[m]; h[m] = h[i]; h[i] = t; i = m;
                }
                return true;
            }
        }

        bool compute_along_surface()
        {
            Lines.Clear();
            // (0 and negative mean the default; a NaN would pass as the default too, +infinity in SafetyMargin
            // as a line out of reach: success with no lines)
            if (MathUtil.IsFinite(EdgeLength) == false || MathUtil.IsFinite(SafetyMargin) == false)
                return false;
            double h = (EdgeLength > 0) ? EdgeLength : SafetyDistance / 8;
            if (h <= 0)
                return false;
            if (has_outliers(3 * SafetyDistance + 2 * h))
                return false;
            if (!(source_steps(0.5 * h) <= MaxSourcePoints) || refinement_too_large(3 * SafetyDistance + 2 * h, h)) {
                failStatus = Statuses.TooLarge;
                return false;
            }

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
            // large (survey) triangles near the sources are split first, so the band is not whole triangles
            build_segments();
            DMesh3 refined = refine_near_sources(2 * h, band);
            var grid = new PointHashGrid3d<int>(band, -1);
            for (int i = 0; i < pts.Count; ++i)
                grid.InsertPointUnsafe(i, pts[i]);
            var band_tris = new MeshFaceSelection(refined);
            var seeds = new List<int>();
            foreach (int tid in refined.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                refined.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d m = (a + b + c) / 3;
                double rad = Math.Max(m.Distance(a), Math.Max(m.Distance(b), m.Distance(c)));
                var near = grid.FindNearestInRadius(m, band + rad, (i) => pts[i].Distance(m));
                if (near.Key < 0) continue;
                band_tris.Select(tid);
                if (near.Value <= rad + h) seeds.Add(tid);
            }
            // the band cut out of the triangles can touch itself at vertices (bowties) and leave islands of
            // triangles that are near a source in space but not connected to it on the terrain. The heat
            // method gives arbitrary distances on islands without a source (they then made spurious lines,
            // depending on rounding, eg at UTM coordinates), and the remesher degenerates at bowties: keep
            // the bowtie-free part connected to the sources
            band_tris.RemoveBowties();
            seeds.RemoveAll((tid) => band_tris.IsSelected(tid) == false);
            var keep = new MeshFaceSelection(refined);
            if (seeds.Count > 0)
                keep.FloodFill(seeds.ToArray(), (tid) => band_tris.IsSelected(tid));
            if (keep.Count == 0)
                return true;        // no terrain near the sources
            RemeshedTerrain = new DSubmesh3(refined, keep).SubMesh;
            var remesher = new Remesher(RemeshedTerrain);
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, FeatureAngle);
            remesher.SetTargetEdgeLength(h);
            remesher.SmoothSpeedT = 0.5;
            for (int k = 0; k < 8; ++k)
                remesher.BasicRemeshPass();
            // a last pass without smoothing collapses / flips away triangles the last smoothing made degenerate
            remesher.EnableSmoothing = false;
            remesher.BasicRemeshPass();
            DMesh3 mesh = RemeshedTerrain;

            // sources mapped to the nearest vertices; sources off the terrain are ignored
            var spatial = new DMeshAABBTree3(mesh, true);
            var sources = new HashSet<int>();
            foreach (Vector3d p in pts) {
                int tid = spatial.FindNearestTriangle(p, h);
                if (tid < 0) continue;
                Index3i t = mesh.GetTriangle(tid);
                int best = t.a;
                foreach (int v in new[] { t.b, t.c })
                    if (mesh.GetVertex(v).DistanceSquared(p) < mesh.GetVertex(best).DistanceSquared(p)) best = v;
                sources.Add(best);
            }

            if (sources.Count == 0)
                return true;        // all sources off the terrain
            Geodesic = new MeshGeodesicDistance(mesh) { TimeFactor = 0.25 };
            if (Geodesic.Compute(sources) == false) {
                failStatus = Statuses.Failed;
                return false;
            }
            double[] dist = Geodesic.Distance;
            double level = SafetyDistance + ((SafetyMargin >= 0) ? SafetyMargin : 0.35 * h);
            var iso = new MeshIsoCurves(mesh, (v) => 0) {
                // NaN: not reachable from a source
                VertexValueF = (vid) => double.IsNaN(dist[vid]) ? SafetyDistance : dist[vid] - level,
                RootMode = MeshIsoCurves.RootfindingModes.SingleLerp
            };
            iso.Compute();
            chain_graph(iso.Graph);
            return true;
        }

        // chain the iso segments into polylines: every edge exactly once; lines run between end points
        // and junctions (degree != 2), the rest are closed loops
        void chain_graph(DGraph3 g)
        {
            var used = new HashSet<int>();
            foreach (int v in g.VertexIndices()) {
                if (g.GetVtxEdgeCount(v) == 2) continue;
                foreach (int eid in g.VtxEdgesItr(v))
                    if (used.Contains(eid) == false)
                        add_line(walk(g, v, eid, used));
            }
            foreach (int eid in g.EdgeIndices())
                if (used.Contains(eid) == false)
                    add_line(walk(g, g.GetEdgeV(eid).a, eid, used));
        }

        void add_line(List<Vector3d> line)
        {
            if (line.Count > 1) Lines.Add(line);
        }

        static List<Vector3d> walk(DGraph3 g, int start, int eid, HashSet<int> used)
        {
            var line = new List<Vector3d>() { g.GetVertex(start) };
            int cur = start;
            while (true) {
                used.Add(eid);
                Index2i ev = g.GetEdgeV(eid);
                int next = (ev.a == cur) ? ev.b : ev.a;
                line.Add(g.GetVertex(next));
                if (next == start || g.GetVtxEdgeCount(next) != 2)
                    break;
                int ne = -1;
                foreach (int e in g.VtxEdgesItr(next))
                    if (used.Contains(e) == false) { ne = e; break; }
                if (ne < 0)
                    break;
                cur = next; eid = ne;
            }
            return line;
        }
    }
}
