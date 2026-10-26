// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> outcome of UVAtlasGenerator </summary>
    public enum UVAtlasStatus
    {
        /// <summary> every chart is within the distortion limits, no overlaps </summary>
        Success = 0,
        /// <summary> no triangles or non-finite vertices </summary>
        InvalidMesh = 1,
        /// <summary> no overlaps, but some charts exceed the distortion limits (MaxCharts reached, or single charts that cannot be split) </summary>
        DistortionLimitExceeded = 2,
        /// <summary> no overlap-free layout was found (should not happen; the UVs are still returned) </summary>
        Failed = 3
    }


    /// <summary>
    /// Builds a UV atlas for an arbitrary mesh (closed or open, any genus, several components): seams or
    /// charts, a parameterization of each chart, packing into the unit square, all checked for overlaps with
    /// exact predicates.
    ///
    /// Per connected component, by Method:
    /// - Cones: BFF with cone singularities (ConeFlattening): one chart cut through automatically placed
    ///   cones. Falls back to Charts if the result overlaps itself.
    /// - Charts: chart segmentation by normals (MeshChartSegmentation), each chart cut into a disc if
    ///   needed (MeshDiscCut) and flattened with BFF (minimal area distortion).
    /// - SingleChart: the component cut into one disc (MeshDiscCut) and flattened with BFF.
    /// - Auto: Cones if that is overlap-free and within the distortion limits, otherwise Charts.
    /// A chart that overlaps itself, has inverted triangles or exceeds MaxAreaDistortion or
    /// MaxConformalDistortion is split in two (MeshChartSegmentation.SplitInTwo) and both parts are flattened
    /// again, while there are fewer than MaxCharts charts. A chart that still overlaps then gets the
    /// flip-free SLIM map (from the Tutte embedding), or the Tutte embedding itself, so the atlas is overlap-free.
    ///
    /// The result is a new mesh, AtlasMesh, with the input's triangles on vertices split along the seams
    /// (AtlasToInputVertex / AtlasToInputTriangle map back) and vertex UVs in [0,1]^2. The final checks
    /// (overlaps, inverted triangles, UV triangles of zero area on non-degenerate surface triangles) are exact on
    /// the double UVs. The UVs stored in AtlasMesh are rounded to float, which moves each by at most half an
    /// ulp; defects that only this rounding causes (tiny or sliver triangles) are counted in
    /// FloatRoundingDefects, not failed.
    /// Status says whether the limits are met; the input is not changed.
    /// </summary>
    public class UVAtlasGenerator
    {
        public enum Methods { Auto = 0, Charts = 1, Cones = 2, SingleChart = 3 }

        public DMesh3 Mesh;
        public Methods Method = Methods.Auto;
        /// <summary> limit per chart: area weighted mean of |log(area ratio)| up to the chart's scale </summary>
        public double MaxAreaDistortion = 0.25;
        /// <summary> limit per chart: area weighted mean of the conformal distortion s1/s2 (1 = conformal) </summary>
        public double MaxConformalDistortion = 1.2;
        /// <summary> charts: largest angle in degrees between a triangle normal and its chart normal </summary>
        public double MaxNormalAngle = 50;
        public int MaxCharts = 128;
        /// <summary> seams prefer sharp edges (MeshDiscCut.FeatureWeight) </summary>
        public double FeatureWeight = 1;
        /// <summary> cones: see ConeFlattening.MaxScaleFactor and MaxCones </summary>
        public double MaxConeScaleFactor = 0.2;
        public int MaxCones = 64;
        /// <summary> gap between charts in the unit square (UVPacker.Padding) </summary>
        public double Padding = 0.005;
        /// <summary> false: no packing, each chart keeps its own UVs (charts overlap each other; Overlap only tests within charts) </summary>
        public bool Pack = true;

        public UVAtlasStatus Status = UVAtlasStatus.Success;
        public string Error;
        /// <summary> the input triangles, split along the seams, with vertex UVs </summary>
        public DMesh3 AtlasMesh;
        /// <summary> UV per vertex id of AtlasMesh </summary>
        public Vector2d[] UV;
        public int[] AtlasToInputVertex;
        public int[] AtlasToInputTriangle;
        /// <summary> chart index per triangle id of AtlasMesh </summary>
        public int[] TriangleChart;
        public int ChartCount;
        /// <summary> charts that exceed the distortion limits </summary>
        public int ChartsOverLimit;
        /// <summary> cone singularities used (Cones and Auto) </summary>
        public int ConeCount;
        /// <summary> components flattened with cones </summary>
        public int ConeComponents;
        /// <summary> total length of the seams (input edges that became boundary edges) </summary>
        public double SeamLength;
        /// <summary> UV triangles of zero area whose surface triangle is not degenerate (0 unless Status is Failed) </summary>
        public int CollapsedTriangleCount;
        /// <summary>
        /// the same test of the UVs as stored in AtlasMesh (rounded to float). Rounding moves each UV by at most
        /// half a float ulp (6e-8 in [0,1]), so its overlaps are at most that deep; they are reported, not failed.
        /// </summary>
        public UVOverlap StoredOverlap;
        /// <summary>
        /// overlapping pairs, inverted and collapsed triangles that appear only in the float UVs (rounding of
        /// tiny or sliver triangles, eg vertices 1e-7 apart); reported only
        /// </summary>
        public int FloatRoundingDefects;
        /// <summary> exact overlap test of the final UVs (double, as in UV) </summary>
        public UVOverlap Overlap;
        /// <summary> distortion of the final UVs (charts scaled to their surface area) </summary>
        public ParameterizationDistortion Distortion;
        /// <summary> fraction of the unit square covered (after packing) </summary>
        public double Coverage;
        /// <summary> per chart: how it was flattened and its distortion </summary>
        public List<ChartInfo> Charts = new List<ChartInfo>();

        public class ChartInfo
        {
            public int Triangles;
            /// <summary> "cones", "bff", "lscm", "slim", "tutte" or "line" (a chart of degenerate triangles only) </summary>
            public string Method;
            public int Cones;
            public ParameterizationDistortion Distortion;
            public bool WithinLimits;
        }

        public UVAtlasGenerator(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Succeeded { get { return Status == UVAtlasStatus.Success; } }

        class Chart
        {
            public DMesh3 Disc;
            public Vector2d[] UV;           // by vertex id of Disc
            public int[] ToWorkV;           // vertex id of Disc -> vertex id of the work mesh
            public int[] ToWorkT;
            public ChartInfo Info = new ChartInfo();
            public bool Injective;
        }

        DMesh3 work;
        List<int[]> discToAtlasT;
        int[] workToV, workToT;
        List<Chart> charts;

        bool fail(UVAtlasStatus status, string error)
        {
            Status = status; Error = error;
            return false;
        }

        public bool Compute()
        {
            Status = UVAtlasStatus.Success; Error = null; AtlasMesh = null; UV = null;
            ChartCount = ChartsOverLimit = ConeCount = ConeComponents = CollapsedTriangleCount = FloatRoundingDefects = 0; SeamLength = 0; Coverage = 0;
            Charts = new List<ChartInfo>();
            Overlap = null; Distortion = null;
            if (Mesh.TriangleCount == 0)
                return fail(UVAtlasStatus.InvalidMesh, "mesh has no triangles");
            foreach (int vid in Mesh.VertexIndices()) {
                Vector3d p = Mesh.GetVertex(vid);
                if (!MathUtil.IsFinite(p.x) || !MathUtil.IsFinite(p.y) || !MathUtil.IsFinite(p.z))
                    return fail(UVAtlasStatus.InvalidMesh, "vertex " + vid + " is not finite");
            }
            work = MeshDiscCut.SplitAlongEdges(Mesh, null, out workToV, out workToT);
            if (work == null)
                return fail(UVAtlasStatus.InvalidMesh, "could not split the bowtie vertices");
            charts = new List<Chart>();

            List<List<int>> components = triangleComponents(work);
            for (int ci = 0; ci < components.Count; ++ci) {
                List<int> comp = components[ci];
                int budget = Math.Max(1, MaxCharts - charts.Count - (components.Count - ci - 1));
                if (Method == Methods.SingleChart) {
                    Chart c = flattenDisc(comp);
                    if (c == null)
                        return fail(UVAtlasStatus.Failed, Error);
                    if (!c.Injective) c = makeInjective(comp, c);
                    if (c == null)
                        return fail(UVAtlasStatus.Failed, Error);
                    charts.Add(c);
                    continue;
                }
                if (Method == Methods.Cones || Method == Methods.Auto) {
                    Chart c = flattenCones(comp);
                    if (c != null && c.Injective && (Method == Methods.Cones || c.Info.WithinLimits)) {
                        charts.Add(c);
                        ConeComponents++;
                        ConeCount += c.Info.Cones;
                        continue;
                    }
                }
                if (!flattenCharts(comp, budget))
                    return fail(UVAtlasStatus.Failed, Error);
            }
            assemble();
            return Status == UVAtlasStatus.Success;
        }


        // segmentation, then each chart flattened and split while it is not good enough
        bool flattenCharts(List<int> comp, int budget)
        {
            int[] labels;
            double maxAngle;
            int n = MeshChartSegmentation.Segment(work, comp, MaxNormalAngle, budget, 0.1, 4, 1, out labels, out maxAngle);
            var regions = new List<int>[n];
            for (int k = 0; k < n; ++k) regions[k] = new List<int>();
            for (int i = 0; i < comp.Count; ++i) regions[labels[i]].Add(comp[i]);
            var queue = new Queue<List<int>>();
            foreach (var r in regions) if (r.Count > 0) queue.Enqueue(r);
            int accepted = 0;
            while (queue.Count > 0) {
                List<int> region = queue.Dequeue();
                Chart c = flattenDisc(region);
                // BFF is conformal; where it folds (eg at sliver triangles) the flip-free SLIM may be good enough
                if (c != null && !c.Injective) {
                    Chart s = slim(c);
                    if (s != null && s.Info.WithinLimits) c = s;
                }
                if (c != null && c.Info.WithinLimits) {
                    charts.Add(c); accepted++;
                    continue;
                }
                if (region.Count > 1 && accepted + queue.Count + 2 <= budget) {
                    int[] split;
                    if (MeshChartSegmentation.SplitInTwo(work, region, 0.1, out split) == 2) {
                        var a = new List<int>(); var b = new List<int>();
                        for (int i = 0; i < region.Count; ++i) (split[i] == 0 ? a : b).Add(region[i]);
                        queue.Enqueue(a); queue.Enqueue(b);
                        continue;
                    }
                }
                if (c == null || !c.Injective)
                    c = makeInjective(region, c);
                if (c == null)
                    return false;
                charts.Add(c); accepted++;
            }
            return true;
        }

        // the triangles (of the work mesh) as a new mesh
        DMesh3 extract(IList<int> tris, out int[] toWorkV, out int[] toWorkT)
        {
            var sub = new DMesh3();
            var map = new Dictionary<int, int>();
            var tv = new List<int>();
            toWorkT = new int[tris.Count];
            for (int k = 0; k < tris.Count; ++k) {
                Index3i t = work.GetTriangle(tris[k]);
                Index3i nt = Index3i.Zero;
                for (int j = 0; j < 3; ++j) {
                    int v;
                    if (!map.TryGetValue(t[j], out v)) {
                        v = sub.AppendVertex(work.GetVertex(t[j]));
                        map[t[j]] = v; tv.Add(t[j]);
                    }
                    nt[j] = v;
                }
                sub.AppendTriangle(nt);
                toWorkT[k] = tris[k];
            }
            toWorkV = tv.ToArray();
            return sub;
        }

        // cut the region into a disc, flatten with BFF (LSCM if BFF fails)
        Chart flattenDisc(IList<int> tris)
        {
            int[] subToWorkV, subToWorkT;
            DMesh3 sub = extract(tris, out subToWorkV, out subToWorkT);
            var cut = new MeshDiscCut(sub) { FeatureWeight = FeatureWeight };
            if (!cut.Compute()) {
                Error = "cutting a chart into a disc failed: " + cut.Error;
                return null;
            }
            DMesh3 disc = cut.CutMesh;
            if (allDegenerate(disc)) {
                // nothing to flatten (eg a stray collinear triangle): the vertices on a line, which the final
                // checks accept because the surface triangles are degenerate too
                Chart lc = makeChart(disc, cut.CutToInputVertex, cut.CutToInputTriangle, subToWorkV, subToWorkT);
                evaluate(lc, lineLayout(disc), "line");
                return lc;
            }
            ParameterizationResult r = new BoundaryFirstFlattening(disc) { ScaleToSurfaceArea = true }.Compute();
            string method = "bff";
            if (!r.Succeeded) {
                r = new LSCMParameterization(disc).Compute();
                method = "lscm";
            }
            var c = makeChart(disc, cut.CutToInputVertex, cut.CutToInputTriangle, subToWorkV, subToWorkT);
            if (!r.Succeeded) {
                c.Info.Method = method;
                return c;   // UV null: not injective, gets the fallback
            }
            evaluate(c, r, method);
            return c;
        }

        static bool allDegenerate(DMesh3 mesh)
        {
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                if (!new TriangleFrame(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c)).Degenerate)
                    return false;
            }
            return true;
        }

        // UVs of a chart of degenerate triangles: the positions along the longest axis of the bounding box
        static ParameterizationResult lineLayout(DMesh3 mesh)
        {
            AxisAlignedBox3d box = mesh.GetBounds();
            int axis = box.Width >= box.Height ? (box.Width >= box.Depth ? 0 : 2) : (box.Height >= box.Depth ? 1 : 2);
            Vector2d[] uv = new Vector2d[mesh.MaxVertexID];
            foreach (int v in mesh.VertexIndices())
                uv[v] = new Vector2d(mesh.GetVertex(v)[axis] - box.Min[axis], 0);
            return new ParameterizationResult() { UV = uv, Distortion = ParameterizationDistortion.Compute(mesh, uv) };
        }

        Chart flattenCones(IList<int> tris)
        {
            int[] subToWorkV, subToWorkT;
            DMesh3 sub = extract(tris, out subToWorkV, out subToWorkT);
            var cf = new ConeFlattening(sub) { FeatureWeight = FeatureWeight, MaxScaleFactor = MaxConeScaleFactor, MaxCones = MaxCones };
            ParameterizationResult r = cf.Compute();
            if (!r.Succeeded)
                return null;
            var c = makeChart(cf.CutMesh, cf.Cut.CutToInputVertex, cf.Cut.CutToInputTriangle, subToWorkV, subToWorkT);
            evaluate(c, r, "cones");
            c.Info.Cones = cf.ConeVertices.Count;
            return c;
        }

        // the chart's disc flattened with SLIM (from the Tutte embedding), null if SLIM fails
        Chart slim(Chart c)
        {
            ParameterizationResult r = new SLIMParameterization(c.Disc).Compute();
            if (!r.Succeeded) return null;
            var s = new Chart() { Disc = c.Disc, ToWorkV = c.ToWorkV, ToWorkT = c.ToWorkT };
            s.Info.Triangles = c.Info.Triangles;
            evaluate(s, r, "slim");
            return s;
        }

        // flip-free fallback: SLIM from the Tutte embedding, or the Tutte embedding (convex boundary: injective)
        Chart makeInjective(IList<int> tris, Chart c)
        {
            if (c == null) {
                int[] subToWorkV, subToWorkT;
                DMesh3 sub = extract(tris, out subToWorkV, out subToWorkT);
                var cut = new MeshDiscCut(sub) { FeatureWeight = FeatureWeight };
                if (!cut.Compute()) {
                    Error = "cutting a chart into a disc failed: " + cut.Error;
                    return null;
                }
                c = makeChart(cut.CutMesh, cut.CutToInputVertex, cut.CutToInputTriangle, subToWorkV, subToWorkT);
            }
            ParameterizationResult r = new SLIMParameterization(c.Disc).Compute();
            if (r.Succeeded) {
                evaluate(c, r, "slim");
                if (c.Injective) return c;
            }
            r = new TutteEmbedding(c.Disc).Compute();
            if (!r.Succeeded) {
                Error = "no injective parameterization of a chart: " + r.Error;
                return null;
            }
            evaluate(c, r, "tutte");
            if (!c.Injective) {
                Error = "no injective parameterization of a chart (Tutte embedding not injective, eg degenerate surface triangles)";
                return null;
            }
            return c;
        }

        Chart makeChart(DMesh3 disc, int[] discToSubV, int[] discToSubT, int[] subToWorkV, int[] subToWorkT)
        {
            var c = new Chart() { Disc = disc };
            c.ToWorkV = new int[disc.MaxVertexID];
            for (int v = 0; v < c.ToWorkV.Length; ++v) c.ToWorkV[v] = subToWorkV[discToSubV[v]];
            c.ToWorkT = new int[disc.MaxTriangleID];
            for (int t = 0; t < c.ToWorkT.Length; ++t) c.ToWorkT[t] = subToWorkT[discToSubT[t]];
            c.Info.Triangles = disc.TriangleCount;
            return c;
        }

        void evaluate(Chart c, ParameterizationResult r, string method)
        {
            c.UV = r.UV;
            c.Info.Method = method;
            c.Info.Distortion = r.Distortion;
            UVOverlap ov = UVOverlap.Compute(c.Disc, r.UV);
            // UV triangles may be degenerate only where the surface triangle is
            c.Injective = ov.OverlappingPairs == 0 && ov.InvertedTriangles == 0 && CollapsedTriangles(c.Disc, ov) == 0;
            c.Info.WithinLimits = c.Injective && r.Distortion.Triangles > 0
                ? r.Distortion.MeanAreaDistortion <= MaxAreaDistortion && r.Distortion.MeanConformal <= MaxConformalDistortion
                : c.Injective;
        }

        /// <summary>
        /// UV-degenerate triangles of the overlap test whose surface triangle is not degenerate (a chart, or
        /// part of it, flattened to a line)
        /// </summary>
        public static int CollapsedTriangles(DMesh3 mesh, UVOverlap overlap)
        {
            int n = 0;
            foreach (int tid in overlap.DegenerateTriangleIds) {
                Index3i t = mesh.GetTriangle(tid);
                if (!new TriangleFrame(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c)).Degenerate)
                    n++;
            }
            return n;
        }

        void assemble()
        {
            var atlas = new DMesh3(Mesh.HasVertexNormals, Mesh.HasVertexColors, true, Mesh.HasTriangleGroups);
            var uv = new List<Vector2d>();
            var toV = new List<int>();
            var toT = new List<int>();
            var chartOf = new List<int>();
            discToAtlasT = new List<int[]>();
            for (int ci = 0; ci < charts.Count; ++ci) {
                Chart c = charts[ci];
                int[] map = new int[c.Disc.MaxVertexID];
                int[] tmap = new int[c.Disc.MaxTriangleID];
                discToAtlasT.Add(tmap);
                foreach (int v in c.Disc.VertexIndices()) {
                    map[v] = atlas.AppendVertex(work, c.ToWorkV[v]);
                    uv.Add(c.UV[v]);
                    toV.Add(workToV[c.ToWorkV[v]]);
                }
                foreach (int t in c.Disc.TriangleIndices()) {
                    Index3i tri = c.Disc.GetTriangle(t);
                    int wt = c.ToWorkT[t];
                    tmap[t] = atlas.AppendTriangle(new Index3i(map[tri.a], map[tri.b], map[tri.c]), Mesh.HasTriangleGroups ? work.GetTriangleGroup(wt) : -1);
                    toT.Add(workToT[wt]);
                    chartOf.Add(ci);
                }
                Charts.Add(c.Info);
                if (!c.Info.WithinLimits) ChartsOverLimit++;
            }
            AtlasMesh = atlas;
            AtlasToInputVertex = toV.ToArray();
            AtlasToInputTriangle = toT.ToArray();
            TriangleChart = chartOf.ToArray();
            ChartCount = charts.Count;
            UV = uv.ToArray();

            if (Pack) {
                var packer = new UVPacker(atlas) { UV = UV, Padding = Padding, ScaleChartsToSurfaceArea = true };
                ParameterizationResult pr = packer.Compute();
                if (pr.Succeeded) {
                    UV = pr.UV;
                    Coverage = packer.Coverage;
                }
            }
            foreach (int v in atlas.VertexIndices())
                atlas.SetVertexUV(v, (Vector2f)UV[v]);

            // seams: atlas boundary edges on interior input edges (each seam edge has two sides)
            foreach (int eid in atlas.BoundaryEdgeIndices()) {
                Index2i ev = atlas.GetEdgeV(eid);
                int ie = Mesh.FindEdge(AtlasToInputVertex[ev.a], AtlasToInputVertex[ev.b]);
                if (ie != DMesh3.InvalidID && !Mesh.IsBoundaryEdge(ie))
                    SeamLength += 0.5 * atlas.GetVertex(ev.a).Distance(atlas.GetVertex(ev.b));
            }

            Distortion = ParameterizationDistortion.Compute(atlas, UV);
            if (Pack) {
                Overlap = UVOverlap.Compute(atlas, UV);
                StoredOverlap = UVOverlap.Compute(atlas, storedUV(UV));
            } else {
                // charts are not placed: test each chart by itself, with the triangle ids of AtlasMesh
                Overlap = new UVOverlap();
                StoredOverlap = new UVOverlap();
                for (int ci = 0; ci < charts.Count; ++ci) {
                    Chart c = charts[ci];
                    merge(Overlap, UVOverlap.Compute(c.Disc, c.UV), discToAtlasT[ci]);
                    merge(StoredOverlap, UVOverlap.Compute(c.Disc, storedUV(c.UV)), discToAtlasT[ci]);
                }
            }
            CollapsedTriangleCount = CollapsedTriangles(atlas, Overlap);
            FloatRoundingDefects = StoredOverlap.OverlappingPairs + StoredOverlap.InvertedTriangles + CollapsedTriangles(atlas, StoredOverlap);
            bool overflow = false;
            foreach (int v in atlas.VertexIndices()) {
                Vector2f f = atlas.GetVertexUV(v);
                if (!MathUtil.IsFinite(f.x) || !MathUtil.IsFinite(f.y)) overflow = true;
            }
            if (Overlap.OverlappingPairs > 0 || Overlap.InvertedTriangles > 0 || CollapsedTriangleCount > 0 || overflow) {
                Status = UVAtlasStatus.Failed;
                Error = Overlap.OverlappingPairs + " overlapping UV triangle pairs, " + Overlap.InvertedTriangles + " inverted UV triangles, "
                    + CollapsedTriangleCount + " UV triangles of zero area on non-degenerate surface triangles"
                    + (overflow ? ", UVs out of the float range" : "");
            } else if (ChartsOverLimit > 0) {
                Status = UVAtlasStatus.DistortionLimitExceeded;
                Error = ChartsOverLimit + " of " + ChartCount + " charts exceed the distortion limits";
            }
        }

        static Vector2d[] storedUV(Vector2d[] uv)
        {
            Vector2d[] f = new Vector2d[uv.Length];
            for (int v = 0; v < uv.Length; ++v) f[v] = (Vector2d)(Vector2f)uv[v];
            return f;
        }

        // add the results of a chart's test, with its triangle ids mapped to AtlasMesh
        static void merge(UVOverlap into, UVOverlap from, int[] toAtlasT)
        {
            into.OverlappingPairs += from.OverlappingPairs;
            into.InvertedTriangles += from.InvertedTriangles;
            into.DegenerateTriangles += from.DegenerateTriangles;
            foreach (Index2i p in from.Pairs)
                if (into.Pairs.Count < into.MaxReportedPairs) {
                    int a = toAtlasT[p.a], b = toAtlasT[p.b];
                    into.Pairs.Add(new Index2i(Math.Min(a, b), Math.Max(a, b)));
                }
            foreach (int t in from.InvertedTriangleIds) into.InvertedTriangleIds.Add(toAtlasT[t]);
            foreach (int t in from.DegenerateTriangleIds) into.DegenerateTriangleIds.Add(toAtlasT[t]);
        }

        static List<List<int>> triangleComponents(DMesh3 mesh)
        {
            var comps = new List<List<int>>();
            int[] label = new int[mesh.MaxTriangleID];
            for (int i = 0; i < label.Length; ++i) label[i] = -1;
            foreach (int seed in mesh.TriangleIndices()) {
                if (label[seed] >= 0) continue;
                var list = new List<int>();
                var stack = new Stack<int>();
                stack.Push(seed); label[seed] = comps.Count;
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    list.Add(t);
                    Index3i nb = mesh.GetTriNeighbourTris(t);
                    for (int j = 0; j < 3; ++j)
                        if (nb[j] != DMesh3.InvalidID && label[nb[j]] < 0) { label[nb[j]] = comps.Count; stack.Push(nb[j]); }
                }
                list.Sort();
                comps.Add(list);
            }
            return comps;
        }
    }
}
