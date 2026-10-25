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
        /// <summary> grid edges through material thinner than a cell (both ends outside), see HiddenWall </summary>
        public int HiddenWallEdges;
        /// <summary> hidden gap / wall edges at least MinFixableWidth wide (refining can resolve them) </summary>
        public int FixableEdges;
        /// <summary> narrowest fixable gap or wall (perpendicular width) of the last ContourAdaptive </summary>
        public double MinFixableFound = double.MaxValue;
        public double MinFixableWidth = 0;
        /// <summary> some of the hidden gaps / walls of the last ContourAdaptive (gap center, width), for diagnostics </summary>
        public List<KeyValuePair<Vector3d, double>> HiddenSamples = new List<KeyValuePair<Vector3d, double>>();
        /// <summary> time per phase (ms, summed over the refinement levels): octree, then the dual contouring phases </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> hidden gap edges at each refinement level of ContourRefined </summary>
        public List<int> GapEdgesPerLevel = new List<int>();


        /// <summary>
        /// Prepares the inputs: copies them (compacted), flips inside-out ones and inside-out closed
        /// components (their winding number is -1 inside) and builds the trees and winding number caches.
        /// </summary>
        public WindingContouring(IEnumerable<DMesh3> inputs, Func<double[], double> combineF, double fastWindingBeta)
        {
            List<DMesh3> meshes = new List<DMesh3>();
            foreach (DMesh3 m in inputs)
                meshes.Add(OrientedCopy(m));
            Meshes = meshes.ToArray();
            Spatials = new DMeshAABBTree3[Meshes.Length];
            for (int i = 0; i < Meshes.Length; ++i) {
                Spatials[i] = new DMeshAABBTree3(Meshes[i], true);
                Spatials[i].FWNBeta = fastWindingBeta;
                Spatials[i].FastWindingNumber(Vector3d.Zero);   // build the cache once, before parallel use
            }
            CombineF = combineF;
        }


        /// <summary>
        /// Compacted copy with inside-out parts flipped, so that the winding number is +1 inside: closed
        /// components that are inside out for their nesting (eg a disjoint inverted part) on their own, then
        /// the whole by its signed volume (relative to the center: for open meshes it depends on the origin).
        /// </summary>
        public static DMesh3 OrientedCopy(DMesh3 m)
        {
            DMesh3 copy = new DMesh3(m, true);
            foreach (int[] comp in MeshPostRepairCleanup.FindInvertedComponents(copy))
                foreach (int tid in comp)
                    copy.ReverseTriOrientation(tid);
            Vector3d c = copy.CachedBounds.Center;
            if (MeshMeasurements.VolumeArea(copy, copy.TriangleIndices(), (vid) => copy.GetVertex(vid) - c).x < 0)
                copy.ReverseOrientation(false);
            return copy;
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
            hiddenWidths.Clear();
            WindingNumberOctree octree = build_octree(cellSize);
            if (octree == null)
                return null;
            DualContouring dc = make_dual_contouring(octree);
            dc.Generate();
            foreach (var kv in dc.PhaseMilliseconds)
                add_phase(kv.Key, kv.Value);
            WindingEvaluations = octree.WindingEvaluations;
            return finish(dc);
        }

        WindingNumberOctree build_octree(double cellSize)
        {
            WindingNumberOctree octree = new WindingNumberOctree(Meshes, Spatials, CombineF) {
                TargetLeafCellSize = cellSize, WindingIso = Iso, CancelF = CancelF,
                GridOffset = new Vector3d(0.1371, 0.2713, 0.3891)
            };
            var timer = System.Diagnostics.Stopwatch.StartNew();
            octree.Build();
            add_phase("octree", timer.ElapsedMilliseconds);
            return CancelF() ? null : octree;
        }

        DualContouring make_dual_contouring(WindingNumberOctree octree)
        {
            DualContouring dc = new DualContouring() {
                Implicit = new FieldImplicit() { Owner = this },
                IsoValue = Iso, CellSize = octree.LeafCellSize, CancelF = CancelF,
                Bounds = new AxisAlignedBox3d(octree.Origin, octree.Origin + octree.LeafCount * octree.LeafCellSize * Vector3d.One),
                CandidateCells = octree.SurfaceCells,
                NodeValueF = octree.NodeValue
            };
            // tolerances relative to the grid edge (coarse or fine)
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                Hermite(a.Distance(b), a, b, p, out point, out normal);
            dc.HiddenGapF = (a, b) => HiddenGap(a.Distance(b), a, b);
            dc.HiddenWallF = (a, b) => HiddenWall(a.Distance(b), a, b);
            return dc;
        }

        DMesh3 finish(DualContouring dc)
        {
            HiddenGapEdges = dc.HiddenGapEdges;
            HiddenWallEdges = dc.HiddenWallEdges;
            classify_hidden(dc);
            TransitionErrors = dc.TransitionErrors;
            if (CancelF() || dc.Mesh == null)
                return null;
            NonManifoldTriangles = dc.NonManifoldTriangles;
            if (NonManifoldTriangles > 0)
                FillSmallHoles(dc.Mesh);
            return dc.Mesh;
        }

        // FixableEdges, MinFixableFound and HiddenSamples from the hidden edges of the last crossing search;
        // returns the fixable ones
        List<DualContouring.HiddenEdge> classify_hidden(DualContouring dc)
        {
            var fixableList = new List<DualContouring.HiddenEdge>();
            double minFixable = double.MaxValue;
            HiddenSamples = new List<KeyValuePair<Vector3d, double>>();
            foreach (DualContouring.HiddenEdge he in dc.HiddenEdges) {
                double width;
                if (hiddenWidths.TryGetValue(new EdgePoints(he.A, he.B), out width) == false)
                    continue;
                if (width >= MinFixableWidth) {
                    fixableList.Add(he);
                    minFixable = Math.Min(minFixable, width);
                }
                if (HiddenSamples.Count < 10000)
                    HiddenSamples.Add(new KeyValuePair<Vector3d, double>(0.5 * (he.A + he.B), width));
            }
            FixableEdges = fixableList.Count;
            MinFixableFound = minFixable;
            return fixableList;
        }

        void add_phase(string name, long ms)
        {
            long cur;
            PhaseMilliseconds[name] = PhaseMilliseconds.TryGetValue(name, out cur) ? cur + ms : ms;
        }

        /// <summary> refine only the regions around hidden gaps / walls (see ContourRefined); false: halve the cell size globally </summary>
        public bool LocalRefinement = true;
        /// <summary> local refinement: cells refined beyond the 4 cells around each hidden gap / wall edge </summary>
        public int RefineMargin = 1;
        /// <summary>
        /// local refinement: at most this many times the refined region is grown where the fine signs on its
        /// border had to be imposed (the coarse cells there miss part of a gap or wall, eg one that crosses a
        /// sign-changing grid edge)
        /// </summary>
        public int MaxGrowRounds = 2;
        /// <summary>
        /// local refinement: if the refined region would contain more than this fraction of the cells the
        /// surface passes through, the whole grid is refined instead (cheaper then)
        /// </summary>
        public double MaxLocalFraction = 0.5;
        /// <summary> local refinement: coarse cells refined by the last ContourRefined, and by which factor </summary>
        public int RefinedCells, RefineFactor = 1;
        /// <summary>
        /// local refinement: at most this many times the cells around polygons that dual contouring left out
        /// (non-manifold edges, see DualContouring.NonManifoldCells) are refined and contoured again
        /// </summary>
        public int MaxNonManifoldRounds = 2;
        /// <summary> local refinement: how often the last ContourRefined refined for non-manifold edges </summary>
        public int NonManifoldRefinements;
        /// <summary> local refinement: fine crossings not assigned to a coarse patch (see DualContouring.TransitionErrors) </summary>
        public int TransitionErrors;

        /// <summary>
        /// ContourAdaptive, refined for hidden gaps and walls (gaps or material thinner than a cell, which the
        /// node signs cannot see: the surfaces on both sides cut through each other, or thin walls vanish or
        /// break up). Dual contouring needs about two cells across a gap or wall, so the cell size is halved
        /// directly as often as the narrowest one needs for that (measured perpendicular to its surfaces),
        /// then checked again. Only gaps and walls the levels left can resolve count (at least
        /// cellSize / 2^maxLevels wide): narrower ones (eg nearly touching layers of open meshes) are not
        /// worth the work. Refinement is done for at least minEdges such edges; fewer are only reported in
        /// HiddenGapEdges / HiddenWallEdges. At most maxLevels halvings; cellSize returns the finest size used.
        ///
        /// With LocalRefinement, only the cells around the hidden gap / wall edges (RefineMargin more in each
        /// direction) are refined, the rest stays at the original cell size (see DualContouring.SetRefinement
        /// for the transition between them); the work for the refinement grows with the area of the refined
        /// regions only. Otherwise the cell size is halved everywhere, each level costs about 4x.
        /// </summary>
        public DMesh3 ContourRefined(ref double cellSize, int maxLevels, int minEdges = 1)
        {
            MinFixableWidth = cellSize / Math.Pow(2, maxLevels);
            GapEdgesPerLevel.Clear();
            RefinedCells = 0; RefineFactor = 1; NonManifoldRefinements = 0;
            int evaluations = 0;
            if (LocalRefinement) {
                bool tooLarge;
                DMesh3 local = contour_local(ref cellSize, maxLevels, minEdges, out tooLarge);
                if (tooLarge == false)
                    return local;
                // refining most of the surface: global refinement is cheaper (and needs no transitions)
                evaluations = WindingEvaluations;
                GapEdgesPerLevel.Clear();
                RefinedCells = 0; RefineFactor = 1;
            }

            DMesh3 contour = ContourAdaptive(cellSize);
            evaluations += WindingEvaluations;
            GapEdgesPerLevel.Add(HiddenGapEdges + HiddenWallEdges);
            int used = 0;
            while (contour != null && FixableEdges >= Math.Max(1, minEdges) && used < maxLevels) {
                int need = (int)Math.Ceiling(Math.Log(2 * cellSize / MinFixableFound, 2));
                int k = Math.Max(1, Math.Min(need, maxLevels - used));
                double next = cellSize / Math.Pow(2, k);
                if (WindingNumberOctree.RequiredDepth(WindingNumberOctree.Bounds(Meshes), next) > WindingNumberOctree.MaxSupportedDepth)
                    break;
                cellSize = next;
                used += k;
                contour = ContourAdaptive(cellSize);
                evaluations += WindingEvaluations;
                GapEdgesPerLevel.Add(HiddenGapEdges + HiddenWallEdges);
            }
            WindingEvaluations = evaluations;
            return contour;
        }

        DMesh3 contour_local(ref double cellSize, int maxLevels, int minEdges, out bool tooLarge)
        {
            tooLarge = false;
            hiddenWidths.Clear();
            double coarse = cellSize;
            WindingNumberOctree octree = build_octree(coarse);
            if (octree == null)
                return null;
            DualContouring dc = make_dual_contouring(octree);
            dc.FindCrossings();
            if (CancelF())
                return null;
            List<DualContouring.HiddenEdge> fixableList = classify_hidden(dc);
            GapEdgesPerLevel.Add(dc.HiddenGapEdges + dc.HiddenWallEdges);

            var fineOctrees = new List<WindingNumberOctree>();
            WindingNumberOctree fine = null;
            HashSet<Vector3i> region = new HashSet<Vector3i>();
            int used = 0, log2F = 0, growRounds = 0;
            var timer = new System.Diagnostics.Stopwatch();
            while (true) {
                bool newLevel = false;
                if (fixableList.Count >= Math.Max(1, minEdges) && used < maxLevels) {
                    // refine (further) around the fixable hidden edges, as far as the narrowest one needs
                    double cur = coarse / (1 << log2F);
                    int need = (int)Math.Ceiling(Math.Log(2 * cur / MinFixableFound, 2));
                    int k = Math.Max(1, Math.Min(need, maxLevels - used));
                    if (octree.MaxDepth + log2F + k > WindingNumberOctree.MaxSupportedDepth)
                        break;
                    log2F += k;
                    used += k;
                    newLevel = true;
                    foreach (DualContouring.HiddenEdge he in fixableList)
                        add_to_region(region, octree, he.A, he.B);
                } else if (log2F > 0 && dc.ImposedCells.Count > 0 && growRounds < MaxGrowRounds) {
                    // the coarse grid misses something at the border of the refined region (the fine signs
                    // there had to be imposed): refine there too
                    growRounds++;
                    foreach (Vector3i c in dc.ImposedCells)
                        add_to_region(region, octree, c);
                } else
                    break;
                int inRegion = 0;
                foreach (Vector3i c in octree.SurfaceCells)
                    if (region.Contains(c)) inRegion++;
                if (inRegion > MaxLocalFraction * octree.SurfaceCells.Count) {
                    tooLarge = true;
                    WindingEvaluations = octree.WindingEvaluations;
                    foreach (WindingNumberOctree fo in fineOctrees)
                        WindingEvaluations += fo.WindingEvaluations;
                    return null;
                }
                int F = 1 << log2F;
                timer.Restart();
                if (newLevel) {
                    fine = new WindingNumberOctree(Meshes, Spatials, CombineF) { WindingIso = Iso, CancelF = CancelF };
                    fineOctrees.Add(fine);
                }
                fine.RegionF = region_filter(region, F);
                fine.BuildOnGrid(octree.Origin, coarse / F, octree.MaxDepth + log2F);    // keeps the node values of the same grid
                add_phase("octree.fine", timer.ElapsedMilliseconds);
                if (CancelF())
                    return null;
                dc.SetRefinement(F, region, fine.SurfaceCells, fine.NodeValue);
                dc.FindCrossings();
                if (CancelF())
                    return null;
                fixableList = classify_hidden(dc);
                if (newLevel)
                    GapEdgesPerLevel.Add(dc.HiddenGapEdges + dc.HiddenWallEdges);
                else
                    GapEdgesPerLevel[GapEdgesPerLevel.Count - 1] = dc.HiddenGapEdges + dc.HiddenWallEdges;
            }
            dc.Generate();
            // polygons left out for non-manifold edges: refine around them (one level more if nothing is
            // refined yet, else grow the refined region) and contour again
            for (int round = 0; round < MaxNonManifoldRounds && dc.NonManifoldCells.Count > 0 && CancelF() == false; ++round) {
                bool newLevel = false;
                if (log2F == 0) {
                    if (used >= maxLevels || octree.MaxDepth + 1 > WindingNumberOctree.MaxSupportedDepth)
                        break;
                    log2F = 1;
                    used++;
                    newLevel = true;
                }
                int before = region.Count;
                foreach (Vector3i c in dc.NonManifoldCells)
                    add_to_region(region, octree, c);
                if (region.Count == before)
                    break;
                int inRegion = 0;
                foreach (Vector3i c in octree.SurfaceCells)
                    if (region.Contains(c)) inRegion++;
                if (inRegion > MaxLocalFraction * octree.SurfaceCells.Count)
                    break;
                int F = 1 << log2F;
                timer.Restart();
                if (newLevel) {
                    fine = new WindingNumberOctree(Meshes, Spatials, CombineF) { WindingIso = Iso, CancelF = CancelF };
                    fineOctrees.Add(fine);
                }
                fine.RegionF = region_filter(region, F);
                fine.BuildOnGrid(octree.Origin, coarse / F, octree.MaxDepth + log2F);
                add_phase("octree.fine", timer.ElapsedMilliseconds);
                if (CancelF())
                    return null;
                dc.SetRefinement(F, region, fine.SurfaceCells, fine.NodeValue);
                dc.Generate();
                NonManifoldRefinements++;
            }
            foreach (var kv in dc.PhaseMilliseconds)
                add_phase(kv.Key, kv.Value);
            WindingEvaluations = octree.WindingEvaluations;
            foreach (WindingNumberOctree fo in fineOctrees)
                WindingEvaluations += fo.WindingEvaluations;
            RefinedCells = region.Count;
            RefineFactor = 1 << log2F;
            cellSize = coarse / RefineFactor;
            return finish(dc);
        }

        // the coarse cells around a hidden edge or face diagonal a-b (on a coarse edge: the 4 cells around it),
        // plus RefineMargin
        void add_to_region(HashSet<Vector3i> region, WindingNumberOctree octree, Vector3d a, Vector3d b)
        {
            Vector3d g = (0.5 * (a + b) - octree.Origin) / octree.LeafCellSize;
            Vector3i lo = Vector3i.Zero, hi = Vector3i.Zero;
            for (int d = 0; d < 3; ++d) {
                lo[d] = (int)Math.Floor(g[d] - 0.25) - RefineMargin;
                hi[d] = (int)Math.Floor(g[d] + 0.25) + RefineMargin;
            }
            for (int z = lo.z; z <= hi.z; ++z)
                for (int y = lo.y; y <= hi.y; ++y)
                    for (int x = lo.x; x <= hi.x; ++x)
                        region.Add(new Vector3i(x, y, z));
        }

        // a coarse cell plus RefineMargin
        void add_to_region(HashSet<Vector3i> region, WindingNumberOctree octree, Vector3i cell)
        {
            for (int z = -RefineMargin; z <= RefineMargin; ++z)
                for (int y = -RefineMargin; y <= RefineMargin; ++y)
                    for (int x = -RefineMargin; x <= RefineMargin; ++x)
                        region.Add(cell + new Vector3i(x, y, z));
        }

        // octree cells (fine leaf coordinates, power-of-two sizes at aligned positions) that overlap the region
        // (coarse cells of F fine cells each)
        static Func<Vector3i, int, bool> region_filter(HashSet<Vector3i> region, int F)
        {
            var ancestors = new HashSet<long>();
            foreach (Vector3i c in region) {
                if (c.x < 0 || c.y < 0 || c.z < 0)
                    continue;       // the octree has no cells there
                Vector3i m = F * c;
                for (int s = F, level = 0; s <= (1 << 21); s *= 2, level++)
                    if (ancestors.Add(ancestor_key(level, m / s)) == false)
                        break;
            }
            return (min, size) => {
                if (size >= F) {
                    int level = 0;
                    while ((F << level) < size) level++;
                    return ancestors.Contains(ancestor_key(level, min / size));
                }
                return region.Contains(min / F);
            };
        }
        static long ancestor_key(int level, Vector3i a)
        {
            return ((long)level << 60) | ((long)a.x << 40) | ((long)a.y << 20) | (long)a.z;
        }

        // end points of a grid edge, as passed to HiddenGap / HiddenWall
        struct EdgePoints : IEquatable<EdgePoints>
        {
            public Vector3d A, B;
            public EdgePoints(Vector3d a, Vector3d b) { A = a; B = b; }
            public bool Equals(EdgePoints o) { return A == o.A && B == o.B; }
            public override bool Equals(object o) { return o is EdgePoints && Equals((EdgePoints)o); }
            public override int GetHashCode() { unchecked { return A.GetHashCode() * 31 + B.GetHashCode(); } }
        }
        System.Collections.Concurrent.ConcurrentDictionary<EdgePoints, double> hiddenWidths = new System.Collections.Concurrent.ConcurrentDictionary<EdgePoints, double>();


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
        /// Not counted: surfaces at a steeper angle, and the apex of a wedge where the surfaces actually meet
        /// (eg where the two inputs of a union meet at a small angle): near its apex a wedge is narrower than
        /// a cell at any cell size, so refining would not remove it, and dual contouring handles the crease.
        /// </summary>
        public bool HiddenGap(double cellSize, Vector3d a, Vector3d b)
        {
            return hidden_layer(cellSize, a, b, false);
        }

        /// <summary>
        /// For a grid edge with both ends outside: true if the field rises above the iso-value between two
        /// roughly parallel input surfaces on the edge, ie the edge crosses material thinner than a cell
        /// (sharp edges where the surfaces meet are not counted, as for HiddenGap).
        /// </summary>
        public bool HiddenWall(double cellSize, Vector3d a, Vector3d b)
        {
            return hidden_layer(cellSize, a, b, true);
        }

        bool hidden_layer(double cellSize, Vector3d a, Vector3d b, bool wall)
        {
            Vector3d origin, dir;
            List<Hit> hits = collect_hits(cellSize, a, b, 0, out origin, out dir);
            for (int i = 0; i + 1 < hits.Count; ++i) {
                if (hits[i + 1].T - hits[i].T < 1e-6 * cellSize)
                    continue;
                Vector3d n0 = Meshes[hits[i].Mesh].GetTriNormal(hits[i].Tri), n1 = Meshes[hits[i + 1].Mesh].GetTriNormal(hits[i + 1].Tri);
                if (Math.Abs(n0.Dot(n1)) < GapParallelCos)
                    continue;
                Vector3d p0 = origin + hits[i].T * dir, p1 = origin + hits[i + 1].T * dir, m = 0.5 * (p0 + p1);
                if ((Field(m) > Iso) != wall)
                    continue;
                if (surfaces_meet(hits[i].Mesh, p0, n0, hits[i + 1].Mesh, p1, n1, m, cellSize))
                    continue;
                // width perpendicular to the surfaces (the edge can cross them obliquely)
                double width = (hits[i + 1].T - hits[i].T) * 0.5 * (Math.Abs(dir.Dot(n0)) + Math.Abs(dir.Dot(n1)));
                hiddenWidths[new EdgePoints(a, b)] = width;
                return true;
            }
            return false;
        }
        const double GapParallelCos = 0.5;     // surfaces within 60 degrees of parallel

        // Do the surfaces through p0 and p1 meet near m (a wedge or sharp edge, not a gap or wall)? The
        // point of the line where their tangent planes meet that is closest to m must lie on both input
        // surfaces (up to the planes' deviation from curved surfaces, relative to its distance).
        bool surfaces_meet(int mesh0, Vector3d p0, Vector3d n0, int mesh1, Vector3d p1, Vector3d n1, Vector3d m, double cellSize)
        {
            Vector3d d = n0.Cross(n1);
            double dd = d.LengthSquared;
            if (dd < 1e-12)
                return false;       // parallel: they do not meet
            // a point on both planes: x = (h0 (n1 x d) + h1 (d x n0)) / |d|^2, with h = n . p
            double h0 = n0.Dot(p0), h1 = n1.Dot(p1);
            Vector3d x = (h0 * n1.Cross(d) + h1 * d.Cross(n0)) / dd;
            Vector3d dn = d / Math.Sqrt(dd);
            x += (m - x).Dot(dn) * dn;
            double tol = 0.25 * x.Distance(m) + 0.1 * cellSize;
            return surface_distance(mesh0, x, tol) <= tol && surface_distance(mesh1, x, tol) <= tol;
        }

        double surface_distance(int mesh, Vector3d p, double maxDist)
        {
            double distSqr;
            int tid = Spatials[mesh].FindNearestTriangle(p, out distSqr, maxDist);
            return (tid == DMesh3.InvalidID) ? double.MaxValue : Math.Sqrt(distSqr);
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
                RemoveSplinters(mesh, cellSize);
                endPhase("cleanup");
            }
            if (reduceTolerance > 0 && cancelF() == false) {
                // reduce; if that pushed the surface through itself somewhere (the contour itself has no
                // self-intersections), reduce again with every collapse checked (slower, so only then)
                DMesh3 contour = new DMesh3(mesh);
                reduce(mesh, reduceTolerance * cellSize, false);
                int after = MeshRepairMetrics.SelfIntersections(mesh);
                if (after > 0 && after > MeshRepairMetrics.SelfIntersections(contour)) {
                    mesh.Copy(contour);
                    reduce(mesh, reduceTolerance * cellSize, true);
                }
                endPhase("reduce");
            }
            return new DMesh3(mesh, true);
        }


        static void reduce(DMesh3 mesh, double tol, bool preventSelfIntersections)
        {
            Reducer reducer = new Reducer(mesh) {
                AreaWeightedQuadrics = false, EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad), MinCollapseTriangleQuality = 0.05,
                PreventSelfIntersections = preventSelfIntersections
            };
            reducer.ReduceToMaxQuadricError(tol * tol);
        }

        /// <summary>
        /// Removes closed components without volume at the resolution: thinner than 2% of a cell on average
        /// (volume / area &lt; 0.01 cell; a slab of thickness t has t / 2) or smaller than 1% of a cell in
        /// volume. They come from nearly coincident surfaces, eg A minus a copy of A turned by a fraction of
        /// a degree, and are noise, not material. Returns the number removed.
        /// </summary>
        public static int RemoveSplinters(DMesh3 mesh, double cellSize)
        {
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            Vector3d center = mesh.CachedBounds.Center;
            int removed = 0;
            foreach (var c in comps.Components) {
                bool closed = true;
                foreach (int t in c.Indices) {
                    Index3i te = mesh.GetTriEdges(t);
                    if (mesh.IsBoundaryEdge(te.a) || mesh.IsBoundaryEdge(te.b) || mesh.IsBoundaryEdge(te.c)) { closed = false; break; }
                }
                if (closed == false)
                    continue;
                Vector2d va = MeshMeasurements.VolumeArea(mesh, c.Indices, (vid) => mesh.GetVertex(vid) - center);
                double vol = Math.Abs(va.x), area = va.y;
                if (vol < 0.01 * cellSize * cellSize * cellSize || vol < 0.01 * cellSize * area) {
                    foreach (int t in c.Indices)
                        mesh.RemoveTriangle(t, true, false);
                    removed++;
                }
            }
            return removed;
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
