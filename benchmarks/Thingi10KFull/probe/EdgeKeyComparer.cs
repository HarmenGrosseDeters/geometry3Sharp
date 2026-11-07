using System.Collections.Generic;
using g3;

internal sealed class EdgeKeyComparer : IEqualityComparer<long>
{
    internal static readonly EdgeKeyComparer Instance = new EdgeKeyComparer();

    private EdgeKeyComparer() { }

    public bool Equals(long x, long y) => x == y;

    // Int64's XOR hash collapses correlated vertex IDs; reuse the existing pair hash.
    public int GetHashCode(long key) => new Index2i((int)(key >> 32), (int)key).GetHashCode();
}
