using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;


namespace g3 {
	
	/// <summary>
	/// Isotropic remeshing in place (Botsch and Kobbelt style): each BasicRemeshPass() splits edges longer than
	/// MaxEdgeLength, collapses edges shorter than MinEdgeLength, flips edges to improve vertex valence, then
	/// applies tangential-ish Laplacian smoothing and (optionally) projects the vertices onto a target surface.
	/// Call SetTargetEdgeLength() and then BasicRemeshPass() several times (typically 5-20 passes;
	/// ModifiedEdgesLastPass gives a convergence hint). There is no return value; the mesh (passed to the
	/// constructor) is modified directly, vertex and triangle ids are not preserved. A pass that would turn a closed
	/// component inside out (a target edge length far above the wall thickness) is rolled back and counted in
	/// InvertingPassesRejected (RejectInvertingPasses).
	///
	/// Important behaviour: without a projection target the smoothing shrinks and rounds the surface, so for
	/// shape-preserving remeshing call SetProjectionTarget(MeshProjectionTarget.Auto(originalMesh)). Boundary
	/// edges and vertices are split, collapsed and smoothed like interior ones unless constrained, use
	/// <see cref="MeshConstraintUtil"/> (FixAllBoundaryEdges, PreserveBoundaryLoops, PreserveBoundaryAndFeatures)
	/// or SetExternalConstraints(). PreventNormalFlips is off by default. The default (buffered) parallel smoothing
	/// gives the same result as the serial one; only EnableSmoothInPlace with parallel smoothing depends on the
	/// thread timing.
	///
	/// See <see cref="gs.RemesherPro"/> for a faster variant (queue-based passes and a FastestRemesh/SharpEdgeReprojectionRemesh
	/// driver), <see cref="RegionRemesher"/> to remesh only part of a mesh, and <see cref="Reducer"/> for
	/// simplification to a triangle count or error (Remesher only targets edge lengths).
	/// </summary>
	public class Remesher : MeshRefinerBase
    {
        IProjectionTarget target = null;

		/// <summary> allow edge flips (to improve valence; boundary edges are never flipped) </summary>
		public bool EnableFlips = true;
		/// <summary> allow collapses of edges shorter than MinEdgeLength </summary>
		public bool EnableCollapses = true;
		/// <summary> allow splits of edges longer than MaxEdgeLength </summary>
		public bool EnableSplits = true;
		/// <summary> run a smoothing pass after the edge operations of each pass </summary>
		public bool EnableSmoothing = true;

        /// <summary> reject collapses and flips that would flip a triangle normal (see EdgeFlipTolerance). Default false </summary>
        public bool PreventNormalFlips = false;

		/// <summary> edges shorter than this are collapsed. Usually set with SetTargetEdgeLength() </summary>
		public double MinEdgeLength = 0.001f;
		/// <summary> edges longer than this are split. Usually set with SetTargetEdgeLength() </summary>
		public double MaxEdgeLength = 0.1f;

        /// <summary>
        /// Opt-in quality safeguards, 0 = off (default, the original behaviour). If &gt; 0 (eg 0.2), with quality
        /// 4*sqrt(3)*area / sum of squared edge lengths (1 equilateral, 0 degenerate):
        ///  - vertices on constraint curves are smoothed along their curve (towards their two neighbours across edges
        ///    constrained to the same target; junctions and curve ends stay) instead of with the surface smoothing,
        ///    so the projection onto the curve keeps them in place along it (otherwise they could land on another part
        ///    of the curve, on a corner or on an edge);
        ///  - a smoothing move is rejected if it flips an incident (not flat) triangle, or leaves one below this
        ///    quality and worse than before; a projection move that would flip one is shortened to a half, quarter or
        ///    eighth step, or rejected (the next pass gets closer);
        ///  - the buffered smoothing and projection passes revert the moves of every triangle the moves together
        ///    would still flip; the projection pass is then buffered too, so the result does not depend on the order
        ///    or thread timing (not with EnableSmoothInPlace, whose parallel pass reads positions being written);
        ///  - an edge whose triangle pair contains a triangle below this quality is flipped if that at least doubles
        ///    the pair's minimum quality and keeps the orientation;
        ///  - an edge with a flat neighbouring triangle (its opposite vertex on the edge) is not split, which would
        ///    put the new vertex onto that vertex;
        ///  - collapses and flips that would flip a triangle normal are rejected (as with PreventNormalFlips);
        ///  - a collapse that would create a flat triangle (eg two vertices of a straight feature curve joined over
        ///    a third one on it) is rejected. Other collapses are not restricted: blocking poor but not flat results
        ///    (eg with MinCollapseTriangleQuality) keeps short edges that the splits around them multiply.
        /// Not guarded: the projection of a new vertex after a split. Together, on 20 Thingi10K meshes remeshed with
        /// creases kept, 28% of the poor (q &lt; 0.1) and 13% of the flat (q &lt; 1e-3) triangles and 16% of the
        /// self-intersecting pairs remain (benchmarks/RemeshQualityBenchmark). RemesherPro queues the edges of a
        /// vertex whose move was rejected. With a target edge length far above the wall thickness the result is
        /// undefined with and without this option: the walls are folded through each other (self-intersections);
        /// passes that turn a closed component inside out are rolled back (RejectInvertingPasses).
        /// </summary>
        public double MinTriangleQuality = 0;

        /// <summary>
        /// Roll back a pass (BasicRemeshPass, RemesherPro.RemeshIteration) that turns a closed component of the mesh
        /// inside out (an outer surface or a cavity): after it, the number of closed components with a positive or
        /// with a negative signed volume differs from before, or a component has another sign than most of its
        /// vertices had before (a volume within 1e-9 area^1.5 of zero counts as neither, so a component flattened to
        /// zero volume is rolled back too). This happens when the target edge length
        /// is far above the wall thickness (the walls are folded through each other), with or without
        /// MinTriangleQuality. The mesh and the constraints are restored to their state before the pass and
        /// InvertingPassesRejected is incremented; the next pass starts from there again (and is usually rolled back
        /// too, so the result stays at the last pass that kept the orientation). Costs a copy of the mesh and the
        /// constraints per pass if the mesh has a closed component. The rollback is for the whole mesh: one small part
        /// that keeps inverting (eg a coarse cone among finer parts) stops the remeshing of all other parts too. In
        /// RemesherPro.FastestRemesh a rolled-back last iteration leaves the state after an iteration without
        /// projection. Not guarded: the other passes (FullSmoothPass, FullProjectionPass,
        /// RemesherPro.FastSplitIteration, TrackedFaceProjectionPass) and a cancelled pass. Default true (false in
        /// EdgeLoopRemesher).
        /// </summary>
        public bool RejectInvertingPasses = true;

        /// <summary> status: the number of passes rolled back by RejectInvertingPasses so far </summary>
        public int InvertingPassesRejected = 0;

		/// <summary> smoothing step per pass, in [0,1]: each vertex moves this fraction towards its smoothed position </summary>
		public double SmoothSpeedT = 0.1f;
		/// <summary> weights of the smoothing: uniform (one-ring centroid), cotangent or mean-value </summary>
		public enum SmoothTypes {
			Uniform, Cotan, MeanValue
		};
		/// <summary> smoothing weights used if CustomSmoothF is not set </summary>
		public SmoothTypes SmoothType = SmoothTypes.Uniform;

