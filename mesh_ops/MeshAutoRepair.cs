// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;

namespace gs
{
    /// <summary>
    /// Mesh Auto Repair top-level driver: tries to turn a broken triangle mesh (cracks, holes, inconsistent
    /// orientation, bowties, tiny edges) into a closed, consistently and outward oriented mesh while keeping
    /// the input triangles where possible. Steps: optional removal of interior/occluded triangles (RemoveMode),
    /// component orientation (MeshRepairOrientation), crack closing (MergeCoincidentEdges), collapse of tiny
    /// boundary edges, hole filling (SimpleHoleFiller / MinimalHoleFill / SmoothedHoleFill), bowtie
    /// disconnection, and as a last resort repeated erosion of boundary triangles (ErosionIterations) before
    /// filling again. Finally tiny edges are collapsed (MinEdgeLengthTol), the global orientation is solved and
    /// optionally MeshPostRepairCleanup runs.
    ///
    /// Behaviour: the input mesh is modified in place, and at the end <see cref="Mesh"/> is replaced by a new,
    /// compacted copy with vertex normals, so always read the result from <see cref="Mesh"/>.
    /// Apply() returns false only if cancelled (see Progress); it returns true even if the mesh could not be
    /// closed, so check eg Mesh.IsClosed(). Self-intersections are not removed, and erosion can delete parts
    /// of the input. Tolerances are absolute (model units).
    ///
    /// Use MeshRobustRepair to get a clean solid in all cases (it runs this class first and falls back to
    /// MeshWindingNumberSolidify, which remeshes); use MeshWindingNumberSolidify directly to rebuild any input
    /// as a closed surface at a grid resolution; use ExactMeshBoolean.Resolve to resolve self-intersections and
    /// nested shells of an already closed mesh exactly.
    ///
    /// TODO:
    ///   - remove degenerate *faces*  (which may still have all edges > length)
    ///       - this is tricky, in many CAD meshes these faces can't just be collapsed. But can often remove via flipping...?
    /// 
    /// </summary>
    public class MeshAutoRepair
    {
        /// <summary>
        /// Distance (absolute) within which boundary edges are merged to close cracks (up to 2x this in the
        /// second round); boundary edges shorter than half of it are collapsed before that.
        /// </summary>
		public double RepairTolerance = MathUtil.ZeroTolerancef;

        /// <summary>
        /// At the end, edges shorter than this (absolute) are assumed degenerate and collapsed. 0 disables.
        /// </summary>
        public double MinEdgeLengthTol = 0.0001;

        /// <summary>
        /// If the mesh is still open after the first two rounds, up to this many times all triangles touching
        /// the boundary are deleted and the repair is repeated. This removes input geometry.
        /// </summary>
        public int ErosionIterations = 5;


        // [TODO] interior components?
        /// <summary>
        /// Which triangles to remove before the repair (see RemoveOccludedTriangles, per-vertex test: a
        /// triangle is removed if any of its vertices is classified as inside).
        /// None: keep all. Interior: fast winding number &gt; 0.5. Occluded: rays along all six axis
        /// directions hit the mesh.
        /// </summary>
        public enum RemoveModes {
			None = 0, Interior = 1, Occluded = 2
		};
        /// <summary> see RemoveModes; default None </summary>
		public RemoveModes RemoveMode = MeshAutoRepair.RemoveModes.None;

        /// <summary>
        /// If true, run MeshPostRepairCleanup at the end of Apply(). Removes duplicate triangles and
        /// bowties that the pipeline leaves behind, and fixes many of the degenerate triangles, folds
        /// and inside-out components it introduces (mostly during hole filling).
        /// </summary>
        public bool PostRepairCleanup = false;

        /// <summary>
        /// Minimal: MinimalHoleFill (falls back to SimpleHoleFiller).
        /// Smoothed: SmoothedHoleFill (fan fill, remesh to the average border edge length, laplacian
        /// smooth, boundary kept fixed) for holes with more than 4 vertices, falls back to Minimal.
        /// </summary>
        /// <summary>
        /// Non-orientable regions: if true (default), the orientation keeps a short seam
        /// (MeshRepairOrientation.KeepShortSeams), and the mesh is cut open along it before the hole filling, so that
        /// holes at the seam are filled instead of eroded, and once more at the end for closed non-orientable meshes
        /// (kept only if the mesh is closed again afterwards): the result is closed in the winding number sense
        /// (ExactMeshBoolean.IsWindingClosed) where possible. false: as geometry3Sharp 1.0.324 (depth-first
        /// orientation, holes at a seam are left to the erosion and the last-resort fill).
        /// </summary>
        public bool CutOrientationSeams = true;

