using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

namespace Meshing2Acceptance
{
    /// <summary>Experimental broad phase for the oracle's unchanged proper-crossing predicate.
    /// Each node hull contains every descendant segment, since it contains their endpoints.
    /// A closed-half-plane separation excludes proper crossings, including boundary-only contacts.
    /// Point/triangle and duplicate-coordinate checks retain responsibility for such contacts.</summary>
    public sealed class StrtreeEdgeSearch
    {
        readonly double[] x, y;
        readonly Pt[] exact;
        readonly long[] keys;
        static int A(long key) => (int)(key >> 32);
        static int B(long key) => (int)(uint)key;
        public static long Key(int a,int b) => a<b ? ((long)a<<32)|(uint)b : ((long)b<<32)|(uint)a;
        public StrtreeEdgeSearch(Mesh2Output mesh,long[] edges)
        {
            if (mesh.ExactVertices!=null) throw new ArgumentException("Binary64 pilot only");
            x=mesh.VX; y=mesh.VY;
            if (x.Length!=y.Length || x.Concat(y).Any(v=>double.IsNaN(v)||double.IsInfinity(v))) throw new ArgumentException("Finite matching coordinates required");
            exact=x.Select((v,i)=>Pt.FromDoubles(v,y[i])).ToArray();
            keys=edges.Distinct(EdgeKeys.Instance).OrderBy(k=>k).ToArray();
            if (keys.Any(k=>A(k)<0 || B(k)<0 || A(k)>=x.Length || B(k)>=x.Length)) throw new ArgumentException("Invalid edge index");
        }
        // Existing oracle OrientV filter, unchanged; no new error bound or epsilon.
        int Orient(int a,int b,int c)
        {
            double ax=x[a], ay=y[a];
            double l=(x[b]-ax)*(y[c]-ay), rr=(y[b]-ay)*(x[c]-ax);
            double det=l-rr, sum=Math.Abs(l)+Math.Abs(rr), bound=1e-15*sum;
            if (sum>1e-200 && sum<1e200 && Math.Abs(det)>bound) return det>0?1:-1;
            return Exact.Orient(exact[a],exact[b],exact[c]);
        }
        bool Proper(int a,int b,int c,int d) => Orient(a,b,c)*Orient(a,b,d)<0 && Orient(c,d,a)*Orient(c,d,b)<0;
        Envelope Box(int index)
        {
            int a=A(keys[index]), b=B(keys[index]);
            return new Envelope(x[a],x[b],y[a],y[b]);
        }
        int[] Hull(IEnumerable<int> source)
        {
            var sorted=source.Distinct().OrderBy(i=>x[i]).ThenBy(i=>y[i]).ThenBy(i=>i).ToArray();
            var unique=new List<int>();
            foreach (int id in sorted)
                if (unique.Count==0 || x[id]!=x[unique[unique.Count-1]] || y[id]!=y[unique[unique.Count-1]]) unique.Add(id);
            if (unique.Count<=2) return unique.ToArray();
            var lower=new List<int>(); var upper=new List<int>();
            foreach (int id in unique) {
                while (lower.Count>=2 && Orient(lower[lower.Count-2],lower[lower.Count-1],id)<=0) lower.RemoveAt(lower.Count-1);
                lower.Add(id);
            }
            for (int i=unique.Count-1;i>=0;i--) {
                int id=unique[i];
                while (upper.Count>=2 && Orient(upper[upper.Count-2],upper[upper.Count-1],id)<=0) upper.RemoveAt(upper.Count-1);
                upper.Add(id);
            }
            lower.RemoveAt(lower.Count-1); upper.RemoveAt(upper.Count-1);
            lower.AddRange(upper); return lower.ToArray();
        }
        sealed class Node
        {
            public Envelope Box;
            public Node[] Children;
            public int Item, MaxItem;
            public int[] Hull;
        }
        Node Wrap(IBoundable<Envelope,int> source)
        {
            var node=new Node { Box=source.Bounds };
            if (source is AbstractNode<Envelope,int> branch) {
                node.Children=branch.ChildBoundables.Select(Wrap).ToArray();
                node.MaxItem=node.Children.Length==0?-1:node.Children.Max(c=>c.MaxItem);
                node.Hull=Hull(node.Children.SelectMany(c=>c.Hull));
            } else {
                node.Item=source.Item; node.MaxItem=node.Item;
                int a=A(keys[node.Item]), b=B(keys[node.Item]);
                node.Hull=x[a]==x[b] && y[a]==y[b] ? new[]{a} : new[]{a,b};
            }
            return node;
        }
        bool Separated(int a,int b,Node node,out bool line)
        {
            bool positive=false, negative=false;
            foreach (int id in node.Hull) {
                int sign=id==a || id==b ? 0 : Orient(a,b,id); positive|=sign>0; negative|=sign<0;
                if (positive && negative) break;
            }
            // Every descendant endpoint is on one closed side of the query line.
            // A proper crossing needs strict opposite signs; zero-only contacts are safe here.
            line=!(positive && negative);
            if (line) return true;
            for (int i=0;i<node.Hull.Length;i++) {
                int u=node.Hull[i], v=node.Hull[(i+1)%node.Hull.Length];
                // Hull edges are CCW; its entire content is in their closed left half-planes.
                // The query segment in a closed right half-plane cannot cross a node segment properly.
                if (Orient(u,v,a)<=0 && Orient(u,v,b)<=0) return true;
            }
            return false;
        }
        sealed class Visitor : IItemVisitor<int>
        {
            public Action<int> Visit;
            public void VisitItem(int id) => Visit(id);
        }
        static int[] Ranks(double[] values)
        {
            var order=Enumerable.Range(0,values.Length).OrderBy(i=>values[i]).ToArray();
            var ranks=new int[values.Length]; int rank=0;
            for (int i=0;i<order.Length;i++) {
                if (i>0 && values[order[i]]!=values[order[i-1]]) rank++;
                ranks[order[i]]=rank;
            }
            return ranks;
        }
        public EdgeIndexResult Run(string mode,int queryCount=0) => RunCore(mode,queryCount,out _);
        public IReadOnlyList<(long,long)> FindCrossings()
        {
            RunCore("hulls",0,out var found); return found;
        }
        EdgeIndexResult RunCore(string mode,int queryCount,out List<(long,long)> found)
        {
            if (mode!="boxes" && mode!="nts" && mode!="hulls") throw new ArgumentException("Unknown edge mode");
            int count=queryCount<=0?keys.Length:Math.Min(queryCount,keys.Length);
            var queries=Enumerable.Range(0,count).Select(i=>(int)((long)i*keys.Length/count)).ToArray();
            var hits=new List<(long,long)>(); long candidates=0,nodes=0,linePruned=0,hullPruned=0;
            void Check(int i,int j) {
                if (j<=i) return;
                int a=A(keys[i]),b=B(keys[i]),c=A(keys[j]),d=B(keys[j]);
                if (a==c || a==d || b==c || b==d) return;
                candidates++;
                if (Proper(a,b,c,d)) hits.Add((keys[i],keys[j]));
            }
            using var process=Process.GetCurrentProcess(); var cpu=process.TotalProcessorTime; var watch=Stopwatch.StartNew();
            if (mode=="boxes") {
                var rx=Ranks(x);var ry=Ranks(y);
                var boxes=keys.Select(k=>new[]{Math.Min(rx[A(k)],rx[B(k)]),Math.Max(rx[A(k)],rx[B(k)]),Math.Min(ry[A(k)],ry[B(k)]),Math.Max(ry[A(k)],ry[B(k)])}).ToArray();
                if (count==keys.Length) BoxPairs.Overlaps(boxes,boxes,true,(i,j)=>Check(Math.Min(i,j),Math.Max(i,j)));
                else BoxPairs.Overlaps(queries.Select(i=>boxes[i]).ToArray(),boxes,false,(qi,j)=>Check(queries[qi],j));
            } else if (count>0) {
                var tree=new STRtree<int>();
                for (int i=0;i<keys.Length;i++) tree.Insert(Box(i),i);
                tree.Build();
                if (mode=="nts") {
                    var visitor=new Visitor();
                    foreach (int i in queries) { visitor.Visit=j=>Check(i,j);tree.Query(Box(i),visitor); }
                } else {
                    var root=Wrap(tree.Root);
                    void Walk(Node node,int i,Envelope box) {
                        nodes++;
                        if (node.MaxItem<=i || !node.Box.Intersects(box)) return;
                        if (node.Children==null) { Check(i,node.Item);return; }
                        if (Separated(A(keys[i]),B(keys[i]),node,out bool line)) {
                            if (line) linePruned++;else hullPruned++;
                            return;
                        }
                        foreach (var child in node.Children) Walk(child,i,box);
                    }
                    foreach (int i in queries) Walk(root,i,Box(i));
                }
            }
            watch.Stop();process.Refresh();
            hits.Sort();found=hits;
            return new EdgeIndexResult(mode,count,watch.Elapsed.TotalSeconds,(process.TotalProcessorTime-cpu).TotalSeconds,candidates,nodes,linePruned,hullPruned,hits.Count,Fingerprint(hits));
        }
        static string Fingerprint(List<(long,long)> hits)
        {
            string text=string.Join("\n",hits.Select(p=>p.Item1+" "+p.Item2));
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
        }
        public EdgeIndexResult Exhaustive()
        {
            var hits=new List<(long,long)>();
            for (int i=0;i<keys.Length;i++) for (int j=i+1;j<keys.Length;j++) {
                int a=A(keys[i]),b=B(keys[i]),c=A(keys[j]),d=B(keys[j]);
                if (a==c || a==d || b==c || b==d) continue;
                int O(int u,int v,int p)=>Exact.Orient(exact[u],exact[v],exact[p]);
                if (O(a,b,c)*O(a,b,d)<0 && O(c,d,a)*O(c,d,b)<0) hits.Add((keys[i],keys[j]));
            }
            hits.Sort();return new EdgeIndexResult("exhaustive",keys.Length,0,0,0,0,0,0,hits.Count,Fingerprint(hits));
        }
    }
    public sealed class EdgeIndexResult
    {
        public string Mode { get; }
        public int Queries { get; }
        public double Seconds { get; }
        public double CpuSeconds { get; }
        public long Candidates { get; }
        public long Nodes { get; }
        public long LinePruned { get; }
        public long HullPruned { get; }
        public int Hits { get; }
        public string Fingerprint { get; }
        public EdgeIndexResult(string mode,int queries,double seconds,double cpuSeconds,long candidates,long nodes,long linePruned,long hullPruned,int hits,string fingerprint)
        { Mode=mode;Queries=queries;Seconds=seconds;CpuSeconds=cpuSeconds;Candidates=candidates;Nodes=nodes;LinePruned=linePruned;HullPruned=hullPruned;Hits=hits;Fingerprint=fingerprint; }
    }
}