        /// <summary> this overrides default smoothing if provided: (mesh, vertex id, SmoothSpeedT) -> new position </summary>
        public Func<DMesh3, int, double, Vector3d> CustomSmoothF;

        
        // Sometimes we need to have very granular control over what happens to
        // specific vertices. This function allows client to specify such behavior.
        // Somewhat redundant w/ VertexConstraints, but simpler to code.
        /// <summary> per-vertex overrides returned by VertexControlF </summary>
        [Flags] public enum VertexControl
        {
            AllowAll = 0,
            NoSmooth = 1,
            NoProject = 2,
            NoMovement = NoSmooth | NoProject
        }
        /// <summary> optional per-vertex control (vertex id -> allowed movement); null = allow all </summary>
        public Func<int, VertexControl> VertexControlF;


        // other options

        // [RMS] this is a debugging aid, will break to debugger if these edges are touched, in debug builds
        public List<int> DebugEdges = new List<int>();

        /// <summary> if a projection target is set, we can project onto it in different ways </summary>
        public enum TargetProjectionMode
        {
            NoProjection,           // disable projection
            AfterRefinement,        // do all projection after the refine/smooth pass
            Inline                  // project after each vertex update. Better results but more
                                    // expensive because eg we might create a vertex with
                                    // split, then project, then smooth, then project again.
        }
        /// <summary> when vertices are projected onto the projection target. Default AfterRefinement </summary>
        public TargetProjectionMode ProjectionMode = TargetProjectionMode.AfterRefinement;

        // this just lets us write more concise tests below
        bool EnableInlineProjection { get { return ProjectionMode == TargetProjectionMode.Inline; } }

        /// <summary> enable parallel projection. Only applied in AfterRefinement mode </summary>
        public bool EnableParallelProjection = true;

        /// <summary>
        /// Enable parallel smoothing (default true). With EnableSmoothInPlace this produces slightly different
        /// results across runs (order side-effects); with the default buffered smoothing the positions are
        /// computed from the previous pass.
        /// </summary>
        public bool EnableParallelSmooth = true;

        /// <summary>
        /// if smoothing is done in-place, we don't need an extra buffer, but also 
        /// there will some randomness introduced in results. Probably worse. Default false
        /// </summary>
        public bool EnableSmoothInPlace = false;

		/// <summary> remesh mesh m in place </summary>
		public Remesher(DMesh3 m) : base(m)
        {
		}
        protected Remesher()        // for subclasses that extend our behavior
        {
        }



        /// <summary> the surface vertices are projected onto, or null </summary>
        public IProjectionTarget ProjectionTarget {
            get { return this.target; }
        }
        /// <summary>
        /// Set the surface the vertices are projected onto (see ProjectionMode), typically
        /// MeshProjectionTarget.Auto(copy of the input mesh) to stay on the original shape. Null disables projection.
        /// </summary>
        public void SetProjectionTarget(IProjectionTarget target)
        {
            this.target = target;
        }

        
        /// <summary>
        /// Set min/max edge-lengths to sane values for given target edge length:
        /// MinEdgeLength = 0.66 * fLength, MaxEdgeLength = 1.33 * fLength
        /// </summary>
        public void SetTargetEdgeLength(double fLength)
        {
            // from Botsch paper
            //MinEdgeLength = fLength * (4.0/5.0);
            //MaxEdgeLength = fLength * (4.0/3.0);
            // much nicer!! makes sense as when we split, edges are both > min !
            MinEdgeLength = fLength * 0.66;
            MaxEdgeLength = fLength * 1.33;
        }


        public bool ENABLE_PROFILING = false;


        // glboal mesh info that, if known, lets us avoid work in remesh
        bool MeshIsClosed = false;

        /// <summary>
        /// we can vastly speed things up if we precompute some invariants. 
        /// You need to re-run this if you are changing the mesh externally
        /// between remesh passes, otherwise you will get weird results.
        /// But you will probably still come out ahead, computation-time-wise.
        /// Optional: BasicRemeshPass() does not call it. Currently it only detects a closed mesh (no boundary
        /// edges), which skips boundary-vertex checks in the flip test.
        /// </summary>
        public virtual void Precompute()
        {
            // if we know mesh is closed, we can skip is-boundary checks, which makes
            // the flip-valence tests much faster!
            MeshIsClosed = true;
            foreach (int eid in mesh.EdgeIndices()) {
                if (mesh.IsBoundaryEdge(eid)) {
                    MeshIsClosed = false;
                    break;
                }
            }
        }



        /// <summary>
        /// Number of edges that were modified in previous Remesh pass.
        /// If this number gets small relative to edge count, you have probably converged (ish)
        /// </summary>
        public int ModifiedEdgesLastPass = 0;





        /// <summary>
        /// Linear edge-refinement pass, followed by smoothing and projection
        /// - Edges are processed in prime-modulo-order to break symmetry
        /// - smoothing is done in parallel if EnableParallelSmooth = true
        /// - Projection pass if ProjectionMode == AfterRefinement
        /// - number of modified edges returned in ModifiedEdgesLastPass
        /// Modifies the mesh in place; returns early (leaving a valid mesh) if Progress is cancelled,
        /// and does nothing for an empty mesh.
        /// </summary>
		public virtual void BasicRemeshPass() {
            if (mesh.TriangleCount == 0)    // badness if we don't catch this...
                return;

            begin_pass();
            object inversionGuard = begin_inversion_guard();

            // Iterate over all edges in the mesh at start of pass.
            // Some may be removed, so we skip those.
            // However, some old eid's may also be re-used, so we will touch
            // some new edges. Can't see how we could efficiently prevent this.
            //
            begin_ops();

            int cur_eid = start_edges();
            bool done = false;
            ModifiedEdgesLastPass = 0;
            do {
                if (mesh.IsEdge(cur_eid)) {
                    ProcessResult result = ProcessEdge(cur_eid);
                    if (result == ProcessResult.Ok_Collapsed || result == ProcessResult.Ok_Flipped || result == ProcessResult.Ok_Split)
                        ModifiedEdgesLastPass++;
                }
                if (Cancelled())        // expensive to check every iter?
                    return;
                cur_eid = next_edge(cur_eid, out done);
            } while (done == false);
            end_ops();

            if (Cancelled())
                return;

            begin_smooth();
            if (EnableSmoothing && SmoothSpeedT > 0) {
                if (EnableSmoothInPlace)
                    FullSmoothPass_InPlace(EnableParallelSmooth);
                else
                    FullSmoothPass_Buffer(EnableParallelSmooth);
                DoDebugChecks();
            }
            end_smooth();

            if (Cancelled())
                return;

            begin_project();
            if (target != null && ProjectionMode == TargetProjectionMode.AfterRefinement) {
                FullProjectionPass();
                DoDebugChecks();
            }
            end_project();

            if (Cancelled())
                return;

            end_inversion_guard(inversionGuard);
            end_pass();
		}



        // RejectInvertingPasses: the closed components of a mesh by the sign of their signed volume
        sealed class ComponentSigns
        {
            public Index2i Counts;          // components with a positive (a) and a negative (b) volume
            public sbyte[] VertexSign;      // per vertex id: the sign of its closed component (0: open, no volume, no vertex)
        }

