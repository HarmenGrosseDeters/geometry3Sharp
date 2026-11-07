"""Controlled test-helper faults; restores sources and runs the full property suite.
From repository root: python3 tests/geometry3Sharp.Tests/property_validation/check_mutations.py /path/to/dotnet /tmp/property-mutations.json
No production source or mutation infrastructure is modified.
"""
import json
import pathlib
import subprocess
import sys

helper = pathlib.Path('tests/geometry3Sharp.Tests/Harness/BooleanOracle.cs')
properties = pathlib.Path('tests/geometry3Sharp.Tests/ExactBooleanPropertyTests.CsCheck.cs')
original_bytes = {p: p.read_bytes() for p in (helper, properties)}
original = {p: content.decode("utf-8") for p, content in original_bytes.items()}
base = [sys.argv[1], 'test', 'tests/geometry3Sharp.Tests', '-c', 'Release', '-f', 'net10.0', '--no-restore']
mutants = [
    ('empty_before_finiteness', helper, 'if (mesh == null) return "null mesh";',
     'if (mesh == null) return "null mesh";\n            if (mesh.TriangleCount == 0) return "";',
     'FullyQualifiedName~EmptyResult_StillRequiresFiniteCoordinates'),
    ('vacuous_family_check', properties, 'return valid ? "" : "generator left " + family + ": " + c;',
     'return "";', 'FullyQualifiedName~DirectedFamilyCheck_RejectsWrongRelations'),
]
rows = []
try:
    for name, path, before, after, test_filter in mutants:
        assert original[path].count(before) == 1
        path.write_text(original[path].replace(before, after))
        command = base + ['--filter', test_filter]
        run = subprocess.run(command, capture_output=True, text=True)
        summary = [line for line in run.stdout.splitlines() if 'Failed!' in line or 'Passed!' in line]
        rows.append(dict(mutation=name, command=command, exit_code=run.returncode, summary=summary))
        path.write_bytes(original_bytes[path])
        assert run.returncode != 0 and any('Failed!' in line for line in summary), run.stdout[-6000:] + run.stderr
finally:
    for path, content in original_bytes.items():
        path.write_bytes(content)
    command = base + ['--filter', 'FullyQualifiedName~ExactBooleanProperty|FullyQualifiedName~ExactBooleanMetamorphic',
                      '--logger', 'trx;LogFileName=boolean-properties-after.trx', '--results-directory', '/tmp/boolean-properties-final']
    run = subprocess.run(command, capture_output=True, text=True)
    rows.append(dict(mutation='restored', command=command, exit_code=run.returncode,
                     summary=[line for line in run.stdout.splitlines() if 'Failed!' in line or 'Passed!' in line]))
    pathlib.Path(sys.argv[2]).write_text(json.dumps(rows, indent=2)+'\n')
    assert run.returncode == 0, run.stdout[-10000:] + run.stderr
print('Both controlled faults detected; restored full property suite passes')
