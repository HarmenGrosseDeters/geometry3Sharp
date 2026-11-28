// Copyright (c) geometry3Sharp contributors. Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1.0.txt
using System;
using System.Collections.Generic;
using System.Threading;
using g3;

namespace gs
{
    /// <summary>
    /// Feature-preserving mitered (point-to-plane) offsetting and open-sheet thickening based on the explicit
    /// per-element offset polyhedra and exact arrangement/winding-number union framework of Cao et al.
    /// (arXiv:2412.15564 / ACM TOG, SIGGRAPH 2026, "Feature-Preserving Offset Meshing").
    ///
    /// Unlike Euclidean offsetting (<see cref="MeshOffset"/>) or alpha wrapping (<see cref="MeshAlphaWrap"/>),
    /// which round convex edges and corners by a sphere of radius Distance, a mitered offset keeps planar faces
    /// planar and extends convex creases and corners to the exact intersection of the shifted face planes
    /// (clamped by <see cref="MiterLimit"/> to prevent unbounded spikes at acute angles).
    ///
    /// Modes (<see cref="OffsetModes"/>):
    ///   - <see cref="OffsetModes.Outward"/>: outward mitered offset of a closed, positively oriented mesh by Distance &gt; 0
    ///     (exact union of the input, per-face outward prisms, convex edge miter wedges, and convex corner hulls).
    ///   - <see cref="OffsetModes.Inward"/>: inward mitered inset of a closed, positively oriented mesh by Distance &gt; 0
    ///     (exact difference of the input minus inward face prisms, reflex edge miter wedges, and reflex corner hulls).
    ///   - <see cref="OffsetModes.DoubleSided"/>: symmetric thickening of an open CAD sheet, shell, or arbitrary triangle
    ///     mesh by +/-Distance (total wall thickness 2 * Distance) into a closed, 2-manifold, self-intersection-free solid
    ///     with crisp planar faces and sharp mitered convex and concave creases.
    ///
    /// Scale invariance: the input is processed in a power-of-two normalized frame centered on the triangle bounding box,
    /// so 2^k scalings of the mesh and Distance produce 2^k times the result bit for bit.
    /// Usage: set the fields, call <see cref="Compute"/>, and read <see cref="Result"/> and <see cref="Status"/>.
    /// <see cref="Compute"/> can be called repeatedly on the same instance after changing parameters.
    /// </summary>
    public class MeshMiteredOffset
    {
        /// <summary> Offset direction / shell mode. </summary>
        public enum OffsetModes
        {
            /// <summary> Outward mitered offset of a closed solid by +Distance. </summary>
            Outward = 0,
            /// <summary> Inward mitered inset of a closed solid by Distance. </summary>
            Inward = 1,
            /// <summary> Symmetric double-sided thickening of an open sheet/shell or soup by +/-Distance (thickness 2 * Distance). </summary>
            DoubleSided = 2
        }

        /// <summary> Result status of <see cref="Compute"/>. </summary>
        public enum OffsetStatus
        {
            /// <summary> <see cref="Result"/> is set: closed, 2-manifold, self-intersection-free solid. </summary>
            Success = 0,
            /// <summary> Null or empty mesh, all degenerate triangles, non-finite coordinates, or extent outside [2^-900, 2^900]. </summary>
            InvalidInput = 1,
            /// <summary> Outward or Inward mode was requested on a mesh that is not closed in the winding sense. </summary>
            NotClosed = 2,
            /// <summary> Outward or Inward mode was requested on a closed mesh with non-positive signed volume. </summary>
            InsideOut = 3,
            /// <summary> Distance &lt;= 0 or non-finite, or MiterLimit &lt; 1 or non-finite. </summary>
            InvalidParameters = 4,
            /// <summary> Inward inset consumed the entire solid cleanly (empty result). </summary>
            Empty = 5,
            /// <summary> Exact arrangement / boolean or safe rounding failed (see <see cref="Error"/>). </summary>
            Failed = 6,
            /// <summary> Cancelled via <see cref="Cancellation"/> or <see cref="CancelF"/>. </summary>
            Cancelled = 7
        }

        /// <summary> Input mesh (never modified). Isolated vertices are ignored. </summary>
        public DMesh3 Mesh;
        /// <summary> Offset mode (default <see cref="OffsetModes.Outward"/>). </summary>
        public OffsetModes Mode = OffsetModes.Outward;
        /// <summary> Positive offset distance (half-thickness in <see cref="OffsetModes.DoubleSided"/> mode). </summary>
        public double Distance;
        /// <summary>
        /// Maximum miter extrusion ratio relative to <see cref="Distance"/> (must be &gt;= 1, default 4.0).
        /// Acute convex creases/corners whose exact miter distance exceeds MiterLimit * Distance are bevel-clamped.
        /// </summary>
        public double MiterLimit = 4.0;
        /// <summary> Safe rounding option forwarded to <see cref="ExactMeshBoolean"/> (default true). </summary>
        public bool SafeRounding = true;
        /// <summary> Optional cancellation token. </summary>
        public CancellationToken Cancellation = CancellationToken.None;
        /// <summary> Optional legacy cancellation callback. </summary>
        public Func<bool> CancelF;

        /// <summary> Result mesh on <see cref="OffsetStatus.Success"/> (empty mesh on <see cref="OffsetStatus.Empty"/>, null on failure). </summary>
        public DMesh3 Result { get; private set; }
        /// <summary> Status of the last <see cref="Compute"/> call. </summary>
        public OffsetStatus Status { get; private set; } = OffsetStatus.InvalidInput;
        /// <summary> Diagnostic message when <see cref="Status"/> is not <see cref="OffsetStatus.Success"/>. </summary>
        public string Error { get; private set; } = "";
        /// <summary> Number of convex offset polyhedra (face prisms, edge wedges, corner hulls) assembled before the exact boolean. </summary>
        public int PolyhedraCount { get; private set; }