        // the state before the pass
        sealed class InversionSnapshot
        {
            public DMesh3 Mesh;
            public MeshConstraints Constraints;
            public ComponentSigns Signs;
        }

        // the signs after the last guarded pass, valid while mesh.Timestamp is unchanged (saves one count per pass)
        ComponentSigns signsCached;
        int signsTimestamp = -1;

        /// <summary> RejectInvertingPasses: snapshot of the mesh before a pass, or null (off, or no closed component with a volume) </summary>
        protected object begin_inversion_guard()
        {
            if (RejectInvertingPasses == false)
                return null;
            if (signsCached == null || mesh.Timestamp != signsTimestamp) {
                signsCached = compute_component_signs(mesh, null, out _);
                signsTimestamp = mesh.Timestamp;
            }
            if (signsCached.Counts.a == 0 && signsCached.Counts.b == 0)
                return null;
            return new InversionSnapshot() {
                Mesh = new DMesh3(mesh), Constraints = constraints?.Copy(), Signs = signsCached
            };
        }

        /// <summary> RejectInvertingPasses: restore the snapshot if the pass turned a closed component inside out. Returns true if it did </summary>
        protected bool end_inversion_guard(object guard)
        {
            var snapshot = guard as InversionSnapshot;
            if (snapshot == null)
                return false;
            ComponentSigns signs = compute_component_signs(mesh, snapshot.Signs.VertexSign, out bool componentChanged);
            if (componentChanged == false && signs.Counts.a == snapshot.Signs.Counts.a && signs.Counts.b == snapshot.Signs.Counts.b) {
                signsCached = signs;
                signsTimestamp = mesh.Timestamp;
                return false;
            }
            mesh.Copy(snapshot.Mesh);
            signsCached = snapshot.Signs;
            signsTimestamp = mesh.Timestamp;
            if (constraints != null)
                constraints.CopyFrom(snapshot.Constraints);
            ModifiedEdgesLastPass = 0;
            InvertingPassesRejected++;
            OnPassRolledBack();
            return true;
        }

        /// <summary> called after RejectInvertingPasses restored the mesh (subclasses reset their state that refers to ids) </summary>
        protected virtual void OnPassRolledBack()
        {
        }

        /// <summary>
        /// The number of closed components (connected through edges, without a boundary edge) of mesh with a positive
        /// (a) and a negative (b) signed volume; a volume within 1e-9 area^1.5 of zero counts as neither.
        /// </summary>
        internal static Index2i ClosedComponentSigns(DMesh3 mesh)
        {
            return compute_component_signs(mesh, null, out _).Counts;
        }

        // The signs of the closed components. With before (VertexSign of an earlier state), componentChanged is true
        // if a closed component has another sign than most of its vertices had before (by id; ids reused within the
        // pass are outvoted): catches two components that change sign in opposite directions in the same pass, which
        // leaves the counts unchanged.
        static ComponentSigns compute_component_signs(DMesh3 mesh, sbyte[] before, out bool componentChanged)
        {
            // the buffers directly: this runs twice per pass
            DVector<int> edges = mesh.EdgesBuffer, tris = mesh.TrianglesBuffer;
            DVector<double> verts = mesh.VerticesBuffer;
            RefCountVector edgesRef = mesh.EdgesRefCounts, trisRef = mesh.TrianglesRefCounts;
            int NT = mesh.MaxTriangleID, NE = mesh.MaxEdgeID;
            int[] parent = new int[NT];
            for (int t = 0; t < NT; ++t)
                parent[t] = t;
            int find(int t) {
                while (parent[t] != t) {
                    parent[t] = parent[parent[t]];
                    t = parent[t];
                }
                return t;
            }
            var open = new List<int>();
            for (int eid = 0; eid < NE; ++eid) {
                if (edgesRef.isValidUnsafe(eid) == false)
                    continue;
                int ta = edges[4 * eid + 2], tb = edges[4 * eid + 3];
                if (tb == DMesh3.InvalidID) {
                    open.Add(ta);
                } else {
                    int ra = find(ta), rb = find(tb);
                    if (ra != rb)
                        parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            }
            bool[] isOpen = new bool[NT];
            foreach (int t in open)
                isOpen[find(t)] = true;

            // relative to a vertex of the component (the lowest triangle's first one), so a translation of the
            // mesh changes the volume only by rounding
            int[] origin = new int[NT];
            double[] volume = new double[NT], area = new double[NT];
            long[] votes = (before != null) ? new long[NT] : null;
            for (int t = 0; t < NT; ++t)
                origin[t] = -1;
            for (int t = 0; t < NT; ++t) {
                if (trisRef.isValidUnsafe(t) == false)
                    continue;
                int r = find(t);
                if (isOpen[r])
                    continue;
                int va = tris[3 * t], vb = tris[3 * t + 1], vc = tris[3 * t + 2];
                if (origin[r] < 0)
                    origin[r] = va;
                int o = 3 * origin[r];
                double ox = verts[o], oy = verts[o + 1], oz = verts[o + 2];
                Vector3d a = new Vector3d(verts[3 * va] - ox, verts[3 * va + 1] - oy, verts[3 * va + 2] - oz);
                Vector3d b = new Vector3d(verts[3 * vb] - ox, verts[3 * vb + 1] - oy, verts[3 * vb + 2] - oz);
                Vector3d c = new Vector3d(verts[3 * vc] - ox, verts[3 * vc + 1] - oy, verts[3 * vc + 2] - oz);
                volume[r] += a.Dot(b.Cross(c));
                area[r] += (b - a).Cross(c - a).Length;
                if (votes != null) {
                    if (va < before.Length) votes[r] += before[va];
                    if (vb < before.Length) votes[r] += before[vb];
                    if (vc < before.Length) votes[r] += before[vc];
                }
            }
            int nPositive = 0, nNegative = 0;
            sbyte[] sign = new sbyte[NT];
            componentChanged = false;
            for (int r = 0; r < NT; ++r) {
                if (origin[r] < 0)
                    continue;
                double V = volume[r] / 6, A = area[r] / 2, tol = 1e-9 * A * Math.Sqrt(A);
                if (V > tol) { nPositive++; sign[r] = 1; }
                else if (V < -tol) { nNegative++; sign[r] = -1; }
                if (votes != null && votes[r] != 0 && Math.Sign(votes[r]) != sign[r])
                    componentChanged = true;
            }
            var result = new ComponentSigns() { Counts = new Index2i(nPositive, nNegative), VertexSign = new sbyte[mesh.MaxVertexID] };
            for (int t = 0; t < NT; ++t) {
                if (trisRef.isValidUnsafe(t) == false)
                    continue;
                int r = find(t);
                if (isOpen[r])
                    continue;
                result.VertexSign[tris[3 * t]] = result.VertexSign[tris[3 * t + 1]] = result.VertexSign[tris[3 * t + 2]] = sign[r];
            }
            return result;
        }


        // subclasses can override these to implement custom behavior...

        protected virtual void OnEdgeSplit(int edgeID, int va, int vb, DMesh3.EdgeSplitInfo splitInfo)
        {
            // this is for subclasses...
        }

        protected virtual void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo collapseInfo)
        {
            // this is for subclasses...
        }




        // start_edges() and next_edge() control the iteration over edges that will be refined.
        // Default here is to iterate over entire mesh.
        // Subclasses can override these two functions to restrict the affected edges (eg EdgeLoopRemesher)


