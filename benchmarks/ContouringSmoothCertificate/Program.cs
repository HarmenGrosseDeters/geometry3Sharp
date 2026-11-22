// The cost and the yield of ContouringSmoothCertificate (internal; docs/design/CONTOURING_TOPOLOGY.md 7.13) on closed
// smooth fields: triply periodic nodal functions with a smooth cap, contoured by DualContouring on a uniform grid.
//
//   dotnet run -c Release --project benchmarks/ContouringSmoothCertificate > benchmarks/ContouringSmoothCertificate/results/run.csv
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class Program
{
    // g(K x) - level + cap (|x|^2 / R^2)^3, inside where negative
    sealed class CappedTpms : IntervalImplicit3d, ImplicitFunction3d
    {
        readonly IntervalTpms tpms; readonly double R, cap;
        public CappedTpms(TpmsType type, double k, double radius, double cap) { tpms = new IntervalTpms(type, k, 0); R = radius; this.cap = cap; }
        public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
        {
            IntervalEnclosure e = tpms.Enclose(x, y, z);
            ExactInterval r2 = ExactInterval.Square(ExactInterval.Of(R));
            ExactInterval u = (ExactInterval.Square(x) + ExactInterval.Square(y) + ExactInterval.Square(z)) / r2, u2 = ExactInterval.Square(u);
            ExactInterval d = ExactInterval.Of(6) * ExactInterval.Of(cap) * u2 / r2;
            e.F = e.F + ExactInterval.Of(cap) * (u2 * u); e.Gx = e.Gx + d * x; e.Gy = e.Gy + d * y; e.Gz = e.Gz + d * z;
            return e;
        }
        public double Value(ref Vector3d p)
        {
            double u = p.LengthSquared / (R * R);
            return TpmsLattice.Evaluate(tpms.Type, tpms.K * p.x, tpms.K * p.y, tpms.K * p.z) + cap * u * u * u;
        }
    }

    static double Median(Func<double> run, int repeats)
    {
        var t = Enumerable.Range(0, repeats).Select(_ => run()).OrderBy(x => x).ToArray();
        return t[t.Length / 2];
    }

    static int Main()
    {
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine("type,periods,n,cells_per_period,surface_cells,uncertified_cells,certified,passed_t0,passed_t1,passed_t2,topology,contour_ms,certificate_serial_ms,certificate_parallel_ms");
        var origin = new Vector3d(-1.113, -1.107, -1.103); const double size = 2.2;
        foreach (TpmsType type in new[] { TpmsType.Gyroid, TpmsType.SchwarzP, TpmsType.SchwarzD, TpmsType.Neovius, TpmsType.IWP, TpmsType.Lidinoid })
            foreach (int periods in new[] { 1, 2 })
                foreach (int n in new[] { 32, 64 }) {
                    var f = new CappedTpms(type, 2 * Math.PI * periods / size, 0.9, 4 * (type == TpmsType.Neovius ? 4 : type == TpmsType.IWP ? 2 : 1));
                    DualContouring dc = null;
                    double contourMs = Median(() => {
                        var sw = Stopwatch.StartNew();
                        dc = new DualContouring { Implicit = f, InsideIsLess = true, CellSize = size / n, Simplify = false,
                            Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One), RecordCertificateData = true };
                        dc.Generate();
                        return sw.Elapsed.TotalMilliseconds;
                    }, 3);
                    ContouringSmoothCertificate cert = null;
                    Func<bool, double> time = parallel => Median(() => {
                        var sw = Stopwatch.StartNew();
                        cert = new ContouringSmoothCertificate { Field = f, Contour = dc, ParallelCompute = parallel };
                        cert.Compute();
                        return sw.Elapsed.TotalMilliseconds;
                    }, 3);
                    double serial = time(false), par = time(true);
                    var topo = MeshTopologySummary.Compute(dc.Mesh);
                    Console.WriteLine(string.Join(",", type, periods, n, n / periods, cert.SurfaceCells, cert.UncertifiedCells.Count, cert.Certified ? "yes" : "no",
                        cert.PassedAtTightening[0], cert.PassedAtTightening[1], cert.PassedAtTightening[2],
                        "c=" + topo.Components + ";g=" + topo.Genus.ToString(inv), contourMs.ToString("0", inv), serial.ToString("0", inv), par.ToString("0", inv)));
                }
        return 0;
    }
}
