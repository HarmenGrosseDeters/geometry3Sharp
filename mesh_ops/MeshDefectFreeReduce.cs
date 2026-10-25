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

        /// <summary> reduces Mesh in place </summary>
        public bool Apply()
        {
            if (Mesh.TriangleCount == 0)
                return false;
            DMesh3 input = new DMesh3(Mesh);
            double radius = PinRadius;
            if (radius <= 0) {
                double sum = 0; int n = 0;
                foreach (int eid in input.EdgeIndices()) {
                    Index2i ev = input.GetEdgeV(eid);
                    sum += input.GetVertex(ev.a).Distance(input.GetVertex(ev.b)); n++;
                }
                radius = 2 * sum / Math.Max(1, n);
            }
            var inputDefects = defect_points(input);
            var pins = new List<Vector3d>(inputDefects);
            var inputGrid = new PointHashGrid3d<Vector3d>(radius, Vector3d.Zero);
            foreach (Vector3d p in inputDefects) inputGrid.InsertPointUnsafe(p, p);

            DMesh3 result = null;
            for (Rounds = 1; Rounds <= MaxRounds; ++Rounds) {
                result = new DMesh3(input);
                Reducer reducer = CreateReducer(result);
                PinnedVertices = pin_near(result, pins, radius, reducer);
                Reduce(reducer);
                // new defects: those not near a defect of the input
                var newDefects = new List<Vector3d>();
                foreach (Vector3d p in defect_points(result)) {
                    var near = inputGrid.FindNearestInRadius(p, radius, (q) => q.Distance(p));
                    if (near.Value == double.MaxValue || near.Key.Distance(p) > radius)
                        newDefects.Add(p);
                }
                DefectsLeft = newDefects.Count;
                if (newDefects.Count == 0)
                    break;
                if (Rounds == MaxRounds) {
                    // still defects: keep the input there entirely (no reduction near any defect found)
                    break;
                }
                pins.AddRange(newDefects);
            }
            Mesh.Copy(result);
            return true;
        }

        // points where the mesh has self-intersections or folds
        List<Vector3d> defect_points(DMesh3 mesh)
        {
            var points = new List<Vector3d>();
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(mesh)) {
                points.Add(mesh.GetTriCentroid(pair.a));
                points.Add(mesh.GetTriCentroid(pair.b));
            }
            foreach (int eid in MeshRepairMetrics.FoldedEdges(mesh, FoldAngle)) {
                Index2i ev = mesh.GetEdgeV(eid);
                points.Add(0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b)));
            }
            return points;
        }

        // pin the vertices within radius of the points
        static int pin_near(DMesh3 mesh, List<Vector3d> points, double radius, Reducer reducer)
        {
            if (points.Count == 0)
                return 0;
            var grid = new PointHashGrid3d<int>(radius, -1);
            for (int k = 0; k < points.Count; ++k) grid.InsertPointUnsafe(k, points[k]);
            MeshConstraints cons = reducer.Constraints ?? new MeshConstraints();
            int pinned = 0;
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                var near = grid.FindNearestInRadius(v, radius, (k) => points[k].Distance(v));
                if (near.Key >= 0) {
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