        // We are using a modulo-index loop to break symmetry/pathological conditions. 
        // For example in a highly tessellated minimal cylinder, if the top/bottom loops have
        // sequential edge IDs, and all edges are < min edge length, then we can easily end
        // up successively collapsing each tiny edge, and eroding away the entire mesh!
        // By using modulo-index loop we jump around and hence this is unlikely to happen.
        int nPrime = 31337;     // any prime coprime to MaxEdgeID will do...
        int nMaxEdgeID;
        protected virtual int start_edges()
        {
            nMaxEdgeID = mesh.MaxEdgeID;
            nPrime = MathUtil.ModuloIterationStep(nMaxEdgeID);
            return 0;
        }

        protected virtual int next_edge(int cur_eid, out bool bDone)
        {
            int new_eid = (cur_eid + nPrime) % nMaxEdgeID;
            bDone = (new_eid == 0);
            return new_eid;
        }


        protected virtual IEnumerable<int> smooth_vertices()
        {
            return mesh.VertexIndices();
        }
        protected virtual IEnumerable<int> project_vertices()
        {
            return mesh.VertexIndices();
        }




		protected enum ProcessResult {
			Ok_Collapsed,
			Ok_Flipped,
			Ok_Split,
			Ignored_EdgeIsFine,
            Ignored_EdgeIsFullyConstrained,
			Failed_OpNotSuccessful,
			Failed_NotAnEdge
		};

		protected virtual ProcessResult ProcessEdge(int edgeID) 
		{
            RuntimeDebugCheck(edgeID);

            EdgeConstraint constraint =
                (constraints == null) ? EdgeConstraint.Unconstrained : constraints.GetEdgeConstraint(edgeID);
            if (constraint.NoModifications)
                return ProcessResult.Ignored_EdgeIsFullyConstrained;

			// look up verts and tris for this edge
			int a = 0, b = 0, t0 = 0, t1 = 0;
			if ( mesh.GetEdge(edgeID, ref a, ref b, ref t0, ref t1) == false )
				return ProcessResult.Failed_NotAnEdge;
			bool bIsBoundaryEdge = (t1 == DMesh3.InvalidID);

            // look up 'other' verts c (from t0) and d (from t1, if it exists)
            Index2i ov = mesh.GetEdgeOpposingV(edgeID);
            int c = ov.a, d = ov.b;

			Vector3d vA = mesh.GetVertex(a);
			Vector3d vB = mesh.GetVertex(b);
            double edge_len_sqr = vA.DistanceSquared(vB);

            begin_collapse();

            // check if we should collapse, and also find which vertex we should collapse to,
            // in cases where we have constraints/etc
            int collapse_to = -1;
            bool bCanCollapse = EnableCollapses
                                && constraint.CanCollapse
                                && edge_len_sqr < MinEdgeLength*MinEdgeLength
                                && can_collapse_constraints(edgeID, a, b, c, d, t0, t1, out collapse_to);

			// optimization: if edge cd exists, we cannot collapse or flip. look that up here?
			//  funcs will do it internally...
			//  (or maybe we can collapse if cd exists? edge-collapse doesn't check for it explicitly...)

			// if edge length is too short, we want to collapse it
			bool bTriedCollapse = false;
			if ( bCanCollapse ) {

                int iKeep = b, iCollapse = a;
                Vector3d vNewPos = (vA + vB) * 0.5;

                // if either vtx is fixed, collapse to that position
                if ( collapse_to == b ) {
                    vNewPos = vB;
                } else if ( collapse_to == a ) {
                    iKeep = a; iCollapse = b;
                    vNewPos = vA;
                } else
                    vNewPos = get_projected_collapse_position(iKeep, vNewPos);

                // a collapse must not create a flat triangle (eg two vertices of a straight feature curve joined
                // over a third one on it)
                if (MinTriangleQuality > 0 && collapse_creates_flat(a, b, ref vNewPos, t0, t1))
                    goto abort_collapse;

                // if new position would flip normal of one of the existing triangles
                // either one-ring, don't allow it
                if (PreventNormalFlips || MinTriangleQuality > 0) {
                    if (collapse_creates_flip_or_invalid(a, b, ref vNewPos, t0, t1) || collapse_creates_flip_or_invalid(b, a, ref vNewPos, t0, t1)) {
                        goto abort_collapse;
                    }
                }

                // TODO be smart about picking b (keep vtx). 
                //    - swap if one is bdry vtx, for example?
                // lots of cases where we cannot collapse, but we should just let
                // mesh sort that out, right?
                COUNT_COLLAPSES++;
				DMesh3.EdgeCollapseInfo collapseInfo;
				MeshResult result = mesh.CollapseEdge(iKeep, iCollapse, out collapseInfo);
				if ( result == MeshResult.Ok ) {
					mesh.SetVertex(iKeep, vNewPos);
                    if (constraints != null) {
                        constraints.ClearEdgeConstraint(edgeID);
                        constraints.ClearEdgeConstraint(collapseInfo.eRemoved0);
                        if ( collapseInfo.eRemoved1 != DMesh3.InvalidID )
                            constraints.ClearEdgeConstraint(collapseInfo.eRemoved1);
                        constraints.ClearVertexConstraint(iCollapse);
                    }
                    OnEdgeCollapse(edgeID, iKeep, iCollapse, collapseInfo);
                    DoDebugChecks();

					return ProcessResult.Ok_Collapsed;
				} else 
					bTriedCollapse = true;
			}
            abort_collapse:

            end_collapse();
            begin_flip();

			// if this is not a boundary edge, maybe we want to flip
			bool bTriedFlip = false;
			if ( EnableFlips && constraint.CanFlip && bIsBoundaryEdge == false ) {

                // can we do this more efficiently somehow?
                bool a_is_boundary_vtx = (MeshIsClosed) ? false : (bIsBoundaryEdge || mesh.IsBoundaryVertex(a));
				bool b_is_boundary_vtx = (MeshIsClosed) ? false : (bIsBoundaryEdge || mesh.IsBoundaryVertex(b));
				bool c_is_boundary_vtx = (MeshIsClosed) ? false : mesh.IsBoundaryVertex(c);
				bool d_is_boundary_vtx = (MeshIsClosed) ? false :  mesh.IsBoundaryVertex(d);
				int valence_a = mesh.GetVtxEdgeCount(a), valence_b = mesh.GetVtxEdgeCount(b);
				int valence_c = mesh.GetVtxEdgeCount(c), valence_d = mesh.GetVtxEdgeCount(d);
				int valence_a_target = (a_is_boundary_vtx) ? valence_a : 6;
				int valence_b_target = (b_is_boundary_vtx) ? valence_b : 6;
				int valence_c_target = (c_is_boundary_vtx) ? valence_c : 6;
				int valence_d_target = (d_is_boundary_vtx) ? valence_d : 6;


				// if total valence error improves by flip, we want to do it
				int curr_err = Math.Abs(valence_a-valence_a_target) + Math.Abs(valence_b-valence_b_target)
				                   + Math.Abs(valence_c-valence_c_target) + Math.Abs(valence_d-valence_d_target);
				int flip_err = Math.Abs((valence_a-1)-valence_a_target) + Math.Abs((valence_b-1)-valence_b_target)
				                   + Math.Abs((valence_c+1)-valence_c_target) + Math.Abs((valence_d+1)-valence_d_target);

                bool bTryFlip = flip_err < curr_err;
                if (bTryFlip && (PreventNormalFlips || MinTriangleQuality > 0) && flip_inverts_normals(a, b, c, d, t0))
                    bTryFlip = false;
                // not for the valence if it creates a degenerate triangle (it would undo the flip below)
                if (bTryFlip && flip_creates_degenerate(ref vA, ref vB, c, d))
                    bTryFlip = false;
                // a degenerate triangle on this edge (eg c or d slid onto it along a constrained curve, where
                // no edge is short enough to collapse): flip it away if that gives two proper triangles
                if (bTryFlip == false && flip_removes_degenerate(ref vA, ref vB, c, d))
                    bTryFlip = true;
                // a poor triangle on this edge (eg a cap with its obtuse corner opposite it)
                if (bTryFlip == false && MinTriangleQuality > 0 && flip_improves_poor(ref vA, ref vB, c, d))
                    bTryFlip = true;

                if (bTryFlip) {
                    DMesh3.EdgeFlipInfo flipInfo;
                    COUNT_FLIPS++;
					MeshResult result = mesh.FlipEdge(edgeID, out flipInfo);
					if ( result == MeshResult.Ok ) {
                        DoDebugChecks();
						return ProcessResult.Ok_Flipped;
					} else 
						bTriedFlip = true;

				}
			}

            end_flip();
            begin_split();

			// if edge length is too long, we want to split it
			bool bTriedSplit = false;
			if ( EnableSplits && constraint.CanSplit && edge_len_sqr > MaxEdgeLength*MaxEdgeLength
                    && (!(MinTriangleQuality > 0) || has_flat_neighbour(ref vA, ref vB, c, d) == false) ) {

				DMesh3.EdgeSplitInfo splitInfo;
                COUNT_SPLITS++;
				MeshResult result = mesh.SplitEdge(edgeID, out splitInfo);
				if ( result == MeshResult.Ok ) {
                    update_after_split(edgeID, a, b, ref splitInfo);
                    OnEdgeSplit(edgeID, a, b, splitInfo);
                    DoDebugChecks();
					return ProcessResult.Ok_Split;
				} else
					bTriedSplit = true;
			}

            end_split();


			if ( bTriedFlip || bTriedSplit || bTriedCollapse )
				return ProcessResult.Failed_OpNotSuccessful;
			else
				return ProcessResult.Ignored_EdgeIsFine;
		}