        const double MinExtent = 1.1832913578315177e-271; // 2^-900
        const double MaxExtent = 8.4527124981706439e270;  // 2^900

        bool is_cancelled()
        {
            return Cancellation.IsCancellationRequested || (CancelF != null && CancelF());
        }

        OffsetStatus fail(OffsetStatus status, string error)
        {
            Result = null;
            Status = status;
            Error = error ?? "";
            return status;
        }

        /// <summary> Computes the mitered offset or thickened shell. Returns true on Success (or Empty for Inward). </summary>
        public bool Compute()
        {
            Result = null;
            Error = "";
            PolyhedraCount = 0;

            if (is_cancelled()) {
                fail(OffsetStatus.Cancelled, "cancelled");
                return false;
            }
            if (Mesh == null || Mesh.TriangleCount == 0) {
                fail(OffsetStatus.InvalidInput, "mesh is null or empty");
                return false;
            }
            if (!MathUtil.IsFinite(Distance) || !(Distance > 0)) {
                fail(OffsetStatus.InvalidParameters, "Distance must be finite and positive");
                return false;
            }
            if (!MathUtil.IsFinite(MiterLimit) || !(MiterLimit >= 1.0)) {
                fail(OffsetStatus.InvalidParameters, "MiterLimit must be finite and >= 1");
                return false;
            }

            // Collect triangle-referenced vertices, check finiteness, and compute triangle bounding box.
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            var usedVerts = new Dictionary<int, int>();
            var vertsOrig = new List<Vector3d>();
            var tris = new List<Index3i>(Mesh.TriangleCount);
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Vector3d a = Mesh.GetVertex(t.a), b = Mesh.GetVertex(t.b), c = Mesh.GetVertex(t.c);
                if (!a.IsFinite || !b.IsFinite || !c.IsFinite) {
                    fail(OffsetStatus.InvalidInput, "mesh contains non-finite vertex coordinates");
                    return false;
                }
                int ia = map_vert(t.a, a, usedVerts, vertsOrig, ref box);
                int ib = map_vert(t.b, b, usedVerts, vertsOrig, ref box);
                int ic = map_vert(t.c, c, usedVerts, vertsOrig, ref box);
                tris.Add(new Index3i(ia, ib, ic));
            }
            if (tris.Count == 0) {
                fail(OffsetStatus.InvalidInput, "mesh has no triangles");
                return false;
            }

            double extent = Math.Max(box.Width, Math.Max(box.Height, box.Depth));
            if (!MathUtil.IsFinite(extent) || extent < MinExtent || extent > MaxExtent) {
                fail(OffsetStatus.InvalidInput, "mesh extent is zero or outside supported range [2^-900, 2^900]");
                return false;
            }

            // Normalize by a power of two centered on box midpoint for exact 2^k scale invariance and UTM accuracy.
            int scaleExp = pow2_exp(extent);
            Vector3d origin = new Vector3d(
                0.5 * box.Min.x + 0.5 * box.Max.x,
                0.5 * box.Min.y + 0.5 * box.Max.y,
                0.5 * box.Min.z + 0.5 * box.Max.z);
            if (!origin.IsFinite) {
                fail(OffsetStatus.InvalidInput, "mesh center is not finite");
                return false;
            }
            double dWork = ExactPoint3.ScaleB(Distance, -scaleExp);
            if (!MathUtil.IsFinite(dWork) || !(dWork > 0) || dWork * MiterLimit > 1e6) {
                fail(OffsetStatus.InvalidParameters, "Distance is out of supported working scale relative to mesh extent");
                return false;
            }

            int nv = vertsOrig.Count;
            var workVerts = new Vector3d[nv];
            for (int i = 0; i < nv; ++i)
                workVerts[i] = scale_vec(vertsOrig[i] - origin, -scaleExp);

            var workMesh = new DMesh3();
            for (int i = 0; i < nv; ++i) workMesh.AppendVertex(workVerts[i]);
            for (int i = 0; i < tris.Count; ++i) workMesh.AppendTriangle(tris[i]);

            if (Mode == OffsetModes.Outward || Mode == OffsetModes.Inward) {
                if (!ExactMeshBoolean.IsWindingClosed(workMesh)) {
                    fail(OffsetStatus.NotClosed, "Outward and Inward modes require a closed mesh");
                    return false;
                }
                double vol = signed_volume(workVerts, tris);
                if (!(vol > 0)) {
                    fail(OffsetStatus.InsideOut, "closed mesh has non-positive signed volume");
                    return false;
                }
            }

            // Compute unit face normals for all non-degenerate triangles.
            int nt = tris.Count;
            var faceNormal = new Vector3d[nt];
            var faceValid = new bool[nt];
            int validFaces = 0;
            for (int t = 0; t < nt; ++t) {
                Index3i f = tris[t];
                Vector3d n = (workVerts[f.b] - workVerts[f.a]).Cross(workVerts[f.c] - workVerts[f.a]);
                double len = n.Length;
                if (len > 0 && MathUtil.IsFinite(len)) {
                    faceNormal[t] = n / len;
                    faceValid[t] = true;
                    validFaces++;
                }
            }
            if (validFaces == 0) {
                fail(OffsetStatus.InvalidInput, "all triangles are degenerate");
                return false;
            }

            // Build undirected edge adjacency for manifold dihedral detection (deterministic ascending triangle order).
            var edgeInc = new Dictionary<long, List<(int tri, int u, int v, int opp)>>();
            var vertTris = new List<int>[nv];
            for (int i = 0; i < nv; ++i) vertTris[i] = new List<int>();
            for (int t = 0; t < nt; ++t) {
                if (!faceValid[t]) continue;
                Index3i f = tris[t];
                vertTris[f.a].Add(t);
                vertTris[f.b].Add(t);
                vertTris[f.c].Add(t);
                add_edge_inc(edgeInc, f.a, f.b, f.c, t);
                add_edge_inc(edgeInc, f.b, f.c, f.a, t);
                add_edge_inc(edgeInc, f.c, f.a, f.b, t);
            }

