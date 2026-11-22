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

    // The cost of the internal certificate record of TwoLevelMarchingTetrahedra (Lemma 9's data): time of Generate with
    // and without it (9 runs each, alternating, serial; median, smallest and largest), and the payload of the record
    // against the mesh's vertex and triangle arrays.
    //   dotnet run -c Release --project benchmarks/ContouringSmoothCertificate -- record > benchmarks/ContouringSmoothCertificate/results/record.csv
    static int RecordCost()
    {
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine("field,cells,thickness_cells,nodes,vertices,triangles,generate_ms,generate_min_ms,generate_max_ms,with_record_ms,with_record_min_ms,with_record_max_ms,time_pct,mesh_bytes,record_payload_bytes,record_pct,allocated_mib,allocated_with_record_mib,allocated_pct");
        var origin = new Vector3d(-1.113, -1.107, -1.103); const double size = 2.2;
        foreach (TpmsType type in new[] { TpmsType.Gyroid, TpmsType.SchwarzP })
            foreach (int n in new[] { 64, 128, 192 })
                foreach (double cells in new[] { 0.5, 2.0 }) {
                    double h = size / n;
                    var field = new CappedTpms(type, 2 * Math.PI * 2 / size, 0.9, 4);
                    Func<bool, TwoLevelMarchingTetrahedra> make = rec => new TwoLevelMarchingTetrahedra {
                        Field = field, HalfWidth = cells * h / 2 * (2 * Math.PI * 2 / size), CellSize = h, ParallelCompute = false, RecordCertificateData = rec,
                        Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One) };
                    TwoLevelMarchingTetrahedra off = make(false), on = make(true);
                    off.Generate(); on.Generate();
                    var offMs = new System.Collections.Generic.List<double>(); var onMs = new System.Collections.Generic.List<double>();
                    for (int run = 0; run < 9; ++run) {
                        var sw = Stopwatch.StartNew(); off.Generate(); offMs.Add(sw.Elapsed.TotalMilliseconds);
                        sw.Restart(); on.Generate(); onMs.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    offMs.Sort(); onMs.Sort();
                    double tOff = offMs[4], tOn = onMs[4];
                    long a0 = GC.GetAllocatedBytesForCurrentThread(); off.Generate(); long aOff = GC.GetAllocatedBytesForCurrentThread() - a0;
                    a0 = GC.GetAllocatedBytesForCurrentThread(); on.Generate(); long aOn = GC.GetAllocatedBytesForCurrentThread() - a0;
                    var r = on.Certificate;
                    // the mesh as DMesh3 holds it: 3 doubles per vertex, 3 ints per triangle (without its edge tables); the payload of
                    // the record: one byte per node, two ints and a byte per vertex, a byte per triangle (its lists grow by doubling,
                    // so the instance can hold up to twice the list part)
                    long meshBytes = 24L * on.Mesh.VertexCount + 12L * on.Mesh.TriangleCount;
                    long recordBytes = r.NodeFlags.Length + 9L * r.VertexA.Count + r.TriangleLevel.Count;
                    Console.WriteLine(string.Join(",", type, n, cells.ToString(inv), r.NodeFlags.Length, on.Mesh.VertexCount, on.Mesh.TriangleCount,
                        tOff.ToString("0.0", inv), offMs[0].ToString("0.0", inv), offMs[8].ToString("0.0", inv), tOn.ToString("0.0", inv), onMs[0].ToString("0.0", inv), onMs[8].ToString("0.0", inv),
                        (100 * (tOn / tOff - 1)).ToString("0.0", inv),
                        meshBytes, recordBytes, (100.0 * recordBytes / meshBytes).ToString("0.0", inv),
                        (aOff / 1048576.0).ToString("0.0", inv), (aOn / 1048576.0).ToString("0.0", inv), (100.0 * (aOn - aOff) / aOff).ToString("0.0", inv)));
                }
        return 0;
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "record") return RecordCost();
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
