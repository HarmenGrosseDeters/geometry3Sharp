using System; using System.Diagnostics; using g3;
// ns per call of the filter-settled path of the public predicates (random inputs, so the filter decides), and of the
// plane-side filter formula (copy of ExactMeshBoolean.plane_side_filtered), loop overhead subtracted
static class P {
  static double Time(Func<int,int> f, int n) { int s=0; for (int i=0;i<n/10;i++) s+=f(i); var sw=Stopwatch.StartNew(); for (int i=0;i<n;i++) s+=f(i); double ns=sw.Elapsed.TotalMilliseconds*1e6/n; if (s==42) Console.Write(""); return ns; }
  static void Main() {
    var r = new Random(1); int N=4096; var v3=new Vector3d[N*5]; var v2=new Vector2d[N*4];
    for (int i=0;i<v3.Length;i++) v3[i]=new Vector3d(r.NextDouble(),r.NextDouble(),r.NextDouble());
    for (int i=0;i<v2.Length;i++) v2[i]=new Vector2d(r.NextDouble(),r.NextDouble());
    double[] pa={0.3,0.5,0.7,0.1}; int n=100_000_000; int s=0;
    for (int rep=0;rep<2;rep++) {
    var sw=Stopwatch.StartNew(); for (int i=0;i<n;i++){int k=(i&(N-1))*4; s+=ExactPredicates.Orient2d(v2[k],v2[k+1],v2[k+2]);} double o2=sw.Elapsed.TotalMilliseconds*1e6/n;
    sw.Restart(); for (int i=0;i<n;i++){int k=(i&(N-1))*5; s+=ExactPredicates.Orient3d(v3[k],v3[k+1],v3[k+2],v3[k+3]);} double o3=sw.Elapsed.TotalMilliseconds*1e6/n;
    sw.Restart(); for (int i=0;i<n;i++){int k=(i&(N-1))*4; s+=ExactPredicates.InCircle(v2[k],v2[k+1],v2[k+2],v2[k+3]);} double ic=sw.Elapsed.TotalMilliseconds*1e6/n;
    sw.Restart(); for (int i=0;i<n/2;i++){int k=(i&(N-1))*5; s+=ExactPredicates.InSphere(v3[k],v3[k+1],v3[k+2],v3[k+3],v3[k+4]);} double isp=sw.Elapsed.TotalMilliseconds*2e6/n;
    sw.Restart(); for (int i=0;i<n;i++){int k=(i&(N-1))*5; double a=pa[0]*v3[k].x,b=pa[1]*v3[k].y,c=pa[2]*v3[k].z,d=pa[3]; double mag=Math.Abs(a)+Math.Abs(b)+Math.Abs(c)+Math.Abs(d); double bound=1e-14*mag; double v=a+b+c-d; s+=v>bound?1:(v<-bound?-1:2);} double ps=sw.Elapsed.TotalMilliseconds*1e6/n;
    Console.WriteLine($"rep{rep}: orient2d {o2:F1} orient3d {o3:F1} incircle {ic:F1} insphere {isp:F1} plane_side {ps:F1} ns (s={s&1})");
    }
  }
}
