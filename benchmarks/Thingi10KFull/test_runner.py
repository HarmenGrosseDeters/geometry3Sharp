import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import run_full as runner


def clean_boolean():
    row = {'file': '123.off'}
    for op in runner.OPS:
        row.update({op + '_status': 'ok', op + '_ms': 5, op + '_wall_ms': 8,
                    op + '_tris': 4, op + '_finite': True, op + '_wclosed': True,
                    op + '_manifold_closed': True, op + '_degenerate': 0, op + '_zero_area': 0,
                    op + '_not_clean_reason': '', op + '_not_clean_self_intersections_checked': True,
                    op + '_selfint': 0})
    return row


class RunnerTests(unittest.TestCase):
    def test_atomic_json_retries_transient_windows_replace_lock(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'progress.json'
            path.write_text('{"state": "old"}', encoding='utf-8')
            original_replace = Path.replace
            attempts = []

            def replace(source, target):
                attempts.append(source)
                if len(attempts) < 3:
                    self.assertEqual(json.loads(path.read_text())['state'], 'old')
                    error = PermissionError('temporarily locked')
                    error.winerror = 5
                    raise error
                return original_replace(source, target)

            with patch.object(Path, 'replace', autospec=True, side_effect=replace), \
                 patch.object(runner.time, 'sleep'):
                runner.atomic_json(path, {'state': 'running'})
            self.assertEqual(len(attempts), 3)
            self.assertEqual(json.loads(path.read_text()), {'state': 'running'})
            self.assertFalse(path.with_suffix('.json.tmp').exists())

    def test_atomic_json_permanent_windows_lock_is_bounded(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'progress.json'
            path.write_text('{"state": "old"}', encoding='utf-8')
            error = PermissionError('persistently locked')
            error.winerror = 5
            with patch.object(Path, 'replace', side_effect=error) as replace, \
                 patch.object(runner.time, 'sleep'):
                with self.assertRaises(PermissionError):
                    runner.atomic_json(path, {'state': 'running'})
            self.assertEqual(replace.call_count, 8)
            self.assertEqual(json.loads(path.read_text())['state'], 'old')

    def test_atomic_json_other_errors_are_not_retried(self):
        with tempfile.TemporaryDirectory() as directory:
            error = OSError('unrelated failure')
            with patch.object(Path, 'replace', side_effect=error) as replace, \
                 patch.object(runner.time, 'sleep') as sleep:
                with self.assertRaises(OSError):
                    runner.atomic_json(Path(directory) / 'progress.json', {})
            self.assertEqual(replace.call_count, 1)
            sleep.assert_not_called()

    def test_wrap_requires_actual_clean_finite_valid_checked_result(self):
        row = dict(status='closed', wrap_status='Success', after_finite=True, after_valid=True,
                   not_clean_reason='', not_clean_self_intersections_checked=True)
        self.assertEqual(runner.normalize_wrap(row), ('ok', ''))
        for key in ('after_finite', 'after_valid', 'not_clean_reason'):
            bad = dict(row)
            del bad[key]
            self.assertNotEqual(runner.normalize_wrap(bad)[0], 'ok')
        row['not_clean_self_intersections_checked'] = False
        self.assertEqual(runner.normalize_wrap(row)[0], 'quality_unchecked')

    def test_wrap_failure_reason_and_exception_are_preserved(self):
        self.assertEqual(runner.normalize_wrap(dict(status='closed', wrap_status='Success',
                         not_clean_reason='bowties')), ('unclean', 'bowties'))
        self.assertEqual(runner.normalize_wrap(dict(status='exception', error='InvalidOperationException: bad')),
                         ('exception', 'InvalidOperationException: bad'))

    def test_boolean_quality_unknown_is_not_success(self):
        row = clean_boolean()
        self.assertEqual(runner.normalize_boolean(row, 'union'), ('ok', ''))
        row['union_selfint'] = -1
        self.assertEqual(runner.normalize_boolean(row, 'union')[0], 'quality_unchecked')
        del row['union_finite']
        self.assertEqual(runner.normalize_boolean(row, 'union')[0], 'validation_missing')

    def test_boolean_empty_is_valid_but_nonempty_unclean_is_failure(self):
        row = clean_boolean()
        row.update(intersection_tris=0, intersection_not_clean_reason='empty')
        self.assertEqual(runner.normalize_boolean(row, 'intersection'), ('ok', ''))
        row['union_not_clean_reason'] = 'degenerate triangles'
        self.assertEqual(runner.normalize_boolean(row, 'union'), ('unclean', 'degenerate triangles'))

    def test_download_failure_and_timeout_never_get_invented_algorithm_times(self):
        row = dict(file='123.off', status='download_failed', error='IOError: bad download')
        converted = runner.operation_rows('boolean', row)
        self.assertTrue(all(r['thingi_id'] == '123' and r['status'] == 'download_failed'
                            and r['time_ms'] is None for r in converted))
        row = clean_boolean()
        row.update(union_status='timeout', union_error='TimeoutExpired: 300 seconds')
        del row['union_ms']
        self.assertEqual(runner.normalize_boolean(row, 'union')[0], 'timeout')
        self.assertIsNone(runner.model_row('boolean', row)['time_ms'])

    def test_resume_recovers_final_partial_write_and_retries_download_failures(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'results.jsonl'
            path.write_text(json.dumps({'file': '123.off'}) + '\n' +
                            json.dumps({'file': '456.off', 'status': 'download_failed'}) + '\n{"file":', encoding='utf-8')
            self.assertEqual(set(runner.load_results(path)), {'123.off'})
            self.assertEqual(set(runner.load_results(path)), {'123.off'})
            self.assertTrue(path.read_text().endswith('\n'))

    def test_resume_rejects_non_final_corruption(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'results.jsonl'
            path.write_text('broken\n{"file":"123.off"}\n')
            with self.assertRaises(RuntimeError):
                runner.load_results(path)

    def test_eval_failure_keeps_individual_boolean_results_and_times(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'scratch').mkdir()
            result = subprocess.CompletedProcess([], 0, stdout='{"file":"123.off","tris":4}\n', stderr='')
            with patch.object(runner, 'ROOT', root), patch.object(runner.LoggedSubprocess, 'run', return_value=result), \
                 patch.object(runner.cmp, 'run_one', return_value={'status': 'ok', 'ms': 7}), \
                 patch.object(runner.cmp, 'evaluate', side_effect=subprocess.TimeoutExpired('eval', 3600)):
                runner.LOCAL.last_process = result
                raw = runner.run_boolean(Path('123.off'))
            self.assertIn('TimeoutExpired', raw['evaluation_error'])
            for op in runner.OPS:
                self.assertEqual(raw[op + '_ms'], 7)
                self.assertEqual(runner.normalize_boolean(raw, op)[0], 'evaluation_failed')
            self.assertFalse(list((root / 'scratch').iterdir()))

    def test_export_exception_is_reported_for_every_operation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'scratch').mkdir()
            with patch.object(runner, 'ROOT', root), patch.object(runner.LoggedSubprocess, 'run',
                       side_effect=subprocess.TimeoutExpired('export', 1800)):
                raw = runner.run_boolean(Path('123.off'))
            self.assertTrue(all(r['status'] == 'not_run_export_failed' and 'TimeoutExpired' in r['reason']
                                for r in runner.operation_rows('boolean', raw)))
            self.assertGreaterEqual(raw['wall_ms'], 0)
            self.assertFalse(list((root / 'scratch').iterdir()))

    def test_existing_download_with_wrong_size_is_not_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '123.off'
            path.write_bytes(b'bad')
            with patch.object(runner.urllib.request, 'urlopen', side_effect=IOError('offline')), \
                 patch.object(runner.time, 'sleep'):
                with self.assertRaises(IOError):
                    runner.download('https://example.invalid', path, 4)
            self.assertFalse(path.with_suffix('.off.part').exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)
