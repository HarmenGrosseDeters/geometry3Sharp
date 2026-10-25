using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace g3
{

	/// <summary>
	/// Cut the Mesh with the Plane, in place. The *positive* side, ie (p-o).n &gt; 0, is removed.
    /// If possible, Cut() returns the boundary loop(s) along the cut in CutLoops, and open boundary
	/// pieces in CutSpans (this happens if the cut intersects holes in the mesh; FoundOpenSpans is set).
	/// Also FillHoles() for a topological fill of the closed loops. Or use CutLoops and fill yourself.
	///
	/// Cut() returns true (it throws if an edge split fails); check CutLoopsFailed for the loop extraction.
	/// Vertices within MathUtil.Epsilon of the plane count as on the plane (use a unit-length normal, the
	/// tolerance is applied to (p-o).n). Crossing edges are split at the exact plane intersection.
	/// Use MeshPlaneCut to trim a mesh or open it along a plane; to cut a mesh with another mesh see
	/// <see cref="MeshMeshCut"/> and <see cref="ExactMeshBoolean"/>.
	/// 
	/// Algorithm is:
	///    1) find all edge crossings
	///    2) Do edge splits at crossings
	///    3) delete all vertices on positive side
    ///    4) (optionally) collapse any degenerate boundary edges 
	///    5) find loops through valid boundary edges (ie connected to splits, or on-plane edges)
	/// 
	/// [TODO] could run into trouble w/ on-plane degenerate triangles. Should optionally
	///   discard any triangles with all vertex distances &lt; epsilon. But this complicates
	///   boundary edge tracking...
	/// 
	/// </summary>
	public class MeshPlaneCut
	{
        // Inputs
		/// <summary> the mesh to cut (modified in place) </summary>
		public DMesh3 Mesh;
		/// <summary> a point on the plane </summary>
		public Vector3d PlaneOrigin;
		/// <summary> plane normal; the side it points to is removed </summary>
		public Vector3d PlaneNormal;

        /// <summary>
        /// a plane cut very near a vertex can result in degenerate edges on the open loops/spans, which 
        /// can cause problems downstream (eg if hole-filling). It is easy for us to collapse these before
        /// we construct the loops. Default true
        /// </summary>
        public bool CollapseDegenerateEdgesOnCut = true;

        /// <summary> the min-edge-length if we are collapsing degenerate edges </summary>
        public double DegenerateEdgeTol = MathUtil.ZeroTolerancef;

        /// <summary> if non-null, we will only cut the edges of these triangles and remove their vertices </summary>
        public MeshFaceSelection CutFaceSet = null;

        // Outputs
		/// <summary> closed boundary loops along the cut, after Cut() (empty if CutLoopsFailed) </summary>
		public List<EdgeLoop> CutLoops;
        /// <summary> open boundary spans along the cut (where the cut meets holes), after Cut(); not set if CutLoopsFailed </summary>
        public List<EdgeSpan> CutSpans;
        /// <summary> set to true if we could not compute cut loops/spans </summary>
        public bool CutLoopsFailed = false;
        /// <summary> set to true if we found open spans in cut </summary>
        public bool FoundOpenSpans = false;

		/// <summary> after FillHoles(): the new triangles of each loop in CutLoops (null where filling failed) </summary>
		public List<int[]> LoopFillTriangles;

		/// <summary>
		/// Cut mesh with the plane through origin with the given normal (any direction; the side the normal
		/// points to is removed). Call Cut() to run.
		/// </summary>
		public MeshPlaneCut(DMesh3 mesh, Vector3d origin, Vector3d normal)
		{
			Mesh = mesh;
			PlaneOrigin = origin;
			PlaneNormal = normal;
		}


		public virtual ValidationStatus Validate()
		{
			// [TODO]
			return ValidationStatus.Ok;
		}


		/// <summary>
		/// Split the edges that cross the plane, remove the triangles on the positive side, and find the cut
		/// boundary loops/spans (CutLoops, CutSpans, CutLoopsFailed, FoundOpenSpans). Modifies Mesh in place.
		/// Returns true; throws an Exception if an edge split fails.
		/// </summary>
		public virtual bool Cut()
		{
			double invalidDist = double.MinValue;

            MeshEdgeSelection CutEdgeSet = null;
            MeshVertexSelection CutVertexSet = null;
            if (CutFaceSet != null) {
                CutEdgeSet = new MeshEdgeSelection(Mesh, CutFaceSet);
                CutVertexSet = new MeshVertexSelection(Mesh, CutEdgeSet);
            }

			// compute signs
			int MaxVID = Mesh.MaxVertexID;
			double[] signs = new double[MaxVID];
			gParallel.ForEach(Interval1i.Range(MaxVID), (vid) => {
				if (Mesh.IsVertex(vid)) {
					Vector3d v = Mesh.GetVertex(vid);
					signs[vid] = (v - PlaneOrigin).Dot(PlaneNormal);
				} else
					signs[vid] = invalidDist;
			});

			HashSet<int> ZeroEdges = new HashSet<int>();
			HashSet<int> ZeroVertices = new HashSet<int>();
			HashSet<int> OnCutEdges = new HashSet<int>();

			// have to skip processing of new edges. If edge id
			// is > max at start, is new. Otherwise if in NewEdges list, also new.
			int MaxEID = Mesh.MaxEdgeID;
			HashSet<int> NewEdges = new HashSet<int>();

            IEnumerable<int> edgeItr = Interval1i.Range(MaxEID);
            if (CutEdgeSet != null)
                edgeItr = CutEdgeSet;

            // cut existing edges with plane, using edge split
            foreach (int eid in edgeItr) { 
				if (Mesh.IsEdge(eid) == false)
					continue;
				if (eid >= MaxEID || NewEdges.Contains(eid))
					continue;

				Index2i ev = Mesh.GetEdgeV(eid);
				double f0 = signs[ev.a];
				double f1 = signs[ev.b];

				// If both signs are 0, this edge is on-contour
				// If one sign is 0, that vertex is on-contour
				int n0 = (Math.Abs(f0) < MathUtil.Epsilon) ? 1 : 0;
				int n1 = (Math.Abs(f1) < MathUtil.Epsilon) ? 1 : 0;
				if (n0 + n1 > 0) {
					if (n0 + n1 == 2)
						ZeroEdges.Add(eid);
					else
						ZeroVertices.Add((n0 == 1) ? ev[0] : ev[1]);
					continue;
				}

				// no crossing
				if (f0 * f1 > 0)
					continue;

				DMesh3.EdgeSplitInfo splitInfo;
				MeshResult result = Mesh.SplitEdge(eid, out splitInfo);
				if (result != MeshResult.Ok) {
					throw new Exception("MeshPlaneCut.Cut: failed in SplitEdge");
					//return false;
				}

				// SplitEdge just bisects edge - use plane intersection instead
				double t = f0 / (f0 - f1);
				Vector3d newPos = (1 - t) * Mesh.GetVertex(ev.a) + (t) * Mesh.GetVertex(ev.b);
				Mesh.SetVertex(splitInfo.vNew, newPos);

				NewEdges.Add(splitInfo.eNewBN);
				NewEdges.Add(splitInfo.eNewCN);  OnCutEdges.Add(splitInfo.eNewCN);
				if (splitInfo.eNewDN != DMesh3.InvalidID) {
					NewEdges.Add(splitInfo.eNewDN);
					OnCutEdges.Add(splitInfo.eNewDN);
				}
			}

            // remove one-rings of all positive-side vertices. 
            IEnumerable<int> vertexSet = Interval1i.Range(MaxVID);
            if (CutVertexSet != null)
                vertexSet = CutVertexSet;
            foreach ( int vid in vertexSet ) { 
				if (signs[vid] > 0 && Mesh.IsVertex(vid))
					Mesh.RemoveVertex(vid, true, false);
			}

            // collapse degenerate edges if we got em
            if (CollapseDegenerateEdgesOnCut) {
                collapse_degenerate_edges(OnCutEdges, ZeroEdges);
            }


			// ok now we extract boundary loops, but restricted
			// to either the zero-edges we found, or the edges we created! bang!!
			Func<int, bool> CutEdgeFilterF = (eid) => {
				if (OnCutEdges.Contains(eid) || ZeroEdges.Contains(eid))
					return true;
				return false;
			};
			try {
				MeshBoundaryLoops loops = new MeshBoundaryLoops(Mesh, false);
				loops.EdgeFilterF = CutEdgeFilterF;
				loops.Compute();

				CutLoops = loops.Loops;
                CutSpans = loops.Spans;
				CutLoopsFailed = false;
                FoundOpenSpans = CutSpans.Count > 0;

			} catch {
				CutLoops = new List<EdgeLoop>();
				CutLoopsFailed = true;
			}

            return true;

		} // Cut()



        protected void collapse_degenerate_edges(HashSet<int> OnCutEdges, HashSet<int> ZeroEdges)
        {
            HashSet<int>[] sets = new HashSet<int>[2] { OnCutEdges, ZeroEdges };

            double tol2 = DegenerateEdgeTol * DegenerateEdgeTol;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero;
            int collapsed = 0;
            do {
                collapsed = 0;
                foreach (var edge_set in sets) {
                    foreach (int eid in edge_set) {
                        if (Mesh.IsEdge(eid) == false)
                            continue;
                        Mesh.GetEdgeV(eid, ref a, ref b);
                        if (a.DistanceSquared(b) > tol2)
                            continue;

                        Index2i ev = Mesh.GetEdgeV(eid);
                        DMesh3.EdgeCollapseInfo collapseInfo;
                        MeshResult result = Mesh.CollapseEdge(ev.a, ev.b, out collapseInfo);
                        if (result == MeshResult.Ok)
                            collapsed++;
                    }
                }
            } while (collapsed != 0);
        }





		/// <summary>
		/// A quick-and-dirty hole filling: each loop in CutLoops is filled with SimpleHoleFiller (one triangle for
		/// 3-vertex loops, otherwise a fan around a new centroid vertex), with a new triangle group per loop unless constantGroupID &gt;= 0. Open CutSpans
		/// are not filled. Returns false if any loop could not be filled (see LoopFillTriangles).
		/// If you want something better, process the returned CutLoops yourself. Call after Cut().
		/// </summary>
		public bool FillHoles(int constantGroupID = -1)
		{
			bool bAllOk = true;

			LoopFillTriangles = new List<int[]>(CutLoops.Count);

			foreach ( EdgeLoop loop in CutLoops) {
				SimpleHoleFiller filler = new SimpleHoleFiller(Mesh, loop);
				int gid = (constantGroupID >= 0) ? constantGroupID : Mesh.AllocateTriangleGroup();
				if ( filler.Fill(gid) ) {
					LoopFillTriangles.Add(filler.NewTriangles);
				} else {
					bAllOk = false;
					LoopFillTriangles.Add(null);
				}
			}

			return bAllOk;

		} // FillHoles




	}
}
