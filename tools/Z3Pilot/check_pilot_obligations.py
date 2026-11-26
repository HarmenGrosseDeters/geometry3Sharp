"""Common source checks and reporting for the local proof obligations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

import z3

ROOT = Path(__file__).resolve().parents[2]


def source(path, start, end, digest, root=ROOT, prose=False):
    text = (root / path).read_text(encoding='utf-8')
    section = text[text.index(start):]
    section = section[:section.index(end, len(start))]
    if not prose:
        section = re.sub(r'//[^\n]*', '', section)
    actual = hashlib.sha256(re.sub(r'\s+', '', section).encode()).hexdigest()
    if actual != digest:
        raise ValueError(f'{path}: review the solver transcription at {start}')
    return section


def check(name, logic, assertions, counterexample, expected, timeout_ms):
    solver = z3.SolverFor(logic)
    solver.set(timeout=timeout_ms)
    solver.from_string(assertions + f'\n(assert {counterexample})')
    result = solver.check()
    row = dict(name=name, result=str(result), expected=expected)
    if str(result) == 'unknown':
        row['reason'] = solver.reason_unknown()
    return row


def report(rows, **details):
    return dict(z3=z3.get_version_string(),
                passed=all(row['result'] == row['expected'] for row in rows),
                obligations=rows, **details)


def run_test(test, run, env_name):
    result = run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
    data = json.dumps(result, indent=2) + '\n'
    if os.environ.get(env_name):
        Path(os.environ[env_name]).write_text(data, encoding='utf-8')
    test.assertTrue(result['passed'], data)


def main(run):
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path)
    parser.add_argument('--timeout-ms', type=int, default=120000)
    args = parser.parse_args()
    result = run(args.timeout_ms)
    data = json.dumps(result, indent=2) + '\n'
    if args.out:
        args.out.write_text(data, encoding='utf-8')
    else:
        print(data, end='')
    return 0 if result['passed'] else 1
