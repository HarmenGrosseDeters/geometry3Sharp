"""Integration checks for the reader harness (set READER_FUZZ_DLL and DOTNET_HOST_PATH)."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
DLL = os.environ['READER_FUZZ_DLL']
DOTNET = os.environ.get('DOTNET_HOST_PATH', 'dotnet')


class PilotTests(unittest.TestCase):
    def test_seed_is_valid_and_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            seed = Path(tmp) / 'triangle.stl'
            command = [sys.executable, str(HERE / 'generate_seed.py'), tmp]
            subprocess.run(command, check=True, capture_output=True)
            data = seed.read_bytes()
            self.assertEqual(len(data), 134)
            self.assertEqual(subprocess.run([DOTNET, DLL, 'verify-seed', str(seed)], capture_output=True).returncode, 0)
            self.assertNotEqual(subprocess.run(command, capture_output=True).returncode, 0)
            self.assertEqual(seed.read_bytes(), data)

    def test_short_binary_is_rejected_as_seed_but_replay_accepts_parse_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            seed = Path(tmp) / 'short.stl'
            seed.write_bytes(bytes(81))
            self.assertNotEqual(subprocess.run([DOTNET, DLL, 'verify-seed', str(seed)], capture_output=True).returncode, 0)
            self.assertEqual(subprocess.run([DOTNET, DLL, 'replay', str(seed)], capture_output=True).returncode, 0)

    def test_swallowed_exception_is_not_accepted(self):
        self.assertEqual(subprocess.run([DOTNET, DLL, 'policy-self-test'], capture_output=True).returncode, 0)


if __name__ == '__main__':
    unittest.main()
