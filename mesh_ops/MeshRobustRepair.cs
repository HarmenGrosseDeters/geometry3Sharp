// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Repair that ends with a clean solid wherever possible: MeshAutoRepair (with the post-repair
    /// cleanup) first, which keeps the input triangles; if its result is not clean, its self-intersections
    /// are resolved exactly (ExactResolve: ExactMeshBoolean.Resolve, which keeps the geometry); if that is not
    /// clean either, the repaired mesh is solidified (winding number, adaptive dual contouring), which gives a
    /// closed, manifold surface without self-intersections at the cost of remeshing it (deviation about a
    /// cell, see SolidifyResolution). If the solidify fails or its result is not clean either (rare:
    /// self-intersections where thin sheets meet in few cells), the mesh is alpha wrapped (MeshAlphaWrap,
    /// AlphaWrapFallback), whose result is always closed, manifold and free of self-intersections, but wraps
    /// over cavities and gaps narrower than about 2 alpha and lies about Offset outside the input.
    ///
    /// Clean means: closed, no bowties, no degenerate triangles, no inside-out components and, if
    /// CheckSelfIntersections, no self-intersections (exact, MeshRepairMetrics).
    ///
    /// A clean result can still have lost volume (eg a self-overlapping input made non-overlapping by
    /// dropping a layer): if its solid agrees with the input's winding number solid less than MinVolumeIoU,
    /// the input itself is used instead, resolved exactly or solidified (its winding number solid is the reference).
    ///
    /// Behaviour: the result is in <see cref="Mesh"/> after Apply(), a new mesh object. The mesh passed to the
    /// constructor is used as the working mesh of MeshAutoRepair and is modified by it, so pass a copy if
    /// you need the input afterwards. Check UsedExactResolve / UsedSolidify / UsedAlphaWrap / FallbackReason (and
    /// ExactResolveReason, SolidifyReason) to see which path was taken.
    /// Cost: the self-intersection check and the volume comparison are cheap compared to the solidify
    /// fallback, whose cost grows with SolidifyResolution.
    ///
    /// Use MeshAutoRepair alone if the input triangles must be kept (no remeshing), MeshWindingNumberSolidify
    /// to always remesh, ExactMeshBoolean.Resolve to resolve self-intersections exactly without remeshing.
    /// </summary>
    public class MeshRobustRepair
    {
        /// <summary> input mesh; after a successful Apply() the repaired (or solidified) result </summary>
        public DMesh3 Mesh;
        /// <summary> cells along the bounding box diagonal for the solidify fallback </summary>
        public int SolidifyResolution = 128;
        /// <summary> if true, self-intersections make the repair result unclean (triggers the solidify fallback) </summary>
        public bool CheckSelfIntersections = true;
        /// <summary> minimum volume IoU of the repaired mesh with the input's winding number solid (0 = no check) </summary>
        public double MinVolumeIoU = 0.99;
        /// <summary> configures the first stage (eg tolerances) </summary>
        public Action<MeshAutoRepair> ConfigureRepair = null;
        /// <summary> replaces the first stage (input -> repaired mesh); null = MeshAutoRepair </summary>
        public Func<DMesh3, DMesh3> RepairF = null;
        /// <summary> return true to cancel (checked after the repair stage and during solidify) </summary>
        public Func<bool> CancelF = () => { return false; };
        /// <summary>
        /// before solidifying: resolve the self-intersections exactly (the solid where the winding number is
        /// positive, ExactMeshBoolean.Resolve); used if the result is clean and its volume agrees with the input.
        /// Only for meshes that are closed in the winding number sense (exact winding numbers).
        /// </summary>
        public bool ExactResolve = true;
        /// <summary> rounding the new vertices to double can create tiny self-intersections: resolve again, up to this often </summary>
        public int ExactResolveRounds = 4;
        /// <summary>
        /// the exact resolve is not tried for meshes with more self-intersecting triangle pairs than this (its time
        /// grows with them; a heavily overlapping mesh is solidified instead); 0 = no limit
        /// </summary>
        public int ExactResolveMaxPairs = 200000;
        /// <summary> last resort: alpha wrap the mesh if the solidify fails or its result is not clean </summary>
        public bool AlphaWrapFallback = true;
        /// <summary> alpha wrap fallback: alpha = bounding box diagonal / WrapRelativeAlpha </summary>
        public double WrapRelativeAlpha = 300;
        /// <summary> alpha wrap fallback: offset = bounding box diagonal / WrapRelativeOffset </summary>
        public double WrapRelativeOffset = 3000;

        // outputs
        /// <summary> true if the result was produced by the solidify fallback (remeshed), false if it is the repair result </summary>
        public bool UsedSolidify;
        /// <summary> why the repair result was not used ("" if it was) </summary>
        public string FallbackReason = "";
        /// <summary>
        /// volume IoU of the repair result with the input (NaN if the input has no solid or not checked); an
        /// estimate from as many samples as the check needed (MeshRepairMetrics.VolumeIoUAtLeast)
        /// </summary>
        public double RepairVolumeIoU = double.NaN;
        /// <summary> the solidify fallback, if it ran (for its statistics); null otherwise </summary>
        public MeshWindingNumberSolidify Solidify;
        /// <summary> why the solidify result was not used ("" if it was or did not run) </summary>
        public string SolidifyReason = "";
        /// <summary> true if the result is the alpha wrap (last resort) </summary>
        public bool UsedAlphaWrap;
        /// <summary> the alpha wrap fallback, if it ran; null otherwise </summary>
        public MeshAlphaWrap AlphaWrap;
        public bool UsedExactResolve;
        /// <summary> why the exact resolve was not used ("" if it was or was not tried) </summary>
        public string ExactResolveReason = "";
        /// <summary> resolves done by the exact stage (0 if not tried) </summary>
        public int ExactResolveRoundsUsed;
        /// <summary> per resolve round: self-intersecting triangle pairs of its result (diagnostics) </summary>
        public List<int> ExactResolveSelfIntersections = new List<int>();
        /// <summary>
        /// milliseconds per phase of the last Apply() (summed if a phase runs more than once): "repair", "check"
        /// (cleanness incl. self-intersections), "volume" (VolumeIoU), "exact" (exact resolve incl. its checks;
        /// parts of it in "exact.orient", "exact.closed", "exact.resolve", "exact.check", "exact.volume"),
        /// "solidify", "solidify." + the phases of MeshWindingNumberSolidify, "solidify.check" (cleanness of
        /// its result) and "wrap" (alpha wrap fallback)
        /// </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        Stopwatch phase_timer = new Stopwatch();
        void end_phase(string name)
        {
            add_phase(name, phase_timer.ElapsedMilliseconds);
            phase_timer.Restart();
        }
        void add_phase(string name, long ms)
        {
            long cur;
            PhaseMilliseconds.TryGetValue(name, out cur);
            PhaseMilliseconds[name] = cur + ms;
        }

        /// <summary> the mesh is modified by the repair stage (see class comment) </summary>
        public MeshRobustRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// Repair, check, and solidify if needed (see class comment). On success Mesh is the result.
        /// </summary>
        /// <returns>
        /// false if cancelled, if the mesh to solidify is empty, or if both the solidify fallback (eg the grid
        /// would be too large) and the alpha wrap failed; Mesh is then not replaced. true otherwise.
        /// </returns>
        public bool Apply()
        {
            UsedSolidify = false;
            UsedAlphaWrap = false;
            SolidifyReason = "";
            Solidify = null;
            AlphaWrap = null;
            UsedExactResolve = false;
            ExactResolveReason = "";
            ExactResolveRoundsUsed = 0;
            ExactResolveSelfIntersections.Clear();
            FallbackReason = "";
            RepairVolumeIoU = double.NaN;
            PhaseMilliseconds.Clear();
            phase_timer.Restart();
            DMesh3 input = new DMesh3(Mesh);
            DMesh3 result;
            if (RepairF != null) {
                result = RepairF(Mesh);
            } else {
                var repair = new MeshAutoRepair(Mesh) { PostRepairCleanup = true };
                ConfigureRepair?.Invoke(repair);
                repair.Apply();
                result = repair.Mesh;
            }
            end_phase("repair");
            if (CancelF())
                return false;
            FallbackReason = NotCleanReason(result, CheckSelfIntersections);
            end_phase("check");
            DMesh3 solidifyFrom = result;
            if (FallbackReason == "" && MinVolumeIoU > 0) {
                // (NaN: the input has no solid to compare with, accepted)
                if (MeshRepairMetrics.VolumeIoUAtLeast(input, result, MinVolumeIoU, out RepairVolumeIoU) == false) {
                    FallbackReason = "volume differs from the input";
                    solidifyFrom = input;
                }
                end_phase("volume");
            }
            if (FallbackReason == "") {
                Mesh = result;
                return true;
            }
            if (solidifyFrom.TriangleCount == 0)
                return false;
            if (ExactResolve) {
                DMesh3 exact = resolve_exactly(result, input, false);
                // its volume may differ only because components are oriented differently from the input (eg a
                // nested part taken for a cavity): then resolve it again, oriented by the input's solid
                if (exact == null && ExactResolveReason == "volume differs from the input" && CancelF() == false)
                    exact = resolve_exactly(result, input, true);
                // a clean result whose volume differs: also the input itself
                if (exact == null && solidifyFrom != result && CancelF() == false) {
                    string reason = ExactResolveReason;
                    exact = resolve_exactly(solidifyFrom, input, false);
                    if (exact == null)
                        ExactResolveReason = reason + "; input: " + ExactResolveReason;
                }
                end_phase("exact");
                if (CancelF())
                    return false;
                if (exact != null) {
                    Mesh = exact;
                    UsedExactResolve = true;
                    return true;
                }
            }
            Solidify = new MeshWindingNumberSolidify(solidifyFrom) { GridResolution = SolidifyResolution, Adaptive = true, CancelF = CancelF };
            bool solidified = Solidify.Compute() && Solidify.Result != null && Solidify.Result.TriangleCount > 0;
            end_phase("solidify");
            foreach (var kv in Solidify.PhaseMilliseconds) PhaseMilliseconds["solidify." + kv.Key] = kv.Value;
            if (CancelF())
                return false;
            SolidifyReason = solidified ? NotCleanReason(Solidify.Result, CheckSelfIntersections) : "solidify failed";
            end_phase("solidify.check");
            if (SolidifyReason != "" && AlphaWrapFallback) {
                AlphaWrap = new MeshAlphaWrap(solidifyFrom) { RelativeAlpha = WrapRelativeAlpha, RelativeOffset = WrapRelativeOffset, CancelF = CancelF };
                AlphaWrap.Compute();
                end_phase("wrap");
                // (PointLimitReached: less detail, same guarantees)
                if ((AlphaWrap.Status == MeshAlphaWrap.WrapStatus.Success || AlphaWrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached)
                    && AlphaWrap.Result != null && AlphaWrap.Result.TriangleCount > 0) {
                    Mesh = AlphaWrap.Result;
                    UsedAlphaWrap = true;
                    return true;
                }
            }
            if (solidified == false)
                return false;
            Mesh = Solidify.Result;
            UsedSolidify = true;
            return true;
        }

        // the solid of the mesh with its self-intersections resolved exactly, or null (ExactResolveReason says why)
        DMesh3 resolve_exactly(DMesh3 from, DMesh3 input, bool orientByInput)
        {
            var timer = Stopwatch.StartNew();
            // inside-out parts flipped, so that the solid is where the winding number is positive (guessed from
            // the nesting of the components, or from the input's solid)
            DMesh3 m = orientByInput ? OrientedByReference(from, input) : WindingContouring.OrientedCopy(from);
            add_phase("exact.orient", timer.ElapsedMilliseconds); timer.Restart();
            bool closed = ExactMeshBoolean.IsWindingClosed(m);
            add_phase("exact.closed", timer.ElapsedMilliseconds); timer.Restart();
            if (closed == false) {
                ExactResolveReason = "not closed (winding number)";
                return null;
            }
            if (ExactResolveMaxPairs > 0) {
                int pairs = MeshRepairMetrics.SelfIntersections(m);
                if (pairs > ExactResolveMaxPairs) {
                    ExactResolveReason = "too many self-intersections";
                    return null;
                }
            }
            for (int round = 0; round < ExactResolveRounds; ++round) {
                if (CancelF())
                    return null;
                timer.Restart();
                DMesh3 res = ExactMeshBoolean.Resolve(m);
                add_phase("exact.resolve", timer.ElapsedMilliseconds); timer.Restart();
                ExactResolveRoundsUsed++;
                if (res == null || res.TriangleCount == 0) {
                    ExactResolveReason = "resolve failed";
                    return null;
                }
                // slivers where intersection curves nearly meet: collapsed or flipped by the cleanup
                string reason = NotCleanReason(res, false);
                if (reason == "degenerate triangles") {
                    new MeshPostRepairCleanup(res).Apply();
                    res = new DMesh3(res, true);
                }
                // where solids touch (at a vertex or along an edge) Resolve gives manifold sheets with duplicated
                // vertices, which touch: separate them minimally
                SeparateTouchingVertices(res, SeparateFraction);
                reason = NotCleanReason(res, false);
                if (reason == "" && CheckSelfIntersections) {
                    int si = MeshRepairMetrics.SelfIntersections(res);
                    ExactResolveSelfIntersections.Add(si);
                    if (si > 0)
                        reason = "self-intersections";
                }
                add_phase("exact.check", timer.ElapsedMilliseconds); timer.Restart();
                if (reason == "self-intersections") {
                    // from rounding the new vertices (or the cleanup): tiny, resolved by the next round
                    ExactResolveReason = reason;
                    m = res;
                    continue;
                }
                if (reason != "") {
                    ExactResolveReason = reason;
                    return null;
                }
                if (MinVolumeIoU > 0) {
                    double iou;
                    bool agrees = MeshRepairMetrics.VolumeIoUAtLeast(input, res, MinVolumeIoU, out iou);
                    add_phase("exact.volume", timer.ElapsedMilliseconds);
                    if (agrees == false) {
                        ExactResolveReason = "volume differs from the input";
                        return null;
                    }
                    RepairVolumeIoU = iou;
                }
                ExactResolveReason = "";
                return res;
            }
            return null;
        }

        /// <summary>
        /// Compacted copy of mesh with inside-out parts flipped (WindingContouring.OrientedCopy), then each
        /// closed component oriented by the solid of reference (where its generalized winding number is
        /// > 0.5), sampled just off both sides of its triangles: reversed if the reference has its solid on
        /// the outer side and none on the inner (OrientedCopy guesses a cavity or an inverted part from the
        /// nesting alone, eg makes a part nested in another one a cavity), and turned outwards if the
        /// reference is solid on both sides (an inner shell of a solid, not a cavity).
        /// </summary>
        public static DMesh3 OrientedByReference(DMesh3 mesh, DMesh3 reference)
        {
            DMesh3 m = WindingContouring.OrientedCopy(mesh);
            if (reference == null || reference.TriangleCount == 0)
                return m;
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            DMeshAABBTree3 tree = null;
            foreach (var comp in comps.Components) {
                int[] tris = comp.Indices;
                bool closed = tris.All(t => {
                    Index3i te = m.GetTriEdges(t);
                    return !m.IsBoundaryEdge(te.a) && !m.IsBoundaryEdge(te.b) && !m.IsBoundaryEdge(te.c);
                });
                if (closed == false)
                    continue;
                if (tree == null) {
                    tree = new DMeshAABBTree3(WindingContouring.OrientedCopy(reference), true);
                    tree.FastWindingNumber(Vector3d.Zero);
                }
                // area of the sampled triangles with the reference solid only on the inner side, only on the
                // outer side, on both
                double inner = 0, outer = 0, both = 0, total = 0;
                int samples = Math.Min(200, tris.Length);
                for (int k = 0; k < samples; ++k) {
                    int tid = tris[(int)((long)k * tris.Length / samples)];
                    Vector3d n, c;
                    double area;
                    m.GetTriInfo(tid, out n, out area, out c);
                    if (area <= 0 || n.IsNormalized == false)
                        continue;
                    double eps = 0.01 * Math.Sqrt(area);
                    bool sIn = tree.FastWindingNumber(c - eps * n) > 0.5, sOut = tree.FastWindingNumber(c + eps * n) > 0.5;
                    total += area;
                    if (sIn && sOut) both += area;
                    else if (sIn) inner += area;
                    else if (sOut) outer += area;
                }
                bool flip = false;
                if (outer > inner && outer > 0.5 * total)
                    flip = true;
                else if (both > 0.5 * total && inner < 0.25 * total && outer < 0.25 * total)
                    flip = MeshMeasurements.VolumeArea(m, tris, m.GetVertex).x < 0;
                if (flip)
                    foreach (int tid in tris)
                        m.ReverseTriOrientation(tid);
            }
            return m;
        }

        /// <summary> how far SeparateTouchingVertices moves a vertex, relative to the distance to the centroid of its neighbours </summary>
        public double SeparateFraction = 1e-4;

        /// <summary>
        /// Vertices at the same position (eg from splitting a non-manifold vertex or edge into manifold sheets)
        /// make their sheets touch there. Each is moved by fraction of the way towards the centroid of its
        /// neighbours, so the sheets separate. Returns the number of vertices moved.
        /// </summary>
        public static int SeparateTouchingVertices(DMesh3 mesh, double fraction)
        {
            var at = new Dictionary<Vector3d, List<int>>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                List<int> list;
                if (at.TryGetValue(v, out list) == false)
                    at[v] = list = new List<int>();
                list.Add(vid);
            }
            var moves = new List<KeyValuePair<int, Vector3d>>();
            foreach (var kv in at) {
                if (kv.Value.Count < 2)
                    continue;
                foreach (int vid in kv.Value) {
                    Vector3d c = Vector3d.Zero;
                    int n = 0;
                    foreach (int nb in mesh.VtxVerticesItr(vid)) {
                        c += mesh.GetVertex(nb);
                        n++;
                    }
                    if (n > 0)
                        moves.Add(new KeyValuePair<int, Vector3d>(vid, kv.Key + fraction * (c / n - kv.Key)));
                }
            }
            foreach (var m in moves)
                mesh.SetVertex(m.Key, m.Value);
            return moves.Count;
        }

        /// <summary>
        /// "" if the mesh is clean (see class comment), otherwise the first problem found, one of "empty",
        /// "not closed", "bowties", "degenerate triangles" (area &lt; 1e-12 * diagonal^2), "inside-out components",
        /// "self-intersections". Useful as a test assertion: NotCleanReason(mesh) == "".
        /// </summary>
        public static string NotCleanReason(DMesh3 mesh, bool checkSelfIntersections = true)
        {
            if (mesh.TriangleCount == 0) return "empty";
            if (mesh.IsClosed() == false) return "not closed";
            if (MeshIterators.BowtieVertices(mesh).Any()) return "bowties";
            double diag = mesh.CachedBounds.DiagonalLength;
            double areaEps = 1e-12 * diag * diag;
            if (mesh.TriangleIndices().Any(t => mesh.GetTriArea(t) < areaEps)) return "degenerate triangles";
            if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) return "inside-out components";
            if (checkSelfIntersections && MeshRepairMetrics.HasSelfIntersections(mesh)) return "self-intersections";
            return "";
        }
    }
}
