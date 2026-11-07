import csv
import json
from pathlib import Path
import tempfile
import unittest

import analyze


class AnalysisTests(unittest.TestCase):
    def test_unknown_self_intersections_are_not_certified_clean(self):
        for count in (-1, None, False, 0.0):
            self.assertEqual(analyze.input_group({'input': {'closed': True, 'selfint': count}}),
                             'closed_self_intersections_unknown')
        self.assertEqual(analyze.input_group({'input': {'closed': True, 'selfint': 0}}),
                         'closed_no_self_intersections')
        self.assertEqual(analyze.input_group({'input': {'closed': True, 'selfint': 1}}),
                         'closed_self_intersecting')
        self.assertEqual(analyze.input_group({'input': {'closed': False, 'wclosed': True, 'selfint': 0}}),
                         'open_or_weakly_closed')
        self.assertEqual(analyze.input_group({}), 'input_unknown')

    def test_timing_preserves_unknowns_and_uses_nearest_rank(self):
        self.assertEqual(analyze.timing([None, float('nan'), -1, False])['timed_count'], 0)
        result = analyze.timing([None, 0, 1000, 2000, 3000])
        self.assertEqual(result, dict(timed_count=4, median_seconds=1.5,
                                     p95_seconds=3.0, max_seconds=3.0))

    def test_report_preserves_source_and_separates_unknown_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'source'
            (source / 'results').mkdir(parents=True)
            records = []
            for number, intersections in enumerate((0, -1)):
                raw = dict(file=f'{number}.stl', commit=analyze.runner.COMMIT,
                           input=dict(closed=True, selfint=intersections, vA=1, vB=1))
                for op in analyze.runner.OPS:
                    raw.update({op + '_status': 'ok', op + '_finite': True,
                                op + '_wclosed': True, op + '_manifold_closed': True,
                                op + '_degenerate': 0, op + '_zero_area': 0,
                                op + '_selfint': 0, op + '_not_clean_self_intersections_checked': True,
                                op + '_not_clean_reason': '', op + '_tris': 4, op + '_ms': None})
                raw.update(union_not_clean_reason='bowties', intersection_tris=0,
                           intersection_not_clean_reason='empty', difference_ms=0)
                records.append(raw)
            (source / 'results' / 'boolean.jsonl').write_text(
                ''.join(json.dumps(row) + '\n' for row in records), encoding='utf-8')
            (source / 'results' / 'wrap.jsonl').write_text('', encoding='utf-8')
            before = {path: path.read_bytes() for path in source.rglob('*.jsonl')}
            destination = Path(directory) / 'report'
            result = analyze.analyze(source, destination)
            self.assertEqual(result['boolean']['operations'], 6)
            self.assertEqual(result['boolean']['candidate_operations'], 2)
            self.assertEqual(result['boolean']['empty_operations'], 2)
            self.assertEqual(result['boolean']['timing']['timed_count'], 2)
            for group in ('closed_no_self_intersections', 'closed_self_intersections_unknown'):
                self.assertEqual(result['boolean']['groups'][group]['statuses'], {'unclean': 1, 'ok': 2})
            with (destination / 'candidates.csv').open(newline='', encoding='utf-8') as stream:
                rows = list(csv.DictReader(stream))
            self.assertEqual({row['input_group'] for row in rows},
                             {'closed_no_self_intersections', 'closed_self_intersections_unknown'})
            self.assertTrue(all(row['time_ms'] == '' for row in rows))
            for path, content in before.items():
                self.assertEqual(path.read_bytes(), content)
            with self.assertRaises(FileExistsError):
                analyze.analyze(source, destination)


if __name__ == '__main__':
    unittest.main()
