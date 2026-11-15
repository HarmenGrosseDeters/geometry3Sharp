// Replays only the production assembly's loss guards for a single compact grid.
// This is diagnostic evidence, not a second solver or a correctness certificate.
using System;
using System.Collections.Generic;
using g3;

static class ProductionAssemblyDiagnostic
{
    static double MaxAbs(Vector3d v) => Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));

    static bool Dot(Vector3d a, Vector3d b, int triangle, string label, out double result)
    {
        result=0;
        for (int i=0;i<3;i++) {
            double term=a[i]*b[i], sum=result+term;
            if ((term==0 && a[i]!=0 && b[i]!=0) || !MathUtil.IsFinite(sum) ||
                (term!=0 && result!=0 && (sum==result || sum==term))) {
                Console.WriteLine($"replayed_assembly_guard=dot triangle={triangle} label={label} axis={i} accumulated={result:R} term={term:R} operand_a={a[i]:R} operand_b={b[i]:R}");
                return false;
            }
            result=sum;
        }
        return true;
    }

    static bool Add(Dictionary<int,double>[] rows, int row, int col, double value, int triangle)
    {
        rows[row].TryGetValue(col,out double old); double sum=old+value;
        if (!MathUtil.IsFinite(sum) || (value!=0 && old!=0 && (sum==old || sum==value))) {
            Console.WriteLine($"replayed_assembly_guard=row triangle={triangle} row={row} col={col} accumulated={old:R} term={value:R}");
            return false;
        }
        rows[row][col]=sum;return true;
    }

    static bool Edge(Dictionary<int,double>[] rows,int a,int b,double weight,int triangle) =>
        Add(rows,a,a,weight,triangle) && Add(rows,b,b,weight,triangle) &&
        Add(rows,a,b,-weight,triangle) && Add(rows,b,a,-weight,triangle);

    public static void Explain(DMesh3 mesh)
    {
        int n=mesh.VertexCount; var points=new Vector3d[n]; var rows=new Dictionary<int,double>[n];
        var origin=mesh.GetVertex(0); double scale=0;
        for(int i=0;i<n;i++) { points[i]=mesh.GetVertex(i)-origin;scale=Math.Max(scale,MaxAbs(points[i]));rows[i]=new Dictionary<int,double>(); }
        if (!(scale>0) || !MathUtil.IsFinite(scale)) { Console.WriteLine("replayed_assembly_guard=unknown reason=normalization");return; }
        for(int i=0;i<n;i++)points[i]/=scale;
        foreach(int tid in mesh.TriangleIndices()) {
            var t=mesh.GetTriangle(tid);var ab=points[t.b]-points[t.a];var ac=points[t.c]-points[t.a];
            var cross=ab.Cross(ac);double m=MaxAbs(cross);
            if (!(m>0) || !MathUtil.IsFinite(m)) { Console.WriteLine($"replayed_assembly_guard=unknown triangle={tid} reason=area");return; }
            cross/=m;double area=m*Math.Sqrt(cross.Dot(cross));
            if (!(area>0) || !MathUtil.IsFinite(area)) { Console.WriteLine($"replayed_assembly_guard=unknown triangle={tid} reason=area");return; }
            if (!Dot(ab,ac,tid,"a",out double da) || !Dot(-ab,ac-ab,tid,"b",out double db) || !Dot(-ac,ab-ac,tid,"c",out double dc)) return;
            double wa=0.5*da/area, wb=0.5*db/area, wc=0.5*dc/area, share=area/6;
            if (!MathUtil.IsFinite(wa) || !MathUtil.IsFinite(wb) || !MathUtil.IsFinite(wc) || !(share>0)) {
                Console.WriteLine($"replayed_assembly_guard=unknown triangle={tid} reason=weight_or_mass");return;
            }
            if (!Edge(rows,t.b,t.c,wa,tid) || !Edge(rows,t.a,t.c,wb,tid) || !Edge(rows,t.a,t.b,wc,tid)) return;
        }
        Console.WriteLine("replayed_assembly_guard=not_reproduced");
    }
}
