// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;

namespace gs
{
	/// <summary>
	/// Repairs triangle orientation in place. OrientComponents() makes the orientation consistent within
	/// each edge-connected component (patches of consistent triangles flipped along a spanning tree, see there;
	/// KeepShortSeams = false: flood fill from an arbitrary triangle, reversing neighbours that disagree, as
	/// geometry3Sharp 1.0.324); SolveGlobalOrientation() then reverses whole components that face inwards, judged by ray
	/// casts from far outside (the side of the triangles that is visible from outside should be the front).
	///
	/// Call OrientComponents() first: SolveGlobalOrientation() works on the components it found.
	/// Non-orientable components (eg Moebius strips) keep a seam of inconsistent orientation. The global step
	/// is a heuristic for open or nested surfaces; for closed meshes MeshPostRepairCleanup.FindInvertedComponents
	/// (signed volume and nesting) is an alternative. Used by MeshAutoRepair.
	/// </summary>
	public class MeshRepairOrientation
	{
		/// <summary> the mesh whose triangles are reoriented in place </summary>
		public DMesh3 Mesh;

		DMeshAABBTree3 spatial;
		protected DMeshAABBTree3 Spatial {
			get {
				if (spatial == null)
					spatial = new DMeshAABBTree3(Mesh, true);
				return spatial; 
			}
		}

		/// <summary>
		/// OrientComponents keeps consistently oriented patches together and leaves a short seam in non-orientable
		/// components (default). false: the depth-first propagation of geometry3Sharp 1.0.324.
		/// </summary>
		public bool KeepShortSeams = true;

		/// <summary> spatial is an optional prebuilt AABB tree of mesh3 (built on demand otherwise) </summary>
		public MeshRepairOrientation(DMesh3 mesh3, DMeshAABBTree3 spatial = null)
		{
			Mesh = mesh3;
			this.spatial = spatial;
		}


		class Component
		{
			public List<int> triangles;
			public double outFacing;
			public double inFacing;
		}
		List<Component> Components = new List<Component>();




		// TODO:
		//  - (in merge coincident) don't merge tris with same/opposite normals (option)
		//  - after orienting components, try to find adjacent open components and
		//    transfer orientation between them
		//  - orient via nesting


		/// <summary>
		/// Make the orientation consistent within each connected component (triangles connected by edges).
		/// Patches of triangles that are already consistently oriented with each other are kept together
		/// and flipped as a whole: along a maximum spanning tree of the patch adjacency (weight = number of
		/// shared edges), each patch is oriented against its parent, starting from the largest patch, which
		/// keeps its orientation. An orientable component ends up consistent. A non-orientable one (or one
		/// whose patches cannot be 2-coloured) keeps the inconsistent edges between patches off the tree,
		/// which are few, where a depth-first propagation from a single triangle could leave a long seam
		/// anywhere in it. Whether that is the outward orientation is decided by SolveGlobalOrientation.
		/// With KeepShortSeams = false, the depth-first propagation of geometry3Sharp 1.0.324 is used instead.
		/// </summary>
		public void OrientComponents()
		{
			if (KeepShortSeams == false) {
				orient_depth_first();
				return;
			}
			Components = new List<Component>();

			// patches: union over consistently oriented interior edges
			int maxT = Mesh.MaxTriangleID;
			int[] patch = new int[maxT];
			for (int i = 0; i < maxT; ++i)
				patch[i] = i;
			var seamEdges = new List<int>();
			foreach (int eid in Mesh.EdgeIndices()) {
				Index2i et = Mesh.GetEdgeT(eid);
				if (et.b == DMesh3.InvalidID)
					continue;
				if (is_consistent(eid, et))
					union(patch, et.a, et.b);
				else
					seamEdges.Add(eid);
			}

			// patch adjacency (all edges between two different patches are inconsistent), heaviest first
			var weight = new Dictionary<long, int>(MixedKeyComparer.Instance);
			foreach (int eid in seamEdges) {
				Index2i et = Mesh.GetEdgeT(eid);
				int pa = find(patch, et.a), pb = find(patch, et.b);
				if (pa == pb)
					continue;
				long key = ((long)Math.Min(pa, pb) << 32) | (uint)Math.Max(pa, pb);
				int w;
				weight.TryGetValue(key, out w);
				weight[key] = w + 1;
			}
			var pairs = weight.ToList();
			pairs.Sort((x, y) => (x.Value != y.Value) ? y.Value.CompareTo(x.Value) : x.Key.CompareTo(y.Key));

			// maximum spanning forest of the patches (Kruskal)
			int[] tree = new int[maxT];
			for (int i = 0; i < maxT; ++i)
				tree[i] = i;
			var adj = new Dictionary<int, List<int>>();
			foreach (var kv in pairs) {
				int pa = (int)(kv.Key >> 32), pb = (int)(kv.Key & 0xffffffff);
				if (find(tree, pa) == find(tree, pb))
					continue;
				union(tree, pa, pb);
				add_adj(adj, pa, pb);
				add_adj(adj, pb, pa);
			}

			// triangles and area per patch
			var patchTris = new Dictionary<int, List<int>>();
			var patchArea = new Dictionary<int, double>();
			foreach (int tid in Mesh.TriangleIndices()) {
				int p = find(patch, tid);
				List<int> list;
				if (patchTris.TryGetValue(p, out list) == false) {
					patchTris[p] = list = new List<int>();
					patchArea[p] = 0;
				}
				list.Add(tid);
				patchArea[p] += Mesh.GetTriArea(tid);
			}

			// per tree (= connected component): start at its largest patch, flip every other level
			var byTree = new Dictionary<int, List<int>>();
			foreach (int p in patchTris.Keys) {
				int r = find(tree, p);
				List<int> list;
				if (byTree.TryGetValue(r, out list) == false)
					byTree[r] = list = new List<int>();
				list.Add(p);
			}
			var flip = new Dictionary<int, bool>();
			var queue = new Queue<int>();
			foreach (List<int> ps in byTree.Values) {
				int root = ps[0];
				foreach (int p in ps)
					if (patchArea[p] > patchArea[root] || (patchArea[p] == patchArea[root] && patchTris[p].Count > patchTris[root].Count))
						root = p;
				flip[root] = false;
				queue.Enqueue(root);
				Component c = new Component() { triangles = new List<int>() };
				while (queue.Count > 0) {
					int p = queue.Dequeue();
					bool f = flip[p];
					foreach (int tid in patchTris[p]) {
						if (f)
							Mesh.ReverseTriOrientation(tid);
						c.triangles.Add(tid);
					}
					List<int> nbrs;
					if (adj.TryGetValue(p, out nbrs) == false)
						continue;
					foreach (int q in nbrs) {
						if (flip.ContainsKey(q))
							continue;
						flip[q] = !f;
						queue.Enqueue(q);
					}
				}
				Components.Add(c);
			}

			smooth_seams();
		}