        // After we split an edge, we have created a new edge and a new vertex.
        // The edge needs to inherit the constraint on the other pre-existing edge that we kept.
        // In addition, if the edge vertices were both constrained, then we /might/
        // want to also constrain this new vertex, possibly project to constraint target. 
        protected virtual void update_after_split(int edgeID, int va, int vb, ref DMesh3.EdgeSplitInfo splitInfo)
        {
            bool bPositionFixed = false;
            if (constraints != null && constraints.HasEdgeConstraint(edgeID)) {
                // inherit edge constraint
                constraints.SetOrUpdateEdgeConstraint(splitInfo.eNewBN, constraints.GetEdgeConstraint(edgeID));

                // [RMS] update vertex constraints. Note that there is some ambiguity here.
                //   Both verts being constrained doesn't inherently mean that the edge is on
                //   a constraint, that's why these checks are only applied if edge is constrained.
                //   But constrained edge doesn't necessarily mean we want to inherit vert constraints!!
                //
                //   although, pretty safe to assume that we would at least disable flips
                //   if both vertices are constrained to same line/curve. So, maybe this makes sense...
                //
                //   (perhaps edge constraint should be explicitly tagged to resolve this ambiguity??)

                // vert inherits Fixed if both orig edge verts Fixed, and both tagged with same SetID
                VertexConstraint ca = constraints.GetVertexConstraint(va);
                VertexConstraint cb = constraints.GetVertexConstraint(vb);
                if (ca.Fixed && cb.Fixed) {
                    int nSetID = (ca.FixedSetID >= 0 && ca.FixedSetID == cb.FixedSetID) ?      // set ids start at 0, as in MeshRefinerBase
                        ca.FixedSetID : VertexConstraint.InvalidSetID;
                    constraints.SetOrUpdateVertexConstraint(splitInfo.vNew,
                        new VertexConstraint(true, nSetID));
                    bPositionFixed = true;
                }

                // vert inherits Target if:
                //  1) both source verts and edge have same Target, and is same as edge target
                //  2) either vert has same target as edge, and other vert is fixed
                if ( ca.Target != null || cb.Target != null ) {
                    IProjectionTarget edge_target = constraints.GetEdgeConstraint(edgeID).Target;
                    IProjectionTarget set_target = null;
                    if (ca.Target == cb.Target && ca.Target == edge_target)
                        set_target = edge_target;
                    else if (ca.Target == edge_target && cb.Fixed)
                        set_target = edge_target;
                    else if (cb.Target == edge_target && ca.Fixed)
                        set_target = edge_target;
                    if ( set_target != null ) {
                        constraints.SetOrUpdateVertexConstraint(splitInfo.vNew,
                            new VertexConstraint(set_target));
                        project_vertex(splitInfo.vNew, set_target);
                        bPositionFixed = true;
                    }
                }
            }

            if ( EnableInlineProjection && bPositionFixed == false && target != null ) {
                project_vertex(splitInfo.vNew, target);
            }
        }


        protected virtual void project_vertex(int vID, IProjectionTarget targetIn)
        {
            Vector3d curpos = mesh.GetVertex(vID);
            Vector3d projected = targetIn.Project(curpos, vID);
            mesh.SetVertex(vID, projected);
        }

        // quality (MeshRefinerBase.TriangleQuality) below which a triangle counts as degenerate for flips
        const double DegenerateFlipQuality = 1e-6;

        // edge [a,b] with opposite vertices c and d: one of the triangles after the flip would be degenerate
        bool flip_creates_degenerate(ref Vector3d vA, ref Vector3d vB, int c, int d)
        {
            Vector3d vC = mesh.GetVertex(c), vD = mesh.GetVertex(d);
            return Math.Min(TriangleQuality(ref vC, ref vD, ref vB), TriangleQuality(ref vD, ref vC, ref vA)) < DegenerateFlipQuality;
        }

        // edge [a,b] with opposite vertices c and d: true if one of its triangles is degenerate, both
        // triangles after the flip are not, and the flip keeps the orientation (cd crosses ab)
        bool flip_removes_degenerate(ref Vector3d vA, ref Vector3d vB, int c, int d)
        {
            Vector3d vC = mesh.GetVertex(c), vD = mesh.GetVertex(d);
            double q0 = TriangleQuality(ref vA, ref vB, ref vC), q1 = TriangleQuality(ref vB, ref vA, ref vD);
            if (Math.Min(q0, q1) >= DegenerateFlipQuality)
                return false;
            double n0 = TriangleQuality(ref vC, ref vD, ref vB), n1 = TriangleQuality(ref vD, ref vC, ref vA);
            if (Math.Min(n0, n1) < 100 * DegenerateFlipQuality)
                return false;
            // the new triangles must face the same way as the non-degenerate old one
            Vector3d nOld = (q0 >= q1) ? MathUtil.Normal(ref vA, ref vB, ref vC) : MathUtil.Normal(ref vB, ref vA, ref vD);
            Vector3d nn0 = MathUtil.Normal(ref vC, ref vD, ref vB), nn1 = MathUtil.Normal(ref vD, ref vC, ref vA);
            return nOld.Dot(nn0) > 0 && nOld.Dot(nn1) > 0;
        }


