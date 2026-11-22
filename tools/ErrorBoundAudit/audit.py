"""Runs the models of bounds.py: the derivation (symbolic, exact rationals) and the concrete check of the derivation
(random and adversarial doubles against exact fractions). Prints the table of the research note.

    python3 tools/ErrorBoundAudit/audit.py [--samples N] [--markdown]
"""
import argparse
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bounds                                           # noqa: E402
from fpbound import Context, Sampler                    # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def squeeze(s):
    return re.sub(r'\s+', '', s)


SCOPE_CHARS = 3500      # how far after its start a function's anchors are searched (characters without whitespace)


def anchors_missing(e, cache={}):
    """the anchors that are not in the source: inside the entry's scope (the function) where it has one; an anchor
    that starts with '~' (a constant, a helper) anywhere in the file"""
    if e.file not in cache:
        with open(os.path.join(ROOT, e.file), encoding='utf-8') as f:
            cache[e.file] = squeeze(f.read())
    text = cache[e.file]
    region = text
    if e.scope is not None:
        at = text.find(squeeze(e.scope))
        if at < 0 or text.find(squeeze(e.scope), at + 1) >= 0:
            return ['scope (must occur exactly once): ' + e.scope]
        region = text[at:at + SCOPE_CHARS]
    return [x for x in e.anchors if squeeze(x.lstrip('~')) not in (text if x.startswith('~') else region)]


def first_line(e):
    """the line of the entry's first anchor (for the table)"""
    with open(os.path.join(ROOT, e.file), encoding='utf-8') as f:
        for n, line in enumerate(f, 1):
            if squeeze(e.anchors[0].lstrip('~')) in squeeze(line):
                return n
    return 0


def derive(e):
    c = Context(assume_normal=e.normal_only)
    r = bounds.Run(c)
    e.model(c, r)
    return r.verdicts


def concrete(e, samples, seed=1):
    """the model on doubles: every node's facts and the final claim; returns (violations, largest error / bound)"""
    rng = random.Random(seed)
    worst, bad = 0.0, []
    samples = min(samples, e.samples or samples)
    for k in range(samples * (2 if e.normal_only else 1)):
        if k < samples:
            c = Context(Sampler(rng, **e.sample), assume_normal=e.normal_only)
        else:       # the subnormal range of a normal-only model: the final claim alone
            c = Context(Sampler(rng, emin=e.tiny_emin, emax=e.tiny_emin + 80), assume_normal=True, check_nodes=False)
        r = bounds.Run(c)
        e.model(c, r)
        bad += c.violations
        worst = max([worst] + r.observed)
    return bad, worst


def status(v, e=None):
    if not v.ok:
        return 'FINDING ' + v.finding if getattr(v, 'finding', None) else 'NOT PROVED'
    if v.kind == 'reference':
        return 'matches reference'
    if v.kind == 'arithmetic':
        return 'checked (arithmetic)'
    return 'proved (normal range)' if e is not None and e.normal_only else 'proved'


def margin_text(v):
    if v.margin is None:
        return '-'
    m = float(v.margin)
    return '%.4g' % m if abs(m - 1) > 1e-6 else '1 %+.3g u' % (float(v.margin - 1) * 2**53)


def run(samples):
    """every entry: (entry, missing anchors, verdicts, concrete violations, largest observed error / bound)"""
    return [(e, anchors_missing(e), derive(e)) + concrete(e, samples) for e in bounds.ENTRIES]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--samples', type=int, default=50, help='concrete runs per model (the derivation needs none)')
    ap.add_argument('--markdown', action='store_true', help='the table of the research note')
    a = ap.parse_args()
    failed = 0
    if a.markdown:
        print('| id | file:line | claimed bound | assumption | claim | result | margin | observed |')
        print('|---|---|---|---|---|---|---|---|')
    for e, miss, verdicts, bad, worst in run(a.samples):
        failed += bool(miss) or bool(bad) or any(status(v) == 'NOT PROVED' for _, v in verdicts)
        for label, v in verdicts:
            if a.markdown:
                print('| %s | `%s:%d` | %s | %s | %s | %s | %s | %s |' % (e.id, e.file, first_line(e), e.claimed.replace('|', '\\|'), e.assumption.replace('|', '\\|'), label, status(v, e),
                                                                     margin_text(v), '%.3g' % worst if worst else '-'))
            else:
                print('%-40s %-34s %-21s margin %-12s observed %-7.3g (%s)' % (e.id, label[:34], status(v, e), margin_text(v), worst, v.detail))
        if miss:
            print('  SOURCE DRIFT: %r' % miss)
        if bad:
            print('  CONCRETE VIOLATIONS (%d): %r' % (len(bad), bad[:3]))
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
