// Experimental fixed-point interval arithmetic; not used by the library.
using System;
using System.Diagnostics;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

readonly struct Interval
{
    public readonly BigInteger Lo, Hi;
    public static int Bits;
    public static BigInteger Scale => BigInteger.One << Bits;
    public Interval(BigInteger lo, BigInteger hi) { if (lo > hi) throw new ArgumentException(); Lo=lo; Hi=hi; }
    static BigInteger Floor(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign < 0) { numerator=-numerator; denominator=-denominator; }
        BigInteger remainder; var quotient=BigInteger.DivRem(numerator,denominator,out remainder);
        return remainder.Sign < 0 ? quotient-1 : quotient;
    }
    static BigInteger Ceil(BigInteger numerator, BigInteger denominator) { return -Floor(-numerator,denominator); }
    static void DecimalFraction(string text, out BigInteger numerator, out BigInteger denominator)
    {
        string[] exponentParts=text.ToLowerInvariant().Split('e');
        if(exponentParts.Length>2)throw new FormatException();
        int exponent=exponentParts.Length==2 ? int.Parse(exponentParts[1],System.Globalization.CultureInfo.InvariantCulture) : 0;
        string mantissa=exponentParts[0]; int point=mantissa.IndexOf('.');
        if(point>=0) { exponent-=mantissa.Length-point-1; mantissa=mantissa.Remove(point,1); }
        numerator=BigInteger.Parse(mantissa,System.Globalization.CultureInfo.InvariantCulture);
        if(Math.Abs((long)exponent)>10000)throw new FormatException();
        if(exponent>=0) { numerator*=BigInteger.Pow(10,exponent); denominator=BigInteger.One; }
        else denominator=BigInteger.Pow(10,-exponent);
    }
    public static Interval FromDecimalBounds(string lower, string upper)
    {
        BigInteger ln,ld,un,ud;DecimalFraction(lower,out ln,out ld);DecimalFraction(upper,out un,out ud);
        if(ln*ud>un*ld)throw new ArgumentException("reversed decimal bounds");
        return new Interval(Floor(ln*Scale,ld),Ceil(un*Scale,ud));
    }
    public static Interval FromDouble(double v)
    {
        if (!double.IsFinite(v)) throw new ArgumentException();
        long raw=BitConverter.DoubleToInt64Bits(v); bool negative=raw<0;
        int field=(int)((raw>>52)&0x7ff); long mantissa=raw&0x000fffffffffffffL;
        if(field!=0) mantissa|=1L<<52;
        int exponent=(field==0 ? -1074 : field-1075)+Bits;
        BigInteger numerator=negative ? -new BigInteger(mantissa) : new BigInteger(mantissa);
        if(exponent>=0) return new Interval(numerator<<exponent,numerator<<exponent);
        BigInteger denominator=BigInteger.One << -exponent;
        return new Interval(Floor(numerator,denominator),Ceil(numerator,denominator));
    }
    public static Interval operator +(Interval a, Interval b) => new Interval(a.Lo+b.Lo,a.Hi+b.Hi);
    public static Interval operator -(Interval a, Interval b) => new Interval(a.Lo-b.Hi,a.Hi-b.Lo);
    public static Interval operator *(Interval a, Interval b)
    {
        var x=a.Lo*b.Lo;var y=a.Lo*b.Hi;var z=a.Hi*b.Lo;var w=a.Hi*b.Hi;
        var low=BigInteger.Min(BigInteger.Min(x,y),BigInteger.Min(z,w));
        var high=BigInteger.Max(BigInteger.Max(x,y),BigInteger.Max(z,w));
        return new Interval(low>>Bits,-((-high)>>Bits));
    }
    public static Interval operator /(Interval a, Interval b)
    {
        if(b.Lo.Sign<=0 && b.Hi.Sign>=0) throw new DivideByZeroException();
        var lo=new[]{Floor(a.Lo<<Bits,b.Lo),Floor(a.Lo<<Bits,b.Hi),Floor(a.Hi<<Bits,b.Lo),Floor(a.Hi<<Bits,b.Hi)};
        var hi=new[]{Ceil(a.Lo<<Bits,b.Lo),Ceil(a.Lo<<Bits,b.Hi),Ceil(a.Hi<<Bits,b.Lo),Ceil(a.Hi<<Bits,b.Hi)};
        var l=lo[0];var h=hi[0];for(int i=1;i<4;i++){l=BigInteger.Min(l,lo[i]);h=BigInteger.Max(h,hi[i]);}
        return new Interval(l,h);
    }
    public static BigInteger IntegerSqrt(BigInteger n)
    {
        if(n.Sign<0) throw new ArgumentException(); if(n.IsZero) return BigInteger.Zero;
        BigInteger x=BigInteger.One << (int)((n.GetBitLength()+1)/2);
        while(true) { var next=(x+n/x)>>1; if(next>=x) return x; x=next; }
    }
    public static Interval Sqrt(Interval a)
    {
        if(a.Hi.Sign<0) throw new ArgumentException();
        BigInteger low=BigInteger.Max(BigInteger.Zero,a.Lo)<<Bits, high=a.Hi<<Bits;
        BigInteger l=IntegerSqrt(low),h=IntegerSqrt(high);
        if(h*h<high)h++;
        return new Interval(l,h);
    }
}

