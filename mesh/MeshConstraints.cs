using System;
using System.Collections.Generic;
using System.Linq;

namespace g3
{

    /// <summary> which refinement operations are forbidden on an edge (see <see cref="EdgeConstraint"/>) </summary>
    [Flags]
    public enum EdgeRefineFlags
    {
        NoConstraint = 0,
        NoFlip = 1,
        NoSplit = 2,
        NoCollapse = 4,
		FullyConstrained = NoFlip | NoSplit | NoCollapse,

		PreserveTopology = 8,	// this flag just means we want to avoid 'losing' this edge
								// when we collapse a neighbour (but allow the edge itself to be collapsed).
								// Eg when constraining edges for Reduce
								// [currently not checked by Remesher or Reducer]
    }


    /// <summary>
    /// Constraint on one edge for <see cref="Remesher"/> and <see cref="Reducer"/>: forbidden operations
    /// (<see cref="EdgeRefineFlags"/>), an optional projection target (informational only) and a tracking id.
    /// When a constrained edge is split, both halves keep the constraint. Note that collapsing a neighbour
    /// edge that would remove a constrained edge is also refused.
    /// </summary>
    public struct EdgeConstraint
    {
        EdgeRefineFlags refineFlags;
        public IProjectionTarget Target;        // edge is associated with this projection Target.
                                                // Currently only used as information, we do not explicitly
                                                // project edges onto targets (must also set VertexConstraint)

        public int TrackingSetID;               // not actually a constraint, but allows is to find descendents
                                                // of an constrained input edge


        public EdgeConstraint(EdgeRefineFlags rflags)
        {
            refineFlags = rflags;
            Target = null;
            TrackingSetID = -1;
        }

        public EdgeConstraint(EdgeRefineFlags rflags, IProjectionTarget target)
        {
            refineFlags = rflags;
            Target = target;
            TrackingSetID = -1;
        }

        public bool CanFlip {
            get { return (refineFlags & EdgeRefineFlags.NoFlip) == 0; }
        }
        public bool CanSplit {
            get { return (refineFlags & EdgeRefineFlags.NoSplit) == 0; }
        }
        public bool CanCollapse {
            get { return (refineFlags & EdgeRefineFlags.NoCollapse) == 0; }
        }
        public bool NoModifications {
            get { return (refineFlags & EdgeRefineFlags.FullyConstrained) == EdgeRefineFlags.FullyConstrained; }
        }

        public bool IsUnconstrained {
            get { return refineFlags == EdgeRefineFlags.NoConstraint && Target == null; }
        }

        static public readonly EdgeConstraint Unconstrained = new EdgeConstraint() { refineFlags = 0, TrackingSetID = -1 };
        static public readonly EdgeConstraint NoFlips = new EdgeConstraint() { refineFlags = EdgeRefineFlags.NoFlip, TrackingSetID = -1 };
        static public readonly EdgeConstraint FullyConstrained = new EdgeConstraint() { refineFlags = EdgeRefineFlags.FullyConstrained, TrackingSetID = -1 };
    }



    /// <summary>
    /// Constraint on one vertex for <see cref="Remesher"/> and <see cref="Reducer"/>: Fixed (the vertex does
    /// not move; edges to it collapse to its position), or Target (the vertex is projected onto the target
    /// after each move, eg to keep it on a boundary curve). Pinned is Fixed without a set id.
    /// </summary>
    public struct VertexConstraint
    {
        public bool Fixed;
        public int FixedSetID;      // in Remesher, we can allow two Fixed vertices with 
                                    // same FixedSetID to be collapsed together

        public IProjectionTarget Target;    // vertex is constrained to lie on this projection Target.
                                            // Fixed and Target are mutually exclusive
        
        public VertexConstraint(bool isFixed, int setID = InvalidSetID)
        {
            Fixed = isFixed;
            FixedSetID = setID;
            Target = null;
        }