        // edge [a,b] with opposite vertices c and d: one of its triangles is below MinTriangleQuality, the flip at
        // least doubles the minimum quality of the pair, and the new triangles face within 60 degrees of the better
        // old one (no flips across a ridge). Cosines from the unnormalized cross products: no absolute thresholds.
        bool flip_improves_poor(ref Vector3d vA, ref Vector3d vB, int c, int d)
        {
            Vector3d vC = mesh.GetVertex(c), vD = mesh.GetVertex(d);
            double q0 = TriangleQuality(ref vA, ref vB, ref vC), q1 = TriangleQuality(ref vB, ref vA, ref vD);
            double qmin = Math.Min(q0, q1);
            if (qmin >= MinTriangleQuality)
                return false;
            double n0 = TriangleQuality(ref vC, ref vD, ref vB), n1 = TriangleQuality(ref vD, ref vC, ref vA);
            if (!(Math.Min(n0, n1) > 2 * qmin))
                return false;
            Vector3d nOld = (q0 >= q1) ? (vB - vA).Cross(vC - vA) : (vA - vB).Cross(vD - vB);
            Vector3d nn0 = (vD - vC).Cross(vB - vC), nn1 = (vC - vD).Cross(vA - vD);
            return cos_above(ref nOld, ref nn0, 0.5) && cos_above(ref nOld, ref nn1, 0.5);
        }

        // collapsing edge [a,b] (triangles t0, t1) to newPos leaves a triangle of either one-ring flat that was not
        bool collapse_creates_flat(int a, int b, ref Vector3d newPos, int t0, int t1)
        {
            for (int k = 0; k < 2; ++k)
                foreach (int tid in mesh.VtxTrianglesItr(k == 0 ? a : b)) {
                    if (tid == t0 || tid == t1)
                        continue;
                    Index3i t = mesh.GetTriangle(tid);
                    Vector3d pa = mesh.GetVertex(t.a), pb = mesh.GetVertex(t.b), pc = mesh.GetVertex(t.c);
                    if (TriangleQuality(ref pa, ref pb, ref pc) < DegenerateFlipQuality)
                        continue;       // already flat
                    if (t.a == a || t.a == b) pa = newPos;
                    if (t.b == a || t.b == b) pb = newPos;
                    if (t.c == a || t.c == b) pc = newPos;
                    if (TriangleQuality(ref pa, ref pb, ref pc) < DegenerateFlipQuality)
                        return true;
                }
            return false;
        }

        // the cosine of the angle between the (unnormalized) vectors is above c
        static bool cos_above(ref Vector3d u, ref Vector3d v, double c)
        {
            double dot = u.Dot(v);
            return dot > 0 && dot * dot > c * c * u.LengthSquared * v.LengthSquared;
        }

        // edge [a,b] with opposite vertices c and d (d may be invalid): a neighbouring triangle is flat
        bool has_flat_neighbour(ref Vector3d vA, ref Vector3d vB, int c, int d)
        {
            Vector3d vC = mesh.GetVertex(c);
            if (TriangleQuality(ref vA, ref vB, ref vC) < DegenerateFlipQuality)
                return true;
            if (d == DMesh3.InvalidID)
                return false;
            Vector3d vD = mesh.GetVertex(d);
            return TriangleQuality(ref vB, ref vA, ref vD) < DegenerateFlipQuality;
        }

        /// <summary>
        /// True if moving vertex vID to newPos flips one of its (not flat) triangles, or leaves one below
        /// MinTriangleQuality and below the worst quality around vID before the move.
        /// </summary>
        protected bool move_degrades(int vID, Vector3d newPos, bool flipsOnly = false)
        {
            double qOld = double.MaxValue, qNew = double.MaxValue;
            Vector3d ring = Vector3d.Zero;      // the summed normals of the triangles that are not flat
            bool anyFlat = false;
            foreach (int tid in mesh.VtxTrianglesItr(vID)) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
                Vector3d n0 = (b - a).Cross(c - a);
                double q0 = TriangleQuality(ref a, ref b, ref c);
                qOld = Math.Min(qOld, q0);
                if (t.a == vID) a = newPos; else if (t.b == vID) b = newPos; else c = newPos;
                Vector3d n1 = (b - a).Cross(c - a);
                if (q0 >= DegenerateFlipQuality) {
                    if (!(n0.Dot(n1) > 0))
                        return true;
                    ring += n0;
                } else
                    anyFlat = true;
                qNew = Math.Min(qNew, TriangleQuality(ref a, ref b, ref c));
            }
            // a flat triangle has no orientation to keep (the move may be the one that repairs it), but if it does not
            // stay flat it must face the way its neighbours around vID did, not fold over
            if (anyFlat && folds_flat_triangle(vID, newPos, ref ring))
                return true;
            return flipsOnly == false && qNew < MinTriangleQuality && qNew < qOld;
        }