        public enum HoleFillModes { Minimal = 0, Smoothed = 1 }
        /// <summary> hole filling strategy for non-trivial holes (holes with 3 or 4 vertices always use the minimal fill) </summary>
        public HoleFillModes HoleFillMode = HoleFillModes.Minimal;

        bool fill_inconsistent_loops = false;





        /// <summary>
        /// Set this to be able to cancel the running repair (Apply() then returns false and the mesh is left
        /// partially repaired)
        /// </summary>
        public ProgressCancel Progress = null;

        /// <summary>
        /// if this returns true, abort computation. 
        /// </summary>
        protected virtual bool Cancelled()
        {
            return (Progress == null) ? false : Progress.Cancelled();
        }





        /// <summary>
        /// The mesh to repair. Modified in place during Apply(), and replaced by a compacted copy (with vertex
        /// normals) at the end of a successful Apply(): read the result from here.
        /// </summary>
        public DMesh3 Mesh;

        /// <summary> called after every pipeline step with its name and the current mesh (diagnostics) </summary>
        public Action<string, DMesh3> OnStage = null;
        void stage(string name) { OnStage?.Invoke(name, Mesh); }

        /// <summary> the mesh is repaired in place (see Mesh) </summary>
		public MeshAutoRepair(DMesh3 mesh3)
		{
			Mesh = mesh3;
		}