            var edgeKeys = new List<long>(edgeInc.Keys);
            edgeKeys.Sort();

            // Canonicalize face normals across coplanar consistently-oriented adjacent triangles so rotated flat quads
            // share bit-identical offset planes and exact diagonal walls (avoiding 1-ulp coplanar normal drift).
            var patchId = canonicalize_coplanar_normals(nt, workVerts, tris, faceValid, faceNormal, edgeKeys, edgeInc);

            var combined = new DMesh3();
            int polyCount = 0;

            if (Mode == OffsetModes.Outward) {
                append_mesh_copy(combined, workMesh);
                polyCount++;
            }

            // 1. Per-triangle offset prisms with canonical per-edge diagonals so shared walls cancel exactly.
            //    In DoubleSided mode we emit both the +n and -n half-prisms from the base triangle so each half-prism's
            //    side wall along every edge matches the corresponding +n / -n edge wedge bit for bit.
            for (int t = 0; t < nt; ++t) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                if (!faceValid[t]) continue;
                Index3i f = tris[t];
                Vector3d a = workVerts[f.a], b = workVerts[f.b], c = workVerts[f.c];
                Vector3d off = dWork * faceNormal[t];
                if (Mode == OffsetModes.Outward || Mode == OffsetModes.DoubleSided) {
                    if (append_prism(combined, a, b, c, a + off, b + off, c + off, f.a, f.b, f.c))
                        polyCount++;
                }
                if (Mode == OffsetModes.Inward || Mode == OffsetModes.DoubleSided) {
                    if (append_prism(combined, a - off, b - off, c - off, a, b, c, f.a, f.b, f.c))
                        polyCount++;
                }
            }

            // Track convex edges, miter/bevel offset vectors, and edge bevel half-spaces per vertex on each active side.
            bool doPos = Mode == OffsetModes.Outward || Mode == OffsetModes.DoubleSided;
            bool doNeg = Mode == OffsetModes.Inward || Mode == OffsetModes.DoubleSided;
            var posEdgeCount = doPos ? new int[nv] : null;
            var negEdgeCount = doNeg ? new int[nv] : null;
            var posEdgeOffs = doPos ? new List<Vector3d>[nv] : null;
            var negEdgeOffs = doNeg ? new List<Vector3d>[nv] : null;
            var posBevelPlanes = doPos ? new List<(Vector3d n, double rhs)>[nv] : null;
            var negBevelPlanes = doNeg ? new List<(Vector3d n, double rhs)>[nv] : null;

            // 2. Per-edge mitered (or bevel-clamped) convex wedges with explicit canonical-diagonal side walls.
            foreach (long key in edgeKeys) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var inc = edgeInc[key];
                if (inc.Count != 2)
                    continue;
                var e0 = inc[0];
                var e1 = inc[1];
                // Consistently oriented manifold edge: half-edges run in opposite directions (e0.u == e1.v && e0.v == e1.u).
                if (!(e0.u == e1.v && e0.v == e1.u))
                    continue;
                // Coplanar triangles in the same canonical flat patch meet flush along their shared prism wall.
                if (patchId[e0.tri] == patchId[e1.tri])
                    continue;

                Vector3d pa = workVerts[e0.u], pb = workVerts[e0.v], pc0 = workVerts[e0.opp], pc1 = workVerts[e1.opp];
                int orient = ExactPredicates.Orient3d(pa, pb, pc0, pc1);
                if (orient == 0)
                    continue;

