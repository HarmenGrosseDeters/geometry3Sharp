using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Meshing2Acceptance;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

namespace Meshing2Acceptance
{
public sealed class StrtreePointSearch
{
    readonly Mesh2Output mesh;
    readonly Pt[] exact;
    readonly int[] used;
    public StrtreePointSearch(Mesh2Output mesh)
    {
        if (mesh.ExactVertices != null) throw new ArgumentException("Pilot accepts binary64 output only");
        if (mesh.VX.Concat(mesh.VY).Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new ArgumentException("Non-finite vertex");
        this.mesh = mesh;
        exact = mesh.VX.Select((x,i) => Pt.FromDoubles(x,mesh.VY[i])).ToArray();
        used = mesh.Triangles.SelectMany(t => t).Distinct().OrderBy(i => i).ToArray();
        if (mesh.Triangles.Any(t => Orient(t[0],t[1],t[2])<=0)) throw new ArgumentException("Pilot requires positive triangles, as Conformity does after Orientation");
    }

    // The existing oracle's OrientV filter, unchanged; uncertain cases use its exact predicate.
    int Orient(int a, int b, double cx, double cy, Pt c)
    {
        double ax=mesh.VX[a], ay=mesh.VY[a];
        double l=(mesh.VX[b]-ax)*(cy-ay), rr=(mesh.VY[b]-ay)*(cx-ax);
        double det=l-rr, sum=Math.Abs(l)+Math.Abs(rr), bound=1e-15*sum;
        if (sum>1e-200 && sum<1e200 && Math.Abs(det)>bound) return det>0 ? 1 : -1;
        return Exact.Orient(exact[a],exact[b],c);
    }
    int Orient(int a,int b,int c) => Orient(a,b,mesh.VX[c],mesh.VY[c],exact[c]);
    Envelope TriangleBox(int ti)
    {
        var t=mesh.Triangles[ti];
        return new Envelope(t.Min(i=>mesh.VX[i]),t.Max(i=>mesh.VX[i]),t.Min(i=>mesh.VY[i]),t.Max(i=>mesh.VY[i]));
    }

    sealed class Node
    {
        public Envelope Box;
        public Node[] Children;
        public int Item;
        public Pt[] Corners;
    }
    static Node Wrap(NetTopologySuite.Geometries.IBoundable<Envelope,int> source)
    {
        var e=source.Bounds;
        var node=new Node { Box=e };
        if (source is AbstractNode<Envelope,int> branch) {
            node.Children=branch.ChildBoundables.Select(Wrap).ToArray();
            node.Corners=new[] { Pt.FromDoubles(e.MinX,e.MinY),Pt.FromDoubles(e.MaxX,e.MinY),Pt.FromDoubles(e.MinX,e.MaxY),Pt.FromDoubles(e.MaxX,e.MaxY) };
        } else node.Item=source.Item;
        return node;
    }
    bool Outside(int[] t,Node node)
    {
        for (int edge=0;edge<3;edge++) {
            int a=t[edge], b=t[(edge+1)%3];
            // orient(a,b,p) is affine in p: maximize it over the closed box.
            // Its x coefficient is -(by-ay), its y coefficient is bx-ax.
            bool right=mesh.VY[b]<=mesh.VY[a], top=mesh.VX[b]>=mesh.VX[a];
            int corner=(right?1:0)+(top?2:0);
            double x=right?node.Box.MaxX:node.Box.MinX, y=top?node.Box.MaxY:node.Box.MinY;
            // Strictly outside only: points on edges must still be reported.
            if (Orient(a,b,x,y,node.Corners[corner])<0) return true;
        }
        return false;
    }
    sealed class Visitor : IItemVisitor<int>
    {
        public Action<int> Visit;
        public void VisitItem(int item) => Visit(item);
    }
    static int[] Ranks(double[] values)
    {
        var order=Enumerable.Range(0,values.Length).OrderBy(i => values[i]).ToArray();
        var ranks=new int[values.Length]; int rank=0;
        for (int i=0;i<order.Length;i++) {
            if (i>0 && values[order[i]]!=values[order[i-1]]) rank++;
            ranks[order[i]]=rank;
        }
        return ranks;
    }
    PointIndexResult RunCore(string mode,int queryCount,out List<(int,int)> found)
    {
        if (mode!="boxes" && mode!="nts" && mode!="pruned") throw new ArgumentException("Unknown mode");
        int count=queryCount<=0 ? mesh.Triangles.Length : Math.Min(queryCount,mesh.Triangles.Length);
        var queries=Enumerable.Range(0,count).Select(i => (int)((long)i*mesh.Triangles.Length/count)).ToArray();
        var hits=new List<(int,int)>(); long candidates=0,nodes=0,pruned=0;
        void Check(int ti,int id) {
            var t=mesh.Triangles[ti];
            if (id==t[0] || id==t[1] || id==t[2]) return;
            candidates++;
            if (Orient(t[0],t[1],id)>=0 && Orient(t[1],t[2],id)>=0 && Orient(t[2],t[0],id)>=0) hits.Add((ti,id));
        }
        using var process=Process.GetCurrentProcess();
        var cpuStart=process.TotalProcessorTime;
        var watch=Stopwatch.StartNew();
        if (mode=="boxes") {
            var rx=Ranks(mesh.VX); var ry=Ranks(mesh.VY);
            var tb=queries.Select(i => mesh.Triangles[i]).Select(t => new[] { t.Min(i=>rx[i]),t.Max(i=>rx[i]),t.Min(i=>ry[i]),t.Max(i=>ry[i]) }).ToArray();
            var vb=used.Select(i => new[] { rx[i],rx[i],ry[i],ry[i] }).ToArray();
            BoxPairs.Overlaps(tb,vb,false,(ti,vi) => Check(queries[ti],used[vi]));
        } else {
            var tree=new STRtree<int>();
            foreach (int id in used) tree.Insert(new Envelope(mesh.VX[id],mesh.VX[id],mesh.VY[id],mesh.VY[id]),id);
            tree.Build();
            if (mode=="nts") {
                var visitor=new Visitor();
                foreach (int ti in queries) {
                    visitor.Visit=id => Check(ti,id);
                    tree.Query(TriangleBox(ti),visitor);
                }
            } else {
                var root=Wrap(tree.Root);
                void Walk(Node node,int ti,Envelope triangleBox) {
                    nodes++;
                    if (!node.Box.Intersects(triangleBox)) return;
                    if (node.Children==null) { Check(ti,node.Item); return; }
                    if (Outside(mesh.Triangles[ti],node)) { pruned++; return; }
                    foreach (var child in node.Children) Walk(child,ti,triangleBox);
                }
                foreach (int ti in queries) Walk(root,ti,TriangleBox(ti));
            }
        }
        watch.Stop();
        process.Refresh();
        double cpu=(process.TotalProcessorTime-cpuStart).TotalSeconds;
        hits.Sort();
        found=hits;
        string canonical=string.Join("\n",hits.Select(p=>p.Item1+" "+p.Item2));
        string hash=Fingerprint(canonical);
        return new PointIndexResult(mode,count,watch.Elapsed.TotalSeconds,cpu,candidates,nodes,pruned,hits.Count,hash);
    }
    public PointIndexResult Run(string mode,int queryCount=0) => RunCore(mode,queryCount,out _);
    public IReadOnlyList<(int,int)> FindTouches()
    {
        RunCore("pruned",0,out var found);
        return found;
    }
    static string Fingerprint(string canonical)
    {
        using (var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","").ToLowerInvariant();
    }
    public PointIndexResult Exhaustive()
    {
        var hits=new List<(int,int)>();
        for (int ti=0;ti<mesh.Triangles.Length;ti++) foreach (int id in used) {
            var t=mesh.Triangles[ti];
            if (t.Contains(id)) continue;
            // Independent exact leaf test: no floating filter or spatial index.
            if (Exact.Orient(exact[t[0]],exact[t[1]],exact[id])>=0 && Exact.Orient(exact[t[1]],exact[t[2]],exact[id])>=0 && Exact.Orient(exact[t[2]],exact[t[0]],exact[id])>=0) hits.Add((ti,id));
        }
        hits.Sort();
        string canonical=string.Join("\n",hits.Select(p=>p.Item1+" "+p.Item2));
        return new PointIndexResult("exhaustive",mesh.Triangles.Length,0,0,0,0,0,hits.Count,Fingerprint(canonical));
    }
}
public sealed class PointIndexResult
{
    public string Mode { get; }
    public int Queries { get; }
    public double Seconds { get; }
    public double CpuSeconds { get; }
    public long Candidates { get; }
    public long Nodes { get; }
    public long Pruned { get; }
    public int Hits { get; }
    public string Fingerprint { get; }
    public PointIndexResult(string mode,int queries,double seconds,double cpuSeconds,long candidates,long nodes,long pruned,int hits,string fingerprint)
    { Mode=mode; Queries=queries; Seconds=seconds; CpuSeconds=cpuSeconds; Candidates=candidates; Nodes=nodes; Pruned=pruned; Hits=hits; Fingerprint=fingerprint; }
}
}
