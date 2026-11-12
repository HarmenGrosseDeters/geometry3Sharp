"""Z3 pilot 4: the termination of the midpoint split of DMeshAABBTree3's top-down build (spatial/DMeshAABBTree.cs,
split_tri_set_midpoint), the rule that #451 broke: with centers that are adjacent doubles the midpoint rounds onto
one end, one side of the partition is empty, and the same set was split again on the next axis without end (a
stack overflow); the scan also ran past the set. QA 2.0 T14.

What is checked (binary64, round to nearest even, subnormals included; every obligation is a counterexample query
that must return UNSAT, with satisfiable assumptions; every mutant must return SAT with a witness replayed in plain
Python floats; UNKNOWN never counts):

  O1  the midpoint of the extremes a < b of the centers, m = (b + a) * 0.5 (Interval1d.Center), satisfies a <= m <= b,
      so the left side (centers <= m) holds at least the minimal center: n0 >= 1;
  O2  the right side (centers > m) is empty exactly when m == b (the rounded midpoint reaches the upper end; it holds
      b otherwise, trivially): the case the guard `n0 < 1 || n1 < 1` exists for; both m == a and m == b are reachable
      (adjacent doubles), SAT with a witness;
  O3  the sizes: a split of a set of iCount >= 2 elements has n0 >= 1 and n1 >= 1 after the guard (the partition
      sides, or the half and half fallback iCount / 2), so both are smaller than iCount: the recursion is on strictly
      smaller sets and ends (a leaf is iCount < max(TopDownLeafMaxTriCount, 2), so a set that is split has >= 2);
  (the scan `while (l < iCount && centers[l] <= m)` is bounded by its first operand; with m == b (O2) every center is
  <= m, so without that operand it would leave the set: this is source_contracts' job, the loop is not modelled)

MUTANTS: without the guard on n1 (the code before #451) an empty side is possible (m == b); the midpoint with an
unbounded sum overflows (a + b = inf) when the centers are not bounded by MaxValue / 2 (the code's argument: a center
is a third of a finite sum).

The model is held to the source by `source_contracts` (the lines it reads must be present).
"""
import json
import pathlib
import struct
import time

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = 'spatial/DMeshAABBTree.cs'
INTERVAL = 'math/Interval1d.cs'
F64 = z.Float64()
RNE = z.RNE()
MAX_DOUBLE = struct.unpack('<d', struct.pack('<Q', 0x7FEFFFFFFFFFFFFF))[0]


def _norm(s):
    return ''.join(s.split())


