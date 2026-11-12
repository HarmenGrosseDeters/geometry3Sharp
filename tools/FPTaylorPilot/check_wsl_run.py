"""Checks of wsl_run.sh's control flow with a stub FPTaylor (no network, no toolchain).

Not named test_*.py on purpose: wsl_run.sh runs the test_*.py files itself, and this file runs wsl_run.sh.
Run with: python -m unittest check_wsl_run
"""
from pathlib import Path
import os
import shutil
import sys
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASE = 'FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381'
# 2^-53 is below the ux bound (2^-52) and the nx bound (about 3.55e-15); the decimal agrees with the hex.
GOOD = 'Absolute error (exact): 1.110223e-16 (0x1p-53)'
TOO_LARGE = 'Absolute error (exact): 1.000000e-12 (' + float(1e-12).hex() + ')'


def environment():
    # the interpreter running these checks; Windows' python3 alias is not a real interpreter
    # the unit tests of test_*.py need Linux (they run scripts directly); this check runs them on Linux via wsl_run.sh
    return {**os.environ, 'PYTHON': Path(sys.executable).as_posix(), 'WSL_RUN_SKIP_UNIT_TESTS': '1'}


@unittest.skipIf(os.name == 'nt' or shutil.which('bash') is None, 'needs Linux: the stub is run directly')
class WslRunTests(unittest.TestCase):
    def run_script(self, report, extra=()):
        with tempfile.TemporaryDirectory() as directory:
            install = Path(directory) / 'install'
            base = install / BASE
            base.mkdir(parents=True)
            executable = base / 'fptaylor'
            executable.write_text('#!/bin/sh\nprintf "%s\\n" ' + "'" + report + "'" + '\n')
            executable.chmod(0o755)
            return subprocess.run(['bash', str(HERE / 'wsl_run.sh'), str(ROOT), str(install), *extra],
                                  text=True, capture_output=True, env=environment())

    def test_stub_passes_all_steps_and_reports_it(self):
        result = self.run_script(GOOD)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('wsl_run: all steps passed', result.stdout)
        self.assertIn('reusing FPTaylor installation', result.stdout)

    def test_a_too_large_bound_fails_the_run(self):
        result = self.run_script(TOO_LARGE)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('wsl_run: all steps passed', result.stdout)

    def test_existing_directory_without_fptaylor_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(['bash', str(HERE / 'wsl_run.sh'), str(ROOT), directory],
                                    text=True, capture_output=True)
            self.assertEqual(result.returncode, 2)
            self.assertIn('use a fresh directory', result.stderr)

    def test_bad_arguments(self):
        for args in ((), ('only-one',), ('a', 'b', 'c', 'd')):
            with self.subTest(args=args):
                result = subprocess.run(['bash', str(HERE / 'wsl_run.sh'), *args], capture_output=True)
                self.assertEqual(result.returncode, 2)
        result = self.run_script(GOOD, extra=('--bogus',))
        self.assertEqual(result.returncode, 2)


if __name__ == '__main__':
    unittest.main()
