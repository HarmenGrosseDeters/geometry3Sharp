#!/usr/bin/env python3
"""Summary tables of DualContouringIntersections runs.

usage: python3 analyze.py <out.jsonl>... [--per-input]
Groups by input family (mesh file / mesh:<solid> / imp:<shape> without the @seed) and resolution."""
import collections, json, sys

CLASSES = ['envelope_in', 'envelope_out', 'multipatch', 'transition', 'degenerate', 'unknown']


def family(d):
    n = d['input']
    if n.startswith('imp:'):
        return n.split('@')[0]
    if n.startswith('mesh:'):
        return 'mesh:procedural'
    return 'T1'


def main():
    files = [a for a in sys.argv[1:] if not a.startswith('--')]
    per_input = '--per-input' in sys.argv
    rows = [json.loads(l) for f in files for l in open(f)]
    groups = collections.OrderedDict()
    for d in sorted(rows, key=lambda d: (family(d), d['res'])):
        key = (d['input'] if per_input else family(d), d['res'])
        groups.setdefault(key, []).append(d)
    print('| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) '
          '| moved / vertices | multi-patch polys | 1 / 2 concave | SI triangles in / out / multi-patch / transition | covered pairs '
          '| check+repair share of DC time |')
    print('|' + '---|' * 14)
    for (name, res), g in groups.items():
        s = lambda k: sum(d.get(k, 0) for d in g)
        n = lambda pred: sum(1 for d in g if pred(d))
        polys = max(s('polys'), 1)
        dcms = sum(v for d in g for k, v in d.items() if k.startswith('dc_ms_'))
        cls = ' / '.join(str(s('si_tris_' + c)) for c in CLASSES[:4])
        print(f"| {name} | {res} | {len(g)} | {n(lambda d: d.get('si_found', 0) > 0)} | {s('si_found')} | {n(lambda d: d.get('si_left', 0) > 0)} "
              f"| {n(lambda d: d.get('final_si', 0) > 0)} | {s('nonmanifold_tris')} ({n(lambda d: d.get('nonmanifold_tris', 0) > 0)}) "
              f"| {s('resolve_moved')} / {s('verts')} | {100 * s('multipatch_polys') / polys:.2f} % "
              f"| {100 * s('concave1') / polys:.2f} % / {100 * s('concave2') / polys:.3f} % | {cls} | {s('covered_pairs')} "
              f"| {100 * s('dc_ms_intersections') / max(dcms, 1):.0f} % |")
    bad = [d for d in rows if d.get('status') not in ('ok',)]
    if bad:
        print('\nnot ok:', [(d['input'], d['res'], d.get('status'), d.get('error')) for d in bad])
    notin = sum(d.get('verts_not_in_cell', 0) for d in rows)
    print(f"\nvertices not strictly inside their cell: {notin} of {sum(d.get('verts', 0) for d in rows)}; "
          f"quads with 3 or 4 concave vertices: {sum(d.get('concave3', 0) + d.get('concave4', 0) for d in rows)}")


if __name__ == '__main__':
    main()
