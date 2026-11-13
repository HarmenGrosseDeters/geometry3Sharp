using System;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    // throwaway measurement of the A2 level-set certificate (not committed)
    public class MeshOffsetTopologyProbe
    {
        readonly ITestOutputHelper output;
        public MeshOffsetTopologyProbe(ITestOutputHelper o) { output = o; }

        [Fact]
        public void Probe()
        {
            var inputs = new (string, DMesh3)[] {
                ("box", DegenerateFamilies.Box(Vector3d.Zero, new Vector3d(2, 3, 1.5))),
                ("rotsphere", Rot(new Sphere3Generator_NormalizedCube() { Radius = 5, EdgeVertices = 7 }.Generate().MakeDMesh())),
                                ("sphere", new Sphere3Generator_NormalizedCube() { Radius = 5, EdgeVertices = 11 }.Generate().MakeDMesh()),
                ("rotbox", Rot(DegenerateFamilies.Box(Vector3d.Zero, new Vector3d(2, 3, 1.5)))),
                            };
            foreach (var (name, m) in inputs)
                foreach (double dr in new[] { 0.1, -0.1 }) foreach (double rc in new[] { 0.25 }) {
                    double d = dr * m.CachedBounds.Diagonal.Length;
                    var o = new MeshOffset(m, d) { CertifyTopology = true, RelativeCellSize = rc };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    o.Compute();
                    var t = o.Topology;
                    long topo = o.PhaseMilliseconds["topology"], a1 = o.PhaseMilliseconds.Values.Sum() - topo;
                    output.WriteLine($"{name} d={d:G3} cell {rc}: {o.ResultStatus} A1 {a1} ms, topo {topo} ms ({(double)topo / a1:F1}x), share {100.0 * t.CertifiedCells / (t.CertifiedCells + t.Listed.Count):F1} % zeroFree {t.ZeroFreeCells} in {t.ZeroFreeBlocks} blocks, certified {t.CertifiedCells}, listed {t.Listed.Count} [{string.Join(", ", t.ListedBy.Select(kv => kv.Key + " " + kv.Value))}], mismatch {t.NodeSignMismatches}, evals {t.Evaluations}");
                }
        }

        static DMesh3 Rot(DMesh3 m)
        {
            var q = new Quaterniond(new Vector3d(0.3, 0.7, 0.2).Normalized, 23.7);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, q * m.GetVertex(v));
            return m;
        }
    }
}
