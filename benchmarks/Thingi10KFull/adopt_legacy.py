"""Adopt fully computed legacy jobs after their runner and child processes are stopped."""
import argparse
import json
import math
from pathlib import Path
import run_full as legacy
import run_pipeline as pipeline


def inspect_job(directory, logs, tool):
    info = json.loads((directory / 'info.json').read_text(encoding='utf-8'))
    name = info['file']
    if not pipeline.safe_name(name):
        raise ValueError('Unsafe legacy filename')
    export = json.loads((logs / 'export.command.json').read_text(encoding='utf-8'))
    expected_export = ['dotnet', str(tool), 'export']
    if export['command'][:3] != expected_export or Path(export['command'][-1]) != directory:
        raise ValueError('Legacy export provenance mismatch')
    exported = json.loads((logs / 'export.stdout.log').read_text(encoding='utf-8').strip().splitlines()[-1])
    if exported != info or exported.get('error'):
        raise ValueError('Legacy export is not demonstrably complete')
    raw = {'file': name, 'class': 'thingi10k', 'method': 'ours', 'input': info,
           'commit': legacy.COMMIT, 'thingi_id': Path(name).stem, 'benchmark_mode': 'boolean',
           'compute_wall_ms': None, 'evaluation_state': 'pending', 'export_wall_ms': export['wall_ms'],
           'wall_time_source': 'legacy_command_log; full_compute_wall_time_unavailable',
           'legacy_completion_evidence': 'all operation JSON reports ok with finite ms and existing outputs; exit codes not logged'}
    runs = {}
    for op in legacy.OPS:
        command = json.loads((logs / (op + '.command.json')).read_text(encoding='utf-8'))
        expected = ['dotnet', str(tool), 'run', str(directory / 'A.off'), str(directory / 'B.off'), op,
                    str(directory / ('ours_' + op + '.off'))]
        if command['command'] != expected:
            raise ValueError('Legacy operation provenance mismatch: ' + op)
        result = legacy.cmp.parse_json_line((logs / (op + '.stdout.log')).read_text(encoding='utf-8'))
        if (not result or result.get('ok') is not True or result.get('error') or
                not isinstance(result.get('ms'), (int, float)) or not math.isfinite(result['ms']) or result['ms'] < 0 or
                not (directory / ('ours_' + op + '.off')).is_file()):
            raise ValueError('Legacy operation is not demonstrably complete: ' + op)
        result.update(status='ok', wall_ms=command['wall_ms'], wall_time_source='legacy_command_log')
        runs[op] = result
        raw.update({op + '_' + k: v for k, v in result.items()})
    return raw, runs


def adopt(root, directory):
    config = json.loads((root / 'config.json').read_text(encoding='utf-8'))
    if config['commit'] != legacy.COMMIT:
        raise ValueError('Legacy measured commit mismatch')
    for relative, expected in config['binary_sha256'].items():
        if pipeline.digest(root / relative) != expected:
            raise ValueError('Frozen binary mismatch')
    info = json.loads((directory / 'info.json').read_text(encoding='utf-8'))
    if not pipeline.safe_name(info['file']):
        raise ValueError('Unsafe legacy filename')
    raw, runs = inspect_job(directory, root / 'results' / 'diagnostics' / info['file'],
                            root / 'bin' / 'BooleanComparison' / 'BooleanComparison.dll')
    final = pipeline.read_results(root / 'results' / 'boolean.jsonl')
    if raw['file'] in final and final[raw['file']].get('status') != 'download_failed':
        raise ValueError('Legacy job already finalized')
    store = pipeline.JobStore(root / 'pending-evaluation', config['binary_sha256'])
    path = store.publish(directory, raw, runs)
    pipeline.append(root / 'results' / 'computed.jsonl', raw)
    return path


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-root', type=Path, required=True)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    print(adopt(args.run_root, args.directory))
