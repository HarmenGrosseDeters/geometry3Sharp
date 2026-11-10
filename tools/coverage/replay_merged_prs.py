#!/usr/bin/env python3
"""Replay the changed-line coverage gate on the last merged pull requests (an impact measurement, not a gate).

With the coverage reports of the checked-out HEAD (--coverage-dir layout of changed_line_coverage.py), for each recent
merged PR that changed library C# files: the lines that the PR added and that are still in HEAD (git blame), and how many of
them the reports do not hit. A line later rewritten is not counted; the result is a lower bound of what the gate would have
flagged at merge time.

Usage: python3 tools/coverage/replay_merged_prs.py <out.json> [--coverage-dir test-results]
"""
import sys, subprocess, re, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import changed_line_coverage as c
root = Path(c.git(Path.cwd(), 'rev-parse', '--show-toplevel').strip()).resolve()
def git(*a): return c.git(root, *a)
cov_dir = Path(sys.argv[sys.argv.index('--coverage-dir') + 1]) if '--coverage-dir' in sys.argv else root / 'test-results'
cov, _ = c.read_coverage(cov_dir, root)
merges = git('log', '--first-parent', '--merges', '-n', '120', '--format=%H\t%s', 'origin/master').strip().split('\n')
results = []
for line in merges:
    h, subj = line.split('\t', 1)
    m = re.match(r'Merge pull request #(\d+)', subj)
    if not m: continue
    try:
        parents = git('rev-list', '--parents', '-n', '1', h).split()
    except Exception: continue
    if len(parents) != 3: continue
    base, tip = parents[1], parents[2]
    ancestor, changes = c.changed_lines(root, base, tip)
    if not changes: continue
    commits = set(git('rev-list', f'{ancestor}..{tip}').split())
    surv = {}
    for path, lines in changes.items():
        try:
            blame = git('blame', '--porcelain', 'HEAD', '--', path)  # note: HEAD of coverage = checked-out tree
        except Exception: continue
        cur = None; mine = set()
        for bl in blame.split('\n'):
            mm = re.match(r'^([0-9a-f]{40}) \d+ (\d+)', bl)
            if mm:
                if mm.group(1) in commits: mine.add(int(mm.group(2)))
        if mine: surv[path] = mine
    if not surv: continue
    inscope = {p: l for p, l in surv.items() if p in cov or True}
    ev = c.evaluate(inscope, cov)
    results.append({'pr': m.group(1), 'title': subj[:60], 'files': len(surv), 'executable_surviving': ev['changed_executable_lines'], 'uncovered': {k: v[:12] for k, v in ev['uncovered'].items()}, 'n_uncovered': sum(len(v) for v in ev['uncovered'].values()), 'missing_files': ev['missing_files']})
    if len(results) >= 18: break
out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('replay.json')
out.write_text(json.dumps(results, indent=1))
print('| PR | executable lines still in HEAD | uncovered | missing from reports | files with uncovered lines (first lines) |')
print('|---|---|---|---|---|')
for r in results:
    print('| #%s | %d | %d | %s | %s |' % (r['pr'], r['executable_surviving'], r['n_uncovered'], ', '.join(r['missing_files'][:3]), '; '.join('%s: %s' % (k, v[:6]) for k, v in list(r['uncovered'].items())[:4])))