class Program
{
    static string PivotOutputPath;
    static bool ScalarMode;
    static bool EnvelopeMode, BoundsOnly;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static BigInteger Min4(BigInteger a,BigInteger b,BigInteger c,BigInteger d)=>BigInteger.Min(BigInteger.Min(a,b),BigInteger.Min(c,d));
    static BigInteger Max4(BigInteger a,BigInteger b,BigInteger c,BigInteger d)=>BigInteger.Max(BigInteger.Max(a,b),BigInteger.Max(c,d));
    static void Add(Dictionary<int,Interval> row,int index,Interval value)
    {
        Interval old;if(!row.TryGetValue(index,out old))old=new Interval(0,0);row[index]=old+value;
    }
    static Interval[] Sub(Interval[] a,Interval[] b)=>new[]{a[0]-b[0],a[1]-b[1],a[2]-b[2]};
    static Interval Dot(Interval[] a,Interval[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
    static Interval[] Cross(Interval[] a,Interval[] b)=>new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
    static int FindRoot(int[] parent,int vertex)
    {
        while(parent[vertex]!=vertex) { parent[vertex]=parent[parent[vertex]];vertex=parent[vertex]; }
        return vertex;
    }
    static void Union(int[] parent,int a,int b)
    {
        a=FindRoot(parent,a);b=FindRoot(parent,b);if(a!=b)parent[b]=a;
    }
    static Interval Square(Interval value)
    {
        var product=value*value;
        return new Interval(value.Lo.Sign<=0 && value.Hi.Sign>=0 ? BigInteger.Zero : BigInteger.Max(BigInteger.Zero,product.Lo),product.Hi);
    }
    static void Edge(Dictionary<int,Interval>[] rows,int a,int b,Interval w)
    {
        Add(rows[a],a,w);Add(rows[b],b,w);Add(rows[a],b,new Interval(-w.Hi,-w.Lo));Add(rows[b],a,new Interval(-w.Hi,-w.Lo));
    }
    // Bound ||Q_ff u_f + Q_fc u_c||_2 / mu from the independently assembled
    // intervals, using the already-rounded binary64 world-coordinate candidate.
    static void CheckResidual(double[][] original,Dictionary<int,Interval>[] rows,Interval[] mass,int[] free,
        Interval scale,Interval shift,JsonElement candidateBits,JsonElement targetBits)
    {
        int n=original.Length;if(candidateBits.GetArrayLength()!=n||targetBits.GetArrayLength()!=n)throw new FormatException("candidate size");
        var displacement=new Interval[n][];
        for(int i=0;i<n;i++) {
            displacement[i]=new Interval[3];
            for(int axis=0;axis<3;axis++) {
                long candidateRaw=candidateBits[i][axis].GetInt64(),targetRaw=targetBits[i][axis].GetInt64();
                if(free[i]<0 && candidateRaw!=targetRaw) { Console.WriteLine("position_certificate=not_established reason=handle");Environment.ExitCode=2;return; }
                double value=BitConverter.Int64BitsToDouble(candidateRaw);
                if(!double.IsFinite(value)) { Console.WriteLine("position_certificate=not_established reason=nonfinite");Environment.ExitCode=2;return; }
                displacement[i][axis]=(Interval.FromDouble(value)-Interval.FromDouble(original[i][axis]))/scale;
            }
        }
        var gradient=new Interval[n][];
        for(int i=0;i<n;i++)gradient[i]=new[]{new Interval(0,0),new Interval(0,0),new Interval(0,0)};
        for(int k=0;k<n;k++) {
            var lap=new[]{new Interval(0,0),new Interval(0,0),new Interval(0,0)};
            foreach(var e in rows[k])for(int axis=0;axis<3;axis++)lap[axis]=lap[axis]+e.Value*displacement[e.Key][axis];
            foreach(var e in rows[k])if(free[e.Key]>=0)for(int axis=0;axis<3;axis++)
                gradient[e.Key][axis]=gradient[e.Key][axis]+e.Value*lap[axis]/mass[k];
        }
        var tolerance=Interval.FromDouble(1e-8);bool all=true;
        BigInteger errorSquared=BigInteger.Zero;
        for(int axis=0;axis<3;axis++) {
            BigInteger sum=0;
            for(int i=0;i<n;i++)if(free[i]>=0) {
                var g=gradient[i][axis];var magnitude=BigInteger.Max(BigInteger.Abs(g.Lo),BigInteger.Abs(g.Hi));sum+=magnitude*magnitude;
            }
            BigInteger norm=Interval.IntegerSqrt(sum);if(norm*norm<sum)norm++;
            BigInteger rem;var bound=BigInteger.DivRem(norm*Interval.Scale,shift.Lo,out rem);if(!rem.IsZero)bound++;
            errorSquared+=bound*bound;
            Console.WriteLine($"axis={axis} residual_norm_upper_scaled={norm} normalized_error_upper_scaled={bound} bits={Interval.Bits}");
            if(bound>tolerance.Lo)all=false;
        }
        // The fixture is a single component ordered with its root at index zero.
        // Each squared norm has a conservative lower endpoint; the accumulated
        // per-axis residual bounds give an upper squared Euclidean error.
        BigInteger restSquaredLower=BigInteger.Zero,responseSquaredLower=BigInteger.Zero;
        for(int i=0;i<n;i++) {
            var rest=new Interval(0,0);var response=new Interval(0,0);
            for(int axis=0;axis<3;axis++) {
                var delta=(Interval.FromDouble(original[i][axis])-Interval.FromDouble(original[0][axis]))/scale;
                rest=rest+Square(delta);
                if(free[i]>=0)response=response+Square(displacement[i][axis]);
            }
            restSquaredLower=BigInteger.Max(restSquaredLower,rest.Lo);
            if(free[i]>=0)responseSquaredLower=BigInteger.Max(responseSquaredLower,response.Lo);
        }
        BigInteger unit=Interval.Scale,tol=tolerance.Lo;
        bool absoluteOk=restSquaredLower.Sign>0 && errorSquared*unit<=tol*tol*restSquaredLower;
        bool relativeOk=errorSquared*(unit+tol)*(unit+tol)<=tol*tol*responseSquaredLower*unit;
        Console.WriteLine($"euclidean_absolute={absoluteOk} euclidean_relative={relativeOk} component_scope=fixture");
        all &= absoluteOk && relativeOk;
        Console.WriteLine(all ? "position_certificate=within_tolerance" : "position_certificate=not_established");
        if(!all)Environment.ExitCode=2;
    }
    // Pilot-only: independently assemble from exact binary64 fixture metadata.
    // The exported permutation is untrusted and is checked to be a bijection.
    static void FactorMesh(string path)
    {
        var total=Stopwatch.StartNew();
        byte[] contents=File.ReadAllBytes(path);string digest=Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
        using var json=JsonDocument.Parse(contents);var root=json.RootElement;
        var vertices=root.GetProperty("vertices_bits");int n=vertices.GetArrayLength(),nf=root.GetProperty("size").GetInt32();
        if(n<3||n>(BoundsOnly ? 100000 : 10000)||nf<1||nf>n)throw new ArgumentOutOfRangeException();
        var coords=new double[n][];
        // JsonElement array indexing traverses preceding elements. Enumerate
        // large arrays once so fixture parsing remains linear in vertex count.
        int coordinateIndex=0;
        foreach(var vertex in vertices.EnumerateArray()) {
            int i=coordinateIndex++;
            coords[i]=new double[3];for(int k=0;k<3;k++) {
                coords[i][k]=BitConverter.Int64BitsToDouble(vertex[k].GetInt64());
                if(!double.IsFinite(coords[i][k]))throw new FormatException("nonfinite vertex");
            }
        }
        double scale=BitConverter.Int64BitsToDouble(root.GetProperty("scale_bits").GetInt64());
        double shift=BitConverter.Int64BitsToDouble(root.GetProperty("shift_bits").GetInt64());
        if(!(scale>0)||!double.IsFinite(scale)||!(shift>0)||!double.IsFinite(shift))throw new FormatException("invalid scale or shift");
        var fixedJson=root.GetProperty("fixed");if(fixedJson.GetArrayLength()!=n)throw new FormatException("fixed size");
        var free=new int[n];int count=0;
        int fixedIndex=0;
        foreach(var fixedVertex in fixedJson.EnumerateArray())free[fixedIndex++]=fixedVertex.GetBoolean() ? -1 : count++;
        if(count!=nf)throw new FormatException("free count");
        var inverse=new int[nf];var seen=new bool[nf];var order=root.GetProperty("order");
        if(order.GetArrayLength()!=nf)throw new FormatException("permutation size");
        int orderIndex=0;
        foreach(var orderedVertex in order.EnumerateArray()) {
            int v=orderedVertex.GetInt32();if(v<0||v>=nf||seen[v])throw new FormatException("permutation");
            seen[v]=true;inverse[v]=orderIndex++;
        }
        var p=new Interval[n][];var rows=new Dictionary<int,Interval>[n];var mass=new Interval[n];var scaleI=Interval.FromDouble(scale);
        if(scaleI.Lo.Sign<=0) { Console.WriteLine("status=uncertain reason=scale");Environment.ExitCode=1;return; }
        for(int i=0;i<n;i++) {
            p[i]=new Interval[3];rows[i]=new Dictionary<int,Interval>();mass[i]=new Interval(0,0);
            for(int k=0;k<3;k++)p[i][k]=(Interval.FromDouble(coords[i][k])-Interval.FromDouble(coords[0][k]))/scaleI;
        }
        var half=Interval.FromDouble(0.5);var six=Interval.FromDouble(6);
        var components=new int[n];for(int i=0;i<n;i++)components[i]=i;
        foreach(var face in root.GetProperty("faces").EnumerateArray()) {
            int a=face[0].GetInt32(),b=face[1].GetInt32(),c=face[2].GetInt32();
            if(a<0||b<0||c<0||a>=n||b>=n||c>=n||a==b||a==c||b==c)throw new FormatException("face");
            Union(components,a,b);Union(components,a,c);
            var ab=Sub(p[b],p[a]);var ac=Sub(p[c],p[a]);var cross=Cross(ab,ac);var area=Interval.Sqrt(Dot(cross,cross));
            if(area.Lo.Sign<=0) { Console.WriteLine("status=uncertain reason=area");Environment.ExitCode=1;return; }
            Edge(rows,b,c,half*Dot(ab,ac)/area);
            Edge(rows,a,c,half*Dot(Sub(p[a],p[b]),Sub(p[c],p[b]))/area);
            Edge(rows,a,b,half*Dot(Sub(p[a],p[c]),Sub(p[b],p[c]))/area);
            var share=area/six;mass[a]=mass[a]+share;mass[b]=mass[b]+share;mass[c]=mass[c]+share;
        }
        for(int i=1;i<n;i++)if(FindRoot(components,i)!=FindRoot(components,0)) {
            Console.WriteLine("status=uncertain reason=disconnected");Environment.ExitCode=1;return;
        }
        var columns=new Dictionary<int,Interval>[nf];for(int i=0;i<nf;i++)columns[i]=new Dictionary<int,Interval>();
        for(int k=0;k<n;k++) {
            if(mass[k].Lo.Sign<=0) { Console.WriteLine("status=uncertain reason=mass");Environment.ExitCode=1;return; }
            var entries=new List<KeyValuePair<int,Interval>>();
            foreach(var e in rows[k])if(free[e.Key]>=0)entries.Add(new KeyValuePair<int,Interval>(inverse[free[e.Key]],e.Value));
            for(int i=0;i<entries.Count;i++)for(int j=0;j<=i;j++) {
                int row=Math.Max(entries[i].Key,entries[j].Key),col=Math.Min(entries[i].Key,entries[j].Key);
                Add(columns[col],row,entries[i].Value*entries[j].Value/mass[k]);
            }
        }
        var shiftI=Interval.FromDouble(shift);
        if(shiftI.Lo.Sign<=0) { Console.WriteLine("status=uncertain reason=shift");Environment.ExitCode=1;return; }
        for(int i=0;i<nf;i++) {
            Interval diag;if(!columns[i].TryGetValue(i,out diag))throw new FormatException("missing diagonal");
            columns[i][i]=diag-shiftI;
        }
        string shiftText=shift.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        if(ScalarMode ? ScalarCholesky.Verify(columns,digest,shiftText,PivotOutputPath,EnvelopeMode,BoundsOnly) : Factor(columns,digest,shiftText,"mesh")) {
            if(ScalarMode) Console.WriteLine("position_certificate=not_checked reason=experimental_scalar_matrix_only");
            else if(root.TryGetProperty("candidate_bits",out var candidateBits) && root.TryGetProperty("target_bits",out var targetBits))
                CheckResidual(coords,rows,mass,free,scaleI,shiftI,candidateBits,targetBits);
            else Console.WriteLine("position_certificate=not_checked reason=missing_candidate_or_targets");
        }
        total.Stop();
        Console.WriteLine($"mesh_total_ms={total.Elapsed.TotalMilliseconds:F3} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64}");
    }
    static void FactorExport(string path)
    {
        byte[] contents=File.ReadAllBytes(path);
        string digest=Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
        using var json=JsonDocument.Parse(contents);
        var root=json.RootElement; int n=root.GetProperty("size").GetInt32();
        if(n<1||n>(BoundsOnly ? 100000 : 10000))throw new ArgumentOutOfRangeException("size");
        var columns=new Dictionary<int,Interval>[n];for(int i=0;i<n;i++)columns[i]=new Dictionary<int,Interval>();
        foreach(var entry in root.GetProperty("entries").EnumerateArray()) {
            int row=entry[0].GetInt32(),col=entry[1].GetInt32();
            if(col<0||row<col||row>=n)throw new FormatException("invalid lower triangle index");
            columns[col].Add(row,Interval.FromDecimalBounds(entry[2].GetString(),entry[3].GetString()));
        }
        Factor(columns,digest,root.GetProperty("shift_binary64").GetString(),"export");
    }
    static bool Factor(Dictionary<int,Interval>[] columns,string digest,string shift,string mode)
    {
        int n=columns.Length;
        using var pivotOutput=PivotOutputPath==null ? null : File.CreateText(PivotOutputPath);
        if(pivotOutput!=null)pivotOutput.WriteLine($"# input_sha256={digest}");
        long updates=0; var watch=Stopwatch.StartNew(); BigInteger minPivot=BigInteger.Zero,minPivotHi=BigInteger.Zero;int minIndex=-1;
        for(int k=0;k<n;k++) {
            Interval pivot;if(!columns[k].TryGetValue(k,out pivot)||pivot.Lo.Sign<=0) {
                Console.WriteLine($"status=uncertain pivot={k} input_sha256={digest}");Environment.ExitCode=1;return false;
            }
            if(k==0||pivot.Lo<minPivot) { minPivot=pivot.Lo;minPivotHi=pivot.Hi;minIndex=k; }
            if(pivotOutput!=null)pivotOutput.WriteLine($"{k} {pivot.Lo} {pivot.Hi}");
            var neighbors=new List<int>(columns[k].Keys);neighbors.Remove(k);neighbors.Sort();
            var col=new Interval[neighbors.Count];for(int i=0;i<col.Length;i++)col[i]=columns[k][neighbors[i]];
            for(int i=0;i<neighbors.Count;i++) {
                var factor=col[i]/pivot;
                for(int j=0;j<=i;j++) {
                    int row=neighbors[i], c=neighbors[j];Interval previous;
                    if(!columns[c].TryGetValue(row,out previous))previous=new Interval(0,0);
                    columns[c][row]=previous-factor*col[j];updates++;
                }
            }
            columns[k].Clear();
        }
        watch.Stop();
        Console.WriteLine($"status=positive_interval_pivots mode={mode} bits={Interval.Bits} size={n} updates={updates} input_sha256={digest} shift_binary64={shift} min_pivot_index={minIndex} min_pivot_lo_scaled={minPivot} min_pivot_hi_scaled={minPivotHi} elapsed_ms={watch.Elapsed.TotalMilliseconds:F3} peak_working_set_bytes={Process.GetCurrentProcess().PeakWorkingSet64}");
        return true;
    }
    static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="scalar-selftest") { ScalarCholesky.SelfTest();return; }
        EnvelopeMode=args.Length>0 && (args[0]=="mesh-envelope"||args[0]=="mesh-envelope-bounds");
        BoundsOnly=args.Length>0 && args[0]=="mesh-envelope-bounds";
        ScalarMode=args.Length>0 && (args[0]=="mesh-scalar"||EnvelopeMode);
        bool factor=args.Length>0 && (args[0]=="factor"||args[0]=="mesh"||ScalarMode);
        Interval.Bits=factor ? int.Parse(args[1]) : args.Length>0?int.Parse(args[0]):512;
        if(Interval.Bits<16||Interval.Bits>4096)throw new ArgumentOutOfRangeException();
        if(factor) { if(args.Length>3)PivotOutputPath=args[3];if(args[0]=="mesh"||ScalarMode)FactorMesh(args[2]);else FactorExport(args[2]); return; }
        var random=new Random(886);int cases=10000;
        var watch=Stopwatch.StartNew();
        for(int i=0;i<cases;i++) {
            int ashift=i%3==0?0:i%3==1?Interval.Bits/2:Interval.Bits-10;
            int bshift=i%4==0?0:i%4==1?Interval.Bits/2:Interval.Bits-10;
            BigInteger al=(new BigInteger(random.NextInt64(-1000000,1000000))<<ashift)+random.NextInt64(-100,100);
            BigInteger ah=al+(new BigInteger(random.NextInt64(0,10000))<<ashift);
            BigInteger bl=(new BigInteger(random.NextInt64(-1000000,1000000))<<bshift)+random.NextInt64(-100,100);
            BigInteger bh=bl+(new BigInteger(random.NextInt64(0,10000))<<bshift);
            var a=new Interval(al,ah);var b=new Interval(bl,bh);var p=a*b;
            var sum=a+b; var difference=a-b;
            Check(sum.Lo==a.Lo+b.Lo && sum.Hi==a.Hi+b.Hi,"addition enclosure");
            Check(difference.Lo==a.Lo-b.Hi && difference.Hi==a.Hi-b.Lo,"subtraction enclosure");
            var pmin=Min4(a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi);
            var pmax=Max4(a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi);
            Check(p.Lo*Interval.Scale<=pmin && p.Hi*Interval.Scale>=pmax,"multiply enclosure");
            if(b.Lo.Sign>0 || b.Hi.Sign<0) {
                var q=a/b;
                foreach(var numerator in new[]{a.Lo,a.Hi}) foreach(var denominator in new[]{b.Lo,b.Hi}) {
                    var lhs=q.Lo*denominator;var rhs=numerator*Interval.Scale;var upper=q.Hi*denominator;
                    Check(denominator.Sign>0 ? lhs<=rhs && rhs<=upper : lhs>=rhs && rhs>=upper,"division enclosure");
                }
            }
            var positive=new Interval(BigInteger.Abs(al),BigInteger.Abs(al)+random.NextInt64(0,10000));
            var root=Interval.Sqrt(positive);
            Check(root.Lo*root.Lo<=positive.Lo*Interval.Scale && root.Hi*root.Hi>=positive.Hi*Interval.Scale,"sqrt enclosure");
        }
        var crossing=Interval.Sqrt(new Interval(-Interval.Scale,4*Interval.Scale));
        Check(crossing.Lo==0 && crossing.Hi==2*Interval.Scale,"sqrt across zero and perfect square");
        bool refused=false;
        try { var ignored=new Interval(-Interval.Scale,Interval.Scale) / new Interval(-1,1); }
        catch(DivideByZeroException) { refused=true; }
        Check(refused,"division by an interval containing zero");
        var decimalBounds=Interval.FromDecimalBounds("-1.25e-1","5e-1");
        Check(decimalBounds.Lo*8==-Interval.Scale && decimalBounds.Hi*2==Interval.Scale,"decimal exponent conversion");
        var tinyBounds=Interval.FromDecimalBounds("-1e-100","1e-100");
        Check(tinyBounds.Lo<0 && tinyBounds.Hi>0,"sub-grid decimal conversion");
        bool reversed=false;
        try { var ignored=Interval.FromDecimalBounds("1e-100","0"); }
        catch(ArgumentException) { reversed=true; }
        Check(reversed,"reversed narrow decimal bounds");
        foreach(double x in new[]{0d,1d,-1d,0.1,-0.1,double.Epsilon,-double.Epsilon,double.MaxValue,-double.MaxValue}) {
            var iv=Interval.FromDouble(x);
            // Reconstruct the dyadic input exactly with its binary exponent, then compare integers.
            long bits=BitConverter.DoubleToInt64Bits(x);int e=(int)((bits>>52)&2047);long m=bits&0x000fffffffffffffL;
            if(e!=0)m|=1L<<52; BigInteger exact=bits<0 ? -new BigInteger(m) : new BigInteger(m);
            int power=(e==0?-1074:e-1075)+Interval.Bits;
            if(power>=0) Check(iv.Lo<=exact<<power && iv.Hi>=exact<<power,"double enclosure");
            else { var d=BigInteger.One << -power; Check(iv.Lo*d<=exact && iv.Hi*d>=exact,"double enclosure"); }
        }
        watch.Stop();
        Console.WriteLine($"bits={Interval.Bits} randomized_cases={cases} status=passed elapsed_ms={watch.Elapsed.TotalMilliseconds:F3}");
    }
}