        public VertexConstraint(IProjectionTarget target)
        {
            Fixed = false;
            FixedSetID = InvalidSetID;
            Target = target;
        }

        public const int InvalidSetID = -1;     // clients should interpret negative values as invalid
                                                // (in case you wanted to use negative values for something else...)

        static public readonly VertexConstraint Unconstrained = new VertexConstraint() 
            { Fixed = false, FixedSetID = InvalidSetID, Target = null };
        static public readonly VertexConstraint Pinned = new VertexConstraint() 
            { Fixed = true, FixedSetID = InvalidSetID, Target = null };
     }




    /// <summary>
    /// Per-edge and per-vertex constraints (by id) for <see cref="Remesher"/> and <see cref="Reducer"/>,
    /// passed with SetExternalConstraints(). Ids not in the sets are unconstrained. The refiner updates this
    /// object as it splits and collapses edges. <see cref="MeshConstraintUtil"/> has helpers that fill it for
    /// common cases (fixed or preserved boundaries, group boundaries, feature edges).
    /// </summary>
    public class MeshConstraints
    {

        Dictionary<int, EdgeConstraint> Edges = new Dictionary<int, EdgeConstraint>();

        int set_id_counter;         // use this to allocate FixedSetIDs

        public MeshConstraints()
        {
            set_id_counter = 0;
        }

        /// <summary> a new unique FixedSetID / TrackingSetID (0, 1, 2, ...) </summary>
        public int AllocateSetID() {
            return set_id_counter++;
        }

        public bool HasEdgeConstraint(int eid)
        {
            return Edges.ContainsKey(eid);
        }

        public EdgeConstraint GetEdgeConstraint(int eid)
        {
            EdgeConstraint ec;
            if (Edges.TryGetValue(eid, out ec))
                return ec;
            return EdgeConstraint.Unconstrained;
        }

        public void SetOrUpdateEdgeConstraint(int eid, EdgeConstraint ec)
        {
            Edges[eid] = ec;
        }

        public void ClearEdgeConstraint(int eid)
        {
            Edges.Remove(eid);
        }

        public List<int> FindConstrainedEdgesBySetID(int setID)
        {
            List<int> result = new List<int>();
            foreach ( var pair in Edges ) {
                if (pair.Value.TrackingSetID == setID)
                    result.Add(pair.Key);
            }
            return result;
        }



        Dictionary<int, VertexConstraint> Vertices = new Dictionary<int, VertexConstraint>();

        public bool HasVertexConstraint(int vid)
        {
            return Vertices.ContainsKey(vid);
        }

        public VertexConstraint GetVertexConstraint(int vid)
        {
            VertexConstraint vc;
            if (Vertices.TryGetValue(vid, out vc))
                return vc;
            return VertexConstraint.Unconstrained;
        }

        public bool GetVertexConstraint(int vid, ref VertexConstraint vc)
        {
            return Vertices.TryGetValue(vid, out vc);
        }

        public void SetOrUpdateVertexConstraint(int vid, VertexConstraint vc)
        {
            Vertices[vid] = vc;
        }

        public void ClearVertexConstraint(int vid)
        {
            Vertices.Remove(vid);
        }


        public System.Collections.IEnumerable VertexConstraintsItr() {
            foreach (KeyValuePair<int,VertexConstraint> v in Vertices)
                yield return v;
        }


        /// <summary> a copy of these constraints (Remesher.RejectInvertingPasses) </summary>
        internal MeshConstraints Copy()
        {
            var copy = new MeshConstraints();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary> replace these constraints with a copy of other's (Remesher.RejectInvertingPasses) </summary>
        internal void CopyFrom(MeshConstraints other)
        {
            Edges = new Dictionary<int, EdgeConstraint>(other.Edges);
            Vertices = new Dictionary<int, VertexConstraint>(other.Vertices);
            set_id_counter = other.set_id_counter;
        }

		public bool HasConstraints {
			get { return Edges.Count > 0 || Vertices.Count > 0; }
		}

    }
}
