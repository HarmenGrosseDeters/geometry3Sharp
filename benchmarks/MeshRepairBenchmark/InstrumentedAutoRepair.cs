// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using gs;

// Copy of gs.MeshAutoRepair (pipeline as in geometry3Sharp 1.0.324, Boost license) with a callback
// after every step, so the benchmark can measure which step introduces defects.
// Uses the library's MeshEditor, so it includes the fixed DisconnectBowtie.
namespace MeshRepairBenchmark
{
    /// <summary>
    /// Mesh Auto Repair top-level driver.
    /// 
    /// TODO:
    ///   - remove degenerate *faces*  (which may still have all edges > length)
    ///       - this is tricky, in many CAD meshes these faces can't just be collapsed. But can often remove via flipping...?
    /// 
    /// </summary>
    public class InstrumentedAutoRepair
    {
        // tolerances as in gs.MeshAutoRepair: relative to the diagonal of the triangles by default, absolute
        // when set explicitly or with ToleranceModes.Absolute
        public enum ToleranceModes { Relative = 0, Absolute = 1 }
        public ToleranceModes ToleranceMode = ToleranceModes.Relative;
        public double RelativeRepairTolerance = 1e-8;
        public double RelativeMinEdgeLength = 1e-6;

        public double RepairTolerance {
            get { return repairTolerance; }
            set { repairTolerance = value; repairToleranceSet = true; }
        }
        double repairTolerance = MathUtil.ZeroTolerancef;
        bool repairToleranceSet = false;

        // assume edges shorter than this are degenerate and should be collapsed
        public double MinEdgeLengthTol {
            get { return minEdgeLengthTol; }
            set { minEdgeLengthTol = value; minEdgeLengthTolSet = true; }
        }
        double minEdgeLengthTol = 0.0001;
        bool minEdgeLengthTolSet = false;

        public double EffectiveRepairTolerance { get; private set; }
        public double EffectiveMinEdgeLength { get; private set; }

        void compute_tolerances()
        {
            double diag = 0, magnitude = 0;
            if (Mesh.TriangleCount > 0) {
                AxisAlignedBox3d box = MeshRepairMetrics.TriangleBounds(Mesh);
                diag = box.DiagonalLength;
                magnitude = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
            }
            double floor = 16 * MathUtil.Epsilon * magnitude;
            bool absolute = ToleranceMode == ToleranceModes.Absolute;
            EffectiveRepairTolerance = (absolute || repairToleranceSet) ? repairTolerance
                : Math.Max(RelativeRepairTolerance * diag, floor);
            EffectiveMinEdgeLength = (absolute || minEdgeLengthTolSet) ? minEdgeLengthTol
                : (RelativeMinEdgeLength > 0 ? Math.Max(RelativeMinEdgeLength * diag, floor) : 0);
        }

        // number of times we will delete border triangles and try again
        public int ErosionIterations = 5;


        // [TODO] interior components?
        public enum RemoveModes {
			None = 0, Interior = 1, Occluded = 2
		};
		public RemoveModes RemoveMode = InstrumentedAutoRepair.RemoveModes.None;





        /// <summary>
        /// Set this to be able to cancel running remesher
        /// </summary>
        public ProgressCancel Progress = null;

        /// <summary>
        /// if this returns true, abort computation. 
        /// </summary>
        protected virtual bool Cancelled()
        {
            return (Progress == null) ? false : Progress.Cancelled();
        }





        public DMesh3 Mesh;

		public Action<string, DMesh3> OnStage = null;
        void Stage(string name) { OnStage?.Invoke(name, Mesh); }

		public InstrumentedAutoRepair(DMesh3 mesh3)
		{
			Mesh = mesh3;
		}


		public bool Apply()
		{
			bool do_checks = false;

			if ( do_checks ) Mesh.CheckValidity();

            compute_tolerances();

            /*
             * Remove parts of the mesh we don't want before we bother with anything else
             * TODO: maybe we need to repair orientation first? if we want to use MWN...
             */
			do_remove_inside();
                if (Cancelled()) return false;

            int repeat_count = 0;
            repeat_all:

            /*
             * make sure orientation of connected components is consistent
             * TODO: what about mobius strip problems?
             */
            repair_orientation(false); Stage("orient_1");
                if (Cancelled()) return false;

            /*
             *  Do safe close-cracks to handle easy cases
             */

            repair_cracks(true, EffectiveRepairTolerance); Stage("cracks_unique");
                if (Mesh.IsClosed()) goto all_done;
                if (Cancelled()) return false;

            /*
             * Collapse tiny edges and then try easy cases again, and
             * then allow for handling of ambiguous cases
             */

            collapse_all_degenerate_edges(EffectiveRepairTolerance*0.5, true); Stage("collapse_boundary_edges");
                if (Cancelled()) return false;
            repair_cracks(true, 2*EffectiveRepairTolerance); Stage("cracks_unique_2x");
                if (Cancelled()) return false;
            repair_cracks(false, 2*EffectiveRepairTolerance); Stage("cracks_any_2x");
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * Possibly we have joined regions with different orientation (is it?), fix that
             * TODO: mobius strips again
             */
            repair_orientation(false); Stage("orient_2");
                if (Cancelled()) return false;

            if (do_checks) Mesh.CheckValidity();

            // get rid of any remaining single-triangles before we start filling holes
            remove_loners(); Stage("remove_loners");

            /*
             * Ok, fill simple holes. 
             */
            int nRemainingBowties = 0;
			int nHoles; bool bSawSpans;
			fill_trivial_holes(out nHoles, out bSawSpans); Stage("fill_trivial_holes");
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * Now fill harder holes. If we saw spans, that means boundary loops could
             * not be resolved in some cases, do we disconnect bowties and try again.
             */
            fill_any_holes(out nHoles, out bSawSpans); Stage("fill_any_holes");
                if (Cancelled()) return false;
            if (bSawSpans) {
				disconnect_bowties(out nRemainingBowties);
				fill_any_holes(out nHoles, out bSawSpans); Stage("bowties+fill_any_holes");
			}
                if (Cancelled()) return false;
                if (Mesh.IsClosed()) goto all_done;

            /*
             * We may have a closed mesh now but it might still have bowties (eg
             * tetrahedra sharing vtx case). So disconnect those.
             */
            disconnect_bowties(out nRemainingBowties); Stage("disconnect_bowties");
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
                MeshEditor.RemoveTriangles(Mesh, bdry_faces, true); Stage("erosion");
                goto repeat_all;
            }

            all_done:

            /*
             * Remove tiny edges
             */
            if (EffectiveMinEdgeLength > 0) {
                collapse_all_degenerate_edges(EffectiveMinEdgeLength, false); Stage("collapse_short_edges");
            }
                if (Cancelled()) return false;

            /*
             * finally do global orientation
             */
            repair_orientation(true); Stage("orient_global");
                if (Cancelled()) return false;

            if (do_checks) Mesh.CheckValidity();

            /*
             * Might as well compact output mesh...
             */
			Mesh = new DMesh3(Mesh, true); Stage("compact");
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
                MinimalHoleFill filler = new MinimalHoleFill(Mesh, loop);
				bool filled = filler.Apply();
				if (filled == false) {
                    if (Cancelled()) break;
                    SimpleHoleFiller fallback = new SimpleHoleFiller(Mesh, loop);
					filled = fallback.Fill();
				}
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
            MeshRepairOrientation orient = new MeshRepairOrientation(Mesh);
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



        bool remove_loners()
        {
            bool bOK = MeshEditor.RemoveIsolatedTriangles(Mesh);
            return true;
        }


    }
}
