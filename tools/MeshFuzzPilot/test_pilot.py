"""Integration checks for the mesh-operation harness (set MESH_FUZZ_DLL and DOTNET_HOST_PATH)."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

DLL = os.environ['MESH_FUZZ_DLL']
DOTNET = os.environ.get('DOTNET_HOST_PATH', 'dotnet')


def run(*args, ops=None):
    # (a time limit: a hanging seed or input fails the test instead of stalling the CI job)
    env = dict(os.environ)
    env.pop('MESH_FUZZ_OPS', None)
    if ops is not None:
        env['MESH_FUZZ_OPS'] = ops
    return subprocess.run([DOTNET, DLL, *args], capture_output=True, text=True, timeout=300, env=env)


class PilotTests(unittest.TestCase):
    def test_self_test(self):
        # the decoder is deterministic, the seeds cover every operation and run without a finding
        r = run('self-test')
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_seeds_are_written_once_and_replay_cleanly(self):
        with tempfile.TemporaryDirectory() as tmp:
            corpus = Path(tmp) / 'corpus'
            self.assertEqual(run('write-seeds', str(corpus)).returncode, 0)
            seeds = sorted(corpus.iterdir())
            self.assertEqual(len(seeds), 132)
            before = {p.name: p.read_bytes() for p in seeds}
            # a directory that is not empty is refused and left as it is
            self.assertNotEqual(run('write-seeds', str(corpus)).returncode, 0)
            self.assertEqual({p.name: p.read_bytes() for p in sorted(corpus.iterdir())}, before)
            for seed in seeds:
                r = run('replay', str(seed))
                self.assertEqual(r.returncode, 0, seed.name + ': ' + r.stderr)

    def test_short_and_empty_inputs_decode(self):
        with tempfile.TemporaryDirectory() as tmp:
            # empty, and short inputs whose first byte selects each operation (the rest decodes as zeros)
            for n, data in [(0, b'')] + [(op, bytes([op, 1, 2])) for op in range(22)]:
                p = Path(tmp) / f'short{n}-{len(data)}.bin'
                p.write_bytes(data)
                r = run('replay', str(p))
                # (a short input decodes at the scale 2^-1022: a Finding is a result of the operation, which this test does
                # not judge; any other exception is a harness error)
                self.assertTrue(r.returncode == 0 or 'Program+Finding' in r.stderr, r.stderr)

    def test_ops_filter_selects_the_operation_and_replay_needs_it(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'in.bin'
            p.write_bytes(bytes([1, 0, 0]))
            # the first byte selects among the selected operations: 1 % 2 is the second of the two
            r = run('replay', str(p), ops='tree-ray,alpha-wrap')
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn('alpha-wrap', r.stdout)
            r = run('replay', str(p), ops='5,tree-ray')
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn('tree-ray', r.stdout)
            # an unknown name is refused
            self.assertNotEqual(run('replay', str(p), ops='no-such-operation').returncode, 0)
            # the seeds are written for the selected operations only, and replay with the filter
            corpus = Path(tmp) / 'corpus'
            self.assertEqual(run('write-seeds', str(corpus), ops='tree-nearest,tree-ray').returncode, 0)
            seeds = sorted(corpus.iterdir())
            self.assertEqual(len(seeds), 12)
            self.assertTrue(all(x.name.startswith(('tree-nearest', 'tree-ray')) for x in seeds))
            for seed in seeds:
                r = run('replay', str(seed), ops='tree-nearest,tree-ray')
                self.assertEqual(r.returncode, 0, seed.name + ': ' + r.stderr)

    def test_tree_nearest_judges_the_raw_tree_in_raw_space(self):
        # a campaign artifact (110 bytes) the raw-scale judgement used to report ("nearest triangle 14 at squared distance 4.4e-278, the
        # minimum is 0"): the raw tree is right, the distance of its triangle was recomputed on the normalised copy and compared exactly
        data = bytes.fromhex('000503fe03fe00000000003000000000000000ffffffff0000000000000000000000000000000000000007040a020e030a050f0315040d06ff000000000000000000000000000000000000ffffffffffffff7300000102000000010403050107ffffffff14041c000203fe03fe00')
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'raw-scale.bin'
            p.write_bytes(data)
            r = run('replay', str(p), ops='tree-nearest')
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn('tree-nearest', r.stdout)      # the operation ran (the case was decoded and judged)

    def test_dump_prints_the_case_with_round_trip_coordinates(self):
        with tempfile.TemporaryDirectory() as tmp:
            corpus = Path(tmp) / 'corpus'
            self.assertEqual(run('write-seeds', str(corpus)).returncode, 0)
            r = run('dump', str(corpus / 'tree-ray-0.bin'))
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn('A vertices:', r.stdout)
            self.assertIn('A triangles:', r.stdout)
            self.assertEqual(r.stdout.count('query '), 8)

    def test_usage_error(self):
        self.assertEqual(run('no-such-mode').returncode, 2)


if __name__ == '__main__':
    unittest.main()