        /// <summary>
        /// Run the repair pipeline (see class comment).
        /// </summary>
        /// <returns>false if cancelled, otherwise true, also if the mesh could not be closed (check Mesh.IsClosed())</returns>
		public bool Apply()
		{
			bool do_checks = false;

			if ( do_checks ) Mesh.CheckValidity();


            /*
             * Remove parts of the mesh we don't want before we bother with anything else
             * TODO: maybe we need to repair orientation first? if we want to use MWN...
             */
			do_remove_inside();
                if (Cancelled()) return false;

            int repeat_count = 0;
            fill_inconsistent_loops = false;
            repeat_all:

            /*
             * make sure orientation of connected components is consistent
             * TODO: what about mobius strip problems?
             */
            repair_orientation(false); stage("orient_1");
                if (Cancelled()) return false;

            /*
             *  Do safe close-cracks to handle easy cases
             */

            repair_cracks(true, RepairTolerance); stage("cracks_unique");
                if (Mesh.IsClosed()) goto all_done;
                if (Cancelled()) return false;

            /*
             * Collapse tiny edges and then try easy cases again, and
             * then allow for handling of ambiguous cases
             */

            collapse_all_degenerate_edges(RepairTolerance*0.5, true); stage("collapse_boundary_edges");
                if (Cancelled()) return false;
            repair_cracks(true, 2*RepairTolerance); stage("cracks_unique_2x");
                if (Cancelled()) return false;
            repair_cracks(false, 2*RepairTolerance); stage("cracks_any_2x");
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * Possibly we have joined regions with different orientation (is it?), fix that
             * TODO: mobius strips again
             */
            repair_orientation(false); stage("orient_2");
                if (Cancelled()) return false;

            /*
             * Where a component is not orientable, a seam of inconsistently oriented edges is left. A hole
             * it ends at runs against the boundary orientation and is not filled (see fill_any_holes),
             * so cut the mesh open along the seam: then it is orientable and its holes can be filled.
             */
            if (CutOrientationSeams && cut_orientation_seams() > 0) {
                repair_orientation(false);
                // coincident boundary edges that now run in opposite directions (eg the two sides of a slit
                // left by the cut) are joined again; MergeEdges never joins edges that run the same way
                repair_cracks(true, 2 * RepairTolerance);
                repair_cracks(false, 2 * RepairTolerance);
                stage("cut_seams");
            }

            if (do_checks) Mesh.CheckValidity();

            // get rid of any remaining single-triangles before we start filling holes
            remove_loners(); stage("remove_loners");

            /*
             * Ok, fill simple holes. 
             */
            int nRemainingBowties = 0;
			int nHoles; bool bSawSpans;
			fill_trivial_holes(out nHoles, out bSawSpans); stage("fill_trivial_holes");
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * Now fill harder holes. If we saw spans, that means boundary loops could
             * not be resolved in some cases, do we disconnect bowties and try again.
             */
            fill_any_holes(out nHoles, out bSawSpans); stage("fill_any_holes");
                if (Cancelled()) return false;
            if (bSawSpans) {
				disconnect_bowties(out nRemainingBowties);
				fill_any_holes(out nHoles, out bSawSpans); stage("bowties+fill_any_holes");
			}
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * We may have a closed mesh now but it might still have bowties (eg
             * tetrahedra sharing vtx case). So disconnect those.
             */
            disconnect_bowties(out nRemainingBowties); stage("disconnect_bowties");
                if (Cancelled()) return false;

            /*
             * If the mesh is not closed, we will do one more round to try again.
             */
            if (repeat_count == 0 && Mesh.IsClosed() == false) {
				repeat_count++;
				goto repeat_all;
			}

            /*
             * Ok, we didn't get anywhere on our first repeat. If we are still not
             * closed, we will try deleting boundary triangles and repeating.
             * Repeat this N times.
             */
            if ( repeat_count <= ErosionIterations && Mesh.IsClosed() == false) {
                repeat_count++;
                MeshFaceSelection bdry_faces = new MeshFaceSelection(Mesh);
                foreach (int eid in MeshIterators.BoundaryEdges(Mesh))
                    bdry_faces.SelectEdgeTris(eid);
                MeshEditor.RemoveTriangles(Mesh, bdry_faces, true); stage("erosion");
                goto repeat_all;
            }

            /*
             * Last resort: if the mesh is still open, the remaining holes are bounded by loops that
             * run against the boundary orientation (non-orientable regions). Fill them anyway, an
             * orientation seam is better than a hole.
             */
            if (Mesh.IsClosed() == false) {
                fill_inconsistent_loops = true;
                fill_any_holes(out nHoles, out bSawSpans); stage("fill_inconsistent_loops");
            }

            all_done:

            /*
             * Remove tiny edges
             */
            if (MinEdgeLengthTol > 0) {
                collapse_all_degenerate_edges(MinEdgeLengthTol, false); stage("collapse_short_edges");
            }
                if (Cancelled()) return false;

            /*
             * The mesh may have been closed early (goto all_done above), which skips disconnect_bowties,
             * and collapsing tiny edges can create new bowties. Disconnect them before orienting.
             */
            disconnect_bowties(out nRemainingBowties); stage("disconnect_bowties_final");
                if (Cancelled()) return false;

            /*
             * A closed mesh can still be non-orientable (eg two sheets welded with opposite orientations):
             * then its seam is a cycle of inconsistent edges and the mesh does not bound a solid. Cut it
             * open there and fill the holes: two loops at the same position if the seam is two-sided, one loop
             * running twice over it if it is one-sided (eg a projective plane). The fills can coincide or
             * overlap, which the exact resolve of MeshRobustRepair handles; if the mesh is not closed afterwards,
             * the uncut mesh is kept.
             */
            if (CutOrientationSeams && Mesh.IsClosed() && has_orientation_seams()) {
                DMesh3 uncut = new DMesh3(Mesh);
                cut_orientation_seams();
                repair_orientation(false);
                fill_trivial_holes(out nHoles, out bSawSpans);
                fill_any_holes(out nHoles, out bSawSpans);
                // a cut side that could not be filled: keep the closed, non-orientable mesh
                if (Mesh.IsClosed() == false)
                    Mesh = uncut;
                stage("cut_seams_final");
            }
                if (Cancelled()) return false;

            /*
             * finally do global orientation
             */
            repair_orientation(true); stage("orient_global");
                if (Cancelled()) return false;

            if (PostRepairCleanup) {
                MeshPostRepairCleanup cleanup = new MeshPostRepairCleanup(Mesh);
                cleanup.Apply(); stage("cleanup");
            }
                if (Cancelled()) return false;

            if (do_checks) Mesh.CheckValidity();

            /*
             * Might as well compact output mesh...
             */
			Mesh = new DMesh3(Mesh, true);
            MeshNormals.QuickCompute(Mesh);

			return true;
		}