def source_contracts(text=None, interval_text=None):
    """The code the lemmas model, read from the sources (fails on drift instead of checking a stale transcription)."""
    if text is None:
        text = (ROOT / SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    if interval_text is None:
        interval_text = (ROOT / INTERVAL).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    start = text.index('int split_tri_set_midpoint(')
    body = _norm(text[start:text.index('int split_tri_set_sorted', start) if 'int split_tri_set_sorted' in text[start:] else start + 6000])
    required = [
        'doublemidpoint=interval.Center;',
        'if(interval.a<interval.b){',
        'while(l<iCount&&centers[iStart+l][axis]<=midpoint)l++;',
        'while(r>=0&&centers[iStart+r][axis]>midpoint)r--;',
        'n0=l;n1=iCount-n0;',
        'if(n0<1||n1<1){n0=iCount/2;n1=iCount-n0;}',
        '}else{',
        'n0=iCount/2;n1=iCount-n0;',
    ]
    pos = 0
    for r in required:
        i = body.find(r, pos)
        if i < 0:
            raise ValueError('split_tri_set_midpoint no longer contains (in this order): ' + r)
        pos = i
    if '(b+a)*0.5' not in _norm(interval_text):
        raise ValueError('Interval1d.Center is no longer (b + a) * 0.5')
    if 'Math.Max(TopDownLeafMaxTriCount,2)' not in _norm(text):
        raise ValueError('the leaf limit is no longer Math.Max(TopDownLeafMaxTriCount, 2)')
    return dict(center='(b + a) * 0.5', guard='n0 < 1 || n1 < 1', fallback='iCount / 2', leaf_limit_min=2)


def _check(hyps, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*hyps)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _center(a, b):
    return z.fpMul(RNE, z.fpAdd(RNE, b, a), z.FPVal(0.5, F64))


def _finite(x, bound=None):
    c = [z.Not(z.fpIsNaN(x)), z.Not(z.fpIsInf(x))]
    if bound is not None:
        c.append(z.fpLEQ(z.fpAbs(x), z.FPVal(bound, F64)))
    return c


def _fp_value(model, var):
    """the value of a binary64 variable as a Python float, exact (through its IEEE bits)"""
    bits = model.eval(z.fpToIEEEBV(var), model_completion=True).as_long()
    return struct.unpack('<d', struct.pack('<Q', bits))[0]


def _row(name, hyps, bad, timeout_ms, expect, extra=None):
    s, r, secs = _check(hyps, bad, timeout_ms)
    out = dict(name=name, result=str(r), expected=expect, seconds=round(secs, 3), ok=str(r) == expect)
    if r == z.unknown:
        out['reason'] = s.reason_unknown()
    sv = z.Solver()
    sv.set(timeout=timeout_ms)
    sv.add(*hyps)
    out['assumptions_result'] = str(sv.check())
    if extra and r == z.sat:
        out['witness'] = extra(s.model())
    return out


def run(timeout_ms=120000):
    contracts = source_contracts()
    a, b = z.FP('a', F64), z.FP('b', F64)
    half = MAX_DOUBLE / 2
    hyps = _finite(a, half) + _finite(b, half) + [z.fpLT(a, b)]
    m = _center(a, b)
    rows = []

    # (three sign cases, two claims each: one query over all signs takes longer than two minutes in the bit-blaster)
    zero = z.FPVal(0.0, F64)
    cases = (('both_nonnegative', [z.fpGEQ(a, zero)]), ('a_negative_b_nonnegative', [z.fpLT(a, zero), z.fpGEQ(b, zero)]),
             ('both_negative', [z.fpLT(b, zero)]))
    for case, extra_hyps in cases:
        for claim, bad in (('a_le_midpoint', z.Not(z.fpLEQ(a, m))), ('midpoint_le_b', z.Not(z.fpLEQ(m, b)))):
            rows.append(_row(f'O1_{claim}_{case}', hyps + extra_hyps, bad, timeout_ms, 'unsat'))

    def witness(model):
        va, vb = _fp_value(model, a), _fp_value(model, b)
        mid = (vb + va) * 0.5
        return dict(a=repr(va), b=repr(vb), midpoint=repr(mid), midpoint_is_b=bool(mid == vb), midpoint_is_a=bool(mid == va))

    # O2: the right side (centers > m) holds b unless m == b; m == b and m == a are both reachable (witnesses)
    rows.append(_row('O2_midpoint_can_round_onto_b_(the_case_of_the_guard)', hyps + [z.fpEQ(m, b)], z.BoolVal(True), timeout_ms, 'sat', witness))
    rows.append(_row('O2_midpoint_can_round_onto_a', hyps + [z.fpEQ(m, a)], z.BoolVal(True), timeout_ms, 'sat', witness))

    # O3 on integers: after the guard both sides are non-empty and smaller than the set
    n, n0, n1 = z.Ints('n n0 n1')
    guarded = z.If(z.Or(n0 < 1, n1 < 1), z.And(n0 == n / 2, n1 == n - n0), z.BoolVal(True))
    rows.append(_row('O3_after_the_guard_both_sides_are_smaller_than_the_set',
                     [n >= 2, n0 >= 0, n1 >= 0, n0 + n1 == n, guarded],
                     z.Not(z.And(n0 >= 1, n1 >= 1, n0 < n, n1 < n)), timeout_ms, 'unsat'))
    # the mutant without the fallback: an empty side is possible when n0 = 0 or n1 = 0 (no guard), n0 + n1 = n
    rows.append(_row('O3_mutant_without_the_guard_(an_empty_side_keeps_the_set_whole)', [n >= 2, n0 >= 0, n1 >= 0, n0 + n1 == n],
                     z.Not(z.And(n0 >= 1, n1 >= 1)), timeout_ms, 'sat'))

    # mutant: the sum overflows when the centers are only finite (no MaxValue / 2 bound)
    big = _finite(a) + _finite(b) + [z.fpLT(a, b)]
    rows.append(_row('mutant_midpoint_of_unbounded_centers_overflows', big, z.Not(z.And(z.fpLEQ(a, _center(a, b)), z.fpLEQ(_center(a, b), b))),
                     timeout_ms, 'sat', witness))
    passed = all(r['ok'] and r['assumptions_result'] == 'sat' for r in rows)
    return dict(contracts=contracts, obligations=rows, passed=passed)


if __name__ == '__main__':
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--out')
    ap.add_argument('--timeout-ms', type=int, default=120000)
    args = ap.parse_args()
    report = run(args.timeout_ms)
    text = json.dumps(report, indent=2)
    if args.out:
        pathlib.Path(args.out).write_text(text + '\n')
    print(text)
    raise SystemExit(0 if report['passed'] else 1)
