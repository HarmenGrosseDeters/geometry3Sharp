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
    /// <summary>Certified broad phase for the oracle's unchanged proper-crossing predicate.
    /// Each node hull contains every descendant segment, since it contains their endpoints.
    /// A closed-half-plane separation excludes proper crossings, including boundary-only contacts.
    /// Point/triangle and duplicate-coordinate checks retain responsibility for such contacts.
    /// The cost per visited node is bounded: a node keeps a containing polygon of at most <see cref="HullLimit"/> vertices (the
    /// exact hull where it is that small, else the corners of its closed box), so the walk costs a constant per node that a box
    /// query visits (on a convex boundary the root's exact hull holds nearly every point, and testing all of it per node made the
    /// search O(E·h), review of #411).</summary>
    public sealed class StrtreeEdgeSearch
    {
        readonly double[] x, y;
        readonly Pt[] exact;
        readonly long[] keys;
        /// <summary>The largest polygon a node tests; a node whose hull would be larger tests its four box corners instead.</summary>
        public const int HullLimit = 16;
        static int A(long key) => (int)(key >> 32);
        static int B(long key) => (int)(uint)key;
        public static long Key(int a,int b) => a<b ? ((long)a<<32)|(uint)b : ((long)b<<32)|(uint)a;
        public StrtreeEdgeSearch(Mesh2Output mesh,long[] edges)
        {
            // Self-checks, not verdicts (see StrtreePointSearch's constructor): Conformity calls this only on the fast path
            // (ExactVertices == null), after Run.Vertices() rejected non-finite coordinates and after EdgesAndNeighbours() built the
            // edge keys from the triangles' own indices; Execute returns after Orientation() when !r.Ok. A throw means that coupling broke.
            if (mesh.ExactVertices!=null) throw new ArgumentException("Binary64 pilot only");
            x=mesh.VX; y=mesh.VY;
            if (x.Length!=y.Length || x.Concat(y).Any(v=>double.IsNaN(v)||double.IsInfinity(v))) throw new ArgumentException("Finite matching coordinates required");
            exact=x.Select((v,i)=>Pt.FromDoubles(v,y[i])).ToArray();
            keys=edges.Distinct(EdgeKeys.Instance).OrderBy(k=>k).ToArray();
            if (keys.Any(k=>A(k)<0 || B(k)<0 || A(k)>=x.Length || B(k)>=x.Length)) throw new ArgumentException("Invalid edge index");
        }
        // Existing oracle OrientV filter, unchanged; no new error bound or epsilon.
        int Orient(int a,int b,double cx,double cy,Pt c)
        {
            double ax=x[a], ay=y[a];
            double l=(x[b]-ax)*(cy-ay), rr=(y[b]-ay)*(cx-ax);
            double det=l-rr, sum=Math.Abs(l)+Math.Abs(rr), bound=1e-15*sum;
            if (sum>1e-200 && sum<1e200 && Math.Abs(det)>bound) return det>0?1:-1;
            return Exact.Orient(exact[a],exact[b],c);
        }
        int Orient(int a,int b,int c) => Orient(a,b,x[c],y[c],exact[c]);
        bool Proper(int a,int b,int c,int d) => Orient(a,b,c)*Orient(a,b,d)<0 && Orient(c,d,a)*Orient(c,d,b)<0;
        Envelope Box(int index)
        {
            int a=A(keys[index]), b=B(keys[index]);
            return new Envelope(x[a],x[b],y[a],y[b]);
        }
        sealed class Node
        {
            public Envelope Box;
            public Node[] Children;
            public int Item, MaxItem;
            /// <summary>A convex polygon (CCW) of at most max(HullLimit, 4) vertices that contains every descendant endpoint, as point
            /// handles (see Tree): the exact endpoint hull where that is small, else a coarser one (see Tree.Wrap).</summary>
            public int[] Hull;
        }
        /// <summary>The certified hierarchy of one run. A point handle h is vertex h if h ≥ 0, else corner -1-h of a child's box, whose
        /// exact point is made only if the filter needs it. Built per run, so concurrent runs share nothing mutable.</summary>
        sealed class Tree
        {
            readonly StrtreeEdgeSearch s;
            readonly List<double> cx=new List<double>(), cy=new List<double>();
            readonly List<Pt?> cp=new List<Pt?>();
            public Tree(StrtreeEdgeSearch search) { s=search; }
            /// <summary>The nodes whose polygon is their box (their hull had more than HullLimit vertices).</summary>
            public long Coarse;
            double X(int h) => h>=0 ? s.x[h] : cx[-1-h];
            double Y(int h) => h>=0 ? s.y[h] : cy[-1-h];
            Pt P(int h)
            {
                if (h>=0) return s.exact[h];
                // (an NTS envelope's bounds are the input doubles themselves: the corner is exact)
                return cp[-1-h] ??= Pt.FromDoubles(cx[-1-h],cy[-1-h]);
            }
            int Corner(double px,double py) { cx.Add(px); cy.Add(py); cp.Add(null); return -cx.Count; }
            /// <summary>The oracle's OrientV filter, unchanged, on point handles; no new error bound or epsilon.</summary>
            public int Orient(int u,int v,int c)
            {
                double ux=X(u), uy=Y(u), vx=X(v), vy=Y(v), qx=X(c), qy=Y(c);
                // three points on one vertical or horizontal line: exactly collinear (box corners often are; spares the exact fallback)
                if ((ux==vx && vx==qx) || (uy==vy && vy==qy)) return 0;
                double l=(vx-ux)*(qy-uy), rr=(vy-uy)*(qx-ux);
                double det=l-rr, sum=Math.Abs(l)+Math.Abs(rr), bound=1e-15*sum;
                if (sum>1e-200 && sum<1e200 && Math.Abs(det)>bound) return det>0?1:-1;
                return Exact.Orient(P(u),P(v),P(c));
            }
            /// <summary>The convex hull (CCW, Andrew's monotone chain) of the given points, without repeated coordinates.</summary>
            int[] Hull(List<int> source)
            {
                source.Sort((p,q)=>{ double px=X(p),qx=X(q); if (px!=qx) return px.CompareTo(qx); double py=Y(p),qy=Y(q); return py!=qy?py.CompareTo(qy):p.CompareTo(q); });
                var unique=new List<int>();
                foreach (int p in source)
                    if (unique.Count==0 || X(p)!=X(unique[unique.Count-1]) || Y(p)!=Y(unique[unique.Count-1])) unique.Add(p);
                if (unique.Count<=2) return unique.ToArray();
                var lower=new List<int>(); var upper=new List<int>();
                foreach (int p in unique) {
                    while (lower.Count>=2 && Orient(lower[lower.Count-2],lower[lower.Count-1],p)<=0) lower.RemoveAt(lower.Count-1);
                    lower.Add(p);
                }
                for (int i=unique.Count-1;i>=0;i--) {
                    int p=unique[i];
                    while (upper.Count>=2 && Orient(upper[upper.Count-2],upper[upper.Count-1],p)<=0) upper.RemoveAt(upper.Count-1);
                    upper.Add(p);
                }
                lower.RemoveAt(lower.Count-1); upper.RemoveAt(upper.Count-1);
                lower.AddRange(upper); return lower.ToArray();
            }
            /// <summary>Wraps an NTS node. Its polygon is the convex hull of its children's polygons if that has at most HullLimit
            /// vertices, else the four corners of its closed box (which contains that hull). By induction every polygon contains the node's
            /// endpoints and has at most max(HullLimit, 4) vertices: the test per visited node is O(HullLimit), and the build is linear
            /// in the tree size (an exact endpoint hull at every level was O(n log n) per level, and O(h) per visit).</summary>
            public Node Wrap(IBoundable<Envelope,int> source)
            {
                var node=new Node { Box=source.Bounds };
                if (source is AbstractNode<Envelope,int> branch) {
                    var children=new Node[branch.ChildBoundables.Count]; var points=new List<int>();
                    for (int c=0;c<children.Length;c++) { children[c]=Wrap(branch.ChildBoundables[c]); points.AddRange(children[c].Hull); }
                    node.Children=children;
                    node.MaxItem=-1;
                    foreach (var c in children) node.MaxItem=Math.Max(node.MaxItem,c.MaxItem);
                    node.Hull=Hull(points);
                } else {
                    node.Item=source.Item; node.MaxItem=node.Item;
                    int a=A(s.keys[node.Item]), b=B(s.keys[node.Item]);
                    node.Hull=s.x[a]==s.x[b] && s.y[a]==s.y[b] ? new[]{a} : new[]{a,b};
                }
                if (node.Hull.Length>HullLimit) {
                    // the closed box contains the polygon (and so every descendant endpoint); its corners in CCW order. A hull of more than
                    // HullLimit ≥ 3 vertices has three non-collinear points, so the box has positive width and height: four distinct
                    // corners (a repeated corner would make a zero-length edge whose orientation 0 "separates" everything)
                    var e=node.Box; Coarse++;
                    node.Hull=new[] { Corner(e.MinX,e.MinY),Corner(e.MaxX,e.MinY),Corner(e.MaxX,e.MaxY),Corner(e.MinX,e.MaxY) };
                }
                return node;
            }
            public bool Separated(int a,int b,Node node,ref long pruneTests,out bool line)
            {
                bool positive=false, negative=false;
                foreach (int p in node.Hull) {
                    pruneTests++;
                    int sign=p==a || p==b ? 0 : Orient(a,b,p); positive|=sign>0; negative|=sign<0;
                    if (positive && negative) break;
                }
                // Every descendant endpoint is in the polygon, so on one closed side of the query line (a half-plane is convex).
                // A proper crossing needs strict opposite signs; zero-only contacts are safe here.
                line=!(positive && negative);
                if (line) return true;
                for (int i=0;i<node.Hull.Length;i++) {
                    int u=node.Hull[i], v=node.Hull[(i+1)%node.Hull.Length];
                    pruneTests+=2;
                    // Polygon edges are CCW; its entire content is in their closed left half-planes.
                    // The query segment in a closed right half-plane cannot cross a node segment properly.
                    if (Orient(u,v,a)<=0 && Orient(u,v,b)<=0) return true;
                }
                return false;
            }
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
            var hits=new List<(long,long)>(); long candidates=0,nodes=0,linePruned=0,hullPruned=0,pruneTests=0,coarse=0;
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
                    var hierarchy=new Tree(this); var root=hierarchy.Wrap(tree.Root); coarse=hierarchy.Coarse;
                    void Walk(Node node,int i,Envelope box) {
                        nodes++;
                        if (node.MaxItem<=i || !node.Box.Intersects(box)) return;
                        if (node.Children==null) { Check(i,node.Item);return; }
                        if (hierarchy.Separated(A(keys[i]),B(keys[i]),node,ref pruneTests,out bool line)) {
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
            return new EdgeIndexResult(mode,count,watch.Elapsed.TotalSeconds,(process.TotalProcessorTime-cpu).TotalSeconds,candidates,nodes,linePruned,hullPruned,hits.Count,Fingerprint(hits)) { PruneTests=pruneTests,CoarseNodes=coarse };
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
        /// <summary>Orientation tests made by the pruning certificates (hull vertices, box corners, hull edges): with Nodes the walk's work.</summary>
        public long PruneTests { get; set; }
        /// <summary>Inner nodes that test their box corners because their hull exceeded the hull limit.</summary>
        public long CoarseNodes { get; set; }
        public EdgeIndexResult(string mode,int queries,double seconds,double cpuSeconds,long candidates,long nodes,long linePruned,long hullPruned,int hits,string fingerprint)
        { Mode=mode;Queries=queries;Seconds=seconds;CpuSeconds=cpuSeconds;Candidates=candidates;Nodes=nodes;LinePruned=linePruned;HullPruned=hullPruned;Hits=hits;Fingerprint=fingerprint; }
    }
}
