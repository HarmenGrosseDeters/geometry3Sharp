"""Integration checks for the mesh-operation harness (set MESH_FUZZ_DLL and DOTNET_HOST_PATH)."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

DLL = os.environ['MESH_FUZZ_DLL']
DOTNET = os.environ.get('DOTNET_HOST_PATH', 'dotnet')


def run(*args):
    # (a time limit: a hanging seed or input fails the test instead of stalling the CI job)
    return subprocess.run([DOTNET, DLL, *args], capture_output=True, text=True, timeout=300)


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
            self.assertEqual(len(seeds), 18)
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
            for n, data in [(0, b'')] + [(op, bytes([op, 1, 2])) for op in range(6)]:
                p = Path(tmp) / f'short{n}-{len(data)}.bin'
                p.write_bytes(data)
                r = run('replay', str(p))
                self.assertEqual(r.returncode, 0, r.stderr)

    def test_usage_error(self):
        self.assertEqual(run('no-such-mode').returncode, 2)


if __name__ == '__main__':
    unittest.main()
