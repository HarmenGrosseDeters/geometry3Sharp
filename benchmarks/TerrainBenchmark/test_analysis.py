"""Analyzer compatibility; integration is opt-in with TERRAIN_BENCHMARK_DLL.
Run: python3 -m unittest discover -s benchmarks/TerrainBenchmark -p 'test_*.py' -v
"""
import contextlib
import io
import json
import os
import pathlib
import struct
import subprocess
import tempfile
import unittest

import analyze


class AnalysisTests(unittest.TestCase):
    def test_old_and_new_rows(self):
        rows = []
        for case in ('tdc', 'gtin'):
            old = dict(case=case, status='ok', nodes=100, tdc_s=1, gtin_s=1,
                       peak_mb=10, dz_rms=2)
            new = dict(old, closest_status='partial', closest_valid=99,
                       closest_requested=100, closest_invalid=1, closest_missing=0,
                       closest_d3_rms=1.25, closest_dh_rms=1,
                       closest_dv_rms=.75)
            rows.extend((old, new))
        with tempfile.TemporaryDirectory() as d:
            path = pathlib.Path(d) / 'rows.jsonl'
            path.write_text('\n'.join(map(json.dumps, rows)))
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                analyze.main(str(path))
        text = out.getvalue()
        self.assertIn('dz rms m', text)
        self.assertIn('99/100', text)
        self.assertIn('1.25', text)
        self.assertIn('partial', text)
        self.assertIn('| - |', text)


@unittest.skipUnless(os.getenv('TERRAIN_BENCHMARK_DLL'), 'set TERRAIN_BENCHMARK_DLL for integration')
class IntegrationTests(unittest.TestCase):
    def test_procedural_grid_json(self):
        for case in ('tdc', 'gtin'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as d:
                path = pathlib.Path(d)
                (path / 'slope.json').write_text(json.dumps(dict(x0=0, y0=0, dx=1, nx=12, ny=12)))
                values = [float(i + j) for j in range(12) for i in range(12)]
                (path / 'slope.f32').write_bytes(struct.pack('<144f', *values))
                env = dict(os.environ, TERRAIN_DATA=d)
                proc = subprocess.run([os.getenv('DOTNET', 'dotnet'), os.environ['TERRAIN_BENCHMARK_DLL'],
                                       case, 'grid=slope', 'closest_samples=512'],
                                      env=env, capture_output=True, text=True, check=True)
                row = json.loads(proc.stdout.strip().splitlines()[-1])
                self.assertEqual('ok', row['status'], row)
                self.assertTrue(row['ok'])
                self.assertEqual('ok', row['closest_status'])
                self.assertEqual('m', row['closest_units'])
                self.assertEqual('result_to_reference', row['closest_direction'])
                self.assertEqual(0, row['closest_invalid'])
                self.assertEqual(0, row['closest_missing'])
                for component in ('d3', 'dh', 'dv'):
                    self.assertEqual(512, row[f'closest_{component}_n'])
                    self.assertLess(row[f'closest_{component}_max'], 1e-10)
                self.assertIn('dz_rms', row)
                self.assertIn('missed', row)
                self.assertIn('xy_overlaps', row)


if __name__ == '__main__':
    unittest.main()
