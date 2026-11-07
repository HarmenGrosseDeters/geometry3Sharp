import csv
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import snapshot


class SnapshotTests(unittest.TestCase):
    def row(self, **values):
        return dict(file='123.off', commit=snapshot.runner.COMMIT, **values)

    def test_complete_prefix_ignores_unterminated_tail(self):
        row = self.row()
        data = json.dumps(row).encode() + b'\n'
        self.assertEqual(snapshot.complete_rows(data + b'{"file":'), [row])
        self.assertEqual(snapshot.complete_rows(data + json.dumps(self.row()).encode()), [row])

    def test_complete_prefix_rejects_corruption_duplicates_and_mixed_commits(self):
        valid = json.dumps(self.row()).encode() + b'\n'
        with self.assertRaises(json.JSONDecodeError):
            snapshot.complete_rows(b'broken\n' + valid)
        with self.assertRaises(ValueError):
            snapshot.complete_rows(valid + valid)
        row = self.row()
        row['commit'] = 'other'
        with self.assertRaises(ValueError):
            snapshot.complete_rows(json.dumps(row).encode() + b'\n')

    def test_capture_is_read_only_consistent_and_preserves_unknown_times(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / 'run'
            (root / 'results').mkdir(parents=True)
            config = dict(commit=snapshot.runner.COMMIT, repository='D:\\private\\repository',
                          dataset_revision='revision', expected_models=10000)
            (root / 'config.json').write_text(json.dumps(config), encoding='utf-8')
            (root / 'smoke.json').write_text('{}', encoding='utf-8')
            for mode in snapshot.runner.MODES:
                row = self.row(status='download_failed', error='IOError: ' + str(root / 'scratch'))
                (root / 'results' / (mode + '.jsonl')).write_text(
                    json.dumps(row) + '\n{"file":', encoding='utf-8')
            before = {path: path.read_bytes() for path in root.rglob('*') if path.is_file()}
            destination = Path(directory) / 'snapshot'
            summary = snapshot.capture(root, destination)
            self.assertEqual(summary['state'], 'interim')
            self.assertEqual(summary['completed_records'], {'wrap': 1, 'boolean': 1})
            for path, content in before.items():
                self.assertEqual(path.read_bytes(), content)
            with (destination / 'results' / 'per_operation.csv').open(newline='', encoding='utf-8') as stream:
                rows = list(csv.DictReader(stream))
            self.assertEqual(len(rows), 4)
            self.assertTrue(all(row['time_ms'] == '' for row in rows))
            self.assertTrue(all('<run-root>' in row['reason'] for row in rows))
            for relative, digest in summary['published_file_sha256'].items():
                self.assertEqual(hashlib.sha256((destination / relative).read_bytes()).hexdigest(), digest)
            with self.assertRaises(FileExistsError):
                snapshot.capture(root, destination)


if __name__ == '__main__':
    unittest.main()
