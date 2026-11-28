// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Thin-wedge preprocessing for 2D constrained / quality meshing (spec O13, F-19).
    /// <para>
    /// A non-locked constraint edge that leaves a host constraint edge at an angle strictly below 10 degrees
    /// (<c>sin^2(theta) &lt; sin^2(10 deg)</c>) and whose far endpoint projects onto the host within
    /// <c>wedgeTolerance</c> is collapsed into the host:
    /// <list type="bullet">
    /// <item><b>Free wedge vertex (Case I):</b> if the far endpoint has no other incident edge leaving the host
    /// at &gt;= 10 degrees or beyond <c>wedgeTolerance</c> (and is not pinned as a host, anchor, or locked endpoint),
    /// it is projected onto the host, collapsing slowly drifting polylines cleanly along the host.</item>
    /// <item><b>Corner / branch vertex (Case II):</b> if another incident edge leaves the host (e.g. a wall turning
    /// upward at 90 degrees or a polyline exiting the tolerance band), the far endpoint stays at its original
    /// coordinate and the wedge edge is rerouted along the host to its orthogonal foot plus a perpendicular
    /// connector to the far endpoint.</item>
    /// </list>
    /// Host edges never move, coinciding input vertices move together as one canonical point, and no vertex or
    /// rerouted edge moves more than <c>wedgeTolerance</c> from its original input position. Uses a static
    /// centroid-split BVH built in strict <c>O(E log E)</c> worst-case time via dual pre-sorted axes and a
    /// deterministic vertex-bounded worklist, preserving exact <c>2^k</c> scale invariance.
    /// </para>
    /// </summary>
    internal static class WedgeCollapse2
    {
        // sin^2(10 degrees) = 0.0301536896070458... (dimensionless -> 2^k scale-invariant)
        const double MaxWedgeSin2 = 0.0301536896070458;
        // Relative collinearity / endpoint tolerance (2^-40)
        const double OnHostRelEps = 1.0 / (1L << 40);

        struct EdgeRec
        {
            public int A, B;          // canonical vertex ids
            public int OwnerKind;     // 0 = segment, 1 = ring, 2 = region ring
            public int OwnerIdx;
            public int LocalEdgeIdx;  // edge index within its ring (0 for segment)
            public int Marker;
            public bool Locked;
            public bool Alive;
            public bool Rerouted;
            public int HostId;        // original edge id in the static host BVH
        }

        struct BvhNode
        {
            public double MinX, MinY, MaxX, MaxY;
            public int Left, Right; // >= 0 internal children; < 0 leaf: start = ~Left, count = Right
        }

        internal static ConstrainedMesh2Input Apply(
            ConstrainedMesh2Input input,
            double wedgeTolerance,
            CancellationToken cancel,
            out int mergedVertices,
            out int reroutedEdges)
        {
            return Apply(input, wedgeTolerance, cancel, out mergedVertices, out reroutedEdges, out _);
        }

        internal static ConstrainedMesh2Input Apply(
            ConstrainedMesh2Input input,
            double wedgeTolerance,
            CancellationToken cancel,
            out int mergedVertices,
            out int reroutedEdges,
            out long bvhVisits)
        {
            mergedVertices = 0;
            reroutedEdges = 0;
            bvhVisits = 0;
            if (wedgeTolerance == 0) return input;
            if (double.IsNaN(wedgeTolerance) || !(wedgeTolerance > 0) || double.IsInfinity(wedgeTolerance))
                throw new ArgumentException("WedgeTolerance must be zero or positive and finite (spec O13)");

            int origN = input.Points?.Count ?? 0;
            if (origN < 3) return input;

            // Normalize coordinates (-0.0 -> +0.0) and canonicalize exact duplicate input points to their lowest index
            // so coinciding vertices share incident edges, locks, pinning, and Case I moves (preserving InputToVertex).
            var origPts = new Vector2d[origN];
            var pts = new List<Vector2d>(origN + 16);
            var canon = new int[origN];
            var aliasHead = new int[origN];
            var aliasNext = new int[origN];
            var footByCoord = new Dictionary<Vector2d, int>(origN);
            for (int i = 0; i < origN; ++i) {
                var p = input.Points[i];
                var norm = new Vector2d(p.x == 0 ? 0.0 : p.x, p.y == 0 ? 0.0 : p.y);
                origPts[i] = norm;
                pts.Add(norm);
                aliasHead[i] = -1;
                aliasNext[i] = -1;
                if (footByCoord.TryGetValue(norm, out int rep)) {
                    canon[i] = rep;
                    aliasNext[i] = aliasHead[rep];
                    aliasHead[rep] = i;
                } else {
                    canon[i] = i;
                    footByCoord.Add(norm, i);
                }
            }
            List<double> heights = input.Heights != null ? new List<double>(input.Heights) : null;

            // Collect all constraint edges from segments, rings, and region rings in deterministic input order.
            var edges = new List<EdgeRec>();
            var ringLists = new List<List<int>>();
            var regRingLists = new List<List<int>>();

            if (input.Segments != null) {
                for (int si = 0; si < input.Segments.Count; ++si) {
                    var s = input.Segments[si];
                    if (s == null) continue;
                    int ca = canon[s.A], cb = canon[s.B];
                    if (ca != cb) {
                        edges.Add(new EdgeRec {
                            A = ca, B = cb, OwnerKind = 0, OwnerIdx = si, LocalEdgeIdx = 0,
                            Marker = s.Marker, Locked = s.Locked, Alive = true, HostId = edges.Count
                        });
                    }
                }
            }

            if (input.Rings != null) {
                for (int ri = 0; ri < input.Rings.Count; ++ri) {
                    var r = input.Rings[ri];
                    if (r == null || r.Vertices == null || r.Vertices.Length < 2) { ringLists.Add(null); continue; }
                    var cycle = NormalizeRing(r.Vertices);
                    ringLists.Add(cycle);
                    if (cycle.Count < 3) continue;
                    for (int k = 0; k < cycle.Count; ++k) {
                        int ca = canon[cycle[k]], cb = canon[cycle[(k + 1) % cycle.Count]];
                        if (ca != cb) {
                            edges.Add(new EdgeRec {
                                A = ca, B = cb, OwnerKind = 1, OwnerIdx = ri, LocalEdgeIdx = k,
                                Marker = r.Marker, Locked = false, Alive = true, HostId = edges.Count
                            });
                        }
                    }
                }
            }

            if (input.Regions != null) {
                for (int gi = 0; gi < input.Regions.Count; ++gi) {
                    var g = input.Regions[gi];
                    if (g == null || g.Ring == null || g.Ring.Length < 2) { regRingLists.Add(null); continue; }
                    var cycle = NormalizeRing(g.Ring);
                    regRingLists.Add(cycle);
                    if (cycle.Count < 3) continue;
                    for (int k = 0; k < cycle.Count; ++k) {
                        int ca = canon[cycle[k]], cb = canon[cycle[(k + 1) % cycle.Count]];
                        if (ca != cb) {
                            edges.Add(new EdgeRec {
                                A = ca, B = cb, OwnerKind = 2, OwnerIdx = gi, LocalEdgeIdx = k,
                                Marker = 0, Locked = false, Alive = true, HostId = edges.Count
                            });
                        }
                    }
                }
            }

            int hostCount = edges.Count;
            if (hostCount < 2) return input;

            // Static snapshots of original host edges (host geometry never moves, spec O13).
            var hostA = new Vector2d[hostCount];
            var hostB = new Vector2d[hostCount];
            var hostVA = new int[hostCount];
            var hostVB = new int[hostCount];
            var hostLocked = new bool[hostCount];
            var hostKind = new int[hostCount];
            var hostLen2Scaled = new (int ex, double len2)[hostCount];
            for (int i = 0; i < hostCount; ++i) {
                hostVA[i] = edges[i].A;
                hostVB[i] = edges[i].B;
                hostA[i] = origPts[edges[i].A];
                hostB[i] = origPts[edges[i].B];
                hostLocked[i] = edges[i].Locked;
                hostKind[i] = edges[i].OwnerKind;
                hostLen2Scaled[i] = ScaledLen2(hostA[i], hostB[i]);
            }

            // Canonical vertex -> incident edge ids, plus pinned/moved status
            var incident = new List<int>[origN];
            var vertexPinned = new bool[origN];
            var vertexMoved = new bool[origN];
            var vertexHasLocked = new bool[origN];
            for (int ei = 0; ei < hostCount; ++ei) {
                int va = edges[ei].A, vb = edges[ei].B;
                if (incident[va] == null) incident[va] = new List<int>(4);
                if (incident[vb] == null) incident[vb] = new List<int>(4);
                incident[va].Add(ei);
                incident[vb].Add(ei);
                if (edges[ei].Locked) {
                    vertexHasLocked[va] = true;
                    vertexHasLocked[vb] = true;
                }
            }

            // Build deterministic static centroid-split BVH in strict O(E log E) time via dual pre-sorted axes.
            var cx = new double[hostCount];
            var cy = new double[hostCount];
            var orderX = new int[hostCount];
            var orderY = new int[hostCount];
            for (int i = 0; i < hostCount; ++i) {
                cx[i] = 0.5 * hostA[i].x + 0.5 * hostB[i].x;
                cy[i] = 0.5 * hostA[i].y + 0.5 * hostB[i].y;
                orderX[i] = i;
                orderY[i] = i;
            }
            Array.Sort(orderX, (i, j) => {
                int c = cx[i].CompareTo(cx[j]);
                return c != 0 ? c : i.CompareTo(j);
            });
            Array.Sort(orderY, (i, j) => {
                int c = cy[i].CompareTo(cy[j]);
                return c != 0 ? c : i.CompareTo(j);
            });
            var bvhLeafIds = new int[hostCount];
            var bvhSide = new bool[hostCount];
            var bvhScratch = new int[hostCount];
            var bvh = new List<BvhNode>(2 * hostCount);
            int leafCursor = 0;
            BuildBvh(bvh, orderX, orderY, bvhLeafIds, bvhSide, bvhScratch, 0, hostCount, ref leafCursor, hostA, hostB, cx, cy);

            // Track Case II reroutes per (ownerKind, ownerIdx, localEdgeIdx)
            var reroutes = new Dictionary<(int kind, int idx, int localIdx), int>();

            // Deterministic worklist: each edge visited at most 3 times (initial + at most once per endpoint moving)
            var queue = new Queue<int>(hostCount);
            var inQueue = new bool[hostCount];
            for (int ei = 0; ei < hostCount; ++ei) {
                queue.Enqueue(ei);
                inQueue[ei] = true;
            }

            var candidates = new List<int>(32);
            var bvhStack = new Stack<int>(64);
            int processed = 0;

            while (queue.Count > 0) {
                if ((++processed & 255) == 0) cancel.ThrowIfCancellationRequested();
                int ei = queue.Dequeue();
                inQueue[ei] = false;
                var e = edges[ei];
                if (!e.Alive || e.Locked || e.Rerouted) continue;
                Vector2d pa = pts[e.A], pb = pts[e.B];
                if (pa == pb) { e.Alive = false; edges[ei] = e; continue; }

                // Query candidate host segments whose AABB overlaps e's AABB expanded by wedgeTolerance
                double qMinX = Math.Min(pa.x, pb.x) - wedgeTolerance;
                double qMaxX = Math.Max(pa.x, pb.x) + wedgeTolerance;
                double qMinY = Math.Min(pa.y, pb.y) - wedgeTolerance;
                double qMaxY = Math.Max(pa.y, pb.y) + wedgeTolerance;
                candidates.Clear();
                bvhVisits += QueryBvh(bvh, bvhLeafIds, bvhStack, qMinX, qMinY, qMaxX, qMaxY, candidates);
                if (candidates.Count == 0) continue;
                candidates.Sort();

                var eLen2 = ScaledLen2(pa, pb);
                int ePri = HostPriority(e.Locked, e.OwnerKind);

                // Find best host for either orientation (p -> q) of edge e
                int bestHost = -1;
                int bestP = -1, bestQ = -1;
                Vector2d bestFoot = Vector2d.Zero;
                double bestFootT = 0;
                int bestDistEx = int.MaxValue;
                double bestDistScaled2 = double.PositiveInfinity;

                for (int ci = 0; ci < candidates.Count; ++ci) {
                    int hi = candidates[ci];
                    if (hi == e.HostId) continue;
                    // Host cannot have had either of its own endpoints moved by an earlier merge
                    if (vertexMoved[hostVA[hi]] || vertexMoved[hostVB[hi]]) continue;
                    // Host preference: locked > ring/region > free segment; within equal priority, longer > lower HostId.
                    int hPri = HostPriority(hostLocked[hi], hostKind[hi]);
                    if (hPri < ePri) continue;
                    if (hPri == ePri) {
                        int lenCmp = CompareScaledLen2(hostLen2Scaled[hi], eLen2);
                        if (lenCmp < 0 || (lenCmp == 0 && hi > e.HostId)) continue;
                    }

                    Vector2d h0 = hostA[hi], h1 = hostB[hi];
                    for (int dir = 0; dir < 2; ++dir) {
                        int p = dir == 0 ? e.A : e.B;
                        int q = dir == 0 ? e.B : e.A;
                        Vector2d pp = pts[p], pq = pts[q];

                        // 1. p must lie on host segment h (exact endpoint or within 2^-40 * |h| of segment h)
                        if (!PointOnSegment(h0, h1, pp)) continue;

                        // 2. Angle between (p -> q) and host h must be strictly < 10 degrees and non-collinear
                        if (!IsThinWedgeAngle(pp, pq, h0, h1)) continue;

                        // 3. Projection foot of q onto h must lie on h (t in [0, 1]), distance in (0, wedgeTolerance],
                        //    and original coordinate of q must also be within wedgeTolerance of foot (O13 drift guard).
                        if (!ProjectWithinTolerance(h0, h1, pq, wedgeTolerance, out Vector2d foot, out double tFoot, out int dEx, out double dS2))
                            continue;
                        if (foot == pq) continue;
                        if (q < origN && !WithinTolerance(origPts[q], foot, wedgeTolerance)) continue;

                        // Respect locked host segments (O9): do not introduce a new interior vertex on a locked host
                        if (hostLocked[hi] && foot != h0 && foot != h1) continue;

                        // Deterministic best host selection: smallest distance from q to host, tie-break by lowest host id
                        if (bestHost < 0 || CompareScaledVal(dEx, dS2, bestDistEx, bestDistScaled2) < 0) {
                            bestHost = hi;
                            bestP = p;
                            bestQ = q;
                            bestFoot = foot;
                            bestFootT = tFoot;
                            bestDistEx = dEx;
                            bestDistScaled2 = dS2;
                        }
                    }
                }

                if (bestHost < 0) continue;

                Vector2d bh0 = hostA[bestHost], bh1 = hostB[bestHost];

                // Decide Case I (move free wedge vertex q onto host foot) vs Case II (keep q, reroute e via foot)
                bool canMoveQ = bestQ < origN
                    && bestQ != hostVA[bestHost]
                    && bestQ != hostVB[bestHost]
                    && !vertexPinned[bestQ]
                    && !vertexMoved[bestQ]
                    && !vertexHasLocked[bestQ];

                if (canMoveQ) {
                    var inc = incident[bestQ];
                    if (inc != null) {
                        for (int k = 0; k < inc.Count; ++k) {
                            int oe = inc[k];
                            if (oe == ei) continue;
                            var other = edges[oe];
                            if (!other.Alive) continue;
                            if (other.Locked) { canMoveQ = false; break; }
                            int r = other.A == bestQ ? other.B : other.A;
                            Vector2d pr = pts[r];
                            // If another incident edge exits the wedge angle or tolerance band of this host, q is a corner/branch -> Case II
                            if (!StaysInWedgeBand(pts[bestQ], pr, bh0, bh1, wedgeTolerance)) {
                                canMoveQ = false;
                                break;
                            }
                        }
                    }
                }

                if (canMoveQ) {
                    // Case I: project free wedge vertex q (and all coinciding input aliases) onto host foot
                    Vector2d oldCoord = pts[bestQ];
                    Vector2d newCoord = new Vector2d(bestFoot.x == 0 ? 0.0 : bestFoot.x, bestFoot.y == 0 ? 0.0 : bestFoot.y);
                    pts[bestQ] = newCoord;
                    for (int al = aliasHead[bestQ]; al >= 0; al = aliasNext[al])
                        pts[al] = newCoord;
                    if (footByCoord.TryGetValue(oldCoord, out int oldOwner) && oldOwner == bestQ)
                        footByCoord.Remove(oldCoord);
                    if (!footByCoord.ContainsKey(newCoord))
                        footByCoord.Add(newCoord, bestQ);

                    vertexPinned[hostVA[bestHost]] = true;
                    vertexPinned[hostVB[bestHost]] = true;
                    if (bestP < origN) vertexPinned[bestP] = true;
                    vertexMoved[bestQ] = true;
                    ++mergedVertices;

                    // Re-queue other edges incident to bestQ so drifting polyline chains propagate cleanly
                    var inc = incident[bestQ];
                    if (inc != null) {
                        for (int k = 0; k < inc.Count; ++k) {
                            int oe = inc[k];
                            if (oe != ei && edges[oe].Alive && !edges[oe].Rerouted && !inQueue[oe]) {
                                inQueue[oe] = true;
                                queue.Enqueue(oe);
                            }
                        }
                    }
                } else {
                    // Case II: keep q at its original coordinate; reroute e = (bestP, bestQ) as (bestP, foot) + (foot, bestQ)
                    Vector2d fNorm = new Vector2d(bestFoot.x == 0 ? 0.0 : bestFoot.x, bestFoot.y == 0 ? 0.0 : bestFoot.y);
                    int fIdx;
                    if (footByCoord.TryGetValue(fNorm, out fIdx) && pts[fIdx] == fNorm) {
                        // Reuse existing vertex at exact foot coordinate
                    } else {
                        fIdx = pts.Count;
                        pts.Add(fNorm);
                        footByCoord[fNorm] = fIdx;
                        if (heights != null) {
                            double z0 = heights[hostVA[bestHost]];
                            double z1 = heights[hostVB[bestHost]];
                            // Convex combination is overflow-free for all finite z0, z1 and bestFootT in [0, 1]
                            heights.Add(z0 == z1 ? z0 : (1.0 - bestFootT) * z0 + bestFootT * z1);
                        }
                    }
                    int fCanon = fIdx < origN ? canon[fIdx] : fIdx;
                    if (fCanon == bestP || fCanon == bestQ) continue;
                    vertexPinned[hostVA[bestHost]] = true;
                    vertexPinned[hostVB[bestHost]] = true;
                    if (bestP < origN) vertexPinned[bestP] = true;
                    if (bestQ < origN) vertexPinned[bestQ] = true;
                    reroutes[(e.OwnerKind, e.OwnerIdx, e.LocalEdgeIdx)] = fIdx;
                    e.Rerouted = true;
                    edges[ei] = e;
                    ++reroutedEdges;
                }
            }

            if (mergedVertices == 0 && reroutedEdges == 0) return input;

            // Rebuild ConstrainedMesh2Input with updated points, segments, rings, and regions
            var result = new ConstrainedMesh2Input {
                Points = pts,
                Heights = heights,
                HoleSeeds = input.HoleSeeds != null ? new List<Vector2d>(input.HoleSeeds) : new List<Vector2d>()
            };

            if (input.Segments != null) {
                for (int si = 0; si < input.Segments.Count; ++si) {
                    var s = input.Segments[si];
                    if (s == null) continue;
                    if (reroutes.TryGetValue((0, si, 0), out int fIdx)) {
                        result.Segments.Add(new Mesh2Segment(s.A, fIdx, s.Marker, s.Locked));
                        result.Segments.Add(new Mesh2Segment(fIdx, s.B, s.Marker, s.Locked));
                    } else {
                        result.Segments.Add(new Mesh2Segment(s.A, s.B, s.Marker, s.Locked));
                    }
                }
            }

            if (input.Rings != null) {
                for (int ri = 0; ri < input.Rings.Count; ++ri) {
                    var r = input.Rings[ri];
                    if (r == null) continue;
                    var cycle = ringLists[ri];
                    if (cycle == null || cycle.Count < 3) {
                        result.Rings.Add(new Mesh2Ring(r.Vertices != null ? (int[])r.Vertices.Clone() : new int[0], r.Hole, r.Marker));
                        continue;
                    }
                    var expanded = ExpandCycle(cycle, 1, ri, reroutes);
                    result.Rings.Add(new Mesh2Ring(expanded, r.Hole, r.Marker));
                }
            }

            if (input.Regions != null) {
                for (int gi = 0; gi < input.Regions.Count; ++gi) {
                    var g = input.Regions[gi];
                    if (g == null) continue;
                    if (g.Ring == null) {
                        result.Regions.Add(Mesh2Region.BySeed(g.Seed, g.Label, g.MaxArea));
                    } else {
                        var cycle = regRingLists[gi];
                        var expanded = cycle != null && cycle.Count >= 3
                            ? ExpandCycle(cycle, 2, gi, reroutes)
                            : (int[])g.Ring.Clone();
                        result.Regions.Add(Mesh2Region.ByRing(expanded, g.Label, g.MaxArea));
                    }
                }
            }

            return result;
        }

        static int HostPriority(bool locked, int ownerKind) => locked ? 2 : (ownerKind > 0 ? 1 : 0);

        static List<int> NormalizeRing(int[] vertices)
        {
            int count = vertices.Length;
            if (count > 1 && vertices[count - 1] == vertices[0]) --count;
            var list = new List<int>(count);
            for (int i = 0; i < count; ++i) list.Add(vertices[i]);
            return list;
        }

        static int[] ExpandCycle(List<int> cycle, int kind, int idx, Dictionary<(int kind, int idx, int localIdx), int> reroutes)
        {
            var expanded = new List<int>(cycle.Count + 4);
            for (int k = 0; k < cycle.Count; ++k) {
                expanded.Add(cycle[k]);
                if (reroutes.TryGetValue((kind, idx, k), out int fIdx))
                    expanded.Add(fIdx);
            }
            return expanded.ToArray();
        }

        // ---- Static centroid-split BVH over original host segments (strict O(E log E) build, O(log E + k) query) ----

        static int BuildBvh(
            List<BvhNode> nodes, int[] orderX, int[] orderY, int[] leafIds, bool[] side, int[] scratch,
            int start, int count, ref int leafCursor,
            Vector2d[] a, Vector2d[] b, double[] cx, double[] cy)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            double cMinX = double.PositiveInfinity, cMinY = double.PositiveInfinity;
            double cMaxX = double.NegativeInfinity, cMaxY = double.NegativeInfinity;
            for (int i = start; i < start + count; ++i) {
                int id = orderX[i];
                double x0 = Math.Min(a[id].x, b[id].x), x1 = Math.Max(a[id].x, b[id].x);
                double y0 = Math.Min(a[id].y, b[id].y), y1 = Math.Max(a[id].y, b[id].y);
                if (x0 < minX) minX = x0;
                if (x1 > maxX) maxX = x1;
                if (y0 < minY) minY = y0;
                if (y1 > maxY) maxY = y1;
                double mx = cx[id], my = cy[id];
                if (mx < cMinX) cMinX = mx;
                if (mx > cMaxX) cMaxX = mx;
                if (my < cMinY) cMinY = my;
                if (my > cMaxY) cMaxY = my;
            }
            int nodeIdx = nodes.Count;
            if (count <= 8) {
                int leafStart = leafCursor;
                for (int i = start; i < start + count; ++i)
                    leafIds[leafCursor++] = orderX[i];
                nodes.Add(new BvhNode { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Left = ~leafStart, Right = count });
                return nodeIdx;
            }
            nodes.Add(new BvhNode { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY });

            // Split along the axis with larger centroid spread (avoids O(E^2) on long parallel stripes).
            bool splitX = (cMaxX - cMinX) >= (cMaxY - cMinY);
            int mid = count / 2;
            int[] primary = splitX ? orderX : orderY;
            int[] secondary = splitX ? orderY : orderX;

            for (int i = start; i < start + mid; ++i) side[primary[i]] = true;
            for (int i = start + mid; i < start + count; ++i) side[primary[i]] = false;

            int leftPos = start, rightPos = start + mid;
            for (int i = start; i < start + count; ++i) {
                int id = secondary[i];
                if (side[id]) scratch[leftPos++] = id;
                else scratch[rightPos++] = id;
            }
            Array.Copy(scratch, start, secondary, start, count);

            int left = BuildBvh(nodes, orderX, orderY, leafIds, side, scratch, start, mid, ref leafCursor, a, b, cx, cy);
            int right = BuildBvh(nodes, orderX, orderY, leafIds, side, scratch, start + mid, count - mid, ref leafCursor, a, b, cx, cy);
            var n = nodes[nodeIdx];
            n.Left = left;
            n.Right = right;
            nodes[nodeIdx] = n;
            return nodeIdx;
        }

        static long QueryBvh(List<BvhNode> nodes, int[] leafIds, Stack<int> stack, double minX, double minY, double maxX, double maxY, List<int> results)
        {
            if (nodes.Count == 0) return 0;
            long visits = 0;
            stack.Clear();
            stack.Push(0);
            while (stack.Count > 0) {
                ++visits;
                var n = nodes[stack.Pop()];
                if (n.MaxX < minX || n.MinX > maxX || n.MaxY < minY || n.MinY > maxY) continue;
                if (n.Left < 0) {
                    int start = ~n.Left, count = n.Right;
                    for (int i = start; i < start + count; ++i) results.Add(leafIds[i]);
                } else {
                    stack.Push(n.Right);
                    stack.Push(n.Left);
                }
            }
            return visits;
        }

        // ---- Scale-invariant geometry helpers (overflow/underflow-free, bit-identical under 2^k scaling) ----

        static int Exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
            int e = (int)((bits >> 52) & 0x7FF);
            if (e != 0) return e - 1023;
            bits = BitConverter.DoubleToInt64Bits(Math.Abs(x) * 18446744073709551616.0);
            return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
        }

        static int ScaleOf(double a, double b, double c, double d)
        {
            double m = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
            return m > 0 ? Exponent(m) : 0;
        }

        static (int ex, double len2) ScaledLen2(Vector2d a, Vector2d b)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            if (dx == 0 && dy == 0) return (int.MinValue, 0);
            int ex = ScaleOf(dx, dy, 0, 0);
            double sx = ExactPoint3.ScaleB(dx, -ex), sy = ExactPoint3.ScaleB(dy, -ex);
            return (ex, sx * sx + sy * sy);
        }

        static int CompareScaledLen2((int ex, double len2) a, (int ex, double len2) b) =>
            CompareScaledVal(a.ex, a.len2, b.ex, b.len2);

        static int CompareScaledVal(int exA, double vA, int exB, double vB)
        {
            if (exA == exB) return vA.CompareTo(vB);
            if (exA == int.MinValue) return -1;
            if (exB == int.MinValue) return 1;
            int diff = exA - exB;
            if (diff > 2) return 1;
            if (diff < -2) return -1;
            double scaledA = ExactPoint3.ScaleB(vA, 2 * diff);
            return scaledA.CompareTo(vB);
        }

        static bool PointOnSegment(Vector2d a, Vector2d b, Vector2d p)
        {
            if (p == a || p == b) return true;
            double hx = b.x - a.x, hy = b.y - a.y;
            if (hx == 0 && hy == 0) return false;
            double px = p.x - a.x, py = p.y - a.y;
            int ex = ScaleOf(hx, hy, px, py);
            double shx = ExactPoint3.ScaleB(hx, -ex), shy = ExactPoint3.ScaleB(hy, -ex);
            double spx = ExactPoint3.ScaleB(px, -ex), spy = ExactPoint3.ScaleB(py, -ex);
            double h2 = shx * shx + shy * shy;
            if (!(h2 > 0)) return false;
            double t = (spx * shx + spy * shy) / h2;
            if (t < -OnHostRelEps || t > 1.0 + OnHostRelEps) return false;
            double cross = spx * shy - spy * shx;
            // Perpendicular distance^2 = cross^2 / h2 <= (OnHostRelEps)^2 * h2
            return cross * cross <= (OnHostRelEps * OnHostRelEps) * h2 * h2;
        }

        static bool IsThinWedgeAngle(Vector2d p, Vector2d q, Vector2d h0, Vector2d h1)
        {
            double ux = q.x - p.x, uy = q.y - p.y;
            double vx = h1.x - h0.x, vy = h1.y - h0.y;
            if ((ux == 0 && uy == 0) || (vx == 0 && vy == 0)) return false;
            int eu = ScaleOf(ux, uy, 0, 0);
            int ev = ScaleOf(vx, vy, 0, 0);
            double sux = ExactPoint3.ScaleB(ux, -eu), suy = ExactPoint3.ScaleB(uy, -eu);
            double svx = ExactPoint3.ScaleB(vx, -ev), svy = ExactPoint3.ScaleB(vy, -ev);
            double u2 = sux * sux + suy * suy;
            double v2 = svx * svx + svy * svy;
            double cross = sux * svy - suy * svx;
            if (cross == 0) return false;
            double sin2 = (cross * cross) / (u2 * v2);
            return sin2 < MaxWedgeSin2;
        }

        static bool ProjectWithinTolerance(
            Vector2d h0, Vector2d h1, Vector2d q, double tol,
            out Vector2d foot, out double tFoot, out int distEx, out double distScaled2)
        {
            foot = Vector2d.Zero;
            tFoot = 0;
            distEx = int.MinValue;
            distScaled2 = 0;
            double hx = h1.x - h0.x, hy = h1.y - h0.y;
            if (hx == 0 && hy == 0) return false;
            double qx = q.x - h0.x, qy = q.y - h0.y;
            int ex = ScaleOf(hx, hy, qx, qy);
            double shx = ExactPoint3.ScaleB(hx, -ex), shy = ExactPoint3.ScaleB(hy, -ex);
            double sqx = ExactPoint3.ScaleB(qx, -ex), sqy = ExactPoint3.ScaleB(qy, -ex);
            double h2 = shx * shx + shy * shy;
            if (!(h2 > 0)) return false;
            double t = (sqx * shx + sqy * shy) / h2;
            if (t < -OnHostRelEps || t > 1.0 + OnHostRelEps) return false;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            // Offset form from h0 preserves precision at UTM / 2^20 coordinates and exact 2^k scaling
            Vector2d f = t == 0.0 ? h0 : (t == 1.0 ? h1 : new Vector2d(h0.x + t * hx, h0.y + t * hy));
            double dx = q.x - f.x, dy = q.y - f.y;
            if (dx == 0 && dy == 0) return false;
            int dex = ScaleOf(dx, dy, 0, 0);
            double sdx = ExactPoint3.ScaleB(dx, -dex), sdy = ExactPoint3.ScaleB(dy, -dex);
            double d2 = sdx * sdx + sdy * sdy;
            double stol = ExactPoint3.ScaleB(tol, -dex);
            if (!(d2 <= stol * stol)) return false;
            foot = f;
            tFoot = t;
            distEx = dex;
            distScaled2 = d2;
            return true;
        }

        static bool WithinTolerance(Vector2d a, Vector2d b, double tol)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            if (dx == 0 && dy == 0) return true;
            int ex = ScaleOf(dx, dy, 0, 0);
            double sdx = ExactPoint3.ScaleB(dx, -ex), sdy = ExactPoint3.ScaleB(dy, -ex);
            double stol = ExactPoint3.ScaleB(tol, -ex);
            return sdx * sdx + sdy * sdy <= stol * stol;
        }

        /// <summary>
        /// True if edge (q -> r) continues within the same thin-wedge angle (&lt; 10 deg) and tolerance band of host (h0, h1).
        /// Used to decide whether q is an interior drifting-polyline vertex (Case I) vs a branch/corner (Case II).
        /// </summary>
        static bool StaysInWedgeBand(Vector2d q, Vector2d r, Vector2d h0, Vector2d h1, double tol)
        {
            double ux = r.x - q.x, uy = r.y - q.y;
            double vx = h1.x - h0.x, vy = h1.y - h0.y;
            if ((ux == 0 && uy == 0) || (vx == 0 && vy == 0)) return true;
            int eu = ScaleOf(ux, uy, 0, 0);
            int ev = ScaleOf(vx, vy, 0, 0);
            double sux = ExactPoint3.ScaleB(ux, -eu), suy = ExactPoint3.ScaleB(uy, -eu);
            double svx = ExactPoint3.ScaleB(vx, -ev), svy = ExactPoint3.ScaleB(vy, -ev);
            double u2 = sux * sux + suy * suy;
            double v2 = svx * svx + svy * svy;
            double cross = sux * svy - suy * svx;
            double sin2 = (cross * cross) / (u2 * v2);
            if (!(sin2 < MaxWedgeSin2)) return false;

            // Far end r must also project onto h within tol
            double qx = r.x - h0.x, qy = r.y - h0.y;
            int ex = ScaleOf(vx, vy, qx, qy);
            double shx = ExactPoint3.ScaleB(vx, -ex), shy = ExactPoint3.ScaleB(vy, -ex);
            double sqx = ExactPoint3.ScaleB(qx, -ex), sqy = ExactPoint3.ScaleB(qy, -ex);
            double h2 = shx * shx + shy * shy;
            if (!(h2 > 0)) return false;
            double t = (sqx * shx + sqy * shy) / h2;
            if (t < -OnHostRelEps || t > 1.0 + OnHostRelEps) return false;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            Vector2d f = t == 0.0 ? h0 : (t == 1.0 ? h1 : new Vector2d(h0.x + t * vx, h0.y + t * vy));
            return WithinTolerance(r, f, tol);
        }
    }
}
