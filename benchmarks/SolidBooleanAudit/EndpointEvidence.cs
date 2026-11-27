using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using g3;

// Offline inspection of a snapshot left by the public guard. Nothing here changes the Boolean path.
static class EndpointEvidence
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal sealed record Snapshot(long ActiveSources, long TouchedSources, long Boundary, long Interior,
        long RawReferenceCeiling, long EndpointBound, long WeightedBound);

    public static Snapshot Read(ExactMeshBoolean boolean)
    {
        object arrangement = typeof(ExactMeshBoolean).GetField("arr", Members).GetValue(boolean);
        Type type = arrangement.GetType();
        var triangles = (IEnumerable)type.GetField("Triangles", Members).GetValue(arrangement);
        var culled = (bool[])type.GetField("Culled", Members).GetValue(arrangement);
        if (type.GetField("TriangulateMeshes", Members).GetValue(arrangement) != null)
            throw new InvalidOperationException("The audit expects the direct Boolean path without a triangulation mask.");
        if ((bool)type.GetField("RegionClipping", Members).GetValue(arrangement))
            throw new InvalidOperationException("Region clipping can discard endpoints from this snapshot.");
        Type triangleType = type.GetNestedType("InputTriangle");
        var degenerate = triangleType.GetProperty("Degenerate");
        var points = triangleType.GetProperty("Points");
        var segments = triangleType.GetProperty("Segments");
        var corners = triangleType.GetProperty("Corners");
        Type segmentType = type.GetNestedType("Segment");
        var start = segmentType.GetField("P");
        var end = segmentType.GetField("Q");
        var canonical = type.GetMethod("Canonical").CreateDelegate<Func<int, int>>(arrangement);
        var orient = type.GetMethod("Orient", new[] { triangleType, typeof(int), typeof(int), typeof(int) });

        long activeSources = 0, touchedSources = 0, raw = 0, boundary = 0, interior = 0;
        var args = new object[4];
        int ti = -1;
        foreach (object triangle in triangles) {
            if (culled[++ti] || (bool)degenerate.GetValue(triangle))
                continue;
            activeSources++;
            var ids = (HashSet<int>)points.GetValue(triangle);
            var edges = (ICollection)segments.GetValue(triangle);
            raw = checked(raw + ids.Count + 2L * edges.Count);
            if (ids.Count == 0 && edges.Count == 0)
                continue;
            touchedSources++;
            var known = new HashSet<int>();
            foreach (int id in ids)
                known.Add(canonical(id));
            foreach (object segment in edges) {
                known.Add(canonical((int)start.GetValue(segment)));
                known.Add(canonical((int)end.GetValue(segment)));
            }
            var corner = (Index3i)corners.GetValue(triangle);
            for (int k = 0; k < 3; ++k)
                known.Remove(canonical(corner[k]));
            args[0] = triangle;
            foreach (int id in known) {
                args[3] = id;
                bool onBoundary = false;
                for (int k = 0; k < 3; ++k) {
                    args[1] = corner[k];
                    args[2] = corner[(k + 1) % 3];
                    int sign = (int)orient.Invoke(arrangement, args);
                    if (sign < 0)
                        throw new InvalidOperationException($"An endpoint projects outside its source triangle {ti}.");
                    onBoundary |= sign == 0;
                }
                if (onBoundary) boundary++; else interior++;
            }
        }
        long endpointBound = activeSources + boundary + interior;
        if (endpointBound < boolean.Counts["piece_lower_bound"])
            throw new InvalidOperationException("The complete snapshot count is below the guard's prefix certificate.");
        return new Snapshot(activeSources, touchedSources, boundary, interior, checked(activeSources + raw),
            endpointBound, activeSources + boundary + 2 * interior);
    }
}