		/// <summary>
		/// A triangle with two or three inconsistent edges is flipped (fewer inconsistent edges), until
		/// there is none: straightens jagged seams.
		/// </summary>
		void smooth_seams()
		{
			var queue = new Queue<int>(Mesh.TriangleIndices());
			int maxFlips = 10 * Mesh.TriangleCount;
			while (queue.Count > 0 && maxFlips > 0) {
				int tid = queue.Dequeue();
				if (Mesh.IsTriangle(tid) == false)
					continue;
				Index3i te = Mesh.GetTriEdges(tid);
				int bad = 0;
				for (int j = 0; j < 3; ++j) {
					Index2i et = Mesh.GetEdgeT(te[j]);
					if (et.b != DMesh3.InvalidID && is_consistent(te[j], et) == false)
						bad++;
				}
				if (bad < 2)
					continue;
				Mesh.ReverseTriOrientation(tid);
				maxFlips--;
				Index3i nbrs = Mesh.GetTriNeighbourTris(tid);
				for (int j = 0; j < 3; ++j)
					if (nbrs[j] != DMesh3.InvalidID)
						queue.Enqueue(nbrs[j]);
			}
		}

		// geometry3Sharp 1.0.324: each triangle oriented like the one it is reached from, depth-first
		void orient_depth_first()
		{
			Components = new List<Component>();

			HashSet<int> remaining = new HashSet<int>(Mesh.TriangleIndices());
			List<int> stack = new List<int>();
			while (remaining.Count > 0) {
				Component c = new Component();
				c.triangles = new List<int>();

				stack.Clear();
				int start = remaining.First();
				remaining.Remove(start);
				c.triangles.Add(start);
				stack.Add(start);
				while (stack.Count > 0) {
					int cur = stack[stack.Count - 1];
					stack.RemoveAt(stack.Count - 1);
					Index3i tcur = Mesh.GetTriangle(cur);

					Index3i nbrs = Mesh.GetTriNeighbourTris(cur);
					for (int j = 0; j < 3; ++j) {
						int nbr = nbrs[j];
						if (remaining.Contains(nbr) == false)
							continue;

						int a = tcur[j];
						int b = tcur[(j+1)%3];

						Index3i tnbr = Mesh.GetTriangle(nbr);
						if (IndexUtil.find_tri_ordered_edge(b, a, ref tnbr) == DMesh3.InvalidID) {
							Mesh.ReverseTriOrientation(nbr);
						}
						stack.Add(nbr);
						remaining.Remove(nbr);
						c.triangles.Add(nbr);
					}

				}

				Components.Add(c);
			}
		}

