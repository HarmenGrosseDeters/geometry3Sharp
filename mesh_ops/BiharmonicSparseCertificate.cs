// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;
using I = g3.BiharmonicFixedInterval;

namespace g3
{
    // Bounded reference verifier: one connected active component, <=400 vertices.
    // Rest proof is snapshotted from original binary64; every final-position
    // residual is evaluated afresh. No timestamp-only cache validation.
    internal static class BiharmonicSparseCertificate
    {
        const long MaxUpdates = 1000000;
        const int MaxEntries = 50000;
        static bool SameBits(Vector3d a,Vector3d b) => BitConverter.DoubleToInt64Bits(a.x)==BitConverter.DoubleToInt64Bits(b.x) &&
            BitConverter.DoubleToInt64Bits(a.y)==BitConverter.DoubleToInt64Bits(b.y) && BitConverter.DoubleToInt64Bits(a.z)==BitConverter.DoubleToInt64Bits(b.z);
        static I Zero => new I(0, 0);
        static void Add(Dictionary<int,I> row,int col,I value) {
            I old; if(!row.TryGetValue(col,out old)) old=Zero; row[col]=old+value;
        }
        static I[] Sub(I[] a,I[] b) => new[] {a[0]-b[0],a[1]-b[1],a[2]-b[2]};
        static I Dot(I[] a,I[] b) => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        static I[] Cross(I[] a,I[] b) => new[] {a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        static I Square(I x) {
            var p=x*x;return new I(x.Lo.Sign<=0 && x.Hi.Sign>=0 ? BigInteger.Zero : BigInteger.Max(BigInteger.Zero,p.Lo),p.Hi);
        }
        static void Edge(Dictionary<int,I>[] rows,int a,int b,I weight) {
            Add(rows[a],a,weight);Add(rows[b],b,weight);
            var negative=new I(-weight.Hi,-weight.Lo);Add(rows[a],b,negative);Add(rows[b],a,negative);
        }
        internal static bool TryVerify(DMesh3 mesh,List<int> ids,Dictionary<int,int> lookup,
            Vector3d[] original,Vector3d[] candidate,int[] roots,bool[] active,int[] free,int count,
            IReadOnlyDictionary<int,Vector3d> handles,double positionTolerance,double responseTolerance,out double bound)
        {
            Prepared prepared = null;
            return TryVerify(mesh,ids,lookup,original,candidate,roots,active,free,count,handles,positionTolerance,responseTolerance,out bound,ref prepared);
        }
        internal static bool TryVerify(DMesh3 mesh,List<int> ids,Dictionary<int,int> lookup,
            Vector3d[] original,Vector3d[] candidate,int[] roots,bool[] active,int[] free,int count,
            IReadOnlyDictionary<int,Vector3d> handles,double positionTolerance,double responseTolerance,out double bound,ref Prepared prepared)
        {
            bound=double.NaN;int n=ids.Count;
            if(n<3 || n>400 || mesh.VertexCount!=n || mesh.TriangleCount>2000 || count<1 || count>=n) return false;
            // The caller supplies these arrays from its validated traversal. Refuse
            // multiple components until separate resource/accounting is implemented.
            for(int i=0;i<n;i++) {
                if(!active[i] || roots[i]!=0 || !original[i].IsFinite || !candidate[i].IsFinite)return false;
                if(!mesh.IsVertex(ids[i])||!SameBits(original[i],mesh.GetVertex(ids[i]))) {prepared=null;return false;}
            }
            double scale=0;
            for(int i=0;i<n;i++) {
                var d=original[i]-original[0];if(!d.IsFinite)return false;
                scale=Math.Max(scale,Math.Max(Math.Abs(d.x),Math.Max(Math.Abs(d.y),Math.Abs(d.z))));
            }
            if(!(scale>0)||!MathUtil.IsFinite(scale))return false;
            if(prepared!=null && !prepared.Matches(mesh,ids,original,free))prepared=null;
            try {
                if(prepared==null)prepared=Prepare(mesh,ids,lookup,original,free,count,scale);
                return prepared!=null && prepared.Check(candidate,handles,positionTolerance,responseTolerance,out bound);
            }
            catch(DivideByZeroException) {prepared=null;return false;}
            catch(BiharmonicCertificateRangeException) {prepared=null;return false;}
        }
        static Prepared Prepare(DMesh3 mesh,List<int> ids,Dictionary<int,int> lookup,Vector3d[] original,
            int[] free,int count,double scale)
        {
            int n=ids.Count;var scaleI=I.FromDouble(scale);if(scaleI.Lo.Sign<=0)return null;
            var p=new I[n][];var rows=new Dictionary<int,I>[n];var mass=new I[n];
            for(int i=0;i<n;i++) {
                p[i]=new I[3];rows[i]=new Dictionary<int,I>();mass[i]=Zero;
                for(int axis=0;axis<3;axis++)p[i][axis]=(I.FromDouble(original[i][axis])-I.FromDouble(original[0][axis]))/scaleI;
            }
            var half=I.FromDouble(0.5);var six=I.FromDouble(6);
            foreach(int tid in mesh.TriangleIndices()) {
                var tri=mesh.GetTriangle(tid);int a=lookup[tri.a],b=lookup[tri.b],c=lookup[tri.c];
                var ab=Sub(p[b],p[a]);var ac=Sub(p[c],p[a]);var cross=Cross(ab,ac);var area=I.Sqrt(Dot(cross,cross));
                if(area.Lo.Sign<=0)return null;
                Edge(rows,b,c,half*Dot(ab,ac)/area);
                Edge(rows,a,c,half*Dot(Sub(p[a],p[b]),Sub(p[c],p[b]))/area);
                Edge(rows,a,b,half*Dot(Sub(p[a],p[c]),Sub(p[b],p[c]))/area);
                var share=area/six;mass[a]=mass[a]+share;mass[b]=mass[b]+share;mass[c]=mass[c]+share;
            }
            var columns=new Dictionary<int,I>[count];for(int i=0;i<count;i++)columns[i]=new Dictionary<int,I>();
            long updates=0;int entriesStored=0;
            for(int k=0;k<n;k++) {
                if(mass[k].Lo.Sign<=0)return null;
                var entries=new List<KeyValuePair<int,I>>();foreach(var e in rows[k])if(free[e.Key]>=0)entries.Add(new KeyValuePair<int,I>(free[e.Key],e.Value));
                for(int i=0;i<entries.Count;i++)for(int j=0;j<=i;j++) {
                    if(++updates>MaxUpdates)return null;
                    int row=Math.Max(entries[i].Key,entries[j].Key),col=Math.Min(entries[i].Key,entries[j].Key);
                    if(!columns[col].ContainsKey(row)&&++entriesStored>MaxEntries)return null;
                    Add(columns[col],row,entries[i].Value*entries[j].Value/mass[k]);
                }
            }
            // An untrusted fixed trial; positive interval pivots, not the trial
            // itself, establish the lower spectral bound.
            var shift=I.FromDouble(0.5);
            for(int i=0;i<count;i++) {I diagonal;if(!columns[i].TryGetValue(i,out diagonal))return null;columns[i][i]=diagonal-shift;}
            for(int k=0;k<count;k++) {
                I pivot;if(!columns[k].TryGetValue(k,out pivot)||pivot.Lo.Sign<=0)return null;
                var neighbors=new List<int>(columns[k].Keys);neighbors.Remove(k);neighbors.Sort();
                var values=new I[neighbors.Count];for(int i=0;i<values.Length;i++)values[i]=columns[k][neighbors[i]];
                for(int i=0;i<neighbors.Count;i++) {
                    var factor=values[i]/pivot;
                    for(int j=0;j<=i;j++) {
                        if(++updates>MaxUpdates)return null;
                        int row=neighbors[i],col=neighbors[j];
                        if(!columns[col].ContainsKey(row)&&++entriesStored>MaxEntries)return null;
                        Add(columns[col],row,Zero-factor*values[j]);
                    }
                }
                entriesStored-=columns[k].Count;columns[k].Clear();
            }
            return new Prepared(mesh,ids,original,free,p,rows,mass,scaleI,shift);
        }
        // Opaque rest snapshot: only this class owns the arrays/dictionaries.
        // Every target/output still gets a fresh residual and response check.
        internal sealed class Prepared
        {
            readonly DMesh3 mesh;
            readonly int[] ids,free,triangleIds;
            readonly Index3i[] triangles;
            readonly Vector3d[] original;
            readonly I[][] p;
            readonly Dictionary<int,I>[] rows;
            readonly I[] mass;
            readonly I scaleI,shift;
            internal Prepared(DMesh3 mesh,List<int> ids,Vector3d[] original,int[] free,I[][] p,Dictionary<int,I>[] rows,I[] mass,I scaleI,I shift)
            {
                this.mesh=mesh;this.ids=ids.ToArray();this.original=(Vector3d[])original.Clone();this.free=(int[])free.Clone();
                this.p=p;this.rows=rows;this.mass=mass;this.scaleI=scaleI;this.shift=shift;
                var tids=new List<int>(mesh.TriangleIndices());triangleIds=tids.ToArray();triangles=new Index3i[tids.Count];
                for(int i=0;i<tids.Count;i++)triangles[i]=mesh.GetTriangle(tids[i]);
            }
            internal bool Matches(DMesh3 current,List<int> currentIds,Vector3d[] positions,int[] currentFree)
            {
                if(!ReferenceEquals(mesh,current)||currentIds.Count!=ids.Length||current.VertexCount!=ids.Length||current.TriangleCount!=triangles.Length)return false;
                for(int i=0;i<ids.Length;i++)if(ids[i]!=currentIds[i]||free[i]!=currentFree[i]||!SameBits(original[i],positions[i])||
                    !current.IsVertex(ids[i])||!SameBits(original[i],current.GetVertex(ids[i])))return false;
                for(int i=0;i<triangles.Length;i++) {
                    if(!current.IsTriangle(triangleIds[i]))return false;
                    var a=triangles[i];var b=current.GetTriangle(triangleIds[i]);
                    if(a.a!=b.a||a.b!=b.b||a.c!=b.c)return false;
                }
                return true;
            }
            internal bool Check(Vector3d[] candidate,IReadOnlyDictionary<int,Vector3d> handles,double pt,double rt,out double bound)
            {
                bound=double.NaN;int n=ids.Length;
                var displacement=new I[n][];
                for(int i=0;i<n;i++) {
                    if(free[i]<0) {Vector3d target;if(!handles.TryGetValue(ids[i],out target)||target!=candidate[i])return false;}
                    displacement[i]=new I[3];
                    for(int axis=0;axis<3;axis++)displacement[i][axis]=(I.FromDouble(candidate[i][axis])-I.FromDouble(original[i][axis]))/scaleI;
                }
                var gradient=new I[n][];for(int i=0;i<n;i++)gradient[i]=new[]{Zero,Zero,Zero};
                for(int k=0;k<n;k++) {
                    var lap=new[]{Zero,Zero,Zero};
                    foreach(var e in rows[k])for(int axis=0;axis<3;axis++)lap[axis]=lap[axis]+e.Value*displacement[e.Key][axis];
                    foreach(var e in rows[k])if(free[e.Key]>=0)for(int axis=0;axis<3;axis++)
                        gradient[e.Key][axis]=gradient[e.Key][axis]+e.Value*lap[axis]/mass[k];
                }
                BigInteger errorSquared=0,maximumBound=0;
                for(int axis=0;axis<3;axis++) {
                    BigInteger sum=0;for(int i=0;i<n;i++)if(free[i]>=0) {
                        var g=gradient[i][axis];var magnitude=BigInteger.Max(BigInteger.Abs(g.Lo),BigInteger.Abs(g.Hi));sum+=magnitude*magnitude;
                    }
                    var norm=I.IntegerSqrt(sum);if(norm*norm<sum)norm++;
                    BigInteger remainder;var upper=BigInteger.DivRem(norm*I.Scale,shift.Lo,out remainder);if(!remainder.IsZero)upper++;
                    maximumBound=BigInteger.Max(maximumBound,upper);errorSquared+=upper*upper;
                }
                BigInteger restLower=0,responseLower=0;
                for(int i=0;i<n;i++) {
                    var rest=Zero;var response=Zero;
                    for(int axis=0;axis<3;axis++) {rest=rest+Square(p[i][axis]);response=response+Square(displacement[i][axis]);}
                    restLower=BigInteger.Max(restLower,rest.Lo);if(free[i]>=0)responseLower=BigInteger.Max(responseLower,response.Lo);
                }
                var position=I.FromDouble(pt).Lo;var relative=I.FromDouble(rt).Lo;var unit=I.Scale;
                if(restLower.Sign<=0 || errorSquared*unit>position*position*restLower ||
                    errorSquared*(unit+relative)*(unit+relative)>relative*relative*responseLower*unit)return false;
                double numerator=ExactInterval.Up((double)maximumBound);
                if(!MathUtil.IsFinite(numerator))return false;
                bound=ExactInterval.Up(numerator/(double)unit);
                return MathUtil.IsFinite(bound);
            }
        }
    }
}
