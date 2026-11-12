"""Tables of docs/design/SNAP_ROUNDING_3D.md from the raw results in results/.

  summarize.py trace <trace.jsonl> <baseline.jsonl>   where the rounding time goes (run_trace.sh; the baseline is the
                                                      untraced `BooleanComparison profile --repeat 1 --safe-rounding`)
  summarize.py search <trace.jsonl>                   the first full search, serial split (cases of 100k triangles and more)
  summarize.py eval <trace.jsonl>                     the evaluations of proposed edits
  summarize.py ab <ab.jsonl>                          run_ab.sh: rounding and boolean time per configuration, hashes
  summarize.py cull <cull.jsonl>                      `SnapRoundingExperiment cull`

The trace's own bookkeeping (two timestamps per pair test and per query) inflates the parts it times, so the shares are
of the untraced rounding time and can add up to more than 100 %: read them as an order of size, not as a split.
"""
import collections
import json
import sys


def rows(path):
    return [json.loads(l) for l in open(path) if l.strip()]


def g(t, k):
    return t.get(k, 0.0)


def trace(path, base_path):
    base = {}
    for d in rows(base_path):
        p = d['phases_ms']
        base[(d['case'].split('/')[-1], d['op'])] = (p.get('rounding', 0), sum(v for k, v in p.items() if '.' not in k),
                                                    d.get('rounding_clean'), d.get('rounding_limit'))
    print('| case | op | triangles | exact | boolean ms | rounding ms | share | setup | tree | search | degenerate | near | pairs shared | pairs apart | of them at a distance | clean | limit | edits | evaluations |')
    print('|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|')
    for r in rows(path):
        t = r['trace']
        b = base.get((r['case'], r['op']))
        if not b:
            continue
        rd = max(b[0], 1)
        pc = lambda x: '%.0f %%' % (100 * x / rd)
        setup = sum(v for k, v in t.items() if k.startswith('P.setup'))
        inc = g(t, 'T.eval.pairs.new.incident') + g(t, 'T.eval.pairs.old.incident')
        non = g(t, 'T.eval.pairs.new.nonincident') + g(t, 'T.eval.pairs.old.nonincident')
        n = g(t, 'in.triangles')
        print('| %s | %s | %d | %.0f %% | %d | %d | %.0f %% | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %d | %d |' % (
            r['case'], r['op'], n, 100 * g(t, 'diag.skipped_exact') / max(n, 1), b[1], b[0], 100 * b[0] / max(b[1], 1),
            pc(setup), pc(g(t, 'T.tree')), pc(g(t, 'T.search')), pc(g(t, 'T.degenerate')), pc(g(t, 'T.eval.near')), pc(inc), pc(non),
            pc(g(t, 'T.eval.pairs.new.nonincident.dist_pos')) if r.get('dist') else '-', 'yes' if b[2] else 'no',
            'yes' if b[3] else 'no', g(t, 'out.edits'), g(t, 'eval.calls')))