		bool is_consistent(int eid, Index2i et)
		{
			Index2i ev = Mesh.GetEdgeV(eid);
			Index3i ta = Mesh.GetTriangle(et.a), tb = Mesh.GetTriangle(et.b);
			return (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref ta) != DMesh3.InvalidID)
				!= (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref tb) != DMesh3.InvalidID);
		}

		// patch ids of neighbours are close, and long.GetHashCode (low ^ high word) maps such pairs to few buckets
		class MixedKeyComparer : IEqualityComparer<long>
		{
			public static readonly MixedKeyComparer Instance = new MixedKeyComparer();
			public bool Equals(long x, long y) { return x == y; }
			public int GetHashCode(long k) { return (int)(((ulong)k * 0x9E3779B97F4A7C15UL) >> 32); }
		}

		static int find(int[] parent, int x)
		{
			while (parent[x] != x) {
				parent[x] = parent[parent[x]];
				x = parent[x];
			}
			return x;
		}
		static void union(int[] parent, int a, int b)
		{
			int ra = find(parent, a), rb = find(parent, b);
			if (ra != rb)
				parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
		}
		static void add_adj(Dictionary<int, List<int>> adj, int a, int b)
		{
			List<int> list;
			if (adj.TryGetValue(a, out list) == false)
				adj[a] = list = new List<int>();
			list.Add(b);
		}





		/// <summary>
		/// For each component found by OrientComponents(), sum the area of triangles hit from the front and
		/// from the back by rays from far outside (used by SolveGlobalOrientation).
		/// </summary>
		public void ComputeStatistics()
		{
			var s = this.Spatial;  // make sure this exists
            // Cannot do in parallel because we set a filter on spatial DS. 
            // Also we are doing rays in parallel anyway...
            foreach ( var c in Components ) {
                compute_statistics(c);
            }
		}
		void compute_statistics(Component c)
		{
			int NC = c.triangles.Count;
			c.inFacing = c.outFacing = 0;
            double dist = 2 * Mesh.CachedBounds.DiagonalLength;

            // only want to raycast triangles in this 
            HashSet<int> tris = new HashSet<int>(c.triangles);
            spatial.TriangleFilterF = tris.Contains;

            // We want to try to figure out what is 'outside' relative to the world.
            // Assumption is that faces we can hit from far away should be oriented outwards.
            // So, for each triangle we construct far-away points in positive and negative normal
            // direction, then raycast back towards the triangle. If we hit the triangle from
            // one side and not the other, that is evidence we should keep/reverse that triangle.
            // If it is not hit, or hit from both, that does not provide any evidence.
            // We collect up this keep/reverse evidence and use the larger to decide on the global orientation.

            SpinLock count_lock = new SpinLock();

			gParallel.BlockStartEnd(0, NC - 1, (a, b) => {
				for (int i = a; i <= b; ++i) {
					int ti = c.triangles[i];
					Vector3d normal, centroid; double area;
					Mesh.GetTriInfo(ti, out normal, out area, out centroid);
					if (area < MathUtil.ZeroTolerancef)
						continue;

                    // construct far away points
                    Vector3d pos_pt = centroid + dist * normal;
                    Vector3d neg_pt = centroid - dist * normal;

                    // raycast towards triangle from far-away point
                    int hit_pos = spatial.FindNearestHitTriangle(new Ray3d(pos_pt, -normal));
                    int hit_neg = spatial.FindNearestHitTriangle(new Ray3d(neg_pt, normal));
                    if (hit_pos != ti && hit_neg != ti)
                        continue;       // no evidence
                    if (hit_pos == ti && hit_neg == ti)
                        continue;       // no evidence (?)

                    bool taken = false;
                    count_lock.Enter(ref taken);

                    if (hit_neg == ti)
                        c.inFacing += area;
                    else if (hit_pos == ti)
                        c.outFacing += area;

                    count_lock.Exit();
				}
			});

            spatial.TriangleFilterF = null;
        }



		/// <summary>
		/// Reverse each component (from OrientComponents) that is hit from outside on its back side more than
		/// on its front side (area weighted). Does nothing if OrientComponents() was not called.
		/// </summary>
		public void SolveGlobalOrientation()
		{
			ComputeStatistics();
			MeshEditor editor = new MeshEditor(Mesh);
			foreach (Component c in Components) {
				if (c.inFacing > c.outFacing) {
					editor.ReverseTriangles(c.triangles);
				}
			}
		}



	}
}
