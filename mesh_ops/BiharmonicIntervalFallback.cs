// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    // Dense outward-rounded enclosure used only after the ordinary assembly detects a
    // completely lost contribution. Refuse systems too large or intervals too wide.
    internal static class BiharmonicIntervalFallback
    {
        const int MaxVertices = 16;
        static ExactInterval Z { get { return ExactInterval.Of(0); } }
        static ExactInterval One { get { return ExactInterval.Of(1); } }
        static ExactInterval Whole { get { return new ExactInterval(double.NegativeInfinity, double.PositiveInfinity); } }
        static bool Finite(ExactInterval x) { return MathUtil.IsFinite(x.Lo) && MathUtil.IsFinite(x.Hi) && x.Lo <= x.Hi; }
        static ExactInterval Div(ExactInterval a, ExactInterval b)
        {
            if (!Finite(a) || !Finite(b) || (b.Lo <= 0 && b.Hi >= 0)) return Whole;
            double[] aa = { a.Lo, a.Hi }, bb = { b.Lo, b.Hi };
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i < 2; ++i) for (int j = 0; j < 2; ++j) {
                double v = ExactDyadic.Quotient(ExactDyadic.Of(aa[i]), ExactDyadic.Of(bb[j]));
                if (!MathUtil.IsFinite(v)) return Whole;
                lo = Math.Min(lo, ExactInterval.Down(v)); hi = Math.Max(hi, ExactInterval.Up(v));
            }
            return new ExactInterval(lo, hi);
        }
        static bool SqrtBound(double x, bool upper, out double result)
        {
            result = Math.Sqrt(x);
            if (!MathUtil.IsFinite(result)) return false;
            ExactDyadic v = ExactDyadic.Of(x);
            for (int i = 0; i < 16; ++i) {
                ExactDyadic t = ExactDyadic.Of(result);
                int cmp = ExactDyadic.Compare(t * t, v);
                if (upper ? cmp >= 0 : cmp <= 0) return true;
                result = upper ? ExactInterval.Up(result) : ExactInterval.Down(result);
            }
            return false;
        }
        static ExactInterval Sqrt(ExactInterval x)
        {
            if (!Finite(x) || x.Hi < 0) return Whole;
            double lo, hi;
            if (!SqrtBound(Math.Max(0, x.Lo), false, out lo) || !SqrtBound(x.Hi, true, out hi)) return Whole;
            return new ExactInterval(lo, hi);
        }
        static double MaxAbs(Vector3d v) { return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))); }
        static ExactInterval AbsSquared(ExactInterval[] x) {
            return ExactInterval.Square(x[0]) + ExactInterval.Square(x[1]) + ExactInterval.Square(x[2]);
        }
        static ExactInterval Dot(ExactInterval[] a, ExactInterval[] b) {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
        static ExactInterval[] Sub(ExactInterval[] a, ExactInterval[] b) {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }
        static ExactInterval[] Cross(ExactInterval[] a, ExactInterval[] b) {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }
        static void Edge(ExactInterval[,] l, int a, int b, ExactInterval w) {
            l[a,a] = l[a,a] + w; l[b,b] = l[b,b] + w; l[a,b] = l[a,b] - w; l[b,a] = l[b,a] - w;
        }
        // The output enclosure is required to be within RelativeTolerance times the
        // component's rest scale plus eight ULPs of the final world coordinate.
        internal static bool TrySolve(DMesh3 mesh, List<int> ids, Dictionary<int,int> lookup,
            Vector3d[] original, int[] root, bool[] active, int[] free, int count,
            IReadOnlyDictionary<int,Vector3d> handles, double tolerance, out Vector3d[] output, out double error)
        {
            output = null; error = 0; int n = ids.Count;
            if (n > MaxVertices || count == 0) return false;
            var scales = new double[n];
            for (int i = 0; i < n; ++i) if (active[i]) {
                Vector3d delta = original[i] - original[root[i]];
                if (!delta.IsFinite) return false;
                scales[root[i]] = Math.Max(scales[root[i]], MaxAbs(delta));
            }
            for (int i = 0; i < n; ++i) if (active[i] && !(scales[root[i]] > 0)) return false;
            var p = new ExactInterval[n][]; var d = new ExactInterval[n][];
            for (int i = 0; i < n; ++i) {
                p[i] = new[] { Z, Z, Z }; d[i] = new[] { Z, Z, Z };
                if (!active[i]) continue;
                var scale = ExactInterval.Of(scales[root[i]]);
                Vector3d target;
                bool fixedVertex = handles.TryGetValue(ids[i], out target);
                for (int k = 0; k < 3; ++k) {
                    p[i][k] = Div(ExactInterval.Of(original[i][k]) - ExactInterval.Of(original[root[i]][k]), scale);
                    if (fixedVertex) d[i][k] = Div(ExactInterval.Of(target[k]) - ExactInterval.Of(original[i][k]), scale);
                    if (!Finite(p[i][k]) || !Finite(d[i][k])) return false;
                }
            }
            var l = new ExactInterval[n,n]; var mass = new ExactInterval[n];
            for (int i=0;i<n;++i) { mass[i]=Z; for(int j=0;j<n;++j) l[i,j]=Z; }
            foreach (int tid in mesh.TriangleIndices()) {
                var t=mesh.GetTriangle(tid); int a=lookup[t.a], b=lookup[t.b], c=lookup[t.c];
                if (!active[a]) {
                    Vector3d x0=original[a], x1=original[b], x2=original[c];
                    if (ExactPredicates.Orient2d(new Vector2d(x0.x,x0.y),new Vector2d(x1.x,x1.y),new Vector2d(x2.x,x2.y)) == 0 &&
                        ExactPredicates.Orient2d(new Vector2d(x0.x,x0.z),new Vector2d(x1.x,x1.z),new Vector2d(x2.x,x2.z)) == 0 &&
                        ExactPredicates.Orient2d(new Vector2d(x0.y,x0.z),new Vector2d(x1.y,x1.z),new Vector2d(x2.y,x2.z)) == 0) return false;
                    continue;
                }
                var ab=Sub(p[b],p[a]); var ac=Sub(p[c],p[a]); var bc=Sub(p[c],p[b]);
                var ba=Sub(p[a],p[b]); var ca=Sub(p[a],p[c]); var cb=Sub(p[b],p[c]);
                var area=Sqrt(AbsSquared(Cross(ab,ac)));
                if (!Finite(area) || !(area.Lo > 0)) return false;
                var wa=Div(Dot(ab,ac),area)*ExactInterval.Of(0.5);
                var wb=Div(Dot(ba,bc),area)*ExactInterval.Of(0.5);
                var wc=Div(Dot(ca,cb),area)*ExactInterval.Of(0.5);
                var share=Div(area,ExactInterval.Of(6));
                if (!Finite(wa)||!Finite(wb)||!Finite(wc)||!Finite(share)||!(share.Lo>0)) return false;
                Edge(l,b,c,wa); Edge(l,a,c,wb); Edge(l,a,b,wc);
                mass[a]=mass[a]+share; mass[b]=mass[b]+share; mass[c]=mass[c]+share;
            }
            var q=new ExactInterval[count,count]; var rhs=new ExactInterval[count,3];
            for(int i=0;i<count;++i) { for(int j=0;j<count;++j) q[i,j]=Z; for(int k=0;k<3;++k) rhs[i,k]=Z; }
            for(int k=0;k<n;++k) if(active[k]) {
                if(!Finite(mass[k]) || !(mass[k].Lo>0)) return false;
                for(int i=0;i<n;++i) if(free[i]>=0) {
                    var factor=Div(l[k,i],mass[k]);
                    if(!Finite(factor)) return false;
                    for(int j=0;j<n;++j) {
                        var term=factor*l[k,j];
                        if(free[j]>=0) q[free[i],free[j]]=q[free[i],free[j]]+term;
                        else if(active[j]) for(int axis=0;axis<3;++axis) rhs[free[i],axis]=rhs[free[i],axis]-term*d[j][axis];
                    }
                }
            }
            // Interval Gaussian elimination. A pivot containing zero cannot certify the solve.
            for(int i=0;i<count;++i) {
                if(!Finite(q[i,i]) || q[i,i].Lo<=0) return false;
                for(int j=i+1;j<count;++j) {
                    var f=Div(q[j,i],q[i,i]); if(!Finite(f)) return false;
                    for(int k=i+1;k<count;++k) q[j,k]=q[j,k]-f*q[i,k];
                    for(int axis=0;axis<3;++axis) rhs[j,axis]=rhs[j,axis]-f*rhs[i,axis];
                    q[j,i]=Z;
                }
            }
            var x=new ExactInterval[count,3];
            for(int i=count-1;i>=0;--i) for(int axis=0;axis<3;++axis) {
                var value=rhs[i,axis]; for(int j=i+1;j<count;++j) value=value-q[i,j]*x[j,axis];
                x[i,axis]=Div(value,q[i,i]); if(!Finite(x[i,axis])) return false;
            }
            output=new Vector3d[n];
            for(int i=0;i<n;++i) {
                Vector3d target;
                if(handles.TryGetValue(ids[i],out target)) { output[i]=target; continue; }
                if(!active[i]) return false;
                for(int axis=0;axis<3;++axis) {
                    var world=ExactInterval.Of(original[i][axis])+ExactInterval.Of(scales[root[i]])*x[free[i],axis];
                    if(!Finite(world)) return false;
                    double mid=world.Lo/2+world.Hi/2;
                    double width=ExactInterval.Up(Math.Max(mid-world.Lo,world.Hi-mid));
                    double rounding=Math.Max(Math.Abs(ExactInterval.Up(mid)-mid),Math.Abs(mid-ExactInterval.Down(mid)));
                    double allowed=ExactInterval.Down(tolerance*scales[root[i]] + 8*rounding);
                    if(!MathUtil.IsFinite(mid)||!MathUtil.IsFinite(width)||!MathUtil.IsFinite(allowed)||width>allowed) return false;
                    output[i][axis]=mid; error=Math.Max(error,ExactInterval.Up(width/scales[root[i]]));
                }
            }
            return MathUtil.IsFinite(error);
        }
    }
}
