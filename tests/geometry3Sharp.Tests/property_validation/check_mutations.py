"""Controlled test-helper faults; restores sources and runs the full property suite.
From repository root: python3 tests/geometry3Sharp.Tests/property_validation/check_mutations.py /path/to/dotnet /tmp/property-mutations.json
No production source or mutation infrastructure is modified. The report holds commands with a <dotnet>
placeholder and failed/passed counts only (no paths, durations or logs).
"""
import json
import pathlib
import re
import subprocess
import sys
import tempfile

helper = pathlib.Path('tests/geometry3Sharp.Tests/Harness/BooleanOracle.cs')
properties = pathlib.Path('tests/geometry3Sharp.Tests/ExactBooleanPropertyTests.CsCheck.cs')
original_bytes = {p: p.read_bytes() for p in (helper, properties)}
original = {p: content.decode("utf-8") for p, content in original_bytes.items()}
base = ['test', 'tests/geometry3Sharp.Tests', '-c', 'Release', '-f', 'net10.0', '--no-restore']
neighbours = 'FullyQualifiedName~DirectedFamilyCheck_RejectsNeighbouringRelations'
mutants = [
    ('empty_before_finiteness', helper, 'if (mesh == null) return "null mesh";',
     'if (mesh == null) return "null mesh";\n            if (mesh.TriangleCount == 0) return "";',
     'FullyQualifiedName~EmptyResult_StillRequiresFiniteCoordinates'),
    ('vacuous_family_check', properties, 'return valid ? "" : "generator left " + family + ": " + c;',
     'return "";', 'FullyQualifiedName~DirectedFamilyCheck_RejectsWrongRelations'),
    ('rejecting_family_check', properties, 'return valid ? "" : "generator left " + family + ": " + c;',
     'return "generator left " + family + ": " + c;', 'FullyQualifiedName~DirectedFamilyCheck_AcceptsCanonicalCase'),
    # The pre-fix shrink source: candidates from CsCheck's per-process thread generator.
    ('nondeterministic_shrink', properties,
     'var pcg = new PCG(root.Stream, SplitMix64(root.Seed ^ ((ulong)root.Stream << 32) ^ SplitMix64((ulong)k + 1)));',
     'var pcg = new PCG(root.Stream, PCG.ThreadPCG.Next64());', 'FullyQualifiedName~Shrink_IsDeterministicForAGivenSeed'),
    # Weakened classifiers aimed at a neighbouring family. Seven of these eight still pass
    # DirectedFamilyCheck_RejectsWrongRelations (only coplanar_accepts_containment fails it);
    # the neighbour controls must reject all eight.
    ('touching_any_contact_count', properties, 'overlap.Count(x => x == 0) == count;',
     'overlap.Count(x => x == 0) >= 1;', neighbours),
    ('disjoint_allows_contact', properties, 'valid = overlap.Any(x => x < 0);',
     'valid = overlap.Any(x => x <= 0);', neighbours),
    ('identical_accepts_containment', properties, 'case "identical": valid = identical;',
     'case "identical": valid = containsA || containsB;', neighbours),
    ('strict_nested_accepts_shared_faces', properties,
     'case "strict_nested": valid = Enumerable.Range(0, 3).All(j => c.A.Lo(j) < c.B.Lo(j) && c.A.Hi(j) > c.B.Hi(j));',
     'case "strict_nested": valid = containsA && !identical;', neighbours),
    ('overlapping_accepts_shared_planes', properties,
     'Enumerable.Range(0, 3).All(j => c.A.Lo(j) != c.B.Lo(j) && c.A.Hi(j) != c.B.Hi(j));',
     'true;', neighbours),
    ('overlapping_accepts_containment', properties,
     'case "overlapping": valid = overlap.All(x => x > 0) && !containsA && !containsB &&',
     'case "overlapping": valid = overlap.All(x => x > 0) &&', neighbours),
    ('coplanar_one_shared_plane', properties,
     'Any(j => c.A.Lo(j) == c.B.Lo(j) && c.A.Hi(j) == c.B.Hi(j))',
     'Any(j => c.A.Lo(j) == c.B.Lo(j) || c.A.Hi(j) == c.B.Hi(j))', neighbours),
    ('coplanar_accepts_containment', properties,
     'case "coplanar_overlap": valid = overlap.All(x => x > 0) && !containsA && !containsB &&',
     'case "coplanar_overlap": valid = overlap.All(x => x > 0) &&', neighbours),
]


def counts(lines):
    for line in lines:
        m = re.search(r'Failed:\s+(\d+), Passed:\s+(\d+)', line)
        if m and ('Failed!' in line or 'Passed!' in line):
            return dict(failed=int(m.group(1)), passed=int(m.group(2)))
    return dict(failed=None, passed=None)


def shown(command):
    return ' '.join(['dotnet'] + [("'" + a + "'") if '|' in a else a for a in command])


rows = []
try:
    for name, path, before, after, test_filter in mutants:
        assert original[path].count(before) == 1, name
        path.write_text(original[path].replace(before, after))
        command = base + ['--filter', test_filter]
        run = subprocess.run([sys.argv[1]] + command, capture_output=True, text=True)
        rows.append(dict(mutation=name, command=shown(command), exit_code=run.returncode, **counts(run.stdout.splitlines())))
        path.write_bytes(original_bytes[path])
        assert run.returncode != 0 and rows[-1]['failed'], run.stdout[-6000:] + run.stderr
finally:
    for path, content in original_bytes.items():
        path.write_bytes(content)
    command = base + ['--filter', 'FullyQualifiedName~ExactBooleanProperty|FullyQualifiedName~ExactBooleanMetamorphic']
    with tempfile.TemporaryDirectory() as results:
        run = subprocess.run([sys.argv[1]] + command + ['--results-directory', results], capture_output=True, text=True)
    rows.append(dict(mutation='restored', command=shown(command), exit_code=run.returncode, **counts(run.stdout.splitlines())))
    pathlib.Path(sys.argv[2]).write_text(json.dumps(dict(results=rows), indent=1)+'\n')
    assert run.returncode == 0, run.stdout[-10000:] + run.stderr
print('All controlled faults detected; restored full property suite passes')
