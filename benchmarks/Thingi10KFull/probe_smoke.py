"""Differential evaluation smoke on procedural soups, never on committed mesh files."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import run_full as legacy


VERTICES = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)]
FACES = [(0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3)]


def cases():
    yield 'clean', VERTICES, FACES
    yield 'empty', [], []
    yield 'open', VERTICES, FACES[:-1]
    yield 'inverted', VERTICES, [tuple(reversed(t)) for t in FACES]
    yield 'degenerate', [(x, y, z * 1e-14) for x, y, z in VERTICES], FACES
    # Two closed tetrahedra share only the origin.
    yield 'bowtie', VERTICES + [(-x, -y, -z) for x, y, z in VERTICES[1:]], FACES + [
        tuple(0 if i == 0 else i + 3 for i in reversed(t)) for t in FACES]
    yield 'self_intersection', VERTICES + [(x + .2, y + .2, z + .2) for x, y, z in VERTICES], FACES + [
        tuple(i + 4 for i in t) for t in FACES]
    points = [(x + 3 * i, y, z) for i in range(1024) for x, y, z in VERTICES]
    closed = [tuple(v + 4 * i for v in face) for i in range(1024) for face in FACES]
    yield 'correlated_clean', points, closed
    yield 'correlated_open', points, [tuple(v + 4 * i for v in face) for i in range(1024) for face in FACES[:-1]]


def main(original, probe, report):
    results = []
    with tempfile.TemporaryDirectory(prefix='thingi_probe_') as temp:
        root = Path(temp)
        for name, vertices, faces in cases():
            for exponent in (-8, 0, 8):
                for translation in ((0, 0, 0), (4, -3, 2)):
                    scale = 2 ** exponent
                    label = f'{name}_{exponent}_{translation[0]}'
                    job = root / label
                    job.mkdir()
                    points = [tuple(scale * p[k] + translation[k] for k in range(3)) for p in vertices]
                    soup = 'OFF\n' + f'{len(points)} {len(faces)} 0\n'
                    soup += ''.join(' '.join(map(str, p)) + '\n' for p in points)
                    soup += ''.join('3 ' + ' '.join(map(str, t)) + '\n' for t in faces)
                    for op in legacy.OPS:
                        (job / ('ours_' + op + '.off')).write_text(soup, encoding='utf-8')
                    (job / 'info.json').write_text(json.dumps({'vA': 1, 'vB': 1, 'file': label, 'tris': len(faces),
                        'center': translation}), encoding='utf-8')
                    frozen = subprocess.run(['dotnet', str(original), 'eval', str(job)], capture_output=True,
                                            text=True, timeout=60, check=True)
                    phases = job / 'phases.jsonl'
                    env = dict(os.environ, THINGI_EVAL_PHASE_LOG=str(phases))
                    instrumented = subprocess.run(['dotnet', str(probe), 'eval', str(job)], capture_output=True,
                                                  text=True, timeout=60, check=True, env=env)
                    before = [json.loads(line) for line in frozen.stdout.splitlines()]
                    after = [json.loads(line) for line in instrumented.stdout.splitlines()]
                    assert before == after, label
                    events = [json.loads(line) for line in phases.read_text().splitlines()]
                    assert events and len(events) % 2 == 0, label
                    for i in range(0, len(events), 2):
                        start, end = events[i:i + 2]
                        assert start['state'] == 'started' and end['state'] == 'completed', label
                        assert start['phase'] == end['phase'] and start['operation'] == end['operation'], label
                        assert end['wall_ms'] >= 0, label
                    if name in ('clean', 'correlated_clean'):
                        for op in legacy.OPS:
                            assert after[0][op + '_not_clean_reason'] == '', label
                            assert after[0][op + '_finite'] and after[0][op + '_manifold_closed'], label
                    results.append({'case': label, 'equal': True, 'metrics': after, 'phase_events': len(events)})
    evidence = {'command': subprocess.list2cmdline([sys.executable, *sys.argv]),
                'commit': legacy.COMMIT, 'cases': results, 'passed': True}
    report.write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    print(f'Differential probe smoke: {len(results)} procedural cases passed; phase pairs complete.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--original', type=Path, required=True)
    parser.add_argument('--probe', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    main(args.original, args.probe, args.report)
