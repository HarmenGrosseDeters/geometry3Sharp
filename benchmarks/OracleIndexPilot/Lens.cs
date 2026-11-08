using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;

/// <summary>A convex boundary that defeats endpoint-hull pruning: m points on each of two parabolas, y = ±h(1 - x²), sharing the two
/// tips. Every point is on the convex hull, so the root hull holds all of them, and every chord of the strip splits it.</summary>
public static class Lens
{
    public static (double[] x,double[] y) Points(int m)
    {
        // upper arc left to right (tips included), then the lower arc right to left without the tips: a CCW ring of 2m-2 points
        var x=new List<double>(); var y=new List<double>();
        for (int i=0;i<m;i++) { double t=-1+2.0*i/(m-1); x.Add(t); y.Add(0.25*(1-t*t)); }
        for (int i=m-2;i>=1;i--) { double t=-1+2.0*i/(m-1); x.Add(t); y.Add(-0.25*(1-t*t)); }
        // the ring above runs clockwise (upper arc left to right first): reverse it
        x.Reverse(); y.Reverse();
        return (x.ToArray(),y.ToArray());
    }
    /// <summary>The zigzag strip between the arcs: 2(m-2) triangles, positive, with the arcs, the verticals and the diagonals as edges.</summary>
    public static Mesh2Output Strip(int m)
    {
        var (px,py)=Points(m);
        int n=px.Length;
        // after the reversal: index of upper point i is (n-1)-i; lower point i (1..m-2) is i-1
        int U(int i)=>n-1-i; int L(int i)=>i==0?U(0):i==m-1?U(m-1):i-1;
        var tris=new List<int[]>();
        for (int i=0;i<m-1;i++) {
            if (i>0) tris.Add(new[]{L(i),L(i+1),U(i)});
            if (i<m-2) tris.Add(new[]{L(i+1),U(i+1),U(i)});
        }
        var mesh=new Mesh2Output { VX=px,VY=py,Triangles=tris.ToArray() };
        foreach (var t in mesh.Triangles)
            if (Exact.Orient(Pt.FromDoubles(px[t[0]],py[t[0]]),Pt.FromDoubles(px[t[1]],py[t[1]]),Pt.FromDoubles(px[t[2]],py[t[2]]))<=0) throw new InvalidOperationException("lens strip triangle not positive");
        return mesh;
    }
    public static long[] EdgeKeys(Mesh2Output mesh) =>
        mesh.Triangles.SelectMany(t=>new[]{StrtreeEdgeSearch.Key(t[0],t[1]),StrtreeEdgeSearch.Key(t[1],t[2]),StrtreeEdgeSearch.Key(t[2],t[0])}).ToArray();
    /// <summary>The lens as a mesher input: one outer ring of 2m-2 points, M1 (no angle bound).</summary>
    public static string Input(int m)
    {
        var (px,py)=Points(m);
        var input=new Mesh2Input { X=px,Y=py };
        input.Rings.Add(new RingInput(Enumerable.Range(0,px.Length).ToArray(),false));
        return TextFormat.WriteInput(input);
    }
    /// <summary>The other family: k parallel constraint chains along the diagonal (m points each, chains 1/(4k) apart across it) in
    /// a square. Its edges run along the diagonal, so their boxes overlap across all chains: a box sweep tests many pairs that the
    /// hull test separates by a line.</summary>
    public static string CombInput(int k,int m)
    {
        var input=new Mesh2Input(); var x=new List<double>{-1,3,3,-1}; var y=new List<double>{-1,-1,3,3};
        for (int j=0;j<k;j++) {
            double off=(j+0.5)/(4.0*k);
            for (int i=0;i<m;i++) {
                double t=0.25+1.5*i/(m-1);
                x.Add(t+off); y.Add(t-off);
                if (i>0) input.Segments.Add(new SegmentInput(x.Count-2,x.Count-1));
            }
        }
        input.X=x.ToArray(); input.Y=y.ToArray();
        input.Rings.Add(new RingInput(new[]{0,1,2,3},false));
        return TextFormat.WriteInput(input);
    }
}
