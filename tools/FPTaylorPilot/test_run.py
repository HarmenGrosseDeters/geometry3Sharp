"""Tests for the fail-closed FPTaylor runner, without the optional toolchain."""
import shlex
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent


class RunnerTests(unittest.TestCase):
    def invoke(self, payload, status=0):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            executable = base / 'fptaylor'
            executable.write_text('#!/bin/sh\nprintf "%s\\n" ' + shlex.quote(payload) + '\nexit ' + str(status) + '\n')
            executable.chmod(0o755)
            return subprocess.run([str(HERE / 'run.sh'), str(base), str(HERE / 'smoke.txt')],
                                  text=True, capture_output=True)

    def test_success_requires_one_finite_result(self):
        self.assertEqual(self.invoke('Absolute error (exact): 2.220447e-16').returncode, 0)
        for value in ('nan', 'inf', '-1', '-1e-9999', 'nonsense'):
            self.assertNotEqual(self.invoke('Absolute error (exact): ' + value).returncode, 0)
        self.assertNotEqual(self.invoke('no result').returncode, 0)
        self.assertNotEqual(self.invoke('Absolute error (exact): 1\nAbsolute error (exact): 1').returncode, 0)

    def test_tool_failure_does_not_pass_with_a_result(self):
        self.assertNotEqual(self.invoke('Absolute error (exact): 1e-12', 1).returncode, 0)

    def test_corrupt_download_fails_before_extraction(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / 'ocaml-4.14.2.tar.gz').write_bytes(b'corrupt')
            result = subprocess.run([str(HERE / 'install.sh'), directory], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((Path(directory) / 'ocaml-4.14.2').exists())

    def test_missing_args_and_tool(self):
        self.assertEqual(subprocess.run([str(HERE / 'run.sh')], capture_output=True).returncode, 2)
        self.assertEqual(subprocess.run([str(HERE / 'install.sh')], capture_output=True).returncode, 2)
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(subprocess.run([str(HERE / 'run.sh'), directory, str(HERE / 'smoke.txt')],
                                            capture_output=True).returncode, 2)


if __name__ == '__main__':
    unittest.main()
