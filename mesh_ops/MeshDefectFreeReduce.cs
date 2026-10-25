// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Mesh reduction that adds no self-intersections and no folds. The Reducer checks each collapse only
    /// locally (normal flips, triangle quality), so collapses can push triangles through nearby parts of the
    /// surface. Here the reduced result is checked exactly (MeshRepairMetrics: intersecting triangle pairs,
    /// folds over FoldAngle); if it has defects the input does not have there, the input vertices near them
    /// are pinned and the reduction is repeated from the input, at most MaxRounds times. Defects of the
    /// input itself are left alone (their surroundings are pinned from the start).
    /// </summary>
    public class MeshDefectFreeReduce
    {
        public DMesh3 Mesh;
        /// <summary> creates and configures the reducer for a mesh (eg tolerances, quadric options) </summary>
        public Func<DMesh3, Reducer> CreateReducer = (m) => new Reducer(m);
        /// <summary> runs the reduction (eg r => r.ReduceToMaxQuadricError(tol)) </summary>
        public Action<Reducer> Reduce;
        /// <summary> input vertices within this distance of a defect are pinned (0 = 2 x mean edge length) </summary>
        public double PinRadius = 0;
        public int MaxRounds = 4;
        public double FoldAngle = 175;

        // outputs
        public int Rounds;
        public int PinnedVertices;
        /// <summary> new defects (not near input defects) left in the result; 0 unless MaxRounds ran out </summary>
        public int DefectsLeft;

        public MeshDefectFreeReduce(DMesh3 mesh, Action<Reducer> reduce)
        {
            Mesh = mesh;
            Reduce = reduce;
        }

        /// <summary>
        /// Reduces Mesh in place. If new defects are left after MaxRounds, Mesh is left unreduced (and
        /// false is returned): no reduction is better than one with new self-intersections.
        /// </summary>
        public bool Apply()
        {
            if (Mesh.TriangleCount == 0)
                return false;
            DMesh3 input = new DMesh3(Mesh);
            double baseRadius = PinRadius;
            if (baseRadius <= 0) {
                double sum = 0; int n = 0;
                foreach (int eid in input.EdgeIndices()) {
                    Index2i ev = input.GetEdgeV(eid);
                    sum += input.GetVertex(ev.a).Distance(input.GetVertex(ev.b)); n++;
                }
                baseRadius = 2 * sum / Math.Max(1, n);
            }
            // regions (center, radius) pinned in the input; defects of the input are kept as they are
            var inputDefects = defect_regions(input, baseRadius);
            var pins = new List<Vector4d>(inputDefects);

            DMesh3 result = null;
            DefectsLeft = 0;
            for (Rounds = 1; Rounds <= MaxRounds; ++Rounds) {
                result = new DMesh3(input);
                Reducer reducer = CreateReducer(result);
                PinnedVertices = pin_in(result, pins, reducer);
                Reduce(reducer);
                // new defects: not within a region of a defect of the input
                var newDefects = new List<Vector4d>();
                foreach (Vector4d d in defect_regions(result, baseRadius))
                    if (inside_any(xyz(d), inputDefects) == false)
                        newDefects.Add(d);
                DefectsLeft = newDefects.Count;
                if (newDefects.Count == 0)
                    break;
                // pin their regions, growing with every round (collapses can move things further)
                double grow = Math.Pow(1.5, Rounds - 1);
                foreach (Vector4d d in newDefects)
                    pins.Add(new Vector4d(d.x, d.y, d.z, d.w * grow));
            }
            if (DefectsLeft > 0)
                return false;
            Mesh.Copy(result);
            return true;
        }

        static Vector3d xyz(Vector4d v) { return new Vector3d(v.x, v.y, v.z); }

        static bool inside_any(Vector3d p, List<Vector4d> regions)
        {
            foreach (Vector4d r in regions)
                if (p.Distance(xyz(r)) <= r.w) return true;
            return false;
        }

        // regions around self-intersections and folds: the triangles involved (their bounding spheres),
        // plus the base radius
        List<Vector4d> defect_regions(DMesh3 mesh, double baseRadius)
        {
            var regions = new List<Vector4d>();
            Action<int> add_tri = (tid) => {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d m = (a + b + c) / 3;
                double r = Math.Max(m.Distance(a), Math.Max(m.Distance(b), m.Distance(c)));
                regions.Add(new Vector4d(m.x, m.y, m.z, r + baseRadius));
            };
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(mesh)) {
                add_tri(pair.a); add_tri(pair.b);
            }
            foreach (int eid in MeshRepairMetrics.FoldedEdges(mesh, FoldAngle)) {
                Index2i et = mesh.GetEdgeT(eid);
                add_tri(et.a); add_tri(et.b);
            }
            return regions;
        }

        // pin the vertices inside the regions
        static int pin_in(DMesh3 mesh, List<Vector4d> regions, Reducer reducer)
        {
            if (regions.Count == 0)
                return 0;
            double maxR = 0;
            foreach (Vector4d r in regions) maxR = Math.Max(maxR, r.w);
            var grid = new PointHashGrid3d<int>(maxR, -1);
            for (int k = 0; k < regions.Count; ++k) grid.InsertPointUnsafe(k, xyz(regions[k]));
            MeshConstraints cons = reducer.Constraints ?? new MeshConstraints();
            int pinned = 0;
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                // nearest region center by distance minus radius (inside if <= 0)
                var near = grid.FindNearestInRadius(v, maxR, (k) => v.Distance(xyz(regions[k])) - regions[k].w);
                if (near.Key >= 0 && near.Value <= 0) {
                    cons.SetOrUpdateVertexConstraint(vid, VertexConstraint.Pinned);
                    pinned++;
                }
            }
            if (reducer.Constraints == null)
                reducer.SetExternalConstraints(cons);
            return pinned;
        }
    }
}
