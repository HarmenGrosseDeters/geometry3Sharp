import argparse
from collections import Counter
import csv
import hashlib
import json
import math
from pathlib import Path
import statistics

import run_full as runner
import snapshot


GROUPS = ('closed_no_self_intersections', 'closed_self_intersecting',
          'closed_self_intersections_unknown', 'open_or_weakly_closed', 'input_unknown')


def input_group(raw):
    info = raw.get('input') or {}
    if info.get('closed') is False:
        return 'open_or_weakly_closed'
    if info.get('closed') is not True:
        return 'input_unknown'
    intersections = info.get('selfint')
    if type(intersections) is int and intersections == 0:
        return 'closed_no_self_intersections'
    if type(intersections) is int and intersections > 0:
        return 'closed_self_intersecting'
    return 'closed_self_intersections_unknown'


def timing(values):
    values = sorted(value / 1000 for value in values
                    if type(value) in (int, float) and math.isfinite(value) and value >= 0)
    if not values:
        return {'timed_count': 0, 'median_seconds': None, 'p95_seconds': None, 'max_seconds': None}
    return {'timed_count': len(values), 'median_seconds': statistics.median(values),
            'p95_seconds': values[math.ceil(0.95 * len(values)) - 1], 'max_seconds': values[-1]}


def analyze(source, destination):
    source, destination = Path(source), Path(destination)
    streams = {}
    hashes = {}
    for mode in runner.MODES:
        data = (source / 'results' / (mode + '.jsonl')).read_bytes()
        streams[mode] = snapshot.complete_rows(data)
        hashes[mode] = hashlib.sha256(data).hexdigest()
    counts = {group: Counter() for group in GROUPS}
    models = Counter()
    reasons = {group: Counter() for group in GROUPS}
    candidates, empties, slowest, evaluation_failures = [], [], [], []
    fields = ('thingi_id', 'file', 'operation', 'input_group', 'input_selfint', 'status',
              'reason', 'time_ms', 'result_triangles', 'input_volume_a', 'input_volume_b',
              'result_volume', 'ie_error', 'diff_error', 'commit')
    boolean_times = []
    for raw in streams['boolean']:
        group = input_group(raw)
        models[group] += 1
        for operation in runner.operation_rows('boolean', raw):
            op = operation['operation']
            counts[group][operation['status']] += 1
            if operation['status'] != 'ok':
                reasons[group][operation['reason']] += 1
            info = raw.get('input') or {}
            row = {key: operation.get(key) for key in fields}
            row.update(input_group=group, input_selfint=info.get('selfint'),
                       result_triangles=raw.get(op + '_tris'), input_volume_a=info.get('vA'),
                       input_volume_b=info.get('vB'), result_volume=raw.get(op + '_volume'),
                       ie_error=raw.get('ie_error'), diff_error=raw.get('diff_error'))
            if group in ('closed_no_self_intersections', 'closed_self_intersections_unknown'):
                if operation['status'] == 'unclean':
                    candidates.append(row)
                if raw.get(op + '_tris') == 0:
                    empties.append(row)
            if operation['status'] == 'evaluation_failed':
                evaluation_failures.append(row)
            value = operation['time_ms']
            if type(value) in (int, float) and math.isfinite(value) and value >= 0:
                boolean_times.append(value)
                slowest.append(row)
    slowest.sort(key=lambda row: (-row['time_ms'], row['file'], row['operation']))
    wrap_status = Counter(runner.normalize_wrap(raw)[0] for raw in streams['wrap'])
    summary = {'measured_commit': runner.COMMIT, 'source_jsonl_sha256': hashes,
               'classification': 'closed must be true and selfint exactly zero; negative/missing counts are unknown',
               'percentile_method': 'nearest rank: ceil(0.95 * timed_count)',
               'boolean': {'models': len(streams['boolean']), 'operations': sum(sum(counter.values()) for counter in counts.values()),
                           'groups': {group: {'models': models[group], 'operations': sum(counts[group].values()),
                                              'statuses': dict(counts[group]), 'non_ok_reasons': dict(reasons[group])}
                                      for group in GROUPS},
                           'timing': timing(boolean_times), 'candidate_operations': len(candidates),
                           'empty_operations': len(empties)},
               'wrap': {'models': len(streams['wrap']), 'statuses': dict(wrap_status),
                        'timing': timing([raw.get('wrap_ms') for raw in streams['wrap']])}}
    destination.mkdir(parents=True, exist_ok=False)
    for name, rows in (('candidates', candidates), ('empty_results', empties),
                       ('slowest_operations', slowest[:50]), ('evaluation_failures', evaluation_failures)):
        with (destination / (name + '.csv')).open('w', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=fields)
            writer.writeheader()
            writer.writerows(rows)
    (destination / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    return summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Stratify an immutable Thingi10K snapshot without changing measurements.')
    parser.add_argument('snapshot', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(json.dumps(analyze(args.snapshot, args.destination), indent=2))
