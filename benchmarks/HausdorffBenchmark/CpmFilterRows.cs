// Benchmark of CertifiedPointMeshDistance.LowerBound and AtLeastDistance (PR #1649, CPM-1 / CPM-2).
// Invoked via: DOTNET_TieredPGO=0 dotnet run -c Release --project benchmarks/HausdorffBenchmark -- cpm [runs]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    static void CpmFilterRows(int runs)
    {
        Type cpm = typeof(MeshHausdorffDistance).Assembly.GetType("gs.CertifiedPointMeshDistance", true);
        var lowerBound = (Func<DMesh3, Vector3d, double>)Delegate.CreateDelegate(
            typeof(Func<DMesh3, Vector3d, double>),
            cpm.GetMethod("LowerBound", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        var atLeast = (Func<DMesh3, Vector3d, double, bool>)Delegate.CreateDelegate(
            typeof(Func<DMesh3, Vector3d, double, bool>),
            cpm.GetMethod("AtLeastDistance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        var cases = new (string name, DMesh3 mesh, Vector3d center, double span)[] {
            ("sphere 12 (1,728 tris)", Sphere(12, 1.0, Vector3d.Zero), Vector3d.Zero, 2.5),
            ("sphere 24 (6,912 tris)", Sphere(24, 1.0, Vector3d.Zero), Vector3d.Zero, 2.5),
            ("bumpy 20 (4,800 tris)", Bumpy(9, 20), Vector3d.Zero, 2.5),
            ("utm sphere 12 (1,728 tris)", Sphere(12, 50.0, new Vector3d(632117.25, 5712043.5, 113.75)),
                new Vector3d(632117.25, 5712043.5, 113.75), 120.0),
        };

        Console.WriteLine($"CertifiedPointMeshDistance (64 queries/case, median of {runs} runs)");
        Console.WriteLine("| case | LowerBound (ms) | AtLeast(0.5*lb, ms) | AtLeast(1.5*lb, ms) | digest |");
        Console.WriteLine("|---|---:|---:|---:|---|");

        foreach (var c in cases) {
            var rnd = new Random(1649);
            var pts = new Vector3d[64];
            var lbs = new double[64];
            for (int i = 0; i < pts.Length; ++i) {
                pts[i] = c.center + new Vector3d(
                    (rnd.NextDouble() * 2 - 1) * c.span,
                    (rnd.NextDouble() * 2 - 1) * c.span,
                    (rnd.NextDouble() * 2 - 1) * c.span);
                lbs[i] = lowerBound(c.mesh, pts[i]);
            }

            ulong digest = 14695981039346656037UL;
            void Mix(ulong x) { digest ^= x; digest *= 1099511628211UL; }

            double Median(Action body)
            {
                body(); // warm-up
                var ts = new double[runs];
                for (int r = 0; r < runs; ++r) {
                    var sw = Stopwatch.StartNew();
                    body();
                    ts[r] = sw.Elapsed.TotalMilliseconds;
                }
                Array.Sort(ts);
                return ts[runs / 2];
            }

            double tLb = Median(() => {
                for (int i = 0; i < pts.Length; ++i) lbs[i] = lowerBound(c.mesh, pts[i]);
            });
            int tFalseCnt = 0, tTrueCnt = 0;
            double tAtLo = Median(() => {
                int cnt = 0;
                for (int i = 0; i < pts.Length; ++i) if (atLeast(c.mesh, pts[i], 0.5 * lbs[i])) ++cnt;
                tFalseCnt = cnt;
            });
            double tAtHi = Median(() => {
                int cnt = 0;
                for (int i = 0; i < pts.Length; ++i) if (atLeast(c.mesh, pts[i], 1.5 * lbs[i] + 1e-9)) ++cnt;
                tTrueCnt = cnt;
            });

            for (int i = 0; i < lbs.Length; ++i) Mix((ulong)BitConverter.DoubleToInt64Bits(lbs[i]));
            Mix((ulong)tFalseCnt);
            Mix((ulong)tTrueCnt);
            Console.WriteLine($"| {c.name} | {tLb:F2} | {tAtLo:F2} | {tAtHi:F2} | {digest:X16} |");
        }
    }
}
