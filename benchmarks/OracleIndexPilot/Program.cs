using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Meshing2Acceptance;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

static class Program
{
    static Mesh2Output Make(double[][] points,int[][] triangles) => new Mesh2Output { VX=points.Select(p=>p[0]).ToArray(),VY=points.Select(p=>p[1]).ToArray(),Triangles=triangles };
    static void SelfTest()
    {
        var cases=new List<Mesh2Output> {
            Make(new[]{new[]{0d,0},new[]{4d,0},new[]{0d,4},new[]{1d,1},new[]{2d,1},new[]{1d,2}},new[]{new[]{0,1,2},new[]{3,4,5}}),
            Make(new[]{new[]{0d,0},new[]{4d,0},new[]{0d,4},new[]{2d,0},new[]{2d,-1},new[]{3d,-1}},new[]{new[]{0,1,2},new[]{3,4,5}}),
            Make(new[]{new[]{0d,0},new[]{4d,0},new[]{0d,4},new[]{0d,0},new[]{-2d,0},new[]{0d,-2}},new[]{new[]{0,1,2},new[]{3,4,5}})
        };
        var random=new Random(117);
        var boundaryPoints=new List<double[]> { new[]{0d,0},new[]{100d,0},new[]{0d,100} };
        var boundaryTriangles=new List<int[]> { new[]{0,1,2} };
        for (int i=0;i<12;i++) {
            int start=boundaryPoints.Count; double x=2+2*i;
            boundaryPoints.AddRange(new[]{new[]{x,0d},new[]{x,-1d},new[]{x+.5,-1d}});
            boundaryTriangles.Add(new[]{start,start+1,start+2});
            start=boundaryPoints.Count; x=60+.2*i;
            boundaryPoints.AddRange(new[]{new[]{x,60d},new[]{x+.05,60d},new[]{x,60.05}});
            boundaryTriangles.Add(new[]{start,start+1,start+2});
        }
        var multiTreeBoundary=Make(boundaryPoints.ToArray(),boundaryTriangles.ToArray());
        cases.Add(multiTreeBoundary);
        for (int sample=0;sample<100;sample++) {
            var points=Enumerable.Range(0,20).Select(_=>new[]{(double)random.Next(-10,11),(double)random.Next(-10,11)}).ToArray();
            var triangles=new List<int[]>();
            for (int attempt=0;attempt<60 && triangles.Count<20;attempt++) {
                int a=random.Next(20),b=random.Next(20),c=random.Next(20);
                int sign=Exact.Orient(Pt.FromDoubles(points[a][0],points[a][1]),Pt.FromDoubles(points[b][0],points[b][1]),Pt.FromDoubles(points[c][0],points[c][1]));
                if (sign!=0) triangles.Add(sign>0?new[]{a,b,c}:new[]{a,c,b});
            }
            cases.Add(Make(points,triangles.ToArray()));
        }
        int checkedCases=0;
        foreach (var original in cases) {
          string originalFingerprint=new StrtreePointSearch(original).Exhaustive().Fingerprint;
          foreach (int exponent in new[]{-500,0,500}) {
            double scale=Math.ScaleB(1,exponent);
            var mesh=new Mesh2Output { VX=original.VX.Select(x=>x*scale).ToArray(),VY=original.VY.Select(y=>y*scale).ToArray(),Triangles=original.Triangles };
            var search=new StrtreePointSearch(mesh); var expected=search.Exhaustive();
            if (expected.Fingerprint!=originalFingerprint) throw new Exception("Power-of-two invariance differs");
            foreach (string mode in new[]{"boxes","nts","pruned"}) {
                var actual=search.Run(mode);
                if (actual.Fingerprint!=expected.Fingerprint || actual.Hits!=expected.Hits) throw new Exception("Search differs from exact exhaustive check: "+mode);
                if (ReferenceEquals(original,multiTreeBoundary) && mode=="pruned" && actual.Pruned==0) throw new Exception("Targeted case must prune an actual subtree");
                if (search.Run(mode).Fingerprint!=actual.Fingerprint) throw new Exception("Reuse differs");
            }
            Parallel.For(0,3,_=> { if (search.Run("pruned").Fingerprint!=expected.Fingerprint) throw new Exception("Parallel differs"); });
            checkedCases++;
          }
        }
        Console.WriteLine("SELFTEST passed: "+checkedCases+" cases; exact exhaustive equivalence, boundary/nested/coincident cases, power-of-two scales, reuse and parallel determinism.");
    }
    static int Main(string[] args)
    {
        if (args.Length==1 && args[0]=="selftest") { SelfTest(); return 0; }
        if (args.Length<2 || args.Length>3) { Console.Error.WriteLine("OracleIndexPilot <boxes|nts|pruned> <mesh2-output> [query-count; default all]"); return 2; }
        var mesh=TextFormat.ReadOutput(File.ReadAllText(args[1]),out _);
        var search=new StrtreePointSearch(mesh);
        Console.WriteLine(JsonSerializer.Serialize(new { phase="start",mode=args[0],vertices=mesh.VertexCount,triangles=mesh.Triangles.Length }));
        Console.WriteLine(JsonSerializer.Serialize(search.Run(args[0],args.Length==3?int.Parse(args[2]):0)));
        return 0;
    }
}
