import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import run_full as runner
import test_runner


def main():
    (runner.ROOT / 'scratch').mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='procedural_smoke_', dir=runner.ROOT / 'scratch') as directory:
        mesh = Path(directory) / 'procedural_tetra.off'
        mesh.write_text('OFF\n4 4 0\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n3 0 2 1\n3 0 1 3\n3 0 3 2\n3 1 2 3\n', encoding='utf-8')
        runner.LOCAL.logs = runner.ROOT / 'validation' / 'procedural_tetra'
        wrap = runner.run_wrap(mesh, 120)
        boolean = runner.run_boolean(mesh)
        summary = {'command': 'C:\\Python314\\python.exe smoke.py', 'commit': runner.COMMIT,
                   'wrap': wrap, 'boolean': boolean,
                   'wrap_summary': runner.operation_rows('wrap', wrap),
                   'boolean_summary': runner.operation_rows('boolean', boolean)}
        runner.atomic_json(runner.ROOT / 'smoke.json', summary)
        assert runner.normalize_wrap(wrap) == ('ok', ''), summary['wrap_summary']
        for op in runner.OPS:
            assert boolean[op + '_status'] == 'ok', summary['boolean_summary']
            for suffix in ('finite', 'wclosed', 'manifold_closed'):
                assert boolean[op + '_' + suffix] is True, (op, suffix)
            assert boolean[op + '_selfint'] == 0, op
            assert op + '_not_clean_reason' in boolean, op
        print(json.dumps({'wrap': summary['wrap_summary'], 'boolean': summary['boolean_summary']}))
    with patch.object(runner, 'normalize_boolean', return_value=('ok', '')):
        suite = unittest.TestSuite([test_runner.RunnerTests('test_boolean_quality_unknown_is_not_success')])
        result = unittest.TestResult()
        suite.run(result)
        assert not result.wasSuccessful(), 'Mutation was not detected'
        print('Mutation check: forced success was detected by the unknown-quality test.')


if __name__ == '__main__':
    main()
