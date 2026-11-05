"""Compile isolated edge-hash mutants against the measured library; never edit deployed assets."""
import argparse
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import prepare_probe
from test_pipeline import ProbePreparationTests

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / 'probe' / 'edge-hash' / 'EdgeHashBenchmark.csproj'
SOURCE = ROOT / 'probe' / 'EdgeKeyComparer.cs'


def main(library, report):
    source = SOURCE.read_text(encoding='utf-8')
    mutants = [
        ('default_xor_hash', 'new Index2i((int)(key >> 32), (int)key).GetHashCode()',
         'key.GetHashCode()', 'Correlated keys still collapse'),
        ('hash_as_identity', 'x == y', 'GetHashCode(x) == GetHashCode(y)',
         'Hash collisions must not define equality'),
    ]
    evidence = []
    with tempfile.TemporaryDirectory(prefix='thingi_hash_mutation_') as temp:
        for name, old, new, expected in mutants:
            if source.count(old) != 1:
                raise RuntimeError('Mutation anchor changed: ' + name)
            directory = Path(temp) / name
            directory.mkdir()
            mutant = directory / 'EdgeKeyComparer.cs'
            mutant.write_text(source.replace(old, new), encoding='utf-8')
            build = ['dotnet', 'build', str(PROJECT), '-c', 'Release', '--no-restore',
                     '-p:FrozenLibrary=' + str(library), '-p:HashComparerSource=' + str(mutant),
                     '-o', str(directory / 'bin')]
            subprocess.run(build, capture_output=True, text=True, timeout=120, check=True)
            command = ['dotnet', str(directory / 'bin' / 'BooleanComparison.dll'), '--self-test']
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            if result.returncode == 0 or expected not in result.stderr:
                raise AssertionError('Mutation was not detected: ' + name)
            evidence.append({'mutation': name, 'build_command': subprocess.list2cmdline(build),
                             'test_command': subprocess.list2cmdline(command), 'detected': True,
                             'exit_code': result.returncode, 'failure': result.stderr.strip()})
    original = prepare_probe.instrument
    def reverted(text):
        return original(text).replace('new Dictionary<long, int>(EdgeKeyComparer.Instance)',
                                      'new Dictionary<long, int>()')
    stream = io.StringIO()
    with patch.object(prepare_probe, 'instrument', side_effect=reverted):
        result = unittest.TextTestRunner(stream=stream).run(unittest.TestSuite([
            ProbePreparationTests('test_edge_hash_change_preserves_keys_direction_counts_and_frozen_driver')]))
    if len(result.failures) != 1 or result.errors:
        raise AssertionError('Reverting the evaluator dictionary was not detected')
    evidence.append({'mutation': 'probe_default_comparer', 'detected': True,
                     'failure': stream.getvalue()})
    report.write_text(json.dumps({'command': subprocess.list2cmdline([sys.executable, *sys.argv]),
                                 'mutations': evidence, 'passed': True}, indent=2), encoding='utf-8')
    print(f'Edge-hash mutations: {len(evidence)} detected; deployed files untouched.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frozen-library', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    main(args.frozen_library.resolve(), args.report)