		void fill_trivial_holes(out int nRemaining, out bool saw_spans)
		{
			MeshBoundaryLoops loops = new MeshBoundaryLoops(Mesh);
			nRemaining = 0;
			saw_spans = loops.SawOpenSpans;

			foreach (var loop in loops) {
                if (Cancelled()) break;
                // see fill_any_holes
                if (fill_inconsistent_loops == false && loops.IsConsistentlyOriented(loop) == false) {
                    saw_spans = true;
                    ++nRemaining;
                    continue;
                }
                bool filled = false;
				if (loop.VertexCount == 3) {
					SimpleHoleFiller filler = new SimpleHoleFiller(Mesh, loop);
					filled = filler.Fill();
				} else if ( loop.VertexCount == 4 ) {
					MinimalHoleFill filler = new MinimalHoleFill(Mesh, loop);
					filled = filler.Apply();
					if (filled == false) {
						SimpleHoleFiller fallback = new SimpleHoleFiller(Mesh, loop);
						filled = fallback.Fill();
					}
				}

				if (filled == false)
					++nRemaining;
			}
		}



		void fill_any_holes(out int nRemaining, out bool saw_spans)
		{
			MeshBoundaryLoops loops = new MeshBoundaryLoops(Mesh);
			nRemaining = 0;
			saw_spans = loops.SawOpenSpans;

			foreach (var loop in loops) {
                if (Cancelled()) break;
                // A loop that runs against the boundary orientation somewhere (inconsistently oriented
                // triangles, usually around a bowtie) is not filled: that can create a non-orientable
                // surface. Report it like an unresolved span, so bowties get disconnected and the next
                // round can fill it after the orientation repair (or erosion removes the region).
                // Only the last-resort fill at the end of Apply() fills such loops.
                if (fill_inconsistent_loops == false && loops.IsConsistentlyOriented(loop) == false) {
                    saw_spans = true;
                    ++nRemaining;
                    continue;
                }
                bool filled = false;
                if (HoleFillMode == HoleFillModes.Smoothed && loop.VertexCount > 4)
                    filled = smoothed_fill(loop);
                if (filled == false) {
                    MinimalHoleFill filler = new MinimalHoleFill(Mesh, loop);
                    filled = filler.Apply();
                }
				if (filled == false) {
                    if (Cancelled()) break;
                    SimpleHoleFiller fallback = new SimpleHoleFiller(Mesh, loop);
					filled = fallback.Fill();
				}
			}
		}


        bool smoothed_fill(EdgeLoop loop)
        {
            double len = 0;
            int NV = loop.VertexCount;
            for (int i = 0; i < NV; ++i)
                len += Mesh.GetVertex(loop.Vertices[i]).Distance(Mesh.GetVertex(loop.Vertices[(i + 1) % NV]));
            if (len < MathUtil.ZeroTolerancef)
                return false;
            try {
                SmoothedHoleFill fill = new SmoothedHoleFill(Mesh, loop);
                fill.TargetEdgeLength = len / NV;
                fill.ConstrainToHoleInterior = true;
                return fill.Apply();
            } catch (Exception) {
                return false;
            }
        }




		bool repair_cracks(bool bUniqueOnly, double mergeDist)
		{
			try {
				MergeCoincidentEdges merge = new MergeCoincidentEdges(Mesh);
				merge.OnlyUniquePairs = bUniqueOnly;
				merge.MergeDistance = mergeDist;
				return merge.Apply();
			} catch (Exception /*e*/) {
				// ??
				return false;
			}
		}



		bool remove_duplicate_faces(double vtxTolerance, out int nRemoved)
		{
			nRemoved = 0;
			try {
				RemoveDuplicateTriangles dupe = new RemoveDuplicateTriangles(Mesh);
				dupe.VertexTolerance = vtxTolerance;
				bool bOK = dupe.Apply();
				nRemoved = dupe.Removed;
				return bOK;

			} catch (Exception/*e*/) {
				return false;
			}
		}



