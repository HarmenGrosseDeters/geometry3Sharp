"""Instrument the frozen evaluation driver only; generate source outside the repository."""
import argparse
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / 'sources' / 'benchmarks' / 'BooleanComparison' / 'Program.cs'

TRACE = r'''
    static string phaseOperation, phaseName;
    static readonly Stopwatch phaseTimer = new Stopwatch();

    static void PhaseEvent(string state, long? ms)
    {
        string path = Environment.GetEnvironmentVariable("THINGI_EVAL_PHASE_LOG");
        if (string.IsNullOrEmpty(path)) return;
        var row = new Dictionary<string, object> {
            ["pid"] = Environment.ProcessId, ["operation"] = phaseOperation, ["phase"] = phaseName,
            ["state"] = state, ["wall_ms"] = ms, ["at"] = DateTimeOffset.UtcNow.ToString("O") };
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(file);
        writer.WriteLine(JsonSerializer.Serialize(row));
        writer.Flush();
        file.Flush(true);
    }

    static void FinishPhase()
    {
        if (phaseName == null) return;
        phaseTimer.Stop();
        PhaseEvent("completed", phaseTimer.ElapsedMilliseconds);
        phaseName = null;
    }

    static void Phase(string operation, string name)
    {
        FinishPhase();
        phaseOperation = operation;
        phaseName = name;
        PhaseEvent("started", null);
        phaseTimer.Restart();
    }

    // Same checks, order, thresholds and early returns as the pinned NotCleanReason.
    // Keep the library binary unchanged; differential smoke tests guard this diagnostic copy.
    static string ProbeNotCleanReason(DMesh3 mesh, bool checkSelfIntersections, string op)
    {
        Phase(op, "clean_empty");
        if (mesh.TriangleCount == 0) return "empty";
        Phase(op, "clean_closed");
        if (mesh.IsClosed() == false) return "not closed";
        Phase(op, "clean_bowties");
        if (MeshIterators.BowtieVertices(mesh).Any()) return "bowties";
        Phase(op, "clean_degenerate");
        double diag = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
        double areaEps = 1e-12 * diag * diag;
        if (mesh.TriangleIndices().Any(t => mesh.GetTriArea(t) < areaEps)) return "degenerate triangles";
        Phase(op, "clean_orientation");
        if (MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) return "inconsistent orientation";
        Phase(op, "clean_inverted_components");
        if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) return "inside-out components";
        Phase(op, "clean_self_intersections");
        if (checkSelfIntersections && MeshRepairMetrics.HasSelfIntersections(mesh)) return "self-intersections";
        return "";
    }
'''


def instrument(source):
    changes = [
        ('    static int Eval(string dir)', TRACE + '\n    static int Eval(string dir)'),
        ('                List<Vector3d> v; List<Index3i> t;',
         '                Phase(op, "read_soup");\n                List<Vector3d> v; List<Index3i> t;'),
        ('                var ids = new Dictionary<Vector3d, int>();',
         '                Phase(op, "weld_vertices");\n                var ids = new Dictionary<Vector3d, int>();'),
        ('                var edgeUse = new Dictionary<long, int>();',
         '                Phase(op, "edge_topology");\n                var edgeUse = new Dictionary<long, int>(EdgeKeyComparer.Instance);'),
        ('                DMesh3 mesh = new DMesh3();',
         '                Phase(op, "reconstruct_mesh");\n                DMesh3 mesh = new DMesh3();'),
        ('                double volume = 0;',
         '                Phase(op, "volume_and_zero_area");\n                double volume = 0;'),
        ('                r[op + "_selfint"] =',
         '                Phase(op, "self_intersection_count");\n                r[op + "_selfint"] ='),
        ('MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections);',
         'ProbeNotCleanReason(mesh, checkSelfIntersections, op);'),
        ('                    r[op + "_not_clean_exception"] = diagnosticError.GetType().Name + ": " + diagnosticError.Message;\n                }',
         '                    r[op + "_not_clean_exception"] = diagnosticError.GetType().Name + ": " + diagnosticError.Message;\n                } finally {\n                    FinishPhase();\n                }'),
        ('            Console.WriteLine(JsonSerializer.Serialize(r));\n        }\n        return 0;\n    }\n}',
         '            FinishPhase();\n            Console.WriteLine(JsonSerializer.Serialize(r));\n        }\n        return 0;\n    }\n}'),
    ]
    for old, new in changes:
        if source.count(old) != 1:
            raise ValueError('Frozen source anchor changed: ' + old)
        source = source.replace(old, new)
    return source


def prepare(destination, expected_hash):
    destination = destination.resolve()
    if destination.exists() or destination.is_relative_to(ROOT.parents[1]):
        raise ValueError('Use a new external source destination')
    source = SOURCE.read_bytes()
    if hashlib.sha256(source).hexdigest() != expected_hash:
        raise ValueError('Frozen driver source SHA256 mismatch')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open('x', encoding='utf-8', newline='\n') as f:
        f.write(instrument(source.decode('utf-8').replace('\r\n', '\n')))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--source-sha256', required=True)
    args = parser.parse_args()
    prepare(args.destination, args.source_sha256)
