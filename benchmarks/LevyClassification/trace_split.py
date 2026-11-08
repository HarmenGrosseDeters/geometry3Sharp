#!/usr/bin/env python3
"""Split the classification phase of a sampled CPU trace into its parts.

usage: python3 trace_split.py <trace.speedscope.json>...
The trace comes from `dotnet-trace collect --profile cpu-sampling --format speedscope -- dotnet LevyClassification.dll
<case> --ops <op>` (trace.sh). A sample belongs to the classification if a frame of ExactMeshBoolean.classify (or one of
its compiler-generated lambdas, run on pool threads) is on its stack; it is attributed to the first of these parts found
from the leaf up (CPU samples on all threads, so parallel parts count with their CPU time, not wall time):
  rays         front_winding (exact ray per patch and closed candidate mesh)
  gwn          front_generalized_winding / winding_tree (open meshes)
  stack        coplanar_stack (incl. its centroid point)
  patch_setup  patch_winding minus the above (centroid, candidate query)
  rest         union-find, edge grouping, the per-piece pass
Prints one JSON line per trace."""
import json, sys

PARTS = [('rays', ('front_winding',)), ('gwn', ('front_generalized_winding', 'winding_tree')),
         ('stack', ('coplanar_stack',)), ('patch_setup', ('patch_winding',))]


def split(path):
    d = json.load(open(path))
    names = [f['name'] for f in d['shared']['frames']]
    is_cls = [('ExactMeshBoolean' in n and 'classify' in n) for n in names]
    part_of = []
    for n in names:
        p = None
        if 'ExactMeshBoolean' in n:
            for key, pats in PARTS:
                if any(('.' + s + '(') in n or ('::' + s) in n or n.endswith('.' + s) or ('.' + s + ' ') in n for s in pats):
                    p = key
                    break
        part_of.append(p)
    tot = {k: 0.0 for k, _ in PARTS}
    tot['rest'] = 0.0
    allw = 0.0
    for prof in d['profiles']:
        if prof.get('type') != 'sampled':
            continue
        for stack, w in zip(prof['samples'], prof['weights']):
            allw += w
            if not any(is_cls[f] for f in stack) and not any(part_of[f] for f in stack):
                continue
            part = 'rest'
            for f in reversed(stack):
                if part_of[f]:
                    part = part_of[f]
                    break
            tot[part] += w
    cls = sum(tot.values())
    return {'trace': path, 'cpu_all': round(allw, 1), 'cpu_classification': round(cls, 1),
            **{k: round(v / cls, 3) if cls else 0 for k, v in tot.items()}}


if __name__ == '__main__':
    for p in sys.argv[1:]:
        print(json.dumps(split(p)))
