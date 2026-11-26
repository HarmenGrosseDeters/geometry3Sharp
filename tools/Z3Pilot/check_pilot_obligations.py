"""Common source checks and reporting for the local proof obligations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

import z3

ROOT = Path(__file__).resolve().parents[2]


def normalize(text):
    return re.sub(r'\s+|//[^\n]*', '', text)


def source(path, start, end, digest, root=ROOT):
    """Check the SHA-256 of a span, ignoring whitespace and line comments."""
    text = (root / path).read_text(encoding='utf-8-sig')
    # Normalize after selecting the span so a comment can be an end marker.
    left = text.index(start)
    right = text.index(end, left + len(start))
    section = text[left:right]
    if hashlib.sha256(normalize(section).encode()).hexdigest() != digest:
        raise ValueError(f'{path}: review the solver transcription at {start}')
    return section


def source_contract(path, anchors, root=ROOT):
    for anchor in anchors:
        source(path, *anchor, root=root)


def has_free_constant(goal):
    # SMT-LIB define-fun expansions share ASTs. Visit each node once.
    pending = [goal]
    seen = set()
    while pending:
        term = pending.pop()
        if term.get_id() in seen:
            continue
        seen.add(term.get_id())
        if z3.is_const(term) and term.decl().kind() == z3.Z3_OP_UNINTERPRETED:
            return True
        pending.extend(term.children())
    return False


def check_expr(name, premises, goal, expected, timeout_ms, logic=None):
    if not has_free_constant(goal):
        raise ValueError(f'{name}: ground expressions belong in a replay')
    solver = z3.SolverFor(logic) if logic else z3.Solver()
    solver.set(timeout=timeout_ms, random_seed=0)
    solver.add(*premises, goal)
    result = solver.check()
    row = dict(name=name, result=str(result), expected=expected)
    if result == z3.unknown:
        row['reason'] = solver.reason_unknown()
    return row


def check(name, logic, assertions, goal, expected, timeout_ms):
    parsed = list(z3.parse_smt2_string(f'{assertions}\n(assert {goal})'))
    return check_expr(name, parsed[:-1], parsed[-1], expected, timeout_ms, logic)


def collect(obligations, timeout_ms):
    return report([check_expr(*item, timeout_ms) for item in obligations])


def verify(logic, model, obligations, timeout_ms):
    return report([check(name, logic, model, goal, expected, timeout_ms)
                   for name, goal, expected in obligations])


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
