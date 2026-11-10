#!/usr/bin/env python3
"""The Boolean 2.0 sign-off tally (owner 2026-11-10: contacts counted apart from unclean): per phase and class, the
operations clean / contacts / empty / unclean (by reason) / failed and the unavailable models.
  python3 runs/final/signoff.py runs/final/results > runs/final/results/SIGNOFF.md
Inputs in the results dir: target.jsonl with target_rerun_pr474.jsonl and target_rerun_pr483.jsonl in place of its
rows, control.jsonl, rest.jsonl (if present) with rest_rerun_429.jsonl in place of its rows (if present), and the
contact checks contacts_check_master.jsonl / contacts_check_base385.jsonl / contacts_check_rest.jsonl: a
"self-intersections" row whose contact check (same id, op and reason) has verdict "contacts" counts as contacts."""
import collections, json, os, sys

d = sys.argv[1]


def rows(name):
    p = os.path.join(d, name)
    if not os.path.exists(p):
        return None
    return [json.loads(l) for l in open(p) if l.strip().startswith('{')]


def overlay(base, *more):
    out = collections.OrderedDict()
    for r in base:
        out[(r['id'], r.get('op'))] = r
    for m in more:
        for r in m or []:
            if r.get('op') is None:
                for k in [k for k in out if k[0] == r['id']]:
                    del out[k]
            out[(r['id'], r.get('op'))] = r
    return list(out.values())


def checks(*names):
    c = {}
    for n in names:
        for r in rows(n) or []:
            if 'op' in r and r.get('reason') == 'self-intersections':
                c[(r['id'], r['op'])] = r.get('verdict', 'self-intersections')
    return c


def verdict(r, chk):
    if r.get('class') == 'unavailable' or (r.get('op') is None and 'error' in r):
        return 'unavailable' if r.get('class') == 'unavailable' else 'failed'
    if 'error' in r or 'exception' in r or r.get('ok') is False or r.get('status') not in (None, 'Success'):
        return 'failed'
    v = r.get('reason')
    if v == 'self-intersections':
        v = r.get('verdict') or chk.get((r['id'], r['op']), 'self-intersections (not checked)')
    return {'': 'clean', 'empty': 'empty', 'contacts': 'contacts', None: 'no verdict'}.get(v, 'unclean: ' + str(v))


phases = []
t = rows('target.jsonl')
if t:
    phases.append(('target (master 79a2c1ab, with the #474 / #483 re-runs)',
                   overlay(t, rows('target_rerun_pr474.jsonl'), rows('target_rerun_pr483.jsonl')),
                   checks('contacts_check_master.jsonl')))
c = rows('control.jsonl')
if c:
    phases.append(('control (#385 baseline cd7fb8ab)', c, checks('contacts_check_base385.jsonl')))
r = rows('rest.jsonl')
if r:
    phases.append(('rest (master 79a2c1ab)', overlay(r, rows('rest_rerun_429.jsonl')), checks('contacts_check_rest.jsonl')))

print('# Exact Boolean 2.0 sign-off tally\n')
print('Per operation (union, intersection, difference of each model with its #385 pairing). "contacts": NotCleanReason')
print('says self-intersections, but every pair only touches where the solids touch (duplicated vertices; the contact')
print('check of FinalRunner); counted apart from unclean (owner 2026-11-10). Unavailable: models whose export failed.\n')
cols = ['clean', 'contacts', 'empty', 'unclean', 'failed']
print('| phase | class | models | ops | ' + ' | '.join(cols) + ' | unavailable models |')
print('|---|---|---|---|' + '---|' * len(cols) + '---|')
details = []
for name, rs, chk in phases:
    by = collections.defaultdict(list)
    for x in rs:
        by[x.get('class', '?')].append(x)
    for cls in sorted(by, key=lambda k: (k == 'unavailable', k)):
        xs = by[cls]
        if cls == 'unavailable':
            continue
        v = collections.Counter(verdict(x, chk) for x in xs)
        unclean = sum(n for k, n in v.items() if k.startswith('unclean'))
        print(f"| {name} | {cls} | {len({x['id'] for x in xs})} | {len(xs)} | {v['clean']} | {v['contacts']} | {v['empty']} | "
              f"{unclean} | {v['failed'] + v['no verdict']} | |")
        reasons = collections.Counter(k[9:] for k in v.elements() if k.startswith('unclean'))
        if reasons:
            details.append(f"- {name}, {cls}: unclean by reason: " + ', '.join(f'{k} {n}' for k, n in reasons.most_common()))
        failed = sorted({x['id'] for x in xs if verdict(x, chk) == 'failed'})
        if failed:
            details.append(f"- {name}, {cls}: failed: " + ', '.join(failed))
    un = sorted({x['id'] for x in by.get('unavailable', [])}, key=lambda s: int(s) if s.isdigit() else 0)
    if un:
        print(f"| {name} | unavailable | {len(un)} | | | | | | | {', '.join(un)} |")
print()
print('\n'.join(details))
