using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The final lost-part check of <see cref="MeshRobustRepair"/> (F1 of docs/design/ROBUST_REPAIR_SCALE_FREE_STUDY.md, owner 2026-11-17):
    /// the input is split into separable parts, components linked when their bounding boxes, each padded by 1e-3 of its own diagonal, meet
    /// (closed and open components alike). A part is lost when no triangle of the result lies within a quarter of the part's own diagonal of
    /// any of its sampled vertices. Every part is judged against its own size, not against the scene or the largest component, so a small part far
    /// from a large one (which the scene-relative cleanups, the rounding's tiny test and the lost-part check relative to the largest component
    /// let go) is seen. Inputs with one part are not checked (the check is about parts lying apart), nor parts of fewer than MinTriangles
    /// triangles (stray triangles the repair removes on purpose) or without extent, nor inputs with more than MaxComponents components
    /// (triangle soups). No parallel loop; the samples and the order are deterministic (vertex ids, components in the order of their first
    /// triangle). Run in the working frame of Apply(), so the decision is the same for 2^k scaled inputs by construction.
    /// </summary>
    internal static class MeshRobustRepairLostParts
    {
        internal const int MaxComponents = 4096;
        internal const int MinTriangles = 4;
        internal const int SamplesPerPart = 64;
        /// <summary> padding of each component's box, a fraction of its own diagonal </summary>
        internal const double BoxPadding = 1e-3;
        /// <summary> a part is present if the result has a triangle closer than this fraction of the part's diagonal to one of its samples </summary>
        internal const double PresentDistance = 0.25;

        internal sealed class Part
        {
            public double Diagonal;
            public Vector3d[] Samples;
        }

        /// <summary>
        /// The parts of the input that the check judges (taken before the repair, which changes the mesh in place); null if the input
        /// has fewer than two parts or more than MaxComponents components
        /// </summary>
        public static List<Part> Parts(DMesh3 input)
        {
            if (input.TriangleCount == 0)
                return null;
            int[] componentOf;
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(input, out componentOf);
            int n = boxes.Count;
            if (n < 2 || n > MaxComponents)
                return null;
            var padded = new AxisAlignedBox3d[n];
            for (int i = 0; i < n; ++i) {
                double d = BoxPadding * boxes[i].DiagonalLength;
                padded[i] = new AxisAlignedBox3d(boxes[i].Min - new Vector3d(d, d, d), boxes[i].Max + new Vector3d(d, d, d));
            }
            // union-find over the components, pairs found by a sweep along x (sorted by the padded minimum, ties by index)
            var parent = new int[n];
            for (int i = 0; i < n; ++i) parent[i] = i;
            Func<int, int> find = (i) => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            var order = new int[n];
            for (int i = 0; i < n; ++i) order[i] = i;
            Array.Sort(order, (a, b) => { int c = padded[a].Min.x.CompareTo(padded[b].Min.x); return c != 0 ? c : a.CompareTo(b); });
            for (int ii = 0; ii < n; ++ii) {
                int i = order[ii];
                for (int jj = ii + 1; jj < n && padded[order[jj]].Min.x <= padded[i].Max.x; ++jj) {
                    int j = order[jj];
                    if (meets(padded[i], padded[j])) {
                        int a = find(i), b = find(j);
                        if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                    }
                }
            }
            // the parts: their triangles' vertices (in id order) and boxes
            var partOf = new Dictionary<int, int>();
            var partBoxes = new List<AxisAlignedBox3d>();
            var partTris = new List<int>();
            var partVerts = new List<List<int>>();
            var seen = new bool[input.MaxVertexID];
            foreach (int t in input.TriangleIndices()) {
                Index3i tv = input.GetTriangle(t);
                int r = find(componentOf[tv.a]), p;
                if (partOf.TryGetValue(r, out p) == false) {
                    partOf[r] = p = partBoxes.Count;
                    partBoxes.Add(AxisAlignedBox3d.Empty);
                    partTris.Add(0);
                    partVerts.Add(new List<int>());
                }
                AxisAlignedBox3d b = partBoxes[p];
                partTris[p]++;
                for (int k = 0; k < 3; ++k) {
                    b.Contain(input.GetVertex(tv[k]));
                    if (seen[tv[k]] == false) {
                        seen[tv[k]] = true;
                        partVerts[p].Add(tv[k]);
                    }
                }
                partBoxes[p] = b;
            }
            if (partBoxes.Count < 2)
                return null;
            var parts = new List<Part>();
            for (int p = 0; p < partBoxes.Count; ++p) {
                double diag = partBoxes[p].DiagonalLength;
                if (partTris[p] < MinTriangles || !(diag > 0) || double.IsInfinity(diag))
                    continue;
                List<int> vs = partVerts[p];
                vs.Sort();
                int stride = Math.Max(1, (vs.Count + SamplesPerPart - 1) / SamplesPerPart);
                var samples = new List<Vector3d>();
                for (int k = 0; k < vs.Count; k += stride)
                    samples.Add(input.GetVertex(vs[k]));
                parts.Add(new Part() { Diagonal = diag, Samples = samples.ToArray() });
            }
            return parts.Count > 0 ? parts : null;
        }

        /// <summary> true if a part has no triangle of the result within PresentDistance of its own diagonal of any of its samples </summary>
        public static bool AnyLost(List<Part> parts, DMesh3 result)
        {
            if (parts == null)
                return false;
            if (result.TriangleCount == 0)
                return true;
            var tree = new DMeshAABBTree3(result, true);
            foreach (Part part in parts) {
                double reach = PresentDistance * part.Diagonal;
                bool present = false;
                foreach (Vector3d p in part.Samples) {
                    if (tree.FindNearestTriangle(p, reach) != DMesh3.InvalidID) {
                        present = true;
                        break;
                    }
                }
                if (present == false)
                    return true;
            }
            return false;
        }

        static bool meets(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return a.Max.x >= b.Min.x && b.Max.x >= a.Min.x && a.Max.y >= b.Min.y && b.Max.y >= a.Min.y && a.Max.z >= b.Min.z && b.Max.z >= a.Min.z;
        }
    }
}
