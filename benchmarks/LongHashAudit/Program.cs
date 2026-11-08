using System;
using System.Collections.Generic;
using System.Linq;
using g3;

// Collision probe, not an end-to-end timing benchmark. Uses the production comparer.
class Program
{
    static void Main()
    {
        var mixed = (IEqualityComparer<long>)typeof(DMesh3).Assembly.GetType("g3.EdgeKeyComparer")
            .GetField("Instance").GetValue(null);
        var edges = new List<(int a, int b)>();
        const int n = 128;
        for (int y = 0; y < n; ++y)
            for (int x = 0; x < n; ++x) {
                int a = y * n + x;
                if (x + 1 < n) edges.Add((a, a + 1));
                if (y + 1 < n) edges.Add((a, a + n));
                if (x + 1 < n && y + 1 < n) edges.Add((a, a + n + 1));
            }
        Probe("edge32 (Terrain map/composite, geodesic path)", edges.Select(e => ((long)e.a << 32) | (uint)e.b), mixed);
        Probe("edge31 (Terrain overlay/catch)", edges.Select(e => ((long)e.a << 31) | (uint)e.b), mixed);
        Probe("safety (min * int.MaxValue + max)", edges.Select(e => (long)e.a * int.MaxValue + e.b), mixed);
        Probe("geodesic weights/fans (a * N + b)", edges.Select(e => (long)e.a * n * n + e.b), mixed);
        Probe("Terrain scalar A/B vertices", Enumerable.Range(0, n*n).SelectMany(v => new[] { (long)v, (1L << 62) | (uint)v }), mixed);
        Probe("Terrain corners (I * (NY+2) + J)", Grid(n,n,1,(x,y,z) => (long)x*(n+2)+y), mixed);
        Probe("Terrain merge grid (x * 0x100000007 + y)", Grid(n,n,1,(x,y,z) => x*0x100000007L+y), mixed);
        Probe("Winding ancestors compact", Grid(64,64,64,Ancestor), mixed);
        Probe("Winding ancestors wide", Grid(512,2,512,Ancestor), mixed);
        Probe("Dual cells compact", Grid(64,64,64,Node), mixed);
        Probe("Dual cells wide", Grid(2048,2,32,Node), mixed);
        Probe("Dual coarse/fine same nodes", Grid(64,64,16,Node).SelectMany(k => new[] { k, k | long.MinValue }), mixed);
    }
    static long Ancestor(int x,int y,int z) => ((long)x<<40)|((long)y<<20)|(uint)z;
    static long Node(int x,int y,int z) => (long)(x+(1<<20))|((long)(y+(1<<20))<<21)|((long)(z+(1<<20))<<42);
    static IEnumerable<long> Grid(int nx,int ny,int nz,Func<int,int,int,long> key) {
        for(int x=0;x<nx;x++) for(int y=0;y<ny;y++) for(int z=0;z<nz;z++) yield return key(x,y,z);
    }
    static void Probe(string name,IEnumerable<long> input,IEqualityComparer<long> mixed) {
        long[] keys=input.Distinct().ToArray();
        Console.WriteLine($"{name}: keys={keys.Length}, default={keys.Select(k=>k.GetHashCode()).Distinct().Count()}, mixed={keys.Select(mixed.GetHashCode).Distinct().Count()}");
    }
}
