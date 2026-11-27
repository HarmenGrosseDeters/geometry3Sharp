"""Fast offline checks for fixture generation and benchmark oracles."""
from fractions import Fraction
import json
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from common import load, mesh_stats
from corpus_extension import Run
from g3_run import box_bounds_match, box_probes
from generate import box, generate
from real_booleans import read_off


class BenchmarkTests(unittest.TestCase):
    def test_generation_is_repeatable_and_references_exist(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            first, second = root / 'first', root / 'second'
            self.assertEqual(generate(first), 52)
            self.assertEqual(generate(second), 52)
            manifest = json.loads((first / 'manifest.json').read_text())
            cases = manifest['cases']
            self.assertEqual(len({case['id'] for case in cases}), len(cases))
            for case in cases:
                for filename in case['inputs'].values():
                    self.assertEqual((first / filename).read_bytes(), (second / filename).read_bytes())
            self.assertEqual((first / 'manifest.json').read_bytes(), (second / 'manifest.json').read_bytes())

    def test_touch_and_overlap_have_distinct_regularized_results(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            generate(root)
            cases = {case['id']: case for case in json.loads((root / 'manifest.json').read_text())['cases']}
            overlaps = cases['B09-contact-transition-1']
            contact = cases['B09-contact-transition-2']
            self.assertGreater(Fraction(overlaps['regularized_volumes']['intersection']), 0)
            self.assertIn('intersection', contact['empty_operations'])
            self.assertEqual(mesh_stats(load(root / contact['inputs']['a']))['volume'], 1)
            self.assertEqual(mesh_stats(load(root / contact['inputs']['b']))['boundary_edges'], 0)

    def test_position_oracle_rejects_equal_volume_wrong_geometry(self):
        reference = [[(0, 0, 0), (1, 1, 1)], [(2, 0, 0), (3, 1, 1)]]
        actual = box()
        wrong = box((2, 0, 0), (3, 1, 1))
        self.assertEqual(mesh_stats(actual)['volume'], mesh_stats(wrong)['volume'])
        self.assertEqual(box_probes(actual, reference, 'difference_ab'), [])
        self.assertEqual(len(box_probes(wrong, reference, 'difference_ab')), 2)

    def test_box_bounds_catch_shift_invisible_to_midpoint_probes(self):
        operands = [[(0, 0, 0), (1, 1, 1)], [(2, 0, 0), (3, 1, 1)]]
        shifted = box((0.25, 0, 0), (1.25, 1, 1))
        self.assertEqual(box_probes(shifted, operands, 'difference_ab'), [])
        self.assertFalse(box_bounds_match(shifted, operands, 'difference_ab'))
        self.assertTrue(box_bounds_match(box(), operands, 'difference_ab'))

    def test_corpus_run_refuses_stale_output_after_process_failure(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            run = Run(SimpleNamespace(data=root, output=root / 'results', dotnet='dotnet',
                                      cli='g3cli.dll', corpus='test'))
            output = run.work / 'prior.obj'
            output.write_text('stale')
            failed = SimpleNamespace(returncode=1, stdout='{"status":"ok"}', stderr='')
            with patch('corpus_extension.subprocess.run', return_value=failed):
                row = run.run('case', 'wrap', 'wrap', root / 'input.obj', output)
            self.assertEqual(row['status'], 'process_error')
            self.assertFalse(output.exists())

    def test_containment_report_records_timed_out_wrap(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            commands = [{'case':'ABC-example','stage':'fine-wrap','status':'timeout',
                         'argv':['wrap','input.obj','missing.obj']}]
            (root / 'abc.json').write_text(json.dumps({'commands':commands}))
            script = Path(__file__).with_name('verify_corpus_wrap.py')
            subprocess.run([sys.executable,str(script),'unused-checker',str(root),'--corpus','abc'],
                           check=True,timeout=3)
            checks = json.loads((root / 'abc-containment.json').read_text())
            self.assertEqual(checks, [{'case':'ABC-example','stage':'fine-wrap',
                                       'status':'wrap_unavailable','wrap_status':'timeout'}])

    def test_off_accepts_same_line_header_from_openscad(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'tetrahedron.off'
            body = '4 4 0\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n3 0 2 1\n3 0 1 3\n3 0 3 2\n3 1 2 3\n'
            for separator in (' ', '\n'):
                path.write_text('OFF' + separator + body)
                stats = mesh_stats(read_off(path))
                self.assertEqual(stats['boundary_edges'], 0)
                self.assertAlmostEqual(stats['volume'], 1/6)


if __name__ == '__main__':
    unittest.main()