        // moving vID to newPos turns one of its flat triangles into one facing against ring
        bool folds_flat_triangle(int vID, Vector3d newPos, ref Vector3d ring)
        {
            foreach (int tid in mesh.VtxTrianglesItr(vID)) {
                Index3i t = mesh.GetTriangle(tid);
                Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
                if (TriangleQuality(ref a, ref b, ref c) >= DegenerateFlipQuality)
                    continue;
                if (t.a == vID) a = newPos; else if (t.b == vID) b = newPos; else c = newPos;
                if (TriangleQuality(ref a, ref b, ref c) < DegenerateFlipQuality)
                    continue;       // still flat: not worse
                if (!((b - a).Cross(c - a).Dot(ring) > 0))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// MinTriangleQuality: the projection of vertex vID from cur towards projected, or the largest of the half,
        /// quarter and eighth of that step that flips none of its triangles (the next pass gets closer); cur if none
        /// does (then OnVertexMoveRejected).
        /// </summary>
        protected Vector3d guarded_projection(int vID, Vector3d cur, Vector3d projected)
        {
            double t = 1;
            for (int k = 0; k < 4; ++k, t *= 0.5) {
                Vector3d p = (k == 0) ? projected : Vector3d.Lerp(cur, projected, t);
                if (move_degrades(vID, p, true) == false)
                    return p;
            }
            OnVertexMoveRejected(vID);
            return cur;
        }

        /// <summary> called (possibly in parallel) when MinTriangleQuality rejects a smoothing or projection move of vID </summary>
        protected virtual void OnVertexMoveRejected(int vID)
        {
        }

        // used by collapse-edge to get projected position for new vertex
        protected virtual Vector3d get_projected_collapse_position(int vid, Vector3d vNewPos)
        {
            if (constraints != null) {
                VertexConstraint vc = constraints.GetVertexConstraint(vid);
                if (vc.Target != null) 
                    return vc.Target.Project(vNewPos, vid);
                if (vc.Fixed)
                    return vNewPos;
            }
            // no constraint applied, so if we have a target surface, project to that
            if ( EnableInlineProjection && target != null ) {
                if (VertexControlF == null || (VertexControlF(vid) & VertexControl.NoProject) == 0)
                    return target.Project(vNewPos, vid);
            }
            return vNewPos;
        }







		protected virtual void FullSmoothPass_InPlace(bool bParallel) {
            Func<DMesh3, int, double, Vector3d> smoothFunc = MeshUtil.UniformSmooth;
            if (CustomSmoothF != null) {
                smoothFunc = CustomSmoothF;
            } else {
                if (SmoothType == SmoothTypes.MeanValue)
                    smoothFunc = MeshUtil.MeanValueSmooth;
                else if (SmoothType == SmoothTypes.Cotan)
                    smoothFunc = MeshUtil.CotanSmooth;
            }

            Action<int> smooth = (vID) => {
                bool bModified = false;
                Vector3d vSmoothed = ComputeSmoothedVertexPos(vID, smoothFunc, out bModified);
                if ( bModified )
                    mesh.SetVertex(vID, vSmoothed);
            };

            if (bParallel) {
                gParallel.ForEach<int>(smooth_vertices(), smooth);
            } else {
                foreach ( int vID in smooth_vertices() )
                    smooth(vID);
            }
		}




        protected virtual void FullSmoothPass_Buffer(bool bParallel)
        {
            InitializeVertexBufferForPass();

            Func<DMesh3, int, double, Vector3d> smoothFunc = MeshUtil.UniformSmooth;
            if (CustomSmoothF != null) {
                smoothFunc = CustomSmoothF;
            } else {
                if (SmoothType == SmoothTypes.MeanValue)
                    smoothFunc = MeshUtil.MeanValueSmooth;
                else if (SmoothType == SmoothTypes.Cotan)
                    smoothFunc = MeshUtil.CotanSmooth;
            }

            Action<int> smooth = (vID) => {
                bool bModified = false;
                Vector3d vSmoothed = ComputeSmoothedVertexPos(vID, smoothFunc, out bModified);
                if (bModified) {
                    vModifiedV[vID] = true;
                    vBufferV[vID] = vSmoothed;
                }
            };

            if (bParallel) {
                gParallel.ForEach<int>(smooth_vertices(), smooth);
            } else {
                foreach (int vID in smooth_vertices())
                    smooth(vID);
            }

            ApplyVertexBuffer(bParallel);
        }



        protected DVector<Vector3d> vBufferV = new DVector<Vector3d>();
        // bool[], not BitArray: the parallel passes set flags of different vertices concurrently, and a
        // BitArray packs 32 flags into one int, so those writes race (read-modify-write) and lose flags
        protected bool[] vModifiedV = new bool[4096];

        // MinTriangleQuality: each buffered move was checked against the old positions of its neighbours, but neighbours
        // move together: revert the moves of every (not flat) triangle that the buffered positions flip, until none
        // does. In triangle id order, so the result does not depend on the thread timing.
        void revert_flipping_moves()
        {
            bool changed = true;
            while (changed) {
                changed = false;
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    if (!(vModifiedV[t.a] || vModifiedV[t.b] || vModifiedV[t.c]))
                        continue;
                    Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
                    Vector3d na = vModifiedV[t.a] ? vBufferV[t.a] : a, nb = vModifiedV[t.b] ? vBufferV[t.b] : b, nc = vModifiedV[t.c] ? vBufferV[t.c] : c;
                    Vector3d n0 = (b - a).Cross(c - a);
                    if (TriangleQuality(ref a, ref b, ref c) < DegenerateFlipQuality) {
                        // flat before: if it does not stay flat, it must face the way its (not flat) neighbours did
                        if (TriangleQuality(ref na, ref nb, ref nc) < DegenerateFlipQuality)
                            continue;
                        n0 = Vector3d.Zero;
                        Index3i nbrs = mesh.GetTriNeighbourTris(tid);
                        for (int k = 0; k < 3; ++k) {
                            if (nbrs[k] == DMesh3.InvalidID) continue;
                            Vector3d p = Vector3d.Zero, q = Vector3d.Zero, r = Vector3d.Zero;
                            mesh.GetTriVertices(nbrs[k], ref p, ref q, ref r);
                            if (TriangleQuality(ref p, ref q, ref r) >= DegenerateFlipQuality)
                                n0 += (q - p).Cross(r - p);
                        }
                    }
                    if ((nb - na).Cross(nc - na).Dot(n0) > 0)
                        continue;
                    foreach (int v in new[] { t.a, t.b, t.c })
                        if (vModifiedV[v]) {
                            vModifiedV[v] = false;
                            OnVertexMoveRejected(v);
                        }
                    changed = true;
                }
            }
        }

        protected virtual void InitializeVertexBufferForPass()
        {
            if (vBufferV.size < mesh.MaxVertexID)
                vBufferV.resize(mesh.MaxVertexID + mesh.MaxVertexID / 5);
            if (vModifiedV.Length < mesh.MaxVertexID) {
                vModifiedV = new bool[2 * mesh.MaxVertexID];
            } else {
                Array.Clear(vModifiedV, 0, vModifiedV.Length);
            }
        }

        protected virtual void ApplyVertexBuffer(bool bParallel)
        {
            if (MinTriangleQuality > 0)
                revert_flipping_moves();
            // [TODO] can probably use block-parallel here...
            if (bParallel) {
                gParallel.BlockStartEnd(0, mesh.MaxVertexID-1, (a,b) => {
                    for (int vid = a; vid <= b; vid++) {
                        if (vModifiedV[vid])
                            Mesh.SetVertex(vid, vBufferV[vid]);
                    }
                });
            } else {
                foreach (int vid in mesh.VertexIndices()) {
                    if (vModifiedV[vid])
                        Mesh.SetVertex(vid, vBufferV[vid]);
                }
            }
        }




        /// <summary>
        /// This computes smoothed positions w/ proper constraints/etc.
        /// Does not modify mesh.
        /// </summary>
        protected virtual Vector3d ComputeSmoothedVertexPos(int vID, Func<DMesh3, int, double, Vector3d> smoothFunc, out bool bModified)
        {
            bModified = false;
            VertexConstraint vConstraint = VertexConstraint.Unconstrained;
            get_vertex_constraint(vID, ref vConstraint);
            if (vConstraint.Fixed)
                return Mesh.GetVertex(vID);
            VertexControl vControl = (VertexControlF == null) ? VertexControl.AllowAll : VertexControlF(vID);
            if ( (vControl & VertexControl.NoSmooth) != 0 )
                return Mesh.GetVertex(vID);

            Vector3d vSmoothed;
            int nCurve = 0;
            Vector3d curveSum = Vector3d.Zero;
            if (MinTriangleQuality > 0 && vConstraint.Target != null && constraints != null) {
                // along the curve: towards the neighbours across the edges constrained to the same target
                foreach (int eid in mesh.VtxEdgesItr(vID)) {
                    if (constraints.GetEdgeConstraint(eid).Target != vConstraint.Target)
                        continue;
                    Index2i ev = mesh.GetEdgeV(eid);
                    curveSum += mesh.GetVertex(ev.a == vID ? ev.b : ev.a);
                    nCurve++;
                }
            }
            if (nCurve == 2)
                vSmoothed = Vector3d.Lerp(Mesh.GetVertex(vID), 0.5 * curveSum, SmoothSpeedT);
            else if (nCurve > 2 || (nCurve == 1 && MinTriangleQuality > 0))
                return Mesh.GetVertex(vID);         // a junction or the end of a curve
            else
                vSmoothed = smoothFunc(mesh, vID, SmoothSpeedT);
            Debug.Assert(vSmoothed.IsFinite);     // this will really catch a lot of bugs...

            // project onto either vtx constraint target, or surface target
            if (vConstraint.Target != null) {
                vSmoothed = vConstraint.Target.Project(vSmoothed, vID);
            } else if (EnableInlineProjection && target != null) {
                if ( (vControl & VertexControl.NoProject) == 0)
                    vSmoothed = target.Project(vSmoothed, vID);
            }

            if (MinTriangleQuality > 0 && move_degrades(vID, vSmoothed)) {
                OnVertexMoveRejected(vID);
                return Mesh.GetVertex(vID);
            }
            bModified = true;
            return vSmoothed;
        }





        // Project vertices onto projection target. 
        // We can do projection in parallel if we have .net 
        protected virtual void FullProjectionPass()
        {
            if (MinTriangleQuality > 0) {
                // each move is checked against the positions before the pass: buffered, so the result does not
                // depend on the order (or thread timing) in which the vertices are projected
                InitializeVertexBufferForPass();
                Action<int> projectBuffered = (vID) => {
                    if (vertex_is_constrained(vID))
                        return;
                    if (VertexControlF != null && (VertexControlF(vID) & VertexControl.NoProject) != 0)
                        return;
                    Vector3d cur = mesh.GetVertex(vID);
                    Vector3d projected = guarded_projection(vID, cur, target.Project(cur, vID));
                    if (projected == cur)
                        return;
                    vModifiedV[vID] = true;
                    vBufferV[vID] = projected;
                };
                if (EnableParallelProjection)
                    gParallel.ForEach<int>(project_vertices(), projectBuffered);
                else
                    foreach (int vid in project_vertices())
                        projectBuffered(vid);
                ApplyVertexBuffer(EnableParallelProjection);
                return;
            }
            Action<int> project = (vID) => {
                if (vertex_is_constrained(vID))
                    return;
                if (VertexControlF != null && (VertexControlF(vID) & VertexControl.NoProject) != 0)
                    return;
                Vector3d curpos = mesh.GetVertex(vID);
                Vector3d projected = target.Project(curpos, vID);
                mesh.SetVertex(vID, projected);
            };

            if (EnableParallelProjection) {
                gParallel.ForEach<int>(project_vertices(), project);
            } else {
                foreach (int vid in project_vertices())
                    project(vid);
            }
        }


        [Conditional("DEBUG")] 
        void RuntimeDebugCheck(int eid)
        {
            if (DebugEdges.Contains(eid))
                System.Diagnostics.Debugger.Break();
        }


        public bool ENABLE_DEBUG_CHECKS = false;
        protected virtual void DoDebugChecks()
        {
            if (ENABLE_DEBUG_CHECKS == false)
                return;

            DebugCheckVertexConstraints();

            // [RMS] keeping this for now, is useful in testing that we are preserving group boundaries
            //foreach ( int eid in mesh.EdgeIndices() ) {
            //    if (mesh.IsGroupBoundaryEdge(eid))
            //        if (constraints.GetEdgeConstraint(eid).CanFlip) {
            //            Util.gBreakToDebugger();
            //            throw new Exception("fuck");
            //        }
            //}
            //foreach ( int vid in mesh.VertexIndices() ) {
            //    if (mesh.IsGroupBoundaryVertex(vid))
            //        if (constraints.GetVertexConstraint(vid).Target == null)
            //            Util.gBreakToDebugger();
            //}
        }

        void DebugCheckVertexConstraints()
        {
            if (constraints == null)
                return;

            foreach ( KeyValuePair<int,VertexConstraint> vc in constraints.VertexConstraintsItr() ) {
                int vid = vc.Key;
                if (vc.Value.Target != null) {
                    Vector3d curpos = mesh.GetVertex(vid);
                    Vector3d projected = vc.Value.Target.Project(curpos, vid);
                    if (curpos.DistanceSquared(projected) > 0.0001f)
                        Util.gBreakToDebugger();
                }
            }
        }




        //
        // profiling functions, turn on ENABLE_PROFILING to see output in console
        // 
        int COUNT_SPLITS, COUNT_COLLAPSES, COUNT_FLIPS;
        Stopwatch AllOpsW, SmoothW, ProjectW, FlipW, SplitW, CollapseW;

        protected virtual void begin_pass() {
            if ( ENABLE_PROFILING ) {
                COUNT_SPLITS = COUNT_COLLAPSES = COUNT_FLIPS = 0;
                AllOpsW = new Stopwatch();
                SmoothW = new Stopwatch();
                ProjectW = new Stopwatch();
                FlipW = new Stopwatch();
                SplitW = new Stopwatch();
                CollapseW = new Stopwatch();
            }
        }

        protected virtual void end_pass() {
            if ( ENABLE_PROFILING ) {
                System.Console.WriteLine(string.Format(
                    "RemeshPass: T {0} V {1} splits {2} flips {3} collapses {4}", mesh.TriangleCount, mesh.VertexCount, COUNT_SPLITS, COUNT_FLIPS, COUNT_COLLAPSES
                    ));
                System.Console.WriteLine(string.Format(
                    "           Timing1:  ops {0} smooth {1} project {2}", Util.ToSecMilli(AllOpsW.Elapsed), Util.ToSecMilli(SmoothW.Elapsed), Util.ToSecMilli(ProjectW.Elapsed)
                    ));
                System.Console.WriteLine(string.Format(
                    "           Timing2:  collapse {0} flip {1} split {2}", Util.ToSecMilli(CollapseW.Elapsed), Util.ToSecMilli(FlipW.Elapsed), Util.ToSecMilli(SplitW.Elapsed)
                    ));
            }
        }

        protected virtual void begin_ops() {
            if ( ENABLE_PROFILING ) AllOpsW.Start();
        }
        protected virtual void end_ops() {
            if ( ENABLE_PROFILING ) AllOpsW.Stop();
        }
        protected virtual void begin_smooth() {
            if ( ENABLE_PROFILING ) SmoothW.Start();
        }
        protected virtual void end_smooth() {
            if ( ENABLE_PROFILING ) SmoothW.Stop();
        }
        protected virtual void begin_project() {
            if ( ENABLE_PROFILING ) ProjectW.Start();
        }
        protected virtual void end_project() {
            if ( ENABLE_PROFILING ) ProjectW.Stop();
        }

        protected virtual void begin_collapse() {
            if ( ENABLE_PROFILING ) CollapseW.Start();
        }
        protected virtual void end_collapse() {
            if ( ENABLE_PROFILING ) CollapseW.Stop();
        }
        protected virtual void begin_flip() {
            if ( ENABLE_PROFILING ) FlipW.Start();
        }
        protected virtual void end_flip() {
            if ( ENABLE_PROFILING ) FlipW.Stop();
        }
        protected virtual void begin_split() {
            if ( ENABLE_PROFILING ) SplitW.Start();
        }
        protected virtual void end_split() {
            if ( ENABLE_PROFILING ) SplitW.Stop();
        }

	}







}
