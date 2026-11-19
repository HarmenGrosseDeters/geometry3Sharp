// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace BoolCmp
{
    /// <summary>
    /// The eval's diagnostic of "inside-out components" (issue #1067, iud p0008 step 3: a 34-triangle residue of volume 1.5e-20 that
    /// SafeRounding's component removal leaves). Per closed inside-out component of a mesh: what the removal's tests see of it, so
    /// that it can be read off why it stays. The numbers are those of MeshRoundingRepair.component_scales and
    /// remove_tiny_components with the default settings (read from a MeshRoundingRepair): size = the largest component's diagonal
    /// (components joined by vertices, as the repair does); tiny = area below 1e4 * (DegenerateArea * size^2); thin = 2 |V| below
    /// maxLength * area, with maxLength = max(MaxDisplacement * size, UlpFactor * ulp(largest coordinate of the box of the
    /// vertex-connected component), AbsoluteTolerance); closed = no boundary edge (open components are not described: the
    /// removal skips them and they have no volume sign); "inverted" is the exact sign of the volume
    /// (MeshPostRepairCleanup.volume_sign), "flagged" the verdict of FindInvertedComponents (which looks at the nesting too, on this
    /// mesh as it is). Whether the component was touched by an edit of the repair (the test that gates the removal of a thin one)
    /// cannot be told afterwards, so both outcomes are given: would_remove_if_untouched (the rule of #1152: tiny, inverted and
    /// flagged) and would_remove_if_touched (tiny, or thin unless it holds real volume for its area), each without the
    /// KeepWhenEmptied veto, which is_whole_result tells about. Double precision: the eval's oracle, not exact code.
    /// </summary>
    public static class InsideOutDiagnostics
    {
        /// <summary> a double for a JSON row: a value that is not finite (an overflowing extent) as a string, which the serializer accepts </summary>
        public static object Num(double x) => double.IsNaN(x) || double.IsInfinity(x) ? (object)x.ToString(System.Globalization.CultureInfo.InvariantCulture) : x;

        /// <summary> one row per closed inside-out component (inverted by the exact sign, or flagged by FindInvertedComponents), in the order of its smallest triangle id </summary>
        public static List<Dictionary<string, object>> Describe(DMesh3 mesh)
        {
            var rows = new List<Dictionary<string, object>>();
            if (mesh == null || mesh.TriangleCount == 0)
                return rows;
            var defaults = new MeshRoundingRepair(new DMesh3());
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(mesh, out int[] componentOf);
            double size = MeshRepairMetrics.LargestDiagonal(boxes);
            var flagged = new bool[mesh.MaxTriangleID];
            bool anyOpen = false, anyClosed = false;
            foreach (int[] fc in MeshPostRepairCleanup.FindInvertedComponents(mesh))
                foreach (int t in fc)
                    flagged[t] = true;
            // (as the repair computes it: DegenerateArea times the size squared, then 1e4 times that)
            double tinyBound = 1e4 * (defaults.DegenerateArea * size * size);
            foreach (List<int> comp0 in MeshRoundingRepair.connected_components(mesh)) {
                int[] comp = comp0.ToArray();
                bool closed = true;
                foreach (int t in comp) {
                    Index3i te = mesh.GetTriEdges(t);
                    closed &= mesh.IsBoundaryEdge(te.a) == false && mesh.IsBoundaryEdge(te.b) == false && mesh.IsBoundaryEdge(te.c) == false;
                }
                if (closed == false) {
                    anyOpen = true;
                    continue;
                }
                anyClosed = true;
                int sign = MeshPostRepairCleanup.volume_sign(mesh, comp);
                bool isFlagged = flagged[comp[0]];
                if (sign >= 0 && isFlagged == false)
                    continue;
                double area = 0, minArea = double.MaxValue, volume = 0;
                Vector3d o = mesh.GetVertex(mesh.GetTriangle(comp[0]).a);
                foreach (int t in comp) {
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                    double ar = 0.5 * (b - a).Cross(c - a).Length;
                    area += ar;
                    minArea = Math.Min(minArea, ar);
                    volume += (a - o).Dot((b - o).Cross(c - o)) / 6;
                }
                // (the box of the vertex-connected component: two lobes that meet at a vertex are one component of the repair)
                AxisAlignedBox3d box = boxes[componentOf[mesh.GetTriangle(comp[0]).a]];
                double largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
                double ulpTerm = defaults.UlpFactor * Ulp(largest);
                double maxLength = Math.Max(defaults.MaxDisplacement * size, Math.Max(ulpTerm, defaults.AbsoluteTolerance));
                double absVolume = Math.Abs(volume);
                bool tiny = area < tinyBound, thin = 2 * absVolume < maxLength * area;
                bool realVolume = absVolume >= 1e-3 * area * Math.Sqrt(area);
                rows.Add(new Dictionary<string, object> {
                    ["tris"] = comp.Length,
                    ["is_whole_result"] = comp.Length == mesh.TriangleCount,
                    ["volume_sign"] = sign,
                    ["flagged"] = isFlagged,
                    ["area"] = Num(area),
                    ["min_tri_area"] = Num(minArea),
                    ["abs_volume"] = Num(absVolume),
                    ["box_diagonal"] = Num(box.DiagonalLength),
                    ["size"] = Num(size),
                    ["tiny_bound"] = Num(tinyBound),
                    ["tiny"] = tiny,
                    ["max_length"] = Num(maxLength),
                    ["thin"] = thin,
                    // the shape test of the removal: a component that is not tiny and holds |V| >= 1e-3 area^1.5 is kept (ScaleDisparity)
                    ["volume_ratio"] = Num(area > 0 ? absVolume / (area * Math.Sqrt(area)) : 0.0),
                    ["thickness"] = Num(area > 0 ? 2 * absVolume / area : 0.0),
                    ["would_remove_if_untouched"] = tiny && sign < 0 && isFlagged,
                    ["would_remove_if_touched"] = (tiny || thin) && (tiny || realVolume == false),
                });
            }
            // (open components are skipped; a mesh of open ones only still says so)
            if (anyOpen && anyClosed == false)
                rows.Add(new Dictionary<string, object> { ["open_components_only"] = true });
            return rows;
        }

        // the gap to the next double above x (x finite, not negative); net48 has no Math.BitIncrement
        static double Ulp(double x)
        {
            if (!(x >= 0) || double.IsInfinity(x))
                return 0;
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;
        }

        /// <summary> the mesh of a soup (positions and triangles as read from a result file), without welding: the library's own mesh. Null if the soup is not a mesh (an edge with more than two triangles, a degenerate or repeated triangle) </summary>
        public static DMesh3 FromSoup(IList<Vector3d> v, IList<Index3i> t)
        {
            var m = new DMesh3();
            foreach (Vector3d p in v)
                m.AppendVertex(p);
            foreach (Index3i f in t)
                if (m.AppendTriangle(f.a, f.b, f.c) < 0)
                    return null;
            return m;
        }
    }
}
