"""Capture complete JSONL records without modifying or stopping the live run."""
import argparse
from collections import Counter
import csv
from datetime import datetime
import hashlib
import json
from pathlib import Path

import run_full as runner


def complete_rows(data):
    lines = data.split(b'\n')[:-1]
    rows = [json.loads(line) for line in lines if line.strip()]
    names = [row['file'] for row in rows]
    if len(names) != len(set(names)):
        raise ValueError('Duplicate model records')
    if any(row.get('commit') != runner.COMMIT for row in rows):
        raise ValueError('Mixed measurement commits')
    return rows


def redact(value, replacements):
    if isinstance(value, str):
        for source, target in replacements:
            value = value.replace(source, target).replace(source.replace('\\', '/'), target)
        return value
    if isinstance(value, list):
        return [redact(item, replacements) for item in value]
    if isinstance(value, dict):
        return {key: redact(item, replacements) for key, item in value.items()}
    return value


def capture(run_root, destination):
    if destination.exists():
        raise FileExistsError('Snapshots are immutable; choose a new destination')
    config = json.loads((run_root / 'config.json').read_text(encoding='utf-8'))
    if config['commit'] != runner.COMMIT:
        raise ValueError('Unexpected measurement commit')
    replacements = [(str(run_root), '<run-root>'),
                    (str(run_root.parent / 'dataset'), '<dataset-root>'),
                    (config['repository'], '<repository-root>'),
                    (r'C:\Python314\python.exe', '<python>'),
                    (str(run_root.parent), '<external-root>')]
    captured = {}
    input_hashes = {}
    for mode in runner.MODES:
        path = run_root / 'results' / (mode + '.jsonl')
        with path.open('rb') as stream:
            data = stream.read(path.stat().st_size)
        input_hashes[mode] = hashlib.sha256(data).hexdigest()
        captured[mode] = redact(complete_rows(data), replacements)
    results = destination / 'results'
    results.mkdir(parents=True)
    for mode, rows in captured.items():
        (results / (mode + '.jsonl')).write_text(
            ''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')
    for name, converter in (('per_model.csv', runner.model_row),
                            ('per_operation.csv', runner.operation_rows)):
        with (results / name).open('w', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=runner.FIELDS)
            writer.writeheader()
            for mode, rows in captured.items():
                for row in rows:
                    converted = converter(mode, row)
                    writer.writerows(converted if isinstance(converted, list) else [converted])
    (destination / 'config.json').write_text(
        json.dumps(redact(config, replacements), indent=2) + '\n', encoding='utf-8')
    (destination / 'smoke.json').write_text(
        json.dumps(redact(json.loads((run_root / 'smoke.json').read_text(encoding='utf-8')),
                          replacements), indent=2) + '\n', encoding='utf-8')
    summary = {
        'state': 'interim', 'captured_at': datetime.now().astimezone().isoformat(),
        'commit': runner.COMMIT, 'dataset_revision': config['dataset_revision'],
        'expected_models_per_mode': config['expected_models'],
        'completed_records': {mode: len(rows) for mode, rows in captured.items()},
        'model_status_counts': {mode: dict(Counter(runner.model_row(mode, row)['status']
                                                   for row in rows))
                                for mode, rows in captured.items()},
        'capture_policy': 'Independent bounded reads of live JSONL prefixes; only newline-terminated records; CSV rebuilt from these records; local paths redacted; no meshes or binaries',
        'input_prefix_sha256_before_redaction': input_hashes,
        'published_file_sha256': {
            str(path.relative_to(destination)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(destination.rglob('*')) if path.is_file()}
    }
    (destination / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    return summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run_root', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(json.dumps(capture(args.run_root, args.destination), indent=2))
