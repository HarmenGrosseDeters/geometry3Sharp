using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meshing2Acceptance;

static class EdgeSelfTest
{
    sealed class Case
    {
        public double[][] Points;
        public int[][] Edges;
        public bool MustPruneLine,MustPruneHull,MustCoarsen;
    }
    static Case Make(double[][] points,params int[][] edges) => new Case { Points=points,Edges=edges };
    public static void Run()
    {
        var cases=new List<Case> {
            Make(new[]{new[]{0d,0},new[]{4d,4},new[]{0d,4},new[]{4d,0}},new[]{0,1},new[]{2,3}),
            Make(new[]{new[]{0d,0},new[]{4d,0},new[]{2d,0},new[]{2d,2}},new[]{0,1},new[]{2,3}),
            Make(new[]{new[]{0d,0},new[]{4d,0},new[]{1d,0},new[]{6d,0}},new[]{0,1},new[]{2,3}),
            Make(new[]{new[]{0d,0},new[]{4d,4},new[]{0d,0},new[]{4d,0}},new[]{0,1},new[]{2,3}),
            Make(new[]{new[]{0d,0},new[]{0d,0},new[]{-1d,0},new[]{1d,0}},new[]{0,1},new[]{2,3}),
            Make(new[]{new[]{0d,0},new[]{4d,4},new[]{8d,8},new[]{12d,12}},new[]{0,1},new[]{2,3})
        };
        var fanPoints=new List<double[]> {new[]{0d,0}};var fanEdges=new List<int[]>();
        for(int i=0;i<50;i++){fanPoints.Add(new[]{10d+i,20d-i});fanEdges.Add(new[]{0,i+1});}
        cases.Add(new Case {Points=fanPoints.ToArray(),Edges=fanEdges.ToArray(),MustPruneLine=true});
        var parallelPoints=new List<double[]>();var parallelEdges=new List<int[]>();
        for(int i=0;i<40;i++){int n=parallelPoints.Count;parallelPoints.Add(new[]{(double)i,0d});parallelPoints.Add(new[]{i+100d,100d});parallelEdges.Add(new[]{n,n+1});}
        cases.Add(new Case {Points=parallelPoints.ToArray(),Edges=parallelEdges.ToArray(),MustPruneLine=true});
        var hullPoints=new List<double[]> {new[]{0d,0},new[]{2d,2}};var hullEdges=new List<int[]> {new[]{0,1}};
        var corners=new[]{new[]{1.5,2.5},new[]{2.5,1.5},new[]{3d,3}};
        for(int i=0;i<30;i++){
            int n=hullPoints.Count;hullPoints.Add(corners[i%3]);hullPoints.Add(corners[(i+1)%3]);hullEdges.Add(new[]{n,n+1});
        }
        cases.Add(new Case {Points=hullPoints.ToArray(),Edges=hullEdges.ToArray(),MustPruneHull=true});
        var crossingPoints=new List<double[]> {new[]{-100d,0},new[]{100d,0}};var crossingEdges=new List<int[]> {new[]{0,1}};
        for(int i=-20;i<=20;i++){int n=crossingPoints.Count;crossingPoints.Add(new[]{(double)i,-10d});crossingPoints.Add(new[]{(double)i,10d});crossingEdges.Add(new[]{n,n+1});}
        cases.Add(Make(crossingPoints.ToArray(),crossingEdges.ToArray()));
        // a convex boundary (every point on the hull) with its boundary edges and random chords, many crossing: upper nodes have
        // hulls above StrtreeEdgeSearch.HullLimit and test their box corners instead (review of #411); the chords cross across them
        var ring=new List<double[]>();var ringEdges=new List<int[]>();var chordRandom=new Random(733);
        for(int i=0;i<96;i++){double t=-1+2.0*(i%48)/47;ring.Add(new[]{t*64,(i<48?1:-1)*(16-16*t*t)+(i<48?0.5:-0.5)});}
        for(int i=0;i<47;i++){ringEdges.Add(new[]{i,i+1});ringEdges.Add(new[]{48+i,49+i});}
        for(int i=0;i<200;i++)ringEdges.Add(new[]{chordRandom.Next(96),chordRandom.Next(96)});
        cases.Add(new Case {Points=ring.ToArray(),Edges=ringEdges.ToArray(),MustCoarsen=true});
        var random=new Random(461);
        for(int c=0;c<100;c++){
            var p=Enumerable.Range(0,20).Select(_=>new[]{(double)random.Next(-10,11),(double)random.Next(-10,11)}).ToArray();
            var e=Enumerable.Range(0,35).Select(_=>new[]{random.Next(20),random.Next(20)}).ToArray();
            cases.Add(Make(p,e));
        }
        int checkedCases=0;
        void Check(Case c,int exponent,double translate=0){
            double scale=Math.ScaleB(1,exponent);
            var mesh=new Mesh2Output {VX=c.Points.Select(p=>p[0]*scale+translate).ToArray(),VY=c.Points.Select(p=>p[1]*scale+translate).ToArray()};
            var keys=c.Edges.Select(e=>StrtreeEdgeSearch.Key(e[0],e[1])).ToArray();
            var search=new StrtreeEdgeSearch(mesh,keys);var expected=search.Exhaustive();
            var original=new Mesh2Output {VX=c.Points.Select(p=>p[0]).ToArray(),VY=c.Points.Select(p=>p[1]).ToArray()};
            if(new StrtreeEdgeSearch(original,keys).Exhaustive().Fingerprint!=expected.Fingerprint)throw new Exception("Edge similarity invariance differs");
            foreach(string mode in new[]{"boxes","nts","hulls"}){
                var actual=search.Run(mode);
                if(actual.Fingerprint!=expected.Fingerprint || actual.Hits!=expected.Hits)throw new Exception("Edge search differs from exact exhaustive check: "+mode);
                if(search.Run(mode).Fingerprint!=actual.Fingerprint)throw new Exception("Edge reuse differs");
                if(mode=="hulls" && c.MustPruneLine && actual.LinePruned==0)throw new Exception("Targeted closed-side case must prune a subtree");
                if(mode=="hulls" && c.MustPruneHull && actual.HullPruned==0)throw new Exception("Targeted finite-segment case must prune by a hull side");
                if(mode=="hulls" && c.MustCoarsen && (actual.CoarseNodes==0 || actual.Hits==0))throw new Exception("Targeted convex case must test box corners on crossing chords");
            }
            Parallel.For(0,3,_=>{if(search.Run("hulls").Fingerprint!=expected.Fingerprint)throw new Exception("Edge parallel differs");});
            checkedCases++;
        }
        foreach(var c in cases){foreach(int exponent in new[]{-500,0,500})Check(c,exponent);Check(c,0,1048576);}
        Check(cases[0],-1074);Check(cases[0],1020);
        Console.WriteLine("EDGE SELFTEST passed: "+checkedCases+" cases; exact all-pairs equivalence, contact/collinearity/shared/degenerate cases, actual line/hull pruning, scales/translation, reuse and parallel determinism.");
    }
}
