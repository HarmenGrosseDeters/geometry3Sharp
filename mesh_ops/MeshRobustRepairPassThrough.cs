using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Stage 0 of the part handling of <see cref="MeshRobustRepair"/> (design note docs/design/ROBUST_REPAIR_SCALE_FREE.md, option B): a closed
    /// connected component that is clean on its own (<see cref="MeshRobustRepair.NotCleanReason"/> of the component alone: closed, manifold,
    /// consistently oriented, outside facing, no self-intersections, no degenerate triangle) and whose bounding box meets the box of no other
    /// component is passed through untouched. Only the other components go through the repair stages, so the thresholds of those stages
    /// (relative to the diagonal of what they get) no longer see a clean part far away, and the defects of one part cannot touch another.
    /// </summary>
    internal static class MeshRobustRepairPassThrough
    {
        /// <summary>
        /// Splits the input. Returns false (nothing to do) if it has fewer than two or more than maxParts components or no component that
        /// passes through. passed: the pass through parts, appended in the order of their first triangle (a new mesh); rest: the other
        /// components (a new mesh, empty if all pass through); passedBoxes: the box of each passed part.
        /// </summary>
        public static bool Split(DMesh3 input, int maxParts, bool checkSelfIntersections, out DMesh3 passed, out DMesh3 rest, out List<AxisAlignedBox3d> passedBoxes)
        {
            passed = rest = null;
            passedBoxes = null;
            if (input.TriangleCount == 0)
                return false;
            int[] componentOf;
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(input, out componentOf);
            if (boxes.Count < 2 || boxes.Count > maxParts)
                return false;
            var tris = new List<int>[boxes.Count];
            for (int i = 0; i < tris.Length; ++i)
                tris[i] = new List<int>();
            foreach (int t in input.TriangleIndices())
                tris[componentOf[input.GetTriangle(t).a]].Add(t);
            var passedMesh = new DMesh3();
            var restMesh = new DMesh3();
            var boxesOut = new List<AxisAlignedBox3d>();
            for (int i = 0; i < boxes.Count; ++i) {
                bool alone = true;
                for (int j = 0; j < boxes.Count && alone; ++j)
                    alone = j == i || Meets(boxes[i], boxes[j]) == false;
                DMesh3 part = null;
                if (alone) {
                    part = new DSubmesh3(input, tris[i]).SubMesh;
                    if (MeshRobustRepair.NotCleanReason(part, checkSelfIntersections) != "")
                        part = null;
                }
                if (part != null) {
                    MeshEditor.Append(passedMesh, part);
                    boxesOut.Add(boxes[i]);
                } else {
                    MeshEditor.Append(restMesh, new DSubmesh3(input, tris[i]).SubMesh);
                }
            }
            if (boxesOut.Count == 0)
                return false;
            passed = passedMesh;
            rest = restMesh;
            passedBoxes = boxesOut;
            return true;
        }

        // closed boxes: touching boxes meet (AxisAlignedBox3d.Intersects is strict)
        static bool Meets(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return a.Max.x >= b.Min.x && b.Max.x >= a.Min.x && a.Max.y >= b.Min.y && b.Max.y >= a.Min.y && a.Max.z >= b.Min.z && b.Max.z >= a.Min.z;
        }

        /// <summary> true if the box of any triangle-bearing part of mesh meets one of the boxes </summary>
        public static bool MeetsAny(DMesh3 mesh, List<AxisAlignedBox3d> boxes)
        {
            if (mesh.TriangleCount == 0)
                return false;
            AxisAlignedBox3d b = MeshRepairMetrics.TriangleBounds(mesh);
            foreach (var p in boxes)
                if (Meets(b, p))
                    return true;
            return false;
        }
    }
}