		bool collapse_degenerate_edges(
			double minLength, bool bBoundaryOnly, 
			out int collapseCount)
		{
			collapseCount = 0;
            // don't iterate sequentially because there may be pathological cases
            foreach (int eid in MathUtil.ModuloIteration(Mesh.MaxEdgeID)) {
                if (Cancelled()) break;
                if (Mesh.IsEdge(eid) == false)
					continue;
                bool is_boundary_edge = Mesh.IsBoundaryEdge(eid);
                if (bBoundaryOnly && is_boundary_edge == false)
					continue;
				Index2i ev = Mesh.GetEdgeV(eid);
				Vector3d a = Mesh.GetVertex(ev.a), b = Mesh.GetVertex(ev.b);
				if (a.Distance(b) < minLength) {
					int keep = Mesh.IsBoundaryVertex(ev.a) ? ev.a : ev.b;
					int discard = (keep == ev.a) ? ev.b : ev.a;
					DMesh3.EdgeCollapseInfo collapseInfo;
					MeshResult result = Mesh.CollapseEdge(keep, discard, out collapseInfo);
					if (result == MeshResult.Ok) {
						++collapseCount;
						if (Mesh.IsBoundaryVertex(keep) == false || is_boundary_edge)
							Mesh.SetVertex(keep, (a + b) * 0.5);
					}
				}
			}
			return true;
		}
		bool collapse_all_degenerate_edges(double minLength, bool bBoundaryOnly)
		{
			bool repeat = true;
			while (repeat) {
                if (Cancelled()) break;
                int collapse_count;
				collapse_degenerate_edges(minLength, bBoundaryOnly, out collapse_count);
				if (collapse_count == 0)
					repeat = false;
			}
			return true;
		}




		bool disconnect_bowties(out int nRemaining)
		{
			MeshEditor editor = new MeshEditor(Mesh);
			nRemaining = editor.DisconnectAllBowties();
			return true;
		}


        void repair_orientation(bool bGlobal)
        {
            MeshRepairOrientation orient = new MeshRepairOrientation(Mesh) { KeepShortSeams = CutOrientationSeams };
            orient.OrientComponents();
            if (Cancelled()) return;
            if (bGlobal)
                orient.SolveGlobalOrientation();
        }




		bool remove_interior(out int nRemoved)
		{
			RemoveOccludedTriangles remove = new RemoveOccludedTriangles(Mesh);
			remove.PerVertex = true;
			remove.InsideMode = RemoveOccludedTriangles.CalculationMode.FastWindingNumber;
			remove.Apply();
			nRemoved = remove.RemovedT.Count();
			return true;
		}
		bool remove_occluded(out int nRemoved)
		{
			RemoveOccludedTriangles remove = new RemoveOccludedTriangles(Mesh);
			remove.PerVertex = true;
			remove.InsideMode = RemoveOccludedTriangles.CalculationMode.SimpleOcclusionTest;
			remove.Apply();
			nRemoved = remove.RemovedT.Count();
			return true;
		}
		bool do_remove_inside()
		{
			int nRemoved = 0;
			if (RemoveMode == RemoveModes.Interior) {
				return remove_interior(out nRemoved);
			} else if (RemoveMode == RemoveModes.Occluded) {
				return remove_occluded(out nRemoved);
			}
			return true;
		}



        /// <summary>
        /// Cut the mesh open along its interior edges whose two triangles run through them in the same
        /// direction (the seams OrientComponents leaves in non-orientable components). Returns the number
        /// of vertices added (0: nothing could be cut).
        /// </summary>
        int cut_orientation_seams()
        {
            List<int> seams = orientation_seams();
            if (seams.Count == 0)
                return 0;
            return new MeshEditor(Mesh).CutAlongEdges(seams);
        }
        bool has_orientation_seams()
        {
            return orientation_seams().Count > 0;
        }
        List<int> orientation_seams()
        {
            List<int> seams = new List<int>();
            foreach (int eid in Mesh.EdgeIndices()) {
                Index2i et = Mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID)
                    continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                Index3i ta = Mesh.GetTriangle(et.a), tb = Mesh.GetTriangle(et.b);
                if ((IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref ta) != DMesh3.InvalidID)
                    == (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref tb) != DMesh3.InvalidID))
                    seams.Add(eid);
            }
            return seams;
        }


        bool remove_loners()
        {
            bool bOK = MeshEditor.RemoveIsolatedTriangles(Mesh);
            return true;
        }


    }
}
