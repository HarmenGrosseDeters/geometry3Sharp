"""Source contracts and SMT-LIB queries for small arithmetic pilots."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import unittest

import z3

ROOT = Path(__file__).resolve().parents[2]


def source_contract(path, anchors):
    """Hash the normalized source between each pair of unique anchors."""
    code = normalize((ROOT / path).read_text(encoding='utf-8'))
    for start, end, expected in anchors:
        start, end = normalize(start), normalize(end)
        left = code.index(start)
        right = code.index(end, left + len(start))
        actual = hashlib.sha256(code[left:right].encode()).hexdigest()
        if actual != expected:
            raise ValueError(f'{path}: review the solver model after changes near {start}')


def normalize(text):
    return re.sub(r'\s+|//[^\n]*', '', text)


def verify(logic, model, obligations, timeout_ms):
    results = {}
    for name, goal, expected in obligations:
        solver = z3.SolverFor(logic)
        solver.set(timeout=timeout_ms)
        solver.from_string(f'{model}\n(assert {goal})')
        results[name] = dict(result=str(solver.check()), expected=expected)
    return dict(z3=z3.get_version_string(),
                passed=all(row['result'] == row['expected'] for row in results.values()),
                obligations=results)


def write_report(report, path=None):
    rendered = json.dumps(report, indent=2) + '\n'
    if path:
        Path(path).write_text(rendered, encoding='utf-8')
    return rendered


def main(run):
    parser = argparse.ArgumentParser()
    parser.add_argument('--out')
    parser.add_argument('--timeout-ms', type=int, default=120000)
    args = parser.parse_args()
    report = run(args.timeout_ms)
    print(write_report(report, args.out), end='')
    return 0 if report['passed'] else 1


class PilotTest(unittest.TestCase):
    def check_pilot(self, run, report_name):
        report = run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        rendered = write_report(report, os.environ.get(report_name))
        self.assertTrue(report['passed'], rendered)
