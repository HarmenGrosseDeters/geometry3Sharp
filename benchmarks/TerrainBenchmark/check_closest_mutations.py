"""Four test-local faults; restores the helper and reruns the correct implementation.
Run from repository root: python3 benchmarks/TerrainBenchmark/check_closest_mutations.py /path/to/dotnet /path/to/report.json
"""
import json
import pathlib
import re
import subprocess
import sys

source = pathlib.Path('benchmarks/TerrainBenchmark/ClosestFields.cs')
original = source.read_text()
mutants = {
    'vertical_instead_of_3d': ('length = d.Length;', 'length = Math.Abs(d.z);'),
    'zero_horizontal': ('double h = new Vector2d(d.x, d.y).Length', 'double h = 0'),
    'mixed_correspondence': ('double h = new Vector2d(d.x, d.y).Length', 'double h = new Vector2d(p.x, p.y).Length'),
    'capped_search': ('tree.FindNearestTriangle(p)', 'tree.FindNearestTriangle(p, 0.01)'),
}
command = [sys.argv[1], 'test', 'tests/geometry3Sharp.Tests', '-c', 'Release', '-f', 'net10.0', '--no-restore', '--filter', 'FullyQualifiedName~TerrainDistanceMetric']
report = []


def counts(lines):
    """Failed/passed counts of the dotnet test summary line (no durations or paths are recorded)."""
    for line in lines:
        m = re.search(r'Failed:\s+(\d+), Passed:\s+(\d+)', line)
        if m and ('Failed!' in line or 'Passed!' in line):
            return dict(failed=int(m.group(1)), passed=int(m.group(2)))
    return dict(failed=None, passed=None)


try:
    for name, (before, after) in mutants.items():
        assert original.count(before) == 1
        source.write_text(original.replace(before, after))
        run = subprocess.run(command, capture_output=True, text=True)
        lines = run.stdout.splitlines() + run.stderr.splitlines()
        summary = [l for l in lines if 'Failed!' in l or 'Passed!' in l]
        report.append(dict(mutation=name, exit_code=run.returncode, **counts(summary)))
        assert run.returncode != 0 and any('Failed!' in l for l in summary), '\n'.join(lines[-30:])
finally:
    source.write_text(original)
    run = subprocess.run(command, capture_output=True, text=True)
    report.append(dict(mutation='restored', exit_code=run.returncode, **counts(run.stdout.splitlines())))
    pathlib.Path(sys.argv[2]).write_text(json.dumps(dict(
        command='python3 benchmarks/TerrainBenchmark/check_closest_mutations.py <dotnet> <out>',
        test_command=' '.join(['dotnet'] + command[1:]),
        results=report), indent=1)+'\n')
    assert run.returncode == 0, run.stdout[-5000:] + run.stderr
print('All four controlled faults detected; restored implementation passes')
