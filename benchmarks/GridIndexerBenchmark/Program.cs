using System;
using System.Diagnostics;
using g3;
class Bench {
 static void Main() {
  var rng = new Random(804); var pts = new Vector3d[4096];
  for(int i=0;i<pts.Length;i++) pts[i]=new Vector3d(rng.NextDouble()*2000-1000,rng.NextDouble()*2000-1000,rng.NextDouble()*2000-1000);
  var indexer=new ShiftGridIndexer3(new Vector3d(12,-45,29),0.75);
  for(int run=0;run<6;run++){long sum=0;var sw=Stopwatch.StartNew();for(int i=0;i<4000000;i++){var cell=indexer.ToGrid(pts[i & 4095]);sum+=cell.x+cell.y+cell.z;}sw.Stop();Console.WriteLine($"{sw.Elapsed.TotalMilliseconds:F3} ms checksum {sum}");}
 }
}
