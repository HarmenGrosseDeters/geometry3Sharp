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
    ///   - <see cref="OffsetModes.Outward"/>: outward mitered offset of a closed, positively oriented solid by Distance &gt; 0
    ///     (exact union of the input, per-face outward prisms, convex edge miter wedges, and convex corner polyhedra).
    ///   - <see cref="OffsetModes.Inward"/>: inward mitered inset of a closed, positively oriented solid by Distance &gt; 0
    ///     (exact difference of the input minus inward face prisms, reflex edge miter wedges, and reflex corner polyhedra).
    ///   - <see cref="OffsetModes.DoubleSided"/>: symmetric thickening of an open CAD sheet, shell, or arbitrary triangle
    ///     soup by +/-Distance (total wall thickness 2 * Distance) into a closed, 2-manifold solid with crisp planar faces
    ///     and sharp mitered convex and concave creases.
    ///
    /// Input vertices are welded by exact position so unwelded soups (e.g. STL) are handled identically to welded meshes.
    /// Scale invariance: the input is processed in a power-of-two normalized frame centered on the triangle bounding box,
    /// so 2^k scalings of the mesh and Distance produce 2^k times the result bit for bit.
    /// Output verification: when <see cref="SafeRounding"/> is true, the returned mesh is verified in world coordinates to be
    /// closed, 2-manifold, positively oriented, and free of degenerate triangles and crossing self-intersections (exact
    /// touching contacts where offset shells meet are split into manifold pairs and counted in <see cref="Contacts"/>).
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
            /// <summary>
            /// <see cref="Result"/> is set: closed, 2-manifold, positively oriented solid free of crossing self-intersections
            /// and degenerate triangles (when <see cref="SafeRounding"/> is true; exact touching contacts are counted in <see cref="Contacts"/>).
            /// </summary>
            Success = 0,
            /// <summary> Null or empty mesh, all degenerate triangles, non-finite coordinates, or extent outside [2^-900, 2^900]. </summary>
            InvalidInput = 1,
            /// <summary> Outward or Inward mode was requested on a mesh that is not closed in the winding sense. </summary>
            NotClosed = 2,
            /// <summary> Outward or Inward mode was requested on a closed mesh with an inside-out shell or inconsistent orientation. </summary>
            InsideOut = 3,
            /// <summary> Invalid Mode, Distance &lt;= 0 or out of working scale [1e-12, 1e6/MiterLimit], or MiterLimit &lt; 1 or non-finite. </summary>
            InvalidParameters = 4,
            /// <summary> Inward inset consumed the entire solid cleanly (empty result). </summary>
            Empty = 5,
            /// <summary> Exact arrangement / boolean, safe rounding, or world-coordinate cleanliness verification failed (see <see cref="Error"/>). </summary>
            Failed = 6,
            /// <summary> Cancelled via <see cref="Cancellation"/>. </summary>
            Cancelled = 7
        }

        /// <summary> Input mesh (never modified). Isolated vertices are ignored; coincident vertices are welded by exact position. </summary>
        public DMesh3 Mesh;
        /// <summary> Offset mode (default <see cref="OffsetModes.Outward"/>). </summary>
        public OffsetModes Mode = OffsetModes.Outward;
        /// <summary> Positive offset distance (half-thickness in <see cref="OffsetModes.DoubleSided"/> mode). </summary>
        public double Distance;
        /// <summary>
        /// Maximum miter extrusion ratio relative to <see cref="Distance"/> (must be &gt;= 1, default 4.0).
        /// Acute convex creases and corners whose exact miter distance exceeds MiterLimit * Distance are bevel-clamped.
        /// </summary>
        public double MiterLimit = 4.0;
        /// <summary> Safe rounding and world-coordinate cleanliness verification (default true). </summary>
        public bool SafeRounding = true;
        /// <summary> Optional cancellation token propagated through polyhedron generation and <see cref="ExactMeshBoolean"/>. </summary>
        public CancellationToken Cancellation = CancellationToken.None;

        /// <summary> Result mesh on <see cref="OffsetStatus.Success"/> (empty mesh on <see cref="OffsetStatus.Empty"/>, null on failure). </summary>
        public DMesh3 Result { get; private set; }
        /// <summary> Status of the last <see cref="Compute"/> call. </summary>
        public OffsetStatus Status { get; private set; } = OffsetStatus.InvalidInput;
        /// <summary> Diagnostic message when <see cref="Status"/> is not <see cref="OffsetStatus.Success"/>. </summary>
        public string Error { get; private set; } = "";
        /// <summary> Number of convex offset polyhedra (face prisms, edge wedges, corner polyhedra) assembled before the exact boolean. </summary>
        public int PolyhedraCount { get; private set; }
        /// <summary> Number of world-coordinate positions with duplicated vertices in <see cref="Result"/> (exact touching contacts). </summary>
        public int Contacts { get; private set; }
        /// <summary> True when <see cref="SafeRounding"/> verified the result free of rounding defects. </summary>
        public bool RoundingClean { get; private set; }

        const double MinExtent = 1.1832913578315177e-271; // 2^-900
        const double MaxExtent = 8.4527124981706439e270;  // 2^900
        const double MinRelativeDistance = 1e-12;
        const double MaxRelativeMiterDistance = 1e6;
        const double CoplanarDotThreshold = 1.0 - 1e-10;

        bool is_cancelled()
        {
            return Cancellation.IsCancellationRequested;
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
            Status = OffsetStatus.Failed;
            Error = "";
            PolyhedraCount = 0;
            Contacts = 0;
            RoundingClean = false;

            if (is_cancelled()) {
                fail(OffsetStatus.Cancelled, "cancelled");
                return false;
            }
            if (Mode != OffsetModes.Outward && Mode != OffsetModes.Inward && Mode != OffsetModes.DoubleSided) {
                fail(OffsetStatus.InvalidParameters, "Mode is not a defined OffsetModes value");
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

            // Collect triangle-referenced vertices welded by exact position (so unwelded STL soups work identically).
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            var posToIdx = new Dictionary<Vector3d, int>();
            var vertsOrig = new List<Vector3d>();
            var tris = new List<Index3i>(Mesh.TriangleCount);
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Vector3d a = Mesh.GetVertex(t.a), b = Mesh.GetVertex(t.b), c = Mesh.GetVertex(t.c);
                if (!a.IsFinite || !b.IsFinite || !c.IsFinite) {
                    fail(OffsetStatus.InvalidInput, "mesh contains non-finite vertex coordinates");
                    return false;
                }
                int ia = map_vert_pos(a, posToIdx, vertsOrig, ref box);
                int ib = map_vert_pos(b, posToIdx, vertsOrig, ref box);
                int ic = map_vert_pos(c, posToIdx, vertsOrig, ref box);
                if (ia == ib || ib == ic || ic == ia)
                    continue;
                tris.Add(new Index3i(ia, ib, ic));
            }
            if (tris.Count == 0) {
                fail(OffsetStatus.InvalidInput, "mesh has no non-degenerate triangles");
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
            if (!MathUtil.IsFinite(dWork) || !(dWork >= MinRelativeDistance) || dWork * MiterLimit > MaxRelativeMiterDistance) {
                fail(OffsetStatus.InvalidParameters, "Distance is outside supported working scale relative to mesh extent");
                return false;
            }

            int nv = vertsOrig.Count;
            var workVerts = new Vector3d[nv];
            for (int i = 0; i < nv; ++i)
                workVerts[i] = scale_vec(vertsOrig[i] - origin, -scaleExp);

            // For DoubleSided thickening, consistently orient each edge-connected manifold sheet component
            // so opposing face windings on an open CAD sheet never leave missing crease wedges.
            if (Mode == OffsetModes.DoubleSided)
                orient_manifold_components_deterministically(tris);

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

            bool doPos = Mode == OffsetModes.Outward || Mode == OffsetModes.DoubleSided;
            bool doNeg = Mode == OffsetModes.Inward || Mode == OffsetModes.DoubleSided;

            // Validate triangles against collinearity/slivers and exact prism orientation before building adjacency.
            int nt = tris.Count;
            var faceNormal = new Vector3d[nt];
            var faceValid = new bool[nt];
            int validFaces = 0;
            for (int t = 0; t < nt; ++t) {
                Index3i f = tris[t];
                Vector3d a = workVerts[f.a], b = workVerts[f.b], c = workVerts[f.c];
                Vector3d e1 = b - a, e2 = c - a, e3 = c - b;
                double maxEdgeSq = Math.Max(e1.LengthSquared, Math.Max(e2.LengthSquared, e3.LengthSquared));
                if (!(maxEdgeSq > 0)) continue;
                Vector3d n = e1.Cross(e2);
                double len = n.Length;
                if (!MathUtil.IsFinite(len) || !(len > 1e-12 * maxEdgeSq))
                    continue;
                Vector3d unitN = n / len;
                Vector3d off = dWork * unitN;
                if (doPos && ExactPredicates.Orient3d(a, b, c, a + off) <= 0)
                    continue;
                if (doNeg && ExactPredicates.Orient3d(a - off, b - off, c - off, a) <= 0)
                    continue;
                faceNormal[t] = unitN;
                faceValid[t] = true;
                validFaces++;
            }
            if (validFaces == 0) {
                fail(OffsetStatus.InvalidInput, "all triangles are degenerate or too thin for the requested offset");
                return false;
            }

            // Build undirected edge adjacency over valid triangles (deterministic ascending triangle order).
            var edgeInc = new Dictionary<long, List<HalfEdgeInc>>();
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

            // Canonicalize face normals across coplanar consistently-oriented manifold patches so rotated flat quads
            // share bit-identical offset planes and exact diagonal walls (avoiding 1-ulp coplanar normal drift).
            var patchId = canonicalize_coplanar_normals(nt, workVerts, tris, faceValid, faceNormal, edgeKeys, edgeInc);

            var combined = new DMesh3();
            int polyCount = 0;

            // 1. Per-triangle offset prisms with canonical per-edge diagonals so shared walls cancel exactly.
            for (int t = 0; t < nt; ++t) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                if (!faceValid[t]) continue;
                Index3i f = tris[t];
                Vector3d a = workVerts[f.a], b = workVerts[f.b], c = workVerts[f.c];
                Vector3d off = dWork * faceNormal[t];
                if (doPos) {
                    if (append_prism(combined, a, b, c, a + off, b + off, c + off, f.a, f.b, f.c))
                        polyCount++;
                }
                if (doNeg) {
                    if (append_prism(combined, a - off, b - off, c - off, a, b, c, f.a, f.b, f.c))
                        polyCount++;
                }
            }

            // Partition each vertex's incident triangles into edge-connected umbrellas (handling bowtie vertices cleanly)
            // and collect convex edge wedges & bevel half-spaces per (vertex, triangle).
            var vertTriAdj = new Dictionary<long, List<int>>();
            var triPosBevels = doPos ? new List<(Vector3d n, double rhs)>[nt] : null;
            var triNegBevels = doNeg ? new List<(Vector3d n, double rhs)>[nt] : null;
            var vertTriPosConvexCount = doPos ? new Dictionary<long, int>() : null;
            var vertTriNegConvexCount = doNeg ? new Dictionary<long, int>() : null;

            // 2. Per-edge mitered (or bevel-clamped) convex wedges with explicit canonical-diagonal side walls.
            foreach (long key in edgeKeys) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var inc = edgeInc[key];
                if (inc.Count == 2) {
                    var e0 = inc[0];
                    var e1 = inc[1];
                    if (!(e0.u == e1.v && e0.v == e1.u))
                        continue;

                    link_umbrella_tris(vertTriAdj, e0.u, e0.tri, e1.tri);
                    link_umbrella_tris(vertTriAdj, e0.v, e0.tri, e1.tri);

                    if (patchId[e0.tri] == patchId[e1.tri])
                        continue;

                    Vector3d pa = workVerts[e0.u], pb = workVerts[e0.v], pc0 = workVerts[e0.opp], pc1 = workVerts[e1.opp];
                    int orient = ExactPredicates.Orient3d(pa, pb, pc0, pc1);
                    if (orient == 0)
                        continue;

                    if (orient < 0 && doPos) {
                        if (emit_edge_wedge(combined, pa, pb, e0.u, e0.v, pc0, faceNormal[e0.tri], faceNormal[e1.tri],
                            dWork, MiterLimit, isNegSide: false, out var bevelPlane)) {
                            polyCount++;
                            record_convex_edge(e0.u, e0.v, e0.tri, e1.tri, bevelPlane, vertTriPosConvexCount, triPosBevels);
                        }
                    } else if (orient > 0 && doNeg) {
                        if (emit_edge_wedge(combined, pb, pa, e0.v, e0.u, pc0, -faceNormal[e0.tri], -faceNormal[e1.tri],
                            dWork, MiterLimit, isNegSide: true, out var bevelPlane)) {
                            polyCount++;
                            record_convex_edge(e0.u, e0.v, e0.tri, e1.tri, bevelPlane, vertTriNegConvexCount, triNegBevels);
                        }
                    }
                } else if (inc.Count >= 4 && (inc.Count % 2) == 0) {
                    // Non-manifold edge with even incidence: pair opposite-winding half-edges cyclically around the edge.
                    int uRef = inc[0].u < inc[0].v ? inc[0].u : inc[0].v;
                    int vRef = inc[0].u < inc[0].v ? inc[0].v : inc[0].u;
                    process_nonmanifold_edge(combined, workVerts, faceNormal, patchId, uRef, vRef, inc,
                        doPos, doNeg, dWork, MiterLimit, vertTriAdj,
                        vertTriPosConvexCount, triPosBevels, vertTriNegConvexCount, triNegBevels, ref polyCount);
                }
            }

            // 3. Per-(vertex, umbrella) mitered convex corner polyhedra via exact indexed half-space clipping.
            for (int v = 0; v < nv; ++v) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var incTris = vertTris[v];
                if (incTris.Count < 3)
                    continue;

                var umbrellas = partition_vertex_umbrellas(v, incTris, vertTriAdj);
                for (int u = 0; u < umbrellas.Count; ++u) {
                    var umb = umbrellas[u];
                    if (umb.Count < 3)
                        continue;

                    if (doPos && count_umbrella_convex_edges(v, umb, vertTriPosConvexCount) >= 2) {
                        if (emit_vertex_corner_clipped(combined, v, umb, workVerts, tris, faceNormal, +1.0,
                            triPosBevels, dWork, MiterLimit))
                            polyCount++;
                    }
                    if (doNeg && count_umbrella_convex_edges(v, umb, vertTriNegConvexCount) >= 2) {
                        if (emit_vertex_corner_clipped(combined, v, umb, workVerts, tris, faceNormal, -1.0,
                            triNegBevels, dWork, MiterLimit))
                            polyCount++;
                    }
                }
            }

            PolyhedraCount = polyCount;
            if (combined.TriangleCount == 0) {
                fail(OffsetStatus.Failed, "no valid offset polyhedra were produced");
                return false;
            }

            // 4. Exact arrangement + winding-number boolean via ExactMeshBoolean.
            //    Outward and Inward keep workMesh as MeshA with CountInsideOutCells = true so multi-component
            //    inside-out shells and inconsistently oriented solids are caught while true cavities are permitted.
            ExactMeshBoolean booleanOp;
            if (Mode == OffsetModes.Outward) {
                booleanOp = new ExactMeshBoolean(workMesh, combined, ExactMeshBoolean.Operations.Union) {
                    SafeRounding = SafeRounding,
                    AllowOpenMeshes = false,
                    CountInsideOutCells = true,
                    Cancellation = Cancellation
                };
            } else if (Mode == OffsetModes.Inward) {
                booleanOp = new ExactMeshBoolean(workMesh, combined, ExactMeshBoolean.Operations.Difference) {
                    SafeRounding = SafeRounding,
                    AllowOpenMeshes = false,
                    CountInsideOutCells = true,
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
                if (booleanOp.Status == ExactMeshBoolean.BooleanStatus.InconsistentOrientation) {
                    fail(OffsetStatus.InsideOut, "closed input mesh has inconsistently oriented faces: " + booleanOp.Error);
                    return false;
                }
                fail(OffsetStatus.Failed, "exact boolean failed (" + booleanOp.Status + "): " + booleanOp.Error);
                return false;
            }

            if ((Mode == OffsetModes.Outward || Mode == OffsetModes.Inward)
                && ((booleanOp.InsideOutCellsOfInput != null && booleanOp.InsideOutCellsOfInput[0] > 0)
                    || booleanOp.InsideOutUndecided > 0)) {
                fail(OffsetStatus.InsideOut, "closed input mesh contains an inside-out shell");
                return false;
            }

            DMesh3 res = booleanOp.Result;
            if (res == null || res.TriangleCount == 0) {
                if (Mode == OffsetModes.Inward) {
                    Result = new DMesh3();
                    Status = OffsetStatus.Empty;
                    RoundingClean = booleanOp.RoundingClean;
                    return true;
                }
                fail(OffsetStatus.Failed, "exact boolean returned an empty mesh");
                return false;
            }

            if (!unscale_mesh(res, origin, scaleExp)) {
                fail(OffsetStatus.Failed, "unscaled coordinates overflowed double range");
                return false;
            }

            // 5. World-coordinate cleanliness verification (catches rounding defects and UTM unscaling quantization).
            if (SafeRounding) {
                string reason = MeshRobustRepair.NotCleanReason(res, checkSelfIntersections: true);
                if (reason != "" && !is_pure_contacts_only(res, reason)) {
                    // If unscaling back to a non-zero (e.g. UTM) origin quantized coordinates, run a world-coordinate rounding pass.
                    if (origin != Vector3d.Zero) {
                        var repair = new MeshRoundingRepair(res);
                        repair.Apply();
                        reason = MeshRobustRepair.NotCleanReason(res, checkSelfIntersections: true);
                    }
                    if (reason != "" && !is_pure_contacts_only(res, reason)) {
                        fail(OffsetStatus.Failed, "output failed cleanliness verification: " + reason);
                        return false;
                    }
                }
                if (res.TriangleCount == 0) {
                    if (Mode == OffsetModes.Inward) {
                        Result = new DMesh3();
                        Status = OffsetStatus.Empty;
                        RoundingClean = true;
                        return true;
                    }
                    fail(OffsetStatus.Failed, "rounding repair left an empty mesh");
                    return false;
                }
                Contacts = count_contacts(res);
                RoundingClean = booleanOp.RoundingClean && (reason == "" || is_pure_contacts_only(res, reason));
            } else {
                Contacts = count_contacts(res);
                RoundingClean = false;
            }

            Result = res;
            Status = OffsetStatus.Success;
            return true;
        }

        // ---- Geometry & topology helpers ----

        struct HalfEdgeInc
        {
            public int tri, u, v, opp;
        }

        static bool is_pure_contacts_only(DMesh3 mesh, string reason)
        {
            if (reason != "self-intersections")
                return false;
            if (!ExactMeshBoolean.IsWindingClosed(mesh))
                return false;
            return SolidBooleanPipeline.CrossingTrianglesOfAll(mesh).Count == 0;
        }

        static int count_contacts(DMesh3 mesh)
        {
            var seen = new Dictionary<Vector3d, int>(mesh.VertexCount);
            int contacts = 0;
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                if (seen.TryGetValue(p, out int c)) {
                    if (c == 1) contacts++;
                    seen[p] = c + 1;
                } else {
                    seen[p] = 1;
                }
            }
            return contacts;
        }

        static int map_vert_pos(Vector3d pos, Dictionary<Vector3d, int> posToIdx, List<Vector3d> verts, ref AxisAlignedBox3d box)
        {
            if (posToIdx.TryGetValue(pos, out int idx))
                return idx;
            idx = verts.Count;
            posToIdx[pos] = idx;
            verts.Add(pos);
            box.Contain(pos);
            return idx;
        }

        static void add_edge_inc(Dictionary<long, List<HalfEdgeInc>> edgeInc, int u, int v, int opp, int tri)
        {
            long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
            if (!edgeInc.TryGetValue(key, out var list)) {
                list = new List<HalfEdgeInc>(2);
                edgeInc[key] = list;
            }
            list.Add(new HalfEdgeInc { tri = tri, u = u, v = v, opp = opp });
        }

        /// <summary>
        /// Deterministically orients each manifold edge-connected component of an open sheet consistently so that
        /// DoubleSided thickening treats creases identically regardless of per-triangle winding flips.
        /// </summary>
        static void orient_manifold_components_deterministically(List<Index3i> tris)
        {
            int nt = tris.Count;
            var edgeInc = new Dictionary<long, List<(int tri, int u, int v)>>(nt * 2);
            for (int t = 0; t < nt; ++t) {
                Index3i f = tris[t];
                add_orient_edge(edgeInc, f.a, f.b, t);
                add_orient_edge(edgeInc, f.b, f.c, t);
                add_orient_edge(edgeInc, f.c, f.a, t);
            }

            var adj = new List<(int nb, bool sameDir)>[nt];
            for (int t = 0; t < nt; ++t) adj[t] = new List<(int, bool)>(3);
            var keys = new List<long>(edgeInc.Keys);
            keys.Sort();
            foreach (long key in keys) {
                var list = edgeInc[key];
                if (list.Count != 2) continue;
                var a = list[0];
                var b = list[1];
                bool sameDir = a.u == b.u && a.v == b.v;
                adj[a.tri].Add((b.tri, sameDir));
                adj[b.tri].Add((a.tri, sameDir));
            }

            var visited = new bool[nt];
            var flipped = new bool[nt];
            var queue = new Queue<int>();
            for (int seed = 0; seed < nt; ++seed) {
                if (visited[seed]) continue;
                visited[seed] = true;
                queue.Enqueue(seed);
                while (queue.Count > 0) {
                    int cur = queue.Dequeue();
                    bool curFlip = flipped[cur];
                    var nbrs = adj[cur];
                    for (int i = 0; i < nbrs.Count; ++i) {
                        int nb = nbrs[i].nb;
                        if (visited[nb]) continue;
                        visited[nb] = true;
                        flipped[nb] = nbrs[i].sameDir ? !curFlip : curFlip;
                        queue.Enqueue(nb);
                    }
                }
            }

            for (int t = 0; t < nt; ++t) {
                if (flipped[t]) {
                    Index3i f = tris[t];
                    tris[t] = new Index3i(f.a, f.c, f.b);
                }
            }
        }

        static void add_orient_edge(Dictionary<long, List<(int tri, int u, int v)>> map, int u, int v, int tri)
        {
            long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
            if (!map.TryGetValue(key, out var list)) {
                list = new List<(int, int, int)>(2);
                map[key] = list;
            }
            list.Add((tri, u, v));
        }

        static int[] canonicalize_coplanar_normals(int nt, Vector3d[] verts, List<Index3i> tris,
            bool[] faceValid, Vector3d[] faceNormal,
            List<long> edgeKeys, Dictionary<long, List<HalfEdgeInc>> edgeInc)
        {
            var adj = new List<int>[nt];
            for (int t = 0; t < nt; ++t) adj[t] = new List<int>(3);

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

        static void link_umbrella_tris(Dictionary<long, List<int>> vertTriAdj, int v, int t0, int t1)
        {
            add_vt_link(vertTriAdj, vt_key(v, t0), t1);
            add_vt_link(vertTriAdj, vt_key(v, t1), t0);
        }

        static void add_vt_link(Dictionary<long, List<int>> map, long key, int nbTri)
        {
            if (!map.TryGetValue(key, out var list)) {
                list = new List<int>(2);
                map[key] = list;
            }
            if (!list.Contains(nbTri))
                list.Add(nbTri);
        }

        static long vt_key(int v, int tri)
        {
            return ((long)v << 32) | (uint)tri;
        }

        static void record_convex_edge(int u, int v, int t0, int t1, (Vector3d n, double rhs)? bevelPlane,
            Dictionary<long, int> vertTriConvexCount, List<(Vector3d n, double rhs)>[] triBevels)
        {
            inc_vt_count(vertTriConvexCount, vt_key(u, t0));
            inc_vt_count(vertTriConvexCount, vt_key(u, t1));
            inc_vt_count(vertTriConvexCount, vt_key(v, t0));
            inc_vt_count(vertTriConvexCount, vt_key(v, t1));
            if (bevelPlane.HasValue) {
                if (triBevels[t0] == null) triBevels[t0] = new List<(Vector3d, double)>();
                if (triBevels[t1] == null) triBevels[t1] = new List<(Vector3d, double)>();
                triBevels[t0].Add(bevelPlane.Value);
                triBevels[t1].Add(bevelPlane.Value);
            }
        }

        static void inc_vt_count(Dictionary<long, int> map, long key)
        {
            map.TryGetValue(key, out int c);
            map[key] = c + 1;
        }

        /// <summary>
        /// Handles an even-valence non-manifold edge (e.g. two shells touching along a shared edge) by sorting
        /// incident half-edges cyclically around the directed axis (uRef -> vRef). Adjacent opposite-winding pairs
        /// bounding interior sectors link their respective umbrellas; exterior sectors &gt; pi emit +n wedges, and
        /// interior sectors &gt; pi emit -n wedges.
        /// </summary>
        static void process_nonmanifold_edge(DMesh3 combined, Vector3d[] workVerts, Vector3d[] faceNormal, int[] patchId,
            int uRef, int vRef, List<HalfEdgeInc> inc, bool doPos, bool doNeg, double dWork, double miterLimit,
            Dictionary<long, List<int>> vertTriAdj,
            Dictionary<long, int> vertTriPosConvexCount, List<(Vector3d n, double rhs)>[] triPosBevels,
            Dictionary<long, int> vertTriNegConvexCount, List<(Vector3d n, double rhs)>[] triNegBevels,
            ref int polyCount)
        {
            int fwd = 0, rev = 0;
            for (int i = 0; i < inc.Count; ++i) {
                if (inc[i].u == uRef && inc[i].v == vRef) fwd++;
                else if (inc[i].u == vRef && inc[i].v == uRef) rev++;
            }
            if (fwd != rev || fwd == 0)
                return;

            Vector3d pa = workVerts[uRef], pb = workVerts[vRef];
            Vector3d axis = pb - pa;
            double axisLen = axis.Length;
            if (!(axisLen > 0)) return;
            axis /= axisLen;
            orthonormal_basis(axis, out Vector3d bx, out Vector3d by);

            var sorted = new List<(double angle, HalfEdgeInc he)>(inc.Count);
            for (int i = 0; i < inc.Count; ++i) {
                Vector3d d = workVerts[inc[i].opp] - pa;
                d -= d.Dot(axis) * axis;
                double ang = Math.Atan2(d.Dot(by), d.Dot(bx));
                if (ang < 0) ang += 2.0 * Math.PI;
                sorted.Add((ang, inc[i]));
            }
            sorted.Sort((a, b) => {
                int cmp = a.angle.CompareTo(b.angle);
                return cmp != 0 ? cmp : a.he.tri.CompareTo(b.he.tri);
            });

            int m = sorted.Count;
            for (int i = 0; i < m; ++i) {
                var cur = sorted[i];
                var nxt = sorted[(i + 1) % m];
                bool curFwd = cur.he.u == uRef;
                bool nxtFwd = nxt.he.u == uRef;
                if (curFwd == nxtFwd)
                    continue;
                double span = nxt.angle - cur.angle;
                if (span <= 0) span += 2.0 * Math.PI;

                if (!curFwd && nxtFwd) {
                    // Interior sector from (vRef -> uRef) CCW to (uRef -> vRef): link into the same shell umbrella.
                    link_umbrella_tris(vertTriAdj, uRef, cur.he.tri, nxt.he.tri);
                    link_umbrella_tris(vertTriAdj, vRef, cur.he.tri, nxt.he.tri);
                    if (doNeg && span > Math.PI + 1e-10 && patchId[cur.he.tri] != patchId[nxt.he.tri]) {
                        if (emit_edge_wedge(combined, pb, pa, vRef, uRef, workVerts[nxt.he.opp],
                            -faceNormal[nxt.he.tri], -faceNormal[cur.he.tri], dWork, miterLimit, isNegSide: true, out var bevelPlane)) {
                            polyCount++;
                            record_convex_edge(uRef, vRef, nxt.he.tri, cur.he.tri, bevelPlane, vertTriNegConvexCount, triNegBevels);
                        }
                    }
                } else if (curFwd && !nxtFwd) {
                    // Exterior sector from (uRef -> vRef) CCW to (vRef -> uRef): convex on +n when span > pi.
                    if (doPos && span > Math.PI + 1e-10 && patchId[cur.he.tri] != patchId[nxt.he.tri]) {
                        if (emit_edge_wedge(combined, pa, pb, uRef, vRef, workVerts[cur.he.opp],
                            faceNormal[cur.he.tri], faceNormal[nxt.he.tri], dWork, miterLimit, isNegSide: false, out var bevelPlane)) {
                            polyCount++;
                            record_convex_edge(uRef, vRef, cur.he.tri, nxt.he.tri, bevelPlane, vertTriPosConvexCount, triPosBevels);
                        }
                    }
                }
            }
        }

        static List<List<int>> partition_vertex_umbrellas(int v, List<int> incTris, Dictionary<long, List<int>> vertTriAdj)
        {
            var result = new List<List<int>>(1);
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            for (int i = 0; i < incTris.Count; ++i) {
                int seed = incTris[i];
                if (!visited.Add(seed)) continue;
                var comp = new List<int>();
                queue.Enqueue(seed);
                while (queue.Count > 0) {
                    int cur = queue.Dequeue();
                    comp.Add(cur);
                    if (vertTriAdj.TryGetValue(vt_key(v, cur), out var nbrs)) {
                        for (int j = 0; j < nbrs.Count; ++j) {
                            int nb = nbrs[j];
                            if (visited.Add(nb))
                                queue.Enqueue(nb);
                        }
                    }
                }
                comp.Sort();
                result.Add(comp);
            }
            return result;
        }

        static int count_umbrella_convex_edges(int v, List<int> umbrella, Dictionary<long, int> vertTriConvexCount)
        {
            int sum = 0;
            for (int i = 0; i < umbrella.Count; ++i) {
                if (vertTriConvexCount.TryGetValue(vt_key(v, umbrella[i]), out int c))
                    sum += c;
            }
            // Each convex edge in the umbrella contributes 1 to both incident triangles in the umbrella.
            return sum / 2;
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
        /// where (n1 x n2) . (pb - pa) > 0. Emits an explicit extruded convex prism whose shared side walls
        /// match the adjacent +n or -n face prisms bit for bit on their canonical diagonals.
        /// </summary>
        static bool emit_edge_wedge(DMesh3 dst, Vector3d pa, Vector3d pb, int idA, int idB, Vector3d pc0,
            Vector3d n1, Vector3d n2, double d, double miterLimit, bool isNegSide,
            out (Vector3d n, double rhs)? bevelPlane)
        {
            bevelPlane = null;
            double dot = MathUtil.Clamp(n1.Dot(n2), -1.0, 1.0);
            if (dot > CoplanarDotThreshold)
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
                    profile.Add((d * miterRatio) * u);
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
                        bevelPlane = (u, miterLimit * d);
                        profile.Add(b1);
                        profile.Add(b2);
                    } else {
                        bevelPlane = (u, miterLimit * d);
                        profile.Add((d * miterLimit) * u);
                    }
                }
            } else {
                // Exact or near-180-degree razor fold (n1 ~ -n2): emit orthogonal bevel cap pointing away from pc0.
                Vector3d edgeDir = pb - pa;
                Vector3d outDir = n1.Cross(edgeDir);
                double oLen = outDir.Length;
                if (!(oLen > 1e-12))
                    return false;
                Vector3d u = outDir / oLen;
                if (u.Dot(pc0 - pa) > 0)
                    u = -u;
                Vector3d b1 = d * (n1 + u);
                Vector3d b2 = d * (n2 + u);
                bevelPlane = (u, d);
                profile.Add(b1);
                profile.Add(b2);
            }

            profile.Add(d * n2);

            // Verify consistent strictly non-zero orientation across every fan triangle of the cross-section
            // (never using profile[k - 1] alone, which is collinear with profile[1] on a 180-degree razor fold).
            int k = profile.Count;
            Vector3d a0 = pa + profile[0], b0 = pb + profile[0];
            int firstSign = ExactPredicates.Orient3d(a0, pa + profile[1], pa + profile[2], b0);
            if (firstSign == 0)
                return false;
            for (int i = 2; i < k - 1; ++i) {
                int s = ExactPredicates.Orient3d(a0, pa + profile[i], pa + profile[i + 1], b0);
                if (s != firstSign)
                    return false;
            }
            if (firstSign < 0) {
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

            // Side quads with exact canonical diagonals matching both +n and -n face prisms:
            //   - On +n side: prism has bottom=base, top=base+off => canonical diagonal pairs base(minId) with top(maxId).
            //   - On -n side: prism has bottom=base-off, top=base => canonical diagonal pairs off(minId) with base(maxId).
            for (int i = 0; i < k; ++i) {
                int j = (i + 1) % k;
                if (i == 0 && isNegSide) {
                    append_canonical_quad(dst, va[1], vb[1], vb[0], va[0], idA, idB);
                } else if (i == k - 1) {
                    if (isNegSide)
                        append_canonical_quad(dst, vb[k - 1], va[k - 1], va[0], vb[0], idB, idA);
                    else
                        append_canonical_quad(dst, va[0], vb[0], vb[k - 1], va[k - 1], idA, idB);
                } else {
                    append_canonical_quad(dst, vb[i], va[i], va[j], vb[j], idB, idA);
                }
            }
            return true;
        }

        /// <summary>
        /// Emits the closed convex mitered corner polyhedron for a vertex umbrella via exact indexed half-space
        /// clipping in normalized local offset coordinates (delta / d). Handles arbitrary vertex valences (e.g.
        /// high-valence cone apices) without collinear-horizon degeneracies or valence cutoffs.
        /// </summary>
        static bool emit_vertex_corner_clipped(DMesh3 dst, int v, List<int> umbrella, Vector3d[] workVerts,
            List<Index3i> tris, Vector3d[] faceNormals, double side,
            List<(Vector3d n, double rhs)>[] triBevels, double d, double miterLimit)
        {
            // 1. Collect deduplicated face normals, edge bevel planes, and incident edge half-spaces for this umbrella.
            var normals = new List<Vector3d>(umbrella.Count);
            var planes = new List<(Vector3d n, double rhs)>(umbrella.Count * 2 + 4);
            var edgePlanes = new List<(Vector3d dir, double rhs)>(umbrella.Count + 2);
            Vector3d vPos = workVerts[v];

            for (int i = 0; i < umbrella.Count; ++i) {
                int tid = umbrella[i];
                Vector3d n = side * faceNormals[tid];
                bool dup = false;
                for (int j = 0; j < normals.Count; ++j) {
                    if (normals[j].Dot(n) > CoplanarDotThreshold) { dup = true; break; }
                }
                if (!dup) {
                    normals.Add(n);
                    planes.Add((n, 1.0));
                }

                // Incident edges of tid touching v define the normal-cone half-spaces t_e . delta <= overlap,
                // where a small bounded positive overlap inside each adjacent edge wedge eliminates 1-ulp
                // grazing cracks on oblique/rotated faces while staying strictly inside the true offset solid.
                Index3i f = tris[tid];
                add_unique_edge_plane(edgePlanes, vPos, f.a == v ? workVerts[f.b] : workVerts[f.a], d);
                add_unique_edge_plane(edgePlanes, vPos, f.c == v ? workVerts[f.b] : workVerts[f.c], d);
            }
            if (normals.Count < 3)
                return false;

            for (int i = 0; i < umbrella.Count; ++i) {
                var bev = triBevels[umbrella[i]];
                if (bev == null) continue;
                for (int j = 0; j < bev.Count; ++j)
                    add_unique_plane_norm(planes, bev[j].n, bev[j].rhs / d);
            }

            // 2. Start from a power-of-two cube [-B, +B]^3 in normalized (delta / d) space and clip by face, bevel, and edge planes.
            double B = 16.0;
            while (B < 4.0 * miterLimit) B *= 2.0;
            var poly = ConvexPolyhedron.CreateBox(B);
            for (int i = 0; i < planes.Count; ++i) {
                if (!poly.Clip(planes[i].n, planes[i].rhs))
                    return false;
            }
            for (int i = 0; i < edgePlanes.Count; ++i) {
                if (!poly.Clip(edgePlanes[i].dir, edgePlanes[i].rhs))
                    return false;
            }

            // 3. Reject unbounded saddle corners (any vertex reaching the artificial box boundary).
            double boundLimit = 0.5 * B;
            double maxLenSq = 0;
            Vector3d farthest = Vector3d.Zero;
            var used = poly.CollectUsedVertexIndices();
            if (used.Count < 4)
                return false;
            for (int i = 0; i < used.Count; ++i) {
                Vector3d p = poly.Verts[used[i]];
                if (Math.Abs(p.x) >= boundLimit || Math.Abs(p.y) >= boundLimit || Math.Abs(p.z) >= boundLimit)
                    return false;
                double l2 = p.LengthSquared;
                if (l2 > maxLenSq) {
                    maxLenSq = l2;
                    farthest = p;
                }
            }

            // 4. Corner-only bevel clamp: if the exact miter tip still exceeds MiterLimit (e.g. tetrahedron or acute cone
            //    apex whose edges were not individually bevelled), clip cleanly by the perpendicular corner bevel plane.
            double maxLen = Math.Sqrt(maxLenSq);
            if (maxLen > miterLimit * (1.0 + 1e-5)) {
                Vector3d uCorner = farthest / maxLen;
                if (!poly.Clip(uCorner, miterLimit))
                    return false;
            }

            return poly.EmitScaled(dst, vPos, d);
        }

        static void add_unique_edge_plane(List<(Vector3d dir, double rhs)> edgePlanes, Vector3d vPos, Vector3d otherPos, double d)
        {
            Vector3d e = otherPos - vPos;
            double len = e.Length;
            if (!(len > 0)) return;
            Vector3d dir = e / len;
            double overlap = Math.Min(0.1, 0.25 * (len / d));
            for (int i = 0; i < edgePlanes.Count; ++i) {
                if (edgePlanes[i].dir.Dot(dir) > CoplanarDotThreshold) {
                    if (overlap < edgePlanes[i].rhs)
                        edgePlanes[i] = (edgePlanes[i].dir, overlap);
                    return;
                }
            }
            edgePlanes.Add((dir, overlap));
        }

        static void add_unique_plane_norm(List<(Vector3d n, double rhs)> planes, Vector3d n, double rhs)
        {
            for (int i = 0; i < planes.Count; ++i) {
                if (planes[i].n.Dot(n) > CoplanarDotThreshold && Math.Abs(planes[i].rhs - rhs) <= 1e-9)
                    return;
            }
            planes.Add((n, rhs));
        }

        static void orthonormal_basis(Vector3d axis, out Vector3d bx, out Vector3d by)
        {
            Vector3d refVec = Math.Abs(axis.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY;
            bx = axis.Cross(refVec);
            bx.Normalize();
            by = axis.Cross(bx);
        }

        /// <summary>
        /// Indexed convex polyhedron with watertight Sutherland-Hodgman half-space clipping.
        /// Every edge cut is cached by canonical undirected vertex-index pair (min(u,v), max(u,v)) and the cap
        /// face chains exact unmatched reversed half-edges, guaranteeing closed manifold output by construction.
        /// </summary>
        sealed class ConvexPolyhedron
        {
            public readonly List<Vector3d> Verts;
            public List<List<int>> Faces;

            ConvexPolyhedron(List<Vector3d> verts, List<List<int>> faces)
            {
                Verts = verts;
                Faces = faces;
            }

            public static ConvexPolyhedron CreateBox(double b)
            {
                var v = new List<Vector3d>(16) {
                    new Vector3d(-b, -b, -b), // 0
                    new Vector3d( b, -b, -b), // 1
                    new Vector3d( b,  b, -b), // 2
                    new Vector3d(-b,  b, -b), // 3
                    new Vector3d(-b, -b,  b), // 4
                    new Vector3d( b, -b,  b), // 5
                    new Vector3d( b,  b,  b), // 6
                    new Vector3d(-b,  b,  b)  // 7
                };
                // Outward CCW faces
                var f = new List<List<int>>(12) {
                    new List<int> { 0, 3, 2, 1 }, // -z
                    new List<int> { 4, 5, 6, 7 }, // +z
                    new List<int> { 0, 1, 5, 4 }, // -y
                    new List<int> { 3, 7, 6, 2 }, // +y
                    new List<int> { 0, 4, 7, 3 }, // -x
                    new List<int> { 1, 2, 6, 5 }  // +x
                };
                return new ConvexPolyhedron(v, f);
            }

            public bool Clip(Vector3d n, double rhs)
            {
                const double Eps = 1e-11;
                int nv = Verts.Count;
                var dist = new double[nv];
                var side = new sbyte[nv]; // -1 inside, 0 on plane, +1 outside
                bool anyOutside = false, anyInside = false;

                // Evaluate only vertices referenced by current faces so stale vertices never trigger false clips.
                var used = new bool[nv];
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    for (int i = 0; i < face.Count; ++i)
                        used[face[i]] = true;
                }
                for (int i = 0; i < nv; ++i) {
                    if (!used[i]) continue;
                    double d = n.Dot(Verts[i]) - rhs;
                    dist[i] = d;
                    if (d > Eps) { side[i] = 1; anyOutside = true; }
                    else if (d < -Eps) { side[i] = -1; anyInside = true; }
                    else { side[i] = 0; }
                }

                if (!anyOutside)
                    return true;
                if (!anyInside) {
                    Faces.Clear();
                    return false;
                }

                var edgeSplit = new Dictionary<long, int>();
                var nextFaces = new List<List<int>>(Faces.Count + 1);
                var planeEdges = new HashSet<long>();
                var planeEdgeList = new List<(int a, int b)>();

                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    int m = face.Count;
                    var clipped = new List<int>(m + 1);
                    bool hasStrictlyInside = false;
                    for (int i = 0; i < m; ++i) {
                        int u = face[i];
                        int v = face[(i + 1) % m];
                        sbyte su = side[u];
                        sbyte sv = side[v];
                        if (su <= 0) {
                            clipped.Add(u);
                            if (su < 0) hasStrictlyInside = true;
                        }
                        if ((su < 0 && sv > 0) || (su > 0 && sv < 0)) {
                            int mid = get_or_create_split(u, v, n, rhs, dist, edgeSplit);
                            clipped.Add(mid);
                        }
                    }

                    // Remove consecutive duplicate vertex IDs
                    int write = 0;
                    for (int i = 0; i < clipped.Count; ++i) {
                        if (write == 0 || clipped[i] != clipped[write - 1])
                            clipped[write++] = clipped[i];
                    }
                    if (write > 1 && clipped[write - 1] == clipped[0])
                        write--;
                    if (write < clipped.Count)
                        clipped.RemoveRange(write, clipped.Count - write);

                    if (clipped.Count < 3 || !hasStrictlyInside)
                        continue;

                    nextFaces.Add(clipped);
                    for (int i = 0; i < clipped.Count; ++i) {
                        int a = clipped[i];
                        int b = clipped[(i + 1) % clipped.Count];
                        bool aOn = a >= nv || side[a] == 0;
                        bool bOn = b >= nv || side[b] == 0;
                        if (aOn && bOn) {
                            long dk = dir_edge_key(a, b);
                            if (planeEdges.Add(dk))
                                planeEdgeList.Add((a, b));
                        }
                    }
                }

                // Chain unmatched plane half-edges (reversed: b -> a) into the cap face(s).
                var capNext = new Dictionary<int, int>();
                var capStarts = new List<int>();
                for (int i = 0; i < planeEdgeList.Count; ++i) {
                    int a = planeEdgeList[i].a;
                    int b = planeEdgeList[i].b;
                    if (!planeEdges.Contains(dir_edge_key(b, a))) {
                        capNext[b] = a;
                        capStarts.Add(b);
                    }
                }

                if (capStarts.Count >= 3) {
                    capStarts.Sort();
                    var usedCap = new HashSet<int>();
                    for (int i = 0; i < capStarts.Count; ++i) {
                        int start = capStarts[i];
                        if (usedCap.Contains(start)) continue;
                        var loop = new List<int>(capStarts.Count);
                        int cur = start;
                        int guard = capStarts.Count + 2;
                        while (guard-- > 0 && !usedCap.Contains(cur) && capNext.TryGetValue(cur, out int nxt)) {
                            usedCap.Add(cur);
                            loop.Add(cur);
                            cur = nxt;
                            if (cur == start)
                                break;
                        }
                        if (cur == start && loop.Count >= 3)
                            nextFaces.Add(loop);
                    }
                }

                Faces = nextFaces;
                return Faces.Count >= 4;
            }

            int get_or_create_split(int u, int v, Vector3d n, double rhs, double[] dist, Dictionary<long, int> edgeSplit)
            {
                int lo = u < v ? u : v;
                int hi = u < v ? v : u;
                long key = ((long)lo << 32) | (uint)hi;
                if (edgeSplit.TryGetValue(key, out int existing))
                    return existing;
                double dLo = dist[lo];
                double dHi = dist[hi];
                double denom = dLo - dHi;
                Vector3d p;
                if (Math.Abs(dLo) <= Math.Abs(dHi)) {
                    double t = denom != 0 ? MathUtil.Clamp(dLo / denom, 0.0, 1.0) : 0.5;
                    p = Verts[lo] + t * (Verts[hi] - Verts[lo]);
                } else {
                    double t = denom != 0 ? MathUtil.Clamp(-dHi / denom, 0.0, 1.0) : 0.5;
                    p = Verts[hi] + t * (Verts[lo] - Verts[hi]);
                }
                // Snap exact coordinate for axis-aligned planes only (keeps oblique points strictly on their parent edge).
                if (Math.Abs(n.x) == 1.0 && n.y == 0.0 && n.z == 0.0) p.x = rhs * n.x;
                else if (Math.Abs(n.y) == 1.0 && n.x == 0.0 && n.z == 0.0) p.y = rhs * n.y;
                else if (Math.Abs(n.z) == 1.0 && n.x == 0.0 && n.y == 0.0) p.z = rhs * n.z;
                int id = Verts.Count;
                Verts.Add(p);
                edgeSplit[key] = id;
                return id;
            }

            public List<int> CollectUsedVertexIndices()
            {
                var set = new HashSet<int>();
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    for (int i = 0; i < face.Count; ++i)
                        set.Add(face[i]);
                }
                var list = new List<int>(set);
                list.Sort();
                return list;
            }

            public bool EmitScaled(DMesh3 dst, Vector3d origin, double scale)
            {
                if (Faces.Count < 4)
                    return false;
                // Check positive volume before emitting so a degenerate flat slice is never appended.
                double vol6 = 0;
                Vector3d anchor = Verts[Faces[0][0]];
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    Vector3d a = Verts[face[0]] - anchor;
                    for (int i = 1; i < face.Count - 1; ++i)
                        vol6 += a.Cross(Verts[face[i]] - anchor).Dot(Verts[face[i + 1]] - anchor);
                }
                if (!(vol6 > 1e-12))
                    return false;

                var vmap = new int[Verts.Count];
                for (int i = 0; i < vmap.Length; ++i) vmap[i] = -1;
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    int v0 = get_dst_vert(dst, vmap, face[0], origin, scale);
                    for (int i = 1; i < face.Count - 1; ++i) {
                        int v1 = get_dst_vert(dst, vmap, face[i], origin, scale);
                        int v2 = get_dst_vert(dst, vmap, face[i + 1], origin, scale);
                        dst.AppendTriangle(v0, v1, v2);
                    }
                }
                return true;
            }

            int get_dst_vert(DMesh3 dst, int[] vmap, int idx, Vector3d origin, double scale)
            {
                if (vmap[idx] >= 0) return vmap[idx];
                int vid = dst.AppendVertex(origin + scale * Verts[idx]);
                vmap[idx] = vid;
                return vid;
            }
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
