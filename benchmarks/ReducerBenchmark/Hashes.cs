// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Security.Cryptography;
using g3;
using gs;

// `hashes`: SHA-256 of every reduced mesh of the benchmark set (vertex coordinates bit for bit, triangles), to show
// that a change of the MaxDeviation check leaves the reductions unchanged
static class ReducerHashes
{
    public static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain)
    {
        foreach (var (name, input) in new (string, DMesh3)[] { ("bumpy sphere", bumpy(130)), ("terrain", terrain(300)) })
            foreach (double rel in new[] { 1e-3, 3e-3, 1e-2 }) {
                var m = new DMesh3(input);
                new Reducer(m) { MaxDeviation = rel * input.CachedBounds.DiagonalLength }.ReduceToTriangleCount(4);
                var bytes = new System.Collections.Generic.List<byte>();
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    bytes.AddRange(BitConverter.GetBytes(v));
                    bytes.AddRange(BitConverter.GetBytes(p.x)); bytes.AddRange(BitConverter.GetBytes(p.y)); bytes.AddRange(BitConverter.GetBytes(p.z));
                }
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    bytes.AddRange(BitConverter.GetBytes(t));
                    bytes.AddRange(BitConverter.GetBytes(tv.a)); bytes.AddRange(BitConverter.GetBytes(tv.b)); bytes.AddRange(BitConverter.GetBytes(tv.c));
                }
                using (var sha = SHA256.Create())
                    Console.WriteLine($"{name} {rel}: {m.TriangleCount} triangles, {BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16)}");
            }
    }
}