def search(path):
    print('| case | op | queries | box hits per query | pairs with a shared corner | pairs without | traversal ms | shared: filter + exact ms | apart: filter + exact ms | parallel search ms | first tree ms | setup ms |')
    print('|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for r in rows(path):
        t = r['trace']
        if g(t, 'in.triangles') < 100000:
            continue
        qn = max(g(t, 'diag.queries'), 1)
        print('| %s | %s | %d | %.0f | %d | %d | %.0f | %.0f + %.0f | %.0f + %.0f | %.0f | %.0f | %.0f |' % (
            r['case'], r['op'], qn, g(t, 'diag.box_hits') / qn, g(t, 'diag.pairs_incident'), g(t, 'diag.pairs_nonincident'),
            g(t, 'Tdiag.traversal'), g(t, 'Tdiag.inc_filter'), g(t, 'Tdiag.inc_exact'), g(t, 'Tdiag.non_filter'), g(t, 'Tdiag.non_exact'),
            g(t, 'P.search.first'), g(t, 'P.tree.first'), sum(v for k, v in t.items() if k.startswith('P.setup'))))


def evaluations(path):
    print('| case | op | evaluations | of them past the limit | accepted | regions | near queries | hits per query | us per query | pair tests shared | pair tests apart | ns per test | passes ms | in improves ms |')
    print('|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for r in rows(path):
        t = r['trace']
        if g(t, 'eval.calls') < 2000:
            continue
        nq = max(g(t, 'eval.near_queries'), 1)
        pi = g(t, 'eval.pairs.new.incident') + g(t, 'eval.pairs.old.incident')
        pn = g(t, 'eval.pairs.new.nonincident') + g(t, 'eval.pairs.old.nonincident')
        tp = sum(g(t, 'T.eval.pairs.' + k) for k in ('new.incident', 'old.incident', 'new.nonincident', 'old.nonincident'))
        print('| %s | %s | %d | %d | %d | %d | %d | %.0f | %.1f | %d | %d | %.0f | %.0f | %.0f |' % (
            r['case'], r['op'], g(t, 'eval.calls'), g(t, 'eval.stage1'), g(t, 'eval.accepted'), g(t, 'eval.regions'), nq,
            g(t, 'eval.near_hits') / nq, 1000 * g(t, 'T.eval.near') / nq, pi, pn, 1e6 * tp / max(pi + pn, 1),
            g(t, 'P.passes') + g(t, 'P.contacts_and_passes2'), g(t, 'T.eval')))


def ab(path):
    best = collections.defaultdict(dict)     # (case, op) -> cfg -> (rounding, total, hash, clean)
    for d in rows(path):
        k, c = (d['case'], d['op']), d['cfg']
        if c not in best[k] or d['rounding_ms'] < best[k][c][0]:
            best[k][c] = (d['rounding_ms'], d['total_ms'], d['hash'], d['clean'])
    cfgs = ['base', 'sepaxis', 'sepaxis_stop']
    print('| case | op | clean | rounding ms: base | sepaxis | sepaxis_stop | boolean ms: base | sepaxis | sepaxis_stop | hash equal: sepaxis | sepaxis_stop |')
    print('|---|---|---|---:|---:|---:|---:|---:|---:|---|---|')
    tot = collections.Counter()
    for k in best:
        b = best[k]
        if any(c not in b for c in cfgs):
            continue
        for c in cfgs:
            tot[c + 'r'] += b[c][0]; tot[c + 't'] += b[c][1]
        print('| %s | %s | %s | %d | %d | %d | %d | %d | %d | %s | %s |' % (
            k[0], k[1], 'yes' if b['base'][3] else 'no', b['base'][0], b['sepaxis'][0], b['sepaxis_stop'][0], b['base'][1], b['sepaxis'][1],
            b['sepaxis_stop'][1], 'yes' if b['sepaxis'][2] == b['base'][2] else 'NO', 'yes' if b['sepaxis_stop'][2] == b['base'][2] else 'NO'))
    print('| **sum** | | | %d | %d | %d | %d | %d | %d | | |' % tuple(tot[c + x] for x in 'rt' for c in cfgs))


def cull(path):
    names = ['box', 'box+side', 'box+axes', 'own-box', 'axes']
    print('| case | op | triangles | queries | ' + ' | '.join('%s: ms, hits per query, pair tests' % n for n in names) + ' | same pairs |')
    print('|---|---|---:|---:|' + '---:|' * len(names) + '---|')
    for d in rows(path):
        print('| %s | %s | %d | %d | %s | %s |' % (d['case'], d['op'], d['triangles'], d['queries'], ' | '.join(
            '%.0f, %.0f, %d' % (d[n]['ms'], d[n]['hits_per_query'], d[n]['pair_tests']) for n in names),
            'yes' if all(d[n]['same_pairs'] for n in names) else 'NO'))


if __name__ == '__main__':
    {'trace': trace, 'search': search, 'eval': evaluations, 'ab': ab, 'cull': cull}[sys.argv[1]](*sys.argv[2:])