                // orient < 0: convex on the +normal side ((n0 x n1) . (pb - pa) > 0).
                // orient > 0: convex on the -normal side (((-n0) x (-n1)) . (pa - pb) > 0, so pass endpoints pb -> pa).
                if (orient < 0 && doPos) {
                    if (emit_edge_wedge(combined, pa, pb, e0.u, e0.v, pc0, faceNormal[e0.tri], faceNormal[e1.tri],
                        dWork, MiterLimit, out var offs, out var bevelPlane)) {
                        polyCount++;
                        record_edge_miter(e0.u, e0.v, offs, bevelPlane, posEdgeCount, posEdgeOffs, posBevelPlanes);
                    }
                } else if (orient > 0 && doNeg) {
                    if (emit_edge_wedge(combined, pb, pa, e0.v, e0.u, pc0, -faceNormal[e0.tri], -faceNormal[e1.tri],
                        dWork, MiterLimit, out var offs, out var bevelPlane)) {
                        polyCount++;
                        record_edge_miter(e0.u, e0.v, offs, bevelPlane, negEdgeCount, negEdgeOffs, negBevelPlanes);
                    }
                }
            }

            // 3. Per-vertex mitered convex corner polyhedra (for vertices with >= 2 convex incident edges on an active side).
            for (int v = 0; v < nv; ++v) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                if (doPos && posEdgeCount[v] >= 2) {
                    if (emit_vertex_corner(combined, workVerts[v], vertTris[v], faceNormal, +1.0,
                        posEdgeOffs[v], posBevelPlanes[v], dWork, MiterLimit))
                        polyCount++;
                }
                if (doNeg && negEdgeCount[v] >= 2) {
                    if (emit_vertex_corner(combined, workVerts[v], vertTris[v], faceNormal, -1.0,
                        negEdgeOffs[v], negBevelPlanes[v], dWork, MiterLimit))
                        polyCount++;
                }
            }

            PolyhedraCount = polyCount;
            if (combined.TriangleCount == 0) {
                if (Mode == OffsetModes.Inward) {
                    if (!unscale_mesh(workMesh, origin, scaleExp)) {
                        fail(OffsetStatus.Failed, "unscaled coordinates overflowed double range");
                        return false;
                    }
                    Result = workMesh;
                    Status = OffsetStatus.Success;
                    return true;
                }
                fail(OffsetStatus.InvalidInput, "no valid offset polyhedra were produced");
                return false;
            }

            // 4. Exact arrangement + generalized winding number classification via ExactMeshBoolean.
            ExactMeshBoolean booleanOp;
            if (Mode == OffsetModes.Inward) {
                booleanOp = new ExactMeshBoolean(workMesh, combined, ExactMeshBoolean.Operations.Difference) {
                    SafeRounding = SafeRounding,
                    Cancellation = Cancellation
                };
            } else {
                booleanOp = new ExactMeshBoolean(combined, new DMesh3(), ExactMeshBoolean.Operations.Union) {
                    SafeRounding = SafeRounding,
                    AllowOpenMeshes = false,
                    Cancellation = Cancellation
                };
            }

            if (!booleanOp.Compute()) {
                if (booleanOp.Status == ExactMeshBoolean.BooleanStatus.Cancelled) {
                    fail(OffsetStatus.Cancelled, "cancelled during exact boolean");
                    return false;
                }
                fail(OffsetStatus.Failed, "exact boolean failed (" + booleanOp.Status + "): " + booleanOp.Error);
                return false;
            }

            DMesh3 res = booleanOp.Result;
            if (res == null || res.TriangleCount == 0) {
                if (Mode == OffsetModes.Inward) {
                    Result = new DMesh3();
                    Status = OffsetStatus.Empty;
                    return true;
                }
                fail(OffsetStatus.Failed, "exact boolean returned an empty mesh");
                return false;
            }

            if (!unscale_mesh(res, origin, scaleExp)) {
                fail(OffsetStatus.Failed, "unscaled coordinates overflowed double range");
                return false;
            }
            Result = res;
            Status = OffsetStatus.Success;
            return true;
        }

        // ---- Geometry helpers ----

        static int[] canonicalize_coplanar_normals(int nt, Vector3d[] verts, List<Index3i> tris,
            bool[] faceValid, Vector3d[] faceNormal,
            List<long> edgeKeys, Dictionary<long, List<(int tri, int u, int v, int opp)>> edgeInc)
        {
            var adj = new List<int>[nt];
            for (int t = 0; t < nt; ++t) adj[t] = new List<int>(3);

            const double CoplanarDotThreshold = 1.0 - 1e-10;
            foreach (long key in edgeKeys) {
                var inc = edgeInc[key];
                if (inc.Count != 2) continue;
                var e0 = inc[0];
                var e1 = inc[1];
                if (!(e0.u == e1.v && e0.v == e1.u)) continue;
                if (faceNormal[e0.tri].Dot(faceNormal[e1.tri]) > CoplanarDotThreshold) {
                    adj[e0.tri].Add(e1.tri);
                    adj[e1.tri].Add(e0.tri);
                }
            }

            var patchId = new int[nt];
            for (int t = 0; t < nt; ++t) patchId[t] = -1;
            int nextPatch = 0;
            var queue = new Queue<int>();
            var members = new List<int>();

            for (int seed = 0; seed < nt; ++seed) {
                if (!faceValid[seed] || patchId[seed] >= 0) continue;
                int pid = nextPatch++;
                Vector3d seedNormal = faceNormal[seed];
                patchId[seed] = pid;
                members.Clear();
                queue.Clear();
                queue.Enqueue(seed);
                while (queue.Count > 0) {
                    int cur = queue.Dequeue();
                    members.Add(cur);
                    var nbrs = adj[cur];
                    for (int i = 0; i < nbrs.Count; ++i) {
                        int nb = nbrs[i];
                        if (patchId[nb] >= 0) continue;
                        // Keep patch strictly flat relative to the seed normal as well so gently curved surfaces do not drift.
                        if (seedNormal.Dot(faceNormal[nb]) > CoplanarDotThreshold) {
                            patchId[nb] = pid;
                            queue.Enqueue(nb);
                        }
                    }
                }
                if (members.Count > 1) {
                    members.Sort();
                    Vector3d weighted = Vector3d.Zero;
                    for (int i = 0; i < members.Count; ++i) {
                        Index3i f = tris[members[i]];
                        weighted += (verts[f.b] - verts[f.a]).Cross(verts[f.c] - verts[f.a]);
                    }
                    double len = weighted.Length;
                    if (len > 0 && MathUtil.IsFinite(len)) {
                        Vector3d canon = weighted / len;
                        for (int i = 0; i < members.Count; ++i)
                            faceNormal[members[i]] = canon;
                    }
                }
            }
            return patchId;
        }

        static int map_vert(int origId, Vector3d pos, Dictionary<int, int> map, List<Vector3d> verts, ref AxisAlignedBox3d box)
        {
            if (map.TryGetValue(origId, out int idx))
                return idx;
            idx = verts.Count;
            map[origId] = idx;
            verts.Add(pos);
            box.Contain(pos);
            return idx;
        }

        static void add_edge_inc(Dictionary<long, List<(int tri, int u, int v, int opp)>> edgeInc, int u, int v, int opp, int tri)
        {
            long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
            if (!edgeInc.TryGetValue(key, out var list)) {
                list = new List<(int, int, int, int)>(2);
                edgeInc[key] = list;
            }
            list.Add((tri, u, v, opp));
        }

        static void record_edge_miter(int u, int v, List<Vector3d> offs, (Vector3d n, double rhs)? bevelPlane,
            int[] counts, List<Vector3d>[] perVert, List<(Vector3d n, double rhs)>[] perVertBevels)
        {
            counts[u]++;
            counts[v]++;
            if (offs != null && offs.Count > 0) {
                if (perVert[u] == null) perVert[u] = new List<Vector3d>();
                if (perVert[v] == null) perVert[v] = new List<Vector3d>();
                perVert[u].AddRange(offs);
                perVert[v].AddRange(offs);
            }
            if (bevelPlane.HasValue) {
                if (perVertBevels[u] == null) perVertBevels[u] = new List<(Vector3d, double)>();
                if (perVertBevels[v] == null) perVertBevels[v] = new List<(Vector3d, double)>();
                perVertBevels[u].Add(bevelPlane.Value);
                perVertBevels[v].Add(bevelPlane.Value);
            }
        }

        static double signed_volume(Vector3d[] verts, List<Index3i> tris)
        {
            double sum = 0;
            for (int i = 0; i < tris.Count; ++i) {
                Index3i f = tris[i];
                sum += verts[f.a].Cross(verts[f.b]).Dot(verts[f.c]);
            }
            return sum / 6.0;
        }

        static void append_mesh_copy(DMesh3 dst, DMesh3 src)
        {
            var vmap = new Dictionary<int, int>(src.VertexCount);
            foreach (int vid in src.VertexIndices())
                vmap[vid] = dst.AppendVertex(src.GetVertex(vid));
            foreach (int tid in src.TriangleIndices()) {
                Index3i t = src.GetTriangle(tid);
                dst.AppendTriangle(vmap[t.a], vmap[t.b], vmap[t.c]);
            }
        }

        /// <summary>
        /// Appends a quad (a0, b0, b1, a1) with outward winding, choosing the diagonal deterministically from
        /// the vertex IDs (aId, bId) so that two adjacent polyhedra sharing the opposite quad (b0, a0, a1, b1)
        /// emit the exact same two triangles with reversed orientation.
        /// </summary>
        static void append_canonical_quad(DMesh3 dst, int a0, int b0, int b1, int a1, int aId, int bId)
        {
            if (aId < bId) {
                dst.AppendTriangle(a0, b0, b1);
                dst.AppendTriangle(a0, b1, a1);
            } else {
                dst.AppendTriangle(a0, b0, a1);
                dst.AppendTriangle(b0, b1, a1);
            }
        }

        static bool append_prism(DMesh3 dst, Vector3d a0, Vector3d b0, Vector3d c0,
            Vector3d a1, Vector3d b1, Vector3d c1, int idA, int idB, int idC)
        {
            if (ExactPredicates.Orient3d(a0, b0, c0, a1) <= 0)
                return false;
            int ia0 = dst.AppendVertex(a0), ib0 = dst.AppendVertex(b0), ic0 = dst.AppendVertex(c0);
            int ia1 = dst.AppendVertex(a1), ib1 = dst.AppendVertex(b1), ic1 = dst.AppendVertex(c1);
            // Bottom (normal -n) and top (normal +n)
            dst.AppendTriangle(ia0, ic0, ib0);
            dst.AppendTriangle(ia1, ib1, ic1);
            // 3 side quads (outward oriented, canonical edge diagonals)
            append_canonical_quad(dst, ia0, ib0, ib1, ia1, idA, idB);
            append_canonical_quad(dst, ib0, ic0, ic1, ib1, idB, idC);
            append_canonical_quad(dst, ic0, ia0, ia1, ic1, idC, idA);
            return true;
        }

        /// <summary>
        /// Emits the closed convex mitered (or bevel-clamped) wedge along a convex dihedral edge (pa -> pb)
        /// where (n1 x n2) . (pb - pa) > 0. The wedge is emitted as an explicit extruded convex prism whose
        /// two faces meeting the adjacent triangle prisms use the exact same canonical edge diagonals.
        /// </summary>
        static bool emit_edge_wedge(DMesh3 dst, Vector3d pa, Vector3d pb, int idA, int idB, Vector3d pc0,
            Vector3d n1, Vector3d n2, double d, double miterLimit,
            out List<Vector3d> miterOffsets, out (Vector3d n, double rhs)? bevelPlane)
        {
            miterOffsets = new List<Vector3d>(2);
            bevelPlane = null;
            double dot = MathUtil.Clamp(n1.Dot(n2), -1.0, 1.0);
            if (dot > 1.0 - 1e-10)
                return false;

            // Cross-section polygon in cyclic order around (pb - pa): [0, d*n1, miter/bevel..., d*n2].
            var profile = new List<Vector3d>(5) { Vector3d.Zero, d * n1 };

            Vector3d sum = n1 + n2;
            double sumLen = sum.Length;
            if (sumLen > 1e-12) {
                Vector3d u = sum / sumLen;
                double cosAlpha = MathUtil.Clamp(u.Dot(n1), 1e-15, 1.0);
                double miterRatio = 1.0 / cosAlpha;
                if (miterRatio <= miterLimit) {
                    Vector3d m = (d * miterRatio) * u;
                    miterOffsets.Add(m);
                    profile.Add(m);
                } else {
                    // Exact bevel plane u . (x - p) = miterLimit * d intersected with shifted face planes n1, n2.
                    double sinAlpha = Math.Sqrt(Math.Max(0.0, 1.0 - cosAlpha * cosAlpha));
                    Vector3d w = n1 - cosAlpha * u;
                    double wLen = w.Length;
                    if (sinAlpha > 1e-12 && wLen > 1e-12) {
                        w /= wLen;
                        double s = (1.0 - miterLimit * cosAlpha) / sinAlpha;
                        Vector3d b1 = d * (miterLimit * u + s * w);
                        Vector3d b2 = d * (miterLimit * u - s * w);
                        miterOffsets.Add(b1);
                        miterOffsets.Add(b2);
                        bevelPlane = (u, miterLimit * d);
                        profile.Add(b1);
                        profile.Add(b2);
                    } else {
                        Vector3d m = (d * miterLimit) * u;
                        miterOffsets.Add(m);
                        bevelPlane = (u, miterLimit * d);
                        profile.Add(m);
                    }
                }
            } else {
                // Nearly 180-degree razor fold (n1 ~ -n2): emit orthogonal bevel cap pointing away from pc0.
                Vector3d edgeDir = pb - pa;
                Vector3d outDir = n1.Cross(edgeDir);
                double oLen = outDir.Length;
                if (oLen <= 1e-12)
                    return false;
                Vector3d u = outDir / oLen;
                if (u.Dot(pc0 - pa) > 0)
                    u = -u;
                Vector3d b1 = d * (n1 + u);
                Vector3d b2 = d * (n2 + u);
                miterOffsets.Add(b1);
                miterOffsets.Add(b2);
                bevelPlane = (u, d);
                profile.Add(b1);
                profile.Add(b2);
            }

            profile.Add(d * n2);

            // Verify positive prism orientation along (pa -> pb); reverse edge if nearly antiparallel roundoff flipped it.
            int k = profile.Count;
            Vector3d a0 = pa + profile[0], a1 = pa + profile[1], a2 = pa + profile[k - 1], b0 = pb + profile[0];
            int o = ExactPredicates.Orient3d(a0, a1, a2, b0);
            if (o == 0)
                return false;
            if (o < 0) {
                Vector3d tmpP = pa; pa = pb; pb = tmpP;
                int tmpId = idA; idA = idB; idB = tmpId;
            }

            var va = new int[k];
            var vb = new int[k];
            for (int i = 0; i < k; ++i) {
                va[i] = dst.AppendVertex(pa + profile[i]);
                vb[i] = dst.AppendVertex(pb + profile[i]);
            }

            // End caps at pa (outward -edgeDir) and pb (outward +edgeDir)
            for (int i = 1; i < k - 1; ++i) {
                dst.AppendTriangle(va[0], va[i + 1], va[i]);
                dst.AppendTriangle(vb[0], vb[i], vb[i + 1]);
            }

            // Side quads with exact canonical edge diagonals:
            // side 0 -> 1 has outward winding (vb[0], va[0], va[1], vb[1]) (opposite of face e0 prism's (va[0], vb[0], vb[1], va[1]))
            // side k-1 -> 0 has outward winding (va[0], vb[0], vb[k-1], va[k-1]) (opposite of face e1 prism's (vb[0], va[0], va[k-1], vb[k-1]))
            for (int i = 0; i < k; ++i) {
                int j = (i + 1) % k;
                if (i == k - 1) {
                    append_canonical_quad(dst, va[0], vb[0], vb[k - 1], va[k - 1], idA, idB);
                } else {
                    append_canonical_quad(dst, vb[i], va[i], va[j], vb[j], idB, idA);
                }
            }
            return true;
        }

        /// <summary>
        /// Emits the closed convex mitered corner polyhedron at vertex v on side s (+1 or -1) by intersecting
        /// the incident shifted face half-spaces and any bevel clamp half-spaces from acute incident edges/corners.
        /// </summary>
        static bool emit_vertex_corner(DMesh3 dst, Vector3d v, List<int> incTris, Vector3d[] faceNormals,
            double side, List<Vector3d> edgeOffs, List<(Vector3d n, double rhs)> edgeBevels, double d, double miterLimit)
        {
            // Collect deduplicated unit plane normals around v on this side (deterministic ascending order).
            const double DupDot = 1.0 - 1e-10;
            var normals = new List<Vector3d>(incTris.Count);
            for (int i = 0; i < incTris.Count; ++i) {
                Vector3d n = side * faceNormals[incTris[i]];
                bool dup = false;
                for (int j = 0; j < normals.Count; ++j) {
                    if (normals[j].Dot(n) > DupDot) { dup = true; break; }
                }
                if (!dup) normals.Add(n);
            }
            if (normals.Count < 3)
                return false;

            // Half-space system: all shifted face planes (n_i . x <= d) plus incident edge bevel planes (u . x <= rhs).
            var planes = new List<(Vector3d n, double rhs)>(normals.Count + (edgeBevels != null ? edgeBevels.Count : 0) + 2);
            for (int i = 0; i < normals.Count; ++i)
                planes.Add((normals[i], d));
            if (edgeBevels != null) {
                for (int i = 0; i < edgeBevels.Count; ++i)
                    add_unique_plane(planes, edgeBevels[i].n, edgeBevels[i].rhs, d);
            }

            double maxRad = miterLimit * d;
            double maxRadTol = maxRad * (1.0 + 1e-5);
            double planeTol = 1e-5 * d;

            // If any feasible 3-face intersection still exceeds MiterLimit * d (acute pyramid tip whose edges were not bevelled),
            // add a corner bevel half-space along its direction so the tip is bevelled cleanly rather than pinched.
            int nf = normals.Count;
            if (nf <= 12) {
                for (int i = 0; i < nf; ++i) {
                    for (int j = i + 1; j < nf; ++j) {
                        Vector3d c = normals[i].Cross(normals[j]);
                        if (c.LengthSquared < 1e-12) continue;
                        for (int l = j + 1; l < nf; ++l) {
                            double det = c.Dot(normals[l]);
                            if (Math.Abs(det) < 1e-8) continue;
                            Vector3d x = (d / det) * (normals[j].Cross(normals[l]) + normals[l].Cross(normals[i]) + c);
                            if (!x.IsFinite) continue;
                            double len = x.Length;
                            if (len > maxRadTol && satisfies_all_planes(planes, x, planeTol))
                                add_unique_plane(planes, x / len, maxRad, d);
                        }
                    }
                }
            }

            var pts = new List<Vector3d>(24) { v };
            double mergeTolSq = (1e-9 * d) * (1e-9 * d);
            for (int i = 0; i < normals.Count; ++i)
                add_unique_point(pts, v + d * normals[i], mergeTolSq);
            if (edgeOffs != null) {
                for (int i = 0; i < edgeOffs.Count; ++i)
                    add_unique_point(pts, v + edgeOffs[i], mergeTolSq);
            }

            // Collect all feasible triple-plane intersections of the half-space system (faces + bevel planes).
            int np = planes.Count;
            if (np <= 16) {
                for (int i = 0; i < np; ++i) {
                    for (int j = i + 1; j < np; ++j) {
                        Vector3d c = planes[i].n.Cross(planes[j].n);
                        if (c.LengthSquared < 1e-12) continue;
                        for (int l = j + 1; l < np; ++l) {
                            double det = c.Dot(planes[l].n);
                            if (Math.Abs(det) < 1e-8) continue;
                            Vector3d x = (planes[i].rhs * planes[j].n.Cross(planes[l].n)
                                        + planes[j].rhs * planes[l].n.Cross(planes[i].n)
                                        + planes[l].rhs * c) / det;
                            if (!x.IsFinite) continue;
                            if (satisfies_all_planes(planes, x, planeTol))
                                add_unique_point(pts, v + x, mergeTolSq);
                        }
                    }
                }
            }

            // Fallback / high-valence QEF point (Cao et al. §3.1) when feasible within all half-spaces.
            if (solve_qef_offset(normals, d, out Vector3d qefDelta)
                && qefDelta.Length <= maxRadTol
                && satisfies_all_planes(planes, qefDelta, planeTol)) {
                add_unique_point(pts, v + qefDelta, mergeTolSq);
            }

            return append_convex_hull(dst, pts);
        }

        static void add_unique_plane(List<(Vector3d n, double rhs)> planes, Vector3d n, double rhs, double d)
        {
            const double DupDot = 1.0 - 1e-10;
            double rhsTol = 1e-9 * d;
            for (int i = 0; i < planes.Count; ++i) {
                if (planes[i].n.Dot(n) > DupDot && Math.Abs(planes[i].rhs - rhs) <= rhsTol)
                    return;
            }
            planes.Add((n, rhs));
        }

        static void add_unique_point(List<Vector3d> pts, Vector3d p, double mergeTolSq)
        {
            if (!p.IsFinite) return;
            for (int i = 0; i < pts.Count; ++i) {
                if ((pts[i] - p).LengthSquared <= mergeTolSq)
                    return;
            }
            pts.Add(p);
        }

        static bool satisfies_all_planes(List<(Vector3d n, double rhs)> planes, Vector3d delta, double tol)
        {
            if (!delta.IsFinite) return false;
            for (int i = 0; i < planes.Count; ++i) {
                if (planes[i].n.Dot(delta) > planes[i].rhs + tol)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Solves the 3x3 symmetric point-to-plane normal equations A * delta = b via truncated Jacobi eigendecomposition.
        /// Deterministic and handles rank 1 (planar), rank 2 (crease), and rank 3 (corner) identically.
        /// </summary>
        static bool solve_qef_offset(List<Vector3d> normals, double d, out Vector3d delta)
        {
            delta = Vector3d.Zero;
            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
            double bx = 0, by = 0, bz = 0;
            for (int i = 0; i < normals.Count; ++i) {
                Vector3d n = normals[i];
                a00 += n.x * n.x; a01 += n.x * n.y; a02 += n.x * n.z;
                a11 += n.y * n.y; a12 += n.y * n.z; a22 += n.z * n.z;
                bx += d * n.x; by += d * n.y; bz += d * n.z;
            }

            // 3x3 symmetric Jacobi eigendecomposition (fixed 20 sweeps, deterministic pivot order).
            double[,] m = { { a00, a01, a02 }, { a01, a11, a12 }, { a02, a12, a22 } };
            double[,] e = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 20; ++sweep) {
                double off = Math.Abs(m[0, 1]) + Math.Abs(m[0, 2]) + Math.Abs(m[1, 2]);
                if (off < 1e-15)
                    break;
                for (int p = 0; p < 3; ++p) {
                    for (int q = p + 1; q < 3; ++q) {
                        double mpq = m[p, q];
                        if (Math.Abs(mpq) < 1e-16)
                            continue;
                        double tau = (m[q, q] - m[p, p]) / (2.0 * mpq);
                        double t = tau >= 0
                            ? 1.0 / (tau + Math.Sqrt(1.0 + tau * tau))
                            : -1.0 / (-tau + Math.Sqrt(1.0 + tau * tau));
                        double c = 1.0 / Math.Sqrt(1.0 + t * t);
                        double s = t * c;

                        double mpp = m[p, p], mqq = m[q, q];
                        m[p, p] = mpp - t * mpq;
                        m[q, q] = mqq + t * mpq;
                        m[p, q] = m[q, p] = 0.0;

                        int r = 3 - p - q;
                        double mpr = m[p, r], mqr = m[q, r];
                        m[p, r] = m[r, p] = c * mpr - s * mqr;
                        m[q, r] = m[r, q] = s * mpr + c * mqr;

                        for (int row = 0; row < 3; ++row) {
                            double erp = e[row, p], erq = e[row, q];
                            e[row, p] = c * erp - s * erq;
                            e[row, q] = s * erp + c * erq;
                        }
                    }
                }
            }

            double l0 = m[0, 0], l1 = m[1, 1], l2 = m[2, 2];
            double lmax = Math.Max(l0, Math.Max(l1, l2));
            if (!(lmax > 1e-12))
                return false;
            double thresh = 1e-8 * lmax;

            Vector3d b = new Vector3d(bx, by, bz);
            Vector3d x = Vector3d.Zero;
            for (int col = 0; col < 3; ++col) {
                double lam = m[col, col];
                if (lam > thresh) {
                    Vector3d ev = new Vector3d(e[0, col], e[1, col], e[2, col]);
                    x += (ev.Dot(b) / lam) * ev;
                }
            }
            if (!x.IsFinite)
                return false;
            delta = x;
            return true;
        }

        /// <summary>
        /// Exact deterministic incremental 3D convex hull using ExactPredicates.Orient3d.
        /// Appends a closed, positively oriented convex polyhedron to dst, or returns false if degenerate.
        /// </summary>
        static bool append_convex_hull(DMesh3 dst, List<Vector3d> rawPts)
        {
            // Exact deduplication preserving deterministic input order.
            var pts = new List<Vector3d>(rawPts.Count);
            for (int i = 0; i < rawPts.Count; ++i) {
                Vector3d p = rawPts[i];
                if (!p.IsFinite) continue;
                bool dup = false;
                for (int j = 0; j < pts.Count; ++j) {
                    if (pts[j] == p) { dup = true; break; }
                }
                if (!dup) pts.Add(p);
            }
            if (pts.Count < 4)
                return false;

            // 1. Find initial non-degenerate tetrahedron (i0, i1, i2, i3).
            int i0 = 0, i1 = -1;
            double bestD2 = 0;
            for (int i = 1; i < pts.Count; ++i) {
                double d2 = (pts[i] - pts[i0]).LengthSquared;
                if (d2 > bestD2) { bestD2 = d2; i1 = i; }
            }
            if (i1 < 0) return false;

            Vector3d e01 = pts[i1] - pts[i0];
            int i2 = -1;
            double bestArea2 = 0;
            for (int i = 1; i < pts.Count; ++i) {
                if (i == i1) continue;
                double a2 = e01.Cross(pts[i] - pts[i0]).LengthSquared;
                if (a2 > bestArea2) { bestArea2 = a2; i2 = i; }
            }
            if (i2 < 0) return false;

            Vector3d n012 = e01.Cross(pts[i2] - pts[i0]);
            int i3 = -1;
            double bestVol = 0;
            for (int i = 1; i < pts.Count; ++i) {
                if (i == i1 || i == i2) continue;
                if (ExactPredicates.Orient3d(pts[i0], pts[i1], pts[i2], pts[i]) == 0)
                    continue;
                double vol = Math.Abs(n012.Dot(pts[i] - pts[i0]));
                if (vol > bestVol || i3 < 0) { bestVol = vol; i3 = i; }
            }
            if (i3 < 0) return false;

            if (ExactPredicates.Orient3d(pts[i0], pts[i1], pts[i2], pts[i3]) < 0) {
                int tmp = i1; i1 = i2; i2 = tmp;
            }

            // Outward-oriented initial faces (Orient3d(a, b, c, interior) < 0).
            var faces = new List<Index3i>(2 * pts.Count) {
                new Index3i(i0, i2, i1),
                new Index3i(i0, i1, i3),
                new Index3i(i1, i2, i3),
                new Index3i(i2, i0, i3)
            };

            // 2. Incremental insertion with exact Orient3d visibility.
            var visible = new List<int>();
            var visEdgeSet = new HashSet<long>();
            var horizon = new List<(int u, int v)>();
            for (int p = 0; p < pts.Count; ++p) {
                if (p == i0 || p == i1 || p == i2 || p == i3)
                    continue;
                Vector3d vp = pts[p];
                visible.Clear();
                for (int f = 0; f < faces.Count; ++f) {
                    Index3i tri = faces[f];
                    if (ExactPredicates.Orient3d(pts[tri.a], pts[tri.b], pts[tri.c], vp) > 0)
                        visible.Add(f);
                }
                if (visible.Count == 0)
                    continue;

                visEdgeSet.Clear();
                for (int k = 0; k < visible.Count; ++k) {
                    Index3i tri = faces[visible[k]];
                    visEdgeSet.Add(dir_edge_key(tri.a, tri.b));
                    visEdgeSet.Add(dir_edge_key(tri.b, tri.c));
                    visEdgeSet.Add(dir_edge_key(tri.c, tri.a));
                }

                horizon.Clear();
                for (int k = 0; k < visible.Count; ++k) {
                    Index3i tri = faces[visible[k]];
                    if (!visEdgeSet.Contains(dir_edge_key(tri.b, tri.a))) horizon.Add((tri.a, tri.b));
                    if (!visEdgeSet.Contains(dir_edge_key(tri.c, tri.b))) horizon.Add((tri.b, tri.c));
                    if (!visEdgeSet.Contains(dir_edge_key(tri.a, tri.c))) horizon.Add((tri.c, tri.a));
                }

                // Remove visible faces in descending index order to keep remaining order deterministic.
                for (int k = visible.Count - 1; k >= 0; --k)
                    faces.RemoveAt(visible[k]);

                for (int k = 0; k < horizon.Count; ++k)
                    faces.Add(new Index3i(horizon[k].u, horizon[k].v, p));
            }

            if (faces.Count < 4)
                return false;

            // Emit only hull vertices referenced by final faces.
            var vmap = new int[pts.Count];
            for (int i = 0; i < vmap.Length; ++i) vmap[i] = -1;
            for (int f = 0; f < faces.Count; ++f) {
                Index3i tri = faces[f];
                int va = vmap[tri.a] >= 0 ? vmap[tri.a] : (vmap[tri.a] = dst.AppendVertex(pts[tri.a]));
                int vb = vmap[tri.b] >= 0 ? vmap[tri.b] : (vmap[tri.b] = dst.AppendVertex(pts[tri.b]));
                int vc = vmap[tri.c] >= 0 ? vmap[tri.c] : (vmap[tri.c] = dst.AppendVertex(pts[tri.c]));
                dst.AppendTriangle(va, vb, vc);
            }
            return true;
        }

        static long dir_edge_key(int u, int v)
        {
            return ((long)u << 32) | (uint)v;
        }

        static bool unscale_mesh(DMesh3 mesh, Vector3d origin, int scaleExp)
        {
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = origin + scale_vec(mesh.GetVertex(vid), scaleExp);
                if (!p.IsFinite)
                    return false;
                mesh.SetVertex(vid, p);
            }
            return true;
        }

        static Vector3d scale_vec(Vector3d v, int exp)
        {
            return new Vector3d(
                ExactPoint3.ScaleB(v.x, exp),
                ExactPoint3.ScaleB(v.y, exp),
                ExactPoint3.ScaleB(v.z, exp));
        }

        static int pow2_exp(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int biased = (int)((bits >> 52) & 0x7FFL);
            if (biased != 0)
                return biased - 1023;
            long sbits = BitConverter.DoubleToInt64Bits(v * 18014398509481984.0); // 2^54
            return (int)((sbits >> 52) & 0x7FFL) - 1023 - 54;
        }
    }
}
