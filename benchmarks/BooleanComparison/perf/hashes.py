#!/usr/bin/env python3
"""Compare the result hashes of two profile outputs (exact_hashes.sh): python3 hashes.py before.jsonl after.jsonl"""
import json, sys

def load(f):
    r = {}
    for l in open(f):
        if l.startswith('{'):
            d = json.loads(l)
            r[(d['case'], d['op'])] = (d.get('hash'), d.get('status'), d.get('triangles'))
    return r

a, b = load(sys.argv[1]), load(sys.argv[2])
diff = [k for k in a if a[k] != b.get(k)] + [k for k in b if k not in a]
print(len(a), 'runs before,', len(b), 'after,', len(diff), 'differ')
for k in diff[:20]:
    print(k, a.get(k), b.get(k))
sys.exit(1 if diff else 0)
