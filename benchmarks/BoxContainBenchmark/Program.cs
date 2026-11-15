using System;
using System.Diagnostics;
using g3;
class Bench {
  static void Main() {
    var rng = new Random(803); var pts = new Vector3d[4096];
    for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3d(rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
    for (int run = 0; run < 6; run++) {
      var box = AxisAlignedBox3d.Empty; var sw = Stopwatch.StartNew();
      for (int i = 0; i < 4000000; i++) box.Contain(pts[i & 4095]);
      sw.Stop(); Console.WriteLine($"{sw.Elapsed.TotalMilliseconds:F3} ms checksum {box.Min.x:R} {box.Max.x:R}");
    }
  }
}
