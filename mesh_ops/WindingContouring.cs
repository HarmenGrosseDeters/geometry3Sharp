// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Shared implementation of MeshWindingNumberSolidify and MeshWindingNumberBoolean: extracts the
    /// iso-surface of a field built from the winding numbers of one or more input meshes with dual
    /// contouring, using the input meshes for exact Hermite data, and post-processes the result.
    /// </summary>
    internal class WindingContouring
    {
        public DMesh3[] Meshes;
        public DMeshAABBTree3[] Spatials;
        /// <summary> combines the winding numbers of the inputs (null for a single input) </summary>
        public Func<double[], double> CombineF;
        public double Iso = 0.5;
        public int MaxEdgeHits = 8;
        public Func<bool> CancelF = () => { return false; };

        // outputs of ContourAdaptive()
        public int NonManifoldTriangles;
        public int WindingEvaluations;
        public int HiddenGapEdges;
        /// <summary> hidden gap edges whose gap is at least MinFixableGapWidth wide (refining can resolve them) </summary>
        public int FixableGapEdges;
        public double MinFixableGapWidth = 0;
        /// <summary> time per phase (ms, summed over the refinement levels): octree, then the dual contouring phases </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> hidden gap edges at each refinement level of ContourRefined </summary>
        public List<int> GapEdgesPerLevel = new List<int>();


        /// <summary>
        /// Prepares the inputs: copies them, flips inside-out ones (their winding number is -1 inside)
        /// and builds the trees and winding number caches.
        /// </summary>
        public WindingContouring(IEnumerable<DMesh3> inputs, Func<double[], double> combineF, double fastWindingBeta)
        {
            List<DMesh3> meshes = new List<DMesh3>();
            foreach (DMesh3 m in inputs) {
                DMesh3 copy = new DMesh3(m);
                // the signed volume (relative to the center: for open meshes it depends on the origin)
                Vector3d c = copy.CachedBounds.Center;
                if (MeshMeasurements.VolumeArea(copy, copy.TriangleIndices(), (vid) => copy.GetVertex(vid) - c).x < 0)
                    copy.ReverseOrientation(false);
                meshes.Add(copy);
            }
            Meshes = meshes.ToArray();
            Spatials = new DMeshAABBTree3[Meshes.Length];
            for (int i = 0; i < Meshes.Length; ++i) {
                Spatials[i] = new DMeshAABBTree3(Meshes[i], true);
                Spatials[i].FWNBeta = fastWindingBeta;
                Spatials[i].FastWindingNumber(Vector3d.Zero);   // build the cache once, before parallel use
            }
            CombineF = combineF;
        }


        public double Field(Vector3d p)
        {
            if (Meshes.Length == 1)
                return Spatials[0].FastWindingNumber(p);
            double[] w = new double[Meshes.Length];
            for (int i = 0; i < Meshes.Length; ++i)
                w[i] = Spatials[i].FastWindingNumber(p);
            return CombineF(w);
        }


        /// <summary>
        /// Octree over the field (winding number only near the surface), then sparse dual contouring of its
        /// leaf cells. The leaf grid is shifted by an odd fraction of a cell, so faces at round coordinates
        /// (common in CAD data) do not land on grid nodes, where inside/outside is ambiguous.
        /// </summary>
        public DMesh3 ContourAdaptive(double cellSize)
        {
            WindingNumberOctree octree = new WindingNumberOctree(Meshes, Spatials, CombineF) {
                TargetLeafCellSize = cellSize, WindingIso = Iso, CancelF = CancelF,
                GridOffset = new Vector3d(0.1371, 0.2713, 0.3891)
            };
            var timer = System.Diagnostics.Stopwatch.StartNew();
            octree.Build();
            add_phase("octree", timer.ElapsedMilliseconds);
            if (CancelF())
                return null;

            DualContouring dc = new DualContouring() {
                Implicit = new FieldImplicit() { Owner = this },
                IsoValue = Iso, CellSize = octree.LeafCellSize, CancelF = CancelF,
                Bounds = new AxisAlignedBox3d(octree.Origin, octree.Origin + octree.LeafCount * octree.LeafCellSize * Vector3d.One),
                CandidateCells = octree.SurfaceCells,
                NodeValueF = octree.NodeValue
            };
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                Hermite(cellSize, a, b, p, out point, out normal);
            dc.HiddenGapF = (a, b) => HiddenGap(cellSize, a, b);
            fixableGaps = 0;
            dc.Generate();
            foreach (var kv in dc.PhaseMilliseconds)
                add_phase(kv.Key, kv.Value);
            WindingEvaluations = octree.WindingEvaluations;
            HiddenGapEdges = dc.HiddenGapEdges;
            FixableGapEdges = fixableGaps;
            if (CancelF())
                return null;
            NonManifoldTriangles = dc.NonManifoldTriangles;
            if (NonManifoldTriangles > 0)
                FillSmallHoles(dc.Mesh);
            return dc.Mesh;
        }

        void add_phase(string name, long ms)
        {
            long cur;
            PhaseMilliseconds[name] = PhaseMilliseconds.TryGetValue(name, out cur) ? cur + ms : ms;
        }

        /// <summary>
        /// ContourAdaptive, refined for hidden gaps: while the result has hidden gap edges that the remaining
        /// halvings can resolve (gaps at least cellSize / 2^levelsLeft wide), halve the cell size and
        /// recompute. Narrower gaps (eg between nearly touching layers of open meshes) are not worth the
        /// work: each level costs about 4x. Once the gaps are gone the gap is only about one cell wide, and
        /// the surfaces on both sides can still cut through each other (dual contouring needs about two cells
        /// across a gap), so one more level is added. Refinement also stops when a level did not reduce the
        /// fixable gap edges (they are not gaps that a finer grid resolves). At most maxLevels halvings;
        /// cellSize returns the size used.
        /// </summary>
        public DMesh3 ContourRefined(ref double cellSize, int maxLevels)
        {
            MinFixableGapWidth = cellSize / Math.Pow(2, maxLevels);
            DMesh3 contour = ContourAdaptive(cellSize);
            int evaluations = WindingEvaluations;
            GapEdgesPerLevel.Clear();
            GapEdgesPerLevel.Add(HiddenGapEdges);
            bool hadGaps = false, extraDone = false;
            int prevFixable = int.MaxValue;
            for (int level = 0; level < maxLevels && contour != null; ++level) {
                if (FixableGapEdges > 0 && FixableGapEdges >= prevFixable)
                    break;
                prevFixable = FixableGapEdges;
                if (FixableGapEdges > 0)
                    hadGaps = true;
                else if (!hadGaps || extraDone || HiddenGapEdges > 0)
                    break;
                else
                    extraDone = true;
                if (WindingNumberOctree.RequiredDepth(WindingNumberOctree.Bounds(Meshes), 0.5 * cellSize) > WindingNumberOctree.MaxSupportedDepth)
                    break;
                cellSize *= 0.5;
                contour = ContourAdaptive(cellSize);
                evaluations += WindingEvaluations;
                GapEdgesPerLevel.Add(HiddenGapEdges);
            }
            WindingEvaluations = evaluations;
            return contour;
        }


        // field gradient fallback for dual contouring where no Hermite data is found
        class FieldImplicit : ImplicitFunction3d
        {
            public WindingContouring Owner;
            public double Value(ref Vector3d pt) { return Owner.Field(pt); }
        }


        /// <summary>
        /// Hermite data for the grid edge a (inside) -> b (outside): preferably where the edge hits an input
        /// mesh (exact point on the edge), otherwise the nearest input point to the interpolated crossing p.
        /// Only surfaces where the field actually crosses the iso-value count, so interior shells (and, for
        /// booleans, the parts of one input inside the other) are ignored.
        /// </summary>
        public bool Hermite(double cellSize, Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal)
        {
            point = p; normal = Vector3d.Zero;
            Vector3d origin, dir;
            List<Hit> hits = collect_hits(cellSize, a, b, 0.01 * cellSize, out origin, out dir);

            // With a single surface on the edge, that surface is the boundary. With several, find where the
            // field crosses the iso-value between them and use the transition closest to p.
            int best = -1;
            if (hits.Count == 1) {
                best = 0;
            } else if (hits.Count > 1) {
                bool insideBefore = true;
                double bestDist = double.MaxValue;
                double tp = (p - origin).Dot(dir);
                for (int i = 0; i < hits.Count; ++i) {
                    bool insideAfter = (i == hits.Count - 1) ? false
                        : Field(origin + 0.5 * (hits[i].T + hits[i + 1].T) * dir) > Iso;
                    if (insideBefore != insideAfter) {
                        double d = Math.Abs(hits[i].T - tp);
                        if (d < bestDist) { bestDist = d; best = i; }
                    }
                    insideBefore = insideAfter;
                }
            }
            if (best >= 0) {
                Vector3d n = Meshes[hits[best].Mesh].GetTriNormal(hits[best].Tri);
                if (n.LengthSquared > 0.5) {
                    point = origin + hits[best].T * dir; normal = n;
                    return true;
                }
            }

            // fallback: nearest input point
            double bestDistSqr = double.MaxValue;
            for (int mi = 0; mi < Meshes.Length; ++mi) {
                double distSqr;
                int tid = Spatials[mi].FindNearestTriangle(p, out distSqr, 1.5 * cellSize);
                if (tid == DMesh3.InvalidID || distSqr >= bestDistSqr)
                    continue;
                DistPoint3Triangle3 dist = MeshQueries.TriangleDistance(Meshes[mi], tid, p);
                Vector3d tn = Meshes[mi].GetTriNormal(tid);
                if (tn.LengthSquared < 0.5 || is_boundary(dist.TriangleClosest, tn, cellSize) == false)
                    continue;
                bestDistSqr = distSqr;
                point = dist.TriangleClosest; normal = tn;
            }
            return bestDistSqr < double.MaxValue;
        }

        // input surfaces along the segment a-b (extended by margin at both ends), from all meshes, sorted by T
        List<Hit> collect_hits(double cellSize, Vector3d a, Vector3d b, double margin, out Vector3d origin, out Vector3d dir)
        {
            double len = a.Distance(b);
            dir = (b - a) / len;
            origin = a - margin * dir;
            double maxT = len + 2 * margin;
            List<Hit> hits = new List<Hit>();
            for (int mi = 0; mi < Meshes.Length && hits.Count < MaxEdgeHits; ++mi) {
                Ray3d ray = new Ray3d(origin, dir);
                double t0 = 0;
                while (hits.Count < MaxEdgeHits) {
                    int hit = Spatials[mi].FindNearestHitTriangle(ray, maxT - t0);
                    if (hit == DMesh3.InvalidID)
                        break;
                    IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(Meshes[mi], hit, ray);
                    if (intr.Find() == false)
                        break;
                    hits.Add(new Hit() { Mesh = mi, Tri = hit, T = t0 + intr.RayParameter });
                    double advance = intr.RayParameter + 1e-6 * cellSize;
                    t0 += advance;
                    ray = new Ray3d(ray.PointAt(advance), dir);
                }
            }
            hits.Sort((x, y) => x.T.CompareTo(y.T));
            return hits;
        }

        /// <summary>
        /// For a grid edge with both ends inside: true if the field drops below the iso-value between two
        /// roughly parallel input surfaces on the edge, ie the edge crosses a gap the node signs cannot see.
        /// Not counted: surfaces at a steeper angle, and the apex of a wedge between surfaces that meet (eg
        /// where the two inputs of a union meet at a small angle): at any cell size the wedge is narrower
        /// than a cell over the same number of cells near its apex, so refining would not remove it, and
        /// dual contouring handles the crease. Only (nearly) parallel gaps are resolved by refining.
        /// </summary>
        public bool HiddenGap(double cellSize, Vector3d a, Vector3d b)
        {
            Vector3d origin, dir;
            List<Hit> hits = collect_hits(cellSize, a, b, 0, out origin, out dir);
            for (int i = 0; i + 1 < hits.Count; ++i) {
                if (hits[i + 1].T - hits[i].T < 1e-6 * cellSize)
                    continue;
                Vector3d n0 = Meshes[hits[i].Mesh].GetTriNormal(hits[i].Tri), n1 = Meshes[hits[i + 1].Mesh].GetTriNormal(hits[i + 1].Tri);
                if (Math.Abs(n0.Dot(n1)) < GapParallelCos)
                    continue;
                // surfaces at an angle theta meeting at distance s: the wedge between them is narrower than
                // two cells for s < cell / tan(theta / 2), at every cell size
                Vector3d p0 = origin + hits[i].T * dir, p1 = origin + hits[i + 1].T * dir, m = 0.5 * (p0 + p1);
                double sinT = n0.Cross(n1).Length, tanHalf = sinT / (1 + Math.Abs(n0.Dot(n1)));
                if (tanHalf > 0 && crease_distance(p0, n0, p1, n1, m) * tanHalf < cellSize)
                    continue;
                if (Field(m) < Iso) {
                    if (hits[i + 1].T - hits[i].T >= MinFixableGapWidth)
                        System.Threading.Interlocked.Increment(ref fixableGaps);
                    return true;
                }
            }
            return false;
        }
        int fixableGaps;
        const double GapParallelCos = 0.5;     // surfaces within 60 degrees of parallel

        // distance from m to the line where the planes (p0, n0) and (p1, n1) meet (infinite if parallel)
        static double crease_distance(Vector3d p0, Vector3d n0, Vector3d p1, Vector3d n1, Vector3d m)
        {
            Vector3d d = n0.Cross(n1);
            double dd = d.LengthSquared;
            if (dd < 1e-6)      // within about 0.06 degrees of parallel
                return double.MaxValue;
            // a point on both planes: x = (h0 (n1 x d) + h1 (d x n0)) / |d|^2, with h = n . p
            double h0 = n0.Dot(p0), h1 = n1.Dot(p1);
            Vector3d x = (h0 * n1.Cross(d) + h1 * d.Cross(n0)) / dd;
            Vector3d dn = d / Math.Sqrt(dd);
            Vector3d r = m - x;
            return (r - r.Dot(dn) * dn).Length;
        }

        struct Hit
        {
            public int Mesh, Tri;
            public double T;
        }

        bool is_boundary(Vector3d q, Vector3d n, double cellSize)
        {
            double eps = 0.05 * cellSize;
            return (Field(q + eps * n) > Iso) != (Field(q - eps * n) > Iso);
        }


        /// <summary>
        /// Cleanup and reduction. The reducer uses unweighted plane quadrics (error = sum of squared distances
        /// to the original planes, so corners and edges only move within the tolerance of every adjacent
        /// plane), limits how far a collapse may turn a triangle (no folds) and how bad it may make one
        /// (no degenerate triangles). Returns a compacted copy.
        /// </summary>
        public static DMesh3 PostProcess(DMesh3 mesh, bool cleanup, double reduceTolerance, double cellSize,
            Func<bool> cancelF, Action<string> endPhase)
        {
            if (cleanup) {
                new MeshPostRepairCleanup(mesh).Apply();
                endPhase("cleanup");
            }
            if (reduceTolerance > 0 && cancelF() == false) {
                Reducer reducer = new Reducer(mesh) {
                    AreaWeightedQuadrics = false, EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad), MinCollapseTriangleQuality = 0.05
                };
                double tol = reduceTolerance * cellSize;
                reducer.ReduceToMaxQuadricError(tol * tol);
                endPhase("reduce");
            }
            return new DMesh3(mesh, true);
        }


        /// <summary>
        /// Dual contouring leaves out the rare quads that would make an edge non-manifold (an ambiguous face
        /// where both cells have a single patch). Fill the resulting small holes; bowties this can leave are
        /// removed by the cleanup pass.
        /// </summary>
        public static void FillSmallHoles(DMesh3 mesh)
        {
            // the holes' borders often run through bowtie vertices
            new MeshEditor(mesh).DisconnectAllBowties();
            MeshBoundaryLoops loops = null;
            try {
                loops = new MeshBoundaryLoops(mesh, true);
            } catch (Exception) {
            }
            if (loops != null) {
                foreach (EdgeLoop loop in loops) {
                    MinimalHoleFill fill = new MinimalHoleFill(mesh, loop);
                    if (fill.Apply() == false)
                        new SimpleHoleFiller(mesh, loop).Fill();
                }
            }
            fan_fill_remaining_holes(mesh);
        }

        // Fallback for holes the fillers could not close: walk the border edges into loops and fill each
        // with a fan, orienting every fan triangle to match the triangle on the other side of its border edge.
        static void fan_fill_remaining_holes(DMesh3 mesh)
        {
            HashSet<int> remaining = new HashSet<int>(MeshIterators.BoundaryEdges(mesh));
            while (remaining.Count > 0) {
                int start = remaining.First();
                List<int> loopEdges = new List<int>();
                int cur = start, vCur = mesh.GetEdgeV(start).b;
                while (true) {
                    loopEdges.Add(cur);
                    remaining.Remove(cur);
                    int next = -1;
                    foreach (int eid in mesh.VtxEdgesItr(vCur))
                        if (remaining.Contains(eid)) { next = eid; break; }
                    if (next < 0)
                        break;
                    Index2i nv = mesh.GetEdgeV(next);
                    vCur = (nv.a == vCur) ? nv.b : nv.a;
                    cur = next;
                }
                if (loopEdges.Count < 3)
                    continue;
                Vector3d center = Vector3d.Zero;
                foreach (int eid in loopEdges) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    center += 0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b));
                }
                int c = mesh.AppendVertex(center / loopEdges.Count);
                foreach (int eid in loopEdges) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    Index3i t = mesh.GetTriangle(mesh.GetEdgeT(eid).a);
                    // the existing triangle traverses the edge a->b or b->a; the new one goes the other way
                    bool aToB = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref t) != DMesh3.InvalidID;
                    if (aToB)
                        mesh.AppendTriangle(ev.b, ev.a, c);
                    else
                        mesh.AppendTriangle(ev.a, ev.b, c);
                }
            }
        }
    }
}
