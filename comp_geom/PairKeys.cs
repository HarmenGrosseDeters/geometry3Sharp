using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The comparer for the 64-bit pair keys of the 2D mesher (source pairs of the small-angle cache, unsplittable pieces, grid cells): the
    /// shared <see cref="EdgeKeyComparer"/>. The default hash of a long is lo ^ hi, which for (a &lt;&lt; 32) | c is a ^ c: ids that lie
    /// close to each other (neighbours in a mesh) fall on a few dozen values and 200 000 pairs make one chain per lookup. A snap-grid
    /// parcels mesh of 56 000 triangles spent 20 of 22 seconds there (QA 2.0 T5, Meshing2D open items, item 2).
    /// </summary>
    internal static class PairKeys
    {
        /// <summary> tests only (per thread): a comparer to use instead, eg one that counts the probes (read when a refiner or a merge is created) </summary>
        [System.ThreadStatic] internal static IEqualityComparer<long> ForTests;

        internal static IEqualityComparer<long> Comparer { get { return ForTests ?? EdgeKeyComparer.Instance; } }
    }
}
