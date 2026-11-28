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
    /// Output verification: when <see cref="SafeRounding"/> is true, the boolean runs with strict rounding and the returned
    /// mesh is verified in world coordinates to be closed, 2-manifold, positively oriented, and free of degenerate triangles
    /// and crossing self-intersections (exact touching contacts where offset shells meet are split into manifold pairs and
    /// counted in <see cref="Contacts"/>).
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
            /// <summary> Null or empty mesh, all degenerate triangles, slivers breaking closed-solid adjacency/components, non-finite coordinates, or extent outside [2^-900, 2^900]. </summary>
            InvalidInput = 1,
            /// <summary> Outward or Inward mode was requested on a mesh that is not closed in the winding sense (open or inconsistently oriented). </summary>
            NotClosed = 2,
            /// <summary> Outward or Inward mode was requested on a closed mesh with an inside-out shell. </summary>
            InsideOut = 3,
            /// <summary> Invalid Mode, Distance &lt;= 0 or below world floating-point resolution / outside [1e-12, 1e6/MiterLimit] of extent, or MiterLimit outside [1, 1e4]. </summary>
            InvalidParameters = 4,
            /// <summary> Inward inset consumed the entire solid cleanly (empty result). </summary>
            Empty = 5,
            /// <summary> Exact arrangement / boolean, strict safe rounding, or world-coordinate cleanliness verification failed (see <see cref="Error"/>). </summary>
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
        /// Maximum miter extrusion ratio relative to <see cref="Distance"/> (must be in [1, 1e4], default 4.0).
        /// Acute convex creases and corners whose exact miter distance exceeds MiterLimit * Distance are bevel-clamped.
        /// </summary>
        public double MiterLimit = 4.0;
        /// <summary> Strict safe rounding and world-coordinate cleanliness verification (default true). </summary>
        public bool SafeRounding = true;
        /// <summary> Optional cancellation token propagated through polyhedron generation and <see cref="ExactMeshBoolean"/>, and checked around world-space rounding repair. </summary>
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
        const double MaxMiterLimit = 1e4;
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
            if (!MathUtil.IsFinite(MiterLimit) || !(MiterLimit >= 1.0) || MiterLimit > MaxMiterLimit) {
                fail(OffsetStatus.InvalidParameters, "MiterLimit must be finite and in [1, 1e4]");
                return false;
            }

            // Collect non-degenerate triangles welded by exact position (so unwelded STL soups work identically).
            // Skip coincident-vertex triangles BEFORE updating box so stray degenerate points cannot inflate extent.
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
                if (a == b || b == c || c == a)
                    continue;
                int ia = map_vert_pos(a, posToIdx, vertsOrig, ref box);
                int ib = map_vert_pos(b, posToIdx, vertsOrig, ref box);
                int ic = map_vert_pos(c, posToIdx, vertsOrig, ref box);
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

            // Reject offsets below floating-point resolution at world coordinates (e.g. sub-ulp Distance at UTM origin)
            // so unscaling can never silently quantize the offset back to zero.
            double maxCoord = max_abs_coord(box);
            if (maxCoord > 0 && MathUtil.IsFinite(maxCoord)) {
                double minWorldOffset = ExactPoint3.ScaleB(1.0, pow2_exp(maxCoord) - 42); // 1024 * ulp(maxCoord)
                if (Distance < minWorldOffset) {
                    fail(OffsetStatus.InvalidParameters, "Distance is below floating-point resolution at mesh world coordinates");
                    return false;
                }
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

            bool doPos = Mode == OffsetModes.Outward || Mode == OffsetModes.DoubleSided;
            bool doNeg = Mode == OffsetModes.Inward || Mode == OffsetModes.DoubleSided;

            // Validate triangles against collinearity/slivers and exact prism orientation before building topology.
            // Rescale edge vectors by a power of two per triangle so tiny valid components do not underflow.
            int nt = tris.Count;
            var faceValid = new bool[nt];
            var faceNormal = new Vector3d[nt];
            int validFaces = 0;
            for (int t = 0; t < nt; ++t) {
                Index3i f = tris[t];
                Vector3d a = workVerts[f.a], b = workVerts[f.b], c = workVerts[f.c];
                Vector3d e1 = b - a, e2 = c - a, e3 = c - b;
                double maxEdgeComp = Math.Max(max_abs(e1), Math.Max(max_abs(e2), max_abs(e3)));
                if (!(maxEdgeComp > 0)) continue;
                int eExp = pow2_exp(maxEdgeComp);
                Vector3d se1 = scale_vec(e1, -eExp), se2 = scale_vec(e2, -eExp), se3 = scale_vec(e3, -eExp);
                double maxEdgeSq = Math.Max(se1.LengthSquared, Math.Max(se2.LengthSquared, se3.LengthSquared));
                Vector3d n = se1.Cross(se2);
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

            // For DoubleSided thickening, consistently orient each edge-connected manifold sheet component over
            // valid triangles only so slivers cannot block orientation propagation across creases.
            if (Mode == OffsetModes.DoubleSided)
                orient_manifold_components_deterministically(tris, faceValid, faceNormal);

            // Build workMesh preserving ALL triangles (duplicating vertices on non-manifold edges or >32,766 valence
            // so DMesh3.AppendTriangle never silently drops triangles; ExactMeshBoolean welds by exact position).
            var workMesh = new DMesh3();
            for (int i = 0; i < nv; ++i) workMesh.AppendVertex(workVerts[i]);
            for (int i = 0; i < nt; ++i)
                append_triangle_preserve_all(workMesh, workVerts, tris[i]);

            if (Mode == OffsetModes.Outward || Mode == OffsetModes.Inward) {
                if (!ExactMeshBoolean.IsWindingClosed(workMesh)) {
                    fail(OffsetStatus.NotClosed, "Outward and Inward modes require a mesh closed in the winding sense");
                    return false;
                }
                double vol = anchored_signed_volume(workVerts, tris);
                if (vol < 0) {
                    fail(OffsetStatus.InsideOut, "closed mesh has negative signed volume");
                    return false;
                }
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

            // In Outward/Inward modes, if degenerate/sliver triangles were filtered out, verify that:
            //   (a) remaining valid faces are still winding-balanced (no broken edge adjacency), AND
            //   (b) no entire disconnected closed component was silently dropped by faceValid filtering.
            if ((Mode == OffsetModes.Outward || Mode == OffsetModes.Inward) && validFaces < nt) {
                if (!edges_are_winding_balanced(edgeKeys, edgeInc) || any_component_has_no_valid_faces(nv, tris, faceValid)) {
                    fail(OffsetStatus.InvalidInput, "closed mesh contains degenerate or sliver triangles that break surface adjacency or drop a component");
                    return false;
                }
            }

            // Canonicalize face normals across coplanar consistently-oriented manifold patches so rotated flat quads
            // share bit-identical offset planes and exact diagonal walls (avoiding 1-ulp coplanar normal drift).
            var patchId = canonicalize_coplanar_normals(nt, workVerts, tris, faceValid, faceNormal, edgeKeys, edgeInc);

            // Pre-build vertex umbrella links & boundary rim edges BEFORE emitting edge wedges so asymmetric convex
            // edge wedges can clip against adjacent endpoint faces (preventing protrusions on hip roofs / asymmetric vertices).
            var vertTriAdj = new Dictionary<long, List<int>>();
            var vertBoundaryEdges = Mode == OffsetModes.DoubleSided ? new List<(int tri, int otherVert, int oppVert)>[nv] : null;
            var nonManifoldSectors = new List<NonManifoldSector>();

            foreach (long key in edgeKeys) {
                var inc = edgeInc[key];
                if (inc.Count == 1 && vertBoundaryEdges != null) {
                    var e = inc[0];
                    add_boundary_edge(vertBoundaryEdges, e.u, e.tri, e.v, e.opp);
                    add_boundary_edge(vertBoundaryEdges, e.v, e.tri, e.u, e.opp);
                } else if (inc.Count == 2) {
                    var e0 = inc[0];
                    var e1 = inc[1];
                    if (e0.u == e1.v && e0.v == e1.u) {
                        link_umbrella_tris(vertTriAdj, e0.u, e0.tri, e1.tri);
                        link_umbrella_tris(vertTriAdj, e0.v, e0.tri, e1.tri);
                    }
                } else if (inc.Count >= 4 && (inc.Count % 2) == 0) {
                    int uRef = inc[0].u < inc[0].v ? inc[0].u : inc[0].v;
                    int vRef = inc[0].u < inc[0].v ? inc[0].v : inc[0].u;
                    collect_nonmanifold_sectors(workVerts, uRef, vRef, inc, vertTriAdj, nonManifoldSectors);
                }
            }

            // Partition each vertex into edge-connected umbrellas and map (v, tri) -> umbrella index.
            var vertUmbrellas = new List<List<int>>[nv];
            var vertTriToUmb = new Dictionary<long, int>();
            for (int v = 0; v < nv; ++v) {
                if (vertTris[v].Count == 0) continue;
                var umbs = partition_vertex_umbrellas(v, vertTris[v], vertTriAdj);
                vertUmbrellas[v] = umbs;
                for (int ui = 0; ui < umbs.Count; ++ui) {
                    var umb = umbs[ui];
                    for (int j = 0; j < umb.Count; ++j)
                        vertTriToUmb[vt_key(v, umb[j])] = ui;
                }
            }

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

            var vertPosConvexEdges = doPos ? new List<ConvexEdgeInfo>[nv] : null;
            var vertNegConvexEdges = doNeg ? new List<ConvexEdgeInfo>[nv] : null;

            // 2a. Manifold convex edge wedges (clipped against endpoint umbrella faces if an asymmetric corner would protrude).
            foreach (long key in edgeKeys) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var inc = edgeInc[key];
                if (inc.Count != 2)
                    continue;
                var e0 = inc[0];
                var e1 = inc[1];
                if (!(e0.u == e1.v && e0.v == e1.u))
                    continue;
                if (patchId[e0.tri] == patchId[e1.tri])
                    continue;

                Vector3d pa = workVerts[e0.u], pb = workVerts[e0.v], pc0 = workVerts[e0.opp], pc1 = workVerts[e1.opp];
                int orient = ExactPredicates.Orient3d(pa, pb, pc0, pc1);
                if (orient == 0) {
                    // Exact 180-degree razor fold (opposing coplanar faces sharing a crease edge).
                    if (faceNormal[e0.tri].Dot(faceNormal[e1.tri]) < -0.5) {
                        if (doPos && emit_edge_wedge(combined, pa, pb, e0.u, e0.v, pc0, faceNormal[e0.tri], faceNormal[e1.tri],
                            dWork, MiterLimit, isNegSide: false, e0.tri, vertUmbrellas, vertTriToUmb, faceNormal, out var bevelPos)) {
                            polyCount++;
                            record_vertex_convex_edge(vertPosConvexEdges, e0.u, e0.v, e0.tri, e1.tri, bevelPos, partnerNormal: null);
                        } else if (!doPos && doNeg && emit_edge_wedge(combined, pb, pa, e0.v, e0.u, pc0, -faceNormal[e0.tri], -faceNormal[e1.tri],
                            dWork, MiterLimit, isNegSide: true, e0.tri, vertUmbrellas, vertTriToUmb, faceNormal, out var bevelNeg)) {
                            polyCount++;
                            record_vertex_convex_edge(vertNegConvexEdges, e0.u, e0.v, e0.tri, e1.tri, bevelNeg, partnerNormal: null);
                        }
                    }
                    continue;
                }

                if (orient < 0 && doPos) {
                    if (emit_edge_wedge(combined, pa, pb, e0.u, e0.v, pc0, faceNormal[e0.tri], faceNormal[e1.tri],
                        dWork, MiterLimit, isNegSide: false, e0.tri, vertUmbrellas, vertTriToUmb, faceNormal, out var bevelPlane)) {
                        polyCount++;
                        record_vertex_convex_edge(vertPosConvexEdges, e0.u, e0.v, e0.tri, e1.tri, bevelPlane, partnerNormal: null);
                    }
                } else if (orient > 0 && doNeg) {
                    if (emit_edge_wedge(combined, pb, pa, e0.v, e0.u, pc0, -faceNormal[e0.tri], -faceNormal[e1.tri],
                        dWork, MiterLimit, isNegSide: true, e0.tri, vertUmbrellas, vertTriToUmb, faceNormal, out var bevelPlane)) {
                        polyCount++;
                        record_vertex_convex_edge(vertNegConvexEdges, e0.u, e0.v, e0.tri, e1.tri, bevelPlane, partnerNormal: null);
                    }
                }
            }

            // 2b. Non-manifold convex edge sectors (> pi exterior on +n, > pi interior on -n).
            for (int si = 0; si < nonManifoldSectors.Count; ++si) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var s = nonManifoldSectors[si];
                if (patchId[s.triA] == patchId[s.triB])
                    continue;
                if (s.isExterior && doPos) {
                    if (emit_edge_wedge(combined, workVerts[s.uRef], workVerts[s.vRef], s.uRef, s.vRef, workVerts[s.oppA],
                        faceNormal[s.triA], faceNormal[s.triB], dWork, MiterLimit, isNegSide: false, s.triA,
                        vertUmbrellas, vertTriToUmb, faceNormal, out var bevelPlane)) {
                        polyCount++;
                        // Record partner face normals so neither touching shell's corner overshoots the cross-shell wedge.
                        record_vertex_convex_edge(vertPosConvexEdges, s.uRef, s.vRef, s.triA, s.triB, bevelPlane,
                            partnerNormal: (faceNormal[s.triA], faceNormal[s.triB]));
                    }
                } else if (!s.isExterior && doNeg) {
                    if (emit_edge_wedge(combined, workVerts[s.vRef], workVerts[s.uRef], s.vRef, s.uRef, workVerts[s.oppA],
                        -faceNormal[s.triA], -faceNormal[s.triB], dWork, MiterLimit, isNegSide: true, s.triA,
                        vertUmbrellas, vertTriToUmb, faceNormal, out var bevelPlane)) {
                        polyCount++;
                        record_vertex_convex_edge(vertNegConvexEdges, s.uRef, s.vRef, s.triA, s.triB, bevelPlane,
                            partnerNormal: (-faceNormal[s.triA], -faceNormal[s.triB]));
                    }
                }
            }

            // 3. Per-(vertex, umbrella) mitered convex corner polyhedra via exact indexed half-space clipping.
            for (int v = 0; v < nv; ++v) {
                if (is_cancelled()) { fail(OffsetStatus.Cancelled, "cancelled"); return false; }
                var umbrellas = vertUmbrellas[v];
                if (umbrellas == null)
                    continue;

                var bndAtV = vertBoundaryEdges != null ? vertBoundaryEdges[v] : null;
                for (int u = 0; u < umbrellas.Count; ++u) {
                    var umb = umbrellas[u];
                    if (umb.Count < 3)
                        continue;

                    var umbSet = new HashSet<int>(umb);
                    if (doPos && collect_umbrella_convex_edges(vertPosConvexEdges[v], umbSet, out var posExtraPlanes) >= 2) {
                        if (emit_vertex_corner_clipped(combined, v, umb, umbSet, workVerts, tris, faceNormal, +1.0,
                            posExtraPlanes, bndAtV, dWork, MiterLimit))
                            polyCount++;
                    }
                    if (doNeg && collect_umbrella_convex_edges(vertNegConvexEdges[v], umbSet, out var negExtraPlanes) >= 2) {
                        if (emit_vertex_corner_clipped(combined, v, umb, umbSet, workVerts, tris, faceNormal, -1.0,
                            negExtraPlanes, bndAtV, dWork, MiterLimit))
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
            ExactMeshBoolean booleanOp;
            if (Mode == OffsetModes.Outward) {
                booleanOp = new ExactMeshBoolean(workMesh, combined, ExactMeshBoolean.Operations.Union) {
                    SafeRounding = SafeRounding,
                    StrictRounding = SafeRounding,
                    AllowOpenMeshes = false,
                    CountInsideOutCells = true,
                    Cancellation = Cancellation
                };
            } else if (Mode == OffsetModes.Inward) {
                booleanOp = new ExactMeshBoolean(workMesh, combined, ExactMeshBoolean.Operations.Difference) {
                    SafeRounding = SafeRounding,
                    StrictRounding = SafeRounding,
                    AllowOpenMeshes = false,
                    CountInsideOutCells = true,
                    Cancellation = Cancellation
                };
            } else {
                booleanOp = new ExactMeshBoolean(combined, new DMesh3(), ExactMeshBoolean.Operations.Union) {
                    SafeRounding = SafeRounding,
                    StrictRounding = SafeRounding,
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

            if ((Mode == OffsetModes.Outward || Mode == OffsetModes.Inward)
                && ((booleanOp.InsideOutCellsOfInput != null && booleanOp.InsideOutCellsOfInput[0] > 0)
                    || booleanOp.InsideOutUndecided > 0)) {
                fail(OffsetStatus.InsideOut, "closed input mesh contains an inside-out shell");
                return false;
            }

            if (SafeRounding && !booleanOp.RoundingClean) {
                fail(OffsetStatus.Failed, "exact boolean rounding did not produce a clean result");
                return false;
            }

            DMesh3 res = booleanOp.Result;
            if (res == null || res.TriangleCount == 0) {
                if (Mode == OffsetModes.Inward) {
                    Result = new DMesh3();
                    Status = OffsetStatus.Empty;
                    RoundingClean = SafeRounding && booleanOp.RoundingClean;
                    return true;
                }
                fail(OffsetStatus.Failed, "exact boolean returned an empty mesh");
                return false;
            }

            // 5. Cleanliness verification & unscaling.
            //    - When origin == 0, unscaling is an exact power-of-two ScaleB: verify in normalized coordinates so
            //      extreme scales in [2^-900, 2^900] never underflow/overflow volume/area inside NotCleanReason.
            //    - When origin != 0 (e.g. UTM), record exact pre-unscale point IDs so newly snapped UTM false contacts
            //      are caught and repaired with SharedPositionsTouch = true and strict tolerances.
            if (origin == Vector3d.Zero) {
                if (SafeRounding) {
                    string reason = MeshRobustRepair.NotCleanReason(res, checkSelfIntersections: true);
                    bool pureContacts = is_pure_contacts_only(res, reason);
                    if (reason != "" && !pureContacts) {
                        fail(OffsetStatus.Failed, "output failed cleanliness verification: " + reason);
                        return false;
                    }
                    Contacts = count_contacts(res);
                    RoundingClean = booleanOp.RoundingClean && (reason == "" || pureContacts);
                } else {
                    Contacts = count_contacts(res);
                    RoundingClean = false;
                }
                if (!unscale_mesh(res, origin, scaleExp)) {
                    fail(OffsetStatus.Failed, "unscaled coordinates overflowed double range");
                    return false;
                }
            } else {
                Dictionary<int, int> prePointIds = null;
                int preContacts = 0;
                if (SafeRounding)
                    prePointIds = build_point_ids(res, out preContacts);

                if (!unscale_mesh(res, origin, scaleExp)) {
                    fail(OffsetStatus.Failed, "unscaled coordinates overflowed double range");
                    return false;
                }

                if (SafeRounding) {
                    // Temporarily shift exponent into normal range if world coordinates exceed [2^-100, 2^100]
                    // (exact bit-preserving ScaleB keeps world-coordinate quantization intact while avoiding NaN/OutsideRange).
                    int checkShift = extreme_world_shift(res);
                    if (checkShift != 0)
                        scale_mesh_inplace(res, -checkShift);

                    int postContacts = count_contacts(res);
                    bool hasNewFalseContact = postContacts > preContacts;
                    string reason = MeshRobustRepair.NotCleanReason(res, checkSelfIntersections: true);
                    bool pureContacts = !hasNewFalseContact && is_pure_contacts_only(res, reason);
                    bool worldRepairOk = true;

                    if (hasNewFalseContact || (reason != "" && !pureContacts)) {
                        if (is_cancelled()) {
                            fail(OffsetStatus.Cancelled, "cancelled before world-coordinate rounding repair");
                            return false;
                        }
                        var repair = new MeshRoundingRepair(res) {
                            SharedPositionsTouch = true,
                            UlpFactor = 0,
                            AbsoluteTolerance = 0,
                            PointIdF = vid => prePointIds.TryGetValue(vid, out int pid) ? pid : vid
                        };
                        bool repaired = repair.Apply();
                        if (is_cancelled()) {
                            fail(OffsetStatus.Cancelled, "cancelled during world-coordinate rounding repair");
                            return false;
                        }
                        worldRepairOk = repaired && repair.IsClean && repair.RemovedResolvable == 0 && repair.RemainingFalseContacts == 0;
                        reason = MeshRobustRepair.NotCleanReason(res, checkSelfIntersections: true);
                        pureContacts = is_pure_contacts_only(res, reason);
                    }

                    if (checkShift != 0)
                        scale_mesh_inplace(res, checkShift);

                    if (!worldRepairOk || (reason != "" && !pureContacts)) {
                        fail(OffsetStatus.Failed, "output failed world-coordinate cleanliness verification: " + (reason != "" ? reason : "rounding repair left defects"));
                        return false;
                    }
                    if (res.TriangleCount == 0) {
                        if (Mode == OffsetModes.Inward && worldRepairOk) {
                            Result = new DMesh3();
                            Status = OffsetStatus.Empty;
                            RoundingClean = true;
                            return true;
                        }
                        fail(OffsetStatus.Failed, "rounding repair left an empty mesh");
                        return false;
                    }
                    Contacts = count_contacts(res);
                    RoundingClean = booleanOp.RoundingClean && worldRepairOk && (reason == "" || pureContacts);
                } else {
                    Contacts = count_contacts(res);
                    RoundingClean = false;
                }
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

        struct NonManifoldSector
        {
            public int uRef, vRef, triA, triB, oppA;
            public bool isExterior;
        }

        struct ConvexEdgeInfo
        {
            public int triA, triB;
            public (Vector3d n, double rhs)? bevel;
            public (Vector3d nA, Vector3d nB)? partnerNormals;
        }

        static double max_abs(Vector3d v)
        {
            return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
        }

        static double max_abs_coord(AxisAlignedBox3d b)
        {
            return Math.Max(max_abs(b.Min), max_abs(b.Max));
        }

        static int extreme_world_shift(DMesh3 mesh)
        {
            double maxC = 0;
            foreach (int vid in mesh.VertexIndices())
                maxC = Math.Max(maxC, max_abs(mesh.GetVertex(vid)));
            if (!(maxC > 0) || !MathUtil.IsFinite(maxC))
                return 0;
            int e = pow2_exp(maxC);
            return Math.Abs(e) > 100 ? e : 0;
        }

        static void scale_mesh_inplace(DMesh3 mesh, int exp)
        {
            foreach (int vid in mesh.VertexIndices())
                mesh.SetVertex(vid, scale_vec(mesh.GetVertex(vid), exp));
        }

        static Dictionary<int, int> build_point_ids(DMesh3 mesh, out int contacts)
        {
            var posToId = new Dictionary<Vector3d, int>(mesh.VertexCount);
            var posCounts = new Dictionary<Vector3d, int>(mesh.VertexCount);
            var vidToPid = new Dictionary<int, int>(mesh.VertexCount);
            int nextPid = 0;
            contacts = 0;
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                if (!posToId.TryGetValue(p, out int pid)) {
                    pid = nextPid++;
                    posToId[p] = pid;
                    posCounts[p] = 1;
                } else {
                    if (posCounts[p] == 1) contacts++;
                    posCounts[p]++;
                }
                vidToPid[vid] = pid;
            }
            return vidToPid;
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

        static void append_triangle_preserve_all(DMesh3 dst, Vector3d[] verts, Index3i f)
        {
            if (dst.AppendTriangle(f) >= 0)
                return;
            int a = dst.AppendVertex(verts[f.a]);
            int b = dst.AppendVertex(verts[f.b]);
            int c = dst.AppendVertex(verts[f.c]);
            dst.AppendTriangle(a, b, c);
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

        static void add_boundary_edge(List<(int tri, int otherVert, int oppVert)>[] perVert, int v, int tri, int other, int opp)
        {
            if (perVert[v] == null) perVert[v] = new List<(int, int, int)>(2);
            perVert[v].Add((tri, other, opp));
        }

        static bool edges_are_winding_balanced(List<long> edgeKeys, Dictionary<long, List<HalfEdgeInc>> edgeInc)
        {
            for (int i = 0; i < edgeKeys.Count; ++i) {
                var inc = edgeInc[edgeKeys[i]];
                if (inc.Count == 0) continue;
                int u0 = inc[0].u < inc[0].v ? inc[0].u : inc[0].v;
                int v0 = inc[0].u < inc[0].v ? inc[0].v : inc[0].u;
                int balance = 0;
                for (int j = 0; j < inc.Count; ++j) {
                    if (inc[j].u == u0 && inc[j].v == v0) balance++;
                    else if (inc[j].u == v0 && inc[j].v == u0) balance--;
                }
                if (balance != 0)
                    return false;
            }
            return true;
        }

        static bool any_component_has_no_valid_faces(int nv, List<Index3i> tris, bool[] faceValid)
        {
            var parent = new int[nv];
            for (int i = 0; i < nv; ++i) parent[i] = i;
            for (int t = 0; t < tris.Count; ++t) {
                Index3i f = tris[t];
                dsu_union(parent, f.a, f.b);
                dsu_union(parent, f.b, f.c);
            }
            var compHasAny = new bool[nv];
            var compHasValid = new bool[nv];
            for (int t = 0; t < tris.Count; ++t) {
                int r = dsu_find(parent, tris[t].a);
                compHasAny[r] = true;
                if (faceValid[t]) compHasValid[r] = true;
            }
            for (int i = 0; i < nv; ++i) {
                if (compHasAny[i] && !compHasValid[i])
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Deterministically orients each manifold edge-connected component of valid triangles consistently so that
        /// DoubleSided thickening treats creases identically regardless of per-triangle winding flips or nearby slivers.
        /// </summary>
        static void orient_manifold_components_deterministically(List<Index3i> tris, bool[] faceValid, Vector3d[] faceNormal)
        {
            int nt = tris.Count;
            var edgeInc = new Dictionary<long, List<(int tri, int u, int v)>>(nt * 2);
            for (int t = 0; t < nt; ++t) {
                if (!faceValid[t]) continue;
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
                if (!faceValid[seed] || visited[seed]) continue;
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
                    faceNormal[t] = -faceNormal[t];
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

        static void record_vertex_convex_edge(List<ConvexEdgeInfo>[] perVert, int u, int v, int t0, int t1,
            (Vector3d n, double rhs)? bevelPlane, (Vector3d nA, Vector3d nB)? partnerNormal)
        {
            var info = new ConvexEdgeInfo { triA = t0, triB = t1, bevel = bevelPlane, partnerNormals = partnerNormal };
            if (perVert[u] == null) perVert[u] = new List<ConvexEdgeInfo>(4);
            if (perVert[v] == null) perVert[v] = new List<ConvexEdgeInfo>(4);
            perVert[u].Add(info);
            perVert[v].Add(info);
        }

        static int collect_umbrella_convex_edges(List<ConvexEdgeInfo> edgesAtV, HashSet<int> umbSet,
            out List<(Vector3d n, double rhs)> extraPlanes)
        {
            extraPlanes = null;
            if (edgesAtV == null || edgesAtV.Count == 0)
                return 0;
            int count = 0;
            for (int i = 0; i < edgesAtV.Count; ++i) {
                var e = edgesAtV[i];
                bool inA = umbSet.Contains(e.triA);
                bool inB = umbSet.Contains(e.triB);
                if (!inA && !inB)
                    continue;
                count++;
                if (inA && inB && e.bevel.HasValue) {
                    if (extraPlanes == null) extraPlanes = new List<(Vector3d, double)>(2);
                    extraPlanes.Add(e.bevel.Value);
                } else if (e.partnerNormals.HasValue) {
                    // Cross-shell non-manifold convex sector: include the partner face plane (rhs = 1.0 in d-normalized coords)
                    // so neither shell's single-umbrella corner overshoots the cross-shell wedge.
                    if (extraPlanes == null) extraPlanes = new List<(Vector3d, double)>(2);
                    Vector3d partnerN = inA ? e.partnerNormals.Value.nB : e.partnerNormals.Value.nA;
                    extraPlanes.Add((partnerN, double.NaN)); // NaN sentinel signals unit face plane (rhs = d)
                }
            }
            return count;
        }

        /// <summary>
        /// Sorts an even-valence non-manifold edge cyclically using exact Orient3d radial order, arranges same-ray
        /// coplanar opposite-winding ties to alternate cleanly with their cyclic neighbors (handling both touching
        /// exterior solids and cavity-touching-wall edges), links interior umbrella pairs, and collects &gt; pi sectors.
        /// </summary>
        static void collect_nonmanifold_sectors(Vector3d[] workVerts, int uRef, int vRef, List<HalfEdgeInc> inc,
            Dictionary<long, List<int>> vertTriAdj, List<NonManifoldSector> sectorsOut)
        {
            int fwd = 0, rev = 0;
            for (int i = 0; i < inc.Count; ++i) {
                if (inc[i].u == uRef && inc[i].v == vRef) fwd++;
                else if (inc[i].u == vRef && inc[i].v == uRef) rev++;
            }
            if (fwd != rev || fwd == 0)
                return;

            Vector3d pa = workVerts[uRef], pb = workVerts[vRef];
            Vector3d refOpp = workVerts[inc[0].opp];
            Vector3d refDir = refOpp - pa;
            Vector3d edgeDir = pb - pa;
            double edgeLenSq = edgeDir.LengthSquared;
            if (!(edgeLenSq > 0)) return;
            Vector3d refPerp = refDir - (refDir.Dot(edgeDir) / edgeLenSq) * edgeDir;
            if (!(refPerp.LengthSquared > 0)) return;

            var sorted = new List<HalfEdgeInc>(inc);
            sorted.Sort((a, b) => compare_radial_exact(pa, pb, refOpp, refPerp, uRef, workVerts, a, b));

            // Adjust same-ray coplanar groups (where Orient3d == 0 and same half-plane) so their internal fwd/rev
            // order alternates cleanly with the preceding distinct-ray face (handles both exterior touch & cavity touch).
            int m = sorted.Count;
            var groupStart = new List<int>();
            for (int i = 0; i < m; ++i) {
                if (i == 0 || !same_radial_ray(pa, pb, refOpp, refPerp, workVerts, sorted[i - 1], sorted[i]))
                    groupStart.Add(i);
            }
            if (groupStart.Count > 1) {
                for (int g = 0; g < groupStart.Count; ++g) {
                    int sIdx = groupStart[g];
                    int eIdx = g + 1 < groupStart.Count ? groupStart[g + 1] : m;
                    if (eIdx - sIdx < 2) continue;
                    int prevIdx = (sIdx - 1 + m) % m;
                    bool prevIsFwd = sorted[prevIdx].u == uRef;
                    // We want the first element of this coplanar group to be opposite of prevIsFwd.
                    var fwdList = new List<HalfEdgeInc>();
                    var revList = new List<HalfEdgeInc>();
                    for (int j = sIdx; j < eIdx; ++j) {
                        if (sorted[j].u == uRef) fwdList.Add(sorted[j]);
                        else revList.Add(sorted[j]);
                    }
                    bool wantFwd = !prevIsFwd;
                    int fi = 0, ri = 0;
                    for (int j = sIdx; j < eIdx; ++j) {
                        if ((wantFwd && fi < fwdList.Count) || ri >= revList.Count)
                            sorted[j] = fwdList[fi++];
                        else
                            sorted[j] = revList[ri++];
                        wantFwd = !wantFwd;
                    }
                }
            }

            for (int i = 0; i < m; ++i) {
                var cur = sorted[i];
                var nxt = sorted[(i + 1) % m];
                bool curFwd = cur.u == uRef;
                bool nxtFwd = nxt.u == uRef;
                if (curFwd == nxtFwd)
                    continue;

                int sectorOrient = ExactPredicates.Orient3d(pa, pb, workVerts[cur.opp], workVerts[nxt.opp]);
                bool sectorOverPi = sectorOrient < 0;

                if (!curFwd && nxtFwd) {
                    link_umbrella_tris(vertTriAdj, uRef, cur.tri, nxt.tri);
                    link_umbrella_tris(vertTriAdj, vRef, cur.tri, nxt.tri);
                    if (sectorOverPi) {
                        sectorsOut.Add(new NonManifoldSector {
                            uRef = uRef, vRef = vRef, triA = nxt.tri, triB = cur.tri, oppA = nxt.opp, isExterior = false
                        });
                    }
                } else if (curFwd && !nxtFwd) {
                    if (sectorOverPi) {
                        sectorsOut.Add(new NonManifoldSector {
                            uRef = uRef, vRef = vRef, triA = cur.tri, triB = nxt.tri, oppA = cur.opp, isExterior = true
                        });
                    }
                }
            }
        }

        static bool same_radial_ray(Vector3d pa, Vector3d pb, Vector3d refOpp, Vector3d refPerp,
            Vector3d[] workVerts, HalfEdgeInc a, HalfEdgeInc b)
        {
            Vector3d oa = workVerts[a.opp], ob = workVerts[b.opp];
            return radial_half(pa, pb, refOpp, refPerp, oa) == radial_half(pa, pb, refOpp, refPerp, ob)
                && ExactPredicates.Orient3d(pa, pb, oa, ob) == 0;
        }

        static int compare_radial_exact(Vector3d pa, Vector3d pb, Vector3d refOpp, Vector3d refPerp, int uRef,
            Vector3d[] workVerts, HalfEdgeInc a, HalfEdgeInc b)
        {
            if (a.tri == b.tri) return 0;
            Vector3d oa = workVerts[a.opp], ob = workVerts[b.opp];
            int ha = radial_half(pa, pb, refOpp, refPerp, oa);
            int hb = radial_half(pa, pb, refOpp, refPerp, ob);
            if (ha != hb) return ha.CompareTo(hb);
            int o = ExactPredicates.Orient3d(pa, pb, oa, ob);
            if (o != 0) return o > 0 ? -1 : 1;
            bool aFwd = a.u == uRef;
            bool bFwd = b.u == uRef;
            if (aFwd != bFwd) return aFwd ? -1 : 1;
            return a.tri.CompareTo(b.tri);
        }

        static int radial_half(Vector3d pa, Vector3d pb, Vector3d refOpp, Vector3d refPerp, Vector3d opp)
        {
            int o = ExactPredicates.Orient3d(pa, pb, refOpp, opp);
            if (o > 0) return 0;
            if (o < 0) return 1;
            return (opp - pa).Dot(refPerp) >= 0 ? 0 : 1;
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

        static double anchored_signed_volume(Vector3d[] verts, List<Index3i> tris)
        {
            int nv = verts.Length;
            var parent = new int[nv];
            for (int i = 0; i < nv; ++i) parent[i] = i;
            for (int i = 0; i < tris.Count; ++i) {
                Index3i f = tris[i];
                dsu_union(parent, f.a, f.b);
                dsu_union(parent, f.b, f.c);
            }
            var compAnchor = new int[nv];
            for (int i = 0; i < nv; ++i) compAnchor[i] = -1;
            double sum = 0;
            for (int i = 0; i < tris.Count; ++i) {
                Index3i f = tris[i];
                int root = dsu_find(parent, f.a);
                if (compAnchor[root] < 0) compAnchor[root] = f.a;
                Vector3d o = verts[compAnchor[root]];
                Vector3d a = verts[f.a] - o;
                Vector3d b = verts[f.b] - o;
                Vector3d c = verts[f.c] - o;
                sum += a.Cross(b).Dot(c);
            }
            return sum / 6.0;
        }

        static int dsu_find(int[] parent, int x)
        {
            while (parent[x] != x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        static void dsu_union(int[] parent, int a, int b)
        {
            int ra = dsu_find(parent, a), rb = dsu_find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

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
            dst.AppendTriangle(ia0, ic0, ib0);
            dst.AppendTriangle(ia1, ib1, ic1);
            append_canonical_quad(dst, ia0, ib0, ib1, ia1, idA, idB);
            append_canonical_quad(dst, ib0, ic0, ic1, ib1, idB, idC);
            append_canonical_quad(dst, ic0, ia0, ia1, ic1, idC, idA);
            return true;
        }

        /// <summary>
        /// Emits the closed convex mitered (or bevel-clamped) wedge along a convex dihedral edge (pa -> pb).
        /// Fast path emits an explicit extruded convex prism matching the adjacent face prisms bit-for-bit on their
        /// canonical diagonals; when an asymmetric endpoint face plane would be protruded by the miter (e.g. a hip
        /// roof ridge meeting a shallower hip face), the wedge is clipped cleanly against the violating endpoint plane(s).
        /// </summary>
        static bool emit_edge_wedge(DMesh3 dst, Vector3d pa, Vector3d pb, int idA, int idB, Vector3d pc0,
            Vector3d n1, Vector3d n2, double d, double miterLimit, bool isNegSide, int refTri,
            List<List<int>>[] vertUmbrellas, Dictionary<long, int> vertTriToUmb, Vector3d[] faceNormals,
            out (Vector3d n, double rhs)? bevelPlane)
        {
            bevelPlane = null;

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
                // Exact or near-180-degree razor fold (n1 ~ -n2): emit orthogonal bevel cap at MiterLimit * d.
                Vector3d edgeDir = pb - pa;
                double edgeLen = edgeDir.Length;
                if (!(edgeLen > 0))
                    return false;
                Vector3d outDir = n1.Cross(edgeDir / edgeLen);
                double oLen = outDir.Length;
                if (!(oLen > 1e-12))
                    return false;
                Vector3d u = outDir / oLen;
                if (u.Dot(pc0 - pa) > 0)
                    u = -u;
                Vector3d b1 = d * (n1 + miterLimit * u);
                Vector3d b2 = d * (n2 + miterLimit * u);
                bevelPlane = (u, miterLimit * d);
                profile.Add(b1);
                profile.Add(b2);
            }

            profile.Add(d * n2);

            // Verify consistent strictly non-zero orientation across every fan triangle of the cross-section.
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

            // Check whether any convex capping face plane (distinct from n1, n2, with n . (other - vPos) < 0)
            // in idA's or idB's umbrella is violated by the unclipped wedge miter.
            double sideSign = isNegSide ? -1.0 : +1.0;
            List<(Vector3d n, double rhs)> clipPlanes = null;
            collect_violated_endpoint_planes(idA, refTri, pa, pb, profile, n1, n2, d, sideSign, vertUmbrellas, vertTriToUmb, faceNormals, ref clipPlanes);
            collect_violated_endpoint_planes(idB, refTri, pb, pa, profile, n1, n2, d, sideSign, vertUmbrellas, vertTriToUmb, faceNormals, ref clipPlanes);

            if (clipPlanes != null) {
                // Asymmetric convex corner (e.g. hip roof ridge): clip wedge polyhedron directly in work coordinates
                // so all unclipped vertices stay bit-for-bit identical to the adjacent face prisms and canonical diagonals match.
                var poly = ConvexPolyhedron.CreateExtrudedPrism(pa, pb, profile, idA, idB, isNegSide);
                double scaleRef = Math.Max(d, Math.Max(max_abs(pa), max_abs(pb)) + miterLimit * d);
                double clipEps = Math.Max(1e-12 * d, 1e-13 * scaleRef);
                for (int i = 0; i < clipPlanes.Count; ++i) {
                    if (!poly.Clip(clipPlanes[i].n, clipPlanes[i].rhs, clipEps))
                        return false;
                }
                return poly.EmitScaled(dst, Vector3d.Zero, 1.0, minVol6: 1e-12 * d * d * d);
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

            // Side quads with exact canonical diagonals matching both +n and -n face prisms
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

        static void collect_violated_endpoint_planes(int vId, int refTri, Vector3d vPos, Vector3d otherPos, List<Vector3d> profile,
            Vector3d n1, Vector3d n2, double d, double sideSign, List<List<int>>[] vertUmbrellas,
            Dictionary<long, int> vertTriToUmb, Vector3d[] faceNormals, ref List<(Vector3d n, double rhs)> clipPlanes)
        {
            if (vertUmbrellas[vId] == null || !vertTriToUmb.TryGetValue(vt_key(vId, refTri), out int ui))
                return;
            Vector3d edgeVec = otherPos - vPos;
            double edgeLen = edgeVec.Length;
            if (!(edgeLen > 0))
                return;
            var umb = vertUmbrellas[vId][ui];
            // Trigger clipping only for convex endpoint capping planes (n . (otherPos - vPos) < 0) that the unclipped
            // miter genuinely protrudes past (> 0.1% of d). Never clip against concave walls rising along the edge.
            double tol = d * (1.0 + 1e-3);
            double capTol = -1e-6 * edgeLen;
            for (int i = 0; i < umb.Count; ++i) {
                Vector3d n = sideSign * faceNormals[umb[i]];
                if (n.Dot(n1) > CoplanarDotThreshold || n.Dot(n2) > CoplanarDotThreshold)
                    continue;
                if (n.Dot(edgeVec) >= capTol)
                    continue;
                bool violated = false;
                for (int p = 1; p < profile.Count; ++p) {
                    if (n.Dot(profile[p]) > tol) {
                        violated = true;
                        break;
                    }
                }
                if (violated) {
                    if (clipPlanes == null) clipPlanes = new List<(Vector3d, double)>(2);
                    double rhs = n.Dot(vPos) + d;
                    bool dup = false;
                    for (int c = 0; c < clipPlanes.Count; ++c) {
                        if (clipPlanes[c].n.Dot(n) > CoplanarDotThreshold && Math.Abs(clipPlanes[c].rhs - rhs) <= 1e-9 * d) {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup) clipPlanes.Add((n, rhs));
                }
            }
        }

        /// <summary>
        /// Emits the closed convex mitered corner polyhedron for a vertex umbrella via exact indexed half-space
        /// clipping in normalized local offset coordinates (delta / d). Handles arbitrary vertex valences, sharp
        /// bevel-clamped acute apices, asymmetric convex corners (hip roofs), gently curved surfaces, and open rims.
        /// </summary>
        static bool emit_vertex_corner_clipped(DMesh3 dst, int v, List<int> umbrella, HashSet<int> umbSet,
            Vector3d[] workVerts, List<Index3i> tris, Vector3d[] faceNormals, double side,
            List<(Vector3d n, double rhs)> extraPlanes, List<(int tri, int otherVert, int oppVert)> bndAtV,
            double d, double miterLimit)
        {
            // 1. Collect deduplicated face normals and extra bevel/partner planes for this umbrella.
            var normals = new List<Vector3d>(umbrella.Count);
            var planes = new List<(Vector3d n, double rhs)>(umbrella.Count * 2 + 4);
            Vector3d vPos = workVerts[v];
            Vector3d normalSum = Vector3d.Zero;

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
                    normalSum += n;
                }
            }
            if (normals.Count < 3)
                return false;

            // Collect incident edge directions first so they can also supply a strict supporting axis for acute corners.
            var rawEdgePlanes = new List<(Vector3d dir, double rhs, double maxLenNorm)>(umbrella.Count + 2);
            for (int i = 0; i < umbrella.Count; ++i) {
                Index3i f = tris[umbrella[i]];
                add_unique_edge_plane(rawEdgePlanes, vPos, f.a == v ? workVerts[f.b] : workVerts[f.a], d, 0.05);
                add_unique_edge_plane(rawEdgePlanes, vPos, f.c == v ? workVerts[f.b] : workVerts[f.c], d, 0.05);
            }

            // Find a strict supporting axis nAvg of the normal cone (nAvg . n_i > 1e-3 for all i):
            //   1) Try normalized sum of face normals (works for balanced/curved corners).
            //   2) Fallback to normalized negative sum of incident edge directions (works for acute asymmetric base corners).
            //   3) Fallback to normalized 3-face intersection direction of the first 3 linearly independent normals.
            bool hasBackStop = try_strict_cone_axis(normals, normalSum, out Vector3d nAvg);
            if (!hasBackStop) {
                Vector3d negEdgeSum = Vector3d.Zero;
                for (int i = 0; i < rawEdgePlanes.Count; ++i) negEdgeSum -= rawEdgePlanes[i].dir;
                hasBackStop = try_strict_cone_axis(normals, negEdgeSum, out nAvg);
            }
            if (!hasBackStop) {
                for (int i = 0; i < normals.Count && !hasBackStop; ++i) {
                    for (int j = i + 1; j < normals.Count && !hasBackStop; ++j) {
                        Vector3d c = normals[i].Cross(normals[j]);
                        if (c.LengthSquared < 1e-12) continue;
                        for (int l = j + 1; l < normals.Count; ++l) {
                            double det = c.Dot(normals[l]);
                            if (Math.Abs(det) < 1e-8) continue;
                            Vector3d x = (normals[j].Cross(normals[l]) + normals[l].Cross(normals[i]) + c) / det;
                            if (try_strict_cone_axis(normals, x, out nAvg)) {
                                hasBackStop = true;
                                break;
                            }
                        }
                    }
                }
            }

            var edgePlanes = rawEdgePlanes;
            if (!hasBackStop) {
                for (int i = 0; i < edgePlanes.Count; ++i)
                    edgePlanes[i] = (edgePlanes[i].dir, 0.0, edgePlanes[i].maxLenNorm);
            }

            if (extraPlanes != null) {
                for (int i = 0; i < extraPlanes.Count; ++i) {
                    double normRhs = double.IsNaN(extraPlanes[i].rhs) ? 1.0 : extraPlanes[i].rhs / d;
                    add_unique_plane_norm(planes, extraPlanes[i].n, normRhs);
                }
            }

            // Collect outward boundary-rim clipping planes for open-sheet umbrellas so curved rims stay flush.
            List<Vector3d> rimPlanes = null;
            if (bndAtV != null) {
                for (int i = 0; i < bndAtV.Count; ++i) {
                    var be = bndAtV[i];
                    if (!umbSet.Contains(be.tri)) continue;
                    Vector3d edgeDir = workVerts[be.otherVert] - vPos;
                    Vector3d nFace = side * faceNormals[be.tri];
                    Vector3d rimOut = nFace.Cross(edgeDir);
                    double rLen = rimOut.Length;
                    if (!(rLen > 0)) continue;
                    rimOut /= rLen;
                    if (rimOut.Dot(workVerts[be.oppVert] - vPos) > 0)
                        rimOut = -rimOut;
                    if (rimPlanes == null) rimPlanes = new List<Vector3d>(2);
                    bool dup = false;
                    for (int j = 0; j < rimPlanes.Count; ++j) {
                        if (rimPlanes[j].Dot(rimOut) > CoplanarDotThreshold) { dup = true; break; }
                    }
                    if (!dup) rimPlanes.Add(rimOut);
                }
            }

            // 2. Build initial box [-B, +B]^3 and clip by face planes, bevel/partner planes, back-stop, and rim planes.
            double B = 16.0;
            while (B < 4.0 * miterLimit) B *= 2.0;
            double clipEps = Math.Max(1e-11, 1e-13 * B);
            double boundLimit = 0.5 * B;

            var poly = ConvexPolyhedron.CreateBox(B);
            for (int i = 0; i < planes.Count; ++i) {
                if (!poly.Clip(planes[i].n, planes[i].rhs, clipEps))
                    return false;
            }
            if (hasBackStop) {
                if (!poly.Clip(-nAvg, 0.0, clipEps))
                    return false;
            }
            if (rimPlanes != null) {
                for (int i = 0; i < rimPlanes.Count; ++i) {
                    if (!poly.Clip(rimPlanes[i], 0.0, clipEps))
                        return false;
                }
            }

            // 3. On strictly pointed cones (hasBackStop):
            //    (a) Pre-clamp along +nAvg at MiterLimit BEFORE edgePlanes/boundLimit rejection so arbitrarily acute
            //        cones/pyramids bevel cleanly instead of hitting the box boundary and being dropped.
            //    (b) Relax edgePlanes ONLY for true outer shifted-face apices (strictly in the positive dual cone
            //        n_i . p > 0 for all face normals, with >= 3 active planes), never for sideways pancake vertices.
            if (hasBackStop) {
                var preUsed = poly.CollectUsedVertexIndices();
                bool exceedsAlongAxis = false;
                for (int i = 0; i < preUsed.Count; ++i) {
                    Vector3d p = poly.Verts[preUsed[i]];
                    if (nAvg.Dot(p) > miterLimit * (1.0 + 1e-5)) {
                        exceedsAlongAxis = true;
                        break;
                    }
                }
                if (exceedsAlongAxis) {
                    if (!poly.Clip(nAvg, miterLimit, clipEps))
                        return false;
                    preUsed = poly.CollectUsedVertexIndices();
                }

                for (int i = 0; i < preUsed.Count; ++i) {
                    Vector3d p = poly.Verts[preUsed[i]];
                    if (Math.Abs(p.x) >= boundLimit || Math.Abs(p.y) >= boundLimit || Math.Abs(p.z) >= boundLimit)
                        continue;
                    if (!is_true_outer_apex(normals, planes, p, miterLimit))
                        continue;
                    for (int e = 0; e < edgePlanes.Count; ++e) {
                        double proj = edgePlanes[e].dir.Dot(p);
                        if (proj > edgePlanes[e].rhs) {
                            double relaxed = Math.Min(edgePlanes[e].maxLenNorm, proj + 0.05);
                            if (relaxed > edgePlanes[e].rhs)
                                edgePlanes[e] = (edgePlanes[e].dir, relaxed, edgePlanes[e].maxLenNorm);
                        }
                    }
                }
            }

            // 4. Clip by incident edge half-spaces.
            for (int i = 0; i < edgePlanes.Count; ++i) {
                if (!poly.Clip(edgePlanes[i].dir, edgePlanes[i].rhs, clipEps))
                    return false;
            }

            // 5. Reject unbounded saddle corners (any surviving vertex reaching the artificial box boundary).
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

            // 6. Final corner bevel clamp if the bounded corner peak still exceeds MiterLimit.
            double maxLen = Math.Sqrt(maxLenSq);
            if (maxLen > miterLimit * (1.0 + 1e-5)) {
                if (!poly.Clip(farthest / maxLen, miterLimit, clipEps))
                    return false;
            }

            return poly.EmitScaled(dst, vPos, d);
        }

        static bool is_true_outer_apex(List<Vector3d> normals, List<(Vector3d n, double rhs)> planes, Vector3d p, double miterLimit)
        {
            if (p.LengthSquared > (miterLimit * miterLimit) * 1.001)
                return false;
            for (int i = 0; i < normals.Count; ++i) {
                if (normals[i].Dot(p) <= 1e-4)
                    return false;
            }
            int active = 0;
            for (int i = 0; i < planes.Count; ++i) {
                if (Math.Abs(planes[i].n.Dot(p) - planes[i].rhs) <= 1e-4) {
                    if (++active >= 3)
                        return true;
                }
            }
            return false;
        }

        static bool try_strict_cone_axis(List<Vector3d> normals, Vector3d candidate, out Vector3d axis)
        {
            axis = Vector3d.Zero;
            double len = candidate.Length;
            if (!(len > 1e-9) || !MathUtil.IsFinite(len))
                return false;
            Vector3d u = candidate / len;
            for (int i = 0; i < normals.Count; ++i) {
                if (u.Dot(normals[i]) <= 1e-3)
                    return false;
            }
            axis = u;
            return true;
        }

        static void add_unique_edge_plane(List<(Vector3d dir, double rhs, double maxLenNorm)> edgePlanes,
            Vector3d vPos, Vector3d otherPos, double d, double maxOverlap)
        {
            Vector3d e = otherPos - vPos;
            double len = e.Length;
            if (!(len > 0)) return;
            Vector3d dir = e / len;
            double lenNorm = len / d;
            double overlap = maxOverlap > 0 ? Math.Min(maxOverlap, 0.25 * lenNorm) : 0.0;
            for (int i = 0; i < edgePlanes.Count; ++i) {
                if (edgePlanes[i].dir.Dot(dir) > CoplanarDotThreshold) {
                    double rhs = Math.Min(edgePlanes[i].rhs, overlap);
                    double ml = Math.Min(edgePlanes[i].maxLenNorm, lenNorm);
                    edgePlanes[i] = (edgePlanes[i].dir, rhs, ml);
                    return;
                }
            }
            edgePlanes.Add((dir, overlap, lenNorm));
        }

        static void add_unique_plane_norm(List<(Vector3d n, double rhs)> planes, Vector3d n, double rhs)
        {
            for (int i = 0; i < planes.Count; ++i) {
                if (planes[i].n.Dot(n) > CoplanarDotThreshold && Math.Abs(planes[i].rhs - rhs) <= 1e-9)
                    return;
            }
            planes.Add((n, rhs));
        }

        /// <summary>
        /// Indexed convex polyhedron with watertight Sutherland-Hodgman half-space clipping.
        /// Every edge cut is cached by canonical undirected vertex-index pair (min(u,v), max(u,v)), the cap
        /// face chains exact unmatched reversed half-edges, and EmitScaled verifies exact closed edge balance
        /// before appending to dst.
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

            /// <summary>
            /// Builds an indexed closed convex prism directly in work coordinates along (pa -> pb), orienting
            /// side-quad starting vertices so fan triangulation from face[0] matches append_canonical_quad.
            /// </summary>
            public static ConvexPolyhedron CreateExtrudedPrism(Vector3d pa, Vector3d pb, List<Vector3d> profile,
                int idA, int idB, bool isNegSide)
            {
                int k = profile.Count;
                var v = new List<Vector3d>(2 * k + 8);
                for (int i = 0; i < k; ++i) v.Add(pa + profile[i]);
                for (int i = 0; i < k; ++i) v.Add(pb + profile[i]);

                var f = new List<List<int>>(k + 2);
                // Start cap at pa (clockwise around +edgeDir => outward -edgeDir)
                var capA = new List<int>(k);
                for (int i = k - 1; i >= 0; --i) capA.Add(i);
                f.Add(capA);
                // End cap at pb (CCW around +edgeDir => outward +edgeDir)
                var capB = new List<int>(k);
                for (int i = 0; i < k; ++i) capB.Add(k + i);
                f.Add(capB);
                // Side quads (outward), rotated so fan from face[0] uses the exact canonical diagonal of append_canonical_quad:
                //   append_canonical_quad(a0, b0, b1, a1, aId, bId) uses diagonal (a0, b1) if aId < bId else (b0, a1).
                for (int i = 0; i < k; ++i) {
                    int j = (i + 1) % k;
                    if (i == 0 && isNegSide) {
                        // Matches append_canonical_quad(va[1], vb[1], vb[0], va[0], idA, idB)
                        f.Add(idA < idB ? new List<int> { 1, k + 1, k, 0 } : new List<int> { 0, 1, k + 1, k });
                    } else if (i == k - 1) {
                        if (isNegSide) {
                            // Matches append_canonical_quad(vb[k-1], va[k-1], va[0], vb[0], idB, idA)
                            f.Add(idB < idA ? new List<int> { 0, k, 2 * k - 1, k - 1 } : new List<int> { k - 1, 0, k, 2 * k - 1 });
                        } else {
                            // Matches append_canonical_quad(va[0], vb[0], vb[k-1], va[k-1], idA, idB)
                            f.Add(idA < idB ? new List<int> { 0, k, 2 * k - 1, k - 1 } : new List<int> { k, 2 * k - 1, k - 1, 0 });
                        }
                    } else {
                        // Matches append_canonical_quad(vb[i], va[i], va[j], vb[j], idB, idA)
                        f.Add(idB < idA ? new List<int> { k + i, i, j, k + j } : new List<int> { i, j, k + j, k + i });
                    }
                }
                return new ConvexPolyhedron(v, f);
            }

            public bool Clip(Vector3d n, double rhs, double eps)
            {
                int nv = Verts.Count;
                var dist = new double[nv];
                var side = new sbyte[nv]; // -1 inside, 0 on plane, +1 outside
                bool anyOutside = false, anyInside = false;

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
                    if (d > eps) { side[i] = 1; anyOutside = true; }
                    else if (d < -eps) { side[i] = -1; anyInside = true; }
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

                var capNext = new Dictionary<int, int>();
                var capStarts = new List<int>();
                for (int i = 0; i < planeEdgeList.Count; ++i) {
                    int a = planeEdgeList[i].a;
                    int b = planeEdgeList[i].b;
                    if (!planeEdges.Contains(dir_edge_key(b, a))) {
                        if (capNext.ContainsKey(b)) {
                            Faces.Clear();
                            return false;
                        }
                        capNext[b] = a;
                        capStarts.Add(b);
                    }
                }

                if (capStarts.Count < 3) {
                    Faces.Clear();
                    return false;
                }

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
                    if (cur != start || loop.Count < 3) {
                        Faces.Clear();
                        return false;
                    }
                    nextFaces.Add(loop);
                }
                if (usedCap.Count != capStarts.Count) {
                    Faces.Clear();
                    return false;
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

            public bool EmitScaled(DMesh3 dst, Vector3d origin, double scale, double minVol6 = 1e-12)
            {
                if (Faces.Count < 4)
                    return false;

                var edgeBalance = new Dictionary<long, int>(Faces.Count * 4);
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    for (int i = 0; i < face.Count; ++i) {
                        int a = face[i], b = face[(i + 1) % face.Count];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        edgeBalance.TryGetValue(key, out int bal);
                        edgeBalance[key] = bal + (a < b ? 1 : -1);
                    }
                }
                foreach (var kv in edgeBalance) {
                    if (kv.Value != 0)
                        return false;
                }

                double vol6 = 0;
                Vector3d anchor = Verts[Faces[0][0]];
                for (int fi = 0; fi < Faces.Count; ++fi) {
                    var face = Faces[fi];
                    Vector3d a = Verts[face[0]] - anchor;
                    for (int i = 1; i < face.Count - 1; ++i)
                        vol6 += a.Cross(Verts[face[i]] - anchor).Dot(Verts[face[i + 1]] - anchor);
                }
                if (!(vol6 > minVol6))
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
                int vid = dst.AppendVertex(scale == 1.0 && origin == Vector3d.Zero ? Verts[idx] : origin + scale * Verts[idx]);
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
